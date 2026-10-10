using System.Windows;
using System.Windows.Media;
using Pen = System.Windows.Media.Pen;

namespace WinPieGestures;

internal static class ThemeBrandMark
{
    private static readonly Dictionary<string, DrawingImage> Cache = new();

    public static DrawingImage Get(FrameworkElement root)
    {
        var ink = (SolidColorBrush)root.FindResource("TextPrimaryBrush");
        var line = (SolidColorBrush)root.FindResource("TextSecondaryBrush");
        var surface = (SolidColorBrush)root.FindResource("CardBackgroundBrush");
        var accent = (SolidColorBrush)root.FindResource("AccentPrimaryBrush");
        string key = $"{ink.Color}/{line.Color}/{surface.Color}/{accent.Color}";
        if (Cache.TryGetValue(key, out var cached)) return cached;

        var drawing = new DrawingGroup();
        var pen = new Pen(line, 1.4);
        pen.Freeze();
        for (int i = 0; i < 8; i++)
        {
            var sector = IconHelper.CreateAdvancedSectorGeometry(32, 32, i * 45 - 20, i * 45 + 20, 18, 29, "Original", 1, 1.4);
            sector.Freeze();
            drawing.Children.Add(new GeometryDrawing(i == 7 ? accent : surface, pen, sector));
        }
        drawing.Children.Add(new GeometryDrawing(surface, pen, new EllipseGeometry(new Point(32, 32), 13, 13)));
        var pointer = Geometry.Parse("M 27,24 L 39,32 L 33,34 L 31,41 Z");
        pointer.Freeze();
        drawing.Children.Add(new GeometryDrawing(ink, null, pointer));
        // A transparent bound keeps the mark aligned at both caption and sidebar sizes.
        drawing.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 64, 64))));
        drawing.Freeze();
        var image = new DrawingImage(drawing);
        image.Freeze();
        if (Cache.Count >= 16) Cache.Clear();
        Cache.Add(key, image);
        return image;
    }
}
