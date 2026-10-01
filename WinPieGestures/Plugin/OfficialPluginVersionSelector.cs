using System;
using System.Linq;
using System.Text.RegularExpressions;
using StarPie.Plugin;

namespace WinPieGestures.Plugins;

internal readonly record struct OfficialPluginEnvironment(string HostVersion, string ApiVersion, string TargetFramework)
{
    internal static OfficialPluginEnvironment Current => new(PluginManifestReader.HostVersion, PluginApi.ApiVersion, PluginManifestReader.HostTargetFramework);
}

internal enum OfficialPluginCompatibilityKind { Compatible, HostTooOld, HostTooNew, ApiUnsupported, FrameworkUnsupported, InvalidMetadata }

internal readonly record struct OfficialPluginCompatibility(OfficialPluginCompatibilityKind Kind, string Required = "", string Actual = "")
{
    internal bool IsCompatible => Kind == OfficialPluginCompatibilityKind.Compatible;
    internal string Message => Kind switch
    {
        OfficialPluginCompatibilityKind.Compatible => "",
        OfficialPluginCompatibilityKind.HostTooOld => I18n.TF("PluginsCompatibilityHostOld", Required, Actual),
        OfficialPluginCompatibilityKind.HostTooNew => I18n.TF("PluginsCompatibilityHostNew", Required, Actual),
        OfficialPluginCompatibilityKind.ApiUnsupported => I18n.TF("PluginsCompatibilityApi", Required, Actual),
        OfficialPluginCompatibilityKind.FrameworkUnsupported => I18n.TF("PluginsCompatibilityFramework", Required, Actual),
        _ => I18n.T("PluginsCompatibilityInvalid"),
    };
}

internal readonly record struct OfficialPluginSelection(OfficialPluginModule? Compatible, OfficialPluginModule? Latest, OfficialPluginCompatibility Blocked)
{
    internal OfficialPluginModule? Display => Compatible ?? Latest;
}

/// <summary>纯选择模块：完整目录和当前环境输入，输出最高兼容发布。不下载，不修改缓存或安装状态。</summary>
internal static class OfficialPluginVersionSelector
{
    internal static bool HasValidRequirements(OfficialPluginModule module) =>
        SimpleVersion.TryParse(module.MinHostVersion, out SimpleVersion minimum) &&
        !string.IsNullOrWhiteSpace(module.ApiVersion) && Regex.IsMatch(module.ApiVersion, @"^[0-9]+\.[0-9]+$") && SimpleVersion.TryParse(module.ApiVersion, out _) &&
        TargetFrameworkInfo.TryParse(module.TargetFramework, out _) &&
        (string.IsNullOrWhiteSpace(module.MaxHostVersion) ||
            (SimpleVersion.TryParse(module.MaxHostVersion, out SimpleVersion maximum) && maximum.CompareTo(minimum) >= 0));

    internal static OfficialPluginCompatibility Evaluate(OfficialPluginModule module, OfficialPluginEnvironment? environment = null)
    {
        OfficialPluginEnvironment host = environment ?? OfficialPluginEnvironment.Current;
        if (!HasValidRequirements(module) || !SimpleVersion.TryParse(host.HostVersion, out SimpleVersion hostVersion) ||
            !SimpleVersion.TryParse(host.ApiVersion, out SimpleVersion hostApi) || !TargetFrameworkInfo.TryParse(host.TargetFramework, out TargetFrameworkInfo hostFramework))
            return new(OfficialPluginCompatibilityKind.InvalidMetadata);
        SimpleVersion.TryParse(module.MinHostVersion, out SimpleVersion minimum);
        if (!SimpleVersion.SatisfiesMinimum(hostVersion, minimum))
            return new(OfficialPluginCompatibilityKind.HostTooOld, module.MinHostVersion, host.HostVersion);
        if (!string.IsNullOrWhiteSpace(module.MaxHostVersion) && SimpleVersion.TryParse(module.MaxHostVersion, out SimpleVersion maximum) && hostVersion.CompareTo(maximum) > 0)
            return new(OfficialPluginCompatibilityKind.HostTooNew, module.MaxHostVersion!, host.HostVersion);
        SimpleVersion.TryParse(module.ApiVersion, out SimpleVersion api);
        if (api.Major != hostApi.Major || api.Minor > hostApi.Minor)
            return new(OfficialPluginCompatibilityKind.ApiUnsupported, module.ApiVersion, host.ApiVersion);
        TargetFrameworkInfo.TryParse(module.TargetFramework, out TargetFrameworkInfo framework);
        if (!TargetFrameworkInfo.IsCompatible(framework, hostFramework))
            return new(OfficialPluginCompatibilityKind.FrameworkUnsupported, module.TargetFramework, host.TargetFramework);
        return new(OfficialPluginCompatibilityKind.Compatible);
    }

    internal static OfficialPluginSelection Select(OfficialPluginCatalogEntry entry, OfficialPluginEnvironment? environment = null)
    {
        OfficialPluginModule? best = null, latest = null;
        SimpleVersion bestVersion = default, latestVersion = default;
        foreach (OfficialPluginModule module in entry.Versions ?? new())
        {
            if (module == null || !SimpleVersion.TryParse(module.Version, out SimpleVersion version)) continue;
            module.Id = entry.Id;
            if (latest == null || version.CompareTo(latestVersion) > 0) { latest = module; latestVersion = version; }
            if (Evaluate(module, environment).IsCompatible && (best == null || version.CompareTo(bestVersion) > 0))
            { best = module; bestVersion = version; }
        }
        return new(best, latest, best != null ? new(OfficialPluginCompatibilityKind.Compatible) :
            latest != null ? Evaluate(latest, environment) : new(OfficialPluginCompatibilityKind.InvalidMetadata));
    }
}
