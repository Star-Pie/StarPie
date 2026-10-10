using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using StarPie.Plugin;
using WinPieGestures.Plugins;

internal static class Program
{
    private static int _passed, _failed;
    [ThreadStatic] private static bool _publishing;
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Check(bool condition, string name)
    { Console.WriteLine($"[{(condition ? "PASS" : "FAIL")}] {name}"); if (condition) Interlocked.Increment(ref _passed); else Interlocked.Increment(ref _failed); }
    private static void Throws(Action action, string name)
    { try { action(); Check(false, name); } catch (PluginContractException) { Check(true, name); } }
    private static Task Wait(Task task) => task.WaitAsync(TimeSpan.FromSeconds(8));
    private static TaskCompletionSource<bool> Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static InteractionEvent Event(InteractionEventKind kind, long sequence, long session = 500) =>
        new(kind, session, sequence, InteractionSource.NormalGesture, "Global", new(InteractionTargetKind.Sector, 1));
    private static int Publish(PluginRuntime runtime, InteractionEvent input)
    { _publishing = true; try { return runtime.PublishInteractionEvent(input); } finally { _publishing = false; } }

    private sealed class Recorder : IInteractionContribution
    {
        internal readonly List<InteractionEvent> Seen = new();
        internal Func<InteractionEvent, CancellationToken, ValueTask>? Handler;
        internal int DescriptorReads;
        internal bool RanOnPublisher;
        internal string Id = "record";
        internal InteractionEventKind Filter = InteractionEventKind.All;
        public InteractionDescriptor Descriptor { get { DescriptorReads++; return new() { Id = Id, Events = Filter }; } }
        public async ValueTask OnInteractionAsync(InteractionEvent input, CancellationToken cancellationToken)
        {
            RanOnPublisher |= _publishing;
            lock (Seen) Seen.Add(input);
            if (Handler != null) await Handler(input, cancellationToken);
        }
    }

    private sealed class Rig : IDisposable
    {
        internal readonly PluginCatalog Catalog = new();
        internal readonly Dictionary<string, PluginInstance> Owners = new();
        internal readonly PluginRuntime Runtime;
        internal bool Enabled = true;
        internal Rig(int capacity = 128) => Runtime = new(Catalog, id => Owners.GetValueOrDefault(id), () => Enabled, capacity);
        internal PluginInstance Owner(string id)
        {
            var owner = new PluginInstance(id, new PluginRegistryEntry { Id = id, Enabled = true }, new PluginScanResult());
            typeof(PluginInstance).GetMethod("OpenInvocationGate", PrivateInstance)!.Invoke(owner, null);
            typeof(PluginInstance).GetMethod("SetState", PrivateInstance)!.Invoke(owner, new object[] { PluginRuntimeState.Active });
            Owners[id] = owner;
            return owner;
        }
        internal IDisposable Add(PluginInstance owner, Recorder recorder)
        {
            var session = Catalog.BeginSession(owner.PluginId);
            var registry = new PluginInteractionRegistry(session, owner, Catalog);
            var token = registry.Register(recorder);
            Check(session.Commit(out _), "transaction commits " + owner.PluginId);
            return token;
        }
        internal Task Stop(PluginInstance owner)
        {
            var drained = owner.BeginStopping();
            Catalog.RevokeAll(owner.PluginId);
            Runtime.Interactions.OnPluginStopping(owner.PluginId);
            return drained;
        }
        public void Dispose() { foreach (var owner in Owners.Values) _ = Stop(owner); }
    }

    private static void TestSessions()
    {
        var seen = new List<InteractionEvent>();
        var normal = new PluginInteractionSession(InteractionSource.NormalGesture, "CAD", e => { seen.Add(e); return 1; });
        var sticky = new PluginInteractionSession(InteractionSource.StickyWheel, "CAD", _ => 0);
        Check(normal.SessionId != sticky.SessionId, "IDs unique across wheel sources");
        normal.Update(-1, -1, false, false);
        Check(seen.Count == 0, "pre-presentation changes are cached, not presented");
        normal.Presented(); normal.Presented();
        normal.Update(2, -1, false, false);
        int count = seen.Count;
        normal.Update(2, -1, false, false);
        Check(seen.Count == count, "stable target does not repeat events");
        normal.Update(2, -1, true, false);
        Check(seen[^1].Kind == InteractionEventKind.SelectionChanged && seen[^1].Target == InteractionTarget.None, "submenu without child selection clears hover target");
        normal.Update(2, 1, true, false);
        normal.Update(2, -1, false, false);
        normal.Commit(new(InteractionTargetKind.Sector, 2)); normal.Dispose(); normal.Dispose();
        normal.Update(5, -1, false, false); normal.Cancel("late"); normal.Commit(InteractionTarget.Core);
        Check(seen.Count(e => e.Kind == InteractionEventKind.Presented) == 1, "Presented at most once");
        Check(seen.Count(e => e.Kind == InteractionEventKind.SessionEnded) == 1, "End at most once; no late events");
        Check(seen[^2].Kind == InteractionEventKind.ActionCommitted && seen[^1].Kind == InteractionEventKind.SessionEnded,
            "confirmation precedes End even when UI closes first");
        Check(seen[^1].Reason == "ActionCommitted", "End preserves terminal outcome");
        Check(seen.Select(e => e.Sequence).SequenceEqual(Enumerable.Range(1, seen.Count).Select(i => (long)i)), "source sequences increase");
        Check(seen.All(e => e.Source == InteractionSource.NormalGesture && e.ProfileId == "CAD"), "source and profile captured");
        Check(seen[1].Target == InteractionTarget.Core && seen[2].Target.Sector == 2, "old target snapshots stay immutable");
        Check(seen.Any(e => e.Kind == InteractionEventKind.NavigationCancelled && e.Reason == "SubmenuCollapsed"), "submenu back is not session cancellation");
        Check(PluginInteractionSession.Target(-2, -1).Kind == InteractionTargetKind.None &&
              PluginInteractionSession.Target(-1, -1).Kind == InteractionTargetKind.Core, "None and Core distinguished");
        Check(PluginInteractionSession.Target(3, 4).Kind == InteractionTargetKind.SubSector, "subsector identity preserved");
        var cancelled = new PluginInteractionSession(InteractionSource.StickyWheel, "Other", e => { seen.Add(e); return 0; });
        cancelled.Presented(); cancelled.Commit(InteractionTarget.None); cancelled.End();
        Check(seen[^2].Kind == InteractionEventKind.SessionCancelled && seen[^1].Reason == "NoTarget", "no target cannot confirm action");
        var pending = new PluginInteractionSession(InteractionSource.StickyWheel, "", e => { seen.Add(e); return 0; });
        pending.BindProfile("Chosen"); pending.Presented(); pending.BindProfile("late"); pending.End();
        Check(seen[^1].ProfileId == "Chosen", "profile binds before first event and remains stable afterward");
        var failedBeforeBinding = new PluginInteractionSession(InteractionSource.StickyWheel, "", e => { seen.Add(e); return 0; });
        failedBeforeBinding.End("PresentationFailed");
        Check(seen[^1].Reason == "PresentationFailed" && seen[^1].ProfileId == "", "accepted request can end before profile or UI preparation");        var rejected = new PluginInteractionSession(InteractionSource.StickyWheel, "Other", e => { seen.Add(e); return 0; });
        int offset = seen.Count; rejected.End("PresentationFailed"); rejected.Presented();
        Check(seen.Count == offset + 1 && seen[^1].Kind == InteractionEventKind.SessionEnded, "failed presentation has no Presented");
    }

