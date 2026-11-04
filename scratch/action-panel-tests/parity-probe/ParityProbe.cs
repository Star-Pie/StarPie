using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using WinPieGestures;
using WinPieGestures.Plugins;
using StarPie.Plugin;

internal static class ParityProbe
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 3 || args[2] != "--test-instance") throw new ArgumentException("host-hash sandbox --test-instance required");
        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(AppConfig).Assembly.Location)));
        if (hash != args[0]) throw new InvalidOperationException("Candidate mismatch");
        foreach (string key in new[] { "LOCALAPPDATA", "APPDATA", "TEMP", "TMP" })
        { string path = Path.Combine(args[1], key); Directory.CreateDirectory(path); Environment.SetEnvironmentVariable(key, path); }
        ConfigManager.DisableAutoStartSync = true; ConfigManager.CurrentConfig = new AppConfig();
        _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var constructor = typeof(ActionPanelActionEditor).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        object?[] values = constructor.GetParameters().Select(_ => (object?)null).ToArray();
        values[0] = new ActionItem { Type = "Hotkey", Parameter = "Ctrl+C" };
        var window = (Window)constructor.Invoke(values);
        bool found = Descendants(window.Content).OfType<Button>().Any(button => button.Name == "TestPanelAction");
        window.Close();
        values[0] = new ActionItem { Type = "ShellTool", Parameter = "copy_path" };
        window = (Window)constructor.Invoke(values);
        var schema = new PluginActionRegistration { PluginId = "fixture.shell", Parameters = new ParameterField[]
        { new() { Key = HostActionFields.Parameter, Type = ParameterFieldType.Text, Label = "Function ID" } } };
        typeof(ActionPanelActionEditor).GetMethod("BuildClaimedFields", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [schema]);
        bool picker = Descendants(window.Content).OfType<Button>().Any(button => button.Name == "PanelPickShellTool");
        window.Close(); Application.Current.Shutdown();
        found &= picker;
        Console.WriteLine((found ? "PASS" : "FAIL") + " action editor exposes test-trigger and ShellTool picker commands; host=" + hash + "; no Show or input");
        return found ? 0 : 1;
    }
    private static IEnumerable<DependencyObject> Descendants(object node)
    {
        if (node is not DependencyObject item) yield break;
        yield return item;
        foreach (object child in LogicalTreeHelper.GetChildren(item)) foreach (var descendant in Descendants(child)) yield return descendant;
    }
}
