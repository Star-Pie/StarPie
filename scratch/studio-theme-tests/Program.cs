using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WinPieGestures;
using WinPieGestures.ArtStyles;
using WinPieGestures.Plugins;

internal static class Program
{
    private static int checks;
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        checks++;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        if (!args.Contains("--test-instance")) throw new ArgumentException("--test-instance required");
        var folder = Directory.CreateTempSubdirectory("starpie-studio-theme-");
        try
        {
            foreach (var key in new[] { "APPDATA", "LOCALAPPDATA", "TEMP", "TMP" })
                Environment.SetEnvironmentVariable(key, folder.FullName);
            Run(folder.FullName);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { folder.Delete(true); }
    }

    private static void Run(string data)
    {
        var candidate = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "CandidateDll").Value!;
        string loaded = typeof(SettingsWindow).Assembly.Location;
        static string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
        Check(Hash(loaded) == Hash(candidate), "loaded DLL differs from supplied candidate");
        Console.WriteLine($"Candidate {candidate}\nSHA256 {Hash(loaded)}");

        ConfigManager.DisableAutoStartSync = true;
        var config = ConfigManager.CurrentConfig;
        config.AutoCheckUpdate = false;
        config.ConfigMode = "Pro";
        var global = new WheelProfile { ProcessName = "Global", DisplayName = "Theme fixture" };
        global.EnsureLayers();
        config.Profiles = [global];
        var custom = new ArtStyleProfile { Id = "user-studio-theme-fixture", Name = "Custom color fixture", CornerRadius = 11 };
        custom.Colors.Surface = "#FFF1DD";
        custom.Colors.Text = "#292429";
        custom.Colors.Accent = "#773355";
        config.CustomArtStyles = [custom];

        var app = new App();
        using var hold = Dispatcher.CurrentDispatcher.DisableProcessing();
        app.InitializeComponent();
        long before = ConfigManager.ConfigurationRevision;
        static string[] JsonFiles(string root) => Directory.GetFiles(root, "*.json", SearchOption.AllDirectories)
            .OrderBy(p => p).Select(p => p + "|" + File.GetLastWriteTimeUtc(p).Ticks + "|" + Hash(p)).ToArray();
        var files = JsonFiles(data);
        var window = new SettingsWindow();
        window.SwitchToTab(2);
        T Get<T>(string name) where T : FrameworkElement => (T)window.FindName(name);
        Get<RadioButton>("MappingsViewModeCanvasRadio").IsChecked = true;
        var studio = Get<Grid>("MappingsCanvasModeGrid");
        var stage = Get<Border>("StudioStage");
        var rail = Get<MappingsProfileRail>("StudioProfileRail");
        var inspector = Get<Border>("StudioInspector");
        var dock = Get<ActionPaletteDock>("StudioPalette");
        rail.Refresh(config, global);
        var title = (TextBlock)rail.FindName("Title");
        var dockTitle = (TextBlock)dock.FindName("DockTitle");
        var entries = (Panel)dock.FindName("EntryPanel");
        var hotkey = entries.Children.OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetAutomationId(b) == "StudioPaletteHotkey");
        var area = new Size(1200, 760);
        var update = typeof(SettingsWindow).GetMethod("UpdateStudioLayout", BindingFlags.NonPublic | BindingFlags.Instance)!;
        void Arrange()
        {
            studio.Measure(area);
            studio.Arrange(new Rect(area));
            update.Invoke(window, null);
            studio.InvalidateMeasure();
            studio.Measure(area);
            studio.Arrange(new Rect(area));
            studio.UpdateLayout();
        }
        static Color ColorOf(Brush value) => ((SolidColorBrush)value).Color;
        static Color Parse(string value) => (Color)ColorConverter.ConvertFromString(value)!;
        static bool Placed(FrameworkElement element, Visual root)
        {
            if (element.RenderSize.Width < 8 || element.RenderSize.Height < 8) return false;
            for (DependencyObject? ancestor = element; ancestor != null; ancestor = VisualTreeHelper.GetParent(ancestor))
            {
                if (ancestor is UIElement item && item.Visibility != Visibility.Visible) return false;
                if (ReferenceEquals(ancestor, root)) return true;
            }
            return false;
        }
        int rows = 0;
        foreach (var theme in new[] { "System", "Light", "Dark", "TitaniumGray" })
        foreach (var id in new[] { "", "paper", "obsidian", "grove", "glacier", "pixel", custom.Id })
        {
            config.AppTheme = theme;
            config.SelectedArtStyleId = id;
            AppThemeManager.ApplyTheme(window, theme);
            Arrange();
            var art = id.Length == 0 ? null : ArtStyleCatalog.Find(config, id)!;
            var effective = AppThemeManager.CurrentEffectiveTheme;
            string label = theme + "/" + (art?.Id ?? "base");
            if (art == null && theme == "System") Check(effective is "Light" or "Dark", label + " system resolution");
            else Check(effective == (art == null ? theme : art.IsDark ? "Dark" : "Light"), label + " selected theme");
            var surface = Parse(art?.Colors.Surface ?? (effective switch { "Dark" => "#131C2E", "TitaniumGray" => "#202024", _ => "#FFFFFF" }));
            var ink = Parse(art?.Colors.Text ?? (effective switch { "Dark" => "#F8FAFC", "TitaniumGray" => "#F4F4F5", _ => "#0F172A" }));
            var accent = Parse(art?.Colors.Accent ?? (effective == "Light" ? "#2563EB" : "#3B82F6"));
            Check(ColorOf(stage.Background) == surface && ColorOf(inspector.Background) == surface, label + " stage/inspector surface actual=" + stage.Background);
            Check(ColorOf(title.Foreground) == ink && ColorOf(dockTitle.Foreground) == ink, label + " child text brush");
            Check(ColorOf((Brush)hotkey.FindResource("AccentPrimaryBrush")) == accent, label + " palette accent");
            if (art != null) Check(stage.CornerRadius.TopLeft == art.CornerRadius, label + " stage corner");
            foreach (var button in new[] { Get<Button>("StudioRailToggle"), Get<Button>("StudioInspectorToggle"), Get<Button>("StudioListToggle"), (Button)rail.FindName("Add"), hotkey })
                Check(Placed(button, studio) && button.Template != null && button.IsEnabled, label + " mapped button " + button.Name);
            Console.WriteLine($"PASS {label} effective={effective} surface={surface} ink={ink} accent={accent}");
            rows++;
        }
        Check(!window.IsVisible && new WindowInteropHelper(window).Handle == IntPtr.Zero, "window acquired a native handle");
        Check(!PluginHost.IsInitialized && App.MainMouseHook == null && App.MainKeyboardHook == null, "plugin host or hooks started");
        Check(before == ConfigManager.ConfigurationRevision && files.SequenceEqual(JsonFiles(data)), "config or plugin JSON changed");
        Console.WriteLine($"{rows} theme combinations, {checks} assertions passed; unshown WPF controls arranged in 1200 x 760 DIP Studio.");
    }
}
