using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StarPie.Plugin;
using WinPieGestures;
using WinPieGestures.Plugins;

internal static class Program
{
    private static int _passed;
    private static int _failed;
    private const BindingFlags NonPublicStatic = BindingFlags.Static | BindingFlags.NonPublic;
    private const BindingFlags NonPublicInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    private static void Check(bool condition, string name, string detail = "")
    {
        string suffix = string.IsNullOrEmpty(detail) ? "" : $" ({detail})";
        Console.WriteLine($"[{(condition ? "PASS" : "FAIL")}] {name}{suffix}");
        if (condition)
        {
            Interlocked.Increment(ref _passed);
        }
        else
        {
            Interlocked.Increment(ref _failed);
        }
    }

    private sealed class SlowCancelContribution : IActionContribution
    {
        private readonly TaskCompletionSource<ActionResult> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _entered = new(false);
        private readonly ManualResetEventSlim _releaseCancelCallback = new(false);

        public ActionDescriptor Descriptor => new()
        {
            Id = "slowCancel",
            DisplayName = "Slow Cancel Probe",
            Kind = ActionKind.Background,
            TimeoutSeconds = 30
        };

        public IReadOnlyList<ParameterField> Parameters => Array.Empty<ParameterField>();
        public string? Validate(IReadOnlyDictionary<string, string> parameters) => null;
        public string Preview(IReadOnlyDictionary<string, string> parameters) => "SlowCancel";
        public bool WaitEntered(int ms) => _entered.Wait(ms);

        public void ReleaseAll()
        {
            _releaseCancelCallback.Set();
            _tcs.TrySetResult(ActionResult.Ok());
        }

        public async Task<ActionResult> ExecuteAsync(PluginActionInput input, CancellationToken cancellationToken)
        {
            using var reg = cancellationToken.Register(() =>
            {
                // Simulate a misbehaving synchronous cancellation callback
                _releaseCancelCallback.Wait(1800);
            });
            _entered.Set();
            return await _tcs.Task.ConfigureAwait(false);
        }
    }

    private sealed class SyncBlockingSequentialContribution : IActionContribution
    {
        private readonly ManualResetEventSlim _releaseSyncBlock = new(false);
        private readonly ManualResetEventSlim _entered = new(false);

        public ActionDescriptor Descriptor => new()
        {
            Id = "syncBlockSeq",
            DisplayName = "Sync Blocking Sequential Probe",
            Kind = ActionKind.Sequential,
            TimeoutSeconds = 1
        };

        public IReadOnlyList<ParameterField> Parameters => Array.Empty<ParameterField>();
        public string? Validate(IReadOnlyDictionary<string, string> parameters) => null;
        public string Preview(IReadOnlyDictionary<string, string> parameters) => "SyncBlockSeq";
        public bool WaitEntered(int ms) => _entered.Wait(ms);
        public void Release() => _releaseSyncBlock.Set();

        public Task<ActionResult> ExecuteAsync(PluginActionInput input, CancellationToken cancellationToken)
        {
            _entered.Set();
            // Block synchronously BEFORE returning any Task
            _releaseSyncBlock.Wait(5000);
            return Task.FromResult(ActionResult.Ok());
        }
    }

