using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Windows.Input;
using StarPie.Plugin;

namespace WinPieGestures;

/// <summary>
/// 键盘映射单条预编译目标项，存储预先计算好的虚拟键码、硬件扫描码及扩展键标志。
/// </summary>
internal struct RemapTarget
{
	public ushort TargetVk;
	public ushort TargetScan;
	public uint TargetFlags;
}

/// <summary>会话撤销原因。</summary>
internal enum RevokeReason
{
	ExplicitDeactivate,
	FocusLost,
	EmergencyEscape,
	RecorderPriority,
	HostPaused,
	PluginStopping,
	HostShutdown,
}

/// <summary>
/// 键盘映射编解码器，提供统一的序列化、反序列化、规范化键名及默认预设管理。
/// 严格采用带显式版本前缀（"v1|"）的不可歧义协议格式。
/// </summary>
public static class KeyMapCodec
{
	public const string VersionPrefix = "v1|";

	private static readonly Dictionary<string, uint> CustomKeyNameToVk = new(StringComparer.OrdinalIgnoreCase)
	{
		{ "Num0", 0x60 }, { "NumPad0", 0x60 }, { "NP0", 0x60 },
		{ "Num1", 0x61 }, { "NumPad1", 0x61 }, { "NP1", 0x61 },
		{ "Num2", 0x62 }, { "NumPad2", 0x62 }, { "NP2", 0x62 },
		{ "Num3", 0x63 }, { "NumPad3", 0x63 }, { "NP3", 0x63 },
		{ "Num4", 0x64 }, { "NumPad4", 0x64 }, { "NP4", 0x64 },
		{ "Num5", 0x65 }, { "NumPad5", 0x65 }, { "NP5", 0x65 },
		{ "Num6", 0x66 }, { "NumPad6", 0x66 }, { "NP6", 0x66 },
		{ "Num7", 0x67 }, { "NumPad7", 0x67 }, { "NP7", 0x67 },
		{ "Num8", 0x68 }, { "NumPad8", 0x68 }, { "NP8", 0x68 },
		{ "Num9", 0x69 }, { "NumPad9", 0x69 }, { "NP9", 0x69 },
		{ "NumMultiply", 0x6A }, { "NumPadMultiply", 0x6A }, { "Num*", 0x6A },
		{ "NumAdd", 0x6B }, { "NumPadAdd", 0x6B }, { "Num+", 0x6B },
		{ "NumSubtract", 0x6D }, { "NumPadSubtract", 0x6D }, { "Num-", 0x6D },
		{ "NumDecimal", 0x6E }, { "NumPadDecimal", 0x6E }, { "Num.", 0x6E },
		{ "NumDivide", 0x6F }, { "NumPadDivide", 0x6F }, { "Num/", 0x6F },
		{ "Esc", 0x1B }, { "Escape", 0x1B },
		{ "Enter", 0x0D }, { "Return", 0x0D },
		{ "Space", 0x20 },
		{ "Tab", 0x09 },
		{ "Backspace", 0x08 }, { "Back", 0x08 },
		{ "Delete", 0x2E }, { "Del", 0x2E },
		{ "Insert", 0x2D }, { "Ins", 0x2D },
		{ "Home", 0x24 }, { "End", 0x23 },
		{ "PageUp", 0x21 }, { "PgUp", 0x21 },
		{ "PageDown", 0x22 }, { "PgDn", 0x22 },
		{ "Up", 0x26 }, { "Down", 0x28 }, { "Left", 0x25 }, { "Right", 0x27 },
		{ ".", 0xBE }, { "OemPeriod", 0xBE }, { "Period", 0xBE }, { "Dot", 0xBE },
		{ ",", 0xBC }, { "OemComma", 0xBC }, { "Comma", 0xBC },
		{ "-", 0xBD }, { "OemMinus", 0xBD }, { "Minus", 0xBD },
		{ "=", 0xBB }, { "OemPlus", 0xBB }, { "Plus", 0xBB }, { "Equal", 0xBB }, { "Equals", 0xBB },
		{ ";", 0xBA }, { "OemSemicolon", 0xBA }, { "Oem1", 0xBA }, { "Semicolon", 0xBA },
		{ "/", 0xBF }, { "OemQuestion", 0xBF }, { "Oem2", 0xBF }, { "Slash", 0xBF },
		{ "`", 0xC0 }, { "OemTilde", 0xC0 }, { "Oem3", 0xC0 }, { "Tilde", 0xC0 }, { "Backquote", 0xC0 },
		{ "[", 0xDB }, { "OemOpenBrackets", 0xDB }, { "Oem4", 0xDB }, { "LeftBracket", 0xDB },
		{ "\\", 0xDC }, { "OemPipe", 0xDC }, { "Oem5", 0xDC }, { "Backslash", 0xDC },
		{ "]", 0xDD }, { "OemCloseBrackets", 0xDD }, { "Oem6", 0xDD }, { "RightBracket", 0xDD },
		{ "'", 0xDE }, { "OemQuotes", 0xDE }, { "Oem7", 0xDE }, { "Quote", 0xDE },
	};

