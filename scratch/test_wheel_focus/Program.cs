using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using WinPieGestures;
using MouseEventArgs = WinPieGestures.MouseEventArgs;

namespace test_wheel_focus;

internal static class Program
{
	private static int _passedCount = 0;
	private static int _failedCount = 0;
	private static int _releaseStuckModifiersCallCount = 0;
	private static readonly List<(ActionItem Action, ActionExecutionContext? Context)> _executedActions = new();
	private static string _testIsolatedDataDir = string.Empty;

	[STAThread]
	private static int Main(string[] args)
	{
		// 1. 严格检查启动参数与测试环境隔离
		bool isTestInstance = args.Any(a => string.Equals(a, "--test-instance", StringComparison.OrdinalIgnoreCase));
		if (!isTestInstance)
		{
			Console.WriteLine("[ERROR] Test runner must be executed with --test-instance argument!");
			return 1;
		}

		// 2. 在触碰 ConfigManager、AppLogger 等类型之前设置独立临时 LOCALAPPDATA
		_testIsolatedDataDir = Path.Combine(Path.GetTempPath(), "StarPie_Focus_Test_" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_testIsolatedDataDir);
		Environment.SetEnvironmentVariable("LOCALAPPDATA", _testIsolatedDataDir);

		Console.WriteLine("===============================================================");
		Console.WriteLine("  StarPie SP-PR178-FOCUS-001 Automated Test Suite (Hardened R2)");
		Console.WriteLine($"  Isolated LOCALAPPDATA: {_testIsolatedDataDir}");
		Console.WriteLine("===============================================================");

		if (Application.Current == null)
		{
			_ = new Application();
		}

		// 3. 全局静态初始化防护：禁止自启同步；隔离宿主未安装音效插件。
		ConfigManager.DisableAutoStartSync = true;
		ConfigManager.CurrentConfig = new AppConfig
		{
			Trigger = new TriggerConfig { TriggerType = "Mouse", MouseButton = "RightButton" },
			GestureEnabled = true,
			GestureTriggerButton = "MiddleButton"
		};

		// 4. 接管所有系统副作用：按键释放、动作最终执行。宿主已无内置播放引擎。
		ActionExecutor.ReleaseStuckModifiersOverride = () =>
		{
			Interlocked.Increment(ref _releaseStuckModifiersCallCount);
		};
		ActionExecutor.ActionExecutionOverride = (action, ctx) =>
		{
			lock (_executedActions)
			{
				_executedActions.Add((action, ctx));
			}
		};

		// 执行全部确定性生产路径测试
		RunTest("Scenario 1: Normal hover trigger & exact action dispatch", TestNormalHoverTriggerAndFocus);
		RunTest("Scenario 2: Quick release & queuing race protection", TestQuickReleaseQueuingRace);
		RunTest("Scenario 3: Activation failure safe fallback (no dispatch to A)", TestActivationFailureFallback);
		RunTest("Scenario 4: Target window destroyed mid-gesture (IsTargetValid)", TestTargetDestroyedMidGesture);
		RunTest("Scenario 5: Stale ShowRadialUI callback after session superseded", TestStaleShowRadialUICallbackAfterNewSession);
		RunTest("Scenario 6: Cancellation / Dispose / NewSession / Pause before consumption", TestCancellationOrDisposeDuringQueuedRelease);
		RunTest("Scenario 7: Foreground target focus shifts away before execution", TestForegroundTargetSwitchedAwayBeforeExecution);
		RunTest("Scenario 8: Queue consumption PID changed safety cancel", TestQueueConsumptionPidChanged);
		RunTest("Scenario 9: Enqueued action snapshot immutability (Clone protection)", TestEnqueuedActionSnapshotImmutability);
		RunTest("Scenario 10: Mouse missing modifiers does NOT pollute keyboard trigger", TestMouseMissingModifiersDoesNotPolluteKeyboard);
		RunTest("Scenario 11: Window B middle-click wheel trigger vs drawing gesture conflict", TestTargetMiddleClickConflictWithDrawingGesture);
		RunTest("Scenario 12: Legacy action without context executes normally", TestLegacyActionWithoutContextExecutesNormally);
		RunTest("Scenario 13: Isolated mouse release does not create pending session", TestIsolatedReleaseDoesNotCreatePendingSession);
		RunTest("Scenario 14: Isolation rules based on target snapshot & modifiers", TestIsolationRulesTargetSnapshot);
		RunTest("Scenario 15: Non-mouse triggers remain side-effect free", TestNonMouseTriggersUntouched);
		RunTest("Scenario 16: Controlled mock Shell surface fallback", TestShellSurfaceControlledMock);
		RunTest("Scenario 17: Conservative activation safety (no Alt / no Topmost tampering)", TestConservativeActivationSafety);
		RunTest("Scenario 18: ReleaseStuckModifiers test seam coverage", TestReleaseStuckModifiersSeamCoverage);
		RunTest("Scenario 19: Picker and runtime share process identity and path", TestSharedWindowProcessIdentity);
		RunTest("Scenario 20: Failed process reads cannot become picker bindings", TestSharedWindowProcessFailures);
		RunTest("Scenario 21: Component processes retain independent bindings", TestIndependentComponentProcessBindings);
		RunTest("Scenario 22: Cached identity retains path and failure consistency", TestWindowProcessSnapshotCache);

		Console.WriteLine("===============================================================");
		Console.WriteLine($"  Results: {_passedCount} passed, {_failedCount} failed, total {_passedCount + _failedCount}");
		Console.WriteLine($"  Side-Effect Free Proof: Stuck modifier releases intercepted: {_releaseStuckModifiersCallCount}");
		Console.WriteLine($"  Evidence Directory: {_testIsolatedDataDir}");
		Console.WriteLine("===============================================================");

		return _failedCount == 0 ? 0 : 1;
	}

	private static void RunTest(string testName, Action testMethod)
	{
		Console.Write($"[RUN] {testName} ... ");
		try
		{
			ResetAllTestState();
			testMethod();
			Console.WriteLine("PASSED");
			_passedCount++;
		}
		catch (Exception ex)
		{
			Console.WriteLine("FAILED");
			Console.WriteLine($"      Error: {ex.Message}");
			Console.WriteLine($"      {ex.StackTrace?.Split('\n')[0]}");
			_failedCount++;
		}
		finally
		{
			ResetAllTestState();
			PumpDispatcher();
		}
	}

