using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Threading;
using WinPieGestures;

internal static partial class Program
{
    private static int checks;
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException(label);
        Console.WriteLine($"PASS {++checks}: {label}");
    }

    private static ActionItem Chain() => new()
    {
        Type = "Hotkey", Name = "Root", SubActions = { new() { Name = "Two", SubActions = {
            new() { Name = "Three", SubActions = { new() { Name = "Four", SubActions = {
                new() { Name = "Five", Type = "Plugin", PluginActionRef = new() { PluginId = "fixture.offline", ContributionId = "select" },
                    ExtensionData = new() { ["retained"] = "yes" } }
            } } } } } } }
    };

    [STAThread]
    private static void Main(string[] args)
    {
        string dir = Directory.CreateDirectory(Path.Combine(Path.GetFullPath(args[0]), Guid.NewGuid().ToString("N"))).FullName;
        foreach (string name in new[] { "LOCALAPPDATA", "APPDATA", "TEMP", "TMP" })
            Environment.SetEnvironmentVariable(name, dir);
        Check(args.Contains("--test-instance"), "isolated instance");
        string path = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "CandidateDll").Value!;
        Check(SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(typeof(WheelProfile).Assembly.Location))), "candidate DLL identity");
        ConfigManager.DisableAutoStartSync = true;
        ConfigManager.CurrentConfig.EnableGlobalInheritance = true;
        var app = new App();
        using var disabled = Dispatcher.CurrentDispatcher.DisableProcessing();
        app.InitializeComponent();

        var layer = JsonSerializer.Deserialize<WheelLayer>("{}")!;
        Check(layer.MaxMenuDepth == 2, "legacy JSON defaults to two");
        layer.MaxMenuDepth = -3;
        Check(layer.Clone().MaxMenuDepth == -3 && layer.EffectiveMaxMenuDepth == 2, "raw low value survives copy");
        layer.MaxMenuDepth = 90;
        Check(layer.EffectiveMaxMenuDepth == 5 && JsonSerializer.Deserialize<WheelLayer>(JsonSerializer.Serialize(layer))!.MaxMenuDepth == 90, "raw high value survives JSON");
        layer.MaxMenuDepth = 5;
        layer.SectorCount = 8;
        var global = new WheelProfile { ProcessName = "Global", Layers = { layer } };
        global.EnsureLayers();
        layer.Actions[2] = Chain();
        int[] full = [2, 0, 0, 0, 0];
        for (int depth = 1; depth <= 5; depth++)
            Check(global.GetEffectiveMenuAction(full[..depth]) != null, $"level {depth} resolves");
        Check(global.GetEffectiveMenuAction([]) == null && global.GetEffectiveMenuAction([2, -1]) == null &&
            global.GetEffectiveMenuAction([2, 0, 0, 0, 0, 0]) == null &&
            global.GetEffectiveMenuAction([2], layerIndex: 8) == null, "reject invalid paths and layers");
        layer.MaxMenuDepth = 2;
        Check(global.GetEffectiveMenuAction(full) == null && global.GetEffectiveMenuAction([2, 0])!.Name == "Two", "depth two hides descendants only");
        layer.MaxMenuDepth = 5;
        var leaf = global.GetEffectiveMenuAction(full)!;
        Check(leaf.ExtensionData!["retained"] == "yes" && leaf.PluginActionRef!.PluginId == "fixture.offline", "restored plugin reference and parameters");
        var copy = global.Clone("Clone");
        copy.GetEffectiveMenuAction(full)!.Name = "changed";
        Check(copy.Layers[0].MaxMenuDepth == 5 && leaf.Name == "Five", "copied profile has independent deep branch");

        var local = new WheelProfile { ProcessName = "example", Layers = { new(), new() { SectorCount = 4, MaxMenuDepth = 5 } }, ActiveLayerIndex = 1 };
        local.EnsureLayers();
        var inherited = local.GetEffectiveMenuAction([1, 0, 0, 0, 0], global);
        Check(inherited?.Name == "Five" && inherited.IsInherited && !leaf.IsInherited, "global layer-zero fallback and four-to-eight direction");
        var sameLayer = layer.Clone();
        sameLayer.Actions[2].SubActions[0].Name = "same layer";
        global.Layers.Add(sameLayer);
        Check(local.GetEffectiveMenuAction([1, 0], global)?.Name == "same layer", "same named-layer index wins");
        local.Actions[1] = new() { Type = "None" };
        Check(local.GetEffectiveMenuAction([1, 0], global) == null, "disabled local root masks global");
        local.Actions[1] = new() { Type = "Hotkey", Parameter = "F14" };
        Check(local.GetEffectiveMenuAction([1, 0], global) == null, "local branch never borrows children");
        layer.Actions[2].SubActions.Add(new() { Type = "None", SubActions = { Chain() } });
        Check(global.GetEffectiveMenuAction([2, 1, 0]) == null, "disabled ancestor blocks descendants");

        string config = JsonSerializer.Serialize(new { Profiles = new[] { global } });
        string package = Path.Combine(dir, "roundtrip.spconfig");
        PortableConfigBundle.Export(config, package, dir);
        using var imported = PortableConfigBundle.PrepareImport(package, Path.Combine(dir, "icons"));
        using var json = JsonDocument.Parse(imported.ConfigJson);
        var restored = json.RootElement.GetProperty("Profiles")[0].Deserialize<WheelProfile>()!;
        Check(restored.Layers[0].MaxMenuDepth == 5 && restored.GetEffectiveMenuAction(full)?.ExtensionData?["retained"] == "yes", "portable bundle preserves five levels");
        long before = ConfigManager.ConfigurationRevision;
        var editor = new SubActionEditorWindow("N", "Root", layer.Actions[2].SubActions, 5);
        for (int i = 0; i < 3; i++) editor.EnterBranch(editor.SubSlots[0]);
        Check(editor.SubSlots[0].Name == "Five" && !editor.SubSlots[0].CanEditChildren, "editor reaches fifth level and prevents a sixth");
        editor.SubSlots[0].Name = "Draft leaf";
        editor.LeaveBranch();
        editor.EnterBranch(editor.SubSlots[0]);
        var draft = editor.GetDraft();
        Check(editor.SubSlots[0].Name == "Draft leaf" && draft[0].SubActions[0].SubActions[0].SubActions[0].Name == "Draft leaf", "back and save draft keep deep edits");
        Check(leaf.Name == "Five" && ConfigManager.ConfigurationRevision == before, "browsing and discarded draft leave source untouched");
        var legacy = new SubActionEditorWindow("N", "Root", layer.Actions[2].SubActions);
        Check(!legacy.SubSlots[0].CanEditChildren, "legacy editor limits depth");
        ConfigManager.CurrentConfig.Profiles = new() { global };
        var settings = new SettingsWindow();
        var picker = (System.Windows.Controls.ComboBox)settings.FindName("MenuDepthPicker");
        Check(picker.SelectedIndex == 3, "actual settings surface shows depth of selected layer");
        long revision = ConfigManager.ConfigurationRevision;
        picker.SelectedIndex = 0;
        Check(layer.MaxMenuDepth == 2 && ConfigManager.ConfigurationRevision > revision, "user change updates active layer immediately");
        settings.ExecuteUndo();
        Check(layer.MaxMenuDepth == 5 && picker.SelectedIndex == 3, "undo restores depth and picker");
        Check(((System.Windows.Controls.Button)settings.FindName("FocusRedoSubActionsBtn")).IsEnabled, "redo is available through actual settings button after undo");
        settings.ExecuteRedo();
        Check(layer.MaxMenuDepth == 2 && picker.SelectedIndex == 0 && leaf.Name == "Five", "redo depth preserves hidden branch");
        var originalSlot = settings.CaptureSectorSnapshot(global, 2);
        leaf.Name = "Renamed fifth level";
        settings.RecordSingleSectorTransaction(global, 2, "MenuEditBranch", originalSlot, settings.CaptureSectorSnapshot(global, 2));
        settings.ExecuteUndo();
        Check(layer.Actions[2].SubActions[0].SubActions[0].SubActions[0].SubActions[0].Name == "Five", "undo restores fifth-level edit");
        settings.ExecuteRedo();
        Check(layer.Actions[2].SubActions[0].SubActions[0].SubActions[0].SubActions[0].Name == "Renamed fifth level", "redo restores fifth-level edit");
        if (args.Length > 2 && File.Exists(args[2]))
        {
            var seed = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(args[2]))!;
            Check(seed.Profiles[0].GetEffectiveMenuAction([0, 0, 0, 0, 1])?.Parameter == "[T8-B]" &&
                seed.Profiles[0].GetEffectiveMenuAction([0, 0, 0], layerIndex: 1) == null, "manual demo and two-level control resolve");
        }
        ExerciseCanvas(settings, global);
        Console.WriteLine($"PASS all {checks} checks");
    }
}