    private static void TestConfiguredConfirmation()
    {
        var seen = new List<InteractionEvent>();
        PluginInteractionSession New() => new(InteractionSource.NormalGesture, "Global", e => { seen.Add(e); return 0; });
        using (var empty = New()) empty.Confirm(new WinPieGestures.ActionItem(), InteractionTarget.Core);
        Check(!seen.Any(e => e.Kind == InteractionEventKind.ActionCommitted), "default empty action cannot confirm");
        using (var named = New()) named.Confirm(new WinPieGestures.ActionItem { Type = "Hotkey", Name = "label", Parameter = "" }, InteractionTarget.Core);
        Check(!seen.Any(e => e.Kind == InteractionEventKind.ActionCommitted), "icon/name alone is not executable hotkey");
        using (var plugin = New()) plugin.Confirm(new WinPieGestures.ActionItem { Type = "Plugin" }, InteractionTarget.Core);
        Check(!seen.Any(e => e.Kind == InteractionEventKind.ActionCommitted), "missing plugin action reference cannot confirm");
        using (var malformed = New()) malformed.Confirm(new WinPieGestures.ActionItem { Type = null! }, InteractionTarget.Core);
        Check(!seen.Any(e => e.Kind == InteractionEventKind.ActionCommitted), "null legacy type safely cancels");
        using (var valid = New()) valid.Confirm(new WinPieGestures.ActionItem { Type = "Hotkey", Parameter = "Ctrl+C" }, InteractionTarget.Core);
        Check(seen[^2].Kind == InteractionEventKind.ActionCommitted, "configured action confirms without executing it");
        using (var quick = New()) quick.Confirm(new WinPieGestures.ActionItem { Type = "Hotkey", Parameter = "Ctrl+C" }, new(InteractionTargetKind.Sector, 2));
        Check(seen[^2].Kind == InteractionEventKind.ActionCommitted, "quick release can confirm without a Presented event");
        var superseded = New(); superseded.Cancel("Superseded"); superseded.Dispose();
        Check(seen[^2].Kind == InteractionEventKind.SessionCancelled && seen[^1].Reason == "Superseded", "replacement reason preserved through completion");
    }
    private static void TestRenderCompletion()
    {
        bool visible = false, disposed = false, presented = true;
        long current = 42, requested = 42;
        var seen = new List<InteractionEvent>();
        var interaction = new PluginInteractionSession(InteractionSource.NormalGesture, "Render", e =>
        {
            if (e.Kind == InteractionEventKind.Presented) Check(visible, "Presented follows real reveal action");
            seen.Add(e); return 1;
        });
        Func<bool> guard = () => WinPieGestures.WheelPresentationCompletion.IsCurrent(disposed, presented, current, requested);
        Action render = () => WinPieGestures.WheelPresentationCompletion.TryComplete(guard, () => visible = true, interaction.Presented);
        Check(seen.Count == 0 && !visible, "scheduling Render does not report Presented");
        render();
        Check(visible && seen.Count(e => e.Kind == InteractionEventKind.Presented) == 1, "valid Render completes and notifies");
        render();
        Check(seen.Count(e => e.Kind == InteractionEventKind.Presented) == 1, "duplicate completion is harmless to event session");
        visible = false; current++;
        int before = seen.Count; render();
        Check(!visible && seen.Count == before, "stale presentation version cannot reveal or notify");
        current = requested; disposed = true; render();
        Check(!visible && seen.Count == before, "disposed Render cannot reveal or notify");
        disposed = false; presented = false; render();
        Check(!visible && seen.Count == before, "dismissed Render cannot reveal or notify");
        presented = true; bool notified = false;
        bool succeeded = WinPieGestures.WheelPresentationCompletion.TryComplete(guard, () => current++, () => notified = true);
        Check(!succeeded && !notified, "reentrant reveal invalidation cannot report completion");
        current = requested;
        try { WinPieGestures.WheelPresentationCompletion.TryComplete(guard, () => throw new InvalidOperationException("sentinel reveal failure"), () => notified = true); }
        catch (InvalidOperationException) { }
        Check(!notified, "failed reveal cannot notify Presented");
        var closeBeforeRender = new PluginInteractionSession(InteractionSource.StickyWheel, "Render", e => { seen.Add(e); return 1; });
        closeBeforeRender.End("DismissedByPlugin"); before = seen.Count;
        WinPieGestures.WheelPresentationCompletion.TryComplete(guard, () => visible = true, closeBeforeRender.Presented);
        Check(seen.Count == before, "ended event session ignores late successful visual completion");
    }

