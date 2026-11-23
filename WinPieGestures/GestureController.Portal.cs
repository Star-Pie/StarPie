using System.Windows;
using System.Windows.Media;
using WinPieGestures.Plugins;

namespace WinPieGestures;

public partial class GestureController
{
    private readonly record struct PortalSlotKey(int Level, int Index, int[]? Path);
    private readonly record struct PortalHit(PortalSlotKey Key, ActionItem? Action, Vector Anchor, double Outer,
        int Sector = -2, int Secondary = -1, bool InlineNavigation = false, bool Inherited = false);
    private readonly record struct PortalRelease(Point Point, string Button, ProfileTriggerActivation? Activation, long Frame);
    private sealed record PortalParentView(WheelProfile Profile, PortalSlotKey Slot, int Sector, int Secondary,
        bool Expanded, DeepPage? Page, FixedRingSession? FixedSession);

    private PortalSessionStack? _portalStack;
    private ScreenContext? _portalScreen;
    private PortalParentView?[]? _portalParents;
    private PortalDwell? _portalDwell;
    private PortalHit? _portalHover;
    private PortalSlotKey? _portalFailedSlot;
    private Point _portalPreviousPointer;
    private long _portalDwellElapsed = -1;
    private long _portalVersion;
    private long _portalVisualEpoch;
    private long _portalPressedEpoch;
    private Point _portalPressedPoint;
    private long? _pendingPortalReleaseFrame;
    private volatile bool _portalKeyReleased;
    private volatile bool _portalAcceptInput;
    private volatile int _portalDepth;
    private bool _portalRendering;
    private bool _portalMovePending;
    private Point _portalMovePoint;
    private PortalRelease? _portalRelease;
    private PortalBackInput? _portalBackInput;

    private void PreparePortalRoot(RadialWindow window, WheelProfile profile, long version)
    {
        if (!_isGestureActive || version != _gestureVersion || window.PresentationVersion != version) return;
        bool releasedDuringSwitch;
        lock (_uiUpdateSync) releasedDuringSwitch = _portalRelease.HasValue;
        StopPortalRendering();
        Interlocked.Increment(ref _portalVisualEpoch);
        if (releasedDuringSwitch && _isGestureActive && version == _gestureVersion)
        {
            CancelGestureTracking("PortalLayerSwitchDuringRelease");
            return;
        }
        var layer = CaptureReleaseLayer(profile);
        if (layer == null || !HasPortalActions(profile, layer)) return;
        var config = ConfigManager.CurrentConfig;
        var center = window.ActualPhysicalCenter;
        var focus = _currentWheelFocusSession;
        if (focus == null && _keyboardPanelTarget != 0 && _keyboardPanelTargetPid != 0)
        {
            var target = _keyboardMatchingTarget ?? WheelFocusSwitcher.CreateForegroundTargetSnapshot();
            if ((target == null || target.Hwnd != _keyboardPanelTarget || target.ProcessId != _keyboardPanelTargetPid) &&
                WheelFocusSwitcher.GetForegroundWindowSafe() == _keyboardPanelTarget &&
                ActiveWindowHelper.GetWindowRealProcessId(_keyboardPanelTarget) == _keyboardPanelTargetPid &&
                WheelFocusSwitcher.IsWindow(_keyboardPanelTarget))
            {
                string proc = ActiveWindowHelper.GetProcessNameForWindow(_keyboardPanelTarget);
                target = new TargetWindowInfo(_keyboardPanelTarget, _keyboardPanelTargetPid, proc, isEligibleBackgroundTarget: false);
            }
            if (target?.Hwnd == _keyboardPanelTarget && target.ProcessId == _keyboardPanelTargetPid &&
                WheelFocusSwitcher.IsTargetValid(target))
                focus = new WheelFocusSession(target, center);
        }
        _portalScreen = ScreenHelper.GetScreenContextAtPoint(center);
        _portalPreviousPointer = center;
        _portalStack = new(new(new(profile, layer), center, window.PortalVisualRadius(_fixedSession?.VisibleDepth ?? 2), center,
            window.PortalCoreRadius), ConfigManager.ConfigurationRevision, config.EffectivePortalMaxDepth, focus);
        _portalVersion = version;
        _portalKeyReleased = false;
        _portalParents = new PortalParentView?[config.EffectivePortalMaxDepth - 1];
        _portalDwell ??= new PortalDwell(generation => Interlocked.Exchange(ref _portalDwellElapsed, generation));
        lock (_uiUpdateSync)
        {
            _portalMovePoint = _lastMovePoint;
            _portalMovePending = true;
            _portalDepth = 1;
            _portalAcceptInput = true;
        }
        CompositionTarget.Rendering += RenderPortalInput;
        _portalRendering = true;
    }

