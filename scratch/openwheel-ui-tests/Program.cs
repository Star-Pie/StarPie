using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinPieGestures;
using WinPieGestures.Plugins;

internal static class Program
{
    private static int _passed;
    private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.NonPublic;

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, (Action)(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length != 1 || !args.Contains("--test-instance")) throw new ArgumentException("Use --test-instance.");
        string sandbox = Directory.CreateTempSubdirectory("starpie-openwheel-ui-").FullName;
        foreach (string key in new[] { "APPDATA", "LOCALAPPDATA", "TEMP", "TMP" }) Environment.SetEnvironmentVariable(key, sandbox);
        try
        {
            RunChecks();
        }
        finally
        {
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, true);
        }
        Console.WriteLine($"{_passed} passed");
    }

    private static void RunChecks()
    {
        ConfigManager.DisableAutoStartSync = true;
        string candidate = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "CandidateDll").Value!;
        Check(SHA256.HashData(File.ReadAllBytes(candidate)).SequenceEqual(SHA256.HashData(
            File.ReadAllBytes(typeof(PortalTargetEditor).Assembly.Location))), "Candidate hash");
        var app = new App();
        app.InitializeComponent();
        var profile = new WheelProfile { ProcessName = "design.exe", Layers = { new(), new() { Name = "Drafting", EnableCenterAction = true, CenterAction = new() { Type = "Hotkey", Parameter = "Ctrl+Z", Name = "Undo" } } } };
        var parallel = new WheelProfile { ProcessName = "cross-app", UseApplicationContext = false, Layers = { new() } };
        var appProfile = new WheelProfile { ProcessName = "editor.exe", Layers = { new() } };
        foreach (var entry in new[] { profile, parallel, appProfile }) entry.EnsureLayers();
        var config = ConfigManager.CurrentConfig;
        config.Profiles = [profile, parallel, appProfile];
        config.ShowCoreCircle = true;
        config.ShowSelectedActionText = true;
        ConfigManager.MarkConfigurationChanged();
        var source = new PortalLayerAddress(profile, profile.Layers[0]);
        var portal = profile.Layers[0].Actions[0];
        portal.Type = "OpenWheel";
        portal.PortalTarget = new() { SameProfile = true, TargetLayerIndex = 1, TargetLayerNameSnapshot = "Drafting" };
        var loop = profile.Layers[1].Actions[0];
        loop.Type = "OpenWheel";
        loop.PortalTarget = new() { SameProfile = true, TargetLayerIndex = 0 };
        profile.Layers[1].Actions[1] = new ActionItem { Type = "Hotkey", Parameter = "Ctrl+C", Name = "Copy" };
        Check(PortalTargetEditor.DestinationsFor(config.Profiles, source, "same").Single().Address.Layer == profile.Layers[1] &&
            PortalTargetEditor.DestinationsFor(config.Profiles, source, "parallel").Single().Address.Profile == parallel &&
            PortalTargetEditor.DestinationsFor(config.Profiles, source, "apps").Single().Address.Profile == appProfile,
            "Three source lists exclude the current layer");
        Check(!ActionTypeCatalog.BuildLocalizedActionTypes().Any(item => item.Tag == "OpenWheel") &&
            ActionTypeCatalog.BuildAggregatedActionTypes(includePortal: true).Any(item => item.Tag == "OpenWheel"),
            "Only wheel slot editors opt in to the action");

        var editor = new PortalTargetEditor();
        var grid = new Grid { Children = { editor } };
        int writes = 0;
        int previews = 0;
        PortalLayerAddress? jump = null;
        var context = new PortalEditorContext(config, source, address => jump = address, _ => previews++);
        string before = JsonSerializer.Serialize(config);
        var translated = new HashSet<string>();
        foreach (string locale in new[] { "zh-CN", "zh-TW", "en-US", "ja-JP" })
        {
            I18n.SetLanguage(locale);
            editor.Bind(context, portal, target => { writes++; PortalTargetEditor.ApplyTarget(portal, target); });
            var title = Find<TextBlock>("Title");
            var warning = Find<TextBlock>("Warning");
            translated.Add(title.Text);
            Check(title.Text == I18n.T("PortalTarget") && warning.Text.Contains("Drafting") && warning.Visibility == Visibility.Visible,
                locale + " uses localized controls and a cycle path");
            foreach (var (theme, dpi) in new[] { ("Light", 96), ("ObsidianDark", 144), ("TitaniumGray", 192) })
            {
                AppThemeManager.ApplyTheme(grid, theme);
                grid.Measure(new Size(320, 480));
                grid.Arrange(new Rect(0, 0, 320, 480));
                grid.UpdateLayout();
                var render = new RenderTargetBitmap(320 * dpi / 96, 480 * dpi / 96, dpi, dpi, PixelFormats.Pbgra32);
                render.Render(grid);
                var thumbnail = Find<PortalWheelPreview>("Thumbnail");
                var position = thumbnail.TransformToAncestor(grid).Transform(new Point());
                Check(thumbnail.ActualWidth >= 90 && position.X >= 0 && position.Y >= 0 &&
                    position.X + thumbnail.ActualWidth <= 320 && position.Y + thumbnail.ActualHeight <= 480,
                    locale + " " + theme + " " + dpi + " DPI thumbnail inside inspector");
                var foreground = (SolidColorBrush)grid.FindResource("TextPrimaryBrush");
                var background = (SolidColorBrush)grid.FindResource("SubtleCardBrush");
                double ink = Luminance(foreground.Color);
                double pane = Luminance(background.Color);
                Check((Math.Max(ink, pane) + .05) / (Math.Min(ink, pane) + .05) >= 4.5,
                    locale + " " + theme + " inspector warning and heading contrast");
            }
        }
        Check(translated.Count == 4 && writes == 0 && JsonSerializer.Serialize(config) == before, "Four translations and no state changes on bind or render");
        var sourceBox = Find<ComboBox>("Sources");
        sourceBox.SelectedValue = "parallel";
        Check(writes == 0 && profile.Layers[1].LayerId == null, "Browsing sources does not write a target or generate its ID");
        var targetBox = Find<ComboBox>("Destinations");
        targetBox.SelectedIndex = 0;
        Check(writes == 1 && portal.PortalTarget?.TargetProfileKey == parallel.ProcessName &&
            portal.PortalTarget?.TargetLayerId == parallel.Layers[0].LayerId && parallel.Layers[0].LayerId != null, "Explicit selection assigns a stable ID");
        Find<Button>("Jump").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Find<Button>("Ghost").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(jump?.Profile == parallel && previews == 1 && writes == 1, "Jump and preview use staged, read-only callbacks");

        var closure = StudioProfileLibrary.PortalExportProfiles(profile, config.Profiles);
        Check(closure.SequenceEqual(new[] { profile, parallel }), "Export includes only the linked profiles");
        var imported = StudioProfileLibrary.ReadProfiles(StudioProfileLibrary.SerializeProfiles(closure));
        StudioProfileLibrary.NameImportedProfiles(imported, config.Profiles, "Imported draft");
        var importedAction = imported[0].Layers[0].Actions[0];
        Check(imported[0].ProcessName == "Imported draft" && imported[1].ProcessName != parallel.ProcessName &&
            PortalReferenceAnalyzer.Resolve(imported, imported[0], importedAction.PortalTarget!)?.Profile == imported[1],
            "A naming collision retargets the imported chain to its independent copy");

        // Fixes 3 & 4: RadialWindow child core badge swap, ShowPortalFeedback storyboard, and PortalTrailOverlay Z-order & bounds.
        var screen = ScreenHelper.GetScreenContextAtPoint(new Point(400, 400));
        var center = new Point(screen.PhysicalWorkArea.Left + screen.PhysicalWorkArea.Width / 2,
            screen.PhysicalWorkArea.Top + screen.PhysicalWorkArea.Height / 2);
        var radial = new RadialWindow(center, profile) { Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        radial.Show();
        radial.GetType().GetMethod("SetMousePassThrough", InstanceFlags)!.Invoke(radial, [true]);
        try
        {
            var view = new WheelProfile { ProcessName = profile.ProcessName, Layers = profile.Layers, ActiveLayerIndex = 1 };
            view.SyncRootPropertiesFromActiveLayer();
            var rootFrame = new PortalFrame(source, center, 133, center);
            var childFrame = new PortalFrame(new(profile, profile.Layers[1]), new Point(center.X + 200, center.Y), 133, center);
            var stack = new PortalSessionStack(rootFrame, ConfigManager.ConfigurationRevision, 5, null);
            stack.Push(childFrame);

            void CallRadial(string name, params object?[] args) =>
                typeof(RadialWindow).GetMethod(name, InstanceFlags | BindingFlags.Public)!.Invoke(radial, args);
            object? FieldRadial(string name) => typeof(RadialWindow).GetField(name, InstanceFlags)!.GetValue(radial);

            CallRadial("PresentPortalFrame", childFrame, view, ConfigManager.ConfigurationRevision, 9L, "MainWheel");
            var back = (Border?)FieldRadial("_portalBack");
            var exitIcon = (FrameworkElement)radial.FindName("CoreExitIcon");
            var textPanel = (FrameworkElement)radial.FindName("CoreTextPanel");
            Check(back != null && back.Visibility == Visibility.Visible &&
                exitIcon.Visibility == Visibility.Collapsed && textPanel.Visibility == Visibility.Collapsed,
                "Child portal frame hides CoreExitIcon/CoreTextPanel cleanly behind _portalBack");
            var backText = (TextBlock)back!.Child;
            Check(Luminance(((SolidColorBrush)backText.Foreground).Color) > 0.7,
                "_portalBack text keeps high contrast light ink on dark core ellipse");

            radial.HighlightSector(1, -1, false);
            Check(back!.Visibility == Visibility.Collapsed && exitIcon.Visibility == Visibility.Collapsed,
                "Hovering child sector hides _portalBack while showing selection overlay");
            radial.HighlightSector(-1, -1, false);
            Check(back.Visibility == Visibility.Visible && exitIcon.Visibility == Visibility.Collapsed,
                "Returning hover to child core restores _portalBack without exposing CoreExitIcon");

            CallRadial("ShowPortalFeedback", "PortalBlocked");
            Check(FieldRadial("_layerBadgeStoryboard") != null &&
                ((FrameworkElement)radial.FindName("LayerIndicatorBadge")).Visibility == Visibility.Visible,
                "ShowPortalFeedback manages LayerIndicatorBadge via _layerBadgeStoryboard");

            bool origShowLayer = config.ShowLayerIndicator;
            config.ShowLayerIndicator = false;
            var badge = (Border)radial.FindName("LayerIndicatorBadge");
            badge.Visibility = Visibility.Collapsed;
            badge.Background = null;
            CallRadial("ShowPortalFeedback", "PortalBlocked");
            Check(badge.Visibility == Visibility.Visible && badge.Background != null,
                "ShowPortalFeedback forces LayerIndicatorBadge styling even when ShowLayerIndicator is false");
            config.ShowLayerIndicator = origShowLayer;

            var captureMethod = radial.GetType().GetMethod("CapturePortalParentSnapshot", InstanceFlags)!;
            radial.UpdateLayout();
            var snapCore = captureMethod.Invoke(radial, ["ParentCore", 133.0, 0, -1])!;
            var coreImg = (ImageSource?)snapCore.GetType().GetField("Item1")!.GetValue(snapCore);
            var snapFade = captureMethod.Invoke(radial, ["FadedWheel", 133.0, 0, -1])!;
            var fadeImg = (ImageSource?)snapFade.GetType().GetField("Item1")!.GetValue(snapFade);
            Check(coreImg is { IsFrozen: true } && fadeImg is { IsFrozen: true },
                "CapturePortalParentSnapshot produces frozen snapshots for ParentCore and FadedWheel");

            CallRadial("DrawPortalTrail", stack, screen);
            var trail = (Window?)FieldRadial("_portalTrail");
            nint radialHwnd = new WindowInteropHelper(radial).Handle;
            nint insertAfter = (nint)trail!.GetType().GetField("_insertAfter", InstanceFlags)!.GetValue(trail)!;
            Check(trail.IsVisible && insertAfter == radialHwnd &&
                trail.Width <= screen.PhysicalWorkArea.Width / screen.DpiScale.DpiScaleX + 1,
                "PortalTrailOverlay inserts directly below RadialWindow within minimal monitor bounds");

            var overlayType = trail.GetType();
            var buildGeo = overlayType.GetMethod("BuildCustomMarkerGeometry", BindingFlags.Static | BindingFlags.NonPublic)!;
            foreach (string shape in new[] { "Circle", "FourPointStar", "Diamond", "Hexagon", "RoundedSquare" })
            {
                var geo = (Geometry)buildGeo.Invoke(null, [shape, new Point(32, 32), 20.0])!;
                Check(geo.IsFrozen && !geo.IsEmpty(), $"Custom marker geometry builds clean frozen shape for {shape}");
            }
            string origVisualMode = config.PortalParentVisualMode;
            bool origBeam = config.PortalTrailShowBeam;
            foreach (string visMode in new[] { "ParentCore", "FadedWheel", "CustomMarker", "OutlineCircle" })
            {
                config.PortalParentVisualMode = visMode;
                config.PortalCustomShape = "FourPointStar";
                config.PortalTrailShowBeam = visMode != "OutlineCircle";
                var visImg = visMode == "ParentCore" ? coreImg : visMode == "FadedWheel" ? fadeImg : null;
                var modeStack = new PortalSessionStack(new PortalFrame(source, center, 133, center, 30, visImg, 145),
                    ConfigManager.ConfigurationRevision, 5, null);
                modeStack.Push(childFrame);
                CallRadial("DrawPortalTrail", modeStack, screen);
                Check(trail.IsVisible, $"PortalTrailOverlay renders {visMode} mode cleanly");
            }
            config.PortalParentVisualMode = origVisualMode;
            config.PortalTrailShowBeam = origBeam;

            radial.Dismiss(9L);
            Check(FieldRadial("_portalBack") == null && !trail.IsVisible, "Dismiss clears _portalBack and hides PortalTrailOverlay");
        }
        finally
        {
            radial.CloseFast();
        }

        // Fix 5: SettingsWindow inspector FocusSubActionsBorder stays consistent when toggling OpenWheel.
        config.AutoCheckUpdate = false;
        var settings = new SettingsWindow();
        settings.EnsureUiInitialized();
        var subTarget = new ActionItem { Type = "Hotkey", Parameter = "Ctrl+V", Name = "Paste" };
        profile.Layers[0].Actions[2] = new ActionItem { Type = "Hotkey", Parameter = "Ctrl+X", Name = "Cut", SubActions = { subTarget } };
        ((ComboBox)settings.FindName("MappingsProfileComboBox")).SelectedItem = profile;
        ((ComboBox)settings.FindName("LayerSelectComboBox")).SelectedItem = profile.Layers[0];
        settings.SelectPrimarySlot(0); // OpenWheel slot
        var subActionsBorder = (Border)settings.FindName("FocusSubActionsBorder");
        Check(subActionsBorder.Visibility == Visibility.Collapsed,
            "Primary OpenWheel sector hides FocusSubActionsBorder");
        settings.SelectPrimarySlot(2); // Normal sector with child
        Check(subActionsBorder.Visibility == Visibility.Visible,
            "Selecting a normal sector restores FocusSubActionsBorder");
        subTarget.Type = "OpenWheel";
        settings.SelectSubAction(2, 0);
        Check(subActionsBorder.Visibility == Visibility.Visible,
            "Secondary OpenWheel sub-slot keeps parent sibling FocusSubActionsBorder visible");

        // P1-3 & P2-3: OpenWheel sector hides stashed SubActions from tier-2 navigation/edit buttons and SlotViewModel.
        var stashedSub = new ActionItem { Type = "Hotkey", Parameter = "Ctrl+A", Name = "StashedAll" };
        profile.Layers[0].Actions[0].SubActions = new List<ActionItem> { stashedSub };
        settings.SelectPrimarySlot(0);
        ((RadioButton)settings.FindName("MappingsTier2SegmentRadio")).IsChecked = true;
        int? selectedSubAfterTier2 = (int?)settings.GetType().GetField("_selectedSubActionIndex", InstanceFlags)!.GetValue(settings);
        Check(selectedSubAfterTier2 == null && profile.Layers[0].Actions[0].SubActions.Count == 1,
            "Switching MappingsTier2SegmentRadio on OpenWheel sector keeps _selectedSubActionIndex null and preserves stashed SubActions");
        ((Button)settings.FindName("FocusAddSubActionBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ((Button)settings.FindName("FocusClearSubActionsBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(profile.Layers[0].Actions[0].SubActions.Count == 1 && ReferenceEquals(profile.Layers[0].Actions[0].SubActions[0], stashedSub),
            "FocusAddSubActionBtn and FocusClearSubActionsBtn do not mutate stashed SubActions on OpenWheel sectors");
        ((Button)settings.FindName("FocusNextSlotBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        selectedSubAfterTier2 = (int?)settings.GetType().GetField("_selectedSubActionIndex", InstanceFlags)!.GetValue(settings);
        Check(selectedSubAfterTier2 == null && profile.Layers[0].Actions[0].SubActions.Count == 1,
            "FocusNextSlotBtn advances past OpenWheel without entering stashed SubActions");

        var vmPortal = new SlotViewModel(0, 8, "N", profile.Layers[0].Actions[0]);
        var vmNormal = new SlotViewModel(1, 8, "NE", new ActionItem { Type = "Hotkey", Name = "Normal" });
        Check(vmPortal.SubActionCount == 0 && vmPortal.ActionTypes.Any(o => o.Tag == "OpenWheel") &&
            vmNormal.SubActionCount == 0 && !vmNormal.ActionTypes.Any(o => o.Tag == "OpenWheel"),
            "SlotViewModel reports 0 SubActionCount on OpenWheel and opts ActionTypes into OpenWheel only for portal slots");

        // P2-4: PortalInitialRadius computes honeycomb FixedRings outer extent instead of legacy SecondaryExtent.
        bool origMultiTier = config.EnableMultiTier;
        bool origAutoExpand = config.AutoExpandSubRingsOnPopup;
        string origStyle = config.SubmenuStyle;
        config.EnableMultiTier = true;
        config.AutoExpandSubRingsOnPopup = true;
        config.SubmenuStyle = "Fan";
        var honeyLayer = new WheelLayer
        {
            SectorCount = 8,
            MaxMenuDepth = 3,
            FixedRings = new FixedRingMenu
            {
                AlwaysVisible = true,
                Tiers =
                {
                    new FixedRingTier
                    {
                        Count = 12,
                        Slots = { new FixedRingSlot { ParentIndex = 0, Action = new ActionItem { Type = "Hotkey", Name = "F3", Parameter = "F3" } } }
                    }
                }
            }
        };
        var honeyProfile = new WheelProfile { ProcessName = "honey.exe", Layers = { honeyLayer } };
        honeyProfile.EnsureLayers();
        honeyLayer.Actions[0] = new ActionItem { Type = "Hotkey", Name = "P", Parameter = "Ctrl+P", SubActions = { new ActionItem { Type = "Hotkey", Name = "S", Parameter = "Ctrl+S" } } };
        honeyProfile.SyncRootPropertiesFromActiveLayer();
        double honeyRadius = (double)typeof(RadialWindow).GetMethod("PortalInitialRadius", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [honeyProfile, screen])!;
        double legacySecondary = (double)typeof(RadialWindow).Assembly.GetType("WinPieGestures.LegacyRingGeometry")!
            .GetMethod("SecondaryExtent", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!
            .Invoke(null, [config, (double)config.WheelRadius, (double)config.InnerRadius])!;
        Check(honeyRadius > config.WheelRadius && Math.Abs(honeyRadius - legacySecondary) > 1.0,
            "PortalInitialRadius returns honeycomb outer extent rather than legacy SecondaryExtent for Fan+FixedRings");
        config.EnableMultiTier = origMultiTier;
        config.AutoExpandSubRingsOnPopup = origAutoExpand;
        config.SubmenuStyle = origStyle;

        // MappingsProfileRail anti-flicker layout: Card has transparent Background and CardActions uses Opacity rather than Visibility.Collapsed.
        var rail = new MappingsProfileRail { Width = 240, Height = 360 };
        rail.GetType().GetMethod("Refresh", InstanceFlags)!.Invoke(rail, [config, profile]);
        rail.Measure(new Size(240, 360));
        rail.Arrange(new Rect(0, 0, 240, 360));
        rail.UpdateLayout();
        Border? FindVisualBorder(DependencyObject root, string name)
        {
            if (root is Border b && b.Name == name) return b;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                if (FindVisualBorder(VisualTreeHelper.GetChild(root, i), name) is { } found) return found;
            return null;
        }
        var cardBorder = FindVisualBorder(rail, "Card");
        var cardActions = FindVisualBorder(rail, "CardActions");
        Check(cardBorder != null && cardBorder.Background != null && cardActions != null &&
            cardActions.Visibility == Visibility.Visible && cardActions.Opacity == 0,
            "MappingsProfileRail keeps Card hit-testable and hides CardActions via Opacity to avoid hover flicker");

        // Tab 2 PortalAppearance card and Global SettingsSearch integration.
        var modeCombo = (ComboBox)settings.FindName("PortalParentVisualModeCombo");
        var fadePanel = (FrameworkElement)settings.FindName("PortalFadeSettingsPanel");
        var customPanel = (FrameworkElement)settings.FindName("PortalCustomSettingsPanel");
        var shapeCombo = (ComboBox)settings.FindName("PortalCustomShapeCombo");
        var previewImg = (Image)settings.FindName("PortalCustomPreviewImage");
        modeCombo.SelectedValue = "FadedWheel";
        Check(config.PortalParentVisualMode == "FadedWheel" && fadePanel.Visibility == Visibility.Visible &&
            customPanel.Visibility == Visibility.Collapsed,
            "Tab 2 PortalParentVisualModeCombo switches to FadedWheel and reveals opacity panel");
        modeCombo.SelectedValue = "CustomMarker";
        shapeCombo.SelectedValue = "FourPointStar";
        Check(config.PortalParentVisualMode == "CustomMarker" && config.PortalCustomShape == "FourPointStar" &&
            customPanel.Visibility == Visibility.Visible && previewImg.Source != null,
            "Tab 2 CustomMarker switches shape to FourPointStar and renders live vector preview");
        modeCombo.SelectedValue = "ParentCore";

        var searchPanel = (FrameworkElement)settings.FindName("SettingsSearchPanel");
        var searchInput = (TextBox)settings.FindName("SettingsSearchInput");
        var searchResults = (ListBox)settings.FindName("SettingsSearchResults");
        searchPanel.Visibility = Visibility.Visible;
        searchInput.Text = I18n.T("PortalAppearanceTitle");
        Check(searchResults.Items.Count > 0, "Global settings search finds Tab 2 PortalAppearanceTitle");
        searchInput.Text = I18n.T("PortalTriggerTitle");
        Check(searchResults.Items.Count > 0, "Global settings search finds Tab 4 PortalTriggerTitle");
        searchInput.Text = I18n.T("PinnedNavKeysTitle");
        Check(searchResults.Items.Count > 0, "Global settings search finds Tab 1 PinnedNavKeysTitle");
        searchPanel.Visibility = Visibility.Collapsed;

        // Graphical pinned keyboard navigation editor in Tab 1 (TriggerCommonPanel).
        var upBtn = (Button)settings.FindName("PinnedNavUpKeyButton");
        var leftBtn = (Button)settings.FindName("PinnedNavLeftKeyButton");
        var downBtn = (Button)settings.FindName("PinnedNavDownKeyButton");
        var rightBtn = (Button)settings.FindName("PinnedNavRightKeyButton");
        var confirmBtn = (Button)settings.FindName("PinnedNavConfirmKeyButton");
        var resetNavBtn = (Button)settings.FindName("PinnedNavResetButton");
        var upGlyph = (TextBlock)settings.FindName("PinnedNavUpKeyGlyph");
        var confirmGlyph = (TextBlock)settings.FindName("PinnedNavConfirmKeyGlyph");
        var navStatus = (TextBlock)settings.FindName("PinnedNavStatusText");
        Check(upBtn != null && leftBtn != null && downBtn != null && rightBtn != null && confirmBtn != null &&
            upGlyph.Text == "↑" && confirmGlyph.Text == "Enter ↵" && !resetNavBtn.IsEnabled,
            "Graphical pinned nav keycap cluster initializes with default glyphs and disabled reset button");

        using var offscreenSource = new HwndSource(new HwndSourceParameters("starpie-offscreen") { WindowStyle = 0 });
        bool InvokeNavKey(Key key)
        {
            var args = new System.Windows.Input.KeyEventArgs(Keyboard.PrimaryDevice, offscreenSource, 0, key)
            {
                RoutedEvent = UIElement.PreviewKeyDownEvent
            };
            return (bool)typeof(SettingsWindow).GetMethod("TryHandlePinnedNavRecordingKeyDown", InstanceFlags)!.Invoke(settings, [args])!;
        }

        // Start recording Up, reject Ctrl modifier, cancel via Esc.
        typeof(SettingsWindow).GetMethod("PinnedNavKeyButton_Click", InstanceFlags)!.Invoke(settings, [upBtn, new RoutedEventArgs(Button.ClickEvent)]);
        Check(navStatus.Visibility == Visibility.Visible, "Clicking keycap opens recording prompt");
        Check(InvokeNavKey(Key.LeftCtrl) && config.PinnedNavUpVkCode == PinnedArrowNavigation.Up, "Modifier key alone is rejected during keycap recording");
        Check(InvokeNavKey(Key.Escape) && config.PinnedNavUpVkCode == PinnedArrowNavigation.Up, "Escape cancels keycap recording");

        // Record W for Up, then bind W to Confirm to verify automatic swap & distinctness.
        typeof(SettingsWindow).GetMethod("PinnedNavKeyButton_Click", InstanceFlags)!.Invoke(settings, [upBtn, new RoutedEventArgs(Button.ClickEvent)]);
        Check(InvokeNavKey(Key.W) && config.PinnedNavUpVkCode == 0x57 && upGlyph.Text == "W" && resetNavBtn.IsEnabled,
            "Single key W replaces Up keycap cleanly");
        typeof(SettingsWindow).GetMethod("PinnedNavKeyButton_Click", InstanceFlags)!.Invoke(settings, [confirmBtn, new RoutedEventArgs(Button.ClickEvent)]);
        Check(InvokeNavKey(Key.W) && config.PinnedNavConfirmVkCode == 0x57 && config.PinnedNavUpVkCode == PinnedArrowNavigation.Confirm &&
            confirmGlyph.Text == "W" && upGlyph.Text == "Enter ↵",
            "Rebinding duplicate key swaps keycaps to preserve distinct bindings");
        typeof(SettingsWindow).GetMethod("PinnedNavResetButton_Click", InstanceFlags)!.Invoke(settings, [resetNavBtn, new RoutedEventArgs(Button.ClickEvent)]);
        Check(config.PinnedNavUpVkCode == PinnedArrowNavigation.Up && config.PinnedNavConfirmVkCode == PinnedArrowNavigation.Confirm &&
            !resetNavBtn.IsEnabled, "Reset defaults restores standard arrow + Enter keybindings");

        // Offscreen verification of OfficialPluginsOnboardingDialog crisp ClearType rendering & effect-free ancestor tree.
        var onboarding = new OfficialPluginsOnboardingDialog();
        Check(TextOptions.GetTextFormattingMode(onboarding) == TextFormattingMode.Display &&
            TextOptions.GetTextRenderingMode(onboarding) == TextRenderingMode.ClearType &&
            RenderOptions.GetClearTypeHint(onboarding) == ClearTypeHint.Enabled &&
            onboarding.UseLayoutRounding && onboarding.SnapsToDevicePixels,
            "OfficialPluginsOnboardingDialog matches SettingsWindow crisp Display+ClearType typography settings");
        foreach (string ctrlName in new[] { "TitleTextBlock", "IntroTextBlock", "ChannelLabelTextBlock", "ChannelComboBox", "InstallButton" })
        {
            DependencyObject? cur = (DependencyObject)onboarding.FindName(ctrlName)!;
            while (cur != null)
            {
                if (cur is UIElement ui)
                    Check(ui.Effect == null, $"OfficialPluginsOnboardingDialog ancestor of {ctrlName} ({ui.GetType().Name}) has no ShaderEffect blur");
                cur = VisualTreeHelper.GetParent(cur) ?? LogicalTreeHelper.GetParent(cur);
            }
        }
        var comboBox = (ComboBox)onboarding.FindName("ChannelComboBox")!;
        comboBox.ApplyTemplate();
        var dropDownBorder = (Border)comboBox.Template.FindName("dropDownBorder", comboBox)!;
        Check(dropDownBorder.Effect == null, "OfficialPluginsOnboardingDialog ComboBox dropdown border separates shadow from text");

        T Find<T>(string name) where T : FrameworkElement => (T)editor.FindName(name);
    }

    private static void Check(bool result, string name)
    {
        if (!result) throw new InvalidOperationException(name);
        Console.WriteLine($"PASS {++_passed} {name}");
    }

    private static double Luminance(Color color)
    {
        static double Linear(byte channel)
        {
            double value = channel / 255.0;
            return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
        }
        return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
    }
}
