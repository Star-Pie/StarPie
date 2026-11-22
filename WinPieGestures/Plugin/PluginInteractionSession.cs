using StarPie.Plugin;

namespace WinPieGestures.Plugins;

/// <summary>两个轮盘源共用的语义会话；独立于音效 ID，UI 先关闭仍允许合法终结反馈。</summary>
internal sealed class PluginInteractionSession : IDisposable
{
    private static long _nextId;
    private readonly object _gate = new();
    private readonly Func<InteractionEvent, int> _publish;
    private readonly InteractionSource _source;
    private string _profileId;
    private long _sequence;
    private bool _presented;
    private bool _ended;
    private InteractionEvent? _pendingEnd;
    private bool _expanded;
    private int _expandedSector = -1;
    private bool _escaped;
    private bool _terminal;
    private InteractionTarget _target = InteractionTarget.None;
    private string _outcome = "Closed";
    internal long SessionId { get; } = Interlocked.Increment(ref _nextId);

    internal PluginInteractionSession(InteractionSource source, string profileId,
        Func<InteractionEvent, int>? publish = null)
    { _source = source; _profileId = profileId; _publish = publish ?? PluginHost.PublishInteractionEvent; }

    internal void BindProfile(string profileId)
    {
        lock (_gate)
            if (!_ended && _sequence == 0) _profileId = profileId;
    }
    internal static InteractionTarget Target(int sector, int sub, bool escaped = false) => escaped
        ? InteractionTarget.None
        : sector == -1 ? InteractionTarget.Core
        : sector < 0 ? InteractionTarget.None
        : sub >= 0 ? new(InteractionTargetKind.SubSector, sector, sub)
        : new(InteractionTargetKind.Sector, sector);

    internal void Presented()
    {
        lock (_gate)
        {
            if (_ended || _presented) return;
            _presented = true;
            Emit(InteractionEventKind.Presented);
            Emit(InteractionEventKind.SelectionChanged);
            if (_expanded) Emit(InteractionEventKind.SubmenuExpanded);
        }
    }

    internal void Update(int sector, int sub, bool expanded, bool escaped, bool preservePrimaryTarget = false)
    {
        lock (_gate)
        {
            if (_ended || _terminal) return;
            var target = expanded && sub < 0 && (!preservePrimaryTarget || sector < 0)
                ? Target(sector < 0 ? sector : -2, -1, escaped)
                : Target(sector, sub, escaped);
            int nextExpandedSector = expanded
                ? (sector >= 0 ? sector : _expandedSector)
                : (preservePrimaryTarget && escaped ? _expandedSector : -1);
            bool shouldEmitExpand = expanded &&
                ((!_expanded && (_expandedSector < 0 || nextExpandedSector != _expandedSector)) ||
                 (_expanded && nextExpandedSector >= 0 && _expandedSector >= 0 && nextExpandedSector != _expandedSector));
            if (_presented)
            {
                if ((!_escaped && escaped) || (_expanded && !expanded))
                    Emit(InteractionEventKind.NavigationCancelled, escaped ? "Escaped" : "SubmenuCollapsed", target);
                if (shouldEmitExpand) Emit(InteractionEventKind.SubmenuExpanded, target: target);
                if (_target != target) Emit(InteractionEventKind.SelectionChanged, target: target);
            }
            _target = target;
            _expanded = expanded;
            _expandedSector = nextExpandedSector;
            _escaped = escaped;
        }
    }

    internal static bool IsExecutableAction(ActionItem? action)
        => IsExecutableAction(action, allowWhitespaceText: false);

    internal static bool IsExecutableAction(ActionItem? action, bool allowWhitespaceText)
    {
        if (action == null || !WheelProfile.IsActionConfigured(action)) return false;
        if (string.Equals(action.Type, "Hotkey", StringComparison.OrdinalIgnoreCase))
            return !string.IsNullOrWhiteSpace(action.Parameter);
        if (string.Equals(action.Type, "Text", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(action.Type, "String", StringComparison.OrdinalIgnoreCase))
            return allowWhitespaceText ? !string.IsNullOrEmpty(action.Parameter) : !string.IsNullOrWhiteSpace(action.Parameter);
        if (string.Equals(action.Type, "Plugin", StringComparison.OrdinalIgnoreCase))
            return action.PluginActionRef?.IsValid == true;
        return true;
    }

    internal void Confirm(ActionItem? action, InteractionTarget target, string cancelReason = "NoAction")
    {
        if (!IsExecutableAction(action) || target.Kind == InteractionTargetKind.None) Cancel(cancelReason);
        else Commit(target);
    }
    internal void Commit(InteractionTarget target)
    {
        lock (_gate)
        {
            if (_ended || _terminal) return;
            if (target.Kind == InteractionTargetKind.None) { Cancel("NoTarget"); return; }
            _target = target;
            _terminal = true;
            _outcome = "ActionCommitted";
            Emit(InteractionEventKind.ActionCommitted);
        }
    }

    internal void Cancel(string reason)
    {
        lock (_gate)
        {
            if (_ended || _terminal) return;
            _terminal = true;
            _outcome = reason;
            Emit(InteractionEventKind.SessionCancelled, reason);
        }
    }

    // 外层会话锁内只冻结，不调用发布器，避免撤销与旧 Present 守卫竞争终结原因。
    internal void FreezeEnd(string reason)
    {
        lock (_gate)
        {
            if (_ended) return;
            _ended = true;
            _pendingEnd = CreateEvent(InteractionEventKind.SessionEnded, _terminal ? _outcome : reason);
        }
    }

    internal void End(string reason = "Closed")
    {
        FreezeEnd(reason);
        InteractionEvent? ending;
        lock (_gate)
        {
            ending = _pendingEnd;
            _pendingEnd = null;
        }
        if (ending != null) _publish(ending);
    }

    private InteractionEvent CreateEvent(InteractionEventKind kind, string reason = "", InteractionTarget? target = null) =>
        new(kind, SessionId, ++_sequence, _source, _profileId, target ?? _target, reason);

    private void Emit(InteractionEventKind kind, string reason = "", InteractionTarget? target = null) =>
        _publish(CreateEvent(kind, reason, target));

    public void Dispose() => End();
}
