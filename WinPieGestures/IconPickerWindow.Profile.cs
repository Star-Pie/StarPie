using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace WinPieGestures;

public partial class IconPickerWindow
{
    private static string ProfileApplicationIconLabel(string? path) => string.IsNullOrWhiteSpace(path)
        ? I18n.T("StudioProfileIconDefault")
        : Path.GetFileName(path);

    private void ProfileAppLibrary_Click(object sender, RoutedEventArgs e)
    {
        var picker = new ProgramPickerWindow { Owner = this };
        if (picker.ShowDialog() == true && !string.IsNullOrWhiteSpace(picker.SelectedPath))
            SetUnselectedIcon(picker.SelectedPath);
    }

    private void ProfileCapture_Click(object sender, RoutedEventArgs e)
    {
        var picker = new WindowPickerWindow(WindowPickerMode.ExecutablePath) { Owner = this };
        if (picker.ShowDialog() == true && !string.IsNullOrWhiteSpace(picker.SelectedPath))
            SetUnselectedIcon(picker.SelectedPath);
    }

    private void ProfileBrowse_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog
        {
            Title = I18n.T("StudioProfileIconBrowse"),
            Filter = I18n.T("StudioRailIconFilter")
        };
        if (picker.ShowDialog(this) == true) SetUnselectedIcon(picker.FileName);
    }
}
