using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using WinPieGestures;

/// <summary>
/// StarPie「鼠标呼出轮盘时前台焦点自动对齐」（<see cref="WheelFocusSwitcher"/>）的端到端验证。
///
/// 一次性工具，不参与主程序构建（scratch 目录性质）。用法：
/// <code>powershell -ExecutionPolicy Bypass -File scratch\test_wheel_focus.ps1</code>
///
/// 为什么必须有它：这段逻辑的三条判据（只认别人的窗口、跳过 no-activate 悬浮层、沿 z 序兜底）
/// 全部够不着单元测试与 UI 回归套件 —— 它们要的是「屏幕上真的压着几层窗口」。
/// 这里用子进程拉起几个真窗口，把轮盘窗口那种「压在最上面又不是目标」的处境原地复现出来。
///
/// 每条用例都自带前置条件断言（见 <see cref="CheckOverlayIsTopHit"/>）：探针摆的那层窗口
/// 必须真的是该点上最上面的窗口，否则用例会因为「下面那层被解析出来」而假绿。
///
/// 注意：运行期间会短暂改动前台窗口（几秒），结束后恢复运行前的那个前台窗口。
/// </summary>
internal static class WheelFocusProbe
{
	[StructLayout(LayoutKind.Sequential)]
	private struct RECT
	{
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct POINT
	{
		public int X;
		public int Y;
	}

	private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

	[DllImport("user32.dll")]
	private static extern nint GetForegroundWindow();

	[DllImport("user32.dll")]
	private static extern nint WindowFromPoint(POINT point);

	[DllImport("user32.dll")]
	private static extern nint GetAncestor(nint hWnd, uint gaFlags);

	[DllImport("user32.dll")]
	private static extern nint GetTopWindow(nint hWnd);

	[DllImport("user32.dll")]
	private static extern nint GetWindow(nint hWnd, uint uCmd);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetClassName(nint hWnd, StringBuilder lpClassName, int nMaxCount);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool IsWindowVisible(nint hWnd);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool IsWindow(nint hWnd);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern nint FindWindow(string? lpClassName, string? lpWindowName);

	[DllImport("user32.dll")]
	private static extern nint GetShellWindow();

	[DllImport("user32.dll")]
	private static extern nint GetDesktopWindow();

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

	[DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
	private static extern nint GetWindowLongPtr64(nint hWnd, int nIndex);

	[DllImport("user32.dll", EntryPoint = "GetWindowLong")]
	private static extern nint GetWindowLong32(nint hWnd, int nIndex);

	private static nint GetWindowLongPtr(nint hWnd, int nIndex)
	{
		return (IntPtr.Size == 8) ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);
	}

	private const int GWL_EXSTYLE = -20;
	private const int WS_EX_NOACTIVATE = 0x08000000;
	private const int WS_EX_TOOLWINDOW = 0x00000080;
	private static readonly nint HWND_TOPMOST = new nint(-1);
	private const uint SWP_NOSIZE = 0x0001;
	private const uint SWP_NOMOVE = 0x0002;
	private const uint SWP_NOZORDER = 0x0004;
	private const uint SWP_NOACTIVATE = 0x0010;

	// 轮盘呼出点：A 窗口 (60,80,420,300) 的中心。
	private const int PointX = 270;
	private const int PointY = 230;

	private static int _failures;
	private static int _checks;

	private static bool Trace => Environment.GetEnvironmentVariable("TFW_TRACE") == "1";

	[STAThread]
	private static int Main(string[] args)
	{
		Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
		if (args.Length > 0 && string.Equals(args[0], "--child", StringComparison.OrdinalIgnoreCase))
		{
			return RunChildWindow(args);
		}
		if (args.Length > 0 && string.Equals(args[0], "--diag", StringComparison.OrdinalIgnoreCase))
		{
			return RunDiag(args);
		}
		if (args.Length > 0 && string.Equals(args[0], "--dump", StringComparison.OrdinalIgnoreCase))
		{
			// 不建窗口的坐标扫描：只回答「这个点上命中的是谁、解析成了谁」。
			foreach (string pair in (args.Length > 1 ? args[1] : string.Empty).Split(';'))
			{
				string[] xy = pair.Split(',');
				int dx = int.Parse(xy[0]);
				int dy = int.Parse(xy[1]);
				nint dh = WindowFromPoint(new POINT { X = dx, Y = dy });
				nint dr = GetAncestor(dh, 2u);
				StringBuilder dc = new StringBuilder(128);
				GetClassName(dr, dc, dc.Capacity);
				GetWindowThreadProcessId(dr, out uint dpid);
				long dex = GetWindowLongPtr(dr, GWL_EXSTYLE).ToInt64();
				Console.WriteLine($"({dx},{dy}) hit=0x{dh.ToInt64():X} root=0x{dr.ToInt64():X} 类={dc} pid={dpid} ex=0x{dex:X} 解析=0x{WheelFocusSwitcher.TestResolveTargetWindow(dx, dy).ToInt64():X}");
			}
			return 0;
		}
		return RunProbe();
	}

