using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace WinPieGestures;

/// <summary>
/// 鼠标呼出轮盘时的前台焦点对齐。
///
/// <para><b>要解决的问题</b>：触发键的按下/抬起被 StarPie 的低级鼠标钩子吞掉，宿主窗口因此收不到这次点击，
/// Windows 也就不会把它激活 —— 前台焦点仍留在原窗口上。多显示器场景下表现为：光标已移到显示器 2
/// 并在那里呼出轮盘，轮盘也画在显示器 2，但随后执行的动作（快捷键注入、窗口操作等）全部落到了
/// 显示器 1 上原来那个前台窗口里，形成误操作。</para>
///
/// <para><b>本类的边界</b>：只服务于「<b>鼠标</b>呼出轮盘」这一条路径 —— 由
/// <c>GestureController.ShowRadialUI</c> 在真正呈现轮盘之前调用一次。键盘触发路径、
/// 插件经 <c>IHostWheelService</c> 呼出的粘滞轮盘、以及任何不是「用鼠标呼出轮盘」的路径都不经过这里，
/// 它们的前台语义保持原样。</para>
///
/// <para><b>行为</b>：解析呼出点所在的顶层窗口，若它可被安全激活且尚不是前台窗口，就把它切到前台
/// （复用 <see cref="WindowTaskbarHelper.ActivateWindow"/> 处理 Windows 前台锁）。
/// 不做任何其它副作用：不注入点击、不改窗口样式、不移动窗口。</para>
/// </summary>
internal static class WheelFocusSwitcher
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

	// 竞态上限：z 序链理论上有限，这里只是防守异常情况下 GetWindow 返回自环。
	private const int MaxZOrderWalk = 4096;

	// 不接受激活的悬浮层（轮盘、轨迹浮层、HUD、OSD、输入法候选窗）与点击穿透窗口：
	// 它们不是用户想聚焦的「界面」，真正的目标在其下方，因此跳过并继续向下找。
	private const long WS_EX_NOACTIVATE = 0x08000000L;
	private const long WS_EX_TRANSPARENT = 0x00000020L;

	// 桌面、任务栏与 Shell 表面：把前台交给它们等于让用户当前软件失去焦点，没有任何收益。
	private static readonly HashSet<string> ShellWindowClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"Progman",
		"WorkerW",
		"SHELLDLL_DefView",
		"SysListView32",
		"Shell_TrayWnd",
		"Shell_SecondaryTrayWnd",
		"NotifyIconOverflowWindow",
		"TopLevelWindowForOverflowXamlIsland",
		"Windows.UI.Core.CoreWindow",
		"Shell_InputSwitchTopLevelWindow",
		"MultitaskingViewFrame",
		"ForegroundStaging"
	};

	[DllImport("user32.dll")]
	private static extern nint WindowFromPoint(POINT point);

	[DllImport("user32.dll")]
	private static extern nint GetAncestor(nint hWnd, uint gaFlags);

	[DllImport("user32.dll")]
	private static extern nint GetTopWindow(nint hWnd);

	[DllImport("user32.dll")]
	private static extern nint GetWindow(nint hWnd, uint uCmd);

	[DllImport("user32.dll")]
	private static extern nint GetForegroundWindow();

	[DllImport("user32.dll")]
	private static extern nint GetShellWindow();

	[DllImport("user32.dll")]
	private static extern nint GetDesktopWindow();

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
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

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

	/// <summary>
	/// 把前台焦点对齐到指定物理坐标所在的顶层窗口。
	/// 返回该坐标解析出的目标窗口句柄（<see cref="IntPtr.Zero"/> 表示没有可切换的目标）；
	/// 调用方可以据此判断「这次轮盘属于哪个窗口/哪个程序」。
	/// 目标已经是前台时不做任何动作。
	/// </summary>
	public static nint SwitchFocusToPoint(Point physicalPoint)
	{
		try
		{
			POINT pt = new POINT
			{
				X = (int)Math.Round(physicalPoint.X),
				Y = (int)Math.Round(physicalPoint.Y)
			};
			nint target = ResolveTargetWindow(pt);
			if (target == IntPtr.Zero)
			{
				return IntPtr.Zero;
			}
			if (target == GetForegroundWindow())
			{
				// 前台已经落在轮盘呼出点所在的窗口上：无需切换，也不做任何置顶/还原动作。
				return target;
			}
			bool activated = WindowTaskbarHelper.ActivateWindow(target);
			if (activated)
			{
				AppLogger.LogDebug($"WheelFocusSwitcher: wheel focus moved to window 0x{target.ToInt64():X} at ({pt.X},{pt.Y}), process '{ActiveWindowHelper.GetProcessNameForWindow(target)}'.");
			}
			else
			{
				AppLogger.LogDebug($"WheelFocusSwitcher: failed to activate window 0x{target.ToInt64():X} at ({pt.X},{pt.Y}).");
			}
			return target;
		}
		catch (Exception ex)
		{
			AppLogger.LogDebug($"WheelFocusSwitcher: {ex.GetType().Name}: {ex.Message}");
			return IntPtr.Zero;
		}
	}

	/// <summary>
	/// 呼出点所在窗口的进程名（小写 exe 名，与 <see cref="ActiveWindowHelper.GetActiveWindowProcessName"/> 同一格式）；
	/// 解析不到合适目标、或进程名取不出来时返回 <c>null</c>（调用方按原路径回落到前台窗口）。
	///
	/// <para><b>只读、不激活</b>：鼠标钩子线程可以安全调用。轮盘方案按进程名取，
	/// 所以这一步必须和前台切换看同一个窗口 —— 否则跨屏呼出时方案会取到另一块屏幕上那个旧前台窗口的进程，
	/// 用户得呼出第二次才能拿到该有的选项。</para>
	/// </summary>
	public static string? TryGetProcessNameAtPoint(Point physicalPoint)
	{
		try
		{
			POINT pt = new POINT
			{
				X = (int)Math.Round(physicalPoint.X),
				Y = (int)Math.Round(physicalPoint.Y)
			};
			nint target = ResolveTargetWindow(pt);
			if (target == IntPtr.Zero)
			{
				return null;
			}
			// 「取不出来」与「解析失败」对调用方是同一件事：都该回落到前台窗口的原行为。
			// 顶层的 UWP 进程经常 OpenProcess 不到（Suspended/受保护），那时 GetProcessNameForWindow
			// 只会给 "unknown.exe" —— 拿它去取方案等于把用户已经配好的方案换成默认方案。
			string processName = ActiveWindowHelper.GetProcessNameForWindow(target);
			return string.Equals(processName, "unknown.exe", StringComparison.OrdinalIgnoreCase) ? null : processName;
		}
		catch (Exception ex)
		{
			AppLogger.LogDebug($"WheelFocusSwitcher: {ex.GetType().Name}: {ex.Message}");
			return null;
		}
	}

	/// <summary>
	/// 解析呼出点下应当接管前台的顶层窗口；没有合适目标时返回 <see cref="IntPtr.Zero"/>。
	/// </summary>
	private static nint ResolveTargetWindow(POINT pt)
	{
		// 首选 WindowFromPoint：与系统的命中判定完全一致（自动排除隐藏、禁用、逐像素透明的窗口，
		// 也能正确处理 UWP 托管窗口与子窗口）。命中的是自己人的窗口时再走下面的兜底。
		nint hit = WindowFromPoint(pt);
		nint hitRoot = (hit != IntPtr.Zero) ? GetAncestor(hit, GA_ROOT) : IntPtr.Zero;
		if (IsEligibleTarget(hitRoot, pt))
		{
			return hitRoot;
		}

		// 兜底只对「命中的那层本身不该接管前台」成立 —— 也就是 StarPie 自己的窗口（轮盘 HWND 隐藏后
		// 仍占着原位置、轨迹浮层等）与 no-activate / 点击穿透的悬浮层，真正压在下面的那个窗口才是
		// 用户手指底下的界面。EnumWindows/GetTopWindow 的顺序即 z 序。
		//
		// ⚠️ 命中的是 Shell 表面（桌面、任务栏）时绝不能兜底：桌面铺满整块屏幕，沿 z 序往下找到的
		// 第一个「矩形盖住该点」的窗口就是压在桌面底下的那个最大化程序 —— 在桌面空白处或任务栏空白处
		// 呼出轮盘，会把那个程序的窗口抢到前台，用户点的是桌面，前台却跳给了别的软件。
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
	/// 桌面与任务栏是用户真正点在的东西（<c>MouseHook</c> 里任务栏那几下会放行原生点击），
	/// 在它们上面往下钻只会钻到压在底下的无关程序。
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
	/// 判据刻意保守：任何一条不满足都只是放弃切换（前台保持原样），绝不会切到错误窗口上。
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
			// StarPie 自身的窗口（轮盘、轨迹浮层、控制台）绝不作为焦点目标：
			// 把前台切给自己没有任何意义，还会让随后注入的按键打回自己身上。
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

	#region 诊断与测试切缝 (Diagnostics & Test Seams)
	/// <summary>
	/// 仅供 <c>scratch/test_wheel_focus</c> 无界面地断言「这个呼出点解析到了哪个窗口」。
	/// 只解析、不激活，因此探针不会改动任何窗口的前台状态或 z 序。
	/// </summary>
	internal static nint TestResolveTargetWindow(double physicalX, double physicalY)
	{
		return ResolveTargetWindow(new POINT
		{
			X = (int)Math.Round(physicalX),
			Y = (int)Math.Round(physicalY)
		});
	}
	#endregion
}
