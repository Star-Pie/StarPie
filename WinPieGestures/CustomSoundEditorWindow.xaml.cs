using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WinPieGestures;

/// <summary>
/// 自定义交互音效调音台独立弹窗 (UI Demo 体验版)
/// </summary>
public partial class CustomSoundEditorWindow : Window
{
	private readonly List<CustomSoundProfile> _profiles;
	private CustomSoundProfile? _currentProfile;
	private bool _isLoading = false;

	public CustomSoundProfile? SelectedProfile => _currentProfile;

	public CustomSoundEditorWindow(string? initialTheme = null)
	{
		InitializeComponent();
		AppThemeManager.ApplyTheme(this, ConfigManager.CurrentConfig?.AppTheme ?? "System");

		if (ConfigManager.CurrentConfig?.CustomSoundProfiles != null && ConfigManager.CurrentConfig.CustomSoundProfiles.Count > 0)
		{
			_profiles = ConfigManager.CurrentConfig.CustomSoundProfiles;
		}
		else
		{
			_profiles = CustomSoundProfile.CreateDefaultDemoProfiles();
			if (ConfigManager.CurrentConfig != null)
			{
				ConfigManager.CurrentConfig.CustomSoundProfiles = _profiles;
			}
		}

		string targetId = initialTheme ?? ConfigManager.CurrentConfig?.ActiveCustomSoundProfileId ?? _profiles.FirstOrDefault()?.Id;
		LoadProfiles(targetId);
	}

	private void LoadProfiles(string? preferredTheme = null)
	{
		_isLoading = true;
		ProfileSelectorComboBox.Items.Clear();

		foreach (var p in _profiles)
		{
			ProfileSelectorComboBox.Items.Add(p.Name);
		}

		int selectedIdx = 0;
		if (!string.IsNullOrEmpty(preferredTheme))
		{
			for (int i = 0; i < _profiles.Count; i++)
			{
				if (_profiles[i].Id.Equals(preferredTheme, StringComparison.OrdinalIgnoreCase) ||
					_profiles[i].Name.Contains(preferredTheme, StringComparison.OrdinalIgnoreCase))
				{
					selectedIdx = i;
					break;
				}
			}
		}

		ProfileSelectorComboBox.SelectedIndex = selectedIdx;
		_isLoading = false;

		SelectProfile(selectedIdx);
	}

