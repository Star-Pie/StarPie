namespace StarPie.Plugin;

/// <summary>
/// <c>plugin.json</c> 清单的内存模型。宿主与打包/校验工具共用这一份定义，避免字段语义漂移。
/// <para>
/// 所有字段采用 <c>PascalCase</c> 属性名 + <c>PropertyNameCaseInsensitive</c> 反序列化，
/// 与主程序既有的 <c>ConfigManager</c> 约定保持一致。
/// </para>
/// </summary>
public sealed class PluginManifest
{
    /// <summary>清单结构版本，必须被宿主支持（当前为 <see cref="PluginApi.ManifestSchemaVersion"/>）。</summary>
    public int SchemaVersion { get; set; } = PluginApi.ManifestSchemaVersion;

    /// <summary>全局唯一 ID，反向域名风格，如 <c>com.example.pomodoro</c>。</summary>
    public string Id { get; set; } = "";

    /// <summary>显示名。</summary>
    public string Name { get; set; } = "";

    /// <summary>一句话简介。</summary>
    public string Description { get; set; } = "";

    /// <summary>Optional UI translations readable without loading the plugin; absent locales keep the original labels.</summary>
    public Dictionary<string, string> NameTranslations { get; set; } = new();
    public Dictionary<string, string> DescriptionTranslations { get; set; } = new();

    /// <summary>作者或组织。</summary>
    public string Author { get; set; } = "";

    /// <summary>项目主页。</summary>
    public string? Homepage { get; set; }

    /// <summary>SPDX 许可证标识，如 <c>MIT</c>。</summary>
    public string License { get; set; } = "MIT";

    /// <summary>插件版本（语义化），如 <c>1.0.0</c>。</summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>编译所依赖的 SDK 契约版本，如 <c>1.0</c>。主版本必须与宿主一致。</summary>
    public string ApiVersion { get; set; } = PluginApi.ApiVersion;

    /// <summary>最低可运行的宿主版本。</summary>
    public string MinHostVersion { get; set; } = "0.0.0";

    /// <summary>最高可运行的宿主版本；留空表示不限。</summary>
    public string? MaxHostVersion { get; set; }

    /// <summary>目标框架。宿主会做「不高于宿主」比较，如 <c>net8.0-windows</c> / <c>net8.0-windows10.0.19041.0</c>。</summary>
    public string TargetFramework { get; set; } = "net8.0-windows";

    /// <summary>目标平台，必须为 <c>win-x64</c>。</summary>
    public string Platform { get; set; } = "win-x64";

    /// <summary>
    /// 入口程序集文件名（相对插件目录）。留空时按「插件目录下唯一的 <c>StarPie.Plugin.*.dll</c>，
    /// 否则取 <c>&lt;id&gt;.dll</c>」推断。
    /// </summary>
    public string? Assembly { get; set; }

    /// <summary>
    /// <see cref="IStarPiePlugin"/> 实现类型的全名。留空时按默认命名约定推断；推断到 0 个或多个实现即拒绝加载。
    /// </summary>
    public string? EntryType { get; set; }

    /// <summary>
    /// 运行时形态。缺省 <c>"assembly"</c> —— 存量插件一个字都不用改。
    /// <para>
    /// <c>"assembly"</c>：托管程序集，进程内加载进 <c>AssemblyLoadContext</c>（原路径）。
    /// </para>
    /// <para>
    /// <c>"process"</c>：独立进程，由宿主按 <see cref="Command"/> 拉起。这条形态是为
    /// Rust / Go / C++ / Python 等无法产出 .NET 程序集的语言准备的 —— 宿主不加载任何代码，
    /// 只负责触发与生命周期。代价是插件拿不到 <see cref="IPluginContext"/> 下的宿主服务
    /// （上下文、窗口控制、剪贴板…），它必须自己实现这些。
    /// </para>
    /// <para>
    /// <b>为什么不叫 <c>kind</c> 或 <c>type</c></b>：清单里 <c>type</c> 已经指「动作类型」，
    /// 再用一次会让「进程插件」和「动作类型」两件事在排查时混为一谈。
    /// </para>
    /// </summary>
    public string Runtime { get; set; } = PluginRuntimeTypes.Assembly;

