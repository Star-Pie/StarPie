using System;
using System.Runtime.InteropServices;
using System.Text;

namespace WinPieGestures;

public static class FullScreenHelper
{
	internal struct RECT
	{
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
	private struct MONITORINFO
	{
		public int cbSize;
		public RECT rcMonitor;
		public RECT rcWork;
		public uint dwFlags;
	}

	private const uint MONITOR_DEFAULTTONEAREST = 2u;
	private const int GWL_EXSTYLE = -20;
	private const int GWL_STYLE = -16;
	private const int WS_MAXIMIZE = 0x01000000;
	private const int WS_CAPTION = 0x00C00000;
	private const int WS_EX_LAYERED = 0x00080000;
	private const int WS_EX_TRANSPARENT = 0x00000020;

	[DllImport("user32.dll")]
	private static extern nint GetForegroundWindow();

	[DllImport("user32.dll")]
	private static extern nint GetShellWindow();

	[DllImport("user32.dll")]
	private static extern nint GetDesktopWindow();

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

	[DllImport("user32.dll")]
	private static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

	[DllImport("user32.dll", CharSet = CharSet.Auto)]
	private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

	[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
	private static extern int GetClassName(nint hWnd, StringBuilder lpClassName, int nMaxCount);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern int GetWindowLong(nint hWnd, int nIndex);

	private static nint _cachedHwnd = IntPtr.Zero;
	private static bool _cachedFullScreen = false;
	private static long _cachedTick = 0;
	private static readonly object _lock = new object();

	public static bool IsActiveWindowFullScreen(nint knownForegroundWindow = 0, string? knownProcessName = null)
	{
		nint foregroundWindow = knownForegroundWindow != IntPtr.Zero ? knownForegroundWindow : GetForegroundWindow();
		if (foregroundWindow == IntPtr.Zero)
		{
			return false;
		}

		long now = Environment.TickCount64;
		lock (_lock)
		{
			if (foregroundWindow == _cachedHwnd && (now - _cachedTick) < 150)
			{
				return _cachedFullScreen;
			}
		}

		if (foregroundWindow == GetShellWindow() || foregroundWindow == GetDesktopWindow())
		{
			lock (_lock) { _cachedHwnd = foregroundWindow; _cachedFullScreen = false; _cachedTick = now; }
			return false;
		}

		StringBuilder sbClass = new StringBuilder(256);
		GetClassName(foregroundWindow, sbClass, 256);
		string className = sbClass.ToString();

		if (string.Equals(className, "Progman", StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(className, "WorkerW", StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(className, "SHELLDLL_DefView", StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(className, "SysListView32", StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(className, "Shell_TrayWnd", StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(className, "Shell_SecondaryTrayWnd", StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(className, "Windows.UI.Core.CoreWindow", StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(className, "ScreenClippingHost", StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(className, "SnippingTool", StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(className, "SnippingToolHost", StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(className, "Snipaste", StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(className, "PixPin", StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(className, "ScreenCaptureWnd", StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(className, "CChatRoomScreenCaptureWnd", StringComparison.OrdinalIgnoreCase))
		{
			lock (_lock) { _cachedHwnd = foregroundWindow; _cachedFullScreen = false; _cachedTick = now; }
			return false;
		}

		string activeProc = knownProcessName ?? ActiveWindowHelper.GetActiveWindowProcessName();
		if (string.Equals(activeProc, "explorer.exe", StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(activeProc, "screenclippinghost.exe", StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(activeProc, "snippingtool.exe", StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(activeProc, "snippingtoolhost.exe", StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(activeProc, "snipaste.exe", StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(activeProc, "pixpin.exe", StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(activeProc, "sharex.exe", StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(activeProc, "flameshot.exe", StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(activeProc, "lightshot.exe", StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(activeProc, "shellexperiencehost.exe", StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(activeProc, "startmenuexperiencehost.exe", StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(activeProc, "searchhost.exe", StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(activeProc, "textinputhost.exe", StringComparison.OrdinalIgnoreCase))
		{
			lock (_lock) { _cachedHwnd = foregroundWindow; _cachedFullScreen = false; _cachedTick = now; }
			return false;
		}

		if (!GetWindowRect(foregroundWindow, out var lpRect))
		{
			lock (_lock) { _cachedHwnd = foregroundWindow; _cachedFullScreen = false; _cachedTick = now; }
			return false;
		}
		nint hMonitor = MonitorFromWindow(foregroundWindow, 2u);
		if (hMonitor == IntPtr.Zero)
		{
			lock (_lock) { _cachedHwnd = foregroundWindow; _cachedFullScreen = false; _cachedTick = now; }
			return false;
		}
		MONITORINFO lpmi = default(MONITORINFO);
		lpmi.cbSize = Marshal.SizeOf(lpmi);
		if (!GetMonitorInfo(hMonitor, ref lpmi))
		{
			lock (_lock) { _cachedHwnd = foregroundWindow; _cachedFullScreen = false; _cachedTick = now; }
			return false;
		}

		bool isFs = false;
		if (CoversMonitor(lpRect, lpmi.rcMonitor))
		{
			// 检查是否为透明/分层窗口 (如截屏工具遮罩、透明悬浮窗、HUD等)，这类窗口绝非独占全屏游戏
			int exStyle = GetWindowLong(foregroundWindow, GWL_EXSTYLE);
			int style = GetWindowLong(foregroundWindow, GWL_STYLE);
			isFs = IsFullScreenSnapshot(lpRect, lpmi.rcMonitor, style, exStyle);
		}

		lock (_lock)
		{
			_cachedHwnd = foregroundWindow;
			_cachedFullScreen = isFs;
			_cachedTick = now;
		}
		return isFs;
	}

	private static bool CoversMonitor(RECT window, RECT monitor) =>
		window.Left <= monitor.Left && window.Top <= monitor.Top &&
		window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;

	internal static bool IsFullScreenSnapshot(RECT window, RECT monitor, int style, int exStyle)
	{
		// 自动隐藏任务栏时，带标题栏的最大化窗口也可能覆盖整个显示器；
		// 无标题栏的全屏窗口即使保留最大化标记，也仍按原来的全屏策略处理。
		bool isCaptionedMaximized = (style & WS_MAXIMIZE) != 0 && (style & WS_CAPTION) == WS_CAPTION;
		return !isCaptionedMaximized && CoversMonitor(window, monitor) &&
			(exStyle & WS_EX_LAYERED) == 0 && (exStyle & WS_EX_TRANSPARENT) == 0;
	}
}
