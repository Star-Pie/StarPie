using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace WinPieGestures;

public partial class SettingsWindow
{
	public void RefreshFocusSubActionsListAndChips()
	{
		CancelListDrag();
		RefreshFocusSubActionsChips();
		RefreshFocusSubActionsOrderedList();
		UpdateActionBreadcrumb();
		UpdateUndoRedoButtonStates();
		UpdateClipboardButtonStates();
	}

	public void UpdateActionBreadcrumb()
	{
		if (FocusActionBreadcrumbText == null) return;

		WheelProfile? profile = GetSelectedProfile();
		if (profile == null)
		{
			FocusActionBreadcrumbText.Text = "";
			if (FocusRestoreInheritanceBtn != null) FocusRestoreInheritanceBtn.Visibility = Visibility.Collapsed;
			return;
		}

		bool isGlobal = string.Equals(profile.ProcessName, "Global", StringComparison.OrdinalIgnoreCase);
		bool inheritanceEnabled = ConfigManager.CurrentConfig?.EnableGlobalInheritance == true;
		bool showRestore = false;

		string[] directions = ResolveDirectionNames(profile.SectorCount);

		if (_selectedSlotIndex == -1)
		{
			FocusActionBreadcrumbText.Text = "📍 " + I18n.T("CenterCoreTitle");
			if (!isGlobal && inheritanceEnabled && profile.CenterAction != null && profile.CenterAction.IsInherited)
			{
				FocusActionBreadcrumbText.Text += " (" + I18n.T("SubActionInherited") + ")";
			}
			showRestore = !isGlobal && inheritanceEnabled && profile.CenterAction != null && !profile.CenterAction.IsInherited && WheelProfile.IsActionConfigured(profile.CenterAction);
		}
		else if (_selectedSlotIndex >= 0 && _selectedSlotIndex < profile.Actions.Count)
		{
			string dirName = (_selectedSlotIndex < directions.Length) ? directions[_selectedSlotIndex] : $"{_selectedSlotIndex + 1}";
			ActionItem primary = profile.Actions[_selectedSlotIndex];
			bool isPrimaryConfigured = WheelProfile.IsActionConfigured(primary) || !string.Equals(primary.Type, "Inherit", StringComparison.OrdinalIgnoreCase);

			if (_selectedSubActionIndex.HasValue)
			{
				int subIdx = _selectedSubActionIndex.Value;
				var subs = (primary.SubActions != null && primary.SubActions.Count > 0)
					? primary.SubActions
					: (!isPrimaryConfigured ? profile.GetEffectiveAction(_selectedSlotIndex)?.SubActions : null);

				string subName = (subs != null && subIdx >= 0 && subIdx < subs.Count) ? (subs[subIdx].Name ?? $"子动作 #{subIdx + 1}") : $"子动作 #{subIdx + 1}";

				FocusActionBreadcrumbText.Text = $"📍 [扇区 {_selectedSlotIndex + 1} - {dirName}] > [子动作 #{subIdx + 1}: {subName}]";
				if (!isPrimaryConfigured)
				{
					FocusActionBreadcrumbText.Text += $" ({I18n.T("SubActionInherited")})";
				}
				showRestore = !isGlobal && inheritanceEnabled && isPrimaryConfigured;
			}
			else
			{
				string slotName = string.IsNullOrEmpty(primary.Name) ? dirName : primary.Name;
				FocusActionBreadcrumbText.Text = $"📍 [扇区 {_selectedSlotIndex + 1} - {slotName}]";
				if (!isPrimaryConfigured)
				{
					FocusActionBreadcrumbText.Text += $" ({I18n.T("SubActionInherited")})";
				}
				showRestore = !isGlobal && inheritanceEnabled && isPrimaryConfigured;
			}
		}

		if (FocusRestoreInheritanceBtn != null)
		{
			FocusRestoreInheritanceBtn.Visibility = showRestore ? Visibility.Visible : Visibility.Collapsed;
			FocusRestoreInheritanceBtn.ToolTip = I18n.T("FocusRestoreInheritSectorConfirm");
		}
	}

