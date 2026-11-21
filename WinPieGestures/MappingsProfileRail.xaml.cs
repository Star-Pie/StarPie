using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;

namespace WinPieGestures;

internal enum StudioRailAction
{
    Select, Copy, Delete, Export, Import, AddFromProgram, AddFromWindow, AddFromFile, AddCustom,
    SelectLayer, AddLayer, RenameLayer, CopyLayer, DeleteLayer
}

public partial class MappingsProfileRail : UserControl
{
    private AppConfig? _config;
    private WheelProfile? _selection;
    private string? _tag;
    private CardRow[] _visibleCards = Array.Empty<CardRow>();
    private LanguageCode? _displayLanguage;
    private sealed record CardRow(WheelProfile Profile, bool Selected, bool ShowLayers, string Glyph,
        string Summary, string TagCaption, string IconKey, LayerRow[] Layers)
    {
        public bool CanDelete => !string.Equals(Profile.ProcessName, "Global", StringComparison.OrdinalIgnoreCase);
        public bool HasTags => TagCaption.Length > 0;
        public string Export => I18n.T("StudioRailExport");
        public string Copy => I18n.T("StudioRailCopy");
        public string Delete => I18n.T("StudioRailDelete");
        public string AddLayer => I18n.T("AddLayer");
    }
    private sealed record LayerRow(WheelProfile Profile, WheelLayer Layer, bool Selected, string Caption)
    {
        public string Actions => I18n.T("StudioRailLayerMenu");
    }
    internal event Action<StudioRailAction, WheelProfile?, WheelLayer?>? Requested;
    internal Func<WheelProfile,Task<BitmapSource?>>? LoadIcon { get; set; }

    public MappingsProfileRail() => InitializeComponent();

    internal static string GlyphFor(WheelProfile p) =>
        string.Equals(p.ProcessName, "Global", StringComparison.OrdinalIgnoreCase) ? "◎" : p.UseApplicationContext ? "▣" : "◈";

    internal void Refresh(AppConfig config, WheelProfile? selected)
    {
        _config = config;
        _selection = selected;
        Title.Text = I18n.T("StudioRailLibrary");
        Search.ToolTip = Hint.Text = I18n.T("StudioRailSearch");
        AutomationProperties.SetName(Search, Hint.Text);
        AutomationProperties.SetName(Filter, I18n.T("StudioRailTagFilter"));
        Add.Content = I18n.T("AddProfileShort");
        Import.Content = I18n.T("StudioRailImport");
        NoMatch.Text = I18n.T("StudioRailNoMatch");
        if (_tag != null && !AllTags().Contains(_tag, StringComparer.OrdinalIgnoreCase)) _tag = null;
        RenderCards();
    }

