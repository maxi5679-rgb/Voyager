namespace Voyager;

/// <summary>
/// 画面に出す文字。日本語と英語を並べて持つ。
///
/// resx を使わないのは、対訳が離れた場所に散ると片方だけ直して食い違うから。
/// ここでは 1 行に両方あるので、読めば揃っているか分かる。
///
/// いまは設定で明示的に選ぶ。将来 OS の表示言語に従わせるときは
/// <see cref="Use"/> に "auto" を足し、CultureInfo を見て ja / en に落とすだけで済む。
/// </summary>
internal static class Strings
{
    private static bool _en;

    /// <summary>"ja"、"en"、"auto"。"auto"（や知らない値）は Windows の表示言語で決める。</summary>
    public static void Use(string? lang) => _en = Resolve(lang) == "en";

    /// <summary>
    /// 設定の値を、実際に使う言語（"ja" か "en"）にする。
    /// "auto" は Windows の表示言語が日本語なら日本語、それ以外は英語。
    /// </summary>
    public static string Resolve(string? lang) => lang?.ToLowerInvariant() switch
    {
        "ja" => "ja",
        "en" => "en",
        _ => System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja" ? "ja" : "en",
    };

    public static string Current => _en ? "en" : "ja";

    private static string T(string ja, string en) => _en ? en : ja;

    // ---------------------------------------------------------------- ツールバー

    public static string OmniPlaceholder => T("URL または質問", "URL or a question");
    public static string NewTabTip => T("新しいタブ (Ctrl+T)", "New tab (Ctrl+T)");
    public static string PickAiTip => T("使う AI を選び直す", "Choose a different AI");
    public static string Settings => T("設定", "Settings");
    public static string BackTip => T("戻る (Alt+←)", "Back (Alt+←)");
    public static string ForwardTip => T("進む (Alt+→)", "Forward (Alt+→)");
    public static string ReloadTip => T("再読み込み (F5)", "Reload (F5)");
    public static string Home => T("ホーム", "Home");
    public static string AddBookmarkTip => T("このページをブックマーク (Ctrl+D)", "Bookmark this page (Ctrl+D)");
    public static string BookmarksTip => T("ブックマーク (Ctrl+Shift+O)", "Bookmarks (Ctrl+Shift+O)");
    public static string PickAi => T("AI 選択", "Choose AI");

    // ---------------------------------------------------------------- タブ

    public static string NewTab => T("新しいタブ", "New tab");

    // ---------------------------------------------------------------- 編集

    public static string Undo => T("元に戻す", "Undo");
    public static string Cut => T("切り取り", "Cut");
    public static string Copy => T("コピー", "Copy");
    public static string Paste => T("貼り付け", "Paste");
    public static string Delete => T("削除", "Delete");
    public static string SelectAll => T("すべて選択", "Select all");

    // ---------------------------------------------------------------- ページの右クリック

    public static string OpenLinkInNewTab => T("リンクを新しいタブで開く", "Open link in new tab");
    public static string CopyLinkAddress => T("リンクアドレスをコピー", "Copy link address");
    public static string OpenImage => T("画像を開く", "Open image");
    public static string SaveImage => T("画像を保存", "Save image as…");
    public static string CopyImage => T("画像をコピー", "Copy image");
    public static string CopyImageAddress => T("画像アドレスをコピー", "Copy image address");
    public static string Back => T("戻る", "Back");
    public static string Forward => T("進む", "Forward");
    public static string Reload => T("再読み込み", "Reload");
    public static string CopyPageAddress => T("このページのアドレスをコピー", "Copy page address");

    /// <summary>選択した語をそのまま出すので、括弧まで含めて言語ごとに変える。</summary>
    public static string SearchFor(string label) => T($"「{label}」を検索", $"Search for “{label}”");

    // ---------------------------------------------------------------- ダウンロード

    public static string SaveTo => T("保存先", "Save to");
    public static string DownloadFolder => T("ダウンロードの保存先", "Download folder");

    // ---------------------------------------------------------------- 知らせ

    public static string WebView2InitFailed(string detail) =>
        T($"WebView2 の初期化に失敗しました。\n\n{detail}",
          $"WebView2 could not start.\n\n{detail}");

    public static string ImageSaveFailed(string detail) =>
        T($"画像を保存できませんでした。\n\n{detail}",
          $"The image could not be saved.\n\n{detail}");

    public static string PageOpenFailed(string detail) =>
        T($"ページを開けませんでした。\n\n{detail}",
          $"That page could not be opened.\n\n{detail}");


    // ---------------------------------------------------------------- ブックマーク バー