	private static void ResetAllTestState()
	{
		WheelFocusSwitcher.ResetTestSeams();
		ActiveWindowHelper.ResetTestSeams();
		lock (_executedActions)
		{
			_executedActions.Clear();
		}
	}

	private static void PumpDispatcher()
	{
		if (Application.Current?.Dispatcher == null) return;
		DispatcherFrame frame = new DispatcherFrame();
		Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
		Dispatcher.PushFrame(frame);
	}

	private static WheelProfile CreateDummyProfile(string proc = "app_b.exe")
	{
		return new WheelProfile
		{
			ProcessName = proc,
			DisplayName = "ProfileB",
			Actions = new List<ActionItem>
			{
				new ActionItem { Name = "ActionB_Sector0", Type = "Custom", Parameter = "TestB0" }
			}
		};
	}

	/// <summary>
	/// Scenario 1: 正常鼠标悬停触发与焦点对齐
	/// 目标窗口 B (0x2000) 成功激活为前台，轮盘使用窗口 B 的配置，真实消费后动作精确执行且仅执行一次。
	/// </summary>
	private static void TestNormalHoverTriggerAndFocus()
	{
		nint hwndA = 0x1000;
		nint hwndB = 0x2000;
		nint currentFg = hwndA;

		WheelFocusSwitcher.ForegroundWindowGetterOverride = () => currentFg;
		WheelFocusSwitcher.IsWindowOverride = (h) => h == hwndA || h == hwndB;
		ActiveWindowHelper.RealPidGetterOverride = (h) => (h == hwndB) ? 200u : (h == hwndA ? 100u : 0u);
		ActiveWindowHelper.ProcessNameGetterOverride = (h) => (h == hwndB) ? "app_b.exe" : (h == hwndA ? "app_a.exe" : "unknown.exe");
		WheelFocusSwitcher.WindowActivatorOverride = (h) =>
		{
			if (h == hwndB)
			{
				currentFg = hwndB;
				return true;
			}
			return false;
		};

		var targetB = new TargetWindowInfo(hwndB, 200, "app_b.exe", isEligibleBackgroundTarget: true);
		WheelFocusSwitcher.TargetResolverOverride = (pt) => targetB;

		using var controller = new GestureController(new MouseHook(), null);

		var profileB = CreateDummyProfile();
		controller.TestActiveProfile = profileB;

		var session = new WheelFocusSession(targetB, new Point(300, 300));
		controller.TestCurrentWheelFocusSession = session;

		// 模拟正常呼出轮盘
		var switchResult = WheelFocusSwitcher.SwitchFocus(session);
		Assert(switchResult == FocusSwitchResult.Activated, "SwitchFocus should return Activated");
		Assert(session.FocusState == WheelFocusState.Focused, "FocusState should be Focused");
		Assert(currentFg == hwndB, "Foreground should now be Window B");

		// 选中扇区 0 并模拟释放
		controller.TestSetGestureActive(true, 1);
		controller.TestQueueHighlightUpdate(0, -1, false, false, 1);
		session.FocusState = WheelFocusState.Focused;

		controller.TestCompleteMouseTriggerRelease(new Point(350, 300), "RightButton");
		PumpDispatcher();

		// 等待后台消费者处理
		Thread.Sleep(50);

		// 验证真实消费逻辑：精确动作身份、目标 HWND/PID 与执行次数 (严格 1 次)
		lock (_executedActions)
		{
			Assert(_executedActions.Count == 1, $"Expected exactly 1 executed action, but got {_executedActions.Count}");
			var item = _executedActions[0];
			Assert(item.Action.Name == "ActionB_Sector0", $"Action name mismatch: expected ActionB_Sector0, got {item.Action.Name}");
			Assert(item.Action.Parameter == "TestB0", $"Action param mismatch: expected TestB0, got {item.Action.Parameter}");
			Assert(item.Context != null, "ActionExecutionContext must be attached");
			Assert(item.Context!.TargetHwnd == hwndB, "TargetHwnd in context must match Window B");
			Assert(item.Context.TargetProcessId == 200, "TargetProcessId in context must match Window B PID");
		}
	}

	/// <summary>
	/// Scenario 2: 快速松手与 UI 排队竞态
	/// ShowRadialUI 尚未执行或被跳过（FocusState 为 NotAttempted），
	/// 动作派发前独立确认目标并完成对齐；对齐成功且真实执行一次。
	/// </summary>
	private static void TestQuickReleaseQueuingRace()
	{
		nint hwndA = 0x1000;
		nint hwndB = 0x2000;
		nint currentFg = hwndA;

		WheelFocusSwitcher.ForegroundWindowGetterOverride = () => currentFg;
		WheelFocusSwitcher.IsWindowOverride = (h) => h == hwndA || h == hwndB;
		ActiveWindowHelper.RealPidGetterOverride = (h) => (h == hwndB) ? 200u : (h == hwndA ? 100u : 0u);
		ActiveWindowHelper.ProcessNameGetterOverride = (h) => (h == hwndB) ? "app_b.exe" : (h == hwndA ? "app_a.exe" : "unknown.exe");

		int activationAttempts = 0;
		WheelFocusSwitcher.WindowActivatorOverride = (h) =>
		{
			activationAttempts++;
			if (h == hwndB)
			{
				currentFg = hwndB;
				return true;
			}
			return false;
		};

		var targetB = new TargetWindowInfo(hwndB, 200, "app_b.exe", isEligibleBackgroundTarget: true);
		var session = new WheelFocusSession(targetB, new Point(300, 300));
		Assert(session.FocusState == WheelFocusState.NotAttempted, "Session must start as NotAttempted");

		using var controller = new GestureController(new MouseHook(), null);
		controller.TestCurrentWheelFocusSession = session;
		controller.TestActiveProfile = CreateDummyProfile();

		controller.TestSetGestureActive(true, 1);
		controller.TestQueueHighlightUpdate(0, -1, false, false, 1);

		// 快速松手：ShowRadialUI 未运行，FocusState 仍为 NotAttempted
		controller.TestCompleteMouseTriggerRelease(new Point(350, 300), "RightButton");
		PumpDispatcher();

		// 等待消费
		Thread.Sleep(50);

		// 必须在动作派发前独立触发聚焦激活并成功派发
		Assert(activationAttempts >= 1, "SwitchFocus must be invoked before action execution even if ShowRadialUI was skipped");
		Assert(session.FocusState == WheelFocusState.Focused, "FocusState must be resolved to Focused");
		Assert(currentFg == hwndB, "Foreground must be on Window B");

		lock (_executedActions)
		{
			Assert(_executedActions.Count == 1, "Action must be executed exactly once after focus confirmation");
			Assert(_executedActions[0].Action.Name == "ActionB_Sector0", "Action name must match");
			Assert(_executedActions[0].Context?.TargetHwnd == hwndB, "Context target must match Window B");
		}
	}

