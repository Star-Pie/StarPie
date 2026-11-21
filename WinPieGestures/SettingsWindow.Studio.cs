using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace WinPieGestures;

public partial class SettingsWindow
{
    private TextBlock FocusPluginTitleText => FocusPluginPanel?.FocusPluginTitleText!;
    private TextBlock FocusPluginDetailText => FocusPluginPanel?.FocusPluginDetailText!;
    private TextBlock FocusPluginParamsHintText => FocusPluginPanel?.FocusPluginParamsHintText!;
    private TextBlock FocusPluginValidationText => FocusPluginPanel?.FocusPluginValidationText!;
    private Button FocusPluginReloadBtn => FocusPluginPanel?.FocusPluginReloadBtn!;
    private StackPanel FocusPluginParamsPanel = null!;

    private bool _studioReady;
    private bool _studioAction;
    private bool _studioHideRail;
    private bool _studioHideInspector;
    private bool _studioRailDrawer;
    private bool _studioInspectorDrawer;
    private bool? _studioAutoOverride;
    private readonly Dictionary<FrameworkElement, (Panel Parent, int Index)> _studioHomes = new();

    private double StudioWindowWidth => ActualWidth > 0 ? ActualWidth : RootScaleGrid.ActualWidth * RootUiScaleTransform.ScaleX;
    private bool StudioAutoNavigation => _studioReady && MappingsSettingsGrid.Visibility == Visibility.Visible &&
        StudioWindowWidth is > 0 and <= 1240 && _studioAutoOverride != false;

    private void InitializeMappingsStudio()
    {
        foreach (var element in new FrameworkElement[] { MenuDepthControls, ApplicationIdentificationPanel,
                     LayerSwitchTriggerLabel, LayerSwitchTriggerComboBox })
        {
            var parent = (Panel)element.Parent;
            _studioHomes.Add(element, (parent, parent.Children.IndexOf(element)));
        }
        InitializeStudioRail();
        InitializeStudioPalette();
        _studioReady = true;
        RootScaleGrid.SizeChanged += (_, _) => UpdateStudioWindowSpacing();
        CanvasTierSelector.IsVisibleChanged += (_, _) => UpdateStudioStageTools();
        ((Panel)MappingsLinkSubActionsBtn.Parent).Children.Remove(MappingsLinkSubActionsBtn);
        StudioTopTools.Children.Add(MappingsLinkSubActionsBtn);
        UpdateStudioHosts(MappingsViewModeCanvasRadio.IsChecked == true);
        LocalizeStudio();
    }

    // The compact view and Studio edit the same controls. Reparenting keeps each control's handlers and state.
    private void MoveStudioElement(FrameworkElement element, FrameworkElement target, int position = -1)
    {
        if (element.Parent == target) return;
        if (element.Parent is Panel oldPanel) oldPanel.Children.Remove(element);
        else if (element.Parent is ContentControl oldHost) oldHost.Content = null;
        if (target is ContentControl host) host.Content = element;
        else if (target is Panel panel) panel.Children.Insert(position < 0 ? panel.Children.Count : Math.Min(position, panel.Children.Count), element);
    }

    private void UpdateStudioHosts(bool canvas)
    {
        if (!_studioReady) return;
        bool updating = _isUpdatingUi;
        _isUpdatingUi = true;
        try
        {
            LegacyMappingsHeading.Visibility = canvas ? Visibility.Collapsed : Visibility.Visible;
            if (canvas)
            {
                MoveStudioElement(MenuDepthControls, _studioAction ? StudioActionDepthHost : StudioDepthHost);
                MoveStudioElement(ApplicationIdentificationPanel, StudioConditionsHost);
                MoveStudioElement(LayerSwitchTriggerLabel, StudioStructureTools);
                MoveStudioElement(LayerSwitchTriggerComboBox, StudioStructureTools);
            }
            else
            {
                foreach (var item in _studioHomes.OrderBy(pair => pair.Value.Index))
                    MoveStudioElement(item.Key, item.Value.Parent, item.Value.Index);
            }
        }
        finally { _isUpdatingUi = updating; }
        UpdateStudioLayout();
        ApplySidebarLayout();
    }

    private void ShowStudioProfileInspector() => SetStudioContext(false);
    private void ShowStudioActionInspector() => SetStudioContext(true);

    private void SetStudioContext(bool action)
    {
        if (!_studioReady) return;
        _studioAction = action;
        StudioProfileSection.Visibility = action ? Visibility.Collapsed : Visibility.Visible;
        StudioActionSection.Visibility = action ? Visibility.Visible : Visibility.Collapsed;
        if (MappingsCanvasModeGrid.Visibility == Visibility.Visible)
            MoveStudioElement(MenuDepthControls, action ? StudioActionDepthHost : StudioDepthHost);
        _studioRailDrawer = false;
        UpdateStudioLayout();
        if (!action) RefreshStudioRail();
    }

