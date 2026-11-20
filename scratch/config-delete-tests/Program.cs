using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Input;
using WinPieGestures;

internal static class Program
{
    private static int _checks;

    private static int Main()
    {
        string isolatedRoot = Path.Combine(Path.GetTempPath(), "StarPieConfigDeleteTests-" + Guid.NewGuid().ToString("N"));
        string localAppData = Path.Combine(isolatedRoot, "LocalAppData");
        string appData = Path.Combine(isolatedRoot, "AppData");
        Directory.CreateDirectory(localAppData);
        Directory.CreateDirectory(appData);
        Environment.SetEnvironmentVariable("LOCALAPPDATA", localAppData);
        Environment.SetEnvironmentVariable("APPDATA", appData);

        try
        {
            RunSuite(isolatedRoot, localAppData);
            Console.WriteLine($"PASS {_checks} checks; isolated LOCALAPPDATA, zero user config writes.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL after {_checks}: {ex}");
            return 1;
        }
        finally
        {
            try
            {
                if (Directory.Exists(isolatedRoot))
                {
                    Directory.Delete(isolatedRoot, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunSuite(string isolatedRoot, string localAppData)
    {
        string candidate = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "StarPieCandidateDir").Value!;
        string expected = Path.Combine(candidate, "StarPie.dll");
        string loaded = typeof(ConfigManager).Assembly.Location;
        Check(Path.GetFullPath(loaded).Equals(Path.Combine(AppContext.BaseDirectory, "StarPie.dll"), StringComparison.OrdinalIgnoreCase),
            "fresh probe host location");
        Check(Hash(expected) == Hash(loaded), "candidate SHA256 binding");
        Console.WriteLine($"CANDIDATE={expected}; SHA256={Hash(expected)}");

        ConfigManager.DisableAutoStartSync = true;
        string expectedConfigsDir = Path.GetFullPath(Path.Combine(localAppData, "StarPie", "Configs"));
        Check(Path.GetFullPath(ConfigManager.ConfigsFolder).Equals(expectedConfigsDir, StringComparison.OrdinalIgnoreCase),
            "ConfigManager bound to isolated temp LOCALAPPDATA");

        TestConfigDeletionAndResolution(localAppData);
        TestMissingLockedAndTraversalRejection(isolatedRoot, localAppData);
        TestDeletedLayerTriggerResidualAcrossSaveAndReload();
    }

    private static void TestConfigDeletionAndResolution(string localAppData)
    {
        ConfigManager.LoadConfig();
        string configsDir = ConfigManager.ConfigsFolder;
        Check(ConfigManager.GetSavedConfigNames().Contains("默认配置", StringComparer.OrdinalIgnoreCase),
            "initial default config listed");

        // 1. Normal inactive profile deletion
        Check(ConfigManager.SaveConfigAs("NormalProfile"), "save NormalProfile");
        Check(ConfigManager.SwitchToConfig("默认配置"), "switch back to default");
        Check(File.Exists(Path.Combine(configsDir, "NormalProfile.json")), "NormalProfile.json exists");
        Check(ConfigManager.DeleteSavedConfig("NormalProfile", out string fallback1), "delete normal inactive profile");
        Check(fallback1 == "默认配置", "inactive delete reports active fallback");
        Check(!File.Exists(Path.Combine(configsDir, "NormalProfile.json")), "NormalProfile.json deleted from disk");

        // 2. Normal active profile deletion must switch cleanly and never recreate the deleted file
        Check(ConfigManager.SaveConfigAs("ActiveNormal"), "save ActiveNormal");
        Check(ConfigManager.CurrentConfig.ActiveConfigProfileName == "ActiveNormal", "ActiveNormal is active");
        Check(ConfigManager.DeleteSavedConfig("ActiveNormal", out string fallback2), "delete active normal profile");
        Check(fallback2 == "默认配置", "active delete switches to default fallback");
        Check(ConfigManager.CurrentConfig.ActiveConfigProfileName == "默认配置", "ActiveConfigProfileName updated");
        Check(!File.Exists(Path.Combine(configsDir, "ActiveNormal.json")), "ActiveNormal.json deleted from disk");
        var namesAfterNormal = ConfigManager.GetSavedConfigNames();
        Check(!namesAfterNormal.Contains("ActiveNormal", StringComparer.OrdinalIgnoreCase), "GetSavedConfigNames does not recreate deleted active profile");
        Check(!File.Exists(Path.Combine(configsDir, "ActiveNormal.json")), "EnsureConfigsFolder does not recreate deleted active profile");

        // 3. Leading & trailing space filename: list, export, switch, save, and active delete
        string spaceStem = "  leading_trailing  ";
        string spacePath = Path.Combine(configsDir, $"{spaceStem}.json");
        var spaceConfig = ConfigManager.CreateDefaultConfig();
        spaceConfig.DragThreshold = 42.0;
        spaceConfig.ActiveConfigProfileName = spaceStem;
        File.WriteAllText(spacePath, JsonSerializer.Serialize(spaceConfig));

        var listed = ConfigManager.GetSavedConfigNames();
        Check(listed.Contains(spaceStem, StringComparer.Ordinal), "GetSavedConfigNames exposes untrimmed stem");

        string exportedJson = ConfigManager.GetConfigJsonForExport(spaceStem);
        var exportedParsed = JsonSerializer.Deserialize<AppConfig>(exportedJson);
        Check(exportedParsed != null && Math.Abs(exportedParsed.DragThreshold - 42.0) < 0.001,
            "GetConfigJsonForExport resolves untrimmed stem");

        string exportTarget = Path.Combine(localAppData, "exported_space.json");
        Check(ConfigManager.ExportConfigToFile(spaceStem, exportTarget), "ExportConfigToFile succeeds for untrimmed stem");
        var exportedFileParsed = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(exportTarget));
        Check(exportedFileParsed != null && Math.Abs(exportedFileParsed.DragThreshold - 42.0) < 0.001,
            "ExportConfigToFile copies untrimmed stem file");

        Check(ConfigManager.SwitchToConfig(spaceStem), "SwitchToConfig resolves untrimmed stem");
        Check(ConfigManager.CurrentConfig.ActiveConfigProfileName == spaceStem, "SwitchToConfig keeps raw stem active");
        Check(Math.Abs(ConfigManager.CurrentConfig.DragThreshold - 42.0) < 0.001, "SwitchToConfig loads untrimmed stem config");
        Check(ConfigManager.SaveConfig(), "SaveConfig mirrors back to untrimmed stem");
        Check(!File.Exists(Path.Combine(configsDir, "leading_trailing.json")), "SaveConfig does not create duplicate trimmed file");

        Check(ConfigManager.DeleteSavedConfig(spaceStem, out string fallback3), "delete active untrimmed stem config");
        Check(fallback3 == "默认配置", "fallback excludes both deleted raw and clean names");
        Check(!File.Exists(spacePath), "untrimmed space json deleted from disk");
        Check(!File.Exists(Path.Combine(configsDir, "leading_trailing.json")), "clean-name duplicate does not exist after delete");
        Check(ConfigManager.CurrentConfig.ActiveConfigProfileName == "默认配置", "active profile switched cleanly after untrimmed delete");
        Check(!ConfigManager.GetSavedConfigNames().Contains(spaceStem, StringComparer.Ordinal), "deleted space profile stays gone on list");

        // 4. Trimmed / case-insensitive resolution & exact-stem priority when both raw and clean exist
        string spaceOnlyPath = Path.Combine(configsDir, "  space_only .json");
        File.WriteAllText(spaceOnlyPath, JsonSerializer.Serialize(ConfigManager.CreateDefaultConfig()));
        Check(ConfigManager.DeleteSavedConfig("SPACE_ONLY", out _), "trimmed/case-insensitive delete removes spaced file");
        Check(!File.Exists(spaceOnlyPath), "space_only file removed from disk");

        string dupSpacePath = Path.Combine(configsDir, "  dup  .json");
        string dupCleanPath = Path.Combine(configsDir, "dup.json");
        File.WriteAllText(dupSpacePath, JsonSerializer.Serialize(ConfigManager.CreateDefaultConfig()));
        File.WriteAllText(dupCleanPath, JsonSerializer.Serialize(ConfigManager.CreateDefaultConfig()));
        Check(ConfigManager.DeleteSavedConfig("  dup  ", out _), "exact stem match deletes spaced file when clean file also exists");
        Check(!File.Exists(dupSpacePath) && File.Exists(dupCleanPath), "clean sibling file preserved when exact spaced stem deleted");
        Check(ConfigManager.DeleteSavedConfig("dup", out _), "clean file deleted cleanly");
        Check(!File.Exists(dupCleanPath), "dup.json removed from disk");

        // 5. Special-char filename & clean-match deletion & rename
        string specialStem = "Pro#1 (v2) & [CAD];+~";
        string specialPath = Path.Combine(configsDir, $"{specialStem}.json");
        File.WriteAllText(specialPath, JsonSerializer.Serialize(ConfigManager.CreateDefaultConfig()));
        Check(ConfigManager.GetSavedConfigNames().Contains(specialStem, StringComparer.Ordinal), "special-char stem listed");
        Check(ConfigManager.RenameSavedConfig(specialStem, "Renamed#2"), "rename special-char config");
        Check(!File.Exists(specialPath) && File.Exists(Path.Combine(configsDir, "Renamed#2.json")), "renamed special-char file moved");
        Check(ConfigManager.DeleteSavedConfig("Renamed#2", out _), "delete renamed special-char config");
        Check(!File.Exists(Path.Combine(configsDir, "Renamed#2.json")), "renamed special-char file removed from disk");

        string uncleanedDiskPath = Path.Combine(configsDir, "Special!@#.json");
        File.WriteAllText(uncleanedDiskPath, JsonSerializer.Serialize(ConfigManager.CreateDefaultConfig()));
        Check(ConfigManager.DeleteSavedConfig("Special!@#*", out _), "clean-match deletion removes Special!@#.json");
        Check(!File.Exists(uncleanedDiskPath), "Special!@#.json removed from disk");
    }

    private static void TestMissingLockedAndTraversalRejection(string isolatedRoot, string localAppData)
    {
        string configsDir = ConfigManager.ConfigsFolder;
        Check(ConfigManager.SwitchToConfig("默认配置"), "reset to default config");
        Check(ConfigManager.SaveConfigAs("KeepMe"), "create KeepMe");
        Check(ConfigManager.SwitchToConfig("默认配置"), "switch back to default");

        // 1. Missing file does not switch or return true
        string activeBefore = ConfigManager.CurrentConfig.ActiveConfigProfileName;
        Check(!ConfigManager.DeleteSavedConfig("NonExistentProfile", out string missingFallback), "missing profile delete returns false");
        Check(missingFallback == "", "missing profile delete leaves fallback empty");
        Check(ConfigManager.CurrentConfig.ActiveConfigProfileName == activeBefore, "missing delete does not switch away");

        // 2. Path traversal attempts must be blocked and never touch outside files
        string outsideInAppData = Path.Combine(localAppData, "StarPie", "outside.json");
        string outsideInRoot = Path.Combine(isolatedRoot, "outside.json");
        File.WriteAllText(outsideInAppData, "{\"Keep\":true}");
        File.WriteAllText(outsideInRoot, "{\"Keep\":true}");
        foreach (string traversal in new[]
        {
            "../outside",
            "..\\outside",
            "..\\..\\outside",
            "..",
            ".",
            "sub\\outside",
            "sub/outside",
            "C:\\outside",
            "outside\0name",
            "   "
        })
        {
            Check(!ConfigManager.DeleteSavedConfig(traversal, out string travFallback), $"traversal blocked for '{traversal}'");
            Check(travFallback == "", $"traversal fallback empty for '{traversal}'");
            Check(!ConfigManager.SwitchToConfig(traversal), $"SwitchToConfig blocks traversal '{traversal}'");
            Check(!ConfigManager.RenameSavedConfig("KeepMe", traversal), $"RenameSavedConfig blocks traversal target '{traversal}'");
            Check(!ConfigManager.ExportConfigToFile(traversal, Path.Combine(isolatedRoot, "should_not_exist.json")),
                $"ExportConfigToFile blocks traversal '{traversal}'");
        }
        Check(File.Exists(outsideInAppData) && File.Exists(outsideInRoot), "outside files untouched by traversal attempts");

        // 3. Locked inactive file must fail without deleting or falsely returning true
        string lockedInactivePath = Path.Combine(configsDir, "LockedInactive.json");
        File.WriteAllText(lockedInactivePath, JsonSerializer.Serialize(ConfigManager.CreateDefaultConfig()));
        using (var lockStream = new FileStream(lockedInactivePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Check(!ConfigManager.DeleteSavedConfig("LockedInactive", out string lockedFallback1), "locked inactive delete returns false");
            Check(lockedFallback1 == "", "locked inactive delete leaves fallback empty");
            Check(ConfigManager.CurrentConfig.ActiveConfigProfileName == "默认配置", "locked inactive delete does not switch");
            Check(File.Exists(lockedInactivePath), "locked inactive file still exists");
        }
        Check(ConfigManager.DeleteSavedConfig("LockedInactive", out _), "unlocked inactive file deletes cleanly");

        // 4. Locked active file must fail without switching away!
        Check(ConfigManager.SaveConfigAs("LockedActive"), "save LockedActive");
        string lockedActivePath = Path.Combine(configsDir, "LockedActive.json");
        using (var lockStream = new FileStream(lockedActivePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Check(!ConfigManager.DeleteSavedConfig("LockedActive", out string lockedFallback2), "locked active delete returns false");
            Check(lockedFallback2 == "", "locked active delete leaves fallback empty");
            Check(ConfigManager.CurrentConfig.ActiveConfigProfileName == "LockedActive", "locked active delete does not switch away");
            Check(File.Exists(lockedActivePath), "locked active file still exists");
        }
        Check(ConfigManager.DeleteSavedConfig("LockedActive", out string afterUnlockFallback), "unlocked active deletes cleanly");
        Check(afterUnlockFallback != "LockedActive", "unlocked active switches to remaining config");
        Check(!File.Exists(lockedActivePath), "LockedActive.json deleted after unlock");

        // 5. Last remaining config cannot be deleted
        foreach (string name in ConfigManager.GetSavedConfigNames().Where(n => !string.Equals(n, "默认配置", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            ConfigManager.DeleteSavedConfig(name, out _);
        }
        Check(ConfigManager.GetSavedConfigNames().Count == 1, "only one config remains");
        Check(!ConfigManager.DeleteSavedConfig("默认配置", out _), "cannot delete last remaining config");
        Check(File.Exists(Path.Combine(configsDir, "默认配置.json")), "last remaining config still exists on disk");
    }

    private static void TestDeletedLayerTriggerResidualAcrossSaveAndReload()
    {
        ConfigManager.ResetToDefault();
        var config = ConfigManager.CurrentConfig;
        config.Trigger = Mouse("RightButton");

        var global = config.Profiles.First(p => string.Equals(p.ProcessName, "Global", StringComparison.OrdinalIgnoreCase));
        global.EnsureLayers();
        global.Layers.Clear();
        var g0 = Layer("Global0", Mouse("XButton1"));
        var g1 = Layer("Global1", Key(114, ModifierKeys.Alt));
        var g2 = Layer("Global2");
        global.Layers.AddRange(new[] { g0, g1, g2 });
        global.EnsureLayers();

        var cad = new WheelProfile
        {
            ProcessName = "CAD",
            DisplayName = "CAD",
            BoundProcesses = "cad.exe",
            HasExplicitProcessBindings = true,
            UseApplicationContext = true,
            ActivationTrigger = Mouse("XButton1") // legacy root trigger for layer 0
        };
        var l0 = Layer("Sketch", Mouse("MiddleButton")); // explicit layer 0 trigger superseding legacy XButton1
        var l1 = Layer("Part", Key(112, ModifierKeys.Control));
        var l2 = Layer("Assembly", Mouse("XButton2"));
        cad.Layers = new List<WheelLayer> { l0, l1, l2 };
        cad.EnsureLayers();

        var legacyOnly = new WheelProfile
        {
            ProcessName = "LegacyApp",
            DisplayName = "LegacyApp",
            BoundProcesses = "legacyapp.exe",
            HasExplicitProcessBindings = true,
            UseApplicationContext = true,
            ActivationTrigger = Mouse("XButton1") // legacy root trigger only (layer0 ActivationTrigger == null)
        };
        var leg0 = Layer("Leg0");
        var leg1 = Layer("Leg1", Key(113, ModifierKeys.Shift));
        legacyOnly.Layers = new List<WheelLayer> { leg0, leg1 };
        legacyOnly.EnsureLayers();

        config.Profiles.Add(cad);
        config.Profiles.Add(legacyOnly);
        Check(ConfigManager.SaveConfig(), "save multi-layer trigger config");

        // Pre-delete assertions
        Check(ResolveMouse("cad.exe", "MiddleButton")?.Layer == l0, "pre-delete cad layer0 routes on MiddleButton");
        Check(ResolveKey("cad.exe", 112, ModifierKeys.Control)?.Layer == l1, "pre-delete cad layer1 routes on Ctrl+F1");
        Check(ResolveMouse("cad.exe", "XButton2")?.Layer == l2, "pre-delete cad layer2 routes on XButton2");
        Check(ResolveMouse("legacyapp.exe", "XButton1")?.Layer == leg0, "pre-delete legacyOnly layer0 routes on root XButton1");
        Check(ResolveMouse("other.exe", "XButton1")?.Layer == g0, "pre-delete global layer0 routes on XButton1");
        Check(ResolveKey("other.exe", 114, ModifierKeys.Alt)?.Layer == g1, "pre-delete global layer1 routes on Alt+F3");

        // 1. Delete layer N (l1 in cad, g1 in global)
        Check(cad.RemoveLayer(l1), "remove cad layer N (l1)");
        Check(l1.ActivationTrigger == null, "removed layer N clears its ActivationTrigger");
        Check(global.RemoveLayer(g1), "remove global layer N (g1)");
        Check(g1.ActivationTrigger == null, "removed global layer N clears its ActivationTrigger");
        Check(ConfigManager.SaveConfig(), "save after layer N removal");

        Check(ProfileTriggerRouter.TryResolveKeyboard("cad.exe", 112, ModifierKeys.Control, out _) == ProfileTriggerResolution.None,
            "deleted cad layer N trigger immediately returns None after SaveConfig");
        Check(ProfileTriggerRouter.TryResolveKeyboard("other.exe", 114, ModifierKeys.Alt, out _) == ProfileTriggerResolution.None,
            "deleted global layer N trigger immediately returns None after SaveConfig");

        ConfigManager.LoadConfig();
        Check(ProfileTriggerRouter.TryResolveKeyboard("cad.exe", 112, ModifierKeys.Control, out _) == ProfileTriggerResolution.None,
            "deleted cad layer N trigger stays None after LoadConfig reload");
        Check(ProfileTriggerRouter.TryResolveKeyboard("other.exe", 114, ModifierKeys.Alt, out _) == ProfileTriggerResolution.None,
            "deleted global layer N trigger stays None after LoadConfig reload");

        // Re-acquire live profile references after LoadConfig
        global = ConfigManager.CurrentConfig.Profiles.First(p => string.Equals(p.ProcessName, "Global", StringComparison.OrdinalIgnoreCase));
        cad = ConfigManager.CurrentConfig.Profiles.First(p => string.Equals(p.ProcessName, "CAD", StringComparison.OrdinalIgnoreCase));
        legacyOnly = ConfigManager.CurrentConfig.Profiles.First(p => string.Equals(p.ProcessName, "LegacyApp", StringComparison.OrdinalIgnoreCase));

        Check(ResolveMouse("cad.exe", "MiddleButton")?.Layer == cad.Layers[0], "cad layer0 still routes after layer N delete & reload");
        Check(ResolveMouse("cad.exe", "XButton2")?.Layer == cad.Layers[1], "cad layer2 still routes after layer N delete & reload");

        // 2. Delete layer 0 (cad: both explicit layer0 trigger AND dormant legacy root trigger; legacyOnly: root trigger; global: layer0)
        var cadLayer0 = cad.Layers[0];
        var legacyLayer0 = legacyOnly.Layers[0];
        var globalLayer0 = global.Layers[0];

        Check(cad.RemoveLayer(cadLayer0), "remove cad layer 0");
        Check(cadLayer0.ActivationTrigger == null, "removed cad layer 0 clears WheelLayer.ActivationTrigger");
        Check(cad.ActivationTrigger == null, "removing cad layer 0 clears legacy WheelProfile.ActivationTrigger");

        Check(legacyOnly.RemoveLayer(legacyLayer0), "remove legacyOnly layer 0");
        Check(legacyOnly.ActivationTrigger == null, "removing legacyOnly layer 0 clears legacy WheelProfile.ActivationTrigger");

        Check(global.RemoveLayer(globalLayer0), "remove global layer 0");
        Check(globalLayer0.ActivationTrigger == null, "removed global layer 0 clears WheelLayer.ActivationTrigger");
        Check(global.ActivationTrigger == null, "removing global layer 0 clears WheelProfile.ActivationTrigger");

        Check(ConfigManager.SaveConfig(), "save after layer 0 removals");

        // Immediate routing check after SaveConfig
        Check(ProfileTriggerRouter.TryResolveMouse("other.exe", "XButton1", ModifierKeys.None, out _) == ProfileTriggerResolution.None,
            "deleted global layer 0 trigger immediately returns None after SaveConfig");
        Check(ProfileTriggerRouter.TryResolveMouse("cad.exe", "MiddleButton", ModifierKeys.None, out _) == ProfileTriggerResolution.None,
            "deleted cad layer 0 explicit trigger immediately returns None after SaveConfig");
        Check(ProfileTriggerRouter.TryResolveMouse("cad.exe", "XButton1", ModifierKeys.None, out _) == ProfileTriggerResolution.None,
            "deleted cad layer 0 legacy root trigger does not leak after SaveConfig");
        Check(ProfileTriggerRouter.TryResolveMouse("legacyapp.exe", "XButton1", ModifierKeys.None, out _) == ProfileTriggerResolution.None,
            "deleted legacyOnly layer 0 root trigger immediately returns None after SaveConfig");
        Check(ResolveMouse("cad.exe", "XButton2")?.Layer == cad.Layers[0],
            "surviving cad layer still routes on XButton2 immediately");
        Check(ResolveKey("legacyapp.exe", 113, ModifierKeys.Shift)?.Layer == legacyOnly.Layers[0],
            "surviving legacyOnly layer still routes on Shift+F2 immediately");

        // Reload from disk and verify zero residuals
        ConfigManager.LoadConfig();
        var reloadedCad = ConfigManager.CurrentConfig.Profiles.First(p => string.Equals(p.ProcessName, "CAD", StringComparison.OrdinalIgnoreCase));
        var reloadedLegacy = ConfigManager.CurrentConfig.Profiles.First(p => string.Equals(p.ProcessName, "LegacyApp", StringComparison.OrdinalIgnoreCase));
        Check(reloadedCad.ActivationTrigger == null && reloadedCad.Layers.Count == 1, "cad layer0 & legacy trigger stay cleared after reload");
        Check(reloadedLegacy.ActivationTrigger == null && reloadedLegacy.Layers.Count == 1, "legacyOnly layer0 & root trigger stay cleared after reload");
        Check(ProfileTriggerRouter.TryResolveMouse("other.exe", "XButton1", ModifierKeys.None, out _) == ProfileTriggerResolution.None,
            "deleted global layer 0 trigger stays None after LoadConfig reload");
        Check(ProfileTriggerRouter.TryResolveMouse("cad.exe", "MiddleButton", ModifierKeys.None, out _) == ProfileTriggerResolution.None,
            "deleted cad layer 0 trigger stays None after LoadConfig reload");
        Check(ProfileTriggerRouter.TryResolveMouse("cad.exe", "XButton1", ModifierKeys.None, out _) == ProfileTriggerResolution.None,
            "deleted cad legacy root trigger stays None after LoadConfig reload");
        Check(ProfileTriggerRouter.TryResolveMouse("legacyapp.exe", "XButton1", ModifierKeys.None, out _) == ProfileTriggerResolution.None,
            "deleted legacyOnly root trigger stays None after LoadConfig reload");
        Check(ResolveMouse("cad.exe", "XButton2")?.Layer == reloadedCad.Layers[0],
            "surviving cad layer still routes on XButton2 after reload");
        Check(ResolveKey("legacyapp.exe", 113, ModifierKeys.Shift)?.Layer == reloadedLegacy.Layers[0],
            "surviving legacyOnly layer still routes on Shift+F2 after reload");

        // 3. ValidateLayerRemoval rejects removing the sole triggered layer of a parallel profile
        var parallel = new WheelProfile
        {
            ProcessName = "Parallel",
            DisplayName = "Parallel",
            UseApplicationContext = false,
            ActivationTrigger = Mouse("XButton1")
        };
        parallel.Layers = new List<WheelLayer> { Layer("P0", Mouse("MiddleButton")), Layer("P1") };
        parallel.EnsureLayers();
        var parallelConfig = new AppConfig { Profiles = new List<WheelProfile> { global, parallel }, Trigger = Mouse("RightButton") };
        Check(ProfileTriggerRouter.ValidateLayerRemoval(parallelConfig, parallel, parallel.Layers[0]) == ProfileTriggerValidationError.MissingTrigger,
            "ValidateLayerRemoval rejects deleting sole triggered layer of parallel profile even when legacy root trigger exists");
        parallel.Layers[1].ActivationTrigger = Mouse("XButton2");
        Check(ProfileTriggerRouter.ValidateLayerRemoval(parallelConfig, parallel, parallel.Layers[0]) == ProfileTriggerValidationError.None,
            "ValidateLayerRemoval allows deleting layer 0 when remaining layer has valid trigger");
    }

    private static WheelLayer Layer(string name, TriggerConfig? trigger = null) => new()
    {
        Name = name,
        ActivationTrigger = trigger,
        Actions = new List<ActionItem> { new ActionItem { Type = "Hotkey", Name = name, Parameter = "Ctrl+C" } }
    };

    private static TriggerConfig Mouse(string button, ModifierKeys mods = ModifierKeys.None) => new()
    {
        TriggerType = "Mouse",
        MouseButton = button,
        RequireCtrl = mods.HasFlag(ModifierKeys.Control),
        RequireShift = mods.HasFlag(ModifierKeys.Shift),
        RequireAlt = mods.HasFlag(ModifierKeys.Alt),
        RequireWin = mods.HasFlag(ModifierKeys.Windows)
    };

    private static TriggerConfig Key(uint vk, ModifierKeys mods = ModifierKeys.None) => new()
    {
        TriggerType = "Keyboard",
        VkCode = vk,
        Key = KeyInterop.KeyFromVirtualKey((int)vk).ToString(),
        RequireCtrl = mods.HasFlag(ModifierKeys.Control),
        RequireShift = mods.HasFlag(ModifierKeys.Shift),
        RequireAlt = mods.HasFlag(ModifierKeys.Alt),
        RequireWin = mods.HasFlag(ModifierKeys.Windows)
    };

    private static ProfileTriggerActivation? ResolveMouse(string app, string button, ModifierKeys mods = ModifierKeys.None) =>
        ProfileTriggerRouter.TryResolveMouse(app, button, mods, out var hit) == ProfileTriggerResolution.Match ? hit : null;

    private static ProfileTriggerActivation? ResolveKey(string app, uint vk, ModifierKeys mods) =>
        ProfileTriggerRouter.TryResolveKeyboard(app, vk, mods, out var hit) == ProfileTriggerResolution.Match ? hit : null;

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static void Check(bool value, string message) { _checks++; if (!value) throw new InvalidOperationException(message); }
}
