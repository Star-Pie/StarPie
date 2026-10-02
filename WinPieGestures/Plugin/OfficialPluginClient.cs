using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using StarPie.Plugin;

namespace WinPieGestures.Plugins;

/// <summary>StarPie 官方模块仓库的 catalog 条目。</summary>
internal sealed class OfficialPluginCatalog
{
    public int SchemaVersion { get; set; }
    public string CatalogVersion { get; set; } = "";
    public string ReleaseTag { get; set; } = "";
    public string ReleaseChannel { get; set; } = "";
    public string SdkApiVersion { get; set; } = "";
    public string MinimumHostVersion { get; set; } = "";
    public DateTimeOffset GeneratedAt { get; set; }
    public List<OfficialPluginCatalogEntry> Modules { get; set; } = new();
}

internal sealed class OfficialPluginCatalogEntry
{
    public string Id { get; set; } = "";
    public List<OfficialPluginModule> Versions { get; set; } = new();
}

/// <summary>一个不可变发布版本的元数据。ID 由所属目录项赋值，不重复序列化。</summary>
internal sealed class OfficialPluginModule
{
    [JsonIgnore]
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string ReleaseTag { get; set; } = "";
    public string AssetName { get; set; } = "";
    public string PackageUrl { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long Size { get; set; }
    public string ApiVersion { get; set; } = "";
    public string TargetFramework { get; set; } = "";
    public string MinHostVersion { get; set; } = "";
    public string? MaxHostVersion { get; set; }
    public List<string> TypeClaims { get; set; } = new();
    public List<string> Capabilities { get; set; } = new();
    public string Description { get; set; } = "";
    public string Author { get; set; } = "";
    public string? Homepage { get; set; }
    public string License { get; set; } = "";
    public string? Icon { get; set; }
    public List<string> Tags { get; set; } = new();
    public List<OfficialPluginFeature> Features { get; set; } = new();
}

internal sealed class OfficialPluginFeature
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public string? IconKey { get; set; }
}

internal sealed class OfficialPluginCatalogCache
{
    public int CacheVersion { get; set; } = 1;
    public DateTimeOffset CachedAt { get; set; }
    public OfficialPluginCatalog Catalog { get; set; } = new();
}

internal sealed class OfficialPluginInstallResult
{
    public bool RestartSuggested { get; init; }
    public bool Success { get; init; }
    public string PluginId { get; init; } = "";
    public string Error { get; init; } = "";
    public bool Enabled { get; init; }

    internal static OfficialPluginInstallResult FromInstallResult(PluginInstallResult result) => new()
    {
        Success = result.Success, PluginId = result.PluginId, Error = result.Error,
        Enabled = result.Enabled, RestartSuggested = result.RestartSuggested,
    };
}

/// <summary>
/// 官方插件在线目录与 .spkg 下载器。
/// <para>下载只在插件管理页显式触发，绝不进入鼠标钩子或动作执行热路径。</para>
/// </summary>
internal static class OfficialPluginClient
{
    internal const string RepositoryUrl = "https://github.com/Star-Pie/StarPie-Official-Plugins";
    internal const string ReleasesFeedUrl = RepositoryUrl + "/releases.atom";
    private const long MaxPackageSize = 100L * 1024L * 1024L;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("StarPie/1.8 official-plugin-client");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/atom+xml");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }

