using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;

namespace WinPieGestures;

/// <summary>
/// 目标窗口身份快照：在同一次手势会话中保持同一份窗口 HWND、PID 和进程信息。
/// </summary>
public sealed class TargetWindowInfo
{
	public nint Hwnd { get; }
	public uint ProcessId { get; }
	public string ProcessName { get; }
	public bool IsEligibleBackgroundTarget { get; }
	internal WindowProcessIdentity MatchingIdentity { get; }
	public string MatchingProcessName => MatchingIdentity.ProcessName;

	public TargetWindowInfo(nint hwnd, uint processId, string? processName, bool isEligibleBackgroundTarget)
		: this(hwnd, processId, processName, isEligibleBackgroundTarget, null) { }

	internal TargetWindowInfo(nint hwnd, uint processId, string? processName, bool isEligibleBackgroundTarget,
		WindowProcessIdentity? matchingIdentity)
	{
		Hwnd = hwnd;
		ProcessId = processId;
		ProcessName = string.IsNullOrWhiteSpace(processName) ? "unknown.exe" : processName.Trim().ToLowerInvariant();
		IsEligibleBackgroundTarget = isEligibleBackgroundTarget;
		MatchingIdentity = matchingIdentity is { IsKnown: true } identity
			? identity : new WindowProcessIdentity(Hwnd, ProcessId, ProcessName, string.Empty);
	}

	internal ActionExecutionContext CreateExecutionContext(Func<bool> sessionValid, Action? onConsumed = null)
	{
		return new ActionExecutionContext(Hwnd, ProcessId, ProcessName,
			() => sessionValid() && WheelFocusSwitcher.IsTargetValid(this), onConsumed);
	}
	public static TargetWindowInfo None => new TargetWindowInfo(IntPtr.Zero, 0, "unknown.exe", false);
}

/// <summary>
/// 轮盘前台焦点切换状态。
/// </summary>
public enum WheelFocusState
{
	NotAttempted,
	AlreadyForeground,
	Focused,
	Failed,
	Invalidated
}

/// <summary>
/// 焦点切换执行结果。
/// </summary>
public enum FocusSwitchResult
{
	AlreadyForeground,
	Activated,
	NoTarget,
	Failed
}

/// <summary>
/// 轮盘呼出会话的焦点上下文，跟踪目标窗口与焦点状态。
/// </summary>
public sealed class WheelFocusSession
{
	private int _isCancelled;

	public long SessionId { get; }
	public Point StartPoint { get; }
	public TargetWindowInfo TargetInfo { get; }
	public WheelFocusState FocusState { get; set; } = WheelFocusState.NotAttempted;

	public bool IsCancelled
	{
		get => Volatile.Read(ref _isCancelled) != 0;
		set => Volatile.Write(ref _isCancelled, value ? 1 : 0);
	}

	public void Cancel()
	{
		Volatile.Write(ref _isCancelled, 1);
	}

	public WheelFocusSession(TargetWindowInfo targetInfo, Point startPoint, long sessionId = 0)
	{
		SessionId = sessionId;
		StartPoint = startPoint;
		TargetInfo = targetInfo ?? TargetWindowInfo.None;
	}

	public WheelFocusSession(long sessionId, Point startPoint, TargetWindowInfo targetInfo)
	{
		SessionId = sessionId;
		StartPoint = startPoint;
		TargetInfo = targetInfo ?? TargetWindowInfo.None;
	}
}

/// <summary>
/// 负责在鼠标呼出轮盘时解析呼出点下的顶层窗口并在适当阶段对齐前台焦点。
/// 仅服务于鼠标呼出路径；键盘触发、插件与悬浮球保持原有行为。
/// </summary>
public static class WheelFocusSwitcher
{
	[StructLayout(LayoutKind.Sequential)]
	private struct POINT
	{
		public int X;
		public int Y;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct RECT
	{
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;
	}

	private const uint GA_ROOT = 2u;
	private const uint GW_HWNDNEXT = 2u;
	private const int GWL_EXSTYLE = -20;
	private const long WS_EX_NOACTIVATE = 0x08000000L;
	private const long WS_EX_TRANSPARENT = 0x00000020L;

	/// <summary>
	/// 沿 z 序下钻的最大窗口遍历步数。通常穿透层（StarPie透明层或OSD）下 1~3 层即为目标；
	/// 限制为 32 步避免钩子热路径遍历成百上千不可见窗口导致卡顿。
	/// </summary>
	private const int MaxZOrderWalk = 32;

