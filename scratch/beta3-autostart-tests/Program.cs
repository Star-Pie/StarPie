using System.Xml.Linq;
using WinPieGestures;

// This executable never creates WindowsAutoStartSystem. All registration operations use FakeSystem.
var cases = new List<(string Name, Func<Task> Run)>();
string executable = Path.Combine(Path.GetTempPath(), "StarPie tests & 中文", "StarPie.exe");
const string sid = "S-1-5-21-100-200-300-1001";
void Check(bool condition, string message) { if (!condition) { throw new InvalidOperationException(message); } }
AutoStartRegistration Service(FakeSystem system) => new(system, executable, sid);
void Test(string name, Func<Task> run) => cases.Add((name, run));

Test("XML preserves escaped path, SID, interactive desktop and battery policy", () =>
{
    string xml = AutoStartRegistration.BuildTaskXml(executable, sid);
    var root = XDocument.Parse(xml).Root!;
    XNamespace ns = root.Name.Namespace;
    Check(root.Element(ns + "Actions")!.Element(ns + "Exec")!.Element(ns + "Command")!.Value == executable, "Executable changed during XML escaping");
    Check(root.Element(ns + "Actions")!.Element(ns + "Exec")!.Element(ns + "WorkingDirectory")!.Value == Path.GetDirectoryName(executable), "Working directory lost");
    Check(AutoStartRegistration.MatchesTask(xml, executable, sid), "Generated XML does not meet registration contract");
    Check(!xml.Contains("Password") && !xml.Contains("ServiceAccount"), "Unexpected background/service identity");
    return Task.CompletedTask;
});

Test("Readback rejects each battery, time, identity and action mutation", () =>
{
    foreach (var (element, bad) in new[] {
        ("DisallowStartIfOnBatteries", "true"), ("StopIfGoingOnBatteries", "true"),
        ("ExecutionTimeLimit", "PT72H"), ("MultipleInstancesPolicy", "Parallel"),
        ("RunOnlyIfIdle", "true"), ("RunOnlyIfNetworkAvailable", "true"),
        ("RunLevel", "LeastPrivilege"), ("LogonType", "ServiceAccount"),
        ("UserId", "S-1-5-18"), ("Arguments", "--other"), ("Enabled", "false"),
        ("Command", executable + ".other"), ("WorkingDirectory", "C:\\Windows") })
    {
        var mutant = XDocument.Parse(AutoStartRegistration.BuildTaskXml(executable, sid));
        mutant.Descendants().First(node => node.Name.LocalName == element).Value = bad;
        Check(!AutoStartRegistration.MatchesTask(mutant.ToString(), executable, sid), $"Readback accepted {element}={bad}");
    }
    Check(!AutoStartRegistration.MatchesTask("<broken", executable, sid), "Malformed XML accepted");
    return Task.CompletedTask;
});

Test("Ordinary startup removes elevated task and migrates legacy Run alias", async () =>
{
    var fake = new FakeSystem { TaskXml = AutoStartRegistration.BuildTaskXml(executable, sid) };
    fake.Run["WinPieGestures"] = "old";
    var result = await Service(fake).ChangeAsync(true, false);
    Check(result.IsSuccess && result.State.RegistryEnabled && !result.State.AdminTaskEnabled, "Ordinary startup state wrong");
    Check(fake.Run["StarPie"] == $"\"{executable}\" --autostart --minimized" && !fake.Run.ContainsKey("WinPieGestures"), "Run migration wrong");
    Check(fake.Deletes == 1 && fake.Registrations == 0, "Ordinary startup registered elevated task");
});

Test("Elevated startup preserves registry fallback and verifies imported XML", async () =>
{
    var fake = new FakeSystem();
    var result = await Service(fake).ChangeAsync(true, true);
    Check(result.IsSuccess && result.State.RegistryEnabled && result.State.AdminTaskEnabled, "Elevated startup state wrong");
    Check(fake.Registrations == 1 && fake.TaskXml != null && AutoStartRegistration.MatchesTask(fake.TaskXml, executable, sid), "Task import contract wrong");
});

foreach (var failure in new[] { AutoStartChangeStatus.Failed, AutoStartChangeStatus.Cancelled, AutoStartChangeStatus.TimedOut })
{
    Test($"{failure} registration keeps fallback without false success", async () =>
    {
        var fake = new FakeSystem { CommandStatus = failure };
        var result = await Service(fake).ChangeAsync(true, true);
        Check(result.Status == failure && !result.IsSuccess && result.State.RegistryEnabled && !result.State.AdminTaskEnabled, "Failure promoted to success or fallback lost");
    });
}

Test("Exit zero with corrupted XML is verification failure", async () =>
{
    var fake = new FakeSystem { CorruptReadback = true };
    var result = await Service(fake).ChangeAsync(true, true);
    Check(result.Status == AutoStartChangeStatus.VerificationFailed && !result.IsSuccess, "Unverified task promoted to success");
});

