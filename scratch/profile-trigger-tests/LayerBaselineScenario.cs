using System.Text.Json;
using System.Windows.Input;
using WinPieGestures;

internal static class LayerBaselineScenario
{
    internal static void Run()
    {
        // Uses APIs already present in 1309. Old JSON silently ignores the new
        // field, so failure is actual routing behavior, not missing API/compiler errors.
        const string json = """
        {"Trigger":{"TriggerType":"Mouse","MouseButton":"RightButton"},"Profiles":[
          {"ProcessName":"Global"},
          {"ProcessName":"CAD","BoundProcesses":"cad.exe","Layers":[
            {"Name":"Sketch"},
            {"Name":"Assembly","ActivationTrigger":{"TriggerType":"Mouse","MouseButton":"XButton2"}}
          ]}
        ]}
        """;
        AppConfig config = JsonSerializer.Deserialize<AppConfig>(json)!;
        ConfigManager.CurrentConfig = config;
        ProfileTriggerRouter.Publish(config);
        var result = ProfileTriggerRouter.TryResolveMouse("cad.exe", "XButton2", ModifierKeys.None, out var activation);
        if (result != ProfileTriggerResolution.Match || activation?.Profile != config.Profiles[1])
            throw new InvalidOperationException("Layer trigger JSON must route XButton2 to CAD, not pass through.");
    }
}
