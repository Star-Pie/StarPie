using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows;
using WinPieGestures;

internal static class Program
{
    private static int _checks;
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
        _checks++;
    }
    private static void WrongScope(Action action, string message)
    {
        try { action(); }
        catch (PortableConfigBundleException ex) when (ex.ReasonCode == PortableConfigBundleReasonCodes.UnsupportedFormat)
        { Check(true, message); return; }
        throw new InvalidOperationException(message);
    }

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length != 2 || args[0] != "--candidate") throw new ArgumentException("--candidate <StarPie.dll>");
        var root = Directory.CreateTempSubdirectory("starpie-studio-profile-");
        try
        {
            Environment.SetEnvironmentVariable("APPDATA", root.FullName);
            Environment.SetEnvironmentVariable("LOCALAPPDATA", root.FullName);
            RunChecks(args, root);
        }
        finally { root.Delete(true); }
    }

    private static void EnsureApp()
    {
        ConfigManager.DisableAutoStartSync = true;
        if (Application.Current != null) return;
        var app = new App();
        app.InitializeComponent();
    }

    private static void CheckPicker()
    {
        EnsureApp();
        foreach (var language in new[] { LanguageCode.ZhCn, LanguageCode.ZhTw, LanguageCode.En, LanguageCode.Ja })
        {
            I18n.CurrentLanguage = language;
            var picker = new IconPickerWindow("Command", forProfile: true, applicationIconPath: @"C:\Fixture\Legacy.ico");
            try
            {
                var actions = (WrapPanel)picker.FindName("ProfileIconSources");
                var clear = (Button)picker.FindName("ClearIconButton");
                var content = (FrameworkElement)picker.Content;
                content.Measure(new Size(580, 420));
                content.Arrange(new Rect(0, 0, 580, 420));
                Check(new WindowInteropHelper(picker).Handle == IntPtr.Zero &&
                    picker.Title.StartsWith(I18n.T("StudioRailIcon"), StringComparison.Ordinal) &&
                    actions.Visibility == Visibility.Visible && actions.Children.Count == 3 &&
                    actions.Children.OfType<Button>().All(button => button.ActualWidth > 0 && button.ActualHeight > 0) &&
                    picker.SelectedIconKey == "Command" && !picker.RestoreDefaultProfileIcon,
                    $"unshown profile picker retains selection and fits its localized source buttons in {language}");
                clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(picker.SelectedIconKey == "" && picker.SelectedApplicationIconPath == null && picker.RestoreDefaultProfileIcon,
                    $"default selection explicitly clears the vector and the prior file hint in {language}");
            }
            finally { picker.Close(); }
        }
        var actionPicker = new IconPickerWindow("Command");
        try
        {
            Check(((WrapPanel)actionPicker.FindName("ProfileIconSources")).Visibility == Visibility.Collapsed &&
                actionPicker.SelectedIconKey == "Command", "action picker keeps its existing selection and sources");
        }
        finally { actionPicker.Close(); }
    }

    private static void RunChecks(string[] args, DirectoryInfo root)
    {
        string loaded = typeof(WheelProfile).Assembly.Location;
        static string Hash(string p) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)));
        string hash = Hash(loaded);
        Check(hash == Hash(args[1]), "executed DLL matches candidate " + hash);
        Check(!Directory.Exists(StudioProfileLibrary.IconDirectory), "resolving the profile transfer icon directory creates nothing");
        var legacy = JsonSerializer.Deserialize<WheelProfile>("{}")!;
        Check(legacy.Tags.Count == 0 && JsonSerializer.Deserialize<WheelProfile>("""{"Tags":null}""")!.Tags.Count == 0,
            "missing and null tags keep legacy profiles readable");
        var p = new WheelProfile { ProcessName = "diagram", DisplayName = "Engineering", BoundProcesses = "cad work.exe",
            HasExplicitProcessBindings = true, Tags = StudioProfileLibrary.ParseTags(" #Design；CAD, #design,, 模型") };
        Check(p.Tags.SequenceEqual(new[] { "Design", "CAD", "模型" }), "tag editor trims and de-duplicates");
        Check(StudioProfileLibrary.Matches(p, "engineer #cad", null) && StudioProfileLibrary.Matches(p, "WORK.EXE", "设计") == false &&
            StudioProfileLibrary.Matches(p, "Work.Exe", "cad") && StudioProfileLibrary.Matches(p, "模型", null), "name, process, hashtag and filter intersect");
        Check(!StudioProfileLibrary.Matches(p, "#engineering", null) && !StudioProfileLibrary.Matches(p, "zzzz", null), "hashtags only match tags");
        var duplicate = p.Clone("copy");
        duplicate.Tags.Add("copy");
        Check(duplicate.Tags.Count == 4 && p.Tags.Count == 3, "clone has independent tag storage");
        Check(legacy.IconKey == null && legacy.ApplicationIconPath == null, "legacy profile uses the existing icon fallback");
        p.IconKey = "Command";
        Check(p.Clone("icon copy").IconKey == "Command" &&
            JsonSerializer.Deserialize<WheelProfile>(JsonSerializer.Serialize(p))!.IconKey == "Command", "built-in profile icon survives copy and JSON");
        var preview = new Image();
        preview.Resources["AccentPrimaryBrush"] = Brushes.DarkSlateBlue;
        Check(ProfileIconPresentation.TrySetVector(preview, p.IconKey) &&
            preview.Source is DrawingImage { Drawing: GeometryDrawing { Geometry.IsFrozen: true } }, "built-in icon renders as frozen vector geometry");
        var drawing = (GeometryDrawing)((DrawingImage)preview.Source).Drawing;
        Check(drawing.Brush == Brushes.DarkSlateBlue, "vector uses the theme accent");
        preview.Resources["AccentPrimaryBrush"] = Brushes.White;
        Check(drawing.Brush == Brushes.White, "vector updates when the accent changes");
        Check(!ProfileIconPresentation.TrySetVector(new Image(), "custom:missing") &&
            !Directory.Exists(StudioProfileLibrary.IconDirectory), "missing SVG falls back without creating the resource directory");
        Check(JsonSerializer.Deserialize<WheelProfile>(JsonSerializer.Serialize(p))!.Tags.SequenceEqual(p.Tags), "tags survive configuration JSON");
        p.Layers[0].Actions[0].IconKey = "custom:fixture";
        p.Layers[0].FixedRings = new FixedRingMenu();
        p.Layers[0].FixedRings!.SetSlot(5, 3, new FixedRingSlot { ParentIndex = -1, Action = new ActionItem { Name = "Deep icon", IconKey = "custom:deep" } });
        static TriggerConfig Key(uint vk) => new() { TriggerType = "Keyboard", Key = "Fixture", VkCode = vk };
        var incoming = new WheelProfile { ProcessName = "incoming", BoundProcesses = "cad.exe", HasExplicitProcessBindings = true,
            Layers = [new WheelLayer { ActivationTrigger = Key(0x7c) }, new WheelLayer { ActivationTrigger = Key(0x7c) }] };
        var live = new AppConfig { Profiles = [] };
        Check(ProfileTriggerRouter.ValidateBindings(live, incoming) == ProfileTriggerValidationError.None &&
            StudioProfileLibrary.HasImportConflict(live, incoming) && live.Profiles.Count == 0,
            "prospective import detects duplicate dedicated keys inside detached profile without altering live settings");
        incoming.Layers[1].ActivationTrigger = Key(0x7d);
        Check(!StudioProfileLibrary.HasImportConflict(live, incoming), "distinct keys on incoming layers are accepted");
        live.Profiles.Add(new WheelProfile { ProcessName = "existing", BoundProcesses = "CAD.EXE", HasExplicitProcessBindings = true,
            Layers = [new WheelLayer { ActivationTrigger = Key(0x7e) }] });
        Check(StudioProfileLibrary.HasImportConflict(live, incoming), "profiles with independent layer keys still compete for the ordinary application route");
        incoming.ActivationTrigger = Key(0x7f);
        Check(!StudioProfileLibrary.HasImportConflict(live, incoming), "different dedicated routes can share an application with its ordinary profile");
        EnsureApp();
        var rail = new MappingsProfileRail();
        var config = new AppConfig { Profiles = new() { p, new WheelProfile { ProcessName = "other" } }, ConfigMode = "Pro" };
        rail.Refresh(config, p);
        var cards = (ItemsControl)rail.FindName("Cards");
        var search = (TextBox)rail.FindName("Search");
        Check(cards.Items.Count == 2, "unshown rail populates both cards");
        search.Text = "#cad";
        Check(cards.Items.Count == 1, "typing immediately filters bound cards");
        search.Text = "#missing";
        Check(cards.Items.Count == 0 && ((TextBlock)rail.FindName("NoMatch")).Visibility == Visibility.Visible,
            "no-match state leaves the editing profile intact");
        var identity = new StudioProfileIdentity();
        identity.Refresh(p);
        var identityIcon = (Image)identity.FindName("AppImage");
        Check(identityIcon.Source is DrawingImage &&
            ((TextBlock)identity.FindName("Fallback")).Visibility == Visibility.Collapsed, "inspector shows the chosen profile icon");
        p.IconKey = null;
        identity.Refresh(p);
        Check(identityIcon.Source == null && ((TextBlock)identity.FindName("Fallback")).Visibility == Visibility.Visible,
            "clearing profile icon immediately restores the fallback");
        p.IconKey = "custom:profile";
        var name = (TextBox)identity.FindName("NameEntry");
        var tags = (TextBox)identity.FindName("TagsEntry");
        name.Text = "draft";
        tags.Text = "";
        identity.Refresh(p);
        bool committed = false;
        identity.EditingCompleted += (owner, label, values) => committed = ReferenceEquals(owner, p) && label == "draft" && values == "";
        ((Button)identity.FindName("Apply")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(committed && p.DisplayName == "Engineering", "identity preserves draft on refresh and emits an explicit commit with empty tags");
        identity.Refresh(config.Profiles[1]);
        Check(name.Text == "other", "changing profile discards the previous draft");
        string snapshot = StudioProfileLibrary.SerializeProfile(p);
        using (var json = JsonDocument.Parse(snapshot))
            Check(json.RootElement.EnumerateObject().Count() == 1 && json.RootElement.GetProperty("Profiles").GetArrayLength() == 1,
                "profile snapshot contains only one card");
        foreach (var invalid in new[] { """{"Profiles":[]}""", """{"Profiles":[{},{}]}""", """{"Profiles":[{"ProcessName":"x"}],"Language":"en"}""", """{"Profiles":[{"ProcessName":"x","SectorCount":2147483647}]}""" })
        {
            try { StudioProfileLibrary.ReadProfile(invalid); }
            catch (InvalidDataException) { _checks++; continue; }
            throw new Exception("invalid profile envelope was accepted");
        }
        foreach (var layer in new[] {
            """{"Actions":[{"SubActions":[null]}]}""",
            """{"CenterAction":{"SubActions":[{"SubActions":[null]}]}}""",
            """{"FixedRings":{"Tiers":null}}""",
            """{"FixedRings":{"Tiers":[null,{"Slots":null}]}}""",
            """{"FixedRings":{"Tiers":[{"Slots":[null,{"Action":null}]}]}}"""
        })
        {
            try { StudioProfileLibrary.ReadProfile("""{"Profiles":[{"ProcessName":"x","Layers":[""" + layer + "]}]}"); }
            catch (InvalidDataException) { _checks++; continue; }
            throw new InvalidOperationException("malformed action structure accepted " + layer);
        }
        string input = Directory.CreateDirectory(Path.Combine(root.FullName, "source")).FullName;
        string output = Path.Combine(root.FullName, "target");
        File.WriteAllText(Path.Combine(input, "fixture.svg"), """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path fill="currentColor" d="M0 0 L10 0 L10 10 Z"/></svg>""");
        File.Copy(Path.Combine(input, "fixture.svg"), Path.Combine(input, "deep.svg"));
        File.Copy(Path.Combine(input, "fixture.svg"), Path.Combine(input, "profile.svg"));
        File.Copy(Path.Combine(input, "fixture.svg"), Path.Combine(input, "unreferenced.svg"));
        string single = Path.Combine(root.FullName, "library.starpie-config");
        string whole = Path.Combine(root.FullName, "whole.starpie-config");
        PortableConfigBundle.ExportProfile(snapshot, single, input);
        PortableConfigBundle.Export(snapshot, whole, input);
        using (var archive = ZipFile.OpenRead(single))
            Check(archive.Entries.Count(e => e.FullName.EndsWith(".svg")) == 3, "only referenced SVGs included, including the profile and fifth ring; repeated root/layer keys deduplicated");
        WrongScope(() => PortableConfigBundle.PrepareImport(single, output), "whole-config import rejects single-profile package");
        WrongScope(() => PortableConfigBundle.PrepareProfileImport(whole, output), "profile import rejects whole-config package");
        Check(!Directory.Exists(output), "wrong scope created no resources");
        using (var pending = PortableConfigBundle.PrepareProfileImport(single, output))
        {
            var imported = StudioProfileLibrary.ReadProfile(pending.ConfigJson);
            Check(imported.IconKey?.StartsWith("custom:profile_portable_", StringComparison.Ordinal) == true &&
                File.Exists(Path.Combine(output, imported.IconKey["custom:".Length..] + ".svg")), "profile package remaps the selected SVG and installs its bytes");
            string? deepKey = imported.Layers[0].FixedRings?.GetSlot(5, 3)?.Action.IconKey;
            Check(deepKey?.StartsWith("custom:deep_portable_", StringComparison.Ordinal) == true &&
                File.Exists(Path.Combine(output, deepKey["custom:".Length..] + ".svg")), "fifth-ring icon survives package import under a remapped name");
            string? key = imported.Layers[0].Actions[0].IconKey;
            Check(imported.Tags.SequenceEqual(p.Tags) && key?.StartsWith("custom:fixture_portable_", StringComparison.Ordinal) == true &&
                File.Exists(Path.Combine(output, key["custom:".Length..] + ".svg")), "import retains tags and remaps SVG");
        }
        Check(!Directory.Exists(output), "cancelled profile import rolls resources back");
        using (var importedWhole = PortableConfigBundle.PrepareImport(whole, StudioProfileLibrary.IconDirectory))
        {
            var imported = StudioProfileLibrary.ReadProfile(importedWhole.ConfigJson);
            var importedImage = new Image();
            Check(imported.IconKey?.StartsWith("custom:profile_portable_", StringComparison.Ordinal) == true &&
                ProfileIconPresentation.TrySetVector(importedImage, imported.IconKey) && importedImage.Source is DrawingImage,
                "full configuration import remaps the profile SVG and renders it from the shared icon library");
        }
        Check(StudioProfileLibrary.BuildExportFileName("chrome.exe") == "chrome.exe.starpie-config" &&
            StudioProfileLibrary.BuildExportFileName("code.exe") == "code.exe.starpie-config" &&
            StudioProfileLibrary.BuildExportFileName("WPS 文字") == "WPS 文字.starpie-config" &&
            StudioProfileLibrary.BuildExportFileName("Blender 4.2") == "Blender 4.2.starpie-config" &&
            StudioProfileLibrary.BuildExportFileName("demo.starpie-config") == "demo.starpie-config" &&
            StudioProfileLibrary.BuildExportFileName("") == "StarPie_Profile.starpie-config" &&
            StudioProfileLibrary.EnsureExportPackagePath(@"C:\tmp\chrome.exe") == @"C:\tmp\chrome.exe.starpie-config" &&
            StudioProfileLibrary.EnsureExportPackagePath(@"C:\tmp\ok.starpie-config") == @"C:\tmp\ok.starpie-config",
            "export filenames always preserve .starpie-config extension even when profile name ends with .exe");

        var stashProfile = new WheelProfile { ProcessName = "stash", DisplayName = "Stash", SectorCount = 12 };
        stashProfile.EnsureLayers();
        var stashLayer = stashProfile.Layers[0];
        stashLayer.SectorCount = 12;
        stashLayer.Actions = Enumerable.Range(0, 12).Select(i => new ActionItem
        {
            Type = "Hotkey",
            Name = $"S12_{i}",
            Parameter = $"Ctrl+Alt+F{i + 1}",
            IconKey = i == 11 ? "custom:stash" : ""
        }).ToList();
        stashProfile.SyncRootPropertiesFromActiveLayer();

        Func<List<ActionItem>?, int, int, Dictionary<ActionItem, int>?, List<ActionItem>> migrateForTest =
            (actions, oldCount, newCount, _) => SettingsWindow.MigrateActionsBetweenSectorCounts(actions, oldCount, newCount);

        var to4 = WheelLayer.SwitchSectorCountWithSnapshots(
            stashLayer, stashLayer.Actions, 12, 4, migrateForTest, out _);
        stashLayer.SectorCount = 4;
        stashLayer.Actions = to4;
        Check(to4.Count == 4 && to4[0].Name == "S12_0" && to4[1].Name == "S12_3" && to4[2].Name == "S12_6" && to4[3].Name == "S12_9",
            "switching 12 -> 4 maps cardinal actions while stashing 12-sector snapshot");

        var to8 = WheelLayer.SwitchSectorCountWithSnapshots(
            stashLayer, stashLayer.Actions, 4, 8, migrateForTest, out _);
        stashLayer.SectorCount = 8;
        stashLayer.Actions = to8;
        var backTo12 = WheelLayer.SwitchSectorCountWithSnapshots(
            stashLayer, stashLayer.Actions, 8, 12, migrateForTest, out _);
        stashLayer.SectorCount = 12;
        stashLayer.Actions = backTo12;
        Check(backTo12.Count == 12 &&
            Enumerable.Range(0, 12).All(i => backTo12[i].Name == $"S12_{i}" && backTo12[i].Parameter == $"Ctrl+Alt+F{i + 1}") &&
            backTo12[11].IconKey == "custom:stash",
            "switching 12 -> 4 -> 8 -> 12 losslessly restores all 12 actions and custom icon");

        var clonedStash = stashProfile.Clone("stash-copy");
        clonedStash.Layers[0].SectorCountSnapshots!["4"][0].Name = "MUTATED";
        Check(stashProfile.Layers[0].SectorCountSnapshots!["4"][0].Name == "S12_0",
            "cloning profile deep-clones SectorCountSnapshots independently");
        var backTo4BeforeExport = WheelLayer.SwitchSectorCountWithSnapshots(
            stashLayer, stashLayer.Actions, 12, 4, migrateForTest, out _);
        stashLayer.SectorCount = 4;
        stashLayer.Actions = backTo4BeforeExport;
        stashProfile.SyncRootPropertiesFromActiveLayer();
        var roundtrippedStash = StudioProfileLibrary.ReadProfile(StudioProfileLibrary.SerializeProfile(stashProfile));
        Check(roundtrippedStash.Layers[0].SectorCountSnapshots != null &&
            roundtrippedStash.Layers[0].SectorCountSnapshots!.TryGetValue("12", out var round12) &&
            round12.Count == 12 && round12[11].IconKey == "custom:stash",
            "SectorCountSnapshots survives StudioProfileLibrary JSON roundtrip");

        string stashInput = Path.Combine(root.FullName, "stash-icons");
        string stashOutput = Path.Combine(root.FullName, "stash-out");
        Directory.CreateDirectory(stashInput);
        File.WriteAllText(Path.Combine(stashInput, "stash.svg"), """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path fill="currentColor" d="M0 0 L10 0 L10 10 Z"/></svg>""");
        string stashPkg = Path.Combine(root.FullName, "stash.exe.starpie-config");
        PortableConfigBundle.ExportProfile(StudioProfileLibrary.SerializeProfile(stashProfile), stashPkg, stashInput);
        using (var stashPending = PortableConfigBundle.PrepareProfileImport(stashPkg, stashOutput))
        {
            var importedStash = StudioProfileLibrary.ReadProfile(stashPending.ConfigJson);
            string? remappedStashKey = importedStash.Layers[0].SectorCountSnapshots?["12"]?[11]?.IconKey;
            Check(remappedStashKey?.StartsWith("custom:stash_portable_", StringComparison.Ordinal) == true &&
                File.Exists(Path.Combine(stashOutput, remappedStashKey["custom:".Length..] + ".svg")),
                "portable profile bundle exports and remaps custom SVG inside stashed SectorCountSnapshots");
        }

        foreach (var badSnapLayer in new[] {
            """{"SectorCount":4,"SectorCountSnapshots":{"6":[{"Type":"Hotkey","Name":"x"}]}}""",
            """{"SectorCount":4,"SectorCountSnapshots":{"4":[]}}""",
            """{"SectorCount":4,"SectorCountSnapshots":{"4":[null,{"Type":"Hotkey","Name":"a"},{"Type":"Hotkey","Name":"b"},{"Type":"Hotkey","Name":"c"}]}}"""
        })
        {
            try { StudioProfileLibrary.ReadProfile("""{"Profiles":[{"ProcessName":"x","Layers":[""" + badSnapLayer + "]}]}"); }
            catch (InvalidDataException) { _checks++; continue; }
            throw new InvalidOperationException("malformed SectorCountSnapshots accepted " + badSnapLayer);
        }

        CheckPicker();
        Console.WriteLine($"{_checks} checks passed.");
    }
}
