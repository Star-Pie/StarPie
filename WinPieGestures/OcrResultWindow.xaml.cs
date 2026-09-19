using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace WinPieGestures;

public partial class OcrResultWindow : Window
{
	public OcrResultWindow(string text, string engineName, string latency)
	{
		InitializeComponent();
		AppThemeManager.ApplyTheme(this, AppThemeManager.CurrentEffectiveTheme);

		ResultTextBox.Text = text ?? string.Empty;
		EngineText.Text = $"{engineName} · {latency}";

		ApplyLocalization();

		ResultTextBox.TextChanged += (s, e) =>
		{
			if (CharCountText != null)
			{
				CharCountText.Text = string.Format(I18n.T("OcrResultCharCountFmt"), ResultTextBox.Text.Length);
			}
		};

		ResultTextBox.SelectAll();
		ResultTextBox.Focus();
	}

	public void ApplyLocalization()
	{
		base.Title = I18n.T("OcrResultTitle");
		if (ResultHeaderTitleText != null) ResultHeaderTitleText.Text = I18n.T("OcrResultHeader");
		if (CharCountText != null) CharCountText.Text = string.Format(I18n.T("OcrResultCharCountFmt"), ResultTextBox.Text.Length);
		if (ClipboardStatusText != null) ClipboardStatusText.Text = I18n.T("OcrResultCopiedAuto");
		if (CopyButton != null) CopyButton.Content = I18n.T("OcrResultBtnCopy");
		if (SearchButton != null) SearchButton.Content = I18n.T("OcrResultBtnSearch");
		if (SettingsButton != null) SettingsButton.Content = I18n.T("OcrResultBtnSettings");
		if (CloseDoneButton != null) CloseDoneButton.Content = I18n.T("OcrResultBtnDone");
	}

	private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (e.ButtonState == MouseButtonState.Pressed)
		{
			DragMove();
		}
	}

	private void Window_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Escape)
		{
			Close();
		}
	}

	private void CloseButton_Click(object sender, RoutedEventArgs e)
	{
		Close();
	}

	private void CopyButton_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			System.Windows.Clipboard.SetText(ResultTextBox.Text);
			ClipboardStatusText.Text = I18n.T("OcrResultCopiedManual");
			ClipboardStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
		}
		catch
		{
		}
	}

	private void SearchButton_Click(object sender, RoutedEventArgs e)
	{
		string query = ResultTextBox.Text.Trim();
		if (query.Length > 80)
		{
			query = query.Substring(0, 80);
		}
		if (!string.IsNullOrEmpty(query))
		{
			try
			{
				string url = "https://www.bing.com/search?q=" + Uri.EscapeDataString(query);
				Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
			}
			catch
			{
			}
		}
	}

	private void SettingsButton_Click(object sender, RoutedEventArgs e)
	{
		OcrManager.ShowSettingsDialog();
	}

	protected override void OnClosed(EventArgs e)
	{
		base.OnClosed(e);
		_ = System.Threading.Tasks.Task.Run(async () =>
		{
			try
			{
				await System.Threading.Tasks.Task.Delay(5000).ConfigureAwait(false);
				MemoryOptimizer.TrimMemory(force: false);
			}
			catch { }
		});
	}
}