    public static string BarEmptyHint => T("右クリックで、いま開いているページをここに追加できます",
                                           "Right-click to add the page you are on");
    public static string MenuEmpty => T("(空)", "(empty)");
    public static string MenuTooDeep => T("…（サイドバーで開く）", "… (open in the sidebar)");
    public static string Open => T("開く", "Open");
    public static string OpenInNewTab => T("新しいタブで開く", "Open in new tab");
    public static string MoveLeft => T("左へ", "Move left");
    public static string MoveRight => T("右へ", "Move right");
    public static string ClearIcon => T("アイコンを消す", "Remove icon");
    public static string MoveToUnsorted => T("未整理へ移す", "Move to unsorted");
    public static string RemoveFromBar => T("バーから削除", "Remove from bar");
    public static string AddThisPage => T("このページを追加", "Add this page");
    public static string BookmarkManager => T("ブックマークマネージャー", "Bookmark manager");
    public static string ManageBookmarks => T("管理", "Manage");
    public static string ManageBookmarksTip => T("ブックマークマネージャーを開く", "Open the bookmark manager");
    public static string BmEdit => T("編集", "Edit");
    public static string BmRename => T("名前を変更", "Rename");
    public static string BmMoveTo => T("フォルダへ移動…", "Move to folder…");
    public static string BmMoveN => T("{0} 件をフォルダへ移動…", "Move {0} to folder…");
    public static string BmDeleteN => T("{0} 件を削除", "Delete {0}");
    public static string BmMove => T("移動", "Move");
    public static string BmSelected => T("{0} 件選択中", "{0} selected");
    public static string BmDragN => T("{0} 件", "{0} items");
    public static string BmDuplicates => T("重複", "Duplicates");
    public static string BmNoDuplicates => T("同じアドレスのブックマークはありません。", "No bookmarks share an address.");
    public static string BmDupNote => T("同じアドレスのブックマークです。各グループの先頭（バーにあるもの、無ければ一番古いもの）を残す候補にしています。",
                                       "Bookmarks with the same address. The first of each group (the one on the bar, or else the oldest) is the one to keep.");
    public static string BmSelectExtras => T("先頭以外をすべて選ぶ", "Select all but the first of each");
    public static string BmShowInFolder => T("フォルダを表示", "Show in folder");
    public static string BmDeadLinks => T("リンク切れ", "Broken links");
    public static string BmCheckStart => T("確認を始める", "Start checking");
    public static string BmCheckStop => T("止める", "Stop");
    public static string BmCheckAgain => T("もう一度確認する", "Check again");
    public static string BmCheckIdle => T("「{0}」とその下のブックマークを、1 件ずつゆっくり確認します。ほかのフォルダを確認するときは、そのフォルダを開いてからここへ戻ってください。",
                                         "Checks the bookmarks in “{0}” and below, slowly, one at a time. To check another folder, open it first and come back here.");
    public static string BmCheckRunning => T("「{0}」を確認中", "Checking “{0}”");
    public static string BmCheckDone => T("「{0}」の確認が終わりました", "Finished checking “{0}”");
    public static string BmCheckStopped => T("「{0}」の確認を止めました", "Stopped checking “{0}”");
    public static string BmCheckNetwork => T("ネットにつながらなくなったようなので止めました（「{0}」）", "Stopped because the connection seems to be down (“{0}”)");
    public static string BmCheckProgress => T("{0} / {1} 件", "{0} of {1}");
    public static string BmCheckSummary => T("切れている {0}・確認できなかった {1}", "Broken {0} · Couldn't confirm {1}");
    public static string BmCheckNone => T("問題のあるリンクは見つかりませんでした。", "No problems found.");
    public static string BmGroupDead => T("切れている", "Broken");
    public static string BmGroupUnsure => T("確認できなかった（ボット対策などで断られただけのこともあります）", "Couldn't confirm (the site may just be blocking automated checks)");
    public static string BmSelectDead => T("切れているものをすべて選ぶ", "Select all broken");
    public static string BmWhyNotFound => T("ページが無い", "Page not found");
    public static string BmWhyNoHost => T("ドメインが無い", "No such domain");
    public static string BmWhyRefused => T("つながらない", "Connection failed");
    public static string BmWhyDenied => T("断られた", "Access denied");
    public static string BmWhyTooMany => T("アクセスが多すぎる", "Too many requests");
    public static string BmWhyServer => T("サーバーエラー", "Server error");
    public static string BmWhyTimeout => T("時間切れ", "Timed out");
    public static string BmWhyTls => T("安全な接続ができない", "Secure connection failed");
    public static string BmWhyOther => T("エラー", "Error");
    public static string BmDeleted => T("{0} 件を削除しました", "Deleted {0}");
    public static string BmUndo => T("元に戻す", "Undo");
    public static string BmConfirmMany => T("{0} 件を削除します。選んだフォルダの中身も一緒に消えます。",
                                           "This deletes {0} items, including everything inside the chosen folders.");
    public static string BmCopyUrl => T("アドレスをコピー", "Copy address");
    public static string BmUrl => T("アドレス", "Address");
    public static string BmSave => T("保存", "Save");
    public static string BmCancel => T("キャンセル", "Cancel");
    public static string BmEmpty => T("このフォルダは空です", "This folder is empty");
    public static string BmNoHits => T("見つかりませんでした", "No matches");
    public static string BmHitsFormat => T("{0} 件", "{0} found");
    public static string BmConfirmFolder => T("「{0}」とその中身をすべて削除します。", "This deletes “{0}” and everything in it.");
    public static string BmBadUrl => T("アドレスが正しくありません。http:// か https:// で始まるものか、example.com の形で入れてください。",
                                       "That address is not valid. Use one starting with http:// or https://, or like example.com.");
    public static string BookmarkAdded => T("ブックマークに追加しました", "Bookmark added");
    public static string BookmarkEdit => T("ブックマークを編集", "Edit bookmark");
    public static string BookmarkName => T("名前", "Name");
    public static string BookmarkFolder => T("フォルダ", "Folder");
    public static string Done => T("完了", "Done");
    public static string NewFolder => T("フォルダを作る", "New folder");
    public static string NewFolderName => T("新しいフォルダ", "New folder");

