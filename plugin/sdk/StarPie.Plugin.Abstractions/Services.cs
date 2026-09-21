namespace StarPie.Plugin;

/// <summary>
/// 契约违约异常。由宿主在插件调用注册 API 不合法时抛出。
/// <para>
/// 抛出后宿主会把这个插件整体标记为加载失败并卸载 —— 不做「部分注册」，
/// 避免留下一个状态半残、用户无法解释的插件。
/// </para>
/// </summary>
public sealed class PluginContractException : Exception
{
    public PluginContractException(string message) : base(message) { }
    public PluginContractException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// 插件调用了<b>自己在清单里没有声明</b>的宿主能力。
/// <para>
/// <b>刻意不继承 <see cref="PluginContractException"/></b>，这是本类型存在的全部理由。
/// 后者的语义是「插件违反了注册契约」，宿主会因此把插件<b>整体标记为加载失败并卸载</b>。
/// 而「清单里漏了一行能力声明」远不到那个程度 —— 若继承它，用户看到的现象会是
/// 「插件突然坏了 / 被系统禁用了」，而真实原因是清单少写了一个词，
/// 排查方向会完全跑偏。
/// </para>
/// <para>
/// 所以这里是一个普通的、可被捕获的异常：只有那一次动作执行失败，
/// 日志与异常消息里带着可操作的修复指引，宿主与插件都照常活着。
/// </para>
/// </summary>
public sealed class PluginCapabilityDeniedException : Exception
{
    public PluginCapabilityDeniedException(PluginCapability capability, string serviceName, string pluginId)
        : base($"插件 \"{pluginId}\" 调用了 {serviceName}，但它的清单未声明 \"{capability}\" 能力。"
               + $"请在 plugin.json 的 capabilities 数组里加入 \"{capability}\" 后重新安装。")
    {
        Capability = capability;
        ServiceName = serviceName;
        PluginId = pluginId;
    }

    /// <summary>缺少的能力。</summary>
    public PluginCapability Capability { get; }

    /// <summary>被调用的服务接口名。</summary>
    public string ServiceName { get; }

    /// <summary>发起调用的插件 ID。</summary>
    public string PluginId { get; }
}

/// <summary>插件专属日志。宿主会自动加上 <c>[plugin:&lt;id&gt;]</c> 前缀并落盘到独立文件，同时做限流。</summary>
public interface IPluginLogger
{
    void Debug(string message);
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? exception = null);

    /// <summary>该插件日志文件路径（便于在加载失败时提示用户去查看）。</summary>
    string LogFilePath { get; }
}

/// <summary>
/// 插件私有配置。宿主读写 <c>plugins\&lt;id&gt;\settings.json</c>，与主 <c>config.json</c> 完全隔离。
/// <para>键值一律用字符串，避免插件自定义类型进入持久化层。修改后需调用 <see cref="Save"/>。</para>
/// </summary>
public interface IPluginSettings
{
    string? Get(string key);
    void Set(string key, string? value);

    bool GetBool(string key, bool defaultValue = false);
    int GetInt(string key, int defaultValue = 0);
    double GetDouble(string key, double defaultValue = 0);

    /// <summary>把所有未保存的修改落盘（原子写）。</summary>
    void Save();
}

/// <summary>非侵入式通知服务。宿主优先走托盘气泡，没有托盘时降级为日志。</summary>
public interface INotificationService
{
    /// <summary>提示一条信息。<paramref name="message"/> 建议不超过 80 字。</summary>
    void Notify(string title, string message);
}

/// <summary>宿主环境信息（只读）。</summary>
public interface IHostInfo
{
    /// <summary>StarPie 主程序版本，例如 <c>1.7.4</c>。</summary>
    string HostVersion { get; }

    /// <summary>SDK 契约版本，例如 <c>1.0</c>。</summary>
    string ApiVersion { get; }

    /// <summary>当前界面语言代码，例如 <c>zh-CN</c> / <c>en</c>。</summary>
    string LanguageCode { get; }

    /// <summary>StarPie 是否以管理员权限运行。</summary>
    bool IsElevated { get; }