	// ------------------------------------------------------------------
	// 子进程模式：在指定矩形上摆一个真实窗口，供父进程当作「别的程序的窗口 / 悬浮层」。
	// ------------------------------------------------------------------
	private static int RunChildWindow(string[] args)
	{
		string title = ArgValue(args, "--title") ?? "TFW_CHILD";
		Rectangle bounds = ParseRect(ArgValue(args, "--rect") ?? "0,0,200,150");
		bool noActivate = HasFlag(args, "--noactivate");
		bool topmost = HasFlag(args, "--topmost");

		ProbeForm form = new ProbeForm(noActivate)
		{
			Text = title,
			FormBorderStyle = FormBorderStyle.FixedSingle,
			StartPosition = FormStartPosition.Manual,
			// ShowInTaskbar 一律为 true：WinForms 把它设成 false 时会把窗体挂到隐藏的
			// "parking window" 之下，于是它不再是 z 序里的顶层窗口 —— 那样就复现不出
			// 「轮盘 HWND 压在目标上面」的处境了（no-activate 变体靠 WS_EX_TOOLWINDOW 不进任务栏）。
			ShowInTaskbar = true,
			TopMost = topmost,
			BackColor = noActivate ? Color.FromArgb(255, 240, 200) : Color.FromArgb(200, 225, 255)
		};
		form.Bounds = bounds;
		Application.Run(form);
		return 0;
	}

	/// <summary>
	/// 诊断模式：在本进程里摆一个窗口，然后打印指定点上系统的真实命中结果与解析结果，
	/// 用来回答「探针摆的这个窗口到底有没有进 z 序 / 有没有被窗口解析器选中」。
	/// <code>test_wheel_focus.exe --diag --rect 170,130,200,150 --topmost --at 270,230</code>
	/// </summary>
	private static int RunDiag(string[] args)
	{
		Rectangle bounds = ParseRect(ArgValue(args, "--rect") ?? "170,130,200,150");
		string[] at = (ArgValue(args, "--at") ?? $"{PointX},{PointY}").Split(',');

		ProbeForm form = new ProbeForm(HasFlag(args, "--noactivate"))
		{
			Text = "TFW_DIAG",
			FormBorderStyle = FormBorderStyle.FixedSingle,
			StartPosition = FormStartPosition.Manual,
			ShowInTaskbar = true,
			TopMost = HasFlag(args, "--topmost")
		};
		form.Bounds = bounds;
		form.Show();
		Pump();
		form.EnsureTopmost();
		Thread.Sleep(200);
		Pump();

		int x = int.Parse(at[0]);
		int y = int.Parse(at[1]);
		Console.WriteLine($"本进程 PID: {Environment.ProcessId}");
		Console.WriteLine($"本进程窗口: 0x{form.Handle.ToInt64():X} {DescribeWindow(form.Handle, new POINT { X = x, Y = y })}");
		Console.WriteLine(DescribePoint(x, y));
		Console.WriteLine($"WheelFocusSwitcher 解析结果 = 0x{WheelFocusSwitcher.TestResolveTargetWindow(x, y).ToInt64():X}");
		form.Close();
		return 0;
	}

	private sealed class ProbeForm : Form
	{
		private readonly bool _noActivate;

		public ProbeForm(bool noActivate)
		{
			_noActivate = noActivate;
		}

		protected override CreateParams CreateParams
		{
			get
			{
				CreateParams cp = base.CreateParams;
				if (_noActivate)
				{
					cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
				}
				return cp;
			}
		}

		/// <summary>显示后强制一次 HWND_TOPMOST（仅在真正有消息循环的进程里有效，见 EnsureTopmost 注释）。</summary>
		protected override void OnShown(EventArgs e)
		{
			base.OnShown(e);
			EnsureTopmost();
		}

