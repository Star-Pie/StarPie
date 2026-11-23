using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace WinPieGestures;

public partial class SettingsWindow
{
    private enum AppearancePreviewKind { Configure, Popup, Effects }

    private sealed record AppearanceField(string Caption, double Value, double Min, double Max,
        Action<double> Set, AppearanceDimension? Handle = null)
    {
        public Func<(double Value, double Min, double Max)>? ReadBounds { get; init; }

        public static AppearanceField Dependent(string caption, Func<(double Value, double Min, double Max)> readBounds,
            Action<double> set, AppearanceDimension? handle = null)
        {
            var (value, min, max) = readBounds();
            return new(caption, value, min, max, set, handle) { ReadBounds = readBounds };
        }
    }

    private static readonly (string Value, string Caption)[] WheelShapes =
    [
        ("Original", "CutStyleItemClassic"),
        ("Circle", "CutStyleItemCircles"),
        ("RoundedCapsule", "CutStyleItemCapsules"),
        ("HexagonHive", "CutStyleItemHexagons")
    ];

    private AppearancePreviewKind _appearancePreview;
    private bool _appearanceSidebarNeedsRefresh = true;
    private bool _appearanceInitialized;
    private bool _appearanceComparing;
    private int _appearanceDepth = 2;
    private int _appearanceLevel = 1;
    private AppConfig? _appearanceSidebarConfig;
    private Slider? _appearanceHandleSlider;
    private AppearanceField? _appearanceHandleField;
    private readonly List<Action> _refreshAppearanceBounds = [];

    private bool CanEditWheelAppearance =>
        _isUiInitialized && !_isUpdatingUi && !_resourcesReleased && AppearanceSettingsGrid.Visibility == Visibility.Visible &&
        ReferenceEquals(_appearanceSidebarConfig, ConfigManager.CurrentConfig);

    private WheelProfile? AppearanceProfile(AppConfig config) => _selectedProfile ?? config.Profiles.FirstOrDefault();

    private void RefreshWheelAppearance()
    {
        RefreshWheelAppearanceSidebar();
        RefreshWheelAppearancePreview();
    }