    /// <summary>是否为便携模式（插件根目录位于程序目录下）。</summary>
    bool IsPortable { get; }

    /// <summary>主程序可执行文件路径。单文件发布形态下可能为空。</summary>
    string HostExecutablePath { get; }

    /// <summary>
    /// 本插件是否声明了某项能力（读的是自己清单里的 <c>capabilities</c>）。
    /// <para>
    /// 需要该能力才能工作的插件<b>应当先问这里再动手</b>，而不是等宿主抛
    /// <see cref="PluginCapabilityDeniedException"/> —— 前者能给用户一句
    /// 「本插件需要「进程」能力」的说明，后者只能让那次动作静默失败。
    /// </para>
    /// </summary>
    bool HasCapability(PluginCapability capability);
}

/// <summary>
/// 宿主事件订阅。<b>所有订阅都必须持有返回的 token 并在 <c>Shutdown</c> 中释放</b> ——
/// 这是可回收 ALC 能否真正卸载的决定性因素。
/// </summary>
public interface IPluginEvents
{
    /// <summary>界面语言切换。回调在 UI 线程触发，参数为新语言代码。</summary>
    IDisposable OnLanguageChanged(Action<string> handler);

    /// <summary>
    /// 轮盘即将呈现。回调<b>保证不在钩子线程</b>（宿主已切到 UI 线程），
    /// 但仍在呼出路径上，因此必须极快，禁止 IO。
    /// </summary>
    IDisposable OnWheelOpening(Action<ActionContext> handler);

    /// <summary>轮盘关闭后。回调在 UI 线程触发。</summary>
    IDisposable OnWheelClosed(Action handler);
}

/// <summary>UI 线程调度门面。插件若持有后台线程并需要触碰 UI，必须经此切回。</summary>
public interface IDispatcherFacade
{
    bool IsOnUiThread { get; }

    /// <summary>投递到 UI 线程（不等结果）。UI 线程不可用时静默忽略。</summary>
    void Post(Action action);

    /// <summary>在 UI 线程执行并等待完成。</summary>
    Task InvokeAsync(Action action);
}

/// <summary>
/// 宿主已验证的动作能力。插件做「发快捷键 / 启程序 / 开文件夹 / 操作剪贴板 / 开网址」时
/// <b>必须</b>走这里，禁止自己 P/Invoke <c>SendInput</c> 或 <c>Process.Start</c>。
/// <para>
/// 原因：主程序内部已解决硬件扫描码映射、修饰键 10~15ms 时延保持、扩展键标志、
/// Unicode 字符流注入、提权降权令牌等一堆坑。插件自己重写一遍不仅会踩坑，
/// 还可能因为与主程序的全局钩子互相干扰而形成死循环。
/// </para>
/// <para>
/// <b>线程约束</b>：只有 <see cref="ActionKind.Sequential"/> 类动作可以调用这些方法；
/// 后台类动作调用会造成输入序列与前台动作交叉，宿主不为此负责。
/// </para>
/// </summary>
public interface IHostActionInvoker
{
    /// <summary>发送组合键，写法如 <c>"Ctrl+Shift+G"</c>、<c>"Win+D"</c>、<c>"F5"</c>。返回是否成功下发。</summary>
    bool SendHotkey(string hotkey);

    /// <summary>以 Unicode 字符流逐字输入文本，规避输入法阻断。</summary>
    bool SendText(string text);

    /// <summary>启动程序或打开文档。<paramref name="runAsStandardUser"/> 为 true 时通过 Shell 令牌降权启动。</summary>
    bool Launch(string path, string arguments = "", bool runAsStandardUser = false);

    /// <summary>在资源管理器中打开文件夹（不存在则尝试创建）。</summary>
    bool OpenFolder(string folderPath);

    /// <summary>用指定浏览器打开网址。<paramref name="browserChoice"/> 取值 Default/Chrome/Edge/Firefox/Custom。</summary>
    bool OpenUrl(string url, string browserChoice = "Default", string? customBrowserPath = null);

