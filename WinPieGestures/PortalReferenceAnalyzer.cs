namespace WinPieGestures;

internal readonly record struct PortalLayerAddress(WheelProfile Profile, WheelLayer Layer);

internal sealed record PortalReference(PortalLayerAddress Source, ActionItem Action, WheelPortalTarget Target,
    PortalLayerAddress? Destination, bool Active);

// Used while editing or transferring configuration. Input callbacks consume prepared bindings instead.
internal sealed class PortalReferenceAnalyzer
{
    internal IReadOnlyList<PortalReference> References { get; }

    internal PortalReferenceAnalyzer(IReadOnlyList<WheelProfile> profiles)
    {
        var references = new List<PortalReference>();
        foreach (var profile in profiles)
            foreach (var layer in profile.Layers ?? [])
                foreach (var (action, active) in StoredActions(layer))
                    if (action.PortalTarget is { } target)
                        references.Add(new(new(profile, layer), action, target, Resolve(profiles, profile, target), active));
        References = references;
    }

    internal static PortalLayerAddress? Resolve(IReadOnlyList<WheelProfile> profiles, WheelProfile owner, WheelPortalTarget target)
    {
        WheelProfile? profile = target.SameProfile ? owner : null;
        if (!target.SameProfile && !string.IsNullOrWhiteSpace(target.TargetProfileKey))
        {
            for (int i = 0; i < profiles.Count; i++)
                if (string.Equals(profiles[i].ProcessName, target.TargetProfileKey, StringComparison.OrdinalIgnoreCase))
                {
                    profile = profiles[i];
                    break;
                }
        }
        if (profile?.Layers is not { } layers) return null;

        // An ID identifies a specific layer. A deleted layer must never redirect to a different index.
        if (!string.IsNullOrWhiteSpace(target.TargetLayerId))
        {
            WheelLayer? found = null;
            foreach (var layer in layers)
                if (string.Equals(layer.LayerId, target.TargetLayerId, StringComparison.OrdinalIgnoreCase))
                {
                    if (found != null) return null;
                    found = layer;
                }
            return found != null ? new(profile, found) : null;
        }
        return (uint)target.TargetLayerIndex < (uint)layers.Count ? new(profile, layers[target.TargetLayerIndex]) : null;
    }

    internal IEnumerable<PortalReference> FindIncomingReferences(WheelProfile profile, WheelLayer? layer = null) =>
        References.Where(reference => reference.Destination is { } destination && ReferenceEquals(destination.Profile, profile) &&
            (layer == null || ReferenceEquals(destination.Layer, layer)));

    // Includes both endpoints and repeats the source at the end when there is a cycle.
    internal IReadOnlyList<PortalLayerAddress>? FindCyclePath(PortalLayerAddress source, PortalLayerAddress destination,
        ActionItem? replacing = null)
    {
        var edges = References.Where(r => r.Active && r.Destination.HasValue && !ReferenceEquals(r.Action, replacing))
            .ToLookup(r => r.Source, r => r.Destination!.Value);
        var visited = new Dictionary<PortalLayerAddress, PortalLayerAddress> { [destination] = destination };
        var pending = new Stack<PortalLayerAddress>();
        pending.Push(destination);
        while (pending.TryPop(out var current))
        {
            if (current == source)
            {
                var path = new List<PortalLayerAddress> { source };
                while (current != destination)
                {
                    current = visited[current];
                    path.Add(current);
                }
                path.Add(source);
                path.Reverse();
                return path;
            }
            foreach (var next in edges[current])
                if (visited.TryAdd(next, current)) pending.Push(next);
        }
        return null;
    }

    internal static void RemapClonedLayers(WheelProfile source, WheelProfile clone)
    {
        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < source.Layers.Count; i++)
            if (source.Layers[i].LayerId is { Length: > 0 } oldId && clone.Layers[i].LayerId is { } newId)
                ids.TryAdd(oldId, newId);
        if (ids.Count == 0) return;

        foreach (var layer in clone.Layers)
            foreach (var (action, _) in StoredActions(layer))
                if (action.PortalTarget is { SameProfile: true, TargetLayerId: { } id } target &&
                    ids.TryGetValue(id, out var replacement))
                    target.TargetLayerId = replacement;
    }

    private static IEnumerable<(ActionItem Action, bool Active)> StoredActions(WheelLayer layer)
    {
        if (layer.Actions != null)
            foreach (var root in layer.Actions)
                foreach (var item in Walk(root, 1, layer.ActiveFixedRings != null ? 2 : layer.EffectiveMaxMenuDepth))
                    yield return item;
        if (layer.FixedRings is { } menu)
            for (int level = 3; level <= 5; level++)
            {
                var slots = menu.Tiers?.ElementAtOrDefault(level - 3)?.Slots;
                if (slots == null) continue;
                for (int i = 0; i < slots.Count; i++)
                {
                    bool enabled = layer.ActiveFixedRings != null && level <= layer.EffectiveMaxMenuDepth && i < menu.GetCount(level);
                    foreach (var item in Walk(slots[i]?.Action, level, enabled ? level : 0))
                        yield return item;
                }
            }
    }

    private static IEnumerable<(ActionItem Action, bool Active)> Walk(ActionItem? action, int depth, int activeLimit)
    {
        if (action == null) yield break;
        yield return (action, action.IsWheelPortal && depth <= activeLimit);
        if (action.SubActions == null) yield break;
        int nextLimit = action.IsWheelPortal || string.Equals(action.Type, "None", StringComparison.OrdinalIgnoreCase) ? 0 : activeLimit;
        foreach (var child in action.SubActions)
            foreach (var item in Walk(child, depth + 1, nextLimit))
                yield return item;
    }
}
