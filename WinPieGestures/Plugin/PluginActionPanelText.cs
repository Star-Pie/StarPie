namespace WinPieGestures.Plugins;

/// <summary>
/// 插件动作编辑面板（「手势动作」页里选中一个插件动作之后那一块）的**全部面向用户文案**。
/// <para>
/// <b>为什么单独成类、而不是留在 <c>SettingsWindow</c> 里</b>：这块文案是**代码拼串**，
/// 而窗口类的私有方法<b>无界面自检根本够不着</b> ——「英文界面下它还残留中文吗」
/// 就只能靠人肉切语言点一遍。搬到这里之后，<c>--plugin-selftest</c> 的 <c>[3g]</c>
/// 能逐语言驱动它并逐字段断言。同一条理由已用在 <see cref="PluginListItem"/>
/// 与 <c>PluginInstallConfirmationText</c> 上：<b>可测性是靠摆放位置换来的</b>。
/// </para>
/// <para>
/// 入参刻意是**基本类型**而不是 <c>PluginActionRegistration</c>：注册表的字段随时会长，
/// 而自检里要构造一个合法的 registration 得连带填一堆无关字段。基本类型让「驱动一次」变成一行。
/// </para>
/// <para>
/// <b>改这块文案就要同步看自检 <c>[3g]</c></b>：它按「三种处境 × 四种语言」遍历，
/// 少接一条键会以「取到的是裸键名」报出来。
/// </para>
/// <para>
/// 三段文案用元组返回而不是新建类型，与 <see cref="PluginListItem.DescribeState"/> 保持一致：
/// 这个形状只在这里借用一次，不值得多一个类型。
/// </para>
/// </summary>
internal static class PluginActionPanelText
{
    /// <summary>
    /// 处境一：类型选了「插件动作」，但还没挑具体动作。
    /// </summary>
    /// <param name="candidateCount">当前可选的插件动作数。<b>0 与非 0 是两句不同的话</b>：
    /// 前者要引导用户去插件页装插件，后者要引导他去下拉框里挑 ——
    /// 让用户对着一棵空下拉框找东西，只会让人以为功能坏了。</param>
    internal static (string Title, string Detail, string? Hint) NotChosen(int candidateCount) => (
        PanelTitle,
        candidateCount > 0 ? I18n.T("PluginsPanelNotChosenPick") : I18n.T("PluginsPanelNotChosenEmpty"),
        null);

    /// <summary>处境二：引用还在、贡献点却没了（插件被停用 / 卸载，或升级后移除了该动作）。</summary>
    internal static (string Title, string Detail, string? Hint) Unavailable(string contributionId) => (
        PanelTitle,
        I18n.TF("PluginsPanelUnavailable", contributionId),
        I18n.T("PluginsPanelUnavailableHint"));

    /// <summary>处境三：正常。</summary>
    /// <param name="pluginDisplayName">插件的显示名。与 <paramref name="pluginId"/> 相同时只显示前者，
    /// 免得界面上出现「StarPie 官方动作（starpie.official）」这种把同一件事说两遍的行。</param>
    /// <param name="description">插件自述。属于**插件自带数据**，不翻译、原样透出。</param>
    internal static (string Title, string Detail, string? Hint) Registered(
        string displayName,
        string pluginId,
        string pluginDisplayName,
        string contributionId,
        bool background,
        int timeoutSeconds,
        string? description)
    {
        var detail = new System.Text.StringBuilder();

        detail.Append(string.Equals(pluginDisplayName, pluginId, StringComparison.Ordinal)
            ? I18n.TF("PluginsPanelProvider", pluginDisplayName)
            : I18n.TF("PluginsPanelProviderWithId", pluginDisplayName, pluginId));

        // 「　|　」是**字形**分隔符（排版装饰），不随语言变；只有标点类分隔符才走词条
        // （对照 PluginsEnumSeparator：简中「、」/ 英文 ", "）。两者别混 ——
        // 混了的表现是英文界面里冒出一个顿号，或者中文界面里被塞进半角逗号。
        detail.Append("　|　").Append(I18n.TF(
            "PluginsPanelExecutionMode",
            I18n.T(background ? "PluginsPanelKindBackground" : "PluginsPanelKindSerial")));

        if (timeoutSeconds > 0)
        {
            detail.Append("　|　").Append(I18n.TF("PluginsPanelTimeout", timeoutSeconds));
        }

        detail.Append('\n').Append(I18n.TF("PluginsPanelContributionId", contributionId));
        if (!string.IsNullOrWhiteSpace(description))
        {
            detail.Append('\n').Append(description);
        }

        return ("🔌 " + displayName, detail.ToString(), null);
    }

    /// <summary>
    /// 参数表单上方那一行提示。
    /// <para>
    /// 计数分支收在这里、不外露：写成「传 bool 让调用方自己挑」的话，
    /// 「0 个必填」与「有必填」各自走到哪一句就散在两处了，加一种处境要改两个文件。
    /// </para>
    /// </summary>
    /// <param name="requiredCount">必填字段数。<b>布尔项不计入</b> —— 它未填即视为 false，
    /// 不存在「留空被拦下」，算进去会让用户以为有个开关必须先动一下才能保存。</param>
    internal static string ParamsHint(int requiredCount) => requiredCount > 0
        ? I18n.TF("PluginsPanelRequiredParams", requiredCount)
        : I18n.T("PluginsPanelAllOptional");

    /// <summary>
    /// 校验结论里「还差几项」那一句。字段级错误由表单就地标红，
    /// 这里只说总数 —— 否则同一条信息会在界面上出现两遍。
    /// </summary>
    internal static string IssuesCount(int count) => I18n.TF("PluginsPanelIssuesCount", count);

    /// <summary>
    /// 面板标题。三种处境共用一句话（就是类型名）—— 处境差异写在正文里，
    /// 标题跟着变会让人以为换了一块面板。
    /// </summary>
    private static string PanelTitle => "🔌 " + I18n.T("ActionTypePluginShort");
}
