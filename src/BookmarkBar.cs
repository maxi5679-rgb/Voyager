using System.Drawing.Drawing2D;

namespace Voyager;

/// <summary>
/// アドレス欄の下に出る一行のブックマークバー。中身は store の RootBar 直下だけ。
///
/// 子コントロールは置かず、一枚に自前で描いて当たり判定で拾う。
/// 20 個も Button を並べるとタブ追加のたびにレイアウトが走ってちらつくのと、
/// 「入る分だけ並べて残りは » へ」の判定を自分で持ちたいため。
/// </summary>
internal sealed class BookmarkBar : Panel
{
    // 寸法はすべて 96 dpi 基準。字が拡大率に追従するようになったので、
    // 入れ物も一緒に伸ばさないと 200% で文字が切られる。S() を通して引く。
    private const int BarHeight = 28;
    private int PadX => S(8);            // 左右の余白
    private int GapX => S(2);            // 項目どうしの間
    private int IconGap => S(5);         // アイコンと文字の間
    private int MaxItemWidth => S(180);
    private int OverflowWidth => S(24);

    /// <summary>96 dpi 基準の値を、いまの画面の画素数に直す。</summary>
    private int S(int logical) => (int)Math.Round(logical * DeviceDpi / 96.0);

    /// <summary>ファビコンの一辺。字と同じだけ拡大率に追従させないと、200% で豆粒になる。</summary>
    private int IconSize => S(Favicons.Size);

    /// <summary>フォルダメニューに一度に見せる行数。これを超えた分はスクロールで送る。</summary>
    private const int MaxMenuRows = 20;

    /// <summary>いまの拡大率に合わせた大きさでファビコンを引く。</summary>
    private Image? Icon(BookmarkNode n) => Favicons.Get(n.Icon, IconSize);

    private readonly BookmarkStore _store;
    private readonly List<BookmarkNode> _items = [];
    private readonly List<Rectangle> _rects = [];
    private readonly List<BookmarkNode> _hidden = [];
    private Rectangle _overflowRect = Rectangle.Empty;

    private int _hover = -1;             // _items の添字。-2 は » ボタン
    /// <summary>自前で描く字。static で 1 個持つと拡大率に追従しないので、そのつど引く。</summary>
    private Font ItemFont => Theme.Ui(9f, FontStyle.Regular, DeviceDpi);

    /// <summary>リンクを開いてほしい。bool は「新しいタブで」。</summary>
    public event Action<string, bool>? OpenRequested;

    /// <summary>いま開いているページをバーに入れたい。(URL, 題名) を返す。null なら追加しない。</summary>
    public event Func<(string? Url, string? Title)>? CurrentPageRequested;

    /// <summary>ストアを書き換えた。サイドバー側も描き直してほしい。</summary>
    public event Action? StoreChanged;

    public BookmarkBar(BookmarkStore store)
    {
        _store = store;
        Dock = DockStyle.Top;
        Height = BarHeight;   // 実際の高さは ApplyDpi() で入れ直す
        BackColor = Theme.Surface;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Reload();
    }

    // ---------------------------------------------------------------- 並べ直し

    public void Reload()
    {
        _items.Clear();
        foreach (var n in _store.Children(BookmarkStore.RootBar)) _items.Add(n);
        Layout_();
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Layout_();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyDpi();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ApplyDpi();
    }

    private void ApplyDpi()
    {
        var h = S(BarHeight);
        if (Height != h) Height = h;
        Layout_();
        Invalidate();
        Log.Write($"bar dpi: {DeviceDpi} height={h} icon={IconSize}");
    }

    private void Layout_()
    {
        _rects.Clear();
        _hidden.Clear();
        _overflowRect = Rectangle.Empty;
        if (_items.Count == 0 || Width <= 0) return;

        var widths = new int[_items.Count];
        using (var g = CreateGraphics())
            for (var i = 0; i < _items.Count; i++)
                widths[i] = Measure(g, _items[i]);

        // まず全部入るか試す。入らないときだけ » の場所を空けて置き直す。
        if (!Place(widths, Width - PadX))
        {
            _rects.Clear();
            _hidden.Clear();
            Place(widths, Width - PadX - OverflowWidth);
            _overflowRect = new Rectangle(Width - PadX - OverflowWidth + 2, 2, OverflowWidth - 4, Height - 5);
        }
    }

