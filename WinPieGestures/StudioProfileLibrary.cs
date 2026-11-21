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
        JsonSerializer.Serialize(new { Profiles = new[] { profile } });

    internal static WheelProfile ReadProfile(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1 ||
            !root.TryGetProperty("Profiles", out var profiles) || profiles.ValueKind != JsonValueKind.Array ||
            profiles.GetArrayLength() != 1 || profiles[0].ValueKind != JsonValueKind.Object ||
            !profiles[0].TryGetProperty("ProcessName", out var name) || name.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(name.GetString()))
            throw new InvalidDataException();
        var profile = profiles[0].Deserialize<WheelProfile>() ?? throw new InvalidDataException();
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