		/// <summary>
		/// 强制置顶。注意：在没有消息循环的进程里（本探针的父进程）SetWindowPos(HWND_TOPMOST)
		/// 会返回 true 却落不到扩展样式上（实测 ex 仍无 WS_EX_TOPMOST），所以父进程一侧改用
		/// 「把窗口激活到最前」来保证它真的压在目标窗口上面。
		/// </summary>
		public void EnsureTopmost()
		{
			if (TopMost && Handle != IntPtr.Zero)
			{
				bool ok = SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
				if (Trace)
				{
					Console.WriteLine($"      [trace] SetWindowPos(TOPMOST) ok={ok} err={Marshal.GetLastWin32Error()} ex=0x{GetWindowLongPtr(Handle, GWL_EXSTYLE).ToInt64():X}");
				}
			}
		}
	}

	// ------------------------------------------------------------------
	// 探针
	// ------------------------------------------------------------------
	private static int RunProbe()
	{
		Console.WriteLine("== StarPie 鼠标呼出轮盘 · 前台焦点对齐验证 ==");
		Console.WriteLine($"屏幕: {Screen.PrimaryScreen!.Bounds}  工作区: {Screen.PrimaryScreen.WorkingArea}");
		Console.WriteLine($"呼出点: ({PointX},{PointY})（A 窗口的中心）");
		Console.WriteLine("提示：若桌面上恰好有置顶窗口压在该点上，本套用例会以前置条件失败的方式报出来，请先关掉。");
		Console.WriteLine();

		nint originalForeground = GetForegroundWindow();
		List<Process> children = new List<Process>();
		List<Form> selfWindows = new List<Form>();

		try
		{
			children.Add(SpawnChild("TFW_A", new Rectangle(60, 80, 420, 300), noActivate: false, topmost: false));
			children.Add(SpawnChild("TFW_B", new Rectangle(560, 80, 420, 300), noActivate: false, topmost: false));
			nint hA = WaitForWindow("TFW_A", 20000);
			nint hB = WaitForWindow("TFW_B", 20000);
			if (hA == IntPtr.Zero || hB == IntPtr.Zero)
			{
				Check("准备：两个「别的程序」的窗口已出现", false, $"A=0x{hA.ToInt64():X} B=0x{hB.ToInt64():X}");
				return Finish(originalForeground, children, selfWindows);
			}
			Check("准备：两个「别的程序」的窗口已出现", true, $"A=0x{hA.ToInt64():X} B=0x{hB.ToInt64():X}");

			// [1] 呼出点解析：压在下面的是 A，就应当解析到 A。
			ActivateWindow(hA);
			CheckResolve("解析：呼出点落在 A 上 → 目标就是 A",
				WheelFocusSwitcher.TestResolveTargetWindow(PointX, PointY), hA, PointX, PointY);

			// [2] 核心行为：前台在 B（跨显示器时"前台在另一块屏"的那种处境），鼠标呼出轮盘后前台切到 A。
			ActivateWindow(hB);
			nint switched = WheelFocusSwitcher.SwitchFocusToPoint(new System.Windows.Point(PointX, PointY));
			bool switchedForeground = WaitForForeground(hA);
			Check("切换：前台切到呼出点所在的 A",
				switched == hA && switchedForeground,
				$"返回=0x{switched.ToInt64():X} 前台=0x{GetForegroundWindow().ToInt64():X} 期望=0x{hA.ToInt64():X}");

			// [3] 幂等：前台已经是 A 时，不应再做任何置顶/还原动作。
			nint again = WheelFocusSwitcher.SwitchFocusToPoint(new System.Windows.Point(PointX, PointY));
			Check("幂等：前台已经是 A 时不产生额外切换",
				again == hA && WaitForForeground(hA),
				$"返回=0x{again.ToInt64():X} 前台=0x{GetForegroundWindow().ToInt64():X}");

			// [4][5] StarPie 自身进程的窗口压在 A 上（轮盘 HWND 的处境）：不能当目标，必须沿 z 序兜底到 A。
			ActivateWindow(hA);
			Form selfTopmost = ShowSelfWindow("TFW_SELF", new Rectangle(170, 130, 200, 150), topmost: true);
			selfWindows.Add(selfTopmost);
			CheckOverlayIsTopHit("自身窗口：探针自身进程的窗口确实压在 A 上（前置条件）", selfTopmost.Handle, PointX, PointY);
			CheckResolve("自身窗口：压在 A 上的 StarPie 自己的窗口不是目标，兜底解析到 A",
				WheelFocusSwitcher.TestResolveTargetWindow(PointX, PointY), hA, PointX, PointY);
			ActivateWindow(hB);
			nint switchedUnderSelf = WheelFocusSwitcher.SwitchFocusToPoint(new System.Windows.Point(PointX, PointY));
			bool underSelfForeground = WaitForForeground(hA);
			Check("自身窗口：前台不会被切给自己，仍切到 A",
				switchedUnderSelf == hA && underSelfForeground,
				$"返回=0x{switchedUnderSelf.ToInt64():X} 前台=0x{GetForegroundWindow().ToInt64():X} 期望=0x{hA.ToInt64():X}");
			selfTopmost.Close();
			selfWindows.Remove(selfTopmost);
			Pump();

			// [6][7] 别的进程的 WS_EX_NOACTIVATE 悬浮层（HUD / OSD / 桌面挂件）：同样跳过，继续找下面那层。
			children.Add(SpawnChild("TFW_NA", new Rectangle(170, 130, 200, 150), noActivate: true, topmost: true));
			nint hNa = WaitForWindow("TFW_NA", 20000);
			if (hNa == IntPtr.Zero)
			{
				Check("悬浮层：其它进程的 no-activate 置顶窗口已出现", false, "未找到 TFW_NA");
			}
			else
			{
				ActivateWindow(hA);
				CheckOverlayIsTopHit("悬浮层：探针的 no-activate 悬浮层确实压在最上面（前置条件）", hNa, PointX, PointY);
				CheckResolve("悬浮层：其它进程的 no-activate 置顶窗口不是目标，兜底解析到 A",
					WheelFocusSwitcher.TestResolveTargetWindow(PointX, PointY), hA, PointX, PointY);
				ActivateWindow(hB);
				nint switchedUnderNa = WheelFocusSwitcher.SwitchFocusToPoint(new System.Windows.Point(PointX, PointY));
				bool underNaForeground = WaitForForeground(hA);
				Check("悬浮层：前台不会被切给 no-activate 悬浮层，仍切到 A",
					switchedUnderNa == hA && underNaForeground,
					$"返回=0x{switchedUnderNa.ToInt64():X} 前台=0x{GetForegroundWindow().ToInt64():X} 期望=0x{hA.ToInt64():X}");
			}

			// [8] 别的进程的普通置顶窗口：它就是用户手指底下的那个界面，该切给它。
			children.Add(SpawnChild("TFW_TOP", new Rectangle(170, 130, 200, 150), noActivate: false, topmost: true));
			nint hTop = WaitForWindow("TFW_TOP", 20000);
			if (hTop == IntPtr.Zero)
			{
				Check("普通置顶窗口：已出现", false, "未找到 TFW_TOP");
			}
			else
			{
				ActivateWindow(hB);
				CheckOverlayIsTopHit("普通置顶窗口：探针的置顶窗口确实压在最上面（前置条件）", hTop, PointX, PointY);
				CheckResolve("普通置顶窗口：解析目标就是压在最上面的它",
					WheelFocusSwitcher.TestResolveTargetWindow(PointX, PointY), hTop, PointX, PointY);
				nint switchedToTop = WheelFocusSwitcher.SwitchFocusToPoint(new System.Windows.Point(PointX, PointY));
				bool topForeground = WaitForForeground(hTop);
				Check("普通置顶窗口：前台切到它",
					switchedToTop == hTop && topForeground,
					$"返回=0x{switchedToTop.ToInt64():X} 前台=0x{GetForegroundWindow().ToInt64():X} 期望=0x{hTop.ToInt64():X}");
			}

			// [10] 方案归属：轮盘方案是按进程名取的，而"这次轮盘属于哪个程序"必须看呼出点所在的窗口，
			//      不是前台窗口 —— 否则跨屏呼出时第一次拿到的是另一块屏幕上那个程序的方案（用户得呼出第二次）。
			RunProfileOwnershipCheck(children);

			// [9] Shell 表面（任务栏 / 桌面）：永远不作为目标，也绝不让前台从用户软件上跑掉。
			//     这里同时守住「命中 Shell 表面时不许沿 z 序往下钻」：任务栏正下方压着的那一层
			//     （Win11 的 MSTaskListWClass 等任务栏子窗口，以及延伸到任务栏底下的最大化程序）
			//     矩形同样盖住该点，一旦往下钻就会把用户当前软件抢走。
			nint taskbar = FindWindow("Shell_TrayWnd", null);
			if (taskbar != IntPtr.Zero && GetWindowRect(taskbar, out RECT tb) && tb.Right > tb.Left && tb.Bottom > tb.Top)
			{
				int tx = (tb.Left + tb.Right) / 2;
				int ty = (tb.Top + tb.Bottom) / 2;
				nint resolvedOnTaskbar = WheelFocusSwitcher.TestResolveTargetWindow(tx, ty);
				Console.WriteLine($"      任务栏空白处 ({tx},{ty})：{DescribePoint(tx, ty)}");
				Check("Shell 表面：任务栏/桌面不会被当作焦点目标",
					NeverShellSurface(resolvedOnTaskbar, taskbar),
					$"任务栏=0x{taskbar.ToInt64():X} 解析结果=0x{resolvedOnTaskbar.ToInt64():X}"
						+ (NeverShellSurface(resolvedOnTaskbar, taskbar) ? string.Empty : Environment.NewLine + "      " + DescribePoint(tx, ty)));
				// 任务栏是「命中层本身就不是目标」的一种兜底场景：解析结果必须为空，
				// 不能退而求其次切给压在任务栏底下的那个窗口（旧实现会这么做）。
				Check("Shell 表面：任务栏上不做 z 序兜底，宁可不动前台",
					resolvedOnTaskbar == IntPtr.Zero,
					$"解析结果=0x{resolvedOnTaskbar.ToInt64():X}（期望 0x0）"
						+ (resolvedOnTaskbar == IntPtr.Zero ? string.Empty : Environment.NewLine + "      " + DescribePoint(tx, ty)));
			}
			else
			{
				Console.WriteLine("SKIP  Shell 表面：未找到任务栏窗口");
			}
		}
		catch (Exception ex)
		{
			Check("探针未抛异常", false, ex.ToString());
		}
		finally
		{
			Finish(originalForeground, children, selfWindows);
		}

		Console.WriteLine();
		Console.WriteLine(_failures == 0
			? $"全部通过（{_checks} 项断言）。"
			: $"失败 {_failures} / {_checks} 项。");
		return _failures == 0 ? 0 : 1;
	}

