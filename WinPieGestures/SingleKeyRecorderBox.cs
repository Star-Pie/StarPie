using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace WinPieGestures;

/// <summary>
/// 键盘空间重映射专用的高响应度单键录制控件。
/// 严格限定单键录制协议（非组合键、禁用修饰键、长按 Esc 紧急撤销保留保护）；
/// 在录制态时阻断 WPF 默认焦点跳转与窗口默认/取消按钮抢占，并主动通知控制器暂停活动重映射。
/// </summary>
public class SingleKeyRecorderBox : Control
{
	public static readonly DependencyProperty SelectedKeyProperty;
	public static readonly DependencyProperty IsRecordingProperty;
	public static readonly DependencyProperty PlaceholderProperty;
	public static readonly DependencyProperty RecordingHintProperty;
	public static readonly DependencyProperty IsSourceKeyProperty;

	public event EventHandler<string>? KeySelected;
	public event EventHandler? RecordingStarted;
	public event EventHandler? RecordingCancelled;
	public event EventHandler<string>? RecordingFailed;

	private TextBlock? _displayTextBlock;
	private Button? _clearButton;
	private Border? _mainBorder;

	public string SelectedKey
	{
		get => (string)GetValue(SelectedKeyProperty);
		set => SetValue(SelectedKeyProperty, value);
	}

	public bool IsRecording
	{
		get => (bool)GetValue(IsRecordingProperty);
		set => SetValue(IsRecordingProperty, value);
	}

	public string Placeholder
	{
		get => (string)GetValue(PlaceholderProperty);
		set => SetValue(PlaceholderProperty, value);
	}

	public string? RecordingHint
	{
		get => (string?)GetValue(RecordingHintProperty);
		set => SetValue(RecordingHintProperty, value);
	}

	public bool IsSourceKey
	{
		get => (bool)GetValue(IsSourceKeyProperty);
		set => SetValue(IsSourceKeyProperty, value);
	}

	static SingleKeyRecorderBox()
	{
		SelectedKeyProperty = DependencyProperty.Register(
			nameof(SelectedKey),
			typeof(string),
			typeof(SingleKeyRecorderBox),
			new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedKeyChanged));

		IsRecordingProperty = DependencyProperty.Register(
			nameof(IsRecording),
			typeof(bool),
			typeof(SingleKeyRecorderBox),
			new PropertyMetadata(false, OnIsRecordingChanged));

		PlaceholderProperty = DependencyProperty.Register(
			nameof(Placeholder),
			typeof(string),
			typeof(SingleKeyRecorderBox),
			new PropertyMetadata(string.Empty, OnVisualPropertyChanged));

		RecordingHintProperty = DependencyProperty.Register(
			nameof(RecordingHint),
			typeof(string),
			typeof(SingleKeyRecorderBox),
			new PropertyMetadata(null, OnVisualPropertyChanged));

		IsSourceKeyProperty = DependencyProperty.Register(
			nameof(IsSourceKey),
			typeof(bool),
			typeof(SingleKeyRecorderBox),
			new PropertyMetadata(false, OnVisualPropertyChanged));

