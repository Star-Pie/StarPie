using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Windows;
using Microsoft.Win32;

namespace WinPieGestures;

public static class ActionExecutor
{
	private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

	private struct MOUSEINPUT
	{
		public int dx;

		public int dy;

		public uint mouseData;

		public uint dwFlags;

		public uint time;

		public nint dwExtraInfo;
	}

	private struct KEYBDINPUT
	{
		public ushort wVk;

		public ushort wScan;

		public uint dwFlags;

		public uint time;

		public nint dwExtraInfo;
	}

	private struct HARDWAREINPUT
	{
		public uint uMsg;

		public ushort wParamL;

		public ushort wParamH;
	}

	[StructLayout(LayoutKind.Explicit)]
	private struct InputUnion
	{
		[FieldOffset(0)]
		public MOUSEINPUT mi;

		[FieldOffset(0)]
		public KEYBDINPUT ki;

		[FieldOffset(0)]
		public HARDWAREINPUT hi;
	}

	private struct INPUT
	{
		public uint type;

		public InputUnion U;
	}

	public class HotkeyStep
	{
		public List<ushort> Modifiers { get; } = new List<ushort>();

		public ushort MainKey { get; set; }

		public string RawToken { get; set; } = string.Empty;

		public override string ToString()
		{
			List<string> parts = new List<string>();
			foreach (ushort mod in Modifiers)
			{
				parts.Add(mod switch
				{
					162 => "Ctrl",
					160 => "Shift",
					164 => "Alt",
					91 => "Win",
					_ => $"Mod({mod})"
				});
			}
			if (MainKey != 0)
			{
				parts.Add($"VK({MainKey})");
			}
			return string.Join("+", parts);
		}
	}

	public class HotkeyDetails
	{
		public List<ushort> Modifiers { get; } = new List<ushort>();

		public List<ushort> SequenceKeys { get; } = new List<ushort>();

		public ushort MainKey { get; set; }

		public List<HotkeyStep> Steps { get; } = new List<HotkeyStep>();
	}

	private const int SW_HIDE = 0;

	private const int SW_SHOWNORMAL = 1;

	private const int SW_SHOWMINIMIZED = 2;

	private const int SW_SHOWMAXIMIZED = 3;

	private const int SW_SHOW = 5;

	private const int SW_MINIMIZE = 6;

	private const int SW_RESTORE = 9;

	private const uint INPUT_KEYBOARD = 1u;

	private const uint KEYEVENTF_KEYUP = 2u;

	private const uint KEYEVENTF_EXTENDEDKEY = 1u;

	private const ushort VK_LCONTROL = 162;

	private const ushort VK_LSHIFT = 160;

	private const ushort VK_LMENU = 164;

	private const ushort VK_LWIN = 91;

	private const ushort VK_VOLUME_MUTE = 173;

	private const ushort VK_VOLUME_DOWN = 174;

	private const ushort VK_VOLUME_UP = 175;

	private const ushort VK_LEFT = 37;

	private const ushort VK_UP = 38;

	private const ushort VK_RIGHT = 39;

	private const ushort VK_DOWN = 40;

	private const ushort VK_ESCAPE = 27;

	private const ushort VK_RETURN = 13;

	private const ushort VK_TAB = 9;

	private const ushort VK_SPACE = 32;

	[DllImport("user32.dll")]
	private static extern bool LockWorkStation();

	[DllImport("shell32.dll", CharSet = CharSet.Auto)]
	private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

	[DllImport("user32.dll")]
	private static extern nint GetForegroundWindow();

	[DllImport("user32.dll", ExactSpelling = true)]
	private static extern nint GetAncestor(nint hwnd, uint gaFlags);

	[DllImport("user32.dll")]
	private static extern bool SetForegroundWindow(nint hWnd);

	[DllImport("user32.dll")]
	private static extern bool ShowWindow(nint hWnd, int nCmdShow);

	[DllImport("user32.dll")]
	private static extern bool IsIconic(nint hWnd);

	[DllImport("user32.dll")]
	private static extern bool IsWindowVisible(nint hWnd);

	[DllImport("user32.dll")]
	private static extern bool BringWindowToTop(nint hWnd);

	[DllImport("user32.dll")]
	private static extern bool EnumWindows(EnumWindowsProc enumProc, nint lParam);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

	[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	private static extern int GetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);

	
	[DllImport("user32.dll")]
	private static extern uint MapVirtualKey(uint uCode, uint uMapType);

	[DllImport("user32.dll")]
	private static extern short GetAsyncKeyState(int nVirtKey);

