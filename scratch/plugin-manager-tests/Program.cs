using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Threading;
using WinPieGestures;
using WinPieGestures.Plugins;

internal static class Program
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static int _checks;
    private static int _failures;
    private static readonly string Sandbox = Path.Combine(Path.GetTempPath(), "StarPie-PluginManagerTests-" + Guid.NewGuid().ToString("N"));

    [STAThread]
    private static int Main()
    {
        Environment.SetEnvironmentVariable("LOCALAPPDATA", Path.Combine(Sandbox, "app-data"));
        PluginPaths.OverrideRootsForTesting(Path.Combine(Sandbox, "plugins"), Path.Combine(Sandbox, "scan"));
        PluginHost.HeadlessMode = true;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        Run("catalog success", () => CatalogRefresh(false, false));
        Run("catalog failure", () => CatalogRefresh(true, false));
        Run("catalog failure retains cache but reports error", () => CatalogRefresh(true, true));
        Run("uninstall of unreleased runtime", UnreleasedUninstall);
        Run("uninstall of locked files", LockedUninstall);
        Run("uninstall success and missing plugin", NormalUninstall);
        Run("uninstall stop-result propagation", StopResults);
        Run("file lock classification", FileLockClassification);
        Run("visible restart handoff", VisibleRestart);
        Run("uninstall restart localization", Localization);
        Console.WriteLine($"Checks: {_checks}; failed scenarios: {_failures}. No network, windows, plugin code, or process launches.");
        return _failures == 0 ? 0 : 1;
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        _checks++;
    }

    private static void Run(string name, Action test)
    {
        try { test(); Console.WriteLine("PASS " + name); }
        catch (Exception ex) { _failures++; Console.WriteLine("FAIL " + name + ": " + ex.GetBaseException().Message); }
    }

    private static object? Field(object target, string name) => target.GetType().GetField(name, Fields)!.GetValue(target);
    private static void Set(object target, string name, object? value) => target.GetType().GetField(name, Fields)!.SetValue(target, value);
    private static bool RestartSuggested(PluginUninstallResult result)
        => typeof(PluginUninstallResult).GetProperty("RestartSuggested")?.GetValue(result) is true;

    private static void Complete(Task task)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(5) };
        var timeout = Stopwatch.StartNew();
        timer.Tick += (_, _) => { if (task.IsCompleted || timeout.Elapsed > TimeSpan.FromSeconds(10)) frame.Continue = false; };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        Check(task.IsCompleted, "bounded dispatcher pump completes");
        task.GetAwaiter().GetResult();
    }

    private static void CatalogRefresh(bool fail, bool cached)
    {
        // Deliberately bypass Window construction: only named controls are created, never shown.
        // The production refresh method runs with real async/finally and a fake HTTP transport.
        var window = (SettingsWindow)RuntimeHelpers.GetUninitializedObject(typeof(SettingsWindow));
        var status = new TextBlock();
        var button = new Button();
        // The fake transport serves the direct GitHub feed and catalog.
        var channel = new ComboBox();
        channel.Items.Add(new ComboBoxItem { Tag = OfficialPluginDownloadOptions.ChannelDirect });
        channel.SelectedIndex = 0;
        Set(window, "PluginDownloadChannelComboBox", channel);
        Set(window, "OfficialPluginsStatusText", status);
        Set(window, "RefreshOfficialPluginsButton", button);
        var oldCatalog = new OfficialPluginCatalog { SchemaVersion = 2, CatalogVersion = "old", ReleaseTag = "test" };
        if (cached)
        {
            Set(window, "_officialPluginCatalog", oldCatalog);
            Set(window, "_officialCatalogCachedAt", DateTimeOffset.UtcNow);
        }
        using var handler = new CatalogHandler(fail);
        OfficialPluginClient.HttpHandlerOverrideForTesting = handler;
        try
        {
            var method = typeof(SettingsWindow).GetMethod("RefreshOfficialPluginsAsync", Fields)!;
            var task = (Task)method.Invoke(window, null)!;
            Check(!task.IsCompleted && status.Text == I18n.T("PluginsOfficialLoading") && !button.IsEnabled,
                "refresh displays loading and disables button while request is pending");
            var duplicate = (Task)method.Invoke(window, null)!;
            Check(duplicate.IsCompletedSuccessfully && handler.Requests == 1, "reentrant refresh does not send another request");
            handler.Release.TrySetResult(true);
            Complete(task);
            Check(button.IsEnabled && Field(window, "_officialPluginsLoading") is false, "refresh clears busy state");
            Check(status.Text != I18n.T("PluginsOfficialLoading"), "completion replaces loading label without changing page");
            if (fail)
            {
                var error = (string)Field(window, "_officialPluginsError")!;
                Check(status.Text == I18n.TF("PluginsOfficialUnavailable", error), "failure is visible immediately, even with a cached catalog");
                if (cached) Check(ReferenceEquals(Field(window, "_officialPluginCatalog"), oldCatalog), "failed refresh preserves cached catalog");
            }
            else
            {
                var catalog = (OfficialPluginCatalog)Field(window, "_officialPluginCatalog")!;
                Check(catalog.CatalogVersion == "fresh" && status.Text.Contains("0"), "success shows fetched catalog status");
            }
        }
        finally { OfficialPluginClient.HttpHandlerOverrideForTesting = null; }
    }

    private sealed class CatalogHandler(bool fail) : HttpMessageHandler
    {
        public readonly TaskCompletionSource<bool> Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Requests;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Requests);
            await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (request.RequestUri!.AbsolutePath.EndsWith("releases.atom", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<feed xmlns='http://www.w3.org/2005/Atom'><entry><id>tag:github.com,2008:Repository/test</id><link href='https://github.com/Star-Pie/StarPie-Official-Plugins/releases/tag/test'/></entry></feed>") };
            if (request.RequestUri.AbsolutePath.EndsWith("module-catalog.json", StringComparison.Ordinal))
            {
                if (fail) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new OfficialPluginCatalog { SchemaVersion = 2, ReleaseTag = "test", CatalogVersion = "fresh" })) };
            }
            throw new InvalidOperationException("Unexpected HTTP request: " + request.RequestUri);
        }
    }

    private static PluginInstance Register(string suffix)
    {
        string id = "starpie.test." + suffix;
        var entry = new PluginRegistryEntry { Id = id, Name = suffix, InstallPath = id, Enabled = true };
        var instance = new PluginInstance(id, entry, new PluginScanResult());
        Directory.CreateDirectory(instance.ManagedDirectory);
        File.WriteAllText(Path.Combine(instance.ManagedDirectory, "payload.txt"), "sentinel");
        PluginRegistryStore.UpsertEntry(entry);
        var instances = (Dictionary<string, PluginInstance>)typeof(PluginHost).GetField("Instances", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        instances.Add(id, instance);
        return instance;
    }

    private static void UnreleasedUninstall()
    {
        var instance = Register("unreleased");
        instance.MarkStopPending("sentinel runtime still held");
        var result = PluginHost.UninstallAsync(instance.PluginId, true).GetAwaiter().GetResult();
        Check(!result.Success && Directory.Exists(instance.ManagedDirectory), "unreleased runtime does not delete files");
        Check(RestartSuggested(result), "unreleased runtime explicitly suggests restart");
        Check(!PluginRegistryStore.FindEntry(instance.PluginId)!.Enabled, "failed uninstall preserves disabled preference");
    }

    private static void LockedUninstall()
    {
        var instance = Register("locked");
        string file = Path.Combine(instance.ManagedDirectory, "payload.txt");
        using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = PluginHost.UninstallAsync(instance.PluginId, true).GetAwaiter().GetResult();
            Check(!result.Success && Directory.Exists(instance.ManagedDirectory), "locked uninstall retains registry and original directory");
            Check(RestartSuggested(result), "sharing violation explicitly suggests restart even after runtime fully stopped");
            Check(!PluginHost.IsRestartRequired(instance.PluginId), "file lock case cannot rely on runtime restart flag");
        }
        var retry = PluginHost.UninstallAsync(instance.PluginId, true).GetAwaiter().GetResult();
        Check(retry.Success && !RestartSuggested(retry) && PluginHost.Find(instance.PluginId) == null, "retry after releasing lock completes normally");
    }

    private static void NormalUninstall()
    {
        var instance = Register("normal");
        var result = PluginHost.UninstallAsync(instance.PluginId, true).GetAwaiter().GetResult();
        Check(result.Success && !RestartSuggested(result) && !Directory.Exists(instance.ManagedDirectory), "successful uninstall does not request restart");
        var missing = PluginHost.UninstallAsync("starpie.test.missing", true).GetAwaiter().GetResult();
        Check(!missing.Success && !RestartSuggested(missing), "missing plugin is not misclassified as restartable");
    }

    private static void StopResults()
    {
        var factory = typeof(PluginUninstallResult).GetMethod("FromStopFailure", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Check(factory != null, "uninstall exposes typed stop-failure conversion");
        foreach (var state in new[] { PluginStopStatus.Pending, PluginStopStatus.RequiresRestart, PluginStopStatus.Failed })
        {
            var stop = new PluginStopResult { Status = state, PluginId = "starpie.test.stop", Message = "sentinel" };
            var result = (PluginUninstallResult)factory!.Invoke(null, new object[] { stop })!;
            Check(!result.Success && result.Error.Contains(stop.Message) && RestartSuggested(result) == stop.RestartSuggested,
                "uninstall preserves stop error and restart classification: " + state);
        }
    }

    private static void FileLockClassification()
    {
        Check(PluginHost.IsFileInUse(new IOException("sharing", unchecked((int)0x80070020))), "sharing violation is restartable");
        Check(PluginHost.IsFileInUse(new IOException("lock", unchecked((int)0x80070021))), "lock violation is restartable");
        Check(!PluginHost.IsFileInUse(new UnauthorizedAccessException("permission")), "permission denial does not suggest restart");
        Check(!PluginHost.IsFileInUse(new IOException("disk", unchecked((int)0x80070070))), "disk full does not suggest restart");
    }

    private static void VisibleRestart()
    {
        var method = typeof(AppRestartCoordinator).GetMethod("CreateStartInfo", BindingFlags.Static | BindingFlags.NonPublic)!;
        var parameters = method.GetParameters();
        var start = (ProcessStartInfo)method.Invoke(null, parameters.Length == 4
            ? new object[] { @"C:\test folder\StarPie.exe", 123, 456L, true }
            : new object[] { @"C:\test folder\StarPie.exe", 123, 456L })!;
        Check(start.ArgumentList.SequenceEqual(new[] { AppRestartCoordinator.WaitArgument, "123", "456" }), "visible restart omits silent flag and keeps predecessor identity");
        Check(!start.UseShellExecute && start.CreateNoWindow && start.FileName == @"C:\test folder\StarPie.exe", "restart keeps direct executable launch, no console");
        Check(start.WindowStyle == ProcessWindowStyle.Normal, "visible restart must not force-hide the new UI window");
        var silent = (ProcessStartInfo)method.Invoke(null, parameters.Length == 4
            ? new object[] { "StarPie.exe", 123, 456L, false }
            : new object[] { "StarPie.exe", 123, 456L })!;
        Check(silent.ArgumentList.SequenceEqual(new[] { "--silent", AppRestartCoordinator.WaitArgument, "123", "456" }), "default restart retains existing silent semantics");
        Check(silent.WindowStyle == ProcessWindowStyle.Hidden, "silent restart retains hidden startup style");
        int waits = 0;
        Check(AppRestartCoordinator.WaitForPreviousProcess(start.ArgumentList.ToArray(), (pid, ticks) => { waits++; return pid == 123 && ticks == 456; }) && waits == 1,
            "visible restart still waits for the precise predecessor");
        Check(!AppRestartCoordinator.WaitForPreviousProcess(start.ArgumentList.ToArray(), (_, _) => false), "visible restart aborts on wait timeout");
        Check(!AppRestartCoordinator.WaitForPreviousProcess(start.ArgumentList.ToArray(), (_, _) => throw new IOException("sentinel")), "visible restart aborts on wait failure");
        Check(AppRestartCoordinator.WaitForPreviousProcess(Array.Empty<string>(), (_, _) => throw new Exception("must not wait")), "ordinary startup does not wait");
        foreach (var invalid in new[] { new[] { AppRestartCoordinator.WaitArgument }, new[] { AppRestartCoordinator.WaitArgument, "bad", "456" }, new[] { AppRestartCoordinator.WaitArgument, "123", "0" }, new[] { AppRestartCoordinator.WaitArgument, Environment.ProcessId.ToString(), "456" } })
            Check(!AppRestartCoordinator.WaitForPreviousProcess(invalid, (_, _) => throw new Exception("must not wait")), "invalid/self restart identity is rejected");
    }

    private static void Localization()
    {
        string previous = I18n.CurrentLanguageCode;
        try
        {
            foreach (string language in new[] { "zh-CN", "zh-TW", "en", "ja" })
            {
                I18n.SetLanguage(language);
                string prompt = I18n.TF("PluginsUninstallRestartPrompt", "PluginName", "DETAIL");
                Check(prompt.Contains("PluginName") && prompt.Contains("DETAIL") && !prompt.Contains("{0}") && !prompt.Contains("{1}"), "uninstall retry prompt localized: " + language);
                Check(I18n.TF("PluginsUninstallDirectoryDeleteFailed", "DETAIL").Contains("DETAIL"), "uninstall directory failure localized: " + language);
            }
        }
        finally { I18n.SetLanguage(previous); }
    }
}
