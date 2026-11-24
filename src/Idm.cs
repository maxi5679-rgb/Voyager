using System.Diagnostics;
using Microsoft.Win32;

namespace Voyager;

/// <summary>
/// Internet Download Manager（IDM）への受け渡し。
///
/// IDM の拡張機能は、ネイティブメッセージングで IDM 本体と話す。WebView2 の拡張機能はそれに
/// 対応していないので、拡張機能は使わない。代わりに、ダウンロードが始まったら
/// その URL を IDMan.exe /d に渡し、Voyager 側のダウンロードは取りやめる。
///
/// ストア版（MSIX）では使わない。IDM は別売りのソフトで、ストアの審査に余計な説明が要るため。
/// 自分の PC で build.cmd から入れる版（MSI）で、IDM が入っているときだけ設定に出る。
///
/// 制限: URL しか渡せないので、ログインが要るダウンロード（Cookie が要るもの）は
/// IDM 側で失敗することがある。そのときは設定で切れば、これまでどおり Voyager が落とす。
/// </summary>
internal static class Idm
{
    /// <summary>IDMan.exe の場所。ストア版、または IDM が入っていなければ null。</summary>
    public static string? ExePath { get; } = Find();

    public static bool Available => ExePath is not null;

    /// <summary>URL を IDM に渡す。渡せたら true。</summary>
    public static bool Send(string url)
    {
        if (ExePath is null) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) ||
            (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps))
            return false;   // blob: や data: はページの中にしか無いので、IDM には取りに行けない

        try
        {
            var psi = new ProcessStartInfo(ExePath) { UseShellExecute = false };
            psi.ArgumentList.Add("/d");
            psi.ArgumentList.Add(url);
            Process.Start(psi)?.Dispose();
            return true;
        }
        catch (Exception ex)
        {
            Log.Write($"!! idm: start failed: {ex.GetType().Name} {ex.Message}");
            return false;
        }
    }

    private static string? Find()
    {
        if (AppPaths.IsPackaged) return null;
        try
        {
            // IDM は自分の場所をここに書いておく
            if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\DownloadManager", "ExePath", null) is string reg &&
                File.Exists(reg))
                return reg;

            foreach (var root in new[]
                     {
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     })
            {
                if (string.IsNullOrEmpty(root)) continue;
                var p = Path.Combine(root, "Internet Download Manager", "IDMan.exe");
                if (File.Exists(p)) return p;
            }
        }
        catch (Exception ex)
        {
            Log.Write($"idm: lookup failed: {ex.GetType().Name} {ex.Message}");
        }
        return null;
    }
}
