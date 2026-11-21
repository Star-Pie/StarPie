using System.Windows;
using System.Windows.Media;

namespace WinPieGestures;

internal sealed partial class FixedRingGeometry
{
    private sealed class Honeycomb
    {
        private readonly string _shape;
        private readonly Outline _hitFace;
        private readonly double _corner;
        private static readonly double RootThree = Math.Sqrt(3);
        private static readonly (int Q, int R)[] Steps = [(-1, 1), (-1, 0), (0, -1), (1, -1), (1, 0), (0, 1)];
        private readonly record struct Cell(Point Center, double Angle);
        private readonly Cell[] _roots, _children;
        private readonly Cell[][] _outerShells;
        private readonly double[] _inner = new double[6], _outer = new double[6];
        private readonly double _pitch, _face, _halfWidth;

        internal Honeycomb(int roots, double pitch, double face, string shape, double corner)
        {
            _pitch = pitch;
            _face = face;
            _shape = shape;
            _corner = corner;
            _halfWidth = RootThree * _face / 2;
            _hitFace = MakeFace(new Point());
            // Reserve addresses independently of occupancy. Even twelve parents get three distinct
            // cells each in their own angular interval on the two intermediate shells.
            int rootShell = Math.Max(2, (roots + 5) / 6);
            int childShell = ChildShell(roots);
            var primary = MakeShell(rootShell);
            var children = MakeShell(childShell).Concat(MakeShell(childShell + 1)).ToArray();
            _roots = new Cell[roots];
            _children = new Cell[3 * roots];
            double sector = Math.Tau / roots;
            for (int i = 0; i < roots; i++)
            {
                double angle = i * sector;
                _roots[i] = primary.MinBy(cell => Math.Abs(Math.IEEERemainder(cell.Angle - angle, Math.Tau)));
                var candidates = children.Where(cell =>
                    (int)Math.Floor(cell.Angle / sector + .5) % roots == i).ToList();
                // Choose the middle first, then the two wings. A single child keeps the same
                // address when its siblings are added, even when all parents open together.
                foreach (int slot in new[] { 1, 0, 2 })
                {
                    double target = angle + (slot - 1) * sector / 3;
                    var best = candidates.OrderBy(cell => Math.Round(Math.Abs(Math.IEEERemainder(cell.Angle - target, Math.Tau)), 12))
                        .ThenByDescending(cell => ((Vector)cell.Center).LengthSquared).First();
                    _children[i * 3 + slot] = best;
                    candidates.Remove(best);
                }
            }
            _outerShells = Enumerable.Range(childShell + 2, 3).Select(MakeShell).ToArray();
            for (int level = 1; level <= 5; level++)
            {
                var cells = LevelCells(level);
                _inner[level] = cells.Min(cell => ((Vector)cell.Center).Length) - _face;
                _outer[level] = cells.Max(cell => ((Vector)cell.Center).Length) + _face;
            }
        }

        internal double Width => 2 * _halfWidth;
        internal double Inner(int level) => _inner[level];
        internal double Outer(int level) => _outer[level];
        private static int ChildShell(int roots) => roots <= 12 ? 3 : roots;

        internal static double Extent(int roots, int level, double pitch, double face) =>
            RootThree * (ChildShell(roots) + level - 1) * pitch + face;

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
                    cells[i++] = new(new Point(x * _pitch, y * _pitch), angle < 0 ? angle + Math.Tau : angle);
                    q += step.Q;
                    r += step.R;
                }
            }
            return cells;
        }

        private Cell[] LevelCells(int level) => level switch { 1 => _roots, 2 => _children, _ => _outerShells[level - 3] };
        private static Cell At(Cell[] cells, int index, int count) => cells[index * cells.Length / count];
        internal Point Center(int level, int index, int count) => At(LevelCells(level), index, count).Center;

        internal Outline OutlineAt(int level, int index, int count) => MakeFace(Center(level, index, count));

        private Outline MakeFace(Point center)
        {
            Geometry shape = _shape switch
            {
                "Circle" => new EllipseGeometry(center, _halfWidth, _halfWidth),
                "RoundedCapsule" or "FloatingCapsules" or "RoundedRect" or "Capsule" =>
                    new RectangleGeometry(new Rect(center.X - _halfWidth, center.Y - _face * .5, Width, _face),
                        Math.Min(_face * .5, _corner), Math.Min(_face * .5, _corner)),
                _ => IconHelper.CreateHexagonGeometry(center.X, center.Y, _face, _corner, 90)
            };
            return Flatten(shape, _pitch / 2000);
        }

        internal int Hit(Point point, int level, int count)
        {
            var cells = LevelCells(level);
            for (int i = 0; i < count; i++)
            {
                var center = At(cells, i, count).Center;
                double x = Math.Abs(point.X - center.X), y = Math.Abs(point.Y - center.Y);
                if (x <= _halfWidth && y + x / RootThree <= _face)
                    return _hitFace.Contains(new Point(point.X - center.X, point.Y - center.Y)) ? i : -1;
            }
            return -1;
        }
    }
}
