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
    };
    private Button _back = null!, _fwd = null!, _reload = null!, _home = null!, _engineBtn = null!, _settingsBtn = null!;
    private Button _star = null!, _bmBtn = null!, _newTabBtn = null!;

    /// <summary>吹き出しは 1 つを使い回す。言語を切り替えたときに差し替えるため。</summary>
    private readonly ToolTip _tips = new();

    /// <summary>右上のボタン置き場。拡大率を変えたときに幅が足りなくなる件の計測用に持っておく。</summary>
    private FlowLayoutPanel _topRight = null!;

    private readonly BookmarkStore _bookmarks = BookmarkStore.Load();
    private BookmarkSidebar _sidebar = null!;
    private BookmarkBar _bar = null!;
    private readonly Splitter _splitter = new()
    {
        // MinSize はサイドバーの下限幅。既定幅と同じ 220 にしていたので、
        // 広げることはできても狭めることが一度もできなかった。
        Dock = DockStyle.Left, Width = 5, BackColor = Theme.Border, MinExtra = 360, MinSize = 120,
        // 既定の VSplit（←||→）は XOR マスクで背景を反転して描く古い形式で、
        // 暗い背景や高い拡大率だと黒い塊に潰れる。システムのテーマから描かれる
        // SizeWE（↔）に替える。Chrome も VS Code もこちらを使っている。
        Cursor = Cursors.SizeWE,
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

    /// <summary>「Voyager について」を開いているか。</summary>
    private bool _aboutOpen;

    /// <summary>いま出している右クリックメニュー。ページが動いたら畳むために持っておく。</summary>
    private ContextMenuStrip? _menu;

    /// <summary>利用者がアドレス欄を編集中かどうか（編集中だけ自動更新を止める）。</summary>
    private bool _omniEditing;
    /// <summary>プログラムから _omni.Text を書き換えている最中のフラグ。</summary>
    private bool _omniSyncing;
    /// <summary>アドレス欄に入ってきた直後かどうか。最初の 1 クリックだけ全選択するのに使う。</summary>
    private bool _omniFresh;

    /// <summary>内部ページ専用。外部サイトは絶対にここに読み込まない。</summary>
    private readonly WebView2 _uiView = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Theme.Background };

    public MainForm()
    {
        // 画面を組む前に言語を決める。ここから後に作るものは全部これを見る。
        Strings.Use(_settings.Language);
        Log.Write($"language: setting={_settings.Language} -> {Strings.Current} " +
                  $"(Windows {System.Globalization.CultureInfo.CurrentUICulture.Name})");

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
        _sidebar = new BookmarkSidebar(_bookmarks) { Visible = _settings.SidebarOpen, LogicalWidth = _settings.SidebarWidth };
        _splitter.Visible = _settings.SidebarOpen;
        _sidebar.ManageRequested += OpenBookmarkManager;
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
        _bar.AddPageRequested += at => ShowBookmarkPopup(BookmarkStore.RootBar, at);
        // 片方が書き換えたら、もう片方も描き直す。同じストアを 2 つの画面が見ているため。
        _bar.StoreChanged += () => { _sidebar.Reload(); RefreshManager(); };
        _sidebar.StoreChanged += () => { _bar.Reload(); RefreshManager(); };

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
        _topRight = topRight;
        var newTab = IconButton("+", Strings.NewTabTip, (_, _) => NewTab());
        _newTabBtn = newTab;
        _engineBtn = TextButton("AI", Strings.PickAiTip, (_, _) => OpenPicker());
        _settingsBtn = TextButton(Strings.Settings, Strings.Settings, (_, _) => ToggleSettings());
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
        _back = IconButton("‹", Strings.BackTip, (_, _) => Active()?.CoreWebView2?.GoBack());
        _fwd = IconButton("›", Strings.ForwardTip, (_, _) => Active()?.CoreWebView2?.GoForward());
        _reload = IconButton("↻", Strings.ReloadTip, (_, _) => Active()?.CoreWebView2?.Reload());
        _home = IconButton("⌂", Strings.Home, (_, _) => GoHome());
        _star = IconButton("☆", Strings.AddBookmarkTip, (_, _) => AddCurrentPage());
        _bmBtn = IconButton("▤", Strings.BookmarksTip, (_, _) => ToggleSidebar());
        navLeft.Controls.AddRange([_back, _fwd, _reload, _home, _star, _bmBtn]);

        var omniHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 9, 10, 9), BackColor = Theme.Surface };
        _omni.Dock = DockStyle.Fill;
        _omni.PlaceholderText = Strings.OmniPlaceholder;
        _omni.TabStop = false;   // 起動直後にここへフォーカスが来ないように
        _omni.TextChanged += (_, _) => { if (!_omniSyncing) _omniEditing = true; };
        // 追跡用の引数は、見ているあいだだけ畳む。触ったら元の全体に戻す（直す・コピーするのは本物）。
        _omni.Leave += (_, _) =>
        {
            _omniEditing = false;
            _omniFresh = false;
            if (IsHandleCreated && !Disposing && !IsDisposed) BeginInvoke(() => { if (!IsDisposed) UpdateChrome(); });
        };

        // よそからアドレス欄に入ってきた最初の 1 クリックだけ、URL を丸ごと選ぶ。
        // 2 回目からは普通にキャレットが置けるので、URL の一部だけ直すのも邪魔しない。
        //
        // MouseDown ではなく MouseUp で選ぶ理由：押した時点で選んでも、その後
        // TextBox の既定の処理が離した位置にキャレットを置き直して選択が消える。
        _omni.Enter += (_, _) => { _omniFresh = true; ShowFullUrl(); };
        _omni.MouseUp += (_, _) =>
        {
            if (!_omniFresh) return;
            _omniFresh = false;
            if (_omni.TextLength == 0) return;
            _omni.SelectAll();
            Log.Write("omni: select all (first click)");
        };

        _omni.ContextMenuStrip = BuildOmniMenu();

        _omni.KeyDown += (_, e) =>
        {
            _omniFresh = false;   // キーを打ち始めたらもう「入ってきた直後」ではない
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

    private Button Base(string text, string tip, EventHandler onClick, int width)
    {
        var b = new Button
        {
            Text = text,
            // 幅を画素で決め打つと、字だけ拡大率どおりに大きくなって枠から溢れる
            // （192 dpi で Perplexity が切れていたのがこれ）。字を測らせて、
            // 96 dpi ぶんを下限に置く。エンジン名の長さの違いにも同時に効く。
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(width, 30),
            Padding = new Padding(6, 2, 6, 2),
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
        _tips.SetToolTip(b, tip);
        return b;
    }

    private Button IconButton(string text, string tip, EventHandler onClick) => Base(text, tip, onClick, 34);

    /// <summary>
    /// 言語を切り替えたときに、もう作ってしまった画面の文字を入れ直す。
    /// メニューと内部ページは開くたびに作り直すので、ここで面倒を見るのは
    /// 一度きりしか文字を入れていないもの（ボタン・吹き出し・案内文）だけでいい。
    /// </summary>
    private void ApplyStrings()
    {
        _omni.PlaceholderText = Strings.OmniPlaceholder;
        _settingsBtn.Text = Strings.Settings;

        _tips.SetToolTip(_newTabBtn, Strings.NewTabTip);
        _tips.SetToolTip(_engineBtn, Strings.PickAiTip);
        _tips.SetToolTip(_settingsBtn, Strings.Settings);
        _tips.SetToolTip(_back, Strings.BackTip);
        _tips.SetToolTip(_fwd, Strings.ForwardTip);
        _tips.SetToolTip(_reload, Strings.ReloadTip);
        _tips.SetToolTip(_home, Strings.Home);
        _tips.SetToolTip(_star, Strings.AddBookmarkTip);
        _tips.SetToolTip(_bmBtn, Strings.BookmarksTip);

        // 中身を持たないタブの題名（「新しいタブ」）も入れ直す。
        foreach (var t in _tabs)
            if (t.Url is null) t.Title = t.Page == BookmarkManager.PageId ? Strings.BookmarkManager : Strings.NewTab;

        var omni = _omni.Width;
        LayoutTabs();
        UpdateChrome();
        LogChromeWidths($"language {Strings.Current}");
        Log.Write($"strings: {Strings.Current} settings=\"{_settingsBtn.Text}\" " +
                  $"settingsW={_settingsBtn.Width} aiW={_engineBtn.Width} omniW={omni}->{_omni.Width}");
    }

    private Button TextButton(string text, string tip, EventHandler onClick)
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
                Language = BrowserLanguage(),
                AllowSingleSignOnUsingOSPrimaryAccount = false,
            };
            Log.Write($"package: {AppPaths.PackageFamilyName ?? "none (MSI)"}");
            Log.Write($"env create: userData={AppSettings.UserDataDir}");
            _env = await CoreWebView2Environment.CreateAsync(null, AppSettings.UserDataDir, options);
            Log.Write($"env ok: browser={_env.BrowserVersionString}");

            await _uiView.EnsureCoreWebView2Async(_env);
            var core = _uiView.CoreWebView2;
            PruneHistory(core.Profile);
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
                {
                    a.Cancel = true;
                    return;
                }

                // 内部ページの履歴はアプリの状態と合っていない。マウスの「戻る」ボタンなどで
                // WebView2 が自分で前の内部ページへ戻ると、マネージャーのタブなのに AI の画面が出る。
                // 履歴移動は止めて、アプリとしての「戻る」に置き換える。
                if (a.NavigationKind == CoreWebView2NavigationKind.BackOrForward)
                {
                    a.Cancel = true;
                    Log.Write("internal page: back/forward blocked");
                    BeginInvoke(InternalBack);
                }
            };
            RestoreLastTabs();
            Render();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Strings.WebView2InitFailed(ex.Message),
                App.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    /// <summary>
    /// 上段の幅の計測。200% や英語表示でボタンが切れないかを、画面写真なしで判定するためのもの。
    ///
    /// 以前は「子の合計 &lt;= パネル幅」で fits を出していたが、topRight は
    /// Dock=Right + AutoSize なので、パネル幅は子の合計そのもの。あれは常に yes になる
    /// 同語反復だった。見るべきは上段のどこまでをボタンが食べ、タブ列に何画素残ったか。
    ///
    /// clip は、いちばん右のボタン（設定 / Settings）の右端が上段の外へ出ていないか。
    /// tabs は残った幅。ここが 1 タブぶん（92 論理画素）を割ると、タブが潰れ始める。
    /// </summary>
    private void LogChromeWidths(string when)
    {
        if (_topRight is null || _settingsBtn is null) return;

        var bar = _topBar.ClientSize.Width;
        var right = _topRight.Width;                       // ボタン群が食べた幅
        var tabs = _tabStrip.ClientSize.Width;             // タブ列に残った幅
        var edge = _topRight.Left + _settingsBtn.Right;    // 右端ボタンの右辺（上段の座標で）
        var room = Scale(92);                              // タブ 1 個分の下限

        Log.Write($"topbar {when}: dpi={DeviceDpi} lang={Strings.Current} bar={bar} right={right} tabs={tabs} " +
                  $"+={_newTabBtn.Width} ai={_engineBtn.Width}(\"{_engineBtn.Text}\") " +
                  $"set={_settingsBtn.Width}(\"{_settingsBtn.Text}\") " +
                  $"edge={edge} clip={(edge > bar ? "YES" : "no")} " +
                  $"tabsOk={(tabs >= room ? "yes" : "NO")}");
    }

    /// <summary>96 dpi 基準の値を、いまの画面の画素数に直す。</summary>
    private int Scale(int logical) => (int)Math.Round(logical * DeviceDpi / 96.0);

    /// <summary>
    /// 画素で直書きしてある枠の寸法を、いまの拡大率に合わせる。
    /// WinForms はここを直してくれないので、200% だとバーが字を切り、
    /// スプリッタの掴み代が半分になる。
    /// </summary>
    private void ApplyChromeDpi()
    {
        _topBar.Height = Scale(40);
        _navBar.Height = Scale(48);
        _splitter.Width = Scale(5);
        _splitter.MinSize = Scale(120);
        _splitter.MinExtra = Scale(360);
        _tabStrip.Padding = new Padding(Scale(8), 0, 0, 0);
        _topRight.Padding = new Padding(0, Scale(5), Scale(8), 0);
        LayoutTabs();

        // メニューの字は作った時点の拡大率で実体が固まる。作り直す。
        var old = _omni.ContextMenuStrip;
        _omni.ContextMenuStrip = BuildOmniMenu();
        old?.Dispose();
    }

    /// <summary>
    /// アドレス欄の右クリックメニュー。既定のままだと Windows の明るいメニューが出て、
    /// ここだけアプリの配色から浮く。顔ぶれは既定と同じものを並べ直しただけで、
    /// 「Unicode 制御文字の挿入」のような普段使わない項目は落としてある。
    ///
    /// ページ側と違ってフォーカスの小細工は要らない。WebView2 ではなく
    /// ただの TextBox なので、ContextMenuStrip を差すだけで素直に出る。
    /// </summary>
    private ContextMenuStrip BuildOmniMenu()
    {
        var menu = DarkMenu.Create(DeviceDpi);

        var undo = DarkMenu.Item(Strings.Undo, () => { if (_omni.CanUndo) _omni.Undo(); });
        var cut = DarkMenu.Item(Strings.Cut, () => _omni.Cut());
        var copy = DarkMenu.Item(Strings.Copy, () => _omni.Copy());
        var paste = DarkMenu.Item(Strings.Paste, () => _omni.Paste());
        var del = DarkMenu.Item(Strings.Delete, () => { if (_omni.SelectionLength > 0) _omni.SelectedText = ""; });
        var all = DarkMenu.Item(Strings.SelectAll, () => _omni.SelectAll());

        menu.Items.AddRange(new ToolStripItem[]
        {
            undo, new ToolStripSeparator(), cut, copy, paste, del, new ToolStripSeparator(), all,
        });

        // 出す直前に、押せるものだけ押せるようにする。灰色のまま並んでいる方が
        // 「いま何ができるか」が読めるので、項目そのものは隠さない。
        menu.Closed += (_, _) => { if (!_omni.Focused) UpdateChrome(); };
        menu.Opening += (_, _) =>
        {
            ShowFullUrl();
            var sel = _omni.SelectionLength > 0;
            undo.Enabled = _omni.CanUndo;
            cut.Enabled = sel;
            copy.Enabled = sel;
            del.Enabled = sel;
            all.Enabled = _omni.TextLength > 0 && _omni.SelectionLength < _omni.TextLength;
            paste.Enabled = ClipboardHasText(out var unreadable);
            Log.Write($"omni menu: dpi={DeviceDpi} sel={_omni.SelectionLength}/{_omni.TextLength} " +
                      $"undo={undo.Enabled} paste={(unreadable ? "err" : paste.Enabled.ToString())}");
        };

        return menu;
    }

    /// <summary>
    /// クリップボードに文字があるか。
    ///
    /// Windows のクリップボードは一度に 1 プロセスしか開けない。他のアプリが掴んでいる
    /// 一瞬に当たると例外になるので、少し待って 3 回試す。一度で諦めると
    /// 「中身はあるのに貼り付けが灰色」が時々起きる。
    ///
    /// それでも読めなければ伏せる（押せて何も起きない方が困る）。読めなかったことは
    /// <paramref name="unreadable"/> で返し、ログでは「空」と区別する。
    /// </summary>
    private static bool ClipboardHasText(out bool unreadable)
    {
        for (var i = 0; i < 3; i++)
        {
            try
            {
                unreadable = false;
                return Clipboard.ContainsText();
            }
            catch
            {
                Thread.Sleep(30);
            }
        }
        unreadable = true;
        return false;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ApplyChromeDpi();
        LogChromeWidths("shown");
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        // 直後はまだ再配置の途中のことがある。落ち着いてから直して測る。
        BeginInvoke(() =>
        {
            ApplyChromeDpi();
            LogChromeWidths("dpi changed");
        });
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
        if (_sidebar.Visible) _settings.SidebarWidth = _sidebar.LogicalWidth;   // 96 dpi 基準で残す
        _settings.BarVisible = _bar.Visible;
        SaveTabsForNextTime();
        _settings.Save();
        _faviconSave.Stop();   // 直後に自分で書くので、二重に書かせない
        _bookmarks.Save();
        _linkCheck?.Stop();
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

        var showInternal = _settingsOpen || _aboutOpen || _engine is null || _active.Url is null || _active.Page is not null;

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
                LogHtml(_aboutOpen ? Pages.About(_env?.BrowserVersionString)
                        : _settingsOpen ? Pages.Settings(_settings, _engine, HistoryKeepDays, _historyNote)
                        : _active.Page == BookmarkManager.PageId ? Pages.Bookmarks()
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

    /// <summary>
    /// アドレス欄を、畳んでいない元のアドレスにする。入ってきたときと右クリックのとき。
    /// 打ち込み中は触らない。
    /// </summary>
    private void ShowFullUrl()
    {
        if (_omniEditing || _active.Url is not { } url || _omni.Text == url) return;
        _omniSyncing = true;
        _omni.Text = url;
        _omniSyncing = false;
    }

    private static string LogHtml(string html)
    {
        Log.Write($"  html generated: {html.Length} chars, head={html[..Math.Min(60, html.Length)].Replace("\n", " ")}");
        return html;
    }

    private void UpdateChrome()
    {
        _engineBtn.Text = _engine?.Name ?? Strings.PickAi;
        _settingsBtn.ForeColor = _settingsOpen ? Theme.Accent : Theme.Text;

        // 利用者が打ち込んでいる最中だけ自動更新を止める（フォーカスの有無では判定しない。
        // 起動直後からフォーカスを持っていると、一度も URL が入らなくなる）。
        // 代入しただけでは横スクロールが右端に残り、長い URL は末尾しか見えないので先頭へ戻す。
        var url = _active.Url ?? "";
        var shown = _omni.Focused ? url : UrlHelper.WithoutTracking(url);
        if (!_omniEditing && _omni.Text != shown)
        {
            _omniSyncing = true;
            _omni.Text = shown;
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
            item.DragMoved += (_, screenX) => DragTab(captured, screenX);
            item.DragEnded += _ => EndTabDrag(captured);
            tab.Item = item;
            _tabStrip.Controls.Add(item);
        }
        _tabStrip.ResumeLayout();
        LayoutTabs();
        UpdateChrome();
    }

    /// <summary>ドラッグを始めたときの位置（ログ用）。</summary>
    private int _tabDragFrom = -1;

    /// <summary>
    /// ドラッグ中のタブを、マウスの位置に合わせてその場で並べ替える。
    /// ほかのタブの真ん中を越えたら入れ替わる（Chrome と同じ）。
    /// 作り直さずに並び順だけ動かすので、つかんでいるタブはそのままマウスを掴み続ける。
    /// </summary>
    private void DragTab(BrowserTab tab, int screenX)
    {
        if (tab.Item is null) return;
        var current = _tabs.IndexOf(tab);
        if (current < 0) return;
        if (_tabDragFrom < 0) _tabDragFrom = current;

        var x = _tabStrip.PointToClient(new Point(screenX, 0)).X;
        var target = _tabs.Count(t => !ReferenceEquals(t, tab) && t.Item is not null && t.Item.Left + t.Item.Width / 2 < x);
        if (target == current) return;

        _tabs.RemoveAt(current);
        _tabs.Insert(target, tab);
        _tabStrip.Controls.SetChildIndex(tab.Item, target);
    }

    private void EndTabDrag(BrowserTab tab)
    {
        var to = _tabs.IndexOf(tab);
        if (_tabDragFrom >= 0 && _tabDragFrom != to) Log.Write($"tabs: moved from {_tabDragFrom + 1} to {to + 1} of {_tabs.Count}");
        _tabDragFrom = -1;
    }

    private void LayoutTabs()
    {
        if (_tabs.Count == 0) return;
        // 92/180 は 96 dpi 基準の下限と上限。生の画素で挟むと、200% の画面では
        // 上限 180 が実質 90 相当になり、題名が「新しい…」で切れる。
        var available = Math.Max(Scale(120), _tabStrip.ClientSize.Width - Scale(12));
        var width = Math.Clamp(available / _tabs.Count - Scale(6), Scale(92), Scale(180));
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
        // タブを押した場合は外側クリックで勝手に閉じるが、キーボードで移ると残る。
        CloseMenu();
        _active = tab;
        CloseInternalPages();
        Render();
        LoadIfDeferred(tab);
    }

    private void NewTab()
    {
        var tab = new BrowserTab();
        _tabs.Add(tab);
        _active = tab;
        CloseInternalPages();
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
        LoadIfDeferred(_active);
    }

    // ---------------------------------------------------------------- 前回のタブ

    /// <summary>
    /// サイトに伝える言語（WebView2 を作るときに 1 回だけ決まる）。
    /// "auto" なら Windows の表示言語のまま。
    /// </summary>
    private string BrowserLanguage() => _settings.Language switch
    {
        "ja" => "ja-JP",
        "en" => "en-US",
        _ => System.Globalization.CultureInfo.CurrentUICulture.Name is { Length: > 0 } name ? name : "en-US",
    };

    /// <summary>閉じるとき、開いていたタブを覚える。オフなら何も残さない。</summary>
    private void SaveTabsForNextTime()
    {
        if (!_settings.RestoreTabs) { _settings.LastTabs = []; _settings.LastActiveTab = 0; return; }
        var pages = _tabs.Where(t => t.Page is null && UrlHelper.IsNavigable(t.Url)).ToList();
        _settings.LastTabs = pages.Select(t => new SavedTab(t.Url!, t.Title)).ToList();
        _settings.LastActiveTab = Math.Max(0, pages.IndexOf(_active));
        Log.Write($"session: saved {pages.Count} tabs");
    }

    /// <summary>
    /// 起動したとき、前回のタブを並べ直す。前面のタブだけすぐ開き、ほかは選ばれたときに開く
    /// （何十枚もあると、起動直後に全部が一斉に読み込みを始めて重くなるため）。
    /// </summary>
    private void RestoreLastTabs()
    {
        if (!_settings.RestoreTabs || _settings.LastTabs is not { Count: > 0 } saved) return;
        var list = saved.Where(s => UrlHelper.IsNavigable(s.Url)).Take(100).ToList();
        if (list.Count == 0) return;

        for (var i = 0; i < list.Count; i++)
        {
            var tab = i == 0 ? _tabs[0] : new BrowserTab();
            if (i > 0) _tabs.Add(tab);
            tab.Url = list[i].Url;
            tab.Title = string.IsNullOrWhiteSpace(list[i].Title) ? UrlHelper.HostTitle(list[i].Url) : list[i].Title!;
        }
        _active = _tabs[Math.Clamp(_settings.LastActiveTab, 0, list.Count - 1)];
        RebuildTabStrip();
        Log.Write($"session: restored {list.Count} tabs (front {_tabs.IndexOf(_active) + 1})");
        Navigate(_active, _active.Url!);
    }

    /// <summary>前回のタブで、まだ開いていない（選ばれていない）ものなら、ここで開く。</summary>
    private void LoadIfDeferred(BrowserTab tab)
    {
        if (tab.View is null && tab.Page is null && tab.Url is { } url && UrlHelper.IsNavigable(url))
            Navigate(tab, url);
    }

    private void OpenInNewTab(string url)
    {
        if (!UrlHelper.IsNavigable(url)) return;
        var tab = new BrowserTab();
        _tabs.Add(tab);
        _active = tab;
        CloseInternalPages();
        RebuildTabStrip();
        Navigate(tab, url);
    }

    // ---------------------------------------------------------------- ナビゲーション

    /// <param name="focusPage">開いたあと、ページにフォーカスを移す（アドレス欄から開いたとき）。</param>
    private async void Navigate(BrowserTab tab, string url, bool focusPage = false)
    {
        if (!UrlHelper.IsNavigable(url)) return;

        tab.Url = url;
        tab.Page = null;
        tab.RequestedUrl = url;
        tab.RequestPending = true;
        tab.Title = UrlHelper.HostTitle(url);
        CloseInternalPages();
        _omniEditing = false;

        try
        {
            await EnsureView(tab);
            tab.View!.CoreWebView2.Navigate(url);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(Strings.PageOpenFailed(ex.Message), App.Name,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            tab.Url = null;
        }
        Render();

        // Chrome と同じく、アドレス欄で Enter を押したらページへフォーカスを移す。
        // アドレス欄にカーソルが残ったままだと、追跡用の引数を畳んだ表示にならない。
        if (focusPage && ReferenceEquals(tab, _active) && tab.View is { Visible: true } view)
        {
            view.Focus();
            Log.Write("omni: focus moved to the page");
        }
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
            // サイトの背景は Chrome と同じ白。ページが背景色を指定していない所はこの色で塗られる。
            // 以前は内部ページと同じ暗い色にしていたが、Yahoo! JAPAN のように中央の記事だけに
            // 色を付けるサイトでは左右に暗い帯が出て、広い画面ほど「2/3 しか表示されない」ように見えた。
            // 内部ページ（_uiView）は暗いまま。
            DefaultBackgroundColor = Color.White,
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

        ApplyDownloadDir(core);
        core.DownloadStarting += (_, a) =>
        {
            // 非同期ラムダではないが、ここで例外を落とすと WebView2 側で握り潰される。
            try { OnDownloadStarting(a); }
            catch (Exception ex) { Log.Write($"!! download failed: {ex.GetType().Name} {ex.Message}"); }
        };

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
            // ハッシュだけの移動（Yahoo! の画像検索など）は NavigationStarting が来ない。
            // こちらは必ず来るので、メニューを畳むのはここでも見る。
            CloseMenu();
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
            CloseMenu();
            // http / https 以外（file: など）は開かない
            if (!UrlHelper.IsNavigable(a.Uri)) { a.Cancel = true; return; }
            TrackRequested(tab, a);
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
    /// RequestedUrl を、こちらが指示した遷移とそのリダイレクトの間だけ生かす。
    ///   リダイレクト              → そのまま（twitter.com → x.com を追うため）
    ///   指示した遷移の最初の 1 回 → 印を下ろすだけ
    ///   それ以外の新しい遷移      → リンク・戻る・再読み込みなど。消す
    /// </summary>
    private static void TrackRequested(BrowserTab tab, CoreWebView2NavigationStartingEventArgs a)
    {
        if (a.IsRedirected) return;
        if (tab.RequestPending) { tab.RequestPending = false; return; }
        if (tab.RequestedUrl is null) return;
        Log.Write($"requested cleared: new navigation to {Log.Url(a.Uri)}");
        tab.RequestedUrl = null;
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
        Navigate(_active, url ?? _engine.Ask(text), focusPage: true);
    }

    private void GoHome()
    {
        CloseInternalPages();
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
        tab.Page = null;
        tab.Title = Strings.NewTab;
        Render();
    }

    private void OpenPicker()
    {
        _engine = null;
        CloseInternalPages();
        if (_settings.RememberEngine) { _settings.EngineId = null; _settings.Save(); }
        Render();
    }

    private void ToggleSettings()
    {
        var open = !_settingsOpen;
        CloseInternalPages();
        _settingsOpen = open;
        if (open) _historyNote = null;   // 前に開いたときの「消しました」は持ち越さない
        Render();
    }

    private void ToggleAbout()
    {
        var open = !_aboutOpen;
        CloseInternalPages();
        _aboutOpen = open;
        Render();
    }

    /// <summary>
    /// 内部ページを全部閉じる。1 つずつ false にして回ると、画面が増えたときに
    /// 必ずどこかで付け忘れる。閉じる処理はここ 1 か所だけにしておく。
    /// </summary>
    private void CloseInternalPages()
    {
        _settingsOpen = false;
        _aboutOpen = false;
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

        if (type?.StartsWith("bm:", StringComparison.Ordinal) == true)
        {
            OnManagerMessage(type, msg);
            return;
        }

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

            case "about":
                ToggleAbout();
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

            case "setRestoreTabs":
                _settings.RestoreTabs = msg.TryGetProperty("value", out var rt) && rt.ValueKind == JsonValueKind.True;
                if (!_settings.RestoreTabs) { _settings.LastTabs = []; _settings.LastActiveTab = 0; }
                _settings.Save();
                Log.Write($"session: restore tabs {(_settings.RestoreTabs ? "on" : "off")}");
                return;

            case "setPageContextMenu":
                _settings.PageContextMenu = msg.TryGetProperty("value", out var pcm) && pcm.ValueKind == JsonValueKind.True;
                _settings.Save();
                ApplyContextMenuSetting();
                return;

            case "clearHistory":
                ClearHistory();
                return;

            case "pickDownloadDir":
                PickDownloadDir();
                return;

            case "resetDownloadDir":
                _settings.DownloadDir = "";
                _settings.Save();
                Render();
                return;

            case "setAskDownloadDir":
                _settings.AskDownloadDir = msg.TryGetProperty("value", out var ask) && ask.ValueKind == JsonValueKind.True;
                _settings.Save();
                return;

            case "setRemember":
                _settings.RememberEngine = msg.TryGetProperty("value", out var b) && b.ValueKind == JsonValueKind.True;
                _settings.EngineId = _settings.RememberEngine ? _engine?.Id : null;
                _settings.Save();
                return;

            case "setLanguage":
                _settings.Language = Str("value") switch { "en" => "en", "ja" => "ja", _ => "auto" };
                _settings.Save();
                Strings.Use(_settings.Language);
                ApplyStrings();
                Render();
                return;

            case "setEngine":
                _engine = Engines.ById(Str("value"));
                if (_settings.RememberEngine) _settings.EngineId = _engine?.Id;
                _settings.Save();
                Render();
                return;
        }
    }

    // ---------------------------------------------------------------- 閲覧履歴

    /// <summary>WebView2 の閲覧履歴を残す日数。これより古い分は起動のたびに消す。</summary>
    private const int HistoryKeepDays = 7;

    /// <summary>設定画面に出す「消しました」の一言。設定を開き直すと消える。</summary>
    private string? _historyNote;

    /// <summary>
    /// WebView2（中の Chromium）は、開いたページを自分で記録している
    /// （データフォルダの EBWebView\Default\History）。Voyager の画面からは見えず、
    /// CCleaner などの掃除ソフトも Voyager のことは知らないので、放っておくと溜まり続ける。
    /// 起動のたびに古い分を消す。起動は待たせず、結果はログにだけ残す。
    /// </summary>
    private static async void PruneHistory(CoreWebView2Profile profile)
    {
        try
        {
            var now = DateTime.Now;
            await profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.BrowsingHistory,
                now.AddYears(-30), now.AddDays(-HistoryKeepDays));
            Log.Write($"history: removed entries older than {HistoryKeepDays} days");
        }
        catch (Exception ex) { Log.Write($"history: prune failed: {ex.GetType().Name} {ex.Message}"); }
    }

    /// <summary>設定の［閲覧履歴を消す］。ログイン状態と Cookie は消さない。</summary>
    private async void ClearHistory()
    {
        if (_uiView.CoreWebView2 is not { } core) return;
        var answer = MessageBox.Show(this, Strings.ClearHistoryConfirm, App.Name,
            MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.OK) return;

        try
        {
            await core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.BrowsingHistory);
            _historyNote = Strings.HistoryCleared(DateTime.Now);
            Log.Write("history: cleared all");
        }
        catch (Exception ex)
        {
            _historyNote = Strings.HistoryClearFailed(ex.Message);
            Log.Write($"history: clear failed: {ex.GetType().Name} {ex.Message}");
        }
        if (_settingsOpen) Render();
    }

    // ---------------------------------------------------------------- ダウンロード

    /// <summary>設定した保存先を WebView2 に渡す。空のままなら Windows の既定を使う。</summary>
    private void ApplyDownloadDir(CoreWebView2 core)
    {
        var dir = _settings.DownloadDir;
        if (string.IsNullOrWhiteSpace(dir)) return;

        if (!Directory.Exists(dir))
        {
            // 外付けを抜いた後などに起こる。既定に戻して続ける（落とさない）。
            Log.Write($"download dir missing: {dir}");
            return;
        }
        try { core.Profile.DefaultDownloadFolderPath = dir; }
        catch (ArgumentException) { /* 使えない場所なら既定のまま */ }
        catch (NotImplementedException) { /* 古いランタイム */ }
    }

    private void ApplyDownloadDir()
    {
        foreach (var t in _tabs)
            if (t.View?.CoreWebView2 is { } c) ApplyDownloadDir(c);
    }

    /// <summary>
    /// ダウンロードが始まるところ。「毎回たずねる」が入っているときだけ割り込む。
    ///
    /// ダイアログを出している間 WebView2 を待たせるので、Deferral を必ず取る。
    /// 取らずに待たせると、こちらが答えを出す前に既定の動作で走り出す。
    /// </summary>
    private void OnDownloadStarting(CoreWebView2DownloadStartingEventArgs a)
    {
        var name = Path.GetFileName(a.ResultFilePath);

        // URL は丸ごと残さない。画像 CDN の配信 URL はパスに 200 文字級の使い捨て鍵が
        // 入っていることがあり、Log.Url() はクエリしか伏せないので素通りしてしまう。
        // 不具合を追うのに要るのは「どこから何を」までで、その先は要らない。
        var host = Uri.TryCreate(a.DownloadOperation.Uri, UriKind.Absolute, out var from) ? from.Host : "?";
        Log.Write($"download: {host} name={name}");

        // 「画像を保存」の見張りに、ちゃんと来たことを知らせる
        if (_imageSaveAt != 0)
        {
            Log.Write($"  image save: download started after {Environment.TickCount64 - _imageSaveAt}ms");
            _imageSaveAt = 0;
        }

        if (!_settings.AskDownloadDir)
        {
            Log.Write($"  -> {Path.GetDirectoryName(a.ResultFilePath)}");
            return;
        }

        using var deferral = a.GetDeferral();
        a.Handled = true;   // 自分で訊くので、WebView2 の既定のダウンロード UI は出さない

        using var dlg = new SaveFileDialog
        {
            Title = Strings.SaveTo,
            FileName = name,
            InitialDirectory = LastOrPreferredDir(),
            OverwritePrompt = true,
        };

        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            a.ResultFilePath = dlg.FileName;
            Log.Write($"  -> {Path.GetDirectoryName(dlg.FileName)}");

            RememberSaveDir(dlg.FileName);
        }
        else
        {
            a.Cancel = true;
            Log.Write("  cancelled");
        }
    }

    /// <summary>
    /// 「毎回たずねる」のダイアログを開く場所。前回選んだフォルダが残っていればそこ、
    /// 無ければ設定の保存先。
    /// </summary>
    private string LastOrPreferredDir()
    {
        var last = _settings.LastSaveDir;
        if (!string.IsNullOrWhiteSpace(last) && Directory.Exists(last))
        {
            Log.Write("  save dialog: last folder");
            return last;
        }
        Log.Write("  save dialog: default folder");
        return PreferredDownloadDir();
    }

    /// <summary>次の保存ウィンドウはここから開く。</summary>
    private void RememberSaveDir(string file)
    {
        var chosen = Path.GetDirectoryName(file);
        if (string.IsNullOrEmpty(chosen) || chosen == _settings.LastSaveDir) return;
        _settings.LastSaveDir = chosen;
        _settings.Save();
    }

    /// <summary>
    /// 画像を自分で保存する。押したらすぐ保存ウィンドウを出し、取得はその裏で始めておく。
    /// URL に画像の拡張子が無いときは、種類を知るために取得を最大 1 秒だけ待つ。
    /// 間に合わなければ拡張子なしでウィンドウを出し、書くときに中身から判別して足す。
    /// </summary>
    private async void SaveImageOwn(CoreWebView2 core, string url, string? referer)
    {
        var started = Environment.TickCount64;
        var fetch = ImageSaver.FetchAsync(core, url, referer);
        _ = fetch.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);

        try
        {
            var name = ImageSaver.NameFromUrl(url, out var hasExt);
            var ext = hasExt ? Path.GetExtension(name).TrimStart('.') : null;
            if (!hasExt && await Task.WhenAny(fetch, Task.Delay(1000)) == fetch)
            {
                var r = await fetch;   // 失敗していればここで投げて、ウィンドウを出さずに知らせる
                ext = ImageSaver.ExtOf(r);
                if (ext is not null) name += "." + ext;
            }

            using var dlg = new SaveFileDialog
            {
                Title = Strings.SaveImage,
                FileName = name,
                InitialDirectory = LastOrPreferredDir(),
                OverwritePrompt = true,
            };
            if (ext is not null)
            {
                dlg.Filter = $"{ext.ToUpperInvariant()} (*.{ext})|*.{ext}|*.*|*.*";
                dlg.DefaultExt = ext;
            }

            Log.Write($"  image save: dialog after {Environment.TickCount64 - started}ms name={name}");
            if (dlg.ShowDialog(this) != DialogResult.OK)
            {
                Log.Write("  image save: cancelled");
                return;
            }
            RememberSaveDir(dlg.FileName);

            var result = await fetch;
            var path = dlg.FileName;

            // 拡張子なしで訊いた（1 秒で種類が分からなかった）なら、ここで足す。
            // 足した名前が既にあれば上書きせず、(1)、(2) … と避ける。
            if (Path.GetExtension(path).Length == 0 && ImageSaver.ExtOf(result) is { } late)
            {
                path = UniquePath(path + "." + late);
                Log.Write($"  image save: added .{late} after the dialog");
            }

            await File.WriteAllBytesAsync(path, result.Bytes);
            var kind = result.MediaType ?? (ImageSaver.ExtFromBytes(result.Bytes) is { } sniffed ? $"sniffed {sniffed}" : "?");
            Log.Write($"  image save: wrote {result.Bytes.Length} bytes ({kind}) " +
                      $"-> {Path.GetDirectoryName(path)}");
        }
        catch (Exception ex)
        {
            Log.Write($"!! image save failed: {ex.GetType().Name} {ex.Message}");
            MessageBox.Show(this, Strings.ImageSaveFailed(ex.Message), Strings.SaveImage,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>path が既にあれば「名前 (1).拡張子」…と空いている名前を返す。</summary>
    private static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path) ?? "";
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i = 1; ; i++)
        {
            var p = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(p)) return p;
        }
    }

    /// <summary>ログ用。URL は残さず種類とホストだけ。</summary>
    private static string UrlKind(string url) =>
        url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? "data"
        : Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : "?";

    private static async Task<T?> WithTimeout<T>(Task<T?> task, int ms)
    {
        var done = await Task.WhenAny(task, Task.Delay(ms));
        return done == task ? await task : default;
    }

    /// <summary>
    /// 右クリックした座標の下にある IMG の URL をページに訊く。無ければ null。
    /// 主フレームしか見えないので、iframe の中の画像は取れない（そのときは Chromium に任せる）。
    /// e.Location の座標系に確信が無いので、そのままと devicePixelRatio で割ったものの両方で探す。
    /// </summary>
    private static async Task<string?> ImageUrlAtPoint(CoreWebView2? core, Point p)
    {
        if (core is null) return null;
        try
        {
            var js = $$"""
                (() => {
                  const at = (x, y) => {
                    const el = document.elementFromPoint(x, y);
                    return el && el.tagName === 'IMG' ? (el.currentSrc || el.src || null) : null;
                  };
                  const r = devicePixelRatio || 1;
                  return at({{p.X}}, {{p.Y}}) || (r !== 1 ? at({{p.X}} / r, {{p.Y}} / r) : null);
                })()
                """;
            var raw = await core.ExecuteScriptAsync(js);
            return System.Text.Json.JsonSerializer.Deserialize<string?>(raw);
        }
        catch (Exception ex)
        {
            Log.Write($"  image url probe failed {ex.GetType().Name}");
            return null;
        }
    }

    /// <summary>設定の保存先。無ければユーザーフォルダ。</summary>
    private string PreferredDownloadDir()
    {
        var dir = _settings.DownloadDir;
        if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir)) return dir;
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    /// <summary>設定画面の「変更」から呼ばれる。選ばれたら保存して画面を描き直す。</summary>
    private void PickDownloadDir()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = Strings.DownloadFolder,
            UseDescriptionForTitle = true,
            SelectedPath = PreferredDownloadDir(),
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        _settings.DownloadDir = dlg.SelectedPath;
        _settings.Save();
        ApplyDownloadDir();
        Log.Write($"download dir set: {dlg.SelectedPath}");
        Render();
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
        Log.Write($"  kind={target.Kind} link={target.HasLinkUri} src={target.HasSourceUri} editable={target.IsEditable} " +
                  $"selLen={(sel ?? "").Length} loc={e.Location.X},{e.Location.Y} dpi={DeviceDpi}");

        var menu = DarkMenu.Create(DeviceDpi);

        // ---- 画像保存の計測 ----
        // 「座標を拾い直すので外れる」という最初の仮説は外れた（開いたときも選んだときも同じ IMG）。
        // 実際は、Chromium が .tmp を書き始めても DownloadStarting がページを動かすまで届かない。
        // 開いた瞬間と選んだ瞬間の座標の下は、Chromium に任せる経路の手掛かりとして残してある。
        var openedAt = Environment.TickCount64;
        var point = e.Location;
        var isImage = target.Kind == CoreWebView2ContextMenuTargetKind.Image;
        // 「画像を保存」は URL が分かれば自分で取る（ImageSaver）。URL はここで押さえておく。
        // WebView2 が渡してこないとき（src=none）は、ページに座標の下の IMG を訊く。
        Task<string?> imageUrl = Task.FromResult<string?>(null);
        var pageUrl = core.Source;
        var hadSrc = target.HasSourceUri;
        if (isImage)
        {
            Log.Write($"  image: frame={(target.IsRequestedForMainFrame ? "main" : "sub")} src={SrcSummary(target)}");
            _ = ProbeAtPoint(core, point, "at-open");

            var src = target.HasSourceUri ? target.SourceUri : null;
            if (ImageSaver.CanFetch(src)) imageUrl = Task.FromResult<string?>(src);
            else if (src is null) imageUrl = ImageUrlAtPoint(core, point);
            // blob: は自分では取れない。Chromium に任せる。
        }

        if (target.HasLinkUri && UrlHelper.IsNavigable(target.LinkUri))
        {
            var link = target.LinkUri;
            menu.Items.Add(DarkMenu.Item(Strings.OpenLinkInNewTab, () => OpenInNewTab(link)));
            menu.Items.Add(DarkMenu.Item(Strings.CopyLinkAddress, () => SetClipboard(link)));
        }

        // 画像のときだけ、WebView2 の既定コマンドを借りる経路に入る。
        // ただし「画像を保存」は、URL が分かれば選ばれた時点で自前（ImageSaver）に切り替える。
        // 借りるのは、URL が取れなかったときの逃げ道として。
        var borrowed = false;
        var pick = -1;
        var pickName = "";

        // WebView2 の既定コマンドを、こちらのメニューの項目として借りる。
        // 対象（どの画像か）は向こうが覚えているので、こちらが SourceUri を
        // 取れるかどうかとは関係が無い。Yahoo! の画像検索は取れない（src=False）。
        void Borrow(string name, string label)
        {
            var id = DefaultCommandId(e, name);
            if (id < 0) return;
            borrowed = true;
            menu.Items.Add(DarkMenu.Item(label, () => { pick = id; pickName = name; }));
        }

        if (target.Kind == CoreWebView2ContextMenuTargetKind.Image)
        {
            // 「開く」だけは既定コマンドに相当するものが無いので、URL を持っているときだけ。
            if (target.HasSourceUri)
            {
                var src = target.SourceUri;
                menu.Items.Add(DarkMenu.Item(Strings.OpenImage, () => OpenInNewTab(src)));
            }

            Borrow("saveImageAs", Strings.SaveImage);
            Borrow("copyImage", Strings.CopyImage);
            Borrow("copyImageLocation", Strings.CopyImageAddress);

            // 既定コマンドが 1 つも借りられなかったときの保険。
            // 自前で出せるのはアドレスのコピーだけ（URL を持っていれば）。
            if (!borrowed)
            {
                if (target.HasSourceUri)
                {
                    var src = target.SourceUri;
                    menu.Items.Add(DarkMenu.Item(Strings.CopyImageAddress, () => SetClipboard(src)));
                }
                Log.Write($"  no image commands among: {DefaultCommandNames(e)}");
            }
        }

        if (!string.IsNullOrEmpty(sel))
        {
            var text = sel;
            var label = text.Length > 18 ? text[..18] : text;
            menu.Items.Add(DarkMenu.Item(Strings.Copy, () => SetClipboard(text)));
            menu.Items.Add(DarkMenu.Item(Strings.SearchFor(label), () => Submit(text)));
        }

        if (target.IsEditable)
        {
            var text = sel;
            menu.Items.Add(DarkMenu.Item(Strings.Cut, () => Cut(core, text), !string.IsNullOrEmpty(text)));
            menu.Items.Add(DarkMenu.Item(Strings.Paste, () => Paste(core)));
        }

        Separator(menu);
        menu.Items.Add(DarkMenu.Item(Strings.Back, () => core.GoBack(), core.CanGoBack));
        menu.Items.Add(DarkMenu.Item(Strings.Forward, () => core.GoForward(), core.CanGoForward));
        menu.Items.Add(DarkMenu.Item(Strings.Reload, () => core.Reload()));
        menu.Items.Add(DarkMenu.Item(Strings.CopyPageAddress, () => SetClipboard(core.Source)));
        Separator(menu);
        menu.Items.Add(DarkMenu.Item(Strings.NewTab, NewTab));
        menu.Items.Add(DarkMenu.Item(Strings.Home, GoHome));
        menu.Items.Add(DarkMenu.Item(Strings.Settings, ToggleSettings));

        // Deferral を取るのは最後。取った後の行で例外が出ると Complete されないまま残り、
        // WebView2 が永久に待つ。ここまで来ればもう何も失敗しない。
        if (borrowed)
        {
            var deferral = e.GetDeferral();

            // 閉じたら待たせている WebView2 を解放する。
            //
            // 項目の Click とメニューの Closed はどちらが先か当てにできない（右クリックの
            // MouseDown / MouseUp で一度これに嵌まっている）ので、Closed から更に
            // BeginInvoke で後ろへ送る。そうすれば Click は必ず済んでいる。
            menu.Closed += (_, _) => BeginInvoke(async () =>
            {
                string? ownSave = null;
                try
                {
                    // 画像の保存は、URL が分かっていれば Chromium に渡さず自分でやる。
                    if (pickName == "saveImageAs")
                    {
                        var url = await WithTimeout(imageUrl, 1000);
                        if (ImageSaver.CanFetch(url))
                        {
                            ownSave = url;
                            pick = -1;
                            Log.Write($"  image save: own ({(hadSrc ? "src" : "probe")} {UrlKind(url)})");
                        }
                        else
                        {
                            Log.Write("  image save: no usable URL, handing to Chromium");
                        }
                    }

                    if (pick >= 0)
                    {
                        // 渡す直前に座標の下をもう一度見る。Chromium が拾い直すのとほぼ同じ瞬間。
                        var held = Environment.TickCount64 - openedAt;
                        _ = ProbeAtPoint(core, point, "at-select");

                        e.SelectedCommandId = pick;
                        Log.Write($"  default command {pick} ({pickName}) selected held={held}ms");

                        if (pickName == "saveImageAs")
                        {
                            var at = Environment.TickCount64;
                            _imageSaveAt = at;
                            WatchImageSave(at, $"held={held}ms");
                        }
                    }
                }
                catch (Exception ex) { Log.Write($"!! select command failed: {ex.GetType().Name} {ex.Message}"); }
                finally { deferral.Complete(); }

                // WebView2 を放してから保存ウィンドウを出す
                if (ownSave is not null) SaveImageOwn(core, ownSave, pageUrl);
            });
        }

        // WebView2 の Location は DIP 基準で当てにしづらいので、実際のカーソル位置（画面座標）に出す
        Log.Write($"  items={menu.Items.Count} showing at cursor {Cursor.Position}");
        ShowMenuAtCursor(menu);
    }

    /// <summary>
    /// WebView2 が用意している既定メニューから、名前でコマンド番号を探す。
    /// 見つからなければ -1（その版に無い、という以上の意味は無いので黙って諦める）。
    /// </summary>
    private static int DefaultCommandId(CoreWebView2ContextMenuRequestedEventArgs e, string name)
    {
        try
        {
            foreach (var item in e.MenuItems)
                if (string.Equals(item.Name, name, StringComparison.Ordinal))
                    return item.CommandId;
        }
        catch (Exception ex) { Log.Write($"!! default command lookup failed: {ex.GetType().Name} {ex.Message}"); }
        return -1;
    }

    /// <summary>既定メニューに何が並んでいたか。1 つも借りられなかったときの手掛かり用。</summary>
    private static string DefaultCommandNames(CoreWebView2ContextMenuRequestedEventArgs e)
    {
        try { return string.Join(",", e.MenuItems.Select(m => m.Name)); }
        catch (Exception ex) { return $"({ex.GetType().Name})"; }
    }

    // ---------------------------------------------------------------- 画像保存の計測

    /// <summary>「画像を保存」を渡した時刻。0 なら見張っていない。</summary>
    private long _imageSaveAt;

    /// <summary>
    /// 画像の出どころを「スキーム ホスト」だけに縮めて返す。
    /// パスは残さない。画像 CDN はパスそのものに使い捨ての鍵を埋めてくる。
    /// blob: と data: はそれ自体が手掛かりなので、スキームだけ出す。
    /// </summary>
    private static string SrcSummary(CoreWebView2ContextMenuTarget t)
    {
        if (!t.HasSourceUri) return "none";
        var s = t.SourceUri;
        if (s.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return "data";
        if (s.StartsWith("blob:", StringComparison.OrdinalIgnoreCase)) return "blob";
        return Uri.TryCreate(s, UriKind.Absolute, out var u) ? $"{u.Scheme} {u.Host}" : "?";
    }

    /// <summary>
    /// 右クリックした座標の下に、いま何があるかをページに訊いて記録する。
    ///
    /// 開いたときと選んだときの 2 回呼ぶ。Chromium の saveImageAs は実行時に座標を
    /// 拾い直すので、2 回目が IMG でなければ、それが黙って失敗する理由になる。
    /// 待たずに投げっぱなしにするのは、選んだ瞬間の状態を見たいから（待つと遅れる）。
    ///
    /// 主フレームしか見えない。画像が iframe の中なら、ここには IFRAME と出る。
    /// </summary>
    private static async Task ProbeAtPoint(CoreWebView2? core, Point p, string when)
    {
        if (core is null) return;
        try
        {
            // e.Location が CSS ピクセルか物理ピクセルか確信が無い（96 dpi なら同じ、
            // 144 / 192 だとずれる）。両方の解釈で突いて並べる。開いた瞬間は Chromium が
            // 「画像」と言っているので、そのとき IMG を返した方が正しい座標系だと分かる。
            var js = $$"""
                (() => {
                  const at = (x, y) => {
                    const el = document.elementFromPoint(x, y);
                    if (!el) return 'none';
                    let s = el.tagName;
                    const c = typeof el.className === 'string' ? el.className.trim().split(/\s+/)[0] : '';
                    if (c) s += '.' + c.slice(0, 24);
                    if (el.tagName === 'IMG') {
                      try {
                        const u = new URL(el.currentSrc || el.src);
                        s += ' ' + u.protocol.replace(':', '') + ' ' + u.host + ' ' + el.naturalWidth + 'x' + el.naturalHeight;
                      } catch { s += ' ?'; }
                    }
                    return s;
                  };
                  const r = devicePixelRatio || 1;
                  const raw = at({{p.X}}, {{p.Y}});
                  if (r === 1) return raw + ' dpr=1';
                  return 'raw=' + raw + ' | scaled=' + at({{p.X}} / r, {{p.Y}} / r) + ' dpr=' + r;
                })()
                """;
            var raw = await core.ExecuteScriptAsync(js);
            var text = System.Text.Json.JsonSerializer.Deserialize<string>(raw) ?? raw;
            Log.Write($"  {when}: {text} ({p.X},{p.Y})");
        }
        catch (Exception ex)
        {
            Log.Write($"  {when}: probe failed {ex.GetType().Name}");
        }
    }

    /// <summary>
    /// 「画像を保存」を渡してから 3 秒、DownloadStarting が来るかを見張る。
    /// いまは「起きなかった」ことがログに残らず、後から数えようがない。それを残す。
    /// </summary>
    private async void WatchImageSave(long startedAt, string info)
    {
        await Task.Delay(3000);
        if (_imageSaveAt != startedAt) return;   // 来た、または次の保存で上書きされた
        _imageSaveAt = 0;
        Log.Write($"image save: NO download within 3s ({info})");
    }

    /// <summary>
    /// メニューをカーソル位置に出す。WebView2 にフォーカスがあるまま画面座標で出すと、
    /// 開いた直後にアクティブでなくなって閉じてしまうので、フォームに紐付けて表示する。
    /// </summary>
    private void ShowMenuAtCursor(ContextMenuStrip menu)
    {
        CloseMenu();   // 前のが残っていたら先に畳む
        _menu = menu;
        menu.Closed += (_, _) => { if (ReferenceEquals(_menu, menu)) _menu = null; };

        Activate();
        menu.Show(this, PointToClient(Cursor.Position));
        menu.Focus();
    }

    /// <summary>
    /// 出している右クリックメニューを畳む。
    ///
    /// WinForms のメニューは WebView2 がページを移ったことを知らない。何もしないと、
    /// もう無い要素に対するメニューが画面に residual として残り続ける。
    /// 「画像を保存」の Deferral は Closed から解放されるので、ここで閉じても宙に浮かない。
    /// </summary>
    private void CloseMenu()
    {
        var m = _menu;
        _menu = null;
        if (m is null || m.IsDisposed || !m.Visible) return;

        Log.Write("context menu: closed (page moved)");
        m.Close(ToolStripDropDownCloseReason.AppFocusChange);
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
    /// ★（と Ctrl+D）。Chrome と同じで、押した時点で入れてしまい、窓で名前と場所を直せるようにする。
    /// 入れ先は前回その窓で選んだフォルダ。初めてなら「未整理」。
    /// すでに入っているページなら、入れ直さずに「編集」として同じ窓を出す。
    /// </summary>
    private void AddCurrentPage() => ShowBookmarkPopup(null, null);

    private BookmarkPopup? _bmPopup;

    private void ShowBookmarkPopup(string? preferredFolder, Point? screenAt)
    {
        var url = _active.Url;
        if (string.IsNullOrWhiteSpace(url)) return;

        _bmPopup?.Close();

        var node = FindByUrl(url);
        var isNew = node is null;
        if (node is null)
        {
            var folder = preferredFolder;
            if (folder is null)
            {
                var last = _settings.LastBookmarkFolder;
                folder = !string.IsNullOrEmpty(last) && _bookmarks.Get(last) is { IsFolder: true, IsDeleted: false }
                    ? last : BookmarkStore.RootOther;
            }
            var title = string.IsNullOrWhiteSpace(_active.Title) ? UrlHelper.HostTitle(url) : _active.Title;
            node = _bookmarks.AddLink(folder, title, url);
            _bookmarks.Save();
            BookmarksChanged();
            Log.Write($"bookmark added: -> {FolderKind(folder)}");
        }

        var popup = new BookmarkPopup(_bookmarks, node, isNew, DeviceDpi);
        var target = node;
        popup.Finished += p => FinishBookmarkPopup(p, target);

        // ★ の真下（バーから呼ばれたときはクリックした位置）に出し、画面からはみ出さないよう寄せる。
        var at = screenAt ?? _star.PointToScreen(new Point(0, _star.Height + Scale(4)));
        var size = popup.GetPreferredSize(Size.Empty);
        var area = Screen.FromPoint(at).WorkingArea;
        at.X = Math.Clamp(at.X, area.Left, Math.Max(area.Left, area.Right - size.Width));
        at.Y = Math.Clamp(at.Y, area.Top, Math.Max(area.Top, area.Bottom - size.Height));
        popup.Location = at;

        _bmPopup = popup;
        popup.Show(this);
    }

    private void FinishBookmarkPopup(BookmarkPopup p, BookmarkNode node)
    {
        if (ReferenceEquals(_bmPopup, p)) _bmPopup = null;

        if (p.Removed)
        {
            _bookmarks.Remove(node.Id);
            Log.Write("bookmark popup: removed");
        }
        else
        {
            var title = p.EnteredTitle;
            if (title.Length > 0 && title != node.Title)
            {
                node.Title = title;
                node.UpdatedAt = DateTimeOffset.UtcNow;
            }
            var folder = p.ChosenFolderId;
            var moved = folder != node.ParentId && _bookmarks.MoveToEnd(node.Id, folder);
            if (_settings.LastBookmarkFolder != folder)
            {
                _settings.LastBookmarkFolder = folder;
                _settings.Save();
            }
            Log.Write($"bookmark popup: done{(moved ? $" moved -> {FolderKind(folder)}" : "")}");
        }

        _bookmarks.Save();
        BookmarksChanged();
        // p は Show() で出した窓なので、閉じ終わると WinForms が自分で Dispose する。
    }

    /// <summary>ブックマークを書き換えたあと、バー・サイドバー・★ を描き直す。</summary>
    private void BookmarksChanged()
    {
        _sidebar.Reload();
        _bar.Reload();
        UpdateChrome();
        RefreshManager();
    }

    // ---------------------------------------------------------------- ブックマークマネージャー

    /// <summary>マネージャーで最後に開いていたフォルダ。開き直したときにそこから始める。</summary>
    private string _bmFolder = BookmarkStore.RootBar;

    /// <summary>マネージャーで検索中の語。空なら _bmFolder の中身を出している。</summary>
    private string _bmSearch = "";

    /// <summary>マネージャーを開く前にいたタブ。マネージャーで「戻る」を押したらそこへ帰る。</summary>
    private BrowserTab? _bmReturnTab;

    /// <summary>
    /// 内部ページでの「戻る」。設定や About が開いていれば閉じる。
    /// マネージャーのタブなら、開く前にいたタブへ移る（そのタブが残っていれば）。
    /// </summary>
    private void InternalBack()
    {
        if (_settingsOpen || _aboutOpen)
        {
            CloseInternalPages();
            Render();
            return;
        }
        if (_active.Page == BookmarkManager.PageId && _bmReturnTab is { } back && _tabs.Contains(back) && !ReferenceEquals(back, _active))
        {
            SelectTab(back);
            return;
        }
        Render();   // 念のため、いまの状態の画面を出し直す
    }

    /// <summary>マネージャーのタブを開く。もう開いていればそこへ移るだけ。</summary>
    private void OpenBookmarkManager()
    {
        if (_active.Page != BookmarkManager.PageId) _bmReturnTab = _active;
        var tab = _tabs.FirstOrDefault(t => t.Page == BookmarkManager.PageId);
        if (tab is null)
        {
            tab = new BrowserTab { Page = BookmarkManager.PageId, Title = Strings.BookmarkManager };
            _tabs.Add(tab);
            RebuildTabStrip();
        }
        Log.Write("bookmark manager: open");
        _active = tab;
        CloseInternalPages();
        RebuildTabStrip();
        Render();
    }

    private bool ManagerShowing =>
        _active.Page == BookmarkManager.PageId && !_settingsOpen && !_aboutOpen && _uiView.CoreWebView2 is not null;

    private void PostToManager(object message) => PostJsonToManager(JsonSerializer.Serialize(message));

    private void PostJsonToManager(string json)
    {
        if (!ManagerShowing) return;
        try { _uiView.CoreWebView2.PostWebMessageAsJson(json); }
        catch (Exception ex) when (ex is COMException or InvalidOperationException) { }
    }

    /// <summary>木と、いま見ている一覧を送り直す。ほかの場所（★ やサイドバー）で変えたときも呼ぶ。</summary>
    private void RefreshManager()
    {
        if (!ManagerShowing) return;
        if (_bookmarks.Get(_bmFolder) is not { IsFolder: true, IsDeleted: false }) _bmFolder = BookmarkStore.RootBar;
        PostToManager(BookmarkManager.Tree(_bookmarks, _bmFolder, _linkCheck));
        PostToManager(_bmView == "dead" ? BookmarkManager.DeadLinks(_bookmarks, _linkCheck, _bmFolder)
            : _bmView == "dups" ? BookmarkManager.Duplicates(_bookmarks)
            : _bmSearch.Length > 0 ? BookmarkManager.Search(_bookmarks, _bmSearch)
            : BookmarkManager.Items(_bookmarks, _bmFolder));
    }

    /// <summary>最後にマネージャーで消したもの。「元に戻す」は直前の 1 回分だけ。</summary>
    private BookmarkStore.Removal? _bmUndo;

    /// <summary>フォルダでも検索でもない見方（"dups" ＝ 重複、"dead" ＝ リンク切れ）。空ならふつう。</summary>
    private string _bmView = "";

    /// <summary>リンク切れの確認。最後の 1 回分の結果を、閉じるまで持っておく。</summary>
    private LinkChecker? _linkCheck;
    private DateTime _linkCheckPosted;

    private void StartLinkCheck()
    {
        if (_linkCheck is { Running: true }) return;
        var links = BookmarkManager.LinksUnder(_bookmarks, _bmFolder);
        var ua = _uiView.CoreWebView2?.Settings.UserAgent ?? "Mozilla/5.0";
        var check = new LinkChecker(_bmFolder, links, ua);
        check.Checked += (_, r) => BeginInvoke(() => OnLinkChecked(check, r));
        check.Finished += () => BeginInvoke(() => OnLinkCheckFinished(check));
        _linkCheck = check;
        Log.Write($"link check: start {check.Total} links in {FolderKind(_bmFolder)}");
        check.Start();
        RefreshManager();
    }

    private void OnLinkChecked(LinkChecker check, LinkChecker.Result r)
    {
        if (!ReferenceEquals(check, _linkCheck) || IsDisposed) return;
        if (r.Kind != LinkChecker.Kind.Ok) { RefreshManager(); return; }   // 一覧と木の数を出し直す
        // 問題が無かったものは、進み具合だけを半秒に 1 回まで。
        if (DateTime.UtcNow - _linkCheckPosted < TimeSpan.FromMilliseconds(500)) return;
        _linkCheckPosted = DateTime.UtcNow;
        PostToManager(new { type = "bm:check", check = BookmarkManager.CheckState(_bookmarks, check, _bmFolder) });
    }

    private void OnLinkCheckFinished(LinkChecker check)
    {
        if (!ReferenceEquals(check, _linkCheck) || IsDisposed) return;
        Log.Write($"link check: {(check.NetworkLost ? "network lost" : check.Stopped ? "stopped" : "done")} " +
                  $"{check.Done}/{check.Total}, broken {check.Count(LinkChecker.Kind.Dead)}, " +
                  $"unsure {check.Count(LinkChecker.Kind.Unsure)}, {check.Elapsed.TotalMinutes:0.0} min");
        RefreshManager();
    }
    private string? _bmUndoToken;

    private static IEnumerable<string> Ids(JsonElement msg) =>
        msg.TryGetProperty("ids", out var ids) && ids.ValueKind == JsonValueKind.Array
            ? ids.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToList()
            : [];

    /// <summary>マネージャーからの bm:* メッセージ。</summary>
    private void OnManagerMessage(string type, JsonElement msg)
    {
        string? Str(string name) => msg.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        switch (type)
        {
            case "bm:init":
                _bmSearch = "";
                _bmView = "";
                RefreshManager();
                return;

            case "bm:list":
                if (Str("folder") is { } folder && _bookmarks.Get(folder) is { IsFolder: true })
                {
                    _bmFolder = folder;
                    _bmSearch = "";
                    _bmView = "";
                    // 大きなフォルダを開くのが遅い件の切り分け用。組み立てと受け渡しの時間を残す。
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var json = JsonSerializer.Serialize(BookmarkManager.Items(_bookmarks, folder));
                    var built = sw.ElapsedMilliseconds;
                    PostJsonToManager(json);
                    Log.Write($"bookmark manager: list {_bookmarks.Children(folder).Count} items, {json.Length / 1024} KB, " +
                              $"built {built}ms, posted {sw.ElapsedMilliseconds - built}ms");
                }
                return;

            case "bm:search":
                _bmSearch = Str("q")?.Trim() ?? "";
                _bmView = "";
                RefreshManager();
                return;

            case "bm:view":
                // 重複の一覧など、フォルダではない見方。
                _bmView = Str("view") is "dups" or "dead" ? Str("view")! : "";
                _bmSearch = "";
                if (_bmView == "dups")
                    Log.Write($"bookmark manager: duplicates ({BookmarkManager.DuplicateGroups(_bookmarks).Count} groups)");
                RefreshManager();
                return;

            case "bm:open":
                if (_bookmarks.Get(Str("id") ?? "") is { IsLink: true, Url: { } target } && UrlHelper.IsNavigable(target))
                {
                    var newTab = msg.TryGetProperty("newTab", out var nt) && nt.ValueKind == JsonValueKind.True;
                    Log.Write($"bookmark manager: open {(newTab ? "new tab" : "here")}");
                    if (newTab) OpenInNewTab(target); else Navigate(_active, target);
                }
                return;

            case "bm:edit":
            {
                if (_bookmarks.Get(Str("id") ?? "") is not { IsDeleted: false } node) return;
                var title = Str("title")?.Trim() ?? "";
                if (title.Length == 0) return;
                string? url = null;
                if (node.IsLink)
                {
                    url = BookmarkManager.NormalizeUrl(Str("url"));
                    if (url is null)
                    {
                        PostToManager(new { type = "bm:error", title = Strings.BmEdit, text = Strings.BmBadUrl });
                        return;
                    }
                }
                // 取り込み元の名前は画面で差し替えて見せているだけ（「未整理」など）。根は名前を変えさせない。
                if (node.Id is BookmarkStore.RootBar or BookmarkStore.RootOther) return;
                node.Title = title;
                if (url is not null && url != node.Url) { node.Url = url; node.Icon = null; }
                node.UpdatedAt = DateTimeOffset.UtcNow;
                _bookmarks.Save();
                Log.Write($"bookmark manager: edit {node.Kind}");
                BookmarksChanged();
                return;
            }

            case "bm:newFolder":
            {
                var parent = Str("parent") ?? _bmFolder;
                if (_bookmarks.Get(parent) is not { IsFolder: true, IsDeleted: false }) parent = BookmarkStore.RootBar;
                var title = Str("title")?.Trim();
                if (string.IsNullOrEmpty(title)) return;
                _bookmarks.AddFolder(parent, title);
                _bookmarks.Save();
                Log.Write("bookmark manager: new folder");
                BookmarksChanged();
                return;
            }

            case "bm:delete":
            {
                var removal = _bookmarks.RemoveMany(Ids(msg));
                if (removal.Tops.Count == 0) return;
                _bookmarks.Save();
                _bmUndo = removal;
                _bmUndoToken = Guid.NewGuid().ToString("N");
                Log.Write($"bookmark manager: deleted {removal.Tops.Count} chosen " +
                          $"({removal.All.Count} entries counting what was inside folders)");
                BookmarksChanged();
                PostToManager(new { type = "bm:deleted", token = _bmUndoToken, count = removal.Tops.Count });
                return;
            }

            case "bm:undo":
            {
                if (_bmUndo is null || Str("token") != _bmUndoToken) return;
                _bookmarks.RestoreRemoved(_bmUndo);
                Log.Write($"bookmark manager: undo {_bmUndo.Tops.Count}");
                _bmUndo = null;
                _bmUndoToken = null;
                _bookmarks.Save();
                BookmarksChanged();
                return;
            }

            case "bm:move":
            {
                var to = Str("to");
                if (to is null || _bookmarks.Get(to) is not { IsFolder: true, IsDeleted: false }) return;
                var moved = 0;
                foreach (var id in Ids(msg))
                    if (id is not (BookmarkStore.RootBar or BookmarkStore.RootOther) && _bookmarks.MoveToEnd(id, to)) moved++;
                if (moved == 0) return;
                _bookmarks.Save();
                Log.Write($"bookmark manager: moved {moved} -> {FolderKind(to)}");
                BookmarksChanged();
                return;
            }

            case "bm:perf":
            {
                // ページ側で、フォルダを押してから一覧が描き終わるまで。
                int Num(string name) => msg.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : -1;
                Log.Write($"bookmark manager: list shown {Num("rows")} rows {Num("ms")}ms after the click (drawing {Num("draw")}ms)");
                return;
            }

            case "bm:checkStart":
                StartLinkCheck();
                return;

            case "bm:checkStop":
                if (_linkCheck is { Running: true } c)
                {
                    Log.Write("link check: stop requested");
                    c.Stop();
                }
                return;

            case "bm:drop":
            {
                // ドラッグで落とした。before の直前へ（null なら末尾へ）。同じフォルダなら並べ替え。
                var to = Str("to");
                if (to is null) return;
                var moved = _bookmarks.MoveMany(Ids(msg), to, Str("before"));
                if (moved == 0) { Log.Write("bookmark manager: drop changed nothing"); return; }
                _bookmarks.Save();
                Log.Write($"bookmark manager: dropped {moved} -> {FolderKind(to)} ({(Str("before") is null ? "end" : "before an item")})");
                BookmarksChanged();
                return;
            }
        }
    }

    /// <summary>ログ用。フォルダ名は残さず、バー／未整理／その他だけ。</summary>
    private static string FolderKind(string id) =>
        id is BookmarkStore.RootBar or BookmarkStore.RootOther ? id : "folder";

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
            // Ctrl+L はここで選び終えているので、続くクリックでは選び直さない。
            case Keys.L when ctrl: _omni.Focus(); _omni.SelectAll(); _omniFresh = false; return;
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
