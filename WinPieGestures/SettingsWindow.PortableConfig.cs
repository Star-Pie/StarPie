using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace WinPieGestures;

public partial class SettingsWindow
{
	private bool _configTransferBusy;

	private void SetConfigTransferBusy(bool busy)
	{
		_configTransferBusy = busy;
		ImportConfigButton.IsEnabled = ExportConfigButton.IsEnabled = !busy;
	}

	private async Task ExportSelectedConfigAsync()
	{
		if (_configTransferBusy) return;
		string name = GetSelectedConfigProfileName();
		var dialog = new SaveFileDialog {
			Filter = I18n.T("PortableConfigFilter"), FileName = $"StarPie_Config_{name}_{DateTime.Now:yyyyMMdd}",
			Title = I18n.T("ConfigTransferExportTitle"), AddExtension = true
		};
		if (dialog.ShowDialog(this) != true) return;
		SetConfigTransferBusy(true);
		try
		{
			if (string.Equals(Path.GetExtension(dialog.FileName), ".json", StringComparison.OrdinalIgnoreCase))
			{
				if (!ConfigManager.ExportConfigToFile(name, dialog.FileName)) throw new IOException();
			}
			else
			{
				string json = ConfigManager.GetConfigJsonForExport(name);
				string icons = IconHelper.GetCustomIconsDirectory();
				await Task.Run(() => PortableConfigBundle.Export(json, dialog.FileName, icons));
			}
			if (!_resourcesReleased) AppMessageBox.Show(this, I18n.T("ConfigTransferExported"), I18n.T("ConfigTransferExportTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
		}
		catch (Exception error) { ShowConfigTransferError(error, exporting: true); }
		finally { if (!_resourcesReleased) SetConfigTransferBusy(false); }
	}

	private async Task ImportSelectedConfigAsync()
	{
		if (_configTransferBusy) return;
		var dialog = new OpenFileDialog { Filter = I18n.T("PortableConfigImportFilter"), Title = I18n.T("ConfigTransferImportTitle") };
		if (dialog.ShowDialog(this) != true) return;
		var naming = new InputDialog(I18n.T("ConfigTransferImportTitle"), I18n.T("ProfileRenamePrompt"),
			Path.GetFileNameWithoutExtension(dialog.FileName), value =>
				(!string.IsNullOrWhiteSpace(ConfigManager.CleanFileName(value)), I18n.T("ProfileNameRequired"))) { Owner = this };
		if (naming.ShowDialog() != true) return;
		long revision = ConfigManager.ConfigurationRevision;
		SetConfigTransferBusy(true);
		PortableConfigImportTransaction? transaction = null;
		try
		{
			string json;
			if (string.Equals(Path.GetExtension(dialog.FileName), ".json", StringComparison.OrdinalIgnoreCase))
				json = await Task.Run(() => {
					if (new FileInfo(dialog.FileName).Length > PortableConfigBundle.MaxConfigBytes) throw new InvalidDataException();
					return File.ReadAllText(dialog.FileName);
				});
			else
			{
				string icons = IconHelper.GetCustomIconsDirectory();
				transaction = await Task.Run(() => PortableConfigBundle.PrepareImport(dialog.FileName, icons));
				json = transaction.ConfigJson;
			}
			if (_resourcesReleased || !IsVisible) return;
			if (revision != ConfigManager.ConfigurationRevision) throw new InvalidOperationException(I18n.T("ConfigTransferChanged"));
			if (!ConfigManager.ImportExternalConfigJson(json, naming.InputText.Trim(), out _)) throw new InvalidDataException();
			transaction?.Commit();
			IconHelper.InvalidateCustomIconCache();
			_selectedProfile = null;
			_isUpdatingUi = true;
			try { LoadConfigToUi(); AppThemeManager.ApplyTheme(this, ConfigManager.CurrentConfig.AppTheme ?? "System"); }
			finally { _isUpdatingUi = false; }
			RefreshProfilesUi(ConfigManager.CurrentConfig.Profiles?.Find(p => string.Equals(p.ProcessName, "Global", StringComparison.OrdinalIgnoreCase)));
			ReloadThemePresets(); RefreshConfigProfilesUi();
			RenderMappingsWheelPreview(); RenderLiveWheelPreview();
			AppMessageBox.Show(this, I18n.T("ConfigTransferImported"), I18n.T("ConfigTransferImportTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
		}
		catch (Exception error) { ShowConfigTransferError(error); }
		finally { transaction?.Dispose(); if (!_resourcesReleased) SetConfigTransferBusy(false); }
	}

	private void ShowConfigTransferError(Exception error, bool exporting = false)
	{
		AppLogger.LogError("Configuration transfer failed", error);
		if (_resourcesReleased) return;
		string key = error is PortableConfigBundleException bundle ? bundle.ReasonCode switch {
			PortableConfigBundleReasonCodes.IconMissing => "ConfigTransferMissingIcon",
			PortableConfigBundleReasonCodes.IconBitmapUnsupported or PortableConfigBundleReasonCodes.SvgInvalid => "ConfigTransferSvgUnsupported",
			PortableConfigBundleReasonCodes.HashMismatch or PortableConfigBundleReasonCodes.SizeMismatch => "ConfigTransferIntegrity",
			PortableConfigBundleReasonCodes.ConfigTooLarge or PortableConfigBundleReasonCodes.IconTooLarge or PortableConfigBundleReasonCodes.AssetSizeLimit or PortableConfigBundleReasonCodes.ResourceSizeLimit or PortableConfigBundleReasonCodes.AssetCountLimit => "ConfigTransferSize",
			_ => "ConfigTransferInvalid"
		} : error is InvalidOperationException ? "ConfigTransferChanged" : "ConfigTransferInvalid";
		AppMessageBox.Show(this, I18n.T(key), I18n.T(exporting ? "ConfigTransferExportTitle" : "ConfigTransferImportTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
	}
}
