using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using StarPie.Plugin;

namespace WinPieGestures.Plugins;

internal sealed class PluginInputPanelService : IHostInputPanelService
{
    private readonly PluginInstance _owner;
    private readonly long _generation;
    private readonly PluginCapability _capabilities;

    internal PluginInputPanelService(PluginInstance owner, PluginCapability capabilities)
    {
        _owner = owner;
        _generation = owner.GenerationId;
        _capabilities = capabilities;
    }

    private void Require(PluginCapability capability)
    {
        if ((_capabilities & capability) != capability)
            throw new PluginCapabilityDeniedException(capability, nameof(IHostInputPanelService), _owner.PluginId);
        if (!_owner.CanAcceptInteraction(_generation))
            throw new InvalidOperationException(I18n.T("InputPanelOwnerUnavailable"));
    }

    public IInputPanelSession Open(ActionContext target, Action<InputPanelCloseReason> onClosed)
    {
        Require(PluginCapability.Ui);
        Require(PluginCapability.InputSimulation);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(onClosed);
        return InputPanelCoordinator.Open(_owner, _generation, target, onClosed, _capabilities);
    }

    public InputPanelAppearance GetAppearance()
    {
        Require(PluginCapability.Ui);
        if (Application.Current?.Dispatcher.CheckAccess() != true)
            throw new InvalidOperationException("Read the input panel palette on the UI dispatcher.");
        static string Color(string key, string fallback) => Application.Current?.TryFindResource(key)
            is System.Windows.Media.SolidColorBrush brush ? brush.Color.ToString() : fallback;
        return new()
        {
            Background = Color("WindowBackgroundBrush", "#F8FAFC"),
            Surface = Color("InputBackgroundBrush", "#FFFFFF"),
            Foreground = Color("TextPrimaryBrush", "#172331"),
            Border = Color("InputBorderBrush", "#CBD5E1"),
            Accent = Color("AccentPrimaryBrush", "#526F55"),
            AccentForeground = Color("AccentTextBrush", "#FFFFFF"),
        };
    }

    public PluginActionBindingInfo DescribeAction(ConfiguredPluginAction binding)
    {
        Require(PluginCapability.PluginActions);
        return Describe(_owner.PluginId, binding);
    }

    public async Task<PluginActionBindingInfo> ResolveActionAsync(ConfiguredPluginAction binding,
        CancellationToken cancellationToken = default)
    {
        Require(PluginCapability.PluginActions);
        cancellationToken.ThrowIfCancellationRequested();
        if (TryParse(binding, out string pluginId, out _) &&
            !string.Equals(pluginId, _owner.PluginId, StringComparison.OrdinalIgnoreCase))
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                Require(PluginCapability.PluginActions);
                PluginHost.LoadActionsForBinding(pluginId);
            }, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        Require(PluginCapability.PluginActions);
        return Describe(_owner.PluginId, binding);
    }

    internal static PluginActionBindingInfo Describe(string ownerId, ConfiguredPluginAction? binding)
    {
        if (!TryParse(binding, out string pluginId, out _))
            return new() { UnavailableReason = I18n.T("InputPanelInvalidAction") };
        PluginInstance? target = PluginHost.Find(pluginId);
        bool available = target != null && target.Entry.Enabled &&
            target.State is not (PluginRuntimeState.Stopping or PluginRuntimeState.Quarantined or
                PluginRuntimeState.Incompatible or PluginRuntimeState.RequiresRestart or PluginRuntimeState.Failed) &&
            !string.Equals(ownerId, pluginId, StringComparison.OrdinalIgnoreCase);
        bool registered = PluginHost.TryGetAction(binding!.ActionId, out var action);
        available &= registered;
        return new()
        {
            IsAvailable = available,
            DisplayName = registered ? action.CurrentDisplayName : binding.ActionId,
            UnavailableReason = available ? null : I18n.T("InputPanelActionUnavailable"),
        };
    }

    internal static bool TryParse(ConfiguredPluginAction? binding, out string pluginId, out string shortId)
    {
        pluginId = shortId = "";
        if (binding == null || string.IsNullOrWhiteSpace(binding.ActionId) || binding.ActionId.Length > 256 || binding.Parameters == null || binding.Parameters.Count > 128)
            return false;
        int separator = binding.ActionId.LastIndexOf('.');
        if (separator <= 0 || separator == binding.ActionId.Length - 1) return false;
        pluginId = binding.ActionId[..separator];
        shortId = binding.ActionId[(separator + 1)..];
        return binding.Parameters.All(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Key.Length <= 128 &&
            pair.Value != null && pair.Value.Length <= PluginApi.MaxParameterValueLength);
    }

    internal static ActionItem? BuildAction(ConfiguredPluginAction binding)
    {
        if (!TryParse(binding, out string pluginId, out string shortId)) return null;
        return new ActionItem
        {
            Type = PluginApi.ActionTypeName,
            Name = binding.ActionId,
            PluginActionRef = new PluginActionRef { PluginId = pluginId, ContributionId = shortId },
            ExtensionData = new Dictionary<string, string>(binding.Parameters, StringComparer.OrdinalIgnoreCase),
        };
    }

    public async Task<ConfiguredPluginAction?> EditActionAsync(ConfiguredPluginAction? current,
        long ownerWindowHandle = 0, CancellationToken cancellationToken = default)
    {
        Require(PluginCapability.Ui);
        Require(PluginCapability.PluginActions);
        cancellationToken.ThrowIfCancellationRequested();
        Application app = Application.Current ?? throw new InvalidOperationException(I18n.T("InputPanelOwnerUnavailable"));
        return await app.Dispatcher.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            Require(PluginCapability.PluginActions);
            InputPanelCoordinator.CloseActive(InputPanelCloseReason.Replaced);
            var editor = new PluginActionBindingDialog(_owner.PluginId, current, ownerWindowHandle);
            using CancellationTokenRegistration registration = cancellationToken.Register(() =>
                editor.Dispatcher.BeginInvoke((Action)(() => { if (editor.IsVisible) editor.Close(); })));
            return editor.ShowDialog() == true ? editor.Result : null;
        });
    }
}

