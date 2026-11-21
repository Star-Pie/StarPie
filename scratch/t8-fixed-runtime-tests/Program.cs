using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinPieGestures;

internal static partial class Program
{
    const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Type Controller = typeof(GestureController);
    static readonly Assembly Host = Controller.Assembly;
    static readonly Point Origin = new(-820, 300);
    static readonly DpiScale Dpi = new(1.5, 1.25);
    static readonly List<ActionItem> Recorded = [];
    static double legacyExtent, outerScale;
    static object currentGeometry = null!;
    static int checks;
    static readonly WheelLayer Layer = new() { SectorCount = 4, MaxMenuDepth = 5, Actions = [Text("east"), Text("south")] };
    static readonly WheelProfile Profile = new() { ProcessName = "Global", Layers = [Layer] };

    static ActionItem Text(string name) => new() { Name = name, Type = "Text", Parameter = name };
    static void Check(bool pass, string name)
    {
        if (!pass) throw new InvalidOperationException(name);
        Console.WriteLine("PASS " + name);
        checks++;
    }
    static object New(Type type, params object?[] args) => Activator.CreateInstance(type, Members, null, args, null)!;
    static object? Call(object target, string method, params object?[] args) =>
        target.GetType().GetMethod(method, Members)!.Invoke(target, args);
    static object? Get(object obj, string name) => obj.GetType().GetProperty(name, Members)!.GetValue(obj);
    static void Field(object obj, string key, object? value) => obj.GetType().GetField(key, Members)!.SetValue(obj, value);
    static Type Nested(string key) => Controller.GetNestedType(key, BindingFlags.NonPublic)!;

    static object Session(bool persistent, bool publish = true, bool navigationOnly = false, int shownParent = 0, bool allSecondary = false, double scale = 1)
    {
        var menu = Layer.FixedRings = new FixedRingMenu { AlwaysVisible = persistent };
        for (int level = 3, parent = FixedRingMenu.SecondaryKey(0, 0); level <= 5; level++)
        {
            int i = level + 1;
            menu.SetSlot(level, i, new() { Action = Text("tier-" + level), ParentIndex = parent });
            menu.Tiers[level - 3]!.DwellMs = 600;
            menu.Tiers[level - 3]!.WakeBandWidth = 24;
            parent = i;
        }
        if (navigationOnly)
            menu.SetSlot(3, 4, new() { ParentIndex = FixedRingMenu.SecondaryKey(0, 0),
                Action = new() { Type = "Hotkey", Parameter = "", Name = "navigation only" } });
        menu.SetSlot(3, 1, new() { Action = Text("other root"), ParentIndex = FixedRingMenu.SecondaryKey(1, 0) });
        menu.SetSlot(3, 7, new() { Action = Text("fifth child"), ParentIndex = FixedRingMenu.SecondaryKey(0, 4) });
        menu.SetSlot(5, 10, new() { Action = Text("independent") });
        menu.SetSlot(5, 11, new() { Action = new() { Type = "Plugin", Name = "missing",
            PluginActionRef = new() { PluginId = "fixture.missing", ContributionId = "missing" } } });
        legacyExtent = (double)Host.GetType("WinPieGestures.LegacyRingGeometry")!
            .GetMethod("SecondaryExtent", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [ConfigManager.CurrentConfig, ConfigManager.CurrentConfig.WheelRadius, ConfigManager.CurrentConfig.InnerRadius])!;
        outerScale = scale;
        Type geometryType = Host.GetType("WinPieGestures.FixedRingGeometry")!;
        var geometry = currentGeometry = ConfigManager.CurrentConfig.SubmenuStyle == "Fan"
            ? New(geometryType, 4, ConfigManager.CurrentConfig.WheelRadius, legacyExtent + 5 * scale, scale, true)
            : New(geometryType, 4, ConfigManager.CurrentConfig.WheelRadius, legacyExtent + 5 * scale, scale);
        var session = New(Nested("FixedRingSession"), Profile, Layer, 18L, geometry, Origin, Dpi, legacyExtent);
        Call(session, "SetShownSecondary", shownParent, allSecondary);
        if (publish)
        {
            Call(session, "Present");
            PaintFrame(session);
        }
        return session;
    }

