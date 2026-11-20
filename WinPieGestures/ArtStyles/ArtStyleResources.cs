using System.Collections;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace WinPieGestures.ArtStyles;

internal sealed class ThemeResourceDictionary : ResourceDictionary
{
    public ThemeResourceDictionary(string cacheKey)
    {
        CacheKey = cacheKey;
    }

    public string CacheKey { get; }
}

public static class ArtStyleResources
{
    private const int MaxBrushCacheEntries = 256;
    private const int MaxFontCacheEntries = 64;
    private const int MaxShadowCacheEntries = 32;
    private const int MaxStyleDictionaryEntries = 32;

    private static readonly DropShadowEffect StandardCardShadow = CreateShadow(12, 1, .03);
    private static readonly DropShadowEffect LegacySoftChipShadow = CreateShadow(8, 1, .16);
    private static readonly DropShadowEffect LegacyChipShadow = CreateShadow(8, 1, .12);
    private static readonly object CacheGate = new();
    private static readonly ConcurrentDictionary<string, SolidColorBrush> BrushCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, FontFamily> FontFamilyCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, DropShadowEffect> ShadowCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, ThemeResourceDictionary> StyleDictionaryCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, ThemeResourceDictionary> StandardDictionaryCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConditionalWeakTable<ResourceDictionary, CleanTrackState> CleanStates = new();
    private static ThemeResourceDictionary? _legacyMetricsOnlyDictionary;

    private sealed class CleanTrackState
    {
        public int CleanedCount = -1;
    }

    internal static readonly string[] BrushKeys =
    [
        "WindowBackgroundBrush",
        "SidebarBackgroundBrush",
        "CardBackgroundBrush",
        "CardBorderBrush",
        "TextPrimaryBrush",
        "TextSecondaryBrush",
        "TextMutedBrush",
        "TextTertiaryBrush",
        "InputBackgroundBrush",
        "InputBorderBrush",
        "ItemHoverBrush",
        "SubtleCardBrush",
        "NavTabDefaultBgBrush",
        "NavTabDefaultFgBrush",
        "NavTabHoverBgBrush",
        "NavTabHoverFgBrush",
        "NavTabActiveBgBrush",
        "NavTabActiveFgBrush",
        "ButtonDefaultBgBrush",
        "ButtonDefaultFgBrush",
        "ButtonDefaultBorderBrush",
        "ButtonHoverBgBrush",
        "AccentPrimaryBrush",
        "AccentHoverBrush",
        "AccentTextBrush",
        "PreviewCanvasBackgroundBrush",
        "PreviewCanvasBorderBrush",
        "PreviewGridLineBrush"
    ];

    internal static readonly string[] MetricKeys =
    [
        "CardCornerRadius",
        "ControlCornerRadius",
        "ArtBorderThickness",
        "ArtFontFamily",
        "CardShadowEffect",
        "ChipShadowSoftEffect",
        "ChipShadowEffect"
    ];

    private static readonly HashSet<string> AllManagedKeys = new(BrushKeys.Concat(MetricKeys), StringComparer.Ordinal);
    private static readonly HashSet<string> MetricKeySet = new(MetricKeys, StringComparer.Ordinal);

    public static void Apply(FrameworkElement root, ArtStyleProfile profile, bool publish = true)
    {
        ApplyCore(root, profile, publish, applyControlFontFamily: true);
    }

    internal static void ApplyForThemeManager(FrameworkElement root, ArtStyleProfile profile)
    {
        ApplyCore(root, profile, publish: true, applyControlFontFamily: false);
    }