    private void RefreshWheelAppearanceSidebar()
    {
        if (WheelAppearanceFields == null || AppearanceSettingsGrid.Visibility != Visibility.Visible ||
            ConfigManager.CurrentConfig is not { } config) return;

        if (!_appearanceInitialized)
        {
            _appearanceInitialized = true;
            var profile = AppearanceProfile(config);
            int activeDepth = profile?.Layers.ElementAtOrDefault(profile.ActiveLayerIndex)?.EffectiveMaxMenuDepth ?? 2;
            _appearanceDepth = config.EnableMultiTier ? Math.Clamp(activeDepth, 2, 5) : 1;
            AppearanceGeometryCanvas.LevelSelected += SelectAppearanceLevel;
            AppearanceGeometryCanvas.HandleChanged += value =>
            {
                if (CanEditWheelAppearance && _appearanceHandleSlider is { } slider)
                    slider.Value = value;
            };
        }

        _appearanceSidebarConfig = config;
        _appearanceSidebarNeedsRefresh = false;
        bool effects = _appearancePreview == AppearancePreviewKind.Effects;
        WheelAppearanceSidebar.Visibility = effects ? Visibility.Collapsed : Visibility.Visible;
        LegacyWheelDimensions.Visibility = effects ? Visibility.Visible : Visibility.Collapsed;
        AppearanceGeometryStage.Visibility = effects ? Visibility.Collapsed : Visibility.Visible;
        ResetDimensionsButton.Visibility = effects ? Visibility.Visible : Visibility.Collapsed;
        Tab1_LivePreviewHint.Visibility = effects ? Visibility.Visible : Visibility.Collapsed;
        PreviewPanHintText.Visibility = effects ? Visibility.Visible : Visibility.Collapsed;
        LivePreviewTierSegmentBorder.Visibility = effects && config.SubmenuStyle == "Fan"
            ? Visibility.Visible : Visibility.Collapsed;

        AppearancePreviewChoices.Children.Clear();
        AddAppearanceButton(AppearancePreviewChoices, "WheelAppearanceConfigure",
            _appearancePreview == AppearancePreviewKind.Configure, () => ChangeAppearancePreview(AppearancePreviewKind.Configure));
        AddAppearanceButton(AppearancePreviewChoices, "WheelAppearancePopup",
            _appearancePreview == AppearancePreviewKind.Popup, () => ChangeAppearancePreview(AppearancePreviewKind.Popup));
        AddAppearanceButton(AppearancePreviewChoices, "WheelAppearanceEffects", effects,
            () => ChangeAppearancePreview(AppearancePreviewKind.Effects));

        AppearancePreviewHint.Visibility = effects ? Visibility.Collapsed : Visibility.Visible;
        var profileForPreview = AppearanceProfile(config);
        var activeLayer = profileForPreview?.Layers.ElementAtOrDefault(profileForPreview.ActiveLayerIndex);
        string previewHint = "WheelAppearanceCanvasHint";
        if (_appearancePreview == AppearancePreviewKind.Popup)
        {
            var fixedMenu = _appearanceDepth > 2 ? activeLayer?.ActiveFixedRings : null;
            previewHint = _appearanceDepth > 2 && fixedMenu == null ? "WheelAppearancePopupFixedHint" :
                config.EnableMultiTier && fixedMenu is { AlwaysVisible: false } ? "WheelAppearancePopupNearbyHint" :
                "WheelAppearancePopupHint";
        }
        AppearancePreviewHint.Text = I18n.T(previewHint);

        WheelAppearanceFields.Children.Clear();
        _refreshAppearanceBounds.Clear();
        if (effects) return;
        AddAppearanceText(WheelAppearanceFields, "WheelAppearanceTitle", title: true);
        var depth = new UniformGrid { Columns = 5, Margin = new Thickness(0, 3, 0, 4) };
        WheelAppearanceFields.Children.Add(depth);
        for (int level = 1; level <= 5; level++)
        {
            int count = level;
            AddAppearanceButton(depth, I18n.TF("WheelAppearanceLevelCount", count), count == _appearanceDepth,
                () => ChangeAppearanceDepth(count), localized: true);
            AutomationProperties.SetAutomationId(depth.Children[depth.Children.Count - 1], $"WheelAppearanceDepth{count}");
        }
        AddAppearanceText(WheelAppearanceFields, "WheelAppearanceDepthHint");
        AddAppearanceText(WheelAppearanceFields, "WheelAppearanceStructure", title: true);
        var layouts = AppearanceButtons(WheelAppearanceFields);
        AddAppearanceButton(layouts, "SubmenuStyleWheel", config.SubmenuStyle != "Fan", () => ChangeAppearanceLayout("Wheel"));
        AddAppearanceButton(layouts, "SubmenuStyleFan", config.SubmenuStyle == "Fan", () => ChangeAppearanceLayout("Fan"));

        AddAppearanceText(WheelAppearanceFields, "WheelAppearancePresets", title: true);
        var presets = AppearanceButtons(WheelAppearanceFields);
        AddAppearanceButton(presets, "WheelAppearanceCompact", false, () => ApplyAppearancePreset(.82));
        AddAppearanceButton(presets, "WheelAppearanceBalanced", false, () => ApplyAppearancePreset(1));
        AddAppearanceButton(presets, "WheelAppearanceSpacious", false, () => ApplyAppearancePreset(1.16));

        AddAppearanceText(WheelAppearanceFields, "WheelAppearanceScope", title: true);
        var scopes = AppearanceButtons(WheelAppearanceFields);
        for (int level = 0; level <= _appearanceDepth; level++)
        {
            int choice = level;
            AddAppearanceButton(scopes, AppearanceScopeCaption(choice), choice == _appearanceLevel,
                () => SelectAppearanceLevel(choice), localized: true);
        }
        var actions = AppearanceButtons(WheelAppearanceFields);
        AddAppearanceButton(actions, "WheelAppearanceResetScope", false, ResetAppearanceScope);
        AddAppearanceButton(actions, _appearanceComparing ? "WheelAppearanceReturn" : "WheelAppearanceCompare",
            _appearanceComparing, () =>
            {
                _appearanceComparing = !_appearanceComparing;
                RefreshWheelAppearance();
            });
        BuildAppearanceFields(config);
    }

