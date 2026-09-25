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
/// 官方核心插件批量安装报告。
/// </summary>
internal sealed class OfficialPluginBatchInstallReport
{
    public bool AllSucceeded => FailureCount == 0;
    public int SuccessCount => SucceededPluginIds.Count;
    public int SkippedCount => SkippedPluginIds.Count;
    public int FailureCount => FailedPlugins.Count;
    public List<string> SucceededPluginIds { get; init; } = new();
    public List<string> SkippedPluginIds { get; init; } = new();
    public Dictionary<string, string> FailedPlugins { get; init; } = new(StringComparer.OrdinalIgnoreCase);
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

    /// <summary>
    /// 预告公示的基线权限。如果 catalog 中模块要求超出此范围的权限，必须暂停请求用户确认。
    /// </summary>
    public static readonly HashSet<string> AllowedBaselineCapabilities = new(StringComparer.OrdinalIgnoreCase)
    {
        "Process",
        "InputSimulation"
    };

    private static int _isInstalling;

    /// <summary>
    /// 判断是否应该弹出一次性引导安装弹窗。
    /// </summary>
    public static bool ShouldPrompt(out List<string> missingPluginIds)
    {
        missingPluginIds = new List<string>();

        // 1. 无界面或自检模式下严禁弹窗
        if (PluginHost.HeadlessMode)
        {
            return false;
        }

        // 2. 插件系统总开关关闭或处于安全模式时严禁弹窗及擅自开启
        if (!PluginHost.IsEnabled || !ConfigManager.CurrentConfig.Plugins.EnablePluginSystem)
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
    /// 检查指定插件是否已经安装（不论是已启用、已禁用、还是不同版本）。
    /// </summary>
    public static bool IsPluginInstalled(string pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId)) return false;
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
    /// 执行官方核心插件的批量下载与安装。
    /// 仅安装缺失项；已安装项跳过（不覆盖、不升级、不改动禁用状态）。
    /// 支持测试注入 catalogFetcher、moduleInstaller 和 extraCapabilityPrompter。
    /// </summary>
    public static async Task<OfficialPluginBatchInstallReport> InstallMissingPluginsAsync(
        Func<CancellationToken, Task<OfficialPluginCatalog>>? catalogFetcher = null,
        Func<OfficialPluginModule, CancellationToken, Task<OfficialPluginInstallResult>>? moduleInstaller = null,
        Func<string, List<string>, Task<bool>>? extraCapabilityPrompter = null,
        IProgress<OfficialPluginBatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // 重入防护
        if (Interlocked.CompareExchange(ref _isInstalling, 1, 0) != 0)
        {
            throw new InvalidOperationException("已有官方插件批量安装正在进行中，请勿重复操作。");
        }

        var report = new OfficialPluginBatchInstallReport();

        try
        {
            List<string> missingIds = GetMissingPluginIds();
            foreach (string id in TargetPluginIds)
            {
                if (!missingIds.Contains(id, StringComparer.OrdinalIgnoreCase))
                {
                    report.SkippedPluginIds.Add(id);
                }
            }

            if (missingIds.Count == 0)
            {
                progress?.Report(new OfficialPluginBatchProgress
                {
                    TotalCount = 0,
                    CompletedCount = 0,
                    Message = I18n.T("OfficialPluginsOnboardingAllSucceeded")
                });
                return report;
            }

            progress?.Report(new OfficialPluginBatchProgress
            {
                TotalCount = missingIds.Count,
                CompletedCount = 0,
                Message = I18n.T("OfficialPluginsOnboardingStatusFetchingCatalog")
            });

            // 1. 获取 catalog
            catalogFetcher ??= OfficialPluginClient.FetchCatalogAsync;
            OfficialPluginCatalog catalog = await catalogFetcher(cancellationToken).ConfigureAwait(false);

            if (catalog?.Modules == null)
            {
                throw new InvalidOperationException("获取到的官方插件 catalog 为空或解析失败。");
            }

            var moduleMap = catalog.Modules.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);

            // 2. 逐一安装缺失项
            moduleInstaller ??= OfficialPluginClient.InstallAsync;
            int total = missingIds.Count;
            int completed = 0;

            foreach (string id in missingIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!moduleMap.TryGetValue(id, out OfficialPluginModule? module) || module == null)
                {
                    report.FailedPlugins[id] = $"官方 catalog 中未找到插件 {id}。";
                    completed++;
                    progress?.Report(new OfficialPluginBatchProgress
                    {
                        TotalCount = total,
                        CompletedCount = completed,
                        CurrentPluginId = id,
                        Message = report.FailedPlugins[id]
                    });
                    continue;
                }

                // 权限检查：是否超出了预告基线权限 (Process, InputSimulation)
                var extraCapabilities = (module.Capabilities ?? new List<string>())
                    .Where(cap => !AllowedBaselineCapabilities.Contains(cap))
                    .ToList();

                if (extraCapabilities.Count > 0)
                {
                    bool allowed = false;
                    if (extraCapabilityPrompter != null)
                    {
                        allowed = await extraCapabilityPrompter(module.Name, extraCapabilities).ConfigureAwait(false);
                    }

                    if (!allowed)
                    {
                        report.FailedPlugins[id] = $"未授权超出基线的额外权限：{string.Join(", ", extraCapabilities)}。";
                        completed++;
                        progress?.Report(new OfficialPluginBatchProgress
                        {
                            TotalCount = total,
                            CompletedCount = completed,
                            CurrentPluginId = id,
                            Message = report.FailedPlugins[id]
                        });
                        continue;
                    }
                }

                progress?.Report(new OfficialPluginBatchProgress
                {
                    TotalCount = total,
                    CompletedCount = completed,
                    CurrentPluginId = id,
                    Message = I18n.TF("OfficialPluginsOnboardingStatusInstallingItem", completed + 1, total, module.Name)
                });

                try
                {
                    OfficialPluginInstallResult installResult = await moduleInstaller(module, cancellationToken).ConfigureAwait(false);
                    if (installResult.Success)
                    {
                        report.SucceededPluginIds.Add(id);
                    }
                    else
                    {
                        report.FailedPlugins[id] = string.IsNullOrWhiteSpace(installResult.Error) ? "安装失败" : installResult.Error;
                    }
                }
                catch (Exception ex)
                {
                    report.FailedPlugins[id] = ex.Message;
                }

                completed++;
                progress?.Report(new OfficialPluginBatchProgress
                {
                    TotalCount = total,
                    CompletedCount = completed,
                    CurrentPluginId = id,
                    Message = report.FailedPlugins.TryGetValue(id, out string? err)
                        ? $"[{module.Name}] {err}"
                        : $"[{module.Name}] 安装成功"
                });
            }

            // 3. 安装完成后，如果有成功项，刷新官方类型认领与可用性事件
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
