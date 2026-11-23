using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using WinPieGestures.Plugins;

namespace WinPieGestures;

public partial class SettingsWindow
{
    private sealed record PortalEditLocation(PortalLayerAddress Address, int Sector, int? Secondary, int? FixedLevel,
        int FixedIndex, int[]? Branch, int? DeepSlot);
    private readonly Stack<PortalEditLocation> _portalEditBack = new();
    private PortalEditLocation? _portalPreviewLocation;
    private WheelPortalTarget? _portalPreviewTarget;

    private PortalEditorContext? CreatePortalEditorContext(WheelProfile? profile)
    {
        if (profile == null || GestureController.CaptureReleaseLayer(profile) is not { } layer) return null;
        return new(ConfigManager.CurrentConfig, new(profile, layer), JumpToPortalDestination, TogglePortalPreview);
    }

    private void BindFocusPortal(ActionItem item)
    {
        var profile = _selectedProfile;
        var context = _selectedSlotIndex < 0 ? null : CreatePortalEditorContext(profile);
        if (item.IsWheelPortal && _selectedSubActionIndex == null && FocusSubActionsBorder != null)
            FocusSubActionsBorder.Visibility = Visibility.Collapsed;
        int sector = _selectedSlotIndex;
        int? sub = _selectedSubActionIndex;
        FocusPortalEditor.Bind(context, item, target =>
        {
            if (_isUpdatingUi || _isUpdatingFocusUi || context == null || !ReferenceEquals(profile, _selectedProfile) ||
                sector != _selectedSlotIndex || sub != _selectedSubActionIndex || !ReferenceEquals(context.Config, ConfigManager.CurrentConfig) ||
                !ReferenceEquals(context.Source.Layer, GestureController.CaptureReleaseLayer(_selectedProfile))) return;
            if (GetCurrentFocusActionItem(ensureLocalForEdit: true) is not { IsWheelPortal: true } action) return;
            PortalTargetEditor.ApplyTarget(action, target);
            RecordFocusPropertyChange("EditActionProperty", coalesce: false);
            UpdateFocusEditorUi();
            RenderMappingsWheelPreview();
            RefreshStudioRail();
            ScheduleAutoSave();
        });
    }

    private PortalEditLocation? CapturePortalLocation()
    {
        var profile = _selectedProfile;
        var layer = GestureController.CaptureReleaseLayer(profile);
        if (profile == null || layer == null) return null;
        return new(new(profile, layer), _selectedSlotIndex, _selectedSubActionIndex, EditingOuterFixedSlot ? _fixedLevel : null,
            EditingOuterFixedSlot ? _fixedIndices[_fixedLevel] : 0, _deepCanvasParent, _deepCanvasChild);
    }

    private void JumpToPortalDestination(PortalLayerAddress address)
    {
        if (CapturePortalLocation() is not { } origin || origin.Address == address || !PortalLocationExists(address)) return;
        _portalEditBack.Push(origin);
        NavigatePortalStudio(address);
        RenderPortalBackLink();
    }

    private static bool PortalLocationExists(PortalLayerAddress address) =>
        ConfigManager.CurrentConfig.Profiles.Contains(address.Profile) && address.Profile.Layers.Contains(address.Layer);

    private void NavigatePortalStudio(PortalLayerAddress address)
    {
        MappingsViewModeCanvasRadio.IsChecked = true;
        CloseDeepCanvas();
        CloseFixedCanvas();
        if (!ReferenceEquals(_selectedProfile, address.Profile)) MappingsProfileComboBox.SelectedItem = address.Profile;
        if (!ReferenceEquals(LayerSelectComboBox.SelectedItem, address.Layer)) LayerSelectComboBox.SelectedItem = address.Layer;
        ShowStudioProfileInspector();
        RenderMappingsWheelPreview();
    }

    private void RenderPortalBackLink()
    {
        if (PortalReturnLink == null) return;
        while (_portalEditBack.TryPeek(out var location) && !PortalLocationExists(location.Address)) _portalEditBack.Pop();
        if (!_portalEditBack.TryPeek(out var previous))
        {
            PortalReturnLink.Visibility = Visibility.Collapsed;
            return;
        }
        PortalReturnLink.Visibility = Visibility.Visible;
        PortalReturnLink.Content = I18n.TF("PortalBackLink", previous.Address.Profile.FriendlyName, previous.Address.Layer.DisplayName);
        PortalReturnLink.ToolTip = PortalReturnLink.Content;
    }

