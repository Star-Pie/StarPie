using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using WinPieGestures;

internal static partial class Program
{
    private static void ExerciseFixedLayoutChoices()
    {
        static WheelLayer Legacy(int depth) => new()
        {
            MaxMenuDepth = depth, SectorCount = 4,
            Actions = [new() { Type = "Text", Name = "root", SubActions = [new()
                { Type = "Text", Name = "child", SubActions = [new() { Name = "legacy leaf", Type = "Text", Parameter = "kept" }] }] }]
        };
        var deep = Legacy(4);
        var paused = Legacy(5);
        paused.FixedRingsPaused = true;
        var shallow = Legacy(2);
        var untouched = Legacy(3);
        var profile = new WheelProfile { ProcessName = "Global", Layers = [deep, paused, shallow, untouched] };
        profile.EnsureLayers();
        ConfigManager.CurrentConfig.Profiles = [profile];
        ConfigManager.CurrentConfig.SubmenuStyle = "Fan";
        var window = new SettingsWindow();
        var style = (ComboBox)window.FindName("SubmenuStyleComboBox");
        var layers = (ComboBox)window.FindName("LayerSelectComboBox");
        var depth = (ComboBox)window.FindName("MenuDepthPicker");
        var notice = (TextBlock)window.FindName("Tab1_SubDimensionsNoticeText");
        string original = JsonSerializer.Serialize(deep);
        var refresh = typeof(SettingsWindow).GetMethod("RefreshMenuDepth", BindingFlags.Instance | BindingFlags.NonPublic)!;
        long revision = ConfigManager.ConfigurationRevision;
        refresh.Invoke(window, [profile]);
        layers.SelectedIndex = 3;
        layers.SelectedIndex = 0;
        Check(profile.Layers.All(l => l.FixedRings == null) && ConfigManager.ConfigurationRevision == revision &&
            JsonSerializer.Serialize(deep) == original, "opening, refreshing and browsing legacy deep Fan layers preserves their layout and configuration revision");
        style.SelectedIndex = 0;
        style.SelectedIndex = 1;
        Check(deep.ActiveFixedRings is { AlwaysVisible: false } menu &&
            menu.Tiers[0]!.Slots.Any(s => s?.Action is { Name: "legacy leaf", Parameter: "kept" } && s.ParentIndex == 0) &&
            JsonSerializer.Serialize(deep.Actions) == JsonSerializer.Serialize(LegacyActions(original)) &&
            profile.Layers.Skip(1).All(l => l.FixedRings == null),
            "explicit Fan selection on an existing four-level legacy tree creates only that layer's progressive honeycomb");
        var geometry = (StackPanel)window.FindName("LegacySubGeometryPanel");
        var font = (Slider)window.FindName("SubWheelFontSizeSlider");
        var icon = (Slider)window.FindName("SubWheelIconSizeSlider");
        ((RadioButton)window.FindName("Tier2ConfigSegmentRadio")).IsChecked = true;
        Check(geometry.Visibility == Visibility.Collapsed && !geometry.IsAncestorOf(font) && !geometry.IsAncestorOf(icon) &&
            font.Visibility == Visibility.Visible && icon.Visibility == Visibility.Visible &&
            notice.Text == I18n.T("FixedHoneycombDimensions") && I18n.AllTranslations("FixedHoneycombDimensions").Distinct().Count() == 4,
            "compiled appearance panel hides legacy geometry, retains secondary typography and icons, and explains shared grid dimensions in four languages");
        var migrated = JsonSerializer.Serialize(deep);
        window.ExecuteUndo();
        Check(JsonSerializer.Serialize(deep) == original && ConfigManager.CurrentConfig.SubmenuStyle == "Wheel" &&
            style.SelectedIndex == 0 && geometry.Visibility == Visibility.Visible, "undo of Fan selection restores the global style, legacy geometry controls and exact original tree");
        window.ExecuteRedo();
        Check(JsonSerializer.Serialize(deep) == migrated && style.SelectedIndex == 1 && geometry.Visibility == Visibility.Collapsed,
            "redo restores the exact migrated menu and geometry controls");
        layers.SelectedIndex = 1;
        style.SelectedIndex = 0;
        style.SelectedIndex = 1;
        depth.SelectedIndex = 2;
        Check(paused.FixedRingsPaused && paused.FixedRings == null && geometry.Visibility == Visibility.Visible,
            "explicit Fan and deep depth changes respect an explicitly paused legacy layer");
        layers.SelectedIndex = 2;
        style.SelectedIndex = 0;
        style.SelectedIndex = 1;
        Check(shallow.FixedRings == null && geometry.Visibility == Visibility.Visible, "the two-level Fan keeps its independent geometry");
        layers.SelectedIndex = 3;
        Check(untouched.FixedRings == null, "selecting a different deep layer under Fan does not migrate it");
        depth.SelectedIndex = 3;
        Check(untouched.ActiveFixedRings is { AlwaysVisible: false } && untouched.MaxMenuDepth == 5 &&
            geometry.Visibility == Visibility.Collapsed, "explicit three-to-five depth change under Fan creates a progressive grid");
        window.ExecuteUndo();
        Check(untouched.FixedRings == null && untouched.MaxMenuDepth == 3 && style.SelectedIndex == 1 &&
            geometry.Visibility == Visibility.Visible, "depth undo restores legacy three-level Fan without changing the selected style");
        window.ExecuteRedo();
        Check(untouched.MaxMenuDepth == 5 && untouched.ActiveFixedRings?.AlwaysVisible == false,
            "depth redo restores the migrated fifth level");
        static List<ActionItem> LegacyActions(string snapshot) =>
            JsonSerializer.Deserialize<WheelLayer>(snapshot)!.Actions;
    }
}