	private void RefreshFocusSubActionsOrderedList()
	{
		if (FocusSubActionsOrderedListPanel == null) return;
		FocusSubActionsOrderedListPanel.Children.Clear();

		if (_selectedSlotIndex < 0) return;
		WheelProfile? profile = GetSelectedProfile();
		if (profile?.Actions == null || _selectedSlotIndex >= profile.Actions.Count) return;

		ActionItem primaryAction = profile.Actions[_selectedSlotIndex];
		bool isGlobalProfile = string.Equals(profile.ProcessName, "Global", StringComparison.OrdinalIgnoreCase);
		bool inheritanceEnabled = ConfigManager.CurrentConfig?.EnableGlobalInheritance == true;
		bool isInherited = false;
		ActionItem? effAction = null;

		if (!isGlobalProfile && inheritanceEnabled && !WheelProfile.IsActionConfigured(primaryAction))
		{
			effAction = profile.GetEffectiveAction(_selectedSlotIndex);
			if (effAction != null && effAction.IsInherited)
			{
				isInherited = true;
			}
		}

		var subActions = (primaryAction.SubActions != null && primaryAction.SubActions.Count > 0)
			? primaryAction.SubActions
			: (isInherited && effAction?.SubActions != null && effAction.SubActions.Count > 0 ? effAction.SubActions : primaryAction.SubActions);

		if (subActions == null || subActions.Count == 0) return;

		bool isFan = string.Equals(ConfigManager.CurrentConfig?.SubmenuStyle, "Fan", StringComparison.OrdinalIgnoreCase);
		int maxAllowed = isFan ? 3 : subActions.Count; // 外圈模式保留所有子动作

		for (int i = 0; i < subActions.Count; i++)
		{
			int subIdx = i;
			ActionItem subItem = subActions[i];
			bool isSelected = (_selectedSubActionIndex == subIdx);
			bool isExceeded = (isFan && i >= 3);
			bool isItemInherited = isInherited || subItem.IsInherited;

			Border itemBorder = new Border
			{
				Background = isSelected 
					? (Brush)FindResource("NavTabActiveBgBrush") 
					: (Brush)FindResource("CardBackgroundBrush"),
				BorderBrush = isSelected 
					? (Brush)FindResource("AccentPrimaryBrush") 
					: (Brush)FindResource("CardBorderBrush"),
				BorderThickness = new Thickness(isSelected ? 1.5 : 1.0),
				CornerRadius = new CornerRadius(6),
				Padding = new Thickness(8, 5, 8, 5),
				Margin = new Thickness(0, 0, 0, 4),
				Cursor = System.Windows.Input.Cursors.Hand,
				Opacity = isExceeded ? 0.6 : (isItemInherited ? 0.85 : 1.0)
			};

			Grid itemGrid = new Grid();
			itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 序号 Badge
			itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 图标
			itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 名称与参数摘要
			itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 状态标签 (激活/未激活/继承)
			itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 操作按钮 (上移/下移/删除)

			// 1. 编号 Badge
			Border badge = new Border
			{
				Background = isSelected ? (Brush)FindResource("AccentPrimaryBrush") : (Brush)FindResource("SubtleCardBrush"),
				CornerRadius = new CornerRadius(3),
				Padding = new Thickness(5, 1, 5, 1),
				Margin = new Thickness(0, 0, 8, 0),
				VerticalAlignment = VerticalAlignment.Center
			};
			TextBlock badgeText = new TextBlock
			{
				Text = $"#{subIdx + 1}",
				FontSize = 10,
				FontWeight = FontWeights.Bold,
				Foreground = isSelected ? (Brush)FindResource("AccentTextBrush") : (Brush)FindResource("TextSecondaryBrush")
			};
			badge.Child = badgeText;
			Grid.SetColumn(badge, 0);
			itemGrid.Children.Add(badge);

			// 2. 图标
			string iconSvg = !string.IsNullOrEmpty(subItem.CustomIconSvg)
				? subItem.CustomIconSvg
				: IconHelper.GetSvgPathByKey(subItem.IconKey);
			if (!string.IsNullOrEmpty(iconSvg))
			{
				try
				{
					Path iconPath = new Path
					{
						Data = Geometry.Parse(iconSvg),
						Fill = (Brush)FindResource("AccentPrimaryBrush"),
						Width = 14,
						Height = 14,
						Stretch = Stretch.Uniform,
						Margin = new Thickness(0, 0, 8, 0),
						VerticalAlignment = VerticalAlignment.Center
					};
					Grid.SetColumn(iconPath, 1);
					itemGrid.Children.Add(iconPath);
				}
				catch { }
			}

			// 3. 名称与参数摘要
			StackPanel textPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
			string nameText = string.IsNullOrEmpty(subItem.Name) ? $"子动作 #{subIdx + 1}" : subItem.Name;
			TextBlock nameBlock = new TextBlock
			{
				Text = nameText,
				FontSize = 11,
				FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal,
				Foreground = isSelected ? (Brush)FindResource("AccentPrimaryBrush") : (Brush)FindResource("TextPrimaryBrush")
			};
			textPanel.Children.Add(nameBlock);

			string paramSummary = GetActionParamSummary(subItem);
			if (!string.IsNullOrEmpty(paramSummary))
			{
				TextBlock summaryBlock = new TextBlock
				{
					Text = paramSummary,
					FontSize = 9.5,
					Foreground = (Brush)FindResource("TextMutedBrush"),
					TextTrimming = TextTrimming.CharacterEllipsis,
					Margin = new Thickness(0, 1, 0, 0)
				};
				textPanel.Children.Add(summaryBlock);
			}
			Grid.SetColumn(textPanel, 2);
			itemGrid.Children.Add(textPanel);

			// 4. 状态标签
			if (isExceeded)
			{
				Border inactiveBadge = new Border
				{
					Background = new SolidColorBrush(Color.FromArgb(30, 239, 68, 68)),
					BorderBrush = new SolidColorBrush(Color.FromArgb(100, 239, 68, 68)),
					BorderThickness = new Thickness(1),
					CornerRadius = new CornerRadius(3),
					Padding = new Thickness(4, 1, 4, 1),
					Margin = new Thickness(6, 0, 6, 0),
					VerticalAlignment = VerticalAlignment.Center
				};
				TextBlock inactiveText = new TextBlock
				{
					Text = I18n.T("SubActionInactive"),
					FontSize = 9.5,
					Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68))
				};
				inactiveBadge.Child = inactiveText;
				Grid.SetColumn(inactiveBadge, 3);
				itemGrid.Children.Add(inactiveBadge);
			}
			else if (isItemInherited)
			{
				Border inheritBadge = new Border
				{
					Background = new SolidColorBrush(Color.FromArgb(30, 56, 189, 248)),
					BorderBrush = new SolidColorBrush(Color.FromArgb(100, 56, 189, 248)),
					BorderThickness = new Thickness(1),
					CornerRadius = new CornerRadius(3),
					Padding = new Thickness(4, 1, 4, 1),
					Margin = new Thickness(6, 0, 6, 0),
					VerticalAlignment = VerticalAlignment.Center
				};
				TextBlock inheritText = new TextBlock
				{
					Text = I18n.T("SubActionInherited"),
					FontSize = 9.5,
					Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248))
				};
				inheritBadge.Child = inheritText;
				Grid.SetColumn(inheritBadge, 3);
				itemGrid.Children.Add(inheritBadge);
			}

			// 5. 操作按钮 (上移/下移/删除)
			if (!isItemInherited)
			{
				StackPanel actionsPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

				if (subIdx > 0)
				{
					Button upBtn = new Button
					{
						Content = "▲",
						Style = (Style)FindResource("ModernButtonStyle"),
						Height = 20,
						Width = 20,
						Padding = new Thickness(0),
						Margin = new Thickness(2, 0, 2, 0),
						FontSize = 9,
						ToolTip = I18n.T("MoveSubActionUp")
					};
					upBtn.Click += (s, e) =>
					{
						e.Handled = true;
						MoveSubAction(profile, _selectedSlotIndex, subIdx, subIdx - 1);
					};
					actionsPanel.Children.Add(upBtn);
				}

				if (subIdx < subActions.Count - 1)
				{
					Button downBtn = new Button
					{
						Content = "▼",
						Style = (Style)FindResource("ModernButtonStyle"),
						Height = 20,
						Width = 20,
						Padding = new Thickness(0),
						Margin = new Thickness(2, 0, 2, 0),
						FontSize = 9,
						ToolTip = I18n.T("MoveSubActionDown")
					};
					downBtn.Click += (s, e) =>
					{
						e.Handled = true;
						MoveSubAction(profile, _selectedSlotIndex, subIdx, subIdx + 1);
					};
					actionsPanel.Children.Add(downBtn);
				}

				Button delBtn = new Button
				{
					Content = "✕",
					Style = (Style)FindResource("ModernButtonStyle"),
					Height = 20,
					Width = 20,
					Padding = new Thickness(0),
					Margin = new Thickness(2, 0, 0, 0),
					FontSize = 9,
					Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68)),
					ToolTip = I18n.T("DeleteSubAction")
				};
				delBtn.Click += (s, e) =>
				{
					e.Handled = true;
					DeleteSubAction(profile, _selectedSlotIndex, subIdx);
				};
				actionsPanel.Children.Add(delBtn);

				Grid.SetColumn(actionsPanel, 4);
				itemGrid.Children.Add(actionsPanel);
			}

			itemBorder.Child = itemGrid;

			ContextMenu rowMenu = new ContextMenu();
			MenuItem copyRowItem = new MenuItem { Header = "📋 " + I18n.T("FocusCopyActionBtnText") };
			copyRowItem.Click += (s, e) =>
			{
				ActionClipboard.CopySingleAction(subItem, profile.ProcessName, profile.ActiveLayerIndex, _selectedSlotIndex);
				NotifyClipboardCopied(subItem.Name ?? $"子动作 #{subIdx + 1}");
				UpdateClipboardButtonStates();
			};
			rowMenu.Items.Add(copyRowItem);

			if (!isItemInherited)
			{
				MenuItem upRowItem = new MenuItem { Header = "▲ " + I18n.T("MoveSubActionUp"), IsEnabled = subIdx > 0 };
				upRowItem.Click += (s, e) => MoveSubAction(profile, _selectedSlotIndex, subIdx, subIdx - 1);
				rowMenu.Items.Add(upRowItem);

				MenuItem downRowItem = new MenuItem { Header = "▼ " + I18n.T("MoveSubActionDown"), IsEnabled = subIdx < subActions.Count - 1 };
				downRowItem.Click += (s, e) => MoveSubAction(profile, _selectedSlotIndex, subIdx, subIdx + 1);
				rowMenu.Items.Add(downRowItem);

				MenuItem delRowItem = new MenuItem { Header = "✕ " + I18n.T("DeleteSubAction") };
				delRowItem.Click += (s, e) => DeleteSubAction(profile, _selectedSlotIndex, subIdx);
				rowMenu.Items.Add(delRowItem);
			}

			itemBorder.ContextMenu = rowMenu;

			if (!isItemInherited)
			{
				itemBorder.PreviewMouseLeftButtonDown += (s, e) =>
				{
					if (e.OriginalSource is DependencyObject dep && FindVisualParent<Button>(dep) != null)
					{
						return;
					}
					BeginListDrag(profile, _selectedSlotIndex, subIdx, e.GetPosition(FocusSubActionsOrderedListPanel));
				};

				itemBorder.PreviewMouseLeftButtonUp += (s, e) =>
				{
					if (!_isDraggingListItem && _listDragSourceIndex == subIdx)
					{
						_listDragSourceIndex = -1;
						_listDragStartPos = null;
						SelectSubAction(_selectedSlotIndex, subIdx);
					}
				};
			}
			else
			{
				itemBorder.MouseLeftButtonDown += (s, e) =>
				{
					e.Handled = true;
					SelectSubAction(_selectedSlotIndex, subIdx);
				};
			}

			FocusSubActionsOrderedListPanel.Children.Add(itemBorder);
		}

		if (!_isListPanelHandlersAttached && FocusSubActionsOrderedListPanel != null)
		{
			_isListPanelHandlersAttached = true;
			FocusSubActionsOrderedListPanel.PreviewMouseMove += FocusSubActionsOrderedListPanel_PreviewMouseMove;
			FocusSubActionsOrderedListPanel.PreviewMouseLeftButtonUp += FocusSubActionsOrderedListPanel_PreviewMouseLeftButtonUp;
			FocusSubActionsOrderedListPanel.LostMouseCapture += FocusSubActionsOrderedListPanel_LostMouseCapture;
		}
	}

	private int _listDragSourceIndex = -1;
	private Point? _listDragStartPos = null;
	private bool _isDraggingListItem = false;
	private int _listDropTargetIndex = -1;
	private bool _isListPanelHandlersAttached = false;
	private Border? _listInsertionIndicator;
	private WheelProfile? _listDragProfile;
	private WheelLayer? _listDragLayer;
	private int _listDragParentSlotIndex = -1;

	private void BeginListDrag(WheelProfile profile, int slotIndex, int subIndex, Point position)
	{
		CancelListDrag();
		_listDragProfile = profile;
		_listDragLayer = profile.GetActiveLayer();
		_listDragParentSlotIndex = slotIndex;
		_listDragSourceIndex = subIndex;
		_listDragStartPos = position;
	}

	private void FocusSubActionsOrderedListPanel_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
	{
		if (FocusSubActionsOrderedListPanel == null) return;

		if (_listDragStartPos != null && !_isDraggingListItem && e.LeftButton == System.Windows.Input.MouseButtonState.Pressed && _listDragSourceIndex >= 0)
		{
			Point cur = e.GetPosition(FocusSubActionsOrderedListPanel);
			if (Math.Abs(cur.Y - _listDragStartPos.Value.Y) > 5)
			{
				_isDraggingListItem = true;
				FocusSubActionsOrderedListPanel.CaptureMouse();
				ShowListInsertionIndicator();
			}
		}

		if (_isDraggingListItem && _listInsertionIndicator != null)
		{
			Point cur = e.GetPosition(FocusSubActionsOrderedListPanel);
			UpdateListInsertionIndicator(cur);
		}
	}

	private void FocusSubActionsOrderedListPanel_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
	{
		if (!_isDraggingListItem) return;
		if (FocusSubActionsOrderedListPanel != null)
		{
			UpdateListInsertionIndicator(e.GetPosition(FocusSubActionsOrderedListPanel));
		}
		e.Handled = true;
		CompleteListDrag(true);
	}

	private void CompleteListDrag(bool commit)
	{
		WheelProfile? profile = _listDragProfile;
		WheelLayer? layer = _listDragLayer;
		int parentSlot = _listDragParentSlotIndex;
		int fromIdx = _listDragSourceIndex;
		int gapIndex = _listDropTargetIndex;
		bool wasDragging = _isDraggingListItem;
		// Clear before releasing capture: normal release also raises LostMouseCapture.
		CancelListDrag();
		if (!commit || !wasDragging || profile == null ||
			!ReferenceEquals(profile, GetSelectedProfile()) || _selectedSlotIndex != parentSlot ||
			!ReferenceEquals(layer, profile.GetActiveLayer()) || parentSlot < 0 || parentSlot >= profile.Actions.Count)
		{
			return;
		}
		int count = profile.Actions[parentSlot].SubActions?.Count ?? 0;
		if (fromIdx < 0 || fromIdx >= count || gapIndex < 0 || gapIndex > count) return;
		int toIdx = gapIndex > fromIdx ? gapIndex - 1 : gapIndex;
		if (fromIdx != toIdx) MoveSubAction(profile, parentSlot, fromIdx, toIdx);
	}

	private void FocusSubActionsOrderedListPanel_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
	{
		CancelListDrag();
	}

	public void CancelListDrag()
	{
		_isDraggingListItem = false;
		_listDragSourceIndex = -1;
		_listDropTargetIndex = -1;
		_listDragStartPos = null;
		_listDragProfile = null;
		_listDragLayer = null;
		_listDragParentSlotIndex = -1;
		HideListInsertionIndicator();
		if (FocusSubActionsOrderedListPanel?.IsMouseCaptured == true)
		{
			FocusSubActionsOrderedListPanel.ReleaseMouseCapture();
		}
	}

	private void EnsureListInsertionIndicator()
	{
		if (_listInsertionIndicator == null)
		{
			_listInsertionIndicator = new Border
			{
				Height = 3,
				Background = (Brush)FindResource("AccentPrimaryBrush"),
				CornerRadius = new CornerRadius(1.5),
				Margin = new Thickness(4, 2, 4, 2),
				Visibility = Visibility.Collapsed
			};
		}
	}

	private void ShowListInsertionIndicator()
	{
		EnsureListInsertionIndicator();
		if (_listInsertionIndicator != null && FocusSubActionsOrderedListPanel != null)
		{
			if (!FocusSubActionsOrderedListPanel.Children.Contains(_listInsertionIndicator))
			{
				FocusSubActionsOrderedListPanel.Children.Add(_listInsertionIndicator);
			}
			_listInsertionIndicator.Visibility = Visibility.Visible;
		}
	}

	private void HideListInsertionIndicator()
	{
		if (_listInsertionIndicator != null && FocusSubActionsOrderedListPanel != null)
		{
			_listInsertionIndicator.Visibility = Visibility.Collapsed;
			if (FocusSubActionsOrderedListPanel.Children.Contains(_listInsertionIndicator))
			{
				FocusSubActionsOrderedListPanel.Children.Remove(_listInsertionIndicator);
			}
		}
	}

	private void UpdateListInsertionIndicator(Point cur)
	{
		if (FocusSubActionsOrderedListPanel == null) return;
		if (!new Rect(0, 0, FocusSubActionsOrderedListPanel.ActualWidth, FocusSubActionsOrderedListPanel.ActualHeight).Contains(cur))
		{
			_listDropTargetIndex = -1;
			HideListInsertionIndicator();
			return;
		}

		var itemBorders = FocusSubActionsOrderedListPanel.Children.OfType<Border>()
			.Where(b => !ReferenceEquals(b, _listInsertionIndicator)).ToList();
		if (itemBorders.Count == 0)
		{
			_listDropTargetIndex = -1;
			HideListInsertionIndicator();
			return;
		}

		int bestIndex = itemBorders.Count;
		for (int i = 0; i < itemBorders.Count; i++)
		{
			Border b = itemBorders[i];
			GeneralTransform transform = b.TransformToAncestor(FocusSubActionsOrderedListPanel);
			Point pt = transform.Transform(new Point(0, 0));
			double midY = pt.Y + b.ActualHeight / 2.0;
			if (cur.Y < midY)
			{
				bestIndex = i;
				break;
			}
		}

		_listDropTargetIndex = bestIndex;
		ShowListInsertionIndicator();

		if (FocusSubActionsOrderedListPanel.Children.Contains(_listInsertionIndicator))
		{
			FocusSubActionsOrderedListPanel.Children.Remove(_listInsertionIndicator);
		}
		int insertVisualIdx = Math.Min(_listDropTargetIndex, FocusSubActionsOrderedListPanel.Children.Count);
		FocusSubActionsOrderedListPanel.Children.Insert(insertVisualIdx, _listInsertionIndicator!);
	}

	private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
	{
		while (child != null)
		{
			if (child is T parent) return parent;
			child = VisualTreeHelper.GetParent(child);
		}
		return null;
	}

	private string GetActionParamSummary(ActionItem item)
	{
		if (item == null) return "";
		string type = item.Type ?? "";
		string param = item.Parameter ?? "";
		return type switch
		{
			"Hotkey" => $"快捷键: {param}",
			"Launch" => $"启动: {System.IO.Path.GetFileName(param)}",
			"Folder" => $"文件夹: {param}",
			"Url" or "WebUrl" => $"网址: {param}",
			"Command" => $"命令: {param}",
			"System" => $"系统: {param}",
			"WindowTiler" => $"分屏: {param}",
			"Plugin" => $"插件: {item.PluginActionRef?.FullId ?? param}",
			_ => string.IsNullOrEmpty(param) ? type : $"{type}: {param}"
		};
	}

	public void MoveSubAction(WheelProfile profile, int slotIndex, int fromIdx, int toIdx)
	{
		if (profile?.Actions == null || slotIndex < 0 || slotIndex >= profile.Actions.Count) return;
		if (fromIdx == toIdx) return;

		ActionItem? effectivePrimary = profile.GetEffectiveAction(slotIndex);
		int count = (profile.Actions[slotIndex].SubActions != null && profile.Actions[slotIndex].SubActions.Count > 0)
			? profile.Actions[slotIndex].SubActions.Count
			: (effectivePrimary?.SubActions?.Count ?? 0);

		if (fromIdx < 0 || fromIdx >= count || toIdx < 0 || toIdx >= count) return;

		var before = CaptureSectorSnapshot(profile, slotIndex);
		EnsureLocalSlotForEdit(profile, slotIndex);
		ActionItem primary = profile.Actions[slotIndex];
		primary.SubActions ??= new List<ActionItem>();
		if (fromIdx >= primary.SubActions.Count || toIdx >= primary.SubActions.Count) return;

		ActionItem item = primary.SubActions[fromIdx];
		primary.SubActions.RemoveAt(fromIdx);
		primary.SubActions.Insert(toIdx, item);

		_selectedSubActionIndex = toIdx;

		var after = CaptureSectorSnapshot(profile, slotIndex);
		RecordSingleSectorTransaction(profile, slotIndex, "调整二级子动作顺序", before, after);

		UpdateFocusEditorUi();
		RefreshFocusSubActionsListAndChips();
		RefreshSlots();
		RenderMappingsWheelPreview();
		ScheduleAutoSave();
	}

	public void DeleteSubAction(WheelProfile profile, int slotIndex, int subIdx)
	{
		if (profile?.Actions == null || slotIndex < 0 || slotIndex >= profile.Actions.Count) return;

		ActionItem? effectivePrimary = profile.GetEffectiveAction(slotIndex);
		int count = (profile.Actions[slotIndex].SubActions != null && profile.Actions[slotIndex].SubActions.Count > 0)
			? profile.Actions[slotIndex].SubActions.Count
			: (effectivePrimary?.SubActions?.Count ?? 0);

		if (subIdx < 0 || subIdx >= count) return;

		var before = CaptureSectorSnapshot(profile, slotIndex);
		EnsureLocalSlotForEdit(profile, slotIndex);
		ActionItem primary = profile.Actions[slotIndex];
		if (primary.SubActions == null || subIdx >= primary.SubActions.Count) return;

		primary.SubActions.RemoveAt(subIdx);

		if (_selectedSubActionIndex == subIdx)
		{
			_selectedSubActionIndex = null;
		}
		else if (_selectedSubActionIndex > subIdx)
		{
			_selectedSubActionIndex--;
		}

		var after = CaptureSectorSnapshot(profile, slotIndex);
		RecordSingleSectorTransaction(profile, slotIndex, "删除二级动作", before, after);

		UpdateFocusEditorUi();
		RefreshFocusSubActionsListAndChips();
		RefreshSlots();
		RenderMappingsWheelPreview();
		ScheduleAutoSave();
	}

	private void FocusRestoreInheritanceBtn_Click(object sender, RoutedEventArgs e)
	{
		WheelProfile? profile = GetSelectedProfile();
		if (profile == null) return;
		if (string.Equals(profile.ProcessName, "Global", StringComparison.OrdinalIgnoreCase)) return;

		if (_selectedSlotIndex >= 0)
		{
			// 统一为恢复整个扇区继承契约
			var msg = I18n.T("FocusRestoreInheritSectorConfirm");
			if (AppMessageBox.Show(this, msg, "StarPie", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
			{
				RestoreSectorInheritance(profile, _selectedSlotIndex);
			}
		}
		else if (_selectedSlotIndex == -1)
		{
			// 恢复中心核心继承
			RestoreCenterInheritance(profile);
		}
	}

	public void RestoreSectorInheritance(WheelProfile profile, int slotIndex)
	{
		if (profile.Actions == null || slotIndex < 0 || slotIndex >= profile.Actions.Count) return;

		var before = CaptureSectorSnapshot(profile, slotIndex);

		// 将本地扇区重置为空/继承占位
		profile.Actions[slotIndex] = new ActionItem
		{
			Name = "",
			Type = "Inherit",
			Parameter = "",
			SubActions = null,
			IsInherited = true
		};

		var after = CaptureSectorSnapshot(profile, slotIndex);
		RecordSingleSectorTransaction(profile, slotIndex, "恢复扇区继承全局", before, after);

		_selectedSubActionIndex = null;
		UpdateFocusEditorUi();
		RefreshFocusSubActionsListAndChips();
		RefreshSlots();
		RenderMappingsWheelPreview();
		ScheduleAutoSave();
	}

	public void RestoreSubActionInheritance(WheelProfile profile, int slotIndex, int subIdx)
	{
		// V1 契约：不新增单叶片动态继承，统一恢复整个扇区继承契约
		RestoreSectorInheritance(profile, slotIndex);
	}

	public void RestoreCenterInheritance(WheelProfile profile)
	{
		var before = CaptureCenterSnapshot(profile);

		profile.CenterAction = new ActionItem
		{
			Name = "",
			Type = "Inherit",
			Parameter = "",
			IsInherited = true
		};
		profile.SyncActiveLayerFromRootProperties();

		var after = CaptureCenterSnapshot(profile);
		RecordCenterTransaction(profile, "恢复中心核心继承全局", before, after);

		UpdateFocusEditorUi();
		RefreshSlots();
		RenderMappingsWheelPreview();
		ScheduleAutoSave();
	}

	private void FocusCopyActionBtn_Click(object sender, RoutedEventArgs e)
	{
		CopyCurrentSelection();
	}

	private void FocusCopyCurrentAction_Click(object sender, RoutedEventArgs e)
	{
		CopyCurrentAction();
	}

	private void FocusCopyEntireSector_Click(object sender, RoutedEventArgs e)
	{
		CopyEntireSector();
	}

	private void FocusPasteActionBtn_Click(object sender, RoutedEventArgs e)
	{
		PasteToCurrentSelection();
	}

	private void FocusUndoSubActionsBtn_Click(object sender, RoutedEventArgs e)
	{
		ExecuteUndo();
	}

	private void FocusRedoSubActionsBtn_Click(object sender, RoutedEventArgs e)
	{
		ExecuteRedo();
	}
}
