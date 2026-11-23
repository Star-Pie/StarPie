using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Path = System.Windows.Shapes.Path;

namespace WinPieGestures;

public partial class RadialWindow
{
    private Action<PinnedArrowSlot[]>? _pinnedArrowSlotsChanged;
    private PinnedArrowSlot[] _pinnedArrowSlots = [];
    private PinnedArrowAddress? _pinnedArrowSelection;
    private DispatcherOperation? _pinnedArrowRefresh;
    private UIElement? _pinnedArrowAwaitingOpacity;

    internal PinnedArrowAddress? PinnedArrowPointerSelection => _deepOverlay is { } deep
        ? deep.ArrowPointerSelection
        : _fixedOverlay is { PointerControlsOuter: true } outer ? outer.ArrowPointerSelection : LegacyArrowPointerSelection;

    private PinnedArrowAddress? LegacyArrowPointerSelection => _currentHighlightedSector < 0 ? null
        : _currentHighlightedSubSector >= 0 ? new(2, _currentHighlightedSubSector, _currentHighlightedSector)
        : new(1, _currentHighlightedSector);

    internal void ConfigurePinnedArrowSelection(Action<PinnedArrowSlot[]>? slotsChanged)
    {
        Dispatcher.VerifyAccess();
        ResetPinnedArrowSelection();
        _pinnedArrowSlotsChanged = slotsChanged;
    }

    internal void SetPinnedArrowSelection(PinnedArrowAddress? selection)
    {
        Dispatcher.VerifyAccess();
        if (selection is { } address && !ContainsPinnedArrowAddress(address)) selection = null;
        if (_pinnedArrowSelection == selection) return;

        var previous = _pinnedArrowSelection;
        var pointer = LegacyArrowPointerSelection;
        _pinnedArrowSelection = selection;
        if (previous is { } old && previous != pointer) PaintLegacyArrowSlot(old, false);
        if (pointer is { } focusedByPointer) PaintLegacyArrowSlot(focusedByPointer, selection == null || selection == pointer);
        if (selection is { } focusedByKeyboard && selection != pointer) PaintLegacyArrowSlot(focusedByKeyboard, true);
        _fixedOverlay?.InvalidateVisual();
        _deepOverlay?.InvalidateVisual();
    }

    private bool ContainsPinnedArrowAddress(PinnedArrowAddress address)
    {
        foreach (var slot in _pinnedArrowSlots)
            if (slot.Address == address) return true;
        return false;
    }

    private bool PaintsPinnedArrowSelection(PinnedArrowAddress address, bool pointer) =>
        _pinnedArrowSelection is { } focused ? focused == address : pointer;

    private void ResetPinnedArrowSelection()
    {
        var notify = _pinnedArrowSlotsChanged;
        if (notify == null) return;
        _pinnedArrowRefresh?.Abort();
        _pinnedArrowRefresh = null;
        WaitForPinnedArrowOpacity(null);
        SetPinnedArrowSelection(null);
        _pinnedArrowSlots = [];
        _pinnedArrowSlotsChanged = null;
        notify(_pinnedArrowSlots);
    }

    private void InvalidatePinnedArrowSlots()
    {
        if (_pinnedArrowSlotsChanged == null) return;
        WaitForPinnedArrowOpacity(null);
        if (_pinnedArrowSlots.Length > 0)
        {
            SetPinnedArrowSelection(null);
            _pinnedArrowSlots = [];
            _pinnedArrowSlotsChanged(_pinnedArrowSlots);
        }
        QueuePinnedArrowRefresh();
    }

