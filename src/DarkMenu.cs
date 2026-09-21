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
    /// <summary>メニューの字。Fit() の採寸と食い違わないよう、1 か所に置く。</summary>
    public static readonly Font ItemFont = Theme.Ui(9f);

    /// <summary>題名 1 行に許す幅（画素）。これを超えたら末尾を「…」にする。</summary>
    public const int MaxTextWidth = 320;

    public static ContextMenuStrip Create() => new()
    {
        Renderer = new DarkMenuRenderer(),
        BackColor = Theme.Card,
        ForeColor = Theme.Text,
        Font = ItemFont,
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
    public static string Fit(string text, int max = MaxTextWidth)
    {
        if (string.IsNullOrEmpty(text)) return text;
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

    private static int Width(string s) =>
        TextRenderer.MeasureText(s, ItemFont, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;
}
