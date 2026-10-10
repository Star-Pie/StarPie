using System.IO;
using System.Windows;
using System.Windows.Controls;
using StarPie.Plugin;
using WinPieGestures;
using WinPieGestures.Plugins;

internal static class Program
{
    private static int _passed;
    [STAThread]
    private static int Main(string[] args)
    {
        if (!args.Contains("--test-instance")) return 2;
        string sandbox = Path.Combine(Path.GetTempPath(), "starpie-controls-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        Environment.SetEnvironmentVariable("LOCALAPPDATA", sandbox);
        PluginPaths.OverrideRootsForTesting(Path.Combine(sandbox, "installed"), Path.Combine(sandbox, "scan"));
        try
        {
            // Load the real host resources without Run/Startup: no hooks, tray, windows or actions.
            var application = new App();
            application.InitializeComponent();
            // Warm standard controls before loading the collectible test fixture.
            var warmup = new Button { Content = "WPF bootstrap" };
            warmup.Measure(new Size(120, 32));
            warmup.Arrange(new Rect(0, 0, 120, 32));
            warmup.ApplyTemplate();
            Check(new ActionDescriptor().ShowInActionPicker, "legacy SDK descriptors remain visible by default");
            Check(new SettingsPageDescriptor().ActionIds.Count == 0, "legacy pages have no commands by default");
            Check(PluginApi.ApiVersion == "1.11" && PluginApi.ApiVersionMinor == 11, "SDK version represents minor eleven");
            Check((int)ParameterFieldType.Number == 2 && (int)ParameterFieldType.KeyMap == 9 && (int)ParameterFieldType.Slider == 10, "slider is additive without renumbering existing fields");
            TestRegistration();
            TestLiveLoadAndCommands(sandbox);
            RenderPetSettingsIfRequested();
            Console.WriteLine($"PASS: {_passed} contract assertions; no windows shown or actions invoked.");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine("FAIL: " + ex); return 1; }
        finally { PluginHost.ShutdownAll(); }
    }

    private static void TestRegistration()
    {
        var catalog = new PluginCatalog();
        var session = catalog.BeginSession("user.orderfixture");
        var settings = new PluginSettings(Path.Combine(PluginPaths.Root, "order", "settings.json"));
        var commands = new List<string> { "manage" };
        var registry = new PluginSettingsPageRegistry(session, "user.orderfixture", settings);
        registry.Register(new SettingsPageDescriptor { ActionIds = commands });
        commands[0] = "changedAfterRegistration";
        session.StageAction(new PluginActionRegistration { PluginId = "user.orderfixture", ShortId = "manage", FullId = "user.orderfixture.manage" });
        Check(session.Commit(out _), "page may be declared before action");
        Check(catalog.TryGetSettingsPage("user.orderfixture")!.ActionIds.SequenceEqual(new[] { "manage" }), "command list is an owned immutable snapshot");
        foreach (var ids in new[] { new[] { "other.manage" }, new[] { "manage", "MANAGE" }, new[] { "" } })
        {
            var invalid = catalog.BeginSession("user.invalid");
            bool rejected = false;
            try { new PluginSettingsPageRegistry(invalid, "user.invalid", settings).Register(new SettingsPageDescriptor { ActionIds = ids }); }
            catch (PluginContractException) { rejected = true; }
            Check(rejected, "invalid, duplicate or cross-plugin command is rejected");
        }
        var unknown = catalog.BeginSession("user.unknown");
        new PluginSettingsPageRegistry(unknown, "user.unknown", settings).Register(new SettingsPageDescriptor { ActionIds = new[] { "missing" } });
        unknown.StageAction(new PluginActionRegistration { FullId = "user.unknown.present", PluginId = "user.unknown", ShortId = "present" });
        Check(!unknown.Commit(out _) && !catalog.TryGetAction("user.unknown.present", out _), "missing command rejects the whole registration transaction");
    }

    private static void TestLiveLoadAndCommands(string sandbox)
    {
        string fixture = Environment.GetEnvironmentVariable("STARPIE_CONTROL_FIXTURE_DLL") ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../Fixture/bin/Release/net8.0-windows/ControlFixture.dll"));
        Check(File.Exists(fixture), "test-only SDK fixture built");
        PluginHost.HeadlessMode = true;
        PluginHost.Initialize();
        Check(Path.GetFullPath(PluginPaths.Root).StartsWith(Path.GetFullPath(sandbox) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "all plugin data stays inside pinned sandbox");
        var scan = PluginScanner.ScanSelectedDll(fixture);
        Check(scan.Accepted, "SDK-only fixture accepted by scanner");
        var install = PluginHost.CommitInstall(scan, new PluginInstallOptions { Acknowledged = true, EnableAfterInstall = true });
        Check(install.Success && install.Enabled, "fixture follows actual install/load/initialize/commit/invocation-gate lifecycle: " + install.Error);
        const string id = "user.controlfixture";
        var ui = ExerciseLoadedControls();
        var page = ui.Page;
        var stop = PluginHost.DisableAsync(id, PluginStopReason.UserDisabled, TimeSpan.FromSeconds(12)).GetAwaiter().GetResult();
        Console.WriteLine("STOP EVIDENCE: " + System.Text.Json.JsonSerializer.Serialize(stop));
        GC.KeepAlive(ui.Panel);
        Check(stop.IsFullyStopped && PluginSettingsPageService.Open(id) == null, "stop revokes command-only settings page");
        Check(PluginSettingsPageService.CreateCommand(id, "manage", page.Generation) == null, "disabled command is rejected");
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("STARPIE_CONTROL_PROBE_MODE"))) return;
        Check(PluginHost.Enable(id, out _), "fixture can reload through actual lifecycle");
        var latest = PluginSettingsPageService.Open(id)!;
        page.Target.Write("stale", "must-not-be-saved");
        PluginSettingsPageService.Persist(page);
        Check(PluginSettingsPageService.CreateCommand(id, "manage", page.Generation) == null && latest.Generation != page.Generation, "reloaded generation rejects old settings controls");
        string settingsFile = Path.Combine(PluginPaths.Root, id, "settings.json");
        Check(!File.Exists(settingsFile) || !File.ReadAllText(settingsFile).Contains("must-not-be-saved"), "closing stale page does not persist its old target");
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (PluginSettingsPageService.Page Page, StackPanel Panel) ExerciseLoadedControls()
    {
        const string id = "user.controlfixture";
        string? mode = Environment.GetEnvironmentVariable("STARPIE_CONTROL_PROBE_MODE");
        if (mode == "minimal") return (new PluginSettingsPageService.Page { PluginId = id }, new StackPanel());
        var page = PluginSettingsPageService.Open(id)!;
        if (mode == "page") return (page, new StackPanel());
        if (mode == "picker") { PluginActionBinding.BuildPluginActionItems(); return (page, new StackPanel()); }
        if (mode == "bar")
        {
            var panel = new StackPanel();
            new PluginParameterForm(panel, () => null, () => {}).Build(Array.Empty<ParameterField>(), id);
            return (page, panel);
        }
        Check(PluginSettingsPageService.HasPage(id) && page != null && page.Fields.Count == 0, "command-only settings page is available");
        Check(page!.Commands.Single().ShortId == "manage", "hidden management command resolves in configuration");
        var choices = PluginActionBinding.BuildPluginActionItems().Where(i => i.PluginId == id).ToList();
        Check(choices.Count == 1 && choices[0].FullId == id + ".toggle", "new picker shows one toggle only");
        var legacy = PluginHost.CreateActionItem(id + ".legacy")!;
        legacy.ExtensionData = new Dictionary<string, string> { ["keep"] = "old-value" };
        Check(PluginActionBinding.ProjectSelectedAction(legacy) == id + ".legacy", "hidden persisted action remains valid");
        Check(PluginActionBinding.BuildPluginActionItems(legacy.PluginActionRef!.FullId).Any(i => i.FullId == id + ".legacy"), "selected legacy action remains visible when editing old config");
        var vm = new SubSlotViewModel { Action = legacy };
        Check(vm.PluginActionOptions!.Cast<PluginActionItem>().Any(i => i.FullId == id + ".legacy"), "secondary slot keeps selected legacy reference");
        var gesture = new GestureMappingViewModel(new GestureMapping { Action = legacy });
        Check(gesture.PluginActionOptions!.Cast<PluginActionItem>().Any(i => i.FullId == id + ".legacy"), "gesture row keeps selected legacy reference");
        var command = PluginSettingsPageService.CreateCommand(id, "manage", page.Generation);
        Check(command?.PluginActionRef?.FullId == id + ".manage", "current same-plugin settings command creates normal action envelope");
        Check(PluginSettingsPageService.CreateCommand(id, "legacy", page.Generation) == null, "undeclared action cannot be invoked as settings command");
        Check(PluginSettingsPageService.CreateCommand(id, "other.manage", page.Generation) == null, "command cannot refer to another plugin");
        Check(PluginSettingsPageService.CreateCommand(id, "manage", page.Generation - 1) == null, "old generation command is rejected");
        var hostPanel = new StackPanel();
        var form = new PluginParameterForm(hostPanel, () => new ActionItemParameterTarget(legacy), () => throw new Exception("initialization wrote configuration"));
        form.Build(Array.Empty<ParameterField>(), id);
        Check(!form.IsEmpty && hostPanel.Children.Count > 0, "configuration command bar renders even without action parameters");
        Check(legacy.ExtensionData["keep"] == "old-value", "rebuilding configuration preserves unknown persisted values");
        hostPanel.Children.Add(TestSectionsAndSliders(id));
        return (page, hostPanel);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static StackPanel TestSectionsAndSliders(string id)
    {
        PluginHost.Catalog.RemoveSettingsPage(id);
        var session = PluginHost.Catalog.BeginSession(id);
        var instance = PluginHost.Find(id)!;
        var keys = new List<string> { "animate", "speed" };
        var descriptor = new SettingsPageDescriptor
        {
            Title = "General settings", ActionIds = new[] { "manage" },
            Fields = new[]
            {
                new ParameterField { Key = "size", Label = "Size", Type = ParameterFieldType.Slider, Min = 48, Max = 360, Step = 1, DefaultValue = "140", Unit = "px" },
                new ParameterField { Key = "animate", Label = "Animation", Type = ParameterFieldType.Bool, DefaultValue = "true" },
                new ParameterField { Key = "speed", Label = "Speed", Type = ParameterFieldType.Slider, Min = 0.25, Max = 3, Step = 0.05, DefaultValue = "1", Unit = "×" },
            },
            Sections = new[] { new SettingsSectionDescriptor { Id = "animation", Title = "Animation settings", FieldKeys = keys } },
        };
        new PluginSettingsPageRegistry(session, id, instance.Settings).Register(descriptor);
        keys[0] = "size";
        Check(session.Commit(out _), "section declaration commits through catalog");
        var general = PluginSettingsPageService.Open(id)!;
        var animation = PluginSettingsPageService.Open(id, "animation")!;
        Check(general.Fields.Count == 1 && general.AllFields.Count == 3, "main settings exclude animation fields but keep full declaration");
        Check(animation.Fields.Count == 2 && animation.Fields.All(field => field.Key != "size"), "section uses nested key snapshot, not mutated plugin list");
        foreach (string saved in new[] { "140.5", "999", "NaN" })
        {
            instance.Settings.Set("size", saved);
            var restored = PluginSettingsPageService.Open(id)!;
            int restoredChanges = 0;
            var restoredPanel = new StackPanel();
            new PluginParameterForm(restoredPanel, () => restored.Target, () => restoredChanges++).Build(restored.Fields, id);
            Check(instance.Settings.Get("size") == saved && !restored.Target.Dirty && restoredChanges == 0, "off-grid or invalid saved slider value is not rewritten during build: " + saved);
            if (saved == "140.5")
                Check(Descendants(restoredPanel).OfType<Slider>().Single().Value == 140.5, "off-grid value is restored exactly until explicit interaction");
            else Check(PluginSettingsPageService.Validate(restored).Count > 0, "invalid saved slider value remains a visible validation issue");
        }
        instance.Settings.Set("size", "140");
        Check(general.Sections.Single().Id == "animation" && animation.Sections.Count == 0, "section navigation appears without recursive self-shortcut");
        Check(PluginSettingsPageService.Open(id, "unknown") == null, "unknown configuration section rejected");
        int changes = 0;
        var panel = new StackPanel();
        var form = new PluginParameterForm(panel, () => PluginSettingsPageService.IsCurrent(general) ? general.Target : null, () => changes++);
        form.Build(general.Fields, id);
        Check(changes == 0 && !general.Target.Dirty, "slider initialization does not write or mark dirty");
        var controls = Descendants(panel).ToArray();
        var slider = controls.OfType<Slider>().Single();
        Check(slider.Value == 140 && slider.SmallChange == 1, "slider restores numeric defaults and step");
        var plus = controls.OfType<Button>().Single(button => Equals(button.Content, "+"));
        panel.Measure(new Size(400, 500));
        panel.Arrange(new Rect(0, 0, 400, 500));
        panel.UpdateLayout();
        var plusPresenter = Descendants(plus).OfType<ContentPresenter>().Single();
        Check(plusPresenter.ActualWidth >= 8, "compact plus glyph has enough space with the actual host button style");
        plus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(slider.Value == 141 && instance.Settings.Get("size") == "141" && changes == 1, "plus button writes one invariant value");
        slider.Value = slider.Maximum;
        Check(!plus.IsEnabled, "plus disabled at upper bound");
        var minus = controls.OfType<Button>().Single(button => Equals(button.Content, "−"));
        slider.Value = slider.Minimum;
        Check(!minus.IsEnabled, "minus disabled at lower bound");
        var speed = descriptor.Fields.Single(field => field.Key == "speed");
        foreach (string invalid in new[] { "NaN", "Infinity", "-Infinity", "4", "0.1" })
            Check(PluginParameterValidator.Validate(new[] { speed }, new Dictionary<string, string> { ["speed"] = invalid }).Count > 0, "slider rejects invalid/out-of-range value: " + invalid);
        var tinyTarget = new ActionItem { ExtensionData = new Dictionary<string, string> { ["tiny"] = "1E-12" } };
        var tinyPanel = new StackPanel();
        var tinyForm = new PluginParameterForm(tinyPanel, () => new ActionItemParameterTarget(tinyTarget), () => { });
        tinyForm.Build(new[] { new ParameterField { Key = "tiny", Type = ParameterFieldType.Slider, Min = 0, Max = 1E-10, Step = 1E-12 } }, null);
        Descendants(tinyPanel).OfType<Button>().Single(button => Equals(button.Content, "+")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(double.Parse(tinyTarget.ExtensionData["tiny"], System.Globalization.CultureInfo.InvariantCulture) == 2E-12, "tiny finite slider steps and persistence do not round to zero");
        var sectionPanel = new StackPanel();
        new PluginParameterForm(sectionPanel, () => animation.Target, () => changes++, false).Build(animation.Fields, id, "animation");
        Check(Descendants(sectionPanel).OfType<Slider>().Count() == 1 && !Descendants(sectionPanel).OfType<Button>().Any(button => Equals(button.Content, "Animation settings")), "animation form contains speed without another animation shortcut");
        foreach (var invalid in new[]
        {
            new[] { new SettingsSectionDescriptor { Id = "x", FieldKeys = new[] { "missing" } } },
            new[] { new SettingsSectionDescriptor { Id = "x", FieldKeys = new[] { "size", "size" } } },
            new[] { new SettingsSectionDescriptor { Id = "x", FieldKeys = new[] { "size" } }, new SettingsSectionDescriptor { Id = "x", FieldKeys = new[] { "speed" } } },
        })
        {
            var pending = PluginHost.Catalog.BeginSession("user.invalidsection");
            bool rejected = false;
            try { new PluginSettingsPageRegistry(pending, "user.invalidsection", instance.Settings).Register(new SettingsPageDescriptor { Fields = descriptor.Fields, Sections = invalid }); }
            catch (PluginContractException) { rejected = true; }
            Check(rejected, "unknown or duplicate section field/ID rejected");
        }
        return panel;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        yield return parent;
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Descendants(System.Windows.Media.VisualTreeHelper.GetChild(parent, i))) yield return child;
    }

    private static void RenderPetSettingsIfRequested()
    {
        string? folder = Environment.GetEnvironmentVariable("STARPIE_CONTROLS_PREVIEW");
        string? pluginDll = Environment.GetEnvironmentVariable("STARPIE_CONTROLS_PET_DLL");
        if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(pluginDll)) return;
        // Explicit offline preview only: isolated plugin roots, registration, no Execute or Show.
        var scan = PluginScanner.ScanSelectedDll(pluginDll);
        Check(scan.Accepted, "preview candidate accepted by paired SDK scanner");
        Check(scan.Manifest?.Id == "io.github.softblack42.desktoppet" && scan.Manifest.Author == "SoftBlack42" && scan.Manifest.Version == "0.1.5", "bare DLL scanner resolves the renamed identity, author and version");
        var install = PluginHost.CommitInstall(scan, new PluginInstallOptions { Acknowledged = true, EnableAfterInstall = true });
        Check(install.Success && install.Enabled, "preview candidate initialized inside sandbox: " + install.Error);
        Directory.CreateDirectory(folder);
        foreach (string? section in new string?[] { null, "animation" })
        {
            var window = new PluginSettingsPageWindow("io.github.softblack42.desktoppet", section);
            var content = (FrameworkElement)window.Content;
            var size = new Size(700, 640);
            content.Measure(size);
            content.Arrange(new Rect(size));
            content.UpdateLayout();
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(700, 640, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            var backdrop = new System.Windows.Media.DrawingVisual();
            using (var drawing = backdrop.RenderOpen()) drawing.DrawRectangle(window.Background, null, new Rect(size));
            bitmap.Render(backdrop);
            bitmap.Render(content);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(folder, section == null ? "pet-settings.png" : "animation-settings.png"));
            encoder.Save(output);
            // Unshown window, no edited target: closing performs no settings write.
            window.Close();
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _passed++;
        Console.WriteLine(" [PASS] " + message);
    }
}
