using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace WinPieGestures;

public partial class GestureController
{
    private sealed record PinnedArrowFrame(RadialWindow Window, Dispatcher Dispatcher, long Gesture, long LayerRequest,
        long PortalEpoch, long Revision, nint Target, DeepPage? Page, PinnedArrowSlot[] Slots,
        PinnedNavBindings Bindings, uint TriggerKey, uint LayerKey);

    private sealed record PinnedArrowFocus(long Gesture, long LayerRequest, long PortalEpoch, long PointerVersion,
        DeepPage? Page, PinnedArrowAddress Address);

    private volatile PinnedArrowFrame? _pinnedArrowFrame;
    private PinnedArrowFocus? _pinnedArrowFocus;
    private long _pinnedArrowPointerVersion;

    private void PublishPinnedArrows(RadialWindow window, long gesture, long revision, PinnedArrowSlot[] slots)
    {
        if (slots.Length == 0)
        {
            var old = _pinnedArrowFrame;
            if (old?.Gesture == gesture && ReferenceEquals(old.Window, window)) _pinnedArrowFrame = null;
            return;
        }
        if (!_pinState.Presented || !_isGestureActive || revision != ConfigManager.ConfigurationRevision ||
            gesture != Interlocked.Read(ref _gestureVersion) || _switchingFixedLayer ||
            !ReferenceEquals(window, _radialWindow) || window.PresentationVersion != gesture ||
            _pinTarget is not { } target) return;

        uint triggerKey = _pinTrigger is { TriggerType: "Keyboard", RequireCtrl: false, RequireShift: false,
            RequireAlt: false, RequireWin: false } trigger ? trigger.VkCode : 0;
        var config = ConfigManager.CurrentConfig;
        uint layerKey = _activeProfile?.Layers.Count > 1
            ? string.Equals(config.LayerSwitchTrigger, "Tab", StringComparison.OrdinalIgnoreCase) ? 9u
            : string.Equals(config.LayerSwitchTrigger, "CustomKey", StringComparison.OrdinalIgnoreCase) ? config.LayerSwitchVkCode : 0u
            : 0u;
        long portalEpoch = Interlocked.Read(ref _portalVisualEpoch);
        var frame = new PinnedArrowFrame(window, window.Dispatcher, gesture, Interlocked.Read(ref _layerSwitchSequence),
            portalEpoch, revision, target.Hwnd, _deepPage, slots, PinnedNavBindings.FromConfig(config), triggerKey, layerKey);
        _pinnedArrowFrame = frame;
        var focus = GetPinnedArrowFocus(frame, Interlocked.Read(ref _pinnedArrowPointerVersion));
        window.SetPinnedArrowSelection(PinnedArrowNavigation.IndexOf(slots, focus) >= 0 ? focus : null);
    }

    private PinnedArrowAddress? GetPinnedArrowFocus(PinnedArrowFrame frame, long pointerVersion)
    {
        var focus = Volatile.Read(ref _pinnedArrowFocus);
        return focus?.Gesture == frame.Gesture && focus.LayerRequest == frame.LayerRequest &&
            focus.PortalEpoch == frame.PortalEpoch && focus.PointerVersion == pointerVersion &&
            ReferenceEquals(focus.Page, frame.Page) ? focus.Address : null;
    }

    private bool IsCurrentPinnedArrowFrame(PinnedArrowFrame frame) =>
        ReferenceEquals(frame, _pinnedArrowFrame) && _pinState.Presented && _pinState.TriggerReleased &&
        !_switchingFixedLayer && frame.Gesture == Interlocked.Read(ref _gestureVersion) &&
        frame.LayerRequest == Interlocked.Read(ref _layerSwitchSequence) &&
        frame.PortalEpoch == Interlocked.Read(ref _portalVisualEpoch) &&
        frame.Revision == ConfigManager.ConfigurationRevision &&
        ReferenceEquals(frame.Page, _deepPage);

