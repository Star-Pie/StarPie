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
        internal sealed record Cell(ActionItem? Display, FixedRingSlot? SourceSlot, int? Parent, Geometry Shape)
        {
            internal bool IsVisible(bool persistent, int level, int[] path) => persistent ||
                (level - 3 < path.Length && Display != null && Parent == path[level - 3]);
        }

        // The radial span of a petal changes with the pointer angle. Read cached line segments
        // of the actual rendered outline; a projected angle alone cannot arm a hidden or empty slot.
        private sealed class ParentOutline
        {
            private readonly Rect _bounds;
            private readonly (Point Start, Vector Edge)[] _edges;
            internal ParentOutline(Geometry shape)
            {
                _bounds = shape.Bounds;
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

            internal bool Contains(Point p, double radius) => _bounds.Contains(p) &&
                TrySpan(p, radius, out double inner, out double outer) && radius >= inner && radius <= outer;

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
        internal readonly bool PersistentFan;
        private readonly List<(int Key, ParentOutline Outline)> _fanPetals = new();
        internal readonly Cell[][] Rows;
        private readonly Dictionary<RingHit, ParentOutline> _parents = new();
        private readonly Dictionary<int, ParentOutline> _secondaryOutlines = new();
        private readonly Dictionary<int, SlotBinding> _secondaryBindings = new();
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FixedRingGeometry,
            Dictionary<(int Level, int Owner, int Index, int Count), ParentOutline>> CachedOutlines = new();
        private readonly SlotBinding[] _rootBindings;
        private readonly SlotBinding _center;
        private readonly SlotBinding[][] _bindings;
        private readonly Point _origin;
        private readonly DpiScale _dpi;
        private readonly double _trigger, _primaryRadius;
        private readonly double[] _returnRadii = new double[3];
        private RingHit _armed;
        private double _armedRadius;
        private int _trackedPrimary = -1;
        private double _trackedRadius;
        internal bool Pinned { private get; set; }
        internal int ExtendedPrimary { get; private set; } = -1;
        internal bool ShowingExtendedPrimary => ControlsOuter && Path.Length == 0 && ExtendedPrimary >= 0;
        private readonly Dictionary<int, ParentOutline> _primaryOutlines = new();
        private readonly double _legacyExtent;
        internal int ShownParent { get; private set; } = -1;
        internal bool ShowAllSecondary { get; private set; }
        internal int[] Path { get; private set; } = [];
        private volatile int[]? _presentedPath;
        internal bool Presented { get; private set; }
        internal void Present() => Presented = true;
        internal void PublishFrame(int[] path) => _presentedPath = path;
        internal RingHit Hover { get; private set; }
        internal int ActiveLevel => Path.Length + 2;
        internal int VisibleDepth => Persistent ? Rows.Length : ActiveLevel;
        internal bool ControlsOuter { get; private set; }

        internal FixedRingSession(WheelProfile profile, WheelLayer layer, long version, FixedRingGeometry geometry,
            Point origin, DpiScale dpi, double legacyExtent)
        {
            Profile = profile;
            Layer = layer;
            Menu = layer.ActiveFixedRings!;
            Persistent = Menu.ShowsAllOnPopup(ConfigManager.CurrentConfig);
            PersistentFan = Persistent && ConfigManager.CurrentConfig.SubmenuStyle == "Fan";
            Version = version;
            Revision = ConfigManager.ConfigurationRevision;
            Geometry = geometry;
            _origin = origin;
            _dpi = dpi;
            _legacyExtent = legacyExtent;
            var config = ConfigManager.CurrentConfig;
            _center = SlotBinding.Capture(profile.GetEffectiveCenterAction());
            _rootBindings = new SlotBinding[layer.SectorCount];
            Rows = new Cell[layer.EffectiveMaxMenuDepth][];
            _bindings = new SlotBinding[Rows.Length][];
            _trigger = config.SubWheelTriggerDistance > 20 ? config.SubWheelTriggerDistance : 95;
            _primaryRadius = Math.Max(1, config.WheelRadius);
            var cache = CachedOutlines.GetOrCreateValue(geometry);
            for (int level = 3; level <= Rows.Length; level++)
            {
                var row = Rows[level - 1] = new Cell[Menu.GetCount(level)];
                var bindings = _bindings[level - 1] = new SlotBinding[row.Length];
                for (int i = 0; i < row.Length; i++)
                {
                    var slot = Menu.GetSlot(level, i);
                    var bound = bindings[i] = SlotBinding.Capture(slot?.Action);
                    row[i] = new(bound.Action, slot, slot?.ParentIndex, geometry.GetShape(level, i, row.Length));
                    if (level > 3 && slot?.ParentIndex is int parent && (uint)parent < (uint)Rows[level - 2].Length)
                    {
                        var owner = Rows[level - 2][parent];
                        var address = new RingHit(level - 1, parent);
                        if (!Persistent && owner.Display != null && !_parents.ContainsKey(address))
                        {
                            var key = (level - 1, -1, parent, Rows[level - 2].Length);
                            if (!cache.TryGetValue(key, out var outline)) cache[key] = outline = new(owner.Shape);
                            _parents[address] = outline;
                        }
                    }
                }
            }
            for (int root = 0; root < layer.SectorCount; root++)
            {
                var parent = profile.GetEffectiveAction(root);
                _rootBindings[root] = SlotBinding.Capture(parent);
                int count = parent?.SubActions?.Count ?? 0;
                int visible = config.SubmenuStyle == "Fan" ? Math.Min(RadialWindow.FanSubmenuSlotCount, count) : count;
                if (PersistentFan || (!Persistent && !config.ExpandsSubRingsOnPopup && visible > 0))
                {
                    var cacheKey = (1, -1, root, layer.SectorCount);
                    if (!cache.TryGetValue(cacheKey, out var primary))
                        cache[cacheKey] = primary = new(LegacyRingGeometry.Primary(config, layer.SectorCount,
                            root, config.WheelRadius, config.InnerRadius).Shape);
                    _primaryOutlines[root] = primary;
                }
                for (int sub = 0; sub < visible; sub++)
                {
                    var child = parent!.SubActions[sub];
                    int key = FixedRingMenu.SecondaryKey(root, sub);
                    bool configured = WheelProfile.IsActionConfigured(child);
                    if (!configured && !PersistentFan) continue;
                    if (configured) _secondaryBindings[key] = SlotBinding.Capture(child);

                    var cacheKey = (2, root, sub, count);
                    if (!cache.TryGetValue(cacheKey, out var outline))
                    {
                        var (shape, _) = LegacyRingGeometry.Secondary(config, layer.SectorCount, root, sub, count,
                            config.WheelRadius, config.InnerRadius);
                        cache[cacheKey] = outline = new(shape);
                    }
                    if (PersistentFan) _fanPetals.Add((key, outline));
                    if (!configured) continue;
                    _secondaryOutlines[key] = outline;
                    if (!Persistent && Array.Exists(Rows[2], c => c.Display != null && c.Parent == key))
                        _parents[new(2, key)] = outline;
                }
            }
        }

        private Point Relative(Point point) => new((point.X - _origin.X) / _dpi.DpiScaleX, (point.Y - _origin.Y) / _dpi.DpiScaleY);
        internal bool IsOuterPoint(Point point) => ((Vector)Relative(point)).Length > _legacyExtent;
        internal bool IsCurrent(WheelProfile? profile) => ReferenceEquals(profile, Profile) &&
            ReferenceEquals(CaptureReleaseLayer(profile), Layer) && ReferenceEquals(Layer.ActiveFixedRings, Menu) &&
            Revision == ConfigManager.ConfigurationRevision && ConfigManager.CurrentConfig.EnableMultiTier &&
            Rows.Length == Layer.EffectiveMaxMenuDepth && Persistent == Menu.ShowsAllOnPopup(ConfigManager.CurrentConfig) && _rootBindings.Length == Layer.SectorCount;

        internal bool SetShownSecondary(int parent, bool all)
        {
            if (ShownParent == parent && all == ShowAllSecondary) return false;
            ShownParent = parent;
            ShowAllSecondary = all;
            if (_armed.Level == 2 && !all && FixedRingMenu.TryDecodeSecondaryKey(_armed.Index, out int root, out _) && root != parent)
                _armed = default;
            return true;
        }

        internal bool WasVisible(RingHit hit)
        {
            var rendered = _presentedPath;
            return Presented && rendered != null && hit.IsOuter &&
                Rows[hit.Level - 1][hit.Index].IsVisible(Persistent, hit.Level, rendered);
        }

        internal RingHit Hit(Point point)
        {
            var local = Relative(point);
            for (int level = 3; level <= VisibleDepth; level++)
            {
                int index = Geometry.Hit(local, level, Rows[level - 1].Length);
                if (index >= 0 && Rows[level - 1][index].IsVisible(Persistent, level, Path)) return new(level, index);
            }
            return default;
        }

        internal bool Eligible(RingHit hit) => hit.IsOuter && (Persistent || hit.Level == ActiveLevel ||
            (hit.Level - 2 < Path.Length && Path[hit.Level - 2] == hit.Index)) &&
            Rows[hit.Level - 1][hit.Index] is { Display: not null } cell && (Persistent || cell.Parent == Path[hit.Level - 3]);

        private double Threshold(int level, double inner, double outer)
        {
            if (_trigger <= _primaryRadius)
                return inner + (outer - inner) * _trigger / _primaryRadius;
            double nextMiddle = (Geometry.Inner(level + 1) + Geometry.OuterForCount(level + 1, Rows[level].Length)) / 2;
            double fraction = Math.Clamp((_trigger - _primaryRadius) / (Math.Max(260, _trigger) - _primaryRadius), 0, 1);
            return outer + Math.Max(0, nextMiddle - outer) * fraction;
        }

        private bool CheckDistance(RingHit hit, Point local, double radius, bool mayArm)
        {
            if (!Presented || (hit.IsOuter && !WasVisible(hit)) || hit.Level != ActiveLevel || !_parents.TryGetValue(hit, out var outline) ||
                !outline.TrySpan(local, radius, out double inner, out double outer))
            {
                _armed = default;
                return false;
            }
            bool inside = radius >= inner && radius <= outer;
            if (!(inside && mayArm) && !(_armed == hit && radius >= _armedRadius && radius >= outer))
            {
                _armed = default;
                return false;
            }
            _armed = hit;
            _armedRadius = radius;
            double threshold = Threshold(hit.Level, inner, outer);
            if (radius + 1e-7 < threshold) return false;
            // A move into the new ring must remain selectable even when a large global
            // distance required crossing its parent's outer edge first.
            Open(hit, Math.Max(inner, Math.Min(outer, threshold) - .12 * (outer - inner)));
            return true;
        }

        internal bool AimSecondary(Point point, int root, int sub)
        {
            if (Persistent || Path.Length != 0 || ControlsOuter || !Presented) return false;
            int key = root >= 0 && sub >= 0 ? FixedRingMenu.SecondaryKey(root, sub) : -1;
            var hit = new RingHit(2, key);
            if (!(ShowAllSecondary || root == ShownParent) || !_parents.ContainsKey(hit))
            {
                // Legacy hit testing can return no slot between a short Fan petal and its
                // complete radial envelope. Keep a continuous outward projection in that gap.
                if (sub >= 0 || (root >= 0 && root != ShownParent && !ShowAllSecondary)) _armed = default;
                return false;
            }
            var local = Relative(point);
            return CheckDistance(hit, local, ((Vector)local).Length, true);
        }

        private bool TryHitPersistentPetal(Point point, int lockedParent, out int root, out int sub)
        {
            root = sub = -1;
            if (!PersistentFan || !Presented || !ShowAllSecondary) return false;
            var local = Relative(point);
            double radius = ((Vector)local).Length;
            // Draw order is also the pinned hit order where neighboring parents have overlapping petals.
            for (int i = _fanPetals.Count - 1; i >= 0; --i)
            {
                var petal = _fanPetals[i];
                if (!petal.Outline.Contains(local, radius)) continue;
                return FixedRingMenu.TryDecodeSecondaryKey(petal.Key, out root, out sub) &&
                    (lockedParent < 0 || root == lockedParent);
            }
            return false;
        }

        internal RingHit HitPersistentFanInner(Point point, SubmenuLockTracker parentLock)
        {
            if (!PersistentFan || !Presented) return default;
            var local = Relative(point);
            double radius = ((Vector)local).Length;
            var config = ConfigManager.CurrentConfig;
            bool lockEnabled = config.LockParentSectorOnSubmenu && !Pinned;
            parentLock.ResolveParentSector(lockEnabled, true, true, radius, -1, _primaryRadius, config);
            if (TryHitPersistentPetal(point, parentLock.LockedParentSector, out int parent, out int child))
            {
                int key = FixedRingMenu.SecondaryKey(parent, child);
                if (lockEnabled && _secondaryBindings.ContainsKey(key)) parentLock.Lock(parent);
                return new(2, key);
            }
            if (parentLock.IsLocked) return default;
            double deadzone = ConfigManager.CurrentConfig.GetEffectiveCoreDeadzone();
            if (radius < (deadzone > 0 ? deadzone : 15)) return new(0, -1);
            if (radius > _primaryRadius) return default;
            double turn = Math.Atan2(local.Y, local.X) / (2 * Math.PI);
            int count = _rootBindings.Length;
            int index = (int)Math.Floor((turn + 1) * count + .5) % count;
            return _primaryOutlines.TryGetValue(index, out var outline) && outline.Contains(local, radius)
                ? new(1, index) : default;
        }

        internal bool CanConfirmSecondary(Point point, int root, int sub)
        {
            if (root < 0 || sub < 0 || !(ShowAllSecondary || root == ShownParent)) return false;
            var hit = new RingHit(2, FixedRingMenu.SecondaryKey(root, sub));
            if (Path.Length > 0 && Path[0] != hit.Index) return false;
            if (!_secondaryOutlines.TryGetValue(hit.Index, out var outline)) return false;
            var local = Relative(point);
            double radius = ((Vector)local).Length;
            return outline.TrySpan(local, radius, out double inner, out double outer) && radius >= inner && radius <= outer;
        }

        private void TrackExtendedPrimary(Point local, double radius)
        {
            if (Persistent || _primaryOutlines.Count == 0 || (!Pinned && _trigger <= _legacyExtent) || ShowAllSecondary || Path.Length != 0) return;
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
            _lastPointer = point;
            var local = Relative(point);
            double radius = ((Vector)local).Length;
            TrackExtendedPrimary(local, radius);
            var previous = Path;
            var oldHover = Hover;
            bool outer = radius > _legacyExtent;
            bool changed = outer != ControlsOuter;
            ControlsOuter = outer;
            while (Path.Length > 0 && radius < _returnRadii[Path.Length - 1])
            {
                Path = Path[..^1];
                _armed = default;
            }
            var hit = outer ? Hit(point) : default;
            Hover = Eligible(hit) ? hit : default;
            if (!Persistent)
            {
                if (Hover.IsOuter && Hover.Level == ActiveLevel)
                    CheckDistance(Hover, local, radius, true);
                else if (_armed.Level != 0)
                {
                    // A secondary still inside the legacy envelope receives the authoritative
                    // Wheel/Fan selection below. Outside its shape continue only on the same ray corridor.
                    bool inLegacyParent = _armed.Level == 2 && !outer && _parents.TryGetValue(_armed, out var outline) &&
                        outline.TrySpan(local, radius, out double inner, out double edge) && radius >= inner && radius <= edge;
                    if (!inLegacyParent) CheckDistance(_armed, local, radius, false);
                }
            }
            return changed || !ReferenceEquals(previous, Path) || oldHover != Hover;
        }

        internal bool Enter(RingHit hit)
        {
            if (!Presented || Persistent || hit.Level != ActiveLevel || !_parents.ContainsKey(hit) ||
                (hit.IsOuter && (!WasVisible(hit) || !Eligible(hit)))) return false;
            // An explicit pinned click opens a navigation-only parent at any distance.
            var local = Relative(_lastPointer);
            double radius = ((Vector)local).Length;
            double exit = _parents[hit].TrySpan(local, radius, out double inner, out _) ?
                inner : hit.Level == 2 ? _primaryRadius : Geometry.Inner(hit.Level);
            Open(hit, exit);
            return true;
        }

        private void Open(RingHit hit, double exit)
        {
            _returnRadii[hit.Level - 2] = exit;
            Path = AppendMenuSlot(Path, hit.Index);
            _armed = default;
        }

        private Point _lastPointer;

        internal bool IsEscaped(Point point)
        {
            var config = ConfigManager.CurrentConfig;
            double outer = VisibleDepth >= 3 ? Geometry.OuterForCount(VisibleDepth, Rows[VisibleDepth - 1].Length) : _legacyExtent;
            double limit = Math.Max(outer + 1, config.OuterEscapeDistance > 0 ? config.OuterEscapeDistance : outer * 1.5);
            if (ShowingExtendedPrimary) limit = Math.Max(limit, _trigger + 2);
            return config.EnableOuterEscapeCancel && ((Vector)Relative(point)).Length > limit;
        }

        internal (int sector, int sub) Target(RingHit hit)
        {
            if (!Eligible(hit)) return (-2, -1);
            int index = hit.Index;
            for (int level = hit.Level; level >= 3; level--)
            {
                if (Rows[level - 1][index].Parent is not int parent || parent < 0) return (-2, -1);
                if (level == 3) return _secondaryBindings.ContainsKey(parent) &&
                    FixedRingMenu.TryDecodeSecondaryKey(parent, out int root, out int sub) ? (root, sub) : (-2, -1);
                if ((uint)parent >= (uint)Rows[level - 2].Length || Rows[level - 2][parent].Display == null) return (-2, -1);
                index = parent;
            }
            return (-2, -1);
        }

        private bool BindingCurrent(int level, int i)
        {
            var row = Rows[level - 1];
            if (Menu.GetCount(level) != row.Length) return false;
            var slot = Menu.GetSlot(level, i);
            return slot != null && ReferenceEquals(row[i].SourceSlot, slot) && slot.ParentIndex == row[i].Parent &&
                _bindings[level - 1][i].Matches(slot.Action, false);
        }

        private bool SecondaryCurrent(int key)
        {
            if (!_secondaryBindings.TryGetValue(key, out var binding)) return false;
            if (!FixedRingMenu.TryDecodeSecondaryKey(key, out int root, out int sub)) return false;
            var action = Profile.GetEffectiveAction(root);
            bool inherited = _rootBindings[root].Action?.IsInherited == true;
            return inherited == (action?.IsInherited == true) && _rootBindings[root].Matches(action, inherited) &&
                action?.SubActions is { } children && sub < children.Count && binding.Matches(children[sub], inherited);
        }

        internal bool Resolve(RingHit hit, out ActionItem? action)
        {
            action = null;
            if (!IsCurrent(Profile)) return false;
            if (hit.Level is 0 or 1 or 2)
            {
                if (hit.Level == 0 && hit.Index != -1) return true;
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
                    current = hit.Level == 0 ? Profile.GetEffectiveCenterAction() : Profile.GetEffectiveAction(hit.Index);
                    inherited = binding.Action?.IsInherited == true;
                    if (binding.Action == null && current == null) return true;
                    if (inherited != (current?.IsInherited == true) || !binding.Matches(current, inherited)) return false;
                }
                if (IsDeepMenuLeafAvailable(binding.Action)) action = binding.Action;
                return true;
            }
            if (!Eligible(hit)) return true;
            // Independent or already-hidden owners remain valid direct targets. If an owner was
            // present when shown, any replacement, reordering or removal invalidates this release.
            int index = hit.Index;
            for (int level = hit.Level; level >= 3; level--)
            {
                if (!BindingCurrent(level, index)) return false;
                if (Rows[level - 1][index].Parent is not int parent || parent < 0) break;
                if (level == 3)
                {
                    if (_secondaryBindings.ContainsKey(parent) && !SecondaryCurrent(parent)) return false;
                    break;
                }
                if ((uint)parent >= (uint)Rows[level - 2].Length || Rows[level - 2][parent].Display == null) break;
                index = parent;
            }
            var saved = _bindings[hit.Level - 1][hit.Index].Action;
            if (IsDeepMenuLeafAvailable(saved)) action = saved;
            return true;
        }
    }
    private sealed record FixedRelease(FixedRingSession Session, RingHit Hit);
    private sealed record FixedPress(FixedRingSession Session, int[] Path, RingHit Hit);
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
            if (_switchingFixedLayer || _fixedSession != null || UsesFixedRings(profile) ||
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
            if (changed || (session.ControlsOuter && escaped != _lastEscapedState))
            {
                if (session.ControlsOuter) _lastEscapedState = escaped;
                QueueFixedRender(session);
            }
            return session.ControlsOuter;
        }
    }

    private void AimFixedSecondary(Point point, int sector, int sub)
    {
        if (_fixedSession == null) return;
        lock (_uiUpdateSync)
            if (_fixedSession is { } session && session.AimSecondary(point, sector, sub))
                QueueFixedRender(session);
    }

    private void PublishSecondaryVisuals(RadialWindow window)
    {
        lock (_uiUpdateSync)
        {
            if (_fixedSession is not { } session) return;
            bool changed = session.SetShownSecondary(window.ShownSecondaryParent, window.AllShownSecondaries);
            if (session.AimSecondary(_lastMovePoint, _selectedSectorIndex, _selectedSubSectorIndex) || changed)
                QueueFixedRender(session);
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
                (_selectedSectorIndex, _selectedSubSectorIndex) = session.Target(session.Hover);
                _interactionSession?.Update(_selectedSectorIndex, _selectedSubSectorIndex, session.VisibleDepth > 2, _lastEscapedState,
                    preservePrimaryTarget: _pinState.Enabled);
                _radialWindow?.SetOuterEscapeState(_lastEscapedState);
            }
            _radialWindow?.UpdateFixedRings(session.Path, session.Hover);
        }
    }

    private bool ConfirmFixedSelection(Point point, FixedPress? down)
    {
        lock (_uiUpdateSync)
        {
            var session = _fixedSession;
            if (_switchingFixedLayer) return true;
            if (session == null) return down != null || UsesFixedRings(_activeProfile);
            if (down == null && !session.IsOuterPoint(point)) return false;
            if (!session.IsCurrent(_activeProfile) || down == null || !ReferenceEquals(down.Session, session) ||
                !ReferenceEquals(down.Path, session.Path)) return true;
            var hit = session.Hit(point);
            if (down.Hit != hit || !session.WasVisible(hit)) return true;
            if (!session.Resolve(hit, out var action)) return true;
            if (action == null)
            {
                if (session.Enter(hit)) QueueFixedRender(session);
                return true;
            }
            _mouseTriggerDown = true;
            CompleteMouseTriggerRelease(point, _pinTrigger?.MouseButton ?? "RightButton", _activeActivation);
            return true;
        }
    }
}
