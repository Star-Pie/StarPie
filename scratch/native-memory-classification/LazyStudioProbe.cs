using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using WinPieGestures;
using WinPieGestures.ArtStyles;
using WinPieGestures.Plugins;

internal static class LazyStudioProbe
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        checks++;
        Console.WriteLine("PASS " + message);
    }

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var metadata = typeof(LazyStudioProbe).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .ToDictionary(a => a.Key, a => a.Value!);
            if (args.Length != 2 || args[1] != "--test-instance") throw new InvalidOperationException("Isolated mode required");
            string root = Path.GetFullPath(args[0]);
            string allowed = Path.GetFullPath(metadata["ArtifactsRoot"]).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Path.IsPathFullyQualified(args[0]) || !root.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || Directory.Exists(root))
                throw new InvalidOperationException("Fresh owned directory required");
            foreach (string key in new[] { "LOCALAPPDATA", "APPDATA", "TEMP", "TMP" })
            {
                string path = Path.Combine(root, key); Directory.CreateDirectory(path); Environment.SetEnvironmentVariable(key, path);
            }
            string loaded = typeof(SettingsWindow).Assembly.Location;
            string digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(loaded)));
            string expectedDigest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(metadata["StarPieCandidateDir"], "StarPie.dll"))));
            Check(digest == expectedDigest, "loaded exact candidate");
            var type = typeof(SettingsWindow);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var hostField = type.GetField("ThemeStudioHost", flags);
            if (hostField == null)
            {
                Console.Error.WriteLine("FAIL baseline eagerly instantiates ThemeStudio; deferred host absent");
                return 1;
            }
            var component = type.GetField("_themeStudioControl", flags)!;
            var change = type.GetMethod("ThemeStudioHost_IsVisibleChanged", flags)!;
            var reload = type.GetMethod("ReloadArtStyleStudio", flags)!;
            var released = type.GetField("_resourcesReleased", flags)!;
            var owner = (SettingsWindow)RuntimeHelpers.GetUninitializedObject(type);
            var host = new ContentControl(); hostField.SetValue(owner, host);
            _ = ConfigManager.CurrentConfig;
            ConfigManager.CurrentConfig.AppTheme = "Light";
            reload.Invoke(owner, null);
            Check(component.GetValue(owner) == null && host.Content == null, "reload before appearance keeps editor unconstructed");
            change.Invoke(owner, new object[] { host, new DependencyPropertyChangedEventArgs(UIElement.IsVisibleProperty, true, false) });
            Check(host.Content == null, "hidden transition does not construct editor");
            string selected = ArtStyleCatalog.GetAll(ConfigManager.CurrentConfig).Last().Id;
            ConfigManager.CurrentConfig.SelectedArtStyleId = selected;
            change.Invoke(owner, new object[] { host, new DependencyPropertyChangedEventArgs(UIElement.IsVisibleProperty, false, true) });
            var studio = host.Content as ThemeStudio;
            Check(studio != null && ReferenceEquals(component.GetValue(owner), studio), "first visible transition constructs actual editor");
            Check((string)typeof(ThemeStudio).GetField("selectedId", flags)!.GetValue(studio)! == selected, "deferred construction reads latest config");
            Check(studio!.FindName("ThemeCards") is ItemsControl cards && cards.Items.Count > 0, "actual constructor populates theme cards");
            change.Invoke(owner, new object[] { host, new DependencyPropertyChangedEventArgs(UIElement.IsVisibleProperty, false, true) });
            Check(ReferenceEquals(studio, host.Content), "reopening reuses existing editor");
            ConfigManager.CurrentConfig.SelectedArtStyleId = ArtStyleCatalog.DefaultId;
            reload.Invoke(owner, null);
            Check((string)typeof(ThemeStudio).GetField("selectedId", flags)!.GetValue(studio)! == ArtStyleCatalog.DefaultId, "existing editor reloads config");
            var closedOwner = (SettingsWindow)RuntimeHelpers.GetUninitializedObject(type);
            var closedHost = new ContentControl(); hostField.SetValue(closedOwner, closedHost); released.SetValue(closedOwner, true);
            change.Invoke(closedOwner, new object[] { closedHost, new DependencyPropertyChangedEventArgs(UIElement.IsVisibleProperty, false, true) });
            Check(closedHost.Content == null, "released owner cannot instantiate editor");
            var xml = XDocument.Load(Path.Combine(metadata["SourceRoot"], "WinPieGestures", "SettingsWindow.xaml"));
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            Check(xml.Descendants().Any(e => e.Name.LocalName == "ContentControl" && (string?)e.Attribute(x + "Name") == "ThemeStudioHost" && (string?)e.Attribute("IsVisibleChanged") == change.Name), "production XAML wires deferred visibility handler");
            Check(!xml.Descendants().Any(e => e.Name.LocalName == "ThemeStudio"), "production XAML has no eager editor element");
            Check(Application.Current == null && App.MainMouseHook == null && App.MainKeyboardHook == null && !PluginHost.IsInitialized, "no Application hooks or plugin host");
            Check(!studio.IsLoaded && PresentationSource.FromVisual(studio) == null, "actual editor remains offscreen");
            Console.WriteLine("RESULT " + JsonSerializer.Serialize(new { checks, CandidateDllSha256 = digest, LoadedAssembly = loaded, Boundary = "Uninitialized owner plus offscreen UserControl; no Window constructor, Application, Show, dispatcher pump, hooks, plugins or live target." }));
            GC.KeepAlive(owner);
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 2; }
    }
}
