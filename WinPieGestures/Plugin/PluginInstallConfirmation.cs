using System.Text;

namespace WinPieGestures.Plugins;

/// <summary>
/// 安装确认页的输入 —— 把「扫描目录候选」与「手动选 <c>.dll</c>」两条路归一。
/// <para>
/// 两条路只在三处不同：<see cref="State"/>（装下去是新建，还是覆盖什么样的旧版）、
/// <see cref="EnableAfterInstall"/>（装完是否立即启用）、以及 <see cref="Note"/>
/// （扫描目录给出的处境说明；手动安装没有这一维，留空串）。
/// </para>
/// <para>
/// <b>为什么要有这个类型</b>：确认页原先有两份独立实现，各自从自己的数据源（<c>PluginCandidate</c>
/// 与 <c>PluginScanResult</c>）里现场取字段拼正文。两份实现在信息、措辞、i18n 接入程度上全都不同步。
/// 归一成一个显式输入之后，「同一个确认语义只有一条路」这件事才有地方可验。
/// </para>
/// </summary>
internal sealed class PluginInstallConfirmation
{
    /// <summary>已经识别通过的扫描结果。调用方必须先确认 <c>Accepted</c>，本类型不再校验。</summary>
    public PluginScanResult Scan { get; init; } = new();

    public PluginCandidateState State { get; init; } = PluginCandidateState.Installable;

    /// <summary>处境说明，渲染在「扫描结果」一行。为空则不显示该行。</summary>
    public string Note { get; init; } = "";

    /// <summary>
    /// 装完是否立即启用。
    /// <para>
    /// <b>必须与真正落盘时传给 <c>CommitInstallAsync</c> 的值一致</b>：这里说「装完立即启用」
    /// 而落盘时传 <c>false</c>，用户看到的承诺与结果就是相反的，且没有任何报错。
    /// </para>
    /// </summary>
    public bool EnableAfterInstall { get; init; }
}

