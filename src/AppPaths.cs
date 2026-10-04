using System.Runtime.InteropServices;

namespace Voyager;

/// <summary>
/// 設定・ブックマーク・Cookie・ログの置き場所。
///
/// MSI 版（普通のインストール）は従来どおり
///   設定とブックマーク  %APPDATA%\Voyager
///   Cookie とログイン   %LOCALAPPDATA%\Voyager\WebView2
///   デバッグログ        %LOCALAPPDATA%\Voyager
///
/// MSIX 版（Microsoft Store）は、Windows がそのアプリに用意する
///   %LOCALAPPDATA%\Packages\&lt;パッケージファミリー名&gt;\LocalState
/// の下にまとめる。MSIX のアプリが %LOCALAPPDATA%\Voyager を見に行くと、
/// 同じ PC に MSI 版があればその Cookie を読み書きしてしまう（既存のファイルは
/// その場で書き換えられる）。同時に起動すれば壊れるおそれもあるので、最初から分けておく。
/// このフォルダはアンインストールで消える。Store のアプリとして普通の振る舞い。
/// </summary>
internal static class AppPaths
{
    /// <summary>MSIX として動いているときのパッケージファミリー名。MSI 版なら null。</summary>
    public static string? PackageFamilyName { get; } = DetectPackage();

    public static bool IsPackaged => PackageFamilyName is not null;

    /// <summary>設定とブックマーク。</summary>
    public static string Settings { get; } = IsPackaged
        ? PackageLocalState()
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Voyager");

    /// <summary>デバッグログと debug.on。</summary>
    public static string Local { get; } = IsPackaged
        ? PackageLocalState()
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Voyager");

    /// <summary>WebView2 のデータ（Cookie・ログイン状態・閲覧履歴）。</summary>
    public static string WebView2 { get; } = Path.Combine(Local, "WebView2");

    private static string PackageLocalState() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Packages", PackageFamilyName!, "LocalState");

    // 見つからなければ APPMODEL_ERROR_NO_PACKAGE (15700) が返る。
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFamilyName(ref int length, char[]? name);

    private static string? DetectPackage()
    {
        try
        {
            var length = 0;
            GetCurrentPackageFamilyName(ref length, null);   // 必要な長さだけ聞く
            if (length <= 0) return null;

            var buffer = new char[length];
            if (GetCurrentPackageFamilyName(ref length, buffer) != 0) return null;
            var name = new string(buffer, 0, Math.Max(0, length - 1));   // 末尾の NUL を除く
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return null;   // Windows 7 / 8.0 には無い API。どのみち MSIX ではない
        }
    }
}
