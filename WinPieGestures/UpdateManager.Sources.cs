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
        string.Equals(source, "atomgit", StringComparison.OrdinalIgnoreCase) ? "atomgit" : "github";

    public static string GetReleasePageUrl(ReleaseInfo? release, string? source)
    {
        if (NormalizeReleaseSource(source) == "atomgit") return AtomGitRepository + "/releases";
        return release?.Source == "github" && Uri.TryCreate(release.HtmlUrl, UriKind.Absolute, out var url) &&
            url.Scheme == Uri.UriSchemeHttps && url.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            ? url.AbsoluteUri : "https://github.com/SoftBlack42/StarPie/releases";
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

    private async Task<HttpResponseMessage> SendAtomGitGetAsync(string value, CancellationToken ct)
    {
        var url = new Uri(value);
        for (int redirects = 0; redirects <= 5; redirects++)
        {
            if (!IsAtomGitAssetUrl(url.AbsoluteUri)) throw new InvalidDataException("Untrusted AtomGit redirect destination.");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            var response = await _atomGitHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308)) return response;
            var location = response.Headers.Location;
            response.Dispose();
            if (location == null) throw new InvalidDataException("AtomGit redirect is missing its destination.");
            url = new Uri(url, location);
        }
        throw new InvalidDataException("AtomGit redirect limit exceeded.");
    }

    private async Task<List<ReleaseInfo>> FetchAtomGitReleasesAsync(CancellationToken ct)
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
        string source = NormalizeReleaseSource(releaseSource);
        if (release.Source != source) throw new InvalidOperationException("Release belongs to a different update source.");
        string? rawUrl = standalone ? release.StandaloneAssetUrl : release.LightweightAssetUrl;
        if (string.IsNullOrEmpty(rawUrl)) throw new InvalidDataException("Selected release package is not available.");
        string? expectedHash = null;
        if (source == "atomgit")
        {
            if (!IsAtomGitAssetUrl(rawUrl) || !IsAtomGitAssetUrl(release.ChecksumsAssetUrl))
                throw new InvalidDataException("AtomGit package/checksum attachment is not ready.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var response = await SendAtomGitGetAsync(release.ChecksumsAssetUrl!, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[4096];
            int read;
            while ((read = await stream.ReadAsync(chunk, timeout.Token).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > 256 * 1024) throw new InvalidDataException("Checksum attachment is too large.");
                buffer.Write(chunk, 0, read);
            }
            string fileName = $"StarPie-{release.TagName}-{(standalone ? "Standalone" : "Lightweight")}-win-x64.zip";
            expectedHash = ParseChecksum(Encoding.UTF8.GetString(buffer.ToArray()), fileName);
        }
        string downloadUrl = source == "atomgit" ? rawUrl : GetProxiedDownloadUrl(rawUrl, proxySource, customProxy);
        await DownloadAssetAsync(downloadUrl, destinationPath, progress, ct, source, expectedHash).ConfigureAwait(false);
    }

    internal static string ParseChecksum(string text, string fileName)
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
        return result ?? throw new InvalidDataException("Package checksum is missing.");
    }
}
