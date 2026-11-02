using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Win32;

namespace WinPieGestures;

public enum AutoStartChangeStatus
{
    Success, Failed, Cancelled, TimedOut, VerificationFailed, NotEnabled, Suppressed
}

public sealed record AutoStartState(bool RegistryEnabled, bool AdminTaskEnabled, bool IsKnown)
{
    public bool IsEnabled => RegistryEnabled || AdminTaskEnabled;
}

public sealed record AutoStartChangeResult(AutoStartChangeStatus Status, AutoStartState State, int? ErrorCode = null)
{
    public bool IsSuccess => Status == AutoStartChangeStatus.Success;
}

internal sealed record AutoStartTaskQuery(bool Exists, bool IsKnown, string? Xml = null);
internal sealed record AutoStartCommandResult(AutoStartChangeStatus Status, int? ExitCode = null);

// All system writes stay behind this adapter; the orchestration is exercised with an in-memory fake.
internal interface IAutoStartSystem
{
    IReadOnlyDictionary<string, string> ReadRunValues();
    void WriteRunValue(string name, string command);
    void RemoveRunValue(string name);
    Task<AutoStartTaskQuery> QueryTaskAsync(CancellationToken cancellationToken);
    Task<AutoStartCommandResult> RegisterTaskAsync(string xml, CancellationToken cancellationToken);
    Task<AutoStartCommandResult> DeleteTaskAsync(CancellationToken cancellationToken);
}

internal sealed class AutoStartRegistration(IAutoStartSystem system, string executablePath, string userSid)
{
    internal const string TaskName = "StarPie_AdminAutoStart";
    internal const string Arguments = "--autostart --minimized";
    private static readonly string[] RunNames = ["StarPie", "WinPieGestures"];
    private readonly SemaphoreSlim _changes = new(1, 1);

    internal static string BuildTaskXml(string executablePath, string userSid)
    {
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        return new XDocument(new XDeclaration("1.0", "utf-16", null),
            new XElement(ns + "Task", new XAttribute("version", "1.2"),
                new XElement(ns + "Triggers", new XElement(ns + "LogonTrigger",
                    new XElement(ns + "Enabled", true), new XElement(ns + "UserId", userSid))),
                new XElement(ns + "Principals", new XElement(ns + "Principal", new XAttribute("id", "Author"),
                    new XElement(ns + "UserId", userSid), new XElement(ns + "LogonType", "InteractiveToken"),
                    new XElement(ns + "RunLevel", "HighestAvailable"))),
                new XElement(ns + "Settings",
                    new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(ns + "DisallowStartIfOnBatteries", false),
                    new XElement(ns + "StopIfGoingOnBatteries", false),
                    new XElement(ns + "RunOnlyIfNetworkAvailable", false),
                    new XElement(ns + "RunOnlyIfIdle", false),
                    new XElement(ns + "Enabled", true),
                    new XElement(ns + "ExecutionTimeLimit", "PT0S")),
                new XElement(ns + "Actions", new XAttribute("Context", "Author"), new XElement(ns + "Exec",
                    new XElement(ns + "Command", executablePath),
                    new XElement(ns + "Arguments", Arguments),
                    new XElement(ns + "WorkingDirectory", Path.GetDirectoryName(executablePath) ?? string.Empty)))))
            .ToString();
    }

