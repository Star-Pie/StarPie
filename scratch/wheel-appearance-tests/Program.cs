using System.Collections;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinPieGestures;

internal static class Program
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;

    [STAThread]
    private static void Main(string[] args)
    {
        if (!args.Contains("--test-instance")) throw new InvalidOperationException("--test-instance required");
        var folder = Directory.CreateTempSubdirectory("starpie-appearance-");
        foreach (string key in new[] { "APPDATA", "LOCALAPPDATA", "TEMP", "TMP" })
            Environment.SetEnvironmentVariable(key, folder.FullName);
        try { Run(); }
        finally { folder.Delete(true); }
    }

    private static void Run()
    {
        static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        string candidate = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().Single(x => x.Key == "CandidateDll").Value!;
        Check(Hash(typeof(SettingsWindow).Assembly.Location) == Hash(candidate), "exact candidate " + Hash(candidate));
        ConfigManager.DisableAutoStartSync = true;
        var cfg = ConfigManager.CurrentConfig;
        cfg.AutoCheckUpdate = false;
        cfg.EnableMultiTier = true;
        cfg.SubmenuStyle = "Wheel";
        cfg.SubWheelShape = null;
        cfg.Shape = "Original";
        cfg.WheelRadius = 133;
        cfg.InnerRadius = 70;
        cfg.SubWheelOuterRadius = 196;
        cfg.ShowCoreCircle = true;
        var profile = new WheelProfile { ProcessName = "Global", SectorCount = 8 };
        profile.EnsureLayers();
        var layer = profile.GetActiveLayer();
        layer.Actions[0] = new ActionItem { Type = "Text", Parameter = "sample", Name = "Sample" };
        layer.Actions[0].SubActions.Add(new ActionItem { Type = "Text", Parameter = "child", Name = "Child" });
        cfg.Profiles = [profile];

        var app = new App();
        app.InitializeComponent();
        using var disable = Dispatcher.CurrentDispatcher.DisableProcessing();
        var window = new SettingsWindow();
        var page = Find<Grid>("AppearanceSettingsGrid");
        page.Visibility = Visibility.Visible;
        Invoke(window, "RefreshWheelAppearance");
        var sidebar = Find<StackPanel>("WheelAppearanceFields");
        var stage = Find<WheelAppearanceCanvas>("AppearanceGeometryCanvas");
        var choices = Find<WrapPanel>("AppearancePreviewChoices");

        stage.Arrange(new Rect(0, 0, 460, 360));
        var bitmap = new RenderTargetBitmap(460, 360, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(stage);
        var pixels = new byte[460 * 360 * 4];
        bitmap.CopyPixels(pixels, 460 * 4, 0);
        Check(new WindowInteropHelper(window).Handle == IntPtr.Zero && pixels.Where((_, i) => i % 4 == 3).Count(x => x != 0) > 200,
            "offscreen renderer paints geometry without a native window");
        Check(CellCount(stage, 1, visible: true) == 8 && CellCount(stage, 2, visible: true) >= 24,
            "configuration stage shows occupied and vacant positions");

        long revision = ConfigManager.ConfigurationRevision;
        string before = JsonSerializer.Serialize(cfg);
        foreach (LanguageCode lang in new[] { LanguageCode.ZhCn, LanguageCode.ZhTw, LanguageCode.En, LanguageCode.Ja })
        {
            I18n.CurrentLanguage = lang;
            Invoke(window, "RefreshWheelAppearance");
            sidebar.Measure(new Size(300, double.PositiveInfinity));
            sidebar.Arrange(new Rect(0, 0, 300, sidebar.DesiredSize.Height));
            var depthButtons = Enumerable.Range(1, 5).Select(i => FindById<Button>(sidebar, $"WheelAppearanceDepth{i}")).ToArray();
            Check(depthButtons.All(b => b.ActualWidth > 0 && ReferenceEquals(b.Parent, depthButtons[0].Parent)) &&
                depthButtons.Select(b => LayoutInformation.GetLayoutSlot(b).Y).Distinct().Count() == 1 &&
                depthButtons.Select(b => LayoutInformation.GetLayoutSlot(b).X).Distinct().Count() == 5 &&
                Walk(sidebar).OfType<TextBlock>().Any(x => x.Text == I18n.T("WheelAppearanceTitle")),
                "localized sidebar laid out " + lang);
        }
        Click(FindById<Button>(choices, "WheelAppearancePopup"));
        Click(FindById<Button>(choices, "WheelAppearanceConfigure"));
        Click(FindById<Button>(sidebar, "WheelAppearanceCompare"));
        Click(FindById<Button>(sidebar, "WheelAppearanceReturn"));
        for (int depth = 1; depth <= 5; depth++)
        {
            var buttons = Enumerable.Range(1, 5).Select(i => FindById<Button>(sidebar, $"WheelAppearanceDepth{i}")).ToArray();
            Check(buttons.All(b => ReferenceEquals(b.Parent, buttons[0].Parent)) && buttons[0].Parent is UniformGrid { Columns: 5 },
                $"depth {depth}: all five choices share one row");
            Click(buttons[depth - 1]);
            Check((int)window.GetType().GetField("_appearanceDepth", Private)!.GetValue(window)! == depth,
                $"depth {depth}: direct selection updates preview");
        }
        Click(FindById<Button>(sidebar, "WheelAppearanceDepth2"));
        Check(ConfigManager.ConfigurationRevision == revision && JsonSerializer.Serialize(cfg) == before,
            "preview modes, depth, compare and localization preserve configuration and revision");

        Invoke(window, "SelectAppearanceLevel", 1);
        var primaryShape = FindById<ComboBox>(sidebar, "WheelAppearanceShapeChoice");
        ChooseShape(primaryShape, "Circle");
        Check(cfg.Shape == "Circle" && cfg.SubWheelShape == "Original",
            "first explicit primary edit preserves the legacy secondary outline");
        double separation = cfg.SubWheelOuterRadius - cfg.WheelRadius;
        var drag = (Action<double>?)stage.GetType().GetField("HandleChanged", Private)!.GetValue(stage);
        double oldRadius = cfg.WheelRadius;
        var thickness = FindById<Slider>(sidebar, "WheelAppearanceThicknessSlider");
        drag!(thickness.Value + 17);
        Check(cfg.WheelRadius == oldRadius + 17 && cfg.SubWheelOuterRadius - cfg.WheelRadius == separation,
            "size handle updates the sidebar and preserves outer sub-ring thickness");
        var innerRadius = FindById<Slider>(sidebar, "WheelAppearanceInnerSlider");
        thickness.Value = thickness.Minimum;
        innerRadius.Value = 100;
        Check(cfg.InnerRadius <= cfg.WheelRadius - 18 && innerRadius.Maximum == cfg.WheelRadius - 18,
            "reducing primary thickness updates the inner radius limit on the same sidebar");
        innerRadius.Value = 50;
        Check(thickness.Value == cfg.WheelRadius - cfg.InnerRadius && thickness.Maximum == 280 - cfg.InnerRadius,
            "editing the inner radius refreshes the existing thickness slider");

        Invoke(window, "SelectAppearanceLevel", 2);
        ChooseShape(FindById<ComboBox>(sidebar, "WheelAppearanceShapeChoice"), "HexagonHive");
        Check(cfg.SubWheelShape == "HexagonHive" && cfg.Shape == "Circle", "secondary shape applies independently");
        var secondaryRadius = FindById<Slider>(sidebar, "WheelAppearanceSecondarySizeSlider");
        var secondaryGap = FindById<Slider>(sidebar, "WheelAppearanceSecondaryGapSlider");
        secondaryRadius.Value = secondaryRadius.Minimum;
        secondaryGap.Value = 20;
        Check(cfg.SubWheelInnerGap <= cfg.SubWheelOuterRadius - cfg.WheelRadius - 3,
            "reducing secondary radius updates the gap limit on the same sidebar");

        Click(FindById<Button>(sidebar, "WheelAppearanceDepth4"));
        Invoke(window, "SelectAppearanceLevel", 3);
        var toggle = FindById<CheckBox>(sidebar, "WheelAppearanceTierOverride");
        toggle.IsChecked = true;
        toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var tier = FindById<Slider>(sidebar, "WheelAppearanceThicknessSlider");
        tier.Value = 51;
        Check(cfg.FixedOuterRingAppearance?.Level3?.Thickness == 51 && cfg.FixedOuterRingAppearance.Level4 == null,
            "explicit tier override affects only the selected tier");

        cfg.SubmenuStyle = "Fan";
        Invoke(window, "SelectAppearanceLevel", 0);
        var honeyShape = FindById<ComboBox>(sidebar, "WheelAppearanceShapeChoice");
        Check(honeyShape.Items.Count == 2, "fixed honeycomb exposes only circle and hexagon");
        ChooseShape(honeyShape, "HexagonHive");
        var latestHoneyShape = FindById<ComboBox>(sidebar, "WheelAppearanceShapeChoice");
        ChooseShape(latestHoneyShape, "Circle");
        Check(cfg.FixedHoneycombAppearance?.Shape == "Circle" && cfg.Shape == "Circle" && cfg.SubWheelShape == "HexagonHive",
            "honeycomb edits apply to one shared profile, independent of legacy shapes");
        Invoke(window, "SelectAppearanceLevel", 4);
        Check(Walk(sidebar).OfType<Slider>().Count() == 0, "fixed honeycomb does not offer independent tier geometry");
        Click(FindById<Button>(choices, "WheelAppearancePopup"));
        Check(CellCount(stage, 1, visible: true) == 1 && CellCount(stage, 3, visible: true) == 0,
            "popup preview hides vacant and unrevealed positions");
        Check(Find<TextBlock>("AppearancePreviewHint").Text == I18n.T("WheelAppearancePopupFixedHint"),
            "popup preview explains that deeper levels require a fixed ring");
        layer.Actions[1] = new ActionItem { Type = "Text", Parameter = " " };
        layer.Actions[2] = new ActionItem { Type = "Hotkey", Parameter = " " };
        layer.Actions[0].SubActions[0].Parameter = "";
        layer.Actions[0].Parameter = "";
        Invoke(window, "RefreshWheelAppearance");
        Check(CellCount(stage, 1, visible: true) == 1 &&
            FixedRingMenu.LowerConfiguredSlots(profile, 2, true).SetEquals([(1, 1)]),
            "whitespace text remains configured while empty text and whitespace hotkeys stay hidden");
        var legacyBranch = new ActionItem { Type = "Text", Parameter = "",
            SubActions = [new ActionItem { Type = "Text", Parameter = "",
                SubActions = [new ActionItem { Type = "Text", Parameter = " " }] }] };
        Check(FixedRingMenu.LowerConfiguredSlots([legacyBranch], 3, false).Count == 0,
            "a classic navigation branch does not point to an unavailable deeper whitespace leaf");

        layer.MaxMenuDepth = 5;
        var menu = layer.FixedRings = new FixedRingMenu { AlwaysVisible = false };
        menu.SetSlot(5, 0, new FixedRingSlot { ParentIndex = 27, Action = new ActionItem { Type = "Text", Parameter = "test" } });
        menu.SetSlot(5, 10, new FixedRingSlot { Action = new ActionItem { Type = "Text", Parameter = "other" } });
        foreach (string layout in new[] { "Wheel", "Fan" })
        {
            cfg.SubmenuStyle = layout;
            Invoke(window, "RefreshWheelAppearance");
            stage.Render(cfg, profile, 5, 5, true, 1, null);
            Check(CellCount(stage, 5, visible: true) == 0, layout + " popup waits for nearby pointer");
            Invoke(stage, "UpdateNearby", CellCenter(stage, 5, 0));
            Check(CellCount(stage, 5, visible: true) == 1 && CellCount(stage, 4, visible: true) == 0,
                layout + " popup shows only the local level-five slot across an empty fourth level");
            Invoke(stage, "UpdateNearby", new Point());
            Check(CellCount(stage, 5, visible: true) == 0, layout + " popup hides the slot on retreat");
            stage.Render(cfg, profile, 5, 5, false, 1, null);
            Check(CellCount(stage, 5, visible: true) == menu.GetCount(5), layout + " editor shows configured and vacant slots");
        }
        Check(new WindowInteropHelper(window).Handle == IntPtr.Zero &&
            !WinPieGestures.Plugins.PluginHost.IsInitialized, "offline test never opens a window or initializes the plugin host");

        Console.WriteLine($"{_checks} appearance checks passed.");
        return;
        T Find<T>(string id) where T : FrameworkElement => (T)window.FindName(id);
    }

    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        foreach (var item in LogicalTreeHelper.GetChildren(root))
            if (item is DependencyObject child)
                foreach (var element in Walk(child)) yield return element;
    }

    private static T FindById<T>(DependencyObject parent, string id) where T : DependencyObject =>
        Walk(parent).OfType<T>().First(e => AutomationProperties.GetAutomationId(e) == id);

    private static void ChooseShape(ComboBox combo, string tag) =>
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().Single(item => (string)item.Tag == tag);

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Invoke(object target, string method, params object[] values) =>
        target.GetType().GetMethod(method, Private)!.Invoke(target, values);

    private static int CellCount(WheelAppearanceCanvas stage, int level, bool visible)
    {
        var cells = (IEnumerable)stage.GetType().GetField("_cells", Private)!.GetValue(stage)!;
        var filter = stage.GetType().GetMethod("Visible", Private)!;
        return cells.Cast<object>().Count(cell => (int)cell.GetType().GetProperty("Level")!.GetValue(cell)! == level &&
            (!visible || (bool)filter.Invoke(stage, [cell])!));
    }

    private static Point CellCenter(WheelAppearanceCanvas stage, int level, int index)
    {
        var cells = (IEnumerable)stage.GetType().GetField("_cells", Private)!.GetValue(stage)!;
        var cell = cells.Cast<object>().Single(item => (int)item.GetType().GetProperty("Level")!.GetValue(item)! == level &&
            (int)item.GetType().GetProperty("Index")!.GetValue(item)! == index);
        return (Point)cell.GetType().GetProperty("Center")!.GetValue(cell)!;
    }

    private static void Check(bool ok, string label)
    {
        if (!ok) throw new InvalidOperationException(label);
        Console.WriteLine("PASS " + label);
        _checks++;
    }
}
