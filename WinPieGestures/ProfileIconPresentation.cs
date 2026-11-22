using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace WinPieGestures;

internal static class ProfileIconPresentation
{
    internal static bool TrySetVector(Image image, string? key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        string? path = key.StartsWith("custom:", StringComparison.OrdinalIgnoreCase)
            ? ReadCustomSvg(key)
            : IconHelper.VectorIconList.Find(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))?.SvgData;
        if (string.IsNullOrEmpty(path)) return false;

        Geometry geometry;
        try
        {
            geometry = Geometry.Parse(path);
            if (geometry.Bounds.IsEmpty) return false;
            geometry.Freeze();
        }
        catch (FormatException) { return false; }

        image.SetResourceReference(TextElement.ForegroundProperty, "AccentPrimaryBrush");
        var drawing = new GeometryDrawing { Geometry = geometry };
        BindingOperations.SetBinding(drawing, GeometryDrawing.BrushProperty, new System.Windows.Data.Binding
        {
            Source = image,
            Path = new PropertyPath("(0)", TextElement.ForegroundProperty)
        });
        image.Source = new DrawingImage(drawing);
        return true;
    }

    private static string? ReadCustomSvg(string key)
    {
        if (!Directory.Exists(StudioProfileLibrary.IconDirectory)) return null;
        return IconHelper.GetCustomIcons()
            .Find(icon => icon.IsSvg && string.Equals(icon.Key, key, StringComparison.OrdinalIgnoreCase))?.SvgData;
    }
}