    private void PortalReturnLink_Click(object sender, RoutedEventArgs e)
    {
        if (!_portalEditBack.TryPop(out var previous) || !PortalLocationExists(previous.Address)) { RenderPortalBackLink(); return; }
        NavigatePortalStudio(previous.Address);
        if (previous.FixedLevel is int level && previous.Address.Layer.ActiveFixedRings is { } menu &&
            level <= previous.Address.Layer.EffectiveMaxMenuDepth && previous.FixedIndex < menu.GetCount(level))
        {
            OpenFixedCanvas(previous.Address.Profile);
            SelectFixedSlot(level, previous.FixedIndex);
        }
        else if (previous.Branch is { Length: >= 2 } branch)
        {
            OpenLegacyCanvasBranch(branch[0], branch[1]);
            _deepCanvasParent = branch;
            _deepCanvasChild = previous.DeepSlot;
            RenderMappingsWheelPreview();
        }
        else if (previous.Secondary is int sub) SelectSubAction(previous.Sector, sub);
        else if (previous.Sector >= 0) SelectPrimarySlot(previous.Sector);
        RenderPortalBackLink();
    }

    private void TogglePortalPreview(WheelPortalTarget target)
    {
        var location = CapturePortalLocation();
        if (location == null) return;
        bool same = _portalPreviewLocation == location && WheelPortalTarget.SameValue(_portalPreviewTarget, target);
        _portalPreviewLocation = same ? null : location;
        _portalPreviewTarget = same ? null : target.Clone();
        RenderMappingsWheelPreview();
    }

    private void DrawPortalGhost(WheelProfile profile, Point origin, Point sector, double scale, double parentExtent)
    {
        if (_portalPreviewTarget is not { } portal || _portalPreviewLocation != CapturePortalLocation() ||
            PortalReferenceAnalyzer.Resolve(ConfigManager.CurrentConfig.Profiles, profile, portal) is not { } destination) return;
        var config = ConfigManager.CurrentConfig;
        int depth = destination.Layer.EffectiveMaxMenuDepth;
        double childRadius = Math.Max(24, config.WheelRadius * scale);
        if (depth > 2 && destination.Layer.ActiveFixedRings?.ShowsAllOnPopup(config) == true)
            childRadius *= 1 + (depth - 2) * .38;
        Vector position = sector - origin;
        if (portal.IsOuterOffset && position.LengthSquared > 0)
        {
            position.Normalize();
            position *= parentExtent + childRadius + config.EffectivePortalOuterTangentGapDip * scale;
        }
        Point center = origin + position;
        var canvas = MappingsWheelPreviewCanvas;
        var beam = new Polyline
        {
            Points = new PointCollection { origin, sector, center }, StrokeDashArray = new DoubleCollection { 3, 2 },
            Stroke = (Brush)FindResource("AccentPrimaryBrush"), StrokeThickness = 1.5, IsHitTestVisible = false
        };
        Panel.SetZIndex(beam, 20);
        canvas.Children.Add(beam);
        var ghost = new PortalWheelPreview { Width = childRadius * 2, Height = childRadius * 2, IsHitTestVisible = false, Opacity = .48 };
        ghost.SetTarget(config, destination);
        Canvas.SetLeft(ghost, center.X - childRadius);
        Canvas.SetTop(ghost, center.Y - childRadius);
        Panel.SetZIndex(ghost, 21);
        canvas.Children.Add(ghost);
    }

    private void DrawPortalMarker(ActionItem? item, Point center, double width)
    {
        if (item?.IsWheelPortal != true) return;
        var badge = new TextBlock
        {
            Text = "◎", Foreground = (Brush)FindResource("AccentPrimaryBrush"), FontWeight = FontWeights.Bold,
            FontSize = Math.Clamp(width * .3, 8, 12), IsHitTestVisible = false
        };
        Canvas.SetLeft(badge, center.X + width * .23);
        Canvas.SetTop(badge, center.Y - width * .43);
        Panel.SetZIndex(badge, 15);
        MappingsWheelPreviewCanvas.Children.Add(badge);
    }

