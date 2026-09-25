using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StarPie.Plugin;

namespace WinPieGestures.Plugins;

/// <summary>安装选项。由安装确认卡收集，体现「用户手动选择启用」的产品语义。</summary>
internal sealed class PluginInstallOptions
{
    /// <summary>安装后立即启用。默认 false —— 安装与启用是两个动作。</summary>
    public bool EnableAfterInstall { get; set; }

    /// <summary>目标插件已存在时是否覆盖。</summary>
    public bool OverwriteExisting { get; set; }

    /// <summary>用户是否勾选了「我已了解此插件将以 StarPie 当前权限在进程内运行」。</summary>
    public bool Acknowledged { get; set; }

    /// <summary>用户确认过的能力集合（写入 registry，用于升级时比对是否新增了高风险能力）。</summary>
    public List<string> AcknowledgedCapabilities { get; set; } = new();

    /// <summary>开发者模式：只登记外部路径，不复制文件（便于附加调试器与热重载）。</summary>
    public bool DeveloperExternalPath { get; set; }

    /// <summary>
    /// 写进 <c>registry.json</c> 的安装来源：<c>UserSelectedFile</c>（文件对话框）/
    /// <c>ScanDirectory</c>（只读扫描目录）。
    /// <para>用途只有一个：日后排查「这个插件是怎么进来的」。不做任何逻辑分支。</para>
    /// </summary>
    public string SourceKind { get; set; } = "UserSelectedFile";

    /// <summary>是否来自官方在线模块 catalog；允许使用 starpie.* 命名空间与顶层类型认领。</summary>
    public bool Official { get; set; }
}

internal sealed class PluginInstallResult
{
    public bool Success { get; init; }
    public string PluginId { get; init; } = "";
    public string Error { get; init; } = "";
    public bool Enabled { get; init; }
}

/// <summary>
/// 插件系统门面 —— 主程序与插件世界之间<b>唯一</b>的对外入口。
/// <para>
/// 除 <see cref="PluginHost"/> 之外的宿主模块（Scanner / Loader / Catalog / Invoker / RegistryStore）
/// 全部是 <c>internal</c> 且不对外暴露。这样做的目的是把「主程序需要改动的面」压到最小：
/// <c>ActionExecutor</c> 只需要认识这一个类型的一个方法。
/// </para>
/// </summary>
internal static class PluginHost
{
    public static event Action? PluginAvailabilityChanged;

    /// <summary>贡献点注册表。全局唯一实例。</summary>
    public static readonly PluginCatalog Catalog = new();

    private static readonly object Gate = new();
    private static readonly Dictionary<string, PluginInstance> Instances = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 三条 SPP 调用路径的统一运行时入口。动作路径先适配现有实现，交互与轮盘结构路径先建立空接缝。
    /// </summary>
    private static readonly PluginRuntime Runtime = new(Catalog, Find, static () => _enabled);
    private static readonly ConcurrentDictionary<string, Lazy<Task<PluginStopResult>>> StopOperations =
        new(StringComparer.OrdinalIgnoreCase);

    public static readonly TimeSpan DefaultStopGracePeriod = TimeSpan.FromSeconds(5);

    private static bool _initialized;
    private static bool _enabled = true;
    private static bool _developerMode;
    private static PluginsPreference _preferences = new();

    /// <summary>安全模式：启动时若判定上次是插件导致的崩溃，本次不加载任何插件。</summary>
    private static bool _safeModeActive;

    /// <summary>
    /// 无界面模式（<c>--plugin-selftest</c> / <c>--plugin-paths</c>）：跑完即退，不参与
    /// 启动健康记账。必须在 <see cref="Initialize"/> 之前置位。
    /// </summary>
    public static bool HeadlessMode { get; set; }

    /// <summary>托盘气泡注入点。UI 层设置后插件通知即可显示为气泡。</summary>
    public static Action<string, string>? NotificationSink
    {
        get => PluginNotificationHub.Sink;
        set => PluginNotificationHub.Sink = value;
    }

    public static bool IsInitialized => _initialized;
    public static bool IsEnabled => _enabled;
    public static bool IsSafeModeActive => _safeModeActive;
    public static bool IsDeveloperMode => _developerMode;

    /// <summary>已安装插件数量（不含被忽略的目录）。</summary>
    public static int InstalledCount
    {
        get { lock (Gate) return Instances.Count; }
    }

    // ------------------------------------------------------------------ 生命周期

    /// <summary>
    /// 初始化插件系统。必须在主程序启动早期、且**不阻塞首帧**的前提下调用。
    /// <para>
    /// 这里只做三件廉价的事：解析路径、清理残留、纯静态扫描清单。<b>不加载任何程序集</b>，
    /// 因此零插件用户的启动开销与接入插件系统之前完全一致（守住 R1 内存与启动红线）。
    /// </para>
    /// </summary>
    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            _preferences = ConfigManager.CurrentConfig?.Plugins ?? new PluginsPreference();
            _enabled = _preferences.EnablePluginSystem;
            _developerMode = _preferences.DeveloperMode;

            PluginPaths.Configure(_preferences.PortableMode);

            if (!_enabled)
            {
                AppLogger.LogInfo("[plugin] 插件系统已在设置中关闭，跳过初始化。");
                return;
            }

            if (!PluginPaths.EnsureDirectories())
            {
                AppLogger.LogWarn($"[plugin] 插件目录创建失败，插件系统将不可用：{PluginPaths.Root}");
            }

            PluginLogger.CleanOldPluginLogs();
            CleanupPendingDeletions();

            CheckSafeMode();

            int discovered = SyncFromDisk();
            // registry.json 是启动期的唯一官方类型认领来源。安装/启用路径会主动重建，
            // 但已有插件在启动时也必须先恢复这张路由表，否则 UI 会把全部官方动作误判为未安装。
            PluginActionClaimRegistry.Rebuild(PluginRegistryStore.SnapshotEntries());
            AppLogger.LogInfo(
                $"[plugin] 插件系统就绪：宿主区={PluginPaths.Root}，扫描目录={PluginPaths.ScanRoot}" +
                $"（存在={PluginPaths.ScanRootExists}），已登记 {Instances.Count} 个插件" +
                $"（本次扫描新发现 {discovered} 个），安全模式={_safeModeActive}");

            if (!_safeModeActive && _preferences.PreloadOnStartup)
            {
                SchedulePreload();
            }