Test("Registry write readback mismatch is verification failure", async () =>
{
    var fake = new FakeSystem { IgnoreRegistryWrite = true };
    var result = await Service(fake).ChangeAsync(true, false);
    Check(result.Status == AutoStartChangeStatus.VerificationFailed && !result.State.RegistryEnabled, "Ignored Run write became success");
});

Test("Exit zero with remaining task is verification failure", async () =>
{
    var fake = new FakeSystem { TaskXml = AutoStartRegistration.BuildTaskXml(executable, sid), IgnoreDelete = true };
    var result = await Service(fake).ChangeAsync(false, false);
    Check(result.Status == AutoStartChangeStatus.VerificationFailed && result.State.AdminTaskEnabled, "Task not removed became success");
});

Test("Disable removes own aliases and task, preserving unrelated Run values", async () =>
{
    var fake = new FakeSystem { TaskXml = AutoStartRegistration.BuildTaskXml(executable, sid) };
    fake.Run["StarPie"] = "old"; fake.Run["WinPieGestures"] = "legacy"; fake.Run["OtherProduct"] = "unrelated";
    var result = await Service(fake).ChangeAsync(false, false);
    Check(result.IsSuccess && !result.State.IsEnabled, "Disable did not verify removal");
    Check(fake.Run.Count == 1 && fake.Run["OtherProduct"] == "unrelated", "Unrelated value changed");
});

Test("Denied deletion reports remaining elevated task", async () =>
{
    var fake = new FakeSystem { TaskXml = AutoStartRegistration.BuildTaskXml(executable, sid), CommandStatus = AutoStartChangeStatus.Cancelled };
    fake.Run["StarPie"] = "old";
    var result = await Service(fake).ChangeAsync(false, false);
    Check(!result.IsSuccess && result.Status == AutoStartChangeStatus.Cancelled && result.State.AdminTaskEnabled && !result.State.RegistryEnabled, "Remaining task hidden");
});

Test("Unknown query is not mistaken for absent task", async () =>
{
    var fake = new FakeSystem { QueryKnown = false };
    var result = await Service(fake).ChangeAsync(false, false);
    Check(result.Status == AutoStartChangeStatus.VerificationFailed && !result.State.IsKnown && fake.Deletes == 0, "Unknown query became success");
});

Test("Registry failure does not attempt elevated registration", async () =>
{
    var fake = new FakeSystem { FailRegistryWrite = true };
    var result = await Service(fake).ChangeAsync(true, true);
    Check(result.Status == AutoStartChangeStatus.Failed && fake.Registrations == 0, "Registry failure bypassed");
});

Test("Repair disabled startup does not enable it", async () =>
{
    var fake = new FakeSystem();
    var result = await Service(fake).ChangeAsync(true, true, repairOnly: true);
    Check(result.Status == AutoStartChangeStatus.NotEnabled && fake.Writes == 0 && fake.Registrations == 0 && fake.Deletes == 0, "Repair enabled disabled startup");
});

Test("Repair disabled task without Run fallback does not enable it", async () =>
{
    var doc = XDocument.Parse(AutoStartRegistration.BuildTaskXml(executable, sid));
    doc.Root!.Element(doc.Root.Name.Namespace + "Settings")!.Element(doc.Root.Name.Namespace + "Enabled")!.Value = "false";
    var fake = new FakeSystem { TaskXml = doc.ToString() };
    var result = await Service(fake).ChangeAsync(true, true, repairOnly: true);
    Check(result.Status == AutoStartChangeStatus.NotEnabled && fake.Writes == 0 && fake.Registrations == 0, "Disabled task repaired into enabled state");
});

Test("Repair enabled legacy fallback refreshes current candidate without adding elevation", async () =>
{
    var fake = new FakeSystem(); fake.Run["WinPieGestures"] = "old candidate";
    var result = await Service(fake).ChangeAsync(true, true, repairOnly: true);
    Check(result.IsSuccess && fake.Registrations == 0 && fake.Run["StarPie"].Contains(executable), "Repair unexpectedly added elevation");
});

Test("Repair actual elevated task ignores stale false preference", async () =>
{
    var fake = new FakeSystem { TaskXml = AutoStartRegistration.BuildTaskXml(executable, sid) };
    var result = await Service(fake).ChangeAsync(true, false, repairOnly: true);
    Check(result.IsSuccess && fake.Registrations == 1 && fake.Deletes == 0 && result.State.AdminTaskEnabled, "Repair deleted existing elevated task");
});

Test("Repair Run fallback preserves deliberately disabled task", async () =>
{
    var doc = XDocument.Parse(AutoStartRegistration.BuildTaskXml(executable, sid));
    doc.Root!.Element(doc.Root.Name.Namespace + "Settings")!.Element(doc.Root.Name.Namespace + "Enabled")!.Value = "false";
    string disabledXml = doc.ToString();
    var fake = new FakeSystem { TaskXml = disabledXml };
    fake.Run["StarPie"] = "old candidate";
    var result = await Service(fake).ChangeAsync(true, true, repairOnly: true);
    Check(result.IsSuccess && fake.Registrations == 0 && fake.Deletes == 0 && fake.TaskXml == disabledXml && !result.State.AdminTaskEnabled, "Repair mutated a disabled task");
});

