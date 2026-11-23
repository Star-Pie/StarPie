using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace WinPieGestures;

public partial class GestureController
{
    internal readonly record struct RingHit(int Level, int Index)
    {
        internal bool IsOuter => Level >= 3;
    }

    internal sealed class FixedRingSession
    {
        internal sealed record Cell(ActionItem? Display, FixedRingSlot? SourceSlot, Geometry Shape);

        // The radial span of a petal changes with the pointer angle. Read cached line segments
        // of the actual rendered outline; a projected angle alone cannot arm a hidden or empty slot.
        private sealed class ParentOutline
        {
            private readonly (Point Start, Vector Edge)[] _edges;
            internal ParentOutline(Geometry shape)
            {
                var polygon = shape.GetFlattenedPathGeometry(.08, ToleranceType.Absolute);
                var edges = new List<(Point, Vector)>();
                Point Map(Point p) => polygon.Transform?.Transform(p) ?? p;
                foreach (var figure in polygon.Figures)
                {
                    Point start = Map(figure.StartPoint), previous = start;
                    void Add(Point p)
                    {
                        if (p != previous) edges.Add((previous, p - previous));
                        previous = p;
                    }
                    foreach (var segment in figure.Segments)
                        if (segment is PolyLineSegment poly) foreach (var p in poly.Points) Add(Map(p));
                        else if (segment is LineSegment line) Add(Map(line.Point));
                    Add(start);
                }
                _edges = edges.ToArray();
            }

            internal bool TrySpan(Point p, double radius, out double inner, out double outer)
            {
                inner = double.MaxValue;
                outer = 0;
                if (radius <= 0) return false;
                var ray = (Vector)p / radius;
                foreach (var (start, edge) in _edges)
                {
                    double cross = Vector.CrossProduct(ray, edge);
                    if (Math.Abs(cross) < .0000001) continue;
                    double fraction = Vector.CrossProduct((Vector)start, ray) / cross;
                    if (fraction is < 0 or > 1) continue;
                    double distance = Vector.CrossProduct((Vector)start, edge) / cross;
                    if (distance < 0) continue;
                    inner = Math.Min(inner, distance);
                    outer = Math.Max(outer, distance);
                }
                return outer > inner;
            }
        }

        internal readonly WheelProfile Profile;
        internal readonly WheelLayer Layer;
        internal readonly FixedRingMenu Menu;
        internal readonly FixedRingGeometry Geometry;
        internal readonly long Version, Revision;
        internal readonly bool Persistent;
        internal bool IsHoneycomb => Geometry.IsHoneycomb;
        internal readonly Cell[][] Rows;
        private readonly Dictionary<int, ParentOutline> _secondaryOutlines = new();
        private readonly Dictionary<int, SlotBinding> _secondaryBindings = new();
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FixedRingGeometry,
            Dictionary<(int Level, int Owner, int Index, int Count), ParentOutline>> CachedOutlines = new();
        private readonly HashSet<(int Level, int Index)> _configuredSlots;
        private readonly SlotBinding[] _rootBindings;
        private readonly int[] _secondaryCounts;
        private readonly SlotBinding _center;
        private readonly SlotBinding[][] _bindings;
        private readonly Point _origin;
        private readonly DpiScale _dpi;
        private readonly double _trigger, _primaryRadius;
        private readonly FixedRingProximity _proximity;
        private readonly double _configuredExtent;
        private long _presentedThird, _presentedFourth, _presentedFifth;
        private int _trackedPrimary = -1;
        private double _trackedRadius;
        internal bool Pinned { private get; set; }
        internal int ExtendedPrimary { get; private set; } = -1;
        internal bool ShowingExtendedPrimary => ControlsOuter && Visibility.VisibleDepth == 2 && ExtendedPrimary >= 0;
        internal bool NeedsExtendedSecondary => ControlsOuter && ExtendedPrimary >= 0 &&
            !ShowAllSecondary && ShownParent != ExtendedPrimary;
        private readonly Dictionary<int, ParentOutline> _primaryOutlines = new();
        private readonly double _legacyExtent;
        internal int ShownParent { get; private set; } = -1;
        internal bool ShowAllSecondary { get; private set; }
        internal FixedRingVisibility Visibility { get; private set; }
        internal bool Presented { get; private set; }
        internal void Present() => Presented = true;

