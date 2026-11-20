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

    private static Task<HttpResponseMessage> SendAtomGitAsync(string value, CancellationToken cancellationToken) =>
        SendAtomGitRequestAsync(value, false, cancellationToken);

    private static async Task<HttpResponseMessage> SendAtomGitRequestAsync(string value, bool probePackage, CancellationToken cancellationToken)
    {
        var uri = new Uri(value);
        for (int redirects = 0; redirects <= 5; redirects++)
        {
            if (!UpdateManager.IsAtomGitAssetUrl(uri.AbsoluteUri)) throw new InvalidDataException("Untrusted official plugin mirror URL.");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (probePackage) request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 3);
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
        return await ReadCatalogTextAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> ReadCatalogTextAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        response.EnsureSuccessStatusCode();
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
    internal static TimeSpan? ApiAttemptTimeoutOverrideForTesting { get; set; }

    private static async Task<string> ReadAtomGitApiJsonAsync(string url, CancellationToken token)
    {
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(token);
        attempt.CancelAfter(ApiAttemptTimeoutOverrideForTesting ?? TimeSpan.FromSeconds(4));
        try { return await ReadAtomGitJsonAsync(url, attempt.Token).ConfigureAwait(false); }
        catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException(I18n.T("OfficialPluginTimeoutAtom"), ex);
        }
    }

    private static async Task<IReadOnlyList<OfficialPluginMirrorRelease>> FetchAtomGitPluginReleasesAsync(CancellationToken cancellationToken)
    {
        // Bound discovery while allowing historical backfills to be re-ordered
        // by source publication time before choosing the usual eight candidates.
        string json = await ReadAtomGitApiJsonAsync(AtomGitApiUrl + "?per_page=100&page=1", cancellationToken).ConfigureAwait(false);
        // An older partial mirror can expose all packages before its final
        // metadata PATCH. Require synchronization provenance before discovery.
        return OfficialPluginMirrorReleaseParser.Parse(json, UpdateManager.IsAtomGitAssetUrl)
            .Where(item => item.HasSourcePublicationTime).ToArray();
    }
    private static string PublicAtomGitAssetUrl(string releaseTag, string assetName) =>
        $"{AtomGitRepositoryUrl}/releases/download/{Uri.EscapeDataString(releaseTag)}/{Uri.EscapeDataString(assetName)}";

    private static bool ApiUnavailable(Exception ex) =>
        ex is TimeoutException || ex is HttpRequestException http &&
            (http.StatusCode is null or System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.TooManyRequests
                or System.Net.HttpStatusCode.RequestTimeout || (int?)http.StatusCode >= 500);

    // The public release URL can return an HTML page with HTTP 200. Check the package
    // header and its advertised full length before caching a mirror catalog without API provenance.
    private static async Task<bool> IsPublicArchiveAvailableAsync(OfficialPluginModule module, CancellationToken token)
    {
        using var response = await SendAtomGitRequestAsync(PublicAtomGitAssetUrl(module.ReleaseTag, module.AssetName), true, token).ConfigureAwait(false);
        if (response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Gone) return false;
        response.EnsureSuccessStatusCode();
        var range = response.Content.Headers.ContentRange;
        bool correctLength = response.StatusCode == System.Net.HttpStatusCode.PartialContent
            ? range?.From == 0 && range.To == 3 && range.Length == module.Size
            : response.StatusCode == System.Net.HttpStatusCode.OK && response.Content.Headers.ContentLength == module.Size;
        if (!correctLength) return false;
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        byte[] prefix = new byte[4];
        return await stream.ReadAtLeastAsync(prefix, 4, false, token).ConfigureAwait(false) == prefix.Length &&
            prefix.SequenceEqual(new byte[] {0x50, 0x4B, 0x03, 0x04});
    }

    private static async Task<string> ResolveAtomGitPackageAsync(OfficialPluginModule module, CancellationToken cancellationToken)
    {
        if (module.UsePublicAtomGitAssets) return PublicAtomGitAssetUrl(module.ReleaseTag, module.AssetName);
        string json;
        try { json = await ReadAtomGitApiJsonAsync(AtomGitApiUrl + "/tags/" + Uri.EscapeDataString(module.ReleaseTag), cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && ApiUnavailable(ex)) { return PublicAtomGitAssetUrl(module.ReleaseTag, module.AssetName); }
        var releases = OfficialPluginMirrorReleaseParser.Parse("[" + json + "]", UpdateManager.IsAtomGitAssetUrl);
        var release = releases.SingleOrDefault(item => item.Tag == module.ReleaseTag);
        if (release == null || !release.Assets.TryGetValue(module.AssetName, out string? url))
            throw new HttpRequestException(I18n.T("OfficialPluginAtomGitUnavailable"), null, System.Net.HttpStatusCode.NotFound);
        return url;
    }
}