    internal static void ApplyStandardThemeForThemeManager(
        FrameworkElement root,
        string canonicalTheme,
        string windowBg,
        string sidebarBg,
        string cardBg,
        string cardBorder,
        string textPrimary,
        string textSecondary,
        string textMuted,
        string inputBg,
        string inputBorder,
        string itemHover,
        string subtleCard,
        string navTabDefaultBg,
        string navTabDefaultFg,
        string navTabHoverBg,
        string navTabHoverFg,
        string navTabActiveBg,
        string navTabActiveFg,
        string buttonDefaultBg,
        string buttonDefaultFg,
        string buttonDefaultBorder,
        string buttonHoverBg,
        string accentPrimary,
        string accentHover,
        string accentText,
        string previewCanvasBg,
        string previewCanvasBorder,
        string previewGridLine)
    {
        ThemeResourceDictionary dict = GetOrCreateStandardDictionary(
            canonicalTheme,
            windowBg,
            sidebarBg,
            cardBg,
            cardBorder,
            textPrimary,
            textSecondary,
            textMuted,
            inputBg,
            inputBorder,
            itemHover,
            subtleCard,
            navTabDefaultBg,
            navTabDefaultFg,
            navTabHoverBg,
            navTabHoverFg,
            navTabActiveBg,
            navTabActiveFg,
            buttonDefaultBg,
            buttonDefaultFg,
            buttonDefaultBorder,
            buttonHoverBg,
            accentPrimary,
            accentHover,
            accentText,
            previewCanvasBg,
            previewCanvasBorder,
            previewGridLine);
        ApplyThemeDictionary(root, dict, publish: true);
    }

    public static void ApplyPreview(FrameworkElement root, ArtStyleProfile profile) => Apply(root, profile, false);

    public static void ResetMetrics(FrameworkElement root)
    {
        if (root == null)
        {
            return;
        }

        ApplyLegacyMetricsToTarget(root.Resources);
        if (Application.Current != null && !ReferenceEquals(root.Resources, Application.Current.Resources))
        {
            ApplyLegacyMetricsToTarget(Application.Current.Resources);
        }

        if (root is Control control)
        {
            control.SetResourceReference(Control.FontFamilyProperty, "ArtFontFamily");
            UiTypography.Invalidate(root);
        }
    }

    internal static FontFamily GetCachedFontFamily(string font)
    {
        string normalized = string.IsNullOrWhiteSpace(font) ? "Segoe UI, Microsoft YaHei UI" : font;
        if (FontFamilyCache.TryGetValue(normalized, out FontFamily? cached))
        {
            return cached;
        }

        var family = new FontFamily(normalized);
        if (FontFamilyCache.Count >= MaxFontCacheEntries)
        {
            FontFamilyCache.Clear();
        }
        return FontFamilyCache.GetOrAdd(normalized, family);
    }

    private static void ApplyCore(FrameworkElement root, ArtStyleProfile profile, bool publish, bool applyControlFontFamily)
    {
        if (root == null || profile == null)
        {
            return;
        }

        ThemeResourceDictionary dict = GetOrCreateStyleDictionary(profile);
        ApplyThemeDictionary(root, dict, publish);
        if (applyControlFontFamily && root is Control control)
        {
            control.SetResourceReference(Control.FontFamilyProperty, "ArtFontFamily");
            UiTypography.Invalidate(root);
        }
    }

    private static ThemeResourceDictionary GetOrCreateStyleDictionary(ArtStyleProfile profile)
    {
        var c = profile.Colors;
        string cacheKey = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"art:{c.Background}|{c.Sidebar}|{c.Surface}|{c.Border}|{c.Text}|{c.Muted}|{c.Accent}|{profile.CornerRadius}|{profile.StrokeWidth}|{profile.FontFamily}|{profile.ShadowBlur}|{profile.ShadowDepth}|{profile.ShadowOpacity}");

        lock (CacheGate)
        {
            if (StyleDictionaryCache.TryGetValue(cacheKey, out ThemeResourceDictionary? cached))
            {
                return cached;
            }
        }

        string hover = Blend(c.Surface, c.Accent, .08);
        string active = Blend(c.Surface, c.Accent, .14);
        string accentText = ArtStyleResolver.Contrast("#FFFFFF", c.Accent) >= 4.5 ? "#FFFFFF" : "#11171B";
        string accentHover = Blend(c.Accent, c.Text, .12);
        string previewGrid = Blend(c.Background, c.Border, .42);

        var dict = new ThemeResourceDictionary(cacheKey);
        PopulateBrushSet(
            dict,
            c.Background,
            c.Sidebar,
            c.Surface,
            c.Border,
            c.Text,
            c.Muted,
            c.Muted,
            c.Surface,
            c.Border,
            hover,
            c.Background,
            "#00000000",
            c.Muted,
            hover,
            c.Text,
            active,
            c.Text,
            c.Surface,
            c.Text,
            c.Border,
            hover,
            c.Accent,
            accentHover,
            accentText,
            c.Background,
            c.Border,
            previewGrid);

