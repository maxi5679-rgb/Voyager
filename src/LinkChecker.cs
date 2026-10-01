using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Voyager;

/// <summary>
/// ブックマークのリンク切れを、裏で 1 件ずつゆっくり確かめる。
///
/// 相手のサイトに迷惑をかけないよう、同時に 2 件まで、同じサイトへは間を空けて 1 件ずつ。
/// Cookie は付けない（ログイン状態を相手に送らない）。中身は読まず、返事の頭だけ見て切る。
///
/// 404 / 410、ドメインが無い、つながらない、だけを「切れている」とする。
/// 403 や 5xx、時間切れはボット対策でも起きるので「確認できなかった」に分ける。
///
/// 途中でこちらのネットが切れると、全部が「ドメインが無い」「つながらない」になってしまう。
/// そういう失敗が続いたら、さっき開けたサイトにもう一度つないでみて、それも駄目なら
/// 続いた分を捨てて止める（NetworkLost）。
/// </summary>
internal sealed class LinkChecker
{
    public enum Kind { Ok, Dead, Unsure }
    public enum Reason { None, NotFound, NoHost, Refused, Denied, TooMany, Server, Timeout, Tls, Other }
    public sealed record Result(Kind Kind, Reason Reason, int Status);

    private const int Workers = 2;
    private const int SuspiciousStreak = 15;
    private static readonly TimeSpan SameHostGap = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        UseCookies = false,
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 8,
        AutomaticDecompression = DecompressionMethods.All,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    })
    {
        Timeout = Timeout.InfiniteTimeSpan,   // 1 件ごとの時間切れは自分で付ける
    };

    private readonly List<(string Id, string Url)> _queue;
    private readonly string _userAgent;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _lock = new();
    private readonly HashSet<string> _busyHosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _lastHit = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stopwatch _clock = new();
    private int _done;
    private readonly List<string> _streak = [];   // 続けて「つながらない」系だった id
    private string? _lastOkUrl;
    private bool _probing;

    public ConcurrentDictionary<string, Result> Results { get; } = new();
    public int Total => _queue.Count;
    public int Done => Volatile.Read(ref _done);
    public bool Running { get; private set; }
    public bool Stopped { get; private set; }
    public bool NetworkLost { get; private set; }
    public string ScopeId { get; }
    public TimeSpan Elapsed => _clock.Elapsed;

    /// <summary>1 件済むごと（裏のスレッドから）。</summary>
    public event Action<string, Result>? Checked;

    /// <summary>全部済んだか、止めたとき（裏のスレッドから）。</summary>
    public event Action? Finished;

    public LinkChecker(string scopeId, IEnumerable<(string Id, string Url)> links, string userAgent)
    {
        ScopeId = scopeId;
        _userAgent = userAgent;
        _queue = links.Where(l => Uri.TryCreate(l.Url, UriKind.Absolute, out var u) && u.Scheme is "http" or "https").ToList();
    }

    public int Count(Kind kind) => Results.Values.Count(r => r.Kind == kind);

    public void Start()
    {
        Running = true;
        _clock.Start();
        var next = 0;
        var tasks = Enumerable.Range(0, Workers).Select(_ => Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                var item = await TakeAsync(() => next, v => next = v);
                if (item is null) return;
                var (id, url, host) = item.Value;
                Result result;
                try { result = await CheckAsync(url); }
                finally
                {
                    lock (_lock) { _busyHosts.Remove(host); _lastHit[host] = DateTime.UtcNow; }
                }
                if (_cts.IsCancellationRequested) return;
                Results[id] = result;
                Interlocked.Increment(ref _done);
                if (await NetworkLooksDownAsync(id, url, result)) return;
                Checked?.Invoke(id, result);
            }
        })).ToArray();

        _ = Task.WhenAll(tasks).ContinueWith(_ =>
        {
            Running = false;
            _clock.Stop();
            Finished?.Invoke();
        }, TaskScheduler.Default);
    }

    /// <summary>
    /// ネットそのものが切れていないか。つながらない系の失敗が続いたら、さっき開けたサイトで確かめる。
    /// 切れていたら、続いた分の結果を捨てて止め、true を返す。
    /// </summary>
    private async Task<bool> NetworkLooksDownAsync(string id, string url, Result result)
    {
        var netFail = result.Status == 0 && result.Reason is Reason.NoHost or Reason.Refused or Reason.Timeout;
        string? probe;
        lock (_lock)
        {
            if (NetworkLost)
            {
                // もう止めると決まったあとに返ってきた分も捨てる。
                if (Results.TryRemove(id, out _)) Interlocked.Decrement(ref _done);
                return true;
            }
            if (!netFail)
            {
                _streak.Clear();
                if (result.Kind == Kind.Ok) _lastOkUrl = url;
                return false;
            }
            _streak.Add(id);
            if (_streak.Count < SuspiciousStreak || _probing) return false;
            _probing = true;
            probe = _lastOkUrl;
        }

        var online = probe is not null && (await CheckAsync(probe)).Status != 0;
        lock (_lock)
        {
            _probing = false;
            if (online) { _streak.Clear(); return false; }
            Log.Write($"link check: {_streak.Count} network failures in a row and the probe failed; stopping");
            foreach (var s in _streak)
                if (Results.TryRemove(s, out _)) Interlocked.Decrement(ref _done);
            _streak.Clear();
            NetworkLost = true;
        }
        Stop();
        return true;
    }

    public void Stop()
    {
        if (!Running) return;
        Stopped = true;
        _cts.Cancel();
    }

    /// <summary>
    /// 次に確かめるものを取る。使っていないサイトで、前回から間が空いたものを先頭から探す。
    /// 全部が待ちなら少し寝て探し直す。もう無ければ null。
    /// </summary>
    private async Task<(string Id, string Url, string Host)?> TakeAsync(Func<int> getNext, Action<int> setNext)
    {
        while (!_cts.IsCancellationRequested)
        {
            lock (_lock)
            {
                var start = getNext();
                if (start >= _queue.Count) return null;
                var now = DateTime.UtcNow;
                // 先頭から少しだけ見る。同じサイトが続く並び（取り込んだフォルダなど）でも詰まらないように。
                for (var i = start; i < Math.Min(_queue.Count, start + 200); i++)
                {
                    var (id, url) = _queue[i];
                    if (id is null) continue;   // 先に取ったもの
                    var host = new Uri(url).Host;
                    if (_busyHosts.Contains(host)) continue;
                    if (_lastHit.TryGetValue(host, out var last) && now - last < SameHostGap) continue;
                    _busyHosts.Add(host);
                    _queue[i] = (null!, url);
                    if (i == start)
                    {
                        var n = start + 1;
                        while (n < _queue.Count && _queue[n].Id is null) n++;
                        setNext(n);
                    }
                    return (id, url, host);
                }
            }
            try { await Task.Delay(200, _cts.Token); } catch (OperationCanceledException) { return null; }
        }
        return null;
    }

    private async Task<Result> CheckAsync(string url)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", _userAgent);
            req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,*/*;q=0.8");
            // 中身は読まない。頭が来たら切る。
            using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            return Classify((int)res.StatusCode);
        }
        catch (OperationCanceledException) when (!_cts.IsCancellationRequested)
        {
            return new Result(Kind.Unsure, Reason.Timeout, 0);
        }
        catch (OperationCanceledException)
        {
            return new Result(Kind.Unsure, Reason.Other, 0);   // 止めた。数えない
        }
        catch (HttpRequestException ex)
        {
            return ex.HttpRequestError switch
            {
                HttpRequestError.NameResolutionError => new Result(Kind.Dead, Reason.NoHost, 0),
                // 断られた（相手のサーバーが無い）ときだけ「切れている」。ほかのつながらなさは分からない。
                HttpRequestError.ConnectionError when ex.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionRefused }
                    => new Result(Kind.Dead, Reason.Refused, 0),
                HttpRequestError.ConnectionError => new Result(Kind.Unsure, Reason.Refused, 0),
                HttpRequestError.SecureConnectionError => new Result(Kind.Unsure, Reason.Tls, 0),
                _ => new Result(Kind.Unsure, Reason.Other, 0),
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or UriFormatException or NotSupportedException)
        {
            return new Result(Kind.Unsure, Reason.Other, 0);
        }
    }

    private static Result Classify(int status) => status switch
    {
        < 400 => new Result(Kind.Ok, Reason.None, status),
        404 or 410 => new Result(Kind.Dead, Reason.NotFound, status),
        401 or 403 => new Result(Kind.Unsure, Reason.Denied, status),
        429 => new Result(Kind.Unsure, Reason.TooMany, status),
        >= 500 => new Result(Kind.Unsure, Reason.Server, status),
        _ => new Result(Kind.Unsure, Reason.Other, status),
    };
}
