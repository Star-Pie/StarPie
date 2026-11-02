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

    private bool _wheelFontListLoaded;
    private bool _coreFontListLoaded;
    private string? _wheelFontListLanguage;
    private string? _coreFontListLanguage;

    private void WheelFontFamilyComboBox_DropDownOpened(object? sender, EventArgs e)
    {
        PopulateAppearanceFontOptions(WheelFontFamilyComboBox, ConfigManager.CurrentConfig?.WheelFontFamily,
            true, ref _wheelFontListLoaded, ref _wheelFontListLanguage);
    }

    private void CoreFontFamilyComboBox_DropDownOpened(object? sender, EventArgs e)
    {
        PopulateAppearanceFontOptions(CoreFontFamilyComboBox, ConfigManager.CurrentConfig?.CoreFontFamily,
            true, ref _coreFontListLoaded, ref _coreFontListLanguage);
    }

    private void PopulateAppearanceFontOptions(ComboBox? comboBox, string? configuredFont, bool includeInstalled,
        ref bool listLoaded, ref string? listLanguage)
    {
        if (comboBox == null) return;
        string language = I18n.CurrentLanguageCode;
        if (listLanguage == language && comboBox.Items.Count > 0 && (listLoaded || !includeInstalled)) return;

        string selected = (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString()
            ?? configuredFont ?? "Microsoft YaHei UI, Segoe UI";
        bool wasUpdating = _isUpdatingUi;
        _isUpdatingUi = true;
        try
        {
            comboBox.Items.Clear();
            var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var preset in GetStandardFontFamilies())
            {
                comboBox.Items.Add(new ComboBoxItem { Content = preset.Item1, Tag = preset.Item2,
                    FontFamily = new FontFamily(preset.Item2) });
                sources.Add(preset.Item2);
                sources.Add(preset.Item2.Split(',')[0].Trim());
            }
            bool installedLoaded = false;
            if (includeInstalled)
            {
                comboBox.Items.Add(new Separator());
                try
                {
                    foreach (var font in Fonts.SystemFontFamilies.OrderBy(GetFontDisplayName, StringComparer.CurrentCultureIgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(font.Source) || !sources.Add(font.Source)) continue;
                        string displayName = GetFontDisplayName(font);
                        string caption = string.Equals(displayName, font.Source, StringComparison.OrdinalIgnoreCase)
                            ? "\ud83d\udd24 " + displayName : $"\ud83d\udd24 {displayName} ({font.Source})";
                        comboBox.Items.Add(new ComboBoxItem { Content = caption, Tag = font.Source, FontFamily = font });
                    }
                    installedLoaded = true;
                }
                catch
                {
                    // Keep presets/current value usable and allow another attempt on the next opening.
                }
            }
            SetComboBoxSelectedValue(comboBox, selected);
            listLoaded = installedLoaded;
            listLanguage = language;
        }
        finally { _isUpdatingUi = wasUpdating; }
    }
}
