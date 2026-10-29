using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace WinPieGestures;

public static class ActiveWindowHelper
{
	private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

	[DllImport("user32.dll")]
	private static extern nint GetForegroundWindow();

	[DllImport("user32.dll", SetLastError = true)]
	private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern nint OpenProcess(uint processAccess, bool bInheritHandle, uint processId);

	[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	private static extern bool QueryFullProcessImageName(nint hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool CloseHandle(nint hObject);

	private static nint _cachedWindow = IntPtr.Zero;
	private static uint _cachedProcessId = 0;
	private static string _cachedProcessName = "unknown.exe";
	private static string _cachedExecutablePath = "";
	private static long _cachedTick = 0;
	private static readonly object _cacheLock = new object();

	internal static Func<nint>? ForegroundHwndGetterOverride { get; set; }

	public static nint GetForegroundHwnd()
	{
		if (ForegroundHwndGetterOverride != null)
		{
			return ForegroundHwndGetterOverride();
		}
		if (WheelFocusSwitcher.ForegroundWindowGetterOverride != null)
		{
			return WheelFocusSwitcher.ForegroundWindowGetterOverride();
		}
		try
		{
			return GetForegroundWindow();
		}
		catch
		{
			return IntPtr.Zero;
		}
	}

	public static string GetActiveWindowProcessName()
	{
		return GetActiveWindowInfo(out _);
	}

	public static string GetMatchingActiveWindowProcessName()
	{
		if (!ConfigManager.CurrentConfig.EnableComponentIdentification) return GetActiveWindowProcessName();
		return WindowIdentification.ResolveForeground(GetForegroundHwnd()).ProcessName;
	}

	public static string GetActiveWindowInfo(out nint foregroundWindow)
	{
		foregroundWindow = GetForegroundHwnd();
		GetWindowProcessInfo(foregroundWindow, out _, out string processName);
		return processName;
	}

	internal static Func<nint, uint>? RealPidGetterOverride { get; set; }
	internal static Func<nint, string>? ProcessNameGetterOverride { get; set; }
	internal static Func<nint, (uint ProcessId, string ExecutablePath)>? WindowProcessReaderOverride { get; set; }

	/// <summary>
	/// 读取指定窗口当前实时的进程 ID（直接调用 Win32，绕过 150ms 缓存）。
	/// </summary>
	public static uint GetWindowRealProcessId(nint window)
	{
		if (RealPidGetterOverride != null)
		{
			return RealPidGetterOverride(window);
		}
		if (window == IntPtr.Zero)
		{
			return 0;
		}
		try
		{
			GetWindowThreadProcessId(window, out uint pid);
			return pid;
		}
		catch
		{
			return 0;
		}
	}

	/// <summary>
	/// 获取指定窗口所在进程的可执行文件名（小写，与 <see cref="GetActiveWindowProcessName"/> 同一格式）。
	/// </summary>
	public static string GetProcessNameForWindow(nint window)
	{
		GetWindowProcessInfo(window, out _, out string processName);
		return processName;
	}

	/// <summary>
	/// 获取指定窗口的 PID 与进程名快照。
	/// </summary>
	public static void GetWindowProcessInfo(nint window, out uint processId, out string processName)
	{
		GetWindowProcessInfo(window, out processId, out processName, out _);
	}

	/// <summary>
	/// 捕捉器与运行时共用同一份窗口进程快照；路径只来自该窗口所属 PID，不追溯父进程。
	/// </summary>
	internal static void GetWindowProcessInfo(nint window, out uint processId, out string processName, out string executablePath)
	{
		processId = 0;
		processName = "unknown.exe";
		executablePath = "";
		if (window == IntPtr.Zero)
		{
			return;
		}

		if (ProcessNameGetterOverride != null || RealPidGetterOverride != null)
		{
			processId = RealPidGetterOverride != null ? RealPidGetterOverride(window) : 0;
			processName = ProcessNameGetterOverride != null ? ProcessNameGetterOverride(window) : "unknown.exe";
			return;
		}

		try
		{
			long now = Environment.TickCount64;
			lock (_cacheLock)
			{
				if (window == _cachedWindow && (now - _cachedTick) < 150)
				{
					processId = _cachedProcessId;
					processName = _cachedProcessName;
					executablePath = _cachedExecutablePath;
					return;
				}
			}

			(processId, executablePath) = WindowProcessReaderOverride != null
				? WindowProcessReaderOverride(window)
				: ReadWindowProcess(window);
			if (processId != 0 && !string.IsNullOrEmpty(executablePath))
			{
				string fileName = Path.GetFileName(executablePath);
				if (!string.IsNullOrEmpty(fileName))
				{
					processName = fileName.ToLowerInvariant();
				}
			}

			lock (_cacheLock)
			{
				_cachedWindow = window;
				_cachedProcessId = processId;
				_cachedProcessName = processName;
				_cachedExecutablePath = executablePath;
				_cachedTick = now;
			}
		}
		catch
		{
			processId = 0;
			processName = "unknown.exe";
			executablePath = "";
		}
	}

	private static (uint ProcessId, string ExecutablePath) ReadWindowProcess(nint window)
	{
		GetWindowThreadProcessId(window, out uint processId);
		if (processId == 0)
		{
			return (0, "");
		}

		nint hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
		if (hProcess != IntPtr.Zero)
		{
			try
			{
				uint size = 1024;
				StringBuilder sb = new StringBuilder((int)size);
				if (QueryFullProcessImageName(hProcess, 0, sb, ref size))
				{
					return (processId, sb.ToString());
				}
			}
			finally
			{
				CloseHandle(hProcess);
			}
		}
		return (processId, "");
	}

	internal static void ResetTestSeams()
	{
		ForegroundHwndGetterOverride = null;
		RealPidGetterOverride = null;
		ProcessNameGetterOverride = null;
		WindowProcessReaderOverride = null;
		lock (_cacheLock)
		{
			_cachedWindow = IntPtr.Zero;
			_cachedProcessId = 0;
			_cachedProcessName = "unknown.exe";
			_cachedExecutablePath = "";
			_cachedTick = 0;
		}
	}
}