        // Input may sample during rendering. Each hit checks just the published mask for its tier.
        internal void PublishFrame(FixedRingVisibility visibility)
        {
            System.Threading.Volatile.Write(ref _presentedThird, (long)visibility.Third);
            System.Threading.Volatile.Write(ref _presentedFourth, (long)visibility.Fourth);
            System.Threading.Volatile.Write(ref _presentedFifth, (long)visibility.Fifth);
        }

        internal RingHit Hover { get; private set; }
        internal int VisibleDepth => Visibility.VisibleDepth;
        internal bool ControlsOuter { get; private set; }

        internal FixedRingSession(WheelProfile profile, WheelLayer layer, long version, FixedRingGeometry geometry,
            Point origin, DpiScale dpi, double legacyExtent)
        {
            Profile = profile;
            Layer = layer;
            Menu = layer.ActiveFixedRings!;
            Persistent = Menu.ShowsAllOnPopup(ConfigManager.CurrentConfig);
            Version = version;
            Revision = ConfigManager.ConfigurationRevision;
            Geometry = geometry;
            _origin = origin;
            _dpi = dpi;
            _legacyExtent = IsHoneycomb ? geometry.Outer(2) : legacyExtent;
            var config = ConfigManager.CurrentConfig;
            _center = SlotBinding.Capture(profile.GetEffectiveCenterAction());
            _rootBindings = new SlotBinding[layer.SectorCount];
            _secondaryCounts = new int[layer.SectorCount];
            Rows = new Cell[layer.EffectiveMaxMenuDepth][];
            _bindings = new SlotBinding[Rows.Length][];
            _configuredSlots = Menu.ConfiguredSlots(profile, Rows.Length, config.SubmenuStyle == "Fan");
            _trigger = config.SubWheelTriggerDistance > 20 ? config.SubWheelTriggerDistance : 95;
            _primaryRadius = Math.Max(1, config.WheelRadius);
            _proximity = new(geometry, Menu, Rows.Length, _configuredSlots);
            Visibility = Persistent ? _proximity.Configured : default;
            int deepest = _proximity.Configured.VisibleDepth;
            _configuredExtent = deepest > 2 ? geometry.OuterForCount(deepest, Menu.GetCount(deepest)) : _legacyExtent;
            var cache = CachedOutlines.GetOrCreateValue(geometry);
            for (int level = 3; level <= Rows.Length; level++)
            {
                var row = Rows[level - 1] = new Cell[Menu.GetCount(level)];
                var bindings = _bindings[level - 1] = new SlotBinding[row.Length];
                for (int i = 0; i < row.Length; i++)
                {
                    var slot = Menu.GetSlot(level, i);
                    var bound = bindings[i] = SlotBinding.Capture(slot?.Action);
                    var display = _configuredSlots.Contains((level, i)) ? bound.Action : null;
                    row[i] = new(display, slot, display != null ? geometry.GetShape(level, i, row.Length) : System.Windows.Media.Geometry.Empty);
                }
            }
            for (int root = 0; root < layer.SectorCount; root++)
            {
                var parent = Persistent ? FixedRingMenu.DisplayRoot(profile, root) : profile.GetEffectiveAction(root);
                _rootBindings[root] = SlotBinding.Capture(parent);
                int count = parent?.InlineSubActions?.Count ?? 0;
                _secondaryCounts[root] = count;
                int visible = config.SubmenuStyle == "Fan" ? Math.Min(RadialWindow.FanSubmenuSlotCount, count) : count;
                if (IsHoneycomb || (!Persistent && visible > 0))
                {
                    var cacheKey = (1, -1, root, layer.SectorCount);
                    if (!cache.TryGetValue(cacheKey, out var primary))
                        cache[cacheKey] = primary = new(geometry.Primary(config, root).Shape);
                    _primaryOutlines[root] = primary;
                }
                for (int sub = 0; sub < visible; sub++)
                {
                    var child = parent!.InlineSubActions[sub];
                    int key = FixedRingMenu.SecondaryKey(root, sub);
                    if (!_configuredSlots.Contains((2, key))) continue;
                    _secondaryBindings[key] = SlotBinding.Capture(child);

                    var cacheKey = (2, root, sub, count);
                    if (!cache.TryGetValue(cacheKey, out var outline))
                    {
                        var (shape, _) = geometry.Secondary(config, root, sub, count);
                        cache[cacheKey] = outline = new(shape);
                    }
                    _secondaryOutlines[key] = outline;
                }
            }
        }

