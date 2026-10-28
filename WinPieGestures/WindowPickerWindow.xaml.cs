using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinPieGestures;

public enum WindowPickerMode
{
	ExecutablePath,
	ProcessNameOnly
}

public partial class WindowPickerWindow : Window
{
	public class RunningWindowInfo
	{
		public nint Hwnd { get; set; }
		public uint ProcessId { get; set; }
		public string WindowTitle { get; set; } = "";
		public string ProcessName { get; set; } = "";
		public string CleanProcessName { get; set; } = "";
		public string ExecutablePath { get; set; } = "";
		public ImageSource? IconSource { get; set; }
	}

	[DllImport("user32.dll")]
	private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);
	private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool IsWindowVisible(nint hWnd);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);

	[DllImport("user32.dll")]
	private static extern int GetWindowTextLength(nint hWnd);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetClassName(nint hWnd, StringBuilder lpClassName, int nMaxCount);

	[DllImport("user32.dll")]
	private static extern nint WindowFromPoint(POINT Point);

	[DllImport("user32.dll")]
	private static extern nint GetAncestor(nint hwnd, uint gaFlags);
	private const uint GA_ROOT = 2;

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetCursorPos(out POINT lpPoint);

	[StructLayout(LayoutKind.Sequential)]
	public struct POINT
	{
		public int X;
		public int Y;
	}

	public WindowPickerMode Mode { get; set; } = WindowPickerMode.ExecutablePath;
	public string SelectedPath { get; private set; } = "";
	public string SelectedProcessName { get; private set; } = "";
	public string SelectedTitle { get; private set; } = "";

	private readonly ObservableCollection<RunningWindowInfo> _windows = new();
	private readonly List<RunningWindowInfo> _allWindows = new();
	private bool _isDraggingCrosshair = false;
	private uint _ownProcessId = 0;

	public WindowPickerWindow(WindowPickerMode mode = WindowPickerMode.ExecutablePath)
	{
		Mode = mode;
		InitializeComponent();

		try
		{
			_ownProcessId = (uint)Process.GetCurrentProcess().Id;
		}
		catch { }

		try
		{
			AppThemeManager.ApplyTheme(this, ConfigManager.CurrentConfig?.AppTheme ?? "System");
		}
		catch { }

		if (Mode == WindowPickerMode.ProcessNameOnly)
		{
			HeaderTitleText.Text = "🎯 捕捉运行中窗口以获取进程名称";
			HeaderDescText.Text = "请选取或瞄准要加入平铺排除名单的程序，选定后自动追加进程名。";
		}

		WindowsListView.ItemsSource = _windows;
		PreviewMouseMove += Window_PreviewMouseMove;
		PreviewMouseUp += Window_PreviewMouseUp;
	}

	private void Window_Loaded(object sender, RoutedEventArgs e)
	{
		ScanRunningWindows();
	}

	private void ScanRunningWindows()
	{
		_allWindows.Clear();
		_windows.Clear();
		nint thisHwnd = new WindowInteropHelper(this).Handle;

		EnumWindows((hWnd, lParam) =>
		{
			try
			{
				if (!IsWindowVisible(hWnd) || hWnd == thisHwnd)
				{
					return true;
				}

				int length = GetWindowTextLength(hWnd);
				if (length <= 0)
				{
					return true;
				}

				StringBuilder titleSb = new StringBuilder(length + 1);
				GetWindowText(hWnd, titleSb, titleSb.Capacity);
				string title = titleSb.ToString().Trim();
				if (string.IsNullOrWhiteSpace(title))
				{
					return true;
				}

				StringBuilder classSb = new StringBuilder(256);
				GetClassName(hWnd, classSb, 256);
				string className = classSb.ToString();

				// 过滤 Windows 外壳桌面/托盘等非独立应用
				if (className == "Progman" || className == "WorkerW" || className == "Shell_TrayWnd" ||
					className == "Shell_SecondaryTrayWnd" || className == "Windows.UI.Core.CoreWindow")
				{
					return true;
				}

				RunningWindowInfo? winInfo = ReadWindowProcessInfo(hWnd);
				if (winInfo == null || winInfo.ProcessId == _ownProcessId)
				{
					return true;
				}

				// 再次防护 StarPie 自身
				if (winInfo.CleanProcessName.Equals("starpie", StringComparison.OrdinalIgnoreCase) ||
					winInfo.CleanProcessName.Equals("winpiegestures", StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}

				winInfo.WindowTitle = title;
				winInfo.IconSource = ExtractWindowOrFileIcon(hWnd, winInfo.ExecutablePath);

				_allWindows.Add(winInfo);
			}
			catch { }

			return true;
		}, IntPtr.Zero);

		ApplyFilter(SearchTextBox.Text);
		StatusTextBlock.Text = $"✅ 已探测到 {_allWindows.Count} 个活跃桌面窗口（已安全排除 StarPie 自身）";
	}

	internal static RunningWindowInfo? ReadWindowProcessInfo(nint hwnd)
	{
		ActiveWindowHelper.GetWindowProcessInfo(hwnd, out uint processId, out string processName, out string executablePath);
		// 读取失败不能伪造成可绑定的进程，否则捕捉成功后仍无法在运行时匹配。
		if (processId == 0 || string.IsNullOrEmpty(processName) ||
			processName.Equals("unknown.exe", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		return new RunningWindowInfo
		{
			Hwnd = hwnd,
			ProcessId = processId,
			ProcessName = processName,
			CleanProcessName = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
				? processName.Substring(0, processName.Length - 4)
				: processName,
			ExecutablePath = executablePath
		};
	}

	private ImageSource? ExtractWindowOrFileIcon(nint hwnd, string filePath)
	{
		try
		{
			if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
			{
				using var sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(filePath);
				if (sysIcon != null)
				{
					var bmp = Imaging.CreateBitmapSourceFromHIcon(
						sysIcon.Handle,
						Int32Rect.Empty,
						BitmapSizeOptions.FromEmptyOptions());
					bmp.Freeze();
					return bmp;
				}
			}
		}
		catch { }
		return null;
	}

	private void ApplyFilter(string keyword)
	{
		string kw = keyword?.Trim().ToLowerInvariant() ?? "";
		_windows.Clear();
		foreach (var win in _allWindows)
		{
			if (string.IsNullOrEmpty(kw) ||
				win.WindowTitle.ToLowerInvariant().Contains(kw) ||
				win.ProcessName.ToLowerInvariant().Contains(kw) ||
				win.CleanProcessName.ToLowerInvariant().Contains(kw))
			{
				_windows.Add(win);
			}
		}
	}

	private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
	{
		SearchPlaceholderText.Visibility = string.IsNullOrEmpty(SearchTextBox.Text) ? Visibility.Visible : Visibility.Collapsed;
		ApplyFilter(SearchTextBox.Text);
	}

	private void RefreshButton_Click(object sender, RoutedEventArgs e)
	{
		ScanRunningWindows();
	}

	private void WindowsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		OkButton.IsEnabled = WindowsListView.SelectedItem is RunningWindowInfo;
	}

	private void WindowsListView_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
	{
		if (WindowsListView.SelectedItem is RunningWindowInfo sel)
		{
			CommitSelection(sel);
		}
	}

	private void Ok_Click(object sender, RoutedEventArgs e)
	{
		if (WindowsListView.SelectedItem is RunningWindowInfo sel)
		{
			CommitSelection(sel);
		}
	}

	private void CommitSelection(RunningWindowInfo sel)
	{
		SelectedPath = sel.ExecutablePath;
		SelectedProcessName = sel.CleanProcessName;
		SelectedTitle = sel.WindowTitle;
		DialogResult = true;
		Close();
	}

	private void Cancel_Click(object sender, RoutedEventArgs e)
	{
		DialogResult = false;
		Close();
	}

	// ==================== Spy++ 十字准星拖拽瞄准 ====================

	private void CrosshairTargetBox_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
	{
		if (e.LeftButton == MouseButtonState.Pressed)
		{
			_isDraggingCrosshair = true;
			CrosshairTargetBox.CaptureMouse();
			this.Opacity = 0.35; // 窗口半透明，便于看清桌面底下
			CrosshairStatusText.Text = "🎯 瞄准中... 请按住拖动准星至屏幕任意窗口上方，松开鼠标即可捕获！";
			CrosshairStatusText.Foreground = (Brush)FindResource("AccentPrimaryBrush");
		}
	}

	private void Window_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
	{
		if (_isDraggingCrosshair)
		{
			GetCursorPos(out POINT pt);
			nint targetHwnd = WindowFromPoint(pt);
			targetHwnd = GetAncestor(targetHwnd, GA_ROOT);

			nint thisHwnd = new WindowInteropHelper(this).Handle;
			if (targetHwnd != IntPtr.Zero && targetHwnd != thisHwnd)
			{
				RunningWindowInfo? winInfo = ReadWindowProcessInfo(targetHwnd);
				if (winInfo != null && winInfo.ProcessId != _ownProcessId)
				{
					StringBuilder sb = new StringBuilder(256);
					GetWindowText(targetHwnd, sb, 256);
					string title = sb.ToString().Trim();
					CrosshairStatusText.Text = $"🎯 当前瞄准: {(string.IsNullOrEmpty(title) ? winInfo.ProcessName : title)} ({winInfo.ProcessName})";
				}
			}
		}
	}

	private void Window_PreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
	{
		if (_isDraggingCrosshair)
		{
			_isDraggingCrosshair = false;
			CrosshairTargetBox.ReleaseMouseCapture();
			this.Opacity = 1.0;

			GetCursorPos(out POINT pt);
			nint targetHwnd = WindowFromPoint(pt);
			targetHwnd = GetAncestor(targetHwnd, GA_ROOT);

			nint thisHwnd = new WindowInteropHelper(this).Handle;
			if (targetHwnd != IntPtr.Zero && targetHwnd != thisHwnd)
			{
				RunningWindowInfo? winInfo = ReadWindowProcessInfo(targetHwnd);
				if (winInfo != null && winInfo.ProcessId != _ownProcessId)
				{
					StringBuilder sb = new StringBuilder(256);
					GetWindowText(targetHwnd, sb, 256);
					string title = sb.ToString().Trim();
					if (!winInfo.CleanProcessName.Equals("starpie", StringComparison.OrdinalIgnoreCase) &&
						!winInfo.CleanProcessName.Equals("winpiegestures", StringComparison.OrdinalIgnoreCase))
					{
						SelectedPath = winInfo.ExecutablePath;
						SelectedProcessName = winInfo.CleanProcessName;
						SelectedTitle = title;
						DialogResult = true;
						Close();
						return;
					}
				}
			}

			CrosshairStatusText.Text = "未命中有效外部窗口，请重试或在下方列表直接点选。";
			CrosshairStatusText.Foreground = (Brush)FindResource("TextSecondaryBrush");
		}
	}
}
