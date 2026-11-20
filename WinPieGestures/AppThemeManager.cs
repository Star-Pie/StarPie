using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace WinPieGestures;

public static class AppThemeManager
{
	private sealed class WindowThemeState
	{
		public bool PendingDarkMode;
		public bool SourceHooked;
		public bool? AppliedDwmDark;
	}

	private static readonly ConditionalWeakTable<Window, object> ThemedWindows = new();
	private static readonly ConditionalWeakTable<Window, WindowThemeState> WindowDarkStates = new();

	public static bool IsHostThemeWindow(Window window) => window.GetType().Assembly == typeof(AppThemeManager).Assembly || ThemedWindows.TryGetValue(window, out _);
	public static string CurrentEffectiveTheme { get; private set; } = "Light";

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int attrValue, int attrSize);

	public static void ApplyTheme(FrameworkElement rootElement, string themeName)
	{
		if (rootElement == null)
		{
			return;
		}

		Window? themedWindow = rootElement as Window ?? Window.GetWindow(rootElement);
		if (themedWindow != null)
		{
			ThemedWindows.GetValue(themedWindow, static _ => new object());
		}

		bool isDark;
		var art = ArtStyles.ArtStyleResolver.Resolve(ConfigManager.CurrentConfig);
		if (art != null)
		{
			ArtStyles.ArtStyleResources.ApplyForThemeManager(rootElement, art);
			CurrentEffectiveTheme = art.IsDark ? "Dark" : "Light";
			isDark = art.IsDark;
		}
		else
		{
			string text = themeName;
			if (string.Equals(themeName, "System", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(themeName))
			{
				text = IsWindowsInDarkTheme() ? "Dark" : "Light";
			}

			CurrentEffectiveTheme = text;
			isDark = !string.Equals(text, "Light", StringComparison.OrdinalIgnoreCase);
			ApplyStandardThemeDictionary(rootElement, text);
		}

		UiTypography.Apply(rootElement, ConfigManager.CurrentConfig);
		if (themedWindow != null)
		{
			SetWindowDarkMode(themedWindow, isDark);
			HostWindowFrame.Apply(themedWindow);
		}
	}

	private static void ApplyStandardThemeDictionary(FrameworkElement rootElement, string themeName)
	{
		switch (themeName.ToLowerInvariant())
		{
			case "dark":
			case "obsidiandark":
			case "midnightnavy":
			case "royalviolet":
				ArtStyles.ArtStyleResources.ApplyStandardThemeForThemeManager(
					rootElement, "Dark",
					"#090D16", "#0F172A", "#131C2E", "#1E293B", "#F8FAFC", "#CBD5E1", "#94A3B8",
					"#0B1120", "#334155", "#1E293B", "#0F172A", "#00000000", "#CBD5E1", "#1E293B",
					"#F8FAFC", "#1E293B", "#60A5FA", "#1E293B", "#F8FAFC", "#334155", "#334155",
					"#3B82F6", "#60A5FA", "#FFFFFF", "#0B1120", "#1E293B", "#1E293B");
				break;
			case "titaniumgray":
				ArtStyles.ArtStyleResources.ApplyStandardThemeForThemeManager(
					rootElement, "TitaniumGray",
					"#121214", "#18181B", "#202024", "#2E2E33", "#F4F4F5", "#D4D4D8", "#A1A1AA",
					"#141416", "#3F3F46", "#27272A", "#18181B", "#00000000", "#D4D4D8", "#27272A",
					"#F4F4F5", "#27272A", "#E4E4E7", "#27272A", "#F4F4F5", "#3F3F46", "#3F3F46",
					"#3B82F6", "#60A5FA", "#FFFFFF", "#141416", "#2E2E33", "#2E2E33");
				break;
			default:
				ArtStyles.ArtStyleResources.ApplyStandardThemeForThemeManager(
					rootElement, "Light",
					"#F8FAFC", "#FFFFFF", "#FFFFFF", "#E2E8F0", "#0F172A", "#475569", "#64748B",
					"#FFFFFF", "#CBD5E1", "#F1F5F9", "#F8FAFC", "#00000000", "#475569", "#F1F5F9",
					"#0F172A", "#EFF6FF", "#2563EB", "#FFFFFF", "#334155", "#CBD5E1", "#F1F5F9",
					"#2563EB", "#1D4ED8", "#FFFFFF", "#F1F5F9", "#CBD5E1", "#E2E8F0");
				break;
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
			WindowThemeState state = WindowDarkStates.GetValue(window, static _ => new WindowThemeState());
			state.PendingDarkMode = isDark;
			nint handle = new WindowInteropHelper(window).Handle;
			if (handle == IntPtr.Zero)
			{
				if (!state.SourceHooked)
				{
					state.SourceHooked = true;
					window.SourceInitialized += (_, _) => ApplyPendingDarkMode(window, state);
				}
			}
			else
			{
				ApplyPendingDarkMode(window, state, handle);
			}
		}
		catch
		{
		}
	}

	private static void ApplyPendingDarkMode(Window window, WindowThemeState state, nint knownHandle = 0)
	{
		try
		{
			bool targetDark = state.PendingDarkMode;
			if (state.AppliedDwmDark == targetDark)
			{
				return;
			}

			nint handle = knownHandle != IntPtr.Zero ? knownHandle : new WindowInteropHelper(window).Handle;
			if (handle == IntPtr.Zero)
			{
				return;
			}

			int attrValue = targetDark ? 1 : 0;
			DwmSetWindowAttribute(handle, 20, ref attrValue, 4);
			DwmSetWindowAttribute(handle, 19, ref attrValue, 4);
			state.AppliedDwmDark = targetDark;
		}
		catch
		{
		}
	}

	public static bool IsWindowsInDarkTheme()
	{
		try
		{
			using RegistryKey? registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
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

	public static bool IsEffectiveDark(string? themeName)
	{
		var art = ArtStyles.ArtStyleResolver.Resolve(ConfigManager.CurrentConfig);
		if (art != null) return art.IsDark;
		if (string.IsNullOrEmpty(themeName) || string.Equals(themeName, "System", StringComparison.OrdinalIgnoreCase)) return IsWindowsInDarkTheme();
		return !string.Equals(themeName, "Light", StringComparison.OrdinalIgnoreCase);
	}
}