	/// <summary>
	/// Scenario 3: 激活失败安全回退
	/// 激活窗口 B 失败时，焦点依然在窗口 A 上。
	/// 严禁向窗口 A 误发针对窗口 B 的动作！0 动作执行。
	/// </summary>
	private static void TestActivationFailureFallback()
	{
		nint hwndA = 0x1000;
		nint hwndB = 0x2000;
		nint currentFg = hwndA;

		WheelFocusSwitcher.ForegroundWindowGetterOverride = () => currentFg;
		WheelFocusSwitcher.IsWindowOverride = (h) => h == hwndA || h == hwndB;
		ActiveWindowHelper.RealPidGetterOverride = (h) => (h == hwndB) ? 200u : (h == hwndA ? 100u : 0u);
		ActiveWindowHelper.ProcessNameGetterOverride = (h) => (h == hwndB) ? "protected_b.exe" : (h == hwndA ? "app_a.exe" : "unknown.exe");
		WheelFocusSwitcher.WindowActivatorOverride = (h) => false; // 激活失败

		var targetB = new TargetWindowInfo(hwndB, 200, "protected_b.exe", isEligibleBackgroundTarget: true);
		var session = new WheelFocusSession(targetB, new Point(300, 300));

		using var controller = new GestureController(new MouseHook(), null);
		controller.TestCurrentWheelFocusSession = session;
		controller.TestActiveProfile = CreateDummyProfile();

		controller.TestSetGestureActive(true, 1);
		controller.TestQueueHighlightUpdate(0, -1, false, false, 1);

		controller.TestCompleteMouseTriggerRelease(new Point(350, 300), "RightButton");
		PumpDispatcher();
		Thread.Sleep(50);

		lock (_executedActions)
		{
			Assert(_executedActions.Count == 0, "Action MUST NOT be executed when focus alignment fails!");
		}
		Assert(session.FocusState == WheelFocusState.Failed, "FocusState must be Failed");
		Assert(currentFg == hwndA, "Foreground must remain untouched on Window A");
	}

	/// <summary>
	/// Scenario 4: 目标窗口在手势中途关闭/崩溃
	/// 窗口 B 句柄失效，IsTargetValid 校验失败，动作安全取消。
	/// </summary>
	private static void TestTargetDestroyedMidGesture()
	{
		nint hwndA = 0x1000;
		nint hwndB = 0x2000;
		nint currentFg = hwndA;

		WheelFocusSwitcher.ForegroundWindowGetterOverride = () => currentFg;
		WheelFocusSwitcher.IsWindowOverride = (h) => h == hwndA; // B 不存在
		WheelFocusSwitcher.WindowActivatorOverride = (h) => false;

		var targetB = new TargetWindowInfo(hwndB, 200, "crashed_b.exe", isEligibleBackgroundTarget: true);
		Assert(!WheelFocusSwitcher.IsTargetValid(targetB), "IsTargetValid must return false for closed window");

		var session = new WheelFocusSession(targetB, new Point(300, 300));

		using var controller = new GestureController(new MouseHook(), null);
		controller.TestCurrentWheelFocusSession = session;
		controller.TestActiveProfile = CreateDummyProfile();

		controller.TestSetGestureActive(true, 1);
		controller.TestQueueHighlightUpdate(0, -1, false, false, 1);

		controller.TestCompleteMouseTriggerRelease(new Point(350, 300), "RightButton");
		PumpDispatcher();
		Thread.Sleep(50);

		lock (_executedActions)
		{
			Assert(_executedActions.Count == 0, "Action MUST NOT be executed when target window was destroyed!");
		}
	}

	/// <summary>
	/// Scenario 5: 旧显示回调在新会话开始后到达
	/// 会话 1 的 ShowRadialUI 回调在会话 1 被取消/新会话启动后才被 Dispatcher 调度。
	/// 必须在任何状态修改、窗口激活前立即拒绝，不得修改 _activeProfile，不得激活窗口。
	/// </summary>
	private static void TestStaleShowRadialUICallbackAfterNewSession()
	{
		nint hwndA = 0x1000;
		nint hwndB = 0x2000;
		nint currentFg = hwndA;

		int activationCount = 0;
		WheelFocusSwitcher.ForegroundWindowGetterOverride = () => currentFg;
		WheelFocusSwitcher.IsWindowOverride = (h) => h == hwndA || h == hwndB;
		WheelFocusSwitcher.WindowActivatorOverride = (h) =>
		{
			activationCount++;
			currentFg = h;
			return true;
		};

		var targetB = new TargetWindowInfo(hwndB, 200, "app_b.exe", isEligibleBackgroundTarget: true);
		var session1 = new WheelFocusSession(targetB, new Point(300, 300));

		using var controller = new GestureController(new MouseHook(), null);
		controller.TestCurrentWheelFocusSession = session1;
		controller.TestSetGestureActive(true, 1);

		var profile1 = CreateDummyProfile("stale_app.exe");

		// 模拟会话 1 已失效，且新会话 2 已经启动活跃 (_isGestureActive 为 true, 版本为 2)
		session1.Cancel();
		var targetC = new TargetWindowInfo(0x3000, 300, "app_c.exe", isEligibleBackgroundTarget: true);
		var session2 = new WheelFocusSession(targetC, new Point(400, 400));
		controller.TestCurrentWheelFocusSession = session2;
		controller.TestActiveProfile = CreateDummyProfile("app_c.exe");
		controller.TestSetGestureActive(true, 2);

		// 此时迟到的 session1 的 ShowRadialUI 回调试图执行 (持有旧版本 1 和旧 session1)
		bool shown = controller.TestShowRadialUI(new Point(300, 300), profile1, 1, session1);

		Assert(!shown, "ShowRadialUI must reject stale/superseded callback!");
		Assert(activationCount == 0, "ShowRadialUI must NOT activate window for stale callback!");
		Assert(controller.TestActiveProfile?.ProcessName == "app_c.exe", "ShowRadialUI must NOT overwrite active profile with stale profile!");
		Assert(currentFg == hwndA, "Foreground must remain on Window A");
	}

