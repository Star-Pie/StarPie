using System;
using System.Collections.Generic;

namespace WinPieGestures.Plugins;

/// <summary>官方插件市场页的卡片视图模型，仅承载目录展示状态，不把网络逻辑塞进 WPF 绑定对象。</summary>
/// <remarks>
/// <para>
/// 文案一律走 <see cref="I18n"/>：本类型整体由 <c>RenderOfficialPluginItems()</c> 每次刷新时重建，
/// 所以切换语言后只要重新绑定数据源就能换掉。**不要**改成字段缓存 —— 那会让语言切换后卡片仍是旧语言。
/// </para>
/// <para>
/// <b>页面从「插件页里的一个 MaxHeight=220 面板」搬成独立页之后</b>，卡片从窄条变成了双列网格单元，
/// 于是多了头像、ID 行与哈希行三块。这三块都<strong>不引入新词条</strong>：
/// 头像走 <see cref="PluginListItem.ResolveAvatar"/> 的退化逻辑，ID 与 SHA-256 是国际通用标识符
/// （与 <c>PluginListItem.DetailText</c> 同一条约定），只有「没有认领任何动作类型」时的兜底
/// 与状态/按钮文案需要查表。
/// </para>
/// </remarks>
internal sealed class OfficialPluginListItem
{
    public OfficialPluginModule Module { get; }

    public string DisplayName => Module.Name;

    /// <summary>模块 ID（<c>starpie.*</c>），网格卡片上单独一行小字。ASCII，不翻译。</summary>
    public string IdText => Module.Id;

    /// <summary>① 头像方块里的字形。官方模块没有清单图标字段，一律退化为名称首字。</summary>
    public string AvatarText { get; }

    /// <summary>形如 <c>v1.2.0</c>；未知版本时为空串，卡片上不占位。</summary>
    public string VersionText => string.IsNullOrWhiteSpace(Module.Version) ? "" : $"v{Module.Version}";

    /// <summary>
    /// 这个模块认领了哪些动作类型（即装上之后会从类型下拉里接管哪些动作）。
    /// <para>
    /// 用列表而不是拼成一行：独立页改成双列网格后，一行拼串会在窄卡片里折成两三行且看不出边界；
    /// 逐项标签才能换行排列。没有任何认领时退化成一句兜底说明（而非空列表）——
    /// 空列表会让那一块在卡片上留一片无法解释的空白。
    /// </para>
    /// </summary>
    public IReadOnlyList<string> ClaimTags { get; }

    /// <summary>次要标识：包哈希前 12 位。概念稿这里放的是「⭐ 评分 / 安装数」，而 catalog 里没有这两项。</summary>
    public string MetaText { get; }

    /// <summary>状态徽标文案（未安装 / 已是最新 / 已装 vX · 有更新）。</summary>
    public string StateText { get; }

    public string InstallButtonText { get; }

    public bool CanInstall { get; }

    public bool IsInstalled { get; }

    /// <summary>已装且版本落后 —— 市场页「可更新」这个筛选项的判据。</summary>
    public bool HasUpdate => IsInstalled && CanInstall;

    public OfficialPluginListItem(OfficialPluginModule module, string? installedVersion)
    {
        Module = module;
        IsInstalled = !string.IsNullOrWhiteSpace(installedVersion);

        AvatarText = PluginListItem.ResolveAvatar(null, module.Name);

        var claims = new List<string>();
        foreach (string claim in module.TypeClaims)
        {
            if (!string.IsNullOrWhiteSpace(claim)) claims.Add(claim);
        }
        ClaimTags = claims.Count > 0 ? claims : new[] { I18n.T("PluginsOfficialSummaryFallback") };

        // 哈希理论上总是 64 位；取前 12 位显示。长度不够时整行留空，而不是把一截残哈希当完整值摆出来。
        string hash = module.Sha256;
        MetaText = hash.Length >= 12 ? $"SHA256 {hash[..12]}" : "";

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
