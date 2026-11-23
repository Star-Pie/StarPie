using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinPieGestures.Plugins;
using PathShape = System.Windows.Shapes.Path;
using DockEntry = WinPieGestures.ActionPaletteDock.Entry;

namespace WinPieGestures;

public partial class SettingsWindow
{
    // A tree path includes its primary slot. Fixed outer slots use their absolute ring number and index.
    private sealed record PaletteSlot(int[] Path, int FixedLevel = 0, bool Vacant = false)
    {
        internal int Index => Path[^1];
        internal int Level => FixedLevel > 0 ? FixedLevel : Path[0] < 0 ? 0 : Path.Length;
        internal string Caption => Level == 0 ? I18n.T("CenterCoreTitle") :
            I18n.TF("CanvasEditLevel", Level, string.Join(" › ", Path.Select(i => (i + 1).ToString("00"))));
    }
    private sealed record PaletteHit(Geometry Shape, PaletteSlot Slot);
    private sealed record Placement(PaletteSlot Slot, ActionItem? Previous);
    private readonly List<PaletteHit> _paletteHits = new();
    private readonly List<PathShape> _paletteGuides = new();
    private int _paletteRenderVersion;
    private (WheelProfile Profile, WheelLayer Layer, int Version)? _paletteOrigin;
    private string? _palettePreviousCaption;
    private Brush? _palettePreviousInk;
    private Geometry? _paletteHighlight;
    private Geometry? _paletteLastShape;
    private Placement? _paletteLastPlacement;

