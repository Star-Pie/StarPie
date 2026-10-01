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
	private static string _cachedProcessName = "unknown.exe";
	private static long _cachedTick = 0;
	private static readonly object _cacheLock = new object();

	public static string GetActiveWindowProcessName()
	{
		return GetActiveWindowInfo(out _);
	}

	public static string GetActiveWindowInfo(out nint foregroundWindow)
	{
		foregroundWindow = IntPtr.Zero;
		try
		{
			foregroundWindow = GetForegroundWindow();
			if (foregroundWindow == IntPtr.Zero)
			{
				return "unknown.exe";
			}

			long now = Environment.TickCount64;
			lock (_cacheLock)
			{
				if (foregroundWindow == _cachedWindow && (now - _cachedTick) < 150)
				{
					return _cachedProcessName;
				}
			}

			return ResolveProcessName(foregroundWindow);
		}
		catch
		{
			return "unknown.exe";
		}
	}

	/// <summary>
	/// 指定窗口所在进程的可执行文件名（小写，与 <see cref="GetActiveWindowProcessName"/> 同一格式）。
	///
	/// <para>存在的理由：轮盘方案是按进程名取的，而「用鼠标呼出轮盘时该属于哪个程序」必须看
	/// <b>呼出点所在的窗口</b>，不是前台窗口 —— 多显示器上把光标移到另一块屏幕时，前台窗口还在原屏幕上，
	/// 于是轮盘会错用另一个程序的方案（用户得呼出第二次才正确）。</para>
	/// </summary>
	public static string GetProcessNameForWindow(nint window)
	{
		if (window == IntPtr.Zero)
		{
			return "unknown.exe";
		}
		try
		{
			long now = Environment.TickCount64;
			lock (_cacheLock)
			{
				// 与前台路径共用同一格缓存：键就是 hwnd，查的是别的窗口只会未命中并重算，不会读到错的名字。
				if (window == _cachedWindow && (now - _cachedTick) < 150)
				{
					return _cachedProcessName;
				}
			}
			return ResolveProcessName(window);
		}
		catch
		{
			return "unknown.exe";
		}
	}

	private static string ResolveProcessName(nint window)
	{
		GetWindowThreadProcessId(window, out var lpdwProcessId);
		if (lpdwProcessId == 0)
		{
			return "unknown.exe";
		}

		nint hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, lpdwProcessId);
		if (hProcess != IntPtr.Zero)
		{
			try
			{
				uint size = 1024;
				StringBuilder sb = new StringBuilder((int)size);
				if (QueryFullProcessImageName(hProcess, 0, sb, ref size))
				{
					string fullPath = sb.ToString();
					string fileName = Path.GetFileName(fullPath);
					if (!string.IsNullOrEmpty(fileName))
					{
						string procName = fileName.ToLowerInvariant();
						lock (_cacheLock)
						{
							_cachedWindow = window;
							_cachedProcessName = procName;
							_cachedTick = Environment.TickCount64;
						}
						return procName;
					}
				}
			}
			finally
			{
				CloseHandle(hProcess);
			}
		}

		lock (_cacheLock)
		{
			_cachedWindow = window;
			_cachedProcessName = "unknown.exe";
			_cachedTick = Environment.TickCount64;
		}
		return "unknown.exe";
	}
}
