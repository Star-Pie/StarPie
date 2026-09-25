using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Forms;

namespace WinPieGestures;

public partial class SubActionEditorWindow : Window
{
	public ObservableCollection<SubSlotViewModel> SubSlots { get; } = new ObservableCollection<SubSlotViewModel>();

	public List<ActionItem> ResultSubActions { get; private set; } = new List<ActionItem>();

	public SubActionEditorWindow(string directionLabel, string sectorName, List<ActionItem>? existingSubActions)
	{
		InitializeComponent();
		AppThemeManager.ApplyTheme(this, ConfigManager.CurrentConfig?.AppTheme ?? "System");
		SectorInfoTitle.Text = directionLabel + " 方位 - [" + sectorName + "] 级联子菜单";

		bool isFan = string.Equals(ConfigManager.CurrentConfig?.SubmenuStyle, "Fan", StringComparison.OrdinalIgnoreCase);
		int maxAllowed = isFan ? 3 : 4;
		string styleName = isFan ? "蜂窝扇 (Honeycomb Fan)" : "外圈子环 (Sub-Ring)";
		SubCountLimitText.Text = $"当光标向外划向该扇区时展开二级子菜单。当前【{styleName}】模式下支持配置 1~{maxAllowed} 个二级子动作。";

		if (existingSubActions != null && existingSubActions.Count > 0)
		{
			int num = 1;
			foreach (ActionItem existingSubAction in existingSubActions)
			{
				SubSlots.Add(new SubSlotViewModel
				{
					IndexNumber = num++,
					IsExpanded = true,
					Action = new ActionItem
					{
						Name = existingSubAction.Name,
						Type = existingSubAction.Type,
						Parameter = existingSubAction.Parameter,
						Arguments = existingSubAction.Arguments,
						IconKey = existingSubAction.IconKey,
						CustomIconSvg = existingSubAction.CustomIconSvg,
						InheritAppIconPath = existingSubAction.InheritAppIconPath,
						CommandTerminal = existingSubAction.CommandTerminal,
						RunAsStandardUser = existingSubAction.RunAsStandardUser,
						BrowserChoice = existingSubAction.BrowserChoice,
						BrowserPath = existingSubAction.BrowserPath,
						PluginActionRef = existingSubAction.PluginActionRef,
						ExtensionData = existingSubAction.ExtensionData != null ? new Dictionary<string, string>(existingSubAction.ExtensionData) : null
					}
				});
			}
		}

		SubActionsItemsControl.ItemsSource = SubSlots;
		ReindexSubSlots();
	}

	private void ReindexSubSlots()
	{
		for (int i = 0; i < SubSlots.Count; i++)
		{
			SubSlots[i].IndexNumber = i + 1;
			SubSlots[i].CanMoveUp = (i > 0);
			SubSlots[i].CanMoveDown = (i < SubSlots.Count - 1);
		}
		UpdateEmptyState();
	}

	private void UpdateEmptyState()
	{
		EmptyStateBorder.Visibility = (SubSlots.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
		bool isFan = string.Equals(ConfigManager.CurrentConfig?.SubmenuStyle, "Fan", StringComparison.OrdinalIgnoreCase);
		int maxAllowed = isFan ? 3 : 4;
		AddSubActionButton.IsEnabled = SubSlots.Count < maxAllowed;
		AddSubActionButton.ToolTip = SubSlots.Count >= maxAllowed
			? (isFan ? "当前蜂窝扇模式下最多支持配置 3 个二级级联子动作" : "当前外圈子环模式下最多支持配置 4 个二级级联子动作")
			: null;
	}

	private void AddSubActionButton_Click(object sender, RoutedEventArgs e)
	{
		bool isFan = string.Equals(ConfigManager.CurrentConfig?.SubmenuStyle, "Fan", StringComparison.OrdinalIgnoreCase);
		int maxAllowed = isFan ? 3 : 4;
		if (SubSlots.Count < maxAllowed)
		{
			int num = SubSlots.Count + 1;
			SubSlots.Add(new SubSlotViewModel
			{
				IndexNumber = num,
				IsExpanded = true,
				Action = new ActionItem
				{
					Name = $"子动作 {num}",
					Type = "Hotkey",
					Parameter = "",
					IconKey = ""
				}
			});
			ReindexSubSlots();
		}
		else
		{
			string styleName = isFan ? "蜂窝扇 (Honeycomb Fan)" : "外圈子环 (Sub-Ring)";
			System.Windows.MessageBox.Show(this, $"当前二级菜单样式为【{styleName}】，每个主扇区最多支持配置 {maxAllowed} 个二级级联子动作。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
		}
	}

	private void ToggleExpand_Click(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { DataContext: SubSlotViewModel vm })
		{
			vm.IsExpanded = !vm.IsExpanded;
		}
	}

	private void SubMoveUp_Click(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { DataContext: SubSlotViewModel vm })
		{
			int idx = SubSlots.IndexOf(vm);
			if (idx > 0)
			{
				SubSlots.Move(idx, idx - 1);
				ReindexSubSlots();
			}
		}
	}