    // ------------------------------------------------------------------ [1] ConfigManager
    private static void Test1_ConfigCorruptProtectionAndSavePropagation(string sandboxLocalAppData)
    {
        Console.WriteLine("\n=== [1] ConfigManager: Corrupt Fallback Protection & Save Result Propagation ===");
        string starPieDir = Path.Combine(sandboxLocalAppData, "StarPie");
        Directory.CreateDirectory(starPieDir);
        string configPath = Path.Combine(starPieDir, "config.json");
        string corruptContent = "{ \"corrupt_user_asset\": true, INVALID_JSON ";
        File.WriteAllText(configPath, corruptContent);

        ConfigManager.DisableAutoStartSync = true;
        ConfigManager.LoadConfig();

        Check(ConfigManager.IsFallbackConfig, "1.1 Corrupt config.json sets IsFallbackConfig = true");
        string[] backups = Directory.GetFiles(starPieDir, "config.json.corrupt.*");
        Check(backups.Length >= 1, "1.2 Corrupt config.json is backed up to .corrupt.*", $"backups={backups.Length}");

        // Simulate automatic save (e.g., SettingsWindow closing or auto-save debounce)
        bool autoSaveResult = ConfigManager.SaveConfig();
        string diskAfterAutoSave = File.ReadAllText(configPath);
        Check(!autoSaveResult, "1.3 SaveConfig() returns false while IsFallbackConfig is true");
        Check(diskAfterAutoSave == corruptContent, "1.4 Automatic SaveConfig() does not overwrite corrupt config.json on disk");

        // Explicit recovery via ResetToDefault clears IsFallbackConfig and writes valid config
        ConfigManager.ResetToDefault("默认配置");
        Check(!ConfigManager.IsFallbackConfig, "1.5 Explicit ResetToDefault clears IsFallbackConfig");
        Check(ConfigManager.SaveConfig(), "1.6 SaveConfig() succeeds after explicit recovery");

        // Verify atomic profile mirror write leaves no .tmp behind
        string profileMirror = Path.Combine(ConfigManager.ConfigsFolder, "默认配置.json");
        Check(File.Exists(profileMirror) && !File.Exists(profileMirror + ".tmp"), "1.7 Profile mirror exists without leftover .tmp");

        // Verify SaveConfig failure propagates through ImportConfig and SwitchToConfig when config.json is locked
        string validImportPath = Path.Combine(starPieDir, "external_valid.json");
        Check(ConfigManager.ExportConfig(validImportPath), "1.8 ExportConfig writes valid external file");

        using (var fileLock = new FileStream(configPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            bool importWhileLocked = ConfigManager.ImportConfig(validImportPath);
            Check(!importWhileLocked, "1.9 ImportConfig returns false when SaveConfig fails due to locked config.json");

            bool switchWhileLocked = ConfigManager.SwitchToConfig("默认配置");
            Check(!switchWhileLocked, "1.10 SwitchToConfig returns false when SaveConfig fails due to locked config.json");
        }

        bool importUnlocked = ConfigManager.ImportConfig(validImportPath);
        Check(importUnlocked, "1.11 ImportConfig returns true when config.json is writable");
    }

    // ------------------------------------------------------------------ [2] Plugin Timeout Boundaries
    private static async Task Test2_PluginSyncBlockingTimeouts()
    {
        Console.WriteLine("\n=== [2] PluginHost / PluginInstance / PluginInvoker: Synchronous Blocking Timeout Boundaries ===");
        PluginHost.HeadlessMode = true;
        PluginHost.Initialize();

        string pluginId = "demo.timeout.probe";
        var owner = new PluginInstance(
            pluginId,
            new PluginRegistryEntry { Id = pluginId, Name = "Timeout Probe", Enabled = true },
            new PluginScanResult { Accepted = true });
        typeof(PluginInstance).GetMethod("OpenInvocationGate", NonPublicInstance)!.Invoke(owner, null);
        typeof(PluginInstance).GetMethod("SetState", NonPublicInstance)!.Invoke(owner, new object[] { PluginRuntimeState.Active });
        typeof(PluginInstance).GetField("_plugin", NonPublicInstance)!.SetValue(owner, new DummyPlugin());

        var instances = (Dictionary<string, PluginInstance>)typeof(PluginHost).GetField("Instances", NonPublicStatic)!.GetValue(null)!;
        instances[pluginId] = owner;

        try
        {
            // 2A: Slow synchronous CancellationToken callback during StopPluginAsync
            var slowCancel = new SlowCancelContribution();
            var bgReg = new PluginActionRegistration
            {
                PluginId = pluginId,
                ShortId = "slowCancel",
                FullId = $"{pluginId}.slowCancel",
                Contribution = slowCancel,
                DisplayName = "Slow Cancel",
                Kind = ActionKind.Background,
                TimeoutSeconds = 30
            };

            var bgOutcome = PluginInvoker.Invoke(owner, bgReg, new Dictionary<string, string>(), new PluginCallCoordinator());
            Check(bgOutcome.QueuedToBackground && slowCancel.WaitEntered(2000), "2.1 Background action entered and registered slow cancel callback");

            var swStop = Stopwatch.StartNew();
            PluginStopResult stopRes = await PluginHost.DisableAsync(pluginId, PluginStopReason.SelfTest, TimeSpan.FromMilliseconds(150));
            swStop.Stop();

            Check(stopRes.Status == PluginStopStatus.Pending, "2.2 StopPluginAsync returns Pending on grace timeout", $"status={stopRes.Status}");
            Check(swStop.ElapsedMilliseconds < 900, "2.3 StopPluginAsync is not blocked by synchronous 1800ms CancellationToken callback", $"elapsed={swStop.ElapsedMilliseconds}ms");

            slowCancel.ReleaseAll();
            PluginStopResult finalStop = await PluginHost.DisableAsync(pluginId, PluginStopReason.SelfTest, TimeSpan.FromSeconds(5));
            Check(finalStop.IsFullyStopped, "2.4 StopPluginAsync completes after slow callback and call finish");

            // Reset owner to Active for 2B
            owner.Entry.Enabled = true;
            typeof(PluginInstance).GetMethod("OpenInvocationGate", NonPublicInstance)!.Invoke(owner, null);
            typeof(PluginInstance).GetMethod("SetState", NonPublicInstance)!.Invoke(owner, new object[] { PluginRuntimeState.Active });

            // 2B: Synchronous blocking prologue inside Sequential ExecuteAsync
            var syncBlock = new SyncBlockingSequentialContribution();
            var seqReg = new PluginActionRegistration
            {
                PluginId = pluginId,
                ShortId = "syncBlockSeq",
                FullId = $"{pluginId}.syncBlockSeq",
                Contribution = syncBlock,
                DisplayName = "Sync Block Seq",
                Kind = ActionKind.Sequential,
                TimeoutSeconds = 1
            };

            var swSeq = Stopwatch.StartNew();
            var seqOutcome = PluginInvoker.Invoke(owner, seqReg, new Dictionary<string, string>(), new PluginCallCoordinator());
            swSeq.Stop();

            Check(!seqOutcome.Success, "2.5 Sequential action that blocks synchronously before returning Task fails on timeout");
            Check(swSeq.ElapsedMilliseconds < 2500, "2.6 Sequential synchronous block bounded by 1s timeout instead of 5s hang", $"elapsed={swSeq.ElapsedMilliseconds}ms");
            Check(owner.ActiveCallCount == 1, "2.7 Invocation lease remains held while timed-out synchronous prologue is still running", $"active={owner.ActiveCallCount}");

            syncBlock.Release();
            bool drained = SpinWait.SpinUntil(() => owner.ActiveCallCount == 0, 2000);
            Check(drained, "2.8 Invocation lease is released once timed-out synchronous work finishes");

            var heldLeases = new List<PluginInvocationLease>();
            for (int i = 0; i < PluginInstance.MaxConcurrentInvocations; i++)
            {
                if (owner.TryAcquireInvocation(PluginCallKind.ActionExecution, out var l, out _) && l != null)
                {
                    heldLeases.Add(l);
                }
            }
            bool rejectedWhenFull = !owner.TryAcquireInvocation(PluginCallKind.ActionExecution, out _, out string capError);
            foreach (var l in heldLeases)
            {
                l.Dispose();
            }
            Check(rejectedWhenFull && capError.Contains(PluginInstance.MaxConcurrentInvocations.ToString()),
                "2.9 TryAcquireInvocation rejects new calls when active calls reach MaxConcurrentInvocations",
                capError);
        }
        finally
        {
            instances.Remove(pluginId);
        }
    }

    private sealed class DummyPlugin : IStarPiePlugin
    {
        public void Initialize(IPluginContext context) { }
        public void Shutdown() { }
    }

    // ------------------------------------------------------------------ [3] Failed Initialize ALC Unload
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static PluginInstance LoadFailingFixturePlugin(string dllPath)
    {
        var scan = PluginScanner.ScanSelectedDll(dllPath);
        Check(scan.Accepted, "3.1 AuditOptFixture DLL accepted by PluginScanner");

        var owner = new PluginInstance(
            "demo.audit.opt.fixture",
            new PluginRegistryEntry
            {
                Id = "demo.audit.opt.fixture",
                Name = "Audit Opt Fixture",
                Enabled = true,
                ExternalPath = dllPath
            },
            scan);

        owner.Settings.Set("failInit", "true");
        bool loaded = owner.Load(out string failureReason);
        Check(!loaded, "3.2 Initialize exception rejects plugin load", failureReason);
        return owner;
    }

    private static void Test3_FailedInitializeRevokesSubscriptionsAndUnloadsAlc()
    {
        Console.WriteLine("\n=== [3] PluginInstance: Failed Initialize Event Subscription Revocation & ALC Unload ===");
        string dllPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "../../../Fixture/bin/Release/net8.0-windows/StarPie.AuditOptFixture.dll"));

