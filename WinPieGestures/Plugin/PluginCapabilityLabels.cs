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
/// <b>文案是硬编码中文</b>，与搬家之前一致 —— 这不是遗漏，本次不做 i18n 改造，
/// 免得把「补两个能力标签」变成一次文案迁移。
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
    /// </summary>
    internal static readonly (PluginCapability Capability, string Text)[] All =
    {
        (PluginCapability.Process, "· 启动进程 / 执行命令"),
        (PluginCapability.FileSystem, "· 读写你的文件"),
        (PluginCapability.Network, "· 访问网络"),
        (PluginCapability.Clipboard, "· 读取或修改剪贴板"),
        (PluginCapability.Registry, "· 读写注册表"),
        (PluginCapability.GlobalHook, "· 安装全局键盘/鼠标钩子"),
        (PluginCapability.Ui, "· 显示界面与通知"),
        (PluginCapability.Admin, "· 需要管理员权限"),

        // 下面三行是「后果可能在别的程序里发生」的三项，措辞刻意用具象动词：
        // 「移动窗口」比「窗口控制」更早让人想到自己正在做的事被打断。
        (PluginCapability.WindowControl, "· 移动 / 置顶 / 改变你正在使用的窗口"),
        (PluginCapability.ScreenCapture, "· 读取屏幕内容（截屏）"),
        (PluginCapability.InputSimulation, "· 向当前窗口发送按键"),
    };

    /// <summary>把一组能力位拼成确认页上的多行文本；没有任何能力时返回「（无）」。</summary>
    internal static string Describe(PluginCapability capabilities)
    {
        var parts = new List<string>();

        foreach ((PluginCapability capability, string text) in All)
        {
            if (capabilities.HasFlag(capability)) parts.Add(text);
        }

        return parts.Count == 0 ? "（无）" : string.Join("\n", parts);
    }
}
