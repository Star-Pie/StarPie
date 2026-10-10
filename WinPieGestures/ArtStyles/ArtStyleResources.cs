using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace WinPieGestures.ArtStyles;

public static class ArtStyleResources
{
    private static readonly DropShadowEffect LegacySoftChipShadow = CreateShadow(8, 1, .16);
    private static readonly DropShadowEffect LegacyChipShadow = CreateShadow(8, 1, .12);

    public static void Apply(FrameworkElement root, ArtStyleProfile profile, bool publish = true)
    {
        var c = profile.Colors;
        string hover = Blend(c.Surface, c.Accent, .08);
        string active = Blend(c.Surface, c.Accent, .14);
        string accentText = ArtStyleResolver.Contrast("#FFFFFF", c.Accent) >= 4.5 ? "#FFFFFF" : "#11171B";
        var colors = new Dictionary<string, string>
        {
            ["WindowBackgroundBrush"] = c.Background, ["SidebarBackgroundBrush"] = c.Sidebar,
            ["CardBackgroundBrush"] = c.Surface, ["CardBorderBrush"] = c.Border,
            ["TextPrimaryBrush"] = c.Text, ["TextSecondaryBrush"] = c.Muted,
            ["TextMutedBrush"] = c.Muted, ["TextTertiaryBrush"] = c.Muted,
            ["InputBackgroundBrush"] = c.Surface, ["InputBorderBrush"] = c.Border,
            ["ItemHoverBrush"] = hover, ["SubtleCardBrush"] = c.Background,
            ["NavTabDefaultBgBrush"] = "#00000000", ["NavTabDefaultFgBrush"] = c.Muted,
            ["NavTabHoverBgBrush"] = hover, ["NavTabHoverFgBrush"] = c.Text,
            ["NavTabActiveBgBrush"] = active, ["NavTabActiveFgBrush"] = c.Text,
            ["ButtonDefaultBgBrush"] = c.Surface, ["ButtonDefaultFgBrush"] = c.Text,
            ["ButtonDefaultBorderBrush"] = c.Border, ["ButtonHoverBgBrush"] = hover,
            ["AccentPrimaryBrush"] = c.Accent, ["AccentHoverBrush"] = Blend(c.Accent, c.Text, .12),
            ["AccentTextBrush"] = accentText,
            ["PreviewCanvasBackgroundBrush"] = c.Background, ["PreviewCanvasBorderBrush"] = c.Border,
            ["PreviewGridLineBrush"] = Blend(c.Background, c.Border, .42)
        };
        foreach (var pair in colors) Set(root, pair.Key, Brush(pair.Value), publish);
        SetMetrics(root, profile.CornerRadius, profile.StrokeWidth, profile.FontFamily,
            profile.ShadowBlur, profile.ShadowDepth, profile.ShadowOpacity, publish);
    }

    public static void ApplyPreview(FrameworkElement root, ArtStyleProfile profile) => Apply(root, profile, false);

    public static void ResetMetrics(FrameworkElement root)
    {
        SetMetrics(root, 12, 1,"Segoe UI, Microsoft YaHei UI", 12, 1, .03);
        Set(root,"ControlCornerRadius",new CornerRadius(6));
        Set(root, "ChipShadowSoftEffect", LegacySoftChipShadow);
        Set(root, "ChipShadowEffect", LegacyChipShadow);
    }

    private static void SetMetrics(FrameworkElement root, double radius, double stroke, string font,
        double blur, double depth, double opacity, bool publish = true)
    {
        Set(root, "CardCornerRadius", new CornerRadius(radius), publish);
        Set(root, "ControlCornerRadius", new CornerRadius(Math.Min(12, radius * .6)), publish);
        Set(root, "ArtBorderThickness", new Thickness(stroke), publish);
        Set(root, "ArtFontFamily", new FontFamily(font), publish);
        var effect = CreateShadow(blur, depth, opacity);
        Set(root, "CardShadowEffect", effect, publish);
        Set(root, "ChipShadowSoftEffect", effect, publish);
        Set(root, "ChipShadowEffect", effect, publish);
        if (root is Control control) control.SetResourceReference(Control.FontFamilyProperty, "ArtFontFamily");
    }

    private static DropShadowEffect CreateShadow(double blur, double depth, double opacity)
    {
        var effect = new DropShadowEffect { BlurRadius = blur, ShadowDepth = depth, Opacity = opacity, Color = Colors.Black };
        effect.Freeze();
        return effect;
    }

    private static void Set(FrameworkElement root, string key, object value, bool publish = true)
    {
        root.Resources[key] = value;
        if (publish && Application.Current != null) Application.Current.Resources[key] = value;
    }

    public static SolidColorBrush Brush(string hex, double opacity = 1)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)) { Opacity = opacity };
        brush.Freeze();
        return brush;
    }

    private static string Blend(string first, string second, double amount)
    {
        Color a = (Color)ColorConverter.ConvertFromString(first), b = (Color)ColorConverter.ConvertFromString(second);
        return Color.FromRgb((byte)(a.R * (1-amount) + b.R * amount), (byte)(a.G * (1-amount) + b.G * amount), (byte)(a.B * (1-amount) + b.B * amount)).ToString();
    }
}
