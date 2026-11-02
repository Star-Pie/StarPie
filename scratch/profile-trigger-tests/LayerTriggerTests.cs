using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using WinPieGestures;

internal static class LayerTriggerTests
{
    private static int _checks;
    internal static int Run()
    {
        LayerBaselineScenario.Run();
        Check(true, "common old/new JSON counterexample now routes");
        var global = Profile("Global", "");
        var cad = Profile("CAD", "CAD.EXE");
        var other = Profile("Other", "other.exe");
        var config = new AppConfig { Profiles = new() { global, cad, other }, Trigger = Mouse("RightButton") };
        cad.Layers[0].ActivationTrigger = Mouse("MiddleButton");
        cad.Layers[1].ActivationTrigger = Key(112, ModifierKeys.Control);
        other.Layers[1].ActivationTrigger = Key(112, ModifierKeys.Control);
        global.Layers[1].ActivationTrigger = Mouse("XButton2");
        Publish(config);
        var first = MouseRoute("cad.exe", "MiddleButton")!;
        var second = KeyRoute("CAD", 112, ModifierKeys.Control)!;
        Check(first.Profile == cad && first.Layer == cad.Layers[0], "app/layer0 direct mouse route");
        Check(second.Profile == cad && second.Layer == cad.Layers[1], "app/layer1 direct keyboard route");
        Check(KeyRoute("other.exe", 112, ModifierKeys.Control)?.Layer == other.Layers[1], "same key in different application scope");
        Check(KeyRoute("cad", 112, ModifierKeys.Control | ModifierKeys.Shift) == null, "layer shortcut exact modifiers");
        Check(MouseRoute("unknown.exe", "XButton2")?.Layer == global.Layers[1], "Global layer fallback on unknown app");
        Check(MouseRoute("cad", "XButton2")?.Layer == global.Layers[1], "Global key also available when app has no matching key");
        Check(MouseRoute("cad", "RightButton")?.Profile == cad, "default app trigger still available for new layer-only profiles");

        var controller = (GestureController)RuntimeHelpers.GetUninitializedObject(typeof(GestureController));
        var pinField = typeof(GestureController).GetField("_pinState", BindingFlags.Instance | BindingFlags.NonPublic);
        if (pinField != null) pinField.SetValue(controller, Activator.CreateInstance(pinField.FieldType, nonPublic: true));
        SetActivation(controller, second);
        Check(controller.SelectActivationLayer(cad), "actual presentation selector accepts pinned layer");
        Check(cad.ActiveLayerIndex == 1 && ReferenceEquals(cad.Actions, cad.Layers[1].Actions), "presentation selector selects layer1 action tree instead of resetting0");
        cad.ActiveLayerIndex = 0;
        cad.SyncRootPropertiesFromActiveLayer();
        Check(controller.SelectActivationLayer(cad) && cad.ActiveLayerIndex == 1, "queued presentation reselects captured layer after intervening edit");
        var stableLayer = second.Layer!;
        cad.Layers.Reverse();
        Check(second.TrySelectInitialLayer() && cad.Layers[cad.ActiveLayerIndex] == stableLayer, "route follows layer reference after reorder");
        cad.Layers.Remove(stableLayer);
        Check(!second.IsLayerAvailable && !second.TrySelectInitialLayer(), "deleted layer cannot select another index");
        Check(!controller.SelectActivationLayer(cad), "actual presentation selector rejects deleted layer");
        cad.Layers.Add(stableLayer);
        SetActivation(controller, null);
        cad.ActiveLayerIndex = 1;
        Check(controller.SelectActivationLayer(cad) && cad.ActiveLayerIndex == 0, "old presentation default still layer0");

        cad.ActiveLayerIndex = 1;
        var releasedLayer = GestureController.CaptureReleaseLayer(cad);
        Check(releasedLayer == stableLayer, "release captures deliberately selected layer");
        cad.ActiveLayerIndex = 0;
        cad.SyncRootPropertiesFromActiveLayer();
        Check(GestureController.TryResolveReleasedAction(cad, releasedLayer, 0, -1, false, false, out var releasedAction)
            && releasedAction?.Name == "Two", "queued release uses captured action after layer switch");
        cad.Layers.Reverse();
        Check(GestureController.TryResolveReleasedAction(cad, releasedLayer, 0, -1, false, false, out releasedAction)
            && releasedAction?.Name == "Two", "queued release follows reordered layer reference");
        Check(GestureController.TryResolveReleasedAction(cad, releasedLayer, 0, -1, false, true, out releasedAction)
            && releasedAction == null, "locked submenu release does not execute parent action");
        stableLayer.EnableCenterAction = true;
        stableLayer.CenterAction = new ActionItem { Type = "Hotkey", Name = "Two center", Parameter = "Ctrl+C" };
        Check(GestureController.TryResolveReleasedAction(cad, releasedLayer, -1, -1, false, false, out releasedAction)
            && releasedAction?.Name == "Two center", "center action uses captured layer");
        Check(GestureController.TryResolveReleasedAction(cad, releasedLayer, 0, -1, true, false, out releasedAction)
            && releasedAction == null, "escaped release does not select sector action");
        cad.Layers.Remove(stableLayer);
        Check(!GestureController.TryResolveReleasedAction(cad, releasedLayer, 0, -1, false, false, out releasedAction)
            && releasedAction == null, "deleted release layer cancels without active-layer fallback");
        cad.Layers.Add(stableLayer);

        var snapshot = KeyRoute("cad", 112, ModifierKeys.Control)!;
        cad.Layers[1].ActivationTrigger!.VkCode = 113;
        Check(snapshot.Trigger.VkCode == 112, "in-flight layer trigger copied");
        Publish(config);
        Check(KeyRoute("cad", 113, ModifierKeys.Control)?.Layer == stableLayer, "publication updates layer shortcut");
        Check(KeyRoute("cad", 112, ModifierKeys.Control) == null, "old layer key removed after publication");
        Check(snapshot.Layer == stableLayer && snapshot.Trigger.VkCode == 112, "published edit does not change existing activation");

        var clone = stableLayer.Clone();
        Check(clone.ActivationTrigger != stableLayer.ActivationTrigger, "layer trigger deep clone");
        clone.ActivationTrigger!.RequireAlt = true;
        Check(!stableLayer.ActivationTrigger!.RequireAlt, "layer clone edits independent");
        Check(ProfileTriggerRouter.ValidateLayerTrigger(config, cad, clone, stableLayer.ActivationTrigger) == ProfileTriggerValidationError.Conflict, "copied layer collision detected before insertion");
        var profileClone = cad.Clone("CADCopy");
        Check(ProfileTriggerRouter.ValidateBindings(config, profileClone) == ProfileTriggerValidationError.Conflict, "copied profile checks all layer bindings");
        Check(profileClone.Layers[1].ActivationTrigger != cad.Layers[1].ActivationTrigger, "profile clone owns cloned layer keys");
        Check(ProfileTriggerRouter.ValidateBindings(config, other, "cad.exe") == ProfileTriggerValidationError.None, "different physical keys on new app binding allowed");
        other.Layers[1].ActivationTrigger = Key(113, ModifierKeys.Control);
        Check(ProfileTriggerRouter.ValidateBindings(config, other, "cad.exe") == ProfileTriggerValidationError.Conflict, "binding edit checks layer keys without profile root trigger");
        Check(other.BoundProcesses == "other.exe", "proposed validation leaves original binding intact");

        var roundtrip = JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(config))!;
        Check(roundtrip.Profiles[1].Layers[1].ActivationTrigger?.VkCode == 113, "per-layer JSON roundtrip");
        Check(roundtrip.Profiles[1].Layers[1].ActivationTrigger?.RequireAlt == false, "per-layer false retained");
        Check(JsonSerializer.Deserialize<WheelLayer>("{\"Name\":\"Old\"}")!.ActivationTrigger == null, "missing old layer key remains null");
        Check(JsonSerializer.Deserialize<WheelLayer>("{\"ActivationTrigger\":null}")!.ActivationTrigger == null, "explicit null layer key remains null");

