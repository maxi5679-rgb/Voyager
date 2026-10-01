using System.Text.RegularExpressions;

namespace Voyager;

internal static partial class UrlHelper
{
    [GeneratedRegex(@"^[\w.-]+\.[a-z]{2,}([/:?#].*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex BareHostRegex();

    /// <summary>
    /// 入力欄の文字列を URL として解釈する。URL でなければ null（＝AI への質問として扱う）。
    /// 栞 Chromium 版の parseUrl() を移植。
    /// </summary>
    public static string? Parse(string raw)
    {
        var t = raw.Trim();
        if (t.Length == 0) return null;

        switch (t.ToLowerInvariant())
        {
            case "google" or "ぐぐる" or "グーグル": return "https://www.google.co.jp/";
            case "yahoo" or "ヤフー" or "やふー": return "https://www.yahoo.co.jp/";
            case "youtube" or "ようつべ" or "ユーチューブ": return "https://www.youtube.com/";
        }

        try
        {
            if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                t.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return new Uri(t).ToString();

            if (!t.Contains(' ') && BareHostRegex().IsMatch(t))
                return new Uri("https://" + t).ToString();
        }
        catch (UriFormatException)
        {
            return null;
        }

        return null;
    }

    /// <summary>実際に表示してよい URL か（http/https のみ許可）。</summary>
    /// <summary>
    /// 広告やメールのリンクに付く追跡用の引数。名前で決め打ちする。
    /// 「si」「ref」のように、サイトによっては本当に意味のある名前は入れない。
    /// </summary>
    private static readonly HashSet<string> TrackingNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "fbclid", "gclid", "dclid", "gbraid", "wbraid", "msclkid", "yclid", "twclid", "ttclid",
        "igshid", "igsh", "mc_cid", "mc_eid", "_hsenc", "_hsmi", "mkt_tok", "_ga", "_gl",
    };

    /// <summary>
    /// アドレス欄に見せるためだけに、追跡用の引数（utm_*、fbclid など）を落とす。
    /// 開くアドレスそのものは変えない。ほかの引数と # 以降は、元の綴りと順番のまま残す。
    /// 落とすものが無ければ、渡されたものをそのまま返す。
    /// </summary>
    public static string WithoutTracking(string url)
    {
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return url;
        var q = url.IndexOf('?');
        if (q < 0) return url;
        var hash = url.IndexOf('#');
        if (hash >= 0 && hash < q) return url;   // ? は # の後ろ（ページ内の目印の一部）

        var query = hash < 0 ? url[(q + 1)..] : url[(q + 1)..hash];
        var fragment = hash < 0 ? "" : url[hash..];
        var pairs = query.Split('&');
        var kept = pairs.Where(p => !IsTracking(p)).ToList();
        if (kept.Count == pairs.Length) return url;

        var rest = string.Join("&", kept.Where(p => p.Length > 0));
        return url[..q] + (rest.Length > 0 ? "?" + rest : "") + fragment;
    }

    private static bool IsTracking(string pair)
    {
        var eq = pair.IndexOf('=');
        var raw = eq < 0 ? pair : pair[..eq];
        string name;
        try { name = Uri.UnescapeDataString(raw); } catch (UriFormatException) { name = raw; }
        return name.StartsWith("utm_", StringComparison.OrdinalIgnoreCase) || TrackingNames.Contains(name);
    }

    public static bool IsNavigable(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) &&
        (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);

    /// <summary>タブに出す短い見出し。</summary>
    public static string HostTitle(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return url ?? "";
        var host = u.Host;
        return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
    }
}
