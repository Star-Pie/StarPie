using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using WinPieGestures.Plugins;

namespace WinPieGestures;

public partial class SettingsWindow
{
    private IReadOnlyList<OfficialPluginListItem> _marketCards = [];
    private IReadOnlyList<PluginListItem> _installedCards = [];
    private bool _updatingLibraryFilters;
    private OfficialPluginStatsSnapshot? _releaseStats;
    private bool _releaseStatsRefreshFailed;

    private async Task RefreshPluginReleaseStatsAsync(OfficialPluginCatalog catalog, CancellationToken lifetime)
    {
        try
        {
            _releaseStats = await OfficialPluginReleaseStats.RefreshAsync(catalog, _releaseStats, lifetime);
            _releaseStatsRefreshFailed = false;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException or InvalidDataException or JsonException)
        {
            _releaseStatsRefreshFailed = true;
            AppLogger.LogWarn($"[plugin] GitHub release stats refresh: {ex.Message}");
        }
    }

    private void RenderPluginReleaseStatsStatus()
    {
        if (PluginReleaseStatsStatusText == null) return;
        if (_releaseStats == null)
        {
            PluginReleaseStatsStatusText.Text = I18n.T(_releaseStatsRefreshFailed ? "PluginStatsRefreshFailed" : "PluginStatsNotLoaded");
            return;
        }
        string cachedAt = _releaseStats.UpdatedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        PluginReleaseStatsStatusText.Text = _releaseStatsRefreshFailed
            ? I18n.TF(_releaseStats.IsPartial ? "PluginStatsUsingPartialCache" : "PluginStatsUsingCache", cachedAt)
            : I18n.TF(_releaseStats.IsPartial ? "PluginStatsPartialRefresh" : "PluginStatsLastRefresh", cachedAt);
    }

    private void OpenOfficialPluginReleaseButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string url } ||
            !OfficialPluginReleaseDiscovery.TryExtractOfficialReleaseTag(url, out string tag) ||
            OfficialPluginReleaseStats.BuildReleaseUrl(tag) is not { } releasePage) return;
        try { Process.Start(new ProcessStartInfo(releasePage) { UseShellExecute = true }); }
        catch (Exception ex)
        {
            AppLogger.LogWarn($"[plugin] Open official release page: {ex.Message}");
            System.Windows.MessageBox.Show(this, I18n.T("PluginStatsReleaseOpenFailed"), I18n.T("PluginStatsReleaseLink"));
        }
    }

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
            PopulateFilter(MarketStatusCombo, I18n.T("PluginStatsAllPlugins"),
            [
                Choice("updates", "PluginStatsHaveUpdates"), Choice("installed", "PluginStatsInstalled"),
                Choice("available", "PluginStatsNotInstalled"), Choice("recentPublished", "PluginStatsRecentPublished"),
                Choice("recentUpdated", "PluginStatsRecentUpdated"),
                Choice("downloads100", "PluginStatsDownloads100"), Choice("downloads1000", "PluginStatsDownloads1000")
            ]);
            PopulateFilter(InstalledFilterCombo, I18n.T("PluginLibraryAllSources"), sources);
            var sortChoices = new[]
            {
                Choice("published", "PluginStatsSortPublished"), Choice("updated", "PluginStatsSortUpdated"),
                Choice("downloads", "PluginStatsSortDownloads")
            };
            PopulateFilter(MarketSortCombo, I18n.T("PluginStatsSortName"), sortChoices);
            PopulateFilter(InstalledSortCombo, I18n.T("PluginStatsSortName"), sortChoices);
            AutomationProperties.SetName(MarketSortCombo, I18n.T("PluginStatsSortLabel"));
            AutomationProperties.SetName(InstalledSortCombo, I18n.T("PluginStatsSortLabel"));
        }
        finally { _updatingLibraryFilters = false; }
        ApplyMarketFilter();
        ApplyInstalledFilter();
    }

    private static KeyValuePair<string, string> Choice(string key, string text) => KeyValuePair.Create(key, I18n.T(text));

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
        if (ReferenceEquals(sender, MarketFilterCombo) || ReferenceEquals(sender, MarketStatusCombo) ||
            ReferenceEquals(sender, MarketSortCombo)) ApplyMarketFilter();
        else if (ReferenceEquals(sender, InstalledFilterCombo) || ReferenceEquals(sender, InstalledSortCombo)) ApplyInstalledFilter();
    }

    private void PluginLibrarySearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        bool hasQuery = !string.IsNullOrEmpty(PluginLibrarySearchBox?.Text);
        if (PluginLibrarySearchPlaceholder != null)
            PluginLibrarySearchPlaceholder.Visibility = hasQuery ? Visibility.Collapsed : Visibility.Visible;
        if (PluginLibrarySearchClearButton != null)
            PluginLibrarySearchClearButton.Visibility = hasQuery ? Visibility.Visible : Visibility.Collapsed;
        ApplyMarketFilter();
        ApplyInstalledFilter();
    }

    private void PluginLibrarySearchClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (PluginLibrarySearchBox == null) return;
        PluginLibrarySearchBox.Text = "";
        PluginLibrarySearchBox.Focus();
    }

    private void PluginLibrarySearchBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Escape || string.IsNullOrEmpty(PluginLibrarySearchBox?.Text)) return;
        PluginLibrarySearchBox.Text = "";
        e.Handled = true;
    }

    private void ApplyMarketFilter()
    {
        if (OfficialPluginItemsControl == null || MarketEmptyText == null) return;
        string? category = MarketFilterCombo?.SelectedValue as string;
        string? filter = MarketStatusCombo?.SelectedValue as string;
        string? query = PluginLibrarySearchBox?.Text;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var rows = PluginLibraryQuery.Sort(
            _marketCards.Where(item => PluginLibraryQuery.Matches(item, category, filter, now, query)),
            MarketSortCombo?.SelectedValue as string, item => item.ReleaseInfo, item => item.DisplayName, item => item.Module.Id).ToList();
        OfficialPluginItemsControl.ItemsSource = rows;
        MarketEmptyText.Text = I18n.T(_marketCards.Count == 0 ? "PluginLibraryMarketEmpty" : "PluginStatsNoMatches");
        MarketEmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyInstalledFilter()
    {
        if (PluginListBox == null || PluginsEmptyStatePanel == null) return;
        string? source = InstalledFilterCombo.SelectedValue as string;
        string? query = PluginLibrarySearchBox?.Text;
        var rows = PluginLibraryQuery.Sort(
            _installedCards.Where(item => (string.IsNullOrEmpty(source) || item.IsOfficial == (source == "official")) &&
                PluginLibraryQuery.MatchesSearch(item, query)),
            InstalledSortCombo?.SelectedValue as string,
            item => item.ReleaseInfo, item => item.DisplayName, item => item.PluginId).ToList();
        PluginListBox.ItemsSource = rows;
        if (PluginsEmptyTitleText != null && PluginsEmptyHintText != null)
        {
            if (_installedCards.Count == 0)
            {
                PluginsEmptyTitleText.Text = I18n.T("PluginsEmptyTitle");
                PluginsEmptyHintText.Visibility = Visibility.Visible;
            }
            else
            {
                PluginsEmptyTitleText.Text = I18n.T("PluginStatsNoMatches");
                PluginsEmptyHintText.Visibility = Visibility.Collapsed;
            }
        }
        PluginsEmptyStatePanel.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
