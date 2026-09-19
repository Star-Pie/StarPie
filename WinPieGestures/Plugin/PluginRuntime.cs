using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StarPie.Plugin;

namespace WinPieGestures.Plugins;

/// <summary>SPP 调用路径的稳定 ID。路径 ID 属于协议语义，不与具体 C# 类型名绑定。</summary>
internal static class PluginPathIds
{
    public const string ActionExecution = "action-execution";
    public const string InteractionEvent = "interaction-event";
    public const string WheelStructure = "wheel-structure";
}

/// <summary>
/// 一条插件调用路径的宿主模块。
/// <para>
/// 这个共同接口只统一路径的生命周期，不试图把动作、事件和轮盘结构压成同一种请求/结果。
/// 每条路径仍通过自己的强类型方法承载业务语义。
/// </para>
/// </summary>
internal abstract class PluginPathModule
{
    public abstract string PathId { get; }

    public virtual void OnPluginStopping(string pluginId)
    {
    }

    public virtual void OnPluginStopped(string pluginId)
    {
    }
}

/// <summary>
/// 宿主支持的调用路径注册表。新增路径只需注册新的 <see cref="PluginPathModule"/>，
/// 公共生命周期广播无需再增加中央 switch。
/// </summary>
internal sealed class PluginPathRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, PluginPathModule> _modules = new(StringComparer.OrdinalIgnoreCase);

    public void Register(PluginPathModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (string.IsNullOrWhiteSpace(module.PathId))
        {
            throw new ArgumentException("插件路径模块必须提供稳定的 PathId。", nameof(module));
        }

        lock (_gate)
        {
            if (!_modules.TryAdd(module.PathId, module))
            {
                throw new InvalidOperationException($"插件调用路径重复注册：{module.PathId}");
            }
        }
    }

    public IReadOnlyList<string> SnapshotPathIds()
    {
        lock (_gate)
        {
            return _modules.Keys.OrderBy(static id => id, StringComparer.Ordinal).ToArray();
        }
    }

    public void NotifyPluginStopping(string pluginId)
    {
        foreach (PluginPathModule module in SnapshotModules())
        {
            try
            {
                module.OnPluginStopping(pluginId);
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"[plugin] 路径 {module.PathId} 处理插件停止通知时异常", ex);
            }
        }
    }

    public void NotifyPluginStopped(string pluginId)
    {
        foreach (PluginPathModule module in SnapshotModules())
        {
            try
            {
                module.OnPluginStopped(pluginId);
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"[plugin] 路径 {module.PathId} 处理插件已停止通知时异常", ex);
            }
        }
    }

    private PluginPathModule[] SnapshotModules()
    {
        lock (_gate)
        {
            return _modules.Values.ToArray();
        }
    }
}

/// <summary>触发插件加载的宿主场景，用于诊断和后续策略区分。</summary>
internal enum PluginActivationReason
{
    ManualEnable,
    StartupPreload,
    ActionExecution,
    WheelStructureQuery,
}

internal enum PluginActivationStatus
{
    Ready,
    PluginSystemDisabled,
    NotInstalled,
    Disabled,
    Quarantined,
    Incompatible,
    Stopping,
    RequiresRestart,
    LoadFailed,
}

/// <summary>一次运行时激活的结果。加载机制公用，是否触发加载由各条路径自行决定。</summary>
internal sealed class PluginActivationResult
{
    public PluginActivationStatus Status { get; init; }
    public PluginInstance? Instance { get; init; }
    public string Error { get; init; } = "";

    public bool IsReady => Status == PluginActivationStatus.Ready && Instance != null;
}

/// <summary>
/// 插件运行时激活协调器。它只负责检查状态并确保已启用插件完成加载，
/// 不负责修改用户的 Enabled 偏好，也不决定哪一条路径应该惰性加载。
/// </summary>
internal sealed class PluginActivationCoordinator
{
    private readonly Func<string, PluginInstance?> _findInstance;
    private readonly Func<bool> _isPluginSystemEnabled;

    public PluginActivationCoordinator(
        Func<string, PluginInstance?> findInstance,
        Func<bool> isPluginSystemEnabled)
    {
        _findInstance = findInstance ?? throw new ArgumentNullException(nameof(findInstance));
        _isPluginSystemEnabled = isPluginSystemEnabled ?? throw new ArgumentNullException(nameof(isPluginSystemEnabled));
    }

    public PluginInstance? FindInstance(string pluginId) => _findInstance(pluginId);