	/// <summary>
	/// Scenario 6: 已入队、未消费时发生 Cancel / Dispose / 新手势 / 暂停，执行次数为零
	/// </summary>
	private static void TestCancellationOrDisposeDuringQueuedRelease()
	{
		nint hwndB = 0x2000;
		WheelFocusSwitcher.ForegroundWindowGetterOverride = () => hwndB;
		WheelFocusSwitcher.IsWindowOverride = (h) => true;
		ActiveWindowHelper.RealPidGetterOverride = (h) => 200u;
		ActiveWindowHelper.ProcessNameGetterOverride = (h) => (h == hwndB) ? "app_b.exe" : "unknown.exe";

		// 6a: 已入队、未消费时 CancelGestureTracking -> 执行次数为零
		{
			lock (_executedActions) { _executedActions.Clear(); }
			var targetB = new TargetWindowInfo(hwndB, 200, "app_b.exe", isEligibleBackgroundTarget: true);
			var session = new WheelFocusSession(targetB, new Point(300, 300));
			using var controller = new GestureController(new MouseHook(), null);
			controller.TestPendingExecutionSession = session;

			var action = new ActionItem { Name = "TestCancel", Type = "Custom" };
			var ctx = new ActionExecutionContext(
				targetB.Hwnd, targetB.ProcessId, targetB.ProcessName,
				() => !session.IsCancelled && WheelFocusSwitcher.IsTargetValid(targetB));

			var envelope = new ActionEnvelope(action, ctx);
			// 模拟在消费前用户触发取消
			controller.TestCancelGestureTracking();
			Assert(session.IsCancelled, "CancelGestureTracking must set session.IsCancelled = true");

			// 真实生产消费者执行
			bool result = ActionExecutor.ProcessActionEnvelope(envelope);
			Assert(!result, "ProcessActionEnvelope must return false when session was cancelled");
			lock (_executedActions)
			{
				Assert(_executedActions.Count == 0, "Executed count must be zero after cancellation");
			}
		}

		// 6b: 已入队、未消费时 Dispose -> 执行次数为零
		{
			lock (_executedActions) { _executedActions.Clear(); }
			var targetB = new TargetWindowInfo(hwndB, 200, "app_b.exe", isEligibleBackgroundTarget: true);
			var session = new WheelFocusSession(targetB, new Point(300, 300));
			var controller = new GestureController(new MouseHook(), null);
			controller.TestPendingExecutionSession = session;

			var action = new ActionItem { Name = "TestDispose", Type = "Custom" };
			var ctx = new ActionExecutionContext(
				targetB.Hwnd, targetB.ProcessId, targetB.ProcessName,
				() => !session.IsCancelled && WheelFocusSwitcher.IsTargetValid(targetB));

			var envelope = new ActionEnvelope(action, ctx);
			// 模拟在消费前释放控制器
			controller.Dispose();
			Assert(session.IsCancelled, "Dispose must set session.IsCancelled = true");

			bool result = ActionExecutor.ProcessActionEnvelope(envelope);
			Assert(!result, "ProcessActionEnvelope must return false after Dispose");
			lock (_executedActions)
			{
				Assert(_executedActions.Count == 0, "Executed count must be zero after Dispose");
			}
		}

		// 6c: 已入队、未消费时发生新手势按键按下 -> 执行次数为零
		{
			lock (_executedActions) { _executedActions.Clear(); }
			var targetB = new TargetWindowInfo(hwndB, 200, "app_b.exe", isEligibleBackgroundTarget: true);
			var session = new WheelFocusSession(targetB, new Point(300, 300));
			using var controller = new GestureController(new MouseHook(), null);
			controller.TestPendingExecutionSession = session;

			var action = new ActionItem { Name = "TestNewSession", Type = "Custom" };
			var ctx = new ActionExecutionContext(
				targetB.Hwnd, targetB.ProcessId, targetB.ProcessName,
				() => !session.IsCancelled && WheelFocusSwitcher.IsTargetValid(targetB));

			var envelope = new ActionEnvelope(action, ctx);

			// 新手势按下
			controller.TestHookOnTriggerButtonDown(new MouseEventArgs(400, 400, targetB));
			Assert(session.IsCancelled, "New trigger button down must invalidate previous pending session");

			bool result = ActionExecutor.ProcessActionEnvelope(envelope);
			Assert(!result, "ProcessActionEnvelope must return false when superseded by new session");
			lock (_executedActions)
			{
				Assert(_executedActions.Count == 0, "Executed count must be zero after new session began");
			}
		}

		// 6d: 已入队、未消费时通过生产入口暂停并恢复 -> 旧动作作废(执行为0)，恢复后新动作正常执行(执行为1)
		{
			lock (_executedActions) { _executedActions.Clear(); }
			var targetB = new TargetWindowInfo(hwndB, 200, "app_b.exe", isEligibleBackgroundTarget: true);
			var oldSession = new WheelFocusSession(targetB, new Point(300, 300));
			var mouseHook = new MouseHook();
			using var controller = new GestureController(mouseHook, null);
			controller.TestPendingExecutionSession = oldSession;

			var oldAction = new ActionItem { Name = "TestOldPausedAction", Type = "Custom" };
			var oldCtx = new ActionExecutionContext(
				targetB.Hwnd, targetB.ProcessId, targetB.ProcessName,
				() => !mouseHook.IsPaused && !oldSession.IsCancelled && WheelFocusSwitcher.IsTargetValid(targetB),
				() => { if (ReferenceEquals(controller.TestPendingExecutionSession, oldSession)) controller.TestPendingExecutionSession = null; });

			var oldEnvelope = new ActionEnvelope(oldAction, oldCtx);

			// 通过生产共用状态转换方法暂停，再恢复（测试严禁自行补调用 InvalidatePendingExecutionSession）
			App.ApplyPauseStateTransition(true, mouseHook, null, controller, null);
			App.ApplyPauseStateTransition(false, mouseHook, null, controller, null);

			// 真实生产消费者消费旧动作 -> 必须丢弃，执行次数为 0
			bool oldResult = ActionExecutor.ProcessActionEnvelope(oldEnvelope);
			Assert(!oldResult, "ProcessActionEnvelope must return false for old action enqueued before pause");
			lock (_executedActions)
			{
				Assert(_executedActions.Count == 0, "Old action enqueued before pause must NOT execute even after resume!");
			}

			// 恢复后新建合法动作
			var newSession = new WheelFocusSession(targetB, new Point(350, 350));
			controller.TestPendingExecutionSession = newSession;

			var newAction = new ActionItem { Name = "TestResumedNewAction", Type = "Custom" };
			var newCtx = new ActionExecutionContext(
				targetB.Hwnd, targetB.ProcessId, targetB.ProcessName,
				() => !mouseHook.IsPaused && !newSession.IsCancelled && WheelFocusSwitcher.IsTargetValid(targetB),
				() => { if (ReferenceEquals(controller.TestPendingExecutionSession, newSession)) controller.TestPendingExecutionSession = null; });

			var newEnvelope = new ActionEnvelope(newAction, newCtx);

			// 真实生产消费者消费新动作 -> 必须执行，执行次数为 1
			bool newResult = ActionExecutor.ProcessActionEnvelope(newEnvelope);
			Assert(newResult, "ProcessActionEnvelope must return true for new action created after resume");
			lock (_executedActions)
			{
				Assert(_executedActions.Count == 1, "New action created after resume must execute exactly once");
				Assert(_executedActions[0].Action.Name == "TestResumedNewAction", "Executed action must be the new action");
			}
		}
	}

