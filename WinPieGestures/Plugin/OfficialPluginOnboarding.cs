using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WinPieGestures.Plugins;

/// <summary>
/// 官方核心插件元数据（用于在发起任何网络请求前在 UI 中知情公示）。
/// </summary>
internal sealed class OfficialPluginTargetInfo
{
    public string Id { get; init; } = "";
    public string NameKey { get; init; } = "";
    public string DescriptionKey { get; init; } = "";
    public string Icon { get; init; } = "🔌";
    public IReadOnlyList<string> DisclosedCapabilities { get; init; } = Array.Empty<string>();

    public string Name => I18n.T(NameKey);
    public string Description => I18n.T(DescriptionKey);
}

/// <summary>
/// 官方核心插件批量安装进度。
/// </summary>
internal sealed class OfficialPluginBatchProgress
{
    public int TotalCount { get; init; }
    public int CompletedCount { get; init; }
    public string CurrentPluginId { get; init; } = "";
    public string Message { get; init; } = "";
}

/// <summary>
/// 官方核心插件在批量安装过程中的状态分类。
/// </summary>
internal enum OfficialPluginStatusKind
{
    /// <summary>原本安装但被用户停用（保持原状，绝不擅自启用）</summary>
    OriginallyInstalledDisabled,

    /// <summary>原本已安装且处于启用状态</summary>
    AlreadyInstalledAndEnabled,

    /// <summary>本次新安装且已成功启用</summary>
    NewlyInstalledAndEnabled,

    /// <summary>本次安装成功但启用失败（如依赖缺失、加载异常）</summary>
    InstalledButEnableFailed,

    /// <summary>安装失败（网络、校验、哈希或用户拒绝权限授权）</summary>
    InstallFailed
}

/// <summary>
/// 单个官方核心插件的处理结果明细。
/// </summary>
internal sealed class OfficialPluginItemResult
{
    public string PluginId { get; init; } = "";
    public string PluginName { get; init; } = "";
    public OfficialPluginStatusKind Status { get; set; }
    public string? ErrorOrGuidance { get; set; }
}

/// <summary>
/// 官方核心插件批量安装报告。
/// </summary>
internal sealed class OfficialPluginBatchInstallReport
{
    /// <summary>
    /// 是否 5 项官方核心插件均处于活跃且启用状态（无失败、无启用失败、无原本停用）。
    /// </summary>
    public bool AllSuccessfullyActive =>
        FailedPlugins.Count == 0 &&
        InstalledButEnableFailedPlugins.Count == 0 &&
        OriginallyInstalledDisabledPluginIds.Count == 0 &&
        (SucceededPluginIds.Count + AlreadyInstalledAndEnabledPluginIds.Count) == OfficialPluginOnboarding.TargetPluginIds.Length;

    public bool AllSucceeded => FailedPlugins.Count == 0 && InstalledButEnableFailedPlugins.Count == 0;
    public int SuccessCount => SucceededPluginIds.Count;
    public int SkippedCount => SkippedPluginIds.Count;
    public int FailureCount => FailedPlugins.Count + InstalledButEnableFailedPlugins.Count;