	private void SubMoveDown_Click(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { DataContext: SubSlotViewModel vm })
		{
			int idx = SubSlots.IndexOf(vm);
			if (idx >= 0 && idx < SubSlots.Count - 1)
			{
				SubSlots.Move(idx, idx + 1);
				ReindexSubSlots();
			}
		}
	}

	private void SubTest_Click(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { DataContext: SubSlotViewModel vm })
		{
			ActionExecutor.ExecuteForTesting(vm.Action);
		}
	}

	private void DeleteSubAction_Click(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { DataContext: SubSlotViewModel vm })
		{
			SubSlots.Remove(vm);
			ReindexSubSlots();
		}
	}

	private void SubPickIcon_Click(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { DataContext: SubSlotViewModel vm })
		{
			IconPickerWindow iconPickerWindow = new IconPickerWindow(vm.IconKey)
			{
				Owner = this
			};
			if (iconPickerWindow.ShowDialog() == true)
			{
				vm.IconKey = iconPickerWindow.SelectedIconKey ?? "";
				vm.InheritAppIconPath = "";
			}
		}
	}

	private void SubHotkeyBuilder_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if (sender is FrameworkElement { DataContext: SubSlotViewModel vm })
			{
				HotkeyBuilderDialog dlg = new HotkeyBuilderDialog(vm.Parameter ?? "")
				{
					Owner = this
				};
				if (dlg.ShowDialog() == true)
				{
					vm.Parameter = dlg.ResultHotkey;
				}
			}
		}
		catch (Exception ex)
		{
			System.Windows.MessageBox.Show(this, "打开按键拼装器失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
		}
	}

	private void SubPickProgramFromLibrary_Click(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { DataContext: SubSlotViewModel vm })
		{
			ProgramPickerWindow programPicker = new ProgramPickerWindow
			{
				Owner = this
			};
			if (programPicker.ShowDialog() == true && !string.IsNullOrEmpty(programPicker.SelectedPath))
			{
				vm.Parameter = programPicker.SelectedPath;
				vm.InheritAppIconPath = programPicker.SelectedPath;
				vm.IconKey = "";
				vm.CustomIconSvg = "";
				vm.Name = !string.IsNullOrEmpty(programPicker.SelectedName)
					? programPicker.SelectedName
					: Path.GetFileNameWithoutExtension(programPicker.SelectedPath);
			}
		}
	}

	private void SubCaptureRunningWindow_Click(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { DataContext: SubSlotViewModel vm })
		{
			WindowPickerWindow picker = new WindowPickerWindow(WindowPickerMode.ExecutablePath)
			{
				Owner = this
			};
			if (picker.ShowDialog() == true && !string.IsNullOrEmpty(picker.SelectedPath))
			{
				vm.Parameter = picker.SelectedPath;
				vm.InheritAppIconPath = picker.SelectedPath;
				vm.IconKey = "";
				vm.CustomIconSvg = "";
				vm.Name = !string.IsNullOrEmpty(picker.SelectedTitle)
					? picker.SelectedTitle
					: (!string.IsNullOrEmpty(picker.SelectedProcessName) ? picker.SelectedProcessName : Path.GetFileNameWithoutExtension(picker.SelectedPath));
			}
		}
	}

	private void SubBrowseProgram_Click(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { DataContext: SubSlotViewModel vm })
		{
			Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog
			{
				Filter = "应用程序 (*.exe;*.lnk;*.bat;*.cmd)|*.exe;*.lnk;*.bat;*.cmd|所有文件 (*.*)|*.*",
				Title = "选择要启动的应用程序或快捷方式"
			};
			if (dlg.ShowDialog(this) == true)
			{
				vm.Parameter = dlg.FileName;
				vm.InheritAppIconPath = dlg.FileName;
				vm.IconKey = "";
				vm.CustomIconSvg = "";
				vm.Name = Path.GetFileNameWithoutExtension(dlg.FileName);
			}
		}
	}

	private void SubBrowseCustomBrowser_Click(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { DataContext: SubSlotViewModel vm })
		{
			Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog
			{
				Filter = "浏览器执行程序 (*.exe)|*.exe|所有文件 (*.*)|*.*",
				Title = "选择自定义浏览器执行程序"
			};
			if (dlg.ShowDialog(this) == true)
			{
				vm.BrowserPath = dlg.FileName;
			}
		}
	}

	private void SubUrlPreset_GitHub(object sender, RoutedEventArgs e) => SetSubWebUrl(sender, "https://github.com", "GitHub");
	private void SubUrlPreset_Bilibili(object sender, RoutedEventArgs e) => SetSubWebUrl(sender, "https://www.bilibili.com", "哔哩哔哩");
	private void SubUrlPreset_Bing(object sender, RoutedEventArgs e) => SetSubWebUrl(sender, "https://www.bing.com", "Bing 搜索");
	private void SubUrlPreset_Google(object sender, RoutedEventArgs e) => SetSubWebUrl(sender, "https://www.google.com", "Google");

	private void SetSubWebUrl(object sender, string url, string name)
	{
		if (sender is FrameworkElement { DataContext: SubSlotViewModel vm })
		{
			vm.Parameter = url;
			if (ActionNameDefaults.IsAutoFilled(vm.Name))
			{
				vm.Name = name;
			}
			if (string.IsNullOrEmpty(vm.IconKey))
			{
				vm.IconKey = "Globe";
			}
		}
	}

	private void SubBrowseFolder_Click(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { DataContext: SubSlotViewModel vm })
		{
			using FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog
			{
				Description = "选择要打开的本地文件夹",
				UseDescriptionForTitle = true,
				ShowNewFolderButton = true
			};
			if (folderBrowserDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
			{
				vm.Parameter = folderBrowserDialog.SelectedPath;
				if (ActionNameDefaults.IsAutoFilled(vm.Name))
				{
					vm.Name = Path.GetFileName(folderBrowserDialog.SelectedPath);
					if (string.IsNullOrEmpty(vm.Name)) vm.Name = folderBrowserDialog.SelectedPath;
				}
				if (string.IsNullOrEmpty(vm.IconKey))
				{
					vm.IconKey = "Folder";
				}
			}
		}
	}

	private void SubFolderPreset_ThisPC(object sender, RoutedEventArgs e) => SetSubFolder(sender, "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", "此电脑");
	private void SubFolderPreset_RecycleBin(object sender, RoutedEventArgs e) => SetSubFolder(sender, "::{645FF040-5081-101B-9F08-00AA002F954E}", "回收站");
	private void SubFolderPreset_Desktop(object sender, RoutedEventArgs e) => SetSubFolder(sender, Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "桌面");
	private void SubFolderPreset_Downloads(object sender, RoutedEventArgs e) => SetSubFolder(sender, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"), "下载");
	private void SubFolderPreset_Documents(object sender, RoutedEventArgs e) => SetSubFolder(sender, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "文档");

	private void SetSubFolder(object sender, string path, string name)
	{
		if (sender is FrameworkElement { DataContext: SubSlotViewModel vm })
		{
			vm.Parameter = path;
			if (ActionNameDefaults.IsAutoFilled(vm.Name))
			{
				vm.Name = name;
			}
			if (string.IsNullOrEmpty(vm.IconKey))
			{
				vm.IconKey = "Folder";
			}
		}
	}

	private void SubTilePreset_2L(object sender, RoutedEventArgs e) => SetSubTile(sender, "2L");
	private void SubTilePreset_2T(object sender, RoutedEventArgs e) => SetSubTile(sender, "2T");
	private void SubTilePreset_3L12(object sender, RoutedEventArgs e) => SetSubTile(sender, "3L12");
	private void SubTilePreset_4G(object sender, RoutedEventArgs e) => SetSubTile(sender, "4G");
	private void SubTilePreset_3R(object sender, RoutedEventArgs e) => SetSubTile(sender, "3R");

	private void SetSubTile(object sender, string layout)
	{
		if (sender is FrameworkElement { DataContext: SubSlotViewModel vm })
		{
			vm.TileLayout = layout;
			if (ActionNameDefaults.IsAutoFilled(vm.Name))
			{
				vm.Name = "平铺: " + WindowTiler.LayoutDisplayName(layout);
			}
			if (string.IsNullOrEmpty(vm.IconKey) && string.IsNullOrEmpty(vm.InheritAppIconPath) && string.IsNullOrEmpty(vm.CustomIconSvg))
			{
				vm.IconKey = "Tile";
			}
		}
	}

	private void SubOpacityPreset_70(object sender, RoutedEventArgs e) => SetSubOpacity(sender, 70);
	private void SubOpacityPreset_80(object sender, RoutedEventArgs e) => SetSubOpacity(sender, 80);
	private void SubOpacityPreset_90(object sender, RoutedEventArgs e) => SetSubOpacity(sender, 90);
	private void SubOpacityPreset_100(object sender, RoutedEventArgs e) => SetSubOpacity(sender, 100);

	private void SetSubOpacity(object sender, int opacity)
	{
		if (sender is FrameworkElement { DataContext: SubSlotViewModel vm })
		{
			vm.WindowOpacityValue = opacity;
			if (ActionNameDefaults.IsAutoFilled(vm.Name))
			{
				vm.Name = $"透明度: {opacity}%";
			}
			if (string.IsNullOrEmpty(vm.IconKey) && string.IsNullOrEmpty(vm.InheritAppIconPath) && string.IsNullOrEmpty(vm.CustomIconSvg))
			{
				vm.IconKey = "Eye";
			}
		}
	}

	private void SubSwitchPreset_1(object sender, RoutedEventArgs e) => SetSubSwitch(sender, "1");
	private void SubSwitchPreset_2(object sender, RoutedEventArgs e) => SetSubSwitch(sender, "2");
	private void SubSwitchPreset_3(object sender, RoutedEventArgs e) => SetSubSwitch(sender, "3");
	private void SubSwitchPreset_4(object sender, RoutedEventArgs e) => SetSubSwitch(sender, "4");

	private void SetSubSwitch(object sender, string slot)
	{
		if (sender is FrameworkElement { DataContext: SubSlotViewModel vm })
		{
			vm.NthWindowIndex = slot;
			if (ActionNameDefaults.IsAutoFilled(vm.Name))
			{
				vm.Name = $"切换应用 #{slot}";
			}
			if (string.IsNullOrEmpty(vm.IconKey) && string.IsNullOrEmpty(vm.InheritAppIconPath) && string.IsNullOrEmpty(vm.CustomIconSvg))
			{
				vm.IconKey = "Window";
			}
		}
	}

	private void SubTestOcr_Click(object sender, RoutedEventArgs e)
	{
		OcrManager.StartCaptureAndRecognize();
	}

	private void SubOpenOcrSettings_Click(object sender, RoutedEventArgs e)
	{
		OcrSettingsDialog dlg = new OcrSettingsDialog
		{
			Owner = this
		};
		dlg.ShowDialog();
	}

	private void SubPickShellTool_Click(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { DataContext: SubSlotViewModel vm })
		{
			ShellActionPickerWindow picker = new ShellActionPickerWindow(vm.Parameter)
			{
				Owner = this
			};
			if (picker.ShowDialog() == true && picker.SelectedTool != null)
			{
				vm.Parameter = picker.SelectedTool.Id;
				vm.Name = picker.SelectedTool.Name;
				vm.IconKey = picker.SelectedTool.IconKey;
				vm.InheritAppIconPath = "";
			}
		}
	}

	private void SubInheritFromLibrary_Click(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { DataContext: SubSlotViewModel vm })
		{
			ProgramPickerWindow picker = new ProgramPickerWindow
			{
				Owner = this
			};
			if (picker.ShowDialog() == true && !string.IsNullOrEmpty(picker.SelectedPath))
			{
				vm.InheritAppIconPath = picker.SelectedPath;
			}
		}
	}

	private void SubInheritFromCapture_Click(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { DataContext: SubSlotViewModel vm })
		{
			WindowPickerWindow picker = new WindowPickerWindow
			{
				Owner = this
			};
			if (picker.ShowDialog() == true && !string.IsNullOrEmpty(picker.SelectedPath))
			{
				vm.InheritAppIconPath = picker.SelectedPath;
			}
		}
	}

	private void SubInheritFromBrowse_Click(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { DataContext: SubSlotViewModel vm })
		{
			Microsoft.Win32.OpenFileDialog ofd = new Microsoft.Win32.OpenFileDialog
			{
				Filter = "可提取图标程序与文件 (*.exe;*.ico;*.dll;*.lnk)|*.exe;*.ico;*.dll;*.lnk|所有文件 (*.*)|*.*",
				Title = "选择要提取并继承图标的程序或文件"
			};
			if (ofd.ShowDialog(this) == true)
			{
				vm.InheritAppIconPath = ofd.FileName;
			}
		}
	}

	private void SubClearInheritedIcon_Click(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { DataContext: SubSlotViewModel vm })
		{
			vm.InheritAppIconPath = "";
		}
	}

	private void SaveButton_Click(object sender, RoutedEventArgs e)
	{
		ResultSubActions = SubSlots.Select((SubSlotViewModel s) => s.Action).ToList();
		DialogResult = true;
		Close();
	}

	private void CancelButton_Click(object sender, RoutedEventArgs e)
	{
		DialogResult = false;
		Close();
	}
}
