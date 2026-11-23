using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinPieGestures;

public partial class SettingsWindow
{
	private WheelProfile? _pendingParallelProfile;
	private WheelProfile? _lastDetailProfile;
	private bool _profileBindingsDetailsExpanded;
	private FrameworkElement? _layoutResourceHost;
	private int _profileIconRevision;
	private readonly Dictionary<string, BitmapSource?> _profileIcons = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, Task<BitmapSource?>> _profileIconLoads = new(StringComparer.OrdinalIgnoreCase);

	private Style? TryFindProfileStyle(string resourceKey) =>
		(_layoutResourceHost ?? (FrameworkElement)this).TryFindResource(resourceKey) as Style;

	private static List<string> ProfileBindingTokens(WheelProfile profile)
	{
		return ProfileProcessBindings.GetNames(profile).Select(p => p + ".exe").ToList();
	}

	private void RefreshProfilePresentation()
	{
		if (ProfileQuickSwitchPanel == null || ConfigManager.CurrentConfig?.Profiles == null) return;
		int revision = ++_profileIconRevision;
		if (!ReferenceEquals(_lastDetailProfile, _selectedProfile))
		{
			_profileBindingsDetailsExpanded = false;
			_lastDetailProfile = _selectedProfile;
		}

		string displayMode = ProfilePresentation.NormalizeQuickSwitchDisplayMode(
			ConfigManager.CurrentConfig.ProfileQuickSwitchDisplayMode);
		ConfigManager.CurrentConfig.ProfileQuickSwitchDisplayMode = displayMode;
		SyncProfileDisplayModeComboBox(displayMode);

		double horizontalOffset = ProfileQuickSwitchScrollViewer?.HorizontalOffset ?? 0;
		ProfileQuickSwitchPanel.Children.Clear();
		Style? modernButtonStyle = TryFindProfileStyle("ModernButtonStyle");
		int profileCount = ConfigManager.CurrentConfig.Profiles.Count;
		int index = 0;
		foreach (var profile in ConfigManager.CurrentConfig.Profiles)
		{
			bool selected = ReferenceEquals(profile, _selectedProfile);
			bool isLast = index++ == profileCount - 1;
			var button = new Button
			{
				Tag = profile,
				Margin = new Thickness(0, 0, isLast ? 0 : 6, 0),
				ToolTip = profile.DisplayTitle
			};
			if (modernButtonStyle != null) button.Style = modernButtonStyle;
			AutomationProperties.SetName(button, profile.DisplayTitle);
			AutomationProperties.SetItemStatus(button, selected ? "Selected" : "Unselected");
			button.SetResourceReference(Control.BackgroundProperty, selected ? "NavTabActiveBgBrush" : "SubtleCardBrush");
			button.SetResourceReference(Control.BorderBrushProperty, selected ? "AccentPrimaryBrush" : "CardBorderBrush");
			button.BorderThickness = new Thickness(selected ? 2 : 1);
			button.Click += (_, _) =>
			{
				if (MappingsProfileComboBox != null) MappingsProfileComboBox.SelectedItem = profile;
				else { _selectedProfile = profile; UpdateProfileBindingUi(); }
			};

			if (displayMode == ProfileQuickSwitchDisplayModes.TextOnly)
			{
				button.Height = 32;
				button.Padding = new Thickness(10, 0, 10, 0);
				button.Content = new TextBlock
				{
					Text = (selected ? "✓ " : "") + profile.FriendlyName,
					MaxWidth = 120,
					TextTrimming = TextTrimming.CharacterEllipsis,
					VerticalAlignment = VerticalAlignment.Center
				};
			}
			else if (displayMode == ProfileQuickSwitchDisplayModes.IconOnly)
			{
				button.Width = 36;
				button.Height = 34;
				button.Padding = new Thickness(0);
				var container = new Grid { Width = 32, Height = 30 };
				var image = new Image
				{
					Width = 20,
					Height = 20,
					HorizontalAlignment = HorizontalAlignment.Center,
					VerticalAlignment = VerticalAlignment.Center
				};
				container.Children.Add(image);
				if (selected)
				{
					var mark = new TextBlock
					{
						Text = "✓",
						FontSize = 9,
						FontWeight = FontWeights.Bold,
						HorizontalAlignment = HorizontalAlignment.Right,
						VerticalAlignment = VerticalAlignment.Bottom,
						Margin = new Thickness(0, 0, 1, 0)
					};
					mark.SetResourceReference(TextBlock.ForegroundProperty, "AccentPrimaryBrush");
					container.Children.Add(mark);
				}
				button.Content = container;
				_ = LoadProfileIconAsync(profile, image, revision);
			}
			else
			{
				button.Height = 34;
				button.Padding = new Thickness(8, 0, 8, 0);
				var image = new Image
				{
					Width = 20,
					Height = 20,
					Margin = new Thickness(0, 0, 6, 0),
					VerticalAlignment = VerticalAlignment.Center
				};
				var content = new StackPanel { Orientation = Orientation.Horizontal };
				content.Children.Add(image);
				content.Children.Add(new TextBlock
				{
					Text = (selected ? "✓ " : "") + profile.FriendlyName,
					MaxWidth = 105,
					TextTrimming = TextTrimming.CharacterEllipsis,
					VerticalAlignment = VerticalAlignment.Center
				});
				button.Content = content;
				_ = LoadProfileIconAsync(profile, image, revision);
			}

			ProfileQuickSwitchPanel.Children.Add(button);
		}
		ProfileQuickSwitchScrollViewer?.ScrollToHorizontalOffset(horizontalOffset);

		bool isGlobal = _selectedProfile == null ||
			string.Equals(_selectedProfile.ProcessName, "Global", StringComparison.OrdinalIgnoreCase);
		if (ProfileContextSwitchRow != null)
		{
			ProfileContextSwitchRow.Visibility = isGlobal ? Visibility.Collapsed : Visibility.Visible;
		}
		if (ProfileUseContextCheckBox != null)
		{
			bool updating = _isUpdatingUi;
			try
			{
				_isUpdatingUi = true;
				ProfileUseContextCheckBox.IsChecked = _selectedProfile?.UseApplicationContext != false;
			}
			finally
			{
				_isUpdatingUi = updating;
			}
			ProfileUseContextCheckBox.Content = I18n.T("ProfileUseContext");
			ProfileUseContextCheckBox.ToolTip = I18n.T(
				_selectedProfile?.UseApplicationContext != false ? "ProfileContextHint" : "ProfileParallelHint");
		}
		UpdateProfileModeHint();

		bool showContextBindings = !isGlobal && _selectedProfile != null && _selectedProfile.UseApplicationContext;
		if (ProfileCustomBindingPanel != null)
		{
			ProfileCustomBindingPanel.Visibility = showContextBindings ? Visibility.Visible : Visibility.Collapsed;
		}
		if (ProfileBoundProcessesLabel != null)
		{
			ProfileBoundProcessesLabel.Text = I18n.T("ProfileApplicableAppsLabel");
			ProfileBoundProcessesLabel.ToolTip = I18n.T("ProfileBoundProcessesHint");
		}
		if (ProfileQuickAddProgramBtn != null)
		{
			ProfileQuickAddProgramBtn.Content = I18n.T("ProfileQuickAddProgram");
			ProfileQuickAddProgramBtn.ToolTip = I18n.T("ProfileBoundProcessesToolTip");
		}
		if (ProfileQuickCaptureMenuItem != null) ProfileQuickCaptureMenuItem.Header = I18n.T("ProfileCaptureWindowBtnText");
		if (ProfileQuickPickProgramMenuItem != null) ProfileQuickPickProgramMenuItem.Header = I18n.T("ProfilePickProgramBtnText");
		if (ProfileQuickBrowseExeMenuItem != null) ProfileQuickBrowseExeMenuItem.Header = I18n.T("ProfileBrowseExeBtnText");
		if (ProfileManageBindingsToggleBtn != null)
		{
			ProfileManageBindingsToggleBtn.Content = I18n.T(
				_profileBindingsDetailsExpanded ? "ProfileManageBindingsCollapse" : "ProfileManageBindingsExpand");
			ProfileManageBindingsToggleBtn.ToolTip = I18n.T("ProfileBoundProcessesHint");
		}

		List<string> tokens = _selectedProfile != null ? ProfileBindingTokens(_selectedProfile) : new List<string>();
		PopulateQuickBindingChips(tokens, modernButtonStyle);

		if (ProfileBindingsDetailPanel != null)
		{
			ProfileBindingsDetailPanel.Visibility = (showContextBindings && _profileBindingsDetailsExpanded)
				? Visibility.Visible
				: Visibility.Collapsed;
		}

		if (ProfileBindingsPanel != null && _selectedProfile != null)
		{
			ProfileBindingsPanel.Children.Clear();
			foreach (string process in tokens)
			{
				var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 8, 2) };
				row.Children.Add(new TextBlock
				{
					Text = "▣ " + process,
					MaxWidth = 210,
					TextTrimming = TextTrimming.CharacterEllipsis,
					VerticalAlignment = VerticalAlignment.Center,
					ToolTip = process
				});
				var edit = new Button
				{
					Content = I18n.T("ProfileEditBinding"),
					Tag = process,
					Padding = new Thickness(7, 2, 7, 2),
					Margin = new Thickness(5, 0, 3, 0)
				};
				if (modernButtonStyle != null) edit.Style = modernButtonStyle;
				edit.Click += ProfileEditBinding_Click;
				var remove = new Button
				{
					Content = "×",
					Tag = process,
					ToolTip = I18n.T("ProfileRemoveBinding"),
					Padding = new Thickness(7, 2, 7, 2)
				};
				if (modernButtonStyle != null) remove.Style = modernButtonStyle;
				remove.Click += ProfileRemoveBinding_Click;
				row.Children.Add(edit);
				row.Children.Add(remove);
				ProfileBindingsPanel.Children.Add(row);
			}
			if (showContextBindings && ProfileBindingsPanel.Children.Count == 0)
			{
				ProfileBindingsPanel.Children.Add(new TextBlock
				{
					Text = I18n.T("ProfileNoBindings"),
					TextWrapping = TextWrapping.Wrap
				});
			}
		}

		RefreshProfileActivationUi();
	}

	private void SyncProfileDisplayModeComboBox(string displayMode)
	{
		if (ProfileDisplayModeComboBox == null) return;
		if (ProfileDisplayModeIconAndTextItem != null) ProfileDisplayModeIconAndTextItem.Content = I18n.T("ProfileDisplayModeIconAndText");
		if (ProfileDisplayModeTextOnlyItem != null) ProfileDisplayModeTextOnlyItem.Content = I18n.T("ProfileDisplayModeTextOnly");
		if (ProfileDisplayModeIconOnlyItem != null) ProfileDisplayModeIconOnlyItem.Content = I18n.T("ProfileDisplayModeIconOnly");
		ProfileDisplayModeComboBox.ToolTip = I18n.T("ProfileDisplayModeToolTip");

		bool updating = _isUpdatingUi;
		try
		{
			_isUpdatingUi = true;
			foreach (object itemObj in ProfileDisplayModeComboBox.Items)
			{
				if (itemObj is ComboBoxItem item &&
					string.Equals(item.Tag as string, displayMode, StringComparison.OrdinalIgnoreCase))
				{
					ProfileDisplayModeComboBox.SelectedItem = item;
					break;
				}
			}
		}
		finally
		{
			_isUpdatingUi = updating;
		}
	}

	private void PopulateQuickBindingChips(List<string> tokens, Style? modernButtonStyle)
	{
		if (ProfileQuickBindingsPanel == null) return;
		for (int i = ProfileQuickBindingsPanel.Children.Count - 1; i >= 0; i--)
		{
			if (!ReferenceEquals(ProfileQuickBindingsPanel.Children[i], ProfileQuickAddProgramBtn))
			{
				ProfileQuickBindingsPanel.Children.RemoveAt(i);
			}
		}

		int insertIndex = 0;
		if (tokens.Count == 0)
		{
			var emptyLabel = new TextBlock
			{
				Text = I18n.T("ProfileNoBindingsShort"),
				FontSize = 11.5,
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(0, 2, 6, 2)
			};
			emptyLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
			ProfileQuickBindingsPanel.Children.Insert(insertIndex++, emptyLabel);
			return;
		}

		Style? closeButtonStyle = TryFindProfileStyle("CanvasToolBtnStyle") ?? modernButtonStyle;
		foreach (string process in tokens)
		{
			var chip = new Border
			{
				CornerRadius = new CornerRadius(4),
				BorderThickness = new Thickness(1),
				Padding = new Thickness(7, 1, 3, 1),
				Margin = new Thickness(0, 2, 5, 2),
				Height = 24,
				ToolTip = process
			};
			chip.SetResourceReference(Border.BackgroundProperty, "SubtleCardBrush");
			chip.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");

			var stack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
			var label = new TextBlock
			{
				Text = process,
				FontSize = 11.5,
				MaxWidth = 140,
				TextTrimming = TextTrimming.CharacterEllipsis,
				VerticalAlignment = VerticalAlignment.Center
			};
			label.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
			var removeBtn = new Button
			{
				Content = "×",
				Tag = process,
				ToolTip = I18n.T("ProfileRemoveBinding"),
				Width = 18,
				Height = 18,
				Padding = new Thickness(0),
				Margin = new Thickness(3, 0, 0, 0),
				FontSize = 11,
				VerticalAlignment = VerticalAlignment.Center
			};
			if (closeButtonStyle != null) removeBtn.Style = closeButtonStyle;
			AutomationProperties.SetName(removeBtn, I18n.T("ProfileRemoveBinding") + ": " + process);
			removeBtn.Click += ProfileRemoveBinding_Click;

			stack.Children.Add(label);
			stack.Children.Add(removeBtn);
			chip.Child = stack;
			ProfileQuickBindingsPanel.Children.Insert(insertIndex++, chip);
		}
	}

	private async Task<BitmapSource?> GetCachedProfileIconAsync(WheelProfile profile)
	{
		string cacheKey = profile.ApplicationIconPath + "|" + string.Join(",", ProfileBindingTokens(profile));
		if (!_profileIcons.TryGetValue(cacheKey, out var icon))
		{
			try
			{
				if (!_profileIconLoads.TryGetValue(cacheKey, out var loading))
				{
					string? iconHint = profile.ApplicationIconPath;
					var tokens = ProfileBindingTokens(profile).ToArray();
					bool global = string.Equals(profile.ProcessName, "Global", StringComparison.OrdinalIgnoreCase);
					loading = Task.Run(() =>
					{
						if (global && string.IsNullOrWhiteSpace(iconHint)) return null;
						string? path = iconHint;
						if (string.IsNullOrWhiteSpace(path))
						{
							string cache = Path.Combine(ApplicationDataPaths.ProgramCacheDirectory, "program_cache.json");
							if (File.Exists(cache) && new FileInfo(cache).Length <= 4 * 1024 * 1024)
							{
								using var json = JsonDocument.Parse(File.ReadAllText(cache));
								foreach (var item in json.RootElement.EnumerateArray())
								{
									if (!item.TryGetProperty("Path", out var value)) continue;
									string? candidate = value.GetString();
									if (candidate != null && tokens.Any(p => string.Equals(p, Path.GetFileName(candidate), StringComparison.OrdinalIgnoreCase))) { path = candidate; break; }
								}
							}
						}
						if (string.IsNullOrWhiteSpace(path)) return null;
						var result = IconHelper.ExtractShellItemIcon(path, 32);
						if (result?.CanFreeze == true) result.Freeze();
						return result;
					});
					_profileIconLoads[cacheKey] = loading;
				}
				icon = await loading;
			}
			catch { icon = null; }
			_profileIconLoads.Remove(cacheKey);
			if (_resourcesReleased) return null;
			if (_profileIcons.Count >= 128) _profileIcons.Clear();
			_profileIcons[cacheKey] = icon;
		}
		return icon;
	}

	private async Task LoadProfileIconAsync(WheelProfile profile, Image image, int revision)
	{
		if (ProfileIconPresentation.TrySetVector(image, profile.IconKey)) return;

		var icon = await GetCachedProfileIconAsync(profile);
		if (_resourcesReleased) return;
		if (revision != _profileIconRevision) return;
		if (ProfilePresentation.NormalizeQuickSwitchDisplayMode(
			ConfigManager.CurrentConfig?.ProfileQuickSwitchDisplayMode) == ProfileQuickSwitchDisplayModes.TextOnly) return;
		if (icon != null)
		{
			image.Source = icon;
		}
		else
		{
			image.Visibility = Visibility.Collapsed;
			if (image.Parent is Panel panel && !panel.Children.OfType<TextBlock>().Any(tb => tb.Tag as string == "FallbackGlyph"))
			{
				var glyph = new TextBlock
				{
					Text = string.Equals(profile.ProcessName, "Global", StringComparison.OrdinalIgnoreCase) ? "🌐" : profile.UseApplicationContext ? "▣" : "◎",
					Tag = "FallbackGlyph",
					VerticalAlignment = VerticalAlignment.Center
				};
				if (panel is StackPanel)
				{
					glyph.FontSize = 18;
					glyph.Margin = new Thickness(0, 0, 6, 0);
				}
				else
				{
					glyph.FontSize = 17;
					glyph.HorizontalAlignment = HorizontalAlignment.Center;
				}
				panel.Children.Insert(0, glyph);
			}
		}
	}

	internal void SetProfileQuickSwitchDisplayMode(string? mode)
	{
		if (ConfigManager.CurrentConfig == null) return;
		string normalized = ProfilePresentation.NormalizeQuickSwitchDisplayMode(mode);
		if (!string.Equals(ConfigManager.CurrentConfig.ProfileQuickSwitchDisplayMode, normalized, StringComparison.Ordinal))
		{
			ConfigManager.CurrentConfig.ProfileQuickSwitchDisplayMode = normalized;
			if (_isUiInitialized && !_isUpdatingUi) ScheduleAutoSave();
			else ConfigManager.MarkConfigurationChanged();
		}
		RefreshProfilePresentation();
	}

	internal bool ScrollProfileQuickSwitchByWheelDelta(int delta)
	{
		if (ProfileQuickSwitchScrollViewer == null || delta == 0) return false;
		double step = -(delta / 120.0) * 64.0;
		double current = ProfileQuickSwitchScrollViewer.HorizontalOffset;
		double maxOffset = ProfileQuickSwitchScrollViewer.ScrollableWidth;
		double target = maxOffset > 0
			? Math.Clamp(current + step, 0, maxOffset)
			: Math.Max(0, current + step);
		ProfileQuickSwitchScrollViewer.ScrollToHorizontalOffset(target);
		return true;
	}

	private void ProfileQuickSwitchScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
	{
		if (ScrollProfileQuickSwitchByWheelDelta(e.Delta))
		{
			e.Handled = true;
		}
	}

	private void ProfileDisplayModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (_isUpdatingUi || ConfigManager.CurrentConfig == null) return;
		if (ProfileDisplayModeComboBox?.SelectedItem is ComboBoxItem item && item.Tag is string mode)
		{
			SetProfileQuickSwitchDisplayMode(mode);
		}
	}

	private void ProfileQuickAddProgramBtn_Click(object sender, RoutedEventArgs e)
	{
		if (sender is Button btn && btn.ContextMenu != null)
		{
			btn.ContextMenu.PlacementTarget = btn;
			btn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
			btn.ContextMenu.IsOpen = true;
		}
		else
		{
			ProfilePickProgramBtn_Click(sender, e);
		}
	}

	private void ProfileManageBindingsToggleBtn_Click(object sender, RoutedEventArgs e)
	{
		_profileBindingsDetailsExpanded = !_profileBindingsDetailsExpanded;
		RefreshProfilePresentation();
	}

	private void ProfileUseContext_Changed(object sender, RoutedEventArgs e)
	{
		if (_isUpdatingUi || _selectedProfile == null) return;
		bool context = ProfileUseContextCheckBox.IsChecked == true;
		if (context == _selectedProfile.UseApplicationContext) return;
		var error = ProfileTriggerRouter.ValidateContextMode(ConfigManager.CurrentConfig, _selectedProfile, context);
		if (error == ProfileTriggerValidationError.MissingTrigger)
		{
			RefreshProfilePresentation();
			RecordProfileActivationButton_Click(sender, e);
			_pendingParallelProfile = _selectedProfile;
			UpdateProfileModeHint();
			return;
		}
		if (error != ProfileTriggerValidationError.None) { ShowProfileRouteError(error); RefreshProfilePresentation(); return; }
		_selectedProfile.UseApplicationContext = context;
		ScheduleAutoSave(); UpdateProfileBindingUi();
	}

	private void ShowProfileRouteError(ProfileTriggerValidationError error)
	{
		if (!IsVisible) return;
		AppMessageBox.Show(this,
			I18n.T(error == ProfileTriggerValidationError.MissingTrigger ? "ProfileParallelRecordRequired" : "ProfileActivationConflict"),
			I18n.T("ProfileActivationRecord"), MessageBoxButton.OK, MessageBoxImage.Information);
	}

	private void UpdateProfileModeHint()
	{
		if (ProfileModeHint == null || ProfileModeBadgeBorder == null || _selectedProfile == null) return;
		bool isGlobal = string.Equals(_selectedProfile.ProcessName, "Global", StringComparison.OrdinalIgnoreCase);
		if (ReferenceEquals(_pendingParallelProfile, _selectedProfile))
		{
			ProfileModeHint.Text = I18n.T("ProfileParallelRecordRequired");
			ProfileModeBadgeBorder.ToolTip = ProfileModeHint.Text;
			ProfileModeBadgeBorder.Visibility = Visibility.Visible;
		}
		else if (!isGlobal && !_selectedProfile.UseApplicationContext)
		{
			ProfileModeHint.Text = I18n.T("ParallelProfileMode");
			ProfileModeBadgeBorder.ToolTip = I18n.T("ProfileParallelHint");
			ProfileModeBadgeBorder.Visibility = Visibility.Visible;
		}
		else
		{
			ProfileModeHint.Text = string.Empty;
			ProfileModeBadgeBorder.ToolTip = null;
			ProfileModeBadgeBorder.Visibility = Visibility.Collapsed;
		}
	}

	private void ProfileRemoveBinding_Click(object sender, RoutedEventArgs e)
	{
		if (_selectedProfile == null || sender is not Button { Tag: string process }) return;
		var bindings = ProfileBindingTokens(_selectedProfile);
		bindings.RemoveAll(p => string.Equals(p, process, StringComparison.OrdinalIgnoreCase));
		if (TryApplyProfileBindings(string.Join(", ", bindings))) { ScheduleAutoSave(); UpdateProfileBindingUi(); }
	}

	private void ProfileEditBinding_Click(object sender, RoutedEventArgs e)
	{
		if (_selectedProfile == null || sender is not Button { Tag: string process }) return;
		WheelProfile target = _selectedProfile;
		var picker = new ProgramPickerWindow { Owner = this };
		if (picker.ShowDialog() != true || string.IsNullOrWhiteSpace(picker.SelectedPath) || !ReferenceEquals(target, _selectedProfile)) return;
		var bindings = ProfileBindingTokens(target);
		int index = bindings.FindIndex(p => string.Equals(p, process, StringComparison.OrdinalIgnoreCase));
		if (index < 0) return;
		bindings[index] = Path.GetFileName(picker.SelectedPath).ToLowerInvariant();
		if (TryApplyProfileBindings(string.Join(", ", bindings.Distinct(StringComparer.OrdinalIgnoreCase))))
		{
			target.ApplicationIconPath = picker.SelectedPath;
			ScheduleAutoSave(); UpdateProfileBindingUi();
		}
	}

	private void RenameCurrentProfile()
	{
		if (_selectedProfile == null) return;
		var dialog = new InputDialog(I18n.T("ProfileRenameTitle"), I18n.T("ProfileRenamePrompt"), _selectedProfile.FriendlyName,
			value => (!string.IsNullOrWhiteSpace(value) && !ConfigManager.CurrentConfig.Profiles.Any(p => !ReferenceEquals(p, _selectedProfile) &&
				string.Equals(p.FriendlyName, value.Trim(), StringComparison.OrdinalIgnoreCase)), I18n.T("ProfileNameInvalidOrDuplicate"))) { Owner = this };
		if (dialog.ShowDialog() != true) return;
		// Freeze legacy process aliases before changing the friendly label.
		if (!_selectedProfile.HasExplicitProcessBindings && !string.Equals(_selectedProfile.ProcessName, "Global", StringComparison.OrdinalIgnoreCase))
		{
			_selectedProfile.BoundProcesses = string.Join(", ", ProfileBindingTokens(_selectedProfile));
			_selectedProfile.HasExplicitProcessBindings = true;
		}
		_selectedProfile.DisplayName = dialog.InputText.Trim();
		ScheduleAutoSave(); RefreshProfilesUi(_selectedProfile);
	}
}
