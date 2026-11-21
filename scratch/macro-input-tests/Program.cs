using System.Reflection;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using WinPieGestures;

internal static class Program
{
    private readonly record struct Event(ushort Key, ushort Scan, uint Flags, nint Extra, bool Legacy = false);
    private static readonly List<Event> Events = new();
    private static readonly List<Event[]> Batches = new();
    private static readonly HashSet<int> PhysicalKeys = new();
    private static int _passed, _failed;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("Expected candidate DLL, SHA256 and owned sandbox.");
        string dll = Path.GetFullPath(args[0]);
        if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll))) != args[1] ||
            !File.ReadAllBytes(dll).SequenceEqual(File.ReadAllBytes(typeof(ActionExecutor).Assembly.Location)))
            throw new InvalidOperationException("Loaded candidate does not match the expected SHA256.");
        string sandbox = Path.GetFullPath(args[2]);
        Directory.CreateDirectory(sandbox);
        foreach (string name in new[] { "LOCALAPPDATA", "APPDATA", "TEMP", "TMP" }) Environment.SetEnvironmentVariable(name, sandbox);
        ConfigManager.DisableAutoStartSync = true;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        try
        {
            Run("Alt has one down and one up", () => { ActionExecutor.ExecuteHotkey("Alt"); Exact((164, false), (164, true)); });
            Run("macro Alt A E contains exactly six events", () =>
            {
                foreach (string key in new[] { "Alt", "A", "E" }) ActionExecutor.ExecuteHotkey(key);
                Exact((164, false), (164, true), (65, false), (65, true), (69, false), (69, true));
            });
            Run("multi-step Alt A E uses the same event sequence", () =>
            {
                ActionExecutor.ExecuteHotkey("Alt, A, E");
                Exact((164, false), (164, true), (65, false), (65, true), (69, false), (69, true));
            });
            Run("ordinary key is paired", () => { ActionExecutor.ExecuteHotkey("A"); Exact((65, false), (65, true)); });
            Run("Ctrl C releases once without generic aliases", () =>
            {
                ActionExecutor.ExecuteHotkey("Ctrl+C"); Exact((162, false), (67, false), (67, true), (162, true));
            });
            Run("chord releases in reverse order", () =>
            {
                ActionExecutor.ExecuteHotkey("Ctrl+Shift+A");
                Exact((162, false), (160, false), (65, false), (65, true), (160, true), (162, true));
            });
            Run("held physical Alt is borrowed without release", () =>
            {
                PhysicalKeys.Add(164); ActionExecutor.ExecuteHotkey("Alt+A"); Exact((65, false), (65, true));
            });
            Run("held opposite-side Ctrl is borrowed", () =>
            {
                PhysicalKeys.Add(163); ActionExecutor.ExecuteHotkey("Ctrl+C"); Exact((67, false), (67, true));
            });
            Run("borrowed modifier loss stops before the main key", () =>
            {
                int reads = 0;
                ActionExecutor.KeyboardKeyStateOverride = key => key == 18 && ++reads == 1 ? unchecked((short)0x8000) : (short)0;
                MustThrow(() => ActionExecutor.ExecuteHotkey("Alt+A"));
                Require(Events.Count == 0, "unmodified A was sent after Alt was released");
            });
            Run("concurrent host calls do not borrow each other's modifiers", ConcurrentChords);
            Run("standalone modifier refuses to steal a held key", () =>
            {
                PhysicalKeys.Add(164); MustThrow(() => ActionExecutor.ExecuteHotkey("Alt"));
                Require(Events.Count == 0, "held modifier was changed");
            });
            Run("private panel chord shares balanced input", () =>
            {
                ActionExecutor.ExecuteHotkey("Ctrl+C", true); Exact((162, false), (67, false), (67, true), (162, true));
            });
            Run("PrintScreen retains one atomic pair with zero scan", () =>
            {
                ActionExecutor.ExecuteHotkey("PrintScreen"); Exact((44, false), (44, true));
                Require(Batches.Count == 1 && Batches[0].Length == 2 && Events.All(e => e.Scan == 0), "PrintScreen batch/scan changed");
            });
            Run("extended keys retain extended flag", () =>
            {
                ActionExecutor.ExecuteHotkey("Right"); Exact((39, false), (39, true));
                Require(Events.All(e => (e.Flags & 1) != 0), "missing extended flag");
            });
            Run("Win chord never emits delayed releases into later input", () =>
            {
                ActionExecutor.ExecuteHotkey("Win+Shift+S"); int count = Events.Count;
                ActionExecutor.ExecuteHotkey("Alt"); Thread.Sleep(140);
                Require(count == 6, "unexpected Win chord events");
                Exact((91, false), (160, false), (83, false), (83, true), (160, true), (91, true), (164, false), (164, true));
            });
            Run("text remains Unicode and does not become menu keys", () =>
            {
                ActionExecutor.SendTextInput("AE");
                Require(Events.Count == 4 && Events.All(e => e.Key == 0 && (e.Flags & 4) != 0), "TEXT is not Unicode");
                Require(Events.Select(e => e.Scan).SequenceEqual(new ushort[] { 65, 65, 69, 69 }), "TEXT units changed");
            });
            Run("partial modifier batch releases only accepted downs and stops", () =>
            {
                int calls = 0;
                ActionExecutor.KeyboardInputSenderOverride = packets => Accept(packets, ++calls == 1 ? 1u : (uint)packets.Length);
                MustThrow(() => ActionExecutor.ExecuteHotkey("Ctrl+Shift+A")); Exact((162, false), (162, true));
            });
            Run("zero accepted inputs fails before later macro steps", () =>
            {
                ActionExecutor.KeyboardInputSenderOverride = packets => Accept(packets, 0);
                MustThrow(() => { ActionExecutor.ExecuteHotkey("Alt"); ActionExecutor.ExecuteHotkey("A"); });
                Require(Events.Count == 0, "failed input continued or emitted legacy releases");
            });
            Run("exception releases an owned modifier once", () =>
            {
                int calls = 0;
                ActionExecutor.KeyboardInputSenderOverride = packets => ++calls == 2 ? throw new IOException("fake send failure") : Accept(packets, (uint)packets.Length);
                MustThrow(() => ActionExecutor.ExecuteHotkey("Ctrl+C")); Exact((162, false), (162, true));
            });
            Run("partial PrintScreen batch balances the accepted down", () =>
            {
                int calls = 0;
                ActionExecutor.KeyboardInputSenderOverride = packets => Accept(packets, ++calls == 1 ? 1u : (uint)packets.Length);
                MustThrow(() => ActionExecutor.ExecuteHotkey("PrintScreen")); Exact((44, false), (44, true));
            });
            Run("normal release failure is retried only for the owned key", () =>
            {
                int calls = 0;
                ActionExecutor.KeyboardInputSenderOverride = packets => Accept(packets, ++calls == 2 ? 0u : (uint)packets.Length);
                MustThrow(() => ActionExecutor.ExecuteHotkey("Alt")); Exact((164, false), (164, true));
            });
            Run("empty hotkey produces no input", () => { ActionExecutor.ExecuteHotkey(""); Require(Events.Count == 0, "empty input emitted events"); });
            Run("cancelled empty wheel does not clean up unrelated keys", () => CheckWheelCleanup("CancelGestureTracking", new object[] { "TestCancelled" }));
            Run("ending empty wheel does not clean up unrelated keys", () => CheckWheelCleanup("EndActiveGesture", new object[] { Type.Missing }));
        }
        finally { ActionExecutor.ResetTestSeams(); app.Shutdown(); }
        Console.WriteLine($"RESULT: {_passed} passed, {_failed} failed; all senders fake; no product windows or hooks.");
        return _failed == 0 ? 0 : 1;
    }

    private static void Reset()
    {
        Events.Clear(); Batches.Clear(); PhysicalKeys.Clear();
        ActionExecutor.KeyboardInputSenderOverride = p => Accept(p, (uint)p.Length);
        ActionExecutor.LegacyKeyboardInputOverride = (key, scan, flags, extra) => Events.Add(new(key, scan, flags, extra, true));
        ActionExecutor.KeyboardKeyStateOverride = key => PhysicalKeys.Contains(key) ? unchecked((short)0x8000) : (short)0;
        ActionExecutor.KeyboardMapVirtualKeyOverride = (key, _) => key;
        ActionExecutor.ReleaseStuckModifiersOverride = null;
    }
    private static uint Accept((ushort Key, ushort Scan, uint Flags, nint ExtraInfo)[] packets, uint count)
    {
        var batch = packets.Take((int)count).Select(p => new Event(p.Key, p.Scan, p.Flags, p.ExtraInfo)).ToArray();
        lock (Events) { Batches.Add(batch); Events.AddRange(batch); }
        return count;
    }
    private static void ConcurrentChords()
    {
        using var firstDown = new ManualResetEventSlim();
        using var continueFirst = new ManualResetEventSlim();
        using var secondStarted = new ManualResetEventSlim();
        int held = 0, stopped = 0;
        ActionExecutor.KeyboardKeyStateOverride = key => (key is 17 or 162) && Volatile.Read(ref held) != 0 ? unchecked((short)0x8000) : (short)0;
        ActionExecutor.KeyboardInputSenderOverride = packets =>
        {
            uint accepted = Accept(packets, (uint)packets.Length);
            foreach (var packet in packets)
                if (packet.Key == 162) Volatile.Write(ref held, (packet.Flags & 2) == 0 ? 1 : 0);
            if (packets.Any(p => p.Key == 162 && (p.Flags & 2) == 0) && Interlocked.Exchange(ref stopped, 1) == 0)
            {
                firstDown.Set();
                if (!continueFirst.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("fake sender was not resumed");
            }
            return accepted;
        };
        var first = Task.Factory.StartNew(() => ActionExecutor.ExecuteHotkey("Ctrl+A"), TaskCreationOptions.LongRunning);
        Task? second = null;
        try
        {
            Require(firstDown.Wait(TimeSpan.FromSeconds(5)), "first fake modifier was not reached");
            second = Task.Factory.StartNew(() => { secondStarted.Set(); ActionExecutor.ExecuteHotkey("Ctrl+C"); }, TaskCreationOptions.LongRunning);
            Require(secondStarted.Wait(TimeSpan.FromSeconds(5)), "second call did not start");
            Require(!second.Wait(100), "second input interleaved while the first owned Ctrl");
        }
        finally
        {
            continueFirst.Set();
            if (second != null) Task.WaitAll(first, second); else first.GetAwaiter().GetResult();
        }
        Exact((162, false), (65, false), (65, true), (162, true), (162, false), (67, false), (67, true), (162, true));
    }
    private static void Exact(params (int Key, bool Up)[] expected)
    {
        Require(Events.All(e => !e.Legacy && e.Extra == ActionExecutor.StarPieExtraInfo), "legacy/unmarked input emitted");
        var actual = Events.Select(e => ((int)e.Key, (e.Flags & 2) != 0)).ToArray();
        Require(actual.SequenceEqual(expected), "unexpected input sequence: " + string.Join(",", actual));
    }
    private static void CheckWheelCleanup(string method, object[] args)
    {
        int cleanups = 0;
        ActionExecutor.ReleaseStuckModifiersOverride = () => cleanups++;
        using (var controller = new GestureController(new MouseHook(), new KeyboardHook()))
            typeof(GestureController).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(controller, args);
        Require(cleanups == 0 && Events.Count == 0, "wheel released keys it did not press");
    }
    private static void MustThrow(Action action)
    {
        bool threw = false; try { action(); } catch { threw = true; }
        Require(threw, "failed input was reported as success");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Run(string name, Action action)
    {
        Reset();
        try { action(); _passed++; Console.WriteLine("PASS " + name); }
        catch (Exception ex) { _failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
    }
}
