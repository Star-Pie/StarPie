using System.Text.RegularExpressions;
using StarPie.Plugin;

namespace WinPieGestures.Plugins;

/// <summary>元数据在 Initialize 时快照；撤销立刻断开插件代码引用。</summary>
internal sealed class PluginInteractionRegistration : IDisposable
{
    private readonly object _gate = new();
    private IInteractionContribution? _contribution;
    internal PluginInstance Owner { get; }
    internal long Generation { get; }
    internal string FullId { get; }
    internal InteractionEventKind Events { get; }
    internal bool IsRegistered { get { lock (_gate) return _contribution != null; } }

    internal PluginInteractionRegistration(PluginInstance owner, string id,
        InteractionEventKind events, IInteractionContribution contribution)
    {
        Owner = owner;
        Generation = owner.GenerationId;
        FullId = owner.PluginId + "." + id;
        Events = events;
        _contribution = contribution;
    }

    internal bool TryBegin(PluginCallCoordinator calls, out IInteractionContribution? contribution,
        out PluginInvocationLease? lease)
    {
        lock (_gate)
        {
            contribution = null;
            lease = null;
            if (_contribution == null || !calls.TryAcquireInvocation(
                    Owner, Generation, PluginCallKind.InteractionEvent, out lease, out _)) return false;
            contribution = _contribution;
            return true;
        }
    }

    public void Dispose() { lock (_gate) _contribution = null; }
}

internal sealed class PluginInteractionGroup
{
    internal PluginInteractionRegistration[] Registrations { get; }
    internal PluginInstance Owner => Registrations[0].Owner;
    internal long Generation => Registrations[0].Generation;
    internal PluginInteractionGroup(PluginInteractionRegistration[] registrations) => Registrations = registrations;
    internal int RegisteredCount =>
        Registrations.Count(r => r.IsRegistered);
}

/// <summary>在目录锁内构建、整体发布；事件热路径只读对应类型的匹配组。</summary>
internal sealed class PluginInteractionSnapshot
{
    private readonly Dictionary<InteractionEventKind, PluginInteractionGroup[]> _byEvent = new();
    internal PluginInteractionGroup[] Groups { get; }

    internal PluginInteractionSnapshot(IEnumerable<PluginInteractionRegistration> registrations)
    {
        Groups = registrations.GroupBy(r => r.Owner)
            .Select(g => new PluginInteractionGroup(g.ToArray())).ToArray();
        foreach (InteractionEventKind kind in Enum.GetValues<InteractionEventKind>())
        {
            // All 是订阅掩码，不是可发布事件；索引仅包含单一事件类型。
            int bits = (int)kind;
            if (bits == 0 || (bits & (bits - 1)) != 0) continue;
            var matches = new List<PluginInteractionGroup>();
            foreach (PluginInteractionGroup group in Groups)
            {
                var matched = group.Registrations.Where(r => (r.Events & kind) != 0).ToArray();
                if (matched.Length != 0) matches.Add(new PluginInteractionGroup(matched));
            }
            if (matches.Count != 0) _byEvent.Add(kind, matches.ToArray());
        }
    }

    internal PluginInteractionGroup[] ForEvent(InteractionEventKind kind) =>
        _byEvent.TryGetValue(kind, out var groups) ? groups : Array.Empty<PluginInteractionGroup>();
}

internal sealed class PluginInteractionRegistry : IInteractionRegistry
{
    private static readonly Regex IdPattern = new("^[A-Za-z][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant);
    private readonly PluginRegistrationSession _session;
    private readonly PluginInstance _owner;
    private readonly PluginCatalog _catalog;

    internal PluginInteractionRegistry(PluginRegistrationSession session, PluginInstance owner, PluginCatalog catalog)
    { _session = session; _owner = owner; _catalog = catalog; }

    public IDisposable Register(IInteractionContribution contribution)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        lock (_session.InteractionGate)
        {
            if (_session.InteractionRegistrationClosed)
                throw new PluginContractException("Interaction registration is only allowed during Initialize.");
            InteractionDescriptor descriptor = contribution.Descriptor ??
                throw new PluginContractException("Interaction Descriptor cannot be null.");
            string id = (descriptor.Id ?? "").Trim();
            if (!IdPattern.IsMatch(id) || descriptor.Events == InteractionEventKind.None ||
                (descriptor.Events & ~InteractionEventKind.All) != 0)
                throw new PluginContractException("Invalid interaction ID or event filter.");
            var registration = new PluginInteractionRegistration(_owner, id, descriptor.Events, contribution);
            if (_session.StagedInteractions.Any(r => r.FullId.Equals(registration.FullId, StringComparison.OrdinalIgnoreCase)))
                throw new PluginContractException("Duplicate interaction contribution: " + registration.FullId);
            _session.StagedInteractions.Add(registration);
            return new RegistrationToken(() => _catalog.RemoveInteraction(registration));
        }
    }
}
