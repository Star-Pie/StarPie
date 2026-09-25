using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Input;

namespace WinPieGestures;

public class KeyboardHook : IDisposable
{
	private struct KBDLLHOOKSTRUCT
	{
		public uint vkCode;

		public uint scanCode;

		public uint flags;

		public uint time;

		public nint dwExtraInfo;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct POINT
	{
		public int x;

		public int y;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct MSG
	{
		public nint hwnd;
		public uint message;
		public nuint wParam;
		public nint lParam;
		public uint time;
		public POINT pt;
		public uint lPrivate;
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct MOUSEINPUT
	{
		public int dx;
		public int dy;
		public uint mouseData;
		public uint dwFlags;
		public uint time;
		public nint dwExtraInfo;
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct KEYBDINPUT
	{
		public ushort wVk;
		public ushort wScan;
		public uint dwFlags;
		public uint time;
		public nint dwExtraInfo;
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct HARDWAREINPUT
	{
		public uint uMsg;
		public ushort wParamL;
		public ushort wParamH;
	}

	[StructLayout(LayoutKind.Explicit)]
	internal struct InputUnion
	{
		[FieldOffset(0)]
		public MOUSEINPUT mi;

		[FieldOffset(0)]
		public KEYBDINPUT ki;

		[FieldOffset(0)]
		public HARDWAREINPUT hi;
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct INPUT
	{
		public uint type;

		public InputUnion U;
	}

	public const nint StarPieExtraInfo = 0x53544152;

	private delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

	private const int WH_KEYBOARD_LL = 13;

	private const int WM_KEYDOWN = 256;

	private const int WM_KEYUP = 257;

	private const int WM_SYSKEYDOWN = 260;

	private const int WM_SYSKEYUP = 261;

	private const uint WM_QUIT = 18u;

	private const uint PM_NOREMOVE = 0u;

	private const uint KEYEVENTF_EXTENDEDKEY = 1u;

	private const uint KEYEVENTF_KEYUP = 2u;

	private const uint INPUT_KEYBOARD = 1u;

	private readonly LowLevelKeyboardProc _proc;

	private nint _hookId = IntPtr.Zero;

	private readonly object _lifecycleSync = new object();

	private Thread? _hookThread;

	private uint _hookThreadId;

	private ManualResetEventSlim? _hookReady;

	private Exception? _hookStartException;

	private volatile bool _stopRequested;

	private int _isPaused;

	public bool IsPaused
	{
		get => Volatile.Read(ref _isPaused) != 0;
		set => Volatile.Write(ref _isPaused, value ? 1 : 0);
	}

	private int _suppressGlobalHotkeysForRecording;

	public bool SuppressGlobalHotkeysForRecording
	{
		get => Volatile.Read(ref _suppressGlobalHotkeysForRecording) != 0;
		set => Volatile.Write(ref _suppressGlobalHotkeysForRecording, value ? 1 : 0);
	}

	private readonly HashSet<uint> _exclusiveDownModifiers = new HashSet<uint>();
	private volatile bool _isDrainingModifiers;
	private volatile string? _pendingCompletedHotkey;
	private long _drainingStartTicks;

	public void StartExclusiveRecording()
	{
		KeyboardRemapController.Current.NotifyRecorderActive();
		lock (_exclusiveDownModifiers)
		{
			_exclusiveDownModifiers.Clear();
			_isDrainingModifiers = false;
			_pendingCompletedHotkey = null;
			_drainingStartTicks = 0;
		}
		SuppressGlobalHotkeysForRecording = true;
	}

	public void CancelExclusiveRecording()
	{
		lock (_exclusiveDownModifiers)
		{
			_exclusiveDownModifiers.Clear();
			_isDrainingModifiers = false;
			_pendingCompletedHotkey = null;
			_drainingStartTicks = 0;
		}
		SuppressGlobalHotkeysForRecording = false;
		OnExclusiveRecordCancelled?.Invoke();
	}

	public ModifierKeys GetExclusiveActiveModifiers()
	{
		ModifierKeys mods = ModifierKeys.None;
		lock (_exclusiveDownModifiers)
		{
			if (_exclusiveDownModifiers.Contains(17) || _exclusiveDownModifiers.Contains(162) || _exclusiveDownModifiers.Contains(163))
				mods |= ModifierKeys.Control;
			if (_exclusiveDownModifiers.Contains(16) || _exclusiveDownModifiers.Contains(160) || _exclusiveDownModifiers.Contains(161))
				mods |= ModifierKeys.Shift;
			if (_exclusiveDownModifiers.Contains(18) || _exclusiveDownModifiers.Contains(164) || _exclusiveDownModifiers.Contains(165))
				mods |= ModifierKeys.Alt;
			if (_exclusiveDownModifiers.Contains(91) || _exclusiveDownModifiers.Contains(92))
				mods |= ModifierKeys.Windows;
		}

		if ((GetAsyncKeyState(17) & 0x8000) != 0) mods |= ModifierKeys.Control;
		if ((GetAsyncKeyState(16) & 0x8000) != 0) mods |= ModifierKeys.Shift;
		if ((GetAsyncKeyState(18) & 0x8000) != 0) mods |= ModifierKeys.Alt;
		if ((GetAsyncKeyState(91) & 0x8000) != 0 || (GetAsyncKeyState(92) & 0x8000) != 0) mods |= ModifierKeys.Windows;

		return mods;
	}

	public event Action? OnExclusiveRecordCancelled;

	public event Action<string>? OnExclusiveRecordCompleted;

	public event Action<ModifierKeys>? OnExclusiveRecordModifiersChanged;

	public static bool IsModifierVk(uint vkCode)
	{
		return vkCode == 16 || vkCode == 160 || vkCode == 161 ||
		       vkCode == 17 || vkCode == 162 || vkCode == 163 ||
		       vkCode == 18 || vkCode == 164 || vkCode == 165 ||
		       vkCode == 91 || vkCode == 92;
	}

	public event EventHandler<GlobalKeyEventArgs>? OnKeyDown;

	public event EventHandler<GlobalKeyEventArgs>? OnKeyUp;

	public event EventHandler<GlobalKeyEventArgs>? OnRawKeyEvent;

	[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	private static extern nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

	[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool UnhookWindowsHookEx(nint hhk);

	[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

	[DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	private static extern nint GetModuleHandle(string lpModuleName);

	[DllImport("kernel32.dll")]
	private static extern uint GetCurrentThreadId();

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool PeekMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern int GetMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool TranslateMessage(ref MSG lpMsg);

	[DllImport("user32.dll")]
	private static extern nint DispatchMessage(ref MSG lpMsg);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool PostThreadMessage(uint idThread, uint msg, nuint wParam, nint lParam);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

	[DllImport("user32.dll")]
	private static extern uint MapVirtualKey(uint uCode, uint uMapType);

	[DllImport("user32.dll")]
	private static extern short GetAsyncKeyState(int nVirtKey);

	public KeyboardHook()
	{
		_proc = HookCallback;
	}

	public void Start()
	{
		ManualResetEventSlim ready;
		lock (_lifecycleSync)
		{
			if (_hookThread != null && _hookThread.IsAlive)
			{
				return;
			}
			_stopRequested = false;
			_hookStartException = null;
			ready = new ManualResetEventSlim(initialState: false);
			_hookReady = ready;
			_hookThread = new Thread(HookThreadMain)
			{
				IsBackground = true,
				Name = "StarPie.KeyboardHook",
				Priority = ThreadPriority.AboveNormal
			};
			_hookThread.Start();
		}

		try
		{
			if (!ready.Wait(TimeSpan.FromSeconds(5)))
			{
				throw new TimeoutException("Timed out while starting the low-level keyboard hook.");
			}
			Exception? startException;
			lock (_lifecycleSync)
			{
				startException = _hookStartException;
			}
			if (startException != null)
			{
				throw new Exception("Failed to set low-level keyboard hook.", startException);
			}
		}
		catch
		{
			Stop();
			throw;
		}
	}

	public void Stop()
	{
		Thread? hookThread;
		uint hookThreadId;
		lock (_lifecycleSync)
		{
			_stopRequested = true;
			hookThread = _hookThread;
			hookThreadId = _hookThreadId;
		}

		if (hookThreadId != 0)
		{
			PostThreadMessage(hookThreadId, WM_QUIT, 0u, IntPtr.Zero);
		}

		if (hookThread != null && hookThread.ManagedThreadId != Environment.CurrentManagedThreadId)
		{
			hookThread.Join(TimeSpan.FromMilliseconds(500));
		}

		lock (_lifecycleSync)
		{
			if (_hookThread == hookThread && (hookThread == null || !hookThread.IsAlive))
			{
				_hookThread = null;
				_hookThreadId = 0;
				_hookReady?.Dispose();
				_hookReady = null;
				_hookId = IntPtr.Zero;
			}
		}
	}

	private void HookThreadMain()
	{
		nint hookId = IntPtr.Zero;
		uint threadId = GetCurrentThreadId();
		lock (_lifecycleSync)
		{
			_hookThreadId = threadId;
		}

		try
		{
			PeekMessage(out MSG _, IntPtr.Zero, 0u, 0u, PM_NOREMOVE);
			hookId = SetHook(_proc);
			if (hookId == IntPtr.Zero)
			{
				throw new InvalidOperationException("SetWindowsHookEx returned a null keyboard hook handle.");
			}
			lock (_lifecycleSync)
			{
				_hookId = hookId;
			}
			_hookReady?.Set();

			MSG message;
			while (!_stopRequested)
			{
				int result = GetMessage(out message, IntPtr.Zero, 0u, 0u);
				if (result <= 0)
				{
					break;
				}
				TranslateMessage(ref message);
				DispatchMessage(ref message);
			}
		}
		catch (Exception ex)
		{
			lock (_lifecycleSync)
			{
				_hookStartException = ex;
			}
			_hookReady?.Set();
		}
		finally
		{
			if (hookId != IntPtr.Zero)
			{
				UnhookWindowsHookEx(hookId);
			}
			lock (_lifecycleSync)
			{
				if (_hookId == hookId)
				{
					_hookId = IntPtr.Zero;
				}
				_hookThreadId = 0;
			}
			_hookReady?.Set();
		}
	}

	private nint SetHook(LowLevelKeyboardProc proc)
	{
		using Process process = Process.GetCurrentProcess();
		using ProcessModule processModule = process.MainModule;
		if (processModule == null)
		{
			throw new InvalidOperationException("MainModule is null.");
		}
		return SetWindowsHookEx(13, proc, GetModuleHandle(processModule.ModuleName), 0u);
	}

	public static ModifierKeys GetCurrentModifiers()
	{
		ModifierKeys val = ModifierKeys.None;
		if ((GetAsyncKeyState(17) & 0x8000) != 0)
		{
			val |= ModifierKeys.Control;
		}
		if ((GetAsyncKeyState(16) & 0x8000) != 0)
		{
			val |= ModifierKeys.Shift;
		}
		if ((GetAsyncKeyState(18) & 0x8000) != 0)
		{
			val |= ModifierKeys.Alt;
		}
		if ((GetAsyncKeyState(91) & 0x8000) != 0 || (GetAsyncKeyState(92) & 0x8000) != 0)
		{
			val |= ModifierKeys.Windows;
		}
		return val;
	}

	private nint HookCallback(int nCode, nint wParam, nint lParam)
	{
		if (nCode >= 0)
		{
			KBDLLHOOKSTRUCT kbd = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
			if (kbd.dwExtraInfo == StarPieExtraInfo)
			{
				// StarPie 自发模拟的按键直接快速放行，杜绝自身捕获与竞争
				return CallNextHookEx(_hookId, nCode, wParam, lParam);
			}

			int num = (int)wParam;
			uint vkCode = kbd.vkCode;
			ModifierKeys currentModifiers = GetCurrentModifiers();

			if (SuppressGlobalHotkeysForRecording)
			{
				bool isDown = (num == WM_KEYDOWN || num == WM_SYSKEYDOWN);
				bool isUp = (num == WM_KEYUP || num == WM_SYSKEYUP);

				if (_isDrainingModifiers)
				{
					// 录制完成排空阶段：持续吞没按键松开事件，直到用户释放所有物理按键
					if (isUp && IsModifierVk(vkCode))
					{
						lock (_exclusiveDownModifiers)
						{
							_exclusiveDownModifiers.Remove(vkCode);
						}
					}

					bool anyModPhysicallyDown = false;
					lock (_exclusiveDownModifiers)
					{
						if (_exclusiveDownModifiers.Count > 0) anyModPhysicallyDown = true;
					}
					if (!anyModPhysicallyDown)
					{
						anyModPhysicallyDown = (GetAsyncKeyState(16) & 0x8000) != 0 ||
						                       (GetAsyncKeyState(17) & 0x8000) != 0 ||
						                       (GetAsyncKeyState(18) & 0x8000) != 0 ||
						                       (GetAsyncKeyState(91) & 0x8000) != 0 ||
						                       (GetAsyncKeyState(92) & 0x8000) != 0;
					}

					// 超过 1.5 秒安全超时强制解除排空
					bool timeout = _drainingStartTicks > 0 && (DateTime.UtcNow.Ticks - _drainingStartTicks > TimeSpan.FromSeconds(1.5).Ticks);

					if (!anyModPhysicallyDown || timeout)
					{
						_isDrainingModifiers = false;
						SuppressGlobalHotkeysForRecording = false;
					}
					return 1; // 吞没全部排空事件，彻底杜绝 Win 开始菜单与 Alt 系统菜单弹出
				}

				if (isDown)
				{
					if (vkCode == 27) // Escape
					{
						ModifierKeys mods = GetExclusiveActiveModifiers();
						if (mods == ModifierKeys.None)
						{
							// 单纯按下 Esc：取消独占录制
							_isDrainingModifiers = true;
							_drainingStartTicks = DateTime.UtcNow.Ticks;
							OnExclusiveRecordCancelled?.Invoke();
							return 1;
						}
					}

					if (IsModifierVk(vkCode))
					{
						lock (_exclusiveDownModifiers)
						{
							_exclusiveDownModifiers.Add(vkCode);
						}
						ModifierKeys mods = GetExclusiveActiveModifiers();
						OnExclusiveRecordModifiersChanged?.Invoke(mods);
						return 1; // 吞没修饰键按下
					}

					// 普通主按键按下：获取当前捕获的修饰键组合并构建热键字符串
					ModifierKeys currentMods = GetExclusiveActiveModifiers();
					Key key = KeyInterop.KeyFromVirtualKey((int)vkCode);
					string hotkeyStr = HotkeyRecorderBox.BuildHotkeyString(key, currentMods);
					if (!string.IsNullOrEmpty(hotkeyStr))
					{
						_pendingCompletedHotkey = hotkeyStr;
						_isDrainingModifiers = true;
						_drainingStartTicks = DateTime.UtcNow.Ticks;
						OnExclusiveRecordCompleted?.Invoke(hotkeyStr);
					}
					return 1; // 吞没主按键按下，防止触发 Win+D、Alt+Tab 等系统与外部热键
				}
				else if (isUp)
				{
					if (IsModifierVk(vkCode))
					{
						lock (_exclusiveDownModifiers)
						{
							_exclusiveDownModifiers.Remove(vkCode);
						}
						ModifierKeys mods = GetExclusiveActiveModifiers();
						OnExclusiveRecordModifiersChanged?.Invoke(mods);
					}
					return 1; // 吞没按键松开
				}
			}

			if (IsPaused)
			{
				return CallNextHookEx(_hookId, nCode, wParam, lParam);
			}

			if (KeyboardRemapController.Current.TryProcessHookEvent(vkCode, (int)num, kbd.flags, kbd.time))
			{
				return 1;
			}

			GlobalKeyEventArgs e = new GlobalKeyEventArgs(vkCode, currentModifiers);
			OnRawKeyEvent?.Invoke(this, e);

			switch (num)
			{
			case WM_KEYDOWN:
			case WM_SYSKEYDOWN:
			{
				GlobalKeyEventArgs e3 = new GlobalKeyEventArgs(vkCode, currentModifiers);
				OnKeyDown?.Invoke(this, e3);
				if (e3.Handled)
				{
					return 1;
				}
				break;
			}
			case WM_KEYUP:
			case WM_SYSKEYUP:
			{
				GlobalKeyEventArgs e2 = new GlobalKeyEventArgs(vkCode, currentModifiers);
				OnKeyUp?.Invoke(this, e2);
				if (e2.Handled)
				{
					return 1;
				}
				break;
			}
			}
		}
		return CallNextHookEx(_hookId, nCode, wParam, lParam);
	}

	public void ReplayKeyPress(uint vkCode)
	{
		if (vkCode == 0) return;

		ushort scan = (ushort)MapVirtualKey(vkCode, 0u);
		INPUT down = new INPUT
		{
			type = INPUT_KEYBOARD,
			U = new InputUnion
			{
				ki = new KEYBDINPUT
				{
					wVk = (ushort)vkCode,
					wScan = scan,
					dwFlags = 0u,
					time = 0u,
					dwExtraInfo = StarPieExtraInfo
				}
			}
		};
		INPUT up = new INPUT
		{
			type = INPUT_KEYBOARD,
			U = new InputUnion
			{
				ki = new KEYBDINPUT
				{
					wVk = (ushort)vkCode,
					wScan = scan,
					dwFlags = KEYEVENTF_KEYUP,
					time = 0u,
					dwExtraInfo = StarPieExtraInfo
				}
			}
		};

		if (vkCode == 33 || vkCode == 34 || vkCode == 35 || vkCode == 36 ||
		    vkCode == 37 || vkCode == 38 || vkCode == 39 || vkCode == 40 ||
		    vkCode == 44 || vkCode == 45 || vkCode == 46 ||
		    vkCode == 91 || vkCode == 92 || vkCode == 111 ||
		    (vkCode >= 166 && vkCode <= 179))
		{
			down.U.ki.dwFlags |= KEYEVENTF_EXTENDEDKEY;
			up.U.ki.dwFlags |= KEYEVENTF_EXTENDEDKEY;
		}

		SendInput(2u, new INPUT[] { down, up }, Marshal.SizeOf(typeof(INPUT)));
	}

	public void Dispose()
	{
		Stop();
	}
}
