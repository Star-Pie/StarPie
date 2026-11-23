using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using BitOperations = System.Numerics.BitOperations;

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
    private readonly double _corner, _ringGap;
    private readonly (double Thickness, double Gap) _third, _fourth, _fifth;
    private readonly double? _honeycombCoreRadius;
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
        var shared = config == null
            ? (Thickness: 39 * _rootScale, SectorGap: 3.8 * _rootScale, RingGap: 5 * _rootScale, CornerRadius: 4.5 * _rootScale, Shape: "Original")
            : ReadFixedTierDefaults(rootOuterRadius, config);
        _shape = shared.Shape;
        _corner = shared.CornerRadius * fit;
        _ringGap = shared.RingGap * fit;
        _third = ScaleTier(3);
        _fourth = ScaleTier(4);
        _fifth = ScaleTier(5);

        if (honeycomb && config != null)
        {
            var settings = ReadHoneycombSettings(rootOuterRadius, config);
            _honeycomb = new Honeycomb(rootCount, settings.Pitch * fit, settings.Face * fit, settings.Shape, settings.CornerRadius * fit);
            _honeycombCoreRadius = config.FixedHoneycombAppearance?.CoreRadius is double radius && double.IsFinite(radius)
                ? settings.CoreRadius : null;
        }

        (double Thickness, double Gap) ScaleTier(int level)
        {
            var tier = config?.FixedOuterRingAppearance?.GetTier(level);
            return (BoundedOverride(tier?.Thickness, 2, 160, shared.Thickness) * fit,
                BoundedOverride(tier?.SectorGap, 0, 40, shared.SectorGap) * fit);
        }
    }

    internal bool IsHoneycomb => _honeycomb != null;
    private static double RingThickness(double radius, AppConfig config) =>
        Math.Max(2, radius - Math.Clamp(config.InnerRadius, 0, radius));

    private static double BoundedOverride(double? value, double minimum, double maximum, double fallback) =>
        value is double finite && double.IsFinite(finite) ? Math.Clamp(finite, minimum, maximum) : fallback;

    private static string? KnownOuterShape(string? shape) => shape switch
    {
        "Original" or "Circle" or "HexagonHive" or "RoundedCapsule" or "FloatingCapsules" or "RoundedRect" or "Capsule" => shape,
        _ => null
    };

    internal static (double Thickness, double SectorGap, double RingGap, double CornerRadius, string Shape)
        FixedTierDefaults(AppConfig config) => ReadFixedTierDefaults(config.WheelRadius, config);

    private static (double Thickness, double SectorGap, double RingGap, double CornerRadius, string Shape)
        ReadFixedTierDefaults(double radius, AppConfig config)
    {
        var appearance = config.FixedOuterRingAppearance;
        return (
            BoundedOverride(appearance?.Thickness, 2, 160, RingThickness(radius, config)),
            BoundedOverride(appearance?.SectorGap, 0, 40, Math.Max(0, config.SectorGap)),
            BoundedOverride(appearance?.RingGap, 0, 60, 5 * radius / 72),
            BoundedOverride(appearance?.CornerRadius, 0, 40, Math.Max(0, config.SectorCornerRadius)),
            KnownOuterShape(appearance?.Shape) ?? config.Shape ?? "Original");
    }

    private static (double Pitch, double Face, double CornerRadius, double CoreRadius, string Shape)
        ReadHoneycombSettings(double radius, AppConfig config)
    {
        const double rootThree = 1.7320508075688772;
        var appearance = config.FixedHoneycombAppearance;
        double thickness = RingThickness(radius, config);
        double legacyPitch = Math.Max((2 * radius - thickness) / 6.4, (Math.Max(0, config.CoreRadius) + thickness / 2 + 1) / 3);
        double legacyFace = Math.Clamp(Math.Min(legacyPitch, thickness / 2) - Math.Max(0, config.SectorGap) / rootThree, legacyPitch * .08, legacyPitch);
        double width = BoundedOverride(appearance?.CellWidth, 4, 120, legacyFace * rootThree);
        double gap = BoundedOverride(appearance?.CellGap, 0, 60, (legacyPitch - legacyFace) * rootThree);
        double face = width / rootThree;
        double pitch = (width + gap) / rootThree;
        double core = BoundedOverride(appearance?.CoreRadius, 2, 130, config.CoreRadius);
        if (appearance?.CoreRadius is double explicitCore && double.IsFinite(explicitCore))
            pitch = Math.Max(pitch, (core / .99 + face) / 3);
        string shape = appearance?.Shape is "Circle" or "HexagonHive" ? appearance.Shape : config.Shape ?? "Original";
        return (pitch, face, BoundedOverride(appearance?.CornerRadius, 0, 40, Math.Max(0, config.SectorCornerRadius)), core, shape);
    }

    internal static (double CellWidth, double CellGap, double CornerRadius, double CoreRadius, string Shape)
        HoneycombDefaults(AppConfig config)
    {
        var settings = ReadHoneycombSettings(config.WheelRadius, config);
        double rootThree = Math.Sqrt(3);
        return (settings.Face * rootThree, (settings.Pitch - settings.Face) * rootThree,
            settings.CornerRadius, settings.CoreRadius, settings.Shape is "Original" ? "HexagonHive" : settings.Shape);
    }

    internal static double GetThirdInnerRadius(AppConfig config, double secondaryExtent, double outerScale, double rootRadius) =>
        Math.Max(double.Epsilon, secondaryExtent + ReadFixedTierDefaults(rootRadius, config).RingGap * outerScale * 72 / rootRadius);

    internal static double FitScale(AppConfig config, int roots, double radius, double secondaryExtent, int depth, double limit)
    {
        double unit = radius / 72;
        if (config.SubmenuStyle == "Fan")
        {
            var settings = ReadHoneycombSettings(radius, config);
            return unit * Math.Min(1, Math.Max(1, limit) / Honeycomb.Extent(roots, depth, settings.Pitch, settings.Face));
        }

        var shared = ReadFixedTierDefaults(radius, config);
        double extra = 0;
        for (int level = 3; level <= depth; level++)
        {
            var tier = config.FixedOuterRingAppearance?.GetTier(level);
            extra += BoundedOverride(tier?.Thickness, 2, 160, shared.Thickness) + shared.RingGap;
        }
        return unit * Math.Min(1, Math.Max(1, limit - secondaryExtent) / extra);
    }

    internal double CoreRadius(double configured) => IsHoneycomb ? CoreDeadzoneRadius(_honeycombCoreRadius ?? configured) : configured;

    internal double CoreDeadzoneRadius(double configured) => IsHoneycomb
        ? Math.Min(configured * _scale / _rootScale, Inner(1) * .99) : configured;

    internal (Geometry Shape, Point Label) Primary(AppConfig config, int root) => IsHoneycomb
        ? (GetShape(1, root, _rootCount), GetCenter(1, root, _rootCount))
        : LegacyRingGeometry.Primary(config, _rootCount, root, config.WheelRadius, config.InnerRadius);

    internal (Geometry Shape, Point Label) Secondary(AppConfig config, int root, int child, int count)
    {
        if (!IsHoneycomb) return LegacyRingGeometry.Secondary(config, _rootCount, root, child, count, config.WheelRadius, config.InnerRadius);
        int address = root * 3 + child;
        return (GetShape(2, address, _rootCount * 3), GetCenter(2, address, _rootCount * 3));
    }

    private (double Thickness, double Gap) Tier(int level) => level switch
    {
        4 => _fourth,
        5 => _fifth,
        _ => _third
    };

    internal double Inner(int level) => _honeycomb != null ? _honeycomb.Inner(level) : level switch
    {
        3 => _thirdInner,
        4 => _thirdInner + _third.Thickness + _ringGap,
        5 => _thirdInner + _third.Thickness + _fourth.Thickness + 2 * _ringGap,
        _ => (33 + 44 * (level - 1)) * _rootScale
    };
    internal double Outer(int level) => _honeycomb != null ? _honeycomb.Outer(level) :
        Inner(level) + (level >= 3 ? Tier(level).Thickness : 39 * _rootScale);
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

    internal ulong NearSlots(Point point, int level, int count, ulong configured, ulong previous)
    {
        if (configured == 0) return 0;
        double approach = Math.Clamp(CellWidth(level, count) * .65, 6, 32);
        // A slightly wider exit margin keeps the same cell visible during small pointer jitters.
        double retreat = approach * 1.25;
        if (!WithinRadius(point, Math.Max(0, Inner(level) - retreat), OuterForCount(level, count) + retreat)) return 0;

        ulong visible = 0;
        while (configured != 0)
        {
            int index = BitOperations.TrailingZeroCount(configured);
            ulong bit = 1UL << index;
            configured &= ~bit;
            double distance = (previous & bit) == 0 ? approach : retreat;
            if (Get(level, index, count).IsNear(point, distance)) visible |= bit;
        }
        return visible;
    }

    internal int Hit(Point relativeDips, int level, int count) => HitVisible(relativeDips, level, count, ulong.MaxValue);

    internal int HitVisible(Point relativeDips, int level, int count, ulong visible)
    {
        if (level == 1) count = _rootCount;
        if (visible == 0 || count < 1 || !WithinRadius(relativeDips, Inner(level), OuterForCount(level, count))) return -1;
        if (_honeycomb != null)
        {
            int cell = _honeycomb.Hit(relativeDips, level, count);
            return cell >= 0 && IsVisible(cell) ? cell : -1;
        }
        double angle = Math.Atan2(relativeDips.Y, relativeDips.X) * Degrees;
        int index = (int)Math.Floor((angle + 360) * count / 360 + .5) % count;
        // Adjacent slots can be visible while the pointer is over an empty slot.
        // Reject that slot before reaching the outline cache's geometry builder.
        return IsVisible(index) && Get(level, index, count).Contains(relativeDips) ? index : -1;

        bool IsVisible(int index) => visible == ulong.MaxValue || (uint)index < 64 && (visible & (1UL << index)) != 0;
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
        double maximumGap = _shape == "Original" ? (lo + hi) * Math.PI / count * .59 : span * .8;
        double gap = Math.Min(Tier(level).Gap, maximumGap);
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
        private Rect Bounds { get; } = Shape.Bounds;

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

        internal bool IsNear(Point point, double distance)
        {
            if (Points.Length == 0) return false;
            var area = Bounds;
            area.Inflate(distance, distance);
            if (!area.Contains(point)) return false;
            if (Contains(point)) return true;

            double limit = distance * distance;
            var start = Points[^1];
            foreach (var end in Points)
            {
                Vector edge = end - start;
                double length = edge.LengthSquared;
                if (length > 0)
                {
                    Vector delta = point - start;
                    double fraction = Math.Clamp(Vector.Multiply(delta, edge) / length, 0, 1);
                    if ((delta - edge * fraction).LengthSquared <= limit) return true;
                }
                start = end;
            }
            return false;
        }
    }
}