    private static async Task TestFrozenEndPublishing()
    {
        var entered = Gate(); var release = Gate(); int ends = 0; string reason = "";
        var interaction = new PluginInteractionSession(InteractionSource.StickyWheel, "Freeze", e =>
        {
            ends++; reason = e.Reason; entered.TrySetResult(true); release.Task.GetAwaiter().GetResult(); return 1;
        });
        interaction.FreezeEnd("DismissedByPlugin");
        Check(ends == 0, "freezing outcome inside state lock never publishes");
        var publisher = Task.Run(() => interaction.End("SupersededBeforePresentation"));
        await Wait(entered.Task);
        try
        {
            var contender = Task.Run(() => { interaction.Presented(); interaction.Update(1, -1, false, false); interaction.End("late"); });
            await Wait(contender);
            Check(ends == 1 && reason == "DismissedByPlugin", "concurrent End preserves frozen reason and publishes once");
        }
        finally { release.TrySetResult(true); await Wait(publisher); }
    }
    private static async Task TestRegistration()
    {
        using var rig = new Rig(); var owner = rig.Owner("test.transaction"); var recorder = new Recorder();
        var session = rig.Catalog.BeginSession(owner.PluginId);
        var registry = new PluginInteractionRegistry(session, owner, rig.Catalog);
        var token = registry.Register(recorder);
        Check(Publish(rig.Runtime, Event(InteractionEventKind.Presented, 1)) == 0, "staged contribution invisible");
        Throws(() => registry.Register(new Recorder()), "duplicate ID rejected");
        Throws(() => registry.Register(new Recorder { Id = "1bad" }), "invalid ID rejected");
        Throws(() => registry.Register(new Recorder { Id = "badFilter", Filter = InteractionEventKind.None }), "empty filter rejected");
        Check(session.Commit(out _), "Initialize success commits");
        Throws(() => registry.Register(new Recorder { Id = "late" }), "post-Initialize registration rejected");
        Check(Publish(rig.Runtime, Event(InteractionEventKind.Presented, 1)) == 1, "typed runtime routes registered contribution");
        await Wait(rig.Runtime.Interactions.FindQueue(owner.PluginId)!.Completion);
        Check(recorder.Seen.Count == 1 && recorder.DescriptorReads == 1, "Publish does not access plugin metadata");
        Check(!recorder.RanOnPublisher, "plugin callback not on publishing stack");
        token.Dispose(); token.Dispose();
        Check(Publish(rig.Runtime, Event(InteractionEventKind.SessionEnded, 2)) == 0, "token disposal idempotently revokes");
        var rejected = rig.Catalog.BeginSession(owner.PluginId);
        new PluginInteractionRegistry(rejected, owner, rig.Catalog).Register(new Recorder { Id = "rejected" });
        rejected.Report("Initialize failure"); Check(!rejected.Commit(out _), "failed transaction rejected"); rejected.Discard();
        Check(rig.Catalog.SnapshotInteractions().Length == 0, "failed Initialize leaves no subscribers");
        var tornDown = rig.Owner("test.teardown");
        var abandoned = rig.Catalog.BeginSession(tornDown.PluginId);
        var abandonedRegistry = new PluginInteractionRegistry(abandoned, tornDown, rig.Catalog);
        abandonedRegistry.Register(new Recorder());
        typeof(PluginInstance).GetField("_session", PrivateInstance)!.SetValue(tornDown, abandoned);
        typeof(PluginInstance).GetMethod("Teardown", PrivateInstance)!.Invoke(tornDown, null);
        Check(abandoned.StagedInteractions.Count == 0, "production Teardown discards staged contributions");
        Throws(() => abandonedRegistry.Register(new Recorder { Id = "late" }), "captured registry closed by production failure cleanup");
        rig.Add(owner, new Recorder { Filter = InteractionEventKind.Presented });
        Check(Publish(rig.Runtime, Event(InteractionEventKind.SelectionChanged, 3)) == 0, "filter excludes unrelated events");
        rig.Enabled = false;
        Check(Publish(rig.Runtime, Event(InteractionEventKind.Presented, 4)) == 0, "system disabled refuses broadcast");
        rig.Enabled = true; owner.Entry.Enabled = false;
        Check(Publish(rig.Runtime, Event(InteractionEventKind.Presented, 5)) == 0, "disabled plugin not loaded or invoked");
        owner.Entry.Enabled = true;
        typeof(PluginInstance).GetMethod("SetState", PrivateInstance)!.Invoke(owner, new object[] { PluginRuntimeState.Installed });
        Check(Publish(rig.Runtime, Event(InteractionEventKind.Presented, 6)) == 0 && owner.State == PluginRuntimeState.Installed,
            "unloaded plugin is not activated by broadcast");
    }

