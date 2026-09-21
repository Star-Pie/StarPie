namespace StarPie.Plugin;

/// <summary>
/// 插件声明需要使用的宿主能力。
/// <para>
/// 语义是「<b>声明</b>」而非「<b>授权</b>」：.NET 进程内插件不存在真正的权限门（CAS 已废弃），
/// 宿主无法拦截插件直接调用 BCL。这个枚举的作用是让用户在安装确认页看到插件要做什么，
/// 并让审核者能对照源码检查声明是否诚实。请如实声明，不要为了少一次风险提示而漏报。
/// </para>
/// </summary>
[Flags]
public enum PluginCapability
{
    /// <summary>不使用任何需要声明的能力（纯计算 / 纯 UI 扩展）。</summary>
    None = 0,

    /// <summary>启动进程、执行命令。</summary>
    Process = 1 << 0,

    /// <summary>读写用户文件（插件私有目录之外）。</summary>
    FileSystem = 1 << 1,

    /// <summary>发起网络请求。</summary>
    Network = 1 << 2,

    /// <summary>读写系统剪贴板。</summary>
    Clipboard = 1 << 3,

    /// <summary>读写注册表。</summary>
    Registry = 1 << 4,

    /// <summary>注册全局输入钩子。首版禁止（请改用 IPluginEvents 由宿主代发事件）。</summary>
    GlobalHook = 1 << 5,

    /// <summary>打开自己的窗口 / 弹窗。</summary>
    Ui = 1 << 6,

    /// <summary>需要管理员权限才能正常工作（宿主已提权时插件会继承该权限）。</summary>
    Admin = 1 << 7,

    /// <summary>
    /// 操作<b>其它程序</b>的窗口：平铺、置顶、改透明度、搬到另一块显示器、激活任务栏槽位。
    /// <para>
    /// 与 <see cref="Ui"/> 分开是刻意的，不要合并：<c>Ui</c> 的语义是「打开自己的窗口 / 弹窗」，
    /// 拿它来表示「移动别人的窗口」会让安装确认页对用户说假话 ——
    /// 用户看到「界面」两个字，脑子里想的是弹个对话框，实际后果却是他正在用的窗口被挪走。
    /// </para>
    /// <para>
    /// 新值取 <c>1 &lt;&lt; 8</c> 而不是插进前面的中间：已有插件清单里存的是<b>名字</b>，
    /// 但把新成员插在中间会改变后续所有成员的位值，那是一次没必要的兼容性风险。
    /// </para>
    /// </summary>
    WindowControl = 1 << 8,

    /// <summary>
    /// 读取<b>屏幕内容</b>（截屏后交给 OCR 识别）。
    /// <para>
    /// 单独一项而不是并进 <see cref="Ui"/>：截屏是隐私敏感的能力，用户在安装确认页上
    /// 需要看到的正是「它会看到我的屏幕」这件事 —— 藏在一句「界面」里等于没说。
    /// </para>
    /// <para>
    /// 与 <see cref="WindowControl"/> 同理，新值取 <c>1 &lt;&lt; 9</c> 而不是插进中间：
    /// 插入会改变后续所有成员的位值。
    /// </para>
    /// </summary>
    ScreenCapture = 1 << 9,

    /// <summary>
    /// 模拟<b>键盘输入</b>：向当前前台窗口投递组合键，或触发一个以合成按键实现的系统功能
    /// （最小化 / 最大化 / 任务视图 / 音量 / 媒体控制 …）。
    /// <para>
    /// 与 <see cref="Process"/> 分开是刻意的，不要合并：两者在系统控制这个动作里都会发生，
    /// 但后果不同 —— 「进程」是<b>多出一个后台进程</b>，「模拟输入」是
    /// <b>往用户正在打字的那个窗口里按键</b>。用户能接受前者不代表能接受后者。
    /// </para>
    /// <para>
    /// 它拦的是「让宿主替你按键」这条路径（<c>IHostSystemService.RunPreset</c>）。
    /// 进程内的插件当然仍可自己 P/Invoke <c>SendInput</c>，SDK 拦不住 ——
    /// 门禁换来的从来不是安全，而是「安装确认页上的声明有对应物」。
    /// </para>
    /// <para>
    /// 与 <see cref="WindowControl"/> / <see cref="ScreenCapture"/> 同理，新值取
    /// <c>1 &lt;&lt; 10</c> 而不是插进中间：插入会改变后续所有成员的位值。
    /// </para>
    /// </summary>
    InputSimulation = 1 << 10,