    private void ChangeAppearancePreview(AppearancePreviewKind preview)
    {
        _appearancePreview = preview;
        _appearanceComparing = false;
        RefreshWheelAppearance();
        if (preview == AppearancePreviewKind.Effects) RenderLiveWheelPreview();
    }

    private void ChangeAppearanceDepth(int depth)
    {
        _appearanceDepth = depth;
        _appearanceLevel = Math.Min(_appearanceLevel, depth);
        if (IsAppearanceHoneycomb(ConfigManager.CurrentConfig)) _appearanceLevel = 0;
        _appearanceComparing = false;
        RefreshWheelAppearance();
    }

    private void ChangeAppearanceLayout(string style)
    {
        var cfg = ConfigManager.CurrentConfig;
        if (!CanEditWheelAppearance || cfg.SubmenuStyle == style || AppearanceProfile(cfg) is not { } profile) return;
        ChangeMenuLayout(profile, profile.GetActiveLayer().MaxMenuDepth, style);
        if (IsAppearanceHoneycomb(cfg)) _appearanceLevel = 0;
        _appearanceComparing = false;
        RefreshWheelAppearance();
    }

    private void SelectAppearanceLevel(int level)
    {
        if (_appearancePreview != AppearancePreviewKind.Configure) return;
        _appearanceLevel = Math.Clamp(level, 0, _appearanceDepth);
        _appearanceComparing = false;
        RefreshWheelAppearance();
    }

    private static string AppearanceScopeCaption(int level) => level switch
    {
        0 => I18n.T("WheelAppearanceWhole"),
        1 => I18n.T("WheelAppearancePrimary"),
        2 => I18n.T("WheelAppearanceSecondary"),
        _ => I18n.TF("WheelAppearanceLevel", level)
    };

    private bool IsAppearanceHoneycomb(AppConfig cfg) => _appearanceDepth > 2 && cfg.SubmenuStyle == "Fan";

    private void RefreshWheelAppearancePreview()
    {
        if (AppearanceGeometryCanvas == null || AppearanceSettingsGrid.Visibility != Visibility.Visible ||
            ConfigManager.CurrentConfig is not { } cfg) return;
        if (_appearanceSidebarNeedsRefresh || !ReferenceEquals(_appearanceSidebarConfig, cfg)) RefreshWheelAppearanceSidebar();
        if (_appearancePreview == AppearancePreviewKind.Effects) return;

        var drawn = _appearanceComparing ? new AppConfig
        {
            SubmenuStyle = cfg.SubmenuStyle, ShowCoreCircle = cfg.ShowCoreCircle,
            EnableMultiTier = cfg.EnableMultiTier, AutoExpandSubRingsOnPopup = cfg.AutoExpandSubRingsOnPopup
        } : cfg;
        AppearanceHandle? handle = null;
        if (!_appearanceComparing && _appearanceHandleField is { Handle: { } dimension } field &&
            _appearanceHandleSlider is { } slider)
            handle = new AppearanceHandle(dimension, slider.Value, slider.Minimum, slider.Maximum, I18n.T(field.Caption));
        AppearanceGeometryCanvas.Render(drawn, AppearanceProfile(cfg), _appearanceDepth, _appearanceLevel,
            _appearancePreview == AppearancePreviewKind.Popup, PreviewScaleTransform?.ScaleX ?? 1, handle);
    }

