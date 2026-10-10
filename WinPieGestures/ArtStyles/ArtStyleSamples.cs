using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace WinPieGestures.ArtStyles;

public static class ArtStyleSamples
{
    public static FrameworkElement Wheel(ArtStyleProfile profile, double size)
    {
        var canvas = new Canvas { Width = size, Height = size, IsHitTestVisible = false };
        double mid = size/2, outer = size*.37, inner = size*.20, core = size*.14;
        var renderer = new ArtStyleRenderer(profile);
        renderer.Initialize("",new AppConfig());
        renderer.RenderDecorations(canvas,new Grid(),mid,mid,outer,core,0);
        for (int i = 0; i < 8; i++)
        {
            var shape = new Path { Data = IconHelper.CreateAdvancedSectorGeometry(mid,mid,i*45-20,i*45+20,inner,outer,"Original",1,Math.Min(4,profile.CornerRadius*size/320)),
                Fill = i == 0 ? renderer.HighlightSectorBrush : renderer.DefaultSectorBrush,
                Stroke = renderer.SectorBorderBrush, StrokeThickness = Math.Max(.6,profile.StrokeWidth*size/200) };
            if (shape.Data.CanFreeze) shape.Data.Freeze();
            renderer.ApplySectorHighlight(shape,i==0);
            canvas.Children.Add(shape);
            if (size >= 120)
            {
                var label = new TextBlock { Text = new[] { "↗", "+", "⌘", "▣", "↶", "◇", "↓", "≡" }[i],
                    FontSize = size*.066, Width = 22, TextAlignment = TextAlignment.Center,
                    Foreground = i==0 ? renderer.HighlightTextBrush : renderer.TextColorBrush };
                double angle = i*Math.PI/4, r = (inner+outer)/2;
                Canvas.SetLeft(label,mid+Math.Cos(angle)*r-11); Canvas.SetTop(label,mid+Math.Sin(angle)*r-size*.045);
                canvas.Children.Add(label);
            }
        }
        var center = new Ellipse { Width = core*2, Height = core*2, Fill = renderer.CoreBgBrush,
            Stroke = renderer.CoreBorderBrush, StrokeThickness = profile.StrokeWidth*.6 };
        Canvas.SetLeft(center,mid-core); Canvas.SetTop(center,mid-core); canvas.Children.Add(center);
        return canvas;
    }
}
