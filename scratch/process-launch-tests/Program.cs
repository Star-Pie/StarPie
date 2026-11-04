using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
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
    private static void Main(string[] args)
    {
        // 所有日志都在隔离目录；不打开窗口，不启动程序，不弹 UAC。
        Environment.SetEnvironmentVariable("LOCALAPPDATA", Path.Combine(AppContext.BaseDirectory, "test-env", Guid.NewGuid().ToString("N")));
        VerifyCandidateBinding();
        RunUrlRoutingRegressionTests();
        if (args.Contains("--urls-only"))
        {
            Console.WriteLine($"PASS: {_checks} candidate-bound URL routing checks; all launch/toggle backends were fakes.");
            return;
        }
        RunDefaultBrowserRegressionTests();
        Check(AppVersionInfo.DisplayVersion == "2.0.1-beta.4", "host release version matches the v2.0.1-beta.4 candidate");
        Check(SimpleVersion.TryParse("1.8.0-beta.4", out var oldHost) &&
            SimpleVersion.TryParse("1.8.0-beta.5", out _), "minimum host versions parse");
        SimpleVersion.TryParse(AppVersionInfo.DisplayVersion, out var currentHost);
        SimpleVersion.TryParse("1.8.0-beta.5", out var minimum);
        Check(!SimpleVersion.SatisfiesMinimum(oldHost, minimum), "beta.4 cannot load beta.5 modules");
        Check(SimpleVersion.SatisfiesMinimum(currentHost, minimum), "current host accepts beta.5 modules");
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
        RunPluginUpdateRestartChecks();
        RunWindowToggleRegressionTests();
        RunColdEditorIntegration();
        Console.WriteLine($"PASS: {_checks} host launch/SDK/UI/update-restart checks; no processes or windows were started.");
    }

    private static void VerifyCandidateBinding()
    {
        string? candidateDirectory = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "StarPieCandidateDir").Value;
        Check(!string.IsNullOrWhiteSpace(candidateDirectory), "test assembly declares an explicit candidate");
        foreach (Assembly assembly in new[] { typeof(ActionExecutor).Assembly, typeof(IPluginContext).Assembly })
        {
            string fileName = Path.GetFileName(assembly.Location);
            string expectedFile = Path.Combine(candidateDirectory!, fileName);
            Check(Path.GetFullPath(assembly.Location).Equals(Path.Combine(AppContext.BaseDirectory, fileName), StringComparison.OrdinalIgnoreCase),
                "assembly is loaded from this fresh test output: " + fileName);
            Check(File.Exists(expectedFile), "candidate file exists: " + expectedFile);
            string expectedHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(expectedFile)));
            string actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location)));
            Check(expectedHash == actualHash, "loaded assembly matches candidate SHA-256: " + fileName);
            Console.WriteLine($"CANDIDATE: {expectedFile}; SHA256={actualHash}");
        }
    }

    private static void RunUrlRoutingRegressionTests()
    {
        (string Input, string Target)[] urls =
        {
            ("https://example.test", "https://example.test"),
            ("https://example.test/", "https://example.test/"),
            ("http://example.test/", "http://example.test/"),
            ("HTTPS://Example.test/search?q=a%2Fb#part", "HTTPS://Example.test/search?q=a%2Fb#part"),
            ("https://example.test/tool!name", "https://example.test/tool!name"),
            ("https://example.test/tool.exe?mode=CAD!edit#panel", "https://example.test/tool.exe?mode=CAD!edit#panel"),
            ("https://example.test/绘图/零件?q=选择", "https://example.test/绘图/零件?q=选择"),
            ("  \"https://example.test/\"  ", "https://example.test/")
        };
        foreach ((string input, string target) in urls)
        foreach (ProcessLaunchMode mode in Enum.GetValues<ProcessLaunchMode>())
        foreach (string arguments in new[] { "", "--unchanged" })
        {
            int toggleCalls = 0, launchCalls = 0;
            bool result = ActionExecutor.ExecuteLaunchWithMode(input, arguments, mode,
                _ => { toggleCalls++; return LaunchWindowToggleResult.Minimized; },
                (start, actualMode) =>
                {
                    launchCalls++;
                    Check(start.FileName == target, "URL target is preserved: " + target);
                    Check(start.Arguments == arguments, "URL arguments are preserved");
                    Check(actualMode == mode, "URL explicit launch mode is preserved");
                    Check(start.UseShellExecute, "URL retains shell launch semantics");
                    return true;
                });
            Check(result && toggleCalls == 0 && launchCalls == 1,
                $"HTTP(S) bypasses window toggle and packaged-app inference: {mode}, {input}");
        }

        foreach (ProcessLaunchMode mode in Enum.GetValues<ProcessLaunchMode>())
        {
            int launchCalls = 0;
            bool result = ActionExecutor.ExecuteLaunchWithMode("https://example.test/", "", mode,
                _ => throw new Exception("URL failure must not consult window toggle"),
                (_, _) => { launchCalls++; return false; });
            Check(!result && launchCalls == 1, "URL backend failure is returned without retry/fallback: " + mode);
        }
        try
        {
            ActionExecutor.ExecuteLaunch("https://example.test/", "", runAsStandardUser: true,
                _ => throw new Exception("legacy URL must not toggle a window"), (_, _) => false);
            throw new Exception("legacy URL failure must propagate");
        }
        catch (InvalidOperationException)
        {
            _checks++;
        }
    }

    private static void RunDefaultBrowserRegressionTests()
    {
        AppConfig defaults = ConfigManager.CreateDefaultConfig();
        ActionItem browser = defaults.Profiles.Single(profile => profile.ProcessName == "Global")
            .Actions.Single(action => action.Parameter == "https://www.google.com");
        Check(browser.Type == "WebUrl", "fresh default browser uses WebUrl");
        Check(browser.SubActions.Count == 3 && browser.SubActions[0].Type == "Launch" && browser.SubActions[1].Type == "Launch",
            "default browser executable children retain Launch behavior");

        var legacy = new ActionItem
        {
            Type = "Launch", Parameter = "https://example.test/", RunAsStandardUser = true,
            ExtensionData = new Dictionary<string, string> { ["privateSetting"] = "keep" }
        };
        var roundtrip = JsonSerializer.Deserialize<ActionItem>(JsonSerializer.Serialize(legacy))!;
        Check(roundtrip.Type == legacy.Type && roundtrip.Parameter == legacy.Parameter && roundtrip.RunAsStandardUser &&
            roundtrip.ExtensionData!["privateSetting"] == "keep", "legacy URL action roundtrip preserves type, mode and unknown data");
    }

    private static void RunWindowToggleRegressionTests()
    {
        // 1. Floral scenario (baseline regression):
        // HWND 133528: visible=true, iconic=false, style=WS_POPUP|WS_VISIBLE|WS_CLIPSIBLINGS (0x94000000), exStyle=WS_EX_NOACTIVATE|WS_EX_LAYERED|WS_EX_TOOLWINDOW|WS_EX_TRANSPARENT (0x080800A0), title="com.floral-notepaper.app-siw", class="com.floral-notepaper.app-sic", isMainWindowHint=true
        // HWND 133526: visible=true, iconic=true, style=WS_OVERLAPPEDWINDOW|WS_VISIBLE|WS_MINIMIZE (0x34CF0000), exStyle=WS_EX_APPWINDOW|WS_EX_WINDOWEDGE|WS_EX_ACCEPTFILES (0x00040110), title="花笺", class="Tauri Window", isMainWindowHint=false
        var floralAuxiliary = new WindowSnapshot
        {
            Handle = (nint)133528,
            ProcessId = 16456,
            Title = "com.floral-notepaper.app-siw",
            ClassName = "com.floral-notepaper.app-sic",
            IsVisible = true,
            IsIconic = false,
            Owner = 0,
            Style = 0x94000000L,
            ExStyle = 0x080800A0L,
            Width = 100,
            Height = 100,
            IsForeground = false,
            IsMainWindowHint = true
        };
        var floralReal = new WindowSnapshot
        {
            Handle = (nint)133526,
            ProcessId = 16456,
            Title = "花笺",
            ClassName = "Tauri Window",
            IsVisible = true,
            IsIconic = true,
            Owner = 0,
            Style = 0x34CF0000L,
            ExStyle = 0x00040110L,
            Width = 800,
            Height = 600,
            IsForeground = false,
            IsMainWindowHint = false
        };

        // Verification 1: Candidate selection must reject auxiliary window and choose real window
        var selected = ProcessWindowSelector.SelectCandidate(new[] { floralAuxiliary, floralReal });
        Check(selected?.Handle == (nint)133526, "floral scenario must select real window 133526, not auxiliary window 133528");

        // Verification 2: Deterministic selection regardless of EnumWindows order
        var selectedReverse = ProcessWindowSelector.SelectCandidate(new[] { floralReal, floralAuxiliary });
        Check(selectedReverse?.Handle == (nint)133526, "selection must be deterministic regardless of enumeration order");

        // Verification 3: All auxiliary windows candidate -> returns null
        var onlyAux = ProcessWindowSelector.SelectCandidate(new[] { floralAuxiliary });
        Check(onlyAux == null, "process with only auxiliary windows must not be treated as activatable");

        // Verification 4: Normal window without title is still valid candidate
        var untitledNormal = new WindowSnapshot
        {
            Handle = (nint)200001,
            ProcessId = 5555,
            Title = "",
            ClassName = "CustomAppClass",
            IsVisible = true,
            IsIconic = false,
            Owner = 0,
            Style = 0x14CF0000L, // WS_OVERLAPPEDWINDOW | WS_VISIBLE
            ExStyle = 0x00040000L, // WS_EX_APPWINDOW
            Width = 500,
            Height = 400
        };
        Check(ProcessWindowSelector.SelectCandidate(new[] { untitledNormal })?.Handle == (nint)200001, "normal untitled window is accepted");

        // Verification 5: Toggle cycle:
        // Case 5a: Minimized (iconic) window -> restore and activate ("唤出")
        bool floralIsIconic = true;
        bool floralIsForeground = false;
        int restoreCalls = 0, activateCalls = 0, minimizeCalls = 0;
        var toggleRes1 = LaunchWindowToggle.Toggle(
            floralReal,
            h => floralIsForeground,
            h => floralIsIconic,
            h => { minimizeCalls++; floralIsIconic = true; floralIsForeground = false; return true; },
            h => { restoreCalls++; floralIsIconic = false; return true; },
            h => { activateCalls++; floralIsForeground = true; return true; });
        Check(toggleRes1 == LaunchWindowToggleResult.Activated && restoreCalls == 1 && activateCalls == 1 && minimizeCalls == 0,
            "minimized window is restored and activated");

        // Case 5b: Foreground normal window -> minimize ("收回")
        var floralForeground = new WindowSnapshot
        {
            Handle = (nint)133526,
            ProcessId = 16456,
            Title = "花笺",
            ClassName = "Tauri Window",
            IsVisible = true,
            IsIconic = false,
            Owner = 0,
            Style = 0x14CF0000L,
            ExStyle = 0x00040110L,
            IsForeground = true
        };
        bool floralFgIconic = false;
        bool floralFgForeground = true;
        restoreCalls = 0; activateCalls = 0; minimizeCalls = 0;
        var toggleRes2 = LaunchWindowToggle.Toggle(
            floralForeground,
            h => floralFgForeground,
            h => floralFgIconic,
            h => { minimizeCalls++; floralFgIconic = true; floralFgForeground = false; return true; },
            h => { restoreCalls++; floralFgIconic = false; return true; },
            h => { activateCalls++; floralFgForeground = true; return true; });
        Check(toggleRes2 == LaunchWindowToggleResult.Minimized && minimizeCalls == 1 && restoreCalls == 0 && activateCalls == 0,
            "foreground window is minimized (toggled back)");

        // Case 5c: Foreground window toggle again after minimized -> restores and activates
        restoreCalls = 0; activateCalls = 0; minimizeCalls = 0;
        var toggleRes3 = LaunchWindowToggle.Toggle(
            floralForeground,
            h => floralFgForeground,
            h => floralFgIconic,
            h => { minimizeCalls++; floralFgIconic = true; floralFgForeground = false; return true; },
            h => { restoreCalls++; floralFgIconic = false; return true; },
            h => { activateCalls++; floralFgForeground = true; return true; });
        Check(toggleRes3 == LaunchWindowToggleResult.Activated && restoreCalls == 1 && activateCalls == 1 && minimizeCalls == 0,
            "subsequent toggle restores and activates minimized window again");

        // Case 5d: Refused minimize (ShowWindow returns true for visible window, but isIconic remains false)
        int refusedMinimizeCalls = 0;
        var refusedMinWindow = new WindowSnapshot { Handle = (nint)101, ProcessId = 42, Title = "Refused", IsVisible = true, Width = 800, Height = 600 };
        var refusedMinRes = LaunchWindowToggle.Toggle(
            refusedMinWindow,
            h => true,  // foreground
            h => false, // remains not iconic!
            h => { refusedMinimizeCalls++; return true; }, // ShowWindow returns true because previously visible
            h => throw new Exception("restore called"),
            h => throw new Exception("activate called"));
        Check(refusedMinRes == LaunchWindowToggleResult.ActivationFailed && refusedMinimizeCalls == 1,
            "refused minimize where ShowWindow=true but iconic remains false must fail");

        // Case 5e: Refused restore (isIconic remains true after restore call)
        int refusedRestoreCalls = 0;
        var refusedRestoreRes = LaunchWindowToggle.Toggle(
            floralReal,
            h => false,
            h => true, // remains iconic
            h => false,
            h => { refusedRestoreCalls++; return true; },
            h => true);
        Check(refusedRestoreRes == LaunchWindowToggleResult.ActivationFailed && refusedRestoreCalls == 1,
            "refused restore where iconic remains true must fail");

        // Case 5f: Activation failure: candidate exists but activate returns false
        var toggleFail = LaunchWindowToggle.Toggle(
            floralReal,
            h => false,
            h => false,
            h => false,
            h => true,
            h => false); // activation fails
        Check(toggleFail == LaunchWindowToggleResult.ActivationFailed, "activation failure returns ActivationFailed");

        // Verification 6: Launch execution integration:
        // Case 6a: StandardUser with existing window -> toggles window, does NOT call Shell launch
        int launchCalls = 0;
        bool stdUserOk = ActionExecutor.ExecuteLaunchWithMode(
            "floral-notepaper.exe",
            "",
            ProcessLaunchMode.StandardUser,
            _ => LaunchWindowToggleResult.Activated,
            (psi, mode) => { launchCalls++; return true; });
        Check(stdUserOk && launchCalls == 0, "StandardUser with existing window toggles without launching new process");

        // Case 6b: Existing window activation fails -> returns false, does NOT launch duplicate process
        launchCalls = 0;
        bool failLaunch = ActionExecutor.ExecuteLaunchWithMode(
            "floral-notepaper.exe",
            "",
            ProcessLaunchMode.StandardUser,
            _ => LaunchWindowToggleResult.ActivationFailed,
            (psi, mode) => { launchCalls++; return true; });
        Check(!failLaunch && launchCalls == 0, "activation failure reports failure without duplicate process launch");

        // Case 6c: StandardUser without window -> launches via backend; if backend fails, does NOT fallback to Process.Start
        launchCalls = 0;
        bool stdUserFailedBackend = ActionExecutor.ExecuteLaunchWithMode(
            "floral-notepaper.exe",
            "",
            ProcessLaunchMode.StandardUser,
            _ => LaunchWindowToggleResult.NoWindow,
            (psi, mode) => { launchCalls++; return false; });
        Check(!stdUserFailedBackend && launchCalls == 1, "failed StandardUser launch backend does not fallback");

        // Case 6d: Administrator mode with existing window -> does NOT toggle window, calls launch with Administrator
        ProcessLaunchMode launchedMode = ProcessLaunchMode.Default;
        launchCalls = 0;
        bool adminOk = ActionExecutor.ExecuteLaunchWithMode(
            "floral-notepaper.exe",
            "",
            ProcessLaunchMode.Administrator,
            _ => throw new Exception("Administrator must not toggle window"),
            (psi, mode) => { launchCalls++; launchedMode = mode; return true; });
        Check(adminOk && launchCalls == 1 && launchedMode == ProcessLaunchMode.Administrator,
            "Administrator mode bypasses window toggle and launches elevated");

        // Case 6e: Non-empty arguments with existing window -> does NOT toggle window, calls launch
        launchCalls = 0;
        bool argsOk = ActionExecutor.ExecuteLaunchWithMode(
            "floral-notepaper.exe",
            "--new-window",
            ProcessLaunchMode.Default,
            _ => throw new Exception("Non-empty arguments must not toggle window"),
            (psi, mode) => { launchCalls++; return true; });
        Check(argsOk && launchCalls == 1, "non-empty arguments bypass window toggle and launch");

        // Case 6f: Special programs (cmd, calc, etc.) -> does NOT toggle window
        launchCalls = 0;
        bool specialOk = ActionExecutor.ExecuteLaunchWithMode(
            "calc.exe",
            "",
            ProcessLaunchMode.Default,
            _ => throw new Exception("Special program must not toggle window"),
            (psi, mode) => { launchCalls++; return true; });
        Check(specialOk && launchCalls == 1, "special programs bypass window toggle and launch");

        // Case 6g: Packaged application with administrator mode -> throws NotSupportedException
        try
        {
            ActionExecutor.ExecuteLaunchWithMode("shell:AppsFolder\\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", "", ProcessLaunchMode.Administrator);
            throw new Exception("UWP admin launch must throw NotSupportedException");
        }
        catch (NotSupportedException)
        {
            _checks++;
        }

        // Case 6h: Legacy ExecuteLaunch failure propagation
        try
        {
            ActionExecutor.ExecuteLaunch("fail.exe", "", runAsStandardUser: true,
                _ => LaunchWindowToggleResult.NoWindow,
                (psi, mode) => false);
            throw new Exception("failed legacy ExecuteLaunch must throw");
        }
        catch (InvalidOperationException)
        {
            _checks++;
        }

        try
        {
            ActionExecutor.ExecuteLaunch("fail.exe", "", runAsStandardUser: false,
                _ => LaunchWindowToggleResult.ActivationFailed,
                (psi, mode) => throw new Exception("must not launch"));
            throw new Exception("activation failure on legacy ExecuteLaunch must throw");
        }
        catch (InvalidOperationException)
        {
            _checks++;
        }

        ActionExecutor.ExecuteLaunch("ok.exe", "", runAsStandardUser: false,
            _ => LaunchWindowToggleResult.Activated,
            (psi, mode) => throw new Exception("must not launch on activated window"));
        _checks++;
    }

    private static void RunPluginUpdateRestartChecks()
    {
        foreach (PluginStopStatus status in Enum.GetValues<PluginStopStatus>())
        {
            var stop = new PluginStopResult { Status = status, PluginId = "test.update", Message = "held" };
            bool expected = status is PluginStopStatus.Pending or PluginStopStatus.RequiresRestart;
            Check(stop.RestartSuggested == expected, "restart is suggested only for unresolved stopping/unload " + status);
            if (!stop.IsFullyStopped)
            {
                PluginInstallResult failure = PluginInstallResult.FromStopFailure(stop);
                Check(!failure.Success && failure.PluginId == stop.PluginId && failure.Error.Contains(stop.Message) && failure.RestartSuggested == expected,
                    "installation retains stop reason and restart hint " + status);
                var official = OfficialPluginInstallResult.FromInstallResult(failure);
                Check(!official.Success && official.PluginId == failure.PluginId && official.Error == failure.Error && official.RestartSuggested == expected,
                    "official updater propagates restart hint " + status);
            }
        }
        Check(!OfficialPluginInstallResult.FromInstallResult(new PluginInstallResult { Error = "network/validation failure" }).RestartSuggested,
            "unrelated failures do not suggest restart");
        var installed = OfficialPluginInstallResult.FromInstallResult(new PluginInstallResult { Success = true, Enabled = true, PluginId = "test" });
        Check(installed.Success && installed.Enabled && !installed.RestartSuggested, "successful installation keeps existing result semantics");
        Check(PluginHost.IsFileInUse(new IOException("sharing", unchecked((int)0x80070020))), "sharing violation suggests restart");
        Check(PluginHost.IsFileInUse(new IOException("lock", unchecked((int)0x80070021))), "lock violation suggests restart");
        Check(!PluginHost.IsFileInUse(new IOException("disk full", unchecked((int)0x80070070))), "disk full does not suggest restart");
        Check(!PluginHost.IsFileInUse(new UnauthorizedAccessException("denied")), "permissions error does not suggest restart");

        string sandbox = Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA")!, "update-lock");
        string source = Path.Combine(sandbox, "source");
        string hostRoot = Path.Combine(sandbox, "plugin-data");
        string target = Path.Combine(hostRoot, "test.update");
        Directory.CreateDirectory(source); Directory.CreateDirectory(target);
        string sourceFile = Path.Combine(source, "test.dll");
        string targetFile = Path.Combine(target, "test.dll");
        File.WriteAllText(sourceFile, "new payload"); File.WriteAllText(targetFile, "old payload");
        string privateSettings = Path.Combine(target, "settings.json");
        File.WriteAllText(privateSettings, "private settings");
        var scan = new PluginScanResult
        {
            Accepted = true, ManifestSource = "Manifest", SourceDirectory = source, DllPath = sourceFile,
            Manifest = new PluginManifest { Id = "test.update", Name = "Test", Version = "1.0.1" },
        };
        PluginPaths.OverrideRootsForTesting(hostRoot, Path.Combine(sandbox, "scan"));
        using (var held = new FileStream(targetFile, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Check(!PluginHost.CopyPayload(scan, target, true, out string copyError, out bool restart) && restart && copyError.Length > 0,
                "actual destination sharing lock reaches copy failure hint");
            var result = PluginHost.CommitInstall(scan, new PluginInstallOptions { Acknowledged = true, OverwriteExisting = true });
            Check(!result.Success && result.RestartSuggested && result.PluginId == scan.Manifest.Id,
                "installation surfaces destination lock without reporting success");
            Check(File.ReadAllText(targetFile) == "old payload" && File.ReadAllText(privateSettings) == "private settings",
                "locked payload and private settings are not overwritten");
            var bare = new PluginScanResult { ManifestSource = "AssemblyMetadata", DllPath = sourceFile };
            Check(!PluginHost.CopyPayload(bare, target, true, out _, out bool bareRestart) && bareRestart,
                "bare DLL update also surfaces sharing lock");
        }
        Check(PluginHost.CopyPayload(scan, target, true, out _, out bool afterRelease) && !afterRelease && File.ReadAllText(targetFile) == "new payload",
            "manual retry after releasing lock succeeds without restart hint");
        Check(File.ReadAllText(privateSettings) == "private settings", "retry preserves plugin private settings");
        scan.SourceDirectory = Path.Combine(sandbox, "missing");
        Check(!PluginHost.CopyPayload(scan, target, true, out _, out bool missingRestart) && !missingRestart,
            "missing update source is not misclassified as file occupation");

        string previousLanguage = I18n.CurrentLanguageCode;
        foreach (string language in new[] { "zh-CN", "zh-TW", "en", "ja" })
        {
            I18n.SetLanguage(language);
            string prompt = I18n.TF("PluginsUpdateRestartPrompt", "TestPlugin", "DETAIL");
            Check(prompt.Contains("TestPlugin") && prompt.Contains("DETAIL") && !prompt.Contains("{0}") && !prompt.Contains("{1}"),
                "update restart prompt includes original failure and plugin name " + language);
            Check(I18n.T("PluginsRestartStartFailed") != "PluginsRestartStartFailed", "restart launch failure localized " + language);
        }
        I18n.SetLanguage(previousLanguage);

        int waitCalls = 0;
        bool Wait(int pid, long ticks) { waitCalls++; Check(pid == 123 && ticks == 456, "restart preserves parent identity"); return true; }
        Check(AppRestartCoordinator.WaitForPreviousProcess(new[] { "--silent" }, Wait) && waitCalls == 0, "normal startup never waits on another process");
        Check(AppRestartCoordinator.WaitForPreviousProcess(new[] { "--silent", AppRestartCoordinator.WaitArgument, "123", "456" }, Wait) && waitCalls == 1,
            "restart waits for predecessor before normal startup");
        Check(!AppRestartCoordinator.WaitForPreviousProcess(new[] { AppRestartCoordinator.WaitArgument, "123", "456" }, (_, _) => false),
            "restart timeout cancels new startup without killing predecessor");
        Check(!AppRestartCoordinator.WaitForPreviousProcess(new[] { AppRestartCoordinator.WaitArgument, "123", "456" }, (_, _) => throw new IOException("wait failure")),
            "restart wait failure cancels new startup");
        foreach (string[] invalid in new[] {
            new[] { AppRestartCoordinator.WaitArgument }, new[] { AppRestartCoordinator.WaitArgument, "bad", "456" },
            new[] { AppRestartCoordinator.WaitArgument, "0", "456" }, new[] { AppRestartCoordinator.WaitArgument, "123", "bad" },
            new[] { AppRestartCoordinator.WaitArgument, "123", "0" }, new[] { AppRestartCoordinator.WaitArgument, Environment.ProcessId.ToString(), "456" },
        }) Check(!AppRestartCoordinator.WaitForPreviousProcess(invalid, (_, _) => throw new Exception("must not wait")), "invalid restart arguments do not wait");
        var start = AppRestartCoordinator.CreateStartInfo(@"C:\test folder\StarPie.exe", 123, 456);
        Check(start.FileName == @"C:\test folder\StarPie.exe" && !start.UseShellExecute && start.CreateNoWindow,
            "restart launches same executable directly without shell quoting");
        Check(start.ArgumentList.SequenceEqual(new[] { "--silent", AppRestartCoordinator.WaitArgument, "123", "456" }),
            "restart passes separate silent/predecessor arguments without update resume task");
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
            Check(PluginManifestReader.Validate(productionManifest, out _, out _, true),
                "beta.5 accepts production minimum and SDK requirements " + type);
            // 使用原始生产清单的隔离副本，不放宽最低宿主版本或 SDK 门禁。
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