    private static bool HasPortalActions(WheelProfile profile, WheelLayer layer)
    {
        int depth = ConfigManager.CurrentConfig.EnableMultiTier ? layer.EffectiveMaxMenuDepth : 1;
        bool fixedRings = depth > 2 && layer.ActiveFixedRings != null;
        int inlineDepth = fixedRings ? 2 : depth;
        foreach (var action in layer.Actions)
            if (HasPortal(action, inlineDepth)) return true;
        if (ConfigManager.CurrentConfig.EnableGlobalInheritance && !string.Equals(profile.ProcessName, "Global", StringComparison.OrdinalIgnoreCase))
        {
            var global = ConfigManager.GetGlobalProfile();
            int layerIndex = profile.ActiveLayerIndex < global.Layers.Count ? profile.ActiveLayerIndex : 0;
            if (global.Layers[layerIndex].Actions.Any(action => HasPortal(action, inlineDepth)))
                for (int sector = 0; sector < layer.SectorCount; sector++)
                    if (HasPortal(profile.GetEffectiveAction(sector), inlineDepth)) return true;
        }
        if (fixedRings)
            for (int level = 3; level <= depth; level++)
                for (int i = 0; i < layer.ActiveFixedRings!.GetCount(level); i++)
                    if (layer.ActiveFixedRings.GetSlot(level, i)?.Action.IsWheelPortal == true) return true;
        return false;

        static bool HasPortal(ActionItem? action, int remaining)
        {
            if (action?.IsWheelPortal == true) return true;
            if (remaining > 1 && action?.InlineSubActions is { } children)
                foreach (var child in children)
                    if (HasPortal(child, remaining - 1)) return true;
            return false;
        }
    }

    private void RequestPortalStop()
    {
        if (!_portalAcceptInput) return;
        _portalAcceptInput = false;
        _portalDepth = 0;
    }

    private void StopPortalRendering()
    {
        RequestPortalStop();
        if (_portalRendering)
        {
            CompositionTarget.Rendering -= RenderPortalInput;
            _portalRendering = false;
        }
        ClearPortalHover();
        _portalFailedSlot = null;
        _portalStack = null;
        _portalParents = null;
        _portalScreen = null;
        lock (_uiUpdateSync)
        {
            _portalMovePending = false;
            _portalRelease = null;
            _portalBackInput = null;
        }
    }

    private bool QueuePortalMove(Point point)
    {
        if (!_portalAcceptInput) return false;
        lock (_uiUpdateSync)
        {
            if (!_portalAcceptInput) return false;
            _portalMovePoint = point;
            _portalMovePending = true;
        }
        return true;
    }

    private bool QueuePortalRelease(Point point, string button, ProfileTriggerActivation? activation, long? frame = null)
    {
        if (!_portalAcceptInput || !_isGestureActive || _pinState.Enabled) return false;
        lock (_uiUpdateSync)
        {
            if (!_portalAcceptInput || _gestureVersion != _portalVersion) return false;
            _portalRelease ??= new(point, button, activation, frame ?? _portalVisualEpoch);
        }
        return true;
    }

    private bool QueuePortalBack(PortalBackInput input)
    {
        if (_portalDepth < 2) return false;
        lock (_uiUpdateSync)
        {
            if (_portalDepth < 2) return false;
            _portalBackInput ??= input;
        }
        return true;
    }

    private bool PortalSessionIsCurrent() => _portalAcceptInput && _isGestureActive &&
        _gestureVersion == _portalVersion && _portalStack?.MatchesRevision(ConfigManager.ConfigurationRevision) == true &&
        !_disposed && !_mouseHook.IsPaused && _activeActivation?.IsLayerAvailable != false &&
        _portalStack?.Focus?.IsCancelled != true;

    private bool PortalFrameIsCurrent() => PortalSessionIsCurrent() &&
        (_switchingFixedLayer || ReferenceEquals(_portalStack?.Current.Address.Layer, CaptureReleaseLayer(_activeProfile)));

