using System.Globalization;

namespace WinPieGestures.Plugins;

internal sealed record PluginReleaseCardInfo(long? Downloads = null, DateTimeOffset? FirstPublished = null,
    DateTimeOffset? LastPublished = null, string? ReleasePageUrl = null)
{
    internal static PluginReleaseCardInfo Empty { get; } = new();

    public bool HasReleasePage => ReleasePageUrl != null;
    public bool HasDates => FirstPublished.HasValue || LastPublished.HasValue;
    public string DownloadsText => Downloads is { } downloads
        ? I18n.TF("PluginStatsDownloads", downloads.ToString("N0", CultureInfo.CurrentCulture))
        : I18n.T("PluginStatsDownloadsUnknown");
    public string ReleaseButtonText => I18n.T("PluginStatsReleaseLink");
    public string DatesText => I18n.TF("PluginStatsDates", FormatDate(FirstPublished), FormatDate(LastPublished));

    private static string FormatDate(DateTimeOffset? date) =>
        date?.ToLocalTime().ToString("d", CultureInfo.CurrentCulture) ?? "—";

    public static PluginReleaseCardInfo Build(OfficialPluginCatalogEntry entry,
        IReadOnlyDictionary<OfficialPluginAssetKey, OfficialPluginAssetStats> assets)
    {
        var keys = OfficialPluginReleaseStats.Keys(entry).ToArray();
        var latestTag = keys.Select(key => key.ReleaseTag).OrderByDescending(tag =>
            Version.Parse(tag.AsSpan("modules-".Length))).FirstOrDefault();
        string? releaseUrl = OfficialPluginReleaseStats.BuildReleaseUrl(latestTag);
        var matched = keys.Where(assets.ContainsKey).Select(key => assets[key]).ToArray();
        // Incomplete histories cannot give a total or a reliable first publication date.
        if (keys.Length == 0 || matched.Length != keys.Length) return new(ReleasePageUrl: releaseUrl);

        var newest = matched.OrderByDescending(item => item.PublishedAt)
            .ThenByDescending(item => Version.Parse(item.ReleaseTag.AsSpan("modules-".Length))).First();
        long? total = matched.All(item => item.DownloadCount.HasValue)
            ? matched.Sum(item => (long?)item.DownloadCount!.Value) : null;
        return new(total, matched.Min(item => item.PublishedAt), newest.PublishedAt,
            OfficialPluginReleaseStats.BuildReleaseUrl(newest.ReleaseTag));
    }
}

internal static class PluginLibraryQuery
{
    public static IEnumerable<T> Sort<T>(IEnumerable<T> cards, string? order,
        Func<T, PluginReleaseCardInfo> release, Func<T, string> name, Func<T, string> id)
    {
        IOrderedEnumerable<T> ordered = order switch
        {
            "downloads" => cards.OrderByDescending(card => release(card).Downloads),
            "published" => cards.OrderByDescending(card => release(card).FirstPublished),
            "updated" => cards.OrderByDescending(card => release(card).LastPublished),
            _ => cards.OrderBy(name, StringComparer.CurrentCultureIgnoreCase)
        };
        return ordered.ThenBy(name, StringComparer.CurrentCultureIgnoreCase).ThenBy(id, StringComparer.OrdinalIgnoreCase);
    }

    public static bool Matches(OfficialPluginListItem item, string? category, string? filter, DateTimeOffset now)
    {
        if (!string.IsNullOrEmpty(category) && !item.Categories.Contains(category, StringComparer.OrdinalIgnoreCase))
            return false;
        return filter switch
        {
            "updates" => item.IsInstalled && item.CanInstall,
            "installed" => item.IsInstalled,
            "available" => !item.IsInstalled,
            "recentPublished" => item.ReleaseInfo.FirstPublished >= now.AddDays(-30),
            "recentUpdated" => item.ReleaseInfo.LastPublished >= now.AddDays(-30),
            "downloads100" => item.ReleaseInfo.Downloads >= 100,
            "downloads1000" => item.ReleaseInfo.Downloads >= 1000,
            _ => true
        };
    }
}
