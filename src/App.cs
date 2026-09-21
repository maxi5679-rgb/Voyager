using System.Reflection;

namespace Voyager;

internal static class App
{
    public const string Name = "Voyager";
    public const string Codename = "V'Ger";

    /// <summary>
    /// 表示用のバージョン。exe に埋め込まれた値をそのまま読むので、
    /// ビルド時の -p:Version=... と必ず一致する（ここに数字を直書きしない）。
    /// </summary>
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var asm = Assembly.GetExecutingAssembly();

        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(info))
        {
            // "1.0.7+abcdef" のようなビルドメタデータは落とす
            var plus = info.IndexOf('+');
            return plus >= 0 ? info[..plus] : info;
        }

        var file = asm.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
        if (!string.IsNullOrEmpty(file)) return file;

        return asm.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