    private void RenderPortalInput(object? sender, EventArgs e)
    {
        if (!PortalFrameIsCurrent())
        {
            bool cancel = _isGestureActive && _portalAcceptInput && _portalVersion == _gestureVersion;
            StopPortalRendering();
            if (cancel) CancelGestureTracking("PortalSourceChanged");
            return;
        }
        Point point;
        bool move;
        PortalRelease? release;
        PortalBackInput? back;
        lock (_uiUpdateSync)
        {
            point = _portalMovePoint;
            move = _portalMovePending;
            _portalMovePending = false;
            release = _portalRelease;
            _portalRelease = null;
            back = _portalBackInput;
            _portalBackInput = null;
        }
        try
        {
            if (_switchingFixedLayer)
            {
                if (release.HasValue) CancelGestureTracking("PortalLayerSwitchDuringRelease");
                return;
            }
            if (back is { } input)
                BackFromPortal(input, release.HasValue);
            else if (release is { } up)
                FinishPortalHold(up);
            else
            {
                long elapsed = Interlocked.Exchange(ref _portalDwellElapsed, -1);
                if (move || elapsed != -1)
                {
                    if (!EvaluatePortalHover(point, elapsed) && move) ProcessMoveCore(point);
                }
            }
        }
        catch (Exception exception)
        {
            StopPortalRendering();
            CancelGestureTracking("PortalPresentationFailed");
            AppLogger.LogError("Portal presentation failed", exception);
        }
    }

    private void ClearPortalHover()
    {
        _portalHover = null;
        _portalDwell?.Dispose();
        Interlocked.Exchange(ref _portalDwellElapsed, -1);
    }

    private PortalHit SamplePortalSlot(Point point)
    {
        var window = _radialWindow!;
        if (_deepPage is { } page)
        {
            int child = page.Hit(point, _currentDpiScaleX, _currentDpiScaleY);
            if (child >= 0)
            {
                var slot = page.Frame.Slots[child];
                return new(new(page.Path.Length + 1, child, page.Path), page.Child(child), (Vector)slot.Label,
                    slot.Label.X == 0 && slot.Label.Y == 0 ? page.Frame.OuterDistance : ((Vector)slot.Label).Length + 12,
                    page.Path[0], page.Path[1], page.CanEnter(child), page.IsInheritedBranch);
            }
            return new(default, null, default, 0, child == DeepMenuFrame.Center ? -1 : -2,
                InlineNavigation: child == DeepMenuFrame.Back);
        }
        if (_fixedSession is { Presented: true } fixedSession)
        {
            var hit = fixedSession.Hit(point);
            if (hit.IsOuter && fixedSession.WasVisible(hit))
            {
                var cell = fixedSession.Rows[hit.Level - 1][hit.Index];
                int count = fixedSession.Rows[hit.Level - 1].Length;
                var middle = fixedSession.Geometry.GetCenter(hit.Level, hit.Index, count);
                double outer = fixedSession.Geometry.IsHoneycomb ? ((Vector)middle).Length + cell.Shape.Bounds.Height / 2 :
                    fixedSession.Geometry.OuterForCount(hit.Level, count);
                return new(new(hit.Level, hit.Index, null), cell.Display, (Vector)middle, outer);
            }
        }
        if (!TryHitPinnedPoint(point, out int sector, out int sub, ignoreWindowScale: true)) return new(default, null, default, 0);
        if (sector < 0) return new(default, _portalDepth > 1 ? null : _activeProfile?.GetEffectiveCenterAction(), default, 0, -1);
        var root = InputRoot(sector);
        var children = root?.InlineSubActions;
        var action = sub < 0 ? root : children != null && sub < children.Count ? children[sub] : null;
        var anchor = window.PortalLegacyAnchor(sector, sub);
        int index = sub < 0 ? sector : FixedRingMenu.SecondaryKey(sector, sub);
        return new(new(sub < 0 ? 1 : 2, index, null), action, anchor, window.PortalLegacyOuter(anchor, sub), sector, sub,
            action?.InlineSubActions is { Count: > 0 } && ConfigManager.CurrentConfig.EnableMultiTier, root?.IsInherited == true);
    }