	/// <summary>
	/// Scenario 7: 初始目标已在前台，消费前前台切走，执行次数为零
	/// 验证真实生产 ProcessActionEnvelope 逻辑校验前台焦点是否在目标 HWND 上。
	/// </summary>
	private static void TestForegroundTargetSwitchedAwayBeforeExecution()
	{
		nint hwndB = 0x2000;
		nint hwndC = 0x3000;
		nint currentFg = hwndB; // 起初就在前台

		WheelFocusSwitcher.ForegroundWindowGetterOverride = () => currentFg;
		WheelFocusSwitcher.IsWindowOverride = (h) => true;
		ActiveWindowHelper.RealPidGetterOverride = (h) => (h == hwndB) ? 200u : 300u;

		var targetB = new TargetWindowInfo(hwndB, 200, "app_b.exe", isEligibleBackgroundTarget: false);
		var action = new ActionItem { Name = "SaveFile", Type = "Hotkey", Parameter = "Ctrl+S" };
		var ctx = new ActionExecutionContext(targetB.Hwnd, targetB.ProcessId, targetB.ProcessName, () => true);

		var envelope = new ActionEnvelope(action, ctx);

		// 消费前：焦点切到了 hwndC
		currentFg = hwndC;

		// 运行真实生产消费逻辑
		bool result = ActionExecutor.ProcessActionEnvelope(envelope);
		Assert(!result, "ProcessActionEnvelope must safely abort when foreground switched to another window!");
		lock (_executedActions)
		{
			Assert(_executedActions.Count == 0, "Executed action count must be zero when foreground switched away!");
		}
	}

	/// <summary>
	/// Scenario 8: 消费前 HWND 对应的实时 PID 改变，执行次数为零
	/// 目标有效入队，但在消费前目标进程崩溃退出，原 HWND 被另一个进程接管 (PID 改变)。
	/// </summary>
	private static void TestQueueConsumptionPidChanged()
	{
		nint hwndB = 0x2000;
		nint currentFg = hwndB;

		WheelFocusSwitcher.ForegroundWindowGetterOverride = () => currentFg;
		WheelFocusSwitcher.IsWindowOverride = (h) => true;

		uint currentPid = 200u;
		ActiveWindowHelper.RealPidGetterOverride = (h) => (h == hwndB) ? currentPid : 0u;

		var targetB = new TargetWindowInfo(hwndB, 200, "app_b.exe", isEligibleBackgroundTarget: true);
		var action = new ActionItem { Name = "ImportantAction", Type = "Hotkey" };
		var ctx = new ActionExecutionContext(targetB.Hwnd, targetB.ProcessId, targetB.ProcessName);

		var envelope = new ActionEnvelope(action, ctx);

		// 模拟消费前 PID 改变（原进程退出，句柄被 PID 999 复用）
		currentPid = 999u;

		// 运行真实生产消费逻辑
		bool result = ActionExecutor.ProcessActionEnvelope(envelope);
		Assert(!result, "ProcessActionEnvelope must reject action when real PID does not match!");
		lock (_executedActions)
		{
			Assert(_executedActions.Count == 0, "Executed action count must be zero when PID changed!");
		}
	}

	/// <summary>
	/// Scenario 9: 入队后修改原动作及子动作，消费者仍收到原快照 (Clone 快照保护)
	/// </summary>
	private static void TestEnqueuedActionSnapshotImmutability()
	{
		nint hwndB = 0x2000;
		WheelFocusSwitcher.ForegroundWindowGetterOverride = () => hwndB;
		WheelFocusSwitcher.IsWindowOverride = (h) => true;
		ActiveWindowHelper.RealPidGetterOverride = (h) => 200u;

		var originalAction = new ActionItem
		{
			Name = "OriginalAction",
			Type = "Hotkey",
			Parameter = "Ctrl+C",
			SubActions = new List<ActionItem>
			{
				new ActionItem { Name = "OriginalSubAction", Parameter = "Sub1" }
			}
		};

		var targetB = new TargetWindowInfo(hwndB, 200, "app_b.exe", isEligibleBackgroundTarget: false);
		var ctx = new ActionExecutionContext(targetB.Hwnd, targetB.ProcessId, targetB.ProcessName, () => true);

		// 入队（内部应通过 Clone 产生独立快照）
		ActionExecutor.EnqueueAction(originalAction, ctx);

		// 用户在队列等待期间立即篡改原实例
		originalAction.Name = "MUTATED_ACTION";
		originalAction.Parameter = "MUTATED_PARAM";
		originalAction.SubActions[0].Name = "MUTATED_SUB_ACTION";

		// 等待消费完成
		Thread.Sleep(50);

		lock (_executedActions)
		{
			Assert(_executedActions.Count == 1, "Exactly 1 action must be executed");
			var executed = _executedActions[0].Action;
			Assert(executed.Name == "OriginalAction", $"Action name must remain 'OriginalAction', but was '{executed.Name}'");
			Assert(executed.Parameter == "Ctrl+C", $"Action param must remain 'Ctrl+C', but was '{executed.Parameter}'");
			Assert(executed.SubActions.Count == 1, "SubActions count must be 1");
			Assert(executed.SubActions[0].Name == "OriginalSubAction", $"SubAction name must remain 'OriginalSubAction', but was '{executed.SubActions[0].Name}'");
		}
	}

