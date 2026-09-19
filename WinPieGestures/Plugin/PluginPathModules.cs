using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using StarPie.Plugin;

namespace WinPieGestures.Plugins;

/// <summary>插件动作参数校验结果。声明式字段问题与插件自定义说明分开保存。</summary>
internal sealed class PluginActionValidation
{
    public List<PluginParameterIssue> DeclaredIssues { get; init; } = new();
    public string? PluginMessage { get; init; }

    public bool IsValid => DeclaredIssues.Count == 0 && string.IsNullOrEmpty(PluginMessage);

    public string? Describe()
    {
        if (DeclaredIssues.Count > 0) return DeclaredIssues[0].ToString();
        return string.IsNullOrEmpty(PluginMessage) ? null : PluginMessage;
    }
}

/// <summary>从持久化动作复制出的不可变执行请求，插件调用期间不再读取可变 ActionItem。</summary>
internal sealed class PluginActionRequest
{
    private PluginActionRequest(
        string pluginId,
        string contributionId,
        string actionName,
        IReadOnlyDictionary<string, string> parameters)
    {
        PluginId = pluginId;
        ContributionId = contributionId;
        ActionName = actionName;
        Parameters = parameters;
    }

    public string PluginId { get; }
    public string ContributionId { get; }
    public string FullId => $"{PluginId}.{ContributionId}";
    public string ActionName { get; }
    public IReadOnlyDictionary<string, string> Parameters { get; }

    public static PluginActionRequest CreateClaimed(ActionItem action, PluginTypeClaimBinding binding)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(binding);

        return new PluginActionRequest(
            binding.PluginId,
            binding.ContributionId,
            string.IsNullOrWhiteSpace(action.Name) ? binding.TypeName : action.Name,
            new ReadOnlyDictionary<string, string>(ActionParameterProjection.Project(action)));
    }
    public static bool TryCreate(ActionItem? action, out PluginActionRequest? request)
    {
        request = null;
        PluginActionRef? reference = action?.PluginActionRef;
        if (reference == null || !reference.IsValid) return false;

        var parameters = action!.ExtensionData != null
            ? new Dictionary<string, string>(action.ExtensionData, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        request = new PluginActionRequest(
            reference.PluginId.Trim(),
            reference.ContributionId.Trim(),
            action.Name ?? "",
            new ReadOnlyDictionary<string, string>(parameters));
        return true;
    }
}

/// <summary>
/// 动作执行路径：请求快照 → 公用激活 → 动作查询 → 统一参数校验 → 调度执行。
/// 惰性加载机制来自公共激活协调器，但只有动作路径决定在执行时触发它。
/// </summary>
internal sealed class ActionExecutionPathModule : PluginPathModule
{
    private readonly PluginCatalog _catalog;
    private readonly PluginActivationCoordinator _activation;
    private readonly PluginCallCoordinator _calls;

