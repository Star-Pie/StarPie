using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using WinPieGestures;

internal static partial class Program
{
    private static void ExerciseCanvas(SettingsWindow settings, WheelProfile global)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var canvas = (Canvas)settings.FindName("MappingsWheelPreviewCanvas");
        var card = (Border)settings.FindName("DeepCanvasEditorCard");
        Check(card != null, "inline canvas editor present in compiled settings page");
        var editor = (CanvasMenuActionEditor)settings.FindName("DeepCanvasEditor");
        var name = (TextBox)editor.FindName("NameBox");
        var text = (TextBox)editor.FindName("ParameterBox");
        var type = (ComboBox)editor.FindName("TypeBox");
        void Call(string method, params object[] args) =>
            typeof(SettingsWindow).GetMethod(method, flags)!.Invoke(settings, args);
        Button ButtonWithId(string id) => canvas.Children.OfType<Button>().Single(item => AutomationProperties.GetAutomationId(item) == id);
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
        void ClickSector(string id)
        {
            canvas.Measure(new Size(300, 300));
            canvas.Arrange(new Rect(0, 0, 300, 300));
            var button = ButtonWithId(id);
            var bounds = new Rect(Canvas.GetLeft(button), Canvas.GetTop(button), button.Width, button.Height);
            for (double x = bounds.Left; x < bounds.Right; x += 1.5)
                for (double y = bounds.Top; y < bounds.Bottom; y += 1.5)
                    if (VisualTreeHelper.HitTest(canvas, new Point(x, y))?.VisualHit is Button hit && hit == button)
                    {
                        Click(hit);
                        return;
                    }
            throw new InvalidOperationException("No painted hit for " + id);
        }
        void Child(int i) => ClickSector("DeepCanvasChild" + i);
        void EnterFromOuterRing(int sector)
        {
            settings.SelectPrimarySlot(sector);
            var hits = (List<CanvasHitItem>)typeof(SettingsWindow).GetField("_canvasHitItems", flags)!.GetValue(settings)!;
            var target = hits.Single(h => h.SlotIndex == sector && h.SubIndex == 0);
            var b = target.Bounds;
            CanvasHitItem? hit = null;
            for (double x = b.Left; x <= b.Right && hit == null; x += .7)
                for (double y = b.Top; y <= b.Bottom; y += .7)
                    if (ReferenceEquals(settings.HitTestCanvasPoint(new Point(x, y)), target)) { hit = target; break; }
            Check(hit != null, "existing outer-ring geometry hits intended second-tier slot");
            Call("SelectCanvasSubAction", hit!.SlotIndex, hit.SubIndex!.Value);
            Check(card!.Visibility == Visibility.Collapsed, "one secondary click selects without opening a branch");
            Click((Button)settings.FindName("EditMenuBranchButton"));
        }

