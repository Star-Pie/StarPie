using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;

namespace WinPieGestures;

public partial class GestureController
{
    // Frame and page identity are replaced together; queued input can never act on a later page.
    private sealed record SlotBinding(ActionItem? Source, ActionItem? Action, bool HasChildren)
    {
        internal static SlotBinding Capture(ActionItem? item) => new(item, item?.CloneWithoutSubActions(),
            item?.SubActions is { Count: > 0 } && WheelProfile.IsActionConfigured(item));

        internal bool Matches(ActionItem? current, bool inherited)
        {
            if (current == null || Action is not { } saved || (!inherited && !ReferenceEquals(Source, current))) return false;
            if (saved.Type != current.Type || saved.Name != current.Name || saved.Parameter != current.Parameter ||
                saved.Arguments != current.Arguments || saved.CommandTerminal != current.CommandTerminal ||
                saved.BrowserChoice != current.BrowserChoice || saved.BrowserPath != current.BrowserPath ||
                saved.RunAsStandardUser != current.RunAsStandardUser || saved.LaunchOnCursorMonitor != current.LaunchOnCursorMonitor ||
                saved.PluginActionRef?.PluginId != current.PluginActionRef?.PluginId ||
                saved.PluginActionRef?.ContributionId != current.PluginActionRef?.ContributionId ||
                saved.ExtensionData?.Count != current.ExtensionData?.Count) return false;
            if (saved.ExtensionData != null)
                foreach (var pair in saved.ExtensionData)
                    if (!current.ExtensionData!.TryGetValue(pair.Key, out string? value) || pair.Value != value) return false;
            return true;
        }
    }

    private sealed record DeepPage(WheelProfile Profile, WheelLayer Layer, long Revision, long Version,
        int[] Path, ActionItem Parent, string Breadcrumb, DeepMenuFrame Frame, Point Entry, DeepPage? Previous)
    {
        private readonly SlotBinding[] _slots = CaptureSlots(Parent, Frame.Slots.Count);
        private static SlotBinding[] CaptureSlots(ActionItem parent, int count)
        {
            var slots = new SlotBinding[count];
            for (int i = 0; i < count; i++) slots[i] = SlotBinding.Capture(parent.SubActions[i]);
            return slots;
        }
        internal bool IsInheritedBranch => Previous?.IsInheritedBranch ?? Parent.IsInherited;
        internal SlotBinding? Binding(int slot) => (uint)slot < (uint)_slots.Length ? _slots[slot] : null;

        internal bool IsCurrent(WheelProfile? profile) =>
            ReferenceEquals(profile, Profile) && ReferenceEquals(CaptureReleaseLayer(profile), Layer) &&
            ConfigManager.ConfigurationRevision == Revision && ConfigManager.CurrentConfig.EnableMultiTier &&
            Path.Length < Layer.EffectiveMaxMenuDepth;

        internal int Hit(Point point, double scaleX, double scaleY) =>
            Math.Abs(point.X - Entry.X) < 3 * Math.Max(1, scaleX) &&
            Math.Abs(point.Y - Entry.Y) < 3 * Math.Max(1, scaleY) ? -1 : Frame.HitTest(point);

        internal ActionItem? Child(int slot) =>
            Binding(slot)?.Source;

        internal bool CanEnter(int slot) => Path.Length + 1 < Layer.EffectiveMaxMenuDepth &&
            Binding(slot)?.HasChildren == true;
    }

    private readonly record struct DeepRelease(DeepPage? Page, int Hit, bool Suppress)
    {
        internal FixedRelease? Fixed { get; init; }
    }

    // Resolving an inherited root clones its entire subtree. Prepare these roots once per
    // presentation on the UI thread; the low-level hook only reads the published table.
    private sealed record InputRoots(WheelProfile Profile, WheelLayer Layer, long Revision,
        ActionItem?[] Actions, bool[][] ConfiguredChildren);
    private volatile InputRoots? _inputRoots;

