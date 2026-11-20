using System.Reflection;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using WinPieGestures;

namespace SubmenuParentLockTests;

public static partial class Program
{
    private static void ReviewCheck(bool condition, string name) =>
        Assert(condition, "Review_" + name, "Production behavior verified", "Production behavior violated");

    private static void RunReviewRegressions()
    {
        Console.WriteLine("\n--- Independent review counterexamples ---");
        var config = new AppConfig
        {
            LockParentSectorOnSubmenu = true, EnableMultiTier = true,
            WheelRadius = 100, InnerRadius = 40, CoreDeadzoneRadius = 20,
            SubWheelOuterRadius = 155, SubWheelTriggerDistance = 141,
            EnableOuterEscapeCancel = false
        };
        ConfigManager.CurrentConfig = config;
        var profile = CreateTestProfile(8, "Global");
        config.Profiles = [profile];
        using (var controller = new GestureController(new MouseHook()))
        {
            controller.TestActiveProfile = profile;
            controller.TestSetStartPoint(new Point(0, 0));
            controller.TestSetGestureActive(true, 500);
            controller.TestPrepareSubmenuGeometry();
            controller.TestProcessMove(new Point(147, -29));
            ReviewCheck(controller.TestSubmenuLock.IsLocked, "StalePresentation_Setup");
            ReviewCheck(!controller.TestShowRadialUI(new Point(), profile, 499), "StalePresentation_Rejected");
            ReviewCheck(controller.TestSubmenuLock.IsLocked, "StalePresentation_PreservesCurrentLock");

            controller.TestSetGestureActive(true, 501);
            controller.TestPrepareSubmenuGeometry();
            controller.TestProcessMove(new Point(120, 0));
            ReviewCheck(controller.TestGetHoverState().SubSector == -1 && !controller.TestSubmenuLock.IsLocked,
                "HiddenRingBeforeTrigger_HasNoChildHit");
            controller.TestProcessMove(new Point(147, -29));
            controller.TestProcessMove(new Point(180, 0));
            ReviewCheck(controller.TestGetHoverState().SubSector == -1 && controller.TestSubmenuLock.IsLocked,
                "BeyondVisibleRing_ClearsChildAndRetainsParent");

            controller.TestSetGestureActive(true, 508);
            controller.TestPrepareSubmenuGeometry();
            controller.TestProcessMove(new Point(147, -29));
            profile.Actions[1].Type = "System";
            profile.Actions[1].Parameter = "volumeup";
            profile.Actions[1].SubActions.Clear();
            config.EnableOuterEscapeCancel = true;
            config.OuterEscapeDistance = 200;
            controller.TestProcessMove(new Point(180, 180));
            ReviewCheck(controller.TestGetHoverState().Escaped && !controller.TestSubmenuLock.IsLocked,
                "LockedNonVolumeParent_CannotBorrowAdjacentVolumeEscapeExemption");
            config.EnableOuterEscapeCancel = false;

            controller.TestSetGestureActive(true, 502);
            controller.TestPrepareSubmenuGeometry();
            controller.TestProcessMove(new Point(147, -29));
            config.LockParentSectorOnSubmenu = false;
            controller.TestProcessMove(new Point(70, 0));
            ReviewCheck(!controller.TestSubmenuLock.IsLocked, "DisablingOption_ClearsLock");
            config.LockParentSectorOnSubmenu = true;

            // Global GetEffectiveAction deliberately returns placeholder slots. The new
            // interaction must still reject unconfigured children before highlighting/dispatch.
            profile.Actions[0].SubActions = [new ActionItem { Parameter = "Ctrl+A" }, new ActionItem()];
            controller.TestSetGestureActive(true, 503);
            controller.TestPrepareSubmenuGeometry();
            controller.TestProcessMove(new Point(150, 5));
            ReviewCheck(controller.TestGetHoverState().SubSector == -1,
                "GlobalPlaceholderChild_IsNotSelectable");
            ActionItem? dispatched = null;
            controller.TestActionEnqueued = action => dispatched = action;
            controller.TestCompleteMouseTriggerRelease(new Point(150, 5), "RightButton");
            PumpDispatcher();
            ReviewCheck(dispatched == null, "DirectEntryIntoEmptyChild_DoesNotExecuteParent");
        }

        var disabled = CreateTestProfile(8, "Global");
        disabled.Actions[0].Type = "None";
        using (var controller = new GestureController(new MouseHook()))
        {
            controller.TestActiveProfile = disabled;
            controller.TestSetGestureActive(true, 506);
            controller.TestPrepareSubmenuGeometry();
            controller.TestSetStartPoint(new Point());
            controller.TestProcessMove(new Point(147, -29));
            ReviewCheck(!controller.TestSubmenuLock.IsLocked, "DisabledParent_DoesNotOfferSubmenu");
        }

        var global = CreateTestProfile(8, "Global");
        global.Actions[1].SubActions.Clear();
        global.Actions[2].SubActions = [new ActionItem { Parameter = "Ctrl+A" }];
        var local = new WheelProfile { ProcessName = "editor.exe", SectorCount = 4,
            Actions = Enumerable.Range(0, 4).Select(_ => new ActionItem { Type = "Inherit" }).ToList() };
        local.EnsureLayers();
        config.Profiles = [global, local];
        config.EnableGlobalInheritance = true;
        ReviewCheck(local.GetEffectiveAction(1)?.SubActions.Count == 1, "CrossCountInheritance_Setup");
        using (var controller = new GestureController(new MouseHook()))
        {
            controller.TestActiveProfile = local;
            controller.TestSetGestureActive(true, 507);
            controller.TestPrepareSubmenuGeometry();
            controller.TestSetStartPoint(new Point());
            controller.TestProcessMove(new Point(0, 150));
            ReviewCheck(controller.TestSubmenuLock.LockedParentSector == 1 && controller.TestGetHoverState().SubSector == 0,
                "CrossCountInheritance_UsesMappedGlobalSector");
        }

        config.EnableGlobalInheritance = false;
        config.SubmenuStyle = "Fan";
        config.SubWheelTriggerDistance = 95;
        profile = CreateTestProfile(12, "Global");
        profile.Actions[0].SubActions.Add(new ActionItem { Parameter = "Ctrl+C" });
        config.Profiles = [profile];
        using (var controller = new GestureController(new MouseHook()))
        {
            controller.TestActiveProfile = profile;
            controller.TestSetStartPoint(new Point());
            controller.TestSetGestureActive(true, 504);
            controller.TestPrepareSubmenuGeometry();
            // Center petal at ~131 DIP. The right wing covers 31 degrees,
            // beyond Gemini's 12-sector cut-off of 27 degrees.
            controller.TestProcessMove(new Point(131, 0));
            double angle = 31 * Math.PI / 180;
            controller.TestProcessMove(new Point(131 * Math.Cos(angle), 131 * Math.Sin(angle)));
            ReviewCheck(controller.TestGetHoverState().Sector == 0 && controller.TestGetHoverState().SubSector == 2,
                "TwelveSectorFan_VisibleWingRemainsSelectable");
            controller.TestProcessMove(new Point(500, 0));
            ReviewCheck(controller.TestGetHoverState().SubSector == -1, "BeyondVisibleFan_HasNoChildHit");
            controller.TestSetGestureActive(true, 505);
            controller.TestPrepareSubmenuGeometry();
            controller.TestProcessMove(new Point(96, 0));
            ReviewCheck(!controller.TestSubmenuLock.IsLocked && controller.TestGetHoverState().ShowSubTier,
                "FanGapBeforePetal_DoesNotLock");
            angle = 25.6 * Math.PI / 180;
            controller.TestProcessMove(new Point(131 * Math.Cos(angle), 131 * Math.Sin(angle)));
            ReviewCheck(controller.TestSubmenuLock.LockedParentSector == 0 && controller.TestGetHoverState().SubSector == 2,
                "FanGapToVisibleWing_PreservesExpandedParent");
        }
    }

