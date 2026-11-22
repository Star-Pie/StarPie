using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WpfButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using WpfRadioButton = System.Windows.Controls.RadioButton;
using WpfTextBoxBase = System.Windows.Controls.Primitives.TextBoxBase;

namespace WinPieGestures;

public partial class SettingsWindow
{
    private bool _refreshingTriggerScene;
    private bool _changingTriggerSection;
    private bool? _viewedAllowList;
    private AppConfig? _triggerSceneConfig;
    private RemovedTriggerRule? _removedTriggerRule;
    private string[] _triggerSimulatorSuggestions = [];

    private sealed record RemovedTriggerRule(AppConfig Config, bool AllowList, int Index,
        string Process, int RemainingMatches, string? OverrideKey, TriggerConfig? Override);

    private static bool UsesTriggerAllowList(AppConfig config) =>
        string.Equals(config.IsolationMode, "Whitelist", StringComparison.OrdinalIgnoreCase);

    private bool ViewsTriggerAllowList(AppConfig config) => _viewedAllowList ?? UsesTriggerAllowList(config);

    private static List<string>? TriggerList(AppConfig config, bool allowList) =>
        allowList ? config.WhitelistedProcesses : config.BlacklistedProcesses;

    private static string NormalizeTriggerProcess(string? input)
    {
        string name = System.IO.Path.GetFileName(input?.Trim() ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name) || name == ".exe" ||
            name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0) return "";
        return (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exe").ToLowerInvariant();
    }