    // ---------------------------------------------------------------- サイドバー

    public static string SearchBookmarks => T("ブックマークを検索", "Search bookmarks");
    public static string Import => T("読み込み", "Import");
    public static string ImportTip => T("他のブラウザの bookmarks.html を取り込む",
                                        "Import a bookmarks.html from another browser");
    public static string Export => T("書き出し", "Export");
    public static string ExportTip => T("bookmarks.html として保存する", "Save as bookmarks.html");
    public static string TooManyHits => T("200 件以上（先頭のみ表示）", "over 200 (showing the first few)");
    public static string Hits(int n) => T($"{n} 件", n == 1 ? "1 match" : $"{n} matches");
    public static string TotalCount(int n) => T($"全 {n:N0} 件", n == 1 ? "1 bookmark" : $"{n:N0} bookmarks");
    public static string Untitled => T("(名前なし)", "(no name)");
    public static string Rename => T("名前を変更 (F2)", "Rename (F2)");
    public static string MoveToBar => T("ブックマーク バーへ移す", "Move to the bookmarks bar");
    public static string MoveToFolder => T("フォルダへ移動", "Move to folder");
    public static string PutHere => T("ここに入れる", "Put it here");

    // ---------------------------------------------------------------- ダイアログ

    public static string DeleteBookmark => T("ブックマークの削除", "Delete bookmark");
    public static string DeleteFolderWarning(string title, int n) =>
        T($"「{title}」の中の {n:N0} 件も一緒に消えます。よろしいですか。",
          $"This also deletes the {n:N0} bookmarks inside “{title}”. Go ahead?");

    public static string ImportTitle => T("ブックマークの読み込み", "Import bookmarks");
    public static string ImportFilter => T("ブックマーク (*.html;*.htm)|*.html;*.htm|すべてのファイル (*.*)|*.*",
                                           "Bookmarks (*.html;*.htm)|*.html;*.htm|All files (*.*)|*.*");
    public static string ImportedFolder(DateTime when) =>
        T($"インポート {when:yyyy-MM-dd}", $"Imported {when:yyyy-MM-dd}");
    public static string ImportDone(string label, string detail) =>
        T($"「{label}」に取り込みました。\n\n{detail}",
          $"Imported into “{label}”.\n\n{detail}");
    public static string ImportFailed => T("ファイルを読み込めませんでした。", "That file could not be read.");

    public static string ExportTitle => T("ブックマークの書き出し", "Export bookmarks");
    public static string ExportFilter => T("ブックマーク (*.html)|*.html", "Bookmarks (*.html)|*.html");
    public static string ExportDone => T("書き出しました。", "Exported.");
    public static string ExportFailed => T("保存できませんでした。", "That file could not be saved.");