    private static void RunPreviewReviewRegressions()
    {
        Console.WriteLine("\n--- Offscreen production preview and layer paths (no Show) ---");
        var profile = CreateTestProfile(8, "Global");
        var config = new AppConfig
        {
            LockParentSectorOnSubmenu = true, EnableMultiTier = true, AutoCheckUpdate = false,
            WheelRadius = 100, InnerRadius = 40, CoreRadius = 25, CoreDeadzoneRadius = 20,
            SubWheelOuterRadius = 155, SubWheelTriggerDistance = 141,
            Profiles = [profile], IconLayoutMode = "TextOnly"
        };
        ConfigManager.CurrentConfig = config;
        var settings = new SettingsWindow();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        T Field<T>(string name) => (T)typeof(SettingsWindow).GetField(name, flags)!.GetValue(settings)!;
        void Render() => typeof(SettingsWindow).GetMethod("RenderLiveWheelPreview", flags)!.Invoke(settings, null);
        try
        {
            Render();
            double scale = Field<double>("_liveWheelPreviewScale");
            double ringAngle = -11.25 * Math.PI / 180;
            settings.UpdateLiveWheelPreviewHover(new Point(150 + 145 * scale * Math.Cos(ringAngle), 150 + 145 * scale * Math.Sin(ringAngle)));
            ReviewCheck(Field<SubmenuLockTracker>("_previewSubmenuLock").LockedParentSector == 0,
                "Preview_UsesRenderedScaleForLockEntry");
            settings.UpdateLiveWheelPreviewHover(new Point(150, 150 + 145 * scale));
            ReviewCheck(Field<int>("_lastHoveredSector") == 0 && Field<int>("_lastHoveredSubIndex") == -1,
                "Preview_LockedParentSurvivesAngularCrossing");
            settings.UpdateLiveWheelPreviewHover(new Point(150, 150 + 70 * scale));
            ReviewCheck(!Field<SubmenuLockTracker>("_previewSubmenuLock").IsLocked && Field<int>("_lastHoveredSector") == 2,
                "Preview_RetreatUnlocksInSameUnits");

            config.SubmenuStyle = "Fan";
            config.SubWheelTriggerDistance = 95;
            profile.Actions[0].SubActions.Add(new ActionItem { Parameter = "Ctrl+C" });
            profile.Actions[1].SubActions = profile.Actions[0].SubActions.Select(a => a.Clone()).ToList();
            ((RadioButton)settings.FindName("Tier1ConfigSegmentRadio")).IsChecked = true;
            Render();
            var paths = Field<List<Path>>("_previewSubSectorPaths");
            ReviewCheck(Field<List<int>>("_previewSubParentIndices").Contains(1), "Preview_PreparesFanForEveryUsableParent");
            ReviewCheck(paths.All(path => path.Opacity == 0), "Preview_DoesNotExposeEveryFanAtOnce");
            ReviewCheck(paths.All(path => !path.IsHitTestVisible), "Preview_HiddenFansCannotCaptureClicks");
            scale = Field<double>("_liveWheelPreviewScale");
            double r = 131 * scale / Math.Sqrt(2);
            settings.UpdateLiveWheelPreviewHover(new Point(150 + r, 150 + r));
            ReviewCheck(Field<SubmenuLockTracker>("_previewSubmenuLock").LockedParentSector == 1 &&
                Field<int>("_lastHoveredSubIndex") >= 0, "Preview_HoveredFanParentHasVisibleSelectableChildren");
            var parents = Field<List<int>>("_previewSubParentIndices");
            ReviewCheck(paths.Where((_, i) => parents[i] == 1).All(p => p.Opacity > 0) &&
                paths.Where((_, i) => parents[i] != 1).All(p => p.Opacity == 0), "Preview_OnlyHoveredFanIsExposed");
            ReviewCheck(paths.Where((_, i) => parents[i] != 1).All(p => !p.IsHitTestVisible),
                "Preview_NonHoveredFansCannotCaptureClicks");
        }
        finally
        {
            typeof(SettingsWindow).GetField("_isClosingForRelease", flags)!.SetValue(settings, true);
            settings.Close();
        }

        config.SubmenuStyle = "Wheel";
        config.SubWheelTriggerDistance = 141;
        profile.Layers.Add(new WheelLayer { SectorCount = 4,
            Actions = Enumerable.Range(0, 4).Select(i => new ActionItem { Parameter = "Ctrl+C", Name = "Next " + i }).ToList() });
        foreach (string trigger in new[] { "Wheel", "Tab", "CustomKey" })
        {
            config.LayerSwitchTrigger = trigger;
            config.LayerSwitchVkCode = 65;
            profile.ActiveLayerIndex = 0;
            profile.SyncRootPropertiesFromActiveLayer();
            using var controller = new GestureController(new MouseHook());
            controller.TestActiveProfile = profile;
            controller.TestSetStartPoint(new Point(500, 500));
            controller.TestSetGestureActive(true, 600);
            controller.TestPrepareSubmenuGeometry();
            controller.TestProcessMove(new Point(648, 474));
            var radial = new RadialWindow(new Point(500, 500), profile);
            typeof(GestureController).GetField("_radialWindow", flags)!.SetValue(controller, radial);
            if (trigger == "Wheel")
            {
                var method = typeof(GestureController).GetMethod("Hook_OnMouseWheel", flags)!;
                var wheelEvent = new MouseWheelHookEventArgs(-120, 0.0, 0.0);
                method.Invoke(controller, [null, wheelEvent]);
            }
            else
            {
                typeof(GestureController).GetMethod("KeyboardHook_OnKeyDown", flags)!.Invoke(controller,
                    [null, new GlobalKeyEventArgs(trigger == "Tab" ? 9u : 65u, ModifierKeys.None)]);
            }
            PumpDispatcher();
            ReviewCheck(profile.ActiveLayerIndex == 1 && !controller.TestSubmenuLock.IsLocked &&
                controller.TestGetHoverState().SubSector == -1, "LayerSwitch_" + trigger + "_ClearsPreviousLayerLock");
        }
    }

