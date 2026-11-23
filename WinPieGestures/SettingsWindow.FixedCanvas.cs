using System.Windows;
using System.Windows.Controls;

namespace WinPieGestures;

public partial class SettingsWindow
{
    private WheelLayer? _fixedLayer;
    private WheelProfile? _fixedProfile;
    private int _fixedLevel = 3;
    private int? _fixedInnerLevel;
    private WheelLayer? _twoTierEditorLayer;
    private readonly int[] _fixedIndices = new int[6];
    private readonly Dictionary<(int Level, int Index), FixedRingSlotDraft> _fixedDrafts = new();
    private FixedRingSlotDraft? _boundFixedDraft;
    private bool _fixedFilling, _fixedBindRequired;
    private bool EditingOuterFixedSlot => _fixedInnerLevel == null && OnFixedCanvas;
    private bool OnFixedCanvas => _fixedLayer != null && _fixedLayer.EffectiveMaxMenuDepth >= 3 &&
        ReferenceEquals(GetSelectedProfile(), _fixedProfile) &&
        ReferenceEquals(GestureController.CaptureReleaseLayer(_fixedProfile!), _fixedLayer) &&
        _fixedLayer.ActiveFixedRings != null && MappingsViewModeCanvasRadio?.IsChecked == true;

    private sealed record RingEdit(ActionAddress Address, string Description, Action<WheelLayer> Revert,
        Action<WheelLayer> Apply, int ItemsCount = 1) : IActionTransaction
    {
        public bool MatchesContext(WheelProfile? profile, int index) => Address.MatchesContext(profile, index);
        public bool MatchesContext(string? name, int index) => Address.MatchesContext(name, index);
        public void Undo(SettingsWindow window) => window.ApplyFixedChange(Address, Revert);
        public void Redo(SettingsWindow window) => window.ApplyFixedChange(Address, Apply);
    }

    private void CommitFixedChange(WheelProfile profile, string label, Action<WheelLayer> revert, Action<WheelLayer> apply, int items = 1)
    {
        var layer = profile.GetActiveLayer();
        var address = new ActionAddress(profile.ProcessName, profile.ActiveLayerIndex, -1, null, GetProfileInstanceId(profile), layer);
        _transactionHistory.Push(new RingEdit(address, label, revert, apply, items));
        ApplyFixedChange(address, apply);
    }

    private void ApplyFixedChange(ActionAddress address, Action<WheelLayer> change)
    {
        if (GetSelectedProfile() is not { } profile || !address.MatchesContext(profile, profile.ActiveLayerIndex)) return;
        change(address.LayerInstance!);
        RefreshMenuDepth(profile);
        RenderMappingsWheelPreview();
        UpdateUndoRedoButtonStates();
        ScheduleAutoSave();
    }

    private void FixedLayoutToggle_Click(object sender, RoutedEventArgs e)
    {
        if (!_isUiInitialized || _isUpdatingUi || GetSelectedProfile() is not { } profile) return;
        var layer = profile.GetActiveLayer();
        var before = layer.FixedRings;
        bool paused = layer.FixedRingsPaused, disable = layer.ActiveFixedRings != null;
        var menu = before ?? FixedRingMenu.FromLegacy(profile);
        if (before == null && ConfigManager.CurrentConfig.SubmenuStyle == "Fan") menu.AlwaysVisible = false;
        CloseDeepCanvas();
        MappingsViewModeCanvasRadio.IsChecked = true;
        int oldDepth = layer.MaxMenuDepth;
        if (!disable) BeginFixedCanvas(profile);
        CommitFixedChange(profile, disable ? "FixedReturnLegacy" : "FixedEnableLayout",
            l => { l.FixedRings = before; l.FixedRingsPaused = paused; l.MaxMenuDepth = oldDepth; },
            l => { l.FixedRings = menu; l.FixedRingsPaused = disable; if (!disable && oldDepth < 3) l.MaxMenuDepth = 3; });
    }

    private void BeginFixedCanvas(WheelProfile profile)
    {
        ShowStudioActionInspector();
        CloseDeepCanvas();
        SetFixedCanvasContext(profile, profile.GetActiveLayer());
    }

    private void SetFixedCanvasContext(WheelProfile profile, WheelLayer layer)
    {
        _twoTierEditorLayer = null;
        _fixedInnerLevel = null;
        _fixedProfile = profile;
        _fixedLayer = layer;
        _fixedLevel = 3;
        Array.Clear(_fixedIndices);
        _fixedDrafts.Clear();
    }

