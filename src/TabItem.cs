using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace Voyager;

/// <summary>タブ 1 個分の見た目。Chromium 版の .tab に合わせた角丸カード。</summary>
internal sealed class TabItem : Panel
{
    /// <summary>タブ名は Label ではなく自前で描く。
    /// Label は AutoSize=false だと幅が足りないとき先に「折り返し」を選ぶので、
    /// AutoEllipsis を立てても長い題名が 2 行になってタブからはみ出す。</summary>
    private Font LabelFont => Theme.Ui(9f, FontStyle.Regular, DeviceDpi);
    private string _text = "";
    private readonly Label _close;
    private bool _active;
    private bool _hover;

    // 96 dpi 基準の寸法。実行中に作るタブは WinForms の自動拡大を通らないので、
    // ここの数字をそのまま置くと 200% の画面で半分の大きさになる。S() で換算する。
    private const int BaseHeight = 30;
    private const int BaseWidth = 172;
    private const int CloseWidth = 26;
    private const int TextPadLeft = 12;
    private const int TextPadRight = 8;
    private const int Radius = 10;

    /// <summary>96 dpi 基準の長さを、いまの拡大率の画素数に直す。</summary>
    internal int S(int logical) => (int)Math.Round(logical * DeviceDpi / 96.0);

    public event EventHandler? Activated;
    public event EventHandler? CloseRequested;

    /// <summary>ドラッグ中。int はマウスの画面上の X。並べ替えは受け手（MainForm）が行う。</summary>
    public event Action<TabItem, int>? DragMoved;

    /// <summary>ドラッグが終わった（離した、またはマウスを奪われた）。</summary>
    public event Action<TabItem>? DragEnded;

    private Point _downAt;
    private bool _pressed;
    private bool _dragging;
    private bool _dragged;   // 今回の押下でドラッグしたか。離したときの Click を無視するため

    public TabItem()
    {
        Height = BaseHeight;
        Width = BaseWidth;
        Margin = new Padding(0, 4, 6, 0);
        BackColor = Theme.Surface;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);

        Cursor = Cursors.Hand;
        _close = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Right,
            Width = CloseWidth,
            Text = "×",
            ForeColor = Theme.Muted,
            Font = Theme.Ui(10f),   // ApplyDpi で拡大率に合わせて差し替える
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand,
        };

        // WinForms は離したときに Click → MouseUp の順で呼ぶ。ドラッグした押下の Click は無視する。
        Click += (_, _) => { if (!_dragged) Activated?.Invoke(this, EventArgs.Empty); };
        _close.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        _close.MouseEnter += (_, _) => { _close.ForeColor = Theme.Text; };
        _close.MouseLeave += (_, _) => { _close.ForeColor = Theme.Muted; };

        MouseEnter += (_, _) => { _hover = true; Invalidate(); };
        MouseLeave += (_, _) => { _hover = false; Invalidate(); };

        Controls.Add(_close);
    }

    // ---------------------------------------------------------------- ドラッグで並べ替え

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        _pressed = true;
        _dragging = false;
        _dragged = false;
        _downAt = e.Location;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_pressed || (e.Button & MouseButtons.Left) == 0) return;
        if (!_dragging)
        {
            // 少し動いただけ（クリックの手ぶれ）ではドラッグにしない。
            var slop = SystemInformation.DragSize;
            if (Math.Abs(e.X - _downAt.X) < slop.Width && Math.Abs(e.Y - _downAt.Y) < slop.Height) return;
            _dragging = true;
            _dragged = true;
            Activated?.Invoke(this, EventArgs.Empty);   // Chrome と同じく、つかんだタブを前面にする
        }
        DragMoved?.Invoke(this, PointToScreen(e.Location).X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        EndDrag();
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture) EndDrag();   // Alt+Tab などでマウスを奪われた
    }

    private void EndDrag()
    {
        var was = _dragging;
        _pressed = false;
        _dragging = false;
        if (was) DragEnded?.Invoke(this);
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

    /// <summary>高さ・閉じるボタン・余白・字の大きさを、いまの拡大率に合わせ直す。
    /// 幅は MainForm.LayoutTabs が本数に応じて決めるので、ここでは触らない。</summary>
    private void ApplyDpi()
    {
        var h = S(BaseHeight);
        if (Height != h) Height = h;

        var w = S(CloseWidth);
        if (_close.Width != w) _close.Width = w;
        _close.Font = Theme.Ui(10f, FontStyle.Regular, DeviceDpi);

        Margin = new Padding(0, S(4), S(6), 0);
        Invalidate();
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
        using var path = Rounded(r, S(Radius));
        using var fill = new SolidBrush(_active ? Theme.CardHover : _hover ? Theme.Card : Theme.Surface);
        g.FillPath(fill, path);
        using var pen = new Pen(_active ? Theme.Accent : Theme.Border);
        g.DrawPath(pen, path);

        // 1 行に収め、入らない分は末尾を「…」にする。折り返させない。
        var left = S(TextPadLeft);
        var right = _close.Visible ? _close.Width : S(TextPadRight);
        var textRect = new Rectangle(left, 0, Math.Max(0, Width - left - right), Height);
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