    public static async Task<OfficialPluginCatalog> FetchCatalogAsync(CancellationToken cancellationToken = default)
    {
        // 不使用 GitHub REST Releases API：匿名 REST 调用共享严格的每小时限额，
        // 容易在普通用户环境里返回 403。Atom feed 不消耗该 REST rate limit，
        // 只用于找到最新已发布标签；真正的 catalog 与包仍从 Release 资产下载。
        string releaseTag = await FetchLatestReleaseTagAsync(cancellationToken).ConfigureAwait(false);
        string catalogUrl = $"{RepositoryUrl}/releases/download/{Uri.EscapeDataString(releaseTag)}/module-catalog.json";
        byte[] catalogBytes = await Http.GetByteArrayAsync(catalogUrl, cancellationToken).ConfigureAwait(false);
        OfficialPluginCatalog? catalog = JsonSerializer.Deserialize<OfficialPluginCatalog>(catalogBytes, JsonOptions);
        ValidateCatalog(catalog);
        if (!string.Equals(catalog!.ReleaseTag, releaseTag, StringComparison.Ordinal))
            throw new InvalidDataException($"官方 catalog 的 releaseTag 与发布标签不一致：{catalog.ReleaseTag} / {releaseTag}。");

        // 下载成功后写入本地缓存。缓存失败不应把成功的目录刷新误报成失败。
        try
        {
            SaveCachedCatalog(catalog);
        }
        catch (Exception ex)
        {
            AppLogger.LogWarn($"[plugin] 保存官方插件目录缓存失败：{ex.Message}");
        }

        return catalog;
    }

