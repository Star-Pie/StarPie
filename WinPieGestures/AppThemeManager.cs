using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace WinPieGestures;

public static class AppThemeManager
{
	public static string CurrentEffectiveTheme { get; private set; } = "Light";

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int attrValue, int attrSize);

	public static void ApplyTheme(FrameworkElement rootElement, string themeName)
	{
		if (rootElement != null)
		{
			string text = themeName;
			if (string.Equals(themeName, "System", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(themeName))
			{
				text = (IsWindowsInDarkTheme() ? "Dark" : "Light");
			}
			CurrentEffectiveTheme = text;
			bool isDark = !string.Equals(text, "Light", StringComparison.OrdinalIgnoreCase);
			switch (text.ToLowerInvariant())
			{
			case "dark":
			case "obsidiandark":
			case "midnightnavy":
			case "royalviolet":
				SetThemeBrushes(rootElement, "#090D16", "#0F172A", "#131C2E", "#1E293B", "#F8FAFC", "#CBD5E1", "#94A3B8", "#0B1120", "#334155", "#1E293B", "#0F172A", "#00000000", "#CBD5E1", "#1E293B", "#F8FAFC", "#1E293B", "#60A5FA", "#1E293B", "#F8FAFC", "#334155", "#334155", "#3B82F6", "#60A5FA", "#FFFFFF", "#0B1120", "#1E293B", "#1E293B");
				break;
			case "titaniumgray":
				SetThemeBrushes(rootElement, "#121214", "#18181B", "#202024", "#2E2E33", "#F4F4F5", "#D4D4D8", "#A1A1AA", "#141416", "#3F3F46", "#27272A", "#18181B", "#00000000", "#D4D4D8", "#27272A", "#F4F4F5", "#27272A", "#E4E4E7", "#27272A", "#F4F4F5", "#3F3F46", "#3F3F46", "#3B82F6", "#60A5FA", "#FFFFFF", "#141416", "#2E2E33", "#2E2E33");
				break;
			default:
				SetThemeBrushes(rootElement, "#F8FAFC", "#FFFFFF", "#FFFFFF", "#E2E8F0", "#0F172A", "#475569", "#64748B", "#FFFFFF", "#CBD5E1", "#F1F5F9", "#F8FAFC", "#00000000", "#475569", "#F1F5F9", "#0F172A", "#EFF6FF", "#2563EB", "#FFFFFF", "#334155", "#CBD5E1", "#F1F5F9", "#2563EB", "#1D4ED8", "#FFFFFF", "#F1F5F9", "#CBD5E1", "#E2E8F0");
				break;
			}
			Window window = (rootElement as Window) ?? Window.GetWindow((DependencyObject)(object)rootElement);
			if (window != null)
			{
				SetWindowDarkMode(window, isDark);
			}
		}
	}

	public static void SetWindowDarkMode(Window window, bool isDark)
	{
		if (window == null)
		{
			return;
		}
		try
		{
			nint handle = new WindowInteropHelper(window).Handle;
			if (handle == IntPtr.Zero)
			{
				window.SourceInitialized += delegate
				{
					SetWindowDarkMode(window, isDark);
				};
			}
			else
			{
				int attrValue = (isDark ? 1 : 0);
				DwmSetWindowAttribute(handle, 20, ref attrValue, 4);
				DwmSetWindowAttribute(handle, 19, ref attrValue, 4);
			}
		}
		catch
		{
		}
	}

	private static void SetThemeBrushes(FrameworkElement root, string windowBg, string sidebarBg, string cardBg, string cardBorder, string textPrimary, string textSecondary, string textMuted, string inputBg, string inputBorder, string itemHover, string subtleCard, string navTabDefaultBg, string navTabDefaultFg, string navTabHoverBg, string navTabHoverFg, string navTabActiveBg, string navTabActiveFg, string buttonDefaultBg, string buttonDefaultFg, string buttonDefaultBorder, string buttonHoverBg, string accentPrimary, string accentHover, string accentText, string previewCanvasBg, string previewCanvasBorder, string previewGridLine)
	{
		SetBrush(root, "WindowBackgroundBrush", windowBg);
		SetBrush(root, "SidebarBackgroundBrush", sidebarBg);
		SetBrush(root, "CardBackgroundBrush", cardBg);
		SetBrush(root, "CardBorderBrush", cardBorder);
		SetBrush(root, "TextPrimaryBrush", textPrimary);
		SetBrush(root, "TextSecondaryBrush", textSecondary);
		SetBrush(root, "TextMutedBrush", textMuted);
		SetBrush(root, "TextTertiaryBrush", textMuted);
		SetBrush(root, "InputBackgroundBrush", inputBg);
		SetBrush(root, "InputBorderBrush", inputBorder);
		SetBrush(root, "ItemHoverBrush", itemHover);
		SetBrush(root, "SubtleCardBrush", subtleCard);
		SetBrush(root, "NavTabDefaultBgBrush", navTabDefaultBg);
		SetBrush(root, "NavTabDefaultFgBrush", navTabDefaultFg);
		SetBrush(root, "NavTabHoverBgBrush", navTabHoverBg);
		SetBrush(root, "NavTabHoverFgBrush", navTabHoverFg);
		SetBrush(root, "NavTabActiveBgBrush", navTabActiveBg);
		SetBrush(root, "NavTabActiveFgBrush", navTabActiveFg);
		SetBrush(root, "ButtonDefaultBgBrush", buttonDefaultBg);
		SetBrush(root, "ButtonDefaultFgBrush", buttonDefaultFg);
		SetBrush(root, "ButtonDefaultBorderBrush", buttonDefaultBorder);
		SetBrush(root, "ButtonHoverBgBrush", buttonHoverBg);
		SetBrush(root, "AccentPrimaryBrush", accentPrimary);
		SetBrush(root, "AccentHoverBrush", accentHover);
		SetBrush(root, "AccentTextBrush", accentText);
		SetBrush(root, "PreviewCanvasBackgroundBrush", previewCanvasBg);
		SetBrush(root, "PreviewCanvasBorderBrush", previewCanvasBorder);
		SetBrush(root, "PreviewGridLineBrush", previewGridLine);
	}

	private static void SetBrush(FrameworkElement root, string key, string hex)
	{
		SolidColorBrush value = CreateSolidBrush(hex);
		root.Resources[key] = value;
		if (Application.Current != null)
		{
			Application.Current.Resources[key] = value;
		}
	}

	private static SolidColorBrush CreateSolidBrush(string hex)
	{
		SolidColorBrush solidColorBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
		((Freezable)solidColorBrush).Freeze();
		return solidColorBrush;
	}

	public static bool IsWindowsInDarkTheme()
	{
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
			if (registryKey != null && registryKey.GetValue("AppsUseLightTheme") is int num)
			{
				return num == 0;
			}
		}
		catch
		{
		}
		return false;
	}
}
