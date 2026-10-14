using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using WinPieGestures;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using Panel = System.Windows.Controls.Panel;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace ActionEditingTests;

internal static class FocusEditingUiTests
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static void SelectionRefresh()
    {
        var profile = Profile("Global", "A", "A-child");
        profile.Actions[1] = Parent("B", "B-first", "B-second");
        profile.Actions[2] = Parent("Empty");
        var config = new AppConfig { Profiles = new() { profile } };
        typeof(ConfigManager).GetProperty("CurrentConfig")!.SetValue(null, config);
        var fixture = Window(profile);
        fixture.Window.RefreshFocusSubActionsListAndChips();
        fixture.Window.SelectPrimarySlot(1);
        Equal(2, fixture.List.Children.Count, "switching primary sectors refreshes row count");
        Require(Texts(fixture.List).Contains("B-first") && Texts(fixture.List).Contains("B-second"), "new rows use the newly selected parent");
        Require(fixture.Breadcrumb.Text.Contains("B"), "breadcrumb switches with the primary sector");
        Require(fixture.Count.Text.Contains("2"), "count switches with rows");

        fixture.Window.SelectSubAction(1, 0);
        Equal("B-first", fixture.Name.Text, "sub-action editor displays the selected child");
        Require(fixture.Breadcrumb.Text.Contains("B-first"), "breadcrumb identifies the selected child");
        fixture.Window.SelectPrimarySlot(2);
        Equal(0, fixture.List.Children.Count, "empty primary selection clears old rows");
        Require(fixture.Count.Text.Contains("0"), "empty count does not retain previous count");
        fixture.Window.SelectCenterCore();
        Equal(0, fixture.List.Children.Count, "center selection clears rows");
        Require(!fixture.Breadcrumb.Text.Contains("B-first"), "center breadcrumb does not retain child selection");

        var nextProfile = Profile("Global", "Next", "Next-child");
        config.Profiles = new() { nextProfile };
        Set(fixture.Window, "_selectedProfile", nextProfile);
        fixture.Window.SelectPrimarySlot(0);
        Require(Texts(fixture.List).Contains("Next-child"), "profile change refreshes rows");
        nextProfile.Layers.Add(new WheelLayer { Name = "Layer 2", SectorCount = 4, Actions = new() { Parent("Layer parent", "Layer child"), new(), new(), new() } });
        nextProfile.ActiveLayerIndex = 1;
        nextProfile.SyncRootPropertiesFromActiveLayer();
        fixture.Window.SelectPrimarySlot(0);
        Require(Texts(fixture.List).Contains("Layer child"), "layer change refreshes rows");

        var global = Profile("Global", "Global parent", "Inherited first", "Inherited second");
        var local = Profile("editor.exe", "");
        local.Actions[0] = new ActionItem { Type = "Inherit", Name = "" };
        config.Profiles = new() { global, local };
        config.EnableGlobalInheritance = true;
        Set(fixture.Window, "_selectedProfile", local);
        string before = JsonSerializer.Serialize(local.Actions[0]);
        fixture.Window.SelectPrimarySlot(0);
        Equal(2, fixture.List.Children.Count, "inherited primary selection displays effective children");
        fixture.Window.SelectSubAction(0, 1);
        Equal("Inherited second", fixture.Name.Text, "inherited child editor uses child rather than parent");
        Equal(before, JsonSerializer.Serialize(local.Actions[0]), "selection never materializes inherited data");
    }

    internal static void InsertionBoundaries()
    {
        var profile = Profile("Global", "Parent", "First", "Second", "Third");
        typeof(ConfigManager).GetProperty("CurrentConfig")!.SetValue(null, new AppConfig { Profiles = new() { profile } });
        var fixture = Window(profile);
        fixture.Window.RefreshFocusSubActionsListAndChips();
        foreach (Border row in fixture.List.Children) row.Height = 30;
        fixture.List.Measure(new Size(300, 500));
        fixture.List.Arrange(new Rect(0, 0, 300, fixture.List.DesiredSize.Height));
        Invoke(fixture.Window, "UpdateListInsertionIndicator", new Point(20, fixture.List.ActualHeight - 1));
        Equal(3, Get<int>(fixture.Window, "_listDropTargetIndex"), "last gap is after the final row");
        Invoke(fixture.Window, "UpdateListInsertionIndicator", new Point(-1, 10));
        Equal(-1, Get<int>(fixture.Window, "_listDropTargetIndex"), "outside drop is invalid");
        Invoke(fixture.Window, "UpdateListInsertionIndicator", new Point(20, -1));
        Equal(-1, Get<int>(fixture.Window, "_listDropTargetIndex"), "above-list drop is invalid");
    }

    internal static void ListDragLifecycle()
    {
        var profile = Profile("Global", "Parent", "First", "Second", "Third");
        typeof(ConfigManager).GetProperty("CurrentConfig")!.SetValue(null, new AppConfig { Profiles = new() { profile } });
        var fixture = Window(profile);
        Begin(fixture.Window, profile, 0, 0, 2);
        Invoke(fixture.Window, "CompleteListDrag", true);
        Equal("Second,First,Third", Order(profile), "drop before third row accounts for removing the source");
        Equal(1, History(fixture.Window).UndoCount, "one committed drag produces one transaction");
        fixture.Window.ExecuteUndo();
        Equal("First,Second,Third", Order(profile), "drag undo restores order");
        fixture.Window.ExecuteRedo();
        Equal("Second,First,Third", Order(profile), "drag redo restores order");

        Begin(fixture.Window, profile, 0, 0, 3);
        fixture.Window.CancelListDrag();
        Invoke(fixture.Window, "CompleteListDrag", true);
        Equal("Second,First,Third", Order(profile), "cancel or lost capture prevents commit");
        Equal(-1, Get<int>(fixture.Window, "_listDragSourceIndex"), "cancel clears source");
        Equal(-1, Get<int>(fixture.Window, "_listDropTargetIndex"), "cancel clears target");

        Begin(fixture.Window, profile, 0, 0, 3);
        Set(fixture.Window, "_selectedSlotIndex", 1);
        Invoke(fixture.Window, "CompleteListDrag", true);
        Equal("Second,First,Third", Order(profile), "selection change cannot redirect a pending drag");
        Set(fixture.Window, "_selectedSlotIndex", 0);
        Begin(fixture.Window, profile, 0, 0, 1);
        Invoke(fixture.Window, "CompleteListDrag", true);
        Equal(1, History(fixture.Window).UndoCount, "same final position produces no transaction");
        Begin(fixture.Window, profile, 0, 0, 3);
        Invoke(fixture.Window, "CompleteListDrag", true);
        Equal("First,Third,Second", Order(profile), "last gap moves to the end");
        Require(!Get<bool>(fixture.Window, "_isDraggingListItem"), "commit clears dragging state before releasing capture");

        Begin(fixture.Window, profile, 0, 0, 3);
        var escape = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, new HeadlessPresentationSource(), 0, System.Windows.Input.Key.Escape);
        escape.RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent;
        Invoke(fixture.Window, "Window_PreviewKeyDown", fixture.Window, escape);
        Require(escape.Handled, "Escape is handled by an active list drag");
        Require(!Get<bool>(fixture.Window, "_isDraggingListItem"), "Escape cancels list drag");
        Equal("First,Third,Second", Order(profile), "Escape does not reorder actions");

        Begin(fixture.Window, profile, 0, 0, 3);
        var otherProfile = Profile("Global", "Other", "Other child");
        Set(fixture.Window, "_selectedProfile", otherProfile);
        Invoke(fixture.Window, "CompleteListDrag", true);
        Equal("First,Third,Second", Order(profile), "profile change prevents stale drag commit");
        Set(fixture.Window, "_selectedProfile", profile);
        Begin(fixture.Window, profile, 0, 0, 3);
        profile.Layers.Add(new WheelLayer { Name = "Second", SectorCount = 4, Actions = new() { Parent("Layer", "Layer child"), new(), new(), new() } });
        profile.ActiveLayerIndex = 1;
        profile.SyncRootPropertiesFromActiveLayer();
        Invoke(fixture.Window, "CompleteListDrag", true);
        Equal("Layer child", Order(profile), "layer change prevents stale drag commit");
        Equal("First,Third,Second", string.Join(",", profile.Layers[0].Actions[0].SubActions.Select(a => a.Name)), "previous layer remains unchanged");
    }

    internal static void HistoryContextIsolation()
    {
        var profile = Profile("Global", "Original", "Original-child");
        var config = new AppConfig { Profiles = new() { profile } };
        typeof(ConfigManager).GetProperty("CurrentConfig")!.SetValue(null, config);
        var fixture = Window(profile);
        Set(fixture.Window, "_dragSourceSlotIndex", -100);
        fixture.Window.ProcessCanvasDrop(profile, new CanvasHitItem { SlotIndex = 1 }, false);
        Equal(1, History(fixture.Window).UndoCount, "cross-sector move is recorded");
        var recreated = Profile("Global", "Recreated", "New-child");
        config.Profiles = new() { recreated };
        Set(fixture.Window, "_selectedProfile", recreated);
        string before = JsonSerializer.Serialize(recreated.Actions);
        fixture.Window.ExecuteUndo();
        Equal(before, JsonSerializer.Serialize(recreated.Actions), "undo cannot overwrite a recreated same-name profile");
        Equal(1, History(fixture.Window).UndoCount, "rejected undo leaves history unchanged");

        config.Profiles = new() { profile };
        Set(fixture.Window, "_selectedProfile", profile);
        fixture.Window.ExecuteUndo();
        Equal("Original-child", Order(profile), "original profile still permits undo");
        Set(fixture.Window, "_selectedProfile", recreated);
        fixture.Window.ExecuteRedo();
        Equal(before, JsonSerializer.Serialize(recreated.Actions), "redo cannot overwrite a recreated same-name profile");
        Equal(1, History(fixture.Window).RedoCount, "rejected redo leaves history unchanged");

        Set(fixture.Window, "_selectedProfile", profile);
        fixture.Window.ExecuteRedo();
        profile.Layers[0] = new WheelLayer { Name = "Replacement", SectorCount = 4, Actions = new() { Parent("Replacement", "Replacement-child"), new(), new(), new() } };
        profile.SyncRootPropertiesFromActiveLayer();
        string layerBefore = JsonSerializer.Serialize(profile.Actions);
        fixture.Window.ExecuteUndo();
        Equal(layerBefore, JsonSerializer.Serialize(profile.Actions), "undo cannot overwrite a replacement at the same layer index");
    }

    private static void Begin(SettingsWindow window, WheelProfile profile, int parent, int source, int gap)
    {
        Invoke(window, "BeginListDrag", profile, parent, source, new Point(5, 5));
        Set(window, "_isDraggingListItem", true);
        Set(window, "_listDropTargetIndex", gap);
    }

    private static string Order(WheelProfile profile) => string.Join(",", profile.Actions[0].SubActions.Select(a => a.Name));
    private static ActionTransactionHistory History(SettingsWindow window) => Get<ActionTransactionHistory>(window, "_transactionHistory");
    private static ActionItem Parent(string name, params string[] children) => new() { Name = name, Parameter = "F1", SubActions = children.Select(n => new ActionItem { Name = n, Parameter = "F2" }).ToList() };
    private static WheelProfile Profile(string process, string name, params string[] children)
    {
        var profile = new WheelProfile { ProcessName = process, SectorCount = 4, Actions = new() { Parent(name, children), new(), new(), new() }, CenterAction = new() { Name = "Center", Parameter = "F3" } };
        profile.EnsureLayers();
        return profile;
    }

    private static (SettingsWindow Window, StackPanel List, TextBlock Breadcrumb, TextBlock Count, TextBox Name) Window(WheelProfile profile)
    {
        // Only FrameworkElement/controls are constructed. Never construct a Window/App or show a native surface.
        var window = (SettingsWindow)RuntimeHelpers.GetUninitializedObject(typeof(SettingsWindow));
        var frameworkState = new FrameworkElement();
        for (Type? type = typeof(FrameworkElement); type != null; type = type.BaseType)
            foreach (var field in type.GetFields(Fields | BindingFlags.Public | BindingFlags.DeclaredOnly))
                field.SetValue(window, field.GetValue(frameworkState));
        Set(window, "_selectedProfile", profile);
        Set(window, "_selectedSlotIndex", 0);
        Set(window, "_selectedMultiSlots", new List<int>());
        Set(window, "_transactionHistory", new ActionTransactionHistory());
        foreach (string key in new[] { "NavTabActiveBgBrush", "CardBackgroundBrush", "AccentPrimaryBrush", "CardBorderBrush", "TextPrimaryBrush", "TextSecondaryBrush", "AccentTextBrush", "SubtleCardBrush", "TextMutedBrush" }) window.Resources[key] = Brushes.Black;
        window.Resources["ModernButtonStyle"] = new Style(typeof(Button));
        var list = new StackPanel();
        var breadcrumb = new TextBlock();
        var count = new TextBlock();
        var name = new TextBox();
        Set(window, "FocusSubActionsOrderedListPanel", list);
        Set(window, "FocusSubActionsChipsPanel", new WrapPanel());
        Set(window, "FocusActionBreadcrumbText", breadcrumb);
        Set(window, "FocusSubActionsCountLabel", count);
        Set(window, "FocusActionNameTextBox", name);
        return (window, list, breadcrumb, count, name);
    }

    private static IEnumerable<string> Texts(DependencyObject node)
    {
        if (node is TextBlock text) yield return text.Text;
        IEnumerable<DependencyObject> children = node switch
        {
            Panel panel => panel.Children.Cast<DependencyObject>(),
            Decorator decorator when decorator.Child != null => new[] { decorator.Child },
            _ => Enumerable.Empty<DependencyObject>()
        };
        foreach (var child in children)
            foreach (string value in Texts(child)) yield return value;
    }
    private static void Set(SettingsWindow window, string field, object value) => typeof(SettingsWindow).GetField(field, Fields)!.SetValue(window, value);
    private static T Get<T>(SettingsWindow window, string field) => (T)typeof(SettingsWindow).GetField(field, Fields)!.GetValue(window)!;
    private static void Invoke(SettingsWindow window, string method, params object[] args) => typeof(SettingsWindow).GetMethod(method, Fields)!.Invoke(window, args);
    private static void Equal<T>(T expected, T actual, string message) => Require(EqualityComparer<T>.Default.Equals(expected, actual), $"{message}: expected={expected}, actual={actual}");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class HeadlessPresentationSource : PresentationSource
    {
        public override System.Windows.Media.Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override System.Windows.Media.CompositionTarget GetCompositionTargetCore() => null!;
    }
}
