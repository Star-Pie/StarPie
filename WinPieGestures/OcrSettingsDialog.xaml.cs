using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Windows.Globalization;
using Windows.Media.Ocr;

namespace WinPieGestures;

public partial class OcrSettingsDialog : Window
{
	public OcrSettingsDialog()
	{
		InitializeComponent();
		AppThemeManager.ApplyTheme(this, AppThemeManager.CurrentEffectiveTheme);
		ApplyLocalization();
		LoadConfig();
	}

	private void Window_Loaded(object sender, RoutedEventArgs e)
	{
		LoadConfig();
	}

	public void ApplyLocalization()
	{
		base.Title = I18n.T("OcrDialogTitle");
		if (OcrHeaderTitleText != null) OcrHeaderTitleText.Text = I18n.T("OcrDialogHeader");
		if (OcrHeaderSubtitleText != null) OcrHeaderSubtitleText.Text = I18n.T("OcrDialogSubtitle");
		if (ProviderSectionText != null) ProviderSectionText.Text = I18n.T("OcrProviderSection");
		if (ProviderLocalRadio != null) ProviderLocalRadio.Content = I18n.T("OcrProviderLocal");
		if (ProviderAiRadio != null) ProviderAiRadio.Content = I18n.T("OcrProviderAi");
		if (ProviderCustomRadio != null) ProviderCustomRadio.Content = I18n.T("OcrProviderCustom");

		if (LocalEngineTitleText != null) LocalEngineTitleText.Text = I18n.T("OcrLocalTitle");
		if (LocalEngineDescText != null) LocalEngineDescText.Text = I18n.T("OcrLocalDesc");
		if (PriorityLangLabelText != null) PriorityLangLabelText.Text = I18n.T("OcrPriorityLang");
		if (LocalLangStatusAlertText != null) LocalLangStatusAlertText.Text = I18n.T("OcrLocalAlertNoLang");
		if (OpenOptionalFeaturesButton != null) OpenOptionalFeaturesButton.Content = I18n.T("OcrBtnOpenFeatures");

		if (AiEngineTitleText != null) AiEngineTitleText.Text = I18n.T("OcrAiTitle");
		if (AiEngineDescText != null) AiEngineDescText.Text = I18n.T("OcrAiDesc");
		if (AiEndpointLabelText != null) AiEndpointLabelText.Text = I18n.T("OcrAiEndpoint");
		if (AiApiKeyLabelText != null) AiApiKeyLabelText.Text = I18n.T("OcrAiApiKey");
		if (AiModelLabelText != null) AiModelLabelText.Text = I18n.T("OcrAiModel");
		if (AiPromptModeLabelText != null) AiPromptModeLabelText.Text = I18n.T("OcrAiPromptMode");

		if (AiModelPresetItemDefault != null) AiModelPresetItemDefault.Content = I18n.T("OcrAiModelPresetDefault");
		if (AiModelPresetItemGpt != null) AiModelPresetItemGpt.Content = I18n.T("OcrAiModelPresetGpt");
		if (AiModelPresetItemQwen != null) AiModelPresetItemQwen.Content = I18n.T("OcrAiModelPresetQwen");
		if (AiModelPresetItemOllama != null) AiModelPresetItemOllama.Content = I18n.T("OcrAiModelPresetOllama");
		if (AiModelPresetItemZhipu != null) AiModelPresetItemZhipu.Content = I18n.T("OcrAiModelPresetZhipu");

		if (AiPromptModeItemText != null) AiPromptModeItemText.Content = I18n.T("OcrAiPromptText");
		if (AiPromptModeItemLatex != null) AiPromptModeItemLatex.Content = I18n.T("OcrAiPromptLatex");
		if (AiPromptModeItemMarkdown != null) AiPromptModeItemMarkdown.Content = I18n.T("OcrAiPromptMarkdown");
		if (AiPromptModeItemTranslate != null) AiPromptModeItemTranslate.Content = I18n.T("OcrAiPromptTranslate");

		if (CustomEngineTitleText != null) CustomEngineTitleText.Text = I18n.T("OcrCustomTitle");
		if (CustomEngineDescText != null) CustomEngineDescText.Text = I18n.T("OcrCustomDesc");
		if (CustomHttpUrlLabelText != null) CustomHttpUrlLabelText.Text = I18n.T("OcrCustomUrl");

		if (BehaviorsSectionText != null) BehaviorsSectionText.Text = I18n.T("OcrBehaviorsSection");
		if (AutoCopyCheckBox != null) AutoCopyCheckBox.Content = I18n.T("OcrBehaviorCopy");
		if (ShowResultWinCheckBox != null) ShowResultWinCheckBox.Content = I18n.T("OcrBehaviorShowWin");
		if (RemoveCjkSpacesCheckBox != null) RemoveCjkSpacesCheckBox.Content = I18n.T("OcrBehaviorRemoveSpaces");
		if (MergeLinesCheckBox != null) MergeLinesCheckBox.Content = I18n.T("OcrBehaviorMergeLines");

		if (TestSnippetBtn != null)
		{
			TestSnippetBtn.Content = I18n.T("OcrBtnTestSnippet");
			TestSnippetBtn.ToolTip = I18n.T("OcrTipTestSnippet");
		}
		if (TestConnBtn != null)
		{
			TestConnBtn.Content = I18n.T("OcrBtnTestConn");
			TestConnBtn.ToolTip = I18n.T("OcrTipTestConn");
		}
		if (CancelBtn != null) CancelBtn.Content = I18n.T("OcrBtnCancel");
		if (SaveBtn != null) SaveBtn.Content = I18n.T("OcrBtnSave");
	}

