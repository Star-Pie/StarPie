using System;

namespace WinPieGestures.Plugins;

/// <summary>每次刷新从完整历史选择当前最高兼容版本。不将较旧版本冒充更新或自动降级。</summary>
internal sealed class OfficialPluginListItem
{
    public OfficialPluginModule Module { get; }
    public string DisplayName => Module.Name;
    public string VersionText => string.IsNullOrWhiteSpace(Module.Version) ? "" : $"v{Module.Version}";
    public string SummaryText => !string.IsNullOrWhiteSpace(Module.Description) ? Module.Description : Module.Id;
    public string StateText { get; }
    public string InstallButtonText { get; }
    public string DetailsText => I18n.T("PluginsDetailButton");
    public bool CanInstall { get; }
    public bool IsInstalled { get; }

    public OfficialPluginListItem(OfficialPluginCatalogEntry entry, string? installedVersion, OfficialPluginEnvironment? environment = null)
    {
        OfficialPluginSelection selection = OfficialPluginVersionSelector.Select(entry, environment);
        Module = selection.Display ?? new OfficialPluginModule { Id = entry.Id, Name = entry.Id };
        IsInstalled = !string.IsNullOrWhiteSpace(installedVersion);
        if (selection.Compatible == null)
        {
            StateText = selection.Blocked.Message;
            InstallButtonText = I18n.T("PluginsOfficialActionUnavailable");
            return;
        }
        if (!IsInstalled)
        {
            StateText = I18n.T("PluginsOfficialStateNotInstalled");
            InstallButtonText = I18n.T("PluginsOfficialActionInstall");
            CanInstall = true;
            return;
        }
        if (!SimpleVersion.TryParse(installedVersion, out SimpleVersion installed) || !SimpleVersion.TryParse(Module.Version, out SimpleVersion candidate))
        {
            StateText = I18n.T("PluginsCompatibilityInvalid");
            InstallButtonText = I18n.T("PluginsOfficialActionUnavailable");
            return;
        }
        int comparison = candidate.CompareTo(installed);
        if (comparison > 0)
        {
            StateText = I18n.TF("PluginsOfficialStateUpdateAvailable", installedVersion);
            InstallButtonText = I18n.T("PluginsOfficialActionUpdate");
            CanInstall = true;
        }
        else
        {
            StateText = I18n.T(comparison == 0 ? "PluginsOfficialStateCompatibleCurrent" : "PluginsOfficialStateInstalledNewer");
            InstallButtonText = I18n.T("PluginsOfficialActionInstalled");
        }
    }
}
