using System;
using System.Collections.Generic;
using System.Text;
using StarPie.Plugin;

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

/// <summary>确认页里一行的种类，决定窗口画不画行首标记（✔ / ⚠️）以及用什么颜色。</summary>
internal enum PluginConfirmLineKind
{
    /// <summary>普通信息行（标签 + 值）。</summary>
    Normal = 0,

    /// <summary>一条能力。窗口会画成「✔ 移动窗口 / 改变你正在使用的窗口」。</summary>
    Capability = 1,

    /// <summary>需要用户多看一眼的一行（例如「装下去会覆盖掉什么」）。窗口会加一个 ⚠️。</summary>
    Notice = 2,
}

/// <summary>确认页里的一行。</summary>
internal sealed class PluginConfirmLine
{
    public PluginConfirmLine(string text, PluginConfirmLineKind kind = PluginConfirmLineKind.Normal)
    {
        Text = text;
        Kind = kind;
    }

    /// <summary>已经按当前语言取好的文案。</summary>
    public string Text { get; }

    public PluginConfirmLineKind Kind { get; }
}

/// <summary>
/// 确认页的一个分区（窗口里渲染成一张卡片）。
/// <para>
/// <b>为什么要有分区这一层</b>：纯文本正文在 <c>MessageBox</c> 里读得下去，摊进一张自绘窗口就散架了 ——
/// 十来行等权的文字挤在一个滚动区里，用户找不到「它会拿到什么能力」那一段，
/// 而那段恰恰是这一页存在的理由。分区让「身份 / 来源 / 能力 / 装到哪 / 风险」各自成为一个可跳读的块，
/// 能力行还能逐条加行首标记。
/// </para>
/// <para>
/// <see cref="TitleKey"/> 与 <see cref="Title"/> 并存是刻意的：自检要断言「每个分区都有词条、
/// 分区标题互不相同」，而那种断言只能在**键**上做 —— 拿译文比等于把断言绑到某一种语言上。
/// </para>
/// </summary>
internal sealed class PluginConfirmSection
{
    /// <summary>分区标题的词条键。空串表示这一段刻意没有标题（目前只有「扫描结果」那一段）。</summary>
    public string TitleKey { get; init; } = "";

    /// <summary>已按当前语言取好的标题；<see cref="TitleKey"/> 为空时也是空串。</summary>
    public string Title { get; init; } = "";

    public IReadOnlyList<PluginConfirmLine> Lines { get; init; } = Array.Empty<PluginConfirmLine>();
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
/// <para>
/// <b>一条正文，两种消费方式</b>：窗口按 <see cref="BuildSections"/> 逐区渲染；
/// 「复制详情」按钮与自检走 <see cref="Build"/>（把分区展平成纯文本）。
/// 两者必须同源，否则窗口里显示的与用户复制走的内容会各说各话 ——
/// 展平函数只有一个，所以这件事在结构上成立。
/// </para>
/// </summary>
internal static class PluginInstallConfirmationText
{
    /// <summary>「扫描结果」那一段刻意不设标题：它只有一行，自己就说明了自己是什么。</summary>
    public const string UntitledSectionKey = "";

    /// <summary>
    /// 安全提示那一段的标题键。
    /// <para>
    /// 暴露成常量是因为<b>有两个消费者要认这一段</b>：自检 <c>[3e]</c> 断言「最后一段是它」，
    /// 窗口要把它从滚动区里摘出来固定到「安装」按钮上方（内容一多它就滚没了，
    /// 而它正是用户按下按钮前必须看到的最后一句）。两处各写一遍字面量，
    /// 改一处漏一处时**没有任何报错** —— 自检继续绿，只有窗口上少了一块风险提示。
    /// </para>
    /// </summary>
    public const string SecuritySectionKey = "PluginsConfirmSecurityTitle";

