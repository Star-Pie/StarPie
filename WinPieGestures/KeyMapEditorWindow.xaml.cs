using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseWheelEventArgs = System.Windows.Input.MouseWheelEventArgs;
using Keyboard = System.Windows.Input.Keyboard;
using MouseButton = System.Windows.Input.MouseButton;
using StarPie.Plugin;

namespace WinPieGestures;

/// <summary>
/// 键盘空间重映射交互式可视化编辑器窗口。
/// 支持单键录制控件、固定布局与动态按键自适应扩展、画布缩放平移、单/多关联关系高亮连线及严格校验。
/// </summary>
public partial class KeyMapEditorWindow : Window
{
	private static readonly HashSet<string> FixedPhysicalKeys = new(StringComparer.OrdinalIgnoreCase)
	{
		"Q", "W", "E", "R", "A", "S", "D", "Z", "X", "C"
	};

	private static readonly HashSet<string> FixedTargetKeys = new(StringComparer.OrdinalIgnoreCase)
	{
		"Num0", "Num1", "Num2", "Num3", "Num4", "Num5", "Num6", "Num7", "Num8", "Num9"
	};

	private readonly ObservableCollection<KeyboardRemapEntry> _entries = new();
	private readonly Dictionary<string, Button> _physicalButtons = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, Button> _targetButtons = new(StringComparer.OrdinalIgnoreCase);

	private List<KeyboardRemapEntry> _activeRelationships = new();
	private string? _activeSingleSource;
	private string? _activeSingleTarget;
	private bool _isUpdatingSelectionFromDiagram;
	private bool _isDiagramInspectionOnly;
	private string? _pendingPairSourceKey;

	private bool _isPanning;
	private Point _panStartPoint;
	private Point _panStartTranslate;
	private bool _hasUserInteractedWithCanvas;

	public static readonly DependencyProperty DeleteButtonLabelProperty = DependencyProperty.Register(
		nameof(DeleteButtonLabel), typeof(string), typeof(KeyMapEditorWindow), new PropertyMetadata("删除"));

	public string DeleteButtonLabel
	{
		get => (string)GetValue(DeleteButtonLabelProperty);
		set => SetValue(DeleteButtonLabelProperty, value);
	}

	public string ResultSerializedKeyMap { get; private set; } = "";
	public string ResultKeyMap => ResultSerializedKeyMap;

	private KeyboardRemapEntry? _editingEntry;
	public KeyboardRemapEntry? EditingEntry => _editingEntry;

	public ObservableCollection<KeyboardRemapEntry> Entries => _entries;
	public SingleKeyRecorderBox SourceRecorder => SourceKeyRecorder;
	public SingleKeyRecorderBox TargetRecorder => TargetKeyRecorder;
	public TextBlock StatusText => StatusTextBlock;
	public Button AddBtn => AddButton;
	public TextBlock ListEditHintText => ListEditHintTextBlock;

	public double CurrentZoom => DiagramScaleTransform.ScaleX;
	public IReadOnlyList<KeyboardRemapEntry> ActiveDiagramRelationships => _activeRelationships;
	public IReadOnlyDictionary<string, Button> PhysicalButtons => _physicalButtons;
	public IReadOnlyDictionary<string, Button> TargetButtons => _targetButtons;
	public WrapPanel OtherSources => OtherSourcesPanel;
	public WrapPanel OtherTargets => OtherTargetsPanel;
	public Border Viewport => DiagramViewport;
	public Canvas CanvasRoot => DiagramCanvasRoot;
	public Grid LayoutGrid => DiagramLayoutGrid;
	public bool IsDiagramInspectionOnly => _isDiagramInspectionOnly;
	public string? PendingPairSourceKey => _pendingPairSourceKey;
	public double TranslateX => DiagramTranslateTransform.X;
	public double TranslateY => DiagramTranslateTransform.Y;
	public TextBlock MappingHintTextBlock => SelectedMappingHint;
	public int ActiveConnectionLineCount => ConnectionCanvas.Children.OfType<System.Windows.Shapes.Path>().Count(p => p.Stroke != null);

