using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using WinPieGestures;

internal static class Program
{
    private static int _checks;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        Console.WriteLine($"PASS {++_checks} {name}");
    }

    [STAThread]
    private static void Main(string[] args)
    {
        string folder = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(folder);
        foreach (var key in new[] { "APPDATA", "LOCALAPPDATA", "TEMP", "TMP" }) Environment.SetEnvironmentVariable(key, folder);
        string candidate = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "CandidateDll").Value!;
        Check(args.Contains("--test-instance") && SHA256.HashData(File.ReadAllBytes(candidate)).SequenceEqual(
            SHA256.HashData(File.ReadAllBytes(typeof(FixedRingMenu).Assembly.Location))), "exact candidate and isolated entry");
        var profile = new WheelProfile { ProcessName = "Global", Layers = { new WheelLayer { SectorCount = 4, MaxMenuDepth = 5 } } };
        profile.EnsureLayers();
        var layer = profile.Layers[0];
        Check(layer.ActiveFixedRings == null && !JsonSerializer.Serialize(layer).Contains("FixedRings"), "legacy layers do not opt in or emit new fields");
        profile.Actions[0] = new() { Name = "Tools", SubActions = { new() { Name = "Edit", SubActions = { new() { Name = "Insert", Type = "Text", Parameter = "I" } } } } };
        profile.Actions[1] = new() { Name = "Window", SubActions = { new() { Name = "Tile", SubActions = { new() { Name = "Docked" } } } } };
        var menu = layer.FixedRings = FixedRingMenu.FromLegacy(profile);
        Check(menu.GetSlot(3,0)?.Action.Name == "Insert" && menu.GetSlot(3,3)?.Action.Name == "Docked", "conversion places legacy descendants near their original directions");
        Check(profile.GetEffectiveMenuAction([0,0,0])?.Name == "Insert" && profile.GetEffectiveMenuAction([1,0,0]) == null, "third-level routing checks both lower levels");
        menu.GetSlot(3,0)!.Action.Name = "Edited";
        Check(profile.Actions[0].SubActions[0].SubActions[0].Name == "Insert", "conversion leaves the source tree intact");
        Check(Enumerable.Range(3,3).Select(menu.GetCount).SequenceEqual(new[]{12,16,20}) &&
              Enumerable.Range(3,3).Select(FixedRingMenu.MaximumCount).SequenceEqual(new[]{23,30,37}), "circumference based limits");
        var keys = new HashSet<int>();
        for (int p = 0; p < 32; p++)
        for (int c = 0; c < 16; c++)
        {
            int key = FixedRingMenu.SecondaryKey(p,c);
            if (!keys.Add(key) || !FixedRingMenu.TryDecodeSecondaryKey(key,out int root,out int child) || root != p || child != c) throw new InvalidOperationException("secondary address collision");
        }
        Check(keys.Count == 512, "wide legacy secondary menus have unique reversible addresses");
        foreach (string style in new[]{"Wheel","Fan"})
        {
            var cfg = new AppConfig { SubmenuStyle = style };
            var primary = LegacyRingGeometry.Primary(cfg,4,0,72,30);
            if (!primary.Shape.FillContains(primary.Label)) throw new InvalidOperationException("primary centre");
            double extent = LegacyRingGeometry.SecondaryExtent(cfg,72,30);
            for (int j=0; j<3; j++)
            {
                var item = LegacyRingGeometry.Secondary(cfg,4,1,j,3,72,30);
                if (!item.Shape.FillContains(item.Label) || ((Vector)item.Label).Length >= extent) throw new InvalidOperationException("secondary centre or extent");
            }
            var compact = new FixedRingGeometry(4,72,extent+5,.6);
            Check(compact.Inner(3) > extent && compact.Outer(5) > compact.Outer(4), style + " lower geometry leaves room for three distinct outer tiers");
        }
        var narrow = new AppConfig { SubmenuStyle = "Wheel", Shape = "HexagonHive", SubWheelOuterRadius = 148, SubWheelInnerGap = 7, SubWheelCornerRadius = 0 };
        var protruding = LegacyRingGeometry.Secondary(narrow,4,0,0,1,133,45).Shape;
        var tip = new Point(154,0);
        Check(protruding.FillContains(tip) && LegacyRingGeometry.SecondaryExtent(narrow,133,45) >= tip.X,
            "narrow legacy hexagon keeps its selectable outer tip before tier three begins");
        menu.SetCount(3,7); menu.SetCount(5,37);
        menu.SetSlot(5,36,new FixedRingSlot { ParentIndex = 123, Action = new ActionItem { Name="Tail", Type="Text", Parameter="T", IconKey="custom:leaf" } });
        menu.SetCount(5,4); menu.AlwaysVisible=false; layer.FixedRingsPaused=true;
        var clone=profile.Clone("Copy");
        clone.Layers[0].FixedRings!.GetSlot(5,36)!.Action.Parameter="X";
        Check(menu.GetSlot(5,36)!.Action.Parameter=="T" && clone.Layers[0].ActiveFixedRings==null &&
            clone.Layers[0].FixedRings!.GetSlot(5,36)!.ParentIndex==123, "deep clone keeps paused state, hidden slots and old parent metadata");
        string config=JsonSerializer.Serialize(new { Profiles = new[]{profile} });
        var copy=JsonDocument.Parse(config).RootElement.GetProperty("Profiles")[0].Deserialize<WheelProfile>()!.Layers[0];
        copy.FixedRingsPaused=false;
        var restored=copy.ActiveFixedRings!;
        restored.SetCount(5,37);
        Check(restored.GetCount(3)==7 && restored.GetSlot(5,36)!.Action.Parameter=="T" && restored.GetSlot(5,35)==null &&
              restored.GetSlot(5,36)?.ParentIndex==123 && !restored.AlwaysVisible, "sparse data, odd counts and hidden slot metadata survive disk round trip");
        var oldMenu = JsonSerializer.Deserialize<FixedRingMenu>("""{"Tiers":[{"WakeBandWidth":24,"DwellMs":0}]}""")!;
        var legacy = JsonSerializer.SerializeToElement(oldMenu.Clone()).GetProperty("Tiers")[0];
        var fresh = JsonSerializer.SerializeToElement(new FixedRingTier());
        Check(legacy.GetProperty("WakeBandWidth").GetInt32()==24 && legacy.GetProperty("DwellMs").GetInt32()==0 &&
            !fresh.TryGetProperty("WakeBandWidth",out _) && !fresh.TryGetProperty("DwellMs",out _),
            "existing wake settings round-trip, new tiers store only current settings");
        var src=Directory.CreateDirectory(Path.Combine(folder,"source")).FullName;
        var dest=Directory.CreateDirectory(Path.Combine(folder,"dest")).FullName;
        File.WriteAllText(Path.Combine(src,"leaf.svg"),"<svg xmlns=\"http://www.w3.org/2000/svg\"><path d=\"M0 0 L5 5\"/></svg>");
        File.WriteAllText(Path.Combine(dest,"leaf.svg"),"<svg xmlns=\"http://www.w3.org/2000/svg\"><path d=\"M0 0 L2 2\"/></svg>");
        string package=Path.Combine(folder,"fixed.spconfig"); PortableConfigBundle.Export(config,package,src);
        using var imported=PortableConfigBundle.PrepareImport(package,dest);
        var importedLayer=JsonDocument.Parse(imported.ConfigJson).RootElement.GetProperty("Profiles")[0].Deserialize<WheelProfile>()!.Layers[0];
        var importedSlot=importedLayer.FixedRings!.GetSlot(5,36)!;
        string remap=importedSlot.Action.IconKey;
        Check(remap.StartsWith("custom:") && remap!="custom:leaf" && importedSlot.ParentIndex==123,
            "portable custom icon collision remaps in hidden slot of paused layer and preserves old metadata");

        CheckNewAppearanceGeometry();
        CheckConfiguredVisibility();

        int positions=0;
        foreach (double radius in new[]{54.0,72.0,121.5})
        {
            var geometry=new FixedRingGeometry(4,radius);
            foreach (int level in Enumerable.Range(3,3))
            foreach (int count in Enumerable.Range(4,FixedRingMenu.MaximumCount(level)-3))
            for(int i=0;i<count;i++)
            {
                double angle=2*Math.PI*i/count;
                Point At(double r, double a) => new(r*Math.Cos(a),r*Math.Sin(a));
                double outer=geometry.Outer(level),mid=(geometry.Inner(level)+outer)/2;
                var p=At(mid,angle);
                var nearEdge=At(outer-3*geometry.Scale,angle);
                var boundary=At(mid,angle+Math.PI/count);
                var shape=geometry.GetShape(level,i,count);
                if(!shape.IsFrozen || !shape.FillContains(p) || geometry.Hit(p,level,count)!=i ||
                   geometry.Hit(boundary,level,count)!=-1 ||
                   !shape.FillContains(nearEdge) || geometry.Hit(nearEdge,level,count)!=i)
                    throw new InvalidOperationException($"level {level} count {count} slot {i} radius {radius}");
                positions++;
            }
        }
        Check(positions>4000,$"{positions} rounded slot centres, angular gaps and outer edges");
        var warm=new FixedRingGeometry(4,72);
        Point middle=new(139,0),edge=new(157,0);
        for(int i=0;i<100;i++){warm.Hit(middle,3,12);warm.Hit(edge,3,12);}
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<20000;i++){warm.Hit(middle,3,12);warm.Hit(edge,3,12);}
        Check(GC.GetAllocatedBytesForCurrentThread()==before,"warm centre and outer-edge hit tests allocate zero on the input thread");
        if (args.Length > 2)
        {
            var demo = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(args[2]))!.Profiles.Single();
            demo.EnsureLayers();
            Check(demo.Layers.Count == 3 && demo.Layers[0].ActiveFixedRings?.AlwaysVisible == true &&
                  demo.Layers[1].ActiveFixedRings?.AlwaysVisible == false && demo.Layers[2].ActiveFixedRings == null &&
                  demo.GetEffectiveMenuAction([0,0,0,0,0],layerIndex:1)?.Parameter == "[T8-A]" &&
                  demo.GetEffectiveMenuAction([1,0,3],layerIndex:0)?.Name == "靠右" &&
                  demo.Layers[0].FixedRings!.GetSlot(5,10)?.Action.Parameter == "[T8-独立]", "isolated manual preview seed resolves both branches, resident/step modes and original two-level comparison");
        }
        Console.WriteLine($"PASS all {_checks} model/geometry gates");
    }

    private static void CheckNewAppearanceGeometry()
    {
        static bool Near(double a, double b) => Math.Abs(a - b) < .0001;
        var old = JsonSerializer.Deserialize<AppConfig>("""{"WheelRadius":133,"InnerRadius":70,"Shape":"Circle"}""")!;
        string oldSaved = JsonSerializer.Serialize(old);
        Check(!oldSaved.Contains("SubWheelShape") && !oldSaved.Contains("FixedOuterRingAppearance") &&
            !oldSaved.Contains("FixedHoneycombAppearance") && LegacyRingGeometry.SecondaryShape(old) == "Circle",
            "legacy configuration keeps inherited shape and leaves the optional appearance fields absent");

        var before = LegacyRingGeometry.Primary(old, 8, 0, old.WheelRadius, old.InnerRadius);
        var child = LegacyRingGeometry.Secondary(old, 8, 0, 0, 2, old.WheelRadius, old.InnerRadius);
        old.SubWheelShape = "Original";
        var after = LegacyRingGeometry.Primary(old, 8, 0, old.WheelRadius, old.InnerRadius);
        var shapedChild = LegacyRingGeometry.Secondary(old, 8, 0, 0, 2, old.WheelRadius, old.InnerRadius);
        Check(before.Shape.Bounds == after.Shape.Bounds && child.Label == shapedChild.Label && child.Shape.Bounds != shapedChild.Shape.Bounds,
            "secondary shape changes independently of the primary and leaves outer-ring child directions intact");

        var cfg = new AppConfig { WheelRadius = 72, InnerRadius = 32, SubmenuStyle = "Wheel",
            FixedOuterRingAppearance = new() { Shape = "Original", RingGap = 9, Thickness = 30, SectorGap = 3,
                Level4 = new() { Thickness = 48, SectorGap = 10 } },
            FixedHoneycombAppearance = new() { Shape = "HexagonHive", CellWidth = 26, CellGap = 8, CornerRadius = 0, CoreRadius = 18 }
        };
        var roundTrip = JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(cfg))!;
        var appearance = roundTrip.FixedOuterRingAppearance!;
        Check(appearance.Level4 is { Thickness: 48, SectorGap: 10 } && appearance.Level3 == null &&
            appearance.RingGap == 9 && roundTrip.FixedHoneycombAppearance?.CellGap == 8,
            "per-tier and unified honeycomb values round-trip in configuration");
        static FixedRingGeometry Make(AppConfig cfg, double second, int depth = 5, double limit = 2000)
        {
            double unit = FixedRingGeometry.FitScale(cfg, 8, cfg.WheelRadius, second, depth, limit);
            double start = FixedRingGeometry.GetThirdInnerRadius(cfg, second, unit, cfg.WheelRadius);
            return new(8, cfg.WheelRadius, start, unit, cfg.SubmenuStyle == "Fan", cfg);
        }
        var outer = Make(roundTrip, 120);
        Check(Near(outer.Inner(3), 129) && Near(outer.Outer(3) - outer.Inner(3), 30) &&
            Near(outer.Outer(4) - outer.Inner(4), 48) && Near(outer.Outer(5) - outer.Inner(5), 30) &&
            Near(outer.Inner(4) - outer.Outer(3), 9), "outer-ring overrides change only their own thickness and continue downstream placement");
        var fourth = outer.GetShape(4, 0, 16);
        appearance.Level4.SectorGap = 0;
        var noGap = Make(roundTrip, 120);
        Check(fourth.Bounds.Height < noGap.GetShape(4, 0, 16).Bounds.Height &&
            outer.GetShape(3, 0, 12).Bounds == noGap.GetShape(3, 0, 12).Bounds &&
            outer.GetCenter(4, 8, 16) == noGap.GetCenter(4, 8, 16),
            "per-tier sector spacing changes the chosen outline without shifting sector origins");
        Check(Near(Make(roundTrip, 120, limit: 190).Outer(5), 190), "fitted work-area extent uses the actual custom thicknesses and gaps");

        roundTrip.SubmenuStyle = "Fan";
        appearance.RingGap = 0;
        var honey = roundTrip.FixedHoneycombAppearance!;
        var hex = Make(roundTrip, 0);
        var h = hex.GetCenter(4, 3, 16);
        Check(Near(hex.GetShape(4, 3, 16).Bounds.Width, 26) && hex.Hit(h, 4, 16) == 3 &&
            Near(hex.CoreRadius(36), 18) && Near(hex.CoreDeadzoneRadius(9), 9),
            "honeycomb uses explicit shared DIP width, stable centre hit and a separate visible core radius");

        honey.Shape = "Circle";
        var circle = Make(roundTrip, 0);
        Check(circle.GetCenter(4, 3, 16) == h &&
            Near(circle.GetShape(4, 3, 16).Bounds.Width, 26) &&
            circle.GetShape(4, 3, 16).Bounds.Height < hex.GetShape(4, 3, 16).Bounds.Height &&
            circle.Hit(h, 4, 16) == 3, "honeycomb outline changes without moving or remapping addresses");

        honey.CellGap = 16;
        var widerGap = Make(roundTrip, 0);
        Check(widerGap.Outer(5) > circle.Outer(5) && widerGap.GetCenter(4, 3, 16) != h &&
            Near(widerGap.CellWidth(4, 16), 26), "honeycomb spacing translates the same-width outlines on a single lattice");

        honey.Shape = "HexagonHive";
        var sharp = Make(roundTrip, 0);
        double area = sharp.GetShape(3, 0, 12).GetArea();
        honey.CornerRadius = 4;
        var rounded = Make(roundTrip, 0);
        Check(rounded.GetShape(3, 0, 12).GetArea() < area &&
            sharp.GetCenter(3, 0, 12) == rounded.GetCenter(3, 0, 12), "honeycomb corner rounding preserves its lattice");
    }

    private static void CheckConfiguredVisibility()
    {
        var placeholder = new ActionItem { Type = "Text", Name = "Unconfigured" };
        var configured = new ActionItem { Type = "Text", Name = "Configured", Parameter = "T8" };
        var branch = new ActionItem { Type = "Hotkey", Name = "Branch", SubActions = [placeholder, configured] };
        var nested = new ActionItem { Type = "Hotkey", Name = "Nested", SubActions = [branch] };
        ActionItem?[] roots = [placeholder, branch, nested, null];
        var one = FixedRingMenu.LowerConfiguredSlots(roots, 1, false);
        var two = FixedRingMenu.LowerConfiguredSlots(roots, 2, false);
        var three = FixedRingMenu.LowerConfiguredSlots(roots, 3, true);
        Check(one.Count == 0 && two.SetEquals([(1, 1), (2, FixedRingMenu.SecondaryKey(1, 1))]),
            "legacy invocation hides editable placeholders and keeps a valid child at its original physical address");
        Check(three.Contains((1, 2)) && three.Contains((2, FixedRingMenu.SecondaryKey(2, 0))),
            "progressive invocation retains navigation-only branches with reachable actions");
        var deep = JsonSerializer.Deserialize<FixedRingMenu>("""
            {"Tiers":[{"Slots":[{"ParentIndex":8,"Action":{"Type":"Text","Name":"Third","Parameter":"T8"}}]},
                       {"Slots":[{"ParentIndex":0,"Action":{"Type":"Text","Name":"Blank"}}]},
                       {"Slots":[{"ParentIndex":99,"Action":{"Type":"Text","Name":"Fifth","Parameter":"T8"}}]}]}
            """)!;
        var fixedVisible = deep.ConfiguredSlots(roots, 5, true);
        Check(fixedVisible.SetEquals([(1, 1), (2, FixedRingMenu.SecondaryKey(1, 1)), (3, 0), (5, 0)]) &&
            deep.GetSlot(3, 0)?.ParentIndex == 8 && deep.GetSlot(5, 0)?.ParentIndex == 99,
            "fixed occupancy ignores old parent links, keeps a populated fifth tier across a blank fourth and retains stored metadata");
    }

}
