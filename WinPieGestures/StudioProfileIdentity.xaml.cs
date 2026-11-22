using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace WinPieGestures;

public partial class StudioProfileIdentity : UserControl
{
    private WheelProfile? _profile;
    private string _originalName = "";
    private string _originalTags = "";
    private bool _updating;
    private int _iconGeneration;
    private string? _iconKey;
    internal event Action<WheelProfile,string,string>? EditingCompleted;
    internal event Action<WheelProfile>? IconRequested;
    internal Func<WheelProfile,Task<BitmapSource?>>? LoadIcon { get; set; }

    public StudioProfileIdentity() => InitializeComponent();

    internal void Refresh(WheelProfile? profile, bool accepted = false)
    {
        IsEnabled = profile != null;
        IconButton.ToolTip = I18n.T("StudioRailIcon");
        AutomationProperties.SetName(IconButton, I18n.T("StudioRailIcon"));
        NameEntry.ToolTip = I18n.T("ProfileRenameTitle");
        AutomationProperties.SetName(NameEntry, I18n.T("ProfileRenameTitle"));
        TagsLabel.Text = I18n.T("StudioRailTags");
        TagsEntry.ToolTip = I18n.T("StudioRailTagPrompt");
        AutomationProperties.SetName(TagsEntry, TagsLabel.Text);
        Apply.Content = I18n.T("BtnApply");
        string name = profile?.FriendlyName ?? "";
        string tags = profile == null ? "" : string.Join(", ", profile.Tags);
        bool selectionChanged = !ReferenceEquals(profile, _profile);
        _updating = true;
        try
        {
            if (accepted || selectionChanged || _originalName != name) NameEntry.Text = name;
            if (accepted || selectionChanged || _originalTags != tags) TagsEntry.Text = tags;
            _profile = profile;
            _originalName = name;
            _originalTags = tags;
        }
        finally { _updating = false; }
        SyncApply();
        string? key = profile == null ? null : profile.IconKey + "|" + profile.ApplicationIconPath + "|" + profile.BoundProcesses + "|" + profile.ProcessName;
        if (selectionChanged || key != _iconKey)
        {
            _iconKey = key;
            AppImage.Source = null;
            Fallback.Text = profile == null ? "" : MappingsProfileRail.GlyphFor(profile);
            Fallback.Visibility = Visibility.Visible;
            int generation = ++_iconGeneration;
            if (ProfileIconPresentation.TrySetVector(AppImage, profile?.IconKey))
                Fallback.Visibility = Visibility.Collapsed;
            else
                _ = RefreshIconAsync(profile, generation);
        }
    }

    private async Task RefreshIconAsync(WheelProfile? profile, int generation)
    {
        if (profile == null || LoadIcon == null) return;
        var icon = await LoadIcon(profile);
        if (generation != _iconGeneration || icon == null) return;
        AppImage.Source = icon;
        Fallback.Visibility = Visibility.Collapsed;
    }

    private void DraftChanged(object sender, TextChangedEventArgs e)
    {
        if (!_updating && Apply != null) SyncApply();
    }
    private void SyncApply() => Apply.Visibility =
        NameEntry.Text != _originalName || TagsEntry.Text != _originalTags ? Visibility.Visible : Visibility.Collapsed;
    private void CommitOnEnter(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Commit(); e.Handled = true; }
    }
    private void Commit()
    {
        if (_profile == null) return;
        EditingCompleted?.Invoke(_profile, NameEntry.Text, TagsEntry.Text);
    }
    private void ApplyClick(object sender, RoutedEventArgs e) => Commit();
    private void IconClick(object sender, RoutedEventArgs e)
    {
        if (_profile != null) IconRequested?.Invoke(_profile);
    }
}