	public KeyMapEditorWindow(string? initialSerializedKeyMap)
	{
		InitializeComponent();
		AppThemeManager.ApplyTheme(this, AppThemeManager.CurrentEffectiveTheme);
		IndexVisualKeyButtons();
		HookRecorderEvents();
		ApplyLocalization();

		// 严格遵循：若初始串为空/空白，保持完全清空状态，绝不隐式自动填充默认预设
		if (!string.IsNullOrWhiteSpace(initialSerializedKeyMap))
		{
			if (KeyMapCodec.TryDecode(initialSerializedKeyMap, out var decoded, out _) && decoded != null)
			{
				foreach (var entry in decoded)
				{
					_entries.Add(new KeyboardRemapEntry
					{
						FromKey = KeyMapCodec.NormalizeKeyName(entry.FromKey),
						ToKey = KeyMapCodec.NormalizeKeyName(entry.ToKey)
					});
				}
			}
		}

		MappingsDataGrid.ItemsSource = _entries;
		SyncDynamicVisualKeys();
		RefreshVisualHighlights();

		Loaded += (s, e) =>
		{
			FitCanvas();
			UpdateDiagramConnections();
			Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
			{
				if (!_hasUserInteractedWithCanvas)
				{
					FitCanvas();
				}
			}));
		};

		ContentRendered += (s, e) =>
		{
			if (!_hasUserInteractedWithCanvas)
			{
				FitCanvas();
			}
		};
	}

	private void HookRecorderEvents()
	{
		SourceKeyRecorder.KeySelected += (s, sourceKey) =>
		{
			if (_isUpdatingSelectionFromDiagram)
			{
				return;
			}
			_pendingPairSourceKey = null;
			if (!string.IsNullOrEmpty(sourceKey))
			{
				_isDiagramInspectionOnly = false;
			}
			StatusTextBlock.Text = "";
			if (string.IsNullOrEmpty(sourceKey))
			{
				_editingEntry = null;
				MappingsDataGrid.SelectedItem = null;
				ClearActiveRelationships();
				UpdateAddButtonState();
				return;
			}

			var existing = _entries.FirstOrDefault(m => string.Equals(m.FromKey, sourceKey, StringComparison.OrdinalIgnoreCase));
			if (existing != null && existing != _editingEntry)
			{
				_editingEntry = existing;
				TargetKeyRecorder.SetKey(existing.ToKey);
				_isUpdatingSelectionFromDiagram = true;
				try
				{
					MappingsDataGrid.SelectedItem = existing;
					MappingsDataGrid.ScrollIntoView(existing);
				}
				finally
				{
					_isUpdatingSelectionFromDiagram = false;
				}
				SelectedMappingHint.Text = $"{KeyMapCodec.GetFriendlyKeyDisplayName(existing.FromKey)} ➔ {KeyMapCodec.GetFriendlyKeyDisplayName(existing.ToKey)}";
				SetActiveRelationships(new[] { existing });
			}
			else if (_editingEntry != null && !string.IsNullOrEmpty(TargetKeyRecorder.SelectedKey))
			{
				SelectedMappingHint.Text = $"{KeyMapCodec.GetFriendlyKeyDisplayName(sourceKey)} ➔ {KeyMapCodec.GetFriendlyKeyDisplayName(TargetKeyRecorder.SelectedKey)}";
				SetActiveRelationships(new[] { new KeyboardRemapEntry { FromKey = sourceKey, ToKey = TargetKeyRecorder.SelectedKey } });
			}
			else
			{
				TargetKeyRecorder.ClearKey();
				SetActiveSingleSource(sourceKey);
				Dispatcher.BeginInvoke(new Action(() =>
				{
					TargetKeyRecorder.Focus();
					TargetKeyRecorder.StartRecording();
				}));
			}
			UpdateAddButtonState();
		};

		TargetKeyRecorder.KeySelected += (s, targetKey) =>
		{
			if (_isUpdatingSelectionFromDiagram)
			{
				return;
			}
			_pendingPairSourceKey = null;
			if (!string.IsNullOrEmpty(targetKey))
			{
				_isDiagramInspectionOnly = false;
			}
			StatusTextBlock.Text = "";
			if (!string.IsNullOrEmpty(SourceKeyRecorder.SelectedKey) && !string.IsNullOrEmpty(targetKey))
			{
				SelectedMappingHint.Text = $"{KeyMapCodec.GetFriendlyKeyDisplayName(SourceKeyRecorder.SelectedKey)} ➔ {KeyMapCodec.GetFriendlyKeyDisplayName(targetKey)}";
				SetActiveRelationships(new[] { new KeyboardRemapEntry { FromKey = SourceKeyRecorder.SelectedKey, ToKey = targetKey } });
			}
			else if (!string.IsNullOrEmpty(targetKey))
			{
				SetActiveSingleTarget(targetKey);
			}
		};

		SourceKeyRecorder.RecordingFailed += (s, err) =>
		{
			StatusTextBlock.Text = err;
		};

		TargetKeyRecorder.RecordingFailed += (s, err) =>
		{
			StatusTextBlock.Text = err;
		};

		SourceKeyRecorder.RecordingStarted += (s, e) =>
		{
			_pendingPairSourceKey = null;
			_isDiagramInspectionOnly = false;
			StatusTextBlock.Text = "";
		};

		TargetKeyRecorder.RecordingStarted += (s, e) =>
		{
			_pendingPairSourceKey = null;
			_isDiagramInspectionOnly = false;
			StatusTextBlock.Text = "";
		};
	}

	private void IndexVisualKeyButtons()
	{
		_physicalButtons["Q"] = Key_Q;
		_physicalButtons["W"] = Key_W;
		_physicalButtons["E"] = Key_E;
		_physicalButtons["R"] = Key_R;
		_physicalButtons["A"] = Key_A;
		_physicalButtons["S"] = Key_S;
		_physicalButtons["D"] = Key_D;
		_physicalButtons["Z"] = Key_Z;
		_physicalButtons["X"] = Key_X;
		_physicalButtons["C"] = Key_C;

		_targetButtons["Num7"] = Key_Num7;
		_targetButtons["Num8"] = Key_Num8;
		_targetButtons["Num9"] = Key_Num9;
		_targetButtons["Num4"] = Key_Num4;
		_targetButtons["Num5"] = Key_Num5;
		_targetButtons["Num6"] = Key_Num6;
		_targetButtons["Num1"] = Key_Num1;
		_targetButtons["Num2"] = Key_Num2;
		_targetButtons["Num3"] = Key_Num3;
		_targetButtons["Num0"] = Key_Num0;
	}

	public void SyncDynamicVisualKeys()
	{
		var requiredSources = _entries
			.Select(e => KeyMapCodec.NormalizeKeyName(e.FromKey))
			.Where(k => !FixedPhysicalKeys.Contains(k))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(k => k)
			.ToList();

		var requiredTargets = _entries
			.Select(e => KeyMapCodec.NormalizeKeyName(e.ToKey))
			.Where(k => !FixedTargetKeys.Contains(k))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(k => k)
			.ToList();

		// 清理已被移除的动态源按键
		var dynamicSourcesToRemove = _physicalButtons.Keys
			.Where(k => !FixedPhysicalKeys.Contains(k) && !requiredSources.Contains(k, StringComparer.OrdinalIgnoreCase))
			.ToList();
		foreach (var k in dynamicSourcesToRemove)
		{
			_physicalButtons.Remove(k);
		}

		// 清理已被移除的动态目标按键
		var dynamicTargetsToRemove = _targetButtons.Keys
			.Where(k => !FixedTargetKeys.Contains(k) && !requiredTargets.Contains(k, StringComparer.OrdinalIgnoreCase))
			.ToList();
		foreach (var k in dynamicTargetsToRemove)
		{
			_targetButtons.Remove(k);
		}

		// 重建 OtherSourcesPanel
		OtherSourcesPanel.Children.Clear();
		foreach (var srcKey in requiredSources)
		{
			var btn = CreateDynamicKeyButton(srcKey, isSource: true);
			_physicalButtons[srcKey] = btn;
			OtherSourcesPanel.Children.Add(btn);
		}
		OtherSourcesContainer.Visibility = requiredSources.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

		// 重建 OtherTargetsPanel
		OtherTargetsPanel.Children.Clear();
		foreach (var tgtKey in requiredTargets)
		{
			var btn = CreateDynamicKeyButton(tgtKey, isSource: false);
			_targetButtons[tgtKey] = btn;
			OtherTargetsPanel.Children.Add(btn);
		}
		OtherTargetsContainer.Visibility = requiredTargets.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
	}

	private Button CreateDynamicKeyButton(string canonicalKey, bool isSource)
	{
		string friendly = KeyMapCodec.GetFriendlyKeyDisplayName(canonicalKey);
		var btn = new Button
		{
			Content = friendly,
			Tag = canonicalKey,
			ToolTip = canonicalKey,
			Style = TryFindResource("VisualKeyStyle") as Style,
			Height = 42,
			Margin = new Thickness(3)
		};

		if (friendly.Length > 2)
		{
			btn.Width = double.NaN;
			btn.Padding = new Thickness(8, 0, 8, 0);
			btn.MinWidth = 46;
		}
		else
		{
			btn.Width = 46;
		}

		if (isSource)
		{
			btn.Click += VisualKey_Click;
		}
		else
		{
			btn.Click += VisualTarget_Click;
		}

		return btn;
	}

	private void ApplyLocalization()
	{
		Title = I18n.T("KeyMapEditorTitle");
		TitleTextBlock.Text = I18n.T("KeyMapEditorTitle");
		SubtitleTextBlock.Text = I18n.T("KeyMapEditorSubtitle");
		PhysicalHeader.Text = I18n.T("KeyMapEditorPhysicalHeader");
		TargetHeader.Text = I18n.T("KeyMapEditorTargetHeader");
		if (!string.IsNullOrEmpty(_pendingPairSourceKey))
		{
			var existing = _entries.FirstOrDefault(m => string.Equals(m.FromKey, _pendingPairSourceKey, StringComparison.OrdinalIgnoreCase));
			string fromFriendly = KeyMapCodec.GetFriendlyKeyDisplayName(_pendingPairSourceKey);
			if (existing != null)
			{
				string toFriendly = KeyMapCodec.GetFriendlyKeyDisplayName(existing.ToKey);
				SelectedMappingHint.Text = I18n.TF("KeyMapWaitingForTargetKeyWithCurrent", fromFriendly, toFriendly);
			}
			else
			{
				SelectedMappingHint.Text = I18n.TF("KeyMapWaitingForTargetKey", fromFriendly);
			}
		}
		else
		{
			SelectedMappingHint.Text = I18n.T("KeyMapEditorMappingHint");
		}
		PresetButton.Content = I18n.T("KeyMapEditorPresetSpatial");
		ClearButton.Content = I18n.T("KeyMapEditorClear");
		SaveButton.Content = I18n.T("KeyMapEditorSave");
		CancelButton.Content = I18n.T("KeyMapEditorCancel");
		ListEditHintTextBlock.Text = I18n.T("KeyMapListEditHint");
		UpdateAddButtonState();

		ColSource.Header = I18n.T("KeyMapEditorSourceKey");
		ColTarget.Header = I18n.T("KeyMapEditorTargetKey");
		ColAction.Header = I18n.T("KeyMapEditorActionHeader");

		SourceKeyRecorder.Placeholder = I18n.T("KeyMapRecordSourcePlaceholder") ?? "点击录制源按键...";
		TargetKeyRecorder.Placeholder = I18n.T("KeyMapRecordTargetPlaceholder") ?? "点击录制目标按键...";
		SourceKeyRecorder.RecordingHint = I18n.T("KeyMapRecordingHint");
		TargetKeyRecorder.RecordingHint = I18n.T("KeyMapRecordingHint");

		SourceKeyRecorder.ToolTip = I18n.T("KeyMapEditorSourceTooltip");
		TargetKeyRecorder.ToolTip = I18n.T("KeyMapEditorTargetTooltip");

		DeleteButtonLabel = I18n.T("KeyMapEditorDelete");

		DiagramHeaderTextBlock.Text = I18n.T("KeyMapDiagramHeader") ?? "映射关系图谱";
		DiagramHintTextBlock.Text = I18n.T("KeyMapDiagramHint") ?? "（点击查看关联，Ctrl+滚轮缩放，拖动平移）";
		OtherSourcesHeader.Text = I18n.T("KeyMapOtherSourcesHeader") ?? "其他源按键";
		OtherTargetsHeader.Text = I18n.T("KeyMapOtherTargetsHeader") ?? "其他目标按键";
		FitCanvasButton.Content = I18n.T("KeyMapFitCanvas") ?? "适应画布";
		ZoomInButton.ToolTip = I18n.T("KeyMapZoomInTooltip") ?? "放大 (Ctrl + 滚轮向上)";
		ZoomOutButton.ToolTip = I18n.T("KeyMapZoomOutTooltip") ?? "缩小 (Ctrl + 滚轮向下)";
	}

	public void RefreshVisualHighlights()
	{
		var mappedSources = new HashSet<string>(_entries.Select(e => e.FromKey), StringComparer.OrdinalIgnoreCase);
		var mappedTargets = new HashSet<string>(_entries.Select(e => e.ToKey), StringComparer.OrdinalIgnoreCase);

		var activeSources = new HashSet<string>(_activeRelationships.Select(r => r.FromKey), StringComparer.OrdinalIgnoreCase);
		if (!string.IsNullOrEmpty(_activeSingleSource))
		{
			activeSources.Add(_activeSingleSource);
		}

		var activeTargets = new HashSet<string>(_activeRelationships.Select(r => r.ToKey), StringComparer.OrdinalIgnoreCase);
		if (!string.IsNullOrEmpty(_activeSingleTarget))
		{
			activeTargets.Add(_activeSingleTarget);
		}

		Brush defaultBg = TryFindResource("CardBackgroundBrush") as Brush ?? Brushes.Transparent;
		Brush defaultBorder = TryFindResource("CardBorderBrush") as Brush ?? (TryFindResource("BorderSubtleBrush") as Brush ?? Brushes.Gray);
		Brush defaultFg = TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.Black;
		Brush accentBorder = TryFindResource("AccentPrimaryBrush") as Brush ?? Brushes.Blue;
		Brush accentHoverBg = TryFindResource("ButtonHoverBgBrush") as Brush ?? Brushes.LightGray;
		Brush accentSelectedBg = TryFindResource("AccentPrimaryBrush") as Brush ?? Brushes.Blue;
		Brush accentSelectedFg = TryFindResource("AccentTextBrush") as Brush ?? Brushes.White;

		foreach (var kvp in _physicalButtons)
		{
			bool isActive = activeSources.Contains(kvp.Key);
			bool isMapped = mappedSources.Contains(kvp.Key);

			if (isActive)
			{
				kvp.Value.BorderBrush = accentBorder;
				kvp.Value.Background = accentSelectedBg;
				kvp.Value.Foreground = accentSelectedFg;
				kvp.Value.BorderThickness = new Thickness(2.0);
			}
			else if (isMapped)
			{
				kvp.Value.BorderBrush = accentBorder;
				kvp.Value.Background = accentHoverBg;
				kvp.Value.Foreground = defaultFg;
				kvp.Value.BorderThickness = new Thickness(1.5);
			}
			else
			{
				kvp.Value.BorderBrush = defaultBorder;
				kvp.Value.Background = defaultBg;
				kvp.Value.Foreground = defaultFg;
				kvp.Value.BorderThickness = new Thickness(1.5);
			}
		}

		foreach (var kvp in _targetButtons)
		{
			bool isActive = activeTargets.Contains(kvp.Key);
			bool isMapped = mappedTargets.Contains(kvp.Key);

			if (isActive)
			{
				kvp.Value.BorderBrush = accentBorder;
				kvp.Value.Background = accentSelectedBg;
				kvp.Value.Foreground = accentSelectedFg;
				kvp.Value.BorderThickness = new Thickness(2.0);
			}
			else if (isMapped)
			{
				kvp.Value.BorderBrush = accentBorder;
				kvp.Value.Background = accentHoverBg;
				kvp.Value.Foreground = defaultFg;
				kvp.Value.BorderThickness = new Thickness(1.5);
			}
			else
			{
				kvp.Value.BorderBrush = defaultBorder;
				kvp.Value.Background = defaultBg;
				kvp.Value.Foreground = defaultFg;
				kvp.Value.BorderThickness = new Thickness(1.5);
			}
		}

		UpdateDiagramConnections();
	}

	private void SetActiveRelationships(IEnumerable<KeyboardRemapEntry> relationships)
	{
		_activeRelationships = relationships.ToList();
		_activeSingleSource = null;
		_activeSingleTarget = null;
		RefreshVisualHighlights();
	}

	private void SetActiveSingleSource(string sourceKey)
	{
		_activeRelationships.Clear();
		_activeSingleSource = KeyMapCodec.NormalizeKeyName(sourceKey);
		_activeSingleTarget = null;
		RefreshVisualHighlights();
	}

	private void SetActiveSingleTarget(string targetKey)
	{
		_activeRelationships.Clear();
		_activeSingleSource = null;
		_activeSingleTarget = KeyMapCodec.NormalizeKeyName(targetKey);
		RefreshVisualHighlights();
	}

	private void ClearActiveRelationships()
	{
		_activeRelationships.Clear();
		_activeSingleSource = null;
		_activeSingleTarget = null;
		RefreshVisualHighlights();
	}

	public void UpdateDiagramConnections()
	{
		ConnectionCanvas.Children.Clear();
		if (_activeRelationships.Count == 0)
		{
			return;
		}

		Brush lineBrush = TryFindResource("AccentPrimaryBrush") as Brush ?? Brushes.Blue;

		foreach (var rel in _activeRelationships)
		{
			if (!_physicalButtons.TryGetValue(rel.FromKey, out var srcBtn) ||
			    !_targetButtons.TryGetValue(rel.ToKey, out var tgtBtn))
			{
				continue;
			}

			Point startPt;
			Point endPt;
			try
			{
				if (!srcBtn.IsLoaded || !tgtBtn.IsLoaded || !ConnectionCanvas.IsLoaded)
				{
					// 无界面单元测试或控件未进入可视呈现树时，安全跳过 TranslatePoint
					continue;
				}

				double srcW = srcBtn.ActualWidth > 0 ? srcBtn.ActualWidth : (double.IsNaN(srcBtn.Width) ? 46 : srcBtn.Width);
				double srcH = srcBtn.ActualHeight > 0 ? srcBtn.ActualHeight : 42;
				double tgtH = tgtBtn.ActualHeight > 0 ? tgtBtn.ActualHeight : 42;

				startPt = srcBtn.TranslatePoint(new Point(srcW, srcH / 2), ConnectionCanvas);
				endPt = tgtBtn.TranslatePoint(new Point(0, tgtH / 2), ConnectionCanvas);
			}
			catch
			{
				continue;
			}

			DrawBezierConnection(startPt, endPt, lineBrush);
		}
	}

	private void DrawBezierConnection(Point startPt, Point endPt, Brush accentBrush)
	{
		double dx = Math.Max(30, (endPt.X - startPt.X) * 0.5);
		Point cp1 = new Point(startPt.X + dx, startPt.Y);
		Point cp2 = new Point(endPt.X - dx, endPt.Y);

		var pathFig = new PathFigure { StartPoint = startPt, IsClosed = false };
		pathFig.Segments.Add(new BezierSegment(cp1, cp2, endPt, true));
		var pathGeo = new PathGeometry();
		pathGeo.Figures.Add(pathFig);

		var path = new System.Windows.Shapes.Path
		{
			Data = pathGeo,
			Stroke = accentBrush,
			StrokeThickness = 2.5,
			StrokeStartLineCap = PenLineCap.Round,
			StrokeEndLineCap = PenLineCap.Round
		};
		ConnectionCanvas.Children.Add(path);

		// 起点圆点
		var startDot = new Ellipse
		{
			Width = 7,
			Height = 7,
			Fill = accentBrush
		};
		Canvas.SetLeft(startDot, startPt.X - 3.5);
		Canvas.SetTop(startDot, startPt.Y - 3.5);
		ConnectionCanvas.Children.Add(startDot);

		// 终点箭头
		Point arrowTip = endPt;
		Point arrowP1 = new Point(endPt.X - 8, endPt.Y - 5);
		Point arrowP2 = new Point(endPt.X - 8, endPt.Y + 5);
		var arrowFig = new PathFigure { StartPoint = arrowTip, IsClosed = true, IsFilled = true };
		arrowFig.Segments.Add(new LineSegment(arrowP1, true));
		arrowFig.Segments.Add(new LineSegment(arrowP2, true));
		var arrowGeo = new PathGeometry();
		arrowGeo.Figures.Add(arrowFig);
		var arrowPath = new System.Windows.Shapes.Path
		{
			Data = arrowGeo,
			Fill = accentBrush
		};
		ConnectionCanvas.Children.Add(arrowPath);
	}

	public void HandleSourceKeyClick(string keyName)
	{
		if (_physicalButtons.TryGetValue(keyName, out var btn))
		{
			VisualKey_Click(btn, new RoutedEventArgs());
		}
		else
		{
			var tempBtn = new Button { Tag = keyName };
			VisualKey_Click(tempBtn, new RoutedEventArgs());
		}
	}

	private void VisualKey_Click(object sender, RoutedEventArgs e)
	{
		if (sender is Button btn && btn.Tag is string keyName)
		{
			string normKey = KeyMapCodec.NormalizeKeyName(keyName);
			_pendingPairSourceKey = normKey;
			_isDiagramInspectionOnly = true;

			_isUpdatingSelectionFromDiagram = true;
			try
			{
				SourceKeyRecorder.SetKey(normKey);

				var existing = _entries.FirstOrDefault(m => string.Equals(m.FromKey, normKey, StringComparison.OrdinalIgnoreCase));
				if (existing != null)
				{
					_editingEntry = existing;
					TargetKeyRecorder.SetKey(existing.ToKey);
					MappingsDataGrid.SelectedItem = existing;
					MappingsDataGrid.ScrollIntoView(existing);

					string fromFriendly = KeyMapCodec.GetFriendlyKeyDisplayName(existing.FromKey);
					string toFriendly = KeyMapCodec.GetFriendlyKeyDisplayName(existing.ToKey);
					SelectedMappingHint.Text = I18n.TF("KeyMapWaitingForTargetKeyWithCurrent", fromFriendly, toFriendly);
				}
				else
				{
					_editingEntry = null;
					TargetKeyRecorder.ClearKey();
					MappingsDataGrid.SelectedItem = null;

					string fromFriendly = KeyMapCodec.GetFriendlyKeyDisplayName(normKey);
					SelectedMappingHint.Text = I18n.TF("KeyMapWaitingForTargetKey", fromFriendly);
				}
			}
			finally
			{
				_isUpdatingSelectionFromDiagram = false;
			}

			SetActiveSingleSource(normKey);
			UpdateAddButtonState();
			StatusTextBlock.Text = "";
		}
	}

	public void HandleTargetKeyClick(string keyName)
	{
		if (_targetButtons.TryGetValue(keyName, out var btn))
		{
			VisualTarget_Click(btn, new RoutedEventArgs());
		}
		else
		{
			var tempBtn = new Button { Tag = keyName };
			VisualTarget_Click(tempBtn, new RoutedEventArgs());
		}
	}

	private void VisualTarget_Click(object sender, RoutedEventArgs e)
	{
		if (sender is Button btn && btn.Tag is string keyName)
		{
			string normTarget = KeyMapCodec.NormalizeKeyName(keyName);

			// 分支 1：处于“点源键 ➔ 点目标键”直接配置模式
			if (!string.IsNullOrEmpty(_pendingPairSourceKey))
			{
				string from = _pendingPairSourceKey;
				string to = normTarget;

				var candidate = new KeyboardRemapEntry { FromKey = from, ToKey = to };

				// 预校验：构建包含候选新项（并排除该源键旧映射）的测试列表
				var testList = new List<KeyboardRemapEntry>(
					_entries.Where(m => !string.Equals(m.FromKey, from, StringComparison.OrdinalIgnoreCase))
				)
				{
					candidate
				};

				string? validationErr = KeyMapValidator.Validate(testList);
				if (!string.IsNullOrEmpty(validationErr))
				{
					StatusTextBlock.Text = validationErr;
					return;
				}

				// 校验成功，在内存中更新 _entries：
				// 已有源键选择新目标时，替换该源键映射；点相同目标不产生重复项。
				int existingIndex = -1;
				for (int i = 0; i < _entries.Count; i++)
				{
					if (string.Equals(_entries[i].FromKey, from, StringComparison.OrdinalIgnoreCase))
					{
						existingIndex = i;
						break;
					}
				}

				if (existingIndex >= 0)
				{
					if (string.Equals(_entries[existingIndex].ToKey, to, StringComparison.OrdinalIgnoreCase))
					{
						candidate = _entries[existingIndex];
					}
					else
					{
						_entries[existingIndex] = candidate;
					}
				}
				else
				{
					_entries.Add(candidate);
				}

				_pendingPairSourceKey = null;
				_isDiagramInspectionOnly = false;
				_editingEntry = candidate;

				SourceKeyRecorder.SetKey(from);
				TargetKeyRecorder.SetKey(to);

				_isUpdatingSelectionFromDiagram = true;
				try
				{
					MappingsDataGrid.SelectedItem = candidate;
					MappingsDataGrid.ScrollIntoView(candidate);
				}
				finally
				{
					_isUpdatingSelectionFromDiagram = false;
				}

				string fromFriendly = KeyMapCodec.GetFriendlyKeyDisplayName(from);
				string toFriendly = KeyMapCodec.GetFriendlyKeyDisplayName(to);
				SelectedMappingHint.Text = $"{fromFriendly} ➔ {toFriendly}";

				SetActiveRelationships(new[] { candidate });
				SyncDynamicVisualKeys();
				UpdateAddButtonState();
				StatusTextBlock.Text = "";
				RefreshVisualHighlights();

				if (!_hasUserInteractedWithCanvas)
				{
					FitCanvas();
				}
				else
				{
					UpdateDiagramConnections();
				}
				return;
			}

			// 分支 2：未先选择源键，单击目标键保持现有“查看关联”行为，绝不新增或修改映射
			TargetKeyRecorder.SetKey(normTarget);
			var matchingEntries = _entries.Where(m => string.Equals(m.ToKey, normTarget, StringComparison.OrdinalIgnoreCase)).ToList();

			if (matchingEntries.Count == 0)
			{
				_editingEntry = null;
				_isUpdatingSelectionFromDiagram = true;
				try
				{
					MappingsDataGrid.SelectedItem = null;
				}
				finally
				{
					_isUpdatingSelectionFromDiagram = false;
				}

				SourceKeyRecorder.ClearKey();
				SelectedMappingHint.Text = $"? ➔ {KeyMapCodec.GetFriendlyKeyDisplayName(normTarget)}";
				SetActiveSingleTarget(normTarget);
				_isDiagramInspectionOnly = true;
			}
			else if (matchingEntries.Count == 1)
			{
				var single = matchingEntries[0];
				_editingEntry = single;
				SourceKeyRecorder.SetKey(single.FromKey);

				_isUpdatingSelectionFromDiagram = true;
				try
				{
					MappingsDataGrid.SelectedItem = single;
					MappingsDataGrid.ScrollIntoView(single);
				}
				finally
				{
					_isUpdatingSelectionFromDiagram = false;
				}

				SelectedMappingHint.Text = $"{KeyMapCodec.GetFriendlyKeyDisplayName(single.FromKey)} ➔ {KeyMapCodec.GetFriendlyKeyDisplayName(single.ToKey)}";
				SetActiveRelationships(new[] { single });
				_isDiagramInspectionOnly = false;
			}
			else
			{
				// 多对一映射："点击目标键时展示所有映射到它的源键，不得在多对一关系中擅自选一条"
				_editingEntry = null;
				_isUpdatingSelectionFromDiagram = true;
				try
				{
					MappingsDataGrid.SelectedItem = null;
				}
				finally
				{
					_isUpdatingSelectionFromDiagram = false;
				}

				SourceKeyRecorder.ClearKey();
				SelectedMappingHint.Text = I18n.TF("KeyMapDiagramMultiMappingHint", matchingEntries.Count, KeyMapCodec.GetFriendlyKeyDisplayName(normTarget));
				SetActiveRelationships(matchingEntries);
				_isDiagramInspectionOnly = true;
			}
			UpdateAddButtonState();
		}
	}

	public void ApplyZoom(double targetScale, Point? center = null)
	{
		_hasUserInteractedWithCanvas = true;
		double clampedScale = Math.Clamp(targetScale, 0.6, 2.0);
		double currentScale = DiagramScaleTransform.ScaleX;

		if (Math.Abs(clampedScale - currentScale) < 0.0001 && center == null)
		{
			return;
		}

		double viewportW = DiagramViewport.ActualWidth > 0 ? DiagramViewport.ActualWidth : (DiagramViewport.Width > 0 ? DiagramViewport.Width : 600);
		double viewportH = DiagramViewport.ActualHeight > 0 ? DiagramViewport.ActualHeight : (DiagramViewport.Height > 0 ? DiagramViewport.Height : 220);

		Point zoomCenter = center ?? new Point(viewportW / 2, viewportH / 2);
		if (viewportW <= 0 || viewportH <= 0)
		{
			DiagramScaleTransform.ScaleX = clampedScale;
			DiagramScaleTransform.ScaleY = clampedScale;
			UpdateZoomPercentText();
			return;
		}

		double oldTx = DiagramTranslateTransform.X;
		double oldTy = DiagramTranslateTransform.Y;

		double ratio = clampedScale / (currentScale > 0 ? currentScale : 1.0);
		double newTx = zoomCenter.X - ratio * (zoomCenter.X - oldTx);
		double newTy = zoomCenter.Y - ratio * (zoomCenter.Y - oldTy);

		DiagramScaleTransform.ScaleX = clampedScale;
		DiagramScaleTransform.ScaleY = clampedScale;
		DiagramTranslateTransform.X = newTx;
		DiagramTranslateTransform.Y = newTy;

		UpdateZoomPercentText();
		UpdateDiagramConnections();
	}

	public void FitCanvas()
	{
		_hasUserInteractedWithCanvas = false;
		double availableW = DiagramViewport.ActualWidth > 0 ? DiagramViewport.ActualWidth : (DiagramViewport.Width > 0 ? DiagramViewport.Width : 600);
		double availableH = DiagramViewport.ActualHeight > 0 ? DiagramViewport.ActualHeight : (DiagramViewport.Height > 0 ? DiagramViewport.Height : 220);

		if (availableW <= 20 || availableH <= 20)
		{
			DiagramScaleTransform.ScaleX = 1.0;
			DiagramScaleTransform.ScaleY = 1.0;
			DiagramTranslateTransform.X = 0;
			DiagramTranslateTransform.Y = 0;
			UpdateZoomPercentText();
			return;
		}

		if (OtherSourcesContainer.Visibility == Visibility.Visible)
		{
			OtherSourcesPanel.InvalidateMeasure();
			OtherSourcesPanel.Measure(new Size(240, double.PositiveInfinity));
			OtherSourcesContainer.InvalidateMeasure();
			OtherSourcesContainer.Measure(new Size(240, double.PositiveInfinity));
		}
		if (OtherTargetsContainer.Visibility == Visibility.Visible)
		{
			OtherTargetsPanel.InvalidateMeasure();
			OtherTargetsPanel.Measure(new Size(220, double.PositiveInfinity));
			OtherTargetsContainer.InvalidateMeasure();
			OtherTargetsContainer.Measure(new Size(220, double.PositiveInfinity));
		}

		DiagramLayoutGrid.InvalidateMeasure();
		DiagramLayoutGrid.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
		double contentW = Math.Max(DiagramLayoutGrid.DesiredSize.Width, DiagramLayoutGrid.ActualWidth);
		double contentH = Math.Max(DiagramLayoutGrid.DesiredSize.Height, DiagramLayoutGrid.ActualHeight);

		if (contentW <= 0) contentW = 650;
		if (contentH <= 0) contentH = 220;

		double padding = Math.Min(20.0, Math.Min(availableW, availableH) * 0.1);
		double scaleX = Math.Max(0.001, (availableW - padding) / contentW);
		double scaleY = Math.Max(0.001, (availableH - padding) / contentH);
		double targetScale = Math.Min(scaleX, scaleY);

		// 确保缩放后几何尺寸绝不超出视口可用空间（留有安全缓冲，坚决杜绝裁切）
		double maxSafeScaleX = Math.Max(0.001, (availableW - 4.0) / contentW);
		double maxSafeScaleY = Math.Max(0.001, (availableH - 4.0) / contentH);
		targetScale = Math.Min(targetScale, Math.Min(maxSafeScaleX, maxSafeScaleY));
		targetScale = Math.Clamp(targetScale, 0.01, 1.2);

		DiagramScaleTransform.ScaleX = targetScale;
		DiagramScaleTransform.ScaleY = targetScale;

		double newTx = (availableW - contentW * targetScale) / 2.0;
		double newTy = (availableH - contentH * targetScale) / 2.0;
		DiagramTranslateTransform.X = Math.Max(0, newTx);
		DiagramTranslateTransform.Y = Math.Max(0, newTy);

		UpdateZoomPercentText();
		UpdateDiagramConnections();
	}

	private void UpdateZoomPercentText()
	{
		int pct = (int)Math.Round(DiagramScaleTransform.ScaleX * 100);
		ZoomPercentTextBlock.Text = $"{pct}%";
	}

	private void ZoomInButton_Click(object sender, RoutedEventArgs e)
	{
		_hasUserInteractedWithCanvas = true;
		ApplyZoom(DiagramScaleTransform.ScaleX + 0.1);
	}

	private void ZoomOutButton_Click(object sender, RoutedEventArgs e)
	{
		_hasUserInteractedWithCanvas = true;
		ApplyZoom(DiagramScaleTransform.ScaleX - 0.1);
	}

	internal void FitCanvasButton_Click(object sender, RoutedEventArgs e)
	{
		_hasUserInteractedWithCanvas = false;
		FitCanvas();
	}

	private void DiagramViewport_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
	{
		if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
		{
			_hasUserInteractedWithCanvas = true;
			Point mousePos = e.GetPosition(DiagramViewport);
			double deltaScale = e.Delta > 0 ? 0.1 : -0.1;
			ApplyZoom(DiagramScaleTransform.ScaleX + deltaScale, mousePos);
			e.Handled = true;
		}
	}

	private void DiagramViewport_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
	{
		if (e.ChangedButton == MouseButton.Left || e.ChangedButton == MouseButton.Middle)
		{
			_isPanning = true;
			_hasUserInteractedWithCanvas = true;
			_panStartPoint = e.GetPosition(DiagramViewport);
			_panStartTranslate = new Point(DiagramTranslateTransform.X, DiagramTranslateTransform.Y);
			DiagramViewport.CaptureMouse();
			DiagramViewport.Cursor = Cursors.SizeAll;
		}
	}

	private void DiagramViewport_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
	{
		if (_isPanning)
		{
			Point currentPoint = e.GetPosition(DiagramViewport);
			Vector delta = currentPoint - _panStartPoint;
			DiagramTranslateTransform.X = _panStartTranslate.X + delta.X;
			DiagramTranslateTransform.Y = _panStartTranslate.Y + delta.Y;
		}
	}

	private void DiagramViewport_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
	{
		if (_isPanning && (e.ChangedButton == MouseButton.Left || e.ChangedButton == MouseButton.Middle))
		{
			_isPanning = false;
			DiagramViewport.ReleaseMouseCapture();
			DiagramViewport.Cursor = Cursors.Arrow;
		}
	}

	private void DiagramViewport_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
	{
		if (_isPanning)
		{
			_isPanning = false;
			DiagramViewport.ReleaseMouseCapture();
			DiagramViewport.Cursor = Cursors.Arrow;
		}
	}

	private void DiagramViewport_SizeChanged(object sender, SizeChangedEventArgs e)
	{
		if (!_hasUserInteractedWithCanvas)
		{
			FitCanvas();
		}
		else
		{
			UpdateDiagramConnections();
		}
	}

	public void UpdateAddButtonState()
	{
		bool isEditing = _editingEntry != null || MappingsDataGrid.SelectedItem is KeyboardRemapEntry;
		AddButton.Content = isEditing
			? (I18n.T("KeyMapEditorUpdateMapping") ?? "更新映射")
			: (I18n.T("KeyMapEditorAddMapping") ?? "添加映射");
	}

	public bool TryCommitPendingMapping(out string? errorMessage)
	{
		errorMessage = null;
		string from = KeyMapCodec.NormalizeKeyName(SourceKeyRecorder.SelectedKey);
		string to = KeyMapCodec.NormalizeKeyName(TargetKeyRecorder.SelectedKey);

		if (string.IsNullOrEmpty(from) && string.IsNullOrEmpty(to))
		{
			_pendingPairSourceKey = null;
			return true;
		}

		if ((_isDiagramInspectionOnly || !string.IsNullOrEmpty(_pendingPairSourceKey)) && (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to)))
		{
			_pendingPairSourceKey = null;
			return true;
		}

		if (!string.IsNullOrEmpty(_pendingPairSourceKey) && string.Equals(_pendingPairSourceKey, from, StringComparison.OrdinalIgnoreCase))
		{
			_pendingPairSourceKey = null;
			return true;
		}

		if (string.IsNullOrEmpty(from) && !string.IsNullOrEmpty(to))
		{
			errorMessage = I18n.T("KeyMapEditorErrIncompleteSource");
			return false;
		}
		if (!string.IsNullOrEmpty(from) && string.IsNullOrEmpty(to))
		{
			errorMessage = I18n.T("KeyMapEditorErrIncompleteTarget");
			return false;
		}

		var candidate = new KeyboardRemapEntry { FromKey = from, ToKey = to };
		var editingEntry = _editingEntry ?? (MappingsDataGrid.SelectedItem as KeyboardRemapEntry);

		if (editingEntry != null &&
		    string.Equals(editingEntry.FromKey, from, StringComparison.OrdinalIgnoreCase) &&
		    string.Equals(editingEntry.ToKey, to, StringComparison.OrdinalIgnoreCase))
		{
			_editingEntry = null;
			_pendingPairSourceKey = null;
			_isDiagramInspectionOnly = false;
			SourceKeyRecorder.ClearKey();
			TargetKeyRecorder.ClearKey();
			MappingsDataGrid.SelectedItem = null;
			ClearActiveRelationships();
			UpdateAddButtonState();
			RefreshVisualHighlights();
			return true;
		}

		var testList = new List<KeyboardRemapEntry>(
			_entries.Where(m => m != editingEntry && !string.Equals(m.FromKey, from, StringComparison.OrdinalIgnoreCase))
		)
		{
			candidate
		};

		string? err = KeyMapValidator.Validate(testList);
		if (!string.IsNullOrEmpty(err))
		{
			errorMessage = err;
			return false;
		}

		int editIndex = -1;
		if (editingEntry != null)
		{
			editIndex = _entries.IndexOf(editingEntry);
			_entries.Remove(editingEntry);
		}

		for (int i = _entries.Count - 1; i >= 0; i--)
		{
			if (string.Equals(_entries[i].FromKey, from, StringComparison.OrdinalIgnoreCase))
			{
				if (editIndex < 0)
				{
					editIndex = i;
				}
				_entries.RemoveAt(i);
			}
		}

		if (editIndex >= 0 && editIndex <= _entries.Count)
		{
			_entries.Insert(editIndex, candidate);
		}
		else
		{
			_entries.Add(candidate);
		}

		_editingEntry = null;
		_pendingPairSourceKey = null;
		_isDiagramInspectionOnly = false;
		SourceKeyRecorder.ClearKey();
		TargetKeyRecorder.ClearKey();
		MappingsDataGrid.SelectedItem = null;
		ClearActiveRelationships();
		SyncDynamicVisualKeys();
		UpdateAddButtonState();
		RefreshVisualHighlights();
		if (!_hasUserInteractedWithCanvas)
		{
			FitCanvas();
		}
		else
		{
			UpdateDiagramConnections();
		}
		return true;
	}

	internal void PresetButton_Click(object sender, RoutedEventArgs e)
	{
		_editingEntry = null;
		_pendingPairSourceKey = null;
		_isDiagramInspectionOnly = false;
		_hasUserInteractedWithCanvas = false;
		SourceKeyRecorder.ClearKey();
		TargetKeyRecorder.ClearKey();
		_entries.Clear();
		foreach (var entry in KeyMapCodec.GetDefaultSpatialPreset())
		{
			_entries.Add(new KeyboardRemapEntry
			{
				FromKey = entry.FromKey,
				ToKey = entry.ToKey
			});
		}
		MappingsDataGrid.SelectedItem = null;
		ClearActiveRelationships();
		SyncDynamicVisualKeys();
		UpdateAddButtonState();
		StatusTextBlock.Text = "";
		RefreshVisualHighlights();
		FitCanvas();
	}

	internal void ClearButton_Click(object sender, RoutedEventArgs e)
	{
		_editingEntry = null;
		_pendingPairSourceKey = null;
		_isDiagramInspectionOnly = false;
		_hasUserInteractedWithCanvas = false;
		SourceKeyRecorder.ClearKey();
		TargetKeyRecorder.ClearKey();
		_entries.Clear();
		MappingsDataGrid.SelectedItem = null;
		ClearActiveRelationships();
		SyncDynamicVisualKeys();
		UpdateAddButtonState();
		StatusTextBlock.Text = "";
		RefreshVisualHighlights();
		FitCanvas();
	}

	internal void AddButton_Click(object sender, RoutedEventArgs e)
	{
		_pendingPairSourceKey = null;
		string from = KeyMapCodec.NormalizeKeyName(SourceKeyRecorder.SelectedKey);
		string to = KeyMapCodec.NormalizeKeyName(TargetKeyRecorder.SelectedKey);

		if (string.IsNullOrEmpty(from) && string.IsNullOrEmpty(to))
		{
			StatusTextBlock.Text = I18n.T("KeyMapEditorErrEmptyKeys");
			return;
		}

		if (string.IsNullOrEmpty(from) && !string.IsNullOrEmpty(to))
		{
			StatusTextBlock.Text = I18n.T("KeyMapEditorErrIncompleteSource");
			return;
		}
		if (!string.IsNullOrEmpty(from) && string.IsNullOrEmpty(to))
		{
			StatusTextBlock.Text = I18n.T("KeyMapEditorErrIncompleteTarget");
			return;
		}

		_isDiagramInspectionOnly = false;
		if (!TryCommitPendingMapping(out string? err))
		{
			StatusTextBlock.Text = err;
			return;
		}

		StatusTextBlock.Text = "";
	}

	internal void DeleteRow_Click(object sender, RoutedEventArgs e)
	{
		_pendingPairSourceKey = null;
		if (sender is Button btn && btn.DataContext is KeyboardRemapEntry entry)
		{
			_entries.Remove(entry);
			if (_editingEntry == entry || MappingsDataGrid.SelectedItem == entry)
			{
				_editingEntry = null;
				MappingsDataGrid.SelectedItem = null;
				SourceKeyRecorder.ClearKey();
				TargetKeyRecorder.ClearKey();
				ClearActiveRelationships();
			}
			SyncDynamicVisualKeys();
			UpdateAddButtonState();
			StatusTextBlock.Text = "";
			RefreshVisualHighlights();
			if (!_hasUserInteractedWithCanvas)
			{
				FitCanvas();
			}
			else
			{
				UpdateDiagramConnections();
			}
		}
	}

	private void MappingsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (_isUpdatingSelectionFromDiagram)
		{
			return;
		}

		_pendingPairSourceKey = null;
		if (MappingsDataGrid.SelectedItem is KeyboardRemapEntry entry)
		{
			_editingEntry = entry;
			_isDiagramInspectionOnly = false;
			string fromFriendly = KeyMapCodec.GetFriendlyKeyDisplayName(entry.FromKey);
			string toFriendly = KeyMapCodec.GetFriendlyKeyDisplayName(entry.ToKey);
			SelectedMappingHint.Text = $"{fromFriendly} ➔ {toFriendly}";
			SourceKeyRecorder.SetKey(entry.FromKey);
			TargetKeyRecorder.SetKey(entry.ToKey);
			SetActiveRelationships(new[] { entry });
		}
		else
		{
			_editingEntry = null;
			ClearActiveRelationships();
		}
		UpdateAddButtonState();
	}

	private void CancelButton_Click(object sender, RoutedEventArgs e)
	{
		_editingEntry = null;
		_pendingPairSourceKey = null;
		SourceKeyRecorder.CancelRecording();
		TargetKeyRecorder.CancelRecording();
		DialogResult = false;
		Close();
	}

	private void SaveButton_Click(object sender, RoutedEventArgs e)
	{
		SourceKeyRecorder.CancelRecording();
		TargetKeyRecorder.CancelRecording();

		if (!TryCommitPendingMapping(out string? pendingErr))
		{
			StatusTextBlock.Text = pendingErr;
			return;
		}

		string? err = KeyMapValidator.Validate(_entries);
		if (!string.IsNullOrEmpty(err))
		{
			StatusTextBlock.Text = err;
			return;
		}

		ResultSerializedKeyMap = KeyMapCodec.Encode(_entries);
		DialogResult = true;
		Close();
	}
}
