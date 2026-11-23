namespace WinPieGestures;

// A draft belongs to one concrete menu, address and saved value. An undo, profile
// switch or replacement during a nested picker must invalidate the old editor.
internal sealed class FixedRingSlotDraft(FixedRingMenu menu, int level, int index)
{
    private readonly FixedRingSlot? _source = menu.GetSlot(level, index);
    private readonly int? _savedParent = menu.GetSlot(level, index)?.ParentIndex;
    internal ActionItem Action { get; set; } = menu.GetSlot(level, index)?.Action.Clone() ?? new() { Name = "", Type = "None" };

    internal bool Matches(FixedRingMenu? current, int selectedLevel, int selectedIndex) =>
        ReferenceEquals(menu, current) && selectedLevel == level && selectedIndex == index &&
        (uint)index < (uint)menu.GetCount(level) && ReferenceEquals(_source, menu.GetSlot(level, index)) && _savedParent == _source?.ParentIndex;

    internal FixedRingSlot? MakeSlot() => string.IsNullOrWhiteSpace(Action.Name) ? null :
        new() { Action = Action.Clone(), ParentIndex = _savedParent };

    internal static int NextEmpty(FixedRingMenu menu, int level, int index)
    {
        int count = menu.GetCount(level);
        for (int offset = 1; offset <= count; offset++)
        {
            int candidate = (index + offset) % count;
            if (menu.GetSlot(level, candidate) == null) return candidate;
        }
        return -1;
    }
}