    private void BuildAppearanceFields(AppConfig cfg)
    {
        _appearanceHandleSlider = null;
        _appearanceHandleField = null;
        var panel = WheelAppearanceFields;
        bool honey = IsAppearanceHoneycomb(cfg);
        int level = _appearanceLevel;
        if (honey)
        {
            AddAppearanceText(panel, "WheelAppearanceHoneyHint");
            if (level != 0)
            {
                var shortcut = AppearanceButtons(panel);
                AddAppearanceButton(shortcut, "WheelAppearanceWhole", false, () => SelectAppearanceLevel(0));
                return;
            }
            var values = FixedRingGeometry.HoneycombDefaults(cfg);
            AddAppearanceShapes(panel, WheelShapes.Where(item => item.Value is "Circle" or "HexagonHive"), values.Shape,
                shape => (cfg.FixedHoneycombAppearance ??= new()).Shape = shape);
            AddAppearanceField(panel, new("WheelAppearanceCellWidth", values.CellWidth, 4, 120,
                x => (cfg.FixedHoneycombAppearance ??= new()).CellWidth = x, AppearanceDimension.HoneycombCellSize));
            AddAppearanceField(panel, new("WheelAppearanceCellGap", values.CellGap, 0, 60,
                x => (cfg.FixedHoneycombAppearance ??= new()).CellGap = x, AppearanceDimension.HoneycombSpacing));
            if (cfg.ShowCoreCircle)
                AddAppearanceField(panel, new("WheelAppearanceCore", values.CoreRadius, 2, 130,
                    x => (cfg.FixedHoneycombAppearance ??= new()).CoreRadius = x, AppearanceDimension.CoreRadius));
            if (values.Shape != "Circle") AddAppearanceFineFields(panel, new AppearanceField("WheelAppearanceCorner", values.CornerRadius,
                0, 40, x => (cfg.FixedHoneycombAppearance ??= new()).CornerRadius = x));
            return;
        }

        if (level == 0)
        {
            if (cfg.ShowCoreCircle)
                AddAppearanceField(panel, new("WheelAppearanceCore", cfg.CoreRadius, 15, cfg.InnerRadius,
                    x => cfg.CoreRadius = x, AppearanceDimension.CoreRadius));
            if (_appearanceDepth <= 2) return;

            AddAppearanceText(panel, "WheelAppearanceDeepDefaults", title: true);
            var values = FixedRingGeometry.FixedTierDefaults(cfg);
            AddAppearanceShapes(panel, WheelShapes, values.Shape, shape => (cfg.FixedOuterRingAppearance ??= new()).Shape = shape);
            AddAppearanceField(panel, new("WheelAppearanceThickness", values.Thickness, 2, 160,
                x => (cfg.FixedOuterRingAppearance ??= new()).Thickness = x, AppearanceDimension.RingThickness));
            AddAppearanceField(panel, new("WheelAppearanceGap", values.SectorGap, 0, 40,
                x => (cfg.FixedOuterRingAppearance ??= new()).SectorGap = x));
            AddAppearanceField(panel, new("WheelAppearanceRingGap", values.RingGap, 0, 60,
                x => (cfg.FixedOuterRingAppearance ??= new()).RingGap = x, AppearanceDimension.RingSpacing));
            if (values.Shape != "Circle") AddAppearanceFineFields(panel, new AppearanceField("WheelAppearanceCorner", values.CornerRadius,
                0, 40, x => (cfg.FixedOuterRingAppearance ??= new()).CornerRadius = x));
            return;
        }

        if (level == 1)
        {
            AddAppearanceShapes(panel, WheelShapes, cfg.Shape, shape => SetAppearancePrimaryShape(cfg, shape));
            AddAppearanceField(panel, AppearanceField.Dependent("WheelAppearanceThickness",
                () => (cfg.WheelRadius - cfg.InnerRadius, Math.Max(18, 70 - cfg.InnerRadius), 280 - cfg.InnerRadius),
                x => SetAppearancePrimaryRadius(cfg, cfg.InnerRadius + x), AppearanceDimension.OuterRadius));
            AddAppearanceField(panel, new("WheelAppearanceGap", cfg.SectorGap, 0, 12, x => cfg.SectorGap = x));
            AddAppearanceFineFields(panel,
                AppearanceField.Dependent("WheelAppearanceInner", () => (cfg.InnerRadius,
                    Math.Max(20, cfg.CoreRadius), cfg.WheelRadius - 18), x => cfg.InnerRadius = x, AppearanceDimension.InnerRadius),
                new("WheelAppearanceCorner", cfg.SectorCornerRadius, 0, 16, x => cfg.SectorCornerRadius = x));
            return;
        }

        if (level == 2)
        {
            AddAppearanceShapes(panel, new[] { ("FollowPrimary", "WheelAppearanceShapeInherit") }.Concat(WheelShapes),
                cfg.SubWheelShape ?? "FollowPrimary", shape => cfg.SubWheelShape = shape);
            bool fan = cfg.SubmenuStyle == "Fan";
            double Outer() => cfg.SubWheelOuterRadius > 0 ? cfg.SubWheelOuterRadius : cfg.WheelRadius * 1.55;
            double Gap() => cfg.SubWheelInnerGap >= 0 ? cfg.SubWheelInnerGap : fan ? 4 : cfg.SectorGap;
            AddAppearanceField(panel, AppearanceField.Dependent("WheelAppearanceSecondarySize",
                () => (Outer(), fan ? 120 : Math.Max(120, cfg.WheelRadius + Gap() + 3), 480),
                x => cfg.SubWheelOuterRadius = x, AppearanceDimension.SecondaryRadius));
            AddAppearanceField(panel, AppearanceField.Dependent("WheelAppearanceSecondaryGap",
                () => (Gap(), 0, fan ? 20 : Math.Min(20, Math.Max(0, Outer() - cfg.WheelRadius - 3))),
                x => cfg.SubWheelInnerGap = x));
            AddAppearanceFineFields(panel, new AppearanceField("WheelAppearanceCorner",
                cfg.SubWheelCornerRadius >= 0 ? cfg.SubWheelCornerRadius : cfg.SectorCornerRadius, 0, 20, x => cfg.SubWheelCornerRadius = x));
            return;
        }

        BuildAppearanceTierFields(cfg, level);
    }