    /// <summary>写入剪贴板（带回退重试）。</summary>
    bool SetClipboardText(string text);

    /// <summary>读取剪贴板文本；无文本内容时返回 null。</summary>
    string? GetClipboardText();
}

/// <summary>一个终端选项。</summary>
public sealed class CommandTerminalOption
{
    /// <summary>标识，如 <c>cmd</c> / <c>powershell_hidden</c>。写进配置的就是这个值。</summary>
    public string Id { get; init; } = "";

    /// <summary>显示名（宿主已按当前语言本地化）。</summary>
    public string DisplayName { get; init; } = "";
}

/// <summary>
/// 宿主已验证的命令执行能力：在指定终端里跑一段命令。
/// <para>
/// <b>这是 SDK 里唯一「参数即任意命令」的攻击面</b>，所以它比
/// <see cref="IHostActionInvoker"/> 多一道门 —— 清单必须声明
/// <see cref="PluginCapability.Process"/>，否则调用抛
/// <see cref="PluginCapabilityDeniedException"/>。
/// </para>
/// <para>
/// <b>但这不构成沙箱，也请不要把它当成沙箱</b>：进程内插件本来就是普通 .NET 代码，
/// 绕开本接口直接 <c>Process.Start</c> 任何时候都做得到，SDK 拦不住。
/// 这道门换到的是另一件事 —— <b>让安装确认页上展示的能力真的对应一个后果</b>。
/// 用户同意安装时看到的「进程」标签，从此不是一句不产生任何后果的话。
/// </para>
/// <para>
/// <b>为什么只有新服务有门禁</b>：<see cref="IHostActionInvoker"/> 的七个方法是既有契约，
/// 给它们补门禁会让已发布、且没声明该能力的插件突然失败 —— 那是破坏性变更。
/// 门禁只能加在新引入的接口上。
/// </para>
/// <para>
/// <b>线程约束</b>：应只在 <see cref="ActionKind.Sequential"/> 类动作里调用。
/// </para>
/// </summary>
public interface IHostCommandService
{
    /// <summary>
    /// 可用的终端选项，<b>顺序即宿主动作编辑器里的下拉顺序</b> —— 插件直接照用即可与宿主界面一致。
    /// <para>
    /// 每次访问都按<b>当前语言</b>重新求值。要在 <c>Parameters</c> 里用它就必须写成属性
    /// （每次渲染重新取），<b>不要缓存到字段</b>，否则切换语言后下拉文案不跟着变。
    /// 实现必须极快且不得有 IO：它会在设置页滚动时被高频调用。
    /// </para>
    /// </summary>
    IReadOnlyList<CommandTerminalOption> Terminals { get; }

    /// <summary>在指定终端里执行命令。只负责<b>发起</b>，不等待命令结束。</summary>
    /// <param name="command">命令原文。</param>
    /// <param name="terminal">
    /// 终端标识，取值见 <see cref="Terminals"/>。调用方无需自行判断合法性：
    /// 未识别的值一律按 <c>cmd</c> 处理（为兼容早于本接口的历史配置）。
    /// </param>
    /// <returns>是否成功发起。失败原因记入宿主日志，<b>不弹对话框</b>（插件侧失败必须可忽略）。</returns>
    /// <exception cref="PluginCapabilityDeniedException">清单未声明 <see cref="PluginCapability.Process"/>。</exception>
    bool Run(string command, string terminal = "cmd");
}

/// <summary>一项 Shell 上下文动词。</summary>
public sealed class ShellVerbOption
{
    /// <summary>标识，如 <c>Windows.CopyAsPath</c>。写进配置的就是这个值。</summary>
    public string Id { get; init; } = "";

    /// <summary>显示名（宿主已按当前语言本地化）。</summary>
    public string DisplayName { get; init; } = "";
}

