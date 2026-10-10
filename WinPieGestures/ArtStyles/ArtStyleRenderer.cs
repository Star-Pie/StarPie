using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace WinPieGestures.ArtStyles;

/// <summary>Material-only renderer: it never changes the wheel's geometry or hit testing.</summary>
public sealed class ArtStyleRenderer : BaseStyleRenderer
{
    private readonly ArtStyleProfile profile;
    private DropShadowEffect? shadow;
    private DropShadowEffect? selectedShadow;
    public Brush HighlightTextBrush { get; private set; } = Brushes.White;
    public string FontFamily => profile.FontFamily;
    private sealed record ContentColors(Brush Normal, bool Overridden);
    private static readonly ConditionalWeakTable<FrameworkElement, ContentColors> Content = new();

    public ArtStyleRenderer(ArtStyleProfile profile) => this.profile = profile.Copy();

    public override void Initialize(string theme, AppConfig config)
    {
        _config = config;
        var c = profile.Colors;
        IsLightTheme = !profile.IsDark;
        DefaultSectorBrush = ArtStyleResources.Brush(c.WheelBackground, profile.WheelOpacity);
        SectorBorderBrush = ArtStyleResources.Brush(c.WheelBorder);
        HighlightSectorBrush = ArtStyleResources.Brush(c.WheelHighlight);
        HighlightBorderBrush = ArtStyleResources.Brush(c.Accent);
        TextColorBrush = ArtStyleResources.Brush(c.WheelText);
        HighlightTextBrush = ArtStyleResources.Brush(c.WheelHighlightText);
        CoreBgBrush = DefaultSectorBrush;
        CoreBorderBrush = SectorBorderBrush;
        BorderThickness = profile.StrokeWidth;
        HighlightBorderThickness = Math.Min(4, profile.StrokeWidth + .7);
        shadow = CreateFrozenDropShadow(Colors.Black, profile.ShadowBlur, profile.ShadowDepth, profile.ShadowOpacity);
        selectedShadow = CreateFrozenDropShadow((Color)ColorConverter.ConvertFromString(c.Accent),
            profile.ShadowBlur, profile.ShadowDepth, profile.ShadowOpacity);
    }

    public override void ApplySectorHighlight(Path path, bool isHighlighted) => path.Effect = isHighlighted ? selectedShadow : shadow;
    public override void ApplyExitHighlight(Path exitIcon, bool isHighlighted) => exitIcon.Effect = isHighlighted ? selectedShadow : null;

    public static void RememberContent(FrameworkElement? content, Brush normal, bool overridden)
    {
        if (content == null) return;
        Content.Remove(content);
        Content.Add(content, new ContentColors(normal, overridden));
    }
    public static Brush ContentBrush(IRadialStyleRenderer? renderer, FrameworkElement? content, bool highlighted, Brush fallback)
    {
        if (renderer is not ArtStyleRenderer art) return fallback;
        if (content != null && Content.TryGetValue(content, out var colors))
        {
            if (colors.Overridden || !highlighted) return colors.Normal;
        }
        return highlighted ? art.HighlightTextBrush : art.TextColorBrush;
    }
    public static string ResolveFont(IRadialStyleRenderer? renderer, string fallback) => renderer is ArtStyleRenderer art ? art.FontFamily : fallback;

    public override void RenderDecorations(Canvas canvas, Grid coreGrid, double cx, double cy, double wheelRadius, double coreRadius, int insertIndex)
    {
        double strength = profile.DecorationStrength;
        if (strength <= 0) return;
        Brush ink = ArtStyleResources.Brush(profile.Colors.Accent, strength * .5);
        double r = wheelRadius + 9;
        if (profile.Decoration is "Orbit" or "Glass" or "Soft")
        {
            var ring = new Ellipse { Width = r*2, Height = r*2, Stroke = ink, StrokeThickness = profile.Decoration == "Soft" ? 2 : .8 };
            if (profile.Decoration == "Orbit") { var dashes = new DoubleCollection { 2, 6 }; dashes.Freeze(); ring.StrokeDashArray = dashes; }
            Add(ring,cx-r,cy-r);
        }
        if (profile.Decoration == "Pixel")
        {
            for (int i = 0; i < 4; i++)
            {
                double a = i * Math.PI/2;
                double x = cx + Math.Cos(a)*r, y = cy + Math.Sin(a)*r;
                Add(new Rectangle { Width = 5, Height = 5, Fill = ink },x-2.5,y-2.5);
                var step = new Rectangle { Width = 3, Height = 3, Fill = ink };
                Add(step,x-1.5+Math.Cos(a)*7,y-1.5+Math.Sin(a)*7);
            }
        }
        else for (int i = 0; i < (profile.Decoration == "Orbit" ? 8 : 4); i++)
        {
            double a = i * Math.PI/(profile.Decoration == "Orbit" ? 4 : 2);
            if (profile.Decoration == "Soft") Add(new Ellipse { Width = 4, Height = 4, Fill = ink },cx+Math.Cos(a)*r-2,cy+Math.Sin(a)*r-2);
            else Add(new Line { X1 = cx+Math.Cos(a)*(r-3), Y1 = cy+Math.Sin(a)*(r-3), X2 = cx+Math.Cos(a)*(r+3), Y2 = cy+Math.Sin(a)*(r+3), Stroke = ink, StrokeThickness = profile.StrokeWidth },0,0);
        }
        void Add(Shape shape, double x, double y)
        {
            shape.IsHitTestVisible = false;
            shape.Tag = "Deco_ArtStyle";
            Canvas.SetLeft(shape,x); Canvas.SetTop(shape,y); Panel.SetZIndex(shape,0);
            canvas.Children.Add(shape);
        }
    }
}