    /// <summary>
    /// 呼出宿主的轮盘并让用户从中选一个扇区执行。
    /// <para>
    /// 与 <see cref="Ui"/> 分开是刻意的，不要合并：<c>Ui</c> 的后果是「多出一个插件自己的窗口」，
    /// 而呼出轮盘的后果重得多 —— 轮盘上每个扇区跑的都是<b>用户自己配置的动作</b>
    /// （启动程序、发快捷键、执行命令 …），插件借此把用户推到「点哪个执行哪个」的位置上。
    /// 更实际的一条：轮盘呼出期间会有一层全屏遮罩接管所有鼠标点击，
    /// 用户此刻的每一次点击都属于选盘，而不是他原来要点的东西 —— 这必须让用户在安装时看见。
    /// </para>
    /// <para>
    /// 与 <see cref="WindowControl"/> / <see cref="ScreenCapture"/> / <see cref="InputSimulation"/>
    /// 同理，新值取 <c>1 &lt;&lt; 11</c> 而不是插进中间：插入会改变后续所有成员的位值。
    /// </para>
    /// </summary>
    Wheel = 1 << 11,
}

/// <summary>宿主持有的插件元数据。由宿主从 manifest 解析后经 <see cref="IPluginContext.Me"/> 提供给插件。</summary>
public sealed class PluginMetadata
{
    /// <summary>全局唯一 ID，反向域名风格，例如 <c>com.example.pomodoro</c>。</summary>
    public string Id { get; init; } = "";

    /// <summary>显示名。</summary>
    public string Name { get; init; } = "";

    /// <summary>插件版本（语义化）。</summary>
    public string Version { get; init; } = "";

    /// <summary>作者或组织。</summary>
    public string Author { get; init; } = "";

    /// <summary>一句话简介。</summary>
    public string Description { get; init; } = "";

    /// <summary>项目主页，可为空。</summary>
    public string? Homepage { get; init; }

    /// <summary>SPDX 许可证标识。</summary>
    public string License { get; init; } = "";

    /// <summary>插件编译所依赖的 SDK 契约版本，例如 <c>1.0</c>。</summary>
    public string ApiVersion { get; init; } = "";

    /// <summary>插件声明的能力集合。</summary>
    public PluginCapability Capabilities { get; init; } = PluginCapability.None;

    /// <summary>插件安装目录（只读，宿主管理）。</summary>
    public string InstallDirectory { get; init; } = "";

    /// <summary>插件私有可写数据目录。</summary>
    public string DataDirectory { get; init; } = "";

    public override string ToString() => $"{Name} ({Id}) v{Version}";
}

/// <summary>
/// 插件动作在 <c>ActionItem</c> 中的引用地址。
/// <para>
/// 持久化形态与内置动作完全一致，宿主通过它把一条轮盘槽位指向某个插件的某个贡献点。
/// 保留为独立 POCO 而不是两个裸字符串，是为了让旧版本宿主反序列化后能整体识别为「不认识的对象」并安全降级。
/// </para>
/// </summary>
public sealed class PluginActionRef
{
    /// <summary>插件 ID。</summary>
    public string PluginId { get; set; } = "";

    /// <summary>贡献点短 ID（不含插件 ID 前缀），由 <see cref="ActionDescriptor.Id"/> 声明。</summary>
    public string ContributionId { get; set; } = "";

    /// <summary>宿主归一化后的完整贡献 ID，形如 <c>&lt;pluginId&gt;.&lt;contributionId&gt;</c>。</summary>
    public string FullId => string.IsNullOrEmpty(PluginId) ? ContributionId : $"{PluginId}.{ContributionId}";

    public bool IsValid => !string.IsNullOrWhiteSpace(PluginId) && !string.IsNullOrWhiteSpace(ContributionId);

    public PluginActionRef Clone() => new() { PluginId = PluginId, ContributionId = ContributionId };

    public override string ToString() => FullId;
}