/// <summary>
/// 宿主已验证的 Shell 上下文动词能力：对<b>当前活动的资源管理器窗口及其选中项</b>执行操作
/// （复制路径、以管理员身份运行、在此处打开终端…）。
/// <para>
/// 与 <see cref="IHostCommandService"/> 一样需要 <see cref="PluginCapability.Process"/> ——
/// 其中若干动词会以提权方式启动进程。
/// </para>
/// <para>
/// <b>宿主自己的正式入口是带搜索与分类的专用挑选器</b>，本接口不做那个 UI：
/// 插件可以拿 <see cref="Verbs"/> 做下拉，也可以声明成自由文本让用户自己填。
/// SDK 只提供能力，不替插件决定怎么呈现。
/// </para>
/// </summary>
public interface IHostShellService
{
    /// <summary>
    /// 推荐的动词标识。<b>这不是白名单</b> —— <see cref="Invoke"/> 只挡空值。
    /// <para>
    /// 原因：历史配置里沉淀了大量旧别名（同一个动作既有 <c>Windows.CopyAsPath</c>
    /// 又有 <c>copy_path</c>），若按本清单校验，本是「防静默失效」的初衷会变成把老配置整体判死 ——
    /// 那是更糟的结果。所以清单只用于<b>呈现与推荐</b>。
    /// </para>
    /// </summary>
    IReadOnlyList<ShellVerbOption> Verbs { get; }

    /// <summary>执行一个上下文动词。只负责发起，不等待完成。</summary>
    /// <returns>是否成功发起（动词为空、或宿主判断当前上下文不适用时返回 false）。</returns>
    /// <exception cref="PluginCapabilityDeniedException">清单未声明 <see cref="PluginCapability.Process"/>。</exception>
    bool Invoke(string verb);
}

/// <summary>一项平铺布局。</summary>
public sealed class WindowLayoutOption
{
    /// <summary>布局码，如 <c>2L</c>（左半屏）、<c>4G</c>（四宫格）。写入动作参数的就是这个值。</summary>
    public string Key { get; init; } = "";

    /// <summary>显示名（宿主已按当前语言本地化）。</summary>
    public string DisplayName { get; init; } = "";
}

/// <summary>
/// 宿主已验证的窗口控制能力：操作<b>其它程序</b>的前台窗口 ——
/// 平铺、置顶、改透明度、搬到下一块显示器、激活任务栏上的第 N 个应用。
/// <para>
/// 需要 <see cref="PluginCapability.WindowControl"/>，否则执行面抛
/// <see cref="PluginCapabilityDeniedException"/>。
/// </para>
/// <para>
/// <b>元数据面（<see cref="Layouts"/> 与三个标记）刻意不受门禁约束</b>：
/// 插件的 <c>Parameters</c> 是<b>属性</b>，注册期就会被读取，在那里抛异常
/// 会让一个「忘了声明能力」的插件在注册阶段整个崩掉 —— 而它其实只是不能在运行时干活。
/// 这与 <see cref="IHostCommandService.Terminals"/> / <see cref="IHostShellService.Verbs"/>
/// 是同一条理由。门禁拦的是<b>产生后果</b>的调用。
/// </para>
/// <para>
/// <b>同样不构成沙箱</b>：进程内插件随时可以自己 P/Invoke <c>SetWindowPos</c>。
/// 这道门换到的是「安装确认页上的『窗口控制』标签真的对应一个后果」。
/// </para>
/// <para>
/// <b>线程约束</b>：应只在 <see cref="ActionKind.Sequential"/> 类动作里调用。
/// </para>
/// </summary>
public interface IHostWindowService
{
    /// <summary>
    /// 可选布局清单。<b>这是布局表的唯一来源</b>——插件请直接用它生成下拉，
    /// 不要在插件里另抄一份：抄一份的后果是「宿主加了新布局，插件下拉里没有」，
    /// 或反过来「插件里能选，宿主执行体不认」，两种都是静默失效。
    /// </summary>
    IReadOnlyList<WindowLayoutOption> Layouts { get; }

    /// <summary>循环切换到下一个布局的标记，作为 <see cref="ApplyLayout"/> 的参数使用。</summary>
    string CycleToken { get; }

    /// <summary>反向循环的标记。</summary>
    string CycleBackToken { get; }

