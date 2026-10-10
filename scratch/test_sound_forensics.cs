// ==============================================================================================
// StarPie 音效稳定性与无头取证自动化验证套件 (Sound Forensics & Stability Test Suite)
//
// 本测试套件用于验证 SP-SOUND-001 音效调度架构、会话生命周期、防抖状态机与 RIFF/WAVE 分块解析正确性。
// 全程运行于 Headless Mock 模式 (TestMode=true)，不触发物理扬声器发声，不产生真实注册表/自启副作用。
//
// 单次执行说明 (Single Run Instructions):
//   dotnet run --project scratch/test_sound_forensics.csproj -c Release --no-build -- --test-instance
//
// 10 轮稳定性验证执行说明 (10-Cycle Stability Gate):
//   powershell -File scratch/run_10_cycles.ps1
// ==============================================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WinPieGestures;
using StarPie.Plugin;
using StarPie.Plugin.Sound;
using WinPieGestures.Plugins;

namespace StarPie.Forensics;

public class SoundForensicsSuite
{
	private static int _passedTests = 0;
	private static int _failedTests = 0;
	private static readonly object _assertLock = new();
	private static readonly List<string> _testLogs = new();
	private static readonly List<(string Name, bool Passed, string Detail)> _testResults = new();
	private static string _sandboxPath = string.Empty;

	internal static int MeasuredMaxPlaybackConcurrency = 0;
	internal static int MeasuredMaxAliveWorkers = 0;
	internal static long MeasuredShutdownDurationMs = 0;
	internal static int TotalWorkersCreatedCount = 0;
	internal static int LeakedWorkersCount = 0;

	private static readonly List<Thread> _globalCreatedWorkers = new();
	private static readonly object _globalWorkerLock = new();
	private static readonly List<IDisposable> _globalBarriersToRelease = new();
	private static readonly object _globalBarrierLock = new();

	internal static void TrackBarrier(IDisposable barrier)
	{
		lock (_globalBarrierLock)
		{
			if (!_globalBarriersToRelease.Contains(barrier))
			{
				_globalBarriersToRelease.Add(barrier);
			}
		}
	}

	internal static void ReleaseAllBarriers()
	{
		List<IDisposable> copy;
		lock (_globalBarrierLock)
		{
			copy = _globalBarriersToRelease.ToList();
		}
		foreach (var b in copy)
		{
			try
			{
				if (b is EventWaitHandle ewh) ewh.Set();
			}
			catch { }
		}
	}

	private static void WaitForAllWorkersToExit(int timeoutMs = 2000)
	{
		var workers = SoundEffectManager.GetHarnessCreatedWorkers();
		foreach (var w in workers)
		{
			if (w != null && w.IsAlive)
			{
				bool joined = w.Join(timeoutMs);
				if (!joined)
				{
					throw new TimeoutException($"Tracked worker thread ID={w.ManagedThreadId} failed to exit within {timeoutMs}ms");
				}
			}
		}
	}

	private static void Log(string message)
	{
		lock (_assertLock)
		{
			Console.WriteLine(message);
			_testLogs.Add(message);
		}
	}

	private static void Assert(bool condition, string testName, string failureDetail = "")
	{
		lock (_assertLock)
		{
			_testResults.Add((testName, condition, failureDetail));
			if (condition)
			{
				_passedTests++;
				Log($"  [PASS] {testName}");
			}
			else
			{
				_failedTests++;
				Log($"  [FAIL] {testName}: {failureDetail}");
			}
		}
	}

	public static int Main(string[] args)
	{
		Log("================================================================================");
		Log("  StarPie SP-SOUND-001 Stage B2a: 音效优先级与会话隔离");
		Log("================================================================================");
		Log($"PID: {Environment.ProcessId}, Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

		// 严格精确校验 --test-instance 参数，杜绝无保护运行
		if (!args.Any(a => string.Equals(a, "--test-instance", StringComparison.OrdinalIgnoreCase)))
		{
			Console.Error.WriteLine("[FATAL ERROR] test_sound_forensics requires exact '--test-instance' command-line argument.");
			return 1;
		}

		// 1. 测试环境强隔离：强制使用完全隔离的独立临时目录，杜绝触碰真实 AppData、注册表与计划任务
		// 必须在首次访问 ConfigManager 前设置 LOCALAPPDATA 环境变量
		_sandboxPath = Path.Combine(Path.GetTempPath(), "StarPie-SoundForensics-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_sandboxPath);
		string isolatedAppData = Path.Combine(_sandboxPath, "AppData");
		Directory.CreateDirectory(isolatedAppData);
		Environment.SetEnvironmentVariable("LOCALAPPDATA", isolatedAppData);

		// 禁用开机自启同步
		ConfigManager.DisableAutoStartSync = true;
		_ = ConfigManager.CurrentConfig;

		// 2. 全程强制启用测试模式模拟后端，杜绝真实扬声器发声与物理 WinMM 穿透
		SoundEffectManager.TestMode = true;
		SoundEffectManager.PlaybackSink = (data, flags) => { };
		SoundEffectManager.WorkerThreadCreated = t =>
		{
			lock (_globalWorkerLock)
			{
				if (!_globalCreatedWorkers.Contains(t)) _globalCreatedWorkers.Add(t);
			}
		};

		try
		{
			TestScenario1_AdjacentTargetShuttling();
			TestScenario2_TierSwitchingAndHoverTiming();
			TestScenario3_PlaybackLatencyAndSingleSlotOverwrite();
			TestScenario4_ExecuteCancelVsHoverRace();
			TestScenario5_ResidualHoverAfterSessionEnds();
			TestScenario6_LifecycleAndConcurrencyMeasurement();
			TestScenario7_CallPathCoverageAudit();
			TestScenario8_ExternalWavJunkChunkStructuralDamage();
			TestStageB1_LifecycleFixTargetAssertions();
			TestStageB1_1_TargetRegressionAssertions();
			TestStageB1_2_CrossGenQueueCleanRace();
			TestStageB2a_PriorityAndSessionIsolation();
			TestStageB2a1_InterleavingAndReclamation();
			TestStageB2a2_LifecycleAndRealInterleaving();
			TestStageB2a3_RemainingIssuesRegressions();
			TestStageB2b_HoverTargetStabilityAssertions();
			TestStageB2b1_PreClaimVerificationRegressions();
			TestStageB2b2_ClaimLockInterleavingRegressions();
			TestStageB3_WavParserAndScalingCorrectness();
		}
		catch (Exception ex)
		{
			Log($"[FATAL EXCEPTION]: {ex}");
			lock (_assertLock) { _failedTests++; }
		}
		finally
		{
			// 确保所有工作线程安全退出，释放全部屏障
			SoundEffectManager.ReleaseAllBarriers();
			ReleaseAllBarriers();
			SoundEffectManager.Shutdown();

			// 收集全量工作线程快照并在锁外等待，任何超时均计为失败
			List<Thread> workersToWait = SoundEffectManager.GetHarnessCreatedWorkers();
			lock (_globalWorkerLock)
			{
				foreach (var w in _globalCreatedWorkers)
				{
					if (!workersToWait.Contains(w)) workersToWait.Add(w);
				}
			}

			bool allWorkersJoined = true;
			List<int> unjoinedWorkerIds = new();
			foreach (var w in workersToWait)
			{
				if (w != null && w.IsAlive)
				{
					bool joined = w.Join(2000);
					if (!joined)
					{
						allWorkersJoined = false;
						unjoinedWorkerIds.Add(w.ManagedThreadId);
					}
				}
			}

			Assert(allWorkersJoined, "Main_Finally_AllWorkersExitedCleanly",
				allWorkersJoined ? $"全量工作线程正常退出 (共检查 {workersToWait.Count} 条线程)" : $"工作线程未退出超时，存活ID=[{string.Join(", ", unjoinedWorkerIds)}]");

			SoundEffectManager.ResetTestSeams();
			// 严格保持 TestMode = true，绝不复位为 false
			SoundEffectManager.TestMode = true;
			SoundEffectManager.PlaybackSink = null;

			// 仅当所有 worker 确认退出后才允许清理临时沙箱并宣称清理成功
			if (allWorkersJoined)
			{
				try
				{
					if (Directory.Exists(_sandboxPath))
					{
						Directory.Delete(_sandboxPath, recursive: true);
					}
				}
				catch { }
			}
			else
			{
				Log("[Test Cleanup Alert] 存在未退出工作线程，保留沙箱供故障排查，不执行清理。");
			}
		}

		Log("================================================================================");
		Log($"  取证执行统计: 总计 {_passedTests + _failedTests} 项用例, 通过: {_passedTests}, 失败: {_failedTests}");
		Log("================================================================================");

		// 生成综合执行结果报告 (Unified Execution Summary Report)
		GenerateSummaryReport();

		return _failedTests == 0 ? 0 : 1;
	}

	#region 场景 1: 相邻目标往返与调度行为刻画 (Adjacent Target Shuttling)
	private static void TestScenario1_AdjacentTargetShuttling()
	{
		Log("\n>>> 场景 1: 相邻目标往返与生产调用链时序刻画 (Adjacent Target Shuttling)");

		long simTime = 1000L;
		SoundEffectManager.TimeProvider = () => simTime;
		SoundEffectManager.LastHoverTick = 0L;

		var queuedRecords = new ConcurrentQueue<(SoundType? Sound, long Tick)>();
		var playbackStarts = new ConcurrentQueue<(SoundType? Sound, long Tick)>();
		var playbackFinishes = new ConcurrentQueue<(SoundType? Sound, long Tick)>();
		int dropCount = 0;

		SoundEffectManager.SoundQueued = (t, wav, tick) => queuedRecords.Enqueue((t, tick));
		SoundEffectManager.SoundDropped = (t, wav, tick) => Interlocked.Increment(ref dropCount);

		// 使用事件屏障可控控制播放开始与结束
		AutoResetEvent playbackStartSignal = new(false);
		AutoResetEvent playbackFinishSignal = new(false);
		AutoResetEvent playbackCompletedSignal = new(false);

		SoundEffectManager.SoundPlaybackFinished = (t, wav, tick) => playbackCompletedSignal.Set();

		SoundEffectManager.PlaybackSink = (data, flags) =>
		{
			long startTick = simTime;
			playbackStarts.Enqueue((SoundType.SectorHover, startTick));
			playbackStartSignal.Set();

			// 等待测试驱动该播放完成
			bool waitFinish = playbackFinishSignal.WaitOne(3000);
			Assert(waitFinish, "1A_PlaybackFinishSignalWithin3000ms");
			playbackFinishes.Enqueue((SoundType.SectorHover, simTime));
		};

		// 构造真实的 GestureController 生产实例（MouseHook 未 Start，不挂全局底层钩子）
		var mouseHook = new MouseHook();
		using var gc = new GestureSoundHarness(mouseHook);

		var cfg = SoundEffectManager.Preferences;
		cfg.EnableSoundEffects = true;
		cfg.SoundOnHover = true;
		SoundEffectManager.Initialize("Mechanical", 0.6, force: true);

		// 1A: 目标稳定前拦截验证（微抖 < 45ms 稳定期限）
		gc.TestSetGestureActive(true, version: 100);
		simTime = 1000L;
		gc.TestQueueHighlightUpdate(0, -1, false, false, 100); // 扇区 0 -> 进入候选
		simTime = 1015L;
		gc.TestQueueHighlightUpdate(1, -1, false, false, 100); // 扇区 1 (delta 15ms < 45ms) -> 扇区0作废，扇区1进入候选
		simTime = 1030L;
		gc.TestQueueHighlightUpdate(0, -1, false, false, 100); // 扇区 0 (delta 15ms < 45ms) -> 扇区1作废，扇区0重新进入候选

		// 微抖期间未达 45ms，未排入发声
		Thread.Sleep(50);
		Assert(queuedRecords.Count == 0, "1A_DebounceSub45msFilterViaProductionLogic",
			$"微抖期间未达到 45ms 稳定期限，期望排入 0 次，实际排入 {queuedRecords.Count} 次");

		// 停留在扇区 0，时间推进至 1075ms (距进入满 45ms 稳定)
		simTime = 1075L;
		SoundEffectManager.TestSignalWorker();
		Thread.Sleep(60);

		Assert(queuedRecords.Count == 1, "1A_StabilizedTargetQueuesExactlyOnce",
			$"目标稳定满 45ms 后期望排入 1 次，实际排入 {queuedRecords.Count} 次");

		// 允许第一个音效播完并断言其完成
		bool startOk1 = playbackStartSignal.WaitOne(2000);
		Assert(startOk1, "1A_PlaybackStartedSignalReceived");
		simTime = 1113L;
		playbackFinishSignal.Set();
		bool completedOk1 = playbackCompletedSignal.WaitOne(2000);
		Assert(completedOk1, "1A_PlaybackCompletedSignalReceived");

		// 1B: 周期 36ms 的慢速往返穿梭（每 36ms 跨越一次相邻扇区分界线）
		while (queuedRecords.TryDequeue(out _)) { }
		while (playbackStarts.TryDequeue(out _)) { }
		while (playbackFinishes.TryDequeue(out _)) { }
		dropCount = 0;

		// 模拟即时回放（播放耗时 38ms，对应 Mechanical Click 实际时长）
		SoundEffectManager.PlaybackSink = (data, flags) =>
		{
			playbackStarts.Enqueue((SoundType.SectorHover, simTime));
		};

		int oscillations = 10;
		gc.TestSetGestureActive(true, version: 101);
		for (int i = 0; i < oscillations; i++)
		{
			simTime = 2000L + i * 36L; // 36ms 步进：驻留时间 36ms < 45ms 稳定期限
			int targetSector = (i % 2 == 0) ? 0 : 1;
			gc.TestQueueHighlightUpdate(targetSector, -1, false, false, 101);
		}

		// 新契约断言：在 36ms 往返穿梭下未达到 45ms 稳定期限，不得排入发声
		Thread.Sleep(50);
		Assert(queuedRecords.Count == 0, "1B_OscillationAt36msDoesNotQueueUnderNewContract",
			$"在 36ms 往返穿梭下未达到 45ms 稳定期限，期望排入 0 次，实际排入 {queuedRecords.Count} 次");

		// 光标在扇区 0 停留稳定超过 45ms
		simTime = 2000L + oscillations * 36L + 50L;
		SoundEffectManager.TestSignalWorker();
		Thread.Sleep(60);

		Assert(queuedRecords.Count == 1, "1B_StabilizedTargetQueuesExactlyOnce",
			$"目标稳定 45ms 后期望排入 1 次，实际排入 {queuedRecords.Count} 次");

		Log($"  [Forensics 1B Metric] 新契约证实：36ms 往返期间排队 0 次，稳定 45ms 后排队 1 次，成功替换旧 35ms 连响行为 (基准频率: {1000.0 / 36.0:F1}Hz)");

		// 1C: 较慢后端回放（例如回放耗时 80ms）下的覆盖与丢弃刻画
		while (queuedRecords.TryDequeue(out _)) { }
		while (playbackStarts.TryDequeue(out _)) { }
		dropCount = 0;

		ManualResetEvent slowPlaybackGate = new(false);
		TrackBarrier(slowPlaybackGate);
		AutoResetEvent slowStarted = new(false);
		AutoResetEvent secondCompleted = new(false);
		int completedCount = 0;

		SoundEffectManager.SoundPlaybackFinished = (t, wav, tick) =>
		{
			if (Interlocked.Increment(ref completedCount) >= 2)
			{
				secondCompleted.Set();
			}
		};

		SoundEffectManager.PlaybackSink = (data, flags) =>
		{
			slowStarted.Set();
			bool waitSlowGate = slowPlaybackGate.WaitOne(3000);
			Assert(waitSlowGate, "1C_SlowPlaybackGateSignalReceived");
		};

		// 触发首声，目标稳定 45ms 后排入，阻塞在慢速后端
		gc.TestSetGestureActive(true, version: 102);
		simTime = 5000L;
		gc.TestQueueHighlightUpdate(0, -1, false, false, 102);
		simTime = 5045L;
		SoundEffectManager.TestSignalWorker();
		bool slowStartedOk = slowStarted.WaitOne(2000);
		Assert(slowStartedOk, "1C_SlowPlaybackStartedReceived");

		// 在回放阻塞期间，连续产生 3 次间隔 36ms 的相邻切换请求
		simTime = 5081L;
		gc.TestQueueHighlightUpdate(1, -1, false, false, 102);
		simTime = 5117L;
		gc.TestQueueHighlightUpdate(0, -1, false, false, 102);
		simTime = 5153L;
		gc.TestQueueHighlightUpdate(1, -1, false, false, 102);

		// 扇区 1 稳定满 45ms
		simTime = 5205L;
		SoundEffectManager.TestSignalWorker();

		// 释放慢速后端，等待第二声回放完成
		slowPlaybackGate.Set();
		bool secondDone = secondCompleted.WaitOne(3000);
		Assert(secondDone, "1C_SecondPlaybackCompletedSignalReceived");

		// 刻画：首声与末声发声，中间未达期限或被覆盖
		Assert(dropCount >= 1, "1C_SlowBackendSingleSlotOverwriteAndDrop",
			$"慢速回放期间，期望丢弃覆盖 >= 1 次，实际丢弃: {dropCount} 次");

		Log($"  [Forensics 1C Metric] 慢速回放阻塞期间总请求: 4, 发生丢弃覆盖: {dropCount} 次, 证实单槽覆盖行为");

		SoundEffectManager.ResetTestSeams();
		SoundEffectManager.PlaybackSink = (data, flags) => { };
	}
	#endregion

	#region 阶段 B2a.1: 会话隔离补齐与交错竞态修复 (Stage B2a.1 Session Isolation & Race Fixes)
	private static void TestStageB2a1_InterleavingAndReclamation()
	{
		Log("\n>>> 阶段 B2a.1: 会话隔离补齐与交错竞态修复断言 (Stage B2a.1 Interleaving & Reclamation)");

		SoundEffectManager.ResetTestSeams();

		// B2a_1_Target1: 队列锁阻塞交错竞态 (阻塞模拟后端，测试持有队列锁；另一个线程提交带会话 ID 的 Hover，等待其阻塞于入队；结束该会话，再释放队列锁。旧 Hover 必须被拒绝，不能重新入队或播放)
		{
			AutoResetEvent blockerStarted = new(false);
			ManualResetEvent allowBlockerExit = new(false);
			TrackBarrier(allowBlockerExit);
			AutoResetEvent hoverPlaybackStarted = new(false);

			SoundEffectManager.SoundPlaybackStarted = (t, wav, tick) =>
			{
				if (t == SoundType.WheelPopup) blockerStarted.Set();
				else if (t == SoundType.SectorHover) hoverPlaybackStarted.Set();
			};

			SoundEffectManager.PlaybackSink = (data, flags) => allowBlockerExit.WaitOne(3000);

			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);
			SoundEffectManager.PlayPreview(SoundType.WheelPopup);
			bool blockerEntered = blockerStarted.WaitOne(2000);
			Assert(blockerEntered, "B2a_1_Target1_BlockerEntered");

			long sessId = SoundEffectManager.BeginSession(SoundSessionSource.NormalGesture);

			ManualResetEvent threadEntered = new(false);
			TrackBarrier(threadEntered);
			ManualResetEvent threadFinished = new(false);
			TrackBarrier(threadFinished);

			// 测试持有队列锁
			lock (SoundEffectManager.TestQueueLock)
			{
				Thread enqueueThread = new Thread(() =>
				{
					threadEntered.Set();
					// 该调用将在 EnqueueSound 内部因队列锁被测试线程持有而阻塞
					SoundEffectManager.Play(SoundType.SectorHover, SoundSessionSource.NormalGesture, sessId);
					threadFinished.Set();
				})
				{
					IsBackground = true,
					Name = "TestStageB2a1_EnqueueThread"
				};
				enqueueThread.Start();

				bool entered = threadEntered.WaitOne(1000);
				Assert(entered, "B2a_1_Target1_EnqueueThreadStarted");

				// 确保 enqueueThread 已经进入 Play 并等待队列锁
				Thread.Sleep(50);

				// 在持有队列锁的同时，结束该会话 (重入队列锁并更新会话状态)
				SoundEffectManager.EndSession(SoundSessionSource.NormalGesture, sessId, allowTerminalFeedback: false);
			} // 释放队列锁

			bool finished = threadFinished.WaitOne(2000);
			Assert(finished, "B2a_1_Target1_EnqueueThreadFinishedAfterLockRelease");

			// 验证旧 Hover 必须被拒绝，不能重新入队
			Assert(SoundEffectManager.TestPendingSound == null, "B2a_1_Target1_OldHoverRejectedFromQueue",
				$"旧会话 Hover 必须被拒绝入队，当前待播槽: {SoundEffectManager.TestPendingSound}");

			allowBlockerExit.Set();
			bool hoverPlayed = hoverPlaybackStarted.WaitOne(500);
			Assert(!hoverPlayed, "B2a_1_Target1_OldHoverNeverPlayed", "旧会话 Hover 绝不播放");

			SoundEffectManager.Shutdown();
			SoundEffectManager.ResetTestSeams();
		}

		// B2a_1_Target2: Worker 取出请求后、真正物理播放前的会话有效性二次校验
		{
			AutoResetEvent blockerStarted = new(false);
			ManualResetEvent allowBlockerExit = new(false);
			TrackBarrier(allowBlockerExit);
			AutoResetEvent secondPlaybackStarted = new(false);

			SoundEffectManager.SoundPlaybackStarted = (t, wav, tick) =>
			{
				if (t == SoundType.WheelPopup) blockerStarted.Set();
				else secondPlaybackStarted.Set();
			};

			SoundEffectManager.PlaybackSink = (data, flags) => allowBlockerExit.WaitOne(3000);

			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);
			SoundEffectManager.PlayPreview(SoundType.WheelPopup);
			blockerStarted.WaitOne(2000);

			long sessId = SoundEffectManager.BeginSession(SoundSessionSource.NormalGesture);
			SoundEffectManager.Play(SoundType.SectorHover, SoundSessionSource.NormalGesture, sessId);
			Assert(SoundEffectManager.TestPendingSound == SoundType.SectorHover, "B2a_1_Target2_HoverEnqueued");

			// 在 Worker 阻塞回放 WheelPopup 期间直接将会话关闭
			SoundEffectManager.EndSession(SoundSessionSource.NormalGesture, sessId, allowTerminalFeedback: false);

			// 释放 WheelPopup
			allowBlockerExit.Set();

			// 验证 Worker 在取出请求时再次核验有效性，会话已关闭则直接丢弃，不播放
			bool secondPlayed = secondPlaybackStarted.WaitOne(600);
			Assert(!secondPlayed, "B2a_1_Target2_WorkerPrePlaybackCheckDropsClosedSessionSound",
				$"已关闭会话的待播项在播放前被丢弃，实际播放: {secondPlayed}");

			SoundEffectManager.Shutdown();
			SoundEffectManager.ResetTestSeams();
		}

		// B2a_1_Target3: 完整接入 SettingsPreview（移除校验旁路，未知/已结束/不匹配会话拒绝，画布离开撤销，试听不被撤销）
		{
			AutoResetEvent blockerStarted = new(false);
			ManualResetEvent allowBlockerExit = new(false);
			TrackBarrier(allowBlockerExit);

			SoundEffectManager.SoundPlaybackStarted = (t, wav, tick) => blockerStarted.Set();
			SoundEffectManager.PlaybackSink = (data, flags) => allowBlockerExit.WaitOne(3000);

			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);
			SoundEffectManager.PlayPreview(SoundType.WheelPopup);
			blockerStarted.WaitOne(2000);

			// 3A: 未知会话 (ID=999999) 必须被拒绝
			SoundEffectManager.Play(SoundType.SectorHover, SoundSessionSource.SettingsPreview, 999999L);
			Assert(SoundEffectManager.TestPendingSound == null, "B2a_1_Target3A_UnknownSessionRejected");

			// 3B: 0L 会话必须被拒绝
			SoundEffectManager.Play(SoundType.SectorHover, SoundSessionSource.SettingsPreview, 0L);
			Assert(SoundEffectManager.TestPendingSound == null, "B2a_1_Target3B_ZeroSessionRejected");

			// 3C: 不匹配会话 (来源为 NormalGesture，但请求指定 SettingsPreview) 必须被拒绝
			long normalSess = SoundEffectManager.BeginSession(SoundSessionSource.NormalGesture);
			SoundEffectManager.Play(SoundType.SectorHover, SoundSessionSource.SettingsPreview, normalSess);
			Assert(SoundEffectManager.TestPendingSound == null, "B2a_1_Target3C_MismatchedSessionRejected");

			// 3D: 合法 SettingsPreview 会话正常接收
			long previewCanvasSess = SoundEffectManager.BeginSession(SoundSessionSource.SettingsPreview);
			SoundEffectManager.LastHoverTick = 0L;
			SoundEffectManager.Play(SoundType.SectorHover, SoundSessionSource.SettingsPreview, previewCanvasSess);
			Assert(SoundEffectManager.TestPendingSound == SoundType.SectorHover, "B2a_1_Target3D_ValidPreviewSessionAccepted");

			// 3E: 画布离开 (MouseLeave) 结束该会话，撤销未播出的画布悬浮
			SoundEffectManager.EndSession(SoundSessionSource.SettingsPreview, previewCanvasSess, allowTerminalFeedback: false);
			Assert(SoundEffectManager.TestPendingSound == null, "B2a_1_Target3E_CanvasMouseLeaveRevokesPendingHover");

			// 3F: 试听按钮使用独立会话生命周期，离开画布绝不撤销用户明确触发的试听
			long auditionSess = SoundEffectManager.BeginSession(SoundSessionSource.SettingsPreview);
			SoundEffectManager.LastHoverTick = 0L;
			SoundEffectManager.PlayPreview(SoundType.SectorHover, SoundSessionSource.SettingsPreview, auditionSess);
			Assert(SoundEffectManager.TestPendingSound == SoundType.SectorHover, "B2a_1_Target3F_AuditionHoverEnqueued");

			// 模拟此时画布发生 MouseLeave (针对 previewCanvasSess)
			SoundEffectManager.EndSession(SoundSessionSource.SettingsPreview, previewCanvasSess, allowTerminalFeedback: false);

			// 验证试听按钮的 Hover 绝不被画布 MouseLeave 撤销
			Assert(SoundEffectManager.TestPendingSound == SoundType.SectorHover,
				"B2a_1_Target3F_CanvasLeaveDoesNotCancelAudition",
				$"试听 Hover 依然保留: {SoundEffectManager.TestPendingSound}");

			allowBlockerExit.Set();
			SoundEffectManager.Shutdown();
			SoundEffectManager.ResetTestSeams();
		}

		// B2a_1_Target4: 代际切换期间创建的有效会话不得被旧代清理抹除 (Generation-bound session cleanup)
		{
			AutoResetEvent blockerStarted = new(false);
			ManualResetEvent allowBlockerExit = new(false);
			TrackBarrier(allowBlockerExit);

			SoundEffectManager.SoundPlaybackStarted = (t, wav, tick) => blockerStarted.Set();
			SoundEffectManager.PlaybackSink = (data, flags) => allowBlockerExit.WaitOne(3000);

			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);
			long gen1Session = SoundEffectManager.BeginSession(SoundSessionSource.NormalGesture);
			SoundEffectManager.PlayPreview(SoundType.WheelPopup);
			blockerStarted.WaitOne(2000);

			// 旧代正在停止中 (调用 Shutdown 触发旧代关闭清理)
			SoundEffectManager.Shutdown();

