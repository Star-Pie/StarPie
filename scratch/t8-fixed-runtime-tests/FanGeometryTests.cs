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
        var configured = session.GetType().GetField("_configuredSlots", Members)!.GetValue(session);
        Field(wheel, "_configuredGestureSlots", configured!);
        Call(wheel, "RenderSectors");
        Call(wheel, "ShowAllSubTiers");
        var paths = canvas.Children.OfType<System.Windows.Shapes.Path>().Where(path => path.Tag is string).ToArray();
        Check(paths.Select(p => p.Tag as string).ToHashSet().SetEquals(expected.Select(p => $"sub_{p.Parent}_{p.Child}")),
            expected.Length == 0 ? "progressive Fan popup keeps original secondary petals hidden" :
            "persistent Fan popup renders all configured original secondary petals across multiple primary sectors");
        canvas.Measure(new Size(600,600));
        canvas.Arrange(new Rect(0,0,600,600));
        var bitmap = new RenderTargetBitmap(600,600,96,96,PixelFormats.Pbgra32);
        bitmap.Render(canvas);
        var pixel = new byte[4];
        foreach (var path in canvas.Children.OfType<System.Windows.Shapes.Path>().Where(path => path.Tag is int))
        {
            int root = (int)path.Tag;
            var cell = ((Geometry Shape, Point Label))Call(currentGeometry, "Primary", config, root)!;
            var display = cell.Label + new Vector(300,300);
            bitmap.CopyPixels(new Int32Rect((int)Math.Round(display.X), (int)Math.Round(display.Y), 1, 1), pixel, 4, 0);
            bool visible = root < 2;
            if (!path.Data.FillContains(display) || Math.Abs(path.Data.GetArea()-cell.Shape.GetArea()) > .05 ||
                (pixel[3] != 0) != visible || (path.Visibility == Visibility.Visible) != visible)
                throw new InvalidOperationException($"Presented Fan primary {root} disagrees with its geometry or visibility");
        }
        Check(true, "primary renderer paints the shared cells at the published locations and respects vacant-root visibility");
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
    private static void CheckClassicGestureVisibility(AppConfig config)
    {
        var empty = new ActionItem { Name = "empty", Type = "Text" };
        var branch = new ActionItem { Name = "branch", Type = "Hotkey", SubActions = [empty, empty, Text("third-child")] };
        var layer = new WheelLayer { SectorCount = 4, MaxMenuDepth = 2, Actions = [branch, empty, Text("any-root")] };
        var profile = new WheelProfile { ProcessName = "Global", Layers = [layer] };
        profile.EnsureLayers();
        profile.SyncRootPropertiesFromActiveLayer();

        var wheel = new RadialWindow(Origin, profile);
        Check(!wheel.IsVisible && PresentationSource.FromVisual(wheel) == null, "classic renderer uses an off-screen window without HWND");
        var canvas = new Canvas { Width = 600, Height = 600 };
        Field(wheel, "WheelCanvas", canvas);
        Field(wheel, "_canvasCenter", 300d);
        Field(wheel, "_innerRadius", config.InnerRadius);
        Field(wheel, "_outerRadius", config.WheelRadius);
        Call(wheel, "EnableFixedGestureLayout");
        Call(wheel, "PrepareFixedLayout", Origin);
        Call(wheel, "RenderSectors");
        var roots = canvas.Children.OfType<System.Windows.Shapes.Path>().Where(path => path.Tag is int).ToArray();
        Check(roots.Length == 4 && roots.Where(path => path.Visibility == Visibility.Visible)
            .Select(path => (int)path.Tag).ToHashSet().SetEquals([0, 2]),
            "classic primary draws only configured roots or ancestors of a configured child");

        Call(wheel, "ShowSubTier", 0);
        Check(ActiveChildPaths().Select(path => path.Tag).SequenceEqual(new object[] { "sub_0_2" }),
            "only the configured third secondary has a path after a cold branch open");
        Call(wheel, "ClearSubTier");
        Call(wheel, "ShowSubTier", 0);
        var addresses = (List<int>)typeof(RadialWindow).GetField("_subSectorChildIndices", Members)!.GetValue(wheel)!;
        Check(ActiveChildPaths().Select(path => path.Tag).SequenceEqual(new object[] { "sub_0_2" }) &&
            addresses.SequenceEqual([2]), "warm sparse secondary cache preserves the original child address");

        IEnumerable<System.Windows.Shapes.Path> ActiveChildPaths() =>
            canvas.Children.OfType<System.Windows.Shapes.Path>().Where(path => path.Tag is string && path.Visibility == Visibility.Visible);
    }

    private static void CheckPersistentFanRelease(System.Windows.Shapes.Path[] petals)
    {
        var cfg = ConfigManager.CurrentConfig;
        var rootShapes = Enumerable.Range(0, Layer.SectorCount)
            .Select(i => ((Geometry Shape, Point Label))Call(currentGeometry, "Primary", cfg, i)!).ToArray();
        Point east = Physical(rootShapes[0].Label), south = Physical(rootShapes[1].Label);
        double r = (double)Call(currentGeometry, "Outer", 2)! * .8;
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
            var oldRelease = Petal(1,0).Label;
            Check(Petal(0,2).Shape.FillContains(oldRelease) && Petal(1,0).Shape.FillContains(oldRelease),
                "legacy two-level Fan fixture retains the overlapping petals");
            double extent = (double)legacy.GetMethod("SecondaryExtent",flags)!.Invoke(null,[config,72d,36d])!;
            var geometry = New(Host.GetType("WinPieGestures.FixedRingGeometry")!,8,72d,extent + 5,1d,true);
            (Geometry Shape, Point Label) Cell(int root, int child) =>
                ((Geometry,Point))Call(geometry, "Secondary", config, root, child, 3)!;
            var entry = Cell(0,1).Label;
            var release = Cell(1,0).Label;
            Check(!Cell(0,2).Shape.FillContains(release) && Cell(1,0).Shape.FillContains(release),
                "the same adjacent parents occupy distinct cells on the deep wheel");
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
                    $"adjacent cells honor the owning parent with lock={locked}, queued [{string.Join(",",names)}]");
            }
        }
        finally { current.SetValue(null,saved); }
    }
    private static void CheckResidentAncestors(AppConfig config)
    {
        string savedStyle = config.SubmenuStyle;
        bool savedExpand = config.AutoExpandSubRingsOnPopup, savedInherit = config.EnableGlobalInheritance;
        var savedProfiles = config.Profiles;
        config.SubmenuStyle = "Fan";
        config.AutoExpandSubRingsOnPopup = true;
        config.EnableGlobalInheritance = true;
        var menu = new FixedRingMenu { AlwaysVisible = true };
        var layer = new WheelLayer { SectorCount = 4, MaxMenuDepth = 5, FixedRings = menu };
        layer.Actions = [
            new() { Type = "None", SubActions = [Text("must stay disabled")] },
            new() { Type = "Inherit", SubActions = [Text("stale local child")] },
            new() { Type = "Hotkey", SubActions = [new() { Type = "Hotkey", Name = "nav" },
                new() { Type = "None", SubActions = [Text("disabled child tree")] }] },
            new() { Type = "Inherit", SubActions = [Text("shadowed local")] }
        ];
        var profile = new WheelProfile { ProcessName = "resident.test", Layers = [layer] };
        var inherited = Text("global owner");
        inherited.SubActions = [Text("inherited child")];
        var global = new WheelProfile { ProcessName = "Global", Layers = [
            new() { SectorCount = 4, Actions = [Text("blocked global"), new() { Type = "None" }, new(), inherited] }] };
        config.Profiles = [global, profile];
        try
        {
            global.EnsureLayers();
            profile.EnsureLayers();
            void Slot(int level, int index, ActionItem action, int? parent = null) =>
                menu.SetSlot(level, index, new() { Action = action, ParentIndex = parent });
            Slot(3,0,Text("blocked via root"),FixedRingMenu.SecondaryKey(0,0));
            Slot(4,0,Text("blocked grandchild"),0);
            Slot(3,1,new() { Type = "Text", Parameter = "  " });
            Slot(3,2,new() { Type = "String", Parameter = "" });
            Slot(3,3,new() { Type = "Plugin", PluginActionRef = new() { PluginId = "only-plugin" } });
            Slot(3,4,new() { Type = "Hotkey" },FixedRingMenu.SecondaryKey(2,0));
            Slot(4,4,Text("navigable grandchild"),4);
            Slot(3,5,new() { Type = "None" });
            Slot(4,5,Text("blocked fixed child"),5);
            Slot(3,6,Text("blocked via child"),FixedRingMenu.SecondaryKey(2,1));
            Slot(3,7,Text("blocked unresolved inheritance"),FixedRingMenu.SecondaryKey(1,0));
            Slot(3,8,Text("independent"));
            // Older Fan configurations can store a secondary address outside the rendered range.
            Slot(3,9,Text("hidden address link"),FixedRingMenu.SecondaryKey(3,4));
            var geometry = New(Host.GetType("WinPieGestures.FixedRingGeometry")!,4,72d,121d,1d,true);
            var session = New(Nested("FixedRingSession"),profile,layer,18L,geometry,Origin,Dpi,140d);
            Call(session,"SetShownSecondary",-1,true);
            Call(session,"Present");
            PaintFrame(session);
            var inheritedKey = FixedRingMenu.SecondaryKey(3,0);
            var expected = new HashSet<(int,int)> { (1,3), (2,inheritedKey), (3,0), (4,0), (4,4), (4,5), (3,6), (3,7), (3,8), (3,9) };
            var configuredSlots = typeof(FixedRingMenu).GetMethod("ConfiguredSlots", Members, [typeof(WheelProfile), typeof(int), typeof(bool)])!;
            var display = (HashSet<(int,int)>)configuredSlots.Invoke(menu, [profile, 5, true])!;
            var runtime = (HashSet<(int,int)>)session.GetType().GetField("_configuredSlots", Members)!.GetValue(session)!;
            if (!display.SetEquals(expected) || !runtime.SetEquals(expected))
                throw new InvalidOperationException($"Resident preview/runtime occupancy: unexpected [{string.Join(",", display.Except(expected))}], missing [{string.Join(",", expected.Except(display))}]");
            foreach (var (level, index) in new[] { (2,0), (2,FixedRingMenu.SecondaryKey(1,0)), (2,FixedRingMenu.SecondaryKey(2,0)),
                    (2,FixedRingMenu.SecondaryKey(2,1)), (3,1), (3,2), (3,3), (3,4), (3,5) })
            {
                var hit = New(Nested("RingHit"),level,index);
                if (Resolve(session,hit).Action != null || (bool)Call(session, "WasVisible", hit)!)
                    throw new InvalidOperationException($"Resident disabled/invalid slot {level}/{index} is visible or confirmable");
            }
            var inheritedHit = New(Nested("RingHit"),2,inheritedKey);
            Check(Resolve(session,inheritedHit).Action?.Name == "inherited child" &&
                Resolve(session,New(Nested("RingHit"),4,4)).Action?.Name == "navigable grandchild",
                "resident Fan retains the inherited legacy child and keeps a configured fixed slot with a blank former parent");
            var hiddenLink = New(Nested("RingHit"),3,9);
            Check(Resolve(session,hiddenLink).Action?.Name == "hidden address link", "resident fixed slot remains selectable despite an obsolete layout-hidden secondary address");
            var prior = layer.Actions[3];
            layer.Actions[3] = new() { Type = "None" };
            Check(Resolve(session,hiddenLink).Action?.Name == "hidden address link" && Resolve(session,inheritedHit).Action == null,
                "disabling a previously inherited root invalidates its legacy child without invalidating a fixed slot");
            layer.Actions[3] = prior;
            var disabledPoint = ((Geometry Shape, Point Label))Call(geometry,"Secondary",config,0,0,1)!;
            int previous = Recorded.Count;
            var controller = Harness(session,profile);
            Call(controller,"TestProcessMove",Physical(disabledPoint.Label));
            Call(controller,"TestCompleteMouseTriggerRelease",Physical(disabledPoint.Label),"RightButton");
            Pump();
            Check(Recorded.Count == previous, "releasing over the child of an explicitly disabled root queues no action");
        }
        finally
        {
            config.SubmenuStyle = savedStyle;
            config.AutoExpandSubRingsOnPopup = savedExpand;
            config.EnableGlobalInheritance = savedInherit;
            config.Profiles = savedProfiles;
        }
    }

    private static void CheckAppearanceControls(AppConfig original)
    {
        var property = typeof(ConfigManager).GetProperty("CurrentConfig")!;
        var cfg = new AppConfig { Shape = "Original", SubmenuStyle = "Fan", WheelRadius = 100, InnerRadius = 46, CoreRadius = 22,
            SectorGap = 4, SectorCornerRadius = 0 };
        property.SetValue(null, cfg);
        try
        {
            object wheel = RuntimeHelpers.GetUninitializedObject(typeof(RadialWindow));
            long revision = 0;
            object Layout() => Call(wheel, "FitFixedGeometry", 8, cfg.WheelRadius, 155d, 5, 800d, ++revision)!;
            object standard = Layout();
            foreach (string style in new[] { "Fan", "Wheel" })
            {
                cfg.SubmenuStyle = style;
                foreach (string appearance in new[] { "Original", "HexagonHive", "Circle", "RoundedCapsule" })
                {
                    cfg.Shape = appearance;
                    cfg.SectorGap = 4;
                    cfg.SectorCornerRadius = 0;
                    var sharp = Layout();
                    cfg.SectorCornerRadius = 10;
                    var rounded = Layout();
                    cfg.SectorGap = 9;
                    var spaced = Layout();
                    foreach (int level in Enumerable.Range(style == "Fan" ? 1 : 3, style == "Fan" ? 5 : 3))
                    {
                        int count = level == 1 ? 8 : level == 2 ? 24 : FixedRingMenu.RecommendedCount(level);
                        const int slot = 1;
                        Geometry Face(object layout) => (Geometry)Call(layout, "GetShape", level, slot, count)!;
                        var face = Face(rounded);
                        var area = face.GetArea();
                        var center = (Point)Call(rounded, "GetCenter", level, slot, count)!;
                        if (appearance == "Circle" && (Math.Abs(face.Bounds.Width - face.Bounds.Height) > .05 ||
                            Math.Abs(area / (face.Bounds.Width * face.Bounds.Height) - Math.PI / 4) > .008))
                            throw new InvalidOperationException($"{style} level {level}: Circle did not paint a circle");
                        if (appearance != "Circle" && Math.Abs(area - Face(sharp).GetArea()) < .01)
                            throw new InvalidOperationException($"{style} level {level}: corner setting did not change the face");
                        if (Face(spaced).GetArea() >= area - .01)
                            throw new InvalidOperationException($"{style} level {level}: gap setting did not shrink the face");
                        for (int y = -5; y <= 5; y++)
                        for (int x = -5; x <= 5; x++)
                        {
                            var probe = center + new Vector(face.Bounds.Width * (x + .17) / 10, face.Bounds.Height * (y + .29) / 10);
                            var hit = (int)Call(rounded, "Hit", probe, level, count)!;
                            if ((hit == slot) != face.FillContains(probe, .001, ToleranceType.Absolute))
                                throw new InvalidOperationException($"{style}/{appearance} {level}/{slot} mismatch at {probe}");
                        }
                    }
                    Check(true, $"{style}/{appearance} consumes the shared Tab 1 gap and corner with painted/hit contours");
                }
            }
            cfg.SubmenuStyle = "Wheel";
            cfg.WheelRadius = 70;
            cfg.InnerRadius = 65;
            cfg.SectorGap = 8;
            foreach (string shapeName in new[] { "Circle", "RoundedCapsule", "HexagonHive" })
            {
                cfg.Shape = shapeName;
                var tight = Call(wheel, "FitFixedGeometry", 12, 70d, 110d, 5, 119d, ++revision)!;
                for (int level = 3; level <= 5; level++)
                {
                    int count = FixedRingMenu.MaximumCount(level);
                    var face = (Geometry)Call(tight, "GetShape", level, 0, count)!;
                    var center = (Point)Call(tight, "GetCenter", level, 0, count)!;
                    var edge = new Point(center.X + face.Bounds.Width * .45, center.Y);
                    if (!face.FillContains(edge) || (int)Call(tight, "Hit", edge, level, count)! != 0)
                        throw new InvalidOperationException($"{shapeName} compressed Wheel misses its painted outer side at level {level}");
                }
            }
            Check(true, "narrow rings remain within their rendered hit boundaries after screen fitting");
            cfg.WheelRadius = 100;
            cfg.InnerRadius = 46;
            cfg.SubmenuStyle = "Fan";
            cfg.Shape = "Original";
            cfg.SectorGap = 4;
            cfg.SectorCornerRadius = 0;
            Point PointAt(object g) => (Point)Call(g, "GetCenter", 3, 0, 12)!;
            cfg.InnerRadius = 60;
            Check(PointAt(Layout()) != PointAt(standard), "inner radius changes the deep lattice pitch");
            cfg.WheelRadius = 124;
            var larger = Layout();
            Check(PointAt(larger).X > PointAt(standard).X, "wheel radius scales all menu tiers");
            cfg.CoreRadius = 65;
            var coreLayout = Layout();
            Check(Math.Abs((double)Call(coreLayout, "CoreRadius", 65d)! - 65) < .01 &&
                (double)Call(coreLayout, "Inner", 1)! > 65, "larger circular core remains clear of primary cells");
        }
        finally { property.SetValue(null, original); }
    }

    private static void CheckFanGeometry(AppConfig config)
    {
        object wheel = RuntimeHelpers.GetUninitializedObject(typeof(RadialWindow));
        object Fitted(string style)
        {
            config.SubmenuStyle = style;
            return Call(wheel, "FitFixedGeometry", 4, 72d, 116d, 5, 430d, 1L)!;
        }
        CheckResidentAncestors(config);
        CheckAppearanceControls(config);
        double oldCorner = config.SectorCornerRadius;
        config.SectorCornerRadius = 0;
        var circular = Fitted("Wheel");
        var honeycomb = Fitted("Fan");
        Geometry Shape(object layout, int level, int index, int count) =>
            (Geometry)Call(layout, "GetShape", level, index, count)!;
        // Start with existing production APIs so the old candidate can demonstrate the actual visual defect.
        double Coverage(Geometry s) => s.GetArea() / (s.Bounds.Width * s.Bounds.Height);
        foreach (int roots in new[] { 4, 8, 12 })
        {
            var layout = Call(wheel, "FitFixedGeometry", roots, 72d, 116d, 5, 430d, 1L)!;
            var cells = new List<(Geometry Shape, Point Center)>();
            for (int level = 1; level <= 5; level++)
            {
                int count = level == 1 ? roots : level == 2 ? roots * 3 : FixedRingMenu.MaximumCount(level);
                for (int index = 0; index < count; index++)
                {
                    var shape = Shape(layout, level, index, count);
                    var center = (Point)Call(layout, "GetCenter", level, index, count)!;
                    if (Coverage(shape) is < .745 or > .755)
                        throw new InvalidOperationException($"Deep Fan {roots} sectors {level}/{index} paints a non-hexagonal face: {shape.Bounds}");
                    CheckCell(level, index, count, shape, center);
                    cells.Add((shape,center));
                }
            }
            void CheckCell(int level, int index, int count, Geometry shape, Point center)
            {
                if ((int)Call(layout, "Hit", center, level, count)! != index || !shape.FillContains(center) ||
                    cells.Any(other => (other.Center-center).Length < shape.Bounds.Width))
                    throw new InvalidOperationException($"Deep Fan {roots} sectors {level}/{index} overlaps or misses");
                if (level != 2) return;
                for (int siblings = index % 3 + 1; siblings <= 3; siblings++)
                {
                    var child = ((Geometry Shape, Point Label))Call(layout, "Secondary", config, index / 3, index % 3, siblings)!;
                    if (!ReferenceEquals(child.Shape, shape) || child.Label != center)
                        throw new InvalidOperationException("Changing a sibling moved a reserved Fan address");
                }
            }
            Check(true, $"Deep Fan {roots} sectors fits all parent and child addresses on a single nonoverlapping lattice");
        }
        var oldShape = Shape(circular, 3, 0, 4);
        var fanShape = Shape(honeycomb, 3, 0, 4);
        Check(Coverage(fanShape) is > .70 and < .80 && fanShape.Bounds.Width / fanShape.Bounds.Height is > .8 and < 1.2 &&
            Math.Abs(fanShape.GetArea() - oldShape.GetArea()) > oldShape.GetArea() * .05,
            $"Fan level three draws a compact hexagonal cell distinct from the Wheel quarter-sector (Fan {fanShape.Bounds}, Wheel {oldShape.Bounds})");

        for (int level = 3; level <= 5; level++)
        {
            foreach (int count in new[] { 4, 7, FixedRingMenu.RecommendedCount(level), FixedRingMenu.MaximumCount(level) })
            {
                int shell = level + 2;
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
            var fitted = Call(wheel, "FitFixedGeometry", 4, 72d, 116d, 5, 150d, 2L)!;
            double scale = (double)Get(fitted,"Scale")!;
            double extent = (double)Call(fitted,"OuterForCount",5,slots)!;
            Check(extent <= 150.00001 && scale < 1 && extent > 149.999,
                $"150-DIP Fan fit scales every tier together for {slots}-slot fifth shell: scale={scale:F6}, extent={extent:F4}");
        }
        var restored = Fitted("Wheel");
        config.SectorCornerRadius = oldCorner;
        Check(Shape(restored, 3, 0, 4).Bounds == oldShape.Bounds &&
            Shape(restored, 3, 0, 4).GetArea() == oldShape.GetArea(), "switching Fan back to Wheel restores original fitted annular geometry");
    }
}