            if (!HeadlessMode)
            {
                ScheduleStartupHealthCheck();
            }
        }
        catch (Exception ex)
        {
            // 插件系统初始化失败绝不能影响主程序启动
            AppLogger.LogError("[plugin] 插件系统初始化失败（已降级为「无插件」运行）", ex);
            _enabled = false;
        }
    }

    /// <summary>宿主退出前的收尾。退出路径允许同步等待短宽限期，最终进程退出由操作系统兜底。</summary>
    public static void ShutdownAll()
    {
        if (!_initialized) return;

        foreach (PluginInstance instance in ListInstances())
        {
            if (!instance.IsLoaded) continue;

            try
            {
                _ = DisableAsync(
                        instance.PluginId,
                        PluginStopReason.ApplicationExit,
                        TimeSpan.FromSeconds(2),
                        CancellationToken.None)
                    .GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"[plugin] 退出时停用 {instance.PluginId} 失败", ex);
            }
        }
    }

    // ------------------------------------------------------------------ 安装

    /// <summary>
    /// 第一步：识别用户手动选择的 <c>.dll</c>。只读元数据，不执行任何插件代码。
    /// 返回结果即是「安装确认卡」要展示的全部内容。
    /// </summary>
    public static PluginScanResult PrepareInstall(string dllPath)
    {
        try
        {
            return PluginScanner.ScanSelectedDll(dllPath);
        }
        catch (Exception ex)
        {
            AppLogger.LogError("[plugin] 识别所选文件时发生未预期异常", ex);
            var result = new PluginScanResult { DllPath = dllPath, Accepted = false };
            return result;
        }
    }

    /// <summary>
    /// 第二步：用户确认后落盘。复制到插件目录并登记为 <b>Disabled</b>。
    /// <para>注意：这里<b>不会加载程序集</b> —— 「安装」与「启用」刻意分成两个动作。</para>
    /// </summary>
    public static async Task<PluginInstallResult> CommitInstallAsync(
        PluginScanResult scan,
        PluginInstallOptions options,
        CancellationToken cancellationToken = default)
    {
        options ??= new PluginInstallOptions();
        string? pluginId = scan?.Manifest?.Id;

        if (options.OverwriteExisting && !string.IsNullOrWhiteSpace(pluginId) && Find(pluginId) != null)
        {
            PluginStopResult stop = await DisableAsync(
                pluginId,
                PluginStopReason.Update,
                DefaultStopGracePeriod,
                cancellationToken).ConfigureAwait(false);

            if (!stop.IsFullyStopped)
            {
                return new PluginInstallResult
                {
                    Success = false,
                    PluginId = pluginId,
                    Error = $"旧版本尚未完全停止，不能覆盖安装：{stop.Message}",
                };
            }
        }

        return CommitInstall(scan, options);
    }

    public static PluginInstallResult CommitInstall(PluginScanResult scan, PluginInstallOptions options)
    {
        options ??= new PluginInstallOptions();

        if (scan?.Manifest == null || !scan.Accepted)
        {
            return new PluginInstallResult { Success = false, Error = "识别未通过，无法安装。" };
        }

        if (!options.Acknowledged)
        {
            return new PluginInstallResult { Success = false, Error = "需要先勾选风险确认才能安装。" };
        }

        PluginManifest manifest = scan.Manifest;

        lock (Gate)
        {
            try
            {
                string installPath = manifest.Id;
                string targetDirectory = Path.Combine(PluginPaths.Root, installPath);
                bool alreadyExists = Directory.Exists(targetDirectory) || PluginRegistryStore.FindEntry(manifest.Id) != null;

                if (alreadyExists && !options.OverwriteExisting)
                {
                    return new PluginInstallResult
                    {
                        Success = false,
                        PluginId = manifest.Id,
                        Error = $"已存在同 ID 的插件（{manifest.Id}）。如需替换请勾选「覆盖已有插件」。",
                    };
                }

                // 已被加载的插件不允许直接覆盖文件，否则会得到「文件被占用」这种看不懂的报错
                PluginInstance? existing = Find(manifest.Id);
                if (existing != null && existing.IsLoaded)
                {
                    return new PluginInstallResult
                    {
                        Success = false,
                        PluginId = manifest.Id,
                        Error = "该插件正在运行，请先停用再覆盖安装。",
                    };
                }

                if (!options.DeveloperExternalPath)
                {
                    if (!CopyPayload(scan, targetDirectory, options.OverwriteExisting, out string copyError))
                    {
                        return new PluginInstallResult { Success = false, PluginId = manifest.Id, Error = copyError };
                    }
                }
                else if (!_developerMode)
                {
                    return new PluginInstallResult
                    {
                        Success = false,
                        Error = "「外部路径登记」需要先在插件页开启开发者模式。",
                    };
                }

                var entry = new PluginRegistryEntry
                {
                    Id = manifest.Id,
                    Name = manifest.Name,
                    Version = manifest.Version,
                    Description = manifest.Description,
                    Author = manifest.Author,
                    License = manifest.License,
                    Homepage = manifest.Homepage,
                    InstallPath = installPath,
                    ExternalPath = options.DeveloperExternalPath ? scan.DllPath : null,
                    Enabled = false,
                    Preload = false,
                    EntrySha256 = scan.Sha256,
                    SignerThumbprint = scan.SignerThumbprint,
                    CapabilitiesAck = new List<string>(options.AcknowledgedCapabilities),
                    AckedAt = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"),
                    AckedHostVersion = PluginManifestReader.HostVersion,
                    Source = options.DeveloperExternalPath ? "DeveloperPath" : options.SourceKind,
                    Official = options.Official,
                    ClaimedTypes = options.Official
                        ? BuildClaimWire(manifest)
                        : new List<string>(),
                    InstalledAt = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"),
                };

                PluginRegistryStore.UpsertEntry(entry);

                var instance = new PluginInstance(manifest.Id, entry, scan);
                lock (Gate)
                {
                    Instances[manifest.Id] = instance;
                }

                string source = scan.ManifestSource == "AssemblyMetadata" ? "（程序集元数据）" : "";
                AppLogger.LogInfo(
                    $"[plugin] 已安装 {manifest.Id} v{manifest.Version}{source}，" +
                    $"SHA256={scan.Sha256Short}，签名={scan.IsSigned}，能力={string.Join(",", entry.CapabilitiesAck)}");

                bool enabled = false;
                if (options.EnableAfterInstall)
                {
                    enabled = Enable(manifest.Id, out string enableError);
                    if (!enabled)
                    {
                        AppLogger.LogWarn($"[plugin] 安装后自动启用 {manifest.Id} 失败：{enableError}");
                    }
                }

                return new PluginInstallResult
                {
                    Success = true,
                    PluginId = manifest.Id,
                    Enabled = enabled,
                };
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"[plugin] 安装 {manifest.Id} 失败", ex);
                return new PluginInstallResult { Success = false, PluginId = manifest.Id, Error = ex.Message };
            }
        }
    }

    // ------------------------------------------------------------------ 启用 / 停用

    /// <summary>用户显式启用插件：运行时加载成功后再持久化 Enabled 偏好。</summary>
    public static bool Enable(string pluginId, out string error)
    {
        error = "";

        PluginActivationResult activation = Runtime.EnsurePluginLoaded(
            pluginId,
            PluginActivationReason.ManualEnable,
            requireEnabled: false);

        if (!activation.IsReady)
        {
            error = activation.Error;
            return false;
        }

        PluginInstance instance = activation.Instance!;
        instance.Entry.Enabled = true;
        PluginRegistryStore.UpsertEntry(instance.Entry);

        NotifyPluginSetChanged();
        return true;
    }

    /// <summary>兼容同步调用；新 UI 与管理流程应使用 <see cref="DisableAsync"/>。</summary>
    public static bool Disable(string pluginId, out string error)
    {
        PluginStopResult result = DisableAsync(
                pluginId,
                PluginStopReason.UserDisabled,
                DefaultStopGracePeriod,
                CancellationToken.None)
            .GetAwaiter().GetResult();

        error = result.IsFullyStopped ? "" : result.Message;
        return result.IsFullyStopped;
    }

    public static async Task<PluginStopResult> DisableAsync(
        string pluginId,
        PluginStopReason reason,
        TimeSpan? gracePeriod = null,
        CancellationToken cancellationToken = default)
    {
        PluginInstance? instance = Find(pluginId);
        if (instance == null)
        {
            return new PluginStopResult
            {
                Status = PluginStopStatus.Failed,
                PluginId = pluginId ?? "",
                Message = $"插件未安装：{pluginId}",
            };
        }

        var lazy = StopOperations.GetOrAdd(
            pluginId,
            _ => new Lazy<Task<PluginStopResult>>(
                () => StopPluginCoreAsync(instance, reason),
                LazyThreadSafetyMode.ExecutionAndPublication));

        Task<PluginStopResult> stopTask = lazy.Value;
        TimeSpan wait = gracePeriod ?? DefaultStopGracePeriod;

        if (wait == Timeout.InfiniteTimeSpan)
        {
            return await stopTask.ConfigureAwait(false);
        }

        Task delay = Task.Delay(wait, cancellationToken);
        Task completed = await Task.WhenAny(stopTask, delay).ConfigureAwait(false);
        if (ReferenceEquals(completed, stopTask))
        {
            return await stopTask.ConfigureAwait(false);
        }

        string pendingMessage =
            $"插件「{instance.Entry.Name}」仍有 {instance.ActiveCallCount} 个调用未结束；" +
            "已拒绝新调用并在后台等待，必要时请重启 StarPie。";
        instance.MarkStopPending(pendingMessage);
        instance.FlushHealth();

        return new PluginStopResult
        {
            Status = PluginStopStatus.Pending,
            PluginId = pluginId,
            Message = pendingMessage,
            RemainingCalls = instance.ActiveCallCount,
        };
    }

    private static async Task<PluginStopResult> StopPluginCoreAsync(
        PluginInstance instance,
        PluginStopReason reason)
    {
        string pluginId = instance.PluginId;
        try
        {
            bool persistDisabled = reason is not (
                PluginStopReason.PluginSystemShutdown or
                PluginStopReason.ApplicationExit);
            if (persistDisabled)
            {
                instance.Entry.Enabled = false;
                PluginRegistryStore.UpsertEntry(instance.Entry);
            }

            if (!instance.IsLoaded)
            {
                Runtime.NotifyPluginStopping(pluginId);
                Runtime.NotifyPluginStopped(pluginId);
                NotifyPluginSetChanged();
                bool requiresRestart = instance.RequiresRestart ||
                                       instance.State == PluginRuntimeState.RequiresRestart;
                return new PluginStopResult
                {
                    Status = requiresRestart
                        ? PluginStopStatus.RequiresRestart
                        : PluginStopStatus.AlreadyStopped,
                    PluginId = pluginId,
                    Message = requiresRestart
                        ? "插件运行时尚未完全释放，需要重启 StarPie。"
                        : "插件已经处于停止状态。",
                };
            }

            instance.UnloadVerdictFinalized = collected =>
            {
                if (collected) return;
                new PluginDispatcherFacade().Post(() =>
                {
                    AppLogger.LogWarn($"[plugin] {pluginId} 已停止，但插件程序集未能释放，需要重启 StarPie。");
                    NotifyUser("插件需要重启完成释放", $"{pluginId} 的程序集仍被引用，重启 StarPie 后才能彻底回收。");
                });
            };

            Task drained = instance.BeginStopping();
            Runtime.NotifyPluginStopping(pluginId);
            await drained.ConfigureAwait(false);

            try
            {
                await Task.Run(instance.Unload).ConfigureAwait(false);
            }
            finally
            {
                Runtime.NotifyPluginStopped(pluginId);
            }

            bool collected = await Task.Run(() => instance.WaitForUnloadVerdict(5000)).ConfigureAwait(false);
            if (collected) instance.ClearError();
            instance.FlushHealth();
            NotifyPluginSetChanged();

            return new PluginStopResult
            {
                Status = collected ? PluginStopStatus.Stopped : PluginStopStatus.RequiresRestart,
                PluginId = pluginId,
                Message = collected
                    ? $"插件 {pluginId} 已停止。"
                    : $"插件 {pluginId} 已停止，但旧程序集仍被引用，需要重启 StarPie。",
            };
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"[plugin] 停用 {pluginId} 失败（原因={reason}）", ex);
            return new PluginStopResult
            {
                Status = PluginStopStatus.Failed,
                PluginId = pluginId,
                Message = ex.Message,
                RemainingCalls = instance.ActiveCallCount,
            };
        }
        finally
        {
            StopOperations.TryRemove(pluginId, out _);
        }
    }

    /// <summary>兼容同步卸载；UI 与管理流程应使用 <see cref="UninstallAsync"/>。</summary>
    public static bool Uninstall(string pluginId, bool removePluginData, out string error)
    {
        PluginUninstallResult result = UninstallAsync(pluginId, removePluginData).GetAwaiter().GetResult();
        error = result.Error;
        return result.Success;
    }

    public static Task<PluginUninstallResult> UninstallAsync(
        string pluginId,
        bool removePluginData,
        CancellationToken cancellationToken = default) =>
        UninstallCoreAsync(pluginId, removePluginData, cancellationToken);

    internal static Task<PluginUninstallResult> UninstallForSelfTestAsync(
        string pluginId,
        bool removePluginData,
        CancellationToken cancellationToken = default) =>
        UninstallCoreAsync(pluginId, removePluginData, cancellationToken);

    private static async Task<PluginUninstallResult> UninstallCoreAsync(
        string pluginId,
        bool removePluginData,
        CancellationToken cancellationToken)
    {
        PluginInstance? instance = Find(pluginId);
        if (instance == null)
        {
            return new PluginUninstallResult { Success = false, Error = $"插件未安装：{pluginId}" };
        }
        PluginStopResult stop = await DisableAsync(
            pluginId,
            PluginStopReason.Uninstall,
            DefaultStopGracePeriod,
            cancellationToken).ConfigureAwait(false);

        if (!stop.IsFullyStopped)
        {
            return new PluginUninstallResult
            {
                Success = false,
                Error = $"插件尚未完全停止，不能删除文件：{stop.Message}",
            };
        }

        try
        {
            if (instance.IsExternal)
            {
                PluginRegistryStore.RemoveEntry(pluginId);
                lock (Gate) Instances.Remove(pluginId);

                AppLogger.LogInfo(
                    $"[plugin] 已卸载 {pluginId}（外部路径登记，源文件未删除：{instance.Entry.ExternalPath}）");
                NotifyPluginSetChanged();
                return new PluginUninstallResult { Success = true };
            }

            string directory = instance.ManagedDirectory;
            if (removePluginData && Directory.Exists(directory))
            {
                try
                {
                    Directory.Delete(directory, recursive: true);
                }
                catch (Exception deleteError)
                {
                    string pending = Path.Combine(PluginPaths.Root, ".pending-delete-" + Guid.NewGuid().ToString("N"));
                    try
                    {
                        Directory.Move(directory, pending);
                        AppLogger.LogWarn(
                            $"[plugin] {pluginId} 目录被占用，已挂起删除，将于下次启动时清理：{deleteError.Message}");
                    }
                    catch
                    {
                        return new PluginUninstallResult
                        {
                            Success = false,
                            Error = $"删除插件目录失败（文件被占用）：{deleteError.Message}。请重启 StarPie 后重试。",
                        };
                    }
                }
            }

            PluginRegistryStore.RemoveEntry(pluginId);
            lock (Gate) Instances.Remove(pluginId);

            AppLogger.LogInfo($"[plugin] 已卸载 {pluginId}（保留数据={!removePluginData}）");
            NotifyPluginSetChanged();
            return new PluginUninstallResult { Success = true };
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"[plugin] 卸载 {pluginId} 失败", ex);
            return new PluginUninstallResult { Success = false, Error = ex.Message };
        }
    }

    // ------------------------------------------------------------------ 参数校验接缝

    /// <summary>保存动作与执行前共用的参数校验入口；设置页校验不会触发惰性加载。</summary>
    public static PluginActionValidation ValidateActionParameters(ActionItem? action) =>
        Runtime.ValidateActionParameters(action);

    // ------------------------------------------------------------------ 执行接缝

    /// <summary>主程序唯一的插件动作入口，具体行为由动作路径模块负责。</summary>
    public static PluginExecuteOutcome ExecutePluginAction(ActionItem action) => Runtime.ExecuteAction(action);

    public static bool IsOfficialClaimedType(string? type) =>
        PluginActionClaimRegistry.IsOfficialClaimedType(type);

    public static bool TryResolveClaimedType(string? type, out PluginTypeClaimBinding binding) =>
        PluginActionClaimRegistry.TryResolve(type, out binding);

    public static PluginExecuteOutcome ExecuteClaimedAction(ActionItem action, PluginTypeClaimBinding binding) =>
        Runtime.ExecuteClaimedAction(action, binding);
    public static IReadOnlyList<string> ClaimedTypeNamesOf(string pluginId) =>
        PluginActionClaimRegistry.Snapshot()
            .Where(binding => string.Equals(binding.PluginId, pluginId, StringComparison.OrdinalIgnoreCase))
            .Select(binding => binding.TypeName)
            .ToArray();

    public static bool IsClaimedTypeAvailable(string? type, out string reason)
    {
        reason = "";
        if (!PluginActionClaimRegistry.TryResolve(type, out PluginTypeClaimBinding binding))
        {
            if (PluginActionClaimRegistry.IsOfficialClaimedType(type))
            {
                reason = "提供该动作的官方插件尚未安装或未登记。";
                return false;
            }

            return true;
        }

        if (!_enabled)
        {
            reason = "插件系统当前已关闭。";
            return false;
        }

        PluginInstance? instance = Find(binding.PluginId);
        if (instance == null)
        {
            reason = $"该动作由内置动作包「{binding.PluginId}」提供，但登记记录已经丢失。";
            return false;
        }
        if (!instance.Entry.Enabled)
        {
            reason = $"该动作属于内置动作包「{instance.Entry.Name}」，它当前已被停用。";
            return false;
        }
        if (instance.State == PluginRuntimeState.Quarantined)
        {
            reason = $"内置动作包「{instance.Entry.Name}」因连续出错已被隔离。";
            return false;
        }
        return true;
    }

    // ------------------------------------------------------------------ 界面数据

    /// <summary>动作下拉里的插件动作分组（供 <c>SlotViewModel</c> 聚合）。</summary>
    public static List<ActionTypeItem> GetPluginActionItems()
    {
        var items = new List<ActionTypeItem>();
        foreach (PluginActionRegistration action in Catalog.SnapshotActions())
        {
            items.Add(new ActionTypeItem
            {
                Tag = PluginApi.ActionTypeName,
                DisplayText = $"🔌 {action.DisplayName}",
            });
        }
        return items;
    }

    /// <summary>已注册的插件动作（供槽位编辑器按插件分组展示）。</summary>
    public static List<PluginActionRegistration> GetRegisteredActions()
    {
        HashSet<string> claimed = PluginActionClaimRegistry.Snapshot()
            .Select(binding => binding.FullId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Catalog.SnapshotActions().Where(action => !claimed.Contains(action.FullId)).ToList();
    }

    public static bool TryGetAction(string fullId, out PluginActionRegistration registration) =>
        Catalog.TryGetAction(fullId, out registration);

    /// <summary>预览文案。插件回调同样经过活动调用租约，失败时返回空串。</summary>
    public static string PreviewAction(string fullId, IReadOnlyDictionary<string, string> parameters) =>
        Runtime.PreviewAction(fullId, parameters);

    /// <summary>
    /// 构造一条指向插件动作的 <see cref="ActionItem"/>。
    /// <para>
    /// 刻意做成静态工厂而不是让调用方自己拼字段：插件动作的持久化形态（<c>Type="Plugin"</c> +
    /// <c>PluginActionRef</c> + <c>ExtensionData</c>）是契约的一部分，散落在各处手拼迟早会写出不一致的配置。
    /// </para>
    /// </summary>
    public static ActionItem? CreateActionItem(string fullId, Dictionary<string, string>? parameters = null)
    {
        if (!Catalog.TryGetAction(fullId, out PluginActionRegistration registration))
        {
            return null;
        }

        var action = new ActionItem
        {
            Type = PluginApi.ActionTypeName,
            Name = registration.DisplayName,
            IconKey = registration.IconKey ?? "",
            PluginActionRef = new PluginActionRef
            {
                PluginId = registration.PluginId,
                ContributionId = registration.ShortId,
            },
        };

        // 用参数默认值填充，让「新建动作」后立即就是可用的
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (ParameterField field in registration.Parameters)
        {
            if (!string.IsNullOrWhiteSpace(field.DefaultValue))
            {
                merged[field.Key] = field.DefaultValue!;
            }
        }
        if (parameters != null)
        {
            foreach (KeyValuePair<string, string> pair in parameters)
            {
                merged[pair.Key] = pair.Value;
            }
        }

        action.ExtensionData = merged.Count > 0 ? merged : null;
        return action;
    }

    /// <summary>
    /// 向用户提示一条与插件有关的信息。
    /// <para>优先走托盘气泡；没有可用托盘时降级为写日志 —— <b>绝不用 MessageBox</b>，
    /// 因为它会阻塞动作线程。</para>
    /// </summary>
    public static void NotifyUser(string title, string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        try
        {
            Action<string, string>? sink = PluginNotificationHub.Sink;
            if (sink != null)
            {
                sink(title ?? "StarPie 插件", message);
                return;
            }
        }
        catch
        {
        }

        AppLogger.LogInfo($"[plugin] 提示：{title} - {message}");
    }

    /// <summary>插件列表快照（供插件管理页）。</summary>
    public static List<PluginInstance> ListInstances()    {
        lock (Gate)
        {
            return Instances.Values
                .OrderBy(i => i.Entry.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }

    public static PluginInstance? Find(string pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId)) return null;
        lock (Gate)
        {
            return Instances.TryGetValue(pluginId, out PluginInstance? instance) ? instance : null;
        }
    }

    /// <summary>把全部插件的健康度落盘（宿主退出或界面刷新时调用）。</summary>
    public static void FlushHealth()
    {
        foreach (PluginInstance instance in ListInstances())
        {
            try
            {
                instance.FlushHealth();
            }
            catch
            {
            }
        }
    }

    /// <summary>开发者模式开关。开启时允许「只登记外部路径不复制文件」。</summary>
    public static void SetDeveloperMode(bool enabled)
    {
        _developerMode = enabled;
        _preferences.DeveloperMode = enabled;
    }

    public static void SetEnabled(bool enabled) =>
        SetEnabledAsync(enabled).GetAwaiter().GetResult();

    public static async Task SetEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        _enabled = enabled;
        _preferences.EnablePluginSystem = enabled;

        if (enabled)
        {
            NotifyPluginSetChanged();
            return;
        }

        List<Task<PluginStopResult>> stops = ListInstances()
            .Where(static instance => instance.IsLoaded)
            .Select(instance => DisableAsync(
                instance.PluginId,
                PluginStopReason.PluginSystemShutdown,
                DefaultStopGracePeriod,
                cancellationToken))
            .ToList();

        if (stops.Count > 0)
        {
            await Task.WhenAll(stops).ConfigureAwait(false);
        }

        NotifyPluginSetChanged();
    }

    // ------------------------------------------------------------------ 统一路径入口

    /// <summary>宿主当前登记的 SPP 路径，用于自检与后续清单兼容判断。</summary>
    public static IReadOnlyList<string> GetSupportedPathIds() => Runtime.SupportedPathIds;

    // 旧版 Opening / Closed 接口暂时作为统一交互路径的兼容适配层。
    public static IDisposable RegisterWheelOpening(string pluginId, Action<ActionContext> handler) =>
        Runtime.RegisterWheelOpening(pluginId, handler);

    public static IDisposable RegisterWheelClosed(string pluginId, Action handler) =>
        Runtime.RegisterWheelClosed(pluginId, handler);

    /// <summary>
    /// 广播「轮盘即将呈现」。当前沿用旧同步回调；正式的有界事件队列将在交互路径阶段实现。
    /// </summary>
    public static void RaiseWheelOpening(ActionContext context)
    {
        if (!_enabled) return;
        Runtime.RaiseWheelOpening(context);
    }

    public static void RaiseWheelClosed()
    {
        if (!_enabled) return;
        Runtime.RaiseWheelClosed();
    }

    /// <summary>
    /// 统一交互事件入口。当前尚未开放统一事件贡献，调用安全返回 0（没有订阅者接收）。
    /// </summary>
    public static int PublishInteractionEvent(PluginInteractionEventEnvelope interactionEvent)
    {
        if (!_enabled) return 0;
        return Runtime.PublishInteractionEvent(interactionEvent);
    }

    /// <summary>
    /// 统一轮盘结构入口。当前尚未开放结构提供者，调用安全返回空快照。
    /// </summary>
    public static ValueTask<PluginWheelStructureSnapshot> QueryWheelStructureAsync(
        PluginWheelStructureRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_enabled) return ValueTask.FromResult(PluginWheelStructureSnapshot.Empty);
        return Runtime.QueryWheelStructureAsync(request, cancellationToken);
    }

    // ------------------------------------------------------------------ 磁盘同步

    /// <summary>
    /// 把磁盘上的插件目录同步到内存登记表。返回本次「新发现」的数量。
    /// <para>只读清单文件，不加载程序集；新发现的插件一律登记为 Disabled。</para>
    /// </summary>
    public static int SyncFromDisk()
    {
        int discovered = 0;

        try
        {
            var knownIds = new HashSet<string>(
                PluginRegistryStore.SnapshotEntries().Select(e => e.Id),
                StringComparer.OrdinalIgnoreCase);

            // ① 已登记（含开发者外部路径）
            foreach (PluginRegistryEntry entry in PluginRegistryStore.SnapshotEntries())
            {
                PluginScanResult scan = ScanEntry(entry);

                // 关键：已在内存里的实例必须「就地更新」，绝不能 new 一个替换掉。
                // 旧实例仍然持有可回收加载上下文与已注册的贡献点，把它从字典里摘掉
                // 就会造出「孤儿」—— 动作还挂在轮盘上，宿主却再也找不到实例来卸载它，
                // 于是程序集、文件锁和内存全部无法释放。
                // 用户第二次进入插件管理页就会踩到这个坑（那里会先与磁盘对账）。
                PluginInstance? existing;
                lock (Gate)
                {
                    Instances.TryGetValue(entry.Id, out existing);
                }

                if (existing != null)
                {
                    existing.Entry = entry;
                    existing.Scan = scan;

                    // 只允许「尚未加载」的实例因识别失败而降级；
                    // 否则会把一个正在正常运行的插件误标成不兼容。
                    if (!scan.Accepted && existing.State != PluginRuntimeState.Active)
                    {
                        existing.MarkIncompatible(scan.DescribeFailure());
                    }

                    continue;
                }

                PluginInstance instance = new(entry.Id, entry, scan);

                if (!scan.Accepted)
                {
                    instance.MarkIncompatible(scan.DescribeFailure());
                }

                lock (Gate)
                {
                    Instances[entry.Id] = instance;
                }
            }

            // ② 手工放进「可写宿主区」目录（plugin-data\）但尚未登记的。
            // 注意这里只认「子目录 + plugin.json」——它对应的是「用户已经手工安装好了」，
            // 与只读来源区 <程序目录>\plugin\ 里那些待安装候选完全不是一回事（见 ScanCandidates）。
            foreach (string directory in Directory.GetDirectories(PluginPaths.Root))
            {
                string name = Path.GetFileName(directory);
                if (name.StartsWith(".pending-delete", StringComparison.OrdinalIgnoreCase)) continue;
                if (!File.Exists(PluginPaths.GetManifestPath(directory))) continue;

                PluginScanResult scan = PluginScanner.ScanInstalledPlugin(directory);
                if (!scan.Accepted || scan.Manifest == null) continue;
                if (!knownIds.Add(scan.Manifest.Id)) continue;

                var entry = new PluginRegistryEntry
                {
                    Id = scan.Manifest.Id,
                    Name = scan.Manifest.Name,
                    Version = scan.Manifest.Version,
                    Description = scan.Manifest.Description,
                    Author = scan.Manifest.Author,
                    License = scan.Manifest.License,
                    Homepage = scan.Manifest.Homepage,
                    InstallPath = name,
                    Enabled = false,
                    EntrySha256 = scan.Sha256,
                    Source = "Discovered",
                    InstalledAt = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"),
                };
                PluginRegistryStore.UpsertEntry(entry);

                var instance = new PluginInstance(entry.Id, entry, scan);
                lock (Gate)
                {
                    Instances[entry.Id] = instance;
                }
                discovered++;
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("[plugin] 同步插件目录失败", ex);
        }

        return discovered;
    }

    // ------------------------------------------------------------------ 只读扫描目录（候选）

    private static IReadOnlyList<PluginCandidate> _candidates = Array.Empty<PluginCandidate>();

    /// <summary>最近一次扫描出的候选插件清单。UI 直接读这个，不要自己去遍历目录。</summary>
    public static IReadOnlyList<PluginCandidate> Candidates
    {
        get { lock (Gate) { return _candidates; } }
    }

    /// <summary>
    /// 扫描<b>只读</b>目录 <c>程序目录\plugin</c>，得出「待安装候选」清单。
    /// <para>
    /// 与 <see cref="SyncFromDisk"/> 的<b>根本区别</b>：这里发现的东西<b>不会</b>登记、
    /// <b>不会</b>加载、也<b>不会</b>出现在插件列表里。它只说「这里躺着这些 .dll，
    /// 你可以装」，装不装由用户点按钮决定。
    /// </para>
    /// <para>
    /// 反过来，<see cref="SyncFromDisk"/> 第 ② 段会自动登记的是<b>可写宿主区</b>里
    /// 「子目录 + plugin.json」的手工投放 —— 那已经是安装产物了，与这里的候选是两回事。
    /// </para>
    /// <para>
    /// 目录不存在时直接得到空清单，<b>绝不创建它</b>：程序目录可能是只读的，
    /// 「本机没有随包附带的插件」本来就是完全正常的状态。
    /// </para>
    /// </summary>
    /// <returns>本次识别出的候选数量（含被拒绝、重复的）。</returns>
    public static int ScanCandidates()
    {
        var list = new List<PluginCandidate>();

        try
        {
            if (!PluginPaths.ScanRootExists)
            {
                lock (Gate) { _candidates = list; }
                return 0;
            }

            // 目录名固定从 PluginPaths 取；这里不递归子目录 ——
            // 扫描目录的约定就是「扁平，只放 .dll」，子目录一律不认。
            string[] files = Directory.GetFiles(PluginPaths.ScanRoot, "*.dll", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            var scans = new List<PluginScanResult>(files.Length);
            foreach (string file in files)
            {
                scans.Add(ScanCandidateFile(file));
            }

            // 同 ID 计数按扫描目录内部去重统计（大小写不敏感）：这是识别「两枚 dll 撞 ID」的依据。
            var idCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (PluginScanResult scan in scans)
            {
                string? id = scan.Manifest?.Id;
                if (string.IsNullOrWhiteSpace(id)) continue;
                idCounts[id!] = idCounts.TryGetValue(id!, out int n) ? n + 1 : 1;
            }

            IReadOnlyDictionary<string, PluginRegistryEntry> installed = SnapshotInstalledById();

            foreach (PluginScanResult scan in scans)
            {
                (PluginCandidateState state, string note) = ClassifyCandidate(scan, idCounts, installed);
                list.Add(new PluginCandidate
                {
                    DllPath = scan.DllPath,
                    FileName = Path.GetFileName(scan.DllPath),
                    Scan = scan,
                    State = state,
                    Note = note,
                });
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("[plugin] 扫描只读插件目录失败", ex);
        }

        lock (Gate) { _candidates = list; }
        return list.Count;
    }

    /// <summary>识别扫描目录里的一枚 dll。异常一律转成「识别未通过」而不是上抛 —— 一枚坏文件不该让整页空掉。</summary>
    internal static PluginScanResult ScanCandidateFile(string file)
    {
        try
        {
            return PluginScanner.ScanSelectedDll(
                file,
                allowReservedIdPrefix: IsFileInsideScanRoot(file));
        }
        catch (Exception ex)
        {
            return new PluginScanResult
            {
                DllPath = file,
                SourceDirectory = Path.GetDirectoryName(file) ?? "",
                Accepted = false,
                Failure = PluginScanFailure.NotDotNetAssembly,
                ErrorDetail = ex.Message,
            };
        }
    }

    private static bool IsFileInsideScanRoot(string file)
    {
        try
        {
            string parent = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(Path.GetDirectoryName(file) ?? ""));
            string scanRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(PluginPaths.ScanRoot));
            return string.Equals(parent, scanRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 为一枚<b>用户手动选中</b>的 <c>.dll</c> 判定「装下去会发生什么」。
    /// <para>
    /// 与候选路径共用同一套判定（<see cref="ClassifyCandidate"/>），只有一维不同：
    /// 手动选文件不存在「扫描目录内部两枚 dll 撞 ID」这种情况 —— 用户此刻选的是磁盘上
    /// 任意一处的一枚文件，扫描目录里躺着什么与它无关，所以撞 ID 上下文传空集合。
    /// </para>
    /// <para>
    /// 共用的意义在于「会发生什么」这句话<b>只写一遍</b>。两条路各判各的，很容易出现
    /// 「候选卡片说这是更新、手动安装却当成全新安装」这种同一枚文件两种说法的情况。
    /// </para>
    /// </summary>
    public static (PluginCandidateState State, string Note) ClassifyManualInstall(PluginScanResult scan)
        => ClassifyCandidate(scan, EmptyIdCounts, SnapshotInstalledById());

    /// <summary>
    /// 「调用方没有扫描目录上下文」时用的空撞 ID 表。
    /// <para>
    /// 刻意用空集合而不是 <c>null</c>：判定函数里少一个判空分支，语义也更直白 ——
    /// 「这个 ID 在扫描目录里只出现一次」，正是手动安装时的事实。
    /// </para>
    /// </summary>
    private static readonly IReadOnlyDictionary<string, int> EmptyIdCounts =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    /// <summary>已登记插件按 ID 建索引，供「和已装的那份比是什么关系」使用。</summary>
    private static Dictionary<string, PluginRegistryEntry> SnapshotInstalledById()
    {
        var installed = new Dictionary<string, PluginRegistryEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (PluginRegistryEntry entry in PluginRegistryStore.SnapshotEntries())
        {
            installed[entry.Id] = entry;
        }
        return installed;
    }

    /// <summary>
    /// 判定一枚候选与「已装的那份」是什么关系。
    /// <para>
    /// 顺序不能换：① 先看识别过没过（没过的连 ID 都没有，谈不上比较）；
    /// ② 再看 ID 是不是保留前缀（官方模块，无论如何都装不上，比出来的结论只会误导）；
    /// ③ 再看扫描目录内部有没有撞 ID（自身有歧义就不该继续比）；
    /// ④ 再看已装的那份是不是外部路径登记（那种情况下根本不该复制文件进来）；
    /// ⑤ 最后才比版本与哈希。
    /// </para>
    /// <para>
    /// 备注文案一律走 <see cref="I18n"/>：它会直接渲染在候选卡片的说明行上，
    /// 写死中文的话，切到英文 / 日文时这一行会与同卡片的状态徽标、按钮文案语言不一致。
    /// </para>
    /// <para>
    /// <b>已知欠账</b>：<c>PluginCandidateNoteRejected</c> 的占位符来自
    /// <see cref="PluginScanResult.DescribeFailure"/>，那一串
    /// （<see cref="PluginScanFailureText.Title"/> / <see cref="PluginScanFailureText.Hint"/> 与扫描器里
    /// 拼进去的 <c>ErrorDetail</c>）目前<b>全是硬编码中文</b>。所以非中文语言下，这一行是
    /// 「英文外壳 + 中文原因」的混排 —— 比修改前（整句中文）进了一步，但没到位。
    /// 彻底修要连识别器一起改（约 60 条短文案），属独立一轮，勿只改其中一段。
    /// </para>
    /// </summary>
    private static (PluginCandidateState State, string Note) ClassifyCandidate(
        PluginScanResult scan,
        IReadOnlyDictionary<string, int> idCounts,
        IReadOnlyDictionary<string, PluginRegistryEntry> installed)
    {
        if (!scan.Accepted || scan.Manifest == null)
        {
            return (PluginCandidateState.Rejected, I18n.TF("PluginCandidateNoteRejected", scan.DescribeFailure()));
        }

        string id = scan.Manifest.Id;

        // 保留前缀（starpie.* 等）＝ 官方模块。宿主在 InstallCandidateAsync 里按契约会拒绝它，
        // 所以这里必须判在「撞 ID」「版本比较」之前：那两者是为「可能装得上的候选」准备的，
        // 而这一枚无论比出什么结论都装不上 —— 报「两枚撞 ID」会把用户引去删文件，
        // 而真正该做的是别把官方模块放进扫描目录。
        if (PluginPaths.IsReservedPluginId(id))
        {
            return (PluginCandidateState.Reserved, I18n.TF("PluginCandidateNoteReserved", id));
        }

        if (idCounts.TryGetValue(id, out int sameId) && sameId > 1)
        {
            return (PluginCandidateState.Duplicate, I18n.TF("PluginCandidateNoteDuplicate", sameId, id));
        }

        if (!installed.TryGetValue(id, out PluginRegistryEntry? entry))
        {
            return (PluginCandidateState.Installable, I18n.T("PluginCandidateNoteInstallable"));
        }

        if (!string.IsNullOrWhiteSpace(entry.ExternalPath))
        {
            return (PluginCandidateState.ExternalRegistered,
                I18n.TF("PluginCandidateNoteExternalRegistered", entry.ExternalPath!));
        }

        string installedVersion = entry.Version ?? "";
        string candidateVersion = scan.Manifest.Version ?? "";

        bool sameHash = !string.IsNullOrWhiteSpace(entry.EntrySha256)
            && string.Equals(entry.EntrySha256, scan.Sha256, StringComparison.OrdinalIgnoreCase);

        if (SimpleVersion.TryParse(installedVersion, out SimpleVersion oldVersion)
            && SimpleVersion.TryParse(candidateVersion, out SimpleVersion newVersion))
        {
            int compare = newVersion.CompareTo(oldVersion);

            if (compare == 0)
            {
                return sameHash
                    ? (PluginCandidateState.Installed, I18n.TF("PluginCandidateNoteInstalled", installedVersion))
                    : (PluginCandidateState.Replaced, I18n.TF("PluginCandidateNoteReplaced", installedVersion));
            }

            if (compare > 0)
            {
                return (PluginCandidateState.Update, I18n.TF("PluginCandidateNoteUpdate", installedVersion, candidateVersion));
            }

            return (PluginCandidateState.Downgrade, I18n.TF("PluginCandidateNoteDowngrade", installedVersion, candidateVersion));
        }

        return (PluginCandidateState.VersionUnknown,
            I18n.TF("PluginCandidateNoteVersionUnknown", installedVersion, candidateVersion)
            + I18n.T(sameHash ? "PluginCandidateNoteSameContent" : "PluginCandidateNoteDifferentContent"));
    }

    /// <summary>
    /// 把一枚候选装进可写宿主区并启用。这是候选卡片上那个按钮的全部逻辑。
    /// <para>
    /// 安装动作本身仍复用 <see cref="CommitInstall"/>，这里只负责三件事：
    /// ① 拦住不允许安装的状态；② 替用户处理「正在运行所以文件被锁」；
    /// ③ 装完立刻重扫候选，让列表刷新成「已装同版本」。
    /// </para>
    /// </summary>
    public static bool InstallCandidate(PluginCandidate candidate, out string error)
    {
        PluginInstallResult result = InstallCandidateAsync(candidate).GetAwaiter().GetResult();
        error = result.Error;
        return result.Success;
    }

    public static async Task<PluginInstallResult> InstallCandidateAsync(
        PluginCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        if (candidate == null)
        {
            return new PluginInstallResult { Success = false, Error = "候选为空。" };
        }

        if (!candidate.CanInstall)
        {
            return new PluginInstallResult
            {
                Success = false,
                Error = $"当前状态不允许安装：{candidate.StateText}。{candidate.Note}",
            };
        }

        PluginScanResult scan = candidate.Scan;
        if (scan.Manifest == null)
        {
            return new PluginInstallResult { Success = false, Error = "识别结果里没有清单，无法安装。" };
        }

        if (PluginPaths.IsReservedPluginId(scan.Manifest.Id))
        {
            return new PluginInstallResult
            {
                Success = false,
                Error = "官方模块只能通过官方在线目录下载和安装。",
            };
        }

        var options = new PluginInstallOptions
        {
            Acknowledged = true,
            OverwriteExisting = true,
            EnableAfterInstall = true,
            SourceKind = "ScanDirectory",
            AcknowledgedCapabilities = scan.Manifest.Capabilities is { Count: > 0 } capabilities
                ? new List<string>(capabilities)
                : new List<string>(),
        };

        PluginInstallResult result = await CommitInstallAsync(scan, options, cancellationToken).ConfigureAwait(false);
        ScanCandidates();
        return result;
    }

    /// <summary>重新扫描单个插件（用户点了「刷新」）。</summary>
    public static PluginScanResult Rescan(string pluginId)
    {
        PluginInstance? instance = Find(pluginId);
        if (instance == null)
        {
            return new PluginScanResult { Accepted = false, Failure = PluginScanFailure.DllNotFound, ErrorDetail = "插件未登记。" };
        }

        PluginScanResult scan = ScanEntry(instance.Entry);
        instance.Scan = scan;

        if (!scan.Accepted && !instance.IsLoaded)
        {
            instance.MarkIncompatible(scan.DescribeFailure());
        }
        else if (scan.Accepted)
        {
            instance.ClearError();
        }

        return scan;
    }

    private static PluginScanResult ScanEntry(PluginRegistryEntry entry)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(entry.ExternalPath))
            {
                return PluginScanner.ScanSelectedDll(entry.ExternalPath!);
            }

            string directory = Path.Combine(
                PluginPaths.Root,
                string.IsNullOrWhiteSpace(entry.InstallPath) ? entry.Id : entry.InstallPath);

            return PluginScanner.ScanInstalledPlugin(
                directory,
                allowReservedIdPrefix: entry.Official);
        }
        catch (Exception ex)
        {
            return new PluginScanResult
            {
                Accepted = false,
                Failure = PluginScanFailure.ManifestInvalid,
                ErrorDetail = ex.Message,
                SourceDirectory = entry.Id,
            };
        }
    }

    // ------------------------------------------------------------------ 安全模式

    /// <summary>
    /// 启动时判定是否需要进入安全模式。
    /// <para>
    /// 判据：上一次启动时记录「已加载插件集」，并有连续 2 次在启动后 30 秒内异常退出。
    /// 触发后自动禁用那批插件，让用户至少能进得去设置页。
    /// </para>
    /// </summary>
    private static void CheckSafeMode()
    {
        try
        {
            PluginHealthFile health = PluginRegistryStore.Health;

            if (!string.IsNullOrWhiteSpace(health.SafeModeUntil)
                && DateTimeOffset.TryParse(health.SafeModeUntil, out DateTimeOffset until)
                && until > DateTimeOffset.Now)
            {
                _safeModeActive = true;
                AppLogger.LogWarn(
                    $"[plugin] 已进入安全模式（至 {until:yyyy-MM-dd HH:mm}），本次启动不加载任何插件。" +
                    "如果确认插件没有问题，可在插件页「重置插件系统」。");
                return;
            }

            if (health.ConsecutiveStartupFailures >= 2 && health.LastStartupPluginSet.Count > 0)
            {
                _safeModeActive = true;

                foreach (string pluginId in health.LastStartupPluginSet)
                {
                    PluginRegistryStore.SetEnabled(pluginId, false);
                    AppLogger.LogWarn($"[plugin] 安全模式：已自动禁用疑似导致启动失败的插件 {pluginId}");
                }

                PluginRegistryStore.MutateHealthFile(h =>
                {
                    h.SafeModeUntil = DateTimeOffset.Now.AddDays(1).ToString("yyyy-MM-ddTHH:mm:sszzz");
                    h.LastStartupPluginSet.Clear();
                    h.ConsecutiveStartupFailures = 0;
                });

                PluginNotificationHub.Sink?.Invoke(
                    "StarPie 已进入插件安全模式",
                    "检测到连续两次启动异常，已临时禁用上次加载的插件。请到「插件」页检查。");
            }

            // 标记一次「启动中」，30 秒后若仍存活则清零（见 ScheduleStartupHealthCheck）
            //
            // 无界面模式（自检 / 路径诊断）不参与记账：它们跑完立刻退出，永远活不到
            // 30 秒健康检查那一刻，于是计数只增不减。而安全模式的判据是
            // 「连续两次启动异常 **且** 上次启动加载过插件」—— 用户装好插件正常用着，
            // 连着跑两次自检就可能被判定为「启动异常」，下次打开 GUI 时插件被自动禁用。
            // 这种误伤比少记一次数严重得多。
            if (!HeadlessMode)
            {
                PluginRegistryStore.MutateHealthFile(h => h.ConsecutiveStartupFailures++);
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("[plugin] 安全模式判定失败", ex);
        }
    }

    /// <summary>启动 30 秒后确认存活：清零失败计数并记录本次加载的插件集。</summary>
    private static void ScheduleStartupHealthCheck()
    {
        try
        {
            var timer = new System.Threading.Timer(_ =>
            {
                try
                {
                    List<string> loaded = ListInstances()
                        .Where(i => i.IsLoaded)
                        .Select(i => i.PluginId)
                        .ToList();

                    PluginRegistryStore.MutateHealthFile(h =>
                    {
                        h.ConsecutiveStartupFailures = 0;
                        h.LastStartupPluginSet = loaded;
                    });

                    FlushHealth();
                    AppLogger.LogInfo(
                        $"[plugin] 启动健康检查通过：本会话加载 {loaded.Count} 个插件" +
                        (loaded.Count > 0 ? $"（{string.Join(", ", loaded)}）" : ""));
                }
                catch
                {
                }
            }, null, TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);

            _ = timer;
        }
        catch
        {
        }
    }

    /// <summary>
    /// 立即预加载单个插件：设置页勾选「开机预加载」后当场拉起，而不是等下一次启动。
    /// <para>失败不改写登记（勾选是用户偏好，下次启动仍会重试），只把原因返回给调用方。</para>
    /// </summary>
    public static bool PreloadNow(string pluginId, out string error)
    {
        error = "";
        if (!_initialized || !_enabled)
        {
            error = I18n.T("PluginsPreloadBlockedSystemOff");
            return false;
        }
        if (_safeModeActive)
        {
            error = I18n.T("PluginsPreloadBlockedSafeMode");
            return false;
        }

        PluginActivationResult activation = Runtime.EnsurePluginLoaded(
            pluginId,
            PluginActivationReason.StartupPreload,
            requireEnabled: true);
        if (!activation.IsReady)
        {
            error = activation.Error;
            AppLogger.LogWarn($"[plugin] 立即预加载 {pluginId} 失败：{activation.Error}");
            return false;
        }
        return true;
    }

    /// <summary>
    /// 立即补跑一轮启动预加载（用户在运行中打开「启动时预加载」总开关时用）。
    /// 条件与 <see cref="SchedulePreload"/> 一致：非安全模式且总开关已开，否则什么都不做。
    /// </summary>
    public static void RequestPreloadSweep()
    {
        if (!_initialized || _safeModeActive || !_preferences.PreloadOnStartup) return;
        SchedulePreload();
    }

    /// <summary>启动后台预加载（仅 Preload=true 的已启用插件），不阻塞首帧。</summary>
    private static void SchedulePreload()
    {
        try
        {
            Task.Run(async () =>
            {
                // 刻意延迟：让主程序先把首帧、托盘、钩子都装好
                await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);

                foreach (PluginInstance instance in ListInstances())
                {
                    if (!instance.Entry.Enabled || !instance.Entry.Preload) continue;
                    if (instance.IsLoaded) continue;

                    try
                    {
                        PluginActivationResult activation = Runtime.EnsurePluginLoaded(
                            instance.PluginId,
                            PluginActivationReason.StartupPreload,
                            requireEnabled: true);
                        if (!activation.IsReady)
                        {
                            AppLogger.LogWarn($"[plugin] 预加载 {instance.PluginId} 失败：{activation.Error}");
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogError($"[plugin] 预加载 {instance.PluginId} 异常", ex);
                    }

                    await Task.Delay(200).ConfigureAwait(false);
                }
            });
        }
        catch (Exception ex)
        {
            AppLogger.LogError("[plugin] 调度预加载失败", ex);
        }
    }

    private static List<string> BuildClaimWire(PluginManifest manifest) =>
        manifest.ClaimedTypes
            .Where(claim => !string.IsNullOrWhiteSpace(claim.TypeName)
                         && !string.IsNullOrWhiteSpace(claim.ContributionId))
            .Select(claim => claim.ToWire())
            .ToList();

    internal static void NotifyPluginSetChanged()
    {
        PluginActionClaimRegistry.Rebuild(PluginRegistryStore.SnapshotEntries());
        try
        {
            ConfigManager.MarkConfigurationChanged();
        }
        catch
        {
        }

        try
        {
            PluginAvailabilityChanged?.Invoke();
        }
        catch (Exception ex)
        {
            AppLogger.LogWarn($"[plugin] 通知动作可用性变化失败：{ex.Message}");
        }
    }

    // ------------------------------------------------------------------ 工具

    /// <summary>清理上次启动挂起的删除目录。</summary>
    private static void CleanupPendingDeletions()
    {
        try
        {
            foreach (string directory in Directory.GetDirectories(PluginPaths.Root, ".pending-delete-*"))
            {
                try
                {
                    Directory.Delete(directory, recursive: true);
                    AppLogger.LogInfo($"[plugin] 已清理挂起删除的目录：{Path.GetFileName(directory)}");
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    /// <summary>
    /// 按识别结果决定「复制什么」。
    /// <para>
    /// 规则只有一条，但必须说清为什么：<b>有没有 <c>plugin.json</c>，就是「这个目录是不是一个插件包」的判据</b>。
    /// </para>
    /// <list type="bullet">
    /// <item><c>ManifestSource == "Manifest"</c>：用户指的那个目录里有 <c>plugin.json</c>，
    /// 也就是在声明「这个目录整体是一个插件包」（可能带依赖 dll、图标、资源）。此时<b>整目录复制</b>。</item>
    /// <item><c>ManifestSource == "AssemblyMetadata"</c>：裸 DLL，靠程序集元数据兜底。
    /// 这种情况下 <c>SourceDirectory</c> 只表示「那枚 dll 碰巧躺在哪个目录」，它<b>不是</b>插件包 ——
    /// 可能正好是「下载」文件夹，也可能就是只读扫描目录 <c>plugin/</c>。
    /// 此时<b>只复制那一枚 dll</b>。
    /// <para>
    /// 早期版本在这里无条件整目录复制，有两个真实后果：从「下载」文件夹装一枚裸 dll
    /// 会把整个下载目录搬进插件目录；从 <c>plugin/</c> 安装则会把邻居插件的 dll 一起搬走
    /// —— 于是出现「只装了 A，B 也莫名其妙出现了」。
    /// </para></item>
    /// </list>
    /// </summary>
    internal static bool CopyPayload(PluginScanResult scan, string targetDirectory, bool overwrite, out string error)
    {
        if (string.Equals(scan.ManifestSource, "Manifest", StringComparison.Ordinal))
        {
            return CopyDirectory(scan.SourceDirectory, targetDirectory, overwrite, out error);
        }

        if (!CopySingleFile(scan.DllPath, targetDirectory, overwrite, out error))
        {
            return false;
        }

        // 裸 DLL 装完之后必须回填一份清单，否则安装目录「缺 plugin.json」，
        // 后续识别（进而是启用）会直接失败 —— 表现是「装上了却怎么都启不动」。
        return WriteGeneratedManifest(scan, targetDirectory, out error);
    }

    /// <summary>
    /// 为裸 DLL 安装回填 <c>plugin.json</c>：把扫描阶段已经确认过的事实固化成清单。
    /// <para>
    /// 只回填「确定的」：ID、名称、版本、作者、能力、入口程序集文件名。
    /// <b>EntryType</b> 也一并写上 —— 扫描阶段已经解析出来了，写下来能让后续加载不再依赖
    /// 「唯一实现」这种约定推断。
    /// </para>
    /// </summary>
    private static bool WriteGeneratedManifest(PluginScanResult scan, string targetDirectory, out string error)
    {
        PluginManifest source = scan.Manifest!;

        var manifest = new PluginManifest
        {
            SchemaVersion = PluginApi.ManifestSchemaVersion,
            Id = source.Id,
            Name = source.Name,
            Description = source.Description,
            Author = source.Author,
            Homepage = source.Homepage,
            License = source.License,
            Version = source.Version,
            ApiVersion = source.ApiVersion,
            MinHostVersion = source.MinHostVersion,
            MaxHostVersion = source.MaxHostVersion,
            TargetFramework = source.TargetFramework,
            Platform = source.Platform,
            Assembly = Path.GetFileName(scan.DllPath),
            EntryType = scan.EntryTypeFullName,
            Capabilities = new List<string>(source.Capabilities),
            Contributions = new PluginContributions { Actions = true },
            Tags = new List<string>(source.Tags),
        };

        return PluginManifestReader.TryWrite(targetDirectory, manifest, out error);
    }

    /// <summary>只复制一枚程序集（裸 DLL 安装用）。</summary>
    private static bool CopySingleFile(string sourceFile, string targetDirectory, bool overwrite, out string error)
    {
        error = "";
        try
        {
            if (string.IsNullOrWhiteSpace(sourceFile) || !File.Exists(sourceFile))
            {
                error = $"源文件不存在：{sourceFile}";
                return false;
            }

            string fileName = Path.GetFileName(sourceFile);

            // 与整目录复制保持同一条规则：SDK 契约程序集由宿主统一提供，插件不该自带一份。
            if (string.Equals(fileName, PluginApi.AbstractionsAssemblyName + ".dll", StringComparison.OrdinalIgnoreCase))
            {
                error = $"{fileName} 是宿主统一提供的 SDK 契约程序集，不能作为插件安装。";
                return false;
            }

            if (!Directory.Exists(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }
            else if (overwrite)
            {
                ClearPreviousPayload(targetDirectory);
            }
            else
            {
                error = $"目标目录已存在：{targetDirectory}";
                return false;
            }

            File.Copy(sourceFile, Path.Combine(targetDirectory, fileName), overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            error = $"复制插件文件失败：{ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// 覆盖安装裸 DLL 前，清掉上一次的「程序集 + 清单」。
    /// <para>
    /// 不清会踩两个坑：① 目录里留下两枚业务 dll，识别时的「唯一业务 dll」约定直接失效，
    /// 插件变成「找不到程序集」；② 上一次若是带 <c>plugin.json</c> 的包，残留清单会继续
    /// 接管识别，新装的裸 dll 会被判成「清单声明的入口类型不存在」。
    /// </para>
    /// <para>
    /// 只清「载荷」，<b>保留插件私有数据</b>：<c>data\</c> 目录与 <c>settings.json</c>
    /// 都是用户的东西，更新一次版本不该把它们清空。
    /// </para>
    /// </summary>
    private static void ClearPreviousPayload(string targetDirectory)
    {
        try
        {
            foreach (string file in Directory.GetFiles(targetDirectory, "*", SearchOption.TopDirectoryOnly))
            {
                if (string.Equals(Path.GetFileName(file), "settings.json", StringComparison.OrdinalIgnoreCase)) continue;
                File.Delete(file);
            }

            foreach (string directory in Directory.GetDirectories(targetDirectory, "*", SearchOption.TopDirectoryOnly))
            {
                if (string.Equals(Path.GetFileName(directory), "data", StringComparison.OrdinalIgnoreCase)) continue;
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogWarn($"[plugin] 覆盖安装前清理旧载荷失败（将按原样覆盖）：{ex.Message}");
        }
    }

    private static bool CopyDirectory(string source, string target, bool overwrite, out string error)
    {
        error = "";
        try
        {
            if (!Directory.Exists(source))
            {
                error = $"源目录不存在：{source}";
                return false;
            }

            if (Directory.Exists(target) && !overwrite)
            {
                error = $"目标目录已存在：{target}";
                return false;
            }

            Directory.CreateDirectory(target);

            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(source, file);
                string destination = Path.Combine(target, relative);

                // 不复制宿主会统一提供的 SDK 程序集，避免现场出现「类型身份分裂」的隐患
                string fileName = Path.GetFileName(file);
                if (string.Equals(fileName, PluginApi.AbstractionsAssemblyName + ".dll", StringComparison.OrdinalIgnoreCase))
                {
                    AppLogger.LogWarn($"[plugin] 已跳过安装包内的 {fileName}（由宿主统一提供）。");
                    continue;
                }

                string? destinationDirectory = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(destinationDirectory) && !Directory.Exists(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                File.Copy(file, destination, overwrite: true);
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"复制插件文件失败：{ex.Message}";
            return false;
        }
    }
}
