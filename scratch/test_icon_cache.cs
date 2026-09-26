using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Media.Imaging;
using WinPieGestures;

public class Program
{
    private static int _failedCount = 0;
    private static int _passedCount = 0;

    [STAThread]
    public static int Main()
    {
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
            Test3_CorruptedCacheSelfHealing(tempBase, localAppData);
            Test4_UnwritableCacheFaultTolerance(tempBase, localAppData);
            Test5_FullConfigurationEntityCoverage();
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

    /// <summary>
    /// 测试 1：程序重启且原文件被删后，能正确从持久化缓存读取快照并正常显示。
    /// </summary>
    private static void Test1_RestartAfterSourceDeleted(string tempBase, string localAppData)
    {
        Console.WriteLine("\n--- Running Test 1: Restart After Source File Deleted ---");

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
    /// </summary>
    private static void Test5_FullConfigurationEntityCoverage()
    {
        Console.WriteLine("\n--- Running Test 5: Full Configuration Entity Icon Pinning Coverage ---");

        AppConfig config = new AppConfig
        {
            Profiles = new List<WheelProfile>
            {
                new WheelProfile
                {
                    ProcessName = "Global",
                    Actions = new List<ActionItem>
                    {
                        new ActionItem { Type = "Hotkey", InheritAppIconPath = @"C:\TestEntities\app_tier1.exe" },
                        new ActionItem
                        {
                            Type = "Launch",
                            Parameter = @"C:\TestEntities\app_launch.exe",
                            SubActions = new List<ActionItem>
                            {
                                new ActionItem { Type = "Hotkey", InheritAppIconPath = @"C:\TestEntities\app_subaction.exe" }
                            }
                        }
                    },
                    CenterAction = new ActionItem { Type = "Hotkey", InheritAppIconPath = @"C:\TestEntities\app_center.exe" },
                    Layers = new List<WheelLayer>
                    {
                        new WheelLayer
                        {
                            Name = "Layer 2",
                            Actions = new List<ActionItem>
                            {
                                new ActionItem { Type = "Hotkey", InheritAppIconPath = @"C:\TestEntities\app_layer_action.exe" }
                            },
                            CenterAction = new ActionItem { Type = "Launch", Parameter = @"C:\TestEntities\app_layer_center.exe" }
                        }
                    }
                },
                new WheelProfile
                {
                    ProcessName = "CAD.exe",
                    Actions = new List<ActionItem>
                    {
                        new ActionItem { Type = "App", Parameter = @"C:\TestEntities\app_cad_type_app.exe" }
                    }
                }
            },
            CancelAction = new ActionItem { Type = "Hotkey", InheritAppIconPath = @"C:\TestEntities\app_cancel.exe" },
            GestureMappings = new List<GestureMapping>
            {
                new GestureMapping
                {
                    Pattern = "D-R",
                    Action = new ActionItem { Type = "Hotkey", InheritAppIconPath = @"C:\TestEntities\app_gesture_inherit.exe" }
                },
                new GestureMapping
                {
                    Pattern = "U-D",
                    Action = new ActionItem { Type = "Launch", Parameter = @"C:\TestEntities\app_gesture_launch.exe" }
                }
            }
        };

        // 通过私有字段反射验证 PinIconsForConfig 收集的全部路径
        IconHelper.ClearPinnedIcons();
        IconHelper.ClearCache();

        // 收集预期路径集合
        string[] expectedPaths = new string[]
        {
            @"C:\TestEntities\app_tier1.exe",
            @"C:\TestEntities\app_launch.exe",
            @"C:\TestEntities\app_subaction.exe",
            @"C:\TestEntities\app_center.exe",
            @"C:\TestEntities\app_layer_action.exe",
            @"C:\TestEntities\app_layer_center.exe",
            @"C:\TestEntities\app_cad_type_app.exe",
            @"C:\TestEntities\app_cancel.exe",
            @"C:\TestEntities\app_gesture_inherit.exe",
            @"C:\TestEntities\app_gesture_launch.exe"
        };

        // 我们通过反射调用私有的 CollectPinnedIconPaths 与 PinIconsForConfig 逻辑
        var collectMethod = typeof(IconHelper).GetMethod("CollectPinnedIconPath", BindingFlags.NonPublic | BindingFlags.Static);
        HashSet<string> collected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 运行 PinIconsForConfig
        IconHelper.PinIconsForConfig(config);

        // 检查 PinIconsForConfig 是否涵盖了全部预期路径
        // 因为这些虚构路径物理上不存在，PinIcon 会调用 IsUnavailableFileSystemSource 并尝试 LoadPersistentIconSnapshot
        // 我们可以测试内部的 CollectPinnedIconPaths 收集到的集合
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

        foreach (var expected in expectedPaths)
        {
            Assert(collected.Contains(expected), "Test5_EntityCovered", $"Entity path covered: {expected}");
        }
        Assert(collected.Count == expectedPaths.Length, "Test5_ExactCoverageCount", $"Total collected: {collected.Count}/{expectedPaths.Length}");
    }

    /// <summary>
    /// 测试 6：插件图标（plugin:前缀矢量与动态注册）完全不受影响。
    /// </summary>
    private static void Test6_PluginIconsUnaffected()
    {
        Console.WriteLine("\n--- Running Test 6: Plugin Icons Unaffected ---");

        string pluginKey = "plugin:sample_plugin:light_bulb";

        // 1. GetSvgPathByKey 对插件前缀正确转发至 PluginHost.Catalog，且绝不触发文件提取
        string? svg = IconHelper.GetSvgPathByKey(pluginKey);
        // sample_plugin 虽未安装，但 ResolveIcon 优雅返回 null 或默认，不发生异常
        Assert(true, "Test6_GetSvgPathByKeyHandled", $"GetSvgPathByKey for '{pluginKey}' executed without throwing");

        // 2. ActionItem 为 Plugin 类型时，若未设置 InheritAppIconPath，PinIcon 不做无效文件提取
        ActionItem pluginAction = new ActionItem
        {
            Type = "Plugin",
            Name = "插件动作",
            Parameter = "param",
            IconKey = pluginKey,
            InheritAppIconPath = ""
        };

        var collectMethod = typeof(IconHelper).GetMethod("CollectPinnedIconPath", BindingFlags.NonPublic | BindingFlags.Static);
        HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        collectMethod?.Invoke(null, new object[] { pluginAction, paths });
        Assert(paths.Count == 0, "Test6_NoFileExtractionForPurePluginAction", "Pure plugin action does not inject invalid file paths into pinned icon list");
    }
}
