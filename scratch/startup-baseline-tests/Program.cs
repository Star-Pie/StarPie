using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using WinPieGestures;

internal static class Program
{
    private static readonly Dictionary<short, OpCode> Codes = typeof(OpCodes).GetFields(BindingFlags.Static | BindingFlags.Public)
        .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)!).ToDictionary(o => o.Value);
    private static int _checks;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            string directory = Path.GetFullPath(args[0]);
            string scenario = args[1];
            bool single = args.Contains("--expect-single");
            string candidate = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(a => a.Key == "StarPieCandidateDir").Value!;
            string expected = Path.Combine(candidate, "StarPie.dll");
            string loaded = typeof(App).Assembly.Location;
            Check(Hash(expected) == Hash(loaded), "candidate hash binding");
            Check(Path.GetFullPath(loaded) == Path.Combine(AppContext.BaseDirectory, "StarPie.dll"), "isolated host copy");
            Check(args.Contains("--test-instance"), "test-instance gate required before ConfigManager initialization");
            Directory.CreateDirectory(directory);
            foreach (string variable in new[] { "LOCALAPPDATA", "APPDATA", "TEMP", "TMP" })
                Environment.SetEnvironmentVariable(variable, directory);
            string configPath = Path.Combine(directory, "StarPie", "config.json");
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            if (scenario != "missing")
                File.WriteAllText(configPath, scenario == "corrupt" ? "{invalid" : JsonSerializer.Serialize(Fixture()));

            var entry = typeof(App).GetMethod("OnStartup", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var calls = Calls(entry).ToArray();
            // Replay only configuration operations from the real entrypoint IL. Never invoke
            // OnStartup: it installs hooks, creates a tray and may show settings.
            var operations = calls.Where(m => m.DeclaringType == typeof(ConfigManager) ||
                (m.DeclaringType == typeof(App) && m.Name == "InitializeConfiguration")).ToArray();
            Check(operations.Length != 0, "real startup has a configuration operation");
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            foreach (var operation in operations)
            {
                if (operation.DeclaringType == typeof(ConfigManager))
                {
                    RuntimeHelpers.RunClassConstructor(typeof(ConfigManager).TypeHandle);
                    if (operation.Name == "LoadConfig") operation.Invoke(null, null);
                    else Check(operation.Name == "IsElevated" || operation.Name == "get_CurrentConfig", "only safe startup config calls replayed");
                }
                else
                {
                    Check(operation.IsStatic && operation.GetParameters().Length == 0, "initialization seam is static and parameterless");
                    var dependencies = Calls((MethodInfo)operation).ToArray();
                    Check(dependencies.Length == 1 && dependencies[0].DeclaringType == typeof(ConfigManager)
                        && dependencies[0].Name == "get_CurrentConfig", "initialization seam only ensures existing type initialization");
                    operation.Invoke(null, null);
                }
            }
            watch.Stop();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            long revision = ConfigManager.ConfigurationRevision;
            bool fallback = ConfigManager.IsFallbackConfig;
            var metric = new { scenario, elapsedMs = watch.Elapsed.TotalMilliseconds, allocatedBytes = allocated,
                revision, fallback, profiles = ConfigManager.CurrentConfig.Profiles.Count, sha256 = Hash(expected) };
            Console.WriteLine("METRIC " + JsonSerializer.Serialize(metric));
            if (single)
                Check(revision == (scenario == "missing" ? 2 : 1), "startup must not perform a second full configuration load");
            if (scenario == "corrupt")
            {
                Check(fallback, "corrupt config enters recovery mode");
                Check(File.ReadAllText(configPath) == "{invalid", "corrupt file remains recoverable");
                Check(Directory.GetFiles(Path.GetDirectoryName(configPath)!, "config.json.corrupt.*").Length == 1, "one backup is preserved");
                Check(!ConfigManager.SaveConfig(), "automatic save cannot overwrite recovery data");
                Check(File.ReadAllText(configPath) == "{invalid", "failed automatic save preserves corrupt input");
            }
            else if (scenario == "missing")
            {
                Check(!fallback && File.Exists(configPath), "missing config generates normal defaults");
                Check(JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(configPath))?.Profiles.Count > 0, "default config is complete and readable");
            }
            else
            {
                Check(!fallback && ConfigManager.CurrentConfig.Profiles.Count == 121, "full fixture loaded");
                Check(ConfigManager.CurrentConfig.Profiles[1].Layers.Count == 3, "all fixture layers preserved");
                Check(ConfigManager.CurrentConfig.Profiles[1].Layers[1].ActivationTrigger?.MouseButton == "MiddleButton", "layer binding retained");
                var preserved = ConfigManager.CurrentConfig;
                if (single)
                {
                    var initializer = typeof(App).GetMethod("InitializeConfiguration", BindingFlags.Static | BindingFlags.NonPublic)!;
                    initializer.Invoke(null, null);
                    initializer.Invoke(null, null);
                    Check(ReferenceEquals(preserved, ConfigManager.CurrentConfig) && ConfigManager.ConfigurationRevision == revision,
                        "repeated startup initialization does not replace data or republish routes");
                }
                var edited = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(configPath))!;
                edited.LongPressDelayMs = 321;
                File.WriteAllText(configPath, JsonSerializer.Serialize(edited));
                ConfigManager.LoadConfig();
                Check(ConfigManager.CurrentConfig.LongPressDelayMs == 321, "explicit reload still reads disk");
                Check(!ReferenceEquals(preserved, ConfigManager.CurrentConfig) && ConfigManager.ConfigurationRevision == revision + 1,
                    "explicit reload replaces config and republishes routes exactly once");
                Check(ProfileTriggerRouter.TryResolveMouse("app0.exe", "MiddleButton", System.Windows.Input.ModifierKeys.None, out var activation)
                    == ProfileTriggerResolution.Match && activation?.Layer == ConfigManager.CurrentConfig.Profiles[1].Layers[1], "reloaded layer route matches actual loaded layer");
            }
            AppLogger.Shutdown();
            Console.WriteLine($"PASS {_checks} startup checks; no GUI, hooks, actions or input injection.");
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"FAIL {_checks}: {e}");
            return 1;
        }
    }

    private static AppConfig Fixture()
    {
        var config = new AppConfig { Profiles = new() { new WheelProfile { ProcessName = "Global" } }, Language = "en" };
        for (int i = 0; i < 120; i++)
        {
            var profile = new WheelProfile { ProcessName = $"App{i}", BoundProcesses = $"app{i}.exe", Layers = new() };
            for (int layer = 0; layer < 3; layer++)
            {
                profile.Layers.Add(new WheelLayer { Name = $"Layer{layer}", SectorCount = 8,
                    ActivationTrigger = layer == 1 ? new TriggerConfig { TriggerType = "Mouse", MouseButton = "MiddleButton" } : null,
                    Actions = Enumerable.Range(0, 8).Select(n => new ActionItem { Type = "Hotkey", Name = $"Action{n}", Parameter = "Ctrl+C" }).ToList() });
            }
            config.Profiles.Add(profile);
        }
        return config;
    }

    private static IEnumerable<MethodBase> Calls(MethodInfo method)
    {
        byte[] il = method.GetMethodBody()!.GetILAsByteArray()!;
        for (int offset = 0; offset < il.Length;)
        {
            short key = il[offset++];
            if (key == 0xfe) key = (short)(0xfe00 | il[offset++]);
            var code = Codes[key];
            int size = code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, offset),
                _ => 4
            };
            if (code == OpCodes.Call || code == OpCodes.Callvirt)
            {
                MethodBase? called = method.Module.ResolveMethod(BitConverter.ToInt32(il, offset));
                if (called != null) yield return called;
            }
            offset += size;
        }
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static void Check(bool condition, string description) { _checks++; if (!condition) throw new InvalidOperationException(description); }
}