        private Point Relative(Point point) => new((point.X - _origin.X) / _dpi.DpiScaleX, (point.Y - _origin.Y) / _dpi.DpiScaleY);
        internal bool IsOuterPoint(Point point)
        {
            var local = Relative(point);
            return ((Vector)local).LengthSquared > _legacyExtent * _legacyExtent ||
                (IsHoneycomb && HitAt(local, _proximity.Configured).IsOuter);
        }
        internal bool IsCurrent(WheelProfile? profile) => ReferenceEquals(profile, Profile) &&
            ReferenceEquals(CaptureReleaseLayer(profile), Layer) && ReferenceEquals(Layer.ActiveFixedRings, Menu) &&
            Revision == ConfigManager.ConfigurationRevision && ConfigManager.CurrentConfig.EnableMultiTier &&
            Rows.Length == Layer.EffectiveMaxMenuDepth && Persistent == Menu.ShowsAllOnPopup(ConfigManager.CurrentConfig) && _rootBindings.Length == Layer.SectorCount;

        internal bool SetShownSecondary(int parent, bool all)
        {
            if (ShownParent == parent && all == ShowAllSecondary) return false;
            ShownParent = parent;
            ShowAllSecondary = all;
            return true;
        }

        internal bool WasVisible(RingHit hit)
        {
            if (!Presented || (uint)hit.Index >= 64) return false;
            long tier = hit.Level switch
            {
                3 => System.Threading.Volatile.Read(ref _presentedThird),
                4 => System.Threading.Volatile.Read(ref _presentedFourth),
                5 => System.Threading.Volatile.Read(ref _presentedFifth),
                _ => 0
            };
            return ((ulong)tier & (1UL << hit.Index)) != 0;
        }

        internal RingHit Hit(Point point) => HitAt(Relative(point), Visibility);

        // The existing interaction event contract has two address fields. Report the
        // geometric direction and a distinct slot address for feedback on fixed tiers.
        internal (int Sector, int Sub) FeedbackAddress(RingHit hit)
        {
            if (!hit.IsOuter) return (-2, -1);
            var center = Geometry.GetCenter(hit.Level, hit.Index, Rows[hit.Level - 1].Length);
            int roots = _rootBindings.Length;
            double turn = Math.Atan2(center.Y, center.X) / Math.Tau;
            int sector = (int)Math.Floor((turn + 1) * roots + .5) % roots;
            return (sector, _secondaryCounts[sector] + (hit.Level - 3) * FixedRingMenu.MaximumCount(5) + hit.Index);
        }

        private RingHit HitAt(Point local, FixedRingVisibility visible)
        {
            for (int level = 3; level <= visible.VisibleDepth; level++)
            {
                int index = Geometry.HitVisible(local, level, Rows[level - 1].Length, visible.At(level));
                if (index >= 0) return new(level, index);
            }
            return default;
        }