    private bool QueuePinnedArrow(uint key, ModifierKeys modifiers)
    {
        var frame = _pinnedArrowFrame;
        if (frame == null) return false;
        var action = frame.Bindings.Match(key);
        if (action == PinnedNavAction.None || key == frame.TriggerKey || key == frame.LayerKey ||
            modifiers != ModifierKeys.None || !IsCurrentPinnedArrowFrame(frame) || _mouseHook.IsPaused ||
            ProfileTriggerRouter.IsRecording || WheelFocusSwitcher.GetForegroundWindowSafe() != frame.Target) return false;

        long pointerVersion = Interlocked.Read(ref _pinnedArrowPointerVersion);
        if (action == PinnedNavAction.Confirm)
        {
            frame.Dispatcher.BeginInvoke(DispatcherPriority.Input, (Action)(() => ConfirmPinnedArrow(frame, pointerVersion)));
            return true;
        }
        uint dirKey = frame.Bindings.DirectionVkFor(action);
        frame.Dispatcher.BeginInvoke(DispatcherPriority.Input, (Action)(() => MovePinnedArrow(frame, dirKey, pointerVersion)));
        return true;
    }

    private void MovePinnedArrow(PinnedArrowFrame frame, uint key, long pointerVersion)
    {
        if (!IsCurrentPinnedArrowFrame(frame) || !_isGestureActive || _disposed ||
            pointerVersion != Interlocked.Read(ref _pinnedArrowPointerVersion) ||
            _mouseHook.IsPaused || _keyboardHook?.IsPaused == true || ProfileTriggerRouter.IsRecording ||
            !frame.Window.MainGrid.IsVisible || frame.Window.MainGrid.Opacity <= 0 ||
            WheelFocusSwitcher.GetForegroundWindowSafe() != frame.Target) return;

        var previous = GetPinnedArrowFocus(frame, pointerVersion) ?? frame.Window.PinnedArrowPointerSelection;
        int current = PinnedArrowNavigation.IndexOf(frame.Slots, previous);
        int index = PinnedArrowNavigation.Next(frame.Slots, current, key);
        if (index < 0) return;

        var focus = new PinnedArrowFocus(frame.Gesture, frame.LayerRequest, frame.PortalEpoch,
            pointerVersion, frame.Page, frame.Slots[index].Address);
        Volatile.Write(ref _pinnedArrowFocus, focus);
        if (!IsCurrentPinnedArrowFrame(frame) || pointerVersion != Interlocked.Read(ref _pinnedArrowPointerVersion))
        {
            Interlocked.CompareExchange(ref _pinnedArrowFocus, null, focus);
            return;
        }
        // Presentation updates only; pointer release geometry stays untouched until Confirm or left-click.
        frame.Window.SetPinnedArrowSelection(focus.Address);
    }

