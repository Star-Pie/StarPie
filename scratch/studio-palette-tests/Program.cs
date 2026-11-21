using System.Collections;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
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
        var isolated = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-palette-" + Guid.NewGuid().ToString("N")));
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
        var dock = window.FindName("StudioPalette") as UserControl;
        Check(dock != null, "compiled settings exposes the action dock");
        var panel = (WrapPanel)dock!.FindName("EntryPanel");
        var canvas = (Canvas)window.FindName("MappingsWheelPreviewCanvas");
        var indicator = (TextBlock)window.FindName("MappingsCurrentEditIndicator");
        var history = (ActionTransactionHistory)typeof(SettingsWindow).GetField("_transactionHistory", Private)!.GetValue(window)!;
        var entries = panel.Children.OfType<Button>().ToDictionary(b => AutomationProperties.GetAutomationId(b)["StudioPalette".Length..]);
        Button hotkey = entries["Hotkey"];
        void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, b));
        object? Invoke(string name, params object?[] values) => typeof(SettingsWindow).GetMethod(name, Private)!.Invoke(window, values);
        string State() => JsonSerializer.Serialize(layer);
        Point Interior(Geometry shape)
        {
            var bounds = shape.Bounds;
            for (double x = bounds.Left + .25; x < bounds.Right; x += .75)
                for (double y = bounds.Top + .25; y < bounds.Bottom; y += .75)
                    if (shape.FillContains(new Point(x, y))) return new(x, y);
            throw new InvalidOperationException("no interior point");
        }
        CanvasHitItem Hit(Func<CanvasHitItem, bool> predicate) =>
            ((List<CanvasHitItem>)typeof(SettingsWindow).GetField("_canvasHitItems", Private)!.GetValue(window)!).First(predicate);
        void Start(Button b) => Invoke("BeginPaletteDrag", b.Tag);
        bool Track(Button b, Point point, bool inside, bool drop) => (bool)Invoke("TrackPaletteDrag", b.Tag, point, inside, drop)!;
        void Drop(Button b, Point p, string feedback)
        {
            Start(b);
            Check(Track(b, p, true, false) && indicator.Text.Contains(feedback), "hover identifies " + feedback);
            Check(Track(b, p, true, true), "drop accepts the hover destination");
            Invoke("EndPaletteDrag");
        }
        void Roundtrip(string before, string after, int oldCount)
        {
            Check(before != after && history.UndoCount == oldCount + 1, "one transaction for the complete edit");
            window.ExecuteUndo();
            Check(State() == before && history.UndoCount == oldCount, "one undo restores the complete layer");
            window.ExecuteRedo();
            Check(State() == after, "one redo restores the complete edit");
        }
        Point DeepButtonPoint(string id)
        {
            canvas.Measure(new Size(300, 300));
            canvas.Arrange(new Rect(0, 0, 300, 300));
            var button = canvas.Children.OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == id);
            var point = button.Clip == null ? new Point(button.DesiredSize.Width / 2, button.DesiredSize.Height / 2) : Interior(button.Clip);
            return new Point(Canvas.GetLeft(button) + point.X, Canvas.GetTop(button) + point.Y);
        }
        Check(entries.Count == 10 && hotkey.IsEnabled && entries["AddChild"].IsEnabled, "ten compiled palette controls include available built-ins");
        var macro = (ActionItem)entries["Macro"].Tag.GetType().GetMethod("CreateAction", Private)!.Invoke(entries["Macro"].Tag, null)!;
        Check(macro.Type == "Plugin" && macro.PluginActionRef?.FullId == "starpie.plugin.macro.macro",
            "workflow tile uses the existing official plugin contribution");
        foreach (var language in Enum.GetValues<LanguageCode>())
        {
            I18n.CurrentLanguage = language;
            window.ApplyLocalization();
            Check(entries.Values.Select(b => AutomationProperties.GetName(b)).Distinct().Count() == 10 &&
                AutomationProperties.GetName(entries["Macro"]) == I18n.T("StudioWorkflow") &&
                AutomationProperties.GetName(hotkey) == I18n.T("ActionTypeHotkeyShort"), "localized palette names in " + language);
        }
        I18n.CurrentLanguage = LanguageCode.En;
        window.ApplyLocalization();
        window.SelectPrimarySlot(0);
        var old = State();
        var undo = history.UndoCount;
        var unavailable = entries.Values.Where(button => !button.IsEnabled).ToList();
        foreach (var button in unavailable) Click(button);
        Check(unavailable.Count != 0 && State() == old && history.UndoCount == undo,
            "unavailable plugin entries leave the target and history untouched");
        Click(hotkey);
        Check(layer.Actions[0].Name == I18n.T("ActionTypeHotkeyShort") && layer.Actions[0].SubActions[0].SubActions.Count == 1,
            "click replaces selected primary and retains its descendants");
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
        old = State(); undo = history.UndoCount;
        Click(entries["AddChild"]);
        Check(State() == old && history.UndoCount == undo, "center rejects child creation without history");
        var pos = Interior(Hit(h => h.IsCenter).Geometry);
        Drop(hotkey, pos, I18n.T("CenterCoreTitle"));
        Check(profile.GetEffectiveCenterAction()?.Type == "Hotkey" && layer.EnableCenterAction, "drop enables the effective center");
        Roundtrip(old, State(), undo);

        window.SelectPrimarySlot(0);
        pos = Interior(Hit(h => h.IsSubAction && h.SlotIndex == 0 && h.SubIndex == 0).Geometry);
        old = State(); undo = history.UndoCount;
        Drop(hotkey, pos, "01 › 01");
        Check(layer.Actions[0].SubActions[0].Type == "Hotkey" && layer.Actions[0].SubActions[0].SubActions[0].Name == "grandchild",
            "populated secondary replacement preserves the deeper branch");
        Roundtrip(old, State(), undo);

        window.SelectPrimarySlot(1);
        pos = Interior(Hit(h => !h.IsCenter && !h.IsSubAction && h.SlotIndex == 1).Geometry);
        old = State(); undo = history.UndoCount;
        Start(hotkey);
        Track(hotkey, pos, true, false);
        Check(!Track(hotkey, pos, false, true), "release outside viewport rejects a previously valid hover");
        Invoke("EndPaletteDrag");
        Start(hotkey);
        window.SelectPrimarySlot(2);
        Check(!Track(hotkey, pos, true, true) && State() == old && history.UndoCount == undo,
            "canvas redraw invalidates stale capture with no project edit or history");
        window.SelectPrimarySlot(1);
        old = State(); undo = history.UndoCount;
        Start(hotkey);
        var guides = ((IEnumerable)typeof(SettingsWindow).GetField("_paletteGuides", Private)!.GetValue(window)!)
            .OfType<System.Windows.Shapes.Path>().ToList();
        var vacantPoint = guides.Select(guide => Interior(guide.Data)).First(point =>
            Track(hotkey, point, true, false) && indicator.Text.Contains("02 › 01"));
        Check(Track(hotkey, vacantPoint, true, true) && layer.Actions[1].SubActions[0].Type == "Hotkey",
            "painted vacant secondary accepts its first action");
        Roundtrip(old, State(), undo);
        window.ExecuteUndo();
        window.SelectPrimarySlot(1);
        Click(entries["AddChild"]);
        Check(layer.Actions[1].SubActions.Count == 1 && layer.Actions[1].SubActions[0].Type == "Hotkey", "click adds one direct child");
        Roundtrip(old, State(), undo);

        Invoke("OpenLegacyCanvasBranch", 0, 0);
        pos = DeepButtonPoint("DeepCanvasChild0");
        old = State(); undo = history.UndoCount;
        Drop(hotkey, pos, "01 › 01 › 01");
        Check(layer.Actions[0].SubActions[0].SubActions[0].Type == "Hotkey" &&
            layer.Actions[0].SubActions[0].SubActions[0].SubActions[0].Name == "great-grandchild", "deep drop preserves descendants");
        Roundtrip(old, State(), undo);
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
        pos = DeepButtonPoint("DeepCanvasAdd");
        old = State(); undo = history.UndoCount;
        Drop(hotkey, pos, "01 › 01 › 02");
        Check(layer.Actions[0].SubActions[0].SubActions.Count == 2, "drop on vacant deep destination appends exactly one action");
        Roundtrip(old, State(), undo);

        window.SelectPrimarySlot(2);
        var selection = (List<int>)typeof(SettingsWindow).GetField("_selectedMultiSlots", Private)!.GetValue(window)!;
        selection.Clear(); selection.AddRange([2, 3]);
        old = State(); undo = history.UndoCount;
        Click(hotkey);
        Check(layer.Actions[2].Type == "Hotkey" && layer.Actions[3].Type == "Hotkey" && selection.SequenceEqual([2, 3]),
            "click applies to both selected slots and retains selection");
        Roundtrip(old, State(), undo);
        selection.Clear();

        var menu = layer.FixedRings = new FixedRingMenu();
        menu.SetSlot(3, 1, new() { Action = Item("fixed"), ParentIndex = 0 });
        menu.SetSlot(4, 1, new() { Action = Item("fixed child"), ParentIndex = 1 });
        Invoke("OpenFixedCanvas", profile);
        Invoke("SelectFixedSlot", 3, 1);
        ((ComboBox)window.FindName("FixedParent")).SelectedValue = -1;
        Check(menu.GetSlot(3, 1)!.ParentIndex == 0, "unapplied parent assignment is still a draft");
        pos = Interior(Hit(h => h.FixedLevel == 3 && h.SlotIndex == 1).Geometry);
        old = State(); undo = history.UndoCount;
        Drop(hotkey, pos, "02");
        Check(menu.GetSlot(3, 1)!.Action.Type == "Hotkey" && menu.GetSlot(3, 1)!.ParentIndex == 0 &&
            menu.GetSlot(4, 1)!.ParentIndex == 1, "replacement at a fixed address preserves parent and children");
        Roundtrip(old, State(), undo);
        Invoke("SelectFixedSlot", 3, 1);
        window.CopyCurrentAction();
        ActionClipboard.CopySingleAction(Item("paste-fixed"));
        old = State(); undo = history.UndoCount;
        window.PasteToCurrentSelection();
        Check(menu.GetSlot(3, 1)?.Action.Name == "paste-fixed" && menu.GetSlot(3, 1)?.ParentIndex == 0 &&
            menu.GetSlot(4, 1)?.ParentIndex == 1, "paste to a fixed slot preserves both sides of its parent mapping");
        Roundtrip(old, State(), undo);
        pos = Interior(Hit(h => h.FixedLevel == 5 && h.SlotIndex == 3).Geometry);
        old = State(); undo = history.UndoCount;
        Drop(hotkey, pos, "04");
        Check(menu.GetSlot(5, 3)?.Action.Type == "Hotkey", "vacant fifth-level slot receives the dropped action");
        Roundtrip(old, State(), undo);
        ((ComboBox)window.FindName("MappingsProfileComboBox")).SelectedItem = local;
        window.SelectPrimarySlot(0);
        string globalBefore = State(), localBefore = JsonSerializer.Serialize(localLayer);
        undo = history.UndoCount;
        Check(local.GetEffectiveAction(0)?.IsInherited == true, "application selection starts with an inherited branch");
        Click(entries["AddChild"]);
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
    }
}