    static Point At(int level, int index, bool band = false)
    {
        if (level < 3) throw new ArgumentOutOfRangeException(nameof(level));
        int count = Layer.FixedRings!.GetCount(level);
        if (ConfigManager.CurrentConfig.SubmenuStyle == "Fan")
        {
            Point center = (Point)Call(currentGeometry, "GetCenter", level, index, count)!;
            if (!band) return Physical(center);
            var shape = (Geometry)Call(currentGeometry, "GetShape", level, index, count)!;
            var ray = (Vector)center;
            ray.Normalize();
            Point edge = center;
            while (shape.FillContains(edge + ray * .2)) edge += ray * .2;
            return Physical(center + (edge - center) * .9);
        }
        double angle = 2 * Math.PI * index / count;
        double r = legacyExtent + outerScale * (5 + 44 * (level - 3) + (band ? 36 : 19));
        return new(Origin.X + r * Math.Cos(angle) * Dpi.DpiScaleX, Origin.Y + r * Math.Sin(angle) * Dpi.DpiScaleY);
    }
    static Point SecondaryPoint(int root = 0, int sub = 0, bool band = true)
    {
        var pair = ConfigManager.CurrentConfig.SubmenuStyle == "Fan"
            ? ((Geometry Shape, Point Label))Call(currentGeometry, "Secondary", ConfigManager.CurrentConfig, root, sub, Layer.Actions[root].SubActions.Count)!
            : ((Geometry Shape, Point Label))Host.GetType("WinPieGestures.LegacyRingGeometry")!
            .GetMethod("Secondary", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null,
                [ConfigManager.CurrentConfig, 4, root, sub, Layer.Actions[root].SubActions.Count, ConfigManager.CurrentConfig.WheelRadius, ConfigManager.CurrentConfig.InnerRadius])!;
        var ray = (Vector)pair.Label;
        ray.Normalize();
        double inner = double.PositiveInfinity, outer = 0;
        for (double radius = 1; radius <= ((Vector)pair.Label).Length + pair.Shape.Bounds.Height + 2; radius += .1)
        {
            if (!pair.Shape.FillContains((Point)(radius * ray))) continue;
            inner = Math.Min(inner, radius);
            outer = radius;
        }
        if (outer <= inner) throw new InvalidOperationException("Missing rendered secondary contour");
        return Physical((Point)(ray * (inner + (outer - inner) * (band ? .94 : .50))));
    }

    static Point Physical(Point p) => new(Origin.X + p.X * Dpi.DpiScaleX, Origin.Y + p.Y * Dpi.DpiScaleY);
    static Point Along(Point p, double radius)
    {
        var ray = new Vector((p.X - Origin.X) / Dpi.DpiScaleX, (p.Y - Origin.Y) / Dpi.DpiScaleY);
        ray.Normalize();
        return Physical((Point)(ray * radius));
    }
    static void PaintFrame(object session) => Call(session, "PublishFrame", Get(session, "Path"));

    static object Hit(object session, Point point) => Call(session, "Hit", point)!;
    static (bool Valid, ActionItem? Action) Resolve(object session, object hit)
    {
        object?[] args = [hit, null];
        return ((bool)Call(session, "Resolve", args)!, (ActionItem?)args[1]);
    }

    static object Harness(object session, WheelProfile? profile = null)
    {
        profile ??= Profile;
        // No input hooks, foreground handles, plugin instances, windows or real action queue.
        var controller = RuntimeHelpers.GetUninitializedObject(Controller);
        foreach (string key in new[] { "_longPressLock", "_uiUpdateSync", "_mouseReleaseDebounceLock" })
            Field(controller, key, new object());
        Field(controller, "_submenuLock", new SubmenuLockTracker());
        Field(controller, "_pinState", New(Host.GetType("WinPieGestures.PinnedWheelState")!));
        Field(controller, "_pinExpandedParent", -1);
        Field(controller, "_activeProfile", profile);
        Field(controller, "_fixedSession", session);
        Field(controller, "_startPoint", Origin);
        Field(controller, "_currentDpiScaleX", Dpi.DpiScaleX);
        Field(controller, "_currentDpiScaleY", Dpi.DpiScaleY);
        Field(controller, "_selectedSectorIndex", -1);
        Field(controller, "_selectedSubSectorIndex", -1);
        Call(controller, "PrepareInputRoots", profile);
        ((SubmenuLockTracker)Controller.GetField("_submenuLock", Members)!.GetValue(controller)!).PrepareGeometry(ConfigManager.CurrentConfig, profile);
        Field(controller, "_gestureVersion", 18L);
        Field(controller, "_isGestureActive", true);
        Controller.GetProperty("TestActionEnqueued", Members)!.SetValue(controller, (Action<ActionItem>)Recorded.Add);
        return controller;
    }

    static void Pump(int ms = 10)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static HashSet<(int, int)> PaintedSlots(object session, Point? unshadedSecondary = null, Point? cellGap = null)
    {
        // ShowFixedRings attaches only a FrameworkElement to the supplied Grid. No Window
        // constructor, Present, Show, HwndSource or live input is involved.
        var wheel = (RadialWindow)RuntimeHelpers.GetUninitializedObject(typeof(RadialWindow));
        var grid = new Grid();
        Field(wheel, "MainGrid", grid);
        foreach (var brush in new[] { "_subDefaultSectorBrush", "_subHighlightSectorBrush", "_subSectorBorderBrush",
            "_subHighlightBorderBrush", "_subTextColorBrush" })
            Field(wheel, brush, Brushes.Black);
        Call(wheel, "ShowFixedRings", session);
        Call(wheel, "UpdateFixedRings", Get(session, "Path"), Get(session, "Hover"));
        const int size = 600;
        grid.Measure(new Size(size, size));
        grid.Arrange(new Rect(0, 0, size, size));
        grid.UpdateLayout();
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(grid);
        var painted = new HashSet<(int, int)>();
        var pixel = new byte[4];
        if (cellGap is { } gap)
        {
            int x = (int)Math.Round((gap.X - Origin.X) / Dpi.DpiScaleX + size / 2);
            int y = (int)Math.Round((gap.Y - Origin.Y) / Dpi.DpiScaleY + size / 2);
            bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
            Check(pixel[3] == 0 && (int)Get(Hit(session, gap), "Level")! < 3,
                "the fully expanded Fan overlay leaves a real gap both transparent and unselectable at nonuniform DPI");
        }
        if (unshadedSecondary is { } secondary)
        {
            var x = (int)Math.Round((secondary.X - Origin.X) / Dpi.DpiScaleX + size / 2);
            var y = (int)Math.Round((secondary.Y - Origin.Y) / Dpi.DpiScaleY + size / 2);
            bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
            Check(pixel[3] == 0, "the fixed overlay leaves the original secondary edge unshaded");
        }
        for (int level = 3; level <= 5; level++)
            for (int index = 0; index < Layer.FixedRings!.GetCount(level); index++)
            {
                var point = At(level, index);
                int x = (int)Math.Round((point.X - Origin.X) / Dpi.DpiScaleX + size / 2);
                int y = (int)Math.Round((point.Y - Origin.Y) / Dpi.DpiScaleY + size / 2);
                bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
                if (pixel[3] != 0) painted.Add((level, index));
            }
        return painted;
    }

