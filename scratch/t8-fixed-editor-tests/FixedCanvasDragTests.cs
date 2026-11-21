using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinPieGestures;

internal static partial class Program
{
    private static void ExerciseFixedCanvasDrag()
    {
        ActionItem Item(string name) => new() { Name = name, Type = "Text", Parameter = name + " payload" };
        var layer = new WheelLayer { SectorCount = 4, MaxMenuDepth = 5, FixedRings = new() };
        for (int i = 0; i < 4; i++)
        {
            var root = Item("root " + i);
            root.SubActions = [Item("child " + i)];
            layer.Actions.Add(root);
        }
        layer.Actions[0].Type = "Plugin";
        layer.Actions[0].PluginActionRef = new() { PluginId = "test.plugin", ContributionId = "offline.fixture" };
        layer.Actions[0].ExtensionData = new() { ["p"] = "root parameter" };
        layer.Actions[0].Extras = new() { ["future"] = JsonSerializer.SerializeToElement(new[] { 11, 22 }) };
        layer.Actions[0].SubActions[0].SubActions.Add(Item("legacy hidden grandchild"));
        for (int depth = 3; depth <= 5; depth++)
        {
            layer.FixedRings.SetCount(depth, 4);
            layer.FixedRings.SetSlot(depth, 0, new() { Action = Item($"A{depth}"), ParentIndex = 0 });
            layer.FixedRings.SetSlot(depth, 1, new() { Action = Item($"B{depth}"), ParentIndex = depth == 3 ? 4 : 1 });
            layer.FixedRings.Tiers[depth - 3]!.Slots.AddRange([null, null]);
            if (depth > 3)
            {
                layer.FixedRings.SetSlot(depth, 6, new() { Action = Item("hidden"), ParentIndex = 0 });
                layer.FixedRings.SetSlot(depth, 7, new() { Action = Item("vacant parent"), ParentIndex = 3 });
            }
        }
        var secondLayer = JsonSerializer.Deserialize<WheelLayer>(JsonSerializer.Serialize(layer))!;
        var profile = new WheelProfile { ProcessName = "Global", Layers = { layer, secondLayer } };
        profile.EnsureLayers();
        ConfigManager.CurrentConfig = ConfigManager.CreateDefaultConfig();
        ConfigManager.CurrentConfig.AutoCheckUpdate = false;
        Check(ConfigManager.CurrentConfig.IconLayoutMode == "IconOnly" && !ConfigManager.CurrentConfig.MappingsCanvasShowText,
            "the first-run configuration factory selects icons and turns canvas captions off");
        ConfigManager.CurrentConfig.Profiles = [profile];
        ConfigManager.CurrentConfig.SubmenuStyle = "Wheel";
        ConfigManager.CurrentConfig.LinkSubActionsWhenDragging = true;
        var wheel = new RadialWindow(new Point(), profile);
        (bool Icon, bool Text) RenderLabel(ActionItem action)
        {
            var runtimeLabel = new DrawingGroup();
            var slot = new DeepMenuFrame.Slot(new RectangleGeometry(new Rect(0, 0, 40, 30)), new Point(20, 15), 40);
            using (var drawing = runtimeLabel.Open())
                typeof(RadialWindow).GetMethod("DrawMenuLabel", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(wheel, [drawing, slot, action, false]);
            static IEnumerable<Drawing> Leaves(Drawing d) => d is DrawingGroup group
                ? group.Children.SelectMany(Leaves) : [d];
            return (Leaves(runtimeLabel).Any(d => d is GeometryDrawing or ImageDrawing),
                Leaves(runtimeLabel).Any(d => d is GlyphRunDrawing));
        }
        Check(RenderLabel(Item("runtime iconless text")) == (true, false) &&
            !wheel.IsVisible && PresentationSource.FromVisual(wheel) == null,
            "the actual runtime outer-label renderer draws an uncustomized action as a vector without text in factory icon-only mode");
        var labelAction = Item("runtime caption");
        labelAction.IconKey = "Copy";
        foreach (bool legacyShowText in new[] { false, true })
        {
            ConfigManager.CurrentConfig.ShowText = legacyShowText;
            foreach (string globalMode in new[] { "IconAndText", "IconOnly", "TextOnly", "Both" })
            foreach (string? overrideMode in new[] { null, "Inherit", "IconAndText", "IconOnly", "TextOnly" })
            {
                ConfigManager.CurrentConfig.IconLayoutMode = globalMode;
                labelAction.LayoutMode = overrideMode;
                string effective = overrideMode is null or "Inherit" ? globalMode : overrideMode;
                Check(RenderLabel(labelAction) == (effective != "TextOnly", effective != "IconOnly"),
                    $"runtime outer icon/text drawings honor global={globalMode}, slot={overrideMode ?? "missing"}, legacy ShowText={legacyShowText}");
            }
        }
        ConfigManager.CurrentConfig.ShowText = false;
        ConfigManager.CurrentConfig.IconLayoutMode = "IconOnly";
        var window = new SettingsWindow();
        var layoutPicker = (ComboBox)window.FindName("IconLayoutModeComboBox");
        void SelectGlobalLayout(string mode) => layoutPicker.SelectedItem = layoutPicker.Items.OfType<ComboBoxItem>()
            .Single(item => Equals(item.Tag, mode));
        SelectGlobalLayout("IconAndText");
        Check(!ConfigManager.CurrentConfig.ShowText && ConfigManager.CurrentConfig.IconLayoutMode == "IconAndText" &&
            Enumerable.Range(3, 3).All(depth =>
            {
                var configured = layer.FixedRings!.GetSlot(depth, 0)!.Action.Clone();
                configured.IconKey = "Copy";
                return RenderLabel(configured) == (true, true);
            }), "choosing Icon + Text in the settings control produces both drawings for configured levels three, four and five with factory ShowText=false");
        SelectGlobalLayout("IconOnly");
        var canvas = (Canvas)window.FindName("MappingsWheelPreviewCanvas");
        void Open()
        {
            var open = (Button)window.FindName("FixedOpenCanvas");
            open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, open));
        }
        Open();
        const BindingFlags methods = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        (Point Point, CanvasHitItem Hit) Locate(int depth, int index)
        {
            canvas.Measure(new Size(300, 300));
            canvas.Arrange(new Rect(0, 0, 300, 300));
            string id = $"FixedRing{depth}Slot{index}";
            var button = canvas.Children.OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == id);
            double left = Canvas.GetLeft(button), top = Canvas.GetTop(button);
            int paintedPoints = 0, candidateHits = 0;
            var fixedLevel = typeof(CanvasHitItem).GetProperty("FixedLevel");
            for (double x = left + .5; x < left + button.Width; x++)
                for (double y = top + .5; y < top + button.Height; y++)
                {
                    var point = new Point(x, y);
                    if (VisualTreeHelper.HitTest(canvas, point)?.VisualHit != button) continue;
                    paintedPoints++;
                    var hit = window.HitTestCanvasPoint(point);
                    if (hit == null) continue;
                    candidateHits++;
                    var marker = fixedLevel?.GetValue(hit);
                    if (!Equals(marker, depth)) continue;
                    int hitIndex = hit.SubIndex is int child ? FixedRingMenu.SecondaryKey(hit.SlotIndex, child) : hit.SlotIndex;
                    if (hitIndex != index) continue;
                    return (point, hit);
                }
            throw new InvalidOperationException($"{id} paintedPoints={paintedPoints}, nonNullHitTestCanvasPoint={candidateHits}, " +
                $"matchingLevel=0, FixedLevelPropertyPresent={fixedLevel != null}");
        }
        void Start(int depth, int index)
        {
            var source = Locate(depth, index);
            var begin = typeof(SettingsWindow).GetMethod("BeginCanvasSlotDrag", methods)
                ?? throw new MissingMethodException("BeginCanvasSlotDrag");
            begin.Invoke(window, [source.Point, source.Hit]);
        }
        void Drop(int depth, int source, int targetDepth, int dest, bool copy = false)
        {
            Start(depth, source);
            var target = Locate(targetDepth, dest);
            window.UpdateCanvasDragHover(target.Point, copy);
            window.ProcessCanvasDrop(profile, target.Hit, copy);
            window.CancelCanvasDrag();
            Open();
        }
        FixedRingSlot? Slot(int depth, int index) => layer.FixedRings!.GetSlot(depth, index);
        string Snapshot() => JsonSerializer.Serialize(layer);
        Button VisualSlot(int depth, int index) => canvas.Children.OfType<Button>().Single(b =>
            AutomationProperties.GetAutomationId(b) == $"FixedRing{depth}Slot{index}");
        double RelativeArea(Button button) => button.Clip.GetArea() / (button.Width * button.Height);
        StackPanel Label(int depth, int index)
        {
            var (_, hit) = Locate(depth, index);
            return canvas.Children.OfType<StackPanel>().Single(label => hit.Geometry.FillContains(
                new Point(Canvas.GetLeft(label) + label.ActualWidth / 2, Canvas.GetTop(label) + label.ActualHeight / 2)));
        }
        bool HasIcon(int depth, int index) => Label(depth, index).Children.OfType<System.Windows.Shapes.Path>()
            .Any(path => path.Data != null && !path.Data.Bounds.IsEmpty);
        string? Caption(int depth, int index) => Label(depth, index).Children.OfType<TextBlock>().SingleOrDefault()?.Text;
        void ToggleCaptions() => ((Button)window.FindName("MappingsShowTextToggleBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        int PixelContribution(int depth, int index)
        {
            var (_, hit) = Locate(depth, index);
            var button = VisualSlot(depth, index);
            var elements = canvas.Children.OfType<StackPanel>().Where(label =>
                hit.Geometry.FillContains(new Point(Canvas.GetLeft(label) + label.ActualWidth / 2,
                    Canvas.GetTop(label) + label.ActualHeight / 2))).Cast<UIElement>().Append(button)
                .Select(element => (Element: element, Saved: element.Opacity)).ToArray();
            byte[] Pixels()
            {
                var bitmap = new RenderTargetBitmap(300, 300, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(canvas);
                var bytes = new byte[300 * 300 * 4];
                bitmap.CopyPixels(bytes, 300 * 4, 0);
                return bytes;
            }
            var painted = Pixels();
            foreach (var (element, _) in elements) element.Opacity = 0;
            try { return Pixels().Where((value, i) => value != painted[i]).Count(); }
            finally { foreach (var (element, saved) in elements) element.Opacity = saved; }
        }
        foreach (var style in new[] { "Wheel", "Fan" })
        {
            SetStyle(style);
            Check(Enumerable.Range(1, 5).All(d => HasIcon(d, 0) && Caption(d, 0) == null) && Caption(5, 2) == "03",
                $"{style} initial Tab 2 displays built-in vectors for actions without artwork at every level and a number for the empty slot");
            ToggleCaptions();
            Check(ConfigManager.CurrentConfig.MappingsCanvasShowText && HasIcon(5, 0) && Caption(5, 0) == "A5",
                $"{style} canvas caption button adds the action name beneath its built-in icon");
            ToggleCaptions();
            Check(!ConfigManager.CurrentConfig.MappingsCanvasShowText && HasIcon(5, 0) && Caption(5, 0) == null,
                $"{style} turning captions off restores icon-only labels");
            var old = JsonSerializer.Deserialize<AppConfig>("""{"IconLayoutMode":"TextOnly","MappingsCanvasShowText":true}""")!;
            ConfigManager.CurrentConfig.IconLayoutMode = old.IconLayoutMode;
            ConfigManager.CurrentConfig.MappingsCanvasShowText = old.MappingsCanvasShowText;
            Slot(5, 1)!.Action.LayoutMode = "IconOnly";
            SetStyle(style);
            Check(!HasIcon(5, 0) && Caption(5, 0) == "A5" && HasIcon(5, 1) && Caption(5, 1) == "B5",
                $"{style} explicit legacy TextOnly, canvas captions and the per-slot icon override survive loading");
            ConfigManager.CurrentConfig.IconLayoutMode = "IconOnly";
            ConfigManager.CurrentConfig.MappingsCanvasShowText = false;
            Slot(5, 1)!.Action.LayoutMode = "Inherit";
            SetStyle(style);
            var parent = Slot(3, 0)!;
            var parentAction = parent.Action;
            parent.Action = new ActionItem { Name = "", Type = "Hotkey" };
            SetStyle(style);
            int vacancyInk = PixelContribution(5, 2), parentInk = PixelContribution(3, 0);
            Check(layer.FixedRings.AlwaysVisible && vacancyInk > 0 && parentInk > 0,
                $"{style} persistent editor paints empty slots and branch parents (vacancy={vacancyInk}, parent={parentInk} changed color bytes)");
            VisualSlot(5, 2).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(((ComboBox)window.FindName("FixedLevel")).SelectedItem is 5 &&
                !((Button)window.FindName("FixedClear")).IsEnabled && Slot(5, 2) == null,
                $"{style} clicking a vacant fixed slot selects it for editing without writing an action");
            string before = Snapshot();
            VisualSlot(2, 0).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(VisualSlot(5, 0) != null &&
                ((TextBox)window.FindName("FocusActionNameTextBox")).Text == layer.Actions[0].SubActions[0].Name &&
                ((Border)window.FindName("FocusActionEditorBorder")).Visibility == Visibility.Visible,
                $"{style} selecting a secondary keeps the deep canvas and binds the inspector to that secondary");
            Drop(2, 0, 5, 2, copy: true);
            Check(Slot(5, 2)?.Action.Name == layer.Actions[0].SubActions[0].Name && PixelContribution(5, 2) > 0,
                $"{style} actual geometry accepts a drop from the inner inspector into a visible vacancy and paints its action");
            window.ExecuteUndo();
            Check(Snapshot() == before && PixelContribution(5, 2) > 0,
                $"{style} undo returns the configured slot to a visible and addressable vacancy");
            parent.Action = parentAction;
        }
        SetStyle("Wheel");
        string crossBefore = Snapshot();
        Drop(3, 0, 4, 1);
        Check(Slot(3, 0)!.Action.Name == "B4" && Slot(4, 1)!.Action.Name == "A3" &&
            Slot(3, 0)!.ParentIndex == 0 && Slot(4, 1)!.ParentIndex == 1 && Slot(5, 1)!.ParentIndex == 1,
            "cross-level drop swaps action payloads and retains both slots' structural addresses");
        window.ExecuteUndo();
        Check(Snapshot() == crossBefore, "undo restores complete layer after cross-level drop");
        var circularSlot = VisualSlot(3, 0);
        SetStyle("Fan");
        foreach (int depth in Enumerable.Range(3, 3))
        {
            var hex = VisualSlot(depth, 0);
            Check(RelativeArea(hex) is > .70 and < .80 && hex.Width / hex.Height is > .8 and < 1.2 &&
                Math.Abs(hex.Width / hex.Height - circularSlot.Width / circularSlot.Height) > .15,
                $"compiled Fan editor uses a compact hexagonal clip on level {depth}, distinct from the original Wheel sector");
        }
        SetStyle("Wheel");
        var missing = new List<string>();
        foreach (int level in Enumerable.Range(1, 5))
        {
            try { Locate(level, 0); }
            catch (InvalidOperationException ex) { missing.Add(ex.Message); }
        }
        Check(missing.Count == 0, "painted sectors across levels 1–5 participate in candidate drag hit testing: " + string.Join(", ", missing));
        Drop(1, 0, 1, 1);
        Check(layer.Actions[1].Name == "root 0" && layer.Actions[0].Name == "root 1" &&
            Slot(3, 0)!.ParentIndex == 4 && Slot(3, 1)!.ParentIndex == 0,
            "primary drag in fixed preview swaps branches and remaps both secondary owners");
        window.ExecuteUndo();
        Open();
        Drop(2, 0, 2, 4);
        Check(layer.Actions[0].SubActions[0].Name == "child 1" && layer.Actions[1].SubActions[0].Name == "child 0" &&
            Slot(3, 0)!.ParentIndex == 4 && Slot(3, 1)!.ParentIndex == 0,
            "secondary drag in fixed preview keeps the legacy swap and third-tier parent ownership");
        window.ExecuteUndo();
        Open();
        void SetStyle(string style)
        {
            var back = (Button)window.FindName("FixedBack");
            back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, back));
            ConfigManager.CurrentConfig.SubmenuStyle = style;
            Open();
        }
        ActionItem? ActionAt(int depth, int index = 0) => depth switch
        {
            1 => layer.Actions[index],
            2 => layer.Actions[index / 4].SubActions[index % 4],
            _ => Slot(depth, index)?.Action
        };
        string? Payload(int depth) => ActionAt(depth) is { } node
            ? JsonSerializer.Serialize(node.CloneWithoutSubActions()) : null;
        foreach (var style in new[] { "Wheel", "Fan" })
        {
            SetStyle(style);
            foreach (var from in Enumerable.Range(1, 5))
            foreach (var to in Enumerable.Range(1, 5).Where(d => d != from))
            {
                string before = Snapshot();
                string? fromPayload = Payload(from), toPayload = Payload(to);
                Drop(from, 0, to, 0);
                Check(Payload(from) == toPayload && Payload(to) == fromPayload &&
                    layer.Actions[0].SubActions[0].SubActions[0].Name == "legacy hidden grandchild" &&
                    Enumerable.Range(3, 3).All(d => Slot(d, 0)?.ParentIndex == 0),
                    $"{style} {from} -> {to} exchanges complete payloads while the legacy tree and fixed parent chain stay put");
                string swapped = Snapshot();
                window.ExecuteUndo();
                Check(Snapshot() == before, $"{style} {from} -> {to} undo restores exact serialized layer");
                window.ExecuteRedo();
                Check(Snapshot() == swapped, $"{style} {from} -> {to} redo restores exact serialized result");
                window.ExecuteUndo();
            }
            string initial = Snapshot();
            Drop(2, 0, 5, 3);
            Check(Slot(5, 3)?.Action.Name == "child 0" && layer.Actions[0].SubActions[0].Parameter == "" &&
                WheelProfile.IsActionConfigured(layer.Actions[0].SubActions[0]) && Slot(3, 0)!.ParentIndex == 0,
                $"{style} move to an empty outer slot leaves a traversable branch for the original secondary's children");
            window.ExecuteUndo();
            Check(Snapshot() == initial, $"{style} undo vacancy move restores contents and storage");
            Drop(1, 0, 4, 1, copy: true);
            Check(layer.Actions[0].ExtensionData!["p"] == "root parameter" &&
                Slot(4, 1)?.Action.ExtensionData?["p"] == "root parameter" &&
                !ReferenceEquals(layer.Actions[0].ExtensionData, Slot(4, 1)?.Action.ExtensionData) &&
                Slot(4, 1)?.ParentIndex == 1 && Slot(4, 1)?.Action.SubActions.Count == 0,
                $"{style} cross-level Ctrl-copy isolates plugin parameters and retains target structure");
            window.ExecuteUndo();
            Check(Snapshot() == initial, $"{style} undo cross-level copy restores displaced action");
            var owner = layer.Actions[2];
            var previousChildren = owner.SubActions;
            owner.SubActions = [];
            SetStyle(style);
            string vacantBefore = Snapshot();
            Start(4, 0);
            var hits = (List<CanvasHitItem>)typeof(SettingsWindow).GetField("_canvasHitItems", methods)!.GetValue(window)!;
            var vacant = hits.Single(h => h.FixedLevel == 2 && h.SlotIndex == 2 && h.Action == null);
            var bounds = vacant.Geometry.Bounds;
            var points = from x in Enumerable.Range((int)Math.Floor(bounds.Left), (int)Math.Ceiling(bounds.Width) + 1)
                         from y in Enumerable.Range((int)Math.Floor(bounds.Top), (int)Math.Ceiling(bounds.Height) + 1)
                         select new Point(x + .5, y + .5);
            var dropPoint = points.First(p => ReferenceEquals(window.HitTestCanvasPoint(p), vacant));
            window.UpdateCanvasDragHover(dropPoint, false);
            window.ProcessCanvasDrop(profile, vacant, false);
            window.CancelCanvasDrag();
            Check(layer.Actions[2].SubActions.Single().Name == "A4" && Slot(4, 0)?.ParentIndex == 0 &&
                Slot(5, 0)?.ParentIndex == 0, $"{style} move into first vacant secondary keeps the fixed slot's structural links");
            window.ExecuteUndo();
            Check(Snapshot() == vacantBefore, $"{style} undo removes materialized secondary");
            layer.Actions[2].SubActions = previousChildren;
        }
        SetStyle("Fan");
        Drop(2, 0, 2, 4);
        Check(layer.Actions[0].SubActions[0].Name == "child 1" && layer.Actions[1].SubActions[0].Name == "child 0" &&
            Slot(3, 0)!.ParentIndex == 4 && Slot(3, 1)!.ParentIndex == 0,
            "Fan secondary geometry reaches the existing cross-sector swap and carries third-tier owners");
        window.ExecuteUndo();
        for (int depth = 3; depth <= 5; depth++)
        {
            string original = Snapshot();
            Drop(depth, 0, depth, 3);
            Check(Slot(depth, 0) == null && Slot(depth, 3)!.Action.Name == $"A{depth}",
                $"Fan level {depth} drag starts on painted honeycomb cell and moves to painted empty cell");
            window.ExecuteUndo();
            Check(Snapshot() == original, $"undo restores Fan level {depth} after actual canvas drag");
        }
        SetStyle("Wheel");
        for (int d = 3; d <= 5; d++)
        {
            string original = Snapshot();
            int? ownerA = Slot(d, 0)!.ParentIndex, ownerB = Slot(d, 1)!.ParentIndex;
            Drop(d, 0, d, 3);
            Check(Slot(d, 0) == null && Slot(d, 3)!.Action.Parameter == $"A{d} payload" && Slot(d, 3)!.ParentIndex == ownerA &&
                (d == 5 || (Slot(d + 1, 0)!.ParentIndex == 3 && Slot(d + 1, 6)!.ParentIndex == 3 && Slot(d + 1, 7)!.ParentIndex == null)),
                $"level {d} move into vacancy carries the action, parent and visible/hidden children");
            string moved = Snapshot();
            window.ExecuteUndo();
            Check(Snapshot() == original, $"level {d} undo restores complete layout after move");
            window.ExecuteRedo();
            Check(Snapshot() == moved, $"level {d} redo restores move");
            window.ExecuteUndo();
            Drop(d, 0, d, 1);
            Check(Slot(d, 0)!.Action.Name == $"B{d}" && Slot(d, 1)!.Action.Name == $"A{d}" &&
                Slot(d, 0)!.ParentIndex == ownerB && Slot(d, 1)!.ParentIndex == ownerA &&
                (d == 5 || (Slot(d + 1, 1)!.ParentIndex == 0 && Slot(d + 1, 0)!.ParentIndex == 1 && Slot(d + 1, 6)!.ParentIndex == 1)),
                $"level {d} swap preserves both owners and moves both sets of children");
            window.ExecuteUndo();
            Check(Snapshot() == original, $"level {d} undo restores swapped actions and children together");
            Drop(d, 0, d, 1, copy: true);
            Check(Slot(d, 1)!.Action.Parameter == $"A{d} payload" && Slot(d, 0)!.Action.Parameter == $"A{d} payload" &&
                !ReferenceEquals(Slot(d, 1)!.Action, Slot(d, 0)!.Action) && Slot(d, 1)!.ParentIndex == ownerA &&
                (d == 5 || (Slot(d + 1, 1)!.ParentIndex == null && Slot(d + 1, 0)!.ParentIndex == 0 && Slot(d + 1, 6)!.ParentIndex == 0)),
                $"level {d} Ctrl-copy clones action and parent, preserves source descendants and detaches overwritten descendants");
            window.ExecuteUndo();
            Check(Snapshot() == original, $"level {d} undo restores overwritten branch after copy");
        }
        string beforeCancel = Snapshot();
        Start(3, 0);
        var outside = new Point(-50, -50);
        window.UpdateCanvasDragHover(outside, false);
        Check(window.HitTestCanvasPoint(outside) == null, "outside canvas has no drop target");
        window.CancelCanvasDrag();
        var oldTarget = Locate(3, 1);
        window.ProcessCanvasDrop(profile, oldTarget.Hit, false);
        Check(Snapshot() == beforeCancel, "cancel discards the pending source and cannot commit a later drop");
        ((ComboBox)window.FindName("FixedDisplay")).SelectedIndex = 1;
        var picker = (ComboBox)window.FindName("FixedLevel");
        picker.SelectedItem = 3;
        Check(!layer.FixedRings!.AlwaysVisible && !canvas.Children.OfType<Button>().Any(b =>
            AutomationProperties.GetAutomationId(b).StartsWith("FixedRing5Slot")), "progressive preview omits the fifth ring when editing level three");
        picker.SelectedItem = 5;
        string progressive = Snapshot();
        Drop(5, 0, 5, 3);
        Check(Slot(5, 0) == null && Slot(5, 3)!.Action.Name == "A5", "progressive fifth-level preview reaches a vacant painted drop target");
        window.ExecuteUndo();
        Check(Snapshot() == progressive, "undo restores progressive fifth-level move");
        var layers = (ComboBox)window.FindName("LayerSelectComboBox");
        string otherBefore = JsonSerializer.Serialize(secondLayer);
        Start(3, 0);
        layers.SelectedIndex = 1;
        Open();
        Check(profile.ActiveLayerIndex == 1, "layer switch selects the second fixed layout during a pending drag");
        var newTarget = Locate(3, 1);
        window.ProcessCanvasDrop(profile, newTarget.Hit, false);
        Check(Snapshot() == progressive && JsonSerializer.Serialize(secondLayer) == otherBefore,
            "release over another fixed layer discards the stale drag source and preserves both layouts");
        layers.SelectedIndex = 0;
        var globalLayer = JsonSerializer.Deserialize<WheelLayer>(Snapshot())!;
        var global = new WheelProfile { ProcessName = "Global", Layers = { globalLayer } };
        global.EnsureLayers();
        profile.ProcessName = "cross-level-inheritance.test";
        profile.Actions[0] = new() { Type = "Inherit", Name = "" };
        ConfigManager.CurrentConfig.EnableGlobalInheritance = true;
        ConfigManager.CurrentConfig.Profiles = [global, profile];
        SetStyle("Wheel");
        var levelPicker = (ComboBox)window.FindName("FixedLevel");
        levelPicker.SelectedItem = 5;
        string inheritedBefore = Snapshot(), globalBefore = JsonSerializer.Serialize(globalLayer);
        Drop(2, 0, 5, 0);
        Check(layer.Actions[0].Name == "root 0" && layer.Actions[0].SubActions[0].Name == "A5" &&
            Slot(5, 0)!.Action.Name == "child 0" && layer.Actions[0].SubActions[0].SubActions[0].Name == "legacy hidden grandchild" &&
            JsonSerializer.Serialize(globalLayer) == globalBefore, "cross-level swap localizes the displayed inherited branch only");
        window.ExecuteUndo();
        Check(Snapshot() == inheritedBefore && profile.GetEffectiveAction(0)?.IsInherited == true &&
            JsonSerializer.Serialize(globalLayer) == globalBefore, "undo restores inheritance without writing the global layer");
        Check(!window.IsVisible && PresentationSource.FromVisual(window) == null, "drag probe exercised off-screen controls");
    }
}
