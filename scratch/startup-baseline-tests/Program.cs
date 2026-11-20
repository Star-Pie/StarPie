using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using WinPieGestures;

internal static class Program
{
    private static readonly Dictionary<short, OpCode> Codes = typeof(OpCodes).GetFields(BindingFlags.Static | BindingFlags.Public)
        .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)!).ToDictionary(o => o.Value);
    private static int _checks;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            string directory = Path.GetFullPath(args[0]);
            string scenario = args[1];
            bool single = args.Contains("--expect-single");
            string candidate = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(a => a.Key == "StarPieCandidateDir").Value!;
            string expected = Path.Combine(candidate, "StarPie.dll");
            string loaded = typeof(App).Assembly.Location;
            Check(Hash(expected) == Hash(loaded), "candidate hash binding");
            Check(Path.GetFullPath(loaded) == Path.Combine(AppContext.BaseDirectory, "StarPie.dll"), "isolated host copy");
            Check(args.Contains("--test-instance"), "test-instance gate required before ConfigManager initialization");
            Directory.CreateDirectory(directory);
            foreach (string variable in new[] { "LOCALAPPDATA", "APPDATA", "TEMP", "TMP" })
                Environment.SetEnvironmentVariable(variable, directory);
            string configPath = Path.Combine(directory, "StarPie", "config.json");
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            if (scenario == "launch-editors")
            {
                TestLaunchEditors();
                Console.WriteLine($"PASS {_checks} launch editor checks (offscreen).");
                return 0;
            }
            if (scenario == "ui")
            {
                TestSettingsControls(directory);
                Console.WriteLine($"PASS {_checks} candidate-bound settings checks; offscreen only.");
                return 0;
            }
            if (scenario != "missing")
                File.WriteAllText(configPath, scenario == "corrupt" ? "{invalid" : JsonSerializer.Serialize(Fixture()));

            var entry = typeof(App).GetMethod("OnStartup", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var calls = Calls(entry).ToArray();
            var policy = typeof(App).GetMethod("ShouldOpenStartupSettings", BindingFlags.Static | BindingFlags.NonPublic)!;
            Check(calls.Contains(policy), "cold startup uses the tested settings policy");
            Check(!JsonSerializer.Deserialize<AppConfig>("{}")!.StartInTray, "legacy profiles open Settings");
            foreach (bool silent in new[] { false, true })
            foreach (bool tray in new[] { false, true })
            foreach (bool pending in new[] { false, true })
            {
                bool open = (bool)policy.Invoke(null, new object[] { silent, tray, pending })!;
                bool expectedOpen = pending || (!silent && !tray);
                Check(open == expectedOpen, $"startup window: silent={silent}, tray={tray}, pending={pending}");
            }

            // Replay only configuration operations from the real entrypoint IL. Never invoke
            // OnStartup: it installs hooks, creates a tray and may show settings.
            var operations = calls.Where(m => m.DeclaringType == typeof(ConfigManager) ||
                (m.DeclaringType == typeof(App) && m.Name == "InitializeConfiguration")).ToArray();
            Check(operations.Length != 0, "real startup has a configuration operation");
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            foreach (var operation in operations)
            {
                if (operation.DeclaringType == typeof(ConfigManager))
                {
                    RuntimeHelpers.RunClassConstructor(typeof(ConfigManager).TypeHandle);
                    if (operation.Name == "LoadConfig") operation.Invoke(null, null);
                    else Check(operation.Name == "IsElevated" || operation.Name == "get_CurrentConfig", "only safe startup config calls replayed");
                }
                else
                {
                    Check(operation.IsStatic && operation.GetParameters().Length == 0, "initialization seam is static and parameterless");
                    var dependencies = Calls((MethodInfo)operation).ToArray();
                    Check(dependencies.Length == 1 && dependencies[0].DeclaringType == typeof(ConfigManager)
                        && dependencies[0].Name == "get_CurrentConfig", "initialization seam only ensures existing type initialization");
                    operation.Invoke(null, null);
                }
            }
            watch.Stop();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            long revision = ConfigManager.ConfigurationRevision;
            bool fallback = ConfigManager.IsFallbackConfig;
            var metric = new { scenario, elapsedMs = watch.Elapsed.TotalMilliseconds, allocatedBytes = allocated,
                revision, fallback, profiles = ConfigManager.CurrentConfig.Profiles.Count, sha256 = Hash(expected) };
            Console.WriteLine("METRIC " + JsonSerializer.Serialize(metric));
            if (single)
                Check(revision == (scenario == "missing" ? 2 : 1), "startup must not perform a second full configuration load");
            if (scenario == "corrupt")
            {
                Check(fallback, "corrupt config enters recovery mode");
                Check(File.ReadAllText(configPath) == "{invalid", "corrupt file remains recoverable");
                Check(Directory.GetFiles(Path.GetDirectoryName(configPath)!, "config.json.corrupt.*").Length == 1, "one backup is preserved");
                Check(!ConfigManager.SaveConfig(), "automatic save cannot overwrite recovery data");
                Check(File.ReadAllText(configPath) == "{invalid", "failed automatic save preserves corrupt input");
            }
            else if (scenario == "missing")
            {
                Check(!fallback && File.Exists(configPath), "missing config generates normal defaults");
                Check(JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(configPath))?.Profiles.Count > 0, "default config is complete and readable");
                foreach (bool preference in new[] { true, false })
                {
                    ConfigManager.CurrentConfig.StartInTray = preference;
                    Check(ConfigManager.SaveConfig(), "tray preference saves");
                    ConfigManager.LoadConfig();
                    using var saved = JsonDocument.Parse(File.ReadAllText(configPath));
                    Check(saved.RootElement.GetProperty("StartInTray").GetBoolean() == preference &&
                        ConfigManager.CurrentConfig.StartInTray == preference, "explicit tray preference round-trips");
                }

            }
            else
            {
                Check(!fallback && ConfigManager.CurrentConfig.Profiles.Count == 121, "full fixture loaded");
                Check(ConfigManager.CurrentConfig.Profiles[1].Layers.Count == 3, "all fixture layers preserved");
                Check(ConfigManager.CurrentConfig.Profiles[1].Layers[1].ActivationTrigger?.MouseButton == "MiddleButton", "layer binding retained");
                var preserved = ConfigManager.CurrentConfig;
                if (single)
                {
                    var initializer = typeof(App).GetMethod("InitializeConfiguration", BindingFlags.Static | BindingFlags.NonPublic)!;
                    initializer.Invoke(null, null);
                    initializer.Invoke(null, null);
                    Check(ReferenceEquals(preserved, ConfigManager.CurrentConfig) && ConfigManager.ConfigurationRevision == revision,
                        "repeated startup initialization does not replace data or republish routes");
                }
                var edited = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(configPath))!;
                edited.LongPressDelayMs = 321;
                File.WriteAllText(configPath, JsonSerializer.Serialize(edited));
                ConfigManager.LoadConfig();
                Check(ConfigManager.CurrentConfig.LongPressDelayMs == 321, "explicit reload still reads disk");
                Check(!ReferenceEquals(preserved, ConfigManager.CurrentConfig) && ConfigManager.ConfigurationRevision == revision + 1,
                    "explicit reload replaces config and republishes routes exactly once");
                Check(ProfileTriggerRouter.TryResolveMouse("app0.exe", "MiddleButton", System.Windows.Input.ModifierKeys.None, out var activation)
                    == ProfileTriggerResolution.Match && activation?.Layer == ConfigManager.CurrentConfig.Profiles[1].Layers[1], "reloaded layer route matches actual loaded layer");
            }
            AppLogger.Shutdown();
            Console.WriteLine($"PASS {_checks} startup checks; no GUI, hooks, actions or input injection.");
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"FAIL {_checks}: {e}");
            return 1;
        }
    }

    private static void TestSettingsControls(string directory)
    {
        ConfigManager.DisableAutoStartSync = true;
        ConfigManager.CurrentConfig.AutoCheckUpdate = false;
        ConfigManager.CurrentConfig.StartInTray = true;
        using var blockDispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher.DisableProcessing();
        var app = new App();
        app.InitializeComponent();
        var settings = new SettingsWindow();
        settings.EnsureUiInitialized();
        var tray = (System.Windows.Controls.CheckBox)settings.FindName("StartInTrayCheckBox");
        Check(tray.IsChecked == true, "compiled checkbox loads saved preference");
        foreach (string language in new[] { "zh-CN", "zh-TW", "en", "ja" })
        {
            I18n.SetLanguage(language);
            settings.ApplyLocalization();
            foreach (string name in new[] { "ProbePluginSourcesButton", "ProbeUpdateSourcesButton" })
            {
                var button = (System.Windows.Controls.Button)settings.FindName(name);
                Check((string)button.Content == I18n.T("SourceProbeButton") && button.IsEnabled, name + " localized and available");
            }
            var formatter = typeof(SettingsWindow).GetMethod("FormatProbeResult", BindingFlags.Static | BindingFlags.NonPublic)!;
            var pluginChannels = ((System.Windows.Controls.ComboBox)settings.FindName("PluginDownloadChannelComboBox"))
                .Items.OfType<System.Windows.Controls.ComboBoxItem>().Where(item => (string)item.Tag != "auto").ToArray();
            var labels = pluginChannels.Select(item => (string)formatter.Invoke(null, new object[] {
                new SourceMeasurement((string)item.Tag, 123) })!).ToArray();
            Check(labels.Length == 5 && labels.Distinct().Count() == 5 && labels.All(label => label.Contains("123 ms")),
                "each selectable transport has its own localized timing");
            string limited = (string)formatter.Invoke(null, new object[] { new SourceMeasurement("atomgit", null, "SourceProbeHttp", 429) })!;
            string tls = (string)formatter.Invoke(null, new object[] { new SourceMeasurement("mirror", null, "SourceProbeTls") })!;
            Check(limited.Contains("HTTP 429") && tls.Contains(I18n.T("SourceProbeTls")), "transport failure reasons remain distinguishable");
            foreach (string name in new[] { "UpdateSourceComboBox", "PluginDownloadChannelComboBox" })
            {
                var combo = (System.Windows.Controls.ComboBox)settings.FindName(name);
                var auto = combo.Items.OfType<System.Windows.Controls.ComboBoxItem>().Single(i => (string)i.Tag == "auto");
                Check((string)auto.Content == I18n.T("SourceAuto"), name + " has localized automatic choice");
            }
        }
        var location = (System.Windows.Controls.TextBlock)settings.FindName("DataLocationText");
        Check(location.Text.Contains(Path.Combine(directory, "StarPie")), "settings shows active isolated data location");
        tray.IsChecked = false;
        Check(ConfigManager.CurrentConfig.StartInTray, "programmatic checkbox initialization does not mutate preference");
        tray.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Check(!ConfigManager.CurrentConfig.StartInTray, "compiled checkbox click updates preference");
        Check(!settings.IsVisible, "test completed without showing a window");
        AppLogger.Shutdown();
    }

    private static void TestLaunchEditors()
    {
        ConfigManager.DisableAutoStartSync = true;
        var config = ConfigManager.CurrentConfig;
        config.AutoCheckUpdate = false;
        config.EnableGlobalInheritance = true;
        var global = ConfigManager.GetGlobalProfile()!;
        var launch = new ActionItem { Type = "Launch", Parameter = "fixture.exe" };
        var child = new ActionItem { Type = "Plugin", PluginActionRef = new()
            { PluginId = "starpie.builtin.launch", ContributionId = "launch" } };
        launch.SubActions = new() { child };
        global.EnsureLayers();
        global.Actions[0] = launch;
        global.CenterAction = launch.Clone();
        global.EnableCenterAction = true;
        global.SyncActiveLayerFromRootProperties();
        using var blockDispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher.DisableProcessing();
        var app = new App();
        app.InitializeComponent();
        var window = new SettingsWindow();
        window.EnsureUiInitialized();
        var box = (System.Windows.Controls.CheckBox)window.FindName("FocusLaunchPlacementCheckBox");
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        void Set(string name, object? value) => typeof(SettingsWindow).GetField(name, flags)!.SetValue(window, value);
        void Refresh() => typeof(SettingsWindow).GetMethod("UpdateFocusEditorUi", flags)!.Invoke(window, null);
        void Click(bool value)
        {
            box.IsChecked = value;
            box.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        }

        Set("_selectedProfile", global);
        Set("_selectedSlotIndex", 0);
        Set("_selectedSubActionIndex", null);
        Refresh();
        Check(box.Visibility == System.Windows.Visibility.Visible && box.IsChecked == false, "legacy Launch control defaults off");
        Click(true);
        Check(global.Actions[0].LaunchOnCursorMonitor, "main slot click updates action");
        Set("_selectedSubActionIndex", 0);
        Refresh();
        Check(box.Visibility == System.Windows.Visibility.Visible && box.IsChecked == false, "official launch contribution has independent control");
        Click(true);
        Check(global.Actions[0].SubActions![0].LaunchOnCursorMonitor, "subaction click updates correct action");
        foreach (string language in new[] { "zh-CN", "zh-TW", "en", "ja" })
        {
            I18n.SetLanguage(language);
            window.ApplyLocalization();
            Check((string)box.Content == I18n.T("LaunchPlacementLabel") && (string)box.Content != "LaunchPlacementLabel", language + " launch label");
        }

        var local = new WheelProfile { ProcessName = "T6", EnableCenterAction = false };
        local.EnsureLayers();
        config.Profiles.Add(local);
        Set("_selectedProfile", local);
        Set("_selectedSlotIndex", -1);
        Set("_selectedSubActionIndex", null);
        Refresh();
        Check(box.Visibility == System.Windows.Visibility.Visible && box.IsChecked == false, "inherited center displays global preference");
        Click(true);
        Check(local.GetEffectiveCenterAction() is { LaunchOnCursorMonitor: true, IsInherited: false } &&
              global.GetEffectiveCenterAction() is { LaunchOnCursorMonitor: false }, "editing inherited center creates only local override");
        var other = new ActionItem { Type = "Plugin", PluginActionRef = new()
            { PluginId = "fixture.other", ContributionId = "launch" } };
        global.Actions[0] = other;
        Set("_selectedProfile", global);
        Set("_selectedSlotIndex", 0);
        Refresh();
        Check(box.Visibility == System.Windows.Visibility.Collapsed, "other plugin with same short id hides control");
        var slot = new SlotViewModel("test", other);
        var sub = new SubSlotViewModel { Action = child };
        var mapping = new GestureMappingViewModel(new GestureMapping { Action = launch });
        slot.LaunchOnCursorMonitor = true;
        sub.LaunchOnCursorMonitor = false;
        mapping.LaunchOnCursorMonitor = false;
        Check(!other.LaunchOnCursorMonitor && !child.LaunchOnCursorMonitor && !launch.LaunchOnCursorMonitor, "editor bindings target the right models and preserve unrelated actions");
        slot.Dispose();
        Check(!window.IsVisible, "settings remained offscreen");
        AppLogger.Shutdown();
    }

    private static AppConfig Fixture()
    {
        var config = new AppConfig { Profiles = new() { new WheelProfile { ProcessName = "Global" } }, Language = "en" };
        for (int i = 0; i < 120; i++)
        {
            var profile = new WheelProfile { ProcessName = $"App{i}", BoundProcesses = $"app{i}.exe", Layers = new() };
            for (int layer = 0; layer < 3; layer++)
            {
                profile.Layers.Add(new WheelLayer { Name = $"Layer{layer}", SectorCount = 8,
                    ActivationTrigger = layer == 1 ? new TriggerConfig { TriggerType = "Mouse", MouseButton = "MiddleButton" } : null,
                    Actions = Enumerable.Range(0, 8).Select(n => new ActionItem { Type = "Hotkey", Name = $"Action{n}", Parameter = "Ctrl+C" }).ToList() });
            }
            config.Profiles.Add(profile);
        }
        return config;
    }

    private static IEnumerable<MethodBase> Calls(MethodInfo method)
    {
        byte[] il = method.GetMethodBody()!.GetILAsByteArray()!;
        for (int offset = 0; offset < il.Length;)
        {
            short key = il[offset++];
            if (key == 0xfe) key = (short)(0xfe00 | il[offset++]);
            var code = Codes[key];
            int size = code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, offset),
                _ => 4
            };
            if (code == OpCodes.Call || code == OpCodes.Callvirt)
            {
                MethodBase? called = method.Module.ResolveMethod(BitConverter.ToInt32(il, offset));
                if (called != null) yield return called;
            }
            offset += size;
        }
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static void Check(bool condition, string description) { _checks++; if (!condition) throw new InvalidOperationException(description); }
}