    private void BuildAppearanceTierFields(AppConfig cfg, int level)
    {
        var panel = WheelAppearanceFields;
        var defaults = FixedRingGeometry.FixedTierDefaults(cfg);
        var tier = cfg.FixedOuterRingAppearance?.GetTier(level);
        var toggle = new CheckBox
        {
            Content = I18n.T("WheelAppearanceTierOverride"), IsChecked = tier != null,
            Margin = new Thickness(0, 7, 0, 6)
        };
        toggle.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
        AutomationProperties.SetAutomationId(toggle, "WheelAppearanceTierOverride");
        toggle.Click += (_, _) =>
        {
            if (!CanEditWheelAppearance) return;
            SetAppearanceTier(cfg.FixedOuterRingAppearance ??= new(), level, toggle.IsChecked == true
                ? new() { Thickness = defaults.Thickness, SectorGap = defaults.SectorGap } : null);
            FinishAppearanceEdit(rebuild: true);
        };
        panel.Children.Add(toggle);
        if (tier == null)
        {
            AddAppearanceText(panel, "WheelAppearanceTierInherited");
            return;
        }
        AddAppearanceField(panel, new("WheelAppearanceThickness", tier.Thickness ?? defaults.Thickness, 2, 160,
            value => tier.Thickness = value, AppearanceDimension.RingThickness));
        AddAppearanceField(panel, new("WheelAppearanceGap", tier.SectorGap ?? defaults.SectorGap, 0, 40, value => tier.SectorGap = value));
    }

    private static void SetAppearancePrimaryShape(AppConfig cfg, string shape)
    {
        if (cfg.Shape == shape) return;
        // Preserve the old secondary outline on the first separate primary edit.
        if (string.IsNullOrEmpty(cfg.SubWheelShape)) cfg.SubWheelShape = LegacyRingGeometry.SecondaryShape(cfg);
        cfg.Shape = shape;
    }

