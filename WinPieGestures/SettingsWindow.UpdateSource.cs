using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace WinPieGestures;

public partial class SettingsWindow
{
    private readonly UpdateSourceState _updateSourceState = new();
    private CancellationTokenSource? _updateCheckCts;
    private int _updateCheckRevision;
    private int _updateDownloadRevision;

    private string CurrentUpdateReleaseSource => UpdateManager.NormalizeReleaseSource(ConfigManager.CurrentConfig?.UpdateReleaseSource);
    private string UpdateSourceDisplayName => I18n.T(CurrentUpdateReleaseSource == "atomgit" ? "UpdateSourceAtomGit" : "UpdateSourceGitHub");
    private bool CanApplyUpdateDownload(int revision) => revision == _updateDownloadRevision &&
        !_resourcesReleased && !App.IsExiting;

    private void LocalizeUpdateReleaseSources()
    {
        if (UpdateSourceTitleText == null) return;
        UpdateSourceTitleText.Text = I18n.T("UpdateSourceTitle");
        UpdateSourceDescText.Text = I18n.T("UpdateSourceDesc");
        UpdateSourceGitHubItem.Content = I18n.T("UpdateSourceGitHub");
        UpdateSourceAtomGitItem.Content = I18n.T("UpdateSourceAtomGit");
        RefreshUpdateReleaseSourceUi();
    }

    private void RefreshUpdateReleaseSourceUi()
    {
        if (UpdateSourceComboBox == null) return;
        if (_updateSourceState.SelectSource(CurrentUpdateReleaseSource)) InvalidateUpdateSourceResults();
        SetComboBoxSelectedValue(UpdateSourceComboBox, CurrentUpdateReleaseSource);
        if (UpdateProxyComboBox != null) UpdateProxyComboBox.IsEnabled = CurrentUpdateReleaseSource == "github";
        if (UpdateProxyDescText != null) UpdateProxyDescText.Text = I18n.T(CurrentUpdateReleaseSource == "atomgit" ? "UpdateSourceProxyUnused" : "UpdateProxyDesc");
        if (ViewReleasesWebBtn != null) ViewReleasesWebBtn.ToolTip = I18n.TF("UpdateSourceWebHint", UpdateSourceDisplayName);
    }

    private void UpdateSourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingUi || ConfigManager.CurrentConfig == null || UpdateSourceComboBox.SelectedItem is not ComboBoxItem item) return;
        string source = UpdateManager.NormalizeReleaseSource(item.Tag as string);
        if (source == CurrentUpdateReleaseSource) return;
        ConfigManager.CurrentConfig.UpdateReleaseSource = source;
        ConfigManager.CurrentConfig.LastCheckUpdateTime = "";
        RefreshUpdateReleaseSourceUi();
        ScheduleAutoSave();
    }

    private void InvalidateUpdateSourceResults()
    {
        _updateCheckRevision++;
        _updateDownloadRevision++;
        _updateCheckCts?.Cancel();
        _downloadCts?.Cancel();
        _latestReleaseInfo = null;
        _allFetchedReleases = null;
        _selectedRollbackRelease = null;
        _downloadedZipPath = null;
        if (UpdateNewVersionPanel != null) UpdateNewVersionPanel.Visibility = Visibility.Collapsed;
        if (UpdateDownloadProgressPanel != null) UpdateDownloadProgressPanel.Visibility = Visibility.Collapsed;
        if (UpdateReadyToInstallPanel != null) UpdateReadyToInstallPanel.Visibility = Visibility.Collapsed;
        if (RollbackDetailCard != null) RollbackDetailCard.Visibility = Visibility.Collapsed;
        if (CheckUpdateNowBtn != null) { CheckUpdateNowBtn.IsEnabled = true; CheckUpdateNowBtn.Content = I18n.T("BtnCheckUpdate"); }
        if (UpdateStatusBadgeText != null) UpdateStatusBadgeText.Text = I18n.T("UpdateSourceChanged");
        if (UpdateStatusDescText != null) UpdateStatusDescText.Text = I18n.TF("UpdateSourceChangedDesc", UpdateSourceDisplayName);
        UpdateRollbackBadgeAndCandidates();
    }

    private void ShowUpdateSourceError()
    {
        _latestReleaseInfo = null;
        _allFetchedReleases = null;
        _selectedRollbackRelease = null;
        if (UpdateNewVersionPanel != null) UpdateNewVersionPanel.Visibility = Visibility.Collapsed;
        if (RollbackDetailCard != null) RollbackDetailCard.Visibility = Visibility.Collapsed;
        if (UpdateStatusBadgeText != null) UpdateStatusBadgeText.Text = I18n.T("UpdateStatusError");
        if (UpdateStatusDescText != null) UpdateStatusDescText.Text = I18n.TF("UpdateSourceErrorDesc", UpdateSourceDisplayName);
        UpdateRollbackBadgeAndCandidates();
    }

    private static string CreateUpdateDownloadPath(string fileName)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars()) fileName = fileName.Replace(invalid, '_');
        // Each operation owns its file, so a cancelled old download cannot delete a new one.
        return Path.Combine(Path.GetTempPath(), "StarPie_Updates", Guid.NewGuid().ToString("N"), fileName);
    }
}
