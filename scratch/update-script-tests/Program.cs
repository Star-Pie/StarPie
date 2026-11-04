using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

internal static class Program
{
    private static readonly List<string> FailureDetails = new();
    private static readonly List<CaseResult> CaseResults = new();
    private static readonly List<AdaptationRecord> AdaptationRecords = new();
    private static readonly List<string> UncoveredBoundaries = new();
    private static int _checks;
    private static int _failures;
    private static string? _skipReason;
    private static string _candidatePath = string.Empty;
    private static string _evidenceRoot = string.Empty;
    private static string _originalScript = string.Empty;
    private static string _adaptedScript = string.Empty;
    private static string _candidateSha256 = string.Empty;
    private static bool _scriptHasAcceptedProtocol;
    private static bool _scriptHasConfirmationProtocol;

    private const string FakeHelpers = """
function Write-TestRecord([string]$json) {
    $path = $env:STARPIE_TEST_START_LOG
    if ([string]::IsNullOrEmpty($path)) { throw 'Missing test record path.' }
    [IO.File]::AppendAllText($path, $json + [Environment]::NewLine)
}
function Fake-ShowMessageBox([string]$message, [string]$title) {
    $record = [ordered]@{ kind = 'messageBox'; message = $message; title = $title } |
        ConvertTo-Json -Compress -Depth 5
    Write-TestRecord $record
    return 0
}
function FakeStart-Process {
    param(
        [Parameter(Mandatory=$true)][string]$FilePath,
        [string]$ArgumentList,
        [string]$WorkingDirectory,
        [string]$WindowStyle,
        [switch]$PassThru
    )
    $mode = [string]$env:STARPIE_TEST_START_MODE
    if ([string]::IsNullOrEmpty($mode)) { $mode = 'running' }
    $requestPath = [IO.Path]::GetFullPath($env:STARPIE_TEST_REQUEST_PATH)
    $root = [IO.Path]::GetDirectoryName($requestPath)
    $backupRoot = Join-Path $root 'backup'
    $backupFiles = @()
    if ([IO.Directory]::Exists($backupRoot)) {
        $backupFiles = @(Get-ChildItem -LiteralPath $backupRoot -Recurse -File | ForEach-Object {
            [ordered]@{
                relative = $_.FullName.Substring($backupRoot.Length).TrimStart('\')
                length = $_.Length
                sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
            }
        })
    }
    $record = [ordered]@{
        kind = 'launch'
        filePath = $FilePath
        argumentList = $ArgumentList
        workingDirectory = $WorkingDirectory
        windowStyle = $WindowStyle
        pid = 424242
        mode = $mode
        backupFiles = $backupFiles
    } | ConvertTo-Json -Compress -Depth 8
    Write-TestRecord $record
    if ($mode -eq 'ready') {
        [IO.File]::WriteAllText((Join-Path $root 'ready'), [string]$env:STARPIE_TEST_READY_TOKEN)
    }
    $process = [pscustomobject]@{
        Id = 424242
        HasExited = ($mode -eq 'exited')
    }
    $process | Add-Member -MemberType ScriptMethod -Name Dispose -Value { }
    return $process
}
function Add-Type {
    throw 'Unexpected Add-Type invocation in adapted update script.'
}
""";

