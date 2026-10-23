using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace WinPieGestures.Plugins;

/// <summary>Owns the verified snapshot until the confirmation/installation completes.</summary>
internal sealed class OfflinePluginPackage : IDisposable
{
    internal string TemporaryRoot { get; }
    internal OfficialPluginModule Module { get; }
    internal PluginScanResult Scan { get; }

    internal OfflinePluginPackage(string root, OfficialPluginModule module, PluginScanResult scan)
        => (TemporaryRoot, Module, Scan) = (root, module, scan);

    public void Dispose()
    {
        if (Directory.Exists(TemporaryRoot)) Directory.Delete(TemporaryRoot, recursive: true);
    }
}

internal static partial class OfficialPluginClient
{
    // This seed is reviewed release metadata, never a catalog supplied alongside an imported package.
    internal static OfficialPluginCatalog LoadBundledCatalog()
    {
        using Stream stream = typeof(OfficialPluginClient).Assembly.GetManifestResourceStream(
            "WinPieGestures.Plugins.OfficialPluginCatalogSeed.json")
            ?? throw new InvalidDataException(I18n.T("PluginsOfflineCatalogMissing"));
        var catalog = JsonSerializer.Deserialize<OfficialPluginCatalog>(stream, JsonOptions);
        ValidateCatalog(catalog);
        return catalog!;
    }

    internal static IEnumerable<OfficialPluginModule> GetTrustedOfflineModules()
    {
        // Reading a cache is optional; a first-time, disconnected computer uses the bundled seed.
        if (TryLoadCachedCatalog(out var cached, out _, out _))
            foreach (var entry in cached.Modules)
                foreach (var module in entry.Versions) yield return module;
        foreach (var entry in LoadBundledCatalog().Modules)
            foreach (var module in entry.Versions) yield return module;
    }

    internal static OfflinePluginPackage PrepareOfflinePackage(string filePath)
    {
        using var source = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (source.Length <= 0 || source.Length > MaxPackageSize)
            throw new InvalidDataException(I18n.T("PluginsOfflineSizeInvalid"));
        string hash = Convert.ToHexString(SHA256.HashData(source));
        var module = GetTrustedOfflineModules().FirstOrDefault(m => m.Size == source.Length &&
            string.Equals(m.Sha256, hash, StringComparison.OrdinalIgnoreCase));
        if (module == null) throw new InvalidDataException(I18n.T("PluginsOfflineUnknownPackage"));
        var compatibility = OfficialPluginVersionSelector.Evaluate(module);
        if (!compatibility.IsCompatible) throw new InvalidDataException(compatibility.Message);
        var previous = PluginRegistryStore.FindEntry(module.Id);
        if (previous != null && SimpleVersion.TryParse(previous.Version, out var installed) &&
            SimpleVersion.TryParse(module.Version, out var requested) && requested.CompareTo(installed) < 0)
            throw new InvalidDataException(I18n.T("PluginsOfficialStateInstalledNewer"));

        string root = Path.Combine(Path.GetTempPath(), "StarPie-OfflinePlugin-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            string snapshot = Path.Combine(root, "package.spkg");
            // Keep the original handle open without write sharing through hashing and snapshotting.
            source.Position = 0;
            using (var target = File.Create(snapshot)) source.CopyTo(target);
            string extracted = Path.Combine(root, "package");
            Directory.CreateDirectory(extracted);
            ExtractPackageSafely(snapshot, extracted);
            VerifyModuleManifest(extracted, module);
            var scan = PluginScanner.ScanInstalledPlugin(extracted, allowReservedIdPrefix: true);
            if (!scan.Accepted || scan.Manifest == null)
                throw new InvalidDataException(I18n.TF("PluginsOfflineScanFailed", scan.DescribeFailure()));
            if (!string.Equals(scan.Manifest.Id, module.Id, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(scan.Manifest.Version, module.Version, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(I18n.T("PluginsOfflineIdentityMismatch"));
            return new OfflinePluginPackage(root, module, scan);
        }
        catch
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            throw;
        }
    }

    internal static async Task<PluginInstallResult> InstallOfflinePackageAsync(
        OfflinePluginPackage package, bool acknowledged, CancellationToken cancellationToken = default)
    {
        if (!acknowledged) return new PluginInstallResult { Error = I18n.T("PluginsOfflineConfirmationRequired") };
        cancellationToken.ThrowIfCancellationRequested();
        var previous = PluginRegistryStore.FindEntry(package.Module.Id);
        if (previous != null && SimpleVersion.TryParse(previous.Version, out var installed) &&
            SimpleVersion.TryParse(package.Module.Version, out var requested) && requested.CompareTo(installed) < 0)
            return new PluginInstallResult { Error = I18n.T("PluginsOfficialStateInstalledNewer") };
        bool preload = previous?.Preload ?? false;
        var result = await PluginHost.CommitInstallAsync(package.Scan, new PluginInstallOptions
        {
            Acknowledged = true,
            AcknowledgedCapabilities = package.Scan.Manifest!.Capabilities.ToList(),
            Official = true,
            SourceKind = "OfficialOfflinePackage",
            OverwriteExisting = true,
            EnableAfterInstall = previous?.Enabled ?? false,
        }, cancellationToken);
        if (result.Success && PluginHost.Find(package.Module.Id) is { } instance)
        {
            instance.Entry.Preload = preload;
            PluginRegistryStore.UpsertEntry(instance.Entry);
        }
        return result;
    }

    internal static async Task SaveOfflinePackageAsync(OfficialPluginModule module, string destination,
        OfficialPluginDownloadOptions options, CancellationToken cancellationToken = default)
    {
        // The caller selected a destination explicitly. Failed downloads must not replace an existing file.
        var compatibility = OfficialPluginVersionSelector.Evaluate(module);
        if (!compatibility.IsCompatible) throw new InvalidDataException(compatibility.Message);
        string staging = destination + ".download-" + Guid.NewGuid().ToString("N");
        try
        {
            await DownloadPackageAsync(module, staging, options, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(staging, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(staging)) File.Delete(staging);
        }
    }
}
