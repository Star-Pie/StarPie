using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using WinPieGestures;
using WinPieGestures.Plugins;

internal static partial class Program
{
    private static void MirrorSources(string sandbox)
    {
        var defaults = JsonSerializer.Deserialize<AppConfig>("{}")!;
        Check(defaults.UpdateReleaseSource == "atomgit" && defaults.OfficialPluginDownloadSource == "atomgit", "missing and new config defaults select AtomGit for both source types");
        defaults.UpdateReleaseSource = "github"; defaults.OfficialPluginDownloadSource = "direct"; defaults.UpdateProxySource = "gh-proxy";
        var restored = JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(defaults))!;
        Check(restored.UpdateReleaseSource == "github" && restored.OfficialPluginDownloadSource == "direct" && restored.UpdateProxySource == "gh-proxy", "explicit choices roundtrip independently of GitHub proxy");
        Check(UpdateManager.NormalizeReleaseSource(null) == "atomgit" && UpdateManager.NormalizeReleaseSource(" github ") == "github", "system source normalization has the requested default and retains explicit GitHub");
        Check(new OfficialPluginDownloadOptions("atomgit").GetChannelDisplayName() == I18n.T("UpdateSourceAtomGit"), "plugin source uses the same localized AtomGit label");
        using var fake = new MirrorHttpFixture(); OfficialPluginClient.HttpHandlerOverrideForTesting = fake;
        var prior = ConfigManager.CurrentConfig; ConfigManager.CurrentConfig = new AppConfig();
        try
        {
            string before = JsonSerializer.Serialize(ConfigManager.CurrentConfig);
            var dialog = new OfficialPluginsOnboardingDialog(); Layout(dialog, 800, 700);
            var channels = Descendants(dialog.Content!).OfType<ComboBox>().Single(item => item.Name == "ChannelComboBox");
            Check(channels.SelectedItem is ComboBoxItem { Tag: "atomgit" } && fake.Requests.Count == 0, "first-run dialog defaults to AtomGit with zero network before consent");
            Check(JsonSerializer.Serialize(ConfigManager.CurrentConfig) == before, "opening first-run dialog does not persist source or other config"); dialog.Close();
            Drain(MirrorDownloads(fake, sandbox));
        }
        finally
        {
            ConfigManager.CurrentConfig = prior; OfficialPluginClient.HttpHandlerOverrideForTesting = null;
            OfficialPluginClient.DownloadTimeoutOverrideForTesting = null; OfficialPluginClient.CatalogTimeoutOverrideForTesting = null;
        }
    }
    private static async Task MirrorDownloads(MirrorHttpFixture fake, string sandbox)
    {
        var options = new OfficialPluginDownloadOptions("atomgit");
        var catalog = await OfficialPluginClient.FetchCatalogAsync(options);
        Check(catalog.ReleaseTag == fake.Module.ReleaseTag && catalog.Modules[0].Versions[0].PackageUrl.StartsWith("https://github.com/", StringComparison.Ordinal), "mirror catalog validates without changing canonical package identity");
        string destination = Path.Combine(sandbox, "mirror-download.spkg");
        await OfficialPluginClient.SaveOfflinePackageAsync(fake.Module, destination, options);
        Check(File.ReadAllBytes(destination).SequenceEqual(fake.Bytes) && fake.Requests.All(uri => UpdateManager.IsAtomGitAssetUrl(uri.AbsoluteUri)), "catalog and package transport stay on the selected mirror and preserve exact bytes");
        Check(fake.Requests.Any(uri => uri.AbsolutePath.Contains("/tags/", StringComparison.Ordinal)), "package resolution binds the original release tag and exact asset name");
        foreach (string mode in new[] { "missing", "tampered", "wrong-tag", "unsafe-redirect", "unavailable" })
        {
            fake.Mode = mode; byte[] prior = File.ReadAllBytes(destination); bool rejected = false;
            try { await OfficialPluginClient.SaveOfflinePackageAsync(fake.Module, destination, options); }
            catch (Exception ex) when (ex is InvalidDataException or HttpRequestException) { rejected = true; }
            Check(rejected && File.ReadAllBytes(destination).SequenceEqual(prior), mode + " fails closed without replacing the destination");
            Check(!Directory.EnumerateFiles(sandbox, "*.download-*").Any(), mode + " leaves no partial package");
        }
        fake.Mode = "allowed-redirect";
        await OfficialPluginClient.SaveOfflinePackageAsync(fake.Module, destination, options);
        Check(File.ReadAllBytes(destination).SequenceEqual(fake.Bytes), "trusted storage redirect retains SHA256 validation");
        fake.Mode = "delay"; OfficialPluginClient.DownloadTimeoutOverrideForTesting = TimeSpan.FromMilliseconds(50);
        bool timedOut = false;
        try { await OfficialPluginClient.SaveOfflinePackageAsync(fake.Module, destination, options); } catch (TimeoutException) { timedOut = true; }
        Check(timedOut, "mirror metadata lookup participates in package timeout");
        OfficialPluginClient.DownloadTimeoutOverrideForTesting = null;
        using var cancel = new CancellationTokenSource(50); bool cancelled = false;
        try { await OfficialPluginClient.FetchCatalogAsync(options, cancel.Token); } catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled, "user cancellation is preserved for mirror discovery");
        fake.Mode = "empty"; bool emptyRejected = false;
        try { await OfficialPluginClient.FetchCatalogAsync(options); } catch (InvalidDataException) { emptyRejected = true; }
        Check(emptyRejected && fake.Requests.All(uri => uri.Host != "github.com" && uri.Host != "api.github.com"), "empty mirror is explicit failure without silent GitHub fallback");
        fake.Mode = "missing"; bool incompleteRejected = false;
        string cacheBefore = File.ReadAllText(PluginPaths.OfficialCatalogCacheFile);
        try { await OfficialPluginClient.FetchCatalogAsync(options); } catch (InvalidDataException) { incompleteRejected = true; }
        Check(incompleteRejected && File.ReadAllText(PluginPaths.OfficialCatalogCacheFile) == cacheBefore,
            "visible catalog with missing current-tag package is rejected without replacing cached catalog");
        fake.Mode = "unmarked"; bool unmarkedRejected = false;
        try { await OfficialPluginClient.FetchCatalogAsync(options); } catch (InvalidDataException) { unmarkedRejected = true; }
        Check(unmarkedRejected && File.ReadAllText(PluginPaths.OfficialCatalogCacheFile) == cacheBefore,
            "complete legacy mirror before provenance PATCH cannot replace cached catalog");
        fake.Mode = "backfill";
        catalog = await OfficialPluginClient.FetchCatalogAsync(options);
        Check(catalog.ReleaseTag == fake.Module.ReleaseTag, "late historical backfill cannot outrank original source publication");
    }
    private sealed class MirrorHttpFixture : HttpMessageHandler
    {
        internal byte[] Bytes { get; } = Encoding.UTF8.GetBytes("fixture-only-package");
        internal string Mode = "ok";
        internal List<Uri> Requests { get; } = new();
        internal OfficialPluginModule Module { get; }
        private const string Catalog = "https://atomgit.com/Star-Pie/StarPie-Official-Plugins/releases/download/modules-fixture/module-catalog.json";
        private const string Package = "https://atomgit.com/Star-Pie/StarPie-Official-Plugins/releases/download/modules-fixture/fixture.spkg";
        private const string Storage = "https://fixture.obs.cn-north-4.myhuaweicloud.com/fixture.spkg";
        internal MirrorHttpFixture()
        {
            Module = new() { Id = "starpie.plugin.fixture", Name = "Fixture", Version = "1.0.0", ReleaseTag = "modules-fixture", AssetName = "fixture.spkg",
                PackageUrl = "https://github.com/Star-Pie/StarPie-Official-Plugins/releases/download/modules-fixture/fixture.spkg", Sha256 = Convert.ToHexString(SHA256.HashData(Bytes)), Size = Bytes.Length,
                ApiVersion = "1.15", TargetFramework = "net8.0-windows", MinHostVersion = "2.0.0" };
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri uri = request.RequestUri!; Requests.Add(uri);
            if (!UpdateManager.IsAtomGitAssetUrl(uri.AbsoluteUri)) throw new InvalidOperationException("Unexpected real-network destination");
            if (Mode == "delay") await Task.Delay(Timeout.Infinite, cancellationToken);
            if (Mode == "unavailable") return new(HttpStatusCode.NotFound) { Content = new StringContent("not synchronized") };
            if (uri.Host == "api.atomgit.com")
            {
                var assets = new List<object> { new { name = "module-catalog.json", browser_download_url = Catalog } };
                if (Mode != "missing") assets.Add(new { name = "fixture.spkg", browser_download_url = Package });
                object release = new { tag_name = Mode == "wrong-tag" ? "other" : Module.ReleaseTag, published_at = "2026-11-01T00:00:00Z", assets,
                    body = Mode == "unmarked" ? "Legacy partial mirror" : "<!-- starpie-source-published-at: 2026-11-01T00:00:00.000Z -->" };
                bool single = uri.AbsolutePath.Contains("/tags/", StringComparison.Ordinal);
                if (!single && Mode == "backfill")
                {
                    var older = new { tag_name = "old", published_at = "2026-12-01T00:00:00Z", assets,
                        body = "Old release\n\n<!-- starpie-source-published-at: 2026-10-01T00:00:00.000Z -->" };
                    return Json(new object[] { older, release });
                }
                return Json(single ? release : Mode == "empty" ? Array.Empty<object>() : new[] { release });
            }
            if (uri.AbsoluteUri == Catalog)
                return Json(new OfficialPluginCatalog { SchemaVersion = 2, ReleaseTag = Module.ReleaseTag, CatalogVersion = "fixture", Modules = new() { new() { Id = Module.Id, Versions = new() { Module } } } });
            if (uri.AbsoluteUri == Package && Mode is "unsafe-redirect" or "allowed-redirect")
            {
                var response = new HttpResponseMessage(HttpStatusCode.Found);
                response.Headers.Location = new(Mode == "unsafe-redirect" ? "https://evil.example/fixture.spkg" : Storage); return response;
            }
            if (uri.AbsoluteUri == Package || uri.AbsoluteUri == Storage)
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(Mode == "tampered" ? Bytes.Select(value => (byte)(value ^ 1)).ToArray() : Bytes) };
            throw new InvalidOperationException("Unexpected fixture route: " + uri.AbsoluteUri);
        }
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    }
}
