using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using WinPieGestures;
using WinPieGestures.ArtStyles;

static class Program
{
    static int checks;

    static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception("CHECK FAILED: " + label);
        checks++;
    }

    static readonly BindingFlags AllInstance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    static void SetField(object target, string name, object? value) =>
        target.GetType().GetField(name, AllInstance)?.SetValue(target, value);

    static T? GetField<T>(object target, string name) =>
        (T?)target.GetType().GetField(name, AllInstance)?.GetValue(target);

    static object? Invoke(object target, string name, params object?[] args) =>
        target.GetType().GetMethod(name, AllInstance)!.Invoke(target, args);

    static object? InvokeWithOutString(object target, string name, string arg0, out string outArg1)
    {
        var method = target.GetType().GetMethod(name, AllInstance)!;
        object?[] callArgs = { arg0, "" };
        object? ret = method.Invoke(target, callArgs);
        outArg1 = (string)(callArgs[1] ?? "");
        return ret;
    }

    static TriggerConfig Mouse(string button, bool ctrl = false) =>
        new() { TriggerType = "Mouse", MouseButton = button, RequireCtrl = ctrl };

    [STAThread]
    static int Main(string[] args)
    {
        string isolated = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "artifacts", "data-" + Guid.NewGuid().ToString("N")));
        Environment.SetEnvironmentVariable("LOCALAPPDATA", isolated);
        Environment.SetEnvironmentVariable("APPDATA", isolated);
        Directory.CreateDirectory(isolated);
        try
        {
            RunTests(args, isolated);
            Console.WriteLine($"ALL STAGE1 UI CHECKS PASSED ({checks} assertions)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void RunTests(string[] args, string isolated)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        ConfigManager.DisableAutoStartSync = true;

        string candidate = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(x => x.Key == "StarPieCandidateDir").Value!;
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        Check(Hash(Path.Combine(candidate, "StarPie.dll")) == Hash(typeof(WheelProfile).Assembly.Location), "candidate StarPie.dll SHA-256 matches");
        Console.WriteLine($"CANDIDATE={candidate}; SHA256={Hash(typeof(WheelProfile).Assembly.Location)}");

        string xamlPath = ResolveSettingsXamlPath(args);
        Console.WriteLine($"XAML={xamlPath}");

        var (win, rootGrid) = BuildHeadlessSettingsWindow(xamlPath);
        PumpDispatcherFrames();

        VerifyDimensionSliderBoundsAndRoundtrip(win, isolated);
        VerifyPreviewTextFormattingAndUiScale(win);
        VerifyMappingsOuterSubRingTextAndLayouts(win, isolated);
        VerifyDeleteTimersSyncAndSetAppTheme(win, isolated);
    }

    static string ResolveSettingsXamlPath(string[] args)
    {
        if (args.Length > 0 && File.Exists(args[0]))
            return Path.GetFullPath(args[0]);
        string? cur = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(cur))
        {
            string probe = Path.Combine(cur, "WinPieGestures", "SettingsWindow.xaml");
            if (File.Exists(probe)) return probe;
            cur = Directory.GetParent(cur)?.FullName;
        }
        throw new FileNotFoundException("Could not locate WinPieGestures/SettingsWindow.xaml");
    }

    static readonly HashSet<string> RoutedEventAttrNames = new(StringComparer.Ordinal)
    {
        "Click", "Checked", "Unchecked", "ValueChanged", "ValuesChanged", "SelectionChanged", "TextChanged",
        "MouseWheel", "PreviewMouseWheel", "MouseDown", "PreviewMouseDown", "MouseMove", "PreviewMouseMove",
        "MouseUp", "PreviewMouseUp", "MouseLeftButtonDown", "MouseLeftButtonUp", "MouseRightButtonDown",
        "MouseEnter", "MouseLeave", "KeyDown", "PreviewKeyDown", "PreviewTextInput", "LostFocus", "GotFocus",
        "DropDownOpened", "IsVisibleChanged", "Loaded", "SizeChanged", "Closing", "DragOver", "Drop",
        "RequestBringIntoView", "ContextMenuOpening"
    };

    static (SettingsWindow Win, Grid RootGrid) BuildHeadlessSettingsWindow(string source)
    {
        try { Application.ResourceAssembly = typeof(SettingsWindow).Assembly; } catch { }
        if (Application.Current == null)
        {
            new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        }

        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        string AddAssembly(string xml) => System.Text.RegularExpressions.Regex.Replace(
            xml,
            @"clr-namespace:(WinPieGestures(?:\.[A-Za-z0-9_.]+)?)(?=[""'])",
            "clr-namespace:$1;assembly=StarPie");

        var document = XDocument.Parse(AddAssembly(File.ReadAllText(source)));
        var appDoc = XDocument.Parse(AddAssembly(File.ReadAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(source)!, "App.xaml"))));
        var appResDictEl = new XElement(appDoc.Root!.Elements().First().Elements().Single());
        var winResDictEl = new XElement(document.Root!.Elements().First().Elements().Single());

        var resources = new XElement(winResDictEl);
        resources.AddFirst(new XElement(wpf + "ResourceDictionary.MergedDictionaries", appResDictEl));

        var rootScaleGridEl = new XElement(document.Descendants().Single(e => (string?)e.Attribute("Name") == "RootScaleGrid"));
        var hostRoot = new XElement(wpf + "Grid",
            document.Root!.Attributes().Where(a => a.IsNamespaceDeclaration),
            new XElement(wpf + "Grid.Resources", resources),
            rootScaleGridEl);

        var handlerNames = typeof(SettingsWindow).GetMethods(AllInstance)
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var attribute in hostRoot.DescendantsAndSelf().Attributes().ToArray())
        {
            if (!attribute.IsNamespaceDeclaration &&
                attribute.Name.LocalName != "Name" &&
                (RoutedEventAttrNames.Contains(attribute.Name.LocalName) || handlerNames.Contains(attribute.Value)))
            {
                attribute.Remove();
            }
        }
        foreach (var setter in hostRoot.Descendants(wpf + "EventSetter").ToArray())
            setter.Remove();

        // Populate Application.Current.Resources from App.xaml so Window-level style lookups & TryFindResource work.
        var appOnlyHost = new XElement(wpf + "ResourceDictionary",
            appDoc.Root!.Attributes().Where(a => a.IsNamespaceDeclaration),
            appResDictEl.Attributes(),
            appResDictEl.Elements());
        var appDict = (ResourceDictionary)XamlReader.Parse(appOnlyHost.ToString());
        Application.Current!.Resources.MergedDictionaries.Add(appDict);

        // Verify App.xaml actually contains implicit Display style for TextBlock & TextBox.
        var appTextBlockStyle = Application.Current.TryFindResource(typeof(TextBlock)) as Style;
        var appTextBoxStyle = Application.Current.TryFindResource(typeof(TextBox)) as Style;
        Check(appTextBlockStyle != null && appTextBlockStyle.Setters.OfType<Setter>().Any(s =>
            s.Property == TextOptions.TextFormattingModeProperty && Equals(s.Value, TextFormattingMode.Display)),
            "App.xaml implicit TextBlock style sets TextFormattingMode.Display");
        Check(appTextBoxStyle != null && appTextBoxStyle.Setters.OfType<Setter>().Any(s =>
            s.Property == TextOptions.TextFormattingModeProperty && Equals(s.Value, TextFormattingMode.Display)),
            "App.xaml implicit TextBox style sets TextFormattingMode.Display");

        var grid = (Grid)XamlReader.Parse(hostRoot.ToString());
        AppThemeManager.ApplyTheme(grid, "Light");

        var win = (SettingsWindow)RuntimeHelpers.GetUninitializedObject(typeof(SettingsWindow));
        typeof(Window).GetConstructor(Type.EmptyTypes)!.Invoke(win, null);
        win.Resources = grid.Resources;
        foreach (var f in typeof(SettingsWindow).GetFields(AllInstance))
        {
            bool isCollectionOrInitOnly = f.IsInitOnly || (f.FieldType.IsGenericType && (
                f.FieldType.GetGenericTypeDefinition() == typeof(List<>) ||
                f.FieldType.GetGenericTypeDefinition() == typeof(Dictionary<,>) ||
                f.FieldType.GetGenericTypeDefinition() == typeof(HashSet<>) ||
                typeof(System.Collections.ICollection).IsAssignableFrom(f.FieldType)));
            if (isCollectionOrInitOnly && f.FieldType != typeof(SettingsWindow) && !f.FieldType.IsAbstract && f.GetValue(win) == null)
            {
                if (f.Name == "_profileIcons") f.SetValue(win, new Dictionary<string, BitmapSource?>(StringComparer.OrdinalIgnoreCase));
                else if (f.Name == "_profileIconLoads") f.SetValue(win, new Dictionary<string, Task<BitmapSource?>>(StringComparer.OrdinalIgnoreCase));
                else if (f.FieldType.GetConstructor(Type.EmptyTypes) is ConstructorInfo ctor)
                    f.SetValue(win, ctor.Invoke(null));
            }
        }
        SetField(win, "_layoutResourceHost", grid);
        WireNamedElements(win, grid);

        var wheelSlider = GetField<Slider>(win, "WheelRadiusSlider")!;
        var innerSlider = GetField<Slider>(win, "InnerRadiusSlider")!;
        var coreSlider = GetField<Slider>(win, "CoreRadiusSlider")!;
        wheelSlider.ValueChanged += (s, e) => Invoke(win, "WheelRadiusSlider_ValueChanged", s!, e);
        innerSlider.ValueChanged += (s, e) => Invoke(win, "InnerRadiusSlider_ValueChanged", s!, e);
        coreSlider.ValueChanged += (s, e) => Invoke(win, "CoreRadiusSlider_ValueChanged", s!, e);

        ArtStyleService.Instance.Changed += (s, e) => Invoke(win, "ArtStyles_Changed", s, e);

        SetField(win, "_selectedLayoutSlotIndex", -1);
        SetField(win, "_isUiInitialized", true);
        return (win, grid);
    }

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
        // Also resolve Transform/x:Name objects stored in XAML properties such as RootScaleGrid.LayoutTransform
        if (root is FrameworkElement rootFe)
        {
            foreach (var f in typeof(SettingsWindow).GetFields(AllInstance))
            {
                if (f.GetValue(target) == null && !f.IsInitOnly && typeof(DependencyObject).IsAssignableFrom(f.FieldType))
                {
                    if (rootFe.FindName(f.Name) is object found && f.FieldType.IsInstanceOfType(found))
                        f.SetValue(target, found);
                }
            }
        }
    }

    static void MeasureAndArrange(FrameworkElement el, double width = 1000, double height = 800)
    {
        el.Measure(new Size(width, height));
        el.Arrange(new Rect(0, 0, width, height));
        el.UpdateLayout();
    }

    static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        foreach (object child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is DependencyObject dep)
            {
                foreach (var sub in FindDescendants<T>(dep)) yield return sub;
            }
        }
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            if (VisualTreeHelper.GetChild(root, i) is DependencyObject vdep)
            {
                foreach (var sub in FindDescendants<T>(vdep)) yield return sub;
            }
        }
    }

    static IEnumerable<T> DistinctDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var item in FindDescendants<T>(root))
        {
            if (seen.Add(item)) yield return item;
        }
    }

    static void PumpDispatcherFrames()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    static AppConfig MakeBaselineConfig()
    {
        var global = new WheelProfile
        {
            ProcessName = "Global",
            DisplayName = "全局默认",
            UseApplicationContext = true,
            SectorCount = 4
        };
        global.EnsureLayers();
        var subTarget = new ActionItem { Name = "父菜单A", Type = "Hotkey", Parameter = "" };
        subTarget.SubActions = new()
        {
            new ActionItem { Name = "复制文本", Type = "Hotkey", Parameter = "Ctrl+C", IconKey = "Copy" },
            new ActionItem { Name = "无图标粘贴", Type = "Hotkey", Parameter = "Ctrl+V", IconKey = "", CustomIconSvg = "" },
            new ActionItem { Name = "子仅文本", Type = "Hotkey", Parameter = "Ctrl+S", IconKey = "Save", LayoutMode = "TextOnly" },
            new ActionItem { Name = "第四个扩展槽位", Type = "Hotkey", Parameter = "Ctrl+Z", IconKey = "" }
        };
        global.Layers[0].Actions[0] = subTarget;
        global.Layers[0].Actions[1] = new ActionItem { Name = "保存文档", Type = "Hotkey", Parameter = "Ctrl+S", IconKey = "Save" };
        global.Layers[0].Actions[2] = new ActionItem { Name = "纯文本扇区", Type = "Hotkey", Parameter = "Ctrl+F", IconKey = "", CustomIconSvg = "" };
        global.Layers[0].Actions[3] = new ActionItem { Name = "打开桌面", Type = "Folder", Parameter = "Desktop", IconKey = "Folder" };
        global.SyncRootPropertiesFromActiveLayer();

        var cfg = new AppConfig
        {
            Language = "zh-CN",
            AppTheme = "Light",
            ConfigMode = "Pro",
            Trigger = Mouse("RightButton"),
            Profiles = new() { global }
        };
        return cfg;
    }

    static void VerifyDimensionSliderBoundsAndRoundtrip(SettingsWindow win, string isolated)
    {
        var wheelSlider = GetField<Slider>(win, "WheelRadiusSlider")!;
        var innerSlider = GetField<Slider>(win, "InnerRadiusSlider")!;
        var coreSlider = GetField<Slider>(win, "CoreRadiusSlider")!;
        var subOuterSlider = GetField<Slider>(win, "SubWheelOuterRadiusSlider")!;
        var sectorGapSlider = GetField<Slider>(win, "SectorGapSlider")!;
        var sectorCornerSlider = GetField<Slider>(win, "SectorCornerRadiusSlider")!;
        var sectorIconSlider = GetField<Slider>(win, "SectorIconSizeSlider")!;
        var sectorFontSlider = GetField<Slider>(win, "SectorFontSizeSlider")!;
        var subGapSlider = GetField<Slider>(win, "SubWheelInnerGapSlider")!;
        var subCornerSlider = GetField<Slider>(win, "SubWheelCornerRadiusSlider")!;
        var subIconSlider = GetField<Slider>(win, "SubWheelIconSizeSlider")!;
        var subFontSlider = GetField<Slider>(win, "SubWheelFontSizeSlider")!;
        var subTrigSlider = GetField<Slider>(win, "SubWheelTriggerDistanceSlider")!;

        // Simulate interactive shrink down to small radius then ResetDimensionsButton_Click.
        ConfigManager.CurrentConfig = MakeBaselineConfig();
        SetField(win, "_isUpdatingUi", true);
        Invoke(win, "LoadConfigToUi");
        SetField(win, "_isUpdatingUi", false);

        wheelSlider.Value = 82.0; // tightens inner/core max under old interactive drag
        Check(innerSlider.Maximum <= 64.0 + 0.01, "interactive shrink tightens InnerRadiusSlider max safely");

        // Now ResetDimensionsButton_Click -> must restore Inner=70 (must not clamp to 64!)
        Invoke(win, "ResetDimensionsButton_Click", new Button(), new RoutedEventArgs());
        Invoke(win, "ResetSubDimensionsButton_Click", new Button(), new RoutedEventArgs());
        Check(Math.Abs(wheelSlider.Value - 133.0) < 0.01, "Reset restores WheelRadius=133");
        Check(Math.Abs(innerSlider.Value - 70.0) < 0.01, "Reset restores InnerRadius=70 without clamping");
        Check(Math.Abs(coreSlider.Value - 36.0) < 0.01, "Reset restores CoreRadius=36");

        // Case A1: Reset -> load normal-large config (was truncated at Core<=Max=36 or Inner<=Max=70/115)
        var largeCfg = MakeBaselineConfig();
        largeCfg.WheelRadius = 240.0;
        largeCfg.InnerRadius = 150.0;
        largeCfg.CoreRadius = 100.0;
        largeCfg.SubWheelOuterRadius = 380.0;
        largeCfg.SectorGap = 10.0;
        largeCfg.SectorCornerRadius = 15.0;
        largeCfg.SectorIconSize = 34.0;
        largeCfg.SectorFontSize = 17.0;
        largeCfg.SubWheelInnerGap = 18.0;
        largeCfg.SubWheelCornerRadius = 19.0;
        largeCfg.SubWheelIconSize = 30.0;
        largeCfg.SubWheelFontSize = 15.0;
        largeCfg.SubWheelTriggerDistance = 240.0;

        ConfigManager.CurrentConfig = largeCfg;
        SetField(win, "_isUpdatingUi", true);
        Invoke(win, "LoadConfigToUi");
        SetField(win, "_isUpdatingUi", false);
        Check((bool)Invoke(win, "SyncUiToConfigAndSave", false)!, "SyncUiToConfigAndSave succeeds after large LoadConfigToUi");
        Check(largeCfg.WheelRadius == 240.0 && largeCfg.InnerRadius == 150.0 && largeCfg.CoreRadius == 100.0 &&
              largeCfg.SubWheelOuterRadius == 380.0 && largeCfg.SectorGap == 10.0 && largeCfg.SectorCornerRadius == 15.0 &&
              largeCfg.SectorIconSize == 34.0 && largeCfg.SectorFontSize == 17.0 && largeCfg.SubWheelInnerGap == 18.0 &&
              largeCfg.SubWheelCornerRadius == 19.0 && largeCfg.SubWheelIconSize == 30.0 && largeCfg.SubWheelFontSize == 15.0 &&
              largeCfg.SubWheelTriggerDistance == 240.0,
            "#235-1 Case A1: reset->load large config preserves exact primary & sub-wheel dimensions");

        // Case A2: Reset -> load XAML-exceeding config (greater than XAML Maximum defaults 280/220/140/480/etc.)
        Invoke(win, "ResetDimensionsButton_Click", new Button(), new RoutedEventArgs());
        Invoke(win, "ResetSubDimensionsButton_Click", new Button(), new RoutedEventArgs());

        var ultraCfg = MakeBaselineConfig();
        ultraCfg.WheelRadius = 340.0;
        ultraCfg.InnerRadius = 220.0;
        ultraCfg.CoreRadius = 160.0;
        ultraCfg.SubWheelOuterRadius = 560.0;
        ultraCfg.SectorGap = 18.0;
        ultraCfg.SectorCornerRadius = 22.0;
        ultraCfg.SectorIconSize = 44.0;
        ultraCfg.SectorFontSize = 22.0;
        ultraCfg.SubWheelInnerGap = 26.0;
        ultraCfg.SubWheelCornerRadius = 28.0;
        ultraCfg.SubWheelIconSize = 40.0;
        ultraCfg.SubWheelFontSize = 20.0;
        ultraCfg.SubWheelTriggerDistance = 320.0;

        ConfigManager.CurrentConfig = ultraCfg;
        SetField(win, "_isUpdatingUi", true);
        Invoke(win, "LoadConfigToUi");
        SetField(win, "_isUpdatingUi", false);
        Check((bool)Invoke(win, "SyncUiToConfigAndSave", false)!, "SyncUiToConfigAndSave succeeds after ultra-large LoadConfigToUi");
        Check(ultraCfg.WheelRadius == 340.0 && ultraCfg.InnerRadius == 220.0 && ultraCfg.CoreRadius == 160.0 &&
              ultraCfg.SubWheelOuterRadius == 560.0 && ultraCfg.SectorGap == 18.0 && ultraCfg.SectorCornerRadius == 22.0 &&
              ultraCfg.SectorIconSize == 44.0 && ultraCfg.SectorFontSize == 22.0 && ultraCfg.SubWheelInnerGap == 26.0 &&
              ultraCfg.SubWheelCornerRadius == 28.0 && ultraCfg.SubWheelIconSize == 40.0 && ultraCfg.SubWheelFontSize == 20.0 &&
              ultraCfg.SubWheelTriggerDistance == 320.0,
            "#235-1 Case A2: reset->load XAML-exceeding config preserves exact primary & sub-wheel dimensions");
        Check(wheelSlider.Maximum >= 340.0 && innerSlider.Maximum >= 220.0 && coreSlider.Maximum >= 160.0 && subOuterSlider.Maximum >= 560.0 &&
              sectorGapSlider.Maximum >= 18.0 && sectorCornerSlider.Maximum >= 22.0 && sectorIconSlider.Maximum >= 44.0 &&
              sectorFontSlider.Maximum >= 22.0 && subGapSlider.Maximum >= 26.0 && subCornerSlider.Maximum >= 28.0 &&
              subIconSlider.Maximum >= 40.0 && subFontSlider.Maximum >= 20.0 && subTrigSlider.Maximum >= 320.0,
            "#235-1 Slider Maximum bounds expanded to match XAML-exceeding imported values");

        // Capture serialized ultra config BEFORE interactive downward drag mutates ultraCfg.WheelRadius to 300
        string exportedUltra = JsonSerializer.Serialize(ultraCfg);

        // Interactive downward drag from 340 -> 300 must NOT shrink WheelRadiusSlider.Maximum below 340 nor drop InnerRadius
        wheelSlider.Value = 300.0;
        Check(wheelSlider.Maximum >= 340.0 && Math.Abs(innerSlider.Value - 220.0) < 0.01 && Math.Abs(coreSlider.Value - 160.0) < 0.01,
            "#235-1 Interactive drag from expanded value keeps outer Maximum and valid inner radii");

        // Case A3: Export/Import JSON roundtrip after Reset
        Invoke(win, "ResetDimensionsButton_Click", new Button(), new RoutedEventArgs());
        Check(ConfigManager.ImportExternalConfigJson(exportedUltra, "UltraRoundtrip", out string savedUltraName), "ImportExternalConfigJson ultra config succeeds");
        ConfigManager.LoadConfig();
        SetField(win, "_isUpdatingUi", true);
        Invoke(win, "LoadConfigToUi");
        SetField(win, "_isUpdatingUi", false);
        Invoke(win, "SyncUiToConfigAndSave", true);
        string reExported = ConfigManager.GetConfigJsonForExport(savedUltraName);
        var roundtripped = JsonSerializer.Deserialize<AppConfig>(reExported)!;
        Check(roundtripped.WheelRadius == 340.0 && roundtripped.InnerRadius == 220.0 && roundtripped.CoreRadius == 160.0 &&
              roundtripped.SubWheelOuterRadius == 560.0 && roundtripped.SubWheelTriggerDistance == 320.0 &&
              roundtripped.SectorIconSize == 44.0 && roundtripped.SubWheelFontSize == 20.0,
            $"#235-1 Case A3: Reset -> Import -> LoadConfigToUi -> SyncUiToConfigAndSave -> Export roundtrip exact match (got W={roundtripped.WheelRadius}, I={roundtripped.InnerRadius}, C={roundtripped.CoreRadius}, SW={roundtripped.SubWheelOuterRadius}, TD={roundtripped.SubWheelTriggerDistance}, SI={roundtripped.SectorIconSize}, SF={roundtripped.SubWheelFontSize})");
    }

    static void VerifyPreviewTextFormattingAndUiScale(SettingsWindow win)
    {
        var liveViewport = GetField<Grid>(win, "PreviewViewportContainer")!;
        var liveCanvas = GetField<Canvas>(win, "LiveWheelPreviewCanvas")!;
        var mapViewport = GetField<Grid>(win, "MappingsPreviewViewportContainer")!;
        var mapCanvas = GetField<Canvas>(win, "MappingsWheelPreviewCanvas")!;
        var appearanceGrid = GetField<Grid>(win, "AppearanceSettingsGrid")!;
        var mappingsGrid = GetField<Grid>(win, "MappingsSettingsGrid")!;
        var rootScaleGrid = GetField<Grid>(win, "RootScaleGrid")!;
        var rootTransform = GetField<ScaleTransform>(win, "RootUiScaleTransform")!;

        foreach (var dep in new DependencyObject[] { liveViewport, liveCanvas, mapViewport, mapCanvas })
        {
            Check(TextOptions.GetTextFormattingMode(dep) == TextFormattingMode.Ideal,
                $"#235-2 {((FrameworkElement)dep).Name} sets TextFormattingMode.Ideal");
            Check(TextOptions.GetTextRenderingMode(dep) == TextRenderingMode.ClearType,
                $"#235-2 {((FrameworkElement)dep).Name} sets TextRenderingMode.ClearType");
            Check(RenderOptions.GetClearTypeHint(dep) == ClearTypeHint.Enabled,
                $"#235-2 {((FrameworkElement)dep).Name} enables ClearTypeHint");
            Check(RenderOptions.GetBitmapScalingMode(dep) == BitmapScalingMode.HighQuality,
                $"#235-2 {((FrameworkElement)dep).Name} sets BitmapScalingMode.HighQuality");
        }
        Check(liveViewport.UseLayoutRounding && liveViewport.SnapsToDevicePixels &&
              mapViewport.UseLayoutRounding && mapViewport.SnapsToDevicePixels,
            "#235-2 viewport containers enable UseLayoutRounding + SnapsToDevicePixels");
        Check(!liveCanvas.UseLayoutRounding && !liveCanvas.SnapsToDevicePixels &&
              !mapCanvas.UseLayoutRounding && !mapCanvas.SnapsToDevicePixels,
            "#235-2 preview canvases disable layout rounding/pixel snapping so zoom scaling stays smooth");

        // Configure both primary & sub-actions with multi-select badges so all preview TextBlocks are generated
        var cfg = MakeBaselineConfig();
        cfg.IconLayoutMode = "Both";
        cfg.MappingsCanvasShowText = true;
        var profile = cfg.Profiles[0];
        ConfigManager.CurrentConfig = cfg;
        SetField(win, "_selectedProfile", profile);
        SetField(win, "_selectedSlotIndex", 0);
        SetField(win, "_selectedSubActionIndex", 0);
        var multiSlots = GetField<List<int>>(win, "_selectedMultiSlots")!;
        multiSlots.Clear();
        multiSlots.AddRange(new[] { 0, 1 });

        appearanceGrid.Visibility = Visibility.Visible;
        mappingsGrid.Visibility = Visibility.Visible;

        Invoke(win, "RenderLiveWheelPreview");
        Invoke(win, "RenderMappingsWheelPreview");

        // Apply layout so WPF style inheritance / setter precedence resolves on all preview TextBlocks!
        MeasureAndArrange(liveViewport, 600, 600);
        MeasureAndArrange(mapViewport, 600, 600);

        var liveTexts = DistinctDescendants<TextBlock>(liveCanvas).ToList();
        var mapTexts = DistinctDescendants<TextBlock>(mapCanvas).ToList();
        Check(liveTexts.Count >= 4, $"LiveWheelPreviewCanvas renders dynamic TextBlocks (count={liveTexts.Count})");
        Check(mapTexts.Count >= 6, $"MappingsWheelPreviewCanvas renders dynamic TextBlocks (count={mapTexts.Count})");
        Check(liveTexts.All(tb => TextOptions.GetTextFormattingMode(tb) == TextFormattingMode.Ideal),
            "#235-2 All rendered TextBlocks inside LiveWheelPreviewCanvas resolve TextFormattingMode.Ideal despite App.xaml Display style");
        Check(mapTexts.All(tb => TextOptions.GetTextFormattingMode(tb) == TextFormattingMode.Ideal),
            "#235-2 All rendered TextBlocks inside MappingsWheelPreviewCanvas resolve TextFormattingMode.Ideal despite App.xaml Display style");

        // Also test Scale != 1.0 switches RootScaleGrid to Ideal and back to Display at 1.0
        Invoke(win, "ApplySettingsUiScale", 1.25);
        Check(Math.Abs(rootTransform.ScaleX - 1.25) < 0.001 && Math.Abs(rootTransform.ScaleY - 1.25) < 0.001,
            "#232 ApplySettingsUiScale(1.25) updates RootUiScaleTransform");
        Check(TextOptions.GetTextFormattingMode(rootScaleGrid) == TextFormattingMode.Ideal,
            "#232 RootScaleGrid switches to TextFormattingMode.Ideal when scale != 1.0");

        Invoke(win, "ApplySettingsUiScale", 1.0);
        Check(TextOptions.GetTextFormattingMode(rootScaleGrid) == TextFormattingMode.Display,
            "#232 RootScaleGrid restores TextFormattingMode.Display at 100% scale");
    }

    static Point ElementCenter(FrameworkElement fe)
    {
        double left = Canvas.GetLeft(fe);
        double top = Canvas.GetTop(fe);
        if (double.IsNaN(left)) left = 0;
        if (double.IsNaN(top)) top = 0;
        double w = fe.ActualWidth > 0 ? fe.ActualWidth : fe.DesiredSize.Width;
        double h = fe.ActualHeight > 0 ? fe.ActualHeight : fe.DesiredSize.Height;
        return new Point(left + w / 2.0, top + h / 2.0);
    }

    static List<FrameworkElement> FindVisualsForSubPaths(Canvas mapCanvas, List<System.Windows.Shapes.Path> subPaths)
    {
        var contentCandidates = mapCanvas.Children.OfType<FrameworkElement>()
            .Where(fe => Panel.GetZIndex(fe) == 6 && fe is not System.Windows.Shapes.Ellipse)
            .ToList();
        var matched = new List<FrameworkElement>();
        foreach (var sp in subPaths)
        {
            var found = contentCandidates
                .Select(c => new { El = c, Center = ElementCenter(c) })
                .Where(x => sp.Data.FillContains(x.Center) || sp.Data.Bounds.Contains(x.Center))
                .OrderBy(x =>
                {
                    Rect b = sp.Data.Bounds;
                    double dx = x.Center.X - (b.X + b.Width / 2.0);
                    double dy = x.Center.Y - (b.Y + b.Height / 2.0);
                    return dx * dx + dy * dy;
                })
                .Select(x => x.El)
                .FirstOrDefault();
            if (found != null) matched.Add(found);
        }
        return matched;
    }

    static void VerifyMappingsOuterSubRingTextAndLayouts(SettingsWindow win, string isolated)
    {
        var mapViewport = GetField<Grid>(win, "MappingsPreviewViewportContainer")!;
        var mapCanvas = GetField<Canvas>(win, "MappingsWheelPreviewCanvas")!;
        var mappingsGrid = GetField<Grid>(win, "MappingsSettingsGrid")!;
        mappingsGrid.Visibility = Visibility.Visible;

        var cfg = MakeBaselineConfig();
        var profile = cfg.Profiles[0];
        cfg.SubmenuStyle = "Wheel";
        cfg.IconLayoutMode = "Both";
        cfg.MappingsCanvasShowText = false;
        ConfigManager.CurrentConfig = cfg;
        SetField(win, "_selectedProfile", profile);
        SetField(win, "_selectedSlotIndex", 0);
        SetField(win, "_selectedSubActionIndex", 0);
        GetField<List<int>>(win, "_selectedMultiSlots")!.Clear();

        // Prepare a custom SVG in custom icons dir to ensure custom: resolution also works in outer sub-ring
        string customDir = IconHelper.GetCustomIconsDirectory();
        Directory.CreateDirectory(customDir);
        File.WriteAllText(System.IO.Path.Combine(customDir, "star_test.svg"),
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\"><path d=\"M12 2L15 9H22L16 14L18 21L12 17L6 21L8 14L2 9H9Z\"/></svg>");
        IconHelper.InvalidateCustomIconCache();
        profile.Layers[0].Actions[0].SubActions![3].IconKey = "custom:star_test";
        profile.SyncRootPropertiesFromActiveLayer();

        // Case C1: ShowText=false, Layout=Both ->
        //   sub 0 ("复制文本", IconKey="Copy") => icon only, no text
        //   sub 1 ("无图标粘贴", IconKey="") => iconless fallback MUST render TextBlock!
        //   sub 2 ("子仅文本", IconKey="Save", LayoutMode="TextOnly") => TextBlock only, NO icon!
        //   sub 3 ("第四个扩展槽位", IconKey="custom:star_test") => custom SVG Path icon
        Invoke(win, "RenderMappingsWheelPreview");
        MeasureAndArrange(mapViewport, 600, 600);

        var subPaths = GetField<List<System.Windows.Shapes.Path>>(win, "_mappingsSubSectorPaths")!;
        Check(subPaths.Count == 4, $"Expected 4 sub-action Paths in Wheel outer ring (got {subPaths.Count})");
        var subVisuals = FindVisualsForSubPaths(mapCanvas, subPaths);
        Check(subVisuals.Count == 4, $"Expected 4 matched outer sub-sector visuals (got {subVisuals.Count})");

        var sub0Texts = DistinctDescendants<TextBlock>(subVisuals[0]).ToList();
        var sub0Paths = DistinctDescendants<System.Windows.Shapes.Path>(subVisuals[0]).ToList();
        Check(sub0Paths.Count == 1 && sub0Texts.Count == 0, "#235-3 C1 sub0 with icon renders icon only when ShowText=false");

        var sub1Texts = DistinctDescendants<TextBlock>(subVisuals[1]).ToList();
        var sub1Paths = DistinctDescendants<System.Windows.Shapes.Path>(subVisuals[1]).ToList();
        Check(sub1Texts.Count == 1 && sub1Paths.Count == 0 && sub1Texts[0].Text.Contains("无图标") &&
              TextOptions.GetTextFormattingMode(sub1Texts[0]) == TextFormattingMode.Ideal &&
              !sub1Texts[0].IsHitTestVisible,
            "#235-3 C1 sub1 iconless fallback renders centered Ideal TextBlock (not blank!)");

        var sub2Texts = DistinctDescendants<TextBlock>(subVisuals[2]).ToList();
        var sub2Paths = DistinctDescendants<System.Windows.Shapes.Path>(subVisuals[2]).ToList();
        Check(sub2Texts.Count == 1 && sub2Paths.Count == 0 && sub2Texts[0].Text.Contains("子仅文本"),
            "#235-3 C1 sub2 per-item TextOnly renders TextBlock and omits icon");

        var sub3Paths = DistinctDescendants<System.Windows.Shapes.Path>(subVisuals[3]).ToList();
        Check(sub3Paths.Count == 1, "#235-3 C1 sub3 resolves custom:star_test.svg in outer sub-ring");

        // Verify hit-testing still hits all 4 sub-sectors and text visuals do not block clicks
        var hitItems = GetField<List<CanvasHitItem>>(win, "_canvasHitItems")!;
        int subHitCount = 0;
        foreach (var item in hitItems)
        {
            if (item.IsSubAction)
            {
                subHitCount++;
                Rect b = item.Geometry.Bounds;
                Point center = new(b.X + b.Width / 2.0, b.Y + b.Height / 2.0);
                var hit = (CanvasHitItem?)Invoke(win, "HitTestCanvasPoint", center);
                Check(hit != null && hit.IsSubAction, "#235-3 outer sub-sector remains hit-testable");
            }
        }
        Check(subHitCount == 4, $"#235-3 HitTest list registers all 4 outer sub-sectors (got {subHitCount})");

        // Case C2: MappingsCanvasShowText = true -> sub0 shows BOTH icon + TextBlock
        cfg.MappingsCanvasShowText = true;
        Invoke(win, "RenderMappingsWheelPreview");
        MeasureAndArrange(mapViewport, 600, 600);
        subPaths = GetField<List<System.Windows.Shapes.Path>>(win, "_mappingsSubSectorPaths")!;
        subVisuals = FindVisualsForSubPaths(mapCanvas, subPaths);
        Check(subVisuals.Count == 4 &&
              DistinctDescendants<System.Windows.Shapes.Path>(subVisuals[0]).Any() &&
              DistinctDescendants<TextBlock>(subVisuals[0]).Any(),
            "#235-3 C2 MappingsCanvasShowText=true renders BOTH icon and label in sub0");

        // Case C3a: BatchLayoutTextOnlyBtn_Click -> sets per-slot LayoutMode="TextOnly" & renders primary labels without icons
        cfg.MappingsCanvasShowText = false;
        var profileCombo = GetField<ComboBox>(win, "MappingsProfileComboBox");
        if (profileCombo != null)
        {
            profileCombo.ItemsSource = cfg.Profiles;
            profileCombo.SelectedItem = profile;
        }
        var multiSlots = GetField<List<int>>(win, "_selectedMultiSlots")!;
        multiSlots.Clear();
        multiSlots.AddRange(new[] { 0, 1, 2, 3 });
        Invoke(win, "BatchLayoutTextOnlyBtn_Click", new Button(), new RoutedEventArgs());
        Check(profile.Layers[0].Actions.Take(4).All(a => a.LayoutMode == "TextOnly"),
            "BatchLayoutTextOnlyBtn_Click sets primary slot LayoutMode to TextOnly");

        // Case C3b: global IconLayoutMode == "TextOnly" (with primary restored to Inherit too) -> both primary & outer sub-ring render ONLY TextBlocks (even with icons)
        multiSlots.Clear();
        foreach (var a in profile.Actions) a.LayoutMode = "Inherit";
        foreach (var a in profile.Layers[0].Actions) a.LayoutMode = "Inherit";
        cfg.IconLayoutMode = "TextOnly";
        Invoke(win, "RenderMappingsWheelPreview");
        MeasureAndArrange(mapViewport, 600, 600);
        subPaths = GetField<List<System.Windows.Shapes.Path>>(win, "_mappingsSubSectorPaths")!;
        subVisuals = FindVisualsForSubPaths(mapCanvas, subPaths);
        Check(subVisuals.Count == 4 && subVisuals.All(c =>
            DistinctDescendants<TextBlock>(c).Any() && !DistinctDescendants<System.Windows.Shapes.Path>(c).Any()),
            "#235-3 C3b global IconLayoutMode=TextOnly renders crisp labels without icons on every outer sub-sector");

        var primaryPaths = GetField<List<System.Windows.Shapes.Path>>(win, "_mappingsSectorPaths")!;
        var primaryVisuals = FindVisualsForSubPaths(mapCanvas, primaryPaths);
        Check(primaryVisuals.Count == 4 && primaryVisuals.All(v =>
            DistinctDescendants<TextBlock>(v).Any(tb => !string.IsNullOrEmpty(tb.Text)) &&
            !DistinctDescendants<System.Windows.Shapes.Path>(v).Any()),
            "#235-3 C3b global IconLayoutMode=TextOnly renders primary sector text labels without icons");

        // Case C4: Fan SubmenuStyle with 4 SubActions under TextOnly -> overflow slot j>=3 uses Opacity 0.5 and label still visible
        cfg.SubmenuStyle = "Fan";
        Invoke(win, "RenderMappingsWheelPreview");
        MeasureAndArrange(mapViewport, 600, 600);
        var fanSubPaths = GetField<List<System.Windows.Shapes.Path>>(win, "_mappingsSubSectorPaths")!;
        Check(fanSubPaths.Count == 4 && fanSubPaths[0].Opacity == 1.0 && Math.Abs(fanSubPaths[3].Opacity - 0.5) < 0.01,
            "#235-3 C4 Fan style dims j>=3 overflow sub-sectors cleanly at 0.5 Opacity");
        var fanSubVisuals = FindVisualsForSubPaths(mapCanvas, fanSubPaths);
        Check(fanSubVisuals.Count == 4 && fanSubVisuals.All(c => DistinctDescendants<TextBlock>(c).Any()),
            "#235-3 C4 Fan style TextOnly renders labels for all 4 sub-sectors including overflow slot");
    }

    static void VerifyDeleteTimersSyncAndSetAppTheme(SettingsWindow win, string isolated)
    {
        var appearanceGrid = GetField<Grid>(win, "AppearanceSettingsGrid")!;
        var liveCanvas = GetField<Canvas>(win, "LiveWheelPreviewCanvas")!;

        // D1: DeleteSavedConfigProfileCore stops _autoSaveDebounceTimer before deleting so 400ms timer cannot recreate file
        var cfgAlpha = MakeBaselineConfig();
        cfgAlpha.WheelRadius = 144.0;
        Check(ConfigManager.ImportExternalConfigJson(JsonSerializer.Serialize(cfgAlpha), "Alpha", out _), "seed Alpha saved config");
        var cfgBeta = MakeBaselineConfig();
        cfgBeta.WheelRadius = 177.0;
        Check(ConfigManager.ImportExternalConfigJson(JsonSerializer.Serialize(cfgBeta), "Beta", out _), "seed Beta saved config");
        ConfigManager.LoadConfig();

        SetField(win, "_isUpdatingUi", false);
        SetField(win, "_isUiInitialized", true);
        Invoke(win, "ScheduleAutoSave");
        var timer = GetField<DispatcherTimer>(win, "_autoSaveDebounceTimer")!;
        Check(timer != null && timer.IsEnabled, "ScheduleAutoSave starts 400ms debounce timer");

        string configsDir = ConfigManager.ConfigsFolder;
        string betaPath = System.IO.Path.Combine(configsDir, "Beta.json");
        Check(File.Exists(betaPath), "Beta.json exists before delete");

        bool delOk = (bool)InvokeWithOutString(win, "DeleteSavedConfigProfileCore", "Beta", out string fallback)!;
        Check(delOk && !File.Exists(betaPath) && !string.Equals(fallback, "Beta", StringComparison.OrdinalIgnoreCase),
            $"#236 D1 DeleteSavedConfigProfileCore deletes Beta.json and falls back (fallback={fallback})");
        var timerAfterDel = GetField<DispatcherTimer>(win, "_autoSaveDebounceTimer");
        Check(timerAfterDel == null || !timerAfterDel.IsEnabled, "#236 D1 stops auto-save debounce timer");

        // Wait past 400ms debounce window & pump dispatcher -> deleted file MUST stay deleted!
        var waitUntil = DateTime.UtcNow.AddMilliseconds(550);
        while (DateTime.UtcNow < waitUntil)
        {
            PumpDispatcherFrames();
            Thread.Sleep(50);
        }
        Check(!File.Exists(betaPath), "#236 D1 deleted config file is NOT recreated after 400ms debounce window");

        // Failed delete returns false and preserves current config untouched
        var beforeFailCfg = ConfigManager.CurrentConfig;
        bool failDel = (bool)InvokeWithOutString(win, "DeleteSavedConfigProfileCore", "DoesNotExistXYZ", out _)!;
        Check(!failDel && ReferenceEquals(ConfigManager.CurrentConfig, beforeFailCfg),
            "#236 D1 failed DeleteSavedConfigProfileCore keeps current config untouched");

        // D2: DeleteLayerCore stops timer, synchronously saves to disk, and immediately drops trigger from ProfileTriggerRouter
        var routeCfg = MakeBaselineConfig();
        routeCfg.ActiveConfigProfileName = fallback;
        var prof = routeCfg.Profiles[0];
        prof.UseApplicationContext = false;
        prof.Layers[0].ActivationTrigger = Mouse("MiddleButton");
        var layer2 = new WheelLayer { Name = "第二层", ActivationTrigger = Mouse("XButton1") };
        prof.Layers.Add(layer2);
        ConfigManager.CurrentConfig = routeCfg;
        ConfigManager.SaveConfig();
        string activePath = System.IO.Path.Combine(configsDir, fallback + ".json");
        var diskBeforeLayerDel = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(activePath))!;
        Check(diskBeforeLayerDel.Profiles[0].Layers.Count == 2, "seed active config file with 2 layers before DeleteLayerCore");
        ProfileTriggerRouter.Publish(routeCfg);
        Check(ProfileTriggerRouter.TryResolveMouse("any.exe", "XButton1", ModifierKeys.None, out var beforeLayerHit) == ProfileTriggerResolution.Match &&
              beforeLayerHit?.Layer == layer2,
            "XButton1 routes to layer2 before DeleteLayerCore");

        SetField(win, "_selectedProfile", prof);
        SetField(win, "_selectedLayer", layer2);
        var layerCombo = GetField<ComboBox>(win, "LayerSelectComboBox");
        if (layerCombo != null)
        {
            layerCombo.ItemsSource = prof.Layers;
            layerCombo.SelectedItem = layer2;
        }
        Invoke(win, "ScheduleAutoSave");
        Check(GetField<DispatcherTimer>(win, "_autoSaveDebounceTimer")!.IsEnabled, "timer armed before DeleteLayerCore");

        bool layerDelOk = (bool)Invoke(win, "DeleteLayerCore", layer2)!;
        Check(layerDelOk && prof.Layers.Count == 1 && !prof.Layers.Contains(layer2), "#236 D2 DeleteLayerCore removes layer");
        Check(!GetField<DispatcherTimer>(win, "_autoSaveDebounceTimer")!.IsEnabled, "#236 D2 stops auto-save timer");
        Check(ProfileTriggerRouter.TryResolveMouse("any.exe", "XButton1", ModifierKeys.None, out _) == ProfileTriggerResolution.None,
            "#236 D2 ProfileTriggerRouter immediately drops deleted layer trigger");

        // Check disk was updated synchronously without waiting for debounce timer!
        var diskCfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(activePath))!;
        Check(diskCfg.Profiles[0].Layers.Count == 1, "#236 D2 persists deleted layer synchronously to disk");

        // D3: SetAppTheme schedules debounced auto-save instead of synchronous save & skips offscreen preview work
        routeCfg.AppTheme = "Light";
        ConfigManager.SaveConfig();
        SetField(win, "_isUpdatingUi", false);
        SetField(win, "_isUiInitialized", true);
        appearanceGrid.Visibility = Visibility.Collapsed;
        liveCanvas.Children.Clear();
        int childrenBeforeOffscreenTheme = liveCanvas.Children.Count;

        Invoke(win, "SetAppTheme", "Dark");
        Check(ConfigManager.CurrentConfig.AppTheme == "Dark" && ConfigManager.CurrentConfig.SelectedArtStyleId == "",
            "#232 D3 SetAppTheme updates CurrentConfig.AppTheme immediately");
        var themeTimer = GetField<DispatcherTimer>(win, "_autoSaveDebounceTimer");
        Check(themeTimer != null && themeTimer.IsEnabled, "#232 D3 SetAppTheme schedules debounced auto-save");
        Check(liveCanvas.Children.Count == childrenBeforeOffscreenTheme,
            "#232 D3 SetAppTheme skips RenderLiveWheelPreview when AppearanceSettingsGrid is Collapsed");

        // Verify disk was NOT synchronously rewritten inside SetAppTheme!
        var diskBeforeFlush = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(activePath))!;
        Check(diskBeforeFlush.AppTheme == "Light", "#232 D3 disk config is not synchronously blocked/written during SetAppTheme");

        // When AppearanceSettingsGrid IS visible, ArtStyleService.NotifyChanged redraws preview & debounce eventually saves
        appearanceGrid.Visibility = Visibility.Visible;
        Invoke(win, "SetAppTheme", "Light");
        Check(liveCanvas.Children.Count > 0, "#232 D3 SetAppTheme redraws LiveWheelPreviewCanvas via ArtStyleService when Appearance tab is visible");

        Invoke(win, "SetAppTheme", "Dark");
        var flushUntil = DateTime.UtcNow.AddMilliseconds(1500);
        while (DateTime.UtcNow < flushUntil && GetField<DispatcherTimer>(win, "_autoSaveDebounceTimer")!.IsEnabled)
        {
            PumpDispatcherFrames();
            Thread.Sleep(50);
        }
        var diskAfterFlush = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(activePath))!;
        Check(diskAfterFlush.AppTheme == "Dark", $"#232 D3 scheduled auto-save flushes theme to disk after debounce (got {diskAfterFlush.AppTheme})");
    }
}
