using System.IO;
using System.Text.Json;

namespace WinPieGestures;

internal static class StudioProfileLibrary
{
    // Resource creation belongs to PortableConfigImportTransaction, including cancellation and wrong-scope cleanup.
    internal static string IconDirectory => Path.Combine(ApplicationDataPaths.Root, "CustomIcons");

    internal static List<string> ParseTags(string input) =>
        input.Split(new[] { ',', ';', '，', '；', '#' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    internal static bool Matches(WheelProfile profile, string query, string? selectedTag)
    {
        var tags = profile.Tags;
        if (selectedTag != null && !tags.Contains(selectedTag, StringComparer.OrdinalIgnoreCase)) return false;
        return query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).All(term =>
            term.StartsWith('#') ? tags.Any(tag => Contains(tag, term[1..])) :
                Contains(profile.FriendlyName, term) || Contains(profile.ProcessName, term) ||
                Contains(profile.BoundProcesses, term) || tags.Any(tag => Contains(tag, term)));
    }

    private static bool Contains(string? source, string text) =>
        source?.Contains(text, StringComparison.OrdinalIgnoreCase) == true;

    internal static bool HasImportConflict(AppConfig config, WheelProfile incoming)
    {
        // Validate the prospective table so the router also compares bindings on two different incoming layers.
        var projected = new AppConfig
        {
            Profiles = config.Profiles.Append(incoming).ToList(),
            Trigger = config.Trigger,
            BlacklistTriggerOverrides = config.BlacklistTriggerOverrides
        };
        if (ProfileTriggerRouter.ValidateBindings(projected, incoming) != ProfileTriggerValidationError.None) return true;

        // BuildSnapshot gives an application its ordinary route even when individual layers have dedicated keys.
        static bool HasOrdinaryRoute(WheelProfile profile) => profile.UseApplicationContext &&
            profile.ActivationTrigger == null && !string.Equals(profile.ProcessName, "Global", StringComparison.OrdinalIgnoreCase);
        if (!HasOrdinaryRoute(incoming)) return false;
        var processes = ProfileProcessBindings.GetNames(incoming);
        return processes.Count != 0 && config.Profiles.Any(p => HasOrdinaryRoute(p) && processes.Overlaps(ProfileProcessBindings.GetNames(p)));
    }

    internal static string SerializeProfile(WheelProfile profile) =>
        SerializeProfiles([profile]);

    internal static string SerializeProfiles(IReadOnlyList<WheelProfile> profiles) =>
        JsonSerializer.Serialize(new { Profiles = profiles });

    internal static IReadOnlyList<WheelProfile> PortalExportProfiles(WheelProfile root, IReadOnlyList<WheelProfile> profiles)
    {
        var result = new List<WheelProfile> { root };
        var seen = new HashSet<WheelProfile> { root };
        var references = new PortalReferenceAnalyzer(profiles).References.ToLookup(reference => reference.Source.Profile);
        for (int i = 0; i < result.Count; i++)
            foreach (var reference in references[result[i]])
                if (reference.Destination is { } destination && seen.Add(destination.Profile))
                    result.Add(destination.Profile);
        return result;
    }

    internal static WheelProfile ReadProfile(string json)
    {
        var profiles = ReadProfiles(json);
        if (profiles.Count != 1) throw new InvalidDataException();
        return profiles[0];
    }

    internal static IReadOnlyList<WheelProfile> ReadProfiles(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1 ||
            !root.TryGetProperty("Profiles", out var profiles) || profiles.ValueKind != JsonValueKind.Array ||
            profiles.GetArrayLength() == 0)
            throw new InvalidDataException();
        var result = new List<WheelProfile>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in profiles.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("ProcessName", out var name) ||
                name.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(name.GetString()) ||
                !keys.Add(name.GetString()!)) throw new InvalidDataException();
            result.Add(ReadEntry(entry));
        }
        return result;
    }

    private static WheelProfile ReadEntry(JsonElement entry)
    {
        var profile = entry.Deserialize<WheelProfile>() ?? throw new InvalidDataException();
        if (profile.SectorCount is not (4 or 8 or 12) || profile.Layers?.Any(l => l == null || l.SectorCount is not (4 or 8 or 12)) == true) throw new InvalidDataException();
        ValidateActions(profile.Actions);
        if (profile.CenterAction != null) ValidateAction(profile.CenterAction);
        foreach (var layer in profile.Layers ?? Enumerable.Empty<WheelLayer>())
        {
            ValidateActions(layer.Actions);
            if (layer.CenterAction != null) ValidateAction(layer.CenterAction);
            if (layer.FixedRings == null) continue;
            if (layer.FixedRings.Tiers == null) throw new InvalidDataException();
            foreach (var tier in layer.FixedRings.Tiers)
            {
                if (tier == null) continue; // Sparse tiers and vacant slots retain their indices.
                if (tier.Slots == null) throw new InvalidDataException();
                foreach (var slot in tier.Slots)
                    if (slot != null) ValidateAction(slot.Action);
            }
        }
        profile.EnsureLayers();
        return profile;
    }

    internal static void NameImportedProfiles(IReadOnlyList<WheelProfile> profiles, IReadOnlyList<WheelProfile> existing, string rootName)
    {
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Global" };
        foreach (var profile in existing)
        {
            reserved.Add(profile.ProcessName);
            reserved.Add(profile.FriendlyName);
        }
        var graph = new PortalReferenceAnalyzer(profiles);
        var renamed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < profiles.Count; i++)
        {
            var profile = profiles[i];
            string original = profile.ProcessName;
            string name = i == 0 ? rootName : profile.FriendlyName;
            if (!reserved.Add(name))
            {
                string copy = I18n.TF("CopyLayerNameFmt", name);
                name = copy;
                int suffix = 2;
                while (!reserved.Add(name)) name = copy + " " + suffix++;
            }
            if (!profile.HasExplicitProcessBindings)
                profile.BoundProcesses = string.Equals(original, "Global", StringComparison.OrdinalIgnoreCase)
                    ? "" : string.Join(", ", ProfileProcessBindings.GetNames(profile));
            profile.HasExplicitProcessBindings = true;
            profile.ProcessName = profile.DisplayName = name;
            profile.ApplicationIconPath = null;
            renamed.Add(original, name);
        }
        foreach (var reference in graph.References)
        {
            var target = reference.Target;
            if (reference.Destination is not { } address) continue;
            if (!target.SameProfile && renamed.TryGetValue(target.TargetProfileKey, out string? key)) target.TargetProfileKey = key;
            target.TargetProfileNameSnapshot = address.Profile.FriendlyName;
            if (string.IsNullOrWhiteSpace(target.TargetLayerId)) target.TargetLayerId = address.Layer.EnsureLayerId();
        }
    }

    private static void ValidateActions(List<ActionItem>? actions)
    {
        if (actions == null) return;
        foreach (var action in actions) ValidateAction(action);
    }

    private static void ValidateAction(ActionItem? action)
    {
        if (action == null) throw new InvalidDataException();
        action.SubActions ??= new();
        ValidateActions(action.SubActions);
    }
}
