using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace WinPieGestures;

public partial class QuickSearchWindow : Window
{
	private static QuickSearchWindow? _instance;
	private string _currentCategory = "All";
	private DispatcherTimer? _debounceTimer;
	private DispatcherTimer? _feedbackTimer;
	private DispatcherTimer? _idleCleanupTimer;
	private int _idleStep;
	private CancellationTokenSource? _searchCts;
	private bool _isContextMenuOpen;
	private bool _isPinned;
	private string _lastSearchedQuery = "";

	public QuickSearchWindow()
	{
		InitializeComponent();
		AppThemeManager.ApplyTheme(this, ConfigManager.CurrentConfig?.AppTheme ?? "System");

		if (ConfigManager.CurrentConfig != null)
		{
			if (ConfigManager.CurrentConfig.QuickSearchWidth >= 560)
				Width = ConfigManager.CurrentConfig.QuickSearchWidth;
			if (ConfigManager.CurrentConfig.QuickSearchHeight >= 380)
				Height = ConfigManager.CurrentConfig.QuickSearchHeight;
			_isPinned = ConfigManager.CurrentConfig.QuickSearchPinned;
		}

		_debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
		_debounceTimer.Tick += DebounceTimer_Tick;

		_feedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
		_feedbackTimer.Tick += (s, e) =>
		{
			ActionFeedbackBanner.Visibility = Visibility.Collapsed;
			_feedbackTimer.Stop();
		};

		IsVisibleChanged += (s, e) =>
		{
			if (IsVisible)
			{
				_idleCleanupTimer?.Stop();
				_idleStep = 0;
			}
			else
			{
				StartIdleCleanup();
			}
		};
	}

	private void StartIdleCleanup()
	{
		_searchCts?.Cancel();
		_idleStep = 0;
		if (_idleCleanupTimer == null)
		{
			_idleCleanupTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
			_idleCleanupTimer.Tick += IdleCleanupTimer_Tick;
		}
		_idleCleanupTimer.Stop();
		_idleCleanupTimer.Start();
	}

	private void IdleCleanupTimer_Tick(object? sender, EventArgs e)
	{
		if (IsVisible)
		{
			_idleCleanupTimer?.Stop();
			return;
		}

		_idleStep++;
		if (_idleStep == 1)
		{
			// 隐藏 10 秒后：释放 WPF 结果列表视觉树与数据绑定
			ResultsListBox.ItemsSource = null;
		}
		else if (_idleStep >= 3)
		{
			// 隐藏 30 秒后：完全销毁窗口实例并释放搜索引擎及动态图标缓存
			_idleCleanupTimer?.Stop();
			try
			{
				Close();
			}
			catch { }
			_instance = null;
			NativeSearchEngine.ClearCaches();
			IconHelper.TrimDynamicCache();
			MemoryOptimizer.TrimMemory(force: false);
		}
	}

	protected override void OnClosed(EventArgs e)
	{
		base.OnClosed(e);
		_idleCleanupTimer?.Stop();
		_debounceTimer?.Stop();
		_feedbackTimer?.Stop();
		_searchCts?.Cancel();
		ResultsListBox.ItemsSource = null;
		if (ReferenceEquals(_instance, this))
		{
			_instance = null;
		}
	}

	public static void ShowOrActivate(Point? triggerPoint = null)
	{
		if (_instance == null || !_instance.IsLoaded)
		{
			_instance = new QuickSearchWindow();
		}
		_instance.ShowAndPosition(triggerPoint);
	}

