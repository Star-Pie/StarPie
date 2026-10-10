using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace WinPieGestures;

public partial class IconPickerWindow : Window
{
	private Border? _selectedCard;

	public string? SelectedIconKey { get; private set; }

	public IconPickerWindow(string? initialKey = null)
	{
		InitializeComponent();
		AppThemeManager.ApplyTheme(this, AppThemeManager.CurrentEffectiveTheme);
		SelectedIconKey = initialKey;
		PopulateIcons();
		ApplyLocalization();
	}

	private void ApplyLocalization()
	{
		base.Title = I18n.T("IconPickerTitle") + " - StarPie";
		if (HeaderTitleText != null)
		{
			HeaderTitleText.Text = I18n.T("IconPickerHeader");
		}
		if (HeaderSubtitleText != null)
		{
			HeaderSubtitleText.Text = I18n.T("IconPickerSubtitle");
		}
		if (SearchTextBox != null)
		{
			SearchTextBox.ToolTip = I18n.T("IconPickerSearchTooltip");
		}
		if (ImportIconButton != null)
		{
			ImportIconButton.Content = I18n.T("IconPickerImport");
		}
		if (ClearIconButton != null)
		{
			ClearIconButton.Content = I18n.T("IconPickerClear");
		}
		if (SelectedIconPrefixText != null)
		{
			SelectedIconPrefixText.Text = I18n.T("IconPickerSelected") + " ";
		}
		if (ConfirmButton != null)
		{
			ConfirmButton.Content = I18n.T("BtnConfirm");
		}
		if (CancelButton != null)
		{
			CancelButton.Content = I18n.T("BtnCancel");
		}
		if (string.IsNullOrEmpty(SelectedIconKey) && SelectedIconNameLabel != null)
		{
			SelectedIconNameLabel.Text = I18n.T("IconPickerNone");
		}
	}

	private void PopulateIcons(string filter = "")
	{
		IconsWrapPanel.Children.Clear();
		_selectedCard = null;
		Brush background = (Brush)FindResource("SubtleCardBrush");
		Brush borderBrush = (Brush)FindResource("InputBorderBrush");
		Brush fill = (Brush)FindResource("TextPrimaryBrush");
		Brush foreground = (Brush)FindResource("TextSecondaryBrush");
		List<IconHelper.CustomIconItem> list = IconHelper.GetCustomIcons();
		if (!string.IsNullOrEmpty(filter))
		{
			list = list.Where((IconHelper.CustomIconItem i) => i.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) || i.Key.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
		}
		foreach (IconHelper.CustomIconItem custom in list)
		{
			Border card = new Border
			{
				Background = background,
				BorderBrush = borderBrush,
				BorderThickness = new Thickness(1.0),
				CornerRadius = new CornerRadius(8.0),
				Margin = new Thickness(4.0),
				Padding = new Thickness(6.0),
				Cursor = Cursors.Hand,
				Tag = custom.Key
			};
			Grid grid = new Grid();
			StackPanel stackPanel = new StackPanel
			{
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center
			};
			FrameworkElement element = ((!custom.IsSvg) ? ((FrameworkElement)new Image
			{
				Width = 24.0,
				Height = 24.0,
				Stretch = Stretch.Uniform,
				HorizontalAlignment = HorizontalAlignment.Center,
				Margin = new Thickness(0.0, 0.0, 0.0, 4.0),
				Source = IconHelper.GetCustomImageSource(custom.FilePath)
			}) : ((FrameworkElement)new Path
			{
				Data = Geometry.Parse(custom.SvgData),
				Fill = (Brush)FindResource("AccentPrimaryBrush"),
				Width = 24.0,
				Height = 24.0,
				Stretch = Stretch.Uniform,
				HorizontalAlignment = HorizontalAlignment.Center,
				Margin = new Thickness(0.0, 0.0, 0.0, 4.0)
			}));
			TextBlock element2 = new TextBlock
			{
				Text = custom.DisplayName,
				FontSize = 10.0,
				Foreground = foreground,
				TextTrimming = TextTrimming.CharacterEllipsis,
				MaxWidth = 72.0,
				HorizontalAlignment = HorizontalAlignment.Center,
				TextAlignment = TextAlignment.Center
			};
			stackPanel.Children.Add(element);
			stackPanel.Children.Add(element2);
			grid.Children.Add(stackPanel);
			Button button = new Button
			{
				Content = "✕",
				FontSize = 9.0,
				Width = 16.0,
				Height = 16.0,
				Padding = new Thickness(0.0),
				HorizontalAlignment = HorizontalAlignment.Right,
				VerticalAlignment = VerticalAlignment.Top,
				Background = Brushes.Transparent,
				BorderThickness = new Thickness(0.0),
				Foreground = (Brush)FindResource("TextSecondaryBrush"),
				Cursor = Cursors.Hand,
				ToolTip = "删除此自定义图标"
			};
			button.Click += delegate(object s, RoutedEventArgs e)
			{
				e.Handled = true;
				if (IconHelper.DeleteCustomIcon(custom.Key))
				{
					PopulateIcons(SearchTextBox.Text.Trim());
				}
			};
			grid.Children.Add(button);
			card.Child = grid;
			if (string.Equals(SelectedIconKey, custom.Key, StringComparison.OrdinalIgnoreCase))
			{
				SelectCustomCard(card, custom);
			}
			card.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
			{
				SelectCustomCard(card, custom);
				if (e.ClickCount == 2)
				{
					Confirm_Click(this, new RoutedEventArgs());
				}
			};
			IconsWrapPanel.Children.Add(card);
		}
		List<VectorIconItem> list2 = IconHelper.VectorIconList;
		if (!string.IsNullOrEmpty(filter))
		{
			list2 = list2.Where((VectorIconItem i) => i.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) || i.Category.Contains(filter, StringComparison.OrdinalIgnoreCase) || i.Key.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
		}
		foreach (VectorIconItem item in list2)
		{
			Border card2 = new Border
			{
				Background = background,
				BorderBrush = borderBrush,
				BorderThickness = new Thickness(1.0),
				CornerRadius = new CornerRadius(8.0),
				Margin = new Thickness(4.0),
				Padding = new Thickness(6.0),
				Cursor = Cursors.Hand,
				Tag = item
			};
			StackPanel stackPanel2 = new StackPanel
			{
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center
			};
			Path element3 = new Path
			{
				Data = Geometry.Parse(item.SvgData),
				Fill = fill,
				Width = 24.0,
				Height = 24.0,
				Stretch = Stretch.Uniform,
				HorizontalAlignment = HorizontalAlignment.Center,
				Margin = new Thickness(0.0, 0.0, 0.0, 4.0)
			};
			TextBlock element4 = new TextBlock
			{
				Text = item.Key,
				FontSize = 10.0,
				Foreground = foreground,
				TextTrimming = TextTrimming.CharacterEllipsis,
				MaxWidth = 72.0,
				HorizontalAlignment = HorizontalAlignment.Center,
				TextAlignment = TextAlignment.Center
			};
			stackPanel2.Children.Add(element3);
			stackPanel2.Children.Add(element4);
			card2.Child = stackPanel2;
			if (string.Equals(SelectedIconKey, item.Key, StringComparison.OrdinalIgnoreCase))
			{
				SelectCard(card2, item);
			}
			card2.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
			{
				SelectCard(card2, item);
				if (e.ClickCount == 2)
				{
					Confirm_Click(this, new RoutedEventArgs());
				}
			};
			IconsWrapPanel.Children.Add(card2);
		}
	}

