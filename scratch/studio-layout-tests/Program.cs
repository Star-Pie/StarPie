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
        Check(window.FindName("StudioPalette") == null && Get<Grid>("StudioSaveFooter") != null,
            "the action dock is removed while the auto-save footer remains");
        var list = Get<RadioButton>("MappingsViewModeListRadio");
        var canvas = Get<RadioButton>("MappingsViewModeCanvasRadio");
        var depth = Get<WrapPanel>("MenuDepthControls");
        var depthPicker = Get<ComboBox>("MenuDepthPicker");
        var depthLabel = Get<TextBlock>("MenuDepthCaption");
        var depthCommands = new[] { Get<Button>("FixedLayoutToggle"), Get<Button>("FixedOpenCanvas")};
        var recognition = Get<FrameworkElement>("ApplicationIdentificationPanel");
        var shared = Get<FrameworkElement>("StudioSharedSettings");
        Check(shared is StackPanel && Get<CheckBox>("StudioCoreToggle").Parent is Grid,
            "profile and global settings are flat; they don't require unfolding sections");
        bool DepthInProfile() => ReferenceEquals(depthPicker.Parent, Get<ContentControl>("StudioDepthHost")) &&
            ReferenceEquals(depthLabel.Parent, Get<ContentControl>("StudioDepthLabelHost")) &&
            Get<WrapPanel>("StudioDepthActions").Children.Cast<Button>().SequenceEqual(depthCommands);
        bool DepthInOriginalGroup() => depth.Children.Cast<FrameworkElement>().SequenceEqual(
            new FrameworkElement[] { depthLabel, depthPicker }.Concat(depthCommands));
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
            Check(Equals(Get<Button>("StudioListToggle").Content, I18n.T("StudioSwitchToList")) &&
                Equals(Get<Button>("StudioListToggle").ToolTip, I18n.T("StudioSwitchToList")) &&
                Equals(Get<Button>("StudioContextBack").Content, "← " + I18n.T("StudioBackToProfileSettings")) &&
                Get<TextBlock>("StudioProfileDisplayLabel").Text == I18n.T("StudioProfileDisplayLabel") &&
                Equals(Get<RadioButton>("MappingsSectorCount12Radio").Content, I18n.TF("StudioProfileSectorChoice", 12)) &&
                ((TextBlock)Get<MappingsProfileRail>("StudioProfileRail").FindName("Title")).Text == I18n.T("StudioRailLibrary") &&
                ((TextBlock)Get<StudioProfileIdentity>("StudioIdentity").FindName("TagsLabel")).Text == I18n.T("StudioRailTags"),
                "localization " + lang);
            if (lang == LanguageCode.ZhCn)
                Check(Equals(Get<Button>("StudioListToggle").Content, "切换列表模式") &&
                    Equals(Get<Button>("StudioContextBack").Content, "← 返回方案设置") &&
                    ((TextBlock)Get<StudioProfileIdentity>("StudioIdentity").FindName("TagsLabel")).Text == "备注",
                    "list switch, back and notes labels match the requested Chinese wording");
            Check(DepthInProfile() && shared.Visibility == Visibility.Visible &&
                ReferenceEquals(recognition.Parent, Get<StackPanel>("StudioConditionsHost")),
                "profile depth and global options are available without expanding a section " + lang);
            window.SelectPrimarySlot(0);
            Check(ReferenceEquals(depth.Parent, Get<ContentControl>("StudioActionDepthHost")) && DepthInOriginalGroup() &&
                Get<FrameworkElement>("StudioActionSection").Visibility == Visibility.Visible, "selecting slot opens its inspector " + lang);
            Check(Equals(Get<Button>("StudioEditChildren").Content, I18n.T("StudioEditChildren")) &&
                Equals(Get<Button>("StudioChildrenBack").Content, "← " + I18n.T("StudioBackToPrimary")),
                "secondary page navigation localized " + lang);
            Check(Get<StackPanel>("StudioSlotAppearance").Visibility == Visibility.Visible &&
                Get<TextBlock>("StudioSlotAppearanceTitle").Text == I18n.T("StudioSlotAppearance"),
                "slot appearance is always expanded " + lang);
            Click("StudioEditChildren");
            Check(Get<StackPanel>("StudioChildrenPage").Visibility == Visibility.Visible &&
                Get<StackPanel>("StudioMainHeader").Visibility == Visibility.Collapsed &&
                Get<StackPanel>("StudioFocusEditorHost").Visibility == Visibility.Collapsed &&
                Get<Button>("StudioChildrenBack").Visibility == Visibility.Visible,
                "secondary page hides the primary editor " + lang);
            var originalChildren = global.Actions[0].SubActions;
            global.Actions[0].SubActions = [new ActionItem { Name = "fixture", Type = "None" }];
            window.SelectSubAction(0, 0);
            Check(Get<StackPanel>("StudioChildrenPage").Visibility == Visibility.Visible &&
                Get<StackPanel>("StudioFocusEditorHost").Visibility == Visibility.Visible,
                "selecting a secondary action exposes its editor on the secondary page " + lang);
            Click("StudioChildrenBack");
            Check(Get<StackPanel>("StudioChildrenPage").Visibility == Visibility.Collapsed &&
                Get<StackPanel>("StudioFocusEditorHost").Visibility == Visibility.Visible &&
                Get<Button>("StudioChildrenBack").Visibility == Visibility.Collapsed,
                "return restores primary inspector " + lang);
            global.Actions[0].SubActions = originalChildren;
            Click("StudioEditChildren");
            window.SelectPrimarySlot(1);
            Check(Get<StackPanel>("StudioChildrenPage").Visibility == Visibility.Collapsed &&
                Get<StackPanel>("StudioFocusEditorHost").Visibility == Visibility.Visible,
                "switching primary sectors closes the secondary page " + lang);
            window.SelectPrimarySlot(0);
            Click("StudioEditChildren");
            Call("UpdateStudioMode", true);
            Check(Get<StackPanel>("StudioChildrenPage").Visibility == Visibility.Collapsed &&
                Get<StackPanel>("StudioAdvancedChildren").Visibility == Visibility.Collapsed,
                "simple mode closes the secondary page " + lang);
            Call("UpdateStudioMode", false);
            Click("StudioContextBack");
            Check(DepthInProfile(), "return from action inspector " + lang);
            Click("StudioListToggle");
            Check(list.IsChecked == true && ReferenceEquals(depth.Parent, legacyDepth) && DepthInOriginalGroup() &&
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
                var toggle = Get<Button>("StudioInspectorToggle");
                var toolbar = (Grid)toggle.Parent;
                var toggleBounds = toggle.TransformToAncestor(toolbar).TransformBounds(new Rect(toggle.RenderSize));
                CheckAll(Math.Abs(toggleBounds.Right - toolbar.ActualWidth) <= 1 &&
                    Equals(toggle.Content, narrowInspector ? "<" : ">"),
                    $"{lang} {width:0} DIP the inspector chevron is at the toolbar's right edge and reflects its visibility");
                if (!narrowInspector)
                {
                    Click("StudioInspectorToggle"); Layout();
                    CheckAll(inspector.Visibility == Visibility.Collapsed && Equals(toggle.Content, "<") &&
                        Equals(toggle.ToolTip, I18n.T("StudioInspectorExpand")), "docked inspector collapses from the chevron " + lang);
                    Click("StudioInspectorToggle"); Layout();
                    CheckAll(inspector.Visibility == Visibility.Visible && Equals(toggle.Content, ">") &&
                        Equals(toggle.ToolTip, I18n.T("StudioInspectorCollapse")), "docked inspector reopens from the chevron " + lang);
                }
                var railToggle = Get<Button>("StudioRailToggle");
                CheckAll(Equals(railToggle.Content, narrowRail ? ">" : "<") &&
                    Equals(railToggle.ToolTip, I18n.T(narrowRail ? "StudioRailExpand" : "StudioRailCollapse")),
                    $"{lang} {width:0} DIP the left chevron matches the rail visibility");
                if (!narrowRail)
                {
                    Click("StudioRailToggle"); Layout();
                    CheckAll(rail.Visibility == Visibility.Collapsed && Equals(railToggle.Content, ">") &&
                        Equals(railToggle.ToolTip, I18n.T("StudioRailExpand")), "docked rail closes to the left " + lang);
                }
                Click("StudioRailToggle"); Layout();
                CheckAll(rail.Visibility == Visibility.Visible && Equals(railToggle.Content, "<") &&
                    Equals(railToggle.ToolTip, I18n.T("StudioRailCollapse")), "left chevron opens the profile rail " + lang);
                var railControl = Get<MappingsProfileRail>("StudioProfileRail");
                InView((TextBox)railControl.FindName("Search"), "search profiles");
                InView((Button)railControl.FindName("Add"), "add profile");
                InView((Button)railControl.FindName("Import"), "import profile");
                if (narrowRail)
                {
                    var close = Get<Button>("StudioRailClose");
                    InView(close, "close rail");
                    CheckAll(Equals(close.Content, "<") && Equals(close.ToolTip, I18n.T("StudioRailCollapse")),
                        "left drawer exposes its collapse chevron " + lang);
                    Click("StudioRailClose"); Layout();
                    CheckAll(rail.Visibility == Visibility.Collapsed && Equals(railToggle.Content, ">"),
                        "left drawer closes and can be reopened from the toolbar " + lang);
                }
                if (narrowInspector) { Click("StudioInspectorToggle"); Layout(); }
                InView((Button)Get<StudioProfileIdentity>("StudioIdentity").FindName("IconButton"), "profile identity");
                InView(Get<Button>("ProfileModeRecordButton"), "record trigger");
                if (narrowInspector)
                {
                    var close = Get<Button>("StudioInspectorClose");
                    InView(close, "close inspector");
                    CheckAll(Equals(toggle.Content, ">") && Equals(close.Content, ">") &&
                        Equals(close.ToolTip, I18n.T("StudioInspectorCollapse")), "drawer exposes the same collapse chevron " + lang);
                    Click("StudioInspectorClose"); Layout();
                    CheckAll(inspector.Visibility == Visibility.Collapsed && Equals(toggle.Content, "<"), "drawer closes and the toolbar offers to reopen it " + lang);
                }
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
                    CheckAll(DepthInProfile(), tag + " returns to Studio");

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
        VerifyInspector();
        void VerifyInspector()
        {
            var content = (FrameworkElement)window.Content;
            var inspector = Get<Border>("StudioInspector");
            var visualRoot = (Visual)content;
            while (VisualTreeHelper.GetParent(visualRoot) is Visual parent) visualRoot = parent;
            VisualTreeHelper.SetRootDpi(visualRoot, new DpiScale(1, 1));
            Call("ApplySettingsUiScale", 1d);
            var paper = WinPieGestures.ArtStyles.ArtStyleCatalog.Find(ConfigManager.CurrentConfig, "paper")!;
            WinPieGestures.ArtStyles.ArtStyleResources.Apply(window, paper, publish: false);

            void Layout(Size size)
            {
                for (int i = 0; i < 2; i++)
                {
                    content.InvalidateMeasure();
                    content.Measure(size);
                    content.Arrange(new Rect(size));
                    content.UpdateLayout();
                    Call("UpdateStudioLayout");
                }
            }
            Rect Bounds(FrameworkElement element, Visual ancestor) =>
                element.TransformToAncestor(ancestor).TransformBounds(new Rect(element.RenderSize));
            void Capture(Size size, string file)
            {
                if (evidenceDirectory == null) return;
                Directory.CreateDirectory(evidenceDirectory);
                var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(content);
                var rect = Bounds(inspector, content);
                var crop = new Int32Rect((int)Math.Ceiling(rect.Left), (int)Math.Ceiling(rect.Top),
                    (int)Math.Floor(rect.Width) - 1, (int)Math.Floor(rect.Height) - 1);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(new CroppedBitmap(bitmap, crop)));
                using var stream = File.Create(Path.Combine(evidenceDirectory, file));
                encoder.Save(stream);
            }

            var revision = ConfigManager.ConfigurationRevision;
            var files = Files(data);
            var normal = new Size(1220, 740);
            I18n.CurrentLanguage = LanguageCode.ZhCn;
            window.ApplyLocalization();
            Click("StudioContextBack");
            var identity = Get<StudioProfileIdentity>("StudioIdentity");
            identity.Refresh(global);
            Layout(normal);
            if (inspector.Visibility != Visibility.Visible) { Click("StudioInspectorToggle"); Layout(normal); }
            Capture(normal, "profile-inspector.png");
            var layoutFraction = config.StudioInspectorFraction;
            try
            {
                foreach (var lang in new[] { LanguageCode.ZhCn, LanguageCode.ZhTw, LanguageCode.En, LanguageCode.Ja })
                {
                    I18n.CurrentLanguage = lang;
                    window.ApplyLocalization();
                    foreach (double fraction in new[] { layoutFraction, .01 })
                    {
                        config.StudioInspectorFraction = fraction;
                        Layout(normal);
                        var chooser = Get<CheckBox>("ComponentIdentificationCheckBox");
                        var track = (FrameworkElement)chooser.Template.FindName("Track", chooser);
                        var rightEdge = Bounds(Get<ComboBox>("MenuDepthPicker"), inspector).Right;
                        var layerSwitch = Bounds(Get<ComboBox>("LayerSwitchTriggerComboBox"), inspector);
                        var profileDisplay = Bounds(Get<ComboBox>("ProfileDisplayModeComboBox"), inspector);
                        string label = $"{lang}, flat inspector {inspector.ActualWidth:F0} DIP";
                        bool Aligned(double right) => Math.Abs(right - rightEdge) <= 1;
                        double recordRight = Bounds(Get<Button>("ProfileModeRecordButton"), inspector).Right;
                        double recognitionRight = Bounds(track, inspector).Right;
                        double coreRight = Bounds(Get<CheckBox>("StudioCoreToggle"), inspector).Right;
                        double inheritanceRight = Bounds(Get<CheckBox>("EnableGlobalInheritanceCheckBox"), inspector).Right;
                        CheckAll(Aligned(recordRight) && Aligned(recognitionRight) && Aligned(coreRight) && Aligned(inheritanceRight),
                            label + $" uses one right edge for recording, recognition, core visibility and inheritance ({recordRight:F1}/{recognitionRight:F1}/{coreRight:F1}/{inheritanceRight:F1} vs {rightEdge:F1})");
                        CheckAll(layerSwitch.Width >= 90 && Aligned(layerSwitch.Right) && Aligned(profileDisplay.Right) &&
                                 Math.Abs(layerSwitch.Left - profileDisplay.Left) <= 1 && Math.Abs(layerSwitch.Height - profileDisplay.Height) <= 1,
                            label + " gives the two global drop-downs the same size and alignment");
                        double labelLeft = Bounds(Get<TextBlock>("SectorCountLabel"), inspector).Left;
                        CheckAll(new[] { "ComponentIdentificationStateText", "StudioInheritLabel", "LayerSwitchTriggerLabel", "StudioProfileDisplayLabel" }
                            .All(name => Math.Abs(Bounds(Get<TextBlock>(name), inspector).Left - labelLeft) <= 1),
                            label + " aligns the global labels and identification status to the same left edge");
                        if (evidenceDirectory != null && lang == LanguageCode.ZhCn && fraction == layoutFraction)
                        {
                            var fullHeight = new Size(normal.Width, 880);
                            Layout(fullHeight);
                            Capture(fullHeight, "profile-inspector-flat.png");
                        }
                    }
                }
            }
            finally
            {
                config.StudioInspectorFraction = layoutFraction;
                I18n.CurrentLanguage = LanguageCode.ZhCn;
                window.ApplyLocalization();
                Layout(normal);
            }
            var profileIcon = Bounds((Button)identity.FindName("IconButton"), inspector);
            var profileName = Bounds((TextBox)identity.FindName("NameEntry"), inspector);
            CheckAll(profileIcon.Right <= profileName.Left && profileName.Top < profileIcon.Bottom && profileName.Bottom > profileIcon.Top,
                "profile name and icon share a line above the existing tags field");
            CheckAll(ConfigManager.ConfigurationRevision == revision && Files(data).SequenceEqual(files),
                "arranging profile settings does not change persisted settings");
        }
        Console.WriteLine($"{_checks} passed, {Failures.Count} failed.");
        if (Failures.Count != 0) throw new InvalidOperationException(string.Join(Environment.NewLine, Failures));
        Console.WriteLine($"{_checks} checks passed.");
    }
}
