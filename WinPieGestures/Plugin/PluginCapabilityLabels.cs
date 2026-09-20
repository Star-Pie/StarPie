using System.Collections.Generic;
using StarPie.Plugin;

namespace WinPieGestures.Plugins;

/// <summary>
/// <see cref="PluginCapability"/> → 安装确认页上那一行说明。
/// <para>
/// <b>为什么值得单独成一个类</b>：这份文案原先内联在
/// <c>SettingsWindow.DescribeCapabilities</c> 里，于是每加一个能力位都要靠人记得去补一行。
/// 事实上连着漏了两次 —— <c>WindowControl</c>（S4c）与 <c>ScreenCapture</c>（S4a）
/// 都是「为了在确认页上说清后果」才独立成项的，却从来没有在确认页上出现过：
/// 用户看到的那份风险清单里，两行字一直是空的。
/// </para>
/// <para>
/// 搬家只是第一步，真正让它不再重演的是自检 <c>[3j]</c> 段落里那条断言（见
/// <c>PluginSelfTest.cs</c>）：它会遍历 <see cref="PluginCapability"/> 的每一个成员，
/// 逐个要求这里有一行非空文案，再反向核对表里没有枚举已移除的位。
/// 再漏一次就会当场红，而不是等到用户手里。
/// </para>
/// <para>
/// <b>那段断言一度是假的</b>：它在一次合并中被整段顶掉（`refactor` 分支重写自检文件，
/// 合并整体取它），而本注释与 <c>AGENTS.md</c> §3.7 仍在引用它 —— 于是「有机器护栏」
/// 这句话在好几个版本里都没有对应物。2026-09-19 已恢复，并用变异测试验过它真的会红
/// （删掉本表里 <c>WindowControl</c> 那一行 ⇒ 自检报「能力位…没有对应文案」）。
/// 改动 <c>PluginSelfTest.cs</c> 时请按 AGENTS.md §5.1 的纪律比对段落号集合。
/// </para>
/// <para>
/// <b>文案现在走词条（2026-09-19 改）</b>。此前这里是硬编码中文，于是英文 / 日文界面上
/// 会出现「This plugin declares the following capabilities: · 启动进程 / 执行命令」这种
/// 中英混排 —— 而它藏在「已经接好 i18n 的确认页」里面，光看那页的代码发现不了。
/// 抓住它的是自检 <c>[3e]</c>：合成一份英文确认页，断言里面一个方块字都不该有。
/// </para>
/// </summary>
internal static class PluginCapabilityLabels
{
    /// <summary>
    /// 逐位列举。<b>顺序即确认页上的显示顺序</b>，与枚举声明顺序保持一致（由轻到重）。
    /// <para>
    /// 每一行都要能回答「用户看到这行字，脑子里出现的后果是否就是插件真会做的事」。
    /// 写不出这一行，通常意味着那个能力位本身该合并 —— 而写得出、却和别的一行说的是同一件事，
    /// 说明它该独立。
    /// </para>
    /// <para>
    /// 这里存的是<b>键</b>而不是文案：本表是 <c>static readonly</c>，在类型初始化时求值一次。
    /// 若把 <c>I18n.T(…)</c> 的结果直接存进来，运行中切换语言后确认页仍是旧语言 ——
    /// 而且是那种「重启就好」的偶发症状。所以一律在 <see cref="Describe"/> 里现取。
    /// </para>
    /// </summary>
    internal static readonly (PluginCapability Capability, string Key)[] All =
    {
        (PluginCapability.Process, "PluginCapabilityProcess"),
        (PluginCapability.FileSystem, "PluginCapabilityFileSystem"),
        (PluginCapability.Network, "PluginCapabilityNetwork"),
        (PluginCapability.Clipboard, "PluginCapabilityClipboard"),
        (PluginCapability.Registry, "PluginCapabilityRegistry"),
        (PluginCapability.GlobalHook, "PluginCapabilityGlobalHook"),
        (PluginCapability.Ui, "PluginCapabilityUi"),
        (PluginCapability.Admin, "PluginCapabilityAdmin"),

        // 下面三行是「后果可能在别的程序里发生」的三项，措辞刻意用具象动词：
        // 「移动窗口」比「窗口控制」更早让人想到自己正在做的事被打断。
        (PluginCapability.WindowControl, "PluginCapabilityWindowControl"),
        (PluginCapability.ScreenCapture, "PluginCapabilityScreenCapture"),
        (PluginCapability.InputSimulation, "PluginCapabilityInputSimulation"),
    };

    /// <summary>把一组能力位拼成确认页上的多行文本；没有任何能力时返回「（无）」（同样走词条）。</summary>
    internal static string Describe(PluginCapability capabilities)
    {
        var parts = new List<string>();

        foreach ((PluginCapability capability, string key) in All)
        {
            if (capabilities.HasFlag(capability)) parts.Add(I18n.T(key));
        }

        return parts.Count == 0 ? I18n.T("PluginCapabilityNone") : string.Join("\n", parts);
    }

    /// <summary>
    /// 把一组能力位拆成<b>逐个</b>标签（插件卡片上那一排小标签用），顺序与 <see cref="All"/> 一致。
    /// <para>
    /// 与 <see cref="Describe"/> 的分工：那个把多项拼成确认页要读的一整段多行文本，
    /// 这个交给界面逐项渲染成独立标签。之所以不写成 <c>Describe(...).Split('\n')</c> ——
    /// 那会把「换行即分隔符」变成一个只存在于人脑里的约定，以后谁在某条文案里换一次行，
    /// 卡片上就会莫名多出一个空标签。
    /// </para>
    /// <para>
    /// 卡片上的能力名此前是直接拼 <c>PluginRegistryEntry.CapabilitiesAck</c> 的原始字符串
    /// （形如 <c>Process</c> / <c>WindowControl</c>），于是中文界面里显示的是英文枚举名 ——
    /// 一个既不空白、也不像错的值，扫代码很难发现。改由本方法按能力位取词条后消失。
    /// </para>
    /// </summary>
    internal static IReadOnlyList<string> DescribeTags(PluginCapability capabilities)
    {
        var tags = new List<string>();

        foreach ((PluginCapability capability, string key) in All)
        {
            if (capabilities.HasFlag(capability)) tags.Add(I18n.T(key));
        }

        return tags;
    }
}
