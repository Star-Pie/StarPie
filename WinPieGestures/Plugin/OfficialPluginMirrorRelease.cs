using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WinPieGestures.Plugins;

internal sealed class OfficialPluginMirrorRelease
{
    internal OfficialPluginMirrorRelease(
        string tag,
        DateTimeOffset publishedAt,
        IReadOnlyDictionary<string, string> assets,
        bool hasSourcePublicationTime = false)
    {
        Tag = tag;
        PublishedAt = publishedAt;
        Assets = assets;
        HasSourcePublicationTime = hasSourcePublicationTime;
    }

    public string Tag { get; }

    public DateTimeOffset PublishedAt { get; }

    public IReadOnlyDictionary<string, string> Assets { get; }
    public bool HasSourcePublicationTime { get; }
}

internal static class OfficialPluginMirrorReleaseParser
{
    internal const int MaxInputBytes = 2 * 1024 * 1024;
    internal const int MaxDepth = 32;
    internal const int MaxReleases = 100;
    internal const int MaxAssetsPerRelease = 512;
    internal const int MaxAssetNameLength = 200;
    internal const string CatalogAssetName = "module-catalog.json";

    private static readonly Regex TagPattern = new(
        @"\A[A-Za-z0-9][A-Za-z0-9._+\-]{0,127}\z",
        RegexOptions.CultureInvariant);

    public static IReadOnlyList<OfficialPluginMirrorRelease> Parse(
        string json,
        Func<string, bool> isAllowedAssetUrl)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(isAllowedAssetUrl);

        if (Encoding.UTF8.GetByteCount(json) > MaxInputBytes)
        {
            throw new InvalidDataException(
                $"The release JSON exceeds the {MaxInputBytes}-byte limit.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions
                {
                    MaxDepth = MaxDepth,
                });
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The release JSON is invalid.", exception);
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("The release JSON root must be an array.");
            }

            if (root.GetArrayLength() > MaxReleases)
            {
                throw new InvalidDataException(
                    $"The release JSON contains more than {MaxReleases} releases.");
            }

            var releases = new List<OfficialPluginMirrorRelease>();
            var tags = new HashSet<string>(StringComparer.Ordinal);

            foreach (JsonElement releaseElement in root.EnumerateArray())
            {
                if (releaseElement.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException("Each release must be an object.");
                }

                if (IsDraft(releaseElement))
                {
                    continue;
                }

                List<AssetEntry>? assets = ReadSafeAssets(
                    releaseElement,
                    isAllowedAssetUrl);
                if (assets == null ||
                    !assets.Any(asset =>
                        string.Equals(
                            asset.Name,
                            CatalogAssetName,
                            StringComparison.Ordinal)))
                {
                    continue;
                }

                string tag = ReadTag(releaseElement);
                if (!tags.Add(tag))
                {
                    throw new InvalidDataException(
                        $"The release JSON contains a duplicate tag: {tag}");
                }

                DateTimeOffset publishedAt = ReadPublishedAt(releaseElement, out bool hasSourcePublicationTime);
                var assetMap = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (AssetEntry asset in assets)
                {
                    assetMap.Add(asset.Name, asset.Url);
                }

                releases.Add(new OfficialPluginMirrorRelease(
                    tag,
                    publishedAt,
                    new ReadOnlyDictionary<string, string>(assetMap), hasSourcePublicationTime));
            }

