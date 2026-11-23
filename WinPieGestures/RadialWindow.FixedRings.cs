using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Pen = System.Windows.Media.Pen;
using RingHit = WinPieGestures.GestureController.RingHit;
using RingSession = WinPieGestures.GestureController.FixedRingSession;

namespace WinPieGestures;

public partial class RadialWindow
{
    private bool _fixedGestureLayout;
    private FixedRingOverlay? _fixedOverlay;
    private HashSet<(int Level, int Index)>? _configuredGestureSlots;

    private ActionItem? DisplayedRoot(int index) => _configuredGestureSlots == null
        ? _profile.GetEffectiveAction(index) : FixedRingMenu.DisplayRoot(_profile, index);
    private sealed record CachedLayout(int Count, double RootRadius, double LegacyExtent, double Scale, bool Honeycomb, long Revision, FixedRingGeometry Geometry);
    private bool ExpandSecondariesOnPopup
    {
        get
        {
            var config = ConfigManager.CurrentConfig;
            if (_fixedGestureLayout && _profile != null && GestureController.CaptureReleaseLayer(_profile) is
                { EffectiveMaxMenuDepth: > 2, ActiveFixedRings: { } menu })
                return menu.ShowsAllOnPopup(config);

            return config.ExpandsSubRingsOnPopup;
        }
    }

    internal double LegacyOuterExtent { get; private set; }
    internal int ShownSecondaryParent => _activeSubTierParentSector;
    internal bool AllShownSecondaries => _allSubTiersActive;
    private CachedLayout? _fixedCache;
    internal FixedRingGeometry? FixedGeometry { get; private set; }

    // Other RadialWindow owners (including sticky/plugin sessions) keep their existing presentation.
    internal void EnableFixedGestureLayout() => _fixedGestureLayout = true;

    private bool HasHoneycomb => FixedGeometry?.IsHoneycomb == true;

    private Geometry CanvasCell((Geometry Shape, Point Label) cell, out double x, out double y)
    {
        x = _canvasCenter + cell.Label.X;
        y = _canvasCenter + cell.Label.Y;
        var shape = cell.Shape.Clone();
        shape.Transform = new TranslateTransform(_canvasCenter, _canvasCenter);
        shape.Freeze();
        return shape;
    }

    private double? PrepareFixedLayout(Point center)
    {
        var config = ConfigManager.CurrentConfig;
        var layer = GestureController.CaptureReleaseLayer(_profile);
        FixedGeometry = null;
        _configuredGestureSlots = null;
        if (!_fixedGestureLayout) return null;
        int depth = config.EnableMultiTier ? layer?.EffectiveMaxMenuDepth ?? 2 : 1;
        bool honeycomb = config.SubmenuStyle == "Fan";
        if (depth <= 2 || layer?.ActiveFixedRings is not { } menu)
        {
            _configuredGestureSlots = FixedRingMenu.LowerConfiguredSlots(_profile, depth, honeycomb);
            return null;
        }
        _configuredGestureSlots = menu.ConfiguredSlots(_profile, depth, honeycomb);
        var screen = ScreenHelper.GetScreenContextAtPoint(center);
        double marginX = config.EdgeSafeMarginX >= 0 ? config.EdgeSafeMarginX : Math.Max(0, config.EdgeSafeMargin);
        double marginY = config.EdgeSafeMarginY >= 0 ? config.EdgeSafeMarginY : Math.Max(0, config.EdgeSafeMargin);
        double limit = Math.Min(screen.PhysicalWorkArea.Width / screen.DpiScale.DpiScaleX / 2 - marginX,
            screen.PhysicalWorkArea.Height / screen.DpiScale.DpiScaleY / 2 - marginY);
        LegacyOuterExtent = config.SubmenuStyle == "Fan" ? 0 : Math.Max(config.WheelRadius, LegacyRingGeometry.SecondaryExtent(config, config.WheelRadius, config.InnerRadius));
        FixedGeometry = FitFixedGeometry(layer.SectorCount, Math.Max(1, config.WheelRadius), LegacyOuterExtent, depth, limit,
            ConfigManager.ConfigurationRevision);
        if (FixedGeometry.IsHoneycomb) LegacyOuterExtent = FixedGeometry.Outer(2);
        return FixedGeometry.OuterForCount(depth, layer.ActiveFixedRings.GetCount(depth));
    }

    private FixedRingGeometry FitFixedGeometry(int count, double rootRadius, double secondaryExtent, int depth, double limit, long revision)
    {
        var config = ConfigManager.CurrentConfig;
        bool honeycomb = config.SubmenuStyle == "Fan";
        double scale = FixedRingGeometry.FitScale(config, count, rootRadius, secondaryExtent, depth, limit);
        var cached = _fixedCache;
        if (cached != null && cached.Count == count && cached.Revision == revision && cached.RootRadius == rootRadius &&
            cached.LegacyExtent == secondaryExtent && cached.Scale == scale && cached.Honeycomb == honeycomb) return cached.Geometry;
        double start = FixedRingGeometry.GetThirdInnerRadius(config, secondaryExtent, scale, rootRadius);
        var geometry = new FixedRingGeometry(count, rootRadius, start, scale, honeycomb, config);
        _fixedCache = new(count, rootRadius, secondaryExtent, scale, honeycomb, revision, geometry);
        return geometry;
    }