    private static string PortalDeleteImpact(WheelProfile profile, WheelLayer? layer = null)
    {
        int count = new PortalReferenceAnalyzer(ConfigManager.CurrentConfig.Profiles).FindIncomingReferences(profile, layer)
            .Count(reference => layer == null ? !ReferenceEquals(reference.Source.Profile, profile) : !ReferenceEquals(reference.Source.Layer, layer));
        return count == 0 ? "" : "\n\n" + I18n.TF("PortalDeleteImpact", count);
    }

    private static void RefreshPortalTargetNames(WheelProfile profile, WheelLayer? layer = null)
    {
        foreach (var reference in new PortalReferenceAnalyzer(ConfigManager.CurrentConfig.Profiles).FindIncomingReferences(profile, layer))
        {
            if (reference.Destination is not { } destination) continue;
            reference.Target.TargetProfileNameSnapshot = profile.FriendlyName;
            reference.Target.TargetLayerNameSnapshot = destination.Layer.DisplayName;
            if (reference.Action.IsWheelPortal && reference.Target.SyncTargetName)
                reference.Action.Name = destination.Layer.DisplayName;
        }
    }

    private void LoadPortalAppearanceSettings()
    {
        if (PortalParentVisualModeCombo == null || ConfigManager.CurrentConfig is not { } config) return;
        bool prev = _isUpdatingUi;
        _isUpdatingUi = true;
        try
        {
            PopulatePortalAppearanceCombos();
            PortalParentVisualModeCombo.SelectedValue = config.EffectivePortalParentVisualMode;
            PortalTrailShowBeamToggle.IsChecked = config.PortalTrailShowBeam;
            PortalFadeOpacitySlider.Value = Math.Round(config.EffectivePortalFadeOpacity * 100);
            PortalCustomShapeCombo.SelectedValue = config.EffectivePortalCustomShape;
            PortalCustomSizeSlider.Value = Math.Round(config.EffectivePortalCustomSizeDip);
            PortalCustomOpacitySlider.Value = Math.Round(config.EffectivePortalCustomOpacity * 100);
            PortalCustomFillTextBox.Text = config.PortalCustomFillColor ?? "";
            PortalCustomBorderTextBox.Text = config.PortalCustomBorderColor ?? "";
            UpdateColorPreviewBorder(PortalCustomFillPreview, PortalCustomFillTextBox.Text);
            UpdateColorPreviewBorder(PortalCustomBorderPreview, PortalCustomBorderTextBox.Text);
            PortalCustomIconCombo.SelectedValue = config.PortalCustomIconKey ?? "";
            RefreshPortalAppearanceReadouts(config);
            RefreshPortalCustomImageText(config);
            RefreshPortalCustomPreview(config);
        }
        finally
        {
            _isUpdatingUi = prev;
        }
    }

    private void LocalizePortalAppearanceSettings()
    {
        if (PortalAppearanceTitleText == null) return;
        PortalAppearanceTitleText.Text = I18n.T("PortalAppearanceTitle");
        PortalAppearanceDescText.Text = I18n.T("PortalAppearanceDesc");
        PortalParentModeLabelText.Text = I18n.T("PortalParentModeLabel");
        PortalTrailShowBeamToggle.Content = I18n.T("PortalTrailShowBeam");
        PortalFadeHighlightHintText.Text = I18n.T("PortalFadeHighlightHint");
        PortalCustomShapeLabelText.Text = I18n.T("PortalCustomShapeLabel");
        PortalCustomFillLabelText.Text = I18n.T("PortalCustomFillLabel");
        PortalCustomBorderLabelText.Text = I18n.T("PortalCustomBorderLabel");
        PortalCustomFillTextBox.ToolTip = I18n.T("PortalCustomColorAuto");
        PortalCustomBorderTextBox.ToolTip = I18n.T("PortalCustomColorAuto");
        PortalCustomFillResetBtn.Content = I18n.T("PortalCustomReset");
        PortalCustomBorderResetBtn.Content = I18n.T("PortalCustomReset");
        PortalCustomIconLabelText.Text = I18n.T("PortalCustomIconLabel");
        PortalCustomImageLabelText.Text = I18n.T("PortalCustomImageLabel");
        PortalCustomImageBrowseBtn.Content = I18n.T("PortalCustomImageBrowse");
        PortalCustomImageClearBtn.Content = I18n.T("PortalCustomImageClear");
        PortalCustomPreviewLabelText.Text = I18n.T("PortalCustomPreviewLabel");
        LoadPortalAppearanceSettings();
    }