        global.Layers[0].ActivationTrigger = Mouse("MiddleButton");
        global.Layers[1].ActivationTrigger = Mouse("MiddleButton");
        Publish(config);
        Check(ProfileTriggerRouter.TryResolveMouse("unknown", "MiddleButton", ModifierKeys.None, out _) == ProfileTriggerResolution.Conflict, "duplicate Global layer keys fail closed");
        Check(MouseRoute("cad", "MiddleButton")?.Profile == cad, "app-specific route wins even when fallback scope conflicts");
        Check(ProfileTriggerRouter.ValidateLayerTrigger(config, global, global.Layers[1], Mouse("MiddleButton")) == ProfileTriggerValidationError.Conflict, "Global layer collision validates");
        global.Layers[0].ActivationTrigger = null;
        global.Layers[1].ActivationTrigger = Mouse("RightButton");
        Publish(config);
        Check(MouseRoute("cad", "RightButton")?.Profile == cad, "matching app legacy default precedes Global layer");
        Check(MouseRoute("unknown", "RightButton")?.Layer == global.Layers[1], "Global layer overrides its implicit global default key");
        config.BlacklistTriggerOverrides = new(StringComparer.OrdinalIgnoreCase) { ["unknown.exe"] = Mouse("RightButton") };
        Publish(config);
        Check(MouseRoute("unknown.exe", "RightButton")?.IsDedicated == false, "legacy process override remains before Global layer shortcut");

