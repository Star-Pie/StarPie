using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;

namespace WinPieGestures;

/// <summary>One prepared branch. Drawing and physical input share the same frozen slot shapes.</summary>
internal sealed class DeepMenuFrame
{
    internal const int None = -1, Back = -2, Center = -3;
    internal sealed record Slot(Geometry Shape, Point Label, double LabelWidth);
    internal ReadOnlyCollection<Slot> Slots { get; }
    internal Rect BackBounds { get; }
    internal bool IsPaged { get; }
    internal double OuterDistance { get; }
    internal double CanvasRadius { get; }

    private readonly Point _origin, _offset;
    private readonly DpiScale _dpi;
    private readonly double _start, _step, _coreRadius;

    internal DeepMenuFrame(Point physicalOrigin, DpiScale dpi, Rect physicalWorkArea, double coreRadius,
        double wheelRadius, double secondRadius, double direction, int depth, int count, bool fan, double escapeRadius)
    {
        if (count < 1 || depth is < 3 or > 5) throw new ArgumentOutOfRangeException();
        count = VisibleSlotCount(count, fan);
        _origin = physicalOrigin;
        _dpi = dpi;
        _coreRadius = coreRadius;
        var work = new Rect(ToLocal(physicalWorkArea.TopLeft), ToLocal(physicalWorkArea.BottomRight));
        // Angle zero is the rightmost sector, as on the main wheel.
        double span = BranchSweep(count, fan);
        _start = direction - span / 2;
        _step = span / count;
        double thickness = Math.Max(32, secondRadius - wheelRadius);
        double inner = secondRadius + (depth - 3) * (thickness + 3) + 3;
        (Slot[] slots, Rect bounds) = MakeSlots(inner);
        IsPaged = !work.Contains(bounds) || (escapeRadius > 0 && inner + thickness + 4 >= escapeRadius);
        if (IsPaged)
        {
            inner = Math.Max(coreRadius + 8, wheelRadius + 2);
            (slots, bounds) = MakeSlots(inner);
            _offset = FitPage(bounds, work, coreRadius);
            if (_offset != default)
            {
                var translation = new TranslateTransform(_offset.X, _offset.Y);
                translation.Freeze();
                for (int i = 0; i < slots.Length; i++)
                {
                    var geometry = slots[i].Shape.Clone();
                    geometry.Transform = translation;
                    geometry.Freeze();
                    slots[i] = slots[i] with { Shape = geometry, Label = slots[i].Label + (Vector)_offset };
                }
                bounds.Offset(_offset.X, _offset.Y);
            }
        }
        Slots = Array.AsReadOnly(slots);
        BackBounds = PlaceBack(work, bounds, direction, coreRadius, wheelRadius);
        var content = Rect.Union(bounds, BackBounds);
        CanvasRadius = Math.Ceiling(Math.Max(Math.Max(Math.Abs(content.Left), Math.Abs(content.Right)),
            Math.Max(Math.Abs(content.Top), Math.Abs(content.Bottom))) + 4);
        // Back is part of the wheel for pinned clicks and the outer escape envelope.
        double extent = FarthestCorner(BackBounds);
        foreach (var slot in slots) extent = Math.Max(extent, FarthestCorner(slot.Shape.Bounds));
        OuterDistance = extent;

        (Slot[], Rect) MakeSlots(double r)
        {
            var result = new Slot[count];
            Rect area = Rect.Empty;
            double middle = r + thickness / 2;
            double arc = 2 * middle * Math.Sin(_step * Math.PI / 360);
            for (int i = 0; i < count; i++)
            {
                double a = _start + i * _step;
                double b = a + _step;
                var label = Polar(middle, (a + b) / 2);
                double gap = Math.Min(_step * 0.10, 0.8);
                double radius = Math.Min(thickness / 2 - 1, arc * 0.46);
                Geometry shape = fan ? new EllipseGeometry(label, radius, radius) :
                    Sector(r, r + thickness, a + gap, b - gap);
                shape.Freeze();
                result[i] = new Slot(shape, label, Math.Max(1, fan ? radius * 1.7 : Math.Min(arc * .82, thickness * .9)));
                area.Union(shape.Bounds);
            }
            return (result, area);
        }
    }

    internal static int VisibleSlotCount(int count, bool fan) => fan ? Math.Min(count, 3) : count;
    internal static double BranchSweep(int count, bool fan) => Math.Min(300, Math.Max(54, count * (fan ? 25.0 : 21.0)));

