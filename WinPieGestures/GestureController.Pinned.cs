using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace WinPieGestures;

public partial class GestureController
{
    private readonly PinnedWheelState _pinState = new();
    private TriggerConfig? _pinTrigger;
    private TargetWindowInfo? _pinTarget;
    private long _pinClickVersion = -1;
    private long _pinPresentationVersion;
    private PinnedWheelMonitor? _pinMonitor;
    private int _pinExpandedParent = -1;
    internal Func<Point, (bool Hit, int Sector, int SubSector)>? TestPinnedHitTest { get; set; }

    private void BeginPinnedPresentation(long version)
    {
        _pinTrigger = _activeTrigger?.Clone();
        _pinTarget = _currentWheelFocusSession?.TargetInfo ?? _keyboardMatchingTarget;
        _pinTarget ??= WheelFocusSwitcher.ResolveTarget(_startPoint);
        if (_pinTarget.Hwnd == IntPtr.Zero || !WheelFocusSwitcher.IsTargetValid(_pinTarget))
        {
            CancelGestureTracking("InvalidPinnedTarget");
            return;
        }
        // Keyboard and mouse confirmations share the same guarded execution path.
        _currentWheelFocusSession ??= new WheelFocusSession(_pinTarget, _startPoint);
        _pinPresentationVersion = version;
        _pinState.Present();
        _pinMonitor?.Dispose();
        _pinMonitor = new PinnedWheelMonitor(_pinTarget.Hwnd, () =>
        {
            if (_pinState.Presented && IsCurrentGesture(version)) CancelGestureTracking("PinnedTargetChanged");
        });
    }

    private void EndPinnedTracking()
    {
        var originalTrigger = _pinTrigger ?? _activeTrigger;
        string? heldMouse = originalTrigger?.TriggerType == "Mouse" ? originalTrigger.MouseButton : null;
        if (!_pinState.EndWithMouseRelease(heldMouse)) return;
        _pinClickVersion = -1;
        _pinExpandedParent = -1;
        _pinTrigger = null;
        _pinTarget = null;
        var monitor = _pinMonitor;
        _pinMonitor = null;
        if (monitor != null) DispatchUi(monitor.Dispose);
    }

    private void PinnedConfigurationChanged()
    {
        if (!_pinState.Enabled) return;
        long version = GetCurrentGestureVersion();
        DispatchUi(() =>
        {
            if (_pinState.Enabled && IsCurrentGesture(version)) CancelGestureTracking("PinnedConfigurationChanged");
        });
    }

    private bool HandlePinnedKeyDown(GlobalKeyEventArgs e)
    {
        if (_pinState.OwnsKeyUp(e.VkCode)) { e.Handled = true; return true; }
        if (!_pinState.Enabled || !_isGestureActive) return false;
        if (e.VkCode == 27)
        {
            _pinState.OwnKeyUp(e.VkCode);
            CancelGestureTracking("PinnedEscape");
            e.Handled = true;
            return true;
        }
        var trigger = _pinTrigger ?? _activeTrigger;
        if (trigger?.TriggerType != "Keyboard" || !MatchesPinnedTrigger(trigger, ProfileTriggerSignature.ForKeyboard(e.VkCode, e.Modifiers))) return false;
        if (_pinState.TriggerReleased)
        {
            _pinState.OwnKeyUp(e.VkCode);
            CancelGestureTracking("PinnedTriggerAgain");
        }
        e.Handled = true; // Auto-repeat before the first physical release is not a second invocation.
        return true;
    }

    private bool HandlePinnedMouse(RawMouseEventArgs e)
    {
        var original = _pinTrigger ?? _activeTrigger;
        var release = !e.IsButtonDown ? _pinState.TakeMouseUp(e.MouseButton,
            original?.TriggerType == "Mouse" ? original.MouseButton : null, _isGestureActive) : PinnedMouseRelease.None;
        if (release != PinnedMouseRelease.None)
        {
            _mouseHook.ForgetActiveTrigger(e.MouseButton);
            if (release == PinnedMouseRelease.Original)
            {
                _mouseTriggerDown = false;
                CancelLongPressTimer();
                e.Handled = true;
                return true;
            }
            long version = e.MouseButton == "LeftButton" ? System.Threading.Interlocked.Exchange(ref _pinClickVersion, -1) : -1;
            Point position = e.Position; // Hook args are reused, never retain them in a callback.
            if (e.MouseButton == "LeftButton" && version >= 0)
                DispatchUi(() => ConfirmPinnedSelection(position, version));
            e.Handled = true;
            return true;
        }
        if (!_pinState.Presented || !e.IsButtonDown) return false;
        _pinState.OwnMouseUp(e.MouseButton);
        e.Handled = true;
        var trigger = _pinTrigger;
        if (trigger?.TriggerType == "Mouse" && e.MouseButton != "LeftButton" &&
            MatchesPinnedTrigger(trigger, ProfileTriggerSignature.ForMouse(e.MouseButton, KeyboardHook.GetCurrentModifiers())) && _pinState.TriggerReleased)
        {
            CancelGestureTracking("PinnedTriggerAgain");
            return true;
        }
        Point positionAtDown = e.Position;
        if (IsOutsidePinnedWheel(positionAtDown))
        {
            CancelGestureTracking("PinnedOutsideClick");
            return true;
        }
        if (e.MouseButton == "LeftButton") _pinClickVersion = GetCurrentGestureVersion();
        return true;
    }