    public static string ImportSummary(int folders, int links, int skipped, int badDates, int icons, int toBar) =>
        T($"フォルダ {folders} / リンク {links} / 空フォルダ除外 {skipped} / 日時不明 {badDates} / アイコン {icons}"
          + (toBar > 0 ? $" / バーへ {toBar}" : ""),
          $"{folders} folders / {links} links / {skipped} empty folders skipped / {badDates} without a date / {icons} icons"
          + (toBar > 0 ? $" / {toBar} onto the bar" : ""));

    public static string DetectedVersion(string? version) =>
        T(string.IsNullOrEmpty(version) ? "検出されたバージョン: なし" : $"検出されたバージョン: {version}",
          string.IsNullOrEmpty(version) ? "Detected version: none" : $"Detected version: {version}");

    // ---------------------------------------------------------------- ページ内メニュー（JS）

    public static string CloseSettings => T("設定を閉じる", "Close settings");
    public static string PickAiMenu => T("AI を選び直す", "Choose a different AI");

    // ---------------------------------------------------------------- 内部ページ

    public static string PickerTitle => T("使う AI を選ぶ", "Choose an AI");
    public static string PickerLead => T("使う AI の公式サイトを開きます。Google や YouTube もそのまま見られます。",
                                         "Opens the AI's own site. Google and YouTube work as usual too.");
    public static string StartTitle => T("どこへ行く", "Where to?");
    public static string StartLead => T("アドレスを貼るか、下のサイトを開きます。質問は選んだ AI 本体に送ります。",
                                        "Paste an address, or pick a site below. A question goes to the AI you chose.");
    public static string OpenEngine(string name) => T($"{name} を開く", $"Open {name}");

    public static string AboutTitle => T("Voyager について", "About Voyager");
    public static string Version(string v) => T($"バージョン {v}", $"Version {v}");
    public static string Build => T("構成", "Build");
    public static string Runtime(string v) => T($"WebView2 ランタイム {v}", $"WebView2 runtime {v}");
    public static string NotDetected => T("未検出", "not detected");
    public static string BuiltAt(string when) => T($"ビルド {when}", $"Built {when}");
    public static string Places => T("場所", "Locations");
    public static string PlaceApp => T("本体", "Application");
    public static string PlaceSettings => T("設定", "Settings");
    public static string PlaceData => T("データ", "Data");
    public static string ToSettings => T("設定へ", "Open settings");
    public static string Nameplate =>
        T("銘板の汚れで <strong>VOYAGER</strong> の三文字が隠れ、残った <strong>V GER</strong> を"
          + "自分の名だと思い込んで還ってきた探査機がいました。こちらは開くたびに名乗ります。",
          "A probe once came home believing its name was <strong>V GER</strong>, three letters of "
          + "<strong>VOYAGER</strong> having been lost under grime. This one says its name every time you open it.");

    public static string SettingsTitle => T("設定", "Settings");
    public static string SettingsLead => T("ホームボタンの行き先と、使う AI を決めます。",
                                           "Where the home button goes, and which AI to use.");
    public static string LanguageHeading => T("言語 / Language", "Language / 言語");
    public static string LanguageAuto => T("自動（Windows に合わせる） / Automatic", "Automatic (follow Windows) / 自動");
    public static string StartupHeading => T("起動時", "On startup");
    public static string RestoreTabs => T("前回開いていたタブを開き直す", "Reopen the tabs from last time");
    public static string RestoreTabsNote => T("前面にあったタブだけをすぐに読み込み、ほかのタブは選んだときに読み込みます。",
                                             "Only the front tab loads right away; the others load when you pick them.");
    public static string LanguageNote =>
        T("ブックマークのフォルダ名（「ブックマーク バー」など）は表示だけ読み替えます。保存されている名前は変わりません。サイトに伝える言語は、次に起動したときから切り替わります。",
          "Built-in folder names are only re-labeled on screen. The names stored in your bookmarks do not change. The language sent to websites changes the next time Voyager starts.");

    public static string HomeHeading => T("ホーム", "Home");
    public static string HomeStart => T("スタート画面", "Start page");
    public static string HomeStartHint => T("AI と行き先を選ぶ画面", "Pick an AI and where to go");
    public static string HomeAi => T("選んでいる AI", "The AI you chose");
    public static string HomeAiHint => T("公式サイトを開く", "Opens its own site");
    public static string HomeCustom => T("指定した URL", "A URL you set");
    public static string HomeCustomHint => T("ホームボタンでこのアドレスを開く", "The home button opens this address");

    public static string AiHeading => T("AI", "AI");
    public static string RememberEngine => T("前回選んだ AI を覚えておく", "Remember the AI I chose last time");
    public static string ChooseAtStartup => T("起動時に選ぶ", "Ask at startup");

