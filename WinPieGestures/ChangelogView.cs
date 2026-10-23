using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WinPieGestures;

internal static class ChangelogView
{
    internal static void Populate(Panel panel, IReadOnlyList<ChangelogEntry> entries)
    {
        panel.Children.Clear();
        for (int index = 0; index < entries.Count; index++)
        {
            ChangelogEntry entry = entries[index];
            var heading = new TextBlock
            {
                Text = entry.Title, FontSize = 13, FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
            };
            heading.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            var expander = new Expander
            {
                Header = heading, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 6, 0, 6),
            };
            expander.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
            expander.Expanded += (sender, args) =>
            {
                if (!ReferenceEquals(args.OriginalSource, sender) || expander.Content != null) return;
                var content = new TextBox
                {
                    Text = entry.Markdown, IsReadOnly = true, AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0),
                    Margin = new Thickness(0, 8, 0, 2), Padding = new Thickness(10),
                    FontSize = 12, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                };
                content.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
                content.SetResourceReference(Control.BackgroundProperty, "SubtleCardBrush");
                expander.Content = content;
            };
            expander.Collapsed += (sender, args) =>
            {
                if (ReferenceEquals(args.OriginalSource, sender)) expander.Content = null;
            };
            panel.Children.Add(expander);
            // Materialize only the newest nonempty section; older bodies stay lazy.
            if (index == 0) expander.IsExpanded = true;
        }
    }
}
