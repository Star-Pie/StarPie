using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using SystemFonts = System.Windows.SystemFonts;

namespace WinPieGestures;

internal static class UiTypography
{
    private enum TypographyMode
    {
        System,
        Theme,
        Named
    }

    private sealed class AppliedState
    {
        public TypographyMode? Mode;
        public string? NamedChoice;
        public FontFamily? NamedFamily;
    }

    private static readonly ConditionalWeakTable<FrameworkElement, AppliedState> AppliedStates = new();

    public static void Apply(FrameworkElement root, AppConfig? config)
    {
        if (root == null)
        {
            return;
        }

        string choice = config?.UiFontFamily ?? "System";
        AppliedState state = AppliedStates.GetValue(root, static _ => new AppliedState());
        ValueSource source = DependencyPropertyHelper.GetValueSource(root, TextElement.FontFamilyProperty);

        if (string.Equals(choice, "Theme", StringComparison.OrdinalIgnoreCase))
        {
            if (state.Mode == TypographyMode.Theme && source.IsExpression)
            {
                return;
            }

            root.SetResourceReference(TextElement.FontFamilyProperty, "ArtFontFamily");
            state.Mode = TypographyMode.Theme;
            state.NamedChoice = null;
            state.NamedFamily = null;
        }
        else if (!IsNamedFont(choice))
        {
            if (state.Mode == TypographyMode.System && source.IsExpression)
            {
                return;
            }

            root.SetResourceReference(TextElement.FontFamilyProperty, SystemFonts.MessageFontFamilyKey);
            state.Mode = TypographyMode.System;
            state.NamedChoice = null;
            state.NamedFamily = null;
        }
        else
        {
            FontFamily family = ArtStyles.ArtStyleResources.GetCachedFontFamily(choice);
            if (state.Mode == TypographyMode.Named &&
                string.Equals(state.NamedChoice, choice, StringComparison.Ordinal) &&
                !source.IsExpression &&
                ReferenceEquals(root.GetValue(TextElement.FontFamilyProperty), family))
            {
                return;
            }

            root.SetValue(TextElement.FontFamilyProperty, family);
            state.Mode = TypographyMode.Named;
            state.NamedChoice = choice;
            state.NamedFamily = family;
        }
    }

    internal static void Invalidate(FrameworkElement root)
    {
        if (root != null)
        {
            AppliedStates.Remove(root);
        }
    }

    public static bool IsNamedFont(string? choice) => !string.IsNullOrWhiteSpace(choice) && choice.Length <= 160 &&
        !string.Equals(choice, "System", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(choice, "Theme", StringComparison.OrdinalIgnoreCase) && choice.IndexOfAny(['#', '/', '\\', ':']) < 0;
}