    /// <summary>还原所有窗口到平铺前状态的标记。</summary>
    string RestoreToken { get; }

    /// <summary>
    /// 透明度的合法下界（百分比）。<b>用它声明动作参数的 <c>Min</c></b>，
    /// 不要在插件里另写一个数字：范围只该有一处定义 ——
    /// 宿主把上界从 100 调到 90 之后，插件里那份写死的范围会继续把 95 判成合法，
    /// 并把这个过时的范围展示给用户。
    /// </summary>
    double OpacityMinPercent { get; }

    /// <summary>透明度的合法上界（百分比，100 = 完全不透明）。</summary>
    double OpacityMaxPercent { get; }

    /// <summary>
    /// 应用一次平铺。<paramref name="layoutKey"/> 既可以是 <see cref="Layouts"/> 里的布局码，
    /// 也可以是上面三个标记之一 —— 它们是「对布局的操作」，不是「一种布局」，
    /// 但最终都由同一个执行体分派。
    /// </summary>
    /// <returns>是否成功发起。</returns>
    /// <exception cref="PluginCapabilityDeniedException">清单未声明 <see cref="PluginCapability.WindowControl"/>。</exception>
    bool ApplyLayout(string layoutKey);

    /// <summary>切换前台窗口的「始终置顶」状态。</summary>
    /// <exception cref="PluginCapabilityDeniedException">清单未声明 <see cref="PluginCapability.WindowControl"/>。</exception>
    bool ToggleTopmost();

    /// <summary>把前台窗口移到下一块显示器。</summary>
    /// <exception cref="PluginCapabilityDeniedException">清单未声明 <see cref="PluginCapability.WindowControl"/>。</exception>
    bool MoveToNextMonitor();

    /// <summary>
    /// 设置前台窗口的不透明度。
    /// <para>
    /// 参数是<b>字符串</b>而不是 <c>int</c>：解析与钳制规则（1~100，非法值如何处理）
    /// 只存在于宿主执行体一处，SDK 不复制第二份。插件的参数本来就是字符串，
    /// 原样透传即可；若在这里声明成 <c>int</c>，插件就不得不先解析一遍，
    /// 于是同一个规则有了两个实现 —— 迟早不一致。
    /// </para>
    /// </summary>
    /// <param name="percent">1~100 的百分比字面量，例如 <c>"80"</c>。</param>
    /// <exception cref="PluginCapabilityDeniedException">清单未声明 <see cref="PluginCapability.WindowControl"/>。</exception>
    bool SetOpacity(string percent);

    /// <summary>
    /// 激活任务栏上第 <paramref name="slotIndex"/> 个应用（从 1 开始，从左往右数）。
    /// </summary>
    /// <exception cref="PluginCapabilityDeniedException">清单未声明 <see cref="PluginCapability.WindowControl"/>。</exception>
    bool ActivateTaskbarSlot(int slotIndex);
}

/// <summary>
/// 屏幕截取服务。
/// <para>
/// 它目前只做一件事：让用户现场框选一块屏幕区域、识别其中的文字，结果按宿主的 OCR 设置处理
/// （复制到剪贴板 / 弹出结果窗口等）。刻意做成「一个动作一个方法」的形状 ——
/// 这是为那个动作外移而生的接缝，不是通用截图 API：真要做通用截图能力，
/// 区域表达、图片格式、返回值都是另一套设计，届时单独添加而不是把这个撑大。
/// </para>
/// </summary>
public interface IHostScreenCaptureService
{
    /// <summary>
    /// 发起一次「框选截屏 + 文字识别」。
    /// <para>
    /// <b>无参数是刻意的</b>：识别区域由用户在按下之后现场框选，没有任何需要事先保存的配置 ——
    /// 所以那个动作本身也就没有参数。
    /// </para>
    /// <para>
    /// <b>返回 <c>void</c> 也是刻意的</b>：框选要等用户操作，识别更是异步的，
    /// 这个调用根本无法同步取得结论。返回 <c>bool</c> 只能表示「有没有成功发起」，
    /// 那是一个很容易被误读成「识别成功了吗」的假信号 —— 不如不返回。
    /// 识别结果走宿主自己的出口（剪贴板 / 结果窗口）。
    /// </para>
    /// </summary>
    /// <exception cref="PluginCapabilityDeniedException">清单未声明 <see cref="PluginCapability.ScreenCapture"/>。</exception>
    void CaptureAndRecognize();
}