    private void PopulatePortalAppearanceCombos()
    {
        PortalParentVisualModeCombo.ItemsSource = new[]
        {
            new ActionTypeItem { Tag = "ParentCore", DisplayText = I18n.T("PortalParentModeCore") },
            new ActionTypeItem { Tag = "FadedWheel", DisplayText = I18n.T("PortalParentModeFade") },
            new ActionTypeItem { Tag = "CustomMarker", DisplayText = I18n.T("PortalParentModeCustom") },
            new ActionTypeItem { Tag = "OutlineCircle", DisplayText = I18n.T("PortalParentModeOutline") },
        };
        PortalCustomShapeCombo.ItemsSource = new[]
        {
            new ActionTypeItem { Tag = "Circle", DisplayText = I18n.T("PortalShapeCircle") },
            new ActionTypeItem { Tag = "FourPointStar", DisplayText = I18n.T("PortalShapeStar4") },
            new ActionTypeItem { Tag = "Diamond", DisplayText = I18n.T("PortalShapeDiamond") },
            new ActionTypeItem { Tag = "Hexagon", DisplayText = I18n.T("PortalShapeHexagon") },
            new ActionTypeItem { Tag = "RoundedSquare", DisplayText = I18n.T("PortalShapeRoundedSquare") },
        };
        var icons = new List<ActionTypeItem>
        {
            new ActionTypeItem { Tag = "", DisplayText = I18n.T("PortalCustomIconNone") }
        };
        foreach (var icon in IconHelper.VectorIconList)
            icons.Add(new ActionTypeItem { Tag = icon.Key, DisplayText = icon.DisplayName });
        PortalCustomIconCombo.ItemsSource = icons;
    }

    private void RefreshPortalAppearanceReadouts(AppConfig config)
    {
        string mode = config.EffectivePortalParentVisualMode;
        PortalFadeSettingsPanel.Visibility = mode == "FadedWheel" ? Visibility.Visible : Visibility.Collapsed;
        PortalCustomSettingsPanel.Visibility = mode == "CustomMarker" ? Visibility.Visible : Visibility.Collapsed;
        PortalFadeOpacityValueText.Text = I18n.TF("PortalFadeOpacityValue", Math.Round(config.EffectivePortalFadeOpacity * 100));
        PortalCustomSizeValueText.Text = I18n.TF("PortalCustomSizeValue", Math.Round(config.EffectivePortalCustomSizeDip));
        PortalCustomOpacityValueText.Text = I18n.TF("PortalCustomOpacityValue", Math.Round(config.EffectivePortalCustomOpacity * 100));
    }

    private void RefreshPortalCustomImageText(AppConfig config)
    {
        bool hasImage = !string.IsNullOrWhiteSpace(config.PortalCustomImagePath);
        PortalCustomImagePathText.Text = hasImage ? System.IO.Path.GetFileName(config.PortalCustomImagePath) : I18n.T("PortalCustomImageNone");
        PortalCustomImagePathText.ToolTip = hasImage ? config.PortalCustomImagePath : null;
        PortalCustomImageClearBtn.IsEnabled = hasImage;
    }