    private static Point FitPage(Rect bounds, Rect work, double core)
    {
        // A corner trigger with edge guarding disabled may have the whole branch outside
        // the monitor. Translate the page as one unit and leave the wheel centre available.
        var best = new Point(Shift(bounds.Left, bounds.Right, work.Left, work.Right),
            Shift(bounds.Top, bounds.Bottom, work.Top, work.Bottom));
        var moved = bounds;
        moved.Offset(best.X, best.Y);
        double gap = core + 3;
        if (!moved.IntersectsWith(new Rect(-gap, -gap, 2 * gap, 2 * gap))) return best;
        double shortest = double.PositiveInfinity;
        foreach (var candidate in new[] {
            new Point(gap - bounds.Left, best.Y), new Point(-gap - bounds.Right, best.Y),
            new Point(best.X, gap - bounds.Top), new Point(best.X, -gap - bounds.Bottom) })
        {
            var test = bounds;
            test.Offset(candidate.X, candidate.Y);
            double distance = ((Vector)candidate).LengthSquared;
            if (work.Contains(test) && distance < shortest) { shortest = distance; best = candidate; }
        }
        return best;
    }

    private static double Shift(double a, double b, double min, double max) =>
        b - a >= max - min ? (min + max - a - b) / 2 : Math.Clamp(0, min - a, max - b);

    private static Rect PlaceBack(Rect work, Rect slots, double direction, double core, double wheel)
    {
        // Prefer the opposite side. At an edge try the other quadrants before clipping the control.
        double radius = Math.Max(wheel * .8, core + 42);
        Rect best = Rect.Empty;
        double bestScore = double.NegativeInfinity;
        foreach (double multiplier in new[] { 1d, 1.5, 2, 2.5, 3, 4, 5, 6, 8 })
        foreach (double delta in new[] { 180d, 90, -90, 135, -135, 45, -45, 0 })
        {
            Point p = Polar(radius * multiplier, direction + delta);
            Rect box = new Rect(p.X - 50, p.Y - 18, 100, 36);
            box.Offset(Shift(box.Left, box.Right, work.Left, work.Right), Shift(box.Top, box.Bottom, work.Top, work.Bottom));
            double dx = Math.Max(0, Math.Max(box.Left, -box.Right));
            double dy = Math.Max(0, Math.Max(box.Top, -box.Bottom));
            if (!box.IntersectsWith(slots) && dx * dx + dy * dy > core * core) return box;
            Rect overlap = Rect.Intersect(box, slots);
            double score = Math.Min(core, Math.Sqrt(dx * dx + dy * dy)) - (overlap.IsEmpty ? 0 : overlap.Width * overlap.Height);
            if (score > bestScore) { bestScore = score; best = box; }
        }
        return best;
    }

    private static double FarthestCorner(Rect box)
    {
        double x = Math.Max(Math.Abs(box.Left), Math.Abs(box.Right));
        double y = Math.Max(Math.Abs(box.Top), Math.Abs(box.Bottom));
        return Math.Sqrt(x * x + y * y);
    }

    private Point ToLocal(Point point) =>
        new((point.X - _origin.X) / _dpi.DpiScaleX, (point.Y - _origin.Y) / _dpi.DpiScaleY);

    internal int HitTest(Point physicalPoint)
    {
        Point p = ToLocal(physicalPoint);
        if (BackBounds.Contains(p)) return Back;
        if (((Vector)p).LengthSquared <= _coreRadius * _coreRadius) return Center;
        double degrees = Math.Atan2(p.Y - _offset.Y, p.X - _offset.X) * 180 / Math.PI;
        double relative = ((degrees - _start) % 360 + 360) % 360;
        int index = (int)(relative / _step);
        return (uint)index < (uint)Slots.Count && Slots[index].Shape.FillContains(p) ? index : None;
    }

    internal bool ContainsWheel(Point physicalPoint) => HitTest(physicalPoint) != None;
    private static Point Polar(double r, double deg) =>
        new(r * Math.Cos(deg * Math.PI / 180), r * Math.Sin(deg * Math.PI / 180));

    private static Geometry Sector(double inside, double outside, double a, double b)
    {
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        ctx.BeginFigure(Polar(inside, a), true, true);
        ctx.LineTo(Polar(outside, a), true, false);
        ctx.ArcTo(Polar(outside, b), new Size(outside, outside), 0, b-a > 180, SweepDirection.Clockwise, true, false);
        ctx.LineTo(Polar(inside, b), true, false);
        ctx.ArcTo(Polar(inside, a), new Size(inside, inside), 0, b-a > 180, SweepDirection.Counterclockwise, true, false);
        return geometry;
    }
}
