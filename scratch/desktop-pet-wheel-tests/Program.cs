using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using StarPie.Plugin;
using WinPieGestures.Plugins;

namespace DesktopPetWheelTests;

internal static class Program
{
    private static int _passedCount = 0;
    private static int _failedCount = 0;

    [STAThread]
    private static int Main(string[] args)
    {
        string originalLocalAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "";
        string tempSandboxDir = Path.Combine(Path.GetTempPath(), $"StarPie_WheelTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempSandboxDir);
        Environment.SetEnvironmentVariable("LOCALAPPDATA", tempSandboxDir);

        try
        {
            Console.WriteLine("=================================================");
            Console.WriteLine("  StarPie - IHostWheelSessionService Host Tests  ");
            Console.WriteLine("=================================================");

            TestPluginWheelServiceImplementsSessionService();
            TestCapabilityGateDeniedWithoutWheelCapability();
            TestCapabilityGateAllowedWithWheelCapability();
            TestHeadlessModeReturnsNull();
            TestInvalidCoordinatesReturnNull();
            TestTokenDisposalDetachesCallbacks();
            TestPluginRevokeDetachesCallbacks();
            TestAngleAndSectorFormulas();
            TestSessionEventArgTypes();
            TestTerminalStateAtMostOnce();
            TestTokenDisposeDoesNotEmitClosed();
            TestFrameCoalescingState();
            TestLateSelectionAfterTerminationIgnored();

            // 新增真实生产接缝回归门控
            TestOwnerLeaseHeldDuringCallbackAndDrainedOnReturn();
            TestGenerationSeparationBlocksOldCallbacksOnSameIdReplacement();
            TestStoppingAndUnregisteredCallerRejected();
            TestRevocationDetachesNonCurrentAndQueuedSessions();
            TestRevokingOneCallerDoesNotAffectOtherCallers();
            TestTokenDisposeDoesNotDismissWheel();
            TestRealLoadGenerationMatchesActiveInstanceAndAcceptsCallback();
            TestRealReloadNewGenerationAcceptedAndOldServiceGenerationBlocked();

            Console.WriteLine("-------------------------------------------------");
            Console.WriteLine($"Result: {_passedCount} passed, {_failedCount} failed.");
            Console.WriteLine("=================================================");

            return _failedCount == 0 ? 0 : 1;
        }
        finally
        {
            Environment.SetEnvironmentVariable("LOCALAPPDATA", originalLocalAppData);
            try
            {
                if (Directory.Exists(tempSandboxDir))
                {
                    Directory.Delete(tempSandboxDir, true);
                }
            }
            catch { }
        }
    }

    private static void Assert(bool condition, string testName, string details = "")
    {
        if (condition)
        {
            Console.WriteLine($"[PASS] {testName}");
            _passedCount++;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FAIL] {testName} - {details}");
            Console.ResetColor();
            _failedCount++;
        }
    }

    private static void TestPluginWheelServiceImplementsSessionService()
    {
        var service = new PluginWheelService("test.plugin", PluginCapability.Wheel);
        Assert(service is IHostWheelService, "ServiceImplementsIHostWheelService");
        Assert(service is IHostWheelSessionService, "ServiceImplementsIHostWheelSessionService");
    }

    private static void TestCapabilityGateDeniedWithoutWheelCapability()
    {
        var deniedService = new PluginWheelService("denied.plugin", PluginCapability.None);
        bool exceptionThrown = false;
        try
        {
            deniedService.RequestTrackedWheel(100, 100, _ => { }, _ => { });
        }
        catch (PluginCapabilityDeniedException ex)
        {
            exceptionThrown = true;
            Assert(ex.Capability == PluginCapability.Wheel, "ExceptionHasCorrectDeniedCapability", ex.Message);
        }
        catch (Exception ex)
        {
            Assert(false, "ThrowsPluginCapabilityDeniedException", $"Unexpected exception: {ex}");
        }

        Assert(exceptionThrown, "CapabilityGateBlocksWithoutWheelCapability");
    }

    private static void TestCapabilityGateAllowedWithWheelCapability()
    {
        var allowedService = new PluginWheelService("allowed.plugin", PluginCapability.Wheel);
        bool exceptionThrown = false;
        try
        {
            PluginHost.HeadlessMode = true;
            IDisposable? token = allowedService.RequestTrackedWheel(100, 100, _ => { }, _ => { });
            Assert(token == null, "HeadlessModeReturnsNullToken");
        }
        catch (Exception ex)
        {
            exceptionThrown = true;
            Assert(false, "CapabilityGateAllowsWithWheelCapability", $"Unexpected exception: {ex}");
        }

        Assert(!exceptionThrown, "CapabilityGatePassesWithWheelCapability");
    }

