using System.Windows;
using System.Windows.Media;

namespace WinPieGestures;

// Legacy profiles may have more than four Wheel children. Fan keeps the original three physical positions.
internal static class LegacyRingGeometry
{
    internal static string SecondaryShape(AppConfig config) => string.IsNullOrEmpty(config.SubWheelShape) || config.SubWheelShape == "FollowPrimary"
        ? config.Shape ?? "Original" : config.SubWheelShape;

    internal static (Geometry Shape, Point Label) Primary(AppConfig cfg, int count, int index, double outer, double inner)
    {
        double angle = 360.0 * index / count;
        return Sector(cfg.Shape ?? "Original", angle, 360.0 / count, inner, outer, cfg.SectorGap, cfg.SectorCornerRadius);
    }

    internal static (Geometry Shape, Point Label) Secondary(AppConfig cfg, int count, int primary, int childIndex,
        int childCount, double outer, double inner)
    {
        if (childCount <= 0 || (uint)childIndex >= (uint)childCount) throw new ArgumentOutOfRangeException();
        double sweep = 360.0 / count, parentAngle = primary * sweep;
        double second = cfg.SubWheelOuterRadius > 0 ? cfg.SubWheelOuterRadius : outer * 1.55;
        string shape = SecondaryShape(cfg);
        if (cfg.SubmenuStyle != "Fan")
        {
            double gap = cfg.SubWheelInnerGap >= 0 ? cfg.SubWheelInnerGap : Math.Max(0, cfg.SectorGap);
            return Sector(shape, parentAngle + ((childIndex + .5) / childCount - .5) * sweep, sweep / childCount,
                outer + gap + 2, second, gap, cfg.SubWheelCornerRadius >= 0 ? cfg.SubWheelCornerRadius : cfg.SectorCornerRadius);
        }
        int visible = Math.Min(childCount, RadialWindow.FanSubmenuSlotCount);
        if (childIndex >= visible) throw new ArgumentOutOfRangeException(nameof(childIndex));
        double ratio = second / (outer * 1.55);
        double itemRadius = (outer - inner) * .4 * Math.Clamp(ratio, .5, 2.5);
        double orbit = (inner + outer) / 2 * ratio + (cfg.SubWheelInnerGap >= 0 ? cfg.SubWheelInnerGap : 4);
        var (u,v) = RadialWindow.GetFanSubOffsetForShape(shape, RadialWindow.GetFanSlotIndex(childIndex,visible));
        double rad = parentAngle * Math.PI / 180;
        var label = new Point((Math.Cos(rad)*u - Math.Sin(rad)*v)*orbit, (Math.Sin(rad)*u + Math.Cos(rad)*v)*orbit);
        var geo = RadialWindow.CreateSubMenuGeometry(shape,label.X,label.Y,itemRadius,rad,0,0,cfg.SubWheelCornerRadius);
        if (!geo.IsFrozen) geo.Freeze();
        return (geo,label);
    }

    private static (Geometry Shape, Point Label) Sector(string outline, double angle, double sweep, double inner,
        double outer, double gap, double corner)
    {
        var shape = IconHelper.CreateAdvancedSectorGeometry(0,0,angle - sweep / 2, angle + sweep / 2,inner,outer,
            outline,gap,corner);
        if (!shape.IsFrozen) shape.Freeze();
        double rad = angle * Math.PI / 180, mid = (inner + outer) / 2;
        return (shape,new Point(mid * Math.Cos(rad), mid * Math.Sin(rad)));
    }

    internal static double SecondaryExtent(AppConfig cfg, double outer, double inner)
    {
        bool fan = cfg.SubmenuStyle == "Fan";
        double radius = fan ? outer : Math.Max(outer, cfg.SubWheelOuterRadius > 0 ? cfg.SubWheelOuterRadius : outer * 1.55);
        if (!fan && SecondaryShape(cfg) == "Original") return radius;
        // Minimum sizes for hexagons and capsules can extend past the nominal wheel radius.
        // A single child on a four-direction wheel has the widest supported secondary shape.
        int count = fan ? RadialWindow.FanSubmenuSlotCount : 1;
        for (int i = 0; i < count; i++)
        {
            var shape = Secondary(cfg,4,0,i,count,outer,inner).Shape;
            var polygon = shape.GetFlattenedPathGeometry(.1,ToleranceType.Absolute);
            foreach (var figure in polygon.Figures)
            {
                Include(figure.StartPoint);
                foreach (var segment in figure.Segments)
                {
                    if (segment is PolyLineSegment poly) foreach (var point in poly.Points) Include(point);
                    else if (segment is LineSegment line) Include(line.Point);
                }
            }
            void Include(Point p) => radius = Math.Max(radius, ((Vector)(polygon.Transform?.Transform(p) ?? p)).Length);
        }
        return radius + .2;
    }
}