    private bool EvaluatePortalHover(Point point, long elapsed)
    {
        var stack = _portalStack!;
        var screen = _portalScreen!;
        var config = ConfigManager.CurrentConfig;
        double parentBackRadius = stack.Depth > 1 ? Math.Max(config.GetEffectiveCoreDeadzone(), stack.Frames[^2].CoreRadius) : 0;
        double childExtent = stack.Depth > 1
            ? Math.Max(stack.Current.OuterRadius,
                _radialWindow?.PortalVisualRadius(_fixedSession?.VisibleDepth ?? 2, _deepPage?.Frame.OuterDistance ?? 0) ?? 0)
            : 0;
        bool parentCoreClearOfChild = stack.Depth > 1 &&
            PortalPlacement.Radius(stack.Frames[^2].PhysicalCenter, stack.Current.PhysicalCenter, screen.DpiScale) >=
            childExtent + parentBackRadius;
        if (parentCoreClearOfChild && !_pinState.Enabled && !_mouseReleasePending &&
            PortalPlacement.Radius(stack.Frames[^2].PhysicalCenter, point, screen.DpiScale) <=
            parentBackRadius && PortalPlacement.Radius(stack.Current.PhysicalCenter, point, screen.DpiScale) >
            config.GetEffectiveCoreDeadzone())
        {
            BackFromPortal(PortalBackInput.ParentCenter, false);
            return true;
        }
        if (_pinState.Enabled || _mouseReleasePending || _volumeGestureTookOver)
        {
            if (_pinState.Enabled)
            {
                var current = SamplePortalSlot(point);
                stack.AcceptPointerSlot(current.Action?.IsWheelPortal == true ? current.Key : null);
            }
            ClearPortalHover();
            return false;
        }

        var hit = SamplePortalSlot(point);
        Point previousPoint = _portalPreviousPointer;
        _portalPreviousPointer = point;
        double radius = PortalPlacement.Radius(stack.Current.PhysicalCenter, point, screen.DpiScale);
        if (config.PortalEnableCrossExpand && hit.Key.Level == 0 && _portalHover is { } previous &&
            PortalPlacement.Crossed(radius, previous.Outer, config) && previous.Anchor.Length > 0)
        {
            // Re-test the same ray inside the last visible slot. A jump through its outer
            // edge can expand it, whereas a lateral move off the slot cancels the dwell.
            var delta = new Vector((point.X - stack.Current.PhysicalCenter.X) / screen.DpiScale.DpiScaleX,
                (point.Y - stack.Current.PhysicalCenter.Y) / screen.DpiScale.DpiScaleY);
            delta *= previous.Anchor.Length / radius;
            var projected = SamplePortalSlot(PortalPlacement.Physical(stack.Current.PhysicalCenter, delta, screen.DpiScale));
            if (projected.Key == previous.Key) hit = projected;
        }
        if (config.PortalEnableCrossExpand && hit.Key.Level == 0 &&
            PortalPlacement.Crossed(radius, config.WheelRadius, config) &&
            PortalPlacement.Radius(stack.Current.PhysicalCenter, previousPoint, screen.DpiScale) < radius)
        {
            Vector movement = point - previousPoint;
            for (int i = 7; i >= 1; i--)
            {
                var crossed = SamplePortalSlot(previousPoint + movement * (i / 8.0));
                if (crossed.Key.Level == 0) continue;
                if (crossed.Action?.IsWheelPortal == true && PortalPlacement.Crossed(radius, crossed.Outer, config))
                    hit = crossed;
                break;
            }
        }
        bool valid = hit.Action?.IsWheelPortal == true;
        if (_portalFailedSlot is { } failed && (!valid || failed != hit.Key)) _portalFailedSlot = null;
        if (!stack.AcceptPointerSlot(valid ? hit.Key : null) || !valid)
        {
            ClearPortalHover();
            return false;
        }
        if (_portalFailedSlot == hit.Key) return false;
        if (PortalPlacement.Crossed(radius, hit.Outer, config))
            return OpenPortal(hit, point, false);

        bool same = _portalHover?.Key == hit.Key;
        if (config.PortalEnableDwellExpand && elapsed != -1 && same && _portalDwell!.IsCurrent(elapsed))
            return OpenPortal(hit, point, false);
        if (!same)
        {
            ClearPortalHover();
            _portalHover = hit;
            if (config.PortalEnableDwellExpand) _portalDwell!.Arm(config.EffectivePortalDwellMs);
        }
        return false;
    }