    internal void ShowFixedRings(RingSession session)
    {
        ClearFixedRings();
        InvalidatePinnedArrowSlots();
        _fixedOverlay = new FixedRingOverlay(this, session);
        MainGrid.Children.Add(_fixedOverlay);
    }

    private void ClearFixedRings()
    {
        if (_fixedOverlay == null) return;
        InvalidatePinnedArrowSlots();
        MainGrid.Children.Remove(_fixedOverlay);
        _fixedOverlay = null;
    }

    internal void UpdateFixedRings(FixedRingVisibility visible, RingHit hover) => _fixedOverlay?.Select(visible, hover);

    internal void RevealFixedSecondary(int parentIndex)
    {
        Dispatcher.VerifyAccess();
        if (!_allSubTiersActive && _activeSubTierParentSector != parentIndex && (uint)parentIndex < (uint)_profile.SectorCount)
            ShowSubTier(parentIndex);
    }

    private sealed class FixedRingOverlay : FrameworkElement
    {
        private sealed record VisualCell(RingSession.Cell Cell, DrawingGroup Label);
        private readonly RingSession _session;
        private readonly RadialWindow _wheel;
        private readonly VisualCell?[][] _rows;
        private readonly Pen _border, _activeBorder;
        private FixedRingVisibility _visible;
        private FixedRingVisibility? _arrowRenderedVisibility;
        private RingHit _hover;

        internal bool ArrowFramePresented => _arrowRenderedVisibility == _visible;
        internal bool PointerControlsOuter => _session.ControlsOuter && !_session.ShowingExtendedPrimary;
        internal PinnedArrowAddress? ArrowPointerSelection => _hover.IsOuter ? new(_hover.Level, _hover.Index) : null;

        internal FixedRingOverlay(RadialWindow wheel, RingSession session)
        {
            _session = session;
            _wheel = wheel;
            _visible = session.Visibility;
            IsHitTestVisible = false;
            _border = new Pen(wheel._subSectorBorderBrush, Math.Max(.7, wheel._borderThickness));
            _activeBorder = new Pen(wheel._subHighlightBorderBrush, Math.Max(1.5, wheel._highlightBorderThickness));
            if (_border.CanFreeze) _border.Freeze();
            if (_activeBorder.CanFreeze) _activeBorder.Freeze();
            _rows = new VisualCell?[session.Rows.Length][];
            for (int level = 3; level <= _rows.Length; level++)
            {
                var cells = session.Rows[level - 1];
                var row = _rows[level - 1] = new VisualCell?[cells.Length];
                for (int i = 0; i < cells.Length; i++)
                {
                    var cell = cells[i];
                    if (cell.Display is not { } item) continue;
                    Point middle = session.Geometry.GetCenter(level, i, cells.Length);
                    double width = session.Geometry.CellWidth(level, cells.Length);
                    var label = new DrawingGroup();
                    using (var dc = label.Open())
                        wheel.DrawMenuLabel(dc, new(cell.Shape, middle, width), item, false);
                    if (label.CanFreeze) label.Freeze();
                    row[i] = new(cell, label);
                }
            }
        }

        internal void Select(FixedRingVisibility visible, RingHit hover)
        {
            if (visible == _visible && hover == _hover) return;
            if (visible != _visible) _wheel.InvalidatePinnedArrowSlots();
            _visible = visible;
            _hover = hover;
            InvalidateVisual();
        }

        internal void AppendPinnedArrowSlots(List<PinnedArrowSlot> slots)
        {
            if (_arrowRenderedVisibility is not { } rendered) return;
            for (int level = 3; level <= rendered.VisibleDepth; level++)
            {
                var row = _rows[level - 1];
                for (int i = 0; i < row.Length; i++)
                {
                    if (!rendered.Contains(level, i) || row[i] is not { } cell || cell.Cell.Shape.IsEmpty() ||
                        !_session.WasVisible(new(level, i))) continue;
                    slots.Add(new(new(level, i), _session.Geometry.GetCenter(level, i, row.Length)));
                }
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.PushTransform(new TranslateTransform(ActualWidth / 2, ActualHeight / 2));
            int visible = _visible.VisibleDepth;
            for (int level = 3; level <= visible; level++)
            {
                var row = _rows[level - 1];
                for (int i = 0; i < row.Length; i++)
                {
                    if (row[i] is not { } visual || !_visible.Contains(level, i)) continue;
                    var cell = visual.Cell;
                    bool selected = _wheel.PaintsPinnedArrowSelection(new(level, i), _hover == new RingHit(level, i));
                    dc.DrawGeometry(selected ? _wheel._subHighlightSectorBrush : _wheel._subDefaultSectorBrush,
                        selected ? _activeBorder : _border, cell.Shape);
                    dc.DrawDrawing(visual.Label);
                }
            }
            dc.Pop();
            _session.PublishFrame(_visible);
            if (_wheel._pinnedArrowSlotsChanged != null && !ArrowFramePresented)
            {
                _arrowRenderedVisibility = _visible;
                _wheel.QueuePinnedArrowRefresh();
            }
        }
    }
}