    private bool IsOutsidePinnedWheel(Point point)
    {
        double dx = (point.X - _startPoint.X) / _currentDpiScaleX;
        double dy = (point.Y - _startPoint.Y) / _currentDpiScaleY;
        double distance = Math.Sqrt(dx * dx + dy * dy);
        var cfg = ConfigManager.CurrentConfig;
        double radius = cfg.WheelRadius;
        if (_lastShowSubTier)
        {
            if (cfg.SubmenuStyle == "Fan")
            {
                int count = _activeProfile?.GetEffectiveAction(_selectedSectorIndex)?.SubActions?.Count ?? 0;
                if (count > 0 && HitTestFanSubs(point, _startPoint, _selectedSectorIndex, count) >= 0) return false;
            }
            else radius = Math.Max(radius, cfg.SubWheelOuterRadius);
        }
        return distance > radius;
    }

    private bool MatchesPinnedTrigger(TriggerConfig trigger, ProfileTriggerSignature signature) =>
        _activeActivation?.IsDedicated == true ? _activeActivation.Signature == signature :
        ProfileTriggerRouter.MatchesLegacy(trigger, signature);

    private bool DrainPinnedKey(uint key, bool down) => down ? _pinState.OwnsKeyUp(key) : _pinState.ConsumeKeyUp(key);

    private void ConfirmPinnedSelection(Point point, long version)
    {
        if (!_pinState.Presented || !IsCurrentGesture(version)) return;
        if (_mouseHook.IsPaused || _activeActivation?.IsLayerAvailable == false || _pinTarget == null ||
            !WheelFocusSwitcher.IsTargetValid(_pinTarget) || WheelFocusSwitcher.GetForegroundWindowSafe() != _pinTarget.Hwnd)
        {
            CancelGestureTracking("PinnedTargetInvalid");
            return;
        }
        if (IsOutsidePinnedWheel(point)) { CancelGestureTracking("PinnedOutsideClick"); return; }
        ProcessMove(point);
        var testHit = TestPinnedHitTest?.Invoke(point);
        int hitSector = testHit?.Sector ?? -1, hitSubSector = testHit?.SubSector ?? -1;
        bool hit = testHit?.Hit ?? (_radialWindow != null && _radialWindow.TryHitPinnedWheel(point, out hitSector, out hitSubSector));
        if (!hit)
        {
            CancelGestureTracking("PinnedOutsideGeometry");
            return;
        }
        _selectedSectorIndex = hitSector;
        _selectedSubSectorIndex = hitSubSector;
        if (_selectedSectorIndex >= 0 && _selectedSubSectorIndex < 0 && ConfigManager.CurrentConfig.EnableMultiTier &&
            _activeProfile?.GetEffectiveAction(_selectedSectorIndex)?.SubActions?.Count > 0)
        {
            _pinExpandedParent = _selectedSectorIndex;
            QueueHighlightUpdate(_selectedSectorIndex, -1, false, true, version);
            return;
        }
        if (!TryResolveReleasedAction(_activeProfile, CaptureReleaseLayer(_activeProfile), _selectedSectorIndex,
            _selectedSubSectorIndex, false, false, out var action) || action == null ||
            string.Equals(action.Type, "None", StringComparison.OrdinalIgnoreCase) ||
            (action.Type == "Hotkey" && string.IsNullOrWhiteSpace(action.Parameter))) return;
        // CompleteMouseTriggerRelease owns hide-before-enqueue, target checks, interaction completion and layer snapshot.
        // An explicit hit on rendered geometry is not a hover-locked release from another parent.
        _submenuLock.Reset();
        _suppressPrimaryOnRelease = false;
        _mouseTriggerDown = true;
        CompleteMouseTriggerRelease(point, _pinTrigger?.MouseButton ?? "RightButton", _activeActivation);
    }
}

// Installed only while a pinned wheel is visible; event-driven, no timer or process polling.
internal sealed class PinnedWheelMonitor : IDisposable
{
    private delegate void WinEventProc(nint hook, uint evt, nint hwnd, int objectId, int childId, uint threadId, uint time);
    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
    private readonly WinEventProc _callback;
    private nint _foreground, _destroy;
    public PinnedWheelMonitor(nint target, Action changed)
    {
        _callback = (_, evt, hwnd, objectId, _, _, _) =>
        {
            if ((evt == 3 && hwnd != target) || (evt == 0x8001 && hwnd == target && objectId == 0)) changed();
        };
        _foreground = SetWinEventHook(3, 3, 0, _callback, 0, 0, 0);
        _destroy = SetWinEventHook(0x8001, 0x8001, 0, _callback, 0, 0, 0);
        if (_foreground == 0 || _destroy == 0) { Dispose(); throw new InvalidOperationException("Pinned wheel monitoring unavailable."); }
    }
    public void Dispose()
    {
        if (_foreground != 0) { UnhookWinEvent(_foreground); _foreground = 0; }
        if (_destroy != 0) { UnhookWinEvent(_destroy); _destroy = 0; }
    }
}
