using StarPie.Plugin;

namespace WinPieGestures.Plugins;

/// <summary>每插件串行有界调度。Publish 只入队，不执行插件代码或取消回调。</summary>
internal sealed class PluginInteractionQueue
{
    private readonly record struct Delivery(InteractionEvent Event, PluginInteractionGroup Group);
    private readonly object _gate = new();
    private readonly LinkedList<Delivery> _pending = new();
    private readonly CancellationTokenSource _cancel = new();
    private readonly PluginCallCoordinator _calls;
    private readonly int _capacity;
    private readonly Func<bool> _isCurrent;
    private bool _stopped;
    private bool _running;
    private Task _worker = Task.CompletedTask;
    private int _failures;
    internal PluginInstance Owner { get; }
    internal long Generation { get; }
    internal int OverflowCount { get; private set; }
    internal int PendingCount { get { lock (_gate) return _pending.Count; } }
    internal Task Completion { get { lock (_gate) return _worker; } }

    internal PluginInteractionQueue(PluginInteractionGroup group, PluginCallCoordinator calls, int capacity, Func<bool> isCurrent)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        Owner = group.Owner;
        Generation = group.Generation;
        _calls = calls;
        _capacity = capacity;
        _isCurrent = isCurrent;
    }

    internal bool Enqueue(InteractionEvent input, PluginInteractionGroup group)
    {
        bool overflow = false;
        lock (_gate)
        {
            if (_stopped) return false;
            if (input.Kind == InteractionEventKind.SelectionChanged && _pending.Last is { } last &&
                last.Value.Event.Kind == InteractionEventKind.SelectionChanged &&
                last.Value.Event.SessionId == input.SessionId && ReferenceEquals(last.Value.Group, group))
            {
                last.Value = new Delivery(input, group);
                return true;
            }
            if (_pending.Count == _capacity)
            {
                var replaceable = _pending.First;
                while (replaceable != null && replaceable.Value.Event.Kind != InteractionEventKind.SelectionChanged)
                    replaceable = replaceable.Next;
                if (replaceable != null) _pending.Remove(replaceable);
                else
                {
                    _stopped = true;
                    _pending.Clear();
                    OverflowCount++;
                    overflow = true;
                }
            }
            if (!overflow)
            {
                _pending.AddLast(new Delivery(input, group));
                if (!_running)
                {
                    _running = true;
                    _worker = Task.Run(DrainAsync);
                }
                return true;
            }
        }
        // CancelAsync schedules plugin token callbacks off the publishing thread.
        _ = CancelAndReleaseAsync();
        _ = Task.Run(() => AppLogger.LogWarn($"[plugin:{Owner.PluginId}] interaction-event queue overflow; routing suspended for generation {Generation}."));
        return false;
    }

    internal void Stop()
    {
        lock (_gate)
        {
            if (_stopped) return;
            _stopped = true;
            _pending.Clear();
        }
        _ = CancelAndReleaseAsync();
    }

    private async Task CancelAndReleaseAsync()
    {
        try { await _cancel.CancelAsync().ConfigureAwait(false); } catch { }
        Task worker;
        lock (_gate) worker = _worker;
        try { await worker.ConfigureAwait(false); } catch { }
        _cancel.Dispose();
    }

    private async Task DrainAsync()
    {
        while (true)
        {
            Delivery delivery;
            lock (_gate)
            {
                if (_pending.First == null)
                {
                    _running = false;
                    return;
                }
                delivery = _pending.First.Value;
                _pending.RemoveFirst();
            }
            foreach (PluginInteractionRegistration registration in delivery.Group.Registrations)
            {
                // 投递组已按事件类型匹配；仍须检查撤销、实例和代际并取得租约。
                // Stop must close the queue gate and start lease acquisition atomically.
                IInteractionContribution? contribution;
                PluginInvocationLease? lease;
                lock (_gate)
                {
                    if (_stopped || !_isCurrent()) break;
                    if (!registration.TryBegin(_calls, out contribution, out lease)) continue;
                }
                using (lease)
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(lease!.CancellationToken, _cancel.Token))
                {
                    try { await contribution!.OnInteractionAsync(delivery.Event, linked.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
                    catch (Exception ex)
                    {
                        if (Interlocked.Increment(ref _failures) == 1)
                            AppLogger.LogError($"[plugin:{Owner.PluginId}] interaction-event callback failed (isolated).", ex);
                    }
                }
            }
        }
    }
}