	[DllImport("user32.dll")]
	private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, nint dwExtraInfo);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

	private static readonly Channel<ActionItem> s_actionChannel = Channel.CreateUnbounded<ActionItem>(new UnboundedChannelOptions
	{
		SingleReader = true,
		SingleWriter = false
	});

	static ActionExecutor()
	{
		Thread worker = new Thread(ProcessActionQueue)
		{
			Name = "StarPie.ActionExecutor",
			IsBackground = true,
			Priority = ThreadPriority.AboveNormal
		};
		worker.Start();
	}

	public static void EnqueueAction(ActionItem action)
	{
		if (action != null)
		{
			s_actionChannel.Writer.TryWrite(action);
		}
	}

	/// <summary>
	/// 设置页「测试」按钮的统一入口：把动作交给后台执行线程，而不是在 UI 线程上就地跑。
	/// <para>
	/// 两个理由缺一不可。① <see cref="Execute"/> 是同步的，而插件动作那条路上 <c>PluginInvoker</c>
	/// 用 <c>task.Wait(超时)</c> 等结果 —— 在 UI 线程上调它就等于让 UI 线程去等一个「要等 UI 线程
	/// 空出来才能完成」的任务。任何 <c>await Dispatcher.InvokeAsync(…)</c> 的插件（也就是要画窗口
	/// 的那一类）点「测试」必然耗满超时，用户看到「执行超时（3s）」，而同一个动作从轮盘上触发完全
	/// 正常。那是最容易被误判成「插件写坏了」的一种假故障。
	/// ② 走 <see cref="EnqueueAction"/> 之后，测试与真实手势跑的是同一条线程，日志里的线程名也因此
	/// 一致 —— 「测试通过」这才对得上「轮盘上也会通过」。
	/// </para>
	/// <para>
	/// 投出去的是 <see cref="ActionItem.Clone"/> 出来的快照而不是界面上那个活的实例：入队意味着执行
	/// 发生在稍后的另一条线程上，而那一刻用户可能已经在继续改这个动作了。
	/// </para>
	/// </summary>
	public static void ExecuteForTesting(ActionItem? action)
	{
		if (action == null)
		{
			return;
		}
		EnqueueAction(action.Clone());
	}

	private static void ProcessActionQueue()
	{
		var reader = s_actionChannel.Reader;
		while (true)
		{
			try
			{
				if (reader.WaitToReadAsync().AsTask().Result)
				{
					while (reader.TryRead(out ActionItem? action))
					{
						if (action != null)
						{
							try
							{
								Execute(action);
							}
							catch
							{
							}
						}
					}
				}
			}
			catch
			{
			}
		}
	}

	public static void Execute(ActionItem action)
	{
		if (action == null)
		{
			return;
		}
		try
		{
			AppLogger.LogInfo($"Executing Action: Name='{action.Name}', Type='{action.Type}', Param='{action.Parameter}', Args='{action.Arguments}', Term='{action.CommandTerminal}'");
			if (Plugins.BuiltinActionCatalog.TryGet(action.Type, out Plugins.BuiltinActionRegistration builtin))
			{
				ExecuteBuiltinActionItem(action, builtin);
				return;
			}

			if (Plugins.PluginHost.TryResolveClaimedType(action.Type, out Plugins.PluginTypeClaimBinding claim))
			{
				ExecuteClaimedActionItem(action, claim);
				return;
			}
			switch (action.Type.Trim())
			{
			case "Hotkey":
				ExecuteHotkey(action.Parameter);
				break;
			case "Text":
			case "String":
				SendTextInput(action.Parameter);
				break;
			case "Plugin":
				// 普通社区插件动作统一走 PluginHost，避免静默失效。
				ExecutePluginActionItem(action);
				break;
			default:
				if (Plugins.PluginHost.IsOfficialClaimedType(action.Type))
				{
					Plugins.PluginHost.NotifyUser(
						"动作不可用",
						"该动作由官方插件提供，但对应插件当前未安装、未启用或不可用。请在插件管理页安装或启用它。");
				}
				else
				{
					AppLogger.LogWarn($"Unknown action type '{action.Type}' was ignored.");
				}
				break;
			}
		}
		catch (Exception ex)
		{
			AppLogger.LogError($"Failed to execute action '{action.Name}' (Type: {action.Type}, Param: {action.Parameter})", ex);
			MessageBox.Show("Failed to execute action '" + action.Name + "': " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Hand);
		}
	}

	/// <summary>
	/// 插件动作的执行包装。
	/// <para>
	/// 刻意做成独立方法而不是直接写在 <c>switch</c> 里，有两个原因：
	/// ① 让「主程序唯一的插件接缝」在代码里一眼可见、可搜索；
	/// ② 所有失败都走 <see cref="AppLogger"/> + 托盘气泡，而<b>不是</b> MessageBox ——
	///    插件是社区代码，它的失败必须可诊断、可忽略，绝不能打断用户。
	/// </para>
	/// </summary>
	internal static void ExecuteBuiltinActionItem(ActionItem action, Plugins.BuiltinActionRegistration registration)
	{
		var parameters = registration.ProjectParameters(action);
		string? invalid = registration.Contribution.Validate(parameters);
		if (!string.IsNullOrWhiteSpace(invalid))
		{
			Plugins.PluginHost.NotifyUser(registration.Contribution.Descriptor.DisplayName, invalid);
			return;
		}

		var input = new StarPie.Plugin.PluginActionInput
		{
			ContributionId = registration.FullId,
			Parameters = parameters,
			Context = new StarPie.Plugin.ActionContext(),
		};
		StarPie.Plugin.ActionResult result = registration.Contribution.ExecuteAsync(input, CancellationToken.None)
			.GetAwaiter().GetResult();
		if (!result.Success)
		{
			throw new InvalidOperationException(result.Message ?? $"内建动作 {registration.FullId} 执行失败。");
		}
	}

	private static void ExecuteClaimedActionItem(ActionItem action, Plugins.PluginTypeClaimBinding claim)
	{
		Plugins.PluginExecuteOutcome outcome = Plugins.PluginHost.ExecuteClaimedAction(action, claim);
		if (!outcome.Success)
		{
			AppLogger.LogWarn($"Claimed action failed: {claim.FullId}, Reason='{outcome.Message}'");
			Plugins.PluginHost.NotifyUser("内置动作执行失败", outcome.Message);
		}
	}
	private static void ExecutePluginActionItem(ActionItem action)
	{
		Plugins.PluginExecuteOutcome outcome = Plugins.PluginHost.ExecutePluginAction(action);

		if (!outcome.Handled)
		{
			AppLogger.LogWarn($"Plugin action not handled: Name='{action.Name}', Ref='{action.PluginActionRef}'");
			return;
		}

		if (outcome.QueuedToBackground)
		{
			AppLogger.LogInfo($"Plugin action queued to background: {action.PluginActionRef}, Name='{action.Name}'");
			return;
		}

		if (outcome.Success)
		{
			AppLogger.LogInfo($"Plugin action succeeded: {action.PluginActionRef}");
			return;
		}

		AppLogger.LogWarn($"Plugin action failed: {action.PluginActionRef}, Reason='{outcome.Message}'");
		Plugins.PluginHost.NotifyUser("插件动作执行失败", outcome.Message);
	}

	public static bool TryToggleProcessWindow(string processOrExePath)
	{
		if (string.IsNullOrWhiteSpace(processOrExePath))
		{
			return false;
		}
		string text = Path.GetFileNameWithoutExtension(processOrExePath).ToLowerInvariant();
		if (text == "explorer" || text == "cmd" || text == "powershell" || text == "wsl" || text == "calc" || text == "calculator" || text == "calculatorapp")
		{
			return false;
		}
		Process[] processesByName = Process.GetProcessesByName(text);
		if ((processesByName == null || processesByName.Length == 0) && text.EndsWith("64"))
		{
			processesByName = Process.GetProcessesByName(text.Substring(0, text.Length - 2));
		}
		if (processesByName == null || processesByName.Length == 0)
		{
			return false;
		}
		nint foregroundWindow = GetForegroundWindow();
		List<nint> windowHandles = new List<nint>();
		Process[] array = processesByName;
		foreach (Process process in array)
		{
			try
			{
				if (process.MainWindowHandle != IntPtr.Zero && IsWindowVisible(process.MainWindowHandle))
				{
					windowHandles.Add(process.MainWindowHandle);
					continue;
				}
				int pid = process.Id;
				EnumWindows(delegate(nint hWnd, nint lParam)
				{
					GetWindowThreadProcessId(hWnd, out var lpdwProcessId);
					if (lpdwProcessId == pid && IsWindowVisible(hWnd))
					{
						StringBuilder stringBuilder = new StringBuilder(256);
						GetWindowText(hWnd, stringBuilder, 256);
						if (stringBuilder.Length > 0)
						{
							windowHandles.Add(hWnd);
						}
					}
					return true;
				}, IntPtr.Zero);
			}
			catch
			{
			}
		}
		if (windowHandles.Count == 0)
		{
			return false;
		}
		foreach (nint item in windowHandles)
		{
			if (item == foregroundWindow && !IsIconic(item))
			{
				ShowWindow(item, 6);
				return true;
			}
		}
		nint num = windowHandles[0];
		if (IsIconic(num))
		{
			ShowWindow(num, 9);
		}
		else
		{
			ShowWindow(num, 5);
		}
		SetForegroundWindow(num);
		BringWindowToTop(num);
		return true;
	}

	public static bool TryToggleFolderWindow(string folderPath)
	{
		if (string.IsNullOrWhiteSpace(folderPath))
		{
			return false;
		}
		string b = folderPath.Trim().Trim('"').TrimEnd('\\', '/');
		try
		{
			Type typeFromProgID = Type.GetTypeFromProgID("Shell.Application");
			if (typeFromProgID != null)
			{
				dynamic val = Activator.CreateInstance(typeFromProgID);
				if (val != null)
				{
					dynamic val2 = val.Windows();
					int num = val2.Count;
					nint foregroundWindow = GetForegroundWindow();
					for (int i = 0; i < num; i++)
					{
						try
						{
							dynamic val3 = val2.Item(i);
							if (!((val3 != null) ? true : false))
							{
								continue;
							}
							string text = val3.LocationURL?.ToString() ?? "";
							if (string.IsNullOrEmpty(text) || !text.StartsWith("file:///", StringComparison.OrdinalIgnoreCase) || !string.Equals(Uri.UnescapeDataString(new Uri(text).LocalPath).TrimEnd('\\', '/'), b, StringComparison.OrdinalIgnoreCase))
							{
								continue;
							}
							nint num2 = (nint)val3.HWND;
							if (num2 == IntPtr.Zero)
							{
								continue;
							}
							if (num2 == foregroundWindow && !IsIconic(num2))
							{
								ShowWindow(num2, 6);
							}
							else
							{
								if (IsIconic(num2))
								{
									ShowWindow(num2, 9);
								}
								else
								{
									ShowWindow(num2, 5);
								}
								SetForegroundWindow(num2);
								BringWindowToTop(num2);
							}
							return true;
						}
						catch
						{
						}
					}
				}
			}
		}
		catch
		{
		}
		return false;
	}

	public static string? FindBrowserExecutable(string browserName)
	{
		string exeName = browserName.ToLowerInvariant() switch
		{
			"chrome" => "chrome.exe",
			"edge" => "msedge.exe",
			"firefox" => "firefox.exe",
			_ => browserName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? browserName : (browserName + ".exe")
		};

		try
		{
			string subKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + exeName;
			using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(subKey))
			{
				if (key?.GetValue(null) is string hklmPath && File.Exists(hklmPath))
				{
					return hklmPath;
				}
			}
			using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subKey))
			{
				if (key?.GetValue(null) is string hkcuPath && File.Exists(hkcuPath))
				{
					return hkcuPath;
				}
			}
		}
		catch
		{
		}

		try
		{
			string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
			string progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
			string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

			List<string> candidatePaths = new List<string>();
			if (browserName.Equals("Chrome", StringComparison.OrdinalIgnoreCase))
			{
				candidatePaths.Add(Path.Combine(progFiles, @"Google\Chrome\Application\chrome.exe"));
				candidatePaths.Add(Path.Combine(progFilesX86, @"Google\Chrome\Application\chrome.exe"));
				candidatePaths.Add(Path.Combine(localAppData, @"Google\Chrome\Application\chrome.exe"));
			}
			else if (browserName.Equals("Edge", StringComparison.OrdinalIgnoreCase))
			{
				candidatePaths.Add(Path.Combine(progFilesX86, @"Microsoft\Edge\Application\msedge.exe"));
				candidatePaths.Add(Path.Combine(progFiles, @"Microsoft\Edge\Application\msedge.exe"));
			}
			else if (browserName.Equals("Firefox", StringComparison.OrdinalIgnoreCase))
			{
				candidatePaths.Add(Path.Combine(progFiles, @"Mozilla Firefox\firefox.exe"));
				candidatePaths.Add(Path.Combine(progFilesX86, @"Mozilla Firefox\firefox.exe"));
			}

			foreach (string path in candidatePaths)
			{
				if (File.Exists(path))
				{
					return path;
				}
			}
		}
		catch
		{
		}

		return null;
	}

	internal static void ExecuteWebUrl(string url, string? browserChoice, string? customBrowserPath)
	{
		if (string.IsNullOrWhiteSpace(url))
		{
			return;
		}
		string target = url.Trim();
		if (!target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
		    !target.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
		    !target.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase))
		{
			target = "https://" + target;
		}

		string browser = browserChoice?.Trim() ?? "Default";
		AppLogger.LogInfo($"Executing WebUrl: URL='{target}', Browser='{browser}', CustomPath='{customBrowserPath}'");

		try
		{
			if (browser.Equals("Chrome", StringComparison.OrdinalIgnoreCase))
			{
				string? chromeExe = FindBrowserExecutable("Chrome");
				Process.Start(new ProcessStartInfo
				{
					FileName = !string.IsNullOrEmpty(chromeExe) ? chromeExe : "chrome.exe",
					Arguments = $"\"{target}\"",
					UseShellExecute = true
				});
			}
			else if (browser.Equals("Edge", StringComparison.OrdinalIgnoreCase))
			{
				string? edgeExe = FindBrowserExecutable("Edge");
				if (!string.IsNullOrEmpty(edgeExe))
				{
					Process.Start(new ProcessStartInfo
					{
						FileName = edgeExe,
						Arguments = $"\"{target}\"",
						UseShellExecute = true
					});
				}
				else
				{
					Process.Start(new ProcessStartInfo
					{
						FileName = "microsoft-edge:" + target,
						UseShellExecute = true
					});
				}
			}
			else if (browser.Equals("Firefox", StringComparison.OrdinalIgnoreCase))
			{
				string? firefoxExe = FindBrowserExecutable("Firefox");
				Process.Start(new ProcessStartInfo
				{
					FileName = !string.IsNullOrEmpty(firefoxExe) ? firefoxExe : "firefox.exe",
					Arguments = $"\"{target}\"",
					UseShellExecute = true
				});
			}
			else if (browser.Equals("Custom", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(customBrowserPath) && File.Exists(customBrowserPath))
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = customBrowserPath,
					Arguments = $"\"{target}\"",
					UseShellExecute = true
				});
			}
			else
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = target,
					UseShellExecute = true
				});
			}
		}
		catch (Exception ex)
		{
			AppLogger.LogError($"Failed to open WebUrl '{target}' with browser '{browser}'", ex);
			try
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = target,
					UseShellExecute = true
				});
			}
			catch (Exception ex2)
			{
				MessageBox.Show("无法打开目标网址: " + ex2.Message, "StarPie", MessageBoxButton.OK, MessageBoxImage.Warning);
			}
		}
	}

	internal static void SafeSetClipboardText(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return;
		}
		try
		{
			if (Application.Current?.Dispatcher != null)
			{
				Application.Current.Dispatcher.Invoke(() =>
				{
					try
					{
						System.Windows.Clipboard.SetDataObject(text, true);
					}
					catch
					{
						System.Windows.Clipboard.SetText(text);
					}
				});
				return;
			}
		}
		catch
		{
		}

		// 备用：若 Dispatcher 不可用或发生跨线程异常，启动独立 STA 线程安全写入
		try
		{
			Thread staThread = new Thread(() =>
			{
				try
				{
					System.Windows.Clipboard.SetDataObject(text, true);
				}
				catch
				{
					try { System.Windows.Clipboard.SetText(text); } catch { }
				}
			});
			staThread.SetApartmentState(ApartmentState.STA);
			staThread.IsBackground = true;
			staThread.Start();
			staThread.Join(500);
		}
		catch
		{
		}
	}

	internal static void ExecuteShellTool(string verb) => ExecuteShellToolCore(verb, StarPie.Plugin.ProcessLaunchMode.Default);

	internal static bool ExecuteShellToolWithMode(string verb, StarPie.Plugin.ProcessLaunchMode mode)
	{
		if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
		if (string.IsNullOrWhiteSpace(verb)) return false;
		if (mode != StarPie.Plugin.ProcessLaunchMode.Default && !SupportsShellToolLaunchMode(verb.Trim()))
			throw new NotSupportedException($"Shell tool '{verb}' does not support an explicit process launch mode.");
		ExecuteShellToolCore(verb, mode);
		return true;
	}

	internal static bool SupportsShellToolLaunchMode(string verb) => verb is
		"VSCode.Open" or "vscode_open" or "Git.BashHere" or "git_bash_here" or
		"Windows.Terminal" or "windows_terminal" or "Windows.CmdHere" or "cmd_here" or
		"Windows.PowerShellHere" or "powershell_here";

	private static void ExecuteShellToolCore(string verb, StarPie.Plugin.ProcessLaunchMode mode)
	{
		if (string.IsNullOrWhiteSpace(verb)) return;
		AppLogger.LogInfo($"Executing ShellTool verb: '{verb}'");

		string v = verb.Trim();
		switch (v)
		{
			case "Windows.CopyAsPath":
			case "copy_path":
			{
				var (folder, selected) = GetActiveExplorerContext();
				if (selected.Count > 0)
				{
					SafeSetClipboardText(string.Join(Environment.NewLine, selected));
				}
				else if (!string.IsNullOrEmpty(folder))
				{
					SafeSetClipboardText(folder);
				}
				break;
			}
			case "StarPie.Builtin.ScreenOCR":
			case "builtin_ocr":
			case "Ocr":
			case "ScreenOcr":
			{
				OcrManager.StartCaptureAndRecognize();
				break;
			}
			case "Windows.RunAs":
			case "run_as_admin":
			{
				var (folder, selected) = GetActiveExplorerContext();
				string targetExe = selected.FirstOrDefault(s => s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
															    s.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) ||
															    s.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
															    s.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase)) ?? "";
				if (!string.IsNullOrEmpty(targetExe))
				{
					Process.Start(new ProcessStartInfo
					{
						FileName = targetExe,
						Verb = "runas",
						UseShellExecute = true,
						WorkingDirectory = Path.GetDirectoryName(targetExe) ?? folder
					});
				}
				else
				{
					Process.Start(new ProcessStartInfo
					{
						FileName = "cmd.exe",
						Verb = "runas",
						UseShellExecute = true,
						WorkingDirectory = folder
					});
				}
				break;
			}
			case "Windows.TaskManager":
			case "task_manager":
			{
				Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true });
				break;
			}
			case "Windows.SnippingTool":
			case "snipping_tool":
			{
				try
				{
					Process.Start(new ProcessStartInfo("ms-screenclip:") { UseShellExecute = true });
				}
				catch
				{
					ExecuteHotkey("Win+Shift+S");
				}
				break;
			}
			case "Windows.NewFolder":
			case "new_folder":
			{
				ExecuteHotkey("Ctrl+Shift+N");
				break;
			}
			case "Windows.Properties":
			case "file_properties":
			{
				ExecuteHotkey("Alt+Enter");
				break;
			}
			case "Windows.Lock":
			case "lock_screen":
			{
				LockWorkStation();
				break;
			}
			case "Windows.EmptyRecycleBin":
			case "empty_recycle_bin":
			{
				SHEmptyRecycleBin(IntPtr.Zero, null, 7u);
				break;
			}
			case "VSCode.Open":
			case "vscode_open":
			{
				var (folder, selected) = GetActiveExplorerContext();
				if (selected.Count > 0)
				{
					StartShellToolProcess(new ProcessStartInfo
					{
						FileName = "code",
						Arguments = string.Join(" ", selected.Select(s => $"\"{s}\"")),
						UseShellExecute = true,
						WorkingDirectory = folder
					}, mode);
				}
				else
				{
					StartShellToolProcess(new ProcessStartInfo
					{
						FileName = "code",
						Arguments = $"\"{folder}\"",
						UseShellExecute = true,
						WorkingDirectory = folder
					}, mode);
				}
				break;
			}
			case "Git.BashHere":
			case "git_bash_here":
			{
				var (folder, _) = GetActiveExplorerContext();
				string[] possibleGitPaths = new[]
				{
					@"C:\Program Files\Git\git-bash.exe",
					@"C:\Program Files (x86)\Git\git-bash.exe",
					Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Git\git-bash.exe")
				};
				string gitExe = possibleGitPaths.FirstOrDefault(File.Exists) ?? "git-bash.exe";
				StartShellToolProcess(new ProcessStartInfo
				{
					FileName = gitExe,
					Arguments = $"--cd=\"{folder}\"",
					UseShellExecute = true,
					WorkingDirectory = folder
				}, mode);
				break;
			}
			case "Windows.Terminal":
			case "windows_terminal":
			{
				var (folder, _) = GetActiveExplorerContext();
				try
				{
					StartShellToolProcess(new ProcessStartInfo
					{
						FileName = "wt.exe",
						Arguments = $"-d \"{folder}\"",
						UseShellExecute = true,
						WorkingDirectory = folder
					}, mode);
				}
				catch when (mode == StarPie.Plugin.ProcessLaunchMode.Default)
				{
					StartShellToolProcess(new ProcessStartInfo
					{
						FileName = "powershell.exe",
						Arguments = $"-NoExit -Command \"Set-Location '{folder}'\"",
						UseShellExecute = true,
						WorkingDirectory = folder
					}, mode);
				}
				break;
			}
			case "Windows.CmdHere":
			case "cmd_here":
			{
				var (folder, _) = GetActiveExplorerContext();
				StartShellToolProcess(new ProcessStartInfo
				{
					FileName = "cmd.exe",
					Arguments = $"/K cd /d \"{folder}\"",
					UseShellExecute = true,
					WorkingDirectory = folder
				}, mode);
				break;
			}
			case "Windows.PowerShellHere":
			case "powershell_here":
			{
				var (folder, _) = GetActiveExplorerContext();
				StartShellToolProcess(new ProcessStartInfo
				{
					FileName = "powershell.exe",
					Arguments = $"-NoExit -Command \"Set-Location '{folder}'\"",
					UseShellExecute = true,
					WorkingDirectory = folder
				}, mode);
				break;
			}
			case "7-Zip.ExtractHere":
			case "7z_extract_here":
			{
				var (folder, selected) = GetActiveExplorerContext();
				string targetArchive = selected.FirstOrDefault(s => IsArchive(s)) ?? "";
				if (string.IsNullOrEmpty(targetArchive))
				{
					Plugins.PluginHost.NotifyUser(
						"7-Zip 解压",
						"未选中压缩包文件：请先在文件资源管理器或桌面上选中要解压的压缩包。");
					break;
				}
				string? sevenZipExe = Find7ZipExecutable();
				if (string.IsNullOrEmpty(sevenZipExe))
				{
					Plugins.PluginHost.NotifyUser(
						"7-Zip 解压",
						"未检测到 7-Zip 安装路径，请确认是否已安装 7-Zip。");
					break;
				}
				try
				{
					Process.Start(new ProcessStartInfo
					{
						FileName = sevenZipExe,
						Arguments = $"x \"{targetArchive}\" -o\"{folder}\" -y",
						UseShellExecute = true,
						WorkingDirectory = folder
					});
				}
				catch (Exception ex)
				{
					AppLogger.LogError($"7-Zip extract failed for '{targetArchive}'", ex);
					Plugins.PluginHost.NotifyUser("7-Zip 解压失败", ex.Message);
				}
				break;
			}
			case "7-Zip.ExtractToFolder":
			case "7z_extract_folder":
			{
				var (folder, selected) = GetActiveExplorerContext();
				string targetArchive = selected.FirstOrDefault(s => IsArchive(s)) ?? "";
				if (string.IsNullOrEmpty(targetArchive))
				{
					Plugins.PluginHost.NotifyUser(
						"7-Zip 解压",
						"未选中压缩包文件：请先在文件资源管理器或桌面上选中要解压的压缩包。");
					break;
				}
				string? sevenZipExe = Find7ZipExecutable();
				if (string.IsNullOrEmpty(sevenZipExe))
				{
					Plugins.PluginHost.NotifyUser(
						"7-Zip 解压",
						"未检测到 7-Zip 安装路径，请确认是否已安装 7-Zip。");
					break;
				}
				try
				{
					string outFolder = Path.Combine(folder, Path.GetFileNameWithoutExtension(targetArchive));
					Process.Start(new ProcessStartInfo
					{
						FileName = sevenZipExe,
						Arguments = $"x \"{targetArchive}\" -o\"{outFolder}\" -y",
						UseShellExecute = true,
						WorkingDirectory = folder
					});
				}
				catch (Exception ex)
				{
					AppLogger.LogError($"7-Zip extract failed for '{targetArchive}'", ex);
					Plugins.PluginHost.NotifyUser("7-Zip 解压失败", ex.Message);
				}
				break;
			}
			case "Bandizip.AutoExtract":
			case "bandizip_extract":
			{
				var (folder, selected) = GetActiveExplorerContext();
				string targetArchive = selected.FirstOrDefault(s => IsArchive(s)) ?? "";
				if (string.IsNullOrEmpty(targetArchive))
				{
					Plugins.PluginHost.NotifyUser(
						"Bandizip 智能解压",
						"未选中压缩包文件：请先在文件资源管理器或桌面上选中要解压的压缩包。");
					break;
				}
				string? bzExe = FindBandizipExecutable();
				if (string.IsNullOrEmpty(bzExe))
				{
					Plugins.PluginHost.NotifyUser(
						"Bandizip 智能解压",
						"未检测到 Bandizip 安装路径，请确认是否已安装 Bandizip。");
					break;
				}
				try
				{
					// Bandizip 官方规范：解压动词为 x，-target:auto 为智能解压模式
					string bzArgs = bzExe.EndsWith("bz.exe", StringComparison.OrdinalIgnoreCase)
						? $"x -y -target:auto -o:\"{folder}\" \"{targetArchive}\""
						: $"x -target:auto -o:\"{folder}\" \"{targetArchive}\"";

					Process.Start(new ProcessStartInfo
					{
						FileName = bzExe,
						Arguments = bzArgs,
						UseShellExecute = true,
						WorkingDirectory = folder
					});
				}
				catch (Exception ex)
				{
					AppLogger.LogError($"Bandizip extract failed for '{targetArchive}'", ex);
					Plugins.PluginHost.NotifyUser("Bandizip 解压失败", ex.Message);
				}
				break;
			}
			case "WinRAR.ExtractHere":
			case "winrar_extract":
			{
				var (folder, selected) = GetActiveExplorerContext();
				string targetArchive = selected.FirstOrDefault(s => IsArchive(s)) ?? "";
				if (string.IsNullOrEmpty(targetArchive))
				{
					Plugins.PluginHost.NotifyUser(
						"WinRAR 解压",
						"未选中压缩包文件：请先在文件资源管理器或桌面上选中要解压的压缩包。");
					break;
				}
				string? winrarExe = FindWinRarExecutable();
				if (string.IsNullOrEmpty(winrarExe))
				{
					Plugins.PluginHost.NotifyUser(
						"WinRAR 解压",
						"未检测到 WinRAR 安装路径，请确认是否已安装 WinRAR。");
					break;
				}
				try
				{
					Process.Start(new ProcessStartInfo
					{
						FileName = winrarExe,
						Arguments = $"x -ibck -y \"{targetArchive}\" \"{folder}\\\"",
						UseShellExecute = true,
						WorkingDirectory = folder
					});
				}
				catch (Exception ex)
				{
					AppLogger.LogError($"WinRAR extract failed for '{targetArchive}'", ex);
					Plugins.PluginHost.NotifyUser("WinRAR 解压失败", ex.Message);
				}
				break;
			}
			case "Windows.ExtractHere":
			case "windows_extract":
			{
				var (folder, selected) = GetActiveExplorerContext();
				string targetArchive = selected.FirstOrDefault(s => IsArchive(s)) ?? "";
				if (string.IsNullOrEmpty(targetArchive))
				{
					Plugins.PluginHost.NotifyUser(
						"Windows 原生解压",
						"未选中压缩包文件：请先在文件资源管理器或桌面上选中要解压的压缩包。");
					break;
				}
				System.Threading.Tasks.Task.Run(() =>
				{
					try
					{
						string outDir = Path.Combine(folder, Path.GetFileNameWithoutExtension(targetArchive));
						Directory.CreateDirectory(outDir);
						ZipFile.ExtractToDirectory(targetArchive, outDir, overwriteFiles: true);
						Plugins.PluginHost.NotifyUser("解压完成", $"已成功解压至：{Path.GetFileName(outDir)}");
					}
					catch (Exception ex)
					{
						AppLogger.LogError($"Windows.ExtractHere failed for '{targetArchive}'", ex);
						Plugins.PluginHost.NotifyUser("Windows 原生解压失败", ex.Message);
					}
				});
				break;
			}

			// --- 压缩扩展 (Compression) ---
			case "7-Zip.CompressZip":
			case "7z_compress_zip":
			{
				var (folder, selected) = GetActiveExplorerContext();
				if (selected.Count == 0)
				{
					Plugins.PluginHost.NotifyUser("7-Zip 压缩", "请先在资源管理器或桌面上选中要压缩的文件或文件夹。");
					break;
				}
				string? sevenZipExe = Find7ZipExecutable();
				if (string.IsNullOrEmpty(sevenZipExe))
				{
					Plugins.PluginHost.NotifyUser("7-Zip 压缩", "未检测到 7-Zip 安装路径。");
					break;
				}
				try
				{
					string targetZip = GetUniqueArchiveDestination(GenerateArchiveDestination(folder, selected, ".zip"));
					string itemsArg = string.Join(" ", selected.Select(s => $"\"{s}\""));
					Process.Start(new ProcessStartInfo
					{
						FileName = sevenZipExe,
						Arguments = $"a -tzip \"{targetZip}\" {itemsArg} -y",
						UseShellExecute = true,
						WorkingDirectory = folder
					});
				}
				catch (Exception ex)
				{
					AppLogger.LogError("7-Zip compress zip failed", ex);
					Plugins.PluginHost.NotifyUser("7-Zip 压缩失败", ex.Message);
				}
				break;
			}
			case "7-Zip.Compress7z":
			case "7z_compress_7z":
			{
				var (folder, selected) = GetActiveExplorerContext();
				if (selected.Count == 0)
				{
					Plugins.PluginHost.NotifyUser("7-Zip 压缩", "请先在资源管理器或桌面上选中要压缩的文件或文件夹。");
					break;
				}
				string? sevenZipExe = Find7ZipExecutable();
				if (string.IsNullOrEmpty(sevenZipExe))
				{
					Plugins.PluginHost.NotifyUser("7-Zip 压缩", "未检测到 7-Zip 安装路径。");
					break;
				}
				try
				{
					string target7z = GetUniqueArchiveDestination(GenerateArchiveDestination(folder, selected, ".7z"));
					string itemsArg = string.Join(" ", selected.Select(s => $"\"{s}\""));
					Process.Start(new ProcessStartInfo
					{
						FileName = sevenZipExe,
						Arguments = $"a -t7z \"{target7z}\" {itemsArg} -y",
						UseShellExecute = true,
						WorkingDirectory = folder
					});
				}
				catch (Exception ex)
				{
					AppLogger.LogError("7-Zip compress 7z failed", ex);
					Plugins.PluginHost.NotifyUser("7-Zip 压缩失败", ex.Message);
				}
				break;
			}
			case "7-Zip.CompressGui":
			case "7z_compress_gui":
			{
				var (folder, selected) = GetActiveExplorerContext();
				if (selected.Count == 0)
				{
					Plugins.PluginHost.NotifyUser("7-Zip 压缩", "请先在资源管理器或桌面上选中要压缩的文件或文件夹。");
					break;
				}
				string? sevenZipExe = Find7ZipExecutable();
				if (string.IsNullOrEmpty(sevenZipExe))
				{
					Plugins.PluginHost.NotifyUser("7-Zip 压缩", "未检测到 7-Zip 安装路径。");
					break;
				}
				try
				{
					string target7z = GetUniqueArchiveDestination(GenerateArchiveDestination(folder, selected, ".7z"));
					string itemsArg = string.Join(" ", selected.Select(s => $"\"{s}\""));
					Process.Start(new ProcessStartInfo
					{
						FileName = sevenZipExe,
						Arguments = $"a -ad \"{target7z}\" -- {itemsArg}",
						UseShellExecute = true,
						WorkingDirectory = folder
					});
				}
				catch (Exception ex)
				{
					AppLogger.LogError("7-Zip compress GUI failed", ex);
					Plugins.PluginHost.NotifyUser("7-Zip 压缩失败", ex.Message);
				}
				break;
			}
			case "Bandizip.CompressZip":
			case "bandizip_compress_zip":
			{
				var (folder, selected) = GetActiveExplorerContext();
				if (selected.Count == 0)
				{
					Plugins.PluginHost.NotifyUser("Bandizip 压缩", "请先在资源管理器或桌面上选中要压缩的文件或文件夹。");
					break;
				}
				string? bzExe = FindBandizipExecutable();
				if (string.IsNullOrEmpty(bzExe))
				{
					Plugins.PluginHost.NotifyUser("Bandizip 压缩", "未检测到 Bandizip 安装路径。");
					break;
				}
				try
				{
					string targetZip = GetUniqueArchiveDestination(GenerateArchiveDestination(folder, selected, ".zip"));
					string itemsArg = string.Join(" ", selected.Select(s => $"\"{s}\""));
					Process.Start(new ProcessStartInfo
					{
						FileName = bzExe,
						Arguments = $"c -y -fmt:zip \"{targetZip}\" {itemsArg}",
						UseShellExecute = true,
						WorkingDirectory = folder
					});
				}
				catch (Exception ex)
				{
					AppLogger.LogError("Bandizip compress zip failed", ex);
					Plugins.PluginHost.NotifyUser("Bandizip 压缩失败", ex.Message);
				}
				break;
			}
			case "Bandizip.Compress7z":
			case "bandizip_compress_7z":
			{
				var (folder, selected) = GetActiveExplorerContext();
				if (selected.Count == 0)
				{
					Plugins.PluginHost.NotifyUser("Bandizip 压缩", "请先在资源管理器或桌面上选中要压缩的文件或文件夹。");
					break;
				}
				string? bzExe = FindBandizipExecutable();
				if (string.IsNullOrEmpty(bzExe))
				{
					Plugins.PluginHost.NotifyUser("Bandizip 压缩", "未检测到 Bandizip 安装路径。");
					break;
				}
				try
				{
					string target7z = GetUniqueArchiveDestination(GenerateArchiveDestination(folder, selected, ".7z"));
					string itemsArg = string.Join(" ", selected.Select(s => $"\"{s}\""));
					Process.Start(new ProcessStartInfo
					{
						FileName = bzExe,
						Arguments = $"c -y -fmt:7z \"{target7z}\" {itemsArg}",
						UseShellExecute = true,
						WorkingDirectory = folder
					});
				}
				catch (Exception ex)
				{
					AppLogger.LogError("Bandizip compress 7z failed", ex);
					Plugins.PluginHost.NotifyUser("Bandizip 压缩失败", ex.Message);
				}
				break;
			}
			case "Bandizip.CompressGui":
			case "bandizip_compress_gui":
			{
				var (folder, selected) = GetActiveExplorerContext();
				if (selected.Count == 0)
				{
					Plugins.PluginHost.NotifyUser("Bandizip 压缩", "请先在资源管理器或桌面上选中要压缩的文件或文件夹。");
					break;
				}
				string? bzExe = FindBandizipExecutable();
				if (string.IsNullOrEmpty(bzExe))
				{
					Plugins.PluginHost.NotifyUser("Bandizip 压缩", "未检测到 Bandizip 安装路径。");
					break;
				}
				try
				{
					string itemsArg = string.Join(" ", selected.Select(s => $"\"{s}\""));
					Process.Start(new ProcessStartInfo
					{
						FileName = bzExe,
						Arguments = $"cd {itemsArg}",
						UseShellExecute = true,
						WorkingDirectory = folder
					});
				}
				catch (Exception ex)
				{
					AppLogger.LogError("Bandizip compress GUI failed", ex);
					Plugins.PluginHost.NotifyUser("Bandizip 压缩失败", ex.Message);
				}
				break;
			}
			case "WinRAR.CompressRar":
			case "winrar_compress_rar":
			{
				var (folder, selected) = GetActiveExplorerContext();
				if (selected.Count == 0)
				{
					Plugins.PluginHost.NotifyUser("WinRAR 压缩", "请先在资源管理器或桌面上选中要压缩的文件或文件夹。");
					break;
				}
				string? winrarExe = FindWinRarExecutable();
				if (string.IsNullOrEmpty(winrarExe))
				{
					Plugins.PluginHost.NotifyUser("WinRAR 压缩", "未检测到 WinRAR 安装路径。");
					break;
				}
				try
				{
					string targetRar = GetUniqueArchiveDestination(GenerateArchiveDestination(folder, selected, ".rar"));
					string itemsArg = string.Join(" ", selected.Select(s => $"\"{s}\""));
					Process.Start(new ProcessStartInfo
					{
						FileName = winrarExe,
						Arguments = $"a -ibck -r \"{targetRar}\" {itemsArg}",
						UseShellExecute = true,
						WorkingDirectory = folder
					});
				}
				catch (Exception ex)
				{
					AppLogger.LogError("WinRAR compress rar failed", ex);
					Plugins.PluginHost.NotifyUser("WinRAR 压缩失败", ex.Message);
				}
				break;
			}
			case "WinRAR.CompressZip":
			case "winrar_compress_zip":
			{
				var (folder, selected) = GetActiveExplorerContext();
				if (selected.Count == 0)
				{
					Plugins.PluginHost.NotifyUser("WinRAR 压缩", "请先在资源管理器或桌面上选中要压缩的文件或文件夹。");
					break;
				}
				string? winrarExe = FindWinRarExecutable();
				if (string.IsNullOrEmpty(winrarExe))
				{
					Plugins.PluginHost.NotifyUser("WinRAR 压缩", "未检测到 WinRAR 安装路径。");
					break;
				}
				try
				{
					string targetZip = GetUniqueArchiveDestination(GenerateArchiveDestination(folder, selected, ".zip"));
					string itemsArg = string.Join(" ", selected.Select(s => $"\"{s}\""));
					Process.Start(new ProcessStartInfo
					{
						FileName = winrarExe,
						Arguments = $"a -afzip -ibck -r \"{targetZip}\" {itemsArg}",
						UseShellExecute = true,
						WorkingDirectory = folder
					});
				}
				catch (Exception ex)
				{
					AppLogger.LogError("WinRAR compress zip failed", ex);
					Plugins.PluginHost.NotifyUser("WinRAR 压缩失败", ex.Message);
				}
				break;
			}
			case "WinRAR.CompressGui":
			case "winrar_compress_gui":
			{
				var (folder, selected) = GetActiveExplorerContext();
				if (selected.Count == 0)
				{
					Plugins.PluginHost.NotifyUser("WinRAR 压缩", "请先在资源管理器或桌面上选中要压缩的文件或文件夹。");
					break;
				}
				string? winrarExe = FindWinRarExecutable();
				if (string.IsNullOrEmpty(winrarExe))
				{
					Plugins.PluginHost.NotifyUser("WinRAR 压缩", "未检测到 WinRAR 安装路径。");
					break;
				}
				try
				{
					string itemsArg = string.Join(" ", selected.Select(s => $"\"{s}\""));
					Process.Start(new ProcessStartInfo
					{
						FileName = winrarExe,
						Arguments = $"a {itemsArg}",
						UseShellExecute = true,
						WorkingDirectory = folder
					});
				}
				catch (Exception ex)
				{
					AppLogger.LogError("WinRAR compress GUI failed", ex);
					Plugins.PluginHost.NotifyUser("WinRAR 压缩失败", ex.Message);
				}
				break;
			}
			case "Windows.CompressZip":
			case "windows_compress_zip":
			{
				var (folder, selected) = GetActiveExplorerContext();
				if (selected.Count == 0)
				{
					Plugins.PluginHost.NotifyUser("Windows 原生压缩", "请先在资源管理器或桌面上选中要压缩的文件或文件夹。");
					break;
				}
				string targetZip = GetUniqueArchiveDestination(GenerateArchiveDestination(folder, selected, ".zip"));
				System.Threading.Tasks.Task.Run(() =>
				{
					try
					{
						if (selected.Count == 1 && Directory.Exists(selected[0]))
						{
							ZipFile.CreateFromDirectory(selected[0], targetZip, CompressionLevel.Optimal, false);
						}
						else
						{
							using var zip = ZipFile.Open(targetZip, ZipArchiveMode.Create);
							foreach (var item in selected)
							{
								if (File.Exists(item))
								{
									zip.CreateEntryFromFile(item, Path.GetFileName(item));
								}
								else if (Directory.Exists(item))
								{
									string rootDirName = Path.GetFileName(item.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
									foreach (var file in Directory.EnumerateFiles(item, "*", SearchOption.AllDirectories))
									{
										string rel = Path.GetRelativePath(item, file);
										zip.CreateEntryFromFile(file, Path.Combine(rootDirName, rel));
									}
								}
							}
						}
						Plugins.PluginHost.NotifyUser("Windows 原生压缩完成", $"已成功生成：{Path.GetFileName(targetZip)}");
					}
					catch (Exception ex)
					{
						AppLogger.LogError($"Windows.CompressZip failed for '{targetZip}'", ex);
						Plugins.PluginHost.NotifyUser("Windows 原生压缩失败", ex.Message);
					}
				});
				break;
			}

			// --- 高频右键与系统扩展 (Explorer & System Essentials) ---
			case "Windows.CopyFileName":
			case "copy_filename":
			{
				var (_, selected) = GetActiveExplorerContext();
				if (selected.Count > 0)
				{
					SafeSetClipboardText(string.Join(Environment.NewLine, selected.Select(s => Path.GetFileName(s.TrimEnd('\\', '/')))));
				}
				break;
			}
			case "Windows.OpenWithNotepad":
			case "open_with_notepad":
			{
				var (folder, selected) = GetActiveExplorerContext();
				if (selected.Count > 0)
				{
					foreach (var s in selected)
					{
						Process.Start(new ProcessStartInfo("notepad.exe", $"\"{s}\"") { UseShellExecute = true, WorkingDirectory = folder });
					}
				}
				else
				{
					Process.Start(new ProcessStartInfo("notepad.exe") { UseShellExecute = true });
				}
				break;
			}
			case "Windows.OpenWithDefault":
			case "open_with_default":
			{
				var (folder, selected) = GetActiveExplorerContext();
				if (selected.Count > 0)
				{
					foreach (var s in selected)
					{
						Process.Start(new ProcessStartInfo(s) { UseShellExecute = true, WorkingDirectory = folder });
					}
				}
				break;
			}
			case "Windows.ComputeSha256":
			case "compute_sha256":
			{
				var (_, selected) = GetActiveExplorerContext();
				string targetFile = selected.FirstOrDefault(File.Exists) ?? "";
				if (string.IsNullOrEmpty(targetFile))
				{
					Plugins.PluginHost.NotifyUser("SHA-256 计算", "请先在资源管理器中选中一个文件。");
					break;
				}
				System.Threading.Tasks.Task.Run(() =>
				{
					try
					{
						using var stream = File.OpenRead(targetFile);
						using var sha256 = SHA256.Create();
						byte[] hashBytes = sha256.ComputeHash(stream);
						string hashStr = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
						SafeSetClipboardText(hashStr);
						Plugins.PluginHost.NotifyUser("SHA-256 计算完成已复制", $"{Path.GetFileName(targetFile)}:\n{hashStr}");
					}
					catch (Exception ex)
					{
						Plugins.PluginHost.NotifyUser("SHA-256 计算失败", ex.Message);
					}
				});
				break;
			}
			case "Windows.ComputeMd5":
			case "compute_md5":
			{
				var (_, selected) = GetActiveExplorerContext();
				string targetFile = selected.FirstOrDefault(File.Exists) ?? "";
				if (string.IsNullOrEmpty(targetFile))
				{
					Plugins.PluginHost.NotifyUser("MD5 计算", "请先在资源管理器中选中一个文件。");
					break;
				}
				System.Threading.Tasks.Task.Run(() =>
				{
					try
					{
						using var stream = File.OpenRead(targetFile);
						using var md5 = MD5.Create();
						byte[] hashBytes = md5.ComputeHash(stream);
						string hashStr = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
						SafeSetClipboardText(hashStr);
						Plugins.PluginHost.NotifyUser("MD5 计算完成已复制", $"{Path.GetFileName(targetFile)}:\n{hashStr}");
					}
					catch (Exception ex)
					{
						Plugins.PluginHost.NotifyUser("MD5 计算失败", ex.Message);
					}
				});
				break;
			}
			case "Windows.ClassicContextMenu":
			case "classic_context_menu":
			{
				ExecuteHotkey("Shift+F10");
				break;
			}
			case "Windows.PermanentDelete":
			case "permanent_delete":
			{
				ExecuteHotkey("Shift+Delete");
				break;
			}
			case "Windows.SendToDesktop":
			case "send_to_desktop":
			{
				var (_, selected) = GetActiveExplorerContext();
				if (selected.Count == 0)
				{
					Plugins.PluginHost.NotifyUser("发送到桌面快捷方式", "请先在资源管理器中选中要发送的文件或文件夹。");
					break;
				}
				try
				{
					string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
					Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
					if (shellType != null)
					{
						dynamic? wsh = Activator.CreateInstance(shellType);
						if (wsh != null)
						{
							foreach (var item in selected)
							{
								string itemName = Path.GetFileName(item.TrimEnd('\\', '/'));
								string linkPath = Path.Combine(desktopPath, $"{itemName} - 快捷方式.lnk");
								dynamic shortcut = wsh.CreateShortcut(linkPath);
								shortcut.TargetPath = item;
								shortcut.WorkingDirectory = Directory.Exists(item) ? item : (Path.GetDirectoryName(item) ?? "");
								shortcut.Save();
							}
							Plugins.PluginHost.NotifyUser("创建快捷方式成功", "已成功在桌面上创建快捷方式。");
						}
					}
				}
				catch (Exception ex)
				{
					AppLogger.LogError("Send to desktop failed", ex);
					Plugins.PluginHost.NotifyUser("创建快捷方式失败", ex.Message);
				}
				break;
			}
			case "Windows.ToggleHidden":
			case "toggle_hidden":
			{
				var (_, selected) = GetActiveExplorerContext();
				if (selected.Count == 0)
				{
					Plugins.PluginHost.NotifyUser("切换隐藏属性", "请先选中要切换属性的文件或文件夹。");
					break;
				}
				try
				{
					foreach (var item in selected)
					{
						var attr = File.GetAttributes(item);
						if ((attr & FileAttributes.Hidden) == FileAttributes.Hidden)
						{
							File.SetAttributes(item, attr & ~FileAttributes.Hidden);
						}
						else
						{
							File.SetAttributes(item, attr | FileAttributes.Hidden);
						}
					}
				}
				catch (Exception ex)
				{
					AppLogger.LogError("Toggle hidden attribute failed", ex);
				}
				break;
			}
			default:
				AppLogger.LogWarn($"Unknown ShellTool verb: '{verb}'");
				break;
		}
	}

	private static (string folder, List<string> selectedPaths) GetActiveExplorerContext()
	{
		string folder = "";
		List<string> selected = new List<string>();
		try
		{
			nint fgHwnd = GetForegroundWindow();
			nint rootHwnd = fgHwnd != IntPtr.Zero ? GetAncestor(fgHwnd, 2 /* GA_ROOT */) : IntPtr.Zero;

			Type? shellType = Type.GetTypeFromProgID("Shell.Application");
			if (shellType != null)
			{
				dynamic? shell = Activator.CreateInstance(shellType);
				if (shell != null)
				{
					dynamic windows = shell.Windows();
					int count = windows.Count;
					dynamic? bestFallbackDoc = null;

					// 1. 遍历打开的资源管理器窗口与标签页
					for (int i = 0; i < count; i++)
					{
						try
						{
							dynamic item = windows.Item(i);
							if (item == null) continue;
							long hwnd = item.HWND;
							nint itemHwnd = (nint)hwnd;

							// 匹配当前活动窗口或其根窗口句柄
							if (itemHwnd == fgHwnd || (rootHwnd != IntPtr.Zero && itemHwnd == rootHwnd))
							{
								dynamic doc = item.Document;
								if (doc != null)
								{
									dynamic sel = doc.SelectedItems();
									if (sel != null && sel.Count > 0)
									{
										// 找到了包含选中项的活动标签页，优先采纳！
										folder = doc.Folder?.Self?.Path ?? "";
										int selCount = sel.Count;
										for (int j = 0; j < selCount; j++)
										{
											string p = sel.Item(j)?.Path ?? "";
											if (!string.IsNullOrEmpty(p)) selected.Add(p);
										}
										if (selected.Count > 0)
										{
											break;
										}
									}
									else if (bestFallbackDoc == null)
									{
										bestFallbackDoc = doc;
									}
								}
							}
						}
						catch { }
					}

					// 如果在匹配窗口中没有选中文件，使用其目录作为工作目录
					if (selected.Count == 0 && bestFallbackDoc != null)
					{
						try
						{
							folder = bestFallbackDoc.Folder?.Self?.Path ?? "";
						}
						catch { }
					}

					// 2. 桌面场景探测：如果前面没取到任何选中文件，检查 Windows 桌面选中项
					if (selected.Count == 0)
					{
						try
						{
							// Shell.Application.Windows().Item() 无参调用返回桌面的 ShellFolderView
							dynamic? desk = windows.Item();
							if (desk != null && desk.Document != null)
							{
								string deskFolder = desk.Document.Folder?.Self?.Path ?? "";
								dynamic deskSel = desk.Document.SelectedItems();
								if (deskSel != null && deskSel.Count > 0)
								{
									int dCount = deskSel.Count;
									for (int j = 0; j < dCount; j++)
									{
										string p = deskSel.Item(j)?.Path ?? "";
										if (!string.IsNullOrEmpty(p)) selected.Add(p);
									}
									if (selected.Count > 0 && !string.IsNullOrEmpty(deskFolder))
									{
										folder = deskFolder;
									}
								}
							}
						}
						catch { }
					}
				}
			}
		}
		catch { }

		if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
		{
			folder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
		}
		return (folder, selected);
	}

	private static bool IsArchive(string path)
	{
		if (string.IsNullOrEmpty(path)) return false;
		string ext = Path.GetExtension(path).ToLowerInvariant();
		return ext is ".zip" or ".7z" or ".rar" or ".tar" or ".gz" or ".bz2" or ".xz" or ".iso"
			or ".tgz" or ".tbz" or ".tbz2" or ".txz" or ".zst" or ".zstd" or ".cab" or ".wim"
			or ".apk" or ".jar" or ".lzma" or ".lz4" or ".001";
	}

	private static string GenerateArchiveDestination(string folder, List<string> selected, string extension)
	{
		if (selected.Count == 1)
		{
			string item = selected[0];
			string baseName = Directory.Exists(item)
				? Path.GetFileName(item.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
				: Path.GetFileNameWithoutExtension(item);
			if (string.IsNullOrEmpty(baseName)) baseName = "Archive";
			return Path.Combine(folder, baseName + extension);
		}
		else if (selected.Count > 1)
		{
			string firstItem = selected[0];
			string baseName = Directory.Exists(firstItem)
				? Path.GetFileName(firstItem.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
				: Path.GetFileNameWithoutExtension(firstItem);
			if (string.IsNullOrEmpty(baseName)) baseName = "Archive";
			return Path.Combine(folder, $"{baseName}_等{selected.Count}项" + extension);
		}
		else
		{
			string folderName = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
			if (string.IsNullOrEmpty(folderName)) folderName = "Archive";
			return Path.Combine(folder, folderName + extension);
		}
	}

	private static string GetUniqueArchiveDestination(string targetArchive)
	{
		if (!File.Exists(targetArchive)) return targetArchive;
		string dir = Path.GetDirectoryName(targetArchive) ?? "";
		string name = Path.GetFileNameWithoutExtension(targetArchive);
		string ext = Path.GetExtension(targetArchive);
		int counter = 1;
		while (File.Exists(Path.Combine(dir, $"{name} ({counter}){ext}")))
		{
			counter++;
		}
		return Path.Combine(dir, $"{name} ({counter}){ext}");
	}

	private static string? _cachedBandizipExe;
	private static string? _cached7ZipExe;
	private static string? _cachedWinRarExe;

	public static string? FindBandizipExecutable()
	{
		if (!string.IsNullOrEmpty(_cachedBandizipExe) && File.Exists(_cachedBandizipExe))
		{
			return _cachedBandizipExe;
		}

		string? found = QueryAppPathFromRegistry("Bandizip.exe", new[]
		{
			@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\Bandizip.exe",
			@"SOFTWARE\Bandisoft\Bandizip",
			@"SOFTWARE\WOW6432Node\Bandisoft\Bandizip"
		});

		if (string.IsNullOrEmpty(found) || !File.Exists(found))
		{
			found = ScanDrivesForExecutable(new[]
			{
				@"Program Files\Bandizip\Bandizip.exe",
				@"Program Files\Bandizip\bz.exe",
				@"Program Files (x86)\Bandizip\Bandizip.exe",
				@"Program Files (x86)\Bandizip\bz.exe",
				@"Bandzip\Bandizip\Bandizip.exe",
				@"Bandzip\Bandizip\bz.exe",
				@"Bandizip\Bandizip.exe",
				@"Bandizip\bz.exe"
			});
		}

		if (string.IsNullOrEmpty(found) || !File.Exists(found))
		{
			found = FindExecutableInPath("Bandizip.exe") ?? FindExecutableInPath("bz.exe");
		}

		if (!string.IsNullOrEmpty(found) && File.Exists(found))
		{
			_cachedBandizipExe = found;
			return found;
		}

		return null;
	}

	public static string? Find7ZipExecutable()
	{
		if (!string.IsNullOrEmpty(_cached7ZipExe) && File.Exists(_cached7ZipExe))
		{
			return _cached7ZipExe;
		}

		string? found = QueryAppPathFromRegistry("7zG.exe", new[]
		{
			@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\7zFM.exe",
			@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\7z.exe",
			@"SOFTWARE\7-Zip",
			@"SOFTWARE\WOW6432Node\7-Zip"
		});

		if (string.IsNullOrEmpty(found) || !File.Exists(found))
		{
			found = ScanDrivesForExecutable(new[]
			{
				@"Program Files\7-Zip-Zstandard\7zG.exe",
				@"Program Files\7-Zip-Zstandard\7z.exe",
				@"Program Files\7-Zip\7zG.exe",
				@"Program Files\7-Zip\7z.exe",
				@"Program Files (x86)\7-Zip\7zG.exe",
				@"Program Files (x86)\7-Zip\7z.exe",
				@"7-Zip\7zG.exe",
				@"7-Zip\7z.exe"
			});
		}

		if (string.IsNullOrEmpty(found) || !File.Exists(found))
		{
			found = FindExecutableInPath("7zG.exe") ?? FindExecutableInPath("7z.exe");
		}

		if (!string.IsNullOrEmpty(found) && File.Exists(found))
		{
			_cached7ZipExe = found;
			return found;
		}

		return null;
	}

	public static string? FindWinRarExecutable()
	{
		if (!string.IsNullOrEmpty(_cachedWinRarExe) && File.Exists(_cachedWinRarExe))
		{
			return _cachedWinRarExe;
		}

		string? found = QueryAppPathFromRegistry("WinRAR.exe", new[]
		{
			@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\WinRAR.exe",
			@"SOFTWARE\WinRAR",
			@"SOFTWARE\WOW6432Node\WinRAR"
		});

		if (string.IsNullOrEmpty(found) || !File.Exists(found))
		{
			found = ScanDrivesForExecutable(new[]
			{
				@"Program Files\WinRAR\WinRAR.exe",
				@"Program Files (x86)\WinRAR\WinRAR.exe",
				@"WinRAR\WinRAR.exe"
			});
		}

		if (string.IsNullOrEmpty(found) || !File.Exists(found))
		{
			found = FindExecutableInPath("WinRAR.exe");
		}

		if (!string.IsNullOrEmpty(found) && File.Exists(found))
		{
			_cachedWinRarExe = found;
			return found;
		}

		return null;
	}

	private static string? QueryAppPathFromRegistry(string targetFileName, string[] subKeys)
	{
		RegistryKey[] roots = new[] { Registry.CurrentUser, Registry.LocalMachine };
		string[] valueNames = new[] { "", "Path", "InstallLocation", "exe64", "exe32" };

		foreach (var root in roots)
		{
			foreach (var subKey in subKeys)
			{
				try
				{
					using var key = root.OpenSubKey(subKey);
					if (key == null) continue;

					foreach (var valName in valueNames)
					{
						object? val = key.GetValue(valName);
						if (val is string str && !string.IsNullOrWhiteSpace(str))
						{
							string candidate = str.Trim('\"', ' ');
							if (File.Exists(candidate))
							{
								if (targetFileName.Equals("7zG.exe", StringComparison.OrdinalIgnoreCase) &&
								    !candidate.EndsWith("7zG.exe", StringComparison.OrdinalIgnoreCase))
								{
									string dir = Path.GetDirectoryName(candidate) ?? "";
									string gPath = Path.Combine(dir, "7zG.exe");
									if (File.Exists(gPath)) return gPath;
									string cPath = Path.Combine(dir, "7z.exe");
									if (File.Exists(cPath)) return cPath;
								}
								if (targetFileName.Equals("Bandizip.exe", StringComparison.OrdinalIgnoreCase) &&
								    !candidate.EndsWith("Bandizip.exe", StringComparison.OrdinalIgnoreCase))
								{
									string dir = Path.GetDirectoryName(candidate) ?? "";
									string bzPath = Path.Combine(dir, "Bandizip.exe");
									if (File.Exists(bzPath)) return bzPath;
									string cPath = Path.Combine(dir, "bz.exe");
									if (File.Exists(cPath)) return cPath;
								}
								return candidate;
							}
							if (Directory.Exists(candidate))
							{
								string full = Path.Combine(candidate, targetFileName);
								if (File.Exists(full)) return full;
								if (targetFileName.Equals("7zG.exe", StringComparison.OrdinalIgnoreCase))
								{
									string alt = Path.Combine(candidate, "7z.exe");
									if (File.Exists(alt)) return alt;
								}
								if (targetFileName.Equals("Bandizip.exe", StringComparison.OrdinalIgnoreCase))
								{
									string alt = Path.Combine(candidate, "bz.exe");
									if (File.Exists(alt)) return alt;
								}
							}
						}
					}
				}
				catch { }
			}
		}
		return null;
	}

	private static string? ScanDrivesForExecutable(string[] relativePaths)
	{
		try
		{
			var drives = DriveInfo.GetDrives();
			foreach (var drive in drives)
			{
				if (!drive.IsReady) continue;
				foreach (var rel in relativePaths)
				{
					try
					{
						string full = Path.Combine(drive.RootDirectory.FullName, rel);
						if (File.Exists(full)) return full;
					}
					catch { }
				}
			}
		}
		catch { }
		return null;
	}

	public static string? FindExecutableInPath(string fileName)
	{
		try
		{
			string? pathEnv = Environment.GetEnvironmentVariable("PATH");
			if (string.IsNullOrEmpty(pathEnv)) return null;

			string[] dirs = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
			foreach (var dir in dirs)
			{
				try
				{
					string full = Path.Combine(dir.Trim('\"', ' '), fileName);
					if (File.Exists(full)) return full;
				}
				catch { }
			}
		}
		catch { }
		return null;
	}

	internal static void ExecuteFolder(string folderPath)
	{
		if (string.IsNullOrWhiteSpace(folderPath))
		{
			return;
		}
		string text = Environment.ExpandEnvironmentVariables(folderPath.Trim().Trim('"'));
		AppLogger.LogInfo($"Executing OpenFolder: '{text}'");
		try
		{
			if (text.StartsWith("::{", StringComparison.OrdinalIgnoreCase) || text.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = "explorer.exe",
					Arguments = text,
					UseShellExecute = true
				});
				return;
			}
			if (Directory.Exists(text))
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = "explorer.exe",
					Arguments = "\"" + text + "\"",
					UseShellExecute = true
				});
			}
			else if (File.Exists(text))
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = "explorer.exe",
					Arguments = "/select,\"" + text + "\"",
					UseShellExecute = true
				});
			}
			else
			{
				try
				{
					string? root = System.IO.Path.GetPathRoot(text);
					if (!string.IsNullOrEmpty(root) && !Directory.Exists(root))
					{
						AppLogger.LogWarn($"Target drive '{root}' for path '{text}' is not available on this machine.");
						Application.Current?.Dispatcher.BeginInvoke(() =>
						{
							try
							{
								MessageBox.Show($"无法打开目标路径 '{folderPath}'：\n当前计算机上找不到指定的驱动器或磁盘分区（{root}）。", "StarPie - 路径不可用", MessageBoxButton.OK, MessageBoxImage.Warning);
							}
							catch { }
						});
						return;
					}
				}
				catch { }

				Process.Start(new ProcessStartInfo
				{
					FileName = text,
					UseShellExecute = true
				});
			}
		}
		catch (Exception ex)
		{
			AppLogger.LogError($"Failed to open folder '{folderPath}'", ex);
			Application.Current?.Dispatcher.BeginInvoke(() =>
			{
				try
				{
					MessageBox.Show("无法打开文件夹 '" + folderPath + "':\n" + ex.Message, "StarPie", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				}
				catch { }
			});
		}
	}

    private static void StartShellToolProcess(ProcessStartInfo info, StarPie.Plugin.ProcessLaunchMode mode)
    {
        if (!ProcessLaunchExecutor.Start(info, mode))
            throw new InvalidOperationException($"Could not launch '{info.FileName}' in mode {mode}.");
    }

    internal static bool ExecuteLaunchWithMode(string path, string arguments, StarPie.Plugin.ProcessLaunchMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (mode == StarPie.Plugin.ProcessLaunchMode.Default)
        {
            ExecuteLaunch(path, arguments, false);
            return true;
        }
        string file = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        bool appId = file.StartsWith("shell:AppsFolder", StringComparison.OrdinalIgnoreCase) ||
            (file.Contains('!') && !file.Contains(":\\") && !file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        if (appId && mode == StarPie.Plugin.ProcessLaunchMode.Administrator)
            throw new NotSupportedException("Packaged application activation does not support administrator launch.");
        if (appId)
        {
            arguments = file.StartsWith("shell:AppsFolder", StringComparison.OrdinalIgnoreCase) ? file : "shell:AppsFolder\\" + file;
            file = "explorer.exe";
        }
        return ProcessLaunchExecutor.Start(new ProcessStartInfo
        {
            FileName = file,
            Arguments = arguments ?? "",
            WorkingDirectory = File.Exists(file) ? Path.GetDirectoryName(file) ?? "" : "",
            UseShellExecute = true,
        }, mode);
    }

	internal static void ExecuteLaunch(string path, string arguments, bool runAsStandardUser = false)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}
		string text = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
		AppLogger.LogInfo($"Executing Launch: Path='{text}', Args='{arguments}', StandardUser={runAsStandardUser}");
		if (text.StartsWith("shell:AppsFolder", StringComparison.OrdinalIgnoreCase) || (text.Contains("!") && !text.Contains(":\\") && !text.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
		{
			string arguments2 = (text.StartsWith("shell:AppsFolder", StringComparison.OrdinalIgnoreCase) ? text : ("shell:AppsFolder\\" + text));
			try
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = "explorer.exe",
					Arguments = arguments2,
					UseShellExecute = true
				});
			}
			catch (Exception ex)
			{
				AppLogger.LogError($"Failed to launch UWP app: {arguments2}", ex);
				throw;
			}
		}
		else
		{
			if (runAsStandardUser)
			{
				try
				{
					string workDir = "";
					if (File.Exists(text))
					{
						workDir = Path.GetDirectoryName(text) ?? "";
					}
					if (TryLaunchUnelevatedViaExplorer(text, arguments ?? "", workDir))
					{
						AppLogger.LogInfo($"Launched '{text}' with Explorer standard user integrity via IShellDispatch2 (de-elevated)");
						return;
					}
				}
				catch (Exception exShell)
				{
					AppLogger.LogWarn($"Unelevated launch failed for '{text}', falling back to Process.Start: {exShell.Message}");
				}
			}

			string exeName = Path.GetFileNameWithoutExtension(text).ToLowerInvariant();
			bool isShellOrSpecial = exeName == "explorer" || exeName == "cmd" || exeName == "powershell" || exeName == "wsl" || exeName == "calc" || exeName == "calculator" || exeName == "calculatorapp";
			if (!isShellOrSpecial && string.IsNullOrWhiteSpace(arguments) && TryToggleProcessWindow(text))
			{
				AppLogger.LogInfo($"Toggled active window for existing process '{text}'");
				return;
			}
			ProcessStartInfo processStartInfo = new ProcessStartInfo
			{
				FileName = text,
				Arguments = (arguments ?? string.Empty),
				UseShellExecute = true
			};
			try
			{
				if (File.Exists(text))
				{
					string directoryName = Path.GetDirectoryName(text);
					if (!string.IsNullOrEmpty(directoryName) && Directory.Exists(directoryName))
					{
						processStartInfo.WorkingDirectory = directoryName;
					}
				}
				else if (Directory.Exists(text))
				{
					processStartInfo.WorkingDirectory = text;
				}
			}
			catch
			{
			}
			try
			{
				System.Diagnostics.Process started = System.Diagnostics.Process.Start(processStartInfo);
				// 启动后自动把新窗口拉到前台（后台等待主窗口出现 → ActivateWindow，含前台解锁链）
				if (started != null)
				{
					System.Diagnostics.Process proc = started;
					System.Threading.Tasks.Task.Run(delegate
					{
						try
						{
							for (int i = 0; i < 40; i++)
							{
								if (proc.MainWindowHandle != IntPtr.Zero)
								{
									break;
								}
								System.Threading.Thread.Sleep(50);
							}
							if (proc.MainWindowHandle != IntPtr.Zero)
							{
								System.Threading.Thread.Sleep(150); // 等窗口内容就绪再激活
								WindowTaskbarHelper.ActivateWindow(proc.MainWindowHandle);
							}
						}
						catch
						{
						}
					});
				}
			}
			catch (Exception ex)
			{
				AppLogger.LogError($"Process.Start failed for '{text}' with args '{arguments}'", ex);
				throw;
			}
		}
	}

	/// <summary>
	/// Issue #58: 当 StarPie 以管理员提权运行时，通过 Windows 资源管理器 (explorer.exe) 桌面 Shell 中转以标准普通用户权限 (Medium Integrity) 启动外部程序。
	/// 解决以普通权限启动失效、终端仍带管理员盾牌、以及因 UIPI 隔离无法拖入外部文件的问题。
	/// </summary>
	public static bool TryLaunchUnelevatedViaExplorer(string path, string arguments, string workingDir) =>
        TryLaunchUnelevatedViaExplorer(path, arguments, workingDir, 1);

    internal static bool TryLaunchUnelevatedViaExplorer(string path, string arguments, string workingDir, int showCommand)
	{
        if (!DesktopShellToken.IsStandardUser())
        {
            AppLogger.LogWarn("Unelevated launch refused: no non-elevated desktop Shell is available.");
            return false;
        }
        var comObjects = new System.Collections.Generic.List<object>();
		try
		{
			Type? shellType = Type.GetTypeFromProgID("Shell.Application");
			if (shellType == null) return false;

			object? shell = Activator.CreateInstance(shellType);
			if (shell == null) return false;
            comObjects.Add(shell);

			object? windows = shellType.InvokeMember("Windows", BindingFlags.InvokeMethod, null, shell, null);
			if (windows == null) return false;
            comObjects.Add(windows);

			// SWC_DESKTOP = 8, SWFO_NEEDDISPATCH = 1
			object[] args = new object[] { 0, Type.Missing, 8, 0, 1 };
			ParameterModifier[] modifiers = new ParameterModifier[1];
			modifiers[0] = new ParameterModifier(5);
			modifiers[0][3] = true;

			object? desktop = windows.GetType().InvokeMember(
				"FindWindowSW",
				BindingFlags.InvokeMethod,
				null,
				windows,
				args,
				modifiers,
				null,
				null);

			if (desktop == null) return false;
            comObjects.Add(desktop);

			object? doc = desktop.GetType().InvokeMember("Document", BindingFlags.GetProperty, null, desktop, null);
			if (doc == null) return false;
            comObjects.Add(doc);

			object? app = doc.GetType().InvokeMember("Application", BindingFlags.GetProperty, null, doc, null);
			if (app == null) return false;
            comObjects.Add(app);

			app.GetType().InvokeMember(
				"ShellExecute",
				BindingFlags.InvokeMethod,
				null,
				app,
				new object[] { path, arguments ?? "", workingDir ?? "", "open", showCommand });

			return true;
		}
		catch (Exception ex)
		{
			AppLogger.LogWarn($"TryLaunchUnelevatedViaExplorer failed for '{path}': {ex.Message}");
			return false;
		}
        finally
        {
            // COM 对象只在本次调用中创建，逆序释放；同一 RCW 可能由不同属性返回。
            var released = new System.Collections.Generic.HashSet<object>(System.Collections.Generic.ReferenceEqualityComparer.Instance);
            for (int i = comObjects.Count - 1; i >= 0; i--)
            {
                object value = comObjects[i];
                if (released.Add(value) && Marshal.IsComObject(value))
                {
                    try { Marshal.ReleaseComObject(value); }
                    catch (Exception ex) { AppLogger.LogWarn($"Releasing launch COM object: {ex.Message}"); }
                }
            }
        }
	}

	/// <summary>Runs a command in the selected terminal (cmd / PowerShell / WSL), with or without a window.</summary>
	internal static bool ExecuteCommand(string command, string? terminal) =>
        ExecuteCommandWithMode(command, StarPie.Plugin.ProcessLaunchMode.Default, terminal);

    internal static bool ExecuteCommandWithMode(string command, StarPie.Plugin.ProcessLaunchMode mode, string? terminal)
	{
		if (string.IsNullOrWhiteSpace(command))
		{
			return false;
		}
		string term = string.IsNullOrEmpty(terminal) ? "cmd" : terminal.Trim().ToLowerInvariant();
		bool hidden = term.EndsWith("_hidden", StringComparison.OrdinalIgnoreCase);
		string shell = hidden ? term.Substring(0, term.Length - "_hidden".Length) : term;
		// Escape embedded quotes for the cmd/PowerShell wrappers ("" is the escape inside Windows quoting)
		string quoted = command.Replace("\"", "\"\"");
		AppLogger.LogInfo($"Executing Command: Shell='{shell}', Hidden={hidden}, Cmd='{command}'");
		try
		{
			switch (shell)
			{
			case "powershell":
				// Visible: keep the window open (-NoExit). Hidden: run to completion.
				return ProcessLaunchExecutor.Start(new ProcessStartInfo("powershell.exe", (hidden ? "-NoProfile -Command \"" : "-NoProfile -NoExit -Command \"") + quoted + "\"")
				{
					UseShellExecute = false,
					CreateNoWindow = hidden
				}, mode);
			case "wsl":
				// WSL receives the raw command after "--"; no extra quoting needed
				return ProcessLaunchExecutor.Start(new ProcessStartInfo("wsl.exe", "-- " + command)
				{
					UseShellExecute = false,
					CreateNoWindow = hidden
				}, mode);
			default:
				// Visible: keep the window open (/k). Hidden: /c so no lingering process.
				return ProcessLaunchExecutor.Start(new ProcessStartInfo("cmd.exe", (hidden ? "/c \"" : "/k \"") + quoted + "\"")
				{
					UseShellExecute = false,
					CreateNoWindow = hidden
				}, mode);
			}
		}
		catch (Exception ex)
		{
			AppLogger.LogError($"Failed to run command '{command}' in '{terminal}'", ex);
			return false;
		}
	}

	/// <summary>
	/// 切换到任务栏第 N 个窗口；参数缺失/非法时默认第 1 个。
	/// 全程在后台线程执行 —— UIA 遍历与前台激活都不得阻塞 UI 与钩子线程。
	/// <para>
	/// 可见性从 <c>private</c> 放宽到 <c>internal</c>：现在唯一的调用方是
	/// <c>Plugins.PluginWindowService.ActivateTaskbarSlot</c>（随包动作包「切换窗口」经它过来），
	/// 宿主界面层不直接调。
	/// </para>
	/// <para>
	/// <b>刻意保持 <c>void</c>、不改成 <c>bool</c></b>：实现体把工作丢给 <c>Task.Run</c> 就返回了，
	/// 真正的失败（第 N 个槽位不存在）发生在后台线程上，这里根本无从得知。
	/// 与其编一个不可靠的返回值，不如把语义留空，由调用方如实说明「只表示已受理」。
	/// </para>
	/// </summary>
	internal static void ExecuteSwitchWindow(string? parameter)
	{
		int n = 1;
		if (int.TryParse(parameter?.Trim(), out int parsed) && parsed > 0)
		{
			n = parsed;
		}
		System.Threading.Tasks.Task.Run(delegate
		{
			if (!WindowTaskbarHelper.ActivateTaskbarSlot(n))
			{
				System.Diagnostics.Debug.WriteLine($"[SwitchWindow] 任务栏第 {n} 个槽位不可用");
			}
		});
	}

	private static bool IsStandardKeyToken(string token)
	{
		string t = token.Trim().ToLowerInvariant();
		return t == "ctrl" || t == "shift" || t == "alt" || t == "win" ||
		       t == "tab" || t == "enter" || t == "esc" || t == "space" ||
		       t == "backspace" || t == "delete" || t == "insert" ||
		       (t.StartsWith("f") && int.TryParse(t.Substring(1), out _)) ||
		       (t.StartsWith("num"));
	}

	public const nint StarPieExtraInfo = 0x53544152;

	public static void ReleaseStuckModifiers()
	{
		try
		{
			// 智能解卡自愈：强制下发 KeyUp 清空系统粘滞状态（双通道 SendInput + keybd_event 注入）
			// 核心守卫：若用户手指物理上正在按着该修饰键（GetAsyncKeyState & 0x8000 != 0），绝不可注销抬起，杜绝破坏前台应用（如 Maya、Photoshop、Blender）的原生组合键
			ushort[] modifiers = new ushort[] { 162, 163, 17, 160, 161, 16, 164, 165, 18, 91, 92 };
			List<INPUT> upInputs = new List<INPUT>();
			foreach (ushort mod in modifiers)
			{
				if ((GetAsyncKeyState((int)mod) & 0x8000) != 0)
				{
					// 用户物理手指正在按压此修饰键，保持物理按下，跳过注销
					continue;
				}
				upInputs.Add(CreateKeyInput(mod, down: false));
				uint flags = KEYEVENTF_KEYUP;
				if (mod == 91 || mod == 92 || mod == 165 || mod == 163)
				{
					flags |= KEYEVENTF_EXTENDEDKEY;
				}
				keybd_event((byte)mod, 0, flags, StarPieExtraInfo);
			}
			if (upInputs.Count > 0)
			{
				SendInput((uint)upInputs.Count, upInputs.ToArray(), Marshal.SizeOf(typeof(INPUT)));
			}
		}
		catch
		{
		}
	}

	private static void ExecuteSingleStep(HotkeyStep step)
	{
		try
		{
			// 1. 纯修饰键步骤 (如 Alt 轻敲呼出 Ribbon 菜单，或单独按下 Shift/Ctrl)
			if (step.MainKey == 0 && step.Modifiers.Count > 0)
			{
				List<INPUT> modDowns = new List<INPUT>();
				foreach (ushort mod in step.Modifiers)
				{
					modDowns.Add(CreateKeyInput(mod, down: true));
				}
				SendInput((uint)modDowns.Count, modDowns.ToArray(), Marshal.SizeOf(typeof(INPUT)));
				System.Threading.Thread.Sleep(15);

				List<INPUT> modUps = new List<INPUT>();
				for (int i = step.Modifiers.Count - 1; i >= 0; i--)
				{
					modUps.Add(CreateKeyInput(step.Modifiers[i], down: false));
				}
				SendInput((uint)modUps.Count, modUps.ToArray(), Marshal.SizeOf(typeof(INPUT)));
				return;
			}

			// 2. 复合键步骤 (如 Alt+H 或 Ctrl+K)
			if (step.Modifiers.Count > 0 && step.MainKey != 0)
			{
				List<INPUT> modDowns = new List<INPUT>();
				foreach (ushort mod in step.Modifiers)
				{
					modDowns.Add(CreateKeyInput(mod, down: true));
				}
				SendInput((uint)modDowns.Count, modDowns.ToArray(), Marshal.SizeOf(typeof(INPUT)));
				System.Threading.Thread.Sleep(12);

				INPUT keyDown = CreateKeyInput(step.MainKey, down: true);
				INPUT keyUp = CreateKeyInput(step.MainKey, down: false);

				if (step.MainKey == 44) // VK_SNAPSHOT
				{
					SendInput(2u, new INPUT[] { keyDown, keyUp }, Marshal.SizeOf(typeof(INPUT)));
				}
				else
				{
					SendInput(1u, new INPUT[] { keyDown }, Marshal.SizeOf(typeof(INPUT)));
					System.Threading.Thread.Sleep(15);
					SendInput(1u, new INPUT[] { keyUp }, Marshal.SizeOf(typeof(INPUT)));
					System.Threading.Thread.Sleep(12);
				}

				List<INPUT> modUps = new List<INPUT>();
				for (int i = step.Modifiers.Count - 1; i >= 0; i--)
				{
					modUps.Add(CreateKeyInput(step.Modifiers[i], down: false));
				}
				SendInput((uint)modUps.Count, modUps.ToArray(), Marshal.SizeOf(typeof(INPUT)));
				return;
			}

			// 3. 单键轻敲步骤 (如 H, V, F 等)
			if (step.MainKey != 0)
			{
				INPUT keyDown = CreateKeyInput(step.MainKey, down: true);
				INPUT keyUp = CreateKeyInput(step.MainKey, down: false);

				if (step.MainKey == 44) // VK_SNAPSHOT
				{
					SendInput(2u, new INPUT[] { keyDown, keyUp }, Marshal.SizeOf(typeof(INPUT)));
				}
				else
				{
					SendInput(1u, new INPUT[] { keyDown }, Marshal.SizeOf(typeof(INPUT)));
					System.Threading.Thread.Sleep(15);
					SendInput(1u, new INPUT[] { keyUp }, Marshal.SizeOf(typeof(INPUT)));
				}
			}
		}
		catch (Exception ex)
		{
			AppLogger.LogError($"Error in ExecuteSingleStep ({step}): {ex.Message}");
		}
	}

	internal static void ExecuteHotkey(string hotkeyString)
	{
		if (string.IsNullOrWhiteSpace(hotkeyString))
		{
			return;
		}

		HotkeyDetails hotkeyDetails = ParseHotkey(hotkeyString);
		if (hotkeyDetails.Modifiers.Count == 0 && hotkeyDetails.MainKey == 0 && hotkeyDetails.Steps.Count == 0)
		{
			// 如果是连续按键字母（例如 "UU", "US", "WASD"），优先使用原生虚拟按键流发送，保证系统菜单与快捷键接收
			string rawStr = hotkeyString.Trim();
			if (rawStr.Length >= 2 && rawStr.Length <= 8 && rawStr.All(char.IsLetterOrDigit))
			{
				AppLogger.LogInfo($"Executing Key Sequence from raw token: '{rawStr}'");
				foreach (char c in rawStr)
				{
					ushort vk = MapKeyStringToVk(c.ToString());
					if (vk != 0)
					{
						INPUT kDown = CreateKeyInput(vk, down: true);
						INPUT kUp = CreateKeyInput(vk, down: false);
						SendInput(1u, new INPUT[] { kDown }, Marshal.SizeOf(typeof(INPUT)));
						System.Threading.Thread.Sleep(15);
						SendInput(1u, new INPUT[] { kUp }, Marshal.SizeOf(typeof(INPUT)));
						System.Threading.Thread.Sleep(25);
					}
					else
					{
						SendTextInput(c.ToString());
					}
				}
				return;
			}
			AppLogger.LogInfo($"Executing Text Input: '{hotkeyString}'");
			SendTextInput(hotkeyString);
			return;
		}

		// 多步骤序列（包括显式步进如 "Alt, H, V, F"、连续多键如 "Alt+H+V+F"、"U+U"、"Ctrl+K, Ctrl+C" 等）
		if (hotkeyDetails.Steps.Count > 1)
		{
			AppLogger.LogInfo($"Executing Multi-Step Hotkey: '{hotkeyString}' ({hotkeyDetails.Steps.Count} steps: [{string.Join(" -> ", hotkeyDetails.Steps)}])");
			System.Threading.Thread.Sleep(15);
			for (int s = 0; s < hotkeyDetails.Steps.Count; s++)
			{
				HotkeyStep step = hotkeyDetails.Steps[s];
				ExecuteSingleStep(step);
				if (s < hotkeyDetails.Steps.Count - 1)
				{
					int stepDelay = (step.Modifiers.Contains(164) && step.MainKey == 0) ? 35 : 25;
					System.Threading.Thread.Sleep(stepDelay);
				}
			}
			return;
		}

		// 无修饰键的多键序列回退兼容（例如 "U+U", "U+S", "A+B"）
		if (hotkeyDetails.Modifiers.Count == 0 && hotkeyDetails.SequenceKeys.Count > 1)
		{
			AppLogger.LogInfo($"Executing Key Sequence: [{string.Join(" -> ", hotkeyDetails.SequenceKeys)}]");
			foreach (ushort vk in hotkeyDetails.SequenceKeys)
			{
				INPUT kDown = CreateKeyInput(vk, down: true);
				INPUT kUp = CreateKeyInput(vk, down: false);
				SendInput(1u, new INPUT[] { kDown }, Marshal.SizeOf(typeof(INPUT)));
				System.Threading.Thread.Sleep(15);
				SendInput(1u, new INPUT[] { kUp }, Marshal.SizeOf(typeof(INPUT)));
				System.Threading.Thread.Sleep(25);
			}
			return;
		}

		AppLogger.LogInfo($"Executing Hotkey: '{hotkeyString}' (MainKey: {hotkeyDetails.MainKey}, Modifiers: [{string.Join(",", hotkeyDetails.Modifiers)}])");

		// 给 DWM 窗口焦点平稳回落预留短暂缓冲时延（轮盘关闭后目标窗口焦点就绪）
		System.Threading.Thread.Sleep(10);

		try
		{
			// 1. If pure modifier combo (e.g. Shift + Alt, Ctrl + Shift)
			if (hotkeyDetails.MainKey == 0 && hotkeyDetails.Modifiers.Count > 0)
			{
				List<INPUT> modDowns = new List<INPUT>();
				foreach (ushort mod in hotkeyDetails.Modifiers)
				{
					modDowns.Add(CreateKeyInput(mod, down: true));
				}
				SendInput((uint)modDowns.Count, modDowns.ToArray(), Marshal.SizeOf(typeof(INPUT)));
				System.Threading.Thread.Sleep(10);
				List<INPUT> modUps = new List<INPUT>();
				for (int i = hotkeyDetails.Modifiers.Count - 1; i >= 0; i--)
				{
					modUps.Add(CreateKeyInput(hotkeyDetails.Modifiers[i], down: false));
				}
				SendInput((uint)modUps.Count, modUps.ToArray(), Marshal.SizeOf(typeof(INPUT)));
				return;
			}

			// 2. Standard Modifier + Main Key combo
			List<INPUT> downInputs = new List<INPUT>();
			foreach (ushort modifier in hotkeyDetails.Modifiers)
			{
				downInputs.Add(CreateKeyInput(modifier, down: true));
			}
			if (downInputs.Count > 0)
			{
				SendInput((uint)downInputs.Count, downInputs.ToArray(), Marshal.SizeOf(typeof(INPUT)));
				System.Threading.Thread.Sleep(12);
			}

			if (hotkeyDetails.MainKey != 0)
			{
				INPUT keySeqDown = CreateKeyInput(hotkeyDetails.MainKey, down: true);
				INPUT keySeqUp = CreateKeyInput(hotkeyDetails.MainKey, down: false);

				if (hotkeyDetails.MainKey == 44) // VK_SNAPSHOT (PrintScreen)
				{
					// 瞬态快门模式：PrintScreen Down 与 Up 作为一个原子数据包同时发送（0ms 间隔）
					// 消除在 Down 和 Up 之间由于外部截图工具抢占全局输入焦点而造成的按键序列截断
					SendInput(2u, new INPUT[] { keySeqDown, keySeqUp }, Marshal.SizeOf(typeof(INPUT)));
				}
				else
				{
					SendInput(1u, new INPUT[] { keySeqDown }, Marshal.SizeOf(typeof(INPUT)));
					System.Threading.Thread.Sleep(12);
					SendInput(1u, new INPUT[] { keySeqUp }, Marshal.SizeOf(typeof(INPUT)));
					System.Threading.Thread.Sleep(10);
				}
			}
		}
		finally
		{
			// 3. 无论中间是否发生异常，始终安全下发所有已按下修饰键的抬起事件，彻底杜绝系统级粘滞
			// 第一通道：SendInput 队列下发 KeyUp
			List<INPUT> upInputs = new List<INPUT>();
			for (int num = hotkeyDetails.Modifiers.Count - 1; num >= 0; num--)
			{
				upInputs.Add(CreateKeyInput(hotkeyDetails.Modifiers[num], down: false));
			}
			if (upInputs.Count > 0)
			{
				SendInput((uint)upInputs.Count, upInputs.ToArray(), Marshal.SizeOf(typeof(INPUT)));
			}

			// 第二通道：keybd_event 直接同步 win32k 全局击键状态表（跨进程/跨权限防御）
			foreach (ushort mod in hotkeyDetails.Modifiers)
			{
				uint flags = KEYEVENTF_KEYUP;
				if (mod == 91 || mod == 92 || mod == 165 || mod == 163)
				{
					flags |= KEYEVENTF_EXTENDEDKEY;
				}
				keybd_event((byte)mod, 0, flags, StarPieExtraInfo);
				if (mod == 162 || mod == 163) keybd_event(17, 0, KEYEVENTF_KEYUP, StarPieExtraInfo); // VK_CONTROL
				else if (mod == 160 || mod == 161) keybd_event(16, 0, KEYEVENTF_KEYUP, StarPieExtraInfo); // VK_SHIFT
				else if (mod == 164 || mod == 165) keybd_event(18, 0, KEYEVENTF_KEYUP, StarPieExtraInfo); // VK_MENU
				else if (mod == 91 || mod == 92) keybd_event((byte)mod, 0, KEYEVENTF_KEYUP | KEYEVENTF_EXTENDEDKEY, StarPieExtraInfo);
			}

			// 4. 双重保险：针对截图软件（Snipaste/PixPin/微信截屏等）或 Win 键系统菜单抢焦场景，
			// 在 +35ms 与 +85ms 异步补发修饰键释放，彻底消灭残留粘滞
			bool hasWinMod = hotkeyDetails.Modifiers.Contains(91) || hotkeyDetails.Modifiers.Contains(92);
			if ((hotkeyDetails.MainKey == 44 || hasWinMod) && hotkeyDetails.Modifiers.Count > 0)
			{
				var modsCopy = hotkeyDetails.Modifiers.ToArray();
				System.Threading.Tasks.Task.Run(async () =>
				{
					try
					{
						await System.Threading.Tasks.Task.Delay(35).ConfigureAwait(false);
						foreach (var mod in modsCopy)
						{
							uint flags = KEYEVENTF_KEYUP;
							if (mod == 91 || mod == 92 || mod == 165 || mod == 163) flags |= KEYEVENTF_EXTENDEDKEY;
							keybd_event((byte)mod, 0, flags, StarPieExtraInfo);
							if (mod == 162 || mod == 163) keybd_event(17, 0, KEYEVENTF_KEYUP, StarPieExtraInfo);
							else if (mod == 91 || mod == 92) keybd_event((byte)mod, 0, KEYEVENTF_KEYUP | KEYEVENTF_EXTENDEDKEY, StarPieExtraInfo);
						}

						await System.Threading.Tasks.Task.Delay(50).ConfigureAwait(false);
						foreach (var mod in modsCopy)
						{
							uint flags = KEYEVENTF_KEYUP;
							if (mod == 91 || mod == 92 || mod == 165 || mod == 163) flags |= KEYEVENTF_EXTENDEDKEY;
							keybd_event((byte)mod, 0, flags, StarPieExtraInfo);
							if (mod == 162 || mod == 163) keybd_event(17, 0, KEYEVENTF_KEYUP, StarPieExtraInfo);
							else if (mod == 91 || mod == 92) keybd_event((byte)mod, 0, KEYEVENTF_KEYUP | KEYEVENTF_EXTENDEDKEY, StarPieExtraInfo);
						}
					}
					catch
					{
					}
				});
			}
		}
	}

	/// <summary>
	/// 执行系统功能预设。
	/// <para>
	/// <b>入参是稳定 ID 还是中文名？两者都收。</b>先 <c>ToLowerInvariant()</c> 再比对，
	/// 所以主力分支是一串英文小写 ID（<c>windowswitcher</c> / <c>alttab</c> / …），
	/// 它们与 <c>SlotViewModel.SystemPresetList</c> 的 <c>Key</c> 一一对应。
	/// </para>
	/// <para>
	/// <b>那几处中文 case 是历史数据兼容，不要把它们改掉、也不要以为它们该接 i18n</b>：
	/// 老版本往 <c>Action.Parameter</c> 里存的是中文显示名（「锁屏」「控制台」「文件秒搜」…），
	/// 用户升级后这些配置还在。中文 case 匹配的是<b>已存在配置文件里的历史字符串</b>，
	/// 属于数据而不是界面文案 —— 界面语言怎么切都不影响老配置里的那几个字。
	/// 拿「中文参与判断」的扫描结果挨个清理时，这几处要按可接受项排除。
	/// </para>
	/// </summary>
	internal static bool ExecuteSystem(string presetName)
	{
		if (string.IsNullOrEmpty(presetName))
		{
			return false;
		}
		string text = presetName.Trim().ToLowerInvariant();

		switch (text)
		{
		case "windowswitcher":
		case "taskswitcher":
		case "alttabsticky":
			ExecuteHotkey("Ctrl+Alt+Tab");
			return true;
		case "alttab":
		case "switchwindow":
			ExecuteHotkey("Alt+Tab");
			return true;
		case "closewindow":
			ExecuteHotkey("Alt+F4");
			return true;
		case "minimize":
			ExecuteHotkey("Win+Down");
			return true;
		case "maximize":
			ExecuteHotkey("Win+Up");
			return true;
		case "snapleft":
			ExecuteHotkey("Win+Left");
			return true;
		case "snapright":
			ExecuteHotkey("Win+Right");
			return true;
		case "taskview":
			ExecuteHotkey("Win+Tab");
			return true;
		case "prevdesktop":
			ExecuteHotkey("Win+Ctrl+Left");
			return true;
		case "nextdesktop":
			ExecuteHotkey("Win+Ctrl+Right");
			return true;
		case "showdesktop":
			ExecuteHotkey("Win+D");
			return true;
		case "fullscreen":
			ExecuteHotkey("F11");
			return true;
		case "screenshot":
			ExecuteHotkey("Win+Shift+S");
			return true;
		case "taskmanager":
			if (!TryToggleProcessWindow("taskmgr"))
			{
				try
				{
					Process.Start(new ProcessStartInfo
					{
						FileName = "taskmgr.exe",
						UseShellExecute = true
					});
				}
				catch
				{
					ExecuteHotkey("Ctrl+Shift+Esc");
				}
			}
			return true;
		case "explorer":
			try
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = "explorer.exe",
					UseShellExecute = true
				});
			}
			catch
			{
				ExecuteHotkey("Win+E");
			}
			return true;
		case "opensettings":
		case "openstarpie":
		case "starpie":
		case "starpie控制台":
		case "控制台":
			Application.Current?.Dispatcher?.BeginInvoke((Action)delegate
			{
				App.ShowSettingsWindow();
			});
			return true;
		case "settings":
			if (!TryToggleProcessWindow("SystemSettings"))
			{
				try
				{
					Process.Start(new ProcessStartInfo
					{
						FileName = "ms-settings:",
						UseShellExecute = true
					});
				}
				catch
				{
					ExecuteHotkey("Win+I");
				}
			}
			return true;
		case "calculator":
			AppLogger.LogInfo("Launching System Calculator");
			try
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = "calc.exe",
					UseShellExecute = true
				});
			}
			catch (Exception ex1)
			{
				AppLogger.LogWarn($"Direct calc.exe launch failed: {ex1.Message}. Attempting ms-calculator: URI...");
				try
				{
					Process.Start(new ProcessStartInfo
					{
						FileName = "ms-calculator:",
						UseShellExecute = true
					});
				}
				catch (Exception ex2)
				{
					AppLogger.LogError("Failed to launch calculator via ms-calculator: URI as well", ex2);
					ExecuteHotkey("Win+R");
				}
			}
			return true;
		case "rundialog":
			ExecuteHotkey("Win+R");
			return true;
		case "windowssearch":
			ExecuteHotkey("Win+S");
			return true;
		case "quicksearch":
		case "quickfinder":
		case "nativesearch":
		case "文件秒搜":
		case "快速秒搜":
		case "原生秒搜":
			Application.Current?.Dispatcher?.BeginInvoke((Action)delegate
			{
				QuickSearchWindow.ShowOrActivate();
			});
			return true;
		case "clipboardhistory":
			ExecuteHotkey("Win+V");
			return true;
		case "lockworkstation":
		case "锁定屏幕":
		case "锁屏":
		case "lock":
			LockWorkStation();
			return true;
		case "volumeup":
			SimulateSingleKey(175);
			return true;
		case "volumedown":
			SimulateSingleKey(174);
			return true;
		case "volumemute":
			SimulateSingleKey(173);
			return true;
		case "playpause":
			SimulateSingleKey(179);
			return true;
		case "nexttrack":
			SimulateSingleKey(176);
			return true;
		case "prevtrack":
			SimulateSingleKey(177);
			return true;
		case "stopmedia":
			SimulateSingleKey(178);
			return true;
		case "newtab":
			ExecuteHotkey("Ctrl+T");
			return true;
		case "closetab":
			ExecuteHotkey("Ctrl+W");
			return true;
		case "reopentab":
			ExecuteHotkey("Ctrl+Shift+T");
			return true;
		case "refresh":
			ExecuteHotkey("F5");
			return true;
		case "hardrefresh":
			ExecuteHotkey("Ctrl+F5");
			return true;
		case "zoomin":
			ExecuteHotkey("Ctrl+Plus");
			return true;
		case "zoomout":
			ExecuteHotkey("Ctrl+Minus");
			return true;
		case "zoomreset":
			ExecuteHotkey("Ctrl+0");
			return true;
		case "sleep":
		case "睡眠":
		case "休眠":
		case "suspend":
			try
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = "rundll32.exe",
					Arguments = "powrprof.dll,SetSuspendState 0,1,0",
					UseShellExecute = true
				});
			}
			catch { }
			return true;
		case "restart":
		case "重启":
		case "reboot":
			try
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = "shutdown.exe",
					Arguments = "/r /t 0",
					UseShellExecute = true
				});
			}
			catch { }
			return true;
		case "shutdown":
		case "关机":
		case "poweroff":
			try
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = "shutdown.exe",
					Arguments = "/s /t 0",
					UseShellExecute = true
				});
			}
			catch { }
			return true;
		default:
			return false;
		}
	}

	private static void SimulateSingleKey(ushort vk)
	{
		INPUT[] pInputs = new INPUT[2]
		{
			CreateKeyInput(vk, down: true),
			CreateKeyInput(vk, down: false)
		};
		SendInput(2u, pInputs, Marshal.SizeOf(typeof(INPUT)));
	}

	public static void SendTextInput(string text)
	{
		if (string.IsNullOrEmpty(text)) return;
		List<INPUT> inputs = new List<INPUT>();
		foreach (char c in text)
		{
			INPUT down = new INPUT { type = 1u };
			down.U.ki = new KEYBDINPUT
			{
				wVk = 0,
				wScan = (ushort)c,
				dwFlags = 4u, // KEYEVENTF_UNICODE
				time = 0u,
				dwExtraInfo = StarPieExtraInfo
			};
			INPUT up = new INPUT { type = 1u };
			up.U.ki = new KEYBDINPUT
			{
				wVk = 0,
				wScan = (ushort)c,
				dwFlags = 4u | 2u, // KEYEVENTF_UNICODE | KEYEVENTF_KEYUP
				time = 0u,
				dwExtraInfo = StarPieExtraInfo
			};
			inputs.Add(down);
			inputs.Add(up);
		}
		SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf(typeof(INPUT)));
	}

	private static INPUT CreateKeyInput(ushort vk, bool down)
	{
		INPUT result = new INPUT
		{
			type = 1u
		};
		ushort scan = (ushort)MapVirtualKey((uint)vk, 0u);
		if (vk == 44) // VK_SNAPSHOT: 扫描码必须为 0，规避 PS/2 SysReq (0x54) 扫描码异常
		{
			scan = 0;
		}
		result.U.ki = new KEYBDINPUT
		{
			wVk = vk,
			wScan = scan,
			dwFlags = ((!down) ? 2u : 0u),
			time = 0u,
			dwExtraInfo = StarPieExtraInfo
		};
		if (vk == 33 || vk == 34 || vk == 35 || vk == 36 ||
		    vk == 37 || vk == 38 || vk == 39 || vk == 40 ||
		    vk == 45 || vk == 46 ||
		    vk == 91 || vk == 92 ||
		    vk == 111 ||
		    vk == 163 || vk == 165 ||
		    (vk >= 166 && vk <= 179))
		{
			result.U.ki.dwFlags |= 1u;
		}
		return result;
	}

	public static HotkeyStep ParseHotkeyStepToken(string token)
	{
		HotkeyStep step = new HotkeyStep { RawToken = token };
		string[] pieces = token.Split(new char[] { '+', ' ' }, StringSplitOptions.RemoveEmptyEntries);
		foreach (string piece in pieces)
		{
			string lower = piece.Trim().ToLowerInvariant();
			switch (lower)
			{
				case "ctrl":
				case "control":
				case "lctrl":
				case "rctrl":
					if (!step.Modifiers.Contains(162)) step.Modifiers.Add(162);
					break;
				case "shift":
				case "lshift":
				case "rshift":
					if (!step.Modifiers.Contains(160)) step.Modifiers.Add(160);
					break;
				case "alt":
				case "menu":
				case "lalt":
				case "ralt":
					if (!step.Modifiers.Contains(164)) step.Modifiers.Add(164);
					break;
				case "win":
				case "lwin":
				case "rwin":
				case "windows":
					if (!step.Modifiers.Contains(91)) step.Modifiers.Add(91);
					break;
				default:
					ushort vk = MapKeyStringToVk(lower);
					if (vk != 0)
					{
						step.MainKey = vk;
					}
					break;
			}
		}
		return step;
	}

	public static HotkeyDetails ParseHotkey(string hotkeyString)
	{
		HotkeyDetails details = new HotkeyDetails();
		if (string.IsNullOrWhiteSpace(hotkeyString))
		{
			return details;
		}

		string normalized = hotkeyString.Trim().Replace("->", ",").Replace(">", ",");

		// 1. 如果包含显式步进分隔符（逗号 ',' 或分号 ';'）
		if (normalized.Contains(',') || normalized.Contains(';'))
		{
			string[] chunks = normalized.Split(new char[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
			foreach (string chunk in chunks)
			{
				string trimmed = chunk.Trim();
				if (!string.IsNullOrEmpty(trimmed))
				{
					HotkeyStep step = ParseHotkeyStepToken(trimmed);
					details.Steps.Add(step);
					foreach (ushort mod in step.Modifiers)
					{
						if (!details.Modifiers.Contains(mod)) details.Modifiers.Add(mod);
					}
					if (step.MainKey != 0)
					{
						details.MainKey = step.MainKey;
						details.SequenceKeys.Add(step.MainKey);
					}
				}
			}
			return details;
		}

		// 2. 无显式步进分隔符：解析以 '+' 或空格分隔的按键
		string[] tokens = normalized.Split(new char[] { '+', ' ' }, StringSplitOptions.RemoveEmptyEntries);
		List<ushort> detectedModifiers = new List<ushort>();
		List<ushort> detectedMainKeys = new List<ushort>();
		List<string> rawNonModifiers = new List<string>();

		for (int i = 0; i < tokens.Length; i++)
		{
			string text = tokens[i].Trim().ToLowerInvariant();
			switch (text)
			{
				case "ctrl":
				case "control":
				case "lctrl":
				case "rctrl":
					if (!detectedModifiers.Contains(162)) detectedModifiers.Add(162);
					break;
				case "shift":
				case "lshift":
				case "rshift":
					if (!detectedModifiers.Contains(160)) detectedModifiers.Add(160);
					break;
				case "alt":
				case "menu":
				case "lalt":
				case "ralt":
					if (!detectedModifiers.Contains(164)) detectedModifiers.Add(164);
					break;
				case "win":
				case "lwin":
				case "rwin":
				case "windows":
					if (!detectedModifiers.Contains(91)) detectedModifiers.Add(91);
					break;
				default:
					ushort vk = MapKeyStringToVk(text);
					if (vk != 0)
					{
						detectedMainKeys.Add(vk);
						rawNonModifiers.Add(tokens[i]);
					}
					break;
			}
		}

		// 2.1 若包含多个非修饰键（如 "Alt+H+V+F"、"U+U"、"A+B+C"），自动按步进序列解析
		if (detectedMainKeys.Count > 1)
		{
			// 如果首项是修饰键（如 Alt），则将其作为第 1 步，后续每一个键作为独立步
			if (detectedModifiers.Count > 0 && tokens.Length > detectedMainKeys.Count)
			{
				string firstLower = tokens[0].Trim().ToLowerInvariant();
				bool firstIsMod = (firstLower == "alt" || firstLower == "menu" || firstLower == "ctrl" || firstLower == "shift" || firstLower == "win");
				if (firstIsMod)
				{
					// 第一步：轻敲修饰键（如 Alt 唤醒 Ribbon KeyTips）
					HotkeyStep modStep = new HotkeyStep { RawToken = tokens[0] };
					modStep.Modifiers.AddRange(detectedModifiers);
					details.Steps.Add(modStep);

					// 后续步：每个非修饰键作为独立步（如 H -> V -> F）
					for (int k = 0; k < detectedMainKeys.Count; k++)
					{
						HotkeyStep keyStep = new HotkeyStep
						{
							MainKey = detectedMainKeys[k],
							RawToken = rawNonModifiers[k]
						};
						details.Steps.Add(keyStep);
					}
				}
				else
				{
					// 若修饰键与主键混合，第一步组合，后续单键
					HotkeyStep firstComboStep = new HotkeyStep { RawToken = tokens[0] };
					firstComboStep.Modifiers.AddRange(detectedModifiers);
					firstComboStep.MainKey = detectedMainKeys[0];
					details.Steps.Add(firstComboStep);

					for (int k = 1; k < detectedMainKeys.Count; k++)
					{
						HotkeyStep keyStep = new HotkeyStep
						{
							MainKey = detectedMainKeys[k],
							RawToken = rawNonModifiers[k]
						};
						details.Steps.Add(keyStep);
					}
				}
			}
			else
			{
				// 无修饰键的多键序列（如 "U+U", "A+B+C"）
				for (int k = 0; k < detectedMainKeys.Count; k++)
				{
					HotkeyStep keyStep = new HotkeyStep
					{
						MainKey = detectedMainKeys[k],
						RawToken = rawNonModifiers[k]
					};
					details.Steps.Add(keyStep);
				}
			}

			details.Modifiers.AddRange(detectedModifiers);
			details.SequenceKeys.AddRange(detectedMainKeys);
			details.MainKey = detectedMainKeys.LastOrDefault();
			return details;
		}

		// 2.2 常规单步快捷键（如 "Ctrl+C", "Win+Shift+S", "Alt+F4", "Shift+Alt"）
		HotkeyStep singleStep = new HotkeyStep { RawToken = normalized };
		singleStep.Modifiers.AddRange(detectedModifiers);
		if (detectedMainKeys.Count == 1)
		{
			singleStep.MainKey = detectedMainKeys[0];
		}
		details.Steps.Add(singleStep);
		details.Modifiers.AddRange(detectedModifiers);
		if (detectedMainKeys.Count == 1)
		{
			details.MainKey = detectedMainKeys[0];
			details.SequenceKeys.Add(detectedMainKeys[0]);
		}
		return details;
	}

	private static ushort MapKeyStringToVk(string keyToken)
	{
		if (string.IsNullOrEmpty(keyToken))
		{
			return 0;
		}
		string text = keyToken.ToLower().Trim();
		if (text.StartsWith("d") && text.Length == 2 && char.IsDigit(text[1]))
		{
			return (ushort)text[1];
		}
		if (text.Length == 1)
		{
			char c = text[0];
			if (c >= 'a' && c <= 'z')
			{
				return (ushort)(65 + (c - 97));
			}
			if (c >= '0' && c <= '9')
			{
				return c;
			}
			switch (c)
			{
			case ';':
				return 186;
			case '+':
			case '=':
				return 187;
			case ',':
				return 188;
			case '-':
				return 189;
			case '.':
				return 190;
			case '/':
				return 191;
			case '`':
				return 192;
			case '[':
				return 219;
			case '\\':
				return 220;
			case ']':
				return 221;
			case '\'':
				return 222;
			}
		}
		if (text.StartsWith("f") && int.TryParse(text.Substring(1), out var result) && result >= 1 && result <= 24)
		{
			return (ushort)(112 + (result - 1));
		}
		if (text.StartsWith("num") || text.StartsWith("numpad"))
		{
			string text2 = text.Replace("numpad", "").Replace("num", "");
			if (int.TryParse(text2, out var result2) && result2 >= 0 && result2 <= 9)
			{
				return (ushort)(96 + result2);
			}
			switch (text2)
			{
			case "add":
			case "plus":
			case "+":
				return 107;
			case "subtract":
			case "minus":
			case "-":
				return 109;
			case "multiply":
			case "star":
			case "*":
				return 106;
			case "divide":
			case "slash":
			case "/":
				return 111;
			case "decimal":
			case "dot":
			case ".":
				return 110;
			}
		}
		switch (text)
		{
		case "left":
			return 37;
		case "up":
			return 38;
		case "right":
			return 39;
		case "down":
			return 40;
		case "home":
			return 36;
		case "end":
			return 35;
		case "pgup":
		case "prior":
		case "pageup":
			return 33;
		case "pgdn":
		case "next":
		case "pagedown":
			return 34;
		case "ins":
		case "insert":
			return 45;
		case "del":
		case "delete":
			return 46;
		case "back":
		case "backspace":
			return 8;
		case "tab":
			return 9;
		case "enter":
		case "return":
			return 13;
		case "esc":
		case "escape":
			return 27;
		case "space":
		case "spacebar":
			return 32;
		case "prtscn":
		case "prtsc":
		case "prntscrn":
		case "snapshot":
		case "printscreen":
		case "print_screen":
		case "print screen":
		case "print":
			return 44;
		case "pause":
			return 19;
		case "capslock":
			return 20;
		case "scrolllock":
			return 145;
		case "numlock":
			return 144;
		case "plus":
			return 187;
		case "minus":
			return 189;
		case "comma":
			return 188;
		case "dot":
		case "period":
			return 190;
		case "slash":
			return 191;
		case "backslash":
			return 220;
		case "semicolon":
			return 186;
		case "quote":
			return 222;
		case "bracketleft":
		case "openbracket":
			return 219;
		case "bracketright":
		case "closebracket":
			return 221;
		case "tilde":
		case "backquote":
			return 192;
		case "volumeup":
			return 175;
		case "volumedown":
			return 174;
		case "mute":
		case "volumemute":
			return 173;
		case "playpause":
		case "mediaplaypause":
			return 179;
		case "nexttrack":
		case "medianext":
			return 176;
		case "mediaprev":
		case "prevtrack":
			return 177;
		case "stopmedia":
		case "mediastop":
			return 178;
		case "browserback":
			return 166;
		case "browserforward":
			return 167;
		case "browserrefresh":
			return 168;
		case "browserstop":
			return 169;
		case "browsersearch":
			return 170;
		case "browserfavorites":
			return 171;
		case "browserhome":
			return 172;
		default:
			return 0;
		}
	}
}
