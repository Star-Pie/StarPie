using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Forms;
using WinPieGestures.Plugins;

namespace WinPieGestures;

public partial class SubActionEditorWindow : Window
{
	public ObservableCollection<SubSlotViewModel> SubSlots { get; } = new ObservableCollection<SubSlotViewModel>();

	public List<ActionItem> ResultSubActions { get; private set; } = new List<ActionItem>();

	private readonly List<ActionItem> _branch = new();
	private readonly int _maxDepth;
	private int SlotLimit => string.Equals(ConfigManager.CurrentConfig?.SubmenuStyle, "Fan", StringComparison.OrdinalIgnoreCase) ? 3 : 4;

	public SubActionEditorWindow(string directionLabel, string sectorName, List<ActionItem>? existingSubActions, int maxDepth = 2, int startChild = -1)
	{
		InitializeComponent();
		AppThemeManager.ApplyTheme(this, ConfigManager.CurrentConfig?.AppTheme ?? "System");
		_maxDepth = Math.Clamp(maxDepth, 2, 5);
		_branch.Add(new ActionItem
		{
			Name = directionLabel + " · " + sectorName,
			SubActions = existingSubActions?.Select(a => a.Clone()).ToList() ?? new List<ActionItem>()
		});
		Title = I18n.T("MenuEditBranch");
		AddSubActionButton.Content = I18n.T("MenuAddChild");
		BranchBackButton.Content = I18n.T("DeepMenuBack");
		EmptyBranchText.Text = I18n.T("MenuEmpty");
		SubActionsItemsControl.ItemsSource = SubSlots;
		LoadBranch();
		if ((uint)startChild < (uint)SubSlots.Count) EnterBranch(SubSlots[startChild]);
	}

	private void LoadBranch()
	{
		SubSlots.Clear();
		if (_branch[^1].SubActions is { } children)
		{
			foreach (var action in children) SubSlots.Add(CreateSlot(action));
		}
		SectorInfoTitle.Text = string.Join("  ›  ", _branch.Select(a => a.Name));
		SubCountLimitText.Text = I18n.TF("MenuEditLevel", _branch.Count + 1, _maxDepth);
		BranchBackButton.Visibility = _branch.Count == 1 ? Visibility.Collapsed : Visibility.Visible;
		ReindexSubSlots();
	}

	private SubSlotViewModel CreateSlot(ActionItem action) => new()
	{
		Action = action, CanEditChildren = _branch.Count + 1 < _maxDepth
	};

	private void StoreBranch()
	{
		// Preserve null on untouched leaves, including after browsing an empty submenu.
		if (SubSlots.Count != 0 || _branch[^1].SubActions != null)
			_branch[^1].SubActions = SubSlots.Select(slot => slot.Action).ToList();
	}

	internal void EnterBranch(SubSlotViewModel slot)
	{
		if (!slot.CanEditChildren || !SubSlots.Contains(slot)) return;
		StoreBranch();
		_branch.Add(slot.Action);
		LoadBranch();
	}

	internal void LeaveBranch()
	{
		if (_branch.Count == 1) return;
		StoreBranch();
		_branch.RemoveAt(_branch.Count - 1);
		LoadBranch();
	}

	private void EnterBranch_Click(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { DataContext: SubSlotViewModel slot }) EnterBranch(slot);
	}

	private void BranchBack_Click(object sender, RoutedEventArgs e) => LeaveBranch();

	internal List<ActionItem> GetDraft()
	{
		StoreBranch();
		return _branch[0].SubActions ?? new List<ActionItem>();
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
		AddSubActionButton.IsEnabled = SubSlots.Count < SlotLimit;
	}

	private void AddSubActionButton_Click(object sender, RoutedEventArgs e)
	{
		if (SubSlots.Count >= SlotLimit) return;
		SubSlots.Add(CreateSlot(new ActionItem
		{
			Name = I18n.TF("EditSubActionNumber", SubSlots.Count + 1), Type = "Hotkey"
		}));
		ReindexSubSlots();
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
			AppMessageBox.Show(this, "打开按键拼装器失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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
		var result = GetDraft();
		if (!ValidateBranch(result, 2)) return;
		ResultSubActions = result;
		DialogResult = true;
		Close();
	}

	private bool ValidateBranch(IEnumerable<ActionItem> actions, int level)
	{
		foreach (var action in actions)
		{
			var validation = PluginHost.ValidateActionParameters(action);
			if (!validation.IsValid)
			{
				AppMessageBox.Show(this, validation.Describe() ?? "", action.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
				return false;
			}
			if (level < _maxDepth && action.SubActions != null && !ValidateBranch(action.SubActions, level + 1)) return false;
		}
		return true;
	}

	private void CancelButton_Click(object sender, RoutedEventArgs e)
	{
		DialogResult = false;
		Close();
	}
}