    internal static bool MatchesTask(string? xml, string executablePath, string userSid)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(xml)) { return false; }
            XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
            var task = XDocument.Parse(xml).Root;
            var triggers = task?.Element(ns + "Triggers")?.Elements().ToArray();
            var principals = task?.Element(ns + "Principals")?.Elements().ToArray();
            var actions = task?.Element(ns + "Actions")?.Elements().ToArray();
            var settings = task?.Element(ns + "Settings");
            if (triggers?.Length != 1 || principals?.Length != 1 || actions?.Length != 1 || settings == null) { return false; }
            var trigger = triggers[0];
            var principal = principals[0];
            var action = actions[0];
            bool Is(XElement parent, string name, string value) => string.Equals(parent.Element(ns + name)?.Value, value, StringComparison.OrdinalIgnoreCase);
            return trigger.Name == ns + "LogonTrigger" && Is(trigger, "UserId", userSid) && Is(trigger, "Enabled", "true") &&
                (trigger.Element(ns + "Delay") == null || Is(trigger, "Delay", "PT0S")) &&
                Is(principal, "UserId", userSid) && Is(principal, "LogonType", "InteractiveToken") && Is(principal, "RunLevel", "HighestAvailable") &&
                action.Name == ns + "Exec" && Is(action, "Command", executablePath) && Is(action, "Arguments", Arguments) &&
                Is(action, "WorkingDirectory", Path.GetDirectoryName(executablePath) ?? string.Empty) &&
                Is(settings, "MultipleInstancesPolicy", "IgnoreNew") && Is(settings, "DisallowStartIfOnBatteries", "false") &&
                Is(settings, "StopIfGoingOnBatteries", "false") && Is(settings, "RunOnlyIfNetworkAvailable", "false") &&
                Is(settings, "RunOnlyIfIdle", "false") && Is(settings, "Enabled", "true") && Is(settings, "ExecutionTimeLimit", "PT0S");
        }
        catch (System.Xml.XmlException) { return false; }
    }

    internal async Task<AutoStartState> ReadStateAsync(CancellationToken cancellationToken = default)
    {
        bool registryEnabled = false;
        bool registryKnown = false;
        try
        {
            var values = system.ReadRunValues();
            registryEnabled = RunNames.Any(values.ContainsKey);
            registryKnown = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { }
        try
        {
            var task = await system.QueryTaskAsync(cancellationToken).ConfigureAwait(false);
            bool taskEnabled = task.Exists;
            if (task.Exists && task.Xml != null)
            {
                XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
                var enabled = XDocument.Parse(task.Xml).Root?.Element(ns + "Settings")?.Element(ns + "Enabled")?.Value;
                taskEnabled = !string.Equals(enabled, "false", StringComparison.OrdinalIgnoreCase);
            }
            return new AutoStartState(registryEnabled, taskEnabled, registryKnown && task.IsKnown);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return new(registryEnabled, false, false); }
    }

    internal async Task<AutoStartChangeResult> ChangeAsync(bool enable, bool asAdmin, bool repairOnly = false, CancellationToken cancellationToken = default)
    {
        try { await _changes.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { return new(AutoStartChangeStatus.Cancelled, new(false, false, false)); }
        try
        {
            var before = await ReadStateAsync(cancellationToken).ConfigureAwait(false);
            if (repairOnly && !before.IsKnown) { return new(AutoStartChangeStatus.VerificationFailed, before); }
            if (repairOnly && !before.IsEnabled) { return new(AutoStartChangeStatus.NotEnabled, before); }
            if (repairOnly)
            {
                // Repair the mechanism that is actually enabled, never stale preferences or a disabled task.
                enable = true;
                asAdmin = before.AdminTaskEnabled;
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (enable)
            {
                // Retain ordinary HKCU startup as fallback if elevated task registration is denied.
                system.WriteRunValue(RunNames[0], $"\"{executablePath}\" {Arguments}");
                system.RemoveRunValue(RunNames[1]);
            }
            else
            {
                foreach (var name in RunNames) { system.RemoveRunValue(name); }
            }

            AutoStartCommandResult command;
            if (enable && asAdmin)
            {
                command = await system.RegisterTaskAsync(BuildTaskXml(executablePath, userSid), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // Query failures must never be interpreted as absence (particularly access denied).
                var task = await system.QueryTaskAsync(cancellationToken).ConfigureAwait(false);
                if (!task.IsKnown) { return new(AutoStartChangeStatus.VerificationFailed, await ReadStateAsync().ConfigureAwait(false)); }
                command = task.Exists && !repairOnly ? await system.DeleteTaskAsync(cancellationToken).ConfigureAwait(false) : new(AutoStartChangeStatus.Success);
            }
            var state = await ReadStateAsync().ConfigureAwait(false);
            if (command.Status != AutoStartChangeStatus.Success) { return new(command.Status, state, command.ExitCode); }
            var runValues = system.ReadRunValues();
            bool registryMatches = enable
                ? runValues.TryGetValue(RunNames[0], out var actual) && actual == $"\"{executablePath}\" {Arguments}" && !runValues.ContainsKey(RunNames[1])
                : RunNames.All(name => !runValues.ContainsKey(name));
            var readback = await system.QueryTaskAsync(CancellationToken.None).ConfigureAwait(false);
            bool taskMatches = enable && asAdmin ? readback.Exists && MatchesTask(readback.Xml, executablePath, userSid) :
                repairOnly ? state.AdminTaskEnabled == before.AdminTaskEnabled : !readback.Exists;
            return new(state.IsKnown && registryMatches && readback.IsKnown && taskMatches ? AutoStartChangeStatus.Success : AutoStartChangeStatus.VerificationFailed, state);
        }
        catch (OperationCanceledException) { return new(AutoStartChangeStatus.Cancelled, await ReadStateAsync().ConfigureAwait(false)); }
        catch (Exception ex) { return new(AutoStartChangeStatus.Failed, await ReadStateAsync().ConfigureAwait(false), ex.HResult); }
        finally { _changes.Release(); }
    }
}

internal sealed class WindowsAutoStartSystem : IAutoStartSystem
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static readonly string SchedulerPath = Path.Combine(Environment.SystemDirectory, "schtasks.exe");
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);

    public IReadOnlyDictionary<string, string> ReadRunValues()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "StarPie", "WinPieGestures" })
        {
            if (key?.GetValue(name) is string value) { values[name] = value; }
        }
        return values;
    }

    public void WriteRunValue(string name, string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        key.SetValue(name, command);
    }

    public void RemoveRunValue(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    public async Task<AutoStartTaskQuery> QueryTaskAsync(CancellationToken cancellationToken)
    {
        var (result, xml) = await RunAsync($"/query /tn \"{AutoStartRegistration.TaskName}\" /xml", false, cancellationToken).ConfigureAwait(false);
        if (result.Status == AutoStartChangeStatus.Success) { return new(true, true, xml); }
        // schtasks localizes its error text; distinguish missing from denied through a read-only COM lookup.
        if (result.Status is AutoStartChangeStatus.Failed)
        {
            return await Task.Run(QueryMissingTask, CancellationToken.None).WaitAsync(CommandTimeout, cancellationToken).ConfigureAwait(false);
        }
        return new(false, false);
    }

    private static AutoStartTaskQuery QueryMissingTask()
    {
        object? scheduler = null;
        object? folder = null;
        object? task = null;
        try
        {
            var type = Type.GetTypeFromProgID("Schedule.Service");
            if (type == null) { return new(false, false); }
            scheduler = Activator.CreateInstance(type);
            ((dynamic)scheduler!).Connect();
            folder = ((dynamic)scheduler!).GetFolder(@"\");
            task = ((dynamic)folder).GetTask(AutoStartRegistration.TaskName);
            return new(true, false);
        }
        catch (COMException ex) when (ex.HResult == unchecked((int)0x80070002)) { return new(false, true); }
        catch { return new(false, false); }
        finally
        {
            foreach (var value in new[] { task, folder, scheduler })
            {
                if (value != null && Marshal.IsComObject(value)) { Marshal.ReleaseComObject(value); }
            }
        }
    }

    public async Task<AutoStartCommandResult> RegisterTaskAsync(string xml, CancellationToken cancellationToken)
    {
        string path = Path.Combine(Path.GetTempPath(), $"StarPie-autostart-{Guid.NewGuid():N}.xml");
        try
        {
            await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            await using (var writer = new StreamWriter(stream, Encoding.Unicode))
            {
                await writer.WriteAsync(xml.AsMemory(), cancellationToken).ConfigureAwait(false);
            }
            var (result, _) = await RunAsync($"/create /tn \"{AutoStartRegistration.TaskName}\" /xml \"{path}\" /f", true, cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally { File.Delete(path); }
    }

    public async Task<AutoStartCommandResult> DeleteTaskAsync(CancellationToken cancellationToken)
    {
        var (result, _) = await RunAsync($"/delete /tn \"{AutoStartRegistration.TaskName}\" /f", true, cancellationToken).ConfigureAwait(false);
        return result;
    }

    private static async Task<(AutoStartCommandResult Result, string Output)> RunAsync(string arguments, bool elevate, CancellationToken cancellationToken)
    {
        bool useShell = elevate && !IsElevated();
        var startInfo = new ProcessStartInfo(SchedulerPath, arguments)
        {
            UseShellExecute = useShell, Verb = useShell ? "runas" : string.Empty,
            CreateNoWindow = !useShell, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = !useShell, RedirectStandardError = !useShell
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CommandTimeout);
        Process? process = null;
        Task<Process?>? start = null;
        try
        {
            // ShellExecute may wait for UAC consent, so keep it off the settings Dispatcher too.
            start = Task.Run(() => Process.Start(startInfo), CancellationToken.None);
            process = await start.WaitAsync(timeout.Token).ConfigureAwait(false);
            if (process == null) { return (new(AutoStartChangeStatus.Failed), string.Empty); }
            Task<string> output = useShell ? Task.FromResult(string.Empty) : ReadOutputAsync(process.StandardOutput.BaseStream, timeout.Token);
            Task<string> error = useShell ? Task.FromResult(string.Empty) : process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            await error.ConfigureAwait(false);
            return (new(process.ExitCode == 0 ? AutoStartChangeStatus.Success : AutoStartChangeStatus.Failed, process.ExitCode), await output.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            if (process != null) { StopPendingProcess(process); }
            else if (start != null)
            {
                // A late UAC response must not produce an unobserved process or a false success.
                _ = start.ContinueWith(t => { if (t.Status == TaskStatus.RanToCompletion && t.Result != null) { StopPendingProcess(t.Result); t.Result.Dispose(); } _ = t.Exception; }, TaskScheduler.Default);
            }
            return (new(cancellationToken.IsCancellationRequested ? AutoStartChangeStatus.Cancelled : AutoStartChangeStatus.TimedOut), string.Empty);
        }
        catch (Win32Exception ex) { return (new(ex.NativeErrorCode == 1223 ? AutoStartChangeStatus.Cancelled : AutoStartChangeStatus.Failed, ex.NativeErrorCode), string.Empty); }
        catch (Exception ex) { return (new(AutoStartChangeStatus.Failed, ex.HResult), string.Empty); }
        finally { process?.Dispose(); }
    }

    private static void StopPendingProcess(Process process)
    {
        try { if (!process.HasExited) { process.Kill(); } }
        catch (InvalidOperationException) { }
        catch (Win32Exception) { }
    }

    private static async Task<string> ReadOutputAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        byte[] bytes = buffer.ToArray();
        // schtasks XML can be UTF-16 when redirected, including paths outside the active console codepage.
        Encoding encoding = bytes.Length >= 2 && (bytes[0] == 0xff && bytes[1] == 0xfe || bytes[0] == '<' && bytes[1] == 0)
            ? Encoding.Unicode : Encoding.UTF8;
        return encoding.GetString(bytes).TrimStart('\uFEFF');
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