    private static bool IsLeftTrigger(TriggerConfig? trigger) =>
        trigger != null && string.Equals(trigger.TriggerType, "Mouse", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(trigger.MouseButton, "LeftButton", StringComparison.OrdinalIgnoreCase);

    private static bool IsUnmodifiedLeftTrigger(TriggerConfig? trigger) =>
        IsLeftTrigger(trigger) && trigger is { RequireCtrl: false, RequireShift: false, RequireAlt: false, RequireWin: false };

    private static string? FindTriggerOverrideKey(AppConfig config, string process) =>
        config.BlacklistTriggerOverrides?.Keys.FirstOrDefault(key =>
            string.Equals(key, process.Trim(), StringComparison.OrdinalIgnoreCase));

    private bool HandleTriggerRecorderInterfaceClick(string mouseButton, System.Windows.Point position)
    {
        if (mouseButton != "LeftButton" || (!_isRecordingTrigger && !_isRecordingProcessTrigger) ||
            !IsVisible || PresentationSource.FromVisual(this) == null) return false;
        // The routed wheel target skips this window; use the actual hit HWND for recording.
        if (WindowIdentification.GetRootWindowAtPoint(position) != new WindowInteropHelper(this).Handle) return false;
        var point = PointFromScreen(position);
        if (!new Rect(RenderSize).Contains(point))
        {
            CancelTriggerSceneRecordings();
            return true;
        }
        var control = FindInputControl(InputHitTest(point) as DependencyObject);
        if (control == null) return false;
        if (!ReferenceEquals(control, RecordTriggerButton) && !ReferenceEquals(control, RecordProcessTriggerButton))
            CancelTriggerSceneRecordings();
        return true;
    }

    private void CancelTriggerSceneRecordings()
    {
        if (_isRecordingTrigger) StopTriggerRecording(saved: false);
        if (_isRecordingProcessTrigger) HideProcessTriggerCard();
    }

    private DependencyObject? FindInputControl(DependencyObject? element)
    {
        while (element != null && !ReferenceEquals(element, this))
        {
            if (element is WpfButtonBase or ItemsControl or WpfTextBoxBase or RangeBase) return element;
            element = element is Visual ? VisualTreeHelper.GetParent(element) :
                (element as FrameworkContentElement)?.Parent;
        }
        return null;
    }

    private bool CanEditTriggerScene => _isUiInitialized && !_isUiInitializing && !_isUpdatingUi &&
        !_refreshingTriggerScene && !_resourcesReleased;

    private void TriggerPageSection_Checked(object sender, RoutedEventArgs e)
    {
        if (!_changingTriggerSection && CanEditTriggerScene && sender is WpfRadioButton { Tag: string section })
            ShowTriggerSceneSection(section);
    }

    private void TriggerManageRules_Click(object sender, RoutedEventArgs e) => ShowTriggerSceneSection("rules");

    private void ShowTriggerSceneSection(string section)
    {
        if (TriggerCommonPanel == null) return;
        bool rules = section == "rules";
        bool advanced = section == "advanced";
        if (!rules && _isRecordingProcessTrigger) HideProcessTriggerCard();
        if ((rules || advanced) && _isRecordingTrigger) StopTriggerRecording(saved: false);
        if (!advanced && _currentExclusiveSession?.Kind == ExclusiveRecordingTargetKind.Gesture)
            CancelExclusiveRecordingIfActive();

        _changingTriggerSection = true;
        try
        {
            TriggerCommonTab.IsChecked = !rules && !advanced;
            TriggerRulesTab.IsChecked = rules;
            TriggerAdvancedTab.IsChecked = advanced;
        }
        finally { _changingTriggerSection = false; }
        TriggerCommonPanel.Visibility = !rules && !advanced ? Visibility.Visible : Visibility.Collapsed;
        TriggerRulesPanel.Visibility = rules ? Visibility.Visible : Visibility.Collapsed;
        TriggerAdvancedPanel.Visibility = advanced ? Visibility.Visible : Visibility.Collapsed;
        TriggerSimulatorPanel.Visibility = advanced ? Visibility.Collapsed : Visibility.Visible;
        SetTriggerWorkspaceWidth(TriggerWorkspace.ActualWidth);
        TriggerPageScroll.ScrollToTop();
        if (rules) RefreshProcessListUI();
        else RefreshTriggerSimulation();
    }

    private void TriggerWorkspace_SizeChanged(object sender, SizeChangedEventArgs e) =>
        SetTriggerWorkspaceWidth(e.NewSize.Width);

    private void SetTriggerWorkspaceWidth(double width)
    {
        if (TriggerSimulatorPanel == null || TriggerSimulatorColumn == null) return;
        bool sideBySide = width >= 780 && TriggerAdvancedPanel?.Visibility != Visibility.Visible;
        var rightWidth = new GridLength(sideBySide ? 288 : 0);
        if (TriggerSimulatorColumn.Width == rightWidth) return;
        TriggerSimulatorColumn.Width = rightWidth;
        Grid.SetRow(TriggerSimulatorPanel, sideBySide ? 0 : 1);
        Grid.SetColumn(TriggerSimulatorPanel, sideBySide ? 1 : 0);
        TriggerSimulatorPanel.Margin = sideBySide ? new Thickness(12, 0, 0, 0) : new Thickness(0);
    }

    private void TriggerInvocationMode_Checked(object sender, RoutedEventArgs e)
    {
        if (!CanEditTriggerScene || sender is not WpfRadioButton { Tag: string mode } ||
            ConfigManager.CurrentConfig is not { } config) return;
        bool enabled = mode != "drag";
        bool only = mode == "hold";
        if (!enabled && IsUnmodifiedLeftTrigger(config.Trigger))
        {
            UpdateLongPressTriggerControls();
            return;
        }
        if (config.LongPressTrigger != enabled || config.LongPressOnlyTrigger != only)
        {
            config.LongPressTrigger = enabled;
            config.LongPressOnlyTrigger = only;
            SyncUiToConfigAndSave();
        }
        UpdateLongPressTriggerControls();
    }

    private void TriggerPolicyShortcut_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is WpfRadioButton { Tag: string tag }) SetTriggerIsolationPolicy(tag == "allow");
    }

    private void SetTriggerIsolationPolicy(bool allowList)
    {
        if (!CanEditTriggerScene || ConfigManager.CurrentConfig is not { } config) return;
        if (UsesTriggerAllowList(config) == allowList) return;
        HideProcessTriggerCard();
        _viewedAllowList = allowList;
        config.IsolationMode = allowList ? "Whitelist" : "Blacklist";
        TriggerFilterProcessesBox.Clear();
        SyncUiToConfigAndSave();
        RefreshProcessListUI();
    }

    private void TriggerListView_Checked(object sender, RoutedEventArgs e)
    {
        if (_refreshingTriggerScene || sender is not WpfRadioButton { Tag: string tag } ||
            ConfigManager.CurrentConfig is not { } config) return;
        bool allowList = tag == "allow";
        if (ViewsTriggerAllowList(config) == allowList) return;
        _viewedAllowList = allowList;
        HideProcessTriggerCard();
        TriggerFilterProcessesBox.Clear();
        RefreshProcessListUI();
    }

    private void TriggerFilterProcesses_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_refreshingTriggerScene && BlacklistListBox != null) RefreshProcessListUI();
    }

    private void RefreshTriggerRuleStatus(AppConfig config)
    {
        bool mode = UsesTriggerAllowList(config);
        bool viewed = ViewsTriggerAllowList(config);
        int exclusions = config.BlacklistedProcesses?.Count ?? 0;
        int allowed = config.WhitelistedProcesses?.Count ?? 0;

        IsolationBlacklistRadio.IsChecked = !mode;
        IsolationWhitelistRadio.IsChecked = mode;
        TriggerHomeDefaultRadio.IsChecked = !mode;
        TriggerHomeAllowRadio.IsChecked = mode;
        TriggerViewExcludeRadio.IsChecked = !viewed;
        TriggerViewAllowRadio.IsChecked = viewed;
        TriggerViewExcludeRadio.Content = I18n.TF("TriggerSceneExcludedCount", exclusions);
        TriggerViewAllowRadio.Content = I18n.TF("TriggerSceneAllowedCount", allowed);
        TriggerScopeSummaryText.Text = mode
            ? allowed == 0 ? I18n.T("TriggerSceneEmptyAllow") : I18n.TF("TriggerSceneAllowedCount", allowed)
            : I18n.TF("TriggerSceneExcludedCount", exclusions);
        ProcessListDescText.Text = viewed
            ? mode ? I18n.T(allowed == 0 ? "TriggerSceneEmptyAllow" : "TriggerSceneAllowActive") : I18n.T("TriggerSceneAllowStored")
            : mode ? I18n.T("TriggerSceneExcludeStored") : I18n.T("TriggerSceneExcludeActive");
        TriggerAddAppExpander.Header = I18n.T(viewed ? "TriggerSceneAddAllowed" : "TriggerSceneAddExcluded");
        TriggerUndoRemoveButton.Visibility = ReferenceEquals(_removedTriggerRule?.Config, config)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RemoveTriggerRule(string process)
    {
        if (!CanEditTriggerScene || ConfigManager.CurrentConfig is not { } config) return;
        bool allowList = ViewsTriggerAllowList(config);
        List<string>? list = TriggerList(config, allowList);
        if (list == null) return;
        int index = list.IndexOf(process);
        if (index < 0) index = list.FindIndex(item => string.Equals(item, process, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return;
        string storedProcess = list[index];
        list.RemoveAt(index);
        int remainingMatches = CountTriggerRuleMatches(list, storedProcess);
        string? overrideKey = null;
        TriggerConfig? previous = null;
        if (!allowList && remainingMatches == 0 && config.BlacklistTriggerOverrides is { } overrides)
        {
            overrideKey = FindTriggerOverrideKey(config, storedProcess);
            if (overrideKey != null) overrides.Remove(overrideKey, out previous);
        }
        _removedTriggerRule = new(config, allowList, index, storedProcess, remainingMatches, overrideKey, previous);
        if (string.Equals(_recordingProcessName, process.Trim(), StringComparison.OrdinalIgnoreCase)) HideProcessTriggerCard();
        SyncUiToConfigAndSave();
        RefreshProcessListUI();
    }

    private static int CountTriggerRuleMatches(List<string> list, string process)
    {
        string clean = process.Trim();
        return list.Count(item => string.Equals(item?.Trim(), clean, StringComparison.OrdinalIgnoreCase));
    }

    private void TriggerUndoRemove_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditTriggerScene || _removedTriggerRule is not { } removal ||
            !ReferenceEquals(ConfigManager.CurrentConfig, removal.Config)) return;
        _removedTriggerRule = null;
        List<string>? list = TriggerList(removal.Config, removal.AllowList);
        if (list != null && CountTriggerRuleMatches(list, removal.Process) == removal.RemainingMatches)
        {
            list.Insert(Math.Min(removal.Index, list.Count), removal.Process);
            if (removal.OverrideKey != null && removal.Override != null)
            {
                removal.Config.BlacklistTriggerOverrides ??= new(StringComparer.OrdinalIgnoreCase);
                removal.Config.BlacklistTriggerOverrides.TryAdd(removal.OverrideKey, removal.Override);
            }
            SyncUiToConfigAndSave();
        }
        RefreshProcessListUI();
    }

    private void RefreshTriggerSimulatorProcesses(AppConfig config)
    {
        if (TriggerSimulatorProcessBox == null) return;
        var names = (config.BlacklistedProcesses ?? []).Concat(config.WhitelistedProcesses ?? [])
            .Select(NormalizeTriggerProcess).Where(name => name.Length != 0)
            .Concat(["explorer.exe", "notepad.exe", "mstsc.exe"])
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (names.SequenceEqual(_triggerSimulatorSuggestions, StringComparer.OrdinalIgnoreCase)) return;
        string previous = _triggerSimulatorSuggestions.Length == 0 ? "explorer.exe" : TriggerSimulatorProcessBox.Text;
        _triggerSimulatorSuggestions = names;
        TriggerSimulatorProcessBox.ItemsSource = names;
        TriggerSimulatorProcessBox.Text = previous;
    }

    private void TriggerUseRuleInSimulation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: BlacklistProcessItemViewModel row }) return;
        TriggerSimulatorProcessBox.Text = row.ProcessName;
        TriggerSimulatorInputBox.SelectedValue = TriggerSceneInput.Global;
        TriggerSimulatorFullscreen.IsChecked = false;
        TriggerSimCtrl.IsChecked = TriggerSimShift.IsChecked = TriggerSimAlt.IsChecked = false;
        RefreshTriggerSimulation();
        TriggerSimulatorPanel.BringIntoView();
    }

    private void TriggerSimProcess_Changed(object sender, TextChangedEventArgs e) => RefreshTriggerSimulation();
    private void TriggerSimInput_Changed(object sender, SelectionChangedEventArgs e) => RefreshTriggerSimulation();
    private void TriggerSimOption_Click(object sender, RoutedEventArgs e) => RefreshTriggerSimulation();

    private void RefreshTriggerSimulation()
    {
        if (_refreshingTriggerScene || _resourcesReleased || TriggerSettingsGrid?.Visibility != Visibility.Visible ||
            TriggerSimulatorPanel?.Visibility != Visibility.Visible || TriggerSimulatorOutcome == null ||
            TriggerSimulatorInputBox.SelectedValue is not TriggerSceneInput input ||
            ConfigManager.CurrentConfig is not { } config) return;

        ModifierKeys extra = (TriggerSimCtrl.IsChecked == true ? ModifierKeys.Control : ModifierKeys.None) |
            (TriggerSimShift.IsChecked == true ? ModifierKeys.Shift : ModifierKeys.None) |
            (TriggerSimAlt.IsChecked == true ? ModifierKeys.Alt : ModifierKeys.None);
        string process = NormalizeTriggerProcess(TriggerSimulatorProcessBox.Text);
        bool fullScreen = TriggerSimulatorFullscreen.IsChecked == true;
        TriggerSceneOutcome outcome = TriggerSceneRules.Simulate(config, process, input, fullScreen, extra);
        string title = outcome switch
        {
            TriggerSceneOutcome.WheelAllowed => "TriggerSceneResultWheel",
            TriggerSceneOutcome.GestureAllowed => "TriggerSceneResultGesture",
            TriggerSceneOutcome.MissingProcess or TriggerSceneOutcome.MissingOverride or
                TriggerSceneOutcome.UnspecifiedTriggerKey => "TriggerSceneResultIncomplete",
            TriggerSceneOutcome.WheelPriority => "TriggerSceneResultPriority",
            _ => "TriggerSceneResultNative"
        };
        TriggerSimulatorOutcome.Text = I18n.T(title);
        TriggerSimulatorReason.Text = I18n.T($"TriggerSceneReason{outcome}");

        TriggerConfig? trigger = input switch
        {
            TriggerSceneInput.Global => config.Trigger,
            TriggerSceneInput.ProcessOverride => TriggerSceneRules.GetOverrideTrigger(config, process),
            _ => new TriggerConfig { TriggerType = "Mouse", MouseButton = config.GestureTriggerButton ?? "MiddleButton" }
        };
        TriggerSimulatorTraceText.Text = string.Join(Environment.NewLine,
            I18n.TF("TriggerSceneTracePolicy", I18n.T(UsesTriggerAllowList(config) ? "TriggerSceneOnlyAllowed" : "TriggerSceneDefaultPolicy")),
            I18n.TF("TriggerSceneTraceInput", trigger != null ? FormatTriggerDisplay(trigger) : "—"),
            I18n.TF("TriggerSceneTraceWindow", I18n.T(fullScreen ? "TriggerSceneSimFullScreen" : "TriggerSceneNormalWindow")),
            I18n.TF("TriggerSceneTraceModifiers", extra == ModifierKeys.None ? "—" : extra.ToString()));
    }

    private void ApplyTriggerSceneLocalization()
    {
        if (TriggerCommonTab == null) return;
        TriggerCommonTab.Content = I18n.T("TriggerSceneCommonTab");
        TriggerRulesTab.Content = I18n.T("TriggerSceneRulesTab");
        TriggerAdvancedTab.Content = I18n.T("TriggerSceneAdvancedTab");
        TriggerInvocationTitle.Text = I18n.T("TriggerSceneInvocation");
        TriggerDragMode.Content = I18n.T("TriggerSceneDrag");
        TriggerEitherMode.Content = I18n.T("TriggerSceneEither");
        TriggerHoldMode.Content = I18n.T("TriggerSceneHold");
        TriggerHoldDelayTitle.Text = I18n.T("TriggerSceneHoldDelay");
        TriggerScopeSummaryTitle.Text = I18n.T("IsolationModeTitle");
        TriggerHomeDefaultRadio.Content = I18n.T("TriggerSceneDefaultPolicy");
        TriggerHomeAllowRadio.Content = I18n.T("TriggerSceneOnlyAllowed");
        TriggerManageRulesButton.Content = I18n.T("TriggerSceneManageRules");
        TriggerHomeFullScreenText.Text = I18n.T("TriggerSceneFullscreen");
        TriggerCaptureProcessButton.Content = I18n.T("TriggerSceneCapture");
        TriggerUndoRemoveButton.Content = I18n.T("TriggerSceneUndoRemove");
        TriggerFilterProcessesLabel.Text = I18n.T("TriggerSceneSearch");
        TriggerNewProcessLabel.Text = I18n.T("TriggerSceneProcessEntry");
        AutomationProperties.SetName(TriggerFilterProcessesBox, TriggerFilterProcessesLabel.Text);
        AutomationProperties.SetName(NewBlacklistProcessTextBox, I18n.T("BlacklistPlaceholder"));
        TriggerSimulatorTitle.Text = I18n.T("TriggerSceneSimulator");
        TriggerSimulatorHint.Text = I18n.T("TriggerSceneSimHint");
        TriggerSimulatorProcessLabel.Text = I18n.T("TriggerSceneSimProcess");
        TriggerSimulatorInputLabel.Text = I18n.T("TriggerSceneSimInput");
        TriggerSimulatorFullscreen.Content = I18n.T("TriggerSceneSimFullScreen");
        TriggerSimulatorModifiersLabel.Text = I18n.T("TriggerSceneSimModifiers");
        TriggerSimulatorTrace.Header = I18n.T("TriggerSceneTrace");
        AutomationProperties.SetName(TriggerSimulatorProcessBox, TriggerSimulatorProcessLabel.Text);
        AutomationProperties.SetName(TriggerSimulatorInputBox, TriggerSimulatorInputLabel.Text);
        var previous = TriggerSimulatorInputBox.SelectedValue as TriggerSceneInput? ?? TriggerSceneInput.Global;
        bool wasRefreshing = _refreshingTriggerScene;
        _refreshingTriggerScene = true;
        try
        {
            TriggerSimulatorInputBox.DisplayMemberPath = "Value";
            TriggerSimulatorInputBox.SelectedValuePath = "Key";
            TriggerSimulatorInputBox.ItemsSource = new[]
            {
                KeyValuePair.Create(TriggerSceneInput.Global, I18n.T("TriggerSceneGlobalInput")),
                KeyValuePair.Create(TriggerSceneInput.ProcessOverride, I18n.T("TriggerSceneOverrideInput")),
                KeyValuePair.Create(TriggerSceneInput.MouseGesture, I18n.T("TriggerSceneGestureInput"))
            };
            TriggerSimulatorInputBox.SelectedValue = previous;
        }
        finally { _refreshingTriggerScene = wasRefreshing; }
    }
}
