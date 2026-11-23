using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WinPieGestures;

internal static class Program
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;

    [STAThread]
    private static void Main(string[] args)
    {
        if (args is not [_, _, _, "--test-instance"])
            throw new ArgumentException("Pass the candidate SHA-256, a sandbox, verify or prepare-manual, and --test-instance.");
        string sandbox = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(sandbox);
        foreach (string variable in new[] { "LOCALAPPDATA", "APPDATA", "TEMP", "TMP" })
            Environment.SetEnvironmentVariable(variable, sandbox);
        Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(AppConfig).Assembly.Location))) == args[0],
            "Loaded host matches the candidate hash");
        ConfigManager.DisableAutoStartSync = true;
        Check((string?)typeof(ConfigManager).GetField("AppDataFolder", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) ==
            Path.Combine(sandbox, "StarPie"), "ConfigManager uses the isolated profile");

        if (args[2] == "verify")
        {
            VerifyDirections();
            VerifyPhysicalPressOwnership();
            VerifyProximityWithSecondaryThreshold();
            VerifySectorFill();
            VerifySessionGates();
            VerifyEnterConfirmation();
        }
        else if (args[2] == "prepare-manual") PrepareManualProfile();
        else throw new ArgumentException("Unknown probe mode.");

        Console.WriteLine($"{_checks} pinned-arrow/proximity checks passed ({args[2]}).");
    }

    private static void VerifyDirections()
    {
        PinnedArrowSlot[] slots =
        [
            new(new(1, 0), new(65, 0)), new(new(1, 1), new(0, 65)),
            new(new(1, 2), new(-65, 0)), new(new(1, 3), new(0, -65)),
            new(new(2, 0, 0), new(118, 5)), new(new(3, 0), new(160, -8)),
            new(new(5, 0), new(245, 0)), new(new(3, 6), new(-161, 5))
        ];
        Check(PinnedArrowNavigation.Next(slots, -1, PinnedArrowNavigation.Left) == 2 &&
            PinnedArrowNavigation.Next(slots, -1, PinnedArrowNavigation.Right) == 0 &&
            PinnedArrowNavigation.Next(slots, -1, PinnedArrowNavigation.Up) == 3 &&
            PinnedArrowNavigation.Next(slots, -1, PinnedArrowNavigation.Down) == 1,
            "Four keys choose the nearest directional sector around the center");
        Check(PinnedArrowNavigation.Next(slots, 0, PinnedArrowNavigation.Right) == 4 &&
            PinnedArrowNavigation.Next(slots, 4, PinnedArrowNavigation.Right) == 5 &&
            PinnedArrowNavigation.Next(slots, 5, PinnedArrowNavigation.Right) == 6,
            "Moving between already visible tiers uses positions and skips the absent fourth tier");
        Check(PinnedArrowNavigation.IndexOf(slots, new(2, 0, 1)) == -1 &&
            PinnedArrowNavigation.IndexOf(slots, new(2, 0, 0)) == 4, "Second rings of different roots have distinct addresses");
        Check(PinnedArrowNavigation.Next(slots, 6, PinnedArrowNavigation.Right) == -1 &&
            PinnedArrowNavigation.Next([], -1, PinnedArrowNavigation.Up) == -1 &&
            PinnedArrowNavigation.Next(slots, 2, 0x0D) == -1, "No wrapping, empty sector, or Enter selection");
        var customNav = new PinnedNavBindings(0x57, 0x53, 0x41, 0x44, 0x20);
        Check(customNav.Match(0x57) == PinnedNavAction.Up && customNav.DirectionVkFor(PinnedNavAction.Up) == PinnedArrowNavigation.Up &&
            customNav.Match(0x20) == PinnedNavAction.Confirm && customNav.Match(PinnedArrowNavigation.Up) == PinnedNavAction.None,
            "Custom pinned keybindings translate directional VKs and confirm key");
    }

    private static void VerifyPhysicalPressOwnership()
    {
        var keys = new PinnedArrowKeys();
        const uint right = PinnedArrowNavigation.Right;
        Check(keys.Observe(right, 0x4D, 0, true) == PinnedArrowKeyEvent.Native &&
            keys.Observe(right, 0x4D, 1, true) == PinnedArrowKeyEvent.Native,
            "Dedicated and keypad arrows with the same virtual key are distinct presses");
        keys.Own(right, 0x4D, 1);
        Check(keys.Observe(right, 0x4D, 0, true) == PinnedArrowKeyEvent.Repeat &&
            keys.Observe(right, 0x4D, 0, false) == PinnedArrowKeyEvent.Native, "A native held keypad key keeps its matching release");
        Check(keys.Observe(right, 0x4D, 1, true) == PinnedArrowKeyEvent.Owned &&
            keys.Observe(right, 0x4D, 1, false) == PinnedArrowKeyEvent.Owned &&
            keys.Observe(right, 0x4D, 1, false) == PinnedArrowKeyEvent.Native, "One consumed down has one consumed release");
        keys.Observe(right, 0x4D, 0, true);
        keys.Own(right, 0x4D, 0);
        Check(keys.Observe(0x66, 0x4D, 0, false) == PinnedArrowKeyEvent.Owned, "Num Lock does not change the physical key release");
        keys.Initialize(key => key == PinnedArrowNavigation.Up);
        Check(keys.Observe(PinnedArrowNavigation.Up, 0x48, 1, true) == PinnedArrowKeyEvent.Repeat &&
            keys.Observe(PinnedArrowNavigation.Up, 0x48, 1, false) == PinnedArrowKeyEvent.Native &&
            keys.Observe(PinnedArrowNavigation.Up, 0x48, 1, true) == PinnedArrowKeyEvent.Native,
            "A key held before the hook started must be released before wheel selection can own it");
        Check(keys.Observe(0x0D, 0x1C, 0, true) == PinnedArrowKeyEvent.Native, "Enter starts native before confirmation");
        keys.Own(0x0D, 0x1C, 0);
        Check(keys.Observe(0x0D, 0x1C, 0, true) == PinnedArrowKeyEvent.Owned &&
            keys.Observe(0x0D, 0x1C, 0, false) == PinnedArrowKeyEvent.Owned &&
            keys.Observe(0x0D, 0x1C, 0, false) == PinnedArrowKeyEvent.Native,
            "Consumed Enter press keeps its matching release swallowed even after close");
        Check(keys.Observe(0x57, 0x11, 0, true) == PinnedArrowKeyEvent.Native, "Custom key W starts native");
        keys.Own(0x57, 0x11, 0);
        Check(keys.Observe(0x57, 0x11, 0, false) == PinnedArrowKeyEvent.Owned,
            "Custom bound single key drains paired key-up cleanly");
    }

    private static void VerifyProximityWithSecondaryThreshold()
    {
        var config = ConfigManager.CurrentConfig;
        config.EnableMultiTier = true;
        config.SubmenuStyle = "Wheel";
        config.SubWheelOuterRadius = 196;
        config.WheelRadius = 72;
        config.InnerRadius = 33;
        config.SubWheelTriggerDistance = 220;
        var profile = SampleProfile();
        var layer = profile.GetActiveLayer();
        var geometry = new FixedRingGeometry(4, 72, 201, 1);
        Point origin = new(-930, 230);
        var dpi = new DpiScale(1.5, 1.25);
        Point Radial(double x) => new(origin.X + x * dpi.DpiScaleX, origin.Y);
        var session = new GestureController.FixedRingSession(profile, layer, 5, geometry, origin, dpi, 196);
        session.Present();
        session.Move(Radial(54));
        session.Move(Radial(219));
        Check(session.Visibility.Contains(3, 0) && !session.NeedsExtendedSecondary,
            "Nearby third tier appears while the second tier still waits for the configured distance");
        session.Move(Radial(221));
        Check(session.Hover == new GestureController.RingHit(3, 0) && session.ExtendedPrimary == 0 && session.NeedsExtendedSecondary,
            "Reaching the original secondary distance requests its reveal even when a nearby outer cell holds the hover");
        session.SetShownSecondary(0, false);
        Check(!session.NeedsExtendedSecondary && session.Hover.Level == 3, "Publishing the secondary keeps the outer hover and satisfies the reveal");
        session.PublishFrame(session.Visibility);
        Check(session.Resolve(session.Hover, out var action) && action?.Name == "East Level 3",
            "A configured outer action resolves by position despite the unknown historical parent");
        var address = session.FeedbackAddress(session.Hover);
        Check(address == (0, layer.Actions[0].SubActions.Count), "Outer feedback is disjoint from actual second-tier addresses");
        session.Move(origin);
        Check(session.Visibility.VisibleDepth == 2 && session.Hover == default, "Returning inside retracts the local outer group");

        Point empty = geometry.GetCenter(3, 1, 12);
        int CountOutlines()
        {
            var cache = typeof(FixedRingGeometry).GetField("_shapes", Private)!.GetValue(geometry)!;
            return (int)cache.GetType().GetProperty("Count")!.GetValue(cache)!;
        }
        int before = CountOutlines();
        Check(geometry.HitVisible(empty, 3, 12, 1) == -1 && CountOutlines() == before,
            "A visible neighbor cannot prime the empty cell outline on the input path");
        Check(geometry.Hit(empty, 3, 12) == 1 && CountOutlines() == before + 1,
            "The probe distinguishes a rejected hole from actually constructing its outline");
    }

    private static void VerifySessionGates()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        ActionExecutor.ReleaseStuckModifiersOverride = () => { };
        var mouse = new MouseHook();
        using var controller = new GestureController(mouse, new KeyboardHook());
        nint foreground = 4321;
        WheelFocusSwitcher.ForegroundWindowGetterOverride = () => foreground;
        var session = (PinnedWheelState)Read(controller, "_pinState")!;
        var type = typeof(GestureController).GetNestedType("PinnedArrowFrame", BindingFlags.NonPublic)!;
        PinnedArrowSlot[] slots = [new(new(1, 0), new(60, 0)), new(new(1, 1), new(0, 60))];
        var frame = Activator.CreateInstance(type, [null, app.Dispatcher, 45L, 0L, 0L, ConfigManager.ConfigurationRevision,
            (nint)4321, null, slots, PinnedNavBindings.Default, PinnedArrowNavigation.Left, PinnedArrowNavigation.Down])!;
        Set(controller, "_pinnedArrowFrame", frame);
        Set(controller, "_gestureVersion", 45L);
        Set(controller, "_isGestureActive", true);
        bool Queue(uint key, ModifierKeys mods = ModifierKeys.None) => (bool)Call(controller, "QueuePinnedArrow", key, mods)!;

        session.Begin(false);
        Check(!Queue(PinnedArrowNavigation.Right), "A non-pinned gesture leaves arrows native");
        session.Begin(true);
        session.Present();
        Check(!Queue(PinnedArrowNavigation.Right), "A pinned wheel waits for the trigger release");
        session.ReleaseTrigger();
        Check(!Queue((uint)'R') && !Queue(0x08) && Queue(0x0D),
            "Unbound keys remain native while default Confirm (Enter) is accepted on pinned wheel");
        Check(!Queue(PinnedArrowNavigation.Left) && !Queue(PinnedArrowNavigation.Down) &&
            !Queue(PinnedArrowNavigation.Right, ModifierKeys.Control), "Activation, layer switch and modified arrows remain native");
        var customFrame = Activator.CreateInstance(type, [null, app.Dispatcher, 45L, 0L, 0L, ConfigManager.ConfigurationRevision,
            (nint)4321, null, slots, new PinnedNavBindings(0x57, 0x53, 0x41, 0x44, 0x20),
            PinnedArrowNavigation.Left, PinnedArrowNavigation.Down])!;
        Set(controller, "_pinnedArrowFrame", customFrame);
        Check(Queue(0x57) && Queue(0x20) && !Queue(0x0D) && !Queue(PinnedArrowNavigation.Right),
            "Rebound navigation keys accept custom VKs while releasing default arrows and Enter");
        Set(controller, "_pinnedArrowFrame", frame);
        foreground = 4322;
        Check(!Queue(PinnedArrowNavigation.Right), "Loss of the pinned foreground releases arrows");
        foreground = 4321;
        mouse.IsPaused = true;
        Check(!Queue(PinnedArrowNavigation.Right), "Paused input stays native");
        mouse.IsPaused = false;

        int enqueued = 0;
        controller.TestActionEnqueued = _ => enqueued++;
        Check(Queue(PinnedArrowNavigation.Right), "An eligible unmodified arrow can post to the UI");
        Call(controller, "RelinquishPinnedArrowSelection");
        Pump();
        Check(Read(controller, "_pinnedArrowFocus") == null && enqueued == 0,
            "Mouse motion invalidates queued arrow selection without dispatching an action");
        Check(Queue(PinnedArrowNavigation.Up), "Another direction remains available in the same session");
        Set(controller, "_layerSwitchSequence", 1L);
        Pump();
        Check(!Queue(PinnedArrowNavigation.Up) && Read(controller, "_pinnedArrowFocus") == null && enqueued == 0,
            "Switching layers retires the old frame and its queued arrow");
        Call(controller, "ClearPinnedArrowSession");
        Check(!Queue(PinnedArrowNavigation.Right), "No key is taken when the presentation is cleared");
    }

    private static void VerifyEnterConfirmation()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        ActionExecutor.ReleaseStuckModifiersOverride = () => { };
        nint fakeHwnd = (nint)9901;
        var fakeTarget = new TargetWindowInfo(fakeHwnd, 5150, "notepad.exe", isEligibleBackgroundTarget: false);
        WheelFocusSwitcher.IsWindowOverride = h => h == fakeHwnd;
        WheelFocusSwitcher.ForegroundWindowGetterOverride = () => fakeHwnd;
        ActiveWindowHelper.RealPidGetterOverride = h => h == fakeHwnd ? 5150u : 0u;
        ActiveWindowHelper.ProcessNameGetterOverride = h => h == fakeHwnd ? "notepad.exe" : "unknown.exe";

        var config = ConfigManager.CurrentConfig;
        config.PinWheelAfterTrigger = true;
        config.EnableMultiTier = true;
        config.SubmenuStyle = "Wheel";
        config.WheelRadius = 120;
        config.InnerRadius = 56;
        config.CoreRadius = 30;
        config.AnimationSpeed = "Custom";
        config.CustomAnimationDurationMs = 0;
        config.PinnedNavUpVkCode = PinnedArrowNavigation.Up;
        config.PinnedNavDownVkCode = PinnedArrowNavigation.Down;
        config.PinnedNavLeftVkCode = PinnedArrowNavigation.Left;
        config.PinnedNavRightVkCode = PinnedArrowNavigation.Right;
        config.PinnedNavConfirmVkCode = PinnedArrowNavigation.Confirm;

        var childLayer = new WheelLayer { Name = "Child", SectorCount = 4 };
        childLayer.Actions =
        [
            new ActionItem { Type = "Hotkey", Name = "ChildPaste", Parameter = "Ctrl+V", IconKey = "Paste" },
            new ActionItem { Type = "None" },
            new ActionItem { Type = "None" },
            new ActionItem { Type = "Hotkey", Name = "ChildNorth", Parameter = "Ctrl+U", IconKey = "Undo" }
        ];
        var rootLayer = new WheelLayer { Name = "Root", SectorCount = 4 };
        rootLayer.Actions =
        [
            new ActionItem { Type = "Hotkey", Name = "Copy", Parameter = "Ctrl+C", IconKey = "Copy" },
            new ActionItem { Type = "Hotkey", Name = "Group", Parameter = "", IconKey = "Folder", SubActions = [Text("ChildA")] },
            new ActionItem
            {
                Type = "OpenWheel",
                Name = "PortalWest",
                PortalTarget = new WheelPortalTarget { SameProfile = true, TargetLayerId = childLayer.EnsureLayerId(), TargetLayerIndex = 1, AnchorMode = "OuterOffset" }
            },
            new ActionItem { Type = "Hotkey", Name = "North", Parameter = "Ctrl+N", IconKey = "Note" }
        ];
        var profile = new WheelProfile { ProcessName = "Global", Layers = { rootLayer, childLayer } };
        profile.ActiveLayerIndex = 0;
        profile.SyncRootPropertiesFromActiveLayer();
        config.Profiles = [profile];
        ConfigManager.MarkConfigurationChanged();

        var screen = ScreenHelper.GetScreenContextAtPoint(new Point(400, 400));
        var center = new Point(screen.PhysicalWorkArea.Left + screen.PhysicalWorkArea.Width / 2,
            screen.PhysicalWorkArea.Top + screen.PhysicalWorkArea.Height / 2);

        using var controller = new GestureController(new MouseHook(), new KeyboardHook());
        ActionItem? executed = null;
        ActionExecutionContext? executedCtx = null;
        controller.TestActionEnqueuedWithContext = (action, ctx) => { executed = action; executedCtx = ctx; };

        var window = new RadialWindow(center, profile) { Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        window.GetType().GetMethod("SetMousePassThrough", Private)!.Invoke(window, [true]);

        try
        {
            long version = 200;
            void StartPinned()
            {
                version++;
                executed = null;
                executedCtx = null;
                profile.ActiveLayerIndex = 0;
                profile.SyncRootPropertiesFromActiveLayer();
                Call(controller, "StopPortalRendering");
                controller.TestEndActiveGesture();
                controller.TestBeginGestureTracking();
                controller.TestSetGestureActive(true, version);
                Set(controller, "_activeProfile", profile);
                var trigger = new TriggerConfig { TriggerType = "Mouse", MouseButton = "RightButton" };
                Set(controller, "_activeTrigger", trigger);
                Set(controller, "_activeActivation", null);
                Set(controller, "_radialWindow", window);
                Set(controller, "_startPoint", center);
                Set(controller, "_lastMovePoint", center);
                Set(controller, "_currentDpiScaleX", screen.DpiScale.DpiScaleX);
                Set(controller, "_currentDpiScaleY", screen.DpiScale.DpiScaleY);
                Set(controller, "_pinTarget", fakeTarget);
                Set(controller, "_pinTrigger", trigger);
                controller.TestCurrentWheelFocusSession = new WheelFocusSession(fakeTarget, center);

                var pin = (PinnedWheelState)Read(controller, "_pinState")!;
                pin.Begin(true);
                pin.Present();
                pin.ReleaseTrigger();

                long rev = ConfigManager.ConfigurationRevision;
                Action<PinnedArrowSlot[]> onSlots = slots =>
                    Call(controller, "PublishPinnedArrows", window, version, rev, slots);
                window.ConfigurePinnedArrowSelection(onSlots);
                window.Present(center, profile, rev, version, null, true, null);
                Call(controller, "PrepareInputRoots", profile);
                Call(controller, "PreparePortalRoot", window, profile, version);
                WaitFor(() => window.MainGrid.IsVisible && window.MainGrid.Opacity > 0 && Read(controller, "_pinnedArrowFrame") != null);
            }

            bool PressKey(uint vk)
            {
                bool handled = (bool)Call(controller, "QueuePinnedArrow", vk, ModifierKeys.None)!;
                Pump();
                return handled;
            }

            // 1. No selection Enter is safe no-op.
            StartPinned();
            Check(PressKey(0x0D) && executed == null && (bool)Read(controller, "_isGestureActive")!,
                "Confirm key with no active selection is consumed without executing or closing");

            // 2. Arrow Right + Enter executes primary leaf action cleanly with target focus.
            Check(PressKey(PinnedArrowNavigation.Right) && PressKey(0x0D), "Right arrow + Enter accepted on leaf sector");
            Check(executed?.Parameter == "Ctrl+C" && executedCtx?.TargetHwnd == fakeHwnd && !(bool)Read(controller, "_isGestureActive")!,
                "Enter confirmation executes highlighted leaf action and closes pinned wheel");

            // 3. Arrow Left + Enter opens OpenWheel portal keeping wheel pinned, then Arrow Right + Enter executes child leaf.
            StartPinned();
            Check(PressKey(PinnedArrowNavigation.Left) && PressKey(0x0D), "Left arrow + Enter triggers OpenWheel portal slot");
            WaitFor(() => (int)Read(controller, "_portalDepth")! == 2 && Read(controller, "_pinnedArrowFrame") != null);
            Check((int)Read(controller, "_portalDepth")! == 2 && ((PinnedWheelState)Read(controller, "_pinState")!).Enabled &&
                (bool)Read(controller, "_isGestureActive")!, "Confirming OpenWheel opens child portal in pinned mode");
            Check(PressKey(PinnedArrowNavigation.Right) && PressKey(0x0D), "Right arrow + Enter confirms child wheel leaf");
            Check(executed?.Parameter == "Ctrl+V" && executedCtx?.TargetHwnd == fakeHwnd && (int)Read(controller, "_portalDepth")! == 0,
                "Enter inside child portal executes child action with root focus and closes portal stack");

            // 4. Arrow Down + Enter on actionless parent expands second-level cascade without closing.
            StartPinned();
            Check(PressKey(PinnedArrowNavigation.Down) && PressKey(0x0D), "Down arrow + Enter hits actionless parent sector");
            Check((int)Read(controller, "_pinExpandedParent")! == 1 && executed == null && (bool)Read(controller, "_isGestureActive")!,
                "Enter on actionless parent expands sub-ring cascade instead of closing");

            // 5. Custom keybindings (W = Up, Space = Confirm) move and confirm while default Enter is released.
            config.PinnedNavUpVkCode = 0x57;
            config.PinnedNavConfirmVkCode = 0x20;
            ConfigManager.MarkConfigurationChanged();
            StartPinned();
            Check(!PressKey(0x0D) && PressKey(0x57) && PressKey(0x20),
                "Custom keybindings replace Confirm/Up cleanly and release old Enter key");
            Check(executed?.Parameter == "Ctrl+N" && !(bool)Read(controller, "_isGestureActive")!,
                "Custom Confirm key (Space) executes sector highlighted via custom Up key (W)");
        }
        finally
        {
            Call(controller, "StopPortalRendering");
            controller.TestEndActiveGesture();
            window.CloseFast();
            Pump();
            WheelFocusSwitcher.ResetTestSeams();
            ActiveWindowHelper.ResetTestSeams();
        }
    }

    private static void VerifySectorFill()
    {
        // Exercise the real Path painting without showing a Window or installing hooks.
        var window = (RadialWindow)RuntimeHelpers.GetUninitializedObject(typeof(RadialWindow));
        var surface = Brushes.WhiteSmoke;
        var highlight = Brushes.DodgerBlue;
        var secondaryHighlight = Brushes.MediumPurple;
        Set(window, "_defaultSectorBrush", surface);
        Set(window, "_highlightSectorBrush", highlight);
        Set(window, "_subDefaultSectorBrush", surface);
        Set(window, "_subHighlightSectorBrush", secondaryHighlight);
        var path = new System.Windows.Shapes.Path { Data = new RectangleGeometry(new Rect(0, 0, 40, 30)) };
        var labels = new System.Windows.Controls.StackPanel();
        Call(window, "PaintWheelSector", path, labels, false, true);
        Check(ReferenceEquals(path.Fill, highlight), "A selected primary uses the full themed surface fill");
        Call(window, "PaintWheelSector", path, labels, true, true);
        Check(ReferenceEquals(path.Fill, secondaryHighlight), "A selected secondary uses its own full highlight fill");
        Call(window, "PaintWheelSector", path, labels, true, false);
        Check(ReferenceEquals(path.Fill, surface), "Clearing selection restores the ordinary sector surface");
    }

    private static WheelProfile SampleProfile()
    {
        var profile = new WheelProfile
        {
            SectorCount = 4,
            Actions = [Text("East"), Text("South"), Text("West"), Text("North")]
        };
        profile.EnsureLayers();
        var layer = profile.GetActiveLayer();
        layer.Actions[0].SubActions = [Text("East child 1"), Text("East child 2")];
        layer.MaxMenuDepth = 5;
        layer.FixedRings = new() { AlwaysVisible = false };
        layer.FixedRings.SetSlot(3, 0, new() { Action = Text("East Level 3"), ParentIndex = -93 });
        layer.FixedRings.SetSlot(3, 6, new() { Action = Text("West Level 3") });
        layer.FixedRings.SetSlot(5, 0, new() { Action = Text("East Level 5"), ParentIndex = 918 });
        layer.FixedRings.SetSlot(5, 10, new() { Action = Text("West Level 5") });
        return profile;
    }

    private static void PrepareManualProfile()
    {
        var config = ConfigManager.CreateDefaultConfig();
        config.AutoCheckUpdate = false;
        config.PinWheelAfterTrigger = true;
        config.LongPressTrigger = true;
        config.LongPressDelayMs = 240;
        config.AutoExpandSubRingsOnPopup = false;
        config.SubmenuStyle = "Wheel";
        config.EnableMultiTier = true;
        config.Profiles = [SampleProfile()];
        ConfigManager.CurrentConfig = config;
        Check(ConfigManager.SaveConfig(), "Saved the manual fixture in the isolated profile");
    }

    private static ActionItem Text(string value) => new() { Type = "Text", Name = value, Parameter = value, IconKey = "Copy" };
    private static object? Read(object target, string field) => target.GetType().GetField(field, Private)!.GetValue(target);
    private static void Set(object target, string field, object? value) => target.GetType().GetField(field, Private)!.SetValue(target, value);
    private static object? Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private)!.Invoke(target, args);
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, (Action)(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void WaitFor(Func<bool> condition, int timeoutMs = 2000)
    {
        int deadline = Environment.TickCount + timeoutMs;
        while (!condition() && Environment.TickCount < deadline)
        {
            Pump();
            System.Threading.Thread.Sleep(10);
        }
        Pump();
    }

    private static void Check(bool ok, string description)
    {
        if (!ok) throw new InvalidOperationException(description);
        Console.WriteLine("PASS " + description);
        _checks++;
    }
}
