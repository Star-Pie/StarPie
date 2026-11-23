namespace WinPieGestures;

public partial class SettingsWindow
{
    // Stage both payloads at their destination while keeping the original submenu structure.
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
                var after = incoming == null && before?.ParentIndex == null && action.SubActions.Count == 0
                    ? null : new FixedRingSlot { Action = action, ParentIndex = before?.ParentIndex };
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
