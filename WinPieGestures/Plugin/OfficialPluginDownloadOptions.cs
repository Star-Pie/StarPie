using System;

namespace WinPieGestures.Plugins;

/// <summary>
/// 官方核心插件下载通道选项快照（不可变模型）。
/// 在用户显式触发一键安装或重试时捕获，传导至目录获取、模块选择与包下载全链路。
/// </summary>
internal sealed class OfficialPluginDownloadOptions
{
    public const string ChannelGhFast = "ghfast";
    public const string ChannelGhProxy = "gh-proxy";
    public const string ChannelMirror = "mirror";
    public const string ChannelDirect = "direct";
    public const string ChannelAtomGit = "atomgit";

    /// <summary>
    /// 标准化后的下载通道标识：ghfast / gh-proxy / mirror / direct。
    /// </summary>
    public string Channel { get; }

    public OfficialPluginDownloadOptions(string? channel)
    {
        Channel = NormalizeChannel(channel);
    }

    /// <summary>
    /// 将配置字符串、历史别名或未知值规范化为四种标准通道之一。
    /// ghproxy/moeyy => ghfast；akams => gh-proxy；custom/未知 => direct。
    /// </summary>
    public static string NormalizeChannel(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return ChannelDirect;
        }

        return source.Trim().ToLowerInvariant() switch
        {
            "ghfast" or "ghproxy" or "moeyy" => ChannelGhFast,
            "gh-proxy" or "akams" => ChannelGhProxy,
            "mirror" => ChannelMirror,
            "direct" => ChannelDirect,
            "atomgit" => ChannelAtomGit,
            _ => ChannelDirect
        };
    }

    /// <summary>
    /// 借助已有的 UpdateManager.Instance.GetProxiedDownloadUrl 构造当前通道的传输 URL。
    /// 严格保留原有单一事实来源，不重复维护镜像域名表。
    /// </summary>
    public string GetProxiedUrl(string rawUrl)
    {
        if (Channel == ChannelAtomGit) throw new InvalidOperationException("AtomGit assets require release metadata resolution.");
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return string.Empty;
        }

        return UpdateManager.Instance.GetProxiedDownloadUrl(rawUrl, Channel);
    }

    /// <summary>
    /// 获取当前通道的用户可见本地化显示文本。
    /// </summary>
    public string GetChannelDisplayName()
    {
        return Channel switch
        {
            ChannelAtomGit => I18n.T("UpdateSourceAtomGit"),
            ChannelGhFast => I18n.T("UpdateProxyGhproxy"),
            ChannelGhProxy => I18n.T("UpdateProxyMoeyy"),
            ChannelMirror => I18n.T("UpdateProxyAkams"),
            _ => I18n.T("UpdateProxyDirect")
        };
    }
}