	private void LoadConfig()
	{
		OcrSettings cfg = ConfigManager.CurrentConfig?.OcrConfig ?? new OcrSettings();

		string provider = cfg.Provider ?? "Local";
		if (provider == "Ai") ProviderAiRadio.IsChecked = true;
		else if (provider == "Custom") ProviderCustomRadio.IsChecked = true;
		else ProviderLocalRadio.IsChecked = true;

		UpdateProviderVisibility();

		// Local
		SetComboSelectedTag(LocalLangComboBox, cfg.LocalLanguage ?? "zh-Hans");

		// AI
		AiEndpointTextBox.Text = !string.IsNullOrEmpty(cfg.AiEndpoint) ? cfg.AiEndpoint : "https://api.openai.com/v1";
		AiApiKeyBox.Password = cfg.AiApiKey ?? "";
		AiModelTextBox.Text = !string.IsNullOrEmpty(cfg.AiModel) ? cfg.AiModel : "gpt-4o-mini";
		SetComboSelectedTag(AiPromptModeComboBox, cfg.AiPromptMode ?? "text");

		// Custom
		CustomHttpUrlTextBox.Text = !string.IsNullOrEmpty(cfg.CustomHttpUrl) ? cfg.CustomHttpUrl : "http://127.0.0.1:1224/api/ocr";

		// Behaviors
		AutoCopyCheckBox.IsChecked = cfg.AutoCopyToClipboard;
		ShowResultWinCheckBox.IsChecked = cfg.ShowResultWindow;
		RemoveCjkSpacesCheckBox.IsChecked = cfg.RemoveSpacesBetweenCjk;
		MergeLinesCheckBox.IsChecked = cfg.MergeLines;

		try
		{
			if (OcrEngine.AvailableRecognizerLanguages.Count == 0)
			{
				LocalLangStatusAlertBorder.Visibility = Visibility.Visible;
				LocalLangStatusAlertText.Text = I18n.T("OcrAlertNoAvailableLanguages");
			}
			else
			{
				LocalLangStatusAlertBorder.Visibility = Visibility.Collapsed;
			}
		}
		catch
		{
			LocalLangStatusAlertBorder.Visibility = Visibility.Collapsed;
		}
	}

