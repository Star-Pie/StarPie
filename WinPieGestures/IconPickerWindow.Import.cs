using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace WinPieGestures;

public partial class IconPickerWindow
{
    private bool _importBusy;
    private bool _importWindowClosed;
    private CancellationTokenSource? _importCancellation;

    protected override void OnClosed(EventArgs e)
    {
        _importWindowClosed = true;
        _importCancellation?.Cancel();
        base.OnClosed(e);
    }

    private async void ImportSvgFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_importBusy) return;
        var dialog = new OpenFolderDialog { Title = I18n.T("SvgImportFolder"), Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        await RunSvgImportAsync(token => SvgIconBatchImporter.ImportFolder(dialog.FolderName, cancellationToken: token));
    }

    private async Task ImportSelectedFilesAsync(string[] paths)
    {
        if (_importBusy) return;
        // Keep the existing single raster import; batch selection deliberately accepts SVG only.
        if (paths.Length == 1 && !string.Equals(Path.GetExtension(paths[0]), ".svg", StringComparison.OrdinalIgnoreCase))
        {
            if (IconHelper.ImportCustomIcon(paths[0]) == null) { ShowImportFailure(); return; }
            PopulateIcons(SearchTextBox.Text.Trim());
            ShowImportStatus(string.Format(I18n.T("SvgImportSummary"), 1, 0, 0));
            return;
        }
        await RunSvgImportAsync(token => SvgIconBatchImporter.ImportFiles(paths, cancellationToken: token));
    }

    private async Task RunSvgImportAsync(Func<CancellationToken, SvgIconBatchImportResult> import)
    {
        if (_importBusy) return;
        _importBusy = true;
        ImportIconButton.IsEnabled = ImportSvgFolderButton.IsEnabled = SearchTextBox.IsEnabled = false;
        _importCancellation = new CancellationTokenSource();
        try
        {
            ShowImportStatus(I18n.T("SvgImportWorking"));
            var result = await Task.Run(() => import(_importCancellation.Token));
            if (_importWindowClosed) return;
            if (result.SuccessCount > 0) { _selectedCard = null; PopulateIcons(SearchTextBox.Text.Trim()); }
            string summary = string.Format(I18n.T("SvgImportSummary"), result.SuccessCount, result.SkippedCount, result.FailureCount);
            var problems = result.Items.Where(item => item.Status != SvgIconBatchItemStatus.Success).ToArray();
            summary += string.Concat(problems.Take(50).Select(item => "\n" + Path.GetFileName(item.SourcePath) + ": " + SvgReason(item.ReasonCode)));
            if (problems.Length > 50) summary += "\n" + string.Format(I18n.T("SvgImportMore"), problems.Length - 50);
            if (result.BatchReasonCode != null) summary += "\n" + SvgReason(result.BatchReasonCode);
            ShowImportStatus(summary);
        }
        catch { if (!_importWindowClosed) ShowImportFailure(); }
        finally
        {
            _importCancellation.Dispose();
            _importCancellation = null;
            _importBusy = false;
            if (!_importWindowClosed) ImportIconButton.IsEnabled = ImportSvgFolderButton.IsEnabled = SearchTextBox.IsEnabled = true;
        }
    }

    private static string SvgReason(string code) => I18n.T(code switch
    {
        SvgIconReasonCodes.UnknownExtension => "SvgReasonExtension",
        SvgIconReasonCodes.DuplicateSource => "SvgReasonDuplicate",
        SvgIconReasonCodes.FileTooLarge => "SvgReasonSize",
        SvgIconReasonCodes.PaintTransparent or SvgIconReasonCodes.PaintUnsupported => "SvgReasonPaint",
        SvgIconReasonCodes.InvalidXml or SvgIconReasonCodes.DtdForbidden or SvgIconReasonCodes.InvalidRoot => "SvgReasonXml",
        SvgIconReasonCodes.PathDataMissing or SvgIconReasonCodes.InvalidPathData or SvgIconReasonCodes.NonFiniteCoordinate or SvgIconReasonCodes.EmptyGeometry => "SvgReasonPath",
        SvgIconReasonCodes.Cancelled => "SvgReasonCancelled",
        SvgIconReasonCodes.InvalidSourcePath or SvgIconReasonCodes.FileNotFound or SvgIconReasonCodes.IoError or SvgIconReasonCodes.TargetDirectoryInvalid or SvgIconReasonCodes.SourceDirectoryInvalid or SvgIconReasonCodes.CacheInvalidationFailed or SvgIconReasonCodes.SourceDestinationCollision => "SvgReasonIo",
        _ => "SvgReasonUnsupported"
    });

    private void ShowImportStatus(string message)
    {
        ImportStatusTextBox.Text = message;
        ImportStatusTextBox.Visibility = Visibility.Visible;
    }
    private void ShowImportFailure() => ShowImportStatus(I18n.T("SvgReasonIo"));
}