    private void SelectFixedInnerLevel(int level)
    {
        if (OnFixedCanvas) _fixedInnerLevel = level;
        else if (_fixedLayer != null) CloseFixedCanvas();
    }

    private void OpenFixedCanvas(WheelProfile profile)
    {
        BeginFixedCanvas(profile);
        RenderMappingsWheelPreview();
    }

    private void FixedOpenCanvas_Click(object sender, RoutedEventArgs e)
    {
        if (EditingOuterFixedSlot || GetSelectedProfile() is not { } profile || profile.GetActiveLayer().ActiveFixedRings == null) return;
        MappingsViewModeCanvasRadio.IsChecked = true;
        OpenFixedCanvas(profile);
    }

    private void FixedBack_Click(object sender, RoutedEventArgs e)
    {
        _twoTierEditorLayer = _fixedLayer;
        CloseFixedCanvas();
        RenderMappingsWheelPreview();
    }

    private void CloseFixedCanvas()
    {
        if (_fixedLayer == null) return;
        _fixedLayer = null;
        _fixedProfile = null;
        _fixedInnerLevel = null;
        StudioFixedBreadcrumb.Visibility = Visibility.Collapsed;
        _fixedGeometry = null;
        _fixedDrafts.Clear();
        _boundFixedDraft = null;
        FixedEditor.BindAction(null, _ => { });
        FixedControlsCard.Visibility = FixedSlotCard.Visibility = Visibility.Collapsed;
        CanvasTierSelector.Visibility = FocusActionEditorBorder.Visibility = Visibility.Visible;
        MappingsLinkSubActionsBtn.Visibility = Visibility.Visible;
        RefreshMenuDepth(GetSelectedProfile());
        MappingsCanvasInstructionsText.Text = I18n.T("MappingsCanvasInstructions");
        UpdateFocusEditorUi();
    }

    private bool RenderFixedCanvas(WheelProfile profile)
    {
        if (FixedControlsCard == null) return false;
        var layer = GestureController.CaptureReleaseLayer(profile);
        if (!ReferenceEquals(_fixedLayer, layer) || !ReferenceEquals(_fixedProfile, profile)) CloseFixedCanvas();
        if (!ReferenceEquals(_twoTierEditorLayer, layer)) _twoTierEditorLayer = null;
        if (layer?.ActiveFixedRings is not { } menu || MappingsViewModeCanvasRadio.IsChecked != true ||
            layer.EffectiveMaxMenuDepth < 3)
        {
            CloseFixedCanvas();
            return false;
        }
        // A multi-level fan opens on the same grid as the live wheel. Browsing only binds view state.
        if (_fixedLayer == null)
        {
            if (ConfigManager.CurrentConfig.SubmenuStyle != "Fan" || _twoTierEditorLayer != null || _deepCanvasParent != null) return false;
            SetFixedCanvasContext(profile, layer);
            _fixedInnerLevel = _selectedSlotIndex < 0 ? 0 : _selectedSubActionIndex.HasValue ? 2 : 1;
        }
        _fixedLevel = Math.Clamp(_fixedLevel, 3, layer.EffectiveMaxMenuDepth);
        int count = menu.GetCount(_fixedLevel);
        int index = _fixedIndices[_fixedLevel] = Math.Clamp(_fixedIndices[_fixedLevel], 0, count - 1);
        ClearCanvasHitItems();
        if (_fixedInnerLevel.HasValue)
        {
            FixedControlsCard.Visibility = FixedSlotCard.Visibility = Visibility.Collapsed;
            FocusActionEditorBorder.Visibility = CanvasTierSelector.Visibility = MappingsLinkSubActionsBtn.Visibility = Visibility.Visible;
            FixedOpenCanvas.Visibility = Visibility.Visible;
            MappingsCanvasInstructionsText.Text = I18n.T("MappingsCanvasInstructions");
            DrawFixedWheel(profile, layer, menu);
            return true;
        }
        FixedControlsCard.Visibility = FixedSlotCard.Visibility = Visibility.Visible;
        FocusActionEditorBorder.Visibility = Visibility.Collapsed;
        CanvasTierSelector.Visibility = MappingsLinkSubActionsBtn.Visibility = FocusSubActionsBorder.Visibility = Visibility.Collapsed;
        FixedOpenCanvas.Visibility = Visibility.Collapsed;
        MappingsCanvasInstructionsText.Text = I18n.T(ConfigManager.CurrentConfig.EnableMultiTier ? "FixedGuide" : "FixedGuideDisabled");
        _fixedFilling = true;
        try
        {
            FixedLevelLabel.Text = I18n.T("FixedLevel");
            FixedCountLabel.Text = I18n.T("FixedCount");
            FixedLevel.ItemsSource = Enumerable.Range(3, layer.EffectiveMaxMenuDepth - 2);
            FixedLevel.SelectedItem = _fixedLevel;
            FixedCount.ItemsSource = Enumerable.Range(4, FixedRingMenu.MaximumCount(_fixedLevel) - 3);
            FixedCount.SelectedItem = count;
            int used = Enumerable.Range(0, count).Count(i => menu.GetSlot(_fixedLevel, i) != null);
            FixedOccupancy.Text = I18n.TF("FixedSlotsSummary", used, count - used,
                FixedRingMenu.RecommendedCount(_fixedLevel), FixedRingMenu.MaximumCount(_fixedLevel));
            FixedDisplayLabel.Text = I18n.T("FixedDisplay");
            FixedDisplay.ItemsSource = new[] { I18n.T("FixedAll"), I18n.T("FixedStep") };
            FixedDisplay.SelectedIndex = menu.AlwaysVisible ? 0 : 1;
            FixedBack.Content = I18n.T("FixedBack");
            FixedUndo.Content = I18n.T("FocusUndoSubActionsBtnText");
            FixedRedo.Content = I18n.T("FocusRedoSubActionsBtnText");
            FixedUndo.IsEnabled = _transactionHistory.CanUndo(profile, profile.ActiveLayerIndex);
            FixedRedo.IsEnabled = _transactionHistory.CanRedo(profile, profile.ActiveLayerIndex);
            BindFixedSlot(menu, index);
        }
        finally { _fixedFilling = false; }
        DrawFixedWheel(profile, layer, menu);
        return true;
    }