	/// <summary>
	/// 方案归属：拉起<b>另一个可执行文件</b>的窗口（记事本，失败则退回 cmd），把它压到指定呼出点上，
	/// 前台却停在探针自己的窗口上 —— 正是「光标在这块屏幕、前台在另一块屏幕」的缺陷现场。
	/// 断言 <see cref="WheelFocusSwitcher.TryGetProcessNameAtPoint"/> 给出的进程名取自呼出点，
	/// 而不是前台窗口（旧实现会让轮盘用错方案，用户得呼出第二次）。
	/// </summary>
	private static void RunProfileOwnershipCheck(List<Process> children)
	{
		const int OtherX = 270;
		const int OtherY = 600;

		Process? other = TrySpawnForeignApp(children);
		if (other == null)
		{
			Console.WriteLine("SKIP  方案归属：未能拉起另一个可执行文件的窗口");
			return;
		}
		nint hOther = WaitForProcessMainWindow(other, 20000);
		if (hOther == IntPtr.Zero)
		{
			Console.WriteLine($"SKIP  方案归属：{other.ProcessName}.exe 的窗口未出现");
			return;
		}

		SetWindowPos(hOther, IntPtr.Zero, 60, 450, 420, 300, SWP_NOZORDER | SWP_NOACTIVATE);
		Pump();
		// 先把这个窗口抬到最上面（它才该是呼出点上的那个窗口），再把前台交给探针自己的另一个窗口。
		ActivateWindow(hOther);
		nint probeWindow = WaitForWindow("TFW_B", 5000);
		if (probeWindow != IntPtr.Zero)
		{
			ActivateWindow(probeWindow);
		}
		Pump();

		nint hit = WindowFromPoint(new POINT { X = OtherX, Y = OtherY });
		nint hitRoot = GetAncestor(hit, 2u);
		Check("方案归属：另一个进程的窗口确实压在呼出点上（前置条件）",
			hitRoot == hOther,
			$"窗口=0x{hOther.ToInt64():X} WindowFromPoint=0x{hit.ToInt64():X} GA_ROOT=0x{hitRoot.ToInt64():X}"
				+ (hitRoot == hOther ? string.Empty : Environment.NewLine + "      " + DescribePoint(OtherX, OtherY)));

		GetWindowThreadProcessId(hOther, out uint otherPid);
		string expectedProc = TryDescribeProcess(otherPid);
		string foregroundProc = ActiveWindowHelper.GetActiveWindowProcessName();
		string? pointProc = WheelFocusSwitcher.TryGetProcessNameAtPoint(new System.Windows.Point(OtherX, OtherY));

		bool matchesPoint = expectedProc.Length == 0
			? !string.IsNullOrEmpty(pointProc)
			: string.Equals(pointProc, expectedProc, StringComparison.OrdinalIgnoreCase);
		bool differsFromForeground = !string.Equals(pointProc, foregroundProc, StringComparison.OrdinalIgnoreCase);

		Check("方案归属：进程名取的是呼出点所在的窗口，而不是前台窗口",
			matchesPoint && differsFromForeground,
			$"呼出点进程={pointProc ?? "(空)"} 期望={expectedProc} 前台进程={foregroundProc}"
				+ (matchesPoint && differsFromForeground ? string.Empty : Environment.NewLine + "      " + DescribePoint(OtherX, OtherY)));
	}

