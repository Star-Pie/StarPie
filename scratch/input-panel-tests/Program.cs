using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StarPie.Plugin;
using WinPieGestures;
using WinPieGestures.Plugins;

internal static class Program
{
    private static int s_passed;
    private static int s_failed;

    private static int Main(string[] args)
    {
        if (!TryVerifyCandidate(args, out string? candidateError))
        {
            Console.WriteLine($"CANDIDATE: FAIL {candidateError}");
            return 2;
        }

        Run("queue preserves order and deep snapshots", QueuePreservesOrderAndDeepSnapshots);
        Run("queue caps pending at 64 and releases once", QueueCapsAndReleasesOnce);
        Run("queue close invalidates old and new requests", QueueCloseInvalidates);
        Run("queue validity callback invalidates pending requests", QueueValidityInvalidates);
        Run("queue enqueue failure releases pending", QueueEnqueueFailureReleases);
        Run("binding parser rejects invalid input", BindingParserRejectsInvalidInput);
        Run("binding parser accepts boundaries and builds Plugin action", BindingParserAcceptsBoundaries);
        Run("binding parameter snapshot is isolated", BindingParameterSnapshotIsIsolated);
        Run("SDK context additions are structural only", SdkContextStructure);
        Run("safe shallow-copy counterexample is rejected", ShallowCopyCounterexampleIsRejected);
        Run("hotkey aliases normalize to one parsed key step", HotkeyAliasesNormalizeAndParse);
        Run("hotkey normalization rejects unsafe shapes", HotkeyNormalizationRejectsUnsafeShapes);
        Run("private hotkey fake execution suppresses input values from logs", PrivateHotkeyExecutionUsesFakeAndSuppressesLogs);
        Run("private hotkey exception logging suppresses sensitive values", PrivateHotkeyExceptionLoggingSuppressesSensitiveValues);
        Run("invalid target rejects before scheduling", InvalidTargetRejectsBeforeScheduling);
        Run("sequential target invalidation suppresses contribution", SequentialTargetInvalidationSuppressesContribution);
        Run("background target invalidation suppresses contribution and releases lease", BackgroundTargetInvalidationSuppressesContribution);
        Run("target context snapshot survives sequential cancellation", TargetContextSnapshotIsPreserved);
        Run("session cancellation stops background work without fault accounting", SessionCancellationStopsBackgroundWork);
        Run("64 live background actions retain all panel capacity until completion", BackgroundCapacityHoldsUntilFakeContributionsComplete);
        Run("sequential timeout retains capacity until the real contribution ends", SequentialTimeoutRetainsCapacityUntilCompletion);
        Run("background timeout retains capacity until the real contribution ends", BackgroundTimeoutRetainsCapacityUntilCompletion);
        Run("Describe requires registration and ResolveActionAsync is additive", DescribeRequiresRegistrationAndResolveIsAdditive);

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

    private static bool TryVerifyCandidate(string[] args, out string? error)
    {
        error = null;
        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            error = "Usage: test_process_launch <candidate-directory>";
            return false;
        }

        string candidate = Path.GetFullPath(args[0]);
        string candidateHost = Path.Combine(candidate, "StarPie.dll");
        string candidateSdk = Path.Combine(candidate, "StarPie.Plugin.Abstractions.dll");
        string loadedHost = typeof(ActionItem).Assembly.Location;
        string loadedSdk = typeof(IPluginContext).Assembly.Location;

        if (!File.Exists(candidateHost) || !File.Exists(candidateSdk))
        {
            error = $"Candidate DLLs not found under {candidate}.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(loadedHost) || string.IsNullOrWhiteSpace(loadedSdk))
        {
            error = "The loaded host or SDK assembly has no file location.";
            return false;
        }

        string hostHash = Sha256(candidateHost);
        string sdkHash = Sha256(candidateSdk);
        string loadedHostHash = Sha256(loadedHost);
        string loadedSdkHash = Sha256(loadedSdk);

        Console.WriteLine($"CANDIDATE_DIR: {candidate}");
        Console.WriteLine($"CANDIDATE_HOST: {candidateHost}");
        Console.WriteLine($"LOADED_HOST: {loadedHost}");
        Console.WriteLine($"CANDIDATE_SDK: {candidateSdk}");
        Console.WriteLine($"LOADED_SDK: {loadedSdk}");
        Console.WriteLine($"HOST_SHA256: {hostHash}");
        Console.WriteLine($"LOADED_HOST_SHA256: {loadedHostHash}");
        Console.WriteLine($"SDK_SHA256: {sdkHash}");
        Console.WriteLine($"LOADED_SDK_SHA256: {loadedSdkHash}");

        if (!EqualsHash(hostHash, loadedHostHash))
        {
            error = "Loaded host DLL hash does not match the supplied candidate.";
            return false;
        }

        if (!EqualsHash(sdkHash, loadedSdkHash))
        {
            error = "Loaded SDK DLL hash does not match the supplied candidate.";
            return false;
        }

        Console.WriteLine("CANDIDATE: PASS");
        return true;
    }

    private static string Sha256(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static bool EqualsHash(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static void QueuePreservesOrderAndDeepSnapshots()
    {
        var captured = new List<(ActionItem Item, ActionExecutionContext Context)>();
        InputPanelQueue queue = CreateQueue(captured, () => true);

        ActionItem first = NewAction("first");
        ActionItem second = NewAction("second");
        ActionItem third = NewAction("third");

        Equal(InputPanelQueueResult.Accepted, queue.Queue(first), "First queue request must be accepted.");
        Equal(InputPanelQueueResult.Accepted, queue.Queue(second), "Second queue request must be accepted.");
        Equal(InputPanelQueueResult.Accepted, queue.Queue(third), "Third queue request must be accepted.");
        Equal(3, captured.Count, "Queue must publish every accepted request.");
        Equal("first", captured[0].Item.Name, "Queue order mismatch.");
        Equal("second", captured[1].Item.Name, "Queue order mismatch.");
        Equal("third", captured[2].Item.Name, "Queue order mismatch.");

        ActionItem snapshot = captured[0].Item;
        Assert(first.PluginActionRef != null && snapshot.PluginActionRef != null,
            "The test action must contain a plugin reference.");
        Assert(first.ExtensionData != null && snapshot.ExtensionData != null,
            "The test action must contain extension data.");
        PluginActionRef sourceRef = first.PluginActionRef!;
        PluginActionRef snapshotRef = snapshot.PluginActionRef!;
        Dictionary<string, string> sourceData = first.ExtensionData!;
        Dictionary<string, string> snapshotData = snapshot.ExtensionData!;
        Equal("plugin", snapshotRef.PluginId, "Plugin ID snapshot mismatch.");
        Equal("first-contribution", snapshotRef.ContributionId, "Contribution snapshot mismatch.");
        Equal("original", snapshotData["value"], "Extension parameter snapshot mismatch.");

        sourceRef.ContributionId = "source-mutated";
        sourceData["value"] = "source-mutated";
        Equal("first-contribution", snapshotRef.ContributionId, "Queue must clone PluginActionRef.");
        Equal("original", snapshotData["value"], "Queue must clone ExtensionData.");

        snapshotRef.ContributionId = "snapshot-mutated";
        snapshotData["value"] = "snapshot-mutated";
        Equal("source-mutated", sourceRef.ContributionId, "Mutating the snapshot must not mutate the source reference.");
        Equal("source-mutated", sourceData["value"], "Mutating the snapshot must not mutate the source dictionary.");

        captured[0].Context.OnConsumed?.Invoke();
    }

    private static void QueueCapsAndReleasesOnce()
    {
        var captured = new List<(ActionItem Item, ActionExecutionContext Context)>();
        InputPanelQueue queue = CreateQueue(captured, () => true);

        for (int i = 0; i < InputPanelQueue.MaximumPending; i++)
        {
            Equal(InputPanelQueueResult.Accepted, queue.Queue(NewAction($"pending-{i}")),
                $"Request {i} must be accepted.");
        }

        Equal(InputPanelQueue.MaximumPending, queue.Pending, "The queue must retain 64 pending requests.");
        Equal(InputPanelQueueResult.Busy, queue.Queue(NewAction("overflow")),
            "The 65th request must be Busy.");
        Equal(InputPanelQueue.MaximumPending, captured.Count, "Busy request must not be published.");

        ActionExecutionContext firstContext = captured[0].Context;
        firstContext.OnConsumed?.Invoke();
        Equal(63, queue.Pending, "Consumption must release one pending slot.");
        firstContext.OnConsumed?.Invoke();
        Equal(63, queue.Pending, "Repeated consumption must not release another slot.");

        Equal(InputPanelQueueResult.Accepted, queue.Queue(NewAction("replacement")),
            "The released slot must accept a new request.");
        Equal(InputPanelQueue.MaximumPending, queue.Pending, "The replacement must fill the released slot.");

        foreach ((ActionItem _, ActionExecutionContext context) in captured)
        {
            context.OnConsumed?.Invoke();
        }
    }

    private static void QueueCloseInvalidates()
    {
        var captured = new List<(ActionItem Item, ActionExecutionContext Context)>();
        InputPanelQueue queue = CreateQueue(captured, () => true);
        Equal(InputPanelQueueResult.Accepted, queue.Queue(NewAction("before-close")),
            "Pre-close request must be accepted.");

        ActionExecutionContext context = captured[0].Context;
        queue.Close();
        Assert(!queue.IsOpen, "Close must mark the queue closed.");
        Assert(context.IsValidCallback != null && !context.IsValidCallback(),
            "Old context must become invalid after Close.");
        Equal(InputPanelQueueResult.Closed, queue.Queue(NewAction("after-close")),
            "New requests must be rejected after Close.");
        Equal(1, captured.Count, "Closed queue must not publish a new request.");
    }

    private static void QueueValidityInvalidates()
    {
        bool valid = true;
        var captured = new List<(ActionItem Item, ActionExecutionContext Context)>();
        InputPanelQueue queue = CreateQueue(captured, () => valid);
        Equal(InputPanelQueueResult.Accepted, queue.Queue(NewAction("target-bound")),
            "Request while valid must be accepted.");

        valid = false;
        Func<bool>? validityCallback = captured[0].Context.IsValidCallback;
        Assert(validityCallback != null && !validityCallback(),
            "Old context must observe validity changes.");
        Equal(InputPanelQueueResult.Closed, queue.Queue(NewAction("target-invalid")),
            "Invalid target must reject new requests.");
        Equal(1, captured.Count, "Invalid target must not publish a new request.");
    }

    private static void QueueEnqueueFailureReleases()
    {
        var queue = new InputPanelQueue(
            0,
            0,
            "fixture",
            () => true,
            (_, _) => throw new InvalidOperationException("fake enqueue failure"));

        Throws<InvalidOperationException>(
            () => queue.Queue(NewAction("enqueue-failure")),
            "Enqueue failure must be propagated.");
        Equal(0, queue.Pending, "Enqueue failure must release the reserved pending slot.");
        Assert(queue.IsOpen, "Enqueue failure must not close the queue.");
    }

    private static void BindingParserRejectsInvalidInput()
    {
        Assert(!PluginInputPanelService.TryParse(null, out _, out _), "Null binding must be rejected.");
        Assert(!PluginInputPanelService.TryParse(Binding(""), out _, out _), "Empty action ID must be rejected.");
        Assert(!PluginInputPanelService.TryParse(Binding("   "), out _, out _), "Whitespace action ID must be rejected.");
        Assert(!PluginInputPanelService.TryParse(Binding("no-separator"), out _, out _), "Action ID without a plugin separator must be rejected.");
        Assert(!PluginInputPanelService.TryParse(Binding(".contribution"), out _, out _), "Leading separator must be rejected.");
        Assert(!PluginInputPanelService.TryParse(Binding("plugin."), out _, out _), "Trailing separator must be rejected.");
        Assert(!PluginInputPanelService.TryParse(Binding(new string('a', 255) + ".x"), out _, out _),
            "Full action ID longer than 256 characters must be rejected.");
        var nullParameters = new ConfiguredPluginAction
        {
            ActionId = "plugin.contribution",
            Parameters = null!,
        };
        Assert(!PluginInputPanelService.TryParse(nullParameters, out _, out _),
            "Null parameter dictionary must be rejected.");

        var tooMany = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i <= 128; i++)
        {
            tooMany[$"k{i}"] = "v";
        }
        Assert(!PluginInputPanelService.TryParse(Binding("plugin.contribution", tooMany), out _, out _),
            "More than 128 parameters must be rejected.");

        var invalidKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [new string('k', 129)] = "v",
        };
        Assert(!PluginInputPanelService.TryParse(Binding("plugin.contribution", invalidKey), out _, out _),
            "An overlong parameter key must be rejected.");

        var nullValue = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["value"] = null!,
        };
        Assert(!PluginInputPanelService.TryParse(Binding("plugin.contribution", nullValue), out _, out _),
            "A null parameter value must be rejected.");

        var overlongValue = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["value"] = new string('x', PluginApi.MaxParameterValueLength + 1),
        };
        Assert(!PluginInputPanelService.TryParse(Binding("plugin.contribution", overlongValue), out _, out _),
            "An overlong parameter value must be rejected.");
        Assert(PluginInputPanelService.BuildAction(Binding("invalid")) == null,
            "BuildAction must return null for an invalid binding.");
    }

    private static void BindingParserAcceptsBoundaries()
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < 128; i++)
        {
            parameters[$"k{i}"] = i == 0 ? new string('x', PluginApi.MaxParameterValueLength) : "v";
        }

        ConfiguredPluginAction binding = Binding("plugin.contribution", parameters);
        Assert(PluginInputPanelService.TryParse(binding, out string pluginId, out string shortId),
            "A boundary-valid binding must parse.");
        Equal("plugin", pluginId, "Parsed plugin ID mismatch.");
        Equal("contribution", shortId, "Parsed contribution ID mismatch.");

        ActionItem action = PluginInputPanelService.BuildAction(binding)
            ?? throw new InvalidOperationException("BuildAction rejected a valid binding.");
        Equal(PluginApi.ActionTypeName, action.Type, "BuildAction must set Type=Plugin.");
        Equal("plugin.contribution", action.Name, "BuildAction must preserve the full action ID.");
        PluginActionRef actionRef = action.PluginActionRef
            ?? throw new InvalidOperationException("BuildAction must create a plugin reference.");
        Equal("plugin", actionRef.PluginId, "BuildAction plugin ID mismatch.");
        Equal("contribution", actionRef.ContributionId, "BuildAction contribution ID mismatch.");
        Dictionary<string, string> actionParameters = action.ExtensionData
            ?? throw new InvalidOperationException("BuildAction must create extension data.");
        Assert(actionParameters.Count == 128,
            "BuildAction must copy all parameters.");
    }

    private static void BindingParameterSnapshotIsIsolated()
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["value"] = "original",
        };
        ActionItem action = PluginInputPanelService.BuildAction(Binding("plugin.contribution", parameters))
            ?? throw new InvalidOperationException("BuildAction rejected a valid binding.");

        parameters["value"] = "source-mutated";
        Dictionary<string, string> actionParameters = action.ExtensionData
            ?? throw new InvalidOperationException("BuildAction must create extension data.");
        Equal("original", actionParameters["value"], "BuildAction must snapshot parameters.");
        Assert(actionParameters.TryGetValue("VALUE", out _),
            "The snapshot must retain case-insensitive parameter lookup.");
        actionParameters["value"] = "snapshot-mutated";
        Equal("source-mutated", parameters["value"], "Mutating the action snapshot must not mutate the binding source.");
    }

    private static void SdkContextStructure()
    {
        string[] baseProperties =
        [
            "Me", "PluginDirectory", "DataDirectory", "Log", "Settings", "Actions", "I18n",
            "Icons", "SettingsPage", "Host", "Commands", "Shell", "Windows", "ScreenCapture",
            "System", "Wheel", "KeyboardRemap", "Info", "Notify", "Events", "Dispatcher",
        ];
        foreach (string name in baseProperties)
        {
            Assert(typeof(IPluginContext).GetProperty(name) != null,
                $"Existing IPluginContext property {name} is missing.");
        }

        Assert(typeof(IPluginContext).GetProperty("InputPanels") == null,
            "InputPanels must be additive and must not appear on the base context.");
        Assert(typeof(IPluginContext).IsAssignableFrom(typeof(IInputPanelPluginContext)),
            "The optional context must derive from IPluginContext.");
        PropertyInfo? inputPanels = typeof(IInputPanelPluginContext).GetProperty("InputPanels");
        Assert(inputPanels != null && inputPanels.PropertyType == typeof(IHostInputPanelService),
            "The optional context must expose IHostInputPanelService.");
        Assert(typeof(IHostInputPanelService).GetMethod("Open") != null,
            "The input panel service structure is missing Open.");
        MethodInfo? resolve = typeof(IHostInputPanelService).GetMethod(
            "ResolveActionAsync",
            [typeof(ConfiguredPluginAction), typeof(CancellationToken)]);
        Assert(resolve != null && resolve.ReturnType == typeof(Task<PluginActionBindingInfo>),
            "The candidate must expose additive Task<PluginActionBindingInfo> ResolveActionAsync.");
        Assert(typeof(IInputPanelSession).GetProperty("IsOpen") != null,
            "The input panel session structure is missing IsOpen.");
        Assert(PluginApi.ApiVersionMinor == 15, "The candidate SDK must expose API minor version 15.");
    }

    private static void ShallowCopyCounterexampleIsRejected()
    {
        ActionItem source = NewAction("mutation-source");
        var shallow = new ActionItem
        {
            Type = source.Type,
            Name = source.Name,
            PluginActionRef = source.PluginActionRef,
            ExtensionData = source.ExtensionData,
        };
        Assert(!HasDeepSnapshot(source, shallow),
            "The safe shallow-copy counterexample must be rejected by the deep-snapshot predicate.");

        ActionItem deep = source.Clone();
        Assert(HasDeepSnapshot(source, deep),
            "Production ActionItem.Clone must satisfy the deep-snapshot predicate.");
    }

    private static void HotkeyAliasesNormalizeAndParse()
    {
        string[] aliases =
        [
            "OemMinus", "OemPlus", "OemOpenBrackets", "OemCloseBrackets", "OemBackslash",
            "OemSemicolon", "OemQuotes", "OemComma", "OemPeriod", "OemQuestion", "OemTilde",
            "Divide", "Multiply", "Add", "Subtract", "Decimal", ",", ";", "Control", "Windows",
        ];

        foreach (string alias in aliases)
        {
            if (string.Equals(alias, "Control", StringComparison.Ordinal) ||
                string.Equals(alias, "Windows", StringComparison.Ordinal))
            {
                AssertHotkeyParses(alias + "+A", 1);
                AssertHotkeyParses("Shift+" + alias + "+A", 2);
                continue;
            }

            AssertHotkeyParses(alias, 0);
            AssertHotkeyParses("Ctrl+" + alias, 1);
            AssertHotkeyParses("Shift+Ctrl+" + alias, 2);
        }

        var legacy = ActionExecutor.ParseHotkey("OemOpenBrackets");
        Assert(legacy.Steps.Count == 1 && legacy.Steps[0].MainKey == 0,
            "The raw OEM token counterexample must remain unparseable without normalization.");
    }

    private static void HotkeyNormalizationRejectsUnsafeShapes()
    {
        string[] invalid =
        [
            "",
            "   ",
            "Ctrl",
            "Ctrl+Shift",
            "Ctr l+A",
            "Ctrl++A",
            "Ctrl+A+B",
            "Ctrl+K,Ctrl+C",
            "Ctrl+Ctrl+A",
            "Ctrl+Control+A",
            "DefinitelyNotAKey",
            new string('x', 257),
        ];

        foreach (string hotkey in invalid)
        {
            Assert(!InputPanelHotkey.TryNormalize(hotkey, out string normalized),
                $"Hotkey '{hotkey}' must be rejected.");
            Equal("", normalized, "Rejected hotkey must not return a normalized value.");
        }
    }

    private static void InvalidTargetRejectsBeforeScheduling()
    {
        int checks = 0;
        var fakeContribution = NewContribution(
            static (_, _) => Task.FromResult(new ActionResult { Success = true, Silent = true }));
        ActionExecutionContext target = NewTarget(
            isValid: () =>
            {
                Interlocked.Increment(ref checks);
                return false;
            },
            context: NewActionContext("invalid-before"));
        PluginInstance instance = NewPluginInstance();

        PluginExecuteOutcome outcome = PluginInvoker.Invoke(
            instance,
            NewRegistration(ActionKind.Sequential, fakeContribution.Contribution),
            EmptyParameters(),
            new PluginCallCoordinator(),
            target);

        Equal(PluginFailureKind.TargetChanged, outcome.Failure, "Invalid pre-target must fail as TargetChanged.");
        Equal(1, checks, "The target must be checked exactly once before scheduling.");
        Equal(0, fakeContribution.Backend.ExecuteCount, "Invalid target must never invoke the contribution.");
        Equal(0, instance.ActiveCallCount, "Pre-scheduler rejection must not retain an invocation lease.");
    }

    private static void SequentialTargetInvalidationSuppressesContribution()
    {
        int checks = 0;
        var fakeContribution = NewContribution(
            static (_, _) => Task.FromResult(new ActionResult { Success = true, Silent = true }));
        ActionExecutionContext target = NewTarget(
            isValid: () => Interlocked.Increment(ref checks) == 1,
            context: NewActionContext("sequential-invalid"));
        PluginInstance instance = NewPluginInstance();

        PluginExecuteOutcome outcome = PluginInvoker.Invoke(
            instance,
            NewRegistration(ActionKind.Sequential, fakeContribution.Contribution),
            EmptyParameters(),
            new PluginCallCoordinator(),
            target);

        Equal(PluginFailureKind.TargetChanged, outcome.Failure,
            "Sequential post-schedule invalidation must fail as TargetChanged.");
        Assert(checks >= 2, "The sequential task must revalidate the target before invoking the contribution.");
        Equal(0, fakeContribution.Backend.ExecuteCount,
            "Invalidated sequential target must suppress the contribution.");
        Equal(0, instance.ActiveCallCount, "Sequential invalidation must release its invocation lease.");
    }

    private static void BackgroundTargetInvalidationSuppressesContribution()
    {
        int checks = 0;
        var secondCheck = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fakeContribution = NewContribution(
            static (_, _) => Task.FromResult(new ActionResult { Success = true, Silent = true }));
        ActionExecutionContext target = NewTarget(
            isValid: () =>
            {
                int count = Interlocked.Increment(ref checks);
                if (count >= 2)
                {
                    secondCheck.TrySetResult(true);
                }
                return count == 1;
            },
            context: NewActionContext("background-invalid"));
        PluginInstance instance = NewPluginInstance();

        PluginExecuteOutcome outcome = PluginInvoker.Invoke(
            instance,
            NewRegistration(ActionKind.Background, fakeContribution.Contribution),
            EmptyParameters(),
            new PluginCallCoordinator(),
            target);

        Assert(outcome.QueuedToBackground, "Background invocation must be scheduled.");
        Wait(secondCheck.Task, "Background target was not revalidated in bounded time.");
        WaitUntil(() => instance.ActiveCallCount == 0, "Background invalidation did not release its lease.");
        Equal(0, fakeContribution.Backend.ExecuteCount,
            "Invalidated background target must suppress the contribution.");
        Equal(0L, instance.InvokeCount, "Target invalidation must not count as a plugin invocation.");
        Equal(0L, instance.InvokeFailureCount, "Target invalidation must not count as a plugin failure.");
    }

    private static void TargetContextSnapshotIsPreserved()
    {
        bool valid = true;
        PluginActionInput? capturedInput = null;
        ActionContext expectedContext = NewActionContext("snapshot");
        var fakeContribution = NewContribution(
            (input, token) =>
            {
                capturedInput = input;
                valid = false;
                return Task.FromException<ActionResult>(new OperationCanceledException(token));
            });
        ActionExecutionContext target = NewTarget(
            isValid: () => valid,
            context: expectedContext);
        PluginInstance instance = NewPluginInstance();

        PluginExecuteOutcome outcome = PluginInvoker.Invoke(
            instance,
            NewRegistration(ActionKind.Sequential, fakeContribution.Contribution),
            EmptyParameters(),
            new PluginCallCoordinator(),
            target);

        Equal(PluginFailureKind.TargetChanged, outcome.Failure,
            "Target invalidation during contribution must fail as TargetChanged.");
        Equal(1, fakeContribution.Backend.ExecuteCount,
            "The contribution must execute once before cancellation.");
        Assert(capturedInput != null, "The contribution must receive PluginActionInput.");
        Assert(ReferenceEquals(expectedContext, capturedInput!.Context),
            "Invoker must pass the session target snapshot, not recollect foreground context.");
        Equal(0, instance.ActiveCallCount, "Contribution cancellation must release its invocation lease.");
        Equal(0L, instance.InvokeCount, "Target cancellation must not increment plugin invocation count.");
        Equal(0L, instance.InvokeFailureCount, "Target cancellation must not increment plugin failure count.");
    }

    private static void SessionCancellationStopsBackgroundWork()
    {
        using var session = new CancellationTokenSource();
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fakeContribution = NewContribution(
            async (_, token) =>
            {
                started.TrySetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                return new ActionResult { Success = true, Silent = true };
            });
        ActionExecutionContext target = NewTarget(
            isValid: () => true,
            context: NewActionContext("session"),
            cancellationToken: session.Token);
        PluginInstance instance = NewPluginInstance();

        PluginExecuteOutcome outcome = PluginInvoker.Invoke(
            instance,
            NewRegistration(ActionKind.Background, fakeContribution.Contribution),
            EmptyParameters(),
            new PluginCallCoordinator(),
            target);

        Assert(outcome.QueuedToBackground, "Session cancellation test must schedule background work.");
        Wait(started.Task, "Background contribution did not start in bounded time.");
        session.Cancel();
        WaitUntil(() => instance.ActiveCallCount == 0, "Session cancellation did not release its lease.");
        Equal(1, fakeContribution.Backend.ExecuteCount,
            "Background contribution must execute once before cancellation.");
        Equal(0L, instance.InvokeCount, "Session cancellation must not increment plugin invocation count.");
        Equal(0L, instance.InvokeFailureCount, "Session cancellation must not increment plugin failure count.");
    }

    private static void BackgroundCapacityHoldsUntilFakeContributionsComplete()
    {
        const int Capacity = InputPanelQueue.MaximumPending;
        var fixtures = new Dictionary<string, FakeInvocationFixture>(StringComparer.Ordinal);
        var contexts = new Dictionary<string, ActionExecutionContext>(StringComparer.Ordinal);
        for (int index = 0; index < Capacity; index++)
        {
            string name = $"background-capacity-{index:D2}";
            fixtures[name] = new FakeInvocationFixture(name, ActionKind.Background, timeoutSeconds: 30);
        }

        InstallFakeInvocationOverride(fixtures);
        try
        {
            InputPanelQueue queue = CreateDeferredQueue(contexts, "background-capacity");
            for (int index = 0; index < Capacity; index++)
            {
                string name = $"background-capacity-{index:D2}";
                Equal(InputPanelQueueResult.Accepted, queue.Queue(NewAction(name)),
                    $"Deferred background action {index} must be accepted.");
            }

            Equal(Capacity, queue.Pending, "All 64 live background actions must retain a queue slot.");
            Equal(InputPanelQueueResult.Busy, queue.Queue(NewAction("background-capacity-overflow")),
                "The 65th live background action must observe Busy.");
            WaitAll(fixtures.Values.Select(fixture => (Task)fixture.Started.Task),
                "Not all fake background contributions started in bounded time.");

            Assert(fixtures.Values.All(fixture => fixture.Backend.ExecuteCount == 1),
                "Each accepted fake background contribution must start exactly once.");
            Assert(contexts.Values.All(context => context.IsConsumptionDeferred),
                "Every live background context must defer consumption before scheduling.");

            string[] names = fixtures.Keys.ToArray();
            foreach (string name in names.Take(Capacity - 1))
            {
                fixtures[name].Complete();
            }

            WaitUntil(() => queue.Pending == 1,
                "Completing 63 fake background actions must leave exactly one held queue slot.");
            fixtures[names[^1]].Complete();
            WaitUntil(() => queue.Pending == 0,
                "The final fake background completion must release the last queue slot.");

            foreach (ActionExecutionContext context in contexts.Values)
            {
                context.OnConsumed?.Invoke();
            }
            Equal(0, queue.Pending, "Repeated OnConsumed callbacks must not release queue capacity twice.");
            Equal(0, fixtures.Values.Sum(fixture => fixture.Instance.ActiveCallCount),
                "Every fake background contribution must release its invocation lease.");
        }
        finally
        {
            foreach (FakeInvocationFixture fixture in fixtures.Values)
            {
                fixture.Complete();
            }
            _ = SpinWait.SpinUntil(
                () => fixtures.Values.All(fixture => fixture.Instance.ActiveCallCount == 0),
                TimeSpan.FromSeconds(10));
            ActionExecutor.ResetTestSeams();
        }
    }

    private static void SequentialTimeoutRetainsCapacityUntilCompletion()
    {
        const string Name = "sequential-timeout";
        var fixture = new FakeInvocationFixture(Name, ActionKind.Sequential, timeoutSeconds: 1);
        var fixtures = new Dictionary<string, FakeInvocationFixture>(StringComparer.Ordinal)
        {
            [Name] = fixture,
        };
        var contexts = new Dictionary<string, ActionExecutionContext>(StringComparer.Ordinal);
        InstallFakeInvocationOverride(fixtures);
        try
        {
            InputPanelQueue queue = CreateDeferredQueue(contexts, "sequential-timeout");
            Task<InputPanelQueueResult> queueTask = Task.Run(() => queue.Queue(NewAction(Name)));
            Wait(fixture.Started.Task, "The sequential fake contribution did not start in bounded time.");
            Wait(fixture.OutcomeReady.Task, "The sequential timeout outcome was not produced in bounded time.");

            Equal(InputPanelQueueResult.Accepted, queueTask.GetAwaiter().GetResult(),
                "The timed-out sequential fake action must have been accepted.");
            Assert(contexts[Name].IsConsumptionDeferred,
                "Sequential timeout must defer consumption while the real task remains active.");
            Equal(PluginFailureKind.PluginFailed, fixture.OutcomeReady.Task.Result.Failure,
                "The sequential timeout must still report a plugin timeout outcome.");
            Equal(1, queue.Pending, "Sequential timeout must retain the queue slot until the contribution ends.");

            fixture.Complete();
            WaitUntil(() => queue.Pending == 0,
                "The real sequential contribution completion must release the retained queue slot.");
            contexts[Name].OnConsumed?.Invoke();
            Equal(0, queue.Pending, "Sequential completion must release the queue slot exactly once.");
            Equal(0, fixture.Instance.ActiveCallCount, "Sequential completion must release its invocation lease.");
        }
        finally
        {
            fixture.Complete();
            _ = SpinWait.SpinUntil(
                () => fixture.Instance.ActiveCallCount == 0,
                TimeSpan.FromSeconds(10));
            ActionExecutor.ResetTestSeams();
        }
    }

    private static void BackgroundTimeoutRetainsCapacityUntilCompletion()
    {
        const string Name = "background-timeout";
        var fixture = new FakeInvocationFixture(Name, ActionKind.Background, timeoutSeconds: 1);
        var fixtures = new Dictionary<string, FakeInvocationFixture>(StringComparer.Ordinal)
        {
            [Name] = fixture,
        };
        var contexts = new Dictionary<string, ActionExecutionContext>(StringComparer.Ordinal);
        InstallFakeInvocationOverride(fixtures);
        try
        {
            InputPanelQueue queue = CreateDeferredQueue(contexts, "background-timeout");
            Equal(InputPanelQueueResult.Accepted, queue.Queue(NewAction(Name)),
                "The background fake action must be accepted.");
            Wait(fixture.OutcomeReady.Task, "The background scheduling outcome was not produced in bounded time.");
            Assert(fixture.OutcomeReady.Task.Result.QueuedToBackground,
                "The fake invocation must schedule background work.");
            WaitUntil(() => fixture.Instance.InvokeFailureCount == 1,
                "The fake background timeout was not recorded in bounded time.");

            Assert(contexts[Name].IsConsumptionDeferred,
                "Background timeout must defer consumption while the real task remains active.");
            Equal(1, queue.Pending, "Background timeout must retain the queue slot until the contribution ends.");

            fixture.Complete();
            WaitUntil(() => queue.Pending == 0,
                "The real background contribution completion must release the retained queue slot.");
            contexts[Name].OnConsumed?.Invoke();
            Equal(0, queue.Pending, "Background completion must release the queue slot exactly once.");
            Equal(0, fixture.Instance.ActiveCallCount, "Background completion must release its invocation lease.");
        }
        finally
        {
            fixture.Complete();
            _ = SpinWait.SpinUntil(
                () => fixture.Instance.ActiveCallCount == 0,
                TimeSpan.FromSeconds(10));
            ActionExecutor.ResetTestSeams();
        }
    }

    private static void DescribeRequiresRegistrationAndResolveIsAdditive()
    {
        PluginActionBindingInfo info = PluginInputPanelService.Describe(
            "fixture-owner",
            Binding("unregistered-plugin.missing-action"));
        Assert(!info.IsAvailable,
            "Describe must report an unregistered action as unavailable without loading it.");
        Equal("unregistered-plugin.missing-action", info.DisplayName,
            "Unregistered action display must fall back to its binding ID.");
        Assert(!string.IsNullOrWhiteSpace(info.UnavailableReason),
            "Unavailable Describe result must include a reason.");
    }

    private static void PrivateHotkeyExecutionUsesFakeAndSuppressesLogs()
    {
        string startMarker = BeginAppLogSegment();

        var captured = new List<ActionExecutor.HotkeyDetails>();
        ActionExecutor.InputPanelHotkeyExecutionOverride = captured.Add;
        try
        {
            string[] validInputs = ["Q", "7", "Ctrl+Shift+K"];
            foreach (string value in validInputs)
            {
                Assert(ActionExecutor.InputPanelHotkeyExecutionOverride != null,
                    "Private hotkey fake seam must be installed before execution.");
                ActionExecutor.ExecuteHotkey(value, privateInput: true);
            }

            Equal(validInputs.Length, captured.Count,
                "Every valid private hotkey must reach the fake execution seam.");
            Equal((ushort)81, captured[0].MainKey, "Letter Q must parse to VK 81.");
            Equal((ushort)55, captured[1].MainKey, "Digit 7 must parse to VK 55.");
            Equal((ushort)75, captured[2].MainKey, "Letter K must parse to VK 75.");
            Equal(2, captured[2].Modifiers.Count, "Ctrl+Shift+K must retain two modifiers.");
            Assert(captured[2].Modifiers.Contains((ushort)162) &&
                   captured[2].Modifiers.Contains((ushort)160),
                "Ctrl+Shift+K must retain Ctrl and Shift modifiers.");

            int beforeInvalid = captured.Count;
            string[] invalidInputs = ["", "Ctrl+Shift", "Ctrl+K,Ctrl+C", "Ctrl++A", "DefinitelyNotAKey"];
            foreach (string value in invalidInputs)
            {
                Assert(ActionExecutor.InputPanelHotkeyExecutionOverride != null,
                    "Private hotkey fake seam must remain installed before invalid-input checks.");
                ActionExecutor.ExecuteHotkey(value, privateInput: true);
            }

            Equal(beforeInvalid, captured.Count,
                "Invalid private hotkey input must not reach the fake execution seam.");
        }
        finally
        {
            ActionExecutor.ResetTestSeams();
            Assert(ActionExecutor.InputPanelHotkeyExecutionOverride == null,
                "ResetTestSeams must clear the private hotkey execution override.");
            FlushAppLoggerQueue();
        }

        string privateInputSegment = ReadAppLogSegment(startMarker);

        string[] forbidden =
        [
            "Executing Hotkey:",
            "Executing Key Sequence",
            "Executing Multi-Step Hotkey:",
            "Executing Text Input:",
            "MainKey:",
            "Modifiers:",
            "'Q'",
            "'7'",
            "'Ctrl+Shift+K'",
            "Error in ExecuteSingleStep",
        ];
        foreach (string value in forbidden)
        {
            Assert(!privateInputSegment.Contains(value, StringComparison.Ordinal),
                $"Private input leaked value-bearing log text: {value}");
        }
    }

    private static void PrivateHotkeyExceptionLoggingSuppressesSensitiveValues()
    {
        string logMarker = BeginAppLogSegment();
        string actionSentinel = "PRIVATE_ACTION_SENTINEL_" + Guid.NewGuid().ToString("N");
        string exceptionSentinel = "PRIVATE_EXCEPTION_SENTINEL_" + Guid.NewGuid().ToString("N");
        const string Hotkey = "Ctrl+Shift+K";
        int fakeCalls = 0;
        var action = new ActionItem
        {
            Type = "Hotkey",
            Name = actionSentinel,
            Parameter = Hotkey,
        };
        ActionExecutionContext context = NewTarget(
            isValid: () => true,
            context: NewActionContext("private-exception"));
        MethodInfo executeCore = typeof(ActionExecutor).GetMethod(
            "ExecuteCore",
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(ActionItem), typeof(ActionExecutionContext)],
            modifiers: null)
            ?? throw new InvalidOperationException("ActionExecutor.ExecuteCore reflection seam is missing.");

        ActionExecutor.InputPanelHotkeyExecutionOverride = _ =>
        {
            fakeCalls++;
            throw new InvalidOperationException(
                $"{exceptionSentinel} hotkey={Hotkey} name={actionSentinel}");
        };
        ActionExecutor.ActionExecutionOverride = (_, _) =>
            throw new InvalidOperationException(
                $"{exceptionSentinel} name={actionSentinel} parameter={Hotkey}");
        try
        {
            Assert(ActionExecutor.InputPanelHotkeyExecutionOverride != null,
                "Private hotkey fake seam must be installed before reflection execution.");
            executeCore.Invoke(null, [action, context]);
            Equal(1, fakeCalls, "The private hotkey exception fixture must reach the fake once.");

            Assert(!ActionExecutor.ProcessActionEnvelope(new ActionEnvelope(action, context)),
                "A plugin-target envelope exception must be isolated and return false.");
        }
        finally
        {
            ActionExecutor.ResetTestSeams();
            Assert(ActionExecutor.InputPanelHotkeyExecutionOverride == null &&
                   ActionExecutor.ActionExecutionOverride == null,
                "ResetTestSeams must clear both exception-fixture overrides.");
            FlushAppLoggerQueue();
        }

        // A background writer may have drained the queue but not acquired FileLock yet.
        WaitUntil(() =>
        {
            string written = ReadAppLogSegment(logMarker);
            return written.Contains("Input panel action failed (InvalidOperationException).", StringComparison.Ordinal) &&
                written.Contains("Input panel action execution failed (InvalidOperationException).", StringComparison.Ordinal);
        }, "Both sanitized exception entries must reach the asynchronous log before inspection.");
        string segment = ReadAppLogSegment(logMarker);
        Assert(segment.Contains("Input panel action failed (InvalidOperationException).", StringComparison.Ordinal),
            "ExecuteCore must retain only the sanitized exception type.");
        Assert(segment.Contains(
                "Input panel action execution failed (InvalidOperationException).",
                StringComparison.Ordinal),
            "ProcessActionEnvelope must retain only the sanitized exception type.");

        string[] forbidden =
        [
            actionSentinel,
            exceptionSentinel,
            Hotkey,
            "Failed to execute action",
            "Param=",
            "parameter=",
            "hotkey=",
            "name=",
        ];
        foreach (string value in forbidden)
        {
            Assert(!segment.Contains(value, StringComparison.Ordinal),
                $"Private exception path leaked sensitive log text: {value}");
        }
    }

    private static void AssertHotkeyParses(string value, int expectedModifiers)
    {
        Assert(InputPanelHotkey.TryNormalize(value, out string normalized),
            $"Hotkey '{value}' must normalize.");
        var parsed = ActionExecutor.ParseHotkey(normalized);
        Assert(parsed.Steps.Count == 1, $"Hotkey '{value}' must parse to exactly one step.");
        Assert(parsed.Steps[0].MainKey != 0, $"Hotkey '{value}' must parse a non-zero main key.");
        Assert(parsed.SequenceKeys.Count == 1, $"Hotkey '{value}' must parse to exactly one sequence key.");
        Equal(expectedModifiers, parsed.Steps[0].Modifiers.Count,
            $"Hotkey '{value}' modifier count mismatch.");
    }

    private static PluginInstance NewPluginInstance(string pluginId = "fixture")
    {
        var instance = new PluginInstance(
            pluginId,
            new PluginRegistryEntry
            {
                Id = pluginId,
                Enabled = true,
            },
            new PluginScanResult());
        typeof(PluginInstance)
            .GetMethod("OpenInvocationGate", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(instance, null);
        return instance;
    }

    private static PluginActionRegistration NewRegistration(
        ActionKind kind,
        IActionContribution contribution,
        int timeoutSeconds = 3,
        string fullId = "fixture.contribution",
        string displayName = "Fixture contribution")
    {
        var registration =
            (PluginActionRegistration)RuntimeHelpers.GetUninitializedObject(typeof(PluginActionRegistration));
        SetReflectedValue(registration, "Contribution", contribution);
        SetReflectedValue(registration, "Kind", kind);
        SetReflectedValue(registration, "FullId", fullId);
        SetReflectedValue(registration, "DisplayName", displayName);
        SetReflectedValue(registration, "TimeoutSeconds", timeoutSeconds);
        return registration;
    }

    private static void SetReflectedValue(object target, string memberName, object value)
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        for (Type? type = target.GetType(); type != null; type = type.BaseType)
        {
            FieldInfo? field = type.GetField($"<{memberName}>k__BackingField", Flags);
            if (field != null)
            {
                field.SetValue(target, value);
                return;
            }
        }

        PropertyInfo? property = target.GetType().GetProperty(memberName, Flags);
        if (property?.SetMethod == null)
        {
            throw new InvalidOperationException($"Reflection fixture cannot set {memberName}.");
        }
        property.SetValue(target, value);
    }

    private static (IActionContribution Contribution, FakeActionContributionProxy Backend) NewContribution(
        Func<PluginActionInput, CancellationToken, Task<ActionResult>> handler)
    {
        var proxy = DispatchProxy.Create<IActionContribution, FakeActionContributionProxy>();
        var contribution = (FakeActionContributionProxy)(object)proxy;
        contribution.ExecuteHandler = handler;
        return (proxy, contribution);
    }

    private static ActionExecutionContext NewTarget(
        Func<bool> isValid,
        ActionContext context,
        CancellationToken cancellationToken = default)
    {
        return new ActionExecutionContext(
            targetHwnd: 0,
            targetProcessId: 0,
            targetProcessName: "fixture",
            isValidCallback: isValid,
            onConsumed: null,
            pluginTargetContext: context,
            sessionCancellation: cancellationToken);
    }

    private static ActionContext NewActionContext(string marker)
    {
        return new ActionContext
        {
            ForegroundProcessName = "fixture-" + marker,
            ForegroundWindowTitle = marker,
            ForegroundWindowHandle = 1,
            CursorX = 2,
            CursorY = 3,
            IsElevated = false,
            LanguageCode = "en-US",
        };
    }

    private static IReadOnlyDictionary<string, string> EmptyParameters() =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static InputPanelQueue CreateDeferredQueue(
        Dictionary<string, ActionExecutionContext> contexts,
        string marker)
    {
        return new InputPanelQueue(
            0,
            0,
            "fixture",
            () => true,
            (action, context) =>
            {
                contexts[action.Name] = context;
                Assert(ActionExecutor.ProcessActionEnvelope(new ActionEnvelope(action, context)),
                    $"Production action processing rejected fake action '{action.Name}'.");
            },
            NewActionContext(marker));
    }

    private static void InstallFakeInvocationOverride(
        IReadOnlyDictionary<string, FakeInvocationFixture> fixtures)
    {
        ActionExecutor.ActionExecutionOverride = (action, context) =>
        {
            FakeInvocationFixture fixture = fixtures[action.Name]
                ?? throw new InvalidOperationException($"No fake invocation fixture for '{action.Name}'.");
            Assert(context != null, "Fake plugin action execution requires an input-panel context.");
            PluginExecuteOutcome outcome = PluginInvoker.Invoke(
                fixture.Instance,
                fixture.Registration,
                EmptyParameters(),
                new PluginCallCoordinator(),
                context);
            fixture.OutcomeReady.TrySetResult(outcome);
        };
    }

    private static void Wait(Task task, string message)
    {
        if (!task.Wait(TimeSpan.FromSeconds(5)))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void WaitAll(IEnumerable<Task> tasks, string message)
    {
        if (!Task.WaitAll(tasks.ToArray(), TimeSpan.FromSeconds(10)))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void WaitUntil(Func<bool> condition, string message)
    {
        if (!SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(5)))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void FlushAppLoggerQueue()
    {
        MethodInfo flush = typeof(AppLogger).GetMethod(
            "FlushQueueToFile",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("AppLogger private flush method is missing.");
        flush.Invoke(null, null);
    }

    private static string BeginAppLogSegment()
    {
        FieldInfo running = typeof(AppLogger).GetField(
            "_isRunning",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("AppLogger _isRunning field is missing.");
        Assert(running.GetValue(null) is true,
            "AppLogger writer must still be running for the isolated log-delta fixture.");
        string marker = "PRIVATE_INPUT_LOG_START_" + Guid.NewGuid().ToString("N");
        AppLogger.LogInfo(marker);
        FlushAppLoggerQueue();
        WaitUntil(
            () => ReadAppLogFile().Contains(marker, StringComparison.Ordinal),
            "The isolated AppLogger start marker did not reach the log file.");
        return marker;
    }

    private static string ReadAppLogSegment(string startMarker)
    {
        FlushAppLoggerQueue();
        string log = ReadAppLogFile();
        int start = log.IndexOf(startMarker, StringComparison.Ordinal);
        Assert(start >= 0, "The isolated AppLogger start marker must remain present.");
        return log[(start + startMarker.Length)..];
    }

    private static string ReadAppLogFile()
    {
        object fileLock = typeof(AppLogger).GetField("FileLock", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        // The fixture must not open a read handle that denies the live writer's append.
        lock (fileLock)
        {
            string path = AppLogger.GetTodayLogFilePath();
            return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : "";
        }
    }

    private static bool HasDeepSnapshot(ActionItem source, ActionItem snapshot)
    {
        if (source.PluginActionRef == null || source.ExtensionData == null)
        {
            return false;
        }

        if (snapshot.PluginActionRef == null || snapshot.ExtensionData == null)
        {
            return false;
        }

        if (ReferenceEquals(source.PluginActionRef, snapshot.PluginActionRef) ||
            ReferenceEquals(source.ExtensionData, snapshot.ExtensionData))
        {
            return false;
        }

        return string.Equals(source.PluginActionRef.PluginId, snapshot.PluginActionRef.PluginId, StringComparison.Ordinal) &&
            string.Equals(source.PluginActionRef.ContributionId, snapshot.PluginActionRef.ContributionId, StringComparison.Ordinal) &&
            source.ExtensionData.Count == snapshot.ExtensionData.Count &&
            source.ExtensionData.All(pair =>
                snapshot.ExtensionData.TryGetValue(pair.Key, out string? value) &&
                string.Equals(pair.Value, value, StringComparison.Ordinal));
    }

    private static InputPanelQueue CreateQueue(
        List<(ActionItem Item, ActionExecutionContext Context)> captured,
        Func<bool> isValid)
    {
        return new InputPanelQueue(
            0,
            0,
            "fixture",
            isValid,
            (item, context) => captured.Add((item, context)));
    }

    private static ActionItem NewAction(string name)
    {
        return new ActionItem
        {
            Type = "Plugin",
            Name = name,
            PluginActionRef = new PluginActionRef
            {
                PluginId = "plugin",
                ContributionId = name + "-contribution",
            },
            ExtensionData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["value"] = "original",
            },
        };
    }

    private static ConfiguredPluginAction Binding(
        string actionId,
        Dictionary<string, string>? parameters = null)
    {
        return new ConfiguredPluginAction
        {
            ActionId = actionId,
            Parameters = parameters ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        };
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} Expected: {expected}; actual: {actual}.");
        }
    }

    private static void Throws<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"{message} Expected {typeof(TException).Name}.");
    }

    private sealed class FakeInvocationFixture
    {
        public FakeInvocationFixture(string id, ActionKind kind, int timeoutSeconds)
        {
            Instance = NewPluginInstance(id);
            var contribution = NewContribution(
                (_, _) =>
                {
                    Started.TrySetResult(true);
                    return Completion.Task;
                });
            Backend = contribution.Backend;
            Registration = NewRegistration(
                kind,
                contribution.Contribution,
                timeoutSeconds,
                id + ".contribution",
                "Fake " + id);
        }

        public PluginInstance Instance { get; }
        public PluginActionRegistration Registration { get; }
        public FakeActionContributionProxy Backend { get; }
        public TaskCompletionSource<bool> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<PluginExecuteOutcome> OutcomeReady { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<ActionResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Complete() =>
            Completion.TrySetResult(new ActionResult { Success = true, Silent = true });
    }
}

public class FakeActionContributionProxy : DispatchProxy
{
    public Func<PluginActionInput, CancellationToken, Task<ActionResult>> ExecuteHandler { get; set; } =
        static (_, _) => Task.FromException<ActionResult>(
            new InvalidOperationException("Fake contribution handler was not configured."));

    public int ExecuteCount { get; private set; }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == nameof(IActionContribution.ExecuteAsync))
        {
            ExecuteCount++;
            return ExecuteHandler(
                (PluginActionInput)args![0]!,
                (CancellationToken)args[1]!);
        }

        throw new MissingMethodException(
            $"Fake contribution does not implement {targetMethod?.Name ?? "(unknown)"}.");
    }
}
