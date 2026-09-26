using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using WinPieGestures;
using WinPieGestures.Plugins;

public class Program
{
    private static int _failedCount = 0;
    private static int _passedCount = 0;

    [STAThread]
    public static int Main(string[] args)
    {
        // 独立子进程分支 1：固定图标并创建持久化快照
        if (args.Length >= 3 && args[0] == "--subproc-pin")
        {
            string targetExe = args[1];
            string localAppDataDir = args[2];
            Environment.SetEnvironmentVariable("LOCALAPPDATA", localAppDataDir);
            IconHelper.ClearPinnedIcons();
            IconHelper.ClearCache();
            IconHelper.PinIcon(targetExe);
            string cacheFile = IconHelper.GetPersistentIconCachePath(targetExe);
            if (!File.Exists(cacheFile))
            {
                Console.Error.WriteLine($"[Subproc1-Error] Cache file not created on disk: {cacheFile}");
                return 2;
            }
            Console.WriteLine($"[Subproc1-OK] Pinned and created cache: {cacheFile}");
            return 0;
        }

        // 独立子进程分支 2：冷启动（全新进程内存干净无状态），检测源文件已不可用，从持久化快照读取
        if (args.Length >= 3 && args[0] == "--subproc-read")
        {
            string targetExe = args[1];
            string localAppDataDir = args[2];
            Environment.SetEnvironmentVariable("LOCALAPPDATA", localAppDataDir);
            if (!IconHelper.IsUnavailableFileSystemSource(targetExe))
            {
                Console.Error.WriteLine($"[Subproc2-Error] Target exe {targetExe} not recognized as unavailable source");
                return 3;
            }
            BitmapSource? icon = IconHelper.GetIcon(targetExe);
            if (icon == null)
            {
                Console.Error.WriteLine($"[Subproc2-Error] Failed to load icon from persistent cache for {targetExe}");
                return 4;
            }
            if (icon.PixelWidth <= 0 || icon.PixelHeight <= 0)
            {
                Console.Error.WriteLine($"[Subproc2-Error] Invalid icon dimensions: {icon.PixelWidth}x{icon.PixelHeight}");
                return 5;
            }
            if (!icon.IsFrozen)
            {
                Console.Error.WriteLine($"[Subproc2-Error] Restored icon is not frozen");
                return 6;
            }
            Console.WriteLine($"[Subproc2-OK] Loaded frozen icon from cache: {icon.PixelWidth}x{icon.PixelHeight}");
            return 0;
        }

        // 主测试套件流程
        Console.WriteLine("==========================================================");
        Console.WriteLine("StarPie PR #142 Icon Cache & Fallback Regression Suite");
        Console.WriteLine("==========================================================");

        string tempBase = Path.Combine(Path.GetTempPath(), "StarPie-IconRegTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempBase);
        string localAppData = Path.Combine(tempBase, "LocalAppData");
        Directory.CreateDirectory(localAppData);
        Environment.SetEnvironmentVariable("LOCALAPPDATA", localAppData);

        try
        {
            Test1_RestartAfterSourceDeleted(tempBase, localAppData);
            Test2_BrokenShortcutFallback(tempBase, localAppData);
            Test_RecycleBinShortcut(tempBase, localAppData);
            Test3_CorruptedCacheSelfHealing(tempBase, localAppData);
            Test4_UnwritableCacheFaultTolerance(tempBase, localAppData);
            Test5_FullConfigurationEntityCoverage(tempBase, localAppData);
            Test6_PluginIconsUnaffected();

            Console.WriteLine("==========================================================");
            Console.WriteLine($"Total: {_passedCount + _failedCount}, Passed: {_passedCount}, Failed: {_failedCount}");
            Console.WriteLine("==========================================================");

            return _failedCount == 0 ? 0 : 1;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempBase))
                {
                    Directory.Delete(tempBase, true);
                }
            }
            catch { }
        }
    }

    private static void Assert(bool condition, string testName, string detail = "")
    {
        if (condition)
        {
            _passedCount++;
            Console.WriteLine($"[PASS] {testName} {(string.IsNullOrEmpty(detail) ? "" : " - " + detail)}");
        }
        else
        {
            _failedCount++;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FAIL] {testName} {(string.IsNullOrEmpty(detail) ? "" : " - " + detail)}");
            Console.ResetColor();
        }
    }

    private static string GetSystemNotepadPath()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");
    }

    private static void CreateShortcut(string lnkPath, string targetPath)
    {
        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null)
        {
            throw new InvalidOperationException("WScript.Shell COM object not available");
        }
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(lnkPath);
        shortcut.TargetPath = targetPath;
        shortcut.Save();
    }

    private static int RunSubprocess(string mode, string targetExe, string localAppDataDir, out string stdout, out string stderr)
    {
        ProcessStartInfo psi = new ProcessStartInfo();
        string? entryAssembly = Assembly.GetEntryAssembly()?.Location;
        string processPath = Environment.ProcessPath ?? "";

        if (File.Exists(processPath) && processPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !processPath.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            psi.FileName = processPath;
            psi.ArgumentList.Add(mode);
            psi.ArgumentList.Add(targetExe);
            psi.ArgumentList.Add(localAppDataDir);
        }
        else if (!string.IsNullOrEmpty(entryAssembly) && File.Exists(entryAssembly))
        {
            psi.FileName = "dotnet";
            psi.ArgumentList.Add(entryAssembly);
            psi.ArgumentList.Add(mode);
            psi.ArgumentList.Add(targetExe);
            psi.ArgumentList.Add(localAppDataDir);
        }
        else
        {
            psi.FileName = processPath;
            psi.ArgumentList.Add(mode);
            psi.ArgumentList.Add(targetExe);
            psi.ArgumentList.Add(localAppDataDir);
        }

        psi.Environment["LOCALAPPDATA"] = localAppDataDir;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;

        using Process proc = Process.Start(psi)!;
        stdout = proc.StandardOutput.ReadToEnd();
        stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        return proc.ExitCode;
    }

    /// <summary>
    /// 测试 1：源文件被删后的持久化缓存恢复。
    /// 包含：
    /// 1A. 单进程内重置缓存模拟重启；
    /// 1B. 严格跨两个独立 OS 进程验证快照创建及源文件删除后的冷启动读取。
    /// </summary>
    private static void Test1_RestartAfterSourceDeleted(string tempBase, string localAppData)
    {
        Console.WriteLine("\n--- Running Test 1A: In-Process Restart Simulation After Source File Deleted ---");

        string testAppExe = Path.Combine(tempBase, "app_to_delete.exe");
        File.Copy(GetSystemNotepadPath(), testAppExe, true);

        // 1. 首次固定图标，生成持久化快照
        IconHelper.ClearPinnedIcons();
        IconHelper.ClearCache();
        IconHelper.PinIcon(testAppExe);

        string cacheFile = IconHelper.GetPersistentIconCachePath(testAppExe);
        Assert(File.Exists(cacheFile), "Test1_SnapshotCreated", $"Cache file: {Path.GetFileName(cacheFile)}");

        // 2. 模拟外部程序被卸载/删除
        File.Delete(testAppExe);
        Assert(!File.Exists(testAppExe), "Test1_SourceFileDeleted", "Source exe is deleted from disk");

        // 3. 模拟程序重启（内存静态缓存全部清空）
        IconHelper.ClearPinnedIcons();
        IconHelper.ClearCache();

        // 4. 重启后获取图标
        Assert(IconHelper.IsUnavailableFileSystemSource(testAppExe), "Test1_SourceDetectedUnavailable", "Deleted file recognized as unavailable");
        BitmapSource? loadedIcon = IconHelper.GetIcon(testAppExe);
        Assert(loadedIcon != null, "Test1_LoadedFromCache", "Icon successfully restored from persistent cache");
        if (loadedIcon != null)
        {
            Assert(loadedIcon.PixelWidth > 0 && loadedIcon.PixelHeight > 0, "Test1_IconValidDimensions", $"Dimensions: {loadedIcon.PixelWidth}x{loadedIcon.PixelHeight}");
            Assert(loadedIcon.IsFrozen, "Test1_IconIsFrozen", "Cached icon is properly frozen");
        }

        Console.WriteLine("\n--- Running Test 1B: Two Independent Processes Snapshot Creation & Cold Read ---");
        string twoProcExe = Path.Combine(tempBase, "two_proc_test_app.exe");
        File.Copy(GetSystemNotepadPath(), twoProcExe, true);

        // 进程 1：固定图标生成持久化快照
        int proc1Exit = RunSubprocess("--subproc-pin", twoProcExe, localAppData, out string out1, out string err1);
        Assert(proc1Exit == 0, "Test1_TwoProc_Process1PinnedAndCreatedSnapshot", $"Process 1 exited with 0 (stdout: {out1.Trim()}, stderr: {err1.Trim()})");

        string twoProcCache = IconHelper.GetPersistentIconCachePath(twoProcExe);
        Assert(File.Exists(twoProcCache), "Test1_TwoProc_SnapshotFileExistsOnDisk", $"Snapshot verified on disk: {Path.GetFileName(twoProcCache)}");

        // 父进程：删除源可执行文件
        File.Delete(twoProcExe);
        Assert(!File.Exists(twoProcExe), "Test1_TwoProc_SourceFileDeleted", "Source executable deleted from disk before Process 2 launch");

        // 进程 2：全新独立 OS 进程，冷启动从持久化缓存读取
        int proc2Exit = RunSubprocess("--subproc-read", twoProcExe, localAppData, out string out2, out string err2);
        Assert(proc2Exit == 0, "Test1_TwoProc_Process2RestoredFromCache", $"Process 2 cold read exited with 0 (stdout: {out2.Trim()}, stderr: {err2.Trim()})");
    }

    /// <summary>
    /// 测试 2：快捷方式文件仍在但目标程序失效时，绝不覆盖已有有效快照，且优先回退到有效快照。
    /// </summary>
    private static void Test2_BrokenShortcutFallback(string tempBase, string localAppData)
    {
        Console.WriteLine("\n--- Running Test 2: Broken Shortcut Fallback & Protection ---");

        string targetExe = Path.Combine(tempBase, "broken_target.exe");
        string shortcutLnk = Path.Combine(tempBase, "broken_test.lnk");
        File.Copy(GetSystemNotepadPath(), targetExe, true);
        CreateShortcut(shortcutLnk, targetExe);

        // 1. 初始状态：目标存在，生成有效快照
        IconHelper.ClearPinnedIcons();
        IconHelper.ClearCache();
        IconHelper.PinIcon(shortcutLnk);

        string cacheFile = IconHelper.GetPersistentIconCachePath(shortcutLnk);
        Assert(File.Exists(cacheFile), "Test2_InitialSnapshotExists", "Valid snapshot created for shortcut");
        byte[] initialBytes = File.ReadAllBytes(cacheFile);
        string initialHash = Convert.ToHexString(SHA256.HashData(initialBytes));

        // 2. 删除目标程序，快捷方式 .lnk 依然保留在桌面
        File.Delete(targetExe);
        Assert(File.Exists(shortcutLnk), "Test2_LnkRemains", "Shortcut file still exists on disk");
        Assert(!File.Exists(targetExe), "Test2_TargetDeleted", "Shortcut target program deleted from disk");

        // 3. 验证失效判定
        Assert(IconHelper.IsBrokenShortcut(shortcutLnk), "Test2_IsBrokenShortcutDetected", "Shortcut target invalidity correctly identified");
        Assert(IconHelper.IsUnavailableFileSystemSource(shortcutLnk), "Test2_IsUnavailableSource", "Broken shortcut classified as unavailable file system source");

        // 4. 模拟进程重启，重新触发 PinIcon 与 GetIcon
        IconHelper.ClearPinnedIcons();
        IconHelper.ClearCache();
        IconHelper.PinIcon(shortcutLnk);

        // 5. 核心断言：快照文件绝未被 Windows 通用空白占位图标覆盖！
        byte[] currentBytes = File.ReadAllBytes(cacheFile);
        string currentHash = Convert.ToHexString(SHA256.HashData(currentBytes));
        Assert(initialHash == currentHash, "Test2_SnapshotNotOverwritten", $"Snapshot hash intact: {currentHash} (size={currentBytes.Length})");

        // 6. 获取图标依然返回原有效快照
        BitmapSource? restoredIcon = IconHelper.GetIcon(shortcutLnk);
        Assert(restoredIcon != null, "Test2_RestoredValidIcon", "GetIcon returns valid persistent snapshot rather than placeholder or null");
    }

    /// <summary>
    /// 回收站（Shell PIDL）快捷方式回归测试：有效虚拟快捷方式绝不得误判为失效，且能成功提取图标并生成快照。
    /// </summary>
    private static void Test_RecycleBinShortcut(string tempBase, string localAppData)
    {
        Console.WriteLine("\n--- Running Test: Recycle Bin (Shell PIDL) Shortcut Support ---");

        string recycleBinLnk = Path.Combine(tempBase, "recycle_bin.lnk");
        CreateShortcut(recycleBinLnk, "::{645FF040-5081-101B-9F08-00AA002F954E}");

        IconHelper.ClearPinnedIcons();
        IconHelper.ClearCache();

        bool isBroken = IconHelper.IsBrokenShortcut(recycleBinLnk);
        Assert(!isBroken, "Test_RecycleBin_NotClassifiedBroken", "Recycle bin shortcut must NOT be classified as broken");

        bool isUnavailable = IconHelper.IsUnavailableFileSystemSource(recycleBinLnk);
        Assert(!isUnavailable, "Test_RecycleBin_NotClassifiedUnavailable", "Recycle bin shortcut must NOT be classified as unavailable file source");

        IconHelper.PinIcon(recycleBinLnk);
        BitmapSource? icon = IconHelper.GetIcon(recycleBinLnk);
        Assert(icon != null, "Test_RecycleBin_IconExtracted", "Valid icon successfully extracted for Recycle Bin shortcut");
        if (icon != null)
        {
            Assert(icon.PixelWidth > 0 && icon.PixelHeight > 0, "Test_RecycleBin_ValidDimensions", $"Dimensions: {icon.PixelWidth}x{icon.PixelHeight}");
        }

        string cacheFile = IconHelper.GetPersistentIconCachePath(recycleBinLnk);
        Assert(File.Exists(cacheFile), "Test_RecycleBin_SnapshotPersisted", $"Snapshot persisted to cache: {Path.GetFileName(cacheFile)}");
    }

    /// <summary>
    /// 测试 3：缓存文件损坏自愈（Corrupted cache self-healing）。
    /// </summary>
    private static void Test3_CorruptedCacheSelfHealing(string tempBase, string localAppData)
    {
        Console.WriteLine("\n--- Running Test 3: Corrupted Cache Self-Healing ---");

        string appExe = Path.Combine(tempBase, "heal_test_app.exe");
        File.Copy(GetSystemNotepadPath(), appExe, true);

        // 1. 生成正常快照
        IconHelper.ClearPinnedIcons();
        IconHelper.ClearCache();
        IconHelper.PinIcon(appExe);

        string cacheFile = IconHelper.GetPersistentIconCachePath(appExe);
        Assert(File.Exists(cacheFile), "Test3_InitialCacheCreated", "Cache file created");

        // 2. 人为损坏快照文件（注入非法二进制数据）
        File.WriteAllBytes(cacheFile, new byte[] { 0xFF, 0x00, 0xAA, 0x55, 0x12, 0x34 });

        // 3. 尝试直接加载损坏快照：不崩溃且返回 null，同时清理无效脏文件
        BitmapSource? corruptLoad = IconHelper.LoadPersistentIconSnapshot(appExe);
        Assert(corruptLoad == null, "Test3_CorruptSnapshotHandledSafely", "LoadPersistentIconSnapshot handled corruption gracefully without throwing");

        // 4. 重置内存缓存，再次获取图标：程序自动重新提取源文件并自愈快照文件
        IconHelper.ClearPinnedIcons();
        IconHelper.ClearCache();
        BitmapSource? healedIcon = IconHelper.GetIcon(appExe);
        Assert(healedIcon != null, "Test3_IconExtractedAgain", "Icon re-extracted from original source file");

        // 重新调用 PinIcon 触发快照自愈落盘
        IconHelper.PinIcon(appExe);
        Assert(File.Exists(cacheFile), "Test3_CacheFileRecreated", "Cache file successfully re-created");
        BitmapSource? reloadedSnapshot = IconHelper.LoadPersistentIconSnapshot(appExe);
        Assert(reloadedSnapshot != null, "Test3_SnapshotSelfHealed", "Healed snapshot loaded successfully as valid BitmapSource");
    }

    /// <summary>
    /// 测试 4：缓存目录不可写容错（Unwritable cache directory fault tolerance）。
    /// </summary>
    private static void Test4_UnwritableCacheFaultTolerance(string tempBase, string localAppData)
    {
        Console.WriteLine("\n--- Running Test 4: Unwritable Cache Fault Tolerance ---");

        string origLocalAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "";
        string unwritableSandbox = Path.Combine(tempBase, "UnwritableSandbox");
        Directory.CreateDirectory(unwritableSandbox);

        // 在 StarPie 目录下创建一个名为 IconCache 的只读文件（阻止创建同名目录）
        string starPieDir = Path.Combine(unwritableSandbox, "StarPie");
        Directory.CreateDirectory(starPieDir);
        string blockingFile = Path.Combine(starPieDir, "IconCache");
        File.WriteAllText(blockingFile, "BLOCKING_FILE");

        try
        {
            Environment.SetEnvironmentVariable("LOCALAPPDATA", unwritableSandbox);
            string appExe = Path.Combine(tempBase, "fault_app.exe");
            File.Copy(GetSystemNotepadPath(), appExe, true);

            IconHelper.ClearPinnedIcons();
            IconHelper.ClearCache();

            // 执行 PinIcon 与 GetIcon，应保证最佳努力执行且不抛出未处理异常
            bool threwException = false;
            BitmapSource? icon = null;
            try
            {
                IconHelper.PinIcon(appExe);
                icon = IconHelper.GetIcon(appExe);
            }
            catch (Exception ex)
            {
                threwException = true;
                Console.WriteLine($"Unexpected exception: {ex}");
            }

            Assert(!threwException, "Test4_NoUnhandledException", "Unwritable cache directory did not throw unhandled exception");
            Assert(icon != null, "Test4_InMemoryExtractionWorks", "In-memory icon extraction and caching continue to function flawlessly");
        }
        finally
        {
            Environment.SetEnvironmentVariable("LOCALAPPDATA", origLocalAppData);
        }
    }

    /// <summary>
    /// 测试 5：全实体持图覆盖（一级扇区、二级级联扇区、中心动作、多层轮盘 profile/layer、取消动作、手势动作）。
    /// 使用真实临时样本，通过 PinIconsForConfig 验证各动作入口，并验证源文件被删后各入口均能从快照恢复。
    /// </summary>
    private static void Test5_FullConfigurationEntityCoverage(string tempBase, string localAppData)
    {
        Console.WriteLine("\n--- Running Test 5: Real Sample Files & Full Configuration Entity Coverage ---");

        string entityDir = Path.Combine(tempBase, "RealEntities");
        Directory.CreateDirectory(entityDir);
        string sysNotepad = GetSystemNotepadPath();

        string tier1Exe = Path.Combine(entityDir, "sample_tier1.exe");
        string launchExe = Path.Combine(entityDir, "sample_launch.exe");
        string subactionExe = Path.Combine(entityDir, "sample_subaction.exe");
        string centerExe = Path.Combine(entityDir, "sample_center.exe");
        string layerActionExe = Path.Combine(entityDir, "sample_layer_action.exe");
        string layerCenterExe = Path.Combine(entityDir, "sample_layer_center.exe");
        string cadAppExe = Path.Combine(entityDir, "sample_cad_app.exe");
        string cancelExe = Path.Combine(entityDir, "sample_cancel.exe");
        string gestureInheritExe = Path.Combine(entityDir, "sample_gesture_inherit.exe");
        string gestureLaunchExe = Path.Combine(entityDir, "sample_gesture_launch.exe");

        string[] allSampleExes = new string[]
        {
            tier1Exe, launchExe, subactionExe, centerExe, layerActionExe,
            layerCenterExe, cadAppExe, cancelExe, gestureInheritExe, gestureLaunchExe
        };

        // 1. 创建真实临时可执行文件样本
        foreach (var sample in allSampleExes)
        {
            File.Copy(sysNotepad, sample, true);
        }
        Assert(allSampleExes.All(File.Exists), "Test5_RealSamplesCreated", $"Created all {allSampleExes.Length} real temporary executable samples");

        // 2. 组装覆盖全部 10 个动作入口的 AppConfig
        AppConfig config = new AppConfig
        {
            Profiles = new List<WheelProfile>
            {
                new WheelProfile
                {
                    ProcessName = "Global",
                    Actions = new List<ActionItem>
                    {
                        new ActionItem { Type = "Hotkey", InheritAppIconPath = tier1Exe },
                        new ActionItem
                        {
                            Type = "Launch",
                            Parameter = launchExe,
                            SubActions = new List<ActionItem>
                            {
                                new ActionItem { Type = "Hotkey", InheritAppIconPath = subactionExe }
                            }
                        }
                    },
                    CenterAction = new ActionItem { Type = "Hotkey", InheritAppIconPath = centerExe },
                    Layers = new List<WheelLayer>
                    {
                        new WheelLayer
                        {
                            Name = "Layer 2",
                            Actions = new List<ActionItem>
                            {
                                new ActionItem { Type = "Hotkey", InheritAppIconPath = layerActionExe }
                            },
                            CenterAction = new ActionItem { Type = "Launch", Parameter = layerCenterExe }
                        }
                    }
                },
                new WheelProfile
                {
                    ProcessName = "CAD.exe",
                    Actions = new List<ActionItem>
                    {
                        new ActionItem { Type = "App", Parameter = cadAppExe }
                    }
                }
            },
            CancelAction = new ActionItem { Type = "Hotkey", InheritAppIconPath = cancelExe },
            GestureMappings = new List<GestureMapping>
            {
                new GestureMapping
                {
                    Pattern = "D-R",
                    Action = new ActionItem { Type = "Hotkey", InheritAppIconPath = gestureInheritExe }
                },
                new GestureMapping
                {
                    Pattern = "U-D",
                    Action = new ActionItem { Type = "Launch", Parameter = gestureLaunchExe }
                }
            }
        };

        IconHelper.ClearPinnedIcons();
        IconHelper.ClearCache();

        // 3. 通过 PinIconsForConfig 驱动真实样本固定与快照持久化
        IconHelper.PinIconsForConfig(config);

        // 4. 验证全部 10 个动作入口在磁盘上均生成了持久化 PNG 快照
        foreach (var sample in allSampleExes)
        {
            string cachePath = IconHelper.GetPersistentIconCachePath(sample);
            Assert(File.Exists(cachePath), "Test5_SnapshotExistsOnDisk", $"Snapshot verified on disk: {Path.GetFileName(sample)}");
        }

        // 5. 模拟这 10 个样本源文件全部从磁盘删除
        foreach (var sample in allSampleExes)
        {
            File.Delete(sample);
        }
        Assert(allSampleExes.All(s => !File.Exists(s)), "Test5_AllSourceSamplesDeleted", $"All {allSampleExes.Length} source executables deleted from disk");

        // 6. 重置内存缓存（模拟程序重启）
        IconHelper.ClearPinnedIcons();
        IconHelper.ClearCache();

        // 7. 逐一验证各动作入口在源文件被删后，依然能无损从持久化快照恢复图标！
        foreach (var sample in allSampleExes)
        {
            Assert(IconHelper.IsUnavailableFileSystemSource(sample), "Test5_UnavailableDetected", $"Unavailable source detected: {Path.GetFileName(sample)}");
            BitmapSource? restored = IconHelper.GetIcon(sample);
            Assert(restored != null && restored.PixelWidth > 0 && restored.PixelHeight > 0, "Test5_SnapshotRestored", $"Icon restored from cache for {Path.GetFileName(sample)}: {restored?.PixelWidth}x{restored?.PixelHeight}");
        }

        // 8. 辅助断言：内部反射收集器准确覆盖了 10 个路径
        var collectMethod = typeof(IconHelper).GetMethod("CollectPinnedIconPath", BindingFlags.NonPublic | BindingFlags.Static);
        HashSet<string> collected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in config.Profiles)
        {
            if (p.Actions != null)
            {
                foreach (var a in p.Actions) collectMethod?.Invoke(null, new object[] { a, collected });
            }
            if (p.CenterAction != null) collectMethod?.Invoke(null, new object[] { p.CenterAction, collected });
            if (p.Layers != null)
            {
                foreach (var l in p.Layers)
                {
                    if (l.Actions != null)
                    {
                        foreach (var a in l.Actions) collectMethod?.Invoke(null, new object[] { a, collected });
                    }
                    if (l.CenterAction != null) collectMethod?.Invoke(null, new object[] { l.CenterAction, collected });
                }
            }
        }
        collectMethod?.Invoke(null, new object[] { config.CancelAction, collected });
        if (config.GestureMappings != null)
        {
            foreach (var gm in config.GestureMappings)
            {
                if (gm.Action != null) collectMethod?.Invoke(null, new object[] { gm.Action, collected });
            }
        }
        Assert(collected.Count == allSampleExes.Length, "Test5_ExactCoverageCount", $"CollectPinnedIconPath covered all {collected.Count}/{allSampleExes.Length} action endpoints");
    }

    /// <summary>
    /// 测试 6：插件图标（plugin:前缀矢量与动态注册）完全不受影响。
    /// 实际向 PluginCatalog 注册插件 SVG，断言解析结果与注册值一致，且撤销后正确清除。
    /// </summary>
    private static void Test6_PluginIconsUnaffected()
    {
        Console.WriteLine("\n--- Running Test 6: Plugin Icons Registration and Resolution ---");

        string pluginId = "test_icon_plugin";
        string shortKey = "custom_tool";
        string fullKey = $"plugin:{pluginId}:{shortKey}";
        string expectedSvg = "M 10 10 L 20 20 L 30 10 Z";

        // 1. 实际向 PluginCatalog 事务式注册插件矢量 SVG 图标
        var session = PluginHost.Catalog.BeginSession(pluginId);
        session.StageIcon(new PluginIconRegistration
        {
            PluginId = pluginId,
            FullKey = fullKey,
            SvgPathData = expectedSvg
        });
        bool committed = session.Commit(out string err);
        Assert(committed && string.IsNullOrEmpty(err), "Test6_PluginIconCommitted", "Plugin icon registered via PluginCatalog session cleanly");

        // 2. 断言 IconHelper.GetSvgPathByKey 解析出的 SVG 路径与实际注册值完全一致
        string? resolvedSvg = IconHelper.GetSvgPathByKey(fullKey);
        Assert(string.Equals(resolvedSvg, expectedSvg, StringComparison.Ordinal), "Test6_ResolvedSvgMatchesRegistered", $"Resolved SVG matches registered: '{resolvedSvg}' == '{expectedSvg}'");

        // 3. 撤销注册后，断言再次解析返回 null
        PluginHost.Catalog.RevokeAll(pluginId);
        string? revokedSvg = IconHelper.GetSvgPathByKey(fullKey);
        Assert(revokedSvg == null, "Test6_RevokedPluginIconReturnsNull", "GetSvgPathByKey returns null after RevokeAll");

        // 4. ActionItem 为 Plugin 类型时，若未设置 InheritAppIconPath，PinIcon 不做无效文件提取
        ActionItem pluginAction = new ActionItem
        {
            Type = "Plugin",
            Name = "插件动作",
            Parameter = "param",
            IconKey = fullKey,
            InheritAppIconPath = ""
        };

        var collectMethod = typeof(IconHelper).GetMethod("CollectPinnedIconPath", BindingFlags.NonPublic | BindingFlags.Static);
        HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        collectMethod?.Invoke(null, new object[] { pluginAction, paths });
        Assert(paths.Count == 0, "Test6_NoFileExtractionForPurePluginAction", "Pure plugin action does not inject invalid file paths into pinned icon list");
    }
}