    /// <summary>
    /// 把正文拆成分区。窗口逐区渲染，<see cref="Build"/> 逐区展平。
    /// <para>
    /// 顺序即阅读顺序，且<b>最后一段必须是安全提示</b>：那一段要挨着「安装」按钮，
    /// 让用户在按下之前最后看到的是风险而不是文件哈希。自检 <c>[3e]</c> 守着这一条。
    /// </para>
    /// </summary>
    public static IReadOnlyList<PluginConfirmSection> BuildSections(PluginInstallConfirmation info)
    {
        PluginScanResult scan = info.Scan;
        PluginManifest? manifest = scan.Manifest;
        PluginCapability capabilities = manifest?.ResolveCapabilities() ?? PluginCapability.None;

        string pluginId = manifest?.Id ?? "";
        string displayName = !string.IsNullOrWhiteSpace(manifest?.Name)
            ? manifest!.Name!
            : System.IO.Path.GetFileName(scan.DllPath);
        string versionText = string.IsNullOrWhiteSpace(manifest?.Version) ? "" : $"v{manifest.Version}";

        var sections = new List<PluginConfirmSection>();

        // ① 它是谁
        // 版本号未知时不该留一个空占位把行尾拖出空格，所以整句 TrimEnd 一次。
        var identity = new List<PluginConfirmLine>
        {
            new(I18n.TF("PluginsConfirmAboutToInstall", displayName, versionText).TrimEnd()),
            new(I18n.TF("PluginsConfirmPluginId", pluginId)),
        };
        if (!string.IsNullOrWhiteSpace(manifest?.Author))
        {
            identity.Add(new PluginConfirmLine(I18n.TF("PluginsConfirmAuthor", manifest!.Author!)));
        }
        if (!string.IsNullOrWhiteSpace(manifest?.Description))
        {
            identity.Add(new PluginConfirmLine(I18n.TF("PluginsConfirmDescription", manifest!.Description!)));
        }
        sections.Add(new PluginConfirmSection
        {
            TitleKey = "PluginsConfirmSectionIdentity",
            Title = I18n.T("PluginsConfirmSectionIdentity"),
            Lines = identity,
        });

        // ② 从哪来、是什么文件
        var source = new List<PluginConfirmLine> { new(I18n.TF("PluginsConfirmFile", scan.DllPath)) };
        if (!string.IsNullOrWhiteSpace(scan.TargetFramework))
        {
            source.Add(new PluginConfirmLine(I18n.TF("PluginsConfirmTargetFramework", scan.TargetFramework)));
        }
        if (!string.IsNullOrWhiteSpace(scan.MachineText))
        {
            source.Add(new PluginConfirmLine(I18n.TF("PluginsConfirmMachine", scan.MachineText)));
        }
        if (!string.IsNullOrWhiteSpace(scan.FileSizeText))
        {
            source.Add(new PluginConfirmLine(I18n.TF("PluginsConfirmFileSize", scan.FileSizeText)));
        }
        if (!string.IsNullOrWhiteSpace(scan.Sha256Short))
        {
            source.Add(new PluginConfirmLine(I18n.TF("PluginsConfirmSha256", scan.Sha256Short)));
        }
        source.Add(new PluginConfirmLine(I18n.TF("PluginsConfirmSignature",
            scan.IsSigned ? scan.SignerSubject ?? "" : I18n.T("PluginsConfirmUnsigned"))));
        if (!string.IsNullOrWhiteSpace(scan.ManifestSource))
        {
            source.Add(new PluginConfirmLine(I18n.TF("PluginsConfirmManifestSource", scan.ManifestSource)));
        }
        sections.Add(new PluginConfirmSection
        {
            TitleKey = "PluginsConfirmSectionSource",
            Title = I18n.T("PluginsConfirmSectionSource"),
            Lines = source,
        });

        // ③ 扫描目录给出的处境说明（只有扫描路径有这一维）
        if (!string.IsNullOrWhiteSpace(info.Note))
        {
            sections.Add(new PluginConfirmSection
            {
                TitleKey = UntitledSectionKey,
                Title = "",
                Lines = new[] { new PluginConfirmLine(I18n.TF("PluginsConfirmScanResult", info.Note)) },
            });
        }

        // ④ 会拿到什么能力
        //
        // 能力刻意写两遍：一行是清单里的原始 ID（供用户对照 plugin.json 核对是同一回事），
        // 一段是机器人话的风险描述（供真正要判断的人看）。少掉前一行，用户就没法确认
        // 自己看到的风险条目与清单里声明的对得上。
        // 连接符必须走词条：顿号是中文标点，英文里得是逗号。
        var capabilityLines = new List<PluginConfirmLine>
        {
            new(I18n.TF("PluginsConfirmDeclaredCapabilities",
                manifest?.Capabilities is { Count: > 0 } declared
                    ? string.Join(I18n.T("PluginsEnumSeparator"), declared)
                    : I18n.T("PluginsConfirmNoCapabilities"))),
        };
        if (capabilities != PluginCapability.None)
        {
            capabilityLines.Add(new PluginConfirmLine(I18n.T("PluginsConfirmCapabilities")));

            // 逐条成行（而不是把 Describe 的多行文本塞进一行）：窗口按 Kind 画行首 ✔，
            // 用户扫读的粒度就是「一项能力一行」。自检 [3e] 断言这里的行数与能力位数相等 ——
            // 一旦有人把它们 join 起来，那个数会立刻变成 1。
            foreach (string capabilityText in PluginCapabilityLabels.DescribeTags(capabilities))
            {
                capabilityLines.Add(new PluginConfirmLine(capabilityText, PluginConfirmLineKind.Capability));
            }
        }
        sections.Add(new PluginConfirmSection
        {
            TitleKey = "PluginsConfirmSectionCapabilities",
            Title = I18n.T("PluginsConfirmSectionCapabilities"),
            Lines = capabilityLines,
        });

        // ⑤ 装到哪、装完会怎样
        var target = new List<PluginConfirmLine>
        {
            new(I18n.TF("PluginsConfirmTargetPath", $"{PluginPaths.Root}\\{pluginId}")),
            new(DescribeOutcome(info.State), PluginConfirmLineKind.Notice),
            new(I18n.T(info.EnableAfterInstall ? "PluginsConfirmEnableNow" : "PluginsConfirmEnableLater")),
        };
        sections.Add(new PluginConfirmSection
        {
            TitleKey = "PluginsConfirmSectionTarget",
            Title = I18n.T("PluginsConfirmSectionTarget"),
            Lines = target,
        });

        // ⑥ 风险与接受。**必须是最后一段**（见方法注释）。
        sections.Add(new PluginConfirmSection
        {
            TitleKey = SecuritySectionKey,
            Title = I18n.T("PluginsConfirmSecurityTitle"),
            Lines = new[]
            {
                new PluginConfirmLine(I18n.T("PluginsConfirmSecurityBody"), PluginConfirmLineKind.Notice),
                new PluginConfirmLine(I18n.T("PluginsConfirmAccept")),
            },
        });

        return sections;
    }

    /// <summary>
    /// 把 <see cref="BuildSections"/> 展平成纯文本 —— 窗口上「复制详情」复制的就是这一份。
    /// <para>
    /// 保留这个纯文本形态有两个理由：① 用户可以把整页粘进 issue / 聊天里求助；
    /// ② 无界面自检 <c>[3e]</c> 一直按字符串断言（占位符、长度、英文无方块字、四语言互不相同），
    /// 有了展平函数它就不必跟着窗口的布局改 —— 而窗口改版是家常便饭。
    /// </para>
    /// <para>
    /// 分区之间空一行，标题在分区首行。与窗口上看到的**信息完全同源**，
    /// 差别只在窗口会给能力行加 ✔。
    /// </para>
    /// </summary>
    public static string Build(PluginInstallConfirmation info)
    {
        var blocks = new List<string>();
        foreach (PluginConfirmSection section in BuildSections(info))
        {
            var lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(section.Title)) lines.Add(section.Title);
            foreach (PluginConfirmLine line in section.Lines) lines.Add(line.Text);
            blocks.Add(string.Join("\n", lines));
        }

        return string.Join("\n\n", blocks);
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
