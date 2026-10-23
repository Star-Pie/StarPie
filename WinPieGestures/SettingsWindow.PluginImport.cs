using System.IO;
using System.Windows;
using WinPieGestures.Plugins;

namespace WinPieGestures;

public partial class SettingsWindow
{
    private bool _pluginImportBusy;

    private OfficialPluginDownloadOptions CapturePluginDownloadOptions() => new(
        (PluginDownloadChannelComboBox?.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string
        ?? ConfigManager.CurrentConfig?.UpdateProxySource);

    private void LocalizePluginDownloadChannels()
    {
        if (PluginDownloadChannelComboBox == null) return;
        PluginDownloadChannelComboBox.ToolTip = I18n.T("OfficialPluginsOnboardingChannelHint");
        string selected = CapturePluginDownloadOptions().Channel;
        foreach (System.Windows.Controls.ComboBoxItem item in PluginDownloadChannelComboBox.Items)
        {
            var options = new OfficialPluginDownloadOptions(item.Tag as string);
            item.Content = options.GetChannelDisplayName();
            if (options.Channel == selected) PluginDownloadChannelComboBox.SelectedItem = item;
        }
    }

    private async Task ImportPluginFileAsync()
    {
        if (_pluginImportBusy || _resourcesReleased || App.IsExiting || !EnsurePluginSystemReady()) return;
        CancellationToken lifetime = _lifetimeCts.Token;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = I18n.T("PluginsImportTitle"),
            Filter = I18n.T("PluginsImportFilter"),
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != true) return;
        _pluginImportBusy = true;
        InstallPluginButton.IsEnabled = false;
        try
        {
            if (string.Equals(Path.GetExtension(dialog.FileName), ".dll", StringComparison.OrdinalIgnoreCase))
            {
                await InstallCommunityPluginAsync(dialog.FileName);
                return;
            }
            if (OfficialPluginsStatusText != null) OfficialPluginsStatusText.Text = I18n.T("PluginsOfflineVerifying");
            using var package = await Task.Run(() => OfficialPluginClient.PrepareOfflinePackage(dialog.FileName), lifetime);
            if (_resourcesReleased || App.IsExiting || !IsVisible) return;
            var previous = PluginRegistryStore.FindEntry(package.Module.Id);
            bool enableAfterInstall = previous?.Enabled ?? false;
            string note = I18n.TF("PluginsOfflineVerified", dialog.FileName);
            if (previous != null) note += "\n" + I18n.TF("PluginsOfflineReplace", previous.Version, package.Module.Version);
            if (!ConfirmPluginInstall(new PluginInstallConfirmation
            {
                Scan = package.Scan,
                State = previous == null ? PluginCandidateState.Installable : PluginCandidateState.Update,
                Note = note,
                EnableAfterInstall = enableAfterInstall,
            })) return;
            var result = await OfficialPluginClient.InstallOfflinePackageAsync(package, acknowledged: true, lifetime);
            if (_resourcesReleased || App.IsExiting || !IsVisible) return;
            if (!result.Success)
            {
                string detail = I18n.TF("PluginsInstallFailed", result.Error);
                if (!result.RestartSuggested || !OfferPluginRestart(result.PluginId, detail,
                    restartSuggested: true, operation: PluginRestartOperation.InstallOrUpdate))
                    AppMessageBox.Show(this, detail, I18n.T("PluginsMsgTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            AppMessageBox.Show(this, enableAfterInstall
                ? (result.Enabled ? I18n.TF("PluginsOfflineInstalled", package.Module.Name)
                    : I18n.TF("PluginsOfflineEnableFailed", package.Module.Name, result.Error))
                : I18n.TF("PluginsInstalledDisabled", package.Module.Id),
                I18n.T("PluginsMsgTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (_resourcesReleased || App.IsExiting || !IsVisible) return;
            AppMessageBox.Show(this, I18n.TF("PluginsOfflineImportFailed", ex.Message),
                I18n.T("PluginsMsgTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _pluginImportBusy = false;
            if (!App.IsExiting && !_resourcesReleased)
            {
                InstallPluginButton.IsEnabled = true;
                RefreshPluginManagerUi();
                HandlePluginAvailabilityChanged();
            }
        }
    }

    private void OpenOfficialPluginMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { ContextMenu: { } menu } button) return;
        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private async void SaveOfficialPluginPackageMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_resourcesReleased || App.IsExiting) return;
        CancellationToken lifetime = _lifetimeCts.Token;
        if (sender is not System.Windows.Controls.MenuItem { Tag: OfficialPluginModule module } item) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = I18n.T("PluginsOfflineSave"),
            Filter = I18n.T("PluginsOfflineSaveFilter"),
            FileName = Path.GetFileName(module.AssetName),
            DefaultExt = ".spkg",
        };
        if (dialog.ShowDialog(this) != true) return;
        item.SetCurrentValue(IsEnabledProperty, false);
        item.SetCurrentValue(System.Windows.Controls.HeaderedItemsControl.HeaderProperty, I18n.T("PluginsOfflineDownloading"));
        try
        {
            await OfficialPluginClient.SaveOfflinePackageAsync(module, dialog.FileName,
                CapturePluginDownloadOptions(), lifetime);
            if (_resourcesReleased || App.IsExiting || !IsVisible) return;
            AppMessageBox.Show(this, I18n.TF("PluginsOfflineSaved", dialog.FileName),
                I18n.T("PluginsMsgTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (_resourcesReleased || App.IsExiting || !IsVisible) return;
            AppMessageBox.Show(this, I18n.TF("PluginsOfflineSaveFailed", ex.Message),
                I18n.T("PluginsMsgTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            if (!App.IsExiting && !_resourcesReleased)
            {
                item.GetBindingExpression(IsEnabledProperty)?.UpdateTarget();
                item.GetBindingExpression(System.Windows.Controls.HeaderedItemsControl.HeaderProperty)?.UpdateTarget();
            }
        }
    }
}