	/// <summary>拉起另一个可执行文件的窗口（记事本优先，失败退回 cmd）。返回 null 表示无法拉起。</summary>
	private static Process? TrySpawnForeignApp(List<Process> children)
	{
		Process? notepad = TryStart("notepad.exe", string.Empty);
		if (notepad != null)
		{
			children.Add(notepad);
			if (WaitForProcessMainWindow(notepad, 8000) != IntPtr.Zero)
			{
				return notepad;
			}
		}
		Process? cmd = TryStart("cmd.exe", "/k title TFW_CMD");
		if (cmd != null)
		{
			children.Add(cmd);
			if (WaitForProcessMainWindow(cmd, 8000) != IntPtr.Zero)
			{
				return cmd;
			}
		}
		return null;
	}

	private static Process? TryStart(string fileName, string arguments)
	{
		try
		{
			ProcessStartInfo psi = new ProcessStartInfo(fileName, arguments)
			{
				UseShellExecute = true,
				WindowStyle = ProcessWindowStyle.Normal
			};
			return Process.Start(psi);
		}
		catch
		{
			return null;
		}
	}

	private static nint WaitForProcessMainWindow(Process process, int timeoutMs)
	{
		long deadline = Environment.TickCount64 + timeoutMs;
		while (Environment.TickCount64 < deadline)
		{
			try
			{
				process.Refresh();
				if (process.HasExited)
				{
					return IntPtr.Zero;
				}
				if (process.MainWindowHandle != IntPtr.Zero)
				{
					return process.MainWindowHandle;
				}
			}
			catch
			{
				return IntPtr.Zero;
			}
			Thread.Sleep(120);
		}
		return IntPtr.Zero;
	}