	private void OpenOptionalFeaturesButton_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			Process.Start(new ProcessStartInfo { FileName = "ms-settings:optionalfeatures", UseShellExecute = true });
		}
		catch
		{
		}
	}

	private void ProviderRadio_Checked(object sender, RoutedEventArgs e)
	{
		UpdateProviderVisibility();
	}

	private void UpdateProviderVisibility()
	{
		if (LocalEngineSettingsPanel == null) return;

		bool isLocal = ProviderLocalRadio.IsChecked == true;
		bool isAi = ProviderAiRadio.IsChecked == true;
		bool isCustom = ProviderCustomRadio.IsChecked == true;

		LocalEngineSettingsPanel.Visibility = isLocal ? Visibility.Visible : Visibility.Collapsed;
		AiEngineSettingsPanel.Visibility = isAi ? Visibility.Visible : Visibility.Collapsed;
		CustomEngineSettingsPanel.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
	}

	private void AiModelPresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (AiModelPresetComboBox.SelectedItem is ComboBoxItem item && item.Tag != null)
		{
			AiModelTextBox.Text = item.Tag.ToString();
		}
	}

	private async void TestConnectionButton_Click(object sender, RoutedEventArgs e)
	{
		TestResultLabel.Text = I18n.T("OcrMsgTesting");
		TestResultLabel.Foreground = System.Windows.Media.Brushes.Yellow;

		try
		{
			if (ProviderLocalRadio.IsChecked == true)
			{
				string langTag = GetComboSelectedTag(LocalLangComboBox) ?? "zh-Hans";
				bool supported = OcrEngine.IsLanguageSupported(new Language(langTag));
				if (supported)
				{
					TestResultLabel.Text = I18n.T("OcrMsgLocalReady");
					TestResultLabel.Foreground = System.Windows.Media.Brushes.LightGreen;
				}
				else
				{
					int count = OcrEngine.AvailableRecognizerLanguages.Count;
					TestResultLabel.Text = string.Format(I18n.T("OcrMsgLocalNotInstalled"), langTag, count);
					TestResultLabel.Foreground = System.Windows.Media.Brushes.Orange;
				}
			}
			else if (ProviderAiRadio.IsChecked == true)
			{
				string ep = AiEndpointTextBox.Text.Trim();
				using HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
				if (!string.IsNullOrWhiteSpace(AiApiKeyBox.Password))
				{
					client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AiApiKeyBox.Password.Trim());
				}
				using HttpResponseMessage resp = await client.GetAsync(ep.TrimEnd('/') + "/models");
				if (resp.IsSuccessStatusCode || (int)resp.StatusCode == 401 || (int)resp.StatusCode == 400)
				{
					TestResultLabel.Text = string.Format(I18n.T("OcrMsgEndpointOk"), (int)resp.StatusCode);
					TestResultLabel.Foreground = System.Windows.Media.Brushes.LightGreen;
				}
				else
				{
					TestResultLabel.Text = string.Format(I18n.T("OcrMsgEndpointErr"), (int)resp.StatusCode);
					TestResultLabel.Foreground = System.Windows.Media.Brushes.Orange;
				}
			}
			else
			{
				string url = CustomHttpUrlTextBox.Text.Trim();
				using HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
				using HttpResponseMessage resp = await client.GetAsync(url);
				TestResultLabel.Text = string.Format(I18n.T("OcrMsgCustomOk"), (int)resp.StatusCode);
				TestResultLabel.Foreground = System.Windows.Media.Brushes.LightGreen;
			}
		}
		catch (Exception ex)
		{
			TestResultLabel.Text = string.Format(I18n.T("OcrMsgTestFailed"), ex.Message);
			TestResultLabel.Foreground = System.Windows.Media.Brushes.Salmon;
		}
	}

	private void TestSnippetButton_Click(object sender, RoutedEventArgs e)
	{
		SaveConfigValues();
		OcrManager.StartCaptureAndRecognize();
	}

	private void SaveButton_Click(object sender, RoutedEventArgs e)
	{
		SaveConfigValues();
		ConfigManager.SaveConfig();
		DialogResult = true;
		Close();
	}

	private void SaveConfigValues()
	{
		if (ConfigManager.CurrentConfig == null) return;
		ConfigManager.CurrentConfig.OcrConfig ??= new OcrSettings();
		OcrSettings cfg = ConfigManager.CurrentConfig.OcrConfig;

		if (ProviderAiRadio.IsChecked == true) cfg.Provider = "Ai";
		else if (ProviderCustomRadio.IsChecked == true) cfg.Provider = "Custom";
		else cfg.Provider = "Local";

		cfg.LocalLanguage = GetComboSelectedTag(LocalLangComboBox) ?? "zh-Hans";
		cfg.AiEndpoint = AiEndpointTextBox.Text.Trim();
		cfg.AiApiKey = AiApiKeyBox.Password.Trim();
		cfg.AiModel = AiModelTextBox.Text.Trim();
		cfg.AiPromptMode = GetComboSelectedTag(AiPromptModeComboBox) ?? "text";
		cfg.CustomHttpUrl = CustomHttpUrlTextBox.Text.Trim();

		cfg.AutoCopyToClipboard = AutoCopyCheckBox.IsChecked == true;
		cfg.ShowResultWindow = ShowResultWinCheckBox.IsChecked == true;
		cfg.RemoveSpacesBetweenCjk = RemoveCjkSpacesCheckBox.IsChecked == true;
		cfg.MergeLines = MergeLinesCheckBox.IsChecked == true;
	}

	private void CancelButton_Click(object sender, RoutedEventArgs e)
	{
		DialogResult = false;
		Close();
	}

	private void Window_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Escape)
		{
			Close();
		}
	}

	private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (e.ButtonState == MouseButtonState.Pressed)
		{
			DragMove();
		}
	}

	private static void SetComboSelectedTag(System.Windows.Controls.ComboBox cb, string tag)
	{
		if (cb == null) return;
		foreach (ComboBoxItem item in cb.Items)
		{
			if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
			{
				cb.SelectedItem = item;
				return;
			}
		}
	}

	private static string? GetComboSelectedTag(System.Windows.Controls.ComboBox cb)
	{
		return (cb?.SelectedItem as ComboBoxItem)?.Tag?.ToString();
	}
}