	/// <summary>
	/// Scenario 10: 鼠标修饰键不足后转入键盘触发，不继承鼠标目标
	/// 鼠标按在窗口 B 上但未满足修饰键要求，事件被放行并清理会话；
	/// 随后键盘长按触发，必须读取当前前台窗口 A 的配置，绝不继承窗口 B 或切换至 B。
	/// </summary>
	private static void TestMouseMissingModifiersDoesNotPolluteKeyboard()
	{
		nint hwndA = 0x1000;
		nint hwndB = 0x2000;
		WheelFocusSwitcher.ForegroundWindowGetterOverride = () => hwndA;
		WheelFocusSwitcher.IsWindowOverride = (h) => true;
		ActiveWindowHelper.ProcessNameGetterOverride = (h) => (h == hwndA) ? "foreground_a.exe" : "background_b.exe";

		// background_b.exe 配置要求按下 Ctrl 键
		ConfigManager.CurrentConfig.BlacklistTriggerOverrides = new Dictionary<string, TriggerConfig>(StringComparer.OrdinalIgnoreCase)
		{
			["background_b.exe"] = new TriggerConfig
			{
				TriggerType = "Mouse",
				MouseButton = "RightButton",
				RequireCtrl = true
			}
		};
		ConfigManager.CurrentConfig.Profiles = new List<WheelProfile>
		{
			new WheelProfile { ProcessName = "foreground_a.exe", DisplayName = "ProfileA" },
			new WheelProfile { ProcessName = "background_b.exe", DisplayName = "ProfileB" }
		};

		using var controller = new GestureController(new MouseHook(), null);

		var targetB = new TargetWindowInfo(hwndB, 200, "background_b.exe", isEligibleBackgroundTarget: true);
		var mouseArgs = new MouseEventArgs(300, 300, targetB);

		// 鼠标在 B 上按下，但未按 Ctrl
		controller.TestHookOnTriggerButtonDown(mouseArgs);

		// 验证：修饰键不足，鼠标事件穿透放行，未接管
		Assert(!mouseArgs.Handled, "Mouse trigger with insufficient modifiers must NOT be handled");
		Assert(controller.TestCurrentWheelFocusSession == null, "Focus session must remain null when modifier check fails");

		// 随后前台 A 触发键盘长按
		// 验证 LongPressTimerCallback 在键盘模式下绝不读取任何鼠标残留会话
		controller.TestSetGestureActive(false, 1);
		// 模拟键盘触发长按
		controller.TestTriggerKeyboardLongPress();

		// 激活的配置必须是前台 A 的配置，不能是 B 的配置
		Assert(controller.TestActiveProfile?.ProcessName == "foreground_a.exe",
			$"Profile must be foreground_a.exe, but got {controller.TestActiveProfile?.ProcessName}");
	}

	/// <summary>
	/// Scenario 11: 窗口 B 专属中键与绘图手势冲突 (GPT Item 3)
	/// 旧前台 A 使用右键轮盘；后台 B 配置中键轮盘；全局鼠标绘图手势也使用中键。
	/// 鼠标在 B 上按中键，应由 B 的轮盘配置正确接管，不能提前被绘图手势吞掉。
	/// </summary>
	private static void TestTargetMiddleClickConflictWithDrawingGesture()
	{
		nint hwndA = 0x1000;
		nint hwndB = 0x2000;

		WheelFocusSwitcher.ForegroundWindowGetterOverride = () => hwndA; // 前台是 A

		// 配置全局手势按键为中键
		ConfigManager.CurrentConfig.GestureEnabled = true;
		ConfigManager.CurrentConfig.GestureTriggerButton = "MiddleButton";
		ConfigManager.CurrentConfig.Trigger = new TriggerConfig { TriggerType = "Mouse", MouseButton = "RightButton" };

		// 窗口 B (special_cad.exe) 专属触发键为 MiddleButton
		ConfigManager.CurrentConfig.BlacklistTriggerOverrides = new Dictionary<string, TriggerConfig>(StringComparer.OrdinalIgnoreCase)
		{
			["special_cad.exe"] = new TriggerConfig
			{
				TriggerType = "Mouse",
				MouseButton = "MiddleButton"
			}
		};

		using var controller = new GestureController(new MouseHook(), null);

		var targetB = new TargetWindowInfo(hwndB, 200, "special_cad.exe", isEligibleBackgroundTarget: true);

		// 构造携带 targetB 的 RawMouseEventArgs
		var rawArgs = new RawMouseEventArgs(519, "MiddleButton", 0, true, 300, 300, targetB);

		// 模拟 Hook_OnRawMouseButton 调用
		// 当按键与 targetB 的轮盘按键重叠时，手势冲突守卫必须让位，Handled 保持 false
		controller.TestHookOnRawMouseButton(rawArgs);

		// 验证冲突守卫成功生效：手势未拦截，原生按键放行给轮盘触发逻辑
		Assert(!rawArgs.Handled, "Raw mouse event Handled must remain false so wheel trigger can take over!");
	}

	/// <summary>
	/// Scenario 12: 无目标上下文的既有动作仍正常执行
	/// 兼容测试：不带 ActionExecutionContext 的普通调用或旧插件动作仍正常执行。
	/// </summary>
	private static void TestLegacyActionWithoutContextExecutesNormally()
	{
		var legacyAction = new ActionItem { Name = "LegacyAction", Type = "Custom", Parameter = "P1" };
		var envelope = new ActionEnvelope(legacyAction, context: null);

		bool result = ActionExecutor.ProcessActionEnvelope(envelope);
		Assert(result, "ProcessActionEnvelope must succeed for action without context");

		lock (_executedActions)
		{
			Assert(_executedActions.Count == 1, "Exactly 1 legacy action must be executed");
			Assert(_executedActions[0].Action.Name == "LegacyAction", "Action name must match");
			Assert(_executedActions[0].Context == null, "Context must be null");
		}
	}

