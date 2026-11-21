using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace WinPieGestures;

/// <summary>Frozen slot outlines shared by the settings preview and the presented wheel. Level one is the primary ring.</summary>
internal sealed partial class FixedRingGeometry
{
    private const double Degrees = 180.0 / Math.PI;
    private readonly int _rootCount;
    private readonly double _scale;
    private readonly double _rootScale;
    private readonly double _thirdInner;
    private readonly string _shape;
    private readonly double _gap, _corner, _thickness;
    private readonly Honeycomb? _honeycomb;
    private readonly ConcurrentDictionary<(int Level, int Index, int Count), Outline> _shapes = new();

    internal FixedRingGeometry(int rootCount, double rootOuterRadius)
        : this(rootCount, rootOuterRadius, 121 * rootOuterRadius / 72, rootOuterRadius / 72) { }

    internal FixedRingGeometry(int rootCount, double rootOuterRadius, double thirdInnerRadius, double outerScale)
        : this(rootCount, rootOuterRadius, thirdInnerRadius, outerScale, false, null) { }

    internal FixedRingGeometry(int rootCount, double rootOuterRadius, double thirdInnerRadius, double outerScale, bool honeycomb)
        : this(rootCount, rootOuterRadius, thirdInnerRadius, outerScale, honeycomb, ConfigManager.CurrentConfig) { }

    internal FixedRingGeometry(int rootCount, double rootOuterRadius, double thirdInnerRadius, double outerScale, bool honeycomb, AppConfig? config)
    {
        if (!double.IsFinite(thirdInnerRadius) || thirdInnerRadius <= 0 || !double.IsFinite(outerScale) || outerScale <= 0)
            throw new ArgumentOutOfRangeException();
        if (rootCount is < 1 or > 32 || !double.IsFinite(rootOuterRadius) || rootOuterRadius <= 0)
            throw new ArgumentOutOfRangeException();
        _rootCount = rootCount;
        _rootScale = rootOuterRadius / 72.0;
        _thirdInner = thirdInnerRadius;
        _scale = outerScale;
        double fit = _scale / _rootScale;
        _shape = config?.Shape ?? "Original";
        _gap = Math.Max(0, config?.SectorGap ?? 3.8 * _rootScale) * fit;
        _corner = Math.Max(0, config?.SectorCornerRadius ?? 4.5 * _rootScale) * fit;
        _thickness = config != null ? RingThickness(rootOuterRadius, config) * fit : 39 * _scale;
        if (honeycomb)
        {
            var (pitch, face) = HoneycombSize(rootOuterRadius, config!);
            _honeycomb = new Honeycomb(rootCount, pitch * fit, face * fit, _shape, _corner);
        }
    }

    internal bool IsHoneycomb => _honeycomb != null;
    private static double RingThickness(double radius, AppConfig config) =>
        Math.Max(2, radius - Math.Clamp(config.InnerRadius, 0, radius));

    private static (double Pitch, double Face) HoneycombSize(double radius, AppConfig config)
    {
        double thickness = RingThickness(radius, config);
        double pitch = Math.Max((2 * radius - thickness) / 6.4, (Math.Max(0, config.CoreRadius) + thickness / 2 + 1) / 3);
        double face = Math.Clamp(Math.Min(pitch, thickness / 2) - Math.Max(0, config.SectorGap) / Math.Sqrt(3), pitch * .08, pitch);
        return (pitch, face);
    }

    internal static double FitScale(AppConfig config, int roots, double radius, double secondaryExtent, int depth, double limit)
    {
        double unit = radius / 72;
        if (config.SubmenuStyle == "Fan")
        {
            var (pitch, face) = HoneycombSize(radius, config);
            return unit * Math.Min(1, Math.Max(1, limit) / Honeycomb.Extent(roots, depth, pitch, face));
        }
        double extra = (depth - 2) * (RingThickness(radius, config) + 5 * unit);
        return unit * Math.Min(1, Math.Max(1, limit - secondaryExtent) / extra);
    }

    internal double CoreRadius(double configured) => IsHoneycomb ? Math.Min(configured * _scale / _rootScale, Inner(1) * .99) : configured;

    internal (Geometry Shape, Point Label) Primary(AppConfig config, int root) => IsHoneycomb
        ? (GetShape(1, root, _rootCount), GetCenter(1, root, _rootCount))
        : LegacyRingGeometry.Primary(config, _rootCount, root, config.WheelRadius, config.InnerRadius);

    internal (Geometry Shape, Point Label) Secondary(AppConfig config, int root, int child, int count)
    {
        if (!IsHoneycomb) return LegacyRingGeometry.Secondary(config, _rootCount, root, child, count, config.WheelRadius, config.InnerRadius);
        int address = root * 3 + child;
        return (GetShape(2, address, _rootCount * 3), GetCenter(2, address, _rootCount * 3));
    }

