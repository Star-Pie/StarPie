using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinPieGestures;

internal static class Program
{
    private static int _checks;
    private static readonly List<string> Failures = new();
    private static void Check(bool ok, string label)
    {
        if (!ok) throw new InvalidOperationException(label);
        _checks++;
        Console.WriteLine("PASS " + label);
    }
    private static void CheckAll(bool ok, string label)
    {
        if (ok) { Check(true, label); return; }
        Console.WriteLine("FAIL " + label);
        Failures.Add(label);
    }

    [STAThread]
    private static void Main(string[] args)
    {
        if (!args.Contains("--test-instance")) throw new InvalidOperationException("Use --test-instance.");
        var dir = Directory.CreateTempSubdirectory("starpie-studio-layout-");
        foreach (string key in new[] { "APPDATA", "LOCALAPPDATA", "TEMP", "TMP" })
            Environment.SetEnvironmentVariable(key, dir.FullName);
        int evidence = Array.IndexOf(args, "--evidence");
        try { Probe(dir.FullName, evidence < 0 ? null : Path.GetFullPath(args[evidence + 1])); } catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
        finally { dir.Delete(true); }
    }

    private static void Probe(string data, string? evidenceDirectory)
    {
        string path = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "CandidateDll").Value!;
        static string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
        var sha = Hash(typeof(SettingsWindow).Assembly.Location);
        Check(sha == Hash(path), "loaded candidate SHA256 " + sha);
        ConfigManager.DisableAutoStartSync = true;
        var config = ConfigManager.CurrentConfig;
        config.AutoCheckUpdate = false;
        config.ConfigMode = "Pro";
        var global = new WheelProfile { ProcessName = "Global", DisplayName = "Fixture" };
        global.EnsureLayers();
        config.Profiles = [global];
        var app = new App();
        using var hold = Dispatcher.CurrentDispatcher.DisableProcessing();
        app.InitializeComponent();
        static string[] Files(string root) => Directory.GetFiles(root, "*.json", SearchOption.AllDirectories)
            .OrderBy(x => x).Select(x => x + "|" + new FileInfo(x).LastWriteTimeUtc.Ticks + "|" + Hash(x)).ToArray();
        var disk = Files(data);
        long before = ConfigManager.ConfigurationRevision;
        var changes = new List<string>();
        ConfigManager.ConfigurationChanged += () => changes.Add(string.Join(" -> ", new System.Diagnostics.StackTrace().GetFrames().Select(x => x.GetMethod()).Where(m => m?.DeclaringType?.Assembly == typeof(SettingsWindow).Assembly).Take(6).Select(m => m?.Name)));
        Check(!WinPieGestures.Plugins.PluginHost.IsInitialized, "plugin host begins uninitialized");
        var window = new SettingsWindow();
        Check(new WindowInteropHelper(window).Handle == IntPtr.Zero, "settings initialized without an HWND");
        T Get<T>(string name) where T : FrameworkElement => (T)window.FindName(name);
        object? Field(string name) => typeof(SettingsWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window);
        void Call(string name, params object[] values) =>
            typeof(SettingsWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, values);
        void Click(string id)
        {
            var b = Get<Button>(id);
            b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, b));
        }
        void Passive(string step)
        {
            var current = Files(data);
            if (ConfigManager.ConfigurationRevision != before || !current.SequenceEqual(disk) || (Field("_autoSaveDebounceTimer") as DispatcherTimer)?.IsEnabled == true)
                Console.WriteLine($"DIAG {step}: revision={before}/{ConfigManager.ConfigurationRevision}, timer={(Field("_autoSaveDebounceTimer") as DispatcherTimer)?.IsEnabled}, files added/changed={string.Join(";", current.Except(disk))}, notifications={string.Join("; ", changes)}");
            CheckAll(ReferenceEquals(config, ConfigManager.CurrentConfig) && ConfigManager.ConfigurationRevision == before &&
                (Field("_autoSaveDebounceTimer") as DispatcherTimer)?.IsEnabled != true && Files(data).SequenceEqual(disk),
                step + " leaves config revision, disk and debounce unchanged");
            Check(!WinPieGestures.Plugins.PluginHost.IsInitialized && !File.Exists(Path.Combine(data, "StarPie", "plugin-data", "health.json")), step + " does not initialize the plugin host or create health metadata");
            Check(App.MainKeyboardHook == null && App.MainMouseHook == null &&
                new[] { "_rawMouseInputHookAttached", "_rawKeyboardInputHookAttached", "_exclusiveKeyboardHookAttached" }.All(f => Equals(Field(f), false)),
                step + " attaches no global input hooks");
        }
        Passive("initialization");
        window.SwitchToTab(2);
        var studio = Get<Grid>("MappingsCanvasModeGrid");
        var list = Get<RadioButton>("MappingsViewModeListRadio");
        var canvas = Get<RadioButton>("MappingsViewModeCanvasRadio");
        var depth = Get<FrameworkElement>("MenuDepthControls");
        var recognition = Get<FrameworkElement>("ApplicationIdentificationPanel");
        list.IsChecked = true;
        var legacyDepth = depth.Parent;
        var legacyConditions = recognition.Parent;
        canvas.IsChecked = true;
        foreach (var lang in new[] { LanguageCode.ZhCn, LanguageCode.ZhTw, LanguageCode.En, LanguageCode.Ja })
        {
            I18n.CurrentLanguage = lang;
            window.ApplyLocalization();
            // An unshown Window never raises IsVisibleChanged. Populate the child views using their production refresh functions.
            Get<MappingsProfileRail>("StudioProfileRail").Refresh(config, global);
            Get<StudioProfileIdentity>("StudioIdentity").Refresh(global);
            Check((Equals(Get<Button>("StudioListToggle").Content, I18n.T("StudioList")) || Equals(Get<Button>("StudioListToggle").ToolTip, I18n.T("StudioList"))) &&
                Equals(Get<Expander>("StudioStructure").Header, I18n.T("StudioStructure")) &&
                ((TextBlock)Get<MappingsProfileRail>("StudioProfileRail").FindName("Title")).Text == I18n.T("StudioRailLibrary") &&
                ((TextBlock)Get<StudioProfileIdentity>("StudioIdentity").FindName("TagsLabel")).Text == I18n.T("StudioRailTags"),
                "localization " + lang);
            Check(ReferenceEquals(depth.Parent, Get<ContentControl>("StudioDepthHost")) &&
                ReferenceEquals(recognition.Parent, Get<StackPanel>("StudioConditionsHost")), "profile host mounting " + lang);
            window.SelectPrimarySlot(0);
            Check(ReferenceEquals(depth.Parent, Get<ContentControl>("StudioActionDepthHost")) &&
                Get<FrameworkElement>("StudioActionSection").Visibility == Visibility.Visible, "selecting slot opens its inspector " + lang);
            Click("StudioContextBack");
            Check(ReferenceEquals(depth.Parent, Get<ContentControl>("StudioDepthHost")), "return from action inspector " + lang);
            Click("StudioListToggle");
            Check(list.IsChecked == true && ReferenceEquals(depth.Parent, legacyDepth) &&
                ReferenceEquals(recognition.Parent, legacyConditions), "list restores original controls " + lang);
            canvas.IsChecked = true;

            foreach (var (pixels, dpi, scale) in new[] { (1190, 1d, 1d), (1425, 1.5, 1.25), (1560, 2d, 1.5) })
            {
                double width = pixels / dpi / scale;
                var root = (Visual)studio;
                while (VisualTreeHelper.GetParent(root) is Visual parent) root = parent;
                VisualTreeHelper.SetRootDpi(root, new DpiScale(dpi, dpi));
                Call("ApplySettingsUiScale", scale);
                Check(Math.Abs(VisualTreeHelper.GetDpi(studio).DpiScaleX - dpi) < .01 &&
                    Math.Abs(((ScaleTransform)window.FindName("RootUiScaleTransform")).ScaleX - scale) < .01,
                    $"DPI and settings scale applied {lang} {dpi * 96:0}/{scale}");
                var area = new Size(width, 760);
                void Layout()
                {
                    studio.Measure(area);
                    studio.Arrange(new Rect(area));
                    Call("UpdateStudioLayout");
                    studio.Measure(area);
                    studio.Arrange(new Rect(area));
                }
                void InView(FrameworkElement button, string id)
                {
                    var box = button.TransformToAncestor(studio).TransformBounds(new Rect(button.RenderSize));
                    string label = $"{lang} {width:0} DIP {id} bounds {box}";
                    CheckAll(box.Width >= (id == "record trigger" ? 36 : 16) && box.Height >= 16 && box.Left >= -1 && box.Top >= -1 && box.Right <= width + 1 && box.Bottom <= area.Height + 1, label);
                }
                Layout();
                Check(Math.Abs(studio.ActualWidth - width) < 2, $"effective studio width {lang} {width:0}: {studio.ActualWidth:0}");
                foreach (var id in new[] { "StudioRailToggle", "StudioInspectorToggle", "StudioListToggle", "MappingsZoomInBtn" })
                    InView(Get<Button>(id), id);
                bool narrowRail = width < 850, narrowInspector = width < 625;
                var rail = Get<FrameworkElement>("StudioRailPane");
                var inspector = Get<FrameworkElement>("StudioInspector");
                Check(rail.Visibility == (narrowRail ? Visibility.Collapsed : Visibility.Visible) &&
                    inspector.Visibility == (narrowInspector ? Visibility.Collapsed : Visibility.Visible), $"pane mode {lang} {width:0}");
                if (narrowRail) { Click("StudioRailToggle"); Layout(); }
                var railControl = Get<MappingsProfileRail>("StudioProfileRail");
                InView((TextBox)railControl.FindName("Search"), "search profiles");
                InView((Button)railControl.FindName("Add"), "add profile");
                InView((Button)railControl.FindName("Import"), "import profile");
                if (narrowRail) { InView(Get<Button>("StudioRailClose"), "close rail"); Click("StudioRailClose"); Layout(); }
                if (narrowInspector) { Click("StudioInspectorToggle"); Layout(); }
                InView((Button)Get<StudioProfileIdentity>("StudioIdentity").FindName("IconButton"), "profile identity");
                InView(Get<Button>("ProfileModeRecordButton"), "record trigger");
                if (narrowInspector) { InView(Get<Button>("StudioInspectorClose"), "close inspector"); Click("StudioInspectorClose"); Layout(); }
            }
            FullWindowContent(lang);
            Passive("Studio/list navigation " + lang);
        }
        void FullWindowContent(LanguageCode lang)
        {
            var content = (FrameworkElement)window.Content;
            var size = new Size(1060, 660);
            var clip = new Rect(size);
            var root = (Visual)content;
            while (VisualTreeHelper.GetParent(root) is Visual parent) root = parent;
            VisualTreeHelper.SetRootDpi(root, new DpiScale(1.25, 1.25));
            var stage = Get<FrameworkElement>("MappingsPreviewViewportContainer");
            var rail = Get<MappingsProfileRail>("StudioProfileRail");
            var identity = Get<StudioProfileIdentity>("StudioIdentity");
            double firstWidth = double.NaN;
            void Layout()
            {
                content.InvalidateMeasure();
                content.Measure(size);
                content.Arrange(clip);
                content.UpdateLayout();
                Call("UpdateStudioLayout");
                content.InvalidateMeasure();
                content.Measure(size);
                content.Arrange(clip);
                content.UpdateLayout();
            }
            Rect ViewportBounds(FrameworkElement element)
            {
                Rect box = element.TransformToAncestor(content).TransformBounds(new Rect(element.RenderSize));
                if (VisualTreeHelper.GetTransform(content) is { } transform) box = transform.TransformBounds(box);
                var offset = VisualTreeHelper.GetOffset(content);
                box.Offset(offset.X, offset.Y);
                return box;
            }
            void Capture(string file)
            {
                if (evidenceDirectory == null || lang != LanguageCode.ZhCn) return;
                Directory.CreateDirectory(evidenceDirectory);
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width * 1.25),
                    (int)Math.Ceiling(size.Height * 1.25), 120, 120, PixelFormats.Pbgra32);
                bitmap.Render(content);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(evidenceDirectory, file));
                encoder.Save(stream);
                Console.WriteLine("PNG " + file);
            }
            if (lang == LanguageCode.ZhCn && evidenceDirectory != null)
            {
                size = new Size(1220, 740);
                clip = new Rect(size);
                Call("ApplySettingsUiScale", 1d);
                Layout();
                Capture("studio-1220x740-100pct.png");
                size = new Size(1060, 660);
                clip = new Rect(size);
            }
            foreach (bool collapsed in new[] { false, true })
            {
                foreach (double scale in new[] { .8d, 2d })
                {
                    Call("ApplySettingsUiScale", scale);
                    Layout();
                    if ((Get<FrameworkElement>("SidebarBorder").ActualWidth < 100) != collapsed)
                    {
                        Click("SidebarToggleButton");
                        Layout();
                    }
                    if (collapsed && scale == 2) Capture("studio-1060x660-200pct.png");
                    string tag = $"full 1060x660 DIP {lang} DPI=120 scale={scale} navigation={(collapsed ? "compact" : "expanded")}";
                    CheckAll((Get<FrameworkElement>("SidebarBorder").ActualWidth < 100) == collapsed, tag + " requested sidebar state");
                    Check(Math.Abs(VisualTreeHelper.GetDpi(stage).DpiScaleX - 1.25) < .01, tag + " root DPI propagated");
                    Check(Math.Abs(Get<Grid>("RootScaleGrid").LayoutTransform.Value.M11 - scale) < .001, tag + " root LayoutTransform applied");
                    Console.WriteLine($"METRIC {tag} studioLogical={studio.RenderSize} stageLogical={stage.RenderSize} studioViewport={ViewportBounds(studio)} stageViewport={ViewportBounds(stage)} sidebar={Get<FrameworkElement>("SidebarBorder").ActualWidth:0.0}DIP");
                    if (scale == .8) firstWidth = studio.ActualWidth;
                    else CheckAll(studio.ActualWidth < firstWidth - 50, tag + " global scale changes effective width");
                    void Visible(FrameworkElement e, string label)
                    {
                        var box = ViewportBounds(e);
                        Rect exposed = Rect.Intersect(box, clip);
                        bool visible = true;
                        for (Visual? v = e; v != null && !ReferenceEquals(v, content); v = VisualTreeHelper.GetParent(v) as Visual)
                        {
                            if (v is UIElement u && u.Visibility != Visibility.Visible) { visible = false; break; }
                            if (VisualTreeHelper.GetClip(v) is { } geometry)
                                exposed.Intersect(v.TransformToAncestor(content).TransformBounds(geometry.Bounds));
                        }
                        bool HitTarget(double x, double y)
                        {
                            bool matches = false;
                            VisualTreeHelper.HitTest(content,
                                v => v is UIElement u && (!u.IsHitTestVisible || u.Visibility != Visibility.Visible)
                                    ? HitTestFilterBehavior.ContinueSkipSelfAndChildren : HitTestFilterBehavior.Continue,
                                r => {
                                    for (DependencyObject? v = r.VisualHit; v != null; v = VisualTreeHelper.GetParent(v))
                                        if (ReferenceEquals(v, e)) { matches = true; break; }
                                    return HitTestResultBehavior.Stop;
                                }, new PointHitTestParameters(new Point(exposed.X + exposed.Width * x, exposed.Y + exposed.Height * y)));
                            return matches;
                        }
                        // A wide translated segmented button can be clipped by its parent while its visible portion remains actionable.
                        bool hitButton = !exposed.IsEmpty && new[] { .5, .2, .8, .05, .95 }.Any(x => new[] { .5, .25, .75 }.Any(y => HitTarget(x, y)));
                        CheckAll(visible && e.IsEnabled && hitButton && exposed.Width >= 12 && exposed.Height >= 12 && box.Left >= -1 && box.Top >= -1 &&
                            box.Right <= size.Width + 1 && box.Bottom <= size.Height + 1, tag + " " + label + " bounds=" + box + " exposed=" + exposed + " hit=" + (hitButton ? "target" : "none"));
                    }
                    var stageBounds = ViewportBounds(stage);
                    var visibleStage = Rect.Intersect(clip, stageBounds);
                    CheckAll(!visibleStage.IsEmpty && visibleStage.Width > 0 && visibleStage.Height > 0, tag + " visible stage=" + visibleStage);
                    var preview = Get<Canvas>("MappingsWheelPreviewCanvas");
                    var toClient = preview.TransformToAncestor(content);
                    var primary = new HashSet<int>();
                    for (int x = 1; x < preview.RenderSize.Width; x += 3)
                    for (int y = 1; y < preview.RenderSize.Height; y += 3)
                    {
                        var p = new Point(x, y);
                        if (!visibleStage.Contains(toClient.Transform(p))) continue;
                        if (window.HitTestCanvasPoint(p) is { IsCenter: false, IsSubAction: false, FixedLevel: null } slot)
                            primary.Add(slot.SlotIndex);
                    }
                    CheckAll(primary.Count == global.SectorCount, tag + $" initially reachable primary sectors {primary.Count}/{global.SectorCount}: " + string.Join(",", primary.OrderBy(i => i)));
                    foreach (var id in new[] { "StudioRailToggle", "StudioInspectorToggle", "StudioListToggle" }) Visible(Get<Button>(id), id);
                    Click("StudioListToggle"); Layout();
                    CheckAll(list.IsChecked == true && ReferenceEquals(depth.Parent, legacyDepth), tag + " navigates to list");
                    canvas.BringIntoView(); Layout();
                    Visible(canvas, "return to Studio");
                    canvas.IsChecked = true; Layout();
                    CheckAll(ReferenceEquals(depth.Parent, Get<ContentControl>("StudioDepthHost")), tag + " returns to Studio");

                    bool railDrawer = studio.ActualWidth < 850, inspectorDrawer = studio.ActualWidth < 625;
                    if (railDrawer) { Click("StudioRailToggle"); Layout(); }
                    Visible((TextBox)rail.FindName("Search"), "search");
                    Visible((Button)rail.FindName("Add"), "add");
                    Visible((Button)rail.FindName("Import"), "import");
                    if (railDrawer) { Visible(Get<Button>("StudioRailClose"), "close library"); Click("StudioRailClose"); Layout(); }
                    if (inspectorDrawer) { Click("StudioInspectorToggle"); Layout(); }
                    Visible((Button)identity.FindName("IconButton"), "identity");
                    Get<Button>("ProfileModeRecordButton").BringIntoView();
                    Layout();
                    Visible(Get<Button>("ProfileModeRecordButton"), "record");
                    if (inspectorDrawer)
                    {
                        Visible(Get<Button>("StudioInspectorClose"), "close inspector");
                        Click("StudioInspectorClose"); Layout();
                    }
                }
            }
        }
        Console.WriteLine($"{_checks} passed, {Failures.Count} failed.");
        if (Failures.Count != 0) throw new InvalidOperationException(string.Join(Environment.NewLine, Failures));
        Console.WriteLine($"{_checks} checks passed.");
    }
}
