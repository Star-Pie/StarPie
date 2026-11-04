using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;

namespace InputPanelTargetTests;

internal static class Program
{
    private const string GuardTypeName = "WinPieGestures.Plugins.InputPanelTargetGuard";

    private static Type? s_guardType;
    private static ConstructorInfo? s_guardConstructor;
    private static MethodInfo? s_onTargetEvent;
    private static AssemblyLoadContext? s_candidateContext;
    private static int s_passed;
    private static int s_failed;

    private static int Main(string[] args)
    {
        if (!CandidateOptions.TryParse(args, out CandidateOptions options, out string? error))
        {
            Console.WriteLine(error);
            return 2;
        }

        Console.WriteLine($"CANDIDATE {options.CandidateDir}");
        Console.WriteLine($"EXPECTED_HOST_SHA256 {options.HostSha256}");
        Console.WriteLine($"EXPECTED_SDK_SHA256 {options.SdkSha256}");

        Run("candidate SHA256 and new guard reflection binding", () => BindCandidate(options));
        Run("event before session epoch is ignored", EventBeforeEpochIsIgnored);
        Run("event equal to session epoch closes immediately", EventAtEpochCloses);
        Run("event after session epoch closes immediately", EventAfterEpochCloses);
        Run("delayed event after refocus still closes", DelayedEventAfterRefocusCloses);
        Run("closed guard is idempotent and does not read clock", ClosedGuardIsIdempotent);
        Run("32-bit tick wrap is restored", TickWrapIsRestored);
        Run("uptime beyond 49 days is restored from current time", UptimeBeyond49DaysIsRestored);
        Run("closed queue rejects unprocessed requests", ClosedQueueRejectsRequests);
        Run("legacy deferred-recovery fixture remains red", LegacyCounterexamples);

        Console.WriteLine($"RESULT: {(s_failed == 0 ? "PASS" : "FAIL")} ({s_passed} passed, {s_failed} failed)");
        return s_failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            s_passed++;
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception ex)
        {
            s_failed++;
            Console.WriteLine($"FAIL {name}: {ex.Message}");
        }
    }

    private static void BindCandidate(CandidateOptions options)
    {
        string hostPath = Path.Combine(options.CandidateDir, "StarPie.dll");
        string sdkPath = Path.Combine(options.CandidateDir, "StarPie.Plugin.Abstractions.dll");
        Assert(File.Exists(hostPath), $"Host DLL not found: {hostPath}");
        Assert(File.Exists(sdkPath), $"SDK DLL not found: {sdkPath}");

        string hostHash = Sha256(hostPath);
        string sdkHash = Sha256(sdkPath);
        Equal(options.HostSha256, hostHash, "Host DLL SHA256 binding mismatch.");
        Equal(options.SdkSha256, sdkHash, "SDK DLL SHA256 binding mismatch.");

        s_candidateContext = new AssemblyLoadContext("starpie-candidate", isCollectible: false);
        var resolver = new AssemblyDependencyResolver(hostPath);
        s_candidateContext.Resolving += (context, name) =>
        {
            string? dependencyPath = resolver.ResolveAssemblyToPath(name);
            return dependencyPath == null ? null : context.LoadFromAssemblyPath(dependencyPath);
        };

        Assembly hostAssembly = s_candidateContext.LoadFromAssemblyPath(hostPath);
        Assembly sdkAssembly = s_candidateContext.LoadFromAssemblyPath(sdkPath);
        Equal(Path.GetFullPath(hostPath), Path.GetFullPath(hostAssembly.Location),
            "The loaded host assembly must come from the candidate directory.");
        Equal(Path.GetFullPath(sdkPath), Path.GetFullPath(sdkAssembly.Location),
            "The loaded SDK assembly must come from the candidate directory.");

        Type guardType = hostAssembly.GetType(GuardTypeName, throwOnError: false)
            ?? throw new InvalidOperationException($"Guard type missing: {GuardTypeName}");
        s_guardType = guardType;
        ConstructorInfo guardConstructor = guardType.GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            binder: null,
            types:
            [
                typeof(long),
                typeof(Func<long>),
                typeof(Func<bool>),
                typeof(Action),
            ],
            modifiers: null)
            ?? throw new InvalidOperationException(
                "Guard constructor must be (long, Func<long>, Func<bool>, Action).");
        s_guardConstructor = guardConstructor;
        MethodInfo onTargetEvent = guardType.GetMethod(
            "OnTargetEvent",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            binder: null,
            types: [typeof(uint)],
            modifiers: null)
            ?? throw new InvalidOperationException("Guard OnTargetEvent(uint) method is missing.");
        s_onTargetEvent = onTargetEvent;

        Console.WriteLine($"HOST_SHA256 {hostHash}");
        Console.WriteLine($"SDK_SHA256 {sdkHash}");
        Console.WriteLine($"GUARD_TYPE {GuardTypeName}");
    }

    private static void EventBeforeEpochIsIgnored()
    {
        const long startedAt = 0x1_0000_0100L;
        var state = new FakePanelState(startedAt + 1_000);
        GuardHandle guard = CreateGuard(state, startedAt);

        guard.OnTargetEvent(Low32(startedAt - 1_000));

        Equal(0, state.CloseAttempts, "An event before the session epoch must be ignored.");
        True(state.IsOpen, "The panel must remain open for a pre-epoch event.");
    }

    private static void EventAtEpochCloses()
    {
        const long startedAt = 0x1_0000_0100L;
        var state = new FakePanelState(startedAt + 100);
        GuardHandle guard = CreateGuard(state, startedAt);

        guard.OnTargetEvent(Low32(startedAt));

        Equal(1, state.CloseAttempts, "An event equal to the session epoch must close.");
        False(state.IsOpen, "The panel must be closed after the epoch event.");
    }

    private static void EventAfterEpochCloses()
    {
        const long startedAt = 0x1_0000_0100L;
        var state = new FakePanelState(startedAt + 100);
        GuardHandle guard = CreateGuard(state, startedAt);

        guard.OnTargetEvent(Low32(startedAt + 50));

        Equal(1, state.CloseAttempts, "An event after the session epoch must close.");
        False(state.IsOpen, "The panel must be closed after the post-epoch event.");
    }

    private static void DelayedEventAfterRefocusCloses()
    {
        const long startedAt = 0x1_0000_0100L;
        var state = new FakePanelState(startedAt + 1_000);
        state.TargetFocused = true;
        GuardHandle guard = CreateGuard(state, startedAt);

        guard.OnTargetEvent(Low32(startedAt + 100));

        Equal(1, state.CloseAttempts,
            "A post-epoch event must close even when the target has already regained focus.");
        False(state.IsOpen, "Refocus must not revive the session after a post-epoch event.");
    }

    private static void ClosedGuardIsIdempotent()
    {
        const long startedAt = 0x1_0000_0100L;
        var state = new FakePanelState(startedAt + 100);
        GuardHandle guard = CreateGuard(state, startedAt);

        guard.OnTargetEvent(Low32(startedAt));
        int clockReadsAfterClose = state.ClockReadCount;
        int closeAttemptsAfterClose = state.CloseAttempts;

        guard.OnTargetEvent(Low32(startedAt + 1));

        Equal(clockReadsAfterClose, state.ClockReadCount,
            "A closed guard must not invoke the injected clock.");
        Equal(closeAttemptsAfterClose, state.CloseAttempts,
            "A closed guard must not close twice.");
        Equal(1, state.CloseNotifications, "The fake close state must record one close.");
    }

    private static void TickWrapIsRestored()
    {
        const long startedAt = 0xFFFFF000L;
        const long now = 0x1_0000_0050L;
        long eventAt = startedAt + 100;
        var state = new FakePanelState(now);
        GuardHandle guard = CreateGuard(state, startedAt);

        guard.OnTargetEvent(Low32(eventAt));

        Equal(1, state.CloseAttempts,
            "A post-epoch event across the 32-bit tick wrap must close.");
        False(state.IsOpen, "The wrapped event must invalidate the queue.");
    }

    private static void UptimeBeyond49DaysIsRestored()
    {
        const long startedAt = 0x1_0000_0100L;
        const long now = startedAt + 200;
        long eventAt = startedAt + 100;
        var state = new FakePanelState(now);
        GuardHandle guard = CreateGuard(state, startedAt);

        guard.OnTargetEvent(Low32(eventAt));

        Equal(1, state.CloseAttempts,
            "The 64-bit current time must restore a uint event time after 49 days.");
        False(state.IsOpen, "The queue must close after a valid high-uptime event.");
    }

    private static void ClosedQueueRejectsRequests()
    {
        const long startedAt = 0x1_0000_0100L;
        var state = new FakePanelState(startedAt + 100);
        state.EnqueueRequest("pending text");
        GuardHandle guard = CreateGuard(state, startedAt);

        guard.OnTargetEvent(Low32(startedAt));

        False(state.TryConsumeRequest(),
            "Closing the guard must make the fake queue reject an unconsumed request.");
        Equal(1, state.PendingRequests, "The unprocessed request must remain unprocessed.");
    }

    private static void LegacyCounterexamples()
    {
        const long startedAt = 0x1_0000_0100L;

        var legacyNotification = new FakePanelState(startedAt + 100);
        Action legacyChanged = legacyNotification.Close;
        legacyChanged();
        Equal(1, legacyNotification.CloseAttempts,
            "The old notification-close fixture must close immediately.");

        var legacyDeferred = new FakePanelState(startedAt + 100)
        {
            TargetValid = false,
        };
        var legacyGuard = new LegacyDeferredGuard(legacyDeferred);
        legacyGuard.OnTargetEvent();
        legacyDeferred.TargetValid = true;
        legacyGuard.RunPosted();
        Equal(0, legacyDeferred.CloseAttempts,
            "The old deferred-recovery fixture must retain the session after refocus.");
        True(legacyDeferred.IsOpen,
            "The red fixture demonstrates the rejected old behavior.");

        var guarded = new FakePanelState(startedAt + 100)
        {
            TargetValid = true,
        };
        GuardHandle newGuard = CreateGuard(guarded, startedAt);
        newGuard.OnTargetEvent(Low32(startedAt + 50));
        Equal(1, guarded.CloseAttempts,
            "The new guard must not retain the session for a post-epoch event.");
        Console.WriteLine("RED-FIXTURE old guard retained the session after away/back; new guard closes.");
    }

    private static GuardHandle CreateGuard(FakePanelState state, long startedAt)
    {
        Assert(s_guardType != null, "Guard reflection binding was not initialized.");
        ConstructorInfo constructor = s_guardConstructor
            ?? throw new InvalidOperationException("Guard constructor reflection was not initialized.");
        MethodInfo onTargetEvent = s_onTargetEvent
            ?? throw new InvalidOperationException("Guard event reflection was not initialized.");

        object guard;
        try
        {
            guard = constructor.Invoke(
            [
                startedAt,
                (Func<long>)state.ReadClock,
                (Func<bool>)(() => state.IsOpen),
                (Action)state.Close,
            ]);
        }
        catch (TargetInvocationException ex)
        {
            throw ex.InnerException ?? ex;
        }

        return new GuardHandle(guard, onTargetEvent);
    }

    private static string Sha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static uint Low32(long value) => unchecked((uint)value);

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void True(bool value, string message) => Assert(value, message);

    private static void False(bool value, string message) => Assert(!value, message);

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} Expected: {expected}; actual: {actual}.");
        }
    }

    private sealed class CandidateOptions
    {
        internal string CandidateDir { get; }
        internal string HostSha256 { get; }
        internal string SdkSha256 { get; }

        private CandidateOptions(string candidateDir, string hostSha256, string sdkSha256)
        {
            CandidateDir = candidateDir;
            HostSha256 = hostSha256;
            SdkSha256 = sdkSha256;
        }

        internal static bool TryParse(
            string[] args,
            out CandidateOptions options,
            out string? error)
        {
            options = new CandidateOptions("", "", "");
            error = null;
            if (args.Length != 3)
            {
                error = "Usage: test_input_panel_target.dll <candidate-dir> <host-sha256> <sdk-sha256>";
                return false;
            }

            if (!IsSha256(args[1]) || !IsSha256(args[2]))
            {
                error = "Both SHA-256 values must be 64 hexadecimal characters.";
                return false;
            }

            options = new CandidateOptions(
                Path.GetFullPath(args[0]),
                args[1].ToUpperInvariant(),
                args[2].ToUpperInvariant());
            return true;
        }

        private static bool IsSha256(string value)
        {
            if (value.Length != 64)
            {
                return false;
            }

            try
            {
                _ = Convert.FromHexString(value);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }

    private sealed class GuardHandle
    {
        private readonly object _guard;
        private readonly MethodInfo _onTargetEvent;

        internal GuardHandle(object guard, MethodInfo onTargetEvent)
        {
            _guard = guard;
            _onTargetEvent = onTargetEvent;
        }

        internal void OnTargetEvent(uint eventTime)
        {
            try
            {
                _onTargetEvent.Invoke(_guard, [eventTime]);
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException ?? ex;
            }
        }
    }

    private sealed class FakePanelState
    {
        private readonly Queue<string> _requests = new();
        private bool _closed;

        internal FakePanelState(long now, bool open = true)
        {
            Now = now;
            IsOpen = open;
            _closed = !open;
        }

        internal long Now { get; set; }

        internal bool IsOpen { get; private set; }

        internal bool TargetValid { get; set; }

        internal bool TargetFocused { get; set; }

        internal int ClockReadCount { get; private set; }

        internal int CloseAttempts { get; private set; }

        internal int CloseNotifications { get; private set; }

        internal int PendingRequests => _requests.Count;

        internal int ConsumedRequests { get; private set; }

        internal long ReadClock()
        {
            ClockReadCount++;
            return Now;
        }

        internal void EnqueueRequest(string request)
        {
            _requests.Enqueue(request);
        }

        internal bool TryConsumeRequest()
        {
            if (!IsOpen || _requests.Count == 0)
            {
                return false;
            }

            _ = _requests.Dequeue();
            ConsumedRequests++;
            return true;
        }

        internal void Close()
        {
            CloseAttempts++;
            if (_closed)
            {
                return;
            }

            _closed = true;
            IsOpen = false;
            CloseNotifications++;
        }

        internal void MarkClosed()
        {
            _closed = true;
            IsOpen = false;
        }

        internal void Reopen()
        {
            _closed = false;
            IsOpen = true;
        }
    }

    private sealed class LegacyDeferredGuard
    {
        private readonly FakePanelState _state;
        private readonly Queue<Action> _posts = new();
        private int _pending;

        internal LegacyDeferredGuard(FakePanelState state)
        {
            _state = state;
        }

        internal void OnTargetEvent()
        {
            if (!_state.IsOpen || _state.TargetValid ||
                Interlocked.Exchange(ref _pending, 1) != 0)
            {
                return;
            }

            _posts.Enqueue(() =>
            {
                Interlocked.Exchange(ref _pending, 0);
                if (_state.IsOpen && !_state.TargetValid)
                {
                    _state.Close();
                }
            });
        }

        internal void RunPosted()
        {
            Assert(_posts.Count > 0, "Expected a legacy posted callback.");
            _posts.Dequeue()();
        }
    }
}
