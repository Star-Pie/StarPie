using System.Globalization;

namespace WinPieGestures.Plugins;

/// <summary>
/// 插件管理页的列表项视图模型。
/// <para>
/// 有意<b>不</b>实现 <c>INotifyPropertyChanged</c>：每次刷新都整体重建列表，
/// 而不是增量更新单个字段。插件状态的变化几乎总是成组的 —— 加载失败会连带改变
/// 动作数、错误文案与按钮可见性，整体重建不会出现「状态只更新了一半」的中间态。
/// 实现变更通知反而会诱导后来者去做局部更新，得不偿失。
/// </para>
/// <para>
/// <b>构建入口是 <see cref="Build"/>，刻意放在本类型里而不是窗口类里</b>：面向用户的
/// 文案全在它里面拼，而窗口类里的私有方法<b>无界面自检根本够不着</b> ——
/// 「这段文案在英文下还残留中文吗」就只能靠人肉切语言点一遍。放在这里之后，
/// <c>--plugin-selftest</c> 的 <c>[3f]</c> 能逐语言驱动它并逐字段断言。
/// </para>
/// </summary>
internal sealed class PluginListItem
{
    public string PluginId { get; init; } = "";

    /// <summary>插件自称的名称。清单缺失时退化为插件 ID。</summary>
    public string DisplayName { get; init; } = "";

    /// <summary>形如 <c>v1.2.0</c>；未知版本时为空串，列表中不占位。</summary>
    public string VersionText { get; init; } = "";

    /// <summary>
    /// ① 头像方块里显示的字形。
    /// <para>
    /// 清单的 <c>Icon</c> 字段约定是<b>相对路径</b>（SVG/PNG），本层不做图片加载，
    /// 所以只有当清单把图标写成单个字形（emoji）时才直接采用，其余一律退化为名称首字 ——
    /// 见 <see cref="ResolveAvatar"/>。这是「概念稿上给了方形头像、我们手上只有名字」的
    /// 诚实降级，不是没做完。
    /// </para>
    /// </summary>
    public string AvatarText { get; init; } = "";

    /// <summary>③ 插件自己的描述（清单原话），独立成行。为空时整行折叠。</summary>
    public string DescriptionText { get; init; } = "";

    /// <summary>供 DataTrigger 判断是否显示描述行。</summary>
    public bool HasDescription => !string.IsNullOrWhiteSpace(DescriptionText);

    /// <summary>④ 一行 meta：作者、许可证、贡献动作数（或「未加载」）。</summary>
    public string MetaText { get; init; } = "";

    /// <summary>
    /// ⑤ 声明的高风险能力，<b>逐个</b>渲染成标签。
    /// <para>
    /// 用列表而不是拼成一行：拼成一行之后界面就没法逐项加底色、也没法断言
    /// 「每一项都真的取到了词条」—— 而这里恰好出过事故（见
    /// <see cref="PluginCapabilityLabels.DescribeTags"/> 的注释）。
    /// </para>
    /// </summary>
    public IReadOnlyList<string> CapabilityTags { get; init; } = Array.Empty<string>();

    /// <summary>供 DataTrigger 判断是否显示能力标签区。</summary>
    public bool HasCapabilities => CapabilityTags.Count > 0;

    /// <summary>次级细节：ID、目标框架、摘要哈希、签名状态、安装路径。沉在卡片最底部的小字。</summary>
    public string DetailText { get; init; } = "";

    /// <summary>状态的当前语言名称，如「运行中」「已隔离」。</summary>
    public string StateText { get; init; } = "";

    /// <summary>状态图标，用于在列表里快速扫读。</summary>
    public string StatusGlyph { get; init; } = "";

    /// <summary>
    /// 卡片上「启用」复选框的文字。
    /// <para>
    /// 这两个按钮在 <c>ListBox.ItemTemplate</c> 里，<b>命名域与窗口不同</b> —— <c>Name</c> 对
    /// 模板内的元素无效，<c>ApplyPluginsPageLocalization()</c> 按名字取控件根本取不到它们。
    /// 唯一的解法是绑定到本类型的属性，由 <see cref="Build"/> 按当前语言填好。
    /// </para>
    /// </summary>
    public string EnableText { get; init; } = "";

    /// <summary>卡片上「卸载」按钮的文字。同 <see cref="EnableText"/> 的绑定理由。</summary>
    public string UninstallText { get; init; } = "";

    /// <summary>错误详情。为空表示健康。</summary>
    public string ErrorText { get; init; } = "";

    /// <summary>供 DataTrigger 判断是否显示错误行。</summary>
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    /// <summary>是否已启用（登记态，不是运行态）。</summary>
    public bool IsEnabled { get; init; }

