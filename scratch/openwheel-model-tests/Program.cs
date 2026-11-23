using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using WinPieGestures;

int checks = 0;
string candidate = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
    .Single(attribute => attribute.Key == "CandidateDll").Value!;
Check(SHA256.HashData(File.ReadAllBytes(candidate)).SequenceEqual(
    SHA256.HashData(File.ReadAllBytes(typeof(AppConfig).Assembly.Location))), "Candidate hash");

var config = JsonSerializer.Deserialize<AppConfig>("""{"Language":"en-US","FutureOption":{"x":1}}""")!;
Check(config.PortalEnableCrossExpand && config.EffectivePortalCrossExpandRatio == .84 &&
    config.PortalEnableDwellExpand && config.EffectivePortalDwellMs == 230 &&
    config.EffectivePortalMaxDepth == 5 && config.EffectivePortalOuterTangentGapDip == 6 &&
    !config.PortalAutoPins && !config.PortalEscapeClosesAll && config.PortalEnableReentryGuard &&
    config.PinnedNavUpVkCode == PinnedArrowNavigation.Up &&
    config.PinnedNavDownVkCode == PinnedArrowNavigation.Down &&
    config.PinnedNavLeftVkCode == PinnedArrowNavigation.Left &&
    config.PinnedNavRightVkCode == PinnedArrowNavigation.Right &&
    config.PinnedNavConfirmVkCode == PinnedArrowNavigation.Confirm, "Legacy configuration defaults");
config.PortalEnableCrossExpand = false;
config.PinnedNavUpVkCode = 0x57;
config.PinnedNavConfirmVkCode = 0x20;
var roundTrip = JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(config))!;
Check(!roundTrip.PortalEnableCrossExpand && roundTrip.Language == "en-US" && roundTrip.Extras!.ContainsKey("FutureOption") &&
    roundTrip.PinnedNavUpVkCode == 0x57 && roundTrip.PinnedNavConfirmVkCode == 0x20,
    "Explicit false, pinned nav keybindings, and unknown settings round-trip");
var dedupConfig = new AppConfig { PinnedNavUpVkCode = 16, PinnedNavDownVkCode = 0x57, PinnedNavLeftVkCode = 0x57, PinnedNavRightVkCode = 0x1B, PinnedNavConfirmVkCode = 0x20 };
var resolvedNav = PinnedNavBindings.FromConfig(dedupConfig);
Check(resolvedNav.Up == PinnedArrowNavigation.Up && resolvedNav.Down == 0x57 &&
    resolvedNav.Left == PinnedArrowNavigation.Left && resolvedNav.Right == PinnedArrowNavigation.Right &&
    resolvedNav.Confirm == 0x20, "PinnedNavBindings rejects modifiers/Esc and deduplicates invalid collisions");
config.PortalCrossExpandRatio = .3;
config.PortalDwellMs = 900;
config.PortalMaxDepth = -1;
config.PortalOuterTangentGapDip = 100;
Check(config.EffectivePortalCrossExpandRatio == .6 && config.EffectivePortalDwellMs == 600 &&
    config.EffectivePortalMaxDepth == 2 && config.EffectivePortalOuterTangentGapDip == 40, "Numeric bounds");
config.PortalCrossExpandRatio = double.NaN;
config.PortalOuterTangentGapDip = double.PositiveInfinity;
Check(config.EffectivePortalCrossExpandRatio == .84 && config.EffectivePortalOuterTangentGapDip == 6, "Non-finite fallbacks");

Check(config.EffectivePortalParentVisualMode == "ParentCore" && config.PortalTrailShowBeam &&
    config.EffectivePortalFadeOpacity == .28 && config.EffectivePortalCustomShape == "Circle" &&
    config.EffectivePortalCustomSizeDip == 42 && config.EffectivePortalCustomOpacity == .85,
    "Parent visual mode defaults");
config.PortalParentVisualMode = "UnknownMode";
config.PortalCustomShape = "UnknownShape";
config.PortalFadeOpacity = .01;
config.PortalCustomSizeDip = 999;
config.PortalCustomOpacity = 2.0;
Check(config.EffectivePortalParentVisualMode == "ParentCore" && config.EffectivePortalCustomShape == "Circle" &&
    config.EffectivePortalFadeOpacity == .08 && config.EffectivePortalCustomSizeDip == 140 &&
    config.EffectivePortalCustomOpacity == 1.0, "Parent visual clamping and unknown mode fallback");