    private static void RunCachedGeometryRegressions()
    {
        Console.WriteLine("\n--- Frozen geometry parity and hot-path allocations ---");
        var config = new AppConfig { LockParentSectorOnSubmenu = true, WheelRadius = 100, InnerRadius = 40,
            SubWheelOuterRadius = 155, SubWheelInnerGap = 7, SubWheelCornerRadius = 6 };
        var profile = CreateTestProfile(8, "Global");
        ConfigManager.CurrentConfig = config;
        var tracker = new SubmenuLockTracker();
        foreach (string shape in new[] { "Original", "Circle", "HexagonHive", "RoundedCapsule", "RoundedRect", "CleanSectors" })
        {
            config.Shape = shape;
            config.SubmenuStyle = "Wheel";
            tracker.PrepareGeometry(config, profile);
            Geometry source = IconHelper.CreateAdvancedSectorGeometry(0, 0, -22.5, 0, 109, 155, shape, 7, 6);
            bool parity = true;
            foreach (double degrees in new[] { -20.0, -11.25, -3.0, 0, 10.0 })
            foreach (double radius in new[] { 100.0, 118, 132, 145, 170 })
            {
                double a = degrees * Math.PI / 180;
                Point point = new(radius * Math.Cos(a), radius * Math.Sin(a));
                parity &= source.FillContains(point) == tracker.ContainsWheelSub(config, point.X, point.Y, 0, 8, 2, 0);
            }
            ReviewCheck(parity, "CachedWheelGeometry_MatchesRenderer_" + shape);
            if (shape == "Circle")
                ReviewCheck(!tracker.ContainsWheelSub(config, 145, 0, 0, 8, 2, 1), "CircleRingGap_IsNotAChild");

            config.SubmenuStyle = "Fan";
            tracker.PrepareGeometry(config, profile);
            var (du, dv) = RadialWindow.GetFanSubOffsetForShape(shape, 2);
            double x = du * 77, y = dv * 77; // orbit=(100+40)/2 + gap 7
            ReviewCheck(tracker.HitTestFan(config, x, y, 0, 8, 3, -1) == 2,
                "CachedFanGeometry_SelectsVisibleWing_" + shape);
            for (int i = 0; i < 1000; i++) tracker.HitTestFan(config, x, y, 0, 8, 3, -1);
            long before = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            int hits = 0;
            for (int i = 0; i < 10000; i++) hits += tracker.HitTestFan(config, x, y, 0, 8, 3, -1);
            double ns = Stopwatch.GetElapsedTime(start).TotalNanoseconds / 10000;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Console.WriteLine($"  {shape} fan geometry: {ns:F0} ns/query; {allocated} allocated bytes / 10,000 queries");
            ReviewCheck(hits == 20000 && allocated == 0, "CachedFanGeometry_NoPerQueryAllocation_" + shape);
        }
    }

