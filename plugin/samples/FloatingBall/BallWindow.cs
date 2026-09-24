using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace StarPie.Plugin.FloatingBall;

/// <summary>
/// 悬浮球的球体窗口 —— 纯代码构建，不带 XAML（产物里也不该出现第二个依赖）。
/// <para>
/// <b>位置用物理像素、尺寸由「DIU 直径 × 所在显示器的 DPI」现算。</b>这不是混了两套单位，
/// 而是各自取最优：位置必须与「显示器怎么排布」这个物理事实一致 —— 宿主
/// <see cref="IHostWheelService.ShowWheel"/> 要的就是虚拟屏幕坐标系的物理像素，
/// 改用 WPF 的 <c>Left</c>/<c>Top</c>（DIU，且相对当前显示器）在混合 DPI 多屏下会得到
/// 「球在副屏上看着对，点它轮盘却跑到主屏」；而直径按 DIU 声明，换到 200% 屏上球应当
/// 跟着变大，而不是缩成一个点。落位时两个值一起交给 <c>SetWindowPos</c>，
/// 与宿主 <c>RadialWindow.PositionWindowOnTargetMonitor</c> 同一套路子。
/// </para>
/// <para>
/// <b>实例不可变</b>：直径 / 颜色 / 不透明度都在构造时定死，要改就换一个新的球。
/// 少掉三个 setter 就少掉三类「参数改了但视觉没跟上」的漂移，而插件侧没有任何机器护栏
/// 查得见这种漂移 —— 唯一守得住它的手段是让它没有地方发生。
/// </para>
/// </summary>
internal sealed class BallWindow : Window
{
    /// <summary>按下到抬起的位移不超过这个物理像素数就算「点击」，否则算拖动。</summary>
    private const int ClickSlopPhysical = 4;

    /// <summary>第一次落位时右侧留的空（物理像素）：贴住屏幕右缘会压住系统托盘图标。</summary>
    private const int DefaultRightMargin = 40;

    private static readonly Color DefaultFill = Color.FromRgb(88, 132, 222);

    private readonly double _diameterDiu;

    private POINT _grabOffset;
    private bool _dragging;
    private bool _pressed;
    private POINT _pressCursor;

    /// <summary>落位点（物理像素，窗口左上角）。两个分量都为负表示「还没定过，用默认落点」。</summary>
    private POINT _location = new(-1, -1);

    public BallWindow(double diameterDiu, double opacity, string fillColor)
    {
        _diameterDiu = Math.Clamp(diameterDiu, 12, 220);

        // 与宿主轮盘同一条纪律：收点击但绝不抢前台。
        // 一个会抢焦点的球意味着「点完球再按 Ctrl+C，复制打进的是球的窗口」。
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;

        Width = _diameterDiu;
        Height = _diameterDiu;
        Content = BuildShape(opacity, fillColor);

        // 事件订阅而不是覆写 OnXxx：宿主工程 UseWindowsForms 与 WPF 并存，
        // 覆写会在两个同名消息类型之间撞车（CS0115，实测踩过）。
        MouseLeftButtonDown += OnLeftButtonDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnLeftButtonUp;
        MouseRightButtonUp += (_, _) => HideRequested?.Invoke();
        SourceInitialized += OnSourceInitialized;
    }

    /// <summary>用户点了一下球。参数是球心在虚拟屏幕坐标系里的物理像素坐标。</summary>
    public event Action<double, double>? WheelRequested;

    /// <summary>用户用右键收起球。</summary>
    public event Action? HideRequested;

    /// <summary>一次拖动结束（松手时触发一次，供调用方把位置落盘）。</summary>
    public event Action? Moved;

    /// <summary>
    /// 指定落位坐标（物理像素，虚拟屏幕坐标系）。可以在 <c>Show()</c> 之前调用。
    /// <para>
    /// 刻意不做成「Show 之后再定位」：那样窗口会先在系统默认位置画一帧，用户看到的是一次跳动。
    /// </para>
    /// </summary>
    public void RequestPhysicalLocation(int left, int top)
    {
        _location = new POINT(left, top);
        if (new WindowInteropHelper(this).Handle != nint.Zero) ApplyLocation();
    }

    /// <summary>球在虚拟屏幕坐标系里的物理矩形。拿不到句柄时是 0×0，调用方据此判断「还没落位」。</summary>
    public PhysicalRect ReadPhysicalRect()
    {
        nint hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == nint.Zero || !GetWindowRect(hwnd, out RECT rect)) return default;