Test("Repair unknown state performs no writes", async () =>
{
    var fake = new FakeSystem { QueryKnown = false };
    var result = await Service(fake).ChangeAsync(true, true, repairOnly: true);
    Check(result.Status == AutoStartChangeStatus.VerificationFailed && fake.Writes == 0 && fake.Registrations == 0, "Unknown repair wrote state");
});

Test("Malformed existing task is unknown and cannot authorize repair", async () =>
{
    var fake = new FakeSystem { TaskXml = "<malformed" };
    var result = await Service(fake).ChangeAsync(true, true, repairOnly: true);
    Check(result.Status == AutoStartChangeStatus.VerificationFailed && !result.State.IsKnown && fake.Writes == 0, "Malformed task authorized repair");
});

Test("Registry read failure is unknown and cannot authorize repair", async () =>
{
    var fake = new FakeSystem { FailRegistryRead = true };
    var result = await Service(fake).ChangeAsync(true, true, repairOnly: true);
    Check(result.Status == AutoStartChangeStatus.VerificationFailed && !result.State.IsKnown && fake.Writes == 0, "Failed registry read authorized repair");
});

Test("Repeated state reads are side-effect free", async () =>
{
    var fake = new FakeSystem(); fake.Run["WinPieGestures"] = "legacy";
    var service = Service(fake);
    await service.ReadStateAsync(); await service.ReadStateAsync();
    Check(fake.Writes == 0 && fake.Deletes == 0 && fake.Registrations == 0 && fake.Run.ContainsKey("WinPieGestures"), "Reading state mutated registration");
});

Test("Pre-cancelled request performs no writes", async () =>
{
    var fake = new FakeSystem(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
    var result = await Service(fake).ChangeAsync(true, true, cancellationToken: cancellation.Token);
    Check(result.Status == AutoStartChangeStatus.Cancelled && fake.Writes == 0 && fake.Registrations == 0, "Cancelled operation wrote state");
});

Test("Pending registration remains awaitable until completion", async () =>
{
    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var fake = new FakeSystem { RegistrationGate = gate.Task };
    var pending = Service(fake).ChangeAsync(true, true);
    Check(!pending.IsCompleted, "Registration reported success before process completion");
    gate.SetResult();
    Check((await pending).IsSuccess, "Completed registration did not finish");
});

int failures = 0;
foreach (var test in cases)
{
    try { await test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception ex) { failures++; Console.WriteLine($"FAIL {test.Name}: {ex}"); }
}
Console.WriteLine($"{cases.Count - failures}/{cases.Count} passed; real registry/task/service writes: 0 (fake adapter only)");
return failures == 0 ? 0 : 1;

sealed class FakeSystem : IAutoStartSystem
{
    public Dictionary<string, string> Run { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string? TaskXml { get; set; }
    public bool QueryKnown { get; set; } = true;
    public bool CorruptReadback { get; set; }
    public bool FailRegistryWrite { get; set; }
    public bool FailRegistryRead { get; set; }
    public bool IgnoreRegistryWrite { get; set; }
    public bool IgnoreDelete { get; set; }
    public Task? RegistrationGate { get; set; }
    public AutoStartChangeStatus CommandStatus { get; set; } = AutoStartChangeStatus.Success;
    public int Writes { get; private set; }
    public int Registrations { get; private set; }
    public int Deletes { get; private set; }
    public IReadOnlyDictionary<string, string> ReadRunValues()
    {
        if (FailRegistryRead) { throw new UnauthorizedAccessException("fake read denial"); }
        return new Dictionary<string, string>(Run);
    }
    public void WriteRunValue(string name, string command)
    {
        if (FailRegistryWrite) { throw new UnauthorizedAccessException("fake denial"); }
        Writes++; if (!IgnoreRegistryWrite) { Run[name] = command; }
    }
    public void RemoveRunValue(string name) { Writes++; Run.Remove(name); }
    public Task<AutoStartTaskQuery> QueryTaskAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new AutoStartTaskQuery(TaskXml != null, QueryKnown, TaskXml));
    }
    public async Task<AutoStartCommandResult> RegisterTaskAsync(string xml, CancellationToken cancellationToken)
    {
        Registrations++;
        if (RegistrationGate != null) { await RegistrationGate.WaitAsync(cancellationToken); }
        if (CommandStatus == AutoStartChangeStatus.Success)
        {
            TaskXml = CorruptReadback ? xml.Replace("PT0S", "PT72H", StringComparison.Ordinal) : xml;
        }
        return new(CommandStatus, CommandStatus == AutoStartChangeStatus.Success ? 0 : 1);
    }
    public Task<AutoStartCommandResult> DeleteTaskAsync(CancellationToken cancellationToken)
    {
        Deletes++;
        if (CommandStatus == AutoStartChangeStatus.Success && !IgnoreDelete) { TaskXml = null; }
        return Task.FromResult(new AutoStartCommandResult(CommandStatus, CommandStatus == AutoStartChangeStatus.Success ? 0 : 1));
    }
}
