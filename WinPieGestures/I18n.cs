using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace WinPieGestures;

/// <summary>
/// 紧凑只读四语系内联值类型结构体：零托管堆分配，作为 Dictionary 的 Value 直接内联存储
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct LocalizedString(string ZhCn, string? ZhTw = null, string? En = null, string? Ja = null)
{
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public string Get(LanguageCode lang) => lang switch
	{
		LanguageCode.ZhTw => ZhTw ?? ZhCn,
		LanguageCode.En => En ?? ZhCn,
		LanguageCode.Ja => Ja ?? ZhCn,
		_ => ZhCn
	};
}

public static class I18n
{
	private static LanguageCode _currentLanguage = LanguageCode.ZhCn;

	private static readonly Dictionary<string, LocalizedString> Translations;

	public static LanguageCode CurrentLanguage
	{
		get => _currentLanguage;
		set
		{
		if (_currentLanguage != value)
		{
			_currentLanguage = value;
			LanguageChanged?.Invoke();
		}
		}
	}

	public static string CurrentLanguageCode => _currentLanguage switch
	{
		LanguageCode.ZhTw => "zh-TW",
		LanguageCode.En => "en",
		LanguageCode.Ja => "ja",
		_ => "zh-CN",
	};

	public static event Action? LanguageChanged;

public static void SetLanguage(string code)
	{
		if (string.Equals(code, "Auto", StringComparison.OrdinalIgnoreCase))
		{
			string name = CultureInfo.CurrentUICulture.Name;
			if (name.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase) || name.StartsWith("zh-HK", StringComparison.OrdinalIgnoreCase) || name.StartsWith("zh-MO", StringComparison.OrdinalIgnoreCase) || name.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase))
			{
				CurrentLanguage = LanguageCode.ZhTw;
			}
			else if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
			{
				CurrentLanguage = LanguageCode.ZhCn;
			}
			else if (name.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
			{
				CurrentLanguage = LanguageCode.Ja;
			}
			else
			{
				CurrentLanguage = LanguageCode.En;
			}
			return;
		}
		// 反编译残留清理：原文是 ILSpy 还原结构化控制流失败后吐出的「长度 + 字符试探 + 跳转表」
		// （31 处 `IL_xxxx` 标签跳转），这里改回它本来的形状 —— 对受支持的语言代码做精确匹配。
		// 注意 code 为 null（配置文件里 Language 缺失）时，原实现兜底落到的同样是 ZhCn，
		// 因此并入 `_` 分支，语义与原来逐条等价。
		CurrentLanguage = code switch
		{
			"zh-TW" or "zh-Hant" or "zh-HK" => LanguageCode.ZhTw,
			"en" or "en-US" or "en-GB" => LanguageCode.En,
			"ja" or "ja-JP" => LanguageCode.Ja,
			_ => LanguageCode.ZhCn
		};
	}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static string T(string key) => GetString(key);

	/// <summary>
	/// 查表并填充占位符（词条里写 <c>{0}</c> / <c>{1}</c>）。
	/// <para>
	/// 单独开这个方法、而不是让每个调用方各自写 <c>string.Format</c>：一是省掉重复代码，
	/// 二是键缺失时 <see cref="T"/> 会原样返回键名，此时 <c>string.Format</c> 作用在不含
	/// 占位符的字符串上是安全的（不会抛）—— 失败路径不会把界面搞崩。
	/// </para>
	/// </summary>
	public static string TF(string key, params object?[] args)
	{
		return string.Format(T(key), args);
	}

	/// <summary>
	/// 取某个键在<b>全部语言</b>下的值（去重后的非空集合，仅内置词条）。
	/// <para>
	/// 专供「与语言无关的判据」使用。典型用例是判断一个动作名是不是系统自动填的默认名：
	/// 只认当前语言的话，用户切一次语言之后，界面里那个<b>旧语言</b>的默认名就会被
	/// 当成「用户自己起的名字」，自动填充从此对那条动作失效 —— 而且换回语言也不恢复。
	/// </para>
	/// <para>
	/// 不含插件注册的外部词条：外部词条在停用插件时会被回收，拿它做判据会让
	/// 「这个名字算不算默认名」随插件启停而变。
	/// </para>
	/// </summary>
	internal static IEnumerable<string> AllTranslations(string key)
	{
		if (!Translations.TryGetValue(key, out LocalizedString value))
		{
			return Array.Empty<string>();
		}

		var result = new List<string>(4);
		void AddIfValid(string? str)
		{
			if (!string.IsNullOrWhiteSpace(str) && !result.Contains(str))
			{
				result.Add(str);
			}
		}
		AddIfValid(value.ZhCn);
		AddIfValid(value.ZhTw);
		AddIfValid(value.En);
		AddIfValid(value.Ja);
		return result;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static string GetString(string key)
	{
		if (Translations.TryGetValue(key, out var localized))
		{
			return localized.Get(_currentLanguage);
		}

		// 插件词条（外部注册）。
		// 走「写时复制」的独立字典而不是直接改 Translations：Translations 在静态构造后即视为只读，
		// 而它的读取遍布 UI 与渲染线程，直接插入会与其形成无锁并发读写。
		// 外部字典在零插件时恒为空，这里的开销只有一次 volatile 读 + Count 判断。
		Dictionary<string, Dictionary<LanguageCode, string>>? external = _externalTranslations;
		if (external.Count > 0 && external.TryGetValue(key, out Dictionary<LanguageCode, string>? extValue))
		{
			if (extValue.TryGetValue(_currentLanguage, out var extCurrent))
			{
				return extCurrent;
			}
			if (extValue.TryGetValue(LanguageCode.ZhCn, out var extFallback))
			{
				return extFallback;
			}
			// 有词条但没有当前语言也没有中文兜底：取第一个可用值，好过把 key 显示给用户
			foreach (string candidate in extValue.Values)
			{
				return candidate;
			}
		}

		return key;
	}

// ------------------------------------------------------------------ 插件外部词条

	/// <summary>
	/// 插件词条的写时复制快照。读取侧完全无锁；写入侧只在插件启用/停用时发生（低频）。
	/// </summary>
	private static volatile Dictionary<string, Dictionary<LanguageCode, string>> _externalTranslations
		= new Dictionary<string, Dictionary<LanguageCode, string>>(StringComparer.Ordinal);

	/// <summary>当前已注册的插件词条数量（诊断用）。</summary>
	public static int ExternalTranslationCount => _externalTranslations.Count;

	/// <summary>
	/// 注册一条插件词条。<paramref name="fullKey"/> 必须是完整 key（含 <c>plugin.&lt;id&gt;.</c> 前缀），
	/// 归一化由宿主在 <c>II18nRegistry</c> 实现里完成，此处不做二次加工。
	/// </summary>
	public static bool RegisterExternal(string fullKey, Dictionary<LanguageCode, string> values)
	{
		if (string.IsNullOrWhiteSpace(fullKey) || values == null || values.Count == 0)
		{
			return false;
		}

		var next = new Dictionary<string, Dictionary<LanguageCode, string>>(_externalTranslations, StringComparer.Ordinal)
		{
			[fullKey] = new Dictionary<LanguageCode, string>(values),
		};
		_externalTranslations = next;
		return true;
	}

	/// <summary>注销一条插件词条。</summary>
	public static bool UnregisterExternal(string fullKey)
	{
		if (string.IsNullOrWhiteSpace(fullKey) || !_externalTranslations.ContainsKey(fullKey))
		{
			return false;
		}

		var next = new Dictionary<string, Dictionary<LanguageCode, string>>(_externalTranslations, StringComparer.Ordinal);
		bool removed = next.Remove(fullKey);
		_externalTranslations = next;
		return removed;
	}

public static string FormatKeyName(string? keyStr, uint vkCode = 0)
	{
		if (string.IsNullOrWhiteSpace(keyStr) && vkCode == 0)
		{
			return string.Empty;
		}

		string normalized = keyStr?.Trim() ?? string.Empty;
		if (string.Equals(normalized, "None", StringComparison.OrdinalIgnoreCase))
		{
			if (vkCode == 0) return string.Empty;
		}

		LanguageCode lang = _currentLanguage;

		// 1. CapsLock / Capital (VkCode 20 / 0x14)
		if (vkCode == 20 || string.Equals(normalized, "Capital", StringComparison.OrdinalIgnoreCase) || string.Equals(normalized, "CapsLock", StringComparison.OrdinalIgnoreCase))
		{
			return lang switch
			{
				LanguageCode.ZhCn => "CapsLock (大写锁定)",
				LanguageCode.ZhTw => "CapsLock (大寫鎖定)",
				LanguageCode.Ja => "CapsLock (大文字ロック)",
				_ => "CapsLock"
			};
		}

		// 2. Space (VkCode 32 / 0x20)
		if (vkCode == 32 || string.Equals(normalized, "Space", StringComparison.OrdinalIgnoreCase))
		{
			return lang switch
			{
				LanguageCode.ZhCn => "Space (空格)",
				LanguageCode.ZhTw => "Space (空白鍵)",
				LanguageCode.Ja => "Space (スペース)",
				_ => "Space"
			};
		}

		// 3. Tab (VkCode 9 / 0x09)
		if (vkCode == 9 || string.Equals(normalized, "Tab", StringComparison.OrdinalIgnoreCase))
		{
			return lang switch
			{
				LanguageCode.ZhCn => "Tab (制表键)",
				LanguageCode.ZhTw => "Tab (製表鍵)",
				LanguageCode.Ja => "Tab",
				_ => "Tab"
			};
		}

		// 4. Wave / Tilde (VkCode 192 / 0xC0)
		if (vkCode == 192 || string.Equals(normalized, "Oem3", StringComparison.OrdinalIgnoreCase) || string.Equals(normalized, "OemTilde", StringComparison.OrdinalIgnoreCase) || normalized == "~" || normalized == "`")
		{
			return lang switch
			{
				LanguageCode.ZhCn => "~ (波浪键)",
				LanguageCode.ZhTw => "~ (波浪鍵)",
				LanguageCode.Ja => "~ (チルダ)",
				_ => "~"
			};
		}

		// 5. Enter / Return (VkCode 13 / 0x0D)
		if (vkCode == 13 || string.Equals(normalized, "Return", StringComparison.OrdinalIgnoreCase) || string.Equals(normalized, "Enter", StringComparison.OrdinalIgnoreCase))
		{
			return lang switch
			{
				LanguageCode.ZhCn => "Enter (回车)",
				LanguageCode.ZhTw => "Enter (回車)",
				LanguageCode.Ja => "Enter",
				_ => "Enter"
			};
		}

		// 6. Backspace / Back (VkCode 8 / 0x08)
		if (vkCode == 8 || string.Equals(normalized, "Back", StringComparison.OrdinalIgnoreCase) || string.Equals(normalized, "Backspace", StringComparison.OrdinalIgnoreCase))
		{
			return lang switch
			{
				LanguageCode.ZhCn => "Backspace (退格)",
				LanguageCode.ZhTw => "Backspace (退格)",
				LanguageCode.Ja => "Backspace",
				_ => "Backspace"
			};
		}

		// 7. Escape / Esc (VkCode 27 / 0x1B)
		if (vkCode == 27 || string.Equals(normalized, "Escape", StringComparison.OrdinalIgnoreCase) || string.Equals(normalized, "Esc", StringComparison.OrdinalIgnoreCase))
		{
			return lang switch
			{
				LanguageCode.ZhCn => "Esc (退出)",
				LanguageCode.ZhTw => "Esc (退出)",
				LanguageCode.Ja => "Esc",
				_ => "Esc"
			};
		}

		// 8. Shifts
		if (vkCode == 160 || string.Equals(normalized, "LeftShift", StringComparison.OrdinalIgnoreCase))
		{
			return lang switch
			{
				LanguageCode.En => "Left Shift",
				_ => "左 Shift"
			};
		}
		if (vkCode == 161 || string.Equals(normalized, "RightShift", StringComparison.OrdinalIgnoreCase))
		{
			return lang switch
			{
				LanguageCode.En => "Right Shift",
				_ => "右 Shift"
			};
		}

		// 9. Ctrls
		if (vkCode == 162 || string.Equals(normalized, "LeftCtrl", StringComparison.OrdinalIgnoreCase))
		{
			return lang switch
			{
				LanguageCode.En => "Left Ctrl",
				_ => "左 Ctrl"
			};
		}
		if (vkCode == 163 || string.Equals(normalized, "RightCtrl", StringComparison.OrdinalIgnoreCase))
		{
			return lang switch
			{
				LanguageCode.En => "Right Ctrl",
				_ => "右 Ctrl"
			};
		}

		// 10. Alts
		if (vkCode == 164 || string.Equals(normalized, "LeftAlt", StringComparison.OrdinalIgnoreCase))
		{
			return lang switch
			{
				LanguageCode.En => "Left Alt",
				_ => "左 Alt"
			};
		}
		if (vkCode == 165 || string.Equals(normalized, "RightAlt", StringComparison.OrdinalIgnoreCase))
		{
			return lang switch
			{
				LanguageCode.En => "Right Alt",
				_ => "右 Alt"
			};
		}

		// 11. Wins
		if (vkCode == 91 || vkCode == 92 || string.Equals(normalized, "LWin", StringComparison.OrdinalIgnoreCase) || string.Equals(normalized, "RWin", StringComparison.OrdinalIgnoreCase))
		{
			return "Win";
		}

		// 12. Arrows
		if (vkCode == 38 || string.Equals(normalized, "Up", StringComparison.OrdinalIgnoreCase))
		{
			return lang switch
			{
				LanguageCode.ZhCn => "方向键 上",
				LanguageCode.ZhTw => "方向鍵 上",
				LanguageCode.Ja => "上矢印",
				_ => "Up"
			};
		}
		if (vkCode == 40 || string.Equals(normalized, "Down", StringComparison.OrdinalIgnoreCase))
		{
			return lang switch
			{
				LanguageCode.ZhCn => "方向键 下",
				LanguageCode.ZhTw => "方向鍵 下",
				LanguageCode.Ja => "下矢印",
				_ => "Down"
			};
		}
		if (vkCode == 37 || string.Equals(normalized, "Left", StringComparison.OrdinalIgnoreCase))
		{
			return lang switch
			{
				LanguageCode.ZhCn => "方向键 左",
				LanguageCode.ZhTw => "方向鍵 左",
				LanguageCode.Ja => "左矢印",
				_ => "Left"
			};
		}
		if (vkCode == 39 || string.Equals(normalized, "Right", StringComparison.OrdinalIgnoreCase))
		{
			return lang switch
			{
				LanguageCode.ZhCn => "方向键 右",
				LanguageCode.ZhTw => "方向鍵 右",
				LanguageCode.Ja => "右矢印",
				_ => "Right"
			};
		}

		// 13. Delete & Insert
		if (vkCode == 46 || string.Equals(normalized, "Delete", StringComparison.OrdinalIgnoreCase))
		{
			return lang switch
			{
				LanguageCode.ZhCn => "Delete (删除)",
				LanguageCode.ZhTw => "Delete (刪除)",
				LanguageCode.Ja => "Delete (削除)",
				_ => "Delete"
			};
		}
		if (vkCode == 45 || string.Equals(normalized, "Insert", StringComparison.OrdinalIgnoreCase))
		{
			return lang switch
			{
				LanguageCode.ZhCn => "Insert (插入)",
				LanguageCode.ZhTw => "Insert (插入)",
				LanguageCode.Ja => "Insert (挿入)",
				_ => "Insert"
			};
		}

		// 14. PageUp & PageDown
		if (vkCode == 33 || string.Equals(normalized, "PageUp", StringComparison.OrdinalIgnoreCase))
		{
			return lang switch
			{
				LanguageCode.ZhCn => "PageUp (上一页)",
				LanguageCode.ZhTw => "PageUp (上一頁)",
				_ => "PageUp"
			};
		}
		if (vkCode == 34 || string.Equals(normalized, "PageDown", StringComparison.OrdinalIgnoreCase))
		{
			return lang switch
			{
				LanguageCode.ZhCn => "PageDown (下一页)",
				LanguageCode.ZhTw => "PageDown (下一頁)",
				_ => "PageDown"
			};
		}

		// 15. PrintScreen / Snapshot
		if (vkCode == 44 || string.Equals(normalized, "PrintScreen", StringComparison.OrdinalIgnoreCase) || string.Equals(normalized, "Snapshot", StringComparison.OrdinalIgnoreCase))
		{
			return lang switch
			{
				LanguageCode.ZhCn => "PrintScreen (截屏)",
				LanguageCode.ZhTw => "PrintScreen (截圖)",
				_ => "PrintScreen"
			};
		}

		// 16. Digits D0..D9
		if (normalized.Length == 2 && normalized[0] == 'D' && char.IsDigit(normalized[1]))
		{
			return normalized[1].ToString();
		}

		// 17. NumPad keys
		if (normalized.StartsWith("NumPad", StringComparison.OrdinalIgnoreCase) && normalized.Length == 7 && char.IsDigit(normalized[6]))
		{
			return "Num " + normalized[6];
		}
		if (string.Equals(normalized, "Multiply", StringComparison.OrdinalIgnoreCase)) return "Num *";
		if (string.Equals(normalized, "Divide", StringComparison.OrdinalIgnoreCase)) return "Num /";
		if (string.Equals(normalized, "Add", StringComparison.OrdinalIgnoreCase)) return "Num +";
		if (string.Equals(normalized, "Subtract", StringComparison.OrdinalIgnoreCase)) return "Num -";
		if (string.Equals(normalized, "Decimal", StringComparison.OrdinalIgnoreCase)) return "Num .";

		// 18. OEM punctuations
		if (string.Equals(normalized, "OemMinus", StringComparison.OrdinalIgnoreCase)) return "-";
		if (string.Equals(normalized, "OemPlus", StringComparison.OrdinalIgnoreCase)) return "=";
		if (string.Equals(normalized, "OemOpenBrackets", StringComparison.OrdinalIgnoreCase)) return "[";
		if (string.Equals(normalized, "OemCloseBrackets", StringComparison.OrdinalIgnoreCase)) return "]";
		if (string.Equals(normalized, "OemPipe", StringComparison.OrdinalIgnoreCase)) return "\\";
		if (string.Equals(normalized, "OemSemicolon", StringComparison.OrdinalIgnoreCase)) return ";";
		if (string.Equals(normalized, "OemQuotes", StringComparison.OrdinalIgnoreCase)) return "'";
		if (string.Equals(normalized, "OemComma", StringComparison.OrdinalIgnoreCase)) return ",";
		if (string.Equals(normalized, "OemPeriod", StringComparison.OrdinalIgnoreCase)) return ".";
		if (string.Equals(normalized, "OemQuestion", StringComparison.OrdinalIgnoreCase)) return "/";

		return !string.IsNullOrEmpty(normalized) ? normalized : (vkCode > 0 ? $"0x{vkCode:X2}" : string.Empty);
	}

		static I18n()
	{
		var dictionary = new Dictionary<string, LocalizedString>(1400, StringComparer.Ordinal);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		void Add(string key, string zhCn, string? zhTw = null, string? en = null, string? ja = null)
		{
			dictionary[key] = new LocalizedString(zhCn, zhTw, en, ja);
		}

		Add("SidebarModeSimple", "💡 简单模式", "💡 簡易模式", "💡 Simple Mode", "💡 シンプルモード");
		Add("SidebarModePro", "⚙️ 高级全星模式", "⚙️ 進階全星模式", "⚙️ Pro Full Mode", "⚙️ プロ全星モード");
		Add("NewCustomPresetButton", "➕ 新建配色", "➕ 新建配色", "➕ New Theme", "➕ 新規配色");
		Add("NewCustomPresetTitle", "新建配色方案", "新建配色方案", "New Color Theme Preset", "新しいカラーテーマ");
		Add("NewCustomPresetPrompt", "请输入新配色方案名称：", "請輸入新配色方案名稱：", "Enter a name for the new color theme:", "新しいカラーテーマ名を入力してください:");
		Add("SavePresetChangesButton", "\ud83d\udcbe 保存当前配色修改", "\ud83d\udcbe 儲存當前配色修改", "\ud83d\udcbe Save Color Changes", "\ud83d\udcbe 現在の配色変更を保存");
		Add("SaveAsNewPresetButton", "➕ 另存为新预设...", "➕ 另存為新預設...", "➕ Save as New Preset...", "➕ 新規プリセットとして保存...");
		Add("DeletePresetButton", "\ud83d\uddd1\ufe0f 删除预设", "\ud83d\uddd1\ufe0f 刪除預設", "\ud83d\uddd1\ufe0f Delete Preset", "\ud83d\uddd1\ufe0f プリセットを削除");
		Add("RenameCustomPresetButton", "✏\ufe0f 重命名预设", "✏\ufe0f 重新命名預設", "✏\ufe0f Rename Preset", "✏\ufe0f プリセット名を変更");
		Add("RenameCustomPresetTitle", "重命名配色方案预设", "重新命名配色方案預設", "Rename Color Preset", "カラープリセット名を変更");
		Add("RenameCustomPresetPrompt", "请输入配色方案预设的新名称：", "請輸入配色方案預設的新名稱：", "Enter a new name for the color preset:", "カラープリセットの新しい名前を入力してください:");
		Add("CustomColorsExpanderDesc", "展开后可精准微调扇区底色、高亮光晕、边框线条、文字与光弧等各项色彩。", "展開後可精準微調扇區底色、高亮光暈、邊框線條、文字與光弧等各項色彩。", "Expand to fine-tune sector background, highlight glow, border outlines, text and arc colors.", "展開してセクター背景、ハイライトグロー、ボーダー、テキスト色などを微調整できます。");
		Add("WheelFontFamily", "轮盘文字字体:", "輪盤文字字體:", "Wheel Font Family:", "ホイールのフォント:");
		Add("SubmenuStyleTitle", "二级菜单样式", "二級選單樣式", "Submenu Style", "サブメニューのスタイル");
		Add("SubmenuStyleDesc", "外圈子环：子动作沿选中扇区外侧环形展开（最多4个）；蜂窝扇：子动作以选中项为中心呈扇形排列（最多3个）。", "外圈子環：子動作沿選中扇區外側環形展開（最多4個）；蜂窩扇：子動作以選中項為中心呈扇形排列（最多3個）。", "Sub-Ring: Sub-actions expand in an outer concentric ring (up to 4 items); Honeycomb Fan: Expands outward from the selected sector in a tight fan (up to 3 items).", "外周リング：選択したセクターの外側にリング状に展開（最大4項目）；ハニカムファン：扇状にコンパクトに展開（最大3項目）。");
		Add("SubmenuStyleWheel", "🌐 外圈子环", "🌐 外圈子環", "🌐 Outer Sub-Ring", "🌐 外周同心リング");
		Add("SubmenuStyleFan", "🍯 蜂窝扇", "🍯 蜂窩扇", "🍯 Honeycomb Fan", "🍯 ハニカムファン");
		Add("OuterEscapeTitle", "外甩脱离取消", "外甩脫離取消", "Outer Escape Cancel", "外側スワイプでキャンセル");
		Add("OuterEscapeDesc", "手势划出后若想放弃，无需拉回中心，直接顺势向外快速划出即可安全取消，0 误触。", "手勢劃出後若想放棄，無需拉回中心，直接順勢向外快速劃出即可安全取消，0 誤觸。", "Flick cursor outwards past the wheel radius to safely cancel without returning to center.", "ホイールの外側へ素早くスワイプすることで、安全に操作をキャンセルできます。");
		Add("OuterEscapeDistanceTitle", "外甩取消距离:", "外甩取消距離:", "Escape Distance:", "キャンセルスワイプ距離:");
		Add("OuterEscapeDistanceDesc", "设定光标划出距离中心多远时判定为放弃。数值越小越灵敏（更易甩出取消），数值越大越沉稳（需甩得更远）。", "設定游標劃出距離中心多遠時判定為放棄。數值越小越靈敏（更易甩出取消），數值越大越沉穩（需甩得更遠）。", "How far past the center the cursor must travel to cancel. Smaller values cancel easier, larger values require a farther flick.", "中心からどれだけ離れたらキャンセルとするかを設定します。値が小さいほど敏感になり、大きいほど遠くへのスワイプが必要になります。");
		Add("OuterEscapeCheckbox", "启用向外顺势甩出取消手势 (推荐开启)", "啟用向外順勢甩出取消手勢 (推薦開啟)", "Enable Outer Escape Cancel (Recommended)", "外側スワイプキャンセルを有効化 (推奨)");
		Add("VolumeDragTitle", "音量拖距调音", "音量拖距調音", "Volume Drag-Adjust", "音量ドラッグ調整");
		Add("VolumeDragDesc", "长按音量加/减扇区向外拖出可连续调音，以下距离参数实时生效，无需重启。", "長按音量加/減扇區向外拖出可連續調音，以下距離參數即時生效，無需重啟。", "Drag outward from a volume +/- sector to adjust continuously; the distance options below apply live without restart.", "音量+/-セクターから外側へドラッグして連続調整。以下の距離パラメータは再起動不要で即時反映されます。");
		Add("VolumeCancelRatioTitle", "缩回取消滞后:", "縮回取消滯後:", "Return Cancel Ratio:", "中心復帰キャンセル係数:");
		Add("VolumeCancelRatioDesc", "调音时缩回到触发距离的百分之多少以下即取消并恢复原音量。值越大越不易被边缘抖动误回收（默认 60%）。", "調音時縮回到觸發距離的百分之多少以下即取消並恢復原音量。值越大越不易被邊緣抖動誤回收（預設 60%）。", "Cancel and restore the original volume once you drag back below this share of the trigger distance. Higher resists edge jitter (default 60%).", "トリガー距離のこの割合まで戻すとキャンセルして元の音量に戻します。値が大きいほど端の揺れによる誤収斂ににくくなります（既定60%）。");
		Add("VolumeFlickFarTitle", "外甩取消距离下限:", "外甩取消距離下限:", "Flick Cancel Distance Floor:", "スワイプキャンセル距離下限:");
		Add("VolumeFlickFarDesc", "仅当光标已远于该距离时，快速甩动才会取消调音，防止近距离拖动被误判为甩出。", "僅當光標已遠於該距離時，快速甩動才會取消調音，防止近距離拖動被誤判為甩出。", "A fast flick only cancels once the pointer is beyond this distance, avoiding short drags being read as a flick.", "カーソルがこの距離を超えて初めて高速スワイプでキャンセル。近距離ドラッグの誤判定を防ぎます。");
		Add("VolumeFlickJumpTitle", "外甩速度跳变阈值:", "外甩速度跳變閾值:", "Flick Jump Threshold:", "スワイプ跳変閾値:");
		Add("VolumeFlickJumpDesc", "一帧之内光标移动超过该距离才算快速甩动。值越大越不容易触发甩出取消。", "一幀之內光標移動超過該距離才算快速甩動。值越大越不容易觸發甩出取消。", "Movement beyond this distance within one frame counts as a fast flick. Larger values make flick-cancel less likely.", "1フレーム内の移動がこの距離を超えると高速スワイプと判定。値が大きいほどスワイプキャンセルは起きにくくなります。");
		Add("IconPickerImport", "➕ 导入自定义图标...", "➕ 匯入自訂圖示...", "➕ Import Custom Icon...", "➕ カスタムアイコンをインポート...");
		Add("CustomColorsExpanderTitle", "\ud83c\udfa8 自定义高级配色与色彩微调", "\ud83c\udfa8 自訂進階配色與色彩微調", "\ud83c\udfa8 Custom Advanced Color Tuning", "\ud83c\udfa8 高度なカラーカスタマイズ");
		Add("AnimSpeedTitle", "功能区高亮与过渡动效速度", "功能區高亮與過渡動效速度", "Hover & Transition Animation Speed", "ホバー・遷移アニメーション速度");
		Add("AnimSpeedDesc", "调节鼠标划向不同功能扇区时的高亮弹出与平滑过渡动画响应速度，定制专属跟手体验。", "調節滑鼠滑向不同功能扇區時的高亮彈出與平滑過渡動畫響應速度，定制專屬手感。", "Adjust the response animation speed when hovering and transitioning across sectors.", "セクター間をホバー・移動する際のアニメーション速度を調整します。");
		Add("AnimSpeedElegant", "\ud83c\udf38 优雅 (130ms / 柔和细腻)", "\ud83c\udf38 優雅 (130ms / 柔和細膩)", "\ud83c\udf38 Elegant (130ms / Smooth & Soft)", "\ud83c\udf38 エレガント (130ms / 滑らか)");
		Add("AnimSpeedBalanced", "⚡ 流畅 (80ms / 推荐默认)", "⚡ 流暢 (80ms / 推薦預設)", "⚡ Fluent (80ms / Recommended)", "⚡ スムーズ (80ms / 推奨)");
		Add("AnimSpeedFast", "\ud83d\ude80 快速 (35ms / 极速响应)", "\ud83d\ude80 快速 (35ms / 極速響應)", "\ud83d\ude80 Snappy (35ms / Ultra-Fast)", "\ud83d\ude80 高速 (35ms / 即座に応答)");
		Add("CoreTransformSectionTitle", "图案尺寸与位置", "圖案尺寸與位置", "Core Pattern & Image Size and Position Tuning", "コアパターン・画像のサイズと位置の微調整");
		Add("CoreIconScaleTitle", "图案大小缩放:", "圖案大小縮放:", "Core Pattern / Image Scale:", "中央パターン／画像のスケーリング:");
		Add("CoreImageOffsetXTitle", "水平偏移:", "水平偏移:", "Horizontal Offset (X):", "水平表示位置オフセット (X):");
		Add("CoreImageOffsetYTitle", "垂直偏移:", "垂直偏移:", "Vertical Offset (Y):", "垂直表示位置オフセット (Y):");
		Add("BtnResetCoreTransform", "🔄 重置尺寸与居中", "🔄 重設尺寸與置中", "🔄 Reset Size & Center Position", "🔄 サイズと中央位置をリセット");
		Add("CoreImagePerformanceTip", "提示：推荐使用 256×256 ~ 512×512 适中分辨率的图片或 SVG 矢量图。导入超高分辨率（如 4K/8K 原图）会增加 GPU 内存占用与重采样计算开销，可能影响手势呼出与高刷响应性能。", "提示：建議使用 256×256 ~ 512×512 適中解析度的圖片或 SVG 向量圖。匯入超高解析度（如 4K/8K 原圖）會增加 GPU 記憶體佔用與重採樣計算開銷，可能影響手勢呼出與高刷響應效能。", "Tip: Recommended image size is 256×256 ~ 512×512 px or SVG vectors. Importing ultra-high resolution images (e.g. 4K/8K) increases GPU memory and texture sampling overhead, which may impact gesture responsiveness.", "ヒント: 256×256～512×512 px の画像または SVG ベクター画像の使用を推奨します。超高解像度画像（4K/8K など）を使用すると、GPU メモリ使用量と再サンプリング負荷が増加し、応答性に影響を与える場合があります。");
		Add("EnableMultiTier", "启用多级轮盘与级联子菜单", "啟用多級輪盤與級聯子選單 (Multi-Tier Sub-Wheels)", "Enable Multi-Tier Cascading Sub-Wheels", "マルチ階層サブホイール機能を有効化");
		Add("EnableMultiTierDesc", "开启后，若扇区配置了二级子动作，光标悬停时外圈将平滑展开扇形级联子菜单，向外划动即可精准触发子功能。", "開啟後，若扇區配置了二級子動作，游標懸停時外圈將平滑展開扇形級聯子選單，向外劃動即可精準觸發子功能。", "When enabled, hovering over a sector with sub-actions will smoothly expand cascading outer sub-sectors. Flick outward to trigger.", "有効にすると、サブアクションが設定されたセクターにホバーした際に外側にカスケードサブメニューが展開され、外側へスワイプしてトリガーできます。");
		Add("AutoExpandSubRingsTitle", "唤出时直接同时展开一二级轮盘", "喚出時直接同時展開一二級輪盤 (Auto-Expand Sub-Rings)", "Expand Sub-Rings Simultaneously on Popup", "ポップアップ時にサブリングを同時展開");
		Add("AutoExpandSubRingsDesc", "开启后，呼出轮盘时所有已配置二级动作的外圈子环将与一级扇区同时呈现，无需向外划出触发距离即可一览全部操作。", "開啟後，呼出輪盤時所有已配置二級動作的外圈子環將與一級扇區同時呈現，無需向外劃出觸發距離即可一覽全部操作。", "When enabled, outer sub-rings for all configured sectors expand simultaneously upon popup without needing to drag past trigger distance.", "有効にすると、ポップアップ時にトリガー距離をスワイプしなくても、設定済みのすべてのサブリングがメインセクターと同時に展開されます。");
		Add("IsolationModeTitle", "进程隔离与生效模式", "處理程序隔離與生效模式", "Process Isolation & Activation Mode", "プロセス分離と有効化モード");
		Add("IsolationBlacklistRadio", "\ud83d\udeab 排除黑名单模式 (默认：全局生效，仅在黑名单程序中放行右键)", "\ud83d\udeab 排除黑名單模式 (預設：全域生效，僅在黑名單程式中放行右鍵)", "\ud83d\udeab Blacklist Mode (Global active, bypass in blacklisted apps)", "\ud83d\udeab ブラックリストモード (既定: 全体有効、除外アプリのみ右クリック通過)");
		Add("IsolationWhitelistRadio", "\ud83d\udee1\ufe0f 启用白名单模式 (仅在白名单程序中生效，其余程序完全放行右键)", "\ud83d\udee1\ufe0f 啟用白名單模式 (僅在白名單程式中生效，其餘程式完全放行右鍵)", "\ud83d\udee1\ufe0f Whitelist Mode (Only active in whitelisted apps, bypass elsewhere)", "\ud83d\udee1\ufe0f ホワイトリストモード (登録アプリのみ有効、他は右クリック通過)");
		Add("BlacklistTitle", "进程排除黑名单", "處理程序排除黑名單", "Process Exclusion Blacklist", "プロセス除外ブラックリスト");
		Add("BlacklistDesc", "在排除黑名单中的应用程序（如远程桌面、画图、3D建模软件）中，完全放行鼠标右键。", "在排除黑名單中的應用程式（如遠端桌面、小畫家、3D建模軟體）中，完全放行滑鼠右鍵。", "Bypass mouse gestures in blacklisted applications (e.g. Remote Desktop, Paint, CAD tools).", "ブラックリストに登録されたアプリ（リモートデスクトップ、ペイントなど）ではジェスチャーを無効化します。");
		Add("WhitelistTitle", "进程启用白名单", "處理程序啟用白名單", "Process Activation Whitelist", "プロセス有効化ホワイトリスト");
		Add("WhitelistDesc", "手势轮盘仅在白名单列表中的应用程序中生效，其他所有程序完全放行鼠标右键。", "手勢輪盤僅在白名單列表中的應用程式中生效，其他所有程式完全放行滑鼠右鍵。", "Mouse gestures will ONLY activate in whitelisted applications, bypassing everywhere else.", "ホワイトリストに登録されたアプリのみでジェスチャーが有効になり、他のアプリでは通過します。");
		Add("SubActionColumnHeader", "级联子菜单", "級聯子選單", "Sub-Menu", "サブメニュー");
		Add("CustomColorsExpander", "展开后可精准微调扇区底色、高亮光晕、边框线条、文字与光弧等各项色彩。", "展開後可精準微調扇區底色、高亮光暈、邊框線條、文字與光弧等各項色彩。", "Expand to fine-tune individual colors for sectors, highlights, borders, text, and glow.", "セクター、ハイライト、ボーダー、テキストなどの色を個別に調整します。");
		Add("MilestonesOlderExpander", "📜 展开查看更早的历史版本演进", "📜 展開查看更早的歷史版本演進", "📜 View Older Milestones", "📜 過去の更新履歴を表示");
		Add("BrowseAppTooltip", "选择应用程序或快捷方式...", "選擇應用程式或捷徑...", "Browse application or shortcut...", "アプリまたはショートカットを参照...");
		Add("BrowseFolderTooltip", "选择本地文件夹...", "選擇本機資料夾...", "Browse local folder...", "フォルダーを参照...");
		Add("BtnConfirm", "确定", "確定", "Confirm", "確定");
		Add("BtnCancel", "取消", "取消", "Cancel", "キャンセル");
		Add("BtnOk", "确定", "確定", "OK", "OK");
		Add("BtnApply", "应用", "套用", "Apply", "適用");
		Add("BtnTest", "测试", "測試", "Test", "テスト");
		Add("BtnBrowseFolder", "选择文件夹...", "選擇資料夾...", "Browse Folder...", "フォルダーを選択...");
		Add("ActionTypeFolder", "\ud83d\udcc2 打开文件夹", "\ud83d\udcc2 開啟資料夾", "\ud83d\udcc2 Open Folder", "\ud83d\udcc2 フォルダーを開く");
		Add("ActionTypeHotkeyShort", "快捷热键", "快捷熱鍵", "Hotkey", "ショートカット");
		Add("ActionTypeOcrShort", "截屏识字 (OCR)", "截圖識字 (OCR)", "Screen OCR", "画面OCR");
		Add("ActionTypeLaunchShort", "启动程序", "啟動程式", "Run App", "アプリ起動");
		Add("ActionTypeFolderShort", "打开文件夹", "開啟資料夾", "Open Folder", "フォルダー");
		Add("ActionTypeWebUrlShort", "打开网址", "開啟網址", "Open URL", "URLを開く");
		Add("ActionTypeSystemShort", "系统控制", "系統控制", "System", "システム");
		Add("ActionTypeShellToolShort", "系统与右键工具", "系統與右鍵工具", "Shell & System Tools", "シェル・右クリックツール");
		Add("ActionTypeCommandShort", "运行命令", "執行命令", "Run Command", "コマンド実行");
		Add("ActionTypeSwitchWindowShort", "切换窗口", "切換視窗", "Switch Window", "ウィンドウ切替");
		Add("TerminalCmd", "CMD", "CMD", "CMD", "CMD");
		Add("TerminalPowerShell", "Powershell", "Powershell", "Powershell", "Powershell");
		Add("TerminalWsl", "WSL", "WSL", "WSL", "WSL");
		Add("TerminalCmdHidden", "CMD (无终端)", "CMD (無終端)", "CMD (no window)", "CMD (非表示)");
		Add("TerminalPowerShellHidden", "Powershell (无终端)", "Powershell (無終端)", "Powershell (no window)", "Powershell (非表示)");
		Add("TerminalWslHidden", "WSL (无终端)", "WSL (無終端)", "WSL (no window)", "WSL (非表示)");
		Add("ProfileCardTitle", "当前配置方案", "當前配置方案", "Active Profiles", "プロファイル設定");
		Add("ProfileCardDesc", "选择或新建针对特定程序（如 Chrome、VS Code）或特定工作流的轮盘配置方案（支持双击重命名）。", "選擇或新建針對特定程式（如 Chrome、VS Code）或特定工作流程的輪盤配置方案（支援按兩下重新命名）。", "Select or create dedicated pie wheel profiles for specific apps (e.g. Chrome, VS Code) or workflows (double-click to rename).", "アプリ（Chrome、VS Codeなど）やワークフローごとに専用のプロファイルを設定します（ダブルクリックで名前変更）。");
		Add("BtnAddAppProfile", "➕ 新增程序专属配置", "➕ 新增程式專屬配置", "➕ Add App Profile", "➕ アプリ専用設定を追加");
		Add("BtnAddCustomProfile", "➕ 新建自定义配置", "➕ 新建自訂配置", "➕ Add Custom Profile", "➕ カスタム設定を追加");
		Add("BtnRenameProfile", "✏\ufe0f 重命名当前配置", "✏\ufe0f 重新命名當前配置", "✏\ufe0f Rename Profile", "✏\ufe0f 名前を変更");
		Add("BtnDeleteProfile", "\ud83d\uddd1\ufe0f 删除当前配置", "\ud83d\uddd1\ufe0f 刪除當前配置", "\ud83d\uddd1\ufe0f Delete Profile", "\ud83d\uddd1\ufe0f 設定を削除");
		Add("BtnDuplicateProfile", "📑 复制方案", "📑 複製方案", "📑 Duplicate Profile", "📑 設定を複製");
		Add("SectorCountOptionTitle", "扇区按键数", "扇區按鍵數", "Sector Count", "セクター数（キー数）");
		Add("SectorCountOptionDesc", "切换手势轮盘的切分数量。4 键最快最不易误触，8 键为标准全能方位，12 键适合功能密集场景。", "切換手勢輪盤的切分數量。4 鍵最快最不易誤觸，8 鍵為標準全能方位，12 鍵適合功能密集場景。", "Switch sector counts: 4-way for fast blind flicks, 8-way for balanced productivity, 12-way for high-density actions.", "セクター数を切り替えます。4キー（誤操作防止）、8キー（標準全方位）、12キー（高密度機能）。");
		Add("SectorActionListTitle", "扇区动作映射列表", "扇區動作對應列表", "Sector Action Mappings", "セクターアクションマッピング");
		Add("SectorActionListDesc", "为每个方位指定触发动作与图标。支持热键组合（如 Ctrl+C）、启动本地程序、打开文件夹与系统级操作。", "為每個方位指定觸發動作與圖示。支援快捷熱鍵組合（如 Ctrl+C）、啟動本地程式、開啟資料夾與系統級操作。", "Assign actions and icons for each sector. Supports hotkeys (e.g. Ctrl+C), app launching, folder opening, and system actions.", "各方向の動作とアイコンを設定します。ショートカット（Ctrl+C等）、アプリ起動、フォルダー、システム制御に対応。");
		Add("SectorActionListReorderHint", "点击右侧功能卡的 ▲ / ▼ 箭头，将功能移动到相邻的轮盘位置槽。", "點擊右側功能卡的 ▲ / ▼ 箭頭，將功能移動到相鄰的輪盤位置槽。", "Click the ▲ / ▼ arrows on the right to move an action to an adjacent wheel-position slot.", "右側の▲ / ▼ボタンをクリックして、アクションを隣のホイール位置へ移動します。");
		Add("SectorPositionSlot", "轮盘位置槽", "輪盤位置槽", "Wheel position", "ホイール位置");
		Add("SectorMoveUp", "将此功能上移一个轮盘位置", "將此功能上移一個輪盤位置", "Move this action up one wheel position", "このアクションを1つ上のホイール位置へ移動");
		Add("SectorMoveDown", "将此功能下移一个轮盘位置", "將此功能下移一個輪盤位置", "Move this action down one wheel position", "このアクションを1つ下のホイール位置へ移動");
		Add("IconPickerTitle", "选择动作矢量图标", "選擇動作向量圖示", "Select Vector Icon", "ベクターアイコンを選択");
		Add("IconPickerHeader", "选择扇区动作矢量图标", "選擇扇區動作向量圖示", "Select Sector Vector Icon", "セクターアイコンを選択");
		Add("IconPickerSubtitle", "精选 30+ 常用高保真矢量图形，支持在不同分辨率及 DPI 下无损清晰渲染。", "精選 30+ 常用高保真向量圖形，支援在不同解析度及 DPI 下無損清晰渲染。", "30+ high-fidelity vector icons with lossless crisp rendering across all DPI displays.", "30種類以上の高精細ベクターアイコン。あらゆるDPIで美しく描画されます。");
		Add("IconPickerSearchTooltip", "输入图标名称或分类进行快速过滤...", "輸入圖示名稱或分類進行快速篩選...", "Search icon name or category...", "アイコン名またはカテゴリで検索...");
		Add("IconPickerClear", "清空图标 (无图标)", "清空圖示 (無圖示)", "Clear Icon (No Icon)", "アイコンをクリア (なし)");
		Add("IconPickerSelected", "已选图标:", "已選圖示:", "Selected Icon:", "選択中のアイコン:");
		Add("IconPickerNone", "(未选择)", "(未選擇)", "(None)", "(未選択)");
		Add("ColorPickerTitle", "颜色选择器", "色彩選擇器", "Color Picker & Eyedropper", "カラーピッカー＆スポイト");
		Add("ColorPickerHue", "色相", "色相", "Hue", "色相");
		Add("ColorPickerAlpha", "不透明", "不透明", "Opacity", "不透明度");
		Add("ColorPickerEyedropperTitle", "\ud83d\udd0d 屏幕取色吸管", "\ud83d\udd0d 螢幕取色吸管", "\ud83d\udd0d Screen Eyedropper", "\ud83d\udd0d 画面スポイト");
		Add("ColorPickerEyedropperDesc", "点击后在屏幕任意窗口吸取精准色彩", "點擊後在螢幕任意視窗吸取精準色彩", "Pick color accurately from any window or desktop on screen", "画面上の任意のウィンドウから正確な色を抽出します");
		Add("ColorPickerEyedropperBtn", "从屏幕吸色", "從螢幕吸色", "Pick Color", "画面から吸色");
		Add("ColorPickerSwatches", "预设经典配色卡 (Quick Swatches - 滚轮滚动查看全部):", "預設經典配色卡 (Quick Swatches - 滾輪滾動查看全部):", "Preset Color Swatches (Scroll to browse):", "プリセットカラーパレット (スクロールで全表示):");
		Add("ColorPickerApply", "应用色彩", "套用色彩", "Apply Color", "色を適用");
		Add("InputDialogTitle", "配置方案 - StarPie", "配置方案 - StarPie", "Profile - StarPie", "プロファイル - StarPie");
		Add("InputDialogEmpty", "名称不能为空，请输入有效的配置名称。", "名稱不能為空，請輸入有效的配置名稱。", "Name cannot be empty. Please enter a valid profile name.", "名前を入力してください。");
		Add("Notice", "提示", "提示", "Notice", "お知らせ");
		Add("Error", "错误", "錯誤", "Error", "エラー");
		Add("AppName", "StarPie", "StarPie", "StarPie", "StarPie");
		Add("AppSubtitle", "现代鼠标轮盘笔势系统", "現代滑鼠輪盤手勢系統", "Modern Mouse Radial Gestures", "次世代マウスラジアルジェスチャー");
		Add("WindowTitle", "StarPie 设置控制台", "StarPie 設定控制台", "StarPie Preferences Console", "StarPie 環境設定コンソール");
		Add("TabTrigger", "触发设置", "觸發設定", "Triggers", "トリガー設定");
		Add("TabAppearance", "外观样式", "外觀樣式", "Appearance", "外観スタイル");
		Add("TabGestures", "手势动作", "手勢動作", "Gestures & Actions", "ジェスチャー");
		Add("TabAdvanced", "系统设置", "系統設定", "System Settings", "システム設定");
		Add("TabAbout", "关于软件", "關於軟體", "About StarPie", "バージョン情報");
		Add("SidebarCollapse", "折叠侧边栏", "摺疊側邊欄", "Collapse sidebar", "サイドバーを折りたたむ");
		Add("SidebarExpand", "展开侧边栏", "展開側邊欄", "Expand sidebar", "サイドバーを展開");
		Add("BottomStatusNote", "注: 所有修改均在内存中即时生效，点击【保存更改】持久化保存至硬盘。", "註: 所有修改均在記憶體中即時生效，點擊【儲存變更】持久化儲存至硬碟。", "Note: All changes take effect in memory immediately. Click [Save Changes] to persist to disk.", "注: 変更はメモリ上で即座に有効になります。[変更を保存] で設定ファイルに永続化されます。");
		Add("BtnSave", "保存更改", "儲存變更", "Save Changes", "変更を保存");
		Add("BtnClose", "关闭并隐藏", "關閉並隱藏", "Close & Hide", "閉じて隠す");
		Add("TriggerHeader", "触发与场景隔离设置", "觸發與場景隔離設定", "Trigger & Scene Isolation", "トリガーとシーンの分離設定");
		Add("TriggerSubheader", "在此配置全局鼠标手势的触发灵敏度、全屏游戏自动拦截与排除程序黑名单。", "在此配置全域滑鼠手勢的觸發靈敏度、全螢幕遊戲自動攔截與排除程式黑名單。", "Configure mouse gesture sensitivity, full-screen gaming bypass, and exclusion blacklist.", "マウスジェスチャーの感度、フルスクリーンゲームでの自動回避、除外プロセスを設定します。");
		Add("TriggerRecorderTitle", "轮盘唤醒触发按键 & 组合键录制", "輪盤喚醒觸發按鍵 & 組合鍵錄製", "Radial Menu Trigger & Combo Key Recorder", "ラジアルメニュー起動トリガー＆コンボキー録画");
		Add("TriggerRecorderDesc", "支持鼠标所有按键（右键/中键/侧键1/侧键2/左键）与键盘单键（如 CapsLock、波浪键~、空格、字母键、F区键等）及组合键一键物理录制绑定。绑定鼠标左键后长按可唤醒轮盘，单机左键保持系统正常点击。未触发手势的轻点将自动放行原生按键点击。建议避免将常用功能按键绑定为触发按键。", "支持滑鼠所有按鍵（右鍵/中鍵/側鍵1/側鍵2/左鍵）與鍵盤單鍵（如 CapsLock、波浪鍵~、空格、字母鍵、F區鍵等）及組合鍵一鍵物理錄製綁定。綁定滑鼠左鍵後長按可喚醒輪盤，單擊左鍵保持系統正常點擊。未觸發手勢的輕點將自動放行原生按鍵點擊。建議避免將常用功能按鍵綁定為觸發按鍵。", "Supports one-click physical recording for all mouse buttons (Right, Middle, Side 1/2, Left) and keyboard keys as well as combos. When Left Button is bound, long-press opens radial menu, while quick click maintains normal click function. It is recommended to avoid binding frequently used functional keys as trigger keys.", "すべてのマウスボタン（右、中央、サイド1/2、左）およびキーボード単キーやコンボの物理録画に対応。左ボタン設定時は長押しでホイール起動、短押しクリックは通常の操作を維持します。常用する機能キーを起動トリガーに割り当てることは避けることを推奨します。");
		Add("TriggerLeftButtonRecordedTip", "🟢 已成功绑定【鼠标左键】：长按左键唤醒轮盘，单机左键保持系统正常点击！", "🟢 已成功綁定【滑鼠左鍵】：長按左鍵喚醒輪盤，單擊左鍵保持系統正常點擊！", "🟢 [Left Mouse Button] bound: Long-press to open radial menu, quick click maintains normal click function!", "🟢 【マウス左ボタン】設定完了：長押しでホイール起動、短押しクリックは通常の操作を維持します！");
		Add("LongPressTriggerTitle", "长按触发按键呼出面板 (可选)", "長按觸發按鍵呼叫面板 (可選)", "Long-press trigger to open the menu (optional)", "長押しでメニューを開く (任意)");
		Add("LongPressTriggerDesc", "按住触发键不动超过设定时长即呼出轮盘；未达时长松手仍为普通按键。与按住拖动呼出共存。", "按住觸發鍵不動超過設定時長即呼叫輪盤；未達時長鬆手仍為普通按鍵。與按住拖動呼叫共存。", "Hold the trigger key still for the configured duration to open the wheel. Releasing early still behaves as a normal key press. Coexists with the drag-to-open gesture.", "トリガーキーを一定時間押し続けるとホイールが開きます。時間前に離すと通常のキー操作になります。ドラッグで開く動作と共存できます。");
		Add("GestureTitle", "鼠标手势", "滑鼠手勢", "Mouse Gestures", "マウスジェスチャー");
		Add("GestureDesc", "启用后，按住手势触发键画出轨迹（如 ↓、→、L 形），释放即执行对应动作；支持单段、双段与三段图样。轻点仍透传原生点击。", "啟用後，按住手勢觸發鍵畫出軌跡（如 ↓、→、L 形），釋放即執行對應動作；支援單段、雙段與三段圖樣。輕點仍透傳原生點擊。", "Hold the gesture trigger button and draw a trail (e.g. ↓, →, L-shape); releasing executes the mapped action. Supports single, double and triple-segment patterns. Quick clicks still pass through.", "トリガーキーを押しながら軌跡を描くと（↓、→、L字など）、離した時点で割り当てたアクションを実行します。1〜3セグメントのジェスチャーに対応。短押しは通常通り通過します。");
		Add("GestureEnableText", "启用鼠标手势模式", "啟用滑鼠手勢模式", "Enable mouse gesture mode", "マウスジェスチャーモードを有効化");
		Add("GestureEnableDescText", "手势触发键将不再弹出轮盘，仅用于绘制手势；其它触发键照常弹轮盘。", "手勢觸發鍵將不再彈出輪盤，僅用於繪製手勢；其它觸發鍵照常彈輪盤。", "The gesture trigger button no longer opens the wheel; it is used for drawing gestures only. Other triggers keep opening the wheel.", "ジェスチャートリガーキーはホイールを開かず、ジェスチャー描画専用になります。他のトリガーは従来通りホイールを開きます。");
		Add("GestureTriggerLabelText", "手势触发键：", "手勢觸發鍵：", "Gesture trigger button:", "ジェスチャートリガーキー：");
		Add("GestureHintPlaceText", "松手提示位置：", "鬆手提示位置：", "Release-hint position:", "離した時のヒント位置：");
		Add("GestureSensitivityTitle", "手势段灵敏度：", "手勢段靈敏度：", "Gesture segment sensitivity:", "ジェスチャー感度：");
		Add("MouseReleaseDebounceTitle", "启用鼠标右键释放防抖", "啟用滑鼠右鍵釋放防抖", "Enable right-button release debounce", "右ボタンのリリースデバウンスを有効化");
		Add("MouseReleaseDebounceDesc", "收到右键松开后短暂等待；期间若再次按下，则视为微动抖动并保持当前轮盘，不执行动作。", "收到右鍵放開後短暫等待；期間若再次按下，則視為微動抖動並保持目前輪盤，不執行動作。", "Briefly waits after right-button release. If another press arrives during the window, it is treated as switch chatter and the current wheel stays active without executing.", "右ボタンを離した後に短時間待機し、その間に再度押された場合はチャタリングとして扱い、アクションを実行せず現在のホイールを維持します。");
		Add("MouseReleaseDebounceValueDesc", "建议 8～15 ms，默认 12 ms。数值越大越能过滤微动抖动，但松手后的动作延迟也会相应增加。", "建議 8～15 ms，預設 12 ms。數值越大越能過濾微動抖動，但放開後的動作延遲也會相應增加。", "Recommended: 8–15 ms; default: 12 ms. Higher values filter more switch chatter but add the same release latency.", "推奨値は 8～15 ms、既定値は 12 ms です。値を大きくするとチャタリング除去は強くなりますが、リリース後の遅延も増加します。");
		Add("CancelActionTitleText", "外甩取消时执行的动作", "外甩取消時執行的動作", "Action on Outer-Escape Cancel", "外側スワイプキャンセル時の動作");
		Add("CancelActionDescText", "仅当通过「外甩取消」（向外甩出且未选中任何动作）时执行这个自定义动作；回到中心取消按钮松手仍为默认静默关闭。", "僅當透過「外甩取消」（向外甩出且未選中任何動作）時執行這個自訂動作；回到中心取消按鈕鬆手仍為預設靜默關閉。", "Only when you cancel by flinging outward (nothing selected) does this custom action run; releasing over the center cancel button still just closes the menu silently.", "外側へフリックしてキャンセルした場合（何も選択していない）のみカスタムアクションを実行します。中央のキャンセルで離すと従来通りサイレントクローズします。");
		Add("CancelActionEnableText", "启用自定义外甩取消动作", "啟用自訂外甩取消動作", "Enable custom outer-escape cancel action", "外側スワイプキャンセル時のカスタム動作を有効化");
		Add("ActionTypeTileShort", "平铺窗口", "平鋪視窗", "Tile Windows", "ウィンドウを並べる");
		Add("TileLayout2L", "左右对半", "左右對半", "Left-Right", "左右分割");
		Add("TileLayout2T", "上下对半", "上下對半", "Top-Bottom", "上下分割");
		Add("TileLayout3L12", "左大列 + 右上/右下", "左大列 + 右上/右下", "Big Left + Right Two", "左大＋右二");
		Add("TileLayout3R21", "右大列 + 左上/左下", "右大列 + 左上/左下", "Big Right + Left Two", "右大＋左二");
		Add("TileLayout3R", "三等分竖列", "三等分豎列", "Three Columns", "3等分列");
		Add("TileLayout4G", "四宫格 2×2", "四宮格 2×2", "2×2 Grid", "2×2 グリッド");
		Add("TileLayout6G", "六宫格 3×2", "六宮格 3×2", "3×2 Grid", "3×2 グリッド");
		Add("ActionTypeWindowManagerShort", "窗口管理", "視窗管理", "Window Management", "ウィンドウ管理");
		Add("ActionTypeTileRestoreShort", "恢复上次平铺", "還原上次平鋪", "Restore Tiles", "前の配置に戻す");
		Add("ActionTypeMoveMonitorShort", "窗口移到下一屏", "視窗移到下一螢幕", "Move to Next Monitor", "次のモニターへ移動");
		Add("ActionTypeTopmostShort", "窗口置顶/取消置顶", "視窗置頂/取消置頂", "Toggle Always-on-Top", "最前面表示の切替");
		Add("ActionTypeOpacityShort", "窗口透明度", "視窗透明度", "Window Opacity", "ウィンドウの透明度");
		Add("TileCycleLabel", "循环切换布局", "循環切換佈局", "Cycle layouts", "レイアウトを順番に切替");
		Add("TileGlobalTitleText", "平铺窗口设置", "平鋪視窗設定", "Tiling Settings", "タイリング設定");
		Add("TileGlobalDescText", "「平铺窗口」动作的全局行为（屏幕边距、窗口间距、包含最小化窗口、排除名单与多布局循环切换）。", "「平鋪視窗」動作的全域行為（螢幕邊距、視窗間距、包含最小化視窗、排除名單與多佈局循環切換）。", "Global behaviour of the Tile Windows action (screen margins, window gaps, minimized windows, exclusion list and layout cycling).", "「ウィンドウを並べる」アクションの全体挙動（画面マージン、ウィンドウ間隔、最小化ウィンドウの包含、除外リスト、レイアウト切替）。");
		Add("TileMarginText", "屏幕边距（上 / 下 / 左 / 右，物理像素，0 = 贴边）：", "螢幕邊距（上 / 下 / 左 / 右，物理像素，0 = 貼邊）：", "Screen margins (Top / Bottom / Left / Right, physical pixels, 0 = flush):", "画面マージン（上 / 下 / 左 / 右、物理ピクセル、0 = 端に寄せる）：");
		Add("TileGapText", "窗口间距（物理像素，0 = 紧贴）：", "視窗間距（物理像素，0 = 緊貼）：", "Gap between windows (physical pixels, 0 = flush):", "ウィンドウ間隔（物理ピクセル、0 = 隙間なし）：");
		Add("TileMinimizeText", "包含最小化窗口（还原后参与平铺）", "包含最小化視窗（還原後參與平鋪）", "Include minimized windows (restore into the layout)", "最小化ウィンドウも含める（復元して配置）");
		Add("TileExcludeText", "平铺排除名单（进程 exe 名，逗号分隔，如 notepad,spotify）：", "平鋪排除名單（程序 exe 名，逗號分隔，如 notepad,spotify）：", "Tiling exclusion list (process exe names, comma-separated, e.g. notepad,spotify):", "並べる対象外のプロセス（exe名、カンマ区切り例 notepad,spotify）：");
		Add("TileRestoreAllLabel", "还原所有窗口（回到平铺前）", "還原所有視窗（回到平鋪前）", "Restore all windows (pre-tile state)", "すべてのウィンドウを元に戻す（並べる前の状態）");
		Add("TileCycleBackLabel", "循环返回", "循環返回", "Cycle previous", "前のレイアウトへ");
		Add("TileLayoutML", "主窗居左 + 右栈", "主窗居左 + 右棧", "Master Left + Stack", "マスター左＋スタック");
		Add("TileLayoutMR", "主窗居右 + 左栈", "主窗居右 + 左棧", "Master Right + Stack", "マスター右＋スタック");
		Add("TileLayoutMT", "主窗居上 + 下栈", "主窗居上 + 下棧", "Master Top + Stack", "マスター上＋スタック");
		Add("TileLayoutMB", "主窗居下 + 上栈", "主窗居下 + 上棧", "Master Bottom + Stack", "マスター下＋スタック");
		Add("TileLayoutMO", "单窗全屏 (Monocle)", "單窗全螢幕 (Monocle)", "Monocle", "モノクル（全画面）");
		Add("TileLayoutHS", "上主 50% + 下栈 (H-Stack)", "上主 50% + 下棧 (H-Stack)", "Master Top + Stack (H-Stack)", "上マスター＋下スタック");
		Add("TileLayoutVS", "左主 50% + 右栈 (V-Stack)", "左主 50% + 右棧 (V-Stack)", "Master Left + Stack (V-Stack)", "左マスター＋右スタック");
		Add("TileLayoutCOL", "等宽竖列 (Columns)", "等寬豎列 (Columns)", "Columns", "等幅カラム");
		Add("TileLayoutBSP", "二叉分割 (BSP)", "二叉分割 (BSP)", "Binary Split (BSP)", "二分木分割 (BSP)");
		Add("TileLayoutAG", "自适应网格 (A-Grid)", "自適應網格 (A-Grid)", "Auto Grid", "自動グリッド");
		Add("TileCycleRangeText", "循环切换范围（布局 key 逗号分隔，空=全部，如 2L,2T,4G,ML）：", "循環切換範圍（佈局 key 逗號分隔，空=全部，如 2L,2T,4G,ML）：", "Cycle range (layout keys, comma-separated; empty = all, e.g. 2L,2T,4G,ML):", "循環切替範囲（レイアウトキーをカンマ区切り。空=全て。例 2L,2T,4G,ML）：");
		Add("GestureMappingTitleText", "手势图样映射", "手勢圖樣映射", "Gesture Pattern Mappings", "ジェスチャーパターン割り当て");
		Add("BtnRecordTrigger", "\ud83d\udd34 点击录制触发键 / 组合键", "\ud83d\udd34 點擊錄製觸發鍵 / 組合鍵", "\ud83d\udd34 Record Trigger / Combo Key", "\ud83d\udd34 トリガーキー/コンボを録画");
		Add("BtnResetDefaultTrigger", "\ud83d\udd04 恢复默认 (鼠标右键)", "\ud83d\udd04 恢復默認 (滑鼠右鍵)", "\ud83d\udd04 Reset Default (Right Mouse)", "\ud83d\udd04 デフォルトに戻す (マウス右ボタン)");
		Add("CurrentBindingLabel", "当前生效触发键：", "當前生效觸發鍵：", "Active Trigger Binding:", "現在の有効トリガー：");
		Add("TriggerButtonTitle", "轮盘唤醒触发按键", "輪盤喚醒觸發按鍵", "Radial Menu Trigger Button", "ラジアルメニュー起動トリガーボタン");
		Add("TriggerButtonDesc", "选择按住并拖动唤醒轮盘手势的鼠标按键。未触发手势的轻点将自动放行原生按键点击。", "選擇按住並拖動喚醒輪盤手勢的滑鼠按鍵。未觸發手勢的輕點將自動放行原生按鍵點擊。", "Select which mouse button to hold and drag to summon the radial menu. Quick clicks without dragging will naturally pass through native click events.", "長押しドラッグでラジアルメニューを起動するマウスボタンを選択します。短押しクリックは通常のクリックとして処理されます。");
		Add("TriggerBtnRight", "🖱️ 鼠标右键 [推荐 / 默认]", "🖱️ 滑鼠右鍵 [推薦 / 默認]", "🖱️ Right Mouse Button [Default / Recommended]", "🖱️ マウス右ボタン [推奨 / デフォルト]");
		Add("TriggerBtnMiddle", "🖱️ 鼠标中键 / 滚轮按压", "🖱️ 滑鼠中鍵 / 滾輪按壓", "🖱️ Middle Mouse Button / Wheel Click", "🖱️ マウス中央ボタン / ホイールクリック");
		Add("TriggerBtnX1", "🖱️ 鼠标侧键 1 / 后退键", "🖱️ 滑鼠側鍵 1 / 後退鍵", "🖱️ Mouse Side Button 1 / Back", "🖱️ マウスサイドボタン 1 / 戻る");
		Add("TriggerBtnX2", "🖱️ 鼠标侧键 2 / 前进键", "🖱️ 滑鼠側鍵 2 / 前進鍵", "🖱️ Mouse Side Button 2 / Forward", "🖱️ マウスサイドボタン 2 / 進む");
		Add("TriggerBtnLeftOnly", "🖱️ 鼠标左键", "🖱️ 滑鼠左鍵", "🖱️ Left Mouse Button", "🖱️ マウス左ボタン");
		Add("TriggerHoldOrDrag", "(长按 / 拖动)", "(長按 / 拖動)", "(Hold / Drag)", "(長押し / ドラッグ)");
		Add("BtnRecordTriggerListening", "⚡ 正在监听... 请按下任意按键 / 组合键 (ESC取消)", "⚡ 正在監聽... 請按下任意按鍵 / 組合鍵 (ESC取消)", "⚡ Listening... Press any key / combo (ESC to cancel)", "⚡ リスニング中... 任意のキー/コンボを押してください (ESCでキャンセル)");
		Add("BtnRecordProcessTriggerListening", "⚡ 正在监听... 请按专属键 (ESC取消)", "⚡ 正在監聽... 請按專屬鍵 (ESC取消)", "⚡ Listening... Press dedicated key (ESC to cancel)", "⚡ リスニング中... 専用キーを押してください (ESCでキャンセル)");
		Add("LiveSensorRecordingModeTip", "🔴 录制模式中：请直接按下你想作为轮盘唤醒键的鼠标按键、键盘按键或组合键（按 ESC 键取消录制）...", "🔴 錄製模式中：請直接按下你想作為輪盤喚醒鍵的滑鼠按鍵、鍵盤按鍵或組合鍵（按 ESC 鍵取消錄製）...", "🔴 Recording mode: Press any mouse button, key, or combo to set as radial trigger (ESC to cancel)...", "🔴 録画モード中: ラジアルメニュー起動キーとして設定するマウスボタン、キー、またはコンボを押してください（ESCでキャンセル）...");
		Add("LiveSensorSavedTip", "🟢 触发按键录制成功并已保存！", "🟢 觸發按鍵錄製成功並已儲存！", "🟢 Trigger binding saved successfully!", "🟢 トリガーキーが正常に保存されました！");
		Add("LiveSensorResetDefaultFmt", "🟢 已恢复默认触发按键：{0}", "🟢 已恢復預設觸發按鍵：{0}", "🟢 Reset to default trigger: {0}", "🟢 デフォルトのトリガーに戻しました：{0}");
		Add("LiveSensorMouseFmt", "🟢 实时捕获输入: {0} | 状态: 硬件信号正常响应", "🟢 即時捕獲輸入: {0} | 狀態: 硬體信號正常響應", "🟢 Captured Mouse Input: {0} | Status: Hardware signal responding", "🟢 マウス入力を検出: {0} | 状態: 正常に応答中");
		Add("LiveSensorKeyboardFmt", "🟢 实时捕获键盘输入: {0} | 虚拟键码 VkCode: 0x{1:X2}", "🟢 即時捕獲鍵盤輸入: {0} | 虛擬鍵碼 VkCode: 0x{1:X2}", "🟢 Captured Keyboard Input: {0} | VkCode: 0x{1:X2}", "🟢 キーボード入力を検出: {0} | 仮想キーコード VkCode: 0x{1:X2}");
		Add("ProcessSensorListeningFmt", "正在监听 [{0}] 专属呼出键：请直接按下你想作为该程序呼出键的鼠标按键（如中键/侧键）或键盘按键（按 ESC 取消）...", "正在監聽 [{0}] 專屬呼出鍵：請直接按下你想作為該程式呼出鍵的滑鼠按鍵（如中鍵/側鍵）或鍵盤按鍵（按 ESC 取消）...", "Listening for [{0}] dedicated trigger: Press any mouse button (Middle/Side) or key to set (ESC to cancel)...", "[{0}] 専用起動キーをリスニング中: 設定したいマウスボタン（中央/サイド等）またはキーを押してください（ESCでキャンセル）...");
		Add("ProcessSensorSavedFmt", "[{0}] 专属触发键录制成功并已保存！", "[{0}] 專屬觸發鍵錄製成功並已儲存！", "[{0}] Dedicated trigger saved successfully!", "[{0}] 専用トリガーが正常に保存されました！");
		Add("ProcessSensorResetFmt", "已恢复 [{0}] 默认设置：完全放行鼠标右键", "已恢復 [{0}] 預設設定：完全放行滑鼠右鍵", "Reset [{0}] to default: Fully pass-through right mouse button", "[{0}] の設定をデフォルトに戻しました: 右クリックを完全に通過");
		Add("ProcessSensorMouseFmt", "实时捕获输入: {0} | 状态: 硬件信号正常响应", "即時捕獲輸入: {0} | 狀態: 硬體信號正常響應", "Captured Mouse Input: {0} | Status: Hardware signal responding", "マウス入力を検出: {0} | 状態: 正常に応答中");
		Add("ProcessSensorKeyboardFmt", "实时捕获键盘输入: {0} | 虚拟键码 VkCode: 0x{1:X2}", "即時捕獲鍵盤輸入: {0} | 虛擬鍵碼 VkCode: 0x{1:X2}", "Captured Keyboard Input: {0} | VkCode: 0x{1:X2}", "キーボード入力を検出: {0} | 仮想キーコード VkCode: 0x{1:X2}");
		Add("SensitivityTitle", "手势触发灵敏度", "手勢觸發靈敏度", "Trigger Sensitivity", "ジェスチャー起動感度");
		Add("SensitivityDesc", "按住鼠标右键移动超过指定像素距离后呼出手势轮盘。距离越小越灵敏，过小可能造成右键微抖动误触。", "按住滑鼠右鍵移動超過指定像素距離後呼出手勢輪盤。距離越小越靈敏，過小可能造成右鍵微抖動誤觸。", "Hold right-click and move beyond this pixel distance to trigger radial menu. Lower values are more sensitive.", "右クリックを押しながら指定ピクセル以上移動するとホイールを呼び出します。値が小さいほど高感度です。");
		Add("SceneIsolationTitle", "场景隔离与防误触", "場景隔離與防誤觸", "Scene Isolation & Guard", "シーン分離と誤操作防止");
		Add("SceneIsolationDesc", "当处于特定场景或配合修饰键操作时，自动绕过轮盘拦截，放行原生右键事件。", "當處於特定場景或配合修飾鍵操作時，自動繞過輪盤攔截，放行原生右鍵事件。", "Automatically bypass radial menu and pass-through native right-click in specific scenarios.", "特定の環境や修飾キー操作時にホイールを無効化し、通常の右クリックを通過させます。");
		Add("FullScreenOption", "全屏游戏/独占应用自动禁用手势", "全螢幕遊戲/獨佔應用自動禁用手勢", "Auto-disable in Full-screen games / Exclusive apps", "全画面ゲーム/専用アプリでジェスチャーを自動無効化");
		Add("FullScreenOptionDesc", "自动检测当前前台窗口是否处于全屏独占状态，避免游戏瞄准等右键操作被拦截。", "自動檢測當前前台視窗是否處於全螢幕獨佔狀態，避免遊戲瞄準等右鍵操作被攔截。", "Detects whether active window is running in full-screen to avoid intercepting gaming right-clicks.", "アクティブなウィンドウが全画面かどうかを検知し、ゲームの照準等の右クリック操作を邪魔しません。");
		Add("ModifierPassTitle", "快捷键旁路穿透 (按住以下修饰键拖拽时不触发手势):", "快速鍵旁路穿透 (按住以下修飾鍵拖曳時不觸發手勢):", "Modifier Pass-Through (hold to bypass gestures):", "修飾キーバイパス (押下中はジェスチャーを無効化):");
		Add("ModifierCtrl", "按住 Ctrl 键时旁路", "按住 Ctrl 鍵時旁路", "Bypass on Ctrl", "Ctrl 押下時にバイパス");
		Add("ModifierShift", "按住 Shift 键时旁路", "按住 Shift 鍵時旁路", "Bypass on Shift", "Shift 押下時にバイパス");
		Add("ModifierAlt", "按住 Alt 键时旁路", "按住 Alt 鍵時旁路", "Bypass on Alt", "Alt 押下時にバイパス");
		Add("BlacklistTitle", "进程排除黑名单", "行程排除黑名單", "Process Exclusion Blacklist", "除外プロセスブラックリスト");
		Add("BlacklistDesc", "在排除名单中的应用程序（如远程桌面、画图、3D建模软件）中，完全放行鼠标右键。", "在排除名單中的應用程式（如遠端桌面、小畫家、3D建模軟體）中，完全放行滑鼠右鍵。", "Native right-click is fully allowed within blacklisted applications (e.g. Remote Desktop, Paint, CAD).", "登録されたアプリ（リモートデスクトップ、ペイント、3Dモデリング等）では右クリックを直接通します。");
		Add("BtnAddProcess", "➕ 添加进程", "➕ 新增處理程序", "➕ Add Process", "➕ プロセス追加");
		Add("BtnPickProcess", "\ud83d\udd0d 选择应用...", "\ud83d\udd0d 選擇應用程式...", "\ud83d\udd0d Select App...", "\ud83d\udd0d アプリを選択...");
		Add("BtnDeleteProcess", "\ud83d\uddd1\ufe0f 移除选中", "\ud83d\uddd1\ufe0f 移除選取", "\ud83d\uddd1\ufe0f Remove Selected", "\ud83d\uddd1\ufe0f 選択項目を削除");
		Add("BlacklistPlaceholder", "输入进程名称 (如 solidworks.exe) 或点击右侧选择应用...", "輸入處理程序名稱 (如 solidworks.exe) 或點擊右側選擇應用程式...", "Enter process name (e.g. solidworks.exe) or browse...", "プロセス名を入力 (例: solidworks.exe) またはアプリを選択...");
		Add("AppearanceHeader", "轮盘外观与形态定制", "輪盤外觀與形態自訂", "Appearance & Shapes Customization", "外観と形状のカスタマイズ");
		Add("AppearanceSubheader", "自由配置轮盘视觉风格、配色方案、高亮边缘光晕、几何切削、图标排版与中心核圆贴图。", "自由配置輪盤視覺風格、配色方案、高亮邊緣光暈、幾何切削、圖示排版與中心核圓貼圖。", "Customize visual styles, color palettes, highlight glow, geometry shapes, typography, and core image.", "ビジュアルスタイル、配色テーマ、グロー発光、幾何学形状、アイコン配置、コアバッジをカスタマイズします。");
		Add("StyleTitle", "轮盘视觉风格", "輪盤視覺風格", "Visual Renderer Style", "ビジュアルレンダラー");
		Add("StyleGlass", "液态毛玻璃", "液態毛玻璃", "Liquid Glassmorphism", "リキッドグラスモーフィズム");
		Add("StyleClassic", "经典圆环", "經典圓環", "Classic Ring", "クラシックリング");
		Add("StyleClean", "悬浮扇区", "懸浮扇區", "Clean Sectors", "クリーンセクター");
		Add("StyleCatPaw", "萌宠猫爪", "萌寵貓爪", "Cute Cat Paw", "キュートキャットポー (肉球)");
		Add("ThemeTitle", "轮盘配色方案", "輪盤配色方案", "Wheel Color Palette", "ホイール配色パレット");
		Add("BtnDeletePreset", "🗑️ 删除预设", "🗑️ 刪除預設", "🗑️ Delete Preset", "🗑️ プリセット削除");
		Add("GlowTitle", "高亮边缘光晕", "高亮邊緣光暈", "Highlight Edge Glow", "ハイライトエッジグロー発光");
		Add("GlowFollowTheme", "跟随主题高亮色", "跟隨主題高亮色", "Follow Theme", "テーマ連動");
		Add("GlowRadius", "光晕半径", "光暈半徑", "Glow Radius", "グロー拡散半径");
		Add("GlowOpacity", "光晕不透明度", "光暈不透明度", "Glow Opacity", "グロー不透明度");
		Add("GeometryTitle", "形态与尺寸", "形態與尺寸", "Geometry & Dimensions", "幾何学形状とサイズ");
		Add("ShapeOriginal", "原生扇区", "原生扇區", "Original Sector", "オリジナルセクター");
		Add("ShapeCircle", "极简圆形", "極簡圓形", "Floating Circle", "フローティングサークル");
		Add("ShapeRounded", "平滑圆角", "平滑圓角", "Rounded Fillet", "角丸フィレット");
		Add("ShapeCapsule", "圆润胶囊", "圓潤膠囊", "Pill Capsules", "ピルカプセル");
		Add("ShapeHexagon", "蜂巢六边形", "蜂巢六邊形", "Hexagon Hive", "ヘキサゴンハニカム");
		Add("RadiusOuter", "轮盘外径", "輪盤外徑", "Outer Radius", "外側半径");
		Add("RadiusInner", "轮盘内径", "輪盤內徑", "Inner Radius", "内側半径");
		Add("RadiusCore", "中心圆半径", "中心圓半徑", "Core Radius", "コア半径");
		Add("SectorGap", "扇区缝隙", "扇區縫隙", "Sector Gap", "セクター間隔");
		Add("SectorCornerRadius", "扇区圆角", "扇區圓角", "Corner Radius", "角丸半径");
		Add("BtnResetGeometry", "重置形态默认值", "重設形態預設值", "Reset Geometry Defaults", "形状初期値に戻す");
		Add("IconLayoutTitle", "图标与文字排版", "圖示與文字排版", "Layout & Typography", "レイアウトと文字");
		Add("LayoutIconText", "图文并茂", "圖文並茂", "Icon & Text", "アイコン＋文字");
		Add("LayoutIconOnly", "仅显示图标", "僅顯示圖示", "Icon Only", "アイコンのみ");
		Add("LayoutTextOnly", "仅显示文字", "僅顯示文字", "Text Only", "文字のみ");
		Add("ShowSectorActionText", "在轮盘扇区中显示动作名称文字", "在輪盤扇區中顯示動作名稱文字", "Show action names in wheel sectors", "ホイールの扇形にアクション名を表示");
		Add("ShowSelectedActionText", "选中扇区时在中心显示动作名称", "選取扇區時在中心顯示動作名稱", "Show selected action name in the center", "選択中のアクション名を中央に表示");
		Add("SectorIconSize", "图标大小", "圖示大小", "Icon Size", "アイコンサイズ");
		Add("SectorFontSize", "文字字号", "文字字級", "Font Size", "文字サイズ");
		Add("LayoutTargetGlobal", "🌐 全局统一排版", "🌐 全域統一排版", "Global Layout", "全体一括設定");
		Add("LayoutTargetSlot", "🎯 扇区独立定制", "🎯 扇區獨立自訂", "Slot Custom", "個別カスタマイズ");
		Add("EnableSlotCustomLayout", "⚡ 启用该扇区独立个性化排版", "⚡ 啟用該扇區獨立個性化排版", "Enable custom styling for this slot", "このセクターの個別スタイルを有効化");
		Add("ResetToGlobalLayout", "🔄 恢复继承全局默认", "🔄 恢復繼承全域預設", "Reset to Global Default", "グローバル設定に戻す");
		Add("ResetSlotLayout", "🔄 恢复继承全局默认", "🔄 恢復繼承全域預設", "Reset to Global Default", "グローバル設定に戻す");
		Add("SectorTextColor", "轮盘文字颜色:", "輪盤文字顏色:", "Sector Text Color:", "ホイール文字色:");
		Add("CoreTextOptions", "中心文字", "中心文字", "Center Text & Selection Options", "中央テキストと選択時表示");
		Add("CoreFontFamily", "中心文字字体:", "中心文字字型:", "Center Font Family:", "中央フォント:");
		Add("SettingsUiScale", "🔍 界面缩放", "🔍 介面縮放", "🔍 UI Scale", "🔍 表示拡大率");
		Add("SettingsUiScaleTip", "调节设置控制台界面整体缩放比例 (80% ~ 200%)，高分屏下可放大文字与控件。窗口尺寸保持不变，内容变大后由页面滚动承接；也可随时使用 Ctrl + / Ctrl - 调节，Ctrl 0 复位。", "調整設定控制台介面整體縮放比例 (80% ~ 200%)，高解析度螢幕下可放大文字與控件。視窗尺寸保持不變，內容變大後由頁面捲動承接；亦可隨時使用 Ctrl + / Ctrl - 調整，Ctrl 0 復位。", "Scale the whole settings console between 80% and 200% for better readability on high-resolution screens. The window size stays untouched - enlarged content simply scrolls. Ctrl + / Ctrl - adjusts it anytime, and Ctrl 0 resets.", "設定画面全体の表示拡大率を 80%〜200% で調整できます。高解像度画面での文字・控件の視認性向上に。ウィンドウサイズは変更されず、拡大した内容はスクロールして表示します。Ctrl + / Ctrl - ですぐに調整、Ctrl 0 でリセット。");
		Add("CoreFontSize", "中心文字大小:", "中心文字大小:", "Center Font Size:", "中央フォントサイズ:");
		Add("CoreFontSizeTitle", "中心文字大小:", "中心文字大小:", "Center Font Size:", "中央フォントサイズ:");
		Add("CoreTextColorAuto", "自动适应配色主题", "自動適應配色主題", "Auto Contrast", "配色テーマに自動追従");
		Add("CoreTextColor", "中心文字颜色:", "中心文字顏色:", "Center Text Color:", "中央文字色:");
		Add("ClickSectorHint", "💡 提示：在右侧画布中点击任意扇区可直接选中", "💡 提示：在右側畫布中點選任意扇區可直接選取", "Tip: Click any sector on the canvas to select", "ヒント: キャンバス上の扇形をクリックして直接選択");
		Add("InheritGlobal", "跟随全局默认", "跟隨全域預設", "Inherit Global Default", "グローバルデフォルトを継承");
		Add("CoreTitle", "中心图标设置", "中心圖示設定", "Center Core Customization", "中央コアのカスタマイズ");
		Add("CoreShowIcon", "显示中心图案 / 贴图", "顯示中心圖案 / 貼圖", "Show Core Icon / Image", "中央アイコン/画像を表示");
		Add("CoreIconType", "核圆图案模式", "核圓圖案模式", "Core Pattern Mode", "コアパターンモード");
		Add("CorePatternExit", "取消手势图标", "取消手勢圖示", "Cancel Cross", "キャンセルバツ");
		Add("CorePatternCrosshair", "精准准心", "精準準心", "Crosshair", "照準レティクル");
		Add("CorePatternWindows", "Windows 微标", "Windows 微標", "Windows Emblem", "Windows ロゴ");
		Add("CorePatternDot", "极简圆点", "極簡圓點", "Minimal Dot", "ミニマルドット");
		Add("CorePatternHome", "主页图标", "首頁圖示", "Home", "ホーム");
		Add("CorePatternPower", "电源图标", "電源圖示", "Power", "電源");
		Add("CorePatternCompass", "星空罗盘", "星空羅盤", "Compass", "コンパス");
		Add("CorePatternCatPaw", "萌宠猫爪", "萌寵貓爪", "Cat Paw", "肉球");
		Add("CorePatternImage", "\ud83d\uddbc\ufe0f 自定义本地图片贴图...", "\ud83d\uddbc\ufe0f 自訂本機圖片貼圖...", "\ud83d\uddbc\ufe0f Custom Local Image...", "\ud83d\uddbc\ufe0f カスタム画像ファイル...");
		Add("BtnBrowseImage", "浏览选择图片", "瀏覽選擇圖片", "Browse Image", "画像を選択");
		Add("ConsoleThemeTitle", "软件主题", "軟體主題", "Console Theme", "コントロールパネルテーマ");
		Add("ThemeCustom", "\ud83c\udfa8 自定义配色", "\ud83c\udfa8 自定義配色", "\ud83c\udfa8 Custom Colors", "\ud83c\udfa8 カスタム配色");
		Add("ThemeSystem", "\ud83d\udda5\ufe0f 跟随 Windows 系统", "\ud83d\udda5\ufe0f 跟隨 Windows 系統", "\ud83d\udda5\ufe0f Follow Windows System", "\ud83d\udda5\ufe0f Windows システムに従う");
		Add("ThemeLight", "☀\ufe0f 极简纯白", "☀\ufe0f 極簡純白", "☀\ufe0f Pure Light", "☀\ufe0f ピュアライト");
		Add("ThemeDark", "\ud83c\udf19 极夜曜黑", "\ud83c\udf19 極夜曜黑", "\ud83c\udf19 OLED Dark", "\ud83c\udf19 OLEDダーク");
		Add("ThemeNavy", "\ud83c\udf0c 午夜深蓝", "\ud83c\udf0c 午夜深藍", "\ud83c\udf0c Midnight Navy", "\ud83c\udf0c ミッドナイトネイビー");
		Add("ThemeViolet", "\ud83d\udd2e 暗夜紫罗兰", "\ud83d\udd2e 暗夜紫羅蘭", "\ud83d\udd2e Royal Violet", "\ud83d\udd2e ロイヤルバイオレット");
		Add("ThemeGray", "⚙\ufe0f 钛金深灰", "⚙\ufe0f 鈦金深灰", "⚙\ufe0f Titanium Gray", "⚙\ufe0f チタングレー");
		Add("GesturesHeader", "手势动作设置", "手勢動作設定", "Gesture Sectors & Action Mappings", "セクター配置とアクション設定");
		Add("SectorCountTitle", "扇区按键数", "扇區按鍵數", "Sector Count", "セクター数（キー数）");
		Add("SectorCount4", "4 键 (十字方位)", "4 鍵 (十字方位)", "4 Sectors (Cross 4-Way)", "4キー (十字方向)");
		Add("SectorCount8", "8 键 (标准八向)", "8 鍵 (標準八向)", "8 Sectors (Standard 8-Way)", "8キー (全方向8方位)");
		Add("SectorCount12", "12 键 (时钟十二向)", "12 鍵 (時鐘十二向)", "12 Sectors (Clock Dial 12-Way)", "12キー (時計盤12方位)");
		Add("ActionTypeHotkey", "⌨\ufe0f 键盘快捷键", "⌨\ufe0f 鍵盤快速鍵", "⌨\ufe0f Keyboard Hotkey", "⌨\ufe0f キーボードショートカット");
		Add("ActionTypeLaunch", "\ud83d\ude80 启动程序/打开网页", "\ud83d\ude80 啟動程式/開啟網頁", "\ud83d\ude80 Launch App / Open URL", "\ud83d\ude80 アプリ起動 / Webを開く");
		Add("ActionTypeSystem", "⚙\ufe0f 系统控制指令", "⚙\ufe0f 系統控制指令", "⚙\ufe0f System Action", "⚙\ufe0f システム制御コマンド");
		Add("BtnRecordHotkey", "点击录制热键", "點擊錄製快速鍵", "Click to Record Hotkey", "クリックしてショートカット録画");
		Add("BtnBrowseApp", "\ud83d\udd0d 选择应用程序...", "\ud83d\udd0d 選擇應用程式...", "\ud83d\udd0d Select Application...", "\ud83d\udd0d アプリケーションを選択...");
		Add("AdvancedHeader", "系统集成与高级偏好设置", "系統整合與進階偏好設定", "System Integration & Preferences", "システム統合と高度な設定");
		Add("LanguageTitle", "界面语言", "介面語言", "Display Language", "表示言語");
		Add("LanguageDesc", "选择软件控制台与轮盘的显示语言，支持即时热切换并自动保存。", "選擇軟體控制台與輪盤的顯示語言，支援即時熱切換並自動儲存。", "Select language for StarPie. Applies immediately without restarting.", "StarPieの表示言語を選択します。再起動不要で即時に切り替わります。");
		Add("ProgramPickerTitle", "选择程序", "選擇程式", "Select Program", "プログラムを選択");
		Add("ProgramPickerHeader", "从已安装的软件和开始菜单中选择", "從已安裝的軟體與開始功能表中選擇", "Select from Installed Apps & Start Menu", "インストール済みアプリやスタートメニューから選択");
		Add("ProgramPickerPlaceholder", "搜索软件名称、可执行文件或路径...", "搜尋軟體名稱、執行檔或路徑...", "Search app name, executable, or path...", "アプリ名、実行可能ファイル、またはパスを検索...");
		Add("ProgramPickerScanning", "正在智能检索系统中已安装的软件，请稍候...", "正在智慧檢索系統中已安裝的軟體，請稍候...", "Scanning installed programs, please wait...", "インストール済みアプリをスキャンしています...");
		Add("BtnManualBrowse", "手动浏览文件...", "手動瀏覽檔案...", "Browse File...", "手動で参照...");
		Add("LangZhCn", "\ud83c\udde8\ud83c\uddf3 简体中文 (Simplified Chinese)", "\ud83c\udde8\ud83c\uddf3 簡體中文 (Simplified Chinese)", "\ud83c\udde8\ud83c\uddf3 简体中文 (Simplified Chinese)", "\ud83c\udde8\ud83c\uddf3 簡体字中国語 (Simplified Chinese)");
		Add("LangZhTw", "\ud83c\udded\ud83c\uddf0/\ud83c\uddf9\ud83c\uddfc 繁體中文 (Traditional Chinese)", "\ud83c\udded\ud83c\uddf0/\ud83c\uddf9\ud83c\uddfc 繁體中文 (Traditional Chinese)", "\ud83c\udded\ud83c\uddf0/\ud83c\uddf9\ud83c\uddfc 繁體中文 (Traditional Chinese)", "\ud83c\udded\ud83c\uddf0/\ud83c\uddf9\ud83c\uddfc 繁体字中国語 (Traditional Chinese)");
		Add("LangEn", "\ud83c\uddfa\ud83c\uddf8 English (US/UK)", "\ud83c\uddfa\ud83c\uddf8 English (US/UK)", "\ud83c\uddfa\ud83c\uddf8 English (US/UK)", "\ud83c\uddfa\ud83c\uddf8 英語 (English)");
		Add("LangJa", "\ud83c\uddef\ud83c\uddf5 日本語 (Japanese)", "\ud83c\uddef\ud83c\uddf5 日本語 (Japanese)", "\ud83c\uddef\ud83c\uddf5 日本語 (Japanese)", "\ud83c\uddef\ud83c\uddf5 日本語 (Japanese)");
		Add("LangAuto", "\ud83d\udda5\ufe0f 跟随系统 (System Default)", "\ud83d\udda5\ufe0f 跟隨系統 (System Default)", "\ud83d\udda5\ufe0f System Default", "\ud83d\udda5\ufe0f システム既定 (System Default)");
		Add("StartupTitle", "开机自启动", "開機自啟動", "Run on Windows Startup", "Windows起動時に自動起動");
		Add("StartupDesc", "在 Windows 开机登录时静默自启动并在后台托盘驻留。", "在 Windows 開機登入時靜默自啟動並在後台托盤駐留。", "Automatically start StarPie silently minimized to tray on login.", "Windows起動時に自動でタスクトレイに常駐します。");
		Add("AutoStartAsAdminTitle", "以管理员权限自启动 (推荐)", "以系統管理員權限自啟動 (推薦)", "Run with Administrator Privileges on Startup", "管理者権限で自動起動 (推奨)");
		Add("AutoStartAsAdminDesc", "通过 Windows 任务计划程序以最高权限静默自启，无需每次弹出 UAC，可在各类高权限窗口中正常响应手势。", "透過 Windows 工作排程器以最高權限靜默自啟，無需每次彈出 UAC，可在各類高權限視窗中正常回應手勢。", "Launches via Windows Task Scheduler with highest privileges without UAC prompt, ensuring gestures work in elevated windows.", "Windowsタスクスケジューラを利用してUACなしで最高権限で自動起動し、管理者権限ウィンドウでも動作します。");
		Add("ProgramPickerRefresh", "\ud83d\udd04 刷新列表", "\ud83d\udd04 重新整理", "\ud83d\udd04 Refresh", "\ud83d\udd04 更新");
		Add("Tier1ConfigSegment", "\ud83d\udd18 一级主轮盘配置", "\ud83d\udd18 一級主輪盤配置", "\ud83d\udd18 Tier 1 Primary Wheel", "\ud83d\udd18 第1層メインホイール");
		Add("Tier2ConfigSegment", "\ud83c\udf1f 二级级联轮盘配置", "\ud83c\udf1f 二級級聯輪盤配置", "\ud83c\udf1f Tier 2 Sub-Wheel", "\ud83c\udf1f 第2層カスケードホイール");
		Add("SubWheelThemeTitle", "二级轮盘视觉风格", "二級輪盤視覺風格", "Tier 2 Visual Style & Colors", "第2層ホイールのスタイルと配色");
		Add("SubWheelThemeDesc", "为二级级联轮盘独立指定视觉渲染器与主题配色，与一级主轮盘自由组合。", "為二級級聯輪盤獨立指定視覺渲染器與主題配色，與一級主輪盤自由組合。", "Independently customize visual style and colors for the secondary cascading wheel.", "第2層カスケードホイールに独自のスタイルと配色を設定します。");
		Add("MemoryTitle", "内存深度整理", "記憶體深度整理", "Memory Optimization", "メモリ最適化");
		Add("MemoryDesc", "启用 Windows 进程工作集深度修剪，后台常驻内存低至 15~25MB。", "啟用 Windows 行程工作集深度修剪，後台常駐記憶體低至 15~25MB。", "Deep trims working set, keeping background RAM usage under 20MB.", "メモリを自動トリムし、バックグラウンド使用量を15〜25MBに維持します。");
		Add("BtnTrimMemory", "立即压缩物理内存", "立即壓縮實體記憶體", "Trim RAM Now", "今すぐメモリ圧縮");
		Add("ElevateTitle", "管理员权限提升", "系統管理員權限提升", "Run as Administrator", "管理者権限で実行");
		Add("ElevateDesc", "以管理员身份重启，可在任务管理器、系统设置等高权限窗口中正常唤起手势。", "以系統管理員身分重啟，可在工作管理員、系統設定等高權限視窗中正常呼出手勢。", "Relaunch with administrator privileges to interact with elevated windows.", "管理者権限で再起動し、タスクマネージャー等の高権限画面でも動作可能にします。");
		Add("BtnElevate", "\ud83d\udee1\ufe0f 以管理员身份重启", "\ud83d\udee1\ufe0f 以系統管理員身分重啟", "\ud83d\udee1\ufe0f Restart as Administrator", "\ud83d\udee1\ufe0f 管理者として再起動");
		Add("BackupTitle", "配置方案与备份", "配置方案與備份", "Configuration Profiles & Backup", "設定プロファイルとバックアップ");
		Add("BackupDesc", "管理多套独立配置方案（如建模CAD、日常办公、游戏娱乐等），随时一键热切换，并支持导入与导出外部配置。", "管理多套獨立配置方案（如建模CAD、日常辦公、遊戲娛樂等），隨時一鍵熱切換，並支援匯入與匯出外部設定。", "Manage multiple independent profiles (CAD, Office, Gaming, etc.) with instant hot-switching, import, and export.", "複数の独立した設定プロファイル（CAD、オフィス、ゲームなど）を管理し、即時切り替え、インポート、エクスポートに対応します。");
		Add("ActiveProfileLabel", "当前激活方案:", "當前啟用方案:", "Active Profile:", "アクティブプロファイル:");
		Add("BtnSaveNewProfile", "➕ 保存为新方案", "➕ 儲存為新方案", "➕ Save as New Profile", "➕ 新規保存");
		Add("BtnRenameProfile", "✏️ 重命名", "✏️ 重新命名", "✏️ Rename", "✏️ 名前の変更");
		Add("BtnDeleteProfile", "🗑️ 删除", "🗑️ 刪除", "🗑️ Delete", "🗑️ 削除");
		Add("BtnExportConfig", "💾 导出选中配置...", "💾 匯出所選配置...", "💾 Export Selected...", "💾 選択した設定をエクスポート...");
		Add("BtnImportConfig", "📂 导入外部配置...", "📂 匯入外部設定檔...", "📂 Import External...", "📂 外部設定をインポート...");
		Add("BtnResetConfig", "🔄 恢复默认配置", "🔄 恢復預設配置", "🔄 Reset to Default", "🔄 デフォルトにリセット");
		Add("UpdateAdvancedToggleTitle", "更新偏好、下载加速与历史版本回退 (展开/折叠)", "更新偏好、下載加速與歷史版本回退 (展開/折疊)", "Update Preferences, Mirrors & Version Rollback (Expand/Collapse)", "更新設定・ミラー・バージョンロールバック (展開/折りたたみ)");
		Add("LogsTitle", "系统运行日志", "系統運行日誌", "System Runtime Logs", "システム動作ログ");
		Add("LogsDesc", "自动记录系统生命周期、手势分发、按键模拟与故障异常信息（保留最近7天），方便故障排查与问题反馈。", "自動記錄系統生命週期、手勢分發、按鍵模擬與故障異常資訊（保留最近7天），方便故障排查與問題回饋。", "Automatically records system lifecycle, gesture events, key simulations, and exceptions (retains 7 days) for diagnostics.", "システムのライフサイクル、ジェスチャイベント、キーシミュレーション、例外を自動記録します（過去7日間保持）。");
		Add("BtnOpenLogFolder", "\ud83d\udcc2 打开日志目录", "\ud83d\udcc2 開啟日誌目錄", "\ud83d\udcc2 Open Log Folder", "\ud83d\udcc2 ログフォルダーを開く");
		Add("BtnViewTodayLog", "\ud83d\udcc4 查看今日运行日志", "\ud83d\udcc4 檢視今日運行日誌", "\ud83d\udcc4 View Today's Log", "\ud83d\udcc4 今日のログを表示");
		Add("AboutHeader", "关于 StarPie", "關於 StarPie", "About StarPie", "StarPie について");
		Add("AboutDesc", "高质感、极速现代 Windows 鼠标轮盘笔势工具", "高質感、極速現代 Windows 滑鼠輪盤手勢工具", "High-aesthetic, ultra-fast modern Windows mouse radial gestures tool.", "洗練されたデザインと高速な応答性を誇る次世代マウスジェスチャーツール");
		Add("BtnOpenChangelog", "查看完整 CHANGELOG", "檢視完整 CHANGELOG", "View Full CHANGELOG", "完全な更新履歴を表示");
		Add("MilestonesTitle", "版本演进历程", "版本演進歷程", "Version Milestones", "バージョン履歴");
		Add("MsgSaveSuccess", "设置已成功保存至硬盘！", "設定已成功儲存至硬碟！", "Settings successfully saved to disk!", "設定が正常に保存されました！");
		Add("MsgConfirmDeletePreset", "确定要永久删除此自定义配色方案吗？\n删除后不可恢复。", "確定要永久刪除此自訂配色方案嗎？\n刪除後不可恢復。", "Are you sure you want to delete this custom color preset?\nThis cannot be undone.", "このカスタム配色プリセットを削除してもよろしいですか？\n削除後は復元できません。");
		Add("MsgConfirmReset", "确定要恢复出厂默认设置吗？所有自定义手势与样式将被重置。", "確定要恢復原廠預設設定嗎？所有自訂手勢與樣式將被重設。", "Are you sure you want to restore factory defaults? All customizations will be reset.", "工場出荷時の初期設定に戻してもよろしいですか？すべてのカスタム設定がリセットされます。");
		Add("TrayPause", "⏸\ufe0f 暂停手势", "⏸\ufe0f 暫停手勢", "⏸\ufe0f Pause Gestures", "⏸\ufe0f ジェスチャーを一時停止");
		Add("TrayResume", "▶\ufe0f 恢复手势", "▶\ufe0f 恢復手勢", "▶\ufe0f Resume Gestures", "▶\ufe0f ジェスチャーを再開");
		Add("TrayPreferences", "⚙\ufe0f 偏好设置", "⚙\ufe0f 偏好設定", "⚙\ufe0f Preferences", "⚙\ufe0f 設定");
		Add("TrayAppearance", "\ud83c\udfa8 外观样式", "\ud83c\udfa8 外觀樣式", "\ud83c\udfa8 Appearance", "\ud83c\udfa8 外観");
		Add("TrayGestures", "⚡ 手势动作", "⚡ 手勢動作", "⚡ Gestures", "⚡ ジェスチャー");
		Add("TrayAbout", "\ud83d\udccb 关于软件", "\ud83d\udccb 關於軟體", "\ud83d\udccb About", "\ud83d\udccb 情報");
		Add("TrayElevate", "\ud83d\udee1\ufe0f 以管理员身份重启", "\ud83d\udee1\ufe0f 以系統管理員身分重啟", "\ud83d\udee1\ufe0f Restart as Administrator", "\ud83d\udee1\ufe0f 管理者として再起動");
		Add("TrayExit", "❌ 退出 StarPie", "❌ 退出 StarPie", "❌ Exit StarPie", "❌ StarPie を終了");
		Add("TrayTooltip", "StarPie - 现代化鼠标轮盘笔势", "StarPie - 現代化滑鼠輪盤手勢", "StarPie - Modern Mouse Radial Gestures", "StarPie - 次世代マウスラジアルジェスチャー");
		Add("UpdateSectionTitle", "软件更新", "軟體更新", "Software Updates", "ソフトウェア更新");
		Add("UpdateStatusLatest", "已是最新版本", "已是最新版本", "Up to Date", "最新バージョンです");
		Add("UpdateStatusNewVersion", "发现新版本", "發現新版本", "Update Available", "新しいバージョンがあります");
		Add("BtnCheckUpdate", "🔄 立即检查更新", "🔄 立即檢查更新", "🔄 Check Updates", "🔄 更新を確認");
		Add("BtnCheckingUpdate", "⏳ 正在检查...", "⏳ 正在檢查...", "⏳ Checking...", "⏳ 確認中...");
		Add("UpdateSilentCheckTitle", "开机静默检查更新", "開機靜默檢查更新", "Silent Check on Startup", "起動時のサイレント更新確認");
		Add("UpdateSilentCheckDesc", "程序启动后在后台静默查询 GitHub Releases", "程式啟動後在後台靜默查詢 GitHub Releases", "Silently query GitHub Releases in the background after launch", "起動後にバックグラウンドで GitHub Releases をサイレント確認");
		Add("UpdateChannelTitle", "更新推送通道", "更新推送通道", "Update Channel", "更新チャンネル");
		Add("UpdateChannelDesc", "选择稳定版或尝鲜版", "選擇穩定版或嘗鮮版", "Select Stable or Preview releases", "安定版またはプレビュー版を選択");
		Add("UpdateChannelStable", "🌟 正式稳定版", "🌟 正式穩定版", "🌟 Stable Release", "🌟 安定版");
		Add("UpdateChannelBeta", "🚀 尝鲜测试版", "🚀 嘗鮮測試版", "🚀 Preview / Beta", "🚀 プレビュー版");
		Add("UpdateProxyTitle", "GitHub 下载加速镜像源", "GitHub 下載加速鏡像源", "GitHub Download Mirror", "GitHub ダウンロードミラー");
		Add("UpdateProxyDesc", "解决国内访问 GitHub Releases 丢包或限速问题，自动加速代理下载", "解決訪問 GitHub Releases 封包遺失或限速問題，自動加速代理下載", "Accelerate GitHub Releases download and mitigate connection issues", "GitHub Releases への接続を高速化し、プロキシ経由でダウンロード");
		Add("UpdateProxyDirect", "🌐 官方直连", "🌐 官方直連", "🌐 Official Direct", "🌐 公式ダイレクト");
		Add("UpdateProxyGhproxy", "⚡ 镜像源 1 (ghfast.top 推荐)", "⚡ 鏡像源 1 (ghfast.top 推薦)", "⚡ Mirror 1 (ghfast.top Recommended)", "⚡ ミラー 1 (ghfast.top 推奨)");
		Add("UpdateProxyMoeyy", "⚡ 镜像源 2 (gh-proxy.com 备用)", "⚡ 鏡像源 2 (gh-proxy.com 備用)", "⚡ Mirror 2 (gh-proxy.com Backup)", "⚡ ミラー 2 (gh-proxy.com 予備)");
		Add("UpdateProxyAkams", "⚡ 镜像源 3 (mirror.ghproxy.com)", "⚡ 鏡像源 3 (mirror.ghproxy.com)", "⚡ Mirror 3 (mirror.ghproxy.com)", "⚡ ミラー 3 (mirror.ghproxy.com)");
		Add("RollbackSectionTitle", "历史版本回退", "歷史版本回退", "Version Rollback", "過去バージョンへのロールバック");
		Add("RollbackSectionDesc", "若当前版本发生兼容性或配置异常，可选择历史版本一键覆盖回退安装。", "若當前版本發生相容性或設定異常，可選擇歷史版本一鍵覆蓋回退安裝。", "If compatibility issues occur, rollback to a prior version with one click.", "互換性の問題が発生した場合は、ワンクリックで以前のバージョンにロールバックできます。");
		Add("RollbackBadgeBeta", "🚀 测试版最多回退5个版本", "🚀 測試版最多回退5個版本", "🚀 Beta track: up to 5 versions", "🚀 ベータ版：最大5バージョンまでロールバック可能");
		Add("RollbackBadgeStable", "🌟 正式版最多回退2个版本", "🌟 正式版最多回退2個版本", "🌟 Stable track: up to 2 versions", "🌟 安定版：最大2バージョンまでロールバック可能");
		Add("BtnRollback", "⬇️ 回退至此版本", "⬇️ 回退至此版本", "⬇️ Rollback to this version", "⬇️ このバージョンにロールバック");
		Add("RollbackEmpty", "（暂无可回退历史版本）", "（暫無可回退歷史版本）", "(No rollback versions available)", "（ロールバック可能なバージョンはありません）");
		Add("RollbackConfirmTitle", "确认版本回退", "確認版本回退", "Confirm Version Rollback", "ロールバックの確認");
		Add("RollbackConfirmMsg", "确定要将 StarPie 回退至版本 {0} 吗？\n\n程序将下载该历史版本安装包并自动覆盖重启。您的自定义手势与按键配置将完整保留。", "確定要將 StarPie 回退至版本 {0} 嗎？\n\n程式將下載該歷史版本安裝包並自動覆蓋重啟。您的自訂手勢與按鍵設定將完整保留。", "Are you sure you want to rollback StarPie to version {0}?\n\nThe update package will be downloaded and safely applied upon restart. Your configurations will be preserved.", "StarPie をバージョン {0} にロールバックしてもよろしいですか？\n\nパッケージをダウンロードして再起動時に適用されます。現在の設定は保持されます。");
		Add("ContributorsHeader", "开源贡献者致谢", "開源貢獻者致謝", "Contributors & Thanks", "コントリビューターへの感謝");
		Add("ContributorsIntro", "感谢以下贡献者为 StarPie (星盘) 开源项目付出的智慧与贡献：", "感謝以下貢獻者為 StarPie (星盤) 開源項目付出的智慧與貢獻：", "Thank you to all contributors who empower StarPie open-source project:", "StarPie オープンソースプロジェクトに貢献してくださった皆様に感謝いたします:");
		Add("ContributorsSyncLocal", "🌐 本地收录名单 (检查更新时可联网刷新)", "🌐 本地收錄名單 (檢查更新時可連網重新整理)", "🌐 Local roster (refreshed when checking updates)", "🌐 ローカル収録名簿 (更新確認時にオンライン更新)");
		Add("ContributorsRefresh", "🔄 刷新", "🔄 重新整理", "🔄 Refresh", "🔄 更新");
		Add("ContributorsRepo", "★ 访问 GitHub 仓库", "★ 造訪 GitHub 倉庫", "★ Visit GitHub Repo", "★ GitHub リポジトリへ");
		Add("OcrCardTitle", "OCR 截屏文字识别与智能接口配置", "OCR 截圖文字辨識與智慧介面配置", "OCR Text Recognition & AI Model Setup", "OCR 画面文字認識と AI モデル設定");
		Add("OcrCardDesc", "管理原生离线识别引擎、多模态 AI 视觉模型 (OpenAI / 硅基流动 / Ollama) 与私有化 HTTP 接口端点与凭证。", "管理原生離線辨識引擎、多模態 AI 視覺模型 (OpenAI / 矽基流動 / Ollama) 與私有化 HTTP 介面端點與憑證。", "Manage native offline OCR engines, multimodal AI visual models, and custom HTTP endpoints.", "ネイティブオフラインOCRエンジン、マルチモーダルAI、およびカスタムHTTPエンドポイントを管理。");
		Add("OcrBadgeLocalEngine", "Windows 本地离线引擎", "Windows 本地離線引擎", "Windows Local OCR", "Windows ローカルOCR");
		Add("BtnTestOcr", "✂️ 截屏测试", "✂️ 截圖測試", "✂️ Test Snipping", "✂️ 認識テスト");
		Add("BtnConfigOcr", "⚙️ 配置接口与模型...", "⚙️ 配置介面與模型...", "⚙️ Setup Engine & API...", "⚙️ エンジンとAPI設定...");
		Add("OcrDialogTitle", "StarPie - OCR 截屏文字识别与智能接口设置", "StarPie - OCR 螢幕截圖文字識別與智慧介面設定", "StarPie - OCR Text Recognition & Engine Settings", "StarPie - OCR 画面文字認識＆エンジン設定");
		Add("OcrDialogHeader", "OCR 截屏文字识别与接口配置", "OCR 螢幕截圖文字識別與介面設定", "OCR Text Recognition & API Setup", "OCR 画面文字認識とAPI設定");
		Add("OcrDialogSubtitle", "支持 Windows 本地原生引擎、AI 视觉多模态大模型与私有化 HTTP 接口", "支援 Windows 本機原生引擎、AI 視覺多模態大模型與私有化 HTTP 介面", "Supports Windows Native OCR, Vision LLMs, and Private HTTP APIs", "Windows ネイティブ、AI ビジョン LLM、プライベート HTTP をサポート");
		Add("OcrProviderSection", "识别引擎服务商 (Provider):", "識別引擎服務商 (Provider):", "Recognition Engine Provider:", "認識エンジンプロバイダー:");
		Add("OcrProviderLocal", "🖥️ 本地离线引擎", "🖥️ 本機離線引擎", "🖥️ Local Offline Engine", "🖥️ ローカルオフライン");
		Add("OcrProviderAi", "🤖 AI 视觉大模型", "🤖 AI 視覺大模型", "🤖 AI Vision LLM", "🤖 AI ビジョンモデル");
		Add("OcrProviderCustom", "🌐 自定义 HTTP", "🌐 自訂 HTTP", "🌐 Custom HTTP", "🌐 カスタム HTTP");
		Add("OcrLocalTitle", "🖥️ Windows 10/11 本地原生 OCR (Windows.Media.Ocr)", "🖥️ Windows 10/11 本機原生 OCR (Windows.Media.Ocr)", "🖥️ Windows 10/11 Native OCR (Windows.Media.Ocr)", "🖥️ Windows 10/11 ネイティブ OCR (Windows.Media.Ocr)");
		Add("OcrLocalDesc", "• 原生离线硬件加速，零延迟 (~15ms)，完全不上云，极致保护本地隐私安全。", "• 原生離線硬體加速，零延遲 (~15ms)，完全不上雲，極致保護本機隱私安全。", "• Native hardware acceleration, ~15ms latency, 100% offline, maximum privacy protection.", "• ネイティブHW加速、低遅延 (~15ms)、クラウド非送信でローカルプライバシーを完全保護。");
		Add("OcrPriorityLang", "优先识别语言:", "優先識別語言:", "Priority Language:", "優先認識言語:");
		Add("OcrLocalAlertNoLang", "⚠️ 当前系统未安装本地 OCR 语言包", "⚠️ 目前系統未安裝本機 OCR 語言套件", "⚠️ Local OCR language pack is not installed on this system", "⚠️ システムにローカル OCR 言語パックがインストールされていません");
		Add("OcrBtnOpenFeatures", "打开系统功能", "開啟系統功能", "Open System Features", "システム機能を開く");
		Add("OcrAiTitle", "🤖 OpenAI 兼容多模态视觉模型 (Vision LLM)", "🤖 OpenAI 相容多模態視覺模型 (Vision LLM)", "🤖 OpenAI-Compatible Vision LLM", "🤖 OpenAI 互換マルチモーダルビジョン (Vision LLM)");
		Add("OcrAiDesc", "• 支持 OpenAI、硅基流动、Ollama、智谱 GLM、DeepSeek-VL 等多模态视觉端点。", "• 支援 OpenAI、矽基流動、Ollama、智譜 GLM、DeepSeek-VL 等多模態視覺端點。", "• Supports OpenAI, SiliconFlow, Ollama, Zhipu GLM, DeepSeek-VL, and other vision endpoints.", "• OpenAI、SiliconFlow、Ollama、Zhipu GLM、DeepSeek-VL などのビジョンエンドポイントに対応。");
		Add("OcrAiEndpoint", "API 端点:", "API 端點:", "API Endpoint:", "API エンドポイント:");
		Add("OcrAiApiKey", "API Key:", "API Key:", "API Key:", "API キー:");
		Add("OcrAiModel", "模型名称:", "模型名稱:", "Model Name:", "モデル名:");
		Add("OcrAiModelPresetDefault", "预设模型...", "預設模型...", "Preset Models...", "プリセットモデル...");
		Add("OcrAiModelPresetGpt", "GPT-4o Mini (推荐)", "GPT-4o Mini (推薦)", "GPT-4o Mini (Recommended)", "GPT-4o Mini (推奨)");
		Add("OcrAiModelPresetQwen", "通义千问 Qwen2.5-VL", "通義千問 Qwen2.5-VL", "Qwen2.5-VL", "Qwen2.5-VL");
		Add("OcrAiModelPresetOllama", "Ollama Llama 3.2 Vision", "Ollama Llama 3.2 Vision", "Ollama Llama 3.2 Vision", "Ollama Llama 3.2 Vision");
		Add("OcrAiModelPresetZhipu", "智谱 GLM-4V", "智譜 GLM-4V", "Zhipu GLM-4V", "Zhipu GLM-4V");
		Add("OcrAiPromptMode", "输出解析模式:", "輸出解析模式:", "Output Mode:", "出力解析モード:");
		Add("OcrAiPromptText", "纯文本提取 (保持原排版)", "純文字擷取 (保持原排版)", "Plain Text (Preserve Layout)", "プレーンテキスト抽出 (レイアウト保持)");
		Add("OcrAiPromptLatex", "LaTeX 数学公式还原 ($$...$$)", "LaTeX 數學公式還原 ($$...$$)", "LaTeX Math Formulas ($$...$$)", "LaTeX 数式復元 ($$...$$)");
		Add("OcrAiPromptMarkdown", "Markdown 表格与结构还原", "Markdown 表格與結構還原", "Markdown Tables & Formatting", "Markdown テーブル・構造復元");
		Add("OcrAiPromptTranslate", "自动多语言智能互译 (Smart Translation)", "自動多語言智能互譯 (Smart Translation)", "Smart Multi-Language Translation", "スマート多言語自動翻訳");
		Add("OcrCustomTitle", "🌐 自定义本地/内网 HTTP OCR 微服务", "🌐 自訂本機/內部網路 HTTP OCR 微服務", "🌐 Custom Local/LAN HTTP OCR Microservice", "🌐 カスタムローカル/LAN HTTP OCR サービス");
		Add("OcrCustomDesc", "• 支持 Umi-OCR、PaddleOCR-json 等本地 HTTP 离线服务接口。", "• 支援 Umi-OCR、PaddleOCR-json 等本機 HTTP 離線服務介面。", "• Supports local offline HTTP services like Umi-OCR and PaddleOCR-json.", "• Umi-OCR や PaddleOCR-json などのローカル HTTP サービスに対応。");
		Add("OcrCustomUrl", "服务 URL:", "服務 URL:", "Service URL:", "サービス URL:");
		Add("OcrBehaviorsSection", "识别完成后的处理行为:", "識別完成後的處理行為:", "Actions After Recognition:", "認識後の自動アクション:");
		Add("OcrBehaviorCopy", "📋 自动复制文本到剪贴板", "📋 自動複製文字到剪貼簿", "📋 Copy text to clipboard automatically", "📋 認識テキストを自動的にクリップボードにコピー");
		Add("OcrBehaviorShowWin", "🪟 弹出识别结果悬浮窗", "🪟 彈出識別結果懸浮窗", "🪟 Show recognition result popup window", "🪟 認識結果ポップアップウィンドウを表示");
		Add("OcrBehaviorRemoveSpaces", "✨ 自动去除中文词间多余空格", "✨ 自動去除中文詞間多餘空格", "✨ Remove redundant spaces between CJK words", "✨ CJK 文字間の余分な空白を自動削除");
		Add("OcrBehaviorMergeLines", "📄 智能合并断行段落", "📄 智慧合併斷行段落", "📄 Merge line breaks into paragraphs smartly", "📄 改行をインテリジェントに結合");
		Add("OcrBtnTestSnippet", "✂️ 截屏测试", "✂️ 截圖測試", "✂️ Snippet Test", "✂️ キャプチャテスト");
		Add("OcrTipTestSnippet", "直接启动全屏选区测试识别", "直接啟動全螢幕選區測試識別", "Launch area selection to test recognition directly", "範囲選択を起動して認識をテスト");
		Add("OcrBtnTestConn", "⚡ 测试接口", "⚡ 測試介面", "⚡ Test Connection", "⚡ 接続テスト");
		Add("OcrTipTestConn", "测试当前所选引擎的连通性", "測試目前所選引擎的連通性", "Test connection of the currently selected engine", "選択中エンジンの接続性をテスト");
		Add("OcrBtnCancel", "取消", "取消", "Cancel", "キャンセル");
		Add("OcrBtnSave", "保存并生效", "儲存並生效", "Save & Apply", "保存して適用");
		Add("OcrMsgTesting", "⏳ 测试中...", "⏳ 測試中...", "⏳ Testing...", "⏳ テスト中...");
		Add("OcrMsgLocalReady", "✓ 本地语言包已就绪，支持原生极速识别", "✓ 本機語言套件已就緒，支援原生極速識別", "✓ Local language pack is ready for instant native recognition", "✓ ローカル言語パック準備完了、高速認識に対応");
		Add("OcrMsgLocalNotInstalled", "⚠️ 当前语言 [{0}] 未安装，可用语言包数: {1}", "⚠️ 目前語言 [{0}] 未安裝，可用語言套件數: {1}", "⚠️ Language [{0}] not installed, available packs: {1}", "⚠️ 言語 [{0}] は未インストールです、利用可能な言語数: {1}");
		Add("OcrMsgEndpointOk", "✓ 接口端点连通正常 (HTTP {0})", "✓ 介面端點連通正常 (HTTP {0})", "✓ Endpoint connected successfully (HTTP {0})", "✓ エンドポイント接続成功 (HTTP {0})");
		Add("OcrMsgEndpointErr", "⚠️ 端点响应异常 (HTTP {0})", "⚠️ 端點回應異常 (HTTP {0})", "⚠️ Endpoint response abnormal (HTTP {0})", "⚠️ エンドポイント応答異常 (HTTP {0})");
		Add("OcrMsgCustomOk", "✓ 微服务已连通 (HTTP {0})", "✓ 微服務已連通 (HTTP {0})", "✓ Microservice connected successfully (HTTP {0})", "✓ サービス接続成功 (HTTP {0})");
		Add("OcrMsgTestFailed", "✕ 连通失败: {0}", "✕ 連通失敗: {0}", "✕ Connection failed: {0}", "✕ 接続失敗: {0}");
		Add("OcrAlertNoAvailableLanguages", "⚠️ 系统未检测到本地 OCR 语言包。建议安装「光学字符识别」可选功能，或切换至上方「AI 视觉模型」。", "⚠️ 系統未偵測到本機 OCR 語言套件。建議安裝「光學字元辨識」選用功能，或切換至上方「AI 視覺模型」。", "⚠️ No local OCR language packs found. Please install the Windows OCR optional feature, or switch to AI Vision LLM above.", "⚠️ ローカル OCR 言語パックが見つかりません。Windows の OCR 機能をインストールするか、AI ビジョンモデルに切り替えてください。");
		Add("OcrResultTitle", "StarPie OCR 识别结果", "StarPie OCR 識別結果", "StarPie OCR Result", "StarPie OCR 認識結果");
		Add("OcrResultHeader", "StarPie OCR 文字识别结果", "StarPie OCR 文字識別結果", "StarPie OCR Text Result", "StarPie OCR テキスト認識結果");
		Add("OcrResultCharCountFmt", "提取文本 (共 {0} 字符):", "擷取文字 (共 {0} 字元):", "Extracted Text ({0} characters):", "抽出テキスト (計 {0} 文字):");
		Add("OcrResultCopiedAuto", "已自动存入系统剪贴板", "已自動存入系統剪貼簿", "Copied to clipboard automatically", "自動的にクリップボードにコピーされました");
		Add("OcrResultCopiedManual", "✓ 已重新复制到剪贴板", "✓ 已重新複製到剪貼簿", "✓ Copied to clipboard again", "✓ クリップボードに再コピーしました");
		Add("OcrResultBtnCopy", "📋 复制文本", "📋 複製文字", "📋 Copy Text", "📋 テキストをコピー");
		Add("OcrResultBtnSearch", "🔍 网页搜索", "🔍 網頁搜尋", "🔍 Web Search", "🔍 ウェブ検索");
		Add("OcrResultBtnSettings", "⚙️ 接口设置", "⚙️ 介面設定", "⚙️ OCR Settings", "⚙️ OCR 設定");
		Add("OcrResultBtnDone", "完成 [ESC]", "完成 [ESC]", "Done [ESC]", "完了 [ESC]");
		Add("AutoStartAsAdminTitle", "以管理员身份开机自启 (推荐)", "以系統管理員身分開機自啟 (推薦)", "Run as Administrator on Startup (Recommended)", "管理者として自動起動 (推奨)");
		Add("AutoStartAsAdminDesc", "通过 Windows 任务计划程序实现最高权限静默自启，无需每次弹出 UAC 提示，可对所有高权限应用生效。", "透過 Windows 工作排程器實現最高權限靜默自啟，無需每次跳出 UAC 提示，可對所有高權限應用程式生效。", "Launch silently with elevated privileges via Windows Task Scheduler without UAC prompts, working across all admin apps.", "Windows タスクスケジューラ経由でUACプロンプトなしに昇格起動し、管理者権限アプリでも確実に機能します。");
		Add("CoreGlobalActions", "全局动作", "全域動作", "Global Actions", "グローバルアクション");
		Add("CoreSectorActions", "{0} 键动作", "{0} 鍵動作", "{0}-Slot Actions", "{0}キー動作");
		Add("DimensionsCardTitle", "轮盘尺寸与间距", "輪盤尺寸與間距", "Radial Geometry & Spacing", "ラジアルの幾何学的寸法と間隔");
		Add("VisualThemeCardTitle", "切削形态与配色", "切削形態與配色", "Cutout Shapes & Color Themes", "カット形状とカラーテーマ");
		Add("ClickSectorHint", "💡 点击任意扇区可直接选中并在左侧微调该扇区的独立文字/图标排版", "💡 點選任一扇區可直接選取並在左側微調該扇區的獨立文字/圖示排版", "💡 Click any sector to select and customize its independent font/icon layout", "💡 セクターをクリックして、フォントやアイコンの個別レイアウトを微調整できます");
		Add("PreviewPanHint", "按住 Ctrl + 鼠标拖动可平移画布", "按住 Ctrl + 滑鼠拖曳可平移畫布", "Hold Ctrl + Drag to pan canvas", "Ctrl + ドラッグでキャンバスをパン");
		Add("ConfigModeSimpleRadio", "💡 简单模式", "💡 簡單模式", "💡 Simple Mode", "💡 シンプルモード");
		Add("ConfigModeProRadio", "⚙️ 高级模式", "⚙️ 高級模式", "⚙️ Pro Mode", "⚙️ プロモード");
		Add("ConfigModeSimpleTitle", "简单模式", "簡單模式", "Simple Mode", "シンプルモード");
		Add("ConfigModeSimpleHint", "已隐藏低频高级微调参数，保留核心极速配置体验", "已隱藏低頻高級微調參數，保留核心極速設定體驗", "Hidden low-frequency advanced parameters for a clean and focused experience", "高度な設定項目を非表示にし、主要な設定に集中します");
		Add("ConfigModeProTitle", "高级模式", "高級模式", "Pro Mode", "プロモード");
		Add("ConfigModeProHint", "已全量开放运行命令、窗口管理、字体排版、平铺高级参数、OCR接口与系统日志等专家功能", "已全量開放運行命令、視窗管理、字型排版、平鋪進階參數、OCR介面與系統日誌等專家功能", "Full access to commands, window management, fonts, tiling parameters, OCR APIs and runtime logs", "コマンド実行、ウィンドウ管理、フォント、分割詳細、OCR API、システムログなどの全機能を利用できます");
		Add("SidebarModeSimple", "💡 简单模式", "💡 簡單模式", "💡 Simple", "💡 シンプル");
		Add("SidebarModePro", "⚙️ 高级模式", "⚙️ 高級模式", "⚙️ Pro Mode", "⚙️ プロ");
		Add("LayerIndicatorSectionTitle", "轮盘层数切换提示徽标", "輪盤層數切換提示徽標", "Layer Switch Indicator Badge", "レイヤ切替インジケーター");
		Add("LayerIndicatorSectionDesc", "呼出轮盘后，滑动滚轮或按快捷键切换多层轮盘时的浮动提示徽标外观与停留时长。", "呼出輪盤後，滑動滾輪或按快捷鍵切換多層輪盤時的浮動提示徽標外觀與停留時長。", "Appearance and fadeout duration of the floating layer badge when switching layers in Advanced Mode.", "アドバンスモードでレイヤを切り替える際のフロートバッジの外観と表示時間。");
		Add("ShowLayerIndicator", "启用层数切换浮动提示徽标", "啟用層數切換浮動提示徽標", "Show Layer Switch Floating Badge", "レイヤ切替フロートバッジを表示");
		Add("LayerIndicatorStyleTitle", "徽标预设风格:", "徽標預設風格:", "Preset Style:", "プリセットスタイル:");
		Add("LayerIndicatorIconTitle", "提示前置图标:", "提示前置圖示:", "Indicator Icon:", "インジケーターアイコン:");
		Add("LayerIndicatorCornerRadiusTitle", "徽标圆角:", "徽標圓角:", "Badge Corner Radius:", "バッジ角の丸み:");
		Add("LayerIndicatorFontSizeTitle", "文字字号:", "文字字型大小:", "Font Size:", "フォントサイズ:");
		Add("LayerIndicatorOffsetYTitle", "垂直偏移:", "垂直偏移:", "Vertical Offset Y:", "垂直オフセット Y:");
		Add("LayerIndicatorDurationTitle", "淡出停留时间:", "淡出停留時間:", "Fadeout Duration:", "表示時間:");
		Add("BtnResetLayerIndicator", "🔄 恢复默认提示徽标样式", "🔄 恢復預設提示徽標樣式", "🔄 Reset Indicator Style", "🔄 インジケータースタイルを初期化");
		Add("SoundEffectsTitle", "轮盘交互音效", "輪盤互動音效", "Radial Interaction Sound Effects", "ホイール起動・操作サウンド効果");
		Add("SoundEffectsDesc", "基于原生 Win32 非托管内存音频管线与极微波形合成，为轮盘唤醒、划过扇区、二级展开与触发确认提供毫秒级零延迟微动音效，建立盲操听觉闭环。", "基於原生 Win32 非託管記憶體音訊管線與極微波形合成，為輪盤喚醒、劃過扇區、二級展開與觸發確認提供毫秒級零延遲微動音效，建立盲操聽覺閉環。", "Powered by native Win32 memory audio pipeline & procedural synthesis, delivering sub-millisecond tactile audio feedback for popup, hover, expansion and execution.", "Win32ネイティブ低遅延メモリオーディオにより、起動、ホバー、サブメニュー展開、実行、キャンセルの各操作に極微フィードバック音を提供します。");
		Add("EnableSoundEffectsTitle", "开启轮盘交互音效 (推荐开启，建立盲操手感)", "開啟輪盤互動音效 (推薦開啟，建立盲操手感)", "Enable Sound Effects (Recommended for muscle memory)", "ホイール操作サウンドを有効化（ブラインド操作に推奨）");
		Add("EnableSoundEffectsSub", "零延迟 < 2ms，常驻内存 < 20KB，不阻塞鼠标任何手势操作。", "零延遲 < 2ms，常駐記憶體 < 20KB，不阻塞滑鼠任何手勢操作。", "Zero latency (<2ms), ultra-low memory (<20KB), non-blocking.", "超低遅延（2ms未満）、メモリ占有極小（20KB未満）、マウス操作を一切妨げません。");
		Add("SoundThemeLabel", "音效主题", "音效主題", "Sound Theme", "サウンドテーマ");
		Add("SoundThemeDesc", "可选机械轴体敲击、现代手机触感微点、轻盈水滴气泡或极简脉冲短音。", "可選機械軸體敲擊、現代手機觸感微點、輕盈水滴氣泡或極簡脈衝短音。", "Choose between mechanical switch, modern tactile haptic, soft bubble, or minimalist blip.", "メカニカル軸、現代風触覚クリック、ソフトバブル、ミニマルパルスから選択できます。");
		Add("SoundVolumeTitle", "交互音量:", "互動音量:", "Feedback Volume:", "効果音音量:");
		Add("SoundVolumeDesc", "硬件级数学振幅无损缩放，完全独立于系统主音量，绝不修改 Windows 系统全局音量。", "硬體級數學振幅無損縮放，完全獨立於系統主音量，絕不修改 Windows 系統全域音量。", "Mathematical sample scaling completely independent of Windows master volume.", "Windowsのマスター音量とは完全に独立した数学的振幅スケーリングを行います。");
		Add("BtnSoundPreview", "🔊 试听全套音效", "🔊 試聽全套音效", "🔊 Preview Sound Pack", "🔊 サウンドを試聴");
		Add("SoundSubEventsTitle", "细项事件独立开关", "細項事件獨立開關", "Independent Event Toggles", "個別イベントのサウンド設定");
		Add("SoundOnPopup", "呼出轮盘", "呼出輪盤", "Menu Popup", "ホイール起動");
		Add("SoundOnHover", "扇区切换/划过", "扇區切換/劃過", "Sector Hover", "セクターホバー");
		Add("SoundOnExpand", "二级菜单展开", "二級選單展開", "Submenu Expand", "サブメニュー展開");
		Add("SoundOnExecute", "动作执行确认", "動作執行確認", "Action Execute", "アクション実行");
		Add("SoundOnCancel", "顺势外甩/取消", "順勢外甩/取消", "Flick Cancel", "キャンセル");
		Add("CustomSoundConfigBtn", "🎛️ 方案配置", "🎛️ 方案配置", "🎛️ Configure Sound", "🎛️ サウンド設定");
		Add("CustomSoundStudioTitle", "🎛️ 自定义交互音效调音台", "🎛️ 自定義互動音效調音台", "🎛️ Custom Sound Studio", "🎛️ カスタムサウンドスタジオ");
		Add("SidebarThemeSystem", "系统", "系統", "System", "システム");
		Add("SidebarThemeLight", "浅色", "淺色", "Light", "ライト");
		Add("SidebarThemeDark", "曜黑", "曜黑", "Dark", "ダーク");
		Add("SidebarThemeGray", "钛灰", "鈦灰", "Gray", "グレー");
		Add("SidebarThemeToggleTip", "切换控制台界面主题 (点击循环切换)", "切換控制台介面主題 (點擊循環切換)", "Toggle Console Theme (Click to cycle)", "コンソールテーマを切り替え（クリックで循環）");
		Add("PARTClearShortcutTip", "清空快捷键", "清空快捷鍵", "Clear shortcut", "ショートカットをクリア");
		Add("LiveSensorReadyTip", "💡 硬件感知器已就绪：随时按下鼠标任意侧键、中键或键盘按键，此处将实时高亮反馈对应按键与键码。", "💡 硬體感知器已就緒：隨時按下滑鼠任意側鍵、中鍵或鍵盤按鍵，此處將即時高亮反饋對應按鍵與鍵碼。", "💡 Hardware sensor ready: Press any mouse button or key to instantly see live feedback and key codes.", "💡 ハードウェアセンサー準備完了: マウスボタンやキーを押すと、リアルタイムでキーコードがハイライト表示されます。");
		Add("TriggerThresholdTitle", "一级轮盘呼出触发位移:", "一級輪盤呼出觸發位移:", "Primary Wheel Popup Trigger Distance:", "メインホイール起動トリガー移動量:");
		Add("TriggerThresholdDesc", "按住触发键移动超过此距离后呼出手势轮盘。距离越小越灵敏，过小可能造成按键微抖误触。", "按住觸發鍵移動超過此距離後呼出手勢輪盤。距離越小越靈敏，過小可能造成按鍵微抖誤觸。", "Hold the trigger key and drag beyond this distance to open the wheel. Smaller values are more sensitive, but may cause jitter misclicks.", "トリガーキーを押しながらこの距離以上ドラッグするとホイールを表示します。値が小さいほど高感度ですが、手のブレで誤作動しやすくなります。");
		Add("CoreDeadzoneTitle", "🎯 核心圆唤醒与死区灵敏度:", "🎯 核心圓喚醒與死區靈敏度:", "🎯 Center Deadzone & Activation Sensitivity:", "🎯 センターデッドゾーンと起動感度:");
		Add("CoreDeadzoneDesc", "调节呼出轮盘后光标停留在中心核心圆触发核心动作或静默取消的有效半径。数值较小时轻划即可命中扇区，数值较大时中心判定区更宽容，更易触发中心核圆动作或防手抖取消。", "調節呼出輪盤後游標停留在中心核心圓觸發核心動作或靜默取消的有效半徑。數值較小時輕劃即可命中扇區，數值較大時中心判定區更寬容，更易觸發中心核圓動作或防手抖取消。", "Effective radius to trigger the center core action or silently cancel. Smaller values select outer sectors easily; larger values provide a wider center safe zone to prevent jitter.", "センター円で中央アクションまたはサイレントキャンセルをトリガーする有効半径。値が小さいと少しのスワイプでセクターを選択でき、値が大きいと中央の許容範囲が広がり手ブレを防止します。");
		Add("MultiTierSectionTitle", "多级轮盘与级联子菜单", "多級輪盤與級聯子選單", "Multi-Tier Cascading Sub-Wheels", "マルチ階層カスケードサブホイール");
		Add("SubWheelTriggerDistLabel", "二级轮盘展开触发距离:", "二級輪盤展開觸發距離:", "Sub-Wheel Expansion Trigger Distance:", "サブホイール展開トリガー距離:");
		Add("SubWheelTriggerDistDesc", "调节光标划出距离中心多远时展开二级级联菜单。数值较小时轻划即可展开，数值较大时需向外划出更远距离才展开二级，防止快速触发一级动作时产生视觉干扰。", "調節游標劃出距離中心多遠時展開二級級聯選單。數值較小時輕劃即可展開，數值較大時需向外劃出更遠距離才展開二級，防止快速觸發一級動作時產生視覺干擾。", "Drag distance required from the center to expand sub-actions. Smaller values expand quickly; larger values avoid visual clutter during fast primary gestures.", "中心からどれだけドラッグした時にサブメニューを展開するか設定します。小さい値では素早く展開し、大きい値ではメインアクション実行時の視覚的邪魔を防ぎます。");
		Add("DirAuto", "✨ 自动（运动反方向）", "✨ 自動（運動反方向）", "✨ Auto (Opposite Motion)", "✨ 自動（移動の逆方向）");
		Add("DirUp", "⬆ 上方", "⬆ 上方", "⬆ Up", "⬆ 上");
		Add("DirDown", "⬇ 下方", "⬇ 下方", "⬇ Down", "⬇ 下");
		Add("DirLeft", "⬅ 左方", "⬅ 左方", "⬅ Left", "⬅ 左");
		Add("DirRight", "➡ 右方", "➡ 右方", "➡ Right", "➡ 右");
		Add("DirUpLeft", "↖ 左上", "↖ 左上", "↖ Up-Left", "↖ 左上");
		Add("DirUpRight", "↗ 右上", "↗ 右上", "↗ Up-Right", "↗ 右上");
		Add("DirDownLeft", "↙ 左下", "↙ 左下", "↙ Down-Left", "↙ 左下");
		Add("DirDownRight", "↘ 右下", "↘ 右下", "↘ Down-Right", "↘ 右下");
		Add("GestureMinSegmentTip", "最小段长（像素）：越大越难把中途小拐弯误识别为方向段", "最小段長（像素）：越大越難把中途小拐彎誤識別為方向段", "Minimum segment length (px): Higher values prevent jitter turns from registering as direction strokes.", "最小セグメント長（px）: 値が大きいほど、軌跡の微小な曲がりを誤検出にくくなります。");
		Add("TipGestureAppPath", "选择的应用程序路径", "選擇的應用程式路徑", "Selected application path", "選択されたアプリのパス");
		Add("TipGestureBrowseApp", "选择应用程序或快捷方式...", "選擇應用程式或捷徑...", "Select application or shortcut...", "アプリやショートカットを選択...");
		Add("TipGestureFolderPath", "选择的本地文件夹路径", "選擇的本機資料夾路徑", "Selected local folder path", "選択されたフォルダパス");
		Add("TipGestureBrowseFolder", "选择本地文件夹...", "選擇本機資料夾...", "Select local folder...", "ローカルフォルダを選択...");
		Add("TipGestureCmd", "要运行的命令，如 ping -n 3 127.0.0.1", "要運行的命令，如 ping -n 3 127.0.0.1", "Command to run, e.g. ping -n 3 127.0.0.1", "実行するコマンド（例: ping -n 3 127.0.0.1）");
		Add("TipGestureTaskbarSlot", "任务栏第 N 个应用（同 Win+N 槽位语义）", "工作列第 N 個應用（同 Win+N 槽位語義）", "N-th taskbar app (equivalent to Win+N slot)", "タスクバーの N 番目のアプリ (Win+N 相当)");
		Add("TipGestureTilePreset", "平铺布局预设", "平鋪佈局預設", "Tile layout preset", "タイルレイアウトプリセット");
		Add("TipGestureCustomName", "名称（显示自定义，可留空）", "名稱（顯示自訂，可留空）", "Display name (optional)", "表示名（任意、空欄可）");
		Add("BtnTestGesture", "测试", "測試", "Test", "テスト");
		Add("TipDeleteGesture", "删除此手势映射", "刪除此手勢映射", "Delete this gesture mapping", "このジェスチャー割り当てを削除");
		Add("BtnAddGestureMapping", "➕ 添加手势映射", "➕ 新增手勢映射", "➕ Add Gesture Mapping", "➕ ジェスチャー割り当てを追加");
		Add("AnimSpeedCustom", "🎛️ 自定义速度", "🎛️ 自訂速度", "🎛️ Custom Speed", "🎛️ カスタム速度");
		Add("SoundPresetMechanical", "⚙️ 机械手感", "⚙️ 機械手感", "⚙️ Mechanical", "⚙️ メカニカル");
		Add("SoundPresetCrisp", "✨ 现代清脆", "✨ 現代清脆", "✨ Modern Crisp", "✨ クリスプモダン");
		Add("SoundPresetBubble", "🫧 柔和气泡", "🫧 柔和氣泡", "🫧 Soft Bubble", "🫧 ソフトバブル");
		Add("SoundPresetShort", "⚡ 极简短音", "⚡ 極簡短音", "⚡ Minimal Click", "⚡ ミニマルショート");
		Add("SoundPresetCustom", "🎛️ 自定义方案", "🎛️ 自訂方案", "🎛️ Custom Studio", "🎛️ カスタムスタジオ");
		Add("SoundMixerTitle", "🎛️ 自定义交互音效调音台", "🎛️ 自訂互動音效調音台", "🎛️ Custom Interaction Sound Studio", "🎛️ カスタム効果音ミキサー");
		Add("SoundMixerBadge", "原生支持", "原生支援", "Native", "ネイティブ");
		Add("SoundMixerDesc", "为 5 个核心交互手势事件单独调校程序化极微波形、本地音频采样与音高音量。", "為 5 個核心互動手勢事件單獨調校程式化極微波形、本地音訊取樣與音高音量。", "Fine-tune procedural micro-waveforms, local samples, pitch and volume across 5 core interaction events.", "5つのコアジェスチャーイベントごとに微小波形、ローカル音声サンプル、ピッチ、音量を個別に調整できます。");
		Add("BtnNewSoundProfile", "➕ 新建", "➕ 新建", "➕ New", "➕ 新規");
		Add("TipNewSoundProfile", "新建自定义方案", "新建自訂方案", "Create new custom sound profile", "カスタム音効プロファイルを新規作成");
		Add("BtnDeleteSoundProfile", "🗑️ 删除", "🗑️ 刪除", "🗑️ Delete", "🗑️ 削除");
		Add("TipDeleteSoundProfile", "删除当前选中的自定义方案", "刪除當前選中的自訂方案", "Delete selected custom sound profile", "選択したプロファイルを削除");
		Add("BtnImportSoundProfile", "📂 导入", "📂 匯入", "📂 Import", "📂 インポート");
		Add("TipImportSoundProfile", "导入音效方案", "匯入音效方案", "Import sound profile", "音効プロファイルをインポート");
		Add("BtnExportSoundProfile", "💾 导出", "💾 匯出", "💾 Export", "💾 エクスポート");
		Add("TipExportSoundProfile", "导出当前方案", "匯出當前方案", "Export current sound profile", "現在のプロファイルをエクスポート");
		Add("BtnResetSoundProfile", "🔄 重置", "🔄 重設", "🔄 Reset", "🔄 初期化");
		Add("TipResetSoundProfile", "重置当前方案为预置默认值", "重設當前方案為預設預設值", "Reset profile to preset defaults", "プロファイルを初期プリセットに戻す");
		Add("BtnOpenSoundEditorWindow", "🎛️ 独立大窗", "🎛️ 獨立大窗", "🎛️ Studio Window", "🎛️ 専用ウィンドウ");
		Add("TipOpenSoundEditorWindow", "打开独立大窗口精细调音台", "開啟獨立大視窗精細調音台", "Open standalone fine-tuning sound studio", "独立した大画面サウンドスタジオを開く");
		Add("SoundSelectProfileLabel", "选择配置方案:", "選擇配置方案:", "Select Sound Profile:", "音効プロファイルを選択:");
		Add("SoundPlayFlowBtn", "🔊 连续模拟完整手势交互体验", "🔊 連續模擬完整手勢互動體驗", "🔊 Simulate Full Gesture Interaction Flow", "🔊 ジェスチャー操作フロー全体を連続シミュレート");
		Add("SoundPlayFlowTip", "依次回放：唤出 ➔ 划过 ➔ 展开 ➔ 执行 ➔ 脱离", "依次回放：喚出 ➔ 劃過 ➔ 展開 ➔ 執行 ➔ 脫離", "Playback sequence: Popup ➔ Hover ➔ Expand ➔ Execute ➔ Escape", "再生順: ポップアップ ➔ ホバー ➔ 展開 ➔ 実行 ➔ 脱出");
		Add("SoundFlowReadyStatus", "准备就绪", "準備就緒", "Ready", "準備完了");
		Add("SoundSynthNotice", "⚡ 纯内存波形合成，零延迟 < 2ms，不占额外资源", "⚡ 純記憶體波形合成，零延遲 < 2ms，不佔額外資源", "⚡ In-memory procedural synthesis, zero latency < 2ms, zero bloat", "⚡ メモリ内プロシージャル波形合成、超低遅延 < 2ms、リソース消費ゼロ");
		Add("SystemAudioWarning", "⚠️ 检测到 Windows 系统主音量当前为 0% 或已静音，会导致所有交互音效无声。", "⚠️ 檢測到 Windows 系統主音量當前為 0% 或已靜音，會導致所有互動音效無聲。", "⚠️ System master volume is muted or at 0%, causing interaction sound effects to be silent.", "⚠️ システムのマスター音量がミュートまたは0%のため、効果音が聞こえません。");
		Add("BtnRestoreSystemAudio", "🔊 一键解除静音并恢复音量 (50%)", "🔊 一鍵解除靜音並恢復音量 (50%)", "🔊 Unmute & Restore Volume (50%)", "🔊 ミュート解除して音量を復元 (50%)");
		Add("OuterEscapeCheckboxDesc", "超过轮盘外圈范围后立即解除高亮，松开右键 0 误触安全放弃。", "超過輪盤外圈範圍後立即解除高亮，放開右鍵 0 誤觸安全放棄。", "De-highlights sectors when dragging outside the wheel; release safely without accidental triggers.", "ホイール外枠を超えると選択を即座に解除し、右クリックを離しても誤作動なく安全に中止します。");
		Add("OuterEscapeSilentNote", "关闭则外甩取消仍为静默关闭。", "關閉則外甩取消仍為靜默關閉。", "When disabled, flick-out cancel silently closes the wheel.", "無効の場合、外側スワイプによるキャンセルは静かにホイールを閉じます。");
		Add("OuterEscapePresetsLabel", "⚡ 外甩常用预设:", "⚡ 外甩常用預設:", "⚡ Common Flick-Out Presets:", "⚡ 外側スワイプの常用プリセット:");
		Add("PresetShowDesktop", "🖥️ 显示桌面 (Win+D)", "🖥️ 顯示桌面 (Win+D)", "🖥️ Show Desktop (Win+D)", "🖥️ デスクトップ表示 (Win+D)");
		Add("PresetShowDesktopTip", "外甩快速显示桌面，查阅文件或切换任务", "外甩快速顯示桌面，查閱檔案或切換任務", "Quickly minimize all to view desktop or switch tasks", "素早くデスクトップを表示し、ファイル確認やタスク切替を行います");
		Add("PresetTaskView", "📑 任务视图 (Win+Tab)", "📑 任務檢視 (Win+Tab)", "📑 Task View (Win+Tab)", "📑 タスクビュー (Win+Tab)");
		Add("PresetTaskViewTip", "外甩浏览多虚拟桌面与所有活动任务窗口", "外甩瀏覽多虛擬桌面與所有活動任務視窗", "View virtual desktops and active task windows", "仮想デスクトップとすべてのアクティブウィンドウを表示");
		Add("PresetCancelEsc", "↩️ 取消/返回 (Esc)", "↩️ 取消/返回 (Esc)", "↩️ Cancel / Back (Esc)", "↩️ キャンセル/戻る (Esc)");
		Add("PresetCancelEscTip", "外甩退出当前弹窗或中断当前操作", "外甩退出當前彈窗或中斷當前操作", "Dismiss popups or abort current operation", "現在のポップアップや操作を中断して終了");
		Add("PresetScreenSnipping", "✂️ 系统截屏 (Win+Shift+S)", "✂️ 系統截圖 (Win+Shift+S)", "✂️ Snipping Tool (Win+Shift+S)", "✂️ 画面キャプチャ (Win+Shift+S)");
		Add("PresetScreenSnippingTip", "外甩快速唤起 Windows 区域截屏工具", "外甩快速喚起 Windows 區域截圖工具", "Launch Windows Snipping Tool region capture", "Windows 領域キャプチャツールを素早く起動");
		Add("PresetTileHalfSplit", "🪟 左右对半平铺", "🪟 左右對半平鋪", "🪟 Snap Left/Right Half", "🪟 左右分割スナップ");
		Add("PresetTileHalfSplitTip", "外甩将当前窗口以左右对半形式快速分屏", "外甩將當前視窗以左右對半形式快速分屏", "Snap active window into half-screen split", "アクティブウィンドウを左右半分に分割配置");
		Add("PresetStarPieSettings", "⚙️ StarPie 控制台", "⚙️ StarPie 控制台", "⚙️ StarPie Settings", "⚙️ StarPie 設定");
		Add("PresetStarPieSettingsTip", "外甩呼出 StarPie 配置控制台界面", "外甩呼出 StarPie 配置控制台介面", "Open StarPie settings console", "StarPie 設定コンソール画面を開く");
		Add("ActionFormTypeLabel", "动作类型:", "動作類型:", "Action Type:", "アクションの種類:");
		Add("ActionFormNameLabel", "动作显示名称:", "動作顯示名稱:", "Display Name:", "表示名:");
		Add("ActionFormNameTip", "自定义此动作的显示名称", "自訂此動作的顯示名稱", "Custom display label for this action", "このアクションの表示名をカスタマイズ");
		Add("ActionFormHotkeysLabel", "快捷按键组合:", "快捷按鍵組合:", "Shortcut Combo:", "ショートカットの組み合わせ:");
		Add("BtnActionBuildHotkeys", "⚙️ 拼装...", "⚙️ 拼裝...", "⚙️ Builder...", "⚙️ 構成...");
		Add("TipActionBuildHotkeys", "打开快捷键组合拼装器", "開啟快捷鍵組合拼裝器", "Open hotkey combination builder", "ショートカットキービルダーを開く");
		Add("ActionFormAppPathLabel", "目标应用程序路径:", "目標應用程式路徑:", "Application Path:", "アプリのパス:");
		Add("BtnActionPickProgram", "📦 软件库选择...", "📦 軟體庫選擇...", "📦 App Library...", "📦 アプリ一覧から選択...");
		Add("TipActionPickProgram", "从已安装软件与开始菜单中选择", "從已安裝軟體與開始功能表中選擇", "Select from installed apps and Start Menu", "インストール済みアプリやスタートメニューから選択");
		Add("BtnActionCaptureWindow", "🎯 捕捉运行窗口...", "🎯 捕捉運行視窗...", "🎯 Window Sniper...", "🎯 ウィンドウ捕捉...");
		Add("TipActionCaptureWindow", "从桌面正在运行的程序中选择或拖拽准星瞄准抓取", "從桌面正在運行的程式中選擇或拖拽準星瞄準抓取", "Select from running windows or drag crosshair to target", "実行中のウィンドウから選択するか照準をドラッグして捕捉");
		Add("BtnActionBrowseFile", "📂 浏览...", "📂 瀏覽...", "📂 Browse...", "📂 参照...");
		Add("TipActionBrowseFile", "打开文件浏览窗口选择可执行文件", "開啟檔案瀏覽視窗選擇可執行檔", "Browse for an executable file", "実行可能ファイルを参照して選択");
		Add("ActionFormWebUrlLabel", "目标网址 URL:", "目標網址 URL:", "Website URL:", "ウェブサイト URL:");
		Add("ActionFormCommonUrlsLabel", "常用网址:", "常用網址:", "Quick Links:", "クイックリンク:");
		Add("ActionFormFolderPathLabel", "目标本地文件夹路径:", "目標本機資料夾路徑:", "Folder Path:", "フォルダパス:");
		Add("BtnActionBrowseFolder", "📂 浏览文件夹...", "📂 瀏覽資料夾...", "📂 Browse Folder...", "📂 フォルダを参照...");
		Add("TipActionBrowseFolder", "选择本地文件夹路径...", "選擇本機資料夾路徑...", "Select a local directory path...", "ローカルフォルダのパスを選択...");
		Add("ActionFormCmdLabel", "命令行指令与终端类型:", "命令列指令與終端機類型:", "Command Line & Shell:", "コマンドラインとシェル:");
		Add("ActionFormWindowCtrlLabel", "🪟 窗口控制子模式:", "🪟 視窗控制子模式:", "🪟 Window Control Sub-Mode:", "🪟 ウィンドウ制御モード:");
		Add("ActionFormSysCmdsLabel", "系统全局指令预设:", "系統全域指令預設:", "System Command Presets:", "システムコマンドプリセット:");
		Add("BtnTestCancelAction", "🧪 模拟测试触发", "🧪 模擬測試觸發", "🧪 Simulate Trigger", "🧪 テスト実行");
		Add("CancelActionStatusHint", "💡 外甩脱离轮盘时，立即执行此自定义动作；回到轮盘中心仍为静默关闭。", "💡 外甩脫離輪盤時，立即執行此自訂動作；回到輪盤中心仍為靜默關閉。", "💡 Executes this custom action when flicked outward. Moving back to center still closes silently.", "💡 ホイール外側にスワイプするとこのアクションを実行します。中心に戻すと静かに閉じます。");
		Add("EdgeOverflowTitle", "屏幕边缘呼出智能防溢出与光标自动对齐", "螢幕邊緣呼出智慧防溢出與游標自動對齊", "Edge Overflow Protection & Smart Cursor Alignment", "画面端オーバーフロー防止とカーソル自動整列");
		Add("EdgeOverflowDesc", "当在屏幕四周边缘（顶部、底部或两侧）呼出轮盘时，智能检测显示器安全边界，防止轮盘扇区被截断并自动对齐光标至轮盘物理中心。", "當在螢幕四周邊緣（頂部、底部或兩側）呼出輪盤時，智慧檢測顯示器安全邊界，防止輪盤扇區被截斷並自動對齊游標至輪盤物理中心。", "Detects screen boundaries when opening near screen edges, preventing sector clipping and aligning cursor to the wheel center.", "画面端付近でホイールを表示する際に境界を検知し、セクターの画面外はみ出しを防ぎカーソルを物理中心に整列させます。");
		Add("EdgeOverflowStrategyLabel", "防溢出处理策略:", "防溢出處理策略:", "Overflow Prevention Strategy:", "はみ出し防止ポリシー:");
		Add("EdgeOverflowStrategyDesc", "智能贴边：自动推入屏幕并对齐光标；屏幕中心：直接在当前显示器正中展现。", "智慧貼邊：自動推入螢幕並對齊游標；螢幕中心：直接在當前顯示器正中展現。", "Smart Snap: Push wheel into bounds and align cursor; Screen Center: Always pop up at monitor center.", "スマートスナップ: 画面内に収めカーソルを整列; 画面中央: モニターの中央に直接表示。");
		Add("EdgeOverflowStrategyAuto", "🛡️ 智能贴边防溢出 (推荐)", "🛡️ 智慧貼邊防溢出 (推薦)", "🛡️ Smart Edge Push (Recommended)", "🛡️ スマートスナップ (推奨)");
		Add("EdgeOverflowStrategyCenter", "🎯 屏幕物理正中心呼出", "🎯 螢幕物理正中心呼出", "🎯 Monitor Center Popup", "🎯 モニター中央にポップアップ");
		Add("EdgeOverflowStrategyNone", "🚫 原生跟随光标 (允许溢出)", "🚫 原生跟隨游標 (允許溢出)", "🚫 Strict Cursor Follow (Allow Overflow)", "🚫 カーソル追従 (はみ出し許可)");
		Add("EdgeOverflowMarginXLabel", "X 轴边缘安全边距:", "X 軸邊緣安全邊距:", "Horizontal (X) Safe Margin:", "水平 (X) 安全マージン:");
		Add("EdgeOverflowMarginXDesc", "调节轮盘左右边缘距离屏幕物理视口边界的保留间距。增大数值可让轮盘更早贴入屏幕内侧并自动对齐光标。", "調節輪盤左右邊緣距離螢幕物理視口邊界的保留間距。增大數值可讓輪盤更早貼入螢幕內側並自動對齊游標。", "Safety padding between wheel sides and monitor viewport edges. Higher values push wheel inward sooner.", "左右エッジと画面端の安全マージン。値を大きくするとより内側にスナップします。");
		Add("EdgeOverflowMarginYLabel", "Y 轴边缘安全边距:", "Y 軸邊緣安全邊距:", "Vertical (Y) Safe Margin:", "垂直 (Y) 安全マージン:");
		Add("EdgeOverflowMarginYDesc", "调节轮盘上下边缘距离屏幕物理视口边界（避让任务栏与顶部标题栏）的保留间距。", "調節輪盤上下邊緣距離螢幕物理視口邊界（避讓任務欄與頂部標題欄）的保留間距。", "Safety padding between wheel top/bottom and viewport edges (clears taskbars and title bars).", "上下エッジと画面端（タスクバーやタイトルバーを回避）の安全マージン。");
		Add("BlacklistModeLabel", "排除黑名单模式", "排除黑名單模式", "Blacklist Mode", "ブラックリストモード");
		Add("BlacklistModeSub", "全局生效，仅在名单内程序放行右键", "全域生效，僅在名單內程式放行右鍵", "Global activation; releases trigger key in listed apps", "全体で有効。リスト内のアプリでのみキーを通過");
		Add("WhitelistModeLabel", "启用白名单模式", "啟用白名單模式", "Whitelist Mode", "ホワイトリストモード");
		Add("WhitelistModeSub", "仅在名单内程序生效，其余完全放行", "僅在名單內程式生效，其餘完全放行", "Active only in listed apps; releases trigger key elsewhere", "リスト内のアプリでのみ有効。他は完全にキーを通過");
		Add("BtnConfigProcessTrigger", "⚙️ 配置触发键", "⚙️ 配置觸發鍵", "⚙️ Custom Trigger", "⚙️ トリガー設定");
		Add("TipConfigProcessTrigger", "为此进程录制专属的呼出按键或组合键", "為此處理程序錄製專屬的呼出按鍵或組合鍵", "Record dedicated popup trigger or combo for this process", "このアプリ専用の起動キーやコンボを登録");
		Add("BtnRestoreProcessPass", "🔄 恢复放行", "🔄 恢復放行", "🔄 Passthrough", "🔄 通過に戻す");
		Add("TipRestoreProcessPass", "清除专属按键，恢复完全放行", "清除專屬按鍵，恢復完全放行", "Clear dedicated trigger and restore full key passthrough", "専用キーをクリアし完全通過に戻す");
		Add("TipRemoveProcessItem", "从名单中移除此进程", "從名單中移除此處理程序", "Remove process from list", "リストからこのプロセスを削除");
		Add("ProcessCustomTriggerCardTitle", "🎯 进程专属唤醒按键配置：", "🎯 處理程序專屬喚醒按鍵配置：", "🎯 Dedicated Process Trigger Configuration:", "🎯 アプリ専用トリガーキー設定:");
		Add("BtnCloseCardTip", "收起此配置卡片", "收起此配置卡片", "Collapse card", "カードを閉じる");
		Add("ProcessCustomTriggerCardDesc", "为选中的程序配置单独的轮盘呼出按键（如在 SolidWorks 中配置中键/侧键，避免与右键笔势冲突；普通右键将 100% 放行给该软件）。", "為選中的程式配置單獨的輪盤呼出按鍵（如在 SolidWorks 中配置中鍵/側鍵，避免與右鍵筆勢衝突；普通右鍵將 100% 放行給該軟體）。", "Configure dedicated triggers for specific apps (e.g. Middle/Side click in SolidWorks to avoid right-click gesture conflicts; standard right-click is fully passed through).", "指定アプリ専用のトリガーキーを設定（例: SolidWorks で中クリックやサイドボタンを割り当て、右クリックジェスチャーとの競合を防止。通常右クリックはアプリに通過）。");
		Add("ProcessCurrentTriggerLabel", "当前专属触发键：", "當前專屬觸發鍵：", "Current Dedicated Trigger:", "現在の専用トリガー:");
		Add("ProcessTriggerUnconfigured", "🚫 未配置", "🚫 未配置", "🚫 Unconfigured", "🚫 未設定");
		Add("BtnRecordProcessTrigger", "🔴 点击录制专属按键 / 组合键", "🔴 點擊錄製專屬按鍵 / 組合鍵", "🔴 Click to Record Dedicated Key / Combo", "🔴 クリックして専用キー/コンボを録画");
		Add("BtnResetProcessTrigger", "🔄 恢复默认", "🔄 恢復預設", "🔄 Reset Default", "🔄 デフォルトに戻す");
		Add("ProcessSensorReadyTip", "硬件感知器已就绪：点击上方录制按钮后，按下你想作为该程序呼出键的鼠标按键（如中键/侧键）或键盘按键，即可自动捕获。", "硬體感知器已就緒：點擊上方錄製按鈕後，按下你想作為該程式呼出鍵的滑鼠按鍵（如中鍵/側鍵）或鍵盤按鍵，即可自動捕獲。", "Hardware sensor ready: Click record above, then press the desired mouse button (Middle/Side) or key to capture automatically.", "ハードウェアセンサー準備完了: 上の録画ボタンをクリック後、割り当てたいマウスボタンやキーを押すと自動登録されます。");
		Add("ProcessTriggerDedicated", "专属按键", "專屬按鍵", "Dedicated", "専用キー");
		Add("ProcessTriggerDefaultPass", "未配置专属键 (完全放行右键)", "未配置專屬鍵 (完全放行右鍵)", "Not configured (right click passed through)", "未設定（右クリックを完全通過）");
		Add("ActionBingSearch", "Bing 搜索", "Bing 搜尋", "Bing Search", "Bing 検索");
		Add("TipGestureOpacity", "输入不透明度百分比 (30~100)", "輸入不透明度百分比 (30~100)", "Enter opacity percentage (30~100)", "不透明度のパーセンテージを入力 (30~100)");
		Add("SettingsWindowTitle", "StarPie 设置控制台", "StarPie 設定主控台", "StarPie Settings Console", "StarPie 設定コンソール");
		Add("ClearHotkey", "清空快捷键", "清空快捷鍵", "Clear Hotkey", "ショートカットをクリア");
		Add("HotkeyPlaceholder", "点击录制/按Esc取消...", "點擊錄製/按Esc取消...", "Click to record / Esc to cancel...", "クリックして録音 / Escでキャンセル...");
		Add("HotkeyRecordingHint", "🔴 录制中... 点击或按Esc完成", "🔴 錄製中... 點擊或按Esc完成", "🔴 Recording... Click or press Esc to finish", "🔴 録音中... クリックまたはEscで完了");
		Add("HotkeyPressCombination", "🔴 请按下快捷键组合...", "🔴 請按下快捷鍵組合...", "🔴 Please press key combination...", "🔴 ショートカットキーの組み合わせを押してください...");
		Add("Tab1_UiStyleLabel", "主题风格:", "主題風格:", "Theme Style:", "テーマスタイル:");
		Add("UiStyleClassicRing", "经典圆环", "經典圓環", "Classic Ring", "クラシックリング");
		Add("UiStyleCleanSectors", "极简扇区", "極簡扇區", "Clean Sectors", "クリーンセクター");
		Add("UiStyleGlassmorphism", "液态毛玻璃", "液態毛玻璃", "Liquid Glassmorphism", "リキッドグラス");
		Add("Tab1_ThemePresetLabel", "配色方案:", "配色方案:", "Color Scheme:", "カラースキーム:");
		Add("ThemeItemSystem", "跟随系统", "跟隨系統", "Follow System", "システムに従う");
		Add("ThemeItemDark", "深色模式", "深色模式", "Dark Mode", "ダークモード");
		Add("ThemeItemLight", "浅色模式", "淺色模式", "Light Mode", "ライトモード");
		Add("ThemeItemMatchaForest", "抹茶森林", "抹茶森林", "Matcha Forest", "抹茶フォレスト");
		Add("ThemeItemGlacialIce", "冰川透蓝", "冰川透藍", "Glacial Ice", "氷河アイスブルー");
		Add("ThemeItemMorandiMuted", "莫兰迪柔灰", "莫蘭迪柔灰", "Morandi Muted Gray", "モランディグレー");
		Add("ThemeItemCustom", "🎨 自定义配色", "🎨 自訂配色", "🎨 Custom Colors", "🎨 カスタム配色");
		Add("BtnNewCustomPreset", "➕ 新建配色", "➕ 新建配色", "➕ New Preset", "➕ 新規プリセット");
		Add("TipNewCustomPreset", "基于当前色彩创建全新的自定义配色方案预设", "基於當前色彩創建全新的自訂配色方案預設", "Create a new custom color preset based on current colors", "現在の色に基づいて新しいカスタムカラースキームプリセットを作成");
		Add("BtnRenameCustomPreset", "✏️ 重命名预设", "✏️ 重新命名預設", "✏️ Rename Preset", "✏️ プリセット名を変更");
		Add("TipRenameCustomPreset", "重命名当前选中的自定义配色方案预设", "重命名當前選中的自訂配色方案預設", "Rename the selected custom color preset", "選択したカスタムカラープリセットの名前を変更");
		Add("BtnDeleteCustomPreset", "🗑️ 删除预设", "🗑️ 刪除預設", "🗑️ Delete Preset", "🗑️ プリセットを削除");
		Add("TipDeleteCustomPreset", "删除当前选中的自定义配色方案预设", "刪除當前選中的自訂配色方案預設", "Delete the selected custom color preset", "選択したカスタムカラープリセットを削除");
		Add("Tab1_CustomColorsSectionLabel", "自定义颜色 (色盘调色 / 屏幕吸色):", "自訂顏色 (色盤調色 / 螢幕吸色):", "Custom Colors (Palette / Eyedropper):", "カスタムカラー (パレット / スポイト):");
		Add("Tab1_SectorBgLabel", "扇区底色:", "扇區底色:", "Sector Background:", "セクター背景色:");
		Add("TipPickColor", "打开调色板选取颜色", "開啟調色盤選取顏色", "Open color picker to select color", "カラーパレットを開いて選択");
		Add("TipEyedropColor", "从屏幕任意位置吸取颜色", "從螢幕任意位置吸取顏色", "Pick color from anywhere on screen", "画面上の任意の位置から色を抽出");
		Add("Tab1_SectorBorderLabel", "扇区边框:", "扇區邊框:", "Sector Border:", "セクター境界線:");
		Add("Tab1_HighlightBgLabel", "高亮底色:", "高亮底色:", "Highlight Background:", "ハイライト背景色:");
		Add("Tab1_HighlightBorderLabel", "高亮边框:", "高亮邊框:", "Highlight Border:", "ハイライト境界線:");
		Add("Tab1_TextColorLabel", "文字颜色:", "文字顏色:", "Text Color:", "テキスト色:");
		Add("BtnSavePresetChanges", "💾 保存当前配色修改", "💾 儲存當前配色修改", "💾 Save Preset Changes", "💾 配色の変更を保存");
		Add("TipSavePresetChanges", "将当前调整的颜色直接保存到正在使用的配色预设中", "將當前調整的顏色直接儲存到正在使用的配色預設中", "Save current adjusted colors directly to the active preset", "現在調整した色を使用中のプリセットに直接保存");
		Add("BtnSaveAsNewPreset", "➕ 另存为新预设...", "➕ 另存為新預設...", "➕ Save as New Preset...", "➕ 新規プリセットとして保存...");
		Add("TipSaveAsNewPreset", "将当前调整的颜色另存为一个全新的独立配色预设", "將當前調整的顏色另存為一個全新的獨立配色預設", "Save adjusted colors as a brand new independent preset", "調整した色を新しい独立したプリセットとして保存");
		Add("Tab1_HighlightGlowModeLabel", "高亮边缘光晕模式:", "高亮邊緣光暈模式:", "Highlight Edge Glow Mode:", "ハイライトエッジグローモード:");
		Add("GlowItemFollowHighlight", "🌈 跟随主题高亮色", "🌈 跟隨主題高亮色", "🌈 Follow Theme Highlight", "🌈 テーマのハイライトに従う");
		Add("GlowItemLilacPurple", "💜 丁香晶紫", "💜 丁香晶紫", "💜 Lilac Purple", "💜 ライラックパープル");
		Add("GlowItemGlacialBlue", "💙 冰川湛蓝", "💙 冰川湛藍", "💙 Glacial Blue", "💙 グレイシャルブルー");
		Add("GlowItemEmeraldGreen", "💚 翡翠荧绿", "💚 翡翠熒綠", "💚 Emerald Green", "💚 エメラルドグリーン");
		Add("GlowItemSakuraPink", "💖 樱花粉晕", "💖 櫻花粉暈", "💖 Sakura Pink", "💖 サクラピンク");
		Add("GlowItemAmberGold", "🧡 琥珀金光", "🧡 琥珀金光", "🧡 Amber Gold", "🧡 アンバーゴールド");
		Add("GlowItemCoralRed", "🔴 珊瑚赤光", "🔴 珊瑚赤光", "🔴 Coral Red", "🔴 コーラルレッド");
		Add("GlowItemIceWhite", "⚪ 冰魄纯白", "⚪ 冰魄純白", "⚪ Ice Pure White", "⚪ アイスピュアホワイト");
		Add("GlowItemCustom", "🎨 自定义光晕颜色", "🎨 自訂光暈顏色", "🎨 Custom Glow Color", "🎨 カスタムグロー色");
		Add("Tab1_GlowColorLabel", "光晕色值:", "光暈色值:", "Glow Color Value:", "グローカラー値:");
		Add("TipPickGlowColor", "打开调色板选取光晕颜色", "開啟調色盤選取光暈顏色", "Open color picker to select glow color", "カラーパレットを開いてグロー色を選択");
		Add("TipEyedropGlowColor", "从屏幕任意位置吸取光晕颜色", "從螢幕任意位置吸取光暈顏色", "Pick glow color from anywhere on screen", "画面上の任意の位置からグロー色を抽出");
		Add("Tab1_GlowRadiusLabel", "光晕弥散半径:", "光暈彌散半徑:", "Glow Blur Radius:", "グローぼかし半径:");
		Add("Tab1_GlowOpacityLabel", "光晕不透明度:", "光暈不透明度:", "Glow Opacity:", "グロー不透明度:");
		Add("Tier2ThemeExpanderHeader", "🌐 二级轮盘风格与配色 (展开定制)", "🌐 二級輪盤風格與配色 (展開自訂)", "🌐 Tier-2 Wheel Style & Colors (Expand to Customize)", "🌐 第2階層ホイールスタイルと配色 (展開して設定)");
		Add("Tab1_SubThemeNotice", "🌟 当前正在单独定制二级级联轮盘专属视觉风格与色彩，支持与一级主轮盘自由组合！", "🌟 當前正在單獨自訂二級級聯輪盤專屬視覺風格與色彩，支援與一級主輪盤自由組合！", "🌟 Currently customizing visual style and colors for Tier-2 cascade wheel independently from Tier-1!", "🌟 現在、第1階層とは独立して第2階層カスケードホイールのビジュアルスタイルと配色を個別にカスタマイズ中！");
		Add("Tab1_SubUiStyleLabel", "二级轮盘视觉风格:", "二級輪盤視覺風格:", "Tier-2 Visual Style:", "第2階層ビジュアルスタイル:");
		Add("SubUiStyleItemFollowPrimary", "跟随一级主轮盘风格", "跟隨一級主輪盤風格", "Follow Tier-1 Wheel Style", "第1階層ホイールスタイルに従う");
		Add("Tab1_SubThemePresetLabel", "二级轮盘配色方案:", "二級輪盤配色方案:", "Tier-2 Color Scheme:", "第2階層カラースキーム:");
		Add("SubThemeItemFollowPrimary", "跟随一级主轮盘配色", "跟隨一級主輪盤配色", "Follow Tier-1 Color Scheme", "第1階層カラースキームに従う");
		Add("SubCustomColorsExpanderTitle", "🎨 二级轮盘高级配色", "🎨 二級輪盤高級配色", "🎨 Tier-2 Wheel Advanced Colors", "🎨 第2階層ホイール高度な配色");
		Add("SubCustomColorsExpanderDesc", "展开后可精准微调二级扇区底色、高亮光晕、边框线条、文字等各项色彩。", "展開後可精準微調二級扇區底色、高亮光暈、邊框線條、文字等各項色彩。", "Expand to fine-tune Tier-2 sector background, highlight glow, border lines, text, etc.", "展開して第2階層セクター背景、ハイライトグロー、境界線、テキストなどを微調整します。");
		Add("Tab1_SubCustomColorsSectionLabel", "自定义十六进制色彩 (色盘调色 / 屏幕吸色):", "自訂十六進位色彩 (色盤調色 / 螢幕吸色):", "Custom Hex Colors (Palette / Eyedropper):", "カスタム16進数カラー (パレット / スポイト):");
		Add("TipSaveSubPresetChanges", "将当前调整的颜色直接保存到正在使用的二级配色预设中", "將當前調整的顏色直接儲存到正在使用的二級配色預設中", "Save current adjusted colors directly to the active Tier-2 preset", "現在調整した色を使用中の第2階層プリセットに直接保存");
		Add("Tab1_SubHighlightGlowLabel", "二级轮盘高亮边缘光晕:", "二級輪盤高亮邊緣光暈:", "Tier-2 Highlight Edge Glow:", "第2階層ハイライトエッジグロー:");
		Add("SubGlowItemFollowPrimary", "🔘 跟随一级主轮盘光晕", "🔘 跟隨一級主輪盤光暈", "🔘 Follow Tier-1 Wheel Glow", "🔘 第1階層グローに従う");
		Add("SubGlowItemFollowHighlight", "🌈 跟随二级主题高亮色", "🌈 跟隨二級主題高亮色", "🌈 Follow Tier-2 Theme Highlight", "🌈 第2階層テーマのハイライトに従う");
		Add("SubGlowItemNone", "🚫 关闭边缘光晕", "🚫 關閉邊緣光暈", "🚫 Disable Edge Glow", "🚫 エッジグローを無効化");
		Add("BtnResetSubTheme", "🔄 恢复与一级轮盘相同主题", "🔄 恢復與一級輪盤相同主題", "🔄 Reset to Same Theme as Tier-1", "🔄 第1階層と同じテーマにリセット");
		Add("Tab1_SectorCutStyleLabel", "扇区切削形态:", "扇區切削形態:", "Sector Cut Shape:", "セクター切削形状:");
		Add("CutStyleItemClassic", "经典紧凑扇区", "經典緊湊扇區", "Classic Compact Sectors", "クラシックコンパクトセクター");
		Add("CutStyleItemCircles", "独立圆形卡片", "獨立圓形卡片", "Detached Circular Cards", "独立した円形カード");
		Add("CutStyleItemCapsules", "悬浮圆角胶囊", "懸浮圓角膠囊", "Floating Rounded Capsules", "フローティング角丸カプセル");
		Add("CutStyleItemHexagons", "蜂巢六边形矩阵", "蜂巢六邊形矩陣", "Honeycomb Hexagon Grid", "ハニカム六角形グリッド");
		Add("Tab1_SectorGapLabel", "扇区缝隙间距:", "扇區縫隙間距:", "Sector Gap Spacing:", "セクター間の隙間:");
		Add("Tab1_SectorCornerRadiusLabel", "扇区边缘平滑倒角:", "扇區邊緣平滑倒角:", "Sector Corner Radius:", "セクター角丸半径:");
		Add("Tab1_WheelRadiusLabel", "轮盘整体半径:", "輪盤整體半徑:", "Wheel Outer Radius:", "ホイール全体半径:");
		Add("Tab1_InnerRadiusLabel", "扇区内半径:", "扇區內半徑:", "Sector Inner Radius:", "セクター内半径:");
		Add("Tab1_CoreRadiusLabel", "中心核心圆半径:", "中心核心圓半徑:", "Center Core Radius:", "センターコア半径:");
		Add("Tier2DimensionsExpanderHeader", "🌐 二级轮盘几何形态与尺寸 (展开微调)", "🌐 二級輪盤幾何形態與尺寸 (展開微調)", "🌐 Tier-2 Wheel Geometry & Dimensions (Expand to Fine-Tune)", "🌐 第2階層ホイール幾何形状と寸法 (展開して微調整)");
		Add("Tab1_SubDimensionsNotice", "🌟 当前正在单独调节二级级联轮盘专属尺寸，与一级轮盘完全独立互不影响。", "🌟 當前正在單獨調節二級級聯輪盤專屬尺寸，與一級輪盤完全獨立互不影響。", "🌟 Currently adjusting Tier-2 cascade wheel dimensions independently from Tier-1.", "🌟 現在、第1階層とは独立して第2階層カスケードホイールの寸法を個別に調整中。");
		Add("Tab1_SubOuterRadiusLabel", "二级轮盘整体外径:", "二級輪盤整體外徑:", "Tier-2 Outer Radius:", "第2階層ホイール外半径:");
		Add("Tab1_SubGapLabel", "二级与一级轮盘间距:", "二級與一級輪盤間距:", "Tier-2 to Tier-1 Gap:", "第2階層と第1階層のホイール間隔:");
		Add("Tab1_SubCornerRadiusLabel", "二级扇区边缘平滑倒角:", "二級扇區邊緣平滑倒角:", "Tier-2 Sector Corner Radius:", "第2階層セクター角丸半径:");
		Add("Tab1_SubIconSizeLabel", "二级轮盘图标尺寸:", "二級輪盤圖示尺寸:", "Tier-2 Icon Size:", "第2階層アイコンサイズ:");
		Add("Tab1_SubFontSizeLabel", "二级轮盘字体字号:", "二級輪盤字體字號:", "Tier-2 Font Size:", "第2階層フォントサイズ:");
		Add("BtnResetSubDimensions", "🔄 恢复二级轮盘默认尺寸", "🔄 恢復二級輪盤預設尺寸", "🔄 Reset Tier-2 to Default Dimensions", "🔄 第2階層をデフォルト寸法にリセット");
		Add("LayoutOptionsSectionTitle", "图标与排版选项", "圖示與排版選項", "Icon & Layout Options", "アイコンとレイアウトのオプション");
		Add("LayoutModeItemBoth", "图标 + 文字 (双行居中)", "圖示 + 文字 (雙行居中)", "Icon + Text (Centered 2-line)", "アイコン + テキスト (中央揃え2行)");
		Add("LayoutModeItemIconOnly", "仅显示图标 (极大化居中)", "僅顯示圖示 (極大化居中)", "Icon Only (Maximized Center)", "アイコンのみ (最大化中央)");
		Add("LayoutModeItemTextOnly", "仅显示文字 (纯文字居中)", "僅顯示文字 (純文字居中)", "Text Only (Pure Text Center)", "テキストのみ (テキスト中央)");
		Add("WheelFontItemSystem", "🖥️ 系统默认", "🖥️ 系統預設", "🖥️ System Default", "🖥️ システムデフォルト");
		Add("WheelFontItemYaHei", "🔤 微软雅黑", "🔤 微軟雅黑", "🔤 Microsoft YaHei", "🔤 メイリオ / 微软雅黑");
		Add("WheelFontItemHarmony", "🔤 鸿蒙字体", "🔤 鴻蒙字體", "🔤 HarmonyOS Sans", "🔤 HarmonyOS フォント");
		Add("WheelFontItemPingFang", "🔤 苹方字体", "🔤 蘋方字體", "🔤 PingFang SC", "🔤 PingFang フォント");
		Add("WheelFontItemMiSans", "🔤 小米兰亭", "🔤 小米蘭亭", "🔤 MiSans", "🔤 MiSans フォント");
		Add("WheelFontItemSimHei", "🔤 黑体", "🔤 黑體", "🔤 SimHei", "🔤 ゴシック体");
		Add("WheelFontItemKaiTi", "🔤 楷体", "🔤 楷體", "🔤 KaiTi", "🔤 明朝体 / 楷書体");
		Add("WheelFontItemConsolas", "🔤 等宽代码体", "🔤 等寬程式碼體", "🔤 Monospace Code", "🔤 等幅コードフォント");
		Add("BtnResetTextOffset", "🔄 位置归位", "🔄 位置歸位", "🔄 Reset Position", "🔄 位置リセット");
		Add("TipResetTextOffset", "一键将文字相对位置与水平/垂直偏移恢复为默认", "一鍵將文字相對位置與水平/垂直偏移恢復為預設", "Reset text relative position and horizontal/vertical offsets to default", "テキストの相対位置と水平/垂直オフセットをデフォルトにリセット");
		Add("PlacementItemBottom", "⬇️ 图标下方 (默认)", "⬇️ 圖示下方 (預設)", "⬇️ Below Icon (Default)", "⬇️ アイコンの下 (デフォルト)");
		Add("PlacementItemTop", "⬆️ 图标上方", "⬆️ 圖示上方", "⬆️ Above Icon", "⬆️ アイコンの上");
		Add("Tab1_TextOffsetXLabel", "水平 X:", "水平 X:", "Horizontal X:", "水平 X:");
		Add("Tab1_TextOffsetYLabel", "垂直 Y:", "垂直 Y:", "Vertical Y:", "垂直 Y:");
		Add("CoreSectionTitle", "中心核心圆与图案文字设置", "中心核心圓與圖案文字設定", "Center Core Circle & Pattern/Text Settings", "センターコア＆パターン・テキスト設定");
		Add("ShowCoreIconTitle", "启用中心图案/图标显示", "啟用中心圖案/圖示顯示", "Enable Center Pattern/Icon Display", "センターパターン/アイコン表示を有効化");
		Add("Tab1_CorePatternTypeLabel", "图案类型:", "圖案類型:", "Pattern Type:", "パターンタイプ:");
		Add("CoreIconTypeItemCrosshair", "精准十字准星", "精準十字準星", "Precision Crosshair", "高精度クロスヘア");
		Add("CoreIconTypeItemWindows", "Windows 徽标", "Windows 徽標", "Windows Logo", "Windows ロゴ");
		Add("CoreIconTypeItemBreatheDot", "中心呼吸光点", "中心呼吸光點", "Breathing Glow Dot", "センターブリージングライト");
		Add("CoreIconTypeItemHomeReturn", "主页与返回", "首頁與返回", "Home & Back", "ホーム＆戻る");
		Add("CoreIconTypeItemCompassStar", "八向罗盘星芒", "八向羅盤星芒", "8-Point Compass Star", "8方向コンパススター");
		Add("CoreIconTypeItemCatPaw", "猫爪肉垫图案", "貓爪肉墊圖案", "Cat Paw Pad", "猫の肉球パターン");
		Add("CoreIconTypeItemVector", "矢量图标库选择", "向量圖示庫選擇", "Vector Icon Library", "ベクターアイコンライブラリ選択");
		Add("CoreIconTypeItemCustomImage", "本地自定义图片", "本地自訂圖片", "Local Custom Image", "ローカルカスタム画像");
		Add("CustomCoreIconNone", "未选择图标", "未選擇圖示", "No Icon Selected", "アイコン未選択");
		Add("BtnPickCoreIcon", "选择图标...", "選擇圖示...", "Select Icon...", "アイコンを選択...");
		Add("TipCoreImagePath", "自定义图片本地路径", "自訂圖片本地路徑", "Local path to custom image", "カスタム画像のローカルパス");
		Add("BtnBrowseCoreImage", "浏览图片...", "瀏覽圖片...", "Browse Image...", "画像を参照...");
		Add("BtnClearCoreImage", "清除", "清除", "Clear", "クリア");
		Add("CoreTextOptionsSectionTitle", "中心文字与选中显示定制", "中心文字與選中顯示自訂", "Center Text & Selection Display Customization", "センターテキスト＆選択表示カスタマイズ");
		Add("CoreTextColorTitle", "中心文字颜色:", "中心文字顏色:", "Center Text Color:", "センターテキスト色:");
		Add("LayerStyleItemDark", "🌌 沉浸深邃暗黑 (推荐)", "🌌 沉浸深邃暗黑 (推薦)", "🌌 Immersive Deep Dark (Recommended)", "🌌 ディープダーク（推奨）");
		Add("LayerStyleItemAuroraBlue", "🧊 晶莹极光蓝透", "🧊 晶瑩極光藍透", "🧊 Aurora Translucent Blue", "🧊 オーロラクリスタルブルー");
		Add("LayerStyleItemObsidianPurple", "🔮 钛金晶透曜紫", "🔮 鈦金晶透曜紫", "🔮 Titanium Crystal Purple", "🔮 チタンクリスタルパープル");
		Add("LayerStyleItemLight", "⚪ 极简透白浅色", "⚪ 極簡透白淺色", "⚪ Minimalist Translucent Light", "⚪ ミニマルクリアライト");
		Add("LayerStyleItemFollowTheme", "🔘 跟随当前轮盘主题", "🔘 跟隨當前輪盤主題", "🔘 Follow Current Wheel Theme", "🔘 現在のホイールテーマに従う");
		Add("LayerStyleItemCustom", "🎨 完全自定义色彩", "🎨 完全自訂色彩", "🎨 Fully Custom Colors", "🎨 完全カスタムカラー");
		Add("LayerIconItemStar", "🌟 璀璨星芒 (默认)", "🌟 璀璨星芒 (預設)", "🌟 Radiant Star (Default)", "🌟 輝く星（デフォルト）");
		Add("LayerIconItemSnowflake", "❄️ 冰晶雪花", "❄️ 冰晶雪花", "❄️ Crystal Snowflake", "❄️ クリスタルスノー");
		Add("LayerIconItemGalaxy", "🌀 宇宙星盘", "🌀 宇宙星盤", "🌀 Cosmic Galaxy", "🌀 コズミックスター");
		Add("LayerIconItemBolt", "⚡ 极速闪电", "⚡ 極速閃電", "⚡ Lightning Bolt", "⚡ スピードライトニング");
		Add("LayerIconItemCrosshair", "🎯 准星靶心", "🎯 準星靶心", "🎯 Crosshair Bullseye", "🎯 ターゲットブルズアイ");
		Add("LayerIconItemGem", "💎 纯净宝石", "💎 純淨寶石", "💎 Pristine Gem", "💎 ピュアジェム");
		Add("LayerIconItemNone", "🚫 无前置图标", "🚫 無前置圖示", "🚫 No Leading Icon", "🚫 前置アイコンなし");
		Add("Tab1_LayerCustomColorsSectionLabel", "🎨 徽标色彩微调:", "🎨 徽標色彩微調:", "🎨 Badge Color Fine-Tuning:", "🎨 バッジカラー微調整:");
		Add("Tab1_LayerBgLabel", "徽标底色:", "徽標底色:", "Badge Background:", "バッジ背景色:");
		Add("Tab1_LayerBorderLabel", "边框颜色:", "邊框顏色:", "Border Color:", "境界線色:");
		Add("Tab1_LayerTextLabel", "文字色彩:", "文字色彩:", "Text Color:", "テキスト色:");
		Add("Tab1_LivePreviewTitle", "实时交互画布", "即時互動畫布", "Live Interactive Canvas", "リアルタイムプレビューキャンバス");
		Add("Tab1_LivePreviewBadge", "60FPS 同步渲染", "60FPS 同步渲染", "60FPS Synchronized Rendering", "60FPS 同期レンダリング");
		Add("Tab1_LivePreviewHint", "💡 移动鼠标至下方轮盘可实时测试高亮与磁吸手感", "💡 移動滑鼠至下方輪盤可即時測試高亮與磁吸手感", "💡 Hover mouse over wheel below to test highlight and snapping feel", "💡 下のホイールにマウスを合わせると、ハイライトと吸着の感触をテストできます");
		Add("TipPreviewZoomOut", "缩小视图 (或使用鼠标滚轮)", "縮小檢視 (或使用滑鼠滾輪)", "Zoom Out (or use mouse wheel)", "縮小 (またはマウスホイールを使用)");
		Add("TipPreviewZoomReset", "点击复位为 100%", "點擊重設為 100%", "Click to reset to 100%", "クリックして100%にリセット");
		Add("TipPreviewZoomIn", "放大视图 (或使用鼠标滚轮)", "放大檢視 (或使用滑鼠滾輪)", "Zoom In (or use mouse wheel)", "拡大 (またはマウスホイールを使用)");
		Add("TipPreviewResetView", "重置视图位置与缩放 (双击画布空白处也可复位)", "重設檢視位置與縮放 (按兩下畫布空白處也可重設)", "Reset view position and zoom (or double-click empty canvas)", "表示位置とズームをリセット (キャンバスの空白部分をダブルクリックでもリセット)");
		Add("BtnResetAllGeometry", "一键重置为推荐几何尺寸", "一鍵重設為推薦幾何尺寸", "One-Click Reset to Recommended Dimensions", "推奨寸法にワンクリックでリセット");
		Add("BtnResetSlotLayoutBatch", "🔄 批量恢复继承全局", "🔄 批次恢復繼承全域", "🔄 Batch Reset to Inherit Global", "🔄 一括で全体継承にリセット");
		Add("Tier1MainWheel", "一级主轮盘", "一級主輪盤", "Tier-1 Wheel", "第1階層メインホイール");
		Add("Tier2SubWheel", "二级级联轮盘", "二級級聯輪盤", "Tier-2 Cascade Wheel", "第2階層カスケードホイール");
		Add("ActionNotConfigured", "未设置动作", "未設定動作", "Action Not Configured", "アクション未設定");
		Add("CustomizingSlotFormat", "📍 正在定制: {0} - 扇区 {1} [{2}]: {3}", "📍 正在自訂: {0} - 扇區 {1} [{2}]: {3}", "📍 Customizing: {0} - Sector {1} [{2}]: {3}", "📍 カスタマイズ中: {0} - セクター {1} [{2}]: {3}");
		Add("CustomizingSubSlotFormat", "📍 正在定制: {0} [{1}] -> 子项 {2}: {3}", "📍 正在自訂: {0} [{1}] -> 子項 {2}: {3}", "📍 Customizing: {0} [{1}] -> Sub-item {2}: {3}", "📍 カスタマイズ中: {0} [{1}] -> サブ項目 {2}: {3}");
		Add("CustomizingBatchFormat", "🎯 批量修改模式 (已多选 {0} 个扇区: {1})", "🎯 批次修改模式 (已多選 {0} 個扇區: {1})", "🎯 Batch Edit Mode ({0} sectors selected: {1})", "🎯 一括編集モード ({0} 個のセクターを選択: {1})");
		Add("PreviewLayerFormat", "第 {0} 层 ({0}/{1})", "第 {0} 層 ({0}/{1})", "Layer {0} ({0}/{1})", "レイヤー {0} ({0}/{1})");
		Add("BatchLayoutHint", "💡 按住 Ctrl 点击可继续增减选择；下方选项将统一批量应用至全部选中扇区", "💡 按住 Ctrl 點擊可繼續增減選擇；下方選項將統一批次套用至全部選中扇區", "💡 Hold Ctrl and click to add/remove selection; options below will be batch applied to all selected sectors", "💡 Ctrlを押しながらクリックして選択を追加/削除。下のオプションは選択したすべてのセクターに一括適用されます");
		Add("LayerLabel", "🌀 轮盘层:", "🌀 輪盤層:", "🌀 Wheel Layer:", "🌀 ホイールレイヤー:");
		Add("AddLayerBtnText", "➕ 加层", "➕ 加層", "➕ Add Layer", "➕ レイヤー追加");
		Add("AddLayerBtnToolTip", "新增一层独立轮盘配置（支持无限多层）", "新增一層獨立輪盤設定（支援無限多層）", "Add a new independent wheel layer (unlimited layers supported)", "新しい独立したホイールレイヤーを追加（無制限）");
		Add("CopyLayerBtnText", "📑 复制", "📑 複製", "📑 Copy", "📑 複製");
		Add("CopyLayerBtnToolTip", "复制当前层的所有扇区动作与中心核圆到新层", "複製目前層的所有扇區動作與中心核圓至新層", "Copy all sector actions and center core of current layer to a new layer", "現在のレイヤーの全セクターアクションと中心コアを新規レイヤーに複製");
		Add("RenameLayerBtnToolTip", "重命名当前轮盘层", "重新命名目前輪盤層", "Rename current wheel layer", "現在のホイールレイヤーの名前を変更");
		Add("DeleteLayerBtnToolTip", "删除当前轮盘层（至少保留一层）", "刪除目前輪盤層（至少保留一層）", "Delete current wheel layer (at least one layer must be kept)", "現在のホイールレイヤーを削除（最低1レイヤー保持）");
		Add("LayerSwitchTriggerLabel", "🔄 切换:", "🔄 切換:", "🔄 Switch:", "🔄 切替:");
		Add("LayerSwitchTriggerComboBoxToolTip", "多层轮盘切换方式：支持鼠标滚轮上下滑动切换或 Tab 键循环切换", "多層輪盤切換方式：支援滑鼠滾輪上下滾動切換或 Tab 鍵循環切換", "Multi-layer wheel switching method: switch via mouse wheel scroll or Tab key cycle", "マルチレイヤー切替方式：マウスホイールの上下スクロールまたはTabキー巡回切替");
		Add("LayerSwitchModeScroll", "🖱️ 滚轮切换", "🖱️ 滾輪切換", "🖱️ Wheel Scroll", "🖱️ マウスホイール");
		Add("LayerSwitchModeTab", "⌨️ Tab 键切换", "⌨️ Tab 鍵切換", "⌨️ Tab Key", "⌨️ Tabキー");
		Add("GesturesPageSubheader", "支持针对不同前台应用程序设置专属的多向手势轮盘、按键动作、中心核圆与级联子动作。", "支援針對不同前景應用程式設定專屬的多向手勢輪盤、按鍵動作、中心核圓與級聯子動作。", "Configure dedicated radial gesture wheels, hotkeys, center core, and cascaded sub-actions for different foreground applications.", "前面の各アプリケーションに応じた専用の多方向ジェスチャーホイール、ショートカット、中心コア、カスケードサブアクションを設定できます。");
		Add("MappingsViewModeCanvasText", "🎯 画布联动精调 (推荐)", "🎯 畫布聯動精調 (推薦)", "🎯 Interactive Canvas (Recommended)", "🎯 インタラクティブキャンバス（推奨）");
		Add("MappingsViewModeListText", "📋 紧凑全览列表", "📋 緊湊全覽清單", "📋 Compact Overview List", "📋 コンパクト一覧リスト");
		Add("CurrentProfileLabel", "当前配置方案:", "目前設定方案:", "Current Profile:", "現在のプロファイル:");
		Add("AddProfileBtnText", "➕ 新增", "➕ 新增", "➕ Add", "➕ 追加");
		Add("AddProfileBtnToolTip", "添加新的轮盘配置方案（点击可选择从程序添加、捕捉窗口或自定义命名）", "新增輪盤設定方案（點擊可選擇從程式新增、捕捉視窗或自訂命名）", "Add a new wheel profile (choose from installed app, window capture, or custom name)", "新しいプロファイルを追加（インストール済みアプリ、ウィンドウキャプチャ、またはカスタム名から選択）");
		Add("AddProfileFromProgram", "🖥️ 从已安装软件中添加 (专属程序配置)...", "🖥️ 從已安裝軟體中新增 (專屬程式設定)...", "🖥️ Add from Installed Programs (App Profile)...", "🖥️ インストール済みソフトから追加（専用プロファイル）...");
		Add("AddProfileCaptureWindow", "🎯 捕捉运行中窗口添加 (专属程序配置)...", "🎯 捕捉執行中視窗新增 (專屬程式設定)...", "🎯 Capture Running Window (App Profile)...", "🎯 実行中ウィンドウからキャプチャ（専用プロファイル）...");
		Add("AddProfileBrowseExe", "📁 浏览本地程序文件添加 (.exe / .lnk)...", "📁 瀏覽本機程式檔案新增 (.exe / .lnk)...", "📁 Browse Local Executable File (.exe / .lnk)...", "📁 ローカル実行ファイルを参照して追加 (.exe / .lnk)...");
		Add("AddProfileCustom", "✏️ 新建自定义名称方案 (工作流/模式配置)...", "✏️ 新建自訂名稱方案 (工作流程/模式設定)...", "✏️ Create Custom Named Profile (Workflow/Mode)...", "✏️ カスタム名プロファイルを新規作成（ワークフロー/モード）...");
		Add("RenameProfileBtnText", "✏️ 重命名", "✏️ 重新命名", "✏️ Rename", "✏️ 名前変更");
		Add("RenameProfileBtnToolTip", "重命名选中的配置方案", "重新命名選取的設定方案", "Rename selected profile", "選択したプロファイルの名前を変更");
		Add("DeleteProfileBtnToolTip", "删除当前选中的配置方案", "刪除目前選取的設定方案", "Delete selected profile", "選択したプロファイルを削除");
		Add("GlobalProfileHint", "全局通用基础方案：当活动前台程序未配置专属轮盘时，手势将自动应用此全局方案。", "全域通用基礎方案：當使用中的前景程式未設定專屬輪盤時，手勢將自動套用此全域方案。", "Global default profile: when the active foreground app has no dedicated wheel, gestures will automatically use this global profile.", "グローバル基本プロファイル：アクティブな前面アプリに専用ホイールが設定されていない場合、自動的にこのグローバル設定が適用されます。");
		Add("ProfileBoundProcessesLabel", "🎯 绑定程序情景:", "🎯 綁定程式情境:", "🎯 Bound Processes:", "🎯 バインド対象プロセス:");
		Add("ProfileBoundProcessesToolTip", "目标程序进程名（如 photoshop.exe 或 code.exe）。支持以英文逗号分隔多个进程。", "目標程式處理程序名稱（如 photoshop.exe 或 code.exe）。支援以半形逗號分隔多個處理程序。", "Target process name (e.g. photoshop.exe or code.exe). Multiple processes separated by commas.", "対象プロセス名（例: photoshop.exe または code.exe）。カンマ区切りで複数指定可能。");
		Add("ProfileCaptureWindowBtnText", "🎯 捕捉窗口...", "🎯 捕捉視窗...", "🎯 Capture Window...", "🎯 ウィンドウ捕捉...");
		Add("ProfileCaptureWindowBtnToolTip", "直接点击桌面上运行中的目标软件窗口，自动识别并绑定其进程", "直接點擊桌面上執行中的目標軟體視窗，自動識別並綁定其處理程序", "Click any running window on desktop to automatically detect and bind its process", "デスクトップ上で実行中のウィンドウをクリックしてプロセスを自動識別・バインド");
		Add("ProfilePickProgramBtnText", "🖥️ 软件库...", "🖥️ 軟體庫...", "🖥️ App Library...", "🖥️ アプリ一覧...");
		Add("ProfilePickProgramBtnToolTip", "从已安装的软件列表中选择程序并绑定", "從已安裝的軟體清單中選擇程式並綁定", "Select an application from installed programs list to bind", "インストール済みアプリ一覧からプログラムを選択してバインド");
		Add("ProfileBrowseExeBtnText", "📁 浏览...", "📁 瀏覽...", "📁 Browse...", "📁 参照...");
		Add("ProfileBrowseExeBtnToolTip", "浏览选取本地可执行程序文件 (.exe / .lnk)", "瀏覽選取本機可執行程式檔案 (.exe / .lnk)", "Browse and select local executable file (.exe / .lnk)", "ローカルの実行可能ファイル (.exe / .lnk) を参照");
		Add("ProfileBoundProcessesHint", "💡 提示：在此程序处于前台活跃状态时唤起轮盘将自动应用本方案。支持以英文逗号分隔多个进程名 (例如 chrome.exe, msedge.exe)。", "💡 提示：在此程式處於前景使用中狀態時喚起輪盤將自動套用本方案。支援以半形逗號分隔多個處理程序名稱 (例如 chrome.exe, msedge.exe)。", "💡 Hint: When this program is active in foreground, invoking the wheel will automatically apply this profile. Multiple process names can be comma-separated (e.g. chrome.exe, msedge.exe).", "💡 ヒント：このプログラムが前面でアクティブな時にホイールを呼び出すと自動適用されます。カンマ区切りで複数のプロセス名を指定できます（例: chrome.exe, msedge.exe）。");
		Add("SectorCountLabel", "扇区方位数量:", "扇區方位數量:", "Sector Count:", "セクター数:");
		Add("SectorCount4Text", "4 键十字方位", "4 鍵十字方位", "4 Sectors (Cross)", "4方向（十字）");
		Add("SectorCount8Text", "8 键全向方位 (推荐)", "8 鍵全向方位 (推薦)", "8 Sectors (Omni, Recommended)", "8方向（全方位・推奨）");
		Add("SectorCount12Text", "12 键钟表方位", "12 鍵鐘錶方位", "12 Sectors (Clock)", "12方向（時計盤）");
		Add("EnableGlobalInheritanceText", "🌐 继承全局方案未配置槽位", "🌐 繼承全域方案未設定位置", "🌐 Inherit Unset Slots from Global", "🌐 未設定スロットをグローバルから継承");
		Add("EnableGlobalInheritanceToolTip", "当专属程序方案中的某个扇区未配置动作时，自动级联继承并执行全局方案对应方位的动作", "當專屬程式方案中的某個扇區未設定動作時，自動級聯繼承並執行全域方案對應方位的動作", "When a sector is not configured in an app profile, automatically inherit and execute the corresponding sector action from the global profile", "専用プロファイルで未設定のセクターがある場合、グローバル設定の同方向アクションを自動継承して実行します");
		Add("FocusSlotInheritedBadgeToolTip", "当前槽位在专属方案中未配置，已自动继承全局方案同向动作", "目前位置在專屬方案中未設定，已自動繼承全域方案同向動作", "Slot not configured in app profile; automatically inheriting action from global profile", "専用プロファイルで未設定のため、グローバル設定の同方向アクションを自動継承しています");
		Add("FocusSlotInheritedBadgeText", "🌐 全局继承", "🌐 全域繼承", "🌐 Inherited", "🌐 継承済み");
		Add("FocusBackToParentBtnText", "◀ 返回父级扇区", "◀ 返回父級扇區", "◀ Back to Parent Sector", "◀ 親セクターに戻る");
		Add("FocusPrevSlotBtnText", "◀ 上一槽", "◀ 上一槽", "◀ Prev Slot", "◀ 前のスロット");
		Add("FocusNextSlotBtnText", "下一槽 ▶", "下一槽 ▶", "Next Slot ▶", "次のスロット ▶");
		Add("FocusCenterCoreBtnText", "🎯 中心核圆", "🎯 中心核圓", "🎯 Center Core", "🎯 中心コア");
		Add("EnableCenterActionText", "启用中心核圆动作", "啟用中心核圓動作", "Enable Center Core Action", "中心コアアクションを有効化");
		Add("CenterDeadzoneReleaseHint", "死区松开触发 · 外甩脱离取消", "死區放開觸發 · 外甩脫離取消", "Release in Deadzone to Trigger · Fling Out to Cancel", "デッドゾーン解放でトリガー・外側フリックでキャンセル");
		Add("CenterPresetsToggleBtnText", "⚡ 常用预设 ▾", "⚡ 常用預設 ▾", "⚡ Common Presets ▾", "⚡ 定番プリセット ▾");
		Add("CenterInfoToggleBtnText", "ℹ️ 说明 ▾", "ℹ️ 說明 ▾", "ℹ️ Info ▾", "ℹ️ 説明 ▾");
		Add("CenterPatternPriorityNotice", "当前已启用自定义中心图案，轮盘中心将优先展示该图案；在中心死区内松开鼠标仍会照常触发本功能。", "目前已啟用自訂中心圖案，輪盤中心將優先展示該圖案；在中心死區內放開滑鼠仍會照常觸發本功能。", "Custom center pattern is active and prioritized in display; releasing in deadzone will still trigger this action.", "カスタム中心パターンが有効な場合そちらが優先表示されますが、中心デッドゾーン内でマウスを離せば通常通り機能が実行されます。");
		Add("CenterPresetFillLabel", "一键填入:", "一鍵填入:", "Quick Fill:", "ワンクリック入力:");
		Add("CenterPresetSettings", "⚙️ 控制台", "⚙️ 控制台", "⚙️ Settings Console", "⚙️ 設定画面");
		Add("CenterPresetDesktop", "🖥️ 显示桌面", "🖥️ 顯示桌面", "🖥️ Show Desktop", "🖥️ デスクトップ表示");
		Add("CenterPresetLock", "🔒 锁定屏幕", "🔒 鎖定螢幕", "🔒 Lock Screen", "🔒 画面ロック");
		Add("CenterPresetWebUrl", "🌐 常用网站", "🌐 常用網站", "🌐 Favorite Website", "🌐 お気に入りサイト");
		Add("CenterPresetExplorer", "📁 资源管理", "📁 檔案總管", "📁 File Explorer", "📁 エクスプローラー");
		Add("CenterFlingExplanation", "💡 外甩脱离机制说明：开启「外甩脱离取消」后，手势若在中心内径死区内释放光标，将直接触发在此配置的动作（如呼出控制台、启动工具或热键）；若需废弃/取消手势，直接向外快速甩出轮盘边缘即可。", "💡 外甩脫離機制說明：開啟「外甩脫離取消」後，手勢若在中心內徑死區內釋放游標，將直接觸發在此設定的動作（如呼出控制台、啟動工具或快速鍵）；若需廢棄/取消手勢，直接向外快速甩出輪盤邊緣即可。", "💡 Fling Cancellation Guide: When 'Fling Out to Cancel' is enabled, releasing the cursor inside the center deadzone triggers this action (e.g. open console, tool, hotkey); to cancel, simply fling the cursor outward past the wheel edge.", "💡 外側フリックキャンセル説明：「外側フリックキャンセル」有効時、中心デッドゾーン内でカーソルを離すと本機能（設定画面、ツール、ショートカットなど）が実行されます。ジェスチャーを中止したい場合は外側へ素早くフリックします。");
		Add("FocusTier2EmptyTitle", "🌟 当前主扇区尚未配置二级级联子动作", "🌟 目前主扇區尚未設定二級級聯子動作", "🌟 No Tier-2 Sub-Actions Configured for this Sector", "🌟 このセクターには第2階層サブアクションが設定されていません");
		Add("FocusTier2EmptySubtitle", "向外划动此扇区时可展开二级子菜单。支持添加 1~4 个二级子动作。", "向外劃動此扇區時可展開二級子選單。支援新增 1~4 個二級子動作。", "Swipe outward from this sector to expand the sub-menu. Supports 1 to 4 sub-actions.", "外側にスワイプすると第2階層サブメニューが展開します。1〜4個のサブアクションを追加可能。");
		Add("FocusAddFirstSubActionText", "➕ 添加第 1 个二级子动作", "➕ 新增第 1 個二級子動作", "➕ Add 1st Sub-Action", "➕ 最初のサブアクションを追加");
		Add("FocusPickIconButtonToolTip", "点击选取矢量图标或自定SVG", "點擊選取向量圖示或自訂SVG", "Click to select vector icon or custom SVG", "クリックしてベクターアイコンまたはカスタムSVGを選択");
		Add("FocusIconLabel", "图标...", "圖示...", "Icon...", "アイコン...");
		Add("FocusActionNameLabel", "轮盘显示文本:", "輪盤顯示文字:", "Wheel Label:", "ホイール表示テキスト:");
		Add("FocusActionTypeLabel", "触发动作类型:", "觸發動作類型:", "Action Type:", "トリガー動作タイプ:");
		Add("FocusRestoreInheritBtnText", "🌐 恢复继承全局", "🌐 恢復繼承全域", "🌐 Restore Global", "🌐 グローバル継承に戻す");
		Add("FocusRestoreInheritBtnToolTip", "清除当前槽位的专属覆写，恢复继承全局方案对应方位的动作", "清除目前位置的專屬覆寫，恢復繼承全域方案對應方位的動作", "Clear local override for this slot and restore inheritance from global profile", "このスロットの個別上書きを解除し、グローバル設定の継承に戻します");
		Add("FocusTestActionBtnText", "▶ 测试触发", "▶ 測試觸發", "▶ Test Trigger", "▶ テスト実行");
		Add("TogglePauseHotkeysBtnText", "⏸️ 暂停全局热键", "⏸️ 暫停全域快速鍵", "⏸️ Pause Global Hotkeys", "⏸️ グローバルショートカットを一時停止");
		Add("TogglePauseHotkeysBtnToolTip", "暂停桌面系统及其他软件的所有全局快捷键，在此独占录入快捷键而不会触发系统（如 Win+D、Alt+Tab、截屏等）或其他软件", "暫停桌面系統及其他軟體的所有全域快速鍵，在此獨佔錄入快速鍵而不會觸發系統（如 Win+D、Alt+Tab、截圖等）或其他軟體", "Pause all global shortcuts in Windows and other apps to record combinations without triggering system hotkeys (Win+D, Alt+Tab, etc.)", "システムや他アプリのグローバルショートカットを一時停止し、誤爆せずに安全に入力記録します");
		Add("FocusHotkeyBuilderBtnText", "⚙️ 拼装组合", "⚙️ 拼裝組合", "⚙️ Hotkey Builder", "⚙️ 組み合わせビルダー");
		Add("FocusLaunchPathToolTip", "应用程序路径", "應用程式路徑", "Application executable path", "アプリケーション実行パス");
		Add("FocusLaunchPickProgramBtnText", "📦 软件库选择...", "📦 軟體庫選擇...", "📦 Select from Apps...", "📦 アプリ一覧から選択...");
		Add("FocusLaunchPickProgramBtnToolTip", "从已安装的软件、微软商店与开始菜单中模糊搜索选取", "從已安裝的軟體、微軟商店與開始功能表中模糊搜尋選取", "Search and select from installed apps, Microsoft Store, and Start Menu", "インストール済みアプリ、MSストア、スタートメニューから検索選択");
		Add("FocusLaunchCaptureWindowBtnText", "🎯 捕捉运行窗口...", "🎯 捕捉執行視窗...", "🎯 Capture Window...", "🎯 実行中ウィンドウを捕捉...");
		Add("FocusLaunchCaptureWindowBtnToolTip", "直接探测并捕捉桌面上正在运行的活跃窗口与程序执行路径", "直接探測並捕捉桌面上正在執行的使用中視窗與程式執行路徑", "Detect and capture running window and its executable path directly from desktop", "デスクトップ上で実行中のウィンドウと実行パスを検出して捕捉");
		Add("FocusLaunchBrowseExeBtnText", "📂 浏览...", "📂 瀏覽...", "📂 Browse...", "📂 参照...");
		Add("FocusLaunchBrowseExeBtnToolTip", "手动浏览可执行文件或快捷方式", "手動瀏覽可執行檔案或捷徑", "Browse executable file or shortcut manually", "実行ファイルやショートカットを手動で参照");
		Add("FocusLaunchArgsLabel", "启动参数:", "啟動參數:", "Arguments:", "引数:");
		Add("FocusLaunchArgsToolTip", "启动命令行参数 (可选)", "啟動命令列參數 (選填)", "Command line launch arguments (optional)", "コマンドライン引数（省略可能）");
		Add("FocusLaunchAsUserTitle", "🛡️ 以常规普通权限启动 (解决高权限下外部文件无法拖入目标软件的问题)", "🛡️ 以一般普通權限啟動 (解決高權限下外部檔案無法拖入目標軟體的問題)", "🛡️ Launch with Standard User Privileges (Fixes drag-and-drop file restrictions under elevated admin)", "🛡️ 標準ユーザー権限で起動（管理者権限下でのファイルドラッグ＆ドロップ制限を解決）");
		Add("FocusLaunchAsUserSubtitle", "当 StarPie 以管理员权限运行时，通过 Windows Shell 降权启动目标程序，恢复文件拖拽交互支持。", "當 StarPie 以系統管理員權限執行時，透過 Windows Shell 降權啟動目標程式，恢復檔案拖曳互動支援。", "When StarPie runs as administrator, launches target app via Windows Shell de-elevation to restore drag-and-drop functionality.", "StarPieが管理者権限で動作している際、Windows Shell経由で通常権限起動しドラッグ＆ドロップ操作を復元します。");
		Add("FocusWebUrlToolTip", "目标网址，如 https://github.com", "目標網址，如 https://github.com", "Target URL, e.g. https://github.com", "対象URL（例: https://github.com）");
		Add("BrowserChoiceDefault", "🌐 系统默认", "🌐 系統預設", "🌐 System Default", "🌐 システム既定");
		Add("BrowserChoiceCustom", "自定义浏览器...", "自訂瀏覽器...", "Custom Browser...", "カスタムブラウザ...");
		Add("FocusCustomBrowserPathToolTip", "自定义浏览器可执行文件路径", "自訂瀏覽器可執行檔案路徑", "Custom browser executable path", "カスタムブラウザ実行パス");
		Add("FocusCustomBrowserBrowseBtnText", "📂 选择...", "📂 選擇...", "📂 Select...", "📂 選択...");
		Add("FocusWebPresetsLabel", "常用网址:", "常用網址:", "Favorite Sites:", "定番サイト:");
		Add("FocusWebPresetBingText", "Bing 搜索", "Bing 搜尋", "Bing Search", "Bing検索");
		Add("FocusFolderPathToolTip", "本地文件夹绝对路径", "本機資料夾絕對路徑", "Absolute local folder path", "ローカルフォルダの絶対パス");
		Add("FocusFolderBrowseBtnText", "📂 浏览...", "📂 瀏覽...", "📂 Browse...", "📂 参照...");
		Add("FocusFolderPresetsLabel", "常用目录:", "常用目錄:", "Common Folders:", "定番フォルダ:");
		Add("FocusFolderPresetThisPcText", "💻 此电脑", "💻 本機", "💻 This PC", "💻 PC");
		Add("FocusFolderPresetThisPcToolTip", "直接打开系统「此电脑」命名空间", "直接開啟系統「本機」命名空間", "Open This PC namespace", "「PC」を開く");
		Add("FocusFolderPresetRecycleBinText", "🗑️ 回收站", "🗑️ 資源回收筒", "🗑️ Recycle Bin", "🗑️ ごみ箱");
		Add("FocusFolderPresetRecycleBinToolTip", "直接打开系统「回收站」命名空间", "直接開啟系統「資源回收筒」命名空間", "Open Recycle Bin namespace", "「ごみ箱」を開く");
		Add("FocusFolderPresetDesktopText", "🖥️ 桌面", "🖥️ 桌面", "🖥️ Desktop", "🖥️ デスクトップ");
		Add("FocusFolderPresetDownloadsText", "📥 下载", "📥 下載", "📥 Downloads", "📥 ダウンロード");
		Add("FocusFolderPresetDocumentsText", "📄 文档", "📄 文件", "📄 Documents", "📄 ドキュメント");
		Add("FocusCommandToolTip", "要执行的命令行语句，如 ping -t 127.0.0.1", "要執行的命令列語句，如 ping -t 127.0.0.1", "Command line to execute, e.g. ping -t 127.0.0.1", "実行するコマンドライン（例: ping -t 127.0.0.1）");
		Add("FocusWindowSubModeLabel", "控制模式:", "控制模式:", "Control Mode:", "制御モード:");
		Add("WindowModeTile", "🔲 平铺窗口排布", "🔲 平鋪視窗排布", "🔲 Tile Windows", "🔲 ウィンドウ整列");
		Add("WindowModeCycle", "🔄 循环切换平铺", "🔄 循環切換平鋪", "🔄 Cycle Tile Layouts", "🔄 レイアウト巡回");
		Add("WindowModeCycleReverse", "⬅️ 反向循环平铺", "⬅️ 反向循環平鋪", "⬅️ Cycle Tile Reverse", "⬅️ 逆順レイアウト巡回");
		Add("WindowModeRestore", "⏪ 还原平铺快照", "⏪ 還原平鋪快照", "⏪ Restore Tile Snapshot", "⏪ 整列スナップショット復元");
		Add("WindowModeTopmost", "📌 窗口置顶 / 取消置顶", "📌 視窗最上層顯示 / 取消最上層", "📌 Toggle Window Always On Top", "📌 最前面表示 / 解除");
		Add("WindowModeMoveMonitor", "🖥️ 移到下一显示器", "🖥️ 移至下一台螢幕", "🖥️ Move to Next Monitor", "🖥️ 次のディスプレイへ移動");
		Add("WindowModeOpacity", "👁️ 窗口透明度调节", "👁️ 視窗透明度調節", "👁️ Adjust Window Opacity", "👁️ ウィンドウ不透明度調整");
		Add("WindowModeSwitch", "🗂️ 任务栏切换 (Win+N)", "🗂️ 工作列切換 (Win+N)", "🗂️ Switch Taskbar App (Win+N)", "🗂️ タスクバー切替 (Win+N)");
		Add("FocusPopulateTileSubActionsBtnText", "✨ 预设 8 布局二级轮盘", "✨ 預設 8 版面二級輪盤", "✨ Preset 8 Layouts Sub-Wheel", "✨ 8分割レイアウトをプリセット");
		Add("FocusPopulateTileSubActionsBtnToolTip", "自动在二级级联菜单中填充 8 种常用平铺布局", "自動在二級級聯選單中填入 8 種常用平鋪版面", "Automatically populate 8 common tiling layouts into the tier-2 sub-wheel", "第2階層サブホイールに8種類の定番ウィンドウ整列レイアウトを自動設定");
		Add("FocusTileCommonLayoutsLabel", "常用布局:", "常用版面:", "Common Layouts:", "定番レイアウト:");
		Add("FocusTilePreset2LText", "左右对半 (2L)", "左右對半 (2L)", "Split Left-Right (2L)", "左右2分割 (2L)");
		Add("FocusTilePreset2TText", "上下对半 (2T)", "上下對半 (2T)", "Split Top-Bottom (2T)", "上下2分割 (2T)");
		Add("FocusTilePreset3L12Text", "左大列 (3L12)", "左大欄 (3L12)", "Left Large Column (3L12)", "左主列 (3L12)");
		Add("FocusTilePreset4GText", "四宫格 (4G)", "四宮格 (4G)", "2x2 Grid (4G)", "4分割グリッド (4G)");
		Add("FocusTilePreset3RText", "三等分 (3R)", "三等分 (3R)", "Three Columns (3R)", "3等分 (3R)");
		Add("FocusTileCycleHint", "💡 触发手势时，自动在下方「平铺窗口设置」中勾选的排布列表中循环轮换下一个布局。", "💡 觸發手勢時，自動在下方「平鋪視窗設定」中勾選的版面清單中循環輪換下一個版面。", "💡 When triggered, automatically cycles to the next layout checked in 'Tiling Window Settings' below.", "💡 ジェスチャー実行時、下の「ウィンドウ整列設定」でチェックされたレイアウトを順次切り替えます。");
		Add("FocusTileRestoreHint", "⏪ 还原所有窗口到平铺前的初始大小与屏幕坐标位置。", "⏪ 還原所有視窗至平鋪前的初始大小與螢幕座標位置。", "⏪ Restores all windows to their size and screen positions before tiling.", "⏪ すべてのウィンドウを整列前の元のサイズと位置に復元します。");
		Add("FocusTileTopmostHint", "📌 将当前鼠标所在窗口或前台活动窗口固定置顶于最前（再次触发即可恢复）。", "📌 將目前滑鼠所在視窗或前景使用中視窗固定置頂於最前（再次觸發即可恢復）。", "📌 Pin window under cursor or active window always on top (trigger again to unpin).", "📌 カーソル位置またはアクティブなウィンドウを最前面に固定（再実行で解除）。");
		Add("FocusTileMoveMonitorHint", "🖥️ 将当前活动窗口移动至下一个物理显示器对应的工作区位置。", "🖥️ 將目前使用中視窗移動至下一台實體螢幕對應的工作區位置。", "🖥️ Move active window to corresponding workspace on the next physical monitor.", "🖥️ 現在のアクティブウィンドウを次のディスプレイの対応エリアに移動します。");
		Add("FocusTileOpacityLabel", "不透明度:", "不透明度:", "Opacity:", "不透明度:");
		Add("FocusTileOpacityPresetsLabel", "快捷预设:", "捷徑預設:", "Quick Presets:", "クイックプリセット:");
		Add("FocusOpacity70Text", "70% 极淡", "70% 極淡", "70% Faint", "70% 薄い");
		Add("FocusOpacity80Text", "80% 查阅", "80% 查閱", "80% Glance", "80% 参照");
		Add("FocusOpacity90Text", "90% 透视", "90% 透視", "90% Translucent", "90% 半透明");
		Add("FocusOpacity100Text", "100% 不透明", "100% 不透明", "100% Opaque", "100% 不透明");
		Add("FocusSwitchWindowIndexLabel", "任务栏序号 (1~20):", "工作列編號 (1~20):", "Taskbar Slot Index (1~20):", "タスクバー位置番号 (1〜20):");
		Add("FocusSwitchWindowIndexHint", "等同快捷键 Win + 序号", "等同快速鍵 Win + 編號", "Equivalent to Win + Number shortcut", "ショートカット Win + 数字 に相当");
		Add("FocusSwitchWindowQuickSelectLabel", "快速选择:", "快速選擇:", "Quick Select:", "クイック選択:");
		Add("FocusSwitchSlot1Text", "#1 槽位", "#1 位置", "#1 Slot", "#1 スロット");
		Add("FocusSwitchSlot2Text", "#2 槽位", "#2 位置", "#2 Slot", "#2 スロット");
		Add("FocusSwitchSlot3Text", "#3 槽位", "#3 位置", "#3 Slot", "#3 スロット");
		Add("FocusSwitchSlot4Text", "#4 槽位", "#4 位置", "#4 Slot", "#4 スロット");
		Add("FocusOcrDefaultStatus", "默认调用 Windows 本地原生 OCR 离线引擎 (0延迟 · 隐私安全)", "預設呼叫 Windows 本機原生 OCR 離線引擎 (0延遲 · 隱私安全)", "Uses Windows Native offline OCR engine by default (Zero latency · Privacy safe)", "Windowsローカル標準OCRオフラインエンジンを既定で使用（低遅延・高セキュリティ）");
		Add("FocusOcrStatusFmt", "当前识别引擎: {0} · 点击右侧测试或更换接口", "當前識別引擎: {0} · 點擊右側測試或更換介面", "Active Engine: {0} · Click right to test or reconfigure", "現在の認識エンジン: {0} · 右側をクリックしてテストまたは設定");
		Add("FocusOcrTestScreenshotBtnText", "✂️ 立即测试截屏", "✂️ 立即測試截圖", "✂️ Test Snipping Now", "✂️ 今すぐキャプチャテスト");
		Add("FocusOcrTestScreenshotBtnToolTip", "立即启动全屏框选测试 OCR 识别效果", "立即啟動全螢幕框選測試 OCR 辨識效果", "Launch fullscreen region selection immediately to test OCR", "全画面範囲選択を起動してOCR認識効果をテスト");
		Add("FocusOcrConfigBtnText", "⚙️ 接口配置", "⚙️ 介面設定", "⚙️ OCR Settings", "⚙️ OCRエンジン設定");
		Add("FocusOcrConfigBtnToolTip", "配置 OCR 引擎（本地引擎 / AI 视觉大模型 / 自定义 HTTP）", "設定 OCR 引擎（本機引擎 / AI 視覺大模型 / 自訂 HTTP）", "Configure OCR engine (Windows Native / Vision AI / Custom HTTP)", "OCRエンジンの設定（ローカルエンジン / AI Vision / カスタムHTTP）");
		Add("FocusShellToolDefaultTitle", "未挑选功能 (点击右侧挑选)", "未挑選功能 (點擊右側挑選)", "No Tool Selected (Click right to choose)", "機能未選択（右側をクリックして選択）");
		Add("FocusShellToolDefaultDesc", "从系统原生增强与右键扩展中选择常用高频功能", "從系統原生增強與右鍵擴充中選擇常用高頻功能", "Choose common utilities from native Windows enhancements and context menu extensions", "Windows標準拡張機能や右クリックメニューから定番機能を選択");
		Add("FocusPickShellToolBtnText", "⚡ 挑选功能...", "⚡ 挑選功能...", "⚡ Pick Tool...", "⚡ 機能を選択...");
		Add("FocusPickShellToolBtnToolTip", "打开系统与右键工具库，支持搜索与分类", "開啟系統與右鍵工具庫，支援搜尋與分類", "Open system and context menu tools catalog with search and filters", "システムとコンテキストメニューのツール一覧を開く（検索・分類対応）");
		Add("FocusInheritIconLabel", "🏷️ 关联外部程序图标:", "🏷️ 關聯外部程式圖示:", "🏷️ Linked App Icon:", "🏷️ 外部アプリアイコン連携:");
		Add("FocusInheritIconUnlinked", "未关联 (显示默认动作图标)", "未關聯 (顯示預設動作圖示)", "Unlinked (shows default action icon)", "未連携（標準アクションアイコン表示）");
		Add("FocusInheritIconLinkedFormat", "已关联: {0}", "已關聯: {0}", "Linked: {0}", "連携中: {0}");
		Add("FocusClearInheritedIconBtnText", "✕ 清除关联", "✕ 清除關聯", "✕ Unlink", "✕ 連携解除");
		Add("FocusClearInheritedIconBtnToolTip", "清除关联的外部程序图标，恢复默认矢量图标", "清除關聯的外部程式圖示，恢復預設向量圖示", "Clear linked program icon and restore default vector icon", "関連付けられた外部アイコンを解除し、標準ベクターアイコンに戻す");
		Add("FocusInheritIconPathToolTip", "关联提取图标的外部程序或快捷方式路径", "關聯擷取圖示的外部程式或捷徑路徑", "Path of external application or shortcut to extract icon from", "アイコンを抽出する外部アプリまたはショートカットのパス");
		Add("FocusInheritIconPickProgramBtnText", "📦 软件库...", "📦 軟體庫...", "📦 App Library...", "📦 アプリ一覧...");
		Add("FocusInheritIconPickProgramBtnToolTip", "从已安装软件与微软商店应用中选取官方高清图标", "從已安裝軟體與微軟商店應用中選取官方高畫質圖示", "Select official high-res icon from installed software or Microsoft Store", "インストール済みアプリやMSストアから公式高解像度アイコンを選択");
		Add("FocusInheritIconCaptureWindowBtnText", "🎯 捕捉窗口...", "🎯 捕捉視窗...", "🎯 Capture Window...", "🎯 ウィンドウ捕捉...");
		Add("FocusInheritIconCaptureWindowBtnToolTip", "直接捕捉桌面上运行中的软件并继承其图标", "直接捕捉桌面上執行中的軟體並繼承其圖示", "Capture running window from desktop to inherit its icon directly", "デスクトップで実行中のアプリをキャプチャしてアイコンを継承");
		Add("FocusInheritIconBrowseBtnText", "📂 浏览...", "📂 瀏覽...", "📂 Browse...", "📂 参照...");
		Add("FocusInheritIconBrowseBtnToolTip", "手动浏览提取 .exe / .ico / .lnk 图标", "手動瀏覽擷取 .exe / .ico / .lnk 圖示", "Browse file system to extract icon from .exe / .ico / .lnk", ".exe / .ico / .lnk ファイルを手動参照してアイコンを抽出");
		Add("FocusSubActionsSectionLabel", "二级级联子动作:", "二級級聯子動作:", "Tier-2 Sub-Actions:", "第2階層サブアクション:");
		Add("FocusSubActionsCountFormat", "({0} 项)", "({0} 項)", "({0} items)", "（{0}件）");
		Add("FocusAddSubActionBtnText", "➕ 添加二级动作", "➕ 新增二級動作", "➕ Add Sub-Action", "➕ サブアクション追加");
		Add("FocusClearSubActionsBtnText", "🗑️ 清空", "🗑️ 清空", "🗑️ Clear", "🗑️ クリア");
		Add("FocusUndoSubActionsBtnText", "↩️ 撤销", "↩️ 復原", "↩️ Undo", "↩️ 元に戻す");
		Add("FocusUndoSubActionsBtnToolTip", "撤销上一次的修改或清空，恢复二级动作列表", "復原上一次的修改或清空，恢復二級動作清單", "Undo the last modification or clear, restoring sub-action list", "直前の変更またはクリアを元に戻し、サブアクションリストを復元");
		Add("FocusBatchBadgeText", "多选", "多選", "Multi", "複数選択");
		Add("FocusBatchTitleText", "批量修改模式", "批次修改模式", "Batch Edit Mode", "一括編集モード");
		Add("FocusBatchTagFormat", "已多选 {0} 个扇区", "已多選 {0} 個扇區", "{0} sectors selected", "{0}個のセクターを選択中");
		Add("FocusBatchSubtitleText", "在右侧画布中按住 Ctrl 点击可增减多选扇区；在此统一调整排版属性", "在右側畫布中按住 Ctrl 點擊可增減多選扇區；在此統一調整排版屬性", "Ctrl+Click sectors on the canvas to multi-select; adjust layout properties together here", "右側のキャンバスでCtrlキーを押しながらクリックして複数選択；レイアウトプロパティを一括調整");
		Add("FocusBatchExitBtnText", "✕ 退出多选", "✕ 結束多選", "✕ Exit Multi-Select", "✕ 複数選択を終了");
		Add("BatchLayoutModeLabel", "批量切换排版模式:", "批次切換排版模式:", "Batch Layout Mode:", "一括レイアウトモード:");
		Add("BatchLayoutBothBtnText", "🖼️+🔤 图文", "🖼️+🔤 圖文", "🖼️+🔤 Both", "🖼️+🔤 画像＋文字");
		Add("BatchLayoutBothBtnToolTip", "将所有选中扇区批量设为图文并茂居中", "將所有選取扇區批次設為圖文並茂置中", "Set all selected sectors to show both icon and text centered", "選択した全セクターをアイコンと文字の両方表示に設定");
		Add("BatchLayoutIconOnlyBtnText", "🖼️ 仅图标", "🖼️ 僅圖示", "🖼️ Icon Only", "🖼️ アイコンのみ");
		Add("BatchLayoutIconOnlyBtnToolTip", "将所有选中扇区批量设为仅显示图标", "將所有選取扇區批次設為僅顯示圖示", "Set all selected sectors to show icon only", "選択した全セクターをアイコンのみ表示に設定");
		Add("BatchLayoutTextOnlyBtnText", "🔤 仅文字", "🔤 僅文字", "🔤 Text Only", "🔤 文字のみ");
		Add("BatchLayoutTextOnlyBtnToolTip", "将所有选中扇区批量设为仅显示文字", "將所有選取扇區批次設為僅顯示文字", "Set all selected sectors to show text only", "選択した全セクターを文字のみ表示に設定");
		Add("BatchLayoutInheritBtnText", "🌐 继承全局", "🌐 繼承全域", "🌐 Inherit Global", "🌐 グローバル継承");
		Add("BatchLayoutInheritBtnToolTip", "将所有选中扇区排版模式批量恢复继承全局", "將所有選取扇區排版模式批次恢復繼承全域", "Reset layout mode of all selected sectors to inherit global", "選択した全セクターのレイアウトをグローバル設定の継承にリセット");
		Add("BatchFontSizeLabel", "文字字号大小:", "文字字型大小:", "Font Size:", "フォントサイズ:");
		Add("BatchIconSizeLabel", "图标尺寸大小:", "圖示尺寸大小:", "Icon Size:", "アイコンサイズ:");
		Add("BatchTextColorLabel", "批量文字颜色:", "批次文字顏色:", "Text Color:", "テキストカラー:");
		Add("BatchTextColorPaletteToolTip", "打开调色板选取颜色", "開啟調色盤選取顏色", "Open color palette", "カラーパレットを開く");
		Add("BatchTextColorEyedropperToolTip", "从屏幕任意位置吸取颜色", "從螢幕任意位置吸取顏色", "Pick color from anywhere on screen", "画面上の任意の位置から色を抽出");
		Add("BatchOffsetXLabel", "水平 X 偏移:", "水平 X 偏移:", "Horizontal X Offset:", "水平Xオフセット:");
		Add("BatchOffsetYLabel", "垂直 Y 偏移:", "垂直 Y 偏移:", "Vertical Y Offset:", "垂直Yオフセット:");
		Add("BatchResetCustomHint", "💡 清除所有选中槽位的独立定制，恢复跟随全局统一外观", "💡 清除所有選取位置的獨立自訂，恢復跟隨全域統一外觀", "💡 Clear custom styling for all selected slots and restore uniform global appearance", "💡 選択したすべてのスロットの個別カスタマイズを解除し、グローバルの統一デザインに戻します");
		Add("BatchResetCustomBtnText", "🔄 清除自定义，恢复跟随全局统一", "🔄 清除自訂，恢復跟隨全域統一", "🔄 Reset Custom Styling, Follow Global", "🔄 個別設定を解除しグローバルに統一");
		Add("Tab2GridSplitterToolTip", "拖拽调整画布与配置区比例，双击恢复默认比例", "拖曳調整畫布與設定區比例，按兩下恢復預設比例", "Drag to adjust canvas and settings ratio, double-click to reset", "ドラッグでキャンバスと設定エリアの比率を調整、ダブルクリックでリセット");
		Add("LiveCanvasHeaderTitle", "实时交互画布", "即時互動畫布", "Interactive Wheel Canvas", "インタラクティブキャンバス");
		Add("MappingsLinkSubActionsToolTip", "开启时：拖拽一级扇区将连同绑定的二级子轮盘一起对调换位\n关闭时：仅对调一级扇区主动作，保留各方位现存的二级子菜单", "開啟時：拖曳一級扇區將連同綁定的二級子輪盤一起對調換位\n關閉時：僅對調一級扇區主動作，保留各方位現存的二級子選單", "When enabled: dragging a primary sector will swap its bound tier-2 sub-wheel together\nWhen disabled: only swaps the primary sector action, keeping existing sub-menus in place", "有効時：第1階層セクターをドラッグすると紐づく第2階層サブホイールも一緒に位置交換\n無効時：第1階層のアクションのみを入れ替え、各方向のサブメニューは保持");
		Add("MappingsLinkSubActionsOn", "一二级链接: 开启", "一二級連結: 開啟", "Link Sub-Wheels: ON", "サブホイール連動: 有効");
		Add("MappingsLinkSubActionsOff", "一二级链接: 关闭", "一二級連結: 關閉", "Link Sub-Wheels: OFF", "サブホイール連動: 無効");
		Add("MappingsFpsBadgeText", "60FPS 同步", "60FPS 同步", "60FPS Sync", "60FPS 同期");
		Add("MappingsCanvasInstructions", "💡 点击内圈选一级扇区，点击外环选二级动作，点击中心选核圆；按住拖动可对调功能位置！", "💡 點擊內圈選一級扇區，點擊外環選二級動作，點擊中心選核圓；按住拖曳可對調功能位置！", "💡 Click inner ring for sector, outer ring for sub-action, center for core; drag to swap positions!", "💡 内側クリックで主セクター、外側でサブアクション、中央でコアを選択；ドラッグで位置を入れ替え！");
		Add("MappingsTier1SegmentText", "🔘 一级主轮盘", "🔘 一級主輪盤", "🔘 Tier-1 Primary Wheel", "🔘 第1階層メインホイール");
		Add("MappingsTier2SegmentText", "🌟 二级级联", "🌟 二級級聯", "🌟 Tier-2 Cascaded", "🌟 第2階層カスケード");
		Add("MappingsShowTextToggleBtnText", "🔤 图文", "🔤 圖文", "🔤 Text", "🔤 文字");
		Add("MappingsShowTextToggleBtnToolTip", "开启/关闭动作名称复合展示（开启后在扇区中直观渲染动作名称与图标，让拖拽对调一目了然）", "開啟/關閉動作名稱複合展示（開啟後在扇區中直觀轉譯動作名稱與圖示，讓拖曳對調一目了然）", "Toggle compound display of action names (renders text and icons inside sectors for intuitive dragging)", "アクション名の複合表示のオン/オフ（セクター内に名前とアイコンを表示し、ドラッグ交換を直感的に）");
		Add("MappingsZoomOutBtnToolTip", "缩小视图", "縮小檢視", "Zoom Out", "縮小");
		Add("MappingsZoomLabelToolTip", "点击复位为 100%", "點擊重設為 100%", "Click to reset zoom to 100%", "クリックで100%にリセット");
		Add("MappingsZoomInBtnToolTip", "放大视图", "放大檢視", "Zoom In", "拡大");
		Add("MappingsResetViewBtnToolTip", "重置视图", "重設檢視", "Reset View", "ビューをリセット");
		Add("MappingsSaveNotice", "💡 修改在内存中即时生效，点击主窗口右下角【保存并生效】持久化至硬盘。", "💡 修改在記憶體中即時生效，點擊主視窗右下角【儲存並生效】持久化至硬碟。", "💡 Changes take effect immediately in memory; click [Save & Apply] at the bottom-right to persist to disk.", "💡 変更はメモリ上で即時反映されます。右下の【保存して適用】をクリックして永続化してください。");
		Add("ListModeProfileHeaderTitle", "当前配置方案", "目前設定方案", "Current Profile", "現在のプロファイル");
		Add("ListModeProfileHeaderDesc", "选择或新建针对特定程序（如 Chrome、VS Code）或特定工作流的轮盘配置方案（支持双击重命名）。", "選擇或新建針對特定程式（如 Chrome、VS Code）或特定工作流程的輪盤設定方案（支援按兩下重新命名）。", "Select or create wheel profiles for specific programs (like Chrome, VS Code) or workflows (double-click to rename).", "特定アプリ（Chrome、VS Codeなど）やワークフロー用のプロファイルを選択または作成（ダブルクリックで名前変更）。");
		Add("ListModeSectorHeaderTitle", "扇区方位数量", "扇區方位數量", "Sector Count", "セクター分割数");
		Add("ListModeSectorHeaderDesc", "切换手势轮盘的切分数量。4 键最快最不易误触，8 键为标准全能方位，12 键适合功能密集场景。", "切換手勢輪盤的劃分數量。4 鍵最快最不易誤觸，8 鍵為標準全能方位，12 鍵適合功能密集場景。", "Change radial wheel sector divisions. 4 sectors is fastest and prevents misclicks; 8 is standard all-around; 12 is for dense workflows.", "ホイールの分割数を変更します。4方向は最速で誤爆しにくく、8方向は標準的、12方向は高密度な操作に最適です。");
		Add("ListModeActionListHeaderTitle", "扇区动作映射列表", "扇區動作對應清單", "Sector Action Mappings", "セクターアクション割り当て一覧");
		Add("ListModeActionListHeaderDesc1", "为每个方位指定触发动作与图标。支持热键组合（如 Ctrl+C）、启动本地程序与系统级操作。", "為每個方位指定觸發動作與圖示。支援快速鍵組合（如 Ctrl+C）、啟動本機程式與系統層級操作。", "Assign actions and icons to each direction. Supports hotkeys (Ctrl+C), launching apps, and system actions.", "各方向にトリガーアクションとアイコンを割り当てます。ショートカット、アプリ起動、システム操作に対応。");
		Add("ListModeActionListHeaderDesc2", "点击右侧功能卡的 ▲ / ▼ 箭头，将功能移动到相邻的轮盘位置槽。", "點擊右側功能卡的 ▲ / ▼ 箭頭，將功能移動至相鄰的輪盤位置。", "Click ▲ / ▼ arrows on slot cards to move functions to adjacent wheel positions.", "各スロット右側の ▲ / ▼ 矢印をクリックして、アクションを隣接スロットに移動します。");
		Add("PickIconToolTip", "点击选取矢量图标", "點擊選取向量圖示", "Click to select vector icon", "クリックしてベクターアイコンを選択");
		Add("HotkeyBuilderButtonText", "⚙️ 拼装", "⚙️ 拼裝", "⚙️ Build", "⚙️ ビルド");
		Add("HotkeyBuilderToolTip", "打开快捷热键拼装组合器（支持 Alt+Tab、Win+Tab、Shift+Alt、多位连续数值等）", "開啟快速熱鍵拼裝組合器（支援 Alt+Tab、Win+Tab、Shift+Alt、多位連續數值等）", "Open hotkey combo builder (supports Alt+Tab, Win+Tab, Shift+Alt, multi-digit sequence, etc.)", "ホットキー作成ビルダーを開く（Alt+Tab、Win+Tab、Shift+Alt、複数桁キー列などに対応）");
		Add("AppPathToolTip", "选择的应用程序路径", "選擇的應用程式路徑", "Selected application executable path", "選択したアプリの実行パス");
		Add("BrowseAppToolTip", "选择应用程序或快捷方式...", "選擇應用程式或捷徑...", "Browse for application or shortcut...", "アプリまたはショートカットを選択...");
		Add("WebUrlToolTip", "目标网址，如 https://github.com", "目標網址，如 https://github.com", "Target URL, e.g. https://github.com", "対象URL（例: https://github.com）");
		Add("FolderPathToolTip", "选择的本地文件夹路径", "選擇的本機資料夾路徑", "Selected local folder path", "選択したフォルダパス");
		Add("BrowseFolderToolTip", "选择本地文件夹...", "選擇本機資料夾...", "Select local folder...", "フォルダを選択...");
		Add("CommandParamToolTip", "要运行的命令，如 ping -n 3 127.0.0.1", "要執行的命令，如 ping -n 3 127.0.0.1", "Command to run, e.g. ping -n 3 127.0.0.1", "実行コマンド（例: ping -n 3 127.0.0.1）");
		Add("SwitchWindowIndexToolTip", "任务栏第 N 个应用（顺序同任务栏/Win+N 槽位，稳定）；图标与切换目标一致；固定未运行的槽位无法启动，托盘驻留不计入", "工作列第 N 個應用（順序同工作列/Win+N 位置，穩定）；圖示與切換目標一致；固定未執行的位置無法啟動，系統匣駐留不計入", "Taskbar N-th app (matches Win+N position); icon matches target; pinned non-running apps cannot launch, tray apps excluded", "タスクバーのN番目アプリ（Win+N相当）；対象アイコンを表示；未起動ピン留めアプリは起動不可");
		Add("TileLayoutToolTip", "平铺布局预设", "平鋪版面預設", "Tile layout preset", "ウィンドウ整列プリセット");
		Add("LaunchArgsToolTip", "启动参数 (如命令行参数或URL)", "啟動參數 (如命令列參數或URL)", "Launch arguments (command line parameters or URL)", "起動引数（コマンドライン引数またはURL）");
		Add("ManageSubActionsToolTip", "配置该扇区的二级级联子动作菜单", "設定該扇區的二級級聯子動作選單", "Configure tier-2 cascaded sub-action menu for this sector", "このセクターの第2階層サブアクションメニューを設定");
		Add("TileSettingsCollapsed", "已收纳 (点击展开)", "已收納 (點擊展開)", "Collapsed (click to expand)", "折りたたみ中（クリックで展開）");
		Add("TileSettingsExpanded", "已展开 (点击收起)", "已展開 (點擊收起)", "Expanded (click to collapse)", "展開中（クリックで折りたたむ）");
		Add("TileSettingsToggleExpand", "展开配置", "展開設定", "Expand Settings", "設定を展開");
		Add("TileSettingsToggleCollapse", "收起配置", "收起設定", "Collapse Settings", "設定を閉じる");
		Add("TileExcludeMinimizedHint", "默认关闭：最小化窗口不参与，仅排布可见窗口。", "預設關閉：最小化視窗不參與，僅排布可見視窗。", "Default off: minimized windows are excluded, only tiling visible windows.", "デフォルト無効：最小化されたウィンドウは除外され、表示中ウィンドウのみ整列します。");
		Add("TileCaptureExcludeProcessBtnText", "🎯 捕捉排除进程...", "🎯 捕捉排除處理程序...", "🎯 Capture Excluded Process...", "🎯 除外プロセスを捕捉...");
		Add("TileCaptureExcludeProcessBtnToolTip", "打开智能窗口捕捉器，选取桌面运行中的程序加入平铺排除名单（自动安全排除 StarPie 自身）", "開啟智慧視窗捕捉器，選取桌面執行中的程式加入平鋪排除清單（自動安全排除 StarPie 自身）", "Open window capture tool to pick running apps to exclude from tiling (StarPie itself is always safely excluded)", "ウィンドウキャプチャを開いて整列除外リストに追加（StarPie自身は自動で安全除外）");
		Add("TileMarginTopToolTip", "上边距（0~1000 物理像素）", "上邊距（0~1000 實體像素）", "Top margin (0~1000 physical px)", "上マージン（0〜1000物理px）");
		Add("TileMarginBottomToolTip", "下边距（0~1000 物理像素）", "下邊距（0~1000 實體像素）", "Bottom margin (0~1000 physical px)", "下マージン（0〜1000物理px）");
		Add("TileMarginLeftToolTip", "左边距（0~1000 物理像素）", "左邊距（0~1000 實體像素）", "Left margin (0~1000 physical px)", "左マージン（0〜1000物理px）");
		Add("TileMarginRightToolTip", "右边距（0~1000 物理像素）", "右邊距（0~1000 實體像素）", "Right margin (0~1000 physical px)", "右マージン（0〜1000物理px）");
		Add("TileGapToolTip", "相邻窗口之间的空隙（0~500 物理像素）", "相鄰視窗之間的間隙（0~500 實體像素）", "Gap between adjacent windows (0~500 physical px)", "隣接ウィンドウ間の間隔（0〜500物理px）");
		Add("TilePresetClassic4BtnText", "✨ 经典常用 (4项)", "✨ 經典常用 (4項)", "✨ Classic 4 Layouts", "✨ 定番4種レイアウト");
		Add("TilePresetClassic4BtnToolTip", "一键勾选 2L、2T、3L12、4G 四种高频排布", "一鍵勾選 2L、2T、3L12、4G 四種高頻版面", "One-click check 4 common layouts: 2L, 2T, 3L12, 4G", "定番の4種レイアウト（2L、2T、3L12、4G）を一括選択");
		Add("TileMoveLayoutUpToolTip", "上移", "上移", "Move Up", "上へ移動");
		Add("TileMoveLayoutDownToolTip", "下移", "下移", "Move Down", "下へ移動");
		Add("TileSelectAllLayoutsBtnText", "全", "全", "All", "全");
		Add("TileSelectAllLayoutsBtnToolTip", "全部参与循环", "全部參與循環", "All participate in cycle", "すべて巡回対象にする");
		Add("TileClearAllLayoutsBtnText", "空", "空", "None", "空");
		Add("TileClearAllLayoutsBtnToolTip", "清空（等效全部参与）", "清空（等效全部參與）", "Clear all (equivalent to all participate)", "すべてクリア（全参加と同等）");
		Add("FocusSlotCenterCoreTitle", "中心核心圆动作 (Center Core)", "中心核心圓動作 (Center Core)", "Center Core Action", "中心コアアクション");
		Add("FocusSlotCenterCoreTag", "核心圆", "核心圓", "Center Core", "中心コア");
		Add("FocusSlotCenterCoreSubtitleInherited", "💡 专属方案未配置中心动作，已自动继承全局方案「{0}」", "💡 專屬方案未設定中心動作，已自動繼承全域方案「{0}」", "💡 Not configured in app profile; inherited from global profile \"{0}\"", "💡 専用プロファイル未設定のため、グローバル「{0}」から自動継承");
		Add("FocusSlotCenterCoreSubtitleDefault", "在开启外甩脱离取消时，鼠标在中心内径死区内松开即可触发", "在開啟外甩脫離取消時，滑鼠在中心內徑死區內放開即可觸發", "When fling-out cancel is enabled, release cursor in center deadzone to trigger", "外側フリックキャンセル有効時、中心デッドゾーン内でマウスを離すとトリガー");
		Add("FocusSlotTier2EmptyTitleFormat", "扇区 {0} [{1}] 级联子动作", "扇區 {0} [{1}] 級聯子動作", "Sector {0} [{1}] Cascaded Sub-Actions", "セクター {0} [{1}] カスケードサブアクション");
		Add("FocusSlotTier2EmptyTag", "二级级联 (未添加)", "二級級聯 (未新增)", "Tier-2 Sub-Wheel (Empty)", "第2階層（未追加）");
		Add("FocusSlotTier2EmptySubtitle", "当前扇区尚未配置二级级联子动作，点击【➕ 添加第 1 个二级子动作】以创建", "目前扇區尚未設定二級級聯子動作，點擊【➕ 新增第 1 個二級子動作】以建立", "No sub-actions configured yet; click [+ Add 1st Sub-Action] to create", "第2階層サブアクションが未設定です。「➕ 最初のサブアクションを追加」をクリックして作成");
		Add("FocusSlotTier2SubActionTitleFormat", "二级动作 [{0}]", "二級動作 [{0}]", "Sub-Action [{0}]", "サブアクション [{0}]");
		Add("FocusSlotTier2SubActionTagFormat", "所属父级: 扇区 {0} [{1}]", "所屬父級: 扇區 {0} [{1}]", "Parent: Sector {0} [{1}]", "親: セクター {0} [{1}]");
		Add("FocusSlotTier2SubActionSubtitle", "向外划动二级扇区即可触发此动作", "向外劃動二級扇區即可觸發此動作", "Swipe outward onto this sub-sector to trigger this action", "第2階層セクターへ外側にスワイプしてこのアクションをトリガー");
		Add("FocusSlotPrimaryTitleFormat", "扇区 {0} [{1}]", "扇區 {0} [{1}]", "Sector {0} [{1}]", "セクター {0} [{1}]");
		Add("FocusSlotPrimaryTag", "一级主扇区", "一級主扇區", "Tier-1 Primary Sector", "第1階層主セクター");
		Add("FocusSlotPrimarySubtitleInherited", "💡 专属方案未配置本槽位，已自动继承全局方案「{0}」", "💡 專屬方案未設定本位置，已自動繼承全域方案「{0}」", "💡 Slot not configured in app profile; inherited from global profile \"{0}\"", "💡 専用プロファイル未設定のため、グローバル「{0}」から自動継承");
		Add("FocusSlotPrimarySubtitleDefault", "点击右侧轮盘直接选中扇区，或在下方配置动作与级联子菜单", "點擊右側輪盤直接選取扇區，或在下方設定動作與級聯子選單", "Click the wheel on the right to select a sector, or configure actions below", "右側のホイールをクリックして選択するか、以下でアクションとサブメニューを設定");
		Add("MappingsEditIndicatorBatch", "🎯 批量修改模式 (已多选 {0} 个扇区)", "🎯 批次修改模式 (已多選 {0} 個扇區)", "🎯 Batch Edit Mode ({0} sectors selected)", "🎯 一括編集モード（{0}個のセクターを選択中）");
		Add("MappingsEditIndicatorCenter", "🎯 正在编辑: 中心核心圆动作", "🎯 正在編輯: 中心核心圓動作", "🎯 Editing: Center Core Action", "🎯 編集中: 中心コアアクション");
		Add("MappingsEditIndicatorSub", "🌟 正在编辑: 二级动作 [{0}]", "🌟 正在編輯: 二級動作 [{0}]", "🌟 Editing: Sub-Action [{0}]", "🌟 編集中: サブアクション [{0}]");
		Add("MappingsEditIndicatorPrimary", "🎯 正在编辑: 扇区 {0} [{1}]", "🎯 正在編輯: 扇區 {0} [{1}]", "🎯 Editing: Sector {0} [{1}]", "🎯 編集中: セクター {0} [{1}]");
		Add("MappingsEditIndicatorDragging", "🔄 正在拖拽 [{0}]，{1}", "🔄 正在拖曳 [{0}]，{1}", "🔄 Dragging [{0}], {1}", "🔄 [{0}] をドラッグ中、{1}");
		Add("MappingsEditIndicatorSubSwapped", "🎯 已对调二级动作顺序：[{0}] ↔ [{1}]！", "🎯 已對調二級動作順序：[{0}] ↔ [{1}]！", "🎯 Swapped sub-action order: [{0}] ↔ [{1}]!", "🎯 サブアクション順序を入れ替えました: [{0}] ↔ [{1}]!");
		Add("MappingsEditIndicatorSubCrossSwapped", "🎯 已跨扇区对调二级动作：[{0}] ↔ [{1}]！", "🎯 已跨扇區對調二級動作：[{0}] ↔ [{1}]！", "🎯 Swapped sub-actions across sectors: [{0}] ↔ [{1}]!", "🎯 セクター間でサブアクションを入れ替えました: [{0}] ↔ [{1}]!");
		Add("MappingsEditIndicatorSubMoved", "🎯 已将二级动作 [{0}] 移动至目标扇区！", "🎯 已將二級動作 [{0}] 移動至目標扇區！", "🎯 Moved sub-action [{0}] to target sector!", "🎯 サブアクション [{0}] を対象セクターに移動しました！");
		Add("MappingsEditIndicatorSwapped", "🎯 已将 [{0}] 与 [{1}] 成功对调位置{2}！", "🎯 已將 [{0}] 與 [{1}] 成功對調位置{2}！", "🎯 Successfully swapped [{0}] and [{1}]{2}!", "🎯 [{0}] と [{1}] の位置を入れ替えました{2}！");
		Add("MappingsEditIndicatorLinkedHint", " (已联动二级菜单)", " (已聯動二級選單)", " (linked sub-wheel)", "（サブホイール連動）");
		Add("ContributorsRefreshTip", "向 GitHub API 请求最新贡献者数据", "向 GitHub API 請求最新貢獻者資料", "Fetch latest contributors from GitHub API", "GitHub API から最新の貢献者データを取得");
		Add("ViewReleasesWebBtnText", "🌐 网页发布页", "🌐 網頁發布頁", "🌐 Releases Page", "🌐 リリースページ");
		Add("ViewReleasesWebBtnToolTip", "直接在默认浏览器中打开 GitHub Releases 发布页", "直接在預設瀏覽器中打開 GitHub Releases 發布頁", "Open GitHub Releases page in your default browser", "デフォルトブラウザで GitHub Releases ページを開く");
		Add("StartDownloadUpdateBtnText", "⬇️ 立即下载更新", "⬇️ 立即下載更新", "⬇️ Download Update", "⬇️ 今すぐダウンロード");
		Add("OpenWebReleaseBtnText", "🌐 前往网页", "🌐 前往網頁", "🌐 Open Webpage", "🌐 Web ページへ");
		Add("UpdateDownloadPkgLabel", "📦 下载版本:", "📦 下載版本:", "📦 Package:", "📦 パッケージ:");
		Add("UpdatePkgStandaloneRadioText", "独立免安装单文件版 (~68 MB, 推荐)", "獨立免安裝單檔案版 (~68 MB, 推薦)", "Standalone Single-File (~68 MB, Recommended)", "スタンドアロン単一ファイル版 (~68 MB, 推奨)");
		Add("UpdatePkgLightweightRadioText", "依赖 .NET 8 运行时轻量版 (~2.7 MB)", "依賴 .NET 8 執行階段輕量版 (~2.7 MB)", "Lightweight (.NET 8 Runtime, ~2.7 MB)", ".NET 8 ランタイム依存軽量版 (~2.7 MB)");
		Add("UpdateChangelogLabel", "📋 详细更新日志:", "📋 詳細更新日誌:", "📋 Detailed Changelog:", "📋 詳細更新履歴:");
		Add("CancelDownloadBtnText", "✖ 取消下载", "✖ 取消下載", "✖ Cancel Download", "✖ ダウンロードをキャンセル");
		Add("UpdateDownloadSpeedCalculating", "⚡ 计算中...", "⚡ 計算中...", "⚡ Calculating...", "⚡ 計算中...");
		Add("UpdateReadyTitleText", "新版本已完整下载就绪", "新版本已完整下載就緒", "New Version Downloaded and Ready", "新しいバージョンのダウンロードが完了しました");
		Add("UpdateReadyDescText", "点击立即重启，将优雅保存当前配置并静默更新覆盖程序，完成后自动唤起新版本。", "點擊立即重啟，將優雅儲存目前設定並靜默更新覆蓋程式，完成後自動喚起新版本。", "Click to restart now. Settings will be safely saved, the update installed quietly, and the new version relaunched.", "今すぐ再起動をクリックすると、現在の設定を安全に保存してサイレント更新を行い、完了後に自動起動します。");
		Add("ApplyRestartUpdateBtnText", "🚀 立即退出并重启更新", "🚀 立即結束並重啟更新", "🚀 Restart to Update", "🚀 終了して更新を再起動");
		Add("OpenUpdateFolderBtnText", "📂 打开文件位置", "📂 開啟檔案位置", "📂 Open File Location", "📂 ファイルの場所を開く");
		Add("UpdateAdvancedOptionsBadge", "高级选项", "進階選項", "Advanced", "詳細設定");
		Add("ChinaFastDownloadBadge", "国内极速下载", "國內極速下載", "Fast Mirror", "高速ミラー");
		Add("RollbackPackageArchLabel", "安装包架构:", "安裝套件架構:", "Package Architecture:", "パッケージアーキテクチャ:");
		Add("RollbackChangelogHeaderLabel", "📋 历史更新日志摘要:", "📋 歷史更新日誌摘要:", "📋 Historical Changelog Summary:", "📋 過去の更新履歴概要:");
		Add("LanguageFollowSystem", "🖥️ 跟随系统", "🖥️ 跟隨系統", "🖥️ Follow System", "🖥️ システムに従う");
		Add("TipTestOcr", "立即启动全屏框选测试当前 OCR 识别配置", "立即啟動全螢幕框選測試目前 OCR 識別設定", "Launch full-screen area selection to test current OCR configuration", "全画面範囲選択を起動して現在の OCR 設定をテスト");
		Add("TipConfigOcr", "打开 OCR 识别引擎与 API 参数设置弹窗", "開啟 OCR 識別引擎與 API 參數設定彈窗", "Open OCR recognition engine and API parameter settings dialog", "OCR 認識エンジンと API パラメータ設定ダイアログを開く");
		Add("TipTrimMemory", "立即清理未引用的工作集物理内存", "立即清理未參照的工作集實體記憶體", "Immediately purge unreferenced working set physical memory", "参照されていないワーキングセット物理メモリを直ちに解放");
		Add("TipSaveNewProfile", "将当前全部轮盘动作与设置另存为一套全新的配置方案", "將目前全部輪盤動作與設定另存為一套全新的設定方案", "Save current radial actions and settings as a new profile", "現在の全ホイールアクションと設定を新しいプロファイルとして保存");
		Add("TipRenameProfile", "重命名当前选中的配置方案", "重新命名目前選取的設定方案", "Rename the currently selected profile", "現在選択されているプロファイルを名前変更");
		Add("TipDeleteProfile", "删除当前选中的配置方案（需保留至少一套）", "刪除目前選取的設定方案（需保留至少一套）", "Delete the currently selected profile (at least one must remain)", "現在選択されているプロファイルを削除（最低1つ保持が必要）");
		Add("TipImportConfig", "导入外部 StarPie JSON 配置文件并自动收纳入配置方案列表", "匯入外部 StarPie JSON 設定檔並自動納入設定方案清單", "Import external StarPie JSON config and add it to profiles list", "外部の StarPie JSON 設定ファイルをインポートしてプロファイル一覧に追加");
		Add("TipExportConfig", "将选中的配置方案导出为单独的 JSON 备份文件", "將選取的設定方案匯出為獨立的 JSON 備份檔案", "Export selected profile as an individual JSON backup file", "選択したプロファイルを個別の JSON バックアップファイルとしてエクスポート");
		Add("TipResetConfig", "恢复初始默认轮盘手势与设置（不会影响其它已保存方案）", "還原初始預設輪盤手勢與設定（不會影響其他已儲存方案）", "Reset default gestures and settings (other saved profiles are unaffected)", "初期デフォルトのジェスチャーと設定をリセット（保存済み他プロファイルには影響しません）");
		Add("TipOpenLogFolder", "在资源管理器中打开日志文件夹", "在檔案總管中開啟記錄檔資料夾", "Open log directory in File Explorer", "エクスプローラーでログフォルダーを開く");
		Add("TipViewTodayLog", "在系统默认编辑器中打开当天的日志文件", "在系統預設編輯器中開啟當天的記錄檔案", "Open today's log file in default text editor", "システムの既定エディタで今日のログファイルを開く");
		Add("UpdateStatusChecking", "正在检查更新...", "正在檢查更新...", "Checking for updates...", "更新を確認中...");
		Add("UpdateStatusFoundNew", "发现新版本 {0}", "發現新版本 {0}", "Update Available: {0}", "新しいバージョンが見つかりました: {0}");
		Add("UpdateStatusFoundNewDesc", "检测到更高版本 {0} 可供升级！发布于 {1}。", "檢測到更高版本 {0} 可供升級！發布於 {1}。", "New version {0} is available! Released on {1}.", "新しいバージョン {0} が利用可能です！公開日: {1}。");
		Add("UpdateNewVersionTag", "🎉 发现新版本 {0}", "🎉 發現新版本 {0}", "🎉 New Version {0} Available", "🎉 新バージョン {0} が見つかりました");
		Add("ReleaseChannelBeta", "尝鲜测试版 (Pre-release)", "嘗鮮測試版 (Pre-release)", "Pre-release (Beta)", "プレビューテスト版 (Pre-release)");
		Add("ReleaseChannelStable", "正式稳定版 (Stable)", "正式穩定版 (Stable)", "Official Stable", "正式安定版 (Stable)");
		Add("UpdateReleaseDateFmt", "发布于 {0} · GitHub Releases", "發布於 {0} · GitHub Releases", "Released on {0} · GitHub Releases", "公開日: {0} · GitHub Releases");
		Add("NoChangelogAvailable", "作者暂未提供更新日志说明。", "作者暫未提供更新日誌說明。", "No changelog notes provided for this release.", "このリリースの更新履歴は提供されていません。");
		Add("UpdateStatusUpToDate", "当前已是最新版本", "目前已是最新版本", "StarPie is up to date", "現在は最新バージョンです");
		Add("UpdateStatusUpToDateDesc", "当前运行版本: StarPie v{0} (64位)。线上最新版本: {1}。上次检查: {2}", "目前執行版本: StarPie v{0} (64位元)。線上最新版本: {1}。上次檢查: {2}", "Current version: StarPie v{0} (64-bit). Latest online: {1}. Last checked: {2}", "現在のバージョン: StarPie v{0} (64ビット)。オンライン最新: {1}。最終確認: {2}");
		Add("UpdateStatusError", "检查更新受阻", "檢查更新受阻", "Update Check Failed", "更新の確認に失敗しました");
		Add("UpdateStatusErrorDesc", "未能从 GitHub 自动获取到 Release 数据，可点击右侧「🌐 网页发布页」手动前往查看。", "未能從 GitHub 自動取得 Release 資料，可點擊右側「🌐 網頁發布頁」手動前往查看。", "Failed to fetch release data from GitHub. Click '🌐 Releases Page' to view manually.", "GitHub からリリースデータを取得できませんでした。「🌐 リリースページ」をクリックして手動で確認してください。");
		Add("RollbackDetailDateFmt", "· 发布于 {0}", "· 發布於 {0}", "· Released on {0}", "· 公開日: {0}");
		Add("RollbackArchStandalone", "独立免安装单文件版 (Standalone)", "獨立免安裝單檔案版 (Standalone)", "Standalone Single-File (Standalone)", "スタンドアロン単一ファイル版 (Standalone)");
		Add("RollbackArchLightweight", "依赖 .NET 运行时轻量版 (Lightweight)", "依賴 .NET 執行階段輕量版 (Lightweight)", "Lightweight (.NET Dependent)", ".NET ランタイム依存軽量版 (Lightweight)");
		Add("RollbackDownloadingFmt", "正在高速下载历史版本 {0}...", "正在高速下載歷史版本 {0}...", "Downloading historical version {0}...", "過去のバージョン {0} を高速ダウンロード中...");
		Add("UpdateStatusCurrentVerDescFmt", "当前运行版本: StarPie v{0} (64位)。上次检查: {1}", "目前執行版本: StarPie v{0} (64位元)。上次檢查: {1}", "Current version: StarPie v{0} (64-bit). Last checked: {1}", "現在のバージョン: StarPie v{0} (64ビット)。最終確認: {1}");
		Add("UpdateLastCheckNever", "未检查", "未檢查", "Never", "未確認");
		Add("UpdateStatusDownloadComplete", "下载完成 · 就绪安装", "下載完成 · 就緒安裝", "Download complete · Ready to install", "ダウンロード完了 · インストール準備完了");
		Add("UpdateStatusRollbackComplete", "回退包下载完成 · 就绪安装", "回退包下載完成 · 就緒安裝", "Rollback package ready · Ready to install", "ロールバックパッケージ完了 · インストール準備完了");
		Add("UpdateDownloadingFmt", "正在高速下载更新包 {0}...", "正在高速下載更新包 {0}...", "Downloading update package {0}...", "更新パッケージ {0} を高速ダウンロード中...");
		Add("UpdateDownloadSpeedConnecting", "⚡ 连接下载源中...", "⚡ 連線下載來源中...", "⚡ Connecting to download source...", "⚡ ダウンロードソースに接続中...");
		Add("BtnViewChangelog", "查看完整 CHANGELOG", "查看完整 CHANGELOG", "View Full CHANGELOG", "完全な CHANGELOG を表示");
		Add("Tab4_AboutTitleText", "关于软件", "關於軟體", "About StarPie", "StarPie について");
		Add("Tab4_AboutDescText", "StarPie 现代鼠标轮盘笔势工具版本信息与完整演进历程。", "StarPie 現代滑鼠輪盤手勢工具版本資訊與完整演進歷程。", "StarPie modern mouse gesture wheel version info and evolution history.", "StarPie モダンマウスジェスチャーホイールのバージョン情報と開発履歴。");
		Add("Tab4_AppSloganText", "高质感、极速现代 Windows 鼠标轮盘笔势工具", "高質感、極速現代 Windows 滑鼠輪盤手勢工具", "Premium, ultra-fast modern Windows mouse gesture wheel tool", "プレミアムで超高速なモダン Windows マウスジェスチャーホイール");
		Add("Tab4_MilestonesHeaderTitle", "版本演进里程碑", "版本演進里程碑", "Version Milestones", "バージョン履歴");
		Add("Tab4_Ms_174_Title", "v1.7.4 正式版 原生全盘极速秒搜 & 专属方案隔离 & 控制台精简 & 性能调优", "v1.7.4 正式版 原生全盤極速秒搜 & 專屬方案隔離 & 主控台精簡 & 效能調校", "v1.7.4 Official Release: Native Quick Finder, Profile Isolation, UI Polish & Performance Optimizations", "v1.7.4 正式版: 100% ネイティブ高速ファイル検索・プロファイル分離・UI 簡素化・パフォーマンス向上");
		Add("Tab4_Ms_174_P1", "• ⚡ 【StarPie 100% 纯原生极速秒搜】：全面告别外部第三方依赖，毫秒级应用内存短路与 50ms 深盘时间预算硬控，消除扫描卡顿；", "• ⚡ 【StarPie 100% 純原生極速秒搜】：全面告別外部第三方依賴，毫秒級應用程式記憶體短路與 50ms 深盤時間預算硬控，消除掃描卡頓；", "• ⚡ [100% Native Quick Finder]: Eliminated all third-party external dependencies; millisecond-level in-memory cache bypass and strict 50ms deep disk time budget to eliminate scan lag;", "• ⚡ 【100% ネイティブ高速検索】：外部依存を完全に排除し、ミリ秒級のメモリ短絡と 50ms ディスク探索時間バジェットで検索カクつきを解消；");
		Add("Tab4_Ms_174_P2", "• 🌐 【全局方案继承按需隔离】：专属程序方案支持多层独立轮盘并彻底解耦全局串扰；继承全局未配置槽位功能默认调整为关闭，保障新建方案纯净独立；", "• 🌐 【全域方案繼承按需隔離】：專屬應用程式方案支援多層獨立輪盤並徹底解耦全域串擾；繼承全域未配置槽位功能預設調整為關閉，保障新建方案純淨獨立；", "• 🌐 [Profile Isolation & Independent Layers]: Dedicated app profiles support multi-layer wheels decoupled from global layers; global slot inheritance defaults to OFF for clean isolation;", "• 🌐 【プロファイル分離と多層独立】：アプリ専用プロファイルで多層ホイールをサポートし、グローバル層との干渉を完全に排除。グローバル継承を既定でオフにし独立性を確保；");
		Add("Tab4_Ms_174_P3", "• 🎨 【控制台界面与文案规范化精炼】：全面解决 Issue #119，统一右上角单选模式切换，消除繁琐并列句式与英文后缀，清理侧边栏冗余图标；", "• 🎨 【主控台介面與文案規範化精煉】：全面解決 Issue #119，統一右上角單選模式切換，消除繁瑣並列句式與英文後綴，清理側邊欄冗餘圖示；", "• 🎨 [Console UI & Copywriting Refinement]: Resolved Issue #119; unified mode switch to top-right radio toggle; stripped redundant parallel phrasing and English suffixes; decluttered sidebar icons;", "• 🎨 【UI とテキストの簡素化・洗練】：Issue #119 を解決し、右上のセグメントトグルにモード切替を集約。冗長な重複文や英語接尾辞を排除し、サイドバーアイコンをすっきり整理；");
		Add("Tab4_Ms_174_P4", "• 🎛️ 【自定义音效合成与渲染零堆分配】：高级模式开放交互音效独立调配，纠正播放图标朝向；动画画刷冻结复用，全局鼠标钩子零堆分配，丝滑高帧。", "• 🎛️ 【自訂音效合成與渲染零堆配置】：進階模式開放互動音效獨立調配，修正播放圖示朝向；動畫筆刷凍結複用，全域滑鼠勾點零堆配置，絲滑高幀率。", "• 🎛️ [Custom Audio Synthesis & Zero Heap Allocation]: Pro mode unlocks custom gesture audio mixer; corrected play icon direction; frozen rendering resources and zero heap allocation in global mouse hook for smooth high FPS.", "• 🎛️ 【カスタム効果音合成とゼロアロケーション描画】：プロモードでジェスチャー効果音の個別調整を開放、再生アイコンの向きを修正。描画リソースの Freeze 化と低遅延フックのゼロアロケーションで超高フレームレートを実現。");
		Add("Tab4_Ms_174b3_Title", "v1.7.4-beta.3 控制台 UI 与文本规范化精简 & 模式切换整合", "v1.7.4-beta.3 控制台 UI 與文字規範化精簡 & 模式切換整合", "v1.7.4-beta.3 Console UI & Text Simplification & Mode Switch Consolidation", "v1.7.4-beta.3 コンソール UI とテキストの簡素化・モード統合");
		Add("Tab4_Ms_174b3_P1", "• 🎨 【消除重复繁琐文本】：全面精简导航栏与卡片标题中重复的“XX与XX”平行并列句式，去除界面控件中硬编码的英文后缀；", "• 🎨 【消除重複繁瑣文字】：全面精簡導航列與卡片標題中重複的「XX與XX」平行並列句式，去除介面控制項中硬編碼的英文後綴；", "• 🎨 [Text Simplification]: Streamlined parallel phrases in navigation tabs and card titles; removed hardcoded English suffixes in controls;", "• 🎨 【テキスト簡素化】：ナビゲーションとカードタイトルの冗長な重複文を整理し、UI コントロールの固定英語サフィックスを削除；");
		Add("Tab4_Ms_174b3_P2", "• 🧭 【导航图标与模式整合】：移除侧边栏多余双图标堆砌，整合重复的模式切换开关为右上角统一单选；", "• 🧭 【導航圖示與模式整合】：移除側邊欄多餘雙圖示堆疊，整合重複的模式切換開關為右上角統一單選；", "• 🧭 [Navigation & Mode Unification]: Removed redundant stacked icons in sidebar; consolidated mode switch into top-right segment toggle;", "• 🧭 【ナビゲーション・モード統合】：サイドバーの重複アイコンを削除し、右上の一元化されたセグメント切り替えに集約；");
		Add("Tab4_Ms_174b3_P3", "• 🌐 【占位符修复】：彻底解决高级系统设置未国际化占位符显示 raw key 文本的问题，多语言字典规范化对齐。", "• 🌐 【佔位符修復】：徹底解決進階系統設定未國際化佔位符顯示 raw key 文字的問題，多語言字典規範化對齊。", "• 🌐 [Placeholder Fix]: Resolved unlocalized raw key placeholders in system settings; standardized multilingual dictionary alignment.", "• 🌐 【プレースホルダー修正】：システム設定で未翻訳 raw key が表示される不具合を修正し、多言語辞書を正規化。");
		Add("Tab4_Ms_174b2_Title", "v1.7.4-beta.2 StarPie 原生全盘极速秒搜纯净版 & 毫秒级分层短路与时间预算保护", "v1.7.4-beta.2 StarPie 原生全盤極速秒搜純淨版 & 毫秒級分層短路與時間預算保護", "v1.7.4-beta.2 Native Quick Finder Pure Edition & Millisecond Tiered Short-Circuit & Time Budget", "v1.7.4-beta.2 ネイティブ高速検索ピュア版・ミリ秒階層短絡と時間予算保護");
		Add("Tab4_Ms_174b2_P1", "• ⚡ 【StarPie 纯原生极速引擎架构】：全面采用 100% 自包含零外部依赖的原生极速搜索引擎，秒出常用应用与高频工程；", "• ⚡ 【StarPie 純原生極速引擎架構】：全面採用 100% 自包含零外部依賴的原生極速搜尋引擎，秒出常用應用與高頻專案；", "• ⚡ [Native Fast Engine]: 100% self-contained zero-external-dependency search engine; instantly indexes apps and frequent projects;", "• ⚡ 【ネイティブ高速エンジン】：外部依存ゼロの完全自己完結型検索エンジンを採用し、常用アプリを瞬時に検索；");
		Add("Tab4_Ms_174b2_P2", "• 🚀 【毫秒级分类短路机制】：搜索应用程序或系统工具时直接从预索引内存库短路返回（0ms~3ms），完全消除磁盘 I/O 震荡；", "• 🚀 【毫秒級分類短路機制】：搜尋應用程式或系統工具時直接從預索引記憶體庫短路返回（0ms~3ms），完全消除磁碟 I/O 震盪；", "• 🚀 [Millisecond Short-Circuit]: Returns app and system utility queries from pre-indexed memory (0ms~3ms), eliminating disk I/O thrashing;", "• 🚀 【ミリ秒階層短絡】：アプリやシステムツールを事前インデックス済みメモリから短絡返却（0ms~3ms）、ディスク負荷をゼロに；");
		Add("Tab4_Ms_174b2_P3", "• ⏱️ 【50ms 时间预算硬控 (Time-Budget)】：深盘遍历增加 50ms 严格时间预算与收敛深度，彻底消除数万目录地毯式扫描导致的数秒失控卡顿；", "• ⏱️ 【50ms 時間預算硬控 (Time-Budget)】：深盤遍歷增加 50ms 嚴格時間預算與收斂深度，徹底消除數萬目錄地毯式掃描導致的數秒失控卡頓；", "• ⏱️ [50ms Time-Budget Guard]: Enforced 50ms strict time budget and depth limit on deep traversal, preventing multi-second freezes;", "• ⏱️ 【50ms 時間予算ガード】：ディープ走査に 50ms の厳格な時間予算を導入し、数万ディレクトリ走査によるフリーズを根絶；");
		Add("Tab4_Ms_174b2_P4", "• 📂 【高频工作区与工程优先】：优先穿透扫描桌面、下载、文档等高频目录，常用文件即敲即出，丝滑流畅。", "• 📂 【高頻工作區與專案優先】：優先穿透掃描桌面、下載、文件等高頻目錄，常用檔案即敲即出，絲滑流暢。", "• 📂 [Frequent Workspace Priority]: Prioritizes Desktop, Downloads, and Documents directories for immediate file discovery.", "• 📂 【高頻度ワークスペース優先】：デスクトップ、ダウンロード、ドキュメントを優先スキャンし、即座に候補を表示。");
		Add("Tab4_Ms_174b1_Title", "v1.7.4-beta.1 播放图标朝向修正 & 自定义音效测试版 & 多层展开二级渲染修复", "v1.7.4-beta.1 播放圖示朝向修正 & 自訂音效測試版 & 多層展開二級渲染修復", "v1.7.4-beta.1 Play Icon Orientation Fix & Custom Sound Mixer Beta & Multi-Tier Sub-Ring Render Fix", "v1.7.4-beta.1 再生アイコン向き修正・カスタム音効ベータ版・多層サブリング描画修正");
		Add("Tab4_Ms_174b1_P1", "• 🎛️ 【自定义交互音效调配 (高级模式·测试功能)】：在高级全景模式下开放方案配置入口，支持为 5 大核心手势事件（唤出、划过扇区、展开二级、动作触发、手势取消）独立调校程序合成微波形、本地音频采样与音高音量；支持方案新建、删除保护与导入导出，简单模式下保持清爽收起；", "• 🎛️ 【自訂互動音效調配 (進階模式·測試功能)】：在進階全景模式下開放方案設定入口，支援為 5 大核心手勢事件獨立調校合成微波形、音訊取樣與音高音量；支援方案新建、刪除保護與匯入匯出；", "• 🎛️ [Custom Sound Mixer]: Advanced mode provides audio customization for 5 gesture events (popup, hover, expand, trigger, cancel) with pitch, volume, waveform synthesis, and profile import/export;", "• 🎛️ 【カスタム音効ミキサー】：5 つの手勢イベント（表示、ホバー、展開、実行、キャンセル）の波形合成、音高、音量を独立調整可能；");
		Add("Tab4_Ms_174b1_P2", "• 🔄 【修复多层轮盘展开二级子环切换显示】：修复开启「唤出时直接同时展开二级轮盘」时，使用滚轮或快捷键切换图层导致二级子轮盘在视觉上丢失的问题；换层后外圈子环同步呈现并即时响应光标悬停；", "• 🔄 【修復多層輪盤展開二級子環切換顯示】：修復開啟「喚出時直接同時展開二級輪盤」時，使用滾輪或快捷鍵切換圖層導致二級子輪盤在視覺上遺失的問題；", "• 🔄 [Multi-Layer Sub-Ring Switch Fix]: Fixed visual disappearance of outer sub-rings when switching wheel layers via scroll wheel or hotkeys while auto-expand is enabled;", "• 🔄 【多層サブリング切り替え修正】：自動展開有効時にホイールスクロールでレイヤーを切り替えてもサブリングが正常に追随表示されるよう修正；");
		Add("Tab4_Ms_174b1_P3", "• ▶️ 【PlayPause 默认矢量图标纠正】：将默认 Play/Pause 图标中的播放三角形纠正为标准朝右（▶）并搭配双竖杆（❚❚），彻底规避原朝左三角形易与后退/上一首混淆的问题；", "• ▶️ 【PlayPause 預設向量圖示糾正】：將預設 Play/Pause 圖示中的播放三角形糾正為標準朝右（▶）並搭配雙豎桿（❚❚），徹底規避原朝左三角形易與後退/上一首混淆的問題；", "• ▶️ [Play/Pause Icon Correction]: Corrected the default Play/Pause icon to standard rightward triangle (▶) and dual bars (❚❚), avoiding confusion with backward navigation;", "• ▶️ 【Play/Pause アイコン修正】：再生アイコンの向きを標準の右向き（▶）に正しく修正；");
		Add("Tab4_Ms_174b1_P4", "• 📝 【扇区长文本智能两行显示优化】：深度完善扇区长文本换行格式化算法，多词及中英混排自动规整为紧凑双行排版。", "• 📝 【扇區長文字智慧兩行顯示最佳化】：深度完善扇區長文字換行格式化演算法，多詞及中英混排自動規整為緊湊雙行排版。", "• 📝 [Two-Line Sector Text Formatting]: Improved line breaking algorithm to neatly balance multi-word and mixed-language titles into two compact lines.", "• 📝 【セクター長文 2 行表示最適化】：複数単語や日英混在テキストをスマートに 2 行へ折り返す自動レイアウトを強化。");
		Add("Tab4_Ms_173_Title", "v1.7.3 正式版 CAD 专属按键唤醒 & 全盘深度秒搜与自由拉伸", "v1.7.3 正式版 CAD 專屬按鍵喚醒 & 全盤深度秒搜與自由拉伸", "v1.7.3 Official: CAD Exclusive Trigger Key & Deep Quick Finder & Freely Resizable Window", "v1.7.3 正式版: CAD 専用トリガーキー・全盤高速検索・自由リサイズ");
		Add("Tab4_Ms_173_P1", "• 🎯 【黑名单程序专属呼出按键】：针对 SolidWorks 等 3D CAD 软件原生笔势痛点，支持在黑名单中为特定程序配置单独专属唤醒按键（如中键/侧键/组合键）；原生鼠标右键 100% 绝对零时延放行给宿主软件，完美兼顾 CAD 笔势与 StarPie 全局手势；", "• 🎯 【黑名單程式專屬呼出按鍵】：針對 SolidWorks 等 3D CAD 軟體原生筆勢痛點，支援在黑名單中為特定程式設定單獨專屬喚醒按鍵；原生滑鼠右鍵 100% 零延遲放行給宿主軟體；", "• 🎯 [CAD App Dedicated Trigger Key]: Configure app-specific triggers (middle, side, combo) for blacklisted apps like SolidWorks, passing right-clicks through with 0ms latency;", "• 🎯 【CAD 専用トリガーキー】：SolidWorks 等の CAD 向けにアプリ専用トリガーを設定可能にし、右クリックを 0ms で透過；");
		Add("Tab4_Ms_173_P2", "• 🔴 【复刻物理录制卡片与实时反馈】：专属按键配置完全复刻 Tab 2 录制交互，支持鼠标所有按键与键盘单键/修饰组合键物理录制，配备硬件感知器与 ESC 快速取消；", "• 🔴 【複刻實體錄製卡片與即時回饋】：專屬按鍵設定完全複刻 Tab 2 錄製互動，支援滑鼠所有按鍵與鍵盤單鍵/組合鍵實體錄製；", "• 🔴 [Dedicated Hotkey Recorder]: Full physical key recording interface for custom app triggers supporting all mouse buttons and keyboard hotkeys;", "• 🔴 【専用キーレコーダー】：マウス各ボタンおよびキーボード修飾キーの物理入力をそのまま記録可能；");
		Add("Tab4_Ms_173_P3", "• 🎬 【全盘秒搜增加视频分类过滤】：新增「🎬 视频」独立分类过滤按钮，内置主流视频格式（.mp4, .mkv, .avi, .mov, .flv, .wmv, .webm 等）精准匹配，并支持无输入时自动推荐近期视频；", "• 🎬 【全盤秒搜增加影片分類過濾】：新增「🎬 影片」獨立分類過濾按鈕，內建主流影片格式精準比對；", "• 🎬 [Quick Finder Video Filter]: Added dedicated Video category filter supporting common formats (.mp4, .mkv, .avi, .mov, etc.) with recent video recommendations;", "• 🎬 【動画カテゴリフィルター】：動画専用フィルターボタンを追加し、主要動画フォーマットを瞬時に絞り込み；");
		Add("Tab4_Ms_173_P4", "• 📌 【秒搜窗口置顶图钉锁定】：右上角增加窗口置顶图钉按钮，开启后窗口始终置顶且点击外部失焦不关闭，方便对照文件与多任务协作；", "• 📌 【秒搜視窗置頂圖釘鎖定】：右上角增加視窗置頂圖釘按鈕，開啟後視窗始終置頂且點擊外部失焦不關閉；", "• 📌 [Quick Finder Pin-to-Top]: Added pin button to keep search window pinned on top even when losing focus, ideal for multitasking;", "• 📌 【ピン留め機能】：検索ウィンドウを最前面に固定するピンボタンを追加、フォーカス喪失時も非表示になりません；");
		Add("Tab4_Ms_173_P5", "• 📐 【秒搜窗口自由拖拽缩放与记忆】：支持右下角点阵手柄与边缘自由拖拽调整窗口宽度与高度，并自动持久化记忆用户自定义窗口尺寸。", "• 📐 【秒搜視窗自由拖曳縮放與記憶】：支援右下角控點與邊緣自由拖曳調整視窗寬度與高度，並自動持久化記憶使用者自訂尺寸。", "• 📐 [Quick Finder Resizable & Size Memory]: Drag window edges or bottom-right grip to resize; dimensions are automatically remembered.", "• 📐 【自由リサイズ＆サイズ記憶】：ウィンドウ端やグリップをドラッグしてサイズ変更可能、サイズを自動記憶。");
		Add("Tab4_Ms_173b8_Title", "v1.7.3-beta.8 全盘秒搜 (Quick Finder) & OCR 截屏识字专项优化", "v1.7.3-beta.8 全盤秒搜 (Quick Finder) & OCR 截圖識字專項最佳化", "v1.7.3-beta.8 Quick Finder & OCR Screenshot Recognition Dedicated Optimization", "v1.7.3-beta.8 高速検索 (Quick Finder) ＆ OCR スクリーンショット認識最適化");
		Add("Tab4_Ms_173b8_P1", "• 🔍 【光标跟随与自由拖拽秒搜 (Quick Finder)】：秒搜窗口唤出时自动定位在鼠标触发光标处，且支持按住窗口顶部及空白区域自由拖拽移动；", "• 🔍 【游標跟隨與自由拖曳秒搜 (Quick Finder)】：秒搜視窗喚出時自動定位在滑鼠觸發游標處，且支援按住視窗頂部自由拖曳移動；", "• 🔍 [Cursor-Following Quick Finder]: Search window appears right at your mouse cursor and supports free dragging anywhere on the header;", "• 🔍 【カーソル追従＆ドラッグ移動】：マウスカーソル位置に検索ウィンドウを瞬時にポップアップ、自由にドラッグ移動可能；");
		Add("Tab4_Ms_173b8_P2", "• ⚡ 【内置原生极速检索引擎】：完全内置自主研发的毫秒级文件与程序极速搜索引擎，开箱即用无需依赖任何第三方软件；", "• ⚡ 【內建原生極速檢索引擎】：完全內建自主研發的毫秒級檔案與程式極速搜尋引擎，開箱即用無需依賴任何第三方軟體；", "• ⚡ [Built-in Native Fast Indexer]: In-house millisecond file & program indexing engine, zero external tools required;", "• ⚡ 【内蔵ネイティブ高速検索】：自社開発のミリ秒インデックスエンジンを内蔵、サードパーティ製ツール不要；");
		Add("Tab4_Ms_173b8_P3", "• 🎯 【OCR 选区与坐标 1:1 精确映射】：采用底层物理全景屏幕快照冻结与直接裁切机制，彻底解决高 DPI 与多显示器缩放下的选区坐标漂移问题；", "• 🎯 【OCR 選區與座標 1:1 精確對應】：採用底層實體全景螢幕快照凍結與直接裁切機制，徹底解決高 DPI 跨螢幕縮放下座標漂移問題；", "• 🎯 [Pixel-Perfect OCR Mapping]: Direct physical screen snapshot and crop mechanism, completely eliminating coordinate drift under multi-monitor mixed DPI;", "• 🎯 【OCR 1:1 ピクセル精度対応】：マルチモニター・混合 DPI 環境下でも選択座標のズレを完全に根絶；");
		Add("Tab4_Ms_173b8_P4", "• 🖼️ 【多分辨率自适应与防黑化修复】：大图自动下采样防止 2600px 引擎超限崩溃，小图高保真双三次插值放大提升识别率，修复 GDI+ 32bpp 格式导致的黑屏问题；", "• 🖼️ 【多解析度自動適應與防黑畫面修復】：大圖自動向下取樣防止引擎超限當機，小圖高傳真雙立方內插放大提升識別率；", "• 🖼️ [OCR Robustness & Anti-Blackout]: Auto downsamples images over 2600px, bicubic upscaling for small text, fixes GDI+ 32bpp black frame glitch;", "• 🖼️ 【解像度適応＆黒画面防止】：2600px 超過画像の安全ダウンサンプリングと低解像度画像の補間拡大で認識率向上；");
		Add("Tab4_Ms_173b8_P5", "• 📄 【智能版面结构重建引擎】：基于词块空间几何信息，自动还原自然段落空行、同一长句智能平滑合并断行、保留表格分栏间隙，并消除汉字间误插空格。", "• 📄 【智慧版面結構重建引擎】：基於詞塊空間幾何資訊，自動還原自然段落空行、同一長句智慧平滑合併斷行、保留表格分欄間隙，並消除中文字間誤插空格。", "• 📄 [Intelligent Layout Reconstruction]: Restores paragraph breaks, merges broken lines smoothly based on geometry, preserves table columns, and eliminates stray spaces.", "• 📄 【インテリジェント段落再構築】：単語ブロックの幾何情報に基づき自然な段落・改行を自動復元し、表の列間隔を維持しつつ余計な空白を除去。");
		Add("Tab4_Ms_173b7_Title", "v1.7.3-beta.7 轮盘触发交互音效系统 (方案C 极低延迟)", "v1.7.3-beta.7 輪盤觸發互動音效系統 (方案C 極低延遲)", "v1.7.3-beta.7 Gesture Interactive Sound FX System (Ultra-Low Latency Plan C)", "v1.7.3-beta.7 インタラクティブ音効システム (超低遅延プラン C)");
		Add("Tab4_Ms_173b7_P1", "• 🔊 【WinMM 原生底层零延迟驱动】：基于 Windows 多媒体 API DirectWave 播放引擎，常驻后台内存消耗 0MB，单次响应时延 < 1ms；", "• 🔊 【WinMM 原生底層零延遲驅動】：基於 Windows 多媒體 API DirectWave 播放引擎，常駐背景記憶體消耗 0MB，單次回應延遲 < 1ms；", "• 🔊 [Zero-Latency WinMM Audio]: Powered by WinMM DirectWave native audio engine, 0MB RAM footprint and <1ms response latency;", "• 🔊 【WinMM ネイティブ低遅延駆動】：Windows DirectWave API で常駐メモリ消費 0MB、応答遅延 1ms 未満を実現；");
		Add("Tab4_Ms_173b7_P2", "• 🎮 【5 级完整交互闭环】：轮盘呼出、扇区划过高亮、级联展开、动作释放触发与外甩取消均配备灵动音效反馈；", "• 🎮 【5 級完整互動閉環】：輪盤呼出、扇區劃過醒目提示、二級展開、動作釋放觸發與外甩取消均配備靈動音效回饋；", "• 🎮 [5 Gesture Sound Stages]: Distinct acoustic feedback for popup, sector hover, submenu expand, action execution, and swipe cancel;", "• 🎮 【5 段階の音響フィードバック】：表示、ホバー、サブメニュー展開、アクション実行、キャンセルに心地よい音効を配置；");
		Add("Tab4_Ms_173b7_P3", "• 🎛️ 【4 款主题预设与独立音量控制】：内置机械手感、现代清脆、柔和气泡、极简短音 4 款专属音效，支持硬件级数学无失真音量调节；", "• 🎛️ 【4 款主題預設與獨立音量控制】：內建機械手感、現代清脆、柔和氣泡、極簡短音 4 款專屬音效，支援硬體級無失真音量調節；", "• 🎛️ [4 Sound Themes & Volume Control]: Built-in Mechanical, Modern Crisp, Soft Bubble, and Minimalist themes with distortion-free volume scaling;", "• 🎛️ 【4 種のテーマ＆独立音量制御】：メカニカル、クリスプ、バブル、ミニマルの 4 プリセットと歪みのない音量制御を搭載；");
		Add("Tab4_Ms_173b7_P4", "• 🛡️ 【35ms 扇区防抖闸门】：鼠标在扇区分界线微颤时自动限频防抖，彻底消除刺耳杂音。", "• 🛡️ 【35ms 扇區防抖閘門】：滑鼠在扇區分界線微顫時自動限頻防抖，徹底消除刺耳雜音。", "• 🛡️ [35ms Debounce Gate]: Rate-limiting debounce gate prevents audio flutter when cursor jitters around sector boundaries.", "• 🛡️ 【35ms チャタリング防止】：セクター境界でのマウス微小振動による連続再生ノイズを防止。");
		Add("Tab4_Ms_173b6_Title", "v1.7.3-beta.6 交互画布自由拉伸比例 & 轮盘层切换方式状态回显修复", "v1.7.3-beta.6 互動畫布自由拉伸比例 & 輪盤層切換方式狀態回顯修復", "v1.7.3-beta.6 Free Aspect Ratio Preview Canvas & Layer Switch Echo Fix", "v1.7.3-beta.6 プレビューキャンバス自由伸縮＆レイヤー切り替え状態表示修正");
		Add("Tab4_Ms_173b6_P1", "• 📐 【Tab 2 画布自适应与自由拉伸】：动作配置页（Tab 2）告别 380px 固定宽度限制，默认采用 1.15:1 优雅自适应比例，并在右下角增加自由拖拽调整手柄；", "• 📐 【Tab 2 畫布自適應與自由拉伸】：動作設定頁告別 380px 固定寬度限制，預設採用 1.15:1 自適應比例，右下角增加自由拖曳手柄；", "• 📐 [Adaptive Preview Canvas]: Tab 2 replaces fixed 380px width with an adaptive 1.15:1 layout and bottom-right drag handle for custom sizing;", "• 📐 【キャンバス自由伸縮】：固定幅を廃止し 1.15:1 適応比率とドラッグハンドルによる直感的なサイズ変更に対応；");
		Add("Tab4_Ms_173b6_P2", "• 🔄 【轮盘层切换方式双向回显修复】：多层轮盘工具栏切换模式升级为现代化交互下拉框，彻底修复保存为 Tab 键切换后 UI 依然错误回显为“滚轮切换”的属性映射与通知缺陷，实现配置加载与切换的双向状态完全同步。", "• 🔄 【輪盤層切換方式雙向回顯修復】：多層輪盤工具列切換模式升級為下拉選單，徹底修復儲存為 Tab 鍵切換後 UI 依然錯誤回顯為「滾輪切換」的缺陷；", "• 🔄 [Layer Switch State Synchronization]: Replaced toggle with a modern ComboBox, fixing two-way state binding where Tab switching previously displayed as scroll wheel.", "• 🔄 【レイヤー切り替え表示同期】：Tab キー切り替え保存後にスクロールと誤表示されるバインディング不具合を解消。");
		Add("Tab4_Ms_173b5_Title", "v1.7.3-beta.5 唤出一二级轮盘全展开 & 开箱高颜值默认配置友好优化", "v1.7.3-beta.5 喚出一二級輪盤全展開 & 開箱高顏值預設設定友好最佳化", "v1.7.3-beta.5 Simultaneous Sub-Ring Expansion & Out-of-the-Box Visual Defaults", "v1.7.3-beta.5 サブホイール同時展開＆高品位デフォルト設定");
		Add("Tab4_Ms_173b5_P1", "• 🌟 【一二级轮盘唤出直展】：在多级轮盘与外圈子环形态下新增「唤出时直接同时展开一二级轮盘」开关，轮盘唤出时全方位子环直接同步呈现，无需拖拽即刻清晰感知并直选子动作；", "• 🌟 【一二級輪盤喚出直展】：新增「喚出時直接同時展開一二級輪盤」開關，喚出時全方位子環直接同步呈現，無需拖曳即可直選子動作；", "• 🌟 [Auto-Expand Sub-Rings]: Added 'Auto-Expand Sub-Rings' toggle to present primary and sub-rings simultaneously upon activation;", "• 🌟 【サブホイール同時展開】：呼び出し時に全方位のサブリングを同時に展開し、即座にサブアクションを選択可能；");
		Add("Tab4_Ms_173b5_P2", "• 🎨 【开箱新手默认友好性】：全面重塑新用户首次进入的默认轮盘参数与配色：默认启用液态毛玻璃 + 浅色模式 + 纯图标居中高质感布局，四象限丰富常用子动作一览无余；", "• 🎨 【開箱新手預設友好性】：全面重塑新使用者首次進入的預設輪盤參數與配色：預設啟用液態毛玻璃 + 淺色模式 + 純圖示居中版面配置；", "• 🎨 [Visual Defaults]: Redesigned out-of-the-box defaults to Glassmorphism Light theme with clean icon layouts across all four quadrants;", "• 🎨 【高品位デフォルト設定】：初期状態をフロストガラス＋ライトモード＋中央アイコンの洗練されたレイアウトに刷新；");
		Add("Tab4_Ms_173b5_P3", "• ⚡ 【子环极速命中与响应】：深度优化多子环常开状态下的极坐标扇区命中与动态高亮，与父级扇区无缝联动，保持 60/120 FPS 丝滑微动效。;", "• ⚡ 【子環極速命中與回應】：深度最佳化多子環常開狀態下的極座標扇區命中與動態醒目提示，保持 60/120 FPS 絲滑微動效；", "• ⚡ [Fluid Sub-Ring Hit-Testing]: Optimized polar sector hit-testing when sub-rings are constantly open, maintaining solid 60/120 FPS animations;", "• ⚡ 【超高速サブリング判定】：サブリング常時展開時の極座標ヒットテストを最適化し、安定した 60/120 FPS を維持；");
		Add("Tab4_Ms_173b5_P4", "• 🏷️ 【完整预发布版本标识】：侧边栏、关于页、更新状态、托盘菜单与启动日志统一显示 v1.7.3-beta.5，并自动隐藏 SDK 附加的提交哈希。", "• 🏷️ 【完整預發布版本標識】：側邊欄、關於頁、更新狀態、系統匣功能表統一顯示完整版本號，自動隱藏提交雜湊。", "• 🏷️ [Clean Version Labels]: Unified version displays across sidebar, about page, and tray menu without raw commit hashes.", "• 🏷️ 【バージョン表記統一】：サイドバー、アバウト、トレイメニューで完全なバージョン番号を一貫して表示。");
		Add("Tab4_Ms_173b4_Title", "v1.7.3-beta.4 内存按需轻量驻留深度优化 & 静默更新自愈守护", "v1.7.3-beta.4 記憶體按需輕量駐留深度最佳化 & 靜默更新自我修復守護", "v1.7.3-beta.4 On-Demand Lightweight Memory Optimization & Silent Update Healing", "v1.7.3-beta.4 オンデマンドメモリ最適化＆サイレント更新保護");
		Add("Tab4_Ms_173b4_P1", "• 🚀 【任务栏预取按需调度】：轮盘仅在当前方案包含 SwitchWindow/Taskbar/Tile 窗口调度动作时才启动 UIAutomation 任务栏预取，杜绝常规手势下的句柄与线程泄漏；", "• 🚀 【工作列預先擷取按需排程】：輪盤僅在目前方案包含視窗排程動作時才啟動 UIAutomation 工作列預先擷取，杜絕常規手勢下的控制代碼與執行緒流失；", "• 🚀 [On-Demand Taskbar Prefetch]: UIAutomation taskbar polling is only initialized when window actions are assigned, preventing handle leaks;", "• 🚀 【タスクバーオンデマンド取得】：ウィンドウ切り替えアクションを含む場合のみ UIAutomation を初期化しハンドルリークを防止；");
		Add("Tab4_Ms_173b4_P2", "• 🛡️ 【自动更新与提权静默自愈】：彻底修复自动更新和以管理员提权重启后未带 --silent 参数误弹设置窗口导致内存驻留 160MB+ 的问题，实现真正的后台零干扰静默秒启；", "• 🛡️ 【自動更新與提升權限靜默自我修復】：徹底修復自動更新和以管理員提升權限重啟後未帶 --silent 參數誤彈設定視窗導致記憶體駐留過大的問題，實現真正的後台零干擾靜默秒啟；", "• 🛡️ [Silent Auto-Relaunch]: Fixed update and elevation restarts to strictly respect the --silent parameter, staying at <20MB background RAM;", "• 🛡️ 【サイレント自動再起動】：アップデートおよび権限昇格時に --silent を維持し、バックグラウンド 20MB 未満での起動を保証；");
		Add("Tab4_Ms_173b4_P3", "• 🧹 【旧版配置 Base64 自动洗涤】：配置加载阶段智能识别并清除旧版本遗留的 Base64 嵌入数据，规避大对象堆 (LOH) 碎片积压；", "• 🧹 【舊版設定 Base64 自動清洗】：設定載入階段智慧辨識並清除舊版本遺留的 Base64 嵌入資料，規避大物件堆疊 (LOH) 碎片積壓；", "• 🧹 [Base64 Purge on Load]: Automatically strips legacy Base64 image blobs during config load to prevent Large Object Heap (LOH) fragmentation;", "• 🧹 【Base64 クリーニング】：設定読み込み時に古い Base64 画像データを自動除去し、LOH 断片化を防止；");
		Add("Tab4_Ms_173b4_P4", "• 🍃 【设置窗口释放深度修剪】：控制台关闭 30 秒进入延迟回收阶段时，强制执行工作集归还与内存整理，彻底回落至 15MB~30MB 极致轻量后台基准。", "• 🍃 【設定視窗釋放深度修剪】：控制台關閉 30 秒進入延遲回收階段時，強制執行工作集歸還與記憶體整理，徹底回落至 15MB~30MB 極致輕量背景基準。", "• 🍃 [Deferred Console GC & Trim]: Releases console resources after 30 seconds idle, shrinking working set down to 15MB~30MB.", "• 🍃 【30秒遅延メモリ解放】：コンソールを閉じて 30 秒後にメモリを強制トリミングし、15MB〜30MB の軽量待機状態へ復元。");
		Add("Tab4_Ms_173b3_Title", "历史版本一键回退 & LOH 碎片根除 & 图标并发冻结缓存", "历史版本一键回復 & LOH 碎片根除 & 圖示并发冻结缓存", "历史版本一键回退 & LOH 碎片根除 & 图标并发冻结缓存", "历史版本一键回退 & LOH 碎片根除 & 图标并发冻结缓存");
		Add("Tab4_Ms_173b3_Desc", "Beta 通道 5 版本 / Stable 通道 2 版本回退池；彻底移除 Base64 图标数据模型；全局图标 Freezable.Freeze 缓存。", "Beta 通道 5 版本 / Stable 通道 2 版本回復池；彻底移除 Base64 圖示数据模型；全局圖示 Freezable.Freeze 缓存。", "Beta 通道 5 版本 / Stable 通道 2 版本回退池；彻底移除 Base64 图标数据模型；全局图标 Freezable.Freeze 缓存。", "Beta 通道 5 版本 / Stable 通道 2 版本回退池；彻底移除 Base64 图标数据模型；全局图标 Freezable.Freeze 缓存。");
		Add("Tab4_Ms_173b2_Title", "全按键长按原地呼出 & 黑名单快捷键穿透修复 & 物理修饰键守卫", "全按键长按原地呼出 & 黑名单快捷鍵穿透修復 & 物理修饰键守卫", "全按键长按原地呼出 & 黑名单快捷键穿透修复 & 物理修饰键守卫", "全按键长按原地呼出 & 黑名单快捷键穿透修复 & 物理修饰键守卫");
		Add("Tab4_Ms_173b2_Desc", "键盘单键/鼠标侧键/中键长按原地呼出轮盘；彻底根治 Maya 等黑名单中组合快捷键失效；GetAsyncKeyState 物理探测守卫。", "键盘单键/鼠标侧键/中键长按原地呼出轮盘；彻底根治 Maya 等黑名单中组合快捷鍵失效；GetAsyncKeyState 物理探测守卫。", "键盘单键/鼠标侧键/中键长按原地呼出轮盘；彻底根治 Maya 等黑名单中组合快捷键失效；GetAsyncKeyState 物理探测守卫。", "键盘单键/鼠标侧键/中键长按原地呼出轮盘；彻底根治 Maya 等黑名单中组合快捷键失效；GetAsyncKeyState 物理探测守卫。");
		Add("Tab4_Ms_172b5_Title", "早期配置无损自愈导入 & 自定义贴图与程序图标内嵌记忆 & 极坐标扇区守护", "早期配置无损自愈导入 & 自定义贴图与程式圖示内嵌记忆 & 极坐标扇區守护", "早期配置无损自愈导入 & 自定义贴图与程序图标内嵌记忆 & 极坐标扇区守护", "早期配置无损自愈导入 & 自定义贴图与程序图标内嵌记忆 & 极坐标扇区守护");
		Add("Tab4_Ms_172b5_Desc", "全面修复旧版配置导入扇区丢失与篡改；配置文件内嵌 Base64 图标记忆；极坐标几何对齐守护；全局继承自愈与 ESC 退出优化。", "全面修復旧版配置导入扇區丢失与篡改；配置文件内嵌 Base64 圖示记忆；极坐标几何对齐守护；全局继承自愈与 ESC 退出最佳化。", "全面修复旧版配置导入扇区丢失与篡改；配置文件内嵌 Base64 图标记忆；极坐标几何对齐守护；全局继承自愈与 ESC 退出优化。", "全面修复旧版配置导入扇区丢失与篡改；配置文件内嵌 Base64 图标记忆；极坐标几何对齐守护；全局继承自愈与 ESC 退出优化。");
		Add("Tab4_Ms_172b2_Title", "扇区全局方案级联继承 & 多屏跨缩放Bug根治 & 幽灵虚影消除 & 底层防丢键优化", "扇區全局方案级联继承 & 多屏跨缩放Bug根治 & 幽灵虚影消除 & 底层防丢键最佳化", "扇区全局方案级联继承 & 多屏跨缩放Bug根治 & 幽灵虚影消除 & 底层防丢键优化", "扇区全局方案级联继承 & 多屏跨缩放Bug根治 & 幽灵虚影消除 & 底层防丢键优化");
		Add("Tab4_Ms_172b2_Desc", "未配置槽位自动级联继承全局方案；彻底根治多屏混合 DPI 轮盘巨大截断缺陷；解除外甩残影；HWND 缓存防护低级钩子防丢键。", "未配置槽位自动级联继承全局方案；彻底根治多屏混合 DPI 轮盘巨大截断缺陷；解除外甩残影；HWND 缓存防护低级钩子防丢键。", "未配置槽位自动级联继承全局方案；彻底根治多屏混合 DPI 轮盘巨大截断缺陷；解除外甩残影；HWND 缓存防护低级钩子防丢键。", "未配置槽位自动级联继承全局方案；彻底根治多屏混合 DPI 轮盘巨大截断缺陷；解除外甩残影；HWND 缓存防护低级钩子防丢键。");
		Add("Tab4_Ms_171_Title", "连续步进快捷键支持 & 外圈子环全展开 & 触发防冲突提示", "连续步进快捷鍵支持 & 外圈子环全展开 & 触发防冲突提示", "连续步进快捷键支持 & 外圈子环全展开 & 触发防冲突提示", "连续步进快捷键支持 & 外圈子环全展开 & 触发防冲突提示");
		Add("Tab4_Ms_171_Desc", "快捷键引擎支持多键顺序步进派发（如 Alt+H+V+F）；外圈子环画布默认全展开并精简一二级按钮；触发按键增加防冲突提示。", "快捷鍵引擎支持多键顺序步进派发（如 Alt+H+V+F）；外圈子环画布預設全展开并精简一二级按钮；触发按键增加防冲突提示。", "快捷键引擎支持多键顺序步进派发（如 Alt+H+V+F）；外圈子环画布默认全展开并精简一二级按钮；触发按键增加防冲突提示。", "快捷键引擎支持多键顺序步进派发（如 Alt+H+V+F）；外圈子环画布默认全展开并精简一二级按钮；触发按键增加防冲突提示。");
		Add("Tab4_Ms_170_Title", "简单模式层级精简 & 中心核圆动作图标呼出修复 & 体验提纯", "简单模式层级精简 & 中心核圆动作圖示呼出修復 & 体验提纯", "简单模式层级精简 & 中心核圆动作图标呼出修复 & 体验提纯", "简单模式层级精简 & 中心核圆动作图标呼出修复 & 体验提纯");
		Add("Tab4_Ms_170_Desc", "鼠标手势卡片、外甩取消动作、边缘防溢出与紧凑全览列表分段切换全面放入高级模式；彻底修复实际呼出轮盘中心核圆配置图标不显示缺陷；简单模式锁定画布精调。", "鼠标手势卡片、外甩取消动作、边缘防溢出与紧凑全览列表分段切换全面放入高级模式；彻底修復实际呼出轮盘中心核圆配置圖示不显示缺陷；简单模式锁定画布精调。", "鼠标手势卡片、外甩取消动作、边缘防溢出与紧凑全览列表分段切换全面放入高级模式；彻底修复实际呼出轮盘中心核圆配置图标不显示缺陷；简单模式锁定画布精调。", "鼠标手势卡片、外甩取消动作、边缘防溢出与紧凑全览列表分段切换全面放入高级模式；彻底修复实际呼出轮盘中心核圆配置图标不显示缺陷；简单模式锁定画布精调。");
		Add("Tab4_Ms_169_Title", "外部图标继承加固 & 防误触配置持久化保护 & 图标回显联动", "外部圖示继承加固 & 防误触配置持久化保护 & 圖示回显联动", "外部图标继承加固 & 防误触配置持久化保护 & 图标回显联动", "外部图标继承加固 & 防误触配置持久化保护 & 图标回显联动");
		Add("Tab4_Ms_169_Desc", "彻底解决重启后扇区图标变为 Win 图标问题；加固开机自启与关机保存门禁，杜绝全屏防误触与外甩取消设置被冲掉；列表与编辑区支持关联图标实时预览；平铺预设不再覆写已有图标。", "彻底解决重启后扇區圖示变为 Win 圖示问题；加固开机自启与关机保存门禁，杜绝全屏防误触与外甩取消設定被冲掉；列表与编辑区支持关联圖示实时预览；平铺预设不再覆写已有圖示。", "彻底解决重启后扇区图标变为 Win 图标问题；加固开机自启与关机保存门禁，杜绝全屏防误触与外甩取消设置被冲掉；列表与编辑区支持关联图标实时预览；平铺预设不再覆写已有图标。", "彻底解决重启后扇区图标变为 Win 图标问题；加固开机自启与关机保存门禁，杜绝全屏防误触与外甩取消设置被冲掉；列表与编辑区支持关联图标实时预览；平铺预设不再覆写已有图标。");
		Add("Tab4_Ms_168_Title", "全新星盘图标 & 二级轮盘方位对齐 & 一二级配置联动 & 弹性字号 & 快捷键增强 & 贡献者致谢", "全新星盘圖示 & 二级轮盘方位对齐 & 一二级配置联动 & 弹性字号 & 快捷鍵增强 & 贡献者致谢", "全新星盘图标 & 二级轮盘方位对齐 & 一二级配置联动 & 弹性字号 & 快捷键增强 & 贡献者致谢", "全新星盘图标 & 二级轮盘方位对齐 & 一二级配置联动 & 弹性字号 & 快捷键增强 & 贡献者致谢");
		Add("Tab4_Ms_168_Desc", "全新同心发光星核品牌图标；修复二级轮盘上方功能与设置相反问题；优化一级/二级配置模式切换与画布联动；轮盘扇区全面接入 Auto Font-Fit 弹性字号；快捷键拼装增加 Pause 与过滤；修复实时画布缩放；优化蜂窝扇迟滞保持手感。", "全新同心发光星核品牌圖示；修復二级轮盘上方功能与設定相反问题；最佳化一级/二级配置模式切换与画布联动；轮盘扇區全面接入 Auto Font-Fit 弹性字号；快捷鍵拼装增加 Pause 与过滤；修復实时画布缩放；最佳化蜂窝扇迟滞保持手感。", "全新同心发光星核品牌图标；修复二级轮盘上方功能与设置相反问题；优化一级/二级配置模式切换与画布联动；轮盘扇区全面接入 Auto Font-Fit 弹性字号；快捷键拼装增加 Pause 与过滤；修复实时画布缩放；优化蜂窝扇迟滞保持手感。", "全新同心发光星核品牌图标；修复二级轮盘上方功能与设置相反问题；优化一级/二级配置模式切换与画布联动；轮盘扇区全面接入 Auto Font-Fit 弹性字号；快捷键拼装增加 Pause 与过滤；修复实时画布缩放；优化蜂窝扇迟滞保持手感。");
		Add("Tab4_Ms_167_Title", "原生 OCR 修复 & 多屏多分辨率唤起对齐 & 核心圆死区滑块 & 二级单扇区聚焦预览", "原生 OCR 修復 & 多屏多分辨率唤起对齐 & 核心圆死区滑块 & 二级单扇區聚焦预览", "原生 OCR 修复 & 多屏多分辨率唤起对齐 & 核心圆死区滑块 & 二级单扇区聚焦预览", "原生 OCR 修复 & 多屏多分辨率唤起对齐 & 核心圆死区滑块 & 二级单扇区聚焦预览");
		Add("Tab4_Ms_167_Desc", "修复原生 OCR 异常与语言包感知；引入 ScreenHelper 彻底解决跨屏混合 DPI 轮盘唤起漂移；多级轮盘设置迁入 Tab 3 释放动作区空间并增加核心圆死区灵敏度滑块；二级轮盘外观预览改为单扇区展开消除遮挡。", "修復原生 OCR 异常与语言包感知；引入 ScreenHelper 彻底解决跨屏混合 DPI 轮盘唤起漂移；多级轮盘設定迁入 Tab 3 释放动作区空间并增加核心圆死区灵敏度滑块；二级轮盘外观预览改为单扇區展开消除遮挡。", "修复原生 OCR 异常与语言包感知；引入 ScreenHelper 彻底解决跨屏混合 DPI 轮盘唤起漂移；多级轮盘设置迁入 Tab 3 释放动作区空间并增加核心圆死区灵敏度滑块；二级轮盘外观预览改为单扇区展开消除遮挡。", "修复原生 OCR 异常与语言包感知；引入 ScreenHelper 彻底解决跨屏混合 DPI 轮盘唤起漂移；多级轮盘设置迁入 Tab 3 释放动作区空间并增加核心圆死区灵敏度滑块；二级轮盘外观预览改为单扇区展开消除遮挡。");
		Add("Tab4_Ms_158_Title", "Win10 计算器修复 & PrintScreen 截屏 & 启动 Explorer 修复 & 运行日志记录", "Win10 计算器修復 & PrintScreen 截屏 & 启动 Explorer 修復 & 运行日志记录", "Win10 计算器修复 & PrintScreen 截屏 & 启动 Explorer 修复 & 运行日志记录", "Win10 计算器修复 & PrintScreen 截屏 & 启动 Explorer 修复 & 运行日志记录");
		Add("Tab4_Ms_158_Desc", "修复 Win10 计算器呼出；支持单独 PrintScreen 截屏热键；修复启动 explorer.exe；新增系统异步运行日志与一键查看诊断。", "修復 Win10 计算器呼出；支持单独 PrintScreen 截屏热键；修復启动 explorer.exe；新增系统异步运行日志与一键查看诊断。", "修复 Win10 计算器呼出；支持单独 PrintScreen 截屏热键；修复启动 explorer.exe；新增系统异步运行日志与一键查看诊断。", "修复 Win10 计算器呼出；支持单独 PrintScreen 截屏热键；修复启动 explorer.exe；新增系统异步运行日志与一键查看诊断。");
		Add("Tab4_Ms_157_Title", "轮盘动作排序 & 方位指示器 & 独立钩子线程 & 手势拖拽流畅度优化", "轮盘动作排序 & 方位指示器 & 独立钩子线程 & 手势拖拽流畅度最佳化", "轮盘动作排序 & 方位指示器 & 独立钩子线程 & 手势拖拽流畅度优化", "轮盘动作排序 & 方位指示器 & 独立钩子线程 & 手势拖拽流畅度优化");
		Add("Tab4_Ms_157_Desc", "新增动作列表 ▲/▼ 排序；新增扇区方位微缩指示器；独立低级钩子后台线程与高频更新调度；二级外径与配置即时导入全面融合。", "新增动作列表 ▲/▼ 排序；新增扇區方位微缩指示器；独立低级钩子后台线程与高频更新调度；二级外径与配置即时导入全面融合。", "新增动作列表 ▲/▼ 排序；新增扇区方位微缩指示器；独立低级钩子后台线程与高频更新调度；二级外径与配置即时导入全面融合。", "新增动作列表 ▲/▼ 排序；新增扇区方位微缩指示器；独立低级钩子后台线程与高频更新调度；二级外径与配置即时导入全面融合。");
		Add("Tab4_Ms_156_Title", "运行命令终端选择 & 蜂巢平滑圆角 & 快捷键拼装修复 & 配色无损备份", "运行命令终端选择 & 蜂巢平滑圆角 & 快捷鍵拼装修復 & 配色无损备份", "运行命令终端选择 & 蜂巢平滑圆角 & 快捷键拼装修复 & 配色无损备份", "运行命令终端选择 & 蜂巢平滑圆角 & 快捷键拼装修复 & 配色无损备份");
		Add("Tab4_Ms_156_Desc", "新增运行命令动作支持CMD/PS/WSL及静默模式；蜂巢六边形平滑圆角调节；快捷键构建器预设芯片；全量配色导出备份。", "新增运行命令动作支持CMD/PS/WSL及静默模式；蜂巢六边形平滑圆角调节；快捷鍵构建器预设芯片；全量配色导出备份。", "新增运行命令动作支持CMD/PS/WSL及静默模式；蜂巢六边形平滑圆角调节；快捷键构建器预设芯片；全量配色导出备份。", "新增运行命令动作支持CMD/PS/WSL及静默模式；蜂巢六边形平滑圆角调节；快捷键构建器预设芯片；全量配色导出备份。");
		Add("Tab4_Ms_145_Title", "多级轮盘与级联子菜单 & 智能呼出收起切换 & 扇区平滑圆角算法重构", "多级轮盘与级联子菜单 & 智能呼出收起切换 & 扇區平滑圆角算法重构", "多级轮盘与级联子菜单 & 智能呼出收起切换 & 扇区平滑圆角算法重构", "多级轮盘与级联子菜单 & 智能呼出收起切换 & 扇区平滑圆角算法重构");
		Add("Tab4_Ms_145_Desc", "支持二级子动作级联展开与多级轮盘开关；应用/文件夹/系统工具智能前台最小化切换；四角圆弧相切倒角算法重构。", "支持二级子动作级联展开与多级轮盘开关；应用/文件夹/系统工具智能前台最小化切换；四角圆弧相切倒角算法重构。", "支持二级子动作级联展开与多级轮盘开关；应用/文件夹/系统工具智能前台最小化切换；四角圆弧相切倒角算法重构。", "支持二级子动作级联展开与多级轮盘开关；应用/文件夹/系统工具智能前台最小化切换；四角圆弧相切倒角算法重构。");
		Add("Tab4_Ms_144_Title", "中心图案缩放平移 & OBS等带参应用启动修复 & 三挡响应动画调速", "中心图案缩放平移 & OBS等带参应用启动修復 & 三挡响应动画调速", "中心图案缩放平移 & OBS等带参应用启动修复 & 三挡响应动画调速", "中心图案缩放平移 & OBS等带参应用启动修复 & 三挡响应动画调速");
		Add("Tab4_Ms_144_Desc", "中心图案支持视口内缩放与偏移调节；修复外部程序工作目录与启动失败；新增优雅/流畅/快速三挡响应速度。", "中心图案支持视口内缩放与偏移调节；修復外部程式工作目录与启动失败；新增优雅/流畅/快速三挡响应速度。", "中心图案支持视口内缩放与偏移调节；修复外部程序工作目录与启动失败；新增优雅/流畅/快速三挡响应速度。", "中心图案支持视口内缩放与偏移调节；修复外部程序工作目录与启动失败；新增优雅/流畅/快速三挡响应速度。");
		Add("Tab4_Ms_139_Title", "打开文件夹动作类型 & 全局界面语言统一性强化", "打开文件夹动作类型 & 全局界面语言统一性强化", "打开文件夹动作类型 & 全局界面语言统一性强化", "打开文件夹动作类型 & 全局界面语言统一性强化");
		Add("Tab4_Ms_139_Desc", "新增打开文件夹专属动作类型与目录选择器，消除 Raw 字典键与混排语言，全弹窗国际化深度适配。", "新增打开文件夹专属动作类型与目录选择器，消除 Raw 字典键与混排语言，全弹窗国际化深度适配。", "新增打开文件夹专属动作类型与目录选择器，消除 Raw 字典键与混排语言，全弹窗国际化深度适配。", "新增打开文件夹专属动作类型与目录选择器，消除 Raw 字典键与混排语言，全弹窗国际化深度适配。");
		Add("Tab4_Ms_138_Title", "StarPie 品牌视觉升级 & 原生圆角星轨图标 & 托盘图标自愈修复", "StarPie 品牌视觉升级 & 原生圆角星轨圖示 & 托盘圖示自愈修復", "StarPie 品牌视觉升级 & 原生圆角星轨图标 & 托盘图标自愈修复", "StarPie 品牌视觉升级 & 原生圆角星轨图标 & 托盘图标自愈修复");
		Add("Tab4_Ms_138_Desc", "升级 StarPie 品牌，修复任务栏圆角与托盘图标缺失，重构控制台侧边栏 Logo 与排版。", "升级 StarPie 品牌，修復任务栏圆角与托盘圖示缺失，重构控制台侧边栏 Logo 与排版。", "升级 StarPie 品牌，修复任务栏圆角与托盘图标缺失，重构控制台侧边栏 Logo 与排版。", "升级 StarPie 品牌，修复任务栏圆角与托盘图标缺失，重构控制台侧边栏 Logo 与排版。");
		Add("Tab4_Ms_134_Title", "纯白主题画布渲染优化 & 全局设置记忆 & 极简内存瘦身", "纯白主题画布渲染最佳化 & 全局設定记忆 & 极简記憶體瘦身", "纯白主题画布渲染优化 & 全局设置记忆 & 极简内存瘦身", "纯白主题画布渲染优化 & 全局设置记忆 & 极简内存瘦身");
		Add("Tab4_Ms_134_Desc", "优化纯白主题下实时画布背景色彩，增加关闭与退出自动记忆功能，大幅优化内存占用至15-25MB。", "最佳化纯白主题下实时画布背景色彩，增加关闭与退出自动记忆功能，大幅最佳化記憶體占用至15-25MB。", "优化纯白主题下实时画布背景色彩，增加关闭与退出自动记忆功能，大幅优化内存占用至15-25MB。", "优化纯白主题下实时画布背景色彩，增加关闭与退出自动记忆功能，大幅优化内存占用至15-25MB。");
		Add("Tab4_Ms_133_Title", "4/12键方位全流程适配修复 & 扇区切削形态精简", "4/12键方位全流程适配修復 & 扇區切削形态精简", "4/12键方位全流程适配修复 & 扇区切削形态精简", "4/12键方位全流程适配修复 & 扇区切削形态精简");
		Add("Tab4_Ms_133_Desc", "修复4键/12键切换无响应问题，支持动态自适应缩放与钟表映射，精简保留四大核心高质感形态。", "修復4键/12键切换无响应问题，支持动态自适应缩放与钟表映射，精简保留四大核心高质感形态。", "修复4键/12键切换无响应问题，支持动态自适应缩放与钟表映射，精简保留四大核心高质感形态。", "修复4键/12键切换无响应问题，支持动态自适应缩放与钟表映射，精简保留四大核心高质感形态。");
		Add("Tab4_Ms_132_Title", "轮盘图标大小调节 & 光弧极简重构", "轮盘圖示大小调节 & 光弧极简重构", "轮盘图标大小调节 & 光弧极简重构", "轮盘图标大小调节 & 光弧极简重构");
		Add("Tab4_Ms_132_Desc", "新增图标大小滑动微调，重构光弧同心导轨与悬浮节点，优化比例。", "新增圖示大小滑动微调，重构光弧同心导轨与悬浮节点，最佳化比例。", "新增图标大小滑动微调，重构光弧同心导轨与悬浮节点，优化比例。", "新增图标大小滑动微调，重构光弧同心导轨与悬浮节点，优化比例。");
		Add("Tab4_Ms_131_Title", "新增液态水滴与光弧轨道形态 & 文字字号调节", "新增液态水滴与光弧轨道形态 & 文字字号调节", "新增液态水滴与光弧轨道形态 & 文字字号调节", "新增液态水滴与光弧轨道形态 & 文字字号调节");
		Add("Tab4_Ms_131_Desc", "精简合并圆角胶囊形态，新增液态水滴与光弧轨道形态，支持自定义文字大小。", "精简合并圆角胶囊形态，新增液态水滴与光弧轨道形态，支持自定义文字大小。", "精简合并圆角胶囊形态，新增液态水滴与光弧轨道形态，支持自定义文字大小。", "精简合并圆角胶囊形态，新增液态水滴与光弧轨道形态，支持自定义文字大小。");
		Add("Tab4_Ms_130_Title", "主题视觉深度重塑 & 自定义配色预设保存", "主题视觉深度重塑 & 自定义配色预设保存", "主题视觉深度重塑 & 自定义配色预设保存", "主题视觉深度重塑 & 自定义配色预设保存");
		Add("Tab4_Ms_130_Desc", "重塑经典、极简与毛玻璃主题，支持保存多套自定义十六进制颜色预设。", "重塑经典、极简与毛玻璃主题，支持保存多套自定义十六进制颜色预设。", "重塑经典、极简与毛玻璃主题，支持保存多套自定义十六进制颜色预设。", "重塑经典、极简与毛玻璃主题，支持保存多套自定义十六进制颜色预设。");
		Add("TipToggleCustomSoundConfig", "展开或关闭自定义交互音效调音台", "展開或關閉自訂互動音效調音台", "Expand or collapse custom interactive sound mixer", "カスタムインタラクティブ音効ミキサーを展開または折りたたむ");
		Add("TipSoundPreview", "依次播放当前主题的唤出、划过、展开、确认与取消音效", "依序播放目前主題的喚出、劃過、展開、確認與取消音效", "Sequentially preview popup, hover, expand, trigger, and cancel sounds", "現在のテーマの表示、ホバー、展開、確認、キャンセル音をプレビュー");
		Add("TipBrowseBlacklist", "从已安装软件列表中快速选择要添加的程序", "從已安裝軟體清單中快速選取要新增的程式", "Quickly select an app from installed programs", "インストール済みアプリ一覧から追加するプログラムを選択");
		Add("TipAddBlacklist", "将输入框中的进程名称加入名单", "將輸入框中的處理程序名稱加入名單", "Add process name from input box to list", "入力ボックスのプロセス名をリストに追加");
		Add("TipDuplicateProfile", "将当前方案的所有动作与多层配置复制为新程序方案", "將目前方案的所有動作與多層設定複製為新程式方案", "Duplicate all actions and multi-layer settings to a new profile", "現在の全アクションと多層設定を新しいプロファイルとして複製");
		Add("TipAddProfile", "从已安装软件或开始菜单中选择程序创建专属配置", "從已安裝軟體或開始功能表中選取程式建立專屬設定", "Select a program from installed software or Start Menu to create dedicated profile", "インストール済みアプリまたはスタートメニューから専用プロファイルを作成");
		Add("TipAddCustomProfile", "自定义命名创建新的轮盘配置方案", "自訂命名建立新的輪盤設定方案", "Create a new radial profile with custom name", "任意の名前で新しいホイールプロファイルを作成");
		Add("IconLayoutModeTitleText", "排版模式:", "排版模式:", "Layout Mode:", "レイアウトモード:");
		Add("LayoutModeItemInherit", "跟随全局默认 (Inherit Global)", "跟隨全域預設 (Inherit Global)", "Inherit Global Default", "グローバル既定に従う (Inherit Global)");
		Add("LayoutModeItemBoth", "图标 + 文字 (双行居中)", "圖示 + 文字 (雙行置中)", "Icon + Text (Centered)", "アイコン + テキスト (中央配置)");
		Add("LayoutModeItemIconOnly", "仅显示图标 (极大化居中)", "僅顯示圖示 (極大化置中)", "Icon Only (Maximized)", "アイコンのみ (最大化)");
		Add("LayoutModeItemTextOnly", "仅显示文字 (纯文字居中)", "僅顯示文字 (純文字置中)", "Text Only (Centered)", "テキストのみ (中央配置)");
		Add("FontSystemDefault", "🖥️ 系统默认 (Microsoft YaHei UI / Segoe UI)", "🖥️ 系統預設 (Microsoft JhengHei UI / Segoe UI)", "🖥️ System Default", "🖥️ システム既定 (Yu Gothic UI / Segoe UI)");
		Add("FontMicrosoftYaHei", "🔤 微软雅黑 (Microsoft YaHei UI)", "🔤 微軟正黑體 (Microsoft JhengHei UI)", "🔤 Microsoft YaHei", "🔤 メイリオ / 游ゴシック (Yu Gothic UI)");
		Add("FontSegoeUI", "🔤 Segoe UI (Windows Fluent)", "🔤 Segoe UI (Windows Fluent)", "🔤 Segoe UI", "🔤 Segoe UI (Windows Fluent)");
		Add("FontHarmonyOS", "🔤 鸿蒙字体 (HarmonyOS Sans SC)", "🔤 鴻蒙字型 (HarmonyOS Sans TC)", "🔤 HarmonyOS Sans", "🔤 HarmonyOS Sans");
		Add("FontPingFang", "🔤 苹方字体 (PingFang SC)", "🔤 蘋方字型 (PingFang TC)", "🔤 PingFang", "🔤 PingFang");
		Add("FontMiSans", "🔤 小米兰亭 (MiSans)", "🔤 小米蘭亭 (MiSans)", "🔤 MiSans", "🔤 MiSans");
		Add("FontSourceHanSans", "🔤 思源黑体 (Source Han Sans SC)", "🔤 思源黑體 (Source Han Sans TC)", "🔤 Source Han Sans", "🔤 源ノ角ゴシック (Source Han Sans JP)");
		Add("FontInter", "🔤 Inter (Modern Sans)", "🔤 Inter (Modern Sans)", "🔤 Inter", "🔤 Inter");
		Add("FontArial", "🔤 Arial", "🔤 Arial", "🔤 Arial", "🔤 Arial");
		Add("FontSimHei", "🔤 黑体 (SimHei)", "🔤 黑體 (SimHei)", "🔤 SimHei", "🔤 SimHei");
		Add("FontKaiTi", "🔤 楷体 (KaiTi)", "🔤 楷體 (KaiTi)", "🔤 KaiTi", "🔤 KaiTi");
		Add("FontFangSong", "🔤 仿宋 (FangSong)", "🔤 仿宋 (FangSong)", "🔤 FangSong", "🔤 FangSong");
		Add("FontMonospace", "🔤 等宽代码体 (Consolas / Cascadia)", "🔤 等寬程式碼字型 (Consolas / Cascadia)", "🔤 Monospace Code (Consolas / Cascadia)", "🔤 等幅コードフォント (Consolas / Cascadia)");
		Add("FontJetBrainsMono", "🔤 JetBrains Mono", "🔤 JetBrains Mono", "🔤 JetBrains Mono", "🔤 JetBrains Mono");
		Add("GlobalProfileDefault", "Global (全局默认)", "Global (全域預設)", "Global (Default)", "Global (グローバル既定)");
		Add("WheelLayerFmt", "第 {0} 层", "第 {0} 層", "Layer {0}", "レイヤー {0}");
		Add("DefaultConfigProfile", "默认配置", "預設配置", "Default Scheme", "既定構成");
		Add("ActiveProfilePrefix", "当前方案: ", "目前配置方案: ", "Current Scheme: ", "現在の構成スキーム: ");
		Add("CustomPresetSuffix", "(自定义预设)", "(自訂預設)", "(Custom Preset)", "(カスタムプリセット)");
		Add("SysCategory_WindowManager", "窗口管理", "視窗管理", "Window Management", "ウィンドウ管理");
		Add("SysCategory_SystemTools", "系统工具", "系統工具", "System Tools", "システムツール");
		Add("SysCategory_Media", "媒体音效", "媒體音訊", "Media & Audio", "メディア・オーディオ");
		Add("SysCategory_WebBrowser", "网页浏览", "網頁瀏覽", "Web Browsing", "ウェブ閲覧");
		Add("SysCategory_PowerControl", "电源控制", "電源控制", "Power Options", "電源制御");
		Add("SysCategory_WindowSwitcher", "窗口管理", "視窗管理", "Window Management", "ウィンドウ管理");
		Add("SysPreset_WindowSwitcher", "常驻窗口切换器 (Window Switcher / Ctrl+Alt+Tab)", "常駐視窗切換器 (Window Switcher / Ctrl+Alt+Tab)", "Window Switcher (Ctrl+Alt+Tab)", "ウィンドウ切り替え (Ctrl+Alt+Tab)");
		Add("SysPresetName_WindowSwitcher", "窗口切换", "視窗切換", "Window Switcher", "ウィンドウ切替");
		Add("SysCategory_AltTab", "窗口管理", "視窗管理", "Window Management", "ウィンドウ管理");
		Add("SysPreset_AltTab", "快速切至上一窗口 (Alt+Tab)", "快速切至上一視窗 (Alt+Tab)", "Switch to Previous Window (Alt+Tab)", "前のウィンドウに切り替え (Alt+Tab)");
		Add("SysPresetName_AltTab", "切换窗口", "切換視窗", "Switch Window", "ウィンドウ切替");
		Add("SysCategory_CloseWindow", "窗口管理", "視窗管理", "Window Management", "ウィンドウ管理");
		Add("SysPreset_CloseWindow", "关闭当前窗口 (Close / Alt+F4)", "關閉目前視窗 (Close / Alt+F4)", "Close Active Window (Alt+F4)", "現在のウィンドウを閉じる (Alt+F4)");
		Add("SysPresetName_CloseWindow", "关闭窗口", "關閉視窗", "Close Window", "ウィンドウを閉じる");
		Add("SysCategory_Minimize", "窗口管理", "視窗管理", "Window Management", "ウィンドウ管理");
		Add("SysPreset_Minimize", "最小化窗口 (Minimize / Win+Down)", "最小化視窗 (Minimize / Win+Down)", "Minimize Window (Win+Down)", "ウィンドウの最小化 (Win+Down)");
		Add("SysPresetName_Minimize", "最小化", "最小化", "Minimize", "最小化");
		Add("SysCategory_Maximize", "窗口管理", "視窗管理", "Window Management", "ウィンドウ管理");
		Add("SysPreset_Maximize", "最大化/还原 (Maximize / Win+Up)", "最大化/還原 (Maximize / Win+Up)", "Maximize/Restore (Win+Up)", "最大化/元に戻す (Win+Up)");
		Add("SysPresetName_Maximize", "最大化", "最大化", "Maximize", "最大化");
		Add("SysCategory_SnapLeft", "窗口管理", "視窗管理", "Window Management", "ウィンドウ管理");
		Add("SysPreset_SnapLeft", "左半屏贴靠 (Snap Left / Win+Left)", "左半屏貼靠 (Snap Left / Win+Left)", "Snap Left (Win+Left)", "左にスナップ (Win+Left)");
		Add("SysPresetName_SnapLeft", "靠左分屏", "靠左分屏", "Snap Left", "左スナップ");
		Add("SysCategory_SnapRight", "窗口管理", "視窗管理", "Window Management", "ウィンドウ管理");
		Add("SysPreset_SnapRight", "右半屏贴靠 (Snap Right / Win+Right)", "右半屏貼靠 (Snap Right / Win+Right)", "Snap Right (Win+Right)", "右にスナップ (Win+Right)");
		Add("SysPresetName_SnapRight", "靠右分屏", "靠右分屏", "Snap Right", "右スナップ");
		Add("SysCategory_TaskView", "窗口管理", "視窗管理", "Window Management", "ウィンドウ管理");
		Add("SysPreset_TaskView", "任务视图/多任务 (Task View / Win+Tab)", "工作檢視/多工 (Task View / Win+Tab)", "Task View (Win+Tab)", "タスクビュー (Win+Tab)");
		Add("SysPresetName_TaskView", "任务视图", "工作檢視", "Task View", "タスクビュー");
		Add("SysCategory_PrevDesktop", "窗口管理", "視窗管理", "Window Management", "ウィンドウ管理");
		Add("SysPreset_PrevDesktop", "上一虚拟桌面 (Prev Desktop)", "上一虛擬桌面 (Prev Desktop)", "Previous Virtual Desktop", "前の仮想デスクトップ");
		Add("SysPresetName_PrevDesktop", "上一桌面", "上一桌面", "Prev Desktop", "前デスクトップ");
		Add("SysCategory_NextDesktop", "窗口管理", "視窗管理", "Window Management", "ウィンドウ管理");
		Add("SysPreset_NextDesktop", "下一虚拟桌面 (Next Desktop)", "下一虛擬桌面 (Next Desktop)", "Next Virtual Desktop", "次の仮想デスクトップ");
		Add("SysPresetName_NextDesktop", "下一桌面", "下一桌面", "Next Desktop", "次デスクトップ");
		Add("SysCategory_ShowDesktop", "窗口管理", "視窗管理", "Window Management", "ウィンドウ管理");
		Add("SysPreset_ShowDesktop", "显示桌面 (Desktop / Win+D)", "顯示桌面 (Desktop / Win+D)", "Show Desktop (Win+D)", "デスクトップを表示 (Win+D)");
		Add("SysPresetName_ShowDesktop", "显示桌面", "顯示桌面", "Show Desktop", "デスクトップ表示");
		Add("SysCategory_FullScreen", "窗口管理", "視窗管理", "Window Management", "ウィンドウ管理");
		Add("SysPreset_FullScreen", "全屏切换 (Full Screen / F11)", "全螢幕切換 (Full Screen / F11)", "Toggle Full Screen (F11)", "全画面表示切替 (F11)");
		Add("SysPresetName_FullScreen", "全屏切换", "全螢幕切換", "Full Screen", "全画面表示");
		Add("SysCategory_Screenshot", "窗口管理", "視窗管理", "Window Management", "ウィンドウ管理");
		Add("SysPreset_Screenshot", "屏幕截图 (Screenshot / Win+Shift+S)", "螢幕截圖 (Screenshot / Win+Shift+S)", "Screen Snipping (Win+Shift+S)", "画面切り取り (Win+Shift+S)");
		Add("SysPresetName_Screenshot", "屏幕截图", "螢幕截圖", "Screenshot", "スクリーンショット");
		Add("SysCategory_TaskManager", "系统工具", "系統工具", "System Tools", "システムツール");
		Add("SysPreset_TaskManager", "任务管理器 (Task Manager / Ctrl+Shift+Esc)", "工作管理員 (Task Manager / Ctrl+Shift+Esc)", "Task Manager (Ctrl+Shift+Esc)", "タスクマネージャー (Ctrl+Shift+Esc)");
		Add("SysPresetName_TaskManager", "任务管理器", "工作管理員", "Task Manager", "タスクマネージャー");
		Add("SysCategory_Explorer", "系统工具", "系統工具", "System Tools", "システムツール");
		Add("SysPreset_Explorer", "文件资源管理器 (Explorer / Win+E)", "檔案總管 (Explorer / Win+E)", "File Explorer (Win+E)", "エクスプローラー (Win+E)");
		Add("SysPresetName_Explorer", "资源管理器", "檔案總管", "Explorer", "エクスプローラー");
		Add("SysCategory_OpenSettings", "系统工具", "系統工具", "System Tools", "システムツール");
		Add("SysPreset_OpenSettings", "StarPie 控制台 (StarPie Settings)", "StarPie 控制台 (StarPie Settings)", "StarPie Settings Console", "StarPie 設定コンソール");
		Add("SysPresetName_OpenSettings", "StarPie控制台", "StarPie控制台", "StarPie Settings", "StarPie設定");
		Add("SysCategory_Settings", "系统工具", "系統工具", "System Tools", "システムツール");
		Add("SysPreset_Settings", "Windows 设置 (Settings / Win+I)", "Windows 設定 (Settings / Win+I)", "Windows Settings (Win+I)", "Windows 設定 (Win+I)");
		Add("SysPresetName_Settings", "系统设置", "系統設定", "Settings", "Windows設定");
		Add("SysCategory_Calculator", "系统工具", "系統工具", "System Tools", "システムツール");
		Add("SysPreset_Calculator", "计算器 (Calculator / calc.exe)", "計算機 (Calculator / calc.exe)", "Calculator (calc.exe)", "電卓 (calc.exe)");
		Add("SysPresetName_Calculator", "计算器", "計算機", "Calculator", "電卓");
		Add("SysCategory_RunDialog", "系统工具", "系統工具", "System Tools", "システムツール");
		Add("SysPreset_RunDialog", "运行窗口 (Run / Win+R)", "執行視窗 (Run / Win+R)", "Run Dialog (Win+R)", "ファイル名を指定して実行 (Win+R)");
		Add("SysPresetName_RunDialog", "运行", "執行", "Run", "ファイル名を指定して実行");
		Add("SysCategory_WindowsSearch", "系统工具", "系統工具", "System Tools", "システムツール");
		Add("SysPreset_WindowsSearch", "系统搜索 (Search / Win+S)", "系統搜尋 (Search / Win+S)", "Windows Search (Win+S)", "Windows 検索 (Win+S)");
		Add("SysPresetName_WindowsSearch", "搜索", "搜尋", "Search", "検索");
		Add("SysCategory_QuickSearch", "系统工具", "系統工具", "System Tools", "システムツール");
		Add("SysPreset_QuickSearch", "全盘文件与程序秒搜 (Quick Finder)", "全磁碟檔案與程式秒搜 (Quick Finder)", "Quick File & App Finder", "高速ファイル・アプリ検索");
		Add("SysPresetName_QuickSearch", "快速秒搜", "快速秒搜", "Quick Finder", "クイック検索");
		Add("SysCategory_ClipboardHistory", "系统工具", "系統工具", "System Tools", "システムツール");
		Add("SysPreset_ClipboardHistory", "剪贴板历史 (Clipboard / Win+V)", "剪貼簿歷程記錄 (Clipboard / Win+V)", "Clipboard History (Win+V)", "クリップボード履歴 (Win+V)");
		Add("SysPresetName_ClipboardHistory", "剪贴板", "剪貼簿", "Clipboard", "クリップボード");
		Add("SysCategory_Lock", "系统工具", "系統工具", "System Tools", "システムツール");
		Add("SysPreset_Lock", "锁定电脑 (Lock Workstation)", "鎖定電腦 (Lock Workstation)", "Lock Workstation (Win+L)", "PCをロック (Win+L)");
		Add("SysPresetName_Lock", "锁定电脑", "鎖定電腦", "Lock PC", "PCロック");
		Add("SysCategory_VolumeUp", "媒体音效", "媒體音訊", "Media & Audio", "メディア・オーディオ");
		Add("SysPreset_VolumeUp", "音量增加 (Volume Up)", "音量增加 (Volume Up)", "Volume Up", "音量を上げる");
		Add("SysPresetName_VolumeUp", "音量加", "音量加", "Volume Up", "音量+");
		Add("SysCategory_VolumeDown", "媒体音效", "媒體音訊", "Media & Audio", "メディア・オーディオ");
		Add("SysPreset_VolumeDown", "音量减小 (Volume Down)", "音量減小 (Volume Down)", "Volume Down", "音量を下げる");
		Add("SysPresetName_VolumeDown", "音量减", "音量減", "Volume Down", "音量-");
		Add("SysCategory_VolumeMute", "媒体音效", "媒體音訊", "Media & Audio", "メディア・オーディオ");
		Add("SysPreset_VolumeMute", "静音切换 (Mute)", "靜音切換 (Mute)", "Mute / Unmute", "消音 (ミュート)");
		Add("SysPresetName_VolumeMute", "静音切换", "靜音切換", "Mute", "ミュート切替");
		Add("SysCategory_PlayPause", "媒体音效", "媒體音訊", "Media & Audio", "メディア・オーディオ");
		Add("SysPreset_PlayPause", "播放/暂停 (Play/Pause)", "播放/暫停 (Play/Pause)", "Play / Pause", "再生 / 一時停止");
		Add("SysPresetName_PlayPause", "播放/暂停", "播放/暫停", "Play/Pause", "再生/一時停止");
		Add("SysCategory_NextTrack", "媒体音效", "媒體音訊", "Media & Audio", "メディア・オーディオ");
		Add("SysPreset_NextTrack", "下一曲 (Next Track)", "下一首 (Next Track)", "Next Track", "次のトラック");
		Add("SysPresetName_NextTrack", "下一曲", "下一首", "Next Track", "次へ");
		Add("SysCategory_PrevTrack", "媒体音效", "媒體音訊", "Media & Audio", "メディア・オーディオ");
		Add("SysPreset_PrevTrack", "上一曲 (Previous Track)", "上一首 (Previous Track)", "Previous Track", "前のトラック");
		Add("SysPresetName_PrevTrack", "上一曲", "上一首", "Prev Track", "前へ");
		Add("SysCategory_StopMedia", "媒体音效", "媒體音訊", "Media & Audio", "メディア・オーディオ");
		Add("SysPreset_StopMedia", "停止播放 (Stop)", "停止播放 (Stop)", "Stop Media", "メディア停止");
		Add("SysPresetName_StopMedia", "停止", "停止", "Stop", "停止");
		Add("SysCategory_NewTab", "网页浏览", "網頁瀏覽", "Web Browsing", "ウェブ閲覧");
		Add("SysPreset_NewTab", "新建标签页 (New Tab / Ctrl+T)", "新分頁 (New Tab / Ctrl+T)", "New Tab (Ctrl+T)", "新しいタブ (Ctrl+T)");
		Add("SysPresetName_NewTab", "新建标签", "新分頁", "New Tab", "新規タブ");
		Add("SysCategory_CloseTab", "网页浏览", "網頁瀏覽", "Web Browsing", "ウェブ閲覧");
		Add("SysPreset_CloseTab", "关闭标签页 (Close Tab / Ctrl+W)", "關閉分頁 (Close Tab / Ctrl+W)", "Close Tab (Ctrl+W)", "タブを閉じる (Ctrl+W)");
		Add("SysPresetName_CloseTab", "关闭标签", "關閉分頁", "Close Tab", "タブを閉じる");
		Add("SysCategory_ReopenTab", "网页浏览", "網頁瀏覽", "Web Browsing", "ウェブ閲覧");
		Add("SysPreset_ReopenTab", "恢复关闭标签 (Reopen / Ctrl+Shift+T)", "重新開啟已關閉的分頁 (Ctrl+Shift+T)", "Reopen Closed Tab (Ctrl+Shift+T)", "閉じたタブを開く (Ctrl+Shift+T)");
		Add("SysPresetName_ReopenTab", "恢复标签", "恢復分頁", "Reopen Tab", "タブを復元");
		Add("SysCategory_Refresh", "网页浏览", "網頁瀏覽", "Web Browsing", "ウェブ閲覧");
		Add("SysPreset_Refresh", "刷新页面 (Refresh / F5)", "重新整理 (Refresh / F5)", "Refresh Page (F5)", "ページの再読み込み (F5)");
		Add("SysPresetName_Refresh", "刷新", "重新整理", "Refresh", "再読み込み");
		Add("SysCategory_HardRefresh", "网页浏览", "網頁瀏覽", "Web Browsing", "ウェブ閲覧");
		Add("SysPreset_HardRefresh", "强制刷新 (Hard Refresh / Ctrl+F5)", "強制重新整理 (Hard Refresh / Ctrl+F5)", "Hard Refresh (Ctrl+F5)", "強制再読み込み (Ctrl+F5)");
		Add("SysPresetName_HardRefresh", "强制刷新", "強制重新整理", "Hard Refresh", "強制再読み込み");
		Add("SysCategory_ZoomIn", "网页浏览", "網頁瀏覽", "Web Browsing", "ウェブ閲覧");
		Add("SysPreset_ZoomIn", "页面放大 (Zoom In / Ctrl++)", "放大 (Zoom In / Ctrl++)", "Zoom In (Ctrl++)", "拡大 (Ctrl++)");
		Add("SysPresetName_ZoomIn", "放大", "放大", "Zoom In", "拡大");
		Add("SysCategory_ZoomOut", "网页浏览", "網頁瀏覽", "Web Browsing", "ウェブ閲覧");
		Add("SysPreset_ZoomOut", "页面缩小 (Zoom Out / Ctrl+-)", "縮小 (Zoom Out / Ctrl+-)", "Zoom Out (Ctrl+-)", "縮小 (Ctrl+-)");
		Add("SysPresetName_ZoomOut", "缩小", "縮小", "Zoom Out", "縮小");
		Add("SysCategory_ZoomReset", "网页浏览", "網頁瀏覽", "Web Browsing", "ウェブ閲覧");
		Add("SysPreset_ZoomReset", "默认缩放 (Reset Zoom / Ctrl+0)", "重設縮放 (Reset Zoom / Ctrl+0)", "Reset Zoom (Ctrl+0)", "既定倍率 (Ctrl+0)");
		Add("SysPresetName_ZoomReset", "默认缩放", "重設縮放", "Reset Zoom", "既定倍率");
		Add("SysCategory_Sleep", "电源控制", "電源控制", "Power Options", "電源制御");
		Add("SysPreset_Sleep", "系统睡眠 (Sleep)", "系統睡眠 (Sleep)", "Sleep", "スリープ");
		Add("SysPresetName_Sleep", "睡眠", "睡眠", "Sleep", "スリープ");
		Add("SysCategory_Restart", "电源控制", "電源控制", "Power Options", "電源制御");
		Add("SysPreset_Restart", "重启电脑 (Restart)", "重新啟動電腦 (Restart)", "Restart PC", "再起動");
		Add("SysPresetName_Restart", "重启", "重新啟動", "Restart", "再起動");
		Add("SysCategory_Shutdown", "电源控制", "電源控制", "Power Options", "電源制御");
		Add("SysPreset_Shutdown", "关闭电脑 (Shutdown)", "關閉電腦 (Shutdown)", "Shut Down PC", "シャットダウン");
		Add("SysPresetName_Shutdown", "关机", "關機", "Shut Down", "シャットダウン");

Add("AboutCheckUpdate", "🔄 检查更新", "🔄 檢查更新", "🔄 Check for updates", "🔄 更新を確認");
		Add("ActionTypePluginShort", "插件动作", "外掛動作", "Plugin Action", "プラグイン動作");
		Add("AddGestureMapping", "➕ 添加手势映射", "➕ 新增手勢對應", "➕ Add mapping", "➕ マッピングを追加");
		Add("AddLayer", "➕ 加层", "➕ 新增圖層", "➕ Add layer", "➕ レイヤーを追加");
		Add("AddProfileShort", "➕ 新增", "➕ 新增", "➕ New", "➕ 新規");
		Add("ApplyRestartUpdate", "🚀 立即退出并重启更新", "🚀 立即結束並重新啟動更新", "🚀 Exit and restart to update", "🚀 終了して再起動し更新");
		Add("BatchLayoutBoth", "🖼️+🔤 图文", "🖼️+🔤 圖文", "🖼️+🔤 Icon + text", "🖼️+🔤 アイコン＋文字");
		Add("BatchLayoutIconOnly", "🖼️ 仅图标", "🖼️ 僅圖示", "🖼️ Icon only", "🖼️ アイコンのみ");
		Add("BatchLayoutInherit", "🌐 继承全局", "🌐 繼承全域", "🌐 Inherit global", "🌐 グローバルを継承");
		Add("BatchLayoutTextOnly", "🔤 仅文字", "🔤 僅文字", "🔤 Text only", "🔤 文字のみ");
		Add("BatchResetCustom", "🔄 清除自定义，恢复跟随全局统一", "🔄 清除自訂，恢復跟隨全域統一", "🔄 Clear customisations, follow global", "🔄 カスタムを消去しグローバルに従う");
		Add("BrowseCoreImage", "浏览图片...", "瀏覽圖片...", "Browse image...", "画像を参照…");
		Add("BtnDeleteCurrentProfile", "\ud83d\uddd1\ufe0f 删除当前配置", "\ud83d\uddd1\ufe0f 刪除當前配置", "\ud83d\uddd1\ufe0f Delete Profile", "\ud83d\uddd1\ufe0f 設定を削除");
		Add("BtnRenameCurrentProfile", "✏\ufe0f 重命名当前配置", "✏\ufe0f 重新命名當前配置", "✏\ufe0f Rename Profile", "✏\ufe0f 名前を変更");
		Add("CancelDownload", "✖ 取消下载", "✖ 取消下載", "✖ Cancel download", "✖ ダウンロードを中止");
		Add("CenterInfoToggle", "ℹ️ 说明 ▾", "ℹ️ 說明 ▾", "ℹ️ Help ▾", "ℹ️ 説明 ▾");
		Add("CenterPresetsToggle", "⚡ 常用预设 ▾", "⚡ 常用預設 ▾", "⚡ Presets ▾", "⚡ よく使うプリセット ▾");
		Add("ClearCoreImage", "清除", "清除", "Clear", "クリア");
		Add("CopyLayer", "📑 复制", "📑 複製", "📑 Copy", "📑 複製");
		Add("CustomSoundExportProfile", "💾 导出", "💾 匯出", "💾 Export", "💾 書き出し");
		Add("CustomSoundImportProfile", "📂 导入", "📂 匯入", "📂 Import", "📂 読み込み");
		Add("CustomSoundNewProfile", "➕ 新建", "➕ 新增", "➕ New", "➕ 新規");
		Add("CustomSoundOpenEditorWindow", "🎛️ 独立大窗", "🎛️ 獨立大視窗", "🎛️ Open in window", "🎛️ 別ウィンドウで開く");
		Add("CustomSoundPlayFlow", "🔊 连续模拟完整手势交互体验", "🔊 連續模擬完整手勢互動體驗", "🔊 Play the full gesture interaction", "🔊 一連の操作をまとめて再生");
		Add("CustomSoundResetProfile", "🔄 重置", "🔄 重設", "🔄 Reset", "🔄 リセット");
		Add("EnableCenterAction", "启用中心核圆动作", "啟用中心核圓動作", "Enable centre core action", "中央コアの動作を有効にする");
		Add("EnableGlobalInheritance", "🌐 继承全局方案未配置槽位", "🌐 繼承全域方案未配置槽位", "🌐 Inherit global for unconfigured slots", "🌐 未設定スロットはグローバルを継承");
		Add("FocusAddSubAction", "➕ 添加二级动作", "➕ 新增二級動作", "➕ Add sub-action", "➕ サブ動作を追加");
		Add("FocusBackToParent", "◀ 返回父级扇区", "◀ 返回上層扇區", "◀ Back to parent sector", "◀ 親セクターに戻る");
		Add("FocusBatchExit", "✕ 退出多选", "✕ 結束多選", "✕ Exit multi-select", "✕ 複数選択を終了");
		Add("FocusCenterCore", "🎯 中心核圆", "🎯 中心核圓", "🎯 Centre core", "🎯 中央コア");
		Add("FocusClearInheritedIcon", "✕ 清除关联", "✕ 清除關聯", "✕ Clear link", "✕ 関連付けを解除");
		Add("FocusClearSubActions", "🗑️ 清空", "🗑️ 清空", "🗑️ Clear all", "🗑️ すべて消去");
		Add("FocusNextSlot", "下一槽 ▶", "下一槽 ▶", "Next slot ▶", "次のスロット ▶");
		Add("FocusPickShellTool", "⚡ 挑选功能...", "⚡ 挑選功能...", "⚡ Pick a tool...", "⚡ 機能を選択…");
		Add("FocusPluginReload", "🔄 停用后重新加载", "🔄 停用後重新載入", "🔄 Reload after disabling", "🔄 無効化して再読み込み");
		Add("FocusPopulateTileSubActions", "✨ 预设 8 布局二级轮盘", "✨ 預設 8 佈局二級輪盤", "✨ Fill 8 tile layouts", "✨ 8分割レイアウトを設定");
		Add("FocusPrevSlot", "◀ 上一槽", "◀ 上一槽", "◀ Previous slot", "◀ 前のスロット");
		Add("FocusRestoreInherit", "🌐 恢复继承全局", "🌐 恢復繼承全域", "🌐 Restore global inheritance", "🌐 グローバル継承に戻す");
		Add("FocusTestAction", "▶ 测试触发", "▶ 測試觸發", "▶ Test trigger", "▶ テスト実行");
		Add("FocusUndoSubActions", "↩️ 撤销", "↩️ 復原", "↩️ Undo", "↩️ 元に戻す");
		Add("IconLayoutModeTitle", "排版模式:", "排版模式:", "Layout mode:", "レイアウトモード:");
		Add("MappingsSectorCount12", "12 键钟表方位", "12 鍵鐘錶方位", "12 positions (clock)", "12方位（時計）");
		Add("MappingsSectorCount4", "4 键十字方位", "4 鍵十字方位", "4 positions (cross)", "4方位（十字）");
		Add("MappingsSectorCount8", "8 键全向方位 (推荐)", "8 鍵全向方位 (推薦)", "8 positions (recommended)", "8方位（推奨）");
		Add("MappingsTier1Segment", "🔘 一级主轮盘", "🔘 一級主輪盤", "🔘 Tier 1 wheel", "🔘 第1階層ホイール");
		Add("MappingsTier2Segment", "🌟 二级级联", "🌟 二級串聯", "🌟 Tier 2 cascade", "🌟 第2階層カスケード");
		Add("MappingsViewModeCanvas", "🎯 画布联动精调 (推荐)", "🎯 畫布關聯精調 (推薦)", "🎯 Canvas live editor (recommended)", "🎯 キャンバス連動編集（推奨）");
		Add("MappingsViewModeList", "📋 紧凑全览列表", "📋 精簡總覽清單", "📋 Compact list", "📋 コンパクト一覧");
		Add("OpenUpdateFolder", "📂 打开文件位置", "📂 開啟檔案位置", "📂 Open file location", "📂 ファイルの場所を開く");
		Add("OpenWebRelease", "🌐 前往网页", "🌐 前往網頁", "🌐 Open release page", "🌐 リリースページを開く");
		Add("PickCoreIcon", "选择图标...", "選擇圖示...", "Choose icon...", "アイコンを選択…");
		Add("PluginCandidateAuthor", "作者 {0}", "作者 {0}", "by {0}", "作者 {0}");
		Add("PluginCandidateCapabilities", "声明能力：{0}", "宣告能力：{0}", "Capabilities: {0}", "宣言機能：{0}");
		Add("PluginCandidateInstall", "📦 安装", "📦 安裝", "📦 Install", "📦 インストール");
		Add("PluginCandidateInstallDowngrade", "⬇️ 降级安装", "⬇️ 降級安裝", "⬇️ Downgrade", "⬇️ ダウングレード");
		Add("PluginCandidateInstallOverwrite", "📦 覆盖安装", "📦 覆蓋安裝", "📦 Overwrite", "📦 上書きインストール");
		Add("PluginCandidateInstallUpdate", "⬆️ 更新", "⬆️ 更新", "⬆️ Update", "⬆️ 更新");
		Add("PluginCandidateNoteDifferentContent", "内容与已装的不同。", "內容與已裝的不同。", "The content differs from the installed version.", "内容はインストール済みのものと異なります。");
		Add("PluginCandidateNoteDowngrade", "已装 v{0}，这枚是更旧的 v{1}。一般不建议降级。", "已裝 v{0}，這枚是更舊的 v{1}。一般不建議降級。", "v{0} is installed; this file is the older v{1}. Downgrading is usually not recommended.", "v{0} がインストール済みで、これは古い v{1} です。通常、ダウングレードは推奨しません。");
		Add("PluginCandidateNoteDuplicate", "扫描目录里有 {0} 枚 .dll 声明了同一个 ID（{1}），无法判断该装哪一枚。请只保留需要的那一个文件。", "掃描目錄裡有 {0} 枚 .dll 宣告了同一個 ID（{1}），無法判斷該裝哪一枚。請只保留需要的那一個檔案。", "{0} .dll files in the scan folder declare the same ID ({1}), so there is no way to tell which one to install. Keep only the file you need.", "スキャンフォルダー内の {0} 個の .dll が同じ ID（{1}）を宣言しているため、どれをインストールすべきか判断できません。必要なファイルだけを残してください。");
		Add("PluginCandidateNoteExternalRegistered", "同一个 ID 已被开发者模式的外部路径登记占用：{0}。如需改为安装副本，请先在列表里卸载那条登记。", "同一個 ID 已被開發者模式的外部路徑登記占用：{0}。如需改為安裝副本，請先在清單裡解除那條登記。", "The same ID is already claimed by a developer-mode external path registration: {0}. To switch to an installed copy, unregister it in the list first.", "同じ ID は既に開発者モードの外部パス登録（{0}）が使用しています。インストール済みのコピーに切り替える場合は、先に一覧からその登録を解除してください。");
		Add("PluginCandidateNoteInstallable", "尚未安装，可直接安装。", "尚未安裝，可直接安裝。", "Not installed yet — you can install it directly.", "まだインストールされていません。そのままインストールできます。");
		Add("PluginCandidateNoteInstalled", "已装同一个版本（v{0}），无需重复安装。", "已裝同一個版本（v{0}），無需重複安裝。", "Version v{0} is already installed — no need to install it again.", "同じバージョン（v{0}）が既にインストールされています。再インストールは不要です。");
		Add("PluginCandidateNoteRejected", "无法安装：{0}", "無法安裝：{0}", "Cannot install: {0}", "インストールできません：{0}");
		Add("PluginCandidateNoteReplaced", "已装的 v{0} 与这枚文件版本号相同但内容不同（哈希不一致）。覆盖安装会用它替换现有文件。", "已裝的 v{0} 與這枚檔案版本號相同但內容不同（雜湊不一致）。覆蓋安裝會用它取代現有檔案。", "The installed v{0} and this file share the same version number but differ in content (hash mismatch). Overwriting will replace the existing file with this one.", "インストール済みの v{0} とこのファイルはバージョンが同じで内容が異なります（ハッシュ不一致）。上書きインストールするとこのファイルに置き換わります。");
		Add("PluginCandidateNoteReserved", "这是官方模块（{0}）。扫描目录只用于手动安装社区插件 —— 官方模块请到上方「官方插件」列表里下载和更新，宿主不会从这里安装它。", "這是官方模組（{0}）。掃描目錄只用於手動安裝社群外掛 —— 官方模組請到上方「官方外掛」清單裡下載和更新，宿主不會從這裡安裝它。", "This is an official module ({0}). The scan folder is only for installing community plugins by hand — download and update official modules from the \"Official plugins\" list above; StarPie will not install it from here.", "これは公式モジュール（{0}）です。スキャンフォルダーはコミュニティプラグインを手動でインストールするためのもので、公式モジュールは上の「公式プラグイン」一覧からダウンロード・更新してください。ここからはインストールされません。");
		Add("PluginCandidateNoteSameContent", "内容与已装的一致。", "內容與已裝的一致。", "The content matches the installed version.", "内容はインストール済みのものと一致します。");
		Add("PluginCandidateNoteUpdate", "已装 v{0}，这枚是更新的 v{1}。", "已裝 v{0}，這枚是更新的 v{1}。", "v{0} is installed; this file is the newer v{1}.", "v{0} がインストール済みで、これは新しい v{1} です。");
		Add("PluginCandidateNoteVersionUnknown", "已装版本「{0}」与候选版本「{1}」至少有一侧解析不了，无法比较新旧。", "已裝版本「{0}」與候選版本「{1}」至少有一側無法解析，無法比較新舊。", "At least one of the installed version \"{0}\" or the candidate version \"{1}\" cannot be parsed, so the two cannot be compared.", "インストール済みバージョン「{0}」と候補バージョン「{1}」の少なくとも一方を解析できないため、新旧を比較できません。");
		Add("PluginCandidateStateDowngrade", "版本更旧", "版本更舊", "Older version", "古いバージョン");
		Add("PluginCandidateStateDuplicate", "ID 重复", "ID 重複", "Duplicate ID", "ID 重複");
		Add("PluginCandidateStateExternalRegistered", "已外部引用", "已外部引用", "Externally registered", "外部参照済み");
		Add("PluginCandidateStateInstallable", "可安装", "可安裝", "Installable", "インストール可能");
		Add("PluginCandidateStateInstalled", "已装同版本", "已裝同版本", "Same version installed", "同じバージョンを導入済み");
		Add("PluginCandidateStateRejected", "无法识别", "無法識別", "Unrecognized", "認識できません");
		Add("PluginCandidateStateReplaced", "内容已变", "內容已變", "Content changed", "内容が変更されています");
		Add("PluginCandidateStateReserved", "官方模块", "官方模組", "Official module", "公式モジュール");
		Add("PluginCandidateStateUpdate", "有新版本", "有新版本", "Update available", "新しいバージョンあり");
		Add("PluginCandidateStateVersionUnknown", "版本待确认", "版本待確認", "Version unknown", "バージョン未確認");
		Add("PluginCapabilityAdmin", "· 需要管理员权限", "· 需要系統管理員權限", "· Require administrator privileges", "· 管理者権限が必要");
		Add("PluginCapabilityClipboard", "· 读取或修改剪贴板", "· 讀取或修改剪貼簿", "· Read or modify the clipboard", "· クリップボードの読み取り・変更");
		Add("PluginCapabilityFileSystem", "· 读写你的文件", "· 讀寫你的檔案", "· Read and write your files", "· ファイルの読み書き");
		Add("PluginCapabilityGlobalHook", "· 安装全局键盘/鼠标钩子", "· 安裝全域鍵盤/滑鼠鉤子", "· Install global keyboard/mouse hooks", "· グローバルなキーボード・マウスフックの設置");
		Add("PluginCapabilityInputRemapping", "· 托管式键盘重映射（进程级按键重定向）", "· 託管式鍵盤重新對應（行程級按鍵重導向）", "· Managed keyboard remapping (process-scoped key redirection)", "· マネージドキーボードリマッピング（プロセス単位のキー再割り当て）");
		Add("PluginCapabilityInputSimulation", "· 向当前窗口发送按键", "· 向目前視窗傳送按鍵", "· Send keystrokes to the current window", "· 現在のウィンドウへキー入力を送信");
		Add("PluginCapabilityNetwork", "· 访问网络", "· 存取網路", "· Access the network", "· ネットワークへのアクセス");
		Add("PluginCapabilityNone", "（无）", "（無）", "(none)", "（なし）");
		Add("PluginCapabilityProcess", "· 启动进程 / 执行命令", "· 啟動行程 / 執行命令", "· Start processes / run commands", "· プロセスの起動 / コマンドの実行");
		Add("PluginCapabilityRegistry", "· 读写注册表", "· 讀寫登錄檔", "· Read and write the registry", "· レジストリの読み書き");
		Add("PluginCapabilityScreenCapture", "· 读取屏幕内容（截屏）", "· 讀取螢幕內容（截圖）", "· Read screen contents (screenshot)", "· 画面内容の読み取り（スクリーンショット）");
		Add("PluginCapabilityUi", "· 显示界面与通知", "· 顯示介面與通知", "· Show windows and notifications", "· ウィンドウと通知の表示");
		Add("PluginCapabilityWheel", "· 呼出你配置的轮盘，显示期间接管你的点击", "· 呼叫你設定的輻盤，顯示期間接管你的點擊", "· Bring up your configured radial wheel and capture clicks while shown", "· 設定済みのラジアルホイールを呼び出し、表示中はクリックを全て受け取ります");
		Add("PluginCapabilityWindowControl", "· 移动 / 置顶 / 改变你正在使用的窗口", "· 移動 / 置頂 / 改變你正在使用中的視窗", "· Move, pin, or alter the window you are using", "· 使用中のウィンドウの移動 / 最前面表示 / 変更");
		Add("KeyMapEditorTitle", "按键映射配置", "按鍵對應設定", "Keyboard Remap Configuration", "キーマップ設定");
		Add("KeyMapEditorSubtitle", "左手物理按键空间映射至数字小键盘合成键；支持可视化点击联动与列表编辑。", "左手實體按鍵空間對應至數字小鍵盤合成鍵；支援視覺化點擊聯動與清單編輯。", "Process-scoped spatial remapping from left-hand physical keys to synthetic numpad keys.", "左手物理キーをテンキー合成キーへ空間再割り当て。UIでのクリック連携とリスト編集に対応。");
		Add("KeyMapEditorPhysicalHeader", "主键盘左手区域 (物理源按键)", "主鍵盤左手區域 (實體來源按鍵)", "Main Keyboard Left-Hand Area (Physical Source)", "メインキーボード左手エリア (物理元キー)");
		Add("KeyMapEditorTargetHeader", "数字小键盘区域 (目标合成键)", "數字小鍵盤區域 (目標合成鍵)", "Numpad Area (Target Synthetic Keys)", "テンキーエリア (変換先合成キー)");
		Add("KeyMapEditorMappingHint", "空间映射", "空間對應", "Spatial Remap", "空間マッピング");
		Add("KeyMapEditorActionHeader", "操作", "操作", "Action", "操作");
		Add("KeyMapEditorSourceTooltip", "源按键名（如 Q、W、A，可直接点击上方按键）", "來源按鍵名稱（如 Q、W、A，可直接點擊上方按鍵）", "Source key (e.g. Q, W, A, or click a key above)", "変換元キー名（Q, W, A など、上部キーをクリック可能）");
		Add("KeyMapEditorTargetTooltip", "目标按键名（如 Num7、Num0，可直接点击上方数字键）", "目標按鍵名稱（如 Num7、Num0，可直接點擊上方數字鍵）", "Target key (e.g. Num7, Num0, or click a numpad key above)", "変換先キー名（Num7, Num0 など、上部テンキーをクリック可能）");
		Add("KeyMapEditorErrEmptyKeys", "源按键或目标按键不能为空", "來源按鍵或目標按鍵不能為空", "Source key and target key cannot be empty", "変換元または変換先キーを指定してください");
		Add("KeyMapFieldEmptySummary", "（未配置按键映射）", "（未設定按鍵對應）", "(No keyboard remap configured)", "（キーマップ未設定）");
		Add("KeyMapFieldConfiguredSummary", "已配置 {0} 项按键映射 (点击右侧按钮查看/编辑)", "已設定 {0} 項按鍵對應 (點擊右側按鈕檢視/編輯)", "{0} keyboard remap rule(s) configured (click button to edit)", "{0} 件のキーマップを設定済み（右側ボタンで編集）");
		Add("KeyMapErrUnsupportedVersion", "不支持的键盘映射协议版本（必须以 \"v1|\" 开头）", "不支援的鍵盤對應通訊協定版本（必須以 \"v1|\" 開頭）", "Unsupported KeyMap protocol version (must start with \"v1|\")", "サポートされていないキーマッププロトコルバージョン（\"v1|\" で始まる必要があります）");
		Add("KeyMapErrMissingSeparator", "映射项 \"{0}\" 格式错误，缺少按键分隔符（应为 \"源键:目标键\"）", "對應項 \"{0}\" 格式錯誤，缺少按鍵分隔符（應為 \"來源鍵:目標鍵\"）", "Mapping entry \"{0}\" is malformed: missing key separator (expected \"FromKey:ToKey\")", "マッピング項目 \"{0}\" の形式が無効です：区切り文字がありません（\"元キー:先キー\" が必要）");
		Add("KeyMapErrUnknownSourceKey", "无法识别源按键: \"{0}\"", "無法識別來源按鍵: \"{0}\"", "Unrecognized source key: \"{0}\"", "変換元キーを認識できません: \"{0}\"");
		Add("KeyMapErrUnknownTargetKey", "无法识别目标按键: \"{0}\"", "無法識別目標按鍵: \"{0}\"", "Unrecognized target key: \"{0}\"", "変換先キーを認識できません: \"{0}\"");
		Add("KeyMapErrMaxEntriesExceeded", "映射项数量（{0}）超出上限（最大 {1} 项）", "對應項數量（{0}）超出上限（最大 {1} 項）", "Mapping entry count ({0}) exceeds maximum limit ({1})", "マッピング項目数（{0}）が上限（最大 {1}）を超えています");
		Add("KeyMapErrModifierAsSource", "禁止将修饰键（Ctrl/Alt/Shift/Win）单独作为源按键: {0}", "禁止將修飾鍵（Ctrl/Alt/Shift/Win）單獨作為來源按鍵: {0}", "Modifier keys (Ctrl/Alt/Shift/Win) cannot be used as source keys: {0}", "修飾キー（Ctrl/Alt/Shift/Win）を変換元キーとして指定することはできません: {0}");
		Add("KeyMapErrModifierAsTarget", "禁止将修饰键（Ctrl/Alt/Shift/Win）作为目标按键: {0}", "禁止將修飾鍵（Ctrl/Alt/Shift/Win）作為目標按鍵: {0}", "Modifier keys (Ctrl/Alt/Shift/Win) cannot be used as target keys: {0}", "修飾キー（Ctrl/Alt/Shift/Win）を変換先キーとして指定することはできません: {0}");
		Add("KeyMapErrEscapeReserved", "Escape 键保留用于长按紧急撤销会话，禁止映射为源按键", "Escape 鍵保留用於長按緊急撤銷會話，禁止對應為來源按鍵", "Escape key is reserved for emergency session revocation and cannot be mapped as source", "Escape キーは緊急セッション解除用に予約されているため、変換元キーに指定できません");
		Add("KeyMapErrDuplicateSource", "存在重复的源按键映射: \"{0}\"", "存在重複的來源按鍵對應: \"{0}\"", "Duplicate source key mapping: \"{0}\"", "重複した変換元キーマッピングがあります: \"{0}\"");
		Add("KeyMapEditorPresetSpatial", "恢复空间数字键盘预设 (QWE→789)", "恢復空間數字鍵盤預設 (QWE→789)", "Restore Spatial Numpad Preset (QWE→789)", "空間テンキー配列プリセットに戻す (QWE→789)");
		Add("KeyMapEditorAddMapping", "添加映射", "新增對應", "Add Mapping", "マッピングを追加");
		Add("KeyMapEditorClear", "清空", "清空", "Clear", "クリア");
		Add("KeyMapEditorSave", "保存", "儲存", "Save", "保存");
		Add("KeyMapEditorCancel", "取消", "取消", "Cancel", "キャンセル");
		Add("KeyMapEditorSourceKey", "源按键 (物理键)", "來源按鍵 (實體鍵)", "Source Key (Physical)", "変換元キー (物理)");
		Add("KeyMapEditorTargetKey", "目标按键 (合成键)", "目標按鍵 (合成鍵)", "Target Key (Synthetic)", "変換先キー (合成)");
		Add("KeyMapEditorDelete", "删除", "刪除", "Delete", "削除");
		Add("KeyMapEditorConfigureButton", "⌨ 配置映射…", "⌨ 設定對應…", "⌨ Configure Mappings…", "⌨ マッピング設定…");
		Add("KeyMapRecordSourcePlaceholder", "点击录制源按键...", "點擊錄製來源按鍵...", "Click to record source key...", "クリックして変換元キーを記録...");
		Add("KeyMapRecordTargetPlaceholder", "点击录制目标按键...", "點擊錄製目標按鍵...", "Click to record target key...", "クリックして変換先キーを記録...");
		Add("KeyMapRecordingHint", "🔴 请按下单键 (Esc 取消)...", "🔴 請按下單鍵 (Esc 取消)...", "🔴 Press a single key (Esc to cancel)...", "🔴 単一キーを押してください (Esc でキャンセル)...");
		Add("KeyMapClearKey", "清空按键", "清空按鍵", "Clear key", "キーをクリア");
		Add("KeyMapErrModifierForbidden", "禁止将修饰键（Ctrl/Alt/Shift/Win）录入为单键映射", "禁止將修飾鍵（Ctrl/Alt/Shift/Win）錄入為單鍵對應", "Modifier keys (Ctrl/Alt/Shift/Win) cannot be recorded as single-key mappings", "修飾キー（Ctrl/Alt/Shift/Win）を単一キーマッピングとして記録することはできません");
		Add("KeyMapEditorErrIncompleteTarget", "已录制源按键，请录制或选择目标按键，或清空后再保存", "已錄製來源按鍵，請錄製或選擇目標按鍵，或清空後再儲存", "Source key recorded. Please record or select a target key, or clear before saving", "変換元キーが記録されています。変換先キーを記録するか、クリアしてから保存してください");
		Add("KeyMapEditorErrIncompleteSource", "已录制目标按键，请录制或选择源按键，或清空后再保存", "已錄製目標按鍵，請錄製或選擇來源按鍵，或清空後再儲存", "Target key recorded. Please record or select a source key, or clear before saving", "変換先キーが記録されています。変換元キーを記録するか、クリアしてから保存してください");
		Add("KeyMapListEditHint", "提示：列表为只读视图。选中行可载入下方录键框修改，完成后点击“更新映射”或直接“保存”。", "提示：列表為唯讀檢視。選取資料列可載入下方錄鍵框修改，完成後點擊「更新對應」或直接「儲存」。", "Tip: The list is read-only. Select a row to load into the recorders below, then click \"Update Mapping\" or \"Save\" directly.", "ヒント: リストは読み取り専用です。行を選択すると下部の入力欄に読み込まれます。「マッピングを更新」または直接「保存」をクリックしてください。");
		Add("KeyMapEditorUpdateMapping", "更新映射", "更新對應", "Update Mapping", "マッピングを更新");
		Add("KeyMapDiagramHeader", "映射关系图谱", "對應關係圖譜", "Remap Relationship Diagram", "マッピング相関図");
		Add("KeyMapDiagramHint", "（点击查看关联，Ctrl+滚轮缩放，拖动平移）", "（點擊檢視關聯，Ctrl+滾輪縮放，拖曳平移）", "(Click to inspect, Ctrl+Wheel to zoom, drag to pan)", "（クリックで関連確認、Ctrl+ホイールで拡大縮小、ドラッグで移動）");
		Add("KeyMapOtherSourcesHeader", "其他源按键", "其他來源按鍵", "Other Source Keys", "その他変換元キー");
		Add("KeyMapOtherTargetsHeader", "其他目标按键", "其他目標按鍵", "Other Target Keys", "その他変換先キー");
		Add("KeyMapFitCanvas", "适应画布", "最適畫布", "Fit Canvas", "画面に合わせる");
		Add("KeyMapZoomInTooltip", "放大 (Ctrl + 滚轮向上)", "放大 (Ctrl + 滾輪向上)", "Zoom In (Ctrl + Wheel Up)", "拡大 (Ctrl + ホイール上)");
		Add("KeyMapZoomOutTooltip", "缩小 (Ctrl + 滚轮向下)", "縮小 (Ctrl + 滾輪向下)", "Zoom Out (Ctrl + Wheel Down)", "縮小 (Ctrl + ホイール下)");
		Add("KeyMapDiagramMultiMappingHint", "{0} 项映射 ➔ {1}", "{0} 項對應 ➔ {1}", "{0} mappings ➔ {1}", "{0} 件のマッピング ➔ {1}");
		Add("KeyMapWaitingForTargetKey", "{0} ➔ ? (请选择目标键)", "{0} ➔ ? (請選擇目標鍵)", "{0} ➔ ? (Click target key to pair)", "{0} ➔ ? (目標キーを選択してください)");
		Add("KeyMapWaitingForTargetKeyWithCurrent", "{0} ➔ ? (请选择目标键，当前: {1})", "{0} ➔ ? (請選擇目標鍵，目前: {1})", "{0} ➔ ? (Click target key to change, current: {1})", "{0} ➔ ? (目標キーを選択して変更、現在: {1})");
		Add("PluginPageHeader", "插件与扩展", "外掛與擴充", "Plugins & Extensions", "プラグインと拡張");
		Add("PluginPageSubheader", "手动选择 .dll 安装社区插件。插件以 StarPie 当前权限在进程内运行，请只安装你信任的来源。", "手動選擇 .dll 安裝社群外掛。外掛以 StarPie 目前權限在行程內執行，請僅安裝你信任的來源。", "Install community plugins by picking a .dll manually. Plugins run in-process with StarPie's current privileges - only install sources you trust.", "コミュニティプラグインは .dll を手動で選択してインストールします。プラグインは StarPie の権限でプロセス内実行されるため、信頼できる提供元のみ導入してください。");
		Add("PluginScanFailureHintAmbiguousContractImplementation", "程序集里有多个 IStarPiePlugin 实现。请在 plugin.json 的 entryType 里明确指定入口类全名。", "組件裡有多個 IStarPiePlugin 實作。請在 plugin.json 的 entryType 裡明確指定進入點類別全名。", "The assembly has several IStarPiePlugin implementations. Name the entry class explicitly in plugin.json's entryType.", "アセンブリ内に IStarPiePlugin の実装が複数あります。plugin.json の entryType でエントリクラスの完全名を指定してください。");
		Add("PluginScanFailureHintApiVersionMismatch", "插件编译时使用的 SDK 契约主版本与当前 StarPie 不一致。请更新插件，或升级 StarPie。", "外掛編譯時使用的 SDK 契約主版本與目前 StarPie 不一致。請更新外掛，或升級 StarPie。", "The plugin was built against a different SDK contract major version than this StarPie. Update the plugin, or update StarPie.", "プラグインがビルド時に使用した SDK 契約のメジャーバージョンが現在の StarPie と一致しません。プラグインを更新するか、StarPie を更新してください。");
		Add("PluginScanFailureHintContractAssemblyVersionMismatch", "插件自带了 StarPie.Plugin.Abstractions.dll 且版本与宿主不一致。请删除插件目录里的这个文件，它会由 StarPie 统一提供。", "外掛自帶了 StarPie.Plugin.Abstractions.dll 且版本與宿主不一致。請刪除外掛目錄裡的這個檔案，它會由 StarPie 統一提供。", "The plugin ships its own StarPie.Plugin.Abstractions.dll whose version differs from the host's. Delete that file from the plugin folder — StarPie provides it centrally.", "プラグインが独自に StarPie.Plugin.Abstractions.dll を同梱しており、バージョンがホストと一致しません。プラグインフォルダーからこのファイルを削除してください。StarPie が一元提供します。");
		Add("PluginScanFailureHintDependencyCycle", "插件之间形成了循环依赖，无法确定加载顺序。请联系作者修复依赖声明。", "外掛之間形成了循環相依，無法確定載入順序。請聯絡作者修復相依宣告。", "The plugins depend on each other in a cycle, so the load order cannot be determined. Ask the author to fix the dependency declarations.", "プラグイン間に循環依存があり、読み込み順を決定できません。作者に依存関係の宣言を修正してもらってください。");
		Add("PluginScanFailureHintDependencyMissing", "插件依赖的另一个插件没有安装或未启用。请先安装并启用依赖项。", "外掛相依的另一個外掛沒有安裝或未啟用。請先安裝並啟用相依項目。", "Another plugin this one depends on is not installed or not enabled. Install and enable the dependency first.", "このプラグインが依存する別のプラグインがインストールされていないか、有効になっていません。先に依存プラグインをインストールして有効にしてください。");
		Add("PluginScanFailureHintDllNotFound", "清单里声明的程序集文件不在插件目录中，请确认打包时没有漏掉 .dll。", "清單裡宣告的組件檔案不在外掛目錄中，請確認封裝時沒有漏掉 .dll。", "The assembly declared in the manifest is not in the plugin folder. Make sure the .dll was not left out when packaging.", "マニフェストで宣言されたアセンブリがプラグインフォルダーにありません。パッケージ作成時に .dll を入れ忘れていないか確認してください。");
		Add("PluginScanFailureHintEntryTypeNotFound", "plugin.json 里 entryType 写的类型名在程序集中不存在，请核对命名空间与类型名拼写。", "plugin.json 裡 entryType 寫的型別名稱在組件中不存在，請核對命名空間與型別名稱拼寫。", "The type named in plugin.json's entryType does not exist in the assembly. Check the namespace and type name spelling.", "plugin.json の entryType に書かれた型名がアセンブリ内に存在しません。名前空間と型名の綴りを確認してください。");
		Add("PluginScanFailureHintHostVersionOutOfRange", "当前 StarPie 版本不在插件声明的可运行区间内。请升级 StarPie，或联系作者放宽版本区间。", "目前 StarPie 版本不在外掛宣告的可執行區間內。請升級 StarPie，或聯絡作者放寬版本區間。", "This StarPie version is outside the range the plugin declares it runs on. Update StarPie, or ask the author to widen the range.", "現在の StarPie のバージョンが、プラグインが宣言した動作可能範囲に含まれていません。StarPie を更新するか、作者に範囲の拡大を依頼してください。");
		Add("PluginScanFailureHintIdNotDeclared", "这个 .dll 既没有同级的 plugin.json，也没有在程序集里声明 StarPiePluginId 元数据。让作者按文档在 csproj 里补上 AssemblyMetadata 是推荐做法（分发时只需一枚 .dll）；带 plugin.json 的完整插件包同样可以安装。", "這個 .dll 既沒有同層的 plugin.json，也沒有在組件裡宣告 StarPiePluginId 中繼資料。請作者依文件在 csproj 裡補上 AssemblyMetadata 是推薦做法（散佈時只需一枚 .dll）；附帶 plugin.json 的完整外掛包同樣可以安裝。", "This .dll has no plugin.json beside it and declares no StarPiePluginId assembly metadata. The recommended fix is for the author to add AssemblyMetadata in the csproj (so only one .dll needs to ship); a full plugin package with plugin.json works just as well.", "この .dll には同じ階層の plugin.json も、アセンブリ内の StarPiePluginId メタデータもありません。作者がドキュメントに沿って csproj に AssemblyMetadata を追加するのが推奨です（配布時は .dll 1 枚で済みます）。plugin.json を含む完全なプラグインパッケージでもインストールできます。");
		Add("PluginScanFailureHintInvalidIdFormat", "插件 ID 需要是反向域名风格，全小写，例如 com.example.mytool。", "外掛 ID 需要是反向網域風格，全小寫，例如 com.example.mytool。", "A plugin ID must be reverse-DNS style, all lowercase, for example com.example.mytool.", "プラグイン ID は逆ドメイン形式のすべて小文字にしてください（例：com.example.mytool）。");
		Add("PluginScanFailureHintManifestInvalid", "请检查 plugin.json 的字段名与类型是否与规范一致（可对照 plugin.schema.json）。", "請檢查 plugin.json 的欄位名稱與型別是否與規範一致（可對照 plugin.schema.json）。", "Check that the field names and types in plugin.json match the spec (compare against plugin.schema.json).", "plugin.json のフィールド名と型が仕様どおりか確認してください（plugin.schema.json と照合できます）。");
		Add("PluginScanFailureHintNoContractImplementation", "程序集里找不到 IStarPiePlugin 的实现类，说明它不是一个 StarPie 插件。", "組件裡找不到 IStarPiePlugin 的實作類別，說明它不是一個 StarPie 外掛。", "The assembly contains no IStarPiePlugin implementation, so it is not a StarPie plugin.", "アセンブリ内に IStarPiePlugin の実装クラスが見つかりません。StarPie プラグインではありません。");
		Add("PluginScanFailureHintNone", "识别已通过，无需修复。", "識別已通過，無需修復。", "The scan passed — nothing to fix.", "スキャンは通過しました。修正の必要はありません。");
		Add("PluginScanFailureHintNotDotNetAssembly", "这是一枚原生 C++ DLL 或非托管库，StarPie 插件必须是 .NET 程序集。你可能选错了文件。", "這是一枚原生 C++ DLL 或非受控程式庫，StarPie 外掛必須是 .NET 組件。你可能選錯了檔案。", "This is a native C++ DLL or an unmanaged library. A StarPie plugin must be a .NET assembly — you may have picked the wrong file.", "これはネイティブ C++ DLL またはアンマネージドライブラリです。StarPie プラグインは .NET アセンブリである必要があります。ファイルの選択を誤っている可能性があります。");
		Add("PluginScanFailureHintNotIlOnly", "程序集混合了本机代码（C++/CLI）。StarPie 只接受纯托管（ILOnly）程序集。", "組件混合了原生程式碼（C++/CLI）。StarPie 只接受純受控（ILOnly）組件。", "The assembly mixes in native code (C++/CLI). StarPie only accepts purely managed (ILOnly) assemblies.", "アセンブリにネイティブコード（C++/CLI）が混在しています。StarPie は純粋なマネージド（ILOnly）アセンブリのみを受け付けます。");
		Add("PluginScanFailureHintReservedIdPrefix", "starpie / windows / microsoft / system / builtin 前缀保留给官方，请换一个前缀。", "starpie / windows / microsoft / system / builtin 前綴保留給官方，請換一個前綴。", "The starpie / windows / microsoft / system / builtin prefixes are reserved for official modules. Please pick a different prefix.", "starpie / windows / microsoft / system / builtin の各プレフィックスは公式用に予約されています。別のプレフィックスを使用してください。");
		Add("PluginScanFailureHintSha256Mismatch", "文件内容与清单声明的哈希不一致，可能下载不完整或被第三方修改过。请从官方渠道重新获取。", "檔案內容與清單宣告的雜湊不一致，可能下載不完整或被第三方修改過。請從官方管道重新取得。", "The file content does not match the hash declared in the manifest — the download may be incomplete or the file modified by a third party. Get it again from the official source.", "ファイルの内容がマニフェストで宣言されたハッシュと一致しません。ダウンロードが不完全か、第三者によって改変された可能性があります。公式の配布元から再取得してください。");
		Add("PluginScanFailureHintTargetFrameworkMismatch", "插件的目标框架高于当前 StarPie。请升级 StarPie，或联系作者改用更低的 net8.0-windows 目标。", "外掛的目標框架高於目前的 StarPie。請升級 StarPie，或聯絡作者改用較低的 net8.0-windows 目標。", "The plugin targets a newer framework than this StarPie build. Update StarPie, or ask the author to target net8.0-windows or lower.", "プラグインのターゲットフレームワークが現在の StarPie より新しいものです。StarPie を更新するか、作者に net8.0-windows 以下へ下げてもらってください。");
		Add("PluginScanFailureHintWrongArchitecture", "程序集被编译为仅 32 位（Requires32Bit）。请把插件的平台目标改为 x64 或 AnyCPU 后重新发布。", "組件被編譯為僅 32 位元（Requires32Bit）。請把外掛的平台目標改為 x64 或 AnyCPU 後重新發佈。", "The assembly is compiled as 32-bit only (Requires32Bit). Change the plugin's platform target to x64 or AnyCPU and rebuild.", "アセンブリが 32 ビット専用（Requires32Bit）でコンパイルされています。プラグインのプラットフォームターゲットを x64 または AnyCPU に変更して再発行してください。");
		Add("PluginScanFailureSeparator", "：", "：", ": ", "：");
		Add("PluginScanFailureTitleAmbiguousContractImplementation", "入口类型不唯一", "進入點類型不唯一", "Ambiguous entry type", "エントリ型が一意に定まりません");
		Add("PluginScanFailureTitleApiVersionMismatch", "插件 SDK 契约版本不兼容", "外掛 SDK 契約版本不相容", "Incompatible plugin SDK version", "プラグイン SDK の契約バージョンが非互換です");
		Add("PluginScanFailureTitleContractAssemblyVersionMismatch", "SDK 程序集版本身份不一致", "SDK 組件版本身分不一致", "SDK assembly version identity mismatch", "SDK アセンブリのバージョン同一性が一致しません");
		Add("PluginScanFailureTitleDependencyCycle", "插件依赖存在环", "外掛相依存在環", "Cyclic plugin dependency", "プラグインの依存関係に循環があります");
		Add("PluginScanFailureTitleDependencyMissing", "缺少依赖插件", "缺少相依外掛", "Missing dependency plugin", "依存プラグインが不足しています");
		Add("PluginScanFailureTitleDllNotFound", "找不到插件程序集", "找不到外掛組件", "Plugin assembly not found", "プラグインアセンブリが見つかりません");
		Add("PluginScanFailureTitleEntryTypeNotFound", "清单声明的入口类型不存在", "清單宣告的進入點類型不存在", "Declared entry type does not exist", "マニフェストで宣言されたエントリ型が存在しません");
		Add("PluginScanFailureTitleHostVersionOutOfRange", "宿主版本超出插件声明区间", "宿主版本超出外掛宣告區間", "Host version outside the declared range", "ホストのバージョンが宣言範囲外です");
		Add("PluginScanFailureTitleIdNotDeclared", "未找到插件标识", "找不到外掛識別碼", "No plugin ID found", "プラグイン識別子が見つかりません");
		Add("PluginScanFailureTitleInvalidIdFormat", "插件 ID 格式非法", "外掛 ID 格式不合法", "Invalid plugin ID format", "プラグイン ID の形式が不正です");
		Add("PluginScanFailureTitleManifestInvalid", "plugin.json 格式不正确", "plugin.json 格式不正確", "plugin.json is malformed", "plugin.json の形式が正しくありません");
		Add("PluginScanFailureTitleNoContractImplementation", "不是 StarPie 插件", "不是 StarPie 外掛", "Not a StarPie plugin", "StarPie プラグインではありません");
		Add("PluginScanFailureTitleNone", "正常", "正常", "Normal", "正常");
		Add("PluginScanFailureTitleNotDotNetAssembly", "不是 .NET 程序集", "不是 .NET 組件", "Not a .NET assembly", ".NET アセンブリではありません");
		Add("PluginScanFailureTitleNotIlOnly", "程序集含本机代码", "組件含原生程式碼", "Assembly contains native code", "アセンブリにネイティブコードが含まれています");
		Add("PluginScanFailureTitleReservedIdPrefix", "插件 ID 使用了保留前缀", "外掛 ID 使用了保留前綴", "Plugin ID uses a reserved prefix", "プラグイン ID が予約済みプレフィックスを使用しています");
		Add("PluginScanFailureTitleSha256Mismatch", "文件已损坏或被修改", "檔案已損毀或被修改", "File is corrupted or modified", "ファイルが破損または改変されています");
		Add("PluginScanFailureTitleTargetFrameworkMismatch", "目标框架不兼容", "目標框架不相容", "Incompatible target framework", "ターゲットフレームワークが非互換です");
		Add("PluginScanFailureTitleWrongArchitecture", "架构不匹配（需要 64 位）", "架構不符（需要 64 位元）", "Wrong architecture (64-bit required)", "アーキテクチャが一致しません（64 ビットが必要）");
		Add("PluginsActionBrokenHint", "⚠️ 原先引用的插件动作已不可用（插件可能已被停用或卸载），请重新选择。", "⚠️ 原先引用的外掛動作已無法使用（外掛可能已被停用或解除安裝），請重新選擇。", "⚠️ The plugin action this referred to is no longer available (the plugin may be disabled or uninstalled). Please choose another one.", "⚠️ 参照していたプラグイン動作は利用できません（プラグインが無効化または削除された可能性があります）。選び直してください。");
		Add("PluginsActionNotSelected", "当前动作尚未选定具体的插件动作。", "目前動作尚未選定具體的外掛動作。", "No specific plugin action has been selected for this action yet.", "この動作には具体的なプラグイン動作がまだ選択されていません。");
		Add("PluginsActionPluginNotFound", "未找到插件 {0}，请到「插件与扩展」页查看。", "找不到外掛 {0}，請到「外掛與擴充」頁查看。", "Plugin {0} was not found. Check the Plugins page.", "プラグイン {0} が見つかりません。「プラグイン」ページを確認してください。");
		Add("PluginsCandidateGone", "这枚候选已经不在扫描目录里了（可能刚被移走或改名）。已重新扫描，请再试一次。", "這枚候選已經不在掃描目錄裡了（可能剛被移走或改名）。已重新掃描，請再試一次。", "This candidate is no longer in the scan folder (it may have been moved or renamed). Rescanned, please try again.", "この候補はスキャンフォルダーに存在しません（移動または名前変更された可能性があります）。再スキャンしましたので、もう一度お試しください。");
		Add("PluginsCardActionCount", "贡献 {0} 个动作", "貢獻 {0} 個動作", "provides {0} action(s)", "動作 {0} 個を提供");
		Add("PluginsCardAuthor", "作者 {0}", "作者 {0}", "by {0}", "作者 {0}");
		Add("PluginsCardCapabilities", "声明能力：{0}", "宣告能力：{0}", "Declared capabilities: {0}", "宣言された機能：{0}");
		Add("PluginsCardEnableCheckBox", "启用", "啟用", "Enable", "有効化");
		Add("PluginsCardExternalPath", "外部路径 {0}", "外部路徑 {0}", "external path {0}", "外部パス {0}");
		Add("PluginsCardNotLoaded", "未加载", "未載入", "not loaded", "未読み込み");
		Add("PluginsCardPreloadCheckBox", "开机预加载", "開機預載入", "Preload", "起動時プリロード");
		Add("PluginsCardRestartReason", "旧程序集尚未从内存释放，重启 StarPie 后才会完全生效。", "舊組件尚未從記憶體釋放，重新啟動 StarPie 後才會完全生效。", "The old assembly is still held in memory; it takes full effect only after restarting StarPie.", "古いアセンブリがまだメモリ上に残っています。StarPie を再起動すると完全に反映されます。");
		Add("PluginsCardSettingsButton", "⚙ 设置", "⚙ 設定", "⚙ Settings", "⚙ 設定");
		Add("PluginsCardSigned", "已签名", "已簽章", "signed", "署名済み");
		Add("PluginsCardUninstallButton", "🗑 卸载", "🗑 解除安裝", "🗑 Uninstall", "🗑 アンインストール");
		Add("PluginsCardUnsigned", "未签名", "未簽章", "unsigned", "未署名");
		Add("PluginsConfirmAboutToInstall", "即将安装：{0} {1}", "即將安裝：{0} {1}", "About to install: {0} {1}", "インストール予定：{0} {1}");
		Add("PluginsConfirmAccept", "点击「确定」表示你已了解并接受以上风险。", "點擊「確定」表示你已了解並接受以上風險。", "Clicking OK means you understand and accept these risks.", "「OK」を押すと、以上のリスクを理解し受け入れたものとみなします。");
		Add("PluginsConfirmActDowngrade", "⚠️ 这会用更旧的版本覆盖现有安装。除非你明确需要退回旧版，否则不建议继续。", "⚠️ 這會用更舊的版本覆蓋現有安裝。除非你明確需要退回舊版，否則不建議繼續。", "⚠️ This overwrites the existing installation with an older version. Not recommended unless you specifically need to roll back.", "⚠️ これにより、より古い版で既存のインストールを上書きします。旧版へ戻す必要が明確でない限り推奨しません。");
		Add("PluginsConfirmActExternal", "这个 ID 目前由「外部路径登记」占用（见上方扫描结果）。继续安装会改由数据目录里的副本接管。", "這個 ID 目前由「外部路徑登記」佔用（見上方掃描結果）。繼續安裝會改由資料目錄裡的副本接管。", "This ID is currently held by an external-path registration (see the scan result above). Continuing makes the copy in the data folder take over.", "この ID は現在「外部パス登録」が使用しています（上のスキャン結果を参照）。続行すると、データフォルダー内のコピーが引き継ぎます。");
		Add("PluginsConfirmActFresh", "这是全新安装，复制进去不会动到已有的任何插件。", "這是全新安裝，複製進去不會動到既有的任何外掛。", "This is a fresh install; nothing already installed is touched.", "これは新規インストールです。既存のプラグインには影響しません。");
		Add("PluginsConfirmActInstalled", "已装的那份与这枚文件完全相同（同版本、同内容），继续安装只会把同样的文件再复制一遍。", "已裝的那份與這枚檔案完全相同（同版本、同內容），繼續安裝只會把同樣的檔案再複製一遍。", "What is installed is identical to this file (same version, same content); continuing only copies the same file again.", "インストール済みのものとこのファイルは完全に同一です（同じバージョン・同じ内容）。続行しても同じファイルをコピーし直すだけです。");
		Add("PluginsConfirmActReplaced", "这会覆盖现有安装：版本号相同，但文件内容不同（重编译或手改过）。", "這會覆蓋現有安裝：版本號相同，但檔案內容不同（重新編譯或手動改過）。", "This overwrites the existing installation: same version number, different file content (rebuilt or hand-edited).", "これは既存のインストールを上書きします（バージョンは同じでも、ファイルの内容が異なります）。");
		Add("PluginsConfirmActUpdate", "这会用较新的版本覆盖现有安装。如果插件正在运行，宿主会先自动停用它再替换文件。", "這會用較新的版本覆蓋現有安裝。如果外掛正在執行，宿主會先自動停用它再取代檔案。", "This overwrites the existing installation with a newer version. If the plugin is running, StarPie disables it first, then replaces the files.", "これにより、より新しい版で既存のインストールを上書きします。プラグインが実行中の場合は、先に自動で無効化してからファイルを置き換えます。");
		Add("PluginsConfirmActVersionUnknown", "已装版本与这枚文件的版本号至少有一侧无法解析，判断不出新旧 —— 继续安装会直接覆盖现有安装。", "已裝版本與這枚檔案的版本號至少有一側無法解析，判斷不出新舊 —— 繼續安裝會直接覆蓋現有安裝。", "At least one of the version numbers cannot be parsed, so newer/older cannot be determined — continuing overwrites the existing installation.", "既存版とこのファイルのバージョン番号の少なくとも一方が解釈できないため、新旧を判断できません。続行すると既存のインストールを上書きします。");
		Add("PluginsConfirmAuthor", "作者：{0}", "作者：{0}", "Author: {0}", "作者：{0}");
		Add("PluginsConfirmCapabilities", "该插件声明了以下能力：", "此外掛宣告了以下能力：", "This plugin declares the following capabilities:", "このプラグインは以下の機能を宣言しています：");
		Add("PluginsConfirmDeclaredCapabilities", "声明能力：{0}", "宣告能力：{0}", "Declared capabilities: {0}", "宣言された機能：{0}");
		Add("PluginsConfirmDescription", "说明：{0}", "說明：{0}", "Description: {0}", "説明：{0}");
		Add("PluginsConfirmDisable", "确定停用「{0}」吗？\n\n· 当前配置中有 {1} 个动作由它提供，停用期间这些动作会暂时失效\n· 配置不会丢失，重新启用即可恢复", "確定停用「{0}」嗎？\n\n· 目前設定中有 {1} 個動作由它提供，停用期間這些動作會暫時失效\n· 設定不會遺失，重新啟用即可恢復", "Disable plugin {0}?\n\n· {1} action(s) in the current configuration come from it and will stop working while it is disabled\n· Nothing is lost from your configuration; re-enabling restores them", "プラグイン「{0}」を無効化しますか？\n\n· 現在の設定には、これが提供する動作が {1} 個あり、無効化中は使用できなくなります\n· 設定は失われません。再度有効化すれば復元します");
		Add("PluginsConfirmDisableTitle", "停用插件", "停用外掛", "Disable plugin", "プラグインを無効化");
		Add("PluginsConfirmEnableLater", "装完处于「未启用」状态：需要你到插件列表里勾选启用，它注册的动作才会出现在「手势与动作」页的动作类型下拉框中。", "裝完處於「未啟用」狀態：需要你到外掛清單裡勾選啟用，它註冊的動作才會出現在「手勢與動作」頁的動作類型下拉選單中。", "It stays disabled after installation: tick \"Enabled\" in the plugin list, and only then do its actions appear in the action-type dropdown on the Gestures & Actions page.", "インストール直後は「無効」のままです。プラグイン一覧で有効化してはじめて、登録した動作が「ジェスチャーと動作」ページの動作タイプのドロップダウンに現れます。");
		Add("PluginsConfirmEnableNow", "装完会立即启用。", "裝完會立即啟用。", "It will be enabled right after installation.", "インストール後すぐに有効化されます。");
		Add("PluginsConfirmFile", "文件：{0}", "檔案：{0}", "File: {0}", "ファイル：{0}");
		Add("PluginsConfirmFileSize", "文件大小：{0}", "檔案大小：{0}", "File size: {0}", "ファイルサイズ：{0}");
		Add("PluginsConfirmMachine", "平台架构：{0}", "平台架構：{0}", "Platform architecture: {0}", "プラットフォーム：{0}");
		Add("PluginsConfirmManifestSource", "清单来源：{0}", "清單來源：{0}", "Manifest source: {0}", "マニフェストの取得元：{0}");
		Add("PluginsConfirmNoCapabilities", "无", "無", "none", "なし");
		Add("PluginsConfirmPluginId", "插件 ID：{0}", "外掛 ID：{0}", "Plugin ID: {0}", "プラグイン ID：{0}");
		Add("PluginsConfirmScanResult", "扫描结果：{0}", "掃描結果：{0}", "Scan result: {0}", "スキャン結果：{0}");
		Add("PluginsConfirmSecurityBody", "插件会以 StarPie 当前的权限在你的电脑上运行代码，请只安装你信任的来源。", "外掛會以 StarPie 目前的權限在你的電腦上執行代碼，請只安裝你信任的來源。", "Plugins run code on your computer with StarPie's own privileges, so only install sources you trust.", "プラグインは StarPie と同じ権限でお使いの PC 上でコードを実行します。信頼できる提供元のみインストールしてください。");
		Add("PluginsConfirmSecurityTitle", "⚠️ 安全提示", "⚠️ 安全提示", "⚠️ Security notice", "⚠️ セキュリティ上の注意");
		Add("PluginsConfirmSha256", "SHA256：{0}…", "SHA256：{0}…", "SHA256: {0}…", "SHA256：{0}…");
		Add("PluginsConfirmSignature", "数字签名：{0}", "數位簽章：{0}", "Digital signature: {0}", "デジタル署名：{0}");
		Add("PluginsConfirmTargetFramework", "目标框架：{0}", "目標框架：{0}", "Target framework: {0}", "ターゲットフレームワーク：{0}");
		Add("PluginsConfirmTargetPath", "拟安装到：{0}", "擬安裝至：{0}", "Will be installed to: {0}", "インストール先：{0}");
		Add("PluginsConfirmTitle", "确认安装插件", "確認安裝外掛", "Confirm plugin installation", "プラグインのインストール確認");
		Add("PluginsConfirmUninstall", "确定要卸载插件 {0} 吗？\n\n· 插件文件与它自己的配置会被删除\n· 已经分配到轮盘上的插件动作会保留，但触发时会提示「插件不可用」\n\n此操作不可撤销。", "確定要解除安裝外掛 {0} 嗎？\n\n· 外掛檔案與它自己的設定會被刪除\n· 已經分配到轉盤上的外掛動作會保留，但觸發時會提示「外掛無法使用」\n\n此操作無法復原。", "Uninstall plugin {0}?\n\n· The plugin files and its own settings will be deleted\n· Plugin actions already assigned to your wheels are kept, but they will report that the plugin is unavailable when triggered\n\nThis cannot be undone.", "プラグイン {0} をアンインストールしますか？\n\n· プラグインのファイルと独自の設定が削除されます\n· ホイールに割り当て済みのプラグイン動作は残りますが、実行時にプラグインが利用できない旨が表示されます\n\nこの操作は取り消せません。");
		Add("PluginsConfirmUninstallTitle", "卸载插件", "解除安裝外掛", "Uninstall plugin", "プラグインをアンインストール");
		Add("PluginsConfirmUnsigned", "无（未签名）", "無（未簽章）", "None (unsigned)", "なし（未署名）");
		Add("PluginsDataDirectoryHint", "数据目录：{0}", "資料目錄：{0}", "Data folder: {0}", "データフォルダー：{0}");
		Add("PluginsDisableFailed", "停用插件 {0} 失败：\n\n{1}", "停用外掛 {0} 失敗：\n\n{1}", "Failed to disable plugin {0}:\n\n{1}", "プラグイン {0} の無効化に失敗しました：\n\n{1}");
		Add("PluginsDisabledNotice", "插件系统已关闭。\n\n已经分配到轮盘上的 {0} 个插件动作会原样保留，但触发时不会执行。\n仍在运行的插件任务会收到取消信号并由宿主继续追踪。", "外掛系統已關閉。\n\n已經分配到轉盤上的 {0} 個外掛動作會原樣保留，但觸發時不會執行。\n仍在執行的外掛工作會收到取消訊號並由宿主繼續追蹤。", "The plugin system is now off.\n\nThe {0} plugin action(s) already assigned to your wheels are kept, but they will not run when triggered.\nPlugin tasks still running will get a cancel signal, and the host keeps tracking them.", "プラグインシステムをオフにしました。\n\nホイールに割り当て済みの {0} 個のプラグイン動作はそのまま残りますが、実行されません。\n実行中のプラグイン処理にはキャンセルが通知され、ホストが引き続き追跡します。");
		Add("PluginsEmptyHint", "把插件 .dll 放进程序目录的 plugin 文件夹并点上方「重新扫描」，或直接点右上角「安装插件 (.dll)」选择文件", "把外掛 .dll 放進程式目錄的 plugin 資料夾並點上方「重新掃描」，或直接點右上角「安裝外掛 (.dll)」選擇檔案", "Drop the plugin .dll into the \"plugin\" folder next to the program and hit \"Rescan\" above, or click \"Install Plugin (.dll)\" at the top right to pick a file", "プラグインの .dll をプログラムフォルダー内の plugin フォルダーに置いて上の「再スキャン」を押すか、右上の「プラグインをインストール (.dll)」でファイルを選択してください");
		Add("PluginsEmptyTitle", "还没有安装任何插件", "還沒有安裝任何外掛", "No plugins installed yet", "プラグインはまだインストールされていません");
		Add("PluginsEnableCheckBox", "启用插件系统", "啟用外掛系統", "Enable plugin system", "プラグイン機能を有効にする");
		Add("PluginsEnableFailed", "启用插件 {0} 失败：\n\n{1}", "啟用外掛 {0} 失敗：\n\n{1}", "Failed to enable plugin {0}:\n\n{1}", "プラグイン {0} の有効化に失敗しました：\n\n{1}");
		Add("PluginsEnumSeparator", "、", "、", ", ", "、");
		Add("PluginsInstallButton", "➕ 手动安装社区插件 (.dll)...", "➕ 手動安裝社群外掛 (.dll)...", "➕ Install Community Plugin (.dll)...", "➕ コミュニティプラグインを手動インストール (.dll)...");
		Add("PluginsInstallFailed", "安装失败：{0}", "安裝失敗：{0}", "Install failed: {0}", "インストールに失敗しました：{0}");
		Add("PluginsInstalledDisabled", "插件 {0} 已安装。\n\n它当前处于「未启用」状态。在列表里勾选「启用」后，它注册的动作才会出现在「手势与动作」页的动作类型下拉框中，从而可以分配到轮盘上。", "外掛 {0} 已安裝。\n\n它目前處於「未啟用」狀態。在清單裡勾選「啟用」後，它註冊的動作才會出現在「手勢與動作」頁的動作類型下拉選單中，從而可以分配到輪盤上。", "Plugin {0} is installed.\n\nIt is currently disabled. Tick \"Enabled\" in the list, and only then do its actions appear in the action-type dropdown on the Gestures & Actions page, where you can assign them to the wheel.", "プラグイン {0} をインストールしました。\n\n現在は「無効」の状態です。一覧で「有効」にチェックを入れてはじめて、登録した動作が「ジェスチャーと動作」ページの動作タイプのドロップダウンに現れ、ホイールに割り当てられるようになります。");
		Add("PluginsInstalledNotify", "{0} 安装完成，到列表中启用它即可使用。", "{0} 安裝完成，到清單中啟用它即可使用。", "{0} installed — enable it in the list to start using it.", "{0} をインストールしました。一覧で有効化すると使えます。");
		Add("PluginsMsgTitle", "StarPie 插件", "StarPie 外掛", "StarPie Plugins", "StarPie プラグイン");
		Add("PluginsNotAPlugin", "这个文件不能作为 StarPie 插件安装。\n\n原因：{0}\n详情：{1}\n\n建议：{2}\n\n文件：{3}", "這個檔案不能作為 StarPie 外掛安裝。\n\n原因：{0}\n詳情：{1}\n\n建議：{2}\n\n檔案：{3}", "This file cannot be installed as a StarPie plugin.\n\nReason: {0}\nDetails: {1}\n\nSuggestion: {2}\n\nFile: {3}", "このファイルは StarPie プラグインとしてインストールできません。\n\n理由：{0}\n詳細：{1}\n\n推奨：{2}\n\nファイル：{3}");
		Add("PluginsNotReady", "插件系统尚未完成初始化。请稍候片刻再试，或重启 StarPie。", "外掛系統尚未完成初始化。請稍候片刻再試，或重新啟動 StarPie。", "The plugin system has not finished initializing yet. Please wait a moment and try again, or restart StarPie.", "プラグインシステムの初期化が完了していません。しばらく待ってから再試行するか、StarPie を再起動してください。");
		Add("PluginsOfficialActionInstall", "⬇️ 下载并安装", "⬇️ 下載並安裝", "⬇️ Download and install", "⬇️ ダウンロードしてインストール");
		Add("PluginsOfficialActionInstalled", "已安装", "已安裝", "Installed", "インストール済み");
		Add("PluginsOfficialActionUpdate", "⬆️ 更新", "⬆️ 更新", "⬆️ Update", "⬆️ 更新");
		Add("PluginsOfficialCatalogInfo", "目录 {0} · {1} 个模块 · 来源 StarPie-Official-Plugins", "目錄 {0} · {1} 個模組 · 來源 StarPie-Official-Plugins", "Catalog {0} · {1} modules · from StarPie-Official-Plugins", "カタログ {0} · {1} モジュール · 提供元 StarPie-Official-Plugins");
		Add("PluginsOfficialClaimSeparator", "、", "、", ", ", "、");
		Add("PluginsOfficialHeader", "官方插件", "官方外掛", "Official plugins", "公式プラグイン");
		Add("PluginsOfficialInstallFailed", "官方插件 {0} 安装失败：\n\n{1}", "官方外掛 {0} 安裝失敗：\n\n{1}", "Failed to install official plugin {0}:\n\n{1}", "公式プラグイン {0} のインストールに失敗しました：\n\n{1}");
		Add("PluginsOfficialInstalled", "官方插件 {0} v{1} 已下载、校验并启用。", "官方外掛 {0} v{1} 已下載、校驗並啟用。", "Official plugin {0} v{1} has been downloaded, verified and enabled.", "公式プラグイン {0} v{1} をダウンロード・検証し、有効化しました。");
		Add("PluginsOfficialLoading", "正在从 GitHub 获取官方插件目录…", "正在從 GitHub 取得官方外掛目錄…", "Fetching the official plugin catalog from GitHub…", "GitHub から公式プラグインカタログを取得しています…");
		Add("PluginsOfficialMsgTitle", "StarPie 官方插件", "StarPie 官方外掛", "StarPie official plugins", "StarPie 公式プラグイン");
		Add("PluginsOfficialRefreshButton", "🌐 刷新目录", "🌐 重新整理目錄", "🌐 Refresh catalog", "🌐 カタログを更新");
		Add("PluginsOfficialStateNotInstalled", "未安装", "尚未安裝", "Not installed", "未インストール");
		Add("PluginsOfficialStateUpToDate", "已是最新", "已是最新", "Up to date", "最新です");
		Add("PluginsOfficialStateUpdateAvailable", "已装 v{0} · 有更新", "已裝 v{0} · 有更新", "v{0} installed · update available", "v{0} 導入済み · 更新あり");
		Add("PluginsOfficialStatusHint", "从 StarPie-Official-Plugins 下载经过 SHA-256 校验的官方模块", "從 StarPie-Official-Plugins 下載經過 SHA-256 校驗的官方模組", "Official modules are downloaded from StarPie-Official-Plugins and verified with SHA-256", "公式モジュールは StarPie-Official-Plugins からダウンロードし、SHA-256 で検証します");
		Add("PluginsOfficialSummaryFallback", "官方动作模块", "官方動作模組", "Official action module", "公式アクションモジュール");
		Add("PluginsOfficialUnavailable", "官方插件目录暂时不可用：{0}", "官方外掛目錄暫時無法使用：{0}", "The official plugin catalog is temporarily unavailable: {0}", "公式プラグインカタログは一時的に利用できません：{0}");
		Add("PluginsOpenDataFolderButton", "📂 打开数据目录", "📂 開啟資料目錄", "📂 Open Data Folder", "📂 データフォルダーを開く");
		Add("PluginsOpenDataFolderFailed", "打开插件目录失败：{0}", "開啟外掛目錄失敗：{0}", "Failed to open the plugin folder: {0}", "プラグインフォルダーを開けませんでした：{0}");
		Add("PluginsOpenScanFolderButton", "📂 打开扫描目录", "📂 開啟掃描目錄", "📂 Open Scan Folder", "📂 スキャンフォルダーを開く");
		Add("PluginsOpenScanFolderFailed", "打开扫描目录失败：{0}", "開啟掃描目錄失敗：{0}", "Failed to open the scan folder: {0}", "スキャンフォルダーを開けませんでした：{0}");
		Add("PluginsPageSubheader", "官方插件从 StarPie-Official-Plugins 下载并校验；社区插件仍可手动选择 .dll 安装。插件以 StarPie 当前权限在进程内运行，请只安装你信任的来源。", "官方外掛從 StarPie-Official-Plugins 下載並校驗；社群外掛仍可手動選擇 .dll 安裝。外掛以 StarPie 目前權限在行程內執行，請只安裝你信任的來源。", "Official plugins are downloaded and verified from StarPie-Official-Plugins; community plugins can still be installed manually. Plugins run in-process with StarPie's own privileges, so only install sources you trust.", "公式プラグインは StarPie-Official-Plugins からダウンロードして検証します。コミュニティプラグインは引き続き .dll を手動で選択できます。プラグインは StarPie と同じ権限で実行されるため、信頼できる提供元のみインストールしてください。");
		Add("PluginsPanelAllOptional", "此动作的参数全部可选。", "此動作的參數全部可選。", "All parameters of this action are optional.", "この動作のパラメーターはすべて任意です。");
		Add("PluginsPanelContributionId", "贡献点 ID：{0}", "貢獻點 ID：{0}", "Contribution ID: {0}", "提供ポイント ID：{0}");
		Add("PluginsPanelExecutionMode", "执行方式：{0}", "執行方式：{0}", "Runs: {0}", "実行方式：{0}");
		Add("PluginsPanelIssuesCount", "还有 {0} 个参数不合法，触发时会被拦下。", "還有 {0} 個參數不合法，觸發時會被攔下。", "{0} parameter(s) are still invalid; the trigger will be blocked.", "まだ {0} 個のパラメーターが不正です。実行時にブロックされます。");
		Add("PluginsPanelKindBackground", "后台并发（不占用动作线程）", "背景並行（不佔用動作執行緒）", "background, concurrent (does not hold the action thread)", "バックグラウンド並行（動作スレッドを占有しません）");
		Add("PluginsPanelKindSerial", "串行（占用动作线程）", "序列（佔用動作執行緒）", "serial (holds the action thread)", "直列（動作スレッドを占有します）");
		Add("PluginsPanelNotChosenEmpty", "当前没有可用的插件动作。请先到「插件与扩展」页安装并启用插件，再回到这里选择。", "目前沒有可用的外掛動作。請先到「外掛與擴充」頁安裝並啟用外掛，再回到這裡選擇。", "No plugin actions are available. Install and enable a plugin on the plugins page first, then come back here to choose one.", "利用できるプラグイン動作がありません。先に「プラグインと拡張」ページでプラグインをインストールして有効化し、ここに戻って選択してください。");
		Add("PluginsPanelNotChosenPick", "尚未选定具体的插件动作。请在上方「插件动作」下拉框中选择 —— 候选动作按插件分组，同一插件的动作都归在它以自己名字命名的那个分组下。", "尚未選定具體的外掛動作。請在上方「外掛動作」下拉選單中選擇 —— 候選動作依外掛分組，同一外掛的動作都歸在它以自己名字命名的那個分組下。", "No plugin action selected yet. Pick one in the plugin action dropdown above — candidates are grouped by plugin, and every action of a plugin lives under the group named after it.", "具体的なプラグイン動作が未選択です。上の「プラグイン動作」ドロップダウンで選択してください —— 候補はプラグインごとにまとまっており、同じプラグインの動作はその名前のグループに入っています。");
		Add("PluginsPanelProvider", "提供插件：{0}", "提供外掛：{0}", "Plugin: {0}", "提供プラグイン：{0}");
		Add("PluginsPanelProviderWithId", "提供插件：{0}（{1}）", "提供外掛：{0}（{1}）", "Plugin: {0} ({1})", "提供プラグイン：{0}（{1}）");
		Add("PluginsPanelRequiredParams", "此动作有 {0} 个必填参数，留空会在触发时被拦下。", "此動作有 {0} 個必填參數，留空會在觸發時被攔下。", "This action has {0} required parameter(s); leaving them empty blocks the trigger.", "この動作には必須パラメーターが {0} 個あります。空欄のまま実行するとブロックされます。");
		Add("PluginsPanelTimeout", "超时：{0} 秒", "逾時：{0} 秒", "Timeout: {0} s", "タイムアウト：{0} 秒");
		Add("PluginsPanelUnavailable", "所引用的插件动作当前不可用：{0}\n可能是该插件已被停用或卸载，也可能是插件升级后移除了这个动作。\n到「插件与扩展」页确认插件状态，或直接在上方「插件动作」下拉框里改选另一个动作。", "所引用的外掛動作目前無法使用：{0}\n可能是該外掛已被停用或解除安裝，也可能是外掛升級後移除了這個動作。\n到「外掛與擴充」頁確認外掛狀態，或直接在上方「外掛動作」下拉選單裡改選另一個動作。", "The referenced plugin action is currently unavailable: {0}\nThe plugin may have been disabled or uninstalled, or an upgrade removed this action.\nCheck the plugin's state on the plugins page, or pick another action in the dropdown above.", "参照しているプラグイン動作は現在利用できません：{0}\nプラグインが無効化／アンインストールされたか、更新でこの動作が削除された可能性があります。\n「プラグインと拡張」ページで状態を確認するか、上の「プラグイン動作」ドロップダウンで別の動作を選び直してください。");
		Add("PluginsPanelUnavailableHint", "⚠️ 触发时会明确提示「插件动作不可用」，不会静默无操作。", "⚠️ 觸發時會明確提示「外掛動作無法使用」，不會靜默無操作。", "⚠️ Triggering it reports \"plugin action unavailable\" — it will not silently do nothing.", "⚠️ 実行時は「プラグイン動作を利用できません」と明示されます。無言で何も起きることはありません。");
		Add("PluginsPickDllFilter", "插件程序集 (*.dll)|*.dll|所有文件 (*.*)|*.*", "外掛組件 (*.dll)|*.dll|所有檔案 (*.*)|*.*", "Plugin assemblies (*.dll)|*.dll|All files (*.*)|*.*", "プラグイン アセンブリ (*.dll)|*.dll|すべてのファイル (*.*)|*.*");
		Add("PluginsPickDllTitle", "选择要安装的插件 (.dll)", "選擇要安裝的外掛 (.dll)", "Select a plugin to install (.dll)", "インストールするプラグインを選択 (.dll)");
		Add("PluginsPreloadBlockedSafeMode", "插件系统处于安全模式", "外掛系統處於安全模式", "the plugin system is in safe mode", "プラグイン機能がセーフモード中です");
		Add("PluginsPreloadBlockedSystemOff", "插件系统未启用", "外掛系統未啟用", "the plugin system is disabled", "プラグイン機能が有効化されていません");
		Add("PluginsPreloadCheckBox", "启动时预加载", "啟動時預載入", "Preload at startup", "起動時にプリロード");
		Add("PluginsPreloadCheckBoxToolTip", "开启后，启动完成数秒会在后台加载勾选了「开机预加载」的已启用插件；关闭时所有插件保持按需惰性加载，不增加启动与内存成本。", "開啟後，啟動完成數秒會在背景載入勾選了「開機預載入」的已啟用外掛；關閉時所有外掛維持依需求延遲載入，不增加啟動與記憶體成本。", "When on, enabled plugins marked \"Preload\" are loaded in the background a few seconds after startup. When off, every plugin stays lazily loaded, with no startup or memory cost.", "オンにすると「起動時プリロード」を有効にした有効化プラグインが、起動完了数秒後にバックグラウンドで読み込まれます。オフの場合、すべてのプラグインは遅延読み込みのままとなり、起動時間もメモリも増加しません。");
		Add("PluginsPreloadFailed", "插件「{0}」已登记预加载，但当场加载未成功（不影响下次启动重试）：{1}", "外掛「{0}」已登記預載入，但當場載入未成功（不影響下次啟動重試）：{1}", "Plugin \"{0}\" is marked for preload, but loading it now failed (next startup will retry): {1}", "プラグイン「{0}」はプリロードとして登録されましたが、今回の読み込みに失敗しました（次回起動時に再試行されます）：{1}");
		Add("PluginsReadFileFailed", "读取所选文件时出错：\n{0}", "讀取所選檔案時發生錯誤：\n{0}", "Failed to read the selected file:\n{0}", "選択したファイルの読み込みに失敗しました:\n{0}");
		Add("PluginsReloadFailed", "重新加载失败：{0}", "重新載入失敗：{0}", "Reload failed: {0}", "再読み込みに失敗しました：{0}");
		Add("PluginsReloadNotStopped", "旧插件尚未完全停止，不能重新加载：\n\n{0}", "舊外掛尚未完全停止，不能重新載入：\n\n{0}", "The old plugin has not fully stopped, so it cannot be reloaded:\n\n{0}", "古いプラグインがまだ完全に停止していないため、再読み込みできません：\n\n{0}");
		Add("PluginsReloaded", "{0} 已重新加载。", "{0} 已重新載入。", "{0} reloaded.", "{0} を再読み込みしました。");
		Add("PluginsReloadedRestartNeeded", "{0} 已重新加载，但旧程序集未能立即从内存释放，需要重启 StarPie 才能完全生效。", "{0} 已重新載入，但舊組件未能立即從記憶體釋放，需要重新啟動 StarPie 才能完全生效。", "{0} reloaded, but the old assembly could not be released from memory right away; restart StarPie for the change to fully take effect.", "{0} を再読み込みしましたが、古いアセンブリをメモリから解放できませんでした。完全に反映するには StarPie を再起動してください。");
		Add("PluginsRescanButton", "🔄 重新扫描", "🔄 重新掃描", "🔄 Rescan", "🔄 再スキャン");
		Add("PluginsRescanCandidateHint", "扫描目录里另有 {0} 个可安装项。", "掃描目錄裡另有 {0} 個可安裝項目。", "There are also {0} installable items in the scan folder.", "スキャンフォルダーには他に {0} 件のインストール可能な項目があります。");
		Add("PluginsRescanFound", "扫描完成，新发现 {0} 个插件。{1}", "掃描完成，新發現 {0} 個外掛。{1}", "Scan complete: {0} new plugin(s) found. {1}", "スキャン完了。新しいプラグインを {0} 個検出しました。{1}");
		Add("PluginsRescanNone", "扫描完成，没有发现新插件。{0}", "掃描完成，沒有發現新外掛。{0}", "Scan complete: no new plugins found. {0}", "スキャン完了。新しいプラグインは見つかりませんでした。{0}");
		Add("PluginsSafeModeWarning", "⚠️ 安全模式：上次启动时插件引发异常，已自动禁用问题插件，避免反复崩溃。", "⚠️ 安全模式：上次啟動時外掛引發例外，已自動停用問題外掛，避免反覆崩潰。", "⚠️ Safe mode: a plugin threw an exception during the last startup. The offending plugin was disabled automatically to prevent repeated crashes.", "⚠️ セーフモード：前回の起動時にプラグインが例外を発生させたため、問題のあるプラグインを自動的に無効化しました。");
		Add("PluginsScanFolderMissing", "扫描目录还不存在：\n{0}\n\nStarPie 不会替你创建它 —— 程序可能装在只读位置，宿主对这里只读不写。\n如需使用随包附带的插件，请手工创建该文件夹，把插件 .dll 放进去，再点「重新扫描」。", "掃描目錄還不存在：\n{0}\n\nStarPie 不會替你建立它 —— 程式可能裝在唯讀位置，宿主對這裡唯讀不寫。\n如需使用隨附的外掛，請手動建立該資料夾，把外掛 .dll 放進去，再點「重新掃描」。", "The scan folder does not exist yet:\n{0}\n\nStarPie will not create it for you — the program may be installed in a read-only location, and StarPie never writes there.\nTo use the plugins shipped with the package, create the folder yourself, drop the plugin .dll into it, then click \"Rescan\".", "スキャンフォルダーがまだ存在しません：\n{0}\n\nStarPie が代わりに作成することはありません（読み取り専用の場所にインストールされている場合があり、ホストはここへ書き込みません）。\n同梱のプラグインを使う場合は、このフォルダーを手動で作成し、プラグインの .dll を置いてから「再スキャン」を押してください。");
		Add("PluginsScanHeaderFound", "扫描目录里发现 {0} 个 .dll，其中 {1} 个可以安装", "掃描目錄裡發現 {0} 個 .dll，其中 {1} 個可以安裝", "Found {0} .dll file(s) in the scan folder, {1} installable", "スキャンフォルダーに .dll が {0} 個あり、うち {1} 個がインストール可能です");
		Add("PluginsScanHeaderMissing", "扫描目录不存在（宿主不会创建它）", "掃描目錄不存在（宿主不會建立它）", "Scan folder does not exist (StarPie will not create it)", "スキャンフォルダーが存在しません（StarPie は作成しません）");
		Add("PluginsScanHeaderNone", "扫描目录里没有可安装的插件", "掃描目錄裡沒有可安裝的外掛", "No installable plugins in the scan folder", "スキャンフォルダーにインストール可能なプラグインはありません");
		Add("PluginsScanPathHint", "把插件 .dll 放进这个文件夹后点「重新扫描」即可识别。该目录由你自己创建：StarPie 装在只读位置时无权创建它。", "把外掛 .dll 放進這個資料夾後點「重新掃描」即可識別。該目錄由你自己建立：StarPie 裝在唯讀位置時無權建立它。", "Drop the plugin .dll into this folder and hit \"Rescan\" to pick it up. You create this folder yourself: StarPie has no permission to create it when installed in a read-only location.", "このフォルダーにプラグインの .dll を置いて「再スキャン」を押すと認識されます。このフォルダーはご自身で作成してください（StarPie が読み取り専用の場所にインストールされている場合、作成する権限がありません）。");
		Add("PluginsSettingsOpenFailed", "无法打开该插件的设置页，详情见日志。", "無法開啟該插件的設定頁，詳情請見記錄。", "Cannot open this plugin's settings page. See the log for details.", "このプラグインの設定ページを開けません。詳細はログをご覧ください。");
		Add("PluginsStateActive", "运行中", "執行中", "Running", "実行中");
		Add("PluginsStateActiveRestartPending", "运行中 · 待重启", "執行中 · 待重啟", "Running · restart pending", "実行中 · 再起動待ち");
		Add("PluginsStateDisabled", "未启用", "未啟用", "Not enabled", "無効");
		Add("PluginsStateEnabledPendingLoad", "已启用 · 待加载", "已啟用 · 待載入", "Enabled · pending load", "有効 · 読み込み待ち");
		Add("PluginsStateFailed", "加载失败", "載入失敗", "Load failed", "読み込み失敗");
		Add("PluginsStateFaulted", "运行异常", "執行異常", "Runtime error", "実行時エラー");
		Add("PluginsStateIncompatible", "不兼容", "不相容", "Incompatible", "非互換");
		Add("PluginsStateLoading", "加载中", "載入中", "Loading", "読み込み中");
		Add("PluginsStateQuarantined", "已隔离", "已隔離", "Quarantined", "隔離済み");
		Add("PluginsStateRestartPending", "待重启生效", "待重啟生效", "Restart required", "再起動で有効");
		Add("PluginsStateStopping", "正在停止", "正在停止", "Stopping", "停止中");
		Add("PluginsStatusEmpty", "尚未安装任何插件。{0}", "尚未安裝任何外掛。{0}", "No plugins installed yet. {0}", "プラグインはまだインストールされていません。{0}");
		Add("PluginsStatusSummary", "共 {0} 个插件，{1} 个已启用。{2}", "共 {0} 個外掛，{1} 個已啟用。{2}", "{0} plugin(s), {1} enabled. {2}", "プラグイン {0} 個、有効 {1} 個。{2}");
		Add("PluginsStoppingTitle", "插件正在后台停止", "外掛正在背景停止", "Plugin is stopping in the background", "プラグインはバックグラウンドで停止中");
		Add("PluginsUninstallFailed", "卸载失败：\n\n{0}", "解除安裝失敗：\n\n{0}", "Uninstall failed:\n\n{0}", "アンインストールに失敗しました：\n\n{0}");
		Add("PluginsUpdatedAndEnabled", "{0} 已更新到 {1} 并已启用。\n\n如果它之前已经在运行，旧程序集要到下次启动 StarPie 才会完全从内存释放。", "{0} 已更新到 {1} 並已啟用。\n\n如果它之前已經在執行，舊組件要到下次啟動 StarPie 才會完全從記憶體釋放。", "{0} was updated to {1} and enabled.\n\nIf it was already running, the old assembly stays in memory until the next StarPie restart.", "{0} を {1} に更新して有効化しました。\n\n既に実行中だった場合、古いアセンブリは次回 StarPie を起動するまでメモリに残ります。");
		Add("ProfileBrowseExe", "📁 浏览...", "📁 瀏覽...", "📁 Browse...", "📁 参照…");
		Add("ProfileCaptureWindow", "🎯 捕捉窗口...", "🎯 擷取視窗...", "🎯 Capture window...", "🎯 ウィンドウを取得…");
		Add("ProfilePickProgram", "🖥️ 软件库...", "🖥️ 軟體庫...", "🖥️ App library...", "🖥️ アプリ一覧…");
		Add("ResetProcessTrigger", "🔄 恢复默认", "🔄 恢復預設", "🔄 Restore default", "🔄 既定に戻す");
		Add("ResetSubDimensions", "🔄 恢复二级轮盘默认尺寸", "🔄 恢復二級輪盤預設尺寸", "🔄 Restore default tier 2 size", "🔄 第2階層の既定サイズに戻す");
		Add("ResetSubTheme", "🔄 恢复与一级轮盘相同主题", "🔄 恢復與一級輪盤相同主題", "🔄 Match tier 1 theme", "🔄 第1階層と同じテーマに戻す");
		Add("ResetTextOffset", "🔄 位置归位", "🔄 位置歸位", "🔄 Reset position", "🔄 位置を初期化");
		Add("RestoreSystemAudio", "🔊 一键解除静音并恢复音量 (50%)", "🔊 一鍵解除靜音並恢復音量 (50%)", "🔊 Unmute and restore volume (50%)", "🔊 ミュート解除して音量を復元（50%）");
		Add("SectorFontSizeTitle", "文字字号大小:", "文字字號大小:", "Text size:", "文字サイズ:");
		Add("SectorIconSizeTitle", "图标尺寸大小:", "圖示尺寸大小:", "Icon size:", "アイコンサイズ:");
		Add("SectorTextPlacementTitle", "文字相对位置:", "文字相對位置:", "Text position:", "文字の位置:");
		Add("ShowCoreIcon", "启用中心图案/图标显示", "啟用中心圖案/圖示顯示", "Show centre pattern / icon", "中央の図柄／アイコンを表示");
		Add("StartDownloadUpdate", "⬇️ 立即下载更新", "⬇️ 立即下載更新", "⬇️ Download update", "⬇️ 更新をダウンロード");
		Add("TabPlugins", "插件与扩展", "外掛與擴充", "Plugins", "プラグイン");
		Add("TestCancelAction", "🧪 模拟测试触发", "🧪 模擬測試觸發", "🧪 Test trigger", "🧪 テスト実行");
		Add("Tier2DimensionsExpander", "🌐 二级轮盘几何形态与尺寸 (展开微调)", "🌐 二級輪盤幾何形態與尺寸 (展開微調)", "🌐 Tier 2 geometry and size (expand to fine-tune)", "🌐 第2階層の形状とサイズ（展開して微調整）");
		Add("Tier2ThemeExpander", "🌐 二级轮盘风格与配色 (展开定制)", "🌐 二級輪盤風格與配色 (展開自訂)", "🌐 Tier 2 style and colours (expand to customise)", "🌐 第2階層のスタイルと配色（展開してカスタマイズ）");
		Add("UpdatePkgLightweight", "依赖 .NET 8 运行时轻量版 (~2.7 MB)", "依賴 .NET 8 執行階段輕量版 (~2.7 MB)", "Lightweight, needs .NET 8 runtime (~2.7 MB)", "軽量版・.NET 8 ランタイムが必要（約 2.7 MB）");
		Add("UpdatePkgStandalone", "独立免安装单文件版 (~68 MB, 推荐)", "獨立免安裝單檔案版 (~68 MB, 推薦)", "Standalone, no install needed (~68 MB, recommended)", "単体動作・インストール不要（約 68 MB、推奨）");
		Add("ViewReleasesWeb", "🌐 网页发布页", "🌐 網頁發佈頁", "🌐 Release page", "🌐 リリースページ");
		// --- Tab4 里程碑卡片 (v1.8.0-beta.2, v1.8.0-beta.1 与 v1.7.4-beta.4) ---
		Add("Tab4_Ms_180b2_Title", "v1.8.0-beta.2 全控制台四语系深度本土化 & LocalizedString 扁平零堆分配 & 交互防残留校验", "v1.8.0-beta.2 全控制台四語系深度本土化 & LocalizedString 扁平零堆配置 & 互動防殘留校驗", "v1.8.0-beta.2 Full Console 4-Language Localization & LocalizedString Zero-Allocation & Residual Validation", "v1.8.0-beta.2 コンソール全体4言語完全対応・LocalizedString ゼロアロケーション・整合性検証ガード");
		Add("Tab4_Ms_180b2_P1", "• 🌐 【全设置控制台 100% 四语系本土化】：全面覆盖触发设置、外观样式、手势动作、系统设置、关于软件全量标签页，重构 OCR 配置与结果浮窗为完整国际化与主题联动；", "• 🌐 【全設定控制台 100% 四語系本土化】：全面覆蓋觸發設定、外觀樣式、手勢動作、系統設定、關於軟體全量分頁，重構 OCR 設定與結果浮窗為完整國際化與主題連動；", "• 🌐 [100% 4-Language Console Localization]: Comprehensive coverage across Trigger, Appearance, Gestures, System, and About tabs; OCR settings and floating windows fully internationalized with theme linkage;", "• 🌐 【全コンソール100% 4言語完全対応】：トリガー、外観、ジェスチャー、システム設定、アバウトの全タブを網羅。OCR設定・結果ウィンドウも完全多言語化＆テーマ連動に対応；");
		Add("Tab4_Ms_180b2_P2", "• ⚡ 【LocalizedString 扁平结构与零堆分配】：I18n 核心字典重构为 16 字节只读紧凑结构体，彻底消除动态切语言时的 GC 抖动与内存碎片，实现毫秒级即时热刷新；", "• ⚡ 【LocalizedString 扁平結構與零堆配置】：I18n 核心字典重構為 16 位元組唯讀緊湊結構體，徹底消除動態切語言時的 GC 抖動與記憶體碎片，實現毫秒級即時熱重新整理；", "• ⚡ [LocalizedString Flat Zero-Allocation]: I18n core refactored to 16-byte readonly compact structs, eliminating GC spikes and memory fragmentation on language switch for instant hot-refresh;", "• ⚡ 【LocalizedString フラット構造・ゼロアロケーション】：I18n 辞書を16バイトの軽量構造体に再構築し、言語切り替え時の GC 負荷をゼロにしてミリ秒級の即時反映を実現；");
		Add("Tab4_Ms_180b2_P3", "• 🎛️ 【侧边栏 2x2 主题矩阵与动态下拉框热刷新】：侧边栏主题切换升级为 2x2 胶囊矩阵；全量动态下拉框 ComboBox 支持四语系即时热刷新与状态持久化；", "• 🎛️ 【側邊欄 2x2 主題矩陣與動態下拉選單熱重新整理】：側邊欄主題切換升級為 2x2 膠囊矩陣；全量動態下拉選單 ComboBox 支援四語系即時熱重新整理與狀態持久化；", "• 🎛️ [2x2 Theme Matrix & Dynamic ComboBox Hot-Refresh]: Sidebar theme selector modernized into a 2x2 capsule matrix; all dynamic ComboBoxes support instant multi-language hot-refresh with state persistence;", "• 🎛️ 【2x2 テーママトリックス＆コンボボックス即時更新】：サイドバーのテーマ選択を2x2カプセル型に刷新。全動的コンボボックスが言語切り替え時の即時反映に対応；");
		Add("Tab4_Ms_180b2_P4", "• 🛡️ 【Tab 4 里程碑规范与防残留校验机制】：建立 XAML、C# 与 I18n 三方严格一致性自动化校验机制，杜绝界面死链与孤儿残留词条。", "• 🛡️ 【Tab 4 里程碑規範與防殘留校驗機制】：建立 XAML、C# 與 I18n 三方嚴格一致性自動化校驗機制，杜絕介面死鏈與孤兒殘留詞條。", "• 🛡️ [Milestone Standards & Anti-Residual Validation]: Established strict 3-way consistency validator across XAML, C#, and I18n, completely eliminating broken bindings and orphan keys.", "• 🛡️ 【マイルストーン仕様＆整合性検証ガード】：XAML、C#、I18n の3者整合性を自動検証する仕組みを導入し、リンク切れや不要キーの残留を根絶。");
		Add("Tab4_Ms_180b1_Title", "v1.8.0-beta.1 插件系统首个公开测试版", "v1.8.0-beta.1 外掛系統首個公開測試版", "v1.8.0-beta.1 Plugin System First Public Beta", "v1.8.0-beta.1 プラグインシステム初の公開ベータ版");
		Add("Tab4_Ms_180b1_P1", "• 🧩 【官方动作插件化】：程序启动、命令、OCR、系统与窗口管理等动作拆分为独立模块，并统一通过插件路径执行；", "• 🧩 【官方動作外掛化】：程式啟動、命令、OCR、系統與視窗管理等動作拆分為獨立模組，並統一透過外掛路徑執行；", "• 🧩 [Official Actions as Plugins]: App launch, command, OCR, system and window management split into modular plugins with unified execution path;", "• 🧩 【公式アクションのプラグイン化】：アプリ起動、コマンド、OCR、システム制御、ウィンドウ管理を独立モジュール化し、統一プラグインパス経由で実行；");
		Add("Tab4_Ms_180b1_P2", "• 📦 【手动安装与集中管理】：新增插件管理页，支持刷新官方目录以及安装、启用、停用、更新和卸载，启动时不自动下载模块；", "• 📦 【手動安裝與集中管理】：新增外掛管理頁，支援重新整理官方目錄以及安裝、啟用、停用、更新和解除安裝，啟動時不自動下載模組；", "• 📦 [Manual Install & Unified Management]: New plugins tab supporting catalog refresh, installation, enabling, disabling, updates and removal without auto-downloading;", "• 📦 【手動インストールと集中管理】：プラグイン管理ページを新設。公式カタログの更新、インストール、有効/無効化、更新、削除に対応し、起動時の自動ダウンロードは行いません；");
		Add("Tab4_Ms_180b1_P3", "• 🧰 【社区 SDK 与参数表单】：开放插件契约、宿主服务和声明式参数系统，插件动作统一分组选择并继承主程序主题与多语言；", "• 🧰 【社群 SDK 與參數表單】：開放外掛協定、宿主服務和宣告式參數系統，外掛動作統一分組選取並繼承主程式主題與多語言；", "• 🧰 [Community SDK & Parameter Forms]: Open plugin contract, host services and declarative parameters; plugin actions grouped cleanly and inheriting themes and i18n;", "• 🧰 【コミュニティSDKとパラメータフォーム】：プラグイン契約、ホストサービス、宣言的パラメータを提供。プラグインアクションはグループ表示され、テーマと言語を継承；");
		Add("Tab4_Ms_180b1_P4", "• 🛡️ 【惰性加载与故障保护】：完善调用租约、异步停用、能力门禁、异常隔离和端到端自检，降低插件对轮盘核心运行的影响。", "• 🛡️ 【惰性載入與故障保護】：完善呼叫租約、非同步停用、能力門禁、例外隔離和端到端自我檢測，降低外掛對輪盤核心運行的影響。", "• 🛡️ [Lazy Loading & Fault Isolation]: Call leases, async deactivation, capability gating, crash resilience and end-to-end self-testing to safeguard radial wheel core.", "• 🛡️ 【遅延ロードと障害保護】：呼び出しリース、非同期停止、権限ゲート、例外分離、エンドツーエンドの自己診断を整備し、ホイール本体への影響を最小限に抑制。");
		Add("Tab4_Ms_174b4_Title", "v1.7.4-beta.4 多层轮盘全局继承解耦 & 同层精准映射与超层优雅回退", "v1.7.4-beta.4 多層輪盤全域繼承解耦 & 同層精準對應與超層優雅回退", "v1.7.4-beta.4 Multi-Layer Wheel Global Inheritance Decoupling & Precise Layer Mapping", "v1.7.4-beta.4 多階層ホイールのグローバル継承分離＆同階層マッピングと階層フォールバック");
		Add("Tab4_Ms_174b4_P1", "• 🧩 【全局层级状态污染消除】：重构动作继承求值链路，彻底解耦对全局方案当前浏览层指针的依赖，在控制台切换全局方案层数时不再污染其他应用程序方案的未配置槽位；", "• 🧩 【全域層級狀態污染消除】：重構動作繼承求值鏈路，徹底解耦對全域方案當前瀏覽層指標的依賴，在控制台切換全域方案層數時不再污染其他應用程式方案的未配置槽位；", "• 🧩 [Global Layer State Decoupling]: Refactored inheritance evaluation pipeline to eliminate dependency on global profile active browsing layer, preventing slot pollution across app profiles;", "• 🧩 【グローバル階層状態の汚染解消】：アクション継承の評価パスを再構築し、設定画面でグローバル階層を切り替えてもアプリ個別プロファイルのスロットを汚染しないよう完全分離；");
		Add("Tab4_Ms_174b4_P2", "• 🎯 【同层对应优先继承】：专属程序方案第 N 层的空白扇区与中心核心圆，优先对应继承全局方案第 N 层的动作与子动作配置；", "• 🎯 【同層對應優先繼承】：專屬程式方案第 N 層的空白扇區與中心核心圓，優先對應繼承全域方案第 N 層的動作與子動作配置；", "• 🎯 [Peer-Layer Priority Inheritance]: Empty sectors and core icon on Layer N of dedicated profiles prioritize inheriting configurations from Layer N of the global profile;", "• 🎯 【同階層優先継承】：個別アプリプロファイルの第N階層の空きセクターと中央コアは、グローバルプロファイルの第N階層のアクション設定を優先継承；");
		Add("Tab4_Ms_174b4_P3", "• 🪜 【多层超额优雅回退】：当程序方案层数多于全局方案时（如程序 3 层、全局 2 层），超额层的未配置槽位自动回退继承全局方案第 1 层动作，杜绝越界与空指针异常；", "• 🪜 【多層超額優雅回退】：當程式方案層數多於全域方案時（如程式 3 層、全域 2 層），超額層的未配置槽位自動回退繼承全域方案第 1 層動作，杜絕越界與空指標例外；", "• 🪜 [Graceful Overflow Fallback]: When an app profile has more layers than the global profile (e.g. 3 vs 2), excess unconfigured slots fallback gracefully to Layer 1 of the global profile;", "• 🪜 【超過階層のフォールバック】：アプリ個別設定の階層数がグローバル設定より多い場合（例: 個別3層、全体2層）、超過した未設定スロットはグローバルの第1層へ安全にフォールバック；");
		Add("Tab4_Ms_174b4_P4", "• ⚡ 【实时运行态切层同步】：桌面划动手势与滚轮切层时，继承动作跟随层级切换毫秒级即时重新评估与渲染。", "• ⚡ 【即時執行態切層同步】：桌面劃動手勢與滾輪切層時，繼承動作跟隨層級切換毫秒級即時重新評估與渲染。", "• ⚡ [Real-time Runtime Layer Sync]: During desktop gesture flicks or wheel-switching layers, inherited actions re-evaluate and render in milliseconds.", "• ⚡ 【実行時のリアルタイム階層同期】：ジェスチャーやホイール操作で階层を切り替える際、継承アクションをミリ秒単位で即座に再評価・レンダリング。");

		// --- 官方核心插件一键引导安装 (1.8.0+) ---
		Add("OfficialPluginNameFolder", "打开文件夹", "開啟資料夾", "Open Folder", "フォルダーを開く");
		Add("OfficialPluginDescFolder", "打开指定目录或文件管理器", "開啟指定目錄或檔案總管", "Open specified folder or file manager", "指定フォルダーまたはファイルマネージャーを開く");
		Add("OfficialPluginNameWebUrl", "打开网页", "開啟網頁", "Open Web URL", "Web ページを開く");
		Add("OfficialPluginDescWebUrl", "使用默认浏览器打开指定网页链接", "使用預設瀏覽器開啟指定網頁連結", "Open URL using default browser", "既定のブラウザーで指定の Web ページを開く");
		Add("OfficialPluginNameLaunch", "启动程序", "啟動程式", "Launch Program", "プログラム起動");
		Add("OfficialPluginDescLaunch", "运行指定应用程序、文件或快捷方式", "執行指定應用程式、檔案或捷徑", "Launch specified app, file, or shortcut", "指定アプリ、ファイル、ショートカットを起動");
		Add("OfficialPluginNameSystem", "系统控制", "系統控制", "System Control", "システム制御");
		Add("OfficialPluginDescSystem", "锁屏、睡眠、关机、音量调节与窗口管理", "鎖定螢幕、睡眠、關機、音量調節與視窗管理", "Lock screen, sleep, shutdown, audio volume, and window control", "画面ロック、スリープ、シャットダウン、音量調整、ウィンドウ管理");
		Add("OfficialPluginNameShellTool", "系统与右键工具", "系統與右鍵工具", "System & Context Menu Tools", "システムと右クリックツール");
		Add("OfficialPluginDescShellTool", "运行终端命令行与 Windows 右键上下文动作", "執行終端命令列與 Windows 右鍵捷徑操作", "Run terminal commands and Windows context menu actions", "ターミナルコマンドと Windows 右クリックメニュー操作を実行");

		Add("OfficialPluginsOnboardingTitle", "安装官方核心插件", "安裝官方核心外掛", "Install Official Core Plugins", "公式コアプラグインの導入");
		Add("OfficialPluginsOnboardingIntro", "StarPie 采用轻量化分发，常用核心动作已插件化并托管于官方仓库。首次进入设置建议一键安装以下 5 项官方核心插件，即可快速开启常用的文件夹、网页、程序启动等核心能力：", "StarPie 採用輕量化發佈，常用核心動作已外掛化並託管於官方倉庫。首次進入設定建議一鍵安裝以下 5 項官方核心外掛，即可快速開啟常用的資料夾、網頁、程式啟動等核心能力：", "StarPie uses lightweight distribution with core actions modularized as official plugins. On first opening Settings, we recommend installing these 5 official core plugins to quickly enable commonly used actions like Open Folder, Launch App, and Web URL:", "StarPie は軽量配布を採用しており、一般的なコアアクションは公式プラグインとして提供されています。初回設定時に以下の 5 つの公式コアプラグインを一括導入することで、フォルダーを開く、アプリ起動などのよく使う機能を有効化できます：");
		Add("OfficialPluginsOnboardingSecurityHeader", "安全与权限说明", "安全與權限說明", "Security & Permissions Notice", "セキュリティと権限について");
		Add("OfficialPluginsOnboardingSecuritySource", "官方来源：https://github.com/Star-Pie/StarPie-Official-Plugins", "官方來源：https://github.com/Star-Pie/StarPie-Official-Plugins", "Official Source: https://github.com/Star-Pie/StarPie-Official-Plugins", "公式配布元：https://github.com/Star-Pie/StarPie-Official-Plugins");
		Add("OfficialPluginsOnboardingSecurityPerms", "所需权限：进程启动（Process）、按键模拟（InputSimulation）", "所需權限：行程啟動（Process）、按鍵模擬（InputSimulation）", "Required Permissions: Process Execution (Process), Keystroke Simulation (InputSimulation)", "必要な権限：プロセス起動（Process）、キー入力シミュレーション（InputSimulation）");
		Add("OfficialPluginsOnboardingSecurityDesc", "插件将在进程内以 StarPie 当前权限运行，并在安装前严格校验 SHA-256 文件哈希。在此确认前，不会发起任何网络请求。", "外掛將在行程內以 StarPie 目前權限執行，並在安裝前嚴格校驗 SHA-256 檔案雜湊。在此確認前，不會發起任何網路請求。", "Plugins run in-process with StarPie's current privileges and undergo strict SHA-256 file hash integrity verification before installation. Zero network requests are made before this confirmation.", "プラグインは StarPie の現在の権限でプロセス内実行され、インストール前に厳格な SHA-256 ハッシュ値による整合性検証が行われます。この確認前には一切のネットワーク通信は行われません。");
		Add("OfficialPluginsOnboardingInstallBtn", "⬇️ 一键安装并启用", "⬇️ 一鍵安裝並啟用", "⬇️ Install & Enable All", "⬇️ 一括インストールして有効化");
		Add("OfficialPluginsOnboardingLaterBtn", "暂不安装", "暫不安裝", "Not Now", "後で");
		Add("OfficialPluginsOnboardingRetryBtn", "🔄 重试未完成项", "🔄 重試未完成項", "🔄 Retry Incomplete Items", "🔄 未完了の項目を再試行");
		Add("OfficialPluginsOnboardingDoneBtn", "完成", "完成", "Done", "完了");
		Add("OfficialPluginsOnboardingHint", "稍后也可在设置「插件与扩展」中随时安装", "稍後也可在設定「外掛與擴充」中隨時安裝", "You can also install them anytime later in Settings > Plugins", "後から設定の「プラグインと拡張」でいつでも導入できます");
		Add("OfficialPluginsOnboardingStatusReady", "点击下方按钮即可一键下载并启用缺失的官方核心插件。", "點擊下方按鈕即可一鍵下載並啟用缺失的官方核心外掛。", "Click below to download and enable missing official core plugins in one click.", "下のボタンをクリックすると、不足している公式コアプラグインを一括導入できます。");
		Add("OfficialPluginsOnboardingStatusFetchingCatalog", "正在获取官方插件目录...", "正在取得官方外掛目錄...", "Fetching official plugin catalog...", "公式プラグインカタログを取得しています...");
		Add("OfficialPluginsOnboardingStatusInstallingItem", "正在下载并安装 ({0}/{1})：{2}...", "正在下載並安裝 ({0}/{1})：{2}...", "Downloading and installing ({0}/{1}): {2}...", "ダウンロードとインストール中 ({0}/{1})：{2}...");
		Add("OfficialPluginsOnboardingStatusCancelling", "正在取消安装，请稍候...", "正在取消安裝，請稍候...", "Cancelling installation, please wait...", "インストールをキャンセルしています。お待ちください...");
		Add("OfficialPluginsOnboardingAllSucceeded", "✅ 5 项官方核心插件已全部安装并启用！", "✅ 5 項官方核心外掛已全部安裝並啟用！", "✅ All 5 official core plugins have been installed and enabled!", "✅ 5 つの公式コアプラグインがすべてインストールされ有効化されました！");
		Add("OfficialPluginsOnboardingPartialFailed", "安装完成 {0} 项，失败 {1} 项。详情：{2}", "安裝完成 {0} 項，失敗 {1} 項。詳情：{2}", "Completed {0} item(s), failed {1} item(s). Details: {2}", "{0} 件完了、{1} 件失敗。詳細：{2}");
		Add("OfficialPluginsOnboardingSummaryWithDisabled", "已成功启用 {0} 项官方核心插件，另有 {1} 项原本被停用的插件已尊重您的设置保持停用。", "已成功啟用 {0} 項官方核心外掛，另有 {1} 項原本被停用的外掛已尊重您的設定保持停用。", "Successfully enabled {0} official core plugin(s); {1} previously disabled plugin(s) were kept disabled per your preferences.", "{0} 件の公式コアプラグインを有効化しました（以前無効化されていた {1} 件は設定を尊重して無効のまま保持）。");
		Add("OfficialPluginsOnboardingEnableFailedGuidance", "插件「{0}」已完成安装但未能自动启用（可能存在加载异常或环境依赖缺失）。您可前往设置「插件」页手动开启或查看日志，也可点击下方“重试”。", "外掛「{0}」已完成安裝但未能自動啟用（可能存在載入異常或環境相依性缺失）。您可前往設定「外掛」頁手動開啟或檢視記錄，也可點擊下方「重試」。", "Plugin \"{0}\" installed successfully but failed to enable automatically. You can enable it manually or inspect logs in Settings > Plugins, or click Retry below.", "プラグイン「{0}」は正常にインストールされましたが、自動有効化に失敗しました。設定の「プラグイン」で手動有効化またはログ確認を行うか、下の「再試行」をクリックしてください。");
		Add("OfficialPluginsOnboardingEnableFailedGuidanceWithReason", "插件「{0}」已在本地安装，但启用失败：{1}。您可前往设置「插件与扩展」手动启用或查看日志，也可点击下方重试。", "外掛「{0}」已在本機安裝，但啟用失敗：{1}。您可前往設定「外掛與擴充」手動啟用或檢視記錄，也可點擊下方重試。", "Plugin \"{0}\" is installed locally but failed to enable: {1}. You can enable it manually or inspect logs in Settings > Plugins, or click Retry below.", "プラグイン「{0}」はローカルにインストール済みですが、有効化に失敗しました：{1}。設定の「プラグインと拡張」で手動有効化またはログ確認を行うか、下の「再試行」をクリックしてください。");
		Add("OfficialPluginsOnboardingInstallFailedGuidance", "插件「{0}」安装未成功：{1}。请检查网络或配置后点击重试。", "外掛「{0}」安裝未成功：{1}。請檢查網路或設定後點擊重試。", "Plugin \"{0}\" failed to install: {1}. Please check your network or configuration and click Retry.", "プラグイン「{0}」のインストールに失敗しました：{1}。ネットワークや設定を確認後、再試行をクリックしてください。");
		Add("OfficialPluginsOnboardingOriginallyDisabledNotice", "插件「{0}」原本已被您停用，已保持停用状态，未擅自启用。", "外掛「{0}」原本已被您停用，已保持停用狀態，未擅自啟用。", "Plugin \"{0}\" was previously disabled; kept disabled and not modified.", "プラグイン「{0}」は以前無効化されていたため、無効状態のまま保持されました。");
		Add("OfficialPluginsOnboardingPermissionRejectedCatalog", "用户拒绝了官方目录声明的额外权限授权（{0}）", "使用者拒絕了官方目錄聲明的額外權限授權（{0}）", "User declined extra permissions declared in the catalog: ({0})", "公式カタログで要求された追加権限の承認が拒否されました（{0}）");
		Add("OfficialPluginsOnboardingPermissionRejectedPackage", "用户拒绝了下载包 plugin.json 实际声明的额外权限授权（{0}）", "使用者拒絕了下載包 plugin.json 實際聲明的額外權限授權（{0}）", "User declined extra permissions declared in the downloaded package: ({0})", "ダウンロードされたパッケージ plugin.json で要求された追加権限の承認が拒否されました（{0}）");
		Add("OfficialPluginsOnboardingExtraCapPackagePrompt", "插件「{0}」下载包的 plugin.json 实际声明了此前未告知的权限：\n\n{1}\n\n是否允许安装该插件？", "外掛「{0}」下載包的 plugin.json 實際聲明了此前未告知的權限：\n\n{1}\n\n是否允許安裝該外掛？", "Plugin \"{0}\" actual plugin.json in package declares permissions not previously disclosed:\n\n{1}\n\nAllow installing this plugin?", "プラグイン「{0}」パッケージの plugin.json は事前に通知されていない実際の権限を要求しています：\n\n{1}\n\nこのプラグインのインストールを許可しますか？");
		Add("OfficialPluginsOnboardingExtraCapPromptTitle", "官方插件额外权限确认", "官方外掛額外權限確認", "Official Plugin Extra Permission Confirmation", "公式プラグインの追加権限確認");
		Add("OfficialPluginsOnboardingExtraCapPrompt", "插件「{0}」请求了超出预告范围的权限：\n\n{1}\n\n是否允许安装该插件？", "外掛「{0}」請求了超出預告範圍的權限：\n\n{1}\n\n是否允許安裝該外掛？", "Plugin \"{0}\" requests permissions beyond the pre-disclosed scope:\n\n{1}\n\nAllow installing this plugin?", "プラグイン「{0}」は事前通知範囲外の権限を要求しています：\n\n{1}\n\nこのプラグインのインストールを許可しますか？");
		Add("OfficialPluginsStateToInstall", "待安装", "待安裝", "To Install", "未導入");
		Add("OfficialPluginsStateInstalled", "已启用", "已啟用", "Enabled", "有効");
		Add("OfficialPluginsStateInstalledDisabled", "已停用", "已停用", "Disabled", "無効");
		Add("PluginsOnboardingBannerTitle", "官方核心插件未全部安装", "官方核心外掛未全部安裝", "Official Core Plugins Incomplete", "公式コアプラグインが未完了です");
		Add("PluginsOnboardingBannerText", "尚有 {0} 项官方核心插件未安装。安装后可使用打开文件夹、启动程序、网页跳转、系统控制等常用内置动作。", "尚有 {0} 項官方核心外掛未安裝。安裝後可使用開啟資料夾、啟動程式、網頁跳轉、系統控制等常用內建動作。", "{0} official core plugin(s) are not installed. Install them to use commonly used built-in actions like Open Folder, Launch App, Web URL, and System Control.", "公式コアプラグインが {0} 件未導入です。インストールするとフォルダーを開く、アプリ起動、Web ページ、システム制御などの機能が使用可能になります。");
		Add("PluginsOnboardingBannerButton", "⬇️ 一键安装缺失项", "⬇️ 一鍵安裝缺失項", "⬇️ Install Missing Plugins", "⬇️ 不足プラグインを一括導入");

		Translations = dictionary;
	}
}
