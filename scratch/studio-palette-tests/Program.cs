using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using WinPieGestures;

internal static class Program
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
        Console.WriteLine($"PASS {++_checks} {message}");
    }

    [STAThread]
    private static void Main(string[] args)
    {
        if (!args.Contains("--test-instance")) throw new InvalidOperationException("Pass --test-instance");
        var isolated = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-editing-" + Guid.NewGuid().ToString("N")));
        foreach (string key in new[] { "APPDATA", "LOCALAPPDATA", "TMP", "TEMP" })
            Environment.SetEnvironmentVariable(key, isolated.FullName);
        var expected = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "CandidateDll").Value!;
        Check(SHA256.HashData(File.ReadAllBytes(expected)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(typeof(SettingsWindow).Assembly.Location))),
            "loaded StarPie.dll matches the specified build");
        ConfigManager.DisableAutoStartSync = true;
        var config = ConfigManager.CurrentConfig;
        config.AutoCheckUpdate = false;
        config.SubmenuStyle = "Wheel";
        config.ShowCoreCircle = true;
        config.EnableMultiTier = true;
        config.EnableGlobalInheritance = true;
        var app = new App();
        using var suspended = Dispatcher.CurrentDispatcher.DisableProcessing();
        app.InitializeComponent();

        ActionItem Item(string name) => new() { Type = "Text", Name = name, Parameter = "fixture-" + name };
        var layer = new WheelLayer { SectorCount = 4, MaxMenuDepth = 5 };
        layer.Actions = [Item("root"), Item("empty branch"), Item("batch A"), Item("batch B")];
        layer.Actions[0].SubActions = [Item("child")];
        layer.Actions[0].SubActions[0].SubActions = [Item("grandchild")];
        layer.Actions[0].SubActions[0].SubActions[0].SubActions = [Item("great-grandchild")];
        var profile = new WheelProfile { ProcessName = "Global", Layers = [layer] };
        profile.EnsureLayers();
        var localLayer = new WheelLayer { SectorCount = 4, MaxMenuDepth = 5, Actions =
            Enumerable.Range(0, 4).Select(_ => new ActionItem { Name = "", Type = "Hotkey" }).ToList() };
        var local = new WheelProfile { ProcessName = "fixture-app", Layers = [localLayer] };
        local.EnsureLayers();
        config.Profiles = [profile, local];
        var window = new SettingsWindow();
        var canvas = (Canvas)window.FindName("MappingsWheelPreviewCanvas");
        var history = (ActionTransactionHistory)typeof(SettingsWindow).GetField("_transactionHistory", Private)!.GetValue(window)!;
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
        object? Invoke(string name, params object?[] values) => typeof(SettingsWindow).GetMethod(name, Private)!.Invoke(window, values);
        string State() => JsonSerializer.Serialize(layer);
        void Roundtrip(string before, string after, int oldCount)
        {
            Check(before != after && history.UndoCount == oldCount + 1, "one transaction for the complete edit");
            window.ExecuteUndo();
            Check(State() == before && history.UndoCount == oldCount, "one undo restores the complete layer");
            window.ExecuteRedo();
            Check(State() == after, "one redo restores the complete edit");
        }
        I18n.CurrentLanguage = LanguageCode.En;
        window.ApplyLocalization();
        window.SelectPrimarySlot(0);
        var old = State();
        var undo = history.UndoCount;
        ((ComboBox)window.FindName("FocusActionTypeComboBox")).SelectedValue = "Hotkey";
        Check(layer.Actions[0].Type == "Hotkey" && layer.Actions[0].SubActions[0].SubActions.Count == 1,
            "the action type picker changes the primary action and retains its descendants");
        Roundtrip(old, State(), undo);

        profile.CenterAction = null;
        profile.EnableCenterAction = false;
        profile.SyncActiveLayerFromRootProperties();
        window.SelectCenterCore();
        Check(profile.CenterAction == null && layer.CenterAction == null, "inspecting an empty center preserves null");
        var enableCenter = (CheckBox)window.FindName("EnableCenterActionCheckBox");
        enableCenter.IsChecked = true;
        Check(profile.GetEffectiveCenterAction()?.Parameter == "OpenSettings", "checking an empty global center explicitly applies the displayed default");
        enableCenter.IsChecked = false;
        profile.CenterAction = null;
        profile.SyncActiveLayerFromRootProperties();
        var staleSelection = (List<int>)typeof(SettingsWindow).GetField("_selectedMultiSlots", Private)!.GetValue(window)!;
        staleSelection.AddRange([0, 1]);
        window.SelectCenterCore();
        Check(staleSelection.Count == 0, "explicit center selection ends a previous batch");
        window.SelectPrimarySlot(1);
        old = State(); undo = history.UndoCount;
        Click((Button)window.FindName("FocusAddFirstSubActionBtn"));
        Check(layer.Actions[1].SubActions.Count == 1 && layer.Actions[1].SubActions[0].Type == "Hotkey",
            "the action editor can add the first secondary action without the dock");
        Roundtrip(old, State(), undo);

        Invoke("OpenLegacyCanvasBranch", 0, 0);
        Click(canvas.Children.OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "DeepCanvasChild0"));
        window.SwitchToTab(2);
        var unnamed = layer.Actions[0].SubActions[0].SubActions[0];
        unnamed.Name = "";
        unnamed.Parameter = "Ctrl+A";
        Invoke("RenderMappingsWheelPreview");
        Check(((Button)window.FindName("StudioBranchCopy")).IsEnabled && ((Button)window.FindName("StudioBranchTest")).IsEnabled, "configured unnamed deep action keeps copy and test available");
        Check((bool)Invoke("TryHandleActionEditingShortcut", System.Windows.Input.Key.C, System.Windows.Input.ModifierKeys.Control, canvas)!, "Ctrl+C routes the deep selection");
        Check(ActionClipboard.Paste().Item1?.Parameter == "Ctrl+A",
            "deep copy resolves the selected branch");
        ActionClipboard.CopySingleAction(Item("paste-deep"));
        old = State(); undo = history.UndoCount;
        Check((bool)Invoke("TryHandleActionEditingShortcut", System.Windows.Input.Key.V, System.Windows.Input.ModifierKeys.Control, canvas)!, "Ctrl+V routes to the deep selection");
        Check(layer.Actions[0].SubActions[0].SubActions[0].Name == "paste-deep" &&
            layer.Actions[0].SubActions[0].SubActions[0].SubActions.Count == 1, "deep paste preserves descendants");
        Roundtrip(old, State(), undo);
        old = State(); undo = history.UndoCount;
        Click(canvas.Children.OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "DeepCanvasAdd"));
        Check(layer.Actions[0].SubActions[0].SubActions.Count == 2, "the deep canvas add button appends exactly one action");
        Roundtrip(old, State(), undo);

        var menu = layer.FixedRings = new FixedRingMenu();
        menu.SetSlot(3, 1, new() { Action = Item("fixed"), ParentIndex = 0 });
        menu.SetSlot(4, 1, new() { Action = Item("fixed child"), ParentIndex = 1 });
        Invoke("OpenFixedCanvas", profile);
        Invoke("SelectFixedSlot", 3, 1);
        ((ComboBox)window.FindName("FixedParent")).SelectedValue = -1;
        Check(menu.GetSlot(3, 1)!.ParentIndex == 0, "unapplied parent assignment is still a draft");
        window.CopyCurrentAction();
        ActionClipboard.CopySingleAction(Item("paste-fixed"));
        old = State(); undo = history.UndoCount;
        window.PasteToCurrentSelection();
        Check(menu.GetSlot(3, 1)?.Action.Name == "paste-fixed" && menu.GetSlot(3, 1)?.ParentIndex == 0 &&
            menu.GetSlot(4, 1)?.ParentIndex == 1, "paste to a fixed slot preserves both sides of its parent mapping");
        Roundtrip(old, State(), undo);
        ((ComboBox)window.FindName("MappingsProfileComboBox")).SelectedItem = local;
        window.SelectPrimarySlot(0);
        string globalBefore = State(), localBefore = JsonSerializer.Serialize(localLayer);
        undo = history.UndoCount;
        Check(local.GetEffectiveAction(0)?.IsInherited == true, "application selection starts with an inherited branch");
        Click((Button)window.FindName("FocusAddSubActionBtn"));
        string localAfter = JsonSerializer.Serialize(localLayer);
        Check(State() == globalBefore && localLayer.Actions[0].SubActions.Count == layer.Actions[0].SubActions.Count + 1 &&
            localLayer.Actions[0].SubActions[0].SubActions.Count == layer.Actions[0].SubActions[0].SubActions.Count &&
            !ReferenceEquals(localLayer.Actions[0].SubActions[0], layer.Actions[0].SubActions[0]) &&
            history.UndoCount == undo + 1, "inherited edit materializes an independent local tree in one transaction");
        window.ExecuteUndo();
        Check(JsonSerializer.Serialize(localLayer) == localBefore && local.GetEffectiveAction(0)?.IsInherited == true &&
            State() == globalBefore, "undo restores the original inheritance and keeps global data intact");
        window.ExecuteRedo();
        Check(JsonSerializer.Serialize(localLayer) == localAfter && State() == globalBefore, "redo restores just the local branch");
        Check(PresentationSource.FromVisual(window) == null && !window.IsVisible, "all checks ran without displaying a window");
        Console.WriteLine($"{_checks} editor checks passed.");
    }
}
