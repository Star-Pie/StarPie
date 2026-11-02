using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Input;
using System.Xml.Linq;
using WinPieGestures;

internal static class Program
{
    private static int _checks;
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int Main(string[] args)
    {
        try
        {
            var candidate = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(a => a.Key == "StarPieCandidateDir").Value!;
            var expected = Path.Combine(candidate, "StarPie.dll");
            var loaded = typeof(WheelProfile).Assembly.Location;
            Check(Path.GetFullPath(loaded).Equals(Path.Combine(AppContext.BaseDirectory, "StarPie.dll"), StringComparison.OrdinalIgnoreCase), "fresh probe host location");
            Check(Hash(expected) == Hash(loaded), "candidate SHA256 binding");
            Console.WriteLine($"CANDIDATE={expected}; SHA256={Hash(expected)}");
            Routing();
            ValidationAndCompatibility();
            InputStateWithoutNativeActions();
            _checks += LayerTriggerTests.Run();
            if (args.Length != 0) UiStructure(args[0]);
            Console.WriteLine($"PASS {_checks} checks; no GUI, hooks, plugin actions or input injection.");
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"FAIL after {_checks}: {e}");
            return 1;
        }
    }

    private static void Routing()
    {
        var global = Profile("Global", "", null);
        var sculpt = Profile("Sculpt", "Blender.EXE", Mouse("RightButton"));
        var animate = Profile("Animate", "blender", Mouse("MiddleButton"));
        var keys = Profile("Keys", "blender.exe", Key(112, ModifierKeys.Control));
        var config = Config(global, sculpt, animate, keys);
        Publish(config);
        Check(ResolveMouse("blender.exe", "RightButton")?.Profile == sculpt, "right opens sculpt");
        Check(ResolveMouse("BLENDER", "MiddleButton")?.Profile == animate, "middle opens animation and case alias");
        Check(ResolveKey("BlEnDeR.ExE", 112, ModifierKeys.Control)?.Profile == keys, "keyboard route with exact chord");
        Check(ResolveKey("blender", 112, ModifierKeys.None) == null, "missing required modifier passes through");
        Check(ResolveKey("blender", 112, ModifierKeys.Control | ModifierKeys.Shift) == null, "extra modifier passes through");
        Check(ResolveKey("blender", 113, ModifierKeys.Control) == null, "wrong key passes through");
        Check(ResolveMouse("blender", "MiddleButton", ModifierKeys.Shift) == null, "extra mouse modifier passes through");
        Check(ResolveMouse("other.exe", "RightButton")?.Profile == global, "default app fallback");
        Check(ConfigManager.GetProfileForProcess("blender.exe") == global, "dedicated profiles do not steal default process routing");

        var a = ResolveMouse("blender.exe", "RightButton")!;
        var b = ResolveMouse("blender.exe", "RightButton")!;
        Check(!ReferenceEquals(a, b), "each press has distinct activation identity");
        Check(a.IsSameRoute(b), "same physical route remains comparable for debounce");
        Check(!a.IsSameRoute(ResolveMouse("blender", "MiddleButton")), "different wheel is not bounce");
        sculpt.ActivationTrigger!.MouseButton = "XButton2";
        Check(a.Trigger.MouseButton == "RightButton", "in-flight trigger copied");
        Check(ResolveMouse("blender.exe", "RightButton")?.Profile == sculpt, "published route is frozen until config publication");
        Publish(config);
        Check(ResolveMouse("blender", "XButton2")?.Profile == sculpt, "republish applies edited binding");
        Check(ResolveMouse("blender", "RightButton")?.Profile == global, "old explicit key returns to legacy default");
        Check(a.Trigger.MouseButton == "RightButton" && a.Profile == sculpt, "existing press survives publication");

        var duplicate = Profile("Conflict", "BLENDER; chrome.exe", Mouse("MiddleButton"));
        config.Profiles.Add(duplicate);
        Publish(config);
        Check(ProfileTriggerRouter.TryResolveMouse("blender.exe", "MiddleButton", ModifierKeys.None, out var ambiguous) == ProfileTriggerResolution.Conflict && ambiguous == null, "imported collision fails closed");
        Check(ProfileTriggerRouter.HasMouseTrigger("blender.exe", "MiddleButton", ModifierKeys.None), "ambiguous wheel input stays reserved from raw gestures");
        Check(ResolveMouse("chrome", "MiddleButton")?.Profile == duplicate, "non-overlapping alias remains routable");
        config.Profiles.Remove(duplicate);
        Publish(config);
        Check(ResolveMouse("blender", "MiddleButton")?.Profile == animate, "deletion removes conflict immediately");
        animate.DisplayName = "Renamed";
        Publish(config);
        Check(ResolveMouse("blender.exe", "MiddleButton")?.Profile == animate, "rename with explicit bindings preserves routing");

        var legacy = Profile("Legacy", "old.exe", null);
        var old = Config(global, legacy);
        old.Trigger = Mouse("RightButton", ModifierKeys.Control);
        old.BlacklistTriggerOverrides = new(StringComparer.OrdinalIgnoreCase) { ["old.exe"] = Key(20, ModifierKeys.Shift) };
        Publish(old);
        Check(ResolveKey("OLD", 20, ModifierKeys.Shift | ModifierKeys.Control)?.Profile == legacy, "legacy override keeps minimum-required modifier matching");
        Check(ResolveMouse("other.exe", "RightButton", ModifierKeys.Control | ModifierKeys.Alt)?.Profile == global, "legacy global accepts extra modifiers");
        Check(ResolveMouse("old", "RightButton", ModifierKeys.Control) == null, "override replaces global trigger in bound app");
        var legacyActivation = ResolveKey("old", 20, ModifierKeys.Shift)!;
        Check(ProfileTriggerRouter.AreEquivalent(legacyActivation.Trigger, old.BlacklistTriggerOverrides["old.exe"]), "copied override retains value authorization");
        var changed = Key(20, ModifierKeys.None);
        Check(!ProfileTriggerRouter.AreEquivalent(changed, old.BlacklistTriggerOverrides["old.exe"]), "similar but unapproved override rejected");
        Publish(config);
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < 50000; i++) ProfileTriggerRouter.TryResolveMouse("BLENDER.EXE", "MiddleButton", ModifierKeys.None, out _);
        watch.Stop();
        Console.WriteLine($"Pure routing 50000 matching lookups: {watch.Elapsed.TotalMilliseconds:F2} ms (not hook-to-render latency).");
    }

    private static void ValidationAndCompatibility()
    {
        var global = Profile("Global", "", Mouse("XButton1"));
        var first = Profile("First", "app.exe, second.exe", Key(20));
        var second = Profile("Second", "else.exe", Key(20));
        var config = Config(global, first, second);
        Publish(config);
        Check(ProfileTriggerRouter.ValidateProfileTrigger(config, global, Mouse("LeftButton")) == ProfileTriggerValidationError.GlobalProtected, "Global protected");
        Check(ResolveMouse("other.exe", "XButton1") == null, "malformed Global dedicated field ignored");
        Check(ProfileTriggerRouter.ValidateProfileTrigger(config, second, Key(20)) == ProfileTriggerValidationError.None, "same key allowed across apps");
        Check(ProfileTriggerRouter.ValidateProfileTrigger(config, second, Key(20), "APP") == ProfileTriggerValidationError.Conflict, "proposed binding collision rejected before write");
        Check(second.BoundProcesses == "else.exe", "validation does not mutate binding");
        foreach (uint vk in new uint[] { 0, 27, 16, 17, 18, 91, 92, 160, 161, 162, 163, 164, 165 })
            Check(ProfileTriggerRouter.ValidateProfileTrigger(config, second, Key(vk)) == ProfileTriggerValidationError.InvalidTrigger, $"invalid dedicated key {vk}");
        foreach (uint vk in new uint[] { 20, 65, 112, 135, 192, 186, 32, 9 })
            Check(ProfileTriggerRouter.ValidateProfileTrigger(config, second, Key(vk, ModifierKeys.Control)) == ProfileTriggerValidationError.None, $"valid dedicated key {vk}");
        var malformed = Mouse("RightButton"); malformed.TriggerType = "unknown";
        Check(ProfileTriggerRouter.ValidateProfileTrigger(config, second, malformed) == ProfileTriggerValidationError.InvalidTrigger, "unknown trigger type rejected");
        Check(ProfileTriggerRouter.ValidateProfileTrigger(config, second, Mouse("bad")) == ProfileTriggerValidationError.InvalidTrigger, "invalid mouse button rejected");
        first.ActivationTrigger = new TriggerConfig { TriggerType = "mouse", MouseButton = "middlebutton" };
        Publish(config);
        var normalized = ResolveMouse("APP.EXE", "MiddleButton")!;
        Check(normalized.Trigger.TriggerType == "Mouse" && normalized.Trigger.MouseButton == "MiddleButton", "canonical trigger reaches exact runtime checks");
        var clone = first.Clone("Copy");
        Check(clone.ActivationTrigger != first.ActivationTrigger, "clone trigger independence");
        clone.ActivationTrigger!.RequireAlt = true;
        Check(!first.ActivationTrigger.RequireAlt, "clone edits do not change original");
        Check(ProfileTriggerRouter.ValidateProfileTrigger(config, clone, first.ActivationTrigger) == ProfileTriggerValidationError.Conflict, "copy conflict detectable before insertion");
        var json = JsonSerializer.Serialize(config);
        var roundtrip = JsonSerializer.Deserialize<AppConfig>(json)!;
        Check(roundtrip.Profiles[1].ActivationTrigger?.MouseButton == "middlebutton", "optional trigger roundtrip");
        Check(roundtrip.Profiles[1].ActivationTrigger?.RequireAlt == false, "explicit false roundtrip");
        var old = JsonSerializer.Deserialize<WheelProfile>("{\"ProcessName\":\"old.exe\",\"BoundProcesses\":\"old.exe\"}")!;
        Check(old.ActivationTrigger == null, "old missing field inherits");
        Check(JsonSerializer.Deserialize<WheelProfile>("{\"ProcessName\":\"old.exe\",\"ActivationTrigger\":null}")!.ActivationTrigger == null, "explicit null inherits");
        foreach (var language in new[] { "zh-CN", "zh-TW", "en", "ja" })
        {
            I18n.SetLanguage(language);
            foreach (var key in new[] { "ProfileActivationRecord", "ProfileActivationHelp", "ProfileActivationInvalid", "ProfileActivationConflict", "ProfileActivationCopyConflict" })
                Check(I18n.T(key) != key && !string.IsNullOrWhiteSpace(I18n.T(key)), $"localized {language}/{key}");
        }
        I18n.SetLanguage("zh-CN");
    }

    private static void InputStateWithoutNativeActions()
    {
        var global = Profile("Global", "", null);
        var profile = Profile("Keyboard", "app.exe", Key(20, ModifierKeys.Control));
        Publish(Config(global, profile));
        var first = ResolveKey("app.exe", 20, ModifierKeys.Control)!;
        var next = ResolveKey("app.exe", 20, ModifierKeys.Control)!;
        var mouseFirst = new ProfileTriggerActivation(profile, Mouse("RightButton"), ProfileTriggerSignature.ForMouse("RightButton", ModifierKeys.None), true, "app.exe");
        var mouseNext = new ProfileTriggerActivation(profile, Mouse("RightButton"), ProfileTriggerSignature.ForMouse("RightButton", ModifierKeys.None), true, "app.exe");
        var controller = (GestureController)RuntimeHelpers.GetUninitializedObject(typeof(GestureController));
        // This fixture bypasses field initializers; provide the newly added input owner too.
        var pinField = typeof(GestureController).GetField("_pinState", BindingFlags.Instance | BindingFlags.NonPublic);
        if (pinField != null) pinField.SetValue(controller, Activator.CreateInstance(pinField.FieldType, nonPublic: true));
        // Do not run constructors, hooks, EndActiveGesture, cancellation, or any
        // path that releases modifiers/queues an actual action.
        Set(controller, "_mouseReleaseDebounceLock", new object());
        Set(controller, "_longPressLock", new object());
        Set(controller, "_activeActivation", mouseFirst);
        Set(controller, "_activeTrigger", first.Trigger);
        Set(controller, "_mouseReleasePending", true);
        Set(controller, "_pendingMouseReleaseActivation", mouseFirst);
        Check((bool)Invoke(controller, "TryCancelPendingMouseReleaseAsBounce", mouseNext, null)!, "new down of same mouse route resumes debounce mechanics");
        Check(ReferenceEquals(Get(controller, "_activeActivation"), mouseFirst), "bounce retains original session identity");
        Set(controller, "_mouseReleasePending", true);
        Set(controller, "_pendingMouseReleaseActivation", mouseFirst);
        var different = new ProfileTriggerActivation(global, Mouse("MiddleButton"), ProfileTriggerSignature.ForMouse("MiddleButton", ModifierKeys.None), false, "app.exe");
        Check(!(bool)Invoke(controller, "TryCancelPendingMouseReleaseAsBounce", different, null)!, "different physical trigger cannot consume release debounce");
        var target = new TargetWindowInfo((nint)123, 10, "app.exe", true);
        var same = new TargetWindowInfo((nint)123, 10, "app.exe", true);
        Check(ProfileTriggerActivation.IsSameTarget(target, same), "same target can debounce");
        Check(!ProfileTriggerActivation.IsSameTarget(target, new TargetWindowInfo((nint)124, 10, "app.exe", true)), "different window of same process cannot debounce");
        Check(!ProfileTriggerActivation.IsSameTarget(target, new TargetWindowInfo((nint)123, 11, "app.exe", true)), "reused HWND with new PID cannot debounce");
        Check(!ProfileTriggerActivation.IsSameTarget(TargetWindowInfo.None, TargetWindowInfo.None), "invalid target cannot debounce");
        Set(controller, "_activeActivation", first);
        Set(controller, "_isGestureActive", true);
        var repeat = new GlobalKeyEventArgs(20, ModifierKeys.Control);
        Invoke(controller, "KeyboardHook_OnKeyDown", null, repeat);
        Check(repeat.Handled && ReferenceEquals(Get(controller, "_activeActivation"), first), "active repeated key cannot open another gesture");
        var unrelated = new GlobalKeyEventArgs(112, ModifierKeys.None);
        Invoke(controller, "KeyboardHook_OnKeyDown", null, unrelated);
        Check(!unrelated.Handled && ReferenceEquals(Get(controller, "_activeActivation"), first), "another trigger passes through without replacing owner");
        Invoke(controller, "KeyboardHook_OnKeyUp", null, unrelated);
        Check(ReferenceEquals(Get(controller, "_activeActivation"), first), "unrelated keyup cannot finish owner");
        Set(controller, "_isGestureActive", false);
        Set(controller, "_isWaitingForThreshold", true);
        Set(controller, "_kbTriggerWaiting", true);
        Invoke(controller, "KeyboardHook_OnKeyUp", null, new GlobalKeyEventArgs(20, ModifierKeys.Control));
        Check(Get(controller, "_activeActivation") == null && Get(controller, "_activeTrigger") == null, "quick tap clears pinned keyboard trigger without native replay");
        Check(ProfileTriggerRouter.IsKeyboardRelease(first.Trigger, 162), "required modifier release ends hold");
        Check(!ProfileTriggerRouter.IsKeyboardRelease(first.Trigger, 164), "unrequired modifier release does not end hold");
        var evt = new WinPieGestures.MouseEventArgs(1, 2) { TriggerActivation = first };
        evt.Update(3, 4);
        Check(evt.TriggerActivation == null, "reused event args clear activation");
        var modifierOnly = new ProfileTriggerActivation(global, Key(0, ModifierKeys.Control),
            ProfileTriggerSignature.ForKeyboard(65, ModifierKeys.Control), false, "app.exe");
        Set(controller, "_activeActivation", modifierOnly);
        Set(controller, "_activeTrigger", modifierOnly.Trigger);
        Set(controller, "_isGestureActive", true);
        var legacyRepeat = new GlobalKeyEventArgs(65, ModifierKeys.Control);
        Invoke(controller, "KeyboardHook_OnKeyDown", null, legacyRepeat);
        Check(legacyRepeat.Handled, "legacy modifier-only active repeat keeps its original swallowing behavior");
    }

    private static void UiStructure(string root)
    {
        var document = XDocument.Load(Path.Combine(root, "WinPieGestures/SettingsWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement Named(string name) => document.Descendants().Single(e => (string?)e.Attribute("Name") == name || (string?)e.Attribute(x + "Name") == name);
        var activation = Named("ProfileActivationPanel");
        var identification = Named("ApplicationIdentificationPanel");
        Check((string?)activation.Attribute("Visibility") == "Collapsed", "profile trigger defaults hidden");
        Check(activation.Parent == identification.Parent, "one shared toolbar available outside both editing view modes");
        Check(activation.Parent!.Name.LocalName == "Grid" && (string?)activation.Attribute("{http://schemas.microsoft.com/winfx/2006/xaml/presentation}Grid.Column") == null
            && (string?)activation.Attribute("Grid.Column") == "1", "trigger toolbar shares identification row in the right column");
        Check(activation.Ancestors().All(e => (string?)e.Attribute("Name") != "MappingsCanvasModeGrid" && (string?)e.Attribute("Name") != "MappingsListModeGrid"), "entry not stranded in one view");
        Check((string?)identification.Attribute("HorizontalAlignment") == "Left", "accepted compact identification layout retained");
    }

    private static TriggerConfig Mouse(string button, ModifierKeys mods = ModifierKeys.None) => Trigger("Mouse", button, 0, mods);
    private static TriggerConfig Key(uint vk, ModifierKeys mods = ModifierKeys.None) => Trigger("Keyboard", "RightButton", vk, mods);
    private static TriggerConfig Trigger(string type, string button, uint vk, ModifierKeys mods) => new() {
        TriggerType = type, MouseButton = button, VkCode = vk, Key = vk == 0 ? "None" : KeyInterop.KeyFromVirtualKey((int)vk).ToString(),
        RequireCtrl = mods.HasFlag(ModifierKeys.Control), RequireShift = mods.HasFlag(ModifierKeys.Shift),
        RequireAlt = mods.HasFlag(ModifierKeys.Alt), RequireWin = mods.HasFlag(ModifierKeys.Windows) };
    private static WheelProfile Profile(string name, string bound, TriggerConfig? trigger) => new() { ProcessName = name, DisplayName = name, BoundProcesses = bound, ActivationTrigger = trigger };
    private static AppConfig Config(params WheelProfile[] profiles) => new() { Profiles = profiles.ToList(), Trigger = Mouse("RightButton") };
    private static void Publish(AppConfig config) { ConfigManager.CurrentConfig = config; ProfileTriggerRouter.Publish(config); }
    private static ProfileTriggerActivation? ResolveMouse(string app, string button, ModifierKeys mods = ModifierKeys.None) => ProfileTriggerRouter.TryResolveMouse(app, button, mods, out var activation) == ProfileTriggerResolution.Match ? activation : null;
    private static ProfileTriggerActivation? ResolveKey(string app, uint vk, ModifierKeys mods) => ProfileTriggerRouter.TryResolveKeyboard(app, vk, mods, out var activation) == ProfileTriggerResolution.Match ? activation : null;
    private static object? Invoke(object obj, string name, params object?[] args) => obj.GetType().GetMethod(name, Fields)!.Invoke(obj, args);
    private static object? Get(object obj, string name) => obj.GetType().GetField(name, Fields)!.GetValue(obj);
    private static void Set(object obj, string name, object? value) => obj.GetType().GetField(name, Fields)!.SetValue(obj, value);
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static void Check(bool value, string message) { _checks++; if (!value) throw new InvalidOperationException(message); }
}