    /// <summary>
    /// 进程形态的启动命令。<c>[0]</c> 是可执行文件（相对插件目录，或绝对路径），其余是参数。
    /// <para>
    /// 参数里可用占位符，宿主在每次执行前替换：<c>{action}</c>（本次触发的贡献点短 ID）、
    /// <c>{languageCode}</c>、<c>{foregroundProcess}</c>、<c>{foregroundTitle}</c>、
    /// <c>{cursorX}</c>、<c>{cursorY}</c>。未知占位符<b>原样保留</b>而不报错 ——
    /// 让插件能用 <c>{...}</c> 表达自己的字面量，也比因为宿主少支持一个占位符就整个动作失败要好。
    /// </para>
    /// <para>
    /// <b>为什么不做成 "InvokeArgs" 字典 + 固定追加参数</b>：那样每种语言、每个插件都得
    /// 接受宿主钦定的一套 flag 顺序；把参数完全交给清单写死，插件作者想要什么形态就是什么形态，
    /// 宿主也不需要为某个插件特判。
    /// </para>
    /// </summary>
    public List<string> Command { get; set; } = new();

    /// <summary>
    /// <b>仅 <see cref="Runtime"/> = <c>"process"</c> 时使用</b>：该进程贡献的动作列表。
    /// <para>
    /// 程序集形态的动作用 <see cref="IActionContribution"/> 在运行时自描述；进程形态没有托管对象，
    /// 于是把同样的信息搬到清单里。字段刻意与 <see cref="ActionDescriptor"/> 一一对应，
    /// 将来接 JSON-RPC 时这份声明可以直接由插件在握手里返回，宿主侧不需要第二套模型。
    /// </para>
    /// <para>
    /// 与 <see cref="PluginContributions.Actions"/> 那个 <c>bool</c> 无关：那个是安装确认页的
    /// 数量提示，这里是真实路由数据。
    /// </para>
    /// </summary>
    public List<PluginProcessAction> Actions { get; set; } = new();

    /// <summary>能力声明。见 <see cref="PluginCapability"/>。</summary>
    public List<string> Capabilities { get; set; } = new();

    /// <summary>
    /// 本插件认领的顶层动作类型。只有随包分发的插件可以使用，社区插件请留空。
    /// <para>
    /// 认领之后，用户在配置里写的 <c>Type="Command"</c> 就由本插件负责执行 ——
    /// 宿主通过 <see cref="PluginApi.TypeClaimsMetadataKey"/> 读静态元数据建立这张表，
    /// <b>不需要加载程序集</b>。
    /// </para>
    /// <para>
    /// 停用插件会让这些类型整体失效（宿主会明确提示，而不是静默无操作）。
    /// 不要认领 <c>"Plugin"</c> —— 那是社区插件动作的保留类型名。
    /// </para>
    /// </summary>
    public List<PluginTypeClaim> ClaimedTypes { get; set; } = new();

    /// <summary>贡献点预声明（用于安装确认页展示与运行时交叉校验）。</summary>
    public PluginContributions Contributions { get; set; } = new();

    /// <summary>插件间依赖。</summary>
    public List<PluginDependency> Dependencies { get; set; } = new();

    /// <summary>插件图标（相对路径，SVG/PNG）。</summary>
    public string? Icon { get; set; }

    /// <summary>分类标签，最多 8 个。</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>面向插件详情页的静态功能说明；不参与运行时动作路由。</summary>
    public List<PluginFeature> Features { get; set; } = new();

    /// <summary>入口程序集的 SHA256（发布时由打包脚本回写）。留空表示不做完整性校验。</summary>
    public string? Sha256 { get; set; }

    /// <summary>把 <see cref="Capabilities"/> 解析为标志枚举；无法识别的项会被忽略。</summary>
    public PluginCapability ResolveCapabilities()
    {
        PluginCapability result = PluginCapability.None;
        foreach (string raw in Capabilities)
        {
            if (Enum.TryParse(raw?.Trim(), ignoreCase: true, out PluginCapability one))
            {
                result |= one;
            }
        }
        return result;
    }

