using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Xml.Linq;
using WinPieGestures;

internal static class Program
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int passed, failed;
    private static string sandbox = "";
    private static object? Call(object o, string method, params object?[] args) =>
        o.GetType().GetMethod(method, Hidden)!.Invoke(o, args);
    private static object? Get(object o, string field) => o.GetType().GetField(field, Hidden)!.GetValue(o);
    private static void Set(object o, string field, object? value) => o.GetType().GetField(field, Hidden)!.SetValue(o, value);
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Run(string name, Action test)
    {
        try { test(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + (e.InnerException ?? e).Message); }
        finally { Pump(); }
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }
    private sealed class TimerHandle : IDisposable
    {
        internal bool Disposed;
        public void Dispose() => Disposed = true;
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly GestureController Controller = new(new MouseHook(), null);
        internal readonly List<Action> Callbacks = new();
        internal readonly List<double> Delays = new();
        internal readonly List<TimerHandle> Handles = new();
        internal ProfileTriggerActivation OriginalActivation;
        internal int Presentations, Replays;
        internal Fixture(string button = "LeftButton", bool keyboard = false, bool pinned = false)
        {
            var profile = new WheelProfile { ProcessName = "Global", Actions = new() };
            profile.EnsureLayers();
            var trigger = new TriggerConfig { TriggerType = keyboard ? "Keyboard" : "Mouse", MouseButton = button, VkCode = 112 };
            ConfigManager.CurrentConfig = new AppConfig { Profiles = new() { profile }, Trigger = trigger,
                LongPressTrigger = true, LongPressDelayMs = 200, DragThreshold = 20, EnableMouseReleaseDebounce = false, PinWheelAfterTrigger = pinned };
            ProfileTriggerRouter.Publish(ConfigManager.CurrentConfig);
            Call(Controller, "BeginGestureTracking");
            OriginalActivation = ProfileTriggerRouter.CreateLegacyActivation("app.exe", trigger, profile);
            Set(Controller, "_activeActivation", OriginalActivation);
            Set(Controller, "_activeTrigger", trigger);
            Set(Controller, "_activeProfile", profile);
            Set(Controller, "_startPoint", new Point(100, 100));
            Set(Controller, "_lastMovePoint", new Point(100, 100));
            Set(Controller, "_isWaitingForThreshold", true);
            Set(Controller, "_mouseTriggerDown", !keyboard);
            Set(Controller, "_kbTriggerWaiting", keyboard);
            Set(Controller, "_kbTriggerDownTick", Environment.TickCount64 - 1000);
            Controller.TestActionEnqueued = _ => throw new Exception("Unexpected action execution");
        }
        internal void SafeSeams()
        {
            PropertyInfo Need(Type type, string name) => type.GetProperty(name, Hidden) ?? throw new Exception("Missing safe seam " + name);
            Need(typeof(GestureController), "LongPressTimerFactoryOverride").SetValue(Controller,
                (Func<Action, TimeSpan, IDisposable>)((action, delay) => {
                    Callbacks.Add(action); Delays.Add(delay.TotalMilliseconds); var h = new TimerHandle(); Handles.Add(h); return h;
                }));
            Need(typeof(GestureController), "LongPressPresentationOverride").SetValue(Controller,
                (Func<Point, WheelProfile, long, WheelFocusSession?, bool>)((_, _, _, _) => { Presentations++; return false; }));
            var hook = (MouseHook)Get(Controller, "_mouseHook")!;
            Need(typeof(MouseHook), "ReplayTriggerClickOverride").SetValue(hook, (Action<string>)(_ => Interlocked.Increment(ref Replays)));
        }
        internal void Start() => Call(Controller, "StartLongPressTimer");
        internal void Move(double x = 300) => Call(Controller, "Hook_OnMouseMove", null, new WinPieGestures.MouseEventArgs(x, 100));
        internal bool Up()
        {
            var raw = new RawMouseEventArgs(514, OriginalActivation.Trigger.MouseButton!, 0, false, 100, 100);
            Call(Controller, "Hook_OnRawMouseButton", null, raw);
            if (raw.Handled) return true;
            var args = new WinPieGestures.MouseEventArgs(100, 100) { TriggerActivation = OriginalActivation };
            Call(Controller, "Hook_OnTriggerButtonUp", null, args);
            return args.Handled;
        }
        internal bool Waiting => (bool)Get(Controller, "_isWaitingForThreshold")!;
        internal bool Active => (bool)Get(Controller, "_isGestureActive")!;
        public void Dispose() { Controller.Dispose(); Pump(); }
    }
    [STAThread]
    private static int Main(string[] args)
    {
        string dll = Path.GetFullPath(args[0]);
        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll)));
        Require(hash == args[1] && hash == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(AppConfig).Assembly.Location))), "Candidate hash mismatch");
        sandbox = Path.GetFullPath(args[3]); Directory.CreateDirectory(sandbox);
        foreach (string name in new[] { "LOCALAPPDATA", "APPDATA", "TEMP", "TMP" }) Environment.SetEnvironmentVariable(name, sandbox);
        ConfigManager.DisableAutoStartSync = true;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Console.WriteLine("CANDIDATE=" + dll + "; SHA256=" + hash);
        Run("legacy delay remains 450", () => Require(JsonSerializer.Deserialize<AppConfig>("{}")!.LongPressDelayMs == 450, "Legacy default changed"));
        foreach (double v in new[] { 0d, 1d, 199d, 200d, 450d, 1000d, -1d, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Run("effective delay " + v, () => {
                var cfg = new AppConfig { LongPressDelayMs = v };
                var p = typeof(AppConfig).GetProperty("EffectiveLongPressDelayMs", Hidden);
                Require(p != null && (double)p.GetValue(cfg)! == (double.IsFinite(v) && v >= 0 ? v : 450), "Effective delay rejected zero or invalid fallback");
            });
        foreach (double v in new[] { 0d, 1d, 200d, 450d })
            Run("delay roundtrip " + v, () => {
                string json = JsonSerializer.Serialize(new AppConfig { LongPressDelayMs = v });
                Require(JsonSerializer.Deserialize<AppConfig>(json)!.LongPressDelayMs == v, "Delay roundtrip changed");
                Require(!json.Contains("EffectiveLongPressDelayMs"), "Derived value leaked into config");
            });
        Run("production export import retains zero", () => {
            ConfigManager.CurrentConfig = new AppConfig { LongPressDelayMs = 0 };
            string path = Path.Combine(sandbox, "zero.json");
            Require(ConfigManager.ExportConfig(path), "Export failed");
            ConfigManager.CurrentConfig.LongPressDelayMs = 450;
            Require(ConfigManager.ImportConfig(path) && ConfigManager.CurrentConfig.LongPressDelayMs == 0, "Import lost zero");
        });
        Run("slider admits zero and one millisecond", () => {
            var node = XDocument.Load(args[2]).Descendants().Single(e => (string?)e.Attribute("Name") == "LongPressDelaySlider");
            double Attr(string n) => double.Parse(node.Attribute(n)!.Value, CultureInfo.InvariantCulture);
            var slider = new Slider { Minimum = Attr("Minimum"), Maximum = Attr("Maximum"), TickFrequency = Attr("TickFrequency") };
            slider.Value = 0; Require(slider.Value == 0, "Zero clamped by UI");
            slider.Value = 1; Require(slider.Value == 1 && slider.TickFrequency == 1, "Low-value precision missing");
            slider.Width = 350; slider.ApplyTemplate(); slider.Measure(new Size(400, 50)); slider.Arrange(new Rect(0, 0, 400, 50));
        });
        Run("settings refresh preserves zero under initialization guard", () => {
            var window = RuntimeHelpers.GetUninitializedObject(typeof(SettingsWindow));
            var slider = new Slider { Minimum = 0, Maximum = 1000 };
            var label = new TextBlock();
            Set(window, "LongPressDelaySlider", slider); Set(window, "LongPressDelayLabel", label); Set(window, "_isUpdatingUi", true);
            ConfigManager.CurrentConfig = new AppConfig { LongPressDelayMs = 0 };
            var method = typeof(SettingsWindow).GetMethod("RefreshLongPressDelayControls", Hidden);
            Require(method != null, "No candidate-bound refresh method"); method!.Invoke(window, null);
            Require(slider.Value == 0 && label.Text == "0 ms" && (bool)Get(window, "_isUpdatingUi")!, "Settings refresh changed value or guard");
        });
        Run("left move continues waiting", () => { using var f = new Fixture(); f.Move(); Require(f.Waiting && !f.Active, "Left drag opened wheel early"); });
        Run("left move with longpress disabled preserves old displacement", () => { using var f = new Fixture(); ConfigManager.CurrentConfig.LongPressTrigger = false; f.Move(); Require(f.Active, "Legacy displacement changed"); });
        foreach (string button in new[] { "RightButton", "MiddleButton", "XButton1", "XButton2" })
            Run(button + " retains displacement", () => { using var f = new Fixture(button); f.Move(); Require(f.Active, "Other button displacement changed"); });
        Run("keyboard retains displacement", () => { using var f = new Fixture(keyboard: true); f.Move(); Require(f.Active, "Keyboard displacement changed"); });
        Run("global longpress only retains waiting", () => { using var f = new Fixture("RightButton"); ConfigManager.CurrentConfig.LongPressOnlyTrigger = true; f.Move(); Require(f.Waiting && !f.Active, "Only-longpress mode changed"); });
        Run("left movement does not cancel its timer", () => {
            using var f = new Fixture(); f.SafeSeams(); f.Start(); f.Move();
            Require(f.Delays.Single() == 200 && !f.Handles.Single().Disposed && f.Waiting, "Movement cancelled timer");
            f.Callbacks[0](); Pump(); Require(f.Active && f.Presentations == 1, "Timed trigger failed after movement");
            f.Move(450); Require(f.Active && !f.Waiting, "Movement selection stopped after timed activation");
        });
        foreach (bool pinned in new[] { false, true })
            Run("zero claims synchronously, pinned=" + pinned, () => {
                using var f = new Fixture(pinned: pinned); f.SafeSeams();
                ConfigManager.CurrentConfig.LongPressDelayMs = 0; f.Start();
                Require(f.Active && !f.Waiting && f.Callbacks.Count == 0 && f.Presentations == 0, "Zero waited or constructed UI synchronously");
                Pump(); Require(f.Presentations == 1, "Zero did not dispatch once");
            });
        Run("zero quick release never replays ordinary click", () => {
            using var f = new Fixture(); f.SafeSeams(); ConfigManager.CurrentConfig.LongPressDelayMs = 0; f.Start(); f.Up(); Pump();
            bool replayed = SpinWait.SpinUntil(() => Volatile.Read(ref f.Replays) != 0, 150);
            Require(!f.Active && !f.Waiting && f.Presentations == 0 && !replayed, "Quick release replayed or revived wheel");
        });
        Run("pinned zero retains session after trigger release", () => {
            using var f = new Fixture(pinned: true); f.SafeSeams(); ConfigManager.CurrentConfig.LongPressDelayMs = 0;
            f.Start(); f.Up(); Pump();
            Require(f.Active && f.Presentations == 1 && f.Replays == 0, "Pinned trigger release cancelled or replayed");
        });
        foreach (var input in new[] { "RightButton", "MiddleButton", "XButton1", "XButton2", "Keyboard" })
            Run("zero trigger " + input, () => {
                using var f = new Fixture(input, keyboard: input == "Keyboard"); f.SafeSeams();
                ConfigManager.CurrentConfig.LongPressDelayMs = 0; f.Start();
                Require(f.Active && !f.Waiting && f.Callbacks.Count == 0, "Zero input did not claim immediately");
                Pump(); Require(f.Presentations == 1, "Zero input failed presentation");
            });
        foreach (double delay in new[] { 1d, 199d, 200d, 450d })
            Run("positive scheduling " + delay, () => {
                using var f = new Fixture(); f.SafeSeams(); ConfigManager.CurrentConfig.LongPressDelayMs = delay;
                f.Start(); Require(f.Delays.Single() == delay && f.Waiting && !f.Active, "Positive delay changed");
            });
        Run("real one millisecond timer remains asynchronous", () => {
            ConfigManager.CurrentConfig = new AppConfig { LongPressDelayMs = 1 };
            using var controller = new GestureController(new MouseHook(), null);
            Call(controller, "StartLongPressTimer");
            Require(SpinWait.SpinUntil(() => Get(controller, "_longPressTimer") == null, 1000), "Real timer did not complete");
            Require(!(bool)Get(controller, "_isGestureActive")!, "Inactive timer activated a gesture");
        });
        Run("nonzero short release replays once", () => {
            using var f = new Fixture(); f.SafeSeams(); f.Start(); f.Up();
            Require(SpinWait.SpinUntil(() => Volatile.Read(ref f.Replays) == 1, 1000), "Short click lost");
            f.Callbacks[0](); Pump(); Require(!f.Active && f.Presentations == 0 && f.Replays == 1, "Old timer revived short click");
        });
        Run("duplicate timer cannot present twice", () => {
            using var f = new Fixture(); f.SafeSeams(); f.Start(); f.Callbacks[0](); f.Callbacks[0](); Pump();
            Require(f.Active && f.Presentations == 1, "Duplicate timer presentation");
        });
        Run("old timer cannot claim newer press", () => {
            using var f = new Fixture(); f.SafeSeams(); f.Start(); var old = f.Callbacks[0];
            Call(f.Controller, "CancelLongPressTimer"); f.Start(); old(); Require(f.Waiting && !f.Active, "Old timer claimed new press");
            f.Callbacks[1](); Pump(); Require(f.Presentations == 1, "Current timer lost");
        });
        Run("pause prevents queued presentation", () => {
            using var f = new Fixture(); f.SafeSeams(); f.Start(); f.Callbacks[0](); f.Controller.OnHostPaused(); Pump();
            Require(!f.Active && f.Presentations == 0, "Pause revived presentation");
        });
        Run("deleted layer cannot present", () => {
            using var f = new Fixture(); f.SafeSeams(); f.Start();
            ((WheelProfile)Get(f.Controller, "_activeProfile")!).Layers.Clear(); f.Callbacks[0](); Pump();
            Require(!f.Active && f.Presentations == 0, "Deleted layer presented");
        });
        Run("cancel invalidates old callbacks", () => {
            using var f = new Fixture(); f.SafeSeams(); f.Start(); Call(f.Controller, "CancelGestureTracking", "test"); f.Callbacks[0](); Pump();
            Require(!f.Active && !f.Waiting && f.Presentations == 0, "Cancelled callback revived state");
        });
        foreach (bool keyboard in new[] { false, true })
            foreach (bool pinned in new[] { false, true })
                Run($"zero binds initial layer before release keyboard={keyboard} pinned={pinned}", () => {
                    using var f = new Fixture(keyboard: keyboard, pinned: pinned); f.SafeSeams();
                    ConfigManager.CurrentConfig.LongPressDelayMs = 0;
                    var profile = (WheelProfile)Get(f.Controller, "_activeProfile")!;
                    profile.Layers[0].CenterAction = new ActionItem { Name = "A", Type = "Custom", Parameter = "fake-A" };
                    profile.Layers[0].EnableCenterAction = true;
                    var layer = new WheelLayer { Name = "B", EnableCenterAction = true, CenterAction = new ActionItem { Name = "B", Type = "Custom", Parameter = "fake-B" } };
                    profile.Layers.Add(layer); profile.SyncRootPropertiesFromActiveLayer();
                    var trigger = f.OriginalActivation.Trigger;
                    f.OriginalActivation = new ProfileTriggerActivation(profile, trigger,
                        keyboard ? ProfileTriggerSignature.ForKeyboard(112, ModifierKeys.None) : ProfileTriggerSignature.ForMouse("LeftButton", ModifierKeys.None),
                        true, "app.exe", layer);
                    Set(f.Controller, "_activeActivation", f.OriginalActivation);
                    var actions = new List<string>(); f.Controller.TestActionEnqueued = a => actions.Add(a.Name);
                    f.Start();
                    Require(profile.ActiveLayerIndex == 1, "Zero activation still points at old layer A");
                    if (keyboard) Call(f.Controller, "KeyboardHook_OnKeyUp", null, new GlobalKeyEventArgs(112, ModifierKeys.None)); else f.Up();
                    Pump();
                    Require(actions.All(a => a != "A"), "Wrong-layer action executed");
                    Require(pinned ? actions.Count == 0 : actions.SequenceEqual(new[] { "B" }), "Expected layer B release semantics missing");
                });
        foreach (double delay in new[] { 0d, 200d })
            Run("presentation preserves latest pointer " + delay, () => {
                using var f = new Fixture(); f.SafeSeams(); ConfigManager.CurrentConfig.LongPressDelayMs = delay;
                var profile = (WheelProfile)Get(f.Controller, "_activeProfile")!;
                profile.Layers[0].Actions = Enumerable.Range(0, 8).Select(i => new ActionItem { Name = "sector" + i, Type = "Custom", Parameter = "fake" }).ToList();
                profile.SyncRootPropertiesFromActiveLayer();
                typeof(GestureController).GetProperty("LongPressPresentationOverride", Hidden)!.SetValue(f.Controller,
                    (Func<Point, WheelProfile, long, WheelFocusSession?, bool>)((_, _, _, _) => true));
                f.Start(); if (delay > 0) f.Callbacks[0]();
                f.Move(100 + ConfigManager.CurrentConfig.CoreRadius + 20);
                int selected = (int)Get(f.Controller, "_selectedSectorIndex")!;
                Require(selected >= 0, "Test failed to select a leaf sector");
                Pump(); Require((int)Get(f.Controller, "_selectedSectorIndex")! == selected, "Presentation replaced selected sector with core");
            });
        Run("presentation failure drains owned release", () => {
            using var f = new Fixture(); f.SafeSeams(); ConfigManager.CurrentConfig.LongPressDelayMs = 0;
            typeof(GestureController).GetProperty("LongPressPresentationOverride", Hidden)!.SetValue(f.Controller,
                (Func<Point, WheelProfile, long, WheelFocusSession?, bool>)((_, _, _, _) => throw new Exception("fake presentation failure")));
            f.Start(); Pump(); Require(!f.Active, "Failure did not cancel");
            Require(f.Up(), "Cancelled session leaked its owned mouse Up");
            Require(!f.Up(), "Cancellation consumed another unowned Up");
            Require(f.Replays == 0, "Cancelled session replayed click");
        });
        Run("deleted layer cancellation drains owned release", () => {
            using var f = new Fixture(); f.SafeSeams(); f.Start();
            ((WheelProfile)Get(f.Controller, "_activeProfile")!).Layers.Clear(); f.Callbacks[0](); Pump();
            Require(f.Up(), "Deleted layer cancellation leaked Up");
            Require(!f.Up(), "Deleted layer cancellation retained stale ownership");
        });
        foreach (bool keyboard in new[] { false, true })
            foreach (double delay in new[] { 0d, 200d })
                Run($"timed center is published before activation keyboard={keyboard} delay={delay}", () => {
                    using var f = new Fixture(keyboard: keyboard); f.SafeSeams();
                    ConfigManager.CurrentConfig.LongPressOnlyTrigger = !keyboard;
                    ConfigManager.CurrentConfig.LongPressDelayMs = delay;
                    var initial = new Point(-100000, -100000); var timed = new Point(100, 100);
                    Set(f.Controller, "_startPoint", initial); Set(f.Controller, "_lastMovePoint", initial);
                    var pointer = typeof(GestureController).GetProperty("LongPressPointerOverride", Hidden);
                    pointer?.SetValue(f.Controller, (Func<Point>)(() => timed));
                    var profile = (WheelProfile)Get(f.Controller, "_activeProfile")!;
                    profile.Layers[0].Actions = Enumerable.Range(0, 8).Select(i => new ActionItem { Name = "sector" + i, Type = "Custom", Parameter = "fake" }).ToList();
                    profile.SyncRootPropertiesFromActiveLayer();
                    var actions = new List<string>(); f.Controller.TestActionEnqueued = a => actions.Add(a.Name);
                    f.Start(); if (delay > 0) f.Callbacks[0]();
                    Require((Point)Get(f.Controller, "_startPoint")! != initial, "Active gesture still uses pre-hold center before UI");
                    Require(pointer != null && (Point)Get(f.Controller, "_startPoint")! == timed, "Controlled timed center missing");
                    f.Move(100 + ConfigManager.CurrentConfig.CoreRadius + 20);
                    if (keyboard) Call(f.Controller, "KeyboardHook_OnKeyUp", null, new GlobalKeyEventArgs(112, ModifierKeys.None)); else f.Up();
                    Pump(); Require(actions.SequenceEqual(new[] { "sector0" }), "Release did not resolve relative to timed center");
                });
        Console.WriteLine($"RESULT {passed} passed, {failed} failed; no product Window, hooks or real input.");
        app.Shutdown(); return failed == 0 ? 0 : 1;
    }
}