    private void PrepareInputRoots(WheelProfile profile)
    {
        var layer = CaptureReleaseLayer(profile);
        if (layer == null || layer.EffectiveMaxMenuDepth <= 2)
        {
            _inputRoots = null;
            return;
        }
        long revision = ConfigManager.ConfigurationRevision;
        var actions = new ActionItem?[layer.SectorCount];
        var configured = new bool[actions.Length][];
        bool fan = ConfigManager.CurrentConfig.SubmenuStyle == "Fan";
        for (int sector = 0; sector < actions.Length; sector++)
        {
            var action = actions[sector] = profile.GetEffectiveAction(sector);
            int count = DeepMenuFrame.VisibleSlotCount(action?.SubActions?.Count ?? 0, fan);
            var slots = configured[sector] = new bool[count];
            for (int i = 0; i < count; i++) slots[i] = WheelProfile.IsActionConfigured(action!.SubActions[i]);
        }
        _inputRoots = new(profile, layer, revision, actions, configured);
    }

    private InputRoots? MatchingInputRoots(WheelLayer layer)
    {
        var roots = _inputRoots;
        return roots != null && ReferenceEquals(roots.Profile, _activeProfile) && ReferenceEquals(roots.Layer, layer) &&
            roots.Revision == ConfigManager.ConfigurationRevision ? roots : null;
    }

    private ActionItem? InputRoot(int sector)
    {
        var layer = CaptureReleaseLayer(_activeProfile);
        if (layer == null || layer.EffectiveMaxMenuDepth == 2) return _activeProfile?.GetEffectiveAction(sector);
        if (MatchingInputRoots(layer) is { } roots && (uint)sector < (uint)roots.Actions.Length)
            return roots.Actions[sector];
        return null;
    }

    private bool IsInputChildConfigured(int sector, int sub, ActionItem? child)
    {
        var layer = CaptureReleaseLayer(_activeProfile);
        if (layer == null || layer.EffectiveMaxMenuDepth == 2) return WheelProfile.IsActionConfigured(child);
        return MatchingInputRoots(layer) is { } roots && (uint)sector < (uint)roots.ConfiguredChildren.Length &&
            (uint)sub < (uint)roots.ConfiguredChildren[sector].Length && roots.ConfiguredChildren[sector][sub];
    }

    private volatile DeepPage? _deepPage;
    private int _deepHover = -1;
    private bool _deepUiPending;
    private bool _deepReturned;
    private Point _deepReturnPoint;
    private int _returnedSector = -1;
    private int _returnedSubSector = -1;

    private void ResetDeepNavigation(bool clearRoots = false)
    {
        if (clearRoots)
        {
            _inputRoots = null;
            _debouncedFixedRelease = null;
            _switchingFixedLayer = false;
        }
        _fixedSession = null;
        _fixedPress = null;
        _fixedRenderQueued = false;
        _deepPage = null;
        _deepHover = -1;
        _deepUiPending = false;
        _deepReturned = false;
        _returnedSector = _returnedSubSector = -1;
    }

    private static int[] AppendMenuSlot(int[] parent, int slot)
    {
        int[] result = new int[parent.Length + 1];
        Array.Copy(parent, result, parent.Length);
        result[^1] = slot;
        return result;
    }

    private bool TryEnterSecondLevelBranch(RadialWindow window, int sector, int sub, Point point, long version)
    {
        var profile = _activeProfile;
        var layer = CaptureReleaseLayer(profile);
        if (UsesFixedRings(profile) || !_isGestureActive || version != _gestureVersion || window.PresentationVersion != version ||
            !ConfigManager.CurrentConfig.EnableMultiTier || layer == null || layer.EffectiveMaxMenuDepth <= 2 ||
            sector < 0 || sub < 0 || (_returnedSector == sector && _returnedSubSector == sub)) return false;
        int[] path = [sector, sub];
        long revision = ConfigManager.ConfigurationRevision;
        var branch = profile!.GetEffectiveMenuAction(path);
        if (branch?.SubActions is not { Count: > 0 }) return false;
        string rootName = profile.GetEffectiveAction(sector)?.Name ?? "";
        return ShowDeepPage(window, profile, layer, revision, version, path, branch,
            rootName + " › " + branch.Name, point, null);
    }