        PluginInstance failedOwner = LoadFailingFixturePlugin(dllPath);

        // Trigger language change to make sure no stale callback fires
        I18n.SetLanguage("en-US");
        I18n.SetLanguage("zh-CN");

        bool alcCollected = failedOwner.WaitForUnloadVerdict(4000);
        Check(alcCollected, "3.3 Collectible ALC is unloaded after Initialize throws following event subscription");
    }

    // ------------------------------------------------------------------ [4] & [5] GestureController & App Memory Trim
    private static void Test4And5_HotpathLockAndMemoryTrimSourceInvariants()
    {
        Console.WriteLine("\n=== [4 & 5] GestureController Lock Scope & Routine Memory Trim Policy ===");
        string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        string gestureCode = File.ReadAllText(Path.Combine(repoRoot, "WinPieGestures", "GestureController.cs"));
        string appCode = File.ReadAllText(Path.Combine(repoRoot, "WinPieGestures", "App.xaml.cs"));

        // Verify GestureController constructs new RadialWindow outside lock (_uiUpdateSync)
        Check(!gestureCode.Contains("window = _radialWindow ??= new RadialWindow(center, profile);"),
            "4.1 GestureController no longer constructs RadialWindow inline inside _uiUpdateSync lock");
        Check(gestureCode.Contains("new RadialWindow(center, profile)"),
            "4.2 GestureController still constructs RadialWindow on demand outside _uiUpdateSync lock");

        // Verify App.xaml.cs no longer calls TrimMemory(force: true) on silent startup or SettingsWindow_Closed
        Check(!appCode.Contains("TrimMemory(force: true)"),
            "5.1 App.xaml.cs does not invoke blocking TrimMemory(force: true) on silent startup or SettingsWindow_Closed");
        Check(appCode.Contains("TrimMemory(force: false)"),
            "5.2 App.xaml.cs uses non-blocking TrimMemory(force: false) for routine background trimming");
    }

    // ------------------------------------------------------------------ [6] QuickSearch & NativeSearchEngine
    private static async Task Test6_NativeSearchSnapshotSafetyAndAsyncEmptyQuery()
    {
        Console.WriteLine("\n=== [6] QuickSearchWindow & NativeSearchEngine: Snapshot Cache Clear & Async Empty Search ===");
        var emptyResults = await NativeSearchEngine.SearchAsync("", "App", 40);
        Check(emptyResults != null && emptyResults.Count > 0, "6.1 SearchAsync handles empty query asynchronously and returns recommendations", $"count={emptyResults?.Count ?? 0}");

        // Concurrent enumeration of SearchAsync while ClearCaches() is called repeatedly
        bool faulted = false;
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(600));
        var readerTask = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    var res = await NativeSearchEngine.SearchAsync("c", "App", 20);
                    foreach (var item in res)
                    {
                        _ = item.FileName;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Reader exception: " + ex);
                    faulted = true;
                    break;
                }
            }
        });

        var clearerTask = Task.Run(() =>
        {
            while (!cts.IsCancellationRequested)
            {
                NativeSearchEngine.ClearCaches();
                Thread.Sleep(5);
            }
        });

        await Task.WhenAll(readerTask, clearerTask);
        Check(!faulted, "6.2 Concurrent SearchAsync and ClearCaches() never throw collection-modified exceptions");

        string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        string quickSearchCode = File.ReadAllText(Path.Combine(repoRoot, "WinPieGestures", "QuickSearchWindow.xaml.cs"));
        Check(!quickSearchCode.Contains("NativeSearchEngine.GetInitialRecommendations("),
            "6.3 QuickSearchWindow does not invoke synchronous GetInitialRecommendations on UI thread");
        int showIdx = quickSearchCode.IndexOf("Show();", StringComparison.Ordinal);
        int triggerIdx = quickSearchCode.IndexOf("TriggerSearch(immediate: true);", StringComparison.Ordinal);
        Check(showIdx > 0 && triggerIdx > showIdx,
            "6.4 QuickSearchWindow.ShowAndFocus calls Show() before TriggerSearch(immediate: true)");
    }

    // ------------------------------------------------------------------ [7] IconHelper Custom Image Decode Budget
    private static void Test7_CustomImageDecodePixelBudget(string sandboxLocalAppData)
    {
        Console.WriteLine("\n=== [7] IconHelper: Custom Image Decode Pixel Budget & Aspect Ratio ===");
        string customDir = Path.Combine(sandboxLocalAppData, "StarPie", "CustomIcons");
        Directory.CreateDirectory(customDir);

        string largePngPath = Path.Combine(customDir, "large_photo.png");
        string smallPngPath = Path.Combine(customDir, "small_icon.png");

        CreateSolidPng(largePngPath, 1600, 1200);
        CreateSolidPng(smallPngPath, 48, 48);

        IconHelper.ClearCache();
        IconHelper.ClearPinnedIcons();

        var decodedLarge = IconHelper.GetCustomImageSource(largePngPath) as BitmapSource;
        Check(decodedLarge != null && decodedLarge.IsFrozen, "7.1 Large custom image loads and is Frozen");
        Check(decodedLarge != null && decodedLarge.PixelWidth <= 256 && decodedLarge.PixelHeight <= 256,
            "7.2 Large 1600x1200 custom image is decoded down to display/DPI budget (<= 256px)",
            $"actual={decodedLarge?.PixelWidth}x{decodedLarge?.PixelHeight}");
        Check(decodedLarge != null && decodedLarge.PixelWidth == 256 && decodedLarge.PixelHeight == 192,
            "7.3 Decoded large custom image preserves 4:3 aspect ratio (256x192)",
            $"actual={decodedLarge?.PixelWidth}x{decodedLarge?.PixelHeight}");

        var decodedSmall = IconHelper.GetCustomImageSource(smallPngPath) as BitmapSource;
        Check(decodedSmall != null && decodedSmall.PixelWidth == 48 && decodedSmall.PixelHeight == 48,
            "7.4 Small 48x48 custom image is NOT upscaled when decoded",
            $"actual={decodedSmall?.PixelWidth}x{decodedSmall?.PixelHeight}");
    }

    private static void CreateSolidPng(string path, int width, int height)
    {
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 0x3B;
            pixels[i + 1] = 0x82;
            pixels[i + 2] = 0xF6;
            pixels[i + 3] = 0xFF;
        }
        BitmapSource bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(fs);
    }

    // ------------------------------------------------------------------ [8] PluginInstance.FlushHealth
    private static void Test8_FlushHealthNoDoubleAccumulation()
    {
        Console.WriteLine("\n=== [8] PluginInstance.FlushHealth: Consistent Batch Delta Without Double Accumulation ===");
        string pluginId = "demo.health.flush.probe";
        var owner = new PluginInstance(
            pluginId,
            new PluginRegistryEntry { Id = pluginId, Name = "Health Probe", Enabled = true },
            new PluginScanResult { Accepted = true });

        // Batch 1: 50 invocations of 10ms each = 500ms
        for (int i = 0; i < 49; i++)
        {
            owner.RecordInvoke(true, 10.0, null);
        }
        owner.FlushHealth();

        // Batch 2: 50 invocations of 10ms each = 500ms (total across 2 batches = 100 calls, 1000ms)
        for (int i = 0; i < 51; i++)
        {
            owner.RecordInvoke(true, 10.0, null);
        }
        owner.FlushHealth();

        PluginHealthEntry entry = PluginRegistryStore.HealthOf(pluginId);
        Check(entry != null, "8.1 Health entry persisted to health.json");
        Check(entry != null && entry.InvokeCount == 100, "8.2 Total InvokeCount across two flushes is 100", $"actual={entry?.InvokeCount}");
        Check(entry != null && Math.Abs(entry.TotalInvokeMs - 1000.0) < 0.01,
            "8.3 TotalInvokeMs across two flushes is 1000ms (not 1500ms double-accumulated)",
            $"actual={entry?.TotalInvokeMs:F2}ms");
        Check(entry != null && Math.Abs(entry.AverageInvokeMs - 10.0) < 0.01,
            "8.4 Persisted AverageInvokeMs is 10.0ms",
            $"actual={entry?.AverageInvokeMs:F2}ms");
    }

    [STAThread]
    private static int Main()
    {
        string? originalLocalAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        string sandboxRoot = Path.Combine(Path.GetTempPath(), "StarPie-AuditOptTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandboxRoot);
        Environment.SetEnvironmentVariable("LOCALAPPDATA", sandboxRoot);
        PluginPaths.OverrideRootsForTesting(Path.Combine(sandboxRoot, "plugin-data"), Path.Combine(sandboxRoot, "plugin-scan"));

        try
        {
            Test1_ConfigCorruptProtectionAndSavePropagation(sandboxRoot);
            Test2_PluginSyncBlockingTimeouts().GetAwaiter().GetResult();
            Test3_FailedInitializeRevokesSubscriptionsAndUnloadsAlc();
            Test4And5_HotpathLockAndMemoryTrimSourceInvariants();
            Test6_NativeSearchSnapshotSafetyAndAsyncEmptyQuery().GetAwaiter().GetResult();
            Test7_CustomImageDecodePixelBudget(sandboxRoot);
            Test8_FlushHealthNoDoubleAccumulation();
        }
        catch (Exception ex)
        {
            Check(false, "Unhandled exception in test suite", ex.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("LOCALAPPDATA", originalLocalAppData);
            try
            {
                if (Directory.Exists(sandboxRoot))
                {
                    Directory.Delete(sandboxRoot, recursive: true);
                }
            }
            catch
            {
            }
        }

        Console.WriteLine($"\n==========================================================");
        Console.WriteLine($"AuditOptTests Summary: {_passed} passed, {_failed} failed");
        Console.WriteLine($"==========================================================");
        return _failed == 0 ? 0 : 1;
    }
}
