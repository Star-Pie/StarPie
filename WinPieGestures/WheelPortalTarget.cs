using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinPieGestures;

public sealed class WheelPortalTarget
{
    public bool SameProfile { get; set; }
    public string TargetProfileKey { get; set; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TargetLayerId { get; set; }

    public int TargetLayerIndex { get; set; }
    public string? TargetProfileNameSnapshot { get; set; }
    public string? TargetLayerNameSnapshot { get; set; }
    public string AnchorMode { get; set; } = "SectorCenter";
    public bool SyncTargetName { get; set; } = true;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extras { get; set; }

    [JsonIgnore]
    internal bool IsOuterOffset => string.Equals(AnchorMode, "OuterOffset", StringComparison.OrdinalIgnoreCase);

    internal static bool SameValue(WheelPortalTarget? left, WheelPortalTarget? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left == null || right == null || left.SameProfile != right.SameProfile ||
            left.TargetProfileKey != right.TargetProfileKey || left.TargetLayerId != right.TargetLayerId ||
            left.TargetLayerIndex != right.TargetLayerIndex || left.TargetProfileNameSnapshot != right.TargetProfileNameSnapshot ||
            left.TargetLayerNameSnapshot != right.TargetLayerNameSnapshot || left.AnchorMode != right.AnchorMode ||
            left.SyncTargetName != right.SyncTargetName || left.Extras?.Count != right.Extras?.Count) return false;
        if (left.Extras != null)
            foreach (var (key, value) in left.Extras)
                if (!right.Extras!.TryGetValue(key, out var other) || value.GetRawText() != other.GetRawText()) return false;
        return true;
    }

    public WheelPortalTarget Clone() => new()
    {
        SameProfile = SameProfile,
        TargetProfileKey = TargetProfileKey,
        TargetLayerId = TargetLayerId,
        TargetLayerIndex = TargetLayerIndex,
        TargetProfileNameSnapshot = TargetProfileNameSnapshot,
        TargetLayerNameSnapshot = TargetLayerNameSnapshot,
        AnchorMode = AnchorMode,
        SyncTargetName = SyncTargetName,
        Extras = Extras == null ? null : new(Extras)
    };
}
