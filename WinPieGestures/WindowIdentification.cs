using System;
using System.Runtime.InteropServices;

namespace WinPieGestures;

internal readonly record struct WindowProcessIdentity(nint Hwnd, uint ProcessId, string ProcessName, string ExecutablePath)
{
    internal bool IsKnown => Hwnd != IntPtr.Zero && ProcessId != 0 &&
        !string.IsNullOrWhiteSpace(ProcessName) && !ProcessName.Equals("unknown.exe", StringComparison.OrdinalIgnoreCase);
}

/// <summary>捕捉和运行时共享的应用身份选择；不负责窗口激活。</summary>
internal static class WindowIdentification
{
    internal static Func<nint, nint>? RootWindowGetterOverride { get; set; }
    internal static Func<nint>? CursorWindowGetterOverride { get; set; }

    internal static WindowProcessIdentity Read(nint hwnd)
    {
        ActiveWindowHelper.GetWindowProcessInfo(hwnd, out uint pid, out string name, out string path);
        return new WindowProcessIdentity(hwnd, pid, name, path);
    }

    internal static WindowProcessIdentity Resolve(nint hit, nint root, bool componentMode)
    {
        WindowProcessIdentity rootIdentity = Read(root);
        if (rootIdentity.IsKnown && componentMode && hit != IntPtr.Zero && hit != root && root != IntPtr.Zero && GetRootWindow(hit) == root)
        {
            WindowProcessIdentity component = Read(hit);
            if (component.IsKnown && component.ProcessId != (uint)Environment.ProcessId) return component;
        }
        return rootIdentity;
    }

    internal static WindowProcessIdentity ResolveForeground(nint foreground)
    {
        // 键盘呼出不能仅因光标停在后台窗口就更换匹配应用，而把动作发给原前台。
        nint hit = ConfigManager.CurrentConfig.EnableComponentIdentification ? GetCursorWindow() : IntPtr.Zero;
        return Resolve(hit, foreground, ConfigManager.CurrentConfig.EnableComponentIdentification);
    }

    internal static nint GetRootWindow(nint hwnd)
    {
        if (hwnd == IntPtr.Zero) return IntPtr.Zero;
        if (RootWindowGetterOverride != null) return RootWindowGetterOverride(hwnd);
        return GetAncestor(hwnd, 2); // GA_ROOT：只沿窗口父链。
    }

    private static nint GetCursorWindow()
    {
        if (CursorWindowGetterOverride != null) return CursorWindowGetterOverride();
        return GetCursorPos(out Point point) ? WindowFromPoint(point) : IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { internal int X; internal int Y; }
    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(Point point);
}
