using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Threading;
using WinPieGestures;

internal static class Program
{
    private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.NonPublic;

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Send) { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, (Action)(() =>
        {
            timer.Stop();
            frame.Continue = false;
        }));
        Dispatcher.PushFrame(frame);
    }

    [STAThread]
    private static void Main()
    {
        int checks = 0;
        void Check(bool result, string name)
        {
            if (!result) throw new InvalidOperationException(name);
            Console.WriteLine($"PASS {++checks} {name}");
        }

        string candidate = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "CandidateDll").Value!;
        Check(SHA256.HashData(File.ReadAllBytes(candidate)).SequenceEqual(
            SHA256.HashData(File.ReadAllBytes(typeof(AppConfig).Assembly.Location))), "Candidate hash");
        var profile = new WheelProfile { Layers = { new(), new(), new() } };
        var second = new WheelProfile { ProcessName = "second.exe", Layers = { new() } };
        var focus = new WheelFocusSession(new TargetWindowInfo((nint)1, 42, "root.exe", false), new Point(5, 5));
        var root = new PortalFrame(new(profile, profile.Layers[0]), new Point(5, 5), 100, new Point());
        var child = new PortalFrame(new(second, second.Layers[0]), new Point(205, 5), 100, new Point(105, 5));
        var stack = new PortalSessionStack(root, 42, 3, focus);
        Check(!stack.CanPush(root.Address) && stack.CanPush(child.Address), "Direct self gate");
        Check(stack.Back(PortalBackInput.Center, false) == PortalBackDecision.Close, "Root no-op closes");
        stack.Push(child);
        foreach (var input in Enum.GetValues<PortalBackInput>())
            Check(stack.Back(input, false) == PortalBackDecision.Pop, "Child returns for " + input);
        Check(stack.Back(PortalBackInput.Escape, true) == PortalBackDecision.Close &&
            stack.Back(PortalBackInput.Backspace, true) == PortalBackDecision.Pop, "Escape override applies only to Escape");
        Check(!stack.CanPush(root.Address), "Cycle gates an ancestor");
        stack.Push(new(new(profile, profile.Layers[1]), new Point(0, 5), 100, new Point()));
        Check(!stack.CanPush(new(profile, profile.Layers[2])) && ReferenceEquals(stack.Focus, focus), "Depth limit and root focus identity");
        stack.Pop(2, true);
        Check(stack.Depth == 2 && stack.Current == child && !stack.AcceptPointerSlot(2) &&
            stack.AcceptPointerSlot(3) && stack.AcceptPointerSlot(2), "Return blocks the original slot until pointer exits");
        stack.Pop(7, false);
        Check(stack.Current == root && stack.AcceptPointerSlot(7), "Disabled return guard");
        Check(stack.MatchesRevision(42) && !stack.MatchesRevision(43), "Revision guard");

        var config = new AppConfig();
        Check(!PortalPlacement.Crossed(83, 100, config) && PortalPlacement.Crossed(84, 100, config), "Crossing threshold");
        config.PortalEnableCrossExpand = false;
        Check(!PortalPlacement.Crossed(200, 100, config), "Crossing can be disabled");
        using var elapsed = new ManualResetEventSlim();
        long fired = -1;
        using var dwell = new PortalDwell(generation => { Interlocked.Exchange(ref fired, generation); elapsed.Set(); });
        long old = dwell.Arm(100);
        dwell.Dispose();
        Check(!dwell.IsCurrent(old), "Leaving the slot invalidates its timer generation");
        long next = dwell.Arm(100);
        Check(elapsed.Wait(TimeSpan.FromSeconds(2)) && next == Volatile.Read(ref fired) && dwell.IsCurrent(next),
            "Only the current dwell completes");
        dwell.Dispose();
        Check(!dwell.IsCurrent(next), "A queued dwell result after return is stale");

        var origin = new Point(-260, 430);
        var work = new Rect(-1500, -100, 1500, 1100);
        var dpi = new DpiScale(1.5, 1.25);
        Check(PortalPlacement.TryPlace(origin, new Vector(100, 0), 120, 122, 6, true, work, dpi, out Point placed) &&
            Math.Abs(PortalPlacement.Radius(origin, placed, dpi) - 248) < .001, "Edge placement maintains tangency on negative mixed-scale coordinates");
        Check(placed.X + 122 * dpi.DpiScaleX <= work.Right && placed.X - 122 * dpi.DpiScaleX >= work.Left &&
            placed.Y + 122 * dpi.DpiScaleY <= work.Bottom && placed.Y - 122 * dpi.DpiScaleY >= work.Top, "Child circle fits work area");
        Check(!PortalPlacement.TryPlace(new Point(180, 180), new Vector(100, 0), 120, 122, 6, true,
            new Rect(0, 0, 360, 360), new DpiScale(1, 1), out _), "An impossible tangent does not overlap");
        Check(PortalPlacement.TryPlace(origin, new Vector(-50, 0), 120, 100, 6, false, work, dpi, out Point centered) &&
            centered.X == -335, "Sector anchor converts DIP to physical pixels");
        Check(PortalPlacement.Handoff(false, true, false, true) == PortalPointerHandoff.Warp &&
            PortalPlacement.Handoff(false, true, true, true) == PortalPointerHandoff.Pin &&
            PortalPlacement.Handoff(true, false, false, false) == PortalPointerHandoff.Pin &&
            PortalPlacement.Handoff(false, false, false, false) == PortalPointerHandoff.Keep, "Cursor handoff policy");

        var pin = new PinnedWheelState();
        pin.Begin(true);
        pin.Present();
        Check(pin.TakeMouseUp("RightButton", "RightButton", true) == PinnedMouseRelease.Original &&
            pin.TakeMouseUp("RightButton", "RightButton", true) == PinnedMouseRelease.None, "Held right-up consumed exactly once after AutoPin");
        pin.OwnMouseUp("RightButton");
        pin.End();
        Check(pin.ConsumeMouseUp("RightButton") && !pin.ConsumeMouseUp("RightButton"), "Return consumes paired right-up after closure");
        pin.OwnKeyUp(27);
        Check(pin.ConsumeKeyUp(27) && !pin.ConsumeKeyUp(27), "Escape consumes paired key-up");

        string sandbox = Directory.CreateTempSubdirectory("starpie-openwheel-input-").FullName;
        foreach (string variable in new[] { "APPDATA", "LOCALAPPDATA", "TEMP", "TMP" })
            Environment.SetEnvironmentVariable(variable, sandbox);
        try
        {
            ConfigManager.DisableAutoStartSync = true;
            typeof(ActionExecutor).GetProperty("ReleaseStuckModifiersOverride", BindingFlags.Static | BindingFlags.NonPublic)!
                .SetValue(null, (Action)(() => { }));
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var mouse = new MouseHook();
            using var controller = new GestureController(mouse);
            var controllerType = typeof(GestureController);
            void Set(string field, object? value) => controllerType.GetField(field, InstanceFlags)!.SetValue(controller, value);
            object? Read(string field) => controllerType.GetField(field, InstanceFlags)!.GetValue(controller);
            object? Call(string method, params object?[] args) => controllerType.GetMethod(method, InstanceFlags)!.Invoke(controller, args);

            controller.TestSetGestureActive(true, 71);
            Set("_portalVersion", 71L);
            Set("_portalVisualEpoch", 74L);
            Set("_portalAcceptInput", true);
            var releasePoint = new Point(30, 90);
            Check((bool)Call("QueuePortalRelease", releasePoint, "", null, null)!, "Controller queues a release against the active frame");
            object queued = Read("_portalRelease")!;
            Call("QueuePortalMove", new Point(90, 90));
            Check((Point)queued.GetType().GetProperty("Point")!.GetValue(queued)! == releasePoint &&
                (long)queued.GetType().GetProperty("Frame")!.GetValue(queued)! == 74, "Later pointer moves cannot replace the sampled release");
            Set("_portalVisualEpoch", 75L);
            Set("_mouseReleasePending", true);
            var controllerPin = (PinnedWheelState)Read("_pinState")!;
            controllerPin.Begin(true);
            controllerPin.Present();
            Call("FinishPortalHold", queued);
            Check(controllerPin.Presented && controllerPin.TriggerReleased && (bool)Read("_mouseReleasePending")! == false,
                "Old-frame release drains pending debounce and retains the new frame without hit testing");
            Call("StopPortalRendering");
            controller.TestEndActiveGesture();

            nint fakeHwnd = (nint)0x51A7;
            WheelFocusSwitcher.IsWindowOverride = h => h == fakeHwnd;
            WheelFocusSwitcher.ForegroundWindowGetterOverride = () => fakeHwnd;
            ActiveWindowHelper.RealPidGetterOverride = h => h == fakeHwnd ? 4242u : 0u;
            ActiveWindowHelper.ProcessNameGetterOverride = h => h == fakeHwnd ? "cad.exe" : "unknown.exe";
            var fakeTarget = new TargetWindowInfo(fakeHwnd, 4242, "cad.exe", isEligibleBackgroundTarget: false);

            var liveConfig = ConfigManager.CurrentConfig;
            liveConfig.PinWheelAfterTrigger = false;
            liveConfig.EnableMultiTier = false;
            liveConfig.EnableOuterEscapeCancel = true;
            liveConfig.WheelRadius = 120;
            liveConfig.InnerRadius = 56;
            liveConfig.CoreRadius = 30;
            liveConfig.CoreDeadzoneRadius = 22;
            liveConfig.OuterEscapeDistance = 260;

            var childLayer = new WheelLayer { Name = "Child", SectorCount = 8, EnableCenterAction = true, CenterAction = new ActionItem { Type = "Hotkey", Parameter = "Ctrl+Z", Name = "Undo" } };
            childLayer.Actions = Enumerable.Range(0, 8).Select(_ => new ActionItem { Type = "None" }).ToList();
            childLayer.Actions[0] = new ActionItem { Type = "Hotkey", Parameter = "Ctrl+C", Name = "Copy" };
            childLayer.Actions[5] = new ActionItem { Type = "Hotkey", Parameter = "Ctrl+Q", Name = "OpposingNW" };
            var rootLayer = new WheelLayer { Name = "Root", SectorCount = 8 };
            rootLayer.Actions = Enumerable.Range(0, 8).Select(_ => new ActionItem { Type = "None" }).ToList();
            rootLayer.Actions[0] = new ActionItem { Type = "ActionPanel", Parameter = "Panel1", Name = "Panel" };
            rootLayer.Actions[1] = new ActionItem
            {
                Type = "OpenWheel",
                Name = "Portal",
                PortalTarget = new WheelPortalTarget { SameProfile = true, TargetLayerId = childLayer.EnsureLayerId(), TargetLayerIndex = 1, AnchorMode = "SectorCenter" }
            };
            var liveProfile = new WheelProfile { ProcessName = "Global", Layers = { rootLayer, childLayer } };
            liveProfile.ActiveLayerIndex = 0;
            liveProfile.SyncRootPropertiesFromActiveLayer();
            liveConfig.Profiles = [liveProfile];
            ConfigManager.MarkConfigurationChanged();

            var screen = ScreenHelper.GetScreenContextAtPoint(new Point(400, 400));
            var center = new Point(screen.PhysicalWorkArea.Left + screen.PhysicalWorkArea.Width / 2,
                screen.PhysicalWorkArea.Top + screen.PhysicalWorkArea.Height / 2);
            double dpiX = Math.Max(1.0, screen.DpiScale.DpiScaleX);
            var window = new RadialWindow(center, liveProfile) { Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
            window.Show();
            window.GetType().GetMethod("SetMousePassThrough", InstanceFlags)!.Invoke(window, [true]);
            try
            {
                ActionItem? executed = null;
                ActionExecutionContext? executedCtx = null;
                controller.TestActionEnqueuedWithContext = (action, ctx) => { executed = action; executedCtx = ctx; };

                void StartSession(TriggerConfig trigger, bool mouseSession)
                {
                    executed = null;
                    executedCtx = null;
                    controller.TestBeginGestureTracking();
                    controller.TestSetGestureActive(true, 100);
                    Set("_activeProfile", liveProfile);
                    Set("_activeTrigger", trigger);
                    Set("_activeActivation", null);
                    Set("_radialWindow", window);
                    Set("_startPoint", center);
                    Set("_currentDpiScaleX", screen.DpiScale.DpiScaleX);
                    Set("_currentDpiScaleY", screen.DpiScale.DpiScaleY);
                    if (mouseSession)
                    {
                        controller.TestCurrentWheelFocusSession = new WheelFocusSession(fakeTarget, center);
                        Set("_keyboardMatchingTarget", null);
                        Set("_keyboardPanelTarget", (nint)0);
                        Set("_keyboardPanelTargetPid", 0u);
                    }
                    else
                    {
                        controller.TestCurrentWheelFocusSession = null;
                        Set("_keyboardMatchingTarget", fakeTarget);
                        Set("_keyboardPanelTarget", fakeHwnd);
                        Set("_keyboardPanelTargetPid", 4242u);
                    }
                    window.Present(center, liveProfile, ConfigManager.ConfigurationRevision, 100);
                    Pump();
                    Call("PrepareInputRoots", liveProfile);
                    Call("PreparePortalRoot", window, liveProfile, 100L);
                }

                var kbReleasePt = new Point(center.X + 165 * dpiX, center.Y);
                var releaseType = controllerType.GetNestedType("PortalRelease", BindingFlags.NonPublic)!;

                // P1-2 fallback A: PreparePortalRoot recovers valid Focus from foreground + _keyboardPanelTarget even when _keyboardMatchingTarget mismatches.
                StartSession(new TriggerConfig { TriggerType = "Keyboard", VkCode = 112 }, mouseSession: false);
                Set("_keyboardMatchingTarget", new TargetWindowInfo((nint)0xDEAD, 999, "wrong.exe", false));
                Call("PreparePortalRoot", window, liveProfile, 100L);
                Check(((PortalSessionStack)Read("_portalStack")!).Focus?.TargetInfo.Hwnd == fakeHwnd,
                    "PreparePortalRoot recovers valid focus from foreground _keyboardPanelTarget when _keyboardMatchingTarget mismatches");

                // P1-2 fallback B: ActionPanel release falls back to _keyboardPanelTarget/_keyboardPanelTargetPid when stack Focus is null.
                StartSession(new TriggerConfig { TriggerType = "Keyboard", VkCode = 112 }, mouseSession: false);
                Set("_keyboardMatchingTarget", null);
                WheelFocusSwitcher.ForegroundWindowGetterOverride = () => (nint)0x9999;
                Call("PreparePortalRoot", window, liveProfile, 100L);
                Check(((PortalSessionStack)Read("_portalStack")!).Focus == null,
                    "Off-target foreground produces null stack focus before ActionPanel fallback");
                long noFocusEpoch = (long)Read("_portalVisualEpoch")!;
                Call("FinishPortalHold", Activator.CreateInstance(releaseType, kbReleasePt, "", null, noFocusEpoch)!);
                Pump();
                Check(executed?.Type == "ActionPanel" && executedCtx?.TargetHwnd == fakeHwnd && executedCtx?.TargetProcessId == 4242u,
                    "ActionPanel release falls back to _keyboardPanelTarget when portal stack focus is null");
                WheelFocusSwitcher.ForegroundWindowGetterOverride = () => fakeHwnd;

                // P2-5 fallback: HandlePinnedMouse and EndPinnedTracking treat null TriggerConfig.MouseButton as global Config.TriggerButton.
                liveConfig.TriggerButton = "RightButton";
                controller.TestBeginGestureTracking();
                controller.TestSetGestureActive(true, 105);
                Set("_pinTrigger", null);
                Set("_activeTrigger", new TriggerConfig { TriggerType = "Mouse", MouseButton = null! });
                var nullBtnPin = (PinnedWheelState)Read("_pinState")!;
                nullBtnPin.Begin(true);
                nullBtnPin.Present();
                var rawTriggerUp = new RawMouseEventArgs(517, "RightButton", 0u, false, center.X, center.Y);
                controller.TestHookOnRawMouseButton(rawTriggerUp);
                Check(rawTriggerUp.Handled && nullBtnPin.TriggerReleased,
                    "HandlePinnedMouse falls back to global TriggerButton when TriggerConfig.MouseButton is null");
                nullBtnPin.Begin(true);
                nullBtnPin.Present();
                Call("EndPinnedTracking");
                controller.TestEndActiveGesture();
                var rawOwnedUp = new RawMouseEventArgs(517, "RightButton", 0u, false, center.X, center.Y);
                controller.TestHookOnRawMouseButton(rawOwnedUp);
                Check(rawOwnedUp.Handled,
                    "EndPinnedTracking owns paired trigger release when TriggerConfig.MouseButton is null");

                // Fix 2: keyboard trigger releasing on ActionPanel at Depth 1 inherits _portalStack.Focus.
                StartSession(new TriggerConfig { TriggerType = "Keyboard", VkCode = 112 }, mouseSession: false);
                long epoch = (long)Read("_portalVisualEpoch")!;
                Call("FinishPortalHold", Activator.CreateInstance(releaseType, kbReleasePt, "", null, epoch)!);
                Pump();
                Check(executed?.Type == "ActionPanel" && executedCtx?.TargetHwnd == fakeHwnd,
                    "Keyboard release at depth 1 preserves portal stack focus context for ActionPanel");

                // Fix 1: Hold & Swipe beyond-rim release on child wheel (Depth 2).
                StartSession(new TriggerConfig { TriggerType = "Mouse", MouseButton = "RightButton" }, mouseSession: true);
                var portalKeyType = controllerType.GetNestedType("PortalSlotKey", BindingFlags.NonPublic)!;
                var portalHitType = controllerType.GetNestedType("PortalHit", BindingFlags.NonPublic)!;
                object OpenHit() => Activator.CreateInstance(portalHitType,
                    Activator.CreateInstance(portalKeyType, 1, 1, null)!,
                    rootLayer.Actions[1],
                    new Vector(0, 88),
                    120.0, 1, -1, false, false)!;

                // Open to child at Sector 1 anchor so pointer matches child center (Keep mode, no SetCursorPos).
                var childAnchorPt = new Point(center.X, center.Y + 88 * screen.DpiScale.DpiScaleY);
                Check((bool)Call("OpenPortal", OpenHit(), childAnchorPt, false)!, "OpenPortal transitions to depth 2 in SectorCenter");
                var activeStack = (PortalSessionStack)Read("_portalStack")!;
                var childCenter = activeStack.Current.PhysicalCenter;

                // Release on child empty slot (West = sector 4) -> must pop back to depth 1, not execute or close.
                epoch = (long)Read("_portalVisualEpoch")!;
                var emptyWestPt = new Point(childCenter.X - 150 * dpiX, childCenter.Y);
                Call("FinishPortalHold", Activator.CreateInstance(releaseType, emptyWestPt, "RightButton", null, epoch)!);
                Check(activeStack.Depth == 1 && (int)Read("_portalDepth")! == 1 && executed == null,
                    "Hold-and-swipe release on child empty slot pops back to parent without closing");

                // Re-enter child (clearing reentry guard by pointer exit or disabled reentry guard).
                liveConfig.PortalEnableReentryGuard = false;
                Check((bool)Call("OpenPortal", OpenHit(), childAnchorPt, false)!, "Re-enter child wheel at depth 2");
                childCenter = activeStack.Current.PhysicalCenter;

                // Unpin so we test Hold & Swipe center release against child CenterAction (Ctrl+Z).
                ((PinnedWheelState)Read("_pinState")!).End();
                epoch = (long)Read("_portalVisualEpoch")!;
                Call("FinishPortalHold", Activator.CreateInstance(releaseType, childCenter, "RightButton", null, epoch)!);
                Check(activeStack.Depth == 1 && executed == null,
                    "Hold-and-swipe release in child center pops back and overrides EnableCenterAction");

                // Re-enter child, unpin, release beyond WheelRadius (170 DIP > 120 DIP, < 260 DIP escape) in Sector 0 (Ctrl+C).
                Check((bool)Call("OpenPortal", OpenHit(), childAnchorPt, false)!, "Re-enter child wheel for beyond-rim leaf release");
                ((PinnedWheelState)Read("_pinState")!).End();
                childCenter = activeStack.Current.PhysicalCenter;
                epoch = (long)Read("_portalVisualEpoch")!;
                var beyondRimEast = new Point(childCenter.X + 170 * dpiX, childCenter.Y);
                Call("FinishPortalHold", Activator.CreateInstance(releaseType, beyondRimEast, "RightButton", null, epoch)!);
                Pump();
                Check(executed?.Parameter == "Ctrl+C" && executedCtx?.TargetHwnd == fakeHwnd && (int)Read("_portalDepth")! == 0,
                    "Hold-and-swipe beyond-rim release on child leaf executes with root focus and closes entire stack");

                // SectorCenter opposing sector test: parent center sits inside child Sector 5 (NW), so Hold & Swipe must select & execute it instead of collapsing to parent.
                liveConfig.PortalEnableReentryGuard = true;
                liveConfig.PortalEnableCrossExpand = true;
                double dpiY = Math.Max(1.0, screen.DpiScale.DpiScaleY);
                var sector1CrossPt = new Point(center.X + 110 * 0.70710678 * dpiX, center.Y + 110 * 0.70710678 * dpiY);
                StartSession(new TriggerConfig { TriggerType = "Mouse", MouseButton = "RightButton" }, mouseSession: true);
                Set("_portalMovePoint", sector1CrossPt);
                Check((bool)Call("EvaluatePortalHover", sector1CrossPt, -1L)!, "Hold-and-swipe opens SectorCenter child wheel at depth 2");
                activeStack = (PortalSessionStack)Read("_portalStack")!;
                Set("_portalMovePoint", center);
                Check(!(bool)Call("EvaluatePortalHover", center, -1L)! && activeStack.Depth == 2,
                    "SectorCenter hover over overlapping parent center stays in child wheel instead of collapsing");
                Call("ProcessMoveCore", center);
                epoch = (long)Read("_portalVisualEpoch")!;
                Call("FinishPortalHold", Activator.CreateInstance(releaseType, center, "RightButton", null, epoch)!);
                Pump();
                Check(executed?.Parameter == "Ctrl+Q" && executedCtx?.TargetHwnd == fakeHwnd && (int)Read("_portalDepth")! == 0,
                    "SectorCenter release on opposing child sector executes Ctrl+Q with root focus and closes stack");

                // Repeated Hold & Swipe open -> drag back to parent center -> re-open #2 -> drag back -> re-open #3 -> leaf execute (OuterOffset, ReentryGuard enabled).
                rootLayer.Actions[1].PortalTarget!.AnchorMode = "OuterOffset";
                ConfigManager.MarkConfigurationChanged();
                StartSession(new TriggerConfig { TriggerType = "Mouse", MouseButton = "RightButton" }, mouseSession: true);
                Set("_portalMovePoint", sector1CrossPt);
                Check((bool)Call("EvaluatePortalHover", sector1CrossPt, -1L)!, "Hold-and-swipe crosses sector 1 to open OuterOffset child wheel (#1)");
                activeStack = (PortalSessionStack)Read("_portalStack")!;
                Check(activeStack.Depth == 2 && !((PinnedWheelState)Read("_pinState")!).Enabled,
                    "Child wheel opens unpinned during Hold-and-swipe");
                Set("_portalMovePoint", center);
                Check((bool)Call("EvaluatePortalHover", center, -1L)!, "Dragging back to parent center pops back to parent wheel (#1)");
                Check(activeStack.Depth == 1 && !((PinnedWheelState)Read("_pinState")!).Enabled && activeStack.BlockedSlot == null,
                    "Drag-back to parent center keeps Hold-and-swipe unpinned and clears reentry block");
                Set("_portalMovePoint", sector1CrossPt);
                Check((bool)Call("EvaluatePortalHover", sector1CrossPt, -1L)!, "Hold-and-swipe re-opens child wheel (#2) after drag-back");
                Check(activeStack.Depth == 2, "Second child expansion reaches depth 2");
                Set("_portalMovePoint", center);
                Check((bool)Call("EvaluatePortalHover", center, -1L)!, "Dragging back to parent center pops back (#2)");
                Set("_portalMovePoint", sector1CrossPt);
                Check((bool)Call("EvaluatePortalHover", sector1CrossPt, -1L)!, "Hold-and-swipe re-opens child wheel (#3) repeatedly");
                childCenter = activeStack.Current.PhysicalCenter;
                epoch = (long)Read("_portalVisualEpoch")!;
                Call("FinishPortalHold", Activator.CreateInstance(releaseType, new Point(childCenter.X + 170 * dpiX, childCenter.Y), "RightButton", null, epoch)!);
                Pump();
                Check(executed?.Parameter == "Ctrl+C" && (int)Read("_portalDepth")! == 0,
                    "Re-opened child wheel executes leaf action on release and closes stack");

                // Pinned explicit re-click overrides reentry guard when pointer stays on the portal slot after returning.
                StartSession(new TriggerConfig { TriggerType = "Mouse", MouseButton = "RightButton" }, mouseSession: true);
                var sector1MidPt = new Point(center.X + 88 * 0.70710678 * dpiX, center.Y + 88 * 0.70710678 * dpiY);
                var pinState = (PinnedWheelState)Read("_pinState")!;
                pinState.Begin(true);
                pinState.Present();
                pinState.ReleaseTrigger();
                Set("_mouseTriggerDown", false);
                Check((bool)Call("ConfirmPortalPoint", sector1MidPt, false, sector1MidPt, null)!, "Pinned click opens child wheel");
                activeStack = (PortalSessionStack)Read("_portalStack")!;
                Set("_portalMovePoint", sector1MidPt);
                Call("BackFromPortal", PortalBackInput.Backspace, false);
                Check(activeStack.Depth == 1 && activeStack.BlockedSlot != null, "Backspace pop leaves BlockedSlot armed while pointer stays on slot");
                Check((bool)Call("ConfirmPortalPoint", sector1MidPt, false, sector1MidPt, null)! && activeStack.Depth == 2,
                    "Explicit pinned click re-opens child wheel even while pointer remained on the portal slot");
            }
            finally
            {
                Call("StopPortalRendering");
                controller.TestEndActiveGesture();
                window.Close();
                Pump();
                WheelFocusSwitcher.ResetTestSeams();
            }
        }
        finally
        {
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, true);
        }
        Console.WriteLine($"{checks} passed");
    }
}