        internal RingHit HitHoneycombInner(Point point, SubmenuLockTracker parentLock)
        {
            if (!IsHoneycomb || !Presented) return default;
            var local = Relative(point);
            double radius = ((Vector)local).Length;
            var config = ConfigManager.CurrentConfig;
            bool lockEnabled = config.LockParentSectorOnSubmenu && !Pinned;
            double exit = Math.Min(Geometry.Inner(2), SubmenuLockTracker.GetSubmenuExitDistance(config, true, _primaryRadius));
            if (!lockEnabled || radius < exit) parentLock.Reset();
            int address = Geometry.Hit(local, 2, 3 * _rootBindings.Length);
            if (address >= 0)
            {
                int parent = address / 3, child = address % 3;
                int key = FixedRingMenu.SecondaryKey(parent, child);
                if ((ShowAllSecondary || parent == ShownParent) && (!parentLock.IsLocked || parentLock.LockedParentSector == parent) &&
                    _secondaryBindings.ContainsKey(key))
                {
                    if (lockEnabled) parentLock.Lock(parent);
                    return new(2, key);
                }
                return default;
            }
            if (parentLock.IsLocked) return default;
            double deadzone = config.GetEffectiveCoreDeadzone();
            if (radius < Geometry.CoreDeadzoneRadius(deadzone > 0 ? deadzone : 15)) return new(0, -1);
            int root = Geometry.Hit(local, 1, _rootBindings.Length);
            return root >= 0 && _configuredSlots.Contains((1, root)) ? new(1, root) : default;
        }

        internal bool CanConfirmSecondary(Point point, int root, int sub)
        {
            if (root < 0 || sub < 0 || !(ShowAllSecondary || root == ShownParent)) return false;
            var hit = new RingHit(2, FixedRingMenu.SecondaryKey(root, sub));
            if (!_secondaryOutlines.TryGetValue(hit.Index, out var outline)) return false;
            var local = Relative(point);
            double radius = ((Vector)local).Length;
            return outline.TrySpan(local, radius, out double inner, out double outer) && radius >= inner && radius <= outer;
        }

        private void TrackExtendedPrimary(Point local, double radius)
        {
            if (Persistent || _primaryOutlines.Count == 0 || ShowAllSecondary) return;
            if (IsHoneycomb)
            {
                int rootHit = Geometry.Hit(local, 1, _rootBindings.Length);
                if (radius < Geometry.Inner(1)) _trackedPrimary = ExtendedPrimary = -1;
                if (rootHit >= 0)
                {
                    if (_trackedPrimary != rootHit) ExtendedPrimary = -1;
                    _trackedPrimary = rootHit;
                }
                if (_trackedPrimary >= 0)
                {
                    if (!_primaryOutlines[_trackedPrimary].TrySpan(local, radius, out double lo, out _))
                    {
                        if (ExtendedPrimary < 0) _trackedPrimary = -1;
                    }
                    else if (radius >= Math.Max(lo, _trigger) && (rootHit >= 0 || radius >= _trackedRadius) &&
                        _rootBindings[_trackedPrimary].HasChildren)
                        ExtendedPrimary = _trackedPrimary;
                }
                _trackedRadius = radius;
                return;
            }
            if (!Pinned && _trigger <= _legacyExtent) return;
            if (radius < Math.Max(25, _primaryRadius - 6))
                ExtendedPrimary = -1;
            int count = _rootBindings.Length;
            double angle = Math.Atan2(local.Y, local.X) / (2 * Math.PI);
            int root = (int)Math.Floor((angle + 1) * count + .5) % count;
            if (!_primaryOutlines.TryGetValue(root, out var outline) ||
                !outline.TrySpan(local, radius, out double inner, out double outer) || radius < inner)
            {
                _trackedPrimary = -1;
                return;
            }
            if (radius <= outer)
            {
                _trackedPrimary = root;
                if (ExtendedPrimary != root) ExtendedPrimary = -1;
            }
            else if (_trackedPrimary != root || (ExtendedPrimary < 0 && radius < _trackedRadius))
                _trackedPrimary = -1;
            else if (radius >= _trigger)
                ExtendedPrimary = root;
            _trackedRadius = radius;
        }

