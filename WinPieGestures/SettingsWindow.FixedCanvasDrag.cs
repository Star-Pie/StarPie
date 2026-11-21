using System.Windows;
using System.Windows.Media;

namespace WinPieGestures;

public partial class SettingsWindow
{
    private CanvasHitItem? _canvasDragSource;

    private void RegisterFixedCanvasHit(WheelProfile profile, FixedRingMenu menu, Geometry shape, double scale,
        int level, int key, ActionItem? action)
    {
        var geometry = new GeometryGroup
        {
            Children = { shape }, Transform = new MatrixTransform(scale, 0, 0, scale, 150, 150)
        };
        geometry.Freeze();
        int slot = key;
        int? sub = null;
        if (level == 2 && FixedRingMenu.TryDecodeSecondaryKey(key, out int primary, out int child))
        {
            slot = primary;
            sub = child;
        }
        _canvasHitItems.Add(new CanvasHitItem
        {
            Geometry = geometry, Bounds = geometry.Bounds, FixedLevel = level, FixedMenu = menu,
            IsCenter = level == 0, IsSubAction = level == 2, SlotIndex = slot, SubIndex = sub, Action = action,
            Address = new(profile.ProcessName, profile.ActiveLayerIndex, slot, sub, GetProfileInstanceId(profile), _fixedLayer)
        });
    }

    private void BeginCanvasSlotDrag(Point position, CanvasHitItem? hit)
    {
        _mappingsDragStartPos = position;
        _canvasDragSource = hit;
        _dragSourceSlotIndex = hit == null ? -999 : hit.IsSubAction ? -100 : hit.SlotIndex;
        _isDraggingSlot = false;
    }

    private bool CanDropOnFixedCanvas(CanvasHitItem target)
    {
        var source = _canvasDragSource;
        if (source?.FixedLevel == null && target.FixedLevel == null) return true;
        if (source?.FixedLevel is not int from || target.FixedLevel is not int to || !EditingOuterFixedSlot ||
            !ReferenceEquals(source.FixedMenu, _fixedLayer!.ActiveFixedRings) ||
            !ReferenceEquals(source.FixedMenu, target.FixedMenu) || !_canvasHitItems.Contains(source) ||
            !_canvasHitItems.Contains(target) || ReferenceEquals(source, target)) return false;
        if (from < 3 && to < 3)
            return from == 2 ? to == 2 || (to == 1 && source.SlotIndex != target.SlotIndex) : to <= 1;
        return from == to && source.Action != null && ReferenceEquals(source.FixedMenu!.GetSlot(from, source.SlotIndex)?.Action, source.Action) &&
            ReferenceEquals(target.FixedMenu!.GetSlot(to, target.SlotIndex)?.Action, target.Action);
    }

    private void DropFixedSlot(WheelProfile profile, CanvasHitItem source, CanvasHitItem target, bool copy)
    {
        var menu = source.FixedMenu!;
        int level = source.FixedLevel!.Value, from = source.SlotIndex, to = target.SlotIndex;
        var original = menu.GetSlot(level, from)!.Clone();
        var displaced = menu.GetSlot(level, to)?.Clone();
        int storedCount = menu.Tiers[level - 3]!.Slots.Count;
        var bindings = new List<(int Index, int Before, int? After)>();
        // The child ring can contain hidden slots after a count reduction, or be hidden by the depth cap.
        if (level < 5 && menu.Tiers.ElementAtOrDefault(level - 2)?.Slots is { } children)
        {
            for (int i = 0; i < children.Count; i++)
            {
                if (children[i]?.ParentIndex is not int parent) continue;
                if (parent == from && !copy) bindings.Add((i, parent, to));
                else if (parent == to) bindings.Add((i, parent, copy || displaced == null ? null : from));
            }
        }

        void Write(WheelLayer layer, bool undo)
        {
            if (!ReferenceEquals(layer.FixedRings, menu)) return;
            if (!copy) menu.SetSlot(level, from, (undo ? original : displaced)?.Clone());
            menu.SetSlot(level, to, (undo ? displaced : original)?.Clone());
            foreach (var binding in bindings)
                if (menu.GetSlot(level + 1, binding.Index) is { } child)
                    child.ParentIndex = undo ? binding.Before : binding.After;
            // A move to an unallocated position grows a sparse tier. Undo its trailing padding as well.
            var slots = menu.Tiers[level - 3]!.Slots;
            if (undo && to >= storedCount && slots.Count == to + 1 && slots.Skip(storedCount).All(slot => slot == null))
                slots.RemoveRange(storedCount, slots.Count - storedCount);
        }

        _fixedLevel = level;
        _fixedIndices[level] = to;
        CommitFixedChange(profile, copy ? "EditCopySector" : "EditSwapSectors",
            layer => Write(layer, true), layer => Write(layer, false), 2 + bindings.Count);
    }
}
