using System;
using System.Diagnostics;
using System.Windows;

namespace WinPieGestures;

public partial class SettingsWindow
{
    private bool _embeddedChangelogLoaded;
    private string? _embeddedChangelogText;

    private void LocalizeEmbeddedChangelog()
    {
        if (ChangelogTitleText != null) ChangelogTitleText.Text = I18n.T("EmbeddedChangelogTitle");
        if (ChangelogSourceText != null) ChangelogSourceText.Text = I18n.T("EmbeddedChangelogSource");
        if (ChangelogStatusText != null) ChangelogStatusText.Text = I18n.T("EmbeddedChangelogUnavailable");
    }

    private void EnsureEmbeddedChangelogLoaded()
    {
        if (_embeddedChangelogLoaded || ChangelogEntriesPanel == null || _resourcesReleased) return;
        _embeddedChangelogLoaded = true;
        try
        {
            _embeddedChangelogText = EmbeddedChangelog.ReadText(typeof(AppVersionInfo).Assembly);
            var entries = EmbeddedChangelog.Parse(_embeddedChangelogText);
            ChangelogView.Populate(ChangelogEntriesPanel, entries);
            ChangelogStatusText.Visibility = entries.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (Exception error)
        {
            _embeddedChangelogText = null;
            ChangelogEntriesPanel.Children.Clear();
            ChangelogStatusText.Visibility = Visibility.Visible;
            AppLogger.LogError("Loading embedded changelog failed", error);
        }
    }

    private void OpenEmbeddedChangelog()
    {
        EnsureEmbeddedChangelogLoaded();
        if (string.IsNullOrWhiteSpace(_embeddedChangelogText))
        {
            AppMessageBox.Show(I18n.T("EmbeddedChangelogUnavailable"), I18n.T("EmbeddedChangelogTitle"));
            return;
        }
        try
        {
            // File creation and external launching happen only in this explicit button action.
            string path = EmbeddedChangelog.ExportToTemporaryFile(_embeddedChangelogText);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception error)
        {
            AppLogger.LogError("Opening embedded changelog failed", error);
            AppMessageBox.Show(I18n.T("EmbeddedChangelogOpenFailed"), I18n.T("EmbeddedChangelogTitle"));
        }
    }
}
