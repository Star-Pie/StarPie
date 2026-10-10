using System.Text.Json;

namespace WinPieGestures.ArtStyles;

/// <summary>Portable, data-only art direction. Geometry and actions deliberately live outside this model.</summary>
public sealed class ArtStyleProfile
{
    public string Id { get; set; } = "user-" + Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "My theme";
    public bool IsDark { get; set; }
    public ArtStylePalette Colors { get; set; } = new();
    public string FontFamily { get; set; } = "Microsoft YaHei UI, Segoe UI";
    public double CornerRadius { get; set; } = 4;
    public double StrokeWidth { get; set; } = 1;
    public double ShadowBlur { get; set; } = 8;
    public double ShadowDepth { get; set; } = 2;
    public double ShadowOpacity { get; set; } = 0.1;
    public double WheelOpacity { get; set; } = 0.96;
    public string Decoration { get; set; } = "Paper";
    public double DecorationStrength { get; set; } = 0.5;

    public ArtStyleProfile Copy() => JsonSerializer.Deserialize<ArtStyleProfile>(JsonSerializer.Serialize(this))!;
}

public sealed class ArtStylePalette
{
    public string Background { get; set; } = "#F5F1E8";
    public string Surface { get; set; } = "#FFFCF5";
    public string Sidebar { get; set; } = "#EDE7DA";
    public string Text { get; set; } = "#252820";
    public string Muted { get; set; } = "#626458";
    public string Accent { get; set; } = "#B54735";
    public string Border { get; set; } = "#D2CCBE";
    public string WheelBackground { get; set; } = "#FFFCF5";
    public string WheelBorder { get; set; } = "#858373";
    public string WheelHighlight { get; set; } = "#B54735";
    public string WheelText { get; set; } = "#252820";
    public string WheelHighlightText { get; set; } = "#FFFFFF";
}
