using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Windows;
using WinPieGestures;

internal static class Program
{
    const BindingFlags Methods = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly Type Controller = typeof(GestureController);
    static int checks;

    static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        Console.WriteLine("PASS " + name);
        checks++;
    }
    static object Construct(Type t, params object?[] args) => Activator.CreateInstance(t, Methods, null, args, null)!;
    static object? Call(object instance, string name, params object?[] args) =>
        instance.GetType().GetMethod(name, Methods)!.Invoke(instance, args);
    static void Set(object instance, string field, object? value) =>
        instance.GetType().GetField(field, Methods)!.SetValue(instance, value);
    static ActionItem Node(string name, params ActionItem[] children) =>
        new() { Type = "Text", Name = name, Parameter = name, SubActions = [..children] };

    [STAThread]
    static void Main(string[] args)
    {
        string sandbox = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(sandbox);
        foreach (var name in new[] { "LOCALAPPDATA", "APPDATA", "TEMP", "TMP" })
            Environment.SetEnvironmentVariable(name, sandbox);
        Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Controller.Assembly.Location))) == args[1], "candidate identity");
        var config = new AppConfig { EnableMultiTier = true };
        typeof(ConfigManager).GetProperty("CurrentConfig")!.SetValue(null, config);
        var leaf = Node("five");
        var level4 = Node("four", leaf, Node("sibling"), new ActionItem
        {
            Type = "Plugin", PluginActionRef = new() { PluginId = "fixture.missing", ContributionId = "missing" }
        });
        var level3 = Node("three", level4);
        var level2 = Node("two", level3, new ActionItem { Type = "None" });
        var root = Node("one", level2);
        var layer = new WheelLayer { MaxMenuDepth = 5, SectorCount = 4, Actions = [root],
            EnableCenterAction = true, CenterAction = Node("core") };
        var profile = new WheelProfile { ProcessName = "Global", Layers = [layer] };
        var controller = RuntimeHelpers.GetUninitializedObject(Controller);
        Set(controller, "_uiUpdateSync", new object());
        Set(controller, "_gestureVersion", 12L);
        Type pageType = Controller.GetNestedType("DeepPage", BindingFlags.NonPublic)!;
        Type releaseType = Controller.GetNestedType("DeepRelease", BindingFlags.NonPublic)!;
        var origin = new Point(500, 500);
        object Page(int[] path, ActionItem parent, object? previous = null)
        {
            var frame = Construct(Controller.Assembly.GetType("WinPieGestures.DeepMenuFrame", true)!,
                origin, new DpiScale(1, 1), new Rect(0, 0, 1000, 1000), 18d,
                80d, 130d, 0d, path.Length + 1, parent.SubActions.Count, false, 0d);
            return Construct(pageType, profile, layer, ConfigManager.ConfigurationRevision, 11L, path,
                parent, parent.Name, frame, new Point(490, 500), previous);
        }
        (bool valid, ActionItem? action) Resolve(object? page, int hit, bool escaped = false, bool suppress = false)
        {
            object?[] parameters = [Construct(releaseType, page, hit, suppress), profile, layer, 0, 0, escaped, false, null];
            bool valid = (bool)Call(controller, "ResolveGestureRelease", parameters)!;
            return (valid, parameters[^1] as ActionItem);
        }

        var third = Page([0, 0], level2);
        var fourth = Page([0, 0, 0], level3, third);
        var fifth = Page([0, 0, 0, 0], level4, fourth);
        foreach (var page in new[] { third, fourth })
            Check((bool)Call(page, "CanEnter", 0)! && Resolve(page, 0).action == null, "enter deeper branch suppresses its own action");
        Check(Resolve(null, -1).action == level2, "legacy second-tier parent remains executable");
        Check(Resolve(fifth, 0).action is { Parameter: "five", SubActions.Count: 0 }, "fifth-level selection captures executable snapshot");
        Check(Resolve(fifth, 1).action?.Parameter == "sibling", "sibling has independent path");
        Check(Resolve(fifth, 2).action == null, "unavailable plugin leaf never reaches execution");
        Check(Resolve(fifth, -3).action == layer.CenterAction, "core has separate action");
        Check(Resolve(fifth, -2).action == null && Resolve(fifth, -1).action == null, "back and gaps suppress execution");
        Check(Resolve(third, 1).action == null && Resolve(fifth, 0, true).action == null, "disabled and escaped suppress execution");
        Check(Resolve(null, -1, suppress: true).action == null, "return to first two tiers suppresses parent");
        Check((int)Call(third, "Hit", new Point(490, 500), 1d, 1d)! == -1 &&
              (int)Call(third, "Hit", origin, 1d, 1d)! == -3, "entry position cannot immediately select center");
        leaf.Parameter = "edited";
        Check(!Resolve(fifth, 0).valid, "in-place edit invalidates old execution parameters");
        leaf.Parameter = "five";
        level4.SubActions[0] = Node("five");
        Check(!Resolve(fifth, 0).valid, "replacement slot invalidates old identity");
        level4.SubActions[0] = leaf;
        layer.MaxMenuDepth = 3;
        Check(!Resolve(fifth, 0).valid && Resolve(third, 0).action?.Parameter == "three", "depth cap hides descendants and preserves capped node action");
        layer.MaxMenuDepth = 5;
        Set(controller, "_gestureVersion", 13L);
        Check(!Resolve(fifth, 0).valid, "new activation invalidates pending release");
        Set(controller, "_gestureVersion", 12L);
        config.EnableMultiTier = false;
        Check(!Resolve(fifth, 0).valid, "multi-tier disabled after presentation invalidates target");
        config.EnableMultiTier = true;
        profile.Layers.Insert(0, new WheelLayer());
        profile.ActiveLayerIndex = 1;
        Check(Resolve(fifth, 0).action?.Parameter == "five", "same layer after reordering");
        profile.Layers.Remove(layer);
        Check(!Resolve(fifth, 0).valid, "deleted layer cancels");
        profile.Layers[0] = layer;
        profile.ActiveLayerIndex = 0;
        _ = new Application(); // Dispatcher only; no window, hook registration or real action.
        using (var gesture = new GestureController(new MouseHook()))
        {
            var captured = new List<string>();
            Controller.GetProperty("TestActionEnqueued", Methods)!.SetValue(gesture,
                (Action<ActionItem>)(item => captured.Add(item.Parameter)));
            var frame = pageType.GetProperty("Frame", Methods)!.GetValue(fifth)!;
            var slots = (System.Collections.IList)frame.GetType().GetProperty("Slots", Methods)!.GetValue(frame)!;
            var slot = slots[1]!;
            Point localPoint = (Point)slot.GetType().GetProperty("Label", Methods)!.GetValue(slot)!;
            Point releasePoint = origin + (Vector)localPoint;
            foreach (bool pinned in new[] { false, true })
            {
                Set(gesture, "_gestureVersion", 11L);
                Set(gesture, "_deepPage", fifth);
                Set(gesture, "_activeProfile", profile);
                Set(gesture, "_isGestureActive", true);
                Set(gesture, "_startPoint", origin);
                Set(gesture, "_mouseTriggerDown", true);
                if (pinned)
                {
                    var pin = Controller.GetField("_pinState", Methods)!.GetValue(gesture)!;
                    Call(pin, "Begin", true);
                    Call(pin, "Present");
                    Call(gesture, "ConfirmDeepSelection", releasePoint, 11L);
                }
                else Call(gesture, "CompleteMouseTriggerRelease", releasePoint, "RightButton", null);
                var pending = new System.Windows.Threading.DispatcherFrame();
                Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle,
                    (Action)(() => pending.Continue = false));
                System.Windows.Threading.Dispatcher.PushFrame(pending);
                Check(captured.SequenceEqual(new[] { "sibling" }), pinned
                    ? "pinned leaf click reaches existing queue exactly once" : "mouse release reaches existing queue exactly once");
                captured.Clear();
            }
        }
        var local = new WheelProfile { ProcessName = "local", Layers = [new WheelLayer { MaxMenuDepth = 5, SectorCount = 4 }] };
        config.Profiles = [profile, local];
        config.EnableGlobalInheritance = true;
        Set(controller, "_activeProfile", local);
        Call(controller, "PrepareInputRoots", local);
        var getInputRoot = Controller.GetMethod("InputRoot", Methods)!.CreateDelegate<Func<int, ActionItem?>>(controller);
        var inheritedRoot = getInputRoot(0);
        Check(inheritedRoot?.IsInherited == true && !ReferenceEquals(root, inheritedRoot), "UI prepared detached inherited root");
        for (int i = 0; i < 2000; i++) _ = getInputRoot(0);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 20000; i++)
            if (!ReferenceEquals(inheritedRoot, getInputRoot(0))) throw new Exception("hook replaced prepared root");
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0, "20,000 inherited input lookups allocate zero bytes");
        ConfigManager.MarkConfigurationChanged();
        Check(getInputRoot(0) == null, "stale inherited input table is rejected");
        Check(!Resolve(fifth, 0).valid, "revision change cancels pending release");
        Console.WriteLine($"{checks} checks passed.");
    }
}
