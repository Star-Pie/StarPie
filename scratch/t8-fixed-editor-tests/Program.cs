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

internal static partial class Program
{
    private static int checks;
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        Console.WriteLine($"PASS {++checks}: {label}");
    }

    [STAThread]
    private static void Main(string[] args)
    {
        if (!args.Contains("--test-instance")) throw new InvalidOperationException("Use --test-instance.");
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "starpie-fixed-editor", Guid.NewGuid().ToString("N"))).FullName;
        foreach (var key in new[] { "APPDATA", "LOCALAPPDATA", "TEMP", "TMP" }) Environment.SetEnvironmentVariable(key, dir);
        string path = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "CandidateDll").Value!;
        Check(SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(typeof(WheelProfile).Assembly.Location))), "selected release matches loaded assembly");
        ConfigManager.DisableAutoStartSync = true;
        ConfigManager.CurrentConfig.AutoCheckUpdate = false;
        var app = new App();
        using var disabled = Dispatcher.CurrentDispatcher.DisableProcessing();
        app.InitializeComponent();
        ExerciseFixedLayoutChoices();
        var layer = new WheelLayer { MaxMenuDepth = 5, SectorCount = 4 };
        layer.Actions.Add(new ActionItem { Type = "Hotkey", Name = "Root A", Parameter = "F13" });
        layer.Actions.Add(new ActionItem { Type = "Hotkey", Name = "Root B", Parameter = "F14" });
        for (int i = 0; i < 5; i++)
            layer.Actions[0].SubActions.Add(new ActionItem { Type = "Hotkey", Name = $"A child {i}", Parameter = "F15" });
        layer.Actions[1].SubActions.Add(new ActionItem { Type = "Hotkey", Name = "B child", Parameter = "F16" });
        ConfigManager.CurrentConfig.SubmenuStyle = "Wheel";
        ConfigManager.CurrentConfig.AutoExpandSubRingsOnPopup = true;
        var global = new WheelProfile { ProcessName = "Global", Layers = { layer, new WheelLayer { SectorCount = 4 } } };
        global.EnsureLayers();
        ConfigManager.CurrentConfig.Profiles = [global];
        var window = new SettingsWindow();
        T Find<T>(string id) where T : FrameworkElement => window.FindName(id) as T ??
            throw new InvalidOperationException("Missing compiled settings control: " + id);
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
        void Press(string id) => Click(Find<Button>(id));
        var canvas = Find<Canvas>("MappingsWheelPreviewCanvas");
        var toggle = Find<Button>("FixedLayoutToggle");
        var level = Find<ComboBox>("FixedLevel");
        var count = Find<ComboBox>("FixedCount");
        var display = Find<ComboBox>("FixedDisplay");
        var editor = Find<CanvasMenuActionEditor>("FixedEditor");
        var name = (TextBox)editor.FindName("NameBox");
        var type = (ComboBox)editor.FindName("TypeBox");
        var text = (TextBox)editor.FindName("ParameterBox");
        JsonElement Tier(int d) => JsonSerializer.SerializeToElement(layer).GetProperty("FixedRings").GetProperty("Tiers")[d - 3];
        JsonElement Slot(int d, int i) => Tier(d).GetProperty("Slots")[i];
        int? LegacyParent(int d, int i) => Slot(d, i).TryGetProperty("ParentIndex", out var value) &&
            value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;
        string Id(Button b) => AutomationProperties.GetAutomationId(b);
        var geo = new Size(300, 300);
        void Select(int d, int i)
        {
            canvas.Measure(geo);
            canvas.Arrange(new Rect(geo));
            var b = canvas.Children.OfType<Button>().Single(x => Id(x) == $"FixedRing{d}Slot{i}");
            double x0 = Canvas.GetLeft(b), y0 = Canvas.GetTop(b);
            for (double x = x0 + .5; x < x0 + b.Width; x++)
                for (double y = y0 + .5; y < y0 + b.Height; y++)
                    if (VisualTreeHelper.HitTest(canvas, new Point(x, y))?.VisualHit == b) { Click(b); return; }
            throw new InvalidOperationException("No painted hit for " + Id(b));
        }
        var distance = Find<Slider>("SubWheelTriggerDistanceSlider");
        double originalDistance = distance.Value;
        distance.Value = originalDistance == 180 ? 181 : 180;
        Check(ConfigManager.CurrentConfig.SubWheelTriggerDistance == distance.Value && Find<TextBlock>("SubWheelTriggerDistanceValueText").Text.Contains(distance.Value.ToString("0")),
            "global expansion-distance control updates the existing configuration value and its readout");
        distance.Value = originalDistance;
        var originalLanguage = I18n.CurrentLanguage;
        foreach (var (language, secondary, proximity) in new[] { (LanguageCode.ZhCn, "二级轮盘", "光标靠近"),
            (LanguageCode.ZhTw, "二級輪盤", "游標靠近"), (LanguageCode.En, "Level 2", "pointer approaches"),
            (LanguageCode.Ja, "第2階層", "ポインターを近づける") })
        {
            I18n.CurrentLanguage = language;
            window.ApplyLocalization();
            Check(Find<TextBlock>("SubWheelTriggerDistLabel").Text.Contains(secondary) &&
                Find<TextBlock>("SubWheelTriggerDistDesc").Text.Contains(proximity),
                $"distance slider describes the second tier and explains outer-ring proximity in {language}");
        }
        I18n.CurrentLanguage = originalLanguage;
        window.ApplyLocalization();
        Check(toggle.IsEnabled && Find<Border>("FixedControlsCard").Visibility == Visibility.Collapsed, "legacy layer has an explicit entry, with its original canvas");
        Click(toggle);
        Check(Find<Border>("FixedControlsCard").Visibility == Visibility.Visible, "fixed outer editor opens from the explicit entry");
        Check(level.Items.Cast<int>().SequenceEqual(new[] { 3, 4, 5 }) &&
            new[] { "FixedWakeControls", "FixedTransfer", "FixedWidth", "FixedDelay", "FixedParentLabel", "FixedParent" }.All(id => window.FindName(id) == null),
            "fixed editor keeps tiers 3–5 without parent or per-transition wake controls");
        ConfigManager.CurrentConfig.AutoExpandSubRingsOnPopup = false;
        Press("FixedBack");
        Press("FixedOpenCanvas");
        Check(display.SelectedIndex == 0 && Equals(display.Items[0], I18n.T("FixedAll")) &&
              layer.FixedRings!.AlwaysVisible && !ConfigManager.CurrentConfig.AutoExpandSubRingsOnPopup &&
              canvas.Children.OfType<Button>().Any(b => Id(b).StartsWith("FixedRing5Slot")),
              "resident choice remains unconditional and vacant slots stay editable with the legacy switch off");
        ConfigManager.CurrentConfig.AutoExpandSubRingsOnPopup = true;
        Press("FixedBack");
        Press("FixedOpenCanvas");
        long revision = ConfigManager.ConfigurationRevision;
        Select(5, 0);
        Check(Equals(level.SelectedItem, 5) && Equals(count.SelectedItem, 20) && name.Text == "" &&
            ConfigManager.ConfigurationRevision == revision, "single click selects empty fifth-level slot without a write or drill-down");
        for (int d = 3; d <= 5; d++)
        {
            level.SelectedItem = d;
            Check(Equals(count.SelectedItem, new[] { 12, 16, 20 }[d - 3]) &&
                Equals(count.Items[count.Items.Count - 1], new[] { 23, 30, 37 }[d - 3]), "independent count defaults and limits at level " + d);
        }
        level.SelectedItem = 3;
        Select(3, 7);
        type.SelectedValue = "Text";
        name.Text = "Assigned A";
        text.Text = "safe fixture";
        Check(ConfigManager.ConfigurationRevision == revision, "typing in a vacant slot leaves saved configuration untouched");
        Press("FixedApply");
        Check(Slot(3, 7).GetProperty("Action").GetProperty("Parameter").GetString() == "safe fixture" &&
            LegacyParent(3, 7) == null && Tier(3).GetProperty("Slots").GetArrayLength() == 8,
            "apply stores an action at its fixed index without creating a parent link");
        display.SelectedIndex = 1;
        Select(3, 7);
        Check(!canvas.Children.OfType<System.Windows.Shapes.Path>().Any() &&
            canvas.Children.OfType<Button>().All(b => b.Effect == null) && name.Text == "Assigned A",
            "a third-level slot has no extra activation-band geometry or shadow");
        display.SelectedIndex = 0;
        Select(4, 11);
        type.SelectedValue = "Text";
        name.Text = "Child";
        text.Text = "fourth level fixture";
        Press("FixedApply");
        Check(LegacyParent(4, 11) == null, "editing a vacant next tier ignores the previously selected third-tier slot");
        var importedAction = layer.FixedRings!.GetSlot(4, 11)!.Action.Clone();
        layer.FixedRings.SetSlot(4, 11, new FixedRingSlot { Action = importedAction, ParentIndex = 7 });
        Select(3, 7);
        count.SelectedItem = 4;
        Check(!canvas.Children.OfType<Button>().Any(b => Id(b) == "FixedRing3Slot7") &&
            Slot(3, 7).GetProperty("Action").GetProperty("Name").GetString() == "Assigned A", "shrinking hides slot without deleting its action");
        Select(4, 11);
        Check(name.Text == "Child" && LegacyParent(4, 11) == 7 &&
            Find<TextBlock>("MappingsCurrentEditIndicator").Text == "Child", "legacy metadata is retained and does not set the canvas selection path");
        text.Text = "edited legacy fixture";
        Press("FixedApply");
        Check(LegacyParent(4, 11) == 7 && Slot(4, 11).GetProperty("Action").GetProperty("Parameter").GetString() == "edited legacy fixture",
            "editing an existing action preserves its serialized legacy metadata");
        level.SelectedItem = 3;
        count.SelectedItem = 8;
        Select(3, 7);
        Check(name.Text == "Assigned A" && LegacyParent(3, 7) == null, "expanding restores the fixed action");
        Select(3, 0);
        name.Text = "Temporary";
        Select(3, 7);
        Select(3, 0);
        Check(name.Text == "Temporary" && Slot(3, 0).ValueKind == JsonValueKind.Null, "per-address draft survives navigation without publishing it");
        display.SelectedIndex = 1;
        Check(!canvas.Children.OfType<Button>().Any(b => Id(b).StartsWith("FixedRing4")),
            "progressive editor shows selected ring and the inner rings");
        level.SelectedItem = 4;
        Select(4, 11);
        Check(name.Text == "Child" && Find<TextBlock>("MappingsCurrentEditIndicator").Text == "Child",
            "selecting a fourth-level slot shows its action without a legacy parent breadcrumb");
        display.SelectedIndex = 0;
        Select(3, 7);
        Check(LegacyParent(3, 7) == null && canvas.Children.OfType<Button>().Any(b => Id(b).StartsWith("FixedRing5Slot")),
            "actions without legacy ancestry and always-visible mode persist");
        Click(toggle);
        Check(Find<Border>("FixedControlsCard").Visibility == Visibility.Collapsed, "pause restores legacy canvas");
        Click(toggle);
        Select(3, 7);
        Check(name.Text == "Assigned A" && LegacyParent(3, 7) == null, "reactivation retains fixed assignments");
        Press("FixedClear");
        Check(Slot(3, 7).ValueKind == JsonValueKind.Null && LegacyParent(4, 11) == 7, "clearing one position preserves another tier's assignment and legacy metadata");
        window.ExecuteUndo();
        Check(name.Text == "Assigned A", "undo restores cleared slot and editor binding");
        var delayed = (Action<ActionItem>)typeof(CanvasMenuActionEditor).GetField("_apply", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
        var layers = Find<ComboBox>("LayerSelectComboBox");
        layers.SelectedIndex = 1;
        Check(global.ActiveLayerIndex == 1 && Find<Border>("FixedSlotCard").Visibility == Visibility.Collapsed,
            "switching to a legacy layer releases fixed controls");
        delayed(new ActionItem { Name = "Stale callback" });
        layers.SelectedIndex = 0;
        Press("FixedOpenCanvas");
        Select(3, 7);
        Check(name.Text == "Assigned A", "delayed edit from another layer cannot change the saved action");
        var depth = Find<ComboBox>("MenuDepthPicker");
        depth.SelectedIndex = 0;
        Check(Find<Border>("FixedControlsCard").Visibility == Visibility.Collapsed && !canvas.Children.OfType<Button>().Any(b => Id(b).StartsWith("FixedRing4")) &&
            Slot(4, 11).GetProperty("Action").GetProperty("Name").GetString() == "Child", "reducing maximum depth hides complete rings and retains deeper assignments");
        depth.SelectedIndex = 3;
        Press("FixedOpenCanvas");
        Select(4, 11);
        Check(name.Text == "Child" && LegacyParent(4, 11) == 7, "raising depth restores the assigned action and legacy metadata");
        Press("FixedBack");
        Check(Find<Border>("CanvasTierSelector").Visibility == Visibility.Visible &&
            Find<Border>("FocusActionEditorBorder").Visibility == Visibility.Visible, "return restores original tier selectors and forms");
        void SelectLegacyChild(int primary, int child)
        {
            window.SelectPrimarySlot(primary);
            typeof(SettingsWindow).GetMethod("SelectCanvasSubAction", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, [primary, child]);
        }
        SelectLegacyChild(1, 0);
        Check(Find<Border>("DeepCanvasEditorCard").Visibility == Visibility.Collapsed &&
            Find<Border>("FixedControlsCard").Visibility == Visibility.Collapsed && Find<TextBox>("FocusActionNameTextBox").Text == "B child",
            "single legacy secondary click selects its original editor");
        Find<TextBox>("FocusActionNameTextBox").Text = "Renamed B";
        Check(layer.Actions[1].SubActions[0].Name == "Renamed B", "existing form edits a secondary action while fixed rings are enabled");
        Press("FocusAddSubActionBtn");
        Check(layer.Actions[1].SubActions.Count == 2 && layer.Actions[0].SubActions.Count == 5,
            "original secondary add keeps independent per-parent counts");
        window.MoveSubAction(global, 1, 0, 1);
        Check(layer.Actions[1].SubActions[1].Name == "Renamed B", "original secondary ordering remains available");
        Press("FixedOpenCanvas");
        level.SelectedItem = 3;
        int fifth = FixedRingMenu.SecondaryKey(0, 4), other = FixedRingMenu.SecondaryKey(1, 0);
        Check(fifth != other && canvas.Children.OfType<Button>().Any(b => Id(b) == $"FixedRing2Slot{fifth}") &&
            canvas.Children.OfType<Button>().Any(b => Id(b) == $"FixedRing2Slot{other}"),
            "the wheel preview retains distinct fifth secondary and next-root slots");
        Press("FixedBack");
        ConfigManager.CurrentConfig.SubmenuStyle = "Fan";
        Press("FixedOpenCanvas");
        Check(!canvas.Children.OfType<Button>().Any(b => Id(b) == $"FixedRing2Slot{fifth}") &&
            canvas.Children.OfType<Button>().Any(b => Id(b) == $"FixedRing2Slot{other}"),
            "Fan preview uses the original three-child limit and retains other second-tier positions");
        layer.FixedRings!.SetSlot(3, 0, new FixedRingSlot
        {
            Action = new ActionItem { Name = "Follow secondary", Type = "Text", Parameter = "legacy fixture" },
            ParentIndex = FixedRingMenu.SecondaryKey(1, 1)
        });
        Press("FixedBack");
        int? Link()
        {
            var slot = Slot(3, 0);
            return slot.TryGetProperty("ParentIndex", out var owner) && owner.ValueKind == JsonValueKind.Number ? owner.GetInt32() : null;
        }
        ConfigManager.CurrentConfig.EnableGlobalInheritance = true;
        WheelLayer ClientLayer(int sectors, int parentKey, bool pause, bool localOverride = false)
        {
            var json = JsonSerializer.SerializeToNode(layer)!.AsObject();
            json["SectorCount"] = sectors;
            json["FixedRingsPaused"] = pause;
            json["FixedRings"]!["Tiers"]![0]!["Slots"]![0]!["ParentIndex"] = parentKey;
            var clientLayer = JsonSerializer.Deserialize<WheelLayer>(json)!;
            clientLayer.Actions = Enumerable.Range(0, sectors).Select(_ => new ActionItem { Type = "Inherit" }).ToList();
            if (localOverride) clientLayer.Actions[1] = new ActionItem { Type = "Hotkey", Name = "Local sector", Parameter = "F19" };
            return clientLayer;
        }
        int? ClientParent(WheelLayer clientLayer)
        {
            var childSlot = JsonSerializer.SerializeToElement(clientLayer).GetProperty("FixedRings").GetProperty("Tiers")[0].GetProperty("Slots")[0];
            return childSlot.TryGetProperty("ParentIndex", out var key) && key.ValueKind == JsonValueKind.Number ? key.GetInt32() : null;
        }
        var pausedEight = ClientLayer(8, 9, true);
        var otherLayer = ClientLayer(4, 5, false);
        var fallback = ClientLayer(4, 5, false);
        var localOverride = ClientLayer(4, 5, false, localOverride: true);
        var receiving = new WheelProfile { ProcessName = "inherited.editor.test", Layers = { pausedEight, otherLayer, fallback } };
        var privateProfile = new WheelProfile { ProcessName = "local.editor.test", Layers = { localOverride } };
        receiving.EnsureLayers();
        privateProfile.EnsureLayers();
        ConfigManager.CurrentConfig.Profiles.Add(receiving);
        ConfigManager.CurrentConfig.Profiles.Add(privateProfile);
        window.MoveSubAction(global, 1, 1, 0);
        Check(Link() == 4 && layer.Actions[1].SubActions[0].Name == "Renamed B", "reorder carries third-tier link with its secondary parent");
        Check(ClientParent(pausedEight) == 8 && ClientParent(fallback) == 4,
            "global reorder reaches a paused eight-sector inheritor and an inactive layer using layer-zero fallback");
        Check(ClientParent(otherLayer) == 5 && ClientParent(localOverride) == 5,
            "another global layer and a locally overridden sector keep their links");
        void Drag(int source, int child, int destination, int? targetChild, bool copy)
        {
            typeof(SettingsWindow).GetField("_dragSourceSlotIndex", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(window, -100 - source * 100 - child);
            window.ProcessCanvasDrop(global, new CanvasHitItem { SlotIndex = destination, SubIndex = targetChild, IsSubAction = targetChild != null }, copy);
        }
        Drag(1, 1, 1, 0, true);
        Check(Link() == 5 && layer.Actions[1].SubActions[1].Name == "Renamed B", "inserting a copied sibling preserves the original parent identity");
        Drag(1, 1, 2, null, false);
        Check(Link() == 8 && layer.Actions[2].SubActions[0].Name == "Renamed B", "moving a secondary to another primary updates the third-tier parent");
        Check(ClientParent(pausedEight) == 16 && ClientParent(fallback) == 8, "global cross-branch move translates to actual inherited directions");
        window.DeleteSubAction(global, 2, 0);
        Check(Link() == null && Slot(3, 0).GetProperty("Action").GetProperty("Name").GetString() == "Follow secondary",
            "deleting a parent makes its third-tier slot independent and preserves the action");
        Check(ClientParent(pausedEight) == null && ClientParent(localOverride) == 5, "global deletion detaches only true inheritors");
        window.ExecuteUndo();
        Check(ClientParent(pausedEight) == 16 && ClientParent(fallback) == 8, "global undo atomically restores links on other profiles");
        Check(Link() == 8 && layer.Actions[2].SubActions[0].Name == "Renamed B", "undo restores the secondary and its third-tier relationship atomically");
        window.ExecuteUndo();
        Check(Link() == 5 && layer.Actions[1].SubActions[1].Name == "Renamed B", "undo cross-branch move restores the original address");
        window.ExecuteRedo();
        window.ExecuteRedo();
        Check(Link() == null && layer.Actions[2].SubActions.Count == 0, "redo replays cross-branch relocation and detachment");
        typeof(WheelLayer).GetProperty("FixedRings")!.SetValue(layer, null);
        var onlyClient = ClientLayerFrom(privateProfile.Layers[0], 1);
        var appOnly = new WheelProfile { ProcessName = "only.app.uses.rings", Layers = { onlyClient } };
        appOnly.EnsureLayers();
        ConfigManager.CurrentConfig.Profiles.Add(appOnly);
        window.MoveSubAction(global, 0, 1, 0);
        Check(ClientParent(onlyClient) == 0, "inherited links follow edits even when Global uses only the legacy layout");
        WheelLayer ClientLayerFrom(WheelLayer template, int parentKey)
        {
            var json = JsonSerializer.SerializeToNode(template)!;
            json["FixedRings"]!["Tiers"]![0]!["Slots"]![0]!["ParentIndex"] = parentKey;
            var clientLayer = JsonSerializer.Deserialize<WheelLayer>(json)!;
            clientLayer.Actions = Enumerable.Range(0, 4).Select(_ => new ActionItem { Type = "Inherit" }).ToList();
            return clientLayer;
        }
        ExercisePrimaryResize();
        ExerciseFixedCanvasDrag();
        Check(!window.IsVisible && PresentationSource.FromVisual(window) == null, "all controls were exercised off screen");
    }

    private static void ExercisePrimaryResize()
    {
        WheelLayer MakeLayer(int count, bool local, params int[] keys)
        {
            var data = new
            {
                SectorCount = count, MaxMenuDepth = 3, FixedRingsPaused = true,
                Actions = Enumerable.Range(0, count).Select(i => local ? new ActionItem { Type = "Inherit" } :
                    new ActionItem { Type = "Hotkey", Name = $"root {i}", Parameter = "F18", SubActions = [new() { Name = $"child {i}", Parameter = "F19" }] }),
                FixedRings = new { Tiers = new[] { new { Count = 4, Slots = keys.Select(k => new { ParentIndex = k, Action = new ActionItem { Name = "outer" } }) } } }
            };
            return JsonSerializer.Deserialize<WheelLayer>(JsonSerializer.Serialize(data))!;
        }
        int?[] Keys(WheelLayer wheel) => JsonSerializer.SerializeToElement(wheel).GetProperty("FixedRings").GetProperty("Tiers")[0]
            .GetProperty("Slots").EnumerateArray().Select(s => s.TryGetProperty("ParentIndex", out var p) && p.ValueKind == JsonValueKind.Number ? (int?)p.GetInt32() : null).ToArray();
        var root = MakeLayer(8, false, 8, 16, 4, 0, 16);
        var global = new WheelProfile { ProcessName = "Global", Layers = { root } };
        var inherit = MakeLayer(8, true, 8, 16, 4, 0, 16);
        var local = new WheelProfile { ProcessName = "inherit.resize", Layers = { inherit } };
        var mask = MakeLayer(8, true, 4);
        mask.Actions[1] = new() { Type = "Hotkey", Parameter = "F20", SubActions = [new() { Name = "local", Parameter = "F21" }] };
        var own = new WheelProfile { ProcessName = "override.resize", Layers = { mask } };
        global.EnsureLayers();
        local.EnsureLayers();
        own.EnsureLayers();
        ConfigManager.CurrentConfig.Profiles = [global, local, own];
        var settings = new SettingsWindow();
        settings.SelectSubAction(2, 0);
        var branchButton = (Button)settings.FindName("EditMenuBranchButton");
        branchButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, branchButton));
        var originalEditor = (CanvasMenuActionEditor)settings.FindName("DeepCanvasEditor");
        ((TextBox)originalEditor.FindName("NameBox")).Text = "Renamed secondary";
        Check(root.Actions[2].SubActions[0].Name == "Renamed secondary" && Keys(root)[0] == 8 && Keys(inherit)[0] == 8,
            "legacy deep editor renames the same secondary without detaching paused local or global-inherited relationships");
        ((RadioButton)settings.FindName("MappingsSectorCount4Radio")).IsChecked = true;
        Check(root.SectorCount == 4 && root.Actions[1].SubActions[0].Name == "Renamed secondary" && root.Actions[2].SubActions[0].Name == "child 4" &&
            Keys(root).SequenceEqual(new int?[] { 4, 8, null, 0, 8 }), "8→4 keeps south/west third-tier parents, detaches discarded diagonals and migrates hidden paused slots");
        Check(Keys(inherit).SequenceEqual(new int?[] { 8, 16, null, 0, 16 }) && Keys(mask)[0] == 4,
            "global resize preserves inherited physical directions, detaches lost branches and skips local overrides");
        settings.ExecuteUndo();
        Check(root.SectorCount == 8 && Keys(root)[2] == 4 && Keys(inherit)[2] == 4, "undo resize restores displaced actions and other profiles' links");
        settings.ExecuteRedo();
        ((RadioButton)settings.FindName("MappingsSectorCount12Radio")).IsChecked = true;
        Check(root.SectorCount == 12 && Keys(root).SequenceEqual(new int?[] { 12, 24, null, 0, 24 }), "4→12 rebinds retained cardinal directions");
        var competing = MakeLayer(12, false, 4, 8);
        var otherGlobal = new WheelProfile { ProcessName = "Global", Layers = { competing } };
        otherGlobal.EnsureLayers();
        ConfigManager.CurrentConfig.Profiles = [otherGlobal];
        var twelve = new SettingsWindow();
        ((RadioButton)twelve.FindName("MappingsSectorCount8Radio")).IsChecked = true;
        Check(competing.Actions[1].SubActions[0].Name == "child 1" && Keys(competing).SequenceEqual(new int?[] { 4, null }),
            "12→8 follows the actual preferred diagonal and detaches the competing source");
    }
}
