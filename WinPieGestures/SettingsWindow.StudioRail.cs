using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace WinPieGestures;

public partial class SettingsWindow
{
    private bool _studioRailInitialized;
    private bool _studioRailRefreshPending;

    private void InitializeStudioRail()
    {
        if (_studioRailInitialized || StudioProfileRail == null || StudioIdentity == null) return;
        _studioRailInitialized = true;
        StudioProfileRail.Requested += OnStudioRailRequest;
        StudioProfileRail.LoadIcon = GetCachedProfileIconAsync;
        StudioIdentity.LoadIcon = GetCachedProfileIconAsync;
        StudioIdentity.EditingCompleted += SaveStudioIdentity;
        StudioIdentity.IconRequested += SelectStudioIcon;
        StudioIdentity.IsVisibleChanged += (_, _) => RefreshStudioRail();
        StudioProfileRail.IsVisibleChanged += (_, _) => RefreshStudioRail();
        RefreshStudioRail();
    }

    private void RefreshStudioRail()
    {
        if (!_studioRailInitialized || _studioRailRefreshPending || _resourcesReleased ||
            (!StudioProfileRail.IsVisible && !StudioIdentity.IsVisible)) return;
        _studioRailRefreshPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(() => {
            _studioRailRefreshPending = false;
            if (_resourcesReleased || ConfigManager.CurrentConfig is not { } config) return;
            if (StudioProfileRail.IsVisible) StudioProfileRail.Refresh(config, _selectedProfile);
            if (StudioIdentity.IsVisible) StudioIdentity.Refresh(_selectedProfile);
        }));
    }

    private async void OnStudioRailRequest(StudioRailAction action, WheelProfile? profile, WheelLayer? layer)
    {
        if (_resourcesReleased || _isUpdatingUi || ConfigManager.CurrentConfig?.Profiles is not { } profiles) return;
        if (profile != null && !profiles.Contains(profile)) return;
        if (layer != null && profile?.Layers.Contains(layer) != true) return;
        var args = new RoutedEventArgs();
        // Exporting a hovered card never navigates away from the user's current edit.
        if (action == StudioRailAction.Export && profile != null) { await ExportStudioProfileAsync(profile); return; }
        if (action == StudioRailAction.Import) { await ImportStudioProfileAsync(); return; }
        if (profile != null && !ReferenceEquals(_selectedProfile, profile))
            MappingsProfileComboBox.SelectedItem = profile;
        if (layer != null && !ReferenceEquals(LayerSelectComboBox.SelectedItem, layer))
            LayerSelectComboBox.SelectedItem = layer;

        switch (action)
        {
            case StudioRailAction.Copy: DuplicateProfileBtn_Click(this, args); break;
            case StudioRailAction.Delete: DeleteProfileButton_Click(this, args); break;
            case StudioRailAction.AddFromProgram: AddProfileButton_Click(this, args); break;
            case StudioRailAction.AddFromWindow: AddProfileByCapture_Click(this, args); break;
            case StudioRailAction.AddFromFile: AddProfileByBrowse_Click(this, args); break;
            case StudioRailAction.AddCustom: AddCustomProfileButton_Click(this, args); break;
            case StudioRailAction.AddLayer: AddLayerBtn_Click(this, args); break;
            case StudioRailAction.CopyLayer: CopyLayerBtn_Click(this, args); break;
            case StudioRailAction.RenameLayer: RenameLayerBtn_Click(this, args); break;
            case StudioRailAction.DeleteLayer: DeleteLayerBtn_Click(this, args); break;
        }
        ShowStudioProfileInspector();
        RefreshStudioRail();
    }

    private static bool StudioNameAvailable(string name, WheelProfile? except = null) =>
        !string.IsNullOrWhiteSpace(name) && !ConfigManager.CurrentConfig.Profiles.Any(p => !ReferenceEquals(p, except) &&
            (string.Equals(name, p.ProcessName, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(name, p.FriendlyName, StringComparison.OrdinalIgnoreCase)));

    private void SaveStudioIdentity(WheelProfile profile, string name, string tags)
    {
        if (!ReferenceEquals(profile, _selectedProfile) || _isUpdatingUi) return;
        name = name.Trim();
        if (name != profile.FriendlyName && !StudioNameAvailable(name, profile))
        {
            AppMessageBox.Show(this, I18n.T("ProfileNameInvalidOrDuplicate"), I18n.T("ProfileRenameTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (name != profile.FriendlyName)
        {
            if (!profile.HasExplicitProcessBindings && !string.Equals(profile.ProcessName, "Global", StringComparison.OrdinalIgnoreCase))
            {
                profile.BoundProcesses = string.Join(", ", ProfileBindingTokens(profile));
                profile.HasExplicitProcessBindings = true;
            }
            profile.DisplayName = name;
            RefreshPortalTargetNames(profile);
        }
        profile.Tags = StudioProfileLibrary.ParseTags(tags);
        StudioIdentity.Refresh(profile, accepted: true);
        ScheduleAutoSave();
        RefreshProfilePresentation();
        RefreshStudioRail();
    }

    private void SelectStudioIcon(WheelProfile profile)
    {
        if (_resourcesReleased || _isUpdatingUi || !ReferenceEquals(profile, _selectedProfile)) return;
        var config = ConfigManager.CurrentConfig;
        var picker = new IconPickerWindow(profile.IconKey, forProfile: true,
            applicationIconPath: profile.ApplicationIconPath) { Owner = this };
        if (picker.ShowDialog() != true || _resourcesReleased || _isUpdatingUi ||
            !ReferenceEquals(profile, _selectedProfile) || !ReferenceEquals(config, ConfigManager.CurrentConfig) ||
            !config.Profiles.Contains(profile)) return;

        string? iconKey = string.IsNullOrEmpty(picker.SelectedIconKey) ? null : picker.SelectedIconKey;
        string? applicationPath = picker.RestoreDefaultProfileIcon
            ? null
            : picker.SelectedApplicationIconPath ?? profile.ApplicationIconPath;
        if (string.Equals(profile.IconKey, iconKey, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(profile.ApplicationIconPath, applicationPath, StringComparison.OrdinalIgnoreCase)) return;

        profile.IconKey = iconKey;
        profile.ApplicationIconPath = applicationPath;
        ScheduleAutoSave();
        RefreshProfilePresentation();
        RefreshStudioRail();
    }

    private async Task ExportStudioProfileAsync(WheelProfile profile)
    {
        if (_configTransferBusy) return;
        var config = ConfigManager.CurrentConfig;
        long revision = ConfigManager.ConfigurationRevision;
        var profiles = StudioProfileLibrary.PortalExportProfiles(profile, config.Profiles);
        if (profiles.Count > 1)
        {
            var include = AppMessageBox.Show(this, I18n.TF("PortalExportDependencies", profiles.Count - 1),
                I18n.T("StudioRailExport"), MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (include == MessageBoxResult.Cancel) return;
            if (include != MessageBoxResult.Yes) profiles = [profile];
        }
        var dialog = new SaveFileDialog { Filter = I18n.T("StudioRailFileType"), DefaultExt = ".starpie-config",
            AddExtension = true, FileName = ConfigManager.CleanFileName(profile.FriendlyName), Title = I18n.T("StudioRailExport") };
        if (dialog.ShowDialog(this) != true) return;
        SetConfigTransferBusy(true);
        try
        {
            if (!ReferenceEquals(config, ConfigManager.CurrentConfig) || revision != ConfigManager.ConfigurationRevision)
                throw new InvalidOperationException(I18n.T("ConfigTransferChanged"));
            string snapshot = StudioProfileLibrary.SerializeProfiles(profiles);
            string icons = StudioProfileLibrary.IconDirectory;
            await Task.Run(() => PortableConfigBundle.ExportProfile(snapshot, dialog.FileName, icons));
        }
        catch (Exception ex) { ShowConfigTransferError(ex, exporting: true); }
        finally { if (!_resourcesReleased) SetConfigTransferBusy(false); }
    }

    private async Task ImportStudioProfileAsync()
    {
        if (_configTransferBusy) return;
        var open = new OpenFileDialog { Filter = I18n.T("StudioRailFileType"), Title = I18n.T("StudioRailImport") };
        if (open.ShowDialog(this) != true) return;
        SetConfigTransferBusy(true);
        var config = ConfigManager.CurrentConfig;
        long revision = ConfigManager.ConfigurationRevision;
        try
        {
            string icons = StudioProfileLibrary.IconDirectory;
            using var transaction = await Task.Run(() => PortableConfigBundle.PrepareProfileImport(open.FileName, icons));
            var imported = await Task.Run(() => StudioProfileLibrary.ReadProfiles(transaction.ConfigJson));
            var incoming = imported[0];
            if (_resourcesReleased || !IsVisible) return;
            if (revision != ConfigManager.ConfigurationRevision || !ReferenceEquals(config, ConfigManager.CurrentConfig))
                throw new InvalidOperationException(I18n.T("ConfigTransferChanged"));
            string suggestion = incoming.FriendlyName;
            if (!StudioNameAvailable(suggestion)) suggestion = I18n.TF("CopyLayerNameFmt", suggestion);
            var naming = new InputDialog(I18n.T("StudioRailImport"), I18n.T("StudioRailImportName"), suggestion,
                v => (StudioNameAvailable(v.Trim()) && !string.Equals(v.Trim(), "Global", StringComparison.OrdinalIgnoreCase),
                    I18n.T("ProfileNameInvalidOrDuplicate"))) { Owner = this };
            if (naming.ShowDialog() != true) return;
            StudioProfileLibrary.NameImportedProfiles(imported, config.Profiles, naming.InputText.Trim());
            var projected = new AppConfig
            {
                Profiles = new(config.Profiles), Trigger = config.Trigger, BlacklistTriggerOverrides = config.BlacklistTriggerOverrides
            };
            foreach (var profile in imported)
            {
                if (StudioProfileLibrary.HasImportConflict(projected, profile))
                {
                    if (AppMessageBox.Show(this, profile.FriendlyName + Environment.NewLine + I18n.T("StudioRailConflict"), I18n.T("StudioRailImport"),
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                    profile.UseApplicationContext = true;
                    profile.BoundProcesses = "";
                    profile.ActivationTrigger = null;
                    foreach (var layer in profile.Layers) layer.ActivationTrigger = null;
                }
                projected.Profiles.Add(profile);
            }
            if (_resourcesReleased || !IsVisible) return;
            if (revision != ConfigManager.ConfigurationRevision || !ReferenceEquals(config, ConfigManager.CurrentConfig))
                throw new InvalidOperationException(I18n.T("ConfigTransferChanged"));
            if (!ConfigManager.CommitProfileImport(config, imported)) throw new IOException();
            transaction.Commit();
            IconHelper.InvalidateCustomIconCache();
            RefreshProfilesUi(incoming);
            ShowStudioProfileInspector();
            RefreshStudioRail();
        }
        catch (PortableConfigBundleException ex) when (ex.ReasonCode == PortableConfigBundleReasonCodes.UnsupportedFormat)
        {
            if (!_resourcesReleased) AppMessageBox.Show(this, I18n.T("StudioRailWrongScope"), I18n.T("StudioRailImport"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex) { ShowConfigTransferError(ex); }
        finally { if (!_resourcesReleased) SetConfigTransferBusy(false); }
    }
}