    private static async Task TestSubscriptionIndex()
    {
        using var rig = new Rig();
        var owner = rig.Owner("test.index"); var other = rig.Owner("test.index.other");
        var entered = Gate(); var release = Gate();
        var first = new Recorder { Id = "first", Filter = InteractionEventKind.Presented | InteractionEventKind.ActionCommitted,
            Handler = async (e, _) => { if (e.Sequence == 1) { entered.TrySetResult(true); await release.Task; } } };
        var selection = new Recorder { Id = "selection", Filter = InteractionEventKind.SelectionChanged };
        var last = new Recorder { Id = "last", Filter = InteractionEventKind.Presented };
        var all = new Recorder { Id = "all" };
        var firstToken = rig.Add(owner, first); rig.Add(owner, selection);
        var lastToken = rig.Add(owner, last); rig.Add(owner, all);
        rig.Add(other, new Recorder { Filter = InteractionEventKind.SelectionChanged });
        var registrations = rig.Catalog.SnapshotInteractions();
        foreach (InteractionEventKind kind in Enum.GetValues<InteractionEventKind>())
        {
            int bits = (int)kind;
            if (bits == 0 || (bits & (bits - 1)) != 0) continue;
            var groups = rig.Catalog.SnapshotInteractions(kind);
            var expected = registrations.SelectMany(g => g.Registrations).Where(r => (r.Events & kind) != 0);
            Check(groups.SelectMany(g => g.Registrations).SequenceEqual(expected), "event index contains only ordered matching registrations: " + kind);
            Check(groups.All(g => g.Registrations.Length != 0 && g.Registrations.All(r => ReferenceEquals(r.Owner, g.Owner))),
                "event index keeps nonempty per-plugin groups: " + kind);
            Check(ReferenceEquals(groups, rig.Catalog.SnapshotInteractions(kind)), "event lookup reuses committed snapshot: " + kind);
        }
        Check(rig.Catalog.SnapshotInteractions(InteractionEventKind.None).Length == 0 &&
              rig.Catalog.SnapshotInteractions(InteractionEventKind.All).Length == 0 &&
              rig.Catalog.SnapshotInteractions((InteractionEventKind)128).Length == 0,
            "none/composite/unknown kinds are not event index entries");
        var presented = rig.Catalog.SnapshotInteractions(InteractionEventKind.Presented);
        Check(presented.Length == 1 && presented[0].Registrations.Length == 3,
            "Presented index excludes selection-only plugin and contribution");
        try
        {
            Check(Publish(rig.Runtime, Event(InteractionEventKind.Presented, 1)) == 3, "indexed publish counts matching contributions");
            await Wait(entered.Task);
            Check(rig.Runtime.Interactions.FindQueue(other.PluginId) == null, "unrelated plugin gets no queue");
            Publish(rig.Runtime, Event(InteractionEventKind.Presented, 2));
            Check(Publish(rig.Runtime, Event(InteractionEventKind.SelectionChanged, 3)) == 3, "selection index routes matching plugins");
            var queue = rig.Runtime.Interactions.FindQueue(owner.PluginId)!;
            var pending = (System.Collections.IEnumerable)typeof(PluginInteractionQueue).GetField("_pending", PrivateInstance)!.GetValue(queue)!;
            var deliveryGroups = pending.Cast<object>().Select(d => (PluginInteractionGroup)d.GetType().GetProperty("Group")!.GetValue(d)!).ToArray();
            Check(deliveryGroups.Length == 2 && ReferenceEquals(deliveryGroups[0], presented[0]) &&
                  deliveryGroups[0].Registrations.Select(r => r.FullId).SequenceEqual(new[] { owner.PluginId + ".first", owner.PluginId + ".last", owner.PluginId + ".all" }) &&
                  deliveryGroups[1].Registrations.Select(r => r.FullId).SequenceEqual(new[] { owner.PluginId + ".selection", owner.PluginId + ".all" }),
                "queued deliveries carry only their already-matched contribution groups");
            lastToken.Dispose();
            var rebuilt = rig.Catalog.SnapshotInteractions(InteractionEventKind.Presented);
            Check(!ReferenceEquals(presented, rebuilt) && rebuilt[0].Registrations.Length == 2,
                "individual revocation rebuilds event index");
            Check(presented[0].Registrations.Length == 3 && presented[0].RegisteredCount == 2,
                "retained delivery snapshot stays stable but revoked registration closes immediately");
            Check(Publish(rig.Runtime, Event(InteractionEventKind.Presented, 4)) == 2, "rebuilt index excludes revoked receiver");
        }
        finally { release.TrySetResult(true); }
        await Wait(rig.Runtime.Interactions.FindQueue(owner.PluginId)!.Completion);
        await Wait(rig.Runtime.Interactions.FindQueue(other.PluginId)!.Completion);
        Check(first.Seen.Select(e => e.Sequence).SequenceEqual(new long[] { 1, 2, 4 }) && last.Seen.Count == 0,
            "buffered pre-revocation delivery skips revoked receiver without losing live receivers");
        Check(selection.Seen.Select(e => e.Sequence).SequenceEqual(new long[] { 3 }) &&
              all.Seen.Select(e => e.Sequence).SequenceEqual(new long[] { 1, 2, 3, 4 }),
            "mixed-filter contributions preserve plugin serial event order");
        Check(new[] { first, selection, last, all }.All(r => r.DescriptorReads == 1), "index rebuild and publish never reread plugin descriptors");
        rig.Catalog.RevokeAll(owner.PluginId);
        Check(Enum.GetValues<InteractionEventKind>().All(kind => rig.Catalog.SnapshotInteractions(kind).All(g => !ReferenceEquals(g.Owner, owner))),
            "whole-plugin revocation removes owner from every event index");
        rig.Add(owner, new Recorder { Id = "first", Filter = InteractionEventKind.Presented });
        firstToken.Dispose();
        Check(rig.Catalog.SnapshotInteractions(InteractionEventKind.Presented).Single().Registrations.Single().IsRegistered,
            "old token cannot revoke replacement with same full contribution ID");
        var staged = rig.Catalog.BeginSession(owner.PluginId);
        var stagedToken = new PluginInteractionRegistry(staged, owner, rig.Catalog).Register(new Recorder { Id = "withdrawn", Filter = InteractionEventKind.SessionEnded });
        stagedToken.Dispose(); Check(staged.Commit(out _), "precommit token withdrawal leaves transaction valid");
        Check(rig.Catalog.SnapshotInteractions(InteractionEventKind.SessionEnded).Length == 0,
            "withdrawn staged registration never enters event index");
    }
    private static async Task TestIndexedSubscriptionBarrier()
    {
        using var rig = new Rig(); var owner = rig.Owner("test.index.barrier");
        var entered = Gate(); var release = Gate();
        var blocker = new Recorder { Id = "blocker", Handler = async (e, _) =>
            { if (e.Sequence == 1) { entered.TrySetResult(true); await release.Task; } } };
        var revoked = new Recorder { Id = "listener", Filter = InteractionEventKind.SelectionChanged };
        var replacement = new Recorder { Id = "listener", Filter = InteractionEventKind.SelectionChanged };
        rig.Add(owner, blocker); var token = rig.Add(owner, revoked);
        try
        {
            Publish(rig.Runtime, Event(InteractionEventKind.Presented, 1)); await Wait(entered.Task);
            Publish(rig.Runtime, Event(InteractionEventKind.SelectionChanged, 2));
            token.Dispose(); rig.Add(owner, replacement);
            Publish(rig.Runtime, Event(InteractionEventKind.SelectionChanged, 3));
            Check(rig.Runtime.Interactions.FindQueue(owner.PluginId)!.PendingCount == 2,
                "adjacent selections cannot merge across rebuilt subscription snapshots");
        }
        finally { release.TrySetResult(true); }
        await Wait(rig.Runtime.Interactions.FindQueue(owner.PluginId)!.Completion);
        Check(blocker.Seen.Select(e => e.Sequence).SequenceEqual(new long[] { 1, 2, 3 }) &&
              revoked.Seen.Count == 0 && replacement.Seen.Select(e => e.Sequence).SequenceEqual(new long[] { 3 }),
            "new registration never receives pre-registration buffered event");
    }
    private static async Task TestQueue()
    {
        using var rig = new Rig(4); var owner = rig.Owner("test.coalescing"); var entered = Gate(); var release = Gate();
        var recorder = new Recorder { Handler = async (e, _) => { if (e.Sequence == 1) { entered.TrySetResult(true); await release.Task; } } };
        rig.Add(owner, recorder); Publish(rig.Runtime, Event(InteractionEventKind.Presented, 1)); await Wait(entered.Task);
        var queue = rig.Runtime.Interactions.FindQueue(owner.PluginId)!;
        Publish(rig.Runtime, Event(InteractionEventKind.SelectionChanged, 2));
        Publish(rig.Runtime, Event(InteractionEventKind.SelectionChanged, 3));
        Check(queue.PendingCount == 1, "adjacent selections coalesced");
        Publish(rig.Runtime, Event(InteractionEventKind.SubmenuExpanded, 4));
        Publish(rig.Runtime, Event(InteractionEventKind.SelectionChanged, 5));
        Publish(rig.Runtime, Event(InteractionEventKind.SessionEnded, 6));
        Check(queue.PendingCount == 4, "coalescing cannot cross navigation/End barriers");
        release.TrySetResult(true); await Wait(queue.Completion);
        Check(recorder.Seen.Select(e => e.Sequence).SequenceEqual(new long[] { 1, 3, 4, 5, 6 }), "delivery order and coalesced gaps preserved");
        Check(owner.ActiveCallCount == 0, "completed callbacks release leases");

        using var small = new Rig(2); var other = small.Owner("test.eviction"); entered = Gate(); release = Gate();
        var observer = new Recorder { Handler = async (e, _) => { if (e.Sequence == 1) { entered.TrySetResult(true); await release.Task; } } };
        small.Add(other, observer); Publish(small.Runtime, Event(InteractionEventKind.Presented, 1)); await Wait(entered.Task);
        Publish(small.Runtime, Event(InteractionEventKind.SelectionChanged, 2));
        Publish(small.Runtime, Event(InteractionEventKind.ActionCommitted, 3));
        Check(Publish(small.Runtime, Event(InteractionEventKind.SessionEnded, 4)) == 1, "critical event displaces replaceable selection");
        Check(small.Runtime.Interactions.FindQueue(other.PluginId)!.PendingCount == 2, "queue capacity never exceeded");
        release.TrySetResult(true); await Wait(small.Runtime.Interactions.FindQueue(other.PluginId)!.Completion);
        Check(observer.Seen.Select(e => e.Sequence).SequenceEqual(new long[] { 1, 3, 4 }), "terminal survives selection pressure");
    }