    private void UpdateStudioMode(bool simple)
    {
        if (!_studioReady) return;
        StudioAdvancedChildren.Visibility = simple ? Visibility.Collapsed : Visibility.Visible;
        LayerSwitchTriggerLabel.Visibility = LayerSwitchTriggerComboBox.Visibility = simple ? Visibility.Collapsed : Visibility.Visible;
        StudioSlotAppearance.Visibility = simple ? Visibility.Collapsed : Visibility.Visible;
        RefreshStudioRail();
    }

    private void LocalizeStudio()
    {
        if (!_studioReady) return;
        StudioRailToggle.Content = StudioContextBack.Content = I18n.T("StudioProfiles");
        StudioListToggle.Content = I18n.T("StudioList");
        StudioListOptions.Header = I18n.T("StudioListOptions");
        StudioInspectorToggle.Content = I18n.T("StudioInspector");
        StudioStructure.Header = I18n.T("StudioStructure");
        StudioConditions.Header = I18n.T("StudioConditions");
        StudioChildren.Header = I18n.T("StudioChildren");
        StudioCore.Header = I18n.T("CenterCoreTitle");
        StudioPluginActionCaption.Text = I18n.T("ActionTypePluginShort");
        FocusPluginPanel.ManageButton.Content = I18n.T("PluginPageHeader");
        LocalizeStudioAppearance();
        StudioPalette.RefreshEntries();
        UpdateStudioStageTools();
        RefreshStudioRail();
    }

