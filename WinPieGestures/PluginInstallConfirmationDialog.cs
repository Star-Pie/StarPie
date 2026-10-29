using System.Windows;
using WinPieGestures.Plugins;

namespace WinPieGestures;

/// <summary>确认界面的薄适配；正文仍由无界面的统一构造器生成。</summary>
internal static class PluginInstallConfirmationDialog
{
    internal static bool Show(Window owner, PluginInstallConfirmation info) =>
        AppMessageBox.Show(owner, PluginInstallConfirmationText.Build(info), I18n.T("PluginsConfirmTitle"),
            MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK;
}
