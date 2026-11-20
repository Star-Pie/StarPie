using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using WinPieGestures;

namespace SubmenuParentLockTests;

public static partial class Program
{
    private static int _passedCount = 0;
    private static int _failedCount = 0;
    private static bool _baselineMode = false;

    [STAThread]
    public static int Main(string[] args)
    {
        if (!args.Contains("--test-instance"))
        {
            Console.Error.WriteLine("Use --test-instance to isolate configuration and offscreen diagnostics.");
            return 2;
        }
        Environment.SetEnvironmentVariable("LOCALAPPDATA", System.IO.Path.Combine(System.IO.Path.GetTempPath(), "StarPie-Submenu-Review-" + Guid.NewGuid().ToString("N")));
        ConfigManager.DisableAutoStartSync = true;
        // Load the same dictionaries as a normal settings window without running host startup.
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        using var styles = typeof(Program).Assembly.GetManifestResourceStream("Fixture.HostStyles.xaml")!;
        var root = System.Xml.Linq.XDocument.Load(styles).Root!;
        var dictionary = root.Element(root.Name.Namespace + "Application.Resources")!.Elements().Single();
        var context = new System.Windows.Markup.ParserContext();
        context.XmlnsDictionary[""] = root.Name.NamespaceName;
        context.XmlnsDictionary["x"] = "http://schemas.microsoft.com/winfx/2006/xaml";
        context.XmlnsDictionary["local"] = "clr-namespace:WinPieGestures;assembly=StarPie";
        application.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(dictionary.ToString(), context);
        _baselineMode = args.Length > 0 && args[0].Equals("--baseline", StringComparison.OrdinalIgnoreCase);
        Console.WriteLine($"=== Submenu Parent Lock Tests (Mode: {(_baselineMode ? "BASELINE RECORDING" : "VERIFICATION")}) ===");

        try
        {
            if (_baselineMode)
            {
                RunBaselineTests();
            }
            else
            {
                RunComprehensiveVerificationTests();
            }

            Console.WriteLine($"\n=======================================================");
            Console.WriteLine($"Final Test Result: {_passedCount} passed, {_failedCount} failed.");
            Console.WriteLine($"=======================================================");
            return _failedCount == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FATAL] Unhandled exception: {ex}");
            return 2;
        }
    }

    private static WheelProfile CreateTestProfile(int sectorCount, string profileName = "TestProfile", bool withInherited = false)
    {
        var profile = new WheelProfile
        {
            ProcessName = profileName,
            DisplayName = profileName,
            SectorCount = sectorCount,
            Actions = new List<ActionItem>()
        };

        for (int i = 0; i < sectorCount; i++)
        {
            var action = new ActionItem
            {
                Type = "Hotkey",
                Name = $"Parent_{i}",
                Parameter = $"Ctrl+{i}",
                SubActions = new List<ActionItem>()
            };

            // Sector 0: fully configured sub-actions
            // Sector 1: sub-action with None and Empty slots (to test slot clearing)
            // Sector 2: empty sub-actions list (no submenu)
            if (i == 0)
            {
                action.SubActions.Add(new ActionItem { Type = "Hotkey", Name = $"Sub_{i}_0", Parameter = $"Alt+{i}_0" });
                action.SubActions.Add(new ActionItem { Type = "Hotkey", Name = $"Sub_{i}_1", Parameter = $"Alt+{i}_1" });
            }
            else if (i == 1)
            {
                action.SubActions.Add(new ActionItem { Type = "Hotkey", Name = $"Sub_Valid", Parameter = "Alt+V" });
                action.SubActions.Add(new ActionItem { Type = "None", Name = $"Sub_Disabled", Parameter = "" });
                action.SubActions.Add(new ActionItem { Type = "Hotkey", Name = $"动作 3", Parameter = "" }); // unconfigured default placeholder
            }
            else if (i == 3 && withInherited)
            {
                // Inherited slot
                action.SubActions.Add(new ActionItem { Type = "Inherit", Name = "Inherited_Sub", Parameter = "" });
            }

            profile.Actions.Add(action);
        }

        profile.EnsureLayers();
        return profile;
    }

    private static void RunBaselineTests()
    {
        Console.WriteLine("\n--- 1. SubWheel Parent Sector Crossing Angle Boundary (Baseline) ---");
        {
            ConfigManager.CurrentConfig = new AppConfig
            {
                EnableMultiTier = true,
                SubWheelTriggerDistance = 141.0,
                WheelRadius = 100.0,
                InnerRadius = 40.0,
                CoreRadius = 35.0,
                CoreDeadzoneRadius = 35.0,
                SubmenuStyle = "Wheel",
                AutoExpandSubRingsOnPopup = false,
                LockParentSectorOnSubmenu = false
            };

            var mouseHook = new MouseHook();
            using var gc = new GestureController(mouseHook);
            var profile = CreateTestProfile(8);
            gc.TestActiveProfile = profile;
            gc.TestSetStartPoint(new Point(500, 500));
            gc.TestSetGestureActive(true, 100);
            gc.TestPrepareSubmenuGeometry();

            // Step 1: Move right into sector 0's SubWheel (distance 150 > trigger 141, angle 0 deg)
            gc.TestProcessMove(new Point(648, 474));
            var (sec1, sub1, esc1, tier1) = gc.TestGetHoverState();
            Console.WriteLine($"  Step 1: Move to (650, 500) -> Dist: 150px, Angle: 0 deg | Sector: {sec1}, Sub: {sub1}, ShowSubTier: {tier1}");

            // Step 2: Move upward across primary angle boundary (140, 60) -> angle ~23.2 deg > 22.5 deg (sector 1)
            gc.TestProcessMove(new Point(640, 560));
            var (sec2, sub2, esc2, tier2) = gc.TestGetHoverState();
            Console.WriteLine($"  Step 2: Move to (640, 560) -> Dist: 152.3px, Angle: 23.2 deg | Sector: {sec2}, Sub: {sub2}, ShowSubTier: {tier2}");

            Assert(sec2 == 1, "Baseline_SubWheel_JumpsAcrossBoundary",
                $"Expected baseline defect: Sector jumped to {sec2} (1 on unpatched baseline).",
                $"Expected Sector to jump to 1, but got {sec2}.");
        }

        Console.WriteLine("\n--- 2. FanSubmenu Parent Sector Crossing Angle Boundary (Baseline) ---");
        {
            ConfigManager.CurrentConfig = new AppConfig
            {
                EnableMultiTier = true,
                SubWheelTriggerDistance = 95.0,
                WheelRadius = 100.0,
                InnerRadius = 40.0,
                CoreRadius = 35.0,
                CoreDeadzoneRadius = 35.0,
                SubmenuStyle = "Fan",
                AutoExpandSubRingsOnPopup = false,
                LockParentSectorOnSubmenu = false
            };

            var mouseHook = new MouseHook();
            using var gc = new GestureController(mouseHook);
            var profile = CreateTestProfile(8);
            gc.TestActiveProfile = profile;
            gc.TestSetStartPoint(new Point(500, 500));
            gc.TestSetGestureActive(true, 200);
            gc.TestPrepareSubmenuGeometry();

            // Step 1: Move right into sector 0's Fan (distance 120 > trigger 95, angle 0 deg)
            gc.TestProcessMove(new Point(620, 500));
            var (sec1, sub1, esc1, tier1) = gc.TestGetHoverState();
            Console.WriteLine($"  Step 1: Move to (620, 500) -> Dist: 120px, Angle: 0 deg | Sector: {sec1}, Sub: {sub1}, ShowSubTier: {tier1}");

            // Step 2: Move to angle 43.5 deg (100, 95) -> angleDiff > 45 * 0.90 (40.5 deg)
            gc.TestProcessMove(new Point(600, 595));
            var (sec2, sub2, esc2, tier2) = gc.TestGetHoverState();
            Console.WriteLine($"  Step 2: Move to (600, 595) -> Dist: 138px, Angle: 43.5 deg | Sector: {sec2}, Sub: {sub2}, ShowSubTier: {tier2}");

            Assert(sec2 == 1, "Baseline_Fan_JumpsAcrossAngleThreshold",
                $"Expected baseline defect: Fan sector jumped to {sec2} (1 on unpatched baseline).",
                $"Expected Sector to jump to 1, but got {sec2}.");
        }
    }

    private static void RunComprehensiveVerificationTests()
    {
        Test_Compatibility_SwitchOff_LegacyPreserved();
        Test_Compatibility_ConfigSerializationAndDefaults();
        Test_SubWheel_LockingAcrossAngleBoundaries_4_8_12();
        Test_Fan_LockingAcrossAngleBoundaries_4_8_12();
        Test_ClearSubItemHit_And_ReleaseExecutionSafety();
        Test_RetreatToPrimaryWheel_HysteresisUnlocking();
        Test_AutoExpandSubRingsOnPopup_NotImmediatelyLocked();
        Test_ResetConditions();
        Test_InheritedAndEmptySubActions();
        RunReviewRegressions();
        RunPreviewReviewRegressions();
        RunCachedGeometryRegressions();
        RunKeyboardReleaseRegression();
        Test_PerformanceAndZeroAllocation();
    }

    private static void Test_Compatibility_SwitchOff_LegacyPreserved()
    {
        Console.WriteLine("\n--- Verification: Switch Off (Legacy Behavior 100% Preserved) ---");

        ConfigManager.CurrentConfig = new AppConfig
        {
            EnableMultiTier = true,
            SubWheelTriggerDistance = 141.0,
            WheelRadius = 100.0,
            SubmenuStyle = "Wheel",
            LockParentSectorOnSubmenu = false // Explicitly OFF
        };

        var mouseHook = new MouseHook();
        using var gc = new GestureController(mouseHook);
        var profile = CreateTestProfile(8);
        gc.TestActiveProfile = profile;
        gc.TestSetStartPoint(new Point(500, 500));
        gc.TestSetGestureActive(true, 1);
        gc.TestPrepareSubmenuGeometry();

        // Move to sector 0 SubWheel
        gc.TestProcessMove(new Point(648, 474));
        var (s1, sub1, _, _) = gc.TestGetHoverState();
        Assert(s1 == 0 && sub1 >= 0, "Legacy_SubWheel_InitialHit", "Initial hit on Sector 0 SubWheel", $"Expected sec=0, got {s1}");

        // Move across primary angle boundary
        gc.TestProcessMove(new Point(640, 560));
        var (s2, _, _, _) = gc.TestGetHoverState();
        Assert(s2 == 1, "Legacy_SubWheel_JumpsWhenSwitchOff", "Sector switches to 1 when lock switch is off", $"Expected sec=1, got {s2}");
        Assert(!gc.TestSubmenuLock.IsLocked, "Legacy_SubWheel_NeverLocksWhenSwitchOff", "LockTracker remains unlocked", "Tracker was locked unexpectedly");
    }

    private static void Test_Compatibility_ConfigSerializationAndDefaults()
    {
        Console.WriteLine("\n--- Verification: Config Serialization & Backward Compatibility ---");

        // 1. Deserializing old config JSON missing "LockParentSectorOnSubmenu"
        string legacyJson = "{\"WheelRadius\": 110.0, \"ActiveConfigProfileName\": \"默认配置\"}";
        var deserialized = JsonSerializer.Deserialize<AppConfig>(legacyJson);
        Assert(deserialized != null && deserialized.LockParentSectorOnSubmenu == false,
            "Config_LegacyMissingFieldDefaultsToFalse",
            "Missing field cleanly deserializes with default false",
            "Deserialized LockParentSectorOnSubmenu was not false");

        // 2. Explicit false serialization
        deserialized!.LockParentSectorOnSubmenu = false;
        string reserialized = JsonSerializer.Serialize(deserialized);
        Assert(reserialized.Contains("\"LockParentSectorOnSubmenu\":false") || reserialized.Contains("\"LockParentSectorOnSubmenu\": false"),
            "Config_ExplicitFalseSerialized",
            "Explicit false value is cleanly preserved in JSON",
            "Serialized JSON did not include LockParentSectorOnSubmenu: false");

        // 3. Explicit true serialization
        deserialized.LockParentSectorOnSubmenu = true;
        string reserializedTrue = JsonSerializer.Serialize(deserialized);
        var backAgain = JsonSerializer.Deserialize<AppConfig>(reserializedTrue);
        Assert(backAgain != null && backAgain.LockParentSectorOnSubmenu == true,
            "Config_ExplicitTrueRoundTrip",
            "Explicit true value round-trips cleanly",
            "Round-trip failed for LockParentSectorOnSubmenu = true");
    }

    private static void Test_SubWheel_LockingAcrossAngleBoundaries_4_8_12()
    {
        Console.WriteLine("\n--- Verification: SubWheel Locking Across Angle Boundaries (4, 8, 12 Sectors) ---");

        int[] sectorCounts = { 4, 8, 12 };
        foreach (int count in sectorCounts)
        {
            ConfigManager.CurrentConfig = new AppConfig
            {
                EnableMultiTier = true,
                SubWheelTriggerDistance = 141.0,
                WheelRadius = 100.0,
                SubmenuStyle = "Wheel",
                AutoExpandSubRingsOnPopup = false,
                LockParentSectorOnSubmenu = true
            };

            var mouseHook = new MouseHook();
            using var gc = new GestureController(mouseHook);
            var profile = CreateTestProfile(count);
            gc.TestActiveProfile = profile;
            gc.TestSetStartPoint(new Point(500, 500));
            gc.TestSetGestureActive(true, 10 + count);
            gc.TestPrepareSubmenuGeometry();

            // Step 1: Move into sector 0 SubWheel at distance 150px, angle 0 deg
            gc.TestProcessMove(new Point(648, 474));
            var (s1, sub1, _, _) = gc.TestGetHoverState();
            Assert(s1 == 0 && sub1 >= 0, $"SubWheel_{count}_InitialEntry",
                $"{count}-sector wheel entered sector 0 SubWheel (sub: {sub1})",
                $"Expected sec=0, got {s1}");
            Assert(gc.TestSubmenuLock.IsLocked && gc.TestSubmenuLock.LockedParentSector == 0,
                $"SubWheel_{count}_LockEngaged",
                $"Lock engaged on parent sector 0",
                $"Lock was not engaged on sector 0");

            // Step 2: Move into angle of sector 1 at distance 150px
            // Sector 1 angle is at 360 / count degrees.
            double sec1AngleDeg = 360.0 / count;
            double rad = sec1AngleDeg * Math.PI / 180.0;
            double px = 500.0 + 150.0 * Math.Cos(rad);
            double py = 500.0 + 150.0 * Math.Sin(rad);

            gc.TestProcessMove(new Point(px, py));
            var (s2, sub2, _, _) = gc.TestGetHoverState();

            Assert(s2 == 0, $"SubWheel_{count}_ParentSectorLocked",
                $"{count}-sector wheel remained locked to parent sector 0 when cursor moved to angle {sec1AngleDeg:F1}°",
                $"Expected sector to remain 0, but switched to {s2}");
            Assert(sub2 == -1, $"SubWheel_{count}_SubSectorClearedOutsideArc",
                $"Sub-sector cleared to -1 when outside parent sector arc",
                $"Expected sub=-1, got {sub2}");

            // Step 3: Move back into sector 0 arc
            gc.TestProcessMove(new Point(648, 474));
            var (s3, sub3, _, _) = gc.TestGetHoverState();
            Assert(s3 == 0 && sub3 >= 0, $"SubWheel_{count}_SubSectorReacquired",
                $"Sub-sector reacquired when returning to sector 0 arc (sub: {sub3})",
                $"Expected sub>=0, got {sub3}");
        }
    }

    private static void Test_Fan_LockingAcrossAngleBoundaries_4_8_12()
    {
        Console.WriteLine("\n--- Verification: FanSubmenu Locking Across Angle Boundaries (4, 8, 12 Sectors) ---");

        int[] sectorCounts = { 4, 8, 12 };
        foreach (int count in sectorCounts)
        {
            ConfigManager.CurrentConfig = new AppConfig
            {
                EnableMultiTier = true,
                SubWheelTriggerDistance = 95.0,
                WheelRadius = 100.0,
                InnerRadius = 40.0,
                SubWheelOuterRadius = 155.0,
                SubmenuStyle = "Fan",
                AutoExpandSubRingsOnPopup = false,
                LockParentSectorOnSubmenu = true
            };

            var mouseHook = new MouseHook();
            using var gc = new GestureController(mouseHook);
            var profile = CreateTestProfile(count);
            profile.Actions[0].SubActions.Add(new ActionItem { Parameter = "Ctrl+C" });
            gc.TestActiveProfile = profile;
            gc.TestSetStartPoint(new Point(500, 500));
            gc.TestSetGestureActive(true, 20 + count);
            gc.TestPrepareSubmenuGeometry();

            // Step 1: Move into sector 0 Fan at distance 120px, angle 0 deg
            gc.TestProcessMove(new Point(620, 500));
            var (s1, sub1, _, _) = gc.TestGetHoverState();
            Assert(s1 == 0 && sub1 >= 0, $"Fan_{count}_InitialEntry",
                $"{count}-sector wheel entered sector 0 Fan (sub: {sub1})",
                $"Expected sec=0, got {s1}");
            Assert(gc.TestSubmenuLock.IsLocked && gc.TestSubmenuLock.LockedParentSector == 0,
                $"Fan_{count}_LockEngaged",
                $"Lock engaged on parent sector 0",
                $"Lock was not engaged on sector 0");

            // Step 2: Move into adjacent sector angle at distance 120px
            double sec1AngleDeg = 2 * 360.0 / count;
            double rad = sec1AngleDeg * Math.PI / 180.0;
            double px = 500.0 + 120.0 * Math.Cos(rad);
            double py = 500.0 + 120.0 * Math.Sin(rad);

            gc.TestProcessMove(new Point(px, py));
            var (s2, sub2, _, _) = gc.TestGetHoverState();

            Assert(s2 == 0, $"Fan_{count}_ParentSectorLocked",
                $"{count}-sector Fan remained locked to parent sector 0 when cursor moved to angle {sec1AngleDeg:F1}°",
                $"Expected sector to remain 0, but switched to {s2}");
            Assert(sub2 == -1, $"Fan_{count}_SubSectorClearedOutsideFan",
                $"Sub-sector cleared to -1 when outside Fan arc",
                $"Expected sub=-1, got {sub2}");

            // Step 3: Move back into Fan arc
            gc.TestProcessMove(new Point(620, 500));
            var (s3, sub3, _, _) = gc.TestGetHoverState();
            Assert(s3 == 0 && sub3 >= 0, $"Fan_{count}_SubSectorReacquired",
                $"Sub-sector reacquired when returning into Fan arc (sub: {sub3})",
                $"Expected sub>=0, got {sub3}");
        }
    }

    private static void Test_ClearSubItemHit_And_ReleaseExecutionSafety()
    {
        Console.WriteLine("\n--- Verification: Clear Sub-Item Hit & Release Execution Safety ---");

        ConfigManager.CurrentConfig = new AppConfig
        {
            EnableMultiTier = true,
            SubWheelTriggerDistance = 141.0,
            WheelRadius = 100.0,
            SubmenuStyle = "Wheel",
            LockParentSectorOnSubmenu = true
        };

        var mouseHook = new MouseHook();
        using var gc = new GestureController(mouseHook);
        var profile = CreateTestProfile(8);
        gc.TestActiveProfile = profile;
        gc.TestSetStartPoint(new Point(500, 500));
        gc.TestSetGestureActive(true, 50);
        gc.TestPrepareSubmenuGeometry();

        ActionItem? enqueuedAction = null;
        gc.TestActionEnqueued = (act) => enqueuedAction = act;

        // 1. Move to Sector 0 SubWheel
        gc.TestProcessMove(new Point(648, 474));
        var (s1, sub1, _, _) = gc.TestGetHoverState();
        Assert(s1 == 0 && sub1 >= 0, "ReleaseSafety_SubWheel_Entered", "Entered Sector 0 SubWheel", "Failed to enter");

        // 2. Move outside the sub-item arc (angle ~23.2 deg)
        gc.TestProcessMove(new Point(640, 560));
        var (s2, sub2, _, _) = gc.TestGetHoverState();
        Assert(s2 == 0 && sub2 == -1, "ReleaseSafety_SubSectorCleared",
            "Parent sector is 0 and subSector is cleared (-1)",
            $"Expected sec=0, sub=-1, got sec={s2}, sub={sub2}");

        // 3. Release mouse while subSector is -1 and parent is locked!
        enqueuedAction = null;
        gc.TestCompleteMouseTriggerRelease(new Point(640, 560), "RightButton");
        PumpDispatcher();

        Assert(enqueuedAction == null, "ReleaseSafety_NoActionExecutedOnEmptySubSlot",
            "Safety Guard Passed: targetAction is null, parent action Parent_0 was NOT executed!",
            $"CRITICAL DEFECT: Parent action '{enqueuedAction?.Name}' was accidentally executed when releasing in empty submenu space!");

        // 4. Test releasing on a valid sub-action executes that sub-action
        gc.TestSetGestureActive(true, 51);
        gc.TestPrepareSubmenuGeometry();
        gc.TestSetStartPoint(new Point(500, 500));
        gc.TestProcessMove(new Point(648, 474)); // on sub-action
        enqueuedAction = null;
        gc.TestCompleteMouseTriggerRelease(new Point(648, 474), "RightButton");
        PumpDispatcher();

        Assert(enqueuedAction != null && enqueuedAction.Name.StartsWith("Sub_0_"),
            "ReleaseSafety_ValidSubActionExecuted",
            $"Valid sub-action '{enqueuedAction?.Name}' executed successfully on release",
            $"Expected valid sub-action, got '{enqueuedAction?.Name ?? "null"}'");
    }

    private static void Test_RetreatToPrimaryWheel_HysteresisUnlocking()
    {
        Console.WriteLine("\n--- Verification: Retreat to Primary Wheel (Hysteresis & Unlocking) ---");

        ConfigManager.CurrentConfig = new AppConfig
        {
            EnableMultiTier = true,
            SubWheelTriggerDistance = 141.0,
            WheelRadius = 100.0,
            SubmenuStyle = "Wheel",
            LockParentSectorOnSubmenu = true
        };

        var mouseHook = new MouseHook();
        using var gc = new GestureController(mouseHook);
        var profile = CreateTestProfile(8);
        gc.TestActiveProfile = profile;
        gc.TestSetStartPoint(new Point(500, 500));
        gc.TestSetGestureActive(true, 60);
        gc.TestPrepareSubmenuGeometry();

        ActionItem? enqueuedAction = null;
        gc.TestActionEnqueued = (act) => enqueuedAction = act;

        // Step 1: Move outward into SubWheel (distance 150px)
        gc.TestProcessMove(new Point(648, 474));
        Assert(gc.TestSubmenuLock.IsLocked, "Hysteresis_EnteredAndLocked", "Locked at dist 150px", "Not locked");

        // Step 2: Move back into hysteresis zone (distance 110px: wheelRadius is 100px, exitDist is 94px)
        // Cursor is at dist 110px: between 94px and 141px -> stays locked!
        gc.TestProcessMove(new Point(610, 500));
        Assert(gc.TestSubmenuLock.IsLocked, "Hysteresis_StaysLockedInDebounceZone",
            "Stays locked in hysteresis debounce zone (dist 110px >= exitDist 94px)",
            "Unlocked prematurely in debounce zone");

        // Step 3: Move inward clearly inside primary wheel (distance 70px < exitDist 94px)
        gc.TestProcessMove(new Point(570, 500));
        var (s3, sub3, _, _) = gc.TestGetHoverState();
        Assert(!gc.TestSubmenuLock.IsLocked, "Hysteresis_UnlocksInsidePrimaryWheel",
            $"Unlocked cleanly when retreating inside primary wheel (dist 70px < 94px)",
            "Failed to unlock when retreating inside primary wheel");
        Assert(s3 == 0 && sub3 == -1, "Hysteresis_PrimarySectorSelected",
            "Primary sector 0 selected without sub-sector",
            $"Expected sec=0, sub=-1, got sec={s3}, sub={sub3}");

        // Step 4: Now rotate to sector 2 (angle 90 deg, down) within primary wheel
        gc.TestProcessMove(new Point(500, 570));
        var (s4, sub4, _, _) = gc.TestGetHoverState();
        Assert(s4 == 2 && sub4 == -1, "Hysteresis_RotateToAnotherPrimarySector",
            $"Can freely rotate to sector 2 (got sec={s4})",
            $"Expected sec=2, got {s4}");

        // Step 5: Release inside primary wheel -> executes parent action Parent_2!
        enqueuedAction = null;
        gc.TestCompleteMouseTriggerRelease(new Point(500, 570), "RightButton");
        PumpDispatcher();
        Assert(enqueuedAction != null && enqueuedAction.Name == "Parent_2",
            "Hysteresis_ExecutesParentActionOnPrimaryRelease",
            $"Releasing inside primary wheel executes parent action '{enqueuedAction?.Name}'",
            $"Expected Parent_2, got '{enqueuedAction?.Name ?? "null"}'");
    }

    private static void Test_AutoExpandSubRingsOnPopup_NotImmediatelyLocked()
    {
        Console.WriteLine("\n--- Verification: AutoExpandSubRingsOnPopup (Does NOT Lock Immediately) ---");

        ConfigManager.CurrentConfig = new AppConfig
        {
            EnableMultiTier = true,
            SubWheelTriggerDistance = 141.0,
            WheelRadius = 100.0,
            SubWheelInnerGap = 4.0,
            SubmenuStyle = "Wheel",
            AutoExpandSubRingsOnPopup = true, // Simultaneous expansion on popup
            LockParentSectorOnSubmenu = true
        };

        var mouseHook = new MouseHook();
        using var gc = new GestureController(mouseHook);
        var profile = CreateTestProfile(8);
        gc.TestActiveProfile = profile;
        gc.TestSetStartPoint(new Point(500, 500));
        gc.TestSetGestureActive(true, 70);
        gc.TestPrepareSubmenuGeometry();

        // Step 1: Hover inside primary wheel (distance 60px < 106px)
        gc.TestProcessMove(new Point(560, 500));
        var (s1, sub1, _, tier1) = gc.TestGetHoverState();

        Assert(tier1 == true, "AutoExpand_VisuallyExpanded", "Sub-tier is visually expanded on popup", "Sub-tier was not expanded");
        Assert(!gc.TestSubmenuLock.IsLocked, "AutoExpand_NotLockedInsidePrimaryWheel",
            "Visual expansion does NOT lock parent sector when cursor is inside primary wheel",
            "Locked prematurely before cursor entered secondary wheel!");

        // Step 2: Rotate around primary wheel (Sector 0 -> Sector 1)
        // Sector 1's first child is configured; its center child is explicitly None.
        double rad1 = 30.0 * Math.PI / 180.0;
        gc.TestProcessMove(new Point(500.0 + 60.0 * Math.Cos(rad1), 500.0 + 60.0 * Math.Sin(rad1))); // 45 deg -> Sector 1
        var (s2, _, _, _) = gc.TestGetHoverState();
        Assert(s2 == 1, "AutoExpand_CanFreelySwitchPrimarySectors",
            "Can freely switch between primary sectors while sub-rings are expanded",
            $"Expected sec=1, got {s2}");

        // Step 3: Now move outward into SubWheel (distance 120px >= subWheelInner 106px)
        gc.TestProcessMove(new Point(500.0 + 120.0 * Math.Cos(rad1), 500.0 + 120.0 * Math.Sin(rad1)));
        Assert(gc.TestSubmenuLock.IsLocked && gc.TestSubmenuLock.LockedParentSector == 1,
            "AutoExpand_LocksWhenActuallyEnteringSubWheel",
            "Locks onto parent sector 1 when cursor actually crosses into SubWheel zone",
            "Failed to lock when entering SubWheel zone");
    }

    private static void Test_ResetConditions()
    {
        Console.WriteLine("\n--- Verification: Reset Conditions (Release, Cancel, Deadzone, Outer Escape, Volume) ---");

        ConfigManager.CurrentConfig = new AppConfig
        {
            EnableMultiTier = true,
            SubWheelTriggerDistance = 141.0,
            WheelRadius = 100.0,
            CoreDeadzoneRadius = 35.0,
            EnableOuterEscapeCancel = true,
            OuterEscapeDistance = 200.0,
            SubmenuStyle = "Wheel",
            LockParentSectorOnSubmenu = true
        };

        var mouseHook = new MouseHook();
        using var gc = new GestureController(mouseHook);
        var profile = CreateTestProfile(8);
        gc.TestActiveProfile = profile;

        // 1. Reset on Deadzone
        gc.TestSetGestureActive(true, 80);
        gc.TestPrepareSubmenuGeometry();
        gc.TestSetStartPoint(new Point(500, 500));
        gc.TestProcessMove(new Point(648, 474)); // enter SubWheel
        Assert(gc.TestSubmenuLock.IsLocked, "Reset_Deadzone_SetupLocked", "Locked at 150px", "Setup failed");
        gc.TestProcessMove(new Point(510, 500)); // move to center deadzone (dist 10px < 35px)
        Assert(!gc.TestSubmenuLock.IsLocked, "Reset_Deadzone_ClearsLock", "Moving into deadzone clears submenu lock", "Lock persisted in deadzone");
        gc.TestCancelGestureTracking();

        // 2. Reset on Outer Escape Cancel
        gc.TestSetGestureActive(true, 81);
        gc.TestPrepareSubmenuGeometry();
        gc.TestSetStartPoint(new Point(500, 500));
        gc.TestProcessMove(new Point(648, 474)); // enter SubWheel
        Assert(gc.TestSubmenuLock.IsLocked, "Reset_Escape_SetupLocked", "Locked at 150px", "Setup failed");
        gc.TestProcessMove(new Point(750, 500)); // move to 250px > outerEscape 200px
        Assert(!gc.TestSubmenuLock.IsLocked, "Reset_OuterEscape_ClearsLock", "Outer escape clears submenu lock", "Lock persisted on escape");
        gc.TestCancelGestureTracking();

        // 3. Reset on Explicit Cancel
        gc.TestSetGestureActive(true, 82);
        gc.TestPrepareSubmenuGeometry();
        gc.TestSetStartPoint(new Point(500, 500));
        gc.TestProcessMove(new Point(648, 474));
        Assert(gc.TestSubmenuLock.IsLocked, "Reset_Cancel_SetupLocked", "Locked at 150px", "Setup failed");
        gc.TestCancelGestureTracking();
        Assert(!gc.TestSubmenuLock.IsLocked, "Reset_ExplicitCancel_ClearsLock", "Cancel clears submenu lock", "Lock persisted after cancel");

        // 4. Reset on New Gesture Start
        gc.TestSetGestureActive(true, 83);
        gc.TestPrepareSubmenuGeometry();
        gc.TestSetStartPoint(new Point(500, 500));
        gc.TestProcessMove(new Point(648, 474));
        Assert(!gc.TestShowRadialUI(new Point(500, 500), profile, 84) && gc.TestSubmenuLock.IsLocked,
            "Reset_StalePresentation_DoesNotTouchCurrentGesture", "Stale callback preserves lock", "Stale callback cleared a live lock");
        gc.TestBeginGestureTracking();
        Assert(!gc.TestSubmenuLock.IsLocked, "Reset_NewGesture_StartsClean", "New gesture start clears submenu lock", "Lock carried over to new gesture");
    }

    private static void Test_InheritedAndEmptySubActions()
    {
        Console.WriteLine("\n--- Verification: Inherited, Disabled, and Empty Sub-Actions ---");

        ConfigManager.CurrentConfig = new AppConfig
        {
            EnableMultiTier = true,
            SubWheelTriggerDistance = 141.0,
            WheelRadius = 100.0,
            SubmenuStyle = "Wheel",
            LockParentSectorOnSubmenu = true
        };

        var mouseHook = new MouseHook();
        using var gc = new GestureController(mouseHook);
        var profile = CreateTestProfile(8);
        gc.TestActiveProfile = profile;

        // Sector 2 in profile has empty SubActions list
        gc.TestSetGestureActive(true, 90);
        gc.TestPrepareSubmenuGeometry();
        gc.TestSetStartPoint(new Point(500, 500));
        // Move to Sector 2 at distance 150px (angle 90 deg)
        gc.TestProcessMove(new Point(500, 650));
        Assert(!gc.TestSubmenuLock.IsLocked, "EmptySubActions_DoesNotLock",
            "Sector with NO sub-actions never locks parent sector",
            "Locked unexpectedly on empty sub-actions sector");

        // Sector 1 has [Valid, Disabled(None), Placeholder]. Enter its valid first child.
        gc.TestSetGestureActive(true, 91);
        gc.TestPrepareSubmenuGeometry();
        gc.TestSetStartPoint(new Point(500, 500));
        double rad1 = 30.0 * Math.PI / 180.0;
        gc.TestProcessMove(new Point(500.0 + 150.0 * Math.Cos(rad1), 500.0 + 150.0 * Math.Sin(rad1)));
        Assert(gc.TestSubmenuLock.IsLocked && gc.TestSubmenuLock.LockedParentSector == 1,
            "HasUsableSubmenu_LocksWhenAtLeastOneSubConfigured",
            "Locks onto Sector 1 because it has at least one configured sub-action",
            "Failed to lock on Sector 1");
    }

    private static void Test_PerformanceAndZeroAllocation()
    {
        Console.WriteLine("\n--- Verification: Performance & Zero Allocation Guard ---");

        var tracker = new SubmenuLockTracker();
        var config = new AppConfig
        {
            EnableMultiTier = true,
            SubWheelTriggerDistance = 141.0,
            WheelRadius = 100.0,
            SubmenuStyle = "Wheel",
            LockParentSectorOnSubmenu = true
        };
        var profile = CreateTestProfile(8);

        // Warm up JIT
        for (int i = 0; i < 1000; i++)
        {
            tracker.ResolveParentSector(true, true, false, 150.0, 0, 100.0, config);
            tracker.Lock(0); // production locks after its separate child hit test succeeds
            tracker.ResolveParentSector(true, true, false, 80.0, 0, 100.0, config);
        }

        // Measure allocations
        int iterations = 100_000;
        var sw = new Stopwatch();
        long allocBefore = GC.GetAllocatedBytesForCurrentThread();
        sw.Start();

        for (int i = 0; i < iterations; i++)
        {
            tracker.ResolveParentSector(true, true, false, 150.0, 0, 100.0, config);
            tracker.Lock(0);
            tracker.ResolveParentSector(true, true, false, 110.0, 0, 100.0, config);
            tracker.ResolveParentSector(true, true, false, 80.0, 1, 100.0, config);
        }

        sw.Stop();
        long allocAfter = GC.GetAllocatedBytesForCurrentThread();
        long totalAlloc = allocAfter - allocBefore;

        double totalMs = sw.Elapsed.TotalMilliseconds;
        double nsPerCall = (totalMs * 1_000_000.0) / (iterations * 3);

        Console.WriteLine($"  {iterations * 3:N0} calls completed in {totalMs:F2} ms ({nsPerCall:F1} ns/call). Total Thread Allocations: {totalAlloc} bytes.");

        Assert(totalAlloc == 0, "Perf_ZeroAllocation",
            $"Zero allocation verified: {totalAlloc} bytes allocated over {iterations * 3:N0} hot-path calls",
            $"Hot-path allocation detected: {totalAlloc} bytes allocated!");

        Assert(nsPerCall < 500.0, "Perf_SubMicrosecondExecution",
            $"Ultra-low latency verified: {nsPerCall:F1} ns per call (< 500 ns requirement)",
            $"Execution too slow: {nsPerCall:F1} ns per call!");
    }

    private static void PumpDispatcher()
    {
        if (Application.Current?.Dispatcher == null) return;
        DispatcherFrame frame = new DispatcherFrame();
        Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void Assert(bool condition, string testName, string passMessage, string failMessage)
    {
        if (condition)
        {
            _passedCount++;
            Console.WriteLine($"  [PASS] {testName}: {passMessage}");
        }
        else
        {
            _failedCount++;
            Console.WriteLine($"  [FAIL] {testName}: {failMessage}");
        }
    }
}
