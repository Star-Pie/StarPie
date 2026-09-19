using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace WinPieGestures;

public partial class ProgramPickerWindow : Window
{
	public class ProgramItem
	{
		public string Name { get; set; } = "";

		public string Path { get; set; } = "";

		public string FriendlyPath { get; set; } = "";

		public string ExeName { get; set; } = "";

		public string AppType { get; set; } = "Win32";

		public string Pinyin { get; set; } = "";

		public string PinyinInitials { get; set; } = "";

		public int UsageCount { get; set; }

		public DateTime LastUsed { get; set; } = DateTime.MinValue;

		public BitmapSource? IconSource { get; set; }

		public string TagDisplay => AppType switch
		{
			"WinStore" => "微软商店", 
			"MSIX" => "MSIX应用", 
			"System" => "系统内置", 
			"Portable" => "绿色便携", 
			_ => "桌面应用", 
		};

		public string BadgeBackground => AppType switch
		{
			"WinStore" => "#186366F1", 
			"MSIX" => "#188B5CF6", 
			"System" => "#180EA5E9", 
			"Portable" => "#18F97316", 
			_ => "#1464748B", 
		};

		public string BadgeForeground => AppType switch
		{
			"WinStore" => "#6366F1", 
			"MSIX" => "#8B5CF6", 
			"System" => "#0284C7", 
			"Portable" => "#F97316", 
			_ => "#64748B", 
		};
	}

	public class CachedProgramEntry
	{
		public string Name { get; set; } = "";

		public string Path { get; set; } = "";

		public string FriendlyPath { get; set; } = "";

		public string ExeName { get; set; } = "";

		public string AppType { get; set; } = "Win32";

		public string Pinyin { get; set; } = "";

		public string PinyinInitials { get; set; } = "";

		public int UsageCount { get; set; }

		public DateTime LastUsed { get; set; } = DateTime.MinValue;
	}

	private readonly List<ProgramItem> _allPrograms = new List<ProgramItem>();

	private readonly ObservableCollection<ProgramItem> _displayedPrograms = new ObservableCollection<ProgramItem>();

	private CancellationTokenSource? _searchCts;

	public string SelectedPath { get; private set; } = "";

	public string SelectedName { get; private set; } = "";

	public ProgramPickerWindow()
	{
		InitializeComponent();
		AppThemeManager.ApplyTheme(this, AppThemeManager.CurrentEffectiveTheme);
		ProgramsListView.ItemsSource = _displayedPrograms;
		ApplyLocalization();
	}

	private void ApplyLocalization()
	{
		base.Title = I18n.T("ProgramPickerTitle") + " - StarPie";
		if (HeaderTitleText != null)
		{
			HeaderTitleText.Text = I18n.T("ProgramPickerHeader");
		}
		if (SearchPlaceholderText != null)
		{
			SearchPlaceholderText.Text = I18n.T("ProgramPickerPlaceholder");
		}
		if (StatusTextBlock != null)
		{
			StatusTextBlock.Text = I18n.T("ProgramPickerScanning");
		}
		if (RefreshButton != null)
		{
			RefreshButton.Content = I18n.T("ProgramPickerRefresh");
		}
		if (ManualBrowseButton != null)
		{
			ManualBrowseButton.Content = I18n.T("BtnManualBrowse");
		}
		if (OkButton != null)
		{
			OkButton.Content = I18n.T("BtnConfirm");
		}
		if (CancelButton != null)
		{
			CancelButton.Content = I18n.T("BtnCancel");
		}
	}

	private async void Window_Loaded(object sender, RoutedEventArgs e)
	{
		await LoadProgramsAsync(forceRescan: false);
	}

	private async Task LoadProgramsAsync(bool forceRescan)
	{
		StatusTextBlock.Visibility = Visibility.Visible;
		StatusTextBlock.Text = I18n.T("ProgramPickerScanning");
		try
		{
			List<ProgramItem> list = null;
			if (!forceRescan)
			{
				list = await Task.Run(() => LoadCache());
			}
			if (list == null || list.Count == 0)
			{
				list = await Task.Run(() => ScanInstalledPrograms());
				SaveCacheAsync(list);
			}
			_allPrograms.Clear();
			_allPrograms.AddRange(list);
			UpdateDisplayedList("");
			StatusTextBlock.Visibility = Visibility.Collapsed;
		}
		catch (Exception ex)
		{
			StatusTextBlock.Text = I18n.T("Error") + ": " + ex.Message;
			StatusTextBlock.Foreground = Brushes.Red;
		}
	}