    private static bool ExpansionMatrix(AppConfig config)
    {
        var failures = new List<string>();
        void Expect(bool value, string name, string? detail = null)
        {
            if (value) Check(true, name);
            else
            {
                failures.Add(name);
                Console.WriteLine("FAIL " + name + (detail == null ? "" : " (" + detail + ")"));
            }
        }

        config.IconLayoutMode = "TextOnly";
        void Exercise(bool autoExpand, bool alwaysVisible, string style, bool direct)
        {
            string label = $"{style}, global={autoExpand}, layer={alwaysVisible}";
            config.AutoExpandSubRingsOnPopup = autoExpand;
            config.SubmenuStyle = style;
            bool secondaryShownAtStart = autoExpand && (style == "Wheel" || alwaysVisible);
            var session = Session(alwaysVisible, shownParent: -1, allSecondary: secondaryShownAtStart);

            // Compare actual published hit addresses with the pixels of the product overlay.
            // A blank/different-parent slice must be absent from both, even when it resolves to no action.
            void InspectSlots(string phase, params (int Level, int Slot)[] expected)
            {
                var paint = PaintedSlots(session, SecondaryPoint());
                var hits = new HashSet<(int, int)>();
                var published = new HashSet<(int, int)>();
                for (int level = 3; level <= 5; level++)
                    for (int index = 0; index < Layer.FixedRings!.GetCount(level); index++)
                    {
                        var hit = Hit(session, At(level, index));
                        if ((int)Get(hit, "Level")! == level && (int)Get(hit, "Index")! == index)
                            hits.Add((level, index));
                        if ((bool)Call(session, "WasVisible", New(Nested("RingHit"), level, index))!)
                            published.Add((level, index));
                    }
                string Describe(HashSet<(int, int)> cells) => $"expected {expected.Length}, actual {cells.Count}";
                Expect(paint.SetEquals(expected), $"{label} {phase} painted slots", Describe(paint));
                Expect(hits.SetEquals(expected), $"{label} {phase} physical hits", Describe(hits));
                Expect(published.SetEquals(expected), $"{label} {phase} published slots", Describe(published));
            }

            if (direct)
            {
                var paint = PaintedSlots(session);
                var independent = Hit(session, At(5, 10));
                Expect(paint.Contains((3, 4)) && paint.Contains((4, 5)) && paint.Contains((5, 10)) &&
                    Resolve(session, independent).Action?.Name == "independent" &&
                    (bool)Call(session, "WasVisible", independent)!, label + " opening permits configured independent slot on level five");
                if (style == "Fan")
                {
                    var first = At(5, 0);
                    var gap = first + (At(5, 1) - first) * .5;
                    var occupied = new (int, int)[] { (3,1), (3,4), (3,7), (4,5), (5,6), (5,10), (5,11) };
                    Check(PaintedSlots(session, cellGap: gap).SetEquals(occupied), "resident Fan paints exactly the occupied outer cells");
                    InspectSlots("resident outer addresses", occupied);
                    var configured = new[] { (0,0), (0,1), (0,2), (1,0) };
                    ExerciseFanPopupSecondaries(session, configured);
                }
                return;
            }
            InspectSlots("opening");
            if (style == "Fan") ExerciseFanPopupSecondaries(session, []);
            var outside = Harness(session);
            int previous = Recorded.Count;
            Call(outside, "TestCompleteMouseTriggerRelease", At(5, 10), "RightButton");
            Pump();
            Expect(Recorded.Count == previous, label + " unopened outer release cannot enqueue", $"queued {Recorded.Count - previous}");

            // A second session navigates after the previous release terminated the first.
            session = Session(alwaysVisible, shownParent: -1, allSecondary: secondaryShownAtStart);
            if (!secondaryShownAtStart)
            {
                Call(session, "AimSecondary", SecondaryPoint(), 0, 0);
                Expect((int)Get(session, "ActiveLevel")! == 2, label + " unpresented secondary cannot expand");
            }
            Call(session, "SetShownSecondary", 0, secondaryShownAtStart);
            Call(session, "AimSecondary", SecondaryPoint(), 0, 0);
            PaintFrame(session);
            InspectSlots("after secondary 0/0", (3, 4));
            Expect(Resolve(session, Hit(session, At(3, 4))).Action?.Name == "tier-3" &&
                Resolve(session, Hit(session, At(3, 1))).Action == null &&
                Resolve(session, Hit(session, At(3, 0))).Action == null &&
                Resolve(session, Hit(session, At(5, 10))).Action == null, label + " only the linked child resolves");
        }

        Exercise(false, true, "Wheel", direct: false);
        Exercise(true, true, "Wheel", direct: true);
        Exercise(true, false, "Wheel", direct: false);
        Exercise(false, false, "Fan", direct: false);
        Exercise(false, true, "Fan", direct: false);
        Exercise(true, false, "Fan", direct: false);
        Exercise(true, true, "Fan", direct: true);
        if (failures.Count == 0) return true;
        Console.Error.WriteLine($"{failures.Count} expansion behavior checks failed");
        return false;
    }

