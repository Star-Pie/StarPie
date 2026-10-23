using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using WinPieGestures;
using WinPieGestures.Plugins;

internal static class Program
{
    private static readonly string Sandbox = Path.Combine(Path.GetTempPath(), "StarPie-OfflineTests-" + Guid.NewGuid().ToString("N"));
    private static int _checks;
    private static int _failed;
    private static int _requests;
    private static string Fixture => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
        "../../../../onboarding-download-tests/Fixture/bin/Release/net8.0-windows/TestFixturePlugin.dll"));
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        Directory.CreateDirectory(Sandbox);
        Environment.SetEnvironmentVariable("LOCALAPPDATA", Path.Combine(Sandbox, "app-data"));
        PluginPaths.OverrideRootsForTesting(Path.Combine(Sandbox, "plugins"), Path.Combine(Sandbox, "scan"));
        PluginHost.HeadlessMode = true;
        OfficialPluginClient.HttpHandlerOverrideForTesting = new Handler(_ =>
        {
            _requests++;
            throw new HttpRequestException("Disconnected: offline operations must never call HTTP");
        });
        Run("production download channel XAML resources load without a window", PluginUiResourceTests.LoadDownloadChannelControl);
        if (args.Contains("--ui-resources-only")) return _failed == 0 ? 0 : 1;
        if (args.Contains("--hash-guard-only"))
        {
            string probe = CreatePackage("hash-guard", "1.0.0", _ => { });
            var expected = ModuleFor(probe);
            expected.Sha256 = new string('0', 64);
            Cache(expected);
            Reject(() => OfficialPluginClient.PrepareOfflinePackage(probe), "hash guard accepted valid ZIP with untrusted bytes");
            Console.WriteLine("PASS isolated trusted-hash guard");
            return 0;
        }
        Run("bundled trusted catalog is available without cached data", () =>
        {
            Check(!File.Exists(PluginPaths.OfficialCatalogCacheFile), "fresh machine");
            var seed = OfficialPluginClient.LoadBundledCatalog();
            Check(seed.SchemaVersion == 2 && seed.Modules.Count == 19, "reviewed release catalog");
            Check(OfficialPluginClient.GetTrustedOfflineModules().Any(m => m.Id == "starpie.builtin.folder"), "core package trust data");
        });
        string package = CreatePackage("valid", "1.0.0", archive => { });
        var module = ModuleFor(package);
        Run("old DLL-only path reproduces the reported offline import failure", () =>
            Check(!PluginHost.PrepareInstall(package).Accepted, "raw spkg cannot go through DLL-only scanner"));
        Run("self-declared catalog and matching hash never grant official identity", () =>
        {
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(package)!, "module-catalog.json"), JsonSerializer.Serialize(Catalog(module), JsonOptions));
            Reject(() => OfficialPluginClient.PrepareOfflinePackage(package), "untrusted sidecar ignored");
            Check(!File.Exists(PluginPaths.RegistryFile), "no registry before consent");
        });
        Cache(module);
        Run("trusted local import scans without installing and cleans cancellation", () =>
        {
            string root;
            using (var prepared = OfficialPluginClient.PrepareOfflinePackage(package))
            {
                root = prepared.TemporaryRoot;
                Check(prepared.Scan.Accepted, "real PE/manifest scan");
                Check(prepared.Scan.Manifest!.Id == module.Id, "identity matches trusted data");
                Check(!File.Exists(PluginPaths.RegistryFile), "no install before confirmation");
                Check(!Directory.Exists(Path.Combine(PluginPaths.Root, module.Id)), "no installed payload before confirmation");
            }
            Check(!Directory.Exists(root), "cancel cleans snapshot");
        });
        using (var prepared = OfficialPluginClient.PrepareOfflinePackage(package))
        {
            var rejected = await OfficialPluginClient.InstallOfflinePackageAsync(prepared, acknowledged: false);
            Check(!rejected.Success && !File.Exists(PluginPaths.RegistryFile), "unconfirmed install refused");
            var result = await OfficialPluginClient.InstallOfflinePackageAsync(prepared, acknowledged: true);
            Check(result.Success, "confirmed install succeeds: " + result.Error);
            var entry = PluginRegistryStore.FindEntry(module.Id)!;
            Check(entry.Official && !entry.Enabled && !entry.Preload, "new official offline import stays disabled");
            Check(entry.Source == "OfficialOfflinePackage", "source is recorded");
        }
        string upgrade = CreatePackage("upgrade", "1.0.1", _ => { });
        var upgradeModule = ModuleFor(upgrade, "1.0.1");
        Cache(module, upgradeModule);
        var existing = PluginRegistryStore.FindEntry(module.Id)!;
        existing.Preload = true;
        PluginRegistryStore.UpsertEntry(existing);
        using (var prepared = OfficialPluginClient.PrepareOfflinePackage(upgrade))
        {
            var result = await OfficialPluginClient.InstallOfflinePackageAsync(prepared, true);
            Check(result.Success, "disabled update succeeds: " + result.Error);
            existing = PluginRegistryStore.FindEntry(module.Id)!;
            Check(!existing.Enabled && existing.Preload && existing.Version == "1.0.1", "disabled and preload preferences preserved");
        }
        Run("downgrades and corrupted hashes are rejected without changing installed data", () =>
        {
            Reject(() => OfficialPluginClient.PrepareOfflinePackage(package), "downgrade refused");
            string bad = Path.Combine(Sandbox, "damaged.spkg");
            File.Copy(upgrade, bad);
            using (var stream = new FileStream(bad, FileMode.Open, FileAccess.Write)) stream.WriteByte(0);
            Reject(() => OfficialPluginClient.PrepareOfflinePackage(bad), "hash mismatch refused");
            Check(PluginRegistryStore.FindEntry(module.Id)!.Version == "1.0.1", "installed entry unchanged");
        });
        Run("matching size and identity cannot bypass the trusted package hash", () =>
        {
            string validZip = CreatePackage("wrong-trusted-hash", "1.0.2", _ => { });
            var wrongHash = ModuleFor(validZip, "1.0.2");
            wrongHash.Sha256 = new string('0', 64);
            Cache(wrongHash);
            Reject(() => OfficialPluginClient.PrepareOfflinePackage(validZip), "trusted hash is required even for an otherwise valid package");
        });
        foreach (string attack in new[] { "traversal", "duplicate", "expanded", "many", "identity", "assembly" })
        {
            Run("reject " + attack, () =>
            {
                string bad = CreatePackage(attack, "1.0.2", zip =>
                {
                    if (attack == "traversal") Write(zip, "../escape.txt", "escape");
                    if (attack == "duplicate") Write(zip, "plugin.json", "{}");
                    if (attack == "expanded")
                    {
                        using var stream = zip.CreateEntry("large.bin", CompressionLevel.SmallestSize).Open();
                        byte[] block = new byte[1024 * 1024];
                        for (int i = 0; i < 101; i++) stream.Write(block);
                    }
                    if (attack == "many") for (int i = 0; i < 513; i++) Write(zip, "item" + i, "");
                }, wrongIdentity: attack == "identity", wrongAssembly: attack == "assembly");
                Cache(ModuleFor(bad, "1.0.2"));
                Reject(() => OfficialPluginClient.PrepareOfflinePackage(bad), attack);
                Check(!File.Exists(Path.Combine(Path.GetTempPath(), "escape.txt")), "no path escape");
            });
        }
        Cache(module);
        Run("incompatible cached package rejected", () =>
        {
            module.MinHostVersion = "999.0.0";
            Cache(module);
            Reject(() => OfficialPluginClient.PrepareOfflinePackage(package), "compatibility gate");
            module.MinHostVersion = "0.0.0";
        });
        Run("corrupt cache falls back to immutable bundled trust", () =>
        {
            File.WriteAllText(PluginPaths.OfficialCatalogCacheFile, "invalid");
            Check(OfficialPluginClient.GetTrustedOfflineModules().Any(), "seed fallback");
            Reject(() => OfficialPluginClient.PrepareOfflinePackage(upgrade), "fixture cannot fall back to untrusted sidecar");
        });
        if (args.Length > 0)
        {
            Run("real released package imports on a fresh offline computer", () =>
            {
                File.Delete(PluginPaths.OfficialCatalogCacheFile);
                using var prepared = OfficialPluginClient.PrepareOfflinePackage(args[0]);
                Check(prepared.Scan.Accepted && prepared.Module.Id == "starpie.builtin.folder", "bundled seed matches actual published spkg");
            });
        }
        Check(_requests == 0, "all offline operations performed zero HTTP requests");
        await DownloadTests(package, module);
        foreach (string language in new[] { "zh-CN", "zh-TW", "en-US", "ja-JP" })
        {
            I18n.SetLanguage(language);
            foreach (string key in new[] { "PluginsOfflineGuide", "PluginsImportFilter", "PluginsOfflineUnknownPackage", "PluginsOfflineSave", "PluginsOfflineImport" })
                Check(I18n.T(key) != key, language + " " + key);
            var item = new OfficialPluginListItem(new OfficialPluginCatalogEntry { Id = module.Id, Versions = [module] }, "1.0.0");
            Check(item.SavePackageText == I18n.T("PluginsOfflineSave") && item.CanSavePackage, "installed plugin can still be saved offline");
        }
        Console.WriteLine($"Checks={_checks}; failed scenarios={_failed}; sandbox={Sandbox}. No windows shown or plugin actions executed.");
        return _failed == 0 ? 0 : 1;
    }

    private static async Task DownloadTests(string package, OfficialPluginModule module)
    {
        string destination = Path.Combine(Sandbox, "saved.spkg");
        OfficialPluginClient.HttpHandlerOverrideForTesting = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent(File.ReadAllBytes(package)) });
        await OfficialPluginClient.SaveOfflinePackageAsync(module, destination, new OfficialPluginDownloadOptions("direct"));
        Check(File.ReadAllBytes(destination).SequenceEqual(File.ReadAllBytes(package)), "export keeps original verified spkg bytes");
        byte[] original = File.ReadAllBytes(destination);
        OfficialPluginClient.HttpHandlerOverrideForTesting = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent([1, 2, 3]) });
        try { await OfficialPluginClient.SaveOfflinePackageAsync(module, destination, new("direct")); Check(false, "bad download rejected"); }
        catch (InvalidDataException) { Check(File.ReadAllBytes(destination).SequenceEqual(original), "failed export preserves existing destination"); }
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        try { await OfficialPluginClient.SaveOfflinePackageAsync(module, destination, new("direct"), cts.Token); Check(false, "cancelled export rejected"); }
        catch (OperationCanceledException) { Check(File.ReadAllBytes(destination).SequenceEqual(original), "cancelled export preserves destination"); }
        Check(!Directory.GetFiles(Sandbox, "*.download-*").Any(), "partial downloads removed");
    }

    private static string CreatePackage(string name, string version, Action<ZipArchive> customize, bool wrongIdentity = false, bool wrongAssembly = false)
    {
        string path = Path.Combine(Sandbox, name + ".spkg");
        byte[] dll = File.ReadAllBytes(Fixture);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var target = archive.CreateEntry("TestFixturePlugin.dll").Open()) target.Write(dll);
        Write(archive, "plugin.json", JsonSerializer.Serialize(new
        {
            schemaVersion = 1, id = "starpie.plugin.keypadlayer", name = "Offline test", version,
            author = "Test fixture", license = "MIT", description = "Isolated offline import test",
            apiVersion = "1.0", targetFramework = "net8.0-windows", minHostVersion = "0.0.0",
            platform = "win-x64", assembly = "TestFixturePlugin.dll", capabilities = Array.Empty<string>()
        }));
        Write(archive, "module.manifest.json", JsonSerializer.Serialize(new
        {
            id = wrongIdentity ? "starpie.invalid" : "starpie.plugin.keypadlayer", version,
            assembly = "TestFixturePlugin.dll", assemblySha256 = wrongAssembly ? new string('0', 64) : Convert.ToHexString(SHA256.HashData(dll))
        }));
        customize(archive);
        return path;
    }

    private static OfficialPluginModule ModuleFor(string path, string version = "1.0.0") => new()
    {
        Id = "starpie.plugin.keypadlayer", Name = "Offline test", Version = version, ReleaseTag = "offline-test",
        AssetName = "fixture.spkg", PackageUrl = OfficialPluginClient.RepositoryUrl + "/releases/download/offline-test/fixture.spkg",
        Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), Size = new FileInfo(path).Length,
        ApiVersion = "1.0", TargetFramework = "net8.0-windows", MinHostVersion = "0.0.0"
    };
    private static OfficialPluginCatalog Catalog(params OfficialPluginModule[] modules) => new()
    {
        SchemaVersion = 2, CatalogVersion = "offline-test", Modules = [new() { Id = modules[0].Id, Versions = modules.ToList() }]
    };
    private static void Cache(params OfficialPluginModule[] modules)
    {
        Directory.CreateDirectory(PluginPaths.Root);
        File.WriteAllText(PluginPaths.OfficialCatalogCacheFile, JsonSerializer.Serialize(new OfficialPluginCatalogCache
            { Catalog = Catalog(modules), CachedAt = DateTimeOffset.UtcNow }, JsonOptions));
    }
    private static void Write(ZipArchive archive, string path, string text)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path).Open());
        writer.Write(text);
    }
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        _checks++;
    }
    private static void Reject(Func<OfflinePluginPackage> action, string label)
    {
        try { using var unexpected = action(); throw new Exception("Accepted: " + label); }
        catch (InvalidDataException) { _checks++; }
    }
    private static void Run(string name, Action test)
    {
        try { test(); Console.WriteLine("PASS " + name); }
        catch (Exception ex) { _failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request));
        }
    }
}
