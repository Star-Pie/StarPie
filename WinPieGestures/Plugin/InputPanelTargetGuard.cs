using System;

namespace WinPieGestures.Plugins;

/// <summary>WinEvent notifications are hints, not a current foreground snapshot.</summary>
internal sealed class InputPanelTargetGuard
{
    private readonly Func<bool> _isOpen;
    private readonly Func<long> _clock;
    private readonly long _startedAt;
    private readonly Action _close;
    internal InputPanelTargetGuard(long startedAt, Func<long> clock, Func<bool> isOpen, Action close)
    { _startedAt = startedAt; _clock = clock; _isOpen = isOpen; _close = close; }

    internal void OnTargetEvent(uint eventTime)
    {
        if (!_isOpen()) return;
        long now = _clock();
        // WinEvent supplies a 32-bit uptime timestamp. Reconstruct its occurrence
        // against the current 64-bit clock, including the 49-day tick wrap.
        long occurredAt = now - unchecked((uint)now - eventTime);
        if (occurredAt >= _startedAt) _close();
    }
}
