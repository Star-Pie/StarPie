using System.Windows;
using System.Windows.Controls;

namespace WinPieGestures;

public partial class SettingsWindow
{
    private bool _studioAppearanceBinding;

    private bool IsStudioBranch(WheelProfile profile) => _studioReady && _studioAction &&
        (EditingOuterFixedSlot || HasDeepCanvasContext(profile) && _deepBoundPath != null);

    private bool CanUseStudioClipboard() => (_deepCanvasParent == null && !EditingOuterFixedSlot) ||
        (GetSelectedProfile() is { } profile && IsStudioBranch(profile));

    private ActionItem? BranchItem(WheelProfile profile) =>
        EditingOuterFixedSlot && _boundFixedDraft is { } draft && CurrentFixedDraft(draft) ? draft.Action : StudioAppearanceTarget(profile);

    private void RefreshStudioBranchTools(WheelProfile profile)
    {
        if (!_studioReady) return;
        bool show = IsStudioBranch(profile);
        StudioBranchTools.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;
        var item = BranchItem(profile);
        StudioBranchCopy.IsEnabled = WheelProfile.IsActionConfigured(item) || !string.IsNullOrWhiteSpace(item?.Name);
        StudioBranchTest.IsEnabled = WheelProfile.IsActionConfigured(item);
        StudioBranchPaste.IsEnabled = ActionClipboard.HasContent;
    }

    private bool CopyStudioBranch(WheelProfile profile)
    {
        if (!IsStudioBranch(profile)) return false;
        if (BranchItem(profile) is { } item && (WheelProfile.IsActionConfigured(item) || !string.IsNullOrWhiteSpace(item.Name)))
        {
            ActionClipboard.CopySingleAction(item, profile.ProcessName, profile.ActiveLayerIndex);
            NotifyClipboardCopied(string.IsNullOrWhiteSpace(item.Name) ? item.Type : item.Name);
            UpdateClipboardButtonStates();
            RefreshStudioBranchTools(profile);
        }
        return true;
    }

    private bool PasteStudioBranch(WheelProfile profile)
    {
        if (!IsStudioBranch(profile)) return false;
        var (item, children, _) = ActionClipboard.Paste();
        if (item == null) return true;
        if (children is { Count: > 0 })
        {
            MappingsCurrentEditIndicator.Text = I18n.T("StudioPasteSingle");
            return true;
        }
        if (EditingOuterFixedSlot && _boundFixedDraft is { } draft && CurrentFixedDraft(draft))
        {
            var menu = profile.GetActiveLayer().ActiveFixedRings!;
            var saved = menu.GetSlot(_fixedLevel, _fixedIndices[_fixedLevel]);
            var target = new PaletteSlot([_fixedIndices[_fixedLevel]], _fixedLevel);
            WritePaletteFixedSlot(profile, new Placement(target, saved?.Action), item);
        }
        else if (_deepBoundPath is { } path) ApplyCanvasEdit(profile, path, _deepBindingVersion, item);
        return true;
    }

    private void StudioBranchTestClick(object sender, RoutedEventArgs e)
    {
        if (GetSelectedProfile() is { } profile && IsStudioBranch(profile) && BranchItem(profile) is { } item && item.Type != "None")
            ActionExecutor.ExecuteForTesting(item.CloneWithoutSubActions());
    }

    private void LocalizeStudioAppearance()
    {
        StudioSlotAppearance.Header = I18n.T("StudioSlotAppearance");
        StudioBranchTest.Content = I18n.T("FocusTestActionBtnText");
        StudioBranchCopy.Content = I18n.T("FocusCopyActionBtnText");
        StudioBranchPaste.Content = I18n.T("FocusPasteActionBtnText");
        _studioAppearanceBinding = true;
        try
        {
            StudioSlotLayout.Items.Clear();
            foreach (var (mode, key) in new[] { ("Inherit", "LayoutModeItemInherit"), ("IconAndText", "LayoutModeItemBoth"),
                         ("IconOnly", "LayoutModeItemIconOnly"), ("TextOnly", "LayoutModeItemTextOnly") })
                StudioSlotLayout.Items.Add(new ComboBoxItem { Tag = mode, Content = I18n.T(key) });
        }
        finally { _studioAppearanceBinding = false; }
        RefreshStudioAppearance();
    }

