using System.Runtime.CompilerServices;

namespace WinPieGestures;

public partial class SettingsWindow
{
    // A copy of a child used for a clipboard insert gets its own identity. Materializing
    // an inherited branch is the one clone operation that retains the original identities.
    private readonly ConditionalWeakTable<ActionItem, object> _secondaryIdentities = new();
    private sealed record InheritedChild(WheelLayer Layer, int Primary, int Child);

    private object?[]? CaptureChildIdentities(WheelProfile profile, int primary)
    {
        var layer = GestureController.CaptureReleaseLayer(profile);
        if (layer == null || (layer.FixedRings == null && !(IsInheritanceSource(profile) &&
            ConfigManager.CurrentConfig.Profiles.Any(p => p.Layers?.Any(l => l?.FixedRings != null) == true)))) return null;
        var root = profile.Actions[primary];
        if (!WheelProfile.IsActionConfigured(root) && profile.GetEffectiveAction(primary) is { IsInherited: true, SubActions: { } inherited })
            return inherited.Select((_, i) => (object?)new InheritedChild(layer, primary, i)).ToArray();
        if (root?.SubActions is { Count: > 0 } children)
            return children.Select(a => a == null ? null : _secondaryIdentities.GetValue(a, _ => new object())).ToArray();
        return [];
    }

    private void RetainMaterializedIdentities(WheelProfile profile, int primary, ActionItem materialized)
    {
        if (GestureController.CaptureReleaseLayer(profile) is not { FixedRings: not null } layer || materialized.SubActions == null) return;
        for (int i = 0; i < materialized.SubActions.Count; i++)
            if (materialized.SubActions[i] is { } child) _secondaryIdentities.Add(child, new InheritedChild(layer, primary, i));
    }

    private static bool IsInheritanceSource(WheelProfile profile) =>
        ConfigManager.CurrentConfig.EnableGlobalInheritance && string.Equals(profile.ProcessName, "Global", StringComparison.OrdinalIgnoreCase) &&
        ReferenceEquals(profile, ConfigManager.GetGlobalProfile());

    private sealed record ParentChange(WheelLayer Layer, FixedRingMenu Menu, int Slot, int? Before, int? After);

    private sealed record ChildBindingEdit(IActionTransaction ActionEdit, List<ParentChange> Changes) : IActionTransaction
    {
        public ActionAddress Address => ActionEdit.Address;
        public string Description => ActionEdit.Description;
        public int ItemsCount => ActionEdit.ItemsCount + Changes.Count;
        public bool MatchesContext(WheelProfile? profile, int index) => ActionEdit.MatchesContext(profile, index);
        public bool MatchesContext(string? name, int index) => ActionEdit.MatchesContext(name, index);

        internal void Restore(bool before)
        {
            foreach (var change in Changes)
            {
                var slots = change.Menu.Tiers?.FirstOrDefault()?.Slots;
                if (ReferenceEquals(change.Layer.FixedRings, change.Menu) && slots != null &&
                    (uint)change.Slot < (uint)slots.Count && slots[change.Slot] is { } slot &&
                    slot.ParentIndex == (before ? change.After : change.Before))
                    slot.ParentIndex = before ? change.Before : change.After;
            }
        }

        public void Undo(SettingsWindow window) { Restore(true); ActionEdit.Undo(window); }
        public void Redo(SettingsWindow window) { Restore(false); ActionEdit.Redo(window); }
    }

