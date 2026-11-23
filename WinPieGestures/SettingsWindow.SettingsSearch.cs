using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WpfControl = System.Windows.Controls.Control;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace WinPieGestures;

public partial class SettingsWindow
{
    private static readonly string[] SettingsSearchPageKeys =
        ["TabTrigger", "TabAppearance", "TabGestures", "TabAdvanced", "TabAbout", "TabPlugins"];

    private sealed record SettingsSearchTarget(int Page, string TitleKey, FrameworkElement Control,
        string? AliasKey = null, string? SectionKey = null, Action? Reveal = null, FrameworkElement? Fallback = null);

    private sealed record SettingsSearchResult(SettingsSearchTarget Target, string Title, string Section);

    private SettingsSearchTarget[]? _settingsSearchTargets;
    private UIElement? _settingsSearchRevealedPanel;

    private SettingsSearchTarget[] CreateSettingsSearchTargets() =>
    [
        new(0, "TriggerSceneRecorder", TriggerRecorderTitleText, "SettingsSearchTriggerWords", "TriggerSceneCommonTab", ShowSearchTriggerCommon),
        new(0, "TriggerSceneInvocation", TriggerInvocationTitle, "SettingsSearchInvocationWords", "TriggerSceneCommonTab", ShowSearchTriggerCommon),
        new(0, "TriggerSceneDistance", ThresholdSlider, null, "TriggerSceneCommonTab", ShowSearchTriggerCommon, TriggerInvocationTitle),
        new(0, "TriggerSceneHoldDelay", TriggerHoldDelayTitle, null, "TriggerSceneCommonTab", ShowSearchTriggerCommon, TriggerInvocationTitle),
        new(0, "PinWheelTitle", PinWheelTitleText, null, "TriggerSceneCommonTab", ShowSearchTriggerCommon),
        new(0, "PinnedNavKeysTitle", PinnedNavKeysCard, "SettingsSearchPinnedNavWords", "TriggerSceneCommonTab", ShowSearchTriggerCommon),
        new(0, "TriggerSceneFullscreen", TriggerHomeFullScreen, "SettingsSearchFullScreenWords", "TriggerSceneCommonTab", ShowSearchTriggerCommon),
        new(0, "IsolationModeTitle", IsolationModeTitleText, "SettingsSearchScopeWords", "TriggerSceneRulesTab", ShowSearchTriggerRules),
        new(0, "TriggerSceneSimulator", TriggerSimulatorTitle, null, "TriggerSceneRulesTab", ShowSearchTriggerRules),
        new(0, "SubmenuStyleTitle", Tab0_SubmenuStyleTitleText, "SettingsSearchSubmenuWords", "TriggerSceneAdvancedTab", ShowSearchTriggerAdvanced, Tab0_MultiTierSectionTitleText),
        new(0, "PortalTriggerTitle", PortalTriggerOptions, "SettingsSearchPortalTriggerWords", "TriggerSceneAdvancedTab", ShowSearchPortalTrigger),
        new(0, "TriggerSceneInputTolerance", SensitivityTitleText, "SettingsSearchToleranceWords", "TriggerSceneAdvancedTab", ShowSearchTriggerAdvanced),
        new(0, "GestureTitle", GestureTitleText, null, "TriggerSceneAdvancedTab", ShowSearchTriggerAdvanced),
        new(0, "OuterEscapeTitle", OuterEscapeTitleText, null, "TriggerSceneAdvancedTab", ShowSearchTriggerAdvanced),
        new(0, "EdgeOverflowTitle", EdgeOverflowTitleText, null, "TriggerSceneAdvancedTab", ShowSearchTriggerAdvanced),
        new(0, "AnimSpeedTitle", AnimSpeedTitleText, null, "TriggerSceneAdvancedTab", ShowSearchTriggerAdvanced),
        new(0, "VolumeDragTitle", VolumeDragTitleText, null, "TriggerSceneAdvancedTab", ShowSearchTriggerAdvanced),
        new(1, "VisualThemeCardTitle", VisualThemeCardTitleText, "SettingsSearchThemeWords"),
        new(1, "Tier2ThemeExpanderHeader", Tier2ThemeExpander, null, null, () => Tier2ThemeExpander.IsExpanded = true),
        new(1, "SettingsSearchGeometry", WheelAppearanceSidebar, "SettingsSearchGeometryWords", null, RevealSearchGeometry),
        new(1, "LayoutOptionsSectionTitle", LayoutOptionsSectionTitle, "SettingsSearchTypographyWords"),
        new(1, "CoreSectionTitle", CoreSectionTitle, null, null, null, AppearancePageHeader),
        new(1, "LayerIndicatorSectionTitle", LayerIndicatorSectionTitle, null, "ConfigModeProRadio",
            () => RevealSearchAdvancedPanel(Tab1_LayerIndicatorCardBorder)),
        new(1, "PortalAppearanceTitle", Tab1_PortalAppearanceCardBorder, "SettingsSearchPortalAppearanceWords"),
        new(2, "StudioRailLibrary", StudioProfileRail, "SettingsSearchProfilesWords", null, ShowSearchProfileRail),
        new(2, "StudioConditions", StudioProfileApplicationScope, "SettingsSearchBindingWords", "StudioProfileInspectorHeading", ShowSearchProfileInspector),
        new(2, "MenuDepthLabel", MenuDepthPicker, null, "StudioStructure", ShowSearchProfileInspector),
        new(2, "FixedDisplay", FixedDisplay, "SettingsSearchDisplayWords", "StudioStructure", RevealSearchWheelDisplay, MenuDepthPicker),
        new(2, "CenterCoreTitle", StudioCoreVisibilityLabel, null, "StudioProfileInspectorHeading", ShowSearchProfileInspector),
        new(2, "StudioList", ListModeActionListHeaderTitle, null, null, () => MappingsViewModeListRadio.IsChecked = true),
        new(3, "LanguageTitle", LanguageComboBox),
        new(3, "UiFontTitle", UiFontTitle),
        new(3, "StartupTitle", StartupTitleText, "SettingsSearchStartupWords"),
        new(3, "OcrCardTitle", OcrCardTitleText, "SettingsSearchOcrWords", "ConfigModeProRadio",
            () => RevealSearchAdvancedPanel(Tab3_OcrCardBorder)),
        new(3, "MemoryTitle", MemoryOptTitleText),
        new(3, "BackupTitle", BackupTitleText, "SettingsSearchBackupWords"),
        new(3, "LogsTitle", LogsTitleText, null, "ConfigModeProRadio",
            () => RevealSearchAdvancedPanel(Tab3_LogsCardBorder)),
        new(4, "UpdateSectionTitle", UpdateSectionTitleText, "SettingsSearchUpdateWords"),
        new(4, "UpdateSourceTitle", UpdateSourceComboBox, "SettingsSearchDownloadSourceWords"),
        new(4, "UpdateChannelTitle", UpdateChannelComboBox),
        new(4, "RollbackSectionTitle", RollbackSectionTitleText),
        new(5, "SettingsSearchMarketplace", MarketFilterCombo, "SettingsSearchMarketplaceWords", null, () => MarketTabButton.IsChecked = true),
        new(5, "SettingsSearchPluginSort", MarketSortCombo, "SettingsSearchPluginSortWords", null, () => MarketTabButton.IsChecked = true),
        new(5, "SettingsSearchPluginSource", PluginDownloadChannelComboBox, "SettingsSearchDownloadSourceWords", null, () => MarketTabButton.IsChecked = true),
        new(5, "SettingsSearchInstalledPlugins", InstalledFilterCombo, "SettingsSearchInstalledWords", null, () => InstalledTabButton.IsChecked = true),
    ];