	/// <summary>用 BCL（与宿主实现相互独立的一条路径）拿窗口所在进程的 exe 名，作为期望值。</summary>
	private static string TryDescribeProcess(uint pid)
	{
		try
		{
			return (Process.GetProcessById((int)pid).ProcessName + ".exe").ToLowerInvariant();
		}
		catch
		{
			return string.Empty;
		}
	}

	private static int Finish(nint originalForeground, List<Process> children, List<Form> selfWindows)
	{
		foreach (Form form in selfWindows)
		{
			try { form.Close(); } catch { }
		}
		foreach (Process child in children)
		{
			try
			{
				if (!child.HasExited)
				{
					child.Kill(entireProcessTree: true);
				}
				child.Dispose();
			}
			catch { }
		}
		Pump();
		if (originalForeground != IntPtr.Zero && IsWindow(originalForeground))
		{
			WindowTaskbarHelper.ActivateWindow(originalForeground);
		}
		return _failures == 0 ? 0 : 1;
	}

	private static void Check(string name, bool ok, string detail)
	{
		_checks++;
		if (!ok)
		{
			_failures++;
		}
		Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}");
		if (!string.IsNullOrEmpty(detail))
		{
			Console.WriteLine($"      {detail}");
		}
	}

	/// <summary>解析类断言：失败时把「这个点上系统真实命中了谁」一并打印出来，避免只看到一个句柄对不上。</summary>
	private static void CheckResolve(string name, nint actual, nint expected, int x, int y)
	{
		if (actual == expected)
		{
			Check(name, true, Describe(actual, expected) + (Trace ? Environment.NewLine + "      " + DescribePoint(x, y) : string.Empty));
			return;
		}
		Check(name, false, Describe(actual, expected) + Environment.NewLine + "      " + DescribePoint(x, y));
	}

