using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace WinPieGestures;

public partial class UpdateManager
{
    private readonly HttpClient _atomGitHttpClient;
    private const string AtomGitRepository = "https://atomgit.com/Star-Pie/StarPie";
    private const string AtomGitReleasesApi = "https://api.atomgit.com/api/v5/repos/Star-Pie/StarPie/releases";

    public static string NormalizeReleaseSource(string? source) =>
        source?.Trim().ToLowerInvariant() switch { "github" => "github", "auto" => "auto", _ => "atomgit" };

    public static bool AcceptsReleaseSource(string? preference, string? actual) =>
        actual is "atomgit" or "github" &&
        (NormalizeReleaseSource(preference) == "auto" || NormalizeReleaseSource(preference) == actual);

    internal async Task<SourceMeasurement[]> ProbeReleaseSourcesAsync(CancellationToken token) =>
        (await CompareReleasesAsync(token).ConfigureAwait(false)).Select(result => result.Measurement).ToArray();

    private Task<SourceResponse<List<ReleaseInfo>>[]> CompareReleasesAsync(CancellationToken token) =>
        MirrorSourceProbe.CompareAsync("github", "atomgit", ReadReleaseIndexAsync, token, TimeSpan.FromSeconds(22));

    private async Task<List<ReleaseInfo>> ReadReleaseIndexAsync(string source, CancellationToken ct)
    {
        using var api = CancellationTokenSource.CreateLinkedTokenSource(ct);
        api.CancelAfter(TimeSpan.FromSeconds(7));
        try
        {
            var releases = source == "atomgit" ? await FetchAtomGitReleaseIndexAsync(api.Token).ConfigureAwait(false)
                : await ReadGitHubAssetIndexAsync(api.Token).ConfigureAwait(false);
            if (releases.Count == 0) throw new InvalidDataException("No installable releases.");
            return releases;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && (ex is OperationCanceledException ||
            ex is HttpRequestException http && IsIndexUnavailable(http)))
        {
            return await FetchTaggedReleasesAsync(source, ct).ConfigureAwait(false);
        }
    }

    private async Task<List<ReleaseInfo>> ReadGitHubAssetIndexAsync(CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync($"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases?per_page=30", ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Invalid release list.");
        return doc.RootElement.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object &&
                (!item.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.True))
            .Select(item => ParseReleaseElement(item, synthesizeMissingAssets: false))
            .OfType<ReleaseInfo>().Where(r => r.ComparableVersion != null &&
                (!string.IsNullOrEmpty(r.StandaloneAssetUrl) || !string.IsNullOrEmpty(r.LightweightAssetUrl))).ToList();
    }

    public static string GetReleasePageUrl(ReleaseInfo? release, string? source)
    {
        if ((NormalizeReleaseSource(source) == "auto" ? release?.Source : NormalizeReleaseSource(source)) == "atomgit") return AtomGitRepository + "/releases";
        return release?.Source == "github" && Uri.TryCreate(release.HtmlUrl, UriKind.Absolute, out var url) &&
            url.Scheme == Uri.UriSchemeHttps && url.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            ? url.AbsoluteUri : "https://github.com/Star-Pie/StarPie/releases";
    }

