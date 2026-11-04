using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WinPieGestures;

internal static class ActionPanelUi
{
    internal static string T(string key) => I18n.T("ActionPanel" + key);
    internal static void Theme(Window window)
    {
        AppThemeManager.ApplyTheme(window, AppThemeManager.CurrentEffectiveTheme);
        window.SetResourceReference(Control.BackgroundProperty, "WindowBackgroundBrush");
        window.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
        foreach (var entry in new[] { (typeof(Button), "ModernButtonStyle"), (typeof(ComboBox), "FlatComboBoxStyle"), (typeof(TextBox), "ModernTextBoxStyle") })
            if (window.TryFindResource(entry.Item2) is Style style) window.Resources[entry.Item1] = style;
    }
    internal static Button Button(string text, Action clicked)
    {
        var button = new Button { Content = text, Margin = new Thickness(3), Padding = new Thickness(10, 6, 10, 6), MinHeight = 30 };
        button.SetResourceReference(Control.StyleProperty, "ModernButtonStyle");
        button.Click += (_, _) => clicked(); return button;
    }
    internal static TextBlock Caption(string text) => new()
    { Text = text, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis,
      HorizontalAlignment = HorizontalAlignment.Stretch, TextAlignment = TextAlignment.Center, ToolTip = text };
}
