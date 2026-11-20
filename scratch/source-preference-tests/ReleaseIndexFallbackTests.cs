using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using WinPieGestures;

internal static class ReleaseIndexFallbackTests
{
    internal static async Task RunAsync(string directory)
    {
        var handler = new RateLimitedIndex();
        using var client = new HttpClient(handler);
        var manager = new UpdateManager(client);
        var result = await manager.ProbeReleaseSourcesAsync(default);
        Require(result.Length == 2 && result.All(m => m.Milliseconds.HasValue), "verified public releases survive API rate limits");

        var releases = await manager.FetchAllReleasesAsync(releaseSource: "atomgit");
        var release = releases.Single();
        Require(release.TagName == "v2.1.0" && release.StandaloneAssetUrl == null && release.LightweightAssetUrl != null,
            "unpublished tag, HTML landing page and unlisted package excluded");
        string file = Path.Combine(directory, "fallback.zip");
        await manager.DownloadReleaseAssetAsync(release, false, file, null, default, "direct", releaseSource: "atomgit");
        Require(File.ReadAllBytes(file).SequenceEqual(RateLimitedIndex.Package), "fixed-source download verified against real manifest entry");

        handler.Malformed = true;
        var invalid = await manager.ProbeReleaseSourcesAsync(default);
        Require(invalid.All(m => m.Milliseconds == null && m.Failure == "SourceProbeInvalid"), "malformed ref advertisement cannot report success");

        var statuses = await MirrorSourceProbe.CompareAsync<string>(new[] { "limit", "tls", "timeout" }, (src, _) =>
            throw src switch
            {
                "limit" => new HttpRequestException("limit", null, HttpStatusCode.TooManyRequests),
                "tls" => new HttpRequestException(HttpRequestError.SecureConnectionError, "TLS"),
                _ => (Exception)new TimeoutException()
            }, default);
        Require(statuses[0].Measurement.Status == 429 && statuses[1].Measurement.Failure == "SourceProbeTls" &&
            statuses[2].Measurement.Failure == "SourceProbeTimeout", "rate limiting, TLS and timeout distinguished");
        Console.WriteLine("PASS 5 host fallback and diagnostic checks");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class RateLimitedIndex : HttpMessageHandler
    {
        public bool Malformed { get; set; }
        public static readonly byte[] Package = new byte[] { 0x50, 0x4b, 0x03, 0x04, 0x81, 0x91 };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var url = request.RequestUri!;
            if (url.Host.StartsWith("api.", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(url.Host == "api.github.com" ? HttpStatusCode.Forbidden : HttpStatusCode.TooManyRequests));
            HttpContent content;
            if (url.AbsolutePath.EndsWith("info/refs"))
            {
                static string Packet(string tag)
                {
                    string line = new string('0', 40) + " refs/tags/" + tag + "\n";
                    return (4 + line.Length).ToString("x4") + line;
                }
                content = new StringContent(Malformed ? "000fbroken" : Packet("v2.3.0-beta.1") + Packet("v2.2.0") + Packet("v2.1.0") + "0000",
                    Encoding.UTF8, "application/x-git-upload-pack-advertisement");
            }
            else if (url.AbsolutePath.Contains("v2.3.0-beta.1"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            else if (url.AbsolutePath.EndsWith("-Standalone-win-x64.zip"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            else if (url.AbsolutePath.Contains("v2.2.0"))
                content = new StringContent("<html>release missing</html>", Encoding.UTF8, "text/html");
            else if (url.AbsolutePath.EndsWith("SHA256SUMS.txt"))
                content = new StringContent(Convert.ToHexString(SHA256.HashData(Package)) + "  StarPie-v2.1.0-Lightweight-win-x64.zip" + Environment.NewLine + new string('0', 64) + "  StarPie-v2.1.0-Standalone-win-x64.zip");
            else content = new ByteArrayContent(Package);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
