using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using WinPieGestures;
using StarPie.Plugin;

internal static partial class Program
{
    private static void ExerciseFanPopupSecondaries(object session, (int Parent, int Child)[] expected)
    {
        // Give the product renderer an off-screen Canvas. None of Present, Show and the native hook runs.
        var config = ConfigManager.CurrentConfig;
        bool showText = config.ShowText;
        config.ShowText = false;
        var wheel = new RadialWindow(Origin, Profile);
        Check(!wheel.IsVisible && PresentationSource.FromVisual(wheel) == null, "Fan popup renderer is attached to an initialized off-screen Window without HWND");
        var canvas = new Canvas { Width = 600, Height = 600 };
        Field(wheel, "WheelCanvas", canvas);
        Field(wheel, "_canvasCenter", 300d);
        Field(wheel, "_innerRadius", config.InnerRadius);
        Field(wheel, "_outerRadius", config.WheelRadius);
        Field(wheel, "_profile", Profile);
        Field(wheel, "_fixedGestureLayout", true);
        typeof(RadialWindow).GetProperty("FixedGeometry", Members)!.SetValue(wheel, currentGeometry);
        Call(wheel, "ShowAllSubTiers");
        var paths = canvas.Children.OfType<System.Windows.Shapes.Path>().ToArray();
        Check(paths.Select(p => p.Tag as string).ToHashSet().SetEquals(expected.Select(p => $"sub_{p.Parent}_{p.Child}")),
            expected.Length == 0 ? "progressive Fan popup keeps original secondary petals hidden" :
            "persistent Fan popup renders all configured original secondary petals across multiple primary sectors");
        canvas.Measure(new Size(600,600));
        canvas.Arrange(new Rect(0,0,600,600));
        var bitmap = new RenderTargetBitmap(600,600,96,96,PixelFormats.Pbgra32);
        bitmap.Render(canvas);
        var pixel = new byte[4];
        foreach (var (parent, child) in expected)
        {
            Point point = SecondaryPoint(parent, child, band: false);
            int x = (int)Math.Round(300 + (point.X - Origin.X) / Dpi.DpiScaleX);
            int y = (int)Math.Round(300 + (point.Y - Origin.Y) / Dpi.DpiScaleY);
            bitmap.CopyPixels(new Int32Rect(x,y,1,1),pixel,4,0);
            var path = paths.Single(p => Equals(p.Tag, $"sub_{parent}_{child}"));
            Check(path.Data.FillContains(new Point(x,y)) && pixel[3] > 0,
                $"persistent Fan secondary {parent}/{child} has a painted production petal at its normalized physical point");
            var controller = Harness(session);
            int previous = Recorded.Count;
            Call(controller, "TestProcessMove", point);
            Call(controller, "TestCompleteMouseTriggerRelease", point, "RightButton");
            Pump();
            Check(Recorded.Count == previous + 1 && Recorded[^1].Name == Layer.Actions[parent].SubActions[child].Name,
                $"persistent Fan secondary {parent}/{child} release on the painted petal queues the matching fixture once");
        }
        if (expected.Length > 0) CheckPersistentFanRelease(paths);
        config.ShowText = showText;
    }
    private static void CheckPersistentFanRelease(System.Windows.Shapes.Path[] petals)
    {
        var cfg = ConfigManager.CurrentConfig;
        var geometry = Host.GetType("WinPieGestures.LegacyRingGeometry")!;
        var rootShapes = Enumerable.Range(0, Layer.SectorCount)
            .Select(i => ((Geometry Shape, Point Label))geometry.GetMethod("Primary", System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.NonPublic)!.Invoke(null, [cfg, Layer.SectorCount, i, cfg.WheelRadius, cfg.InnerRadius])!)
            .ToArray();
        Point east = Physical(rootShapes[0].Label), south = Physical(rootShapes[1].Label);
        double r = (cfg.WheelRadius + legacyExtent) / 2;
        var clear = Enumerable.Range(0, 90).Select(i => i * Math.PI / 180)
            .Select(a => new Point(r * Math.Cos(a), r * Math.Sin(a)))
            .First(p => !petals.Any(v => v.Data.FillContains(p + new Vector(300,300))) &&
                !rootShapes.Any(v => v.Shape.FillContains(p)));
        Point gap = Physical(clear);
        var failed = new List<string>();
        foreach (bool keyboard in new[] { false, true })
        {
            foreach (var (from, to, expected, label) in new (Point, Point, string?, string)[] {
                (east,east,"east","unchanged primary"), (Origin,Origin,"center","unchanged center"),
                (east,gap,null,"primary to clear gap"), (Origin,gap,null,"center to clear gap"),
                (east,south,"south","different primary"), (south,Origin,"center","primary to center"),
                (Origin,east,"east","center to primary"), (east,SecondaryPoint(1,0,false),"south-0","primary to petal") })
            {
                var session = Session(true, allSecondary: true);
                var controller = Harness(session);
                var events = new List<InteractionEvent>();
                var interaction = New(Host.GetType("WinPieGestures.Plugins.PluginInteractionSession")!,
                    InteractionSource.NormalGesture, Profile.ProcessName, (Func<InteractionEvent,int>)(e => { events.Add(e); return 1; }));
                Field(controller, "_interactionSession", interaction);
                Call(interaction, "Presented");
                int previous = Recorded.Count;
                Call(controller, "TestProcessMove", from);
                if (keyboard)
                {
                    Field(controller, "_activeTrigger", new TriggerConfig { TriggerType = "Keyboard", VkCode = 65 });
                    Controller.GetProperty("KeyboardReleasePointerOverride", Members)!.SetValue(controller, (Func<Point?>)(() => to));
                    Call(controller, "KeyboardHook_OnKeyUp", null, new GlobalKeyEventArgs(65, System.Windows.Input.ModifierKeys.None));
                }
                else Call(controller, "TestCompleteMouseTriggerRelease", to, "RightButton");
                Pump();
                var names = Recorded.Skip(previous).Select(a => a.Name).ToArray();
                string route = keyboard ? "keyboard" : "mouse";
                InteractionTarget? target = expected switch {
                    "east" => new(InteractionTargetKind.Sector,0),
                    "south" => new(InteractionTargetKind.Sector,1),
                    "center" => InteractionTarget.Core,
                    "south-0" => new(InteractionTargetKind.SubSector,1,0),
                    _ => null
                };
                var committed = events.Where(e => e.Kind == InteractionEventKind.ActionCommitted).Select(e => e.Target).ToArray();
                if (!committed.SequenceEqual(target is { } selected ? new[] { selected } : []))
                    failed.Add($"{route} {label}: SDK expected [{target}], committed [{string.Join(",",committed)}]");
                if (names.SequenceEqual(expected == null ? [] : new[] { expected }))
                    Check(true, $"persistent Fan {route} release uses final physical point: {label}");
                else failed.Add($"{route} {label}: expected [{expected}], got [{string.Join(',',names)}]");
            }
        }
        Point petalA = SecondaryPoint(0,0,false), petalB = SecondaryPoint(1,0,false);
        var retreat = Physical(new Point((cfg.InnerRadius + SubmenuLockTracker.GetSubmenuExitDistance(cfg, true, cfg.WheelRadius)) / 2, 0));
        bool savedLock = cfg.LockParentSectorOnSubmenu;
        foreach (var (enabled, returnInside) in new[] { (true,false), (true,true), (false,false) })
        {
            cfg.LockParentSectorOnSubmenu = enabled;
            var controller = Harness(Session(true, allSecondary: true));
            var tracker = (SubmenuLockTracker)Controller.GetField("_submenuLock", Members)!.GetValue(controller)!;
            Call(controller, "TestProcessMove", petalA);
            int firstOwner = tracker.LockedParentSector;
            if (returnInside) Call(controller, "TestProcessMove", retreat);
            bool unlocked = !tracker.IsLocked;
            Call(controller, "TestProcessMove", petalB);
            int finalOwner = tracker.LockedParentSector;
            int previous = Recorded.Count;
            Call(controller, "TestCompleteMouseTriggerRelease", petalB, "RightButton");
            Pump();
            var names = Recorded.Skip(previous).Select(a => a.Name).ToArray();
            bool reject = enabled && !returnInside;
            bool expectedState = firstOwner == (enabled ? 0 : -1) && (!returnInside || unlocked) &&
                finalOwner == (enabled ? (returnInside ? 1 : 0) : -1);
            if (!expectedState || !names.SequenceEqual(reject ? [] : new[] { "south-0" }))
                failed.Add($"held Fan lock={enabled} retreat={returnInside}: firstOwner={firstOwner}, unlocked={unlocked}, finalOwner={finalOwner}, queued=[{string.Join(',',names)}]");
            else Check(true, $"held Fan lock={enabled} retreat={returnInside} binds the actual parent until re-entry");
        }
        cfg.LockParentSectorOnSubmenu = true;
        var noIntermediateMove = Harness(Session(true, allSecondary: true));
        Call(noIntermediateMove, "TestProcessMove", petalA);
        int beforeDirect = Recorded.Count;
        Call(noIntermediateMove, "TestCompleteMouseTriggerRelease", petalB, "RightButton");
        Pump();
        if (Recorded.Count != beforeDirect)
            failed.Add($"held Fan directly releasing on another parent without a mouse move queued {Recorded[^1].Name}");
        else Check(true,"held parent lock also rejects a foreign final hit with no intervening mouse move");
        // Pinned hover and the common release path take the explicit painted hit. The foreground guard
        // of the native pinned-click entry is intentionally left to the real-device gate.
        var pinned = Harness(Session(true, allSecondary: true));
        var pin = Controller.GetField("_pinState", Members)!.GetValue(pinned)!;
        Call(pin, "Begin", true);
        Call(pin, "Present");
        Controller.GetProperty("TestPinnedHitTest", Members)!.SetValue(pinned, (Func<Point,(bool,int,int)>)(pt =>
            pt == petalA ? (true,0,0) : pt == petalB ? (true,1,0) : (false,-1,-1)));
        Call(pinned, "ApplyPinnedHover", petalA, 18L, false);
        Call(pinned, "ApplyPinnedHover", petalB, 18L, false);
        if ((int)Controller.GetField("_selectedSectorIndex", Members)!.GetValue(pinned)! != 1)
            failed.Add("pinned Fan hover did not independently select another parent's painted petal");
        int beforePinned = Recorded.Count;
        Call(pinned, "TestCompleteMouseTriggerRelease", petalB, "RightButton");
        Pump();
        if (Recorded.Count != beforePinned + 1 || Recorded[^1].Name != "south-0")
            failed.Add("pinned explicit Fan release failed to select another parent's painted petal");
        else Check(true,"pinned Fan explicit selection stays independent of held-gesture parent locking");
        cfg.LockParentSectorOnSubmenu = savedLock;
        Check(failed.Count == 0, $"persistent Fan release-gap local point {clear}; " + string.Join(" | ",failed));
    }
    private static void CheckOverlappingFanPetals()
    {
        var current = typeof(ConfigManager).GetProperty("CurrentConfig")!;
        var saved = ConfigManager.CurrentConfig;
        var config = new AppConfig { Shape = "Circle", SubmenuStyle = "Fan", WheelRadius = 72, InnerRadius = 36,
            SubWheelOuterRadius = 116, EnableMultiTier = true, AutoExpandSubRingsOnPopup = true, LockParentSectorOnSubmenu = true };
        current.SetValue(null, config);
        try
        {
            var layer = new WheelLayer { SectorCount = 8, MaxMenuDepth = 5, FixedRings = new FixedRingMenu { AlwaysVisible = true } };
            for (int root = 0; root < 8; root++)
            {
                var action = Text($"root-{root}");
                action.SubActions = Enumerable.Range(0,3).Select(i => Text($"petal-{root}-{i}")).ToList();
                layer.Actions.Add(action);
            }
            var profile = new WheelProfile { ProcessName = "Global", Layers = [layer] };
            profile.EnsureLayers();
            var legacy = Host.GetType("WinPieGestures.LegacyRingGeometry")!;
            var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
            (Geometry Shape, Point Label) Petal(int root, int child) =>
                ((Geometry,Point))legacy.GetMethod("Secondary",flags)!.Invoke(null,[config,8,root,child,3,72d,36d])!;
            var entry = Petal(0,1).Label;
            var release = Petal(1,0).Label;
            Check(Petal(0,2).Shape.FillContains(release) && Petal(1,0).Shape.FillContains(release),
                "eight-sector circle fixture puts the release inside both the old right and topmost foreign left petal");
            double extent = (double)legacy.GetMethod("SecondaryExtent",flags)!.Invoke(null,[config,72d,36d])!;
            var geometry = New(Host.GetType("WinPieGestures.FixedRingGeometry")!,8,72d,extent + 5,1d,true);
            foreach (bool locked in new[] { false, true })
            {
                config.LockParentSectorOnSubmenu = locked;
                var session = New(Nested("FixedRingSession"),profile,layer,18L,geometry,Origin,Dpi,extent);
                Call(session,"SetShownSecondary",-1,true);
                Call(session,"Present");
                PaintFrame(session);
                var controller = Harness(session,profile);
                Call(controller,"TestProcessMove",Physical(entry));
                var tracker = (SubmenuLockTracker)Controller.GetField("_submenuLock",Members)!.GetValue(controller)!;
                Check(tracker.LockedParentSector == (locked ? 0 : -1), $"eight-sector entry on center petal captures parent 0 only when enabled ({locked})");
                int previous = Recorded.Count;
                Call(controller,"TestCompleteMouseTriggerRelease",Physical(release),"RightButton");
                Pump();
                var names = Recorded.Skip(previous).Select(a => a.Name).ToArray();
                Check(names.SequenceEqual(locked ? [] : new[] { "petal-1-0" }),
                    $"overlap honors the painted topmost parent before checking lock={locked}, queued [{string.Join(",",names)}]");
            }
        }
        finally { current.SetValue(null,saved); }
    }
    private static void CheckFanGeometry(AppConfig config)
    {
        object wheel = RuntimeHelpers.GetUninitializedObject(typeof(RadialWindow));
        object Fitted(string style)
        {
            config.SubmenuStyle = style;
            return Call(wheel, "FitFixedGeometry", 4, 72d, 116d, 5, 430d, 1L)!;
        }
        var circular = Fitted("Wheel");
        var honeycomb = Fitted("Fan");
        Geometry Shape(object layout, int level, int index, int count) =>
            (Geometry)Call(layout, "GetShape", level, index, count)!;
        // Start with existing production APIs so the old candidate can demonstrate the actual visual defect.
        double Coverage(Geometry s) => s.GetArea() / (s.Bounds.Width * s.Bounds.Height);
        var oldShape = Shape(circular, 3, 0, 4);
        var fanShape = Shape(honeycomb, 3, 0, 4);
        Check(Coverage(fanShape) is > .70 and < .80 && fanShape.Bounds.Width / fanShape.Bounds.Height is > .8 and < 1.2 &&
            Math.Abs(fanShape.GetArea() - oldShape.GetArea()) > oldShape.GetArea() * .05,
            $"Fan level three draws a compact hexagonal cell distinct from the Wheel quarter-sector (Fan {fanShape.Bounds}, Wheel {oldShape.Bounds})");

        for (int level = 3; level <= 5; level++)
        {
            foreach (int count in new[] { 4, 7, FixedRingMenu.RecommendedCount(level), FixedRingMenu.MaximumCount(level) })
            {
                int shell = level == 5 && count == 37 ? 7 : level + 1;
                var shellAngles = (from q in Enumerable.Range(-shell, 2 * shell + 1)
                                   from r in Enumerable.Range(-shell, 2 * shell + 1)
                                   where Math.Max(Math.Abs(q), Math.Max(Math.Abs(r), Math.Abs(q + r))) == shell
                                   let angle = Math.Atan2(1.5 * r, Math.Sqrt(3) * (q + r / 2.0))
                                   orderby (angle + Math.Tau) % Math.Tau
                                   select (angle + Math.Tau) % Math.Tau).ToArray();
                var centers = new List<Point>();
                for (int i = 0; i < count; i++)
                {
                    Geometry shape = Shape(honeycomb, level, i, count);
                    Point p = (Point)Call(honeycomb, "GetCenter", level, i, count)!;
                    if (!shape.IsFrozen || !shape.FillContains(p) || (int)Call(honeycomb, "Hit", p, level, count)! != i ||
                        Coverage(shape) is < .70 or > .80 || shape.Bounds.Width / shape.Bounds.Height is < .8 or > 1.2)
                        throw new InvalidOperationException($"Fan level={level} count={count} slot={i} bounds={shape.Bounds}: rendered hexagon and input disagree");
                    var figure = shape.GetFlattenedPathGeometry().Figures.Single();
                    var vertices = new List<Point> { figure.StartPoint };
                    foreach (var segment in figure.Segments)
                    {
                        if (segment is PolyLineSegment poly) vertices.AddRange(poly.Points);
                        else if (segment is LineSegment line) vertices.Add(line.Point);
                    }
                    var corners = vertices.Distinct().ToArray();
                    if (corners.Length != 6)
                        throw new InvalidOperationException($"Fan {level}/{i}/{count} expected six distinct corners, got {corners.Length}");
                    foreach (var corner in corners)
                    {
                        var inner = p + (corner - p) * .90;
                        var beyond = p + (corner - p) * 1.08;
                        if ((int)Call(honeycomb, "Hit", inner, level, count)! != i || (int)Call(honeycomb, "Hit", beyond, level, count)! == i)
                            throw new InvalidOperationException($"Fan {level}/{i}/{count} hit does not follow the painted corner {corner}");
                    }
                    double a = (Math.Atan2(p.Y,p.X) + Math.Tau) % Math.Tau;
                    if (Math.Abs(a - shellAngles[i * shellAngles.Length / count]) > .00001)
                        throw new InvalidOperationException($"Fan {level}/{i}/{count} does not sample the expected axial-shell direction");
                    centers.Add(p);
                }
                var angles = centers.Select(p => (Math.Atan2(p.Y, p.X) + 2 * Math.PI) % (2 * Math.PI)).ToArray();
                Check(centers[0].X > 0 && Math.Abs(centers[0].Y) < .001 && angles.Zip(angles.Skip(1), (a,b) => b > a).All(x => x),
                    $"Fan level {level} count {count}: all six-sided cells are selectable clockwise from east");

                var between = centers[0] + (centers[1] - centers[0]) * .5;
                Check((int)Call(honeycomb, "Hit", between, level, count)! == -1 &&
                    !Enumerable.Range(0,count).Any(i => Shape(honeycomb, level, i, count).FillContains(between)),
                    $"Fan level {level} count {count}: gap between neighbors is outside every painted contour");
            }
        }
        var sizingLayer = new WheelLayer { SectorCount = 4, MaxMenuDepth = 5, FixedRings = new() };
        var sizingProfile = new WheelProfile { ProcessName = "size.test", Layers = [sizingLayer] };
        sizingProfile.EnsureLayers();
        Field(wheel, "_profile", sizingProfile);
        foreach (var slots in new[] { 20, 37, 20 })
        {
            sizingLayer.FixedRings.SetCount(5, slots);
            var fitted = Call(wheel, "FitFixedGeometry", 4, 72d, 116d, 5, 255d, 2L)!;
            double scale = (double)Get(fitted,"Scale")!;
            double extent = (double)Call(fitted,"OuterForCount",5,slots)!;
            Check(extent <= 255.00001 && (slots == 20 ? Math.Abs(scale - 1) < .00001 : scale < 1 && extent > 254.999),
                $"255-DIP Fan fit uses actual {slots}-slot fifth shell: scale={scale:F6}, extent={extent:F4}");
        }
        var restored = Fitted("Wheel");
        Check(Shape(restored, 3, 0, 4).Bounds == oldShape.Bounds &&
            Shape(restored, 3, 0, 4).GetArea() == oldShape.GetArea(), "switching Fan back to Wheel restores original fitted annular geometry");
    }
}
