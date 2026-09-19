using System;
using System.Windows;
using System.Windows.Media;

namespace WinPieGestures;

public class BlacklistProcessItemViewModel
{
	public string ProcessName { get; set; } = string.Empty;
	public string TriggerDisplayText { get; set; } = string.Empty;
	public string BadgeIcon { get; set; } = "🚫";
	public Brush BadgeBgBrush { get; set; } = Brushes.Transparent;
	public Brush BadgeBorderBrush { get; set; } = Brushes.Transparent;
	public Brush BadgeFgBrush { get; set; } = Brushes.Gray;
	public Visibility ResetVisibility { get; set; } = Visibility.Collapsed;
	public TriggerConfig? OverrideTrigger { get; set; }

	public string ConfigButtonText => I18n.T("BtnConfigProcessTrigger");
	public string ConfigButtonTip => I18n.T("TipConfigProcessTrigger");
	public string RestoreButtonText => I18n.T("BtnRestoreProcessPass");
	public string RestoreButtonTip => I18n.T("TipRestoreProcessPass");
	public string DeleteButtonTip => I18n.T("TipRemoveProcessItem");

	private static readonly SolidColorBrush s_defaultBg;
	private static readonly SolidColorBrush s_defaultBorder;
	private static readonly SolidColorBrush s_defaultFg;
	private static readonly SolidColorBrush s_activeBg;
	private static readonly SolidColorBrush s_activeBorder;
	private static readonly SolidColorBrush s_activeFg;

	static BlacklistProcessItemViewModel()
	{
		s_defaultBg = new SolidColorBrush(Color.FromArgb(20, 148, 163, 184));
		s_defaultBg.Freeze();
		s_defaultBorder = new SolidColorBrush(Color.FromArgb(60, 148, 163, 184));
		s_defaultBorder.Freeze();
		s_defaultFg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
		s_defaultFg.Freeze();

		s_activeBg = new SolidColorBrush(Color.FromArgb(30, 16, 185, 129));
		s_activeBg.Freeze();
		s_activeBorder = new SolidColorBrush(Color.FromArgb(120, 16, 185, 129));
		s_activeBorder.Freeze();
		s_activeFg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
		s_activeFg.Freeze();
	}

	public static BlacklistProcessItemViewModel Create(string processName, TriggerConfig? overrideTrigger)
	{
		var vm = new BlacklistProcessItemViewModel
		{
			ProcessName = processName,
			OverrideTrigger = overrideTrigger
		};

		if (overrideTrigger != null)
		{
			vm.BadgeIcon = "🎯";
			vm.TriggerDisplayText = SettingsWindow.FormatTriggerDisplay(overrideTrigger);
			vm.BadgeBgBrush = s_activeBg;
			vm.BadgeBorderBrush = s_activeBorder;
			vm.BadgeFgBrush = s_activeFg;
			vm.ResetVisibility = Visibility.Visible;
		}
		else
		{
			vm.BadgeIcon = "🚫";
			vm.TriggerDisplayText = I18n.T("ProcessTriggerDefaultPass");
			vm.BadgeBgBrush = s_defaultBg;
			vm.BadgeBorderBrush = s_defaultBorder;
			vm.BadgeFgBrush = s_defaultFg;
			vm.ResetVisibility = Visibility.Collapsed;
		}

		return vm;
	}
}