    /// <summary>
    /// 把运行时实例翻译成列表项。所有面向用户的文案都在这里，且全部走 <see cref="I18n"/>。
    /// <para>
    /// 词条是<b>构建时</b>取的，不加字段缓存：<see cref="Build"/> 只在整体刷新列表时调用，
    /// 而语言切换路径（<c>ApplyPluginsPageLocalization</c>）末尾就会触发一次整体刷新。
    /// 缓存反而会让切语言后卡片留在旧语言。
    /// </para>
    /// </summary>
    public static PluginListItem Build(PluginInstance instance)
    {
        PluginRegistryEntry entry = instance.Entry;
        StarPie.Plugin.PluginManifest? manifest = instance.Scan.Manifest;

        string displayName = !string.IsNullOrWhiteSpace(entry.Name)
            ? entry.Name
            : (!string.IsNullOrWhiteSpace(manifest?.Name) ? manifest!.Name : instance.PluginId);

        (string glyph, string stateText) = DescribeState(instance);

        // ④ meta 行只放「用户关心」的三项：作者、许可证、贡献了几个动作。
        // 技术细节（ID / 架构 / 哈希 / 路径 / 签名）沉到 DetailText，两层信息不互相淹没。
        var meta = new List<string>();
        if (!string.IsNullOrWhiteSpace(entry.Author)) meta.Add(I18n.TF("PluginsCardAuthor", entry.Author));
        if (!string.IsNullOrWhiteSpace(entry.License)) meta.Add(I18n.TF("PluginsCardLicense", entry.License));
        if (instance.ActionCount > 0) meta.Add(I18n.TF("PluginsCardActionCount", instance.ActionCount));
        else if (instance.State != PluginRuntimeState.Active) meta.Add(I18n.T("PluginsCardNoActions"));

        // 「　|　」是**字形**分隔符：分隔的是若干等权短语，英文里换成 ", " 反而会与短语
        // 内部的逗号混在一起看不出来。与 PluginsEnumSeparator（顿号，标点）不是一类。
        string metaText = string.Join("　|　", meta);

        // 「ID」「SHA256」是国际通用标识符，不翻译；其余每一项都走词条。
        var detail = new List<string> { $"ID {instance.PluginId}" };
        if (!string.IsNullOrWhiteSpace(instance.Scan.TargetFramework)) detail.Add(instance.Scan.TargetFramework);
        if (!string.IsNullOrWhiteSpace(instance.Scan.MachineText)) detail.Add(instance.Scan.MachineText);
        if (!string.IsNullOrWhiteSpace(instance.Scan.Sha256Short)) detail.Add($"SHA256 {instance.Scan.Sha256Short}");
        detail.Add(I18n.T(instance.Scan.IsSigned ? "PluginsCardSigned" : "PluginsCardUnsigned"));
        if (!string.IsNullOrWhiteSpace(instance.Directory)) detail.Add(instance.Directory);
        if (!string.IsNullOrWhiteSpace(entry.ExternalPath)) detail.Add(I18n.TF("PluginsCardExternalPath", entry.ExternalPath));

        // 错误行：优先展示插件自己的失败原因；没有失败但待重启时，说明「为什么要重启」。
        // instance.LastError 由宿主的异常路径生成，属宿主内部消息，不在本层接线范围内。
        string errorText = instance.LastError ?? "";
        if (string.IsNullOrWhiteSpace(errorText) && instance.RequiresRestart)
        {
            errorText = I18n.T("PluginsCardRestartReason");
        }

        return new PluginListItem
        {
            PluginId = instance.PluginId,
            DisplayName = displayName,
            VersionText = string.IsNullOrWhiteSpace(entry.Version) ? "" : $"v{entry.Version}",
            AvatarText = ResolveAvatar(manifest?.Icon, displayName),
            DescriptionText = entry.Description ?? "",
            MetaText = metaText,
            CapabilityTags = BuildCapabilityTags(entry.CapabilitiesAck),
            DetailText = string.Join("　·　", detail),
            StateText = stateText,
            StatusGlyph = glyph,
            EnableText = I18n.T("PluginsCardEnableCheckBox"),
            UninstallText = I18n.T("PluginsCardUninstallButton"),
            ErrorText = errorText,
            IsEnabled = entry.Enabled,
        };
    }

    /// <summary>
    /// ① 头像字形。清单的 <c>Icon</c> 约定是相对路径（SVG/PNG），本层不做图片加载 ——
    /// 于是只有当它被写成单个字形（emoji）时才直接采用，其余退化为名称首字。
    /// <para>
    /// 首字用 <see cref="StringInfo.GetNextTextElement(string)"/> 取，而不是 <c>name[0]</c>：
    /// 名字以 emoji 开头是常见写法，而 emoji 是代理对，<c>name[0]</c> 会切出半个字符 ——
    /// 界面上显示成一个方块，既不报错也不像有问题，是典型的「静默错值」。
    /// </para>
    /// <para>
    /// <b>internal 而非 private</b>：官方插件市场页的卡片（<c>OfficialPluginListItem</c>）
    /// 也要同一份退化逻辑。两处各写一遍就会漂移 —— 一处改成 <c>name[0]</c>，
    /// 只有那一处的 emoji 名会显示成方块，而且没人会想到去比对另一个页签。
    /// </para>
    /// </summary>
    internal static string ResolveAvatar(string? icon, string displayName)
    {
        if (!string.IsNullOrWhiteSpace(icon))
        {
            string trimmed = icon.Trim();
            bool looksLikePath = trimmed.Contains('/') || trimmed.Contains('\\') || trimmed.Contains('.');
            if (!looksLikePath) return trimmed;
        }

        if (string.IsNullOrWhiteSpace(displayName)) return "🧩";

        string first = StringInfo.GetNextTextElement(displayName.Trim());
        return first.ToUpperInvariant();
    }