    private bool ShowDeepPage(RadialWindow window, WheelProfile profile, WheelLayer layer, long revision,
        long version, int[] path, ActionItem parent, string breadcrumb, Point entry, DeepPage? previous)
    {
        // Window methods construct WPF objects on its Dispatcher, never in the mouse hook.
        if (!IsCurrentGesture(version) || !_isGestureActive || window.PresentationVersion != version) return true;
        bool inherited = previous?.IsInheritedBranch ?? parent.IsInherited;
        var current = profile.GetEffectiveMenuAction(path, layerIndex: profile.Layers.IndexOf(layer));
        if (!ReferenceEquals(layer, CaptureReleaseLayer(_activeProfile)) || revision != ConfigManager.ConfigurationRevision ||
            current?.SubActions is not { Count: > 0 } || current.IsInherited != inherited ||
            !SlotBinding.Capture(parent).Matches(current, inherited))
        {
            CancelGestureTracking("MenuSourceChanged");
            return true;
        }
        var frame = window.ShowDeepMenu(path, current, breadcrumb);
        var prepared = new DeepPage(profile, layer, revision, version, path, current, breadcrumb, frame, entry, previous);
        lock (_uiUpdateSync)
        {
            if (!_isGestureActive || version != _gestureVersion || revision != ConfigManager.ConfigurationRevision ||
                !ReferenceEquals(layer, CaptureReleaseLayer(_activeProfile)))
            {
                window.ClearDeepMenu();
                return false;
            }
            _deepPage = prepared;
            _deepHover = -1;
            _deepUiPending = false;
            _deepReturned = false;
            _highlightUpdateScheduled = false;
            _lastEscapedState = false;
            return true;
        }
    }

    private void TrackReturnHover(Point point, int sector, int sub)
    {
        if (_returnedSector < 0) return;
        if (Math.Abs(point.X - _deepReturnPoint.X) < 3 * Math.Max(1, _currentDpiScaleX) &&
            Math.Abs(point.Y - _deepReturnPoint.Y) < 3 * Math.Max(1, _currentDpiScaleY)) return;
        if (sector == _returnedSector && (sub < 0 || sub == _returnedSubSector)) return;
        _returnedSector = _returnedSubSector = -1;
        _deepReturned = false;
    }

    private bool IsDeepEscape(DeepPage page, Point point)
    {
        var config = ConfigManager.CurrentConfig;
        if (!config.EnableOuterEscapeCancel || page.Frame.ContainsWheel(point)) return false;
        double dx = (point.X - _startPoint.X) / Math.Max(0.1, _currentDpiScaleX);
        double dy = (point.Y - _startPoint.Y) / Math.Max(0.1, _currentDpiScaleY);
        double limit = Math.Max(page.Frame.OuterDistance + 1,
            config.OuterEscapeDistance > 0 ? config.OuterEscapeDistance : page.Frame.OuterDistance * 1.5);
        return dx * dx + dy * dy > limit * limit;
    }