        var legacy = Profile("Legacy", "legacy.exe");
        legacy.ActivationTrigger = Mouse("XButton1");
        config.Profiles.Add(legacy);
        Publish(config);
        Check(MouseRoute("legacy", "XButton1")?.Layer == legacy.Layers[0], "old profile shortcut still opens first layer");
        Check(ProfileTriggerRouter.GetLayerTrigger(legacy, legacy.Layers[0]) == legacy.ActivationTrigger, "old shortcut visible in layer0 editor");
        Check(ProfileTriggerRouter.ValidateLayerTrigger(config, legacy, legacy.Layers[1], Mouse("XButton1")) == ProfileTriggerValidationError.Conflict, "other layer cannot duplicate old layer0 key");
        Check(ProfileTriggerRouter.ValidateLayerTrigger(config, legacy, legacy.Layers[0], Mouse("XButton1")) == ProfileTriggerValidationError.None, "editing old shortcut in layer0 is not a self-conflict");
        legacy.Layers[0].ActivationTrigger = Mouse("XButton2");
        Publish(config);
        Check(MouseRoute("legacy", "XButton2")?.Layer == legacy.Layers[0], "new explicit layer0 replaces old profile shortcut");
        Check(MouseRoute("legacy", "XButton1") == null, "dormant old shortcut does not remain an unexpected second key");
        Check(legacy.ActivationTrigger?.MouseButton == "XButton1", "legacy field preserved without automatic migration");

        foreach (string language in new[] { "zh-CN", "zh-TW", "en", "ja" })
        {
            I18n.SetLanguage(language);
            foreach (string key in new[] { "LayerActivationStatus", "LayerActivationSelect", "LayerActivationUnset", "LayerActivationCopyConflict" })
                Check(I18n.T(key) != key, $"layer localization {language}/{key}");
        }
        I18n.SetLanguage("zh-CN");
        Console.WriteLine($"PASS {_checks} layer-specific checks.");
        return _checks;
    }

    private static WheelProfile Profile(string name, string process) => new() { ProcessName = name, BoundProcesses = process,
        Layers = new() { Layer("One"), Layer("Two") } };
    private static WheelLayer Layer(string name) => new() { Name = name, Actions = new() { new ActionItem { Type = "Hotkey", Name = name, Parameter = "Ctrl+C" } } };
    private static TriggerConfig Mouse(string button) => new() { TriggerType = "Mouse", MouseButton = button };
    private static TriggerConfig Key(uint key, ModifierKeys mods) => new() { TriggerType = "Keyboard", VkCode = key, RequireCtrl = mods.HasFlag(ModifierKeys.Control), RequireShift = mods.HasFlag(ModifierKeys.Shift) };
    private static void Publish(AppConfig config) { ConfigManager.CurrentConfig = config; ProfileTriggerRouter.Publish(config); }
    private static ProfileTriggerActivation? MouseRoute(string process, string button) => ProfileTriggerRouter.TryResolveMouse(process, button, ModifierKeys.None, out var activation) == ProfileTriggerResolution.Match ? activation : null;
    private static ProfileTriggerActivation? KeyRoute(string process, uint key, ModifierKeys mods) => ProfileTriggerRouter.TryResolveKeyboard(process, key, mods, out var activation) == ProfileTriggerResolution.Match ? activation : null;
    private static void SetActivation(GestureController controller, ProfileTriggerActivation? activation) => typeof(GestureController).GetField("_activeActivation", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(controller, activation);
    private static void Check(bool value, string message) { _checks++; if (!value) throw new InvalidOperationException(message); }
}
