using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using WinPieGestures;
using WinPieGestures.Plugins;

internal static class Program
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;

    [STAThread]
    private static void Main(string[] args)
    {
        Check(args is [_, _, "--test-instance"] &&
            Hash(Path.Combine(args[0], "StarPie" + ".dll")) == Hash(typeof(SettingsWindow).Assembly.Location),
            "the probe loads the exact timestamped candidate");
        string sandbox = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(sandbox);
        foreach (string variable in new[] { "APPDATA", "LOCALAPPDATA", "TEMP", "TMP" })
            Environment.SetEnvironmentVariable(variable, sandbox);
        Run();
        Console.WriteLine($"{_checks} off-screen settings search checks passed.");
    }

    private static void Run()
    {
        ConfigManager.DisableAutoStartSync = true;
        var config = ConfigManager.CurrentConfig;
        config.AutoCheckUpdate = false;
        config.ConfigMode = "Simple";
        config.EnableMultiTier = true;
        var app = new App();
        app.InitializeComponent();
        var window = new SettingsWindow();
        window.EnsureUiInitialized();
        Check(window.FindName("SettingsSearchPanel") is Border, "settings expose a single shared search panel");
        var panel = Find<Border>("SettingsSearchPanel");
        var input = Find<TextBox>("SettingsSearchInput");
        var results = Find<ListBox>("SettingsSearchResults");
        var status = Find<TextBlock>("SettingsSearchStatus");
        Check(panel.Visibility == Visibility.Collapsed, "search starts collapsed");
        var profile = config.Profiles[0];
        var layer = profile.GetActiveLayer();
        layer.MaxMenuDepth = 3;
        layer.FixedRings = new FixedRingMenu();
        layer.FixedRingsPaused = false;
        string before = JsonSerializer.Serialize(config);
        long revision = ConfigManager.ConfigurationRevision;

        Invoke("OpenSettingsSearch");
        Check(panel.Visibility == Visibility.Visible && status.Text == I18n.T("SettingsSearchPrompt"), "search opens with a localized prompt");
        input.Text = "no-settings-match-qx22";
        Check(results.Items.Count == 0 && status.Text == I18n.T("SettingsSearchEmpty"), "empty matches have visible feedback");

        foreach (var (language, query, expected) in new[]
        {
            (LanguageCode.ZhCn, "黑名单", "IsolationModeTitle"),
            (LanguageCode.ZhTw, "黑名單", "IsolationModeTitle"),
            (LanguageCode.En, "plugin download source", "SettingsSearchPluginSource"),
            (LanguageCode.Ja, "全画面", "TriggerSceneFullscreen")
        })
        {
            I18n.CurrentLanguage = language;
            Invoke("LocalizeSettingsSearch");
            input.Text = query;
            Check(TitleKey(results.SelectedItem) == expected, $"localized alias or exact title ranks first in {language}");
            Check((string)results.SelectedItem.GetType().GetProperty("Section")!.GetValue(results.SelectedItem)! != "",
                $"a visible page path accompanies the match in {language}");
        }

        I18n.CurrentLanguage = LanguageCode.ZhCn;
        Invoke("LocalizeSettingsSearch");
        Navigate("黑名单", "IsolationModeTitle", "IsolationModeTitleText");
        Check(Find<RadioButton>("TriggerRulesTab").IsChecked == true, "click path opens the application rules section");

        Navigate("蜂窝扇", "SubmenuStyleTitle", "Tab0_SubmenuStyleTitleText");
        Check(Find<RadioButton>("TriggerAdvancedTab").IsChecked == true, "submenu search opens advanced trigger settings");

        Navigate("轮盘 圆角", "SettingsSearchGeometry", "WheelAppearanceSidebar");
        Check(Find<RadioButton>("NavTab1").IsChecked == true, "geometry search activates appearance");

        Navigate("全部常驻", "FixedDisplay", "FixedDisplay");
        Check(Find<RadioButton>("NavTab2").IsChecked == true &&
            Find<Border>("FixedControlsCard").Visibility == Visibility.Visible, "fixed display result exposes its live inspector");

        Navigate("Windows 开机自启动", "StartupTitle", "StartupTitleText");
        Check(Find<RadioButton>("NavTab3").IsChecked == true, "startup result opens system settings");

        Navigate("OCR", "OcrCardTitle", "OcrCardTitleText");
        Check(Find<Border>("Tab3_OcrCardBorder").Visibility == Visibility.Visible && config.ConfigMode == "Simple",
            "a search can temporarily reveal a specific advanced setting without changing the mode");

        Navigate("更新源", "UpdateSourceTitle", "UpdateSourceComboBox");
        Check(Find<RadioButton>("NavTab4").IsChecked == true &&
            Find<Border>("Tab3_OcrCardBorder").Visibility == Visibility.Collapsed,
            "update source opens and the temporary advanced reveal is reset");

        Navigate("插件下载源", "SettingsSearchPluginSource", "PluginDownloadChannelComboBox");
        Check(Find<RadioButton>("NavTab5").IsChecked == true &&
            Find<RadioButton>("MarketTabButton").IsChecked == true, "plugin source selects the marketplace");

        Navigate("已安装插件", "SettingsSearchInstalledPlugins", "InstalledFilterCombo");
        Check(Find<RadioButton>("InstalledTabButton").IsChecked == true, "installed plugin result switches the plugin tab");

        Invoke("OpenSettingsSearch");
        input.Text = "轮盘";
        var content = (FrameworkElement)window.Content;
        var body = Find<Grid>("SettingsContentBody");
        var pageHost = Find<Grid>("SettingsPageHost");
        var zoom = Find<Grid>("RootScaleGrid").LayoutTransform as System.Windows.Media.ScaleTransform;
        zoom!.ScaleX = zoom.ScaleY = 2;
        var size = new Size(1060, 660);
        for (int pass = 0; pass < 2; pass++)
        {
            content.InvalidateMeasure();
            content.Measure(size);
            content.Arrange(new Rect(size));
            content.UpdateLayout();
        }
        Console.WriteLine($"Search layout {panel.ActualWidth}x{panel.ActualHeight}; page {pageHost.ActualHeight}; results {results.ActualHeight}");
        var close = Find<Button>("SettingsSearchClose");
        Check(panel.ActualWidth <= body.ActualWidth + 1 && panel.ActualHeight <= pageHost.ActualHeight + 1 &&
            input.ActualWidth >= 120 && close.ActualWidth == 30 && results.ActualHeight > 30,
            "search input, close button and results fit the narrow scaled layout");
        Invoke("CloseSettingsSearch", false);
        Check(panel.Visibility == Visibility.Collapsed, "search can close again");
        Check(JsonSerializer.Serialize(config) == before && ConfigManager.ConfigurationRevision == revision,
            "searching and navigating do not change or save settings");

        layer.MaxMenuDepth = 2;
        Navigate("全部常驻", "FixedDisplay", "MenuDepthPicker");
        Check(Find<Border>("FixedControlsCard").Visibility == Visibility.Collapsed &&
            layer.ActiveFixedRings != null && layer.EffectiveMaxMenuDepth == 2,
            "a two-level layer with saved fixed rings navigates to menu depth without enabling the fixed editor");

        Check(new WindowInteropHelper(window).Handle == nint.Zero && !PluginHost.IsInitialized,
            "the probe created no native window or plugin host");

        T Find<T>(string name) where T : FrameworkElement => (T)window.FindName(name)!;
        object? Invoke(string method, params object[] values) =>
            typeof(SettingsWindow).GetMethod(method, Private)!.Invoke(window, values);

        void Navigate(string query, string key, string controlName)
        {
            Invoke("OpenSettingsSearch");
            input.Text = query;
            var result = results.Items.Cast<object>().Single(item => TitleKey(item) == key);
            var target = result.GetType().GetProperty("Target")!.GetValue(result)!;
            var control = Find<FrameworkElement>(controlName);
            bool revealed = false;
            RequestBringIntoViewEventHandler onReveal = (_, e) => revealed |= ReferenceEquals(e.TargetObject, control);
            control.RequestBringIntoView += onReveal;
            Invoke("NavigateSettingsSearch", target);
            control.RequestBringIntoView -= onReveal;
            Check(revealed && panel.Visibility == Visibility.Collapsed, $"{key} scrolled to its real setting and closed search");
        }
    }

    private static string TitleKey(object result)
    {
        var target = result.GetType().GetProperty("Target")!.GetValue(result)!;
        return (string)target.GetType().GetProperty("TitleKey")!.GetValue(target)!;
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
        Console.WriteLine("PASS " + message);
    }
}
