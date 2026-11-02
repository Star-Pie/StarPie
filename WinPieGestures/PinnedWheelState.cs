using System.Collections.Generic;

namespace WinPieGestures;

// Input ownership outlives visibility: an intercepted down always owns its matching up.
internal sealed class PinnedWheelState
{
    private readonly object _sync = new();
    private readonly HashSet<string> _mouseUps = new(System.StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<uint> _keyUps = new();
    private volatile bool _enabled;
    private volatile bool _presented;
    private volatile bool _triggerReleased;
    public bool Enabled => _enabled;
    public bool Presented => _presented;
    public bool TriggerReleased => _triggerReleased;
    public void Begin(bool enabled) { lock (_sync) { _triggerReleased = false; _presented = false; _enabled = enabled; } }
    public void Present() { lock (_sync) { if (_enabled) _presented = true; } }
    public void ReleaseTrigger() { lock (_sync) _triggerReleased = true; }
    public bool End() => EndWithMouseRelease(null);
    public bool EndWithMouseRelease(string? heldButton)
    {
        lock (_sync)
        {
            bool wasEnabled = _enabled;
            if (wasEnabled && !_triggerReleased && heldButton != null) _mouseUps.Add(heldButton);
            _presented = false;
            _enabled = false;
            return wasEnabled;
        }
    }
    public PinnedMouseRelease TakeMouseUp(string button, string? originalButton, bool active)
    {
        lock (_sync)
        {
            if (_mouseUps.Remove(button)) return PinnedMouseRelease.Owned;
            if (_enabled && active && !_triggerReleased && System.StringComparer.OrdinalIgnoreCase.Equals(button, originalButton))
            {
                _triggerReleased = true;
                return PinnedMouseRelease.Original;
            }
            return PinnedMouseRelease.None;
        }
    }
    public void OwnMouseUp(string button) { lock (_sync) _mouseUps.Add(button); }
    public bool ConsumeMouseUp(string button) { lock (_sync) return _mouseUps.Remove(button); }
    public void OwnKeyUp(uint key) { lock (_sync) _keyUps.Add(key); }
    public bool OwnsKeyUp(uint key) { lock (_sync) return _keyUps.Contains(key); }
    public bool ConsumeKeyUp(uint key) { lock (_sync) return _keyUps.Remove(key); }
}

internal enum PinnedMouseRelease { None, Original, Owned }