    private bool OpenPortal(PortalHit hit, Point pointer, bool released)
    {
        ClearPortalHover();
        if (!PortalFrameIsCurrent() || _portalStack is not { } stack || _portalScreen is not { } screen ||
            _radialWindow is not { } window) return false;
        if (hit.Action?.PortalTarget is not { } target) return Fail("PortalBroken");
        var config = ConfigManager.CurrentConfig;
        var owner = hit.Inherited ? ConfigManager.GetGlobalProfile() : stack.Current.Address.Profile;
        var destination = PortalReferenceAnalyzer.Resolve(config.Profiles, owner, target);
        if (destination is not { } address || !stack.CanPush(address))
        {
            return Fail(destination.HasValue ? "PortalBlocked" : "PortalBroken");
        }
        if (stack.Focus == null || !WheelFocusSwitcher.IsTargetValid(stack.Focus.TargetInfo) ||
            WheelFocusSwitcher.GetForegroundWindowSafe() != stack.Focus.TargetInfo.Hwnd)
        {
            CancelGestureTracking("PortalFocusInvalid");
            return true;
        }

        var view = new WheelProfile {
            ProcessName = address.Profile.ProcessName, DisplayName = address.Profile.DisplayName,
            Layers = address.Profile.Layers, ActiveLayerIndex = address.Profile.Layers.IndexOf(address.Layer)
        };
        view.SyncRootPropertiesFromActiveLayer();
        double parentRadius = window.PortalVisualRadius(_fixedSession?.VisibleDepth ?? 2, _deepPage?.Frame.OuterDistance ?? 0);
        double childRadius = RadialWindow.PortalInitialRadius(view, screen);
        if (!PortalPlacement.TryPlace(stack.Current.PhysicalCenter, hit.Anchor, parentRadius, childRadius,
            config.EffectivePortalOuterTangentGapDip, target.IsOuterOffset, screen.PhysicalWorkArea, screen.DpiScale, out var center))
        {
            return Fail("PortalNoSpace");
        }

        _portalParents![stack.Depth - 1] = new(_activeProfile!, hit.Key,
            hit.Sector >= 0 ? hit.Sector : _selectedSectorIndex, hit.Secondary >= 0 ? hit.Secondary : _selectedSubSectorIndex,
            _lastShowSubTier, _deepPage, _fixedSession);
        int highlightSector = hit.Sector >= 0 ? hit.Sector : _selectedSectorIndex;
        int highlightSub = hit.Secondary >= 0 ? hit.Secondary : _selectedSubSectorIndex;
        var (snapshot, snapRadius) = window.CapturePortalParentSnapshot(
            config.EffectivePortalParentVisualMode, parentRadius, highlightSector, highlightSub);
        stack.AttachCurrentVisual(window.PortalCoreRadius, snapshot, snapRadius);
        stack.SetCurrentExtent(parentRadius);
        stack.Push(new(address, center, childRadius, PortalPlacement.Physical(stack.Current.PhysicalCenter, hit.Anchor, screen.DpiScale),
            Math.Max(12, config.CoreRadius)));
        _portalDepth = stack.Depth;
        _currentWheelFocusSession = stack.Focus;
        bool far = PortalPlacement.Radius(center, pointer, screen.DpiScale) > config.GetEffectiveCoreDeadzone() + 8;
        var handoff = PortalPlacement.Handoff(released || _pinState.Enabled, target.IsOuterOffset, config.PortalAutoPins, far);
        if (handoff == PortalPointerHandoff.Warp)
        {
            if (SetCursorPos((int)Math.Round(center.X), (int)Math.Round(center.Y))) pointer = center;
            else handoff = PortalPointerHandoff.Pin;
        }
        if (handoff == PortalPointerHandoff.Pin) PinPortal(released);
        PresentPortalView(view, pointer, null);
        _interactionSession?.PortalNavigated(expanded: true);
        return true;

        bool Fail(string key)
        {
            _portalFailedSlot = hit.Key;
            window.ShowPortalFeedback(key);
            return false;
        }
    }

    private void PinPortal(bool released)
    {
        released |= _activeTrigger?.TriggerType == "Mouse" ? !_mouseTriggerDown : _portalKeyReleased;
        CancelMouseReleaseDebounce();
        if (!_pinState.Enabled)
        {
            _currentWheelFocusSession ??= _portalStack?.Focus;
            _pinState.Begin(true);
            if (released) _pinState.ReleaseTrigger();
            BeginPinnedPresentation(_portalVersion);
            if (!_isGestureActive) return;
            _pinTarget ??= _portalStack?.Focus?.TargetInfo;
            _pinTrigger ??= _activeTrigger;
            if (_radialWindow is { } window && _keyboardHook != null && _portalStack is { } stack)
                window.ConfigurePinnedArrowSelection(slots => PublishPinnedArrows(window, _portalVersion, stack.Revision, slots));
        }
        if (released)
        {
            _pinState.ReleaseTrigger();
            _mouseTriggerDown = false;
        }
    }