    private static int Main(string[] args)
    {
        DateTimeOffset startedUtc = DateTimeOffset.UtcNow;
        try
        {
            (string candidate, string output) = ParseArguments(args);
            _candidatePath = Path.GetFullPath(candidate);
            _evidenceRoot = Path.GetFullPath(output);
            Directory.CreateDirectory(_evidenceRoot);

            if (!File.Exists(_candidatePath))
                throw new FileNotFoundException("Candidate DLL was not found.", _candidatePath);

            _candidateSha256 = HashFile(_candidatePath);
            Assembly assembly = Assembly.LoadFrom(_candidatePath);
            Type scriptType = assembly.GetType("WinPieGestures.UpdateApplyScript", throwOnError: true)!;
            FieldInfo? contentField = scriptType.GetField(
                "Content",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (contentField is null || !contentField.IsLiteral)
                throw new InvalidOperationException("UpdateApplyScript.Content was not found as a literal field.");

            _originalScript = contentField.GetRawConstantValue() as string
                ?? throw new InvalidOperationException("UpdateApplyScript.Content is not a string.");
            _adaptedScript = AdaptScript(_originalScript);

            WriteText(Path.Combine(_evidenceRoot, "original-apply.ps1"), _originalScript, withBom: true);
            WriteText(Path.Combine(_evidenceRoot, "adapted-apply.ps1"), _adaptedScript, withBom: true);
            WriteJson(Path.Combine(_evidenceRoot, "adaptations.json"), new
            {
                testAdaptation = true,
                candidate = _candidatePath,
                candidateSha256 = _candidateSha256,
                originalScriptSha256 = HashText(_originalScript),
                adaptedScriptSha256 = HashText(_adaptedScript),
                scriptHasAcceptedProtocol = _scriptHasAcceptedProtocol,
                scriptHasConfirmationProtocol = _scriptHasConfirmationProtocol,
                replacements = AdaptationRecords,
            });

            RunAllTests();
            WriteSummary(startedUtc, fatal: null);
            Console.WriteLine($"RESULT checks={_checks} failures={_failures}");
            return _failures == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Fail($"fatal: {ex.GetType().Name}: {ex.Message}");
            try
            {
                if (!string.IsNullOrWhiteSpace(_evidenceRoot))
                    WriteSummary(startedUtc, ex);
            }
            catch
            {
                // Keep the original failure visible.
            }
            Console.Error.WriteLine(ex);
            Console.WriteLine($"RESULT checks={_checks} failures={_failures}");
            return 2;
        }
    }

    private static void RunAllTests()
    {
        RunCase("valid-payload-ready", RunValidPayloadTest);
        RunCase("missing-final-confirmation", RunMissingConfirmationTest);
        RunCase("abort-before-confirmation", RunAbortBeforeConfirmationTest);
        RunCase("payload-hash-mismatch", RunHashMismatchTest);
        RunCase("overwrite-failure-rollback", RunOverwriteFailureTest);
        RunCase("fake-child-exits-immediately", RunChildExitTest);
        RunCase("missing-ready-retains-backup", RunMissingReadyTest);
        RunCase("legacy-no-ready", RunLegacyTest);
        RunCase("path-escape-rejected", RunPathEscapeTest);
        RunCase("reparse-rejected", RunReparseTest);
        RunCase("pid-identity-guard", RunPidGuardTest);
    }

    private static void RunValidPayloadTest()
    {
        TestCaseContext context = CreateCase("valid", requireReady: true, mode: "ready", readyTimeout: 1.0);
        WriteTarget(context, "StarPie.exe", "old-exe");
        WriteTarget(context, "lib/dependency.dll", "old-dependency");
        WriteTarget(context, "config.json", "{\"unchanged\":true}");

        PayloadSpec[] payload =
        {
            new("StarPie.exe", Encoding.UTF8.GetBytes("new-exe")),
            new("lib/dependency.dll", Encoding.UTF8.GetBytes("new-dependency")),
        };
        StagePayload(context, payload);
        WriteRequest(context, payload);

        ScriptRun run = RunScript(context);
        Check(run.ExitCode == 0, $"valid payload exits 0 (actual {run.ExitCode})");
        Check(File.Exists(context.PreparedPath), "valid payload writes prepared marker");
        Check(ReadResult(context) == true, "valid payload reports success");
        Check(File.ReadAllText(Path.Combine(context.TargetDirectory, "StarPie.exe")) == "new-exe",
            "valid payload installs executable");
        Check(File.ReadAllText(Path.Combine(context.TargetDirectory, "lib", "dependency.dll")) == "new-dependency",
            "valid payload installs nested file");
        Check(File.ReadAllText(Path.Combine(context.TargetDirectory, "config.json")) == "{\"unchanged\":true}",
            "valid payload leaves unrelated config unchanged");
        Check(!Directory.Exists(context.PayloadDirectory), "valid payload removes payload directory");
        Check(!Directory.Exists(context.BackupDirectory), "valid payload removes backup directory");

        List<JsonElement> launches = ReadRecords(context.FakeEventLog)
            .Where(record => StringProperty(record, "kind") == "launch")
            .ToList();
        Check(launches.Count == 1, $"valid payload records one fake launch (actual {launches.Count})");
        if (launches.Count == 1)
        {
            string arguments = StringProperty(launches[0], "argumentList") ?? string.Empty;
            Check(arguments.Contains("--restart-after-exit", StringComparison.Ordinal),
                "valid launch preserves restart handoff arguments");
            Check(arguments.Contains(int.MaxValue.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal),
                "valid launch preserves the supplied previous PID");
            List<string> backupRelativePaths = BackupRelativePaths(launches[0]);
            Check(backupRelativePaths.Contains("StarPie.exe", StringComparer.OrdinalIgnoreCase),
                "valid payload backs up existing executable");
            Check(backupRelativePaths.Contains("lib\\dependency.dll", StringComparer.OrdinalIgnoreCase),
                "valid payload backs up existing nested file");
        }
    }

    private static void RunMissingConfirmationTest()
    {
        TestCaseContext context = CreateCase(
            "missing-confirmation",
            requireReady: true,
            mode: "ready",
            readyTimeout: 1.0,
            confirmTimeoutSeconds: 0.25,
            prewriteConfirmed: false);
        WriteTarget(context, "StarPie.exe", "old-exe");
        WriteTarget(context, "config.json", "{\"unchanged\":true}");
        PayloadSpec[] payload = { new("StarPie.exe", Encoding.UTF8.GetBytes("new-exe")) };
        StagePayload(context, payload);
        WriteRequest(context, payload);

        ScriptRun run = RunScript(context);
        Check(run.ExitCode == 1, $"missing confirmation exits 1 (actual {run.ExitCode})");
        Check(File.Exists(context.AcceptedPath), "missing confirmation writes accepted acknowledgement");
        Check(!File.Exists(context.ConfirmedPath), "missing confirmation does not receive confirmed acknowledgement");
        Check(File.ReadAllText(Path.Combine(context.TargetDirectory, "StarPie.exe")) == "old-exe",
            "missing confirmation leaves target unchanged");
        Check(File.ReadAllText(Path.Combine(context.TargetDirectory, "config.json")) == "{\"unchanged\":true}",
            "missing confirmation leaves config unchanged");
        Check(ReadResult(context) == false, "missing confirmation writes failure result");
        Check(!File.Exists(context.FakeEventLog) ||
              !ReadRecords(context.FakeEventLog).Any(record => StringProperty(record, "kind") == "launch"),
            "missing confirmation never launches a process");
        Check(!File.Exists(context.FakeEventLog) ||
              !ReadRecords(context.FakeEventLog).Any(record => StringProperty(record, "kind") == "messageBox"),
            "missing confirmation does not open a helper message box");
    }

    private static void RunAbortBeforeConfirmationTest()
    {
        TestCaseContext context = CreateCase(
            "abort-before-confirmation",
            requireReady: true,
            mode: "ready",
            readyTimeout: 1.0,
            confirmTimeoutSeconds: 1.0,
            prewriteConfirmed: false);
        WriteTarget(context, "StarPie.exe", "old-exe");
        WriteTarget(context, "config.json", "{\"unchanged\":true}");
        PayloadSpec[] payload = { new("StarPie.exe", Encoding.UTF8.GetBytes("new-exe")) };
        StagePayload(context, payload);
        WriteRequest(context, payload);

        ScriptRun run = RunScript(context, afterAccepted: testContext =>
            File.WriteAllText(testContext.AbortPath, "1", new UTF8Encoding(false)));
        Check(run.ExitCode == 1, $"abort before confirmation exits 1 (actual {run.ExitCode})");
        Check(File.Exists(context.AcceptedPath), "abort before confirmation follows the accepted acknowledgement");
        Check(File.Exists(context.AbortPath), "abort before confirmation records the abort request");
        Check(!File.Exists(context.ConfirmedPath), "abort before confirmation does not create confirmed");
        Check(File.ReadAllText(Path.Combine(context.TargetDirectory, "StarPie.exe")) == "old-exe",
            "abort before confirmation leaves target unchanged");
        Check(File.ReadAllText(Path.Combine(context.TargetDirectory, "config.json")) == "{\"unchanged\":true}",
            "abort before confirmation leaves config unchanged");
        Check(ReadResult(context) == false, "abort before confirmation writes failure result");
        Check(!File.Exists(context.FakeEventLog) ||
              !ReadRecords(context.FakeEventLog).Any(record => StringProperty(record, "kind") == "launch"),
            "abort before confirmation never launches a process");
        Check(!File.Exists(context.FakeEventLog) ||
              !ReadRecords(context.FakeEventLog).Any(record => StringProperty(record, "kind") == "messageBox"),
            "abort before confirmation does not open a helper message box");
    }

    private static void RunHashMismatchTest()
    {
        TestCaseContext context = CreateCase("hash-mismatch", requireReady: true, mode: "ready", readyTimeout: 1.0);
        WriteTarget(context, "StarPie.exe", "old-exe");
        WriteTarget(context, "config.json", "{\"unchanged\":true}");
        PayloadSpec[] payload = { new("StarPie.exe", Encoding.UTF8.GetBytes("new-exe")) };
        StagePayload(context, payload);
        WriteRequest(context, payload, hashOverride: "BADHASH");

        ScriptRun run = RunScript(context);
        Check(run.ExitCode == 1, $"hash mismatch exits 1 (actual {run.ExitCode})");
        Check(!File.Exists(context.PreparedPath), "hash mismatch fails before prepared marker");
        Check(File.ReadAllText(Path.Combine(context.TargetDirectory, "StarPie.exe")) == "old-exe",
            "hash mismatch leaves target unchanged");
        Check(File.ReadAllText(Path.Combine(context.TargetDirectory, "config.json")) == "{\"unchanged\":true}",
            "hash mismatch leaves config unchanged");
        Check(ReadResult(context) == false, "hash mismatch writes failure result");
        Check(!File.Exists(context.FakeEventLog) ||
              !ReadRecords(context.FakeEventLog).Any(record => StringProperty(record, "kind") == "launch"),
            "hash mismatch never launches a process");
    }

    private static void RunOverwriteFailureTest()
    {
        TestCaseContext context = CreateCase("overwrite-failure", requireReady: true, mode: "ready", readyTimeout: 1.0);
        WriteTarget(context, "first.dll", "old-first");
        Directory.CreateDirectory(Path.Combine(context.TargetDirectory, "second.dll"));
        WriteTarget(context, "config.json", "{\"unchanged\":true}");
        PayloadSpec[] payload =
        {
            new("first.dll", Encoding.UTF8.GetBytes("new-first")),
            new("second.dll", Encoding.UTF8.GetBytes("new-second")),
        };
        StagePayload(context, payload);
        WriteRequest(context, payload);

        ScriptRun run = RunScript(context);
        Check(run.ExitCode == 1, $"overwrite failure exits 1 (actual {run.ExitCode})");
        Check(File.Exists(context.PreparedPath), "overwrite failure reaches prepared marker");
        Check(File.ReadAllText(Path.Combine(context.TargetDirectory, "first.dll")) == "old-first",
            "overwrite failure restores the first target");
        Check(Directory.Exists(Path.Combine(context.TargetDirectory, "second.dll")),
            "overwrite failure leaves the failing target path intact");
        Check(File.ReadAllText(Path.Combine(context.TargetDirectory, "config.json")) == "{\"unchanged\":true}",
            "overwrite failure leaves config unchanged");
        Check(ReadResult(context) == false, "overwrite failure writes failure result");
        Check(!File.Exists(context.FakeEventLog) ||
              !ReadRecords(context.FakeEventLog).Any(record => StringProperty(record, "kind") == "launch"),
            "overwrite failure never launches a process");
    }

    private static void RunChildExitTest()
    {
        TestCaseContext context = CreateCase("child-exit", requireReady: true, mode: "exited", readyTimeout: 1.0);
        WriteTarget(context, "StarPie.exe", "old-exe");
        WriteTarget(context, "config.json", "{\"unchanged\":true}");
        PayloadSpec[] payload = { new("StarPie.exe", Encoding.UTF8.GetBytes("new-exe")) };
        StagePayload(context, payload);
        WriteRequest(context, payload);

        ScriptRun run = RunScript(context);
        Check(run.ExitCode == 1, $"immediate child exit exits 1 (actual {run.ExitCode})");
        Check(File.ReadAllText(Path.Combine(context.TargetDirectory, "StarPie.exe")) == "old-exe",
            "immediate child exit restores old executable");
        Check(File.ReadAllText(Path.Combine(context.TargetDirectory, "config.json")) == "{\"unchanged\":true}",
            "immediate child exit leaves config unchanged");
        Check(ReadResult(context) == false, "immediate child exit writes failure result");
        Check(ReadRecords(context.FakeEventLog).Any(record => StringProperty(record, "kind") == "launch"),
            "immediate child exit still records the attempted launch");
    }

    private static void RunMissingReadyTest()
    {
        TestCaseContext context = CreateCase("missing-ready", requireReady: true, mode: "running", readyTimeout: 0.35);
        WriteTarget(context, "StarPie.exe", "old-exe");
        WriteTarget(context, "config.json", "{\"unchanged\":true}");
        PayloadSpec[] payload = { new("StarPie.exe", Encoding.UTF8.GetBytes("new-exe")) };
        StagePayload(context, payload);
        WriteRequest(context, payload);

        ScriptRun run = RunScript(context);
        Check(run.ExitCode == 1, $"missing ready exits 1 (actual {run.ExitCode})");
        Check(File.ReadAllText(Path.Combine(context.TargetDirectory, "StarPie.exe")) == "new-exe",
            "missing ready retains the new executable");
        Check(File.Exists(Path.Combine(context.BackupDirectory, "StarPie.exe")),
            "missing ready retains the backup");
        Check(File.ReadAllText(Path.Combine(context.TargetDirectory, "config.json")) == "{\"unchanged\":true}",
            "missing ready leaves config unchanged");
        Check(ReadResult(context) == false, "missing ready writes failure result");
        Check(ReadRecords(context.FakeEventLog).Any(record => StringProperty(record, "kind") == "launch"),
            "missing ready records the fake launch");
    }

    private static void RunLegacyTest()
    {
        TestCaseContext context = CreateCase("legacy", requireReady: false, mode: "running", readyTimeout: 1.0);
        WriteTarget(context, "StarPie.exe", "old-exe");
        WriteTarget(context, "config.json", "{\"unchanged\":true}");
        PayloadSpec[] payload = { new("StarPie.exe", Encoding.UTF8.GetBytes("new-exe")) };
        StagePayload(context, payload);
        WriteRequest(context, payload, requireReady: false);

        ScriptRun run = RunScript(context);
        Check(run.ExitCode == 0, $"legacy no-ready exits 0 (actual {run.ExitCode})");
        Check(File.ReadAllText(Path.Combine(context.TargetDirectory, "StarPie.exe")) == "new-exe",
            "legacy no-ready installs the new executable");
        Check(File.ReadAllText(context.UpdateLog).Contains("Legacy version is running", StringComparison.OrdinalIgnoreCase),
            "legacy no-ready records the legacy no-readiness diagnostic");
        Check(ReadResult(context) == true, "legacy no-ready reports success");
        Check(!Directory.Exists(context.PayloadDirectory), "legacy no-ready removes payload directory");
        Check(!Directory.Exists(context.BackupDirectory), "legacy no-ready removes backup directory");
    }

    private static void RunPathEscapeTest()
    {
        TestCaseContext context = CreateCase("path-escape", requireReady: true, mode: "ready", readyTimeout: 1.0);
        WriteTarget(context, "config.json", "{\"unchanged\":true}");
        PayloadSpec[] payload = { new("../escape.txt", Encoding.UTF8.GetBytes("must-not-copy")) };
        StagePayload(context, payload);
        WriteRequest(context, payload);

        ScriptRun run = RunScript(context);
        Check(run.ExitCode == 1, $"path escape exits 1 (actual {run.ExitCode})");
        Check(!File.Exists(context.PreparedPath), "path escape fails before prepared marker");
        Check(File.ReadAllText(Path.Combine(context.TargetDirectory, "config.json")) == "{\"unchanged\":true}",
            "path escape leaves config unchanged");
        Check(ReadResult(context) == false, "path escape writes failure result");
        Check(!File.Exists(context.FakeEventLog) ||
              !ReadRecords(context.FakeEventLog).Any(record => StringProperty(record, "kind") == "launch"),
            "path escape never launches a process");
    }

    private static void RunReparseTest()
    {
        TestCaseContext context = CreateCase("reparse", requireReady: true, mode: "ready", readyTimeout: 1.0);
        string outside = Path.Combine(context.Root, "outside");
        string link = Path.Combine(context.TargetDirectory, "linked");
        Directory.CreateDirectory(outside);
        WriteTarget(context, "config.json", "{\"unchanged\":true}");
        File.WriteAllText(Path.Combine(outside, "sentinel.txt"), "outside-must-remain");
        if (!TryCreateReparsePoint(link, outside, out string reparseError))
        {
            string reason = $"reparse test skipped: BCL Directory.CreateSymbolicLink could not create a reparse point: {reparseError}";
            UncoveredBoundaries.Add(reason);
            Skip(reason);
            return;
        }

        PayloadSpec[] payload = { new("linked/payload.dll", Encoding.UTF8.GetBytes("new-payload")) };
        StagePayload(context, payload);
        WriteRequest(context, payload);

        ScriptRun run = RunScript(context);
        Check(run.ExitCode == 1, $"reparse path exits 1 (actual {run.ExitCode})");
        Check(!File.Exists(context.PreparedPath), "reparse path fails before prepared marker");
        Check(File.ReadAllText(Path.Combine(outside, "sentinel.txt")) == "outside-must-remain",
            "reparse target remains untouched");
        Check(!File.Exists(Path.Combine(outside, "payload.dll")),
            "reparse target does not receive payload");
        Check(ReadResult(context) == false, "reparse path writes failure result");
        Check(!File.Exists(context.FakeEventLog) ||
              !ReadRecords(context.FakeEventLog).Any(record => StringProperty(record, "kind") == "launch"),
            "reparse path never launches a process");
    }

    private static void RunPidGuardTest()
    {
        Check(!_originalScript.Contains("Stop-Process", StringComparison.OrdinalIgnoreCase),
            "extracted script contains no Stop-Process command");
        Check(!_originalScript.Contains("taskkill", StringComparison.OrdinalIgnoreCase),
            "extracted script contains no taskkill command");
        Check(!_originalScript.Contains("TerminateProcess", StringComparison.OrdinalIgnoreCase),
            "extracted script contains no TerminateProcess API");
        Check(!_originalScript.Contains(".Kill(", StringComparison.OrdinalIgnoreCase),
            "extracted script contains no Process.Kill call");

        TestCaseContext context = CreateCase("pid-guard", requireReady: true, mode: "ready", readyTimeout: 1.0);
        WriteTarget(context, "StarPie.exe", "old-exe");
        PayloadSpec[] payload = { new("StarPie.exe", Encoding.UTF8.GetBytes("new-exe")) };
        StagePayload(context, payload);
        WriteRequest(context, payload);
        ScriptRun run = RunScript(context);
        Check(run.ExitCode == 0, $"PID guard behavior exits 0 (actual {run.ExitCode})");
        Check(ReadResult(context) == true, "PID guard behavior reaches success");
        List<JsonElement> launches = ReadRecords(context.FakeEventLog)
            .Where(record => StringProperty(record, "kind") == "launch")
            .ToList();
        Check(launches.Count == 1, "PID guard behavior records one fake launch");
        if (launches.Count == 1)
            Check((StringProperty(launches[0], "argumentList") ?? string.Empty)
                    .Contains(int.MaxValue.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal),
                "PID guard passes the configured previous PID through to restart arguments");
    }

    private static TestCaseContext CreateCase(string name, bool requireReady, string mode, double readyTimeout,
        double confirmTimeoutSeconds = 0.5, bool prewriteConfirmed = true)
    {
        string root = Path.Combine(
            _evidenceRoot,
            "runs",
            name + "-" + Guid.NewGuid().ToString("N"));
        string payload = Path.Combine(root, "payload");
        string backup = Path.Combine(root, "backup");
        string target = Path.Combine(root, "target");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(payload);
        Directory.CreateDirectory(backup);
        Directory.CreateDirectory(target);
        string script = Path.Combine(root, "apply.ps1");
        WriteText(script, _adaptedScript, withBom: true);
        return new TestCaseContext
        {
            Root = root,
            Token = Path.GetFileName(root),
            PayloadDirectory = payload,
            BackupDirectory = backup,
            TargetDirectory = target,
            RequestPath = Path.Combine(root, "request.json"),
            AdaptedScriptPath = script,
            PreparedPath = Path.Combine(root, "prepared"),
            ProceedPath = Path.Combine(root, "proceed"),
            AcceptedPath = Path.Combine(root, "accepted"),
            ConfirmedPath = Path.Combine(root, "confirmed"),
            AbortPath = Path.Combine(root, "abort"),
            ResultPath = Path.Combine(root, "result.json"),
            UpdateLog = Path.Combine(root, "update.log"),
            FakeEventLog = Path.Combine(root, "fake-events.log"),
            RequireReady = requireReady,
            Mode = mode,
            ReadyTimeoutSeconds = readyTimeout,
            ConfirmTimeoutSeconds = confirmTimeoutSeconds,
            PrewriteConfirmed = prewriteConfirmed,
        };
    }

    private static void StagePayload(TestCaseContext context, IEnumerable<PayloadSpec> files)
    {
        foreach (PayloadSpec file in files)
        {
            string path = Path.GetFullPath(Path.Combine(context.PayloadDirectory, file.RelativePath));
            string? parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);
            File.WriteAllBytes(path, file.Content);
        }
    }

