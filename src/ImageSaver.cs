using System.Diagnostics.CodeAnalysis;
using System.Net;
using Microsoft.Web.WebView2.Core;

namespace Voyager;

/// <summary>
/// 「画像を保存」を Chromium に頼まず、自分で画像を取ってくる。
///
/// Chromium の saveImageAs は、押した瞬間に既定の保存先へ .tmp を書き始めるのに、
/// DownloadStarting がこちらへ届くのがページを動かすまで遅れることがある
/// （petal-online の staff-start 画像で 28 秒）。保存先を訊くのはその知らせを受けてからなので、
/// 利用者には「押しても何も起きない」に見える。原因は Chromium の中で、こちらからは直せない。
///
/// そこで URL が分かる画像は、ページと同じ Cookie・Referer・User-Agent を付けて自分で取る。
/// blob: と、URL が分からない画像（iframe の中など）だけは従来どおり Chromium に任せる。
/// </summary>
internal static class ImageSaver
{
    private static readonly HttpClient Http = new(new HttpClientHandler
    {
        UseCookies = false,   // Cookie は WebView2 のものを毎回付ける
        AutomaticDecompression = DecompressionMethods.All,
        AllowAutoRedirect = true,
    })
    {
        Timeout = TimeSpan.FromSeconds(60),
    };

    /// <summary>自分で取れる URL か。http / https / data だけ。</summary>
    public static bool CanFetch([NotNullWhen(true)] string? url) =>
        url is not null &&
        (url.StartsWith("https:", StringComparison.OrdinalIgnoreCase) ||
         url.StartsWith("http:", StringComparison.OrdinalIgnoreCase) ||
         url.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase));

    public sealed record Result(byte[] Bytes, string? MediaType);

    /// <summary>
    /// 画像を取ってくる。失敗は例外で返す（HttpRequestException など）。
    /// data: はその場で解く。
    /// </summary>
    public static async Task<Result> FetchAsync(CoreWebView2 core, string url, string? referer)
    {
        if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return DecodeDataUrl(url);

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", core.Settings.UserAgent);
        req.Headers.TryAddWithoutValidation("Accept", "image/avif,image/webp,image/apng,image/*,*/*;q=0.8");
        if (referer is not null &&
            (referer.StartsWith("https:", StringComparison.OrdinalIgnoreCase) ||
             referer.StartsWith("http:", StringComparison.OrdinalIgnoreCase)))
            req.Headers.Referrer = new Uri(referer);

        // HttpOnly も含めて WebView2 が持っている Cookie をそのまま付ける。
        // ログインしないと見えない画像は、これが無いと 403 か別の画像になる。
        var cookies = await core.CookieManager.GetCookiesAsync(url);
        if (cookies.Count > 0)
            req.Headers.TryAddWithoutValidation("Cookie",
                string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}")));

        using var res = await Http.SendAsync(req);
        res.EnsureSuccessStatusCode();
        var bytes = await res.Content.ReadAsByteArrayAsync();
        return new Result(bytes, res.Content.Headers.ContentType?.MediaType);
    }

    private static Result DecodeDataUrl(string url)
    {
        // data:[<mediatype>][;base64],<data>
        var comma = url.IndexOf(',');
        if (comma < 0) throw new FormatException("data URL without a comma");
        var head = url[5..comma];
        var body = url[(comma + 1)..];
        var parts = head.Split(';');
        var media = parts[0].Length > 0 ? parts[0] : null;
        var bytes = parts.Contains("base64", StringComparer.OrdinalIgnoreCase)
            ? Convert.FromBase64String(Uri.UnescapeDataString(body))
            : System.Text.Encoding.UTF8.GetBytes(Uri.UnescapeDataString(body));
        return new Result(bytes, media);
    }

    private static readonly Dictionary<string, string> ExtByType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = "jpg", ["image/jpg"] = "jpg", ["image/pjpeg"] = "jpg",
        ["image/png"] = "png", ["image/gif"] = "gif", ["image/webp"] = "webp",
        ["image/avif"] = "avif", ["image/svg+xml"] = "svg", ["image/bmp"] = "bmp",
        ["image/x-icon"] = "ico", ["image/vnd.microsoft.icon"] = "ico", ["image/tiff"] = "tif",
    };

    private static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase)
    {
        "jpg", "jpeg", "jfif", "png", "gif", "webp", "avif", "svg", "bmp", "ico", "tif", "tiff",
    };

    public static string? ExtFor(string? mediaType) =>
        mediaType is not null && ExtByType.TryGetValue(mediaType, out var e) ? e : null;

    /// <summary>
    /// 取れた画像の拡張子。Content-Type を先に見て、無い・知らない種類なら中身の先頭で判別する。
    /// static.staff-start.com のように Content-Type を返さないサーバーがある。
    /// </summary>
    public static string? ExtOf(Result r) => ExtFor(r.MediaType) ?? ExtFromBytes(r.Bytes);

    /// <summary>ファイルの先頭数バイト（マジックナンバー）から画像の種類を当てる。分からなければ null。</summary>
    public static string? ExtFromBytes(byte[] b)
    {
        bool At(int offset, params byte[] sig) =>
            b.Length >= offset + sig.Length && b.AsSpan(offset, sig.Length).SequenceEqual(sig);
        bool Ascii(int offset, string sig) =>
            At(offset, System.Text.Encoding.ASCII.GetBytes(sig));

        if (At(0, 0xFF, 0xD8, 0xFF)) return "jpg";
        if (At(0, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)) return "png";
        if (Ascii(0, "GIF87a") || Ascii(0, "GIF89a")) return "gif";
        if (Ascii(0, "RIFF") && Ascii(8, "WEBP")) return "webp";
        if (Ascii(4, "ftypavif") || Ascii(4, "ftypavis")) return "avif";
        if (Ascii(0, "BM")) return "bmp";
        if (At(0, 0x00, 0x00, 0x01, 0x00)) return "ico";
        if (At(0, 0x49, 0x49, 0x2A, 0x00) || At(0, 0x4D, 0x4D, 0x00, 0x2A)) return "tif";

        // SVG は文字。先頭の少しだけ見る。
        var head = System.Text.Encoding.UTF8.GetString(b, 0, Math.Min(b.Length, 512)).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        if (head.StartsWith("<svg", StringComparison.OrdinalIgnoreCase) ||
            (head.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) && head.Contains("<svg", StringComparison.OrdinalIgnoreCase)))
            return "svg";
        return null;
    }

    /// <summary>
    /// URL から保存名の元を作る。拡張子が画像のものなら拡張子付きで、そうでなければ拡張子なしで返す。
    /// hasExt が false のときは、呼び出し側が Content-Type から拡張子を足す。
    /// </summary>
    public static string NameFromUrl(string url, out bool hasExt)
    {
        hasExt = false;
        if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return "image";

        var name = "";
        if (Uri.TryCreate(url, UriKind.Absolute, out var u))
            name = Uri.UnescapeDataString(u.Segments.LastOrDefault() ?? "").Trim('/');

        name = Sanitize(name);
        if (name.Length == 0) return "image";

        var ext = Path.GetExtension(name).TrimStart('.');
        if (ImageExts.Contains(ext))
        {
            hasExt = true;
            return name;
        }
        // 「photo.php」のような画像でない拡張子は落とす。
        return ext.Length > 0 && ext.Length <= 5 ? Path.GetFileNameWithoutExtension(name) : name;
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        name = name.Trim().TrimEnd('.');
        return name.Length > 150 ? name[..150] : name;
    }
}