    private IActionTransaction FollowSecondaryParents(IActionTransaction tx,
        params (int Primary, ActionSnapshot Before, ActionSnapshot After)[] sectors)
    {
        if (tx.Address.LayerInstance is not { } layer || !sectors.Any(s => s.Before.ChildIdentities != null)) return tx;
        var prior = new Dictionary<int, object>();
        var current = new Dictionary<object, int>();
        foreach (var (primary, before, after) in sectors)
        {
            if (primary < 0 || before.ChildIdentities == null || after.ChildIdentities == null) continue;
            for (int i = 0; i < before.ChildIdentities.Length; i++)
                if (before.ChildIdentities[i] is { } identity) prior[FixedRingMenu.SecondaryKey(primary, i)] = identity;
            for (int i = 0; i < after.ChildIdentities.Length; i++)
                if (after.ChildIdentities[i] is { } identity) current[identity] = FixedRingMenu.SecondaryKey(primary, i);
        }
        var moved = new Dictionary<int, int?>();
        foreach (var (key, identity) in prior)
        {
            int? next = current.TryGetValue(identity, out int found) ? found : null;
            if (next != key) moved[key] = next;
        }
        if (moved.Count == 0) return tx;

        var changes = new List<ParentChange>();
        CollectParentChanges(layer, moved, changes);
        if (GetSelectedProfile() is { } global && IsInheritanceSource(global))
        {
            var configuredBefore = sectors.Where(s => WheelProfile.IsActionConfigured(s.Before.Action)).Select(s => s.Primary).ToHashSet();
            var configuredAfter = sectors.Where(s => WheelProfile.IsActionConfigured(s.After.Action)).Select(s => s.Primary).ToHashSet();
            foreach (var profile in ConfigManager.CurrentConfig.Profiles)
            {
                if (ReferenceEquals(profile, global) || profile.Layers == null) continue;
                for (int i = 0; i < profile.Layers.Count; i++)
                {
                    // GetEffectiveAction uses the matching global layer, falling back to layer zero.
                    if ((i < global.Layers.Count ? i : 0) != tx.Address.LayerIndex) continue;
                    var receiver = profile.Layers[i];
                    if (receiver?.FixedRings?.Tiers?.FirstOrDefault()?.Slots is not { Count: > 0 }) continue;
                    var translated = TranslateInheritedParents(profile, receiver, layer.SectorCount > 0 ? layer.SectorCount : global.SectorCount, moved, configuredBefore, configuredAfter);
                    CollectParentChanges(receiver, translated, changes);
                }
            }
        }
        if (changes.Count == 0) return tx;
        var edit = new ChildBindingEdit(tx, changes);
        edit.Restore(false);
        return edit;
    }

    private static void CollectParentChanges(WheelLayer layer, Dictionary<int, int?> moved, List<ParentChange> changes)
    {
        if (layer.FixedRings is not { } menu || menu.Tiers?.FirstOrDefault()?.Slots is not { } slots) return;
        for (int i = 0; i < slots.Count; i++)
            if (slots[i]?.ParentIndex is int old && moved.TryGetValue(old, out int? next) && next != old)
                changes.Add(new(layer, menu, i, old, next));
    }

    private static Dictionary<int, int?> TranslateInheritedParents(WheelProfile profile, WheelLayer local, int globalCount,
        Dictionary<int, int?> moved, HashSet<int> configuredBefore, HashSet<int> configuredAfter, int? changedGlobalCount = null)
    {
        var translated = new Dictionary<int, int?>();
        int nextCount = changedGlobalCount ?? globalCount;
        // A local override (including explicit None) masks the corresponding global sector.
        int count = local.SectorCount > 0 ? local.SectorCount : profile.SectorCount;
        if (count is <= 0 or > 32 || globalCount <= 0) return translated;
        var actions = local.Actions ?? profile.Actions;
        var inherited = Enumerable.Range(0, count).Where(i =>
        {
            var root = actions?.ElementAtOrDefault(i);
            return !WheelProfile.IsActionConfigured(root) && !string.Equals(root?.Type, "None", StringComparison.OrdinalIgnoreCase);
        }).ToArray();
        foreach (var (old, next) in moved)
        {
            if (!FixedRingMenu.TryDecodeSecondaryKey(old, out int oldRoot, out int oldChild) || !configuredBefore.Contains(oldRoot)) continue;
            foreach (int source in inherited.Where(i => WheelProfile.MapInheritedSector(i, count, globalCount) == oldRoot))
            {
                int? targetKey = null;
                if (next is int key && FixedRingMenu.TryDecodeSecondaryKey(key, out int newRoot, out int newChild) && configuredAfter.Contains(newRoot))
                {
                    // Several local sectors can map to one global direction. Preserve each source's
                    // relative angle around its old direction when choosing the destination.
                    double ideal = (double)source / count + (double)newRoot / nextCount - (double)oldRoot / globalCount;
                    double AngularDistance(int i)
                    {
                        double delta = ideal - (double)i / count;
                        return Math.Abs(delta - Math.Round(delta));
                    }
                    int dest = inherited.Where(i => WheelProfile.MapInheritedSector(i, count, nextCount) == newRoot)
                        .OrderBy(AngularDistance).FirstOrDefault(-1);
                    if (dest >= 0) targetKey = FixedRingMenu.SecondaryKey(dest, newChild);
                }
                translated[FixedRingMenu.SecondaryKey(source, oldChild)] = targetKey;
            }
        }
        return translated;
    }