    /// <summary>返回 <see cref="Capabilities"/> 中无法识别的项（用于安装时提示「清单含未知能力声明」）。</summary>
    public List<string> GetUnknownCapabilities()
    {
        var unknown = new List<string>();
        foreach (string raw in Capabilities)
        {
            if (!Enum.TryParse(raw?.Trim(), ignoreCase: true, out PluginCapability _))
            {
                unknown.Add(raw ?? "");
            }
        }
        return unknown;
    }
}

/// <summary>清单里 <see cref="PluginManifest.Runtime"/> 的合法取值。</summary>
public static class PluginRuntimeTypes
{
    /// <summary>托管程序集，进程内加载。</summary>
    public const string Assembly = "assembly";

    /// <summary>独立进程，由宿主拉起。</summary>
    public const string Process = "process";

    /// <summary>全部合法取值，供清单校验与错误提示使用。</summary>
    public static readonly IReadOnlyList<string> All = new[] { Assembly, Process };

    /// <summary>
    /// 归一化清单里的取值（大小写不敏感）。
    /// <para>
    /// 未知取值返回 <c>false</c> 而<b>不是</b>静默退回 <c>assembly</c>：一个写成
    /// <c>"proc"</c> 的清单，如果被当成程序集形态加载，用户看到的会是
    /// 「找不到入口程序集」这种指向错误方向的报错。
    /// </para>
    /// </summary>
    public static bool TryNormalize(string? raw, out string normalized)
    {
        normalized = Assembly;
        if (string.IsNullOrWhiteSpace(raw)) return true;
        foreach (string candidate in All)
        {
            if (string.Equals(candidate, raw.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                normalized = candidate;
                return true;
            }
        }
        return false;
    }
}

/// <summary>
/// 进程插件在清单里声明的一个动作。字段与 <see cref="ActionDescriptor"/> 对齐。
/// <para>
/// <b>没有参数表单</b>：v0.5 的进程插件拿不到宿主渲染的参数控件。需要可配置项时，
/// 要么等 JSON-RPC 的声明式设置页，要么自己在进程里读自己的配置文件 —— 后者是 Selinga 当前的做法。
/// </para>
/// </summary>
public sealed class PluginProcessAction
{
    /// <summary>插件内短 ID（小写驼峰）。宿主会归一化为 <c>&lt;pluginId&gt;.&lt;id&gt;</c>，并作为 <c>{action}</c> 注入命令行。</summary>
    public string Id { get; set; } = "";

    /// <summary>显示名（可直接写中文；支持 <see cref="PluginManifest.NameTranslations"/> 那套词条机制）。</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>列表里的副标题 / 说明。</summary>
    public string? Description { get; set; }

    /// <summary>动作下拉里的分组名，例如「效率工具」。留空归入「插件」。</summary>
    public string Category { get; set; } = "";

    /// <summary>图标。只接受主程序内置的矢量图标 key —— 进程插件无法注册 SVG。</summary>
    public string? IconKey { get; set; }

    /// <summary>
    /// 调度类别，<b>默认 <see cref="ActionKind.Background"/></b>。
    /// <para>
    /// 与程序集插件不同，这里必须默认后台：拉起进程会做磁盘 IO、可能弹窗，
    /// 落在唯一的串行动作线程上会把整个动作队列拖住。真要串行（比如必须先切走前台窗口）
    /// 由作者显式声明，而且要自己保证足够短。
    /// </para>
    /// </summary>
    public ActionKind Kind { get; set; } = ActionKind.Background;

    /// <summary>
    /// 放弃等待的秒数。进程形态<b>从不等待退出</b>，这个值只用于「启动失败」的判定窗口，
    /// 以及日志里区分「刚拉起」与「拉起超时」。填 0 用宿主默认（3 秒）。
    /// </summary>
    public int LaunchTimeoutSeconds { get; set; }
}

/// <summary>贡献点预声明。字段存的是「数量」，用于安装确认页快速告知用户插件会往界面里加什么。</summary>
public sealed class PluginContributions
{
    /// <summary>是否提供自定义动作。</summary>
    public bool Actions { get; set; }

    /// <summary>是否提供矢量图标包。</summary>
    public bool Icons { get; set; }

    /// <summary>是否注册多语言词条。</summary>
    public bool I18n { get; set; }

    /// <summary>是否提供轮盘渲染形态（P1，首版宿主会忽略并给出提示）。</summary>
    public bool Styles { get; set; }

