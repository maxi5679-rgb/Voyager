using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Voyager;

internal sealed class MainForm : Form
{
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly List<BrowserTab> _tabs = [];
    private BrowserTab _active = null!;
    private Engine? _engine;
    private bool _settingsOpen;
    private CoreWebView2Environment? _env;

    // --- chrome (ネイティブ側の UI) ---
    private readonly Panel _topBar = new() { Dock = DockStyle.Top, Height = 40, BackColor = Theme.Surface };
    private readonly Panel _navBar = new() { Dock = DockStyle.Top, Height = 48, BackColor = Theme.Surface };
    private readonly Panel _stage = new() { Dock = DockStyle.Fill, BackColor = Theme.Background };
    /// <summary>ページを表示する領域。サイドバーの右側にあたる。</summary>
    private readonly Panel _content = new() { Dock = DockStyle.Fill, BackColor = Theme.Background };
    private readonly FlowLayoutPanel _tabStrip = new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.LeftToRight,
        WrapContents = false,
        AutoScroll = false,
        Padding = new Padding(8, 0, 0, 0),
        BackColor = Theme.Surface,
    };
    private readonly TextBox _omni = new()
    {
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = Theme.Card,
        ForeColor = Theme.Text,
        Font = Theme.Ui(10f),
        PlaceholderText = "URL または質問",
    };
    private Button _back = null!, _fwd = null!, _reload = null!, _home = null!, _engineBtn = null!, _settingsBtn = null!;
    private Button _star = null!, _bmBtn = null!;

    private readonly BookmarkStore _bookmarks = BookmarkStore.Load();
    private BookmarkSidebar _sidebar = null!;
    private BookmarkBar _bar = null!;
    private readonly Splitter _splitter = new()
    {
        Dock = DockStyle.Left, Width = 5, BackColor = Theme.Border, MinExtra = 360, MinSize = 220,
    };

    /// <summary>
    /// ファビコンの保存を先延ばしにするための時計。
    ///
    /// X は未読の有無で絵そのものを差し替えるので、開いているだけで何度も変わる。
    /// 絵が変わるたびに bookmarks.json（数 MB）を丸ごと書き直すと、ただ眺めている
    /// 間じゅうディスクを叩き続けることになる。画面の方は即座に直し、保存だけ
    /// 落ち着くまで待つ。閉じるときは OnFormClosing が必ず書くので、取りこぼさない。
    /// </summary>
    private readonly System.Windows.Forms.Timer _faviconSave = new() { Interval = 5000 };

    /// <summary>利用者がアドレス欄を編集中かどうか（編集中だけ自動更新を止める）。</summary>
    private bool _omniEditing;
    /// <summary>プログラムから _omni.Text を書き換えている最中のフラグ。</summary>
    private bool _omniSyncing;

    /// <summary>内部ページ専用。外部サイトは絶対にここに読み込まない。</summary>
    private readonly WebView2 _uiView = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Theme.Background };

    public MainForm()
    {
        Text = App.Name;
        BackColor = Theme.Background;
        MinimumSize = new Size(800, 560);
        ClientSize = new Size(Math.Max(800, _settings.WindowWidth), Math.Max(560, _settings.WindowHeight));
        StartPosition = FormStartPosition.CenterScreen;
        if (_settings.Maximized) WindowState = FormWindowState.Maximized;
        KeyPreview = true;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { /* アイコンが無くても動かす */ }

        _engine = _settings.RememberEngine ? Engines.ById(_settings.EngineId) : null;

        _faviconSave.Tick += (_, _) =>
        {
            _faviconSave.Stop();
            _bookmarks.Save();
            Log.Write("favicon: saved");
        };

        BuildChrome();
        // Add した順の逆から端に寄る。上から topBar / navBar / bar / stage の並びになる。
        Controls.Add(_stage);
        Controls.Add(_bar);
        Controls.Add(_navBar);
        Controls.Add(_topBar);

        // Fill を先に、端に寄せるものを後に Add する（この順序が配置を決める）。
        _stage.Controls.Add(_content);
        _stage.Controls.Add(_splitter);
        _stage.Controls.Add(_sidebar);
        _content.Controls.Add(_uiView);

        var first = new BrowserTab();
        _tabs.Add(first);
        _active = first;
        RebuildTabStrip();
    }

    // ---------------------------------------------------------------- UI 構築

    private void BuildChrome()
    {
        _sidebar = new BookmarkSidebar(_bookmarks) { Visible = _settings.SidebarOpen, Width = _settings.SidebarWidth };
        _splitter.Visible = _settings.SidebarOpen;
        _sidebar.OpenRequested += (url, newTab) =>
        {
            if (newTab) NewTab();
            Submit(url);
        };

        _bar = new BookmarkBar(_bookmarks) { Visible = _settings.BarVisible };
        _bar.OpenRequested += (url, newTab) =>
        {
            if (newTab) NewTab();
            Submit(url);
        };
        _bar.CurrentPageRequested += () => (_active.Url, _active.Title);
        // 片方が書き換えたら、もう片方も描き直す。同じストアを 2 つの画面が見ているため。
        _bar.StoreChanged += () => _sidebar.Reload();
        _sidebar.StoreChanged += () => _bar.Reload();

        var topRight = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 5, 8, 0),
            BackColor = Theme.Surface,
        };
        var newTab = IconButton("+", "新しいタブ (Ctrl+T)", (_, _) => NewTab());
        _engineBtn = TextButton("AI", "使う AI を選び直す", (_, _) => OpenPicker());
        _settingsBtn = TextButton("設定", "設定", (_, _) => ToggleSettings());
        topRight.Controls.AddRange([newTab, _engineBtn, _settingsBtn]);

        // タブ列は「下段のアドレス欄と同じ入れ子構造」にする。
        // FlowLayoutPanel を直接 Dock=Fill で置くと、右側のボタンにクリックが届かなくなる。
        var tabHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface };
        tabHost.Controls.Add(_tabStrip);
        // 並べる順番が配置順を決める。埋め尽くし(Fill)を先に Add し、端に寄せる方を後に Add する。
        // ここで BringToFront すると Fill 側がバー全体を占有し、右のボタンの下に潜り込む。
        _topBar.Controls.Add(tabHost);
        _topBar.Controls.Add(topRight);

        var navLeft = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8, 8, 0, 0),
            BackColor = Theme.Surface,
        };
        _back = IconButton("‹", "戻る (Alt+←)", (_, _) => Active()?.CoreWebView2?.GoBack());
        _fwd = IconButton("›", "進む (Alt+→)", (_, _) => Active()?.CoreWebView2?.GoForward());
        _reload = IconButton("↻", "再読み込み (F5)", (_, _) => Active()?.CoreWebView2?.Reload());
        _home = IconButton("⌂", "ホーム", (_, _) => GoHome());
        _star = IconButton("☆", "このページをブックマーク (Ctrl+D)", (_, _) => AddCurrentPage());
        _bmBtn = IconButton("▤", "ブックマーク (Ctrl+Shift+O)", (_, _) => ToggleSidebar());
        navLeft.Controls.AddRange([_back, _fwd, _reload, _home, _star, _bmBtn]);

        var omniHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 9, 10, 9), BackColor = Theme.Surface };
        _omni.Dock = DockStyle.Fill;
        _omni.TabStop = false;   // 起動直後にここへフォーカスが来ないように
        _omni.TextChanged += (_, _) => { if (!_omniSyncing) _omniEditing = true; };
        _omni.Leave += (_, _) => _omniEditing = false;
        _omni.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                _omniEditing = false;
                UpdateChrome();
                return;
            }
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            _omniEditing = false;
            Submit(_omni.Text);
        };
        omniHost.Controls.Add(_omni);

        // 同上。BringToFront するとアドレス欄が左端から始まり、URL の先頭がボタンの下に隠れる。
        _navBar.Controls.Add(omniHost);
        _navBar.Controls.Add(navLeft);
    }

    private static Button Base(string text, string tip, EventHandler onClick, int width)
    {
        var b = new Button
        {
            Text = text,
            Width = width,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Card,
            ForeColor = Theme.Text,
            Font = Theme.Ui(10f),
            Margin = new Padding(0, 0, 6, 0),
            Cursor = Cursors.Hand,
            TabStop = false,
        };
        b.FlatAppearance.BorderColor = Theme.Border;
        b.FlatAppearance.MouseOverBackColor = Theme.CardHover;
        b.FlatAppearance.MouseDownBackColor = Theme.CardHover;
        b.Click += onClick;
        new ToolTip().SetToolTip(b, tip);
        return b;
    }

    private static Button IconButton(string text, string tip, EventHandler onClick) => Base(text, tip, onClick, 34);

    private static Button TextButton(string text, string tip, EventHandler onClick)
    {
        var b = Base(text, tip, onClick, 74);
        b.Font = Theme.Ui(9f);
        return b;
    }

    // ---------------------------------------------------------------- 起動

    private bool _loaded;

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_loaded) { Log.Write("OnLoad skipped (already loaded)"); return; }
        _loaded = true;
        try
        {
            Directory.CreateDirectory(AppSettings.UserDataDir);
            var options = new CoreWebView2EnvironmentOptions
            {
                Language = "ja-JP",
                AllowSingleSignOnUsingOSPrimaryAccount = false,
            };
            Log.Write($"env create: userData={AppSettings.UserDataDir}");
            _env = await CoreWebView2Environment.CreateAsync(null, AppSettings.UserDataDir, options);
            Log.Write($"env ok: browser={_env.BrowserVersionString}");

            await _uiView.EnsureCoreWebView2Async(_env);
            var core = _uiView.CoreWebView2;
            Log.Write($"uiView ready. stage={_content.Width}x{_content.Height} view={_uiView.Width}x{_uiView.Height} visible={_uiView.Visible}");

            core.ProcessFailed += (_, a) => Log.Write($"!! ProcessFailed: {a.ProcessFailedKind} {a.Reason}");
            core.NavigationCompleted += async (_, a) =>
            {
                Log.Write($"nav completed: success={a.IsSuccess} status={a.WebErrorStatus} http={a.HttpStatusCode}");
                try
                {
                    var len = await core.ExecuteScriptAsync("document.body ? document.body.innerText.length : -1");
                    var html = await core.ExecuteScriptAsync("document.documentElement.outerHTML.length");
                    Log.Write($"  body text len={len} html len={html}");
                }
                catch (Exception ex) { Log.Write($"  script err: {ex.Message}"); }
            };
            core.DOMContentLoaded += (_, _) => Log.Write("DOMContentLoaded");
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = true;   // 独自メニューに差し替えるため有効にする
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsWebMessageEnabled = true;
            core.WebMessageReceived += OnUiMessage;
            // 既定メニューを止めるだけ。実際のメニューはページから来る "menu" で出す
            core.ContextMenuRequested += (_, a) => a.Handled = true;
            core.NewWindowRequested += (_, a) => a.Handled = true;
            core.NavigationStarting += (_, a) =>
            {
                // 内部ページ以外（実在するサイト）へは絶対に遷移させない。
                // NavigateToString の遷移は about:blank だったり HTML 文字列そのものだったりするので、
                // 「通信を伴うスキームだけを止める」形にする。
                if (Uri.TryCreate(a.Uri, UriKind.Absolute, out var u) &&
                    u.Scheme is "http" or "https" or "file" or "ftp")
                    a.Cancel = true;
            };
            Render();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"WebView2 の初期化に失敗しました。\n\n{ex.Message}",
                App.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _settings.Maximized = WindowState == FormWindowState.Maximized;
        if (WindowState == FormWindowState.Normal)
        {
            _settings.WindowWidth = ClientSize.Width;
            _settings.WindowHeight = ClientSize.Height;
        }
        _settings.SidebarOpen = _sidebar.Visible;
        if (_sidebar.Visible) _settings.SidebarWidth = _sidebar.Width;
        _settings.BarVisible = _bar.Visible;
        _settings.Save();
        _faviconSave.Stop();   // 直後に自分で書くので、二重に書かせない
        _bookmarks.Save();
        base.OnFormClosing(e);
    }

    // ---------------------------------------------------------------- 描画

    private WebView2? Active() => _active.View;

    /// <summary>
    /// 表示を状態に合わせ直す。ビューは破棄せず Visible の切り替えだけで行うので、
    /// タブを行き来してもログイン状態やスクロール位置が失われない。
    /// </summary>
    private void Render()
    {
        if (_uiView.CoreWebView2 is null) return;

        var showInternal = _settingsOpen || _engine is null || _active.Url is null;

        foreach (var t in _tabs)
            if (t.View is not null)
                t.View.Visible = !showInternal && ReferenceEquals(t, _active);

        _uiView.Visible = showInternal;
        if (showInternal)
        {
            _uiView.BringToFront();
            Log.Write($"render internal: settings={_settingsOpen} engine={_engine?.Id ?? "-"} url={Log.Url(_active.Url)} " +
                      $"view={_uiView.Width}x{_uiView.Height} visible={_uiView.Visible} parent={_uiView.Parent?.Name ?? "?"}");
            _uiView.CoreWebView2.NavigateToString(
                LogHtml(_settingsOpen ? Pages.Settings(_settings, _engine)
                        : _engine is null ? Pages.Picker()
                        : Pages.Start(_engine)));
        }
        else
        {
            _active.View?.BringToFront();

            // WebView2 の描画部分は、フォーカスを持つまでマウス入力を受け取らない。
            // 左クリックで初めてフォーカスが入るため、それまで右クリックが丸ごと無視される
            // （選択してから右クリックすると効くのは、選択のための左クリックが効いていたから）。
            // 表示した時点でこちらから渡しておく。
            // アドレス欄に打ち込んでいる最中は奪わない。
            if (!_omniEditing && !_omni.Focused) _active.View?.Focus();
        }

        UpdateChrome();
    }

    private static string LogHtml(string html)
    {
        Log.Write($"  html generated: {html.Length} chars, head={html[..Math.Min(60, html.Length)].Replace("\n", " ")}");
        return html;
    }

    private void UpdateChrome()
    {
        _engineBtn.Text = _engine?.Name ?? "AI 選択";
        _settingsBtn.ForeColor = _settingsOpen ? Theme.Accent : Theme.Text;

        // 利用者が打ち込んでいる最中だけ自動更新を止める（フォーカスの有無では判定しない。
        // 起動直後からフォーカスを持っていると、一度も URL が入らなくなる）。
        // 代入しただけでは横スクロールが右端に残り、長い URL は末尾しか見えないので先頭へ戻す。
        var url = _active.Url ?? "";
        if (!_omniEditing && _omni.Text != url)
        {
            _omniSyncing = true;
            _omni.Text = url;
            _omni.SelectionStart = 0;
            _omni.SelectionLength = 0;
            _omniSyncing = false;

            // 代入だけでは再描画されないことがある（WebView2 が前面にいると顕著）。
            // 明示的に描き直させる。
            _omni.Invalidate();
            _omni.Update();
        }

        // ★ は「このページが入っているか」で見た目を変える。もう一度押すと外れる。
        var saved = FindByUrl(_active.Url) is not null;
        _star.Text = saved ? "★" : "☆";
        _star.ForeColor = saved ? Theme.Accent : Theme.Text;
        _star.Enabled = !string.IsNullOrWhiteSpace(_active.Url);
        _bmBtn.ForeColor = _sidebar.Visible ? Theme.Accent : Theme.Text;

        var core = _active.View?.CoreWebView2;
        var onPage = _active.Url is not null && !_settingsOpen;
        _back.Enabled = onPage && core?.CanGoBack == true;
        _fwd.Enabled = onPage && core?.CanGoForward == true;
        _reload.Enabled = onPage;
        foreach (var b in new[] { _back, _fwd, _reload })
            b.ForeColor = b.Enabled ? Theme.Text : Theme.Border;

        foreach (var t in _tabs)
        {
            if (t.Item is null) continue;
            t.Item.TabText = t.Title;
            t.Item.Active = ReferenceEquals(t, _active);
            t.Item.ShowClose = _tabs.Count > 1;
        }

        Text = _active.Url is null ? App.Name : $"{_active.Title} - {App.Name}";

        // アドレス欄の中身は URL そのもの。クエリを落として残す。
        Log.Write($"chrome: url={Log.Url(url)} editing={_omniEditing} omniLen={_omni.Text.Length} " +
                  $"onPage={onPage} focused={_omni.Focused} visible={_omni.Visible} size={_omni.Width}x{_omni.Height}");
    }

    private void RebuildTabStrip()
    {
        _tabStrip.SuspendLayout();
        _tabStrip.Controls.Clear();
        foreach (var tab in _tabs)
        {
            var item = new TabItem { TabText = tab.Title };
            var captured = tab;
            item.Activated += (_, _) => SelectTab(captured);
            item.CloseRequested += (_, _) => CloseTab(captured);
            tab.Item = item;
            _tabStrip.Controls.Add(item);
        }
        _tabStrip.ResumeLayout();
        LayoutTabs();
        UpdateChrome();
    }

    private void LayoutTabs()
    {
        if (_tabs.Count == 0) return;
        var available = Math.Max(120, _tabStrip.ClientSize.Width - 12);
        var width = Math.Clamp(available / _tabs.Count - 6, 92, 180);
        foreach (var t in _tabs)
            if (t.Item is not null) t.Item.Width = width;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutTabs();
    }

    // ---------------------------------------------------------------- タブ操作

    private void SelectTab(BrowserTab tab)
    {
        if (ReferenceEquals(tab, _active)) return;
        _active = tab;
        _settingsOpen = false;
        Render();
    }

    private void NewTab()
    {
        var tab = new BrowserTab();
        _tabs.Add(tab);
        _active = tab;
        _settingsOpen = false;
        RebuildTabStrip();
        Render();
    }

    private void CloseTab(BrowserTab tab)
    {
        if (_tabs.Count <= 1) return;
        var index = _tabs.IndexOf(tab);
        _tabs.Remove(tab);

        if (tab.View is not null)
        {
            _content.Controls.Remove(tab.View);
            tab.View.Dispose();
            tab.View = null;
        }

        if (ReferenceEquals(tab, _active))
            _active = _tabs[Math.Clamp(index, 0, _tabs.Count - 1)];

        RebuildTabStrip();
        Render();
    }

    private void OpenInNewTab(string url)
    {
        if (!UrlHelper.IsNavigable(url)) return;
        var tab = new BrowserTab();
        _tabs.Add(tab);
        _active = tab;
        _settingsOpen = false;
        RebuildTabStrip();
        Navigate(tab, url);
    }

    // ---------------------------------------------------------------- ナビゲーション

    private async void Navigate(BrowserTab tab, string url)
    {
        if (!UrlHelper.IsNavigable(url)) return;

        tab.Url = url;
        tab.RequestedUrl = url;
        tab.Title = UrlHelper.HostTitle(url);
        _settingsOpen = false;
        _omniEditing = false;

        try
        {
            await EnsureView(tab);
            tab.View!.CoreWebView2.Navigate(url);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or WebView2RuntimeNotFoundException)
        {
            MessageBox.Show($"ページを開けませんでした。\n\n{ex.Message}", App.Name,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            tab.Url = null;
        }
        Render();
    }

    private async Task EnsureView(BrowserTab tab)
    {
        if (tab.View is not null)
        {
            if (tab.View.CoreWebView2 is null) await tab.View.EnsureCoreWebView2Async(_env);
            return;
        }

        var view = new WebView2
        {
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = Theme.Background,
            Visible = false,
        };
        tab.View = view;
        _content.Controls.Add(view);
        await view.EnsureCoreWebView2Async(_env);

        var core = view.CoreWebView2;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = true;
        core.Settings.AreDefaultContextMenusEnabled = true;   // 独自メニューに差し替えるため有効にしておく
        core.Settings.IsWebMessageEnabled = false;            // サイト側からアプリを操作させない
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsPasswordAutosaveEnabled = true;
        core.Settings.IsGeneralAutofillEnabled = true;

        core.DocumentTitleChanged += (_, _) =>
        {
            var title = core.DocumentTitle;
            tab.Title = string.IsNullOrWhiteSpace(title)
                ? UrlHelper.HostTitle(tab.Url)
                : title.Length > 36 ? title[..36] : title;
            UpdateChrome();
        };
        core.SourceChanged += (_, _) =>
        {
            tab.Url = core.Source;
            if (ReferenceEquals(tab, _active)) UpdateChrome();
        };
        core.HistoryChanged += (_, _) => { if (ReferenceEquals(tab, _active)) UpdateChrome(); };
        core.NavigationCompleted += async (_, _) => await ProbeContextScript(tab);
        // ファビコンは取り込んだ時点の絵で固定されてしまう。X のように未読で
        // 絵そのものを差し替えるサイトだと、赤丸が永久に残る。開いたら取り直す。
        //
        // async void 相当のラムダなので、中で例外が出ても誰も受け取らない。
        // 右クリックの件と同じ落とし穴なので、必ず自分で捕まえて書き出す。
        core.FaviconChanged += async (_, _) =>
        {
            Log.Write("favicon: event");
            try { await RefreshFavicon(tab); }
            catch (Exception ex) { Log.Write($"!! favicon failed: {ex.GetType().Name} {ex.Message}"); }
        };
        Log.Write("favicon: handler attached");
        core.NavigationStarting += (_, a) =>
        {
            // http / https 以外（file: など）は開かない
            if (!UrlHelper.IsNavigable(a.Uri)) a.Cancel = true;
        };
        core.NewWindowRequested += (_, a) =>
        {
            a.Handled = true;
            OpenInNewTab(a.Uri);
        };
        core.WindowCloseRequested += (_, _) => CloseTab(tab);
        core.ContextMenuRequested += (_, a) =>
        {
            // ここで例外を投げるとメニューが出ないだけでなく、原因も残らない。
            try { ShowContextMenu(view, a); }
            catch (Exception ex) { Log.Write($"!! context menu failed: {ex.GetType().Name} {ex.Message}"); }
        };
        _ = InstallContextScript(tab);
    }

    // ---------------------------------------------------------------- 右クリックの横取り

    /// <summary>
    /// ページが contextmenu を自前で処理すると preventDefault され、WebView2 は
    /// ContextMenuRequested を上げてこない。AI の入力欄で右クリックが死ぬのはこれが理由。
    ///
    /// そこで、ページのどのハンドラより先に走るキャプチャ段階の listener を仕込み、
    /// そこで stopPropagation する。preventDefault はしないので既定動作は生き、
    /// WebView2 がこちらにイベントを渡してくれる。
    /// Shift を押していれば設定に関わらず横取りする（Firefox と同じ逃げ道）。
    /// </summary>
    private static string ContextScript(bool pageFirst) => $$"""
        (function () {
          if (window.__vgCtx) { window.__vgCtx.pageFirst = {{(pageFirst ? "true" : "false")}}; return; }
          window.__vgCtx = { pageFirst: {{(pageFirst ? "true" : "false")}} };
          var steal = function (e) { return e.shiftKey || !window.__vgCtx.pageFirst; };

          // stopPropagation では足りない。ページが同じ window にハンドラを付けていると
          // 「同じ節点の別の listener」は止まらないので、そちらだけ生き残ってしまう。
          // stopImmediatePropagation なら後続の listener ごと止まる。
          window.addEventListener('contextmenu', function (e) {
            if (steal(e)) e.stopImmediatePropagation();
          }, true);

          // contextmenu を待っていても間に合わない場合がある。
          // 右ボタンの mousedown が preventDefault されると、Chromium は contextmenu を
          // そもそも発生させない。ページがそれをやると上の listener は一度も呼ばれない。
          // なので押した/離した段階で先に止める。
          // ここで preventDefault は絶対にしないこと。やると自分で contextmenu を潰す。
          ['pointerdown', 'mousedown', 'pointerup', 'mouseup'].forEach(function (t) {
            window.addEventListener(t, function (e) {
              if (e.button === 2 && steal(e)) e.stopImmediatePropagation();
            }, true);
          });
        })();
        """;

    private async Task InstallContextScript(BrowserTab tab)
    {
        var core = tab.View?.CoreWebView2;
        if (core is null) return;
        try
        {
            if (tab.ContextScriptId is { } old) core.RemoveScriptToExecuteOnDocumentCreated(old);
            tab.ContextScriptId = await core.AddScriptToExecuteOnDocumentCreatedAsync(
                ContextScript(_settings.PageContextMenu));
            // すでに開いているページにも今すぐ効かせる（再読み込みを待たせない）
            await core.ExecuteScriptAsync(ContextScript(_settings.PageContextMenu));
            Log.Write($"ctx script: installed id={tab.ContextScriptId} pageFirst={_settings.PageContextMenu}");
        }
        catch (Exception ex)
        {
            // COMException はタブが閉じかけているだけ。それ以外は黙って消すと原因が追えない。
            Log.Write($"!! ctx script install failed: {ex.GetType().Name} {ex.Message}");
        }
    }

    /// <summary>
    /// 注入したスクリプトがそのページで生きているかを見る。
    /// 「右クリックが効かない」の原因が、注入が届いていないのか、
    /// 届いたうえでページに負けているのかを切り分けるため。
    /// </summary>
    private static async Task ProbeContextScript(BrowserTab tab)
    {
        var core = tab.View?.CoreWebView2;
        if (core is null) return;
        try
        {
            var r = await core.ExecuteScriptAsync(
                "JSON.stringify({live:!!window.__vgCtx,pf:window.__vgCtx?window.__vgCtx.pageFirst:null})");
            Log.Write($"ctx probe: {r}");
        }
        catch (COMException) { }
    }

    /// <summary>
    /// いま表示しているページのファビコンで、同じ URL のブックマークを更新する。
    /// WebView2 が既に持っている絵を貰うだけなので、通信は発生しない。
    /// </summary>
    private async Task RefreshFavicon(BrowserTab tab)
    {
        var core = tab.View?.CoreWebView2;
        if (core is null) { Log.Write("favicon: no core"); return; }

        var url = core.Source;
        Log.Write($"favicon: enter source={Log.Url(url)} requested={Log.Url(tab.RequestedUrl)}");
        if (string.IsNullOrWhiteSpace(url)) return;

        // 同じページを指すブックマークを拾う。3 通り見るのは、指示した URL と
        // 実際に着いた URL がずれるため（twitter.com → x.com のリダイレクトなど）。
        // ホストが同じものもまとめて更新する。ファビコンはサイト単位で 1 つなので。
        var host = Uri.TryCreate(url, UriKind.Absolute, out var here) ? here.Host : null;
        var targets = _bookmarks.Nodes
            .Where(n => n.IsLink && !n.IsDeleted && n.Url is not null &&
                        (n.Url == url
                         || n.Url == tab.RequestedUrl
                         || (host is not null
                             && Uri.TryCreate(n.Url, UriKind.Absolute, out var b)
                             && b.Host == host)))
            .ToList();
        if (targets.Count == 0)
        {
            Log.Write($"favicon: no bookmark for {Log.Url(url)} (requested {Log.Url(tab.RequestedUrl)})");
            return;
        }

        try
        {
            using var stream = await core.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png);
            if (stream is null) { Log.Write("favicon: stream null"); return; }

            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            // 空や、明らかに大きすぎるものは入れない（bookmarks.json が膨らむ）
            if (buffer.Length == 0 || buffer.Length > 64 * 1024)
            {
                Log.Write($"favicon: size rejected ({buffer.Length} bytes)");
                return;
            }

            var dataUri = "data:image/png;base64," + Convert.ToBase64String(buffer.ToArray());

            var changed = false;
            foreach (var n in targets)
            {
                if (n.Icon == dataUri) continue;
                n.Icon = dataUri;
                n.UpdatedAt = DateTimeOffset.UtcNow;
                changed = true;
            }
            if (!changed) { Log.Write($"favicon: unchanged ({targets.Count} matched)"); return; }

            // 見た目はすぐ直す。保存は静かになってからまとめて 1 回。
            _bar.Reload();   // サイドバーはアイコンを出していないので触らない（開いた枝が閉じる）
            _faviconSave.Stop();
            _faviconSave.Start();
            Log.Write($"favicon: updated {targets.Count} for {Log.Url(url)} ({buffer.Length} bytes)");
        }
        catch (COMException) { /* タブが閉じかけている */ }
        catch (ArgumentException) { /* 壊れた画像 */ }
    }

    private void ApplyContextMenuSetting()
    {
        foreach (var t in _tabs) _ = InstallContextScript(t);
    }

    private void Submit(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return;

        _engine ??= Engines.Default;
        var url = UrlHelper.Parse(text);
        Navigate(_active, url ?? _engine.Ask(text));
    }

    private void GoHome()
    {
        _settingsOpen = false;
        switch (_settings.HomeKind)
        {
            case "ai" when _engine is not null:
                Navigate(_active, _engine.Home);
                return;
            case "google":
                Navigate(_active, "https://www.google.co.jp/");
                return;
            case "custom":
                var url = UrlHelper.Parse(_settings.HomeUrl);
                if (url is not null) { Navigate(_active, url); return; }
                break;
        }

        ShowStart(_active);
    }

    /// <summary>そのタブを内部ページ（スタート画面）に戻す。</summary>
    private void ShowStart(BrowserTab tab)
    {
        tab.Url = null;
        tab.Title = "新しいタブ";
        Render();
    }

    private void OpenPicker()
    {
        _engine = null;
        _settingsOpen = false;
        if (_settings.RememberEngine) { _settings.EngineId = null; _settings.Save(); }
        Render();
    }

    private void ToggleSettings()
    {
        _settingsOpen = !_settingsOpen;
        Render();
    }

    // ---------------------------------------------------------------- 内部ページからのメッセージ

    private void OnUiMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        // 内部ページ以外からは受け取らない（二重の安全策）。
        // NavigateToString で表示したページの Source は about:blank か空になる。
        if (Uri.TryCreate(e.Source, UriKind.Absolute, out var src) &&
            src.Scheme is "http" or "https" or "file" or "ftp")
            return;

        JsonElement msg;
        try { msg = JsonDocument.Parse(e.WebMessageAsJson).RootElement; }
        catch (JsonException) { return; }

        if (msg.ValueKind != JsonValueKind.Object || !msg.TryGetProperty("type", out var typeProp)) return;
        var type = typeProp.GetString();
        string? Str(string name) => msg.TryGetProperty(name, out var v) ? v.GetString() : null;

        switch (type)
        {
            case "pickEngine":
                var picked = Engines.ById(Str("id"));
                if (picked is null) return;
                _engine = picked;
                if (_settings.RememberEngine) _settings.EngineId = picked.Id;
                _settings.Save();
                // ★ ここが Chromium 版のバグ箇所。前のページを残したまま再描画すると
                //    「選び直したのに前の AI に戻る」ので、タブを必ずスタート画面へ戻す。
                ShowStart(_active);
                return;

            // --- ページ内メニューからの操作 ---
            case "clip":
                SetClipboard(Str("text"));
                return;

            case "paste":
                Paste(_uiView.CoreWebView2);
                return;

            case "newTab":
                NewTab();
                return;

            case "home":
                GoHome();
                return;

            case "toggleSettings":
                ToggleSettings();
                return;

            case "picker":
                OpenPicker();
                return;

            case "open":
                var url = Str("url");
                if (url is not null) Navigate(_active, url);
                return;

            case "setHome":
                _settings.HomeKind = Str("kind") ?? "start";
                _settings.Save();
                Render();
                return;

            case "setHomeUrl":
                _settings.HomeUrl = Str("value") ?? "";
                _settings.Save();
                return;

            case "setPageContextMenu":
                _settings.PageContextMenu = msg.TryGetProperty("value", out var pcm) && pcm.ValueKind == JsonValueKind.True;
                _settings.Save();
                ApplyContextMenuSetting();
                return;

            case "setRemember":
                _settings.RememberEngine = msg.TryGetProperty("value", out var b) && b.ValueKind == JsonValueKind.True;
                _settings.EngineId = _settings.RememberEngine ? _engine?.Id : null;
                _settings.Save();
                return;

            case "setEngine":
                _engine = Engines.ById(Str("value"));
                if (_settings.RememberEngine) _settings.EngineId = _engine?.Id;
                _settings.Save();
                Render();
                return;
        }
    }

    // ---------------------------------------------------------------- 右クリックメニュー

    private void ShowContextMenu(WebView2 view, CoreWebView2ContextMenuRequestedEventArgs e)
    {
        // 何より先に、イベントが来たことだけを記録する。
        // これより下で落ちると、メニューも出ずログも残らず「何も起きない」と区別がつかない。
        Log.Write("context menu: raised");

        var target = e.ContextMenuTarget;
        var core = view.CoreWebView2;
        e.Handled = true;

        // SelectionText は Kind が SelectedText のときしか読めない。
        // それ以外で触ると E_ILLEGAL_METHOD_CALL が飛び、Handled を立てた後なので
        // 既定メニューも自前メニューも出ないまま終わる。必ずここを通して読む。
        var sel = target.Kind == CoreWebView2ContextMenuTargetKind.SelectedText
            ? target.SelectionText
            : null;

        // 選択文字列は本文そのもの。長さだけ残す。
        Log.Write($"  kind={target.Kind} link={target.HasLinkUri} editable={target.IsEditable} " +
                  $"selLen={(sel ?? "").Length} loc={e.Location.X},{e.Location.Y} dpi={DeviceDpi}");

        var menu = DarkMenu.Create();

        if (target.HasLinkUri && UrlHelper.IsNavigable(target.LinkUri))
        {
            var link = target.LinkUri;
            menu.Items.Add(DarkMenu.Item("リンクを新しいタブで開く", () => OpenInNewTab(link)));
            menu.Items.Add(DarkMenu.Item("リンクアドレスをコピー", () => SetClipboard(link)));
        }

        if (target.Kind == CoreWebView2ContextMenuTargetKind.Image && target.HasSourceUri)
        {
            var src = target.SourceUri;
            menu.Items.Add(DarkMenu.Item("画像を開く", () => OpenInNewTab(src)));
            menu.Items.Add(DarkMenu.Item("画像アドレスをコピー", () => SetClipboard(src)));
        }

        if (!string.IsNullOrEmpty(sel))
        {
            var text = sel;
            var label = text.Length > 18 ? text[..18] : text;
            menu.Items.Add(DarkMenu.Item("コピー", () => SetClipboard(text)));
            menu.Items.Add(DarkMenu.Item($"「{label}」を検索", () => Submit(text)));
        }

        if (target.IsEditable)
        {
            var text = sel;
            menu.Items.Add(DarkMenu.Item("切り取り", () => Cut(core, text), !string.IsNullOrEmpty(text)));
            menu.Items.Add(DarkMenu.Item("貼り付け", () => Paste(core)));
        }

        Separator(menu);
        menu.Items.Add(DarkMenu.Item("戻る", () => core.GoBack(), core.CanGoBack));
        menu.Items.Add(DarkMenu.Item("進む", () => core.GoForward(), core.CanGoForward));
        menu.Items.Add(DarkMenu.Item("再読み込み", () => core.Reload()));
        menu.Items.Add(DarkMenu.Item("このページのアドレスをコピー", () => SetClipboard(core.Source)));
        Separator(menu);
        menu.Items.Add(DarkMenu.Item("新しいタブ", NewTab));
        menu.Items.Add(DarkMenu.Item("ホーム", GoHome));
        menu.Items.Add(DarkMenu.Item("設定", ToggleSettings));

        // WebView2 の Location は DIP 基準で当てにしづらいので、実際のカーソル位置（画面座標）に出す
        Log.Write($"  items={menu.Items.Count} showing at cursor {Cursor.Position}");
        ShowMenuAtCursor(menu);
    }

    /// <summary>
    /// メニューをカーソル位置に出す。WebView2 にフォーカスがあるまま画面座標で出すと、
    /// 開いた直後にアクティブでなくなって閉じてしまうので、フォームに紐付けて表示する。
    /// </summary>
    private void ShowMenuAtCursor(ContextMenuStrip menu)
    {
        Activate();
        menu.Show(this, PointToClient(Cursor.Position));
        menu.Focus();
    }

    private static void Separator(ContextMenuStrip menu)
    {
        if (menu.Items.Count > 0 && menu.Items[^1] is not ToolStripSeparator)
            menu.Items.Add(new ToolStripSeparator());
    }

    private static void SetClipboard(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try { Clipboard.SetText(text); } catch (ExternalException) { /* 他アプリがクリップボードを掴んでいる */ }
    }

    private static async void Cut(CoreWebView2 core, string? text)
    {
        SetClipboard(text);
        try { await core.ExecuteScriptAsync("document.execCommand('delete')"); } catch (COMException) { }
    }

    private static async void Paste(CoreWebView2 core)
    {
        string text;
        try { text = Clipboard.ContainsText() ? Clipboard.GetText() : ""; }
        catch (ExternalException) { return; }
        if (text.Length == 0) return;

        var json = JsonSerializer.Serialize(text);
        try { await core.ExecuteScriptAsync($"document.execCommand('insertText', false, {json})"); }
        catch (COMException) { }
    }

    // ---------------------------------------------------------------- ショートカット

    /// <summary>
    /// WebView2 内でのキー入力もこの経路に来るので、ショートカットはここで一括処理する。
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        HandleShortcut(keyData & Keys.KeyCode, keyData & Keys.Modifiers, out var handled);
        return handled || base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>WebView2 側から回ってくるキーはこちらに来る。</summary>
    protected override bool ProcessDialogKey(Keys keyData)
    {
        HandleShortcut(keyData & Keys.KeyCode, keyData & Keys.Modifiers, out var handled);
        return handled || base.ProcessDialogKey(keyData);
    }

    // ---------------------------------------------------------------- ブックマーク

    private void ToggleSidebar()
    {
        _sidebar.Visible = !_sidebar.Visible;
        _splitter.Visible = _sidebar.Visible;
        if (_sidebar.Visible) { _sidebar.Reload(); _sidebar.FocusSearch(); }
        UpdateChrome();
    }

    private void ToggleBar()
    {
        _bar.Visible = !_bar.Visible;
        if (_bar.Visible) _bar.Reload();
        UpdateChrome();
    }

    /// <summary>
    /// 開いているページを「未整理」に入れる。
    /// 行き先を尋ねないのは意図的で、保存を1クリックにしないと人は取らなくなる。
    /// 整理はサイドバーで後からやる。
    /// </summary>
    private void AddCurrentPage()
    {
        var url = _active.Url;
        if (string.IsNullOrWhiteSpace(url)) return;

        if (FindByUrl(url) is { } existing)
        {
            // ★ で消していいのは ★ 自身が入れたもの＝「未整理」の直下だけ。
            // 取り込んだフォルダの奥にある1件を黙って消すと事故になるので、
            // そちらは消さずに「どこにあるか」を出すだけにする。
            if (existing.ParentId == BookmarkStore.RootOther)
            {
                _bookmarks.Remove(existing.Id);
                _bookmarks.Save();
                _sidebar.Reload();
                UpdateChrome();
                return;
            }

            if (!_sidebar.Visible) { _sidebar.Visible = true; _splitter.Visible = true; }
            _sidebar.Reveal(url);
            UpdateChrome();
            return;
        }

        var title = string.IsNullOrWhiteSpace(_active.Title) ? UrlHelper.HostTitle(url) : _active.Title;
        _bookmarks.AddLink(BookmarkStore.RootOther, title, url);
        _bookmarks.Save();
        _sidebar.Reload();
        UpdateChrome();
    }

    private BookmarkNode? FindByUrl(string? url) =>
        url is null ? null
        : _bookmarks.Nodes.FirstOrDefault(n => n.IsLink && !n.IsDeleted && n.Url == url);

    private void HandleShortcut(Keys key, Keys modifiers, out bool handled)
    {
        handled = true;
        var ctrl = modifiers.HasFlag(Keys.Control);
        var alt = modifiers.HasFlag(Keys.Alt);
        var core = _active.View?.CoreWebView2;

        switch (key)
        {
            case Keys.T when ctrl: NewTab(); return;
            case Keys.W when ctrl: CloseTab(_active); return;
            case Keys.L when ctrl: _omni.Focus(); _omni.SelectAll(); return;
            case Keys.D when ctrl: AddCurrentPage(); return;
            // Chrome に合わせる。B はバーの表示切替、O がブックマーク一覧。
            case Keys.B when ctrl && modifiers.HasFlag(Keys.Shift): ToggleBar(); return;
            case Keys.O when ctrl && modifiers.HasFlag(Keys.Shift): ToggleSidebar(); return;
            case Keys.R when ctrl: core?.Reload(); return;
            case Keys.F5: core?.Reload(); return;
            case Keys.Left when alt: if (core?.CanGoBack == true) core.GoBack(); return;
            case Keys.Right when alt: if (core?.CanGoForward == true) core.GoForward(); return;
            case Keys.Tab when ctrl:
                if (_tabs.Count > 1)
                    SelectTab(_tabs[(_tabs.IndexOf(_active) + 1) % _tabs.Count]);
                return;
            default: handled = false; return;
        }
    }
}