    private void BindFixedSlot(FixedRingMenu menu, int index)
    {
        var key = (_fixedLevel, index);
        if (!_fixedDrafts.TryGetValue(key, out var draft) || !draft.Matches(menu, _fixedLevel, index))
            _fixedDrafts[key] = draft = new FixedRingSlotDraft(menu, _fixedLevel, index);
        FixedTitle.Text = I18n.TF("CanvasEditLevel", _fixedLevel, (index + 1).ToString("00"));
        FixedApply.Content = I18n.T("FixedApply");
        FixedNext.Content = I18n.T("FixedNext");
        FixedClear.Content = I18n.T("FixedClear");
        FixedClear.IsEnabled = menu.GetSlot(_fixedLevel, index) != null;
        FixedNext.IsEnabled = FixedRingSlotDraft.NextEmpty(menu, _fixedLevel, index) >= 0;
        FixedApply.IsEnabled = !string.IsNullOrWhiteSpace(draft.Action.Name);
        if (!ReferenceEquals(_boundFixedDraft, draft) || _fixedBindRequired)
        {
            _boundFixedDraft = draft;
            _fixedBindRequired = false;
            FixedEditor.BindAction(draft.Action, edited =>
            {
                if (!CurrentFixedDraft(draft)) return;
                draft.Action = edited;
                FixedApply.IsEnabled = !string.IsNullOrWhiteSpace(edited.Name);
            }, allowEmptyAction: true, portalContext: CreatePortalEditorContext(_fixedProfile), deferPortalIdentity: true);
        }
    }

    private static IEnumerable<(int Primary, int Child, ActionItem Root, ActionItem Action)> SecondaryParents(WheelProfile profile, bool includeVacant = false)
    {
        bool fan = ConfigManager.CurrentConfig.SubmenuStyle == "Fan";
        for (int primary = 0; primary < profile.SectorCount; primary++)
        {
            var root = FixedRingMenu.DisplayRoot(profile, primary);
            if (root?.InlineSubActions is not { } children) continue;
            int n = DeepMenuFrame.VisibleSlotCount(children.Count, fan);
            for (int child = 0; child < n; child++)
                if (children[child] is { } action && (includeVacant || WheelProfile.IsActionConfigured(action)))
                    yield return (primary, child, root, action);
        }
    }

