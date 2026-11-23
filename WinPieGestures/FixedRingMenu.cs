using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace WinPieGestures;

public sealed class FixedRingSlot
{
    // Legacy metadata retained for profile round-tripping. Fixed slots are addressed by position.
    public int? ParentIndex { get; set; }
    public ActionItem Action { get; set; } = new();

    internal FixedRingSlot Clone() => new() { ParentIndex = ParentIndex, Action = Action?.Clone()! };
}

public sealed class FixedRingTier
{
    public int? Count { get; set; }
    // Preserve values supplied by previous preview builds when round-tripping their profiles.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? WakeBandWidth { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? DwellMs { get; set; }
    public List<FixedRingSlot?> Slots { get; set; } = new();

    internal FixedRingTier Clone() => new()
    {
        Count = Count, WakeBandWidth = WakeBandWidth, DwellMs = DwellMs,
        Slots = Slots?.Select(slot => slot?.Clone()).ToList() ?? new()
    };
}

public sealed class FixedRingMenu
{
    public bool AlwaysVisible { get; set; } = true;
    internal bool ShowsAllOnPopup(AppConfig config) => AlwaysVisible && config.EnableMultiTier &&
        config.SubmenuStyle is "Wheel" or "Fan";

    // Entry zero is level three. The first two levels are the existing primary action and its SubActions.
    public List<FixedRingTier?> Tiers { get; set; } = new();

    private static void CheckLevel(int level)
    {
        if (level is < 3 or > 5) throw new ArgumentOutOfRangeException(nameof(level));
    }

    public static int RecommendedCount(int level) => level switch
    {
        3 => 12, 4 => 16, 5 => 20, _ => throw new ArgumentOutOfRangeException(nameof(level))
    };

    public static int MaximumCount(int level) => level switch
    {
        3 => 23, 4 => 30, 5 => 37, _ => throw new ArgumentOutOfRangeException(nameof(level))
    };

    private FixedRingTier? ReadTier(int level)
    {
        CheckLevel(level);
        int i = level - 3;
        return Tiers != null && i < Tiers.Count ? Tiers[i] : null;
    }

    private FixedRingTier EditTier(int level)
    {
        CheckLevel(level);
        Tiers ??= new();
        while (Tiers.Count <= level - 3) Tiers.Add(null);
        return Tiers[level - 3] ??= new();
    }

    public int GetCount(int level) => Math.Clamp(ReadTier(level)?.Count ?? RecommendedCount(level), 4, MaximumCount(level));
    public void SetCount(int level, int count) => EditTier(level).Count = Math.Clamp(count, 4, MaximumCount(level));
    public FixedRingSlot? GetSlot(int level, int index)
    {
        var slots = ReadTier(level)?.Slots;
        if (slots == null || (uint)index >= (uint)slots.Count) return null;
        var slot = slots[index];
        return slot?.Action != null ? slot : null;
    }

    public void SetSlot(int level, int index, FixedRingSlot? slot)
    {
        if ((uint)index >= (uint)MaximumCount(level)) throw new ArgumentOutOfRangeException(nameof(index));
        if (slot == null && GetSlot(level, index) == null) return;
        var slots = EditTier(level).Slots ??= new();
        while (slots.Count <= index) slots.Add(null);
        slots[index] = slot;
    }

    internal HashSet<(int Level, int Index)> ConfiguredSlots(WheelProfile profile, int depth, bool fan) =>
        ConfiguredSlots(ReadEffectiveRoots(profile), depth, fan);

    internal HashSet<(int Level, int Index)> ConfiguredSlots(IReadOnlyList<ActionItem?> roots, int depth, bool fan)
    {
        var visible = new HashSet<(int Level, int Index)>();
        for (int level = 3; level <= depth; level++)
        {
            int count = GetCount(level);
            for (int index = 0; index < count; index++)
                if (GetSlot(level, index) is { } slot && Plugins.PluginInteractionSession.IsExecutableAction(slot.Action))
                    visible.Add((level, index));
        }
        AddLowerSlots(roots, 2, fan, visible, allowWhitespaceText: false);
        return visible;
    }

    internal static HashSet<(int Level, int Index)> LowerConfiguredSlots(WheelProfile profile, int depth, bool fan) =>
        LowerConfiguredSlots(ReadEffectiveRoots(profile), depth, fan);

    internal static HashSet<(int Level, int Index)> LowerConfiguredSlots(IReadOnlyList<ActionItem?> roots, int depth, bool fan)
    {
        var visible = new HashSet<(int Level, int Index)>();
        AddLowerSlots(roots, depth, fan, visible, allowWhitespaceText: true);
        return visible;
    }

    private static ActionItem?[] ReadEffectiveRoots(WheelProfile profile) =>
        Enumerable.Range(0, profile.SectorCount).Select(index => DisplayRoot(profile, index)).ToArray();

