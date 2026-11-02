using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using WinPieGestures;
using WinPieGestures.Plugins;

internal static class RetentionProbe
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly List<object> RootedItems = new();
    private static int checks;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2 || args[1] != "--test-instance") throw new InvalidOperationException("Explicit isolated test invocation required.");
            var metadata = typeof(RetentionProbe).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToDictionary(x => x.Key, x => x.Value!);
            string directory = Path.GetFullPath(args[0]);
            string allowed = Path.GetFullPath(metadata["ArtifactsRoot"]).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!directory.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || Directory.Exists(directory)) throw new InvalidOperationException("Fresh owned artifact directory required.");
            foreach (string key in new[] { "LOCALAPPDATA", "APPDATA", "TEMP", "TMP" })
            {
                string child = Path.Combine(directory, key); Directory.CreateDirectory(child); Environment.SetEnvironmentVariable(key, child);
            }
            string candidate = Path.Combine(metadata["StarPieCandidateDir"], typeof(SettingsWindow).Assembly.GetName().Name + ".dll");
            string hash = Hash(candidate);
            Check(hash == Hash(typeof(SettingsWindow).Assembly.Location), "exact candidate hash binding");
            Check(Application.Current == null && App.MainMouseHook == null && App.MainKeyboardHook == null && !PluginHost.IsInitialized, "no Application, hooks or initialized plugin host");
            _ = ConfigManager.CurrentConfig; // Complete defaults under the isolated test root only.
            int pluginBefore = Subscribers(typeof(PluginHost), "PluginAvailabilityChanged");
            int languageBefore = Subscribers(typeof(I18n), "LanguageChanged");
            foreach (string mode in new[] { "plugin", "timer", "cycle", "slots" })
            {
                var references = CreateOwner(mode);
                CollectProbeOnly();
                Check(references.Owner.IsAlive, mode + " positive control: external publisher/timer/item retains owner before release");
                Release(references.Owner);
                CollectProbeOnly();
                Check(!references.Owner.IsAlive, mode + " production release permits owner collection");
                if (references.Slot != null) Check(!references.Slot.IsAlive, "production release disposes language subscription and permits slot collection");
                Check(Subscribers(typeof(PluginHost), "PluginAvailabilityChanged") == pluginBefore, "plugin event returns to initial subscriber count");
                Check(Subscribers(typeof(I18n), "LanguageChanged") == languageBefore, "language event returns to initial subscriber count");
                RootedItems.Clear();
            }
            Check(Application.Current == null && App.MainMouseHook == null && App.MainKeyboardHook == null && !PluginHost.IsInitialized, "no product Application/hooks/plugin initialization after probe");
            AppLogger.Shutdown();
            Console.WriteLine(JsonSerializer.Serialize(new { checks, CandidateDllSha256 = hash, LoadedAssembly = typeof(SettingsWindow).Assembly.Location,
                Boundary = "Uninitialized owner; production release methods only. No Window constructor/InitializeComponent/Show, Application, dispatcher pump, live target attachment, plugin actions or input. GC affects this disposable probe only." }));
            Console.WriteLine($"PASS {checks} retention path checks; not full WPF window lifetime or live heap proof.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Owner, WeakReference? Slot) CreateOwner(string mode)
    {
        var owner = (SettingsWindow)RuntimeHelpers.GetUninitializedObject(typeof(SettingsWindow));
        Set(owner, "_lifetimeCts", new CancellationTokenSource());
        var slots = new ObservableCollection<SlotViewModel>(); Set(owner, "_slotViewModels", slots);
        var cycleField = typeof(SettingsWindow).GetField("_cycleItems", Instance)!;
        var cycleItems = (IList)Activator.CreateInstance(cycleField.FieldType)!; cycleField.SetValue(owner, cycleItems);
        WeakReference? slotReference = null;
        if (mode is "plugin" or "slots")
        {
            var handler = (Action)Delegate.CreateDelegate(typeof(Action), owner, Method("HandlePluginAvailabilityChanged"));
            PluginHost.PluginAvailabilityChanged += handler;
        }
        if (mode == "timer")
        {
            // Actual 30s timer, stopped synchronously below; no DispatcherFrame is pumped.
            Method("ScheduleDeferredClose").Invoke(owner, null);
        }
        if (mode == "cycle")
        {
            Type itemType = typeof(SettingsWindow).GetNestedType("LayoutCycleItem", BindingFlags.NonPublic)!;
            object item = Activator.CreateInstance(itemType, "probe", "probe", false)!;
            var handler = (PropertyChangedEventHandler)Delegate.CreateDelegate(typeof(PropertyChangedEventHandler), owner, Method("LayoutCycleItem_PropertyChanged"));
            itemType.GetEvent("PropertyChanged")!.AddEventHandler(item, handler);
            cycleItems.Add(item); RootedItems.Add(item);
        }
        if (mode == "slots")
        {
            var slot = new SlotViewModel(0, 8, "probe", new ActionItem { Type = "Hotkey", Name = "probe", Parameter = "" });
            slots.Add(slot); slotReference = new WeakReference(slot);
        }
        return (new WeakReference(owner), slotReference);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Release(WeakReference reference)
    {
        object owner = reference.Target ?? throw new InvalidOperationException("Positive control owner was lost.");
        Method("ReleaseWindowResources").Invoke(owner, null);
        Check((bool)typeof(SettingsWindow).GetField("_resourcesReleased", Instance)!.GetValue(owner)!, "production release flag set");
        Method("ReleaseWindowResources").Invoke(owner, null); // Repeated release must not cancel a disposed CTS.
    }

    private static MethodInfo Method(string name) => typeof(SettingsWindow).GetMethod(name, Instance)!;
    private static void Set(object owner, string name, object value) => typeof(SettingsWindow).GetField(name, Instance)!.SetValue(owner, value);
    private static int Subscribers(Type type, string name) => (type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null) as Delegate)?.GetInvocationList().Length ?? 0;
    private static string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks++; Console.WriteLine("PASS " + message); }
    private static void CollectProbeOnly() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
}
