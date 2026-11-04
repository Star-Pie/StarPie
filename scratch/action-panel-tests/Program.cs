using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using System.Windows.Markup;
using WinPieGestures;
using WinPieGestures.Plugins;
using StarPie.Plugin;
using PathShape = System.Windows.Shapes.Path;

internal static partial class Program
{
    private static int checks;
    private static void Check(bool condition, string name)
    { if (!condition) throw new InvalidOperationException(name); checks++; }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 4 || args[3] != "--test-instance") throw new ArgumentException("candidate host-hash sandbox --test-instance required");
        string candidate = Path.GetFullPath(args[0]); string sandbox = Path.GetFullPath(args[2]);
        Check(Hash(Path.Combine(candidate, "StarPie.dll")) == args[1] && Hash(typeof(AppConfig).Assembly.Location) == args[1], "actual loaded host hash");
        Check(Hash(Path.Combine(candidate, "StarPie.Plugin.Abstractions.dll")) == Hash(typeof(StarPie.Plugin.IPluginContext).Assembly.Location), "actual loaded SDK hash");
        foreach (string variable in new[] { "LOCALAPPDATA", "APPDATA", "TEMP", "TMP" })
        { string path = Path.Combine(sandbox, variable); Directory.CreateDirectory(path); Environment.SetEnvironmentVariable(variable, path); }
        ConfigManager.DisableAutoStartSync = true; ConfigManager.CurrentConfig = new AppConfig { Language = "en", ShowSelectedActionText = false };
        Check(JsonSerializer.Deserialize<AppConfig>("{}")!.ActionPanels.Count == 0, "old config defaults to no panels");
        var scheme = new ActionPanelScheme { Name = "Panel", Hotkey = "Ctrl+Alt+F10" };
        scheme.Keys[0] = new ActionItem { Type = "Hotkey", Parameter = "Ctrl+C", Name = new string('W', 120) };
        scheme.Keys[1] = new ActionItem { Type = "Plugin", PluginActionRef = new PluginActionRef { PluginId = "fixture", ContributionId = "test" }, ExtensionData = new() { ["value"] = "retained" } };
        var clone = scheme.Clone(); clone.Keys[1]!.ExtensionData!["value"] = "changed";
        Check(scheme.Keys[1]!.ExtensionData!["value"] == "retained", "deep binding clone");
        var smaller = scheme.Clone(); smaller.Keys[11] = new ActionItem { Name = "Keep", Parameter = "Q" };
        Check(!smaller.TryResize(2, 3) && smaller.Keys.Count == 12, "shrink never discards a configured key");
        smaller.Keys[11] = null; Check(smaller.TryResize(5, 5) && smaller.Keys.Count == 25, "grow grid");
        Check(smaller.TryResize(2, 3) && smaller.Keys.Count == 6 && smaller.Keys[0]!.Parameter == "Ctrl+C", "safe shrink preserves linear order");
        Check(!smaller.TryResize(11, 3) && !smaller.TryResize(4, 17), "dimension bounds");
        Check(ActionPanelScheme.Validate(new[] { scheme }), "valid scheme");
        var duplicate = scheme.Clone(); duplicate.Id = Guid.NewGuid().ToString("N"); duplicate.Name = "Other";
        Check(!ActionPanelScheme.Validate(new[] { scheme, duplicate }), "duplicate shortcut rejected");
        duplicate.Hotkey = ""; duplicate.Name = "panel";
        Check(!ActionPanelScheme.Validate(new[] { scheme, duplicate }), "duplicate names rejected");
        duplicate.Name = "Other"; duplicate.Keys[0]!.Type = "ActionPanel";
        Check(!ActionPanelScheme.Validate(new[] { duplicate }), "nested panels rejected");
        Check(ActionPanelHotkey.TryParse("Ctrl+Alt+F10", out uint key, out var modifiers) && key == 121 && modifiers == (ModifierKeys.Control | ModifierKeys.Alt), "shortcut parsed outside hook");
        foreach (string invalid in new[] { "A", "Ctrl", "Escape", "Ctrl+K,Ctrl+C", "Ctrl++A" }) Check(!ActionPanelHotkey.TryParse(invalid, out _, out _), "invalid direct shortcut " + invalid);
        Check(ActionPanelHotkey.TryParse("F10", out _, out _), "unmodified function key allowed");
        var config = new AppConfig { Trigger = new TriggerConfig { TriggerType = "Keyboard", VkCode = 121, RequireCtrl = true } };
        Check(ActionPanelHotkey.ConflictsWithWheels(121, modifiers, config), "global wheel shortcut conflict");
        config.Trigger = new TriggerConfig();
        Check(!ActionPanelHotkey.ConflictsWithWheels(121, modifiers, config), "mouse trigger does not conflict");
        config.ActionPanels.Add(scheme);
        var roundtrip = JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(config))!;
        Check(roundtrip.ActionPanels[0].Rows == 4 && roundtrip.ActionPanels[0].Keys[1]!.ExtensionData!["value"] == "retained", "configuration roundtrip retains actions");
        var router = new ActionPanelKeyRouter(); ActionPanelRoute[] routes = [new(scheme.Id, key, modifiers)];
        Check(router.TryHandle(key, modifiers, true, false, false, routes, out string? id, out bool cancel) && id == scheme.Id && !cancel, "first direct down requests once");
        Check(router.TryHandle(key, modifiers, true, false, false, routes, out id, out _) && id == null, "repeat swallowed without repeat request");
        Check(router.TryHandle(key, ModifierKeys.None, false, true, false, [], out _, out _), "key up paired even after pause or route deletion");
        Check(!router.TryHandle(key, modifiers, false, false, false, routes, out _, out _), "key up only consumed once");
        Check(!router.TryHandle(key, modifiers, true, true, false, routes, out _, out _), "paused shortcut does not take input");
        Check(!router.TryHandle(key, modifiers | ModifierKeys.Shift, true, false, false, routes, out _, out _), "extra modifiers do not match");
        foreach (bool reverse in new[] { false, true })
        {
            var overlapping = new ActionPanelKeyRouter();
            ActionPanelRoute[] twoRoutes = [new("a", 65, ModifierKeys.Control), new("b", 66, ModifierKeys.Control)];
            Check(overlapping.TryHandle(65, ModifierKeys.Control, true, false, false, twoRoutes, out id, out _) && id == "a", "overlap A down");
            Check(overlapping.TryHandle(66, ModifierKeys.Control, true, false, false, twoRoutes, out id, out _) && id == "b", "overlap B down");
            Check(overlapping.TryHandle(65, ModifierKeys.Control, true, false, false, twoRoutes, out id, out _) && id == null, "overlap repeat does not retrigger A");
            foreach(uint release in reverse ? new[] { 66u, 65u } : new[] { 65u, 66u })
                Check(overlapping.TryHandle(release, ModifierKeys.None, false, true, false, [], out _, out _), "overlap both releases survive pause and route deletion");
        }
        Check(router.TryHandle(27, ModifierKeys.None, true, false, true, routes, out _, out cancel) && cancel, "Esc cancels active panel");
        Check(router.TryHandle(27, ModifierKeys.None, false, true, false, [], out _, out _), "Esc up paired after closure and pause");
        var prepared = ActionPanelManager.Prepare(scheme.Keys[1]!)!; prepared.ExtensionData!["value"] = "other";
        Check(scheme.Keys[1]!.ExtensionData!["value"] == "retained", "dispatch preparation is a private snapshot");
        Check(ActionPanelManager.Prepare(new ActionItem { Type = "ActionPanel" }) == null, "panel cannot recursively dispatch itself");
        Check(ActionTypeCatalog.BuildLocalizedActionTypes().Count(item => item.Tag == "ActionPanel") == 1 && ActionTypeCatalog.BuildAggregatedActionTypes().Count(item => item.Tag == "ActionPanel") == 1, "both action lists expose one host panel entry");
        string? keyboardDll = Environment.GetEnvironmentVariable("KEYBOARD_PLUGIN_CANDIDATE");
        if (keyboardDll != null)
        {
            Console.WriteLine("PLUGIN_SHA256=" + Hash(keyboardDll));
            var assembly = Assembly.LoadFrom(keyboardDll);
            var modes = assembly.GetType("StarPie.Plugin.TemporaryKeyboard.KeyboardMode", true)!;
            var layouts = assembly.GetType("StarPie.Plugin.TemporaryKeyboard.KeyboardLayouts", true)!;
            foreach (string mode in new[] { "Numeric", "Letters", "Functions", "Full" })
            {
                var rows = (System.Collections.IEnumerable)layouts.GetMethod("Create")!.Invoke(null, [Enum.Parse(modes, mode), false])!;
                foreach (System.Collections.IEnumerable row in rows)
                foreach (object item in row)
                {
                    string kind = item.GetType().GetProperty("Kind")!.GetValue(item)!.ToString()!;
                    if (kind is "Modifier" or "Caps") continue;
                    string value = (string)(item.GetType().GetProperty("ShortcutKey")!.GetValue(item) ?? item.GetType().GetProperty("Value")!.GetValue(item))!;
                    Check(InputPanelHotkey.TryNormalize(value, out _), "template key accepted by actual host parser: " + value);
                }
            }
        }
        _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        // Load the candidate's matching source resources without constructing App or starting it.
        var xaml = XDocument.Load(Path.Combine(Environment.CurrentDirectory, "WinPieGestures", "App.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var resources = new XElement(xaml.Root!.Element(presentation + "Application.Resources")!.Elements().Single());
        foreach (var attribute in xaml.Root.Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
            if (resources.Attribute(attribute.Name) == null) resources.Add(new XAttribute(attribute.Name, attribute.Name.LocalName == "local" ? "clr-namespace:WinPieGestures;assembly=StarPie" : attribute.Value));
        Application.Current.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(resources.ToString()));
        ConfigManager.CurrentConfig = config;
        var future = JsonSerializer.Deserialize<ActionPanelScheme>("{\"Name\":\"Future\",\"future\":{\"nested\":[1,2,3]}}")!;
        var futureClone = future.Clone(); futureClone.AdditionalData!["other"] = JsonSerializer.SerializeToElement(true);
        Check(!future.AdditionalData!.ContainsKey("other") && JsonSerializer.Serialize(futureClone).Contains("\"nested\":[1,2,3]"), "unknown scheme metadata roundtrips and clones independently");
        foreach (string theme in new[] { "Light", "Dark" })
        {
            config.AppTheme = theme;
            var window = new ActionPanelWindow(scheme.Clone(), 0, 0);
            Layout(window, 650, 430); var grid = Descendants(window.Content!).OfType<UniformGrid>().Single();
            var cells = grid.Children.OfType<Button>().ToArray();
            Check(cells.Length == 12 && cells.All(cell => Math.Abs(cell.ActualWidth - cells[0].ActualWidth) < .01), "equal host keys " + theme);
            Check(cells[0].Content is TextBlock { TextTrimming: TextTrimming.CharacterEllipsis }, "host long caption ellipsis");
            double width = cells[0].ActualWidth; Layout(window, 900, 600);
            Check(cells[0].ActualWidth > width, "host grid resizes with window");
            Check(!window.ShowActivated && !window.ShowInTaskbar && !window.Focusable, "host runtime nonactivating metadata");
            Render(window, Path.Combine(sandbox, "host-runtime-" + theme + ".png")); window.Close();
            var editor = new ActionPanelEditor(scheme.Id); Layout(editor, 1060, 720);
            var name = (TextBox)typeof(ActionPanelEditor).GetField("_name", Fields)!.GetValue(editor)!; name.Text = "Edited clone";
            Check(config.ActionPanels[0].Name == "Panel", "editor never mutates the live scheme before Save");
            Check(((List<ActionPanelScheme>)typeof(ActionPanelEditor).GetField("_schemes", Fields)!.GetValue(editor)!)[0].Name == "Edited clone", "renaming updates the editor clone");
            Render(editor, Path.Combine(sandbox, "host-editor-" + theme + ".png")); editor.Close();
            var actionEditor = new ActionPanelActionEditor(new ActionItem { Type = "Hotkey", Name = "Example", Parameter = "Ctrl+C" });
            Layout(actionEditor, 580, 580); Check(Descendants(actionEditor.Content!).OfType<HotkeyRecorderBox>().Count() == 1, "reuses host hotkey recorder"); actionEditor.Close();
        }
        foreach (string json in new[] { "[null]", "[{\"Name\":\"Broken\",\"Keys\":null}]" })
        {
            config.ActionPanels = JsonSerializer.Deserialize<List<ActionPanelScheme>>(json)!;
            string original = JsonSerializer.Serialize(config.ActionPanels);
            var brokenEditor = new ActionPanelEditor(null); Layout(brokenEditor, 1060, 720);
            var save = Descendants(brokenEditor.Content!).OfType<Button>().Single(button => button.Name == "SaveActionPanels");
            Check(!save.IsEnabled, "bad panel data opens read-only"); save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(JsonSerializer.Serialize(config.ActionPanels) == original, "even a forced save event preserves malformed source"); brokenEditor.Close();
        }
        config.ActionPanels = new() { scheme };
        KeyboardPanelContext();
        StalePanelPost();
        HighlightTransition();
        EditorParity(sandbox);
        ShellToolParity(sandbox);
        MirrorSources(sandbox);
        Application.Current.Shutdown();
        Console.WriteLine($"PASS {checks} candidate-bound checks; no Show, hooks, input, action execution or system changes"); return 0;
    }
    private static void StalePanelPost()
    {
        bool current = true; int checkedAtDispatch = 0;
        var context = new ActionExecutionContext((nint)123, 456, "fixture", () => { checkedAtDispatch++; return current; });
        typeof(ActionExecutor).GetMethod("ExecuteCore", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [new ActionItem { Type = "ActionPanel", Parameter = "example" }, context]);
        Check(checkedAtDispatch == 0, "panel validity is deferred until the dispatcher runs");
        current = false;
        var frame = new System.Windows.Threading.DispatcherFrame();
        Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, (Action)(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        Check(checkedAtDispatch == 1, "posted panel rechecks the original gesture after it becomes stale in the same target");
        Check(typeof(ActionPanelManager).GetField("_window", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null) == null,
            "stale panel post creates no window");
    }
    private static void HighlightTransition()
    {
        var type = typeof(RadialWindow); var wheel = (RadialWindow)RuntimeHelpers.GetUninitializedObject(type);
        void Set(string name, object value) => type.GetField(name, Fields)!.SetValue(wheel, value);
        var parent = new PathShape(); var child = new PathShape();
        Set("_sectorPaths", new List<PathShape> { parent }); Set("_contentPanels", new List<StackPanel>());
        Set("_sectorTransforms", new List<TranslateTransform>()); Set("_containerTransforms", new List<TranslateTransform>()); Set("_sectorAngles", new List<double>());
        Set("_subSectorPaths", new List<PathShape> { child }); Set("_subContentContainers", new List<Grid>());
        Set("_subSectorTransforms", new List<TranslateTransform>()); Set("_subContainerTransforms", new List<TranslateTransform>()); Set("_subSectorAngles", new List<double>());
        Set("_defaultSectorBrush", Brushes.White); Set("_highlightSectorBrush", Brushes.Red); Set("_sectorBorderBrush", Brushes.Gray);
        Set("_subDefaultSectorBrush", Brushes.White); Set("_subHighlightSectorBrush", Brushes.Blue); Set("_subSectorBorderBrush", Brushes.Gray);
        Set("_highlightBorderBrush", Brushes.Red); Set("_subHighlightBorderBrush", Brushes.Blue); Set("_textColorBrush", Brushes.Black);
        Set("_currentHighlightedSector", -999); Set("_currentHighlightedSubSector", -1); Set("_activeSubTierParentSector", 0);
        Set("CoreExitIcon", new PathShape()); Set("CoreCustomImageEllipse", new System.Windows.Shapes.Ellipse());
        Set("CoreSelectionTextPanel", new StackPanel());
        Set("CoreSelectionOverlay", Activator.CreateInstance(type.GetField("CoreSelectionOverlay", Fields)!.FieldType)!);
        ConfigManager.CurrentConfig.ShowSelectedActionText = false;
        wheel.HighlightSector(0, -1, false, true); Check(ReferenceEquals(parent.Fill, Brushes.Red), "parent selected alone");
        wheel.HighlightSector(0, 0, false, true); Check(ReferenceEquals(parent.Fill, Brushes.White) && ReferenceEquals(child.Fill, Brushes.Blue), "child selection removes parent highlight");
        wheel.HighlightSector(0, -1, false, true); Check(ReferenceEquals(parent.Fill, Brushes.Red) && !ReferenceEquals(child.Fill, Brushes.Blue), "returning to parent clears child highlight");
    }
    private static void KeyboardPanelContext()
    {
        ConfigManager.CurrentConfig.EnableComponentIdentification = false; ConfigManager.CurrentConfig.PinWheelAfterTrigger = false;
        var mouse = new MouseHook(); var keyboard = new KeyboardHook();
        ActionExecutor.ReleaseStuckModifiersOverride = () => { };
        try
        {
            using var controller = new GestureController(mouse, keyboard);
            var profile = new WheelProfile { Actions = Enumerable.Range(0, 8).Select(_ => new ActionItem { Type = "ActionPanel", Parameter = "example", Name = "Panel" }).ToList() };
            profile.EnsureLayers();
            void Set(string name, object value) => typeof(GestureController).GetField(name, Fields)!.SetValue(controller, value);
            controller.TestBeginGestureTracking();
            Set("_activeProfile", profile); Set("_activeTrigger", new TriggerConfig { TriggerType = "Keyboard", VkCode = 121 });
            Set("_isGestureActive", true); Set("_isWaitingForThreshold", false); Set("_selectedSectorIndex", 0); Set("_selectedSubSectorIndex", -1);
            Set("_keyboardPanelTarget", (nint)123); Set("_keyboardPanelTargetPid", 456u);
            ActionExecutionContext? captured = null; int requests = 0;
            controller.TestActionEnqueuedWithContext = (_, context) => { captured = context; requests++; };
            typeof(GestureController).GetMethod("KeyboardHook_OnKeyUp", Fields)!.Invoke(controller, [keyboard, new GlobalKeyEventArgs(121, ModifierKeys.None)]);
            var frame = new System.Windows.Threading.DispatcherFrame();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, (Action)(() => frame.Continue = false));
            System.Windows.Threading.Dispatcher.PushFrame(frame);
            Check(requests == 1 && captured?.TargetHwnd == (nint)123 && captured.TargetProcessId == 456, "keyboard wheel preserves original HWND/PID with both optional modes off");
            int executed = 0; ActionExecutor.ActionExecutionOverride = (_, _) => executed++;
            var stale = new ActionExecutionContext(captured!.TargetHwnd, captured.TargetProcessId, "fixture", () => false);
            Check(!ActionExecutor.ProcessActionEnvelope(new ActionEnvelope(new ActionItem { Type = "ActionPanel" }, stale)) && executed == 0, "blocked queue discards a stale panel request before any foreground lookup or execution");
        }
        finally { ActionExecutor.ResetTestSeams(); }
    }
    private static void Layout(Window window, double width, double height)
    { var root = (FrameworkElement)window.Content; root.ApplyTemplate(); root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout(); foreach (var control in Descendants(root).OfType<Control>()) control.ApplyTemplate(); root.UpdateLayout(); }
    private static IEnumerable<DependencyObject> Descendants(object root)
    { if(root is not DependencyObject node) yield break; yield return node; foreach(object child in LogicalTreeHelper.GetChildren(node)) foreach(var descendant in Descendants(child)) yield return descendant; }
    private static void Render(Window window, string path)
    {
        var root = (FrameworkElement)window.Content;
        int width = (int)Math.Ceiling(root.ActualWidth + root.Margin.Left + root.Margin.Right), height = (int)Math.Ceiling(root.ActualHeight + root.Margin.Top + root.Margin.Bottom);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual(); using(var drawing = background.RenderOpen()) drawing.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
        bitmap.Render(background); bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); encoder.Save(file);
    }
}