    private static void AddLowerSlots(IReadOnlyList<ActionItem?> roots, int depth, bool fan,
        HashSet<(int Level, int Index)> visible, bool allowWhitespaceText)
    {
        for (int index = 0; index < roots.Count; index++)
        {
            var root = roots[index];
            if (!CanNavigate(root)) continue;

            bool hasChild = false;
            if (depth >= 2 && root!.SubActions is { } children)
            {
                int count = DeepMenuFrame.VisibleSlotCount(children.Count, fan);
                for (int child = 0; child < count; child++)
                {
                    int key = SecondaryKey(index, child);
                    var action = children[child];
                    if (!IsLegacySlotVisible(action, depth - 2, fan, allowWhitespaceText)) continue;
                    hasChild = true;
                    visible.Add((2, key));
                }
            }
            if (hasChild || Plugins.PluginInteractionSession.IsExecutableAction(root, allowWhitespaceText)) visible.Add((1, index));
        }
    }

    internal static bool IsLegacySlotVisible(ActionItem? action, int remainingLevels, bool fan, bool allowWhitespaceText = false)
    {
        if (!CanNavigate(action)) return false;
        if (Plugins.PluginInteractionSession.IsExecutableAction(action, allowWhitespaceText)) return true;
        if (remainingLevels <= 0 || action!.SubActions is not { } children) return false;

        int visible = DeepMenuFrame.VisibleSlotCount(children.Count, fan);
        for (int index = 0; index < visible; index++)
            if (IsLegacySlotVisible(children[index], remainingLevels - 1, fan)) return true;
        return false;
    }

    internal static ActionItem? DisplayRoot(WheelProfile profile, int index)
    {
        var root = profile.GetEffectiveAction(index);
        return CanNavigate(root) ? root : null;
    }

    private static bool CanNavigate(ActionItem? action) => action != null &&
        !string.Equals(action.Type, "None", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(action.Type, "Inherit", StringComparison.OrdinalIgnoreCase);

    internal IEnumerable<ActionItem> StoredActions()
    {
        if (Tiers == null) yield break;
        foreach (var tier in Tiers)
        {
            if (tier?.Slots == null) continue;
            foreach (var slot in tier.Slots)
                if (slot?.Action != null) yield return slot.Action;
        }
    }

    public FixedRingMenu Clone() => new()
    {
        AlwaysVisible = AlwaysVisible, Tiers = Tiers?.Select(tier => tier?.Clone()).ToList() ?? new()
    };

    // The compact range preserves the four usual child addresses. Imported Wheel layouts
    // can contain more children; interleave their indices separately to avoid collisions.
    private const int ExtendedSecondary = 128;
    public static int SecondaryKey(int primaryIndex, int childIndex)
    {
        if ((uint)primaryIndex >= 32 || childIndex < 0) throw new ArgumentOutOfRangeException();
        return childIndex < 4 ? primaryIndex * 4 + childIndex :
            checked(ExtendedSecondary + (childIndex - 4) * 32 + primaryIndex);
    }

    public static bool TryDecodeSecondaryKey(int key, out int primary, out int child)
    {
        primary = key < ExtendedSecondary ? key / 4 : (key - ExtendedSecondary) % 32;
        child = key < ExtendedSecondary ? key % 4 : 4 + (key - ExtendedSecondary) / 32;
        return key >= 0;
    }

    public static FixedRingMenu FromLegacy(WheelProfile profile)
    {
        var menu = new FixedRingMenu();
        var layer = profile.GetActiveLayer();
        var parents = new List<(int Index, ActionItem Node, double Turn)>();
        for (int i = 0; i < layer.SectorCount; i++)
        {
            var root = profile.GetEffectiveAction(i);
            if (root?.SubActions == null) continue;
            for (int j = 0; j < root.SubActions.Count; j++)
                if (root.SubActions[j] is { } child)
                    parents.Add((SecondaryKey(i,j), child, (i + (j + .5) / root.SubActions.Count - .5) / layer.SectorCount));
        }
        int previousCount = Math.Max(1, layer.SectorCount);
        for (int level = 3; level <= 5 && parents.Count > 0; level++)
        {
            int total = parents.Sum(p => p.Node.SubActions?.Count ?? 0);
            if (total == 0) break;
            int count = Math.Clamp(total, RecommendedCount(level), MaximumCount(level));
            var tier = menu.EditTier(level);
            tier.Count = count;
            var descendants = new List<(int Index, ActionItem Node, double Turn)>();
            // Reserve one global position per child, close to its parent's original direction.
            // Overflow stays in the stored tier and the unchanged legacy tree remains available.
            foreach (var (index, parent, turn) in parents)
            {
                if (parent.SubActions == null) continue;
                for (int child = 0; child < parent.SubActions.Count; child++)
                {
                    if (parent.SubActions[child] is not { } action) continue;
                    double direction = turn + ((child + .5) / parent.SubActions.Count - .5) / previousCount;
                    int target = (int)Math.Round(direction * count);
                    int best = -1, distance = int.MaxValue;
                    for (int i = 0; i < count; i++)
                    {
                        if (i < tier.Slots.Count && tier.Slots[i] != null) continue;
                        int delta = Math.Abs(i - target) % count;
                        delta = Math.Min(delta, count - delta);
                        if (delta < distance) { distance = delta; best = i; }
                    }
                    if (best < 0) best = tier.Slots.Count;
                    while (tier.Slots.Count <= best) tier.Slots.Add(null);
                    var copy = action.CloneWithoutSubActions();
                    copy.IsInherited = false;
                    tier.Slots[best] = new() { ParentIndex = index, Action = copy };
                    descendants.Add((best, action, (double)best / count));
                }
            }
            parents = descendants;
            previousCount = count;
        }
        return menu;
    }
}
