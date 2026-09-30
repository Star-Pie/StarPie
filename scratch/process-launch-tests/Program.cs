using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using StarPie.Plugin;
using WinPieGestures;
using WinPieGestures.Plugins;

internal static class Program
{
    private static int _checks;
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); _checks++; }

    [STAThread]
    private static void Main()
    {
        // 所有日志都在隔离目录；不打开窗口，不启动程序，不弹 UAC。
        Environment.SetEnvironmentVariable("LOCALAPPDATA", Path.Combine(Path.GetTempPath(), "StarPie-ProcessLaunchTests-" + Guid.NewGuid().ToString("N")));
        Check(AppVersionInfo.DisplayVersion == "1.8.0-beta.4", "host release version remains beta.4");
        Check(SimpleVersion.TryParse("1.8.0-beta.4", out var oldHost) &&
            SimpleVersion.TryParse("1.8.0-beta.5", out _), "minimum host versions parse");
        SimpleVersion.TryParse("1.8.0-beta.5", out var minimum);
        Check(!SimpleVersion.SatisfiesMinimum(oldHost, minimum), "beta.4 cannot load beta.5 modules");
        Check(SimpleVersion.SatisfiesMinimum(minimum, minimum), "beta.5 accepts beta.5 modules");
        var info = new ProcessStartInfo("cmd.exe", "/c echo test")
        { UseShellExecute = false, WorkingDirectory = @"C:\test folder", CreateNoWindow = true };
        int processCalls = 0, shellCalls = 0;
        bool Process(ProcessStartInfo psi) { processCalls++; return true; }
        bool Shell(string file, string args, string directory, int show)
        { shellCalls++; Check(file == info.FileName && args == info.Arguments && directory == info.WorkingDirectory && show == 0, "standard user launch preserves arguments/cwd/hidden"); return true; }
        Check(ProcessLaunchExecutor.Start(info, ProcessLaunchMode.Default, Shell, psi => { Check(ReferenceEquals(psi, info), "default retains original startup settings"); return Process(psi); }), "default accepted");
        Check(ProcessLaunchExecutor.Start(info, ProcessLaunchMode.Administrator, Shell, psi =>
        {
            Check(psi.Verb == "runas" && psi.UseShellExecute && psi.WindowStyle == ProcessWindowStyle.Hidden, "administrator verb and hidden mode");
            Check(psi.Arguments == info.Arguments && psi.WorkingDirectory == info.WorkingDirectory, "administrator preserves args/cwd");
            return Process(psi);
        }), "administrator accepted");
        Check(ProcessLaunchExecutor.Start(info, ProcessLaunchMode.StandardUser, Shell, Process), "standard user accepted");
        Check(processCalls == 2 && shellCalls == 1, "exactly one backend per call");
        Check(info.Verb == "" && !info.UseShellExecute && info.CreateNoWindow, "request not mutated");
        Check(!ProcessLaunchExecutor.Start(info, ProcessLaunchMode.StandardUser, (_, _, _, _) => false, _ => throw new Exception("fallback occurred")), "failed standard-user launch does not fallback");
        Check(!ProcessLaunchExecutor.Start(info, ProcessLaunchMode.Administrator, (_, _, _, _) => throw new Exception("wrong backend"), _ => false), "failed administrator launch does not fallback");
        try { ProcessLaunchExecutor.Start(info, ProcessLaunchMode.Administrator, Shell, _ => throw new System.ComponentModel.Win32Exception(1223)); throw new Exception("cancel swallowed"); }
        catch (System.ComponentModel.Win32Exception ex) { Check(ex.NativeErrorCode == 1223, "cancellation propagates to service guard"); }
        try { ProcessLaunchExecutor.Start(info, (ProcessLaunchMode)99, Shell, Process); throw new Exception("invalid enum accepted"); }
        catch (ArgumentOutOfRangeException) { _checks++; }
        foreach (string verb in new[] { "cmd_here", "Windows.CmdHere", "powershell_here", "Windows.PowerShellHere", "windows_terminal", "Windows.Terminal", "git_bash_here", "Git.BashHere", "vscode_open", "VSCode.Open" })
            Check(ActionExecutor.SupportsShellToolLaunchMode(verb), "supported shell alias " + verb);
        foreach (string verb in new[] { "copy_path", "Windows.RunAs", "empty_recycle_bin", "unknown" })
        {
            Check(!ActionExecutor.SupportsShellToolLaunchMode(verb), "unsupported verb " + verb);
            try { ActionExecutor.ExecuteShellToolWithMode(verb, ProcessLaunchMode.Administrator); throw new Exception("unsupported verb executed"); }
            catch (NotSupportedException) { _checks++; }
        }
        foreach (ProcessLaunchMode mode in Enum.GetValues<ProcessLaunchMode>())
        {
            try { new PluginHostActionInvoker("test").LaunchWithMode("", mode); throw new Exception("launch gate missing"); }
            catch (PluginCapabilityDeniedException) { _checks++; }
            try { new PluginCommandService("test", PluginCapability.None).RunWithMode("", mode); throw new Exception("command gate missing"); }
            catch (PluginCapabilityDeniedException) { _checks++; }
            try { new PluginShellService("test", PluginCapability.None).InvokeWithMode("", mode); throw new Exception("shell gate missing"); }
            catch (PluginCapabilityDeniedException) { _checks++; }
        }
        Check(!new PluginHostActionInvoker("test", PluginCapability.Process).LaunchWithMode("", ProcessLaunchMode.StandardUser), "empty launch reports false");
        Check(!new PluginCommandService("test", PluginCapability.Process).RunWithMode("", ProcessLaunchMode.Administrator), "empty command reports false");
        Check(!new PluginShellService("test", PluginCapability.Process).InvokeWithMode("", ProcessLaunchMode.Administrator), "empty verb reports false");
        var fallback = new ParameterField
        {
            Key = "pluginOwnedMode", Type = ParameterFieldType.Enum, DefaultValue = "Default",
            FallbackParameterKey = HostActionFields.RunAsStandardUser,
            FallbackValueMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["true"] = "StandardUser", ["false"] = "Default" },
            Options = new[] { new ParameterOption { Value = "Default", Label = "Default" }, new ParameterOption { Value = "Administrator", Label = "Admin" }, new ParameterOption { Value = "StandardUser", Label = "Standard" } },
        };
        var action = new ActionItem { Type = "Launch", Parameter = "test.exe", RunAsStandardUser = true };
        var target = new ContributedActionParameterTarget(action);
        Check(PluginParameterForm.ResolveInitialValue(fallback, target.Stored) == "StandardUser", "legacy true is displayed correctly");
        target.Write(fallback.Key, "Default");
        Check(PluginParameterForm.ResolveInitialValue(fallback, target.Stored) == "Default", "explicit default overrides old flag");
        Check(!HostActionFields.All.Contains(fallback.Key), "private key not added to host fields");
        var roundtrip = JsonSerializer.Deserialize<ActionItem>(JsonSerializer.Serialize(action))!;
        Check(roundtrip.ExtensionData![fallback.Key] == "Default", "mode survives config serialization");
        _ = new Application();
        var panel = new StackPanel(); int changes = 0;
        var form = new PluginParameterForm(panel, () => target, () => changes++);
        form.Build(new[] { fallback }, null);
        Check(changes == 0, "opening form does not write config");
        var combo = FindCombo(panel)!;
        Check(combo.Items.Count == 3 && combo.SelectedItem != null, "UI exposes three choices");
        combo.SelectedIndex = 1;
        Check(action.ExtensionData![fallback.Key] == "Administrator" && changes == 1, "UI selection persists exactly one mode");
        form.Build(new[] { fallback }, null);
        Check(changes == 1 && FindCombo(panel)!.SelectedIndex == 1, "UI rebuild preserves selected mode without writes");
        Check(!new PluginHostActionInvoker("test", PluginCapability.Process).LaunchWithMode("app.exe", (ProcessLaunchMode)99), "invalid mode reaches bool guard and reports false");
        Check(!new PluginHostActionInvoker("test", PluginCapability.Process).LaunchWithMode("shell:AppsFolder\\example", ProcessLaunchMode.Administrator), "unsupported packaged admin launch does not fallback");
        var inherited = new ActionItem { Type = "Launch", RunAsStandardUser = true };
        var local = new ActionItem { Type = "Launch", RunAsStandardUser = true };
        var inheritedTarget = new ContributedActionParameterTarget(inherited, () => local);
        inheritedTarget.Write("pluginOwnedMode", "Administrator");
        Check(inherited.ExtensionData == null && local.ExtensionData!["pluginOwnedMode"] == "Administrator", "editing inherited action writes a local override");
        var extraPanel = new ContributedActionParameterPanel();
        extraPanel.SetAction(action);
        extraPanel.BuildRegisteredFields(new PluginActionRegistration { PluginId = "test", Parameters = new[] { fallback } });
        Check(extraPanel.HasFields && !extraPanel.UsesLegacyStandardUser && FindCombo(extraPanel)!.Items.Count == 3, "claimed-action UI renders extra plugin declaration");
        extraPanel.BuildRegisteredFields(new PluginActionRegistration { PluginId = "test", Parameters = new[] { fallback, new ParameterField { Key = HostActionFields.RunAsStandardUser, Type = ParameterFieldType.Bool } } });
        Check(extraPanel.UsesLegacyStandardUser, "legacy toggle is not hidden when plugin still declares it");
        extraPanel.BuildRegisteredFields(new PluginActionRegistration { PluginId = "test", Parameters = new[] { fallback, new ParameterField { Key = HostActionFields.Parameter, Type = ParameterFieldType.Text } } }, true);
        Check(extraPanel.HasFields && extraPanel.Children.Count == 2, "secondary plugin-action UI also renders full schema");
        form.Reset();
        Check(form.IsEmpty && form.Validate().Count == 0, "reset releases schema and clears validation");
        var noDirectory = new ProcessStartInfo("cmd.exe", "/c exit");
        ProcessLaunchExecutor.Start(noDirectory, ProcessLaunchMode.StandardUser,
            (_, _, directory, show) => { Check(directory == Environment.CurrentDirectory && show == 1, "standard launch preserves inherited working directory"); return true; }, _ => throw new Exception("fallback"));
        RunColdEditorIntegration();
        Console.WriteLine($"PASS: {_checks} host launch/SDK/UI checks; no processes or windows were started.");
    }

    private static void RunColdEditorIntegration()
    {
        string repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string official = Environment.GetEnvironmentVariable("STARPIE_OFFICIAL_PLUGIN_REPO") ??
            Path.GetFullPath(Path.Combine(repo, "..", "StarPie-Official-Plugins"));
        string[] types = { "Launch", "Command", "ShellTool" };
        if (types.Any(type => !File.Exists(Path.Combine(official, "src", "StarPie.Plugin." + type, "bin", "Release", "net8.0-windows", "StarPie.Plugin." + type + ".dll"))))
        { Console.WriteLine("SKIP: cold editor integration requires a Release build of the three official plugins."); return; }
        string root = Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA")!, "cold-editor");
        string scan = Path.Combine(root, "candidates");
        Directory.CreateDirectory(root); Directory.CreateDirectory(scan);
        PluginPaths.OverrideRootsForTesting(root, scan);
        var registry = new PluginRegistryFile();
        foreach (string type in types)
        {
            string id = "starpie.builtin." + type.ToLowerInvariant();
            string directory = Path.Combine(root, id);
            Directory.CreateDirectory(directory);
            string source = Path.Combine(official, "src", "StarPie.Plugin." + type);
            string fileName = "StarPie.Plugin." + type + ".dll";
            File.Copy(Path.Combine(source, "bin", "Release", "net8.0-windows", fileName), Path.Combine(directory, fileName));
            var productionManifest = JsonSerializer.Deserialize<PluginManifest>(
                File.ReadAllText(Path.Combine(source, "plugin.json")),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            Check(!PluginManifestReader.Validate(productionManifest, out PluginScanFailure failure, out _, true) &&
                failure == PluginScanFailure.HostVersionOutOfRange,
                "production beta.5 minimum still rejects beta.4 " + type);
            // 只调整隔离测试副本以测试参数渲染；生产清单和版本门禁保持不变。
            productionManifest.MinHostVersion = AppVersionInfo.DisplayVersion;
            File.WriteAllText(Path.Combine(directory, "plugin.json"), JsonSerializer.Serialize(productionManifest));
            registry.Entries.Add(new PluginRegistryEntry
            {
                Id = id, Name = type, Version = "1.1.0", InstallPath = id, Enabled = true, Preload = false, Official = true,
                CapabilitiesAck = new List<string> { "Process" },
                ClaimedTypes = new List<string> { type + "=" + (type == "ShellTool" ? "shellTool" : type.ToLowerInvariant()) },
                EntrySha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, fileName)))).ToLowerInvariant(),
            });
        }
        File.WriteAllText(PluginPaths.RegistryFile, JsonSerializer.Serialize(registry));
        PluginHost.HeadlessMode = true;
        PluginHost.Initialize();
        foreach (string type in types)
        {
            string id = "starpie.builtin." + type.ToLowerInvariant();
            string fullId = id + "." + (type == "ShellTool" ? "shellTool" : type.ToLowerInvariant());
            Check(!PluginHost.TryGetAction(fullId, out _), "cold startup does not preload " + type);
            var action = new ActionItem { Type = type, Parameter = "test", RunAsStandardUser = type == "Launch" };
            PluginHost.ValidateActionParameters(action);
            Check(!PluginHost.TryGetAction(fullId, out _), "validation does not load " + type);
            var editor = new ContributedActionParameterPanel();
            editor.SetAction(action);
            Check(editor.HasFields && FindCombo(editor)!.Items.Count == 3, "cold action editor loads three-state declaration " + type);
            Check(PluginHost.TryGetAction(fullId, out _), "editing activates only selected action " + type);
            var saved = PluginRegistryStore.FindEntry(id)!;
            Check(saved.Enabled && !saved.Preload, "editing preserves enable/preload preferences " + type);
            if (type == "Launch") Check(FindCombo(editor)!.SelectedIndex == 2, "actual plugin schema displays legacy standard-user selection");
            FindCombo(editor)!.SelectedIndex = 1;
            Check(action.ExtensionData!["launchMode"] == "Administrator", "actual UI writes only plugin extension value " + type);
            editor.SetAction(null);
        }
        Check(!PluginHost.TryGetClaimedEditorRegistration("UnknownType", out _), "unknown editor type does not load a plugin");
        PluginHost.ShutdownAll();
    }

    private static ComboBox? FindCombo(DependencyObject root)
    {
        if (root is ComboBox combo) return combo;
        foreach (object child in LogicalTreeHelper.GetChildren(root))
            if (child is DependencyObject node && FindCombo(node) is ComboBox found) return found;
        return null;
    }
}