    public PluginActivationResult EnsureLoaded(
        string pluginId,
        PluginActivationReason reason,
        bool requireEnabled)
    {
        if (!_isPluginSystemEnabled())
        {
            return Failure(PluginActivationStatus.PluginSystemDisabled, null, "插件系统已在设置中关闭。");
        }

        PluginInstance? instance = _findInstance(pluginId);
        if (instance == null)
        {
            return Failure(PluginActivationStatus.NotInstalled, null, $"插件未安装：{pluginId}");
        }

        if (instance.State == PluginRuntimeState.Quarantined)
        {
            return Failure(
                PluginActivationStatus.Quarantined,
                instance,
                $"插件「{instance.Entry.Name}」因连续出错已被自动禁用。");
        }

        if (instance.State == PluginRuntimeState.Incompatible)
        {
            return Failure(
                PluginActivationStatus.Incompatible,
                instance,
                string.IsNullOrWhiteSpace(instance.LastError)
                    ? $"插件「{instance.Entry.Name}」与当前宿主不兼容。"
                    : instance.LastError!);
        }

        if (instance.State == PluginRuntimeState.Stopping)
        {
            return Failure(
                PluginActivationStatus.Stopping,
                instance,
                $"插件「{instance.Entry.Name}」正在停止，暂时不能重新加载。");
        }

        if (instance.RequiresRestart || instance.State == PluginRuntimeState.RequiresRestart)
        {
            return Failure(
                PluginActivationStatus.RequiresRestart,
                instance,
                $"插件「{instance.Entry.Name}」的旧运行时尚未释放，请重启 StarPie 后再启用。");
        }

        if (requireEnabled && !instance.Entry.Enabled)
        {
            return Failure(
                PluginActivationStatus.Disabled,
                instance,
                $"插件「{instance.Entry.Name}」当前未启用，请在「插件」页启用后再试。");
        }

        if (instance.IsLoaded)
        {
            return Ready(instance);
        }

        AppLogger.LogInfo($"[plugin] {reason} 触发运行时加载：{pluginId}");
        if (!instance.EnsureLoaded(requireEnabled, out bool disabledDuringLoad, out string failure))
        {
            if (disabledDuringLoad)
            {
                return Failure(PluginActivationStatus.Disabled, instance, failure);
            }

            return Failure(
                PluginActivationStatus.LoadFailed,
                instance,
                string.IsNullOrWhiteSpace(failure) ? $"插件「{instance.Entry.Name}」加载失败。" : failure);
        }

        return Ready(instance);
    }

    private static PluginActivationResult Ready(PluginInstance instance) => new()
    {
        Status = PluginActivationStatus.Ready,
        Instance = instance,
    };

    private static PluginActivationResult Failure(
        PluginActivationStatus status,
        PluginInstance? instance,
        string error) => new()
    {
        Status = status,
        Instance = instance,
        Error = error,
    };
}

internal enum PluginStopReason
{
    UserDisabled,
    Reload,
    Update,
    Uninstall,
    PluginSystemShutdown,
    ApplicationExit,
    SelfTest,
}

internal enum PluginStopStatus
{
    AlreadyStopped,
    Stopped,
    Pending,
    RequiresRestart,
    Failed,
}

internal sealed class PluginStopResult
{
    public PluginStopStatus Status { get; init; }
    public string PluginId { get; init; } = "";
    public string Message { get; init; } = "";
    public int RemainingCalls { get; init; }

    public bool IsFullyStopped => Status is PluginStopStatus.AlreadyStopped or PluginStopStatus.Stopped;
}

internal sealed class PluginUninstallResult
{
    public bool Success { get; init; }
    public string Error { get; init; } = "";
}

/// <summary>
/// 三条路径共享的调用协调器。当前先统一异常隔离和诊断入口；后续活动调用租约、取消与超时
/// 会在这里扩展，而不复制到每一条路径。
/// </summary>
internal sealed class PluginCallCoordinator
{
    public bool TryAcquireInvocation(
        PluginInstance instance,
        PluginCallKind kind,
        out PluginInvocationLease? lease,
        out string error) =>
        instance.TryAcquireInvocation(kind, out lease, out error);

    public T Invoke<T>(
        string pathId,
        string operation,
        Func<T> callback,
        Func<Exception, T> fallback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(fallback);

        try
        {
            return callback();
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"[plugin:{pathId}] {operation} 发生未预期异常（已隔离）", ex);
            return fallback(ex);
        }
    }

    public void Invoke(string pathId, string operation, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        try
        {
            callback();
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"[plugin:{pathId}] {operation} 发生未预期异常（已隔离）", ex);
        }
    }

    public async ValueTask<T> InvokeAsync<T>(
        string pathId,
        string operation,
        Func<ValueTask<T>> callback,
        Func<Exception, T> fallback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(fallback);

        try
        {
            return await callback().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"[plugin:{pathId}] {operation} 发生未预期异常（已隔离）", ex);
            return fallback(ex);
        }
    }
}

