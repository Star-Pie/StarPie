using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinPieGestures;
using WinPieGestures.Plugins;

internal static class Program
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;

    [STAThread]
    private static void Main(string[] args)
    {
        Check(args is [_, "--test-instance", _] && Hash(args[0]) == Hash(typeof(SettingsWindow).Assembly.Location),
            "UI regression binds the exact release candidate");
        string sandbox = Directory.CreateTempSubdirectory("starpie-trigger-scene-").FullName;
        foreach (var env in new[] { "APPDATA", "LOCALAPPDATA", "TEMP", "TMP" })
            Environment.SetEnvironmentVariable(env, sandbox);
        try { Run(args[2]); }
        finally { Directory.Delete(sandbox, true); }
        Console.WriteLine($"{_checks} offscreen trigger scene checks passed.");
    }

    private static void Run(string evidence)
    {
        ConfigManager.DisableAutoStartSync = true;
        var config = ConfigManager.CurrentConfig;
        config.AutoCheckUpdate = false;
        config.BlacklistedProcesses = ["sldworks.exe"];
        config.WhitelistedProcesses = ["code.exe"];
        config.BlacklistTriggerOverrides = new(StringComparer.OrdinalIgnoreCase)
        {
            ["sldworks.exe"] = new() { TriggerType = "Mouse", MouseButton = "XButton1" }
        };
        config.IsolationMode = "Blacklist";
        config.DisableOnFullScreen = true;
        config.LongPressTrigger = true;
        config.LongPressOnlyTrigger = false;
        var app = new App();
        app.InitializeComponent();
        var window = new SettingsWindow();
        window.EnsureUiInitialized();

        var section = Find<Grid>("TriggerSettingsGrid");
        var list = Find<ListBox>("BlacklistListBox");
        var simulator = Find<ComboBox>("TriggerSimulatorProcessBox");
        var input = Find<ComboBox>("TriggerSimulatorInputBox");
        var result = Find<TextBlock>("TriggerSimulatorOutcome");
        var details = Find<StackPanel>("TriggerSimulatorPanel");
        var hold = Find<StackPanel>("LongPressDelayPanel");
        var move = Find<StackPanel>("TriggerDistancePanel");
        string before = JsonSerializer.Serialize(config);
        long revision = ConfigManager.ConfigurationRevision;

        Check(Find<StackPanel>("TriggerCommonPanel").IsVisible == false &&
            Find<StackPanel>("TriggerCommonPanel").Visibility == Visibility.Visible &&
            Find<StackPanel>("TriggerAdvancedPanel").Visibility == Visibility.Collapsed,
            "common settings are the default and advanced settings are out of the first viewport");
        Radio("TriggerRulesTab");
        Check(list.Items.OfType<BlacklistProcessItemViewModel>().Single().OverrideTrigger?.MouseButton == "XButton1",
            "exclusion list retains its configured app-specific key");
        simulator.ApplyTemplate();
        Check(simulator.Template.FindName("PART_EditableTextBox", simulator) is TextBox { Visibility: Visibility.Visible },
            "the simulated process picker has a usable editable field");
        ((TextBox)simulator.Template.FindName("PART_EditableTextBox", simulator)).Text = "sldworks";
        Check(result.Text == I18n.T("TriggerSceneResultNative"), "global input passes through a blacklisted app");
        input.SelectedValue = TriggerSceneInput.ProcessOverride;
        Check(result.Text == I18n.T("TriggerSceneResultWheel"), "the same app can be simulated with its dedicated key");
        Radio("TriggerViewAllowRadio");
        var allow = list.Items.OfType<BlacklistProcessItemViewModel>().Single();
        Check(allow.ProcessName == "code.exe" && allow.ConfigureVisibility == Visibility.Collapsed &&
            config.IsolationMode == "Blacklist", "browsing the allow list leaves the active policy unchanged");
        simulator.Text = "code.exe";
        input.SelectedValue = TriggerSceneInput.Global;
        Find<CheckBox>("TriggerSimulatorFullscreen").IsChecked = true;
        Invoke("RefreshTriggerSimulation");
        Check(result.Text == I18n.T("TriggerSceneResultWheel"), "full-screen exception works while browsing either list");
        simulator.Text = "notepad.exe";
        Invoke("RefreshTriggerSimulation");
        Check(Find<TextBlock>("TriggerSimulatorReason").Text == I18n.T("TriggerSceneReasonFullScreenBypass"),
            "full-screen simulator explains the blocking reason");
        Radio("TriggerAdvancedTab");
        var advanced = Find<StackPanel>("TriggerAdvancedPanel").Children.OfType<Expander>().ToArray();
        Check(advanced.Length == 8 && advanced.Contains(Find<Expander>("PortalTriggerOptions")) &&
            advanced.All(item => !item.IsExpanded) && details.Visibility == Visibility.Collapsed,
            "all advanced groups, including linked wheels, start collapsed");
        advanced[0].IsExpanded = true;
        Radio("TriggerCommonTab");
        Check(JsonSerializer.Serialize(config) == before && ConfigManager.ConfigurationRevision == revision,
            "reading, navigating, changing simulated values and expanding controls do not write configuration");

        Radio("TriggerHoldMode");
        Check(config.LongPressTrigger && config.LongPressOnlyTrigger &&
            hold.Visibility == Visibility.Visible && move.Visibility == Visibility.Collapsed &&
            !Find<CheckBox>("GestureEnabledCheckBox").IsEnabled, "hold-only maps to old flags and pauses independent gestures");
        Radio("TriggerDragMode");
        Check(!config.LongPressTrigger && !config.LongPressOnlyTrigger &&
            hold.Visibility == Visibility.Collapsed && move.Visibility == Visibility.Visible &&
            Find<CheckBox>("GestureEnabledCheckBox").IsEnabled, "drag mode shows only the applicable distance");
        config.Trigger = new() { MouseButton = "LeftButton", RequireCtrl = true };
        Radio("TriggerEitherMode");
        Check(move.Visibility == Visibility.Collapsed && hold.Visibility == Visibility.Visible &&
            Find<RadioButton>("TriggerDragMode").IsEnabled, "modified left button follows the live combined-mode movement exception");
        config.Trigger = new();
        Invoke("UpdateTriggerBadgeDisplay");

        Radio("TriggerRulesTab");
        Radio("TriggerViewExcludeRadio");
        Invoke("AddBlacklistProcess", @"C:\Example\Notepad.EXE");
        Check(config.BlacklistedProcesses!.Contains("notepad.exe") && config.WhitelistedProcesses!.SequenceEqual(["code.exe"]),
            "normalized manual input is added to the list being edited");
        Radio("TriggerViewAllowRadio");
        Invoke("AddBlacklistProcess", "notepad");
        Check(config.BlacklistedProcesses!.Contains("notepad.exe") && config.WhitelistedProcesses!.Contains("notepad.exe") &&
            config.IsolationMode == "Blacklist", "editing inactive list preserves its counterpart and active mode");
        Find<TextBox>("TriggerFilterProcessesBox").Text = "code";
        Check(list.Items.OfType<BlacklistProcessItemViewModel>().Select(item => item.ProcessName).SequenceEqual(["code.exe"]),
            "process search filters the viewed list only");
        Radio("TriggerViewExcludeRadio");
        Invoke("RemoveTriggerRule", "sldworks.exe");
        Check(config.BlacklistedProcesses?.Contains("sldworks.exe") == false &&
            !config.BlacklistTriggerOverrides!.ContainsKey("sldworks.exe"), "removing an exclusion also removes its override");
        var undo = Find<Button>("TriggerUndoRemoveButton");
        undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(config.BlacklistedProcesses!.Contains("sldworks.exe") &&
            config.BlacklistTriggerOverrides!["sldworks.exe"].MouseButton == "XButton1", "undo restores the original exclusion and key");
        config.BlacklistedProcesses.Add("  LEGACY.EXE  ");
        config.BlacklistTriggerOverrides["legacy.exe"] = new() { MouseButton = "MiddleButton" };
        Invoke("RefreshProcessListUI");
        var legacy = list.Items.OfType<BlacklistProcessItemViewModel>().Single(item => item.ProcessName.Contains("LEGACY"));
        Invoke("ResetProcessTriggerBtn_Click", new Button { Tag = legacy }, new RoutedEventArgs());
        Check(!config.BlacklistTriggerOverrides.ContainsKey("legacy.exe"), "reset finds a normalized legacy override key");
        config.BlacklistTriggerOverrides["legacy.exe"] = new() { MouseButton = "MiddleButton" };
        Invoke("RemoveTriggerRule", legacy.ProcessName);
        Check(!config.BlacklistedProcesses.Contains(legacy.ProcessName) && !config.BlacklistTriggerOverrides.ContainsKey("legacy.exe"),
            "deleting a legacy row also removes the normalized override");
        undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(config.BlacklistedProcesses.Contains(legacy.ProcessName) &&
            config.BlacklistTriggerOverrides["legacy.exe"].MouseButton == "MiddleButton",
            "undo restores the original legacy text and its override");
        config.BlacklistedProcesses.AddRange(["DUPLICATE.EXE", "duplicate.exe"]);
        config.BlacklistTriggerOverrides["duplicate.exe"] = new() { MouseButton = "MiddleButton" };
        Invoke("RemoveTriggerRule", "duplicate.exe");
        Check(config.BlacklistedProcesses.Contains("DUPLICATE.EXE") && !config.BlacklistedProcesses.Contains("duplicate.exe") &&
            config.BlacklistTriggerOverrides.ContainsKey("duplicate.exe"), "deletion targets the exact selected spelling and preserves the surviving override");
        undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(config.BlacklistedProcesses.Contains("duplicate.exe") && config.BlacklistTriggerOverrides.ContainsKey("duplicate.exe"),
            "undo restores the selected spelling without duplicating its override");

        Radio("IsolationWhitelistRadio");
        Check(config.IsolationMode == "Whitelist" && config.BlacklistedProcesses!.Contains("sldworks.exe") &&
            Find<RadioButton>("TriggerViewAllowRadio").IsChecked == true, "switching active policy preserves both lists");

        foreach (var language in new[] { LanguageCode.ZhCn, LanguageCode.ZhTw, LanguageCode.En, LanguageCode.Ja })
        {
            I18n.CurrentLanguage = language;
            Invoke("ApplyTriggerSceneLocalization");
            Invoke("RefreshProcessListUI");
            Radio("TriggerCommonTab");
            Invoke("SetTriggerWorkspaceWidth", 970d);
            section.Measure(new Size(970, 550));
            section.Arrange(new Rect(0, 0, 970, 550));
            Check(Grid.GetColumn(details) == 1 && details.ActualWidth > 225 &&
                !Find<RadioButton>("TriggerCommonTab").Content.ToString()!.Contains("TriggerScene", StringComparison.Ordinal),
                "localized wide layout " + language);
            if (language == LanguageCode.ZhCn) Capture(section, evidence, "trigger-common.png", 970, 550);
            Invoke("SetTriggerWorkspaceWidth", 650d);
            section.Measure(new Size(650, 520));
            section.Arrange(new Rect(0, 0, 650, 520));
            Check(Grid.GetRow(details) == 1 && Grid.GetColumn(details) == 0 && Find<RadioButton>("TriggerHoldMode").ActualWidth > 25,
                "narrow layout stacks the simulator " + language);
            Radio("TriggerRulesTab");
        }
        Check(new WindowInteropHelper(window).Handle == nint.Zero && !PluginHost.IsInitialized,
            "no native window, plugin host, or actual trigger execution");

        T Find<T>(string name) where T : FrameworkElement => (T)window.FindName(name)!;
        void Radio(string name) => Find<RadioButton>(name).IsChecked = true;
        void Invoke(string method, params object[] values) =>
            typeof(SettingsWindow).GetMethod(method, Private)!.Invoke(window, values);
    }

    private static void Capture(FrameworkElement element, string evidence, string name, int width, int height)
    {
        Directory.CreateDirectory(evidence);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(evidence, name));
        encoder.Save(file);
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
        Console.WriteLine("PASS " + message);
    }
}