    private static void TestHeadlessModeReturnsNull()
    {
        PluginHost.HeadlessMode = true;
        var service = new PluginWheelService("headless.plugin", PluginCapability.Wheel);
        IDisposable? token = service.RequestTrackedWheel(200, 200, _ => { }, _ => { });
        Assert(token == null, "TestHeadlessModeReturnsNull");
    }

    private static void TestInvalidCoordinatesReturnNull()
    {
        PluginHost.HeadlessMode = false;
        var service = new PluginWheelService("coord.plugin", PluginCapability.Wheel);
        IDisposable? nanToken = service.RequestTrackedWheel(double.NaN, 100, _ => { }, _ => { });
        Assert(nanToken == null, "TestNanCoordinateReturnsNull");

        IDisposable? infToken = service.RequestTrackedWheel(100, double.PositiveInfinity, _ => { }, _ => { });
        Assert(infToken == null, "TestInfCoordinateReturnsNull");
    }

    private static PluginInstance RegisterActiveInstance(string id)
    {
        var instances = (Dictionary<string, PluginInstance>)typeof(PluginHost)
            .GetField("Instances", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var value = new PluginInstance(id, new PluginRegistryEntry { Id = id }, new PluginScanResult());
        typeof(PluginInstance).GetMethod("SetState", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(value, new object[] { PluginRuntimeState.Active });
        typeof(PluginInstance).GetField("_acceptingCalls", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(value, true);
        instances[id] = value;
        return value;
    }

    private static void TestTokenDisposalDetachesCallbacks()
    {
        int stateEvents = 0;
        int selectionEvents = 0;
        RegisterActiveInstance("tracker.plugin");

        var session = new StickyWheelSession.Session(
            "tracker.plugin",
            new System.Windows.Point(100, 100),
            1,
            null,
            _ => stateEvents++,
            _ => selectionEvents++);

        session.NotifyState(WheelSessionState.Requested, "Test");
        Assert(stateEvents == 1, "NotifyStateBeforeDisposeReceived");

        session.NotifySelection(new WheelSelectionChangedEventArgs
        {
            SessionId = session.SessionId,
            PhysicalCenterX = 100,
            PhysicalCenterY = 100,
            IsEmptySelection = true,
            AngleDegrees = double.NaN,
            SectorIndex = -1,
            TotalSectors = 8
        });
        Assert(selectionEvents == 1, "NotifySelectionBeforeDisposeReceived");

        session.DetachCallbacks();

        session.NotifyState(WheelSessionState.Closed, "TestClosed");
        Assert(stateEvents == 1, "NotifyStateAfterDisposeBlocked");

        session.NotifySelection(new WheelSelectionChangedEventArgs
        {
            SessionId = session.SessionId,
            PhysicalCenterX = 100,
            PhysicalCenterY = 100,
            IsEmptySelection = false,
            AngleDegrees = 45.0,
            SectorIndex = 1,
            TotalSectors = 8
        });
        Assert(selectionEvents == 1, "NotifySelectionAfterDisposeBlocked");
    }

    private static void TestPluginRevokeDetachesCallbacks()
    {
        int stateEvents = 0;
        RegisterActiveInstance("revoke.plugin");
        var session = new StickyWheelSession.Session(
            "revoke.plugin",
            new System.Windows.Point(100, 100),
            2,
            null,
            _ => stateEvents++,
            null);

        session.NotifyState(WheelSessionState.Requested, "Req");
        Assert(stateEvents == 1, "StateEventBeforeRevoke");

        StickyWheelSession.RevokePluginCallbacks("revoke.plugin");
        session.NotifyState(WheelSessionState.Closed, "Close");
        Assert(stateEvents == 1, "StateEventAfterRevokeBlocked");
    }

    private static void TestAngleAndSectorFormulas()
    {
        int totalSectors = 8;
        double slice = 360.0 / totalSectors;

        for (int s = 0; s < totalSectors; s++)
        {
            double angle = (s * slice) % 360.0;
            double expected = s switch
            {
                0 => 0.0,   // E
                1 => 45.0,  // SE
                2 => 90.0,  // S
                3 => 135.0, // SW
                4 => 180.0, // W
                5 => 225.0, // NW
                6 => 270.0, // N
                7 => 315.0, // NE
                _ => -1.0
            };
            Assert(Math.Abs(angle - expected) < 0.001, $"SectorAngleMapping_{s}", $"Expected {expected}, got {angle}");
        }
    }

    private static void TestSessionEventArgTypes()
    {
        var stateArgs = new WheelSessionStateChangedEventArgs
        {
            SessionId = "sess-1",
            State = WheelSessionState.Presented,
            Reason = "OK"
        };
        Assert(stateArgs.State == WheelSessionState.Presented, "StateArgPresented");

        var selArgs = new WheelSelectionChangedEventArgs
        {
            SessionId = "sess-1",
            PhysicalCenterX = 500,
            PhysicalCenterY = 400,
            IsEmptySelection = false,
            MenuDepth = 1,
            AngleDegrees = 90.0,
            SectorIndex = 2,
            SubSectorIndex = 3,
            TotalSectors = 8
        };
        Assert(selArgs.AngleDegrees == 90.0, "SelArgAngle");
        Assert(selArgs.SectorIndex == 2, "SelArgSector");
        Assert(selArgs.SubSectorIndex == 3, "SelArgSubSector");
    }

    private static void TestTerminalStateAtMostOnce()
    {
        int closedCount = 0;
        int supersededCount = 0;
        RegisterActiveInstance("terminal.plugin");
        var session = new StickyWheelSession.Session(
            "terminal.plugin",
            new System.Windows.Point(100, 100),
            10,
            null,
            args =>
            {
                if (args.State == WheelSessionState.Closed) closedCount++;
                if (args.State == WheelSessionState.Superseded) supersededCount++;
            },
            null);

        session.PostState(WheelSessionState.Closed, "FirstTerminal");
        session.PostState(WheelSessionState.Closed, "SecondTerminal");
        session.PostState(WheelSessionState.Superseded, "LateTerminal");

        Assert(closedCount == 1, "TerminalStateDeliveredExactlyOnce", $"Delivered {closedCount} times");
        Assert(supersededCount == 0, "SecondTerminalStateIgnored", $"Delivered {supersededCount} times");
    }

    private static void TestTokenDisposeDoesNotEmitClosed()
    {
        int stateEvents = 0;
        RegisterActiveInstance("dispose.plugin");
        var session = new StickyWheelSession.Session(
            "dispose.plugin",
            new System.Windows.Point(100, 100),
            11,
            null,
            _ => stateEvents++,
            null);

        var tokenType = typeof(StickyWheelSession).GetNestedType("SessionTrackerToken", System.Reflection.BindingFlags.NonPublic);
        var token = Activator.CreateInstance(tokenType!, session) as IDisposable;

        Assert(token != null, "SessionTrackerTokenCreated");
        token?.Dispose();

        Assert(stateEvents == 0, "TokenDisposeDoesNotEmitAnyState", $"Emitted {stateEvents} events");

        session.PostState(WheelSessionState.Closed, "PostAfterDispose");
        Assert(stateEvents == 0, "CallbacksDetachedAfterTokenDispose", $"Emitted {stateEvents} events");
    }

    private static void TestFrameCoalescingState()
    {
        int selectionCount = 0;
        WheelSelectionChangedEventArgs? lastArgs = null;
        RegisterActiveInstance("coalesce.plugin");
        var session = new StickyWheelSession.Session(
            "coalesce.plugin",
            new System.Windows.Point(100, 100),
            12,
            null,
            null,
            args =>
            {
                selectionCount++;
                lastArgs = args;
            });

        session.PendingSector = 0;
        session.PendingSub = -1;
        session.PendingShowSub = false;
        session.PendingEscaped = false;

        session.PendingSector = 2;
        session.PendingSub = 1;
        session.PendingShowSub = true;
        session.PendingEscaped = false;

        session.DeliverSelection(new WheelSelectionChangedEventArgs
        {
            SessionId = session.SessionId,
            PhysicalCenterX = 100,
            PhysicalCenterY = 100,
            IsEmptySelection = false,
            MenuDepth = 1,
            AngleDegrees = 90.0,
            SectorIndex = session.PendingSector,
            SubSectorIndex = session.PendingSub,
            TotalSectors = 8
        });

        Assert(selectionCount == 1, "SingleSelectionAfterCoalescing");
        Assert(lastArgs?.SectorIndex == 2, "CoalescedFinalSectorIndex2");
        Assert(lastArgs?.SubSectorIndex == 1, "CoalescedFinalSubSectorIndex1");
    }

    private static void TestLateSelectionAfterTerminationIgnored()
    {
        int selectionEvents = 0;
        RegisterActiveInstance("stale.plugin");
        var session = new StickyWheelSession.Session(
            "stale.plugin",
            new System.Windows.Point(100, 100),
            13,
            null,
            null,
            _ => selectionEvents++);

        session.PostState(WheelSessionState.Superseded, "Superseded");
        session.DeliverSelection(new WheelSelectionChangedEventArgs
        {
            SessionId = session.SessionId,
            PhysicalCenterX = 100,
            PhysicalCenterY = 100,
            IsEmptySelection = false,
            AngleDegrees = 45.0,
            SectorIndex = 1,
            SubSectorIndex = -1,
            TotalSectors = 8
        });

        Assert(selectionEvents == 0, "LateSelectionAfterTerminationIgnored");
    }

    private static void TestOwnerLeaseHeldDuringCallbackAndDrainedOnReturn()
    {
        const string id = "lease.test.plugin";
        var instance = RegisterActiveInstance(id);
        bool leased = false, waiting = false;
        Task? drained = null;

        var session = new StickyWheelSession.Session(
            id,
            new System.Windows.Point(100, 100),
            20,
            null,
            _ =>
            {
                leased = instance.ActiveCallCount == 1;
                drained = instance.BeginStopping();
                waiting = !drained.IsCompleted;
            },
            null);

        session.DeliverState(WheelSessionState.Requested, "PositiveLeaseTest");
        Assert(leased, "OwnerLeaseHeldDuringCallback");
        Assert(waiting, "StoppingWaitsForActiveCallbackLease");
        Assert(drained != null && drained.IsCompleted, "StoppingDrainedAfterCallbackReturns");
        Assert(instance.ActiveCallCount == 0, "ActiveCallCountZeroAfterReturn");
    }

    private static void TestGenerationSeparationBlocksOldCallbacksOnSameIdReplacement()
    {
        const string id = "generation.test.plugin";
        var oldGen = RegisterActiveInstance(id);
        int stateCalls = 0, selectionCalls = 0;

        var staleSession = new StickyWheelSession.Session(
            id,
            new System.Windows.Point(100, 100),
            21,
            null,
            _ => stateCalls++,
            _ => selectionCalls++);

        oldGen.BeginStopping();
        var replacement = RegisterActiveInstance(id);

        staleSession.DeliverState(WheelSessionState.Presented, "OldGenDelivery");
        staleSession.DeliverSelection(new WheelSelectionChangedEventArgs
        {
            SessionId = staleSession.SessionId,
            PhysicalCenterX = 100,
            PhysicalCenterY = 100,
            IsEmptySelection = false,
            AngleDegrees = 0,
            SectorIndex = 0,
            TotalSectors = 8
        });

        Assert(stateCalls == 0, "OldGenerationStateCallbackBlockedOnReplacement");
        Assert(selectionCalls == 0, "OldGenerationSelectionCallbackBlockedOnReplacement");
    }

    private static void TestStoppingAndUnregisteredCallerRejected()
    {
        const string stoppingId = "stopping.test.plugin";
        var stoppingInstance = RegisterActiveInstance(stoppingId);
        stoppingInstance.BeginStopping();

        var serviceForStopping = new PluginWheelService(stoppingInstance, PluginCapability.Wheel);
        var tokenFromStopping = serviceForStopping.RequestTrackedWheel(100, 100, _ => { }, _ => { });
        Assert(tokenFromStopping == null, "RequestTrackedFromStoppingInstanceRejected");

        const string unregId = "unregistered.plugin";
        var serviceForUnreg = new PluginWheelService(unregId, PluginCapability.Wheel);
        var tokenFromUnreg = serviceForUnreg.RequestTrackedWheel(100, 100, _ => { }, _ => { });
        Assert(tokenFromUnreg == null, "RequestTrackedFromUnregisteredCallerRejected");
    }

    private static void TestRevocationDetachesNonCurrentAndQueuedSessions()
    {
        const string id = "revoke.noncurrent.plugin";
        var instance = RegisterActiveInstance(id);
        int callbackCalls = 0;

        var nonCurrentSession = new StickyWheelSession.Session(
            id,
            new System.Windows.Point(100, 100),
            22,
            null,
            _ => callbackCalls++,
            _ => callbackCalls++);

        StickyWheelSession.RevokePluginCallbacks(id, instance, instance.GenerationId);

        nonCurrentSession.DeliverState(WheelSessionState.Presented, "DeliverAfterRevoke");
        nonCurrentSession.DeliverSelection(new WheelSelectionChangedEventArgs
        {
            SessionId = nonCurrentSession.SessionId,
            PhysicalCenterX = 100,
            PhysicalCenterY = 100,
            IsEmptySelection = false,
            AngleDegrees = 0,
            SectorIndex = 0,
            TotalSectors = 8
        });

        Assert(callbackCalls == 0, "RevocationDetachesNonCurrentSessionCallbacks");
    }

    private static void TestRevokingOneCallerDoesNotAffectOtherCallers()
    {
        const string idA = "plugin.caller.a";
        const string idB = "plugin.caller.b";
        var instanceA = RegisterActiveInstance(idA);
        var instanceB = RegisterActiveInstance(idB);

        int callsA = 0, callsB = 0;
        var sessionA = new StickyWheelSession.Session(idA, new System.Windows.Point(100, 100), 23, null, _ => callsA++);
        var sessionB = new StickyWheelSession.Session(idB, new System.Windows.Point(100, 100), 24, null, _ => callsB++);

        StickyWheelSession.RevokePluginCallbacks(idA, instanceA, instanceA.GenerationId);

        sessionA.DeliverState(WheelSessionState.Presented, "DeliverA");
        sessionB.DeliverState(WheelSessionState.Presented, "DeliverB");

        Assert(callsA == 0, "RevokedCallerACallbackBlocked");
        Assert(callsB == 1, "UnrelatedCallerBCallbackDelivered");
    }

    private static void TestTokenDisposeDoesNotDismissWheel()
    {
        const string id = "dispose.nodismiss.plugin";
        RegisterActiveInstance(id);
        int calls = 0;

        var session = new StickyWheelSession.Session(id, new System.Windows.Point(100, 100), 25, null, _ => calls++);
        var tokenType = typeof(StickyWheelSession).GetNestedType("SessionTrackerToken", System.Reflection.BindingFlags.NonPublic);
        var token = Activator.CreateInstance(tokenType!, session) as IDisposable;

        Assert(token != null, "TokenCreatedForNoDismissTest");
        token?.Dispose();

        session.DeliverState(WheelSessionState.Presented, "AfterDispose");
        Assert(calls == 0, "CallbacksDetachedAfterTokenDisposeNoDismiss");
    }

    private static string DesktopPetDllPath => Environment.GetEnvironmentVariable("STARPIE_DESKTOP_PET_DLL")
        ?? "H:/starpie-official-worktrees/SP-DESKTOPPET-001/src/StarPie.Plugin.DesktopPet/bin/Release/net8.0-windows/StarPie.Plugin.DesktopPet.dll";

    private static void TestRealLoadGenerationMatchesActiveInstanceAndAcceptsCallback()
    {
        PluginHost.HeadlessMode = true;
        var scan = PluginHost.PrepareInstall(DesktopPetDllPath);
        Assert(scan.Accepted && scan.Manifest != null, "DesktopPetScanAccepted");
        if (!scan.Accepted || scan.Manifest == null) return;

        string id = scan.Manifest.Id;
        var owner = new PluginInstance(id, new PluginRegistryEntry { Id = id, Enabled = true, ExternalPath = DesktopPetDllPath }, scan);
        var instances = (Dictionary<string, PluginInstance>)typeof(PluginHost)
            .GetField("Instances", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        instances[id] = owner;

        try
        {
            bool loaded = owner.Load(out string error);
            Assert(loaded, "DesktopPetRealLoadSuccess", error);
            if (!loaded) return;

            object wheel = owner.ContextForSelfTest!.Wheel;
            long serviceGeneration = (long)wheel.GetType()
                .GetField("_generationId", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(wheel)!;

            bool generationMatches = serviceGeneration == owner.GenerationId;
            Assert(generationMatches, "RealLoadContextWheelGenerationMatchesActiveInstance",
                $"service={serviceGeneration}, active={owner.GenerationId}, state={owner.State}");

            int calls = 0;
            bool leasedDuringCallback = false;
            var session = new StickyWheelSession.Session(
                id,
                new System.Windows.Point(100, 100),
                101,
                null,
                _ =>
                {
                    calls++;
                    leasedDuringCallback = owner.ActiveCallCount > 0;
                },
                ownerInstance: owner,
                ownerGeneration: serviceGeneration);

            session.DeliverState(WheelSessionState.Presented, "RealLoadCallbackDelivery");
            session.DetachCallbacks();

            Assert(calls == 1, "RealLoadCallbackDeliveredUsingServiceGeneration", $"calls={calls}");
            Assert(leasedDuringCallback, "RealLoadCallbackHoldsOwnerLease");
            Assert(owner.ActiveCallCount == 0, "RealLoadCallbackDrainsOwnerLease");
        }
        finally
        {
            owner.Unload();
            instances.Remove(id);
        }
    }

    private static void TestRealReloadNewGenerationAcceptedAndOldServiceGenerationBlocked()
    {
        PluginHost.HeadlessMode = true;
        var scan = PluginHost.PrepareInstall(DesktopPetDllPath);
        Assert(scan.Accepted && scan.Manifest != null, "DesktopPetReloadScanAccepted");
        if (!scan.Accepted || scan.Manifest == null) return;

        string id = scan.Manifest.Id;
        var owner = new PluginInstance(id, new PluginRegistryEntry { Id = id, Enabled = true, ExternalPath = DesktopPetDllPath }, scan);
        var instances = (Dictionary<string, PluginInstance>)typeof(PluginHost)
            .GetField("Instances", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        instances[id] = owner;

        try
        {
            // Round 1
            bool loaded1 = owner.Load(out string error1);
            Assert(loaded1, "DesktopPetRound1LoadSuccess", error1);
            if (!loaded1) return;

            long round1InstanceGen = owner.GenerationId;
            long round1ServiceGen;
            {
                object wheel1 = owner.ContextForSelfTest!.Wheel;
                round1ServiceGen = (long)wheel1.GetType()
                    .GetField("_generationId", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(wheel1)!;
            }
            Assert(round1ServiceGen == round1InstanceGen, "Round1ServiceGenerationMatchesInstance",
                $"service={round1ServiceGen}, active={round1InstanceGen}");

            owner.Unload();
            Assert(!owner.IsLoaded, "InstanceUnloadedAfterRound1");
            Assert(owner.State == PluginRuntimeState.Installed, "InstanceStateInstalledAfterRound1Unload");

            // Round 2 on same instance
            bool loaded2 = owner.Load(out string error2);
            Assert(loaded2, "DesktopPetRound2ReloadSuccess", error2);
            if (!loaded2) return;

            long round2InstanceGen = owner.GenerationId;
            Assert(round2InstanceGen > round1InstanceGen, "ReloadIncrementsInstanceGenerationId",
                $"round2={round2InstanceGen}, round1={round1InstanceGen}");

            long round2ServiceGen;
            {
                object wheel2 = owner.ContextForSelfTest!.Wheel;
                round2ServiceGen = (long)wheel2.GetType()
                    .GetField("_generationId", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(wheel2)!;
            }
            Assert(round2ServiceGen == round2InstanceGen, "Round2ServiceGenerationMatchesReloadedInstance",
                $"service={round2ServiceGen}, active={round2InstanceGen}");

            // Stale service generation from round 1 should be blocked on reloaded instance
            int oldCalls = 0;
            var oldSession = new StickyWheelSession.Session(
                id,
                new System.Windows.Point(100, 100),
                201,
                null,
                _ => oldCalls++,
                ownerInstance: owner,
                ownerGeneration: round1ServiceGen);
            oldSession.DeliverState(WheelSessionState.Presented, "StaleRound1DeliverAttempt");
            oldSession.DetachCallbacks();
            Assert(oldCalls == 0, "OldRound1ServiceGenerationBlockedOnReloadedInstance", $"oldCalls={oldCalls}");

            // New service generation from round 2 should be accepted
            int newCalls = 0;
            var newSession = new StickyWheelSession.Session(
                id,
                new System.Windows.Point(100, 100),
                202,
                null,
                _ => newCalls++,
                ownerInstance: owner,
                ownerGeneration: round2ServiceGen);
            newSession.DeliverState(WheelSessionState.Presented, "NewRound2DeliverAttempt");
            newSession.DetachCallbacks();
            Assert(newCalls == 1, "NewRound2ServiceGenerationAcceptedOnReloadedInstance", $"newCalls={newCalls}");
        }
        finally
        {
            owner.Unload();
            instances.Remove(id);
        }
    }
}