config.PortalFadeOpacity = double.NaN;
config.PortalCustomSizeDip = double.NegativeInfinity;
config.PortalCustomOpacity = double.PositiveInfinity;
Check(config.EffectivePortalFadeOpacity == .28 && config.EffectivePortalCustomSizeDip == 42 &&
    config.EffectivePortalCustomOpacity == .85, "Parent visual non-finite fallbacks");
var visualSource = new AppConfig
{
    PortalParentVisualMode = "CustomMarker",
    PortalCustomShape = "FourPointStar",
    PortalTrailShowBeam = false,
    PortalCustomFillColor = "#FF112233"
};
var visualRoundTrip = JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(visualSource))!;
Check(visualRoundTrip.EffectivePortalParentVisualMode == "CustomMarker" &&
    visualRoundTrip.EffectivePortalCustomShape == "FourPointStar" &&
    !visualRoundTrip.PortalTrailShowBeam && visualRoundTrip.PortalCustomFillColor == "#FF112233",
    "Parent visual settings JSON round-trip");

var root = new WheelProfile { ProcessName = "Global", Layers = { new(), new(), new() } };
root.EnsureLayers();
var first = root.Layers[0];
var second = root.Layers[1];
var third = root.Layers[2];
Check(root.Layers.All(layer => layer.LayerId == null) && !JsonSerializer.Serialize(first).Contains("\"LayerId\""),
    "Legacy layers keep absent IDs");
string secondId = second.EnsureLayerId();
Check(second.EnsureLayerId() == secondId && first.LayerId == null, "Stable ID is allocated only for the chosen layer");
var portal = first.Actions[0];
portal.SubActions.Add(new() { Type = "Hotkey", Parameter = "Ctrl+C", Name = "Copy" });
portal.PortalTarget = new() { SameProfile = true, TargetLayerId = secondId, TargetLayerIndex = 1 };
portal.Type = "OpenWheel";
var restored = JsonSerializer.Deserialize<ActionItem>(JsonSerializer.Serialize(portal))!;
Check(WheelProfile.IsActionConfigured(portal) && restored.InlineSubActions == null &&
    root.GetEffectiveAction(0, 0) == null && root.GetEffectiveMenuAction([0, 0]) == null, "Portal suppresses only its own inline children");
restored.Type = "Hotkey";
Check(restored.InlineSubActions?.Single().Parameter == "Ctrl+C", "Switching back restores the saved menu");
var snapshot = portal.CloneWithoutSubActions();
snapshot.PortalTarget!.TargetLayerId = "changed";
Check(portal.PortalTarget.TargetLayerId == secondId && snapshot.SubActions.Count == 0, "Execution clone isolates portal data");

third.FixedRings = new();
third.MaxMenuDepth = 3;
third.FixedRings.SetSlot(3, 0, new() { Action = portal.Clone() });
var clone = root.Clone("copy");
Check(clone.Layers[0].LayerId == null && clone.Layers[1].LayerId != secondId &&
    clone.Layers[0].Actions[0].PortalTarget!.TargetLayerId == clone.Layers[1].LayerId &&
    clone.Layers[2].FixedRings!.GetSlot(3, 0)!.Action.PortalTarget!.TargetLayerId == clone.Layers[1].LayerId &&
    portal.PortalTarget.TargetLayerId == secondId, "Profile clone remaps portal IDs for inline and fixed slots");

var firstAddress = new PortalLayerAddress(root, first);
var secondAddress = new PortalLayerAddress(root, second);
var thirdAddress = new PortalLayerAddress(root, third);
second.Actions[0] = new() { Type = "OpenWheel", PortalTarget = new() { SameProfile = true, TargetLayerIndex = 2 } };
var graph = new PortalReferenceAnalyzer([root]);
Check(graph.FindCyclePath(firstAddress, firstAddress)!.SequenceEqual(new[] { firstAddress, firstAddress }), "Direct self-reference");
Check(graph.FindCyclePath(thirdAddress, firstAddress)!.SequenceEqual(new[] { thirdAddress, firstAddress, secondAddress, thirdAddress }), "DFS returns indirect loop path");
Check(graph.FindIncomingReferences(root, second).Count() == 2, "Incoming references include fixed slots");
root.Layers.Reverse();
Check(PortalReferenceAnalyzer.Resolve([root], root, portal.PortalTarget) == secondAddress, "ID takes precedence over index");
root.Layers.Remove(second);
Check(new PortalReferenceAnalyzer([root]).References.Any(reference =>
    ReferenceEquals(reference.Action, portal) && reference.Destination == null), "Deleting a target reports a broken link");
Console.WriteLine($"{checks} passed");

void Check(bool result, string name)
{
    if (!result) throw new InvalidOperationException(name);
    Console.WriteLine($"PASS {++checks} {name}");
}