	private void SelectCustomCard(Border card, IconHelper.CustomIconItem custom)
	{
		if (_selectedCard != null)
		{
			_selectedCard.Background = (Brush)FindResource("SubtleCardBrush");
			_selectedCard.BorderBrush = (Brush)FindResource("InputBorderBrush");
		}
		_selectedCard = card;
		SelectedIconKey = custom.Key;
		SelectedIconNameLabel.Text = custom.DisplayName + " (自定义)";
		card.Background = (Brush)FindResource("NavTabActiveBgBrush");
		card.BorderBrush = (Brush)FindResource("AccentPrimaryBrush");
	}

	private void ImportIcon_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			OpenFileDialog openFileDialog = new OpenFileDialog
			{
				Title = "导入自定义图标 (SVG / PNG / ICO / JPG)",
				Filter = "所有支持的图标 (*.svg;*.png;*.ico;*.jpg;*.jpeg;*.bmp;*.webp)|*.svg;*.png;*.ico;*.jpg;*.jpeg;*.bmp;*.webp|SVG 矢量图 (*.svg)|*.svg|图片文件 (*.png;*.ico;*.jpg;*.jpeg;*.bmp)|*.png;*.ico;*.jpg;*.jpeg;*.bmp|所有文件 (*.*)|*.*",
				Multiselect = false
			};
			if (openFileDialog.ShowDialog(this) == true && !string.IsNullOrEmpty(openFileDialog.FileName))
			{
				IconHelper.CustomIconItem customIconItem = IconHelper.ImportCustomIcon(openFileDialog.FileName);
				if (customIconItem != null)
				{
					SelectedIconKey = customIconItem.Key;
					PopulateIcons(SearchTextBox.Text.Trim());
				}
			}
		}
		catch (Exception ex)
		{
			AppMessageBox.Show("导入图标失败:\n" + ex.Message, "StarPie", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private void SelectCard(Border card, VectorIconItem item)
	{
		if (_selectedCard != null)
		{
			_selectedCard.Background = (Brush)FindResource("SubtleCardBrush");
			_selectedCard.BorderBrush = (Brush)FindResource("InputBorderBrush");
		}
		_selectedCard = card;
		SelectedIconKey = item.Key;
		SelectedIconNameLabel.Text = item.DisplayName;
		card.Background = (Brush)FindResource("NavTabActiveBgBrush");
		card.BorderBrush = (Brush)FindResource("AccentPrimaryBrush");
	}

	private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
	{
		PopulateIcons(SearchTextBox.Text.Trim());
	}

	private void ClearIcon_Click(object sender, RoutedEventArgs e)
	{
		SelectedIconKey = "";
		SelectedIconNameLabel.Text = "(无图标)";
		if (_selectedCard != null)
		{
			_selectedCard.Background = (Brush)FindResource("SubtleCardBrush");
			_selectedCard.BorderBrush = (Brush)FindResource("InputBorderBrush");
			_selectedCard = null;
		}
	}

	private void Confirm_Click(object sender, RoutedEventArgs e)
	{
		base.DialogResult = true;
		Close();
	}

	private void Cancel_Click(object sender, RoutedEventArgs e)
	{
		base.DialogResult = false;
		Close();
	}
}