    private IEnumerable<string> AllTags() =>
        _config?.Profiles.SelectMany(p => p.Tags).Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase) ?? Enumerable.Empty<string>();

    private void RenderCards()
    {
        if (_config == null) return;
        Hint.Visibility = Search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Filter.ToolTip = _tag == null ? I18n.T("StudioRailTagFilter") : "#" + _tag;
        Filter.FontWeight = _tag == null ? FontWeights.Normal : FontWeights.Bold;
        Filter.Content = _tag == null ? "#" : "# ✓";
        bool advanced = string.Equals(_config.ConfigMode, "Pro", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(_config.ConfigMode, "Advanced", StringComparison.OrdinalIgnoreCase);
        var rows = _config.Profiles.Where(p => StudioProfileLibrary.Matches(p, Search.Text, _tag))
            .Select(p => CreateCard(p, advanced)).ToArray();
        if (_displayLanguage != I18n.CurrentLanguage || !SameRows(_visibleCards, rows))
        {
            _visibleCards = rows;
            _displayLanguage = I18n.CurrentLanguage;
            Cards.ItemsSource = rows;
        }
        NoMatch.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool SameRows(CardRow[] old, CardRow[] next)
    {
        if (old.Length != next.Length) return false;
        for (int i = 0; i < next.Length; i++)
        {
            var row = old[i];
            var updated = next[i];
            if ((row with { Layers = updated.Layers }) != updated || !row.Layers.SequenceEqual(updated.Layers)) return false;
        }
        return true;
    }

    private CardRow CreateCard(WheelProfile profile, bool advanced)
    {
        bool selected = ReferenceEquals(_selection, profile);
        bool showLayers = selected && advanced;
        var layers = profile.Layers;
        int count = layers?.Count ?? 0;
        int index = count == 0 ? -1 : Math.Clamp(profile.ActiveLayerIndex, 0, count - 1);
        var trigger = index < 0 ? null : ProfileTriggerRouter.GetLayerTrigger(profile, layers![index]);
        string mode = string.Equals(profile.ProcessName, "Global", StringComparison.OrdinalIgnoreCase)
            ? I18n.T("StudioRailGlobal") : I18n.T(profile.UseApplicationContext ? "StudioRailApp" : "ParallelProfileMode");
        var summary = (trigger == null ? mode : SettingsWindow.FormatTriggerDisplay(trigger)) + " · " +
            I18n.TF("StudioRailLayers", Math.Max(1, count));
        var layerRows = !showLayers || layers == null ? Array.Empty<LayerRow>() :
            layers.Select((l, i) => {
                string badge = ProfilePresentation.GetLayerTriggerPresentation(_config, profile, l, false).BadgeText;
                return new LayerRow(profile, l, i == index, badge + " · " + I18n.TF("StudioRailDepth", l.EffectiveMaxMenuDepth));
            }).ToArray();
        return new(profile, selected, showLayers, GlyphFor(profile), summary,
            string.Join("  ", profile.Tags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => "#" + t)), profile.ApplicationIconPath + "|" + profile.BoundProcesses, layerRows);
    }

    private void SearchChanged(object sender, TextChangedEventArgs e) => RenderCards();
    private void FilterClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        void Option(string? tag)
        {
            var item = new MenuItem { Header = tag == null ? I18n.T("StudioRailAllTags") : "#" + tag,
                IsCheckable = true, IsChecked = string.Equals(tag, _tag, StringComparison.OrdinalIgnoreCase) };
            item.Click += (_, _) => { _tag = tag; RenderCards(); };
            menu.Items.Add(item);
        }
        Option(null);
        foreach (var tag in AllTags()) Option(tag);
        OpenMenu(Filter, menu);
    }

    private void AddClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        AddMenu(menu, "AddProfileFromProgram", StudioRailAction.AddFromProgram);
        AddMenu(menu, "AddProfileCaptureWindow", StudioRailAction.AddFromWindow);
        AddMenu(menu, "AddProfileBrowseExe", StudioRailAction.AddFromFile);
        AddMenu(menu, "AddProfileCustom", StudioRailAction.AddCustom);
        OpenMenu(Add, menu);
    }

    private static void OpenMenu(Button button, ContextMenu menu)
    {
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void AddMenu(ContextMenu menu, string key, StudioRailAction action, LayerRow? row = null, bool enabled = true)
    {
        var item = new MenuItem { Header = I18n.T(key), IsEnabled = enabled };
        item.Click += (_, _) => Requested?.Invoke(action, row?.Profile, row?.Layer);
        menu.Items.Add(item);
    }

    private void LayerRightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        LayerMenuClick(sender, e);
        e.Handled = true;
    }

    private void LayerMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: LayerRow row } button) return;
        var menu = new ContextMenu();
        AddMenu(menu, "AddLayer", StudioRailAction.AddLayer, row);
        AddMenu(menu, "RenameLayerBtnText", StudioRailAction.RenameLayer, row);
        AddMenu(menu, "CopyLayerBtnText", StudioRailAction.CopyLayer, row);
        AddMenu(menu, "DeleteLayerBtnText", StudioRailAction.DeleteLayer, row, row.Profile.Layers.Count > 1);
        OpenMenu(button, menu);
    }

    private void LayerClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: LayerRow row })
            Requested?.Invoke(StudioRailAction.SelectLayer, row.Profile, row.Layer);
    }
    private void CardAction(object sender, StudioRailAction action)
    {
        if (sender is Button { Tag: WheelProfile profile }) Requested?.Invoke(action, profile, null);
    }
    private void SelectClick(object sender, RoutedEventArgs e) => CardAction(sender, StudioRailAction.Select);
    private void CopyClick(object sender, RoutedEventArgs e) => CardAction(sender, StudioRailAction.Copy);
    private void DeleteClick(object sender, RoutedEventArgs e) => CardAction(sender, StudioRailAction.Delete);
    private void ExportClick(object sender, RoutedEventArgs e) => CardAction(sender, StudioRailAction.Export);
    private void AddLayerClick(object sender, RoutedEventArgs e) => CardAction(sender, StudioRailAction.AddLayer);
    private void ImportClick(object sender, RoutedEventArgs e) => Requested?.Invoke(StudioRailAction.Import, null, null);
    private async void ImageLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Image { Tag: WheelProfile profile } image || LoadIcon == null) return;
        var icon = await LoadIcon(profile);
        if (image.IsLoaded && ReferenceEquals(profile, image.Tag)) image.Source = icon;
    }
}