internal static class InputPanelCoordinator
{
    private static InputPanelSession? _active;
    private static int _escapeDown;

    internal static IInputPanelSession Open(PluginInstance owner, long generation, ActionContext target,
        Action<InputPanelCloseReason> onClosed, PluginCapability capabilities)
    {
        if (Application.Current?.Dispatcher.CheckAccess() != true)
            throw new InvalidOperationException("Input panels must be opened on the UI dispatcher.");
        nint hwnd = (nint)target.ForegroundWindowHandle;
        uint pid = ActiveWindowHelper.GetWindowRealProcessId(hwnd);
        if (hwnd == 0 || pid == 0 || pid == (uint)Environment.ProcessId || App.IsExiting ||
            App.MainMouseHook?.IsPaused == true || !IsTargetValid(hwnd, pid))
            throw new InvalidOperationException(I18n.T("InputPanelTargetUnavailable"));
        CloseActive(InputPanelCloseReason.Replaced);
        ActionPanelManager.Close();
        var session = new InputPanelSession(owner, generation, hwnd, pid, target, onClosed, capabilities);
        Volatile.Write(ref _active, session);
        try
        {
            session.StartMonitoring();
            if (!IsTargetValid(hwnd, pid)) session.Close(InputPanelCloseReason.TargetChanged);
        }
        catch { session.Close(InputPanelCloseReason.TargetChanged); throw; }
        return session;
    }

    internal static bool IsTargetValid(nint target, uint pid) =>
        WheelFocusSwitcher.IsWindow(target) && WheelFocusSwitcher.IsWindowVisible(target) &&
        WheelFocusSwitcher.GetForegroundWindowSafe() == target && ActiveWindowHelper.GetWindowRealProcessId(target) == pid;

    internal static void Detach(InputPanelSession session) => Interlocked.CompareExchange(ref _active, null, session);
    internal static void CloseActive(InputPanelCloseReason reason) => Volatile.Read(ref _active)?.Close(reason);
    internal static void Revoke(PluginInstance owner, long generation)
    {
        InputPanelSession? session = Volatile.Read(ref _active);
        if (session?.IsOwnedBy(owner, generation) == true) session.Close(InputPanelCloseReason.PluginStopped);
    }

    // Hook path: atomics, state invalidation and a UI post only. Drain the matching
    // release even if pause/target change has already removed the active session.
    internal static bool HandleEscape(uint key, bool down)
    {
        if (key != 27) return false;
        if (!down) return Interlocked.Exchange(ref _escapeDown, 0) != 0;
        if (Volatile.Read(ref _escapeDown) != 0) return true;
        InputPanelSession? session = Volatile.Read(ref _active);
        if (session?.IsOpen != true) return false;
        Interlocked.Exchange(ref _escapeDown, 1);
        session.Close(InputPanelCloseReason.Escape);
        return true;
    }
}