    /// <summary>右端 limit まで置く。全部置けたら true。</summary>
    private bool Place(int[] widths, int limit)
    {
        var x = PadX;
        for (var i = 0; i < _items.Count; i++)
        {
            if (x + widths[i] > limit)
            {
                for (var j = i; j < _items.Count; j++) _hidden.Add(_items[j]);
                return false;
            }
            _rects.Add(new Rectangle(x, 2, widths[i], Height - 5));
            x += widths[i] + GapX;
        }
        return true;
    }

    private int Measure(Graphics g, BookmarkNode n)
    {
        var hasIcon = n.IsFolder || Icon(n) is not null;
        var text = Label(n);
        var textWidth = text.Length == 0
            ? 0
            : TextRenderer.MeasureText(g, text, ItemFont, new Size(int.MaxValue, 20),
                                       TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;
        var w = 6 + (hasIcon ? IconSize + IconGap : 0) + textWidth + 6;
        return Math.Clamp(w, 24, MaxItemWidth);
    }

    /// <summary>ファビコンだけで分かるものは題名を省く、ということはしない。Chrome と同じで題名は出す。</summary>
    private static string Label(BookmarkNode n) =>
        Strings.FolderName(n.Title).Replace("\r", " ").Replace("\n", " ").Trim();

    // ---------------------------------------------------------------- 描画

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Surface);

        // 下辺の区切り線。ページとの境目が無いと浮いて見える。
        using (var pen = new Pen(Theme.Border))
            g.DrawLine(pen, 0, Height - 1, Width, Height - 1);