    private void LocalizeSettingsSearch()
    {
        if (SettingsSearchButton == null) return;
        SetSettingsSearchLabel(SettingsSearchButton, "SettingsSearchOpen");
        SetSettingsSearchLabel(SettingsSearchClose, "SettingsSearchClose");
        SetSettingsSearchLabel(SettingsSearchInput, "SettingsSearchPrompt");
        AutomationProperties.SetName(SettingsSearchResults, I18n.T("SettingsSearchResults"));
        if (SettingsSearchPanel.Visibility == Visibility.Visible) UpdateSettingsSearchResults();
    }

    private static void SetSettingsSearchLabel(FrameworkElement control, string key)
    {
        string label = I18n.T(key);
        AutomationProperties.SetName(control, label);
        control.ToolTip = label;
    }

    private bool TryHandleSettingsSearchShortcut(WpfKeyEventArgs e)
    {
        if (e.Key != Key.F || Keyboard.Modifiers != ModifierKeys.Control || _currentExclusiveSession != null ||
            !_isUiInitialized || SettingsSearchPanel == null)
            return false;
        e.Handled = true;
        OpenSettingsSearch();
        return true;
    }

    private void SettingsSearchButton_Click(object sender, RoutedEventArgs e)
    {
        if (SettingsSearchPanel.Visibility == Visibility.Visible) CloseSettingsSearch();
        else if (_isUiInitialized) OpenSettingsSearch();
    }