	/// <summary>
	/// 前置条件断言：探针自己摆的那层窗口必须真的是该点上最上面的窗口（WindowFromPoint 命中它）。
	/// 没有这条，窗口没摆上去时用例会「因为下面那层被解析出来」而假绿 —— 这条断言存在的全部理由。
	/// </summary>
	private static void CheckOverlayIsTopHit(string name, nint overlay, int x, int y)
	{
		POINT pt = new POINT { X = x, Y = y };
		nint hit = WindowFromPoint(pt);
		// 命中点可能是目标窗口里的子控件（记事本的 Edit、WinForms 的客户区），
		// 所以和产品代码一样按 GA_ROOT 比对顶层窗口。
		nint hitRoot = GetAncestor(hit, 2u);
		long ex = overlay != IntPtr.Zero ? GetWindowLongPtr(overlay, GWL_EXSTYLE).ToInt64() : 0L;
		bool ok = overlay != IntPtr.Zero && hitRoot == overlay;
		Check(name, ok,
			$"窗口=0x{overlay.ToInt64():X} WindowFromPoint=0x{hit.ToInt64():X} GA_ROOT=0x{hitRoot.ToInt64():X} ex=0x{ex:X} Visible={(overlay != IntPtr.Zero && IsWindowVisible(overlay) ? "是" : "否")}"
				+ (ok && !Trace ? string.Empty : Environment.NewLine + "      " + DescribePoint(x, y)));
	}

	private static string Describe(nint actual, nint expected)
	{
		return $"解析结果=0x{actual.ToInt64():X} 期望=0x{expected.ToInt64():X}";
	}

	private static bool NeverShellSurface(nint resolved, nint taskbar)
	{
		return resolved != taskbar && resolved != GetShellWindow() && resolved != GetDesktopWindow();
	}

	/// <summary>
	/// 失败时自解释：把该点上系统的真实命中结果与 z 序候选逐条打印出来
	/// （句柄 / 类名 / 进程 / 可见性 / 扩展样式 / 矩形 / 是否覆盖该点）。
	/// </summary>
	private static string DescribePoint(int x, int y)
	{
		POINT pt = new POINT { X = x, Y = y };
		StringBuilder sb = new StringBuilder();
		nint hit = WindowFromPoint(pt);
		sb.Append($"命中点({x},{y})：WindowFromPoint=0x{hit.ToInt64():X}{DescribeWindow(hit, pt)}");
		nint root = GetAncestor(hit, 2u);
		sb.Append($"  GA_ROOT=0x{root.ToInt64():X}{DescribeWindow(root, pt)}");
		sb.Append(" | z序含该点的顶层窗口：");
		int seen = 0;
		for (nint h = GetTopWindow(IntPtr.Zero); h != IntPtr.Zero && seen < 12; h = GetWindow(h, 2u))
		{
			if (GetWindowRect(h, out RECT rect) && x >= rect.Left && x < rect.Right && y >= rect.Top && y < rect.Bottom)
			{
				seen++;
				sb.Append($"[{seen}]0x{h.ToInt64():X}{DescribeWindow(h, pt)} ");
			}
		}
		return sb.ToString();
	}

	private static string DescribeWindow(nint hWnd, POINT pt)
	{
		if (hWnd == IntPtr.Zero)
		{
			return "(空)";
		}
		StringBuilder cls = new StringBuilder(128);
		GetClassName(hWnd, cls, cls.Capacity);
		GetWindowThreadProcessId(hWnd, out uint pid);
		long ex = GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64();
		bool covers = GetWindowRect(hWnd, out RECT rect) && pt.X >= rect.Left && pt.X < rect.Right && pt.Y >= rect.Top && pt.Y < rect.Bottom;
		return $"(类={cls} pid={pid} 可见={(IsWindowVisible(hWnd) ? "是" : "否")} ex=0x{ex:X} 覆盖={covers} 矩形={rect.Left},{rect.Top},{rect.Right - rect.Left}x{rect.Bottom - rect.Top})";
	}