    static void DistanceMatrix(AppConfig config)
    {
        config.AutoExpandSubRingsOnPopup = false;
        config.LockParentSectorOnSubmenu = true;
        config.EnableOuterEscapeCancel = true;
        foreach (string style in new[] { "Wheel", "Fan" })
        {
            config.SubmenuStyle = style;
            config.SubWheelTriggerDistance = 260;
            var session = Session(false, shownParent: -1, scale: .48);
            var controller = Harness(session);
            void Move(Point pt) => Call(controller, "TestProcessMove", pt);
            int Level() => (int)Get(session, "ActiveLevel")!;
            bool Selected(string field) => (bool)Controller.GetField(field, Members)!.GetValue(controller)!;
            var original = SecondaryPoint(band: false);
            var primary = Physical(style == "Fan" ? (Point)Call(currentGeometry, "GetCenter", 1, 0, 4)! : new Point(52, 0));
            Move(primary);
            Move(Along(primary, 259));
            Check(!Selected("_lastShowSubTier") && Level() == 2, style + " hidden secondary waits for the original primary distance");
            Move(Along(primary, 261));
            Check(Selected("_lastShowSubTier") && !Selected("_lastEscapedState") && Level() == 2,
                style + " high primary distance requests the actual secondary even beyond the small wheel");
            // Mirrors RadialWindow publishing the original secondary after its HighlightSector call.
            Call(session, "SetShownSecondary", 0, false);
            Move(original);
            Check(Level() == 2 && Selected("_lastShowSubTier") &&
                (int)Controller.GetField("_selectedSubSectorIndex", Members)!.GetValue(controller)! == 0,
                style + " moving back to the shown secondary keeps its original owner and hit");
            for (int parent = 2; parent <= 4; parent++)
            {
                var start = parent == 2 ? original : At(parent, parent + 1);
                var geometry = session.GetType().GetField("Geometry", Members)!.GetValue(session)!;
                double mid = ((double)Call(geometry, "Inner", parent + 1)! + (double)Call(geometry, "OuterForCount", parent + 1, Layer.FixedRings!.GetCount(parent + 1))!) / 2;
                Move(start);
                Move(Along(start, mid - .4));
                Check(Level() == parent, style + " maximum distance waits past level " + parent + " outer contour");
                Move(Along(start, mid + .1));
                Check(Level() == parent + 1 && !Selected("_lastEscapedState"), style + " level " + (parent + 1) +
                    " is reachable on a compressed outer layout at nonuniform DPI");
                PaintedSlots(session);
            }
            int n = Recorded.Count;
            Move(At(5, 6));
            Call(controller, "TestCompleteMouseTriggerRelease", At(5, 6), "RightButton");
            Pump();
            Check(Recorded.Count == n + 1 && Recorded[^1].Name == "tier-5",
                style + " directional changes after crossing each parent reach the real linked level-five action");

            session = Session(false, shownParent: 0, scale: .48);
            var secondary = SecondaryPoint();
            var layout = session.GetType().GetField("Geometry", Members)!.GetValue(session)!;
            double thirdMiddle = ((double)Call(layout, "Inner", 3)! + (double)Call(layout, "OuterForCount", 3, Layer.FixedRings!.GetCount(3))!) / 2;
            Call(session, "Move", Along(secondary, thirdMiddle + 1));
            Check(Level() == 2, style + " an outer angular projection alone cannot arm a secondary");
            Call(session, "Move", secondary);
            Call(session, "AimSecondary", secondary, 0, 0);
            var differentDirection = Physical(new Point(-thirdMiddle, 0));
            Call(session, "Move", differentDirection);
            Call(session, "Move", Along(secondary, thirdMiddle + 1));
            Check(Level() == 2, style + " leaving the owning ray cancels an armed crossing");
            Call(session, "Move", secondary);
            Call(session, "AimSecondary", secondary, 0, 0);
            Call(session, "Move", Along(secondary, thirdMiddle - 1));
            Call(session, "Move", Along(secondary, thirdMiddle - 2));
            Call(session, "Move", Along(secondary, thirdMiddle + 1));
            Check(Level() == 2, style + " inward reversal outside the parent cancels an armed crossing");
        }
        config.SubmenuStyle = "Wheel";
        config.SubWheelOuterRadius = 196;
        config.SubWheelTriggerDistance = 141;
        var pinnedSession = Session(false, shownParent: -1);
        var pinnedController = Harness(pinnedSession);
        var pin = Controller.GetField("_pinState", Members)!.GetValue(pinnedController)!;
        Call(pin, "Begin", true);
        Call(pin, "Present");
        // Return the original primary hit, then the real empty space before secondaries are shown.
        var primaryHit = Physical(new Point(52, 0));
        Controller.GetProperty("TestPinnedHitTest", Members)!.SetValue(pinnedController,
            (Func<Point, (bool, int, int)>)(pt => pt == primaryHit ? (true, 0, -1) : (false, -1, -1)));
        Call(pinnedController, "ApplyPinnedHover", primaryHit, 18L, false);
        Call(pinnedController, "ApplyPinnedHover", Along(primaryHit, 140), 18L, false);
        Check(!(bool)Controller.GetField("_lastShowSubTier", Members)!.GetValue(pinnedController)!,
            "pinned extended primary waits below the global threshold inside the secondary envelope");
        Call(pinnedController, "ApplyPinnedHover", Along(primaryHit, 142), 18L, false);
        Check((bool)Controller.GetField("_lastShowSubTier", Members)!.GetValue(pinnedController)! &&
            (int)Controller.GetField("_selectedSectorIndex", Members)!.GetValue(pinnedController)! == 0,
            "pinned hover crosses original primary distance in space occupied by an unshown secondary");
        config.SubWheelOuterRadius = 116;
        // Sample the upper half between the parent outline and the *fitted* next-ring middle.
        config.SubWheelTriggerDistance = 166; // halfway between the original radius 72 and slider maximum 260
        var medium = Session(false, scale: .48);
        var sample = SecondaryPoint();
        Call(medium, "AimSecondary", sample, 0, 0);
        double projected = 116 + .5 * (legacyExtent + .48 * (5 + 19.5) - 116);
        Call(medium, "Move", Along(sample, projected - 1));
        Check((int)Get(medium, "ActiveLevel")! == 2, "intermediate high distance waits for its interpolated threshold");
        Call(medium, "Move", Along(sample, projected + 1));
        Check((int)Get(medium, "ActiveLevel")! == 3, "intermediate high distance opens before the next ring midpoint");
        config.WheelRadius = 290;
        config.SubWheelOuterRadius = 375;
        config.SubWheelTriggerDistance = 260;
        var imported = Session(false);
        Call(imported, "AimSecondary", SecondaryPoint(band: false), 0, 0);
        Check((int)Get(imported, "ActiveLevel")! == 2, "imported primary larger than the slider range waits inside the secondary");
        Call(imported, "AimSecondary", SecondaryPoint(), 0, 0);
        Check((int)Get(imported, "ActiveLevel")! == 3, "imported primary larger than maximum uses the finite within-parent threshold");
        config.WheelRadius = 72;
        config.SubWheelOuterRadius = 116;
        config.EnableOuterEscapeCancel = false;
        config.SubmenuStyle = "Wheel";
        config.SubWheelTriggerDistance = 30;
        var hidden = Session(false, shownParent: -1);
        var held = Harness(hidden);
        var leaf = SecondaryPoint(0, 1, false);
        int before = Recorded.Count;
        Call(held, "TestProcessMove", leaf);
        Call(held, "TestCompleteMouseTriggerRelease", leaf, "RightButton");
        Pump();
        Check(Recorded.Count == before, "ordinary hidden secondary leaf cannot confirm through the original angular selector");
        var shown = Session(false, shownParent: 0);
        var hand = Harness(shown);
        var parentPoint = SecondaryPoint(band: false);
        Call(hand, "TestProcessMove", parentPoint);
        PaintedSlots(shown);
        Call(hand, "TestCompleteMouseTriggerRelease", parentPoint, "RightButton");
        Pump();
        Check(Recorded.Count == before + 1 && Recorded[^1].Name == "east-0", "expanded secondary keeps its own release action");
        shown = Session(false, shownParent: 0);
        hand = Harness(shown);
        Call(hand, "TestProcessMove", parentPoint);
        PaintedSlots(shown);
        var third = At(3, 4);
        Call(hand, "TestProcessMove", third);
        PaintedSlots(shown);
        Call(hand, "TestCompleteMouseTriggerRelease", third, "RightButton");
        Pump();
        Check(Recorded.Count == before + 2 && Recorded[^1].Name == "tier-3", "expanded outer parent keeps its own release action");
    }

