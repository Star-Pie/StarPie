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
    private readonly Honeycomb? _honeycomb;
    private readonly ConcurrentDictionary<(int Level, int Index, int Count), Outline> _shapes = new();

    internal FixedRingGeometry(int rootCount, double rootOuterRadius)
        : this(rootCount, rootOuterRadius, 121 * rootOuterRadius / 72, rootOuterRadius / 72) { }

    internal FixedRingGeometry(int rootCount, double rootOuterRadius, double thirdInnerRadius, double outerScale)
        : this(rootCount, rootOuterRadius, thirdInnerRadius, outerScale, false) { }

    internal FixedRingGeometry(int rootCount, double rootOuterRadius, double thirdInnerRadius, double outerScale, bool honeycomb)
    {
        if (!double.IsFinite(thirdInnerRadius) || thirdInnerRadius <= 0 || !double.IsFinite(outerScale) || outerScale <= 0)
            throw new ArgumentOutOfRangeException();
        if (rootCount is < 1 or > 32 || !double.IsFinite(rootOuterRadius) || rootOuterRadius <= 0)
            throw new ArgumentOutOfRangeException();
        _rootCount = rootCount;
        _rootScale = rootOuterRadius / 72.0;
        _thirdInner = thirdInnerRadius;
        _scale = outerScale;
        if (honeycomb) _honeycomb = new Honeycomb(thirdInnerRadius, outerScale);
    }

    internal static double OuterExtension(int level, int count, bool honeycomb) =>
        honeycomb ? Honeycomb.OuterExtension(level, count) : 39 + 44 * (level - 3);

    internal double Inner(int level) => level >= 3 && _honeycomb != null ? _honeycomb.Inner(level) :
        level >= 3 ? _thirdInner + 44 * (level - 3) * _scale : (33 + 44 * (level - 1)) * _rootScale;
    internal double Outer(int level) => level >= 3 && _honeycomb != null ? _honeycomb.Outer(level, FixedRingMenu.MaximumCount(level)) :
        Inner(level) + 39 * (level >= 3 ? _scale : _rootScale);
    internal double OuterForCount(int level, int count) => level >= 3 && _honeycomb != null ? _honeycomb.Outer(level, count) : Outer(level);
    internal double Scale => _scale;

    internal Point GetCenter(int level, int index, int count)
    {
        if (level == 1) count = _rootCount;
        if ((uint)index >= (uint)count || count < 1 || level is < 1 or > 5) throw new ArgumentOutOfRangeException();
        if (level >= 3 && _honeycomb != null) return _honeycomb.Center(level, index, count);
        double angle = index * 2 * Math.PI / count, radius = (Inner(level) + Outer(level)) / 2;
        return new Point(radius * Math.Cos(angle), radius * Math.Sin(angle));
    }

    internal double CellWidth(int level, int count)
    {
        if (level >= 3 && _honeycomb != null) return _honeycomb.Width;
        return Math.Min(Outer(level) - Inner(level), Math.PI * (Inner(level) + Outer(level)) / count);
    }

    internal Geometry GetShape(int level, int index, int count) => Get(level, index, count).Shape;
    internal int Hit(Point relativeDips, int level, int count)
    {
        if (level == 1) count = _rootCount;
        if (count < 1 || !WithinRadius(relativeDips, Inner(level), OuterForCount(level, count))) return -1;
        if (level >= 3 && _honeycomb != null) return _honeycomb.Hit(relativeDips, level, count);
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
        if (level >= 3 && _honeycomb != null) return _honeycomb.OutlineAt(level, index, count);
        double lo = Inner(level), hi = Outer(level);
        double gap = level == 1 ? 2.3 : 3.8 * _scale / ((hi + lo) * .5) * Degrees;
        double half = (360.0 / count - gap) * .5;
        double radius = 4.5 * _scale;
        double angle = index * 360.0 / count;
        var rounded = IconHelper.CreateRoundedAnnularSectorGeometry(0, 0, angle - half, angle + half, lo, hi, radius);
        var shape = rounded.GetFlattenedPathGeometry(.08 * _scale, ToleranceType.Absolute);
        shape.Freeze();
        var figure = shape.Figures[0];
        var points = new List<Point> { figure.StartPoint };
        foreach (var segment in figure.Segments)
        {
            if (segment is PolyLineSegment poly) points.AddRange(poly.Points);
            else if (segment is LineSegment line) points.Add(line.Point);
        }
        return new Outline(shape, points.ToArray());
    }

    private sealed record Outline(Geometry Shape, Point[] Points)
    {
        // Geometry is prepared by the renderer; hot input reads its identical polygon without WPF allocations.
        internal bool Contains(Point p)
        {
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