        if (_items.Count == 0)
        {
            TextRenderer.DrawText(g, Strings.BarEmptyHint,
                ItemFont, new Rectangle(PadX, 0, Width - PadX * 2, Height - 1), Theme.Muted,
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            return;
        }

        for (var i = 0; i < _rects.Count; i++) DrawItem(g, _items[i], _rects[i], _hover == i);

        if (!_overflowRect.IsEmpty)
        {
            if (_hover == -2) FillHover(g, _overflowRect);
            TextRenderer.DrawText(g, "»", ItemFont, _overflowRect, Theme.Text,
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    private static void FillHover(Graphics g, Rectangle r)
    {
        using var path = TabItem.Rounded(new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1), 6);
        using var fill = new SolidBrush(Theme.CardHover);
        g.FillPath(fill, path);
    }

    private void DrawItem(Graphics g, BookmarkNode n, Rectangle r, bool hover)
    {
        if (hover) FillHover(g, r);

        var x = r.X + 6;
        if (n.IsFolder)
        {
            // フォルダは小さな四角。ファビコンと同じ幅を取るので並びが崩れない。
            var box = new Rectangle(x + S(1), r.Y + (r.Height - S(12)) / 2, IconSize - S(3), S(11));
            var lip = Math.Max(1, S(2));
            var tab = Math.Max(1, S(3));
            using (var fill = new SolidBrush(Theme.Muted)) g.FillRectangle(fill, box.X, box.Y + lip, box.Width, box.Height - lip);
            using (var fill = new SolidBrush(Theme.Muted)) g.FillRectangle(fill, box.X, box.Y, box.Width / 2, tab);
            x += IconSize + IconGap;
        }
        else if (Icon(n) is { } icon)
        {
            g.DrawImage(icon, x, r.Y + (r.Height - IconSize) / 2, IconSize, IconSize);
            x += IconSize + IconGap;
        }

        var text = new Rectangle(x, r.Y, r.Right - 6 - x, r.Height);
        if (text.Width > 0)
            TextRenderer.DrawText(g, Label(n), ItemFont, text, Theme.Text,
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    // ---------------------------------------------------------------- マウス

    private int HitTest(Point p)
    {
        if (!_overflowRect.IsEmpty && _overflowRect.Contains(p)) return -2;
        for (var i = 0; i < _rects.Count; i++)
            if (_rects[i].Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var h = HitTest(e.Location);
        if (h == _hover) return;
        _hover = h;
        Cursor = h == -1 ? Cursors.Default : Cursors.Hand;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover == -1) return;
        _hover = -1;
        Cursor = Cursors.Default;
        Invalidate();
    }

    /// <summary>
    /// 押した瞬間ではなく離したときに開く。MouseDown でメニューを出すと、
    /// 続けて飛んでくる MouseUp がドロップダウンの外側の click と見なされて即座に閉じる。
    /// フォルダを押しても何も出ないように見えるのはこれが理由。
    /// </summary>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var hit = HitTest(e.Location);

        if (e.Button == MouseButtons.Right)
        {
            if (hit >= 0) ShowItemMenu(_items[hit], e.Location);
            else ShowBarMenu(e.Location);
            return;
        }

        if (e.Button is not (MouseButtons.Left or MouseButtons.Middle)) return;

        if (hit == -2) { ShowFolderMenu(_hidden, new Point(_overflowRect.X, Height)); return; }
        if (hit < 0) return;

        var node = _items[hit];
        if (node.IsFolder) ShowFolderMenu(_store.Children(node.Id), new Point(_rects[hit].X, Height));
        else if (!string.IsNullOrWhiteSpace(node.Url))
            OpenRequested?.Invoke(node.Url!, e.Button == MouseButtons.Middle);
    }

    // ---------------------------------------------------------------- メニュー

    /// <summary>フォルダの中身をメニューで出す。入れ子はそのまま入れ子のメニューにする。</summary>
    private void ShowFolderMenu(List<BookmarkNode> children, Point at)
    {
        var menu = DarkMenu.Create(DeviceDpi);
        menu.ShowItemToolTips = true;   // 切り詰めた題名を全部読めるようにする
        var t = Environment.TickCount64;
        Fill(menu.Items, children, 0);
        Log.Write($"folder menu: built in {Environment.TickCount64 - t}ms");
        Popup(menu, at);
    }

    /// <summary>題名を切り詰めたときだけ、元の題名を吹き出しで読めるようにする。</summary>
    private static void Whole(ToolStripItem item, string full, string shown)
    {
        if (!ReferenceEquals(full, shown) && full != shown) item.ToolTipText = full;
    }

    /// <summary>
    /// メニューを出す。押さえている点が 2 つある。
    ///
    /// ひとつはフォーカス。ページ（WebView2）がフォーカスを持ったままだと、出した直後に
    /// 非アクティブと見なされてドロップダウンが閉じる。フォームを手前にして、
    /// メニュー自身にフォーカスを渡す。MainForm 側の右クリックと同じ理由。
    ///
    /// もうひとつは置き場所。項目数の多いフォルダはメニューが縦に長くなり、
    /// 位置を任せると Windows が収まる場所を探して隣のモニターへ飛ばすことがある。
    /// 画面座標で明示し、このバーが乗っているモニターの作業領域に収める。
    /// </summary>
    /// <summary>メニュー 1 枚の高さの上限。行数と画面の高さの小さい方。子のメニューにも同じものを使う。</summary>
    private (int cap, int row, int screen, int rows) MenuCap(Rectangle area)
    {
        var rowHeight = DarkMenu.ItemFont(DeviceDpi).Height + S(8);
        var screenCap = Math.Max(S(200), area.Height - S(80));
        var rowCap = rowHeight * MaxMenuRows + S(16);   // 16 は上下の余白と送り矢印のぶん
        return (Math.Min(screenCap, rowCap), rowHeight, screenCap, rowCap);
    }

    private void Popup(ContextMenuStrip menu, Point at)
    {
        FindForm()?.Activate();

        var area = Screen.FromControl(this).WorkingArea;

        // 高さの上限。以前は「画面の高さ − 80px」だけで見ていたが、それは事実上
        // 画面いっぱいまで伸ばしてよいという意味で、200% では 29 件のフォルダが
        // 画面を縦断してしまった（1 行 22px が 44px になるので、同じ件数で倍伸びる）。
        // 行数でも頭を打ち、あふれた分はメニュー側でスクロールさせる。
        var (cap, rowHeight, screenCap, rowCap) = MenuCap(area);
        menu.MaximumSize = new Size(0, cap);

        var size = menu.GetPreferredSize(Size.Empty);
        Log.Write($"folder menu: dpi={DeviceDpi} items={menu.Items.Count} row={rowHeight} " +
                  $"cap={cap} (screen {screenCap} / rows {rowCap}) height={size.Height}");
        var p = PointToScreen(at);
        p.X = Math.Clamp(p.X, area.Left, Math.Max(area.Left, area.Right - size.Width));
        p.Y = Math.Clamp(p.Y, area.Top, Math.Max(area.Top, area.Bottom - size.Height));

        menu.Show(p, ToolStripDropDownDirection.BelowRight);
        menu.Focus();
    }

    /// <summary>
    /// 子フォルダの中身は、開いたときに作る。
    ///
    /// 以前は開いた瞬間に木を丸ごと組み立てていた。取り込んだブックマーク（37 フォルダ・
    /// 2,363 件・ファビコン 1,414 個）を入れたフォルダでは、「»」を押すたびに 5.5 秒固まり、
    /// その間の連打が溜まって、開いては閉じるを繰り返した。
    /// </summary>
    private void FillLater(ToolStripMenuItem sub, string folderId, int depth)
    {
        // 空だと ▶ が出ず、開けるフォルダに見えない。仮の 1 件を置いておく。
        sub.DropDownItems.Add(new ToolStripMenuItem("…") { Enabled = false });
        var built = false;
        long shownFrom = 0;
        sub.DropDownOpening += (_, _) =>
        {
            if (built) return;
            built = true;
            var t = Environment.TickCount64;
            sub.DropDown.SuspendLayout();
            sub.DropDownItems.Clear();
            var children = _store.Children(folderId);
            Fill(sub.DropDownItems, children, depth);
            // 根のメニューと同じ高さで頭を打ち、あふれた分は送り矢印で動かす。
            // 1.0.78 で 272 件のフォルダを開いた直後に Voyager が応答しなくなった。
            // 組み立て自体は 516ms で終わっていたので、疑わしいのは表示の段。
            // 根のメニューには上限があって 37 件でも問題なく出ていたが、子には無かった。
            sub.DropDown.MaximumSize = new Size(0, MenuCap(Screen.FromControl(this).WorkingArea).cap);
            sub.DropDown.ResumeLayout();
            if (sub.DropDown is ToolStripDropDownMenu dd) dd.ShowItemToolTips = true;
            KeepOnSameScreen(sub);
            Log.Write($"folder submenu: items={children.Count} built in {Environment.TickCount64 - t}ms");
            shownFrom = Environment.TickCount64;
        };
        // 組み立ててから実際に出るまで。ここが出なければ、止まったのは表示の段。
        sub.DropDownOpened += (_, _) =>
        {
            if (shownFrom == 0) return;
            Log.Write($"folder submenu: shown after {Environment.TickCount64 - shownFrom}ms height={sub.DropDown.Height}");
            shownFrom = 0;
        };
    }

    /// <summary>
    /// 子のメニューを親と同じ画面に出す。
    ///
    /// 右に出す余地が無いとき、WinForms は隣のモニターに余地があればそちらへはみ出させる。
    /// バーの右端にあるフォルダ（bookmarks）の子が隣の画面へ出て、モニターが 1 枚なら
    /// 見えない位置になる。拡大率の違う画面をまたぐと描き直しも重い。
    /// 親の画面の右端に収まらないなら左へ出す。
    /// </summary>
    private static void KeepOnSameScreen(ToolStripMenuItem sub)
    {
        if (sub.Owner is not ToolStripDropDown parent) return;
        var area = Screen.FromRectangle(parent.Bounds).WorkingArea;
        var width = sub.DropDown.GetPreferredSize(Size.Empty).Width;
        var toLeft = parent.Bounds.Right + width > area.Right;
        sub.DropDownDirection = toLeft ? ToolStripDropDownDirection.Left : ToolStripDropDownDirection.Right;
        Log.Write($"folder submenu: {(toLeft ? "left" : "right")} (parent right={parent.Bounds.Right} width={width} screen right={area.Right})");
    }

    private void Fill(ToolStripItemCollection into, List<BookmarkNode> children, int depth)
    {
        if (children.Count == 0)
        {
            into.Add(DarkMenu.Item(Strings.MenuEmpty, () => { }, enabled: false));
            return;
        }

        foreach (var c in children)
        {
            var full = Label(c);
            var shown = DarkMenu.Fit(full, DeviceDpi);

            if (c.IsFolder)
            {
                var sub = new ToolStripMenuItem(shown);
                // 深く潜りすぎるメニューは操作できない。5 段で止めて、その先はサイドバーに任せる。
                if (depth >= 5) sub.DropDownItems.Add(DarkMenu.Item(Strings.MenuTooDeep, () => { }, enabled: false));
                else FillLater(sub, c.Id, depth + 1);
                Whole(sub, full, shown);
                into.Add(sub);
            }
            else if (!string.IsNullOrWhiteSpace(c.Url))
            {
                var url = c.Url!;
                var item = DarkMenu.Item(shown, () => OpenRequested?.Invoke(url, false));
                if (Icon(c) is { } icon) item.Image = icon;
                Whole(item, full, shown);
                into.Add(item);
            }
        }
    }

    private void ShowItemMenu(BookmarkNode node, Point at)
    {
        var menu = DarkMenu.Create(DeviceDpi);
        if (node.IsLink)
        {
            var url = node.Url ?? "";
            menu.Items.Add(DarkMenu.Item(Strings.Open, () => OpenRequested?.Invoke(url, false),
                                         !string.IsNullOrWhiteSpace(url)));
            menu.Items.Add(DarkMenu.Item(Strings.OpenInNewTab, () => OpenRequested?.Invoke(url, true),
                                         !string.IsNullOrWhiteSpace(url)));
            menu.Items.Add(new ToolStripSeparator());
        }

        var i = _items.IndexOf(node);
        menu.Items.Add(DarkMenu.Item(Strings.MoveLeft, () => Shift(node, -1), i > 0));
        menu.Items.Add(DarkMenu.Item(Strings.MoveRight, () => Shift(node, +1), i >= 0 && i < _items.Count - 1));
        menu.Items.Add(new ToolStripSeparator());
        if (node.IsLink && !string.IsNullOrEmpty(node.Icon))
            menu.Items.Add(DarkMenu.Item(Strings.ClearIcon, () => ClearIcon(node)));
        menu.Items.Add(FolderMenu.MoveTo(_store, node, DeviceDpi, to => MoveTo(node, to)));
        menu.Items.Add(DarkMenu.Item(Strings.MoveToUnsorted, () => MoveToOther(node)));
        menu.Items.Add(DarkMenu.Item(Strings.RemoveFromBar, () => RemoveFromBar(node)));
        Popup(menu, at);
    }

    private void ShowBarMenu(Point at)
    {
        var menu = DarkMenu.Create(DeviceDpi);
        var page = CurrentPageRequested?.Invoke();
        var canAdd = !string.IsNullOrWhiteSpace(page?.Url);
        menu.Items.Add(DarkMenu.Item(Strings.AddThisPage, AddCurrentPage, canAdd));
        menu.Items.Add(DarkMenu.Item(Strings.NewFolder, AddFolder));
        Popup(menu, at);
    }

    // ---------------------------------------------------------------- 操作

    private void AddCurrentPage()
    {
        var page = CurrentPageRequested?.Invoke();
        if (page is null || string.IsNullOrWhiteSpace(page.Value.Url)) return;

        var title = string.IsNullOrWhiteSpace(page.Value.Title)
            ? UrlHelper.HostTitle(page.Value.Url!)
            : page.Value.Title!;
        _store.AddLink(BookmarkStore.RootBar, title, page.Value.Url!);
        Commit();
    }

    private void AddFolder()
    {
        // 名前を聞く画面はまだ無い。既定の名前で作って、名前はサイドバーの F2 で変えてもらう。
        _store.AddFolder(BookmarkStore.RootBar, Strings.NewFolderName);
        Commit();
    }

    private void Shift(BookmarkNode node, int delta)
    {
        var i = _items.IndexOf(node);
        var to = i + delta;
        if (i < 0 || to < 0 || to >= _items.Count) return;
        _store.Move(node.Id, BookmarkStore.RootBar, to);
        Commit();
    }

    /// <summary>
    /// 保存しているファビコンを捨てる。取り込んだときの絵が古いまま固まっている
    /// （X の未読の赤丸など）ときの逃げ道。次にそのページを開けば入り直す。
    /// </summary>
    private void ClearIcon(BookmarkNode node)
    {
        node.Icon = null;
        node.UpdatedAt = DateTimeOffset.UtcNow;
        Commit();
    }

    private void MoveTo(BookmarkNode node, string folderId)
    {
        if (!_store.MoveToEnd(node.Id, folderId)) return;
        Log.Write($"bookmark moved from bar: {node.Kind} -> {(folderId is BookmarkStore.RootBar or BookmarkStore.RootOther ? folderId : "folder")}");
        Commit();
    }

    private void MoveToOther(BookmarkNode node)
    {
        _store.Move(node.Id, BookmarkStore.RootOther, _store.NextIndex(BookmarkStore.RootOther));
        Commit();
    }

    private void RemoveFromBar(BookmarkNode node)
    {
        if (node.IsFolder)
        {
            var n = _store.CountLinks(node.Id);
            if (n > 0 && MessageBox.Show(
                    Strings.DeleteFolderWarning(node.Title, n),
                    Strings.DeleteBookmark, MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning) != DialogResult.OK) return;
        }
        _store.Remove(node.Id);
        Commit();
    }

    private void Commit()
    {
        _store.Renumber(BookmarkStore.RootBar);
        _store.Save();
        Reload();
        StoreChanged?.Invoke();
    }
}