    public static bool TryLoadCachedCatalog(
        out OfficialPluginCatalog catalog,
        out DateTimeOffset cachedAt,
        out string error)
    {
        catalog = null!;
        cachedAt = default;
        error = "";

        string path = PluginPaths.OfficialCatalogCacheFile;
        if (!File.Exists(path)) return false;

        try
        {
            OfficialPluginCatalogCache? cache = JsonSerializer.Deserialize<OfficialPluginCatalogCache>(
                File.ReadAllText(path), JsonOptions);
            if (cache?.Catalog == null)
            {
                error = "官方插件目录缓存为空。";
                return false;
            }
            if (cache.CacheVersion != 1)
            {
                error = "官方插件目录缓存版本不受支持。";
                return false;
            }

            ValidateCatalog(cache.Catalog);
            catalog = cache.Catalog;
            cachedAt = cache.CachedAt == default
                ? new DateTimeOffset(File.GetLastWriteTimeUtc(path))
                : cache.CachedAt;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static DateTimeOffset? GetCachedCatalogTimestamp()
    {
        try
        {
            string path = PluginPaths.OfficialCatalogCacheFile;
            return File.Exists(path) ? new DateTimeOffset(File.GetLastWriteTimeUtc(path)) : null;
        }
        catch
        {
            return null;
        }
    }

    private static void SaveCachedCatalog(OfficialPluginCatalog catalog)
    {
        Directory.CreateDirectory(PluginPaths.Root);
        string target = PluginPaths.OfficialCatalogCacheFile;
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var cache = new OfficialPluginCatalogCache
        {
            CacheVersion = 1,
            CachedAt = DateTimeOffset.Now,
            Catalog = catalog,
        };

        try
        {
            string json = JsonSerializer.Serialize(cache, JsonOptions);
            File.WriteAllText(temporary, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            catch
            {
            }
        }
    }

    private static async Task<string> FetchLatestReleaseTagAsync(CancellationToken cancellationToken)
    {
        string feed = await Http.GetStringAsync(ReleasesFeedUrl, cancellationToken).ConfigureAwait(false);
        XDocument document = XDocument.Parse(feed, LoadOptions.None);
        XNamespace atom = "http://www.w3.org/2005/Atom";

        foreach (XElement entry in document.Descendants(atom + "entry"))
        {
            string? href = entry.Elements(atom + "link")
                .Select(link => (string?)link.Attribute("href"))
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (!Uri.TryCreate(href, UriKind.Absolute, out Uri? uri)) continue;

            const string marker = "/Star-Pie/StarPie-Official-Plugins/releases/tag/";
            int index = uri.AbsolutePath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0) continue;

            string tag = Uri.UnescapeDataString(uri.AbsolutePath[(index + marker.Length)..]);
            if (!string.IsNullOrWhiteSpace(tag)) return tag;
        }

        throw new InvalidDataException("官方插件仓库尚未发布可用模块 catalog。");
    }

    public static Task<OfficialPluginInstallResult> InstallAsync(
        OfficialPluginModule module,
        CancellationToken cancellationToken = default)
        => InstallAsync(module, extraCapabilityPrompter: null, approvedCapabilities: null, cancellationToken);

    public static async Task<OfficialPluginInstallResult> InstallAsync(
        OfficialPluginModule module,
        Func<string, List<string>, Task<bool>>? extraCapabilityPrompter,
        IReadOnlyCollection<string>? approvedCapabilities,
        CancellationToken cancellationToken = default,
        bool? forceEnable = null)
    {
        if (module == null) return new OfficialPluginInstallResult { Error = "官方插件条目为空。" };
        OfficialPluginCompatibility compatibility = OfficialPluginVersionSelector.Evaluate(module);
        if (!compatibility.IsCompatible)
            return new OfficialPluginInstallResult { PluginId = module.Id, Error = compatibility.Message };
        cancellationToken.ThrowIfCancellationRequested();

        PluginInstance? previous = PluginHost.Find(module.Id);
        if (previous != null && SimpleVersion.TryParse(previous.Entry.Version, out SimpleVersion installedVersion) &&
            SimpleVersion.TryParse(module.Version, out SimpleVersion requestedVersion) && requestedVersion.CompareTo(installedVersion) < 0)
            return new OfficialPluginInstallResult { PluginId = module.Id, Error = I18n.T("PluginsOfficialStateInstalledNewer") };
        bool enableAfterInstall = forceEnable ?? (previous?.Entry.Enabled ?? true);
        bool preloadAfterInstall = previous?.Entry.Preload ?? false;

        string tempRoot = Path.Combine(Path.GetTempPath(), "StarPie-OfficialPlugin-" + Guid.NewGuid().ToString("N"));
        string packagePath = Path.Combine(tempRoot, module.AssetName);
        string extractRoot = Path.Combine(tempRoot, "package");
        Directory.CreateDirectory(tempRoot);

        try
        {
            await DownloadPackageAsync(module, packagePath, cancellationToken).ConfigureAwait(false);
            Directory.CreateDirectory(extractRoot);
            ExtractPackageSafely(packagePath, extractRoot);

            VerifyModuleManifest(extractRoot, module);
            PluginScanResult scan = PluginScanner.ScanInstalledPlugin(extractRoot, allowReservedIdPrefix: true);
            if (!scan.Accepted || scan.Manifest == null)
            {
                return new OfficialPluginInstallResult
                {
                    PluginId = module.Id,
                    Error = "下载包中的 plugin.json 未通过 StarPie 插件校验：" + scan.DescribeFailure(),
                };
            }

            if (!string.Equals(scan.Manifest.Id, module.Id, StringComparison.OrdinalIgnoreCase))
            {
                return new OfficialPluginInstallResult
                {
                    PluginId = module.Id,
                    Error = $"包内插件 ID 与 catalog 不一致：{scan.Manifest.Id}。",
                };
            }

            if (!string.Equals(scan.Manifest.Version, module.Version, StringComparison.OrdinalIgnoreCase))
            {
                return new OfficialPluginInstallResult
                {
                    PluginId = module.Id,
                    Error = $"包内插件版本与 catalog 不一致：{scan.Manifest.Version}。",
                };
            }

            // 按每个插件分别比对下载后 plugin.json 的实际权限与已告知范围
            if (approvedCapabilities != null)
            {
                var manifestCaps = scan.Manifest.Capabilities ?? new List<string>();
                var unapprovedActual = manifestCaps
                    .Where(c => !approvedCapabilities.Contains(c, StringComparer.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (unapprovedActual.Count > 0)
                {
                    bool allowed = false;
                    if (extraCapabilityPrompter != null)
                    {
                        allowed = await extraCapabilityPrompter(module.Name, unapprovedActual).ConfigureAwait(false);
                    }

                    if (!allowed)
                    {
                        return new OfficialPluginInstallResult
                        {
                            Success = false,
                            PluginId = module.Id,
                            Error = "用户拒绝了实际包声明的额外权限：" + string.Join(", ", unapprovedActual),
                        };
                    }
                }
            }

            PluginInstallResult result = await PluginHost.CommitInstallAsync(scan, new PluginInstallOptions
            {
                Acknowledged = true,
                AcknowledgedCapabilities = new List<string>(scan.Manifest.Capabilities),
                EnableAfterInstall = enableAfterInstall,
                OverwriteExisting = true,
                Official = true,
                SourceKind = "OfficialCatalog",
            }, cancellationToken).ConfigureAwait(false);

            if (result.Success && preloadAfterInstall && PluginHost.Find(module.Id) is PluginInstance installed)
            {
                installed.Entry.Preload = true;
                PluginRegistryStore.UpsertEntry(installed.Entry);
            }

            return OfficialPluginInstallResult.FromInstallResult(result);
        }
        catch (OperationCanceledException)
        {
            return new OfficialPluginInstallResult { PluginId = module.Id, Error = "下载已取消。" };
        }
        catch (Exception ex)
        {
            AppLogger.LogWarn($"[plugin] 官方插件 {module.Id} 下载/安装失败：{ex.Message}");
            return new OfficialPluginInstallResult { PluginId = module.Id, Error = ex.Message };
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarn($"[plugin] 清理官方插件临时目录失败：{ex.Message}");
            }
        }
    }

    private static async Task DownloadPackageAsync(
        OfficialPluginModule module,
        string destination,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(module.PackageUrl, UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            || !uri.AbsolutePath.StartsWith("/Star-Pie/StarPie-Official-Plugins/releases/download/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("catalog 中的 packageUrl 不是 HTTPS 地址。");
        }

        if (module.Size <= 0 || module.Size > MaxPackageSize)
            throw new InvalidDataException($"插件包大小不合法：{module.Size} 字节。");

        using HttpResponseMessage response = await Http.GetAsync(
            uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long length && length > MaxPackageSize)
            throw new InvalidDataException("插件包超过 100 MiB 安全上限。");

        await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > MaxPackageSize) throw new InvalidDataException("插件包超过 100 MiB 安全上限。");
            hasher.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);

        if (total != module.Size)
            throw new InvalidDataException($"插件包大小校验失败：catalog={module.Size}，实际={total}。");

        string actual = Convert.ToHexString(hasher.GetHashAndReset());
        if (!string.Equals(actual, module.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("插件包 SHA-256 校验失败，文件可能已被替换或损坏。");
    }

    private static void VerifyModuleManifest(string packageRoot, OfficialPluginModule catalogModule)
    {
        string path = Path.Combine(packageRoot, "module.manifest.json");
        if (!File.Exists(path)) throw new InvalidDataException("插件包缺少 module.manifest.json。");

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;
        string assembly = root.TryGetProperty("assembly", out JsonElement assemblyElement)
            ? assemblyElement.GetString() ?? ""
            : "";
        string expectedHash = root.TryGetProperty("assemblySha256", out JsonElement hashElement)
            ? hashElement.GetString() ?? ""
            : "";
        string packageId = root.TryGetProperty("id", out JsonElement idElement) ? idElement.GetString() ?? "" : "";
        string packageVersion = root.TryGetProperty("version", out JsonElement versionElement) ? versionElement.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(assembly) || string.IsNullOrWhiteSpace(expectedHash))
            throw new InvalidDataException("module.manifest.json 缺少程序集完整性字段。");
        if (!string.Equals(packageId, catalogModule.Id, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(packageVersion, catalogModule.Version, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("module.manifest.json 的模块 ID 或版本与官方 catalog 不一致。");

        if (!string.Equals(Path.GetFileName(assembly), assembly, StringComparison.Ordinal))
            throw new InvalidDataException("module.manifest.json 的程序集路径必须是包根目录下的文件名。");

        string assemblyPath = Path.Combine(packageRoot, assembly);
        if (!File.Exists(assemblyPath)) throw new InvalidDataException("插件包中的主程序集不存在。");
        using FileStream stream = File.OpenRead(assemblyPath);
        string actualHash = Convert.ToHexString(SHA256.HashData(stream));
        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("插件主程序集 SHA-256 校验失败。");
    }
    private static void ExtractPackageSafely(string packagePath, string destination)
    {
        string root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        using ZipArchive archive = ZipFile.OpenRead(packagePath);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            string target = Path.GetFullPath(Path.Combine(destination, relative));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("插件包包含非法路径。");

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            string? parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    internal static void ValidateCatalog(OfficialPluginCatalog? catalog)
    {
        if (catalog == null) throw new InvalidDataException("官方插件 catalog 为空。");
        if (catalog.SchemaVersion != 2) throw new InvalidDataException("不支持的官方插件 catalog 版本；需要 catalog v2。请手动刷新目录。");
        if (catalog.Modules == null) throw new InvalidDataException("官方插件 catalog 缺少 modules。");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (OfficialPluginCatalogEntry entry in catalog.Modules)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Id) || !entry.Id.StartsWith("starpie.", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("catalog 含有非法官方插件 ID。");
            if (!ids.Add(entry.Id)) throw new InvalidDataException($"catalog 含有重复插件：{entry.Id}。");
            if (entry.Versions == null || entry.Versions.Count == 0)
                throw new InvalidDataException($"插件 {entry.Id} 缺少历史版本。");
            var versions = new HashSet<string>(StringComparer.Ordinal);
            foreach (OfficialPluginModule module in entry.Versions)
            {
                if (module == null) throw new InvalidDataException($"插件 {entry.Id} 含有空版本。");
                module.Id = entry.Id;
                if (!SimpleVersion.TryParse(module.Version, out SimpleVersion version) || !versions.Add(version.ToString()))
                    throw new InvalidDataException($"插件 {entry.Id} 的版本无效或重复：{module.Version}。");
                if (string.IsNullOrWhiteSpace(module.Name) || !OfficialPluginVersionSelector.HasValidRequirements(module))
                    throw new InvalidDataException($"插件 {entry.Id} v{module.Version} 缺少合法名称或兼容要求。");
                if (!Uri.TryCreate(module.PackageUrl, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps ||
                    !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
                    !uri.AbsolutePath.StartsWith("/Star-Pie/StarPie-Official-Plugins/releases/download/", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"插件 {entry.Id} 的下载地址不是官方 GitHub Release 地址。");
                if (string.IsNullOrWhiteSpace(module.ReleaseTag) || string.IsNullOrWhiteSpace(module.AssetName) || !module.AssetName.EndsWith(".spkg", StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(Path.GetFileName(uri.AbsolutePath), module.AssetName, StringComparison.Ordinal) ||
                    !uri.AbsolutePath.StartsWith("/Star-Pie/StarPie-Official-Plugins/releases/download/" + Uri.EscapeDataString(module.ReleaseTag) + "/", StringComparison.Ordinal))
                    throw new InvalidDataException($"插件 {entry.Id} 的资产名或发布标签与地址不一致。");
                if (module.Size <= 0 || module.Size > MaxPackageSize || string.IsNullOrEmpty(module.Sha256) || module.Sha256.Length != 64 || !module.Sha256.All(Uri.IsHexDigit))
                    throw new InvalidDataException($"插件 {entry.Id} 的包大小或 SHA-256 不合法。");
                var featureIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (OfficialPluginFeature feature in module.Features ?? new List<OfficialPluginFeature>())
                    if (feature == null || string.IsNullOrWhiteSpace(feature.Id) || string.IsNullOrWhiteSpace(feature.Name) || !featureIds.Add(feature.Id))
                        throw new InvalidDataException($"插件 {entry.Id} 含有无效或重复的功能条目。");
            }
        }
    }
}
