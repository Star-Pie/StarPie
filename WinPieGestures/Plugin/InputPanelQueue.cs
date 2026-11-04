using System;
using System.Threading;
using StarPie.Plugin;

namespace WinPieGestures.Plugins;

/// <summary>Host-only queue state; injected sinks allow input-free behavior verification.</summary>
internal sealed class InputPanelQueue
{
    internal const int MaximumPending = 64;
    private readonly object _gate = new();
    private readonly Func<bool> _isValid;
    private readonly Action<ActionItem, ActionExecutionContext> _enqueue;
    private readonly nint _target;
    private readonly uint _pid;
    private readonly string _processName;
    private readonly ActionContext? _pluginTarget;
    private readonly CancellationToken _cancellation;
    private int _open = 1;
    private int _pending;

    internal InputPanelQueue(nint target, uint pid, string processName, Func<bool> isValid,
        Action<ActionItem, ActionExecutionContext> enqueue, ActionContext? pluginTarget = null,
        CancellationToken cancellation = default)
    {
        _target = target;
        _pid = pid;
        _processName = processName;
        _isValid = isValid;
        _enqueue = enqueue;
        _pluginTarget = pluginTarget;
        _cancellation = cancellation;
    }

    internal bool IsOpen => Volatile.Read(ref _open) != 0;
    internal int Pending { get { lock (_gate) return _pending; } }
    internal void Close() => Interlocked.Exchange(ref _open, 0);

    internal InputPanelQueueResult Queue(ActionItem action)
    {
        if (!IsOpen || !_isValid()) return InputPanelQueueResult.Closed;
        // Clone before publication. The queue never retains a plugin-owned dictionary.
        ActionItem snapshot = action.Clone();
        lock (_gate)
        {
            if (!IsOpen) return InputPanelQueueResult.Closed;
            if (_pending >= MaximumPending) return InputPanelQueueResult.Busy;
            _pending++;
        }
        int consumed = 0;
        void Release()
        {
            if (Interlocked.Exchange(ref consumed, 1) == 0)
                lock (_gate) _pending--;
        }
        var context = new ActionExecutionContext(_target, _pid, _processName,
            () => IsOpen && _isValid(), Release, _pluginTarget, _cancellation);
        try { _enqueue(snapshot, context); }
        catch { Release(); throw; }
        return InputPanelQueueResult.Accepted;
    }
}