            List<OfficialPluginMirrorRelease> ordered = releases
                .OrderByDescending(release => release.PublishedAt)
                .ToList();
            return new ReadOnlyCollection<OfficialPluginMirrorRelease>(ordered);
        }
    }

    private static bool IsDraft(JsonElement release)
    {
        if (release.TryGetProperty("draft", out JsonElement draft))
        {
            if (draft.ValueKind == JsonValueKind.True)
            {
                return true;
            }

            if (draft.ValueKind != JsonValueKind.False &&
                draft.ValueKind != JsonValueKind.Null)
            {
                throw new InvalidDataException(
                    "The release draft field must be a boolean.");
            }
        }

        if (release.TryGetProperty(
                "release_status",
                out JsonElement releaseStatus) &&
            releaseStatus.ValueKind == JsonValueKind.String &&
            string.Equals(
                releaseStatus.GetString(),
                "draft",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static List<AssetEntry>? ReadSafeAssets(
        JsonElement release,
        Func<string, bool> isAllowedAssetUrl)
    {
        if (!release.TryGetProperty("assets", out JsonElement assetsElement) ||
            assetsElement.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (assetsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The release assets field must be an array.");
        }

        int assetCount = assetsElement.GetArrayLength();
        if (assetCount > MaxAssetsPerRelease)
        {
            throw new InvalidDataException(
                $"A release contains more than {MaxAssetsPerRelease} assets.");
        }

        var assets = new List<AssetEntry>(assetCount);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement assetElement in assetsElement.EnumerateArray())
        {
            if (assetElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("Each release asset must be an object.");
            }

            if (!assetElement.TryGetProperty("name", out JsonElement nameElement) ||
                nameElement.ValueKind != JsonValueKind.String ||
                !assetElement.TryGetProperty(
                    "browser_download_url",
                    out JsonElement urlElement) ||
                urlElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            string? name = nameElement.GetString();
            string? url = urlElement.GetString();
            if (!IsSafeAssetName(name) ||
                string.IsNullOrWhiteSpace(url) ||
                !isAllowedAssetUrl(url))
            {
                continue;
            }

            if (!names.Add(name!))
            {
                throw new InvalidDataException(
                    $"A release contains duplicate asset names: {name}");
            }

            assets.Add(new AssetEntry(name!, url));
        }

        return assets;
    }

    private static string ReadTag(JsonElement release)
    {
        if (!release.TryGetProperty("tag_name", out JsonElement tagElement) ||
            tagElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException("A release is missing its tag_name.");
        }

        string? tag = tagElement.GetString();
        if (tag == null || !TagPattern.IsMatch(tag))
        {
            throw new InvalidDataException("A release has an invalid tag_name.");
        }

        return tag;
    }

    private static DateTimeOffset ReadPublishedAt(JsonElement release, out bool hasSourcePublicationTime)
    {
        hasSourcePublicationTime = false;
        // Mirroring an old release creates it today. The synchronization workflow
        // preserves the source publication time in a final, non-rendered line.
        if (release.TryGetProperty("body", out JsonElement body) && body.ValueKind == JsonValueKind.String)
        {
            string text = body.GetString() ?? "";
            const string prefix = "<!-- starpie-source-published-at: ";
            int start = text.LastIndexOf(prefix, StringComparison.Ordinal);
            if (start >= 0 && text.EndsWith(" -->", StringComparison.Ordinal))
            {
                string timestamp = text[(start + prefix.Length)..^4];
                if (DateTimeOffset.TryParseExact(timestamp, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset sourcePublishedAt))
                {
                    hasSourcePublicationTime = true;
                    return sourcePublishedAt;
                }
            }
        }
        if (TryReadDate(release, "published_at", out DateTimeOffset publishedAt))
        {
            return publishedAt;
        }

        if (TryReadDate(release, "created_at", out DateTimeOffset createdAt))
        {
            return createdAt;
        }

        throw new InvalidDataException(
            "A release has neither a valid published_at nor created_at.");
    }

    private static bool TryReadDate(
        JsonElement release,
        string propertyName,
        out DateTimeOffset value)
    {
        value = default;
        if (!release.TryGetProperty(propertyName, out JsonElement dateElement) ||
            dateElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        string? text = dateElement.GetString();
        return !string.IsNullOrWhiteSpace(text) &&
            DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out value);
    }

    private static bool IsSafeAssetName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            name.Length > MaxAssetNameLength ||
            name is "." or "..")
        {
            return false;
        }

        foreach (char character in name)
        {
            if (character == '/' ||
                character == '\\' ||
                char.IsControl(character))
            {
                return false;
            }
        }

        return true;
    }

    private sealed record AssetEntry(string Name, string Url);
}