	private void ProfileSelectorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (_isLoading || ProfileSelectorComboBox.SelectedIndex < 0) return;
		SelectProfile(ProfileSelectorComboBox.SelectedIndex);
	}

	private void SelectProfile(int index)
	{
		if (index < 0 || index >= _profiles.Count) return;
		_currentProfile = _profiles[index];
		ProfileDescriptionLabel.Text = _currentProfile.Description;
		RenderEventCards();
	}

	private void RenderEventCards()
	{
		EventsContainerStackPanel.Children.Clear();
		if (_currentProfile == null) return;

		foreach (var ev in _currentProfile.Events)
		{
			var card = CreateEventCard(ev);
			EventsContainerStackPanel.Children.Add(card);
		}
	}

	private FrameworkElement CreateEventCard(SoundEventConfig ev)
	{
		var border = new Border
		{
			Background = (Brush)FindResource("CardBackgroundBrush"),
			BorderBrush = (Brush)FindResource("CardBorderBrush"),
			BorderThickness = new Thickness(1),
			CornerRadius = new CornerRadius(10),
			Padding = new Thickness(16, 12, 16, 12),
			Margin = new Thickness(0, 0, 0, 10),
			SnapsToDevicePixels = true
		};

		var mainGrid = new Grid();
		mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
		mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

		// Col 0: 事件标识与说明
		var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
		var titleStack = new StackPanel { Orientation = Orientation.Horizontal };
		titleStack.Children.Add(new TextBlock { Text = ev.EventIcon, FontSize = 14, Margin = new Thickness(0, 0, 6, 0) });
		titleStack.Children.Add(new TextBlock
		{
			Text = ev.EventName,
			FontSize = 13,
			FontWeight = FontWeights.SemiBold,
			Foreground = (Brush)FindResource("TextPrimaryBrush")
		});
		infoStack.Children.Add(titleStack);
		infoStack.Children.Add(new TextBlock
		{
			Text = ev.EventDescription,
			FontSize = 10.5,
			Foreground = (Brush)FindResource("TextSecondaryBrush"),
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0, 3, 0, 0)
		});
		Grid.SetColumn(infoStack, 0);
		mainGrid.Children.Add(infoStack);

		// Col 1: 参数调节区
		var controlsStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };

		// 第一行：音源类型 + 采样选择
		var row1 = new Grid { Margin = new Thickness(0, 0, 0, 8) };
		row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
		row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

		var sourceCmb = new ComboBox
		{
			Height = 30,
			Style = (Style)FindResource("FlatComboBoxStyle"),
			Margin = new Thickness(0, 0, 8, 0)
		};
		sourceCmb.Items.Add(new ComboBoxItem { Content = "🎵 程序极微波形", Tag = SoundSourceType.ProceduralWave });
		sourceCmb.Items.Add(new ComboBoxItem { Content = "📁 本地音频文件", Tag = SoundSourceType.CustomFile });
		sourceCmb.Items.Add(new ComboBoxItem { Content = "📦 借用系统预设", Tag = SoundSourceType.BuiltInPreset });
		sourceCmb.Items.Add(new ComboBoxItem { Content = "🔇 静音 (无声)", Tag = SoundSourceType.Mute });

		sourceCmb.SelectedIndex = ev.SourceType switch
		{
			SoundSourceType.ProceduralWave => 0,
			SoundSourceType.CustomFile => 1,
			SoundSourceType.BuiltInPreset => 2,
			SoundSourceType.Mute => 3,
			_ => 0
		};

		Grid.SetColumn(sourceCmb, 0);
		row1.Children.Add(sourceCmb);

		// 动态内容容器（波形选择 或 文件选择）
		var detailContainer = new ContentControl();
		Grid.SetColumn(detailContainer, 1);
		row1.Children.Add(detailContainer);

		void UpdateDetailView()
		{
			if (sourceCmb.SelectedItem is not ComboBoxItem selectedItem) return;
			var type = (SoundSourceType)selectedItem.Tag;
			ev.SourceType = type;

			if (type == SoundSourceType.ProceduralWave)
			{
				var waveCmb = new ComboBox { Height = 30, Style = (Style)FindResource("FlatComboBoxStyle") };
				waveCmb.Items.Add(new ComboBoxItem { Content = "柔和正弦微波 (Sine 1200Hz)", Tag = "Sine1200" });
				waveCmb.Items.Add(new ComboBoxItem { Content = "清脆方波微动 (Square 850Hz)", Tag = "Square850" });
				waveCmb.Items.Add(new ComboBoxItem { Content = "超短脉冲响应 (Pulse 2.5ms)", Tag = "Pulse2ms" });
				waveCmb.Items.Add(new ComboBoxItem { Content = "机械触点白噪 (Noise Snap)", Tag = "NoiseSnap" });
				waveCmb.Items.Add(new ComboBoxItem { Content = "空灵水滴微音 (Bubble Water)", Tag = "BubbleWater" });

				int match = 0;
				for (int i = 0; i < waveCmb.Items.Count; i++)
				{
					if (waveCmb.Items[i] is ComboBoxItem cbi && (string)cbi.Tag == ev.WavePreset)
					{
						match = i;
						break;
					}
				}
				waveCmb.SelectedIndex = match;
				waveCmb.SelectionChanged += (_, _) =>
				{
					if (waveCmb.SelectedItem is ComboBoxItem item) ev.WavePreset = (string)item.Tag;
				};
				detailContainer.Content = waveCmb;
			}
			else if (type == SoundSourceType.BuiltInPreset)
			{
				var presetCmb = new ComboBox { Height = 30, Style = (Style)FindResource("FlatComboBoxStyle") };
				presetCmb.Items.Add(new ComboBoxItem { Content = "⚙️ 机械手感轴体", Tag = "Mechanical" });
				presetCmb.Items.Add(new ComboBoxItem { Content = "✨ 现代清脆数码", Tag = "Crisp" });
				presetCmb.Items.Add(new ComboBoxItem { Content = "🫧 柔和水滴气泡", Tag = "Bubble" });
				presetCmb.Items.Add(new ComboBoxItem { Content = "⚡ 极简超短脉冲", Tag = "Minimalist" });

				int match = 0;
				for (int i = 0; i < presetCmb.Items.Count; i++)
				{
					if (presetCmb.Items[i] is ComboBoxItem cbi && (string)cbi.Tag == ev.BuiltInTheme)
					{
						match = i;
						break;
					}
				}
				presetCmb.SelectedIndex = match;
				presetCmb.SelectionChanged += (_, _) =>
				{
					if (presetCmb.SelectedItem is ComboBoxItem item) ev.BuiltInTheme = (string)item.Tag;
				};
				detailContainer.Content = presetCmb;
			}
			else if (type == SoundSourceType.CustomFile)
			{
				var fileGrid = new Grid();
				fileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
				fileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

				var pathBox = new TextBox
				{
					Height = 30,
					Text = string.IsNullOrEmpty(ev.CustomFilePath) ? "未选择外部音频文件 (.wav / .mp3)" : ev.CustomFilePath,
					IsReadOnly = true,
					VerticalContentAlignment = VerticalAlignment.Center,
					Margin = new Thickness(0, 0, 6, 0)
				};
				var browseBtn = new Button
				{
					Content = "📂 浏览...",
					Height = 30,
					Style = (Style)FindResource("DlgBtnStyle"),
					Cursor = System.Windows.Input.Cursors.Hand
				};
				browseBtn.Click += (_, _) =>
				{
					var ofd = new OpenFileDialog
					{
						Title = $"选择 {ev.EventName} 音频采样",
						Filter = "音频文件 (*.wav;*.mp3)|*.wav;*.mp3|所有文件 (*.*)|*.*"
					};
					if (ofd.ShowDialog() == true)
					{
						ev.CustomFilePath = ofd.FileName;
						pathBox.Text = ofd.FileName;
					}
				};

				Grid.SetColumn(pathBox, 0);
				Grid.SetColumn(browseBtn, 1);
				fileGrid.Children.Add(pathBox);
				fileGrid.Children.Add(browseBtn);
				detailContainer.Content = fileGrid;
			}
			else
			{
				detailContainer.Content = new TextBlock
				{
					Text = "已静音：该手势事件将保持完全静音，不播放任何反馈。",
					FontSize = 11.5,
					Foreground = (Brush)FindResource("TextSecondaryBrush"),
					VerticalAlignment = VerticalAlignment.Center
				};
			}
		}

		sourceCmb.SelectionChanged += (_, _) => UpdateDetailView();
		UpdateDetailView();
		controlsStack.Children.Add(row1);

		// 第二行：音高 (Pitch) + 时长 (Duration) + 独立音量 (Volume)
		var row2 = new Grid();
		row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

		// 音高调节
		var pitchStack = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
		var pitchHeader = new Grid();
		pitchHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		pitchHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		var pitchTitle = new TextBlock { Text = "音高:", FontSize = 11, Foreground = (Brush)FindResource("TextSecondaryBrush") };
		var pitchLabel = new TextBlock { Text = ev.PitchText, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("AccentPrimaryBrush") };
		Grid.SetColumn(pitchTitle, 0);
		Grid.SetColumn(pitchLabel, 1);
		pitchHeader.Children.Add(pitchTitle);
		pitchHeader.Children.Add(pitchLabel);
		var pitchSlider = new Slider { Minimum = -12, Maximum = 12, Value = ev.PitchSemitones, TickFrequency = 1, IsSnapToTickEnabled = true };
		pitchSlider.ValueChanged += (_, e) =>
		{
			ev.PitchSemitones = (int)Math.Round(e.NewValue);
			pitchLabel.Text = ev.PitchText;
		};
		pitchStack.Children.Add(pitchHeader);
		pitchStack.Children.Add(pitchSlider);
		Grid.SetColumn(pitchStack, 0);
		row2.Children.Add(pitchStack);

		// 时长调节
		var durStack = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
		var durHeader = new Grid();
		durHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		durHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		var durTitle = new TextBlock { Text = "时长:", FontSize = 11, Foreground = (Brush)FindResource("TextSecondaryBrush") };
		var durLabel = new TextBlock { Text = ev.DurationText, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("AccentPrimaryBrush") };
		Grid.SetColumn(durTitle, 0);
		Grid.SetColumn(durLabel, 1);
		durHeader.Children.Add(durTitle);
		durHeader.Children.Add(durLabel);
		var durSlider = new Slider { Minimum = 5, Maximum = 120, Value = ev.DurationMs, TickFrequency = 5, IsSnapToTickEnabled = true };
		durSlider.ValueChanged += (_, e) =>
		{
			ev.DurationMs = (int)Math.Round(e.NewValue);
			durLabel.Text = ev.DurationText;
		};
		durStack.Children.Add(durHeader);
		durStack.Children.Add(durSlider);
		Grid.SetColumn(durStack, 1);
		row2.Children.Add(durStack);

		// 音量调节
		var volStack = new StackPanel();
		var volHeader = new Grid();
		volHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		volHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		var volTitle = new TextBlock { Text = "音量:", FontSize = 11, Foreground = (Brush)FindResource("TextSecondaryBrush") };
		var volLabel = new TextBlock { Text = ev.RelativeVolumePercent, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("AccentPrimaryBrush") };
		Grid.SetColumn(volTitle, 0);
		Grid.SetColumn(volLabel, 1);
		volHeader.Children.Add(volTitle);
		volHeader.Children.Add(volLabel);
		var volSlider = new Slider { Minimum = 0, Maximum = 100, Value = ev.RelativeVolume * 100.0, TickFrequency = 5, IsSnapToTickEnabled = true };
		volSlider.ValueChanged += (_, e) =>
		{
			ev.RelativeVolume = e.NewValue / 100.0;
			volLabel.Text = ev.RelativeVolumePercent;
		};
		volStack.Children.Add(volHeader);
		volStack.Children.Add(volSlider);
		Grid.SetColumn(volStack, 2);
		row2.Children.Add(volStack);

		controlsStack.Children.Add(row2);
		Grid.SetColumn(controlsStack, 1);
		mainGrid.Children.Add(controlsStack);

		// Col 2: 独立试听按钮
		var auditionBtn = new Button
		{
			Content = "▶ 试听",
			Height = 32,
			Style = (Style)FindResource("AuditionBtnStyle"),
			VerticalAlignment = VerticalAlignment.Center,
			HorizontalAlignment = HorizontalAlignment.Right,
			Width = 80,
			ToolTip = $"即时播放 {ev.EventName} 的模拟音效"
		};
		auditionBtn.Click += async (_, _) =>
		{
			auditionBtn.Content = "🔊 ...";
			SoundEffectManager.PlayCustomEventPreview(ev);
			await Task.Delay(250);
			auditionBtn.Content = "▶ 试听";
		};
		Grid.SetColumn(auditionBtn, 2);
		mainGrid.Children.Add(auditionBtn);

		border.Child = mainGrid;
		return border;
	}

	private async void PlaySequenceButton_Click(object sender, RoutedEventArgs e)
	{
		PlaySequenceButton.IsEnabled = false;
		try
		{
			var p = _currentProfile;
			var evPopup = p?.Events.FirstOrDefault(x => x.EventType == SoundType.WheelPopup);
			var evHover = p?.Events.FirstOrDefault(x => x.EventType == SoundType.SectorHover);
			var evExpand = p?.Events.FirstOrDefault(x => x.EventType == SoundType.SubmenuExpand);
			var evExec = p?.Events.FirstOrDefault(x => x.EventType == SoundType.ActionExecute);
			var evCancel = p?.Events.FirstOrDefault(x => x.EventType == SoundType.GestureCancel);

			SequenceStatusLabel.Text = "🌟 正在唤出轮盘...";
			if (evPopup != null) SoundEffectManager.PlayCustomEventPreview(evPopup);
			await Task.Delay(220);

			SequenceStatusLabel.Text = "🎯 正在划过扇区...";
			if (evHover != null) SoundEffectManager.PlayCustomEventPreview(evHover);
			await Task.Delay(180);

			SequenceStatusLabel.Text = "🌿 正在展开二级级联...";
			if (evExpand != null) SoundEffectManager.PlayCustomEventPreview(evExpand);
			await Task.Delay(220);

			SequenceStatusLabel.Text = "⚡ 正在执行确认动作...";
			if (evExec != null) SoundEffectManager.PlayCustomEventPreview(evExec);
			await Task.Delay(240);

			SequenceStatusLabel.Text = "↩️ 正在外甩脱离取消...";
			if (evCancel != null) SoundEffectManager.PlayCustomEventPreview(evCancel);
			await Task.Delay(200);

			SequenceStatusLabel.Text = "✅ 完整交互手势音效流演示完毕";
		}
		catch
		{
			SequenceStatusLabel.Text = "准备就绪";
		}
		finally
		{
			PlaySequenceButton.IsEnabled = true;
		}
	}

	private void NewProfile_Click(object sender, RoutedEventArgs e)
	{
		var newProf = new CustomSoundProfile
		{
			Id = Guid.NewGuid().ToString("N"),
			Name = $"🎨 新建自定义方案 {_profiles.Count + 1}",
			Description = "用户新建的自定义音效微调方案。",
			IsBuiltIn = false,
			Events = new List<SoundEventConfig>
			{
				new() { EventType = SoundType.WheelPopup, SourceType = SoundSourceType.ProceduralWave, WavePreset = "Sine1200", PitchSemitones = 0, DurationMs = 25, RelativeVolume = 0.8 },
				new() { EventType = SoundType.SectorHover, SourceType = SoundSourceType.ProceduralWave, WavePreset = "Square850", PitchSemitones = 2, DurationMs = 15, RelativeVolume = 0.7 },
				new() { EventType = SoundType.SubmenuExpand, SourceType = SoundSourceType.ProceduralWave, WavePreset = "Pulse2ms", PitchSemitones = 4, DurationMs = 30, RelativeVolume = 0.85 },
				new() { EventType = SoundType.ActionExecute, SourceType = SoundSourceType.ProceduralWave, WavePreset = "Sine1200", PitchSemitones = 6, DurationMs = 40, RelativeVolume = 0.95 },
				new() { EventType = SoundType.GestureCancel, SourceType = SoundSourceType.ProceduralWave, WavePreset = "Sine1200", PitchSemitones = -4, DurationMs = 35, RelativeVolume = 0.5 }
			}
		};
		_profiles.Add(newProf);
		LoadProfiles(newProf.Id);
		SequenceStatusLabel.Text = $"已创建方案: {newProf.Name}";
	}

	private void DeleteProfile_Click(object sender, RoutedEventArgs e)
	{
		if (_currentProfile == null) return;
		if (_profiles.Count <= 1)
		{
			AppMessageBox.Show(this, "至少需要保留一个音效配置方案，无法删除最后一个方案。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}
		if (_currentProfile.IsBuiltIn)
		{
			AppMessageBox.Show(this, "系统预置默认方案受保护不可删除。如需自定义修改，可点击【➕ 新建方案】创建可编辑副本。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}

		var result = AppMessageBox.Show(this, $"确定要删除自定义音效方案「{_currentProfile.Name}」吗？\n删除后不可撤销。", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question);
		if (result == MessageBoxResult.Yes)
		{
			string deletedName = _currentProfile.Name;
			int delIdx = _profiles.IndexOf(_currentProfile);
			_profiles.Remove(_currentProfile);
			int nextIdx = Math.Clamp(delIdx - 1, 0, _profiles.Count - 1);
			LoadProfiles(_profiles[nextIdx].Id);
			SequenceStatusLabel.Text = $"🗑️ 已删除方案: {deletedName}";
		}
	}

	private void ImportProfile_Click(object sender, RoutedEventArgs e)
	{
		var ofd = new OpenFileDialog
		{
			Title = "导入 StarPie 自定义音效方案",
			Filter = "StarPie 音效方案 (*.starpie-sound;*.json)|*.starpie-sound;*.json|所有文件 (*.*)|*.*"
		};
		if (ofd.ShowDialog() == true)
		{
			try
			{
				string json = System.IO.File.ReadAllText(ofd.FileName);
				var prof = System.Text.Json.JsonSerializer.Deserialize<CustomSoundProfile>(json);
				if (prof != null && prof.Events != null && prof.Events.Count > 0)
				{
					prof.Id = Guid.NewGuid().ToString("N");
					prof.IsBuiltIn = false;
					if (string.IsNullOrWhiteSpace(prof.Name)) prof.Name = "导入方案";
					_profiles.Add(prof);
					LoadProfiles(prof.Id);
					SequenceStatusLabel.Text = $"✅ 成功导入方案: {prof.Name}";
				}
			}
			catch (Exception ex)
			{
				AppMessageBox.Show(this, $"导入失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}
	}

	private void ExportProfile_Click(object sender, RoutedEventArgs e)
	{
		if (_currentProfile == null) return;
		var sfd = new SaveFileDialog
		{
			Title = "导出当前音效方案",
			Filter = "StarPie 音效方案 (*.starpie-sound)|*.starpie-sound|JSON 文件 (*.json)|*.json",
			FileName = $"{_currentProfile.Name}.starpie-sound"
		};
		if (sfd.ShowDialog() == true)
		{
			try
			{
				string json = System.Text.Json.JsonSerializer.Serialize(_currentProfile, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
				System.IO.File.WriteAllText(sfd.FileName, json);
				SequenceStatusLabel.Text = $"✅ 成功导出方案: {System.IO.Path.GetFileName(sfd.FileName)}";
			}
			catch (Exception ex)
			{
				AppMessageBox.Show(this, $"导出失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}
	}

	private void ResetProfile_Click(object sender, RoutedEventArgs e)
	{
		if (_currentProfile != null)
		{
			var defaults = CustomSoundProfile.CreateDefaultDemoProfiles();
			var match = defaults.FirstOrDefault(d => d.Id == _currentProfile.Id);
			if (match != null)
			{
				_currentProfile.Events.Clear();
				foreach (var ev in match.Events)
				{
					_currentProfile.Events.Add(ev.Clone());
				}
				RenderEventCards();
				SequenceStatusLabel.Text = "🔄 已恢复当前方案为预置默认值";
			}
			else
			{
				SequenceStatusLabel.Text = "当前为用户自建方案，保留当前参数";
			}
		}
	}

	private void SaveButton_Click(object sender, RoutedEventArgs e)
	{
		if (ConfigManager.CurrentConfig != null)
		{
			ConfigManager.CurrentConfig.CustomSoundProfiles = _profiles;
			if (_currentProfile != null)
			{
				ConfigManager.CurrentConfig.ActiveCustomSoundProfileId = _currentProfile.Id;
			}
			ConfigManager.SaveConfig();

			if (string.Equals(ConfigManager.CurrentConfig.SoundTheme, "Custom", StringComparison.OrdinalIgnoreCase))
			{
				SoundEffectManager.Initialize("Custom", ConfigManager.CurrentConfig.SoundVolume, force: true);
			}
		}
		DialogResult = true;
		Close();
	}

	private void CloseButton_Click(object sender, RoutedEventArgs e)
	{
		Close();
	}
}
