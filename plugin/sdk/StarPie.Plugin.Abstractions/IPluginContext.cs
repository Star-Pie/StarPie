namespace StarPie.Plugin;

/// <summary>
/// 插件与宿主之间的唯一交互面。由宿主在调用 <see cref="IStarPiePlugin.Initialize"/> 时注入。
/// <para>
/// 这是一个<b>服务定位器</b>：插件永远拿不到宿主的任何具体类型（<c>AppConfig</c>、<c>ActionExecutor</c> 等），
/// 因此宿主可以自由重构内部实现而不破坏全社区插件。这也是「插件禁止引用 StarPie.dll」的落地方式。
/// </para>
/// </summary>
public interface IPluginContext
{
    /// <summary>本插件的元数据（取自 manifest）。</summary>
    PluginMetadata Me { get; }

    /// <summary>插件安装目录（只读，宿主管理）。</summary>
    string PluginDirectory { get; }

    /// <summary>插件私有可写目录（<c>plugins\&lt;id&gt;\data\</c>），已确保存在。</summary>
    string DataDirectory { get; }

    /// <summary>插件专属日志。</summary>
    IPluginLogger Log { get; }

    /// <summary>插件私有配置。</summary>
    IPluginSettings Settings { get; }

    /// <summary>注册动作贡献点（P0 核心）。</summary>
    IActionRegistry Actions { get; }

    /// <summary>注册多语言词条。</summary>
    II18nRegistry I18n { get; }

    /// <summary>注册矢量图标。</summary>
    IIconRegistry Icons { get; }

    /// <summary>
    /// 声明插件级参数页（SDK 1.6 起）。
    /// <para>
    /// 与 <see cref="Actions"/> 的区别：这里的字段属于<b>插件整体</b>，值落在
    /// <see cref="Settings"/> 的同一命名空间里，由宿主在插件管理卡片上统一渲染。
    /// 纯声明式，不需要任何能力位 —— 宿主只是用 SDK 类型画了个表单，
    /// 既不接管用户点击，也不起进程。
    /// </para>
    /// </summary>
    ISettingsPageRegistry SettingsPage { get; }

    /// <summary>宿主已验证的动作能力（发快捷键 / 启程序 / 剪贴板 / 开网址）。</summary>
    IHostActionInvoker Host { get; }

    /// <summary>
    /// 命令执行（在指定终端里跑一段命令）。
    /// <para>
    /// 需要 <see cref="PluginCapability.Process"/> 能力，否则调用抛
    /// <see cref="PluginCapabilityDeniedException"/>。想优雅降级就先问
    /// <see cref="IHostInfo.HasCapability"/>。
    /// </para>
    /// </summary>
    IHostCommandService Commands { get; }

    /// <summary>
    /// Shell 上下文动词（对活动资源管理器窗口的选中项执行操作）。
    /// 与 <see cref="Commands"/> 一样需要 <see cref="PluginCapability.Process"/> 能力。
    /// </summary>
    IHostShellService Shell { get; }

    /// <summary>
    /// 窗口控制（平铺 / 置顶 / 透明度 / 搬到下一屏 / 激活任务栏槽位）。
    /// 执行面需要 <see cref="PluginCapability.WindowControl"/> 能力；
    /// 元数据面（<see cref="IHostWindowService.Layouts"/> 等）始终可读，理由见该接口。
    /// </summary>
    IHostWindowService Windows { get; }

    /// <summary>
    /// 屏幕截取（框选截屏 + 文字识别）。
    /// 执行面需要 <see cref="PluginCapability.ScreenCapture"/> 能力。
    /// </summary>
    IHostScreenCaptureService ScreenCapture { get; }

    /// <summary>
    /// 系统功能（最小化 / 任务视图 / 音量 / 锁屏 / 关机 …）。
    /// 执行面需要 <see cref="PluginCapability.InputSimulation"/> 能力；
    /// 预设清单（<see cref="IHostSystemService.Presets"/>）始终可读，理由见该接口。
    /// </summary>
    IHostSystemService System { get; }

    /// <summary>
    /// 轮盘呼出（在指定屏幕位置呼出用户的轮盘，由用户选扇区执行）。
    /// 需要 <see cref="PluginCapability.Wheel"/> 能力，理由见 <see cref="IHostWheelService"/>。
    /// </summary>
    IHostWheelService Wheel { get; }

    /// <summary>
    /// 键盘重映射（由宿主管理的进程级按键重映射会话，SDK 1.7 起）。
    /// 需要 <see cref="PluginCapability.InputRemapping"/> 能力。
    /// </summary>
    IHostKeyboardRemapService KeyboardRemap { get; }

    /// <summary>宿主环境信息。</summary>
    IHostInfo Info { get; }

    /// <summary>非侵入式通知。</summary>
    INotificationService Notify { get; }

    /// <summary>宿主事件订阅（返回 token，须在 <c>Shutdown</c> 中释放）。</summary>
    IPluginEvents Events { get; }

    /// <summary>UI 线程调度。</summary>
    IDispatcherFacade Dispatcher { get; }
}
