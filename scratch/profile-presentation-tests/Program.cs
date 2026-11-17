using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using WinPieGestures;

static class Program
{
    static int checks;
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks++; }
    static TriggerConfig Mouse(string button, bool ctrl = false) => new() { TriggerType = "Mouse", MouseButton = button, RequireCtrl = ctrl };
    static TriggerConfig Key(uint vk, bool ctrl = false, bool alt = false) => new()
    {
        TriggerType = "Keyboard",
        VkCode = vk,
        Key = KeyInterop.KeyFromVirtualKey((int)vk).ToString(),
        RequireCtrl = ctrl,
        RequireAlt = alt
    };
    static WheelProfile Profile(string id, bool context = true, string? app = null) {
        var p = new WheelProfile { ProcessName = id, DisplayName = id, UseApplicationContext = context, BoundProcesses = app, HasExplicitProcessBindings = app != null };
        p.EnsureLayers(); return p;
    }
    static ProfileTriggerActivation? Resolve(string app, string button, ModifierKeys mods = ModifierKeys.None) =>
        ProfileTriggerRouter.TryResolveMouse(app, button, mods, out var hit) == ProfileTriggerResolution.Match ? hit : null;
    static void Publish(AppConfig config) { ConfigManager.CurrentConfig = config; ProfileTriggerRouter.Publish(config); }
    [STAThread]
    static int Main(string[] args)
    {
        try {
            string isolated = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "artifacts", "data-" + Guid.NewGuid().ToString("N")));
            Environment.SetEnvironmentVariable("LOCALAPPDATA", isolated);
            Environment.SetEnvironmentVariable("APPDATA", isolated);
            Directory.CreateDirectory(isolated);
            string candidate = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().Single(x => x.Key == "StarPieCandidateDir").Value!;
            string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            Check(Hash(Path.Combine(candidate, "StarPie.dll")) == Hash(typeof(WheelProfile).Assembly.Location), "candidate hash");
            Console.WriteLine($"CANDIDATE={candidate}; SHA256={Hash(typeof(WheelProfile).Assembly.Location)}");
            var legacy = JsonSerializer.Deserialize<WheelProfile>("{\"ProcessName\":\"old.exe\",\"BoundProcesses\":\"\"}")!;
            Check(legacy.UseApplicationContext && !legacy.HasExplicitProcessBindings, "legacy defaults");
            Check(ConfigManager.IsProfileBoundToProcess(legacy, "old"), "legacy empty fallback");
            legacy.HasExplicitProcessBindings = true;
            Check(!ConfigManager.IsProfileBoundToProcess(legacy, "old.exe"), "explicit empty stays unbound");
            legacy.UseApplicationContext = false;
            var copy = JsonSerializer.Deserialize<WheelProfile>(JsonSerializer.Serialize(legacy))!;
            Check(!copy.UseApplicationContext && copy.HasExplicitProcessBindings, "false/explicit flags roundtrip");
            var clone = legacy.Clone("clone");
            Check(!clone.UseApplicationContext && clone.HasExplicitProcessBindings && clone.BoundProcesses == "", "clone preserves explicit empty and mode");
            var spaced = new WheelProfile { ProcessName = "old app.exe", DisplayName = "Friendly Label" };
            Check(ConfigManager.IsProfileBoundToProcess(spaced, "old app.exe"), "legacy whole process name with spaces");
            spaced.BoundProcesses = "old app.exe, Friendly Label.exe"; spaced.HasExplicitProcessBindings = true;
            Check(ConfigManager.IsProfileBoundToProcess(spaced, "old app.exe"), "explicit list preserves process name spaces");
            Check(!ConfigManager.IsProfileBoundToProcess(spaced, "Friendly.exe") && !ConfigManager.IsProfileBoundToProcess(spaced, "Label.exe"), "friendly-name words do not become separate process bindings");
            var legacyList = new WheelProfile { ProcessName = "legacy-list", BoundProcesses = "chrome.exe code.exe" };
            Check(ConfigManager.IsProfileBoundToProcess(legacyList, "chrome.exe") && ConfigManager.IsProfileBoundToProcess(legacyList, "code.exe"), "legacy space-separated lists unchanged");

            var global = Profile("Global"); global.DisplayName = "我的基础轮盘";
            global.Layers[0].Name = "通用层";
            var parallel = Profile("workflow", false, "stale.exe");
            parallel.Layers[0].ActivationTrigger = Mouse("MiddleButton");
            parallel.Layers.Add(new WheelLayer { Name = "建模层", ActivationTrigger = Mouse("XButton1") });
            var app = Profile("editor", true, "editor.exe");
            var config = new AppConfig { Profiles = new() { global, parallel, app }, Trigger = Mouse("RightButton") };
            Publish(config);
            Check(!ConfigManager.IsProfileBoundToProcess(parallel, "stale.exe"), "parallel not process fallback");
            Check(Resolve("editor.exe", "MiddleButton")?.Profile == parallel, "parallel in application default scope");
            Check(Resolve("unrelated.exe", "MiddleButton")?.Profile == parallel, "parallel in unrelated app");
            Check(Resolve("editor.exe", "RightButton")?.Profile == app, "existing application default unchanged");
            Check(Resolve("unrelated", "RightButton")?.Profile == global, "global default unchanged");
            Check(Resolve("unrelated", "XButton1")?.Layer == parallel.Layers[1], "parallel layer route");
            Check(Resolve("unrelated", "MiddleButton", ModifierKeys.Shift) == null, "parallel exact modifiers");
            var activation = Resolve("unrelated", "XButton1")!;
            parallel.Layers[1].ActivationTrigger = Mouse("XButton2");
            Check(activation.Trigger.MouseButton == "XButton1" && activation.Layer == parallel.Layers[1], "activation trigger snapshot");
            Check(ProfileTriggerRouter.ValidateLayerTrigger(config, parallel, parallel.Layers[0], Mouse("RightButton")) == ProfileTriggerValidationError.Conflict, "global trigger collision");
            Check(ProfileTriggerRouter.ConflictsWithParallelTrigger(config, Mouse("MiddleButton")), "global editing checks parallel keys");
            Check(ProfileTriggerRouter.ValidateLayerTrigger(config, parallel, parallel.Layers[0], Mouse("RightButton", true)) == ProfileTriggerValidationError.None, "distinct exact chord allowed");
            parallel.Layers[0].ActivationTrigger = Mouse("RightButton", true); Publish(config);
            Check(Resolve("editor.exe", "RightButton", ModifierKeys.Control)?.Profile == parallel, "parallel chord precedes minimum-modifier legacy trigger");
            app.Layers[0].ActivationTrigger = Mouse("XButton2");
            Check(ProfileTriggerRouter.ValidateBindings(config, parallel) == ProfileTriggerValidationError.Conflict, "application dedicated collision");
            Publish(config);
            Check(Resolve("unrelated", "RightButton", ModifierKeys.Control)?.Profile != parallel, "invalid imported parallel disabled entirely");
            Check(Resolve("editor.exe", "XButton2")?.Profile == app, "existing app route retained on bad parallel import");
            app.Layers[0].ActivationTrigger = null;
            parallel.Layers.RemoveAt(1);
            Check(ProfileTriggerRouter.ValidateLayerTrigger(config, parallel, parallel.Layers[0], null) == ProfileTriggerValidationError.MissingTrigger, "cannot clear last parallel key");
            var empty = Profile("draft", false, "draft.exe"); config.Profiles.Add(empty);
            Check(ProfileTriggerRouter.ValidateBindings(config, empty) == ProfileTriggerValidationError.MissingTrigger, "no-key parallel rejected");
            Publish(config);
            Check(Resolve("draft.exe", "RightButton")?.Profile == global, "invalid parallel never becomes implicit app profile");
            var pending = Profile("pending", true, ""); config.Profiles.Add(pending);
            Check(ProfileTriggerRouter.ValidateContextMode(config, pending, false) == ProfileTriggerValidationError.MissingTrigger && pending.UseApplicationContext, "mode validation has no mutation");
            pending.Layers[0].ActivationTrigger = Mouse("XButton1");
            Check(ProfileTriggerRouter.ValidateContextMode(config, pending, false) == ProfileTriggerValidationError.None, "valid explicit mode switch");

            global.Layers.Add(new WheelLayer { Name = "工具层" });
            foreach (string language in new[] { "zh-CN", "zh-TW", "en-US", "ja-JP" }) {
                I18n.SetLanguage(language);
                string caption = ProfilePresentation.LayerCaption(global, 1);
                Check(caption.Contains("我的基础轮盘") && caption.Contains("工具层") && caption.Contains("2/2"), "localized caption retains user names " + language);
                Check(global.ProcessName == "Global", "global identity stable " + language);
                Check(I18n.T("ProfileUseContext") != "ProfileUseContext", "localized context " + language);
            }
            var longName = new string('名', 180); global.DisplayName = longName;
            Check(ProfilePresentation.LayerCaption(global, 1).Contains(longName), "formatter preserves full name for tooltip");
            Check(ProfilePresentation.LayerCaption(global, 200).Contains("2/2"), "out of range caption clamps safely");

            var removal = Profile("remove", true, "remove.exe");
            removal.ActivationTrigger = Mouse("XButton1");
            removal.Layers[0].ActivationTrigger = Mouse("MiddleButton");
            removal.Layers.Add(new WheelLayer { Name = "second" });
            var removeMethod = typeof(WheelProfile).GetMethod("RemoveLayer", BindingFlags.Instance | BindingFlags.NonPublic);
            if (removeMethod != null) removeMethod.Invoke(removal, new object[] { removal.Layers[0] });
            else { removal.Layers.RemoveAt(0); removal.ActiveLayerIndex = 0; removal.SyncRootPropertiesFromActiveLayer(); }
            config.Profiles = new() { global, removal }; Publish(config);
            Check(Resolve("remove.exe", "XButton1") == null, "deleting first layer cannot resurrect old root trigger");
            Check(removal.Layers.Count == 1, "layer deletion retains successor");
            var guard = typeof(ProfileTriggerRouter).GetMethod("ValidateLayerRemoval", BindingFlags.Static | BindingFlags.NonPublic);
            Check(guard != null, "candidate has deletion preflight");
            var parallelRemoval = Profile("parallel-removal", false, "");
            parallelRemoval.Layers[0].ActivationTrigger = Mouse("MiddleButton");
            parallelRemoval.Layers.Add(new WheelLayer { Name = "empty" });
            config.Profiles = new() { global, parallelRemoval };
            Check(guard!.Invoke(null, new object[] { config, parallelRemoval, parallelRemoval.Layers[0] })!.ToString() == "MissingTrigger", "cannot delete final reachable parallel trigger");
            Check(parallelRemoval.Layers.Count == 2 && parallelRemoval.Layers[0].ActivationTrigger != null, "deletion preflight does not mutate original");
            parallelRemoval.Layers[1].ActivationTrigger = Mouse("XButton2");
            Check(guard.Invoke(null, new object[] { config, parallelRemoval, parallelRemoval.Layers[0] })!.ToString() == "None", "parallel deletion allowed with remaining valid trigger");

            Check(ProfileTriggerRouter.ValidateBindings(config, app, "") == ProfileTriggerValidationError.None, "explicit clear can be validated without name fallback");
            VerifyConfigTransfer(isolated);
            VerifyDisplayModeCompatibility(isolated);
            RecordingGuards.Verify(Check);
            if (args.Length != 0 && File.Exists(args[0])) VerifyLayoutAndUiFlows(args[0]);
            Console.WriteLine($"PASS {checks}; no windows, hooks, real input or user config writes"); return 0;
        } catch (Exception error) { Console.Error.WriteLine($"FAIL {checks}: {error}"); return 1; }
    }

    static void VerifyConfigTransfer(string isolated)
    {
        ConfigManager.DisableAutoStartSync = true;
        ConfigManager.LoadConfig();
        string original = ConfigManager.GetConfigJsonForExport(ConfigManager.CurrentConfig.ActiveConfigProfileName);
        Check(ConfigManager.ImportExternalConfigJson(original, "Transfer", out string first) && first == "Transfer", "import valid JSON commits configuration");
        string firstPath = Path.Combine(ConfigManager.ConfigsFolder, first + ".json");
        byte[] firstBytes = File.ReadAllBytes(firstPath);
        Check(ConfigManager.ImportExternalConfigJson(original, "Transfer", out string second) && second != first, "import uses a unique saved configuration name");
        Check(File.ReadAllBytes(firstPath).SequenceEqual(firstBytes), "import never overwrites same-name saved configuration");
        Check(ConfigManager.GetConfigJsonForExport(first) == System.Text.Encoding.UTF8.GetString(firstBytes), "export inactive configuration reads its saved snapshot");
        var active = ConfigManager.CurrentConfig;
        string[] beforeFiles = Directory.GetFiles(ConfigManager.ConfigsFolder).Order().ToArray();
        Check(!ConfigManager.ImportExternalConfigJson("{invalid", "Bad", out _) && ReferenceEquals(active, ConfigManager.CurrentConfig), "malformed import preserves active configuration");
        Check(Directory.GetFiles(ConfigManager.ConfigsFolder).Order().SequenceEqual(beforeFiles), "malformed import leaves no configuration file");
        string configPath = Path.Combine(isolated, "StarPie", "config.json");
        byte[] diskBefore = File.ReadAllBytes(configPath);
        using (var locked = new FileStream(configPath, FileMode.Open, FileAccess.Read, FileShare.None))
            Check(!ConfigManager.ImportExternalConfigJson(original, "Locked", out _) && ReferenceEquals(active, ConfigManager.CurrentConfig), "failed persistence restores active reference");
        Check(File.ReadAllBytes(configPath).SequenceEqual(diskBefore) && !File.Exists(Path.Combine(ConfigManager.ConfigsFolder, "Locked.json")), "failed persistence preserves disk and removes newly created snapshot");
        // A language/UI subscriber failing after persistence cannot undo committed assets.
        var imported = JsonSerializer.Deserialize<AppConfig>(original)!;
        imported.Language = I18n.CurrentLanguageCode == "ja" ? "en" : "ja";
        Action failure = () => throw new InvalidOperationException("presentation-only failure fixture");
        I18n.LanguageChanged += failure;
        try { Check(ConfigManager.ImportExternalConfigJson(JsonSerializer.Serialize(imported), "Presentation", out _), "presentation listener failure after save remains committed success"); }
        finally { I18n.LanguageChanged -= failure; }
        ConfigManager.LoadConfig();
        Check(ConfigManager.CurrentConfig.ActiveConfigProfileName == "Presentation", "committed import survives reload");
    }

    static void VerifyDisplayModeCompatibility(string isolated)
    {
        var fresh = new AppConfig();
        Check(fresh.ProfileQuickSwitchDisplayMode == ProfileQuickSwitchDisplayModes.IconAndText, "default quick-switch mode is IconAndText");
        Check(ProfilePresentation.NormalizeQuickSwitchDisplayMode(null) == ProfileQuickSwitchDisplayModes.IconAndText, "null display mode falls back to IconAndText");
        Check(ProfilePresentation.NormalizeQuickSwitchDisplayMode("  ") == ProfileQuickSwitchDisplayModes.IconAndText, "whitespace display mode falls back to IconAndText");
        Check(ProfilePresentation.NormalizeQuickSwitchDisplayMode("UnknownMode") == ProfileQuickSwitchDisplayModes.IconAndText, "unknown display mode falls back to IconAndText");
        Check(ProfilePresentation.NormalizeQuickSwitchDisplayMode("textonly") == ProfileQuickSwitchDisplayModes.TextOnly, "case-insensitive TextOnly normalizes cleanly");
        Check(ProfilePresentation.NormalizeQuickSwitchDisplayMode("icononly") == ProfileQuickSwitchDisplayModes.IconOnly, "case-insensitive IconOnly normalizes cleanly");

        // Legacy JSON missing ProfileQuickSwitchDisplayMode defaults cleanly through load and health check.
        string baseJson = ConfigManager.GetConfigJsonForExport(ConfigManager.CurrentConfig.ActiveConfigProfileName);
        var node = System.Text.Json.Nodes.JsonNode.Parse(baseJson)!.AsObject();
        node.Remove("ProfileQuickSwitchDisplayMode");
        Check(ConfigManager.ImportExternalConfigJson(node.ToJsonString(), "LegacyNoMode", out string legacyName), "import legacy JSON without display mode");
        ConfigManager.LoadConfig();
        Check(ConfigManager.CurrentConfig.ProfileQuickSwitchDisplayMode == ProfileQuickSwitchDisplayModes.IconAndText, "missing display mode in old JSON falls back to IconAndText");

        // Unknown value in imported JSON falls back without error.
        node["ProfileQuickSwitchDisplayMode"] = "FutureCarousel";
        Check(ConfigManager.ImportExternalConfigJson(node.ToJsonString(), "UnknownMode", out _), "import JSON with unknown display mode succeeds");
        ConfigManager.LoadConfig();
        Check(ConfigManager.CurrentConfig.ProfileQuickSwitchDisplayMode == ProfileQuickSwitchDisplayModes.IconAndText, "unknown display mode falls back cleanly after reload");

        // All three display modes round-trip through JSON export/import and .starpie-config portable packages.
        string bundleDir = Path.Combine(isolated, "portable-display-modes");
        string sourceIcons = Path.Combine(bundleDir, "src-icons");
        string targetIcons = Path.Combine(bundleDir, "dst-icons");
        Directory.CreateDirectory(sourceIcons);
        Directory.CreateDirectory(targetIcons);
        foreach (string mode in new[] { ProfileQuickSwitchDisplayModes.TextOnly, ProfileQuickSwitchDisplayModes.IconOnly, ProfileQuickSwitchDisplayModes.IconAndText })
        {
            var obj = System.Text.Json.Nodes.JsonNode.Parse(baseJson)!.AsObject();
            obj["ProfileQuickSwitchDisplayMode"] = mode;
            Check(ConfigManager.ImportExternalConfigJson(obj.ToJsonString(), "Mode_" + mode, out string savedName), "import display mode " + mode);
            ConfigManager.LoadConfig();
            Check(ConfigManager.CurrentConfig.ProfileQuickSwitchDisplayMode == mode, "reload preserves display mode " + mode);
            string exportedJson = ConfigManager.GetConfigJsonForExport(savedName);
            var exportedNode = System.Text.Json.Nodes.JsonNode.Parse(exportedJson)!.AsObject();
            Check((string?)exportedNode["ProfileQuickSwitchDisplayMode"] == mode, "JSON export preserves display mode " + mode);

            string packagePath = Path.Combine(bundleDir, mode + ".starpie-config");
            PortableConfigBundle.Export(exportedJson, packagePath, sourceIcons);
            using var tx = PortableConfigBundle.PrepareImport(packagePath, targetIcons);
            Check(ConfigManager.ImportExternalConfigJson(tx.ConfigJson, "Bundle_" + mode, out _), "portable package import succeeds for " + mode);
            tx.Commit();
            ConfigManager.LoadConfig();
            Check(ConfigManager.CurrentConfig.ProfileQuickSwitchDisplayMode == mode, "portable package roundtrip preserves display mode " + mode);
        }
    }

    static readonly BindingFlags AllInstance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    static void SetField(object target, string name, object? value) =>
        target.GetType().GetField(name, AllInstance)?.SetValue(target, value);

    static T? GetField<T>(object target, string name) =>
        (T?)target.GetType().GetField(name, AllInstance)?.GetValue(target);

    static object? Invoke(object target, string name, params object?[] args) =>
        target.GetType().GetMethod(name, AllInstance)!.Invoke(target, args);

    static void WireNamedElements(object target, DependencyObject root)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!seen.Add(current)) continue;
            if (current is FrameworkElement fe && !string.IsNullOrEmpty(fe.Name))
            {
                typeof(SettingsWindow).GetField(fe.Name, AllInstance)?.SetValue(target, fe);
            }
            foreach (object child in LogicalTreeHelper.GetChildren(current))
            {
                if (child is DependencyObject dep) queue.Enqueue(dep);
            }
        }
    }

    static SettingsWindow CreateHeadlessWindow(FrameworkElement resourceHost)
    {
        var win = (SettingsWindow)RuntimeHelpers.GetUninitializedObject(typeof(SettingsWindow));
        foreach (var f in typeof(SettingsWindow).GetFields(AllInstance))
        {
            if (f.IsInitOnly && f.FieldType != typeof(SettingsWindow) && !f.FieldType.IsAbstract && f.GetValue(win) == null)
            {
                if (f.Name == "_profileIcons") f.SetValue(win, new Dictionary<string, BitmapSource?>(StringComparer.OrdinalIgnoreCase));
                else if (f.Name == "_profileIconLoads") f.SetValue(win, new Dictionary<string, Task<BitmapSource?>>(StringComparer.OrdinalIgnoreCase));
                else if (f.FieldType.GetConstructor(Type.EmptyTypes) is ConstructorInfo ctor)
                    f.SetValue(win, ctor.Invoke(null));
            }
        }
        SetField(win, "_layoutResourceHost", resourceHost);
        return win;
    }

    static double MeasureAndArrange(Grid rootGrid, double width)
    {
        rootGrid.Width = width;
        rootGrid.Measure(new Size(width, 2000));
        rootGrid.Arrange(new Rect(0, 0, width, rootGrid.DesiredSize.Height));
        rootGrid.UpdateLayout();
        return rootGrid.DesiredSize.Height;
    }

    static void PumpDispatcherFrames()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    static void VerifyLayoutAndUiFlows(string source)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var document = XDocument.Load(source);
        var original = document.Descendants().Single(e => (string?)e.Attribute("Name") == "ProfileBindingCardBorder");
        var panel = new XElement(original.Parent!);
        var resources = new XElement(document.Root!.Elements().First().Elements().Single());
        var app = XDocument.Load(Path.Combine(Path.GetDirectoryName(source)!, "App.xaml"));
        resources.AddFirst(new XElement(wpf + "ResourceDictionary.MergedDictionaries",
            new XElement(app.Root!.Elements().First().Elements().Single())));
        var root = new XElement(wpf + "Grid", document.Root!.Attributes().Where(a => a.IsNamespaceDeclaration),
            new XElement(wpf + "Grid.Resources", resources), panel);
        foreach (var attribute in root.DescendantsAndSelf().Attributes().ToArray())
        {
            if (attribute.IsNamespaceDeclaration && attribute.Value.StartsWith("clr-namespace:", StringComparison.Ordinal)) attribute.Value += ";assembly=StarPie";
            if (!attribute.IsNamespaceDeclaration && (attribute.Value.EndsWith("_Click", StringComparison.Ordinal) ||
                attribute.Value.EndsWith("_Changed", StringComparison.Ordinal) || attribute.Value.EndsWith("_Checked", StringComparison.Ordinal) ||
                attribute.Value.EndsWith("_Unchecked", StringComparison.Ordinal) || attribute.Value.EndsWith("_SelectionChanged", StringComparison.Ordinal) ||
                attribute.Value.EndsWith("_PreviewMouseWheel", StringComparison.Ordinal) ||
                attribute.Value.EndsWith("_TextChanged", StringComparison.Ordinal))) attribute.Remove();
        }
        foreach (var setter in root.Descendants(wpf + "EventSetter").ToArray()) setter.Remove();

        var grid = (Grid)XamlReader.Parse(root.ToString());
        AppThemeManager.ApplyTheme(grid, "Light");
        var visualPanel = (Grid)grid.Children[0];
        Check(visualPanel.RowDefinitions.Count == 4, "profile card has four real rows");

        var headerRow = visualPanel.Children.OfType<Grid>().Single(g => g.Name == "ProfileHeaderRowGrid");
        var quickSwitchRow = visualPanel.Children.OfType<Grid>().Single(g => g.Name == "ProfileQuickSwitchRowGrid");
        var shortcuts = quickSwitchRow.Children.OfType<ScrollViewer>().Single(s => s.Name == "ProfileQuickSwitchScrollViewer");
        var bindings = visualPanel.Children.OfType<Border>().Single(e => e.Name == "ProfileBindingCardBorder");
        Check(Grid.GetRow(headerRow) == 0 && Grid.GetRow(quickSwitchRow) == 1 && Grid.GetRow(bindings) == 2,
            "header row, quick switch row, and binding card occupy rows 0, 1, 2");
        Check(shortcuts.HorizontalScrollBarVisibility == ScrollBarVisibility.Hidden,
            "quick-switch ScrollViewer hides bottom horizontal scrollbar");

        var win = CreateHeadlessWindow(grid);
        WireNamedElements(win, visualPanel);
        var profileCombo = GetField<ComboBox>(win, "MappingsProfileComboBox")!;
        var displayCombo = GetField<ComboBox>(win, "ProfileDisplayModeComboBox")!;
        var dupBtn = GetField<Button>(win, "DuplicateProfileBtn")!;

        // Wire companion controls that live outside the extracted card grid in full SettingsWindow.
        var canvasGrid = new Grid { Visibility = Visibility.Visible };
        var listGrid = new Grid { Visibility = Visibility.Collapsed };
        var canvasRadio = new RadioButton { IsChecked = true };
        var listRadio = new RadioButton { IsChecked = false };
        var proPanel = new Border { Visibility = Visibility.Collapsed };
        var proStatus = new TextBlock();
        var proRecordBtn = new Button();
        var proResetBtn = new Button();
        var layerCombo = new ComboBox();
        SetField(win, "MappingsCanvasModeGrid", canvasGrid);
        SetField(win, "MappingsListModeGrid", listGrid);
        SetField(win, "MappingsViewModeCanvasRadio", canvasRadio);
        SetField(win, "MappingsViewModeListRadio", listRadio);
        SetField(win, "ProfileActivationPanel", proPanel);
        SetField(win, "ProfileActivationStatusText", proStatus);
        SetField(win, "RecordProfileActivationButton", proRecordBtn);
        SetField(win, "ResetProfileActivationButton", proResetBtn);
        SetField(win, "LayerSelectComboBox", layerCombo);
        SetField(win, "FocusActionPanelHost", new StackPanel());

        profileCombo.SelectionChanged += (s, e) => Invoke(win, "MappingsProfileComboBox_SelectionChanged", s!, e);
        layerCombo.SelectionChanged += (s, e) => Invoke(win, "LayerSelectComboBox_SelectionChanged", s!, e);
        displayCombo.SelectionChanged += (s, e) => Invoke(win, "ProfileDisplayModeComboBox_SelectionChanged", s!, e);

        I18n.SetLanguage("zh-CN");
        var global = Profile("Global");
        global.Layers[0].Name = "通用层";
        global.Layers.Add(new WheelLayer { Name = "第二层" });
        var jianying = Profile("JianyingPro", context: true, app: "JianyingPro.exe");
        var multiApp = Profile("AdobeSuite", context: true, app: "Photoshop.exe, Illustrator.exe, Premiere.exe");
        var parallel = Profile("QuickTools", context: false, app: "stale.exe");
        parallel.Layers[0].ActivationTrigger = Mouse("MiddleButton");

        var testConfig = new AppConfig
        {
            Language = "zh-CN",
            ConfigMode = "Simple",
            Trigger = Mouse("RightButton"),
            Profiles = new() { global, jianying, multiApp, parallel }
        };
        Publish(testConfig);

        profileCombo.ItemsSource = testConfig.Profiles;
        profileCombo.SelectedItem = jianying;
        layerCombo.ItemsSource = jianying.Layers;
        layerCombo.SelectedIndex = 0;
        SetField(win, "_selectedProfile", jianying);

        // 1. Non-overlap and layout height checks across widths and profile modes.
        MeasureAndArrange(grid, 760);
        Check(bindings.TranslatePoint(new Point(), visualPanel).Y >= quickSwitchRow.TranslatePoint(new Point(), visualPanel).Y + quickSwitchRow.ActualHeight - 0.1,
            "quick-switch row does not overlap binding card");
        Check(shortcuts.ActualWidth >= quickSwitchRow.ActualWidth - 0.5,
            "quick-switch scroll viewer spans full row width once display mode moved to row 0");
        double comboRight = profileCombo.TranslatePoint(new Point(), headerRow).X + profileCombo.ActualWidth;
        double displayLeft = displayCombo.TranslatePoint(new Point(), headerRow).X;
        double displayRight = displayLeft + displayCombo.ActualWidth;
        double dupLeft = dupBtn.TranslatePoint(new Point(), headerRow).X;
        Check(profileCombo.ActualWidth >= profileCombo.MinWidth - 0.1 &&
              displayLeft >= comboRight - 0.1 &&
              dupLeft >= displayRight - 0.1,
            $"display mode combo sits cleanly in row 0 between MappingsProfileComboBox and DuplicateProfileBtn (combo={profileCombo.ActualWidth:F1}, displayLeft={displayLeft:F1}, dupLeft={dupLeft:F1})");
        Check(double.IsNaN(bindings.Height), "binding card has no fixed height that clips content");

        // Also check row 0 fits without crushing MappingsProfileComboBox at 580 DIP.
        MeasureAndArrange(grid, 580);
        Check(profileCombo.ActualWidth >= profileCombo.MinWidth - 0.1 &&
              dupBtn.TranslatePoint(new Point(), headerRow).X + dupBtn.ActualWidth <= headerRow.ActualWidth + 0.5,
            $"row 0 remains unclipped at 580 DIP (combo={profileCombo.ActualWidth:F1})");
        MeasureAndArrange(grid, 760);

        var detailPanel = GetField<Border>(win, "ProfileBindingsDetailPanel")!;
        Check(detailPanel.Visibility == Visibility.Collapsed, "details panel defaults collapsed");
        double jianyingCollapsedH = bindings.ActualHeight;
        Check(jianyingCollapsedH > 0 && jianyingCollapsedH <= 96.5, $"context profile (1 app, collapsed) height {jianyingCollapsedH:F1} <= 96 DIP at 760 width");

        Invoke(win, "ProfileManageBindingsToggleBtn_Click", GetField<Button>(win, "ProfileManageBindingsToggleBtn")!, new RoutedEventArgs());
        MeasureAndArrange(grid, 760);
        Check(detailPanel.Visibility == Visibility.Visible, "manage toggle expands details panel");
        Check(bindings.ActualHeight > jianyingCollapsedH, "expanded details increases context panel height");

        profileCombo.SelectedItem = parallel;
        MeasureAndArrange(grid, 760);
        Check(detailPanel.Visibility == Visibility.Collapsed, "switching profile auto-collapses details");
        Check(GetField<Panel>(win, "ProfileCustomBindingPanel")!.Visibility == Visibility.Collapsed, "parallel mode hides program binding panel");
        Check(bindings.ActualHeight > 0 && bindings.ActualHeight <= 64.5, $"parallel mode height {bindings.ActualHeight:F1} <= 64 DIP at 760 width");

        profileCombo.SelectedItem = global;
        MeasureAndArrange(grid, 760);
        Check(GetField<Panel>(win, "ProfileContextSwitchRow")!.Visibility == Visibility.Collapsed, "Global profile hides context switch row");
        Check(GetField<Panel>(win, "ProfileCustomBindingPanel")!.Visibility == Visibility.Collapsed, "Global profile hides custom binding panel");
        Check(bindings.ActualHeight > 0 && bindings.ActualHeight <= 64.5, $"Global mode height {bindings.ActualHeight:F1} <= 64 DIP at 760 width");

        // Narrow 600 DIP + English long strings wrap cleanly without fixed-height clipping.
        I18n.SetLanguage("en-US");
        var prevName = multiApp.DisplayName;
        multiApp.DisplayName = "Ultra Long Engineering Creative Profile With Many Words";
        profileCombo.SelectedItem = multiApp;
        Invoke(win, "UpdateProfileBindingUi");
        double narrowH = MeasureAndArrange(grid, 600);
        double expectedCardInner = bindings.DesiredSize.Height - bindings.Margin.Top - bindings.Margin.Bottom;
        var cardContent = (FrameworkElement)bindings.Child;
        Check(narrowH > 0 && bindings.ActualHeight >= expectedCardInner - 0.5 &&
              cardContent.ActualHeight >= cardContent.DesiredSize.Height - 0.5 && bindings.DesiredSize.Height > 0,
            $"narrow 600 DIP English multi-app wraps without clipping (narrowH={narrowH:F2}, actual={bindings.ActualWidth:F2}x{bindings.ActualHeight:F2}, desiredInner={expectedCardInner:F2})");

        // Extra-narrow 440 DIP check ensures WrapPanels wrap onto additional rows cleanly without clipping.
        MeasureAndArrange(grid, 440);
        double extraNarrowInner = bindings.DesiredSize.Height - bindings.Margin.Top - bindings.Margin.Bottom;
        Check(bindings.ActualHeight >= extraNarrowInner - 0.5 && bindings.ActualHeight > jianyingCollapsedH,
            $"440 DIP English multi-app wraps taller cleanly ({bindings.ActualHeight:F2} > {jianyingCollapsedH:F2})");
        MeasureAndArrange(grid, 760);
        multiApp.DisplayName = prevName;
        I18n.SetLanguage("zh-CN");
        profileCombo.SelectedItem = jianying;
        Invoke(win, "UpdateProfileBindingUi");
        MeasureAndArrange(grid, 760);

        // 2. Three-state quick-switch display mode, selection retention, accessible metadata, TextOnly suppression & stale async guard.
        var quickPanel = GetField<StackPanel>(win, "ProfileQuickSwitchPanel")!;
        Check(displayCombo.SelectedItem is ComboBoxItem c0 && (string)c0.Tag == ProfileQuickSwitchDisplayModes.IconAndText,
            "display combo defaults to IconAndText");
        foreach (var btn in quickPanel.Children.OfType<Button>())
        {
            btn.ApplyTemplate();
            Check(btn.Template != null && btn.ActualHeight > 0, "actual quick-switch button template laid out");
        }

        // Inject an in-flight async icon load task in IconAndText, switch to TextOnly, complete task late, verify ignored!
        var iconDict = GetField<Dictionary<string, BitmapSource?>>(win, "_profileIcons")!;
        var loadDict = GetField<Dictionary<string, Task<BitmapSource?>>>(win, "_profileIconLoads")!;
        iconDict.Clear();
        loadDict.Clear();
        string jianyingCacheKey = jianying.ApplicationIconPath + "|JianyingPro.exe";
        var lateTcs = new TaskCompletionSource<BitmapSource?>();
        loadDict[jianyingCacheKey] = lateTcs.Task;
        Invoke(win, "RefreshProfilePresentation");
        var pendingIconBtn = quickPanel.Children.OfType<Button>().Single(b => ReferenceEquals(b.Tag, jianying));
        var staleImage = ((StackPanel)pendingIconBtn.Content).Children.OfType<Image>().Single();

        // Switch via ComboBox SelectionChanged to TextOnly.
        displayCombo.SelectedItem = GetField<ComboBoxItem>(win, "ProfileDisplayModeTextOnlyItem")!;
        Check(testConfig.ProfileQuickSwitchDisplayMode == ProfileQuickSwitchDisplayModes.TextOnly, "combo changes config to TextOnly");
        Check(ReferenceEquals(GetField<WheelProfile>(win, "_selectedProfile"), jianying), "display mode change retains selected profile");

        // Verify TextOnly started no new icon loads when caches are empty.
        iconDict.Clear();
        loadDict.Clear();
        Invoke(win, "RefreshProfilePresentation");
        Check(loadDict.Count == 0 && iconDict.Count == 0, "TextOnly mode starts zero icon loads");
        Check(quickPanel.Children.OfType<Button>().All(b => b.Content is TextBlock), "TextOnly renders text buttons without Image elements");

        // Now complete late callback from prior revision; stale Image and current TextOnly panel must stay untouched!
        var syntheticBitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8);
        syntheticBitmap.Freeze();
        lateTcs.SetResult(syntheticBitmap);
        PumpDispatcherFrames();
        Check(staleImage.Source == null, "stale async icon callback cannot mutate old Image after mode switch");
        Check(quickPanel.Children.OfType<Button>().All(b => b.Content is TextBlock) && !iconDict.ContainsKey(jianyingCacheKey + "_late"),
            "TextOnly UI cannot insert icons from stale callback");

        // Also verify TextOnly guard even if LoadProfileIconAsync is invoked with matching revision in TextOnly mode.
        int currentRev = GetField<int>(win, "_profileIconRevision");
        var dummyPanel = new StackPanel();
        var dummyImage = new Image();
        dummyPanel.Children.Add(dummyImage);
        iconDict[jianyingCacheKey] = syntheticBitmap;
        var loadTask = (Task)Invoke(win, "LoadProfileIconAsync", jianying, dummyImage, currentRev)!;
        PumpDispatcherFrames();
        Check(loadTask.IsCompletedSuccessfully && dummyImage.Source == null && dummyPanel.Children.OfType<TextBlock>().Count() == 0,
            "LoadProfileIconAsync refuses to populate Image or fallback glyph while in TextOnly mode");

        // Switch to IconOnly and verify equal sizes, accessible metadata, Tooltip, and selection mark.
        displayCombo.SelectedItem = GetField<ComboBoxItem>(win, "ProfileDisplayModeIconOnlyItem")!;
        Check(testConfig.ProfileQuickSwitchDisplayMode == ProfileQuickSwitchDisplayModes.IconOnly, "combo changes config to IconOnly");
        Check(ReferenceEquals(GetField<WheelProfile>(win, "_selectedProfile"), jianying), "IconOnly change retains selected profile");
        var iconOnlyButtons = quickPanel.Children.OfType<Button>().ToList();
        Check(iconOnlyButtons.Count == testConfig.Profiles.Count, "IconOnly renders button for each profile");
        foreach (var b in iconOnlyButtons)
        {
            var prof = (WheelProfile)b.Tag;
            Check(b.Width == 36 && b.Height == 34, "IconOnly button has uniform dimensions");
            Check((string?)b.ToolTip == prof.DisplayTitle && AutomationProperties.GetName(b) == prof.DisplayTitle,
                "IconOnly button exposes full DisplayTitle via ToolTip and AutomationProperties.Name");
            bool isSel = ReferenceEquals(prof, jianying);
            Check(AutomationProperties.GetItemStatus(b) == (isSel ? "Selected" : "Unselected"),
                "IconOnly exposes accessible selection status");
            var gridContent = (Grid)b.Content;
            int markCount = gridContent.Children.OfType<TextBlock>().Count(t => t.Text == "✓");
            Check(markCount == (isSel ? 1 : 0), "IconOnly displays checkmark only on selected button");
        }
        // Switch back to IconAndText and verify horizontal mouse-wheel scrolling across overflowing quick-switch profiles.
        displayCombo.SelectedItem = GetField<ComboBoxItem>(win, "ProfileDisplayModeIconAndTextItem")!;
        var extraProfiles = Enumerable.Range(1, 5).Select(i => Profile($"ExtraOverflowProfile{i}", context: true, app: $"extra{i}.exe")).ToList();
        testConfig.Profiles.AddRange(extraProfiles);
        Invoke(win, "RefreshProfilePresentation");
        MeasureAndArrange(grid, 760);
        shortcuts.UpdateLayout();
        var renderedQuickButtons = quickPanel.Children.OfType<Button>().ToList();
        Check(renderedQuickButtons.Count == testConfig.Profiles.Count && renderedQuickButtons[^1].Margin.Right == 0,
            "last quick-switch button omits trailing right margin");
        Check(shortcuts.ScrollableWidth > 0 && shortcuts.HorizontalOffset == 0,
            $"overflowing quick-switch list is horizontally scrollable (scrollable={shortcuts.ScrollableWidth:F1})");
        Check((bool)Invoke(win, "ScrollProfileQuickSwitchByWheelDelta", -120)!, "wheel down scrolls quick-switch list right");
        shortcuts.UpdateLayout();
        double scrolledRightOffset = shortcuts.HorizontalOffset;
        Check(scrolledRightOffset > 0, $"wheel down moves HorizontalOffset right ({scrolledRightOffset:F1} > 0)");
        Check((bool)Invoke(win, "ScrollProfileQuickSwitchByWheelDelta", 120)!, "wheel up scrolls quick-switch list left");
        shortcuts.UpdateLayout();
        Check(shortcuts.HorizontalOffset == 0, "wheel up returns HorizontalOffset back to 0");
        var wheelArgs = new MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, -120)
        {
            RoutedEvent = UIElement.PreviewMouseWheelEvent,
            Source = shortcuts
        };
        Invoke(win, "ProfileQuickSwitchScrollViewer_PreviewMouseWheel", shortcuts, wheelArgs);
        shortcuts.UpdateLayout();
        Check(wheelArgs.Handled && shortcuts.HorizontalOffset > 0,
            "PreviewMouseWheel handler marks event handled and scrolls quick-switch bar right");

        // Restore original 4 profiles and zero scroll offset before badge & recording checks.
        shortcuts.ScrollToHorizontalOffset(0);
        testConfig.Profiles.RemoveAll(p => extraProfiles.Contains(p));
        Invoke(win, "RefreshProfilePresentation");
        MeasureAndArrange(grid, 760);

        // 3. Layer-trigger badge & recording state machine across Simple/Pro, List/Canvas, layers, profiles, 4 languages, and reload.
        var badgeText = GetField<TextBlock>(win, "ProfileLayerKeyBadgeText")!;
        var recordBtn = GetField<Button>(win, "ProfileModeRecordButton")!;
        var resetBtn = GetField<Button>(win, "ProfileModeResetButton")!;

        // Simple mode + Canvas: compact badge & record entry visible and Unset.
        win.ApplyConfigMode("Simple", save: false);
        Check(proPanel.Visibility == Visibility.Collapsed, "Simple mode hides top-right duplicate ProfileActivationPanel");
        Check(badgeText.Text == I18n.T("LayerActivationUnset"), "Simple mode initial badge shows Unset text");
        Check((string)recordBtn.Content == I18n.T("ProfileActivationRecordShort"), "Simple mode Unset button shows Record text");
        Check(resetBtn.Visibility == Visibility.Collapsed, "Unset layer collapses reset button");

        // Start recording -> Esc cancel keeps Unset.
        Invoke(win, "RecordProfileActivationButton_Click", recordBtn, new RoutedEventArgs());
        Check(badgeText.Text == I18n.T("LayerActivationRecordingBadge"), "Recording state shows Recording badge");
        Check((string)recordBtn.Content == I18n.T("ProfileActivationCancel"), "Recording state button shows Cancel recording");
        Invoke(win, "ProcessRawKeyEvent", new GlobalKeyEventArgs(27, ModifierKeys.None));
        Check(jianying.Layers[0].ActivationTrigger == null && badgeText.Text == I18n.T("LayerActivationUnset"),
            "Esc cancel leaves trigger unset and restores Unset badge");

        // Record Ctrl+B -> persists trigger and immediately shows Bound badge + Rerecord button + visible Reset.
        Invoke(win, "RecordProfileActivationButton_Click", recordBtn, new RoutedEventArgs());
        Invoke(win, "ProcessRawKeyEvent", new GlobalKeyEventArgs(66, ModifierKeys.Control));
        Check(jianying.Layers[0].ActivationTrigger != null && jianying.Layers[0].ActivationTrigger!.VkCode == 66 && jianying.Layers[0].ActivationTrigger!.RequireCtrl,
            "recording commits Ctrl+B to layer trigger");
        string expectedBoundText = I18n.TF("LayerActivationBoundFmt", SettingsWindow.FormatTriggerDisplay(jianying.Layers[0].ActivationTrigger!));
        Check(badgeText.Text == expectedBoundText, "successful recording immediately refreshes badge to Bound text");
        Check((string)recordBtn.Content == I18n.T("ProfileActivationRerecord"), "successful recording switches button text to Rerecord");
        Check(resetBtn.Visibility == Visibility.Visible && resetBtn.IsEnabled, "Bound state enables and shows Reset button");

        // Re-record and cancel with Esc -> retains Ctrl+B.
        Invoke(win, "RecordProfileActivationButton_Click", recordBtn, new RoutedEventArgs());
        Check(badgeText.Text == I18n.T("LayerActivationRecordingBadge"), "re-recording enters Recording badge");
        Invoke(win, "ProcessRawKeyEvent", new GlobalKeyEventArgs(27, ModifierKeys.None));
        Check(jianying.Layers[0].ActivationTrigger?.VkCode == 66 && badgeText.Text == expectedBoundText &&
            (string)recordBtn.Content == I18n.T("ProfileActivationRerecord"),
            "canceling re-record preserves existing Ctrl+B binding and Bound badge");

        // Re-record and feed conflicting trigger (MiddleButton matches parallel QuickTools) -> stops recording & preserves Ctrl+B.
        Invoke(win, "RecordProfileActivationButton_Click", recordBtn, new RoutedEventArgs());
        Invoke(win, "CompleteProfileActivationRecording", Mouse("MiddleButton"));
        Check(GetField<WheelProfile>(win, "_recordingActivationProfile") == null, "conflict stops active recording");
        Check(jianying.Layers[0].ActivationTrigger?.VkCode == 66 && badgeText.Text == expectedBoundText,
            "conflict during re-record retains existing Ctrl+B binding");

        // Pro mode ViewMode toggle: List shows ProfileActivationPanel without breaking badge; Canvas collapses ProfileActivationPanel (no duplicate).
        testConfig.ConfigMode = "Pro";
        win.ApplyConfigMode("Pro", save: false);
        Check(proPanel.Visibility == Visibility.Collapsed, "Pro Canvas mode avoids duplicate ProfileActivationPanel");
        listRadio.IsChecked = true;
        canvasRadio.IsChecked = false;
        Invoke(win, "MappingsViewMode_Checked", listRadio, new RoutedEventArgs());
        Check(proPanel.Visibility == Visibility.Visible, "Pro List mode reveals ProfileActivationPanel");
        Check(proStatus.Text.Contains(expectedBoundText), "Pro List status text mirrors Bound badge");
        canvasRadio.IsChecked = true;
        listRadio.IsChecked = false;
        Invoke(win, "MappingsViewMode_Checked", canvasRadio, new RoutedEventArgs());
        Check(proPanel.Visibility == Visibility.Collapsed, "returning to Canvas collapses ProfileActivationPanel");

        // Layer switching & Profile switching keep badge accurate per target.
        profileCombo.SelectedItem = global;
        layerCombo.ItemsSource = global.Layers;
        layerCombo.SelectedIndex = 0;
        Check(badgeText.Text == I18n.T("LayerActivationUnset"), "switching to Global layer 0 shows Unset");
        Invoke(win, "RecordProfileActivationButton_Click", recordBtn, new RoutedEventArgs());
        Invoke(win, "ProcessRawKeyEvent", new GlobalKeyEventArgs(67, ModifierKeys.Control)); // Ctrl+C on Global layer 0
        string globalBound = I18n.TF("LayerActivationBoundFmt", SettingsWindow.FormatTriggerDisplay(global.Layers[0].ActivationTrigger!));
        Check(badgeText.Text == globalBound, "Global layer 0 shows bound Ctrl+C badge");
        layerCombo.SelectedIndex = 1;
        Check(badgeText.Text == I18n.T("LayerActivationUnset"), "switching to Global layer 1 updates badge to Unset");
        layerCombo.SelectedIndex = 0;
        Check(badgeText.Text == globalBound, "switching back to Global layer 0 restores Ctrl+C badge");

        // Imported conflict between two profiles renders ConflictOrDisabled badge text without mutating trigger.
        multiApp.Layers[0].ActivationTrigger = Mouse("MiddleButton"); // duplicates parallel QuickTools
        profileCombo.SelectedItem = multiApp;
        layerCombo.ItemsSource = multiApp.Layers;
        layerCombo.SelectedIndex = 0;
        Check(badgeText.Text == I18n.T("LayerActivationConflictBadge"), "imported conflict renders ConflictOrDisabled badge");

        // Language switching updates badge text across all 4 languages without losing trigger state.
        profileCombo.SelectedItem = jianying;
        layerCombo.ItemsSource = jianying.Layers;
        layerCombo.SelectedIndex = 0;
        foreach (string lang in new[] { "zh-CN", "zh-TW", "en-US", "ja-JP" })
        {
            I18n.SetLanguage(lang);
            testConfig.Language = lang;
            Invoke(win, "UpdateProfileBindingUi");
            string langExpected = I18n.TF("LayerActivationBoundFmt", SettingsWindow.FormatTriggerDisplay(jianying.Layers[0].ActivationTrigger!));
            Check(badgeText.Text == langExpected && (string)recordBtn.Content == I18n.T("ProfileActivationRerecord") &&
                  GetField<TextBlock>(win, "ProfileCurrentLayerKeyLabel")!.Text == I18n.T("ProfileCurrentLayerKeyLabel") &&
                  (string)GetField<Button>(win, "ProfileQuickAddProgramBtn")!.Content == I18n.T("ProfileQuickAddProgram") &&
                  (string)GetField<ComboBoxItem>(win, "ProfileDisplayModeTextOnlyItem")!.Content == I18n.T("ProfileDisplayModeTextOnly"),
                "4-language refresh updates badge, buttons, labels, and display mode menu for " + lang);
        }
        I18n.SetLanguage("zh-CN");
        testConfig.Language = "zh-CN";
        Invoke(win, "UpdateProfileBindingUi");

        // Save and reload preserves trigger and display mode, restoring identical badge state.
        string savedJson = JsonSerializer.Serialize(testConfig);
        Check(ConfigManager.ImportExternalConfigJson(savedJson, "UiStateRoundtrip", out _), "save UI config roundtrip");
        ConfigManager.LoadConfig();
        var reloadedJianying = ConfigManager.CurrentConfig.Profiles.Single(p => p.ProcessName == "JianyingPro");
        var reloadedPresentation = ProfilePresentation.GetLayerTriggerPresentation(ConfigManager.CurrentConfig, reloadedJianying, reloadedJianying.Layers[0], false);
        Check(reloadedPresentation.State == LayerTriggerBadgeState.Bound && reloadedPresentation.BadgeText == expectedBoundText,
            $"save and reload preserves layer trigger badge presentation (state={reloadedPresentation.State}, badge={reloadedPresentation.BadgeText}, expected={expectedBoundText})");

        // Reset button clears trigger and flips badge/button back to Unset/Record.
        Publish(testConfig);
        profileCombo.SelectedItem = jianying;
        Invoke(win, "ResetProfileActivationButton_Click", resetBtn, new RoutedEventArgs());
        Check(jianying.Layers[0].ActivationTrigger == null && badgeText.Text == I18n.T("LayerActivationUnset") &&
            (string)recordBtn.Content == I18n.T("ProfileActivationRecordShort") && resetBtn.Visibility == Visibility.Collapsed,
            "Reset button clears layer trigger and restores Unset state");
    }
}