    public ActionExecutionPathModule(
        PluginCatalog catalog,
        PluginActivationCoordinator activation,
        PluginCallCoordinator calls)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _activation = activation ?? throw new ArgumentNullException(nameof(activation));
        _calls = calls ?? throw new ArgumentNullException(nameof(calls));
    }

    public override string PathId => PluginPathIds.ActionExecution;

    public override void OnPluginStopping(string pluginId) => _catalog.RevokeAll(pluginId);

    public PluginActionValidation Validate(ActionItem? action)
    {
        if (!PluginActionRequest.TryCreate(action, out PluginActionRequest? request))
        {
            return new PluginActionValidation();
        }

        try
        {
            if (!_catalog.TryGetAction(request!.FullId, out PluginActionRegistration registration))
            {
                // 设置页校验不应为了展示错误而加载或启用插件。
                return new PluginActionValidation();
            }

            return ValidateResolved(registration, request.Parameters);
        }
        catch (Exception ex)
        {
            AppLogger.LogError("[plugin] 参数校验流程异常（已放行）", ex);
            return new PluginActionValidation();
        }
    }

    public string Preview(string fullId, IReadOnlyDictionary<string, string> parameters)
    {
        try
        {
            if (!_catalog.TryGetAction(fullId, out PluginActionRegistration registration)) return "";
            PluginInstance? instance = _activation.FindInstance(registration.PluginId);
            if (instance == null ||
                !_calls.TryAcquireInvocation(
                    instance,
                    PluginCallKind.ActionPreview,
                    out PluginInvocationLease? lease,
                    out _))
            {
                return "";
            }

            using (lease)
            {
                return registration.Contribution.Preview(parameters) ?? "";
            }
        }
        catch
        {
            return "";
        }
    }

    public PluginExecuteOutcome Execute(ActionItem action)
    {
        if (!PluginActionRequest.TryCreate(action, out PluginActionRequest? request))
        {
            return PluginExecuteOutcome.NotHandled;
        }

        return ExecuteRequest(request!);
    }

    public PluginExecuteOutcome ExecuteClaimed(ActionItem action, PluginTypeClaimBinding binding) =>
        ExecuteRequest(PluginActionRequest.CreateClaimed(action, binding));

    private PluginExecuteOutcome ExecuteRequest(PluginActionRequest request)
    {
        PluginActivationResult activation = _activation.EnsureLoaded(
            request.PluginId,
            PluginActivationReason.ActionExecution,
            requireEnabled: true);

        if (!activation.IsReady)
        {
            // Disabled / Quarantined / RequiresRestart / Incompatible 都归到 NotEnabled：
            // 共同点是「这个插件此刻用不了」。具体是哪一种由 Message 说给用户听，
            // 而代码要区分的那一步（「插件没启用」而不是「参数写错了」）在这里就够了。
            return new PluginExecuteOutcome
            {
                Handled = true,
                Success = false,
                Failure = PluginFailureKind.NotEnabled,
                Message = activation.Error,
            };
        }

        PluginInstance instance = activation.Instance!;
        if (!_catalog.TryGetAction(request.FullId, out PluginActionRegistration registration))
        {
            return new PluginExecuteOutcome
            {
                Handled = true,
                Success = false,
                Failure = PluginFailureKind.ActionNotFound,
                Message = $"插件已加载，但没有注册动作 {request.FullId}。插件版本可能已变化，请重新编辑该槽位。",
            };
        }

        PluginActionValidation validation = ValidateResolved(registration, request.Parameters);
        if (!validation.IsValid)
        {
            return new PluginExecuteOutcome
            {
                Handled = true,
                Success = false,
                Failure = PluginFailureKind.ValidationFailed,
                Message = $"{registration.DisplayName} 参数不合法：{validation.Describe()}",
            };
        }

        return PluginInvoker.Invoke(instance, registration, request.Parameters, _calls);
    }
    private PluginActionValidation ValidateResolved(
        PluginActionRegistration registration,
        IReadOnlyDictionary<string, string> parameters)
    {
        List<PluginParameterIssue> declaredIssues =
            PluginParameterValidator.Validate(registration.Parameters, parameters);

        string? pluginMessage = null;
        PluginInstance? instance = _activation.FindInstance(registration.PluginId);
        PluginInvocationLease? lease = null;
        if (instance == null)
        {
            pluginMessage = "插件实例已不存在。";
        }
        else if (!_calls.TryAcquireInvocation(
                     instance,
                     PluginCallKind.ActionValidation,
                     out lease,
                     out string leaseError))
        {
            pluginMessage = leaseError;
        }
        else
        {
            using (lease)
            {
                try
                {
                    string? result = registration.Contribution.Validate(parameters);
                    if (!string.IsNullOrWhiteSpace(result)) pluginMessage = result.Trim();
                }
                catch (Exception ex)
                {
                    AppLogger.LogError($"[plugin] 动作 {registration.FullId} 的参数校验抛出异常", ex);
                    pluginMessage = $"插件自身的校验逻辑出错：{ex.GetBaseException().Message}（这是插件的问题，请反馈给插件作者）";
                }
            }
        }

        return new PluginActionValidation
        {
            DeclaredIssues = declaredIssues,
            PluginMessage = pluginMessage,
        };
    }
}

/// <summary>交互事件的只读信封。当前为宿主内部模型，不属于公共 SDK 契约。</summary>
internal sealed class PluginInteractionEventEnvelope
{
    public string EventType { get; init; } = "";
    public int SchemaVersion { get; init; } = 1;
    public long SessionId { get; init; }
    public long Sequence { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public ActionContext Context { get; init; } = new();
}

/// <summary>
/// 交互事件路径模块。旧版 Opening/Closed 订阅暂时由此托管；统一事件队列和背压行为后续补齐。
/// </summary>
internal sealed class InteractionEventPathModule : PluginPathModule
{
    private readonly object _gate = new();
    private readonly PluginActivationCoordinator _activation;
    private readonly PluginCallCoordinator _calls;
    private readonly Dictionary<string, List<Action<ActionContext>>> _wheelOpeningHandlers =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Action>> _wheelClosedHandlers =
        new(StringComparer.OrdinalIgnoreCase);

    public InteractionEventPathModule(
        PluginActivationCoordinator activation,
        PluginCallCoordinator calls)
    {
        _activation = activation ?? throw new ArgumentNullException(nameof(activation));
        _calls = calls ?? throw new ArgumentNullException(nameof(calls));
    }

    public override string PathId => PluginPathIds.InteractionEvent;

    public IDisposable RegisterWheelOpening(string pluginId, Action<ActionContext> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_gate)
        {
            if (!_wheelOpeningHandlers.TryGetValue(pluginId, out List<Action<ActionContext>>? list))
            {
                list = new List<Action<ActionContext>>();
                _wheelOpeningHandlers[pluginId] = list;
            }
            list.Add(handler);
        }

        return new RegistrationToken(() => RemoveWheelOpening(pluginId, handler));
    }

