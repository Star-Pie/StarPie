using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using StarPie.Plugin;
using WinPieGestures;
using WinPieGestures.Plugins;

internal static partial class Program
{
    private static void EditorParity(string sandbox)
    {
        var schema = new PluginActionRegistration { PluginId = "fixture.launch", Parameters = new ParameterField[]
        {
            new() { Key = HostActionFields.Parameter, Type = ParameterFieldType.File, Label = "Program path", Required = true },
            new() { Key = HostActionFields.Arguments, Type = ParameterFieldType.Text, Label = "Arguments" },
            new() { Key = "mode", Type = ParameterFieldType.Enum, Label = "Mode", Options = new[] { new ParameterOption { Value = "follow", Label = "Follow host" } } }
        } };
        void Populate(ActionPanelActionEditor editor)
        {
            // Supply only a fixture schema; no plugin is installed or activated by this test.
            ((TextBlock)typeof(ActionPanelActionEditor).GetField("_status", Fields)!.GetValue(editor)!).Text = "";
            typeof(ActionPanelActionEditor).GetMethod("BuildClaimedFields", Fields)!.Invoke(editor, [schema]);
        }
        ActionItem Item(ActionPanelActionEditor editor) => (ActionItem)typeof(ActionPanelActionEditor).GetField("_item", Fields)!.GetValue(editor)!;
        var original = new ActionItem { Type = "Launch", Name = "Custom caption", Parameter = "legacy.exe", Arguments = "--keep", IconKey = "user.icon",
            ExtensionData = new() { [HostActionFields.Parameter] = "preferred.exe", ["future"] = "retained", ["mode"] = "follow" } };
        string untouched = JsonSerializer.Serialize(original);
        foreach (string language in new[] { "zh-CN", "zh-TW", "en", "ja" })
        foreach (string theme in new[] { "Light", "Dark" })
        {
            ConfigManager.CurrentConfig.Language = language; I18n.SetLanguage(language);
            ConfigManager.CurrentConfig.AppTheme = theme; AppThemeManager.ApplyTheme(new Grid(), theme);
            Check(I18n.CurrentLanguageCode == language && AppThemeManager.CurrentEffectiveTheme == theme, "the requested language and effective theme are actually active");
            var called = new List<ActionPanelPathPicker>(); ActionPanelPathSelection? selection = null;
            var queued = new List<ActionItem>();
            var editor = new ActionPanelActionEditor(original, (_, kind) => { called.Add(kind); return selection; },
                _ => Task.FromResult(new PluginActionValidation()), queued.Add);
            Populate(editor); Layout(editor, 480, 540);
            var controls = Descendants(editor.Content!).OfType<Control>().ToArray();
            var path = controls.OfType<TextBox>().Single(box => box.Name == "PanelLaunchPath");
            var buttons = controls.OfType<Button>().Where(button => button.Name.StartsWith("PanelPath", StringComparison.Ordinal)).ToArray();
            Check(path.Text == "preferred.exe" && called.Count == 0 && queued.Count == 0, "opening the editor preserves projected values without picker or action calls");
            Check(buttons.Length == 3 && controls.OfType<Button>().Count(button => button.Name == "TestPanelAction") == 1, "all wheel-style picker and test commands are present");
            Check(ReferenceEquals(path.Style, editor.FindResource("ModernTextBoxStyle")), "path and name use the shared wheel input style");
            Check(buttons.All(button => button.Template != null && button.ActualWidth <= 440), "picker commands wrap within a narrow editor in every language/theme");
            Check(path.ActualWidth > 350 && path.ActualWidth <= 440, "path stays readable at narrow width");
            foreach (var kind in Enum.GetValues<ActionPanelPathPicker>())
            {
                var button = buttons.Single(button => button.Name == "PanelPath" + kind);
                string prior = JsonSerializer.Serialize(Item(editor));
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(JsonSerializer.Serialize(Item(editor)) == prior, "cancelled " + kind + " picker changes nothing");
                selection = new("selected-" + kind + ".exe", "Suggested caption");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(called[^1] == kind && path.Text == selection.Path && ActionParameterProjection.Project(Item(editor))[HostActionFields.Parameter] == selection.Path,
                    kind + " writes the actual projected launch target");
                Check(Item(editor).Name == "Custom caption" && Item(editor).Arguments == "--keep" && Item(editor).IconKey == "user.icon" && Item(editor).ExtensionData!["future"] == "retained",
                    kind + " preserves custom label, icon, arguments and unrelated metadata");
                selection = null;
            }
            path.Text = "";
            Check(ActionParameterProjection.Project(Item(editor))[HostActionFields.Parameter] == "" && Item(editor).Parameter == "", "clearing the path cannot resurrect the legacy fallback");
            path.Text = "sample.exe";
            if (language == "zh-CN") { Layout(editor, 760, 540); Render(editor, System.IO.Path.Combine(sandbox, "action-editor-" + theme + ".png")); }
            editor.Close(); Check(JsonSerializer.Serialize(original) == untouched, "cancelling the editor leaves the original action byte-equivalent");
        }

        var emptyName = new ActionPanelActionEditor(new ActionItem { Type = "Launch", Name = "" }, (_, _) => new("named.exe", "Picked name"));
        Populate(emptyName);
        Descendants(emptyName.Content!).OfType<Button>().Single(button => button.Name == "PanelPathLibrary").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(Item(emptyName).Name == "Picked name", "an empty label is filled from the same picker result"); emptyName.Close();

        ConfigManager.CurrentConfig.Language = "en"; I18n.SetLanguage("en");
        var pending = new TaskCompletionSource<PluginActionValidation>(); int validations = 0; var submitted = new List<ActionItem>();
        var testEditor = new ActionPanelActionEditor(new ActionItem { Type = "Hotkey", Name = "Before", Parameter = "Ctrl+C" },
            validate: _ => { validations++; return pending.Task; }, enqueue: submitted.Add);
        Task test = testEditor.TestActionAsync(); Task repeated = testEditor.TestActionAsync();
        Check(Descendants(testEditor.Content!).OfType<Button>().Any(button => Equals(button.Content, I18n.T("FocusHotkeyBuilderBtnText"))), "hotkey editing exposes the same builder entry as the wheel");
        Check(validations == 1 && repeated.IsCompleted && !((UIElement)testEditor.Content).IsEnabled, "test validation is single-flight and freezes editing");
        Item(testEditor).Name = "Changed while blocked"; pending.SetResult(new()); Drain(test);
        Check(submitted.Count == 1 && submitted[0].Name == "Before" && testEditor.Result == null, "test queues only its validated snapshot without saving");
        Check(((UIElement)testEditor.Content).IsEnabled, "successful test restores editing"); testEditor.Close();

        var closedPending = new TaskCompletionSource<PluginActionValidation>(); int afterClose = 0;
        var closed = new ActionPanelActionEditor(new ActionItem { Type = "Hotkey", Parameter = "Ctrl+C" }, validate: _ => closedPending.Task, enqueue: _ => afterClose++);
        Task late = closed.TestActionAsync(); closed.Close(); closedPending.SetResult(new()); Drain(late);
        Check(afterClose == 0, "closing during validation prevents a late queued action");
        foreach (bool throws in new[] { false, true })
        {
            var failed = new ActionPanelActionEditor(new ActionItem { Type = "Hotkey", Parameter = "Ctrl+C" },
                validate: _ => throws ? Task.FromException<PluginActionValidation>(new InvalidOperationException("fixture")) : Task.FromResult(new PluginActionValidation { PluginMessage = "Rejected fixture" }),
                enqueue: _ => throw new InvalidOperationException("Must never enqueue"));
            Drain(failed.TestActionAsync());
            Check(((UIElement)failed.Content).IsEnabled && failed.Result == null, "failed validation restores UI without queue or save"); failed.Close();
        }

        var host = new StackPanel();
        var settings = (SettingsWindow)RuntimeHelpers.GetUninitializedObject(typeof(SettingsWindow));
        typeof(SettingsWindow).GetField("FocusActionPanelHost", Fields)!.SetValue(settings, host);
        typeof(SettingsWindow).GetMethod("RefreshActionPanelSettings", Fields)!.Invoke(settings, [new ActionItem { Type = "ActionPanel" }]);
        CheckCompact(host, "main wheel focus entry");
        var secondary = new ContributedActionParameterPanel();
        typeof(ContributedActionParameterPanel).GetField("_viewModel", Fields)!.SetValue(secondary, new SubSlotViewModel());
        secondary.SetAction(new ActionItem { Type = "ActionPanel" }); CheckCompact(secondary, "secondary wheel entry");
    }
    private static void CheckCompact(StackPanel panel, string name)
    {
        panel.Measure(new Size(800, 300)); panel.Arrange(new Rect(0, 0, 800, 300)); panel.UpdateLayout();
        var button = panel.Children.OfType<Button>().Single(); button.ApplyTemplate();
        Check(button.HorizontalAlignment == HorizontalAlignment.Left && button.ActualWidth < 600 && button.Template != null,
            name + " uses a content-width themed configuration button");
        Check(ReferenceEquals(button.Style, Application.Current.FindResource("ModernButtonStyle")), name + " explicitly resolves the shared button style");
    }
    private static void Drain(Task task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            _ = task.ContinueWith(_ => Application.Current.Dispatcher.BeginInvoke((Action)(() => frame.Continue = false)), TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
        }
        task.GetAwaiter().GetResult();
    }
}
