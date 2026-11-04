namespace Voyager;

internal sealed class DarkColorTable : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => Theme.Card;
    public override Color ImageMarginGradientBegin => Theme.Card;
    public override Color ImageMarginGradientMiddle => Theme.Card;
    public override Color ImageMarginGradientEnd => Theme.Card;
    public override Color MenuItemSelected => Theme.CardHover;
    public override Color MenuItemSelectedGradientBegin => Theme.CardHover;
    public override Color MenuItemSelectedGradientEnd => Theme.CardHover;
    public override Color MenuItemBorder => Theme.Accent;
    public override Color MenuBorder => Theme.Border;
    public override Color SeparatorDark => Theme.Border;
    public override Color SeparatorLight => Theme.Border;
}

internal sealed class DarkMenuRenderer() : ToolStripProfessionalRenderer(new DarkColorTable())
{
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Muted;
        base.OnRenderItemText(e);
    }
}

internal static class DarkMenu
{
    /// <summary>
    /// メニューの字。Fit() の採寸と食い違うと切り詰めが狂うので、必ずここから引く。
    /// 拡大率を渡すのは、static で 1 個持つと 200% の画面で 100% の大きさのまま
    /// 描かれてしまうため（Font は作った時点の拡大率で実体が固まる）。
    /// </summary>
    public static Font ItemFont(int dpi) => Theme.Ui(9f, FontStyle.Regular, dpi);

    /// <summary>題名 1 行に許す幅。96 dpi 基準で、拡大率に合わせて伸ばす。</summary>
    public const int MaxTextWidth = 320;

    public static ContextMenuStrip Create(int dpi) => new()
    {
        Renderer = new DarkMenuRenderer(),
        BackColor = Theme.Card,
        ForeColor = Theme.Text,
        Font = ItemFont(dpi),
        ShowImageMargin = false,
        DropShadowEnabled = true,
    };

    public static ToolStripMenuItem Item(string text, Action action, bool enabled = true)
    {
        var item = new ToolStripMenuItem(text) { Enabled = enabled };
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>
    /// 長い題名を切り詰める。ToolStrip は一番長い項目に合わせて際限なく横に伸びるので、
    /// 取り込んだブックマークのように題名が 1 行まるごとあるものが 1 つ混じるだけで、
    /// メニューが画面の何割かを覆ってしまう。字の方を先に短くしておく。
    ///
    /// MaximumSize で止めない理由：あれは外枠を切るだけなので、字が「…」にならず
    /// 途中でぶつ切りになる。
    /// </summary>
    public static string Fit(string text, int dpi)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var max = (int)Math.Round(MaxTextWidth * dpi / 96.0);
        var font = ItemFont(dpi);
        int Width(string s) =>
            TextRenderer.MeasureText(s, font, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;

        if (Width(text) <= max) return text;

        // 入る長さを二分探索する。1 文字ずつ削ると、長い題名で採寸が何十回も走る。
        var lo = 0;
        var hi = text.Length;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (Width(text[..mid] + "…") <= max) lo = mid; else hi = mid - 1;
        }
        return lo == 0 ? "…" : text[..lo] + "…";
    }
}

/// <summary>
/// メニューをホイールで送れるようにする。
///
/// WinForms の ToolStripDropDownMenu は、上限より長いと上下に送り矢印を出すが、
/// ホイールには一切反応しない（ToolStrip 側にホイールの処理が無い）。項目の多いフォルダで
/// 矢印の上に止めておく以外に送る手段が無く、「スクロールが効かない」ように見えていた。
///
/// ホイールはカーソルの下の窓か、フォーカスのある窓のどちらかに届く（Windows の設定次第）。
/// どちらでも拾えるよう、メッセージを窓ではなくアプリ全体で受け、カーソルの下にある
/// 登録済みのメニューを送る。送る処理は矢印を押したときと同じ内部の ScrollInternal(bool) を使う。
/// </summary>
internal static class MenuWheel
{
    private const int WM_MOUSEWHEEL = 0x020A;

    // いま開いているメニューだけを持つ。閉じたものは外すので、作り捨てのメニューが溜まらない。
    private static readonly List<ToolStripDropDownMenu> Menus = [];
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ToolStripDropDownMenu, object> Hooked = new();
    private static readonly System.Reflection.MethodInfo? Scroll =
        typeof(ToolStripDropDownMenu).GetMethod("ScrollInternal",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            [typeof(bool)]);
    // 送った後に矢印の押せる・押せないを描き直す。無くても送ることはできる。
    private static readonly System.Reflection.MethodInfo? RefreshArrows =
        typeof(ToolStripDropDownMenu).GetMethod("UpdateScrollButtonStatus",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            Type.EmptyTypes);
    private static bool _installed;
    private static int _pending;   // 1 目盛り（120）に満たないタッチパッドの細かい送りを貯める

    public static void Enable(ToolStripDropDown dropDown)
    {
        if (dropDown is not ToolStripDropDownMenu menu || Hooked.TryGetValue(menu, out _)) return;
        if (Scroll is null)
        {
            Log.Write("menu wheel: ScrollInternal(bool) not found; wheel scrolling is off");
            return;
        }
        Hooked.Add(menu, new object());
        menu.VisibleChanged += (_, _) =>
        {
            Menus.Remove(menu);
            if (menu.Visible) Menus.Add(menu);
        };
        menu.Disposed += (_, _) => Menus.Remove(menu);
        if (menu.Visible) Menus.Add(menu);
        if (!_installed)
        {
            _installed = true;
            Application.AddMessageFilter(new Filter());
        }
    }

    private sealed class Filter : IMessageFilter
    {
        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != WM_MOUSEWHEEL || Menus.Count == 0) return false;

            // 後から開いた子のメニューほど上に重なっているので、後ろから探す。
            var at = Cursor.Position;
            ToolStripDropDownMenu? target = null;
            for (var i = Menus.Count - 1; i >= 0; i--)
            {
                var menu = Menus[i];
                if (menu.IsDisposed || !menu.Visible || !menu.Bounds.Contains(at)) continue;
                target = menu;
                break;
            }
            if (target is null) return false;

            _pending += (short)((long)m.WParam >> 16 & 0xFFFF);
            var notches = _pending / 120;
            _pending %= 120;
            var lines = SystemInformation.MouseWheelScrollLines;
            var steps = notches * (lines <= 0 ? 3 : Math.Min(lines, 10));
            for (var n = 0; n < Math.Abs(steps); n++)
            {
                var up = steps > 0;
                if (!CanScroll(target, up)) break;
                Scroll!.Invoke(target, [up]);
            }
            if (steps != 0) RefreshArrows?.Invoke(target, null);
            // 送れなくても、メニューの上で回したホイールは下のページへ流さない。
            return true;
        }
    }

    /// <summary>まだ隠れている項目がその向きにあるか。送り矢印が押せるかどうかと同じ判定。</summary>
    private static bool CanScroll(ToolStripDropDownMenu menu, bool up)
    {
        var area = menu.DisplayRectangle;
        ToolStripItem? first = null, last = null;
        foreach (ToolStripItem item in menu.Items)
        {
            if (!item.Available) continue;
            first ??= item;
            last = item;
        }
        if (first is null || last is null) return false;
        return up ? first.Bounds.Top < area.Top : last.Bounds.Bottom > area.Bottom;
    }
}
