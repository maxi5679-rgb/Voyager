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
