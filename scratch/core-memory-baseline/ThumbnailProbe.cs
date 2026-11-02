using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinPieGestures;

internal static class ThumbnailProbe
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length is not (3 or 4) || args[2] != "--test-instance" || args[0] is not ("thumbnail" or "existing-helper" or "background-helper")) throw new InvalidOperationException("Explicit test scenario required.");
            string input;
            if (args.Length == 4) input = args[3];
            else
            {
                string inputConfig = Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA")!, "StarPie", "config.json");
                using var doc = JsonDocument.Parse(File.ReadAllText(inputConfig));
                input = doc.RootElement.GetProperty("CoreCustomImagePath").GetString()!;
            }
            if (!Path.IsPathFullyQualified(input) || input.StartsWith("\\\\") || !File.Exists(input)) throw new InvalidOperationException("Existing local image input required.");
            var metadata = typeof(ThumbnailProbe).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToDictionary(x => x.Key, x => x.Value!);
            string root = Path.GetFullPath(args[1]);
            string allowed = Path.GetFullPath(metadata["ArtifactsRoot"]).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!root.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || Directory.Exists(root)) throw new InvalidOperationException("Fresh owned artifact directory required.");
            string candidate = Path.Combine(metadata["StarPieCandidateDir"], typeof(SettingsWindow).Assembly.GetName().Name + ".dll");
            string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(candidate)));
            if (hash != Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(SettingsWindow).Assembly.Location)))) throw new InvalidOperationException("Candidate binding mismatch.");
            foreach (string key in new[] { "LOCALAPPDATA", "APPDATA", "TEMP", "TMP" })
            {
                string path = Path.Combine(root, key); Directory.CreateDirectory(path); Environment.SetEnvironmentVariable(key, path);
            }
            _ = ConfigManager.CurrentConfig;
            var image = new Image();
            var owner = (SettingsWindow)RuntimeHelpers.GetUninitializedObject(typeof(SettingsWindow));
            typeof(SettingsWindow).GetField("CoreImageThumbnail", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(owner, image);
            GC.Collect(2, GCCollectionMode.Forced, true); // disposable probe process only
            using var process = Process.GetCurrentProcess(); process.Refresh();
            long privateBefore = process.PrivateMemorySize64, residentBefore = process.WorkingSet64;
            var watch = Stopwatch.StartNew();
            ImageSource? source;
            if (args[0] == "thumbnail")
            {
                typeof(SettingsWindow).GetMethod("UpdateCoreImageThumbnail", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, new object?[] { input });
                source = image.Source;
            }
            else source = args[0] == "background-helper" ? IconHelper.GetCustomImageSource(input, 1024) : IconHelper.GetCustomImageSource(input);
            watch.Stop();
            if (source is not BitmapSource bitmap) throw new InvalidOperationException("BitmapSource required.");
            process.Refresh();
            Console.WriteLine("METRIC " + JsonSerializer.Serialize(new
            {
                Scenario = args[0], PixelWidth = bitmap.PixelWidth, PixelHeight = bitmap.PixelHeight, bitmap.IsFrozen,
                PrivateBytesDelta = process.PrivateMemorySize64 - privateBefore, WorkingSetDelta = process.WorkingSet64 - residentBefore,
                ElapsedMs = watch.Elapsed.TotalMilliseconds, CandidateDllSha256 = hash,
                InputSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))),
                Boundary = "Offscreen Image/uninitialized owner only; no Window constructor, Application, plugin, hook, input or pixel buffer copy."
            }));
            GC.KeepAlive(owner); GC.KeepAlive(source);
            int budget = args[0] == "thumbnail" ? 128 : args[0] == "background-helper" ? 1024 : 256;
            if (bitmap.PixelWidth > budget || bitmap.PixelHeight > budget) { Console.Error.WriteLine($"FAIL image exceeds {budget}px decode budget"); return 1; }
            if (!bitmap.IsFrozen) throw new InvalidOperationException("Cached image must be frozen.");
            if (args[0] == "thumbnail")
            {
                var update = typeof(SettingsWindow).GetMethod("UpdateCoreImageThumbnail", BindingFlags.Instance | BindingFlags.NonPublic)!;
                foreach (string? invalid in new string?[] { null, "", "   ", Path.Combine(root, "missing-image.png") })
                {
                    image.Source = source;
                    update.Invoke(owner, new object?[] { invalid });
                    if (image.Source != null) throw new InvalidOperationException("Empty/missing input must clear stale thumbnail.");
                }
                var panel = new Grid();
                typeof(SettingsWindow).GetField("CustomCoreImagePanel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(owner, panel);
                var preview = typeof(SettingsWindow).GetMethod("UpdateCoreIconPreviewUI", BindingFlags.Instance | BindingFlags.NonPublic)!;
                ConfigManager.CurrentConfig.CoreCustomImagePath = input;
                foreach (string mode in new[] { "Exit", "Custom" })
                {
                    ConfigManager.CurrentConfig.CoreIconType = "Image";
                    preview.Invoke(owner, null);
                    if (image.Source is not BitmapSource visible || visible.PixelWidth > 128 || visible.PixelHeight > 128 || panel.Visibility != System.Windows.Visibility.Visible)
                        throw new InvalidOperationException("Image mode must show a bounded preview.");
                    ConfigManager.CurrentConfig.CoreIconType = mode;
                    preview.Invoke(owner, null);
                    if (image.Source != null || panel.Visibility != System.Windows.Visibility.Collapsed || ConfigManager.CurrentConfig.CoreCustomImagePath != input)
                        throw new InvalidOperationException("Non-image mode must release preview and retain saved image path.");
                }
                Console.WriteLine("PASS empty/invalid clear and Image-to-Exit/Custom mode transitions");
            }
            Console.WriteLine($"PASS production loader respects {budget}px budget; background-helper covers decoder, not full wheel rendering"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 2; }
    }
}