    private void QueuePinnedArrowRefresh()
    {
        if (_pinnedArrowSlotsChanged == null || _pinnedArrowRefresh != null || !_isPresented) return;
        // The visual tree and the nested overlays must have rendered their current scope first.
        _pinnedArrowRefresh = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(RefreshPinnedArrowSlots));
    }

    private void RefreshPinnedArrowSlots()
    {
        _pinnedArrowRefresh = null;
        if (_pinnedArrowSlotsChanged == null || _isDisposed || !_isPresented || !MainGrid.IsVisible) return;
        if (MainGrid.Opacity <= 0)
        {
            WaitForPinnedArrowOpacity(MainGrid);
            return;
        }
        if (_deepOverlay is { ArrowFramePresented: false } || _fixedOverlay is { ArrowFramePresented: false }) return;

        var slots = new List<PinnedArrowSlot>();
        Path? awaitingOpacity = null;
        if (_deepOverlay is { } deep)
        {
            deep.AppendPinnedArrowSlots(slots);
        }
        else
        {
            var roots = new ActionItem?[_sectorPaths.Count];
            for (int i = 0; i < _sectorPaths.Count; i++)
            {
                var path = _sectorPaths[i];
                int index = path.Tag is int root ? root : i;
                ActionItem? action = roots[index] = DisplayedRoot(index);
                if (!path.IsVisible || path.Opacity <= 0 || path.Data == null || path.Data.IsEmpty() ||
                    !(_configuredGestureSlots?.Contains((1, index)) ?? WheelProfile.IsActionConfigured(action))) continue;
                slots.Add(new(new(1, index), PinnedArrowCenter(path, _contentPanels[i].Parent as FrameworkElement)));
            }

            for (int i = 0; i < _subSectorPaths.Count; i++)
            {
                var path = _subSectorPaths[i];
                if (!path.IsVisible || path.Data == null || path.Data.IsEmpty()) continue;
                int parent = _subSectorParentIndices[i], child = _subSectorChildIndices[i];
                var actions = roots[parent]?.SubActions;
                if (actions == null || (uint)child >= (uint)actions.Count ||
                    !(_configuredGestureSlots?.Contains((2, FixedRingMenu.SecondaryKey(parent, child))) ??
                        WheelProfile.IsActionConfigured(actions[child]))) continue;
                if (path.Opacity <= 0)
                {
                    awaitingOpacity ??= path;
                    continue;
                }
                slots.Add(new(new(2, child, parent), PinnedArrowCenter(path, _subContentContainers[i])));
            }
            _fixedOverlay?.AppendPinnedArrowSlots(slots);
        }

        WaitForPinnedArrowOpacity(awaitingOpacity);
        var next = slots.ToArray();
        if (_pinnedArrowSlots.AsSpan().SequenceEqual(next)) return;
        _pinnedArrowSlots = next;
        if (_pinnedArrowSelection is { } selected && !ContainsPinnedArrowAddress(selected)) SetPinnedArrowSelection(null);
        _pinnedArrowSlotsChanged(next);
    }

    private Point PinnedArrowCenter(Path path, FrameworkElement? content)
    {
        if (content != null)
            return new(Canvas.GetLeft(content) + content.Width / 2 - _canvasCenter,
                Canvas.GetTop(content) + content.Height / 2 - _canvasCenter);
        Rect bounds = path.Data.Bounds;
        return new(bounds.Left + bounds.Width / 2 - _canvasCenter, bounds.Top + bounds.Height / 2 - _canvasCenter);
    }

    private void WaitForPinnedArrowOpacity(UIElement? visual)
    {
        if (_pinnedArrowAwaitingOpacity == null && visual != null) CompositionTarget.Rendering += OnPinnedArrowOpacityFrame;
        else if (_pinnedArrowAwaitingOpacity != null && visual == null) CompositionTarget.Rendering -= OnPinnedArrowOpacityFrame;
        _pinnedArrowAwaitingOpacity = visual;
    }

    private void OnPinnedArrowOpacityFrame(object? sender, EventArgs e)
    {
        if (_pinnedArrowAwaitingOpacity is { Opacity: > 0 }) QueuePinnedArrowRefresh();
    }

    private void PaintLegacyArrowSlot(PinnedArrowAddress address, bool selected)
    {
        if (address.Level == 1 && (uint)address.Index < (uint)_sectorPaths.Count)
        {
            PaintWheelSector(_sectorPaths[address.Index], _contentPanels[address.Index], secondary: false, selected);
        }
        else if (address.Level == 2)
        {
            for (int i = 0; i < _subSectorPaths.Count; i++)
            {
                if (_subSectorParentIndices[i] != address.Owner || _subSectorChildIndices[i] != address.Index) continue;
                PaintWheelSector(_subSectorPaths[i], SecondarySectorLabels(_subContentContainers[i]), secondary: true, selected);
                break;
            }
        }
    }

    private static StackPanel? SecondarySectorLabels(Grid? grid)
    {
        if (grid == null) return null;
        foreach (UIElement child in grid.Children)
            if (child is StackPanel panel) return panel;
        return null;
    }

    private void PaintWheelSector(Path path, StackPanel? labels, bool secondary, bool selected)
    {
        path.Fill = secondary
            ? selected ? _subHighlightSectorBrush : _subDefaultSectorBrush
            : selected ? _highlightSectorBrush : _defaultSectorBrush;
        path.Stroke = secondary
            ? selected ? _subHighlightBorderBrush : _subSectorBorderBrush
            : selected ? _highlightBorderBrush : _sectorBorderBrush;
        path.StrokeThickness = selected ? _highlightBorderThickness : _borderThickness;
        if (secondary) ApplySubSectorGlow(path, selected);
        else _styleRenderer?.ApplySectorHighlight(path, selected);

        if (labels == null) return;
        var renderer = secondary ? _subStyleRenderer : _styleRenderer;
        Brush fallback = selected ? Brushes.White : secondary ? _subTextColorBrush : _textColorBrush;
        foreach (UIElement child in labels.Children)
        {
            if (child is TextBlock text)
            {
                text.Foreground = ArtStyles.ArtStyleRenderer.ContentBrush(renderer, text, selected, fallback);
                text.FontWeight = selected ? FontWeights.Bold : FontWeights.Medium;
            }
            else if (child is Path icon)
            {
                icon.Fill = ArtStyles.ArtStyleRenderer.ContentBrush(renderer, icon, selected, fallback);
            }
        }
    }
}
