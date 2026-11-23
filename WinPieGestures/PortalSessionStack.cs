using System.Windows;
using System.Windows.Media;

namespace WinPieGestures;

internal sealed record PortalFrame(PortalLayerAddress Address, Point PhysicalCenter, double OuterRadius,
    Point AnchorPhysicalPoint, double CoreRadius = 30.0, ImageSource? ParentSnapshot = null, double SnapshotRadiusDip = 0);

internal enum PortalBackInput { Miss, Center, Blank, RightButton, Backspace, Escape, ParentCenter }
internal enum PortalBackDecision { Close, Pop }
internal enum PortalPointerHandoff { Keep, Warp, Pin }

// The presentation Dispatcher owns the frames. Input hooks only submit points and commands.
internal sealed class PortalSessionStack
{
    private readonly List<PortalFrame> _frames;
    private readonly int _maxDepth;
    internal IReadOnlyList<PortalFrame> Frames => _frames;
    internal PortalFrame Current => _frames[^1];
    internal int Depth => _frames.Count;
    internal long Revision { get; }
    internal WheelFocusSession? Focus { get; }
    internal object? BlockedSlot { get; private set; }

    internal PortalSessionStack(PortalFrame root, long revision, int maxDepth, WheelFocusSession? focus)
    {
        _maxDepth = maxDepth;
        Revision = revision;
        Focus = focus;
        _frames = new(maxDepth) { root };
    }

    internal bool CanPush(PortalLayerAddress address)
    {
        if (Depth >= _maxDepth) return false;
        foreach (var frame in _frames)
            if (frame.Address == address) return false;
        return true;
    }

    internal void Push(PortalFrame frame) => _frames.Add(frame);
    internal void SetCurrentExtent(double radius) => _frames[^1] = Current with { OuterRadius = radius };
    internal void AttachCurrentVisual(double coreRadius, ImageSource? snapshot, double snapshotRadiusDip) =>
        _frames[^1] = Current with { CoreRadius = coreRadius, ParentSnapshot = snapshot, SnapshotRadiusDip = snapshotRadiusDip };

    internal void Pop(object slot, bool guard)
    {
        _frames.RemoveAt(_frames.Count - 1);
        BlockedSlot = guard ? slot : null;
    }

    internal bool AcceptPointerSlot(object? slot)
    {
        if (BlockedSlot == null) return true;
        if (Equals(BlockedSlot, slot)) return false;
        BlockedSlot = null;
        return true;
    }

    internal PortalBackDecision Back(PortalBackInput input, bool escapeClosesAll) =>
        Depth == 1 || (input == PortalBackInput.Escape && escapeClosesAll)
            ? PortalBackDecision.Close : PortalBackDecision.Pop;

    internal bool MatchesRevision(long revision) => Revision == revision;
}

internal static class PortalPlacement
{
    internal static PortalPointerHandoff Handoff(bool releasedOrPinned, bool outer, bool autoPin, bool beyondDeadzone)
    {
        if (releasedOrPinned || outer && autoPin) return PortalPointerHandoff.Pin;
        return outer || beyondDeadzone ? PortalPointerHandoff.Warp : PortalPointerHandoff.Keep;
    }

    internal static bool Crossed(double radius, double outer, AppConfig config) =>
        config.PortalEnableCrossExpand && outer > 0 && radius >= outer * config.EffectivePortalCrossExpandRatio;

    internal static double Radius(Point origin, Point point, DpiScale dpi)
    {
        double x = (point.X - origin.X) / dpi.DpiScaleX;
        double y = (point.Y - origin.Y) / dpi.DpiScaleY;
        return Math.Sqrt(x * x + y * y);
    }

    internal static Point Physical(Point origin, Vector dip, DpiScale dpi) =>
        new(origin.X + dip.X * dpi.DpiScaleX, origin.Y + dip.Y * dpi.DpiScaleY);

    internal static bool TryPlace(Point parent, Vector anchor, double parentRadius, double childRadius,
        double gap, bool tangent, Rect workArea, DpiScale dpi, out Point center)
    {
        var allowed = workArea;
        double inset = tangent ? childRadius : Math.Min(childRadius, Math.Min(
            workArea.Width / dpi.DpiScaleX, workArea.Height / dpi.DpiScaleY) / 2);
        allowed.Inflate(-inset * dpi.DpiScaleX, -inset * dpi.DpiScaleY);
        center = parent;
        if (allowed.IsEmpty) return false;
        if (!tangent)
        {
            center = Clamp(Physical(parent, anchor, dpi), allowed);
            return true;
        }
        if (anchor.LengthSquared <= 0) return false;

        double distance = parentRadius + childRadius + gap;
        double angle = Math.Atan2(anchor.Y, anchor.X);
        for (int step = 0; step <= 12; step++)
        {
            double delta = step * Math.PI / 12;
            if (TryDirection(angle + delta, out center) || (step > 0 && TryDirection(angle - delta, out center)))
                return true;
        }

        // Narrow feasible wedges can lie between the discrete directions. Intersect the ray
        // to the furthest feasible corner with the tangent circle before giving up.
        Point corner = new(parent.X < allowed.Left + allowed.Width / 2 ? allowed.Right : allowed.Left,
            parent.Y < allowed.Top + allowed.Height / 2 ? allowed.Bottom : allowed.Top);
        return TryDirection(Math.Atan2((corner.Y - parent.Y) / dpi.DpiScaleY,
            (corner.X - parent.X) / dpi.DpiScaleX), out center);

        bool TryDirection(double direction, out Point placed)
        {
            placed = Clamp(Physical(parent, new(Math.Cos(direction) * distance, Math.Sin(direction) * distance), dpi), allowed);
            return Radius(parent, placed, dpi) >= distance - .001;
        }
    }

    private static Point Clamp(Point value, Rect rect) =>
        new(Math.Clamp(value.X, rect.Left, rect.Right), Math.Clamp(value.Y, rect.Top, rect.Bottom));
}

// A fired timer is a request, not permission to act: the UI must still check the
// same generation, latest pointer slot, and configuration revision.
internal sealed class PortalDwell : IDisposable
{
    private readonly record struct Ticket(PortalDwell Owner, long Generation);
    private readonly Action<long> _elapsed;
    private System.Threading.Timer? _timer;
    private long _generation;

    internal PortalDwell(Action<long> elapsed) => _elapsed = elapsed;
    internal long Arm(int delayMs)
    {
        Dispose();
        long generation = Volatile.Read(ref _generation);
        _timer = new System.Threading.Timer(static state =>
        {
            var ticket = (Ticket)state!;
            if (ticket.Owner.IsCurrent(ticket.Generation)) ticket.Owner._elapsed(ticket.Generation);
        }, new Ticket(this, generation), delayMs, Timeout.Infinite);
        return generation;
    }
    internal bool IsCurrent(long generation) => Volatile.Read(ref _generation) == generation;
    public void Dispose()
    {
        Interlocked.Increment(ref _generation);
        _timer?.Dispose();
        _timer = null;
    }
}