	private async void RefreshButton_Click(object sender, RoutedEventArgs e)
	{
		await LoadProgramsAsync(forceRescan: true);
	}

	private List<ProgramItem> ScanInstalledPrograms()
	{
		Dictionary<string, ProgramItem> dictionary = new Dictionary<string, ProgramItem>(StringComparer.OrdinalIgnoreCase);
		ScanShellAppsFolder(dictionary);
		AddSystemApps(dictionary);
		ScanStartMenuShortcuts(dictionary);
		ScanDesktopShortcuts(dictionary);
		ScanUserAppDataPrograms(dictionary);
		ScanWindowsApps(dictionary);
		ScanRegistryAppPaths(dictionary);
		ScanRegistryUninstall(dictionary);
		ScanProgramFilesTopLevel(dictionary);
		ScanAllDrivesProgramDirectories(dictionary);
		List<ProgramItem> list = dictionary.Values.ToList();
		list.Sort((ProgramItem a, ProgramItem b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
		return list;
	}

	private void ScanShellAppsFolder(Dictionary<string, ProgramItem> dict)
	{
		try
		{
			Type typeFromProgID = Type.GetTypeFromProgID("Shell.Application");
			if (typeFromProgID == null)
			{
				return;
			}
			dynamic val = Activator.CreateInstance(typeFromProgID);
			dynamic val2 = val.NameSpace("shell:AppsFolder");
			if (val2 == null)
			{
				return;
			}
			foreach (dynamic item in val2.Items())
			{
				try
				{
					string text = item.Name?.ToString() ?? "";
					string text2 = item.Path?.ToString() ?? "";
					if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(text2))
					{
						continue;
					}
					string text3 = text.ToLowerInvariant();
					if (text3.Contains("卸载") || text3.Contains("uninstall") || text3.Contains("redistributable") || text3.Contains("setup") || text3.Contains("update") || text3.Contains("sdk documentation"))
					{
						continue;
					}
					string text4 = "Win32";
					if (text2.Contains("!") || (text2.Contains("_") && !text2.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
					{
						text4 = "WinStore";
					}
					else if (text2.EndsWith(".msix", StringComparison.OrdinalIgnoreCase) || text2.Contains("WindowsApps"))
					{
						text4 = "MSIX";
					}
					else if (text2.Contains("System32") || text2.Contains("SysWOW64"))
					{
						text4 = "System";
					}
					string key = ((!string.IsNullOrEmpty(text2)) ? text2 : text);
					if (dict.ContainsKey(key))
					{
						ProgramItem programItem = dict[key];
						if (string.Equals(programItem.AppType, "Win32", StringComparison.OrdinalIgnoreCase) && text4 == "WinStore")
						{
							programItem.AppType = "WinStore";
						}
						continue;
					}
					BitmapSource icon = IconHelper.GetIcon(text2);
					string exeName = (text2.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? Path.GetFileNameWithoutExtension(text2) : text);
					dict[key] = new ProgramItem
					{
						Name = text,
						Path = text2,
						FriendlyPath = ((text4 == "WinStore" || text4 == "MSIX") ? ("shell:AppsFolder\\" + text2) : text2),
						ExeName = exeName,
						AppType = text4,
						Pinyin = PinyinHelper.GetFullPinyin(text),
						PinyinInitials = PinyinHelper.GetInitials(text),
						IconSource = icon
					};
				}
				catch
				{
				}
			}
		}
		catch (Exception)
		{
		}
	}

	private static bool IsJunkOrHelperExecutable(string displayName, string exePath)
	{
		if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
		{
			return true;
		}
		if (!exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		try
		{
			if (new FileInfo(exePath).Length == 0L)
			{
				return true;
			}
		}
		catch
		{
			return true;
		}
		// 下面几个 if 里的中文关键词（「卸载」「意见反馈」「修复」「使用说明」「用户手册」
		// 「帮助」「官方网站」「访问官网」）匹配的是<b>第三方软件自己的名字</b>，
		// 不是本程序的 UI 文案 —— 数据里本来就带中文，切换界面语言不影响它。
		// 做 i18n 清理时把这几处按「可接受」排除，别顺手删掉：
		// 删了之后中文软件的卸载程序与说明文档会重新出现在程序挑选器里。
		string text = Path.GetFileName(exePath).ToLowerInvariant();
		string text2 = displayName.ToLowerInvariant() + " " + text;
		string text3 = exePath.ToLowerInvariant();
		if (text2.Contains("uninstall") || text2.Contains("unins000") || text2.Contains("unins001") || text2.Contains("uninst") || text2.Contains("卸载") || text2.Contains("remove") || text2.Contains("deleter") || text2.Contains("cleanup"))
		{
			return true;
		}
		if (text2.Contains("setup") || text2.Contains("installer") || text2.Contains("install_helper") || text2.Contains("msiexec") || text2.Contains("vcredist") || text2.Contains("dxsetup") || text2.Contains("dotnetfx") || text2.Contains("ndp4") || text2.Contains("vc_redist") || text2.Contains("setup_helper") || text2.Contains("dpinst"))
		{
			return true;
		}
		if (text2.Contains("update") || text2.Contains("updater") || text2.Contains("autoupdate") || text2.Contains("patcher") || text2.Contains("crashpad") || text2.Contains("crash_report") || text2.Contains("crashreporter") || text2.Contains("feedback") || text2.Contains("意见反馈") || text2.Contains("bugreport"))
		{
			return true;
		}
		if (text2.Contains("diagnostic") || text2.Contains("repair") || text2.Contains("修复") || text2.Contains("fix") || text2.Contains("troubleshoot") || text2.Contains("elevate") || text2.Contains("helper") || text2.Contains("launcher_helper") || text2.Contains("nwjc") || text2.Contains("chromedriver") || text2.Contains("geckodriver") || text2.Contains("phantomjs") || text2.Contains("conhost") || text2.Contains("ffmpeg") || text2.Contains("ffprobe") || text2.Contains("winpty") || text2.Contains("openconsole") || text2.Contains("rcedit") || text2.Contains("language_server") || text2.Contains("webm_encoder") || text2.Contains("compil32") || text2.Contains("iscc") || text2.Contains("islzma") || text2.Contains("iediag"))
		{
			return true;
		}
		if (text3.Contains("\\resources\\") || text3.Contains("\\node_modules\\") || text3.Contains("\\extensions\\") || text3.Contains("\\site-packages\\") || text3.Contains("\\packages\\") || text3.Contains("\\internal\\") || text3.Contains("\\temp\\") || text3.Contains("\\tmp\\") || text3.Contains("\\cache\\") || text3.Contains("\\plugins\\") || text3.Contains("\\sdk\\") || text3.Contains("\\tcl\\") || text3.Contains("\\scripts\\"))
		{
			return true;
		}
		if (text3.Contains("python") && (text3.Contains("\\scripts\\") || text3.Contains("\\site-packages\\") || text3.Contains("\\tcl\\") || text3.Contains("\\lib\\")) && text != "python.exe" && text != "pythonw.exe")
		{
			return true;
		}
		if (text2.Contains("readme") || text2.Contains("license") || text2.Contains("changelog") || text2.Contains("manual") || text2.Contains("使用说明") || text2.Contains("用户手册") || text2.Contains("help") || text2.Contains("帮助") || text2.Contains("website") || text2.Contains("官方网站") || text2.Contains("访问官网") || text2.Contains("homepage") || text2.Contains("forum") || text2.Contains("bbs"))
		{
			return true;
		}
		return false;
	}

	private void AddProgramEntry(Dictionary<string, ProgramItem> dict, string displayName, string exePath, string appType = "Win32")
	{
		if (string.IsNullOrEmpty(exePath))
		{
			return;
		}
		string text;
		try
		{
			text = (File.Exists(exePath) ? Path.GetFullPath(exePath) : exePath);
		}
		catch
		{
			text = exePath;
		}
		if (File.Exists(text) && IsJunkOrHelperExecutable(displayName, text))
		{
			return;
		}
		if (dict.TryGetValue(text, out ProgramItem value))
		{
			string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(text);
			if (string.Equals(value.Name, fileNameWithoutExtension, StringComparison.OrdinalIgnoreCase) && !string.Equals(displayName, fileNameWithoutExtension, StringComparison.OrdinalIgnoreCase))
			{
				value.Name = displayName;
			}
			return;
		}
		BitmapSource icon = IconHelper.GetIcon(text);
		string fileNameWithoutExtension2 = Path.GetFileNameWithoutExtension(text);
		dict[text] = new ProgramItem
		{
			Name = displayName,
			Path = text,
			FriendlyPath = text,
			ExeName = fileNameWithoutExtension2,
			AppType = appType,
			Pinyin = PinyinHelper.GetFullPinyin(displayName),
			PinyinInitials = PinyinHelper.GetInitials(displayName),
			IconSource = icon
		};
	}

	private void AddSystemApps(Dictionary<string, ProgramItem> dict)
	{
		string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
		string folderPath2 = Environment.GetFolderPath(Environment.SpecialFolder.System);
		AddProgramEntry(dict, "文件资源管理器 (Explorer)", Path.Combine(folderPath, "explorer.exe"), "System");
		AddProgramEntry(dict, "记事本 (Notepad)", Path.Combine(folderPath2, "notepad.exe"), "System");
		AddProgramEntry(dict, "任务管理器 (Taskmgr)", Path.Combine(folderPath2, "taskmgr.exe"), "System");
		AddProgramEntry(dict, "计算器 (Calculator)", Path.Combine(folderPath2, "calc.exe"), "System");
		AddProgramEntry(dict, "截图工具 (SnippingTool)", Path.Combine(folderPath2, "SnippingTool.exe"), "System");
		AddProgramEntry(dict, "命令提示符 (CMD)", Path.Combine(folderPath2, "cmd.exe"), "System");
		AddProgramEntry(dict, "Windows PowerShell", Path.Combine(folderPath2, "WindowsPowerShell\\v1.0\\powershell.exe"), "System");
		AddProgramEntry(dict, "画图 (MSPaint)", Path.Combine(folderPath2, "mspaint.exe"), "System");
		AddProgramEntry(dict, "注册表编辑器 (Regedit)", Path.Combine(folderPath, "regedit.exe"), "System");
		AddProgramEntry(dict, "控制面板 (Control Panel)", Path.Combine(folderPath2, "control.exe"), "System");
	}

	private void ScanStartMenuShortcuts(Dictionary<string, ProgramItem> dict)
	{
		foreach (string item in new List<string>
		{
			Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
			Environment.GetFolderPath(Environment.SpecialFolder.Programs),
			Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
			Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft\\Windows\\Start Menu\\Programs"),
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft\\Windows\\Start Menu\\Programs")
		}.Distinct<string>(StringComparer.OrdinalIgnoreCase))
		{
			if (!Directory.Exists(item))
			{
				continue;
			}
			try
			{
				string[] files = Directory.GetFiles(item, "*.lnk", SearchOption.AllDirectories);
				foreach (string obj in files)
				{
					string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(obj);
					if (IconHelper.ResolveShortcutTarget(obj, out string targetPath, out string _, out int _) && !string.IsNullOrEmpty(targetPath) && File.Exists(targetPath) && targetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
					{
						AddProgramEntry(dict, fileNameWithoutExtension, targetPath);
					}
				}
			}
			catch (Exception)
			{
			}
		}
	}

	private void ScanDesktopShortcuts(Dictionary<string, ProgramItem> dict)
	{
		foreach (string item in new List<string>
		{
			Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
			Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
		}.Distinct<string>(StringComparer.OrdinalIgnoreCase))
		{
			if (!Directory.Exists(item))
			{
				continue;
			}
			try
			{
				string[] files = Directory.GetFiles(item, "*.lnk", SearchOption.TopDirectoryOnly);
				foreach (string obj in files)
				{
					string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(obj);
					if (IconHelper.ResolveShortcutTarget(obj, out string targetPath, out string _, out int _) && !string.IsNullOrEmpty(targetPath) && File.Exists(targetPath) && targetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
					{
						AddProgramEntry(dict, fileNameWithoutExtension, targetPath);
					}
				}
			}
			catch
			{
			}
		}
	}

	private void ScanUserAppDataPrograms(Dictionary<string, ProgramItem> dict)
	{
		try
		{
			string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");
			if (!Directory.Exists(path))
			{
				return;
			}
			string[] directories = Directory.GetDirectories(path);
			foreach (string path2 in directories)
			{
				string fileName = Path.GetFileName(path2);
				try
				{
					string[] files = Directory.GetFiles(path2, "*.exe", SearchOption.TopDirectoryOnly);
					foreach (string text in files)
					{
						string displayName = (string.Equals(Path.GetFileNameWithoutExtension(text), fileName, StringComparison.OrdinalIgnoreCase) ? fileName : (fileName + " (" + Path.GetFileNameWithoutExtension(text) + ")"));
						AddProgramEntry(dict, displayName, text);
					}
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
	}

	private void ScanWindowsApps(Dictionary<string, ProgramItem> dict)
	{
		try
		{
			string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft\\WindowsApps");
			if (Directory.Exists(path))
			{
				string[] files = Directory.GetFiles(path, "*.exe", SearchOption.TopDirectoryOnly);
				foreach (string text in files)
				{
					string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(text);
					AddProgramEntry(dict, fileNameWithoutExtension, text);
				}
			}
		}
		catch
		{
		}
	}

	private void ScanRegistryAppPaths(Dictionary<string, ProgramItem> dict)
	{
		(RegistryHive, RegistryView)[] array = new(RegistryHive, RegistryView)[3]
		{
			(RegistryHive.LocalMachine, RegistryView.Registry64),
			(RegistryHive.LocalMachine, RegistryView.Registry32),
			(RegistryHive.CurrentUser, RegistryView.Default)
		};
		for (int i = 0; i < array.Length; i++)
		{
			var (hKey, view) = array[i];
			try
			{
				using RegistryKey registryKey = RegistryKey.OpenBaseKey(hKey, view);
				using RegistryKey registryKey2 = registryKey.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\App Paths");
				if (registryKey2 == null)
				{
					continue;
				}
				string[] subKeyNames = registryKey2.GetSubKeyNames();
				foreach (string text in subKeyNames)
				{
					try
					{
						using RegistryKey registryKey3 = registryKey2.OpenSubKey(text);
						string text2 = registryKey3?.GetValue("")?.ToString();
						if (!string.IsNullOrEmpty(text2))
						{
							string text3 = Environment.ExpandEnvironmentVariables(text2.Trim().Trim('"'));
							if (File.Exists(text3))
							{
								string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(text);
								AddProgramEntry(dict, fileNameWithoutExtension, text3);
							}
						}
					}
					catch
					{
					}
				}
			}
			catch
			{
			}
		}
	}

	private void ScanRegistryUninstall(Dictionary<string, ProgramItem> dict)
	{
		(RegistryHive, RegistryView)[] array = new(RegistryHive, RegistryView)[3]
		{
			(RegistryHive.LocalMachine, RegistryView.Registry64),
			(RegistryHive.LocalMachine, RegistryView.Registry32),
			(RegistryHive.CurrentUser, RegistryView.Default)
		};
		for (int i = 0; i < array.Length; i++)
		{
			var (hKey, view) = array[i];
			try
			{
				using RegistryKey registryKey = RegistryKey.OpenBaseKey(hKey, view);
				using RegistryKey registryKey2 = registryKey.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall");
				if (registryKey2 == null)
				{
					continue;
				}
				string[] subKeyNames = registryKey2.GetSubKeyNames();
				foreach (string name in subKeyNames)
				{
					try
					{
						using RegistryKey registryKey3 = registryKey2.OpenSubKey(name);
						if (registryKey3 == null || (registryKey3.GetValue("SystemComponent") is int num && num == 1) || registryKey3.GetValue("ParentKeyName") != null)
						{
							continue;
						}
						string displayName = registryKey3.GetValue("DisplayName")?.ToString()?.Trim();
						if (string.IsNullOrEmpty(displayName) || displayName.StartsWith("KB", StringComparison.OrdinalIgnoreCase) || displayName.StartsWith("Security Update", StringComparison.OrdinalIgnoreCase) || displayName.StartsWith("Microsoft Visual C++", StringComparison.OrdinalIgnoreCase) || displayName.StartsWith("Windows Software Development Kit", StringComparison.OrdinalIgnoreCase))
						{
							continue;
						}
						string text = registryKey3.GetValue("DisplayIcon")?.ToString();
						string text2 = registryKey3.GetValue("InstallLocation")?.ToString();
						string text3 = "";
						if (!string.IsNullOrEmpty(text))
						{
							string text4 = Environment.ExpandEnvironmentVariables(text.Split(',')[0].Trim().Trim('"'));
							if (File.Exists(text4) && text4.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
							{
								text3 = text4;
							}
						}
						if (string.IsNullOrEmpty(text3) && !string.IsNullOrEmpty(text2) && Directory.Exists(text2))
						{
							try
							{
								string text5 = Directory.GetFiles(text2, "*.exe", SearchOption.TopDirectoryOnly).FirstOrDefault((string e) => !IsJunkOrHelperExecutable(displayName, e));
								if (text5 != null)
								{
									text3 = text5;
								}
							}
							catch
							{
							}
						}
						if (!string.IsNullOrEmpty(text3) && File.Exists(text3))
						{
							AddProgramEntry(dict, displayName, text3);
						}
					}
					catch
					{
					}
				}
			}
			catch
			{
			}
		}
	}

	private void ScanProgramFilesTopLevel(Dictionary<string, ProgramItem> dict)
	{
		List<string> list = new List<string>();
		string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
		string folderPath2 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
		if (Directory.Exists(folderPath))
		{
			list.Add(folderPath);
		}
		if (Directory.Exists(folderPath2) && !string.Equals(folderPath, folderPath2, StringComparison.OrdinalIgnoreCase))
		{
			list.Add(folderPath2);
		}
		foreach (string item in list)
		{
			try
			{
				string[] directories = Directory.GetDirectories(item);
				foreach (string path in directories)
				{
					string fileName = Path.GetFileName(path);
					if (fileName.Equals("Common Files", StringComparison.OrdinalIgnoreCase) || fileName.Equals("Windows Defender", StringComparison.OrdinalIgnoreCase) || fileName.Equals("Windows Mail", StringComparison.OrdinalIgnoreCase) || fileName.Equals("Windows Media Player", StringComparison.OrdinalIgnoreCase) || fileName.Equals("Windows NT", StringComparison.OrdinalIgnoreCase) || fileName.Equals("Windows Photo Viewer", StringComparison.OrdinalIgnoreCase) || fileName.Equals("WindowsPowerShell", StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}
					try
					{
						string[] files = Directory.GetFiles(path, "*.exe", SearchOption.TopDirectoryOnly);
						foreach (string text in files)
						{
							AddProgramEntry(dict, fileName + " (" + Path.GetFileNameWithoutExtension(text) + ")", text);
						}
					}
					catch
					{
					}
				}
			}
			catch
			{
			}
		}
	}

	private void ScanAllDrivesProgramDirectories(Dictionary<string, ProgramItem> dict)
	{
		try
		{
			var drives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed);
			foreach (var drive in drives)
			{
				try
				{
					var rootDirs = Directory.GetDirectories(drive.RootDirectory.FullName);
					foreach (var dir in rootDirs)
					{
						string dirName = Path.GetFileName(dir);
						if (string.IsNullOrEmpty(dirName)) continue;

						// 过滤系统核心卷与垃圾箱
						if (dirName.StartsWith("$") ||
						    dirName.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase) ||
						    dirName.Equals("Windows", StringComparison.OrdinalIgnoreCase) ||
						    dirName.Equals("Recovery", StringComparison.OrdinalIgnoreCase) ||
						    dirName.Equals("MSOCache", StringComparison.OrdinalIgnoreCase))
						{
							continue;
						}

						// 1. 扫描根级目录下的直接可执行程序
						try
						{
							string[] exes = Directory.GetFiles(dir, "*.exe", SearchOption.TopDirectoryOnly);
							foreach (string exe in exes)
							{
								string exeName = Path.GetFileNameWithoutExtension(exe);
								AddProgramEntry(dict, $"{dirName} ({exeName})", exe, "Portable");
							}
						}
						catch { }

						// 2. 深入 1 级子目录（常见如 H:\PS2024\Adobe Photoshop 2024\Photoshop.exe 或 K:\QQ\Bin\QQ.exe）
						try
						{
							string[] subDirs = Directory.GetDirectories(dir);
							foreach (string subDir in subDirs)
							{
								string subName = Path.GetFileName(subDir);
								if (string.IsNullOrEmpty(subName) || subName.StartsWith(".") ||
								    subName.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
								    subName.Equals("temp", StringComparison.OrdinalIgnoreCase) ||
								    subName.Equals("cache", StringComparison.OrdinalIgnoreCase))
								{
									continue;
								}

								try
								{
									string[] subExes = Directory.GetFiles(subDir, "*.exe", SearchOption.TopDirectoryOnly);
									foreach (string subExe in subExes)
									{
										string subExeName = Path.GetFileNameWithoutExtension(subExe);
										AddProgramEntry(dict, $"{subExeName} ({dirName})", subExe, "Portable");
									}
								}
								catch { }
							}
						}
						catch { }
					}
				}
				catch { }
			}
		}
		catch { }
	}

	private void UpdateDisplayedList(string filter)
	{
		_displayedPrograms.Clear();
		List<ProgramItem> list = ((!string.IsNullOrWhiteSpace(filter)) ? (from p in _allPrograms
			select new
			{
				Item = p,
				Score = FuzzyMatcher.Score(p, filter)
			} into x
			where x.Score > 0.0
			orderby x.Score descending
			select x.Item).Take(150).ToList() : _allPrograms.OrderByDescending((ProgramItem p) => p.UsageCount).ThenBy<ProgramItem, string>((ProgramItem p) => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList());
		foreach (ProgramItem item in list)
		{
			_displayedPrograms.Add(item);
		}
	}

	private async void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
	{
		string text = SearchTextBox.Text;
		if (SearchPlaceholderText != null)
		{
			SearchPlaceholderText.Visibility = ((!string.IsNullOrEmpty(text)) ? Visibility.Collapsed : Visibility.Visible);
		}
		_searchCts?.Cancel();
		CancellationTokenSource cts = (_searchCts = new CancellationTokenSource());
		try
		{
			await Task.Delay(35, cts.Token);
			if (cts.Token.IsCancellationRequested)
			{
				return;
			}
			List<ProgramItem> list = await Task.Run(() => string.IsNullOrWhiteSpace(text) ? _allPrograms.OrderByDescending((ProgramItem p) => p.UsageCount).ThenBy<ProgramItem, string>((ProgramItem p) => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList() : (from p in _allPrograms
				select new
				{
					Item = p,
					Score = FuzzyMatcher.Score(p, text)
				} into x
				where x.Score > 0.0
				orderby x.Score descending
				select x.Item).Take(100).ToList(), cts.Token);
			if (cts.Token.IsCancellationRequested)
			{
				return;
			}

			// 如果输入了关键词，通过 StarPie 原生引擎全盘穿透搜索免安装绿色程序 (.exe)
			List<ProgramItem> portableMatches = new List<ProgramItem>();
			string queryTrimmed = text.Trim();
			if (!string.IsNullOrEmpty(queryTrimmed) && queryTrimmed.Length >= 2)
			{
				var nativeItems = await NativeSearchEngine.SearchAsync(queryTrimmed, "App", 50, cts.Token);
				if (cts.Token.IsCancellationRequested) return;

				var existingPaths = new HashSet<string>(list.Select(m => m.Path), StringComparer.OrdinalIgnoreCase);
				foreach (var ev in nativeItems)
				{
					if (existingPaths.Contains(ev.FullPath)) continue;
					existingPaths.Add(ev.FullPath);

					portableMatches.Add(new ProgramItem
					{
						Name = Path.GetFileNameWithoutExtension(ev.FileName),
						Path = ev.FullPath,
						FriendlyPath = ev.FullPath,
						ExeName = Path.GetFileNameWithoutExtension(ev.FileName),
						AppType = "Portable",
						Pinyin = PinyinHelper.GetFullPinyin(ev.FileName),
						PinyinInitials = PinyinHelper.GetInitials(ev.FileName),
						IconSource = IconHelper.GetIcon(ev.FullPath)
					});
				}
			}

			if (cts.Token.IsCancellationRequested) return;

			_displayedPrograms.Clear();
			foreach (ProgramItem item in list)
			{
				_displayedPrograms.Add(item);
			}
			foreach (ProgramItem item in portableMatches)
			{
				_displayedPrograms.Add(item);
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	private void ProgramsListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
	{
		SelectAndClose();
	}

	private void Ok_Click(object sender, RoutedEventArgs e)
	{
		SelectAndClose();
	}

	private void Cancel_Click(object sender, RoutedEventArgs e)
	{
		base.DialogResult = false;
		Close();
	}

	private void ManualBrowse_Click(object sender, RoutedEventArgs e)
	{
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Filter = "可执行程序 (*.exe)|*.exe|快捷方式 (*.lnk)|*.lnk|所有文件 (*.*)|*.*",
			Title = I18n.T("BtnBrowseApp")
		};
		if (openFileDialog.ShowDialog(this) == true)
		{
			string fileName = openFileDialog.FileName;
			if (fileName.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && IconHelper.ResolveShortcutTarget(fileName, out string targetPath, out string _, out int _) && !string.IsNullOrEmpty(targetPath) && File.Exists(targetPath))
			{
				SelectedPath = targetPath;
				SelectedName = Path.GetFileNameWithoutExtension(fileName);
			}
			else
			{
				SelectedPath = fileName;
				SelectedName = Path.GetFileNameWithoutExtension(fileName);
			}
			base.DialogResult = true;
			Close();
		}
	}

	private void SelectAndClose()
	{
		if (ProgramsListView.SelectedItem is ProgramItem programItem)
		{
			SelectedPath = programItem.Path;
			SelectedName = programItem.Name;
			programItem.UsageCount++;
			programItem.LastUsed = DateTime.Now;
			SaveCacheAsync(_allPrograms);
			base.DialogResult = true;
			Close();
		}
		else
		{
			MessageBox.Show("请选择一个程序，或者点击“手动浏览文件...”", "未选择", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private static async Task SaveCacheAsync(List<ProgramItem> items)
	{
		try
		{
			string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StarPie");
			if (!Directory.Exists(text))
			{
				Directory.CreateDirectory(text);
			}
			string path = Path.Combine(text, "program_cache.json");
			string contents = JsonSerializer.Serialize(items.Select((ProgramItem p) => new CachedProgramEntry
			{
				Name = p.Name,
				Path = p.Path,
				FriendlyPath = p.FriendlyPath,
				ExeName = p.ExeName,
				AppType = p.AppType,
				Pinyin = p.Pinyin,
				PinyinInitials = p.PinyinInitials,
				UsageCount = p.UsageCount,
				LastUsed = p.LastUsed
			}).ToList(), new JsonSerializerOptions
			{
				WriteIndented = true
			});
			await File.WriteAllTextAsync(path, contents);
		}
		catch
		{
		}
	}

	private static List<ProgramItem>? LoadCache()
	{
		try
		{
			string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StarPie", "program_cache.json");
			if (!File.Exists(path))
			{
				return null;
			}
			List<CachedProgramEntry> list = JsonSerializer.Deserialize<List<CachedProgramEntry>>(File.ReadAllText(path));
			if (list == null || list.Count == 0)
			{
				return null;
			}
			List<ProgramItem> list2 = new List<ProgramItem>();
			foreach (CachedProgramEntry item2 in list)
			{
				bool flag = item2.AppType == "WinStore" || item2.AppType == "MSIX" || item2.AppType == "System" || item2.Path.Contains("!") || item2.Path.StartsWith("shell:AppsFolder", StringComparison.OrdinalIgnoreCase);
				if (flag || File.Exists(item2.Path) || Directory.Exists(item2.Path))
				{
					ProgramItem item = new ProgramItem
					{
						Name = item2.Name,
						Path = item2.Path,
						FriendlyPath = item2.FriendlyPath,
						ExeName = (string.IsNullOrEmpty(item2.ExeName) ? Path.GetFileNameWithoutExtension(item2.Path) : item2.ExeName),
						AppType = ((!string.IsNullOrEmpty(item2.AppType)) ? item2.AppType : (flag ? "WinStore" : "Win32")),
						Pinyin = (string.IsNullOrEmpty(item2.Pinyin) ? PinyinHelper.GetFullPinyin(item2.Name) : item2.Pinyin),
						PinyinInitials = (string.IsNullOrEmpty(item2.PinyinInitials) ? PinyinHelper.GetInitials(item2.Name) : item2.PinyinInitials),
						UsageCount = item2.UsageCount,
						LastUsed = item2.LastUsed,
						IconSource = IconHelper.GetIcon(item2.Path)
					};
					list2.Add(item);
				}
			}
			return (list2.Count > 0) ? list2 : null;
		}
		catch
		{
			return null;
		}
	}

	protected override void OnClosed(EventArgs e)
	{
		base.OnClosed(e);
		_searchCts?.Cancel();
		_allPrograms.Clear();
		_displayedPrograms.Clear();
		ProgramsListView.ItemsSource = null;
		IconHelper.TrimDynamicCache();
		MemoryOptimizer.TrimMemory(force: false);
	}
}