    private void BackFromPortal(PortalBackInput input, bool released)
    {
        if (_portalStack is not { } stack) return;
        if (stack.Back(input, ConfigManager.CurrentConfig.PortalEscapeClosesAll) == PortalBackDecision.Close)
        {
            CancelGestureTracking("PortalClosed");
            return;
        }
        ClearPortalHover();
        int index = stack.Depth - 2;
        var saved = _portalParents![index]!;
        _portalParents[index] = null;
        stack.Pop(saved.Slot, ConfigManager.CurrentConfig.PortalEnableReentryGuard);
        _portalDepth = stack.Depth;
        if (released || _pinState.Enabled) PinPortal(released);
        var restoredView = input == PortalBackInput.ParentCenter
            ? saved with { Sector = -1, Secondary = -1, Expanded = false }
            : saved;
        PresentPortalView(saved.Profile, _portalMovePoint, restoredView);
        var landed = SamplePortalSlot(_portalMovePoint);
        stack.AcceptPointerSlot(landed.Action?.IsWheelPortal == true ? landed.Key : null);
        _interactionSession?.PortalNavigated(expanded: false);
    }

    private void PresentPortalView(WheelProfile profile, Point pointer, PortalParentView? saved)
    {
        if (!PortalSessionIsCurrent()) return;
        var stack = _portalStack!;
        var frame = stack.Current;
        var window = _radialWindow!;
        _portalPreviousPointer = pointer;
        _portalFailedSlot = null;
        lock (_uiUpdateSync)
        {
            ResetDeepNavigation(clearRoots: true);
            _submenuLock.Reset();
            _suppressPrimaryOnRelease = false;
            _selectedSectorIndex = -2;
            _selectedSubSectorIndex = -1;
            _pinExpandedParent = -1;
            _pinClickVersion = -1;
            _lastEscapedState = _lastShowSubTier = false;
            _highlightUpdateScheduled = false;
            _activeProfile = profile;
            _startPoint = frame.PhysicalCenter;
            _lastMovePoint = _portalMovePoint = pointer;
            if (_portalScreen is { } activeScreen)
            {
                _currentDpiScaleX = activeScreen.DpiScale.DpiScaleX;
                _currentDpiScaleY = activeScreen.DpiScale.DpiScaleY;
            }
            _portalMovePending = false;
        }
        string? parentLabel = stack.Depth > 1 ? stack.Frames[^2].Address.Layer.DisplayName : null;
        window.PresentPortalFrame(frame, profile, stack.Revision, _portalVersion, parentLabel);
        PrepareInputRoots(profile);
        if (ConfigManager.CurrentConfig.LockParentSectorOnSubmenu || frame.Address.Layer.EffectiveMaxMenuDepth > 2)
            _submenuLock.PrepareGeometry(ConfigManager.CurrentConfig, profile);
        PrepareFixedSession(window, profile, _portalVersion, visible: true);
        if (saved != null)
        {
            _selectedSectorIndex = saved.Sector;
            _selectedSubSectorIndex = saved.Secondary;
            _lastShowSubTier = saved.Expanded;
            window.HighlightSector(saved.Sector, saved.Secondary, saved.Expanded);
            if (saved.Page is { } page)
            {
                ShowDeepPage(window, profile, page.Layer, stack.Revision, _portalVersion, page.Path, page.Parent,
                    page.Breadcrumb, pointer, page.Previous);
                // Restored segmented navigation still uses its original frame's positions.
                if (saved.Slot.Path != null) window.HighlightDeepMenu(saved.Slot.Index);
            }
            if (saved.FixedSession is { } fixedSession)
            {
                _fixedSession = fixedSession;
                fixedSession.Pinned = _pinState.Enabled;
                fixedSession.PublishFrame(fixedSession.Visibility);
                window.ShowFixedRings(fixedSession);
                window.UpdateFixedRings(fixedSession.Visibility, fixedSession.Hover);
                PublishSecondaryVisuals(window);
            }
        }
        window.DrawPortalTrail(stack, _portalScreen!);
        Interlocked.Increment(ref _portalVisualEpoch);
    }