	private void ShowAndPosition(Point? triggerPoint = null)
	{
		AppThemeManager.ApplyTheme(this, ConfigManager.CurrentConfig?.AppTheme ?? "System");

		if (ConfigManager.CurrentConfig != null)
		{
			if (ConfigManager.CurrentConfig.QuickSearchWidth >= 560)
				Width = ConfigManager.CurrentConfig.QuickSearchWidth;
			if (ConfigManager.CurrentConfig.QuickSearchHeight >= 380)
				Height = ConfigManager.CurrentConfig.QuickSearchHeight;
			_isPinned = ConfigManager.CurrentConfig.QuickSearchPinned;
		}
		UpdatePinVisual();

		// 1. 获取当前触发时的物理光标坐标并解析所在屏幕上下文
		var physPos = triggerPoint ?? ScreenHelper.GetCursorPhysicalPosition();
		var screenCtx = ScreenHelper.GetScreenContextAtPoint(physPos);
		Point mouseDip = ScreenHelper.PhysicalToDip(physPos, screenCtx.DpiScale);

		// 2. 将搜索框水平居中于鼠标，垂直方向偏上（让顶部搜索条刚好落在光标位置附近，实现指哪搜哪的盲操手感）
		double targetLeft = mouseDip.X - Width / 2.0;
		double targetTop = mouseDip.Y - 45.0;

		// 3. 严格遵循屏幕工作区贴边防溢出规范（支持多显示器与任务栏规避）
		Rect work = screenCtx.DipWorkArea;
		const double margin = 12.0;
		targetLeft = Math.Clamp(targetLeft, work.Left + margin, Math.Max(work.Left + margin, work.Right - Width - margin));
		targetTop = Math.Clamp(targetTop, work.Top + margin, Math.Max(work.Top + margin, work.Bottom - Height - margin));

		Left = targetLeft;
		Top = targetTop;

		SearchInputBox.Text = "";
		_currentCategory = "All";
		_lastSearchedQuery = "";
		UpdateFilterChipsStyle();

		Show();
		Activate();
		SearchInputBox.Focus();
		TriggerSearch(immediate: true);
	}

	private void Window_Loaded(object sender, RoutedEventArgs e)
	{
		UpdatePinVisual();
	}

	private void Window_Deactivated(object? sender, EventArgs e)
	{
		if (_isContextMenuOpen || _isPinned) return;
		// 鼠标点击搜索框外部区域时自动平滑隐藏，避免干扰用户正常工作
		Hide();
	}

	private void PinBtn_Click(object sender, RoutedEventArgs e)
	{
		_isPinned = !_isPinned;
		if (ConfigManager.CurrentConfig != null)
		{
			ConfigManager.CurrentConfig.QuickSearchPinned = _isPinned;
			ConfigManager.SaveConfig();
		}
		UpdatePinVisual();
	}

	private void UpdatePinVisual()
	{
		if (PinBtn == null) return;
		Topmost = true;
		if (_isPinned)
		{
			PinBtn.Background = (Brush)FindResource("AccentPrimaryBrush");
			PinBtn.Foreground = (Brush)FindResource("AccentTextBrush");
			PinBtn.BorderBrush = (Brush)FindResource("AccentHoverBrush");
			PinBtn.ToolTip = "取消置顶 (当前已固定在最前端，失焦不隐藏)";
		}
		else
		{
			PinBtn.ClearValue(BackgroundProperty);
			PinBtn.ClearValue(ForegroundProperty);
			PinBtn.ClearValue(BorderBrushProperty);
			PinBtn.ToolTip = "窗口置顶 (点击固定在最前端，失焦不隐藏)";
		}
	}

	private void WindowResizeGrip_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
	{
		ResizeWindow(e.HorizontalChange, e.VerticalChange);
	}

	private void RightEdge_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
	{
		ResizeWindow(e.HorizontalChange, 0);
	}

	private void BottomEdge_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
	{
		ResizeWindow(0, e.VerticalChange);
	}

	private void ResizeWindow(double deltaW, double deltaH)
	{
		double newW = Math.Max(MinWidth, Width + deltaW);
		double newH = Math.Max(MinHeight, Height + deltaH);

		var screenCtx = ScreenHelper.GetScreenContextAtPoint(new Point(Left, Top));
		Rect work = screenCtx.DipWorkArea;
		newW = Math.Min(newW, work.Width - 24);
		newH = Math.Min(newH, work.Height - 24);

		Width = newW;
		Height = newH;

		if (ConfigManager.CurrentConfig != null)
		{
			ConfigManager.CurrentConfig.QuickSearchWidth = newW;
			ConfigManager.CurrentConfig.QuickSearchHeight = newH;
			ConfigManager.SaveConfig();
		}
	}

	private void ContextMenu_Opened(object sender, RoutedEventArgs e)
	{
		_isContextMenuOpen = true;
	}

