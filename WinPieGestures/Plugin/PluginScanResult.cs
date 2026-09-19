using System;

namespace WinPieGestures.Plugins;

/// <summary>
/// 识别失败原因码。设计原则：<b>每一个失败都必须能翻译成一句用户看得懂、并且知道该做什么的话</b>，
/// 绝不允许只抛一句「加载失败」——社区插件出错时用户无从下手，是最容易劝退的体验。
/// </summary>
internal enum PluginScanFailure
{
    None = 0,

    // ---- 清单层（G-1）----
    IdNotDeclared,
    ManifestInvalid,
    InvalidIdFormat,
    ReservedIdPrefix,

    // ---- 程序集结构层（G-2）----
    DllNotFound,
    NotDotNetAssembly,
    NotIlOnly,
    WrongArchitecture,

    // ---- 契约层（G-3）----
    TargetFrameworkMismatch,
    NoContractImplementation,
    AmbiguousContractImplementation,
    EntryTypeNotFound,
    ApiVersionMismatch,
    ContractAssemblyVersionMismatch,

    // ---- 完整性层（G-4）----
    Sha256Mismatch,

    // ---- 兼容性层 ----
    HostVersionOutOfRange,

    // ---- 依赖层 ----
    DependencyMissing,
    DependencyCycle,
}

/// <summary>
/// 原因码 → 用户可读文案与修复建议。
/// <para>
/// 两个方法都用<b>穷尽 switch 表达式</b>（没有 <c>_</c> 兜底），并刻意屏蔽了 <b>CS8524</b>：
/// </para>
/// <list type="bullet">
/// <item>保留穷尽的收益：往 <see cref="PluginScanFailure"/> 里加一项而忘了加分支，编译器报
/// <b>CS8509</b>（实测：漏一个具名成员时 CS8509 与 CS8524 会同时出现，屏蔽 CS8524 不影响 8509）。</item>
/// <item>屏蔽 CS8524 的原因：它抱怨的是**未命名**枚举值（<c>(PluginScanFailure)19</c> 这类强制转换产物）。
/// 本枚举只在宿主内部产生，没有任何路径由外部数据反序列化或强制转换而来，那种值不存在。
/// 不屏蔽的话，「穷尽」这个特性根本用不了 —— 编译器会对每个穷尽的 switch 都报一次。</item>
/// </list>
/// </summary>
internal static class PluginScanFailureText
{
#pragma warning disable CS8524 // 未命名枚举值不可达，理由见类型注释
    /// <summary>
    /// 原因码 → 一句用户看得懂的短标题。
    /// <para>
    /// <b>刻意不写 <c>_</c> 兜底分支</b>：这样往 <see cref="PluginScanFailure"/> 里加一项、却忘了加词条时，
    /// 编译器会直接报 CS8509（switch 表达式未穷尽），而不是静默退回一句「未知原因」——
    /// 「新增了一种失败原因，却没告诉用户这是什么」正是这个类要防的那件事。
    /// </para>
    /// <para>
    /// 枚举值只在宿主内部产生（没有任何一条路径由外部数据反序列化而来），
    /// 所以不存在「强制转换出未声明的值」因而抛 <c>SwitchExpressionException</c> 的可能。
    /// </para>
    /// </summary>
    public static string Title(PluginScanFailure code) => code switch
    {
        PluginScanFailure.None => I18n.T("PluginScanFailureTitleNone"),

        PluginScanFailure.IdNotDeclared => I18n.T("PluginScanFailureTitleIdNotDeclared"),
        PluginScanFailure.ManifestInvalid => I18n.T("PluginScanFailureTitleManifestInvalid"),
        PluginScanFailure.InvalidIdFormat => I18n.T("PluginScanFailureTitleInvalidIdFormat"),
        PluginScanFailure.ReservedIdPrefix => I18n.T("PluginScanFailureTitleReservedIdPrefix"),

        PluginScanFailure.DllNotFound => I18n.T("PluginScanFailureTitleDllNotFound"),
        PluginScanFailure.NotDotNetAssembly => I18n.T("PluginScanFailureTitleNotDotNetAssembly"),
        PluginScanFailure.NotIlOnly => I18n.T("PluginScanFailureTitleNotIlOnly"),
        PluginScanFailure.WrongArchitecture => I18n.T("PluginScanFailureTitleWrongArchitecture"),

        PluginScanFailure.TargetFrameworkMismatch => I18n.T("PluginScanFailureTitleTargetFrameworkMismatch"),
        PluginScanFailure.NoContractImplementation => I18n.T("PluginScanFailureTitleNoContractImplementation"),
        PluginScanFailure.AmbiguousContractImplementation => I18n.T("PluginScanFailureTitleAmbiguousContractImplementation"),
        PluginScanFailure.EntryTypeNotFound => I18n.T("PluginScanFailureTitleEntryTypeNotFound"),
        PluginScanFailure.ApiVersionMismatch => I18n.T("PluginScanFailureTitleApiVersionMismatch"),
        PluginScanFailure.ContractAssemblyVersionMismatch => I18n.T("PluginScanFailureTitleContractAssemblyVersionMismatch"),

        PluginScanFailure.Sha256Mismatch => I18n.T("PluginScanFailureTitleSha256Mismatch"),
        PluginScanFailure.HostVersionOutOfRange => I18n.T("PluginScanFailureTitleHostVersionOutOfRange"),

        PluginScanFailure.DependencyMissing => I18n.T("PluginScanFailureTitleDependencyMissing"),
        PluginScanFailure.DependencyCycle => I18n.T("PluginScanFailureTitleDependencyCycle"),
    };

