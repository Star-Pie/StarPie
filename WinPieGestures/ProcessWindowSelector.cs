using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace WinPieGestures;

/// <summary>
/// 顶层窗口快照：解耦 Win32 状态读取与选择策略，支持纯数据单元测试与确定性回归。
/// </summary>
public sealed class WindowSnapshot
{
    public nint Handle { get; init; }
    public int ProcessId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string ClassName { get; init; } = string.Empty;
    public bool IsVisible { get; init; }
    public bool IsIconic { get; init; }
    public nint Owner { get; init; }
    public long Style { get; init; }
    public long ExStyle { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public bool IsForeground { get; init; }
    public bool IsMainWindowHint { get; init; }
}

/// <summary>
/// 进程窗口选择器：从目标进程所有顶层窗口中，根据样式、所有者、尺寸、标题与前台状态，
/// 排除工具/辅助/事件窗口，确定性选出真实的用户主界面窗口。
/// </summary>
public static class ProcessWindowSelector
{
    public const long WS_CHILD = 0x40000000L;
    public const long WS_POPUP = 0x80000000L;
    public const long WS_VISIBLE = 0x10000000L;
    public const long WS_MINIMIZE = 0x20000000L;
    public const long WS_CAPTION = 0x00C00000L;
    public const long WS_THICKFRAME = 0x00040000L;

    public const long WS_EX_TOOLWINDOW = 0x00000080L;
    public const long WS_EX_APPWINDOW = 0x00040000L;
    public const long WS_EX_NOACTIVATE = 0x08000000L;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint hWnd, uint uCmd);

    private const uint GW_OWNER = 4;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>
    /// 判断是否为预先排除不进行窗口切换的特殊程序（如资源管理器、终端、计算器等，保持直接启动新实例语义）。
    /// </summary>
    public static bool IsSpecialProgram(string? exeName)
    {
        if (string.IsNullOrWhiteSpace(exeName)) return false;
        string name = exeName.Trim().ToLowerInvariant();
        return name is "explorer" or "cmd" or "powershell" or "pwsh" or "wsl" or "calc" or "calculator" or "calculatorapp";
    }