	private void ContextMenu_Closed(object sender, RoutedEventArgs e)
	{
		_isContextMenuOpen = false;
	}

	private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (e.ChangedButton != MouseButton.Left) return;

		// 检查点击的元素：如果点击的是可交互控件（输入框、按钮、列表项、滚动条等），不触发窗口拖拽
		DependencyObject? current = e.OriginalSource as DependencyObject;
		while (current != null && current != this)
		{
			if (current is TextBox ||
			    current is Button ||
			    current is ListBoxItem ||
			    current is System.Windows.Controls.Primitives.ScrollBar ||
			    current is System.Windows.Controls.Primitives.Thumb)
			{
				return;
			}
			current = VisualTreeHelper.GetParent(current);
		}

		if (e.ButtonState == MouseButtonState.Pressed)
		{
			try
			{
				DragMove();
			}
			catch { }
		}
	}

	private static bool IsDescendantOf(DependencyObject? node, DependencyObject target)
	{
		while (node != null)
		{
			if (node == target) return true;
			node = VisualTreeHelper.GetParent(node);
		}
		return false;
	}

	private void Window_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Escape)
		{
			Hide();
			e.Handled = true;
		}
	}

	private void SearchInputBox_TextChanged(object sender, TextChangedEventArgs e)
	{
		string query = SearchInputBox.Text.Trim();
		PlaceholderText.Visibility = string.IsNullOrEmpty(query) ? Visibility.Visible : Visibility.Collapsed;
		ClearInputBtn.Visibility = string.IsNullOrEmpty(query) ? Visibility.Collapsed : Visibility.Visible;

		if (string.IsNullOrEmpty(query))
		{
			_debounceTimer?.Stop();
			_searchCts?.Cancel();
			if (SearchingIndicator != null)
			{
				SearchingIndicator.Visibility = Visibility.Collapsed;
			}
			_lastSearchedQuery = "";
			TriggerSearch(immediate: true);
		}
		else
		{
			TriggerSearch(immediate: false);
		}
	}

	private void SearchBtn_Click(object sender, RoutedEventArgs e)
	{
		TriggerSearch(immediate: true);
		SearchInputBox.Focus();
	}

	private void TriggerSearch(bool immediate)
	{
		_debounceTimer?.Stop();
		if (immediate)
		{
			ExecuteSearchAsync();
		}
		else
		{
			_debounceTimer?.Start();
		}
	}

	private void DebounceTimer_Tick(object? sender, EventArgs e)
	{
		_debounceTimer?.Stop();
		ExecuteSearchAsync();
	}

	private async void ExecuteSearchAsync()
	{
		_searchCts?.Cancel();
		var cts = new CancellationTokenSource();
		_searchCts = cts;

		string query = SearchInputBox.Text.Trim();
		string category = _currentCategory;

		if (SearchingIndicator != null)
		{
			SearchingIndicator.Visibility = Visibility.Visible;
		}
		if (StatusCountText != null)
		{
			StatusCountText.Text = string.IsNullOrEmpty(query) ? "正在加载常用推荐..." : "⏳ 正在搜索...";
		}

		Stopwatch sw = Stopwatch.StartNew();

		try
		{
			List<SearchResultItem> results = await NativeSearchEngine.SearchAsync(query, category, 120, cts.Token);

			if (cts.Token.IsCancellationRequested) return;

			sw.Stop();
			double elapsedMs = sw.Elapsed.TotalMilliseconds;
			_lastSearchedQuery = query;

			ResultsListBox.ItemsSource = results;
			if (results.Count > 0)
			{
				ResultsListBox.SelectedIndex = 0;
				EmptyResultsNotice.Visibility = Visibility.Collapsed;
				ResultsListBox.Visibility = Visibility.Visible;
			}
			else
			{
				ResultsListBox.Visibility = Visibility.Collapsed;
				EmptyResultsNotice.Visibility = Visibility.Visible;
				EmptyNoticeEmoji.Text = "🔍";
				EmptyNoticeTitle.Text = string.IsNullOrEmpty(query) ? "未发现匹配文件" : $"未找到关于 \"{query}\" 的结果";
				EmptyNoticeSub.Text = "尝试换个关键词，或切换上方分类";
			}

			string countPrefix = string.IsNullOrEmpty(query) ? "常用推荐" : $"找到 {results.Count} 项结果";
			if (StatusCountText != null)
			{
				StatusCountText.Text = $"{countPrefix} · {elapsedMs:F0} ms";
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			AppLogger.LogError($"QuickSearchWindow search error: {ex.Message}", ex);
		}
		finally
		{
			if (ReferenceEquals(_searchCts, cts))
			{
				if (SearchingIndicator != null)
				{
					SearchingIndicator.Visibility = Visibility.Collapsed;
				}
			}
		}
	}

	private void SearchInputBox_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Down)
		{
			if (ResultsListBox.Items.Count > 0)
			{
				int next = (ResultsListBox.SelectedIndex + 1) % ResultsListBox.Items.Count;
				ResultsListBox.SelectedIndex = next;
				ResultsListBox.ScrollIntoView(ResultsListBox.SelectedItem);
			}
			e.Handled = true;
		}
		else if (e.Key == Key.Up)
		{
			if (ResultsListBox.Items.Count > 0)
			{
				int prev = (ResultsListBox.SelectedIndex - 1 + ResultsListBox.Items.Count) % ResultsListBox.Items.Count;
				ResultsListBox.SelectedIndex = prev;
				ResultsListBox.ScrollIntoView(ResultsListBox.SelectedItem);
			}
			e.Handled = true;
		}
		else if (e.Key == Key.Enter)
		{
			string currentQuery = SearchInputBox.Text.Trim();
			// 如果输入内容未执行过搜索，或正在搜索中，回车键立即执行搜索
			if (!string.Equals(currentQuery, _lastSearchedQuery, StringComparison.Ordinal) ||
			    (SearchingIndicator != null && SearchingIndicator.Visibility == Visibility.Visible))
			{
				TriggerSearch(immediate: true);
				e.Handled = true;
				return;
			}

			if (ResultsListBox.SelectedItem is SearchResultItem item)
			{
				if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
				{
					RevealItemInExplorer(item);
				}
				else
				{
					OpenItem(item);
				}
			}
			e.Handled = true;
		}
	}

	private void ClearInputBtn_Click(object sender, RoutedEventArgs e)
	{
		SearchInputBox.Text = "";
		_lastSearchedQuery = "";
		SearchInputBox.Focus();
	}

	private void CloseButton_Click(object sender, RoutedEventArgs e)
	{
		Hide();
		ResultsListBox.ItemsSource = null;
	}

	private void FilterChip_Click(object sender, RoutedEventArgs e)
	{
		if (sender is Button btn && btn.Tag is string cat)
		{
			_currentCategory = cat;
			UpdateFilterChipsStyle();
			TriggerSearch(immediate: true);
			SearchInputBox.Focus();
		}
	}

	private void UpdateFilterChipsStyle()
	{
		var chips = new[] { FilterAllBtn, FilterAppsBtn, FilterCadBtn, FilterDocsBtn, FilterVideoBtn, FilterFoldersBtn, FilterSystemBtn };
		foreach (var chip in chips)
		{
			if (chip == null) continue;
			bool isSelected = string.Equals(chip.Tag as string, _currentCategory, StringComparison.OrdinalIgnoreCase);
			if (isSelected)
			{
				chip.Background = (Brush)FindResource("AccentPrimaryBrush");
				chip.Foreground = (Brush)FindResource("AccentTextBrush");
				chip.BorderBrush = (Brush)FindResource("AccentHoverBrush");
			}
			else
			{
				chip.Background = (Brush)FindResource("ButtonDefaultBgBrush");
				chip.Foreground = (Brush)FindResource("ButtonDefaultFgBrush");
				chip.BorderBrush = (Brush)FindResource("ButtonDefaultBorderBrush");
			}
		}
	}

	private void ResultsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
	{
		if (ResultsListBox.SelectedItem is SearchResultItem item)
		{
			OpenItem(item);
		}
	}

	private void ItemOpen_Click(object sender, RoutedEventArgs e)
	{
		if (sender is Button btn && btn.Tag is SearchResultItem item)
		{
			OpenItem(item);
		}
	}

	private void ContextOpen_Click(object sender, RoutedEventArgs e)
	{
		if (ResultsListBox.SelectedItem is SearchResultItem item)
		{
			OpenItem(item);
		}
	}

	private void ContextReveal_Click(object sender, RoutedEventArgs e)
	{
		if (ResultsListBox.SelectedItem is SearchResultItem item)
		{
			RevealItemInExplorer(item);
		}
	}

	private void ContextRunAsAdmin_Click(object sender, RoutedEventArgs e)
	{
		if (ResultsListBox.SelectedItem is SearchResultItem item)
		{
			RunItemAsAdmin(item);
		}
	}

	private void ContextCopyPath_Click(object sender, RoutedEventArgs e)
	{
		if (ResultsListBox.SelectedItem is SearchResultItem item)
		{
			try
			{
				System.Windows.Clipboard.SetText(item.FullPath);
				ShowFeedbackBanner($"📋 已复制完整路径: {item.FullPath}", isWarning: false);
			}
			catch (Exception ex)
			{
				ShowFeedbackBanner($"复制失败: {ex.Message}", isWarning: true);
			}
		}
	}

	private void ContextCopyFileName_Click(object sender, RoutedEventArgs e)
	{
		if (ResultsListBox.SelectedItem is SearchResultItem item)
		{
			try
			{
				System.Windows.Clipboard.SetText(item.FileName);
				ShowFeedbackBanner($"📝 已复制文件名: {item.FileName}", isWarning: false);
			}
			catch (Exception ex)
			{
				ShowFeedbackBanner($"复制失败: {ex.Message}", isWarning: true);
			}
		}
	}

	private void OpenItem(SearchResultItem item)
	{
		try
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = item.FullPath,
				UseShellExecute = true
			});
			Hide();
		}
		catch (Exception ex)
		{
			AppLogger.LogError($"Failed to open item '{item.FullPath}': {ex.Message}", ex);
			ShowFeedbackBanner($"无法打开目标文件: {ex.Message}", isWarning: true);
		}
	}

	private void RevealItemInExplorer(SearchResultItem item)
	{
		try
		{
			if (File.Exists(item.FullPath) || Directory.Exists(item.FullPath))
			{
				Process.Start("explorer.exe", $"/select,\"{item.FullPath}\"");
				Hide();
			}
			else
			{
				ShowFeedbackBanner("目标路径不存在或已被移动", isWarning: true);
			}
		}
		catch (Exception ex)
		{
			AppLogger.LogError($"Failed to reveal item in explorer '{item.FullPath}': {ex.Message}", ex);
			ShowFeedbackBanner($"定位失败: {ex.Message}", isWarning: true);
		}
	}

	private void RunItemAsAdmin(SearchResultItem item)
	{
		try
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = item.FullPath,
				Verb = "runas",
				UseShellExecute = true
			});
			Hide();
		}
		catch (Exception ex)
		{
			AppLogger.LogError($"Failed to run as admin '{item.FullPath}': {ex.Message}", ex);
			ShowFeedbackBanner($"以管理员身份启动失败: {ex.Message}", isWarning: true);
		}
	}

	private void ShowFeedbackBanner(string message, bool isWarning = false)
	{
		ActionFeedbackText.Text = message;
		if (isWarning)
		{
			ActionFeedbackBanner.Background = new SolidColorBrush(Color.FromArgb(0x18, 0xF5, 0x9E, 0x0B));
			ActionFeedbackBanner.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xF5, 0x9E, 0x0B));
			ActionFeedbackText.Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
			ActionFeedbackIcon.Text = "⚠️";
		}
		else
		{
			ActionFeedbackBanner.Background = new SolidColorBrush(Color.FromArgb(0x18, 0x10, 0xB9, 0x81));
			ActionFeedbackBanner.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x10, 0xB9, 0x81));
			ActionFeedbackText.Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
			ActionFeedbackIcon.Text = "✨";
		}

		ActionFeedbackBanner.Visibility = Visibility.Visible;
		_feedbackTimer?.Stop();
		_feedbackTimer?.Start();
	}

	private void CloseFeedbackBtn_Click(object sender, RoutedEventArgs e)
	{
		ActionFeedbackBanner.Visibility = Visibility.Collapsed;
		_feedbackTimer?.Stop();
	}
}
