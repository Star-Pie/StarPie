using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using WinPieGestures;
using WinPieGestures.Plugins;

internal static class Program
{
    private static int _checks;

    [STAThread]
    private static int Main(string[] args)
    {
        if (!args.Contains("--test-instance")) throw new ArgumentException("Pass --test-instance to disable OS integration.");
        string sandbox = Directory.CreateTempSubdirectory("starpie-data-").FullName;
        string local = Path.Combine(sandbox, "local");
        string roaming = Path.Combine(sandbox, "roaming");
        Environment.SetEnvironmentVariable("LOCALAPPDATA", local);
        Environment.SetEnvironmentVariable("APPDATA", roaming);
        bool loggingStarted = false;
        try
        {
            string candidate = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(a => a.Key == "CandidateDll").Value!;
            using var expected = File.OpenRead(candidate);
            using var loaded = File.OpenRead(typeof(ApplicationDataPaths).Assembly.Location);
            Check(SHA256.HashData(expected).SequenceEqual(SHA256.HashData(loaded)), "actual candidate assembly");
            string install = Directory.CreateDirectory(Path.Combine(sandbox, "install")).FullName;
            string data = Path.Combine(install, "Data");
            string marker = Path.Combine(install, "StarPie.portable");
            ApplicationDataPaths.Initialize(install);
            Check(!ApplicationDataPaths.IsPortable && ApplicationDataPaths.Root == Path.Combine(local, "StarPie"), "default local root");
            Check(ApplicationDataPaths.ProgramCacheDirectory == Path.Combine(roaming, "StarPie"), "default roaming program cache");
            Check(!Directory.Exists(data), "ordinary startup creates no Data directory");
            File.WriteAllText(Path.Combine(install, "portable.flag"), "");
            Check(ApplicationDataPaths.ResolvePortableRoot(install) == null, "plugin-only marker leaves application profile alone");
            Directory.CreateDirectory(data);
            Check(ApplicationDataPaths.ResolvePortableRoot(install) == null, "Data alone does not select the profile");

            Directory.CreateDirectory(marker);
            Refuses(() => ApplicationDataPaths.Initialize(install), "reject malformed marker");
            Directory.Delete(marker);
            File.WriteAllText(marker, "");
            Directory.Delete(data);
            File.WriteAllText(data, "preserve me");
            Refuses(() => ApplicationDataPaths.Initialize(install), "reject file in place of Data");
            Check(File.ReadAllText(data) == "preserve me", "preserve existing file");
            File.Delete(data);
            ApplicationDataPaths.Initialize(install);
            Check(ApplicationDataPaths.IsPortable && ApplicationDataPaths.Root == data && Directory.Exists(data), "marker selects and creates portable root");
            string config = Path.Combine(data, "config.json");
            File.WriteAllText(config, "{}");
            File.SetAttributes(config, FileAttributes.ReadOnly);
            try { Refuses(() => ApplicationDataPaths.Initialize(install), "read-only config fails before loading"); }
            finally { File.SetAttributes(config, FileAttributes.Normal); }
            using (File.Open(config, FileMode.Open, FileAccess.Read, FileShare.None))
                Refuses(() => ApplicationDataPaths.Initialize(install), "inaccessible existing config fails before loading");
            string sentinel = Path.Combine(data, "Configs");
            File.WriteAllText(sentinel, "keep");
            Refuses(() => ApplicationDataPaths.Initialize(install), "invalid existing profile subdirectory is preserved");
            File.Delete(sentinel);
            foreach (string name in new[] { "config.json.tmp", "config.json.bak", "Configs/existing.json", "plugin-data/registry.json", "plugin-data/registry.json.tmp", "CustomIcons/custom.png" })
            {
                string path = Path.Combine(data, name);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "preserved");
                File.SetAttributes(path, FileAttributes.ReadOnly);
                try { Refuses(() => ApplicationDataPaths.Initialize(install), "startup protects read-only " + name); }
                finally { File.SetAttributes(path, FileAttributes.Normal); }
                Check(File.ReadAllText(path) == "preserved", "read-only file bytes " + name);
                File.Delete(path);
            }
            string external = Path.Combine(sandbox, "outside.txt");
            File.WriteAllText(external, "outside");
            string tempLink = config + ".tmp";
            try
            {
                File.CreateSymbolicLink(tempLink, external);
                Refuses(() => ApplicationDataPaths.Initialize(install), "preexisting config temp link blocked");
                Check(File.ReadAllText(external) == "outside", "external symlink target preserved");
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || (ex is IOException && (ex.HResult & 0xffff) == 1314))
            { Console.WriteLine("SKIP file symlink creation requires Windows privilege"); }
            finally { File.Delete(tempLink); }
            foreach (string name in new[] { "Configs", "plugin-data", "CustomIcons" }) Directory.Delete(Path.Combine(data, name));
            ApplicationDataPaths.Initialize(install);
            Check(File.ReadAllText(config) == "{}" && Directory.GetFileSystemEntries(data).Length == 1, "probe preserves profile and cleans temporary files");
            File.Delete(marker);
            Check(ApplicationDataPaths.Root == data && ApplicationDataPaths.ProgramCacheDirectory == data, "root and program cache stay pinned for this launch");

            int junctionOption = Array.IndexOf(args, "--junction-fixture");
            if (junctionOption >= 0)
            {
                string fixture = args[junctionOption + 1];
                Refuses(() => ApplicationDataPaths.ResolvePortableRoot(fixture), "existing directory junction cannot redirect profile writes");
            }

            string old = Directory.CreateDirectory(Path.Combine(local, "WinPieGestures")).FullName;
            File.WriteAllText(Path.Combine(old, "config.json"), "old-config");
            File.Delete(config);
            string oldPlugins = Directory.CreateDirectory(Path.Combine(local, "StarPie", "plugins")).FullName;
            File.WriteAllText(Path.Combine(oldPlugins, "legacy.txt"), "old-plugin");
            PluginPaths.Configure(portableRequested: true);
            Check(PluginPaths.IsPortable && PluginPaths.Root == Path.Combine(data, "plugin-data"), "global profile takes precedence over plugin-only setting");
            Check(!Directory.Exists(PluginPaths.Root) && File.ReadAllText(Path.Combine(oldPlugins, "legacy.txt")) == "old-plugin", "plugin setup skips migration from other profiles");
            loggingStarted = true;
            Check(ConfigManager.ConfigsFolder == Path.Combine(data, "Configs") && File.Exists(config) && !ConfigManager.IsFallbackConfig,
                "real ConfigManager creates the default config and schemes in Data");
            Check(File.ReadAllText(Path.Combine(old, "config.json")) == "old-config" && !File.Exists(Path.Combine(local, "StarPie", "config.json")),
                "user configuration survives and no local copy is created");
            string cacheFile = IconHelper.GetPersistentIconCachePath("example.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
            File.WriteAllText(cacheFile, "existing");
            File.SetAttributes(cacheFile, FileAttributes.ReadOnly);
            try
            {
                Check(IconHelper.LoadPersistentIconSnapshot("example.exe") == null && File.ReadAllText(cacheFile) == "existing",
                    "read-only cache entries survive decode and cleanup");
            }
            finally { File.SetAttributes(cacheFile, FileAttributes.Normal); }
            string scratch = config + ".tmp";
            File.WriteAllText(scratch, "keep temp");
            File.SetAttributes(scratch, FileAttributes.ReadOnly);
            string persisted = File.ReadAllText(config);
            try
            {
                Check(!ConfigManager.SaveConfig() && File.ReadAllText(scratch) == "keep temp" && File.ReadAllText(config) == persisted,
                    "real atomic save declines an unwritable temporary file without changing the active config");
            }
            finally { File.SetAttributes(scratch, FileAttributes.Normal); }
            Check(AppLogger.GetLogFolderPath() == Path.Combine(data, "logs"), "log root");
            Check(IconHelper.GetCustomIconsDirectory() == Path.Combine(data, "CustomIcons") && IconHelper.PersistentIconCacheFolder == Path.Combine(data, "IconCache"), "icon directories");
            Check(!Directory.Exists(roaming) && Directory.GetFileSystemEntries(Path.Combine(local, "StarPie")).Length == 1, "other user data roots remain untouched");
            Console.WriteLine($"RESULT passed={_checks} failed=0");
            return 0;
        }
        finally
        {
            if (loggingStarted) AppLogger.Shutdown();
            Directory.Delete(sandbox, recursive: true);
        }
    }

    private static void Refuses(Action action, string name)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Check(true, name); return; }
        throw new InvalidOperationException(name);
    }

    private static void Check(bool passed, string name)
    {
        if (!passed) throw new InvalidOperationException(name);
        Console.WriteLine("PASS " + name);
        _checks++;
    }
}
