using System.Windows.Media;

namespace WinPieGestures.ArtStyles;

public static class ArtStyleResolver
{
    public static ArtStyleProfile? Resolve(AppConfig? config)
    {
        if (config == null || string.IsNullOrWhiteSpace(config.SelectedArtStyleId)) return null;
        ArtStyleProfile? profile = ArtStyleCatalog.Find(config, config.SelectedArtStyleId);
        return profile != null && Validate(profile).Count == 0 ? profile : ArtStyleCatalog.Find(config, ArtStyleCatalog.DefaultId);
    }

    public static IReadOnlyList<string> Validate(ArtStyleProfile? profile)
    {
        var errors = new List<string>();
        if (profile == null) return ["theme"];
        if (string.IsNullOrWhiteSpace(profile.Id) || profile.Id.Length > 128) errors.Add("id");
        if (string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 80) errors.Add("name");
        if (string.IsNullOrWhiteSpace(profile.FontFamily) || profile.FontFamily.Length > 160 || profile.FontFamily.Contains('#') || profile.FontFamily.Contains('/') || profile.FontFamily.Contains('\\')) errors.Add("fontFamily");
        if (profile.Decoration is not ("Paper" or "Orbit" or "Soft" or "Glass" or "Pixel")) errors.Add("decoration");
        Range(profile.CornerRadius, 0, 24, "cornerRadius");
        Range(profile.StrokeWidth, .5, 3, "strokeWidth");
        Range(profile.ShadowBlur, 0, 30, "shadowBlur");
        Range(profile.ShadowDepth, 0, 6, "shadowDepth");
        Range(profile.ShadowOpacity, 0, .5, "shadowOpacity");
        Range(profile.WheelOpacity, .6, 1, "wheelOpacity");
        Range(profile.DecorationStrength, 0, 1, "decorationStrength");
        if (profile.Colors == null) errors.Add("colors");
        else foreach (var property in typeof(ArtStylePalette).GetProperties())
        {
            string? hex = property.GetValue(profile.Colors) as string;
            // A bounded hex-only grammar prevents arbitrary converter inputs and makes the file portable.
            if (!IsValidColor(hex)) errors.Add("colors." + property.Name);
        }
        return errors;
        void Range(double value, double min, double max, string field)
        { if (!double.IsFinite(value) || value < min || value > max) errors.Add(field); }
    }

    public static bool IsValidColor(string? hex) => hex is { Length: 7 or 9 } && hex[0]=='#' && hex[1..].All(Uri.IsHexDigit);

    public static double Contrast(string foreground, string background)
    {
        Color bg = (Color)ColorConverter.ConvertFromString(background);
        Color fg = (Color)ColorConverter.ConvertFromString(foreground);
        double bgLum = Luminance(bg.R, bg.G, bg.B);
        double alpha = fg.A / 255.0;
        double fgLum = Luminance(fg.R * alpha + bg.R * (1-alpha), fg.G * alpha + bg.G * (1-alpha), fg.B * alpha + bg.B * (1-alpha));
        return (Math.Max(fgLum, bgLum) + .05) / (Math.Min(fgLum, bgLum) + .05);
    }
    private static double Luminance(double r, double g, double b)
    {
        static double Linear(double value) { value /= 255; return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4); }
        return .2126 * Linear(r) + .7152 * Linear(g) + .0722 * Linear(b);
    }
}
