using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using WinPieGestures;
using WinPieGestures.ArtStyles;
using WinPieGestures.Plugins;

internal static class ThemeStudioAllocationProbe
{
    private const string ExpectedHostHash = "1F2D54E3B0D60C6337CAA6114B40C977850F960BD08CB4023A0DE15E78362FB1";

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2 || args[1] != "--test-instance")
                throw new InvalidOperationException("Fresh isolated artifact root and --test-instance required.");
            var metadata = typeof(ThemeStudioAllocationProbe).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>().ToDictionary(x => x.Key, x => x.Value!);
            string allowed = Path.GetFullPath(metadata["ArtifactsRoot"]).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string root = Path.GetFullPath(args[0]);
            if (!Path.IsPathFullyQualified(args[0]) || !root.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || Directory.Exists(root))
                throw new InvalidOperationException("Fresh owned artifact directory required.");
            foreach (string variable in new[] { "LOCALAPPDATA", "APPDATA", "TEMP", "TMP" })
            {
                string path = Path.Combine(root, variable);
                Directory.CreateDirectory(path);
                Environment.SetEnvironmentVariable(variable, path);
            }
            string host = typeof(ThemeStudio).Assembly.Location;
            if (Hash(host) != ExpectedHostHash || Hash(Path.Combine(metadata["StarPieCandidateDir"], "StarPie.dll")) != ExpectedHostHash)
                throw new InvalidOperationException("0858 candidate hash mismatch.");
            AssertNoApplicationOrHost();
            _ = ConfigManager.CurrentConfig; // Generates defaults only under the isolated LOCALAPPDATA.
            ConfigManager.CurrentConfig.AppTheme = "Light";
            string configFile = Path.Combine(root, "LOCALAPPDATA", "StarPie", "config.json");
            string configBefore = Hash(configFile);
            var measurements = new List<object>();
            var keepAlive = new List<FrameworkElement>();
            // Baseline warms the generic WPF control path, without constructing a product Window.
            Measure("empty-content-control", () => new ContentControl(), measurements, keepAlive);
            Measure("theme-studio-first-construction", () => new ThemeStudio(), measurements, keepAlive);
            Measure("theme-studio-warm-construction", () => new ThemeStudio(), measurements, keepAlive);
            foreach (var studio in keepAlive.OfType<ThemeStudio>())
            {
                if (studio.IsLoaded || PresentationSource.FromVisual(studio) != null)
                    throw new InvalidOperationException("Offscreen component unexpectedly attached.");
                if (studio.FindName("ThemeCards") is not ItemsControl cards || cards.Items.Count == 0)
                    throw new InvalidOperationException("Production constructor did not populate theme samples.");
            }
            AssertNoApplicationOrHost();
            if (Hash(configFile) != configBefore) throw new InvalidOperationException("Component wrote isolated config.");
            Console.WriteLine("RESULT " + JsonSerializer.Serialize(new
            {
                CandidateDllSha256 = Hash(host), LoadedAssembly = host, Measurements = measurements,
                ConfigFileUnchanged = true, Offscreen = true,
                Boundary = "Default isolated config, Light theme; actual UserControl constructors only. No Window/Application/Show, dispatcher pumping, plugin actions, hooks, live target attachment or GC. Constructor costs do not predict full-window savings."
            }));
            GC.KeepAlive(keepAlive);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 2;
        }
    }

    private static void Measure(string scenario, Func<FrameworkElement> create,
        List<object> measurements, List<FrameworkElement> keepAlive)
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        long privateBefore = process.PrivateMemorySize64, residentBefore = process.WorkingSet64;
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        int assembliesBefore = AppDomain.CurrentDomain.GetAssemblies().Length;
        var watch = Stopwatch.StartNew();
        var control = create();
        watch.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        process.Refresh();
        int cards = control is ThemeStudio studio && studio.FindName("ThemeCards") is ItemsControl items ? items.Items.Count : 0;
        measurements.Add(new
        {
            Scenario = scenario, AllocatedCurrentThreadBytes = allocated,
            PrivateBytesDelta = process.PrivateMemorySize64 - privateBefore,
            WorkingSetDelta = process.WorkingSet64 - residentBefore,
            ElapsedMs = watch.Elapsed.TotalMilliseconds, ThemeCards = cards,
            AssembliesAdded = AppDomain.CurrentDomain.GetAssemblies().Length - assembliesBefore
        });
        keepAlive.Add(control);
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static void AssertNoApplicationOrHost()
    {
        if (Application.Current != null || App.MainMouseHook != null || App.MainKeyboardHook != null || PluginHost.IsInitialized)
            throw new InvalidOperationException("Application, hooks or plugin host unexpectedly initialized.");
    }
}