/// <summary>一项系统功能预设。</summary>
public sealed class SystemPresetOption
{
    /// <summary>预设键，如 <c>Minimize</c>、<c>TaskView</c>、<c>Shutdown</c>。写进动作参数的就是这个值。</summary>
    public string Key { get; init; } = "";

    /// <summary>显示名（宿主已按当前语言与分类拼好，与宿主设置页里的下拉逐字一致）。</summary>
    public string DisplayName { get; init; } = "";
}

/// <summary>
/// 宿主已验证的系统功能能力：触发一个预设 —— 最小化 / 最大化 / 任务视图 / 音量 / 媒体控制 /
/// 锁屏 / 关机 / 重启 / 任务管理器 / 计算器 …
/// <para>
/// 需要 <see cref="PluginCapability.InputSimulation"/>，否则执行面抛
/// <see cref="PluginCapabilityDeniedException"/>。
/// </para>
/// <para>
/// <b>为什么传的是一个预设键，而不是让插件自己发快捷键</b>：
/// 这张表里有一部分确实就是组合键（最小化 = <c>Win+Down</c>），但也有相当一部分
/// <b>根本不是</b>快捷键 —— 任务管理器、资源管理器、计算器、关机、重启、睡眠都是起进程，
/// 打开星梦设置与快速搜索则是让宿主弹自己的窗口。<c>RunPreset</c> 的统一语义是
/// 「宿主，请替我执行这个系统功能」，把实现细节留在宿主里。
/// </para>
/// <para>
/// 附带的好处是<b>历史别名只能由宿主认识</b>：配置里沉淀了 <c>SnapLeft</c> / <c>靠左分屏</c> /
/// <c>lock</c> / <c>锁屏</c> / <c>starpie控制台</c> 这类跨年代写法，
/// 让插件自己翻译是翻译不出来的 —— 它只能照抄宿主那份 <c>switch</c>，而那份会变。
/// </para>
/// </summary>
public interface IHostSystemService
{
    /// <summary>
    /// 预设清单（有序），就是宿主自己那个下拉的<b>同一份数据源</b>。
    /// <para>
    /// 插件的 <c>Parameters</c> 应当直接用它生成选项，<b>不要另抄一份</b>：
    /// 抄一份的代价是宿主以后加预设时要改两处，漏一处就会出现
    /// 「新预设在这个面板里能选、在那个面板里选不到」的分裂，而且不会有任何报错。
    /// </para>
    /// <para>
    /// 与其它服务的元数据面同理，<b>刻意不受门禁约束</b>：
    /// <c>Parameters</c> 是属性，注册期就会被读取，在这里抛异常会让一个
    /// 「忘了声明能力」的插件在注册阶段整个崩掉 —— 而它其实只是不能在运行时干活。
    /// </para>
    /// <para>
    /// <b>这不是白名单</b>，见 <see cref="RunPreset"/>。
    /// </para>
    /// </summary>
    IReadOnlyList<SystemPresetOption> Presets { get; }

    /// <summary>
    /// 执行一个系统功能预设。只负责<b>发起</b>，不等待结果。
    /// <para>
    /// <b>返回值是「有没有匹配到一个已实现的预设」，不是「执行成功了没有」</b> ——
    /// 关机要几秒、任务管理器要等它启动，这些都无法在这里同步判定。
    /// 返回 <c>false</c> 的可操作含义是：<b>这个键已经不认识了，多半是配置过期</b>。
    /// 插件应当据此给用户一句人话，而不是让按下扇区后什么都没发生。
    /// </para>
    /// <para>
    /// <b>不按 <see cref="Presets"/> 校验</b>：那张表里没有 <c>SnapLeft</c> 的历史别名，
    /// 按表校验会把老配置整体判死 —— 本想防静默失效，结果造出一个更糟的静默失效。
    /// </para>
    /// </summary>
    /// <param name="presetKey">预设键（大小写不敏感，两端空白会被忽略）。</param>
    /// <returns>是否匹配到一个预设并已发起。空键、或键无任何匹配时为 <c>false</c>。</returns>
    /// <exception cref="PluginCapabilityDeniedException">清单未声明 <see cref="PluginCapability.InputSimulation"/>。</exception>
    bool RunPreset(string presetKey);
}

