using System.IO;
using System.Net.Http;
using System.Text;

namespace WinPieGestures.Plugins;

internal static partial class OfficialPluginClient
{
    // Provisioning and release mirroring are separate deployment prerequisites.
    internal const string AtomGitRepositoryUrl = "https://atomgit.com/Star-Pie/StarPie-Official-Plugins";
    internal const string AtomGitApiUrl = "https://api.atomgit.com/api/v5/repos/Star-Pie/StarPie-Official-Plugins/releases";
    private static readonly HttpClient AtomGitHttp = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(45) };

    private static async Task<HttpResponseMessage> SendAtomGitAsync(string value, CancellationToken cancellationToken)
    {
        var uri = new Uri(value);
        for (int redirects = 0; redirects <= 5; redirects++)
        {
            if (!UpdateManager.IsAtomGitAssetUrl(uri.AbsoluteUri)) throw new InvalidDataException("Untrusted official plugin mirror URL.");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("StarPie/official-plugin-client");
            var client = HttpHandlerOverrideForTesting == null ? AtomGitHttp : GetHttpClient();
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308)) return response;
            var location = response.Headers.Location; response.Dispose();
            if (location == null) throw new InvalidDataException("Official plugin mirror redirect has no destination.");
            uri = new Uri(uri, location);
        }
        throw new InvalidDataException("Official plugin mirror redirect limit exceeded.");
    }
    private static async Task<string> ReadAtomGitJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await SendAtomGitAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(I18n.T("OfficialPluginAtomGitUnavailable") + $" (HTTP {(int)response.StatusCode})", null, response.StatusCode);
        if (response.Content.Headers.ContentLength > OfficialPluginMirrorReleaseParser.MaxInputBytes)
            throw new InvalidDataException("Official plugin mirror JSON is too large.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream(); var buffer = new byte[8192]; int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (output.Length + read > OfficialPluginMirrorReleaseParser.MaxInputBytes) throw new InvalidDataException("Official plugin mirror JSON is too large.");
            output.Write(buffer, 0, read);
        }
        return new UTF8Encoding(false, true).GetString(output.ToArray());
    }
    private static async Task<IReadOnlyList<OfficialPluginMirrorRelease>> FetchAtomGitPluginReleasesAsync(CancellationToken cancellationToken)
    {
        // Bound discovery while allowing historical backfills to be re-ordered
        // by source publication time before choosing the usual eight candidates.
        string json = await ReadAtomGitJsonAsync(AtomGitApiUrl + "?per_page=100&page=1", cancellationToken).ConfigureAwait(false);
        // An older partial mirror can expose all packages before its final
        // metadata PATCH. Require synchronization provenance before discovery.
        return OfficialPluginMirrorReleaseParser.Parse(json, UpdateManager.IsAtomGitAssetUrl)
            .Where(item => item.HasSourcePublicationTime).ToArray();
    }
    private static async Task<string> ResolveAtomGitPackageAsync(OfficialPluginModule module, CancellationToken cancellationToken)
    {
        string json = await ReadAtomGitJsonAsync(AtomGitApiUrl + "/tags/" + Uri.EscapeDataString(module.ReleaseTag), cancellationToken).ConfigureAwait(false);
        var releases = OfficialPluginMirrorReleaseParser.Parse("[" + json + "]", UpdateManager.IsAtomGitAssetUrl);
        var release = releases.SingleOrDefault(item => item.Tag == module.ReleaseTag);
        if (release == null || !release.Assets.TryGetValue(module.AssetName, out string? url))
            throw new InvalidDataException(I18n.T("OfficialPluginAtomGitUnavailable"));
        return url;
    }
}
