using System.Windows;
using System.Windows.Media;

namespace WinPieGestures;

internal sealed partial class FixedRingGeometry
{
    private sealed class Honeycomb
    {
        // The demo uses a point-up axial grid with a 19-unit pitch radius and a 1.4-unit inset.
        private const double Pitch = 19, FaceRadius = 17.6;
        private static readonly double RootThree = Math.Sqrt(3);
        private static readonly (int Q, int R)[] Steps = [(-1, 1), (-1, 0), (0, -1), (1, -1), (1, 0), (0, 1)];
        private readonly record struct Cell(Point Center, double Angle);
        private readonly Cell[][] _shells = new Cell[4][];
        private readonly double _pitch, _face, _shift, _halfWidth;

        internal Honeycomb(double thirdInner, double scale)
        {
            // The midpoint of shell four's side is six pitch radii from the origin.
            // Honor the untouched legacy wheel even when only the new tiers shrink to fit.
            _pitch = Math.Min(Pitch * scale, thirdInner / (6 - FaceRadius / Pitch));
            _face = _pitch * FaceRadius / Pitch;
            _halfWidth = RootThree * _face / 2;
            _shift = Math.Max(0, thirdInner - (6 * _pitch - _face));
            for (int shell = 4; shell <= 7; shell++) _shells[shell - 4] = MakeShell(shell);
        }

        internal double Width => 2 * _halfWidth;
        internal double Inner(int level) => _shift + 1.5 * (level + 1) * _pitch - _face;
        internal double Outer(int level, int count) => _shift + RootThree * ShellNumber(level, count) * _pitch + _face;

        internal static double OuterExtension(int level, int count)
        {
            int shell = ShellNumber(level, count);
            return (RootThree * shell - 6) * Pitch + 2 * FaceRadius;
        }

        // The first three shells have 24/30/36 physical positions. A 37-slot fifth tier
        // evenly samples the next complete shell of 42; its cells still share one outline.
        private static int ShellNumber(int level, int count) => level == 5 && count > 36 ? 7 : level + 1;

        private Cell[] MakeShell(int shell)
        {
            var cells = new Cell[6 * shell];
            int q = shell, r = 0, i = 0;
            foreach (var step in Steps)
            {
                for (int sideIndex = 0; sideIndex < shell; sideIndex++)
                {
                    double x = RootThree * (q + r / 2.0), y = 1.5 * r;
                    double angle = Math.Atan2(y, x);
                    double factor = _pitch + _shift / Math.Sqrt(x * x + y * y);
                    cells[i++] = new Cell(new Point(x * factor, y * factor), angle < 0 ? angle + Math.Tau : angle);
                    q += step.Q;
                    r += step.R;
                }
            }
            return cells;
        }

        private Cell[] Shell(int level, int count)
        {
            if (count < 1 || count > FixedRingMenu.MaximumCount(level)) throw new ArgumentOutOfRangeException(nameof(count));
            return _shells[ShellNumber(level, count) - 4];
        }

        private static Cell At(Cell[] shell, int index, int count) => shell[index * shell.Length / count];

        internal Point Center(int level, int index, int count) => At(Shell(level, count), index, count).Center;

        internal Outline OutlineAt(int level, int index, int count)
        {
            var center = Center(level, index, count);
            double x = center.X, y = center.Y, h = _face / 2;
            Point[] points = [new(x + _halfWidth, y + h), new(x, y + _face), new(x - _halfWidth, y + h),
                new(x - _halfWidth, y - h), new(x, y - _face), new(x + _halfWidth, y - h)];
            var polygon = new StreamGeometry();
            using (var dc = polygon.Open())
            {
                dc.BeginFigure(points[0], true, true);
                for (int i = 1; i < points.Length; i++) dc.LineTo(points[i], true, false);
            }
            polygon.Freeze();
            return new Outline(polygon, points);
        }

        internal int Hit(Point point, int level, int count)
        {
            var shell = Shell(level, count);
            double angle = Math.Atan2(point.Y, point.X);
            if (angle < 0) angle += Math.Tau;
            int lo = 0, hi = count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (At(shell, mid, count).Angle < angle) lo = mid + 1;
                else hi = mid;
            }
            // An inset cell never crosses its neighbor's center ray. This holds when cells
            // are moved radially outwards or omitted, so only the two bracketing cells can hit.
            int next = lo == count ? 0 : lo;
            if (Contains(At(shell, next, count).Center, point)) return next;
            int previous = next == 0 ? count - 1 : next - 1;
            return Contains(At(shell, previous, count).Center, point) ? previous : -1;
        }

        private bool Contains(Point center, Point point)
        {
            double x = Math.Abs(point.X - center.X), y = Math.Abs(point.Y - center.Y);
            return x <= _halfWidth && y + x / RootThree <= _face;
        }
    }
}
