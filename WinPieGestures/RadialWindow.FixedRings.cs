using System;
using System.Globalization;
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
    private sealed record CachedLayout(int Count, double RootRadius, double LegacyExtent, double Scale, bool Honeycomb, long Revision, FixedRingGeometry Geometry);
    private bool ExpandSecondariesOnPopup => ConfigManager.CurrentConfig.ExpandsSubRingsOnPopup ||
        (_fixedGestureLayout && _profile != null && GestureController.CaptureReleaseLayer(_profile) is { EffectiveMaxMenuDepth: > 2, ActiveFixedRings: { } menu } &&
         menu.ShowsAllOnPopup(ConfigManager.CurrentConfig));

    internal double LegacyOuterExtent { get; private set; }
    internal int ShownSecondaryParent => _activeSubTierParentSector;
    internal bool AllShownSecondaries => _allSubTiersActive;
    private CachedLayout? _fixedCache;
    internal FixedRingGeometry? FixedGeometry { get; private set; }

    // Other RadialWindow owners (including sticky/plugin sessions) keep their existing presentation.
    internal void EnableFixedGestureLayout() => _fixedGestureLayout = true;

    private double? PrepareFixedLayout(Point center)
    {
        var config = ConfigManager.CurrentConfig;
        var layer = GestureController.CaptureReleaseLayer(_profile);
        FixedGeometry = null;
        if (!_fixedGestureLayout || !config.EnableMultiTier || layer?.ActiveFixedRings == null || layer.EffectiveMaxMenuDepth <= 2) return null;
        int depth = layer.EffectiveMaxMenuDepth;
        var screen = ScreenHelper.GetScreenContextAtPoint(center);
        double marginX = config.EdgeSafeMarginX >= 0 ? config.EdgeSafeMarginX : Math.Max(0, config.EdgeSafeMargin);
        double marginY = config.EdgeSafeMarginY >= 0 ? config.EdgeSafeMarginY : Math.Max(0, config.EdgeSafeMargin);
        double limit = Math.Min(screen.PhysicalWorkArea.Width / screen.DpiScale.DpiScaleX / 2 - marginX,
            screen.PhysicalWorkArea.Height / screen.DpiScale.DpiScaleY / 2 - marginY);
        LegacyOuterExtent = Math.Max(config.WheelRadius, LegacyRingGeometry.SecondaryExtent(config, config.WheelRadius, config.InnerRadius));
        FixedGeometry = FitFixedGeometry(layer.SectorCount, Math.Max(1, config.WheelRadius), LegacyOuterExtent, depth, limit,
            ConfigManager.ConfigurationRevision);
        return FixedGeometry.OuterForCount(depth, layer.ActiveFixedRings.GetCount(depth));
    }

    private FixedRingGeometry FitFixedGeometry(int count, double rootRadius, double secondaryExtent, int depth, double limit, long revision)
    {
        // Only the new outer rings shrink. Primary and Wheel/Fan secondary coordinates stay in their original DIPs.
        double available = Math.Max(1, limit - secondaryExtent);
        bool honeycomb = ConfigManager.CurrentConfig.SubmenuStyle == "Fan";
        int slots = (_profile == null ? null : GestureController.CaptureReleaseLayer(_profile)?.ActiveFixedRings)?.GetCount(depth) ?? FixedRingMenu.MaximumCount(depth);
        double scale = Math.Min(rootRadius / 72, available / (5 + FixedRingGeometry.OuterExtension(depth, slots, honeycomb)));
        var cached = _fixedCache;
        if (cached != null && cached.Count == count && cached.Revision == revision && cached.RootRadius == rootRadius &&
            cached.LegacyExtent == secondaryExtent && cached.Scale == scale && cached.Honeycomb == honeycomb) return cached.Geometry;
        var geometry = new FixedRingGeometry(count, rootRadius, secondaryExtent + 5 * scale, scale, honeycomb);
        _fixedCache = new(count, rootRadius, secondaryExtent, scale, honeycomb, revision, geometry);
        return geometry;
    }

    internal void ShowFixedRings(RingSession session)
    {
        ClearFixedRings();
        _fixedOverlay = new FixedRingOverlay(this, session);
        MainGrid.Children.Add(_fixedOverlay);
    }

    private void ClearFixedRings()
    {
        if (_fixedOverlay == null) return;
        MainGrid.Children.Remove(_fixedOverlay);
        _fixedOverlay = null;
    }

    internal void UpdateFixedRings(int[] path, RingHit hover) => _fixedOverlay?.Select(path, hover);

    private sealed class FixedRingOverlay : FrameworkElement
    {
        private sealed record VisualCell(RingSession.Cell Cell, DrawingGroup Label, DrawingGroup Number);
        private readonly RingSession _session;
        private readonly RadialWindow _wheel;
        private readonly bool _persistent;
        private readonly VisualCell[][] _rows;
        private readonly Pen _border, _activeBorder, _emptyBorder;
        private int[] _path = [];
        private RingHit _hover;

        internal FixedRingOverlay(RadialWindow wheel, RingSession session)
        {
            _session = session;
            _wheel = wheel;
            _persistent = session.Persistent;
            IsHitTestVisible = false;
            _border = new Pen(wheel._subSectorBorderBrush, Math.Max(.7, wheel._borderThickness));
            _activeBorder = new Pen(wheel._subHighlightBorderBrush, Math.Max(1.5, wheel._highlightBorderThickness));
            _emptyBorder = new Pen(wheel._subSectorBorderBrush, .75) { DashStyle = DashStyles.Dash };
            if (_border.CanFreeze) _border.Freeze();
            if (_activeBorder.CanFreeze) _activeBorder.Freeze();
            if (_emptyBorder.CanFreeze) _emptyBorder.Freeze();
            _rows = new VisualCell[session.Rows.Length][];
            for (int level = 3; level <= _rows.Length; level++)
            {
                var cells = session.Rows[level - 1];
                var row = _rows[level - 1] = new VisualCell[cells.Length];
                for (int i = 0; i < cells.Length; i++)
                {
                    var cell = cells[i];
                    Point middle = session.Geometry.GetCenter(level, i, cells.Length);
                    double width = session.Geometry.CellWidth(level, cells.Length);
                    var number = new DrawingGroup();
                    using (var dc = number.Open())
                        wheel.DrawMenuText(dc, (i + 1).ToString("00", CultureInfo.CurrentCulture),
                            middle, width, Math.Min(12, width / 2 + 1), wheel._subTextColorBrush);
                    number.Freeze();
                    var label = number;
                    if (cell.Display is { } item)
                    {
                        label = new DrawingGroup();
                        using (var dc = label.Open())
                            wheel.DrawMenuLabel(dc, new(cell.Shape, middle, width), item, false);
                        if (label.CanFreeze) label.Freeze();
                    }
                    row[i] = new(cell, label, number);
                }
            }
        }

        internal void Select(int[] path, RingHit hover)
        {
            if (ReferenceEquals(path, _path) && hover == _hover) return;
            _path = path;
            _hover = hover;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.PushTransform(new TranslateTransform(ActualWidth / 2, ActualHeight / 2));
            int visible = _persistent ? _rows.Length : _path.Length + 2;
            for (int level = 3; level <= visible; level++)
            {
                var row = _rows[level - 1];
                for (int i = 0; i < row.Length; i++)
                {
                    var visual = row[i];
                    var cell = visual.Cell;
                    if (!cell.IsVisible(_persistent, level, _path)) continue;
                    bool configured = cell.Display != null;
                    bool selected = _hover == new RingHit(level, i);
                    bool path = level - 2 < _path.Length && _path[level - 2] == i;
                    if (!configured) dc.PushOpacity(.48);
                    dc.DrawGeometry(selected || path ? _wheel._subHighlightSectorBrush : _wheel._subDefaultSectorBrush,
                        selected || path ? _activeBorder : configured ? _border : _emptyBorder, cell.Shape);
                    dc.DrawDrawing(configured ? visual.Label : visual.Number);
                    if (!configured) dc.Pop();
                }
            }
            dc.Pop();
            _session.PublishFrame(_path);
        }
    }
}
