using System;

namespace WinPieGestures.Plugins;

/// <summary>官方插件商店列表项，仅承载目录展示状态，不把网络逻辑塞进 WPF 绑定对象。</summary>
/// <remarks>
/// 文案一律走 <see cref="I18n"/>：本类型整体由 <c>RenderOfficialPluginItems()</c> 每次刷新时重建，
/// 所以切换语言后只要重新绑定数据源就能换掉。**不要**改成字段缓存 —— 那会让语言切换后卡片仍是旧语言。
/// </remarks>
internal sealed class OfficialPluginListItem
{
    public OfficialPluginModule Module { get; }
    public string DisplayName => Module.Name;
    public string VersionText => string.IsNullOrWhiteSpace(Module.Version) ? "" : $"v{Module.Version}";
    public string SummaryText => $"{Module.Id}　|　{(Module.TypeClaims.Count == 0
        ? I18n.T("PluginsOfficialSummaryFallback")
        : string.Join(I18n.T("PluginsOfficialClaimSeparator"), Module.TypeClaims))}";
    public string StateText { get; }
    public string InstallButtonText { get; }
    public bool CanInstall { get; }
    public bool IsInstalled { get; }

    public OfficialPluginListItem(OfficialPluginModule module, string? installedVersion)
    {
        Module = module;
        IsInstalled = !string.IsNullOrWhiteSpace(installedVersion);
        if (!IsInstalled)
        {
            StateText = I18n.T("PluginsOfficialStateNotInstalled");
            InstallButtonText = I18n.T("PluginsOfficialActionInstall");
            CanInstall = true;
        }
        else if (string.Equals(installedVersion, module.Version, StringComparison.OrdinalIgnoreCase))
        {
            StateText = I18n.T("PluginsOfficialStateUpToDate");
            InstallButtonText = I18n.T("PluginsOfficialActionInstalled");
            CanInstall = false;
        }
        else
        {
            StateText = I18n.TF("PluginsOfficialStateUpdateAvailable", installedVersion);
            InstallButtonText = I18n.T("PluginsOfficialActionUpdate");
            CanInstall = true;
        }
    }
}
