using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using SystemFonts = System.Windows.SystemFonts;

namespace WinPieGestures;

internal static class UiTypography
{
    public static void Apply(FrameworkElement root, AppConfig? config)
    {
        string choice = config?.UiFontFamily ?? "System";
        if (string.Equals(choice, "Theme", StringComparison.OrdinalIgnoreCase))
            root.SetResourceReference(TextElement.FontFamilyProperty, "ArtFontFamily");
        else if (!IsNamedFont(choice))
            root.SetResourceReference(TextElement.FontFamilyProperty, SystemFonts.MessageFontFamilyKey);
        else root.SetValue(TextElement.FontFamilyProperty, new FontFamily(choice));
    }

    public static bool IsNamedFont(string? choice) => !string.IsNullOrWhiteSpace(choice) && choice.Length <= 160 &&
        !string.Equals(choice, "System", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(choice, "Theme", StringComparison.OrdinalIgnoreCase) && choice.IndexOfAny(['#', '/', '\\', ':']) < 0;
}