    private static void WriteTarget(TestCaseContext context, string relativePath, string content)
    {
        string path = Path.GetFullPath(Path.Combine(context.TargetDirectory, relativePath));
        string? parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    private static void WriteRequest(TestCaseContext context, IReadOnlyList<PayloadSpec> files,
        string? hashOverride = null, bool? requireReady = null)
    {
        var manifest = files.Select(file => new
        {
            RelativePath = file.RelativePath,
            Length = (long)file.Content.Length,
            Sha256 = hashOverride ?? HashBytes(file.Content),
        }).ToArray();
        var request = new
        {
            Token = context.Token,
            PreviousPid = int.MaxValue,
            PreviousStartTicks = 0L,
            TargetDirectory = context.TargetDirectory,
            Executable = Path.Combine(context.TargetDirectory, "StarPie.exe"),
            PayloadDirectory = context.PayloadDirectory,
            BackupDirectory = context.BackupDirectory,
            Files = manifest,
            RequireReadySignal = requireReady ?? context.RequireReady,
            LogFile = context.UpdateLog,
            FailureMessage = "synthetic update failure",
        };
        WriteText(context.RequestPath, JsonSerializer.Serialize(request), withBom: false);
    }

    private static ScriptRun RunScript(TestCaseContext context, Action<TestCaseContext>? afterAccepted = null)
    {
        File.WriteAllText(context.ProceedPath, "1", new UTF8Encoding(false));
        if (context.PrewriteConfirmed)
            File.WriteAllText(context.ConfirmedPath, context.Token, new UTF8Encoding(false));
        string powershell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        if (!File.Exists(powershell))
            throw new FileNotFoundException("Windows PowerShell was not found.", powershell);

        var startInfo = new ProcessStartInfo
        {
            FileName = powershell,
            WorkingDirectory = context.Root,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in new[]
                 {
                     "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden",
                     "-File", context.AdaptedScriptPath, "-RequestPath", context.RequestPath,
                 })
            startInfo.ArgumentList.Add(argument);
        startInfo.Environment["STARPIE_TEST_REQUEST_PATH"] = context.RequestPath;
        startInfo.Environment["STARPIE_TEST_START_MODE"] = context.Mode;
        startInfo.Environment["STARPIE_TEST_START_LOG"] = context.FakeEventLog;
        startInfo.Environment["STARPIE_TEST_READY_TOKEN"] = context.Token;
        startInfo.Environment["STARPIE_TEST_READY_TIMEOUT"] =
            context.ReadyTimeoutSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        startInfo.Environment["STARPIE_TEST_LEGACY_TIMEOUT"] =
            context.ReadyTimeoutSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        startInfo.Environment["STARPIE_TEST_CONFIRM_TIMEOUT"] =
            context.ConfirmTimeoutSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        startInfo.Environment["PSModulePath"] = WindowsPowerShellModulePath();
        WriteText(
            Path.Combine(context.Root, "harness-environment.txt"),
            "PSModulePath=" + startInfo.Environment["PSModulePath"],
            withBom: false);

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        if (afterAccepted is not null)
        {
            var acceptedWait = Stopwatch.StartNew();
            while (!File.Exists(context.AcceptedPath) &&
                   !process.HasExited &&
                   acceptedWait.Elapsed < TimeSpan.FromSeconds(5))
                Thread.Sleep(20);
            if (File.Exists(context.AcceptedPath))
                afterAccepted(context);
        }
        bool exited = process.WaitForExit(15000);
        if (!exited)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }
        string output = stdout.GetAwaiter().GetResult();
        string error = stderr.GetAwaiter().GetResult();
        WriteText(Path.Combine(context.Root, "stdout.log"), output, withBom: false);
        WriteText(Path.Combine(context.Root, "stderr.log"), error, withBom: false);
        if (!exited)
            Check(false, "script process exceeded the 15 second harness timeout");
        return new ScriptRun
        {
            ExitCode = exited ? process.ExitCode : -1,
            Stdout = output,
            Stderr = error,
            TimedOut = !exited,
        };
    }