    public IDisposable RegisterWheelClosed(string pluginId, Action handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_gate)
        {
            if (!_wheelClosedHandlers.TryGetValue(pluginId, out List<Action>? list))
            {
                list = new List<Action>();
                _wheelClosedHandlers[pluginId] = list;
            }
            list.Add(handler);
        }

        return new RegistrationToken(() => RemoveWheelClosed(pluginId, handler));
    }

    public void RaiseWheelOpening(ActionContext context)
    {
        foreach ((string pluginId, Action<ActionContext> handler) in SnapshotWheelOpeningHandlers())
        {
            PluginInstance? instance = _activation.FindInstance(pluginId);
            if (instance == null ||
                !_calls.TryAcquireInvocation(
                    instance,
                    PluginCallKind.InteractionEvent,
                    out PluginInvocationLease? lease,
                    out _))
            {
                continue;
            }

            using (lease)
            {
                try { handler(context); }
                catch (Exception ex)
                {
                    AppLogger.LogError($"[plugin:{pluginId}] OnWheelOpening 回调异常（已拦截）", ex);
                }
            }
        }
    }

    public void RaiseWheelClosed()
    {
        foreach ((string pluginId, Action handler) in SnapshotWheelClosedHandlers())
        {
            PluginInstance? instance = _activation.FindInstance(pluginId);
            if (instance == null ||
                !_calls.TryAcquireInvocation(
                    instance,
                    PluginCallKind.InteractionEvent,
                    out PluginInvocationLease? lease,
                    out _))
            {
                continue;
            }

            using (lease)
            {
                try { handler(); }
                catch (Exception ex)
                {
                    AppLogger.LogError($"[plugin:{pluginId}] OnWheelClosed 回调异常（已拦截）", ex);
                }
            }
        }
    }

    /// <summary>
    /// 统一交互事件入口占位。返回实际投递数；当前尚未开放统一事件贡献，因此固定为 0。
    /// </summary>
    public int Publish(PluginInteractionEventEnvelope interactionEvent)
    {
        ArgumentNullException.ThrowIfNull(interactionEvent);
        return 0;
    }

    public override void OnPluginStopping(string pluginId)
    {
        lock (_gate)
        {
            _wheelOpeningHandlers.Remove(pluginId);
            _wheelClosedHandlers.Remove(pluginId);
        }
    }

    private void RemoveWheelOpening(string pluginId, Action<ActionContext> handler)
    {
        lock (_gate)
        {
            if (!_wheelOpeningHandlers.TryGetValue(pluginId, out List<Action<ActionContext>>? list)) return;
            list.Remove(handler);
            if (list.Count == 0) _wheelOpeningHandlers.Remove(pluginId);
        }
    }

    private void RemoveWheelClosed(string pluginId, Action handler)
    {
        lock (_gate)
        {
            if (!_wheelClosedHandlers.TryGetValue(pluginId, out List<Action>? list)) return;
            list.Remove(handler);
            if (list.Count == 0) _wheelClosedHandlers.Remove(pluginId);
        }
    }

    private (string PluginId, Action<ActionContext> Handler)[] SnapshotWheelOpeningHandlers()
    {
        lock (_gate)
        {
            var handlers = new List<(string, Action<ActionContext>)>();
            foreach (KeyValuePair<string, List<Action<ActionContext>>> pair in _wheelOpeningHandlers)
            {
                foreach (Action<ActionContext> handler in pair.Value) handlers.Add((pair.Key, handler));
            }
            return handlers.ToArray();
        }
    }

    private (string PluginId, Action Handler)[] SnapshotWheelClosedHandlers()
    {
        lock (_gate)
        {
            var handlers = new List<(string, Action)>();
            foreach (KeyValuePair<string, List<Action>> pair in _wheelClosedHandlers)
            {
                foreach (Action handler in pair.Value) handlers.Add((pair.Key, handler));
            }
            return handlers.ToArray();
        }
    }
}

/// <summary>轮盘结构查询请求占位。当前只建立宿主内部强类型接缝。</summary>
internal sealed class PluginWheelStructureRequest
{
    public string ProviderId { get; init; } = "";
    public string ProfileId { get; init; } = "";
    public ActionContext Context { get; init; } = new();
}

/// <summary>轮盘结构快照占位。正式节点模型稳定前不向公共 SDK 暴露。</summary>
internal sealed class PluginWheelStructureSnapshot
{
    public static readonly PluginWheelStructureSnapshot Empty = new();

    public bool IsAvailable { get; init; }
    public string ProviderId { get; init; } = "";
}

/// <summary>轮盘结构路径模块。当前未注册结构提供者时始终返回空快照。</summary>
internal sealed class WheelStructurePathModule : PluginPathModule
{
    public override string PathId => PluginPathIds.WheelStructure;

    public ValueTask<PluginWheelStructureSnapshot> QueryAsync(
        PluginWheelStructureRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        _ = cancellationToken;
        return ValueTask.FromResult(PluginWheelStructureSnapshot.Empty);
    }
}