using System.IO;
using System.Security.Cryptography;
using StarPie.Plugin;
using WinPieGestures.Plugins;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (!args.Contains("--test-instance")) return 2;
        string? dll = Environment.GetEnvironmentVariable("STARPIE_DESKTOP_PET_DLL");
        if (string.IsNullOrWhiteSpace(dll) || !File.Exists(dll))
        {
            Console.WriteLine("A pinned DesktopPet DLL is required; no plugin action is executed.");
            return 2;
        }
        string sandbox = Path.Combine(Path.GetTempPath(), "starpie-pet-compat-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("LOCALAPPDATA", sandbox);
        PluginPaths.OverrideRootsForTesting(Path.Combine(sandbox, "installed"), Path.Combine(sandbox, "scan"));
        var scan = PluginScanner.ScanSelectedDll(dll);
        Console.WriteLine($"SDK={PluginApi.ApiVersion}; Accepted={scan.Accepted}; Reason={scan.ErrorDetail}");
        if (!scan.Accepted) return 1;
        string originalHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll)));
        string source = Path.Combine(sandbox, "source");
        Directory.CreateDirectory(source);
        // Copy only executable metadata. Installed settings, assets and enable preferences stay untouched.
        foreach (string name in new[] { Path.GetFileName(dll), "plugin.json", "StarPie.Plugin.DesktopPet.deps.json" })
        {
            string file = Path.Combine(Path.GetDirectoryName(dll)!, name);
            if (File.Exists(file)) File.Copy(file, Path.Combine(source, name));
        }
        PluginHost.HeadlessMode = true;
        try
        {
            PluginHost.Initialize();
            var isolatedScan = PluginScanner.ScanSelectedDll(Path.Combine(source, Path.GetFileName(dll)));
            var install = PluginHost.CommitInstall(isolatedScan, new PluginInstallOptions { Acknowledged = true, EnableAfterInstall = true });
            if (!install.Success || !install.Enabled) throw new InvalidOperationException(install.Error);
            const string id = "io.github.softblack42.desktoppet";
            var instance = PluginHost.Find(id) ?? throw new InvalidOperationException("Installed instance missing.");
            if (instance.State != PluginRuntimeState.Active) throw new InvalidOperationException("Actual plugin initialization failed.");
            var page = PluginSettingsPageService.Open(id) ?? throw new InvalidOperationException("Real settings registration missing.");
            if (page.Commands.Count == 0 || page.AllFields.All(field => field.Type != ParameterFieldType.Slider))
                throw new InvalidOperationException("Real settings controls missing.");
            foreach (var section in page.Sections)
            {
                if (PluginSettingsPageService.Open(id, section.Id) is not { Fields.Count: > 0 })
                    throw new InvalidOperationException("Real settings section missing.");
            }
            Console.WriteLine($"REAL LOAD PASS: Active; SDK={PluginApi.ApiVersion}; fields={page.AllFields.Count}; commands={page.Commands.Count}; sections={page.Sections.Count}; generation={instance.GenerationId}.");
            Console.WriteLine("No pet action executed, no pet window created; all writable data stays in " + sandbox);
            var stop = PluginHost.DisableAsync(id, PluginStopReason.UserDisabled, TimeSpan.FromSeconds(12)).GetAwaiter().GetResult();
            if (!stop.IsFullyStopped || PluginSettingsPageService.Open(id) != null)
                throw new InvalidOperationException("Real plugin stop/unload failed: " + stop);
            if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll))) != originalHash)
                throw new InvalidOperationException("Installed DLL changed.");
            Console.WriteLine("REAL STOP PASS: contributions revoked, plugin fully stopped, installed DLL unchanged.");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine("FAIL: " + ex); return 1; }
        finally { PluginHost.ShutdownAll(); }
    }
}