        return new PhysicalRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    /// <summary>球心的物理像素坐标。</summary>
    public POINT CenterPhysical
    {
        get
        {
            PhysicalRect rect = ReadPhysicalRect();
            return new POINT(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        }
    }

    // ------------------------------------------------------------------ 交互

    private void OnLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pressed = true;
        _dragging = false;

        if (!GetCursorPos(out POINT cursor)) return;

        PhysicalRect rect = ReadPhysicalRect();
        _pressCursor = cursor;
        _grabOffset = new POINT(cursor.X - rect.X, cursor.Y - rect.Y);
        CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_pressed || !HasDraggedFarEnough()) return;
        if (!GetCursorPos(out POINT cursor)) return;

        // 跟手用的是「光标物理坐标 - 按下时的抓取偏移」，全程不涉及 WPF 坐标：
        // 一旦拿 Mouse.GetPosition 的 DIU 增量去推物理位置，跨屏拖动就必然漂。
        RequestPhysicalLocation(cursor.X - _grabOffset.X, cursor.Y - _grabOffset.Y);
    }

    private void OnLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pressed) return;

        _pressed = false;
        ReleaseMouseCapture();

        if (_dragging)
        {
            // Moved 只在松手时响一次（不在每次 MouseMove 上响）：落盘的是「松手时看到的位置」，
            // 而拖动中途的每一个坐标都只是路过 —— 挂在这上面等于把一次拖拽变成几十次写盘。
            _dragging = false;
            Moved?.Invoke();
            return;
        }

        // 这里不判断轮盘是否真的呼出来了：ShowWheel 的 true 只代表宿主受理
        // （SDK 注释里写的就是这个语义）。球跟着改视觉状态，只会多出
        // 「球的样式和屏幕上实际有的东西不一致」这第二种现象。
        POINT center = CenterPhysical;
        WheelRequested?.Invoke(center.X, center.Y);
    }

    private bool HasDraggedFarEnough()
    {
        if (_dragging) return true;
        if (!GetCursorPos(out POINT cursor)) return false;

        if (Math.Abs(cursor.X - _pressCursor.X) <= ClickSlopPhysical
            && Math.Abs(cursor.Y - _pressCursor.Y) <= ClickSlopPhysical)
        {
            return false;
        }

        _dragging = true;
        return true;
    }

    // ------------------------------------------------------------------ 外观

    private static UIElement BuildShape(double opacity, string fillColor)
    {
        var grid = new Grid { Opacity = Math.Clamp(opacity, 0.08, 1.0) };

        grid.Children.Add(new Ellipse
        {
            Fill = ParseFill(fillColor),

            // 球是半透明的，没有描边就看不出边界 —— 而「哪儿算球、哪儿算球后面那个窗口」
            // 直接决定用户的下一次点击落在谁身上。
            Stroke = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
            StrokeThickness = 1.2,
            Margin = new Thickness(2),
            Effect = new DropShadowEffect
            {
                BlurRadius = 10,
                ShadowDepth = 0,
                Opacity = 0.35,
                Color = Colors.Black,
            },
        });

        return grid;
    }

    private static SolidColorBrush ParseFill(string fillColor)
    {
        Color parsed;

        try
        {
            // 只认颜色串，不认「Sc#R0.5G0.5B0.5 #RRGGBBAA」之外的形态：ConvertFromString 的返回是 object，
            // 硬转会在用户填了个怪字符串时抛出异常 —— 那由下面的 catch 接住并退回默认色。
            parsed = (Color)ColorConverter.ConvertFromString(fillColor)!;
        }
        catch (Exception)
        {
            // 用户在颜色参数里手打了半个 `#FF0`。这不该让球干脆不出来。
            parsed = DefaultFill;
        }

        // 颜色串里的 alpha 被丢掉：透明度归 opacity 参数独管。
        // 两处各乘一遍的结果是「设成 80% 实际得到 32%」，而用户看到的只是「颜色没生效」。
        return new SolidColorBrush(Color.FromRgb(parsed.R, parsed.G, parsed.B));
    }

    // ------------------------------------------------------------------ Win32

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        nint hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != nint.Zero)
        {
            // AllowsTransparency 会让 WPF 自己加上 WS_EX_LAYERED，但 NOACTIVATE / TOOLWINDOW
            // 没人给 —— 缺前者会抢前台，缺后者会在 Alt+Tab 列表里多出一项「球」。
            nint ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, ex | (nint)(WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
        }

        // 无条件定位：没被要求过落点时 ApplyLocation 会算出首次落点（右侧留白、垂直偏上），
        // 被要求过（开机恢复）时用它拿到的那个坐标。跳过这一步就等于把位置交给 WPF 的默认值，
        // 而那时用户看到的球既不在承诺的位置，也没有任何东西会再把它挪过去。
        ApplyLocation();
    }

    private void ApplyLocation()
    {
        nint hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == nint.Zero) return;

        POINT anchor = _location;
        bool firstPlacement = anchor.X < 0 && anchor.Y < 0;

        // 首次落位还不知道最终位置，先用「按主屏宽度估出来的球心」问一次 DPI；
        // 之后的每一次定位都已经有了确切坐标，取到的是准确值。
        if (firstPlacement)
        {
            int guessedWidth = (int)Math.Round(_diameterDiu);
            anchor = new POINT(
                GetSystemMetrics(SM_CXSCREEN) - guessedWidth - DefaultRightMargin,
                (GetSystemMetrics(SM_CYSCREEN) - guessedWidth) * 2 / 5);
        }

        double scale = ReadDpiScale(CenterOf(anchor));
        int width = (int)Math.Round(_diameterDiu * scale);
        int height = (int)Math.Round(_diameterDiu * scale);

        POINT placed = firstPlacement
            ? new POINT(GetSystemMetrics(SM_CXSCREEN) - width - DefaultRightMargin,
                        (GetSystemMetrics(SM_CYSCREEN) - height) * 2 / 5)
            : anchor;

        (int left, int top) = ClampToVirtualScreen(placed.X, placed.Y, width, height);
        _location = new POINT(left, top);

        SetWindowPos(hwnd, HWND_TOPMOST, left, top, width, height, SWP_NOACTIVATE);
    }

    private POINT CenterOf(POINT topLeft)
    {
        int radius = (int)Math.Round(_diameterDiu / 2);
        return new POINT(topLeft.X + radius, topLeft.Y + radius);
    }

    /// <summary>
    /// 指定点所在显示器的 DPI 缩放系数。
    /// <para>
    /// 按<b>球心</b>而不是窗口左上角取显示器：贴左边缘放置时左上角可能落到另一块屏上，
    /// 于是尺寸按副屏算、位置按主屏摆 —— 正是要避开的那类混合 DPI 偏差。
    /// </para>
    /// </summary>
    private static double ReadDpiScale(POINT at)
    {
        try
        {
            nint monitor = MonitorFromPoint(at, MONITOR_DEFAULTTONEAREST);
            if (monitor != nint.Zero
                && GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY) == 0
                && dpiX > 0 && dpiY > 0)
            {
                // 两轴取较大者：本插件把球当正方形画，混着缩放会让它变成椭圆。
                return Math.Max(dpiX, dpiY) / 96.0;
            }
        }
        catch (DllNotFoundException)
        {
            // SHCore 缺失（极老系统）时退回 100%：球只是尺寸不跟随缩放，比崩掉好。
        }
        catch (EntryPointNotFoundException)
        {
        }

        return 1.0;
    }

    private static (int Left, int Top) ClampToVirtualScreen(int left, int top, int width, int height)
    {
        int virtualLeft = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int virtualTop = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int virtualWidth = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int virtualHeight = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        // 拿不到虚拟屏尺寸（异常会话）时别乱夹 —— 夹错了比夹不住更难解释。
        if (virtualWidth <= 0 || virtualHeight <= 0) return (left, top);

        int maxLeft = virtualLeft + Math.Max(0, virtualWidth - width);
        int maxTop = virtualTop + Math.Max(0, virtualHeight - height);

        return (
            Math.Min(Math.Max(left, virtualLeft), Math.Max(virtualLeft, maxLeft)),
            Math.Min(Math.Max(top, virtualTop), Math.Max(virtualTop, maxTop)));
    }

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_NOACTIVATE = 0x08000000;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int MDT_EFFECTIVE_DPI = 0;
    private static readonly nint HWND_TOPMOST = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;

        public POINT(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    /// <summary>物理像素矩形（虚拟屏幕坐标系）。默认值 0×0 表示「还没拿到句柄 / 未落位」。</summary>
    public readonly record struct PhysicalRect(int X, int Y, int Width, int Height);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("SHCore.dll", SetLastError = true)]
    private static extern int GetDpiForMonitor(nint hMonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    // GetWindowLongPtr / SetWindowLongPtr 只是 64 位上的名字，32 位要退回不带 Ptr 的那套。
    // 宿主只出 x64，但成对入口是宿主里的现成写法，照抄比自创省事。
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern nint GetWindowLongPtr64(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern nint GetWindowLong32(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern nint SetWindowLongPtr64(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern nint SetWindowLong32(nint hWnd, int nIndex, nint dwNewLong);

    private static nint GetWindowLongPtr(nint hWnd, int nIndex) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);

    private static nint SetWindowLongPtr(nint hWnd, int nIndex, nint value) =>
        IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, value) : SetWindowLong32(hWnd, nIndex, value);
}
