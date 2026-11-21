using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
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
        ConfigManager.CurrentConfig.Profiles = [profile];
        ConfigManager.CurrentConfig.SubmenuStyle = "Wheel";
        ConfigManager.CurrentConfig.LinkSubActionsWhenDragging = true;
        var window = new SettingsWindow();
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
        Drop(3, 0, 4, 1);
        Check(Snapshot() == beforeCancel, "incompatible cross-tier drag leaves every assignment intact");
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
        Check(!window.IsVisible && PresentationSource.FromVisual(window) == null, "drag probe exercised off-screen controls");
    }
}