    private static void RunKeyboardReleaseRegression()
    {
        var config = new AppConfig { LockParentSectorOnSubmenu = true, WheelRadius = 100, InnerRadius = 40,
            SubWheelOuterRadius = 155, SubWheelTriggerDistance = 141,
            Trigger = new TriggerConfig { TriggerType = "Keyboard", VkCode = 65 } };
        var profile = CreateTestProfile(8, "Global");
        // Even a broken test interception cannot send actual input with these sentinel types.
        foreach (var parent in profile.Actions)
        {
            parent.Type = "ReviewNoOp";
            foreach (var sub in parent.SubActions) sub.Type = "ReviewNoOp";
        }
        config.Profiles = [profile];
        ConfigManager.CurrentConfig = config;
        using var controller = new GestureController(new MouseHook());
        controller.TestActiveProfile = profile;
        controller.TestSetStartPoint(new Point());
        ActionItem? dispatched = null;
        controller.TestActionEnqueued = action => dispatched = action;
        var keyUp = typeof(GestureController).GetMethod("KeyboardHook_OnKeyUp", BindingFlags.Instance | BindingFlags.NonPublic)!;
        controller.TestSetGestureActive(true, 700);
        typeof(GestureController).GetField("_activeTrigger", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(controller, config.Trigger);
        controller.TestPrepareSubmenuGeometry();
        controller.TestProcessMove(new Point(147, -29));
        controller.TestProcessMove(new Point(0, 150));
        keyUp.Invoke(controller, [null, new GlobalKeyEventArgs(65, ModifierKeys.None)]);
        PumpDispatcher();
        ReviewCheck(dispatched == null && !controller.TestSubmenuLock.IsLocked, "KeyboardRelease_EmptySpaceSuppressesParentAndResets");
        controller.TestSetGestureActive(true, 701);
        typeof(GestureController).GetField("_activeTrigger", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(controller, config.Trigger);
        controller.TestPrepareSubmenuGeometry();
        controller.TestProcessMove(new Point(147, -29));
        keyUp.Invoke(controller, [null, new GlobalKeyEventArgs(65, ModifierKeys.None)]);
        PumpDispatcher();
        ReviewCheck(dispatched?.Name.StartsWith("Sub_0_") == true && !controller.TestSubmenuLock.IsLocked,
            "KeyboardRelease_ValidChildUsesSameQueueAndResets");
    }
}