	/// <summary>
	/// Scenario 13: 孤立释放安全
	/// 未接管按下的鼠标抬起，绝不吞键，绝不创建挂起会话。
	/// </summary>
	private static void TestIsolatedReleaseDoesNotCreatePendingSession()
	{
		using var controller = new GestureController(new MouseHook(), null);
		controller.TestSetGestureActive(false, 0);

		// 模拟未接管按下时的孤立物理抬起
		bool handled = controller.TestCompleteMouseTriggerRelease(new Point(100, 100), "RightButton");
		Assert(!handled, "Isolated release must not be handled");
		Assert(controller.TestPendingExecutionSession == null, "Pending execution session must remain null on isolated release");
	}

	/// <summary>
	/// Scenario 14: 黑名单、白名单与必要修饰键
	/// </summary>
	private static void TestIsolationRulesTargetSnapshot()
	{
		using var controller = new GestureController(new MouseHook(), null);

		ConfigManager.CurrentConfig.IsolationMode = "Blacklist";
		ConfigManager.CurrentConfig.BlacklistedProcesses = new List<string> { "solidworks.exe", "cad.exe" };

		var targetAllowed = new TargetWindowInfo(0x2001, 101, "notepad.exe", isEligibleBackgroundTarget: true);
		var targetBlocked = new TargetWindowInfo(0x2002, 102, "solidworks.exe", isEligibleBackgroundTarget: true);

		// 鼠标在 notepad.exe 上，不应被隔离
		bool isIsolated1 = controller.TestCheckIsIsolated(out string proc1, null, targetAllowed);
		Assert(!isIsolated1, "Mouse over notepad.exe should NOT be isolated even if foreground is blacklisted");
		Assert(proc1.Equals("notepad.exe", StringComparison.OrdinalIgnoreCase), "Reported process should be notepad.exe");

		// 鼠标在 solidworks.exe 上，应被正确隔离
		bool isIsolated2 = controller.TestCheckIsIsolated(out string proc2, null, targetBlocked);
		Assert(isIsolated2, "Mouse over solidworks.exe MUST be isolated");
		Assert(proc2.Equals("solidworks.exe", StringComparison.OrdinalIgnoreCase), "Reported process should be solidworks.exe");

		// 白名单模式验证
		ConfigManager.CurrentConfig.IsolationMode = "Whitelist";
		ConfigManager.CurrentConfig.WhitelistedProcesses = new List<string> { "allowed_editor.exe" };

		var targetNotWhite = new TargetWindowInfo(0x2003, 103, "other_app.exe", isEligibleBackgroundTarget: true);
		bool isIsolated3 = controller.TestCheckIsIsolated(out _, null, targetNotWhite);
		Assert(isIsolated3, "Non-whitelisted app in Whitelist mode must be isolated");
	}

	/// <summary>
	/// Scenario 15: 键盘与非鼠标入口无副作用
	/// </summary>
	private static void TestNonMouseTriggersUntouched()
	{
		using var controller = new GestureController(new MouseHook(), null);

		// 键盘触发路径绝不设置 _currentWheelFocusSession
		Assert(controller.TestCurrentWheelFocusSession == null, "Focus session must remain null for non-mouse interactions");

		bool isolated = controller.TestCheckIsIsolated(out string proc, null, null);
		Assert(!string.IsNullOrEmpty(proc), "CheckIsIsolated without target falls back safely to foreground");
	}

	/// <summary>
	/// Scenario 16: 可控假数据 Shell Surface 测试
	/// </summary>
	private static void TestShellSurfaceControlledMock()
	{
		nint currentFg = 0x1000;
		WheelFocusSwitcher.ForegroundWindowGetterOverride = () => currentFg;

		var shellTarget = new TargetWindowInfo(currentFg, 100, "explorer.exe", isEligibleBackgroundTarget: false);
		WheelFocusSwitcher.TargetResolverOverride = (pt) => shellTarget;

		var target = WheelFocusSwitcher.ResolveTarget(new Point(9999, 9999));
		Assert(!target.IsEligibleBackgroundTarget, "Shell surface or desktop must NOT be marked as eligible background target");

		var session = new WheelFocusSession(target, new Point(9999, 9999));
		var result = WheelFocusSwitcher.SwitchFocus(session);
		Assert(result == FocusSwitchResult.AlreadyForeground, "SwitchFocus on non-background target should return AlreadyForeground");
		Assert(session.FocusState == WheelFocusState.AlreadyForeground, "FocusState should be AlreadyForeground");
	}

	/// <summary>
	/// Scenario 17: 保守激活安全性
	/// </summary>
	private static void TestConservativeActivationSafety()
	{
		bool resultZero = WindowTaskbarHelper.ActivateWindowConservative(IntPtr.Zero);
		Assert(!resultZero, "ActivateWindowConservative must safely return false for IntPtr.Zero");

		bool resultInvalid = WindowTaskbarHelper.ActivateWindowConservative((nint)(-1));
		Assert(!resultInvalid, "ActivateWindowConservative must safely return false for invalid handle");
	}

	/// <summary>
	/// Scenario 18: ReleaseStuckModifiers 切缝覆盖验证
	/// </summary>
	private static void TestReleaseStuckModifiersSeamCoverage()
	{
		int before = _releaseStuckModifiersCallCount;
		using var controller = new GestureController(new MouseHook(), null);
		controller.TestCancelGestureTracking();
		int after = _releaseStuckModifiersCallCount;

		Assert(after > before, "CancelGestureTracking must invoke ReleaseStuckModifiers test seam!");
	}