    internal double Inner(int level) => _honeycomb != null ? _honeycomb.Inner(level) :
        level >= 3 ? _thirdInner + (_thickness + 5 * _scale) * (level - 3) : (33 + 44 * (level - 1)) * _rootScale;
    internal double Outer(int level) => _honeycomb != null ? _honeycomb.Outer(level) :
        Inner(level) + (level >= 3 ? _thickness : 39 * _rootScale);
    internal double OuterForCount(int level, int count) => _honeycomb != null ? _honeycomb.Outer(level) : Outer(level);
    internal double Scale => _scale;

    internal Point GetCenter(int level, int index, int count)
    {
        if (level == 1) count = _rootCount;
        if ((uint)index >= (uint)count || count < 1 || level is < 1 or > 5) throw new ArgumentOutOfRangeException();
        if (_honeycomb != null) return _honeycomb.Center(level, index, count);
        double angle = index * 2 * Math.PI / count, radius = (Inner(level) + Outer(level)) / 2;
        return new Point(radius * Math.Cos(angle), radius * Math.Sin(angle));
    }

    internal double CellWidth(int level, int count)
    {
        if (_honeycomb != null) return _honeycomb.Width;
        return Math.Min(Outer(level) - Inner(level), Math.PI * (Inner(level) + Outer(level)) / count);
    }

    internal Geometry GetShape(int level, int index, int count) => Get(level, index, count).Shape;
    internal int Hit(Point relativeDips, int level, int count)
    {
        if (level == 1) count = _rootCount;
        if (count < 1 || !WithinRadius(relativeDips, Inner(level), OuterForCount(level, count))) return -1;
        if (_honeycomb != null) return _honeycomb.Hit(relativeDips, level, count);
        double angle = Math.Atan2(relativeDips.Y, relativeDips.X) * Degrees;
        int index = (int)Math.Floor((angle + 360) * count / 360 + .5) % count;
        return Get(level, index, count).Contains(relativeDips) ? index : -1;
    }

    private static bool WithinRadius(Point point, double inner, double outer)
    {
        double squared = point.X * point.X + point.Y * point.Y;
        return squared >= inner * inner && squared <= outer * outer;
    }

    private Outline Get(int level, int index, int count)
    {
        if (level == 1) count = _rootCount;
        if (level is < 1 or > 5 || count < 1 || (uint)index >= (uint)count) throw new ArgumentOutOfRangeException();
        return _shapes.GetOrAdd((level,index,count), static (key, self) =>
            self.MakeOutline(key.Level, key.Index, key.Count), this);
    }

    private Outline MakeOutline(int level, int index, int count)
    {
        if (_honeycomb != null) return _honeycomb.OutlineAt(level, index, count);
        double lo = Inner(level), hi = Outer(level);
        double angle = index * 360.0 / count, half = 180.0 / count;
        // The primary renderer enforces minimum DIP sizes. Render small fitted cards at a
        // larger coordinate scale, then reduce them uniformly into their reserved ring.
        double span = Math.Min(hi - lo, (lo + hi) * Math.Sin(half / Degrees));
        double gap = Math.Min(_gap, span * .8);
        double zoom = _shape == "Original" ? 1 : Math.Max(1, 24 / (span - gap));
        var styled = IconHelper.CreateAdvancedSectorGeometry(0, 0, angle - half, angle + half,
            lo * zoom, hi * zoom, _shape, gap * zoom, _corner * zoom);
        if (zoom > 1)
        {
            var fitted = new GeometryGroup { Transform = new ScaleTransform(1 / zoom, 1 / zoom) };
            fitted.Children.Add(styled);
            styled = fitted;
        }
        return Flatten(styled, .05 * _scale);
    }

    private static Outline Flatten(Geometry original, double tolerance)
    {
        var shape = original.GetFlattenedPathGeometry(tolerance, ToleranceType.Absolute);
        // Bake in any capsule rotation so the immutable polygon and the WPF drawing share coordinates.
        var transform = shape.Transform ?? Transform.Identity;
        if (shape.Figures.Count == 0) return new Outline(Geometry.Empty, []);
        shape.Freeze();
        var figure = shape.Figures[0];
        var points = new List<Point> { figure.StartPoint };
        foreach (var segment in figure.Segments)
        {
            if (segment is PolyLineSegment poly) points.AddRange(poly.Points);
            else if (segment is LineSegment line) points.Add(line.Point);
        }
        return new Outline(shape, points.Select(transform.Transform).ToArray());
    }

    private sealed record Outline(Geometry Shape, Point[] Points)
    {
        // Geometry is prepared by the renderer; hot input reads its identical polygon without WPF allocations.
        internal bool Contains(Point p)
        {
            if (Points.Length == 0) return false;
            bool inside = false;
            Point previous = Points[^1];
            foreach (Point current in Points)
            {
                if ((current.Y > p.Y) != (previous.Y > p.Y) &&
                    p.X < (previous.X - current.X) * (p.Y - current.Y) / (previous.Y - current.Y) + current.X)
                    inside = !inside;
                previous = current;
            }
            return inside;
        }
    }
}
