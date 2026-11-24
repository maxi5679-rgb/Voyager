using Microsoft.Web.WebView2.Core;

namespace Voyager;

/// <summary>
/// 広告ブロック。同梱の uBlock Origin Lite（extensions\ublock-lite）を WebView2 の拡張機能として読み込む。
///
/// 管理画面は作らない。設定の 1 か所でオン / オフするだけ。
///
/// WebView2 は拡張機能の中身をコピーせず、指定したフォルダをそのまま使う。
/// しかも入れた後にフォルダの中身が変わると、その拡張機能は外されてしまう。
/// インストール先（MSIX なら版ごとに場所が変わる）を直接指すと、更新のたびに外れて
/// 別の拡張機能として入り直すので、データフォルダの下に写してから、そちらを読み込む。
/// 写し直すのは、同梱の manifest.json が変わったとき（= uBlock Origin Lite の版が変わったとき）だけ。
///
/// 普段の起動でやるのは「入っている拡張機能の一覧を聞く」だけなので、起動はほとんど遅くならない。
/// </summary>
internal static class AdBlock
{
    private const string FolderName = "ublock-lite";

    /// <summary>同梱の原本（インストール先の extensions\ublock-lite）。</summary>
    private static string Source => Path.Combine(AppContext.BaseDirectory, "extensions", FolderName);

    /// <summary>WebView2 に読み込ませる写し。</summary>
    private static string Copy => Path.Combine(AppPaths.Local, "Extensions", FolderName);

    /// <summary>前回入れたときの拡張機能の Id。写しと同じ場所に置く。</summary>
    private static string IdFile => Path.Combine(AppPaths.Local, "Extensions", FolderName + ".id");

    /// <summary>
    /// 設定どおりにする。オンなら（必要なら写して）読み込み、オフなら無効にする。
    /// 失敗してもブラウザ自体は普通に動くので、ログに残すだけ。
    /// </summary>
    public static async Task ApplyAsync(CoreWebView2Profile profile, bool enabled)
    {
        var started = Environment.TickCount64;
        try
        {
            var installed = await profile.GetBrowserExtensionsAsync();
            var savedId = ReadId();
            var current = installed.FirstOrDefault(e => e.Id == savedId);

            if (!enabled)
            {
                if (current is { IsEnabled: true }) await current.EnableAsync(false);
                Log.Write($"adblock: off (installed={(current is not null ? "yes" : "no")})");
                return;
            }

            if (!File.Exists(Path.Combine(Source, "manifest.json")))
            {
                Log.Write($"adblock: bundled extension not found at {Source}");
                return;
            }

            // 写しが古い（または無い）なら写し直す。写しの中身が変わると WebView2 はその拡張機能を外すので、
            // その後は入れ直しになる。
            var refreshed = await Task.Run(RefreshCopy);

            if (current is null || refreshed)
            {
                current = await profile.AddBrowserExtensionAsync(Copy);
                WriteId(current.Id);
                Log.Write($"adblock: installed {current.Name} id={current.Id} refreshed={refreshed}");
            }
            else if (!current.IsEnabled)
            {
                await current.EnableAsync(true);
            }

            Log.Write($"adblock: on ({Environment.TickCount64 - started}ms)");
        }
        catch (Exception ex)
        {
            Log.Write($"!! adblock: {ex.GetType().Name} {ex.Message}");
        }
    }

    /// <summary>写しを同梱の原本に揃える。写し直したら true。</summary>
    private static bool RefreshCopy()
    {
        var srcManifest = Path.Combine(Source, "manifest.json");
        var dstManifest = Path.Combine(Copy, "manifest.json");
        if (File.Exists(dstManifest) &&
            File.ReadAllBytes(srcManifest).AsSpan().SequenceEqual(File.ReadAllBytes(dstManifest)))
            return false;

        if (Directory.Exists(Copy)) Directory.Delete(Copy, recursive: true);
        foreach (var file in Directory.EnumerateFiles(Source, "*", SearchOption.AllDirectories))
        {
            if (string.Equals(file, srcManifest, StringComparison.OrdinalIgnoreCase)) continue;
            var target = Path.Combine(Copy, Path.GetRelativePath(Source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
        // manifest.json は最後に置く。途中で止まっても、次の起動で「古い」と分かって写し直せる。
        File.Copy(srcManifest, dstManifest, overwrite: true);
        return true;
    }

    private static string? ReadId()
    {
        try { return File.Exists(IdFile) ? File.ReadAllText(IdFile).Trim() : null; }
        catch (IOException) { return null; }
    }

    private static void WriteId(string id)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(IdFile)!);
        File.WriteAllText(IdFile, id);
    }
}