    /// <summary>
    /// 原因码 → 一段「接下来该怎么办」的建议。同样刻意不写 <c>_</c> 兜底（见 <see cref="Title"/>）。
    /// </summary>
    public static string Hint(PluginScanFailure code) => code switch
    {
        PluginScanFailure.None => I18n.T("PluginScanFailureHintNone"),

        PluginScanFailure.IdNotDeclared => I18n.T("PluginScanFailureHintIdNotDeclared"),
        PluginScanFailure.ManifestInvalid => I18n.T("PluginScanFailureHintManifestInvalid"),
        PluginScanFailure.InvalidIdFormat => I18n.T("PluginScanFailureHintInvalidIdFormat"),
        PluginScanFailure.ReservedIdPrefix => I18n.T("PluginScanFailureHintReservedIdPrefix"),

        PluginScanFailure.DllNotFound => I18n.T("PluginScanFailureHintDllNotFound"),
        PluginScanFailure.NotDotNetAssembly => I18n.T("PluginScanFailureHintNotDotNetAssembly"),
        PluginScanFailure.NotIlOnly => I18n.T("PluginScanFailureHintNotIlOnly"),
        PluginScanFailure.WrongArchitecture => I18n.T("PluginScanFailureHintWrongArchitecture"),

        PluginScanFailure.TargetFrameworkMismatch => I18n.T("PluginScanFailureHintTargetFrameworkMismatch"),
        PluginScanFailure.NoContractImplementation => I18n.T("PluginScanFailureHintNoContractImplementation"),
        PluginScanFailure.AmbiguousContractImplementation => I18n.T("PluginScanFailureHintAmbiguousContractImplementation"),
        PluginScanFailure.EntryTypeNotFound => I18n.T("PluginScanFailureHintEntryTypeNotFound"),
        PluginScanFailure.ApiVersionMismatch => I18n.T("PluginScanFailureHintApiVersionMismatch"),
        PluginScanFailure.ContractAssemblyVersionMismatch => I18n.T("PluginScanFailureHintContractAssemblyVersionMismatch"),

        PluginScanFailure.Sha256Mismatch => I18n.T("PluginScanFailureHintSha256Mismatch"),
        PluginScanFailure.HostVersionOutOfRange => I18n.T("PluginScanFailureHintHostVersionOutOfRange"),

        PluginScanFailure.DependencyMissing => I18n.T("PluginScanFailureHintDependencyMissing"),
        PluginScanFailure.DependencyCycle => I18n.T("PluginScanFailureHintDependencyCycle"),
    };
#pragma warning restore CS8524
}

/// <summary>静态识别的完整结果。这是「安装确认卡」与「插件列表」的数据来源。</summary>
internal sealed class PluginScanResult
{
    public bool Accepted { get; set; }
    public PluginScanFailure Failure { get; set; } = PluginScanFailure.None;
    public string ErrorDetail { get; set; } = "";

    /// <summary>用户手动选择的那个 .dll 的完整路径。</summary>
    public string DllPath { get; set; } = "";

    /// <summary>清单来源目录（用于安装时整目录复制）。</summary>
    public string SourceDirectory { get; set; } = "";

    /// <summary>解析出的清单。可为 null（未通过 G-1 时）。</summary>
    public StarPie.Plugin.PluginManifest? Manifest { get; set; }

    /// <summary>清单来源：<c>Manifest</c>（plugin.json）或 <c>AssemblyMetadata</c>（裸 DLL 兜底）。</summary>
    public string ManifestSource { get; set; } = "";

    // ---- 展示用派生信息 ----
    public string FileSizeText { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string MachineText { get; set; } = "";
    public string TargetFramework { get; set; } = "";
    public string? EntryTypeFullName { get; set; }
    public bool IsSigned { get; set; }
    public string? SignerSubject { get; set; }
    public string? SignerThumbprint { get; set; }
    public bool HasDependencyFile { get; set; }

    /// <summary>摘要哈希前 12 位，用于界面展示。</summary>
    public string Sha256Short => Sha256.Length >= 12 ? Sha256.Substring(0, 12) : Sha256;

    /// <summary>
    /// 「标题：详情」一行。<b>分隔符也走词条</b>（<c>PluginScanFailureSeparator</c>）：
    /// 中文与日文用全角「：」，英文用半角 ": " —— 写死全角的话，英文界面会出现
    /// 「Not a .NET assembly：CorFlags=0x1…」这种中英标点混排。
    /// </summary>
    public string DescribeFailure() =>
        Failure == PluginScanFailure.None
            ? PluginScanFailureText.Title(PluginScanFailure.None)
            : PluginScanFailureText.Title(Failure)
              + I18n.T("PluginScanFailureSeparator")
              + ErrorDetail;
}
