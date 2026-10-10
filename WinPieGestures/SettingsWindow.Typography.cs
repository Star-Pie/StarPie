using System.Windows.Controls;
using System.Windows.Media;

namespace WinPieGestures;

public partial class SettingsWindow
{
    private bool _isRefreshingUiFont;
    private bool _uiFontListLoaded;

    private void ReloadUiFontOptions()
        => PopulateUiFontOptions(false);

    private void PopulateUiFontOptions(bool includeInstalled)
    {
        if (UiFontFamilyComboBox == null) return;
        _isRefreshingUiFont = true;
        try
        {
            UiFontTitle.Text = I18n.T("UiFontTitle");
            UiFontHint.Text = I18n.T("UiFontHint");
            string selected = ConfigManager.CurrentConfig.UiFontFamily ?? "System";
            UiFontFamilyComboBox.Items.Clear();
            UiFontFamilyComboBox.Items.Add(new ComboBoxItem { Content = I18n.T("UiFontSystem"), Tag = "System" });
            UiFontFamilyComboBox.Items.Add(new ComboBoxItem { Content = I18n.T("UiFontTheme"), Tag = "Theme" });
            if (includeInstalled)
            {
                foreach (var font in Fonts.SystemFontFamilies.OrderBy(GetFontDisplayName, StringComparer.CurrentCultureIgnoreCase))
                    UiFontFamilyComboBox.Items.Add(new ComboBoxItem { Content = GetFontDisplayName(font), Tag = font.Source, FontFamily = font });
            }
            if (UiTypography.IsNamedFont(selected) && !UiFontFamilyComboBox.Items.OfType<ComboBoxItem>().Any(item => item.Tag?.ToString() == selected))
                UiFontFamilyComboBox.Items.Add(new ComboBoxItem { Content = selected, Tag = selected, FontFamily = new FontFamily(selected) });
            UiFontFamilyComboBox.SelectedItem = UiFontFamilyComboBox.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), selected, StringComparison.OrdinalIgnoreCase)) ?? UiFontFamilyComboBox.Items[0];
            _uiFontListLoaded = includeInstalled;
        }
        finally { _isRefreshingUiFont = false; }
    }

    private void UiFontFamilyComboBox_DropDownOpened(object? sender, EventArgs e)
    {
        if (!_uiFontListLoaded) PopulateUiFontOptions(true);
    }

    private void UiFontFamilyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingUiFont || _isUpdatingUi || UiFontFamilyComboBox.SelectedItem is not ComboBoxItem { Tag: string family }) return;
        if (string.Equals(ConfigManager.CurrentConfig.UiFontFamily, family, StringComparison.OrdinalIgnoreCase)) return;
        ConfigManager.CurrentConfig.UiFontFamily = family;
        ArtStyles.ArtStyleService.NotifyChanged();
        ScheduleAutoSave();
    }
}
