using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WinPieGestures;
using WinPieGestures.Plugins;

internal static class Program
{
    private static int checks;
    private static void Check(bool ok, string label)
    {
        if (!ok) throw new InvalidOperationException(label);
        checks++;
    }

    private sealed class Handler(Func<Uri, CancellationToken, Task<HttpResponseMessage>> reply) : HttpMessageHandler
    {
        internal ConcurrentQueue<Uri> Visits { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Visits.Enqueue(request.RequestUri!);
            return reply(request.RequestUri!, token);
        }
    }
    private static HttpResponseMessage Json(object value) =>
        new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };

    private static object HostRelease(string tag, bool mirror)
    {
        string baseUrl = mirror ? "https://atomgit.com/release/" : "https://github.com/Star-Pie/StarPie/releases/download/" + tag + "/";
        return new
        {
            tag_name = tag,
            assets = new[] { $"StarPie-{tag}-Standalone-win-x64.zip", $"StarPie-{tag}-Lightweight-win-x64.zip", "SHA256SUMS.txt" }
                .Select(name => new { name, browser_download_url = baseUrl + name, size = 9 })
        };
    }

    private static async Task TestUpdates(string directory)
    {
        var handler = new Handler(async (url, token) =>
        {
            if (url.Host == "api.atomgit.com") return Json(new[] { HostRelease("v2.0.0", true) });
            await Task.Delay(80, token);
            return Json(new[] { HostRelease("v2.1.0", false), HostRelease("v2.0.0", false) });
        });
        using var client = new HttpClient(handler);
        var manager = new UpdateManager(client);
        Check(UpdateManager.NormalizeReleaseSource(null) == "atomgit" && UpdateManager.NormalizeReleaseSource("unknown") == "atomgit", "old update default");
        var fixedResult = await manager.FetchAllReleasesAsync(releaseSource: "atomgit");
        Check(handler.Visits.Count == 1 && handler.Visits.Single().Host == "api.atomgit.com", "fixed release source never probes");
        var merged = await manager.FetchAllReleasesAsync(releaseSource: "auto");
        Check(merged.Select(r => (r.TagName, r.Source)).SequenceEqual(new[] { ("v2.1.0","github"), ("v2.0.0","atomgit") }), "newer version survives stale faster mirror, ties prefer speed");
        Check(UpdateManager.AcceptsReleaseSource("auto", merged[0].Source) && !UpdateManager.AcceptsReleaseSource("auto","auto") &&
            !UpdateManager.AcceptsReleaseSource("atomgit", "github"), "concrete source binding");
        var invalid = new Handler((url, _) => Task.FromResult(Json(url.Host == "api.atomgit.com" ? Array.Empty<object>() : new[] { new { tag_name = "v5.0.0" } })));
        using var badClient = new HttpClient(invalid);
        var bad = new UpdateManager(badClient);
        Check((await bad.ProbeReleaseSourcesAsync(default)).All(r => r.Milliseconds == null), "empty release and missing assets are unavailable");
        Check((await bad.FetchAllReleasesAsync(releaseSource:"auto")).Count == 0, "both unavailable cannot synthesize update");
        var split = new Handler(async (url, token) =>
        {
            if (url.AbsolutePath.EndsWith("SHA256SUMS.txt"))
                return new(HttpStatusCode.OK) { Content = new StringContent(Convert.ToHexString(SHA256.HashData(Payload)) + "  StarPie-v2.1.0-Standalone-win-x64.zip") };
            if (url.AbsolutePath.EndsWith(".zip")) return new(HttpStatusCode.OK) { Content = new ByteArrayContent(Payload) };
            if (url.Host == "api.atomgit.com")
                return Json(new[] { new { tag_name = "v2.1.0", assets = new[] {"StarPie-v2.1.0-Standalone-win-x64.zip","SHA256SUMS.txt"}
                    .Select(name => new { name, browser_download_url = "https://atomgit.com/release/" + name }) } });
            await Task.Delay(40, token);
            return Json(new object[] { new { tag_name = new { malformed = true } }, new { tag_name = "v2.1.0", assets = new[]
                { new { name = "StarPie-v2.1.0-Lightweight-win-x64.zip", browser_download_url = "https://github.com/Star-Pie/StarPie/releases/download/v2.1.0/lite.zip" } } } });
        });
        using var splitClient = new HttpClient(split);
        var splitManager = new UpdateManager(splitClient);
        var release = (await splitManager.FetchAllReleasesAsync(releaseSource: "auto")).Single();
        Check(release.ForPackage(true).Source == "atomgit" && release.ForPackage(false).Source == "github", "split package metadata survives malformed sibling");
        var start = split.Visits.Count;
        foreach (bool standalone in new[] {true, false})
        {
            string file = Path.Combine(directory, standalone ? "standalone.zip" : "lite.zip");
            await splitManager.DownloadReleaseAssetAsync(release, standalone, file, null, default, "direct", releaseSource: "auto");
            Check(File.ReadAllBytes(file).SequenceEqual(Payload), "split package reaches its concrete provider and verification");
        }
        Check(split.Visits.Skip(start).Select(u => u.Host).SequenceEqual(new[] { "atomgit.com","atomgit.com","github.com" }), "AtomGit hash and package stay paired; GitHub owns other package");
        using var cts = new CancellationTokenSource();
        var blocking = new Handler(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return Json(null!); });
        using var blockedClient = new HttpClient(blocking);
        var pending = new UpdateManager(blockedClient).ProbeReleaseSourcesAsync(cts.Token);
        cts.Cancel();
        try { await pending; throw new InvalidOperationException("caller cancellation swallowed"); }
        catch (OperationCanceledException) { checks++; }
        var timeouts = await MirrorSourceProbe.CompareAsync("fast", "late", async (source, token) =>
        {
            if (source == "late") await Task.Delay(Timeout.Infinite, token);
            return source;
        }, default, TimeSpan.FromMilliseconds(80));
        Check(timeouts[0].Value == "fast" && timeouts[1].Measurement.Milliseconds == null, "timeout retains available provider");
    }

    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("signed-off fixture");
    private const string Repo = OfficialPluginClient.RepositoryUrl;
    private const string Mirror = "https://atomgit.com/Star-Pie/StarPie-Official-Plugins/";
    private static OfficialPluginCatalog Catalog(bool mirror) => new()
    {
        SchemaVersion = 2, ReleaseTag = "sample", GeneratedAt = DateTimeOffset.UnixEpoch.AddDays(mirror ? 2 : 1),
        Modules = new() { new() { Id = "starpie.fixture", Versions = new() { new()
        {
            Name = "Sample", Version = "1.0.0", MinHostVersion = "1.7.0", ApiVersion = "1.0", TargetFramework = "net8.0-windows",
            ReleaseTag = "sample", AssetName = "sample.spkg", Size = Payload.Length, Sha256 = Convert.ToHexString(SHA256.HashData(Payload)),
            PackageUrl = Repo + "/releases/download/sample/sample.spkg"
        } } } }
    };
    private static object MirrorRelease() => new
    {
        tag_name = "sample", body = "<!-- starpie-source-published-at: 2026-10-01T00:00:00.000Z -->",
        assets = new[] {"module-catalog.json","sample.spkg"}.Select(name => new { name, browser_download_url = Mirror + name })
    };

    private static async Task TestPlugins(string directory)
    {
        PluginPaths.OverrideRootsForTesting(directory, Path.Combine(directory, "candidates"));
        var auto = new OfficialPluginDownloadOptions("auto");
        bool fail = false, missingMirror = false, corruptMirror = false, mismatchDirect = false;
        var handler = new Handler((url, _) =>
        {
            HttpResponseMessage result;
            if (fail || url.Host is "ghfast.top" or "gh-proxy.com" or "mirror.ghproxy.com") result = new(HttpStatusCode.ServiceUnavailable);
            else if (url.AbsolutePath.EndsWith("releases.atom"))
                result = new(HttpStatusCode.OK) { Content = new StringContent($"<feed xmlns='http://www.w3.org/2005/Atom'><entry><link href='{Repo}/releases/tag/sample'/></entry></feed>") };
            else if (url.AbsolutePath.EndsWith("module-catalog.json"))
            {
                var catalog = Catalog(url.Host == "atomgit.com");
                if (url.Host == "github.com" && mismatchDirect) catalog.Modules[0].Versions[0].Sha256 = new string('0',64);
                result = Json(catalog);
            }
            else if (url.AbsolutePath.EndsWith("tags/sample") && missingMirror) result = new(HttpStatusCode.NotFound);
            else if (url.Host == "atomgit.com" && url.AbsolutePath.EndsWith("sample.spkg") && corruptMirror)
                result = new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[Payload.Length]) };
            else if (url.AbsolutePath.EndsWith("sample.spkg")) result = new(HttpStatusCode.OK) { Content = new ByteArrayContent(Payload) };
            else if (url.Host == "api.atomgit.com") result = url.AbsolutePath.EndsWith("tags/sample") ? Json(MirrorRelease()) : Json(new[] { MirrorRelease() });
            else if (url.Host == "api.github.com") result = Json(new[] { new { tag_name = "sample", html_url = Repo + "/releases/tag/sample", published_at = "2026-01-01T00:00:00Z" } });
            else throw new InvalidOperationException("Unexpected request " + url);
            return Task.FromResult(result);
        });
        OfficialPluginClient.HttpHandlerOverrideForTesting = handler;
        Check(OfficialPluginDownloadOptions.NormalizeChannel(null) == "direct" && OfficialPluginDownloadOptions.NormalizeChannel("moeyy") == "ghfast", "legacy plugin defaults and aliases");
        await OfficialPluginClient.FetchCatalogAsync(new OfficialPluginDownloadOptions("atomgit"));
        Check(handler.Visits.All(u => u.Host.EndsWith("atomgit.com")), "fixed catalog source stays fixed");
        byte[] original = File.ReadAllBytes(PluginPaths.OfficialCatalogCacheFile);
        DateTime time = File.GetLastWriteTimeUtc(PluginPaths.OfficialCatalogCacheFile);
        var probes = await OfficialPluginClient.ProbeCatalogSourcesAsync(default);
        Check(probes.Length == 5 && probes.Count(r => r.Milliseconds.HasValue) == 2 && File.GetLastWriteTimeUtc(PluginPaths.OfficialCatalogCacheFile) == time &&
            original.SequenceEqual(File.ReadAllBytes(PluginPaths.OfficialCatalogCacheFile)), "manual plugin measurement leaves cache untouched");
        var chosen = await OfficialPluginClient.FetchCatalogAsync(auto);
        var module = chosen.Modules.Single().Versions.Single();
        Check(module.PreferredCatalogSource == "atomgit", "fresher compatible catalog wins");
        int count = handler.Visits.Count;
        string destination = Path.Combine(directory,"sample.spkg");
        await OfficialPluginClient.SaveOfflinePackageAsync(module, destination, auto);
        Check(handler.Visits.Skip(count).Select(u => u.Host).SequenceEqual(new[] { "api.atomgit.com","atomgit.com" }) &&
            File.ReadAllBytes(destination).SequenceEqual(Payload), "auto download remains pinned to catalog origin");
        missingMirror = true;
        count = handler.Visits.Count;
        await OfficialPluginClient.SaveOfflinePackageAsync(module, destination, auto);
        Check(handler.Visits.Skip(count).Last().Host == "github.com" && File.ReadAllBytes(destination).SequenceEqual(Payload), "absent historical tag falls back to identical verified package");
        count = handler.Visits.Count;
        try { await OfficialPluginClient.SaveOfflinePackageAsync(module, destination, new("atomgit")); throw new InvalidOperationException("fixed source changed"); }
        catch (HttpRequestException) { checks++; }
        Check(handler.Visits.Skip(count).All(u => u.Host.EndsWith("atomgit.com")), "fixed missing package stays fixed");
        mismatchDirect = true;
        count = handler.Visits.Count;
        try { await OfficialPluginClient.SaveOfflinePackageAsync(module, destination, auto); throw new InvalidOperationException("alternate hash ignored"); }
        catch (HttpRequestException) { checks++; }
        Check(!handler.Visits.Skip(count).Any(u => u.AbsolutePath.EndsWith(".spkg")), "inconsistent alternative metadata prevents any package download");
        missingMirror = false; mismatchDirect = false; corruptMirror = true;
        count = handler.Visits.Count;
        try { await OfficialPluginClient.SaveOfflinePackageAsync(module, destination, auto); throw new InvalidOperationException("hash failure masked"); }
        catch (InvalidDataException) { checks++; }
        Check(handler.Visits.Skip(count).All(u => u.Host.EndsWith("atomgit.com")) && File.ReadAllBytes(destination).SequenceEqual(Payload), "integrity failure surfaced without fallback or overwrite");
        corruptMirror = false;
        Check(OfficialPluginClient.TryLoadCachedCatalog(out var cached, out _, out _), "cache readable");
        var cachedModule = cached.Modules.Single().Versions.Single();
        cachedModule.Sha256 = new string('0', 64);
        count = handler.Visits.Count;
        try { await OfficialPluginClient.SaveOfflinePackageAsync(cachedModule, destination, auto); throw new InvalidOperationException("wrong package accepted"); }
        catch (HttpRequestException) { checks++; }
        Check(handler.Visits.Skip(count).All(u => !u.AbsolutePath.EndsWith(".spkg")) && File.ReadAllBytes(destination).SequenceEqual(Payload), "unmatched cached package cannot overwrite destination");
        fail = true;
        original = File.ReadAllBytes(PluginPaths.OfficialCatalogCacheFile);
        try { await OfficialPluginClient.FetchCatalogAsync(auto); throw new InvalidOperationException("offline refresh succeeded"); }
        catch (HttpRequestException) { checks++; }
        Check(File.ReadAllBytes(PluginPaths.OfficialCatalogCacheFile).SequenceEqual(original), "failed automatic refresh preserves usable cache");
    }

    private static HttpResponseMessage TagAdvertisement(string tag)
    {
        string line = new string('a', 40) + " refs/tags/" + tag + "\n";
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent((line.Length + 4).ToString("x4") + line + "0000") };
        response.Content.Headers.ContentType = new("application/x-git-upload-pack-advertisement");
        return response;
    }

    private static async Task TestFivePluginSources(string directory)
    {
        const string tag = "modules-2026.11.04.1";
        var routeHosts = new Dictionary<string,string>
        {
            ["ghfast"] = "ghfast.top", ["gh-proxy"] = "gh-proxy.com", ["mirror"] = "mirror.ghproxy.com"
        };
        var catalog = Catalog(false);
        catalog.ReleaseTag = tag;
        var item = catalog.Modules[0].Versions[0];
        item.ReleaseTag = tag;
        item.PackageUrl = Repo + "/releases/download/" + tag + "/sample.spkg";
        foreach (var (channel, host) in routeHosts)
        {
            var handler = new Handler((url, _) =>
            {
                if (url.Host != host) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
                if (!url.AbsolutePath.StartsWith("/https://github.com/Star-Pie/StarPie-Official-Plugins", StringComparison.Ordinal))
                    throw new InvalidOperationException("proxy requested unrelated upstream " + url);
                if (url.AbsolutePath.EndsWith("info/refs")) return Task.FromResult(TagAdvertisement(tag));
                if (url.AbsolutePath.EndsWith("releases.atom")) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StringContent($"<feed xmlns='http://www.w3.org/2005/Atom'><entry><link href='{Repo}/releases/tag/{tag}'/></entry></feed>") });
                if (url.AbsolutePath.EndsWith("module-catalog.json")) return Task.FromResult(Json(catalog));
                if (url.AbsolutePath.EndsWith(".spkg")) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Payload) });
                throw new InvalidOperationException("unexpected request " + url);
            });
            OfficialPluginClient.HttpHandlerOverrideForTesting = handler;
            var fixedOptions = new OfficialPluginDownloadOptions(channel);
            var fetched = await OfficialPluginClient.FetchCatalogAsync(fixedOptions);
            string output = Path.Combine(directory, channel + ".spkg");
            await OfficialPluginClient.SaveOfflinePackageAsync(fetched.Modules[0].Versions[0], output, fixedOptions);
            Check(handler.Visits.All(uri => uri.Host == host) && File.ReadAllBytes(output).SequenceEqual(Payload),
                channel + " independently discovers catalog and downloads verified package");
            var automatic = new OfficialPluginDownloadOptions("auto");
            fetched = await OfficialPluginClient.FetchCatalogAsync(automatic);
            var resolved = fetched.Modules[0].Versions[0];
            Check(resolved.PreferredCatalogSource == channel, channel + " can independently win automatic selection");
            await OfficialPluginClient.SaveOfflinePackageAsync(resolved, output, automatic);
            Check(handler.Visits.Last().Host == host && File.ReadAllBytes(output).SequenceEqual(Payload), channel + " serves auto-selected asset");
        }

        byte[] zip = {0x50, 0x4B, 0x03, 0x04, 1, 2, 3, 4};
        item.Size = zip.Length;
        item.Sha256 = Convert.ToHexString(SHA256.HashData(zip));
        bool html = false;
        var publicMirror = new Handler((url, _) =>
        {
            if (url.Host == "api.atomgit.com") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
            if (url.Host != "atomgit.com") throw new InvalidOperationException("mirror used unexpected host " + url);
            if (url.AbsolutePath.EndsWith("info/refs")) return Task.FromResult(TagAdvertisement(tag));
            if (url.AbsolutePath.EndsWith("module-catalog.json")) return Task.FromResult(Json(catalog));
            if (url.AbsolutePath.EndsWith(".spkg")) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new ByteArrayContent(html ? Encoding.UTF8.GetBytes("<html />") : zip) });
            throw new InvalidOperationException("unexpected request " + url);
        });
        OfficialPluginClient.HttpHandlerOverrideForTesting = publicMirror;
        var mirrorOptions = new OfficialPluginDownloadOptions("atomgit");
        var available = await OfficialPluginClient.FetchCatalogAsync(mirrorOptions);
        string file = Path.Combine(directory, "public.spkg");
        await OfficialPluginClient.SaveOfflinePackageAsync(available.Modules[0].Versions[0],file,mirrorOptions);
        Check(File.ReadAllBytes(file).SequenceEqual(zip) && publicMirror.Visits.Count(u => u.Host == "api.atomgit.com") == 1,
            "rate-limited mirror uses public Git refs and verified release URLs without repeating failing API");
        byte[] original = File.ReadAllBytes(PluginPaths.OfficialCatalogCacheFile);
        html = true;
        try { await OfficialPluginClient.FetchCatalogAsync(mirrorOptions); throw new InvalidOperationException("HTML accepted"); }
        catch (InvalidDataException) { checks++; }
        Check(File.ReadAllBytes(PluginPaths.OfficialCatalogCacheFile).SequenceEqual(original), "HTML masquerading as an archive cannot replace a cached catalog");
    }

    private sealed class ProgressingStream(byte[] data) : MemoryStream(data)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(20, cancellationToken);
            return await base.ReadAsync(buffer[..Math.Min(2, buffer.Length)], cancellationToken);
        }
    }

    private static async Task TestSourceTimeouts(string directory)
    {
        const string tag = "modules-2026.11.04.1";
        var catalog = Catalog(true);
        catalog.ReleaseTag = tag;
        var module = catalog.Modules[0].Versions[0];
        module.ReleaseTag = tag;
        module.Id = catalog.Modules[0].Id;
        module.PackageUrl = Repo + "/releases/download/" + tag + "/sample.spkg";
        byte[] archive = { 0x50, 0x4B, 0x03, 0x04, 1, 2, 3, 4 };
        module.Size = archive.Length;
        module.Sha256 = Convert.ToHexString(SHA256.HashData(archive));
        bool transportError = false;
        var apiStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mirror = new Handler(async (url, token) =>
        {
            if (url.Host == "api.atomgit.com")
            {
                apiStarted.TrySetResult();
                if (transportError) throw new HttpRequestException("API connection failed");
                await Task.Delay(Timeout.Infinite, token);
            }
            if (url.Host != "atomgit.com") throw new InvalidOperationException("fixed source leaked to " + url.Host);
            if (url.AbsolutePath.EndsWith("info/refs")) return TagAdvertisement(tag);
            if (url.AbsolutePath.EndsWith("module-catalog.json")) return Json(catalog);
            if (url.AbsolutePath.EndsWith("sample.spkg")) return new(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) };
            throw new InvalidOperationException("unexpected request " + url);
        });
        OfficialPluginClient.HttpHandlerOverrideForTesting = mirror;
        OfficialPluginClient.ApiAttemptTimeoutOverrideForTesting = TimeSpan.FromMilliseconds(100);
        OfficialPluginClient.ResponseHeaderTimeoutOverrideForTesting = TimeSpan.FromMilliseconds(30);
        try
        {
            var fixedMirror = new OfficialPluginDownloadOptions("atomgit");
            var recovered = await OfficialPluginClient.FetchCatalogAsync(fixedMirror);
            Check(recovered.ReleaseTag == tag && mirror.Visits.Any(u => u.AbsolutePath.EndsWith("info/refs")), "fixed mirror recovers from stalled list API within its outer budget");
            string output = Path.Combine(directory, "recovered.spkg");
            await OfficialPluginClient.SaveOfflinePackageAsync(module, output, fixedMirror);
            Check(File.ReadAllBytes(output).SequenceEqual(archive) && mirror.Visits.Last().Host == "atomgit.com",
                "cached fixed package recovers from stalled tag API");
            transportError = true;
            Check((await OfficialPluginClient.FetchCatalogAsync(fixedMirror)).ReleaseTag == tag, "transport failure also reaches fixed mirror public route");
            transportError = false;
            apiStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using var caller = new CancellationTokenSource();
            int before = mirror.Visits.Count;
            var pending = OfficialPluginClient.FetchCatalogAsync(fixedMirror, caller.Token);
            await apiStarted.Task;
            caller.Cancel();
            try { await pending; throw new InvalidOperationException("caller cancellation swallowed"); }
            catch (OperationCanceledException) { checks++; }
            Check(mirror.Visits.Count == before + 1, "caller cancellation never starts fallback");

            module.PreferredCatalogSource = "direct";
            bool progressing = false;
            var packages = new Handler(async (url, token) =>
            {
                if (url.Host == "github.com" && url.AbsolutePath.EndsWith("sample.spkg"))
                {
                    if (!progressing) await Task.Delay(Timeout.Infinite, token);
                    return new(HttpStatusCode.OK) { Content = new StreamContent(new ProgressingStream(archive)) };
                }
                if (url.Host != "gh-proxy.com") return new(HttpStatusCode.Unauthorized);
                if (url.AbsolutePath.EndsWith("releases.atom")) return new(HttpStatusCode.OK)
                    { Content = new StringContent($"<feed xmlns='http://www.w3.org/2005/Atom'><entry><link href='{Repo}/releases/tag/{tag}'/></entry></feed>") };
                if (url.AbsolutePath.EndsWith("module-catalog.json")) return Json(catalog);
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) };
            });
            OfficialPluginClient.HttpHandlerOverrideForTesting = packages;
            await OfficialPluginClient.SaveOfflinePackageAsync(module, output, new("auto"));
            Check(packages.Visits.First().Host == "github.com" && packages.Visits.Last().Host == "gh-proxy.com" &&
                File.ReadAllBytes(output).SequenceEqual(archive), "stalled preferred package headers leave time for a verified alternate");
            progressing = true;
            before = packages.Visits.Count;
            await OfficialPluginClient.SaveOfflinePackageAsync(module, output, new("auto"));
            Check(packages.Visits.Count == before + 1 && File.ReadAllBytes(output).SequenceEqual(archive),
                "a progressing transfer outlives its header timeout without switching");
        }
        finally
        {
            OfficialPluginClient.ApiAttemptTimeoutOverrideForTesting = null;
            OfficialPluginClient.ResponseHeaderTimeoutOverrideForTesting = null;
        }
    }

    private static async Task Main(string[] args)
    {
        string directory = Path.Combine(Path.GetTempPath(),"StarPie-SourcePreference-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Environment.SetEnvironmentVariable("APPDATA", directory);
        Environment.SetEnvironmentVariable("LOCALAPPDATA", directory);
        try
        {
            if (args is ["--live", var channel] && channel is "atomgit" or "direct" or "ghfast" or "gh-proxy" or "mirror" or "auto")
            {
                PluginPaths.OverrideRootsForTesting(directory, Path.Combine(directory, "candidates"));
                using var trace = new LiveHttpTrace();
                var options = new OfficialPluginDownloadOptions(channel);
                var catalog = await OfficialPluginClient.FetchCatalogAsync(options);
                var module = catalog.Modules.SelectMany(entry => entry.Versions.Where(m => m.ReleaseTag == catalog.ReleaseTag))
                    .Where(m => OfficialPluginVersionSelector.Evaluate(m).IsCompatible).OrderBy(m => m.Size).FirstOrDefault()
                    ?? catalog.Modules.SelectMany(e => e.Versions).Where(m => OfficialPluginVersionSelector.Evaluate(m).IsCompatible).OrderBy(m => m.Size).First();
                Console.WriteLine($"catalog {catalog.ReleaseTag}, {catalog.Modules.Count} modules, chosen {module.PreferredCatalogSource ?? channel}");
                string output = Path.Combine(directory, "test.spkg");
                await OfficialPluginClient.SaveOfflinePackageAsync(module, output, options);
                using var stream = File.OpenRead(output);
                string sha = Convert.ToHexString(SHA256.HashData(stream));
                Check(stream.Length == module.Size && sha.Equals(module.Sha256, StringComparison.OrdinalIgnoreCase), "live downloaded payload must match selected catalog");
                Console.WriteLine($"PASS {channel}: {module.Id} {module.Version} ({stream.Length} bytes, SHA256 {sha})");
                return;
            }
            await TestUpdates(directory);
            await ReleaseIndexFallbackTests.RunAsync(directory);
            await TestPlugins(directory);
            await TestFivePluginSources(directory);
            await TestSourceTimeouts(directory);
            Console.WriteLine($"PASS {checks} source selection checks, injected HTTP only");
        }
        finally
        {
            OfficialPluginClient.HttpHandlerOverrideForTesting = null;
            AppLogger.Shutdown();
            Directory.Delete(directory, true);
        }
    }

    private sealed class LiveHttpTrace : System.Diagnostics.Tracing.EventListener
    {
        protected override void OnEventSourceCreated(System.Diagnostics.Tracing.EventSource source)
        {
            if (source.Name == "System.Net.Http") EnableEvents(source, System.Diagnostics.Tracing.EventLevel.Informational);
        }
        protected override void OnEventWritten(System.Diagnostics.Tracing.EventWrittenEventArgs e)
        {
            if (e.EventName == "RequestStart" && e.Payload is { Count: >= 4 } fields)
                Console.WriteLine($"GET {fields[1]}{fields[3]?.ToString()?.Split('?')[0]}");
            else if (e.EventName == "RequestStop" && e.Payload is { Count: > 0 } result)
                Console.WriteLine($"HTTP {result[0]}");
        }
    }
}