        internal bool Move(Point point)
        {
            var local = Relative(point);
            double radius = ((Vector)local).Length;
            TrackExtendedPrimary(local, radius);
            var visible = Persistent ? _proximity.Configured : _proximity.Around(local, Visibility);
            var hit = HitAt(local, visible);
            bool outer = radius > _legacyExtent || (IsHoneycomb && hit.IsOuter);
            if (!outer) hit = default;
            bool changed = visible != Visibility || outer != ControlsOuter || hit != Hover;
            Visibility = visible;
            ControlsOuter = outer;
            Hover = hit;
            return changed;

        }

        internal bool IsEscaped(Point point)
        {
            var config = ConfigManager.CurrentConfig;
            double outer = _configuredExtent;
            double limit = Math.Max(outer + 1, config.OuterEscapeDistance > 0 ? config.OuterEscapeDistance : outer * 1.5);
            if (ShowingExtendedPrimary) limit = Math.Max(limit, _trigger + 2);
            return config.EnableOuterEscapeCancel && ((Vector)Relative(point)).Length > limit;
        }

        private bool BindingCurrent(int level, int i)
        {
            var row = Rows[level - 1];
            if (Menu.GetCount(level) != row.Length) return false;
            var slot = Menu.GetSlot(level, i);
            return slot != null && ReferenceEquals(row[i].SourceSlot, slot) &&
                _bindings[level - 1][i].Matches(slot.Action, false);
        }

        private bool SecondaryCurrent(int key)
        {
            if (!_secondaryBindings.TryGetValue(key, out var binding)) return false;
            if (!FixedRingMenu.TryDecodeSecondaryKey(key, out int root, out int sub)) return false;
            var action = Persistent ? FixedRingMenu.DisplayRoot(Profile, root) : Profile.GetEffectiveAction(root);
            bool inherited = _rootBindings[root].Action?.IsInherited == true;
            return inherited == (action?.IsInherited == true) && _rootBindings[root].Matches(action, inherited) &&
                action?.InlineSubActions is { } children && sub < children.Count && binding.Matches(children[sub], inherited);
        }

        internal bool Resolve(RingHit hit, out ActionItem? action)
        {
            action = null;
            if (!IsCurrent(Profile)) return false;
            if (hit.Level is 0 or 1 or 2)
            {
                if (hit.Level == 0 && hit.Index != -1) return true;
                if (hit.Level != 0 && !_configuredSlots.Contains((hit.Level, hit.Index))) return true;
                SlotBinding? binding;
                ActionItem? current;
                bool inherited;
                if (hit.Level == 2)
                {
                    if (!SecondaryCurrent(hit.Index)) return false;
                    binding = _secondaryBindings[hit.Index];

                }
                else
                {
                    if (hit.Level == 1 && (uint)hit.Index >= (uint)_rootBindings.Length) return false;
                    binding = hit.Level == 0 ? _center : _rootBindings[hit.Index];
                    current = hit.Level == 0 ? Profile.GetEffectiveCenterAction() :
                        Persistent ? FixedRingMenu.DisplayRoot(Profile, hit.Index) : Profile.GetEffectiveAction(hit.Index);
                    inherited = binding.Action?.IsInherited == true;
                    if (binding.Action == null && current == null) return true;
                    if (inherited != (current?.IsInherited == true) || !binding.Matches(current, inherited)) return false;
                }
                if (IsDeepMenuLeafAvailable(binding.Action)) action = binding.Action;
                return true;
            }
            if (!Visibility.Contains(hit.Level, hit.Index) || !WasVisible(hit)) return true;
            if (!BindingCurrent(hit.Level, hit.Index)) return false;
            var saved = _bindings[hit.Level - 1][hit.Index].Action;
            if (IsDeepMenuLeafAvailable(saved)) action = saved;
            return true;
        }
    }
    private sealed record FixedRelease(FixedRingSession Session, RingHit Hit);
    private sealed record FixedPress(FixedRingSession Session, RingHit Hit);
    private sealed record DebouncedFixedRelease(DeepRelease Selection, FixedRingSession? Session, int Sector, int Sub, bool Escaped);
    private DebouncedFixedRelease? _debouncedFixedRelease;
    private FixedRingSession? _fixedSession;
    private FixedPress? _fixedPress;
    private bool _fixedRenderQueued;
    private volatile bool _switchingFixedLayer;
    private long _layerSwitchSequence;

