using System;
using System.Collections.Generic;

namespace WinPieGestures;

/// <summary>
/// 「这个名字是系统自动填的占位名，还是用户自己起的？」—— 全项目唯一一处判据。
/// <para>
/// <b>为什么要收敛成一个函数</b>：这个判断原先散在 30 多个位置，每个位置各自抄一份中文字面量。
/// 结果是它烂成了十几个互不相同的版本 ——
/// `StartsWith("快捷动作") || StartsWith("动作")`、`StartsWith("扇区") || StartsWith("新动作")`、
/// `== "启动程序" || == "取消动作"`… 谁也不认识全貌，加一个动作类型就在自己那块补一个词。
/// </para>
/// <para>
/// <b>收敛时发现的真实缺陷（四种语言全部受影响，与 i18n 无关）</b>：
/// 默认名是由 <c>I18n.T(key)</c> 填进去的（例如「文件夹」类型填 <c>ActionTypeFolderShort</c>），
/// 而判断只认几个写死的中文字面量，两者从未对齐过。以 <c>ActionTypeFolderShort</c> 为例，
/// 简中值是「<b>打开文件夹</b>」—— 既不是「快捷动作」开头也不是「动作」开头。
/// 于是「新建槽位 → 先选一个动作 → 再改成另一个」这条最常规的路径上，
/// 名字<b>不会跟着类型更新</b>：类型已经是平铺窗口，名字还写着上一个动作。
/// 有人踩到过这个坑，就地补了 <c>== "打开文件夹"</c> 和 <c>== "打开网址"</c> ——
/// 补的正是那两个词条的简中值，也就是说补丁只对简中有效，切到英文照样失效。
/// </para>
/// <para>
/// <b>判据必须覆盖全部语言</b>：只认当前语言的话，用户切一次语言之后，
/// 界面里那个<b>旧语言</b>的默认名就会被当成「用户自己起的名字」，自动填充从此永久失效
/// （换回语言也不恢复，因为名字已经是旧语言的值了）。
/// </para>
/// </summary>
internal static class ActionNameDefaults
{
    /// <summary>
    /// 由词条自动填入名字的键名。<b>与本表对应的赋值点是</b>：
    /// <c>SlotViewModel.ActionType</c> 的 setter（文件夹 / 网页 / 切换窗口 / 平铺）、
    /// <c>SettingsWindow.ApplyActionTypeSelection</c>（平铺还原 / 置顶 / 移到下一屏）、
    /// <c>SettingsWindow</c> 插件动作分支。
    /// <para>
    /// <b>新增一个「选类型就自动填名」的位置时，必须把键名加到这里</b>，
    /// 否则那个名字会被当成用户自定义，下次换类型时不会更新。
    /// <c>scratch/scan_cjk_logic.py</c> 会比对「代码里出现的 <c>Name = I18n.T("…")</c> 键集合」
    /// 与本表，漏加会当场报出来。
    /// </para>
    /// </summary>
    private static readonly string[] FilledNameKeys =
    {
        "ActionTypeFolderShort",
        "ActionTypeWebUrlShort",
        "ActionTypeSwitchWindowShort",
        "ActionTypeTileShort",
        "ActionTypePluginShort",
        "ActionTypeTopmostShort",
        "ActionTypeMoveMonitorShort",
        "TileRestoreAllLabel",
    };

    /// <summary>
    /// 历史上直接写死进名字字段、<b>没有对应词条</b>的字面量。
    /// <para>
    /// 老配置里真的存着这些字符串，所以必须继续认出来（认不出的后果是
    /// 用户升级后发现自己那些从没改过名的槽位突然「算自定义名了」）。
    /// 它们同时也是「还没接 i18n 的硬编码 UI 文案」—— 将来把它们接进词条时，
    /// 记得把键名挪到 <see cref="FilledNameKeys"/> 里，两处都要在。
    /// </para>
    /// </summary>
    private static readonly string[] LegacyNames =
    {
        "快捷动作",
        "未命名动作",
        "手势动作",
        "文件夹",
        "截屏识字",
        "快捷键",
        "启动程序",
        "取消动作",

        // ShellTool 类型在 SlotViewModel.ActionType 的 setter 里填的就是它。
        // 与上面几个不同，这句赋值本身是「硬编码中文、尚未接 i18n」的欠账
        // （切到英文后这个默认名仍是中文）；先纳进来，免得将来接词条时又漏掉判据。
        "复制文件/文件夹路径",
    };

    /// <summary>
    /// 「扇区 3」「动作 1」「子动作 2」这类带序号的占位名。
    /// <para>
    /// 一律<b>连空格一起比</b>。原先是裸词前缀（<c>StartsWith("动作")</c>），
    /// 于是用户把动作命名成「动作剪辑」「动作片收藏」时会被误判成占位名、
    /// 名字在换类型时被无声覆盖。带空格之后这类名字恢复为用户自定义。
    /// </para>
    /// </summary>
    private static readonly string[] IndexedPrefixes = { "扇区 ", "动作 ", "子动作 " };

    /// <summary>
    /// 全部自动填充名。刻意用静态只读字段而不是惰性初始化：
    /// 调用方分布在 UI 线程、钩子线程与保存路径上，无锁惰性初始化会在这个
    /// 「只在偶尔才跑一次」的路径上埋一个极难复现的竞态。
    /// </summary>
    private static readonly HashSet<string> AutoFilled = Build();

    /// <summary>名字为空、或等于系统会填的任何一个占位名 ⇒ 视为「还没被用户改过」。</summary>
    public static bool IsAutoFilled(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return true;

        string trimmed = name.Trim();

        if (AutoFilled.Contains(trimmed)) return true;

        foreach (string prefix in IndexedPrefixes)
        {
            if (trimmed.StartsWith(prefix, StringComparison.Ordinal)) return true;
        }

        return false;
    }

    private static HashSet<string> Build()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);

        foreach (string legacy in LegacyNames) set.Add(legacy);

        // 四种语言的每一个值都要进来 —— 见类注释「判据必须覆盖全部语言」。
        foreach (string key in FilledNameKeys)
        {
            foreach (string value in I18n.AllTranslations(key))
            {
                if (!string.IsNullOrWhiteSpace(value)) set.Add(value.Trim());
            }
        }

        return set;
    }
}
