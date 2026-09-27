namespace Voyager;

/// <summary>
/// ブックマークのサイドバー。
///
/// 2,400 件規模を前提にしているので、検索窓を主役にして木は補助に置く。
/// 木は開いたときに初めて子を作る（全部いっぺんに作ると開くたびに固まる）。
/// </summary>
internal sealed class BookmarkSidebar : Panel
{
    /// <summary>
    /// 木の 1 行の高さ。拡大率 100%（96 dpi）のときの値で、そのまま入れてはいけない。
    ///
    /// TreeView.ItemHeight は画素で持つ値なので、150% の画面では字だけが大きくなって
    /// 行の高さを追い越し、上下の行が重なって読めなくなる。ApplyDpi() で必ず換算する。
    /// </summary>
    private const int RowHeight = 22;

    private readonly BookmarkStore _store;
    private readonly TextBox _search;
    private readonly TreeView _tree;
    private readonly Label _count;

    /// <summary>url, 新しいタブで開くか</summary>
    public event Action<string, bool>? OpenRequested;

    /// <summary>ストアを書き換えた。ブックマークバー側も描き直してほしい。</summary>
    public event Action? StoreChanged;

    public BookmarkSidebar(BookmarkStore store)
    {
        _store = store;
        Dock = DockStyle.Left;
        Width = 300;
        BackColor = Theme.Surface;
        Padding = new Padding(8, 8, 8, 8);

        _search = new TextBox
        {
            Dock = DockStyle.Top,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.Card,
            ForeColor = Theme.Text,
            Font = Theme.Ui(10f),
            PlaceholderText = Strings.SearchBookmarks,
        };
        _tree = new TreeView
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Background,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.None,
            Font = Theme.Ui(9.5f),
            HideSelection = false,
            FullRowSelect = true,
            ShowLines = false,
            ShowPlusMinus = true,
            ShowRootLines = true,
            LabelEdit = true,
            ItemHeight = RowHeight,   // 実際の値は ApplyDpi() で入れ直す
        };
        _search.TextChanged += (_, _) => Refresh_();
        _search.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Down) { _tree.Focus(); e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.Escape) { _search.Clear(); e.SuppressKeyPress = true; }
        };

        _tree.BeforeExpand += OnBeforeExpand;
        _tree.NodeMouseDoubleClick += (_, e) => Open(e.Node, (ModifierKeys & Keys.Control) != 0);
        _tree.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Middle && _tree.GetNodeAt(e.Location) is { } n) Open(n, true);
            if (e.Button == MouseButtons.Right && _tree.GetNodeAt(e.Location) is { } r)
            {
                _tree.SelectedNode = r;
                ShowMenu(r, e.Location);
            }
        };
        _tree.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && _tree.SelectedNode is { } n)
            {
                Open(n, e.Control);
                e.SuppressKeyPress = true;
            }
            if (e.KeyCode == Keys.Delete && _tree.SelectedNode is { } d) Delete(d);
            if (e.KeyCode == Keys.F2 && _tree.SelectedNode is { } r) r.BeginEdit();
        };
        _tree.AfterLabelEdit += OnAfterLabelEdit;

        _count = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 18,
            ForeColor = Theme.Muted,
            Font = Theme.Ui(8f),
            TextAlign = ContentAlignment.MiddleLeft,
        };

        var tools = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 34,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Theme.Surface,
        };
        tools.Controls.Add(SmallButton(Strings.Import, Strings.ImportTip, (_, _) => ImportDialog()));
        tools.Controls.Add(SmallButton(Strings.Export, Strings.ExportTip, (_, _) => ExportDialog()));

        // Fill を先に、端に寄せるものを後に。順序が配置を決める。
        Controls.Add(_tree);
        Controls.Add(_count);
        Controls.Add(tools);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 6, BackColor = Theme.Surface });
        Controls.Add(_search);

        Reload();
    }

    private static Button SmallButton(string text, string tip, EventHandler onClick)
    {
        var b = new Button
        {
            Text = text, Width = 86, Height = 26,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Card, ForeColor = Theme.Text,
            Font = Theme.Ui(8.5f),
            Margin = new Padding(0, 2, 6, 0),
            Cursor = Cursors.Hand, TabStop = false,
        };
        b.FlatAppearance.BorderColor = Theme.Border;
        b.FlatAppearance.MouseOverBackColor = Theme.CardHover;
        b.Click += onClick;
        new ToolTip().SetToolTip(b, tip);
        return b;
    }

    // ---------------------------------------------------------------- 拡大率

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyDpi();   // 作った時点では DeviceDpi がまだ確定していない
    }

    /// <summary>画面をまたいで拡大率が変わったとき。WinForms は字だけ直して画素値は触らない。</summary>
    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ApplyDpi();
    }

    private void ApplyDpi()
    {
        var h = (int)Math.Round(RowHeight * DeviceDpi / 96.0);
        if (_tree.ItemHeight != h) _tree.ItemHeight = h;

        // 幅も画素なので、拡大率が変わっても WinForms は直してくれない。
        // 200% の画面では実質半分の幅になり、件数が枠の外へ出る。
        _applying = true;
        try
        {
            var w = ToDevice(_logicalWidth);
            if (Width != w) Width = w;
        }
        finally { _applying = false; }
        _appliedDpi = DeviceDpi;   // ここから先の Resize は利用者の操作とみなす

        Log.Write($"sidebar dpi: {DeviceDpi} itemHeight={h} width={Width} (logical {_logicalWidth})");
    }

    /// <summary>96 dpi 基準の値を、いまの画面の画素数に直す。</summary>
    public int ToDevice(int logical) => (int)Math.Round(logical * DeviceDpi / 96.0);

    /// <summary>
    /// 設定に残す幅。拡大率の違う画面で開き直しても同じ見た目になるよう、
    /// 画面の画素数ではなく 96 dpi 基準に戻して持つ。
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int LogicalWidth
    {
        get => DeviceDpi > 0 ? (int)Math.Round(Width * 96.0 / DeviceDpi) : Width;
        set { _logicalWidth = value; if (IsHandleCreated) Width = ToDevice(value); }
    }

    private int _logicalWidth = 300;
    private bool _applying;

    /// <summary>最後に幅を換算したときの拡大率。0 は「まだ一度も換算していない」。</summary>
    private int _appliedDpi;

    /// <summary>
    /// スプリッタで広げられたとき。利用者が決めた幅を 96 dpi 基準で覚え直す。
    /// これをしないと、次に拡大率が変わった瞬間に元の幅へ戻ってしまう。
    /// </summary>
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        // DeviceDpi が変わった直後は、まだ Width を換算していない。
        // その状態で割り戻すと「新しい拡大率 ÷ 古い画素数」になり、覚えている幅が
        // 往復のたびに縮んでいく（220 → 147 → 110）。換算を終えた拡大率のときだけ拾う。
        if (_applying || !IsHandleCreated || Width <= 0 || DeviceDpi != _appliedDpi) return;
        _logicalWidth = LogicalWidth;
    }

    public void FocusSearch() { _search.Focus(); _search.SelectAll(); }

    /// <summary>その URL を検索欄に入れて、どこに入っているかを見せる。</summary>
    public void Reveal(string url)
    {
        _search.Text = url;
        if (_tree.Nodes.Count > 0) { _tree.SelectedNode = _tree.Nodes[0]; _tree.Focus(); }
    }

    public void Reload() => Refresh_();

    // ---------------------------------------------------------------- 表示

    private void Refresh_()
    {
        _tree.BeginUpdate();
        _tree.Nodes.Clear();

        var q = _search.Text.Trim();
        if (q.Length > 0)
        {
            var hits = _store.Search(q);
            foreach (var h in hits)
            {
                var path = _store.PathOf(h);
                var n = new TreeNode(h.Title.Length > 0 ? Strings.FolderName(h.Title) : h.Url ?? "") { Tag = h };
                n.ToolTipText = string.IsNullOrEmpty(path) ? (h.Url ?? "") : $"{path}\n{h.Url}";
                if (path.Length > 0) n.Text += $"   〈{path}〉";
                _tree.Nodes.Add(n);
            }
            _count.Text = hits.Count >= 200 ? Strings.TooManyHits : Strings.Hits(hits.Count);
        }
        else
        {
            foreach (var rootId in new[] { BookmarkStore.RootBar, BookmarkStore.RootOther })
                if (_store.Get(rootId) is { } root)
                    _tree.Nodes.Add(MakeNode(root));

            var total = _store.Nodes.Count(n => n.IsLink && !n.IsDeleted);
            _count.Text = Strings.TotalCount(total);
            if (_tree.Nodes.Count > 0) _tree.Nodes[0].Expand();
        }

        _tree.EndUpdate();
    }

    private TreeNode MakeNode(BookmarkNode b)
    {
        var n = new TreeNode(b.Title.Length > 0 ? Strings.FolderName(b.Title) : (b.Url ?? Strings.Untitled)) { Tag = b };
        if (b.IsFolder)
        {
            if (_store.Children(b.Id).Count > 0)
            {
                n.Nodes.Add(new TreeNode("..."));   // 開いたときに本物へ入れ替える仮の子
                // 直下の数ではなく配下のリンク総数。2,471 件入っているのに (1) では分からない。
                n.Text += $"  ({_store.CountLinks(b.Id):N0})";
            }
        }
        else n.ToolTipText = b.Url ?? "";
        return n;
    }

    private void OnBeforeExpand(object? sender, TreeViewCancelEventArgs e)
    {
        if (e.Node is null) return;
        if (e.Node.Nodes.Count != 1 || e.Node.Nodes[0].Tag is not null) return;   // 仮の子だけが Tag を持たない
        if (e.Node.Tag is not BookmarkNode b) return;

        _tree.BeginUpdate();
        e.Node.Nodes.Clear();
        foreach (var child in _store.Children(b.Id)) e.Node.Nodes.Add(MakeNode(child));
        _tree.EndUpdate();
    }

    // ---------------------------------------------------------------- 操作

    private void Open(TreeNode? node, bool newTab)
    {
        if (node?.Tag is not BookmarkNode b) return;
        if (b.IsFolder) { if (node.IsExpanded) node.Collapse(); else node.Expand(); return; }
        if (!string.IsNullOrWhiteSpace(b.Url)) OpenRequested?.Invoke(b.Url!, newTab);
    }

    private void ShowMenu(TreeNode node, Point at)
    {
        if (node.Tag is not BookmarkNode b) return;
        var menu = DarkMenu.Create(DeviceDpi);

        if (b.IsLink)
        {
            menu.Items.Add(DarkMenu.Item(Strings.Open, () => Open(node, false)));
            menu.Items.Add(DarkMenu.Item(Strings.OpenInNewTab, () => Open(node, true)));
            menu.Items.Add(new ToolStripSeparator());
        }
        menu.Items.Add(DarkMenu.Item(Strings.Rename, () => node.BeginEdit()));
        if (b.Id != BookmarkStore.RootBar && b.Id != BookmarkStore.RootOther)
        {
            menu.Items.Add(DarkMenu.Item(Strings.MoveToBar, () => MoveToBar(b),
                                         b.ParentId != BookmarkStore.RootBar));
            menu.Items.Add(FolderMenu.MoveTo(_store, b, DeviceDpi, to => MoveTo(b, to)));
            menu.Items.Add(DarkMenu.Item(Strings.Delete, () => Delete(node)));
        }

        // バーの Popup と同じ理由で、フォームを手前にしてメニューにフォーカスを渡す。
        // ページ（WebView2）がフォーカスを持ったままだと、メニュー自体は出ても
        // 「フォルダへ移動 ▶」のようなサブメニューが開いた瞬間に閉じてしまう。
        FindForm()?.Activate();
        Log.Write($"sidebar menu: {b.Kind} items={menu.Items.Count}");
        menu.Show(_tree, at);
        menu.Focus();
    }

    private void MoveTo(BookmarkNode b, string folderId)
    {
        if (!_store.MoveToEnd(b.Id, folderId)) return;
        Log.Write($"bookmark moved: {b.Kind} -> {(folderId is BookmarkStore.RootBar or BookmarkStore.RootOther ? folderId : "folder")}");
        _store.Save();
        Refresh_();
        StoreChanged?.Invoke();
    }

    private void MoveToBar(BookmarkNode b)
    {
        _store.Move(b.Id, BookmarkStore.RootBar, _store.NextIndex(BookmarkStore.RootBar));
        _store.Save();
        Refresh_();
        StoreChanged?.Invoke();
    }

    private void Delete(TreeNode node)
    {
        if (node.Tag is not BookmarkNode b) return;
        if (b.Id is BookmarkStore.RootBar or BookmarkStore.RootOther) return;

        if (b.IsFolder)
        {
            var n = CountUnder(b.Id);
            if (n > 0 && MessageBox.Show(
                    Strings.DeleteFolderWarning(b.Title, n),
                    Strings.DeleteBookmark, MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning) != DialogResult.OK) return;
        }

        _store.Remove(b.Id);
        _store.Save();
        Refresh_();
        StoreChanged?.Invoke();
    }

    private int CountUnder(string id)
    {
        var n = 0;
        var stack = new Stack<string>();
        stack.Push(id);
        while (stack.Count > 0)
        {
            foreach (var c in _store.Children(stack.Pop()))
            {
                if (c.IsLink) n++; else stack.Push(c.Id);
            }
        }
        return n;
    }

    private void OnAfterLabelEdit(object? sender, NodeLabelEditEventArgs e)
    {
        if (e.Label is null) { return; }                    // Esc で取り消し
        if (e.Node?.Tag is not BookmarkNode b) { e.CancelEdit = true; return; }

        var t = e.Label.Trim();
        if (t.Length == 0) { e.CancelEdit = true; return; }

        b.Title = t;
        b.UpdatedAt = DateTimeOffset.UtcNow;
        _store.Save();

        // 件数の (n) を付け直したいので、確定後に作り直す
        BeginInvoke(Refresh_);
    }

    // ---------------------------------------------------------------- 出し入れ

    private void ImportDialog()
    {
        using var dlg = new OpenFileDialog
        {
            Title = Strings.ImportTitle,
            Filter = Strings.ImportFilter,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var html = File.ReadAllText(dlg.FileName, System.Text.Encoding.UTF8);
            // ファイル名に日付が入っていることが多いので、今日の日付は足さない
            var label = Path.GetFileNameWithoutExtension(dlg.FileName);
            if (string.IsNullOrWhiteSpace(label)) label = Strings.ImportedFolder(DateTime.Now);

            Cursor = Cursors.WaitCursor;
            var r = NetscapeBookmarks.Import(_store, html, BookmarkStore.RootOther,
                new ImportOptions(SkipEmptyFolders: true, ImportIcons: true, IntoFolderTitle: label,
                                  ToolbarParentId: BookmarkStore.RootBar));
            _store.Save();
            StoreChanged?.Invoke();
            Cursor = Cursors.Default;

            Refresh_();
            Log.Write($"bookmarks import: {r}");
            MessageBox.Show(
                Strings.ImportDone(label, r.ToString()),
                Strings.ImportTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Cursor = Cursors.Default;
            MessageBox.Show(Strings.ImportFailed, Strings.ImportTitle,
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ExportDialog()
    {
        using var dlg = new SaveFileDialog
        {
            Title = Strings.ExportTitle,
            Filter = Strings.ExportFilter,
            FileName = $"voyager_bookmarks_{DateTime.Now:yyyy_MM_dd}.html",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            File.WriteAllText(dlg.FileName, NetscapeBookmarks.Export(_store), new System.Text.UTF8Encoding(false));
            MessageBox.Show(Strings.ExportDone, Strings.ExportTitle,
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(Strings.ExportFailed, Strings.ExportTitle,
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
