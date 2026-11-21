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
        Check(menu.GetSlot(3,0)?.Action.Name == "Insert" && menu.GetSlot(3,3)?.ParentIndex == FixedRingMenu.SecondaryKey(1,0), "distinct legacy secondary parents keep distinct descendants");
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
        menu.SetSlot(5,36,new FixedRingSlot { ParentIndex = null, Action = new ActionItem { Name="Tail", Type="Text", Parameter="T", IconKey="custom:leaf" } });
        menu.SetCount(5,4); menu.AlwaysVisible=false; layer.FixedRingsPaused=true;
        var clone=profile.Clone("Copy");
        clone.Layers[0].FixedRings!.GetSlot(5,36)!.Action.Parameter="X";
        Check(menu.GetSlot(5,36)!.Action.Parameter=="T" && clone.Layers[0].ActiveFixedRings==null, "deep clone keeps paused state and hidden slots independent");
        string config=JsonSerializer.Serialize(new { Profiles = new[]{profile} });
        var copy=JsonDocument.Parse(config).RootElement.GetProperty("Profiles")[0].Deserialize<WheelProfile>()!.Layers[0];
        copy.FixedRingsPaused=false;
        var restored=copy.ActiveFixedRings!;
        restored.SetCount(5,37);
        Check(restored.GetCount(3)==7 && restored.GetSlot(5,36)!.Action.Parameter=="T" && restored.GetSlot(5,35)==null &&
              !restored.AlwaysVisible, "sparse data, odd counts and hidden slot survive disk round trip");
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
        string remap=importedLayer.FixedRings!.GetSlot(5,36)!.Action.IconKey;
        Check(remap.StartsWith("custom:") && remap!="custom:leaf", "portable custom icon collision remaps even in hidden slot of paused layer");

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
}