    private static ActionItem? FixedAction(WheelProfile profile, FixedRingMenu menu, int level, int index)
    {
        if (level >= 3) return menu.GetSlot(level, index)?.Action;
        if (level == 2)
            return SecondaryParents(profile).FirstOrDefault(p => FixedRingMenu.SecondaryKey(p.Primary, p.Child) == index).Action;
        return GetCanvasRoot(profile, index);
    }

    private bool CurrentFixedDraft(FixedRingSlotDraft draft) =>
        EditingOuterFixedSlot && ReferenceEquals(draft, _boundFixedDraft) &&
        draft.Matches(_fixedLayer!.ActiveFixedRings, _fixedLevel, _fixedIndices[_fixedLevel]);

    private bool CanEditFixedControls => !_fixedFilling && _isUiInitialized && !_isUpdatingUi &&
        _fixedProfile != null && ReferenceEquals(_fixedProfile, GetSelectedProfile()) &&
        ReferenceEquals(GestureController.CaptureReleaseLayer(_fixedProfile), _fixedLayer) && _fixedLayer?.ActiveFixedRings != null;

    private void SelectFixedSlot(int level, int index)
    {
        ShowStudioActionInspector();
        if (!CanEditFixedControls) return;
        _fixedInnerLevel = null;
        _selectedMultiSlots.Clear();
        ExitBatchModeUi();
        _fixedLevel = level;
        _fixedIndices[level] = index;
        RenderMappingsWheelPreview();
    }

    private void FixedLevel_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (CanEditFixedControls && FixedLevel.SelectedItem is int level)
            SelectFixedSlot(level, _fixedIndices[level]);
    }

    private void FixedCount_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!CanEditFixedControls || _fixedLevel < 3 || FixedCount.SelectedItem is not int count) return;
        int level = _fixedLevel, before = _fixedLayer!.ActiveFixedRings!.GetCount(level);
        if (before == count) return;
        CommitFixedChange(_fixedProfile!, "FixedCount", l => l.FixedRings!.SetCount(level, before),
            l => l.FixedRings!.SetCount(level, count));
    }

    private void FixedDisplay_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!CanEditFixedControls || FixedDisplay.SelectedIndex < 0) return;
        bool all = FixedDisplay.SelectedIndex == 0;
        if (_fixedLayer!.FixedRings!.AlwaysVisible == all) return;
        CommitFixedChange(_fixedProfile!, "FixedDisplay", l => l.FixedRings!.AlwaysVisible = !all, l => l.FixedRings!.AlwaysVisible = all);
    }

    private void FixedNext_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditFixedControls || !EditingOuterFixedSlot) return;
        int index = FixedRingSlotDraft.NextEmpty(_fixedLayer!.ActiveFixedRings!, _fixedLevel, _fixedIndices[_fixedLevel]);
        if (index >= 0) SelectFixedSlot(_fixedLevel, index);
    }

    private void FixedApply_Click(object sender, RoutedEventArgs e)
    {
        if (_boundFixedDraft is not { } draft || !CurrentFixedDraft(draft) || draft.MakeSlot() is not { } slot) return;
        if (slot.Action.IsWheelPortal && slot.Action.PortalTarget is { TargetLayerId: null } portal &&
            PortalReferenceAnalyzer.Resolve(ConfigManager.CurrentConfig.Profiles, _fixedProfile!, portal) is { } destination)
            portal.TargetLayerId = destination.Layer.EnsureLayerId();
        WriteFixedSlot(slot);
    }

    private void FixedClear_Click(object sender, RoutedEventArgs e)
    {
        if (_boundFixedDraft is { } draft && CurrentFixedDraft(draft)) WriteFixedSlot(null);
    }

    private void WriteFixedSlot(FixedRingSlot? slot)
    {
        int level = _fixedLevel, index = _fixedIndices[level];
        var before = _fixedLayer!.ActiveFixedRings!.GetSlot(level, index);
        if (before?.ParentIndex == slot?.ParentIndex && AreActionItemsEquivalent(before?.Action, slot?.Action)) return;
        var saved = before?.Clone();
        CommitFixedChange(_fixedProfile!, slot == null ? "FixedClear" : "FixedApply",
            l => l.FixedRings!.SetSlot(level, index, saved?.Clone()), l => l.FixedRings!.SetSlot(level, index, slot?.Clone()), 2);
    }
}