/// <summary>
/// 轮盘呼出服务：在屏幕上的一个指定位置呼出<b>用户自己的轮盘</b>，让用户用鼠标选一个扇区执行。
/// <para>
/// 它是为「悬浮球」这类常驻小窗形态准备的接缝：球本身由插件用 <see cref="PluginCapability.Ui"/>
/// 画，点球之后要发生的是「轮盘出现」—— 而轮盘（外观、扇区、命中、动作分派）整体归宿主所有，
/// 插件既拿不到也不该拿，否则每个做悬浮球的插件都要复刻一遍轮盘，宿主一改布局全数漂移。
/// </para>
/// <para>
/// <b>呼出的是粘滞会话，不是伪造的手势</b>：轮盘出现后光标完全自由，悬停高亮、按下即执行、
/// 点环外即取消；期间宿主的真实手势被短暂挂起（同屏只允许一个轮盘）。会话不绑定插件生命周期 ——
/// 插件被停用/卸载时已经呼出的轮盘照常可用，因为选盘执行的每个动作都来自用户配置，
/// 插件此刻并不在调用栈上。
/// </para>
/// <para>
/// 需要 <see cref="PluginCapability.Wheel"/>，否则抛 <see cref="PluginCapabilityDeniedException"/>。
/// </para>
/// </summary>
public interface IHostWheelService
{
    /// <summary>
    /// 在指定的<b>物理像素</b>屏幕坐标（虚拟屏幕坐标系，与 Win32 <c>GetCursorPos</c> 同系）
    /// 呼出轮盘。
    /// <para>
    /// <b>返回 <c>true</c> 的含义是「宿主已受理并排队呈现」</b>，不是「轮盘已经出现」——
    /// 建窗与入场动画在 UI 线程上完成，本调用本身不同步等待它。返回 <c>false</c> 的当下
    /// 可操作含义只有一条：<b>别改自己的视觉状态去配合一个没出现的轮盘</b>。
    /// 常见失败原因：已有一次真实手势在进行、坐标不在任何显示器范围内、宿主处于无界面自检模式。
    /// 上一版已受理的呼出会被<b>整体替换</b>（旧会话静默收掉，新位置重新呼出），
    /// 所以连点悬浮球不需要插件自己先调 <see cref="DismissWheel"/>。
    /// </para>
    /// </summary>
    /// <param name="physicalCenterX">轮盘圆心的物理像素横坐标（虚拟屏幕坐标系，可为负）。</param>
    /// <param name="physicalCenterY">轮盘圆心的物理像素纵坐标。</param>
    /// <exception cref="PluginCapabilityDeniedException">清单未声明 <see cref="PluginCapability.Wheel"/>。</exception>
    bool ShowWheel(double physicalCenterX, double physicalCenterY);

    /// <summary>
    /// 收掉由本插件上一次 <see cref="ShowWheel"/> 呼出的轮盘。
    /// <para>
    /// 没有它也能活：用户点扇区或点环外时宿自主动收。留着它是为了「再点一次球收起轮盘」这种
    /// 切换式交互 —— 插件自己记不住轮盘是不是已经被用户点掉了，所以 <b>无条件调用即可，
    /// 没有活动会话时它是无害的空操作</b>。返回 <c>true</c> 仅表示确实收掉过一个会话。
    /// </para>
    /// </summary>
    /// <exception cref="PluginCapabilityDeniedException">清单未声明 <see cref="PluginCapability.Wheel"/>。</exception>
    bool DismissWheel();
}
