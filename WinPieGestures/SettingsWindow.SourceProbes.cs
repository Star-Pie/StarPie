using System.Windows;
using System.Windows.Controls;
using WinPieGestures.Plugins;

namespace WinPieGestures;

public partial class SettingsWindow
{
    private CancellationTokenSource? _updateProbeCts;
    private CancellationTokenSource? _pluginProbeCts;

    private async void ProbeUpdateSources_Click(object sender, RoutedEventArgs e)
    {
        if (_resourcesReleased || _updateProbeCts != null) return;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        _updateProbeCts = operation;
        try { await DisplayProbeAsync(ProbeUpdateSourcesButton, UpdateSourceProbeResult, UpdateManager.Instance.ProbeReleaseSourcesAsync, operation.Token); }
        finally { _updateProbeCts = null; }
    }

    private async void ProbePluginSources_Click(object sender, RoutedEventArgs e)
    {
        if (_resourcesReleased || _pluginProbeCts != null) return;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        _pluginProbeCts = operation;
        try { await DisplayProbeAsync(ProbePluginSourcesButton, PluginSourceProbeResult, OfficialPluginClient.ProbeCatalogSourcesAsync, operation.Token); }
        finally { _pluginProbeCts = null; }
    }

    private static string FormatProbeResult(SourceMeasurement measurement)
    {
        string label = measurement.Source is "github" or "direct" ? I18n.T("UpdateSourceGitHub")
            : new OfficialPluginDownloadOptions(measurement.Source).GetChannelDisplayName();
        string outcome = measurement.Milliseconds is long ms ? $"{ms} ms"
            : measurement.Status is int status ? $"HTTP {status}"
            : I18n.T(measurement.Failure ?? "SourceProbeUnavailable");
        return $"{label}  {outcome}";
    }

    private async Task DisplayProbeAsync(Button button, TextBlock result, Func<CancellationToken, Task<SourceMeasurement[]>> probe, CancellationToken token)
    {
        button.IsEnabled = false;
        result.Visibility = Visibility.Visible;
        result.Text = I18n.T("SourceProbeRunning");
        try
        {
            var results = await probe(token);
            if (!token.IsCancellationRequested && !_resourcesReleased)
                result.Text = string.Join("   ·   ", results.Select(FormatProbeResult));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            AppLogger.LogWarn($"[source probe] {ex.Message}");
            if (!token.IsCancellationRequested && !_resourcesReleased) result.Text = I18n.T("SourceProbeUnavailable");
        }
        finally { if (!_resourcesReleased) button.IsEnabled = true; }
    }
}
