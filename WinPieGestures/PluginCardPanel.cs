using System.Windows;

namespace WinPieGestures;

/// <summary>Use one or two equal card columns. Each row takes the height of its tallest card.</summary>
public sealed class PluginCardPanel : System.Windows.Controls.Panel
{
    private const double ColumnMinimum = 300;
    private const double Gap = 12;

    private static int Columns(double width) => double.IsFinite(width) && width >= ColumnMinimum * 2 + Gap ? 2 : 1;

    protected override Size MeasureOverride(Size availableSize)
    {
        int columns = Columns(availableSize.Width);
        double width = (availableSize.Width - (columns - 1) * Gap) / columns;
        double height = 0, rowHeight = 0, widest = 0;
        int count = 0;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            child.Measure(new Size(width, double.PositiveInfinity));
            widest = Math.Max(widest, child.DesiredSize.Width);
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            if (++count % columns != 0) continue;
            height += rowHeight + Gap;
            rowHeight = 0;
        }
        if (count % columns != 0) height += rowHeight;
        else if (count != 0) height -= Gap;
        return new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : widest, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int columns = Columns(finalSize.Width);
        double width = (finalSize.Width - (columns - 1) * Gap) / columns;
        double top = 0;
        UIElement? first = null;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            if (columns == 2 && first == null)
            {
                first = child;
                continue;
            }
            double height = Math.Max(first?.DesiredSize.Height ?? 0, child.DesiredSize.Height);
            if (first != null) first.Arrange(new Rect(0, top, width, height));
            child.Arrange(new Rect(first == null ? 0 : width + Gap, top, width, height));
            first = null;
            top += height + Gap;
        }
        if (first != null) first.Arrange(new Rect(0, top, width, first.DesiredSize.Height));
        return finalSize;
    }
}