    private static async Task TestSessionSeparationAndSerialContributions()
    {
        using var rig = new Rig(); var owner = rig.Owner("test.serial"); var entered = Gate(); var release = Gate();
        var first = new Recorder { Id = "first", Handler = async (e, _) => { if (e.Sequence == 1) { entered.TrySetResult(true); await release.Task; } } };
        var second = new Recorder { Id = "second" };
        rig.Add(owner, first); rig.Add(owner, second);
        Check(Publish(rig.Runtime, Event(InteractionEventKind.Presented, 1)) == 2, "acceptance counts contributions, not queues");
        await Wait(entered.Task);
        Check(second.Seen.Count == 0 && owner.ActiveCallCount == 1, "contributions in one plugin run serially");
        Publish(rig.Runtime, Event(InteractionEventKind.SelectionChanged, 2, 500));
        Publish(rig.Runtime, Event(InteractionEventKind.SelectionChanged, 2, 501));
        var queue = rig.Runtime.Interactions.FindQueue(owner.PluginId)!;
        Check(queue.PendingCount == 2, "different sessions are not coalesced");
        release.TrySetResult(true); await Wait(queue.Completion);
        Check(first.Seen.Count == 3 && second.Seen.Count == 3, "each serial contribution receives both sessions");
    }
    private static async Task TestOverflowAndLease()
    {
        using var rig = new Rig(2); var owner = rig.Owner("test.overflow"); var entered = Gate(); var release = Gate(); var cancelled = Gate();
        var recorder = new Recorder { Handler = async (_, ct) => { using var token = ct.Register(() => { Check(!_publishing, "cancellation callbacks off publisher"); cancelled.TrySetResult(true); }); entered.TrySetResult(true); await release.Task; } };
        rig.Add(owner, recorder); Publish(rig.Runtime, Event(InteractionEventKind.Presented, 1)); await Wait(entered.Task);
        Publish(rig.Runtime, Event(InteractionEventKind.SubmenuExpanded, 2));
        Publish(rig.Runtime, Event(InteractionEventKind.ActionCommitted, 3));
        Check(Publish(rig.Runtime, Event(InteractionEventKind.SessionEnded, 4)) == 0, "all-critical overflow explicitly rejects");
        var queue = rig.Runtime.Interactions.FindQueue(owner.PluginId)!;
        await Wait(cancelled.Task);
        Check(queue.OverflowCount == 1 && queue.PendingCount == 0, "overflow diagnosed and pending references cleared");
        Check(Publish(rig.Runtime, Event(InteractionEventKind.Presented, 5)) == 0 && owner.Entry.Enabled, "overflow suspends routing without changing user preference");
        var drain = rig.Stop(owner);
        Check(!drain.IsCompleted && owner.ActiveCallCount == 1, "uncooperative callback retains lease after cancellation");
        release.TrySetResult(true); await Wait(drain); await Wait(queue.Completion);
        Check(owner.ActiveCallCount == 0 && recorder.Seen.Count == 1, "lease drains only after real callback ends");
    }

