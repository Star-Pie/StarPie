using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using WinPieGestures.Plugins;

namespace WinPieGestures;

public partial class SettingsWindow
{
    private IReadOnlyList<OfficialPluginListItem> _marketCards = [];
    private IReadOnlyList<PluginListItem> _installedCards = [];
    private bool _updatingLibraryFilters;

    private void PluginLibraryTab_Checked(object sender, RoutedEventArgs e)
    {
        if (MarketView == null || InstalledView == null) return;
        bool installed = ReferenceEquals(sender, InstalledTabButton);
        MarketView.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
        InstalledView.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderPluginLibraryFilters()
    {
        if (MarketFilterCombo == null || InstalledFilterCombo == null) return;
        MarketTabButton.Content = I18n.TF("PluginLibraryMarketTab", _marketCards.Count);
        InstalledTabButton.Content = I18n.TF("PluginLibraryInstalledTab", _installedCards.Count);
        _updatingLibraryFilters = true;
        try
        {
            var categories = _marketCards.SelectMany(item => item.Categories)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(category => KeyValuePair.Create(category, PluginCardPresentation.CategoryName(category)))
                .OrderBy(pair => pair.Value, StringComparer.CurrentCultureIgnoreCase);
            var sources = _installedCards.Select(row => KeyValuePair.Create(row.IsOfficial ? "official" : "local", row.SourceText))
                .Distinct().OrderBy(pair => pair.Value, StringComparer.CurrentCultureIgnoreCase);
            PopulateFilter(MarketFilterCombo, I18n.T("PluginLibraryAllCategories"), categories);
            PopulateFilter(InstalledFilterCombo, I18n.T("PluginLibraryAllSources"), sources);
        }
        finally { _updatingLibraryFilters = false; }
        ApplyMarketFilter();
        ApplyInstalledFilter();
    }

    private static void PopulateFilter(System.Windows.Controls.ComboBox box, string all, IEnumerable<KeyValuePair<string, string>> options)
    {
        string previous = box.SelectedValue as string ?? "";
        var choices = options.Prepend(KeyValuePair.Create("", all)).ToList();
        box.DisplayMemberPath = "Value";
        box.SelectedValuePath = "Key";
        box.ItemsSource = choices;
        box.SelectedValue = choices.FirstOrDefault(pair => string.Equals(pair.Key, previous,
            StringComparison.OrdinalIgnoreCase)).Key ?? "";
        box.IsEnabled = choices.Count > 1;
        AutomationProperties.SetName(box, all);
    }

    private void PluginLibraryFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingLibraryFilters) return;
        if (ReferenceEquals(sender, MarketFilterCombo)) ApplyMarketFilter();
        else if (ReferenceEquals(sender, InstalledFilterCombo)) ApplyInstalledFilter();
    }

    private void ApplyMarketFilter()
    {
        if (OfficialPluginItemsControl == null || MarketEmptyText == null) return;
        string? category = MarketFilterCombo.SelectedValue as string;
        IReadOnlyList<OfficialPluginListItem> rows = string.IsNullOrEmpty(category) ? _marketCards :
            _marketCards.Where(item => item.Categories.Contains(category, StringComparer.OrdinalIgnoreCase)).ToList();
        OfficialPluginItemsControl.ItemsSource = rows;
        MarketEmptyText.Text = I18n.T("PluginLibraryMarketEmpty");
        MarketEmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyInstalledFilter()
    {
        if (PluginListBox == null || PluginsEmptyStatePanel == null) return;
        string? source = InstalledFilterCombo.SelectedValue as string;
        IReadOnlyList<PluginListItem> rows = string.IsNullOrEmpty(source) ? _installedCards :
            _installedCards.Where(item => item.IsOfficial == (source == "official")).ToList();
        PluginListBox.ItemsSource = rows;
        PluginsEmptyStatePanel.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
