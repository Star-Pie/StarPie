using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinPieGestures;

internal static class Program
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static int _checks;
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            string directory = Path.GetFullPath(args[0]);
            bool deferred = args.Contains("--expect-deferred");
            Check(args.Contains("--test-instance"), "test instance flag required");
            Directory.CreateDirectory(directory);
            foreach (string name in new[] { "LOCALAPPDATA", "APPDATA", "TEMP", "TMP" }) Environment.SetEnvironmentVariable(name, directory);
            string candidate = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(a => a.Key == "StarPieCandidateDir").Value!;
            string expected = Path.Combine(candidate, "StarPie.dll");
            Check(Hash(expected) == Hash(typeof(SettingsWindow).Assembly.Location), "candidate DLL hash binding");
            Check(Application.Current == null, "no Application created");
            ConfigManager.CurrentConfig.WheelFontFamily = "Segoe UI";
            ConfigManager.CurrentConfig.CoreFontFamily = "Segoe UI";
            var owner = MakeOwner();
            var wheel = Box(owner, "Wheel"); var core = Box(owner, "Core");
            Check(PresentationSource.FromVisual(wheel) == null && PresentationSource.FromVisual(core) == null, "offscreen controls have no presentation source");
            long before = GC.GetTotalMemory(true);
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            long privateBefore = process.PrivateMemorySize64;
            long workingBefore = process.WorkingSet64;
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            var timer = Stopwatch.StartNew();
            Populate(owner);
            timer.Stop();
            long initialAllocation = GC.GetAllocatedBytesForCurrentThread() - allocated;
            long liveDelta = GC.GetTotalMemory(true) - before;
            process.Refresh();
            long privateDelta = process.PrivateMemorySize64 - privateBefore;
            long workingDelta = process.WorkingSet64 - workingBefore;
            allocated = GC.GetAllocatedBytesForCurrentThread();
            var refresh = Stopwatch.StartNew();
            for (int i = 0; i < 5; i++) Populate(owner);
            refresh.Stop();
            long refreshAllocation = GC.GetAllocatedBytesForCurrentThread() - allocated;
            Console.WriteLine("METRIC " + JsonSerializer.Serialize(new { initialMs = timer.Elapsed.TotalMilliseconds,
                initialAllocatedBytes = initialAllocation, managedLiveDeltaBytes = liveDelta,
                probePrivateBytesDelta = privateDelta, probeWorkingSetDelta = workingDelta,
                refresh5Ms = refresh.Elapsed.TotalMilliseconds, refresh5AllocatedBytes = refreshAllocation,
                wheelItems = wheel.Items.Count, coreItems = core.Items.Count, sha256 = Hash(expected) }));
            Check(Tag(wheel) == "Segoe UI" && Tag(core) == "Segoe UI", "initial selection preserved");
            if (deferred)
            {
                Check(wheel.Items.Count <= 16 && core.Items.Count <= 16, "unopened wheel/core pickers must not eagerly populate installed fonts");
                Behavior(owner);
                var refs = Enumerable.Range(0, 4).Select(_ => ReleasedOwner()).ToArray();
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                Check(refs.All(r => !r.IsAlive), "offscreen picker/handler cycles do not retain owners");
            }
            GC.KeepAlive(owner);
            AppLogger.Shutdown();
            Console.WriteLine($"PASS {_checks} font picker checks; no Window constructor, Application, popup, hook or actions.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine($"FAIL {_checks}: {e}"); return 1; }
    }

    private static void Behavior(SettingsWindow owner)
    {
        var wheel = Box(owner, "Wheel"); var core = Box(owner, "Core");
        // Attach the actual production selection handlers. Population must guard them:
        // otherwise Core's handler performs a full save even without user selection.
        foreach (string name in new[] { "Wheel", "Core" })
        {
            var handler = (SelectionChangedEventHandler)Delegate.CreateDelegate(typeof(SelectionChangedEventHandler), owner,
                typeof(SettingsWindow).GetMethod(name + "FontFamilyComboBox_SelectionChanged", Instance)!);
            Box(owner, name).SelectionChanged += handler;
        }
        Set(owner, "_isUpdatingUi", false);
        long revision = ConfigManager.ConfigurationRevision;
        string saved = JsonSerializer.Serialize(ConfigManager.CurrentConfig);
        string selected = "StarPie Probe Missing Family, Segoe UI";
        Set(owner, "_isUpdatingUi", true);
        Call(owner, "SetComboBoxSelectedValue", wheel, "Consolas");
        Call(owner, "SetComboBoxSelectedValue", core, "Consolas");
        Check(Tag(wheel) == "Consolas" && Tag(core) == "Consolas", "single family is not replaced by a matching-prefix fallback chain");
        // Programmatic refreshes use the existing selected-value path under its normal caller guard.
        Set(owner, "_isUpdatingUi", true);
        Call(owner, "SetComboBoxSelectedValue", wheel, selected);
        Call(owner, "SetComboBoxSelectedValue", core, selected);
        Set(owner, "_isUpdatingUi", false);
        Check(Tag(wheel) == selected && Tag(core) == selected, "saved/custom font remains selectable before enumerating installed fonts");
        int coreBefore = core.Items.Count;
        Open(owner, "Wheel");
        Check(core.Items.Count == coreBefore, "opening wheel does not load core");
        Check(Tag(wheel) == selected, "wheel opening preserves selected custom family");
        Check(ConfigManager.ConfigurationRevision == revision && saved == JsonSerializer.Serialize(ConfigManager.CurrentConfig), "wheel population never writes configuration");
        Check(!(bool)Get(owner, "_isUpdatingUi")!, "wheel population restores caller guard");
        Open(owner, "Core");
        Check(Tag(core) == selected && ConfigManager.ConfigurationRevision == revision, "core population preserves selection without save");
        foreach (var box in new[] { wheel, core })
        {
            var tags = box.Items.OfType<ComboBoxItem>().Select(i => i.Tag?.ToString() ?? "").ToArray();
            Check(tags.Length == tags.Distinct(StringComparer.OrdinalIgnoreCase).Count(), "no duplicate font source rows");
            Check(tags.Contains(selected), "custom selection is retained in expanded list");
            foreach (var font in Fonts.SystemFontFamilies.Take(5))
                Check(tags.Any(t => t.Split(',')[0].Trim().Equals(font.Source, StringComparison.OrdinalIgnoreCase)), "installed family available after opening");
        }
        var item = wheel.Items[0]; int count = wheel.Items.Count;
        Open(owner, "Wheel"); Populate(owner);
        Check(wheel.Items.Count == count && ReferenceEquals(item, wheel.Items[0]), "reopening and ordinary refresh do not recreate loaded font rows");
        Check(saved == JsonSerializer.Serialize(ConfigManager.CurrentConfig), "population and refresh preserve every configuration field");
        foreach (string language in new[] { "en", "zh-CN", "zh-TW", "ja" })
        {
            bool changed = I18n.CurrentLanguageCode != language;
            I18n.SetLanguage(language); Populate(owner);
            Check(!changed || (wheel.Items.Count <= 16 && core.Items.Count <= 16), "changed language resets lists to lazy presets");
            Check(wheel.Items.OfType<ComboBoxItem>().Single(i => i.Tag?.ToString() == "Microsoft YaHei UI, Segoe UI").Content?.ToString() == I18n.T("FontSystemDefault"), "preset language updated");
            Check(Tag(wheel) == selected && Tag(core) == selected, "language refresh preserves exact custom selection");
            Open(owner, "Wheel"); Open(owner, "Core");
            Check(Tag(wheel) == selected && Tag(core) == selected, "localized opening preserves selection");
        }
        Check(ConfigManager.ConfigurationRevision == revision && saved == JsonSerializer.Serialize(ConfigManager.CurrentConfig), "language/dropdown refreshes do not persist settings");
        Check(Application.Current == null && PresentationSource.FromVisual(wheel) == null, "no presentation source was created by handlers");
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ReleasedOwner()
    {
        var owner = MakeOwner();
        foreach (string kind in new[] { "Wheel", "Core" })
        {
            Box(owner, kind).SelectionChanged += (SelectionChangedEventHandler)Delegate.CreateDelegate(typeof(SelectionChangedEventHandler), owner,
                typeof(SettingsWindow).GetMethod(kind + "FontFamilyComboBox_SelectionChanged", Instance)!);
            Box(owner, kind).DropDownOpened += (EventHandler)Delegate.CreateDelegate(typeof(EventHandler), owner,
                typeof(SettingsWindow).GetMethod(kind + "FontFamilyComboBox_DropDownOpened", Instance)!);
        }
        Populate(owner); Open(owner, "Wheel"); Open(owner, "Core");
        return new WeakReference(owner);
    }
    private static SettingsWindow MakeOwner()
    {
        var owner = (SettingsWindow)RuntimeHelpers.GetUninitializedObject(typeof(SettingsWindow));
        Set(owner, "WheelFontFamilyComboBox", new ComboBox()); Set(owner, "CoreFontFamilyComboBox", new ComboBox());
        Set(owner, "_isUpdatingUi", true); return owner;
    }
    private static void Populate(SettingsWindow owner) { Call(owner, "PopulateWheelFontFamilies"); Call(owner, "PopulateCoreFontFamilies"); }
    private static void Open(SettingsWindow owner, string kind) => Call(owner, kind + "FontFamilyComboBox_DropDownOpened", Box(owner, kind), EventArgs.Empty);
    private static ComboBox Box(SettingsWindow owner, string kind) => (ComboBox)Get(owner, kind + "FontFamilyComboBox")!;
    private static string? Tag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag?.ToString();
    private static object? Get(object owner, string name) => owner.GetType().GetField(name, Instance)!.GetValue(owner);
    private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Instance)!.SetValue(owner, value);
    private static object? Call(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, Instance)!.Invoke(owner, args);
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static void Check(bool value, string message) { _checks++; if (!value) throw new InvalidOperationException(message); }
}
