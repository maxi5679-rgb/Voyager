namespace Voyager;

/// <summary>
/// 起動時の診断ログ。環境変数 VOYAGER_DEBUG が設定されているか、
/// debug.on がある場合だけ書き出す。
///
/// debug.on とログはアプリのデータフォルダ（MSI 版は %LOCALAPPDATA%\Voyager、
/// MSIX 版はパッケージの LocalState。AppPaths.Local）に置く。
/// インストール先に置くと、MSI が自分の管理外のファイルを消せないため、
/// アンインストール後も Programs\Voyager フォルダが残ってしまう。
///
/// debug.on の 1 行目に既存のフォルダが書いてあれば、ログはそこへ出す。
/// 開発中に作業フォルダへログを集めるための逃げ道で、配布版では使わない。
/// </summary>
internal static class Log
{
    /// <summary>
    /// ログに URL を残すときは必ずこれを通す。クエリ文字列には検索語や
    /// OAuth の state / code_challenge がそのまま入るので、ログには出さない。
    /// 不具合を追うのに要るのは「どのページか」までで、その先は要らない。
    ///
    /// パスも畳む。画像 CDN には 400 文字級の使い捨て鍵をパスに埋めてくるものがあり
    /// （Yahoo! の msp.c.yimg.jp など）、クエリだけ伏せても素通りしてしまう。
    /// 読めないほど長い行になるうえ、その鍵だけで画像を取れてしまう。
    /// </summary>
    private const int PathMax = 64;

    public static string Url(string? url)
    {
        if (string.IsNullOrEmpty(url)) return "-";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return "(非URL)";

        var path = u.AbsolutePath;
        if (path.Length > PathMax)
        {
            // 末尾（たいていファイル名）だけ残す。それも長ければ丸ごと畳む。
            var tail = path[(path.LastIndexOf('/') + 1)..];
            path = tail.Length is > 0 and <= PathMax ? $"/…/{tail}" : "/…";
        }

        var s = u.GetLeftPart(UriPartial.Authority) + path;
        if (!string.IsNullOrEmpty(u.Query)) s += "?…";
        if (!string.IsNullOrEmpty(u.Fragment)) s += "#…";
        return s;
    }


    private static readonly object Gate = new();
    private static readonly string? Path_;

    static Log()
    {
        try
        {
            var dataDir = AppPaths.Local;

            string? marker = null;
            foreach (var candidate in new[]
                     {
                         System.IO.Path.Combine(dataDir, "debug.on"),
                         System.IO.Path.Combine(AppContext.BaseDirectory, "debug.on"),
                     })
            {
                if (File.Exists(candidate)) { marker = candidate; break; }
            }

            var enabled = Environment.GetEnvironmentVariable("VOYAGER_DEBUG") is not null || marker is not null;
            if (!enabled) return;

            var dir = dataDir;
            if (marker is not null)
            {
                var first = File.ReadLines(marker).FirstOrDefault()?.Trim();
                if (!string.IsNullOrEmpty(first) && Directory.Exists(first)) dir = first;
            }

            Directory.CreateDirectory(dir);
            Path_ = System.IO.Path.Combine(dir, "voyager-debug.log");
            File.WriteAllText(Path_, $"=== Voyager {App.Version} debug {DateTime.Now:HH:mm:ss} ==={Environment.NewLine}");
        }
        catch (Exception)
        {
            Path_ = null;
        }
    }

    public static void Write(string message)
    {
        if (Path_ is null) return;
        try
        {
            lock (Gate)
                File.AppendAllText(Path_, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