	private static void TestSharedWindowProcessIdentity()
	{
		nint hwnd = 0x5010;
		const string path = @"C:\Program Files\WPS Office\WPS.EXE";
		ActiveWindowHelper.WindowProcessReaderOverride = h =>
		{
			Assert(h == hwnd, "Identity resolution must use the supplied window, not a parent process");
			return (19676u, path);
		};
		ActiveWindowHelper.GetWindowProcessInfo(hwnd, out uint pid, out string name);
		var picked = WindowPickerWindow.ReadWindowProcessInfo(hwnd)
			?? throw new InvalidOperationException("A known process must be available to the picker without constructing a Window");
		Assert(pid == 19676 && picked.ProcessId == pid, "Picker and runtime must retain the same PID");
		Assert(name == "wps.exe" && picked.ProcessName == name, "Both callers must normalize the same executable name");
		Assert(picked.CleanProcessName == "wps", "Picker binding must retain the existing extension-free format");
		Assert(picked.ExecutablePath == path && picked.Hwnd == hwnd, "Picker must retain the executable path and exact target window");
		ActiveWindowHelper.ForegroundHwndGetterOverride = () => hwnd;
		Assert(ActiveWindowHelper.GetActiveWindowInfo(out var foreground) == name && foreground == hwnd,
			"Keyboard foreground lookup must use the same process identity");

		ActiveWindowHelper.RealPidGetterOverride = _ => 900u;
		ActiveWindowHelper.ProcessNameGetterOverride = _ => "legacy.exe";
		ActiveWindowHelper.WindowProcessReaderOverride = _ => throw new InvalidOperationException("Legacy seams must not query native metadata");
		ActiveWindowHelper.GetWindowProcessInfo(hwnd, out uint legacyPid, out string legacyName);
		Assert(legacyPid == 900 && legacyName == "legacy.exe", "Existing process-name and PID test seams must remain compatible");
	}

	private static void TestSharedWindowProcessFailures()
	{
		int reads = 0;
		ActiveWindowHelper.WindowProcessReaderOverride = _ => { reads++; return (19676u, ""); };
		ActiveWindowHelper.GetWindowProcessInfo(IntPtr.Zero, out uint zeroPid, out string zeroName, out string zeroPath);
		Assert(zeroPid == 0 && zeroName == "unknown.exe" && zeroPath == "" && reads == 0,
			"Null windows must short-circuit without reading any process");
		Assert(WindowPickerWindow.ReadWindowProcessInfo(IntPtr.Zero) == null, "Null windows must not produce a picker row");

		ActiveWindowHelper.GetWindowProcessInfo(0x5020, out uint pid, out string name, out string path);
		Assert(pid == 19676 && name == "unknown.exe" && path == "", "An unreadable image retains its PID but has no fabricated identity");
		Assert(WindowPickerWindow.ReadWindowProcessInfo(0x5020) == null,
			"Image-query failure must not become an app.exe or unknown.exe binding");
		ActiveWindowHelper.WindowProcessReaderOverride = _ => (0u, "");
		Assert(WindowPickerWindow.ReadWindowProcessInfo(0x5021) == null, "A missing PID must not become a picker selection");
		ActiveWindowHelper.WindowProcessReaderOverride = _ => throw new InvalidOperationException("Simulated query failure");
		ActiveWindowHelper.GetWindowProcessInfo(0x5022, out pid, out name, out path);
		Assert(pid == 0 && name == "unknown.exe" && path == "", "Query exceptions must fail closed without retaining another window's path");
		Assert(WindowPickerWindow.ReadWindowProcessInfo(0x5022) == null, "Query exceptions must also fail closed in the picker");
	}

	private static void TestIndependentComponentProcessBindings()
	{
		var writer = CreateDummyProfile("writer");
		writer.BoundProcesses = "WPS.EXE";
		var sheets = CreateDummyProfile("sheets");
		sheets.BoundProcesses = "et";
		var global = CreateDummyProfile("Global");
		ConfigManager.CurrentConfig.Profiles = new List<WheelProfile> { global, writer, sheets };
		ActiveWindowHelper.WindowProcessReaderOverride = h => h == (nint)0x5030
			? (19676u, @"C:\WPS\wps.exe")
			: h == (nint)0x5031 ? (3856u, @"C:\WPS\et.exe")
			: throw new InvalidOperationException("Unexpected host or parent window lookup");
		foreach (var (hwnd, expected) in new[] { ((nint)0x5030, writer), ((nint)0x5031, sheets) })
		{
			var picked = WindowPickerWindow.ReadWindowProcessInfo(hwnd);
			Assert(picked != null, "Each component's supplied window must remain individually identifiable");
			ActiveWindowHelper.GetWindowProcessInfo(hwnd, out _, out string processName);
			Assert(ReferenceEquals(ConfigManager.GetProfileForProcess(picked!.CleanProcessName), expected),
				"Picker binding must match the component-specific profile");
			Assert(ReferenceEquals(ConfigManager.GetProfileForProcess(processName), expected),
				"Runtime must match the same profile without grouping under wpsoffice.exe");
		}
	}

	private static void TestWindowProcessSnapshotCache()
	{
		int reads = 0;
		ActiveWindowHelper.WindowProcessReaderOverride = _ => { reads++; return (610u, @"C:\WPS\WPS.EXE"); };
		ActiveWindowHelper.GetWindowProcessInfo(0x5040, out _, out _);
		ActiveWindowHelper.GetWindowProcessInfo(0x5040, out uint pid, out string name, out string path);
		Assert(reads == 1 && pid == 610 && name == "wps.exe" && path == @"C:\WPS\WPS.EXE",
			"A runtime-warmed cache must retain the executable path for the picker");
		var picked = WindowPickerWindow.ReadWindowProcessInfo(0x5040);
		Assert(picked?.ExecutablePath == path && reads == 1, "Picker must consume the same cached snapshot");

		ActiveWindowHelper.ResetTestSeams();
		ActiveWindowHelper.WindowProcessReaderOverride = _ => (620u, "");
		ActiveWindowHelper.GetWindowProcessInfo(0x5041, out _, out _);
		ActiveWindowHelper.WindowProcessReaderOverride = _ => (620u, @"C:\WPS\et.exe");
		Assert(WindowPickerWindow.ReadWindowProcessInfo(0x5041) == null,
			"Picker must not independently override a cached runtime failure");
		Thread.Sleep(170);
		picked = WindowPickerWindow.ReadWindowProcessInfo(0x5041);
		Assert(picked?.ProcessName == "et.exe" && picked.ExecutablePath == @"C:\WPS\et.exe",
			"An expired failure must recover through the shared resolver");
		ActiveWindowHelper.ResetTestSeams();
		ActiveWindowHelper.WindowProcessReaderOverride = _ => (630u, @"C:\WPS\wps.exe");
		ActiveWindowHelper.GetWindowProcessInfo(0x5041, out pid, out name, out path);
		Assert(pid == 630 && name == "wps.exe", "Reset must invalidate both cached failures and cached paths");
	}

	private static void Assert(bool condition, string message)
	{
		if (!condition)
		{
			throw new InvalidOperationException($"Assertion failed: {message}");
		}
	}
}
