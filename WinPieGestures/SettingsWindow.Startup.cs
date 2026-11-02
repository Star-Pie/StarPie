using System;
using System.Threading.Tasks;
using System.Windows;

namespace WinPieGestures;

public partial class SettingsWindow
{
    private bool _autoStartOperationBusy;

    private void PinWheelCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUi || _isUiInitializing) return;
        ConfigManager.CurrentConfig.PinWheelAfterTrigger = PinWheelCheckBox.IsChecked == true;
        SyncUiToConfigAndSave();
    }

    private void AutoStartBusy(bool busy)
    {
        _autoStartOperationBusy = busy;
        AutoStartCheckBox.IsEnabled = !busy;
        AutoStartAsAdminCheckBox.IsEnabled = !busy;
        RepairAutoStartButton.IsEnabled = !busy && _loadedAutoStartEnabled;
    }

    private void ApplyAutoStartState(AutoStartState state)
    {
        if (!state.IsKnown) { AutoStartStatusText.Text = I18n.T("AutoStartStateUnknown"); return; }
        _isLoadingAutoStartState = true;
        try
        {
            _loadedAutoStartEnabled = state.IsEnabled;
            _loadedAutoStartAsAdmin = state.AdminTaskEnabled || (!state.IsEnabled && ConfigManager.CurrentConfig.AutoStartAsAdmin);
            AutoStartCheckBox.IsChecked = _loadedAutoStartEnabled;
            AutoStartAsAdminCheckBox.IsChecked = _loadedAutoStartAsAdmin;
            AutoStartStatusText.Text = I18n.T(state.AdminTaskEnabled ? "AutoStartTaskEnabled" :
                state.RegistryEnabled ? "AutoStartRunEnabled" : "AutoStartDisabled");
        }
        finally { _isLoadingAutoStartState = false; }
    }

    private async Task RefreshAutoStartStateAsync()
    {
        if (_autoStartOperationBusy) return;
        AutoStartBusy(true);
        try { ApplyAutoStartState(await ConfigManager.GetAutoStartStateAsync()); }
        catch { AutoStartStatusText.Text = I18n.T("AutoStartStateUnknown"); }
        finally { AutoStartBusy(false); }
    }

    private async Task ChangeAutoStartAsync(bool enabled, bool admin, bool repair = false)
    {
        if (_autoStartOperationBusy) return;
        AutoStartBusy(true);
        AutoStartStatusText.Text = I18n.T("AutoStartWorking");
        try
        {
            var result = repair ? await ConfigManager.RepairAutoStartAsync() : await ConfigManager.SetAutoStartAsync(enabled, admin);
            ApplyAutoStartState(result.State);
            if (!result.IsSuccess)
            {
                string message = I18n.T("AutoStartResult" + result.Status);
                AutoStartStatusText.Text = message;
                AppMessageBox.Show(this, message, "StarPie", MessageBoxButton.OK, MessageBoxImage.Exclamation);
            }
        }
        catch
        {
            AutoStartStatusText.Text = I18n.T("AutoStartResultFailed");
            AppMessageBox.Show(this, I18n.T("AutoStartResultFailed"), "StarPie", MessageBoxButton.OK, MessageBoxImage.Error);
            try { ApplyAutoStartState(await ConfigManager.GetAutoStartStateAsync()); }
            catch { AutoStartStatusText.Text = I18n.T("AutoStartStateUnknown"); }
        }
        finally { AutoStartBusy(false); }
    }

    private async void RepairAutoStartButton_Click(object sender, RoutedEventArgs e) =>
        await ChangeAutoStartAsync(_loadedAutoStartEnabled, _loadedAutoStartAsAdmin, repair: true);
}
