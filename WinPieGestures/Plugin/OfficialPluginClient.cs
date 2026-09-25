using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
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
    public List<OfficialPluginModule> Modules { get; set; } = new();
}

internal sealed class OfficialPluginModule
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string ReleaseTag { get; set; } = "";
    public string AssetName { get; set; } = "";
    public string PackageUrl { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long Size { get; set; }
    public string ApiVersion { get; set; } = "";
    public string MinHostVersion { get; set; } = "";
    public string? MaxHostVersion { get; set; }
    public List<string> TypeClaims { get; set; } = new();
    public List<string> Capabilities { get; set; } = new();
}


internal sealed class OfficialPluginInstallResult
{
    public bool Success { get; init; }
    public string PluginId { get; init; } = "";
    public string Error { get; init; } = "";
    public bool Enabled { get; init; }
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
        return catalog;
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

        PluginInstance? previous = PluginHost.Find(module.Id);
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

            return new OfficialPluginInstallResult
            {
                Success = result.Success,
                PluginId = result.PluginId,
                Error = result.Error,
                Enabled = result.Enabled,
            };
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

    private static void ValidateCatalog(OfficialPluginCatalog? catalog)
    {
        if (catalog == null) throw new InvalidDataException("官方插件 catalog 为空。");
        if (catalog.SchemaVersion != 1) throw new InvalidDataException("不支持的官方插件 catalog 版本。");
        if (catalog.Modules == null) throw new InvalidDataException("官方插件 catalog 缺少 modules。");

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (OfficialPluginModule module in catalog.Modules)
        {
            if (string.IsNullOrWhiteSpace(module.Id) || !module.Id.StartsWith("starpie.", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("catalog 含有非法官方插件 ID。");
            if (!ids.Add(module.Id)) throw new InvalidDataException($"catalog 含有重复插件：{module.Id}。");
            if (!Uri.TryCreate(module.PackageUrl, UriKind.Absolute, out Uri? uri)
                || uri.Scheme != Uri.UriSchemeHttps
                || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
                || !uri.AbsolutePath.StartsWith("/Star-Pie/StarPie-Official-Plugins/releases/download/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"插件 {module.Id} 的下载地址不是官方 GitHub Release 地址。");
            if (!string.Equals(Path.GetFileName(uri.AbsolutePath), module.AssetName, StringComparison.Ordinal))
                throw new InvalidDataException($"插件 {module.Id} 的下载资产名与 catalog 不一致。");
            if (module.Size <= 0 || module.Size > MaxPackageSize)
                throw new InvalidDataException($"插件 {module.Id} 的包大小不合法。");
            if (module.Sha256.Length != 64 || !module.Sha256.All(Uri.IsHexDigit))
                throw new InvalidDataException($"插件 {module.Id} 的 SHA-256 不合法。");
        }
    }
}