internal sealed class InputPanelSession : IInputPanelSession
{
    private readonly PluginInstance _owner;
    private readonly long _generation;
    private readonly nint _target;
    private readonly uint _pid;
    private readonly PluginCapability _capabilities;
    private readonly InputPanelQueue _queue;
    private readonly CancellationTokenSource _cancellation = new();
    private Action<InputPanelCloseReason>? _closed;
    private PinnedWheelMonitor? _monitor;
    private InputPanelTargetGuard? _targetGuard;
    private int _closedOnce;

    internal InputPanelSession(PluginInstance owner, long generation, nint target, uint pid, ActionContext original,
        Action<InputPanelCloseReason> closed, PluginCapability capabilities)
    {
        _owner = owner; _generation = generation; _target = target; _pid = pid;
        _capabilities = capabilities; _closed = closed;
        var snapshot = new ActionContext
        {
            ForegroundWindowHandle = target, ForegroundProcessName = original.ForegroundProcessName,
            ForegroundWindowTitle = original.ForegroundWindowTitle, CursorX = original.CursorX, CursorY = original.CursorY,
            IsElevated = original.IsElevated, LanguageCode = original.LanguageCode,
        };
        _queue = new InputPanelQueue(target, pid, snapshot.ForegroundProcessName, Valid, ActionExecutor.EnqueueAction,
            snapshot, _cancellation.Token);
    }

    private bool Valid() => IsOpen && _owner.CanAcceptInteraction(_generation) && !App.IsExiting &&
        App.MainMouseHook?.IsPaused != true && InputPanelCoordinator.IsTargetValid(_target, _pid);
    public bool IsOpen => _queue.IsOpen;
    internal bool IsOwnedBy(PluginInstance owner, long generation) => ReferenceEquals(_owner, owner) && _generation == generation;
    internal void StartMonitoring()
    {
        _targetGuard = new InputPanelTargetGuard(Environment.TickCount64, () => Environment.TickCount64, () => IsOpen,
            () => Close(InputPanelCloseReason.TargetChanged));
        _monitor = new PinnedWheelMonitor(_target, _targetGuard.OnTargetEvent);
    }

    public InputPanelQueueResult QueueText(string text) => string.IsNullOrEmpty(text) || text.Length > PluginApi.MaxParameterValueLength
        ? InputPanelQueueResult.Invalid : _queue.Queue(new ActionItem { Type = "Text", Name = "Input panel", Parameter = text });
    public InputPanelQueueResult QueueHotkey(string hotkey) => !InputPanelHotkey.TryNormalize(hotkey, out string normalized)
        ? InputPanelQueueResult.Invalid : _queue.Queue(new ActionItem { Type = "Hotkey", Name = "Input panel", Parameter = normalized });
    public InputPanelQueueResult QueueAction(ConfiguredPluginAction binding)
    {
        if ((_capabilities & PluginCapability.PluginActions) == 0)
            throw new PluginCapabilityDeniedException(PluginCapability.PluginActions, nameof(QueueAction), _owner.PluginId);
        if (!PluginInputPanelService.Describe(_owner.PluginId, binding).IsAvailable) return InputPanelQueueResult.Unavailable;
        ActionItem? action = PluginInputPanelService.BuildAction(binding);
        return action == null ? InputPanelQueueResult.Invalid : _queue.Queue(action);
    }

    internal void Close(InputPanelCloseReason reason)
    {
        if (Interlocked.Exchange(ref _closedOnce, 1) != 0) return;
        _queue.Close();
        InputPanelCoordinator.Detach(this);
        // A plugin's cancellation callbacks may block. Never run them inside the
        // low-level hook or block the UI during target/pause cancellation.
        _ = Task.Run(() => { try { _cancellation.Cancel(); } catch { } finally { _cancellation.Dispose(); } });
        Action<InputPanelCloseReason>? callback = Interlocked.Exchange(ref _closed, null);
        if (reason == InputPanelCloseReason.PluginStopped) callback = null;
        void Finish()
        {
            _monitor?.Dispose(); _monitor = null;
            _targetGuard = null;
            _owner.Logger.Info($"Input panel session closed ({reason}).");
            if (callback == null || !_owner.TryAcquireInvocation(_generation, PluginCallKind.InteractionEvent, out var lease, out _)) return;
            using (lease)
            {
                try { callback(reason); }
                catch (Exception ex) { AppLogger.LogError("Input panel close callback failed", ex); }
            }
        }
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher?.CheckAccess() == true) Finish();
        else if (dispatcher != null && !dispatcher.HasShutdownStarted) dispatcher.BeginInvoke((Action)Finish);
    }

    public void Dispose() => Close(InputPanelCloseReason.Disposed);
}