    /// <summary>是否提供预设方案 / 配置模板（P1）。</summary>
    public bool Presets { get; set; }

    /// <summary>人类可读的贡献点摘要，用于安装确认卡与列表徽章。</summary>
    public string Describe()
    {
        var parts = new List<string>(4);
        if (Actions) parts.Add("自定义动作");
        if (Icons) parts.Add("图标包");
        if (I18n) parts.Add("多语言");
        if (Styles) parts.Add("渲染形态");
        if (Presets) parts.Add("预设模板");
        return parts.Count == 0 ? "无声明" : string.Join(" · ", parts);
    }
}

/// <summary>插件详情页使用的静态功能说明。保留官方 SDK 1.11 的公开契约。</summary>
public sealed class PluginFeature
{
    /// <summary>稳定的功能短 ID。</summary>
    public string Id { get; set; } = "";
    /// <summary>面向用户的功能名称。</summary>
    public string Name { get; set; } = "";
    /// <summary>功能说明。</summary>
    public string Description { get; set; } = "";
    /// <summary>功能分类。</summary>
    public string Category { get; set; } = "";
    /// <summary>可选图标 key。</summary>
    public string? IconKey { get; set; }
}

/// <summary>插件间依赖声明。</summary>
public sealed class PluginDependency
{
    /// <summary>被依赖插件的 ID。</summary>
    public string Id { get; set; } = "";

    /// <summary>版本区间，简化语义化语法：<c>"1.2.0"</c>（≥）、<c>"[1.0,2.0)"</c>、<c>"*"</c>（任意）。</summary>
    public string VersionRange { get; set; } = "*";
}

/// <summary>
/// 一条顶层动作类型认领：<see cref="TypeName"/> 这个类型名由本插件的
/// <see cref="ContributionId"/> 这个贡献点负责。
/// </summary>
public sealed class PluginTypeClaim
{
    /// <summary>用户配置里 <c>ActionItem.Type</c> 的取值，如 <c>Command</c>。区分大小写无关。</summary>
    public string TypeName { get; set; } = "";

    /// <summary>插件内贡献点的短 ID，与 <see cref="ActionDescriptor.Id"/> 一致，如 <c>command</c>。</summary>
    public string ContributionId { get; set; } = "";

    /// <summary>
    /// 解析 <c>"Command=command;Hotkey=hotkey"</c> 形态的认领串。
    /// <para>
    /// 容错但不含糊：空段直接跳过；缺了 <c>=</c>、或某一侧为空、或与已解析项重复的段
    /// 一律<b>整条丢弃</b>并记进 <paramref name="malformed"/>，由调用方决定怎么告警 ——
    /// 悄悄接受一个半残的认领，结果是「这个动作有时能用有时不能」，最难排查。
    /// </para>
    /// </summary>
    public static List<PluginTypeClaim> ParseAll(string? raw, out List<string> malformed)
    {
        var claims = new List<PluginTypeClaim>();
        malformed = new List<string>();

        if (string.IsNullOrWhiteSpace(raw)) return claims;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string segment in raw!.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string piece = segment.Trim();
            if (piece.Length == 0) continue;

            int separator = piece.IndexOf('=');
            if (separator <= 0 || separator == piece.Length - 1)
            {
                malformed.Add(piece);
                continue;
            }

            string typeName = piece.Substring(0, separator).Trim();
            string contributionId = piece.Substring(separator + 1).Trim();

            if (typeName.Length == 0 || contributionId.Length == 0)
            {
                malformed.Add(piece);
                continue;
            }

            // 同一个类型被同一份清单认领两次：保留第一条，第二条丢弃。
            // 这不是「后者覆盖前者」能解决的 —— 两条指的可能压根不是同一个贡献点，
            // 静默取一条等于替用户做了个他看不见的选择。
            if (!seen.Add(typeName))
            {
                malformed.Add(piece);
                continue;
            }

            claims.Add(new PluginTypeClaim { TypeName = typeName, ContributionId = contributionId });
        }

        return claims;
    }

    /// <summary>序列化回 <c>"类型=短ID"</c> 形态，供登记表持久化。</summary>
    public string ToWire() => $"{TypeName}={ContributionId}";
}