    private void RefreshPortalCustomPreview(AppConfig config)
    {
        if (PortalCustomPreviewImage == null) return;
        Brush edge = TryFindResource("AccentPrimaryBrush") as Brush ?? Brushes.DeepSkyBlue;
        Brush fill = TryFindResource("SubtleCardBrush") as Brush ?? Brushes.SlateGray;
        double previewR = Math.Clamp(config.EffectivePortalCustomSizeDip * 0.44, 10, 26);
        var group = new DrawingGroup();
        using (var dc = group.Open())
            PortalTrailOverlay.DrawCustomMarkerVisual(dc, new Point(32, 32), previewR, config, edge, fill);
        var drawing = new DrawingImage(group);
        if (drawing.CanFreeze) drawing.Freeze();
        PortalCustomPreviewImage.Source = drawing;
    }

    private void PortalAppearanceOptionChanged(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUi || !_isUiInitialized || ConfigManager.CurrentConfig is not { } config ||
            PortalParentVisualModeCombo == null) return;
        if (ReferenceEquals(sender, PortalParentVisualModeCombo) && PortalParentVisualModeCombo.SelectedValue is string mode)
            config.PortalParentVisualMode = mode;
        else if (ReferenceEquals(sender, PortalTrailShowBeamToggle))
            config.PortalTrailShowBeam = PortalTrailShowBeamToggle.IsChecked == true;
        else if (ReferenceEquals(sender, PortalFadeOpacitySlider))
            config.PortalFadeOpacity = PortalFadeOpacitySlider.Value / 100.0;
        else if (ReferenceEquals(sender, PortalCustomShapeCombo) && PortalCustomShapeCombo.SelectedValue is string shape)
            config.PortalCustomShape = shape;
        else if (ReferenceEquals(sender, PortalCustomSizeSlider))
            config.PortalCustomSizeDip = PortalCustomSizeSlider.Value;
        else if (ReferenceEquals(sender, PortalCustomOpacitySlider))
            config.PortalCustomOpacity = PortalCustomOpacitySlider.Value / 100.0;
        else if (ReferenceEquals(sender, PortalCustomIconCombo))
            config.PortalCustomIconKey = PortalCustomIconCombo.SelectedValue as string ?? "";

        RefreshPortalAppearanceReadouts(config);
        RefreshPortalCustomPreview(config);
        ConfigManager.MarkConfigurationChanged();
        ScheduleAutoSave();
    }

    private void PortalCustomColorTextChanged(object sender, TextChangedEventArgs e)
    {
        if (ConfigManager.CurrentConfig is not { } config || PortalCustomFillTextBox == null) return;
        string fillHex = PortalCustomFillTextBox.Text.Trim();
        string borderHex = PortalCustomBorderTextBox.Text.Trim();
        UpdateColorPreviewBorder(PortalCustomFillPreview, fillHex);
        UpdateColorPreviewBorder(PortalCustomBorderPreview, borderHex);
        if (_isUpdatingUi) return;
        config.PortalCustomFillColor = fillHex;
        config.PortalCustomBorderColor = borderHex;
        RefreshPortalCustomPreview(config);
        ConfigManager.MarkConfigurationChanged();
        ScheduleAutoSave();
    }

    private void ResetPortalCustomColor_Click(object sender, RoutedEventArgs e)
    {
        if (ConfigManager.CurrentConfig is not { } config || sender is not FrameworkElement { Tag: string tag }) return;
        if (tag == "Fill") PortalCustomFillTextBox.Text = "";
        else if (tag == "Border") PortalCustomBorderTextBox.Text = "";
        RefreshPortalCustomPreview(config);
    }

    private void BrowsePortalCustomImage_Click(object sender, RoutedEventArgs e)
    {
        if (ConfigManager.CurrentConfig is not { } config) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = I18n.T("PortalCustomImageBrowse"),
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.ico;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.ico;*.gif",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != true) return;
        config.PortalCustomImagePath = dialog.FileName;
        RefreshPortalCustomImageText(config);
        RefreshPortalCustomPreview(config);
        ConfigManager.MarkConfigurationChanged();
        ScheduleAutoSave();
    }

    private void ClearPortalCustomImage_Click(object sender, RoutedEventArgs e)
    {
        if (ConfigManager.CurrentConfig is not { } config) return;
        config.PortalCustomImagePath = "";
        RefreshPortalCustomImageText(config);
        RefreshPortalCustomPreview(config);
        ConfigManager.MarkConfigurationChanged();
        ScheduleAutoSave();
    }
}
