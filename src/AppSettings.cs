using System.Text.Json;
using System.Text.Json.Serialization;

namespace Voyager;

internal sealed record SavedTab(string Url, string? Title);

internal sealed class AppSettings
{
    /// <summary>start | ai | google | custom</summary>
    public string HomeKind { get; set; } = "start";
    public string HomeUrl { get; set; } = "";
    public bool RememberEngine { get; set; } = true;
    public string? EngineId { get; set; }
    public int WindowWidth { get; set; } = 1320;
    public int WindowHeight { get; set; } = 860;
    public bool Maximized { get; set; }

    /// <summary>ブックマークのサイドバーを開いているか。</summary>
    public bool SidebarOpen { get; set; }
    public int SidebarWidth { get; set; } = 300;

    /// <summary>アドレス欄の下のブックマークバーを出すか（Ctrl+Shift+B）。</summary>
    public bool BarVisible { get; set; } = true;

    /// <summary>
    /// 右クリックを、まずページ側に渡すか。
    /// true（既定）なら Google スプレッドシートや claude.ai のような
    /// 自前のメニューを持つサイトはそちらが出る。false なら常に Voyager のメニュー。
    /// どちらでも Shift + 右クリックは必ず Voyager のメニューになる。
    /// </summary>
    public bool PageContextMenu { get; set; } = true;

    /// <summary>
    /// ダウンロードの保存先。空なら Windows の既定（%USERPROFILE%\Downloads）のまま。
    /// C: が手狭なときに F: へ直接落とす、といった使い方を想定している。
    /// </summary>
    public string DownloadDir { get; set; } = "";

    /// <summary>ダウンロードのたびに保存先をたずねるか。</summary>
    public bool AskDownloadDir { get; set; }

    /// <summary>
    /// 「毎回たずねる」で最後に選んだフォルダ。次のダイアログはここから開く。
    /// 空、または消えていたら DownloadDir に戻る。
    /// </summary>
    public string LastSaveDir { get; set; } = "";

    /// <summary>★ の窓で最後に選んだフォルダの id。次に ★ を押したときの入れ先。空なら「未整理」。</summary>
    public string LastBookmarkFolder { get; set; } = "";

    /// <summary>
    /// 画面の言語。"auto"（Windows の表示言語に従う）、"ja"、"en"。
    /// 初めて起動した人は "auto"。前から使っている人は、保存済みの値（"ja" など）がそのまま残る。
    /// </summary>
    public string Language { get; set; } = "auto";

    /// <summary>起動したとき、前回閉じたときのタブを開き直すか。既定はしない（Chrome と同じ）。</summary>
    public bool RestoreTabs { get; set; }

    /// <summary>前回閉じたときのタブ。RestoreTabs がオフのときは空にしておく（見たページを残さない）。</summary>
    public List<SavedTab> LastTabs { get; set; } = [];

    /// <summary>LastTabs のうち、前面にあったタブの番号。</summary>
    public int LastActiveTab { get; set; }

    [JsonIgnore]
    public static string Dir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Voyager");

    [JsonIgnore]
    public static string FilePath { get; } = Path.Combine(Dir, "settings.json");

    /// <summary>Cookie とログイン状態はここに残る（更新しても消えない）。</summary>
    [JsonIgnore]
    public static string UserDataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Voyager", "WebView2");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // 壊れていたら既定値で続行する
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 設定の保存失敗でアプリを止めない
        }
    }
}