/// <summary>
/// 安装确认页正文的构造 —— 两条安装路径共用这一份。
/// <para>
/// 本页是唯一的知情同意关口：插件会以 StarPie 的权限在进程内跑代码，所以正文必须把
/// 「它是谁 / 从哪来 / 会拿到什么能力 / 装到哪 / 装完会怎样」摊开给用户看，
/// 而不是只问一句「确定吗」。
/// </para>
/// <para>
/// 刻意做成<b>不碰控件、不弹窗的纯函数</b>：这样它才能在无界面自检里被逐语言驱动，
/// 「切到英文后这一页还剩下多少中文」才有可能被机器断言 —— 而不是靠人肉切语言点一遍。
/// 这条断言值得存在，因为前几轮 i18n 漏接全都是「编译、静态检查、词表覆盖率全绿，
/// 界面上仍是原文」这一种，而它恰好只在切语言之后才看得见。
/// </para>
/// </summary>
internal static class PluginInstallConfirmationText
{
    public static string Build(PluginInstallConfirmation info)
    {
        PluginScanResult scan = info.Scan;
        StarPie.Plugin.PluginManifest? manifest = scan.Manifest;
        StarPie.Plugin.PluginCapability capabilities =
            manifest?.ResolveCapabilities() ?? StarPie.Plugin.PluginCapability.None;

        string pluginId = manifest?.Id ?? "";
        string displayName = !string.IsNullOrWhiteSpace(manifest?.Name)
            ? manifest!.Name!
            : System.IO.Path.GetFileName(scan.DllPath);
        string versionText = string.IsNullOrWhiteSpace(manifest?.Version) ? "" : $"v{manifest.Version}";

        var text = new StringBuilder();

        // ① 它是谁
        // 版本号未知时不该留一个空占位把行尾拖出空格，所以整句 TrimEnd 一次。
        text.AppendLine(I18n.TF("PluginsConfirmAboutToInstall", displayName, versionText).TrimEnd());
        text.AppendLine(I18n.TF("PluginsConfirmPluginId", pluginId));
        if (!string.IsNullOrWhiteSpace(manifest?.Author))
        {
            text.AppendLine(I18n.TF("PluginsConfirmAuthor", manifest!.Author!));
        }
        if (!string.IsNullOrWhiteSpace(manifest?.Description))
        {
            text.AppendLine(I18n.TF("PluginsConfirmDescription", manifest!.Description!));
        }
        text.AppendLine();

        // ② 从哪来、是什么文件
        text.AppendLine(I18n.TF("PluginsConfirmFile", scan.DllPath));
        if (!string.IsNullOrWhiteSpace(scan.TargetFramework))
        {
            text.AppendLine(I18n.TF("PluginsConfirmTargetFramework", scan.TargetFramework));
        }
        if (!string.IsNullOrWhiteSpace(scan.MachineText))
        {
            text.AppendLine(I18n.TF("PluginsConfirmMachine", scan.MachineText));
        }
        if (!string.IsNullOrWhiteSpace(scan.FileSizeText))
        {
            text.AppendLine(I18n.TF("PluginsConfirmFileSize", scan.FileSizeText));
        }
        if (!string.IsNullOrWhiteSpace(scan.Sha256Short))
        {
            text.AppendLine(I18n.TF("PluginsConfirmSha256", scan.Sha256Short));
        }
        text.AppendLine(I18n.TF("PluginsConfirmSignature",
            scan.IsSigned ? scan.SignerSubject ?? "" : I18n.T("PluginsConfirmUnsigned")));
        if (!string.IsNullOrWhiteSpace(scan.ManifestSource))
        {
            text.AppendLine(I18n.TF("PluginsConfirmManifestSource", scan.ManifestSource));
        }
        text.AppendLine();

        if (!string.IsNullOrWhiteSpace(info.Note))
        {
            text.AppendLine(I18n.TF("PluginsConfirmScanResult", info.Note));
            text.AppendLine();
        }

        // ③ 会拿到什么能力
        // 能力刻意写两遍：一行是清单里的原始 ID（供用户对照 plugin.json 核对是同一回事），
        // 一段是机器人话的风险描述（供真正要判断的人看）。少掉前一行，用户就没法确认
        // 自己看到的风险条目与清单里声明的对得上。
        // 连接符必须走词条：顿号是中文标点，英文里得是逗号。
        text.AppendLine(I18n.TF("PluginsConfirmDeclaredCapabilities",
            manifest?.Capabilities is { Count: > 0 } declared
                ? string.Join(I18n.T("PluginsEnumSeparator"), declared)
                : I18n.T("PluginsConfirmNoCapabilities")));
        if (capabilities != StarPie.Plugin.PluginCapability.None)
        {
            text.AppendLine(I18n.T("PluginsConfirmCapabilities"));
            text.AppendLine(PluginCapabilityLabels.Describe(capabilities));
        }
        text.AppendLine();

        // ④ 装到哪、装完会怎样
        text.AppendLine(I18n.TF("PluginsConfirmTargetPath", $"{PluginPaths.Root}\\{pluginId}"));
        text.AppendLine();
        text.AppendLine(DescribeOutcome(info.State));
        text.AppendLine(I18n.T(info.EnableAfterInstall ? "PluginsConfirmEnableNow" : "PluginsConfirmEnableLater"));
        text.AppendLine();

        // ⑤ 风险与接受
        text.AppendLine(I18n.T("PluginsConfirmSecurityTitle"));
        text.AppendLine(I18n.T("PluginsConfirmSecurityBody"));
        text.Append(I18n.T("PluginsConfirmAccept"));

        return text.ToString();
    }

    /// <summary>
    /// 「装下去会覆盖掉什么」那句话，与 <see cref="PluginCandidateState"/> 一一对应。
    /// <para>
    /// 刻意与「装完是否立即启用」拆成两句：覆盖了哪一份、装完启不启用是两件独立的事，
    /// 写进一句话里就没法单独改一条。
    /// </para>
    /// <para>
    /// 兜底分支给 <see cref="PluginCandidateState.Replaced"/> 而不是抛异常：候选路径不为
    /// <c>Duplicate</c> / <c>Reserved</c> / <c>Rejected</c> 显示安装按钮，手动路径的「识别未通过」
    /// 也在更早的分支返回了 —— 三种都到不了这里。真到了（将来多一个状态位忘了接）也宁可少说一句
    /// 而不是让确认页弹不出来；自检里的 <c>[3e]</c> 守着「每个状态位都有对应文案」。
    /// </para>
    /// </summary>
    public static string DescribeOutcome(PluginCandidateState state) => state switch
    {
        PluginCandidateState.Installable => I18n.T("PluginsConfirmActFresh"),
        PluginCandidateState.Update => I18n.T("PluginsConfirmActUpdate"),
        PluginCandidateState.Downgrade => I18n.T("PluginsConfirmActDowngrade"),
        PluginCandidateState.Replaced => I18n.T("PluginsConfirmActReplaced"),
        PluginCandidateState.Installed => I18n.T("PluginsConfirmActInstalled"),
        PluginCandidateState.VersionUnknown => I18n.T("PluginsConfirmActVersionUnknown"),
        PluginCandidateState.ExternalRegistered => I18n.T("PluginsConfirmActExternal"),
        _ => I18n.T("PluginsConfirmActReplaced"),
    };
}
