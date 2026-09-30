using Microsoft.Web.WebView2.WinForms;

namespace Voyager;

internal sealed class BrowserTab
{
    public string Id { get; } = Guid.NewGuid().ToString("N")[..8];

    public string Title { get; set; } = Strings.NewTab;

    /// <summary>null なら内部ページ（スタート画面）を表示している。</summary>
    public string? Url { get; set; }

    /// <summary>
    /// タブごとに 1 つ持ち、タブを切り替えても破棄しない。
    /// これでログイン状態・スクロール位置・入力中の内容が保たれる。
    /// </summary>
    public WebView2? View { get; set; }

    public TabItem? Item { get; set; }

    /// <summary>右クリックを横取りする注入スクリプトの id。設定を変えたときに差し替える。</summary>
    public string? ContextScriptId { get; set; }

    /// <summary>
    /// このタブに最後に指示した URL。リダイレクト先とは違うことがある
    /// （twitter.com/... を開くと x.com/... に飛ばされる、など）。
    /// ブックマークに入っているのはリダイレクト前の方なので、ファビコンの照合に使う。
    ///
    /// 使ってよいのは、その指示から始まったリダイレクトの間だけ。ページ内のリンクで
    /// 別のページへ移ったら消す（MainForm.TrackRequested）。消さずにいると、
    /// Yahoo! から辿った先のサイトのファビコンが Yahoo! のブックマークに書き込まれた。
    /// </summary>
    public string? RequestedUrl { get; set; }

    /// <summary>RequestedUrl を指示した直後で、その遷移の NavigationStarting をまだ見ていない。</summary>
    public bool RequestPending { get; set; }

    public bool IsReady => View?.CoreWebView2 is not null;
}
