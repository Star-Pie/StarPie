namespace WinPieGestures;

public partial class SettingsWindow
{
    // A parent key names a position on the preceding level. Moving entire branches across
    // levels would invalidate those keys, especially when the two positions are ancestors.
    // Stage both payloads against their original slots, then commit one reversible edit.
    private void DropAcrossFixedLevels(WheelProfile profile, CanvasHitItem source, CanvasHitItem target, bool copy)
    {
        var menu = source.FixedMenu!;
        var roots = new Dictionary<int, ActionItem>();
        var writes = new List<Action<bool>>();
        if (!copy) Stage(source, target.Action);
        Stage(target, source.Action);
        foreach (var (index, after) in roots)
        {
            if (!string.Equals(profile.ProcessName, "Global", StringComparison.OrdinalIgnoreCase) &&
                after.Type == "Hotkey" && !WheelProfile.IsActionConfigured(after))
                after.OverrideGlobalMenu = true;
            var before = profile.Actions[index].Clone();
            if (!AreActionItemsEquivalent(before, after))
                writes.Add(undo => profile.Actions[index] = (undo ? before : after).Clone());
        }
        if (writes.Count == 0) return;

        void Apply(WheelLayer layer, bool undo)
        {
            if (!ReferenceEquals(layer.ActiveFixedRings, menu)) return;
            foreach (var write in writes) write(undo);
            if (roots.Count > 0)
            {
                ResetFocusEditSnapshot();
                RefreshSlots();
                RefreshFocusSubActionsListAndChips();
                UpdateFocusEditorUi();
            }
        }

        if (target.FixedLevel is >= 3)
        {
            _fixedInnerLevel = null;
            _fixedLevel = target.FixedLevel.Value;
            _fixedIndices[_fixedLevel] = target.SlotIndex;
        }
        CommitFixedChange(profile, copy ? "EditCopySector" : "EditSwapSectors",
            layer => Apply(layer, true), layer => Apply(layer, false), writes.Count * 2);
        if (target.FixedLevel == 1) SelectPrimarySlot(target.SlotIndex);
        else if (target.FixedLevel == 2) SelectSubAction(target.SlotIndex, target.SubIndex!.Value);

        void Stage(CanvasHitItem hit, ActionItem? incoming)
        {
            int index = hit.SlotIndex, level = hit.FixedLevel!.Value;
            if (level >= 3)
            {
                var before = menu.GetSlot(level, index);
                var action = PayloadAt(incoming, before?.Action);
                bool ownsChildren = level < 5 && menu.Tiers.ElementAtOrDefault(level - 2)?.Slots?
                    .Any(child => child?.ParentIndex == index) == true;
                var after = incoming == null && before?.ParentIndex == null && !ownsChildren && action.SubActions.Count == 0
                    ? null : new FixedRingSlot { Action = action, ParentIndex = before?.ParentIndex };
                if (before == null && after != null)
                    after.ParentIndex = NewFixedDropParent(profile, menu, level, index);
                if (before?.ParentIndex != after?.ParentIndex || !AreActionItemsEquivalent(before?.Action, after?.Action))
                    writes.Add(StageFixedSlot(menu, level, index, after));
                return;
            }

            if (!roots.TryGetValue(index, out var root))
            {
                root = GetCanvasRoot(profile, index)!.Clone();
                root.IsInherited = false;
                roots.Add(index, root);
            }
            if (level == 1)
            {
                roots[index] = PayloadAt(incoming, root);
                return;
            }
            int childIndex = hit.SubIndex!.Value;
            root.SubActions ??= [];
            var child = PayloadAt(incoming, root.SubActions.ElementAtOrDefault(childIndex));
            if (incoming == null && menu.Tiers.FirstOrDefault()?.Slots?.Any(slot =>
                slot?.ParentIndex == FixedRingMenu.SecondaryKey(index, childIndex)) == true)
                child.OverrideGlobalMenu = true; // Keep the empty secondary traversable as a fixed-ring parent.
            if (hit.IsDropOnly) root.SubActions.Add(child);
            else root.SubActions[childIndex] = child;
        }
    }

    private static ActionItem PayloadAt(ActionItem? incoming, ActionItem? occupant)
    {
        var action = incoming?.CloneWithoutSubActions() ?? new ActionItem { Name = "" };
        action.IsInherited = false;
        action.OverrideGlobalMenu = occupant?.OverrideGlobalMenu ?? false;
        if (occupant?.SubActions != null) action.SubActions = occupant.SubActions;
        return action;
    }

    private int? NewFixedDropParent(WheelProfile profile, FixedRingMenu menu, int level, int index)
    {
        if (_fixedDrafts.TryGetValue((level, index), out var draft) && draft.Matches(menu, level, index))
            return draft.ParentIndex;
        int parent = _fixedIndices[level - 1];
        return parent >= 0 && (level == 3 || parent < menu.GetCount(level - 1)) &&
            FixedAction(profile, menu, level - 1, parent) != null ? parent : null;
    }

    private static Action<bool> StageFixedSlot(FixedRingMenu menu, int level, int index, FixedRingSlot? after)
    {
        int tierIndex = level - 3, tierCount = menu.Tiers.Count;
        var oldTier = menu.Tiers.ElementAtOrDefault(tierIndex);
        int stored = oldTier?.Slots?.Count ?? 0;
        var before = oldTier?.Slots?.ElementAtOrDefault(index)?.Clone();
        var saved = after?.Clone();
        return undo =>
        {
            menu.SetSlot(level, index, (undo ? before : saved)?.Clone());
            if (!undo || menu.Tiers.ElementAtOrDefault(tierIndex) is not { } tier) return;
            if (tier.Slots.Count > stored && tier.Slots.Skip(stored).All(s => s == null))
                tier.Slots.RemoveRange(stored, tier.Slots.Count - stored);
            if (oldTier == null && tier.Slots.Count == 0) menu.Tiers[tierIndex] = null;
            if (menu.Tiers.Count > tierCount && menu.Tiers.Skip(tierCount).All(t => t == null))
                menu.Tiers.RemoveRange(tierCount, menu.Tiers.Count - tierCount);
        };
    }
}