    private void InitializeStudioPalette()
    {
        StudioPalette.Activated += ApplyPaletteSelection;
        StudioPalette.DragStarted += BeginPaletteDrag;
        StudioPalette.DragPosition += (entry, e, drop) =>
        {
            var view = MappingsPreviewViewportContainer;
            // Explicit hit testing ignores the dock's mouse capture and respects
            // clipping, the zoom toolbar, and either drawer covering the viewport.
            var surface = InputHitTest(e.GetPosition(this)) as DependencyObject;
            bool inside = surface == view || (surface is Visual && view.IsAncestorOf(surface));
            TrackPaletteDrag(entry, e.GetPosition(MappingsWheelPreviewCanvas), inside, drop);
        };
        StudioPalette.DragEnded += EndPaletteDrag;
        Deactivated += (_, _) => StudioPalette.CancelGesture();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && StudioPalette.CancelGesture()) e.Handled = true;
        };
    }

    private void RegisterPaletteSecondaryVacancy(WheelProfile profile, int slot, Geometry shape) =>
        _paletteHits.Add(new(shape, new([slot, 0], Vacant: true)));

    private void RegisterPaletteTreeHit(Geometry shape, int[] path, bool vacant = false) =>
        _paletteHits.Add(new(shape, new(path, Vacant: vacant)));

    private PaletteHit? PaletteHitAt(Point point)
    {
        for (int i = _paletteHits.Count - 1; i >= 0; i--)
            if (_paletteHits[i].Shape.FillContains(point)) return _paletteHits[i];
        var hit = HitTestCanvasPoint(point);
        if (hit == null) return null;
        return new(hit.Geometry, hit.FixedLevel is >= 3 ? new([hit.SlotIndex], hit.FixedLevel.Value) :
            new(hit.SubIndex is int child ? [hit.SlotIndex, child] : [hit.IsCenter ? -1 : hit.SlotIndex]));
    }

    private void BeginPaletteDrag(DockEntry entry)
    {
        if (!entry.Available || GetSelectedProfile() is not { } profile || MappingsViewModeCanvasRadio.IsChecked != true) return;
        _paletteOrigin = (profile, profile.GetActiveLayer(), _paletteRenderVersion);
        _palettePreviousCaption = MappingsCurrentEditIndicator.Text;
        _palettePreviousInk = MappingsCurrentEditIndicator.Foreground;
        foreach (var hit in _paletteHits.Where(h => h.Slot.Vacant))
        {
            var guide = new PathShape { Data = hit.Shape, IsHitTestVisible = false, StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection { 3, 2 }, Opacity = .72 };
            guide.SetResourceReference(PathShape.StrokeProperty, "AccentPrimaryBrush");
            Panel.SetZIndex(guide, 90);
            MappingsWheelPreviewCanvas.Children.Add(guide);
            _paletteGuides.Add(guide);
        }
    }

    private void EndPaletteDrag()
    {
        if (_paletteOrigin == null) return;
        foreach (var guide in _paletteGuides) MappingsWheelPreviewCanvas.Children.Remove(guide);
        _paletteGuides.Clear();
        if (_paletteHighlight != null) HideCanvasDragOverlays();
        _paletteHighlight = null;
        _paletteLastShape = null;
        _paletteLastPlacement = null;
        if (_palettePreviousCaption != null && _paletteOrigin.Value.Version == _paletteRenderVersion)
        {
            MappingsCurrentEditIndicator.Text = _palettePreviousCaption;
            MappingsCurrentEditIndicator.Foreground = _palettePreviousInk;
        }
        _paletteOrigin = null;
    }

    private bool TrackPaletteDrag(DockEntry entry, Point point, bool inside, bool drop)
    {
        if (_paletteOrigin is not { } from || from.Version != _paletteRenderVersion ||
            !ReferenceEquals(GetSelectedProfile(), from.Profile) || !ReferenceEquals(GestureController.CaptureReleaseLayer(from.Profile), from.Layer) ||
            MappingsViewModeCanvasRadio.IsChecked != true) return false;
        var hit = inside ? PaletteHitAt(point) : null;
        // An inherited node may require resolving a cloned tree. Resolve it once
        // per hovered shape and revalidate against the current model on release.
        if (drop || !ReferenceEquals(hit?.Shape, _paletteLastShape))
        {
            _paletteLastShape = hit?.Shape;
            _paletteLastPlacement = hit == null || (drop && !entry.Available) ? null :
                PlanPalettePlacement(from.Profile, hit.Slot, entry);
        }
        var placement = _paletteLastPlacement;
        if (placement == null)
        {
            if (_paletteHighlight != null) HideCanvasDragOverlays();
            _paletteHighlight = null;
            MappingsCurrentEditIndicator.Text = I18n.T(inside ? "StudioDropUnavailable" : "EditDragCancel");
            return false;
        }
        if (drop)
        {
            EndPaletteDrag();
            _selectedMultiSlots.Clear();
            ApplyPalettePlacements(from.Profile, entry, [placement]);
            return true;
        }
        if (!ReferenceEquals(_paletteHighlight, hit!.Shape))
        {
            _paletteHighlight = hit.Shape;
            ShowCanvasDropHighlight(new CanvasHitItem { Geometry = hit.Shape }, false);
        }
        MappingsCurrentEditIndicator.Text = I18n.TF(WheelProfile.IsActionConfigured(placement.Previous) ? "StudioReplaceAction" : "StudioPlaceAction",
            I18n.T(entry.Label), placement.Slot.Caption);
        MappingsCurrentEditIndicator.SetResourceReference(TextBlock.ForegroundProperty, "AccentPrimaryBrush");
        return true;
    }

    private void ApplyPaletteSelection(DockEntry entry)
    {
        if (!_isUiInitialized || _isUpdatingUi || !entry.Available || MappingsViewModeCanvasRadio.IsChecked != true || GetSelectedProfile() is not { } profile) return;
        IEnumerable<PaletteSlot> slots = EditingOuterFixedSlot ? [new([_fixedIndices[_fixedLevel]], _fixedLevel)] :
            HasDeepCanvasContext(profile) && _deepBoundPath != null ? [new(_deepBoundPath)] :
            _selectedMultiSlots.Count > 1 ? _selectedMultiSlots.Distinct().Select(slot => new PaletteSlot([slot])) :
            [new(_selectedSubActionIndex is int child ? [_selectedSlotIndex, child] : [_selectedSlotIndex])];
        var placements = new List<Placement>();
        foreach (var slot in slots)
        {
            if (PlanPalettePlacement(profile, slot, entry) is not { } plan)
            {
                MappingsCurrentEditIndicator.Text = I18n.T("StudioDropUnavailable");
                return;
            }
            placements.Add(plan);
        }
        ApplyPalettePlacements(profile, entry, placements);
    }

    private Placement? PlanPalettePlacement(WheelProfile profile, PaletteSlot slot, DockEntry entry)
    {
        if (slot.Path.Length == 0) return null;
        var layer = profile.GetActiveLayer();
        var menu = layer.ActiveFixedRings;
        int level = slot.Level;
        bool child = entry.Kind == "AddChild";
        int capacity = ConfigManager.CurrentConfig.SubmenuStyle == "Fan" ? RadialWindow.FanSubmenuSlotCount : 4;
        if (level == 0) return child ? null : new(slot, profile.GetEffectiveCenterAction() ?? profile.CenterAction);
        if (level > layer.EffectiveMaxMenuDepth) return null;

        if (slot.FixedLevel >= 3)
        {
            if (menu == null || (uint)slot.Index >= (uint)menu.GetCount(level)) return null;
            var previous = menu.GetSlot(level, slot.Index);
            if (!child) return new(slot, previous?.Action);
            if (previous == null || level >= layer.EffectiveMaxMenuDepth) return null;
            var geometry = FixedGeometry(profile);
            return NearestEmptyFixedSlot(menu, geometry, level + 1, geometry.GetCenter(level, slot.Index, menu.GetCount(level)));
        }

        int root = slot.Path[0];
        if ((uint)root >= (uint)profile.Actions.Count || root >= profile.SectorCount) return null;
        var source = GetCanvasRoot(profile, root);
        if (slot.Vacant)
        {
            var owner = GetCanvasNode(source, slot.Path[..^1]);
            if (owner == null || slot.Index != (owner.SubActions?.Count ?? 0) || slot.Index >= capacity) return null;
            return new(slot, null);
        }
        var node = GetCanvasNode(source, slot.Path);
        if (node == null) return null;
        if (!child) return new(slot, node);
        if (level >= layer.EffectiveMaxMenuDepth) return null;
        if (menu != null && level == 2)
        {
            var geometry = FixedGeometry(profile);
            var (_, center) = geometry.Secondary(ConfigManager.CurrentConfig, root, slot.Index, source!.SubActions.Count);
            return NearestEmptyFixedSlot(menu, geometry, 3, center);
        }
        int count = node.SubActions?.Count ?? 0;
        return count < capacity ? new(new([..slot.Path, count], Vacant: true), null) : null;
    }

    private static Placement? NearestEmptyFixedSlot(FixedRingMenu menu, FixedRingGeometry geometry, int level, Point origin)
    {
        int closest = -1, count = menu.GetCount(level);
        double distance = double.PositiveInfinity;
        for (int index = 0; index < count; index++)
        {
            if (menu.GetSlot(level, index) != null) continue;
            double candidate = (geometry.GetCenter(level, index, count) - origin).LengthSquared;
            if (candidate >= distance) continue;
            distance = candidate;
            closest = index;
        }
        return closest < 0 ? null : new(new([closest], level), null);
    }

    private void ApplyPalettePlacements(WheelProfile profile, DockEntry entry, List<Placement> placements)
    {
        if (placements.Count == 0) return;
        if (entry.Kind == "Macro" && !PluginHost.TryGetAction(DockEntry.MacroId + ".macro", out _) &&
            !PluginHost.LoadActionsForBinding(DockEntry.MacroId).Any(action => action.FullId == DockEntry.MacroId + ".macro"))
        {
            MappingsCurrentEditIndicator.Text = I18n.T("StudioProviderUnavailable");
            return;
        }
        ShowStudioActionInspector();
        var edits = new List<IActionTransaction>();
        var address = GetCurrentAddress();
        foreach (var placement in placements)
        {
            var slot = placement.Slot;
            var value = entry.CreateAction();
            if (entry.Kind == "AddChild") value.Name = I18n.TF("EditSubActionNumber", slot.Index + 1);
            if (slot.FixedLevel >= 3)
            {
                WritePaletteFixedSlot(profile, placement, value);
                return;
            }
            int root = slot.Path[0];
            var before = CaptureSectorSnapshot(profile, root);
            if (slot.Level == 0)
            {
                profile.CenterAction = value;
                profile.EnableCenterAction = true;
                profile.SyncActiveLayerFromRootProperties();
            }
            else
            {
                var local = EnsureLocalPrimaryActionForEdit(profile, root)!;
                if (slot.Level == 1)
                {
                    value.SubActions = local.SubActions;
                    profile.Actions[root] = value;
                }
                else
                {
                    var parent = GetCanvasNode(local, slot.Path[..^1])!;
                    parent.SubActions ??= [];
                    if (slot.Vacant)
                    {
                        if (parent.Type is "None" or "Inherit") parent.Type = "Hotkey";
                        parent.SubActions.Add(value);
                    }
                    else
                    {
                        var previous = parent.SubActions[slot.Index];
                        value.SubActions = previous.SubActions;
                        if (slot.Level == 2)
                            _secondaryIdentities.Add(value, _secondaryIdentities.GetValue(previous, _ => new object()));
                        parent.SubActions[slot.Index] = value;
                    }
                    if (!string.Equals(profile.ProcessName, "Global", StringComparison.OrdinalIgnoreCase) &&
                        local.Type == "Hotkey" && !WheelProfile.IsActionConfigured(local)) local.OverrideGlobalMenu = true;
                }
            }
            var after = CaptureSectorSnapshot(profile, root);
            if (!AreSnapshotsEquivalent(before, after))
            {
                var change = new SingleSectorTransaction("StudioApplyAction", address with { SlotIndex = root,
                    SubIndex = slot.Path.Length > 1 ? slot.Path[1] : null }, before, after);
                edits.Add(root < 0 ? change : FollowSecondaryParents(change, (root, before, after)));
            }
        }
        if (edits.Count != 0)
            _transactionHistory.Push(edits.Count == 1 ? edits[0] : new PaletteGroup(address, edits));
        if (_selectedMultiSlots.Count <= 1) SelectPaletteTreeSlot(profile, placements[0].Slot);
        if (edits.Count != 0) OnTransactionApplied(address);
    }

    private sealed record PaletteGroup(ActionAddress Address, List<IActionTransaction> Changes) : IActionTransaction
    {
        public string Description => "StudioApplyAction";
        public int ItemsCount => Changes.Sum(c => c.ItemsCount);
        public bool MatchesContext(WheelProfile? profile, int layer) => Address.MatchesContext(profile, layer);
        public bool MatchesContext(string? name, int layer) => Address.MatchesContext(name, layer);
        public void Undo(SettingsWindow window) { foreach (var change in Changes.AsEnumerable().Reverse()) change.Undo(window); }
        public void Redo(SettingsWindow window) { foreach (var change in Changes) change.Redo(window); }
    }

    private void SelectPaletteTreeSlot(WheelProfile profile, PaletteSlot slot)
    {
        var path = slot.Path;
        if (slot.Level == 0) SelectCenterCore();
        else if (slot.Level == 1) SelectPrimarySlot(path[0]);
        else if (slot.Level == 2) SelectSubAction(path[0], path[1]);
        else
        {
            if (_fixedLayer != null) CloseFixedCanvas();
            _selectedSlotIndex = path[0];
            _selectedSubActionIndex = path[1];
            _deepCanvasProfile = profile;
            _deepCanvasLayer = profile.GetActiveLayer();
            _deepCanvasParent = path[..^1];
            _deepCanvasChild = path[^1];
            RenderMappingsWheelPreview();
        }
    }

    private void WritePaletteFixedSlot(WheelProfile profile, Placement placement, ActionItem value)
    {
        var menu = profile.GetActiveLayer().ActiveFixedRings!;
        int level = placement.Slot.FixedLevel, index = placement.Slot.Index;
        int tierCount = menu.Tiers.Count;
        var oldTier = menu.Tiers.ElementAtOrDefault(level - 3);
        int length = oldTier?.Slots.Count ?? 0;
        var before = menu.GetSlot(level, index)?.Clone();
        if (before != null) value.SubActions = before.Action.SubActions;
        if (AreActionItemsEquivalent(before?.Action, value)) return;
        var after = new FixedRingSlot { Action = value, ParentIndex = before?.ParentIndex };
        if (!EditingOuterFixedSlot) BeginFixedCanvas(profile);
        _fixedLevel = level;
        _fixedIndices[level] = index;
        CommitFixedChange(profile, "StudioApplyAction", layer =>
        {
            if (!ReferenceEquals(layer.ActiveFixedRings, menu)) return;
            menu.SetSlot(level, index, before?.Clone());
            var tier = menu.Tiers[level - 3]!;
            if (tier.Slots.Count > length && tier.Slots.Skip(length).All(s => s == null))
                tier.Slots.RemoveRange(length, tier.Slots.Count - length);
            if (oldTier == null && tier.Slots.Count == 0) menu.Tiers[level - 3] = null;
            if (menu.Tiers.Count > tierCount && menu.Tiers.Skip(tierCount).All(t => t == null))
                menu.Tiers.RemoveRange(tierCount, menu.Tiers.Count - tierCount);
        }, layer =>
        {
            if (ReferenceEquals(layer.ActiveFixedRings, menu)) menu.SetSlot(level, index, after.Clone());
        });
    }
}