	/// <summary>
	/// 获取默认空间数字键盘预设（10组空间映射）：
	/// Q/W/E → Num7/Num8/Num9, A/S/D → Num4/Num5/Num6, Z/X/C → Num1/Num2/Num3, R → Num0
	/// </summary>
	public static IReadOnlyList<KeyboardRemapEntry> GetDefaultSpatialPreset()
	{
		return new List<KeyboardRemapEntry>
		{
			new() { FromKey = "Q", ToKey = "Num7" },
			new() { FromKey = "W", ToKey = "Num8" },
			new() { FromKey = "E", ToKey = "Num9" },
			new() { FromKey = "A", ToKey = "Num4" },
			new() { FromKey = "S", ToKey = "Num5" },
			new() { FromKey = "D", ToKey = "Num6" },
			new() { FromKey = "Z", ToKey = "Num1" },
			new() { FromKey = "X", ToKey = "Num2" },
			new() { FromKey = "C", ToKey = "Num3" },
			new() { FromKey = "R", ToKey = "Num0" },
		};
	}

	/// <summary>
	/// 编码映射表为规范化版本协议字符串，形如 "v1|Q:Num7,W:Num8,E:Num9"
	/// </summary>
	public static string Encode(IEnumerable<KeyboardRemapEntry>? entries)
	{
		if (entries == null) return "";
		var valid = entries
			.Where(e => !string.IsNullOrWhiteSpace(e.FromKey) && !string.IsNullOrWhiteSpace(e.ToKey))
			.Select(e => $"{NormalizeKeyName(e.FromKey)}:{NormalizeKeyName(e.ToKey)}")
			.ToList();
		if (valid.Count == 0) return "";
		return VersionPrefix + string.Join(",", valid);
	}

	/// <summary>
	/// 严格尝试解码版本化映射配置字符串。
	/// 必须以 "v1|" 开头，且所有按键名及分隔符必须完整合法；否则返回 false 并给出详细错误。
	/// </summary>
	public static bool TryDecode(string? text, out List<KeyboardRemapEntry>? entries, out string? error)
	{
		entries = null;
		error = null;

		if (string.IsNullOrWhiteSpace(text))
		{
			entries = new List<KeyboardRemapEntry>();
			return true;
		}

		string trimmed = text.Trim();
		if (!trimmed.StartsWith(VersionPrefix, StringComparison.OrdinalIgnoreCase))
		{
			error = I18n.T("KeyMapErrUnsupportedVersion");
			return false;
		}

		string payload = trimmed.Substring(VersionPrefix.Length).Trim();
		var list = new List<KeyboardRemapEntry>();
		if (string.IsNullOrEmpty(payload))
		{
			entries = list;
			return true;
		}

		string[] pairs = payload.Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
		foreach (string pair in pairs)
		{
			string p = pair.Trim();
			if (string.IsNullOrEmpty(p)) continue;

			string[] parts;
			if (p.Contains("->")) parts = p.Split(new[] { "->" }, StringSplitOptions.None);
			else if (p.Contains(':')) parts = p.Split(':');
			else if (p.Contains('=')) parts = p.Split('=');
			else
			{
				error = string.Format(I18n.T("KeyMapErrMissingSeparator"), p);
				return false;
			}

			if (parts.Length != 2)
			{
				error = string.Format(I18n.T("KeyMapErrMissingSeparator"), p);
				return false;
			}

			string fromRaw = parts[0].Trim();
			string toRaw = parts[1].Trim();

			if (string.IsNullOrEmpty(fromRaw) || string.IsNullOrEmpty(toRaw))
			{
				error = string.Format(I18n.T("KeyMapErrMissingSeparator"), p);
				return false;
			}

			if (!TryGetKeyVk(fromRaw, out _))
			{
				error = string.Format(I18n.T("KeyMapErrUnknownSourceKey"), fromRaw);
				return false;
			}

			if (!TryGetKeyVk(toRaw, out _))
			{
				error = string.Format(I18n.T("KeyMapErrUnknownTargetKey"), toRaw);
				return false;
			}

			list.Add(new KeyboardRemapEntry
			{
				FromKey = NormalizeKeyName(fromRaw),
				ToKey = NormalizeKeyName(toRaw)
			});
		}

		entries = list;
		return true;
	}

	/// <summary>
	/// 解码映射配置字符串。若格式无效返回空集合。
	/// </summary>
	public static IReadOnlyList<KeyboardRemapEntry> Decode(string? text)
	{
		if (TryDecode(text, out var entries, out _) && entries != null)
		{
			return entries;
		}
		return Array.Empty<KeyboardRemapEntry>();
	}

	/// <summary>
	/// 规范化按键名称（大小写与常见别名归一化）
	/// </summary>
	public static string NormalizeKeyName(string? raw)
	{
		if (string.IsNullOrWhiteSpace(raw)) return "";
		string trimmed = raw.Trim();

		if (CustomKeyNameToVk.TryGetValue(trimmed, out uint vk))
		{
			return GetCanonicalKeyName(vk);
		}

		if (Enum.TryParse<Key>(trimmed, true, out Key key))
		{
			uint parsedVk = (uint)KeyInterop.VirtualKeyFromKey(key);
			return GetCanonicalKeyName(parsedVk);
		}

		if (trimmed.Length == 1 && char.IsLetterOrDigit(trimmed[0]))
		{
			return char.ToUpperInvariant(trimmed[0]).ToString();
		}

		return trimmed;
	}