    private bool ConfirmPortalPoint(Point point, bool released, Point? pressedPoint = null, FixedPress? fixedPress = null)
    {
        if (_portalStack == null || !PortalFrameIsCurrent()) return false;
        var hit = SamplePortalSlot(point);
        if (hit.Action?.IsWheelPortal == true)
        {
            if (pressedPoint is Point down && SamplePortalSlot(down).Key != hit.Key) return true;
            if (!released && hit.Key.Level >= 3 && hit.Key.Path == null &&
                (fixedPress == null || !ReferenceEquals(fixedPress.Session, _fixedSession) ||
                 fixedPress.Hit != new RingHit(hit.Key.Level, hit.Key.Index))) return true;
            if (((!released && _pinState.Enabled) || _portalStack.AcceptPointerSlot(hit.Key)) && OpenPortal(hit, point, released)) return true;
            // A configured but unreachable portal remains selectable in the current frame.
            if (released && !_pinState.Enabled) PinPortal(released: true);
            return true;
        }
        if (_portalDepth < 2 || (!released && hit.InlineNavigation) ||
            (PluginInteractionSession.IsExecutableAction(hit.Action) && PluginHost.IsMenuActionAvailable(hit.Action!))) return false;
        BackFromPortal(hit.Sector == -1 ? PortalBackInput.Center : PortalBackInput.Miss, released);
        return true;
    }

    private bool TryHandleLegacyHoldRelease(Point point)
    {
        if (_portalStack == null || !PortalFrameIsCurrent()) return false;
        if (_debouncedFixedRelease == null) ProcessMoveCore(point);
        if (_deepPage != null || _fixedSession is { Presented: true, ControlsOuter: true })
            return ConfirmPortalPoint(point, released: true);

        int sector, sub;
        bool escaped, locked;
        lock (_uiUpdateSync)
        {
            sector = _selectedSectorIndex;
            sub = _selectedSubSectorIndex;
            escaped = _lastEscapedState;
            locked = _submenuLock.IsLocked || _suppressPrimaryOnRelease;
        }
        var profile = _activeProfile;
        var layer = CaptureReleaseLayer(profile);
        ActionItem? action = null;
        if (!escaped && sector >= 0)
            TryResolveReleasedAction(profile, layer, sector, sub, isEscaped: false, locked, out action);

        if (!escaped && sector >= 0 && action?.IsWheelPortal == true && _radialWindow is { } window)
        {
            var anchor = window.PortalLegacyAnchor(sector, sub);
            int index = sub < 0 ? sector : FixedRingMenu.SecondaryKey(sector, sub);
            var key = new PortalSlotKey(sub < 0 ? 1 : 2, index, null);
            var hit = new PortalHit(key, action, anchor, window.PortalLegacyOuter(anchor, sub), sector, sub,
                Inherited: InputRoot(sector)?.IsInherited == true);
            if (_portalStack.AcceptPointerSlot(key) && OpenPortal(hit, point, released: true)) return true;
            if (!_pinState.Enabled) PinPortal(released: true);
            return true;
        }
        if (_portalDepth < 2) return false;
        if (!escaped && sector >= 0 && action != null &&
            PluginInteractionSession.IsExecutableAction(action) && PluginHost.IsMenuActionAvailable(action))
            return false;

        BackFromPortal(!escaped && sector == -1 ? PortalBackInput.Center : PortalBackInput.Miss, released: true);
        return true;
    }

    private void FinishPortalHold(PortalRelease release)
    {
        if (release.Activation != null && !ReferenceEquals(release.Activation, _activeActivation)) return;
        _mouseTriggerDown = false;
        if (release.Frame != _portalVisualEpoch)
        {
            PinPortal(released: true);
            return;
        }
        bool outerMode = _deepPage != null || _fixedSession is { Presented: true, ControlsOuter: true };
        bool handled = outerMode
            ? ConfirmPortalPoint(release.Point, released: true)
            : TryHandleLegacyHoldRelease(release.Point);
        if (handled) return;
        _currentWheelFocusSession ??= _portalStack?.Focus;
        CompleteMouseTriggerRelease(release.Point, release.Button, release.Activation, portalRelease: true);
    }
}