    [STAThread]
    static void Main(string[] args)
    {
        string sandbox = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(sandbox);
        foreach (var key in new[] { "TMP", "TEMP", "APPDATA", "LOCALAPPDATA" })
            Environment.SetEnvironmentVariable(key, sandbox);
        Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Host.Location))) == args[1], "candidate DLL SHA-256");
        _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var config = new AppConfig { EnableMultiTier = true, AutoExpandSubRingsOnPopup = true, EnableOuterEscapeCancel = false, WheelRadius = 72, InnerRadius = 33,
            SubWheelOuterRadius = 116, SubWheelTriggerDistance = 60, SubmenuStyle = "Wheel", Shape = "Original" };
        typeof(ConfigManager).GetProperty("CurrentConfig")!.SetValue(null, config);
        Layer.Actions[0].SubActions = Enumerable.Range(0, 5).Select(i => Text("east-" + i)).ToList();
        Layer.Actions[1].SubActions = [Text("south-0")];
        Profile.Actions = Layer.Actions;
        Profile.SectorCount = Layer.SectorCount;
        Layer.EnableCenterAction = true;
        Layer.CenterAction = Text("center");

        CheckFanGeometry(config);
        // The original 2101 DLL required dwell at a narrow edge even at the minimum global distance.
        double priorDistance = config.SubWheelTriggerDistance;
        config.SubWheelTriggerDistance = 30;
        var immediate = Session(false);
        var immediateController = Harness(immediate);
        Call(immediateController, "TestProcessMove", SecondaryPoint(band: false));
        Check((int)Get(immediate, "ActiveLevel")! == 3, "global minimum opens a real secondary immediately, before a dispatcher tick");
        config.SubWheelTriggerDistance = priorDistance;

        // Exercise only the layout cache fields; the Window constructor and native handles are never invoked.
        var windowFields = RuntimeHelpers.GetUninitializedObject(typeof(RadialWindow));
        var fit = Call(windowFields, "FitFixedGeometry", 4, 133d, 145d, 5, 230d, 9L)!;
        Check((double)Call(fit, "Outer", 5)! <= 230.000001 &&
            ReferenceEquals(fit, Call(windowFields, "FitFixedGeometry", 4, 133d, 145d, 5, 230d, 9L)),
            "five rings fit available DIPs and reuse their warm geometry");
        Check(!ReferenceEquals(fit, Call(windowFields, "FitFixedGeometry", 4, 133d, 145d, 5, 180d, 9L)) &&
            !ReferenceEquals(fit, Call(windowFields, "FitFixedGeometry", 4, 133d, 145d, 5, 230d, 9L)),
            "a different fitted radius replaces the only cached layout");
        var current = Call(windowFields, "FitFixedGeometry", 4, 133d, 145d, 5, 230d, 9L);
        Check(!ReferenceEquals(current, Call(windowFields, "FitFixedGeometry", 4, 133d, 145d, 5, 230d, 10L)),
            "configuration revision evicts old geometry variants");
        object session = Session(true);
        var independent = Hit(session, At(5, 10));
        var linked = Hit(session, At(5, 6));
        Check(Resolve(session, independent).Action?.Name == "independent" && Resolve(session, linked).Action?.Name == "tier-5",
            "persistent rings directly select independent and linked outer slots");
        Check(((ValueTuple<int, int>)Call(session, "Target", linked)!) == (0, 0) &&
            ((ValueTuple<int, int>)Call(session, "Target", independent)!) == (-2, -1), "SDK projection follows ancestry and omits orphan addresses");
        Check(Resolve(session, New(Nested("RingHit"), 0, -1)).Action?.Name == "center", "center binding stays independent");
        Check(Resolve(session, Hit(session, At(5, 9))).Action == null &&
            Resolve(session, Hit(session, At(5, 11))).Action == null, "empty and missing plugin slots cannot execute");
        var originalChild = Layer.Actions[0].SubActions[0];
        Layer.Actions[0].SubActions.RemoveAt(0);
        Check(!Resolve(session, linked).Valid && Resolve(session, independent).Action?.Name == "independent",
            "deleting a legacy secondary owner invalidates its visible linked target and preserves independent access");
        Layer.Actions[0].SubActions.Insert(0, originalChild);
        (Layer.Actions[0].SubActions[0], Layer.Actions[0].SubActions[1]) =
            (Layer.Actions[0].SubActions[1], Layer.Actions[0].SubActions[0]);
        Check(!Resolve(session, linked).Valid, "reordering two legacy secondary owners invalidates the visible linked target");
        (Layer.Actions[0].SubActions[0], Layer.Actions[0].SubActions[1]) =
            (Layer.Actions[0].SubActions[1], Layer.Actions[0].SubActions[0]);
        var saved = Layer.FixedRings!.GetSlot(5, 10)!;
        saved.Action.Parameter = "changed";
        Check(!Resolve(session, independent).Valid, "in-place action mutation invalidates recorded selection");
        saved.Action.Parameter = "independent";
        saved.ParentIndex = 1;
        Check(!Resolve(session, independent).Valid, "parent reassignment invalidates recorded selection");
        saved.ParentIndex = null;
        Layer.FixedRings.SetCount(5, 8);
        Check(!Resolve(session, independent).Valid, "shrinking count blocks previously visible slot");
        Layer.FixedRings.SetCount(5, 20);
        Layer.MaxMenuDepth = 4;
        Check(!Resolve(session, independent).Valid, "reduced depth hides fifth level immediately");
        Layer.MaxMenuDepth = 5;
        Layer.FixedRingsPaused = true;
        Check(!Resolve(session, independent).Valid, "switching to legacy layout invalidates released fixed action");
        Layer.FixedRingsPaused = false;
        Profile.Layers.Add(new WheelLayer());
        Profile.ActiveLayerIndex = 1;
        Check(!Resolve(session, independent).Valid, "switching active wheel layer invalidates recorded fixed selection");
        Profile.ActiveLayerIndex = 0;

        session = Session(false);
        Check(Resolve(session, Hit(session, At(5, 10))).Action == null, "unopened outer ring cannot execute");
        Point band = SecondaryPoint();
        Call(session, "AimSecondary", SecondaryPoint(band: false), 0, 0);
        Pump(175);
        Check((int)Get(session, "ActiveLevel")! == 2, "stationary pointer below the global distance does not expand");
        Call(session, "Move", Origin);
        Call(session, "AimSecondary", band, 0, 0);
        Check((int)Get(session, "ActiveLevel")! == 3, "secondary reaches level three on the same move, ignoring saved dwell and band");
        Check(Resolve(session, Hit(session, At(3, 1))).Action == null &&
            Resolve(session, Hit(session, At(3, 4))).Action?.Name == "tier-3", "fixed third ring belongs to the actual legacy root and child");
        for (int level = 3; level < 5; level++)
        {
            var parent = At(level, level + 1, true);
            Call(session, "Move", parent);
            Check((int)Get(session, "ActiveLevel")! == level, "unpainted parent cannot open level " + (level + 1));
            PaintedSlots(session);
            Call(session, "Move", parent);
            Check((int)Get(session, "ActiveLevel")! == level + 1, "painted parent opens level " + (level + 1) + " without delay");
        }
        Check(Resolve(session, Hit(session, At(5, 6))).Action?.Name == "tier-5" &&
            Resolve(session, Hit(session, At(5, 10))).Action == null, "full progressive path reaches level five, excludes independent slots");
        Call(session, "Move", Origin);
        Check((int)Get(session, "ActiveLevel")! == 2, "inward retreat clears fixed descendants, restores the original wheel");
        Call(session, "AimSecondary", SecondaryPoint(0, 4), 0, 4);
        Check(Resolve(session, Hit(session, At(3, 7))).Action?.Name == "fifth child", "old Wheel menus with five children keep distinct secondary ownership");

        session = Session(false);
        var controller = Harness(session);
        Call(controller, "TestProcessMove", SecondaryPoint());
        Check((int)Get(session, "ActiveLevel")! == 3, "controller opens the legacy Wheel child immediately");
        PaintedSlots(session);
        Call(controller, "TestCompleteMouseTriggerRelease", At(3, 4), "RightButton");
        Pump();
        Check(Recorded.Count == 1 && Recorded[0].Name == "tier-3", "mouse release reaches the substituted enqueue entry once");
        session = Session(true);
        controller = Harness(session);
        Field(controller, "_activeTrigger", new TriggerConfig { TriggerType = "Keyboard", VkCode = 65 });
        Controller.GetProperty("KeyboardReleasePointerOverride", Members)!.SetValue(controller, (Func<Point?>)(() => At(5, 10)));
        Call(controller, "KeyboardHook_OnKeyUp", null, new GlobalKeyEventArgs(65, ModifierKeys.None));
        Pump();
        Check(Recorded.Count == 2 && Recorded[^1].Name == "independent", "keyboard key-up executes the direct independent slot through the substituted queue");

        session = Session(true);
        controller = Harness(session);
        object start = New(Nested("FixedPress"), session, Get(session, "Path"), Hit(session, At(5, 10)));
        Call(controller, "ConfirmFixedSelection", At(5, 6), start);
        Pump();
        Check(Recorded.Count == 2, "pinned down and up on different slots cannot confirm");
        Call(controller, "ConfirmFixedSelection", At(5, 10), start);
        Pump();
        Check(Recorded.Count == 3 && Recorded[^1].Name == "independent", "matching pinned press and release confirms independent outer slot");
        session = Session(true, publish: false);
        controller = Harness(session);
        Call(controller, "TestCompleteMouseTriggerRelease", At(5, 10), "RightButton");
        Pump();
        Check(Recorded.Count == 3, "release before visual presentation stays suppressed");
        session = Session(false);
        controller = Harness(session);
        Call(controller, "TestProcessMove", SecondaryPoint(band: false));
        Pump(8);
        Call(controller, "ResetDeepNavigation", true);
        Pump(70);
        Check((int)Get(session, "ActiveLevel")! == 2, "clearing a gesture discards an armed parent below threshold");
        session = Session(false);
        controller = Harness(session);
        Call(controller, "TestProcessMove", SecondaryPoint());
        Call(controller, "ScheduleMouseReleaseDebounce", SecondaryPoint(), "RightButton", 85, null);
        Call(controller, "TestProcessMove", At(3, 4));
        Pump(150);
        Check(Recorded.Count == 4 && Recorded[^1].Name == "east-0",
            "debounce preserves the original secondary action after immediate expansion and later movement");

        session = Session(false);
        controller = Harness(session);
        Call(controller, "TestProcessMove", SecondaryPoint());
        // Keep the Render callback queued while releasing in the new ring.
        Call(controller, "TestCompleteMouseTriggerRelease", At(3, 4), "RightButton");
        Pump();
        Check(Recorded.Count == 4, "an unpublished child tier cannot execute during a fast release");
        config.SubmenuStyle = "Fan";
        config.LockParentSectorOnSubmenu = true;
        session = Session(false);
        controller = Harness(session);
        Call(controller, "TestProcessMove", SecondaryPoint());
        Pump(95);
        var tracker = (SubmenuLockTracker)Controller.GetField("_submenuLock", Members)!.GetValue(controller)!;
        Check(tracker.LockedParentSector == 0 && (int)Get(session, "ActiveLevel")! == 3,
            "Fan uses its original petal hit and parent lock to open the linked third ring");
        Call(controller, "TestProcessMove", Origin);
        Pump();
        Check(!tracker.IsLocked && (int)Get(session, "ActiveLevel")! == 2, "return through Fan releases the original parent lock");
        Call(controller, "TestCompleteMouseTriggerRelease", Origin, "RightButton");
        Pump();
        Check(Recorded.Count == 5 && Recorded[^1].Name == "center", "legacy center action remains available after retreat");

        config.SubmenuStyle = "Wheel";
        session = Session(true);
        controller = Harness(session);
        Call(controller, "TestProcessMove", SecondaryPoint(0, 4, false));
        Call(controller, "TestCompleteMouseTriggerRelease", SecondaryPoint(0, 4, false), "RightButton");
        Pump();
        Check(Recorded.Count == 6 && Recorded[^1].Name == "east-4", "legacy fifth Wheel child executes its original action in persistent mode");
        controller = Harness(Session(true));
        var primary = new Point(Origin.X, Origin.Y + 49 * Dpi.DpiScaleY);
        Call(controller, "TestProcessMove", primary);
        Call(controller, "TestCompleteMouseTriggerRelease", primary, "RightButton");
        Pump();
        Check(Recorded.Count == 7 && Recorded[^1].Name == "south", "legacy first ring retains its own action");
        session = Session(false);
        controller = Harness(session);
        Call(controller, "TestProcessMove", SecondaryPoint());
        long first = (long)Call(controller, "BeginFixedLayerSwitch", Profile, 1)!;
        long second = (long)Call(controller, "BeginFixedLayerSwitch", Profile, 0)!;
        long latest = (long)Call(controller, "BeginFixedLayerSwitch", Profile, 1)!;
        Field(controller, "_radialWindow", windowFields);
        Call(controller, "SwitchRadialLayerOnUi", windowFields, 1, 18L, first);
        Call(controller, "SwitchRadialLayerOnUi", windowFields, 0, 18L, second);
        Check(first < second && second < latest && Profile.ActiveLayerIndex == 1 &&
            (bool)Controller.GetField("_switchingFixedLayer", Members)!.GetValue(controller)! &&
            ReferenceEquals(Controller.GetField("_fixedSession", Members)!.GetValue(controller), session),
            "A-B-A-B queued layer changes reject both obsolete callbacks, even when the first requests the final index");
        Field(controller, "_radialWindow", null!);
        Pump(80);
        Check((bool)Controller.GetField("_isGestureActive", Members)!.GetValue(controller)! &&
            (int)Get(session, "ActiveLevel")! == 3, "an obsolete render cannot cancel a pending layer switch");
        Call(controller, "TestCompleteMouseTriggerRelease", At(3, 4), "RightButton");
        Pump();
        Check(Recorded.Count == 7, "release between layer selection and redisplay cannot execute an old target");
        Profile.ActiveLayerIndex = 0;
        session = Session(false, navigationOnly: true);
        controller = Harness(session);
        Call(session, "SetShownSecondary", 1, false);
        Call(session, "AimSecondary", SecondaryPoint(), 0, 0);
        Check((int)Get(session, "ActiveLevel")! == 2, "an undisplayed legacy secondary cannot wake its third ring");
        Call(session, "SetShownSecondary", 0, false);
        Call(session, "AimSecondary", SecondaryPoint(), 0, 0);
        PaintFrame(session);
        var nav = Hit(session, At(3, 4));
        var down = New(Nested("FixedPress"), session, Get(session, "Path"), nav);
        Call(controller, "ConfirmFixedSelection", At(3, 4), down);
        Pump();
        Check((int)Get(session, "ActiveLevel")! == 4 && Recorded.Count == 7,
            "pinned click on navigation-only outer parent opens its children without dispatch");
        PaintedSlots(session);
        var leaf = Hit(session, At(4, 5));
        down = New(Nested("FixedPress"), session, Get(session, "Path"), leaf);
        Call(controller, "ConfirmFixedSelection", At(4, 5), down);
        Pump();
        Check(Recorded.Count == 8 && Recorded[^1].Name == "tier-4",
            "pinned leaf confirmation follows the current visible parent");
        if (!ExpansionMatrix(config))
        {
            Environment.ExitCode = 1;
            return;
        }
        DistanceMatrix(config);
        CheckOverlappingFanPetals();
        Console.WriteLine(checks + " fixed-ring runtime checks");
    }
}