    private static bool UsesFixedRings(WheelProfile? profile) => profile != null && ConfigManager.CurrentConfig?.EnableMultiTier == true &&
        CaptureReleaseLayer(profile) is { ActiveFixedRings: not null, EffectiveMaxMenuDepth: > 2 };

    private long BeginFixedLayerSwitch(WheelProfile profile, int next)
    {
        lock (_uiUpdateSync)
        {
            long request = ++_layerSwitchSequence;
            if (_portalAcceptInput || _switchingFixedLayer || _fixedSession != null || UsesFixedRings(profile) ||
                profile.Layers[next] is { ActiveFixedRings: not null, EffectiveMaxMenuDepth: > 2 })
            {
                _switchingFixedLayer = true;
                _fixedPress = null;
                System.Threading.Interlocked.Exchange(ref _pinClickVersion, -1);
            }
            profile.ActiveLayerIndex = next;
            profile.SyncRootPropertiesFromActiveLayer();
            return request;
        }
    }

    private void PrepareFixedSession(RadialWindow window, WheelProfile profile, long version, bool visible = false, long? layerRequest = null)
    {
        if (window.FixedGeometry is not { } geometry || !UsesFixedRings(profile)) return;
        var screen = ScreenHelper.GetScreenContextAtPoint(window.ActualPhysicalCenter);
        var session = new FixedRingSession(profile, CaptureReleaseLayer(profile)!, version, geometry,
            window.ActualPhysicalCenter, screen.DpiScale, window.LegacyOuterExtent);
        window.ShowFixedRings(session);
        lock (_uiUpdateSync)
        {
            if (!_isGestureActive || _gestureVersion != version || (layerRequest.HasValue && layerRequest != _layerSwitchSequence) || !session.IsCurrent(_activeProfile)) return;
            _fixedSession = session;
            session.SetShownSecondary(window.ShownSecondaryParent, window.AllShownSecondaries);
            if (visible) session.Present();
            _lastEscapedState = false;
            _selectedSectorIndex = -2;
            _selectedSubSectorIndex = -1;
            _highlightUpdateScheduled = false;
        }
    }

    // A release before presentation has no displayed fixed target and must never use the legacy angle fallback.
    private bool ProcessFixedMove(Point point)
    {
        if (!_switchingFixedLayer && System.Threading.Volatile.Read(ref _fixedSession) == null && !UsesFixedRings(_activeProfile)) return false;
        lock (_uiUpdateSync)
        {
            if (_switchingFixedLayer)
            {
                _lastMovePoint = point;
                return true;
            }
            if (_debouncedFixedRelease != null) return true;
            if (_volumeAdjustActive) return false;
            var session = _fixedSession;
            if (session == null)
            {
                if (!UsesFixedRings(_activeProfile)) return false;
                _lastMovePoint = point;
                return true;
            }
            _lastMovePoint = point;
            if (_pinState.Enabled)
            {
                _pendingPinnedHoverPoint = point;
                _pinnedPointerVersion = _gestureVersion;
            }
            if (!session.Presented) return true;
            session.Pinned = _pinState.Enabled;
            bool changed = session.Move(point);
            bool escaped = !_pinState.Enabled && session.IsEscaped(point);
            if (session.ShowingExtendedPrimary)
            {
                if (_pinState.Enabled) _pinExpandedParent = session.ExtendedPrimary;
                QueueHighlightUpdate(session.ExtendedPrimary, -1, escaped, !escaped, _gestureVersion);
            }
            if (changed || (session.ControlsOuter && escaped != _lastEscapedState) ||
                (session.NeedsExtendedSecondary && !session.ShowingExtendedPrimary && !escaped))
            {
                if (session.ControlsOuter) _lastEscapedState = escaped;
                QueueFixedRender(session);
            }
            return session.ControlsOuter;
        }
    }

