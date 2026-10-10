namespace WinPieGestures.ArtStyles;

public static class ArtStyleCatalog
{
    public const string DefaultId = "paper";
    private static readonly ArtStyleProfile[] Presets =
    [
        new() { Id = "paper", Name = "Paper", Decoration = "Paper" },
        new() { Id = "obsidian", Name = "Obsidian", IsDark = true, CornerRadius = 8,
            ShadowBlur = 14, ShadowOpacity = .22, Decoration = "Orbit", DecorationStrength = .8,
            Colors = Palette("#11171B", "#1A242A", "#0D1317", "#E8F0EF", "#A5BAB7", "#63D9CA", "#35484D", "#1A242A", "#456267", "#63D9CA", "#E8F0EF", "#102421") },
        new() { Id = "grove", Name = "Grove", CornerRadius = 18, ShadowBlur = 16,
            ShadowOpacity = .08, Decoration = "Soft", DecorationStrength = .35,
            Colors = Palette("#EEF3EA", "#FAFCF6", "#E1EADA", "#263B2D", "#536650", "#54764D", "#C5D4BD", "#FAFCF6", "#9DAF91", "#54764D", "#263B2D", "#FFFFFF") },
        new() { Id = "glacier", Name = "Glacier", CornerRadius = 14, ShadowBlur = 22,
            ShadowOpacity = .15, WheelOpacity = .82, Decoration = "Glass", DecorationStrength = .65,
            Colors = Palette("#EAF4F8", "#F8FDFF", "#DAEAF2", "#163745", "#4B6979", "#176B8A", "#BDD3DE", "#EFFAFF", "#97C8DD", "#176B8A", "#163745", "#FFFFFF") },
        new() { Id = "pixel", Name = "Pixel", CornerRadius = 0, StrokeWidth = 2.5,
            ShadowBlur = 0, ShadowDepth = 3, ShadowOpacity = .3, WheelOpacity = 1,
            FontFamily = "Cascadia Mono, Consolas, Microsoft YaHei UI", Decoration = "Pixel", DecorationStrength = .7,
            Colors = Palette("#F2E9CD", "#FFF9E6", "#E5DABB", "#292C32", "#626057", "#B74327", "#89836F", "#FFF9E6", "#292C32", "#B74327", "#292C32", "#FFFFFF") }
    ];

    private static ArtStylePalette Palette(string background, string surface, string sidebar, string text,
        string muted, string accent, string border, string wheel, string wheelBorder, string highlight, string wheelText, string hoverText)
        => new() { Background = background, Surface = surface, Sidebar = sidebar, Text = text, Muted = muted,
            Accent = accent, Border = border, WheelBackground = wheel, WheelBorder = wheelBorder,
            WheelHighlight = highlight, WheelText = wheelText, WheelHighlightText = hoverText };

    public static bool IsBuiltIn(string? id) => Presets.Any(p => p.Id == id);
    public static IReadOnlyList<ArtStyleProfile> GetAll(AppConfig config) => Presets
        .Concat((config.CustomArtStyles ?? []).Where(p => p != null && !IsBuiltIn(p.Id)))
        .Select(p => p.Copy()).ToArray();
    public static ArtStyleProfile? Find(AppConfig config, string? id) =>
        (Presets.FirstOrDefault(p => p.Id == id) ?? config.CustomArtStyles?.FirstOrDefault(p => p?.Id == id))?.Copy();
}
