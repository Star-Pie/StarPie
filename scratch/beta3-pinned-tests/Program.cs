using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using WinPieGestures;

internal static class Program
{
    private static int _checks;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); _checks++; }
    private static object? Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private | BindingFlags.Public)!.Invoke(target, args);
    private static object Field(object target, string name) => target.GetType().GetField(name, Private)!.GetValue(target)!;
    private static void Set(object target, string name, object? value) => target.GetType().GetField(name, Private)!.SetValue(target, value);
    private static void Static(Type type, string name, object value) => type.GetProperty(name, BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, value);
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, (Action)(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            string sandbox = Path.GetFullPath(args[0]);
            Directory.CreateDirectory(sandbox);
            foreach (string name in new[] { "LOCALAPPDATA", "APPDATA", "TEMP", "TMP" }) Environment.SetEnvironmentVariable(name, sandbox);
            // ConfigManager explicitly honors LOCALAPPDATA; Windows KnownFolder itself intentionally does not.
            Check(Environment.GetEnvironmentVariable("LOCALAPPDATA") == sandbox, "ConfigManager root overridden before initialization");
            string dll = typeof(AppConfig).Assembly.Location;
            Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll))) == args[1], "actual candidate SHA");
            var property = typeof(AppConfig).GetProperty("PinWheelAfterTrigger") ?? throw new Exception("Baseline lacks pinned mode");
            var old = JsonSerializer.Deserialize<AppConfig>("{}")!;
            Check(!(bool)property.GetValue(old)!, "legacy missing field defaults false");
            foreach (bool enabled in new[] { false, true })
            {
                property.SetValue(old, enabled);
                string json = JsonSerializer.Serialize(old);
                Check(json.Contains("\"PinWheelAfterTrigger\":" + enabled.ToString().ToLowerInvariant()), "explicit bool persisted");
                Check((bool)property.GetValue(JsonSerializer.Deserialize<AppConfig>(json))! == enabled, "configuration roundtrip");
            }
            CheckSaveBranches();
            var stateType = typeof(AppConfig).Assembly.GetType("WinPieGestures.PinnedWheelState", true)!;
            var state = Activator.CreateInstance(stateType, nonPublic: true)!;
            bool Value(string name) => (bool)stateType.GetProperty(name)!.GetValue(state)!;
            Call(state, "Begin", true); Call(state, "Present"); Call(state, "ReleaseTrigger");
            Check(Value("Enabled") && Value("Presented") && Value("TriggerReleased"), "release retains visible pinned session");
            foreach (string button in new[] { "LeftButton", "RightButton", "MiddleButton", "XButton1", "XButton2" })
            {
                Call(state, "OwnMouseUp", button); Call(state, "End");
                Check((bool)Call(state, "ConsumeMouseUp", button)!, "cancel retains paired mouse up " + button);
                Check(!(bool)Call(state, "ConsumeMouseUp", button)!, "mouse up consumed once " + button);
                Call(state, "Begin", true);
            }
            Call(state, "OwnKeyUp", 27u); Call(state, "End");
            Check((bool)Call(state, "OwnsKeyUp", 27u)!, "repeat stays swallowed after cancel");
            Check((bool)Call(state, "ConsumeKeyUp", 27u)!, "paired Esc up");
            Check(!(bool)Call(state, "ConsumeKeyUp", 27u)!, "Esc up consumed once");
            Call(state, "Begin", false); Call(state, "Present");
            Check(!Value("Enabled") && !Value("Presented"), "disabled mode cannot become presented");
            for (int i = 0; i < 200; i++)
            {
                Call(state, "Begin", true); Call(state, "Present");
                using var gate = new Barrier(2);
                object? release = null;
                var cancelling = Task.Run(() => { gate.SignalAndWait(); Call(state, "EndWithMouseRelease", "RightButton"); });
                var releasing = Task.Run(() => { gate.SignalAndWait(); release = Call(state, "TakeMouseUp", "RightButton", "RightButton", true); });
                Task.WaitAll(cancelling, releasing);
                Check(release!.ToString() is "Original" or "Owned", "concurrent cancel/release always owns original Up");
                Check(!(bool)Call(state, "ConsumeMouseUp", "RightButton")!, "concurrent cancel/release leaves no stale ownership");
            }

            // Plain WPF dispatcher only: no product App startup, Window, input hooks or real actions.
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            typeof(ActionExecutor).GetProperty("ReleaseStuckModifiersOverride", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, (Action)(() => { }));
            ConfigManager.DisableAutoStartSync = true;
            Check((string)typeof(ConfigManager).GetField("AppDataFolder", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)! == Path.Combine(sandbox, "StarPie"), "actual ConfigManager path is isolated");
            var mouseHook = new MouseHook();
            var keyboardHook = new KeyboardHook();
            using (var controller = new GestureController(mouseHook, keyboardHook))
            {
                object input = Field(controller, "_pinState");
                Call(input, "Begin", true);
                Set(controller, "_isGestureActive", true);
                Set(controller, "_mouseTriggerDown", true);
                Set(controller, "_activeTrigger", new TriggerConfig());
                var mouseUp = new WinPieGestures.MouseEventArgs(0, 0);
                Call(controller, "Hook_OnTriggerButtonUp", new object(), mouseUp);
                Check(mouseUp.Handled && (bool)Field(controller, "_isGestureActive"), "mouse release bypasses execution/end path");
                Check(!(bool)Field(controller, "_mouseTriggerDown"), "mouse trigger ownership released");
                Call(input, "Begin", true); Call(input, "Present");
                Set(controller, "_mouseTriggerDown", true);
                Set(mouseHook, "_activeMouseTriggerButton", "RightButton");
                var physicalUp = new RawMouseEventArgs(517, "RightButton", 0, false, 0, 0);
                Call(controller, "Hook_OnRawMouseButton", new object(), physicalUp);
                Check(physicalUp.Handled && (bool)Field(controller, "_isGestureActive"), "raw original release retains pinned session");
                Check(!(bool)Field(controller, "_mouseTriggerDown") && Field(mouseHook, "_activeMouseTriggerButton") == null, "raw release clears original hook ownership");
                var clickDown = new RawMouseEventArgs(513, "LeftButton", 0, true, 50, 0);
                Call(controller, "Hook_OnRawMouseButton", new object(), clickDown);
                long clickVersion = (long)Field(controller, "_pinClickVersion");
                Call(input, "OwnMouseUp", "RightButton");
                Call(controller, "Hook_OnRawMouseButton", new object(), new RawMouseEventArgs(517, "RightButton", 0, false, 0, 0));
                Check((long)Field(controller, "_pinClickVersion") == clickVersion, "unrelated drained Up cannot erase left-click confirmation");
                Call(input, "ConsumeMouseUp", "LeftButton");
                Set(controller, "_pinClickVersion", -1L);
                Set(controller, "_activeTrigger", new TriggerConfig { TriggerType = "Keyboard", VkCode = 65 });
                Call(input, "Begin", true);
                var repeat = new GlobalKeyEventArgs(65, ModifierKeys.None);
                Call(controller, "KeyboardHook_OnKeyDown", new object(), repeat);
                Check(repeat.Handled && (bool)Field(controller, "_isGestureActive"), "keyboard repeat is not a second trigger");
                var keyUp = new GlobalKeyEventArgs(65, ModifierKeys.None);
                Call(controller, "KeyboardHook_OnKeyUp", new object(), keyUp);
                Check(!keyUp.Handled && (bool)Field(controller, "_isGestureActive"), "native keyboard Down retains its native paired Up");
                var triggerAgain = new GlobalKeyEventArgs(65, ModifierKeys.None);
                Call(controller, "KeyboardHook_OnKeyDown", new object(), triggerAgain);
                Check(triggerAgain.Handled && !(bool)Field(controller, "_isGestureActive"), "second physical keyboard trigger cancels");
                keyUp = new GlobalKeyEventArgs(65, ModifierKeys.None);
                Call(controller, "KeyboardHook_OnKeyUp", new object(), keyUp);
                Check(keyUp.Handled, "second-trigger key up drained after hiding");

                Call(input, "Begin", true); Call(input, "ReleaseTrigger");
                Set(controller, "_isGestureActive", true);
                Set(controller, "_activeTrigger", new TriggerConfig { TriggerType = "Keyboard", VkCode = 65, RequireCtrl = true });
                var modifierOnly = new GlobalKeyEventArgs(17, ModifierKeys.Control);
                Call(controller, "KeyboardHook_OnKeyDown", new object(), modifierOnly);
                Check(!modifierOnly.Handled && (bool)Field(controller, "_isGestureActive"), "modifier alone is not Ctrl+A retrigger");
                var missingModifier = new GlobalKeyEventArgs(65, ModifierKeys.None);
                Call(controller, "KeyboardHook_OnKeyDown", new object(), missingModifier);
                Check(!missingModifier.Handled && (bool)Field(controller, "_isGestureActive"), "A without Ctrl is not Ctrl+A retrigger");
                var fullTrigger = new GlobalKeyEventArgs(65, ModifierKeys.Control);
                Call(controller, "KeyboardHook_OnKeyDown", new object(), fullTrigger);
                Check(fullTrigger.Handled && !(bool)Field(controller, "_isGestureActive"), "complete Ctrl+A retrigger cancels");
                Call(controller, "KeyboardHook_OnKeyUp", new object(), new GlobalKeyEventArgs(65, ModifierKeys.Control));

                Call(input, "Begin", true); Call(input, "Present"); Call(input, "ReleaseTrigger");
                Set(controller, "_isGestureActive", true);
                Call(controller, "ConfirmPinnedSelection", new Point(20, 20), 123456L);
                Check((bool)Field(controller, "_isGestureActive"), "stale confirmation ignored without touching session");
                var outsideDown = new RawMouseEventArgs(513, "LeftButton", 0, true, 10000, 10000);
                Call(controller, "Hook_OnRawMouseButton", new object(), outsideDown);
                Pump();
                Check(outsideDown.Handled && !(bool)Field(controller, "_isGestureActive"), "outside down cancels and is swallowed");
                var outsideUp = new RawMouseEventArgs(514, "LeftButton", 0, false, 10000, 10000);
                Call(controller, "Hook_OnRawMouseButton", new object(), outsideUp);
                Check(outsideUp.Handled, "outside up swallowed after hiding");
                outsideUp = new RawMouseEventArgs(514, "LeftButton", 0, false, 10000, 10000);
                Call(controller, "Hook_OnRawMouseButton", new object(), outsideUp);
                Check(!outsideUp.Handled, "unowned up is not swallowed");

                Call(input, "Begin", true); Call(input, "Present"); Call(input, "ReleaseTrigger");
                Set(controller, "_isGestureActive", true);
                var escape = new GlobalKeyEventArgs(27, ModifierKeys.None);
                Call(controller, "KeyboardHook_OnKeyDown", new object(), escape);
                Check(escape.Handled && !(bool)Field(controller, "_isGestureActive"), "Esc cancels without action");
                var escapeUp = new GlobalKeyEventArgs(27, ModifierKeys.None);
                Call(controller, "KeyboardHook_OnKeyUp", new object(), escapeUp);
                Check(escapeUp.Handled, "Esc up paired after cancellation");

                Call(input, "OwnMouseUp", "LeftButton"); Call(input, "OwnKeyUp", 65u);
                Call(controller, "OnHostPaused");
                mouseHook.IsPaused = keyboardHook.IsPaused = true;
                Check((bool)Call(mouseHook, "TryDrainPinnedRelease", 514, 0u)!, "paused mouse hook drains owned Up");
                Check(!(bool)Call(mouseHook, "TryDrainPinnedRelease", 514, 0u)!, "paused mouse hook leaves unrelated Up native");
                Check((bool)Call(keyboardHook, "TryDrainPinnedRelease", 65u, true)!, "paused keyboard hook drains owned repeat");
                Check((bool)Call(keyboardHook, "TryDrainPinnedRelease", 65u, false)!, "paused keyboard hook drains owned Up");
                Check(!(bool)Call(keyboardHook, "TryDrainPinnedRelease", 65u, false)!, "paused keyboard has no stale ownership");
                mouseHook.IsPaused = keyboardHook.IsPaused = false;

                Static(typeof(WheelFocusSwitcher), "IsWindowOverride", (Func<nint, bool>)(_ => true));
                Static(typeof(WheelFocusSwitcher), "ForegroundWindowGetterOverride", (Func<nint>)(() => (nint)1234));
                Static(typeof(ActiveWindowHelper), "RealPidGetterOverride", (Func<nint, uint>)(_ => 42));
                Static(typeof(ActiveWindowHelper), "ProcessNameGetterOverride", (Func<nint, string>)(_ => "pin-test.exe"));
                var target = new TargetWindowInfo((nint)1234, 42, "pin-test.exe", true);
                var profile = new WheelProfile { ProcessName = "pin-test.exe", Actions = Enumerable.Range(0, 8).Select(_ => new ActionItem()).ToList() };
                profile.Actions[0] = new ActionItem { Type = "Hotkey", Parameter = "Ctrl+C" };
                // Keep the other populated slot while testing an actionless parent; avoid legacy all-empty layer repair.
                profile.Actions[1] = new ActionItem { Type = "Hotkey", Parameter = "Ctrl+X" };
                profile.EnsureLayers();
                int executions = 0;
                string? lastEnqueuedParameter = null;
                controller.GetType().GetProperty("TestActionEnqueued", Private)!.SetValue(controller, (Action<ActionItem>)(action =>
                {
                    Check(!(bool)Field(controller, "_isGestureActive") && !(bool)stateType.GetProperty("Enabled")!.GetValue(input)!, "session ends before enqueue");
                    lastEnqueuedParameter = action.Parameter;
                    executions++;
                }));
                controller.GetType().GetProperty("TestPinnedHitTest", Private)!.SetValue(controller, (Func<Point, (bool, int, int)>)(_ => (true, 0, -1)));
                void Activate(long version)
                {
                    Call(input, "Begin", true); Call(input, "Present"); Call(input, "ReleaseTrigger");
                    Set(controller, "_activeTrigger", new TriggerConfig());
                    Set(controller, "_pinTrigger", new TriggerConfig());
                    Set(controller, "_activeProfile", profile);
                    Set(controller, "_pinTarget", target);
                    Set(controller, "_currentWheelFocusSession", new WheelFocusSession(target, default) { FocusState = WheelFocusState.AlreadyForeground });
                    Set(controller, "_isGestureActive", true);
                    Set(controller, "_gestureVersion", version);
                }
                Activate(150);
                ConfigManager.CurrentConfig.EnableOuterEscapeCancel = false;
                var hoverEvents = new List<StarPie.Plugin.InteractionEvent>();
                object hoverSession = Call(controller, "TestBeginInteractionSession", (Func<StarPie.Plugin.InteractionEvent, int>)(evt => { hoverEvents.Add(evt); return 0; }))!;
                Call(hoverSession, "Presented");
                controller.GetType().GetProperty("TestPinnedHitTest", Private)!.SetValue(controller,
                    (Func<Point, (bool, int, int)>)(point =>
                    {
                        Check(Dispatcher.CurrentDispatcher == app.Dispatcher, "pinned hit testing stays on UI dispatcher");
                        return point.X > 10 && point.X < 100 ? (true, 0, -1) : (false, -1, -1);
                    }));
                Call(controller, "ProcessMove", new Point(10000, 0)); Pump();
                Check((int)Field(controller, "_selectedSectorIndex") < -1, "outside pinned geometry clears angular selection");
                Check(hoverEvents.Last(evt => evt.Kind == StarPie.Plugin.InteractionEventKind.SelectionChanged).Target.Kind == StarPie.Plugin.InteractionTargetKind.None,
                    "outside pinned geometry publishes no sector or core hover");
                int outsideEvents = hoverEvents.Count;
                Call(controller, "ProcessMove", new Point(-10000, 10000)); Pump();
                Check(hoverEvents.Count == outsideEvents, "moving outside across angles does not repeat interaction feedback");
                Call(controller, "ProcessMove", new Point(50, 0)); Pump();
                Check((int)Field(controller, "_selectedSectorIndex") == 0, "reentering visible sector restores selection");
                Set(controller, "_lastShowSubTier", true);
                Set(controller, "_pinExpandedParent", 0);
                Call(controller, "ProcessMove", new Point(10000, 0)); Pump();
                Check((bool)Field(controller, "_lastShowSubTier") && (int)Field(controller, "_pinExpandedParent") == 0,
                    "leaving geometry clears feedback without collapsing expanded submenu");
                double protrudingX = Math.Ceiling(ConfigManager.CurrentConfig.WheelRadius) + 5;
                controller.GetType().GetProperty("TestPinnedHitTest", Private)!.SetValue(controller,
                    (Func<Point, (bool, int, int)>)(point => (point.X == protrudingX, 0, -1)));
                var protrudingDown = new RawMouseEventArgs(519, "MiddleButton", 0, true, (int)protrudingX, 0);
                Call(controller, "Hook_OnRawMouseButton", new object(), protrudingDown); Pump();
                Check(protrudingDown.Handled && (bool)Field(controller, "_isGestureActive"), "click bounds include rendered geometry beyond nominal radius");
                var protrudingUp = new RawMouseEventArgs(520, "MiddleButton", 0, false, (int)protrudingX, 0);
                Call(controller, "Hook_OnRawMouseButton", new object(), protrudingUp);
                Check(protrudingUp.Handled, "geometry-checked down retains paired up ownership");
                int queuedHitCalls = 0;
                controller.GetType().GetProperty("TestPinnedHitTest", Private)!.SetValue(controller,
                    (Func<Point, (bool, int, int)>)(point => { queuedHitCalls++; return point.X < 100 ? (true, 0, -1) : (false, -1, -1); }));
                Task.Run(() => { Call(controller, "ProcessMove", new Point(50, 0)); Call(controller, "ProcessMove", new Point(10000, 0)); }).Wait();
                Check(queuedHitCalls == 0, "input thread does not access WPF geometry");
                Pump();
                Check(queuedHitCalls == 1 && (int)Field(controller, "_selectedSectorIndex") < -1, "queued hover uses latest pointer once");
                queuedHitCalls = 0;
                controller.GetType().GetProperty("TestPinnedHitTest", Private)!.SetValue(controller,
                    (Func<Point, (bool, int, int)>)(point =>
                    {
                        queuedHitCalls++;
                        if (point.X < 100) Task.Run(() => Call(controller, "ProcessMove", new Point(10000, 0))).Wait();
                        return point.X < 100 ? (true, 0, -1) : (false, -1, -1);
                    }));
                Call(controller, "ProcessMove", new Point(50, 0)); Pump();
                Check(queuedHitCalls == 2 && (int)Field(controller, "_selectedSectorIndex") < -1,
                    "new pointer arriving during UI consumption is not overwritten by old point");
                Task.Run(() => Call(controller, "ProcessMove", new Point(50, 0))).Wait();
                Activate(151);
                queuedHitCalls = 0;
                Pump();
                Check(queuedHitCalls == 0, "stale hover callback cannot update a new session");
                Call(input, "Begin", true);
                controller.GetType().GetProperty("TestPinnedHitTest", Private)!.SetValue(controller, (Func<Point, (bool, int, int)>)(_ => (true, 0, -1)));
                Call(controller, "ProcessMove", new Point(50, 0)); Pump();
                Call(input, "Present");
                Call(controller, "ApplyPinnedHoverAtPresentation", 151L);
                Check((int)Field(controller, "_selectedSectorIndex") == 0, "presentation completion recalculates stationary pointer");
                Call(controller, "ProcessMove", new Point(50, 0));
                Call(controller, "RefreshHoverAfterUiChange", new Point(10000, 0), 151L); Pump();
                Check((int)Field(controller, "_selectedSectorIndex") == 0 && (Point)Field(controller, "_pendingPinnedHoverPoint") == new Point(50, 0),
                    "UI refresh never overwrites latest input with stale fallback point");
                controller.GetType().GetProperty("TestPinnedHitTest", Private)!.SetValue(controller,
                    (Func<Point, (bool, int, int)>)(_ => { Activate(152); Set(controller, "_selectedSectorIndex", -1); return (true, 0, -1); }));
                Call(controller, "ApplyPinnedHover", new Point(50, 0), 151L);
                Check((int)Field(controller, "_selectedSectorIndex") == -1, "generation change during hit test rejects old hover publication");
                controller.GetType().GetProperty("TestPinnedHitTest", Private)!.SetValue(controller,
                    (Func<Point, (bool, int, int)>)(_ => { Activate(153); return (false, -1, -1); }));
                var oldOutsideDown = new RawMouseEventArgs(519, "MiddleButton", 0, true, 10000, 0);
                Call(controller, "Hook_OnRawMouseButton", new object(), oldOutsideDown); Pump();
                var oldOutsideUp = new RawMouseEventArgs(520, "MiddleButton", 0, false, 10000, 0);
                Call(controller, "Hook_OnRawMouseButton", new object(), oldOutsideUp);
                Check((bool)Field(controller, "_isGestureActive") && oldOutsideUp.Handled,
                    "old outside-click hit callback cannot cancel new session and still drains paired up");
                Activate(154);
                controller.GetType().GetProperty("TestPinnedHitTest", Private)!.SetValue(controller,
                    (Func<Point, (bool, int, int)>)(point => (point.X < 1000, 0, -1)));
                profile.Layers[0].Actions[0].SubActions = new List<ActionItem> { new() { Type = "Hotkey", Parameter = "Ctrl+V" } };
                Set(controller, "_pinExpandedParent", -1);
                Set(controller, "_lastShowSubTier", false);
                double expansionDistance = Math.Max(25, ConfigManager.CurrentConfig.SubWheelTriggerDistance);
                Call(controller, "ProcessMove", new Point(expansionDistance + 1, 0)); Pump();
                Call(controller, "ProcessMove", new Point(10000, 0)); Pump();
                Call(controller, "ProcessMove", new Point(expansionDistance - 15, 0)); Pump();
                Check((bool)Field(controller, "_lastShowSubTier") && (int)Field(controller, "_pinExpandedParent") == 0,
                    "hover-expanded submenu stays open after leaving and returning closer to same parent");
                profile.Layers[0].Actions[0].SubActions = new List<ActionItem>();
                controller.GetType().GetProperty("TestPinnedHitTest", Private)!.SetValue(controller,
                    (Func<Point, (bool, int, int)>)(_ => { Activate(155); return (true, 0, -1); }));
                Call(controller, "ConfirmPinnedSelection", new Point(50, 0), 154L); Pump();
                Check(executions == 0 && (bool)Field(controller, "_isGestureActive"),
                    "confirmation superseded during geometry testing cannot execute in a new session");
                Call(input, "Begin", false);
                Set(controller, "_lastShowSubTier", false);
                Set(controller, "_pinExpandedParent", -1);
                Call(controller, "ProcessMove", new Point(10000, 0)); Pump();
                Check((int)Field(controller, "_selectedSectorIndex") == 0, "ordinary gesture preserves distance-independent direction selection");
                controller.GetType().GetProperty("TestPinnedHitTest", Private)!.SetValue(controller, (Func<Point, (bool, int, int)>)(_ => (true, 0, -1)));
                Activate(200);
                Call(controller, "ConfirmPinnedSelection", new Point(50, 0), 200L); Pump();
                Check(executions == 1, "leaf confirm enqueues once through real release pipeline");
                Call(controller, "ConfirmPinnedSelection", new Point(50, 0), 200L); Pump();
                Check(executions == 1, "duplicate confirmation cannot enqueue twice");
                profile.Layers[0].Actions[0].Parameter = "";
                Activate(300); Call(controller, "ConfirmPinnedSelection", new Point(50, 0), 300L); Pump();
                Check(executions == 1 && (bool)Field(controller, "_isGestureActive"), "empty slot stays open");
                profile.Layers[0].Actions[0].SubActions = new List<ActionItem> { new() { Type = "Hotkey", Parameter = "Ctrl+V" } };
                Activate(400); Call(controller, "ConfirmPinnedSelection", new Point(50, 0), 400L); Pump();
                Check(executions == 1 && (bool)Field(controller, "_isGestureActive"), "parent navigation stays open without executing");

                // Exercise the actual hook packet -> reused args -> queued confirmation path, without installing a hook.
                Static(typeof(WheelFocusSwitcher), "TargetResolverOverride", (Func<Point, TargetWindowInfo>)(_ => target));
                nint packet = Marshal.AllocHGlobal(32);
                try
                {
                    for (int i = 0; i < 32; i++) Marshal.WriteByte(packet, i, 0);
                    Marshal.WriteInt32(packet, 0, 50);
                    profile.Layers[0].Actions[0].Parameter = "Ctrl+C";
                    profile.Layers[0].Actions[0].SubActions = new List<ActionItem>();
                    Activate(500);
                    int beforeClick = executions;
                    Check((nint)Call(mouseHook, "HookCallback", 0, (nint)513, packet)! == 1, "real packet left down is swallowed");
                    Check((nint)Call(mouseHook, "HookCallback", 0, (nint)514, packet)! == 1, "real packet left up is swallowed");
                    Pump();
                    Check(executions == beforeClick + 1 && !(bool)Field(controller, "_isGestureActive"), "real packet click confirms a pinned leaf once");
                    Activate(501);
                    var router = typeof(AppConfig).Assembly.GetType("WinPieGestures.ProfileTriggerRouter", true)!;
                    var activation = router.GetMethod("CreateLegacyActivation", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null,
                        ["pin-test.exe", new TriggerConfig(), profile]);
                    Set(controller, "_activeActivation", activation);
                    beforeClick = executions;
                    Task.Run(() =>
                    {
                        Check((nint)Call(mouseHook, "HookCallback", 0, (nint)513, packet)! == 1, "hook thread left down is swallowed");
                        Check((nint)Call(mouseHook, "HookCallback", 0, (nint)514, packet)! == 1, "hook thread left up is swallowed");
                    }).Wait();
                    Pump();
                    Check(executions == beforeClick + 1 && !(bool)Field(controller, "_isGestureActive"), "hook thread confirms a pinned leaf with actual activation");
                    ConfigManager.CurrentConfig.SubmenuStyle = "Wheel";
                    profile.Layers[0].Actions[0].SubActions = new List<ActionItem> { new() { Type = "Hotkey", Parameter = "Ctrl+V" } };
                    foreach (bool autoExpand in new[] { true, false })
                    {
                        ConfigManager.CurrentConfig.AutoExpandSubRingsOnPopup = autoExpand;
                        Activate(autoExpand ? 502 : 503);
                        Set(controller, "_activeActivation", activation);
                        beforeClick = executions;
                        Task.Run(() =>
                        {
                            Check((nint)Call(mouseHook, "HookCallback", 0, (nint)513, packet)! == 1, "primary down swallowed");
                            Check((nint)Call(mouseHook, "HookCallback", 0, (nint)514, packet)! == 1, "primary up swallowed");
                        }).Wait();
                        Pump();
                        Check(executions == beforeClick + 1 && !(bool)Field(controller, "_isGestureActive") && lastEnqueuedParameter == "Ctrl+C",
                            "configured primary action executes with children; auto-expand=" + autoExpand);
                    }
                    Activate(504);
                    Set(controller, "_activeActivation", activation);
                    controller.GetType().GetProperty("TestPinnedHitTest", Private)!.SetValue(controller, (Func<Point, (bool, int, int)>)(_ => (true, 0, 0)));
                    beforeClick = executions;
                    Task.Run(() => { Call(mouseHook, "HookCallback", 0, (nint)513, packet); Call(mouseHook, "HookCallback", 0, (nint)514, packet); }).Wait();
                    Pump();
                    Check(executions == beforeClick + 1 && !(bool)Field(controller, "_isGestureActive") && lastEnqueuedParameter == "Ctrl+V", "child click still executes the child rather than its primary action");
                    Activate(505);
                    Set(controller, "_activeActivation", activation);
                    profile.Layers[0].Actions[0].Parameter = "";
                    controller.GetType().GetProperty("TestPinnedHitTest", Private)!.SetValue(controller, (Func<Point, (bool, int, int)>)(_ => (true, 0, -1)));
                    beforeClick = executions;
                    Task.Run(() => { Call(mouseHook, "HookCallback", 0, (nint)513, packet); Call(mouseHook, "HookCallback", 0, (nint)514, packet); }).Wait();
                    Pump();
                    Check(executions == beforeClick && (bool)Field(controller, "_isGestureActive") && (int)Field(controller, "_pinExpandedParent") == 0,
                        $"actionless parent remains navigation-only through real packet pairing (before={beforeClick}, after={executions}, active={Field(controller, "_isGestureActive")}, expanded={Field(controller, "_pinExpandedParent")})");
                }
                finally { Marshal.FreeHGlobal(packet); }
            }
            Console.WriteLine($"PASS {_checks} pinned configuration/input checks; candidate={dll}");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    // Follow the actual candidate handler's IL with only its save/UI calls substituted.
    // This verifies both branches without constructing a settings window or showing a dialog.
    private static void CheckSaveBranches()
    {
        var method = typeof(SettingsWindow).GetMethod("SaveButton_Click", Private)!;
        var codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)!)
            .ToDictionary(c => c.Value);
        byte[] il = method.GetMethodBody()!.GetILAsByteArray()!;
        foreach (bool saveResult in new[] { true, false })
        {
            var stack = new Stack<object?>();
            var locals = new object?[method.GetMethodBody()!.LocalVariables.Count];
            int saves = 0, statuses = 0, alerts = 0, steps = 0;
            bool? statusSuccess = null;
            string? alertMessage = null;
            for (int pc = 0; pc < il.Length;)
            {
                if (++steps > 150) throw new Exception("Save handler IL did not terminate");
                short value = il[pc++];
                if (value == 0xfe) value = (short)(0xfe00 | il[pc++]);
                var op = codes[value];
                int operand = pc;
                pc += op.OperandType switch
                {
                    OperandType.InlineNone => 0,
                    OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                    OperandType.InlineVar => 2,
                    OperandType.InlineMethod or OperandType.InlineString or OperandType.InlineBrTarget or OperandType.InlineI => 4,
                    _ => throw new Exception("Unsupported save-handler operand " + op)
                };
                if (op == OpCodes.Nop) continue;
                if (op == OpCodes.Ret) break;
                if (op == OpCodes.Ldarg_0) { stack.Push(null); continue; }
                if (op == OpCodes.Ldc_I4_0 || op == OpCodes.Ldc_I4_1) { stack.Push(op == OpCodes.Ldc_I4_1 ? 1 : 0); continue; }
                if (op == OpCodes.Ldc_I4_2 || op == OpCodes.Ldc_I4_3 || op == OpCodes.Ldc_I4_4) { stack.Push(op.Value - OpCodes.Ldc_I4_0.Value); continue; }
                if (op == OpCodes.Ldc_I4_S) { stack.Push((int)(sbyte)il[operand]); continue; }
                if (op == OpCodes.Ldc_I4) { stack.Push(BitConverter.ToInt32(il, operand)); continue; }
                if (op == OpCodes.Ldstr) { stack.Push(method.Module.ResolveString(BitConverter.ToInt32(il, operand))); continue; }
                if (op == OpCodes.Stloc_0) { locals[0] = stack.Pop(); continue; }
                if (op == OpCodes.Ldloc_0) { stack.Push(locals[0]); continue; }
                if (op == OpCodes.Pop) { stack.Pop(); continue; }
                if (op == OpCodes.Brtrue_S || op == OpCodes.Brfalse_S || op == OpCodes.Br_S)
                {
                    bool branch = op == OpCodes.Br_S || (Convert.ToBoolean(stack.Pop()) == (op == OpCodes.Brtrue_S));
                    if (branch) pc += (sbyte)il[operand];
                    continue;
                }
                if (op == OpCodes.Call || op == OpCodes.Callvirt)
                {
                    var called = (MethodInfo)method.Module.ResolveMethod(BitConverter.ToInt32(il, operand))!;
                    var arguments = new object?[called.GetParameters().Length];
                    for (int i = arguments.Length - 1; i >= 0; i--) arguments[i] = stack.Pop();
                    if (!called.IsStatic) stack.Pop();
                    object? result = null;
                    if (called.DeclaringType == typeof(SettingsWindow) && called.Name == "SyncUiToConfigAndSave") { saves++; result = saveResult; }
                    else if (called.DeclaringType == typeof(SettingsWindow) && called.Name == "UpdateAutoSaveStatus")
                    {
                        statuses++;
                        Check(!Convert.ToBoolean(arguments[0]), "save feedback is final, not saving");
                        statusSuccess = Convert.ToBoolean(arguments[1]);
                    }
                    else if (called.DeclaringType == typeof(I18n) && called.Name == "T") result = arguments[0];
                    else if (called.DeclaringType?.FullName == "WinPieGestures.AppMessageBox" && called.Name == "Show") { alerts++; alertMessage = (string?)arguments[1]; result = 0; }
                    else throw new Exception("Unexpected save-handler call " + called);
                    if (called.ReturnType != typeof(void)) stack.Push(result);
                    continue;
                }
                throw new Exception("Unsupported save-handler instruction " + op);
            }
            Check(saves == 1, "save invoked once for result " + saveResult);
            Check(statuses == 1 && statusSuccess == saveResult, "feedback reflects real save result " + saveResult);
            Check(alerts == (saveResult ? 0 : 1), "only failed save has a dialog " + saveResult);
            if (!saveResult) Check(alertMessage == "PromptSaveFailedMessage", "failure dialog uses failure message");
        }
    }
}