    private static void SetAppearancePrimaryRadius(AppConfig cfg, double outer)
    {
        double previous = cfg.WheelRadius;
        // The stored secondary radius is absolute. Its radial thickness is relative to the primary rim.
        if (cfg.SubmenuStyle != "Fan")
        {
            double oldSecondary = cfg.SubWheelOuterRadius > 0 ? cfg.SubWheelOuterRadius : previous * 1.55;
            double gap = cfg.SubWheelInnerGap >= 0 ? cfg.SubWheelInnerGap : Math.Max(0, cfg.SectorGap);
            cfg.SubWheelOuterRadius = Math.Max(outer + gap + 3, oldSecondary + outer - previous);
        }
        cfg.WheelRadius = outer;
    }

    private static void SetAppearanceTier(FixedOuterRingAppearance appearance, int level, FixedRingAppearanceTier? tier)
    {
        switch (level)
        {
            case 3: appearance.Level3 = tier; break;
            case 4: appearance.Level4 = tier; break;
            case 5: appearance.Level5 = tier; break;
        }
    }

    private void AddAppearanceShapes(Panel panel, IEnumerable<(string Value, string Caption)> options, string current, Action<string> select)
    {
        AddAppearanceText(panel, "WheelAppearanceShape", title: true);
        var choices = new ComboBox { MinHeight = 29, Margin = new Thickness(0, 4, 0, 7) };
        choices.SetResourceReference(StyleProperty, "FlatComboBoxStyle");
        AutomationProperties.SetName(choices, I18n.T("WheelAppearanceShape"));
        AutomationProperties.SetAutomationId(choices, "WheelAppearanceShapeChoice");
        foreach (var (value, caption) in options)
            choices.Items.Add(new ComboBoxItem { Content = I18n.T(caption), Tag = value, IsSelected = current == value });
        if (choices.SelectedIndex < 0) choices.SelectedIndex = 0;
        choices.SelectionChanged += (_, _) =>
        {
            if (!CanEditWheelAppearance || choices.SelectedItem is not ComboBoxItem { Tag: string value }) return;
            select(value);
            FinishAppearanceEdit(rebuild: true);
        };
        panel.Children.Add(choices);
    }