    private static string WindowsPowerShellModulePath()
    {
        string[] candidates =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "WindowsPowerShell", "Modules"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "WindowsPowerShell", "Modules"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32", "WindowsPowerShell", "v1.0", "Modules"),
        };
        string[] existing = candidates.Where(Directory.Exists).ToArray();
        return existing.Length == 0
            ? Environment.GetEnvironmentVariable("PSModulePath") ?? string.Empty
            : string.Join(';', existing);
    }

    private static bool? ReadResult(TestCaseContext context)
    {
        if (!File.Exists(context.ResultPath))
        {
            Check(false, "result.json exists");
            return null;
        }
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(context.ResultPath));
            return document.RootElement.GetProperty("Success").GetBoolean();
        }
        catch (Exception ex)
        {
            Check(false, $"result.json is valid: {ex.Message}");
            return null;
        }
    }

    private static List<JsonElement> ReadRecords(string path)
    {
        var records = new List<JsonElement>();
        if (!File.Exists(path))
            return records;
        foreach (string line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            try
            {
                using JsonDocument document = JsonDocument.Parse(line);
                records.Add(document.RootElement.Clone());
            }
            catch (Exception ex)
            {
                Check(false, $"fake event record is valid JSON: {ex.Message}");
            }
        }
        return records;
    }

    private static string? StringProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static List<string> BackupRelativePaths(JsonElement launch)
    {
        var paths = new List<string>();
        if (!launch.TryGetProperty("backupFiles", out JsonElement backupFiles) ||
            backupFiles.ValueKind != JsonValueKind.Array)
            return paths;
        foreach (JsonElement item in backupFiles.EnumerateArray())
        {
            string? relative = StringProperty(item, "relative");
            if (!string.IsNullOrEmpty(relative))
                paths.Add(relative);
        }
        return paths;
    }

    private static bool TryCreateReparsePoint(string linkPath, string targetPath, out string error)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            if ((File.GetAttributes(linkPath) & FileAttributes.ReparsePoint) != 0)
            {
                error = string.Empty;
                return true;
            }
            error = "Directory.CreateSymbolicLink did not produce a reparse point.";
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
        return false;
    }

    private static string AdaptScript(string original)
    {
        _scriptHasAcceptedProtocol =
            original.Contains("$accepted = $false", StringComparison.Ordinal) &&
            original.Contains("accepted.tmp", StringComparison.Ordinal);
        _scriptHasConfirmationProtocol =
            _scriptHasAcceptedProtocol &&
            original.Contains("$confirmed = $false", StringComparison.Ordinal) &&
            original.Contains("confirmed", StringComparison.Ordinal);
        string adapted = original;
        adapted = ReplaceExactly(
            adapted,
            "$ErrorActionPreference = 'Stop'",
            "$ErrorActionPreference = 'Stop'\r\n" + FakeHelpers + "\r\n",
            "inject fake launch/message helpers and Add-Type blocker",
            expected: 1);
        adapted = ReplaceExactly(
            adapted,
            "$newProcess = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $request.TargetDirectory -WindowStyle Hidden -PassThru",
            "$newProcess = FakeStart-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $request.TargetDirectory -WindowStyle Hidden -PassThru",
            "replace the real Start-Process call with the fake process helper",
            expected: 1);
        adapted = ReplaceExactly(
            adapted,
            "Add-Type -AssemblyName System.Windows.Forms",
            "$null = $null # Add-Type is blocked by the test harness",
            "replace Windows Forms assembly loading with a no-op",
            expected: 1);
        adapted = ReplaceExactly(
            adapted,
            "$null = [System.Windows.Forms.MessageBox]::Show(($request.FailureMessage + [Environment]::NewLine + $request.LogFile), 'StarPie')",
            "$null = Fake-ShowMessageBox ($request.FailureMessage + [Environment]::NewLine + $request.LogFile) 'StarPie'",
            "replace the real MessageBox call with a file-only fake",
            expected: 1);
        adapted = ReplaceExactly(
            adapted,
            "$waitSeconds = 45",
            "$waitSeconds = [double]::Parse($env:STARPIE_TEST_READY_TIMEOUT, [Globalization.CultureInfo]::InvariantCulture)",
            "make the readiness timeout environment-driven",
            expected: 1);
        adapted = ReplaceExactly(
            adapted,
            "$waitSeconds = 3",
            "$waitSeconds = [double]::Parse($env:STARPIE_TEST_LEGACY_TIMEOUT, [Globalization.CultureInfo]::InvariantCulture)",
            "make the legacy readiness timeout environment-driven",
            expected: 1);
        if (_scriptHasConfirmationProtocol)
        {
            adapted = ReplaceExactly(
                adapted,
                "$timer.Elapsed.TotalSeconds -gt 15",
                "$timer.Elapsed.TotalSeconds -gt [double]::Parse($env:STARPIE_TEST_CONFIRM_TIMEOUT, [Globalization.CultureInfo]::InvariantCulture)",
                "make the final-confirmation timeout environment-driven",
                expected: 1);
        }
        else
        {
            AdaptationRecords.Add(new AdaptationRecord
            {
                Description = "candidate script has no final-confirmation protocol; final-confirmation timeout adaptation was not applied",
                ExpectedOccurrences = 0,
                ActualOccurrences = 0,
                SourceLineNumbers = Array.Empty<int>(),
                ReplacementIsTestOnly = true,
                Applied = false,
            });
        }

        if (Regex.IsMatch(adapted, @"(?m)^\s*Start-Process\b", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("A real Start-Process call remained in the adapted script.");
        if (adapted.Contains("Add-Type -AssemblyName", StringComparison.Ordinal))
            throw new InvalidOperationException("A real Add-Type call remained in the adapted script.");
        return adapted;
    }

    private static string ReplaceExactly(string input, string needle, string replacement,
        string description, int expected)
    {
        var locations = new List<int>();
        int index = 0;
        while ((index = input.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            locations.Add(LineNumber(input, index));
            index += needle.Length;
        }
        if (locations.Count != expected)
            throw new InvalidOperationException(
                $"Adaptation '{description}' expected {expected} occurrence(s), found {locations.Count}.");

        AdaptationRecords.Add(new AdaptationRecord
        {
            Description = description,
            ExpectedOccurrences = expected,
            ActualOccurrences = locations.Count,
            SourceLineNumbers = locations,
            ReplacementIsTestOnly = true,
            Applied = true,
        });
        return input.Replace(needle, replacement, StringComparison.Ordinal);
    }

    private static int LineNumber(string text, int index)
    {
        int line = 1;
        for (int i = 0; i < index; i++)
        {
            if (text[i] == '\n')
                line++;
        }
        return line;
    }

    private static void RunCase(string name, Action action)
    {
        int checksBefore = _checks;
        int failuresBefore = _failures;
        _skipReason = null;
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Fail($"{name} threw {ex.GetType().Name}: {ex.Message}");
        }
        string status = _failures > failuresBefore
            ? "FAIL"
            : _skipReason is null ? "PASS" : "SKIP";
        CaseResults.Add(new CaseResult
        {
            Name = name,
            Status = status,
            Checks = _checks - checksBefore,
            Notes = _skipReason ?? string.Empty,
        });
        Console.WriteLine($"{status} {name} checks={_checks - checksBefore}");
    }

    private static void Skip(string reason)
    {
        _skipReason = reason;
        Console.WriteLine($"SKIP {reason}");
    }

    private static void Check(bool condition, string description)
    {
        _checks++;
        if (condition)
        {
            Console.WriteLine($"PASS {description}");
            return;
        }
        Fail(description);
    }

    private static void Fail(string description)
    {
        _failures++;
        FailureDetails.Add(description);
        Console.Error.WriteLine($"FAIL {description}");
    }

    private static void WriteSummary(DateTimeOffset startedUtc, Exception? fatal)
    {
        var summary = new
        {
            task = "UpdateApplyScript isolated safety behavior tests",
            candidatePath = _candidatePath,
            candidateSha256 = _candidateSha256,
            originalScriptSha256 = HashText(_originalScript),
            adaptedScriptSha256 = HashText(_adaptedScript),
            scriptHasAcceptedProtocol = _scriptHasAcceptedProtocol,
            scriptHasConfirmationProtocol = _scriptHasConfirmationProtocol,
            startedUtc,
            completedUtc = DateTimeOffset.UtcNow,
            checks = _checks,
            failures = _failures,
            result = fatal is null && _failures == 0 ? "PASS" : "FAIL",
            fatal = fatal?.ToString(),
            failureDetails = FailureDetails,
            cases = CaseResults,
            adaptations = AdaptationRecords,
            uncoveredBoundaries = UncoveredBoundaries,
            evidenceRoot = _evidenceRoot,
            testEnvironment = new
            {
                windowsPowerShellModulePath = WindowsPowerShellModulePath(),
                note = "The child PowerShell was given the Windows PowerShell module path so the candidate's Get-FileHash calls resolve under the Codex host environment.",
            },
            safety = new
            {
                controlledPowerShellStarted = true,
                realProductStarted = false,
                realUpdaterStarted = false,
                realPluginStarted = false,
                realGuiStarted = false,
                realInputInjected = false,
                systemSettingsChanged = false,
                realProcessLaunchReplacedByFake = true,
                messageBoxReplacedByFake = true,
            },
        };
        WriteJson(Path.Combine(_evidenceRoot, "summary.json"), summary);
    }

    private static (string Candidate, string Output) ParseArguments(string[] args)
    {
        string? candidate = null;
        string? output = null;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--candidate" when i + 1 < args.Length:
                    candidate = args[++i];
                    break;
                case "--output" when i + 1 < args.Length:
                    output = args[++i];
                    break;
                default:
                    throw new ArgumentException(
                        "Usage: test_update_apply_script --candidate <StarPie.dll> --output <owned-evidence-directory>");
            }
        }
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(output))
            throw new ArgumentException(
                "Usage: test_update_apply_script --candidate <StarPie.dll> --output <owned-evidence-directory>");
        return (candidate, output);
    }

    private static string HashText(string text) => HashBytes(Encoding.UTF8.GetBytes(text));

    private static string HashBytes(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static string HashFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void WriteText(string path, string text, bool withBom)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(path, text, new UTF8Encoding(withBom));
    }

    private static void WriteJson(string path, object value)
    {
        WriteText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }), withBom: false);
    }

    private sealed class TestCaseContext
    {
        public required string Root { get; init; }
        public required string Token { get; init; }
        public required string PayloadDirectory { get; init; }
        public required string BackupDirectory { get; init; }
        public required string TargetDirectory { get; init; }
        public required string RequestPath { get; init; }
        public required string AdaptedScriptPath { get; init; }
        public required string PreparedPath { get; init; }
        public required string ProceedPath { get; init; }
        public required string AcceptedPath { get; init; }
        public required string ConfirmedPath { get; init; }
        public required string AbortPath { get; init; }
        public required string ResultPath { get; init; }
        public required string UpdateLog { get; init; }
        public required string FakeEventLog { get; init; }
        public required bool RequireReady { get; init; }
        public required string Mode { get; init; }
        public required double ReadyTimeoutSeconds { get; init; }
        public required double ConfirmTimeoutSeconds { get; init; }
        public required bool PrewriteConfirmed { get; init; }
    }

    private sealed record PayloadSpec(string RelativePath, byte[] Content);

    private sealed class ScriptRun
    {
        public int ExitCode { get; init; }
        public string Stdout { get; init; } = string.Empty;
        public string Stderr { get; init; } = string.Empty;
        public bool TimedOut { get; init; }
    }

    private sealed class CaseResult
    {
        public string Name { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public int Checks { get; init; }
        public string Notes { get; init; } = string.Empty;
    }

    private sealed class AdaptationRecord
    {
        public string Description { get; init; } = string.Empty;
        public int ExpectedOccurrences { get; init; }
        public int ActualOccurrences { get; init; }
        public IReadOnlyList<int> SourceLineNumbers { get; init; } = Array.Empty<int>();
        public bool ReplacementIsTestOnly { get; init; }
        public bool Applied { get; init; }
    }
}
