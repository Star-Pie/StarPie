using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using WinPieGestures;

internal static class SeedConfig
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2 || args[1] != "--test-instance") throw new InvalidOperationException("Explicit test-instance gate required.");
            var metadata = typeof(SeedConfig).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToDictionary(a => a.Key, a => a.Value!);
            string root = Path.GetFullPath(args[0]);
            string allowed = Path.GetFullPath(metadata["ArtifactsRoot"]).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!root.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || Directory.Exists(root)) throw new InvalidOperationException("New directory under the owned artifact root required.");
            string candidate = Path.Combine(metadata["StarPieCandidateDir"], typeof(ConfigManager).Assembly.GetName().Name + ".dll");
            if (Hash(candidate) != Hash(typeof(ConfigManager).Assembly.Location)) throw new InvalidOperationException("Candidate binding failed.");
            Directory.CreateDirectory(root);
            foreach (string name in new[] { "LOCALAPPDATA", "APPDATA", "TEMP", "TMP" })
            {
                string path = Path.Combine(root, "seed-env", name);
                Directory.CreateDirectory(path);
                Environment.SetEnvironmentVariable(name, path);
            }
            // Only ConfigManager's normal first-install path is exercised. No App,
            // Window, PluginHost initialization, hooks, tray, or actions are run.
            var config = ConfigManager.CurrentConfig;
            if (!ConfigManager.IsTestInstanceMode() || config.Profiles.Count == 0 || config.Profiles[0].Actions.Count == 0)
                throw new InvalidOperationException("Complete application-generated defaults required.");
            config.AutoCheckUpdate = false;
            config.AutoStartAsAdmin = false;
            config.Plugins.EnablePluginSystem = true;
            config.Plugins.PortableMode = false;
            config.Plugins.DeveloperMode = false;
            config.Plugins.ExtraScanDirectories.Clear();
            config.Plugins.PreloadOnStartup = true;
            config.Plugins.HasPromptedOfficialPluginsOnboarding = true;
            string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            string file = Path.Combine(root, "seed-config.json");
            File.WriteAllText(file, json);
            File.WriteAllText(Path.Combine(root, "seed-summary.json"), JsonSerializer.Serialize(new
            {
                CandidateDirectory = Path.GetFullPath(metadata["StarPieCandidateDir"]), CandidateDllSha256 = Hash(candidate),
                Profiles = config.Profiles.Count, ConfigBytes = new FileInfo(file).Length, ConfigSha256 = Hash(file),
                Boundary = "ConfigManager-generated defaults only; no GUI, hooks, plugin initialization or actions."
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"PASS complete default config; profiles={config.Profiles.Count}; SHA256={Hash(file)}; no GUI or plugin initialization.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
