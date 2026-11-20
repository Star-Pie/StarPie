using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using StarPie.Plugin;

namespace WinPieGestures.Plugins;

internal sealed class PluginFeatureItem
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Category { get; init; } = "";
    public string? IconKey { get; init; }
}

internal sealed class PluginDetailModel
{
    public string DisplayName { get; init; } = "";
    public string VersionText { get; init; } = "";
    public string Description { get; init; } = "";
    public string Author { get; init; } = "";
    public string License { get; init; } = "";
    public string Homepage { get; init; } = "";
    public string PluginId { get; init; } = "";
    public string SourceText { get; init; } = "";
    public string StateText { get; init; } = "";
    public string TargetFramework { get; init; } = "";
    public string CapabilitiesText { get; init; } = "";
    public string DependenciesText { get; init; } = "";
    public string InstallPath { get; init; } = "";
    public string SignatureText { get; init; } = "";
    public bool HasPackageDigest { get; init; }
    public string Sha256Text { get; init; } = "";
    public IReadOnlyList<PluginFeatureItem> Features { get; init; } = Array.Empty<PluginFeatureItem>();

    public bool HasFeatures => Features.Count > 0;
    public bool HasCapabilities => !string.IsNullOrWhiteSpace(CapabilitiesText);
    public bool HasDependencies => !string.IsNullOrWhiteSpace(DependenciesText);
}

internal static class PluginDetailModelBuilder
{
    public static PluginDetailModel FromLocal(PluginInstance instance)
    {
        PluginManifest? manifest = instance.Scan.Manifest;
        List<PluginFeatureItem> features = PluginFeatureReader.Read(instance);

        if (features.Count == 0)
        {
            features = PluginHost.Catalog.SnapshotActions()
                .Where(action => string.Equals(action.PluginId, instance.PluginId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(action => action.Category, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(action => action.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .Select(action => new PluginFeatureItem
                {
                    Id = action.ShortId,
                    Name = action.CurrentDisplayName,
                    Description = action.CurrentDescription,
                    Category = action.Category,
                    IconKey = action.IconKey,
                })
                .ToList();
        }

        return new PluginDetailModel
        {
            DisplayName = PluginDisplayText.Name(instance),
            VersionText = string.IsNullOrWhiteSpace(instance.Entry.Version) ? "" : $"v{instance.Entry.Version}",
            Description = PluginDisplayText.Description(instance),
            Author = !string.IsNullOrWhiteSpace(manifest?.Author) ? manifest!.Author : instance.Entry.Author,
            License = !string.IsNullOrWhiteSpace(manifest?.License) ? manifest!.License : instance.Entry.License,
            Homepage = manifest?.Homepage ?? "",
            PluginId = instance.PluginId,
            SourceText = instance.Entry.Official ? I18n.T("PluginsDetailSourceOfficial") : I18n.T("PluginsDetailSourceLocal"),
            StateText = PluginListItem.DescribeState(instance).Text,
            TargetFramework = instance.Scan.TargetFramework,
            CapabilitiesText = string.Join(I18n.T("PluginsEnumSeparator"), manifest?.Capabilities is { Count: > 0 } declared ? declared : instance.Entry.CapabilitiesAck ?? []),
            DependenciesText = manifest?.Dependencies == null
                ? ""
                : string.Join(I18n.T("PluginsEnumSeparator"), manifest.Dependencies.Select(dependency =>
                    string.IsNullOrWhiteSpace(dependency.VersionRange) ? dependency.Id : $"{dependency.Id} {dependency.VersionRange}")),
            InstallPath = instance.Directory,
            SignatureText = I18n.T(instance.Scan.IsSigned ? "PluginsCardSigned" : "PluginsCardUnsigned"),
            Sha256Text = instance.Scan.Sha256,
            Features = features,
        };
    }

    public static PluginDetailModel FromOfficial(OfficialPluginModule module, PluginInstance? installed)
    {
        PluginDetailModel? local = installed == null ? null : FromLocal(installed);
        IReadOnlyList<PluginFeatureItem> features = local?.Features.Count > 0
            ? local.Features
            : (module.Features ?? new List<OfficialPluginFeature>()).Select(feature => new PluginFeatureItem
            {
                Id = feature.Id,
                Name = feature.Name,
                Description = feature.Description,
                Category = feature.Category,
                IconKey = feature.IconKey,
            }).ToList();

        return new PluginDetailModel
        {
            DisplayName = !string.IsNullOrWhiteSpace(module.Name) ? module.Name : (local?.DisplayName ?? module.Id),
            VersionText = string.IsNullOrWhiteSpace(module.Version) ? (local?.VersionText ?? "") : $"v{module.Version}",
            Description = !string.IsNullOrWhiteSpace(module.Description) ? module.Description : (local?.Description ?? ""),
            Author = !string.IsNullOrWhiteSpace(module.Author) ? module.Author : (local?.Author ?? ""),
            License = !string.IsNullOrWhiteSpace(module.License) ? module.License : (local?.License ?? ""),
            Homepage = module.Homepage ?? local?.Homepage ?? "",
            PluginId = module.Id,
            SourceText = I18n.T("PluginsDetailSourceOfficial"),
            StateText = local?.StateText ?? I18n.T("PluginsOfficialStateNotInstalled"),
            TargetFramework = local?.TargetFramework ?? module.TargetFramework,
            CapabilitiesText = string.Join(I18n.T("PluginsEnumSeparator"), module.Capabilities),
            DependenciesText = local?.DependenciesText ?? "",
            InstallPath = local?.InstallPath ?? "",
            SignatureText = local?.SignatureText ?? "",
            HasPackageDigest = !string.IsNullOrWhiteSpace(module.Sha256),
            Sha256Text = string.IsNullOrWhiteSpace(module.Sha256) ? (local?.Sha256Text ?? "") : module.Sha256,
            Features = features,
        };
    }
}

internal static class PluginFeatureReader
{
    public static List<PluginFeatureItem> Read(PluginInstance instance)
    {
        try
        {
            string manifestPath = Path.Combine(instance.Directory, PluginPaths.ManifestFileName);
            if (!File.Exists(manifestPath)) return new List<PluginFeatureItem>();

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifestPath), new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            if (!document.RootElement.TryGetProperty("features", out JsonElement features) || features.ValueKind != JsonValueKind.Array)
                return new List<PluginFeatureItem>();

            var result = new List<PluginFeatureItem>();
            foreach (JsonElement item in features.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                string id = ReadString(item, "id");
                string name = ReadString(item, "name");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) continue;
                result.Add(new PluginFeatureItem
                {
                    Id = id,
                    Name = PluginDisplayText.ReadProperty(item, "name", name),
                    Description = PluginDisplayText.ReadProperty(item, "description", ReadString(item, "description")),
                    Category = PluginDisplayText.ReadProperty(item, "category", ReadString(item, "category")),
                    IconKey = ReadNullableString(item, "iconKey"),
                });
            }
            return result;
        }
        catch (Exception ex)
        {
            AppLogger.LogWarn($"[plugin] 读取插件功能清单失败 {instance.PluginId}：{ex.Message}");
            return new List<PluginFeatureItem>();
        }
    }

    private static string ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static string? ReadNullableString(JsonElement element, string name)
    {
        string value = ReadString(element, name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
