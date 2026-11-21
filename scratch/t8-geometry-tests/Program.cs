using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinPieGestures;

internal static class Program
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    // Exercise the real presentation entry point without showing a window or creating its HWND.
    private static void ExerciseWindow(Assembly host)
    {
        string data = Path.Combine(AppContext.BaseDirectory, "isolated", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(data);
        foreach (string key in new[] { "APPDATA", "LOCALAPPDATA", "TEMP", "TMP" })
            Environment.SetEnvironmentVariable(key, data);

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Type TypeOf(string name) => host.GetType("WinPieGestures." + name, true)!;
        object New(string name) => Activator.CreateInstance(TypeOf(name))!;
        object profile = New("WheelProfile"), branch = New("ActionItem");
        profile.GetType().GetProperty("ProcessName")!.SetValue(profile, "Global");
        var children = (IList)branch.GetType().GetProperty("SubActions")!.GetValue(branch)!;
        foreach (string name in new[] { "A", "B", "C", "D" })
        {
            object item = New("ActionItem");
            item.GetType().GetProperty("Name")!.SetValue(item, name);
            item.GetType().GetProperty("IconKey")!.SetValue(item, "Folder");
            children.Add(item);
        }
        object config = New("AppConfig");
        TypeOf("ConfigManager").GetProperty("CurrentConfig")!.SetValue(null, config);
        if (!Path.GetFullPath((string)TypeOf("ConfigManager").GetField("AppDataFolder", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!)
                .StartsWith(data + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("host configuration isolation failed");
        var type = TypeOf("RadialWindow");
        Window wheel = (Window)Activator.CreateInstance(type, new object[] { new Point(500, 480), profile })!;
        var grid = (System.Windows.Controls.Grid)wheel.FindName("MainGrid");
        int before = grid.Children.Count;
        foreach (string style in new[] { "Wheel", "Fan" })
        {
            config.GetType().GetProperty("SubmenuStyle")!.SetValue(config, style);
            var frame = type.GetMethod("ShowDeepMenu", Any)!.Invoke(wheel, new object[] { new[] { 0, 0 }, branch, "A › B" })!;
            int visible = ((IList)frame.GetType().GetProperty("Slots", Any)!.GetValue(frame)!).Count;
            if (grid.Children.Count != before + 1 || visible != (style == "Fan" ? 3 : 4))
                throw new InvalidOperationException("active visual branch mismatch");
            var overlay = (FrameworkElement)grid.Children[grid.Children.Count - 1];
            overlay.Measure(new Size(grid.Width, grid.Height));
            overlay.Arrange(new Rect(0, 0, grid.Width, grid.Height));
            type.GetMethod("HighlightDeepMenu", Any)!.Invoke(wheel, new object[] { 0 });
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(grid.Width), (int)Math.Ceiling(grid.Height), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(overlay);
            byte[] pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            if (!Array.Exists(pixels, x => x != 0)) throw new InvalidOperationException("active overlay drew no pixels");
            type.GetMethod("ClearDeepMenu", Any)!.Invoke(wheel, null);
            if (grid.Children.Count != before || wheel.IsVisible || PresentationSource.FromVisual(wheel) != null)
                throw new InvalidOperationException("stale branch or visible HWND");
        }
        Console.WriteLine("PASS actual offscreen RadialWindow Wheel/Fan presentation, drawing and clear");
    }

    [STAThread]
    private static int Main(string[] args)
    {
        var type = args.Length == 0 ? typeof(DeepMenuFrame) :
            Assembly.LoadFrom(Path.GetFullPath(args[0])).GetType("WinPieGestures.DeepMenuFrame", true)!;
        if (args.Length != 0) ExerciseWindow(type.Assembly);
        int cases = 0;
        foreach (bool fan in new[] { false, true })
        foreach (int depth in new[] { 3, 4, 5 })
        foreach (int count in new[] { 1, 2, 3, 4, 8 })
        foreach (var scale in new[] { new DpiScale(1, 1), new DpiScale(1.5, 1.25) })
        foreach (int location in new[] { 0, 1, 2 })
        foreach (double escape in new[] { 0d, 170d })
        foreach (double dir in new[] { 0d, 45, 90, 180, 270 })
        {
            // Include negative-origin monitors, mixed axis scales and a trigger against the corner.
            var origin = location switch { 1 => new Point(-1905, -894), 2 => new Point(-9, 150), _ => new Point(-980, -420) };
            var work = new Rect(-1920, -900, 1920, 1080);
            object frame = Activator.CreateInstance(type, Any, null,
                new object[] { origin, scale, work, 32d, 90d, 150d, dir, depth, count, fan, escape }, null)!;
            int Hit(Point p) => (int)type.GetMethod("HitTest", Any)!.Invoke(frame, new object[] { p })!;
            Point Screen(Point p) => new(origin.X + p.X * scale.DpiScaleX, origin.Y + p.Y * scale.DpiScaleY);
            object Get(object o, string prop) => o.GetType().GetProperty(prop, Any)!.GetValue(o)!;
            void Assert(bool ok, string msg)
            {
                if (!ok) throw new InvalidOperationException($"{msg}: fan={fan} depth={depth} n={count} dir={dir} location={location} escape={escape} dpi={scale.DpiScaleX}");
            }
            Assert(Hit(origin) == -3, "original centre still available");
            Assert(Hit(Screen(new Point(-4000, 3000))) == -1, "outside");
            var back = (Rect)Get(frame, "BackBounds");
            Point backCenter = Screen(new Point(back.Left + back.Width / 2, back.Top + back.Height / 2));
            Assert(work.Contains(backCenter) && Hit(backCenter) == -2, "back visible and reachable");
            Assert((bool)type.GetMethod("ContainsWheel", Any)!.Invoke(frame, new object[] { backCenter })!, "pinned back inside");
            if (location == 0 && escape == 0) Assert(!(bool)Get(frame, "IsPaged"), "spacious outward layout");
            if (escape > 0) Assert((bool)Get(frame, "IsPaged"), "escape threshold selects an in-place page");
            static double Radius(Rect r) => Math.Max(Math.Max(((Vector)r.TopLeft).Length, ((Vector)r.TopRight).Length),
                Math.Max(((Vector)r.BottomLeft).Length, ((Vector)r.BottomRight).Length));
            double extent = Radius(back);
            int slot = 0;
            foreach (object item in (IEnumerable)Get(frame, "Slots"))
            {
                Geometry shape = (Geometry)Get(item, "Shape");
                extent = Math.Max(extent, Radius(shape.Bounds));
                Point label = (Point)Get(item, "Label");
                Point physical = Screen(label);
                Assert(shape.IsFrozen && shape.FillContains(label), "frozen shape encloses label");
                Assert(work.Contains(physical) && Hit(physical) == slot, $"visible slot {slot}");
                if (slot == 0 && dir == 0 && depth == 5)
                {
                    var drawing = new DrawingVisual();
                    using (var dc = drawing.RenderOpen())
                    {
                        dc.PushTransform(new TranslateTransform(16 - label.X, 16 - label.Y));
                        dc.DrawGeometry(Brushes.Black, null, shape);
                        dc.Pop();
                    }
                    var target = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
                    target.Render(drawing);
                    byte[] pixel = new byte[4];
                    target.CopyPixels(new Int32Rect(16, 16, 1, 1), pixel, 4, 0);
                    Assert(pixel[3] > 0, "projected hit target renders at label");
                }
                slot++;
            }
            Assert(slot == (fan ? Math.Min(count, 3) : count), "visible slot count including imported fan overflow");
            Assert(Math.Abs(extent - (double)Get(frame, "OuterDistance")) < 1e-6, "escape envelope follows visible slots and back");
            cases++;
        }

        Console.WriteLine($"PASS {cases} branch geometries ({type.Assembly.Location})");
        return 0;
    }
}