    private void StudioVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!_studioReady) return;
        ApplySidebarLayout();
        UpdateStudioLayout();
        if (MappingsCanvasModeGrid.IsVisible) RefreshStudioRail();
    }

    private void StudioSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_studioReady) return;
        ApplySidebarLayout();
        UpdateStudioLayout();
    }

    private void StudioStageSizeChanged(object sender, SizeChangedEventArgs e) => UpdateStudioStageTools();

    private void UpdateStudioStageTools()
    {
        if (!_studioReady) return;
        UpdateStudioWindowSpacing();
        bool low = StudioStage.ActualHeight is > 0 and < 285;
        StudioTierRow.Visibility = low ? Visibility.Collapsed : Visibility.Visible;
        StudioTierToggle.Visibility = low && CanvasTierSelector.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
        StudioTierToggle.Content = "Ⅰ / Ⅱ";
        StudioTierToggle.ToolTip = I18n.T("CanvasMainWheel") + " / " + MappingsTier2SegmentRadio.Content;
        bool compact = StudioStage.ActualHeight is > 0 and < 400 || StudioStage.ActualWidth < 490;
        StudioStage.Padding = new Thickness(compact ? 5 : 14);
        StudioPalette.SetCompact(compact || StudioStage.ActualWidth < 820);
        StudioCanvasTitleRow.Visibility = StudioSaveFooter.Visibility = MappingsCanvasInstructionsText.Visibility =
            compact ? Visibility.Collapsed : Visibility.Visible;
        MappingsLinkSubActionsText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        if (compact)
        {
            StudioRailToggle.Content = "☰";
            StudioInspectorToggle.Content = "⚙";
            StudioListToggle.Content = "☷";
        }
        else SetStudioToolbarLabels();
        System.Windows.Automation.AutomationProperties.SetName(StudioRailToggle, I18n.T("StudioProfiles"));
        System.Windows.Automation.AutomationProperties.SetName(StudioInspectorToggle, I18n.T("StudioInspector"));
        System.Windows.Automation.AutomationProperties.SetName(StudioListToggle, I18n.T("StudioList"));
        StudioRailToggle.ToolTip = I18n.T("StudioProfiles");
        StudioInspectorToggle.ToolTip = I18n.T("StudioInspector");
        StudioListToggle.ToolTip = I18n.T("StudioList");
    }

    private void SetStudioToolbarLabels()
    {
        StudioRailToggle.Content = I18n.T("StudioProfiles");
        StudioInspectorToggle.Content = I18n.T("StudioInspector");
        StudioListToggle.Content = I18n.T("StudioList");
    }

    private void UpdateStudioWindowSpacing()
    {
        if (!_studioReady) return;
        bool small = MappingsSettingsGrid.Visibility == Visibility.Visible && RootScaleGrid.ActualHeight is > 0 and < 470;
        SettingsContentBody.Margin = small ? new Thickness(8, 4, 8, 4) : new Thickness(25, 18, 25, 20);
        SettingsFooter.Margin = SettingsFooter.Padding = small ? new Thickness(0, 3, 0, 0) : new Thickness(0, 15, 0, 0);
        SettingsModeRow.Margin = new Thickness(0, 0, 0, small ? 3 : 14);
        BottomNoteText.Visibility = small ? Visibility.Collapsed : Visibility.Visible;
        SaveButton.Height = CloseButton.Height = small ? 27 : 36;
    }

    private void UpdateStudioWheelFit()
    {
        if (StudioWheelFit == null || !_studioReady) return;
        double available = Math.Min(MappingsPreviewViewportContainer.ActualWidth, MappingsPreviewViewportContainer.ActualHeight);
        double fit = _deepCanvasParent == null && _fixedLayer == null && available > 0
            ? Math.Clamp((available - 6) / 300.0, .1, 1) : 1;
        StudioWheelFit.ScaleX = StudioWheelFit.ScaleY = fit;
    }

    private void StudioTierClick(object sender, RoutedEventArgs e)
    {
        if (CanvasTierSelector.Visibility != Visibility.Visible) return;
        if (MappingsTier2SegmentRadio.IsChecked == true) MappingsTier1SegmentRadio.IsChecked = true;
        else MappingsTier2SegmentRadio.IsChecked = true;
    }

    private void UpdateStudioLayout()
    {
        if (!_studioReady || MappingsCanvasModeGrid.ActualWidth <= 0) return;
        double width = MappingsCanvasModeGrid.ActualWidth;
        bool narrowRail = width < 850;
        bool narrowInspector = width < 625;
        bool showRail = narrowRail ? _studioRailDrawer : !_studioHideRail;
        bool showInspector = narrowInspector ? _studioInspectorDrawer : !_studioHideInspector;
        var cfg = ConfigManager.CurrentConfig;
        double rail = Math.Clamp(cfg.StudioRailFraction > 0 ? cfg.StudioRailFraction * width : 220, 170, 320);
        double inspector = Math.Clamp(cfg.StudioInspectorFraction > 0 ? cfg.StudioInspectorFraction * width : 340, 260, Math.Max(300, width * .45));
        SetStudioPane(StudioRailPane, StudioRailColumn, StudioRailSplitter, 0, showRail, narrowRail, rail, width);
        SetStudioPane(StudioInspector, Tab2RightColumn, Tab2GridSplitter, 4, showInspector, narrowInspector, inspector, width);
        StudioRailClose.Visibility = showRail && narrowRail ? Visibility.Visible : Visibility.Collapsed;
        StudioInspectorClose.Visibility = showInspector && narrowInspector ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void SetStudioPane(FrameworkElement pane, ColumnDefinition column, GridSplitter splitter,
        int home, bool show, bool drawer, double desired, double available)
    {
        pane.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        splitter.Visibility = show && !drawer ? Visibility.Visible : Visibility.Collapsed;
        column.Width = new GridLength(show && !drawer ? desired : 0);
        Grid.SetColumn(pane, drawer ? 0 : home);
        Grid.SetColumnSpan(pane, drawer ? 5 : 1);
        pane.Width = drawer ? Math.Min(available, desired + 16) : double.NaN;
        pane.HorizontalAlignment = drawer ? (home == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right) : HorizontalAlignment.Stretch;
    }

    private void StudioSplitCompleted(object sender, DragCompletedEventArgs e)
    {
        if (!_studioReady || MappingsCanvasModeGrid.ActualWidth <= 0 || e.Canceled) return;
        double width = MappingsCanvasModeGrid.ActualWidth;
        if (sender == StudioRailSplitter)
            ConfigManager.CurrentConfig.StudioRailFraction = StudioRailColumn.ActualWidth / width;
        else ConfigManager.CurrentConfig.StudioInspectorFraction = Tab2RightColumn.ActualWidth / width;
        UpdateStudioLayout();
        ScheduleAutoSave();
    }

    private void StudioRailClick(object sender, RoutedEventArgs e)
    {
        if (MappingsCanvasModeGrid.ActualWidth < 850) _studioRailDrawer = !_studioRailDrawer;
        else _studioHideRail = !_studioHideRail;
        _studioInspectorDrawer = false;
        UpdateStudioLayout();
    }

    private void StudioInspectorClick(object sender, RoutedEventArgs e)
    {
        if (MappingsCanvasModeGrid.ActualWidth < 625) _studioInspectorDrawer = !_studioInspectorDrawer;
        else _studioHideInspector = !_studioHideInspector;
        _studioRailDrawer = false;
        UpdateStudioLayout();
    }

    private void StudioCoreClick(object sender, RoutedEventArgs e) => SelectCenterCore();
    private void StudioProfileClick(object sender, RoutedEventArgs e) => ShowStudioProfileInspector();
    private void StudioListClick(object sender, RoutedEventArgs e) => MappingsViewModeListRadio.IsChecked = true;
}