    private sealed record PrimaryCountEdit(ActionAddress Address, int BeforeCount, List<ActionItem> Before,
        Dictionary<string, List<ActionItem>>? BeforeSnapshots,
        int AfterCount, List<ActionItem> After,
        Dictionary<string, List<ActionItem>>? AfterSnapshots) : IActionTransaction
    {
        public string Description => "SectorCountLabel";
        public int ItemsCount => Before.Count + After.Count;
        public bool MatchesContext(WheelProfile? profile, int index) => Address.MatchesContext(profile, index);
        public bool MatchesContext(string? name, int index) => Address.MatchesContext(name, index);
        public void Undo(SettingsWindow window) => window.SetPrimaryCount(
            BeforeCount, Before.Select(a => a.Clone()).ToList(), WheelLayer.CloneSectorCountSnapshots(BeforeSnapshots));
        public void Redo(SettingsWindow window) => window.SetPrimaryCount(
            AfterCount, After.Select(a => a.Clone()).ToList(), WheelLayer.CloneSectorCountSnapshots(AfterSnapshots));
    }

    private static List<ParentChange> MigrateFixedParents(WheelProfile profile, WheelLayer layer, int oldCount, int newCount,
        List<ActionItem> nextActions, Dictionary<int, int> destinations)
    {
        var changes = new List<ParentChange>();
        var ownKeys = new Dictionary<int, int?>();
        if (layer.FixedRings?.Tiers?.FirstOrDefault()?.Slots is { } stored)
        {
            foreach (var slot in stored)
            {
                if (slot?.ParentIndex is not int key || ownKeys.ContainsKey(key) ||
                    !FixedRingMenu.TryDecodeSecondaryKey(key, out int primary, out int child)) continue;
                int? dest = null;
                if (destinations.TryGetValue(primary, out int next))
                {
                    var effective = profile.GetEffectiveAction(primary);
                    if (effective?.SubActions?.ElementAtOrDefault(child) != null)
                    {
                        bool sameBranch = true;
                        if (effective.IsInherited)
                        {
                            var global = ConfigManager.GetGlobalProfile();
                            int index = profile.ActiveLayerIndex < global.Layers.Count ? profile.ActiveLayerIndex : 0;
                            int globalCount = global.Layers[index].SectorCount;
                            sameBranch = WheelProfile.MapInheritedSector(primary, oldCount, globalCount) ==
                                WheelProfile.MapInheritedSector(next, newCount, globalCount);
                        }
                        if (sameBranch) dest = FixedRingMenu.SecondaryKey(next, child);
                    }
                }
                ownKeys[key] = dest;
            }
            CollectParentChanges(layer, ownKeys, changes);
        }
        if (!IsInheritanceSource(profile)) return changes;
        var moved = new Dictionary<int, int?>();
        var configuredBefore = new HashSet<int>();
        var configuredAfter = new HashSet<int>();
        for (int primary = 0; primary < oldCount && primary < layer.Actions.Count; primary++)
        {
            var root = layer.Actions[primary];
            if (!WheelProfile.IsActionConfigured(root) || root.SubActions == null) continue;
            configuredBefore.Add(primary);
            bool survives = destinations.TryGetValue(primary, out int next);
            if (survives && WheelProfile.IsActionConfigured(nextActions[next])) configuredAfter.Add(next);
            for (int child = 0; child < root.SubActions.Count; child++)
                moved[FixedRingMenu.SecondaryKey(primary, child)] = survives ? FixedRingMenu.SecondaryKey(next, child) : null;
        }
        foreach (var receiver in ConfigManager.CurrentConfig.Profiles)
        {
            if (ReferenceEquals(profile, receiver) || receiver.Layers == null) continue;
            for (int i = 0; i < receiver.Layers.Count; i++)
            {
                if ((i < profile.Layers.Count ? i : 0) != profile.ActiveLayerIndex) continue;
                var local = receiver.Layers[i];
                if (local?.FixedRings?.Tiers?.FirstOrDefault()?.Slots is not { Count: > 0 }) continue;
                var translated = TranslateInheritedParents(receiver, local, oldCount, moved, configuredBefore, configuredAfter, newCount);
                CollectParentChanges(local, translated, changes);
            }
        }
        return changes;
    }
}
