using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinPieGestures.Plugins;

internal readonly record struct OfficialPluginAssetKey(string ReleaseTag, string AssetName);

internal sealed record OfficialPluginAssetStats(string ReleaseTag, string AssetName, DateTimeOffset PublishedAt, long? DownloadCount)
{
    internal OfficialPluginAssetKey Key => new(ReleaseTag, AssetName);
}

internal sealed record OfficialPluginStatsSnapshot(DateTimeOffset UpdatedAt, OfficialPluginAssetStats[] Assets, bool IsPartial = false);

/// <summary>Display-only GitHub statistics, separate from the trusted catalog and package transport.</summary>
internal static class OfficialPluginReleaseStats
{
    private const string ReleasesApi = "https://api.github.com/repos/Star-Pie/StarPie-Official-Plugins/releases";
    private const int PageSize = 50;
    private const int PageLimit = 10;
    private const long CacheLimit = 1024 * 1024;
    private static string CacheFile => Path.Combine(PluginPaths.Root, "github-release-stats.json");

    private static readonly HttpClient GitHub = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(8),
        MaxResponseContentBufferSize = 4 * 1024 * 1024,
        DefaultRequestHeaders =
        {
            { "User-Agent", "StarPie-plugin-marketplace" },
            { "Accept", "application/vnd.github+json" }
        }
    };

    internal static Task<OfficialPluginStatsSnapshot> RefreshAsync(OfficialPluginCatalog catalog,
        OfficialPluginStatsSnapshot? previous, CancellationToken token) => RefreshAsync(catalog, previous, GitHub, token);

    internal static async Task<OfficialPluginStatsSnapshot> RefreshAsync(OfficialPluginCatalog catalog,
        OfficialPluginStatsSnapshot? previous, HttpClient client, CancellationToken token)
    {
        var fresh = await FetchAsync(catalog, client, token).ConfigureAwait(false);
        var snapshot = PreserveUnavailableAssets(catalog, fresh, previous);
        SaveCached(snapshot, token);
        return snapshot;
    }

    private static OfficialPluginStatsSnapshot PreserveUnavailableAssets(OfficialPluginCatalog catalog,
        OfficialPluginStatsSnapshot fresh, OfficialPluginStatsSnapshot? previous)
    {
        if (!fresh.IsPartial || previous == null) return fresh;
        var history = fresh.Assets.ToDictionary(asset => asset.Key);
        var catalogKeys = catalog.Modules.SelectMany(Keys).ToHashSet();
        foreach (var cached in previous.Assets)
        {
            if (!catalogKeys.Contains(cached.Key)) continue;
            if (!history.TryGetValue(cached.Key, out var current))
                history[cached.Key] = cached;
            else if (current.DownloadCount == null && cached.DownloadCount != null)
                history[cached.Key] = current with { DownloadCount = cached.DownloadCount };
        }
        return fresh with { Assets = history.Values.ToArray() };
    }

    internal static async Task<OfficialPluginStatsSnapshot> FetchAsync(
        OfficialPluginCatalog catalog, HttpClient client, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var pending = catalog.Modules.SelectMany(Keys).ToHashSet();
        var found = new List<OfficialPluginAssetStats>();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(TimeSpan.FromSeconds(8));
        for (int page = 1; pending.Count > 0; page++)
        {
            if (page > PageLimit) throw new InvalidDataException("GitHub plugin release history exceeds the refresh limit.");
            string url = FormattableString.Invariant($"{ReleasesApi}?per_page={PageSize}&page={page}");
            using var response = await client.GetAsync(url, budget.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var body = await response.Content.ReadAsStreamAsync(budget.Token).ConfigureAwait(false);
            var releases = await JsonSerializer.DeserializeAsync<GitHubRelease[]>(body, cancellationToken: budget.Token)
                .ConfigureAwait(false) ?? throw new InvalidDataException("Missing GitHub releases.");
            foreach (var release in releases)
            {
                if (release is null || release.Draft || release.PublishedAt is not { } published ||
                    !OfficialPluginReleaseDiscovery.TryExtractOfficialReleaseTag(release.PageUrl, out string tag) ||
                    !string.Equals(tag, release.Tag, StringComparison.Ordinal)) continue;

                foreach (var asset in release.Assets ?? [])
                {
                    if (asset?.Name is not { } assetName || !pending.Remove(new(tag, assetName))) continue;
                    found.Add(new(tag, assetName, published, IsValidCount(asset.DownloadCount) ? asset.DownloadCount : null));
                }
            }
            if (releases.Length < PageSize) break;
        }
        return new(DateTimeOffset.UtcNow, found.ToArray(), pending.Count > 0 || found.Any(asset => asset.DownloadCount == null));
    }

    internal static IEnumerable<OfficialPluginAssetKey> Keys(OfficialPluginCatalogEntry entry) =>
        entry.Versions.Where(version => BuildReleaseUrl(version.ReleaseTag) != null &&
                version.AssetName.EndsWith(".spkg", StringComparison.OrdinalIgnoreCase))
            .Select(version => new OfficialPluginAssetKey(version.ReleaseTag, version.AssetName)).Distinct();

    internal static string? BuildReleaseUrl(string? tag)
    {
        // Catalog tags follow modules-yyyy.MM.dd.N. No URL supplied by the catalog or API is launched.
        if (tag is null || !tag.StartsWith("modules-", StringComparison.Ordinal) ||
            !Version.TryParse(tag.AsSpan("modules-".Length), out _)) return null;
        return OfficialPluginReleaseDiscovery.RepositoryUrl + "/releases/tag/" + Uri.EscapeDataString(tag);
    }

    internal static OfficialPluginStatsSnapshot? LoadCached()
    {
        try
        {
            var info = new FileInfo(CacheFile);
            if (!info.Exists || info.Length > CacheLimit) return null;
            using var input = info.OpenRead();
            var cache = JsonSerializer.Deserialize<OfficialPluginStatsSnapshot>(input);
            return cache is { Assets: not null } && cache.UpdatedAt != default &&
                cache.Assets.All(asset => asset != null && asset.PublishedAt != default && IsValidCount(asset.DownloadCount))
                ? cache : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsValidCount(long? count) => count is null or (>= 0 and <= int.MaxValue);

    internal static void SaveCached(OfficialPluginStatsSnapshot snapshot, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        string temporary = CacheFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CacheFile)!);
            using (var stream = new FileStream(temporary, FileMode.CreateNew))
                JsonSerializer.Serialize(stream, snapshot);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, CacheFile, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLogger.LogWarn($"[plugin] GitHub release stats cache: {ex.Message}");
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string? Tag { get; set; }
        [JsonPropertyName("html_url")] public string? PageUrl { get; set; }
        [JsonPropertyName("draft")] public bool Draft { get; set; }
        [JsonPropertyName("published_at")] public DateTimeOffset? PublishedAt { get; set; }
        [JsonPropertyName("assets")] public GitHubAsset[]? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("download_count")] public long? DownloadCount { get; set; }
    }
}