    private ActionItem? StudioAppearanceTarget(WheelProfile profile)
    {
        if (_selectedMultiSlots.Count > 1) return null;
        if (EditingOuterFixedSlot) return _fixedLayer?.ActiveFixedRings?.GetSlot(_fixedLevel, _fixedIndices[_fixedLevel])?.Action;
        if (HasDeepCanvasContext(profile) && _deepBoundPath is { Length: > 0 } path)
            return GetCanvasNode(GetCanvasRoot(profile, path[0]), path);
        return _selectedSlotIndex < 0 ? profile.GetEffectiveCenterAction() : profile.GetEffectiveAction(_selectedSlotIndex, _selectedSubActionIndex ?? -1);
    }

    private void StudioAppearanceExpanded(object sender, RoutedEventArgs e) => RefreshStudioAppearance();

    private void RefreshStudioAppearance()
    {
        if (!_studioReady || !StudioSlotAppearance.IsExpanded || _studioAppearanceBinding) return;
        var item = GetSelectedProfile() is { } profile ? StudioAppearanceTarget(profile) : null;
        _studioAppearanceBinding = true;
        try
        {
            StudioSlotLayout.IsEnabled = item != null;
            StudioSlotLayout.SelectedValue = string.IsNullOrEmpty(item?.LayoutMode) ? "Inherit" : item.LayoutMode;
        }
        finally { _studioAppearanceBinding = false; }
    }

    private void StudioSlotLayoutChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_studioReady || _studioAppearanceBinding || _isUpdatingUi || !StudioSlotAppearance.IsExpanded ||
            StudioSlotLayout.SelectedValue is not string mode || GetSelectedProfile() is not { } profile ||
            StudioAppearanceTarget(profile) is not { } item || (item.LayoutMode ?? "Inherit") == mode) return;
        if (EditingOuterFixedSlot)
        {
            var copy = _fixedLayer!.ActiveFixedRings!.GetSlot(_fixedLevel, _fixedIndices[_fixedLevel])!.Clone();
            copy.Action.LayoutMode = mode;
            WriteFixedSlot(copy);
        }
        else if (HasDeepCanvasContext(profile) && _deepBoundPath is { } path)
        {
            var copy = item.Clone();
            copy.LayoutMode = mode;
            ApplyCanvasEdit(profile, path, _deepBindingVersion, copy);
        }
        else
        {
            var before = CaptureSectorSnapshot(profile, _selectedSlotIndex);
            ActionItem? local;
            if (_selectedSlotIndex < 0)
            {
                local = item.Clone();
                local.IsInherited = false;
                profile.CenterAction = local;
                profile.EnableCenterAction = true;
            }
            else local = GetCurrentFocusActionItem(ensureLocalForEdit: true);
            if (local == null) return;
            local.LayoutMode = mode;
            profile.SyncActiveLayerFromRootProperties();
            RecordSingleSectorTransaction(profile, _selectedSlotIndex, "StudioSlotAppearance", before, CaptureSectorSnapshot(profile, _selectedSlotIndex));
            RenderMappingsWheelPreview();
            ScheduleAutoSave();
        }
    }

    private void UpdateStudioFixedBreadcrumb(WheelProfile profile, FixedRingMenu menu, Dictionary<int, int> path)
    {
        var panel = StudioFixedBreadcrumb;
        panel.Children.Clear();
        panel.Visibility = Visibility.Visible;
        foreach (var (level, index) in path.OrderBy(entry => entry.Key))
        {
            string caption = FixedAction(profile, menu, level, index)?.Name ?? "";
            if (string.IsNullOrWhiteSpace(caption)) caption = $"{level} · {index + 1:00}";
            if (panel.Children.Count > 0) panel.Children.Add(new TextBlock { Text = "›", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3,0,3,0) });
            var button = new Button { Content = caption, ToolTip = caption, MaxWidth = 110, Padding = new Thickness(5,1,5,1),
                Style = (Style)FindResource("ModernButtonStyle") };
            button.Click += (_, _) =>
            {
                if (level == 1) SelectPrimarySlot(index);
                else if (level == 2 && FixedRingMenu.TryDecodeSecondaryKey(index, out int parent, out int child)) SelectSubAction(parent, child);
                else if (level >= 3) SelectFixedSlot(level, index);
            };
            panel.Children.Add(button);
        }
    }
}
