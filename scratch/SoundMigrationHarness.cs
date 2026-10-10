using System;
using StarPie.Plugin;
using StarPie.Plugin.Sound;
using WinPieGestures;
using WinPieGestures.Plugins;
namespace StarPie.Forensics;

// 宿主控制器只发布真实语义事件；音效行为由生产插件中的同一Router执行。
internal sealed class GestureSoundHarness : IDisposable
{
    private readonly GestureController _controller;
    private readonly SoundEventRouter _router = new();
    private PluginInteractionSession? _session;
    internal GestureSoundHarness(MouseHook hook) => _controller = new GestureController(hook, null);
    internal bool IsGestureActive => _controller.IsGestureActive;
    internal long TestSoundSessionId => _session == null ? 0 : _router.SoundSessionId(_session.SessionId);
    internal void TestSetGestureActive(bool active, long version = 0)
    {
        _controller.TestSetGestureActive(active, version);
        if (!active) return;
        _session = _controller.TestBeginInteractionSession(input =>
        {
            // 悬停/层级专项用例不需要初始Popup；Popup映射由插件SDK测试独立覆盖。
            if (input.Kind != InteractionEventKind.Presented) _router.Handle(input);
            return 1;
        });
        _session.Presented();
    }
    internal void TestQueueHighlightUpdate(int sector, int sub, bool escaped, bool expanded, long version) =>
        _controller.TestQueueHighlightUpdate(sector, sub, escaped, expanded, version);
    internal void TestEndActiveGesture() => _controller.TestEndActiveGesture();
    public void Dispose() => _controller.Dispose();
}

internal static class SoundMigrationContracts
{
    internal static bool CapturedSessionDoesNotRedirect()
    {
        var hook = new MouseHook(); using var controller = new GestureController(hook, null);
        var oldEvents = new System.Collections.Generic.List<InteractionEvent>();
        var newerEvents = new System.Collections.Generic.List<InteractionEvent>();
        var previous = controller.TestBeginInteractionSession(e => { oldEvents.Add(e); return 1; });
        previous.Presented();
        var end = typeof(GestureController).GetMethod("EndActiveGesture", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        object captured = end.Invoke(controller, Array.Empty<object>())!;
        var old = (PluginInteractionSession)captured.GetType().GetField("Item7")!.GetValue(captured)!;
        var current = controller.TestBeginInteractionSession(e => { newerEvents.Add(e); return 1; }); current.Presented();
        old.Commit(new(InteractionTargetKind.Sector, 2)); old.End();
        return oldEvents.Exists(e => e.Kind == InteractionEventKind.ActionCommitted && e.SessionId == previous.SessionId) &&
            !newerEvents.Exists(e => e.Kind == InteractionEventKind.ActionCommitted) && oldEvents.TrueForAll(e => e.SessionId == previous.SessionId);
    }
    internal static bool LegacyFieldsRoundTripWithoutHostAudioProperties()
    {
        string json = System.Text.Json.JsonSerializer.Serialize(ConfigManager.CurrentConfig);
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        node["EnableSoundEffects"] = false; node["SoundVolume"] = 0.25;
        node["CustomSoundProfiles"] = System.Text.Json.Nodes.JsonNode.Parse("[{\"Id\":\"kept\",\"Events\":[{\"SourceType\":3}]}]");
        var loaded = System.Text.Json.JsonSerializer.Deserialize<AppConfig>(node.ToJsonString())!;
        using var saved = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(loaded));
        return typeof(AppConfig).GetProperty("SoundVolume") == null && saved.RootElement.GetProperty("EnableSoundEffects").GetBoolean() == false &&
            saved.RootElement.GetProperty("SoundVolume").GetDouble() == 0.25 && saved.RootElement.GetProperty("CustomSoundProfiles")[0].GetProperty("Id").GetString() == "kept";
    }
    internal static bool AuditionWaitsForCompletion()
    {
        using var started = new System.Threading.ManualResetEventSlim(); using var release = new System.Threading.ManualResetEventSlim();
        SoundEffectManager.ResetTestSeams(); SoundEffectManager.TestMode = true;
        SoundEffectManager.Initialize("Mechanical", 0.6, force: true);
        SoundEffectManager.PlaybackSink = (_, _) => { started.Set(); release.Wait(3000); };
        var task = SoundEffectManager.PlayAuditionAsync(SoundType.ActionExecute, default);
        try { bool waits = started.Wait(3000) && !task.IsCompleted; release.Set(); return waits && task.GetAwaiter().GetResult(); }
        finally { release.Set(); SoundEffectManager.Shutdown(); SoundEffectManager.ResetTestSeams(); SoundEffectManager.TestMode = true; }
    }
    internal static bool RealEscEmitsCancelledAndEnd()
    {
        var hook = new MouseHook(); using var controller = new GestureController(hook, null);
        var events = new System.Collections.Generic.List<InteractionEvent>();
        controller.TestSetGestureActive(true, 901);
        controller.TestBeginInteractionSession(e => { events.Add(e); return 1; }).Presented();
        var input = new GlobalKeyEventArgs(27, System.Windows.Input.ModifierKeys.None);
        typeof(GestureController).GetMethod("KeyboardHook_OnKeyDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(controller, new object?[] { null, input });
        return input.Handled && events.Exists(e => e.Kind == InteractionEventKind.SessionCancelled && e.Reason == "Cancelled") &&
            events.Exists(e => e.Kind == InteractionEventKind.SessionEnded && e.Reason == "Cancelled");
    }
}
