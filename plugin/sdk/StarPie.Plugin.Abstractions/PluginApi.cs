namespace StarPie.Plugin;

/// <summary>
/// SDK 全局常量。这些值同时被宿主与插件使用，属于「契约的一部分」，任何修改都是破坏性变更。
/// </summary>
public static class PluginApi
{
    /// <summary>SDK 契约主版本。插件 manifest 的 <c>apiVersion</c> 主版本必须与之相等才能加载。</summary>
    public const int ApiVersionMajor = 1;

    /// <summary>
    /// SDK 契约次版本。<b>只增不改</b>的能力演进在此递增：
    /// <list type="bullet">
    /// <item>1.1 —— 新增 <see cref="IHostCommandService"/> 与 <see cref="IHostShellService"/>，
    /// 并让 <see cref="PluginCapability.Process"/> 从「安装确认页的标签」变成真实门禁
    /// （未声明该能力的插件调用这两个服务会抛 <see cref="PluginCapabilityDeniedException"/>）。</item>
    /// <item>1.2 —— 新增 <see cref="IHostWindowService"/>（装配到
    /// <see cref="IPluginContext.Windows"/>）与 <see cref="PluginCapability.WindowControl"/>，
    /// 使「平铺 / 置顶 / 透明度 / 移到下一屏 / 切换应用」这五个动作不必再硬编码在宿主里。</item>
    /// <item>1.3 —— 新增 <see cref="IHostScreenCaptureService"/>（装配到
    /// <see cref="IPluginContext.ScreenCapture"/>）与 <see cref="PluginCapability.ScreenCapture"/>，
    /// 使「截屏识字」不必再硬编码在宿主里。</item>
    /// <item>1.4 —— 新增 <see cref="IHostSystemService"/>（装配到
    /// <see cref="IPluginContext.System"/>）与 <see cref="PluginCapability.InputSimulation"/>，
    /// 使「系统控制」不必再硬编码在宿主里。</item>
    /// <item>1.5 —— 新增 <see cref="IHostWheelService"/>（装配到
    /// <see cref="IPluginContext.Wheel"/>）与 <see cref="PluginCapability.Wheel"/>，
    /// 使「悬浮球」这类常驻形态不必（也无法）在插件里复刻轮盘。</item>
    /// <item>1.6 —— 新增 <see cref="ISettingsPageRegistry"/>（装配到
    /// <see cref="IPluginContext.SettingsPage"/>）与 <see cref="SettingsPageDescriptor"/>，
    /// 使插件能声明<b>插件级</b>参数页、由宿主在插件管理卡片上统一渲染。
    /// 同时提供 <see cref="IPluginSettings.OnChanged"/>，让插件在设置成功持久化后响应具体字段变化。</item>
    /// </list>
    /// <para>
    /// <b>每加一个服务面就在这里补一条，别只改数字。</b>这份清单是后来者判断
    /// 「某个接口从哪一版开始存在」的唯一依据 —— 数字变了而清单没变，
    /// 插件作者会按旧清单去推断版本兼容性，而结论是错的。
    /// </para>
    /// </summary>
    public const int ApiVersionMinor = 6;

    /// <summary>
    /// SDK 契约版本字符串，形如 <c>1.6</c>。
    /// <para>
    /// 这里没法用常量插值消掉重复：C# 的常量插值只接受 <c>string</c> 常量，
    /// 而版本号的两个组成部分是 <c>int</c>。所以「这个字符串」与「上面两个数字」
    /// 之间存在一处可能漂移的重复 —— 由自检断言守着（<c>PluginSelfTest</c> 会比对三者）。
    /// 之所以值得守：它正是插件在清单里声明、宿主用来做兼容判断的值，
    /// 漂了以后报错信息里的版本号会和真实契约对不上，排查时先被误导一轮。
    /// </para>
    /// </summary>
    public const string ApiVersion = "1.6";

    /// <summary>本契约程序集的程序集名。宿主的 PluginLoadContext 依赖它做「共享程序集放行」。</summary>
    public const string AbstractionsAssemblyName = "StarPie.Plugin.Abstractions";

    /// <summary>
    /// 当前 plugin.json 清单结构版本。宿主不支持新版本清单时直接拒绝，并提示升级 StarPie。
    /// </summary>
    public const int ManifestSchemaVersion = 1;

    /// <summary>
    /// 插件动作在 <c>ActionItem.Type</c> 中统一使用这个值。
    /// 选它而不是复用内置 type，是为了让旧版主程序读到后走 switch 无匹配分支 → 静默无操作，而不是崩溃。
    /// </summary>
    public const string ActionTypeName = "Plugin";

    /// <summary>
    /// 顶层动作类型认领的<b>程序集元数据键</b>。
    /// <para>
    /// 宿主需要<b>在不加载程序集的前提下</b>知道「用户配置里的 <c>Type="Command"</c> 归哪个插件」——
    /// 这决定了启动时该预加载谁，也决定了轮盘触发时能不能避免一次几百毫秒的现场加载。
    /// 裸 DLL 的识别本来就走静态元数据读取，认领声明搭同一班车即可，代价是几微秒的字节读。
    /// </para>
    /// <para>
    /// 值形如 <c>"Command=command;Hotkey=hotkey;WebUrl=webUrl"</c> —— 分号分隔的
    /// <c>顶层类型名=插件内短 ID</c> 对。写成显式配对而不是只列类型名，
    /// 是为了不去猜「第几个类型对应第几个贡献点」这种顺序假设。
    /// </para>
    /// <para>
    /// <b>只有随包分发的插件可以认领</b>（见 <see cref="ReservedIdPrefixes"/> 的同一动机）：
    /// 否则任何第三方插件都能声明自己认领 <c>"Command"</c>，把用户配好的命令行扇区整体接管过去。
    /// </para>
    /// </summary>
    public const string TypeClaimsMetadataKey = "StarPiePluginTypeClaims";

    /// <summary>插件语言词条的完整 key 前缀，最终形如 <c>plugin.&lt;pluginId&gt;.&lt;key&gt;</c>。</summary>
    public const string I18nKeyPrefix = "plugin.";

    /// <summary>插件图标 key 的完整前缀，最终形如 <c>plugin:&lt;pluginId&gt;:&lt;key&gt;</c>。</summary>
    public const string IconKeyPrefix = "plugin:";

    /// <summary>禁止社区占用的 ID 前缀（保留给官方与系统）。</summary>
    public static readonly string[] ReservedIdPrefixes =
    {
        "starpie", "winpiegestures", "windows", "microsoft", "system", "builtin"
    };

    /// <summary>单个动作参数字符串值的最大长度（字符），防止插件把巨串塞进 config.json 造成 LOH 膨胀。</summary>
    public const int MaxParameterValueLength = 8 * 1024;
}
