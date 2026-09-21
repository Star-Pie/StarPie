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