    private void OpenSettingsSearch()
    {
        CancelExclusiveRecordingIfActive();
        CancelTriggerSceneRecordings();
        SettingsSearchPanel.Visibility = Visibility.Visible;
        UpdateSettingsSearchResults();
        SettingsSearchInput.Focus();
        SettingsSearchInput.SelectAll();
    }

    private void CloseSettingsSearch(bool restoreFocus = true)
    {
        SettingsSearchPanel.Visibility = Visibility.Collapsed;
        if (restoreFocus) SettingsSearchButton.Focus();
    }

    private void SettingsSearchInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SettingsSearchPanel?.Visibility == Visibility.Visible) UpdateSettingsSearchResults();
    }

    private void UpdateSettingsSearchResults()
    {
        string query = SettingsSearchInput.Text.Trim();
        SettingsSearchResult[] results = [];
        if (query.Length != 0)
        {
            _settingsSearchTargets ??= CreateSettingsSearchTargets();
            string[] words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            results = _settingsSearchTargets
                .Select(target =>
                {
                    string section = I18n.T(SettingsSearchPageKeys[target.Page]);
                    if (target.SectionKey is { } sectionKey) section += "  ›  " + I18n.T(sectionKey);
                    return new SettingsSearchResult(target, I18n.T(target.TitleKey), section);
                })
                .Select(result => (Result: result, Score: ScoreSettingsSearch(result, query, words)))
                .Where(match => match.Score > 0)
                .OrderByDescending(match => match.Score)
                .Take(12)
                .Select(match => match.Result)
                .ToArray();
        }
        SettingsSearchResults.ItemsSource = results;
        SettingsSearchResults.SelectedIndex = results.Length > 0 ? 0 : -1;
        SettingsSearchResults.Visibility = results.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        SettingsSearchStatus.Visibility = results.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        SettingsSearchStatus.Text = I18n.T(query.Length == 0 ? "SettingsSearchPrompt" : "SettingsSearchEmpty");
    }

    private static int ScoreSettingsSearch(SettingsSearchResult result, string query, string[] words)
    {
        const StringComparison comparison = StringComparison.CurrentCultureIgnoreCase;
        string aliases = result.Target.AliasKey is { } key ? I18n.T(key) : "";
        if (words.Any(word => !result.Title.Contains(word, comparison) &&
                             !result.Section.Contains(word, comparison) && !aliases.Contains(word, comparison)))
            return 0;
        if (result.Title.Equals(query, comparison)) return 5;
        if (result.Title.StartsWith(query, comparison)) return 4;
        if (result.Title.Contains(query, comparison)) return 3;
        return words.All(word => result.Section.Contains(word, comparison)) ? 1 : 2;
    }

    private void SettingsSearchPanel_PreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                CloseSettingsSearch();
                break;
            case Key.Enter when (SettingsSearchInput.IsKeyboardFocusWithin || SettingsSearchResults.IsKeyboardFocusWithin) &&
                                SettingsSearchResults.SelectedItem is SettingsSearchResult result:
                NavigateSettingsSearch(result.Target);
                break;
            case Key.Up or Key.Down when SettingsSearchInput.IsKeyboardFocusWithin && SettingsSearchResults.Items.Count > 0:
                int delta = e.Key == Key.Up ? -1 : 1;
                SettingsSearchResults.SelectedIndex = Math.Clamp(SettingsSearchResults.SelectedIndex + delta,
                    0, SettingsSearchResults.Items.Count - 1);
                SettingsSearchResults.ScrollIntoView(SettingsSearchResults.SelectedItem);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void SettingsSearchResults_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(SettingsSearchResults, source) is not ListBoxItem { Content: SettingsSearchResult result })
            return;
        NavigateSettingsSearch(result.Target);
        e.Handled = true;
    }

    private void NavigateSettingsSearch(SettingsSearchTarget target)
    {
        CloseSettingsSearch();
        SwitchToTab(target.Page);
        target.Reveal?.Invoke();
        ExpandSettingsSearchAncestors(target.Control);
        UpdateLayout();
        FrameworkElement control = IsSettingsSearchTargetShown(target.Control)
            ? target.Control : target.Fallback ?? GetSettingsSearchPage(target.Page);
        if (!ReferenceEquals(control, target.Control))
        {
            ExpandSettingsSearchAncestors(control);
            UpdateLayout();
        }
        control.BringIntoView();
        if (control is WpfControl { Focusable: true, IsEnabled: true } && control is not HotkeyRecorderBox)
            control.Focus();
    }

    private FrameworkElement GetSettingsSearchPage(int page) => page switch
    {
        0 => TriggerPageHeader,
        1 => AppearancePageHeader,
        2 => StudioCanvasTitleRow,
        3 => AdvancedPageHeader,
        4 => Tab4_AboutTitleText,
        _ => PluginsPageHeader
    };

    private static void ExpandSettingsSearchAncestors(FrameworkElement control)
    {
        for (DependencyObject? parent = GetSettingsSearchParent(control); parent != null; parent = GetSettingsSearchParent(parent))
        {
            if (parent is Expander expander) expander.IsExpanded = true;
        }
    }

    private static bool IsSettingsSearchTargetShown(FrameworkElement control)
    {
        for (DependencyObject? current = control; current != null; current = GetSettingsSearchParent(current))
        {
            if (current is Window) return true;
            if (current is UIElement { Visibility: not Visibility.Visible }) return false;
        }
        return false;
    }

    private static DependencyObject? GetSettingsSearchParent(DependencyObject element) =>
        LogicalTreeHelper.GetParent(element) ?? (element is Visual visual ? VisualTreeHelper.GetParent(visual) : null);

    private void ShowSearchTriggerCommon() => ShowTriggerSceneSection("common");
    private void ShowSearchTriggerRules() => ShowTriggerSceneSection("rules");
    private void ShowSearchTriggerAdvanced() => ShowTriggerSceneSection("advanced");
    private void ShowSearchPortalTrigger() { ShowTriggerSceneSection("advanced"); PortalTriggerOptions.IsExpanded = true; }

    private void RevealSearchGeometry()
    {
        if (_appearancePreview == AppearancePreviewKind.Effects)
            ChangeAppearancePreview(AppearancePreviewKind.Configure);
    }

    private void RevealSearchAdvancedPanel(UIElement panel)
    {
        if (panel.Visibility != Visibility.Collapsed) return;
        _settingsSearchRevealedPanel = panel;
        panel.Visibility = Visibility.Visible;
    }

    private void ClearSettingsSearchReveal()
    {
        if (_settingsSearchRevealedPanel == null) return;
        _settingsSearchRevealedPanel.Visibility = Visibility.Collapsed;
        _settingsSearchRevealedPanel = null;
    }

    private void ShowSearchProfileRail()
    {
        MappingsViewModeCanvasRadio.IsChecked = true;
        _studioHideRail = false;
        _studioRailDrawer = true;
        _studioInspectorDrawer = false;
        UpdateStudioLayout();
    }

    private void ShowSearchProfileInspector()
    {
        MappingsViewModeCanvasRadio.IsChecked = true;
        ShowStudioProfileInspector();
        _studioHideInspector = false;
        _studioInspectorDrawer = true;
        UpdateStudioLayout();
    }

    private void RevealSearchWheelDisplay()
    {
        ShowSearchProfileInspector();
        if (GetSelectedProfile() is { } profile && profile.GetActiveLayer() is
            { EffectiveMaxMenuDepth: >= 3, ActiveFixedRings: not null })
            OpenFixedCanvas(profile);
    }
}