		DefaultStyleKeyProperty.OverrideMetadata(typeof(SingleKeyRecorderBox), new FrameworkPropertyMetadata(typeof(SingleKeyRecorderBox)));
		StyleProperty.OverrideMetadata(typeof(SingleKeyRecorderBox), new FrameworkPropertyMetadata(CreateDefaultStyle()));
		FocusableProperty.OverrideMetadata(typeof(SingleKeyRecorderBox), new FrameworkPropertyMetadata(true));
	}

	public SingleKeyRecorderBox()
	{
		FocusVisualStyle = null;
		Cursor = Cursors.Hand;
		KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.None);
		KeyboardNavigation.SetDirectionalNavigation(this, KeyboardNavigationMode.None);
		KeyboardNavigation.SetControlTabNavigation(this, KeyboardNavigationMode.None);
	}

	public TextBlock? DisplayTextBlock => _displayTextBlock;
	public Button? ClearButton => _clearButton;
	public Border? MainBorder => _mainBorder;

	public static Style CreateDefaultStyle()
	{
		var style = new Style(typeof(SingleKeyRecorderBox));
		style.Setters.Add(new Setter(BackgroundProperty, new DynamicResourceExtension("InputBackgroundBrush")));
		style.Setters.Add(new Setter(BorderBrushProperty, new DynamicResourceExtension("InputBorderBrush")));
		style.Setters.Add(new Setter(ForegroundProperty, new DynamicResourceExtension("TextPrimaryBrush")));
		style.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(1)));
		style.Setters.Add(new Setter(HeightProperty, 28.0));
		style.Setters.Add(new Setter(TemplateProperty, CreateDefaultTemplate()));
		style.Seal();
		return style;
	}

	private static ControlTemplate CreateDefaultTemplate()
	{
		var template = new ControlTemplate(typeof(SingleKeyRecorderBox));

		var borderFactory = new FrameworkElementFactory(typeof(Border), "PART_Border");
		borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
		borderFactory.SetValue(Border.PaddingProperty, new Thickness(8, 2, 8, 2));
		borderFactory.SetValue(Border.SnapsToDevicePixelsProperty, true);
		borderFactory.SetResourceReference(Border.BackgroundProperty, "InputBackgroundBrush");
		borderFactory.SetResourceReference(Border.BorderBrushProperty, "InputBorderBrush");
		borderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1));

		var gridFactory = new FrameworkElementFactory(typeof(Grid));

		var col0 = new FrameworkElementFactory(typeof(ColumnDefinition));
		col0.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
		var col1 = new FrameworkElementFactory(typeof(ColumnDefinition));
		col1.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
		gridFactory.AppendChild(col0);
		gridFactory.AppendChild(col1);

		var textFactory = new FrameworkElementFactory(typeof(TextBlock), "PART_DisplayText");
		textFactory.SetValue(Grid.ColumnProperty, 0);
		textFactory.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
		textFactory.SetValue(TextBlock.FontSizeProperty, 12.0);
		textFactory.SetValue(TextBlock.FontWeightProperty, FontWeights.Medium);
		textFactory.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
		textFactory.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
		gridFactory.AppendChild(textFactory);

		var btnFactory = new FrameworkElementFactory(typeof(Button), "PART_ClearButton");
		btnFactory.SetValue(Grid.ColumnProperty, 1);
		btnFactory.SetValue(ContentControl.ContentProperty, "✕");
		btnFactory.SetValue(WidthProperty, 16.0);
		btnFactory.SetValue(HeightProperty, 16.0);
		btnFactory.SetValue(MarginProperty, new Thickness(4, 0, 0, 0));
		btnFactory.SetValue(PaddingProperty, new Thickness(0));
		btnFactory.SetValue(Button.FontSizeProperty, 10.0);
		btnFactory.SetResourceReference(Button.ForegroundProperty, "TextMutedBrush");
		btnFactory.SetValue(BackgroundProperty, Brushes.Transparent);
		btnFactory.SetValue(BorderThicknessProperty, new Thickness(0));
		btnFactory.SetValue(CursorProperty, Cursors.Hand);
		btnFactory.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
		btnFactory.SetValue(VisibilityProperty, Visibility.Collapsed);
		gridFactory.AppendChild(btnFactory);

		borderFactory.AppendChild(gridFactory);
		template.VisualTree = borderFactory;
		return template;
	}

	public override void OnApplyTemplate()
	{
		base.OnApplyTemplate();
		_displayTextBlock = GetTemplateChild("PART_DisplayText") as TextBlock;
		_clearButton = GetTemplateChild("PART_ClearButton") as Button;
		_mainBorder = GetTemplateChild("PART_Border") as Border;

		if (_clearButton != null)
		{
			_clearButton.ToolTip = I18n.T("KeyMapClearKey") ?? "Clear key";
			_clearButton.Click += (s, e) =>
			{
				ClearKey();
				e.Handled = true;
			};
		}
		UpdateVisualDisplay();
	}

	public void StartRecording()
	{
		if (IsRecording) return;
		IsRecording = true;
		try
		{
			KeyboardRemapController.Current.NotifyRecorderActive();
		}
		catch
		{
		}
		RecordingStarted?.Invoke(this, EventArgs.Empty);
		UpdateVisualDisplay();
	}

	public void CancelRecording()
	{
		if (!IsRecording) return;
		IsRecording = false;
		Keyboard.ClearFocus();
		RecordingCancelled?.Invoke(this, EventArgs.Empty);
		UpdateVisualDisplay();
	}

	public void ClearKey()
	{
		SelectedKey = string.Empty;
		CancelRecording();
		KeySelected?.Invoke(this, string.Empty);
		UpdateVisualDisplay();
	}

	public void SetKey(string keyName)
	{
		SelectedKey = KeyMapCodec.NormalizeKeyName(keyName);
		IsRecording = false;
		Keyboard.ClearFocus();
		UpdateVisualDisplay();
	}

	protected override void OnMouseDown(MouseButtonEventArgs e)
	{
		base.OnMouseDown(e);
		if (e.ChangedButton == MouseButton.Left)
		{
			if (!IsRecording)
			{
				Focus();
				StartRecording();
			}
			else
			{
				CancelRecording();
			}
			e.Handled = true;
		}
	}

	protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
	{
		base.OnGotKeyboardFocus(e);
		StartRecording();
	}

	protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
	{
		base.OnLostKeyboardFocus(e);
		if (IsRecording)
		{
			CancelRecording();
		}
	}

	protected override void OnPreviewKeyDown(KeyEventArgs e)
	{
		if (!IsRecording)
		{
			base.OnPreviewKeyDown(e);
			return;
		}

		// 拦截所有按键事件：防止 Tab 焦点跳转、Enter 触发窗口默认保存、Esc 触发窗口取消关闭
		e.Handled = true;

		Key key = (e.Key == Key.System) ? e.SystemKey : e.Key;
		if (key == Key.ImeProcessed)
		{
			key = e.ImeProcessedKey;
		}

		// 1. Esc 键交互：取消录制并退出焦点（不关闭窗口）
		if (key == Key.Escape)
		{
			CancelRecording();
			return;
		}

		// 2. 修饰键过滤：严禁将 Ctrl/Alt/Shift/Win 等单独录入为单键映射
		if (IsForbiddenModifier(key))
		{
			RecordingFailed?.Invoke(this, I18n.T("KeyMapErrModifierForbidden") ?? "Modifier keys cannot be mapped as single keys");
			return;
		}

		int rawVk = KeyInterop.VirtualKeyFromKey(key);
		if (rawVk <= 0 || rawVk >= 255)
		{
			return;
		}
		uint vk = (uint)rawVk;

		if (KeyMapValidator.IsModifierVk(vk))
		{
			RecordingFailed?.Invoke(this, I18n.T("KeyMapErrModifierForbidden") ?? "Modifier keys cannot be mapped as single keys");
			return;
		}

		// 3. 源按键安全检查：Escape 键保留用于长按紧急撤销会话，禁止映射为源按键
		if (IsSourceKey && vk == 0x1B)
		{
			RecordingFailed?.Invoke(this, I18n.T("KeyMapErrEscapeReserved") ?? "Escape key is reserved");
			CancelRecording();
			return;
		}

		// 4. 规范化按键名称
		string canonical = KeyMapCodec.NormalizeKeyName(KeyMapCodec.GetCanonicalKeyName(vk));
		if (string.IsNullOrEmpty(canonical) || !KeyMapCodec.TryGetKeyVk(canonical, out uint verifiedVk))
		{
			RecordingFailed?.Invoke(this, string.Format(I18n.T("KeyMapErrUnknownSourceKey") ?? "Unknown key: {0}", canonical));
			return;
		}

		if (IsSourceKey && verifiedVk == 0x1B)
		{
			RecordingFailed?.Invoke(this, I18n.T("KeyMapErrEscapeReserved") ?? "Escape key is reserved");
			CancelRecording();
			return;
		}

		if (KeyMapValidator.IsModifierVk(verifiedVk))
		{
			RecordingFailed?.Invoke(this, I18n.T("KeyMapErrModifierForbidden") ?? "Modifier keys cannot be mapped as single keys");
			return;
		}

		// 5. 提交有效单键
		SelectedKey = canonical;
		IsRecording = false;
		Keyboard.ClearFocus();
		UpdateVisualDisplay();
		KeySelected?.Invoke(this, canonical);
	}

	protected override void OnPreviewKeyUp(KeyEventArgs e)
	{
		if (IsRecording)
		{
			e.Handled = true;
		}
		base.OnPreviewKeyUp(e);
	}

	private static bool IsForbiddenModifier(Key key)
	{
		return key is Key.LeftCtrl or Key.RightCtrl
			or Key.LeftAlt or Key.RightAlt
			or Key.LeftShift or Key.RightShift
			or Key.LWin or Key.RWin
			or Key.Apps;
	}

	private void UpdateVisualDisplay()
	{
		if (_displayTextBlock == null) return;

		if (IsRecording)
		{
			_displayTextBlock.Text = !string.IsNullOrEmpty(RecordingHint)
				? RecordingHint
				: (I18n.T("KeyMapRecordingHint") ?? "🔴 Press a single key (Esc to cancel)...");
			_displayTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xE1, 0x1D, 0x48));

			if (_mainBorder != null)
			{
				_mainBorder.BorderBrush = TryFindResource("AccentPrimaryBrush") as Brush
					?? new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
				_mainBorder.Background = TryFindResource("ItemHoverBrush") as Brush
					?? new SolidColorBrush(Color.FromArgb(0x22, 0x25, 0x63, 0xEB));
				_mainBorder.BorderThickness = new Thickness(1.5);
			}

			if (_clearButton != null)
			{
				_clearButton.Visibility = Visibility.Collapsed;
			}
		}
		else
		{
			if (_mainBorder != null)
			{
				_mainBorder.BorderBrush = TryFindResource("InputBorderBrush") as Brush
					?? new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1));
				_mainBorder.Background = TryFindResource("InputBackgroundBrush") as Brush
					?? Brushes.White;
				_mainBorder.BorderThickness = new Thickness(1);
			}

			if (string.IsNullOrEmpty(SelectedKey))
			{
				_displayTextBlock.Text = !string.IsNullOrEmpty(Placeholder)
					? Placeholder
					: (IsSourceKey
						? (I18n.T("KeyMapRecordSourcePlaceholder") ?? "Click to record source key...")
						: (I18n.T("KeyMapRecordTargetPlaceholder") ?? "Click to record target key..."));
				_displayTextBlock.Foreground = TryFindResource("TextMutedBrush") as Brush
					?? new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
				ToolTip = null;

				if (_clearButton != null)
				{
					_clearButton.Visibility = Visibility.Collapsed;
				}
			}
			else
			{
				string friendly = KeyMapCodec.GetFriendlyKeyDisplayName(SelectedKey);
				_displayTextBlock.Text = friendly;
				_displayTextBlock.Foreground = TryFindResource("TextPrimaryBrush") as Brush
					?? new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A));

				ToolTip = string.Equals(friendly, SelectedKey, StringComparison.OrdinalIgnoreCase)
					? SelectedKey
					: $"{friendly} ({SelectedKey})";

				if (_clearButton != null)
				{
					_clearButton.Visibility = Visibility.Visible;
					_clearButton.ToolTip = I18n.T("KeyMapClearKey") ?? "Clear key";
				}
			}
		}
	}

	private static void OnSelectedKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is SingleKeyRecorderBox recorder)
		{
			recorder.UpdateVisualDisplay();
		}
	}

	private static void OnIsRecordingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is SingleKeyRecorderBox recorder)
		{
			recorder.UpdateVisualDisplay();
		}
	}

	private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is SingleKeyRecorderBox recorder)
		{
			recorder.UpdateVisualDisplay();
		}
	}
}

/// <summary>
/// 键名在表格与提示文本中的友好人类可读转换器。
/// 例如普通句点 "OemPeriod" 呈现为 ". (OemPeriod)"，小键盘句点 "NumDecimal" 呈现为 "Num . (NumDecimal)"。
/// </summary>
public class KeyDisplayNameConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		if (value is string keyName && !string.IsNullOrWhiteSpace(keyName))
		{
			string friendly = KeyMapCodec.GetFriendlyKeyDisplayName(keyName);
			if (!string.Equals(friendly, keyName, StringComparison.OrdinalIgnoreCase))
			{
				return $"{friendly} ({keyName})";
			}
			return keyName;
		}
		return value ?? "";
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
	{
		if (value is string str && !string.IsNullOrWhiteSpace(str))
		{
			string trimmed = str.Trim();
			int openParen = trimmed.LastIndexOf('(');
			int closeParen = trimmed.LastIndexOf(')');
			if (openParen >= 0 && closeParen > openParen)
			{
				string keyInside = trimmed.Substring(openParen + 1, closeParen - openParen - 1).Trim();
				return KeyMapCodec.NormalizeKeyName(keyInside);
			}
			return KeyMapCodec.NormalizeKeyName(trimmed);
		}
		return string.Empty;
	}
}