    private bool ProcessDeepMove(Point point)
    {
        var page = _deepPage;
        if (page == null) return false;
        lock (_uiUpdateSync)
        {
            if (!ReferenceEquals(_deepPage, page)) return true;
            _lastMovePoint = point;
            bool valid = page.IsCurrent(_activeProfile);
            int hit = valid ? page.Hit(point, _currentDpiScaleX, _currentDpiScaleY) : -1;
            bool escaped = valid && !_pinState.Enabled && IsDeepEscape(page, point);
            if (valid && _deepHover == hit && _lastEscapedState == escaped) return true;
            _deepHover = hit;
            _lastEscapedState = escaped;
            if (_deepUiPending) return true;
            _deepUiPending = true;
        }
        Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Render, (Action)(() =>
        {
            if (!ReferenceEquals(_deepPage, page) || !_isGestureActive || !IsCurrentGesture(page.Version)) return;
            _deepUiPending = false;
            if (!page.IsCurrent(_activeProfile))
            {
                CancelGestureTracking("MenuSourceChanged");
                return;
            }
            if (_radialWindow is not { } window) return;
            window.SetOuterEscapeState(_lastEscapedState);
            int hit = _deepHover;
            if (!_pinState.Enabled && NavigateDeepPage(window, page, hit, _lastMovePoint)) return;
            window.HighlightDeepMenu(hit);
        }));
        return true;
    }

    // Normal gestures traverse on hover. A pinned wheel traverses on a paired click.
    private bool NavigateDeepPage(RadialWindow window, DeepPage page, int hit, Point point)
    {
        if (hit == -2)
        {
            if (page.Previous is { } up)
                return ShowDeepPage(window, up.Profile, up.Layer, up.Revision, up.Version, up.Path,
                    up.Parent, up.Breadcrumb, point, up.Previous);
            lock (_uiUpdateSync)
            {
                ResetDeepNavigation();
                _deepReturned = true;
                _deepReturnPoint = point;
                _returnedSector = page.Path[0];
                _returnedSubSector = page.Path[1];
                _selectedSectorIndex = page.Path[0];
                _selectedSubSectorIndex = -1;
                _lastShowSubTier = true;
                _lastEscapedState = false;
                _pinExpandedParent = page.Path[0];
            }
            window.ClearDeepMenu();
            window.HighlightSector(page.Path[0], -1, true);
            return true;
        }
        if (!page.CanEnter(hit)) return false;
        var child = page.Child(hit)!;
        return ShowDeepPage(window, page.Profile, page.Layer, page.Revision, page.Version,
            AppendMenuSlot(page.Path, hit), child, page.Breadcrumb + " › " + child.Name, point, page);
    }

    private DeepRelease CaptureDeepRelease(Point point)
    {
        if (_switchingFixedLayer)
        {
            _lastEscapedState = false;
            return new(null, -1, true);
        }
        if (_debouncedFixedRelease is { } sampled)
        {
            if (!ReferenceEquals(sampled.Session, _fixedSession))
            {
                _lastEscapedState = false;
                return new(null, -1, true);
            }
            _selectedSectorIndex = sampled.Sector;
            _selectedSubSectorIndex = sampled.Sub;
            _lastEscapedState = sampled.Escaped;
            return sampled.Selection;
        }
        if (_fixedSession is { } fixedSession)
        {
            bool wasOuter = fixedSession.ControlsOuter;
            fixedSession.Move(point);
            RingHit fixedHit = default;
            if (!fixedSession.Presented)
            {
                _lastEscapedState = false;
                return new(null, -1, true);
            }
            if (fixedSession.ControlsOuter)
            {
                fixedHit = fixedSession.Hit(point);
                if (!fixedSession.WasVisible(fixedHit)) fixedHit = default;
                (_selectedSectorIndex, _selectedSubSectorIndex) = fixedSession.Target(fixedHit);
                _lastEscapedState = !_pinState.Enabled && fixedSession.IsEscaped(point);
            }
            else if (fixedSession.PersistentFan && !_pinState.Enabled)
            {
                // Mouse-up can arrive before the last hover update, including inside a gap.
                fixedHit = fixedSession.HitPersistentFanInner(point, _submenuLock);
                if (fixedHit.Level == 2 && FixedRingMenu.TryDecodeSecondaryKey(fixedHit.Index, out int parent, out int child))
                    (_selectedSectorIndex, _selectedSubSectorIndex) = (parent, child);
                else
                    (_selectedSectorIndex, _selectedSubSectorIndex) = (fixedHit.Level == 1 ? fixedHit.Index : fixedHit.Index == -1 ? -1 : -2, -1);
                _lastEscapedState = false;
            }
            else if (!wasOuter && !_lastEscapedState)
            {
                // The legacy controller owns center/primary/secondary selection and its lock suppression.
                if (_selectedSectorIndex == -1 && fixedSession.Path.Length == 0) fixedHit = new(0, -1);
                else if (fixedSession.CanConfirmSecondary(point, _selectedSectorIndex, _selectedSubSectorIndex))
                    fixedHit = new(2, FixedRingMenu.SecondaryKey(_selectedSectorIndex, _selectedSubSectorIndex));
                else if (fixedSession.Path.Length == 0 && _selectedSubSectorIndex < 0 && _selectedSectorIndex >= 0 && !_suppressPrimaryOnRelease && !_submenuLock.IsLocked)
                    fixedHit = new(1, _selectedSectorIndex);
            }
            return new(null, -1, false) { Fixed = new(fixedSession, fixedHit) };
        }
        if (UsesFixedRings(_activeProfile)) return new(null, -1, true);
        var page = _deepPage;
        if (page == null) return new(null, -1, _deepReturned);
        int hit = page.Hit(point, _currentDpiScaleX, _currentDpiScaleY);
        _lastEscapedState = !_pinState.Enabled && IsDeepEscape(page, point);
        _selectedSectorIndex = hit == -3 ? -1 : page.Path[0];
        _selectedSubSectorIndex = hit == -3 ? -1 : page.Path[1];
        return new(page, hit, false);
    }

    private static bool IsDeepMenuLeafAvailable(ActionItem? action) =>
        action != null && IsExecutablePinnedAction(action) && Plugins.PluginHost.IsMenuActionAvailable(action);

    private bool ResolveGestureRelease(DeepRelease selection, WheelProfile? profile, WheelLayer? layer,
        int sector, int sub, bool escaped, bool locked, out ActionItem? action)
    {
        action = null;
        if (selection.Suppress) return true;
        if (selection.Fixed is { } fixedRelease)
        {
            var session = fixedRelease.Session;
            if (!IsCurrentGesture(session.Version + 1) || !session.IsCurrent(profile) || !ReferenceEquals(layer, session.Layer)) return false;
            return escaped || session.Resolve(fixedRelease.Hit, out action);
        }
        if (selection.Page is not { } page)
            return TryResolveReleasedAction(profile, layer, sector, sub, escaped, locked, out action);
        // EndActiveGesture advances one generation. A newer gesture or cancellation invalidates this callback.
        if (!IsCurrentGesture(page.Version + 1) || !page.IsCurrent(profile) || !ReferenceEquals(layer, page.Layer))
            return false;
        if (escaped) return true;
        if (selection.Hit == -3)
            return TryResolveReleasedAction(profile, layer, -1, -1, false, false, out action);
        if (selection.Hit < 0 || page.CanEnter(selection.Hit)) return true;
        var leaf = profile!.GetEffectiveMenuAction(AppendMenuSlot(page.Path, selection.Hit),
            layerIndex: profile.Layers.IndexOf(page.Layer));
        var binding = page.Binding(selection.Hit);
        if (binding == null || leaf?.IsInherited != page.IsInheritedBranch || !binding.Matches(leaf, page.IsInheritedBranch)) return false;
        if (IsDeepMenuLeafAvailable(binding.Action)) action = binding.Action;
        return true;
    }

    private bool ConfirmDeepSelection(Point point, long version)
    {
        if (_deepPage is not { } page) return false;
        if (!page.IsCurrent(_activeProfile) || !page.Frame.ContainsWheel(point))
        {
            CancelGestureTracking("PinnedOutsideMenu");
            return true;
        }
        int hit = page.Hit(point, _currentDpiScaleX, _currentDpiScaleY);
        if (NavigateDeepPage(_radialWindow!, page, hit, point)) return true;
        if (hit == -3 ? IsExecutablePinnedAction(_activeProfile?.GetEffectiveCenterAction()) :
            IsDeepMenuLeafAvailable(page.Child(hit)))
        {
            _mouseTriggerDown = true;
            CompleteMouseTriggerRelease(point, _pinTrigger?.MouseButton ?? "RightButton", _activeActivation);
        }
        return true;
    }
}