	/// <summary>
	/// 尝试将按键字符串解析为 Windows Virtual-Key 码（1~254）
	/// </summary>
	public static bool TryGetKeyVk(string? keyName, out uint vk)
	{
		vk = 0;
		if (string.IsNullOrWhiteSpace(keyName)) return false;
		string trimmed = keyName.Trim();

		if (CustomKeyNameToVk.TryGetValue(trimmed, out uint customVk))
		{
			vk = customVk;
			return true;
		}

		if (trimmed.Length == 1)
		{
			char c = char.ToUpperInvariant(trimmed[0]);
			if (c >= 'A' && c <= 'Z')
			{
				vk = (uint)c;
				return true;
			}
			if (c >= '0' && c <= '9')
			{
				vk = (uint)c;
				return true;
			}
		}

		if (Enum.TryParse<Key>(trimmed, true, out Key key))
		{
			int v = KeyInterop.VirtualKeyFromKey(key);
			if (v > 0 && v < 255)
			{
				vk = (uint)v;
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// 获取虚拟键码的标准显示名称
	/// </summary>
	public static string GetCanonicalKeyName(uint vk)
	{
		if (vk >= 0x60 && vk <= 0x69)
		{
			return $"Num{vk - 0x60}";
		}

		return vk switch
		{
			0x6A => "NumMultiply",
			0x6B => "NumAdd",
			0x6D => "NumSubtract",
			0x6E => "NumDecimal",
			0x6F => "NumDivide",
			0x1B => "Escape",
			0x0D => "Enter",
			0x20 => "Space",
			0x09 => "Tab",
			0x08 => "Backspace",
			0x2E => "Delete",
			0x2D => "Insert",
			0x24 => "Home",
			0x23 => "End",
			0x21 => "PageUp",
			0x22 => "PageDown",
			0x26 => "Up",
			0x28 => "Down",
			0x25 => "Left",
			0x27 => "Right",
			0xBE => "OemPeriod",
			0xBC => "OemComma",
			0xBD => "OemMinus",
			0xBB => "OemPlus",
			0xBA => "OemSemicolon",
			0xBF => "OemQuestion",
			0xC0 => "OemTilde",
			0xDB => "OemOpenBrackets",
			0xDC => "OemPipe",
			0xDD => "OemCloseBrackets",
			0xDE => "OemQuotes",
			>= 0x41 and <= 0x5A => ((char)vk).ToString(),
			>= 0x30 and <= 0x39 => ((char)vk).ToString(),
			_ => KeyInterop.KeyFromVirtualKey((int)vk).ToString()
		};
	}

	/// <summary>
	/// 获取按键的友好人类可读显示文本（例如普通句点显示为 "."，小键盘句点显示为 "Num ."）。
	/// </summary>
	public static string GetFriendlyKeyDisplayName(string? keyName)
	{
		if (string.IsNullOrWhiteSpace(keyName)) return "";
		string normalized = NormalizeKeyName(keyName);
		return normalized switch
		{
			"OemPeriod" => ".",
			"OemComma" => ",",
			"OemMinus" => "-",
			"OemPlus" => "=",
			"OemSemicolon" or "Oem1" => ";",
			"OemQuestion" or "Oem2" => "/",
			"OemTilde" or "Oem3" => "`",
			"OemOpenBrackets" or "Oem4" => "[",
			"OemPipe" or "Oem5" => "\\",
			"OemCloseBrackets" or "Oem6" => "]",
			"OemQuotes" or "Oem7" => "'",
			"NumDecimal" => "Num .",
			"NumMultiply" => "Num *",
			"NumDivide" => "Num /",
			"NumAdd" => "Num +",
			"NumSubtract" => "Num -",
			_ => normalized
		};
	}
}

/// <summary>
/// 键盘映射校验器，供参数表单、可视化编辑器及运行时激活三方共享。
/// </summary>
public static class KeyMapValidator
{
	private const int MaxMappingEntries = 64;

	private static readonly HashSet<uint> ForbiddenModifierVks = new()
	{
		16, 160, 161, // Shift, LShift, RShift
		17, 162, 163, // Ctrl, LCtrl, RCtrl
		18, 164, 165, // Alt, LAlt, RAlt
		91, 92,       // LWin, RWin
		93,           // Apps
	};

	/// <summary>判断指定虚拟键码是否为禁止作为单键映射的修饰键。</summary>
	public static bool IsModifierVk(uint vk)
	{
		return ForbiddenModifierVks.Contains(vk);
	}

	/// <summary>
	/// 校验序列化字符串。返回 null 表示校验通过，否则返回错误提示。
	/// 非空字符串若存在版本不支持、格式错误或无法解析项，必须返回明确错误信息。
	/// </summary>
	public static string? Validate(string? serialized)
	{
		if (string.IsNullOrWhiteSpace(serialized)) return null;
		if (!KeyMapCodec.TryDecode(serialized, out var entries, out string? decodeError))
		{
			return decodeError ?? I18n.T("KeyMapErrUnsupportedVersion");
		}
		return Validate(entries);
	}

	/// <summary>
	/// 校验映射规则集合。
	/// </summary>
	public static string? Validate(IEnumerable<KeyboardRemapEntry>? entries)
	{
		if (entries == null) return null;
		var list = entries.ToList();
		if (list.Count == 0) return null;

		if (list.Count > MaxMappingEntries)
		{
			return string.Format(I18n.T("KeyMapErrMaxEntriesExceeded"), list.Count, MaxMappingEntries);
		}

		var seenFromVks = new HashSet<uint>();

		foreach (var entry in list)
		{
			if (entry == null || string.IsNullOrWhiteSpace(entry.FromKey) || string.IsNullOrWhiteSpace(entry.ToKey))
			{
				return I18n.T("KeyMapEditorErrEmptyKeys");
			}

			if (!KeyMapCodec.TryGetKeyVk(entry.FromKey, out uint fromVk))
			{
				return string.Format(I18n.T("KeyMapErrUnknownSourceKey"), entry.FromKey);
			}

			if (!KeyMapCodec.TryGetKeyVk(entry.ToKey, out uint toVk))
			{
				return string.Format(I18n.T("KeyMapErrUnknownTargetKey"), entry.ToKey);
			}

			if (ForbiddenModifierVks.Contains(fromVk))
			{
				return string.Format(I18n.T("KeyMapErrModifierAsSource"), entry.FromKey);
			}

			if (ForbiddenModifierVks.Contains(toVk))
			{
				return string.Format(I18n.T("KeyMapErrModifierAsTarget"), entry.ToKey);
			}

			if (fromVk == 0x1B) // VK_ESCAPE
			{
				return I18n.T("KeyMapErrEscapeReserved");
			}

			if (!seenFromVks.Add(fromVk))
			{
				return string.Format(I18n.T("KeyMapErrDuplicateSource"), entry.FromKey);
			}
		}

		return null;
	}
}

/// <summary>
/// 宿主管理的进程级键盘重映射控制器（单例）。
/// 挂接在宿主唯一的低级键盘钩子路径上，保证高频无堆分配、扫描码硬件注入、失焦安全释放与强制长按 Esc 紧急撤销。
/// </summary>
public sealed class KeyboardRemapController
{
	private static readonly Lazy<KeyboardRemapController> _instance =
		new(() => new KeyboardRemapController());

	public static KeyboardRemapController Current => _instance.Value;

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

	internal const uint INPUT_KEYBOARD = 1u;
	internal const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
	internal const uint KEYEVENTF_KEYUP = 0x0002;
	internal const uint KEYEVENTF_SCANCODE = 0x0008;

	private const int WM_KEYDOWN = 256;
	private const int WM_KEYUP = 257;
	private const int WM_SYSKEYDOWN = 260;
	private const int WM_SYSKEYUP = 261;

	[DllImport("user32.dll")]
	private static extern nint GetForegroundWindow();

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

	[DllImport("user32.dll")]
	private static extern uint MapVirtualKey(uint uCode, uint uMapType);

	private readonly object _stateLock = new();
	private readonly object _inputLock = new();

	private static readonly RemapTarget[] EmptyMap = new RemapTarget[256];
	private volatile RemapTarget[] _activeMap = EmptyMap;

	private volatile bool _isActive;
	private int _sessionGeneration;
	private string _activePluginId = "";
	private uint _targetProcessId;
	private string _targetProcessName = "";
	private DateTime? _activatedAtUtc;

	private readonly bool[] _heldPhysicalSourceKeys = new bool[256];
	private readonly bool[] _passthroughPhysicalSourceKeys = new bool[256];
	private readonly byte[] _targetHoldCounts = new byte[256];

	private readonly INPUT[] _singleInputBuffer = new INPUT[1];

	private long _escapeDownTicks;
	private System.Threading.Timer? _escapeEmergencyTimer;

	private nint _cachedForegroundHwnd;
	private uint _cachedForegroundPid;

	private long _injectionSuccessCount;
	private long _injectionFailureCount;
	private int _lastInjectionError;

	// 测试注入下沉、注入失败模拟与前台进程模拟覆盖
	internal delegate void KeyboardInputEventSink(ushort scan, uint flags, bool isKeyUp);
	private KeyboardInputEventSink? _testEventSink;
	private Func<ushort, uint, bool, bool>? _testInjectionFailureMock;
	private nint _testForegroundHwnd;
	private uint _testForegroundPid;
	private bool _hasTestForeground;

	internal void SetTestEventSink(KeyboardInputEventSink? sink)
	{
		lock (_stateLock)
		{
			_testEventSink = sink;
		}
	}

	internal void SetTestInjectionFailureMock(Func<ushort, uint, bool, bool>? mock)
	{
		lock (_stateLock)
		{
			_testInjectionFailureMock = mock;
		}
	}

	/// <summary>
	/// 获取注入诊断数据：成功次数、失败次数与最近一次 Win32 错误码。
	/// 钩子热路径仅使用轻量级原子操作更新本数据，绝不执行同步日志与 IO。
	/// </summary>
	public (long SuccessCount, long FailureCount, int LastWin32Error) GetInjectionDiagnostics()
	{
		return (
			Interlocked.Read(ref _injectionSuccessCount),
			Interlocked.Read(ref _injectionFailureCount),
			Volatile.Read(ref _lastInjectionError)
		);
	}

	/// <summary>获取格式化诊断快照字符串（供离线、管理界面或自检输出）。</summary>
	public string GetDiagnosticsReport()
	{
		var (succ, fail, err) = GetInjectionDiagnostics();
		return $"Success={succ}, Failure={fail}, LastWin32Error={err}";
	}

	/// <summary>重置注入诊断指标。</summary>
	internal void ResetInjectionDiagnostics()
	{
		Interlocked.Exchange(ref _injectionSuccessCount, 0);
		Interlocked.Exchange(ref _injectionFailureCount, 0);
		Volatile.Write(ref _lastInjectionError, 0);
	}

	internal int GetTargetHoldCount(ushort targetVk)
	{
		lock (_stateLock)
		{
			return targetVk < 256 ? _targetHoldCounts[targetVk] : 0;
		}
	}

	internal void SetTestForegroundProcess(nint hwnd, uint pid)
	{
		lock (_stateLock)
		{
			_testForegroundHwnd = hwnd;
			_testForegroundPid = pid;
			_hasTestForeground = true;
		}
	}

	internal void ClearTestForegroundProcess()
	{
		lock (_stateLock)
		{
			_hasTestForeground = false;
			_testForegroundHwnd = 0;
			_testForegroundPid = 0;
		}
	}

	internal void SetEscapeDownTicksForTest(long ticks)
	{
		lock (_stateLock)
		{
			_escapeDownTicks = ticks;
		}
	}

	internal void TriggerEscapeTimerTickForTest()
	{
		OnEscapeTimerTick(null);
	}

	/// <summary>
	/// 规范化进程名以支持带/不带 .exe 扩展名及全路径的进程名匹配（保持严格 PID 门禁的前提下允许形态差异）。
	/// </summary>
	public static string NormalizeProcessName(string? processName)
	{
		if (string.IsNullOrWhiteSpace(processName)) return "";
		string name = processName.Trim();
		try
		{
			name = Path.GetFileName(name);
		}
		catch
		{
		}
		if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
		{
			name = name.Substring(0, name.Length - 4);
		}
		return name.Trim();
	}

	/// <summary>
	/// 构造用于硬件扫描码注入的 INPUT 结构体（供生产注入及单元自检确定性断言格式使用）。
	/// 规范要求：wVk 必须为 0，dwFlags 必须包含 KEYEVENTF_SCANCODE，dwExtraInfo 标记 StarPie 标识。
	/// </summary>
	internal static INPUT FormatKeyboardInput(ushort scan, uint flags, bool isKeyUp)
	{
		INPUT input = default;
		input.type = INPUT_KEYBOARD;
		input.U.ki.wVk = 0;
		input.U.ki.wScan = scan;
		input.U.ki.dwFlags = KEYEVENTF_SCANCODE | flags | (isKeyUp ? KEYEVENTF_KEYUP : 0);
		input.U.ki.time = 0;
		input.U.ki.dwExtraInfo = KeyboardHook.StarPieExtraInfo;
		return input;
	}

	private KeyboardRemapController()
	{
		_escapeEmergencyTimer = new System.Threading.Timer(OnEscapeTimerTick, null, Timeout.Infinite, Timeout.Infinite);
	}

	private void OnEscapeTimerTick(object? state)
	{
		if (!_isActive) return;
		lock (_stateLock)
		{
			if (_isActive && _escapeDownTicks > 0)
			{
				double elapsed = (double)(Stopwatch.GetTimestamp() - _escapeDownTicks) / Stopwatch.Frequency;
				if (elapsed >= 1.5)
				{
					_escapeDownTicks = 0;
					DeactivateInternal(RevokeReason.EmergencyEscape);
				}
			}
		}
	}

	/// <summary>获取当前键盘映射会话快照。</summary>
	public KeyboardRemapStatus GetStatus()
	{
		lock (_stateLock)
		{
			int count = 0;
			if (_isActive)
			{
				for (int i = 0; i < 256; i++)
				{
					if (_activeMap[i].TargetVk != 0) count++;
				}
			}

			return new KeyboardRemapStatus
			{
				IsActive = _isActive,
				ActivePluginId = _activePluginId,
				TargetProcessName = _targetProcessName,
				MappingCount = count,
				ActivatedAtUtc = _activatedAtUtc
			};
		}
	}

	private void GetForegroundProcessInfo(out nint hwnd, out uint pid)
	{
		if (_hasTestForeground)
		{
			hwnd = _testForegroundHwnd;
			pid = _testForegroundPid;
			return;
		}

		hwnd = GetForegroundWindow();
		GetWindowThreadProcessId(hwnd, out pid);
	}

	/// <summary>请求激活进程级键盘映射会话。</summary>
	public KeyboardRemapResult Activate(string pluginId, KeyboardRemapOptions options)
	{
		if (string.IsNullOrWhiteSpace(pluginId))
		{
			return KeyboardRemapResult.Fail("调用方插件 ID 不能为空");
		}

		if (options == null)
		{
			return KeyboardRemapResult.Fail("键盘映射选项不能为空");
		}

		if (string.IsNullOrWhiteSpace(options.KeyMap))
		{
			return KeyboardRemapResult.Fail("键盘映射配置字符串不能为空");
		}

		if (!KeyMapCodec.TryDecode(options.KeyMap, out var entries, out string? decodeError) || entries == null)
		{
			return KeyboardRemapResult.Fail(decodeError ?? "键盘映射配置解析失败");
		}

		string? validationError = KeyMapValidator.Validate(entries);
		if (!string.IsNullOrEmpty(validationError))
		{
			return KeyboardRemapResult.Fail(validationError);
		}

		if (entries.Count == 0)
		{
			return KeyboardRemapResult.Fail("映射规则列表不能为空");
		}

		lock (_stateLock)
		{
			if (_isActive)
			{
				if (!string.Equals(_activePluginId, pluginId, StringComparison.OrdinalIgnoreCase))
				{
					return KeyboardRemapResult.Fail($"已有其他插件（{_activePluginId}）激活了键盘映射会话，全局同一时刻最多一个活动会话");
				}
			}

			// 严格前台进程绑定（Fail-closed：严禁搜索后台进程或回退）
			GetForegroundProcessInfo(out nint fgHwnd, out uint fgPid);
			if (fgPid == 0)
			{
				return KeyboardRemapResult.Fail("无法确定当前前台进程");
			}

			string currentFgName = "";
			try
			{
				using Process fgProc = Process.GetProcessById((int)fgPid);
				currentFgName = fgProc.ProcessName;
			}
			catch (Exception ex)
			{
				return KeyboardRemapResult.Fail($"无法获取前台进程（PID {fgPid}）信息: {ex.Message}");
			}

			string targetProcName = options.TargetProcessName?.Trim() ?? "";
			string normTarget = NormalizeProcessName(targetProcName);
			string normFg = NormalizeProcessName(currentFgName);
			if (!string.IsNullOrEmpty(normTarget))
			{
				if (!string.Equals(normFg, normTarget, StringComparison.OrdinalIgnoreCase))
				{
					return KeyboardRemapResult.Fail($"目标进程 \"{targetProcName}\" 与当前前台进程 \"{currentFgName}\"（PID: {fgPid}）不一致");
				}
			}
			else
			{
				targetProcName = currentFgName;
			}

			// 构建不可变查找表快照并验证扫描码解析
			RemapTarget[] newMap = new RemapTarget[256];
			foreach (var entry in entries)
			{
				if (KeyMapCodec.TryGetKeyVk(entry.FromKey, out uint fromVk) &&
				    KeyMapCodec.TryGetKeyVk(entry.ToKey, out uint toVk))
				{
					if (fromVk < 256 && toVk < 256)
					{
						ushort scan = (ushort)MapVirtualKey(toVk, 0);
						if (scan == 0)
						{
							return KeyboardRemapResult.Fail($"无法为目标按键 \"{entry.ToKey}\" 解析硬件扫描码");
						}

						uint flags = IsExtendedKey(toVk) ? KEYEVENTF_EXTENDEDKEY : 0;
						newMap[fromVk] = new RemapTarget
						{
							TargetVk = (ushort)toVk,
							TargetScan = scan,
							TargetFlags = flags
						};
					}
				}
			}

			// 清理旧按键状态
			ReleaseAllHeldTargetKeys();

			_sessionGeneration++;
			_activeMap = newMap;
			_activePluginId = pluginId;
			_targetProcessId = fgPid;
			_targetProcessName = targetProcName;
			_activatedAtUtc = DateTime.UtcNow;
			_escapeDownTicks = 0;
			_escapeEmergencyTimer?.Change(Timeout.Infinite, Timeout.Infinite);

			_cachedForegroundHwnd = fgHwnd;
			_cachedForegroundPid = fgPid;

			_isActive = true;

			AppLogger.LogInfo($"[KeyboardRemap] 插件 \"{pluginId}\" 激活了键盘映射会话，目标进程: {targetProcName} (PID: {fgPid})，映射项数: {entries.Count}");
			return KeyboardRemapResult.Ok("键盘重映射会话已激活");
		}
	}

	/// <summary>撤销由指定插件激活的键盘映射会话。</summary>
	public KeyboardRemapResult Deactivate(string pluginId)
	{
		lock (_stateLock)
		{
			if (!_isActive)
			{
				return KeyboardRemapResult.Ok("当前无活动会话");
			}

			if (!string.Equals(_activePluginId, pluginId, StringComparison.OrdinalIgnoreCase))
			{
				return KeyboardRemapResult.Fail($"当前会话由插件 \"{_activePluginId}\" 持有，无权撤销");
			}

			DeactivateInternal(RevokeReason.ExplicitDeactivate);
			return KeyboardRemapResult.Ok("键盘重映射会话已撤销");
		}
	}

	/// <summary>切换键盘映射会话状态。</summary>
	public KeyboardRemapResult Toggle(string pluginId, KeyboardRemapOptions options)
	{
		lock (_stateLock)
		{
			if (_isActive)
			{
				if (string.Equals(_activePluginId, pluginId, StringComparison.OrdinalIgnoreCase))
				{
					return Deactivate(pluginId);
				}
				return KeyboardRemapResult.Fail($"已有其他插件（{_activePluginId}）激活了键盘映射会话");
			}
			return Activate(pluginId, options);
		}
	}

	/// <summary>快捷键录制独占模式触发时通知控制器。</summary>
	public void NotifyRecorderActive()
	{
		lock (_stateLock)
		{
			if (_isActive)
			{
				DeactivateInternal(RevokeReason.RecorderPriority);
			}
		}
	}

	/// <summary>宿主全局暂停手势与重映射时撤销会话并释放所有按键。</summary>
	public void OnHostPaused()
	{
		lock (_stateLock)
		{
			if (_isActive)
			{
				DeactivateInternal(RevokeReason.HostPaused);
			}
		}
	}

	/// <summary>插件停用时通知控制器。</summary>
	public void OnPluginStopping(string pluginId)
	{
		lock (_stateLock)
		{
			if (_isActive && string.Equals(_activePluginId, pluginId, StringComparison.OrdinalIgnoreCase))
			{
				DeactivateInternal(RevokeReason.PluginStopping);
			}
		}
	}

	/// <summary>宿主退出时全面收尾释放。</summary>
	public void Shutdown()
	{
		lock (_stateLock)
		{
			if (_isActive)
			{
				DeactivateInternal(RevokeReason.HostShutdown);
			}
			_escapeEmergencyTimer?.Dispose();
			_escapeEmergencyTimer = null;
		}
	}

	private void DeactivateInternal(RevokeReason reason)
	{
		if (!_isActive) return;

		_isActive = false;
		_sessionGeneration++;
		string formerPlugin = _activePluginId;
		_activePluginId = "";
		_targetProcessId = 0;
		_targetProcessName = "";
		_activatedAtUtc = null;
		_activeMap = EmptyMap;

		ReleaseAllHeldTargetKeys();
		_escapeDownTicks = 0;
		_escapeEmergencyTimer?.Change(Timeout.Infinite, Timeout.Infinite);

		// 热路径事件（FocusLost、EmergencyEscape）绝不执行同步日志或 IO
		if (reason != RevokeReason.FocusLost && reason != RevokeReason.EmergencyEscape)
		{
			var (succ, fail, err) = GetInjectionDiagnostics();
			if (fail > 0)
			{
				AppLogger.LogWarn($"[KeyboardRemap] 插件 \"{formerPlugin}\" 会话结束，检测到注入失败: 成功={succ}, 失败={fail}, 最近Win32错误={err}");
			}
			AppLogger.LogInfo($"[KeyboardRemap] 插件 \"{formerPlugin}\" 的映射会话已撤销，原因: {reason}");
		}
	}

	private void ReleaseAllHeldTargetKeys()
	{
		for (int vk = 1; vk < 256; vk++)
		{
			if (_targetHoldCounts[vk] > 0)
			{
				_targetHoldCounts[vk] = 0;
				ushort scan = (ushort)MapVirtualKey((uint)vk, 0);
				uint flags = IsExtendedKey((uint)vk) ? KEYEVENTF_EXTENDEDKEY : 0;
				InjectKeyboardEvent((ushort)vk, scan, flags, isKeyUp: true);
			}
			_heldPhysicalSourceKeys[vk] = false;
			_passthroughPhysicalSourceKeys[vk] = false;
		}
	}

	/// <summary>
	/// 钩子回调热路径快速处理。
	/// 满足：常量时间查表、无堆分配、无 Dispatcher 投递、无 IO 与日志。
	/// </summary>
	public bool TryProcessHookEvent(uint vkCode, int message, uint flags, uint time)
	{
		if (!_isActive || vkCode >= 256) return false;

		bool isDown = (message == WM_KEYDOWN || message == WM_SYSKEYDOWN);
		bool isUp = (message == WM_KEYUP || message == WM_SYSKEYUP);
		if (!isDown && !isUp) return false;

		// 1. 焦点与前台进程校验（必须置于孤儿松开检查之前，确保失焦后的任意事件都能触发故障安全撤销）
		GetForegroundProcessInfo(out nint fgHwnd, out uint fgPid);
		if (fgHwnd != _cachedForegroundHwnd)
		{
			_cachedForegroundHwnd = fgHwnd;
			_cachedForegroundPid = fgPid;
		}

		// Fail-closed：若前台进程 PID 为 0（未知窗口）或发生进程切换，必须立刻撤销会话并原样放行触发事件
		if (fgPid == 0 || (_targetProcessId != 0 && fgPid != _targetProcessId))
		{
			lock (_stateLock)
			{
				if (_isActive && (_targetProcessId != 0 && (fgPid == 0 || fgPid != _targetProcessId)))
				{
					DeactivateInternal(RevokeReason.FocusLost);
				}
			}
			// 关键：新进程或未知前台收到的物理按键必须原样放行！
			return false;
		}

		// 2. 强制长按 Escape 紧急撤销检测（VK_ESCAPE = 27 = 0x1B，不可禁用）
		if (vkCode == 27)
		{
			if (isDown)
			{
				long now = Stopwatch.GetTimestamp();
				if (_escapeDownTicks == 0)
				{
					_escapeDownTicks = now;
					_escapeEmergencyTimer?.Change(1500, Timeout.Infinite);
				}
				else
				{
					double elapsedSec = (double)(now - _escapeDownTicks) / Stopwatch.Frequency;
					if (elapsedSec >= 1.5)
					{
						lock (_stateLock)
						{
							_escapeDownTicks = 0;
							_escapeEmergencyTimer?.Change(Timeout.Infinite, Timeout.Infinite);
							DeactivateInternal(RevokeReason.EmergencyEscape);
						}
						return false; // Escape 原键放行
					}
				}
			}
			else if (isUp)
			{
				_escapeDownTicks = 0;
				_escapeEmergencyTimer?.Change(Timeout.Infinite, Timeout.Infinite);
			}

			// Escape 默认行为始终放行给目标进程
			return false;
		}

		// 3. 直通按键松开与孤儿按键松开防护
		if (isUp)
		{
			if (_passthroughPhysicalSourceKeys[vkCode])
			{
				lock (_stateLock)
				{
					_passthroughPhysicalSourceKeys[vkCode] = false;
				}
				return false; // 首次注入失败的长按按键松开，原样放行并重置直通标记
			}

			if (!_heldPhysicalSourceKeys[vkCode])
			{
				return false; // 未曾作为映射键按下的孤儿按键松开，原样放行
			}
		}

		// 4. 首次注入失败的长按自动重复按键保持原键直通
		if (isDown && _passthroughPhysicalSourceKeys[vkCode])
		{
			return false;
		}

		// 5. 常量时间查表快速判断是否为被映射键
		RemapTarget target = _activeMap[vkCode];
		if (target.TargetVk == 0) return false;

		// 6. 命中映射规则：在状态锁保护下消除停用/注入竞争，并严格处理长按自动重复按键状态机
		lock (_stateLock)
		{
			if (!_isActive) return false;

			target = _activeMap[vkCode];
			if (target.TargetVk == 0) return false;

			if (isDown)
			{
				if (_passthroughPhysicalSourceKeys[vkCode])
				{
					// 首次注入失败的长按自动重复按键：同一次长按中严格保持原键直通，绝不中途切换成映射
					return false;
				}

				bool isFirstDown = !_heldPhysicalSourceKeys[vkCode];
				_heldPhysicalSourceKeys[vkCode] = true;

				if (isFirstDown)
				{
					// 源按键首次由未按下进入按下：目标引用计数累加，从 0 变 1 时注入目标 KeyDown
					if (_targetHoldCounts[target.TargetVk]++ == 0)
					{
						bool success = InjectKeyboardEvent(target.TargetVk, target.TargetScan, target.TargetFlags, isKeyUp: false);
						if (!success)
						{
							// 首次注入失败：回滚目标引用计数，清除映射标记，标记该物理键进入同次长按直通状态，放行物理源按键！
							_targetHoldCounts[target.TargetVk]--;
							_heldPhysicalSourceKeys[vkCode] = false;
							_passthroughPhysicalSourceKeys[vkCode] = true;
							return false;
						}
					}
				}
				else
				{
					// 源按键处于长按连续触发阶段（auto-repeat）：
					// 仅转发重复目标按键 KeyDown，绝不重复递增引用计数，杜绝卡键与按键不平衡缺陷！
					bool success = InjectKeyboardEvent(target.TargetVk, target.TargetScan, target.TargetFlags, isKeyUp: false);
					if (!success)
					{
						// 映射已开始后重复注入失败：绝不能放行不成对的原字母 KeyDown，必须吞没！
						return true;
					}
				}
				return true; // 注入成功，吞没物理源键，阻断默认行为
			}
			else // isUp
			{
				if (_passthroughPhysicalSourceKeys[vkCode])
				{
					_passthroughPhysicalSourceKeys[vkCode] = false;
					return false; // 直通按键松开，原样放行
				}

				if (!_heldPhysicalSourceKeys[vkCode])
				{
					return false; // 孤儿松开放行
				}

				_heldPhysicalSourceKeys[vkCode] = false;
				if (_targetHoldCounts[target.TargetVk] > 0)
				{
					// 仅当映射至此目标键的所有物理源键均已完全松开（引用计数归零）时，才注入目标 KeyUp
					if (--_targetHoldCounts[target.TargetVk] == 0)
					{
						bool success = InjectKeyboardEvent(target.TargetVk, target.TargetScan, target.TargetFlags, isKeyUp: true);
						if (!success)
						{
							// KeyUp 注入失败：绝不能清掉唯一的待释放记录！
							// 恢复引用计数为 1，确保该目标键依然保留在待释放记录中，
							// 留待会话停用/清理（ReleaseAllHeldTargetKeys）时继续补发释放，杜绝卡键！
							_targetHoldCounts[target.TargetVk] = 1;
						}
					}
				}
				return true; // 吞没物理源键松开（因为物理 KeyDown 已被映射吞没，物理 KeyUp 绝不能单独放行）
			}
		}
	}

	private bool InjectKeyboardEvent(ushort vk, ushort scan, uint flags, bool isKeyUp)
	{
		if (_testInjectionFailureMock != null)
		{
			bool allowed = _testInjectionFailureMock(scan, flags, isKeyUp);
			if (!allowed)
			{
				Interlocked.Increment(ref _injectionFailureCount);
				Volatile.Write(ref _lastInjectionError, 87); // ERROR_INVALID_PARAMETER
				return false;
			}
		}

		if (_testEventSink != null)
		{
			_testEventSink(scan, flags, isKeyUp);
			Interlocked.Increment(ref _injectionSuccessCount);
			return true;
		}

		lock (_inputLock)
		{
			_singleInputBuffer[0] = FormatKeyboardInput(scan, flags, isKeyUp);
			uint sent = SendInput(1, _singleInputBuffer, Marshal.SizeOf<INPUT>());
			if (sent != 1)
			{
				int err = Marshal.GetLastWin32Error();
				Interlocked.Increment(ref _injectionFailureCount);
				Volatile.Write(ref _lastInjectionError, err);
				return false;
			}

			Interlocked.Increment(ref _injectionSuccessCount);
			return true;
		}
	}

	private static bool IsExtendedKey(uint vkCode)
	{
		return vkCode == 33 || vkCode == 34 || vkCode == 35 || vkCode == 36 ||
		       vkCode == 37 || vkCode == 38 || vkCode == 39 || vkCode == 40 ||
		       vkCode == 44 || vkCode == 45 || vkCode == 46 ||
		       vkCode == 91 || vkCode == 92 || vkCode == 111 ||
		       (vkCode >= 166 && vkCode <= 179);
	}
}
