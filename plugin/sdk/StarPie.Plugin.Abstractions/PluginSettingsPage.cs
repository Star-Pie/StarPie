namespace StarPie.Plugin;

/// <summary>
/// 插件级参数页的自描述信息。
/// <para>
/// 与 <see cref="ActionDescriptor"/> 的区别在于<b>作用范围</b>：动作参数属于「某个扇区」，
/// 每挂一个扇区填一份；本描述符属于<b>插件整体</b>，全机一份，宿主在插件管理卡片上渲染。
/// 典型内容是该插件所有动作共享的默认值（球的直径、透明度、颜色）。
/// </para>
/// <para>
/// <b>只能由代码声明，不能写进 plugin.json</b>：字段标签要走插件自己的 i18n 词条，
/// 而词条只有在程序集加载、<see cref="II18nRegistry.Register"/> 之后才存在。
/// 清单里能填的只有安装前就要显示的东西。
/// </para>
/// </summary>
public sealed class SettingsPageDescriptor
{
    /// <summary>页面标题（可直接写中文）。</summary>
    public string Title { get; init; } = "";

    /// <summary>可选的 i18n 短键；非空时优先于 <see cref="Title"/>。</summary>
    public string? TitleKey { get; init; }

    /// <summary>标题下方的一段说明，用来交代「这些是全局默认值、动作上可单独覆盖」这类优先级规则。</summary>
    public string? Description { get; init; }

    /// <summary>可选的 i18n 短键；非空时优先于 <see cref="Description"/>。</summary>
    public string? DescriptionKey { get; init; }

    /// <summary>
    /// 字段表。复用 <see cref="ParameterField"/>，因此插件级设置与动作参数在界面上完全同构 ——
    /// 同一套渲染、同一套校验规则（<see cref="ParameterField.Required"/> /
    /// <see cref="ParameterField.Min"/> / <see cref="ParameterField.ValidationRegex"/> 全部生效）。
    /// <para>无字段请返回空列表，不要返回 null。</para>
    /// </summary>
    public IReadOnlyList<ParameterField> Fields { get; init; } = Array.Empty<ParameterField>();
}

/// <summary>
/// 插件级参数页注册表。
/// <para>
/// <b>这里只有声明，没有回调</b>：宿主负责渲染与落盘，插件只负责「说清有哪些字段」。
/// 用户改完值之后，插件通过 <see cref="IPluginSettings"/> 读取即可 ——
/// 宿主写入的键与 <see cref="IPluginSettings"/> 使用的是<b>同一个命名空间</b>，
/// 所以 <c>Settings.Get("diameter")</c> 读到的就是用户在设置页里填进去的那个值。
/// </para>
/// <para>
/// 设置页本身不提供保存回调；需要响应设置变化的插件应通过
/// <see cref="IPluginSettings.OnChanged"/> 订阅具体键，并在停用时释放返回的凭据。
/// 宿主会在插件停用时兜底清理订阅，避免回调处理器钉住插件的 <c>AssemblyLoadContext</c>。
/// </para>
/// </summary>
public interface ISettingsPageRegistry
{
    /// <summary>
    /// 声明本插件的设置页。<b>一个插件一页</b>，重复注册是契约错误。
    /// <para>
    /// 必须在 <see cref="IStarPiePlugin.Initialize"/> 期间调用。<b>此时不要指望
    /// <see cref="SettingsPageDescriptor.TitleKey"/> 已经被解析</b> —— 词条在这期间还留在暂存区，
    /// 要等 <c>Initialize</c> 成功返回后才整体提交，而宿主是在用户点击卡片上「设置」的那一刻
    /// 才按当前语言解析标题与字段标签。所以词条与页的声明顺序无关，插件作者也不需要
    /// 「先注册词条再注册页」。
    /// </para>
    /// <para>
    /// ⚠️ 宿主没有为「注册窗口」设门禁：在 <c>Initialize</c> 之后调用<b>不会抛异常</b>，
    /// 只会把页暂存进一个已经提交完的会话，于是它永远不出现在卡片上。
    /// 表现是「我明明注册了却看不到设置按钮」，而日志里什么都没有 —— 别把注册放到动作执行或事件回调里。
    /// </para>
    /// </summary>
    /// <returns>撤销凭据。<c>Dispose()</c> 后卡片上的「设置」入口消失；宿主停用时兜底撤销。</returns>
    /// <exception cref="PluginContractException">
    /// 传入 <c>null</c>、字段表非法（键为空或重复 / <c>Enum</c> 缺选项）、读取 <c>Fields</c> 时插件自己抛了异常、
    /// 或本插件已声明过设置页。
    /// </exception>
    IDisposable Register(SettingsPageDescriptor page);

    /// <summary>
    /// 按字段键读取当前已保存的原始值；未填写过时返回 <see cref="ParameterField.DefaultValue"/>。
    /// <para>
    /// 这只是 <see cref="IPluginSettings"/> 之上的一层便捷读取，避免插件把「字段键」和
    /// 「自己的配置键」维护成两份真相。插件也可以直接用 <c>Settings</c>，两者等价。
    /// </para>
    /// </summary>
    string? GetValue(string key);
}