    public List<string> SucceededPluginIds { get; init; } = new();
    public List<string> SkippedPluginIds { get; init; } = new();
    public List<string> OriginallyInstalledDisabledPluginIds { get; init; } = new();
    public List<string> AlreadyInstalledAndEnabledPluginIds { get; init; } = new();
    public Dictionary<string, string> InstalledButEnableFailedPlugins { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> FailedPlugins { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, OfficialPluginItemResult> ItemResults { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// 官方核心插件一键引导安装核心服务。
/// <para>
/// 负责一键引导触发判定、缺失模块过滤、知情公示元数据、超限权限拦截及批量安装。
/// 严格遵守知情同意原则：用户同意前不向外部发起任何网络请求。
/// </para>
/// </summary>
internal static class OfficialPluginOnboarding
{
    public const string OfficialRepoUrl = "https://github.com/Star-Pie/StarPie-Official-Plugins";

    public static readonly string[] TargetPluginIds =
    {
        "starpie.builtin.folder",
        "starpie.builtin.weburl",
        "starpie.builtin.launch",
        "starpie.builtin.system",
        "starpie.builtin.shelltool"
    };

    public static readonly IReadOnlyList<OfficialPluginTargetInfo> TargetPlugins = new List<OfficialPluginTargetInfo>
    {
        new()
        {
            Id = "starpie.builtin.folder",
            NameKey = "OfficialPluginNameFolder",
            DescriptionKey = "OfficialPluginDescFolder",
            Icon = "📁",
            DisclosedCapabilities = new[] { "Process" }
        },
        new()
        {
            Id = "starpie.builtin.weburl",
            NameKey = "OfficialPluginNameWebUrl",
            DescriptionKey = "OfficialPluginDescWebUrl",
            Icon = "🌐",
            DisclosedCapabilities = new[] { "Process" }
        },
        new()
        {
            Id = "starpie.builtin.launch",
            NameKey = "OfficialPluginNameLaunch",
            DescriptionKey = "OfficialPluginDescLaunch",
            Icon = "🚀",
            DisclosedCapabilities = new[] { "Process" }
        },
        new()
        {
            Id = "starpie.builtin.system",
            NameKey = "OfficialPluginNameSystem",
            DescriptionKey = "OfficialPluginDescSystem",
            Icon = "⚙️",
            DisclosedCapabilities = new[] { "Process", "InputSimulation" }
        },
        new()
        {
            Id = "starpie.builtin.shelltool",
            NameKey = "OfficialPluginNameShellTool",
            DescriptionKey = "OfficialPluginDescShellTool",
            Icon = "💻",
            DisclosedCapabilities = new[] { "Process" }
        }
    };

    private static int _isInstalling;

    public static OfficialPluginTargetInfo? GetTargetInfo(string pluginId)
    {
        return TargetPlugins.FirstOrDefault(t => string.Equals(t.Id, pluginId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 判断是否应该弹出一次性引导安装弹窗。
    /// 确保首次引导检查发生在 PluginHost.Initialize 完成、PluginPaths.Configure 生效之后。
    /// </summary>
    public static bool ShouldPrompt(out List<string> missingPluginIds)
    {
        missingPluginIds = new List<string>();

        // 1. 无界面或自检模式下严禁弹窗
        if (PluginHost.HeadlessMode)
        {
            return false;
        }

        // 确保首次引导检查发生在 PluginHost.Initialize 完成、PluginPaths.Configure 生效之后
        if (!PluginHost.IsInitialized)
        {
            PluginHost.Initialize();
        }

        // 2. 插件系统总开关关闭或处于安全模式时严禁弹窗及擅自开启
        if (!PluginHost.IsEnabled || ConfigManager.CurrentConfig?.Plugins?.EnablePluginSystem != true)
        {
            return false;
        }

        if (PluginHost.IsSafeModeActive)
        {
            return false;
        }

        // 3. 用户此前已经提示过（不论同意还是暂不安装），不再重复弹窗
        if (ConfigManager.CurrentConfig.Plugins.HasPromptedOfficialPluginsOnboarding)
        {
            return false;
        }

        // 4. 检查 5 项官方插件安装状态
        missingPluginIds = GetMissingPluginIds();
        if (missingPluginIds.Count == 0)
        {
            // 五项已齐全，直接持久化状态，以后不再提示
            MarkPrompted();
            return false;
        }

        return true;
    }

    /// <summary>
    /// 获取当前尚未安装的官方核心插件 ID 列表。
    /// </summary>
    public static List<string> GetMissingPluginIds()
    {
        if (!PluginHost.IsInitialized)
        {
            PluginHost.Initialize();
        }

        var missing = new List<string>();
        foreach (string id in TargetPluginIds)
        {
            if (!IsPluginInstalled(id))
            {
                missing.Add(id);
            }
        }
        return missing;
    }

    /// <summary>
    /// 获取当前已安装但处于停用状态的官方核心插件 ID 列表。
    /// 用于在引导开始前记录用户原有的停用状态，确保重试或批量操作中严格保留用户的停用设置，绝不擅自启用。
    /// </summary>
    public static List<string> GetOriginallyDisabledPluginIds()
    {
        if (!PluginHost.IsInitialized)
        {
            PluginHost.Initialize();
        }

        var disabled = new List<string>();
        foreach (string id in TargetPluginIds)
        {
            if (IsPluginInstalled(id))
            {
                bool isEnabled = false;
                PluginInstance? inst = PluginHost.Find(id);
                if (inst != null)
                {
                    isEnabled = inst.Entry.Enabled;
                }
                else
                {
                    PluginRegistryEntry? entry = PluginRegistryStore.FindEntry(id);
                    isEnabled = entry?.Enabled ?? false;
                }

                if (!isEnabled)
                {
                    disabled.Add(id);
                }
            }
        }
        return disabled;
    }

    /// <summary>
    /// 获取当前未完成的官方核心插件 ID 列表（包括尚未安装的项，以及在当前引导会话中安装成功但启用失败的项）。
    /// 严格排除用户在引导前主动停用的插件。
    /// </summary>
    public static List<string> GetIncompletePluginIds(IReadOnlyCollection<string>? preExistingDisabledPluginIds = null)
    {
        if (!PluginHost.IsInitialized)
        {
            PluginHost.Initialize();
        }

        preExistingDisabledPluginIds ??= GetOriginallyDisabledPluginIds();
        var preExistingSet = new HashSet<string>(preExistingDisabledPluginIds, StringComparer.OrdinalIgnoreCase);

        var incomplete = new List<string>();
        foreach (string id in TargetPluginIds)
        {
            // 用户在引导前主动停用的插件：严格保留，不得列入未完成待安装/待重试项
            if (preExistingSet.Contains(id))
            {
                continue;
            }

            if (!IsPluginInstalled(id))
            {
                incomplete.Add(id);
            }
            else
            {
                bool isEnabled = false;
                PluginInstance? inst = PluginHost.Find(id);
                if (inst != null)
                {
                    isEnabled = inst.Entry.Enabled;
                }
                else
                {
                    PluginRegistryEntry? entry = PluginRegistryStore.FindEntry(id);
                    isEnabled = entry?.Enabled ?? false;
                }

                if (!isEnabled)
                {
                    // 已安装但未启用（且并非用户引导前主动停用）：属于本次安装成功但启用失败项，需要重试
                    incomplete.Add(id);
                }
            }
        }
        return incomplete;
    }

    /// <summary>
    /// 检查指定插件是否已经安装（不论是已启用、已禁用、还是不同版本）。
    /// </summary>
    public static bool IsPluginInstalled(string pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId)) return false;
        if (!PluginHost.IsInitialized)
        {
            PluginHost.Initialize();
        }
        return PluginHost.Find(pluginId) != null || PluginRegistryStore.FindEntry(pluginId) != null;
    }

    /// <summary>
    /// 标记用户已完成引导提示，后续升级不再重复弹出。
    /// </summary>
    public static void MarkPrompted()
    {
        try
        {
            if (ConfigManager.CurrentConfig?.Plugins != null)
            {
                ConfigManager.CurrentConfig.Plugins.HasPromptedOfficialPluginsOnboarding = true;
                ConfigManager.SaveConfig();
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogWarn($"[plugin] 保存引导提示状态失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 本地插件启用委托（用于解耦网络下载与本地重试，并支持测试隔离）。
    /// </summary>
    public delegate bool LocalPluginEnabler(string pluginId, out string error);

    /// <summary>
    /// 执行官方核心插件的批量下载与安装 / 重试。
    /// 仅安装缺失项与未完成项；已安装项跳过（不覆盖、不升级、不改动禁用状态，绝不擅自启用用户原本停用的插件）。
    /// 将“已安装但本次启用失败”的重试与“缺失插件下载安装”分开：
    /// 本地重试不得依赖网络，启用再次失败时报告原因，不得下载覆盖或升级已有插件。
    /// 按每个插件分别比对预告权限、catalog 权限和下载后 plugin.json 的实际权限；超出已告知范围必须再次确认，拒绝则不安装该项。
    /// </summary>
    public static async Task<OfficialPluginBatchInstallReport> InstallMissingPluginsAsync(
        Func<CancellationToken, Task<OfficialPluginCatalog>>? catalogFetcher = null,
        Func<OfficialPluginModule, CancellationToken, Task<OfficialPluginInstallResult>>? moduleInstaller = null,
        Func<string, List<string>, Task<bool>>? extraCapabilityPrompter = null,
        IProgress<OfficialPluginBatchProgress>? progress = null,
        CancellationToken cancellationToken = default,
        Func<OfficialPluginModule, Func<string, List<string>, Task<bool>>?, IReadOnlyCollection<string>?, CancellationToken, Task<OfficialPluginInstallResult>>? detailedModuleInstaller = null,
        IReadOnlyCollection<string>? preExistingDisabledPluginIds = null,
        LocalPluginEnabler? localEnabler = null)
    {
        // 重入防护
        if (Interlocked.CompareExchange(ref _isInstalling, 1, 0) != 0)
        {
            throw new InvalidOperationException("已有官方插件批量安装正在进行中，请勿重复操作。");
        }

        var report = new OfficialPluginBatchInstallReport();

        try
        {
            if (!PluginHost.IsInitialized)
            {
                PluginHost.Initialize();
            }

            preExistingDisabledPluginIds ??= GetOriginallyDisabledPluginIds();
            var preExistingDisabledSet = new HashSet<string>(preExistingDisabledPluginIds, StringComparer.OrdinalIgnoreCase);

            var localRetryIds = new List<string>();
            var missingInstallIds = new List<string>();

            // 区分无需处理的插件状态：原本已启用 vs 原本安装但被用户停用（绝不擅自启用）
            // 以及需要处理的项：本地已安装但启用失败项（阶段 A）vs 尚未安装项（阶段 B）
            foreach (string id in TargetPluginIds)
            {
                var targetInfo = GetTargetInfo(id);
                string name = targetInfo != null ? I18n.T(targetInfo.NameKey) : id;

                if (preExistingDisabledSet.Contains(id))
                {
                    report.SkippedPluginIds.Add(id);
                    report.OriginallyInstalledDisabledPluginIds.Add(id);
                    report.ItemResults[id] = new OfficialPluginItemResult
                    {
                        PluginId = id,
                        PluginName = name,
                        Status = OfficialPluginStatusKind.OriginallyInstalledDisabled,
                        ErrorOrGuidance = I18n.TF("OfficialPluginsOnboardingOriginallyDisabledNotice", name)
                    };
                }
                else if (IsPluginInstalled(id))
                {
                    bool isEnabled = false;
                    PluginInstance? inst = PluginHost.Find(id);
                    if (inst != null)
                    {
                        isEnabled = inst.Entry.Enabled;
                    }
                    else
                    {
                        PluginRegistryEntry? entry = PluginRegistryStore.FindEntry(id);
                        isEnabled = entry?.Enabled ?? false;
                    }

                    if (isEnabled)
                    {
                        report.SkippedPluginIds.Add(id);
                        report.AlreadyInstalledAndEnabledPluginIds.Add(id);
                        report.ItemResults[id] = new OfficialPluginItemResult
                        {
                            PluginId = id,
                            PluginName = name,
                            Status = OfficialPluginStatusKind.AlreadyInstalledAndEnabled,
                            ErrorOrGuidance = null
                        };
                    }
                    else
                    {
                        // 已安装但未启用（且并非用户引导前主动停用）：属于本地安装成功但启用失败项
                        localRetryIds.Add(id);
                    }
                }
                else
                {
                    // 尚未在本地安装：属于缺失项
                    missingInstallIds.Add(id);
                }
            }

            int totalToProcess = localRetryIds.Count + missingInstallIds.Count;
            if (totalToProcess == 0)
            {
                progress?.Report(new OfficialPluginBatchProgress
                {
                    TotalCount = 0,
                    CompletedCount = 0,
                    Message = report.OriginallyInstalledDisabledPluginIds.Count > 0
                        ? I18n.TF("OfficialPluginsOnboardingSummaryWithDisabled", report.AlreadyInstalledAndEnabledPluginIds.Count, report.OriginallyInstalledDisabledPluginIds.Count)
                        : I18n.T("OfficialPluginsOnboardingAllSucceeded")
                });
                return report;
            }

            int completed = 0;

            // =========================================================================
            // 阶段 A：纯本地启用重试 (Local Retry)
            // 严格要求：不得依赖网络，不请求 catalog，不下载覆盖或升级已有插件；
            // 启用再次失败时必须记录并报告真实原因。
            // =========================================================================
            LocalPluginEnabler enabler = localEnabler ?? ((string pid, out string err) => PluginHost.Enable(pid, out err));

            foreach (string id in localRetryIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var targetInfo = GetTargetInfo(id);
                string pluginName = targetInfo != null ? I18n.T(targetInfo.NameKey) : id;

                progress?.Report(new OfficialPluginBatchProgress
                {
                    TotalCount = totalToProcess,
                    CompletedCount = completed,
                    CurrentPluginId = id,
                    Message = I18n.TF("OfficialPluginsOnboardingStatusInstallingItem", completed + 1, totalToProcess, pluginName)
                });

                bool enabled = enabler(id, out string enableError);
                if (enabled)
                {
                    report.SucceededPluginIds.Add(id);
                    report.ItemResults[id] = new OfficialPluginItemResult
                    {
                        PluginId = id,
                        PluginName = pluginName,
                        Status = OfficialPluginStatusKind.NewlyInstalledAndEnabled,
                        ErrorOrGuidance = null
                    };
                }
                else
                {
                    string reason = string.IsNullOrWhiteSpace(enableError) ? "未知原因" : enableError;
                    string guidance = I18n.TF("OfficialPluginsOnboardingEnableFailedGuidanceWithReason", pluginName, reason);
                    report.InstalledButEnableFailedPlugins[id] = guidance;
                    report.ItemResults[id] = new OfficialPluginItemResult
                    {
                        PluginId = id,
                        PluginName = pluginName,
                        Status = OfficialPluginStatusKind.InstalledButEnableFailed,
                        ErrorOrGuidance = guidance
                    };
                }

                completed++;
                progress?.Report(new OfficialPluginBatchProgress
                {
                    TotalCount = totalToProcess,
                    CompletedCount = completed,
                    CurrentPluginId = id,
                    Message = enabled
                        ? $"[{pluginName}] 启用成功"
                        : $"[{pluginName}] 启用失败：{report.InstalledButEnableFailedPlugins[id]}"
                });
            }

            // =========================================================================
            // 阶段 B：缺失插件下载与安装 (Missing Install)
            // 仅在存在真正缺失未安装的项时才发起网络请求获取 catalog。
            // =========================================================================
            if (missingInstallIds.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                progress?.Report(new OfficialPluginBatchProgress
                {
                    TotalCount = totalToProcess,
                    CompletedCount = completed,
                    Message = I18n.T("OfficialPluginsOnboardingStatusFetchingCatalog")
                });

                catalogFetcher ??= OfficialPluginClient.FetchCatalogAsync;
                OfficialPluginCatalog catalog = await catalogFetcher(cancellationToken).ConfigureAwait(false);

                if (catalog?.Modules == null)
                {
                    throw new InvalidOperationException("获取到的官方插件 catalog 为空或解析失败。");
                }

                var moduleMap = catalog.Modules.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);

                foreach (string id in missingInstallIds)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var targetInfo = GetTargetInfo(id);
                    string pluginName = targetInfo != null ? I18n.T(targetInfo.NameKey) : id;

                    if (!moduleMap.TryGetValue(id, out OfficialPluginModule? module) || module == null)
                    {
                        string err = $"官方 catalog 中未找到插件 {id}。";
                        report.FailedPlugins[id] = err;
                        report.ItemResults[id] = new OfficialPluginItemResult
                        {
                            PluginId = id,
                            PluginName = pluginName,
                            Status = OfficialPluginStatusKind.InstallFailed,
                            ErrorOrGuidance = err
                        };
                        completed++;
                        progress?.Report(new OfficialPluginBatchProgress
                        {
                            TotalCount = totalToProcess,
                            CompletedCount = completed,
                            CurrentPluginId = id,
                            Message = err
                        });
                        continue;
                    }

                    // 阶段 1：比对预告权限与 catalog 权限
                    var informedCapabilities = new HashSet<string>(
                        targetInfo?.DisclosedCapabilities ?? Array.Empty<string>(),
                        StringComparer.OrdinalIgnoreCase);

                    var catalogExtra = (module.Capabilities ?? new List<string>())
                        .Where(cap => !informedCapabilities.Contains(cap))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (catalogExtra.Count > 0)
                    {
                        bool allowed = false;
                        if (extraCapabilityPrompter != null)
                        {
                            allowed = await extraCapabilityPrompter(module.Name, catalogExtra).ConfigureAwait(false);
                        }

                        if (!allowed)
                        {
                            string err = I18n.TF("OfficialPluginsOnboardingPermissionRejectedCatalog", string.Join(", ", catalogExtra));
                            report.FailedPlugins[id] = err;
                            report.ItemResults[id] = new OfficialPluginItemResult
                            {
                                PluginId = id,
                                PluginName = module.Name,
                                Status = OfficialPluginStatusKind.InstallFailed,
                                ErrorOrGuidance = err
                            };
                            completed++;
                            progress?.Report(new OfficialPluginBatchProgress
                            {
                                TotalCount = totalToProcess,
                                CompletedCount = completed,
                                CurrentPluginId = id,
                                Message = err
                            });
                            continue;
                        }

                        // 用户同意了 catalog 额外权限，纳入该插件的已告知范围
                        foreach (string c in catalogExtra) informedCapabilities.Add(c);
                    }

                    progress?.Report(new OfficialPluginBatchProgress
                    {
                        TotalCount = totalToProcess,
                        CompletedCount = completed,
                        CurrentPluginId = id,
                        Message = I18n.TF("OfficialPluginsOnboardingStatusInstallingItem", completed + 1, totalToProcess, module.Name)
                    });

                    // 阶段 2：执行下载与安装（比对实际 plugin.json 权限）
                    try
                    {
                        OfficialPluginInstallResult installResult;
                        if (detailedModuleInstaller != null)
                        {
                            installResult = await detailedModuleInstaller(module, extraCapabilityPrompter, informedCapabilities, cancellationToken).ConfigureAwait(false);
                        }
                        else if (moduleInstaller != null)
                        {
                            installResult = await moduleInstaller(module, cancellationToken).ConfigureAwait(false);
                        }
                        else
                        {
                            installResult = await OfficialPluginClient.InstallAsync(
                                module,
                                extraCapabilityPrompter,
                                informedCapabilities,
                                cancellationToken,
                                forceEnable: true).ConfigureAwait(false);
                        }

                        if (installResult.Success)
                        {
                            if (installResult.Enabled)
                            {
                                report.SucceededPluginIds.Add(id);
                                report.ItemResults[id] = new OfficialPluginItemResult
                                {
                                    PluginId = id,
                                    PluginName = module.Name,
                                    Status = OfficialPluginStatusKind.NewlyInstalledAndEnabled,
                                    ErrorOrGuidance = null
                                };
                            }
                            else
                            {
                                // 安装成功但启用失败：不得谎报全部启用，给出可操作的重试与启用指引
                                string guidance = !string.IsNullOrWhiteSpace(installResult.Error)
                                    ? I18n.TF("OfficialPluginsOnboardingEnableFailedGuidanceWithReason", module.Name, installResult.Error)
                                    : I18n.TF("OfficialPluginsOnboardingEnableFailedGuidance", module.Name);
                                report.InstalledButEnableFailedPlugins[id] = guidance;
                                report.ItemResults[id] = new OfficialPluginItemResult
                                {
                                    PluginId = id,
                                    PluginName = module.Name,
                                    Status = OfficialPluginStatusKind.InstalledButEnableFailed,
                                    ErrorOrGuidance = guidance
                                };
                            }
                        }
                        else
                        {
                            string guidance = string.IsNullOrWhiteSpace(installResult.Error)
                                ? I18n.TF("OfficialPluginsOnboardingInstallFailedGuidance", module.Name, "未知错误")
                                : installResult.Error;
                            report.FailedPlugins[id] = guidance;
                            report.ItemResults[id] = new OfficialPluginItemResult
                            {
                                PluginId = id,
                                PluginName = module.Name,
                                Status = OfficialPluginStatusKind.InstallFailed,
                                ErrorOrGuidance = guidance
                            };
                        }
                    }
                    catch (Exception ex)
                    {
                        string guidance = I18n.TF("OfficialPluginsOnboardingInstallFailedGuidance", module.Name, ex.Message);
                        report.FailedPlugins[id] = guidance;
                        report.ItemResults[id] = new OfficialPluginItemResult
                        {
                            PluginId = id,
                            PluginName = module.Name,
                            Status = OfficialPluginStatusKind.InstallFailed,
                            ErrorOrGuidance = guidance
                        };
                    }

                    completed++;
                    progress?.Report(new OfficialPluginBatchProgress
                    {
                        TotalCount = totalToProcess,
                        CompletedCount = completed,
                        CurrentPluginId = id,
                        Message = report.ItemResults.TryGetValue(id, out var itemRes) && itemRes.Status == OfficialPluginStatusKind.NewlyInstalledAndEnabled
                            ? $"[{module.Name}] 安装并启用成功"
                            : $"[{module.Name}] {report.ItemResults[id].ErrorOrGuidance}"
                    });
                }
            }

            // 3. 安装或重试完成后，如果有成功项，刷新官方类型认领与可用性事件
            if (report.SucceededPluginIds.Count > 0)
            {
                PluginActionClaimRegistry.Rebuild(PluginRegistryStore.SnapshotEntries());
                PluginHost.NotifyPluginSetChanged();
            }

            return report;
        }
        finally
        {
            Interlocked.Exchange(ref _isInstalling, 0);
        }
    }
}