	private static void ActivateWindow(nint hWnd)
	{
		WindowTaskbarHelper.ActivateWindow(hWnd);
		Pump();
		Thread.Sleep(120);
		Pump();
	}

	/// <summary>
	/// 断言前的确定性等待：<c>SetForegroundWindow</c> 的生效是异步的（前台锁、跨线程
	/// AttachThreadInput 都要走完），系统繁忙时 <c>GetForegroundWindow()</c> 会晚一拍才反映出来。
	/// 不等待就会得到「探针偶发 FAIL、重跑又全绿」这种假红 —— 断言判的是最终状态，不是瞬时状态。
	/// </summary>
	private static bool WaitForForeground(nint expected, int timeoutMs = 2000)
	{
		long deadline = Environment.TickCount64 + timeoutMs;
		while (Environment.TickCount64 < deadline)
		{
			if (GetForegroundWindow() == expected)
			{
				return true;
			}
			Thread.Sleep(50);
			Pump();
		}
		return GetForegroundWindow() == expected;
	}

	private static void Pump()
	{
		try
		{
			Application.DoEvents();
		}
		catch { }
	}

	/// <summary>
	/// 在探针自己进程里摆一个压在呼出点上的窗口（模拟常驻的轮盘 HWND）。
	/// 靠「激活到最前」而不是 TopMost 把它排到 A 前面：本进程没有消息循环，
	/// SetWindowPos(HWND_TOPMOST) 会返回 true 却不落到扩展样式上（见 ProbeForm.EnsureTopmost）。
	/// </summary>
	private static Form ShowSelfWindow(string title, Rectangle bounds, bool topmost)
	{
		ProbeForm form = new ProbeForm(noActivate: false)
		{
			Text = title,
			FormBorderStyle = FormBorderStyle.FixedSingle,
			StartPosition = FormStartPosition.Manual,
			ShowInTaskbar = true,
			TopMost = topmost,
			BackColor = Color.FromArgb(210, 255, 210)
		};
		form.Bounds = bounds;
		form.Show();
		Pump();
		WindowTaskbarHelper.ActivateWindow(form.Handle);
		form.EnsureTopmost();
		Pump();
		Thread.Sleep(200);
		Pump();
		return form;
	}

	private static Process SpawnChild(string title, Rectangle bounds, bool noActivate, bool topmost)
	{
		string exe = Environment.ProcessPath ?? throw new InvalidOperationException("ProcessPath is null.");
		string arguments = $"--child --title {title} --rect {bounds.Left},{bounds.Top},{bounds.Width},{bounds.Height}"
			+ (noActivate ? " --noactivate" : string.Empty)
			+ (topmost ? " --topmost" : string.Empty);
		ProcessStartInfo psi = new ProcessStartInfo(exe, arguments)
		{
			UseShellExecute = false,
			WorkingDirectory = AppContext.BaseDirectory
		};
		Process? process = Process.Start(psi);
		if (process == null)
		{
			throw new InvalidOperationException($"Failed to start child window '{title}'.");
		}
		return process;
	}

	private static nint WaitForWindow(string title, int timeoutMs)
	{
		long deadline = Environment.TickCount64 + timeoutMs;
		while (Environment.TickCount64 < deadline)
		{
			nint found = FindWindowByTitle(title);
			if (found != IntPtr.Zero)
			{
				return found;
			}
			Thread.Sleep(80);
		}
		return IntPtr.Zero;
	}

	private static nint FindWindowByTitle(string title)
	{
		nint result = IntPtr.Zero;
		EnumWindows(delegate (nint hWnd, nint _)
		{
			StringBuilder sb = new StringBuilder(256);
			if (GetWindowText(hWnd, sb, sb.Capacity) > 0 && string.Equals(sb.ToString(), title, StringComparison.Ordinal))
			{
				result = hWnd;
				return false;
			}
			return true;
		}, IntPtr.Zero);
		return result;
	}

	private static Rectangle ParseRect(string text)
	{
		string[] parts = text.Split(',');
		return new Rectangle(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]), int.Parse(parts[3]));
	}

	private static string? ArgValue(string[] args, string name)
	{
		for (int i = 0; i < args.Length - 1; i++)
		{
			if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
			{
				return args[i + 1];
			}
		}
		return null;
	}

	private static bool HasFlag(string[] args, string name)
	{
		foreach (string arg in args)
		{
			if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}
}