	private static readonly HashSet<string> ShellWindowClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"Progman",
		"WorkerW",
		"Shell_TrayWnd",
		"Shell_SecondaryTrayWnd"
	};

	[DllImport("user32.dll")]
	private static extern nint WindowFromPoint(POINT point);

	[DllImport("user32.dll")]
	private static extern nint GetAncestor(nint hWnd, uint gaFlags);

	[DllImport("user32.dll")]
	private static extern nint GetTopWindow(nint hWnd);

	[DllImport("user32.dll")]
	private static extern nint GetWindow(nint hWnd, uint uCmd);

	[DllImport("user32.dll", EntryPoint = "IsWindow")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool IsWindowNative(nint hWnd);

	[DllImport("user32.dll", EntryPoint = "IsWindowVisible")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool IsWindowVisibleNative(nint hWnd);

	internal static bool IsWindow(nint hWnd)
	{
		if (IsWindowOverride != null)
		{
			return IsWindowOverride(hWnd);
		}
		try
		{
			return IsWindowNative(hWnd);
		}
		catch
		{
			return false;
		}
	}

	internal static bool IsWindowVisible(nint hWnd)
	{
		if (IsWindowOverride != null)
		{
			return IsWindowOverride(hWnd);
		}
		try
		{
			return IsWindowVisibleNative(hWnd);
		}
		catch
		{
			return false;
		}
	}

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool IsIconic(nint hWnd);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

	[DllImport("user32.dll")]
	private static extern nint GetShellWindow();

	[DllImport("user32.dll")]
	private static extern nint GetDesktopWindow();

	[DllImport("user32.dll")]
	private static extern nint GetForegroundWindow();

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetClassName(nint hWnd, StringBuilder lpClassName, int nMaxCount);

	[DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
	private static extern nint GetWindowLongPtr64(nint hWnd, int nIndex);

	[DllImport("user32.dll", EntryPoint = "GetWindowLong")]
	private static extern nint GetWindowLong32(nint hWnd, int nIndex);

	private static nint GetWindowLongPtr(nint hWnd, int nIndex)
	{
		return (IntPtr.Size == 8) ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);
	}

	#region 诊断与测试切缝 (Diagnostics & Test Seams)
	internal static Func<Point, TargetWindowInfo>? TargetResolverOverride { get; set; }
	internal static Func<nint, bool>? WindowActivatorOverride { get; set; }
	internal static Func<nint>? ForegroundWindowGetterOverride { get; set; }
	internal static Func<nint, bool>? IsWindowOverride { get; set; }

	internal static void ResetTestSeams()
	{
		TargetResolverOverride = null;
		WindowActivatorOverride = null;
		ForegroundWindowGetterOverride = null;
		IsWindowOverride = null;
		ActiveWindowHelper.RealPidGetterOverride = null;
		ActiveWindowHelper.ProcessNameGetterOverride = null;
	}
	#endregion

	/// <summary>
	/// 获取当前前台窗口（支持测试切缝覆盖）。
	/// </summary>
	public static nint GetForegroundWindowSafe()
	{
		if (ForegroundWindowGetterOverride != null)
		{
			return ForegroundWindowGetterOverride();
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

	/// <summary>
	/// 解析呼出点下应当作为目标的顶层窗口快照。
	/// 若命中合格的后台窗口，则标记为 <see cref="TargetWindowInfo.IsEligibleBackgroundTarget"/>；
	/// 若命中桌面/任务栏或无有效目标，则回退至当前前台窗口并标记为 false。
	/// </summary>
	public static TargetWindowInfo ResolveTarget(Point physicalPoint)
	{
		if (TargetResolverOverride != null)
		{
			return TargetResolverOverride(physicalPoint);
		}

		try
		{
			POINT pt = new POINT
			{
				X = (int)Math.Round(physicalPoint.X),
				Y = (int)Math.Round(physicalPoint.Y)
			};

			nint targetHwnd = ResolveTargetWindow(pt, out nint hitHwnd);
			nint currentFg = GetForegroundWindowSafe();
			if (targetHwnd != IntPtr.Zero)
			{
				TargetWindowInfo? target = CreateTargetSnapshot(hitHwnd, targetHwnd, targetHwnd != currentFg);
				if (target != null) return target;
			}

			// 桌面/任务栏/无效命中仍回退前台，不能借用被拒绝窗口的组件身份。
			if (currentFg != IntPtr.Zero)
			{
				ActiveWindowHelper.GetWindowProcessInfo(currentFg, out uint fgPid, out string fgProc);
				return new TargetWindowInfo(currentFg, fgPid, fgProc, isEligibleBackgroundTarget: false);
			}
			return TargetWindowInfo.None;
		}
		catch (Exception ex)
		{
			AppLogger.LogDebug($"WheelFocusSwitcher.ResolveTarget: {ex.GetType().Name}: {ex.Message}");
			return TargetWindowInfo.None;
		}
	}

	internal static TargetWindowInfo? CreateForegroundTargetSnapshot()
	{
		nint root = ActiveWindowHelper.GetForegroundHwnd();
		WindowProcessIdentity rootIdentity = WindowIdentification.Read(root);
		if (!rootIdentity.IsKnown) return null;
		return new TargetWindowInfo(root, rootIdentity.ProcessId, rootIdentity.ProcessName, false,
			WindowIdentification.ResolveForeground(root));
	}
	internal static TargetWindowInfo? CreateTargetSnapshot(nint hit, nint root, bool backgroundTarget)
	{
		WindowProcessIdentity rootIdentity = WindowIdentification.Read(root);
		if (!rootIdentity.IsKnown) return null;
		WindowProcessIdentity matching = WindowIdentification.Resolve(hit, root,
			ConfigManager.CurrentConfig.EnableComponentIdentification);
		return new TargetWindowInfo(root, rootIdentity.ProcessId, rootIdentity.ProcessName, backgroundTarget, matching);
	}
	/// <summary>
	/// 尝试对齐前台焦点至会话指定的目标窗口。
	/// 采用保守激活：不注入 Alt，不切换 Topmost 状态。
	/// 成功获得前台焦点返回 Activated 或 AlreadyForeground；失败返回 Failed。
	/// </summary>
	public static FocusSwitchResult SwitchFocus(WheelFocusSession session)
	{
		if (session == null || session.IsCancelled)
		{
			return FocusSwitchResult.Failed;
		}

		if (!session.TargetInfo.IsEligibleBackgroundTarget)
		{
			session.FocusState = WheelFocusState.AlreadyForeground;
			return FocusSwitchResult.AlreadyForeground;
		}

		if (!IsTargetValid(session.TargetInfo))
		{
			session.FocusState = WheelFocusState.Invalidated;
			return FocusSwitchResult.Failed;
		}

		nint currentFg = GetForegroundWindowSafe();
		if (currentFg == session.TargetInfo.Hwnd)
		{
			session.FocusState = WheelFocusState.AlreadyForeground;
			return FocusSwitchResult.AlreadyForeground;
		}

		bool ok;
		if (WindowActivatorOverride != null)
		{
			ok = WindowActivatorOverride(session.TargetInfo.Hwnd);
		}
		else
		{
			ok = WindowTaskbarHelper.ActivateWindowConservative(session.TargetInfo.Hwnd);
		}

		currentFg = GetForegroundWindowSafe();
		if (ok && currentFg == session.TargetInfo.Hwnd)
		{
			session.FocusState = WheelFocusState.Focused;
			AppLogger.LogDebug($"WheelFocusSwitcher: focus successfully aligned to window 0x{session.TargetInfo.Hwnd:X} ({session.TargetInfo.ProcessName}).");
			return FocusSwitchResult.Activated;
		}

		session.FocusState = WheelFocusState.Failed;
		AppLogger.LogWarn($"WheelFocusSwitcher: focus switch failed for window 0x{session.TargetInfo.Hwnd:X} ({session.TargetInfo.ProcessName}), current foreground is 0x{currentFg:X}.");
		return FocusSwitchResult.Failed;
	}

	/// <summary>
	/// 校验会话中的目标窗口是否仍然存活且进程身份未发生改变。
	/// 执行动作前必须重新确认目标仍有效，杜绝向已关闭或失效窗口发送输入。
	/// </summary>
	public static bool IsTargetValid(TargetWindowInfo target)
	{
		if (target == null || target.Hwnd == IntPtr.Zero)
		{
			return false;
		}

		try
		{
			if (!IsWindow(target.Hwnd) || !IsWindowVisible(target.Hwnd))
			{
				return false;
			}

			// 实时核验 PID，直接通过 Win32 读取，绕过 150ms 缓存
			uint currentPid = ActiveWindowHelper.GetWindowRealProcessId(target.Hwnd);
			if (currentPid == 0 || currentPid != target.ProcessId)
			{
				return false;
			}

			ActiveWindowHelper.GetWindowProcessInfo(target.Hwnd, out _, out string proc);
			if (!string.Equals(proc, target.ProcessName, StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}

			WindowProcessIdentity matching = target.MatchingIdentity;
			if (matching.Hwnd != target.Hwnd &&
				(!IsWindow(matching.Hwnd) || WindowIdentification.GetRootWindow(matching.Hwnd) != target.Hwnd ||
				 ActiveWindowHelper.GetWindowRealProcessId(matching.Hwnd) != matching.ProcessId ||
				 !string.Equals(ActiveWindowHelper.GetProcessNameForWindow(matching.Hwnd), matching.ProcessName, StringComparison.OrdinalIgnoreCase)))
			{
				return false;
			}
			return true;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// 解析呼出点下应当接管前台的顶层窗口；没有合适目标时返回 <see cref="IntPtr.Zero"/>。
	/// </summary>
	private static nint ResolveTargetWindow(POINT pt, out nint hitHwnd)
	{
		nint hit = WindowFromPoint(pt);
		hitHwnd = hit;
		nint hitRoot = WindowIdentification.GetRootWindow(hit);
		if (IsEligibleTarget(hitRoot, pt))
		{
			return hitRoot;
		}

		hitHwnd = IntPtr.Zero; // 原命中已被拒绝，覆盖层身份不得泄漏到下层目标。
		if (ShouldSearchBelow(hitRoot))
		{
			int steps = 0;
			for (nint h = GetTopWindow(IntPtr.Zero); h != IntPtr.Zero && steps < MaxZOrderWalk; h = GetWindow(h, GW_HWNDNEXT))
			{
				steps++;
				if (IsEligibleTarget(h, pt))
				{
					return h;
				}
			}
		}
		return IntPtr.Zero;
	}

	/// <summary>
	/// 命中点被拒绝之后，是否还有必要沿 z 序往下找：只有当命中的那层是「不接管前台的覆盖层」时才找。
	/// 桌面与任务栏是用户真正点在的东西，在它们上面往下钻只会钻到压在底下的无关程序。
	/// </summary>
	private static bool ShouldSearchBelow(nint hitRoot)
	{
		if (hitRoot == IntPtr.Zero)
		{
			return true;
		}
		if (IsShellSurface(hitRoot))
		{
			return false;
		}
		GetWindowThreadProcessId(hitRoot, out uint pid);
		if (pid == (uint)Environment.ProcessId)
		{
			return true;
		}
		long exStyle = GetWindowLongPtr(hitRoot, GWL_EXSTYLE).ToInt64();
		return (exStyle & WS_EX_NOACTIVATE) != 0L || (exStyle & WS_EX_TRANSPARENT) != 0L;
	}

	/// <summary>
	/// 该顶层窗口是否可以作为「呼出轮盘的界面」接管前台。
	/// </summary>
	private static bool IsEligibleTarget(nint hWnd, POINT pt)
	{
		if (hWnd == IntPtr.Zero || !IsWindow(hWnd))
		{
			return false;
		}
		if (!IsWindowVisible(hWnd) || IsIconic(hWnd))
		{
			return false;
		}
		if (!ContainsPoint(hWnd, pt))
		{
			return false;
		}
		GetWindowThreadProcessId(hWnd, out uint pid);
		if (pid == 0u || pid == (uint)Environment.ProcessId)
		{
			return false;
		}
		long exStyle = GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64();
		if ((exStyle & WS_EX_NOACTIVATE) != 0L || (exStyle & WS_EX_TRANSPARENT) != 0L)
		{
			return false;
		}
		return !IsShellSurface(hWnd);
	}

	private static bool ContainsPoint(nint hWnd, POINT pt)
	{
		if (!GetWindowRect(hWnd, out RECT rect))
		{
			return false;
		}
		return pt.X >= rect.Left && pt.X < rect.Right && pt.Y >= rect.Top && pt.Y < rect.Bottom;
	}

	private static bool IsShellSurface(nint hWnd)
	{
		if (hWnd == GetShellWindow() || hWnd == GetDesktopWindow())
		{
			return true;
		}
		StringBuilder sb = new StringBuilder(128);
		if (GetClassName(hWnd, sb, sb.Capacity) <= 0)
		{
			return false;
		}
		return ShellWindowClasses.Contains(sb.ToString());
	}
}
