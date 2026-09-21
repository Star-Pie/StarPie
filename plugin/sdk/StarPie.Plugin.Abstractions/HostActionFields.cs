namespace StarPie.Plugin;

/// <summary>
/// 宿主动作参数的<b>持久化字段名</b>。
/// <para>
/// <b>为什么需要它</b>：<c>ActionItem</c> 上的动作参数长期是若干<b>裸字段</b>
/// （<c>Parameter</c> / <c>Arguments</c> / <c>CommandTerminal</c> …），而全项目有近两百处引用它们，
/// 整体搬进 <see cref="IActionContribution"/> 能用的 <c>ExtensionData</c> 字典是一次高风险大改动。
/// 于是宿主在调用动作实现之前，会把这几个字段<b>现读现装</b>成一份参数字典，
/// 键就是这里的常量值。
/// </para>
/// <para>
/// <b>只在一种插件上适用</b>：<b>随包分发、并认领了顶层动作类型</b>的那种。
/// 这类插件在语义上就是宿主的一部分（只是物理上分了个 DLL），说的自然是宿主的语言。
/// 社区插件不受这套约束 —— 它们用 <c>Type="Plugin"</c>，参数写 <c>ExtensionData</c>，
/// 键由插件自己起语义化名字（如 <c>startTime</c>）。
/// </para>
/// <para>
/// <b>用常量而不是裸字符串的理由</b>：宿主若重命名了某个字段，插件侧应当变成编译错误，
/// 而不是在运行期静静地取到空值。这些都是 <c>const</c>，编译进程序集，
/// 改名会立刻炸在构建上 —— 这正是我们要的失败方式。
/// </para>
/// <para>
/// 取值一律用<b>不变文化</b>的字符串字面量（布尔写 <c>"true"</c>/<c>"false"</c>，数字写 <c>0.5</c>），
/// 用 <see cref="PluginActionInput.Bool"/> / <see cref="PluginActionInput.Int"/> 这类
/// 已经处理过区域设置的方法去读。
/// </para>
/// </summary>
public static class HostActionFields
{
    /// <summary>
    /// 主参数。语义由动作自己解释：快捷键动作里它是组合键，启动程序里它是可执行文件路径，
    /// 打开网址里它是 URL，打开文件夹里它是目录路径，运行命令里它是命令行。
    /// </summary>
    public const string Parameter = "Parameter";

    /// <summary>命令行参数 / 启动参数。当前由「启动程序」使用。</summary>
    public const string Arguments = "Arguments";

    /// <summary>运行命令时使用的终端。取值见宿主终端下拉，如 <c>cmd</c> / <c>powershell</c> / <c>wsl</c>。</summary>
    public const string CommandTerminal = "CommandTerminal";

    /// <summary>打开网址时使用的浏览器。取值 <c>Default</c> / <c>Chrome</c> / <c>Edge</c> / <c>Firefox</c> / <c>Custom</c>。</summary>
    public const string BrowserChoice = "BrowserChoice";

    /// <summary>自定义浏览器可执行文件完整路径；仅当 <see cref="BrowserChoice"/> 为 <c>Custom</c> 时生效。</summary>
    public const string BrowserPath = "BrowserPath";

    /// <summary>是否通过 Shell 令牌降权启动，取值 <c>"true"</c> / <c>"false"</c>。当前由「启动程序」使用。</summary>
    public const string RunAsStandardUser = "RunAsStandardUser";

    /// <summary>
    /// 全部字段名。宿主用它构建投影字典，自检用它核对「这里列出的每一个键都真的能被投影出来」——
    /// 漏一个字段的表现是插件永远读到空值，且没有任何报错，属于最难发现的一类缺陷。
    /// </summary>
    public static readonly string[] All =
    {
        Parameter,
        Arguments,
        CommandTerminal,
        BrowserChoice,
        BrowserPath,
        RunAsStandardUser,
    };
}