    /// <summary>
    /// 判断类名或标题是否属于系统辅助窗口、IME 或事件分发目标。
    /// </summary>
    public static bool IsAuxiliaryClassOrTitle(string? className, string? title)
    {
        if (!string.IsNullOrWhiteSpace(className))
        {
            string cls = className.Trim();
            if (cls.Equals("Default IME", StringComparison.OrdinalIgnoreCase) ||
                cls.Equals("MSCTFIME UI", StringComparison.OrdinalIgnoreCase) ||
                cls.Equals("IME", StringComparison.OrdinalIgnoreCase) ||
                cls.Equals("tooltips_class32", StringComparison.OrdinalIgnoreCase) ||
                cls.Equals("OleMainThreadWndClass", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (cls.Contains("Event Target", StringComparison.OrdinalIgnoreCase) ||
                cls.Contains("tray_icon", StringComparison.OrdinalIgnoreCase) ||
                cls.Contains("global_hotkey", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            string t = title.Trim();
            if (t.Contains("Tao Thread Event Target", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("Default IME", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 校验窗口是否符合正常用户交互窗口的底线标准：
    /// 排除子窗口、不可激活窗口(WS_EX_NOACTIVATE)、无APPWINDOW的工具窗口、无APPWINDOW的拥有窗口，以及不可见且非最小化的后台窗口。
    /// </summary>
    public static bool IsValidUserWindowCandidate(WindowSnapshot window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (window.Handle == IntPtr.Zero)
            return false;

        // 必须为可见窗口或已最小化窗口；既不可见又未最小化的为纯后台/隐藏窗口
        if (!window.IsVisible && !window.IsIconic)
            return false;

        // 子窗口非顶层主窗
        if ((window.Style & WS_CHILD) != 0)
            return false;

        // 禁止激活的窗口（如花笺的后台事件通知窗口）绝不能作为用户交互主窗
        if ((window.ExStyle & WS_EX_NOACTIVATE) != 0)
            return false;

        // 工具窗口除非明确声明 WS_EX_APPWINDOW，否则不参与主窗选择
        if ((window.ExStyle & WS_EX_TOOLWINDOW) != 0 && (window.ExStyle & WS_EX_APPWINDOW) == 0)
            return false;

        // 被其他窗口拥有的从属窗口除非明确声明 WS_EX_APPWINDOW，否则排除
        if (window.Owner != IntPtr.Zero && (window.ExStyle & WS_EX_APPWINDOW) == 0)
            return false;

        // 排除系统辅助/事件目标
        if (IsAuxiliaryClassOrTitle(window.ClassName, window.Title))
            return false;

        // 对于非最小化的普通可见窗口，0x0 尺寸的哑窗口予以排除
        if (!window.IsIconic && window.IsVisible)
        {
            if (window.Width <= 0 || window.Height <= 0)
                return false;
        }

        return true;
    }

    /// <summary>
    /// 计算候选窗口排序权重，确保排序具有确定性且符合用户直觉：
    /// 当前前台窗口最高优（支持收回）；APPWINDOW/独立顶层窗口次之；标准标题栏次之；用户非空标题次之；MainWindowHandle 仅作合法后的轻微偏好。
    /// </summary>
    public static int CalculateCandidateScore(WindowSnapshot window)
    {
        int score = 0;

        // 1. 若当前窗口就是前台窗口，最高优先级（便于触发时将其最小化收回）
        if (window.IsForeground)
            score += 10000;

        // 2. 具有显式 APPWINDOW 属性
        if ((window.ExStyle & WS_EX_APPWINDOW) != 0)
            score += 500;

        // 3. 无 Owner 的独立顶级窗口
        if (window.Owner == IntPtr.Zero)
            score += 400;

        // 4. 具有标准标题栏或重绘边框
        if ((window.Style & WS_CAPTION) == WS_CAPTION)
            score += 300;

        // 5. 具有非空用户可见标题
        if (!string.IsNullOrWhiteSpace(window.Title))
            score += 200;

        // 6. 进程主窗口句柄提示（前提是它已经通过了合法性过滤）
        if (window.IsMainWindowHint)
            score += 100;

        // 7. 当前正常显示在桌面（非最小化）
        if (!window.IsIconic && window.IsVisible)
            score += 50;

        return score;
    }

    /// <summary>
    /// 从候选集合中筛选并确定性选出唯一目标窗口。
    /// 当权重相同时以 HWND 数值稳定平局，杜绝因 EnumWindows 顺序偶然波动导致选择分叉。
    /// </summary>
    public static WindowSnapshot? SelectCandidate(IEnumerable<WindowSnapshot> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);

        return windows
            .Where(IsValidUserWindowCandidate)
            .OrderByDescending(CalculateCandidateScore)
            .ThenBy(w => w.Handle.ToInt64())
            .FirstOrDefault();
    }

    /// <summary>
    /// 从实时 HWND 句柄捕获窗口属性快照。
    /// </summary>
    public static WindowSnapshot? CreateSnapshotFromHwnd(nint hWnd, nint foregroundHwnd = 0, nint mainWindowHint = 0)
    {
        if (hWnd == IntPtr.Zero || !IsWindow(hWnd))
            return null;

        GetWindowThreadProcessId(hWnd, out uint pid);

        var sbTitle = new StringBuilder(512);
        GetWindowText(hWnd, sbTitle, 512);

        var sbClass = new StringBuilder(256);
        GetClassName(hWnd, sbClass, 256);

        bool visible = IsWindowVisible(hWnd);
        bool iconic = IsIconic(hWnd);
        nint owner = GetWindow(hWnd, GW_OWNER);
        long style = (long)GetWindowLongPtr(hWnd, GWL_STYLE);
        long exStyle = (long)GetWindowLongPtr(hWnd, GWL_EXSTYLE);

        int width = 0;
        int height = 0;
        if (GetWindowRect(hWnd, out RECT rect))
        {
            width = rect.Right - rect.Left;
            height = rect.Bottom - rect.Top;
        }

        nint fg = foregroundHwnd != IntPtr.Zero ? foregroundHwnd : GetForegroundWindow();

        return new WindowSnapshot
        {
            Handle = hWnd,
            ProcessId = (int)pid,
            Title = sbTitle.ToString(),
            ClassName = sbClass.ToString(),
            IsVisible = visible,
            IsIconic = iconic,
            Owner = owner,
            Style = style,
            ExStyle = exStyle,
            Width = width,
            Height = height,
            IsForeground = (hWnd == fg),
            IsMainWindowHint = (mainWindowHint != IntPtr.Zero && hWnd == mainWindowHint)
        };
    }

    internal static List<WindowSnapshot> SnapshotWindows(HashSet<int> targetPids, HashSet<nint>? mainHints = null)
    {
        nint foregroundHwnd = GetForegroundWindow();
        var candidates = new List<WindowSnapshot>();

        EnumWindows((hWnd, lParam) =>
        {
            try
            {
                GetWindowThreadProcessId(hWnd, out uint pid);
                if (targetPids.Contains((int)pid))
                {
                    bool isHint = mainHints?.Contains(hWnd) == true;
                    var snapshot = CreateSnapshotFromHwnd(hWnd, foregroundHwnd, isHint ? hWnd : IntPtr.Zero);
                    if (snapshot != null)
                    {
                        candidates.Add(snapshot);
                    }
                }
            }
            catch
            {
            }
            return true;
        }, IntPtr.Zero);
        return candidates;
    }

    /// <summary>
    /// 查找指定可执行程序当前运行实例的最佳可用窗口候选。
    /// 确保内部 Process 实例在使用后及时释放，不留下未释放的句柄资源。
    /// </summary>
    public static WindowSnapshot? FindCandidateForProcess(string processOrExePath)
    {
        if (string.IsNullOrWhiteSpace(processOrExePath))
            return null;

        string exeName = Path.GetFileNameWithoutExtension(processOrExePath).Trim().ToLowerInvariant();
        if (IsSpecialProgram(exeName))
            return null;

        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(exeName);
            if (processes.Length == 0 && exeName.EndsWith("64"))
            {
                processes = Process.GetProcessesByName(exeName[..^2]);
            }
        }
        catch
        {
            return null;
        }

        if (processes == null || processes.Length == 0)
            return null;

        try
        {
            var targetPids = new HashSet<int>();
            var mainHints = new HashSet<nint>();
            foreach (var p in processes)
            {
                try
                {
                    targetPids.Add(p.Id);
                    nint mainHwnd = p.MainWindowHandle;
                    if (mainHwnd != IntPtr.Zero)
                    {
                        mainHints.Add(mainHwnd);
                    }
                }
                catch
                {
                }
            }

            if (targetPids.Count == 0)
                return null;

            return SelectCandidate(SnapshotWindows(targetPids, mainHints));
        }
        finally
        {
            foreach (var p in processes)
            {
                p.Dispose();
            }
        }
    }
}