    /// <summary>
    /// ⑤ 把登记表里的能力名（英文枚举名）翻成本语言的标签。
    /// <para>
    /// 认不出来的名字原样保留，不静默丢弃：登记表可能来自比宿主更新、能力位更多的插件，
    /// 而「少显示一项能力」会让用户以为这个插件没要过那个权限 ——
    /// 把原始名字摊在卡片上，至少看得见。
    /// </para>
    /// <para>
    /// <b>刻意是 internal 而不是 private</b>：自检 <c>[3f]</c> 要直接驱动它。
    /// 只靠「卡片构建」那条链路断言的话，覆盖面就取决于「沙箱里那个插件恰好声明了几个能力」——
    /// 声明 0 个时整条断言静默跳过，声明 1 个时「不认识的名字」那条分支永远跑不到。
    /// </para>
    /// </summary>
    internal static IReadOnlyList<string> BuildCapabilityTags(List<string> capabilitiesAck)
    {
        var tags = new List<string>();

        foreach (string name in capabilitiesAck)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;

            if (Enum.TryParse(name, ignoreCase: true, out StarPie.Plugin.PluginCapability capability)
                && capability != 0)
            {
                tags.AddRange(PluginCapabilityLabels.DescribeTags(capability));
            }
            else
            {
                tags.Add(name);
            }
        }

        return tags;
    }

    /// <summary>
    /// 运行时状态 → (图标, 当前语言的状态名)。
    /// <para>
    /// 刻意写成<b>穷尽</b> switch、不留 <c>_</c> 兜底。原先的兜底是 <c>instance.State.ToString()</c>，
    /// 于是不在列举里的 <see cref="PluginRuntimeState.Stopping"/> 在中文界面上直接显示英文单词
    /// 「Stopping」—— 一个既不空白、也不像错的值。
    /// </para>
    /// <para>
    /// 与 <c>PluginScanFailureText</c> 同一处置：屏蔽 <b>CS8524</b>，保留穷尽带来的 <b>CS8509</b>。
    /// CS8524 抱怨的是**未命名**枚举值（<c>(PluginRuntimeState)9</c> 这类强制转换产物），而本枚举
    /// 只在宿主内部赋值、没有任何反序列化或强制转换来源，那种值不存在；不屏蔽的话「穷尽」这个
    /// 特性根本用不了。实测漏一个具名成员时 CS8509 仍会出现。
    /// </para>
    /// <para>
    /// 判据刻意是<b>值</b>而不是实例：<c>--plugin-selftest</c> 要能拿 <c>Enum.GetValues</c>
    /// 逐个成员驱动它，而「逐成员」这件事没法靠构造 9 个 <see cref="PluginInstance"/> 来做。
    /// </para>
    /// </summary>
    public static (string Glyph, string Text) DescribeState(PluginInstance instance) =>
        DescribeState(instance.State, instance.RequiresRestart, instance.Entry.Enabled);

    /// <inheritdoc cref="DescribeState(PluginInstance)"/>
#pragma warning disable CS8524 // 未命名枚举值不可达，理由见上方注释
    public static (string Glyph, string Text) DescribeState(
        PluginRuntimeState state,
        bool requiresRestart,
        bool entryEnabled) => state switch
    {
        PluginRuntimeState.Active => requiresRestart
            ? ("🔄", I18n.T("PluginsStateActiveRestartPending"))
            : ("✅", I18n.T("PluginsStateActive")),
        PluginRuntimeState.Loading => ("⏳", I18n.T("PluginsStateLoading")),
        // Installed 两种处境（已启用待加载 / 未启用）共用同一个图标，只有文字不同。
        PluginRuntimeState.Installed => ("⭕", I18n.T(entryEnabled
            ? "PluginsStateEnabledPendingLoad"
            : "PluginsStateDisabled")),
        PluginRuntimeState.Stopping => ("⭕", I18n.T("PluginsStateStopping")),
        PluginRuntimeState.Faulted => ("⚠️", I18n.T("PluginsStateFaulted")),
        PluginRuntimeState.Quarantined => ("🚫", I18n.T("PluginsStateQuarantined")),
        PluginRuntimeState.Failed => ("❌", I18n.T("PluginsStateFailed")),
        PluginRuntimeState.Incompatible => ("⛔", I18n.T("PluginsStateIncompatible")),
        PluginRuntimeState.RequiresRestart => ("🔄", I18n.T("PluginsStateRestartPending")),
    };
#pragma warning restore CS8524
}