    private static async Task TestRevocationAndGeneration()
    {
        using var rig = new Rig(); var owner = rig.Owner("test.generation"); var entered = Gate(); var release = Gate();
        var recorder = new Recorder { Handler = async (e, _) => { if (e.Sequence == 1) { entered.TrySetResult(true); await release.Task; } } };
        var token = rig.Add(owner, recorder); Publish(rig.Runtime, Event(InteractionEventKind.Presented, 1)); await Wait(entered.Task);
        Publish(rig.Runtime, Event(InteractionEventKind.SelectionChanged, 2));
        var replacement = rig.Owner(owner.PluginId);
        release.TrySetResult(true); await Wait(rig.Runtime.Interactions.FindQueue(owner.PluginId)!.Completion);
        Check(recorder.Seen.Count == 1, "queued old owner not called after same-ID replacement");
        Check(Publish(rig.Runtime, Event(InteractionEventKind.Presented, 3)) == 0, "old subscription not redirected to new instance");
        token.Dispose(); rig.Add(replacement, new Recorder());
        Check(Publish(rig.Runtime, Event(InteractionEventKind.Presented, 4)) == 1, "new generation receives its own events");
        await Wait(rig.Runtime.Interactions.FindQueue(owner.PluginId)!.Completion);
        var generation = replacement.GenerationId;
        typeof(PluginInstance).GetProperty(nameof(PluginInstance.GenerationId))!.SetValue(replacement, generation + 100000);
        Check(!replacement.TryAcquireInvocation(generation, PluginCallKind.InteractionEvent, out _, out _), "same-instance generation change atomically blocks stale lease");
        Check(Publish(rig.Runtime, Event(InteractionEventKind.Presented, 5)) == 0, "stale registration cannot publish after reload");

        using var tokens = new Rig(); var target = tokens.Owner("test.disposal"); entered = Gate(); release = Gate();
        var disposed = new Recorder { Handler = async (e, _) => { if (e.Sequence == 1) { entered.TrySetResult(true); await release.Task; } } };
        var disposable = tokens.Add(target, disposed); Publish(tokens.Runtime, Event(InteractionEventKind.Presented, 1)); await Wait(entered.Task);
        Publish(tokens.Runtime, Event(InteractionEventKind.SelectionChanged, 2)); disposable.Dispose(); release.TrySetResult(true);
        await Wait(tokens.Runtime.Interactions.FindQueue(target.PluginId)!.Completion);
        Check(disposed.Seen.Count == 1, "individual token revokes buffered callback");
    }