/// <summary>
/// 插件调用运行时。主程序仍只接触 <see cref="PluginHost"/>；本类型在门面后统一登记路径、
/// 通过 <see cref="PluginCallCoordinator"/> 治理调用入口并广播插件生命周期，具体路径保持强类型实现。
/// </summary>
internal sealed class PluginRuntime
{
    private readonly PluginPathRegistry _paths = new();
    private readonly PluginCallCoordinator _calls = new();
    private readonly PluginActivationCoordinator _activation;

    public PluginRuntime(
        PluginCatalog catalog,
        Func<string, PluginInstance?> findInstance,
        Func<bool> isPluginSystemEnabled)
    {
        _activation = new PluginActivationCoordinator(findInstance, isPluginSystemEnabled);
        Actions = new ActionExecutionPathModule(catalog, _activation, _calls);
        Interactions = new InteractionEventPathModule(_activation, _calls);
        WheelStructures = new WheelStructurePathModule();

        _paths.Register(Actions);
        _paths.Register(Interactions);
        _paths.Register(WheelStructures);
    }

    public ActionExecutionPathModule Actions { get; }

    public InteractionEventPathModule Interactions { get; }

    public WheelStructurePathModule WheelStructures { get; }

    public IReadOnlyList<string> SupportedPathIds => _paths.SnapshotPathIds();

    public PluginActivationResult EnsurePluginLoaded(
        string pluginId,
        PluginActivationReason reason,
        bool requireEnabled) =>
        _activation.EnsureLoaded(pluginId, reason, requireEnabled);

    public PluginActionValidation ValidateActionParameters(ActionItem? action) => Actions.Validate(action);

    public string PreviewAction(string fullId, IReadOnlyDictionary<string, string> parameters) =>
        Actions.Preview(fullId, parameters);

    public PluginExecuteOutcome ExecuteAction(ActionItem action) =>
        _calls.Invoke(
            PluginPathIds.ActionExecution,
            "执行动作",
            () => Actions.Execute(action),
            static _ => new PluginExecuteOutcome
            {
                Handled = true,
                Success = false,
                Failure = PluginFailureKind.HostError,
                Message = "插件动作运行时发生内部错误，详情见日志。",
            });

    public PluginExecuteOutcome ExecuteClaimedAction(ActionItem action, PluginTypeClaimBinding binding) =>
        _calls.Invoke(
            PluginPathIds.ActionExecution,
            "执行认领动作",
            () => Actions.ExecuteClaimed(action, binding),
            static _ => new PluginExecuteOutcome
            {
                Handled = true,
                Success = false,
                Failure = PluginFailureKind.HostError,
                Message = "认领动作运行时发生内部错误，详情见日志。",
            });
    public IDisposable RegisterWheelOpening(string pluginId, Action<ActionContext> handler) =>
        _calls.Invoke(
            PluginPathIds.InteractionEvent,
            "注册 wheel.opening 兼容订阅",
            () => Interactions.RegisterWheelOpening(pluginId, handler),
            static _ => new RegistrationToken(static () => { }));

    public IDisposable RegisterWheelClosed(string pluginId, Action handler) =>
        _calls.Invoke(
            PluginPathIds.InteractionEvent,
            "注册 wheel.closed 兼容订阅",
            () => Interactions.RegisterWheelClosed(pluginId, handler),
            static _ => new RegistrationToken(static () => { }));

    public void RaiseWheelOpening(ActionContext context) =>
        _calls.Invoke(
            PluginPathIds.InteractionEvent,
            "广播 wheel.opening 兼容事件",
            () => Interactions.RaiseWheelOpening(context));

    public void RaiseWheelClosed() =>
        _calls.Invoke(
            PluginPathIds.InteractionEvent,
            "广播 wheel.closed 兼容事件",
            Interactions.RaiseWheelClosed);

    /// <summary>统一交互事件路径入口。当前仅建立强类型接缝，正式队列分发将在后续实现。</summary>
    public int PublishInteractionEvent(PluginInteractionEventEnvelope interactionEvent) =>
        _calls.Invoke(
            PluginPathIds.InteractionEvent,
            "发布统一交互事件",
            () => Interactions.Publish(interactionEvent),
            static _ => 0);

    /// <summary>统一轮盘结构路径入口。当前没有结构提供者时返回空快照。</summary>
    public ValueTask<PluginWheelStructureSnapshot> QueryWheelStructureAsync(
        PluginWheelStructureRequest request,
        CancellationToken cancellationToken) =>
        _calls.InvokeAsync(
            PluginPathIds.WheelStructure,
            "查询轮盘结构",
            () => WheelStructures.QueryAsync(request, cancellationToken),
            static _ => PluginWheelStructureSnapshot.Empty);

    public void NotifyPluginStopping(string pluginId) => _paths.NotifyPluginStopping(pluginId);

    public void NotifyPluginStopped(string pluginId) => _paths.NotifyPluginStopped(pluginId);
}