    private void ConfirmPinnedArrow(PinnedArrowFrame frame, long pointerVersion)
    {
        if (!IsCurrentPinnedArrowFrame(frame) || !_isGestureActive || _disposed ||
            pointerVersion != Interlocked.Read(ref _pinnedArrowPointerVersion) ||
            _mouseHook.IsPaused || _keyboardHook?.IsPaused == true || ProfileTriggerRouter.IsRecording ||
            _activeActivation?.IsLayerAvailable == false ||
            !frame.Window.MainGrid.IsVisible || frame.Window.MainGrid.Opacity <= 0 ||
            _pinTarget == null || !WheelFocusSwitcher.IsTargetValid(_pinTarget) ||
            WheelFocusSwitcher.GetForegroundWindowSafe() != frame.Target) return;

        var selected = GetPinnedArrowFocus(frame, pointerVersion) ?? frame.Window.PinnedArrowPointerSelection;
        int slotIdx = PinnedArrowNavigation.IndexOf(frame.Slots, selected);
        if (slotIdx < 0) return;

        var slot = frame.Slots[slotIdx];
        var address = slot.Address;
        Point center = frame.Window.ActualPhysicalCenter;
        var dpi = _portalScreen?.DpiScale ?? ScreenHelper.GetScreenContextAtPoint(center).DpiScale;
        Point slotPhysical = PortalPlacement.Physical(center, (Vector)slot.Center, dpi);
        string triggerButton = _pinTrigger?.TriggerType == "Mouse" ? (_pinTrigger.MouseButton ?? "RightButton") : "";

        // 1. Deep Menu Page
        if (frame.Page is { } page)
        {
            if (!page.IsCurrent(_activeProfile))
            {
                CancelGestureTracking("MenuSourceChanged");
                return;
            }
            int index = address.Index;
            if ((uint)index >= (uint)page.Frame.Slots.Count) return;
            var child = page.Child(index);
            if (child?.IsWheelPortal == true && _portalStack != null && PortalFrameIsCurrent())
            {
                var dpSlot = page.Frame.Slots[index];
                Vector anchor = dpSlot.Label != default ? (Vector)dpSlot.Label : (Vector)slot.Center;
                double outer = anchor == default ? page.Frame.OuterDistance : anchor.Length + 12;
                var portalHit = new PortalHit(new(page.Path.Length + 1, index, page.Path), child, anchor, outer,
                    page.Path[0], page.Path[1], false, page.IsInheritedBranch);
                OpenPortal(portalHit, slotPhysical, released: true);
                return;
            }
            if (page.CanEnter(index))
            {
                NavigateDeepPage(frame.Window, page, index, slotPhysical);
                return;
            }
            if (!IsDeepMenuLeafAvailable(child)) return;

            lock (_uiUpdateSync)
            {
                if (!IsCurrentPinnedArrowFrame(frame) || !_isGestureActive || !ReferenceEquals(frame.Page, _deepPage)) return;
                _lastEscapedState = false;
                _selectedSectorIndex = page.Path[0];
                _selectedSubSectorIndex = page.Path[1];
                _submenuLock.Reset();
                _suppressPrimaryOnRelease = false;
                _pinnedKeyboardRelease = new DeepRelease(page, index, false);
                _currentWheelFocusSession ??= _portalStack?.Focus ?? new WheelFocusSession(_pinTarget, center);
                _mouseTriggerDown = true;
                CompleteMouseTriggerRelease(slotPhysical, triggerButton, _activeActivation, portalRelease: true);
            }
            return;
        }

        // 2. Fixed Outer Ring (Levels 3-5)
        if (address.Level >= 3)
        {
            var fixedSession = _fixedSession;
            if (fixedSession == null || !fixedSession.Presented || !fixedSession.IsCurrent(_activeProfile)) return;
            var hit = new RingHit(address.Level, address.Index);
            if (!fixedSession.WasVisible(hit) ||
                (uint)(hit.Level - 1) >= (uint)fixedSession.Rows.Length ||
                (uint)hit.Index >= (uint)fixedSession.Rows[hit.Level - 1].Length) return;
            var display = fixedSession.Rows[hit.Level - 1][hit.Index].Display;
            if (display?.IsWheelPortal == true && _portalStack != null && PortalFrameIsCurrent())
            {
                int count = fixedSession.Rows[hit.Level - 1].Length;
                Vector anchor = (Vector)fixedSession.Geometry.GetCenter(hit.Level, hit.Index, count);
                double outer = fixedSession.Geometry.IsHoneycomb
                    ? anchor.Length + fixedSession.Rows[hit.Level - 1][hit.Index].Shape.Bounds.Height / 2
                    : fixedSession.Geometry.OuterForCount(hit.Level, count);
                var portalHit = new PortalHit(new(hit.Level, hit.Index, null), display, anchor, outer);
                OpenPortal(portalHit, slotPhysical, released: true);
                return;
            }
            if (!fixedSession.Resolve(hit, out var fixedAction) || fixedAction == null) return;

            lock (_uiUpdateSync)
            {
                if (!IsCurrentPinnedArrowFrame(frame) || !_isGestureActive || !ReferenceEquals(_fixedSession, fixedSession)) return;
                _lastEscapedState = false;
                _selectedSectorIndex = -2;
                _selectedSubSectorIndex = -1;
                _submenuLock.Reset();
                _suppressPrimaryOnRelease = false;
                _pinnedKeyboardRelease = new DeepRelease(null, -1, false) { Fixed = new FixedRelease(fixedSession, hit) };
                _currentWheelFocusSession ??= _portalStack?.Focus ?? new WheelFocusSession(_pinTarget, center);
                _mouseTriggerDown = true;
                CompleteMouseTriggerRelease(slotPhysical, triggerButton, _activeActivation, portalRelease: true);
            }
            return;
        }

        // 3. Secondary Sector (Level 2) or Primary Sector (Level 1)
        int sector = address.Level == 2 ? address.Owner : address.Index;
        int sub = address.Level == 2 ? address.Index : -1;
        if (sector < 0) return;

        var profile = _activeProfile;
        var layer = CaptureReleaseLayer(profile);
        var root = InputRoot(sector) ?? profile?.GetEffectiveAction(sector);
        var children = root?.InlineSubActions;
        ActionItem? slotAction = sub < 0 ? root : (children != null && (uint)sub < (uint)children.Count ? children[sub] : null);

        if (slotAction?.IsWheelPortal == true && _portalStack != null && PortalFrameIsCurrent())
        {
            Vector anchor = frame.Window.PortalLegacyAnchor(sector, sub);
            int portalIndex = sub < 0 ? sector : FixedRingMenu.SecondaryKey(sector, sub);
            var portalHit = new PortalHit(new(sub < 0 ? 1 : 2, portalIndex, null), slotAction, anchor,
                frame.Window.PortalLegacyOuter(anchor, sub), sector, sub,
                slotAction.InlineSubActions is { Count: > 0 } && ConfigManager.CurrentConfig.EnableMultiTier,
                root?.IsInherited == true);
            OpenPortal(portalHit, slotPhysical, released: true);
            return;
        }

        if (sub >= 0 && TryEnterSecondLevelBranch(frame.Window, sector, sub, slotPhysical, frame.Gesture))
            return;

        lock (_uiUpdateSync)
        {
            if (!IsCurrentPinnedArrowFrame(frame) || !_isGestureActive || !ReferenceEquals(_activeProfile, profile)) return;
            var activeFixed = _fixedSession;
            bool canExecute;
            DeepRelease keyboardRelease;
            if (activeFixed != null && activeFixed.Presented && activeFixed.IsCurrent(profile))
            {
                var ringHit = sub >= 0
                    ? new RingHit(2, FixedRingMenu.SecondaryKey(sector, sub))
                    : new RingHit(1, sector);
                canExecute = activeFixed.Resolve(ringHit, out var resolved) && resolved != null;
                keyboardRelease = new DeepRelease(null, -1, false) { Fixed = new FixedRelease(activeFixed, ringHit) };
            }
            else
            {
                canExecute = TryResolveReleasedAction(profile, layer, sector, sub, false, false, out var resolved)
                    && IsExecutablePinnedAction(resolved);
                keyboardRelease = new DeepRelease(null, -1, false);
            }

            if (canExecute)
            {
                _selectedSectorIndex = sector;
                _selectedSubSectorIndex = sub;
                _lastEscapedState = false;
                _submenuLock.Reset();
                _suppressPrimaryOnRelease = false;
                _pinnedKeyboardRelease = keyboardRelease;
                _currentWheelFocusSession ??= _portalStack?.Focus ?? new WheelFocusSession(_pinTarget, center);
                _mouseTriggerDown = true;
                CompleteMouseTriggerRelease(slotPhysical, triggerButton, _activeActivation, portalRelease: true);
                return;
            }

            // Actionless parent with sub-actions: Enter expands the 2nd-level cascade so arrows can step into it.
            if (sub < 0 && ConfigManager.CurrentConfig.EnableMultiTier &&
                _activeProfile?.GetEffectiveAction(sector)?.InlineSubActions?.Count > 0)
            {
                _pinExpandedParent = sector;
                QueueHighlightUpdate(sector, -1, false, true, frame.Gesture);
            }
        }
    }

    private void RelinquishPinnedArrowSelection()
    {
        if (!_pinState.Presented) return;
        Interlocked.Increment(ref _pinnedArrowPointerVersion);
        if (Interlocked.Exchange(ref _pinnedArrowFocus, null) == null || _pinnedArrowFrame is not { } frame) return;
        frame.Dispatcher.BeginInvoke(DispatcherPriority.Render, (Action)(() =>
        {
            if (IsCurrentPinnedArrowFrame(frame) && Volatile.Read(ref _pinnedArrowFocus) == null)
                frame.Window.SetPinnedArrowSelection(null);
        }));
    }

    private void ClearPinnedArrowSession()
    {
        _pinnedArrowFrame = null;
        Interlocked.Exchange(ref _pinnedArrowFocus, null);
    }
}
