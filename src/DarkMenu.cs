using System.Runtime.InteropServices;

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
/// メニューはフォーカスを取らないので、ホイールはフォーカスのあるページ（WebView2。別プロセスの窓）
/// へ送られ、アプリのメッセージフィルターには来ない（1.1.12 で確認）。そこで、メニューが
/// 開いている間だけ低レベルのマウスフック（WH_MOUSE_LL）を張って横取りする。
///
/// フックは専用のスレッドで受ける。1.1.13 では UI スレッドで受けていたため、メニューが開いている間は
/// パソコン全体のマウスの動きが 1 つ残らず UI スレッドを通り、UI スレッドが 243 件のフォルダを
/// 組み立てている（0.8 秒）ような間はマウスごと固まって、ひどく重くなった。
/// 専用スレッドは開いているメニューの枠と見比べるだけで、何も待たない。
/// 枠の中で回したホイールだけを、矢印と同じ内部の ScrollInternal(bool) で送るよう UI スレッドへ頼む。
/// </summary>
internal static class MenuWheel
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int WM_QUIT = 0x0012;
    private const uint WM_HOOK_ON = 0x8001;    // WM_APP + 1
    private const uint WM_HOOK_OFF = 0x8002;
    private const uint PM_NOREMOVE = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc proc, IntPtr hMod, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);

    /// <summary>開いているメニューと、その画面上の枠。UI スレッドが丸ごと差し替え、フックのスレッドは読むだけ。</summary>
    private sealed record Open(ToolStripDropDownMenu Menu, Rectangle Bounds);

    // いま開いているメニューだけを持つ（UI スレッド専用）。閉じたものは外すので、作り捨てのメニューが溜まらない。
    private static readonly List<ToolStripDropDownMenu> Menus = [];
    private static volatile Open[] _snapshot = [];
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
    // フックに渡したデリゲートは GC に回収されないよう、ずっと持っておく。
    private static readonly HookProc Proc = OnMouse;
    private static IntPtr _hook;          // フックのスレッド専用
    private static uint _threadId;        // フックのスレッド。0 ならまだ起こしていない
    private static bool _wantOn;          // UI スレッド専用。最後に頼んだ状態
    private static int _pending;          // UI スレッド専用。1 目盛り（120）に満たない細かい送りを貯める

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
            Update();
        };
        menu.LocationChanged += (_, _) => Update();
        menu.SizeChanged += (_, _) => Update();
        menu.Disposed += (_, _) => { Menus.Remove(menu); Update(); };
        if (menu.Visible) { Menus.Add(menu); Update(); }
    }

    /// <summary>枠の一覧を差し替え、メニューが 1 枚でも開いていればフックを張り、全部閉じたら外すよう頼む。</summary>
    private static void Update()
    {
        _snapshot = Menus.Where(m => !m.IsDisposed && m.Visible)
                         .Select(m => new Open(m, m.Bounds)).ToArray();

        var want = _snapshot.Length > 0;
        if (want == _wantOn) return;
        _wantOn = want;
        if (want) _pending = 0;
        if (_threadId == 0)
        {
            if (!want) return;
            StartThread();
        }
        PostThreadMessage(_threadId, want ? WM_HOOK_ON : WM_HOOK_OFF, IntPtr.Zero, IntPtr.Zero);
    }

    private static void StartThread()
    {
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            // メッセージの受け口を先に作っておかないと、最初の PostThreadMessage が落ちる。
            PeekMessage(out _, IntPtr.Zero, 0, 0, PM_NOREMOVE);
            _threadId = GetCurrentThreadId();
            ready.Set();
            Loop();
        })
        {
            IsBackground = true,
            Name = "Voyager menu wheel",
            Priority = ThreadPriority.AboveNormal,
        };
        thread.Start();
        ready.Wait();
    }

    /// <summary>フックのスレッド。フックの呼び出しはここで GetMessage している間に届く。</summary>
    private static void Loop()
    {
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.Message == WM_HOOK_ON && _hook == IntPtr.Zero)
            {
                _hook = SetWindowsHookEx(WH_MOUSE_LL, Proc, GetModuleHandle(null), 0);
                Log.Write(_hook == IntPtr.Zero
                    ? $"menu wheel: hook failed (error {Marshal.GetLastWin32Error()})"
                    : "menu wheel: on");
            }
            else if (msg.Message == WM_HOOK_OFF && _hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
            else if (msg.Message == WM_QUIT)
            {
                break;
            }
        }
    }

    /// <summary>フックのスレッドで呼ばれる。ここでは待たない。枠と見比べて、UI スレッドへ頼むだけ。</summary>
    private static IntPtr OnMouse(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && (int)wParam == WM_MOUSEWHEEL)
        {
            var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            var at = new Point(info.X, info.Y);
            var open = _snapshot;
            // 後から開いた子のメニューほど上に重なっているので、後ろから探す。
            for (var i = open.Length - 1; i >= 0; i--)
            {
                if (!open[i].Bounds.Contains(at)) continue;
                var menu = open[i].Menu;
                var delta = (int)(short)(info.MouseData >> 16);
                try
                {
                    menu.BeginInvoke(() => Handle(menu, delta));
                }
                catch (InvalidOperationException)
                {
                    break;   // ちょうど閉じて窓が無くなった
                }
                return (IntPtr)1;   // メニューの上で回したホイールは下のページへ流さない
            }
        }
        // メニューの外（別の画面も含む）で回したホイールと、マウスの他の動きは、そのまま流す。
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    /// <summary>UI スレッドで、メニューを送る。</summary>
    private static void Handle(ToolStripDropDownMenu target, int delta)
    {
        if (target.IsDisposed || !target.Visible) return;

        _pending += delta;
        var notches = _pending / 120;
        _pending %= 120;
        var lines = SystemInformation.MouseWheelScrollLines;
        var steps = notches * (lines <= 0 ? 3 : Math.Min(lines, 10));
        var moved = 0;
        for (var n = 0; n < Math.Abs(steps); n++)
        {
            var up = steps > 0;
            if (!CanScroll(target, up)) break;
            Scroll!.Invoke(target, [up]);
            moved++;
        }
        if (moved > 0)
        {
            RefreshArrows?.Invoke(target, null);
            target.Invalidate();
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