    public static string DownloadsHeading => T("ダウンロード", "Downloads");
    public static string SaveLocation => T("保存先", "Save to");
    public static string WindowsDefaultFolder => T("Windows の既定（ダウンロード フォルダー）",
                                                   "The Windows default (your Downloads folder)");
    public static string Change => T("変更", "Change");
    public static string ResetToDefault => T("既定に戻す", "Reset");
    public static string AskEveryDownload => T("ダウンロードするたびに保存先を確認する",
                                               "Ask where to save every download");
    public static string DownloadsNote =>
        T("保存先は次回起動時も引き継ぎます。C ドライブではなく F ドライブへ直接落とす、といった指定もできます。<br>"
          + "指定した場所が見つからないとき（外付けを外した後など）は、黙って Windows の既定に戻ります。",
          "The folder is remembered across restarts, so you can send downloads straight to another drive.<br>"
          + "If the folder is gone — an external drive unplugged, say — it quietly falls back to the Windows default.");

    public static string ContextMenuHeading => T("右クリック", "Right-click");
    public static string PreferPageMenu => T("ページ側のメニューを優先する", "Let the page show its own menu");
    public static string ContextMenuNote =>
        T("入れておくと、Google スプレッドシートや claude.ai など自前のメニューを持つサイトでは"
          + "そちらが出ます。代わりに AI の入力欄のように、ページがメニューを出さずに"
          + "右クリックだけ握り潰す場所では何も出ません。<br>外すと、どこでも Voyager のメニューが出ます。<br>"
          + "どちらの設定でも <strong>Shift + 右クリック</strong> は必ず Voyager のメニューです。",
          "With this on, sites that bring their own menu — Google Sheets, claude.ai — get to show it. "
          + "The cost is that somewhere like an AI's input box, which swallows the right-click without "
          + "offering anything, you get nothing at all.<br>With it off, Voyager's menu appears everywhere.<br>"
          + "Either way, <strong>Shift + right-click</strong> always gives you Voyager's menu.");

    public static string HistoryHeading => T("閲覧履歴", "Browsing history");
    public static string ClearHistory => T("閲覧履歴を消す", "Clear browsing history");
    public static string HistoryNote(int days) =>
        T($"開いたページの記録は WebView2 が持っています。{days} 日より古いものは起動のたびに消えます。"
          + "ボタンを押すと全部消えます。どちらでもログイン状態と Cookie は残ります。",
          $"WebView2 keeps its own record of the pages you open. Entries older than {days} days are removed "
          + "every time Voyager starts; the button removes all of them. Logins and cookies are kept either way.");
    public static string ClearHistoryConfirm =>
        T("閲覧履歴をすべて消します。ログイン状態と Cookie は残ります。\n"
          + "開いているタブの「戻る」「進む」も使えなくなります。\n\nよろしいですか？",
          "This clears all browsing history. Logins and cookies are kept.\n"
          + "Open tabs also lose their back and forward history.\n\nContinue?");
    public static string HistoryCleared(DateTime at) =>
        T($"{at:HH:mm} に消しました。", $"Cleared at {at:HH:mm}.");
    public static string HistoryClearFailed(string detail) =>
        T($"消せませんでした: {detail}", $"Could not clear it: {detail}");

    public static string DataHeading => T("データ", "Data");
    public static string DataNote => T("ログイン状態と Cookie は次の場所に保存されます。",
                                       "Logins and cookies are kept here.");
    public static string AboutButton => T("Voyager について", "About Voyager");

    public static string AboutImageAlt =>
        T("星間空間へ出るボイジャー1号の想像図",
          "Artist's concept of Voyager 1 entering interstellar space");

    public static string AboutImageCaption =>
        T("星間空間に入るボイジャー1号の想像図。実写ではありません。<br>Credit: NASA/JPL-Caltech（PIA17462, 2013）",
          "Artist's concept of Voyager 1 entering interstellar space. Not a photograph.<br>Credit: NASA/JPL-Caltech (PIA17462, 2013)");

    // ---------------------------------------------------------------- 既定フォルダの読み替え

    /// <summary>
    /// 作り付けフォルダの表示名。保存されている名前そのものは日本語のまま触らない。
    /// ここで訳すと bookmarks.json のフォルダ名と一致しなくなり、言語を切り替えるたびに
    /// 空のフォルダが増えてしまう。出すときだけ読み替える。
    /// </summary>
    public static string FolderName(string name) => !_en ? name : name switch
    {
        "ブックマーク バー" => "Bookmarks bar",
        "未整理" => "Unsorted",
        "その他のブックマーク" => "Other bookmarks",
        _ => name,
    };
}