        var picker = (ComboBox)settings.FindName("MenuDepthPicker");
        picker.SelectedIndex = 3;
        global.Layers[0].Actions[2] = Chain();
        long revision = ConfigManager.ConfigurationRevision;
        EnterFromOuterRing(2);
        Check(card!.Visibility == Visibility.Visible && name.Text == "Two", "explicit submenu entry opens inline branch with its action");
        Child(0);
        Check(name.Text == "Three", "third-level sector click selects its own editable action");
        Click((Button)settings.FindName("DeepEnter"));
        Child(0);
        Click((Button)settings.FindName("DeepEnter"));
        Child(0);
        Check(name.Text == "Five" && type.SelectedValue?.ToString() == "Plugin" &&
            ConfigManager.ConfigurationRevision == revision, "explicitly entering the selected branches reaches fifth-level plugin without modifying configuration");
        type.SelectedValue = "Text";
        name.Text = "Canvas leaf";
        text.Text = "typed at level 5";
        int[] path = [2, 0, 0, 0, 0];
        var changed = global.GetEffectiveMenuAction(path)!;
        Check(changed.Type == "Text" && changed.Parameter == "typed at level 5" && changed.Name == "Canvas leaf", "live name/type/parameter changes reach only fifth-level selection");
        bool LeafCaptionVisible() => canvas.Children.OfType<Panel>()
            .SelectMany(p => p.Children.OfType<TextBlock>()).Any(t => t.Text == "Canvas leaf");
        var labelsButton = (Button)settings.FindName("MappingsShowTextToggleBtn");
        Check(!ConfigManager.CurrentConfig.MappingsCanvasShowText && !LeafCaptionVisible(),
            "the default deep canvas displays the text action as an icon");
        Click(labelsButton);
        Check(LeafCaptionVisible(), "with canvas labels enabled the edited name appears on the actual canvas");
        Click(labelsButton);
        Check(!LeafCaptionVisible(), "switching off labels again hides the edited caption");
        Check(((Button)settings.FindName("DeepEnter")).Visibility == Visibility.Collapsed, "level five cannot open another branch");
        Click(ButtonWithId("DeepCanvasAdd"));
        name.Text = "Sibling";
        Child(0);
        Check(name.Text == "Canvas leaf" && global.GetEffectiveMenuAction([2, 0, 0, 0, 1])?.Name == "Sibling", "add and click siblings keep edits isolated");
        Click((Button)settings.FindName("DeepLater"));
        Check(name.Text == "Canvas leaf" && global.GetEffectiveMenuAction([2,0,0,0,1])?.Parameter == "typed at level 5", "move fifth-level leaf to its sibling sector with its action");
        Click((Button)settings.FindName("DeepEarlier"));
        Check(global.GetEffectiveMenuAction(path)?.Name == "Canvas leaf", "move leaf back and preserve selection");
        name.Text = "Undo this";
        Click((Button)settings.FindName("DeepUndo"));
        Check(name.Text == "Canvas leaf", "undo rebinds canvas editor to restored leaf");
        Click((Button)settings.FindName("DeepRedo"));
        Check(name.Text == "Undo this", "redo rebinds canvas editor to redone leaf");
        Click(ButtonWithId("DeepCanvasBack"));
        Check(name.Text == "Four", "back shows parent on previous branch");
        Click(ButtonWithId("DeepCanvasAdd"));
        var enter = (Button)settings.FindName("DeepEnter");
        Check(enter.Visibility == Visibility.Visible, "new fourth-level leaf can be expanded for configuration");
        Click(enter);
        Check(!canvas.Children.OfType<Button>().Any(b => AutomationProperties.GetAutomationId(b) == "DeepCanvasChild0"), "empty branch renders without creating a child");
        Click(ButtonWithId("DeepCanvasAdd"));
        name.Text = "New fifth";
        Check(global.GetEffectiveMenuAction([2, 0, 0, 1, 0])?.Name == "New fifth", "canvas can create and configure a new fifth-level child");
        var crumbs = (WrapPanel)settings.FindName("DeepCanvasBreadcrumb");
        Click(crumbs.Children.OfType<Button>().ElementAt(2));
        Check(name.Text == "Two", "clicking a named breadcrumb selects that ancestor itself");
        ConfigManager.CurrentConfig.SubmenuStyle = "Fan";
        Call("RenderMappingsWheelPreview");
        Child(0); Click((Button)settings.FindName("DeepEnter")); Child(0); Click((Button)settings.FindName("DeepEnter")); Child(1);
        Check(name.Text == "Sibling", "actual Fan geometry accepts three nested sector clicks and selects the intended sibling");
        if (Environment.GetEnvironmentVariable("T8_CANVAS_PREVIEW") is { Length: > 0 } imagePath)
        {
            canvas.Measure(new Size(300, 300));
            canvas.Arrange(new Rect(0, 0, 300, 300));
            var picture = new RenderTargetBitmap(600, 600, 192, 192, PixelFormats.Pbgra32);
            picture.Render(canvas);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(picture));
            using var stream = System.IO.File.Create(imagePath);
            encoder.Save(stream);
        }
        ClickSector("DeepCanvasAncestor3");
        Check(name.Text == "Three", "clicking the painted ancestor sector selects its own action");
        ConfigManager.CurrentConfig.SubmenuStyle = "Wheel";
        Click(crumbs.Children.OfType<Button>().ElementAt(2));
        name.Text = "Level two updated";
        picker.SelectedIndex = 0;
        Check(((TextBox)settings.FindName("FocusActionNameTextBox")).Text == "Level two updated", "lowering depth refreshes original inline form with latest parent name");
        Check(card.Visibility == Visibility.Collapsed && global.GetEffectiveMenuAction(path) == null, "reducing depth exits deep page");
        picker.SelectedIndex = 3;
        Check(global.GetEffectiveMenuAction([2, 0, 0, 1, 0])?.Name == "New fifth", "raising depth retains new branches");

        var local = new WheelProfile { ProcessName = "canvas-local", SectorCount = 4,
            Layers = { new WheelLayer { SectorCount = 4, MaxMenuDepth = 5 } } };
        local.EnsureLayers();
        ConfigManager.CurrentConfig.Profiles.Add(local);
        typeof(SettingsWindow).GetField("_selectedProfile", flags)!.SetValue(settings, local);
        global.Actions[2].SubActions[0].Parameter = "F15";
        EnterFromOuterRing(1);
        Child(0); Click((Button)settings.FindName("DeepEnter")); Child(0); Click((Button)settings.FindName("DeepEnter")); Child(0);
        Check(!WheelProfile.IsActionConfigured(local.Actions[1]), "clicking through inherited canvas branch does not make an override");
        name.Text = "Local only";
        Check(global.GetEffectiveMenuAction(path)!.Name == "Undo this" && local.GetEffectiveMenuAction([1,0,0,0,0])!.Name == "Local only",
            "editing inherited canvas leaf creates only the local override");
        ClickSector("DeepCanvasRoot0");
        Check(card.Visibility == Visibility.Collapsed && (int)typeof(SettingsWindow).GetField("_selectedSlotIndex", flags)!.GetValue(settings)! == 0,
            "clicking a different main wheel sector leaves the branch and selects that sector");
        global.Actions[2] = new ActionItem { SubActions = { new ActionItem { SubActions = { new ActionItem { Parameter = "F16" } } } } };
        local.Actions[1] = new ActionItem();
        EnterFromOuterRing(1);
        Child(0);
        T Field<T>(string field) => (T)typeof(SettingsWindow).GetField(field, flags)!.GetValue(settings)!;
        int[] selected = Field<int[]>("_deepBoundPath");
        int version = Field<int>("_deepBindingVersion");
        var expected = Field<ActionItem>("_deepBoundNode");
        long confirmationRevision = ConfigManager.ConfigurationRevision;
        Call("DeleteConfirmedCanvasSelection", local, selected, version - 1, confirmationRevision, expected);
        Check(!WheelProfile.IsActionConfigured(local.Actions[1]), "stale confirmation leaves inherited branch untouched");
        Call("DeleteConfirmedCanvasSelection", local, selected, version, confirmationRevision, expected);
        Check(local.GetEffectiveMenuAction([1,0,0]) == null && global.GetEffectiveMenuAction([2,0,0])?.Parameter == "F16",
            "deleting last inherited leaf leaves local menu empty and preserves global leaf");
        var persisted = System.Text.Json.JsonSerializer.Deserialize<WheelProfile>(System.Text.Json.JsonSerializer.Serialize(local))!;
        Check(persisted.GetEffectiveMenuAction([1,0,0], global) == null, "empty local menu survives persistence");
        settings.ExecuteUndo();
        Check(local.GetEffectiveMenuAction([1,0,0])?.Parameter == "F16", "undo restores last inherited leaf");
        settings.ExecuteRedo();
        Check(local.GetEffectiveMenuAction([1,0,0]) == null, "redo restores explicitly empty local menu");
        settings.SelectSubAction(1, 0);
        Call("FocusRestoreInheritBtn_Click", settings, new RoutedEventArgs());
        Check(!local.Actions[1].OverrideGlobalMenu && local.GetEffectiveMenuAction([1,0,0])?.Parameter == "F16",
            "explicit restore from the two-level editor resumes global inheritance");
        settings.SelectCenterCore();
        Check(card.Visibility == Visibility.Collapsed, "center selection restores original editor");
    }
}
