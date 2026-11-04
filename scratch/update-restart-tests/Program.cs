using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using WinPieGestures;

internal static class Program
{
    private static int _checks;
    private static string _root = "";
    private static string _candidate = "";

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            _candidate = Path.GetFullPath(args.Single());
            string expected = Hash(Path.Combine(_candidate, "StarPie.dll"));
            Check(Hash(typeof(App).Assembly.Location) == expected, "loaded host matches exact candidate DLL");
            Check(AppVersionInfo.DisplayVersion == "2.0.1-beta.4", "candidate is beta.4");
            _root = Path.Combine(AppContext.BaseDirectory, "run ' unicode 测试 " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_root, "temp"));
            Environment.SetEnvironmentVariable("TEMP", Path.Combine(_root, "temp"));
            Environment.SetEnvironmentVariable("TMP", Path.Combine(_root, "temp"));
            Environment.SetEnvironmentVariable("LOCALAPPDATA", Path.Combine(_root, "local"));
            Environment.SetEnvironmentVariable("APPDATA", Path.Combine(_root, "roaming"));
            Check(Application.Current == null, "no product Application created");
            RunMutexCases();
            RunShutdownCasesAsync().GetAwaiter().GetResult();
            RunFakeUiPump();
            RunPreparationCasesAsync().GetAwaiter().GetResult();
            RunReadinessCases();
            RunCandidateIlGuards();
            RunLocalizedMessages();
            Check(Application.Current == null && App.MainMouseHook == null && App.MainKeyboardHook == null,
                "no product windows or hooks initialized");
            Console.WriteLine($"PASS: {_checks} candidate-bound update/restart checks; DLL SHA256={expected}");
            Console.WriteLine("Boundary: owned kernel primitives and file fixtures; fake helper only; no product process, GUI, plugins, input, install or system modification.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }

    private static string Hash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void RunMutexCases()
    {
        using (var mutex = new Mutex(false))
        using (var closing = new EventWaitHandle(false, EventResetMode.ManualReset))
        {
            int wakes = 0;
            Check(SingleInstanceHandoff.Acquire(mutex, closing, () => wakes++, 0, 5, 50) == InstanceHandoffResult.Acquired,
                "free mutex is acquired");
            Check(wakes == 0, "new owner does not wake another instance");
            mutex.ReleaseMutex();
        }
        RunHeldMutexCase(false, false, InstanceHandoffResult.ActivatedExisting);
        RunHeldMutexCase(true, false, InstanceHandoffResult.ExitTimedOut);
        RunHeldMutexCase(true, true, InstanceHandoffResult.Acquired);
        using var abandoned = new Mutex(false);
        var abandoner = new Thread(() => abandoned.WaitOne());
        abandoner.Start(); abandoner.Join();
        using var noClosing = new EventWaitHandle(false, EventResetMode.ManualReset);
        Check(SingleInstanceHandoff.Acquire(abandoned, noClosing, () => throw new Exception(), 0, 0, 0)
            == InstanceHandoffResult.Acquired, "abandoned ownership is safely acquired");
        abandoned.ReleaseMutex();

        using var held = new Mutex(false);
        using var ready = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var owner = new Thread(() => { held.WaitOne(); ready.Set(); release.Wait(); held.ReleaseMutex(); });
        owner.Start(); ready.Wait();
        try
        {
            bool denied = false;
            try { SingleInstanceHandoff.Acquire(held, noClosing, () => throw new UnauthorizedAccessException("fixture"), 0, 0, 0); }
            catch (UnauthorizedAccessException) { denied = true; }
            Check(denied, "wake permission denial propagates instead of authorizing a new instance");
        }
        finally { release.Set(); owner.Join(); }
    }

    private static void RunHeldMutexCase(bool isClosing, bool releaseOnWait, InstanceHandoffResult expected)
    {
        using var mutex = new Mutex(false);
        using var closing = new EventWaitHandle(isClosing, EventResetMode.ManualReset);
        using var ready = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int wakes = 0;
        var owner = new Thread(() => { mutex.WaitOne(); ready.Set(); release.Wait(); mutex.ReleaseMutex(); });
        owner.Start(); ready.Wait();
        Task? releaser = releaseOnWait ? Task.Run(async () => { await Task.Delay(20); release.Set(); }) : null;
        try
        {
            InstanceHandoffResult actual = SingleInstanceHandoff.Acquire(mutex, closing, () => wakes++, 0, 5, 500);
            Check(actual == expected, "held mutex state: " + expected);
            Check(wakes == (isClosing ? 0 : 1), "closing instances are waited for rather than merely woken");
            if (actual == InstanceHandoffResult.Acquired) mutex.ReleaseMutex();
            if (actual == InstanceHandoffResult.ExitTimedOut) Check(owner.IsAlive, "timeout does not terminate the previous owner");
        }
        finally { release.Set(); owner.Join(); releaser?.GetAwaiter().GetResult(); }
    }

    private static async Task RunShutdownCasesAsync()
    {
        var sequence = new AppShutdownSequence();
        var drain = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int cleanup = 0, shutdown = 0, errors = 0;
        Task first = sequence.RunAsync(async () => { Interlocked.Increment(ref cleanup); await drain.Task; },
            () => Interlocked.Increment(ref shutdown), _ => Interlocked.Increment(ref errors));
        Task second = sequence.RunAsync(() => throw new Exception("must not run"), () => shutdown++, _ => errors++);
        Check(ReferenceEquals(first, second), "concurrent exit requests share one task");
        Check(shutdown == 0, "shutdown waits until asynchronous cleanup completes");
        drain.SetResult(); await first;
        Check(cleanup == 1 && shutdown == 1 && errors == 0, "cleanup and shutdown each run exactly once");

        var failing = new AppShutdownSequence();
        await failing.RunAsync(() => throw new IOException("fixture cleanup failure"), () => shutdown++, _ => errors++);
        Check(shutdown == 2 && errors == 1, "cleanup exception still reaches final shutdown");
        await new AppShutdownSequence().RunAsync(() => throw new IOException(), () => shutdown++, _ => throw new IOException());
        Check(shutdown == 3, "even an error reporter exception cannot skip shutdown");
    }

    private sealed class PumpContext : SynchronizationContext
    {
        public readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> Queue = new();
        public override void Post(SendOrPostCallback callback, object? state) => Queue.Enqueue((callback, state));
    }

    private static void RunFakeUiPump()
    {
        SynchronizationContext? prior = SynchronizationContext.Current;
        var context = new PumpContext();
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            bool uiWork = false, finished = false;
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task task = new AppShutdownSequence().RunAsync(async () =>
            {
                context.Post(_ => { uiWork = true; completion.SetResult(); }, null);
                await completion.Task;
            }, () => finished = true, _ => { });
            var timer = Stopwatch.StartNew();
            while (!task.IsCompleted && timer.ElapsedMilliseconds < 2000)
            {
                if (context.Queue.TryDequeue(out var work)) work.Callback(work.State);
                else Thread.Sleep(1);
            }
            Check(task.IsCompletedSuccessfully && uiWork && finished, "exit wait allows queued UI cleanup to complete");
        }
        finally { SynchronizationContext.SetSynchronizationContext(prior); }
    }

    private sealed class FakeHelper(bool exited = false) : IUpdateHelperProcess
    {
        public bool Exited { get; set; } = exited;
        public Action? BeforeStatusCheck { get; set; }
        public bool HasExited { get { BeforeStatusCheck?.Invoke(); return Exited; } }
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private static async Task RunPreparationCasesAsync()
    {
        string target = Path.Combine(_root, "application");
        Directory.CreateDirectory(target);
        string executable = Path.Combine(target, "StarPie.exe");
        await File.WriteAllTextAsync(executable, "ORIGINAL");
        string zip = Path.Combine(_root, "new version.zip");
        using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using Stream output = archive.CreateEntry("StarPie.exe").Open();
            using FileStream input = File.OpenRead(Path.Combine(_candidate, "StarPie.exe"));
            input.CopyTo(output);
        }
        string? requestPath = null;
        var fake = new FakeHelper();
        IUpdateHelperProcess Start(ProcessStartInfo info)
        {
            Check(!info.UseShellExecute && info.CreateNoWindow && info.WindowStyle == ProcessWindowStyle.Hidden,
                "helper uses hidden direct process startup without elevation");
            Check(Path.IsPathFullyQualified(info.FileName) && info.FileName.EndsWith("powershell.exe", StringComparison.OrdinalIgnoreCase),
                "helper executable has an explicit system path");
            requestPath = info.ArgumentList[^1];
            Check(info.ArgumentList[^2] == "-RequestPath", "request path is a separate argument");
            using JsonDocument request = JsonDocument.Parse(File.ReadAllText(requestPath));
            Check(request.RootElement.GetProperty("PreviousPid").GetInt32() == 12345 &&
                request.RootElement.GetProperty("PreviousStartTicks").GetInt64() == 67890,
                "handoff includes both PID and creation identity");
            Check(request.RootElement.GetProperty("RequireReadySignal").GetBoolean(), "beta.4 requires HostReady acknowledgement");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(requestPath)!, "prepared"), "1");
            fake.BeforeStatusCheck = () =>
            {
                string root = Path.GetDirectoryName(requestPath)!;
                if (File.Exists(Path.Combine(root, "proceed"))) File.WriteAllText(Path.Combine(root, "accepted"), Path.GetFileName(root));
            };
            return fake;
        }
        using (UpdateRestartSession session = await UpdateRestartSession.PrepareAsync(zip, executable, 12345, 67890, startHelper: Start))
        {
            Check(File.ReadAllText(executable) == "ORIGINAL", "preparation does not overwrite the running installation");
            await session.CommitAsync();
            Check(File.Exists(Path.Combine(Path.GetDirectoryName(requestPath!)!, "proceed")), "commit explicitly authorizes the helper");
            Check(File.ReadAllText(Path.Combine(Path.GetDirectoryName(requestPath!)!, "confirmed")) == Path.GetFileName(Path.GetDirectoryName(requestPath!)),
                "accepted authorization receives a final token-bound parent confirmation");
        }
        Check(fake.Disposed && !File.Exists(Path.Combine(Path.GetDirectoryName(requestPath!)!, "abort")), "committed helper is detached without abort");
        var aborted = new FakeHelper();
        string? abortRoot = null;
        using (await UpdateRestartSession.PrepareAsync(zip, executable, 1, 1, startHelper: info =>
        {
            abortRoot = Path.GetDirectoryName(info.ArgumentList[^1])!;
            File.WriteAllText(Path.Combine(abortRoot, "prepared"), "1");
            return aborted;
        })) { }
        Check(aborted.Disposed && File.Exists(Path.Combine(abortRoot!, "abort")), "uncommitted disposal cancels helper authorization");
        var exited = new FakeHelper(true);
        bool failed = false;
        try { await UpdateRestartSession.PrepareAsync(zip, executable, 1, 1, startHelper: _ => exited); }
        catch (IOException) { failed = true; }
        Check(failed && exited.Disposed && File.ReadAllText(executable) == "ORIGINAL", "early helper exit preserves the running installation");
        var diedAfterPreparation = new FakeHelper();
        string? deadRoot = null;
        using (UpdateRestartSession session = await UpdateRestartSession.PrepareAsync(zip, executable, 1, 1, startHelper: info =>
        {
            deadRoot = Path.GetDirectoryName(info.ArgumentList[^1])!;
            File.WriteAllText(Path.Combine(deadRoot, "prepared"), "1");
            return diedAfterPreparation;
        }))
        {
            diedAfterPreparation.Exited = true;
            bool rejected = false;
            try { await session.CommitAsync(); } catch (IOException) { rejected = true; }
            Check(rejected && !File.Exists(Path.Combine(deadRoot!, "proceed")), "prepared helper death cannot authorize parent exit");
        }
        var noAcceptance = new FakeHelper();
        string? noAckRoot = null;
        using (UpdateRestartSession session = await UpdateRestartSession.PrepareAsync(zip, executable, 1, 1, startHelper: info =>
        {
            noAckRoot = Path.GetDirectoryName(info.ArgumentList[^1])!;
            File.WriteAllText(Path.Combine(noAckRoot, "prepared"), "1");
            return noAcceptance;
        }))
        {
            bool rejected = false;
            try { await session.CommitAsync(timeout: TimeSpan.FromMilliseconds(60)); }
            catch (TimeoutException) { rejected = true; }
            Check(rejected && !App.IsExiting, "missing authorization acknowledgement keeps the parent active");
        }
        Check(File.Exists(Path.Combine(noAckRoot!, "abort")), "unaccepted authorization is cancelled on dispose");
        Check(!File.Exists(Path.Combine(noAckRoot!, "confirmed")), "timed-out authorization cannot acquire final confirmation");
        bool started = false;
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await UpdateRestartSession.PrepareAsync(zip, executable, 1, 1, cancelled.Token, _ => { started = true; return new FakeHelper(); }); }
        catch (OperationCanceledException) { }
        Check(!started, "cancelled preparation never launches a helper");
        Check(!Directory.EnumerateFiles(target, ".starpie-write-*").Any(), "write preflight leaves no probe file");
    }

    private static void RunReadinessCases()
    {
        string root = Path.Combine(Path.GetTempPath(), "StarPieUpdate", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "request.json");
        string host = Environment.ProcessPath!;
        void WriteRequest(string hash) => File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            Token = Path.GetFileName(root), Executable = host,
            Files = new[] { new { RelativePath = "StarPie.exe", Sha256 = hash } },
        }));
        WriteRequest(Hash(host));
        UpdateRestartSession.ReportReady(new[] { UpdateRestartSession.RequestArgument, path });
        Check(File.ReadAllText(Path.Combine(root, "ready")) == Path.GetFileName(root), "owned request receives a token-bound readiness acknowledgement");
        File.Delete(Path.Combine(root, "ready"));
        WriteRequest(new string('0', 64));
        UpdateRestartSession.ReportReady(new[] { UpdateRestartSession.RequestArgument, path });
        Check(!File.Exists(Path.Combine(root, "ready")), "mismatched executable hash receives no readiness acknowledgement");
        string outside = Path.Combine(_root, "request.json");
        File.Copy(path, outside);
        UpdateRestartSession.ReportReady(new[] { UpdateRestartSession.RequestArgument, outside });
        Check(!File.Exists(Path.Combine(_root, "ready")), "out-of-scope readiness path cannot write an acknowledgement");
    }

    private static void RunCandidateIlGuards()
    {
        MethodInfo exit = typeof(App).GetMethod("OnExit", BindingFlags.NonPublic | BindingFlags.Instance)!;
        List<string> calls = ReadCalls(exit);
        Check(calls.IndexOf("SaveConfig") >= 0 && calls.IndexOf("DisposeInstanceHandles") > calls.IndexOf("SaveConfig"),
            "IL guard: config save precedes final instance-handle release");
        Check(!calls.Contains("ShutdownAll"), "IL guard: final OnExit does not repeat synchronous plugin drain");
        Check(typeof(UpdateManager).GetMethod("RestartAndApplyUpdateAsync")!.ReturnType == typeof(Task<bool>),
            "update UI uses the asynchronous preparation contract");
        var startup = typeof(App).GetMethod("OnStartup", BindingFlags.NonPublic | BindingFlags.Instance)!;
        List<string> startupCalls = ReadCalls(startup, qualify: true);
        Check(startupCalls.IndexOf("EventWaitHandle..ctor") >= 0 &&
            startupCalls.IndexOf("EventWaitHandle..ctor") < startupCalls.IndexOf("Mutex..ctor"),
            "IL guard: wake endpoint exists before the mutex can identify a primary instance");
    }

    private static void RunLocalizedMessages()
    {
        LanguageCode prior = I18n.CurrentLanguage;
        try
        {
            string[] keys = { "StartupErrorTitle", "InstanceStartupFailed", "InstanceExitWaitFailed", "UpdateArchiveMissing", "UpdateRestartPreparationFailed", "UpdateApplyFailureDetail" };
            foreach (LanguageCode language in new[] { LanguageCode.ZhCn, LanguageCode.ZhTw, LanguageCode.En, LanguageCode.Ja })
            {
                I18n.CurrentLanguage = language;
                foreach (string key in keys) Check(!string.IsNullOrWhiteSpace(I18n.T(key)) && I18n.T(key) != key, "new restart message exists in " + language);
                foreach (string status in new[] { "Failed", "Cancelled", "TimedOut", "VerificationFailed", "NotEnabled", "Suppressed" })
                {
                    string key = "AutoStartResult" + status;
                    Check(I18n.T(key) != key, "existing computed autostart key resolves in " + language);
                }
            }
        }
        finally { I18n.CurrentLanguage = prior; }
    }

    private static List<string> ReadCalls(MethodInfo method, bool qualify = false)
    {
        var codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode)).Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(code => unchecked((ushort)code.Value));
        byte[] il = method.GetMethodBody()!.GetILAsByteArray()!;
        var calls = new List<string>();
        for (int offset = 0; offset < il.Length;)
        {
            ushort key = il[offset++];
            if (key == 0xFE) key = (ushort)(0xFE00 | il[offset++]);
            OpCode code = codes[key];
            if (code.OperandType == OperandType.InlineMethod)
            {
                MethodBase target = method.Module.ResolveMethod(BitConverter.ToInt32(il, offset))!;
                calls.Add(qualify ? target.DeclaringType!.Name + "." + target.Name : target.Name);
            }
            offset += code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, offset),
                _ => 4,
            };
        }
        return calls;
    }
}