    private static async Task TestIsolation()
    {
        using var rig = new Rig(); var bad = rig.Owner("test.bad"); var good = rig.Owner("test.good");
        var broken = new Recorder { Handler = (_, _) => throw new InvalidOperationException("sentinel") };
        var working = new Recorder(); rig.Add(bad, broken); rig.Add(good, working);
        Check(Publish(rig.Runtime, Event(InteractionEventKind.Presented, 1)) == 2, "broadcast reaches multiple matching plugins");
        await Wait(Task.WhenAll(rig.Runtime.Interactions.FindQueue(bad.PluginId)!.Completion, rig.Runtime.Interactions.FindQueue(good.PluginId)!.Completion));
        Check(working.Seen.Count == 1 && bad.ActiveCallCount == 0, "exception isolated and lease released");
        Publish(rig.Runtime, Event(InteractionEventKind.SessionEnded, 2));
        await Wait(Task.WhenAll(rig.Runtime.Interactions.FindQueue(bad.PluginId)!.Completion, rig.Runtime.Interactions.FindQueue(good.PluginId)!.Completion));
        Check(working.Seen.Count == 2, "one plugin failure does not poison others");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RealCount(PluginInstance owner)
    {
        var plugin = typeof(PluginInstance).GetField("_plugin", PrivateInstance)!.GetValue(owner)!;
        return (int)plugin.GetType().GetProperty("Count")!.GetValue(plugin)!;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string RealReason(PluginInstance owner)
    {
        var plugin = typeof(PluginInstance).GetField("_plugin", PrivateInstance)!.GetValue(owner)!;
        return (string)plugin.GetType().GetProperty("LastReason")!.GetValue(plugin)!;
    }
    private static async Task TestRealHost()
    {
        PluginHost.HeadlessMode = true; PluginHost.Initialize();
        var dll = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../Fixture/bin/Release/net8.0-windows/StarPie.InteractionFixture.dll"));
        var scan = PluginScanner.ScanSelectedDll(dll);
        Check(scan.Accepted, "real recording DLL accepted by scanner");
        var owner = new PluginInstance("demo.interaction.recorder", new PluginRegistryEntry
            { Id = "demo.interaction.recorder", Enabled = true, ExternalPath = dll }, scan);
        var instances = (Dictionary<string, PluginInstance>)typeof(PluginHost).GetField("Instances", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var runtime = (PluginRuntime)typeof(PluginHost).GetField("Runtime", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        instances.Add(owner.PluginId, owner);
        PluginHost.RefreshInteractionInstances();
        Check(owner.Load(out string error), "real Initialize commits through optional context: " + error);
        Check(PluginHost.PublishInteractionEvent(Event(InteractionEventKind.Presented, 1)) == 1, "actual Host→Runtime→path→DLL callback");
        await Wait(runtime.Interactions.FindQueue(owner.PluginId)!.Completion);
        Check(RealCount(owner) == 1, "loaded DLL implementation handled event");
        var entered = Gate(); var release = Gate();
        var hostGate = typeof(PluginHost).GetField("Gate", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var holder = Task.Run(() => { lock (hostGate) { entered.TrySetResult(true); release.Task.GetAwaiter().GetResult(); } });
        await Wait(entered.Task);
        try
        {
            var publish = Task.Run(() => PluginHost.PublishInteractionEvent(Event(InteractionEventKind.SelectionChanged, 2)));
            Check(await publish.WaitAsync(TimeSpan.FromSeconds(3)) == 1, "Publish succeeds while install Gate held");
        }
        finally { release.TrySetResult(true); await Wait(holder); }
        await Wait(runtime.Interactions.FindQueue(owner.PluginId)!.Completion);
        var pendingSession = new StickyWheelSession.Session(owner.PluginId, new System.Windows.Point(0, 0), 1, null,
            ownerInstance: owner, ownerGeneration: owner.GenerationId);
        typeof(StickyWheelSession).GetField("_current", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, pendingSession);
        Check(StickyWheelSession.Dismiss(owner.PluginId), "actual pre-presentation plugin dismiss accepted");
        // _current 已被撤销，因此这个真实 Present 入口只走版本/属主守卫，不能构造窗口。
        typeof(StickyWheelSession).GetMethod("Present", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { pendingSession });
        await Wait(runtime.Interactions.FindQueue(owner.PluginId)!.Completion);
        Check(RealReason(owner) == "DismissedByPlugin", "late presentation guard cannot overwrite explicit dismiss reason");
        // 按闸门强制：原子撤销已释放 Gate，但 Finish 尚未执行时，UI 的旧 Present 抢先运行。
        var interleaved = new StickyWheelSession.Session(owner.PluginId, new System.Windows.Point(0, 0), 2, null,
            ownerInstance: owner, ownerGeneration: owner.GenerationId);
        typeof(StickyWheelSession).GetField("_current", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, interleaved);
        var reserved = Gate(); var finish = Gate();
        int beforeDismiss = RealCount(owner);
        var dismissal = Task.Run(async () =>
        {
            var reservation = StickyWheelSession.ReserveDismissal(owner.PluginId);
            Check(ReferenceEquals(reservation, interleaved), "actual dismissal reservation owns detached session");
            reserved.TrySetResult(true);
            await finish.Task;
            typeof(StickyWheelSession).GetMethod("FinishDismissal", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { reservation! });
        });
        await Wait(reserved.Task);
        try
        {
            var latePresent = Task.Run(() => typeof(StickyWheelSession).GetMethod("Present", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { interleaved }));
            await Wait(latePresent);
            await Wait(runtime.Interactions.FindQueue(owner.PluginId)!.Completion);
            Check(RealReason(owner) == "DismissedByPlugin", "UI before Finish cannot steal frozen dismissal reason");
            Check(RealCount(owner) == beforeDismiss + 1, "competing UI guard flushes exactly one terminal event");
        }
        finally { finish.TrySetResult(true); await Wait(dismissal); }
        await Wait(runtime.Interactions.FindQueue(owner.PluginId)!.Completion);
        Check(RealCount(owner) == beforeDismiss + 1, "later Finish cannot duplicate terminal publication");
        Check(!StickyWheelSession.Dismiss("not-the-owner"), "missing session dismiss remains harmless");
        var drained = owner.BeginStopping(); runtime.NotifyPluginStopping(owner.PluginId); await Wait(drained);
        Check(PluginHost.PublishInteractionEvent(Event(InteractionEventKind.SessionEnded, 2)) == 0, "real stopped owner has no routes");
        owner.Unload(); runtime.NotifyPluginStopped(owner.PluginId); instances.Remove(owner.PluginId); PluginHost.RefreshInteractionInstances();
        Check(owner.WaitForUnloadVerdict(5000), "real collectible plugin ALC released");
        owner.Settings.Set("failInit", "true");
        instances.Add(owner.PluginId, owner); PluginHost.RefreshInteractionInstances();
        Check(!owner.Load(out _), "real Initialize exception rejects load");
        Check(PluginHost.PublishInteractionEvent(Event(InteractionEventKind.Presented, 9)) == 0 &&
              PluginHost.Catalog.SnapshotInteractions().All(g => g.Owner.PluginId != owner.PluginId),
              "real failed Initialize leaves no published subscriptions");
        instances.Remove(owner.PluginId); PluginHost.RefreshInteractionInstances();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static PluginInstance ExerciseRealSoundModule(bool invokeEvents = true)
    {
        string dll = Path.GetFullPath("plugin/StarPie-Official-Plugins/src/StarPie.Plugin.Sound/bin/Release/net8.0-windows/StarPie.Plugin.Sound.dll");
        var scan = PluginScanner.ScanSelectedDll(dll, allowReservedIdPrefix: true);
        Check(scan.Accepted, "sound production DLL passes manifest/SDK scan");
        var owner = new PluginInstance("starpie.plugin.sound", new PluginRegistryEntry
            { Id = "starpie.plugin.sound", Enabled = true, Official = true, ExternalPath = dll }, scan);
        var instances = (Dictionary<string, PluginInstance>)typeof(PluginHost).GetField("Instances", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var runtime = (PluginRuntime)typeof(PluginHost).GetField("Runtime", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        instances.Add(owner.PluginId, owner); PluginHost.RefreshInteractionInstances();
        Check(owner.Load(out string error), "sound Initialize succeeds without physical playback: " + error);
        var plugin = typeof(PluginInstance).GetField("_plugin", PrivateInstance)!.GetValue(owner)!;
        var engine = plugin.GetType().Assembly.GetType("StarPie.Plugin.Sound.SoundEffectManager")!;
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        engine.GetProperty("TestMode", flags)!.SetValue(null, true);
        using var reached = new ManualResetEventSlim();
        engine.GetProperty("PlaybackSink", flags)!.SetValue(null, (Action<byte[], uint>)((_, _) => reached.Set()));
        if (invokeEvents)
        {
        Check(PluginHost.PublishInteractionEvent(Event(InteractionEventKind.Presented, 1, 900)) == 1, "actual host routes event to loaded sound observer");
        runtime.Interactions.FindQueue(owner.PluginId)!.Completion.GetAwaiter().GetResult();
        Check(reached.Wait(3000), "loaded ALC reaches guarded mock sound backend");
        PluginHost.PublishInteractionEvent(Event(InteractionEventKind.SessionEnded, 2, 900));
        runtime.Interactions.FindQueue(owner.PluginId)!.Completion.GetAwaiter().GetResult();
        }
        engine.GetProperty("PlaybackSink", flags)!.SetValue(null, null);
        var drained = owner.BeginStopping(); runtime.NotifyPluginStopping(owner.PluginId); drained.GetAwaiter().GetResult();
        owner.Unload(); runtime.NotifyPluginStopped(owner.PluginId); instances.Remove(owner.PluginId); PluginHost.RefreshInteractionInstances();
        return owner;
    }
    private static void TestRealSoundModule()
    {
        var idle = ExerciseRealSoundModule(invokeEvents: false);
        Check(idle.WaitForUnloadVerdict(5000), "idle sound metadata/settings ownership releases collectible ALC");
        var owner = ExerciseRealSoundModule();
        Check(owner.WaitForUnloadVerdict(5000), "sound observer/settings/native-worker ownership releases collectible ALC");
        Check(PluginHost.PublishInteractionEvent(Event(InteractionEventKind.Presented, 3, 900)) == 0, "unloaded sound module has no route and no host fallback");
    }

    private static async Task<int> Main()
    {
        string? old = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        string root = Path.Combine(Path.GetTempPath(), "StarPie-InteractionTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); Environment.SetEnvironmentVariable("LOCALAPPDATA", root);
        PluginPaths.OverrideRootsForTesting(Path.Combine(root, "host"), Path.Combine(root, "scan"));
        try
        {
            TestSessions(); TestConfiguredConfirmation(); TestRenderCompletion(); await TestFrozenEndPublishing(); await TestRegistration(); await TestSubscriptionIndex(); await TestIndexedSubscriptionBarrier(); await TestQueue(); await TestSessionSeparationAndSerialContributions(); await TestOverflowAndLease();
            await TestRevocationAndGeneration(); await TestIsolation(); await TestRealHost(); TestRealSoundModule();
        }
        catch (Exception ex) { Check(false, ex.ToString()); }
        finally { Environment.SetEnvironmentVariable("LOCALAPPDATA", old); }
        Console.WriteLine($"Result: {_passed} passed, {_failed} failed. Isolated evidence root: {root}");
        return _failed == 0 ? 0 : 1;
    }
}
