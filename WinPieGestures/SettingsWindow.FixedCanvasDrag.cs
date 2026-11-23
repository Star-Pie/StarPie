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
        if (level == 1 && (action?.SubActions?.Count ?? 0) == 0)
        {
            var cfg = ConfigManager.CurrentConfig;
            var vacant = FixedGeometry(profile).Secondary(cfg, key, 0, 1);
            var guide = new GeometryGroup { Children = { vacant.Shape }, Transform = geometry.Transform };
            guide.Freeze();
            RegisterFixedSecondaryVacancy(profile, menu, key, guide);
        }
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

    private void RegisterFixedSecondaryVacancy(WheelProfile profile, FixedRingMenu menu, int primary, Geometry guide) =>
        _canvasHitItems.Add(new CanvasHitItem
        {
            Geometry = guide, Bounds = guide.Bounds, FixedLevel = 2, FixedMenu = menu,
            IsDropOnly = true, IsSubAction = true, SlotIndex = primary, SubIndex = 0,
            Address = new(profile.ProcessName, profile.ActiveLayerIndex, primary, 0, GetProfileInstanceId(profile), _fixedLayer)
        });

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
        if (source?.FixedLevel is not int from || target.FixedLevel is not int to || !OnFixedCanvas ||
            !ReferenceEquals(source.FixedMenu, _fixedLayer!.ActiveFixedRings) ||
            !ReferenceEquals(source.FixedMenu, target.FixedMenu) || !_canvasHitItems.Contains(source) ||
            !_canvasHitItems.Contains(target) || ReferenceEquals(source, target)) return false;
        if (IsCrossLevelSwap(source, target))
            return IsCurrentCanvasEndpoint(source) && IsCurrentCanvasEndpoint(target) &&
                (source.Action != null || target.Action != null);
        if (target.IsDropOnly && !IsCurrentCanvasEndpoint(target)) return false;
        if (from < 3 && to < 3)
            return from == 2 ? to == 2 || (to == 1 && source.SlotIndex != target.SlotIndex) : to <= 1;
        return from == to && source.Action != null && ReferenceEquals(source.FixedMenu!.GetSlot(from, source.SlotIndex)?.Action, source.Action) &&
            ReferenceEquals(target.FixedMenu!.GetSlot(to, target.SlotIndex)?.Action, target.Action);
    }

    private static bool IsCrossLevelSwap(CanvasHitItem source, CanvasHitItem target) =>
        source.FixedLevel is >= 1 && target.FixedLevel is >= 1 && source.FixedLevel != target.FixedLevel;

    private bool IsCurrentCanvasEndpoint(CanvasHitItem hit)
    {
        int level = hit.FixedLevel!.Value;
        if (level > _fixedLayer!.EffectiveMaxMenuDepth) return false;
        if (level >= 3)
            return (uint)hit.SlotIndex < (uint)hit.FixedMenu!.GetCount(level) &&
                ReferenceEquals(hit.Action, hit.FixedMenu.GetSlot(level, hit.SlotIndex)?.Action);
        if ((uint)hit.SlotIndex >= (uint)_fixedProfile!.SectorCount) return false;
        var root = GetCanvasRoot(_fixedProfile, hit.SlotIndex);
        var current = root;
        if (level == 2)
        {
            if (root == null || string.Equals(root.Type, "None", StringComparison.OrdinalIgnoreCase)) return false;
            if (hit.IsDropOnly) return hit.SubIndex == 0 && (root.SubActions?.Count ?? 0) == 0;
            if (hit.SubIndex is not int index || root.SubActions == null || (uint)index >= (uint)
                DeepMenuFrame.VisibleSlotCount(root.SubActions.Count, ConfigManager.CurrentConfig.SubmenuStyle == "Fan")) return false;
            current = root.SubActions[index];
        }
        // Effective inherited roots are cloned on each lookup.
        return ReferenceEquals(current, hit.Action) || root?.IsInherited == true && AreActionItemsEquivalent(current, hit.Action);
    }

    private void DropFixedSlot(WheelProfile profile, CanvasHitItem source, CanvasHitItem target, bool copy)
    {
        var menu = source.FixedMenu!;
        int level = source.FixedLevel!.Value, from = source.SlotIndex, to = target.SlotIndex;
        var original = menu.GetSlot(level, from)!.Clone();
        var displaced = menu.GetSlot(level, to)?.Clone();
        var moved = new FixedRingSlot { Action = original.Action, ParentIndex = displaced?.ParentIndex };
        var swapped = displaced == null ? null : new FixedRingSlot { Action = displaced.Action, ParentIndex = original.ParentIndex };
        int storedCount = menu.Tiers[level - 3]!.Slots.Count;

        void Write(WheelLayer layer, bool undo)
        {
            if (!ReferenceEquals(layer.FixedRings, menu)) return;
            if (!copy) menu.SetSlot(level, from, (undo ? original : swapped)?.Clone());
            menu.SetSlot(level, to, (undo ? displaced : moved)?.Clone());
            // A move to an unallocated position grows a sparse tier. Undo its trailing padding as well.
            var slots = menu.Tiers[level - 3]!.Slots;
            if (undo && to >= storedCount && slots.Count == to + 1 && slots.Skip(storedCount).All(slot => slot == null))
                slots.RemoveRange(storedCount, slots.Count - storedCount);
        }

        _fixedInnerLevel = null;
        _fixedLevel = level;
        _fixedIndices[level] = to;
        CommitFixedChange(profile, copy ? "EditCopySector" : "EditSwapSectors",
            layer => Write(layer, true), layer => Write(layer, false), 2);
    }
}