    private void AddAppearanceField(Panel panel, AppearanceField field)
    {
        double current = double.IsFinite(field.Value) ? field.Value : field.Min;
        double min = Math.Min(current, Math.Min(field.Min, field.Max));
        double max = Math.Max(current, Math.Max(field.Min, field.Max));
        string caption = I18n.T(field.Caption);
        var row = new DockPanel { Margin = new Thickness(0, 5, 0, 0) };
        var edit = new TextBox { Width = 60, Text = current.ToString("0.#", CultureInfo.CurrentCulture),
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(6, 0, 0, 0) };
        AutomationProperties.SetName(edit, caption);
        DockPanel.SetDock(edit, Dock.Right);
        row.Children.Add(edit);
        row.Children.Add(new TextBlock { Text = caption, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });

        var slider = new Slider { Minimum = min, Maximum = max, Value = current, SmallChange = 1, LargeChange = 5,
            Margin = new Thickness(0, 3, 0, 3) };
        if (field.ReadBounds is { } readBounds)
        {
            _refreshAppearanceBounds.Add(() =>
            {
                var (value, lower, upper) = readBounds();
                SetSliderValueWithBounds(slider, value, Math.Min(lower, upper), Math.Max(lower, upper), resetBoundsToDefault: true);
                edit.Text = slider.Value.ToString("0.#", CultureInfo.CurrentCulture);
            });
        }
        AutomationProperties.SetName(slider, caption);
        AutomationProperties.SetAutomationId(slider, field.Caption + "Slider");
        if (field.Handle is not null && _appearanceHandleSlider == null)
        {
            _appearanceHandleSlider = slider;
            _appearanceHandleField = field;
        }

        void ActivateHandle()
        {
            if (field.Handle is null) return;
            _appearanceHandleSlider = slider;
            _appearanceHandleField = field;
            RefreshWheelAppearancePreview();
        }
        slider.GotKeyboardFocus += (_, _) => ActivateHandle();
        edit.GotKeyboardFocus += (_, _) => ActivateHandle();
        slider.ValueChanged += (_, args) =>
        {
            if (!CanEditWheelAppearance) return;
            double value = Math.Clamp(Math.Round(args.NewValue, 1), slider.Minimum, slider.Maximum);
            double previous = Math.Clamp(Math.Round(args.OldValue, 1), slider.Minimum, slider.Maximum);
            if (value == previous) return;
            field.Set(value);
            edit.Text = value.ToString("0.#", CultureInfo.CurrentCulture);
            FinishAppearanceEdit();
        };
        void CommitNumber()
        {
            string displayed = slider.Value.ToString("0.#", CultureInfo.CurrentCulture);
            if (edit.Text == displayed) return;
            if (double.TryParse(edit.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value) && double.IsFinite(value))
                slider.Value = Math.Clamp(Math.Round(value, 1), slider.Minimum, slider.Maximum);
            edit.Text = slider.Value.ToString("0.#", CultureInfo.CurrentCulture);
        }
        edit.LostKeyboardFocus += (_, _) => CommitNumber();
        edit.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter) return;
            CommitNumber();
            args.Handled = true;
        };
        panel.Children.Add(row);
        panel.Children.Add(slider);
    }

    private void AddAppearanceFineFields(Panel panel, params AppearanceField[] fields)
    {
        var body = new StackPanel { Margin = new Thickness(0, 3, 0, 0) };
        foreach (var field in fields) AddAppearanceField(body, field);
        panel.Children.Add(new Expander { Header = I18n.T("WheelAppearanceFine"), Content = body, Margin = new Thickness(0, 6, 0, 0) });
    }

    private static WrapPanel AppearanceButtons(Panel parent)
    {
        var row = new WrapPanel { Margin = new Thickness(0, 3, 0, 4) };
        parent.Children.Add(row);
        return row;
    }

    private static void AddAppearanceText(Panel parent, string key, bool title = false)
    {
        var label = new TextBlock
        {
            Text = I18n.T(key), FontSize = title ? 13.5 : 11, TextWrapping = TextWrapping.Wrap,
            FontWeight = title ? FontWeights.SemiBold : FontWeights.Normal, Margin = new Thickness(0, 5, 0, 2)
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, title ? "TextPrimaryBrush" : "TextSecondaryBrush");
        parent.Children.Add(label);
    }

    private void AddAppearanceButton(Panel parent, string caption, bool selected, Action click, bool localized = false)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = localized ? caption : I18n.T(caption), TextWrapping = TextWrapping.Wrap },
            Margin = new Thickness(0, 0, 5, 3), Padding = new Thickness(8, 4, 8, 4)
        };
        if (!localized) AutomationProperties.SetAutomationId(button, caption);
        button.SetResourceReference(StyleProperty, selected ? "PrimaryButtonStyle" : "ModernButtonStyle");
        button.Click += (_, _) => { if (CanEditWheelAppearance) click(); };
        parent.Children.Add(button);
    }

    private void ResetAppearanceScope()
    {
        var cfg = ConfigManager.CurrentConfig;
        var defaults = new AppConfig();
        if (IsAppearanceHoneycomb(cfg))
        {
            if (_appearanceLevel != 0) return;
            cfg.FixedHoneycombAppearance = null;
        }
        else switch (_appearanceLevel)
        {
            case 0:
                cfg.CoreRadius = Math.Min(defaults.CoreRadius, cfg.InnerRadius);
                if (_appearanceDepth > 2 && cfg.FixedOuterRingAppearance is { } ring)
                {
                    ring.Shape = null;
                    ring.Thickness = ring.SectorGap = ring.RingGap = ring.CornerRadius = null;
                }
                break;
            case 1:
                SetAppearancePrimaryShape(cfg, defaults.Shape);
                cfg.InnerRadius = Math.Max(cfg.CoreRadius, defaults.InnerRadius);
                SetAppearancePrimaryRadius(cfg, Math.Max(cfg.InnerRadius + 18, defaults.WheelRadius));
                cfg.SectorGap = defaults.SectorGap;
                cfg.SectorCornerRadius = defaults.SectorCornerRadius;
                break;
            case 2:
                cfg.SubWheelShape = "FollowPrimary";
                cfg.SubWheelOuterRadius = cfg.SubmenuStyle == "Fan" ? defaults.SubWheelOuterRadius
                    : Math.Max(defaults.SubWheelOuterRadius, cfg.WheelRadius + defaults.SubWheelInnerGap + 3);
                cfg.SubWheelInnerGap = defaults.SubWheelInnerGap;
                cfg.SubWheelCornerRadius = defaults.SubWheelCornerRadius;
                break;
            default:
                if (cfg.FixedOuterRingAppearance is { } custom) SetAppearanceTier(custom, _appearanceLevel, null);
                break;
        }
        FinishAppearanceEdit(rebuild: true);
    }

    private void ApplyAppearancePreset(double factor)
    {
        var cfg = ConfigManager.CurrentConfig;
        var standard = new AppConfig();
        double Scaled(double value) => Math.Round(value * factor);
        if (IsAppearanceHoneycomb(cfg))
        {
            var values = FixedRingGeometry.HoneycombDefaults(standard);
            var honey = cfg.FixedHoneycombAppearance ??= new();
            honey.CellWidth = Scaled(values.CellWidth);
            honey.CellGap = Scaled(values.CellGap);
            honey.CoreRadius = Scaled(values.CoreRadius);
        }
        else if (_appearanceDepth > 2)
        {
            var values = FixedRingGeometry.FixedTierDefaults(standard);
            var ring = cfg.FixedOuterRingAppearance ??= new();
            ring.Thickness = Scaled(values.Thickness);
            ring.SectorGap = Scaled(values.SectorGap);
            ring.RingGap = Scaled(values.RingGap);
        }
        else
        {
            cfg.WheelRadius = Scaled(standard.WheelRadius);
            cfg.InnerRadius = Scaled(standard.InnerRadius);
            cfg.CoreRadius = Scaled(standard.CoreRadius);
            cfg.SectorGap = Scaled(standard.SectorGap);
            if (_appearanceDepth == 2)
            {
                cfg.SubWheelOuterRadius = Scaled(standard.SubWheelOuterRadius);
                cfg.SubWheelInnerGap = Scaled(standard.SubWheelInnerGap);
            }
        }
        FinishAppearanceEdit(rebuild: true);
    }

    private void FinishAppearanceEdit(bool rebuild = false)
    {
        _appearanceComparing = false;
        var cfg = ConfigManager.CurrentConfig;
        bool updating = _isUpdatingUi;
        _isUpdatingUi = true;
        try
        {
            SyncDimensionSliderBounds(cfg.WheelRadius, cfg.InnerRadius, cfg.CoreRadius, cfg.SubWheelOuterRadius, false);
            WheelRadiusSlider.Value = cfg.WheelRadius;
            InnerRadiusSlider.Value = cfg.InnerRadius;
            CoreRadiusSlider.Value = cfg.CoreRadius;
            SetSliderValueWithBounds(SectorGapSlider, cfg.SectorGap, 0, 12, resetBoundsToDefault: false);
            SetSliderValueWithBounds(SectorCornerRadiusSlider, cfg.SectorCornerRadius, 0, 16, resetBoundsToDefault: false);
            if (cfg.SubWheelOuterRadius > 0) SubWheelOuterRadiusSlider.Value = cfg.SubWheelOuterRadius;
            if (cfg.SubWheelInnerGap >= 0) SetSliderValueWithBounds(SubWheelInnerGapSlider, cfg.SubWheelInnerGap, 0, 20, false);
            if (cfg.SubWheelCornerRadius >= 0) SetSliderValueWithBounds(SubWheelCornerRadiusSlider, cfg.SubWheelCornerRadius, 0, 20, false);
            SetComboBoxSelectedValue(ShapeComboBox, cfg.Shape);
            foreach (var refresh in _refreshAppearanceBounds) refresh();
        }
        finally { _isUpdatingUi = updating; }

        _fixedGeometry = null;
        ScheduleAutoSave();
        if (rebuild) RefreshWheelAppearanceSidebar();
        RefreshWheelAppearancePreview();
    }
}
