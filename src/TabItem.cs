using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace Voyager;

/// <summary>タブ 1 個分の見た目。Chromium 版の .tab に合わせた角丸カード。</summary>
internal sealed class TabItem : Panel
{
    /// <summary>タブ名は Label ではなく自前で描く。
    /// Label は AutoSize=false だと幅が足りないとき先に「折り返し」を選ぶので、
    /// AutoEllipsis を立てても長い題名が 2 行になってタブからはみ出す。</summary>
    private static readonly Font LabelFont = Theme.Ui(9f);
    private string _text = "";
    private readonly Label _close;
    private bool _active;
    private bool _hover;

    public event EventHandler? Activated;
    public event EventHandler? CloseRequested;

    public TabItem()
    {
        Height = 30;
        Width = 172;
        Margin = new Padding(0, 4, 6, 0);
        BackColor = Theme.Surface;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);

        Cursor = Cursors.Hand;
        _close = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Right,
            Width = 26,
            Text = "×",
            ForeColor = Theme.Muted,
            Font = Theme.Ui(10f),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand,
        };

        Click += (_, _) => Activated?.Invoke(this, EventArgs.Empty);
        _close.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        _close.MouseEnter += (_, _) => { _close.ForeColor = Theme.Text; };
        _close.MouseLeave += (_, _) => { _close.ForeColor = Theme.Muted; };

        MouseEnter += (_, _) => { _hover = true; Invalidate(); };
        MouseLeave += (_, _) => { _hover = false; Invalidate(); };

        Controls.Add(_close);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string TabText
    {
        get => _text;
        set
        {
            if (_text == value) return;
            _text = value ?? "";
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Active
    {
        get => _active;
        set
        {
            if (_active == value) return;
            _active = value;
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowClose
    {
        get => _close.Visible;
        set
        {
            if (_close.Visible == value) return;
            _close.Visible = value;
            Invalidate();   // 題名を描ける幅が変わる
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Surface);

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Rounded(r, 10);
        using var fill = new SolidBrush(_active ? Theme.CardHover : _hover ? Theme.Card : Theme.Surface);
        g.FillPath(fill, path);
        using var pen = new Pen(_active ? Theme.Accent : Theme.Border);
        g.DrawPath(pen, path);

        // 1 行に収め、入らない分は末尾を「…」にする。折り返させない。
        var right = _close.Visible ? _close.Width : 8;
        var textRect = new Rectangle(12, 0, Math.Max(0, Width - 12 - right), Height);
        if (textRect.Width > 0 && _text.Length > 0)
            TextRenderer.DrawText(g, _text, LabelFont, textRect,
                _active ? Theme.Text : Theme.Muted,
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    internal static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var p = new GraphicsPath();
        var d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