    private void PublishSecondaryVisuals(RadialWindow window)
    {
        lock (_uiUpdateSync)
        {
            if (_fixedSession is not { } session) return;
            session.SetShownSecondary(window.ShownSecondaryParent, window.AllShownSecondaries);
        }
    }

    private void QueueFixedRender(FixedRingSession session)
    {
        if (_fixedRenderQueued) return;
        _fixedRenderQueued = true;
        Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Render, (Action)(() => RenderFixed(session)));
    }

    private void RenderFixed(FixedRingSession session)
    {
        int secondaryToReveal = -1;
        RadialWindow? window;
        lock (_uiUpdateSync)
        {
            if (!ReferenceEquals(session, _fixedSession) || !_isGestureActive || _gestureVersion != session.Version) return;
            _fixedRenderQueued = false;
            if (_switchingFixedLayer || _debouncedFixedRelease != null) return;
            if (!session.IsCurrent(_activeProfile))
            {
                CancelGestureTracking("MenuSourceChanged");
                return;
            }
            if (session.ControlsOuter && !session.ShowingExtendedPrimary)
            {
                (_selectedSectorIndex, _selectedSubSectorIndex) = (-2, -1);
                if (_interactionSession is { } interaction)
                {
                    var (sector, sub) = session.FeedbackAddress(session.Hover);
                    interaction.Update(sector, sub, session.VisibleDepth > 2, _lastEscapedState,
                        preservePrimaryTarget: _pinState.Enabled);
                }
                _radialWindow?.SetOuterEscapeState(_lastEscapedState);
                if (session.NeedsExtendedSecondary && !_lastEscapedState)
                    secondaryToReveal = session.ExtendedPrimary;
            }
            window = _radialWindow;
            window?.UpdateFixedRings(session.Visibility, session.Hover);
        }

        if (secondaryToReveal < 0 || window == null || !ReferenceEquals(session, _fixedSession) ||
            window.PresentationVersion != session.Version || !IsCurrentGesture(session.Version) ||
            session.ExtendedPrimary != secondaryToReveal || !session.NeedsExtendedSecondary) return;
        // Reveal the old L2 ring without replacing a currently visible fixed-tier hover.
        window.RevealFixedSecondary(secondaryToReveal);
        if (ReferenceEquals(session, _fixedSession)) PublishSecondaryVisuals(window);
    }

    private bool ConfirmFixedSelection(Point point, FixedPress? down)
    {
        lock (_uiUpdateSync)
        {
            var session = _fixedSession;
            if (_switchingFixedLayer) return true;
            if (session == null) return down != null || UsesFixedRings(_activeProfile);
            if (down == null && !session.IsOuterPoint(point)) return false;
            if (!session.IsCurrent(_activeProfile) || down == null || !ReferenceEquals(down.Session, session)) return true;
            var hit = session.Hit(point);
            if (down.Hit != hit || !session.WasVisible(hit)) return true;
            if (!session.Resolve(hit, out var action) || action == null) return true;
            _mouseTriggerDown = true;
            CompleteMouseTriggerRelease(point, _pinTrigger?.MouseButton ?? "RightButton", _activeActivation);
            return true;
        }
    }
}