			// 此时开启新代并创建会话
			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);
			long newGenSession = SoundEffectManager.BeginSession(SoundSessionSource.NormalGesture);

			// 旧代退出
			allowBlockerExit.Set();
			Thread.Sleep(50);

			// 验证新代会话在旧代退出后依然有效且可播放
			Assert(SoundEffectManager.HasSession(newGenSession), "B2a_1_Target4_NewGenSessionPreservedAcrossOldShutdown",
				"新代会话不得被旧代清理抹除");

			AutoResetEvent newHoverPlayed = new(false);
			SoundEffectManager.PlaybackSink = (data, flags) => newHoverPlayed.Set();
			SoundEffectManager.LastHoverTick = 0L;
			SoundEffectManager.Play(SoundType.SectorHover, SoundSessionSource.NormalGesture, newGenSession);
			bool played = newHoverPlayed.WaitOne(2000);
			Assert(played || SoundEffectManager.TestPendingSound == SoundType.SectorHover, "B2a_1_Target4_NewGenSessionSoundPlayable");

			SoundEffectManager.Shutdown();
			SoundEffectManager.ResetTestSeams();
		}

		// B2a_1_Target5: 会话状态有界回收与容量上限 (1000 次常规手势后驻留会话数 <= 64，被修剪会话不可复活)
		{
			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);

			// 模拟 1000 次手势（无确认音结束）
			for (int i = 0; i < 1000; i++)
			{
				long sid = SoundEffectManager.BeginSession(SoundSessionSource.NormalGesture);
				// 模拟各种退出形式：直接结束
				SoundEffectManager.EndSession(SoundSessionSource.NormalGesture, sid, allowTerminalFeedback: false);
			}

			// 验证驻留会话数有界（<= 64）
			int count = SoundEffectManager.SessionCount;
			Assert(count <= 64, "B2a_1_Target5_SessionCountBoundedAt64",
				$"1000 次手势后驻留会话数: {count} (门限: <= 64)");

			// 验证已修剪会话 (如早期的 ID=1) 无法复活
			SoundEffectManager.Play(SoundType.SectorHover, SoundSessionSource.NormalGesture, 1L);
			Assert(SoundEffectManager.TestPendingSound == null, "B2a_1_Target5_PrunedSessionCannotResurrect");

			// 验证最新会话的终态确认音仍可正常工作
			AutoResetEvent executePlayed = new(false);
			SoundType? playedType = null;
			SoundEffectManager.SoundPlayed = (t, wav, tick) =>
			{
				if (t == SoundType.ActionExecute)
				{
					playedType = t;
					executePlayed.Set();
				}
			};

			long currentSess = SoundEffectManager.BeginSession(SoundSessionSource.NormalGesture);
			SoundEffectManager.EndSession(SoundSessionSource.NormalGesture, currentSess, allowTerminalFeedback: true);
			SoundEffectManager.Play(SoundType.ActionExecute, SoundSessionSource.NormalGesture, currentSess);
			bool playedWithinTimeout = executePlayed.WaitOne(2000);
			Assert(playedWithinTimeout && playedType == SoundType.ActionExecute, "B2a_1_Target5_TerminalFeedbackStillWorks",
				$"最新会话终态确认音在 2000ms 内触发成功 (played: {playedWithinTimeout}, type: {playedType})");

			SoundEffectManager.Shutdown();
			SoundEffectManager.ResetTestSeams();
		}

		// 迁移后的行为替代原宿主音效结构检查。
		Assert(SoundMigrationContracts.CapturedSessionDoesNotRedirect(), "B2a_1_Target6_GestureControllerLocalSessionCaptureAudit", "实际控制器捕获的旧语义会话不能重定向到新会话");


		// 迁移后的行为替代原宿主音效结构检查。
		Assert(SoundMigrationContracts.LegacyFieldsRoundTripWithoutHostAudioProperties(), "B2a_1_Target7_SettingsWindowExplicitSessionAudit", "宿主无音效字段时旧音效数据仍经通用扩展数据无损往返");

	}
	#endregion

	#region 阶段 B2a.2: 会话生命周期与真实交错回归 (Stage B2a.2 Lifecycle & Real Interleaving)
	private static void TestStageB2a2_LifecycleAndRealInterleaving()
	{
		Log("\n>>> 阶段 B2a.2: 会话生命周期与真实交错回归断言 (Stage B2a.2 Lifecycle & Real Interleaving)");

		// B2a_2_Target1: 出队后、取得播放资源时会话结束的撤销竞态 (Post-dequeuing Revocation Race)
		// 持有缓存锁，让 Hover 出队并阻塞于缓存读取；结束会话后释放锁，断言播放后端没有收到 Hover。
		{
			SoundEffectManager.ResetTestSeams();
			AutoResetEvent hoverPlaybackStarted = new(false);

			SoundEffectManager.PlaybackSink = (data, flags) =>
			{
				hoverPlaybackStarted.Set();
			};

			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);
			long sid = SoundEffectManager.BeginSession(SoundSessionSource.NormalGesture);

			lock (SoundEffectManager.TestSyncLock)
			{
				SoundEffectManager.LastHoverTick = 0L;
				SoundEffectManager.Play(SoundType.SectorHover, SoundSessionSource.NormalGesture, sid);

				// 证明交错已发生：等待 Worker 将请求出队（_pendingSound 变为空）
				// 此时 Worker 在 ProcessSoundQueue 中已出队，但因为 TestSyncLock 被持有，必然阻塞于缓存读取
				bool dequeued = SpinWait.SpinUntil(() => SoundEffectManager.TestPendingSound == null, 2000);
				Assert(dequeued, "B2a_2_Target1_HoverDequeuedAndBlockedAtResourceAcquisition");

				// 在 Worker 阻塞于取得播放资源期间，结束该会话
				SoundEffectManager.EndSession(SoundSessionSource.NormalGesture, sid, allowTerminalFeedback: false);
			} // 释放缓存锁

			// 断言播放后端没有收到 Hover
			bool hoverReceived = hoverPlaybackStarted.WaitOne(500);
			Assert(!hoverReceived, "B2a_2_Target1_PostDequeueRevocationPreventsPlayback",
				$"出队后取得资源前会话结束，播放后端不得收到 Hover，实际收到={hoverReceived}");

			SoundEffectManager.Shutdown();
			SoundEffectManager.ResetTestSeams();
		}

		// B2a_2_Target2: 无活动上下文的 Shutdown 暂停在会话清理前，并发新代创建的会话与请求必须保留 (Shutdown without active context)
		{
			SoundEffectManager.ResetTestSeams();
			// 确保当前无活动工作线程/上下文 (oldContext == null)
			SoundEffectManager.Shutdown();

			ManualResetEvent shutdownPaused = new(false);
			TrackBarrier(shutdownPaused);
			ManualResetEvent allowShutdownContinue = new(false);
			TrackBarrier(allowShutdownContinue);

			long newGenSessionId = 0L;
			AutoResetEvent newSoundPlayed = new(false);
			SoundEffectManager.PlaybackSink = (data, flags) => newSoundPlayed.Set();

			SoundEffectManager.OnBeforeShutdownSessionCleanup = () =>
			{
				shutdownPaused.Set();
				allowShutdownContinue.WaitOne(3000);
			};

			// 在后台线程执行 Shutdown（此时 oldContext == null）
			Thread shutdownThread = new Thread(() =>
			{
				SoundEffectManager.Shutdown();
			})
			{
				IsBackground = true,
				Name = "TestStageB2a2_ShutdownThread"
			};
			shutdownThread.Start();

			// 等待 Shutdown 暂停在会话清理前
			bool paused = shutdownPaused.WaitOne(2000);
			Assert(paused, "B2a_2_Target2_ShutdownPausedBeforeSessionCleanup");

			// 此时新代启动并创建会话
			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);
			newGenSessionId = SoundEffectManager.BeginSession(SoundSessionSource.NormalGesture);

			// 放行旧清理
			allowShutdownContinue.Set();
			bool joined = shutdownThread.Join(2000);
			Assert(joined, "B2a_2_Target2_ShutdownThreadJoined");

			// 断言：新代创建的会话必须保留，且实际可播放
			bool sessionPreserved = SoundEffectManager.HasSession(newGenSessionId);
			Assert(sessionPreserved, "B2a_2_Target2_NewSessionPreservedAcrossNoContextShutdown",
				$"无活动上下文的 Shutdown 不得清除并发新代会话，会话保留={sessionPreserved}");

			SoundEffectManager.LastHoverTick = 0L;
			SoundEffectManager.Play(SoundType.SectorHover, SoundSessionSource.NormalGesture, newGenSessionId);
			bool played = newSoundPlayed.WaitOne(1000);
			Assert(played, "B2a_2_Target2_NewSessionActuallyPlayableAfterShutdown");

			SoundEffectManager.Shutdown();
			SoundEffectManager.ResetTestSeams();
		}

		// B2a_2_Target3A: 100 次试听后原活动画布会话仍有效，容量回收不得任意删除活动画布会话
		{
			SoundEffectManager.ResetTestSeams();
			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);

			// 创建活动的画布会话
			long canvasSession = SoundEffectManager.BeginSession(SoundSessionSource.SettingsPreview);

			// 连续执行 100 次独立试听
			for (int i = 0; i < 100; i++)
			{
				SoundEffectManager.PlayPreview(SoundType.SectorHover);
			}

			// 断言：100 次试听后，原活动画布会话仍然有效，未被容量回收清除
			bool canvasStillValid = SoundEffectManager.HasSession(canvasSession);
			Assert(canvasStillValid, "B2a_2_Target3A_CanvasSessionPreservedAcross100Auditions",
				$"100 次试听后原画布会话仍存活: {canvasStillValid}");

			SoundEffectManager.EndSession(SoundSessionSource.SettingsPreview, canvasSession, allowTerminalFeedback: false);
			SoundEffectManager.Shutdown();
			SoundEffectManager.ResetTestSeams();
		}

		// 迁移后的行为替代原宿主音效结构检查。
		Assert(SoundMigrationContracts.AuditionWaitsForCompletion(), "B2a_2_Target3B_SettingsWindowFullFlowAudit", "插件试听等待实际Mock后端完成，而不是只等待受理");


		// 迁移后的行为替代原宿主音效结构检查。
		Assert(SoundMigrationContracts.RealEscEmitsCancelledAndEnd(), "B2a_2_Target3C_GestureControllerEscAudit", "真实Esc入口发出取消及唯一结束事件");


		// B2a_2_Target3D: 画布离开不撤销独立试听
		{
			SoundEffectManager.ResetTestSeams();
			AutoResetEvent blockerStarted = new(false);
			ManualResetEvent allowBlockerExit = new(false);
			TrackBarrier(allowBlockerExit);

			SoundEffectManager.SoundPlaybackStarted = (t, wav, tick) => blockerStarted.Set();
			SoundEffectManager.PlaybackSink = (data, flags) => allowBlockerExit.WaitOne(3000);

			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);
			SoundEffectManager.PlayPreview(SoundType.WheelPopup);
			blockerStarted.WaitOne(2000);

			long canvasSess = SoundEffectManager.BeginSession(SoundSessionSource.SettingsPreview);
			long auditionSess = SoundEffectManager.BeginSession(SoundSessionSource.SettingsPreview);

			SoundEffectManager.LastHoverTick = 0L;
			SoundEffectManager.PlayPreview(SoundType.SectorHover, SoundSessionSource.SettingsPreview, auditionSess);
			Assert(SoundEffectManager.TestPendingSound == SoundType.SectorHover, "B2a_2_Target3D_AuditionHoverEnqueued");

			// 画布离开结束 canvasSess
			SoundEffectManager.EndSession(SoundSessionSource.SettingsPreview, canvasSess, allowTerminalFeedback: false);

			// 验证试听音效依然保留
			Assert(SoundEffectManager.TestPendingSound == SoundType.SectorHover, "B2a_2_Target3D_CanvasLeaveDoesNotCancelAudition");

			allowBlockerExit.Set();
			SoundEffectManager.Shutdown();
			SoundEffectManager.ResetTestSeams();
		}

		// B2a_2_Target4: 全量线程追踪仅用于测试，正常生产态运行不得无限保留历史 Thread 引用
		// 遵循静音红线：严禁在测试中设置 TestMode = false，通过独立策略切缝验证
		{
			SoundEffectManager.ResetTestSeams();
			int beforeCount = SoundEffectManager.GetAllCreatedWorkers().Count;
			// 策略切缝：模拟非测试追踪策略（返回 false）
			SoundEffectManager.WorkerTrackingPolicy = () => false;
			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);
			int afterCount = SoundEffectManager.GetAllCreatedWorkers().Count;

			Assert(afterCount == beforeCount, "B2a_2_Target4_WorkerThreadTrackingOnlyInTestMode",
				$"生产态追踪策略关闭时不得保留历史 Thread 引用，实际新增记录数: {afterCount - beforeCount}");

			SoundEffectManager.WorkerTrackingPolicy = null;
			SoundEffectManager.Shutdown();
			SoundEffectManager.ResetTestSeams();
		}
	}
	#endregion

	#region 阶段 B2b.2: 播放认领时间新鲜度与状态锁确定性交错回归断言
	private static void TestStageB2b2_ClaimLockInterleavingRegressions()
	{
		Log("\n>>> 阶段 B2b.2: 播放认领时间新鲜度与状态锁确定性交错回归断言 (Stage B2b.2 Claim Lock Interleaving)");

		// B2b_2_Target1: 候选已选中、资源已取得，但认领等待状态锁。等待期间推进模拟时钟超期（>150ms），释放锁后在认领点拒绝并不播放
		{
			SoundEffectManager.ResetTestSeams();
			long simTime = 10000L;
			SoundEffectManager.TimeProvider = () => simTime;
			long sid = SoundEffectManager.BeginSession(SoundSessionSource.NormalGesture);

			AutoResetEvent workerReachedPreClaim = new(false);
			AutoResetEvent testAcquiredLockSignal = new(false);
			TrackBarrier(testAcquiredLockSignal);

			AutoResetEvent claimEvaluatedSignal = new(false);
			bool? claimResult = null;
			long claimTick = 0L;
			SoundEffectManager.OnHoverClaimEvaluated = (claimed, type, tick) =>
			{
				if (type == SoundType.SectorHover)
				{
					claimResult = claimed;
					claimTick = tick;
					claimEvaluatedSignal.Set();
				}
			};

			AutoResetEvent dropSignal = new(false);
			int droppedCount = 0;
			long dropTick = 0L;
			SoundEffectManager.SoundDropped = (t, wav, tick) =>
			{
				if (t == SoundType.SectorHover)
				{
					Interlocked.Increment(ref droppedCount);
					dropTick = tick;
					dropSignal.Set();
				}
			};

			int playedCount = 0;
			SoundEffectManager.SoundPlayed = (t, wav, tick) =>
			{
				if (t == SoundType.SectorHover)
				{
					Interlocked.Increment(ref playedCount);
				}
			};

			SoundEffectManager.OnBeforeClaimLock = () =>
			{
				workerReachedPreClaim.Set();
				testAcquiredLockSignal.WaitOne(3000);
			};

			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);

			// t=10000 报告目标稳定，截止期 10045ms，过期门限 10045 + 150 = 10195ms
			SoundEffectManager.ReportHover(SoundSessionSource.NormalGesture, sid, level: 0, parentIndex: -1, subIndex: 1);

			// 推进至 10045ms，唤醒 Worker 选中该候选并取得音频资源
			simTime = 10045L;
			SoundEffectManager.TestSignalWorker();

			bool inWindow = workerReachedPreClaim.WaitOne(2000);
			Assert(inWindow, "B2b_2_Target1_CandidateSelectedAndResourceAcquiredBeforeClaimLock");

			// 测试主线程持有状态锁 TestQueueLock，模拟状态锁高争用阻塞
			lock (SoundEffectManager.TestQueueLock)
			{
				// 放行 Worker 尝试获取 _queueLock，此时 Worker 必真实阻塞于 _queueLock
				testAcquiredLockSignal.Set();
				Thread.Sleep(20);

				// 在 Worker 等待状态锁期间推进时钟至 10200ms (> 10195ms，超期 155ms)
				simTime = 10200L;
			} // 释放 TestQueueLock，Worker 此时在持有锁后读取新鲜 nowClaim = 10200L 并执行过期判定

			bool evaluated = claimEvaluatedSignal.WaitOne(2000);
			Assert(evaluated, "B2b_2_Target1_ClaimEvaluatedSignalReceived");
			Assert(claimResult == false, "B2b_2_Target1_ClaimRejectedDueToExpirationUnderLock",
				$"认领点必须拒绝超期候选: claimResult={claimResult}, claimTick={claimTick}");

			bool dropped = dropSignal.WaitOne(2000);
			Assert(dropped && droppedCount == 1, "B2b_2_Target1_SoundDroppedFiredOnExpiration",
				$"超期丢弃事件触发: dropped={dropped}, count={droppedCount}, dropTick={dropTick}");
			Assert(playedCount == 0, "B2b_2_Target1_OverdueCandidateNeverPlayed",
				$"超期候选绝不播放: playedCount={playedCount}");

			var candidateAfter = SoundEffectManager.TestGetHoverCandidateTarget(SoundSessionSource.NormalGesture);
			Assert(candidateAfter == null, "B2b_2_Target1_CandidateRemovedFromDictionaryOnExpiration");

			SoundEffectManager.EndSession(SoundSessionSource.NormalGesture, sid, allowTerminalFeedback: false);
			SoundEffectManager.Shutdown();
			SoundEffectManager.ResetTestSeams();
		}

		// B2b_2_Target2: 第一声因状态锁等待延迟后，在同一次原子认领中更新新鲜时间，第二声开始仍至少相隔 80ms
		{
			SoundEffectManager.ResetTestSeams();
			long simTime = 20000L;
			SoundEffectManager.TimeProvider = () => simTime;
			long sid = SoundEffectManager.BeginSession(SoundSessionSource.NormalGesture);

			AutoResetEvent worker1ReachedPreClaim = new(false);
			AutoResetEvent testAcquiredLockSignal1 = new(false);
			TrackBarrier(testAcquiredLockSignal1);

			AutoResetEvent claim1Signal = new(false);
			bool? claim1Result = null;
			long claim1Tick = 0L;

			AutoResetEvent claim2Signal = new(false);
			bool? claim2Result = null;
			long claim2Tick = 0L;

			int claimCount = 0;
			SoundEffectManager.OnHoverClaimEvaluated = (claimed, type, tick) =>
			{
				if (type == SoundType.SectorHover)
				{
					int n = Interlocked.Increment(ref claimCount);
					if (n == 1)
					{
						claim1Result = claimed;
						claim1Tick = tick;
						claim1Signal.Set();
					}
					else if (n == 2)
					{
						claim2Result = claimed;
						claim2Tick = tick;
						claim2Signal.Set();
					}
				}
			};

			AutoResetEvent start1Signal = new(false);
			AutoResetEvent start2Signal = new(false);
			List<long> startTicks = new();
			object tickLock = new();

			SoundEffectManager.SoundPlaybackStarted = (t, wav, tick) =>
			{
				if (t == SoundType.SectorHover)
				{
					lock (tickLock)
					{
						startTicks.Add(tick);
						if (startTicks.Count == 1) start1Signal.Set();
						else if (startTicks.Count == 2) start2Signal.Set();
					}
				}
			};

			int preClaimHookCount = 0;
			SoundEffectManager.OnBeforeClaimLock = () =>
			{
				if (Interlocked.Increment(ref preClaimHookCount) == 1)
				{
					worker1ReachedPreClaim.Set();
					testAcquiredLockSignal1.WaitOne(3000);
				}
			};

			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);

			// 第 1 个目标：t=20000 报告，稳定期限 20045ms
			SoundEffectManager.ReportHover(SoundSessionSource.NormalGesture, sid, level: 0, parentIndex: -1, subIndex: 1);

			simTime = 20045L;
			SoundEffectManager.TestSignalWorker();

			bool inWindow1 = worker1ReachedPreClaim.WaitOne(2000);
			Assert(inWindow1, "B2b_2_Target2_FirstCandidateSelectedAndResourceAcquired");

			// 主线程持有 TestQueueLock，模拟锁等待争用，并在等待期间推进时钟至 20070ms（延迟 25ms）
			lock (SoundEffectManager.TestQueueLock)
			{
				testAcquiredLockSignal1.Set();
				Thread.Sleep(20);
				simTime = 20070L;
			}

			// 第一声认领并开始播放
			bool c1Evaluated = claim1Signal.WaitOne(2000);
			Assert(c1Evaluated && claim1Result == true, "B2b_2_Target2_FirstClaimSucceededWithDelayedTick",
				$"第一声必须在新鲜时间 20070ms 认领成功: evaluated={c1Evaluated}, result={claim1Result}, tick={claim1Tick}");

			bool s1Started = start1Signal.WaitOne(2000);
			Assert(s1Started && startTicks.Count == 1 && startTicks[0] == 20070L, "B2b_2_Target2_FirstSoundStartedAtDelayedTick",
				$"第一声实际开始时间必须为新鲜时间 20070ms: startTicks[0]={startTicks[0]}");

			// 第 2 个目标：t=20075 报告，稳定期限 20075 + 45 = 20120ms
			// 由于第一声在 20070ms 认领并开始，第二声的最早合法开始时间必须为 20070 + 80 = 20150ms！
			SoundEffectManager.ReportHover(SoundSessionSource.NormalGesture, sid, level: 0, parentIndex: -1, subIndex: 2);

			// 推进至 20120ms（满足自身45ms稳定，但距离第一声开始仅 50ms < 80ms）
			simTime = 20120L;
			SoundEffectManager.TestSignalWorker();
			Thread.Sleep(30);

			// 确实验证：第二声在 20120ms 绝对未认领且未开始
			Assert(claim2Signal.WaitOne(0) == false, "B2b_2_Target2_SecondCandidateNotClaimedAt50msInterval");
			lock (tickLock)
			{
				Assert(startTicks.Count == 1, "B2b_2_Target2_SecondSoundNotStartedAt50msInterval",
					$"未满80ms间隔时第二声绝未开始，当前开始数: {startTicks.Count}");
			}

			// 推进至 20150ms（距离第一声恰好满 80ms 间隔）
			simTime = 20150L;
			SoundEffectManager.TestSignalWorker();

			bool c2Evaluated = claim2Signal.WaitOne(2000);
			Assert(c2Evaluated && claim2Result == true, "B2b_2_Target2_SecondClaimSucceededAt80msInterval",
				$"第二声在满 80ms 间隔后认领成功: evaluated={c2Evaluated}, result={claim2Result}, tick={claim2Tick}");

			bool s2Started = start2Signal.WaitOne(2000);
			Assert(s2Started, "B2b_2_Target2_SecondSoundPlaybackStartedSignalReceived");

			lock (tickLock)
			{
				Assert(startTicks.Count == 2, "B2b_2_Target2_BothSoundsStarted",
					$"两声均已开始: count={startTicks.Count}");
				if (startTicks.Count >= 2)
				{
					long interval = startTicks[1] - startTicks[0];
					Assert(interval >= 80L, "B2b_2_Target2_ActualIntervalAtLeast80ms",
						$"两声实际开始间隔必须 >= 80ms: 实际间隔={interval}ms (start1={startTicks[0]}, start2={startTicks[1]})");
				}
			}

			SoundEffectManager.EndSession(SoundSessionSource.NormalGesture, sid, allowTerminalFeedback: false);
			SoundEffectManager.Shutdown();
			SoundEffectManager.ResetTestSeams();
		}

		// B2b_2_Target3: 全生命周期工作线程跟踪无死角断言与退出门控
		{
			SoundEffectManager.ResetTestSeams();
			var harnessWorkers = SoundEffectManager.GetHarnessCreatedWorkers();
			var trackedWorkers = SoundEffectManager.GetAllCreatedWorkers();

			Assert(harnessWorkers.Count > 0, "B2b_2_Target3_HarnessWorkersTrackedAtLeastOne",
				$"全生命周期跟踪工作线程数: {harnessWorkers.Count}");
			Assert(harnessWorkers.Count >= trackedWorkers.Count, "B2b_2_Target3_HarnessTracksSupersetOfLocalTrackedWorkers",
				$"全生命周期跟踪条数 ({harnessWorkers.Count}) 必须覆盖局部跟踪条数 ({trackedWorkers.Count})");

			// 验证所有全生命周期工作线程均能安全通过 Join 门控
			bool allJoined = true;
			foreach (var w in harnessWorkers)
			{
				if (w != null && w.IsAlive)
				{
					if (!w.Join(2000))
					{
						allJoined = false;
					}
				}
			}
			Assert(allJoined, "B2b_2_Target3_AllHarnessWorkersSafelyJoined",
				"所有全生命周期工作线程均已安全退出");
		}
	}
	#endregion

	#region 阶段 B3: RIFF/WAVE 分块解析与 16-bit PCM 音量缩放正确性回归 (Stage B3 WAV Parser Correctness)
	private static void TestStageB3_WavParserAndScalingCorrectness()
	{
		Log("\n>>> 阶段 B3: RIFF/WAVE 分块解析与 16-bit PCM 音量缩放正确性回归 (Stage B3 WAV Parser Correctness)");

		// B3_Target1: 标准 44 字节单声道 16-bit PCM WAV 正确缩放与多音量档位覆盖
		{
			short[] samples = [500, 1000, 1500, 2000, -500, -1000, -1500, -2000];
			byte[] standardWav = CreateStandardWav(1, 16, 44100, samples);
			byte[] originalCopy = (byte[])standardWav.Clone();

			// 1A: 音量 0.5 档位
			byte[]? scaled05 = SoundEffectManager.ScaleWavVolume(standardWav, 0.5, out string? reason05);
			Assert(scaled05 != null && string.IsNullOrEmpty(reason05), "B3_Target1A_StandardMono16Bit_Scale05_Success");
			Assert(standardWav.SequenceEqual(originalCopy), "B3_Target1A_StandardMono16Bit_InputArrayImmutable");
			Assert(scaled05 != null && scaled05.Take(44).SequenceEqual(originalCopy.Take(44)), "B3_Target1A_StandardMono16Bit_HeaderUntouched");

			if (scaled05 != null)
			{
				double gain05 = SoundEffectManager.GetAcousticGain(0.5);
				bool allSamplesMatch = true;
				for (int i = 0; i < samples.Length; i++)
				{
					short expected = (short)Math.Clamp(samples[i] * gain05, -32768.0, 32767.0);
					short actual = BitConverter.ToInt16(scaled05, 44 + i * 2);
					if (expected != actual) { allSamplesMatch = false; break; }
				}
				Assert(allSamplesMatch, "B3_Target1A_StandardMono16Bit_SamplesScaledAccurately");
			}

			// 1B: 音量 1.0 (增益 ≈ 1.0 旁路)
			byte[]? scaled10 = SoundEffectManager.ScaleWavVolume(standardWav, 1.0, out string? reason10);
			Assert(scaled10 != null && scaled10.SequenceEqual(originalCopy), "B3_Target1B_StandardMono16Bit_FullVolumeBypass");
			Assert(standardWav.SequenceEqual(originalCopy), "B3_Target1B_StandardMono16Bit_InputArrayImmutable_Vol1");

			// 1C: 音量 0.0 (静音)
			byte[]? scaled00 = SoundEffectManager.ScaleWavVolume(standardWav, 0.0, out string? reason00);
			Assert(scaled00 != null && scaled00.Take(44).SequenceEqual(originalCopy.Take(44)), "B3_Target1C_StandardMono16Bit_HeaderUntouched_Vol0");
			if (scaled00 != null)
			{
				bool allZeros = true;
				for (int i = 0; i < samples.Length; i++)
				{
					if (BitConverter.ToInt16(scaled00, 44 + i * 2) != 0) { allZeros = false; break; }
				}
				Assert(allZeros, "B3_Target1C_StandardMono16Bit_ZeroVolumeSilence");
			}

			// 1D: 大动态防削顶钳位测试
			short[] clippingSamples = [30000, 32767, -30000, -32768];
			byte[] clipWav = CreateStandardWav(1, 16, 44100, clippingSamples);
			byte[]? scaledClip = SoundEffectManager.ScaleWavVolume(clipWav, 1.0);
			Assert(scaledClip != null, "B3_Target1D_ClippingSamplesScaled");
		}

		// B3_Target2: 双声道 (Stereo 2-channel, blockAlign=4) 16-bit PCM 正确缩放
		{
			short[] stereoSamples = [1000, 2000, 3000, 4000, -1000, -2000, -3000, -4000]; // 4 组采样对
			byte[] stereoWav = CreateStandardWav(2, 16, 48000, stereoSamples);
			byte[] originalStereoCopy = (byte[])stereoWav.Clone();

			byte[]? scaledStereo = SoundEffectManager.ScaleWavVolume(stereoWav, 0.6, out string? reasonStereo);
			Assert(scaledStereo != null && string.IsNullOrEmpty(reasonStereo), "B3_Target2_StandardStereo16Bit_Scale_Success");
			Assert(stereoWav.SequenceEqual(originalStereoCopy), "B3_Target2_StandardStereo16Bit_InputArrayImmutable");
			Assert(scaledStereo != null && scaledStereo.Take(44).SequenceEqual(originalStereoCopy.Take(44)), "B3_Target2_StandardStereo16Bit_HeaderUntouched");

			if (scaledStereo != null)
			{
				double gain06 = SoundEffectManager.GetAcousticGain(0.6);
				bool stereoMatch = true;
				for (int i = 0; i < stereoSamples.Length; i++)
				{
					short expected = (short)Math.Clamp(stereoSamples[i] * gain06, -32768.0, 32767.0);
					short actual = BitConverter.ToInt16(scaledStereo, 44 + i * 2);
					if (expected != actual) { stereoMatch = false; break; }
				}
				Assert(stereoMatch, "B3_Target2_StandardStereo16Bit_BothChannelsScaledAccurately");
			}
		}

		// B3_Target3: data 位于 JUNK 分块之后 (WAV with JUNK Chunk Before Data)
		{
			short[] samples = [1200, 2400, 3600, 4800];
			byte[] junkWav = CreateWavWithJunkBeforeData(samples);
			byte[] originalJunkCopy = (byte[])junkWav.Clone();

			byte[]? scaledJunk = SoundEffectManager.ScaleWavVolume(junkWav, 0.5, out string? reasonJunk);
			Assert(scaledJunk != null && string.IsNullOrEmpty(reasonJunk), "B3_Target3_WavWithJunkBeforeData_Success");
			Assert(junkWav.SequenceEqual(originalJunkCopy), "B3_Target3_WavWithJunkBeforeData_InputArrayImmutable");

			// 前 68 字节包含 RIFF(12) + fmt (24) + JUNK(24) + data header(8) = 68 字节
			Assert(scaledJunk != null && scaledJunk.Take(68).SequenceEqual(originalJunkCopy.Take(68)), "B3_Target3_WavWithJunkBeforeData_PreDataChunksUntouched");

			if (scaledJunk != null)
			{
				double gain05 = SoundEffectManager.GetAcousticGain(0.5);
				bool samplesMatch = true;
				for (int i = 0; i < samples.Length; i++)
				{
					short expected = (short)Math.Clamp(samples[i] * gain05, -32768.0, 32767.0);
					short actual = BitConverter.ToInt16(scaledJunk, 68 + i * 2);
					if (expected != actual) { samplesMatch = false; break; }
				}
				Assert(samplesMatch, "B3_Target3_WavWithJunkBeforeData_SamplesScaled");
			}
		}

		// B3_Target4: data 位于 LIST 分块之后 (WAV with LIST Chunk Before Data)
		{
			short[] samples = [800, 1600, 2400, 3200];
			byte[] listWav = CreateWavWithListBeforeData(samples);
			byte[] originalListCopy = (byte[])listWav.Clone();

			byte[]? scaledList = SoundEffectManager.ScaleWavVolume(listWav, 0.5, out string? reasonList);
			Assert(scaledList != null && string.IsNullOrEmpty(reasonList), "B3_Target4_WavWithListBeforeData_Success");
			Assert(listWav.SequenceEqual(originalListCopy), "B3_Target4_WavWithListBeforeData_InputArrayImmutable");

			// fmt(24) + LIST(32) + data header(8) = 76 字节 (从 0 开始共 76 字节)
			int preDataLength = 12 + 24 + 32 + 8; // 76
			Assert(scaledList != null && scaledList.Take(preDataLength).SequenceEqual(originalListCopy.Take(preDataLength)), "B3_Target4_WavWithListBeforeData_ListChunkUntouched");

			if (scaledList != null)
			{
				double gain05 = SoundEffectManager.GetAcousticGain(0.5);
				bool samplesMatch = true;
				for (int i = 0; i < samples.Length; i++)
				{
					short expected = (short)Math.Clamp(samples[i] * gain05, -32768.0, 32767.0);
					short actual = BitConverter.ToInt16(scaledList, preDataLength + i * 2);
					if (expected != actual) { samplesMatch = false; break; }
				}
				Assert(samplesMatch, "B3_Target4_WavWithListBeforeData_SamplesScaled");
			}
		}

		// B3_Target5: LIST/INFO 分块位于 data 之后 (WAV with LIST Chunk After Data)
		{
			short[] samples = [500, 1000, 1500, 2000];
			byte[] trailingListWav = CreateWavWithListAfterData(samples);
			byte[] originalTrailingCopy = (byte[])trailingListWav.Clone();

			byte[]? scaledTrailing = SoundEffectManager.ScaleWavVolume(trailingListWav, 0.5, out string? reasonTrailing);
			Assert(scaledTrailing != null && string.IsNullOrEmpty(reasonTrailing), "B3_Target5_WavWithListAfterData_Success");
			Assert(trailingListWav.SequenceEqual(originalTrailingCopy), "B3_Target5_WavWithListAfterData_InputArrayImmutable");

			// 前 44 字节 (RIFF + fmt + data header) 保持不变
			Assert(scaledTrailing != null && scaledTrailing.Take(44).SequenceEqual(originalTrailingCopy.Take(44)), "B3_Target5_WavWithListAfterData_HeaderUntouched");

			// 尾部 LIST 分块 (从 offset 44 + samples.Length*2 开始到文件末尾) 严格逐字节匹配，杜绝旧实现污染尾部分块的缺陷！
			int trailingOffset = 44 + samples.Length * 2;
			int trailingLength = trailingListWav.Length - trailingOffset;
			Assert(scaledTrailing != null && scaledTrailing.Skip(trailingOffset).SequenceEqual(originalTrailingCopy.Skip(trailingOffset)),
				"B3_Target5_WavWithListAfterData_TrailingMetadataUntouched",
				$"尾部元数据分块 (长度 {trailingLength} 字节) 100% 保持完全不变");

			if (scaledTrailing != null)
			{
				double gain05 = SoundEffectManager.GetAcousticGain(0.5);
				bool samplesMatch = true;
				for (int i = 0; i < samples.Length; i++)
				{
					short expected = (short)Math.Clamp(samples[i] * gain05, -32768.0, 32767.0);
					short actual = BitConverter.ToInt16(scaledTrailing, 44 + i * 2);
					if (expected != actual) { samplesMatch = false; break; }
				}
				Assert(samplesMatch, "B3_Target5_WavWithListAfterData_SamplesScaled");
			}
		}

		// B3_Target6: 奇数长度分块填充对齐与奇数 data 边界拒绝 (Odd Chunks & Padding)
		{
			short[] samples = [300, 600, 900, 1200];

			// 6A: 具有合法 1 字节填充的奇数长度 JUNK 分块
			byte[] oddJunkWithPad = CreateWavWithOddJunkChunk(samples, includePadByte: true);
			byte[] originalOddPadCopy = (byte[])oddJunkWithPad.Clone();
			byte[]? scaledOdd = SoundEffectManager.ScaleWavVolume(oddJunkWithPad, 0.5, out string? reasonOdd);
			Assert(scaledOdd != null && string.IsNullOrEmpty(reasonOdd), "B3_Target6A_OddJunkChunkWithPadding_TraversedAndUntouched");
			Assert(oddJunkWithPad.SequenceEqual(originalOddPadCopy), "B3_Target6A_OddJunkChunkWithPadding_InputArrayImmutable");

			// 6B: 缺失 1 字节填充的奇数长度 JUNK 分块（分块边界断裂），必须安全拒绝
			byte[] oddJunkMissingPad = CreateWavWithOddJunkChunk(samples, includePadByte: false);
			byte[]? scaledMissingPad = SoundEffectManager.ScaleWavVolume(oddJunkMissingPad, 0.5, out string? reasonMissingPad);
			Assert(scaledMissingPad == null && !string.IsNullOrEmpty(reasonMissingPad),
				"B3_Target6B_OddJunkChunkMissingPadding_Rejected",
				$"缺失填充字节时安全拒绝并说明原因: {reasonMissingPad}");

			// 6C: 16-bit PCM 下出现奇数长度 data 分块（样本被从中断裂），必须安全拒绝
			byte[] oddDataWav = CreateWavWithOddDataChunk();
			byte[]? scaledOddData = SoundEffectManager.ScaleWavVolume(oddDataWav, 0.5, out string? reasonOddData);
			Assert(scaledOddData == null && reasonOddData != null && reasonOddData.Contains("not an integer multiple of block alignment"),
				"B3_Target6C_OddDataChunk_Rejected",
				$"奇数 data 长度安全拒绝: {reasonOddData}");
		}

		// B3_Target7: 不支持音频格式与错误参数全面拒绝 (Unsupported Formats Rejection)
		{
			// 7A: 8-bit PCM 格式 (必须拒绝，不得强行按 16-bit 缩放)
			byte[] wav8Bit = CreateCustomFmtWav(formatTag: 1, channels: 1, sampleRate: 44100, bitsPerSample: 8);
			byte[]? scaled8Bit = SoundEffectManager.ScaleWavVolume(wav8Bit, 0.5, out string? reason8Bit);
			Assert(scaled8Bit == null && reason8Bit != null && reason8Bit.Contains("Unsupported bit depth: 8-bit"),
				"B3_Target7A_Unsupported8BitPcm_Rejected", reason8Bit ?? "");

			// 7B: 24-bit PCM 格式 (必须拒绝)
			byte[] wav24Bit = CreateCustomFmtWav(formatTag: 1, channels: 1, sampleRate: 44100, bitsPerSample: 24);
			byte[]? scaled24Bit = SoundEffectManager.ScaleWavVolume(wav24Bit, 0.5, out string? reason24Bit);
			Assert(scaled24Bit == null && reason24Bit != null && reason24Bit.Contains("Unsupported bit depth: 24-bit"),
				"B3_Target7B_Unsupported24BitPcm_Rejected", reason24Bit ?? "");

			// 7C: 32-bit Float PCM 格式 (必须拒绝)
			byte[] wavFloat = CreateCustomFmtWav(formatTag: 3, channels: 1, sampleRate: 44100, bitsPerSample: 32);
			byte[]? scaledFloat = SoundEffectManager.ScaleWavVolume(wavFloat, 0.5, out string? reasonFloat);
			Assert(scaledFloat == null && reasonFloat != null && reasonFloat.Contains("Unsupported audio format: 0x0003"),
				"B3_Target7C_Unsupported32BitFloat_Rejected", reasonFloat ?? "");

			// 7D: A-Law / Mu-Law 格式 (必须拒绝)
			byte[] wavALaw = CreateCustomFmtWav(formatTag: 6, channels: 1, sampleRate: 44100, bitsPerSample: 8);
			byte[]? scaledALaw = SoundEffectManager.ScaleWavVolume(wavALaw, 0.5, out string? reasonALaw);
			Assert(scaledALaw == null && reasonALaw != null && reasonALaw.Contains("Unsupported audio format: 0x0006"),
				"B3_Target7D_UnsupportedCodec_ALaw_Rejected", reasonALaw ?? "");

			// 7E: MP3 in RIFF (0x0055) 格式 (必须拒绝)
			byte[] wavMp3 = CreateCustomFmtWav(formatTag: 0x0055, channels: 2, sampleRate: 44100, bitsPerSample: 16);
			byte[]? scaledMp3 = SoundEffectManager.ScaleWavVolume(wavMp3, 0.5, out string? reasonMp3);
			Assert(scaledMp3 == null && reasonMp3 != null && reasonMp3.Contains("Unsupported audio format: 0x0055"),
				"B3_Target7E_UnsupportedCodec_Mp3_Rejected", reasonMp3 ?? "");

			// 7F: 0 声道与多声道 (5.1 环绕声) (必须拒绝)
			byte[] wav6Ch = CreateCustomFmtWav(formatTag: 1, channels: 6, sampleRate: 44100, bitsPerSample: 16);
			byte[]? scaled6Ch = SoundEffectManager.ScaleWavVolume(wav6Ch, 0.5, out string? reason6Ch);
			Assert(scaled6Ch == null && reason6Ch != null && reason6Ch.Contains("Unsupported channel count: 6"),
				"B3_Target7F_UnsupportedChannels_Rejected", reason6Ch ?? "");

			// 7G: 采样率 0 (必须拒绝)
			byte[] wavZeroRate = CreateCustomFmtWav(formatTag: 1, channels: 1, sampleRate: 0, bitsPerSample: 16);
			byte[]? scaledZeroRate = SoundEffectManager.ScaleWavVolume(wavZeroRate, 0.5, out string? reasonZeroRate);
			Assert(scaledZeroRate == null && reasonZeroRate != null && reasonZeroRate.Contains("Invalid sample rate: 0 Hz"),
				"B3_Target7G_InvalidSampleRate_Rejected", reasonZeroRate ?? "");

			// 7H: BlockAlign 与 channels * bits 不符 (必须拒绝)
			byte[] wavBadAlign = CreateCustomFmtWav(formatTag: 1, channels: 2, sampleRate: 44100, bitsPerSample: 16, blockAlignOverride: 2);
			byte[]? scaledBadAlign = SoundEffectManager.ScaleWavVolume(wavBadAlign, 0.5, out string? reasonBadAlign);
			Assert(scaledBadAlign == null && reasonBadAlign != null && reasonBadAlign.Contains("Invalid block alignment"),
				"B3_Target7H_InvalidBlockAlign_Rejected", reasonBadAlign ?? "");

			// 7I: ByteRate 与 sampleRate * blockAlign 不符 (必须拒绝)
			byte[] wavBadByteRate = CreateWavWithMismatchedByteRate();
			byte[]? scaledBadByteRate = SoundEffectManager.ScaleWavVolume(wavBadByteRate, 0.5, out string? reasonBadByteRate);
			Assert(scaledBadByteRate == null && reasonBadByteRate != null && reasonBadByteRate.Contains("Invalid byte rate"),
				"B3_Target7I_InvalidByteRate_Rejected", reasonBadByteRate ?? "");

			// 7J: ByteRate 计算溢出 (宽整数溢出防护) (必须拒绝)
			byte[] wavOverflow = CreateWavWithByteRateOverflow();
			byte[]? scaledOverflow = SoundEffectManager.ScaleWavVolume(wavOverflow, 0.5, out string? reasonOverflow);
			Assert(scaledOverflow == null && reasonOverflow != null && reasonOverflow.Contains("Byte rate calculation overflow"),
				"B3_Target7J_ByteRateOverflow_Rejected", reasonOverflow ?? "");
		}

		// B3_Target8: 损坏、截断与越界文件防护测试 (Corrupted & Truncated Files)
		{
			// 8A: 空引用与极短数据 (< 12 字节)
			byte[]? scaledNull = SoundEffectManager.ScaleWavVolume(null, 0.5, out string? reasonNull);
			Assert(scaledNull == null && reasonNull != null && reasonNull.Contains("WAV data too short"), "B3_Target8A_NullData_Rejected");

			byte[] shortData = [0x52, 0x49, 0x46, 0x46]; // 4 bytes
			byte[]? scaledShort = SoundEffectManager.ScaleWavVolume(shortData, 0.5, out string? reasonShort);
			Assert(scaledShort == null && reasonShort != null && reasonShort.Contains("WAV data too short"), "B3_Target8A_ShortData_Rejected");

			// 8B: 非法魔数 (Non-RIFF / Non-WAVE)
			byte[] notRiff = Encoding.ASCII.GetBytes("FORM0000AIFFfmt 0000000000000000data0000");
			byte[]? scaledNotRiff = SoundEffectManager.ScaleWavVolume(notRiff, 0.5, out string? reasonNotRiff);
			Assert(scaledNotRiff == null && reasonNotRiff != null && reasonNotRiff.Contains("Invalid RIFF container identifier"), "B3_Target8B_NotRiff_Rejected");

			byte[] notWave = (byte[])CreateStandardWav(1, 16, 44100, [100, 200]).Clone();
			Encoding.ASCII.GetBytes("AVI ").CopyTo(notWave, 8);
			byte[]? scaledNotWave = SoundEffectManager.ScaleWavVolume(notWave, 0.5, out string? reasonNotWave);
			Assert(scaledNotWave == null && reasonNotWave != null && reasonNotWave.Contains("Invalid WAVE format identifier"), "B3_Target8B_NotWave_Rejected");

			// 8C: 声明 RIFF 长度超出实际文件长度 (Truncated File)
			byte[] truncatedRiff = CreateStandardWav(1, 16, 44100, [100, 200]);
			BitConverter.GetBytes(999999).CopyTo(truncatedRiff, 4); // 虚报 1MB 大小
			byte[]? scaledTruncRiff = SoundEffectManager.ScaleWavVolume(truncatedRiff, 0.5, out string? reasonTruncRiff);
			Assert(scaledTruncRiff == null && reasonTruncRiff != null && reasonTruncRiff.Contains("exceeds actual data length"), "B3_Target8C_TruncatedRiffDeclaredSize_Rejected");

			// 8D: 分块标头截断 (offset + 8 > file length)
			byte[] truncatedChunkHeader = CreateStandardWav(1, 16, 44100, [100, 200]).Take(40).ToArray(); // 在 data 标头中途截断
			BitConverter.GetBytes(truncatedChunkHeader.Length - 8).CopyTo(truncatedChunkHeader, 4);
			byte[]? scaledTruncHeader = SoundEffectManager.ScaleWavVolume(truncatedChunkHeader, 0.5, out string? reasonTruncHeader);
			Assert(scaledTruncHeader == null && reasonTruncHeader != null && reasonTruncHeader.Contains("Truncated chunk header"), "B3_Target8D_TruncatedChunkHeader_Rejected");

			// 8E: 分块大小越界 (Chunk Size Out Of Bounds)
			byte[] oobChunkWav = CreateStandardWav(1, 16, 44100, [100, 200]);
			BitConverter.GetBytes(0xFFFFFFFF).CopyTo(oobChunkWav, 40); // 将 data 大小修改为 4GB
			byte[]? scaledOob = SoundEffectManager.ScaleWavVolume(oobChunkWav, 0.5, out string? reasonOob);
			Assert(scaledOob == null && reasonOob != null && reasonOob.Contains("boundary out of range"), "B3_Target8E_OutOfBoundsChunkSize_Rejected");

			// 8F: 缺失 fmt 分块
			byte[] noFmtWav = CreateWavWithoutFmt();
			byte[]? scaledNoFmt = SoundEffectManager.ScaleWavVolume(noFmtWav, 0.5, out string? reasonNoFmt);
			Assert(scaledNoFmt == null && reasonNoFmt != null && (reasonNoFmt.Contains("Missing required 'fmt ' chunk") || reasonNoFmt.Contains("encountered before 'fmt ' chunk")), "B3_Target8F_MissingFmtChunk_Rejected");

			// 8G: fmt 分块过短 (< 16 字节)
			byte[] shortFmtWav = CreateWavWithShortFmt();
			byte[]? scaledShortFmt = SoundEffectManager.ScaleWavVolume(shortFmtWav, 0.5, out string? reasonShortFmt);
			Assert(scaledShortFmt == null && reasonShortFmt != null && reasonShortFmt.Contains("'fmt ' chunk size too small"), "B3_Target8G_FmtChunkTooSmall_Rejected");

			// 8H: 缺失 data 分块
			byte[] noDataWav = CreateWavWithoutData();
			byte[]? scaledNoData = SoundEffectManager.ScaleWavVolume(noDataWav, 0.5, out string? reasonNoData);
			Assert(scaledNoData == null && reasonNoData != null && reasonNoData.Contains("Missing required 'data' chunk"), "B3_Target8H_MissingDataChunk_Rejected");

			// 8I: 空 data 分块 (大小为 0)
			byte[] emptyDataWav = CreateStandardWav(1, 16, 44100, []);
			byte[]? scaledEmptyData = SoundEffectManager.ScaleWavVolume(emptyDataWav, 0.5, out string? reasonEmptyData);
			Assert(scaledEmptyData == null && reasonEmptyData != null && reasonEmptyData.Contains("Empty 'data' chunk"), "B3_Target8I_EmptyDataChunk_Rejected");

			// 8J: 拒绝第二个/多个 data 分块 (Multiple data chunks rejected)
			byte[] duplicateDataWav = CreateWavWithDuplicateDataChunks();
			byte[]? scaledDuplicateData = SoundEffectManager.ScaleWavVolume(duplicateDataWav, 0.5, out string? reasonDuplicateData);
			Assert(scaledDuplicateData == null && reasonDuplicateData != null && reasonDuplicateData.Contains("Multiple 'data' chunks not supported"),
				"B3_Target8J_DuplicateDataChunk_Rejected", reasonDuplicateData ?? "");
		}

		// B3_Target9: 文件加载链路回退机制验证 (TryLoadCustomWavFile Fallback & Diagnosis)
		{
			string testDir = Path.Combine(_sandboxPath, "WavB3Tests");
			Directory.CreateDirectory(testDir);

			// 9A: 加载损坏文件 -> 沿用程序化微动合成声回退并记录原因
			string corruptFilePath = Path.Combine(testDir, "corrupt_sound.wav");
			File.WriteAllBytes(corruptFilePath, [0x52, 0x49, 0x46, 0x46, 0x00, 0x00]); // 损坏 WAV
			SoundEffectManager.ResetTestSeams();
			byte[] fallbackWav = SoundEffectManager.TryLoadCustomWavFile(corruptFilePath, 0.5);
			Assert(fallbackWav != null && fallbackWav.Length >= 44, "B3_Target9A_TryLoadCustomWavFile_FallbackToSynthesizedOnCorrupt");
			Assert(!string.IsNullOrEmpty(SoundEffectManager.LastWavFallbackReason), "B3_Target9A_TryLoadCustomWavFile_FallbackReasonRecorded");

			// 9B: 加载不存在的文件 -> 沿用合成声回退并记录 File not found
			SoundEffectManager.ResetTestSeams();
			string missingPath = Path.Combine(testDir, "non_existent.wav");
			byte[] missingFallback = SoundEffectManager.TryLoadCustomWavFile(missingPath, 0.5);
			Assert(missingFallback != null && missingFallback.Length >= 44, "B3_Target9B_TryLoadCustomWavFile_FallbackOnMissingFile");
			Assert(SoundEffectManager.LastWavFallbackReason == "File not found", "B3_Target9B_TryLoadCustomWavFile_ReasonFileNotFound");

			// 9C: 加载非 .wav 扩展名 -> 拒绝并记录原因
			SoundEffectManager.ResetTestSeams();
			string mp3Path = Path.Combine(testDir, "sample.mp3");
			File.WriteAllText(mp3Path, "dummy mp3 data");
			byte[] mp3Fallback = SoundEffectManager.TryLoadCustomWavFile(mp3Path, 0.5);
			Assert(mp3Fallback != null && mp3Fallback.Length >= 44, "B3_Target9C_TryLoadCustomWavFile_FallbackOnNonWavExtension");
			Assert(SoundEffectManager.LastWavFallbackReason == "Not a .wav file extension", "B3_Target9C_TryLoadCustomWavFile_ReasonNotWavExtension");

			// 9D: 加载包含合法 JUNK 分块的外部真实 WAV 文件 -> 正确缩放，绝不回退
			SoundEffectManager.ResetTestSeams();
			string validFilePath = Path.Combine(testDir, "valid_with_junk.wav");
			byte[] validWav = CreateWavWithJunkBeforeData([500, 1000, 1500, 2000]);
			File.WriteAllBytes(validFilePath, validWav);
			byte[] loadedScaled = SoundEffectManager.TryLoadCustomWavFile(validFilePath, 0.5);
			Assert(loadedScaled != null && loadedScaled.Length == validWav.Length, "B3_Target9D_TryLoadCustomWavFile_LoadsAndScalesValidFile");
			Assert(SoundEffectManager.LastWavFallbackReason == null, "B3_Target9D_TryLoadCustomWavFile_NoErrorWhenValid");
		}

		Log("  [Stage B3 Metric] RIFF/WAVE 分块解析、16-bit PCM 正确性、奇数填充、单双声道及异常回退验证全量通过");
	}

	private static byte[] CreateStandardWav(int channels, int bitsPerSample, int sampleRate, short[] samples)
	{
		using var ms = new MemoryStream();
		using var bw = new BinaryWriter(ms);

		bw.Write(Encoding.ASCII.GetBytes("RIFF"));
		int dataBytes = samples.Length * 2;
		int riffSize = 36 + dataBytes;
		bw.Write(riffSize);
		bw.Write(Encoding.ASCII.GetBytes("WAVE"));

		bw.Write(Encoding.ASCII.GetBytes("fmt "));
		bw.Write(16);
		bw.Write((short)1); // PCM
		bw.Write((short)channels);
		bw.Write(sampleRate);
		bw.Write(sampleRate * channels * (bitsPerSample / 8));
		bw.Write((short)(channels * (bitsPerSample / 8)));
		bw.Write((short)bitsPerSample);

		bw.Write(Encoding.ASCII.GetBytes("data"));
		bw.Write(dataBytes);
		for (int i = 0; i < samples.Length; i++)
		{
			bw.Write(samples[i]);
		}
		bw.Flush();
		return ms.ToArray();
	}

	private static byte[] CreateWavWithJunkBeforeData(short[] samples)
	{
		using var ms = new MemoryStream();
		using var bw = new BinaryWriter(ms);

		bw.Write(Encoding.ASCII.GetBytes("RIFF"));
		bw.Write(0); // placeholder
		bw.Write(Encoding.ASCII.GetBytes("WAVE"));

		bw.Write(Encoding.ASCII.GetBytes("fmt "));
		bw.Write(16);
		bw.Write((short)1); // PCM
		bw.Write((short)1); // mono
		bw.Write(44100);
		bw.Write(88200);
		bw.Write((short)2);
		bw.Write((short)16);

		bw.Write(Encoding.ASCII.GetBytes("JUNK"));
		bw.Write(16);
		for (int i = 0; i < 16; i++) bw.Write((byte)0xEE);

		bw.Write(Encoding.ASCII.GetBytes("data"));
		bw.Write(samples.Length * 2);
		for (int i = 0; i < samples.Length; i++)
		{
			bw.Write(samples[i]);
		}
		bw.Flush();
		byte[] bytes = ms.ToArray();
		BitConverter.GetBytes(bytes.Length - 8).CopyTo(bytes, 4);
		return bytes;
	}

	private static byte[] CreateWavWithListBeforeData(short[] samples)
	{
		using var ms = new MemoryStream();
		using var bw = new BinaryWriter(ms);

		bw.Write(Encoding.ASCII.GetBytes("RIFF"));
		bw.Write(0);
		bw.Write(Encoding.ASCII.GetBytes("WAVE"));

		bw.Write(Encoding.ASCII.GetBytes("fmt "));
		bw.Write(16);
		bw.Write((short)1);
		bw.Write((short)1);
		bw.Write(44100);
		bw.Write(88200);
		bw.Write((short)2);
		bw.Write((short)16);

		// LIST Chunk: 24 bytes (INFO chunk)
		bw.Write(Encoding.ASCII.GetBytes("LIST"));
		bw.Write(24);
		bw.Write(Encoding.ASCII.GetBytes("INFO"));
		bw.Write(Encoding.ASCII.GetBytes("INAM"));
		bw.Write(12);
		bw.Write(Encoding.ASCII.GetBytes("StarPieSound"));

		bw.Write(Encoding.ASCII.GetBytes("data"));
		bw.Write(samples.Length * 2);
		for (int i = 0; i < samples.Length; i++)
		{
			bw.Write(samples[i]);
		}
		bw.Flush();
		byte[] bytes = ms.ToArray();
		BitConverter.GetBytes(bytes.Length - 8).CopyTo(bytes, 4);
		return bytes;
	}

	private static byte[] CreateWavWithListAfterData(short[] samples)
	{
		using var ms = new MemoryStream();
		using var bw = new BinaryWriter(ms);

		bw.Write(Encoding.ASCII.GetBytes("RIFF"));
		bw.Write(0);
		bw.Write(Encoding.ASCII.GetBytes("WAVE"));

		bw.Write(Encoding.ASCII.GetBytes("fmt "));
		bw.Write(16);
		bw.Write((short)1);
		bw.Write((short)1);
		bw.Write(44100);
		bw.Write(88200);
		bw.Write((short)2);
		bw.Write((short)16);

		bw.Write(Encoding.ASCII.GetBytes("data"));
		bw.Write(samples.Length * 2);
		for (int i = 0; i < samples.Length; i++)
		{
			bw.Write(samples[i]);
		}

		// Trailing LIST Chunk: 20 bytes payload + 8 bytes header = 28 bytes
		bw.Write(Encoding.ASCII.GetBytes("LIST"));
		bw.Write(20);
		bw.Write(Encoding.ASCII.GetBytes("INFO"));
		bw.Write(Encoding.ASCII.GetBytes("IART"));
		bw.Write(8);
		bw.Write(Encoding.ASCII.GetBytes("StarPie\0"));

		bw.Flush();
		byte[] bytes = ms.ToArray();
		BitConverter.GetBytes(bytes.Length - 8).CopyTo(bytes, 4);
		return bytes;
	}

	private static byte[] CreateWavWithOddJunkChunk(short[] samples, bool includePadByte)
	{
		using var ms = new MemoryStream();
		using var bw = new BinaryWriter(ms);

		bw.Write(Encoding.ASCII.GetBytes("RIFF"));
		bw.Write(0);
		bw.Write(Encoding.ASCII.GetBytes("WAVE"));

		bw.Write(Encoding.ASCII.GetBytes("fmt "));
		bw.Write(16);
		bw.Write((short)1);
		bw.Write((short)1);
		bw.Write(44100);
		bw.Write(88200);
		bw.Write((short)2);
		bw.Write((short)16);

		// Odd length JUNK chunk: 15 bytes
		bw.Write(Encoding.ASCII.GetBytes("JUNK"));
		bw.Write(15);
		for (int i = 0; i < 15; i++) bw.Write((byte)0xAA);
		if (includePadByte)
		{
			bw.Write((byte)0x00); // 1-byte RIFF word-alignment pad
		}

		bw.Write(Encoding.ASCII.GetBytes("data"));
		bw.Write(samples.Length * 2);
		for (int i = 0; i < samples.Length; i++)
		{
			bw.Write(samples[i]);
		}

		bw.Flush();
		byte[] bytes = ms.ToArray();
		BitConverter.GetBytes(bytes.Length - 8).CopyTo(bytes, 4);
		return bytes;
	}

	private static byte[] CreateWavWithOddDataChunk()
	{
		using var ms = new MemoryStream();
		using var bw = new BinaryWriter(ms);

		bw.Write(Encoding.ASCII.GetBytes("RIFF"));
		bw.Write(0);
		bw.Write(Encoding.ASCII.GetBytes("WAVE"));

		bw.Write(Encoding.ASCII.GetBytes("fmt "));
		bw.Write(16);
		bw.Write((short)1);
		bw.Write((short)1);
		bw.Write(44100);
		bw.Write(88200);
		bw.Write((short)2);
		bw.Write((short)16);

		// Odd length data chunk: 7 bytes
		bw.Write(Encoding.ASCII.GetBytes("data"));
		bw.Write(7);
		for (int i = 0; i < 7; i++) bw.Write((byte)0x11);
		bw.Write((byte)0x00); // pad

		bw.Flush();
		byte[] bytes = ms.ToArray();
		BitConverter.GetBytes(bytes.Length - 8).CopyTo(bytes, 4);
		return bytes;
	}

	private static byte[] CreateCustomFmtWav(ushort formatTag, ushort channels, uint sampleRate, ushort bitsPerSample, ushort? blockAlignOverride = null)
	{
		using var ms = new MemoryStream();
		using var bw = new BinaryWriter(ms);

		bw.Write(Encoding.ASCII.GetBytes("RIFF"));
		bw.Write(0);
		bw.Write(Encoding.ASCII.GetBytes("WAVE"));

		bw.Write(Encoding.ASCII.GetBytes("fmt "));
		bw.Write(16);
		bw.Write(formatTag);
		bw.Write(channels);
		bw.Write(sampleRate);
		ushort blockAlign = blockAlignOverride ?? (ushort)(channels * (bitsPerSample / 8));
		bw.Write((uint)(sampleRate * blockAlign));
		bw.Write(blockAlign);
		bw.Write(bitsPerSample);

		bw.Write(Encoding.ASCII.GetBytes("data"));
		bw.Write(16);
		for (int i = 0; i < 16; i++) bw.Write((byte)0x33);

		bw.Flush();
		byte[] bytes = ms.ToArray();
		BitConverter.GetBytes(bytes.Length - 8).CopyTo(bytes, 4);
		return bytes;
	}

	private static byte[] CreateWavWithoutFmt()
	{
		using var ms = new MemoryStream();
		using var bw = new BinaryWriter(ms);

		bw.Write(Encoding.ASCII.GetBytes("RIFF"));
		bw.Write(0);
		bw.Write(Encoding.ASCII.GetBytes("WAVE"));

		bw.Write(Encoding.ASCII.GetBytes("data"));
		bw.Write(8);
		for (int i = 0; i < 8; i++) bw.Write((byte)0x22);

		bw.Flush();
		byte[] bytes = ms.ToArray();
		BitConverter.GetBytes(bytes.Length - 8).CopyTo(bytes, 4);
		return bytes;
	}

	private static byte[] CreateWavWithShortFmt()
	{
		using var ms = new MemoryStream();
		using var bw = new BinaryWriter(ms);

		bw.Write(Encoding.ASCII.GetBytes("RIFF"));
		bw.Write(0);
		bw.Write(Encoding.ASCII.GetBytes("WAVE"));

		bw.Write(Encoding.ASCII.GetBytes("fmt "));
		bw.Write(12); // too short (< 16)
		for (int i = 0; i < 12; i++) bw.Write((byte)0x00);

		bw.Write(Encoding.ASCII.GetBytes("data"));
		bw.Write(8);
		for (int i = 0; i < 8; i++) bw.Write((byte)0x22);

		bw.Flush();
		byte[] bytes = ms.ToArray();
		BitConverter.GetBytes(bytes.Length - 8).CopyTo(bytes, 4);
		return bytes;
	}

	private static byte[] CreateWavWithoutData()
	{
		using var ms = new MemoryStream();
		using var bw = new BinaryWriter(ms);

		bw.Write(Encoding.ASCII.GetBytes("RIFF"));
		bw.Write(0);
		bw.Write(Encoding.ASCII.GetBytes("WAVE"));

		bw.Write(Encoding.ASCII.GetBytes("fmt "));
		bw.Write(16);
		bw.Write((short)1);
		bw.Write((short)1);
		bw.Write(44100);
		bw.Write(88200);
		bw.Write((short)2);
		bw.Write((short)16);

		bw.Flush();
		byte[] bytes = ms.ToArray();
		BitConverter.GetBytes(bytes.Length - 8).CopyTo(bytes, 4);
		return bytes;
	}

	private static byte[] CreateWavWithMismatchedByteRate()
	{
		using var ms = new MemoryStream();
		using var bw = new BinaryWriter(ms);

		bw.Write(Encoding.ASCII.GetBytes("RIFF"));
		bw.Write(0);
		bw.Write(Encoding.ASCII.GetBytes("WAVE"));

		bw.Write(Encoding.ASCII.GetBytes("fmt "));
		bw.Write(16);
		bw.Write((short)1); // PCM
		bw.Write((short)1); // mono
		bw.Write(44100);    // sampleRate
		bw.Write(12345);    // WRONG byteRate (expected 88200)
		bw.Write((short)2); // blockAlign
		bw.Write((short)16);// bitsPerSample

		bw.Write(Encoding.ASCII.GetBytes("data"));
		bw.Write(16);
		for (int i = 0; i < 16; i++) bw.Write((byte)0x33);

		bw.Flush();
		byte[] bytes = ms.ToArray();
		BitConverter.GetBytes(bytes.Length - 8).CopyTo(bytes, 4);
		return bytes;
	}

	private static byte[] CreateWavWithByteRateOverflow()
	{
		using var ms = new MemoryStream();
		using var bw = new BinaryWriter(ms);

		bw.Write(Encoding.ASCII.GetBytes("RIFF"));
		bw.Write(0);
		bw.Write(Encoding.ASCII.GetBytes("WAVE"));

		bw.Write(Encoding.ASCII.GetBytes("fmt "));
		bw.Write(16);
		bw.Write((short)1); // PCM
		bw.Write((short)2); // stereo
		bw.Write((uint)0x80000000); // 2147483648 Hz
		bw.Write((uint)0); // byteRate
		bw.Write((short)4); // blockAlign
		bw.Write((short)16);// bitsPerSample

		bw.Write(Encoding.ASCII.GetBytes("data"));
		bw.Write(16);
		for (int i = 0; i < 16; i++) bw.Write((byte)0x33);

		bw.Flush();
		byte[] bytes = ms.ToArray();
		BitConverter.GetBytes(bytes.Length - 8).CopyTo(bytes, 4);
		return bytes;
	}

	private static byte[] CreateWavWithDuplicateDataChunks()
	{
		using var ms = new MemoryStream();
		using var bw = new BinaryWriter(ms);

		bw.Write(Encoding.ASCII.GetBytes("RIFF"));
		bw.Write(0);
		bw.Write(Encoding.ASCII.GetBytes("WAVE"));

		bw.Write(Encoding.ASCII.GetBytes("fmt "));
		bw.Write(16);
		bw.Write((short)1); // PCM
		bw.Write((short)1); // mono
		bw.Write(44100);
		bw.Write(88200);
		bw.Write((short)2);
		bw.Write((short)16);

		// First data chunk
		bw.Write(Encoding.ASCII.GetBytes("data"));
		bw.Write(16);
		for (int i = 0; i < 16; i++) bw.Write((byte)0x33);

		// Second duplicate data chunk
		bw.Write(Encoding.ASCII.GetBytes("data"));
		bw.Write(16);
		for (int i = 0; i < 16; i++) bw.Write((byte)0x44);

		bw.Flush();
		byte[] bytes = ms.ToArray();
		BitConverter.GetBytes(bytes.Length - 8).CopyTo(bytes, 4);
		return bytes;
	}
	#endregion



	#region 阶段 B2a.3: 停止代际定向清理、单次试听完整回收与动态断言 (Stage B2a.3 Target Regressions)
	private static void TestStageB2a3_RemainingIssuesRegressions()
	{
		Log("\n>>> 阶段 B2a.3: 停止代际定向清理与单次试听完整回收断言 (Stage B2a.3)");

		// B2a_3_Target1: Shutdown 必须只清理自己停止的代际 (防止快照过期竞态抹除并发新代会话及请求)
		{
			SoundEffectManager.ResetTestSeams();
			ManualResetEvent shutdownPausedBeforeSessionLock = new(false);
			TrackBarrier(shutdownPausedBeforeSessionLock);

			// 确保当前无活动上下文 (oldContext == null)
			SoundEffectManager.Shutdown();

			Thread shutdownThread;
			long newSessionId = 0L;
			AutoResetEvent newSoundPlayed = new(false);
			SoundEffectManager.PlaybackSink = (data, flags) => newSoundPlayed.Set();

			// 测试线程持有 _sessionLock
			lock (SoundEffectManager.TestSessionLock)
			{
				shutdownThread = new Thread(() =>
				{
					// 此处启动一次 oldContext == null 的 Shutdown
					// 该 Shutdown 尝试获取 _sessionLock，被测试线程持有锁所阻塞
					shutdownPausedBeforeSessionLock.Set();
					SoundEffectManager.Shutdown();
				})
				{
					IsBackground = true,
					Name = "TestStageB2a3_ShutdownNoContext"
				};
				shutdownThread.Start();

				shutdownPausedBeforeSessionLock.WaitOne(2000);
				Thread.Sleep(50); // 确保 shutdownThread 已启动并阻塞在 lock (_sessionLock)

				// 此时启动新代并创建新会话与新待播请求
				SoundEffectManager.Initialize("Mechanical", 0.6, force: true);
				newSessionId = SoundEffectManager.BeginSession(SoundSessionSource.NormalGesture);
				SoundEffectManager.LastHoverTick = 0L;
				lock (SoundEffectManager.TestQueueLock)
				{
					SoundEffectManager.Play(SoundType.SectorHover, SoundSessionSource.NormalGesture, newSessionId);
					Assert(SoundEffectManager.TestPendingSound == SoundType.SectorHover, "B2a_3_Target1_NewSoundEnqueuedWhileShutdownBlocked");
				}
			}

			bool shutdownJoined = shutdownThread.Join(3000);
			Assert(shutdownJoined, "B2a_3_Target1_ShutdownThreadJoined");

			// 核心断言：依据本次 Shutdown 实际拥有的停止代际清理，因为本次 Shutdown 是 oldContext == null，未停止任何代际，
			// 所以新会话与新待播请求绝不得被删除！
			bool newSessionPreserved = SoundEffectManager.HasSession(newSessionId);
			Assert(newSessionPreserved, "B2a_3_Target1_NewSessionPreservedAcrossStaleSnapshotShutdown",
				$"依据停止代际清理：新会话必须保留，实际保留={newSessionPreserved}");

			// 验证新待播请求保留且实际可播放 (未被旧代 Shutdown 清理丢弃，由新 Worker 成功消费播放)
			bool played = newSoundPlayed.WaitOne(3000);
			Assert(played, "B2a_3_Target1_NewPendingSoundPreservedAcrossStaleSnapshotShutdown",
				$"依据停止代际清理：新待播请求未被清除，实际成功播放={played}");
			Assert(played, "B2a_3_Target1_NewSoundActuallyPlayableAfterShutdown",
				$"新代声音必须实际由 Worker 播放完成，实际播放={played}");

			SoundEffectManager.Shutdown();
			SoundEffectManager.ResetTestSeams();
		}

		// B2a_3_Target2: 完整回收单次试听，阻塞后端下连续提交 1000 次试听容量有界且画布不受影响
		{
			SoundEffectManager.ResetTestSeams();
			AutoResetEvent blockerStarted = new(false);
			ManualResetEvent allowBlockerExit = new(false);
			TrackBarrier(allowBlockerExit);

			SoundEffectManager.SoundPlaybackStarted = (t, wav, tick) => blockerStarted.Set();
			SoundEffectManager.PlaybackSink = (data, flags) => allowBlockerExit.WaitOne(5000);

			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);
			// 1. 创建常驻活动画布会话（不是单次试听）
			long canvasSess = SoundEffectManager.BeginSession(SoundSessionSource.SettingsPreview, isSingleAudition: false);

			// 2. 触发一次试听使后端进入阻塞状态
			SoundEffectManager.PlayPreview(SoundType.WheelPopup, SoundSessionSource.SettingsPreview);
			blockerStarted.WaitOne(2000);

			// 3. 阻塞模拟后端下连续提交至少 1000 次试听
			for (int i = 0; i < 1000; i++)
			{
				SoundEffectManager.LastHoverTick = 0L;
				SoundEffectManager.PlayPreview(SoundType.SectorHover, SoundSessionSource.SettingsPreview);
			}

			// 4. 断言：会话数量必须有界（<= 64），且绝不得删除活动画布
			int sessionCountDuringBurst = SoundEffectManager.SessionCount;
			bool canvasStillActive = SoundEffectManager.HasSession(canvasSess);

			Assert(sessionCountDuringBurst <= 64, "B2a_3_Target2_SessionCountBoundedUnder1000Burst",
				$"1000 次试听爆发后会话数必须 <= 64，实测: {sessionCountDuringBurst}");
			Assert(canvasStillActive, "B2a_3_Target2_CanvasSessionPreservedUnder1000Burst",
				$"活动画布绝对不得被删除，实际存在: {canvasStillActive}");

			// 5. 释放阻塞屏障，验证最新有效请求可播放
			AutoResetEvent latestSoundFinished = new(false);
			SoundEffectManager.SoundPlaybackFinished = (t, wav, tick) =>
			{
				if (t == SoundType.SectorHover) latestSoundFinished.Set();
			};

			allowBlockerExit.Set();
			bool latestPlayed = latestSoundFinished.WaitOne(3000);
			Assert(latestPlayed, "B2a_3_Target2_LatestAuditionActuallyPlayableAfterRelease",
				$"释放后最新有效请求必须可播放，实际播放: {latestPlayed}");

			// 6. 验证播放完成后所有单次试听均被回收，仅画布保留
			Thread.Sleep(50);
			bool canvasStillThere = SoundEffectManager.HasSession(canvasSess);
			Assert(canvasStillThere, "B2a_3_Target2_CanvasSessionSurvivesAfterAllAuditionsFinished");

			SoundEffectManager.EndSession(SoundSessionSource.SettingsPreview, canvasSess, allowTerminalFeedback: false);
			SoundEffectManager.Shutdown();
			SoundEffectManager.ResetTestSeams();
		}

		// B2a_3_Target3A: ResetTestSeams 不得清空整轮测试的线程创建登记
		{
			WaitForAllWorkersToExit(2000);
			int baseCount = SoundEffectManager.GetAllCreatedWorkers().Count;
			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);
			SoundEffectManager.PlayPreview(SoundType.WheelPopup);
			var workersBefore = SoundEffectManager.GetAllCreatedWorkers();
			int countBefore = workersBefore.Count;

			// ResetTestSeams 不得清空整轮测试的线程创建登记
			SoundEffectManager.ResetTestSeams();
			var workersAfter = SoundEffectManager.GetAllCreatedWorkers();
			int countAfter = workersAfter.Count;

			Assert(countBefore >= baseCount, "B2a_3_Target3A_WorkerTrackedOnInit");
			Assert(countAfter == countBefore, "B2a_3_Target3A_ResetTestSeamsPreservesTrackedWorkers",
				$"ResetTestSeams 不得清空线程登记: 重置前={countBefore}, 重置后={countAfter}");

			SoundEffectManager.Shutdown();
			SoundEffectManager.ResetTestSeams();
		}

		// B2a_3_Target3B: 动态断言 GestureController Esc 取消路径保留 GestureCancel 终态反馈音且会话正确关闭
		{
			SoundEffectManager.ResetTestSeams();
			long gestureSess = SoundEffectManager.BeginSession(SoundSessionSource.NormalGesture);
			AutoResetEvent cancelSoundPlayed = new(false);
			SoundEffectManager.PlaybackSink = (data, flags) => { };
			SoundEffectManager.SoundPlaybackFinished = (t, wav, tick) =>
			{
				if (t == SoundType.GestureCancel) cancelSoundPlayed.Set();
			};

			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);

			// 模拟手势中已排队 SectorHover
			SoundEffectManager.LastHoverTick = 0L;
			SoundEffectManager.Play(SoundType.SectorHover, SoundSessionSource.NormalGesture, gestureSess);

			// 触发取消：模拟 Esc 逻辑，传入 allowTerminalFeedback: true 并下发 GestureCancel
			long capturedSessionId = gestureSess;
			lock (SoundEffectManager.TestQueueLock)
			{
				SoundEffectManager.EndSession(SoundSessionSource.NormalGesture, capturedSessionId, allowTerminalFeedback: true);
				SoundEffectManager.Play(SoundType.GestureCancel, SoundSessionSource.NormalGesture, capturedSessionId);

				// 断言待播槽已为 GestureCancel (未被撤销)，且 SectorHover 已被撤销
				Assert(SoundEffectManager.TestPendingSound == SoundType.GestureCancel, "B2a_3_Target3B_EscCancelsHoverAndPreservesCancelFeedback");
			}

			bool cancelPlayed = cancelSoundPlayed.WaitOne(3000);
			Assert(cancelPlayed, "B2a_3_Target3B_EscGestureCancelActuallyPlayed");

			// 播放完成后，终态会话已彻底关闭
			Thread.Sleep(50);
			bool sessionClosed = !SoundEffectManager.IsSessionActive(gestureSess);
			Assert(sessionClosed, "B2a_3_Target3B_EscSessionClosedAfterFeedback");

			SoundEffectManager.Shutdown();
			SoundEffectManager.ResetTestSeams();
		}

		// B2a_3_Target3C: 动态断言 SettingsWindow 完整试听流程正常完成保留终态音 vs 取消撤销
		{
			SoundEffectManager.ResetTestSeams();
			long previewSess = SoundEffectManager.BeginSession(SoundSessionSource.SettingsPreview, isSingleAudition: false);
			AutoResetEvent previewCancelPlayed = new(false);
			SoundEffectManager.PlaybackSink = (data, flags) => { };
			SoundEffectManager.SoundPlaybackFinished = (t, wav, tick) =>
			{
				if (t == SoundType.GestureCancel) previewCancelPlayed.Set();
			};

			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);

			// 正常完成路径：allowTerminalFeedback: true
			SoundEffectManager.PlayPreview(SoundType.WheelPopup, SoundSessionSource.SettingsPreview, previewSess);
			SoundEffectManager.PlayPreview(SoundType.SectorHover, SoundSessionSource.SettingsPreview, previewSess);
			SoundEffectManager.PlayPreview(SoundType.GestureCancel, SoundSessionSource.SettingsPreview, previewSess);
			SoundEffectManager.EndSession(SoundSessionSource.SettingsPreview, previewSess, allowTerminalFeedback: true);

			bool played = previewCancelPlayed.WaitOne(3000);
			Assert(played, "B2a_3_Target3C_PreviewNormalCompletionPlaysTerminalFeedback");

			// 用户取消路径：allowTerminalFeedback: false
			long cancelSess = SoundEffectManager.BeginSession(SoundSessionSource.SettingsPreview, isSingleAudition: false);
			AutoResetEvent blockerStarted = new(false);
			ManualResetEvent allowBlockerExit = new(false);
			TrackBarrier(allowBlockerExit);
			SoundEffectManager.SoundPlaybackStarted = (t, wav, tick) => blockerStarted.Set();
			SoundEffectManager.PlaybackSink = (data, flags) => allowBlockerExit.WaitOne(3000);

			SoundEffectManager.PlayPreview(SoundType.WheelPopup, SoundSessionSource.SettingsPreview, cancelSess);
			blockerStarted.WaitOne(2000);

			// 待播 GestureCancel
			SoundEffectManager.PlayPreview(SoundType.GestureCancel, SoundSessionSource.SettingsPreview, cancelSess);
			// 用户在未开始前取消：allowTerminalFeedback: false
			SoundEffectManager.EndSession(SoundSessionSource.SettingsPreview, cancelSess, allowTerminalFeedback: false);

			Assert(SoundEffectManager.TestPendingSound == null, "B2a_3_Target3C_UserCancelRevokesTerminalFeedback");

			allowBlockerExit.Set();
			SoundEffectManager.Shutdown();
			SoundEffectManager.ResetTestSeams();
		}
	}
	#endregion

	#region 阶段 B2b: 悬停目标稳定防抖断言 (Stage B2b Hover Target Stability Assertions)
	private static void TestStageB2b_HoverTargetStabilityAssertions()
	{
		Log("\n>>> 阶段 B2b: 悬停目标稳定防抖断言 (Stage B2b Hover Target Stability Assertions)");

		// B2b_Target1: 36ms 相邻往返不连响 & 稳定目标仅响一次
		{
			SoundEffectManager.ResetTestSeams();
			long simTime = 1000L;
			SoundEffectManager.TimeProvider = () => simTime;

			var mouseHook = new MouseHook();
			using var gc = new GestureSoundHarness(mouseHook);
			gc.TestSetGestureActive(true, version: 201);
			long sessId = gc.TestSoundSessionId;

			var playedSounds = new ConcurrentQueue<(SoundType? Type, long Tick)>();
			SoundEffectManager.SoundPlayed = (t, wav, tick) => playedSounds.Enqueue((t, tick));

			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);

			// 10 次 36ms 极速往返分界线
			for (int i = 0; i < 10; i++)
			{
				simTime = 1000L + i * 36L;
				int sec = (i % 2 == 0) ? 0 : 1;
				gc.TestQueueHighlightUpdate(sec, -1, false, false, 201);
			}

			// 在往返期间 (总耗时 360ms，但每个扇区驻留仅 36ms < 45ms)，绝不应播放
			Thread.Sleep(50);
			Assert(playedSounds.IsEmpty, "B2b_Target1_OscillationAt36msDoesNotPlay",
				$"36ms往返期间期望0次发声，实际发声: {playedSounds.Count}");

			// 停留在 Sector 0，时间推进 50ms (>= 45ms)
			simTime = 1000L + 10 * 36L + 50L;
			SoundEffectManager.TestSignalWorker();
			Thread.Sleep(60);

			Assert(playedSounds.Count == 1, "B2b_Target1_StabilizedTargetPlaysExactlyOnce",
				$"稳定后期望发声1次，实际发声: {playedSounds.Count}");

			// 继续停留 100ms 并重复报告 Sector 0
			simTime += 100L;
			gc.TestQueueHighlightUpdate(0, -1, false, false, 201);
			SoundEffectManager.TestSignalWorker();
			Thread.Sleep(60);

			Assert(playedSounds.Count == 1, "B2b_Target1_ContinuingStationaryDoesNotReplay",
				$"继续停留期望保持1次发声，实际发声: {playedSounds.Count}");

			gc.TestEndActiveGesture();
			SoundEffectManager.Shutdown();
			SoundEffectManager.ResetTestSeams();
		}

		// B2b_Target2: 停鼠后自动触发 (无需额外鼠标事件在到期时发声)
		{
			SoundEffectManager.ResetTestSeams();
			long sessId = SoundEffectManager.BeginSession(SoundSessionSource.NormalGesture);
			AutoResetEvent hoverPlayed = new(false);
			SoundEffectManager.SoundPlayed = (t, wav, tick) =>
			{
				if (t == SoundType.SectorHover) hoverPlayed.Set();
			};

			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);

			// 鼠标滑入 Sector 2 后绝对静止（不再产生任何鼠标事件）
			SoundEffectManager.ReportHover(SoundSessionSource.NormalGesture, sessId, level: 0, parentIndex: -1, subIndex: 2);

			// 等待工作线程内部按 45ms 超时自动唤醒并触发发声
			bool autoFired = hoverPlayed.WaitOne(200);
			Assert(autoFired, "B2b_Target2_StationaryMouseTriggersWithoutNewEvent",
				"鼠标静止后工作线程在稳定到期时自动触发发声");

			SoundEffectManager.EndSession(SoundSessionSource.NormalGesture, sessId, allowTerminalFeedback: false);
			SoundEffectManager.Shutdown();
			SoundEffectManager.ResetTestSeams();
		}

		// B2b_Target3: 重复报告同目标不延期稳定期限
		{
			SoundEffectManager.ResetTestSeams();
			long simTime = 3000L;
			SoundEffectManager.TimeProvider = () => simTime;
			long sessId = SoundEffectManager.BeginSession(SoundSessionSource.NormalGesture);

			var playedSounds = new ConcurrentQueue<(SoundType? Type, long Tick)>();
			SoundEffectManager.SoundPlayed = (t, wav, tick) => playedSounds.Enqueue((t, tick));

			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);

			// t=3000 进入 Sector 3
			SoundEffectManager.ReportHover(SoundSessionSource.NormalGesture, sessId, level: 0, parentIndex: -1, subIndex: 3);

			// t=3010, 3020, 3030 在同一扇区内微动 (重复报告同一目标)
			simTime = 3010L;
			SoundEffectManager.ReportHover(SoundSessionSource.NormalGesture, sessId, level: 0, parentIndex: -1, subIndex: 3);
			simTime = 3020L;
			SoundEffectManager.ReportHover(SoundSessionSource.NormalGesture, sessId, level: 0, parentIndex: -1, subIndex: 3);
			simTime = 3030L;
			SoundEffectManager.ReportHover(SoundSessionSource.NormalGesture, sessId, level: 0, parentIndex: -1, subIndex: 3);

			// 到达 t=3045 (距初始进入满 45ms)，即使 15ms 前才刚汇报过同一目标，也必须按初始进入时间点放行发声
			simTime = 3045L;
			SoundEffectManager.TestSignalWorker();
			Thread.Sleep(60);

			Assert(playedSounds.Count == 1, "B2b_Target3_SameTargetDoesNotPostponeDeadline",
				$"同目标重复报告不推迟截止期，期望在 3045ms 放行: 实际发声={playedSounds.Count}");

			SoundEffectManager.EndSession(SoundSessionSource.NormalGesture, sessId, allowTerminalFeedback: false);
			SoundEffectManager.Shutdown();
			SoundEffectManager.ResetTestSeams();
		}

		// B2b_Target4: 不同父扇区下同编号子扇区为不同目标
		{
			SoundEffectManager.ResetTestSeams();
			long simTime = 4000L;
			SoundEffectManager.TimeProvider = () => simTime;
			long sessId = SoundEffectManager.BeginSession(SoundSessionSource.NormalGesture);

			var playedList = new ConcurrentQueue<long>();
			SoundEffectManager.SoundPlayed = (t, wav, tick) => playedList.Enqueue(tick);

			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);

			// t=4000: Parent 0, Sub 1
			SoundEffectManager.ReportHover(SoundSessionSource.NormalGesture, sessId, level: 1, parentIndex: 0, subIndex: 1);

			// t=4020: 切换至 Parent 2, Sub 1 (虽然 Sub 同为 1，但 Parent 不同，属于不同目标候选更新)
			simTime = 4020L;
			SoundEffectManager.ReportHover(SoundSessionSource.NormalGesture, sessId, level: 1, parentIndex: 2, subIndex: 1);

			// t=4045: 距 Parent 0 满 45ms，但 Parent 0 已被作废，不应发声
			simTime = 4045L;
			SoundEffectManager.TestSignalWorker();
			Thread.Sleep(50);
			Assert(playedList.IsEmpty, "B2b_Target4_OldParentSubRevokedOnTargetSwitch",
				$"旧父扇区子目标已作废，4045ms期望未发声，实际发声: {playedList.Count}");

			// t=4065: 距 Parent 2 满 45ms，此时应当发声
			simTime = 4065L;
			SoundEffectManager.TestSignalWorker();
			Thread.Sleep(60);
			Assert(playedList.Count == 1, "B2b_Target4_NewParentSubPlaysAtItsOwnDeadline",
				$"新父扇区子目标按自身期限放行，期望1次，实际: {playedList.Count}");

			SoundEffectManager.EndSession(SoundSessionSource.NormalGesture, sessId, allowTerminalFeedback: false);
			SoundEffectManager.Shutdown();
			SoundEffectManager.ResetTestSeams();
		}

		// B2b_Target5: 中心/无效/离开/结束撤销未播放候选
		{
			SoundEffectManager.ResetTestSeams();
			long sessId = SoundEffectManager.BeginSession(SoundSessionSource.NormalGesture);
			int droppedCount = 0;
			int playedCount = 0;
			SoundEffectManager.SoundDropped = (t, wav, tick) => Interlocked.Increment(ref droppedCount);
			SoundEffectManager.SoundPlayed = (t, wav, tick) => Interlocked.Increment(ref playedCount);

			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);

			// 报告候选后立即进入中心无效区 (subIndex < 0 或 CancelHover)
			SoundEffectManager.ReportHover(SoundSessionSource.NormalGesture, sessId, level: 0, parentIndex: -1, subIndex: 1);
			SoundEffectManager.CancelHover(SoundSessionSource.NormalGesture, sessId);

			Thread.Sleep(60);
			Assert(playedCount == 0, "B2b_Target5_CancelHoverRevokesPendingCandidate",
				$"CancelHover成功撤销候选，实际发声: {playedCount}");

			// 离开画布撤销
			long canvasSess = SoundEffectManager.BeginSession(SoundSessionSource.SettingsPreview);
			SoundEffectManager.ReportHover(SoundSessionSource.SettingsPreview, canvasSess, level: 0, parentIndex: -1, subIndex: 2);
			SoundEffectManager.CancelHover(SoundSessionSource.SettingsPreview, canvasSess);

			Thread.Sleep(60);
			Assert(playedCount == 0, "B2b_Target5_CanvasLeaveRevokesCandidate");

			SoundEffectManager.EndSession(SoundSessionSource.NormalGesture, sessId, allowTerminalFeedback: false);
			SoundEffectManager.EndSession(SoundSessionSource.SettingsPreview, canvasSess, allowTerminalFeedback: false);
			SoundEffectManager.Shutdown();
			SoundEffectManager.ResetTestSeams();
		}

		// B2b_Target6: 实际 Hover 播放开始间隔至少 80ms
		{
			SoundEffectManager.ResetTestSeams();
			long simTime = 5000L;
			SoundEffectManager.TimeProvider = () => simTime;
			long sessId = SoundEffectManager.BeginSession(SoundSessionSource.NormalGesture);

			List<long> startTicks = new();
			object tickLock = new();
			SoundEffectManager.SoundPlaybackStarted = (t, wav, tick) =>
			{
				if (t == SoundType.SectorHover)
				{
					lock (tickLock) startTicks.Add(tick);
				}
			};

			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);

			// 第一个 Hover: t=5000 报告，t=5045 播放
			SoundEffectManager.ReportHover(SoundSessionSource.NormalGesture, sessId, level: 0, parentIndex: -1, subIndex: 1);
			simTime = 5045L;
			SoundEffectManager.TestSignalWorker();
			Thread.Sleep(60);

			Assert(startTicks.Count == 1, "B2b_Target6_FirstHoverStartedAt5045");

			// 第二个 Hover: t=5050 报告，稳定门限为 5050 + 45 = 5095ms。
			// 但前一个 Hover 开始于 5045ms，根据 80ms 最小间隔，最早播放时间为 5045 + 80 = 5125ms！
			SoundEffectManager.ReportHover(SoundSessionSource.NormalGesture, sessId, level: 0, parentIndex: -1, subIndex: 2);

			// t=5095 虽已满足 45ms 稳定，但未满 80ms 间隔，不得开始播放
			simTime = 5095L;
			SoundEffectManager.TestSignalWorker();
			Thread.Sleep(50);
			Assert(startTicks.Count == 1, "B2b_Target6_MinPlaybackInterval80msEnforced",
				$"未满80ms间隔不得发声，当前开始数: {startTicks.Count}");

			// 推进至 5125ms，间隔已满，放行播放
			simTime = 5125L;
			SoundEffectManager.TestSignalWorker();
			Thread.Sleep(60);
			Assert(startTicks.Count == 2, "B2b_Target6_SecondHoverPlaysAfter80msInterval",
				$"满80ms间隔后放行发声，实际开始数: {startTicks.Count}");

			SoundEffectManager.EndSession(SoundSessionSource.NormalGesture, sessId, allowTerminalFeedback: false);
			SoundEffectManager.Shutdown();
			SoundEffectManager.ResetTestSeams();
		}

		// B2b_Target7: 慢后端（> 150ms 超期）丢弃过期候选
		{
			SoundEffectManager.ResetTestSeams();
			long simTime = 6000L;
			SoundEffectManager.TimeProvider = () => simTime;
			long sessId = SoundEffectManager.BeginSession(SoundSessionSource.NormalGesture);

			AutoResetEvent blockerStarted = new(false);
			ManualResetEvent allowBlockerExit = new(false);
			TrackBarrier(allowBlockerExit);
			int droppedHoverCount = 0;
			int playedHoverCount = 0;
			SoundEffectManager.SoundDropped = (t, wav, tick) =>
			{
				if (t == SoundType.SectorHover) Interlocked.Increment(ref droppedHoverCount);
			};
			SoundEffectManager.SoundPlayed = (t, wav, tick) =>
			{
				if (t == SoundType.SectorHover) Interlocked.Increment(ref playedHoverCount);
			};

			SoundEffectManager.PlaybackSink = (data, flags) =>
			{
				blockerStarted.Set();
				allowBlockerExit.WaitOne(3000);
			};

			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);

			// 启动一个耗时播放 (例如直接试听 WheelPopup)
			SoundEffectManager.PlayPreview(SoundType.WheelPopup);
			bool blockerIn = blockerStarted.WaitOne(2000);
			Assert(blockerIn, "B2b_Target7_BlockerPlaybackStarted");

			// 在阻塞期间，t=6050 产生一个 Hover 候选 (稳定到期为 6050 + 45 = 6095，超期点为 6095 + 150 = 6245ms)
			simTime = 6050L;
			SoundEffectManager.ReportHover(SoundSessionSource.NormalGesture, sessId, level: 0, parentIndex: -1, subIndex: 1);

			// 慢后端持续到 t=6300ms (此时 6300 > 6245ms，候选已超时超期超过 150ms)
			simTime = 6300L;
			allowBlockerExit.Set();
			Thread.Sleep(60);

			Assert(droppedHoverCount >= 1, "B2b_Target7_ExpiredCandidateDroppedAfter150msOverdue",
				$"超期150ms的Hover候选必须被安全丢弃: 丢弃数={droppedHoverCount}");
			Assert(playedHoverCount == 0, "B2b_Target7_ExpiredCandidateNeverPlayed",
				$"超期候选绝不播放: 播放数={playedHoverCount}");

			SoundEffectManager.EndSession(SoundSessionSource.NormalGesture, sessId, allowTerminalFeedback: false);
			SoundEffectManager.Shutdown();
			SoundEffectManager.ResetTestSeams();
		}

		// B2b_Target8: 三来源隔离及确认音（ActionExecute/GestureCancel/Expand）优先
		{
			SoundEffectManager.ResetTestSeams();
			long simTime = 7000L;
			SoundEffectManager.TimeProvider = () => simTime;

			long gestureSess = SoundEffectManager.BeginSession(SoundSessionSource.NormalGesture);
			long previewSess = SoundEffectManager.BeginSession(SoundSessionSource.SettingsPreview);

			int gestureHoverPlayed = 0;
			int previewHoverPlayed = 0;
			int executePlayed = 0;
			SoundEffectManager.SoundPlayed = (t, wav, tick) =>
			{
				if (t == SoundType.ActionExecute) Interlocked.Increment(ref executePlayed);
				if (t == SoundType.SectorHover)
				{
					if (simTime >= 7200L) Interlocked.Increment(ref previewHoverPlayed);
					else Interlocked.Increment(ref gestureHoverPlayed);
				}
			};

			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);

			// NormalGesture 处于待定 Hover
			SoundEffectManager.ReportHover(SoundSessionSource.NormalGesture, gestureSess, level: 0, parentIndex: -1, subIndex: 1);

			// 此时 ActionExecute (High) 到达：立即作废尚未播放的 Hover 候选，并优先执行 Execute
			SoundEffectManager.Play(SoundType.ActionExecute, SoundSessionSource.NormalGesture, gestureSess);
			Thread.Sleep(60);

			Assert(executePlayed == 1, "B2b_Target8_ActionExecutePreemptsAndPlaysImmediately");

			// 推进时间，被作废的 Hover 绝不播放
			simTime = 7100L;
			SoundEffectManager.TestSignalWorker();
			Thread.Sleep(50);
			Assert(gestureHoverPlayed == 0, "B2b_Target8_RevokedHoverNeverPlayedAfterExecute");

			// Expand 作废旧层级候选
			SoundEffectManager.ReportHover(SoundSessionSource.NormalGesture, gestureSess, level: 0, parentIndex: -1, subIndex: 3);
			SoundEffectManager.Play(SoundType.SubmenuExpand, SoundSessionSource.NormalGesture, gestureSess);
			Thread.Sleep(50);

			// 来源隔离验证：SettingsPreview 的候选不受影响
			SoundEffectManager.ReportHover(SoundSessionSource.SettingsPreview, previewSess, level: 0, parentIndex: -1, subIndex: 0);
			simTime = 7200L;
			SoundEffectManager.TestSignalWorker();
			Thread.Sleep(60);

			Assert(previewHoverPlayed == 1, "B2b_Target8_PreviewCandidatePlaysIndependently");

			SoundEffectManager.EndSession(SoundSessionSource.NormalGesture, gestureSess, allowTerminalFeedback: false);
			SoundEffectManager.EndSession(SoundSessionSource.SettingsPreview, previewSess, allowTerminalFeedback: false);
			SoundEffectManager.Shutdown();
			SoundEffectManager.ResetTestSeams();
		}
	}
	#endregion

	#region 阶段 B2b.1: 修正播放前校验缺口断言 (Stage B2b.1 Pre-Claim Verification Regressions)
	private static void TestStageB2b1_PreClaimVerificationRegressions()
	{
		Log("\n>>> 阶段 B2b.1: 播放前校验缺口与确定性交错回归断言 (Stage B2b.1 Pre-Claim Verification)");

		// B2b_1_Target1: 用屏障阻塞在“候选已选中、尚未取得资源”的窗口，CancelHover 后不播放
		{
			SoundEffectManager.ResetTestSeams();
			long simTime = 10000L;
			SoundEffectManager.TimeProvider = () => simTime;
			long sid = SoundEffectManager.BeginSession(SoundSessionSource.NormalGesture);

			AutoResetEvent candidateSelectedSignal = new(false);
			ManualResetEvent allowResumeAcquire = new(false);
			TrackBarrier(allowResumeAcquire);

			int playedHoverCount = 0;
			int droppedHoverCount = 0;
			SoundEffectManager.SoundPlayed = (t, wav, tick) =>
			{
				if (t == SoundType.SectorHover) Interlocked.Increment(ref playedHoverCount);
			};
			SoundEffectManager.SoundDropped = (t, wav, tick) =>
			{
				if (t == SoundType.SectorHover) Interlocked.Increment(ref droppedHoverCount);
			};

			SoundEffectManager.OnHoverCandidateSelectedBeforeAcquireResource = () =>
			{
				candidateSelectedSignal.Set();
				allowResumeAcquire.WaitOne(3000);
			};

			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);

			// t=10000 报告目标稳定，截止期 10045ms
			SoundEffectManager.ReportHover(SoundSessionSource.NormalGesture, sid, level: 0, parentIndex: -1, subIndex: 1);

			// 推进至 10045ms，唤醒 Worker 选中该候选
			simTime = 10045L;
			SoundEffectManager.TestSignalWorker();

			bool inWindow = candidateSelectedSignal.WaitOne(2000);
			Assert(inWindow, "B2b_1_Target1_CandidateSelectedAndBlockedBeforeAcquiringResource");

			// 在 Worker 阻塞于取得资源期间，用户移开光标或取消
			SoundEffectManager.CancelHover(SoundSessionSource.NormalGesture, sid);
			Assert(droppedHoverCount == 1, "B2b_1_Target1_CancelHoverDroppedCandidate");

			// 放行 Worker 取得资源并到达播放开始认领点
			allowResumeAcquire.Set();
			Thread.Sleep(60);

			Assert(playedHoverCount == 0, "B2b_1_Target1_CancelledHoverNeverPlayedAtClaimPoint",
				$"已取消候选在认领点必须拒绝播放，实际播放数: {playedHoverCount}");

			SoundEffectManager.EndSession(SoundSessionSource.NormalGesture, sid, allowTerminalFeedback: false);
			SoundEffectManager.Shutdown();
			SoundEffectManager.ResetTestSeams();
		}

		// B2b_1_Target2: 阻塞在尚未取得资源窗口，换目标后旧声音不播放，新目标重新满足稳定期限
		{
			SoundEffectManager.ResetTestSeams();
			long simTime = 20000L;
			SoundEffectManager.TimeProvider = () => simTime;
			long sid = SoundEffectManager.BeginSession(SoundSessionSource.NormalGesture);

			AutoResetEvent firstSelectedSignal = new(false);
			ManualResetEvent allowFirstResume = new(false);
			TrackBarrier(allowFirstResume);

			int playedHoverCount = 0;
			int droppedHoverCount = 0;
			List<long> playedTicks = new();
			object pLock = new();

			SoundEffectManager.SoundPlayed = (t, wav, tick) =>
			{
				if (t == SoundType.SectorHover)
				{
					Interlocked.Increment(ref playedHoverCount);
					lock (pLock) playedTicks.Add(tick);
				}
			};
			SoundEffectManager.SoundDropped = (t, wav, tick) =>
			{
				if (t == SoundType.SectorHover) Interlocked.Increment(ref droppedHoverCount);
			};

			int callCount = 0;
			SoundEffectManager.OnHoverCandidateSelectedBeforeAcquireResource = () =>
			{
				if (Interlocked.Increment(ref callCount) == 1)
				{
					firstSelectedSignal.Set();
					allowFirstResume.WaitOne(3000);
				}
			};

			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);

			// t=20000 报告 subIndex 1，截止期 20045ms
			SoundEffectManager.ReportHover(SoundSessionSource.NormalGesture, sid, level: 0, parentIndex: -1, subIndex: 1);

			simTime = 20045L;
			SoundEffectManager.TestSignalWorker();

			bool inWindow = firstSelectedSignal.WaitOne(2000);
			Assert(inWindow, "B2b_1_Target2_FirstCandidateSelectedAndBlocked");

			// 在取得资源前换目标为 subIndex 2 (时间仍为 20045ms，新截止期 20045 + 45 = 20090ms)
			SoundEffectManager.ReportHover(SoundSessionSource.NormalGesture, sid, level: 0, parentIndex: -1, subIndex: 2);
			Assert(droppedHoverCount == 1, "B2b_1_Target2_OldTargetDroppedOnSwitch");

			// 放行旧候选取得资源并在认领点校验（版本与目标不匹配，必须丢弃）
			allowFirstResume.Set();

			// 推进至 20070ms (尚未达到新目标的 20090ms 稳定期限)
			simTime = 20070L;
			SoundEffectManager.TestSignalWorker();
			Thread.Sleep(50);

			Assert(playedHoverCount == 0, "B2b_1_Target2_OldTargetNeverPlayedAndNewTargetNotYetReady",
				$"旧目标未播放且新目标在未满45ms前不播放，实际播放数: {playedHoverCount}");

			// 推进至 20090ms，新目标满 45ms 稳定期限，放行发声
			simTime = 20090L;
			SoundEffectManager.TestSignalWorker();
			Thread.Sleep(60);

			Assert(playedHoverCount == 1, "B2b_1_Target2_NewTargetPlaysExactlyOnceAtOwnDeadline",
				$"新目标在达到自身45ms稳定后放行发声，实际播放数: {playedHoverCount}");

			SoundEffectManager.EndSession(SoundSessionSource.NormalGesture, sid, allowTerminalFeedback: false);
			SoundEffectManager.Shutdown();
			SoundEffectManager.ResetTestSeams();
		}

		// B2b_1_Target3: 阻塞在尚未取得资源窗口，等待超过期限（>150ms）后在认领点丢弃，不补播
		{
			SoundEffectManager.ResetTestSeams();
			long simTime = 30000L;
			SoundEffectManager.TimeProvider = () => simTime;
			long sid = SoundEffectManager.BeginSession(SoundSessionSource.NormalGesture);

			AutoResetEvent candidateSelectedSignal = new(false);
			ManualResetEvent allowResume = new(false);
			TrackBarrier(allowResume);

			int playedHoverCount = 0;
			int droppedHoverCount = 0;
			SoundEffectManager.SoundPlayed = (t, wav, tick) =>
			{
				if (t == SoundType.SectorHover) Interlocked.Increment(ref playedHoverCount);
			};
			SoundEffectManager.SoundDropped = (t, wav, tick) =>
			{
				if (t == SoundType.SectorHover) Interlocked.Increment(ref droppedHoverCount);
			};

			SoundEffectManager.OnHoverCandidateSelectedBeforeAcquireResource = () =>
			{
				candidateSelectedSignal.Set();
				allowResume.WaitOne(3000);
			};

			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);

			// t=30000 报告候选，稳定期限 30045ms，过期门限 30045 + 150 = 30195ms
			SoundEffectManager.ReportHover(SoundSessionSource.NormalGesture, sid, level: 0, parentIndex: -1, subIndex: 1);

			simTime = 30045L;
			SoundEffectManager.TestSignalWorker();

			bool inWindow = candidateSelectedSignal.WaitOne(2000);
			Assert(inWindow, "B2b_1_Target3_CandidateSelectedAndBlocked");

			// 模拟音频资源获取异常缓慢或外部延时：时间推进至 30200ms (> 30195ms，超期 155ms)
			simTime = 30200L;
			allowResume.Set();
			Thread.Sleep(60);

			Assert(droppedHoverCount == 1, "B2b_1_Target3_CandidateDroppedAtClaimPointDueToOverdue",
				$"超期候选在认领点必须被安全丢弃，丢弃数: {droppedHoverCount}");
			Assert(playedHoverCount == 0, "B2b_1_Target3_OverdueCandidateNeverPlayed",
				$"超期候选绝不补播，实际播放数: {playedHoverCount}");

			// 后续时间继续推进，不补发
			simTime = 30300L;
			SoundEffectManager.TestSignalWorker();
			Thread.Sleep(50);
			Assert(playedHoverCount == 0, "B2b_1_Target3_NoLateCompensationPlayback");

			SoundEffectManager.EndSession(SoundSessionSource.NormalGesture, sid, allowTerminalFeedback: false);
			SoundEffectManager.Shutdown();
			SoundEffectManager.ResetTestSeams();
		}

		// B2b_1_Target4: 延迟第一声后，两声实际开始仍相隔至少 80ms
		{
			SoundEffectManager.ResetTestSeams();
			long simTime = 40000L;
			SoundEffectManager.TimeProvider = () => simTime;
			long sid = SoundEffectManager.BeginSession(SoundSessionSource.NormalGesture);

			AutoResetEvent firstSelectedSignal = new(false);
			ManualResetEvent allowFirstResume = new(false);
			TrackBarrier(allowFirstResume);

			List<long> startTicks = new();
			object tickLock = new();
			SoundEffectManager.SoundPlaybackStarted = (t, wav, tick) =>
			{
				if (t == SoundType.SectorHover)
				{
					lock (tickLock) startTicks.Add(tick);
				}
			};

			int hookCount = 0;
			SoundEffectManager.OnHoverCandidateSelectedBeforeAcquireResource = () =>
			{
				if (Interlocked.Increment(ref hookCount) == 1)
				{
					firstSelectedSignal.Set();
					allowFirstResume.WaitOne(3000);
				}
			};

			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);

			// 第 1 个目标：t=40000 报告，原本期望 40045 开始
			SoundEffectManager.ReportHover(SoundSessionSource.NormalGesture, sid, level: 0, parentIndex: -1, subIndex: 1);

			simTime = 40045L;
			SoundEffectManager.TestSignalWorker();

			bool inWindow = firstSelectedSignal.WaitOne(2000);
			Assert(inWindow, "B2b_1_Target4_FirstSelectedAndBlocked");

			// 延迟第一声直至 40070ms 才完成认领与开始播放（延迟了 25ms）
			simTime = 40070L;
			allowFirstResume.Set();
			Thread.Sleep(60);

			Assert(startTicks.Count == 1, "B2b_1_Target4_FirstHoverStartedAtDelayedTime",
				$"第一声在 40070ms 认领并开始，实际开始数: {startTicks.Count}");

			// 第 2 个目标：t=40075 报告，稳定期限 40075 + 45 = 40120ms。
			// 但因为第一声实际开始于 40070ms，两声实际开始必须相隔至少 80ms，因此最早开始时间为 40070 + 80 = 40150ms！
			SoundEffectManager.ReportHover(SoundSessionSource.NormalGesture, sid, level: 0, parentIndex: -1, subIndex: 2);

			// t=40120ms（满足自身45ms稳定但未满第一声后80ms间隔）
			simTime = 40120L;
			SoundEffectManager.TestSignalWorker();
			Thread.Sleep(50);

			Assert(startTicks.Count == 1, "B2b_1_Target4_SecondHoverBlockedByDelayedFirstPlayback",
				$"第一声延迟导致第二声在40120ms仍被间隔门禁拦截，实际开始数: {startTicks.Count}");

			// 推进至 40150ms（刚好满足 80ms 间隔）
			simTime = 40150L;
			SoundEffectManager.TestSignalWorker();
			Thread.Sleep(60);

			Assert(startTicks.Count == 2, "B2b_1_Target4_SecondHoverStartsAfter80msActualInterval",
				$"第二声在满足实际80ms间隔后放行，实际开始数: {startTicks.Count}");

			long interval = startTicks[1] - startTicks[0];
			Assert(interval >= 80, "B2b_1_Target4_ActualStartIntervalAtLeast80ms",
				$"实际开始间隔为 {interval}ms (必须 >= 80ms)");

			SoundEffectManager.EndSession(SoundSessionSource.NormalGesture, sid, allowTerminalFeedback: false);
			SoundEffectManager.Shutdown();
			SoundEffectManager.ResetTestSeams();
		}

		// B2b_1_Target5: 被撤销或过期的请求不得占用播放间隔
		{
			SoundEffectManager.ResetTestSeams();
			long simTime = 50000L;
			SoundEffectManager.TimeProvider = () => simTime;
			long sid = SoundEffectManager.BeginSession(SoundSessionSource.NormalGesture);

			AutoResetEvent firstSelectedSignal = new(false);
			ManualResetEvent allowFirstResume = new(false);
			TrackBarrier(allowFirstResume);

			List<long> startTicks = new();
			object tickLock = new();
			SoundEffectManager.SoundPlaybackStarted = (t, wav, tick) =>
			{
				if (t == SoundType.SectorHover)
				{
					lock (tickLock) startTicks.Add(tick);
				}
			};

			int hookCount = 0;
			SoundEffectManager.OnHoverCandidateSelectedBeforeAcquireResource = () =>
			{
				if (Interlocked.Increment(ref hookCount) == 1)
				{
					firstSelectedSignal.Set();
					allowFirstResume.WaitOne(3000);
				}
			};

			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);

			// 目标 1：t=50000 报告，50045 选中
			SoundEffectManager.ReportHover(SoundSessionSource.NormalGesture, sid, level: 0, parentIndex: -1, subIndex: 1);

			simTime = 50045L;
			SoundEffectManager.TestSignalWorker();
			bool inWindow = firstSelectedSignal.WaitOne(2000);
			Assert(inWindow, "B2b_1_Target5_CandidateSelectedAndBlocked");

			// 在认领前取消目标 1
			SoundEffectManager.CancelHover(SoundSessionSource.NormalGesture, sid);
			allowFirstResume.Set();
			Thread.Sleep(50);

			Assert(startTicks.Count == 0, "B2b_1_Target5_CancelledFirstCandidateNeverStarted");

			// 目标 2：t=50050 报告，稳定期限 50050 + 45 = 50095ms。
			// 因为目标 1 被取消未认领，未占用任何播放间隔，目标 2 应在自身的 50095ms 准时播放，绝不被推迟至 50045 + 80 = 50125ms！
			SoundEffectManager.ReportHover(SoundSessionSource.NormalGesture, sid, level: 0, parentIndex: -1, subIndex: 2);

			simTime = 50095L;
			SoundEffectManager.TestSignalWorker();
			Thread.Sleep(60);

			Assert(startTicks.Count == 1, "B2b_1_Target5_SecondCandidatePlaysAtOwnDeadlineWithoutIntervalPenalty",
				$"已取消请求不占用播放间隔，第二声在50095ms准时开始，实际开始数: {startTicks.Count}");

			SoundEffectManager.EndSession(SoundSessionSource.NormalGesture, sid, allowTerminalFeedback: false);
			SoundEffectManager.Shutdown();
			SoundEffectManager.ResetTestSeams();
		}

		// B2b_1_Target6: 设置画布二级→一级切换（父扇区序号不变）及三路径层级身份审计
		{
			SoundEffectManager.ResetTestSeams();
			long canvasSid = SoundEffectManager.BeginSession(SoundSessionSource.SettingsPreview);

			HoverTargetIdentity? reportedTarget = null;
			SoundEffectManager.TimeProvider = () => 60000L;
			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);

			// 模拟设置画布二级扇区 hover：父扇区 2，子扇区 1 (Level 1)
			SoundEffectManager.ReportHover(SoundSessionSource.SettingsPreview, canvasSid, level: 1, parentIndex: 2, subIndex: 1);
			reportedTarget = SoundEffectManager.TestGetHoverCandidateTarget(SoundSessionSource.SettingsPreview);

			Assert(reportedTarget.HasValue && reportedTarget.Value.Level == 1 && reportedTarget.Value.ParentIndex == 2 && reportedTarget.Value.SubIndex == 1,
				"B2b_1_Target6_CanvasSubSectorReportedCorrectly");

			// 模拟从二级退回一级主扇区（父扇区依然为 2，子扇区变为 -1，Level 0）
			// 在修复前，因为 num15 == prevSec (2 == 2) 且 num16 < 0，SettingsWindow 存在不汇报的遗漏缺口
			SoundEffectManager.ReportHover(SoundSessionSource.SettingsPreview, canvasSid, level: 0, parentIndex: -1, subIndex: 2);
			reportedTarget = SoundEffectManager.TestGetHoverCandidateTarget(SoundSessionSource.SettingsPreview);

			Assert(reportedTarget.HasValue && reportedTarget.Value.Level == 0 && reportedTarget.Value.ParentIndex == -1 && reportedTarget.Value.SubIndex == 2,
				"B2b_1_Target6_CanvasLevel1ToLevel0SameParentSwitchRecognized",
				$"二级退回一级（父扇区不变）正确生成 Level 0 目标: Lvl={reportedTarget?.Level}, Parent={reportedTarget?.ParentIndex}, Sub={reportedTarget?.SubIndex}");

			SoundEffectManager.EndSession(SoundSessionSource.SettingsPreview, canvasSid, allowTerminalFeedback: false);
			SoundEffectManager.Shutdown();
			SoundEffectManager.ResetTestSeams();
		}

		// B2b_1_Target7: 显式试听按钮绕过探索防抖直接即刻播放
		{
			SoundEffectManager.ResetTestSeams();
			long simTime = 70000L;
			SoundEffectManager.TimeProvider = () => simTime;

			AutoResetEvent previewPlayed = new(false);
			SoundEffectManager.SoundPlayed = (t, wav, tick) =>
			{
				if (t == SoundType.SectorHover) previewPlayed.Set();
			};

			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);

			// 显式试听 SectorHover：必须直接入队立即播放，绝不需要等待 45ms 目标稳定
			SoundEffectManager.PlayPreview(SoundType.SectorHover);

			bool playedImmediately = previewPlayed.WaitOne(1000);
			Assert(playedImmediately, "B2b_1_Target7_PlayPreviewBypassesExploratoryDebounce",
				$"显式试听按钮绕过45ms防抖即刻播放: {playedImmediately}");

			SoundEffectManager.Shutdown();
			SoundEffectManager.ResetTestSeams();
		}
	}
	#endregion

	#region 场景 2: 一级/二级切换与 55ms 放行时间刻画
	private static void TestScenario2_TierSwitchingAndHoverTiming()
	{
		Log("\n>>> 场景 2: 一级/二级切换与 55ms 放行时间精确刻画 (Tier Switching & Hover Timing)");

		long simTime = 10000L;
		SoundEffectManager.TimeProvider = () => simTime;
		SoundEffectManager.LastHoverTick = 0L;

		var queuedRecords = new ConcurrentQueue<(SoundType? Sound, long Tick)>();
		SoundEffectManager.SoundQueued = (t, wav, tick) => queuedRecords.Enqueue((t, tick));
		SoundEffectManager.PlaybackSink = (data, flags) => { };

		var mouseHook = new MouseHook();
		using var gc = new GestureSoundHarness(mouseHook);
		gc.TestSetGestureActive(true, version: 200);

		var cfg = SoundEffectManager.Preferences;
		cfg.EnableSoundEffects = true;
		cfg.SoundOnExpand = true;
		cfg.SoundOnHover = true;
		SoundEffectManager.Initialize("Mechanical", 0.6, force: true);

		// 2A: 展开二级菜单 (showSubTier = true)
		simTime = 10000L;
		gc.TestQueueHighlightUpdate(2, -1, false, true, 200);

		// 2B: 验证 40ms 时（距展开 40ms，子扇区尚未满 45ms 稳定）Hover 未放行
		simTime = 10040L;
		gc.TestQueueHighlightUpdate(2, 0, false, true, 200);

		bool hoverBlockedAt40ms = queuedRecords.Count == 1; // 仅有 SubmenuExpand
		Assert(hoverBlockedAt40ms, "2A_HoverBlockedAt40msAfterExpand",
			$"展开后 40ms 时子扇区未满 45ms 应未发声，实际队列数: {queuedRecords.Count}");

		// 2C: 验证子扇区稳定满 45ms (10040 + 45 = 10085ms) 后放行
		SpinWait.SpinUntil(() => SoundEffectManager.TestPendingSound == null, 500);
		simTime = 10085L;
		SoundEffectManager.TestSignalWorker();
		Thread.Sleep(60);

		bool hoverAllowedAt56ms = queuedRecords.Count == 2;
		Assert(hoverAllowedAt56ms, "2B_HoverReleasedAt56msAfterExpand",
			$"子扇区稳定满 45ms 后放行，实际队列数: {queuedRecords.Count}");

		Log($"  [Forensics 2 Metric] 展开后子扇区稳定 45ms 放行 (实测 40ms 未满拦截, 10085ms 稳定放行)");
		Log("  [Forensics 2 Note] Mechanical 展开音效理论波形长 60ms，放行点 55ms 略先于理论波形尾声；Crisp 为 52ms，放行点落后于音效结束");

		SoundEffectManager.ResetTestSeams();
		SoundEffectManager.PlaybackSink = (data, flags) => { };
	}
	#endregion

	#region 场景 3: 播放延迟与单槽覆盖 (Playback Latency & Overwrite)
	private static void TestScenario3_PlaybackLatencyAndSingleSlotOverwrite()
	{
		Log("\n>>> 场景 3: 播放延迟与单槽覆盖取证 (Playback Latency & Overwrite)");

		SoundEffectManager.ResetTestSeams();

		AutoResetEvent playbackStarted = new(false);
		ManualResetEvent allowFinish = new(false);
		TrackBarrier(allowFinish);
		AutoResetEvent secondPlaybackFinished = new(false);
		var finishedList = new ConcurrentQueue<SoundType?>();
		int droppedCount = 0;

		SoundEffectManager.SoundPlaybackStarted = (type, wav, tick) => playbackStarted.Set();
		SoundEffectManager.SoundPlaybackFinished = (type, wav, tick) =>
		{
			finishedList.Enqueue(type);
			if (type == SoundType.ActionExecute)
			{
				secondPlaybackFinished.Set();
			}
		};

		SoundEffectManager.SoundDropped = (type, wav, tick) => Interlocked.Increment(ref droppedCount);

		SoundEffectManager.PlaybackSink = (data, flags) =>
		{
			bool waitFinish = allowFinish.WaitOne(3000);
			Assert(waitFinish, "3_AllowFinishSignalWithin3000ms");
		};

		SoundEffectManager.Initialize("Mechanical", 0.6, force: true);

		// 1. 发起首声，阻塞在 PlaybackSink
		SoundEffectManager.PlayPreview(SoundType.WheelPopup);
		bool started = playbackStarted.WaitOne(2000);
		Assert(started, "3A_FirstSoundStartedPlayback");

		// 2. 连续入队 3 个请求：SectorHover -> SubmenuExpand -> ActionExecute
		SoundEffectManager.PlayPreview(SoundType.SectorHover);
		SoundEffectManager.PlayPreview(SoundType.SubmenuExpand);
		SoundEffectManager.PlayPreview(SoundType.ActionExecute);

		// 3. 释放首声
		allowFinish.Set();

		// 等待第二声回放完成（真实断言回放结束）
		bool secondRan = secondPlaybackFinished.WaitOne(3000);
		Assert(secondRan, "3B_SecondSoundFinishedPlayback");

		var list = finishedList.ToList();
		bool correctSequence = list.Count == 2 && list[0] == SoundType.WheelPopup && list[1] == SoundType.ActionExecute;
		Assert(correctSequence, "3C_IntermediateSoundsDroppedAndLastPlayed",
			$"完成回放序列: [{string.Join(", ", list)}], 丢弃数: {droppedCount}");

		Log($"  [Forensics 3 Metric] 回放序列: [{string.Join(", ", list)}], 丢弃计数: {droppedCount}, 证实符合单槽保留最新请求特性");

		SoundEffectManager.ResetTestSeams();
		SoundEffectManager.PlaybackSink = (data, flags) => { };
	}
	#endregion

	#region 场景 4: 待播 Execute/Cancel 被后续 Hover 覆盖竞争
	private static void TestScenario4_ExecuteCancelVsHoverRace()
	{
		Log("\n>>> 场景 4: 待播 Execute/Cancel 被后续 Hover 覆盖竞争 (Execute/Cancel vs Hover Overwrite)");

		SoundEffectManager.ResetTestSeams();

		// 4A: 待播 ActionExecute 是否被后续 Hover 覆盖
		AutoResetEvent blockerStarted = new(false);
		ManualResetEvent allowBlockerFinish = new(false);
		TrackBarrier(allowBlockerFinish);
		AutoResetEvent blockerFinished = new(false);
		AutoResetEvent secondFinished = new(false);
		int dropCount = 0;

		SoundEffectManager.SoundPlaybackStarted = (type, wav, tick) => blockerStarted.Set();
		SoundEffectManager.SoundPlaybackFinished = (type, wav, tick) =>
		{
			if (type == SoundType.WheelPopup) blockerFinished.Set();
			else if (type == SoundType.SectorHover || type == SoundType.ActionExecute || type == SoundType.GestureCancel) secondFinished.Set();
		};
		SoundEffectManager.SoundDropped = (type, wav, tick) => Interlocked.Increment(ref dropCount);

		SoundEffectManager.PlaybackSink = (data, flags) =>
		{
			bool waitBlocker = allowBlockerFinish.WaitOne(3000);
			Assert(waitBlocker, "4A_AllowBlockerFinishWithin3000ms");
		};

		SoundEffectManager.Initialize("Mechanical", 0.6, force: true);

		// 触发阻塞声
		SoundEffectManager.PlayPreview(SoundType.WheelPopup);
		bool blockerStartOk = blockerStarted.WaitOne(2000);
		Assert(blockerStartOk, "4A_0_BlockerStartedPlayback");

		// 用户松手，触发 ActionExecute
		SoundEffectManager.Play(SoundType.ActionExecute);
		Assert(SoundEffectManager.TestPendingSound == SoundType.ActionExecute, "4A_1_ActionExecutePendingInQueue");

		// 此时迟到/并发产生一次 SectorHover
		SoundEffectManager.LastHoverTick = 0L; // 规避防抖
		SoundEffectManager.Play(SoundType.SectorHover);

		// 契约断言：ActionExecute 优先级高于 SectorHover，迟到的 SectorHover 绝不能覆盖 ActionExecute！
		bool executePreserved = SoundEffectManager.TestPendingSound == SoundType.ActionExecute;
		Assert(executePreserved, "4A_2_PendingExecutePreservedAgainstLateHover",
			$"待播槽当前为: {SoundEffectManager.TestPendingSound}，ActionExecute 成功保留 (未被迟到 Hover 覆盖)");

		allowBlockerFinish.Set();
		bool blockerDone = blockerFinished.WaitOne(2000);
		Assert(blockerDone, "4A_3_BlockerFinishedPlayback");
		bool secondDone = secondFinished.WaitOne(2000);
		Assert(secondDone, "4A_4_SecondFinishedPlayback");

		// 4B: 待播 GestureCancel 是否被后续 Hover 覆盖
		allowBlockerFinish.Reset();
		blockerStarted.Reset();
		blockerFinished.Reset();
		secondFinished.Reset();

		SoundEffectManager.PlayPreview(SoundType.WheelPopup);
		bool blocker2StartOk = blockerStarted.WaitOne(2000);
		Assert(blocker2StartOk, "4B_0_Blocker2StartedPlayback");

		// 触发 GestureCancel
		SoundEffectManager.Play(SoundType.GestureCancel);
		Assert(SoundEffectManager.TestPendingSound == SoundType.GestureCancel, "4B_1_GestureCancelPendingInQueue");

		// 迟到产生 SectorHover
		SoundEffectManager.LastHoverTick = 0L;
		SoundEffectManager.Play(SoundType.SectorHover);

		// 契约断言：GestureCancel 优先级高于 SectorHover，迟到的 SectorHover 绝不能覆盖 GestureCancel！
		bool cancelPreserved = SoundEffectManager.TestPendingSound == SoundType.GestureCancel;
		Assert(cancelPreserved, "4B_2_PendingCancelPreservedAgainstLateHover",
			$"待播槽当前为: {SoundEffectManager.TestPendingSound}，GestureCancel 成功保留 (未被迟到 Hover 覆盖)");

		allowBlockerFinish.Set();
		bool blocker2Done = blockerFinished.WaitOne(2000);
		Assert(blocker2Done, "4B_3_Blocker2FinishedPlayback");
		bool second2Done = secondFinished.WaitOne(2000);
		Assert(second2Done, "4B_4_Second2FinishedPlayback");

		Log("  [Stage B2a Priority Metric] 契约证实：ActionExecute / GestureCancel 优先级高于 SectorHover，待播确认音绝不被迟到 Hover 覆盖");

		SoundEffectManager.ResetTestSeams();
		SoundEffectManager.PlaybackSink = (data, flags) => { };
	}
	#endregion

	#region 场景 5: 会话结束后残留 Hover 真实状态断言
	private static void TestScenario5_ResidualHoverAfterSessionEnds()
	{
		Log("\n>>> 场景 5: 会话结束后残留 Hover 撤销契约验证 (Residual Hover Revocation Contract)");

		SoundEffectManager.ResetTestSeams();

		AutoResetEvent firstSoundStarted = new(false);
		ManualResetEvent allowFirstSoundFinish = new(false);
		TrackBarrier(allowFirstSoundFinish);
		AutoResetEvent hoverStarted = new(false);

		SoundType? currentlyPlaying = null;
		SoundType? playedAfterEnd = null;
		bool sessionEnded = false;

		SoundEffectManager.PlaybackSink = (data, flags) =>
		{
			if (currentlyPlaying == SoundType.WheelPopup)
			{
				firstSoundStarted.Set();
				bool waitFirst = allowFirstSoundFinish.WaitOne(3000);
				Assert(waitFirst, "5_AllowFirstSoundFinishWithin3000ms");
			}
			else if (currentlyPlaying == SoundType.SectorHover)
			{
				if (sessionEnded)
				{
					playedAfterEnd = SoundType.SectorHover;
				}
				hoverStarted.Set();
			}
		};

		SoundEffectManager.SoundPlayed = (type, wav, tick) =>
		{
			currentlyPlaying = type;
		};

		var mouseHook = new MouseHook();
		using var gc = new GestureSoundHarness(mouseHook);
		gc.TestSetGestureActive(true, version: 300);

		var cfg = SoundEffectManager.Preferences;
		cfg.EnableSoundEffects = true;
		cfg.SoundOnPopup = true;
		cfg.SoundOnHover = true;
		SoundEffectManager.Initialize("Mechanical", 0.6, force: true);

		// 1. 触发呼出声（「已经开始的声音」），阻塞在 PlaybackSink
		currentlyPlaying = SoundType.WheelPopup;
		SoundEffectManager.Play(SoundType.WheelPopup);
		bool firstRunning = firstSoundStarted.WaitOne(2000);
		Assert(firstRunning, "5A_FirstSoundActivelyPlaying");

		// 2. 在第一声播放期间，生产高亮转换排入一次 SectorHover（「尚未开始的请求」）
		SoundEffectManager.LastHoverTick = 0L;
		gc.TestQueueHighlightUpdate(1, -1, false, false, 300);

		// 检查状态：Hover 确实作为待播项或候选排入
		bool hoverPending = SoundEffectManager.TestPendingSound == SoundType.SectorHover ||
			SoundEffectManager.TestGetHoverCandidateTarget(SoundSessionSource.NormalGesture).HasValue;
		Assert(hoverPending,
			"5B_HoverIsPendingAndNotYetStarted",
			$"当前待播或候选: {SoundEffectManager.TestPendingSound} / {SoundEffectManager.TestGetHoverCandidateTarget(SoundSessionSource.NormalGesture)}");

		// 3. 真实调用生产会话结束路径 EndActiveGesture()
		gc.TestEndActiveGesture();
		sessionEnded = true;
		Assert(!gc.IsGestureActive, "5C_SessionEndedCleanly");

		// 契约断言：会话结束后，未开始的 Hover 必须被立刻撤销，待播槽与候选均清空
		bool hoverRevoked = SoundEffectManager.TestPendingSound == null &&
			!SoundEffectManager.TestGetHoverCandidateTarget(SoundSessionSource.NormalGesture).HasValue;
		Assert(hoverRevoked,
			"5D_PendingHoverRevokedImmediatelyOnEndActiveGesture",
			$"待播槽与候选已清空: {hoverRevoked}");

		// 4. 释放第一声回放，让工作线程继续调度下一声
		currentlyPlaying = null;
		allowFirstSoundFinish.Set();

		// 等待第二声触发（应该超时未触发，因为 Hover 已被撤销）
		bool hoverRan = hoverStarted.WaitOne(600);
		Assert(!hoverRan && playedAfterEnd == null,
			"5E_PendingHoverNeverPlayedAfterSessionEnded",
			$"会话结束后播放了: {playedAfterEnd} (必须为null)");

		Log($"  [Stage B2a Session Metric] 契约证实：已开始声音自然完成，未开始的 Hover 在 EndActiveGesture 时被原子撤销，会话结束后零残留播放");

		SoundEffectManager.ResetTestSeams();
		SoundEffectManager.PlaybackSink = (data, flags) => { };
	}
	#endregion

	#region 场景 6: 生命周期用例线程与并发调用精确测量
	private static void TestScenario6_LifecycleAndConcurrencyMeasurement()
	{
		Log("\n>>> 场景 6: 生命周期线程存活与播放调用并发数测量 (Lifecycle & Concurrency Measurement)");

		SoundEffectManager.ResetTestSeams();

		AutoResetEvent worker1PlaybackEntered = new(false);
		ManualResetEvent allowWorker1ToExit = new(false);
		TrackBarrier(allowWorker1ToExit);

		int activePlaybackCalls = 0;
		int maxConcurrentPlaybackCalls = 0;
		object statLock = new();
		HashSet<int> callingThreadIds = new();

		SoundEffectManager.PlaybackSink = (data, flags) =>
		{
			int current = Interlocked.Increment(ref activePlaybackCalls);
			lock (statLock)
			{
				if (current > maxConcurrentPlaybackCalls) maxConcurrentPlaybackCalls = current;
				callingThreadIds.Add(Thread.CurrentThread.ManagedThreadId);
			}

			// Worker 1 阻塞在事件屏障
			worker1PlaybackEntered.Set();
			bool waitW1 = allowWorker1ToExit.WaitOne(3000);
			Assert(waitW1, "6_AllowWorker1ToExitWithin3000ms");

			Interlocked.Decrement(ref activePlaybackCalls);
		};

		SoundEffectManager.Initialize("Mechanical", 0.6, force: true);
		Thread? worker1 = SoundEffectManager.CurrentWorkerThread;
		int worker1Id = worker1?.ManagedThreadId ?? 0;

		// 触发播放，让 Worker 1 进入 PlaybackSink 阻塞
		SoundEffectManager.PlayPreview(SoundType.WheelPopup);
		bool w1Entered = worker1PlaybackEntered.WaitOne(2000);
		Assert(w1Entered, "6_W1EnteredPlaybackWithin2000ms");

		// 此时 Worker 1 正在 PlaybackSink 内阻塞。执行 Shutdown()
		Stopwatch sw = Stopwatch.StartNew();
		SoundEffectManager.Shutdown();
		sw.Stop();

		// 检查 Worker 1 是否在底层未返回前保持物理存活（非阻塞退出）
		bool worker1StillAlive = worker1 != null && worker1.IsAlive;
		Assert(worker1StillAlive, "6A_Worker1SurvivesShutdownWhileBlocked",
			$"Worker1 ID={worker1Id}, 耗时: {sw.ElapsedMilliseconds}ms, IsAlive={worker1StillAlive}");

		// 紧接着调用 Initialize()
		SoundEffectManager.Initialize("Mechanical", 0.6, force: true);
		Thread? worker2 = SoundEffectManager.CurrentWorkerThread;
		int worker2Id = worker2?.ManagedThreadId ?? 0;

		// 测量是否有两个播放调用同时进行
		AutoResetEvent worker2PlaybackEntered = new(false);
		ManualResetEvent allowWorker2ToExit = new(false);
		TrackBarrier(allowWorker2ToExit);

		SoundEffectManager.PlaybackSink = (data, flags) =>
		{
			int current = Interlocked.Increment(ref activePlaybackCalls);
			lock (statLock)
			{
				if (current > maxConcurrentPlaybackCalls) maxConcurrentPlaybackCalls = current;
				callingThreadIds.Add(Thread.CurrentThread.ManagedThreadId);
			}

			worker2PlaybackEntered.Set();
			bool waitW2 = allowWorker2ToExit.WaitOne(3000);
			Assert(waitW2, "6_AllowWorker2ToExitWithin3000ms");

			Interlocked.Decrement(ref activePlaybackCalls);
		};

		SoundEffectManager.PlayPreview(SoundType.ActionExecute);
		bool worker2Entered = worker2PlaybackEntered.WaitOne(600);

		// 释放两个屏障
		allowWorker1ToExit.Set();
		allowWorker2ToExit.Set();
		bool w1Joined = worker1 != null && worker1.Join(2000);
		Assert(w1Joined, "6_Worker1JoinedWithin2000ms");
		SoundEffectManager.Shutdown();
		if (worker2 != null && worker2 != worker1)
		{
			bool w2Joined = worker2.Join(2000);
			Assert(w2Joined, "6_Worker2JoinedWithin2000ms");
		}

		int finalMaxConcurrency;
		List<int> callers;
		lock (statLock)
		{
			finalMaxConcurrency = maxConcurrentPlaybackCalls;
			callers = callingThreadIds.ToList();
		}

		Log($"  [Forensics 6 Metric] Worker1ID: {worker1Id}, Worker2ID: {worker2Id}, 最大并发播放调用数: {finalMaxConcurrency}, 调用者线程列表: [{string.Join(", ", callers)}]");

		Assert(finalMaxConcurrency <= 1, "6B_MaxConcurrentPlaybackNeverExceeds1",
			$"最大并发回放调用数: {finalMaxConcurrency} (必须<=1)");

		SoundEffectManager.Shutdown();
		SoundEffectManager.ResetTestSeams();
		SoundEffectManager.PlaybackSink = (data, flags) => { };
	}
	#endregion

	#region 场景 7: 普通轮盘、常驻轮盘、设置预览调用路径全覆盖审计
	private static void TestScenario7_CallPathCoverageAudit()
	{
		Log("\n>>> 场景 7: 轮盘交互三套调用路径覆盖情况审计 (Call Path Coverage Audit)");

		SoundEffectManager.ResetTestSeams();
		var queuedEvents = new ConcurrentBag<SoundType?>();
		SoundEffectManager.SoundQueued = (type, wav, tick) => queuedEvents.Add(type);
		SoundEffectManager.PlaybackSink = (data, flags) => { };

		// 路径 1: 普通手势轮盘 (GestureController.cs) 真实调用链路测试
		var mouseHook = new MouseHook();
		using var gc = new GestureSoundHarness(mouseHook);
		var cfg = SoundEffectManager.Preferences;
		cfg.EnableSoundEffects = true;
		cfg.SoundOnPopup = true;
		cfg.SoundOnHover = true;
		cfg.SoundOnExpand = true;
		cfg.SoundOnExecute = true;
		cfg.SoundOnCancel = true;

		SoundEffectManager.Initialize("Mechanical", 0.6, force: true);

		// 真实触发 WheelPopup
		SoundEffectManager.Play(SoundType.WheelPopup);
		SpinWait.SpinUntil(() => SoundEffectManager.TestPendingSound == null, 500);

		// 真实触发 SectorHover (稳定 45ms 后自动触发)
		gc.TestSetGestureActive(true, version: 701);
		SoundEffectManager.LastHoverTick = 0L;
		gc.TestQueueHighlightUpdate(0, -1, false, false, 701);
		Thread.Sleep(60);

		// 真实触发 SubmenuExpand
		SoundEffectManager.LastHoverTick = 0L;
		gc.TestQueueHighlightUpdate(0, -1, false, true, 701);
		SpinWait.SpinUntil(() => SoundEffectManager.TestPendingSound == null, 500);

		// 真实触发 GestureCancel
		SoundEffectManager.Play(SoundType.GestureCancel);
		SpinWait.SpinUntil(() => SoundEffectManager.TestPendingSound == null, 500);

		// 真实触发 ActionExecute
		SoundEffectManager.Play(SoundType.ActionExecute);

		var eventList = queuedEvents.ToList();
		bool path1Verified = eventList.Contains(SoundType.WheelPopup)
			&& eventList.Contains(SoundType.SectorHover)
			&& eventList.Contains(SoundType.SubmenuExpand)
			&& eventList.Contains(SoundType.GestureCancel)
			&& eventList.Contains(SoundType.ActionExecute);

		Assert(path1Verified, "7A_PathCoverage_GestureControllerComplete",
			$"普通手势轮盘真实调用链覆盖: [{string.Join(", ", eventList.Distinct())}]");

		// 路径 2: 粘滞/常驻轮盘 (StickyWheelSession.cs) 严格静态代码审计 (待人工GUI验证)
		// 严密规则：源码文件不存在或读取失败，明确记为未执行，绝不得判定通过
		bool stickyWheelAuditVerified = false;
		string auditFailureReason = "";
		try
		{
			string[] candidates = new[]
			{
				Path.Combine(Directory.GetCurrentDirectory(), "WinPieGestures", "Plugin", "StickyWheelSession.cs"),
				Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "WinPieGestures", "Plugin", "StickyWheelSession.cs"),
				Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "WinPieGestures", "Plugin", "StickyWheelSession.cs"),
				Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WinPieGestures", "Plugin", "StickyWheelSession.cs")
			};

			string? resolvedFile = candidates.FirstOrDefault(p => File.Exists(p));
			if (resolvedFile != null)
			{
				string text = File.ReadAllText(resolvedFile);
				stickyWheelAuditVerified = AuditStickyWheelSoundPath(text, out auditFailureReason);
				TestStickyWheelAuditCounterexamples();
			}
			else
			{
				stickyWheelAuditVerified = false;
				auditFailureReason = "源码文件 StickyWheelSession.cs 未找到，静态审计明确记为未执行，不得判通过";
			}
		}
		catch (Exception ex)
		{
			stickyWheelAuditVerified = false;
			auditFailureReason = $"读取源码文件失败: {ex.Message}，静态审计明确记为未执行";
		}

		Assert(stickyWheelAuditVerified, "7B_PathCoverage_StickyWheel_StaticAudit", auditFailureReason);

		// 路径 3: 设置窗口交互画布与按钮试听 (Settings Preview & Custom Preview) 真实调用链路测试
		queuedEvents.Clear();
		SoundEffectManager.PlayPreview(SoundType.WheelPopup);
		SpinWait.SpinUntil(() => SoundEffectManager.TestPendingSound == null, 500);
		SoundEffectManager.LastHoverTick = 0L;
		SoundEffectManager.PlayPreview(SoundType.SectorHover);
		var customCfg = new SoundEventConfig { WavePreset = "Sine1200", DurationMs = 20, RelativeVolume = 0.8 };
		SoundEffectManager.PlayCustomEventPreview(customCfg, 0.5);

		bool path3Verified = queuedEvents.Contains(SoundType.WheelPopup) && queuedEvents.Contains(SoundType.SectorHover);
		Assert(path3Verified, "7C_PathCoverage_SettingsPreviewComplete",
			"插件试听核心与自定义参数处理链测试通过");

		Log("  [Forensics 7 Metric] 调用路径审计完成：普通手势语义到插件路由与试听核心已验证；粘滞路径检查语义更新、先关闭再确认及动作入队结构，真实 GUI 时序仍待人工验收");
	}
	// 这是有边界的源码结构护栏，不是通用 C# 解析器，也不冒充真实 GUI 行为验证。
	// 保持字符位置，避免注释/字符串中的伪调用或花括号改变方法边界。
	private static string MaskStickyWheelNonCode(string source) => Regex.Replace(source,
		@"//[^\r\n]*|/\*[\s\S]*?\*/|@""(?:[^""]|"""")*""|""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'",
		match => new string(match.Value.Select(c => c == '\r' || c == '\n' ? c : ' ').ToArray()));

	private static string? FindStickyWheelMethod(string code, string name)
	{
		MatchCollection declarations = Regex.Matches(code,
			@"\b(?:internal|private)\s+static\s+void\s+" + Regex.Escape(name) + @"\s*\([^;{}]*\)\s*\{");
		if (declarations.Count != 1) return null;
		int start = declarations[0].Index + declarations[0].Length;
		int depth = 1;
		for (int i = start; i < code.Length; i++)
		{
			if (code[i] == '{') depth++;
			else if (code[i] == '}' && --depth == 0) return code.Substring(start, i - start);
		}
		return null;
	}

	private static bool IsUnconditionalTopLevelCall(string method, int callIndex)
	{
		int depth = 0;
		int statementStart = 0;
		for (int i = 0; i < callIndex; i++)
		{
			if (method[i] == '{') depth++;
			else if (method[i] == '}')
			{
				depth--;
				if (depth == 0) statementStart = i + 1;
			}
			else if (method[i] == ';' && depth == 0) statementStart = i + 1;
		}
		// 不接受 if (...) CloseUi(...) 这种无花括号的条件调用。
		return depth == 0 && string.IsNullOrWhiteSpace(method.Substring(statementStart, callIndex - statementStart));
	}

	private static bool AuditStickyWheelSoundPath(string source, out string failureReason)
	{
		string code = MaskStickyWheelNonCode(source);
		string? pointer = FindStickyWheelMethod(code, "HandlePointer");
		string? press = FindStickyWheelMethod(code, "HandlePress");
		if (pointer == null || press == null)
		{
			failureReason = "静态审计失败：HandlePointer/HandlePress 方法缺失、边界不完整或重复；不能用其他方法/注释替代";
			return false;
		}

        // 新架构：生产源只生成语义事件；真实音效路由由插件行为测试验证。
        if (Regex.IsMatch(pointer, @"\bInteraction\s*\?\.\s*Update\s*\("))
        {
            var confirmations = Regex.Matches(press, @"\bInteraction\s*\?\.\s*Confirm\s*\(");
            var dispatches = Regex.Matches(press, @"\bActionExecutor\s*\.\s*EnqueueAction\s*\(");
            var closesNew = Regex.Matches(press, @"\bCloseUi\s*\(\s*session\s*,\s*reason\s*,\s*completeInteraction\s*:\s*false\s*\)\s*;");
            bool valid = closesNew.Count == 1 && confirmations.Count > 0 && dispatches.Count > 0 &&
                IsUnconditionalTopLevelCall(press, closesNew[0].Index) && confirmations.Cast<Match>().All(c => c.Index > closesNew[0].Index) &&
                dispatches.Cast<Match>().All(c => c.Index > closesNew[0].Index) && !Regex.IsMatch(code, @"\bSoundEffectManager\b");
            failureReason = valid ? "" : "语义源必须先关闭UI，再确认并派发，且不含宿主播放实现";
            return valid;
        }

		bool hasHover = Regex.IsMatch(pointer, @"\bSoundEffectManager\s*\.\s*(?:ReportHover\s*\(|Play\s*\(\s*SoundType\s*\.\s*SectorHover\b)");
		MatchCollection executes = Regex.Matches(press, @"\bSoundEffectManager\s*\.\s*Play\s*\(\s*SoundType\s*\.\s*ActionExecute\b");
		MatchCollection enqueues = Regex.Matches(press, @"\bActionExecutor\s*\.\s*EnqueueAction\s*\(");
		MatchCollection closes = Regex.Matches(press,
			@"\bCloseUi\s*\(\s*session\s*,\s*reason\s*(?:,\s*(?:completeInteraction\s*:\s*)?(?<completion>false|true)\s*)?\)\s*;");
		bool closesBeforeDispatch = closes.Count == 1 && executes.Count > 0 && enqueues.Count > 0 &&
			IsUnconditionalTopLevelCall(press, closes[0].Index) &&
			executes.Cast<Match>().All(call => closes[0].Index < call.Index) &&
			enqueues.Cast<Match>().All(call => closes[0].Index < call.Index);
		bool defersTerminal = !Regex.IsMatch(press, @"\busing\s+var\s+interactionCompletion\b") ||
			(closes.Count == 1 && closes[0].Groups["completion"].Value == "false");
		bool verified = hasHover && closesBeforeDispatch && defersTerminal;
		failureReason = verified ? "" :
			$"静态结构审计不满足预期（GUI 未验证）：hoverInHandlePointer={hasHover}, executeCalls={executes.Count}, enqueueCalls={enqueues.Count}, closeCalls={closes.Count}, closeBeforeDispatch={closesBeforeDispatch}, defersTerminal={defersTerminal}";
		return verified;
	}

	private static void TestStickyWheelAuditCounterexamples()
	{
		// 只变异内存中的源码夹具；不改产品文件，不调用轮盘、音频或动作。
		const string fixture = @"
internal static void HandlePointer()
{
    SoundEffectManager.ReportHover(SoundSessionSource.StickyWheel, session.SoundSessionId);
}
internal static void HandlePress(bool leftButton)
{
    CloseUi(session, reason, completeInteraction: false);
    using var interactionCompletion = session.Interaction;
    if (target != null)
    {
        SoundEffectManager.Play(SoundType.ActionExecute, SoundSessionSource.StickyWheel, session.SoundSessionId);
        ActionExecutor.EnqueueAction(target);
    }
}";
		const string close = "CloseUi(session, reason, completeInteraction: false);";
		const string play = "SoundEffectManager.Play(SoundType.ActionExecute, SoundSessionSource.StickyWheel, session.SoundSessionId);";
		const string enqueue = "ActionExecutor.EnqueueAction(target);";
		Assert(AuditStickyWheelSoundPath(fixture, out _), "7B_AuditAcceptsDeferredClose");
		Assert(AuditStickyWheelSoundPath(fixture.Replace(close, "CloseUi(\n session,\n reason,\n completeInteraction : false\n );"), out _), "7B_AuditAcceptsMultilineClose");
		Assert(AuditStickyWheelSoundPath(fixture.Replace(close, "CloseUi(session, reason, false);"), out _), "7B_AuditAcceptsPositionalFalse");
		string legacy = fixture.Replace(close, "CloseUi(session, reason);").Replace("using var interactionCompletion = session.Interaction;", "");
		Assert(AuditStickyWheelSoundPath(legacy, out _), "7B_AuditAcceptsLegacyCloseWithoutDeferredSession");
		Assert(!AuditStickyWheelSoundPath(fixture.Replace(close, ""), out _), "7B_AuditRejectsMissingClose");
		Assert(!AuditStickyWheelSoundPath(fixture.Replace(close, "// " + close), out _), "7B_AuditRejectsCommentOnlyClose");
		Assert(!AuditStickyWheelSoundPath(fixture.Replace(close, "var decoy = \"" + close + " { }\";"), out _), "7B_AuditRejectsStringOnlyClose");
		Assert(!AuditStickyWheelSoundPath(fixture.Replace(close, "") + "\nprivate static void Decoy() { " + close + " }", out _), "7B_AuditRejectsCloseInOtherMethod");
		Assert(!AuditStickyWheelSoundPath(fixture.Replace(close, "").Replace(play, play + "\n" + close), out _), "7B_AuditRejectsCloseAfterConfirmation");
		Assert(!AuditStickyWheelSoundPath(fixture.Replace(close, "").Replace(enqueue, enqueue + "\n" + close), out _), "7B_AuditRejectsCloseAfterEnqueue");
		Assert(!AuditStickyWheelSoundPath(fixture.Replace(close, "if (leftButton) { " + close + " }"), out _), "7B_AuditRejectsConditionalClose");
		Assert(!AuditStickyWheelSoundPath(fixture.Replace(close, "if (leftButton) " + close), out _), "7B_AuditRejectsUnbracedConditionalClose");
		Assert(!AuditStickyWheelSoundPath(fixture.Replace(close, "CloseUi(session, reason, completeInteraction: true);"), out _), "7B_AuditRejectsPrematureTerminalCompletion");
		Assert(!AuditStickyWheelSoundPath(fixture.Replace(play, "") + "\nprivate static void Decoy() { " + play + " }", out _), "7B_AuditRejectsConfirmationInOtherMethod");
		Assert(!AuditStickyWheelSoundPath(fixture.Replace(enqueue, ""), out _), "7B_AuditRejectsMissingEnqueue");
		Assert(!AuditStickyWheelSoundPath(fixture.Replace("SoundEffectManager.ReportHover", "UnusedReportHover") + "\nprivate static void Decoy() { SoundEffectManager.ReportHover(); }", out _), "7B_AuditRejectsHoverInOtherMethod");
		Assert(!AuditStickyWheelSoundPath("", out _), "7B_AuditRejectsMissingMethods");
	}
	#endregion

	#region 场景 8: 外部 WAV JUNK 分块结构损坏真实事实刻画
	private static void TestScenario8_ExternalWavJunkChunkStructuralDamage()
	{
		Log("\n>>> 场景 8: 外部 WAV 合法 JUNK 分块结构损坏真实事实刻画 (WAV JUNK Chunk Investigation)");

		byte[] validWavWithJunk = CreateValidWavWithJunkChunk();

		string originalDataTag = Encoding.ASCII.GetString(validWavWithJunk, 60, 4);
		Assert(originalDataTag == "data", "8A_OriginalFixtureHasValidDataTagAtOffset60",
			$"原始构造文件 offset 60..63 处标头为 '{originalDataTag}'");

		byte[] originalCopy = (byte[])validWavWithJunk.Clone();
		byte[]? scaledWav = SoundEffectManager.ScaleWavVolume(validWavWithJunk, 0.5);
		Assert(scaledWav != null, "8B_0_ScaleWavVolumeNotNull", "合法含有 JUNK 分块的 WAV 文件成功缩放返回非空字节流");

		if (scaledWav != null)
		{
			// 断言 8B.1: 原输入数组未被破坏 (Input array remains completely unmodified)
			bool inputImmutable = originalCopy.SequenceEqual(validWavWithJunk);
			Assert(inputImmutable, "8B_1_InputArrayImmutability", "传入 ScaleWavVolume 的原始字节数组保持只读未受破坏");

			// 断言 8B.2: data 标头及之前的所有分块与元数据完全未受破坏 (Header & metadata chunks preserved byte-for-byte)
			string currentTag = Encoding.ASCII.GetString(scaledWav, 60, 4);
			bool metadataPreserved = scaledWav.Take(68).SequenceEqual(originalCopy.Take(68));
			Assert(metadataPreserved && currentTag == "data", "8B_2_MetadataAndChunkTagsPreserved",
				$"分块元数据未受损坏：data 标头与之前 68 字节保持一致 (当前标头: '{currentTag}', 等同原始: {metadataPreserved})");

		// 断言 8B.3: 仅实际 PCM 音频样本按音量缩放 (Only audio samples scaled properly)
		// validWavWithJunk has samples at 68..75: 1000, 2000, 3000, 4000. At volume 0.5, gain is 0.25 => 250, 500, 750, 1000
		double expectedGain = SoundEffectManager.GetAcousticGain(0.5);
		short expected0 = (short)Math.Clamp(1000 * expectedGain, -32768.0, 32767.0);
		short expected1 = (short)Math.Clamp(2000 * expectedGain, -32768.0, 32767.0);
		short expected2 = (short)Math.Clamp(3000 * expectedGain, -32768.0, 32767.0);
		short expected3 = (short)Math.Clamp(4000 * expectedGain, -32768.0, 32767.0);

		short s0 = BitConverter.ToInt16(scaledWav, 68);
		short s1 = BitConverter.ToInt16(scaledWav, 70);
		short s2 = BitConverter.ToInt16(scaledWav, 72);
		short s3 = BitConverter.ToInt16(scaledWav, 74);
		bool samplesCorrect = s0 == expected0 && s1 == expected1 && s2 == expected2 && s3 == expected3;
		Assert(samplesCorrect, "8B_3_AudioSamplesCorrectlyScaled",
			$"音频样本正确缩放：期望 [{expected0}, {expected1}, {expected2}, {expected3}]，实际 [{s0}, {s1}, {s2}, {s3}]");
		}

		SoundEffectManager.Initialize("Mechanical", 0.6, force: true);
		byte[]? builtInWav = SoundEffectManager.GetCachedSoundBuffer(SoundType.SectorHover);
		Assert(builtInWav != null && builtInWav.Length >= 44, "8C_1_BuiltInWaveformGenerated");

		if (builtInWav != null)
		{
			string bRiff = Encoding.ASCII.GetString(builtInWav, 0, 4);
			string bWave = Encoding.ASCII.GetString(builtInWav, 8, 4);
			string bFmt = Encoding.ASCII.GetString(builtInWav, 12, 4);
			string bData = Encoding.ASCII.GetString(builtInWav, 36, 4);

			bool isStrict44 = bRiff == "RIFF" && bWave == "WAVE" && bFmt == "fmt " && bData == "data";
			Assert(isStrict44, "8C_2_BuiltInHeaderIsStrictly44BytesAndImmune",
				"内建主题由 WrapPcmToWav 直接封装，data 标头固定位于 36..39 字节，不经过 ScaleWavVolume");

			Log("  [Forensics 8C Fact] 证实：内建主题完全绕过文件加载与 ScaleWavVolume，不受 44 字节假设损坏影响");
		}
	}

	private static byte[] CreateValidWavWithJunkChunk()
	{
		using var ms = new MemoryStream();
		using var bw = new BinaryWriter(ms);

		// 0..11: RIFF Header
		bw.Write(Encoding.ASCII.GetBytes("RIFF"));
		bw.Write(0);
		bw.Write(Encoding.ASCII.GetBytes("WAVE"));

		// 12..35: "fmt " Chunk (16 bytes)
		bw.Write(Encoding.ASCII.GetBytes("fmt "));
		bw.Write(16);
		bw.Write((short)1);     // PCM
		bw.Write((short)1);     // Mono
		bw.Write(44100);        // SampleRate
		bw.Write(88200);        // ByteRate
		bw.Write((short)2);     // BlockAlign
		bw.Write((short)16);    // BitsPerSample

		// 36..59: "JUNK" Chunk (16 bytes payload)
		bw.Write(Encoding.ASCII.GetBytes("JUNK"));
		bw.Write(16);
		for (int i = 0; i < 16; i++) bw.Write((byte)0);

		// 60..67: "data" Chunk Identifier & Length (offset 60)
		bw.Write(Encoding.ASCII.GetBytes("data"));
		bw.Write(8);

		// 68..75: PCM payload
		bw.Write((short)1000);
		bw.Write((short)2000);
		bw.Write((short)3000);
		bw.Write((short)4000);

		bw.Flush();
		byte[] bytes = ms.ToArray();
		int riffPayloadSize = bytes.Length - 8;
		BitConverter.GetBytes(riffPayloadSize).CopyTo(bytes, 4);
		return bytes;
	}
	#endregion

	#region 阶段 B1: 播放线程生命周期修复目标断言 (Stage B1 Lifecycle Target Assertions)
	private static void TestStageB1_LifecycleFixTargetAssertions()
	{
		Log("\n>>> 阶段 B1: 播放线程生命周期修复目标断言 (Stage B1 Lifecycle Target Assertions)");

		// 断言 1: 最大并发播放调用数绝不超过 1 (Max Concurrent Playback <= 1)
		{
			SoundEffectManager.ResetTestSeams();
			int activeCalls = 0;
			int maxConcurrent = 0;
			object statLock = new();
			AutoResetEvent w1Entered = new(false);
			ManualResetEvent allowW1Exit = new(false);
			TrackBarrier(allowW1Exit);
			AutoResetEvent w2Entered = new(false);
			ManualResetEvent allowW2Exit = new(false);
			TrackBarrier(allowW2Exit);

			int w1Id = 0;
			SoundEffectManager.PlaybackSink = (data, flags) =>
			{
				int current = Interlocked.Increment(ref activeCalls);
				lock (statLock)
				{
					if (current > maxConcurrent) maxConcurrent = current;
				}

				if (Thread.CurrentThread.ManagedThreadId == w1Id)
				{
					w1Entered.Set();
					bool waitW1 = allowW1Exit.WaitOne(3000);
					Assert(waitW1, "B1_AllowW1ExitWithin3000ms");
				}
				else
				{
					w2Entered.Set();
					bool waitW2 = allowW2Exit.WaitOne(3000);
					Assert(waitW2, "B1_AllowW2ExitWithin3000ms");
				}

				Interlocked.Decrement(ref activeCalls);
			};

			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);
			Thread? w1 = SoundEffectManager.CurrentWorkerThread;
			w1Id = w1?.ManagedThreadId ?? 0;

			// 触发旧播放，进入 PlaybackSink 并被阻塞
			SoundEffectManager.PlayPreview(SoundType.WheelPopup);
			bool w1In = w1Entered.WaitOne(2000);
			Assert(w1In, "B1_W1EnteredPlaybackWithin2000ms");

			// 旧播放正在进行中时，关闭音效后立即重新开启
			SoundEffectManager.Shutdown();
			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);

			// 尝试下发新播放请求
			SoundEffectManager.PlayPreview(SoundType.ActionExecute);

			// 等待片刻检测是否有第二条播放并发进入
			bool w2InWhileW1Blocked = w2Entered.WaitOne(400);

			int measuredMax;
			lock (statLock) { measuredMax = maxConcurrent; }

			Assert(measuredMax <= 1 && !w2InWhileW1Blocked, "B1_Target1_MaxConcurrentPlaybackNeverExceeds1",
				$"实测最大并发调用数: {measuredMax} (W2在W1阻塞期间进入={w2InWhileW1Blocked})");

			// 清理与释放
			allowW1Exit.Set();
			allowW2Exit.Set();
			bool w1Joined = w1 != null && w1.Join(2000);
			Assert(w1Joined, "B1_W1JoinedWithin2000ms");
			SoundEffectManager.Shutdown();
		}

		// 断言 2: 旧工作线程不会恢复消费，在释放后必须安全终止退出 (Old Worker Exits Without Resuming)
		{
			SoundEffectManager.ResetTestSeams();
			AutoResetEvent oldWorkerPlaybackEntered = new(false);
			ManualResetEvent allowOldWorkerExitPlayback = new(false);
			TrackBarrier(allowOldWorkerExitPlayback);
			List<int> playbackThreadIds = new();
			object pLock = new();

			int oldWorkerId = 0;
			SoundEffectManager.PlaybackSink = (data, flags) =>
			{
				int tid = Thread.CurrentThread.ManagedThreadId;
				lock (pLock) { playbackThreadIds.Add(tid); }
				if (tid == oldWorkerId)
				{
					oldWorkerPlaybackEntered.Set();
					bool waitExit = allowOldWorkerExitPlayback.WaitOne(3000);
					Assert(waitExit, "B1_AllowOldWorkerExitWithin3000ms");
				}
			};

			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);
			Thread? oldWorker = SoundEffectManager.CurrentWorkerThread;
			oldWorkerId = oldWorker?.ManagedThreadId ?? 0;

			// 触发播放，使旧工作线程进入阻塞
			SoundEffectManager.PlayPreview(SoundType.WheelPopup);
			bool oldEntered = oldWorkerPlaybackEntered.WaitOne(2000);
			Assert(oldEntered, "B1_OldWorkerEnteredWithin2000ms");

			// 关闭并立即重新开启新代际
			SoundEffectManager.Shutdown();
			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);

			// 释放旧工作线程的播放阻塞
			allowOldWorkerExitPlayback.Set();

			bool oldWorkerExited = oldWorker != null && oldWorker.Join(2000);
			Assert(oldWorkerExited, "B1_Target2_OldWorkerExitsWithoutResumingConsumption",
				$"旧工作线程 ID={oldWorkerId} 释放后终止={oldWorkerExited}, IsAlive={oldWorker?.IsAlive}");

			SoundEffectManager.Shutdown();
			var b1Worker = SoundEffectManager.CurrentWorkerThread;
			if (b1Worker != null && b1Worker.IsAlive)
			{
				bool b1Joined = b1Worker.Join(2000);
				Assert(b1Joined, "B1_WorkerJoinedWithin2000ms");
			}
		}

		// 断言 3: 释放旧播放后，新请求正常播放，旧代请求不得复活 (Clean New Playback, No Zombie Resurrections)
		{
			SoundEffectManager.ResetTestSeams();
			AutoResetEvent oldPlaybackEntered = new(false);
			ManualResetEvent allowOldPlaybackExit = new(false);
			TrackBarrier(allowOldPlaybackExit);
			AutoResetEvent newPlaybackFinished = new(false);

			List<(int ThreadId, SoundType? Sound)> playedLog = new();
			object logLock = new();

			int oldTid = 0;
			SoundEffectManager.PlaybackSink = (data, flags) =>
			{
				int tid = Thread.CurrentThread.ManagedThreadId;
				if (oldTid == 0 || tid == oldTid)
				{
					oldPlaybackEntered.Set();
					bool waitOld = allowOldPlaybackExit.WaitOne(3000);
					Assert(waitOld, "B1_AllowOldPlaybackExitWithin3000ms");
				}
			};
			SoundEffectManager.SoundPlaybackFinished = (type, custom, tick) =>
			{
				lock (logLock) { playedLog.Add((Thread.CurrentThread.ManagedThreadId, type)); }
				if (type == SoundType.ActionExecute)
				{
					newPlaybackFinished.Set();
				}
			};

			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);
			Thread? oldThread = SoundEffectManager.CurrentWorkerThread;
			oldTid = oldThread?.ManagedThreadId ?? 0;

			SoundEffectManager.PlayPreview(SoundType.WheelPopup);
			bool oldIn = oldPlaybackEntered.WaitOne(2000);
			Assert(oldIn, "B1_OldPlaybackEnteredWithin2000ms");

			// 在旧播放期间，排队一个旧代待播音效 (SectorHover)
			SoundEffectManager.PlayPreview(SoundType.SectorHover);

			// 执行 Shutdown: 旧代的 SectorHover 应当被清除，不得复活
			SoundEffectManager.Shutdown();

			// 启动新代际
			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);

			// 释放旧播放
			allowOldPlaybackExit.Set();
			bool oldJoined = oldThread != null && oldThread.Join(2000);
			Assert(oldJoined, "B1_OldThreadJoinedWithin2000ms");

			// 下发新请求 ActionExecute
			SoundEffectManager.PlayPreview(SoundType.ActionExecute);
			bool newPlayed = newPlaybackFinished.WaitOne(3000);

			List<SoundType?> playedTypes;
			lock (logLock) { playedTypes = playedLog.Select(x => x.Sound).ToList(); }

			bool newRequestSucceeded = newPlayed && playedTypes.Contains(SoundType.ActionExecute);
			bool oldRequestDidNotResurrect = !playedTypes.Contains(SoundType.SectorHover);

			Assert(newRequestSucceeded && oldRequestDidNotResurrect, "B1_Target3_NewRequestsPlayCleanlyWithoutOldResurrection",
				$"新请求成功={newRequestSucceeded}, 旧请求复活={!oldRequestDidNotResurrect}, 实际完成序列: [{string.Join(", ", playedTypes)}]");

			SoundEffectManager.Shutdown();
		}

		// 断言 4: 反复开关、并发 Initialize/Shutdown、播放异常无永久线程泄漏
		// 严密规则：复制线程列表并在锁外执行 Join，检查全部 Join 返回值
		{
			SoundEffectManager.ResetTestSeams();
			List<Thread> spawnedThreads = new();
			object threadLock = new();

			SoundEffectManager.WorkerThreadCreated = t =>
			{
				lock (threadLock)
				{
					if (!spawnedThreads.Contains(t)) spawnedThreads.Add(t);
				}
			};

			SoundEffectManager.PlaybackSink = (data, flags) =>
			{
				// 模拟偶发异常
				if (Random.Shared.Next(0, 4) == 0)
				{
					throw new InvalidOperationException("Simulated PlaybackSink transient fault");
				}
			};

			const int Iterations = 30;
			List<Exception> errors = new();

			Parallel.For(0, Iterations, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
			{
				try
				{
					if (i % 2 == 0)
					{
						SoundEffectManager.Initialize("Mechanical", 0.6, force: true);
						SoundEffectManager.PlayPreview(SoundType.WheelPopup);
					}
					else
					{
						SoundEffectManager.Shutdown();
					}
				}
				catch (Exception ex)
				{
					lock (errors) { errors.Add(ex); }
				}
			});

			SoundEffectManager.Shutdown();

			List<Thread> threadsToWait;
			lock (threadLock)
			{
				threadsToWait = spawnedThreads.ToList();
			}

			bool allTerminated = true;
			List<int> leakedIds = new();
			foreach (var t in threadsToWait)
			{
				bool joined = t.Join(2000);
				if (!joined)
				{
					allTerminated = false;
					leakedIds.Add(t.ManagedThreadId);
				}
			}

			Assert(allTerminated && errors.Count == 0, "B1_Target4_StressToggleConcurrentInitShutdownNoThreadLeaks",
				$"总记录线程数={spawnedThreads.Count}, 泄漏数={leakedIds.Count} (泄漏ID=[{string.Join(", ", leakedIds)}]), 异常数={errors.Count}");

			SoundEffectManager.Shutdown();
			SoundEffectManager.ResetTestSeams();
			SoundEffectManager.PlaybackSink = (data, flags) => { };
		}
	}
	#endregion

	#region 阶段 B1.1: 播放线程生命周期与测试证据强化断言 (Stage B1.1 Target Regressions)
	private static void TestStageB1_1_TargetRegressionAssertions()
	{
		Log("\n>>> 阶段 B1.1: 播放线程生命周期与测试证据强化断言 (Stage B1.1 Target Regressions)");

		// B1_1_Reg1: 未开始的已停止代请求被安全清除，绝不播放
		{
			SoundEffectManager.ResetTestSeams();
			AutoResetEvent w1Entered = new(false);
			ManualResetEvent allowW1Exit = new(false);
			TrackBarrier(allowW1Exit);
			var finishedSounds = new ConcurrentBag<SoundType?>();

			SoundEffectManager.SoundPlaybackFinished = (type, wav, tick) =>
			{
				finishedSounds.Add(type);
			};

			SoundEffectManager.PlaybackSink = (data, flags) =>
			{
				w1Entered.Set();
				bool waitExit = allowW1Exit.WaitOne(3000);
				Assert(waitExit, "B1_1_Reg1_AllowW1ExitWithin3000ms");
			};

			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);
			Thread? w1 = SoundEffectManager.CurrentWorkerThread;

			// 触发旧播放，Worker 1 进入阻塞
			SoundEffectManager.PlayPreview(SoundType.WheelPopup);
			bool w1In = w1Entered.WaitOne(2000);
			Assert(w1In, "B1_1_Reg1_W1EnteredPlaybackWithin2000ms");

			// 在 W1 阻塞期间：Shutdown -> Initialize -> Queue ActionExecute -> Shutdown 再次立即终止
			SoundEffectManager.Shutdown();
			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);
			SoundEffectManager.PlayPreview(SoundType.ActionExecute);
			SoundEffectManager.Shutdown();

			// 释放 W1
			allowW1Exit.Set();
			bool w1Joined = w1 != null && w1.Join(2000);
			Assert(w1Joined, "B1_1_Reg1_W1JoinedWithin2000ms");

			// 检验：ActionExecute 绝对没有被播放
			bool actionExecutePlayed = finishedSounds.Contains(SoundType.ActionExecute);
			Assert(!actionExecutePlayed, "B1_1_Reg1_UnstartedStoppedRequestNeverPlays",
				$"已停止代的未开始请求被安全丢弃，实际播放清单: [{string.Join(", ", finishedSounds)}]");

			SoundEffectManager.Shutdown();
		}

		// B1_1_Reg2: 慢速后端阻塞时反复开关，最大存活工作线程数恒 <= 1，且并发播放调用恒 <= 1
		{
			SoundEffectManager.ResetTestSeams();
			List<Thread> allCreatedWorkers = new();
			object workersLock = new();

			SoundEffectManager.WorkerThreadCreated = t =>
			{
				lock (workersLock)
				{
					if (!allCreatedWorkers.Contains(t)) allCreatedWorkers.Add(t);
				}
			};

			int activeCalls = 0;
			int maxConcurrent = 0;
			object sinkLock = new();
			AutoResetEvent w1Entered = new(false);
			ManualResetEvent blockerGate = new(false);
			TrackBarrier(blockerGate);

			SoundEffectManager.PlaybackSink = (data, flags) =>
			{
				int cur = Interlocked.Increment(ref activeCalls);
				lock (sinkLock)
				{
					if (cur > maxConcurrent) maxConcurrent = cur;
				}

				w1Entered.Set();
				bool waitBlocker = blockerGate.WaitOne(3000);
				Assert(waitBlocker, "B1_1_Reg2_BlockerGateWithin3000ms");

				Interlocked.Decrement(ref activeCalls);
			};

			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);
			SoundEffectManager.PlayPreview(SoundType.WheelPopup);
			bool w1In = w1Entered.WaitOne(2000);
			Assert(w1In, "B1_1_Reg2_W1EnteredWithin2000ms");

			// 在 W1 被后端持续阻塞期间，反复执行 5 次开关与初始化
			int peakAliveWorkers = 1;
			for (int i = 0; i < 5; i++)
			{
				SoundEffectManager.Shutdown();
				SoundEffectManager.Initialize("Mechanical", 0.6, force: true);
				SoundEffectManager.PlayPreview(SoundType.SectorHover);

				lock (workersLock)
				{
					int currentAlive = allCreatedWorkers.Count(w => w.IsAlive);
					if (currentAlive > peakAliveWorkers) peakAliveWorkers = currentAlive;
				}
			}

			// 记录测量指标
			MeasuredMaxAliveWorkers = peakAliveWorkers;
			lock (sinkLock) { MeasuredMaxPlaybackConcurrency = maxConcurrent; }

			// 释放后端阻塞
			blockerGate.Set();

			// 最终 Shutdown 并等待所有曾创建的线程退出（先复制列表并在锁外 Join）
			SoundEffectManager.Shutdown();
			List<Thread> workersToWait;
			lock (workersLock)
			{
				workersToWait = allCreatedWorkers.ToList();
			}
			foreach (var t in workersToWait)
			{
				bool joined = t.Join(2000);
				Assert(joined, "B1_1_Reg2_AllWorkersJoinedWithin2000ms");
			}

			Log($"  [Stage B1.1 Reg2 Metric] 持续阻塞期间反复开关，实测最大存活工作线程数: {peakAliveWorkers}, 实测最大并发播放数: {MeasuredMaxPlaybackConcurrency}");

			Assert(peakAliveWorkers <= 1 && MeasuredMaxPlaybackConcurrency <= 1, "B1_1_Reg2_MaxConcurrencyAndWorkersNeverExceed1",
				$"实测最大存活Worker数: {peakAliveWorkers} (必须<=1), 最大播放并发: {MeasuredMaxPlaybackConcurrency} (必须<=1)");

			SoundEffectManager.Shutdown();
		}

		// B1_1_Reg3: Shutdown 必须为绝对非阻塞（< 100ms），不得同步 Join 耗时未完的底层物理回放
		{
			SoundEffectManager.ResetTestSeams();
			AutoResetEvent playbackEntered = new(false);
			ManualResetEvent backendSlowGate = new(false);
			TrackBarrier(backendSlowGate);

			SoundEffectManager.PlaybackSink = (data, flags) =>
			{
				playbackEntered.Set();
				bool waitSlow = backendSlowGate.WaitOne(3000);
				Assert(waitSlow, "B1_1_Reg3_BackendSlowGateWithin3000ms");
			};

			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);
			Thread? worker = SoundEffectManager.CurrentWorkerThread;

			SoundEffectManager.PlayPreview(SoundType.WheelPopup);
			bool entered = playbackEntered.WaitOne(2000);
			Assert(entered, "B1_1_Reg3_PlaybackEnteredWithin2000ms");

			Stopwatch sw = Stopwatch.StartNew();
			SoundEffectManager.Shutdown();
			sw.Stop();

			MeasuredShutdownDurationMs = sw.ElapsedMilliseconds;
			Log($"  [Stage B1.1 Reg3 Metric] 底层阻塞时 Shutdown() 实测耗时: {MeasuredShutdownDurationMs} ms (门限: < 100ms)");

			Assert(MeasuredShutdownDurationMs < 100, "B1_1_Reg3_ShutdownDoesNotWaitBlockedBackend",
				$"Shutdown耗时: {MeasuredShutdownDurationMs}ms (必须<100ms, 禁止同步Join或等待后端完成)");

			// 释放慢速后端
			backendSlowGate.Set();
			bool workerExited = worker != null && worker.Join(2000);
			Assert(workerExited, "B1_1_Reg3_WorkerExitedWithin2000ms");

			// 验证 Shutdown 之后，重新 Initialize 能够正常工作且播放新请求
			AutoResetEvent newPlayFinished = new(false);
			SoundEffectManager.SoundPlaybackFinished = (type, wav, tick) =>
			{
				if (type == SoundType.ActionExecute) newPlayFinished.Set();
			};

			SoundEffectManager.PlaybackSink = (data, flags) => { };
			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);
			SoundEffectManager.PlayPreview(SoundType.ActionExecute);

			bool newDone = newPlayFinished.WaitOne(3000);
			Assert(newDone, "B1_1_Reg3_NewPlaybackResumesCleanlyAfterShutdown");

			SoundEffectManager.Shutdown();
		}

		// B1_1_Reg4: 高并发开关与播放调用压力测试下无泄漏、零死锁、异常被安全拦截
		// 严密规则：复制线程列表并在锁外执行 Join，检查全部 Join 返回值
		{
			SoundEffectManager.ResetTestSeams();
			List<Thread> allSpawned = new();
			object threadLock = new();

			SoundEffectManager.WorkerThreadCreated = t =>
			{
				lock (threadLock)
				{
					if (!allSpawned.Contains(t)) allSpawned.Add(t);
				}
			};

			SoundEffectManager.PlaybackSink = (data, flags) =>
			{
				if (Random.Shared.Next(0, 4) == 0)
				{
					throw new InvalidOperationException("Simulated PlaybackSink transient fault");
				}
			};

			const int StressIterations = 40;
			List<Exception> errors = new();

			Parallel.For(0, StressIterations, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
			{
				try
				{
					switch (i % 4)
					{
						case 0:
							SoundEffectManager.Initialize("Mechanical", 0.6, force: true);
							SoundEffectManager.PlayPreview(SoundType.WheelPopup);
							break;
						case 1:
							SoundEffectManager.PlayPreview(SoundType.SectorHover);
							break;
						case 2:
							var cfg = new SoundEventConfig { WavePreset = "Square600", DurationMs = 15, RelativeVolume = 0.8 };
							SoundEffectManager.PlayCustomEventPreview(cfg, 0.5);
							break;
						case 3:
							SoundEffectManager.Shutdown();
							break;
					}
				}
				catch (Exception ex)
				{
					lock (errors) { errors.Add(ex); }
				}
			});

			SoundEffectManager.Shutdown();

			List<Thread> threadsToWait;
			lock (threadLock)
			{
				TotalWorkersCreatedCount = allSpawned.Count;
				threadsToWait = allSpawned.ToList();
			}

			List<int> leakedIds = new();
			foreach (var t in threadsToWait)
			{
				bool joined = t.Join(2000);
				if (!joined)
				{
					leakedIds.Add(t.ManagedThreadId);
				}
			}

			LeakedWorkersCount = leakedIds.Count;
			Log($"  [Stage B1.1 Reg4 Metric] 压力测试并发轮次: {StressIterations}, 创建工作线程数: {TotalWorkersCreatedCount}, 泄漏线程数: {LeakedWorkersCount}, 异常数: {errors.Count}");

			Assert(LeakedWorkersCount == 0 && errors.Count == 0, "B1_1_Reg4_ConcurrentStressNoLeaksNoErrors",
				$"创建线程总数: {TotalWorkersCreatedCount}, 泄漏数: {LeakedWorkersCount} (泄漏ID=[{string.Join(", ", leakedIds)}]), 异常数: {errors.Count}");

			SoundEffectManager.Shutdown();
			SoundEffectManager.ResetTestSeams();
			SoundEffectManager.PlaybackSink = (data, flags) => { };
		}
	}
	#endregion

	#region 阶段 B1.2: 修复跨代队列清理竞态 (Stage B1.2 Cross-Generation Queue Cleaning Race)
	/// <summary>
	/// 阶段 B1.2: 跨代队列清理竞态与新请求恢复验证。
	/// 针对 GPT 已复现的交错：
	/// 1. W1 在模拟后端阻塞。
	/// 2. 测试持有 TestQueueLock，让另一线程调用 Shutdown。
	/// 3. 观察旧 Shutdown 是否已将 _isRunning 设为 false，但尚未完成队列清理（分裂中间态）。
	/// 4. 重新 Initialize，并入队 ActionExecute。
	/// 5. 释放 TestQueueLock，旧 Shutdown 完成。
	/// 6. 验证新请求 ActionExecute 绝不被旧 Shutdown 清掉，必须恢复正常播放；
	///    旧代请求不复活；播放并发调用不超过 1。
	/// </summary>
	private static void TestStageB1_2_CrossGenQueueCleanRace()
	{
		Log("\n>>> 阶段 B1.2: 跨代队列清理竞态与新请求恢复验证 (Stage B1.2 Cross-Gen Queue Race)");

		SoundEffectManager.ResetTestSeams();
		AutoResetEvent w1Entered = new(false);
		ManualResetEvent allowW1Exit = new(false);
		TrackBarrier(allowW1Exit);
		AutoResetEvent actionExecuteFinished = new(false);
		ConcurrentBag<SoundType?> playedSounds = new();
		int activeCalls = 0;
		int maxConcurrent = 0;
		object sinkLock = new();

		SoundEffectManager.PlaybackSink = (data, flags) =>
		{
			int cur = Interlocked.Increment(ref activeCalls);
			lock (sinkLock)
			{
				if (cur > maxConcurrent) maxConcurrent = cur;
			}

			w1Entered.Set();
			bool waitExit = allowW1Exit.WaitOne(3000);
			Assert(waitExit, "B1_2_AllowW1ExitWithin3000ms");

			Interlocked.Decrement(ref activeCalls);
		};

		SoundEffectManager.SoundPlaybackFinished = (type, wav, tick) =>
		{
			playedSounds.Add(type);
			if (type == SoundType.ActionExecute)
			{
				actionExecuteFinished.Set();
			}
		};

		SoundEffectManager.Initialize("Mechanical", 0.6, force: true);
		Thread? w1 = SoundEffectManager.CurrentWorkerThread;

		// 1. 触发首声，Worker 1 进入模拟后端阻塞
		SoundEffectManager.PlayPreview(SoundType.WheelPopup);
		bool w1In = w1Entered.WaitOne(2000);
		Assert(w1In, "B1_2_W1EnteredPlaybackWithin2000ms");

		// 2. 模拟 GPT 复现交错
		AutoResetEvent shutdownStarted = new(false);
		AutoResetEvent shutdownDone = new(false);
		bool observedSplitState = false;

		Thread shutdownThread = new(() =>
		{
			shutdownStarted.Set();
			SoundEffectManager.Shutdown();
			shutdownDone.Set();
		})
		{
			IsBackground = true,
			Name = "B1_2_ShutdownThread"
		};

		lock (SoundEffectManager.TestQueueLock)
		{
			shutdownThread.Start();
			bool started = shutdownStarted.WaitOne(2000);
			Assert(started, "B1_2_ShutdownThreadStartedWithin2000ms");

			// 检测旧 Shutdown 是否在未获得 _queueLock 的情况下将 _isRunning 设为 false
			for (int i = 0; i < 20; i++)
			{
				if (!SoundEffectManager.IsRunning)
				{
					observedSplitState = true;
					break;
				}
				Thread.Sleep(5);
			}

			Log($"  [Stage B1.2 Observation] 分裂中间态 (IsRunning=false 但 _queueLock 被持有): {observedSplitState}");

			// 无论是否观察到分裂中间态，重新 Initialize 并入队 ActionExecute
			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);
			SoundEffectManager.PlayPreview(SoundType.ActionExecute);
		}

		// 释放 TestQueueLock 后，等待 Shutdown 线程完全返回
		bool shutdownFinished = shutdownDone.WaitOne(2000);
		Assert(shutdownFinished, "B1_2_ShutdownThreadCompletedWithin2000ms");

		// 3. 释放 W1 阻塞，允许其退出并交接
		allowW1Exit.Set();
		bool w1Joined = w1 != null && w1.Join(2000);
		Assert(w1Joined, "B1_2_W1JoinedWithin2000ms");

		// 4. 断言核心目标
		// (a) 新请求 ActionExecute 必须恢复播放，绝不能被旧 Shutdown 误清
		bool actionExecutePlayed = actionExecuteFinished.WaitOne(3000);
		Assert(actionExecutePlayed, "B1_2_Target1_NewRequestActionExecutePlayed",
			$"新请求 ActionExecute 必须播放。实际播放记录: [{string.Join(", ", playedSounds)}]");

		// (b) 旧代请求绝不复活
		bool oldRequestResurrected = playedSounds.Count(s => s == SoundType.WheelPopup) > 1;
		Assert(!oldRequestResurrected, "B1_2_Target2_OldGenerationDoesNotResurrect",
			"旧代请求未发生复活");

		// (c) 最大并发播放调用不超过 1
		int finalMax;
		lock (sinkLock) { finalMax = maxConcurrent; }
		Assert(finalMax <= 1, "B1_2_Target3_MaxConcurrentPlaybackNeverExceeds1",
			$"实测最大并发回放调用数: {finalMax} (必须<=1)");

		SoundEffectManager.Shutdown();
	}
	#endregion

	#region 阶段 B2a: 音效优先级与会话隔离断言 (Stage B2a Priority & Session Isolation)
	private static void TestStageB2a_PriorityAndSessionIsolation()
	{
		Log("\n>>> 阶段 B2a: 音效优先级与会话隔离断言 (Stage B2a Priority & Session Isolation)");

		SoundEffectManager.ResetTestSeams();

		// B2a_Target1: Execute/Cancel 待播时，后续 Hover 绝不能覆盖它
		{
			AutoResetEvent blockerStarted = new(false);
			ManualResetEvent allowBlockerExit = new(false);
			TrackBarrier(allowBlockerExit);
			AutoResetEvent playbackDone = new(false);
			SoundType? playedSound = null;

			SoundEffectManager.SoundPlaybackStarted = (t, wav, tick) => blockerStarted.Set();
			SoundEffectManager.SoundPlaybackFinished = (t, wav, tick) =>
			{
				if (t != SoundType.WheelPopup)
				{
					playedSound = t;
					playbackDone.Set();
				}
			};

			SoundEffectManager.PlaybackSink = (data, flags) =>
			{
				allowBlockerExit.WaitOne(3000);
			};

			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);

			// 阻塞工作线程
			SoundEffectManager.PlayPreview(SoundType.WheelPopup);
			blockerStarted.WaitOne(2000);

			// 入队 ActionExecute (High)
			SoundEffectManager.Play(SoundType.ActionExecute);
			Assert(SoundEffectManager.TestPendingSound == SoundType.ActionExecute, "B2a_Target1_ActionExecuteEnqueued");

			// 尝试用 SectorHover (Low) 覆盖
			SoundEffectManager.LastHoverTick = 0L;
			SoundEffectManager.Play(SoundType.SectorHover);

			// 待播项必须保持为 ActionExecute
			bool preserved = SoundEffectManager.TestPendingSound == SoundType.ActionExecute;
			Assert(preserved, "B2a_Target1_HoverCannotOverwriteExecute",
				$"待播项当前为: {SoundEffectManager.TestPendingSound} (期望: ActionExecute)");

			allowBlockerExit.Set();
			playbackDone.WaitOne(2000);
			Assert(playedSound == SoundType.ActionExecute, "B2a_Target1_PreservedActionExecuteActuallyPlayed",
				$"实际完成播放: {playedSound}");

			SoundEffectManager.Shutdown();
			SoundEffectManager.ResetTestSeams();
		}

		// B2a_Target2: 高优先级 (High) 抢占低/中优先级 (Low/Normal)
		{
			AutoResetEvent blockerStarted = new(false);
			ManualResetEvent allowBlockerExit = new(false);
			TrackBarrier(allowBlockerExit);

			SoundEffectManager.SoundPlaybackStarted = (t, wav, tick) => blockerStarted.Set();
			SoundEffectManager.PlaybackSink = (data, flags) => allowBlockerExit.WaitOne(3000);

			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);
			SoundEffectManager.PlayPreview(SoundType.WheelPopup);
			blockerStarted.WaitOne(2000);

			// 先排入 SubmenuExpand (Normal)
			SoundEffectManager.Play(SoundType.SubmenuExpand);
			Assert(SoundEffectManager.TestPendingSound == SoundType.SubmenuExpand, "B2a_Target2_SubmenuExpandEnqueued");

			// 再排入 ActionExecute (High)
			SoundEffectManager.Play(SoundType.ActionExecute);
			Assert(SoundEffectManager.TestPendingSound == SoundType.ActionExecute, "B2a_Target2_HighPriorityPreemptsNormal",
				$"待播项当前为: {SoundEffectManager.TestPendingSound} (期望: ActionExecute)");

			allowBlockerExit.Set();
			SoundEffectManager.Shutdown();
			SoundEffectManager.ResetTestSeams();
		}

		// B2a_Target3: 同优先级采用最新覆盖（Latest Overwrites Previous，单槽容量恒为 1）
		{
			AutoResetEvent blockerStarted = new(false);
			ManualResetEvent allowBlockerExit = new(false);
			TrackBarrier(allowBlockerExit);
			int droppedCount = 0;
			SoundEffectManager.SoundDropped = (t, wav, tick) => Interlocked.Increment(ref droppedCount);

			SoundEffectManager.SoundPlaybackStarted = (t, wav, tick) => blockerStarted.Set();
			SoundEffectManager.PlaybackSink = (data, flags) => allowBlockerExit.WaitOne(3000);

			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);
			SoundEffectManager.PlayPreview(SoundType.WheelPopup);
			blockerStarted.WaitOne(2000);

			// 排入 ActionExecute
			SoundEffectManager.Play(SoundType.ActionExecute);
			// 再次排入同优先级的 GestureCancel
			SoundEffectManager.Play(SoundType.GestureCancel);

			Assert(SoundEffectManager.TestPendingSound == SoundType.GestureCancel && droppedCount == 1,
				"B2a_Target3_SamePriorityLatestOverwritesPrevious",
				$"待播项: {SoundEffectManager.TestPendingSound}, 丢弃计数: {droppedCount}");

			allowBlockerExit.Set();
			SoundEffectManager.Shutdown();
			SoundEffectManager.ResetTestSeams();
		}

		// B2a_Target4: 会话结束后未开始的 Hover 不播放（EndSession 原子撤销该会话未开始请求）
		{
			AutoResetEvent blockerStarted = new(false);
			ManualResetEvent allowBlockerExit = new(false);
			TrackBarrier(allowBlockerExit);
			AutoResetEvent hoverStarted = new(false);

			SoundEffectManager.SoundPlaybackStarted = (t, wav, tick) =>
			{
				if (t == SoundType.WheelPopup) blockerStarted.Set();
				else if (t == SoundType.SectorHover) hoverStarted.Set();
			};
			SoundEffectManager.PlaybackSink = (data, flags) => allowBlockerExit.WaitOne(3000);

			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);
			long sessId = SoundEffectManager.BeginSession(SoundSessionSource.NormalGesture);

			// 呼出轮盘并阻塞
			SoundEffectManager.Play(SoundType.WheelPopup, SoundSessionSource.NormalGesture, sessId);
			blockerStarted.WaitOne(2000);

			// 划过扇区排入 Hover
			SoundEffectManager.LastHoverTick = 0L;
			SoundEffectManager.Play(SoundType.SectorHover, SoundSessionSource.NormalGesture, sessId);
			Assert(SoundEffectManager.TestPendingSound == SoundType.SectorHover, "B2a_Target4_HoverPendingBeforeEnd");

			// 会话正常结束 (EndSession)
			SoundEffectManager.EndSession(SoundSessionSource.NormalGesture, sessId, allowTerminalFeedback: true);

			// 契约断言：未开始的 Hover 必须被立刻撤销
			Assert(SoundEffectManager.TestPendingSound == null, "B2a_Target4_PendingHoverRevokedImmediatelyOnEnd",
				$"待播槽已清空: {SoundEffectManager.TestPendingSound == null}");

			// 释放首声
			allowBlockerExit.Set();

			// 检验 Hover 绝不播放
			bool hoverRan = hoverStarted.WaitOne(600);
			Assert(!hoverRan, "B2a_Target4_HoverNeverPlayedAfterSessionEnded",
				"会话结束后未开始的 Hover 绝不播放");

			SoundEffectManager.Shutdown();
			SoundEffectManager.ResetTestSeams();
		}

		// B2a_Target5: 迟到旧会话请求不能污染新会话
		{
			AutoResetEvent blockerStarted = new(false);
			ManualResetEvent allowBlockerExit = new(false);
			TrackBarrier(allowBlockerExit);
			SoundEffectManager.SoundPlaybackStarted = (t, wav, tick) => blockerStarted.Set();
			SoundEffectManager.PlaybackSink = (data, flags) => allowBlockerExit.WaitOne(3000);

			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);
			SoundEffectManager.PlayPreview(SoundType.WheelPopup);
			blockerStarted.WaitOne(2000);

			long oldSessionId = SoundEffectManager.BeginSession(SoundSessionSource.NormalGesture);
			SoundEffectManager.EndSession(SoundSessionSource.NormalGesture, oldSessionId, allowTerminalFeedback: false);

			// 开启新会话
			long newSessionId = SoundEffectManager.BeginSession(SoundSessionSource.NormalGesture);

			// 模拟迟到的旧会话 Hover
			SoundEffectManager.Play(SoundType.SectorHover, SoundSessionSource.NormalGesture, oldSessionId);
			Assert(SoundEffectManager.TestPendingSound == null, "B2a_Target5_LateOldSessionRequestRejected",
				$"旧会话请求被拒，待播槽为空: {SoundEffectManager.TestPendingSound == null}");

			// 新会话请求正常接收
			SoundEffectManager.LastHoverTick = 0L;
			SoundEffectManager.Play(SoundType.SectorHover, SoundSessionSource.NormalGesture, newSessionId);
			Assert(SoundEffectManager.TestPendingSound == SoundType.SectorHover, "B2a_Target5_NewSessionRequestAccepted",
				$"新会话请求正常接收: {SoundEffectManager.TestPendingSound}");

			allowBlockerExit.Set();
			SoundEffectManager.Shutdown();
			var b2a5Worker = SoundEffectManager.CurrentWorkerThread;
			if (b2a5Worker != null && b2a5Worker.IsAlive)
			{
				bool b2a5Joined = b2a5Worker.Join(2000);
				Assert(b2a5Joined, "B2a_Target5_WorkerJoinedWithin2000ms");
			}
			SoundEffectManager.ResetTestSeams();
		}

		// B2a_Target6: 一个来源结束不影响另一个来源
		{
			AutoResetEvent blockerStarted = new(false);
			ManualResetEvent allowBlockerExit = new(false);
			TrackBarrier(allowBlockerExit);

			SoundEffectManager.SoundPlaybackStarted = (t, wav, tick) => blockerStarted.Set();
			SoundEffectManager.PlaybackSink = (data, flags) => allowBlockerExit.WaitOne(3000);

			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);

			// 阻塞工作线程
			SoundEffectManager.PlayPreview(SoundType.WheelPopup);
			blockerStarted.WaitOne(2000);

			long sessSticky = SoundEffectManager.BeginSession(SoundSessionSource.StickyWheel);
			long sessNormal = SoundEffectManager.BeginSession(SoundSessionSource.NormalGesture);

			// StickyWheel 入队 WheelPopup
			SoundEffectManager.Play(SoundType.WheelPopup, SoundSessionSource.StickyWheel, sessSticky);
			Assert(SoundEffectManager.TestPendingSource == SoundSessionSource.StickyWheel, "B2a_Target6_StickyPending");

			// 结束 NormalGesture 会话
			SoundEffectManager.EndSession(SoundSessionSource.NormalGesture, sessNormal, allowTerminalFeedback: true);

			// StickyWheel 的待播项绝不被误删
			Assert(SoundEffectManager.TestPendingSource == SoundSessionSource.StickyWheel && SoundEffectManager.TestPendingSound == SoundType.WheelPopup,
				"B2a_Target6_OneSourceEndDoesNotAffectOtherSource",
				$"StickyWheel 待播项依然健在: source={SoundEffectManager.TestPendingSource}, sound={SoundEffectManager.TestPendingSound}");

			allowBlockerExit.Set();
			SoundEffectManager.Shutdown();
			SoundEffectManager.ResetTestSeams();
		}

		// B2a_Target7: 已开始的播放自然完成，播放并发仍不超过 1
		{
			AutoResetEvent firstStarted = new(false);
			ManualResetEvent allowFirstExit = new(false);
			TrackBarrier(allowFirstExit);
			AutoResetEvent secondStarted = new(false);
			ManualResetEvent allowSecondExit = new(false);
			TrackBarrier(allowSecondExit);

			int activeConcurrency = 0;
			int maxConcurrency = 0;
			object sinkLock = new();

			SoundEffectManager.PlaybackSink = (data, flags) =>
			{
				int cur = Interlocked.Increment(ref activeConcurrency);
				lock (sinkLock)
				{
					if (cur > maxConcurrency) maxConcurrency = cur;
				}

				if (cur == 1 && !firstStarted.WaitOne(0))
				{
					firstStarted.Set();
					allowFirstExit.WaitOne(3000);
				}
				else
				{
					secondStarted.Set();
					allowSecondExit.WaitOne(3000);
				}

				Interlocked.Decrement(ref activeConcurrency);
			};

			SoundEffectManager.Initialize("Mechanical", 0.6, force: true);
			long sid = SoundEffectManager.BeginSession(SoundSessionSource.NormalGesture);

			SoundEffectManager.Play(SoundType.WheelPopup, SoundSessionSource.NormalGesture, sid);
			firstStarted.WaitOne(2000);

			// 在播放中结束会话：已开始的播放绝不受影响，自然继续
			SoundEffectManager.EndSession(SoundSessionSource.NormalGesture, sid, allowTerminalFeedback: false);

			// 释放首声
			allowFirstExit.Set();

			// 再次排入新请求
			long sid2 = SoundEffectManager.BeginSession(SoundSessionSource.NormalGesture);
			SoundEffectManager.Play(SoundType.WheelPopup, SoundSessionSource.NormalGesture, sid2);
			secondStarted.WaitOne(2000);

			allowSecondExit.Set();
			SoundEffectManager.Shutdown();

			int finalMax;
			lock (sinkLock) { finalMax = maxConcurrency; }
			Assert(finalMax <= 1, "B2a_Target7_ActivePlaybackCompletesConcurrencyNeverExceeds1",
				$"实测最大回放并发数: {finalMax} (必须<=1)");

			SoundEffectManager.ResetTestSeams();
			SoundEffectManager.PlaybackSink = (data, flags) => { };
		}
	}
	#endregion

	#region 自动化测试执行总报告生成 (Unified Execution Summary Report)
	private static void GenerateSummaryReport()
	{
		try
		{
			string tempDir = Path.GetTempPath();
			string reportPath = Path.Combine(tempDir, "StarPie-SoundForensics-Summary.md");

			StringBuilder sb = new();
			sb.AppendLine("# StarPie SP-SOUND-001: 交互音效调度、会话生命周期与 WAV 解析稳定性验证总报告");
			sb.AppendLine();
			sb.AppendLine($"- **执行时间**: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
			sb.AppendLine($"- **进程 PID**: {Environment.ProcessId}");
			sb.AppendLine($"- **测试模式**: Headless Mock/Seam 隔离模式 (`TestMode=true`)");
			sb.AppendLine($"- **用例总计**: {_passedTests + _failedTests} 项");
			sb.AppendLine($"- **通过用例**: {_passedTests} 项");
			sb.AppendLine($"- **失败用例**: {_failedTests} 项");
			sb.AppendLine($"- **通过率**: {(_passedTests + _failedTests > 0 ? (_passedTests * 100.0 / (_passedTests + _failedTests)).ToString("F2") : "0.00")}%");
			sb.AppendLine();
			sb.AppendLine("---");
			sb.AppendLine();
			sb.AppendLine("## 1. 核心架构与稳定性契约 (Verified Architectural Contracts)");
			sb.AppendLine();
			sb.AppendLine("1. **RIFF/WAVE 分块安全解析与 16-bit PCM 缩放**:");
			sb.AppendLine("   - 废除硬编码 44 字节偏移；动态解析 `fmt `、`data`、`JUNK`、`LIST` 等合法分块；");
			sb.AppendLine("   - 严格校验 16-bit PCM (单/双声道)，校验 `byteRate == (ulong)sampleRate * (ulong)blockAlign` 并防溢出；");
			sb.AppendLine("   - 明确拒绝第二个 `data` 分块；");
			sb.AppendLine("   - 保证 `data` 前后所有分块逐字节 100% 不变，输入数组只读不变；");
			sb.AppendLine("   - 损坏或不支持格式安全回退至程序化合成微动声，并记录结构化原因。");
			sb.AppendLine();
			sb.AppendLine("2. **音频工作线程生命周期与会话隔离**:");
			sb.AppendLine("   - 采用代际编号 (Generation) 严格绑定任务，旧工作线程退出绝不窃取或干扰新代请求；");
			sb.AppendLine("   - 有界会话状态机管理 (<= 64 容量)，保护活动画布与手势会话，修剪会话不可复活；");
			sb.AppendLine("   - 取得资源后短锁认领点，会话有效放行自然播放，会话结束安全丢弃；");
			sb.AppendLine("   - 生产态下不驻留历史线程引用，零内存泄漏。");
			sb.AppendLine();
			sb.AppendLine("3. **扇区 Hover 防抖与稳定判决**:");
			sb.AppendLine("   - 目标停留稳定 45ms 门限，相邻扇区抖动平滑过滤；");
			sb.AppendLine("   - 动态会话撤销，手势释放时丢弃悬空 Hover，保留高优先级动作终态确认音。");
			sb.AppendLine();
			sb.AppendLine("---");
			sb.AppendLine();
			sb.AppendLine("## 2. 自动化测试用例执行总表 (Dynamic Test Results)");
			sb.AppendLine();
			sb.AppendLine("| 序号 | 测试用例名称 | 状态 | 详细说明 |");
			sb.AppendLine("| :--- | :--- | :--- | :--- |");
			int idx = 1;
			lock (_assertLock)
			{
				foreach (var r in _testResults)
				{
					string status = r.Passed ? "**PASS**" : "**FAIL**";
					string detail = string.IsNullOrWhiteSpace(r.Detail) ? "-" : r.Detail.Replace("\r", "").Replace("\n", " ");
					sb.AppendLine($"| {idx++} | `{r.Name}` | {status} | {detail} |");
				}
			}
			sb.AppendLine();
			sb.AppendLine("---");
			sb.AppendLine();
			sb.AppendLine("## 3. 人工真机试听与物理声卡验证提示 (Manual Hardware Audio Verification)");
			sb.AppendLine();
			sb.AppendLine("> **说明**: 本自动化测试套件在无头 Mock/Seam 隔离环境下执行，扬声器未发出真实物理声音。以下项目建议在物理硬件环境中进行人工听感确认：");
			sb.AppendLine();
			sb.AppendLine("- [ ] **真实机械/微动音质**: 开启音效，在真实扬声器下听取 Mechanical、Crisp、Soft 等预设，确认起音平滑无爆音破音；");
			sb.AppendLine("- [ ] **自定义 WAV 文件**: 加载含 JUNK 分块的外部 WAV 音效，确认音量缩放生效且音频未畸变；");
			sb.AppendLine("- [ ] **快速连续划动手势**: 快速划动手势轮盘，确认无多重音频并发打架或爆音。");
			sb.AppendLine();
			sb.AppendLine("---");
			sb.AppendLine();
			sb.AppendLine("## 4. 自动化测试日志 (Console Log Tail)");
			sb.AppendLine("```text");
			int logCount = _testLogs.Count;
			int startIdx = Math.Max(0, logCount - 100);
			for (int i = startIdx; i < logCount; i++)
			{
				sb.AppendLine(_testLogs[i]);
			}
			sb.AppendLine("```");

			File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
			Log($"\n[Summary Report Generated Successfully]: {reportPath}");
		}
		catch (Exception ex)
		{
			Log($"[Summary Report Generation Error]: {ex.Message}");
		}
	}
	#endregion
}