        dict["CardCornerRadius"] = new CornerRadius(profile.CornerRadius);
        dict["ControlCornerRadius"] = new CornerRadius(Math.Min(12, profile.CornerRadius * .6));
        dict["ArtBorderThickness"] = new Thickness(profile.StrokeWidth);
        dict["ArtFontFamily"] = GetCachedFontFamily(profile.FontFamily);
        DropShadowEffect effect = GetOrCreateShadow(profile.ShadowBlur, profile.ShadowDepth, profile.ShadowOpacity);
        dict["CardShadowEffect"] = effect;
        dict["ChipShadowSoftEffect"] = effect;
        dict["ChipShadowEffect"] = effect;

        lock (CacheGate)
        {
            if (StyleDictionaryCache.TryGetValue(cacheKey, out ThemeResourceDictionary? raced))
            {
                return raced;
            }

            if (StyleDictionaryCache.Count >= MaxStyleDictionaryEntries)
            {
                StyleDictionaryCache.Clear();
            }

            StyleDictionaryCache[cacheKey] = dict;
        }

        return dict;
    }

    private static ThemeResourceDictionary GetOrCreateStandardDictionary(
        string canonicalTheme,
        string windowBg,
        string sidebarBg,
        string cardBg,
        string cardBorder,
        string textPrimary,
        string textSecondary,
        string textMuted,
        string inputBg,
        string inputBorder,
        string itemHover,
        string subtleCard,
        string navTabDefaultBg,
        string navTabDefaultFg,
        string navTabHoverBg,
        string navTabHoverFg,
        string navTabActiveBg,
        string navTabActiveFg,
        string buttonDefaultBg,
        string buttonDefaultFg,
        string buttonDefaultBorder,
        string buttonHoverBg,
        string accentPrimary,
        string accentHover,
        string accentText,
        string previewCanvasBg,
        string previewCanvasBorder,
        string previewGridLine)
    {
        lock (CacheGate)
        {
            if (StandardDictionaryCache.TryGetValue(canonicalTheme, out ThemeResourceDictionary? cached))
            {
                return cached;
            }
        }

        var dict = new ThemeResourceDictionary("std:" + canonicalTheme);
        PopulateBrushSet(
            dict,
            windowBg,
            sidebarBg,
            cardBg,
            cardBorder,
            textPrimary,
            textSecondary,
            textMuted,
            inputBg,
            inputBorder,
            itemHover,
            subtleCard,
            navTabDefaultBg,
            navTabDefaultFg,
            navTabHoverBg,
            navTabHoverFg,
            navTabActiveBg,
            navTabActiveFg,
            buttonDefaultBg,
            buttonDefaultFg,
            buttonDefaultBorder,
            buttonHoverBg,
            accentPrimary,
            accentHover,
            accentText,
            previewCanvasBg,
            previewCanvasBorder,
            previewGridLine);
        PopulateLegacyMetrics(dict);

        lock (CacheGate)
        {
            if (StandardDictionaryCache.TryGetValue(canonicalTheme, out ThemeResourceDictionary? raced))
            {
                return raced;
            }

            StandardDictionaryCache[canonicalTheme] = dict;
        }

        return dict;
    }

    private static ThemeResourceDictionary GetOrCreateLegacyMetricsOnlyDictionary()
    {
        lock (CacheGate)
        {
            if (_legacyMetricsOnlyDictionary != null)
            {
                return _legacyMetricsOnlyDictionary;
            }

            var dict = new ThemeResourceDictionary("legacy-metrics-only");
            PopulateLegacyMetrics(dict);
            _legacyMetricsOnlyDictionary = dict;
            return dict;
        }
    }

    private static void PopulateLegacyMetrics(ResourceDictionary dict)
    {
        dict["CardCornerRadius"] = new CornerRadius(12);
        dict["ControlCornerRadius"] = new CornerRadius(6);
        dict["ArtBorderThickness"] = new Thickness(1);
        dict["ArtFontFamily"] = GetCachedFontFamily("Segoe UI, Microsoft YaHei UI");
        dict["CardShadowEffect"] = StandardCardShadow;
        dict["ChipShadowSoftEffect"] = LegacySoftChipShadow;
        dict["ChipShadowEffect"] = LegacyChipShadow;
    }

    private static void PopulateBrushSet(
        ResourceDictionary dict,
        string windowBg,
        string sidebarBg,
        string cardBg,
        string cardBorder,
        string textPrimary,
        string textSecondary,
        string textMuted,
        string inputBg,
        string inputBorder,
        string itemHover,
        string subtleCard,
        string navTabDefaultBg,
        string navTabDefaultFg,
        string navTabHoverBg,
        string navTabHoverFg,
        string navTabActiveBg,
        string navTabActiveFg,
        string buttonDefaultBg,
        string buttonDefaultFg,
        string buttonDefaultBorder,
        string buttonHoverBg,
        string accentPrimary,
        string accentHover,
        string accentText,
        string previewCanvasBg,
        string previewCanvasBorder,
        string previewGridLine)
    {
        dict["WindowBackgroundBrush"] = Brush(windowBg);
        dict["SidebarBackgroundBrush"] = Brush(sidebarBg);
        dict["CardBackgroundBrush"] = Brush(cardBg);
        dict["CardBorderBrush"] = Brush(cardBorder);
        dict["TextPrimaryBrush"] = Brush(textPrimary);
        dict["TextSecondaryBrush"] = Brush(textSecondary);
        dict["TextMutedBrush"] = Brush(textMuted);
        dict["TextTertiaryBrush"] = Brush(textMuted);
        dict["InputBackgroundBrush"] = Brush(inputBg);
        dict["InputBorderBrush"] = Brush(inputBorder);
        dict["ItemHoverBrush"] = Brush(itemHover);
        dict["SubtleCardBrush"] = Brush(subtleCard);
        dict["NavTabDefaultBgBrush"] = Brush(navTabDefaultBg);
        dict["NavTabDefaultFgBrush"] = Brush(navTabDefaultFg);
        dict["NavTabHoverBgBrush"] = Brush(navTabHoverBg);
        dict["NavTabHoverFgBrush"] = Brush(navTabHoverFg);
        dict["NavTabActiveBgBrush"] = Brush(navTabActiveBg);
        dict["NavTabActiveFgBrush"] = Brush(navTabActiveFg);
        dict["ButtonDefaultBgBrush"] = Brush(buttonDefaultBg);
        dict["ButtonDefaultFgBrush"] = Brush(buttonDefaultFg);
        dict["ButtonDefaultBorderBrush"] = Brush(buttonDefaultBorder);
        dict["ButtonHoverBgBrush"] = Brush(buttonHoverBg);
        dict["AccentPrimaryBrush"] = Brush(accentPrimary);
        dict["AccentHoverBrush"] = Brush(accentHover);
        dict["AccentTextBrush"] = Brush(accentText);
        dict["PreviewCanvasBackgroundBrush"] = Brush(previewCanvasBg);
        dict["PreviewCanvasBorderBrush"] = Brush(previewCanvasBorder);
        dict["PreviewGridLineBrush"] = Brush(previewGridLine);
    }

    private static void ApplyThemeDictionary(FrameworkElement root, ThemeResourceDictionary themeDict, bool publish)
    {
        SwapMergedThemeDictionary(root.Resources, themeDict, AllManagedKeys);
        if (publish && Application.Current != null && !ReferenceEquals(root.Resources, Application.Current.Resources))
        {
            SwapMergedThemeDictionary(Application.Current.Resources, themeDict, AllManagedKeys);
        }
    }

    private static void ApplyLegacyMetricsToTarget(ResourceDictionary target)
    {
        int idx = FindThemeDictionaryIndex(target.MergedDictionaries);
        ThemeResourceDictionary metricsDict;
        if (idx >= 0 && target.MergedDictionaries[idx] is ThemeResourceDictionary existing)
        {
            bool hasBrushes = existing.Contains("WindowBackgroundBrush");
            if (!hasBrushes)
            {
                metricsDict = GetOrCreateLegacyMetricsOnlyDictionary();
            }
            else if (existing.CacheKey.StartsWith("std:", StringComparison.Ordinal) ||
                     existing.CacheKey.EndsWith("|legacy-metrics", StringComparison.Ordinal))
            {
                metricsDict = existing;
            }
            else
            {
                var combined = new ThemeResourceDictionary(existing.CacheKey + "|legacy-metrics");
                foreach (DictionaryEntry entry in existing)
                {
                    if (entry.Key is string key && !MetricKeySet.Contains(key))
                    {
                        combined[key] = entry.Value;
                    }
                }
                PopulateLegacyMetrics(combined);
                metricsDict = combined;
            }
        }
        else
        {
            metricsDict = GetOrCreateLegacyMetricsOnlyDictionary();
        }

        SwapMergedThemeDictionary(target, metricsDict, MetricKeySet);
    }

    private static void SwapMergedThemeDictionary(
        ResourceDictionary target,
        ThemeResourceDictionary nextDict,
        HashSet<string> keysToStripLocally)
    {
        var merged = target.MergedDictionaries;
        int idx = FindThemeDictionaryIndex(merged);
        if (idx >= 0)
        {
            if (!ReferenceEquals(merged[idx], nextDict))
            {
                merged[idx] = nextDict;
            }
        }
        else
        {
            merged.Add(nextDict);
        }

        StripLocalManagedKeys(target, keysToStripLocally);
    }

    private static int FindThemeDictionaryIndex(IList<ResourceDictionary> merged)
    {
        for (int i = merged.Count - 1; i >= 0; i--)
        {
            if (merged[i] is ThemeResourceDictionary)
            {
                return i;
            }
        }
        return -1;
    }

    private static void StripLocalManagedKeys(ResourceDictionary target, HashSet<string> keysToStrip)
    {
        if (target.Count == 0)
        {
            return;
        }

        var state = CleanStates.GetOrCreateValue(target);
        if (state.CleanedCount == target.Count && ReferenceEquals(keysToStrip, AllManagedKeys))
        {
            return;
        }

        List<object>? toRemove = null;
        foreach (object key in target.Keys)
        {
            if (key is string strKey && keysToStrip.Contains(strKey))
            {
                toRemove ??= new List<object>();
                toRemove.Add(key);
            }
        }

        if (toRemove != null)
        {
            for (int i = 0; i < toRemove.Count; i++)
            {
                target.Remove(toRemove[i]);
            }
        }

        if (ReferenceEquals(keysToStrip, AllManagedKeys))
        {
            state.CleanedCount = target.Count;
        }
    }

    private static DropShadowEffect GetOrCreateShadow(double blur, double depth, double opacity)
    {
        string key = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{blur:R}|{depth:R}|{opacity:R}");
        lock (CacheGate)
        {
            if (ShadowCache.TryGetValue(key, out DropShadowEffect? cached))
            {
                return cached;
            }

            DropShadowEffect created = CreateShadow(blur, depth, opacity);
            if (ShadowCache.Count >= MaxShadowCacheEntries)
            {
                ShadowCache.Clear();
            }
            ShadowCache[key] = created;
            return created;
        }
    }

    private static DropShadowEffect CreateShadow(double blur, double depth, double opacity)
    {
        var effect = new DropShadowEffect { BlurRadius = blur, ShadowDepth = depth, Opacity = opacity, Color = Colors.Black };
        effect.Freeze();
        return effect;
    }

    public static SolidColorBrush Brush(string hex, double opacity = 1)
    {
        string key = opacity == 1
            ? hex
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{hex}@{opacity:R}");
        if (BrushCache.TryGetValue(key, out SolidColorBrush? cached))
        {
            return cached;
        }

        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)) { Opacity = opacity };
        brush.Freeze();
        if (BrushCache.Count >= MaxBrushCacheEntries)
        {
            BrushCache.Clear();
        }
        return BrushCache.GetOrAdd(key, brush);
    }

    private static string Blend(string first, string second, double amount)
    {
        Color a = (Color)ColorConverter.ConvertFromString(first), b = (Color)ColorConverter.ConvertFromString(second);
        return Color.FromRgb((byte)(a.R * (1 - amount) + b.R * amount), (byte)(a.G * (1 - amount) + b.G * amount), (byte)(a.B * (1 - amount) + b.B * amount)).ToString();
    }
}
