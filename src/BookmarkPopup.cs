namespace Voyager;

/// <summary>
/// ★ を押したときの小さな窓。Chrome の「ブックマークに追加しました」と同じ動き。
///
/// 窓が出た時点でブックマークはもう入っている。名前と入れる場所を直すだけの窓で、
/// 何も触らず外をクリックすればそのまま残る。「削除」だけが取り消し。
/// すでに入っているページでは「ブックマークを編集」として開く。
///
/// 結果は閉じたときに Finished で返す。書き換えるのは呼んだ側（MainForm）。
/// </summary>
internal sealed class BookmarkPopup : Form
{
    private readonly int _dpi;
    private readonly TextBox _name;
    private readonly ComboBox _folder;
    private readonly List<(string Id, string Label)> _folders = [];
    private bool _finished;
    private bool _closing;

    /// <summary>「削除」で閉じた。</summary>
    public bool Removed { get; private set; }

    /// <summary>入力された名前（前後の空白を落としたもの）。</summary>
    public string EnteredTitle => _name.Text.Trim();

    /// <summary>選ばれたフォルダの id。</summary>
    public string ChosenFolderId =>
        _folder.SelectedIndex >= 0 ? _folders[_folder.SelectedIndex].Id : BookmarkStore.RootOther;

    /// <summary>閉じたとき、1 回だけ。</summary>
    public event Action<BookmarkPopup>? Finished;

    private int S(int logical) => (int)Math.Round(logical * _dpi / 96.0);

    public BookmarkPopup(BookmarkStore store, BookmarkNode node, bool isNew, int dpi)
    {
        _dpi = dpi;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.None;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = Theme.Border;            // 1px の枠に見せる
        Padding = new Padding(1);
        Font = Theme.Ui(9f, FontStyle.Regular, dpi);

        var body = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Card,
            ColumnCount = 2,
            Padding = new Padding(S(16), S(14), S(16), S(12)),
            Dock = DockStyle.Fill,
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var heading = new Label
        {
            Text = isNew ? Strings.BookmarkAdded : Strings.BookmarkEdit,
            AutoSize = true,
            ForeColor = Theme.Text,
            Font = Theme.Ui(10.5f, FontStyle.Bold, dpi),
            Margin = new Padding(0, 0, 0, S(12)),
        };
        body.Controls.Add(heading, 0, 0);
        body.SetColumnSpan(heading, 2);

        _name = new TextBox
        {
            Text = node.Title,
            Width = S(280),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            Margin = new Padding(0, 0, 0, S(8)),
        };
        body.Controls.Add(FieldLabel(Strings.BookmarkName), 0, 1);
        body.Controls.Add(_name, 1, 1);

        _folder = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            Width = S(280),
            DropDownWidth = S(360),
            MaxDropDownItems = 20,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = S(20),
            Margin = new Padding(0, 0, 0, S(14)),
        };
        _folder.DrawItem += DrawFolder;
        body.Controls.Add(FieldLabel(Strings.BookmarkFolder), 0, 2);
        body.Controls.Add(_folder, 1, 2);

        // フォルダの木を字下げして 1 列に並べる。
        foreach (var root in new[] { BookmarkStore.RootBar, BookmarkStore.RootOther })
            if (store.Get(root) is { } r) AddFolder(store, r, 0);
        foreach (var f in _folders) _folder.Items.Add(f.Label);
        var at = _folders.FindIndex(f => f.Id == node.ParentId);
        _folder.SelectedIndex = at >= 0 ? at : 0;

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Anchor = AnchorStyles.Right,
            BackColor = Theme.Card,
            Margin = Padding.Empty,
        };
        var done = MakeButton(Strings.Done, primary: true);
        done.Click += (_, _) => Close();
        var remove = MakeButton(Strings.Delete, primary: false);
        remove.Click += (_, _) => { Removed = true; Close(); };
        buttons.Controls.Add(done);
        buttons.Controls.Add(remove);
        body.Controls.Add(buttons, 0, 3);
        body.SetColumnSpan(buttons, 2);

        Controls.Add(body);
        AcceptButton = done;
    }

    private void AddFolder(BookmarkStore store, BookmarkNode folder, int depth)
    {
        var name = Strings.FolderName(folder.Title).Replace("\r", " ").Replace("\n", " ").Trim();
        _folders.Add((folder.Id, new string('　', depth) + name));
        if (depth >= 8) return;
        foreach (var c in store.Children(folder.Id))
            if (c.IsFolder) AddFolder(store, c, depth + 1);
    }

    private Label FieldLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Theme.Muted,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(0, S(3), S(12), S(8)),
    };

    private Button MakeButton(string text, bool primary)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = true,
            MinimumSize = new Size(S(76), S(28)),
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Theme.Accent : Theme.Card,
            ForeColor = primary ? Theme.Background : Theme.Text,
            Margin = new Padding(S(8), 0, 0, 0),
            Cursor = Cursors.Hand,
        };
        b.FlatAppearance.BorderColor = primary ? Theme.Accent : Theme.Border;
        b.FlatAppearance.MouseOverBackColor = primary ? Theme.Lead : Theme.CardHover;
        return b;
    }

    /// <summary>選択欄の一覧を暗い配色で描く。既定の描画だと白地に青の選択色になる。</summary>
    private void DrawFolder(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var selected = (e.State & DrawItemState.Selected) != 0;
        using (var bg = new SolidBrush(selected ? Theme.CardHover : Theme.Surface))
            e.Graphics.FillRectangle(bg, e.Bounds);
        TextRenderer.DrawText(e.Graphics, _folders[e.Index].Label, e.Font ?? Font,
            new Rectangle(e.Bounds.X + S(4), e.Bounds.Y, e.Bounds.Width - S(4), e.Bounds.Height),
            Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _name.Focus();
        _name.SelectAll();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        // Esc も閉じるだけ。Chrome と同じで、取り消しは「削除」だけ。
        if (e.KeyCode == Keys.Escape) { e.Handled = true; Close(); }
    }

    /// <summary>外をクリックしたら閉じる（入れたまま）。</summary>
    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        // ボタンで閉じる途中にも Deactivate は来る。そのときは二重に閉じない。
        if (_closing || IsDisposed || !IsHandleCreated) return;
        BeginInvoke(() => { if (!_closing && !IsDisposed) Close(); });
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _closing = true;
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        if (_finished) return;
        _finished = true;
        Finished?.Invoke(this);
    }
}