    // Public AtomGit release assets may be served by AtomGit/GitCode or their OBS storage.
    internal static bool IsAtomGitAssetUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(url.UserInfo) || !url.IsDefaultPort) return false;
        return new[] { "atomgit.com", "gitcode.com", "gitcode.net", "myhuaweicloud.com" }
            .Any(host => url.Host.Equals(host, StringComparison.OrdinalIgnoreCase) ||
                url.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<HttpResponseMessage> SendAtomGitGetAsync(string value, CancellationToken ct, bool probeZip = false)
    {
        var url = new Uri(value);
        for (int redirects = 0; redirects <= 5; redirects++)
        {
            if (!IsAtomGitAssetUrl(url.AbsoluteUri)) throw new InvalidDataException("Untrusted AtomGit redirect destination.");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (probeZip) request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 3);
            var response = await _atomGitHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308)) return response;
            var location = response.Headers.Location;
            response.Dispose();
            if (location == null) throw new InvalidDataException("AtomGit redirect is missing its destination.");
            url = new Uri(url, location);
        }
        throw new InvalidDataException("AtomGit redirect limit exceeded.");
    }

    private async Task<List<ReleaseInfo>> FetchAtomGitReleaseIndexAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var releases = new Dictionary<string, ReleaseInfo>(StringComparer.OrdinalIgnoreCase);
        // Bound both response count and total query time. Never fall through to GitHub.
        for (int page = 1; page <= 5; page++)
        {
            using var response = await SendAtomGitGetAsync(
                $"{AtomGitReleasesApi}?per_page=100&page={page}", timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("AtomGit returned an invalid release list.");
            foreach (var element in document.RootElement.EnumerateArray())
            {
                var release = ParseAtomGitRelease(element);
                if (release != null) releases.TryAdd(release.TagName, release);
            }
            if (document.RootElement.GetArrayLength() < 100)
                return releases.Values.OrderByDescending(r => r.ComparableVersion).ThenByDescending(r => r.PublishedAt).ToList();
        }
        throw new InvalidDataException("AtomGit release pagination exceeded the safety limit.");
    }

    internal ReleaseInfo? ParseAtomGitRelease(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        static string Text(JsonElement item, string name) => item.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        string tag = Text(element, "tag_name");
        if (!Regex.IsMatch(tag, @"^[vV]?\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$") ||
            !ReleaseVersion.TryParse(tag, out var version) || version == null ||
            (element.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) ||
            Text(element, "release_status").Equals("draft", StringComparison.OrdinalIgnoreCase)) return null;
        if (!element.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
        var release = new ReleaseInfo
        {
            Source = "atomgit", TagName = tag, Title = Text(element, "name"), Body = Text(element, "body"),
            ComparableVersion = version, ParsedVersion = version.CoreVersion,
            IsNewerVersion = version.CompareTo(GetCurrentReleaseVersion()) > 0,
            IsPrerelease = version.IsPrerelease || Text(element, "release_status") == "pre" ||
                (element.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True),
            HtmlUrl = AtomGitRepository + "/releases",
        };
        if (DateTimeOffset.TryParse(Text(element, "published_at"), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var published) ||
            DateTimeOffset.TryParse(Text(element, "created_at"), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out published)) release.PublishedAt = published.LocalDateTime;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object) continue;
            string name = Text(asset, "name");
            string url = Text(asset, "browser_download_url");
            if (!IsAtomGitAssetUrl(url)) continue;
            bool standalone = name.Equals($"StarPie-{tag}-Standalone-win-x64.zip", StringComparison.OrdinalIgnoreCase);
            bool lightweight = name.Equals($"StarPie-{tag}-Lightweight-win-x64.zip", StringComparison.OrdinalIgnoreCase);
            bool checksum = name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase);
            if (!standalone && !lightweight && !checksum) continue;
            if (!names.Add(name)) return null; // Ambiguous same-name assets are not safe update candidates.
            long size = asset.TryGetProperty("size", out var sizeValue) && sizeValue.ValueKind == JsonValueKind.Number &&
                sizeValue.TryGetInt64(out var length) && length > 0 ? length : 0;
            if (standalone) { release.StandaloneAssetUrl = url; release.StandaloneAssetSize = size; }
            else if (lightweight) { release.LightweightAssetUrl = url; release.LightweightAssetSize = size; }
            else release.ChecksumsAssetUrl = url;
        }
        // A partially mirrored release is not ready for in-app update/rollback.
        return release.ChecksumsAssetUrl != null &&
            (release.StandaloneAssetUrl != null || release.LightweightAssetUrl != null) ? release : null;
    }

    public async Task DownloadReleaseAssetAsync(ReleaseInfo release, bool standalone, string destinationPath,
        IProgress<UpdateProgressInfo>? progress, CancellationToken ct, string proxySource = "ghfast",
        string customProxy = "", string releaseSource = "github")
    {
        if (NormalizeReleaseSource(releaseSource) == "auto") release = release.ForPackage(standalone);
        string source = release.Source;
        if (!AcceptsReleaseSource(releaseSource, source)) throw new InvalidOperationException("Release belongs to a different update source.");
        string? rawUrl = standalone ? release.StandaloneAssetUrl : release.LightweightAssetUrl;
        if (string.IsNullOrEmpty(rawUrl)) throw new InvalidDataException("Selected release package is not available.");
        string? expectedHash = standalone ? release.StandaloneChecksum : release.LightweightChecksum;
        if (source == "atomgit")
        {
            if (!IsAtomGitAssetUrl(rawUrl) || !IsAtomGitAssetUrl(release.ChecksumsAssetUrl))
                throw new InvalidDataException("AtomGit package/checksum attachment is not ready.");
            if (expectedHash == null)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                using var response = await SendAtomGitGetAsync(release.ChecksumsAssetUrl!, timeout.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                string fileName = $"StarPie-{release.TagName}-{(standalone ? "Standalone" : "Lightweight")}-win-x64.zip";
                expectedHash = ParseChecksum(await ReadChecksumsAsync(response.Content, timeout.Token).ConfigureAwait(false), fileName);
            }
        }
        string downloadUrl = source == "atomgit" ? rawUrl : GetProxiedDownloadUrl(rawUrl, proxySource, customProxy);
        await DownloadAssetAsync(downloadUrl, destinationPath, progress, ct, source, expectedHash).ConfigureAwait(false);
    }

    private static bool IsIndexUnavailable(HttpRequestException exception) =>
        exception.StatusCode is null or System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.RequestTimeout
        || (int?)exception.StatusCode >= 500;

    // Tags can exist before a Release. Only advertise packages actually listed in its checksum attachment.
    private async Task<List<ReleaseInfo>> FetchTaggedReleasesAsync(string source, CancellationToken token)
    {
        string repository = source == "atomgit" ? AtomGitRepository : $"https://github.com/{RepoOwner}/{RepoName}";
        Task<HttpResponseMessage> Send(string url, CancellationToken ct) => source == "atomgit"
            ? SendAtomGitGetAsync(url, ct) : _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        var tags = await MirrorGitTags.ReadAsync(Send, repository, token).ConfigureAwait(false);
        var candidates = tags.Where(t => Regex.IsMatch(t, @"^[vV]?[0-9]+\.[0-9]+\.[0-9]+(?:-[A-Za-z0-9.-]+)?(?:\+[A-Za-z0-9.-]+)?$"))
            .Select(tag => (tag, version: ParseReleaseVersionFromTag(tag))).Where(pair => pair.version != null)
            .OrderByDescending(pair => pair.version).GroupBy(pair => pair.version!.IsPrerelease);
        // Bound degraded-mode traffic. Stable and preview channels each get an independent search.
        var errors = new System.Collections.Concurrent.ConcurrentQueue<HttpRequestException>();
        var groups = await Task.WhenAll(candidates.Select(async group =>
        {
            var found = new List<ReleaseInfo>();
            foreach (var (tag, version) in group.Take(3))
            {
                try
                {
                    var release = await CheckTaggedReleaseAsync(repository, source, tag, version!, Send, token).ConfigureAwait(false);
                    if (release == null) continue;
                    found.Add(release);
                    break;
                }
                catch (HttpRequestException ex) when (!token.IsCancellationRequested)
                {
                    errors.Enqueue(ex);
                }
            }
            return found;
        })).ConfigureAwait(false);
        var results = groups.SelectMany(group => group).OrderByDescending(r => r.ComparableVersion).ToList();
        if (results.Count == 0 && errors.TryPeek(out var failure)) throw failure;
        if (results.Count == 0) throw new InvalidDataException("No published update packages with a checksum manifest.");
        return results;
    }

    private async Task<ReleaseInfo?> CheckTaggedReleaseAsync(string repository, string source, string tag, ReleaseVersion version,
        Func<string, CancellationToken, Task<HttpResponseMessage>> send, CancellationToken token)
    {
        string prefix = $"{repository}/releases/download/{Uri.EscapeDataString(tag)}/";
        string manifest = prefix + "SHA256SUMS.txt";
        using var response = await send(manifest, token).ConfigureAwait(false);
        if (response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Gone ||
            response.Content.Headers.ContentType?.MediaType == "text/html") return null;
        response.EnsureSuccessStatusCode();
        string text = await ReadChecksumsAsync(response.Content, token).ConfigureAwait(false);
        var release = new ReleaseInfo
        {
            TagName = tag, Source = source, ComparableVersion = version, ParsedVersion = version.CoreVersion,
            IsPrerelease = version.IsPrerelease, IsNewerVersion = version.CompareTo(GetCurrentReleaseVersion()) > 0,
            Title = tag, HtmlUrl = $"{repository}/releases/tag/{Uri.EscapeDataString(tag)}", ChecksumsAssetUrl = manifest
        };
        HttpRequestException? failure = null;
        foreach (bool standalone in new[] { false, true })
        {
            string file = $"StarPie-{tag}-{(standalone ? "Standalone" : "Lightweight")}-win-x64.zip";
            string? digest = FindChecksum(text, file);
            if (digest == null) continue;
            string url = prefix + file;
            try
            {
                if (!await IsZipAvailableAsync(source, url, token).ConfigureAwait(false)) continue;
            }
            catch (HttpRequestException ex) when (!token.IsCancellationRequested)
            {
                failure = ex;
                continue;
            }
            if (standalone) { release.StandaloneAssetUrl = url; release.StandaloneChecksum = digest; }
            else { release.LightweightAssetUrl = url; release.LightweightChecksum = digest; }
        }
        if (release.StandaloneAssetUrl != null || release.LightweightAssetUrl != null) return release;
        if (failure != null) throw failure;
        return null;
    }

    private async Task<bool> IsZipAvailableAsync(string source, string url, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 3);
        using var response = source == "atomgit" ? await SendAtomGitGetAsync(url, token, probeZip: true).ConfigureAwait(false)
            : await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Gone) return false;
        response.EnsureSuccessStatusCode();
        if (response.StatusCode == System.Net.HttpStatusCode.PartialContent &&
            response.Content.Headers.ContentRange is not { From: 0, To: 3, Length: > 4 }) return false;
        using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        byte[] prefix = new byte[4];
        return await stream.ReadAtLeastAsync(prefix, 4, false, token).ConfigureAwait(false) == 4 &&
            prefix.SequenceEqual(new byte[] { 0x50, 0x4b, 0x03, 0x04 });
    }

    private static async Task<string> ReadChecksumsAsync(HttpContent content, CancellationToken ct)
    {
        using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > 256 * 1024) throw new InvalidDataException("Checksum attachment is too large.");
            buffer.Write(chunk, 0, read);
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    internal static string ParseChecksum(string text, string fileName) =>
        FindChecksum(text, fileName) ?? throw new InvalidDataException("Package checksum is missing.");

    private static string? FindChecksum(string text, string fileName)
    {
        string? result = null;
        foreach (string line in text.TrimStart('\uFEFF').Split('\n'))
        {
            var match = Regex.Match(line.TrimEnd('\r'), @"^([a-fA-F0-9]{64})[ \t]+\*?(.+)$");
            if (match.Success && string.Equals(match.Groups[2].Value, fileName, StringComparison.OrdinalIgnoreCase))
            {
                if (result != null) throw new InvalidDataException("Duplicate package checksums.");
                result = match.Groups[1].Value;
            }
        }
        return result;
    }
}
