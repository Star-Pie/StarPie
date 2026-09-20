using System;
using System.Collections.Generic;
using System.Globalization;

namespace WinPieGestures;

public static class I18n
{
	private static LanguageCode _currentLanguage = LanguageCode.ZhCn;

	private static readonly Dictionary<string, Dictionary<LanguageCode, string>> Translations;

	public static LanguageCode CurrentLanguage
	{
		get
		{
			return _currentLanguage;
		}
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

	public static string T(string key)
	{
		return GetString(key);
	}

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
		if (!Translations.TryGetValue(key, out Dictionary<LanguageCode, string>? values))
		{
			return Array.Empty<string>();
		}

		var result = new List<string>(values.Count);
		foreach (string value in values.Values)
		{
			if (string.IsNullOrWhiteSpace(value) || result.Contains(value)) continue;
			result.Add(value);
		}
		return result;
	}

	public static string GetString(string key)
	{
		if (Translations.TryGetValue(key, out Dictionary<LanguageCode, string> value))
		{
			if (value.TryGetValue(_currentLanguage, out var value2))
			{
				return value2;
			}
			if (value.TryGetValue(LanguageCode.ZhCn, out var value3))
			{
				return value3;
			}
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

	static I18n()
	{
		Dictionary<string, Dictionary<LanguageCode, string>> dictionary = new Dictionary<string, Dictionary<LanguageCode, string>>();
		dictionary["NewCustomPresetButton"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "➕ 新建配色",
			[LanguageCode.ZhTw] = "➕ 新建配色",
			[LanguageCode.En] = "➕ New Theme",
			[LanguageCode.Ja] = "➕ 新規配色"
		};
		dictionary["NewCustomPresetTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "新建配色方案",
			[LanguageCode.ZhTw] = "新建配色方案",
			[LanguageCode.En] = "New Color Theme Preset",
			[LanguageCode.Ja] = "新しいカラーテーマ"
		};
		dictionary["NewCustomPresetPrompt"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "请输入新配色方案名称：",
			[LanguageCode.ZhTw] = "請輸入新配色方案名稱：",
			[LanguageCode.En] = "Enter a name for the new color theme:",
			[LanguageCode.Ja] = "新しいカラーテーマ名を入力してください:"
		};
		dictionary["SavePresetChangesButton"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\udcbe 保存当前配色修改",
			[LanguageCode.ZhTw] = "\ud83d\udcbe 儲存當前配色修改",
			[LanguageCode.En] = "\ud83d\udcbe Save Color Changes",
			[LanguageCode.Ja] = "\ud83d\udcbe 現在の配色変更を保存"
		};
		dictionary["SaveAsNewPresetButton"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "➕ 另存为新预设...",
			[LanguageCode.ZhTw] = "➕ 另存為新預設...",
			[LanguageCode.En] = "➕ Save as New Preset...",
			[LanguageCode.Ja] = "➕ 新規プリセットとして保存..."
		};
		dictionary["DeletePresetButton"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\uddd1\ufe0f 删除预设",
			[LanguageCode.ZhTw] = "\ud83d\uddd1\ufe0f 刪除預設",
			[LanguageCode.En] = "\ud83d\uddd1\ufe0f Delete Preset",
			[LanguageCode.Ja] = "\ud83d\uddd1\ufe0f プリセットを削除"
		};
		dictionary["RenameCustomPresetButton"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "✏\ufe0f 重命名预设",
			[LanguageCode.ZhTw] = "✏\ufe0f 重新命名預設",
			[LanguageCode.En] = "✏\ufe0f Rename Preset",
			[LanguageCode.Ja] = "✏\ufe0f プリセット名を変更"
		};
		dictionary["RenameCustomPresetTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "重命名配色方案预设",
			[LanguageCode.ZhTw] = "重新命名配色方案預設",
			[LanguageCode.En] = "Rename Color Preset",
			[LanguageCode.Ja] = "カラープリセット名を変更"
		};
		dictionary["RenameCustomPresetPrompt"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "请输入配色方案预设的新名称：",
			[LanguageCode.ZhTw] = "請輸入配色方案預設的新名稱：",
			[LanguageCode.En] = "Enter a new name for the color preset:",
			[LanguageCode.Ja] = "カラープリセットの新しい名前を入力してください:"
		};
		dictionary["CustomColorsExpanderDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "展开后可精准微调扇区底色、高亮光晕、边框线条、文字与光弧等各项色彩。",
			[LanguageCode.ZhTw] = "展開後可精準微調扇區底色、高亮光暈、邊框線條、文字與光弧等各項色彩。",
			[LanguageCode.En] = "Expand to fine-tune sector background, highlight glow, border outlines, text and arc colors.",
			[LanguageCode.Ja] = "展開してセクター背景、ハイライトグロー、ボーダー、テキスト色などを微調整できます。"
		};
		dictionary["WheelFontFamily"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "轮盘文字字体:",
			[LanguageCode.ZhTw] = "輪盤文字字體:",
			[LanguageCode.En] = "Wheel Font Family:",
			[LanguageCode.Ja] = "ホイールのフォント:"
		};
		
		dictionary["SubmenuStyleTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "二级菜单样式",
			[LanguageCode.ZhTw] = "二級選單樣式",
			[LanguageCode.En] = "Submenu Style",
			[LanguageCode.Ja] = "サブメニューのスタイル"
		};
		dictionary["SubmenuStyleDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "外圈子环：子动作沿选中扇区外侧环形展开（最多4个）；蜂窝扇：子动作以选中项为中心呈扇形排列（最多3个）。",
			[LanguageCode.ZhTw] = "外圈子環：子動作沿選中扇區外側環形展開（最多4個）；蜂窩扇：子動作以選中項為中心呈扇形排列（最多3個）。",
			[LanguageCode.En] = "Sub-Ring: Sub-actions expand in an outer concentric ring (up to 4 items); Honeycomb Fan: Expands outward from the selected sector in a tight fan (up to 3 items).",
			[LanguageCode.Ja] = "外周リング：選択したセクターの外側にリング状に展開（最大4項目）；ハニカムファン：扇状にコンパクトに展開（最大3項目）。"
		};
		dictionary["SubmenuStyleWheel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🌐 外圈子环",
			[LanguageCode.ZhTw] = "🌐 外圈子環",
			[LanguageCode.En] = "🌐 Outer Sub-Ring",
			[LanguageCode.Ja] = "🌐 外周同心リング"
		};
		dictionary["SubmenuStyleFan"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🍯 蜂窝扇",
			[LanguageCode.ZhTw] = "🍯 蜂窩扇",
			[LanguageCode.En] = "🍯 Honeycomb Fan",
			[LanguageCode.Ja] = "🍯 ハニカムファン"
		};

		dictionary["OuterEscapeTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "外甩脱离取消",
			[LanguageCode.ZhTw] = "外甩脫離取消",
			[LanguageCode.En] = "Outer Escape Cancel",
			[LanguageCode.Ja] = "外側スワイプでキャンセル"
		};
		dictionary["OuterEscapeDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "手势划出后若想放弃，无需拉回中心，直接顺势向外快速划出即可安全取消，0 误触。",
			[LanguageCode.ZhTw] = "手勢劃出後若想放棄，無需拉回中心，直接順勢向外快速劃出即可安全取消，0 誤觸。",
			[LanguageCode.En] = "Flick cursor outwards past the wheel radius to safely cancel without returning to center.",
			[LanguageCode.Ja] = "ホイールの外側へ素早くスワイプすることで、安全に操作をキャンセルできます。"
		};
		dictionary["OuterEscapeDistanceTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "外甩取消距离:",
			[LanguageCode.ZhTw] = "外甩取消距離:",
			[LanguageCode.En] = "Escape Distance:",
			[LanguageCode.Ja] = "キャンセルスワイプ距離:"
		};
		dictionary["OuterEscapeDistanceDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "设定光标划出距离中心多远时判定为放弃。数值越小越灵敏（更易甩出取消），数值越大越沉稳（需甩得更远）。",
			[LanguageCode.ZhTw] = "設定游標劃出距離中心多遠時判定為放棄。數值越小越靈敏（更易甩出取消），數值越大越沉穩（需甩得更遠）。",
			[LanguageCode.En] = "How far past the center the cursor must travel to cancel. Smaller values cancel easier, larger values require a farther flick.",
			[LanguageCode.Ja] = "中心からどれだけ離れたらキャンセルとするかを設定します。値が小さいほど敏感になり、大きいほど遠くへのスワイプが必要になります。"
		};
		dictionary["OuterEscapeCheckbox"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "启用向外顺势甩出取消手势 (推荐开启)",
			[LanguageCode.ZhTw] = "啟用向外順勢甩出取消手勢 (推薦開啟)",
			[LanguageCode.En] = "Enable Outer Escape Cancel (Recommended)",
			[LanguageCode.Ja] = "外側スワイプキャンセルを有効化 (推奨)"
		};
		dictionary["VolumeDragTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "音量拖距调音",
			[LanguageCode.ZhTw] = "音量拖距調音",
			[LanguageCode.En] = "Volume Drag-Adjust",
			[LanguageCode.Ja] = "音量ドラッグ調整"
		};
		dictionary["VolumeDragDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "长按音量加/减扇区向外拖出可连续调音，以下距离参数实时生效，无需重启。",
			[LanguageCode.ZhTw] = "長按音量加/減扇區向外拖出可連續調音，以下距離參數即時生效，無需重啟。",
			[LanguageCode.En] = "Drag outward from a volume +/- sector to adjust continuously; the distance options below apply live without restart.",
			[LanguageCode.Ja] = "音量+/-セクターから外側へドラッグして連続調整。以下の距離パラメータは再起動不要で即時反映されます。"
		};
		dictionary["VolumeCancelRatioTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "缩回取消滞后:",
			[LanguageCode.ZhTw] = "縮回取消滯後:",
			[LanguageCode.En] = "Return Cancel Ratio:",
			[LanguageCode.Ja] = "中心復帰キャンセル係数:"
		};
		dictionary["VolumeCancelRatioDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "调音时缩回到触发距离的百分之多少以下即取消并恢复原音量。值越大越不易被边缘抖动误回收（默认 60%）。",
			[LanguageCode.ZhTw] = "調音時縮回到觸發距離的百分之多少以下即取消並恢復原音量。值越大越不易被邊緣抖動誤回收（預設 60%）。",
			[LanguageCode.En] = "Cancel and restore the original volume once you drag back below this share of the trigger distance. Higher resists edge jitter (default 60%).",
			[LanguageCode.Ja] = "トリガー距離のこの割合まで戻すとキャンセルして元の音量に戻します。値が大きいほど端の揺れによる誤収斂ににくくなります（既定60%）。"
		};
		dictionary["VolumeFlickFarTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "外甩取消距离下限:",
			[LanguageCode.ZhTw] = "外甩取消距離下限:",
			[LanguageCode.En] = "Flick Cancel Distance Floor:",
			[LanguageCode.Ja] = "スワイプキャンセル距離下限:"
		};
		dictionary["VolumeFlickFarDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "仅当光标已远于该距离时，快速甩动才会取消调音，防止近距离拖动被误判为甩出。",
			[LanguageCode.ZhTw] = "僅當光標已遠於該距離時，快速甩動才會取消調音，防止近距離拖動被誤判為甩出。",
			[LanguageCode.En] = "A fast flick only cancels once the pointer is beyond this distance, avoiding short drags being read as a flick.",
			[LanguageCode.Ja] = "カーソルがこの距離を超えて初めて高速スワイプでキャンセル。近距離ドラッグの誤判定を防ぎます。"
		};
		dictionary["VolumeFlickJumpTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "外甩速度跳变阈值:",
			[LanguageCode.ZhTw] = "外甩速度跳變閾值:",
			[LanguageCode.En] = "Flick Jump Threshold:",
			[LanguageCode.Ja] = "スワイプ跳変閾値:"
		};
		dictionary["VolumeFlickJumpDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "一帧之内光标移动超过该距离才算快速甩动。值越大越不容易触发甩出取消。",
			[LanguageCode.ZhTw] = "一幀之內光標移動超過該距離才算快速甩動。值越大越不容易觸發甩出取消。",
			[LanguageCode.En] = "Movement beyond this distance within one frame counts as a fast flick. Larger values make flick-cancel less likely.",
			[LanguageCode.Ja] = "1フレーム内の移動がこの距離を超えると高速スワイプと判定。値が大きいほどスワイプキャンセルは起きにくくなります。"
		};
		dictionary["IconPickerImport"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "➕ 导入自定义图标...",
			[LanguageCode.ZhTw] = "➕ 匯入自訂圖示...",
			[LanguageCode.En] = "➕ Import Custom Icon...",
			[LanguageCode.Ja] = "➕ カスタムアイコンをインポート..."
		};
		dictionary["CustomColorsExpanderTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83c\udfa8 自定义高级配色与色彩微调",
			[LanguageCode.ZhTw] = "\ud83c\udfa8 自訂進階配色與色彩微調",
			[LanguageCode.En] = "\ud83c\udfa8 Custom Advanced Color Tuning",
			[LanguageCode.Ja] = "\ud83c\udfa8 高度なカラーカスタマイズ"
		};
		dictionary["AnimSpeedTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "功能区高亮与过渡动效速度",
			[LanguageCode.ZhTw] = "功能區高亮與過渡動效速度",
			[LanguageCode.En] = "Hover & Transition Animation Speed",
			[LanguageCode.Ja] = "ホバー・遷移アニメーション速度"
		};
		dictionary["AnimSpeedDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "调节鼠标划向不同功能扇区时的高亮弹出与平滑过渡动画响应速度，定制专属跟手体验。",
			[LanguageCode.ZhTw] = "調節滑鼠滑向不同功能扇區時的高亮彈出與平滑過渡動畫響應速度，定制專屬手感。",
			[LanguageCode.En] = "Adjust the response animation speed when hovering and transitioning across sectors.",
			[LanguageCode.Ja] = "セクター間をホバー・移動する際のアニメーション速度を調整します。"
		};
		dictionary["AnimSpeedElegant"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83c\udf38 优雅 (130ms / 柔和细腻)",
			[LanguageCode.ZhTw] = "\ud83c\udf38 優雅 (130ms / 柔和細膩)",
			[LanguageCode.En] = "\ud83c\udf38 Elegant (130ms / Smooth & Soft)",
			[LanguageCode.Ja] = "\ud83c\udf38 エレガント (130ms / 滑らか)"
		};
		dictionary["AnimSpeedBalanced"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚡ 流畅 (80ms / 推荐默认)",
			[LanguageCode.ZhTw] = "⚡ 流暢 (80ms / 推薦預設)",
			[LanguageCode.En] = "⚡ Fluent (80ms / Recommended)",
			[LanguageCode.Ja] = "⚡ スムーズ (80ms / 推奨)"
		};
		dictionary["AnimSpeedFast"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\ude80 快速 (35ms / 极速响应)",
			[LanguageCode.ZhTw] = "\ud83d\ude80 快速 (35ms / 極速響應)",
			[LanguageCode.En] = "\ud83d\ude80 Snappy (35ms / Ultra-Fast)",
			[LanguageCode.Ja] = "\ud83d\ude80 高速 (35ms / 即座に応答)"
		};
		dictionary["CoreTransformSectionTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "图案尺寸与位置",
			[LanguageCode.ZhTw] = "圖案尺寸與位置",
			[LanguageCode.En] = "Core Pattern & Image Size and Position Tuning",
			[LanguageCode.Ja] = "コアパターン・画像のサイズと位置の微調整"
		};
		dictionary["CoreIconScaleTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "图案大小缩放:",
			[LanguageCode.ZhTw] = "圖案大小縮放:",
			[LanguageCode.En] = "Core Pattern / Image Scale:",
			[LanguageCode.Ja] = "中央パターン／画像のスケーリング:"
		};
		dictionary["CoreImageOffsetXTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "水平偏移:",
			[LanguageCode.ZhTw] = "水平偏移:",
			[LanguageCode.En] = "Horizontal Offset (X):",
			[LanguageCode.Ja] = "水平表示位置オフセット (X):"
		};
		dictionary["CoreImageOffsetYTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "垂直偏移:",
			[LanguageCode.ZhTw] = "垂直偏移:",
			[LanguageCode.En] = "Vertical Offset (Y):",
			[LanguageCode.Ja] = "垂直表示位置オフセット (Y):"
		};
		dictionary["BtnResetCoreTransform"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔄 重置尺寸与居中",
			[LanguageCode.ZhTw] = "🔄 重設尺寸與置中",
			[LanguageCode.En] = "🔄 Reset Size & Center Position",
			[LanguageCode.Ja] = "🔄 サイズと中央位置をリセット"
		};
		dictionary["CoreImagePerformanceTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "提示：推荐使用 256×256 ~ 512×512 适中分辨率的图片或 SVG 矢量图。导入超高分辨率（如 4K/8K 原图）会增加 GPU 内存占用与重采样计算开销，可能影响手势呼出与高刷响应性能。",
			[LanguageCode.ZhTw] = "提示：建議使用 256×256 ~ 512×512 適中解析度的圖片或 SVG 向量圖。匯入超高解析度（如 4K/8K 原圖）會增加 GPU 記憶體佔用與重採樣計算開銷，可能影響手勢呼出與高刷響應效能。",
			[LanguageCode.En] = "Tip: Recommended image size is 256×256 ~ 512×512 px or SVG vectors. Importing ultra-high resolution images (e.g. 4K/8K) increases GPU memory and texture sampling overhead, which may impact gesture responsiveness.",
			[LanguageCode.Ja] = "ヒント: 256×256～512×512 px の画像または SVG ベクター画像の使用を推奨します。超高解像度画像（4K/8K など）を使用すると、GPU メモリ使用量と再サンプリング負荷が増加し、応答性に影響を与える場合があります。"
		};
		dictionary["EnableMultiTier"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "启用多级轮盘与级联子菜单 (Multi-Tier Sub-Wheels)",
			[LanguageCode.ZhTw] = "啟用多級輪盤與級聯子選單 (Multi-Tier Sub-Wheels)",
			[LanguageCode.En] = "Enable Multi-Tier Cascading Sub-Wheels",
			[LanguageCode.Ja] = "マルチ階層サブホイール機能を有効化"
		};
		dictionary["EnableMultiTierDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "开启后，若扇区配置了二级子动作，光标悬停时外圈将平滑展开扇形级联子菜单，向外划动即可精准触发子功能。",
			[LanguageCode.ZhTw] = "開啟後，若扇區配置了二級子動作，游標懸停時外圈將平滑展開扇形級聯子選單，向外劃動即可精準觸發子功能。",
			[LanguageCode.En] = "When enabled, hovering over a sector with sub-actions will smoothly expand cascading outer sub-sectors. Flick outward to trigger.",
			[LanguageCode.Ja] = "有効にすると、サブアクションが設定されたセクターにホバーした際に外側にカスケードサブメニューが展開され、外側へスワイプしてトリガーできます。"
		};
		dictionary["AutoExpandSubRingsTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "唤出时直接同时展开一二级轮盘 (Auto-Expand Sub-Rings)",
			[LanguageCode.ZhTw] = "喚出時直接同時展開一二級輪盤 (Auto-Expand Sub-Rings)",
			[LanguageCode.En] = "Expand Sub-Rings Simultaneously on Popup",
			[LanguageCode.Ja] = "ポップアップ時にサブリングを同時展開"
		};
		dictionary["AutoExpandSubRingsDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "开启后，呼出轮盘时所有已配置二级动作的外圈子环将与一级扇区同时呈现，无需向外划出触发距离即可一览全部操作。",
			[LanguageCode.ZhTw] = "開啟後，呼出輪盤時所有已配置二級動作的外圈子環將與一級扇區同時呈現，無需向外劃出觸發距離即可一覽全部操作。",
			[LanguageCode.En] = "When enabled, outer sub-rings for all configured sectors expand simultaneously upon popup without needing to drag past trigger distance.",
			[LanguageCode.Ja] = "有効にすると、ポップアップ時にトリガー距離をスワイプしなくても、設定済みのすべてのサブリングがメインセクターと同時に展開されます。"
		};
		dictionary["IsolationModeTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "进程隔离与生效模式",
			[LanguageCode.ZhTw] = "處理程序隔離與生效模式",
			[LanguageCode.En] = "Process Isolation & Activation Mode",
			[LanguageCode.Ja] = "プロセス分離と有効化モード"
		};
		dictionary["IsolationBlacklistRadio"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\udeab 排除黑名单模式 (默认：全局生效，仅在黑名单程序中放行右键)",
			[LanguageCode.ZhTw] = "\ud83d\udeab 排除黑名單模式 (預設：全域生效，僅在黑名單程式中放行右鍵)",
			[LanguageCode.En] = "\ud83d\udeab Blacklist Mode (Global active, bypass in blacklisted apps)",
			[LanguageCode.Ja] = "\ud83d\udeab ブラックリストモード (既定: 全体有効、除外アプリのみ右クリック通過)"
		};
		dictionary["IsolationWhitelistRadio"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\udee1\ufe0f 启用白名单模式 (仅在白名单程序中生效，其余程序完全放行右键)",
			[LanguageCode.ZhTw] = "\ud83d\udee1\ufe0f 啟用白名單模式 (僅在白名單程式中生效，其餘程式完全放行右鍵)",
			[LanguageCode.En] = "\ud83d\udee1\ufe0f Whitelist Mode (Only active in whitelisted apps, bypass elsewhere)",
			[LanguageCode.Ja] = "\ud83d\udee1\ufe0f ホワイトリストモード (登録アプリのみ有効、他は右クリック通過)"
		};
		dictionary["WhitelistTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "进程启用白名单",
			[LanguageCode.ZhTw] = "處理程序啟用白名單",
			[LanguageCode.En] = "Process Activation Whitelist",
			[LanguageCode.Ja] = "プロセス有効化ホワイトリスト"
		};
		dictionary["WhitelistDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "手势轮盘仅在白名单列表中的应用程序中生效，其他所有程序完全放行鼠标右键。",
			[LanguageCode.ZhTw] = "手勢輪盤僅在白名單列表中的應用程式中生效，其他所有程式完全放行滑鼠右鍵。",
			[LanguageCode.En] = "Mouse gestures will ONLY activate in whitelisted applications, bypassing everywhere else.",
			[LanguageCode.Ja] = "ホワイトリストに登録されたアプリのみでジェスチャーが有効になり、他のアプリでは通過します。"
		};
		dictionary["SubActionColumnHeader"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "级联子菜单",
			[LanguageCode.ZhTw] = "級聯子選單",
			[LanguageCode.En] = "Sub-Menu",
			[LanguageCode.Ja] = "サブメニュー"
		};
		dictionary["CustomColorsExpander"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "展开后可精准微调扇区底色、高亮光晕、边框线条、文字与光弧等各项色彩。",
			[LanguageCode.ZhTw] = "展開後可精準微調扇區底色、高亮光暈、邊框線條、文字與光弧等各項色彩。",
			[LanguageCode.En] = "Expand to fine-tune individual colors for sectors, highlights, borders, text, and glow.",
			[LanguageCode.Ja] = "セクター、ハイライト、ボーダー、テキストなどの色を個別に調整します。"
		};
		dictionary["MilestonesOlderExpander"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "📜 展开查看更早的历史版本演进",
			[LanguageCode.ZhTw] = "📜 展開查看更早的歷史版本演進",
			[LanguageCode.En] = "📜 View Older Milestones",
			[LanguageCode.Ja] = "📜 過去の更新履歴を表示"
		};
		dictionary["BrowseAppTooltip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "选择应用程序或快捷方式...",
			[LanguageCode.ZhTw] = "選擇應用程式或捷徑...",
			[LanguageCode.En] = "Browse application or shortcut...",
			[LanguageCode.Ja] = "アプリまたはショートカットを参照..."
		};
		dictionary["BrowseFolderTooltip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "选择本地文件夹...",
			[LanguageCode.ZhTw] = "選擇本機資料夾...",
			[LanguageCode.En] = "Browse local folder...",
			[LanguageCode.Ja] = "フォルダーを参照..."
		};
		dictionary["BtnConfirm"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "确定",
			[LanguageCode.ZhTw] = "確定",
			[LanguageCode.En] = "Confirm",
			[LanguageCode.Ja] = "確定"
		};
		dictionary["BtnCancel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "取消",
			[LanguageCode.ZhTw] = "取消",
			[LanguageCode.En] = "Cancel",
			[LanguageCode.Ja] = "キャンセル"
		};
		dictionary["BtnOk"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "确定",
			[LanguageCode.ZhTw] = "確定",
			[LanguageCode.En] = "OK",
			[LanguageCode.Ja] = "OK"
		};
		dictionary["BtnApply"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "应用",
			[LanguageCode.ZhTw] = "套用",
			[LanguageCode.En] = "Apply",
			[LanguageCode.Ja] = "適用"
		};
		dictionary["BtnTest"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "测试",
			[LanguageCode.ZhTw] = "測試",
			[LanguageCode.En] = "Test",
			[LanguageCode.Ja] = "テスト"
		};
		dictionary["BtnBrowseFolder"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "选择文件夹...",
			[LanguageCode.ZhTw] = "選擇資料夾...",
			[LanguageCode.En] = "Browse Folder...",
			[LanguageCode.Ja] = "フォルダーを選択..."
		};
		dictionary["ActionTypeFolder"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\udcc2 打开文件夹",
			[LanguageCode.ZhTw] = "\ud83d\udcc2 開啟資料夾",
			[LanguageCode.En] = "\ud83d\udcc2 Open Folder",
			[LanguageCode.Ja] = "\ud83d\udcc2 フォルダーを開く"
		};
		dictionary["ActionTypeHotkeyShort"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "快捷热键",
			[LanguageCode.ZhTw] = "快捷熱鍵",
			[LanguageCode.En] = "Hotkey",
			[LanguageCode.Ja] = "ショートカット"
		};
		dictionary["ActionTypeOcrShort"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "截屏识字 (OCR)",
			[LanguageCode.ZhTw] = "截圖識字 (OCR)",
			[LanguageCode.En] = "Screen OCR",
			[LanguageCode.Ja] = "画面OCR"
		};
		dictionary["ActionTypeLaunchShort"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "启动程序",
			[LanguageCode.ZhTw] = "啟動程式",
			[LanguageCode.En] = "Run App",
			[LanguageCode.Ja] = "アプリ起動"
		};
		dictionary["ActionTypeFolderShort"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "打开文件夹",
			[LanguageCode.ZhTw] = "開啟資料夾",
			[LanguageCode.En] = "Open Folder",
			[LanguageCode.Ja] = "フォルダー"
		};
		dictionary["ActionTypeWebUrlShort"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "打开网址",
			[LanguageCode.ZhTw] = "開啟網址",
			[LanguageCode.En] = "Open URL",
			[LanguageCode.Ja] = "URLを開く"
		};
		dictionary["ActionTypeSystemShort"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "系统控制",
			[LanguageCode.ZhTw] = "系統控制",
			[LanguageCode.En] = "System",
			[LanguageCode.Ja] = "システム"
		};
		dictionary["ActionTypeShellToolShort"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "系统与右键工具",
			[LanguageCode.ZhTw] = "系統與右鍵工具",
			[LanguageCode.En] = "Shell & System Tools",
			[LanguageCode.Ja] = "シェル・右クリックツール"
		};
		dictionary["ActionTypePluginShort"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "插件动作",
			[LanguageCode.ZhTw] = "外掛動作",
			[LanguageCode.En] = "Plugin Action",
			[LanguageCode.Ja] = "プラグイン動作"
		};
		dictionary["PluginPageHeader"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "插件与扩展",
			[LanguageCode.ZhTw] = "外掛與擴充",
			[LanguageCode.En] = "Plugins & Extensions",
			[LanguageCode.Ja] = "プラグインと拡張"
		};
		dictionary["PluginPageSubheader"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "手动选择 .dll 安装社区插件。插件以 StarPie 当前权限在进程内运行，请只安装你信任的来源。",
			[LanguageCode.ZhTw] = "手動選擇 .dll 安裝社群外掛。外掛以 StarPie 目前權限在行程內執行，請僅安裝你信任的來源。",
			[LanguageCode.En] = "Install community plugins by picking a .dll manually. Plugins run in-process with StarPie's current privileges - only install sources you trust.",
			[LanguageCode.Ja] = "コミュニティプラグインは .dll を手動で選択してインストールします。プラグインは StarPie の権限でプロセス内実行されるため、信頼できる提供元のみ導入してください。"
		};
		dictionary["ActionTypeCommandShort"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "运行命令",
			[LanguageCode.ZhTw] = "執行命令",
			[LanguageCode.En] = "Run Command",
			[LanguageCode.Ja] = "コマンド実行"
		};
		dictionary["ActionTypeSwitchWindowShort"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "切换窗口",
			[LanguageCode.ZhTw] = "切換視窗",
			[LanguageCode.En] = "Switch Window",
			[LanguageCode.Ja] = "ウィンドウ切替"
		};
		dictionary["TerminalCmd"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "CMD",
			[LanguageCode.ZhTw] = "CMD",
			[LanguageCode.En] = "CMD",
			[LanguageCode.Ja] = "CMD"
		};
		dictionary["TerminalPowerShell"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "Powershell",
			[LanguageCode.ZhTw] = "Powershell",
			[LanguageCode.En] = "Powershell",
			[LanguageCode.Ja] = "Powershell"
		};
		dictionary["TerminalWsl"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "WSL",
			[LanguageCode.ZhTw] = "WSL",
			[LanguageCode.En] = "WSL",
			[LanguageCode.Ja] = "WSL"
		};
		dictionary["TerminalCmdHidden"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "CMD (无终端)",
			[LanguageCode.ZhTw] = "CMD (無終端)",
			[LanguageCode.En] = "CMD (no window)",
			[LanguageCode.Ja] = "CMD (非表示)"
		};
		dictionary["TerminalPowerShellHidden"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "Powershell (无终端)",
			[LanguageCode.ZhTw] = "Powershell (無終端)",
			[LanguageCode.En] = "Powershell (no window)",
			[LanguageCode.Ja] = "Powershell (非表示)"
		};
		dictionary["TerminalWslHidden"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "WSL (无终端)",
			[LanguageCode.ZhTw] = "WSL (無終端)",
			[LanguageCode.En] = "WSL (no window)",
			[LanguageCode.Ja] = "WSL (非表示)"
		};
		dictionary["ProfileCardTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "当前配置方案",
			[LanguageCode.ZhTw] = "當前配置方案",
			[LanguageCode.En] = "Active Profiles",
			[LanguageCode.Ja] = "プロファイル設定"
		};
		dictionary["ProfileCardDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "选择或新建针对特定程序（如 Chrome、VS Code）或特定工作流的轮盘配置方案（支持双击重命名）。",
			[LanguageCode.ZhTw] = "選擇或新建針對特定程式（如 Chrome、VS Code）或特定工作流程的輪盤配置方案（支援按兩下重新命名）。",
			[LanguageCode.En] = "Select or create dedicated pie wheel profiles for specific apps (e.g. Chrome, VS Code) or workflows (double-click to rename).",
			[LanguageCode.Ja] = "アプリ（Chrome、VS Codeなど）やワークフローごとに専用のプロファイルを設定します（ダブルクリックで名前変更）。"
		};
		dictionary["BtnAddAppProfile"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "➕ 新增程序专属配置",
			[LanguageCode.ZhTw] = "➕ 新增程式專屬配置",
			[LanguageCode.En] = "➕ Add App Profile",
			[LanguageCode.Ja] = "➕ アプリ専用設定を追加"
		};
		dictionary["BtnAddCustomProfile"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "➕ 新建自定义配置",
			[LanguageCode.ZhTw] = "➕ 新建自訂配置",
			[LanguageCode.En] = "➕ Add Custom Profile",
			[LanguageCode.Ja] = "➕ カスタム設定を追加"
		};
		// 拆键说明（2026-09-17）：下面两键供列表模式的整宽按钮 —— 同一面板统一用长标签
		// （见上方的「➕ 新建自定义配置」与下方的「📑 复制方案」）。画布模式的紧凑工具条按钮
		// 另用短标签的 BtnRenameProfile / BtnDeleteProfile。两组原先各共用一个键，而短文案的
		// 定义写在后面、静默覆盖了长文案，于是列表模式按钮少显示「当前配置」。此处把被顶掉的
		// 长文案独立成键，恢复它原本的设计。
		dictionary["BtnRenameCurrentProfile"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "✏\ufe0f 重命名当前配置",
			[LanguageCode.ZhTw] = "✏\ufe0f 重新命名當前配置",
			[LanguageCode.En] = "✏\ufe0f Rename Profile",
			[LanguageCode.Ja] = "✏\ufe0f 名前を変更"
		};
		dictionary["BtnDeleteCurrentProfile"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\uddd1\ufe0f 删除当前配置",
			[LanguageCode.ZhTw] = "\ud83d\uddd1\ufe0f 刪除當前配置",
			[LanguageCode.En] = "\ud83d\uddd1\ufe0f Delete Profile",
			[LanguageCode.Ja] = "\ud83d\uddd1\ufe0f 設定を削除"
		};
		dictionary["BtnDuplicateProfile"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "📑 复制方案",
			[LanguageCode.ZhTw] = "📑 複製方案",
			[LanguageCode.En] = "📑 Duplicate Profile",
			[LanguageCode.Ja] = "📑 設定を複製"
		};
		dictionary["SectorCountOptionTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "扇区按键数",
			[LanguageCode.ZhTw] = "扇區按鍵數",
			[LanguageCode.En] = "Sector Count",
			[LanguageCode.Ja] = "セクター数（キー数）"
		};
		dictionary["SectorCountOptionDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "切换手势轮盘的切分数量。4 键最快最不易误触，8 键为标准全能方位，12 键适合功能密集场景。",
			[LanguageCode.ZhTw] = "切換手勢輪盤的切分數量。4 鍵最快最不易誤觸，8 鍵為標準全能方位，12 鍵適合功能密集場景。",
			[LanguageCode.En] = "Switch sector counts: 4-way for fast blind flicks, 8-way for balanced productivity, 12-way for high-density actions.",
			[LanguageCode.Ja] = "セクター数を切り替えます。4キー（誤操作防止）、8キー（標準全方位）、12キー（高密度機能）。"
		};
		dictionary["SectorActionListTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "扇区动作映射列表",
			[LanguageCode.ZhTw] = "扇區動作對應列表",
			[LanguageCode.En] = "Sector Action Mappings",
			[LanguageCode.Ja] = "セクターアクションマッピング"
		};
		dictionary["SectorActionListDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "为每个方位指定触发动作与图标。支持热键组合（如 Ctrl+C）、启动本地程序、打开文件夹与系统级操作。",
			[LanguageCode.ZhTw] = "為每個方位指定觸發動作與圖示。支援快捷熱鍵組合（如 Ctrl+C）、啟動本地程式、開啟資料夾與系統級操作。",
			[LanguageCode.En] = "Assign actions and icons for each sector. Supports hotkeys (e.g. Ctrl+C), app launching, folder opening, and system actions.",
			[LanguageCode.Ja] = "各方向の動作とアイコンを設定します。ショートカット（Ctrl+C等）、アプリ起動、フォルダー、システム制御に対応。"
		};
		dictionary["SectorActionListReorderHint"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "点击右侧功能卡的 ▲ / ▼ 箭头，将功能移动到相邻的轮盘位置槽。",
			[LanguageCode.ZhTw] = "點擊右側功能卡的 ▲ / ▼ 箭頭，將功能移動到相鄰的輪盤位置槽。",
			[LanguageCode.En] = "Click the ▲ / ▼ arrows on the right to move an action to an adjacent wheel-position slot.",
			[LanguageCode.Ja] = "右側の▲ / ▼ボタンをクリックして、アクションを隣のホイール位置へ移動します。"
		};
		dictionary["SectorPositionSlot"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "轮盘位置槽",
			[LanguageCode.ZhTw] = "輪盤位置槽",
			[LanguageCode.En] = "Wheel position",
			[LanguageCode.Ja] = "ホイール位置"
		};
		dictionary["SectorMoveUp"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "将此功能上移一个轮盘位置",
			[LanguageCode.ZhTw] = "將此功能上移一個輪盤位置",
			[LanguageCode.En] = "Move this action up one wheel position",
			[LanguageCode.Ja] = "このアクションを1つ上のホイール位置へ移動"
		};
		dictionary["SectorMoveDown"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "将此功能下移一个轮盘位置",
			[LanguageCode.ZhTw] = "將此功能下移一個輪盤位置",
			[LanguageCode.En] = "Move this action down one wheel position",
			[LanguageCode.Ja] = "このアクションを1つ下のホイール位置へ移動"
		};
		dictionary["IconPickerTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "选择动作矢量图标",
			[LanguageCode.ZhTw] = "選擇動作向量圖示",
			[LanguageCode.En] = "Select Vector Icon",
			[LanguageCode.Ja] = "ベクターアイコンを選択"
		};
		dictionary["IconPickerHeader"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "选择扇区动作矢量图标",
			[LanguageCode.ZhTw] = "選擇扇區動作向量圖示",
			[LanguageCode.En] = "Select Sector Vector Icon",
			[LanguageCode.Ja] = "セクターアイコンを選択"
		};
		dictionary["IconPickerSubtitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "精选 30+ 常用高保真矢量图形，支持在不同分辨率及 DPI 下无损清晰渲染。",
			[LanguageCode.ZhTw] = "精選 30+ 常用高保真向量圖形，支援在不同解析度及 DPI 下無損清晰渲染。",
			[LanguageCode.En] = "30+ high-fidelity vector icons with lossless crisp rendering across all DPI displays.",
			[LanguageCode.Ja] = "30種類以上の高精細ベクターアイコン。あらゆるDPIで美しく描画されます。"
		};
		dictionary["IconPickerSearchTooltip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "输入图标名称或分类进行快速过滤...",
			[LanguageCode.ZhTw] = "輸入圖示名稱或分類進行快速篩選...",
			[LanguageCode.En] = "Search icon name or category...",
			[LanguageCode.Ja] = "アイコン名またはカテゴリで検索..."
		};
		dictionary["IconPickerClear"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "清空图标 (无图标)",
			[LanguageCode.ZhTw] = "清空圖示 (無圖示)",
			[LanguageCode.En] = "Clear Icon (No Icon)",
			[LanguageCode.Ja] = "アイコンをクリア (なし)"
		};
		dictionary["IconPickerSelected"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "已选图标:",
			[LanguageCode.ZhTw] = "已選圖示:",
			[LanguageCode.En] = "Selected Icon:",
			[LanguageCode.Ja] = "選択中のアイコン:"
		};
		dictionary["IconPickerNone"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "(未选择)",
			[LanguageCode.ZhTw] = "(未選擇)",
			[LanguageCode.En] = "(None)",
			[LanguageCode.Ja] = "(未選択)"
		};
		dictionary["ColorPickerTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "颜色选择器",
			[LanguageCode.ZhTw] = "色彩選擇器",
			[LanguageCode.En] = "Color Picker & Eyedropper",
			[LanguageCode.Ja] = "カラーピッカー＆スポイト"
		};
		dictionary["ColorPickerHue"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "色相",
			[LanguageCode.ZhTw] = "色相",
			[LanguageCode.En] = "Hue",
			[LanguageCode.Ja] = "色相"
		};
		dictionary["ColorPickerAlpha"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "不透明",
			[LanguageCode.ZhTw] = "不透明",
			[LanguageCode.En] = "Opacity",
			[LanguageCode.Ja] = "不透明度"
		};
		dictionary["ColorPickerEyedropperTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\udd0d 屏幕取色吸管",
			[LanguageCode.ZhTw] = "\ud83d\udd0d 螢幕取色吸管",
			[LanguageCode.En] = "\ud83d\udd0d Screen Eyedropper",
			[LanguageCode.Ja] = "\ud83d\udd0d 画面スポイト"
		};
		dictionary["ColorPickerEyedropperDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "点击后在屏幕任意窗口吸取精准色彩",
			[LanguageCode.ZhTw] = "點擊後在螢幕任意視窗吸取精準色彩",
			[LanguageCode.En] = "Pick color accurately from any window or desktop on screen",
			[LanguageCode.Ja] = "画面上の任意のウィンドウから正確な色を抽出します"
		};
		dictionary["ColorPickerEyedropperBtn"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "从屏幕吸色",
			[LanguageCode.ZhTw] = "從螢幕吸色",
			[LanguageCode.En] = "Pick Color",
			[LanguageCode.Ja] = "画面から吸色"
		};
		dictionary["ColorPickerSwatches"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "预设经典配色卡 (Quick Swatches - 滚轮滚动查看全部):",
			[LanguageCode.ZhTw] = "預設經典配色卡 (Quick Swatches - 滾輪滾動查看全部):",
			[LanguageCode.En] = "Preset Color Swatches (Scroll to browse):",
			[LanguageCode.Ja] = "プリセットカラーパレット (スクロールで全表示):"
		};
		dictionary["ColorPickerApply"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "应用色彩",
			[LanguageCode.ZhTw] = "套用色彩",
			[LanguageCode.En] = "Apply Color",
			[LanguageCode.Ja] = "色を適用"
		};
		dictionary["InputDialogTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "配置方案 - StarPie",
			[LanguageCode.ZhTw] = "配置方案 - StarPie",
			[LanguageCode.En] = "Profile - StarPie",
			[LanguageCode.Ja] = "プロファイル - StarPie"
		};
		dictionary["InputDialogEmpty"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "名称不能为空，请输入有效的配置名称。",
			[LanguageCode.ZhTw] = "名稱不能為空，請輸入有效的配置名稱。",
			[LanguageCode.En] = "Name cannot be empty. Please enter a valid profile name.",
			[LanguageCode.Ja] = "名前を入力してください。"
		};
		dictionary["Notice"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "提示",
			[LanguageCode.ZhTw] = "提示",
			[LanguageCode.En] = "Notice",
			[LanguageCode.Ja] = "お知らせ"
		};
		dictionary["Error"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "错误",
			[LanguageCode.ZhTw] = "錯誤",
			[LanguageCode.En] = "Error",
			[LanguageCode.Ja] = "エラー"
		};
		dictionary["AppName"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "StarPie",
			[LanguageCode.ZhTw] = "StarPie",
			[LanguageCode.En] = "StarPie",
			[LanguageCode.Ja] = "StarPie"
		};
		dictionary["AppSubtitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "现代鼠标轮盘笔势系统",
			[LanguageCode.ZhTw] = "現代滑鼠輪盤手勢系統",
			[LanguageCode.En] = "Modern Mouse Radial Gestures",
			[LanguageCode.Ja] = "次世代マウスラジアルジェスチャー"
		};
		dictionary["WindowTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "StarPie 设置控制台",
			[LanguageCode.ZhTw] = "StarPie 設定控制台",
			[LanguageCode.En] = "StarPie Preferences Console",
			[LanguageCode.Ja] = "StarPie 環境設定コンソール"
		};
		dictionary["TabTrigger"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "触发与场景",
			[LanguageCode.ZhTw] = "觸發與情境",
			[LanguageCode.En] = "Triggers & Scenes",
			[LanguageCode.Ja] = "トリガーとシーン"
		};
		dictionary["TabAppearance"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "外观与形态",
			[LanguageCode.ZhTw] = "外觀與形態",
			[LanguageCode.En] = "Appearance & Shape",
			[LanguageCode.Ja] = "外観と形状"
		};
		dictionary["TabGestures"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "手势与动作",
			[LanguageCode.ZhTw] = "手勢與動作",
			[LanguageCode.En] = "Gestures & Actions",
			[LanguageCode.Ja] = "ジェスチャーとアクション"
		};
		dictionary["TabAdvanced"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "高级与系统",
			[LanguageCode.ZhTw] = "進階與系統",
			[LanguageCode.En] = "Advanced & System",
			[LanguageCode.Ja] = "詳細設定とシステム"
		};
		dictionary["TabAbout"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "关于与更新",
			[LanguageCode.ZhTw] = "關於與更新",
			[LanguageCode.En] = "About & Updates",
			[LanguageCode.Ja] = "バージョンと更新"
		};
		dictionary["TabPlugins"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "插件与扩展",
			[LanguageCode.ZhTw] = "外掛與擴充",
			[LanguageCode.En] = "Plugins",
			[LanguageCode.Ja] = "プラグイン"
		};
		// 侧边栏两级分组的标题。术语刻意保持中性：不出现「核心 / 官方」这类会随版本变化的措辞。
		dictionary["NavGroupPreferences"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "偏好设置",
			[LanguageCode.ZhTw] = "偏好設定",
			[LanguageCode.En] = "Preferences",
			[LanguageCode.Ja] = "基本設定"
		};
		dictionary["NavGroupExtensions"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "扩展生态",
			[LanguageCode.ZhTw] = "擴充生態",
			[LanguageCode.En] = "Ecosystem",
			[LanguageCode.Ja] = "エコシステム"
		};
		// 分组徽章：标识性短标签（协议名 / 分层名），四语言共用同一串，不随界面语言翻译。
		// 仍然走词条表 —— 文案一律从这里取是项目约定，且以后改措辞只需改一处。
		dictionary["NavBadgeCore"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "Core",
			[LanguageCode.ZhTw] = "Core",
			[LanguageCode.En] = "Core",
			[LanguageCode.Ja] = "Core"
		};
		dictionary["PluginsPageSubheader"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "官方插件从 StarPie-Official-Plugins 下载并校验；社区插件仍可手动选择 .dll 安装。插件以 StarPie 当前权限在进程内运行，请只安装你信任的来源。",
			[LanguageCode.ZhTw] = "官方外掛從 StarPie-Official-Plugins 下載並校驗；社群外掛仍可手動選擇 .dll 安裝。外掛以 StarPie 目前權限在行程內執行，請只安裝你信任的來源。",
			[LanguageCode.En] = "Official plugins are downloaded and verified from StarPie-Official-Plugins; community plugins can still be installed manually. Plugins run in-process with StarPie's own privileges, so only install sources you trust.",
			[LanguageCode.Ja] = "公式プラグインは StarPie-Official-Plugins からダウンロードして検証します。コミュニティプラグインは引き続き .dll を手動で選択できます。プラグインは StarPie と同じ権限で実行されるため、信頼できる提供元のみインストールしてください。"
		};
		dictionary["PluginsInstallButton"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "➕ 手动安装社区插件 (.dll)...",
			[LanguageCode.ZhTw] = "➕ 手動安裝社群外掛 (.dll)...",
			[LanguageCode.En] = "➕ Install Community Plugin (.dll)...",
			[LanguageCode.Ja] = "➕ コミュニティプラグインを手動インストール (.dll)..."
		};
		dictionary["PluginsRescanButton"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔄 重新扫描",
			[LanguageCode.ZhTw] = "🔄 重新掃描",
			[LanguageCode.En] = "🔄 Rescan",
			[LanguageCode.Ja] = "🔄 再スキャン"
		};
		dictionary["PluginsOpenDataFolderButton"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "📂 打开数据目录",
			[LanguageCode.ZhTw] = "📂 開啟資料目錄",
			[LanguageCode.En] = "📂 Open Data Folder",
			[LanguageCode.Ja] = "📂 データフォルダーを開く"
		};
		dictionary["PluginsOpenScanFolderButton"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "📂 打开扫描目录",
			[LanguageCode.ZhTw] = "📂 開啟掃描目錄",
			[LanguageCode.En] = "📂 Open Scan Folder",
			[LanguageCode.Ja] = "📂 スキャンフォルダーを開く"
		};
		dictionary["PluginsEnableCheckBox"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "启用插件系统",
			[LanguageCode.ZhTw] = "啟用外掛系統",
			[LanguageCode.En] = "Enable plugin system",
			[LanguageCode.Ja] = "プラグイン機能を有効にする"
		};
		dictionary["PluginsDataDirectoryHint"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "数据目录：{0}",
			[LanguageCode.ZhTw] = "資料目錄：{0}",
			[LanguageCode.En] = "Data folder: {0}",
			[LanguageCode.Ja] = "データフォルダー：{0}"
		};
		dictionary["PluginsStatusEmpty"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "尚未安装任何插件。{0}",
			[LanguageCode.ZhTw] = "尚未安裝任何外掛。{0}",
			[LanguageCode.En] = "No plugins installed yet. {0}",
			[LanguageCode.Ja] = "プラグインはまだインストールされていません。{0}"
		};
		dictionary["PluginsStatusSummary"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "共 {0} 个插件，{1} 个已启用。{2}",
			[LanguageCode.ZhTw] = "共 {0} 個外掛，{1} 個已啟用。{2}",
			[LanguageCode.En] = "{0} plugin(s), {1} enabled. {2}",
			[LanguageCode.Ja] = "プラグイン {0} 個、有効 {1} 個。{2}"
		};
		dictionary["PluginsSafeModeWarning"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚠️ 安全模式：上次启动时插件引发异常，已自动禁用问题插件，避免反复崩溃。",
			[LanguageCode.ZhTw] = "⚠️ 安全模式：上次啟動時外掛引發例外，已自動停用問題外掛，避免反覆崩潰。",
			[LanguageCode.En] = "⚠️ Safe mode: a plugin threw an exception during the last startup. The offending plugin was disabled automatically to prevent repeated crashes.",
			[LanguageCode.Ja] = "⚠️ セーフモード：前回の起動時にプラグインが例外を発生させたため、問題のあるプラグインを自動的に無効化しました。"
		};
		dictionary["PluginsScanHeaderFound"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "扫描目录里发现 {0} 个 .dll，其中 {1} 个可以安装",
			[LanguageCode.ZhTw] = "掃描目錄裡發現 {0} 個 .dll，其中 {1} 個可以安裝",
			[LanguageCode.En] = "Found {0} .dll file(s) in the scan folder, {1} installable",
			[LanguageCode.Ja] = "スキャンフォルダーに .dll が {0} 個あり、うち {1} 個がインストール可能です"
		};
		dictionary["PluginsScanHeaderNone"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "扫描目录里没有可安装的插件",
			[LanguageCode.ZhTw] = "掃描目錄裡沒有可安裝的外掛",
			[LanguageCode.En] = "No installable plugins in the scan folder",
			[LanguageCode.Ja] = "スキャンフォルダーにインストール可能なプラグインはありません"
		};
		dictionary["PluginsScanHeaderMissing"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "扫描目录不存在（宿主不会创建它）",
			[LanguageCode.ZhTw] = "掃描目錄不存在（宿主不會建立它）",
			[LanguageCode.En] = "Scan folder does not exist (StarPie will not create it)",
			[LanguageCode.Ja] = "スキャンフォルダーが存在しません（StarPie は作成しません）"
		};
		dictionary["PluginsScanPathHint"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "把插件 .dll 放进这个文件夹后点「重新扫描」即可识别。该目录由你自己创建：StarPie 装在只读位置时无权创建它。",
			[LanguageCode.ZhTw] = "把外掛 .dll 放進這個資料夾後點「重新掃描」即可識別。該目錄由你自己建立：StarPie 裝在唯讀位置時無權建立它。",
			[LanguageCode.En] = "Drop the plugin .dll into this folder and hit \"Rescan\" to pick it up. You create this folder yourself: StarPie has no permission to create it when installed in a read-only location.",
			[LanguageCode.Ja] = "このフォルダーにプラグインの .dll を置いて「再スキャン」を押すと認識されます。このフォルダーはご自身で作成してください（StarPie が読み取り専用の場所にインストールされている場合、作成する権限がありません）。"
		};
		dictionary["PluginsMsgTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "StarPie 插件",
			[LanguageCode.ZhTw] = "StarPie 外掛",
			[LanguageCode.En] = "StarPie Plugins",
			[LanguageCode.Ja] = "StarPie プラグイン"
		};
		// 这个键此前是「建了没人用」的孤儿（值为「停用失败：{0}」，全仓零引用），而代码里
		// 实际那处是硬编码的「停用插件 {pluginId} 失败：\n\n{...}」。改为与真实用法一致的
		// 两占位形式并接线，孤儿键少一个、硬编码少一处。
		dictionary["PluginsDisableFailed"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "停用插件 {0} 失败：\n\n{1}",
			[LanguageCode.ZhTw] = "停用外掛 {0} 失敗：\n\n{1}",
			[LanguageCode.En] = "Failed to disable plugin {0}:\n\n{1}",
			[LanguageCode.Ja] = "プラグイン {0} の無効化に失敗しました：\n\n{1}"
		};
		dictionary["PluginsReloadFailed"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "重新加载失败：{0}",
			[LanguageCode.ZhTw] = "重新載入失敗：{0}",
			[LanguageCode.En] = "Reload failed: {0}",
			[LanguageCode.Ja] = "再読み込みに失敗しました：{0}"
		};
		dictionary["PluginsReloadNotStopped"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "旧插件尚未完全停止，不能重新加载：\n\n{0}",
			[LanguageCode.ZhTw] = "舊外掛尚未完全停止，不能重新載入：\n\n{0}",
			[LanguageCode.En] = "The old plugin has not fully stopped, so it cannot be reloaded:\n\n{0}",
			[LanguageCode.Ja] = "古いプラグインがまだ完全に停止していないため、再読み込みできません：\n\n{0}"
		};
		dictionary["PluginsReloaded"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "{0} 已重新加载。",
			[LanguageCode.ZhTw] = "{0} 已重新載入。",
			[LanguageCode.En] = "{0} reloaded.",
			[LanguageCode.Ja] = "{0} を再読み込みしました。"
		};
		dictionary["PluginsReloadedRestartNeeded"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "{0} 已重新加载，但旧程序集未能立即从内存释放，需要重启 StarPie 才能完全生效。",
			[LanguageCode.ZhTw] = "{0} 已重新載入，但舊組件未能立即從記憶體釋放，需要重新啟動 StarPie 才能完全生效。",
			[LanguageCode.En] = "{0} reloaded, but the old assembly could not be released from memory right away; restart StarPie for the change to fully take effect.",
			[LanguageCode.Ja] = "{0} を再読み込みしましたが、古いアセンブリをメモリから解放できませんでした。完全に反映するには StarPie を再起動してください。"
		};
		dictionary["PluginsCandidateGone"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "这枚候选已经不在扫描目录里了（可能刚被移走或改名）。已重新扫描，请再试一次。",
			[LanguageCode.ZhTw] = "這枚候選已經不在掃描目錄裡了（可能剛被移走或改名）。已重新掃描，請再試一次。",
			[LanguageCode.En] = "This candidate is no longer in the scan folder (it may have been moved or renamed). Rescanned, please try again.",
			[LanguageCode.Ja] = "この候補はスキャンフォルダーに存在しません（移動または名前変更された可能性があります）。再スキャンしましたので、もう一度お試しください。"
		};
		dictionary["PluginsInstallFailed"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "安装失败：{0}",
			[LanguageCode.ZhTw] = "安裝失敗：{0}",
			[LanguageCode.En] = "Install failed: {0}",
			[LanguageCode.Ja] = "インストールに失敗しました：{0}"
		};
		dictionary["PluginsUpdatedAndEnabled"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "{0} 已更新到 {1} 并已启用。\n\n如果它之前已经在运行，旧程序集要到下次启动 StarPie 才会完全从内存释放。",
			[LanguageCode.ZhTw] = "{0} 已更新到 {1} 並已啟用。\n\n如果它之前已經在執行，舊組件要到下次啟動 StarPie 才會完全從記憶體釋放。",
			[LanguageCode.En] = "{0} was updated to {1} and enabled.\n\nIf it was already running, the old assembly stays in memory until the next StarPie restart.",
			[LanguageCode.Ja] = "{0} を {1} に更新して有効化しました。\n\n既に実行中だった場合、古いアセンブリは次回 StarPie を起動するまでメモリに残ります。"
		};
		dictionary["PluginsConfirmAboutToInstall"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "即将安装：{0} {1}",
			[LanguageCode.ZhTw] = "即將安裝：{0} {1}",
			[LanguageCode.En] = "About to install: {0} {1}",
			[LanguageCode.Ja] = "インストール予定：{0} {1}"
		};
		dictionary["PluginsConfirmFile"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "文件：{0}",
			[LanguageCode.ZhTw] = "檔案：{0}",
			[LanguageCode.En] = "File: {0}",
			[LanguageCode.Ja] = "ファイル：{0}"
		};
		dictionary["PluginsConfirmCapabilities"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "该插件声明了以下能力：",
			[LanguageCode.ZhTw] = "此外掛宣告了以下能力：",
			[LanguageCode.En] = "This plugin declares the following capabilities:",
			[LanguageCode.Ja] = "このプラグインは以下の機能を宣言しています："
		};
		dictionary["PluginsConfirmScanResult"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "扫描结果：{0}",
			[LanguageCode.ZhTw] = "掃描結果：{0}",
			[LanguageCode.En] = "Scan result: {0}",
			[LanguageCode.Ja] = "スキャン結果：{0}"
		};
		// ---- 能力位 → 安装确认页上那一行风险说明 ----
		// 顺序与 PluginCapability 的声明顺序一致（由轻到重），表在 PluginCapabilityLabels.All 里。
		// 这组词条原先根本不存在（整块硬编码中文），是自检 [3e] 的「英文页不许有方块字」
		// 那条断言把它们逼出来的 —— 它藏在「已经接好 i18n 的确认页」内部，肉眼看不出来。
		//
		// 每一条都要能回答「用户看到这行字，脑子里出现的后果是不是插件真会做的事」。
		// 开头「· 」是列表符号，属于排版字形，四种语言都一样，故留在文案里而不是拼在代码里。
		dictionary["PluginCapabilityProcess"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "· 启动进程 / 执行命令",
			[LanguageCode.ZhTw] = "· 啟動行程 / 執行命令",
			[LanguageCode.En] = "· Start processes / run commands",
			[LanguageCode.Ja] = "· プロセスの起動 / コマンドの実行"
		};
		dictionary["PluginCapabilityFileSystem"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "· 读写你的文件",
			[LanguageCode.ZhTw] = "· 讀寫你的檔案",
			[LanguageCode.En] = "· Read and write your files",
			[LanguageCode.Ja] = "· ファイルの読み書き"
		};
		dictionary["PluginCapabilityNetwork"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "· 访问网络",
			[LanguageCode.ZhTw] = "· 存取網路",
			[LanguageCode.En] = "· Access the network",
			[LanguageCode.Ja] = "· ネットワークへのアクセス"
		};
		dictionary["PluginCapabilityClipboard"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "· 读取或修改剪贴板",
			[LanguageCode.ZhTw] = "· 讀取或修改剪貼簿",
			[LanguageCode.En] = "· Read or modify the clipboard",
			[LanguageCode.Ja] = "· クリップボードの読み取り・変更"
		};
		dictionary["PluginCapabilityRegistry"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "· 读写注册表",
			[LanguageCode.ZhTw] = "· 讀寫登錄檔",
			[LanguageCode.En] = "· Read and write the registry",
			[LanguageCode.Ja] = "· レジストリの読み書き"
		};
		dictionary["PluginCapabilityGlobalHook"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "· 安装全局键盘/鼠标钩子",
			[LanguageCode.ZhTw] = "· 安裝全域鍵盤/滑鼠鉤子",
			[LanguageCode.En] = "· Install global keyboard/mouse hooks",
			[LanguageCode.Ja] = "· グローバルなキーボード・マウスフックの設置"
		};
		dictionary["PluginCapabilityUi"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "· 显示界面与通知",
			[LanguageCode.ZhTw] = "· 顯示介面與通知",
			[LanguageCode.En] = "· Show windows and notifications",
			[LanguageCode.Ja] = "· ウィンドウと通知の表示"
		};
		dictionary["PluginCapabilityAdmin"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "· 需要管理员权限",
			[LanguageCode.ZhTw] = "· 需要系統管理員權限",
			[LanguageCode.En] = "· Require administrator privileges",
			[LanguageCode.Ja] = "· 管理者権限が必要"
		};
		dictionary["PluginCapabilityWindowControl"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "· 移动 / 置顶 / 改变你正在使用的窗口",
			[LanguageCode.ZhTw] = "· 移動 / 置頂 / 改變你正在使用中的視窗",
			[LanguageCode.En] = "· Move, pin, or alter the window you are using",
			[LanguageCode.Ja] = "· 使用中のウィンドウの移動 / 最前面表示 / 変更"
		};
		dictionary["PluginCapabilityScreenCapture"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "· 读取屏幕内容（截屏）",
			[LanguageCode.ZhTw] = "· 讀取螢幕內容（截圖）",
			[LanguageCode.En] = "· Read screen contents (screenshot)",
			[LanguageCode.Ja] = "· 画面内容の読み取り（スクリーンショット）"
		};
		dictionary["PluginCapabilityInputSimulation"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "· 向当前窗口发送按键",
			[LanguageCode.ZhTw] = "· 向目前視窗傳送按鍵",
			[LanguageCode.En] = "· Send keystrokes to the current window",
			[LanguageCode.Ja] = "· 現在のウィンドウへキー入力を送信"
		};
		dictionary["PluginCapabilityNone"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "（无）",
			[LanguageCode.ZhTw] = "（無）",
			[LanguageCode.En] = "(none)",
			[LanguageCode.Ja] = "（なし）"
		};

		// ---- 安装确认页：文件事实 ----
		// 这一组是「它到底是什么」的客观信息，两条安装路径（扫描目录候选 / 手动选 .dll）共用。
		// 曾经手动安装那份是整块硬编码中文、候选那份走词条，同一个确认语义两条路 —— 现已归一。
		//
		// 与 PluginScanFailureSeparator 同理：**分隔符也是文案**。顿号「、」是中文标点，
		// 英文里必须换成", " —— 写死顿号的话，英文页会出现
		// 「Declared capabilities: Process、WindowControl」这种中英标点混排。
		// 注意别把它和纯字形分隔符（全角空格 + 竖线那种）混为一谈：后者是图形，不随语言变。
		dictionary["PluginsEnumSeparator"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "、",
			[LanguageCode.ZhTw] = "、",
			[LanguageCode.En] = ", ",
			[LanguageCode.Ja] = "、"
		};
		dictionary["PluginsConfirmPluginId"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "插件 ID：{0}",
			[LanguageCode.ZhTw] = "外掛 ID：{0}",
			[LanguageCode.En] = "Plugin ID: {0}",
			[LanguageCode.Ja] = "プラグイン ID：{0}"
		};
		dictionary["PluginsConfirmAuthor"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "作者：{0}",
			[LanguageCode.ZhTw] = "作者：{0}",
			[LanguageCode.En] = "Author: {0}",
			[LanguageCode.Ja] = "作者：{0}"
		};
		dictionary["PluginsConfirmDescription"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "说明：{0}",
			[LanguageCode.ZhTw] = "說明：{0}",
			[LanguageCode.En] = "Description: {0}",
			[LanguageCode.Ja] = "説明：{0}"
		};
		dictionary["PluginsConfirmTargetFramework"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "目标框架：{0}",
			[LanguageCode.ZhTw] = "目標框架：{0}",
			[LanguageCode.En] = "Target framework: {0}",
			[LanguageCode.Ja] = "ターゲットフレームワーク：{0}"
		};
		dictionary["PluginsConfirmMachine"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "平台架构：{0}",
			[LanguageCode.ZhTw] = "平台架構：{0}",
			[LanguageCode.En] = "Platform architecture: {0}",
			[LanguageCode.Ja] = "プラットフォーム：{0}"
		};
		dictionary["PluginsConfirmFileSize"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "文件大小：{0}",
			[LanguageCode.ZhTw] = "檔案大小：{0}",
			[LanguageCode.En] = "File size: {0}",
			[LanguageCode.Ja] = "ファイルサイズ：{0}"
		};
		dictionary["PluginsConfirmSha256"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "SHA256：{0}…",
			[LanguageCode.ZhTw] = "SHA256：{0}…",
			[LanguageCode.En] = "SHA256: {0}…",
			[LanguageCode.Ja] = "SHA256：{0}…"
		};
		dictionary["PluginsConfirmSignature"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "数字签名：{0}",
			[LanguageCode.ZhTw] = "數位簽章：{0}",
			[LanguageCode.En] = "Digital signature: {0}",
			[LanguageCode.Ja] = "デジタル署名：{0}"
		};
		dictionary["PluginsConfirmUnsigned"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "无（未签名）",
			[LanguageCode.ZhTw] = "無（未簽章）",
			[LanguageCode.En] = "None (unsigned)",
			[LanguageCode.Ja] = "なし（未署名）"
		};
		dictionary["PluginsConfirmManifestSource"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "清单来源：{0}",
			[LanguageCode.ZhTw] = "清單來源：{0}",
			[LanguageCode.En] = "Manifest source: {0}",
			[LanguageCode.Ja] = "マニフェストの取得元：{0}"
		};
		dictionary["PluginsConfirmDeclaredCapabilities"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "声明能力：{0}",
			[LanguageCode.ZhTw] = "宣告能力：{0}",
			[LanguageCode.En] = "Declared capabilities: {0}",
			[LanguageCode.Ja] = "宣言された機能：{0}"
		};
		dictionary["PluginsConfirmNoCapabilities"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "无",
			[LanguageCode.ZhTw] = "無",
			[LanguageCode.En] = "none",
			[LanguageCode.Ja] = "なし"
		};
		dictionary["PluginsConfirmTargetPath"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "拟安装到：{0}",
			[LanguageCode.ZhTw] = "擬安裝至：{0}",
			[LanguageCode.En] = "Will be installed to: {0}",
			[LanguageCode.Ja] = "インストール先：{0}"
		};

		// ---- 安装确认页：装下去会发生什么 ----
		// 与 PluginCandidateState 一一对应。刻意与「启用语义」（下两个键）拆开：
		// 「覆盖了哪一份」和「装完启不启用」是两件独立的事，写进一句话里就没法单独改一条。
		dictionary["PluginsConfirmActFresh"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "这是全新安装，复制进去不会动到已有的任何插件。",
			[LanguageCode.ZhTw] = "這是全新安裝，複製進去不會動到既有的任何外掛。",
			[LanguageCode.En] = "This is a fresh install; nothing already installed is touched.",
			[LanguageCode.Ja] = "これは新規インストールです。既存のプラグインには影響しません。"
		};
		dictionary["PluginsConfirmActUpdate"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "这会用较新的版本覆盖现有安装。如果插件正在运行，宿主会先自动停用它再替换文件。",
			[LanguageCode.ZhTw] = "這會用較新的版本覆蓋現有安裝。如果外掛正在執行，宿主會先自動停用它再取代檔案。",
			[LanguageCode.En] = "This overwrites the existing installation with a newer version. If the plugin is running, StarPie disables it first, then replaces the files.",
			[LanguageCode.Ja] = "これにより、より新しい版で既存のインストールを上書きします。プラグインが実行中の場合は、先に自動で無効化してからファイルを置き換えます。"
		};
		dictionary["PluginsConfirmActDowngrade"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚠️ 这会用更旧的版本覆盖现有安装。除非你明确需要退回旧版，否则不建议继续。",
			[LanguageCode.ZhTw] = "⚠️ 這會用更舊的版本覆蓋現有安裝。除非你明確需要退回舊版，否則不建議繼續。",
			[LanguageCode.En] = "⚠️ This overwrites the existing installation with an older version. Not recommended unless you specifically need to roll back.",
			[LanguageCode.Ja] = "⚠️ これにより、より古い版で既存のインストールを上書きします。旧版へ戻す必要が明確でない限り推奨しません。"
		};
		dictionary["PluginsConfirmActReplaced"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "这会覆盖现有安装：版本号相同，但文件内容不同（重编译或手改过）。",
			[LanguageCode.ZhTw] = "這會覆蓋現有安裝：版本號相同，但檔案內容不同（重新編譯或手動改過）。",
			[LanguageCode.En] = "This overwrites the existing installation: same version number, different file content (rebuilt or hand-edited).",
			[LanguageCode.Ja] = "これは既存のインストールを上書きします（バージョンは同じでも、ファイルの内容が異なります）。"
		};
		dictionary["PluginsConfirmActInstalled"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "已装的那份与这枚文件完全相同（同版本、同内容），继续安装只会把同样的文件再复制一遍。",
			[LanguageCode.ZhTw] = "已裝的那份與這枚檔案完全相同（同版本、同內容），繼續安裝只會把同樣的檔案再複製一遍。",
			[LanguageCode.En] = "What is installed is identical to this file (same version, same content); continuing only copies the same file again.",
			[LanguageCode.Ja] = "インストール済みのものとこのファイルは完全に同一です（同じバージョン・同じ内容）。続行しても同じファイルをコピーし直すだけです。"
		};
		dictionary["PluginsConfirmActVersionUnknown"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "已装版本与这枚文件的版本号至少有一侧无法解析，判断不出新旧 —— 继续安装会直接覆盖现有安装。",
			[LanguageCode.ZhTw] = "已裝版本與這枚檔案的版本號至少有一側無法解析，判斷不出新舊 —— 繼續安裝會直接覆蓋現有安裝。",
			[LanguageCode.En] = "At least one of the version numbers cannot be parsed, so newer/older cannot be determined — continuing overwrites the existing installation.",
			[LanguageCode.Ja] = "既存版とこのファイルのバージョン番号の少なくとも一方が解釈できないため、新旧を判断できません。続行すると既存のインストールを上書きします。"
		};
		dictionary["PluginsConfirmActExternal"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "这个 ID 目前由「外部路径登记」占用（见上方扫描结果）。继续安装会改由数据目录里的副本接管。",
			[LanguageCode.ZhTw] = "這個 ID 目前由「外部路徑登記」佔用（見上方掃描結果）。繼續安裝會改由資料目錄裡的副本接管。",
			[LanguageCode.En] = "This ID is currently held by an external-path registration (see the scan result above). Continuing makes the copy in the data folder take over.",
			[LanguageCode.Ja] = "この ID は現在「外部パス登録」が使用しています（上のスキャン結果を参照）。続行すると、データフォルダー内のコピーが引き継ぎます。"
		};
		dictionary["PluginsConfirmEnableNow"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "装完会立即启用。",
			[LanguageCode.ZhTw] = "裝完會立即啟用。",
			[LanguageCode.En] = "It will be enabled right after installation.",
			[LanguageCode.Ja] = "インストール後すぐに有効化されます。"
		};
		dictionary["PluginsConfirmEnableLater"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "装完处于「未启用」状态：需要你到插件列表里勾选启用，它注册的动作才会出现在「手势与动作」页的动作类型下拉框中。",
			[LanguageCode.ZhTw] = "裝完處於「未啟用」狀態：需要你到外掛清單裡勾選啟用，它註冊的動作才會出現在「手勢與動作」頁的動作類型下拉選單中。",
			[LanguageCode.En] = "It stays disabled after installation: tick \"Enabled\" in the plugin list, and only then do its actions appear in the action-type dropdown on the Gestures & Actions page.",
			[LanguageCode.Ja] = "インストール直後は「無効」のままです。プラグイン一覧で有効化してはじめて、登録した動作が「ジェスチャーと動作」ページの動作タイプのドロップダウンに現れます。"
		};

		// ---- 安装确认页：安全提示 ----
		dictionary["PluginsConfirmSecurityTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚠️ 安全提示",
			[LanguageCode.ZhTw] = "⚠️ 安全提示",
			[LanguageCode.En] = "⚠️ Security notice",
			[LanguageCode.Ja] = "⚠️ セキュリティ上の注意"
		};
		dictionary["PluginsConfirmSecurityBody"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "插件会以 StarPie 当前的权限在你的电脑上运行代码，请只安装你信任的来源。",
			[LanguageCode.ZhTw] = "外掛會以 StarPie 目前的權限在你的電腦上執行代碼，請只安裝你信任的來源。",
			[LanguageCode.En] = "Plugins run code on your computer with StarPie's own privileges, so only install sources you trust.",
			[LanguageCode.Ja] = "プラグインは StarPie と同じ権限でお使いの PC 上でコードを実行します。信頼できる提供元のみインストールしてください。"
		};
		dictionary["PluginsConfirmAccept"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "点击「确定」表示你已了解并接受以上风险。",
			[LanguageCode.ZhTw] = "點擊「確定」表示你已了解並接受以上風險。",
			[LanguageCode.En] = "Clicking OK means you understand and accept these risks.",
			[LanguageCode.Ja] = "「OK」を押すと、以上のリスクを理解し受け入れたものとみなします。"
		};

		// ---- 手动安装（选择 .dll）这条路的对话框与结果提示 ----
		dictionary["PluginsPickDllTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "选择要安装的插件 (.dll)",
			[LanguageCode.ZhTw] = "選擇要安裝的外掛 (.dll)",
			[LanguageCode.En] = "Select a plugin to install (.dll)",
			[LanguageCode.Ja] = "インストールするプラグインを選択 (.dll)"
		};
		dictionary["PluginsPickDllFilter"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "插件程序集 (*.dll)|*.dll|所有文件 (*.*)|*.*",
			[LanguageCode.ZhTw] = "外掛組件 (*.dll)|*.dll|所有檔案 (*.*)|*.*",
			[LanguageCode.En] = "Plugin assemblies (*.dll)|*.dll|All files (*.*)|*.*",
			[LanguageCode.Ja] = "プラグイン アセンブリ (*.dll)|*.dll|すべてのファイル (*.*)|*.*"
		};
		dictionary["PluginsReadFileFailed"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "读取所选文件时出错：\n{0}",
			[LanguageCode.ZhTw] = "讀取所選檔案時發生錯誤：\n{0}",
			[LanguageCode.En] = "Failed to read the selected file:\n{0}",
			[LanguageCode.Ja] = "選択したファイルの読み込みに失敗しました:\n{0}"
		};
		dictionary["PluginsNotAPlugin"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "这个文件不能作为 StarPie 插件安装。\n\n原因：{0}\n详情：{1}\n\n建议：{2}\n\n文件：{3}",
			[LanguageCode.ZhTw] = "這個檔案不能作為 StarPie 外掛安裝。\n\n原因：{0}\n詳情：{1}\n\n建議：{2}\n\n檔案：{3}",
			[LanguageCode.En] = "This file cannot be installed as a StarPie plugin.\n\nReason: {0}\nDetails: {1}\n\nSuggestion: {2}\n\nFile: {3}",
			[LanguageCode.Ja] = "このファイルは StarPie プラグインとしてインストールできません。\n\n理由：{0}\n詳細：{1}\n\n推奨：{2}\n\nファイル：{3}"
		};
		dictionary["PluginsInstalledNotify"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "{0} 安装完成，到列表中启用它即可使用。",
			[LanguageCode.ZhTw] = "{0} 安裝完成，到清單中啟用它即可使用。",
			[LanguageCode.En] = "{0} installed — enable it in the list to start using it.",
			[LanguageCode.Ja] = "{0} をインストールしました。一覧で有効化すると使えます。"
		};
		dictionary["PluginsInstalledDisabled"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "插件 {0} 已安装。\n\n它当前处于「未启用」状态。在列表里勾选「启用」后，它注册的动作才会出现在「手势与动作」页的动作类型下拉框中，从而可以分配到轮盘上。",
			[LanguageCode.ZhTw] = "外掛 {0} 已安裝。\n\n它目前處於「未啟用」狀態。在清單裡勾選「啟用」後，它註冊的動作才會出現在「手勢與動作」頁的動作類型下拉選單中，從而可以分配到輪盤上。",
			[LanguageCode.En] = "Plugin {0} is installed.\n\nIt is currently disabled. Tick \"Enabled\" in the list, and only then do its actions appear in the action-type dropdown on the Gestures & Actions page, where you can assign them to the wheel.",
			[LanguageCode.Ja] = "プラグイン {0} をインストールしました。\n\n現在は「無効」の状態です。一覧で「有効」にチェックを入れてはじめて、登録した動作が「ジェスチャーと動作」ページの動作タイプのドロップダウンに現れ、ホイールに割り当てられるようになります。"
		};
		dictionary["PluginsConfirmTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "确认安装插件",
			[LanguageCode.ZhTw] = "確認安裝外掛",
			[LanguageCode.En] = "Confirm plugin installation",
			[LanguageCode.Ja] = "プラグインのインストール確認"
		};
		dictionary["PluginsScanFolderMissing"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "扫描目录还不存在：\n{0}\n\nStarPie 不会替你创建它 —— 程序可能装在只读位置，宿主对这里只读不写。\n如需使用随包附带的插件，请手工创建该文件夹，把插件 .dll 放进去，再点「重新扫描」。",
			[LanguageCode.ZhTw] = "掃描目錄還不存在：\n{0}\n\nStarPie 不會替你建立它 —— 程式可能裝在唯讀位置，宿主對這裡唯讀不寫。\n如需使用隨附的外掛，請手動建立該資料夾，把外掛 .dll 放進去，再點「重新掃描」。",
			[LanguageCode.En] = "The scan folder does not exist yet:\n{0}\n\nStarPie will not create it for you — the program may be installed in a read-only location, and StarPie never writes there.\nTo use the plugins shipped with the package, create the folder yourself, drop the plugin .dll into it, then click \"Rescan\".",
			[LanguageCode.Ja] = "スキャンフォルダーがまだ存在しません：\n{0}\n\nStarPie が代わりに作成することはありません（読み取り専用の場所にインストールされている場合があり、ホストはここへ書き込みません）。\n同梱のプラグインを使う場合は、このフォルダーを手動で作成し、プラグインの .dll を置いてから「再スキャン」を押してください。"
		};
		dictionary["PluginsEmptyTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "还没有安装任何插件",
			[LanguageCode.ZhTw] = "還沒有安裝任何外掛",
			[LanguageCode.En] = "No plugins installed yet",
			[LanguageCode.Ja] = "プラグインはまだインストールされていません"
		};
		dictionary["PluginsEmptyHint"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "把插件 .dll 放进程序目录的 plugin 文件夹并点上方「重新扫描」，或直接点右上角「安装插件 (.dll)」选择文件",
			[LanguageCode.ZhTw] = "把外掛 .dll 放進程式目錄的 plugin 資料夾並點上方「重新掃描」，或直接點右上角「安裝外掛 (.dll)」選擇檔案",
			[LanguageCode.En] = "Drop the plugin .dll into the \"plugin\" folder next to the program and hit \"Rescan\" above, or click \"Install Plugin (.dll)\" at the top right to pick a file",
			[LanguageCode.Ja] = "プラグインの .dll をプログラムフォルダー内の plugin フォルダーに置いて上の「再スキャン」を押すか、右上の「プラグインをインストール (.dll)」でファイルを選択してください"
		};
		dictionary["PluginsOfficialHeader"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "官方插件",
			[LanguageCode.ZhTw] = "官方外掛",
			[LanguageCode.En] = "Official plugins",
			[LanguageCode.Ja] = "公式プラグイン"
		};
		dictionary["PluginsOfficialStatusHint"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "从 StarPie-Official-Plugins 下载经过 SHA-256 校验的官方模块",
			[LanguageCode.ZhTw] = "從 StarPie-Official-Plugins 下載經過 SHA-256 校驗的官方模組",
			[LanguageCode.En] = "Official modules are downloaded from StarPie-Official-Plugins and verified with SHA-256",
			[LanguageCode.Ja] = "公式モジュールは StarPie-Official-Plugins からダウンロードし、SHA-256 で検証します"
		};
		dictionary["PluginsOfficialLoading"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "正在从 GitHub 获取官方插件目录…",
			[LanguageCode.ZhTw] = "正在從 GitHub 取得官方外掛目錄…",
			[LanguageCode.En] = "Fetching the official plugin catalog from GitHub…",
			[LanguageCode.Ja] = "GitHub から公式プラグインカタログを取得しています…"
		};
		dictionary["PluginsOfficialCatalogInfo"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "目录 {0} · {1} 个模块 · 来源 StarPie-Official-Plugins",
			[LanguageCode.ZhTw] = "目錄 {0} · {1} 個模組 · 來源 StarPie-Official-Plugins",
			[LanguageCode.En] = "Catalog {0} · {1} modules · from StarPie-Official-Plugins",
			[LanguageCode.Ja] = "カタログ {0} · {1} モジュール · 提供元 StarPie-Official-Plugins"
		};
		dictionary["PluginsOfficialUnavailable"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "官方插件目录暂时不可用：{0}",
			[LanguageCode.ZhTw] = "官方外掛目錄暫時無法使用：{0}",
			[LanguageCode.En] = "The official plugin catalog is temporarily unavailable: {0}",
			[LanguageCode.Ja] = "公式プラグインカタログは一時的に利用できません：{0}"
		};
		dictionary["PluginsOfficialRefreshButton"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🌐 刷新目录",
			[LanguageCode.ZhTw] = "🌐 重新整理目錄",
			[LanguageCode.En] = "🌐 Refresh catalog",
			[LanguageCode.Ja] = "🌐 カタログを更新"
		};
		dictionary["PluginsOfficialMsgTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "StarPie 官方插件",
			[LanguageCode.ZhTw] = "StarPie 官方外掛",
			[LanguageCode.En] = "StarPie official plugins",
			[LanguageCode.Ja] = "StarPie 公式プラグイン"
		};
		dictionary["PluginsOfficialInstallFailed"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "官方插件 {0} 安装失败：\n\n{1}",
			[LanguageCode.ZhTw] = "官方外掛 {0} 安裝失敗：\n\n{1}",
			[LanguageCode.En] = "Failed to install official plugin {0}:\n\n{1}",
			[LanguageCode.Ja] = "公式プラグイン {0} のインストールに失敗しました：\n\n{1}"
		};
		dictionary["PluginsOfficialInstalled"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "官方插件 {0} v{1} 已下载、校验并启用。",
			[LanguageCode.ZhTw] = "官方外掛 {0} v{1} 已下載、校驗並啟用。",
			[LanguageCode.En] = "Official plugin {0} v{1} has been downloaded, verified and enabled.",
			[LanguageCode.Ja] = "公式プラグイン {0} v{1} をダウンロード・検証し、有効化しました。"
		};
		dictionary["PluginsOfficialStateNotInstalled"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "未安装",
			[LanguageCode.ZhTw] = "尚未安裝",
			[LanguageCode.En] = "Not installed",
			[LanguageCode.Ja] = "未インストール"
		};
		dictionary["PluginsOfficialStateUpToDate"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "已是最新",
			[LanguageCode.ZhTw] = "已是最新",
			[LanguageCode.En] = "Up to date",
			[LanguageCode.Ja] = "最新です"
		};
		dictionary["PluginsOfficialStateUpdateAvailable"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "已装 v{0} · 有更新",
			[LanguageCode.ZhTw] = "已裝 v{0} · 有更新",
			[LanguageCode.En] = "v{0} installed · update available",
			[LanguageCode.Ja] = "v{0} 導入済み · 更新あり"
		};
		dictionary["PluginsOfficialActionInstall"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⬇️ 下载并安装",
			[LanguageCode.ZhTw] = "⬇️ 下載並安裝",
			[LanguageCode.En] = "⬇️ Download and install",
			[LanguageCode.Ja] = "⬇️ ダウンロードしてインストール"
		};
		dictionary["PluginsOfficialActionInstalled"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "已安装",
			[LanguageCode.ZhTw] = "已安裝",
			[LanguageCode.En] = "Installed",
			[LanguageCode.Ja] = "インストール済み"
		};
		dictionary["PluginsOfficialActionUpdate"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⬆️ 更新",
			[LanguageCode.ZhTw] = "⬆️ 更新",
			[LanguageCode.En] = "⬆️ Update",
			[LanguageCode.Ja] = "⬆️ 更新"
		};
		dictionary["PluginsOfficialSummaryFallback"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "官方动作模块",
			[LanguageCode.ZhTw] = "官方動作模組",
			[LanguageCode.En] = "Official action module",
			[LanguageCode.Ja] = "公式アクションモジュール"
		};
		dictionary["PluginsOfficialClaimSeparator"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "、",
			[LanguageCode.ZhTw] = "、",
			[LanguageCode.En] = ", ",
			[LanguageCode.Ja] = "、"
		};
		dictionary["PluginsActionNotSelected"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "当前动作尚未选定具体的插件动作。",
			[LanguageCode.ZhTw] = "目前動作尚未選定具體的外掛動作。",
			[LanguageCode.En] = "No specific plugin action has been selected for this action yet.",
			[LanguageCode.Ja] = "この動作には具体的なプラグイン動作がまだ選択されていません。"
		};
		dictionary["PluginsActionPluginNotFound"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "未找到插件 {0}，请到「插件与扩展」页查看。",
			[LanguageCode.ZhTw] = "找不到外掛 {0}，請到「外掛與擴充」頁查看。",
			[LanguageCode.En] = "Plugin {0} was not found. Check the Plugins page.",
			[LanguageCode.Ja] = "プラグイン {0} が見つかりません。「プラグイン」ページを確認してください。"
		};

		// ---- 插件管理页：已安装插件的卡片 ----
		// 这一组由 PluginListItem 在**构建时**取当前语言填入（每次刷新整体重建列表，
		// 所以切语言后重新绑定数据源即可换语言）。它们此前是 BuildPluginListItem 里的拼串。
		dictionary["PluginsCardAuthor"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "作者 {0}",
			[LanguageCode.ZhTw] = "作者 {0}",
			[LanguageCode.En] = "by {0}",
			[LanguageCode.Ja] = "作者 {0}"
		};
		dictionary["PluginsCardActionCount"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "贡献 {0} 个动作",
			[LanguageCode.ZhTw] = "貢獻 {0} 個動作",
			[LanguageCode.En] = "provides {0} action(s)",
			[LanguageCode.Ja] = "動作 {0} 個を提供"
		};
		dictionary["PluginsCardNotLoaded"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "未加载",
			[LanguageCode.ZhTw] = "未載入",
			[LanguageCode.En] = "not loaded",
			[LanguageCode.Ja] = "未読み込み"
		};
		dictionary["PluginsCardCapabilities"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "声明能力：{0}",
			[LanguageCode.ZhTw] = "宣告能力：{0}",
			[LanguageCode.En] = "Declared capabilities: {0}",
			[LanguageCode.Ja] = "宣言された機能：{0}"
		};
		dictionary["PluginsCardSigned"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "已签名",
			[LanguageCode.ZhTw] = "已簽章",
			[LanguageCode.En] = "signed",
			[LanguageCode.Ja] = "署名済み"
		};
		dictionary["PluginsCardUnsigned"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "未签名",
			[LanguageCode.ZhTw] = "未簽章",
			[LanguageCode.En] = "unsigned",
			[LanguageCode.Ja] = "未署名"
		};
		dictionary["PluginsCardExternalPath"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "外部路径 {0}",
			[LanguageCode.ZhTw] = "外部路徑 {0}",
			[LanguageCode.En] = "external path {0}",
			[LanguageCode.Ja] = "外部パス {0}"
		};
		dictionary["PluginsCardRestartReason"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "旧程序集尚未从内存释放，重启 StarPie 后才会完全生效。",
			[LanguageCode.ZhTw] = "舊組件尚未從記憶體釋放，重新啟動 StarPie 後才會完全生效。",
			[LanguageCode.En] = "The old assembly is still held in memory; it takes full effect only after restarting StarPie.",
			[LanguageCode.Ja] = "古いアセンブリがまだメモリ上に残っています。StarPie を再起動すると完全に反映されます。"
		};

		// ---- 插件管理页：卡片上的两个按钮（DataTemplate 内的文字） ----
		// 它们在 ListBox.ItemTemplate 里，命名域不同 ⇒ Name 无效，只能 {Binding} 到
		// PluginListItem 的本地化属性。写成 XAML 字面量的话，静态扫「有 Name + 硬编码中文」
		// 与按 auto_id 的 UI 断言**双双看不见**它们（实测就是这样漏了很久）。
		dictionary["PluginsCardEnableCheckBox"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "启用",
			[LanguageCode.ZhTw] = "啟用",
			[LanguageCode.En] = "Enable",
			[LanguageCode.Ja] = "有効化"
		};
		dictionary["PluginsCardUninstallButton"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🗑 卸载",
			[LanguageCode.ZhTw] = "🗑 解除安裝",
			[LanguageCode.En] = "🗑 Uninstall",
			[LanguageCode.Ja] = "🗑 アンインストール"
		};

		// ---- 插件管理页：运行时状态徽标 ----
		// 与 PluginRuntimeState 一一对应，由 DescribePluginState 的穷尽 switch 取用；
		// 加枚举成员不加词条会触发 CS8509（编译期护栏），不会静默退回一个英文枚举名。
		dictionary["PluginsStateActive"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "运行中",
			[LanguageCode.ZhTw] = "執行中",
			[LanguageCode.En] = "Running",
			[LanguageCode.Ja] = "実行中"
		};
		dictionary["PluginsStateActiveRestartPending"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "运行中 · 待重启",
			[LanguageCode.ZhTw] = "執行中 · 待重啟",
			[LanguageCode.En] = "Running · restart pending",
			[LanguageCode.Ja] = "実行中 · 再起動待ち"
		};
		dictionary["PluginsStateLoading"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "加载中",
			[LanguageCode.ZhTw] = "載入中",
			[LanguageCode.En] = "Loading",
			[LanguageCode.Ja] = "読み込み中"
		};
		dictionary["PluginsStateEnabledPendingLoad"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "已启用 · 待加载",
			[LanguageCode.ZhTw] = "已啟用 · 待載入",
			[LanguageCode.En] = "Enabled · pending load",
			[LanguageCode.Ja] = "有効 · 読み込み待ち"
		};
		dictionary["PluginsStateStopping"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "正在停止",
			[LanguageCode.ZhTw] = "正在停止",
			[LanguageCode.En] = "Stopping",
			[LanguageCode.Ja] = "停止中"
		};
		dictionary["PluginsStateDisabled"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "未启用",
			[LanguageCode.ZhTw] = "未啟用",
			[LanguageCode.En] = "Not enabled",
			[LanguageCode.Ja] = "無効"
		};
		dictionary["PluginsStateFaulted"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "运行异常",
			[LanguageCode.ZhTw] = "執行異常",
			[LanguageCode.En] = "Runtime error",
			[LanguageCode.Ja] = "実行時エラー"
		};
		dictionary["PluginsStateQuarantined"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "已隔离",
			[LanguageCode.ZhTw] = "已隔離",
			[LanguageCode.En] = "Quarantined",
			[LanguageCode.Ja] = "隔離済み"
		};
		dictionary["PluginsStateFailed"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "加载失败",
			[LanguageCode.ZhTw] = "載入失敗",
			[LanguageCode.En] = "Load failed",
			[LanguageCode.Ja] = "読み込み失敗"
		};
		dictionary["PluginsStateIncompatible"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "不兼容",
			[LanguageCode.ZhTw] = "不相容",
			[LanguageCode.En] = "Incompatible",
			[LanguageCode.Ja] = "非互換"
		};
		dictionary["PluginsStateRestartPending"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "待重启生效",
			[LanguageCode.ZhTw] = "待重啟生效",
			[LanguageCode.En] = "Restart required",
			[LanguageCode.Ja] = "再起動で有効"
		};

		// ---- 插件管理页：弹窗与托盘提示 ----
		dictionary["PluginsNotReady"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "插件系统尚未完成初始化。请稍候片刻再试，或重启 StarPie。",
			[LanguageCode.ZhTw] = "外掛系統尚未完成初始化。請稍候片刻再試，或重新啟動 StarPie。",
			[LanguageCode.En] = "The plugin system has not finished initializing yet. Please wait a moment and try again, or restart StarPie.",
			[LanguageCode.Ja] = "プラグインシステムの初期化が完了していません。しばらく待ってから再試行するか、StarPie を再起動してください。"
		};
		dictionary["PluginsOpenDataFolderFailed"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "打开插件目录失败：{0}",
			[LanguageCode.ZhTw] = "開啟外掛目錄失敗：{0}",
			[LanguageCode.En] = "Failed to open the plugin folder: {0}",
			[LanguageCode.Ja] = "プラグインフォルダーを開けませんでした：{0}"
		};
		dictionary["PluginsOpenScanFolderFailed"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "打开扫描目录失败：{0}",
			[LanguageCode.ZhTw] = "開啟掃描目錄失敗：{0}",
			[LanguageCode.En] = "Failed to open the scan folder: {0}",
			[LanguageCode.Ja] = "スキャンフォルダーを開けませんでした：{0}"
		};
		// 英文 / 日文模板末尾刻意留一个空格与提示句分隔；提示为空时会留一个**行尾空格**。
		// 气泡提示里看不见，换来的是词条值不必藏一个首空格（那种空格更容易在维护中丢失）。
		dictionary["PluginsRescanFound"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "扫描完成，新发现 {0} 个插件。{1}",
			[LanguageCode.ZhTw] = "掃描完成，新發現 {0} 個外掛。{1}",
			[LanguageCode.En] = "Scan complete: {0} new plugin(s) found. {1}",
			[LanguageCode.Ja] = "スキャン完了。新しいプラグインを {0} 個検出しました。{1}"
		};
		dictionary["PluginsRescanNone"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "扫描完成，没有发现新插件。{0}",
			[LanguageCode.ZhTw] = "掃描完成，沒有發現新外掛。{0}",
			[LanguageCode.En] = "Scan complete: no new plugins found. {0}",
			[LanguageCode.Ja] = "スキャン完了。新しいプラグインは見つかりませんでした。{0}"
		};
		dictionary["PluginsRescanCandidateHint"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "扫描目录里另有 {0} 个可安装项。",
			[LanguageCode.ZhTw] = "掃描目錄裡另有 {0} 個可安裝項目。",
			[LanguageCode.En] = "There are also {0} installable items in the scan folder.",
			[LanguageCode.Ja] = "スキャンフォルダーには他に {0} 件のインストール可能な項目があります。"
		};
		dictionary["PluginsDisabledNotice"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "插件系统已关闭。\n\n已经分配到轮盘上的 {0} 个插件动作会原样保留，但触发时不会执行。\n仍在运行的插件任务会收到取消信号并由宿主继续追踪。",
			[LanguageCode.ZhTw] = "外掛系統已關閉。\n\n已經分配到轉盤上的 {0} 個外掛動作會原樣保留，但觸發時不會執行。\n仍在執行的外掛工作會收到取消訊號並由宿主繼續追蹤。",
			[LanguageCode.En] = "The plugin system is now off.\n\nThe {0} plugin action(s) already assigned to your wheels are kept, but they will not run when triggered.\nPlugin tasks still running will get a cancel signal, and the host keeps tracking them.",
			[LanguageCode.Ja] = "プラグインシステムをオフにしました。\n\nホイールに割り当て済みの {0} 個のプラグイン動作はそのまま残りますが、実行されません。\n実行中のプラグイン処理にはキャンセルが通知され、ホストが引き続き追跡します。"
		};
		dictionary["PluginsConfirmDisableTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "停用插件",
			[LanguageCode.ZhTw] = "停用外掛",
			[LanguageCode.En] = "Disable plugin",
			[LanguageCode.Ja] = "プラグインを無効化"
		};
		dictionary["PluginsConfirmDisable"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "确定停用「{0}」吗？\n\n· 当前配置中有 {1} 个动作由它提供，停用期间这些动作会暂时失效\n· 配置不会丢失，重新启用即可恢复",
			[LanguageCode.ZhTw] = "確定停用「{0}」嗎？\n\n· 目前設定中有 {1} 個動作由它提供，停用期間這些動作會暫時失效\n· 設定不會遺失，重新啟用即可恢復",
			[LanguageCode.En] = "Disable plugin {0}?\n\n· {1} action(s) in the current configuration come from it and will stop working while it is disabled\n· Nothing is lost from your configuration; re-enabling restores them",
			[LanguageCode.Ja] = "プラグイン「{0}」を無効化しますか？\n\n· 現在の設定には、これが提供する動作が {1} 個あり、無効化中は使用できなくなります\n· 設定は失われません。再度有効化すれば復元します"
		};
		dictionary["PluginsEnableFailed"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "启用插件 {0} 失败：\n\n{1}",
			[LanguageCode.ZhTw] = "啟用外掛 {0} 失敗：\n\n{1}",
			[LanguageCode.En] = "Failed to enable plugin {0}:\n\n{1}",
			[LanguageCode.Ja] = "プラグイン {0} の有効化に失敗しました：\n\n{1}"
		};
		dictionary["PluginsStoppingTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "插件正在后台停止",
			[LanguageCode.ZhTw] = "外掛正在背景停止",
			[LanguageCode.En] = "Plugin is stopping in the background",
			[LanguageCode.Ja] = "プラグインはバックグラウンドで停止中"
		};
		dictionary["PluginsConfirmUninstallTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "卸载插件",
			[LanguageCode.ZhTw] = "解除安裝外掛",
			[LanguageCode.En] = "Uninstall plugin",
			[LanguageCode.Ja] = "プラグインをアンインストール"
		};
		dictionary["PluginsConfirmUninstall"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "确定要卸载插件 {0} 吗？\n\n· 插件文件与它自己的配置会被删除\n· 已经分配到轮盘上的插件动作会保留，但触发时会提示「插件不可用」\n\n此操作不可撤销。",
			[LanguageCode.ZhTw] = "確定要解除安裝外掛 {0} 嗎？\n\n· 外掛檔案與它自己的設定會被刪除\n· 已經分配到轉盤上的外掛動作會保留，但觸發時會提示「外掛無法使用」\n\n此操作無法復原。",
			[LanguageCode.En] = "Uninstall plugin {0}?\n\n· The plugin files and its own settings will be deleted\n· Plugin actions already assigned to your wheels are kept, but they will report that the plugin is unavailable when triggered\n\nThis cannot be undone.",
			[LanguageCode.Ja] = "プラグイン {0} をアンインストールしますか？\n\n· プラグインのファイルと独自の設定が削除されます\n· ホイールに割り当て済みのプラグイン動作は残りますが、実行時にプラグインが利用できない旨が表示されます\n\nこの操作は取り消せません。"
		};
		dictionary["PluginsUninstallFailed"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "卸载失败：\n\n{0}",
			[LanguageCode.ZhTw] = "解除安裝失敗：\n\n{0}",
			[LanguageCode.En] = "Uninstall failed:\n\n{0}",
			[LanguageCode.Ja] = "アンインストールに失敗しました：\n\n{0}"
		};
		dictionary["PluginsActionBrokenHint"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚠️ 原先引用的插件动作已不可用（插件可能已被停用或卸载），请重新选择。",
			[LanguageCode.ZhTw] = "⚠️ 原先引用的外掛動作已無法使用（外掛可能已被停用或解除安裝），請重新選擇。",
			[LanguageCode.En] = "⚠️ The plugin action this referred to is no longer available (the plugin may be disabled or uninstalled). Please choose another one.",
			[LanguageCode.Ja] = "⚠️ 参照していたプラグイン動作は利用できません（プラグインが無効化または削除された可能性があります）。選び直してください。"
		};
		dictionary["PluginsPanelNotChosenPick"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "尚未选定具体的插件动作。请在上方「插件动作」下拉框中选择 —— 候选动作按插件分组，同一插件的动作都归在它以自己名字命名的那个分组下。",
			[LanguageCode.ZhTw] = "尚未選定具體的外掛動作。請在上方「外掛動作」下拉選單中選擇 —— 候選動作依外掛分組，同一外掛的動作都歸在它以自己名字命名的那個分組下。",
			[LanguageCode.En] = "No plugin action selected yet. Pick one in the plugin action dropdown above — candidates are grouped by plugin, and every action of a plugin lives under the group named after it.",
			[LanguageCode.Ja] = "具体的なプラグイン動作が未選択です。上の「プラグイン動作」ドロップダウンで選択してください —— 候補はプラグインごとにまとまっており、同じプラグインの動作はその名前のグループに入っています。"
		};
		dictionary["PluginsPanelNotChosenEmpty"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "当前没有可用的插件动作。请先到「插件与扩展」页安装并启用插件，再回到这里选择。",
			[LanguageCode.ZhTw] = "目前沒有可用的外掛動作。請先到「外掛與擴充」頁安裝並啟用外掛，再回到這裡選擇。",
			[LanguageCode.En] = "No plugin actions are available. Install and enable a plugin on the plugins page first, then come back here to choose one.",
			[LanguageCode.Ja] = "利用できるプラグイン動作がありません。先に「プラグインと拡張」ページでプラグインをインストールして有効化し、ここに戻って選択してください。"
		};
		dictionary["PluginsPanelUnavailable"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "所引用的插件动作当前不可用：{0}\n可能是该插件已被停用或卸载，也可能是插件升级后移除了这个动作。\n到「插件与扩展」页确认插件状态，或直接在上方「插件动作」下拉框里改选另一个动作。",
			[LanguageCode.ZhTw] = "所引用的外掛動作目前無法使用：{0}\n可能是該外掛已被停用或解除安裝，也可能是外掛升級後移除了這個動作。\n到「外掛與擴充」頁確認外掛狀態，或直接在上方「外掛動作」下拉選單裡改選另一個動作。",
			[LanguageCode.En] = "The referenced plugin action is currently unavailable: {0}\nThe plugin may have been disabled or uninstalled, or an upgrade removed this action.\nCheck the plugin's state on the plugins page, or pick another action in the dropdown above.",
			[LanguageCode.Ja] = "参照しているプラグイン動作は現在利用できません：{0}\nプラグインが無効化／アンインストールされたか、更新でこの動作が削除された可能性があります。\n「プラグインと拡張」ページで状態を確認するか、上の「プラグイン動作」ドロップダウンで別の動作を選び直してください。"
		};
		dictionary["PluginsPanelUnavailableHint"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚠️ 触发时会明确提示「插件动作不可用」，不会静默无操作。",
			[LanguageCode.ZhTw] = "⚠️ 觸發時會明確提示「外掛動作無法使用」，不會靜默無操作。",
			[LanguageCode.En] = "⚠️ Triggering it reports \"plugin action unavailable\" — it will not silently do nothing.",
			[LanguageCode.Ja] = "⚠️ 実行時は「プラグイン動作を利用できません」と明示されます。無言で何も起きることはありません。"
		};
		dictionary["PluginsPanelProvider"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "提供插件：{0}",
			[LanguageCode.ZhTw] = "提供外掛：{0}",
			[LanguageCode.En] = "Plugin: {0}",
			[LanguageCode.Ja] = "提供プラグイン：{0}"
		};
		dictionary["PluginsPanelProviderWithId"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "提供插件：{0}（{1}）",
			[LanguageCode.ZhTw] = "提供外掛：{0}（{1}）",
			[LanguageCode.En] = "Plugin: {0} ({1})",
			[LanguageCode.Ja] = "提供プラグイン：{0}（{1}）"
		};
		dictionary["PluginsPanelExecutionMode"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "执行方式：{0}",
			[LanguageCode.ZhTw] = "執行方式：{0}",
			[LanguageCode.En] = "Runs: {0}",
			[LanguageCode.Ja] = "実行方式：{0}"
		};
		dictionary["PluginsPanelKindBackground"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "后台并发（不占用动作线程）",
			[LanguageCode.ZhTw] = "背景並行（不佔用動作執行緒）",
			[LanguageCode.En] = "background, concurrent (does not hold the action thread)",
			[LanguageCode.Ja] = "バックグラウンド並行（動作スレッドを占有しません）"
		};
		dictionary["PluginsPanelKindSerial"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "串行（占用动作线程）",
			[LanguageCode.ZhTw] = "序列（佔用動作執行緒）",
			[LanguageCode.En] = "serial (holds the action thread)",
			[LanguageCode.Ja] = "直列（動作スレッドを占有します）"
		};
		dictionary["PluginsPanelTimeout"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "超时：{0} 秒",
			[LanguageCode.ZhTw] = "逾時：{0} 秒",
			[LanguageCode.En] = "Timeout: {0} s",
			[LanguageCode.Ja] = "タイムアウト：{0} 秒"
		};
		dictionary["PluginsPanelContributionId"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "贡献点 ID：{0}",
			[LanguageCode.ZhTw] = "貢獻點 ID：{0}",
			[LanguageCode.En] = "Contribution ID: {0}",
			[LanguageCode.Ja] = "提供ポイント ID：{0}"
		};
		dictionary["PluginsPanelRequiredParams"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "此动作有 {0} 个必填参数，留空会在触发时被拦下。",
			[LanguageCode.ZhTw] = "此動作有 {0} 個必填參數，留空會在觸發時被攔下。",
			[LanguageCode.En] = "This action has {0} required parameter(s); leaving them empty blocks the trigger.",
			[LanguageCode.Ja] = "この動作には必須パラメーターが {0} 個あります。空欄のまま実行するとブロックされます。"
		};
		dictionary["PluginsPanelAllOptional"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "此动作的参数全部可选。",
			[LanguageCode.ZhTw] = "此動作的參數全部可選。",
			[LanguageCode.En] = "All parameters of this action are optional.",
			[LanguageCode.Ja] = "この動作のパラメーターはすべて任意です。"
		};
		dictionary["PluginsPanelIssuesCount"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "还有 {0} 个参数不合法，触发时会被拦下。",
			[LanguageCode.ZhTw] = "還有 {0} 個參數不合法，觸發時會被攔下。",
			[LanguageCode.En] = "{0} parameter(s) are still invalid; the trigger will be blocked.",
			[LanguageCode.Ja] = "まだ {0} 個のパラメーターが不正です。実行時にブロックされます。"
		};
		dictionary["PluginCandidateAuthor"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "作者 {0}",
			[LanguageCode.ZhTw] = "作者 {0}",
			[LanguageCode.En] = "by {0}",
			[LanguageCode.Ja] = "作者 {0}"
		};
		dictionary["PluginCandidateCapabilities"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "声明能力：{0}",
			[LanguageCode.ZhTw] = "宣告能力：{0}",
			[LanguageCode.En] = "Capabilities: {0}",
			[LanguageCode.Ja] = "宣言機能：{0}"
		};
		dictionary["PluginCandidateStateInstallable"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "可安装",
			[LanguageCode.ZhTw] = "可安裝",
			[LanguageCode.En] = "Installable",
			[LanguageCode.Ja] = "インストール可能"
		};
		dictionary["PluginCandidateStateInstalled"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "已装同版本",
			[LanguageCode.ZhTw] = "已裝同版本",
			[LanguageCode.En] = "Same version installed",
			[LanguageCode.Ja] = "同じバージョンを導入済み"
		};
		dictionary["PluginCandidateStateReplaced"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "内容已变",
			[LanguageCode.ZhTw] = "內容已變",
			[LanguageCode.En] = "Content changed",
			[LanguageCode.Ja] = "内容が変更されています"
		};
		dictionary["PluginCandidateStateUpdate"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "有新版本",
			[LanguageCode.ZhTw] = "有新版本",
			[LanguageCode.En] = "Update available",
			[LanguageCode.Ja] = "新しいバージョンあり"
		};
		dictionary["PluginCandidateStateDowngrade"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "版本更旧",
			[LanguageCode.ZhTw] = "版本更舊",
			[LanguageCode.En] = "Older version",
			[LanguageCode.Ja] = "古いバージョン"
		};
		dictionary["PluginCandidateStateVersionUnknown"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "版本待确认",
			[LanguageCode.ZhTw] = "版本待確認",
			[LanguageCode.En] = "Version unknown",
			[LanguageCode.Ja] = "バージョン未確認"
		};
		dictionary["PluginCandidateStateExternalRegistered"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "已外部引用",
			[LanguageCode.ZhTw] = "已外部引用",
			[LanguageCode.En] = "Externally registered",
			[LanguageCode.Ja] = "外部参照済み"
		};
		dictionary["PluginCandidateStateReserved"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "官方模块",
			[LanguageCode.ZhTw] = "官方模組",
			[LanguageCode.En] = "Official module",
			[LanguageCode.Ja] = "公式モジュール"
		};
		dictionary["PluginCandidateStateDuplicate"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "ID 重复",
			[LanguageCode.ZhTw] = "ID 重複",
			[LanguageCode.En] = "Duplicate ID",
			[LanguageCode.Ja] = "ID 重複"
		};
		dictionary["PluginCandidateStateRejected"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "无法识别",
			[LanguageCode.ZhTw] = "無法識別",
			[LanguageCode.En] = "Unrecognized",
			[LanguageCode.Ja] = "認識できません"
		};
		dictionary["PluginCandidateNoteRejected"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "无法安装：{0}",
			[LanguageCode.ZhTw] = "無法安裝：{0}",
			[LanguageCode.En] = "Cannot install: {0}",
			[LanguageCode.Ja] = "インストールできません：{0}"
		};
		dictionary["PluginCandidateNoteDuplicate"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "扫描目录里有 {0} 枚 .dll 声明了同一个 ID（{1}），无法判断该装哪一枚。请只保留需要的那一个文件。",
			[LanguageCode.ZhTw] = "掃描目錄裡有 {0} 枚 .dll 宣告了同一個 ID（{1}），無法判斷該裝哪一枚。請只保留需要的那一個檔案。",
			[LanguageCode.En] = "{0} .dll files in the scan folder declare the same ID ({1}), so there is no way to tell which one to install. Keep only the file you need.",
			[LanguageCode.Ja] = "スキャンフォルダー内の {0} 個の .dll が同じ ID（{1}）を宣言しているため、どれをインストールすべきか判断できません。必要なファイルだけを残してください。"
		};
		dictionary["PluginCandidateNoteReserved"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "这是官方模块（{0}）。扫描目录只用于手动安装社区插件 —— 官方模块请到上方「官方插件」列表里下载和更新，宿主不会从这里安装它。",
			[LanguageCode.ZhTw] = "這是官方模組（{0}）。掃描目錄只用於手動安裝社群外掛 —— 官方模組請到上方「官方外掛」清單裡下載和更新，宿主不會從這裡安裝它。",
			[LanguageCode.En] = "This is an official module ({0}). The scan folder is only for installing community plugins by hand — download and update official modules from the \"Official plugins\" list above; StarPie will not install it from here.",
			[LanguageCode.Ja] = "これは公式モジュール（{0}）です。スキャンフォルダーはコミュニティプラグインを手動でインストールするためのもので、公式モジュールは上の「公式プラグイン」一覧からダウンロード・更新してください。ここからはインストールされません。"
		};
		dictionary["PluginCandidateNoteInstallable"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "尚未安装，可直接安装。",
			[LanguageCode.ZhTw] = "尚未安裝，可直接安裝。",
			[LanguageCode.En] = "Not installed yet — you can install it directly.",
			[LanguageCode.Ja] = "まだインストールされていません。そのままインストールできます。"
		};
		dictionary["PluginCandidateNoteExternalRegistered"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "同一个 ID 已被开发者模式的外部路径登记占用：{0}。如需改为安装副本，请先在列表里卸载那条登记。",
			[LanguageCode.ZhTw] = "同一個 ID 已被開發者模式的外部路徑登記占用：{0}。如需改為安裝副本，請先在清單裡解除那條登記。",
			[LanguageCode.En] = "The same ID is already claimed by a developer-mode external path registration: {0}. To switch to an installed copy, unregister it in the list first.",
			[LanguageCode.Ja] = "同じ ID は既に開発者モードの外部パス登録（{0}）が使用しています。インストール済みのコピーに切り替える場合は、先に一覧からその登録を解除してください。"
		};
		dictionary["PluginCandidateNoteInstalled"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "已装同一个版本（v{0}），无需重复安装。",
			[LanguageCode.ZhTw] = "已裝同一個版本（v{0}），無需重複安裝。",
			[LanguageCode.En] = "Version v{0} is already installed — no need to install it again.",
			[LanguageCode.Ja] = "同じバージョン（v{0}）が既にインストールされています。再インストールは不要です。"
		};
		dictionary["PluginCandidateNoteReplaced"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "已装的 v{0} 与这枚文件版本号相同但内容不同（哈希不一致）。覆盖安装会用它替换现有文件。",
			[LanguageCode.ZhTw] = "已裝的 v{0} 與這枚檔案版本號相同但內容不同（雜湊不一致）。覆蓋安裝會用它取代現有檔案。",
			[LanguageCode.En] = "The installed v{0} and this file share the same version number but differ in content (hash mismatch). Overwriting will replace the existing file with this one.",
			[LanguageCode.Ja] = "インストール済みの v{0} とこのファイルはバージョンが同じで内容が異なります（ハッシュ不一致）。上書きインストールするとこのファイルに置き換わります。"
		};
		dictionary["PluginCandidateNoteUpdate"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "已装 v{0}，这枚是更新的 v{1}。",
			[LanguageCode.ZhTw] = "已裝 v{0}，這枚是更新的 v{1}。",
			[LanguageCode.En] = "v{0} is installed; this file is the newer v{1}.",
			[LanguageCode.Ja] = "v{0} がインストール済みで、これは新しい v{1} です。"
		};
		dictionary["PluginCandidateNoteDowngrade"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "已装 v{0}，这枚是更旧的 v{1}。一般不建议降级。",
			[LanguageCode.ZhTw] = "已裝 v{0}，這枚是更舊的 v{1}。一般不建議降級。",
			[LanguageCode.En] = "v{0} is installed; this file is the older v{1}. Downgrading is usually not recommended.",
			[LanguageCode.Ja] = "v{0} がインストール済みで、これは古い v{1} です。通常、ダウングレードは推奨しません。"
		};
		dictionary["PluginCandidateNoteVersionUnknown"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "已装版本「{0}」与候选版本「{1}」至少有一侧解析不了，无法比较新旧。",
			[LanguageCode.ZhTw] = "已裝版本「{0}」與候選版本「{1}」至少有一側無法解析，無法比較新舊。",
			[LanguageCode.En] = "At least one of the installed version \"{0}\" or the candidate version \"{1}\" cannot be parsed, so the two cannot be compared.",
			[LanguageCode.Ja] = "インストール済みバージョン「{0}」と候補バージョン「{1}」の少なくとも一方を解析できないため、新旧を比較できません。"
		};
		dictionary["PluginCandidateNoteSameContent"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "内容与已装的一致。",
			[LanguageCode.ZhTw] = "內容與已裝的一致。",
			[LanguageCode.En] = "The content matches the installed version.",
			[LanguageCode.Ja] = "内容はインストール済みのものと一致します。"
		};
		dictionary["PluginCandidateNoteDifferentContent"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "内容与已装的不同。",
			[LanguageCode.ZhTw] = "內容與已裝的不同。",
			[LanguageCode.En] = "The content differs from the installed version.",
			[LanguageCode.Ja] = "内容はインストール済みのものと異なります。"
		};
		// ---- 识别失败：原因标题（PluginScanFailureText.Title） ----
		// 键名后缀 = PluginScanFailure 枚举成员名，一一对应。Title/Hint 用的是**穷尽 switch 表达式**
		// （没有 `_` 兜底分支），所以将来往枚举里加一项而忘了加词条，编译器会直接 CS8509 报出来。
		dictionary["PluginScanFailureTitleNone"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "正常",
			[LanguageCode.ZhTw] = "正常",
			[LanguageCode.En] = "Normal",
			[LanguageCode.Ja] = "正常"
		};
		dictionary["PluginScanFailureTitleIdNotDeclared"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "未找到插件标识",
			[LanguageCode.ZhTw] = "找不到外掛識別碼",
			[LanguageCode.En] = "No plugin ID found",
			[LanguageCode.Ja] = "プラグイン識別子が見つかりません"
		};
		dictionary["PluginScanFailureTitleManifestInvalid"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "plugin.json 格式不正确",
			[LanguageCode.ZhTw] = "plugin.json 格式不正確",
			[LanguageCode.En] = "plugin.json is malformed",
			[LanguageCode.Ja] = "plugin.json の形式が正しくありません"
		};
		dictionary["PluginScanFailureTitleInvalidIdFormat"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "插件 ID 格式非法",
			[LanguageCode.ZhTw] = "外掛 ID 格式不合法",
			[LanguageCode.En] = "Invalid plugin ID format",
			[LanguageCode.Ja] = "プラグイン ID の形式が不正です"
		};
		dictionary["PluginScanFailureTitleReservedIdPrefix"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "插件 ID 使用了保留前缀",
			[LanguageCode.ZhTw] = "外掛 ID 使用了保留前綴",
			[LanguageCode.En] = "Plugin ID uses a reserved prefix",
			[LanguageCode.Ja] = "プラグイン ID が予約済みプレフィックスを使用しています"
		};
		dictionary["PluginScanFailureTitleDllNotFound"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "找不到插件程序集",
			[LanguageCode.ZhTw] = "找不到外掛組件",
			[LanguageCode.En] = "Plugin assembly not found",
			[LanguageCode.Ja] = "プラグインアセンブリが見つかりません"
		};
		dictionary["PluginScanFailureTitleNotDotNetAssembly"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "不是 .NET 程序集",
			[LanguageCode.ZhTw] = "不是 .NET 組件",
			[LanguageCode.En] = "Not a .NET assembly",
			[LanguageCode.Ja] = ".NET アセンブリではありません"
		};
		dictionary["PluginScanFailureTitleNotIlOnly"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "程序集含本机代码",
			[LanguageCode.ZhTw] = "組件含原生程式碼",
			[LanguageCode.En] = "Assembly contains native code",
			[LanguageCode.Ja] = "アセンブリにネイティブコードが含まれています"
		};
		dictionary["PluginScanFailureTitleWrongArchitecture"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "架构不匹配（需要 64 位）",
			[LanguageCode.ZhTw] = "架構不符（需要 64 位元）",
			[LanguageCode.En] = "Wrong architecture (64-bit required)",
			[LanguageCode.Ja] = "アーキテクチャが一致しません（64 ビットが必要）"
		};
		dictionary["PluginScanFailureTitleTargetFrameworkMismatch"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "目标框架不兼容",
			[LanguageCode.ZhTw] = "目標框架不相容",
			[LanguageCode.En] = "Incompatible target framework",
			[LanguageCode.Ja] = "ターゲットフレームワークが非互換です"
		};
		dictionary["PluginScanFailureTitleNoContractImplementation"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "不是 StarPie 插件",
			[LanguageCode.ZhTw] = "不是 StarPie 外掛",
			[LanguageCode.En] = "Not a StarPie plugin",
			[LanguageCode.Ja] = "StarPie プラグインではありません"
		};
		dictionary["PluginScanFailureTitleAmbiguousContractImplementation"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "入口类型不唯一",
			[LanguageCode.ZhTw] = "進入點類型不唯一",
			[LanguageCode.En] = "Ambiguous entry type",
			[LanguageCode.Ja] = "エントリ型が一意に定まりません"
		};
		dictionary["PluginScanFailureTitleEntryTypeNotFound"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "清单声明的入口类型不存在",
			[LanguageCode.ZhTw] = "清單宣告的進入點類型不存在",
			[LanguageCode.En] = "Declared entry type does not exist",
			[LanguageCode.Ja] = "マニフェストで宣言されたエントリ型が存在しません"
		};
		dictionary["PluginScanFailureTitleApiVersionMismatch"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "插件 SDK 契约版本不兼容",
			[LanguageCode.ZhTw] = "外掛 SDK 契約版本不相容",
			[LanguageCode.En] = "Incompatible plugin SDK version",
			[LanguageCode.Ja] = "プラグイン SDK の契約バージョンが非互換です"
		};
		dictionary["PluginScanFailureTitleContractAssemblyVersionMismatch"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "SDK 程序集版本身份不一致",
			[LanguageCode.ZhTw] = "SDK 組件版本身分不一致",
			[LanguageCode.En] = "SDK assembly version identity mismatch",
			[LanguageCode.Ja] = "SDK アセンブリのバージョン同一性が一致しません"
		};
		dictionary["PluginScanFailureTitleSha256Mismatch"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "文件已损坏或被修改",
			[LanguageCode.ZhTw] = "檔案已損毀或被修改",
			[LanguageCode.En] = "File is corrupted or modified",
			[LanguageCode.Ja] = "ファイルが破損または改変されています"
		};
		dictionary["PluginScanFailureTitleHostVersionOutOfRange"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "宿主版本超出插件声明区间",
			[LanguageCode.ZhTw] = "宿主版本超出外掛宣告區間",
			[LanguageCode.En] = "Host version outside the declared range",
			[LanguageCode.Ja] = "ホストのバージョンが宣言範囲外です"
		};
		dictionary["PluginScanFailureTitleDependencyMissing"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "缺少依赖插件",
			[LanguageCode.ZhTw] = "缺少相依外掛",
			[LanguageCode.En] = "Missing dependency plugin",
			[LanguageCode.Ja] = "依存プラグインが不足しています"
		};
		dictionary["PluginScanFailureTitleDependencyCycle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "插件依赖存在环",
			[LanguageCode.ZhTw] = "外掛相依存在環",
			[LanguageCode.En] = "Cyclic plugin dependency",
			[LanguageCode.Ja] = "プラグインの依存関係に循環があります"
		};
		dictionary["PluginScanFailureSeparator"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "：",
			[LanguageCode.ZhTw] = "：",
			[LanguageCode.En] = ": ",
			[LanguageCode.Ja] = "："
		};
		// ---- 识别失败：修复建议（PluginScanFailureText.Hint） ----
		dictionary["PluginScanFailureHintNone"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "识别已通过，无需修复。",
			[LanguageCode.ZhTw] = "識別已通過，無需修復。",
			[LanguageCode.En] = "The scan passed — nothing to fix.",
			[LanguageCode.Ja] = "スキャンは通過しました。修正の必要はありません。"
		};
		dictionary["PluginScanFailureHintIdNotDeclared"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "这个 .dll 既没有同级的 plugin.json，也没有在程序集里声明 StarPiePluginId 元数据。让作者按文档在 csproj 里补上 AssemblyMetadata 是推荐做法（分发时只需一枚 .dll）；带 plugin.json 的完整插件包同样可以安装。",
			[LanguageCode.ZhTw] = "這個 .dll 既沒有同層的 plugin.json，也沒有在組件裡宣告 StarPiePluginId 中繼資料。請作者依文件在 csproj 裡補上 AssemblyMetadata 是推薦做法（散佈時只需一枚 .dll）；附帶 plugin.json 的完整外掛包同樣可以安裝。",
			[LanguageCode.En] = "This .dll has no plugin.json beside it and declares no StarPiePluginId assembly metadata. The recommended fix is for the author to add AssemblyMetadata in the csproj (so only one .dll needs to ship); a full plugin package with plugin.json works just as well.",
			[LanguageCode.Ja] = "この .dll には同じ階層の plugin.json も、アセンブリ内の StarPiePluginId メタデータもありません。作者がドキュメントに沿って csproj に AssemblyMetadata を追加するのが推奨です（配布時は .dll 1 枚で済みます）。plugin.json を含む完全なプラグインパッケージでもインストールできます。"
		};
		dictionary["PluginScanFailureHintManifestInvalid"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "请检查 plugin.json 的字段名与类型是否与规范一致（可对照 plugin.schema.json）。",
			[LanguageCode.ZhTw] = "請檢查 plugin.json 的欄位名稱與型別是否與規範一致（可對照 plugin.schema.json）。",
			[LanguageCode.En] = "Check that the field names and types in plugin.json match the spec (compare against plugin.schema.json).",
			[LanguageCode.Ja] = "plugin.json のフィールド名と型が仕様どおりか確認してください（plugin.schema.json と照合できます）。"
		};
		dictionary["PluginScanFailureHintInvalidIdFormat"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "插件 ID 需要是反向域名风格，全小写，例如 com.example.mytool。",
			[LanguageCode.ZhTw] = "外掛 ID 需要是反向網域風格，全小寫，例如 com.example.mytool。",
			[LanguageCode.En] = "A plugin ID must be reverse-DNS style, all lowercase, for example com.example.mytool.",
			[LanguageCode.Ja] = "プラグイン ID は逆ドメイン形式のすべて小文字にしてください（例：com.example.mytool）。"
		};
		dictionary["PluginScanFailureHintReservedIdPrefix"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "starpie / windows / microsoft / system / builtin 前缀保留给官方，请换一个前缀。",
			[LanguageCode.ZhTw] = "starpie / windows / microsoft / system / builtin 前綴保留給官方，請換一個前綴。",
			[LanguageCode.En] = "The starpie / windows / microsoft / system / builtin prefixes are reserved for official modules. Please pick a different prefix.",
			[LanguageCode.Ja] = "starpie / windows / microsoft / system / builtin の各プレフィックスは公式用に予約されています。別のプレフィックスを使用してください。"
		};
		dictionary["PluginScanFailureHintDllNotFound"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "清单里声明的程序集文件不在插件目录中，请确认打包时没有漏掉 .dll。",
			[LanguageCode.ZhTw] = "清單裡宣告的組件檔案不在外掛目錄中，請確認封裝時沒有漏掉 .dll。",
			[LanguageCode.En] = "The assembly declared in the manifest is not in the plugin folder. Make sure the .dll was not left out when packaging.",
			[LanguageCode.Ja] = "マニフェストで宣言されたアセンブリがプラグインフォルダーにありません。パッケージ作成時に .dll を入れ忘れていないか確認してください。"
		};
		dictionary["PluginScanFailureHintNotDotNetAssembly"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "这是一枚原生 C++ DLL 或非托管库，StarPie 插件必须是 .NET 程序集。你可能选错了文件。",
			[LanguageCode.ZhTw] = "這是一枚原生 C++ DLL 或非受控程式庫，StarPie 外掛必須是 .NET 組件。你可能選錯了檔案。",
			[LanguageCode.En] = "This is a native C++ DLL or an unmanaged library. A StarPie plugin must be a .NET assembly — you may have picked the wrong file.",
			[LanguageCode.Ja] = "これはネイティブ C++ DLL またはアンマネージドライブラリです。StarPie プラグインは .NET アセンブリである必要があります。ファイルの選択を誤っている可能性があります。"
		};
		dictionary["PluginScanFailureHintNotIlOnly"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "程序集混合了本机代码（C++/CLI）。StarPie 只接受纯托管（ILOnly）程序集。",
			[LanguageCode.ZhTw] = "組件混合了原生程式碼（C++/CLI）。StarPie 只接受純受控（ILOnly）組件。",
			[LanguageCode.En] = "The assembly mixes in native code (C++/CLI). StarPie only accepts purely managed (ILOnly) assemblies.",
			[LanguageCode.Ja] = "アセンブリにネイティブコード（C++/CLI）が混在しています。StarPie は純粋なマネージド（ILOnly）アセンブリのみを受け付けます。"
		};
		dictionary["PluginScanFailureHintWrongArchitecture"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "程序集被编译为仅 32 位（Requires32Bit）。请把插件的平台目标改为 x64 或 AnyCPU 后重新发布。",
			[LanguageCode.ZhTw] = "組件被編譯為僅 32 位元（Requires32Bit）。請把外掛的平台目標改為 x64 或 AnyCPU 後重新發佈。",
			[LanguageCode.En] = "The assembly is compiled as 32-bit only (Requires32Bit). Change the plugin's platform target to x64 or AnyCPU and rebuild.",
			[LanguageCode.Ja] = "アセンブリが 32 ビット専用（Requires32Bit）でコンパイルされています。プラグインのプラットフォームターゲットを x64 または AnyCPU に変更して再発行してください。"
		};
		dictionary["PluginScanFailureHintTargetFrameworkMismatch"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "插件的目标框架高于当前 StarPie。请升级 StarPie，或联系作者改用更低的 net8.0-windows 目标。",
			[LanguageCode.ZhTw] = "外掛的目標框架高於目前的 StarPie。請升級 StarPie，或聯絡作者改用較低的 net8.0-windows 目標。",
			[LanguageCode.En] = "The plugin targets a newer framework than this StarPie build. Update StarPie, or ask the author to target net8.0-windows or lower.",
			[LanguageCode.Ja] = "プラグインのターゲットフレームワークが現在の StarPie より新しいものです。StarPie を更新するか、作者に net8.0-windows 以下へ下げてもらってください。"
		};
		dictionary["PluginScanFailureHintNoContractImplementation"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "程序集里找不到 IStarPiePlugin 的实现类，说明它不是一个 StarPie 插件。",
			[LanguageCode.ZhTw] = "組件裡找不到 IStarPiePlugin 的實作類別，說明它不是一個 StarPie 外掛。",
			[LanguageCode.En] = "The assembly contains no IStarPiePlugin implementation, so it is not a StarPie plugin.",
			[LanguageCode.Ja] = "アセンブリ内に IStarPiePlugin の実装クラスが見つかりません。StarPie プラグインではありません。"
		};
		dictionary["PluginScanFailureHintAmbiguousContractImplementation"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "程序集里有多个 IStarPiePlugin 实现。请在 plugin.json 的 entryType 里明确指定入口类全名。",
			[LanguageCode.ZhTw] = "組件裡有多個 IStarPiePlugin 實作。請在 plugin.json 的 entryType 裡明確指定進入點類別全名。",
			[LanguageCode.En] = "The assembly has several IStarPiePlugin implementations. Name the entry class explicitly in plugin.json's entryType.",
			[LanguageCode.Ja] = "アセンブリ内に IStarPiePlugin の実装が複数あります。plugin.json の entryType でエントリクラスの完全名を指定してください。"
		};
		dictionary["PluginScanFailureHintEntryTypeNotFound"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "plugin.json 里 entryType 写的类型名在程序集中不存在，请核对命名空间与类型名拼写。",
			[LanguageCode.ZhTw] = "plugin.json 裡 entryType 寫的型別名稱在組件中不存在，請核對命名空間與型別名稱拼寫。",
			[LanguageCode.En] = "The type named in plugin.json's entryType does not exist in the assembly. Check the namespace and type name spelling.",
			[LanguageCode.Ja] = "plugin.json の entryType に書かれた型名がアセンブリ内に存在しません。名前空間と型名の綴りを確認してください。"
		};
		dictionary["PluginScanFailureHintApiVersionMismatch"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "插件编译时使用的 SDK 契约主版本与当前 StarPie 不一致。请更新插件，或升级 StarPie。",
			[LanguageCode.ZhTw] = "外掛編譯時使用的 SDK 契約主版本與目前 StarPie 不一致。請更新外掛，或升級 StarPie。",
			[LanguageCode.En] = "The plugin was built against a different SDK contract major version than this StarPie. Update the plugin, or update StarPie.",
			[LanguageCode.Ja] = "プラグインがビルド時に使用した SDK 契約のメジャーバージョンが現在の StarPie と一致しません。プラグインを更新するか、StarPie を更新してください。"
		};
		dictionary["PluginScanFailureHintContractAssemblyVersionMismatch"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "插件自带了 StarPie.Plugin.Abstractions.dll 且版本与宿主不一致。请删除插件目录里的这个文件，它会由 StarPie 统一提供。",
			[LanguageCode.ZhTw] = "外掛自帶了 StarPie.Plugin.Abstractions.dll 且版本與宿主不一致。請刪除外掛目錄裡的這個檔案，它會由 StarPie 統一提供。",
			[LanguageCode.En] = "The plugin ships its own StarPie.Plugin.Abstractions.dll whose version differs from the host's. Delete that file from the plugin folder — StarPie provides it centrally.",
			[LanguageCode.Ja] = "プラグインが独自に StarPie.Plugin.Abstractions.dll を同梱しており、バージョンがホストと一致しません。プラグインフォルダーからこのファイルを削除してください。StarPie が一元提供します。"
		};
		dictionary["PluginScanFailureHintSha256Mismatch"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "文件内容与清单声明的哈希不一致，可能下载不完整或被第三方修改过。请从官方渠道重新获取。",
			[LanguageCode.ZhTw] = "檔案內容與清單宣告的雜湊不一致，可能下載不完整或被第三方修改過。請從官方管道重新取得。",
			[LanguageCode.En] = "The file content does not match the hash declared in the manifest — the download may be incomplete or the file modified by a third party. Get it again from the official source.",
			[LanguageCode.Ja] = "ファイルの内容がマニフェストで宣言されたハッシュと一致しません。ダウンロードが不完全か、第三者によって改変された可能性があります。公式の配布元から再取得してください。"
		};
		dictionary["PluginScanFailureHintHostVersionOutOfRange"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "当前 StarPie 版本不在插件声明的可运行区间内。请升级 StarPie，或联系作者放宽版本区间。",
			[LanguageCode.ZhTw] = "目前 StarPie 版本不在外掛宣告的可執行區間內。請升級 StarPie，或聯絡作者放寬版本區間。",
			[LanguageCode.En] = "This StarPie version is outside the range the plugin declares it runs on. Update StarPie, or ask the author to widen the range.",
			[LanguageCode.Ja] = "現在の StarPie のバージョンが、プラグインが宣言した動作可能範囲に含まれていません。StarPie を更新するか、作者に範囲の拡大を依頼してください。"
		};
		dictionary["PluginScanFailureHintDependencyMissing"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "插件依赖的另一个插件没有安装或未启用。请先安装并启用依赖项。",
			[LanguageCode.ZhTw] = "外掛相依的另一個外掛沒有安裝或未啟用。請先安裝並啟用相依項目。",
			[LanguageCode.En] = "Another plugin this one depends on is not installed or not enabled. Install and enable the dependency first.",
			[LanguageCode.Ja] = "このプラグインが依存する別のプラグインがインストールされていないか、有効になっていません。先に依存プラグインをインストールして有効にしてください。"
		};
		dictionary["PluginScanFailureHintDependencyCycle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "插件之间形成了循环依赖，无法确定加载顺序。请联系作者修复依赖声明。",
			[LanguageCode.ZhTw] = "外掛之間形成了循環相依，無法確定載入順序。請聯絡作者修復相依宣告。",
			[LanguageCode.En] = "The plugins depend on each other in a cycle, so the load order cannot be determined. Ask the author to fix the dependency declarations.",
			[LanguageCode.Ja] = "プラグイン間に循環依存があり、読み込み順を決定できません。作者に依存関係の宣言を修正してもらってください。"
		};
		dictionary["PluginCandidateInstall"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "📦 安装",
			[LanguageCode.ZhTw] = "📦 安裝",
			[LanguageCode.En] = "📦 Install",
			[LanguageCode.Ja] = "📦 インストール"
		};
		dictionary["PluginCandidateInstallUpdate"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⬆️ 更新",
			[LanguageCode.ZhTw] = "⬆️ 更新",
			[LanguageCode.En] = "⬆️ Update",
			[LanguageCode.Ja] = "⬆️ 更新"
		};
		dictionary["PluginCandidateInstallDowngrade"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⬇️ 降级安装",
			[LanguageCode.ZhTw] = "⬇️ 降級安裝",
			[LanguageCode.En] = "⬇️ Downgrade",
			[LanguageCode.Ja] = "⬇️ ダウングレード"
		};
		dictionary["PluginCandidateInstallOverwrite"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "📦 覆盖安装",
			[LanguageCode.ZhTw] = "📦 覆蓋安裝",
			[LanguageCode.En] = "📦 Overwrite",
			[LanguageCode.Ja] = "📦 上書きインストール"
		};
		dictionary["AboutCheckUpdate"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔄 检查更新",
			[LanguageCode.ZhTw] = "🔄 檢查更新",
			[LanguageCode.En] = "🔄 Check for updates",
			[LanguageCode.Ja] = "🔄 更新を確認"
		};
		dictionary["AddGestureMapping"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "➕ 添加手势映射",
			[LanguageCode.ZhTw] = "➕ 新增手勢對應",
			[LanguageCode.En] = "➕ Add mapping",
			[LanguageCode.Ja] = "➕ マッピングを追加"
		};
		dictionary["AddLayer"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "➕ 加层",
			[LanguageCode.ZhTw] = "➕ 新增圖層",
			[LanguageCode.En] = "➕ Add layer",
			[LanguageCode.Ja] = "➕ レイヤーを追加"
		};
		dictionary["AddProfileShort"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "➕ 新增",
			[LanguageCode.ZhTw] = "➕ 新增",
			[LanguageCode.En] = "➕ New",
			[LanguageCode.Ja] = "➕ 新規"
		};
		dictionary["AnimSpeedCustom"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🎛️ 自定义速度",
			[LanguageCode.ZhTw] = "🎛️ 自訂速度",
			[LanguageCode.En] = "🎛️ Custom speed",
			[LanguageCode.Ja] = "🎛️ カスタム速度"
		};
		dictionary["ApplyRestartUpdate"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🚀 立即退出并重启更新",
			[LanguageCode.ZhTw] = "🚀 立即結束並重新啟動更新",
			[LanguageCode.En] = "🚀 Exit and restart to update",
			[LanguageCode.Ja] = "🚀 終了して再起動し更新"
		};
		dictionary["BatchLayoutBoth"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🖼️+🔤 图文",
			[LanguageCode.ZhTw] = "🖼️+🔤 圖文",
			[LanguageCode.En] = "🖼️+🔤 Icon + text",
			[LanguageCode.Ja] = "🖼️+🔤 アイコン＋文字"
		};
		dictionary["BatchLayoutIconOnly"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🖼️ 仅图标",
			[LanguageCode.ZhTw] = "🖼️ 僅圖示",
			[LanguageCode.En] = "🖼️ Icon only",
			[LanguageCode.Ja] = "🖼️ アイコンのみ"
		};
		dictionary["BatchLayoutInherit"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🌐 继承全局",
			[LanguageCode.ZhTw] = "🌐 繼承全域",
			[LanguageCode.En] = "🌐 Inherit global",
			[LanguageCode.Ja] = "🌐 グローバルを継承"
		};
		dictionary["BatchLayoutTextOnly"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔤 仅文字",
			[LanguageCode.ZhTw] = "🔤 僅文字",
			[LanguageCode.En] = "🔤 Text only",
			[LanguageCode.Ja] = "🔤 文字のみ"
		};
		dictionary["BatchResetCustom"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔄 清除自定义，恢复跟随全局统一",
			[LanguageCode.ZhTw] = "🔄 清除自訂，恢復跟隨全域統一",
			[LanguageCode.En] = "🔄 Clear customisations, follow global",
			[LanguageCode.Ja] = "🔄 カスタムを消去しグローバルに従う"
		};
		dictionary["BrowseCoreImage"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "浏览图片...",
			[LanguageCode.ZhTw] = "瀏覽圖片...",
			[LanguageCode.En] = "Browse image...",
			[LanguageCode.Ja] = "画像を参照…"
		};
		dictionary["CancelActionStatusHint"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "💡 外甩脱离轮盘时，立即执行此自定义动作；回到轮盘中心仍为静默关闭。",
			[LanguageCode.ZhTw] = "💡 外甩脫離輪盤時，立即執行此自訂動作；回到輪盤中心仍為靜默關閉。",
			[LanguageCode.En] = "💡 Flicking away from the wheel runs this custom action right away; returning to the centre still cancels silently.",
			[LanguageCode.Ja] = "💡 外へ振り切るとこのカスタム動作を即実行します。中心に戻した場合は従来どおり何もせず閉じます。"
		};
		dictionary["CancelDownload"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "✖ 取消下载",
			[LanguageCode.ZhTw] = "✖ 取消下載",
			[LanguageCode.En] = "✖ Cancel download",
			[LanguageCode.Ja] = "✖ ダウンロードを中止"
		};
		dictionary["CenterInfoToggle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "ℹ️ 说明 ▾",
			[LanguageCode.ZhTw] = "ℹ️ 說明 ▾",
			[LanguageCode.En] = "ℹ️ Help ▾",
			[LanguageCode.Ja] = "ℹ️ 説明 ▾"
		};
		dictionary["CenterPresetsToggle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚡ 常用预设 ▾",
			[LanguageCode.ZhTw] = "⚡ 常用預設 ▾",
			[LanguageCode.En] = "⚡ Presets ▾",
			[LanguageCode.Ja] = "⚡ よく使うプリセット ▾"
		};
		dictionary["ClearCoreImage"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "清除",
			[LanguageCode.ZhTw] = "清除",
			[LanguageCode.En] = "Clear",
			[LanguageCode.Ja] = "クリア"
		};
		dictionary["CopyLayer"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "📑 复制",
			[LanguageCode.ZhTw] = "📑 複製",
			[LanguageCode.En] = "📑 Copy",
			[LanguageCode.Ja] = "📑 複製"
		};
		dictionary["CoreSectionTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "中心核心圆与图案文字设置",
			[LanguageCode.ZhTw] = "中心核心圓與圖案文字設定",
			[LanguageCode.En] = "Centre core, pattern and text",
			[LanguageCode.Ja] = "中央コアと図柄・文字の設定"
		};
		dictionary["CustomSoundExportProfile"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "💾 导出",
			[LanguageCode.ZhTw] = "💾 匯出",
			[LanguageCode.En] = "💾 Export",
			[LanguageCode.Ja] = "💾 書き出し"
		};
		dictionary["CustomSoundImportProfile"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "📂 导入",
			[LanguageCode.ZhTw] = "📂 匯入",
			[LanguageCode.En] = "📂 Import",
			[LanguageCode.Ja] = "📂 読み込み"
		};
		dictionary["CustomSoundNewProfile"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "➕ 新建",
			[LanguageCode.ZhTw] = "➕ 新增",
			[LanguageCode.En] = "➕ New",
			[LanguageCode.Ja] = "➕ 新規"
		};
		dictionary["CustomSoundOpenEditorWindow"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🎛️ 独立大窗",
			[LanguageCode.ZhTw] = "🎛️ 獨立大視窗",
			[LanguageCode.En] = "🎛️ Open in window",
			[LanguageCode.Ja] = "🎛️ 別ウィンドウで開く"
		};
		dictionary["CustomSoundPlayFlow"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔊 连续模拟完整手势交互体验",
			[LanguageCode.ZhTw] = "🔊 連續模擬完整手勢互動體驗",
			[LanguageCode.En] = "🔊 Play the full gesture interaction",
			[LanguageCode.Ja] = "🔊 一連の操作をまとめて再生"
		};
		dictionary["CustomSoundResetProfile"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔄 重置",
			[LanguageCode.ZhTw] = "🔄 重設",
			[LanguageCode.En] = "🔄 Reset",
			[LanguageCode.Ja] = "🔄 リセット"
		};
		dictionary["EdgeOverflowDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "当在屏幕四周边缘（顶部、底部或两侧）呼出轮盘时，智能检测显示器安全边界，防止轮盘扇区被截断并自动对齐光标至轮盘物理中心。",
			[LanguageCode.ZhTw] = "當在螢幕四周邊緣（頂部、底部或兩側）呼出輪盤時，智慧偵測螢幕安全邊界，防止輪盤扇區被截斷並自動對齊游標至輪盤實際中心。",
			[LanguageCode.En] = "When the wheel opens near a screen edge (top, bottom or either side), the safe area is detected automatically so sectors are never cut off, and the cursor is aligned to the wheel's true centre.",
			[LanguageCode.Ja] = "画面の端（上・下・左右）でホイールを呼び出したとき、安全領域を自動判定してセクターの見切れを防ぎ、カーソルをホイールの実際の中心に合わせます。"
		};
		dictionary["EdgeOverflowTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "屏幕边缘呼出智能防溢出与光标自动对齐",
			[LanguageCode.ZhTw] = "螢幕邊緣呼出智慧防溢出與游標自動對齊",
			[LanguageCode.En] = "Edge-aware overflow prevention and cursor alignment",
			[LanguageCode.Ja] = "画面端での見切れ防止とカーソル自動整列"
		};
		dictionary["EnableCenterAction"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "启用中心核圆动作",
			[LanguageCode.ZhTw] = "啟用中心核圓動作",
			[LanguageCode.En] = "Enable centre core action",
			[LanguageCode.Ja] = "中央コアの動作を有効にする"
		};
		dictionary["EnableGlobalInheritance"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🌐 继承全局方案未配置槽位",
			[LanguageCode.ZhTw] = "🌐 繼承全域方案未配置槽位",
			[LanguageCode.En] = "🌐 Inherit global for unconfigured slots",
			[LanguageCode.Ja] = "🌐 未設定スロットはグローバルを継承"
		};
		dictionary["FocusActionNameLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "轮盘显示文本:",
			[LanguageCode.ZhTw] = "輪盤顯示文字:",
			[LanguageCode.En] = "Wheel label:",
			[LanguageCode.Ja] = "ホイール表示テキスト:"
		};
		dictionary["FocusAddSubAction"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "➕ 添加二级动作",
			[LanguageCode.ZhTw] = "➕ 新增二級動作",
			[LanguageCode.En] = "➕ Add sub-action",
			[LanguageCode.Ja] = "➕ サブ動作を追加"
		};
		dictionary["FocusBackToParent"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "◀ 返回父级扇区",
			[LanguageCode.ZhTw] = "◀ 返回上層扇區",
			[LanguageCode.En] = "◀ Back to parent sector",
			[LanguageCode.Ja] = "◀ 親セクターに戻る"
		};
		dictionary["FocusBatchExit"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "✕ 退出多选",
			[LanguageCode.ZhTw] = "✕ 結束多選",
			[LanguageCode.En] = "✕ Exit multi-select",
			[LanguageCode.Ja] = "✕ 複数選択を終了"
		};
		dictionary["FocusCenterCore"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🎯 中心核圆",
			[LanguageCode.ZhTw] = "🎯 中心核圓",
			[LanguageCode.En] = "🎯 Centre core",
			[LanguageCode.Ja] = "🎯 中央コア"
		};
		dictionary["FocusClearInheritedIcon"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "✕ 清除关联",
			[LanguageCode.ZhTw] = "✕ 清除關聯",
			[LanguageCode.En] = "✕ Clear link",
			[LanguageCode.Ja] = "✕ 関連付けを解除"
		};
		dictionary["FocusClearSubActions"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🗑️ 清空",
			[LanguageCode.ZhTw] = "🗑️ 清空",
			[LanguageCode.En] = "🗑️ Clear all",
			[LanguageCode.Ja] = "🗑️ すべて消去"
		};
		dictionary["FocusNextSlot"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "下一槽 ▶",
			[LanguageCode.ZhTw] = "下一槽 ▶",
			[LanguageCode.En] = "Next slot ▶",
			[LanguageCode.Ja] = "次のスロット ▶"
		};
		dictionary["FocusPickShellTool"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚡ 挑选功能...",
			[LanguageCode.ZhTw] = "⚡ 挑選功能...",
			[LanguageCode.En] = "⚡ Pick a tool...",
			[LanguageCode.Ja] = "⚡ 機能を選択…"
		};
		dictionary["FocusPluginReload"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔄 停用后重新加载",
			[LanguageCode.ZhTw] = "🔄 停用後重新載入",
			[LanguageCode.En] = "🔄 Reload after disabling",
			[LanguageCode.Ja] = "🔄 無効化して再読み込み"
		};
		dictionary["FocusPopulateTileSubActions"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "✨ 预设 8 布局二级轮盘",
			[LanguageCode.ZhTw] = "✨ 預設 8 佈局二級輪盤",
			[LanguageCode.En] = "✨ Fill 8 tile layouts",
			[LanguageCode.Ja] = "✨ 8分割レイアウトを設定"
		};
		dictionary["FocusPrevSlot"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "◀ 上一槽",
			[LanguageCode.ZhTw] = "◀ 上一槽",
			[LanguageCode.En] = "◀ Previous slot",
			[LanguageCode.Ja] = "◀ 前のスロット"
		};
		dictionary["FocusRestoreInherit"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🌐 恢复继承全局",
			[LanguageCode.ZhTw] = "🌐 恢復繼承全域",
			[LanguageCode.En] = "🌐 Restore global inheritance",
			[LanguageCode.Ja] = "🌐 グローバル継承に戻す"
		};
		dictionary["FocusTestAction"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "▶ 测试触发",
			[LanguageCode.ZhTw] = "▶ 測試觸發",
			[LanguageCode.En] = "▶ Test trigger",
			[LanguageCode.Ja] = "▶ テスト実行"
		};
		dictionary["FocusUndoSubActions"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "↩️ 撤销",
			[LanguageCode.ZhTw] = "↩️ 復原",
			[LanguageCode.En] = "↩️ Undo",
			[LanguageCode.Ja] = "↩️ 元に戻す"
		};
		dictionary["GesturesPageSubheader"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "支持针对不同前台应用程序设置专属的多向手势轮盘、按键动作、中心核圆与级联子动作。",
			[LanguageCode.ZhTw] = "支援針對不同前景應用程式設定專屬的多向手勢輪盤、按鍵動作、中心核圓與串聯子動作。",
			[LanguageCode.En] = "Configure a dedicated multi-directional wheel, hotkeys, centre core and cascading sub-actions for each foreground application.",
			[LanguageCode.Ja] = "前面にあるアプリごとに、多方向ホイール・キー操作・中央コア・連鎖サブ動作を個別に設定できます。"
		};
		dictionary["IconLayoutModeTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "排版模式:",
			[LanguageCode.ZhTw] = "排版模式:",
			[LanguageCode.En] = "Layout mode:",
			[LanguageCode.Ja] = "レイアウトモード:"
		};
		dictionary["LayerSwitchTriggerLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔄 切换:",
			[LanguageCode.ZhTw] = "🔄 切換:",
			[LanguageCode.En] = "🔄 Switch:",
			[LanguageCode.Ja] = "🔄 切り替え:"
		};
		dictionary["LayoutOptionsSectionTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "图标与排版选项",
			[LanguageCode.ZhTw] = "圖示與排版選項",
			[LanguageCode.En] = "Icon and layout options",
			[LanguageCode.Ja] = "アイコンとレイアウトの設定"
		};
		dictionary["MappingsSectorCount12"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "12 键钟表方位",
			[LanguageCode.ZhTw] = "12 鍵鐘錶方位",
			[LanguageCode.En] = "12 positions (clock)",
			[LanguageCode.Ja] = "12方位（時計）"
		};
		dictionary["MappingsSectorCount4"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "4 键十字方位",
			[LanguageCode.ZhTw] = "4 鍵十字方位",
			[LanguageCode.En] = "4 positions (cross)",
			[LanguageCode.Ja] = "4方位（十字）"
		};
		dictionary["MappingsSectorCount8"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "8 键全向方位 (推荐)",
			[LanguageCode.ZhTw] = "8 鍵全向方位 (推薦)",
			[LanguageCode.En] = "8 positions (recommended)",
			[LanguageCode.Ja] = "8方位（推奨）"
		};
		dictionary["MappingsTier1Segment"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔘 一级主轮盘",
			[LanguageCode.ZhTw] = "🔘 一級主輪盤",
			[LanguageCode.En] = "🔘 Tier 1 wheel",
			[LanguageCode.Ja] = "🔘 第1階層ホイール"
		};
		dictionary["MappingsTier2Segment"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🌟 二级级联",
			[LanguageCode.ZhTw] = "🌟 二級串聯",
			[LanguageCode.En] = "🌟 Tier 2 cascade",
			[LanguageCode.Ja] = "🌟 第2階層カスケード"
		};
		dictionary["MappingsViewModeCanvas"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🎯 画布联动精调 (推荐)",
			[LanguageCode.ZhTw] = "🎯 畫布關聯精調 (推薦)",
			[LanguageCode.En] = "🎯 Canvas live editor (recommended)",
			[LanguageCode.Ja] = "🎯 キャンバス連動編集（推奨）"
		};
		dictionary["MappingsViewModeList"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "📋 紧凑全览列表",
			[LanguageCode.ZhTw] = "📋 精簡總覽清單",
			[LanguageCode.En] = "📋 Compact list",
			[LanguageCode.Ja] = "📋 コンパクト一覧"
		};
		dictionary["OpenUpdateFolder"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "📂 打开文件位置",
			[LanguageCode.ZhTw] = "📂 開啟檔案位置",
			[LanguageCode.En] = "📂 Open file location",
			[LanguageCode.Ja] = "📂 ファイルの場所を開く"
		};
		dictionary["OpenWebRelease"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🌐 前往网页",
			[LanguageCode.ZhTw] = "🌐 前往網頁",
			[LanguageCode.En] = "🌐 Open release page",
			[LanguageCode.Ja] = "🌐 リリースページを開く"
		};
		dictionary["OuterEscapeCheckboxDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "超过轮盘外圈范围后立即解除高亮，松开右键 0 误触安全放弃。",
			[LanguageCode.ZhTw] = "超過輪盤外圈範圍後立即解除高亮，放開右鍵 0 誤觸安全放棄。",
			[LanguageCode.En] = "Highlight clears as soon as the cursor leaves the wheel, and releasing the right button cancels safely with no misclicks.",
			[LanguageCode.Ja] = "ホイールの外周を越えるとハイライトを解除し、右ボタンを離すと誤操作なく安全に中断します。"
		};
		dictionary["PickCoreIcon"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "选择图标...",
			[LanguageCode.ZhTw] = "選擇圖示...",
			[LanguageCode.En] = "Choose icon...",
			[LanguageCode.Ja] = "アイコンを選択…"
		};
		dictionary["ProfileBrowseExe"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "📁 浏览...",
			[LanguageCode.ZhTw] = "📁 瀏覽...",
			[LanguageCode.En] = "📁 Browse...",
			[LanguageCode.Ja] = "📁 参照…"
		};
		dictionary["ProfileCaptureWindow"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🎯 捕捉窗口...",
			[LanguageCode.ZhTw] = "🎯 擷取視窗...",
			[LanguageCode.En] = "🎯 Capture window...",
			[LanguageCode.Ja] = "🎯 ウィンドウを取得…"
		};
		dictionary["ProfilePickProgram"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🖥️ 软件库...",
			[LanguageCode.ZhTw] = "🖥️ 軟體庫...",
			[LanguageCode.En] = "🖥️ App library...",
			[LanguageCode.Ja] = "🖥️ アプリ一覧…"
		};
		dictionary["ResetProcessTrigger"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔄 恢复默认",
			[LanguageCode.ZhTw] = "🔄 恢復預設",
			[LanguageCode.En] = "🔄 Restore default",
			[LanguageCode.Ja] = "🔄 既定に戻す"
		};
		dictionary["ResetSubDimensions"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔄 恢复二级轮盘默认尺寸",
			[LanguageCode.ZhTw] = "🔄 恢復二級輪盤預設尺寸",
			[LanguageCode.En] = "🔄 Restore default tier 2 size",
			[LanguageCode.Ja] = "🔄 第2階層の既定サイズに戻す"
		};
		dictionary["ResetSubTheme"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔄 恢复与一级轮盘相同主题",
			[LanguageCode.ZhTw] = "🔄 恢復與一級輪盤相同主題",
			[LanguageCode.En] = "🔄 Match tier 1 theme",
			[LanguageCode.Ja] = "🔄 第1階層と同じテーマに戻す"
		};
		dictionary["ResetTextOffset"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔄 位置归位",
			[LanguageCode.ZhTw] = "🔄 位置歸位",
			[LanguageCode.En] = "🔄 Reset position",
			[LanguageCode.Ja] = "🔄 位置を初期化"
		};
		dictionary["RestoreSystemAudio"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔊 一键解除静音并恢复音量 (50%)",
			[LanguageCode.ZhTw] = "🔊 一鍵解除靜音並恢復音量 (50%)",
			[LanguageCode.En] = "🔊 Unmute and restore volume (50%)",
			[LanguageCode.Ja] = "🔊 ミュート解除して音量を復元（50%）"
		};
		dictionary["SectorFontSizeTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "文字字号大小:",
			[LanguageCode.ZhTw] = "文字字號大小:",
			[LanguageCode.En] = "Text size:",
			[LanguageCode.Ja] = "文字サイズ:"
		};
		dictionary["SectorIconSizeTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "图标尺寸大小:",
			[LanguageCode.ZhTw] = "圖示尺寸大小:",
			[LanguageCode.En] = "Icon size:",
			[LanguageCode.Ja] = "アイコンサイズ:"
		};
		dictionary["SectorTextPlacementTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "文字相对位置:",
			[LanguageCode.ZhTw] = "文字相對位置:",
			[LanguageCode.En] = "Text position:",
			[LanguageCode.Ja] = "文字の位置:"
		};
		dictionary["ShowCoreIcon"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "启用中心图案/图标显示",
			[LanguageCode.ZhTw] = "啟用中心圖案/圖示顯示",
			[LanguageCode.En] = "Show centre pattern / icon",
			[LanguageCode.Ja] = "中央の図柄／アイコンを表示"
		};
		dictionary["StartDownloadUpdate"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⬇️ 立即下载更新",
			[LanguageCode.ZhTw] = "⬇️ 立即下載更新",
			[LanguageCode.En] = "⬇️ Download update",
			[LanguageCode.Ja] = "⬇️ 更新をダウンロード"
		};
		dictionary["SubCustomColorsExpanderDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "展开后可精准微调二级扇区底色、高亮光晕、边框线条、文字等各项色彩。",
			[LanguageCode.ZhTw] = "展開後可精準微調二級扇區底色、高亮光暈、邊框線條、文字等各項色彩。",
			[LanguageCode.En] = "Expand to fine-tune the tier 2 sector fill, glow, border, text and other colours.",
			[LanguageCode.Ja] = "展開すると第2階層セクターの背景色・グロー・枠線・文字などの色を細かく調整できます。"
		};
		dictionary["SubCustomColorsExpanderTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🎨 二级轮盘高级配色",
			[LanguageCode.ZhTw] = "🎨 二級輪盤進階配色",
			[LanguageCode.En] = "🎨 Tier 2 advanced colours",
			[LanguageCode.Ja] = "🎨 第2階層の詳細配色"
		};
		dictionary["SubWheelTriggerDistDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "调节光标划出距离中心多远时展开二级级联菜单。数值较小时轻划即可展开，数值较大时需向外划出更远距离才展开二级，防止快速触发一级动作时产生视觉干扰。",
			[LanguageCode.ZhTw] = "調節游標划出距離中心多遠時展開二級串聯選單。數值較小時輕划即可展開，數值較大時需向外划出更遠距離才展開二級，防止快速觸發一級動作時產生視覺干擾。",
			[LanguageCode.En] = "How far the cursor must move from the centre before the tier 2 cascade opens. Smaller values open it with a light flick; larger values require a longer outward movement, avoiding visual noise when tier 1 actions fire quickly.",
			[LanguageCode.Ja] = "カーソルが中心からどれだけ離れたら第2階層カスケードを展開するかを調整します。小さい値では少し動かすだけで開き、大きい値ではより遠くへ動かす必要があり、第1階層の動作を素早く実行する際の視覚的な邪魔を防げます。"
		};
		dictionary["SubWheelTriggerDistLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "二级轮盘展开触发距离:",
			[LanguageCode.ZhTw] = "二級輪盤展開觸發距離:",
			[LanguageCode.En] = "Tier 2 trigger distance:",
			[LanguageCode.Ja] = "第2階層の展開距離:"
		};
		dictionary["SystemAudioWarning"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚠️ 检测到 Windows 系统主音量当前为 0% 或已静音，会导致所有交互音效无声。",
			[LanguageCode.ZhTw] = "⚠️ 偵測到 Windows 系統主音量目前為 0% 或已靜音，會導致所有互動音效無聲。",
			[LanguageCode.En] = "⚠️ The Windows master volume is at 0% or muted — all interaction sounds will be silent.",
			[LanguageCode.Ja] = "⚠️ Windows のシステム音量が 0% かミュートのため、すべての操作音が無音になります。"
		};
		dictionary["TestCancelAction"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🧪 模拟测试触发",
			[LanguageCode.ZhTw] = "🧪 模擬測試觸發",
			[LanguageCode.En] = "🧪 Test trigger",
			[LanguageCode.Ja] = "🧪 テスト実行"
		};
		dictionary["Tier2DimensionsExpander"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🌐 二级轮盘几何形态与尺寸 (展开微调)",
			[LanguageCode.ZhTw] = "🌐 二級輪盤幾何形態與尺寸 (展開微調)",
			[LanguageCode.En] = "🌐 Tier 2 geometry and size (expand to fine-tune)",
			[LanguageCode.Ja] = "🌐 第2階層の形状とサイズ（展開して微調整）"
		};
		dictionary["Tier2ThemeExpander"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🌐 二级轮盘风格与配色 (展开定制)",
			[LanguageCode.ZhTw] = "🌐 二級輪盤風格與配色 (展開自訂)",
			[LanguageCode.En] = "🌐 Tier 2 style and colours (expand to customise)",
			[LanguageCode.Ja] = "🌐 第2階層のスタイルと配色（展開してカスタマイズ）"
		};
		dictionary["UpdatePkgLightweight"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "依赖 .NET 8 运行时轻量版 (~2.7 MB)",
			[LanguageCode.ZhTw] = "依賴 .NET 8 執行階段輕量版 (~2.7 MB)",
			[LanguageCode.En] = "Lightweight, needs .NET 8 runtime (~2.7 MB)",
			[LanguageCode.Ja] = "軽量版・.NET 8 ランタイムが必要（約 2.7 MB）"
		};
		dictionary["UpdatePkgStandalone"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "独立免安装单文件版 (~68 MB, 推荐)",
			[LanguageCode.ZhTw] = "獨立免安裝單檔案版 (~68 MB, 推薦)",
			[LanguageCode.En] = "Standalone, no install needed (~68 MB, recommended)",
			[LanguageCode.Ja] = "単体動作・インストール不要（約 68 MB、推奨）"
		};
		dictionary["ViewReleasesWeb"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🌐 网页发布页",
			[LanguageCode.ZhTw] = "🌐 網頁發佈頁",
			[LanguageCode.En] = "🌐 Release page",
			[LanguageCode.Ja] = "🌐 リリースページ"
		};
		dictionary["SidebarCollapse"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "折叠侧边栏",
			[LanguageCode.ZhTw] = "摺疊側邊欄",
			[LanguageCode.En] = "Collapse sidebar",
			[LanguageCode.Ja] = "サイドバーを折りたたむ"
		};
		dictionary["SidebarExpand"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "展开侧边栏",
			[LanguageCode.ZhTw] = "展開側邊欄",
			[LanguageCode.En] = "Expand sidebar",
			[LanguageCode.Ja] = "サイドバーを展開"
		};
		dictionary["BottomStatusNote"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "注: 所有修改均在内存中即时生效，点击【保存更改】持久化保存至硬盘。",
			[LanguageCode.ZhTw] = "註: 所有修改均在記憶體中即時生效，點擊【儲存變更】持久化儲存至硬碟。",
			[LanguageCode.En] = "Note: All changes take effect in memory immediately. Click [Save Changes] to persist to disk.",
			[LanguageCode.Ja] = "注: 変更はメモリ上で即座に有効になります。[変更を保存] で設定ファイルに永続化されます。"
		};
		dictionary["BtnSave"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "保存更改",
			[LanguageCode.ZhTw] = "儲存變更",
			[LanguageCode.En] = "Save Changes",
			[LanguageCode.Ja] = "変更を保存"
		};
		dictionary["BtnClose"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "关闭并隐藏",
			[LanguageCode.ZhTw] = "關閉並隱藏",
			[LanguageCode.En] = "Close & Hide",
			[LanguageCode.Ja] = "閉じて隠す"
		};
		dictionary["TriggerHeader"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "触发与场景隔离设置",
			[LanguageCode.ZhTw] = "觸發與場景隔離設定",
			[LanguageCode.En] = "Trigger & Scene Isolation",
			[LanguageCode.Ja] = "トリガーとシーンの分離設定"
		};
		dictionary["TriggerSubheader"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "在此配置全局鼠标手势的触发灵敏度、全屏游戏自动拦截与排除程序黑名单。",
			[LanguageCode.ZhTw] = "在此配置全域滑鼠手勢的觸發靈敏度、全螢幕遊戲自動攔截與排除程式黑名單。",
			[LanguageCode.En] = "Configure mouse gesture sensitivity, full-screen gaming bypass, and exclusion blacklist.",
			[LanguageCode.Ja] = "マウスジェスチャーの感度、フルスクリーンゲームでの自動回避、除外プロセスを設定します。"
		};
		dictionary["TriggerRecorderTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "轮盘唤醒触发按键 & 组合键录制",
			[LanguageCode.ZhTw] = "輪盤喚醒觸發按鍵 & 組合鍵錄製",
			[LanguageCode.En] = "Radial Menu Trigger & Combo Key Recorder",
			[LanguageCode.Ja] = "ラジアルメニュー起動トリガー＆コンボキー録画"
		};
		dictionary["TriggerRecorderDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "支持鼠标所有按键（右键/中键/侧键1/侧键2/左键）与键盘单键（如 CapsLock、波浪键~、空格、字母键、F区键等）及组合键一键物理录制绑定。绑定鼠标左键后长按可唤醒轮盘，单机左键保持系统正常点击。未触发手势的轻点将自动放行原生按键点击。建议避免将常用功能按键绑定为触发按键。",
			[LanguageCode.ZhTw] = "支持滑鼠所有按鍵（右鍵/中鍵/側鍵1/側鍵2/左鍵）與鍵盤單鍵（如 CapsLock、波浪鍵~、空格、字母鍵、F區鍵等）及組合鍵一鍵物理錄製綁定。綁定滑鼠左鍵後長按可喚醒輪盤，單擊左鍵保持系統正常點擊。未觸發手勢的輕點將自動放行原生按鍵點擊。建議避免將常用功能按鍵綁定為觸發按鍵。",
			[LanguageCode.En] = "Supports one-click physical recording for all mouse buttons (Right, Middle, Side 1/2, Left) and keyboard keys as well as combos. When Left Button is bound, long-press opens radial menu, while quick click maintains normal click function. It is recommended to avoid binding frequently used functional keys as trigger keys.",
			[LanguageCode.Ja] = "すべてのマウスボタン（右、中央、サイド1/2、左）およびキーボード単キーやコンボの物理録画に対応。左ボタン設定時は長押しでホイール起動、短押しクリックは通常の操作を維持します。常用する機能キーを起動トリガーに割り当てることは避けることを推奨します。"
		};
		dictionary["TriggerLeftButtonRecordedTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🟢 已成功绑定【鼠标左键】：长按左键唤醒轮盘，单机左键保持系统正常点击！",
			[LanguageCode.ZhTw] = "🟢 已成功綁定【滑鼠左鍵】：長按左鍵喚醒輪盤，單擊左鍵保持系統正常點擊！",
			[LanguageCode.En] = "🟢 [Left Mouse Button] bound: Long-press to open radial menu, quick click maintains normal click function!",
			[LanguageCode.Ja] = "🟢 【マウス左ボタン】設定完了：長押しでホイール起動、短押しクリックは通常の操作を維持します！"
		};
		dictionary["LongPressTriggerTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "长按触发按键呼出面板 (可选)",
			[LanguageCode.ZhTw] = "長按觸發按鍵呼叫面板 (可選)",
			[LanguageCode.En] = "Long-press trigger to open the menu (optional)",
			[LanguageCode.Ja] = "長押しでメニューを開く (任意)"
		};
		dictionary["LongPressTriggerDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "按住触发键不动超过设定时长即呼出轮盘；未达时长松手仍为普通按键。与按住拖动呼出共存。",
			[LanguageCode.ZhTw] = "按住觸發鍵不動超過設定時長即呼叫輪盤；未達時長鬆手仍為普通按鍵。與按住拖動呼叫共存。",
			[LanguageCode.En] = "Hold the trigger key still for the configured duration to open the wheel. Releasing early still behaves as a normal key press. Coexists with the drag-to-open gesture.",
			[LanguageCode.Ja] = "トリガーキーを一定時間押し続けるとホイールが開きます。時間前に離すと通常のキー操作になります。ドラッグで開く動作と共存できます。"
		};
		dictionary["GestureTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "鼠标手势",
			[LanguageCode.ZhTw] = "滑鼠手勢",
			[LanguageCode.En] = "Mouse Gestures",
			[LanguageCode.Ja] = "マウスジェスチャー"
		};
		dictionary["GestureDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "启用后，按住手势触发键画出轨迹（如 ↓、→、L 形），释放即执行对应动作；支持单段、双段与三段图样。轻点仍透传原生点击。",
			[LanguageCode.ZhTw] = "啟用後，按住手勢觸發鍵畫出軌跡（如 ↓、→、L 形），釋放即執行對應動作；支援單段、雙段與三段圖樣。輕點仍透傳原生點擊。",
			[LanguageCode.En] = "Hold the gesture trigger button and draw a trail (e.g. ↓, →, L-shape); releasing executes the mapped action. Supports single, double and triple-segment patterns. Quick clicks still pass through.",
			[LanguageCode.Ja] = "トリガーキーを押しながら軌跡を描くと（↓、→、L字など）、離した時点で割り当てたアクションを実行します。1〜3セグメントのジェスチャーに対応。短押しは通常通り通過します。"
		};
		dictionary["GestureEnableText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "启用鼠标手势模式",
			[LanguageCode.ZhTw] = "啟用滑鼠手勢模式",
			[LanguageCode.En] = "Enable mouse gesture mode",
			[LanguageCode.Ja] = "マウスジェスチャーモードを有効化"
		};
		dictionary["GestureEnableDescText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "手势触发键将不再弹出轮盘，仅用于绘制手势；其它触发键照常弹轮盘。",
			[LanguageCode.ZhTw] = "手勢觸發鍵將不再彈出輪盤，僅用於繪製手勢；其它觸發鍵照常彈輪盤。",
			[LanguageCode.En] = "The gesture trigger button no longer opens the wheel; it is used for drawing gestures only. Other triggers keep opening the wheel.",
			[LanguageCode.Ja] = "ジェスチャートリガーキーはホイールを開かず、ジェスチャー描画専用になります。他のトリガーは従来通りホイールを開きます。"
		};
		dictionary["GestureTriggerLabelText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "手势触发键：",
			[LanguageCode.ZhTw] = "手勢觸發鍵：",
			[LanguageCode.En] = "Gesture trigger button:",
			[LanguageCode.Ja] = "ジェスチャートリガーキー："
		};
		dictionary["GestureHintPlaceText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "松手提示位置：",
			[LanguageCode.ZhTw] = "鬆手提示位置：",
			[LanguageCode.En] = "Release-hint position:",
			[LanguageCode.Ja] = "離した時のヒント位置："
		};
		dictionary["GestureSensitivityTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "手势段灵敏度：",
			[LanguageCode.ZhTw] = "手勢段靈敏度：",
			[LanguageCode.En] = "Gesture segment sensitivity:",
			[LanguageCode.Ja] = "ジェスチャー感度："
		};
		dictionary["MouseReleaseDebounceTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "启用鼠标右键释放防抖",
			[LanguageCode.ZhTw] = "啟用滑鼠右鍵釋放防抖",
			[LanguageCode.En] = "Enable right-button release debounce",
			[LanguageCode.Ja] = "右ボタンのリリースデバウンスを有効化"
		};
		dictionary["MouseReleaseDebounceDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "收到右键松开后短暂等待；期间若再次按下，则视为微动抖动并保持当前轮盘，不执行动作。",
			[LanguageCode.ZhTw] = "收到右鍵放開後短暫等待；期間若再次按下，則視為微動抖動並保持目前輪盤，不執行動作。",
			[LanguageCode.En] = "Briefly waits after right-button release. If another press arrives during the window, it is treated as switch chatter and the current wheel stays active without executing.",
			[LanguageCode.Ja] = "右ボタンを離した後に短時間待機し、その間に再度押された場合はチャタリングとして扱い、アクションを実行せず現在のホイールを維持します。"
		};
		dictionary["MouseReleaseDebounceValueDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "建议 8～15 ms，默认 12 ms。数值越大越能过滤微动抖动，但松手后的动作延迟也会相应增加。",
			[LanguageCode.ZhTw] = "建議 8～15 ms，預設 12 ms。數值越大越能過濾微動抖動，但放開後的動作延遲也會相應增加。",
			[LanguageCode.En] = "Recommended: 8–15 ms; default: 12 ms. Higher values filter more switch chatter but add the same release latency.",
			[LanguageCode.Ja] = "推奨値は 8～15 ms、既定値は 12 ms です。値を大きくするとチャタリング除去は強くなりますが、リリース後の遅延も増加します。"
		};
		dictionary["CancelActionTitleText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "外甩取消时执行的动作",
			[LanguageCode.ZhTw] = "外甩取消時執行的動作",
			[LanguageCode.En] = "Action on Outer-Escape Cancel",
			[LanguageCode.Ja] = "外側スワイプキャンセル時の動作"
		};
		dictionary["CancelActionDescText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "仅当通过「外甩取消」（向外甩出且未选中任何动作）时执行这个自定义动作；回到中心取消按钮松手仍为默认静默关闭。",
			[LanguageCode.ZhTw] = "僅當透過「外甩取消」（向外甩出且未選中任何動作）時執行這個自訂動作；回到中心取消按鈕鬆手仍為預設靜默關閉。",
			[LanguageCode.En] = "Only when you cancel by flinging outward (nothing selected) does this custom action run; releasing over the center cancel button still just closes the menu silently.",
			[LanguageCode.Ja] = "外側へフリックしてキャンセルした場合（何も選択していない）のみカスタムアクションを実行します。中央のキャンセルで離すと従来通りサイレントクローズします。"
		};
		dictionary["CancelActionEnableText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "启用自定义外甩取消动作",
			[LanguageCode.ZhTw] = "啟用自訂外甩取消動作",
			[LanguageCode.En] = "Enable custom outer-escape cancel action",
			[LanguageCode.Ja] = "外側スワイプキャンセル時のカスタム動作を有効化"
		};
		dictionary["ActionTypeTileShort"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "平铺窗口",
			[LanguageCode.ZhTw] = "平鋪視窗",
			[LanguageCode.En] = "Tile Windows",
			[LanguageCode.Ja] = "ウィンドウを並べる"
		};
		dictionary["TileLayout2L"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "左右对半",
			[LanguageCode.ZhTw] = "左右對半",
			[LanguageCode.En] = "Left-Right",
			[LanguageCode.Ja] = "左右分割"
		};
		dictionary["TileLayout2T"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "上下对半",
			[LanguageCode.ZhTw] = "上下對半",
			[LanguageCode.En] = "Top-Bottom",
			[LanguageCode.Ja] = "上下分割"
		};
		dictionary["TileLayout3L12"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "左大列 + 右上/右下",
			[LanguageCode.ZhTw] = "左大列 + 右上/右下",
			[LanguageCode.En] = "Big Left + Right Two",
			[LanguageCode.Ja] = "左大＋右二"
		};
		dictionary["TileLayout3R21"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "右大列 + 左上/左下",
			[LanguageCode.ZhTw] = "右大列 + 左上/左下",
			[LanguageCode.En] = "Big Right + Left Two",
			[LanguageCode.Ja] = "右大＋左二"
		};
		dictionary["TileLayout3R"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "三等分竖列",
			[LanguageCode.ZhTw] = "三等分豎列",
			[LanguageCode.En] = "Three Columns",
			[LanguageCode.Ja] = "3等分列"
		};
		dictionary["TileLayout4G"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "四宫格 2×2",
			[LanguageCode.ZhTw] = "四宮格 2×2",
			[LanguageCode.En] = "2×2 Grid",
			[LanguageCode.Ja] = "2×2 グリッド"
		};
		dictionary["TileLayout6G"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "六宫格 3×2",
			[LanguageCode.ZhTw] = "六宮格 3×2",
			[LanguageCode.En] = "3×2 Grid",
			[LanguageCode.Ja] = "3×2 グリッド"
		};
		dictionary["ActionTypeWindowManagerShort"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "窗口管理",
			[LanguageCode.ZhTw] = "視窗管理",
			[LanguageCode.En] = "Window Management",
			[LanguageCode.Ja] = "ウィンドウ管理"
		};
		dictionary["ActionTypeTileRestoreShort"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "恢复上次平铺",
			[LanguageCode.ZhTw] = "還原上次平鋪",
			[LanguageCode.En] = "Restore Tiles",
			[LanguageCode.Ja] = "前の配置に戻す"
		};
		dictionary["ActionTypeMoveMonitorShort"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "窗口移到下一屏",
			[LanguageCode.ZhTw] = "視窗移到下一螢幕",
			[LanguageCode.En] = "Move to Next Monitor",
			[LanguageCode.Ja] = "次のモニターへ移動"
		};
		dictionary["ActionTypeTopmostShort"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "窗口置顶/取消置顶",
			[LanguageCode.ZhTw] = "視窗置頂/取消置頂",
			[LanguageCode.En] = "Toggle Always-on-Top",
			[LanguageCode.Ja] = "最前面表示の切替"
		};
		dictionary["ActionTypeOpacityShort"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "窗口透明度",
			[LanguageCode.ZhTw] = "視窗透明度",
			[LanguageCode.En] = "Window Opacity",
			[LanguageCode.Ja] = "ウィンドウの透明度"
		};
		dictionary["TileCycleLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "循环切换布局",
			[LanguageCode.ZhTw] = "循環切換佈局",
			[LanguageCode.En] = "Cycle layouts",
			[LanguageCode.Ja] = "レイアウトを順番に切替"
		};
		dictionary["TileGlobalTitleText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "平铺窗口设置",
			[LanguageCode.ZhTw] = "平鋪視窗設定",
			[LanguageCode.En] = "Tiling Settings",
			[LanguageCode.Ja] = "タイリング設定"
		};
		dictionary["TileGlobalDescText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "「平铺窗口」动作的全局行为（屏幕边距、窗口间距、包含最小化窗口、排除名单与多布局循环切换）。",
			[LanguageCode.ZhTw] = "「平鋪視窗」動作的全域行為（螢幕邊距、視窗間距、包含最小化視窗、排除名單與多佈局循環切換）。",
			[LanguageCode.En] = "Global behaviour of the Tile Windows action (screen margins, window gaps, minimized windows, exclusion list and layout cycling).",
			[LanguageCode.Ja] = "「ウィンドウを並べる」アクションの全体挙動（画面マージン、ウィンドウ間隔、最小化ウィンドウの包含、除外リスト、レイアウト切替）。"
		};
		dictionary["TileMarginText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "屏幕边距（上 / 下 / 左 / 右，物理像素，0 = 贴边）：",
			[LanguageCode.ZhTw] = "螢幕邊距（上 / 下 / 左 / 右，物理像素，0 = 貼邊）：",
			[LanguageCode.En] = "Screen margins (Top / Bottom / Left / Right, physical pixels, 0 = flush):",
			[LanguageCode.Ja] = "画面マージン（上 / 下 / 左 / 右、物理ピクセル、0 = 端に寄せる）："
		};
		dictionary["TileGapText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "窗口间距（物理像素，0 = 紧贴）：",
			[LanguageCode.ZhTw] = "視窗間距（物理像素，0 = 緊貼）：",
			[LanguageCode.En] = "Gap between windows (physical pixels, 0 = flush):",
			[LanguageCode.Ja] = "ウィンドウ間隔（物理ピクセル、0 = 隙間なし）："
		};
		dictionary["TileMinimizeText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "包含最小化窗口（还原后参与平铺）",
			[LanguageCode.ZhTw] = "包含最小化視窗（還原後參與平鋪）",
			[LanguageCode.En] = "Include minimized windows (restore into the layout)",
			[LanguageCode.Ja] = "最小化ウィンドウも含める（復元して配置）"
		};
		dictionary["TileExcludeText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "平铺排除名单（进程 exe 名，逗号分隔，如 notepad,spotify）：",
			[LanguageCode.ZhTw] = "平鋪排除名單（程序 exe 名，逗號分隔，如 notepad,spotify）：",
			[LanguageCode.En] = "Tiling exclusion list (process exe names, comma-separated, e.g. notepad,spotify):",
			[LanguageCode.Ja] = "並べる対象外のプロセス（exe名、カンマ区切り例 notepad,spotify）："
		};
		dictionary["TileRestoreAllLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "还原所有窗口（回到平铺前）",
			[LanguageCode.ZhTw] = "還原所有視窗（回到平鋪前）",
			[LanguageCode.En] = "Restore all windows (pre-tile state)",
			[LanguageCode.Ja] = "すべてのウィンドウを元に戻す（並べる前の状態）"
		};
		dictionary["TileCycleBackLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "循环返回",
			[LanguageCode.ZhTw] = "循環返回",
			[LanguageCode.En] = "Cycle previous",
			[LanguageCode.Ja] = "前のレイアウトへ"
		};
		dictionary["TileLayoutML"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "主窗居左 + 右栈",
			[LanguageCode.ZhTw] = "主窗居左 + 右棧",
			[LanguageCode.En] = "Master Left + Stack",
			[LanguageCode.Ja] = "マスター左＋スタック"
		};
		dictionary["TileLayoutMR"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "主窗居右 + 左栈",
			[LanguageCode.ZhTw] = "主窗居右 + 左棧",
			[LanguageCode.En] = "Master Right + Stack",
			[LanguageCode.Ja] = "マスター右＋スタック"
		};
		dictionary["TileLayoutMT"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "主窗居上 + 下栈",
			[LanguageCode.ZhTw] = "主窗居上 + 下棧",
			[LanguageCode.En] = "Master Top + Stack",
			[LanguageCode.Ja] = "マスター上＋スタック"
		};
		dictionary["TileLayoutMB"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "主窗居下 + 上栈",
			[LanguageCode.ZhTw] = "主窗居下 + 上棧",
			[LanguageCode.En] = "Master Bottom + Stack",
			[LanguageCode.Ja] = "マスター下＋スタック"
		};
		dictionary["TileLayoutMO"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "单窗全屏 (Monocle)",
			[LanguageCode.ZhTw] = "單窗全螢幕 (Monocle)",
			[LanguageCode.En] = "Monocle",
			[LanguageCode.Ja] = "モノクル（全画面）"
		};
		dictionary["TileLayoutHS"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "上主 50% + 下栈 (H-Stack)",
			[LanguageCode.ZhTw] = "上主 50% + 下棧 (H-Stack)",
			[LanguageCode.En] = "Master Top + Stack (H-Stack)",
			[LanguageCode.Ja] = "上マスター＋下スタック"
		};
		dictionary["TileLayoutVS"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "左主 50% + 右栈 (V-Stack)",
			[LanguageCode.ZhTw] = "左主 50% + 右棧 (V-Stack)",
			[LanguageCode.En] = "Master Left + Stack (V-Stack)",
			[LanguageCode.Ja] = "左マスター＋右スタック"
		};
		dictionary["TileLayoutCOL"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "等宽竖列 (Columns)",
			[LanguageCode.ZhTw] = "等寬豎列 (Columns)",
			[LanguageCode.En] = "Columns",
			[LanguageCode.Ja] = "等幅カラム"
		};
		dictionary["TileLayoutBSP"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "二叉分割 (BSP)",
			[LanguageCode.ZhTw] = "二叉分割 (BSP)",
			[LanguageCode.En] = "Binary Split (BSP)",
			[LanguageCode.Ja] = "二分木分割 (BSP)"
		};
		dictionary["TileLayoutAG"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "自适应网格 (A-Grid)",
			[LanguageCode.ZhTw] = "自適應網格 (A-Grid)",
			[LanguageCode.En] = "Auto Grid",
			[LanguageCode.Ja] = "自動グリッド"
		};
		dictionary["TileCycleRangeText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "循环切换范围（布局 key 逗号分隔，空=全部，如 2L,2T,4G,ML）：",
			[LanguageCode.ZhTw] = "循環切換範圍（佈局 key 逗號分隔，空=全部，如 2L,2T,4G,ML）：",
			[LanguageCode.En] = "Cycle range (layout keys, comma-separated; empty = all, e.g. 2L,2T,4G,ML):",
			[LanguageCode.Ja] = "循環切替範囲（レイアウトキーをカンマ区切り。空=全て。例 2L,2T,4G,ML）："
		};
		dictionary["GestureMappingTitleText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "手势图样映射",
			[LanguageCode.ZhTw] = "手勢圖樣映射",
			[LanguageCode.En] = "Gesture Pattern Mappings",
			[LanguageCode.Ja] = "ジェスチャーパターン割り当て"
		};
		dictionary["BtnRecordTrigger"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\udd34 点击录制触发键 / 组合键",
			[LanguageCode.ZhTw] = "\ud83d\udd34 點擊錄製觸發鍵 / 組合鍵",
			[LanguageCode.En] = "\ud83d\udd34 Record Trigger / Combo Key",
			[LanguageCode.Ja] = "\ud83d\udd34 トリガーキー/コンボを録画"
		};
		dictionary["BtnResetDefaultTrigger"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\udd04 恢复默认 (鼠标右键)",
			[LanguageCode.ZhTw] = "\ud83d\udd04 恢復默認 (滑鼠右鍵)",
			[LanguageCode.En] = "\ud83d\udd04 Reset Default (Right Mouse)",
			[LanguageCode.Ja] = "\ud83d\udd04 デフォルトに戻す (マウス右ボタン)"
		};
		dictionary["CurrentBindingLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "当前生效触发键：",
			[LanguageCode.ZhTw] = "當前生效觸發鍵：",
			[LanguageCode.En] = "Active Trigger Binding:",
			[LanguageCode.Ja] = "現在の有効トリガー："
		};
		dictionary["TriggerButtonTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "轮盘唤醒触发按键",
			[LanguageCode.ZhTw] = "輪盤喚醒觸發按鍵",
			[LanguageCode.En] = "Radial Menu Trigger Button",
			[LanguageCode.Ja] = "ラジアルメニュー起動トリガーボタン"
		};
		dictionary["TriggerButtonDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "选择按住并拖动唤醒轮盘手势的鼠标按键。未触发手势的轻点将自动放行原生按键点击。",
			[LanguageCode.ZhTw] = "選擇按住並拖動喚醒輪盤手勢的滑鼠按鍵。未觸發手勢的輕點將自動放行原生按鍵點擊。",
			[LanguageCode.En] = "Select which mouse button to hold and drag to summon the radial menu. Quick clicks without dragging will naturally pass through native click events.",
			[LanguageCode.Ja] = "長押しドラッグでラジアルメニューを起動するマウスボタンを選択します。短押しクリックは通常のクリックとして処理されます。"
		};
		dictionary["TriggerBtnRight"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🖱️ 鼠标右键 [推荐 / 默认]",
			[LanguageCode.ZhTw] = "🖱️ 滑鼠右鍵 [推薦 / 默認]",
			[LanguageCode.En] = "🖱️ Right Mouse Button [Default / Recommended]",
			[LanguageCode.Ja] = "🖱️ マウス右ボタン [推奨 / デフォルト]"
		};
		dictionary["TriggerBtnMiddle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🖱️ 鼠标中键 / 滚轮按压",
			[LanguageCode.ZhTw] = "🖱️ 滑鼠中鍵 / 滾輪按壓",
			[LanguageCode.En] = "🖱️ Middle Mouse Button / Wheel Click",
			[LanguageCode.Ja] = "🖱️ マウス中央ボタン / ホイールクリック"
		};
		dictionary["TriggerBtnX1"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🖱️ 鼠标侧键 1 / 后退键",
			[LanguageCode.ZhTw] = "🖱️ 滑鼠側鍵 1 / 後退鍵",
			[LanguageCode.En] = "🖱️ Mouse Side Button 1 / Back",
			[LanguageCode.Ja] = "🖱️ マウスサイドボタン 1 / 戻る"
		};
		dictionary["TriggerBtnX2"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🖱️ 鼠标侧键 2 / 前进键",
			[LanguageCode.ZhTw] = "🖱️ 滑鼠側鍵 2 / 前進鍵",
			[LanguageCode.En] = "🖱️ Mouse Side Button 2 / Forward",
			[LanguageCode.Ja] = "🖱️ マウスサイドボタン 2 / 進む"
		};
		dictionary["SensitivityTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "手势触发灵敏度",
			[LanguageCode.ZhTw] = "手勢觸發靈敏度",
			[LanguageCode.En] = "Trigger Sensitivity",
			[LanguageCode.Ja] = "ジェスチャー起動感度"
		};
		dictionary["SensitivityDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "按住鼠标右键移动超过指定像素距离后呼出手势轮盘。距离越小越灵敏，过小可能造成右键微抖动误触。",
			[LanguageCode.ZhTw] = "按住滑鼠右鍵移動超過指定像素距離後呼出手勢輪盤。距離越小越靈敏，過小可能造成右鍵微抖動誤觸。",
			[LanguageCode.En] = "Hold right-click and move beyond this pixel distance to trigger radial menu. Lower values are more sensitive.",
			[LanguageCode.Ja] = "右クリックを押しながら指定ピクセル以上移動するとホイールを呼び出します。値が小さいほど高感度です。"
		};
		dictionary["SceneIsolationTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "场景隔离与防误触",
			[LanguageCode.ZhTw] = "場景隔離與防誤觸",
			[LanguageCode.En] = "Scene Isolation & Guard",
			[LanguageCode.Ja] = "シーン分離と誤操作防止"
		};
		dictionary["SceneIsolationDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "当处于特定场景或配合修饰键操作时，自动绕过轮盘拦截，放行原生右键事件。",
			[LanguageCode.ZhTw] = "當處於特定場景或配合修飾鍵操作時，自動繞過輪盤攔截，放行原生右鍵事件。",
			[LanguageCode.En] = "Automatically bypass radial menu and pass-through native right-click in specific scenarios.",
			[LanguageCode.Ja] = "特定の環境や修飾キー操作時にホイールを無効化し、通常の右クリックを通過させます。"
		};
		dictionary["FullScreenOption"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "全屏游戏/独占应用自动禁用手势",
			[LanguageCode.ZhTw] = "全螢幕遊戲/獨佔應用自動禁用手勢",
			[LanguageCode.En] = "Auto-disable in Full-screen games / Exclusive apps",
			[LanguageCode.Ja] = "全画面ゲーム/専用アプリでジェスチャーを自動無効化"
		};
		dictionary["FullScreenOptionDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "自动检测当前前台窗口是否处于全屏独占状态，避免游戏瞄准等右键操作被拦截。",
			[LanguageCode.ZhTw] = "自動檢測當前前台視窗是否處於全螢幕獨佔狀態，避免遊戲瞄準等右鍵操作被攔截。",
			[LanguageCode.En] = "Detects whether active window is running in full-screen to avoid intercepting gaming right-clicks.",
			[LanguageCode.Ja] = "アクティブなウィンドウが全画面かどうかを検知し、ゲームの照準等の右クリック操作を邪魔しません。"
		};
		dictionary["ModifierPassTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "快捷键旁路穿透 (按住以下修饰键拖拽时不触发手势):",
			[LanguageCode.ZhTw] = "快速鍵旁路穿透 (按住以下修飾鍵拖曳時不觸發手勢):",
			[LanguageCode.En] = "Modifier Pass-Through (hold to bypass gestures):",
			[LanguageCode.Ja] = "修飾キーバイパス (押下中はジェスチャーを無効化):"
		};
		dictionary["ModifierCtrl"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "按住 Ctrl 键时旁路",
			[LanguageCode.ZhTw] = "按住 Ctrl 鍵時旁路",
			[LanguageCode.En] = "Bypass on Ctrl",
			[LanguageCode.Ja] = "Ctrl 押下時にバイパス"
		};
		dictionary["ModifierShift"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "按住 Shift 键时旁路",
			[LanguageCode.ZhTw] = "按住 Shift 鍵時旁路",
			[LanguageCode.En] = "Bypass on Shift",
			[LanguageCode.Ja] = "Shift 押下時にバイパス"
		};
		dictionary["ModifierAlt"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "按住 Alt 键时旁路",
			[LanguageCode.ZhTw] = "按住 Alt 鍵時旁路",
			[LanguageCode.En] = "Bypass on Alt",
			[LanguageCode.Ja] = "Alt 押下時にバイパス"
		};
		dictionary["BlacklistTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "进程排除黑名单",
			[LanguageCode.ZhTw] = "行程排除黑名單",
			[LanguageCode.En] = "Process Exclusion Blacklist",
			[LanguageCode.Ja] = "除外プロセスブラックリスト"
		};
		dictionary["BlacklistDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "在排除名单中的应用程序（如远程桌面、画图、3D建模软件）中，完全放行鼠标右键。",
			[LanguageCode.ZhTw] = "在排除名單中的應用程式（如遠端桌面、小畫家、3D建模軟體）中，完全放行滑鼠右鍵。",
			[LanguageCode.En] = "Native right-click is fully allowed within blacklisted applications (e.g. Remote Desktop, Paint, CAD).",
			[LanguageCode.Ja] = "登録されたアプリ（リモートデスクトップ、ペイント、3Dモデリング等）では右クリックを直接通します。"
		};
		dictionary["BtnAddProcess"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "➕ 添加进程",
			[LanguageCode.ZhTw] = "➕ 新增處理程序",
			[LanguageCode.En] = "➕ Add Process",
			[LanguageCode.Ja] = "➕ プロセス追加"
		};
		dictionary["BtnPickProcess"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\udd0d 选择应用...",
			[LanguageCode.ZhTw] = "\ud83d\udd0d 選擇應用程式...",
			[LanguageCode.En] = "\ud83d\udd0d Select App...",
			[LanguageCode.Ja] = "\ud83d\udd0d アプリを選択..."
		};
		dictionary["BtnDeleteProcess"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\uddd1\ufe0f 移除选中",
			[LanguageCode.ZhTw] = "\ud83d\uddd1\ufe0f 移除選取",
			[LanguageCode.En] = "\ud83d\uddd1\ufe0f Remove Selected",
			[LanguageCode.Ja] = "\ud83d\uddd1\ufe0f 選択項目を削除"
		};
		dictionary["BlacklistPlaceholder"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "输入进程名称 (如 solidworks.exe) 或点击右侧选择应用...",
			[LanguageCode.ZhTw] = "輸入處理程序名稱 (如 solidworks.exe) 或點擊右側選擇應用程式...",
			[LanguageCode.En] = "Enter process name (e.g. solidworks.exe) or browse...",
			[LanguageCode.Ja] = "プロセス名を入力 (例: solidworks.exe) またはアプリを選択..."
		};
		dictionary["AppearanceHeader"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "轮盘外观与形态定制",
			[LanguageCode.ZhTw] = "輪盤外觀與形態自訂",
			[LanguageCode.En] = "Appearance & Shapes Customization",
			[LanguageCode.Ja] = "外観と形状のカスタマイズ"
		};
		dictionary["AppearanceSubheader"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "自由配置轮盘视觉风格、配色方案、高亮边缘光晕、几何切削、图标排版与中心核圆贴图。",
			[LanguageCode.ZhTw] = "自由配置輪盤視覺風格、配色方案、高亮邊緣光暈、幾何切削、圖示排版與中心核圓貼圖。",
			[LanguageCode.En] = "Customize visual styles, color palettes, highlight glow, geometry shapes, typography, and core image.",
			[LanguageCode.Ja] = "ビジュアルスタイル、配色テーマ、グロー発光、幾何学形状、アイコン配置、コアバッジをカスタマイズします。"
		};
		dictionary["StyleTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "轮盘视觉风格",
			[LanguageCode.ZhTw] = "輪盤視覺風格",
			[LanguageCode.En] = "Visual Renderer Style",
			[LanguageCode.Ja] = "ビジュアルレンダラー"
		};
		dictionary["StyleGlass"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "液态毛玻璃",
			[LanguageCode.ZhTw] = "液態毛玻璃",
			[LanguageCode.En] = "Liquid Glassmorphism",
			[LanguageCode.Ja] = "リキッドグラスモーフィズム"
		};
		dictionary["StyleClassic"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "经典圆环",
			[LanguageCode.ZhTw] = "經典圓環",
			[LanguageCode.En] = "Classic Ring",
			[LanguageCode.Ja] = "クラシックリング"
		};
		dictionary["StyleClean"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "悬浮扇区",
			[LanguageCode.ZhTw] = "懸浮扇區",
			[LanguageCode.En] = "Clean Sectors",
			[LanguageCode.Ja] = "クリーンセクター"
		};
		dictionary["StyleCatPaw"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "萌宠猫爪",
			[LanguageCode.ZhTw] = "萌寵貓爪",
			[LanguageCode.En] = "Cute Cat Paw",
			[LanguageCode.Ja] = "キュートキャットポー (肉球)"
		};
		dictionary["ThemeTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "轮盘配色方案",
			[LanguageCode.ZhTw] = "輪盤配色方案",
			[LanguageCode.En] = "Wheel Color Palette",
			[LanguageCode.Ja] = "ホイール配色パレット"
		};
		dictionary["BtnDeletePreset"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🗑️ 删除预设",
			[LanguageCode.ZhTw] = "🗑️ 刪除預設",
			[LanguageCode.En] = "🗑️ Delete Preset",
			[LanguageCode.Ja] = "🗑️ プリセット削除"
		};
		dictionary["GlowTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "高亮边缘光晕",
			[LanguageCode.ZhTw] = "高亮邊緣光暈",
			[LanguageCode.En] = "Highlight Edge Glow",
			[LanguageCode.Ja] = "ハイライトエッジグロー発光"
		};
		dictionary["GlowFollowTheme"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "跟随主题高亮色",
			[LanguageCode.ZhTw] = "跟隨主題高亮色",
			[LanguageCode.En] = "Follow Theme",
			[LanguageCode.Ja] = "テーマ連動"
		};
		dictionary["GlowRadius"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "光晕半径",
			[LanguageCode.ZhTw] = "光暈半徑",
			[LanguageCode.En] = "Glow Radius",
			[LanguageCode.Ja] = "グロー拡散半径"
		};
		dictionary["GlowOpacity"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "光晕不透明度",
			[LanguageCode.ZhTw] = "光暈不透明度",
			[LanguageCode.En] = "Glow Opacity",
			[LanguageCode.Ja] = "グロー不透明度"
		};
		dictionary["GeometryTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "形态与尺寸",
			[LanguageCode.ZhTw] = "形態與尺寸",
			[LanguageCode.En] = "Geometry & Dimensions",
			[LanguageCode.Ja] = "幾何学形状とサイズ"
		};
		dictionary["ShapeOriginal"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "原生扇区",
			[LanguageCode.ZhTw] = "原生扇區",
			[LanguageCode.En] = "Original Sector",
			[LanguageCode.Ja] = "オリジナルセクター"
		};
		dictionary["ShapeCircle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "极简圆形",
			[LanguageCode.ZhTw] = "極簡圓形",
			[LanguageCode.En] = "Floating Circle",
			[LanguageCode.Ja] = "フローティングサークル"
		};
		dictionary["ShapeRounded"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "平滑圆角",
			[LanguageCode.ZhTw] = "平滑圓角",
			[LanguageCode.En] = "Rounded Fillet",
			[LanguageCode.Ja] = "角丸フィレット"
		};
		dictionary["ShapeCapsule"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "圆润胶囊",
			[LanguageCode.ZhTw] = "圓潤膠囊",
			[LanguageCode.En] = "Pill Capsules",
			[LanguageCode.Ja] = "ピルカプセル"
		};
		dictionary["ShapeHexagon"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "蜂巢六边形",
			[LanguageCode.ZhTw] = "蜂巢六邊形",
			[LanguageCode.En] = "Hexagon Hive",
			[LanguageCode.Ja] = "ヘキサゴンハニカム"
		};
		dictionary["RadiusOuter"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "轮盘外径",
			[LanguageCode.ZhTw] = "輪盤外徑",
			[LanguageCode.En] = "Outer Radius",
			[LanguageCode.Ja] = "外側半径"
		};
		dictionary["RadiusInner"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "轮盘内径",
			[LanguageCode.ZhTw] = "輪盤內徑",
			[LanguageCode.En] = "Inner Radius",
			[LanguageCode.Ja] = "内側半径"
		};
		dictionary["RadiusCore"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "中心圆半径",
			[LanguageCode.ZhTw] = "中心圓半徑",
			[LanguageCode.En] = "Core Radius",
			[LanguageCode.Ja] = "コア半径"
		};
		dictionary["SectorGap"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "扇区缝隙",
			[LanguageCode.ZhTw] = "扇區縫隙",
			[LanguageCode.En] = "Sector Gap",
			[LanguageCode.Ja] = "セクター間隔"
		};
		dictionary["SectorCornerRadius"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "扇区圆角",
			[LanguageCode.ZhTw] = "扇區圓角",
			[LanguageCode.En] = "Corner Radius",
			[LanguageCode.Ja] = "角丸半径"
		};
		dictionary["BtnResetGeometry"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "重置形态默认值",
			[LanguageCode.ZhTw] = "重設形態預設值",
			[LanguageCode.En] = "Reset Geometry Defaults",
			[LanguageCode.Ja] = "形状初期値に戻す"
		};
		dictionary["IconLayoutTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "图标与文字排版",
			[LanguageCode.ZhTw] = "圖示與文字排版",
			[LanguageCode.En] = "Layout & Typography",
			[LanguageCode.Ja] = "レイアウトと文字"
		};
		dictionary["LayoutIconText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "图文并茂",
			[LanguageCode.ZhTw] = "圖文並茂",
			[LanguageCode.En] = "Icon & Text",
			[LanguageCode.Ja] = "アイコン＋文字"
		};
		dictionary["LayoutIconOnly"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "仅显示图标",
			[LanguageCode.ZhTw] = "僅顯示圖示",
			[LanguageCode.En] = "Icon Only",
			[LanguageCode.Ja] = "アイコンのみ"
		};
		dictionary["LayoutTextOnly"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "仅显示文字",
			[LanguageCode.ZhTw] = "僅顯示文字",
			[LanguageCode.En] = "Text Only",
			[LanguageCode.Ja] = "文字のみ"
		};
		dictionary["ShowSectorActionText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "在轮盘扇区中显示动作名称文字",
			[LanguageCode.ZhTw] = "在輪盤扇區中顯示動作名稱文字",
			[LanguageCode.En] = "Show action names in wheel sectors",
			[LanguageCode.Ja] = "ホイールの扇形にアクション名を表示"
		};
		dictionary["ShowSelectedActionText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "选中扇区时在中心显示动作名称",
			[LanguageCode.ZhTw] = "選取扇區時在中心顯示動作名稱",
			[LanguageCode.En] = "Show selected action name in the center",
			[LanguageCode.Ja] = "選択中のアクション名を中央に表示"
		};
		dictionary["SectorIconSize"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "图标大小",
			[LanguageCode.ZhTw] = "圖示大小",
			[LanguageCode.En] = "Icon Size",
			[LanguageCode.Ja] = "アイコンサイズ"
		};
		dictionary["SectorFontSize"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "文字字号",
			[LanguageCode.ZhTw] = "文字字級",
			[LanguageCode.En] = "Font Size",
			[LanguageCode.Ja] = "文字サイズ"
		};
		dictionary["LayoutTargetGlobal"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🌐 全局统一排版",
			[LanguageCode.ZhTw] = "🌐 全域統一排版",
			[LanguageCode.En] = "Global Layout",
			[LanguageCode.Ja] = "全体一括設定"
		};
		dictionary["LayoutTargetSlot"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🎯 扇区独立定制",
			[LanguageCode.ZhTw] = "🎯 扇區獨立自訂",
			[LanguageCode.En] = "Slot Custom",
			[LanguageCode.Ja] = "個別カスタマイズ"
		};
		dictionary["EnableSlotCustomLayout"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚡ 启用该扇区独立个性化排版",
			[LanguageCode.ZhTw] = "⚡ 啟用該扇區獨立個性化排版",
			[LanguageCode.En] = "Enable custom styling for this slot",
			[LanguageCode.Ja] = "このセクターの個別スタイルを有効化"
		};
		dictionary["ResetToGlobalLayout"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔄 恢复继承全局默认",
			[LanguageCode.ZhTw] = "🔄 恢復繼承全域預設",
			[LanguageCode.En] = "Reset to Global Default",
			[LanguageCode.Ja] = "グローバル設定に戻す"
		};
		dictionary["SectorTextColor"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "轮盘文字颜色:",
			[LanguageCode.ZhTw] = "輪盤文字顏色:",
			[LanguageCode.En] = "Sector Text Color:",
			[LanguageCode.Ja] = "ホイール文字色:"
		};
		dictionary["CoreTextOptions"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "中心文字",
			[LanguageCode.ZhTw] = "中心文字",
			[LanguageCode.En] = "Center Text & Selection Options",
			[LanguageCode.Ja] = "中央テキストと選択時表示"
		};
		dictionary["CoreFontFamily"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "中心文字字体:",
			[LanguageCode.ZhTw] = "中心文字字型:",
			[LanguageCode.En] = "Center Font Family:",
			[LanguageCode.Ja] = "中央フォント:"
		};
		dictionary["SettingsUiScale"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔍 界面缩放",
			[LanguageCode.ZhTw] = "🔍 介面縮放",
			[LanguageCode.En] = "🔍 UI Scale",
			[LanguageCode.Ja] = "🔍 表示拡大率"
		};
		dictionary["SettingsUiScaleTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "调节设置控制台界面整体缩放比例 (80% ~ 200%)，高分屏下可放大文字与控件。窗口尺寸保持不变，内容变大后由页面滚动承接；也可随时使用 Ctrl + / Ctrl - 调节，Ctrl 0 复位。",
			[LanguageCode.ZhTw] = "調整設定控制台介面整體縮放比例 (80% ~ 200%)，高解析度螢幕下可放大文字與控件。視窗尺寸保持不變，內容變大後由頁面捲動承接；亦可隨時使用 Ctrl + / Ctrl - 調整，Ctrl 0 復位。",
			[LanguageCode.En] = "Scale the whole settings console between 80% and 200% for better readability on high-resolution screens. The window size stays untouched - enlarged content simply scrolls. Ctrl + / Ctrl - adjusts it anytime, and Ctrl 0 resets.",
			[LanguageCode.Ja] = "設定画面全体の表示拡大率を 80%〜200% で調整できます。高解像度画面での文字・控件の視認性向上に。ウィンドウサイズは変更されず、拡大した内容はスクロールして表示します。Ctrl + / Ctrl - ですぐに調整、Ctrl 0 でリセット。"
		};
		dictionary["CoreFontSize"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "中心文字大小:",
			[LanguageCode.ZhTw] = "中心文字大小:",
			[LanguageCode.En] = "Center Font Size:",
			[LanguageCode.Ja] = "中央フォントサイズ:"
		};
		dictionary["CoreTextColorAuto"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "自动适应配色主题",
			[LanguageCode.ZhTw] = "自動適應配色主題",
			[LanguageCode.En] = "Auto Contrast",
			[LanguageCode.Ja] = "配色テーマに自動追従"
		};
		dictionary["CoreTextColor"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "中心文字颜色:",
			[LanguageCode.ZhTw] = "中心文字顏色:",
			[LanguageCode.En] = "Center Text Color:",
			[LanguageCode.Ja] = "中央文字色:"
		};
		dictionary["InheritGlobal"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "跟随全局默认",
			[LanguageCode.ZhTw] = "跟隨全域預設",
			[LanguageCode.En] = "Inherit Global Default",
			[LanguageCode.Ja] = "グローバルデフォルトを継承"
		};
		dictionary["CoreTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "中心图标设置",
			[LanguageCode.ZhTw] = "中心圖示設定",
			[LanguageCode.En] = "Center Core Customization",
			[LanguageCode.Ja] = "中央コアのカスタマイズ"
		};
		dictionary["CoreShowIcon"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "显示中心图案 / 贴图",
			[LanguageCode.ZhTw] = "顯示中心圖案 / 貼圖",
			[LanguageCode.En] = "Show Core Icon / Image",
			[LanguageCode.Ja] = "中央アイコン/画像を表示"
		};
		dictionary["CoreIconType"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "核圆图案模式",
			[LanguageCode.ZhTw] = "核圓圖案模式",
			[LanguageCode.En] = "Core Pattern Mode",
			[LanguageCode.Ja] = "コアパターンモード"
		};
		dictionary["CorePatternExit"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "取消手势图标",
			[LanguageCode.ZhTw] = "取消手勢圖示",
			[LanguageCode.En] = "Cancel Cross",
			[LanguageCode.Ja] = "キャンセルバツ"
		};
		dictionary["CorePatternCrosshair"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "精准准心",
			[LanguageCode.ZhTw] = "精準準心",
			[LanguageCode.En] = "Crosshair",
			[LanguageCode.Ja] = "照準レティクル"
		};
		dictionary["CorePatternWindows"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "Windows 微标",
			[LanguageCode.ZhTw] = "Windows 微標",
			[LanguageCode.En] = "Windows Emblem",
			[LanguageCode.Ja] = "Windows ロゴ"
		};
		dictionary["CorePatternDot"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "极简圆点",
			[LanguageCode.ZhTw] = "極簡圓點",
			[LanguageCode.En] = "Minimal Dot",
			[LanguageCode.Ja] = "ミニマルドット"
		};
		dictionary["CorePatternHome"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "主页图标",
			[LanguageCode.ZhTw] = "首頁圖示",
			[LanguageCode.En] = "Home",
			[LanguageCode.Ja] = "ホーム"
		};
		dictionary["CorePatternPower"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "电源图标",
			[LanguageCode.ZhTw] = "電源圖示",
			[LanguageCode.En] = "Power",
			[LanguageCode.Ja] = "電源"
		};
		dictionary["CorePatternCompass"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "星空罗盘",
			[LanguageCode.ZhTw] = "星空羅盤",
			[LanguageCode.En] = "Compass",
			[LanguageCode.Ja] = "コンパス"
		};
		dictionary["CorePatternCatPaw"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "萌宠猫爪",
			[LanguageCode.ZhTw] = "萌寵貓爪",
			[LanguageCode.En] = "Cat Paw",
			[LanguageCode.Ja] = "肉球"
		};
		dictionary["CorePatternImage"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\uddbc\ufe0f 自定义本地图片贴图...",
			[LanguageCode.ZhTw] = "\ud83d\uddbc\ufe0f 自訂本機圖片貼圖...",
			[LanguageCode.En] = "\ud83d\uddbc\ufe0f Custom Local Image...",
			[LanguageCode.Ja] = "\ud83d\uddbc\ufe0f カスタム画像ファイル..."
		};
		dictionary["BtnBrowseImage"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "浏览选择图片",
			[LanguageCode.ZhTw] = "瀏覽選擇圖片",
			[LanguageCode.En] = "Browse Image",
			[LanguageCode.Ja] = "画像を選択"
		};
		dictionary["ConsoleThemeTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "软件主题",
			[LanguageCode.ZhTw] = "軟體主題",
			[LanguageCode.En] = "Console Theme",
			[LanguageCode.Ja] = "コントロールパネルテーマ"
		};
		dictionary["ThemeCustom"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83c\udfa8 自定义配色",
			[LanguageCode.ZhTw] = "\ud83c\udfa8 自定義配色",
			[LanguageCode.En] = "\ud83c\udfa8 Custom Colors",
			[LanguageCode.Ja] = "\ud83c\udfa8 カスタム配色"
		};
		dictionary["ThemeSystem"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\udda5\ufe0f 跟随 Windows 系统",
			[LanguageCode.ZhTw] = "\ud83d\udda5\ufe0f 跟隨 Windows 系統",
			[LanguageCode.En] = "\ud83d\udda5\ufe0f Follow Windows System",
			[LanguageCode.Ja] = "\ud83d\udda5\ufe0f Windows システムに従う"
		};
		dictionary["ThemeLight"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "☀\ufe0f 极简纯白",
			[LanguageCode.ZhTw] = "☀\ufe0f 極簡純白",
			[LanguageCode.En] = "☀\ufe0f Pure Light",
			[LanguageCode.Ja] = "☀\ufe0f ピュアライト"
		};
		dictionary["ThemeDark"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83c\udf19 极夜曜黑",
			[LanguageCode.ZhTw] = "\ud83c\udf19 極夜曜黑",
			[LanguageCode.En] = "\ud83c\udf19 OLED Dark",
			[LanguageCode.Ja] = "\ud83c\udf19 OLEDダーク"
		};
		dictionary["ThemeNavy"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83c\udf0c 午夜深蓝",
			[LanguageCode.ZhTw] = "\ud83c\udf0c 午夜深藍",
			[LanguageCode.En] = "\ud83c\udf0c Midnight Navy",
			[LanguageCode.Ja] = "\ud83c\udf0c ミッドナイトネイビー"
		};
		dictionary["ThemeViolet"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\udd2e 暗夜紫罗兰",
			[LanguageCode.ZhTw] = "\ud83d\udd2e 暗夜紫羅蘭",
			[LanguageCode.En] = "\ud83d\udd2e Royal Violet",
			[LanguageCode.Ja] = "\ud83d\udd2e ロイヤルバイオレット"
		};
		dictionary["ThemeGray"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚙\ufe0f 钛金深灰",
			[LanguageCode.ZhTw] = "⚙\ufe0f 鈦金深灰",
			[LanguageCode.En] = "⚙\ufe0f Titanium Gray",
			[LanguageCode.Ja] = "⚙\ufe0f チタングレー"
		};
		dictionary["GesturesHeader"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "手势动作设置",
			[LanguageCode.ZhTw] = "手勢動作設定",
			[LanguageCode.En] = "Gesture Sectors & Action Mappings",
			[LanguageCode.Ja] = "セクター配置とアクション設定"
		};
		dictionary["SectorCountTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "扇区按键数",
			[LanguageCode.ZhTw] = "扇區按鍵數",
			[LanguageCode.En] = "Sector Count",
			[LanguageCode.Ja] = "セクター数（キー数）"
		};
		dictionary["SectorCount4"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "4 键 (十字方位)",
			[LanguageCode.ZhTw] = "4 鍵 (十字方位)",
			[LanguageCode.En] = "4 Sectors (Cross 4-Way)",
			[LanguageCode.Ja] = "4キー (十字方向)"
		};
		dictionary["SectorCount8"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "8 键 (标准八向)",
			[LanguageCode.ZhTw] = "8 鍵 (標準八向)",
			[LanguageCode.En] = "8 Sectors (Standard 8-Way)",
			[LanguageCode.Ja] = "8キー (全方向8方位)"
		};
		dictionary["SectorCount12"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "12 键 (时钟十二向)",
			[LanguageCode.ZhTw] = "12 鍵 (時鐘十二向)",
			[LanguageCode.En] = "12 Sectors (Clock Dial 12-Way)",
			[LanguageCode.Ja] = "12キー (時計盤12方位)"
		};
		dictionary["ActionTypeHotkey"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⌨\ufe0f 键盘快捷键",
			[LanguageCode.ZhTw] = "⌨\ufe0f 鍵盤快速鍵",
			[LanguageCode.En] = "⌨\ufe0f Keyboard Hotkey",
			[LanguageCode.Ja] = "⌨\ufe0f キーボードショートカット"
		};
		dictionary["ActionTypeLaunch"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\ude80 启动程序/打开网页",
			[LanguageCode.ZhTw] = "\ud83d\ude80 啟動程式/開啟網頁",
			[LanguageCode.En] = "\ud83d\ude80 Launch App / Open URL",
			[LanguageCode.Ja] = "\ud83d\ude80 アプリ起動 / Webを開く"
		};
		dictionary["ActionTypeSystem"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚙\ufe0f 系统控制指令",
			[LanguageCode.ZhTw] = "⚙\ufe0f 系統控制指令",
			[LanguageCode.En] = "⚙\ufe0f System Action",
			[LanguageCode.Ja] = "⚙\ufe0f システム制御コマンド"
		};
		dictionary["BtnRecordHotkey"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "点击录制热键",
			[LanguageCode.ZhTw] = "點擊錄製快速鍵",
			[LanguageCode.En] = "Click to Record Hotkey",
			[LanguageCode.Ja] = "クリックしてショートカット録画"
		};
		dictionary["BtnBrowseApp"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\udd0d 选择应用程序...",
			[LanguageCode.ZhTw] = "\ud83d\udd0d 選擇應用程式...",
			[LanguageCode.En] = "\ud83d\udd0d Select Application...",
			[LanguageCode.Ja] = "\ud83d\udd0d アプリケーションを選択..."
		};
		dictionary["AdvancedHeader"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "系统集成与高级偏好设置",
			[LanguageCode.ZhTw] = "系統整合與進階偏好設定",
			[LanguageCode.En] = "System Integration & Preferences",
			[LanguageCode.Ja] = "システム統合と高度な設定"
		};
		dictionary["LanguageTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "界面语言 (Display Language)",
			[LanguageCode.ZhTw] = "介面語言 (Display Language)",
			[LanguageCode.En] = "Display Language",
			[LanguageCode.Ja] = "表示言語 (Display Language)"
		};
		dictionary["LanguageDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "选择软件控制台与轮盘的显示语言，支持即时热切换并自动保存。",
			[LanguageCode.ZhTw] = "選擇軟體控制台與輪盤的顯示語言，支援即時熱切換並自動儲存。",
			[LanguageCode.En] = "Select language for StarPie. Applies immediately without restarting.",
			[LanguageCode.Ja] = "StarPieの表示言語を選択します。再起動不要で即時に切り替わります。"
		};
		dictionary["ProgramPickerTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "选择程序",
			[LanguageCode.ZhTw] = "選擇程式",
			[LanguageCode.En] = "Select Program",
			[LanguageCode.Ja] = "プログラムを選択"
		};
		dictionary["ProgramPickerHeader"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "从已安装的软件和开始菜单中选择",
			[LanguageCode.ZhTw] = "從已安裝的軟體與開始功能表中選擇",
			[LanguageCode.En] = "Select from Installed Apps & Start Menu",
			[LanguageCode.Ja] = "インストール済みアプリやスタートメニューから選択"
		};
		dictionary["ProgramPickerPlaceholder"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "搜索软件名称、可执行文件或路径...",
			[LanguageCode.ZhTw] = "搜尋軟體名稱、執行檔或路徑...",
			[LanguageCode.En] = "Search app name, executable, or path...",
			[LanguageCode.Ja] = "アプリ名、実行可能ファイル、またはパスを検索..."
		};
		dictionary["ProgramPickerScanning"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "正在智能检索系统中已安装的软件，请稍候...",
			[LanguageCode.ZhTw] = "正在智慧檢索系統中已安裝的軟體，請稍候...",
			[LanguageCode.En] = "Scanning installed programs, please wait...",
			[LanguageCode.Ja] = "インストール済みアプリをスキャンしています..."
		};
		dictionary["BtnManualBrowse"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "手动浏览文件...",
			[LanguageCode.ZhTw] = "手動瀏覽檔案...",
			[LanguageCode.En] = "Browse File...",
			[LanguageCode.Ja] = "手動で参照..."
		};
		dictionary["LangZhCn"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83c\udde8\ud83c\uddf3 简体中文 (Simplified Chinese)",
			[LanguageCode.ZhTw] = "\ud83c\udde8\ud83c\uddf3 簡體中文 (Simplified Chinese)",
			[LanguageCode.En] = "\ud83c\udde8\ud83c\uddf3 简体中文 (Simplified Chinese)",
			[LanguageCode.Ja] = "\ud83c\udde8\ud83c\uddf3 簡体字中国語 (Simplified Chinese)"
		};
		dictionary["LangZhTw"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83c\udded\ud83c\uddf0/\ud83c\uddf9\ud83c\uddfc 繁體中文 (Traditional Chinese)",
			[LanguageCode.ZhTw] = "\ud83c\udded\ud83c\uddf0/\ud83c\uddf9\ud83c\uddfc 繁體中文 (Traditional Chinese)",
			[LanguageCode.En] = "\ud83c\udded\ud83c\uddf0/\ud83c\uddf9\ud83c\uddfc 繁體中文 (Traditional Chinese)",
			[LanguageCode.Ja] = "\ud83c\udded\ud83c\uddf0/\ud83c\uddf9\ud83c\uddfc 繁体字中国語 (Traditional Chinese)"
		};
		dictionary["LangEn"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83c\uddfa\ud83c\uddf8 English (US/UK)",
			[LanguageCode.ZhTw] = "\ud83c\uddfa\ud83c\uddf8 English (US/UK)",
			[LanguageCode.En] = "\ud83c\uddfa\ud83c\uddf8 English (US/UK)",
			[LanguageCode.Ja] = "\ud83c\uddfa\ud83c\uddf8 英語 (English)"
		};
		dictionary["LangJa"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83c\uddef\ud83c\uddf5 日本語 (Japanese)",
			[LanguageCode.ZhTw] = "\ud83c\uddef\ud83c\uddf5 日本語 (Japanese)",
			[LanguageCode.En] = "\ud83c\uddef\ud83c\uddf5 日本語 (Japanese)",
			[LanguageCode.Ja] = "\ud83c\uddef\ud83c\uddf5 日本語 (Japanese)"
		};
		dictionary["LangAuto"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\udda5\ufe0f 跟随系统 (System Default)",
			[LanguageCode.ZhTw] = "\ud83d\udda5\ufe0f 跟隨系統 (System Default)",
			[LanguageCode.En] = "\ud83d\udda5\ufe0f System Default",
			[LanguageCode.Ja] = "\ud83d\udda5\ufe0f システム既定 (System Default)"
		};
		dictionary["StartupTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "开机自启动",
			[LanguageCode.ZhTw] = "開機自啟動",
			[LanguageCode.En] = "Run on Windows Startup",
			[LanguageCode.Ja] = "Windows起動時に自動起動"
		};
		dictionary["StartupDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "在 Windows 开机登录时静默自启动并在后台托盘驻留。",
			[LanguageCode.ZhTw] = "在 Windows 開機登入時靜默自啟動並在後台托盤駐留。",
			[LanguageCode.En] = "Automatically start StarPie silently minimized to tray on login.",
			[LanguageCode.Ja] = "Windows起動時に自動でタスクトレイに常駐します。"
		};
		dictionary["ProgramPickerRefresh"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\udd04 刷新列表",
			[LanguageCode.ZhTw] = "\ud83d\udd04 重新整理",
			[LanguageCode.En] = "\ud83d\udd04 Refresh",
			[LanguageCode.Ja] = "\ud83d\udd04 更新"
		};
		dictionary["Tier1ConfigSegment"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\udd18 一级主轮盘配置",
			[LanguageCode.ZhTw] = "\ud83d\udd18 一級主輪盤配置",
			[LanguageCode.En] = "\ud83d\udd18 Tier 1 Primary Wheel",
			[LanguageCode.Ja] = "\ud83d\udd18 第1層メインホイール"
		};
		dictionary["Tier2ConfigSegment"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83c\udf1f 二级级联轮盘配置",
			[LanguageCode.ZhTw] = "\ud83c\udf1f 二級級聯輪盤配置",
			[LanguageCode.En] = "\ud83c\udf1f Tier 2 Sub-Wheel",
			[LanguageCode.Ja] = "\ud83c\udf1f 第2層カスケードホイール"
		};
		dictionary["SubWheelThemeTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "二级轮盘视觉风格",
			[LanguageCode.ZhTw] = "二級輪盤視覺風格",
			[LanguageCode.En] = "Tier 2 Visual Style & Colors",
			[LanguageCode.Ja] = "第2層ホイールのスタイルと配色"
		};
		dictionary["SubWheelThemeDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "为二级级联轮盘独立指定视觉渲染器与主题配色，与一级主轮盘自由组合。",
			[LanguageCode.ZhTw] = "為二級級聯輪盤獨立指定視覺渲染器與主題配色，與一級主輪盤自由組合。",
			[LanguageCode.En] = "Independently customize visual style and colors for the secondary cascading wheel.",
			[LanguageCode.Ja] = "第2層カスケードホイールに独自のスタイルと配色を設定します。"
		};
		dictionary["MemoryTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "内存深度整理",
			[LanguageCode.ZhTw] = "記憶體深度整理",
			[LanguageCode.En] = "Memory Optimization",
			[LanguageCode.Ja] = "メモリ最適化"
		};
		dictionary["MemoryDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "启用 Windows 进程工作集深度修剪，后台常驻内存低至 15~25MB。",
			[LanguageCode.ZhTw] = "啟用 Windows 行程工作集深度修剪，後台常駐記憶體低至 15~25MB。",
			[LanguageCode.En] = "Deep trims working set, keeping background RAM usage under 20MB.",
			[LanguageCode.Ja] = "メモリを自動トリムし、バックグラウンド使用量を15〜25MBに維持します。"
		};
		dictionary["BtnTrimMemory"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "立即压缩物理内存",
			[LanguageCode.ZhTw] = "立即壓縮實體記憶體",
			[LanguageCode.En] = "Trim RAM Now",
			[LanguageCode.Ja] = "今すぐメモリ圧縮"
		};
		dictionary["ElevateTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "管理员权限提升",
			[LanguageCode.ZhTw] = "系統管理員權限提升",
			[LanguageCode.En] = "Run as Administrator",
			[LanguageCode.Ja] = "管理者権限で実行"
		};
		dictionary["ElevateDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "以管理员身份重启，可在任务管理器、系统设置等高权限窗口中正常唤起手势。",
			[LanguageCode.ZhTw] = "以系統管理員身分重啟，可在工作管理員、系統設定等高權限視窗中正常呼出手勢。",
			[LanguageCode.En] = "Relaunch with administrator privileges to interact with elevated windows.",
			[LanguageCode.Ja] = "管理者権限で再起動し、タスクマネージャー等の高権限画面でも動作可能にします。"
		};
		dictionary["BtnElevate"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\udee1\ufe0f 以管理员身份重启",
			[LanguageCode.ZhTw] = "\ud83d\udee1\ufe0f 以系統管理員身分重啟",
			[LanguageCode.En] = "\ud83d\udee1\ufe0f Restart as Administrator",
			[LanguageCode.Ja] = "\ud83d\udee1\ufe0f 管理者として再起動"
		};
		dictionary["BackupTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "配置方案与备份",
			[LanguageCode.ZhTw] = "配置方案與備份",
			[LanguageCode.En] = "Configuration Profiles & Backup",
			[LanguageCode.Ja] = "設定プロファイルとバックアップ"
		};
		dictionary["BackupDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "管理多套独立配置方案（如建模CAD、日常办公、游戏娱乐等），随时一键热切换，并支持导入与导出外部配置。",
			[LanguageCode.ZhTw] = "管理多套獨立配置方案（如建模CAD、日常辦公、遊戲娛樂等），隨時一鍵熱切換，並支援匯入與匯出外部設定。",
			[LanguageCode.En] = "Manage multiple independent profiles (CAD, Office, Gaming, etc.) with instant hot-switching, import, and export.",
			[LanguageCode.Ja] = "複数の独立した設定プロファイル（CAD、オフィス、ゲームなど）を管理し、即時切り替え、インポート、エクスポートに対応します。"
		};
		dictionary["ActiveProfileLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "当前激活方案:",
			[LanguageCode.ZhTw] = "當前啟用方案:",
			[LanguageCode.En] = "Active Profile:",
			[LanguageCode.Ja] = "アクティブプロファイル:"
		};
		dictionary["BtnSaveNewProfile"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "➕ 保存为新方案",
			[LanguageCode.ZhTw] = "➕ 儲存為新方案",
			[LanguageCode.En] = "➕ Save as New Profile",
			[LanguageCode.Ja] = "➕ 新規保存"
		};
		dictionary["BtnRenameProfile"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "✏️ 重命名",
			[LanguageCode.ZhTw] = "✏️ 重新命名",
			[LanguageCode.En] = "✏️ Rename",
			[LanguageCode.Ja] = "✏️ 名前の変更"
		};
		dictionary["BtnDeleteProfile"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🗑️ 删除",
			[LanguageCode.ZhTw] = "🗑️ 刪除",
			[LanguageCode.En] = "🗑️ Delete",
			[LanguageCode.Ja] = "🗑️ 削除"
		};
		dictionary["BtnExportConfig"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "💾 导出选中配置...",
			[LanguageCode.ZhTw] = "💾 匯出所選配置...",
			[LanguageCode.En] = "💾 Export Selected...",
			[LanguageCode.Ja] = "💾 選択した設定をエクスポート..."
		};
		dictionary["BtnImportConfig"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "📂 导入外部配置...",
			[LanguageCode.ZhTw] = "📂 匯入外部設定檔...",
			[LanguageCode.En] = "📂 Import External...",
			[LanguageCode.Ja] = "📂 外部設定をインポート..."
		};
		dictionary["BtnResetConfig"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔄 恢复默认配置",
			[LanguageCode.ZhTw] = "🔄 恢復預設配置",
			[LanguageCode.En] = "🔄 Reset to Default",
			[LanguageCode.Ja] = "🔄 デフォルトにリセット"
		};
		dictionary["UpdateAdvancedToggleTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "更新偏好、下载加速与历史版本回退 (展开/折叠)",
			[LanguageCode.ZhTw] = "更新偏好、下載加速與歷史版本回退 (展開/折疊)",
			[LanguageCode.En] = "Update Preferences, Mirrors & Version Rollback (Expand/Collapse)",
			[LanguageCode.Ja] = "更新設定・ミラー・バージョンロールバック (展開/折りたたみ)"
		};
		dictionary["LogsTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "系统运行日志",
			[LanguageCode.ZhTw] = "系統運行日誌",
			[LanguageCode.En] = "System Runtime Logs",
			[LanguageCode.Ja] = "システム動作ログ"
		};
		dictionary["LogsDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "自动记录系统生命周期、手势分发、按键模拟与故障异常信息（保留最近7天），方便故障排查与问题反馈。",
			[LanguageCode.ZhTw] = "自動記錄系統生命週期、手勢分發、按鍵模擬與故障異常資訊（保留最近7天），方便故障排查與問題回饋。",
			[LanguageCode.En] = "Automatically records system lifecycle, gesture events, key simulations, and exceptions (retains 7 days) for diagnostics.",
			[LanguageCode.Ja] = "システムのライフサイクル、ジェスチャイベント、キーシミュレーション、例外を自動記録します（過去7日間保持）。"
		};
		dictionary["BtnOpenLogFolder"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\udcc2 打开日志目录",
			[LanguageCode.ZhTw] = "\ud83d\udcc2 開啟日誌目錄",
			[LanguageCode.En] = "\ud83d\udcc2 Open Log Folder",
			[LanguageCode.Ja] = "\ud83d\udcc2 ログフォルダーを開く"
		};
		dictionary["BtnViewTodayLog"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\udcc4 查看今日运行日志",
			[LanguageCode.ZhTw] = "\ud83d\udcc4 檢視今日運行日誌",
			[LanguageCode.En] = "\ud83d\udcc4 View Today's Log",
			[LanguageCode.Ja] = "\ud83d\udcc4 今日のログを表示"
		};
		dictionary["AboutHeader"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "关于 StarPie",
			[LanguageCode.ZhTw] = "關於 StarPie",
			[LanguageCode.En] = "About StarPie",
			[LanguageCode.Ja] = "StarPie について"
		};
		dictionary["AboutDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "高质感、极速现代 Windows 鼠标轮盘笔势工具",
			[LanguageCode.ZhTw] = "高質感、極速現代 Windows 滑鼠輪盤手勢工具",
			[LanguageCode.En] = "High-aesthetic, ultra-fast modern Windows mouse radial gestures tool.",
			[LanguageCode.Ja] = "洗練されたデザインと高速な応答性を誇る次世代マウスジェスチャーツール"
		};
		dictionary["BtnOpenChangelog"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "查看完整 CHANGELOG",
			[LanguageCode.ZhTw] = "檢視完整 CHANGELOG",
			[LanguageCode.En] = "View Full CHANGELOG",
			[LanguageCode.Ja] = "完全な更新履歴を表示"
		};
		dictionary["MilestonesTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "版本演进历程",
			[LanguageCode.ZhTw] = "版本演進歷程",
			[LanguageCode.En] = "Version Milestones",
			[LanguageCode.Ja] = "バージョン履歴"
		};
		dictionary["MsgSaveSuccess"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "设置已成功保存至硬盘！",
			[LanguageCode.ZhTw] = "設定已成功儲存至硬碟！",
			[LanguageCode.En] = "Settings successfully saved to disk!",
			[LanguageCode.Ja] = "設定が正常に保存されました！"
		};
		dictionary["MsgConfirmDeletePreset"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "确定要永久删除此自定义配色方案吗？\n删除后不可恢复。",
			[LanguageCode.ZhTw] = "確定要永久刪除此自訂配色方案嗎？\n刪除後不可恢復。",
			[LanguageCode.En] = "Are you sure you want to delete this custom color preset?\nThis cannot be undone.",
			[LanguageCode.Ja] = "このカスタム配色プリセットを削除してもよろしいですか？\n削除後は復元できません。"
		};
		dictionary["MsgConfirmReset"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "确定要恢复出厂默认设置吗？所有自定义手势与样式将被重置。",
			[LanguageCode.ZhTw] = "確定要恢復原廠預設設定嗎？所有自訂手勢與樣式將被重設。",
			[LanguageCode.En] = "Are you sure you want to restore factory defaults? All customizations will be reset.",
			[LanguageCode.Ja] = "工場出荷時の初期設定に戻してもよろしいですか？すべてのカスタム設定がリセットされます。"
		};
		dictionary["TrayPause"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⏸\ufe0f 暂停手势",
			[LanguageCode.ZhTw] = "⏸\ufe0f 暫停手勢",
			[LanguageCode.En] = "⏸\ufe0f Pause Gestures",
			[LanguageCode.Ja] = "⏸\ufe0f ジェスチャーを一時停止"
		};
		dictionary["TrayResume"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "▶\ufe0f 恢复手势",
			[LanguageCode.ZhTw] = "▶\ufe0f 恢復手勢",
			[LanguageCode.En] = "▶\ufe0f Resume Gestures",
			[LanguageCode.Ja] = "▶\ufe0f ジェスチャーを再開"
		};
		dictionary["TrayPreferences"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚙\ufe0f 偏好设置",
			[LanguageCode.ZhTw] = "⚙\ufe0f 偏好設定",
			[LanguageCode.En] = "⚙\ufe0f Preferences",
			[LanguageCode.Ja] = "⚙\ufe0f 設定"
		};
		dictionary["TrayAppearance"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83c\udfa8 外观样式",
			[LanguageCode.ZhTw] = "\ud83c\udfa8 外觀樣式",
			[LanguageCode.En] = "\ud83c\udfa8 Appearance",
			[LanguageCode.Ja] = "\ud83c\udfa8 外観"
		};
		dictionary["TrayGestures"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚡ 手势动作",
			[LanguageCode.ZhTw] = "⚡ 手勢動作",
			[LanguageCode.En] = "⚡ Gestures",
			[LanguageCode.Ja] = "⚡ ジェスチャー"
		};
		dictionary["TrayAbout"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\udccb 关于软件",
			[LanguageCode.ZhTw] = "\ud83d\udccb 關於軟體",
			[LanguageCode.En] = "\ud83d\udccb About",
			[LanguageCode.Ja] = "\ud83d\udccb 情報"
		};
		dictionary["TrayElevate"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\udee1\ufe0f 以管理员身份重启",
			[LanguageCode.ZhTw] = "\ud83d\udee1\ufe0f 以系統管理員身分重啟",
			[LanguageCode.En] = "\ud83d\udee1\ufe0f Restart as Administrator",
			[LanguageCode.Ja] = "\ud83d\udee1\ufe0f 管理者として再起動"
		};
		dictionary["TrayExit"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "❌ 退出 StarPie",
			[LanguageCode.ZhTw] = "❌ 退出 StarPie",
			[LanguageCode.En] = "❌ Exit StarPie",
			[LanguageCode.Ja] = "❌ StarPie を終了"
		};
		dictionary["TrayTooltip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "StarPie - 现代化鼠标轮盘笔势",
			[LanguageCode.ZhTw] = "StarPie - 現代化滑鼠輪盤手勢",
			[LanguageCode.En] = "StarPie - Modern Mouse Radial Gestures",
			[LanguageCode.Ja] = "StarPie - 次世代マウスラジアルジェスチャー"
		};
		dictionary["UpdateSectionTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "软件更新",
			[LanguageCode.ZhTw] = "軟體更新",
			[LanguageCode.En] = "Software Updates",
			[LanguageCode.Ja] = "ソフトウェア更新"
		};
		dictionary["UpdateStatusLatest"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "已是最新版本",
			[LanguageCode.ZhTw] = "已是最新版本",
			[LanguageCode.En] = "Up to Date",
			[LanguageCode.Ja] = "最新バージョンです"
		};
		dictionary["UpdateStatusNewVersion"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "发现新版本",
			[LanguageCode.ZhTw] = "發現新版本",
			[LanguageCode.En] = "Update Available",
			[LanguageCode.Ja] = "新しいバージョンがあります"
		};
		dictionary["BtnCheckUpdate"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔄 立即检查更新",
			[LanguageCode.ZhTw] = "🔄 立即檢查更新",
			[LanguageCode.En] = "🔄 Check Updates",
			[LanguageCode.Ja] = "🔄 更新を確認"
		};
		dictionary["BtnCheckingUpdate"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⏳ 正在检查...",
			[LanguageCode.ZhTw] = "⏳ 正在檢查...",
			[LanguageCode.En] = "⏳ Checking...",
			[LanguageCode.Ja] = "⏳ 確認中..."
		};
		dictionary["UpdateSilentCheckTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "开机静默检查更新",
			[LanguageCode.ZhTw] = "開機靜默檢查更新",
			[LanguageCode.En] = "Silent Check on Startup",
			[LanguageCode.Ja] = "起動時のサイレント更新確認"
		};
		dictionary["UpdateSilentCheckDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "程序启动后在后台静默查询 GitHub Releases",
			[LanguageCode.ZhTw] = "程式啟動後在後台靜默查詢 GitHub Releases",
			[LanguageCode.En] = "Silently query GitHub Releases in the background after launch",
			[LanguageCode.Ja] = "起動後にバックグラウンドで GitHub Releases をサイレント確認"
		};
		dictionary["UpdateChannelTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "更新推送通道",
			[LanguageCode.ZhTw] = "更新推送通道",
			[LanguageCode.En] = "Update Channel",
			[LanguageCode.Ja] = "更新チャンネル"
		};
		dictionary["UpdateChannelDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "选择稳定版或尝鲜版",
			[LanguageCode.ZhTw] = "選擇穩定版或嘗鮮版",
			[LanguageCode.En] = "Select Stable or Preview releases",
			[LanguageCode.Ja] = "安定版またはプレビュー版を選択"
		};
		dictionary["UpdateChannelStable"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🌟 正式稳定版",
			[LanguageCode.ZhTw] = "🌟 正式穩定版",
			[LanguageCode.En] = "🌟 Stable Release",
			[LanguageCode.Ja] = "🌟 安定版"
		};
		dictionary["UpdateChannelBeta"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🚀 尝鲜测试版",
			[LanguageCode.ZhTw] = "🚀 嘗鮮測試版",
			[LanguageCode.En] = "🚀 Preview / Beta",
			[LanguageCode.Ja] = "🚀 プレビュー版"
		};
		dictionary["UpdateProxyTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "GitHub 下载加速镜像源",
			[LanguageCode.ZhTw] = "GitHub 下載加速鏡像源",
			[LanguageCode.En] = "GitHub Download Mirror",
			[LanguageCode.Ja] = "GitHub ダウンロードミラー"
		};
		dictionary["UpdateProxyDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "解决国内访问 GitHub Releases 丢包或限速问题，自动加速代理下载",
			[LanguageCode.ZhTw] = "解決訪問 GitHub Releases 封包遺失或限速問題，自動加速代理下載",
			[LanguageCode.En] = "Accelerate GitHub Releases download and mitigate connection issues",
			[LanguageCode.Ja] = "GitHub Releases への接続を高速化し、プロキシ経由でダウンロード"
		};
		dictionary["UpdateProxyDirect"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🌐 官方直连",
			[LanguageCode.ZhTw] = "🌐 官方直連",
			[LanguageCode.En] = "🌐 Official Direct",
			[LanguageCode.Ja] = "🌐 公式ダイレクト"
		};
		dictionary["UpdateProxyGhproxy"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚡ 镜像源 1 (ghfast.top 推荐)",
			[LanguageCode.ZhTw] = "⚡ 鏡像源 1 (ghfast.top 推薦)",
			[LanguageCode.En] = "⚡ Mirror 1 (ghfast.top Recommended)",
			[LanguageCode.Ja] = "⚡ ミラー 1 (ghfast.top 推奨)"
		};
		dictionary["UpdateProxyMoeyy"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚡ 镜像源 2 (gh-proxy.com 备用)",
			[LanguageCode.ZhTw] = "⚡ 鏡像源 2 (gh-proxy.com 備用)",
			[LanguageCode.En] = "⚡ Mirror 2 (gh-proxy.com Backup)",
			[LanguageCode.Ja] = "⚡ ミラー 2 (gh-proxy.com 予備)"
		};
		dictionary["UpdateProxyAkams"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚡ 镜像源 3 (mirror.ghproxy.com)",
			[LanguageCode.ZhTw] = "⚡ 鏡像源 3 (mirror.ghproxy.com)",
			[LanguageCode.En] = "⚡ Mirror 3 (mirror.ghproxy.com)",
			[LanguageCode.Ja] = "⚡ ミラー 3 (mirror.ghproxy.com)"
		};
		dictionary["RollbackSectionTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "历史版本回退",
			[LanguageCode.ZhTw] = "歷史版本回退",
			[LanguageCode.En] = "Version Rollback",
			[LanguageCode.Ja] = "過去バージョンへのロールバック"
		};
		dictionary["RollbackSectionDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "若当前版本发生兼容性或配置异常，可选择历史版本一键覆盖回退安装。",
			[LanguageCode.ZhTw] = "若當前版本發生相容性或設定異常，可選擇歷史版本一鍵覆蓋回退安裝。",
			[LanguageCode.En] = "If compatibility issues occur, rollback to a prior version with one click.",
			[LanguageCode.Ja] = "互換性の問題が発生した場合は、ワンクリックで以前のバージョンにロールバックできます。"
		};
		dictionary["RollbackBadgeBeta"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🚀 测试版最多回退5个版本",
			[LanguageCode.ZhTw] = "🚀 測試版最多回退5個版本",
			[LanguageCode.En] = "🚀 Beta track: up to 5 versions",
			[LanguageCode.Ja] = "🚀 ベータ版：最大5バージョンまでロールバック可能"
		};
		dictionary["RollbackBadgeStable"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🌟 正式版最多回退2个版本",
			[LanguageCode.ZhTw] = "🌟 正式版最多回退2個版本",
			[LanguageCode.En] = "🌟 Stable track: up to 2 versions",
			[LanguageCode.Ja] = "🌟 安定版：最大2バージョンまでロールバック可能"
		};
		dictionary["BtnRollback"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⬇️ 回退至此版本",
			[LanguageCode.ZhTw] = "⬇️ 回退至此版本",
			[LanguageCode.En] = "⬇️ Rollback to this version",
			[LanguageCode.Ja] = "⬇️ このバージョンにロールバック"
		};
		dictionary["RollbackEmpty"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "（暂无可回退历史版本）",
			[LanguageCode.ZhTw] = "（暫無可回退歷史版本）",
			[LanguageCode.En] = "(No rollback versions available)",
			[LanguageCode.Ja] = "（ロールバック可能なバージョンはありません）"
		};
		dictionary["RollbackConfirmTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "确认版本回退",
			[LanguageCode.ZhTw] = "確認版本回退",
			[LanguageCode.En] = "Confirm Version Rollback",
			[LanguageCode.Ja] = "ロールバックの確認"
		};
		dictionary["RollbackConfirmMsg"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "确定要将 StarPie 回退至版本 {0} 吗？\n\n程序将下载该历史版本安装包并自动覆盖重启。您的自定义手势与按键配置将完整保留。",
			[LanguageCode.ZhTw] = "確定要將 StarPie 回退至版本 {0} 嗎？\n\n程式將下載該歷史版本安裝包並自動覆蓋重啟。您的自訂手勢與按鍵設定將完整保留。",
			[LanguageCode.En] = "Are you sure you want to rollback StarPie to version {0}?\n\nThe update package will be downloaded and safely applied upon restart. Your configurations will be preserved.",
			[LanguageCode.Ja] = "StarPie をバージョン {0} にロールバックしてもよろしいですか？\n\nパッケージをダウンロードして再起動時に適用されます。現在の設定は保持されます。"
		};
		dictionary["ContributorsHeader"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "开源贡献者致谢",
			[LanguageCode.ZhTw] = "開源貢獻者致謝",
			[LanguageCode.En] = "Contributors & Thanks",
			[LanguageCode.Ja] = "コントリビューターへの感謝"
		};
		dictionary["ContributorsIntro"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "感谢以下贡献者为 StarPie (星盘) 开源项目付出的智慧与贡献：",
			[LanguageCode.ZhTw] = "感謝以下貢獻者為 StarPie (星盤) 開源項目付出的智慧與貢獻：",
			[LanguageCode.En] = "Thank you to all contributors who empower StarPie open-source project:",
			[LanguageCode.Ja] = "StarPie オープンソースプロジェクトに貢献してくださった皆様に感謝いたします:"
		};
		dictionary["ContributorsSyncLocal"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🌐 本地收录名单 (检查更新时可联网刷新)",
			[LanguageCode.ZhTw] = "🌐 本地收錄名單 (檢查更新時可連網重新整理)",
			[LanguageCode.En] = "🌐 Local roster (refreshed when checking updates)",
			[LanguageCode.Ja] = "🌐 ローカル収録名簿 (更新確認時にオンライン更新)"
		};
		dictionary["ContributorsRefresh"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔄 刷新",
			[LanguageCode.ZhTw] = "🔄 重新整理",
			[LanguageCode.En] = "🔄 Refresh",
			[LanguageCode.Ja] = "🔄 更新"
		};
		dictionary["ContributorsRepo"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "★ 访问 GitHub 仓库",
			[LanguageCode.ZhTw] = "★ 造訪 GitHub 倉庫",
			[LanguageCode.En] = "★ Visit GitHub Repo",
			[LanguageCode.Ja] = "★ GitHub リポジトリへ"
		};
		dictionary["OcrCardTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "OCR 截屏文字识别与智能接口配置",
			[LanguageCode.ZhTw] = "OCR 截圖文字辨識與智慧介面配置",
			[LanguageCode.En] = "OCR Text Recognition & AI Model Setup",
			[LanguageCode.Ja] = "OCR 画面文字認識と AI モデル設定"
		};
		dictionary["OcrCardDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "管理原生离线识别引擎、多模态 AI 视觉模型 (OpenAI / 硅基流动 / Ollama) 与私有化 HTTP 接口端点与凭证。",
			[LanguageCode.ZhTw] = "管理原生離線辨識引擎、多模態 AI 視覺模型 (OpenAI / 矽基流動 / Ollama) 與私有化 HTTP 介面端點與憑證。",
			[LanguageCode.En] = "Manage native offline OCR engines, multimodal AI visual models, and custom HTTP endpoints.",
			[LanguageCode.Ja] = "ネイティブオフラインOCRエンジン、マルチモーダルAI、およびカスタムHTTPエンドポイントを管理。"
		};
		dictionary["OcrBadgeLocalEngine"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "Windows 本地离线引擎",
			[LanguageCode.ZhTw] = "Windows 本地離線引擎",
			[LanguageCode.En] = "Windows Local OCR",
			[LanguageCode.Ja] = "Windows ローカルOCR"
		};
		dictionary["BtnTestOcr"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "✂️ 截屏测试",
			[LanguageCode.ZhTw] = "✂️ 截圖測試",
			[LanguageCode.En] = "✂️ Test Snipping",
			[LanguageCode.Ja] = "✂️ 認識テスト"
		};
		dictionary["BtnConfigOcr"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚙️ 配置接口与模型...",
			[LanguageCode.ZhTw] = "⚙️ 配置介面與模型...",
			[LanguageCode.En] = "⚙️ Setup Engine & API...",
			[LanguageCode.Ja] = "⚙️ エンジンとAPI設定..."
		};
		dictionary["AutoStartAsAdminTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "以管理员身份开机自启 (推荐)",
			[LanguageCode.ZhTw] = "以系統管理員身分開機自啟 (推薦)",
			[LanguageCode.En] = "Run as Administrator on Startup (Recommended)",
			[LanguageCode.Ja] = "管理者として自動起動 (推奨)"
		};
		dictionary["AutoStartAsAdminDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "通过 Windows 任务计划程序实现最高权限静默自启，无需每次弹出 UAC 提示，可对所有高权限应用生效。",
			[LanguageCode.ZhTw] = "透過 Windows 工作排程器實現最高權限靜默自啟，無需每次跳出 UAC 提示，可對所有高權限應用程式生效。",
			[LanguageCode.En] = "Launch silently with elevated privileges via Windows Task Scheduler without UAC prompts, working across all admin apps.",
			[LanguageCode.Ja] = "Windows タスクスケジューラ経由でUACプロンプトなしに昇格起動し、管理者権限アプリでも確実に機能します。"
		};
		dictionary["CoreGlobalActions"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "全局动作",
			[LanguageCode.ZhTw] = "全域動作",
			[LanguageCode.En] = "Global Actions",
			[LanguageCode.Ja] = "グローバルアクション"
		};
		dictionary["CoreSectorActions"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "{0} 键动作",
			[LanguageCode.ZhTw] = "{0} 鍵動作",
			[LanguageCode.En] = "{0}-Slot Actions",
			[LanguageCode.Ja] = "{0}キー動作"
		};
		dictionary["DimensionsCardTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "轮盘尺寸与间距",
			[LanguageCode.ZhTw] = "輪盤尺寸與間距",
			[LanguageCode.En] = "Radial Geometry & Spacing",
			[LanguageCode.Ja] = "ラジアルの幾何学的寸法と間隔"
		};
		dictionary["VisualThemeCardTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "切削形态与配色",
			[LanguageCode.ZhTw] = "切削形態與配色",
			[LanguageCode.En] = "Cutout Shapes & Color Themes",
			[LanguageCode.Ja] = "カット形状とカラーテーマ"
		};
		dictionary["ClickSectorHint"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "💡 点击任意扇区可直接选中并在左侧微调该扇区的独立文字/图标排版",
			[LanguageCode.ZhTw] = "💡 點選任一扇區可直接選取並在左側微調該扇區的獨立文字/圖示排版",
			[LanguageCode.En] = "💡 Click any sector to select and customize its independent font/icon layout",
			[LanguageCode.Ja] = "💡 セクターをクリックして、フォントやアイコンの個別レイアウトを微調整できます"
		};
		dictionary["PreviewPanHint"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "按住 Ctrl + 鼠标拖动可平移画布",
			[LanguageCode.ZhTw] = "按住 Ctrl + 滑鼠拖曳可平移畫布",
			[LanguageCode.En] = "Hold Ctrl + Drag to pan canvas",
			[LanguageCode.Ja] = "Ctrl + ドラッグでキャンバスをパン"
		};
		dictionary["ConfigModeSimpleRadio"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "💡 简单模式",
			[LanguageCode.ZhTw] = "💡 簡單模式",
			[LanguageCode.En] = "💡 Simple Mode",
			[LanguageCode.Ja] = "💡 シンプルモード"
		};
		dictionary["ConfigModeProRadio"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚙️ 高级模式",
			[LanguageCode.ZhTw] = "⚙️ 高級模式",
			[LanguageCode.En] = "⚙️ Pro Mode",
			[LanguageCode.Ja] = "⚙️ プロモード"
		};
		dictionary["ConfigModeSimpleTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "简单模式",
			[LanguageCode.ZhTw] = "簡單模式",
			[LanguageCode.En] = "Simple Mode",
			[LanguageCode.Ja] = "シンプルモード"
		};
		dictionary["ConfigModeSimpleHint"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "已隐藏低频高级微调参数，保留核心极速配置体验",
			[LanguageCode.ZhTw] = "已隱藏低頻高級微調參數，保留核心極速設定體驗",
			[LanguageCode.En] = "Hidden low-frequency advanced parameters for a clean and focused experience",
			[LanguageCode.Ja] = "高度な設定項目を非表示にし、主要な設定に集中します"
		};
		dictionary["ConfigModeProTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "高级模式",
			[LanguageCode.ZhTw] = "高級模式",
			[LanguageCode.En] = "Pro Mode",
			[LanguageCode.Ja] = "プロモード"
		};
		dictionary["ConfigModeProHint"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "已全量开放运行命令、窗口管理、字体排版、平铺高级参数、OCR接口与系统日志等专家功能",
			[LanguageCode.ZhTw] = "已全量開放運行命令、視窗管理、字型排版、平鋪進階參數、OCR介面與系統日誌等專家功能",
			[LanguageCode.En] = "Full access to commands, window management, fonts, tiling parameters, OCR APIs and runtime logs",
			[LanguageCode.Ja] = "コマンド実行、ウィンドウ管理、フォント、分割詳細、OCR API、システムログなどの全機能を利用できます"
		};
		dictionary["SidebarModeSimple"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "💡 简单模式",
			[LanguageCode.ZhTw] = "💡 簡單模式",
			[LanguageCode.En] = "💡 Simple",
			[LanguageCode.Ja] = "💡 シンプル"
		};
		dictionary["SidebarModePro"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚙️ 高级模式",
			[LanguageCode.ZhTw] = "⚙️ 高級模式",
			[LanguageCode.En] = "⚙️ Pro Mode",
			[LanguageCode.Ja] = "⚙️ プロ"
		};
		dictionary["LayerIndicatorSectionTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "轮盘层数切换提示徽标",
			[LanguageCode.ZhTw] = "輪盤層數切換提示徽標",
			[LanguageCode.En] = "Layer Switch Indicator Badge",
			[LanguageCode.Ja] = "レイヤ切替インジケーター"
		};
		dictionary["LayerIndicatorSectionDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "呼出轮盘后，滑动滚轮或按快捷键切换多层轮盘时的浮动提示徽标外观与停留时长。",
			[LanguageCode.ZhTw] = "呼出輪盤後，滑動滾輪或按快捷鍵切換多層輪盤時的浮動提示徽標外觀與停留時長。",
			[LanguageCode.En] = "Appearance and fadeout duration of the floating layer badge when switching layers in Advanced Mode.",
			[LanguageCode.Ja] = "アドバンスモードでレイヤを切り替える際のフロートバッジの外観と表示時間。"
		};
		dictionary["ShowLayerIndicator"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "启用层数切换浮动提示徽标",
			[LanguageCode.ZhTw] = "啟用層數切換浮動提示徽標",
			[LanguageCode.En] = "Show Layer Switch Floating Badge",
			[LanguageCode.Ja] = "レイヤ切替フロートバッジを表示"
		};
		dictionary["LayerIndicatorStyleTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "徽标预设风格:",
			[LanguageCode.ZhTw] = "徽標預設風格:",
			[LanguageCode.En] = "Preset Style:",
			[LanguageCode.Ja] = "プリセットスタイル:"
		};
		dictionary["LayerIndicatorIconTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "提示前置图标:",
			[LanguageCode.ZhTw] = "提示前置圖示:",
			[LanguageCode.En] = "Indicator Icon:",
			[LanguageCode.Ja] = "インジケーターアイコン:"
		};
		dictionary["LayerIndicatorCornerRadiusTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "徽标圆角:",
			[LanguageCode.ZhTw] = "徽標圓角:",
			[LanguageCode.En] = "Badge Corner Radius:",
			[LanguageCode.Ja] = "バッジ角の丸み:"
		};
		dictionary["LayerIndicatorFontSizeTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "文字字号:",
			[LanguageCode.ZhTw] = "文字字型大小:",
			[LanguageCode.En] = "Font Size:",
			[LanguageCode.Ja] = "フォントサイズ:"
		};
		dictionary["LayerIndicatorOffsetYTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "垂直偏移:",
			[LanguageCode.ZhTw] = "垂直偏移:",
			[LanguageCode.En] = "Vertical Offset Y:",
			[LanguageCode.Ja] = "垂直オフセット Y:"
		};
		dictionary["LayerIndicatorDurationTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "淡出停留时间:",
			[LanguageCode.ZhTw] = "淡出停留時間:",
			[LanguageCode.En] = "Fadeout Duration:",
			[LanguageCode.Ja] = "表示時間:"
		};
		dictionary["BtnResetLayerIndicator"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔄 恢复默认提示徽标样式",
			[LanguageCode.ZhTw] = "🔄 恢復預設提示徽標樣式",
			[LanguageCode.En] = "🔄 Reset Indicator Style",
			[LanguageCode.Ja] = "🔄 インジケータースタイルを初期化"
		};
		dictionary["SoundEffectsTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "轮盘交互音效",
			[LanguageCode.ZhTw] = "輪盤互動音效",
			[LanguageCode.En] = "Radial Interaction Sound Effects",
			[LanguageCode.Ja] = "ホイール起動・操作サウンド効果"
		};
		dictionary["SoundEffectsDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "基于原生 Win32 非托管内存音频管线与极微波形合成，为轮盘唤醒、划过扇区、二级展开与触发确认提供毫秒级零延迟微动音效，建立盲操听觉闭环。",
			[LanguageCode.ZhTw] = "基於原生 Win32 非託管記憶體音訊管線與極微波形合成，為輪盤喚醒、劃過扇區、二級展開與觸發確認提供毫秒級零延遲微動音效，建立盲操聽覺閉環。",
			[LanguageCode.En] = "Powered by native Win32 memory audio pipeline & procedural synthesis, delivering sub-millisecond tactile audio feedback for popup, hover, expansion and execution.",
			[LanguageCode.Ja] = "Win32ネイティブ低遅延メモリオーディオにより、起動、ホバー、サブメニュー展開、実行、キャンセルの各操作に極微フィードバック音を提供します。"
		};
		dictionary["EnableSoundEffectsTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "开启轮盘交互音效 (推荐开启，建立盲操手感)",
			[LanguageCode.ZhTw] = "開啟輪盤互動音效 (推薦開啟，建立盲操手感)",
			[LanguageCode.En] = "Enable Sound Effects (Recommended for muscle memory)",
			[LanguageCode.Ja] = "ホイール操作サウンドを有効化（ブラインド操作に推奨）"
		};
		dictionary["EnableSoundEffectsSub"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "零延迟 < 2ms，常驻内存 < 20KB，不阻塞鼠标任何手势操作。",
			[LanguageCode.ZhTw] = "零延遲 < 2ms，常駐記憶體 < 20KB，不阻塞滑鼠任何手勢操作。",
			[LanguageCode.En] = "Zero latency (<2ms), ultra-low memory (<20KB), non-blocking.",
			[LanguageCode.Ja] = "超低遅延（2ms未満）、メモリ占有極小（20KB未満）、マウス操作を一切妨げません。"
		};
		dictionary["SoundThemeLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "音效主题",
			[LanguageCode.ZhTw] = "音效主題",
			[LanguageCode.En] = "Sound Theme",
			[LanguageCode.Ja] = "サウンドテーマ"
		};
		dictionary["SoundThemeDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "可选机械轴体敲击、现代手机触感微点、轻盈水滴气泡或极简脉冲短音。",
			[LanguageCode.ZhTw] = "可選機械軸體敲擊、現代手機觸感微點、輕盈水滴氣泡或極簡脈衝短音。",
			[LanguageCode.En] = "Choose between mechanical switch, modern tactile haptic, soft bubble, or minimalist blip.",
			[LanguageCode.Ja] = "メカニカル軸、現代風触覚クリック、ソフトバブル、ミニマルパルスから選択できます。"
		};
		dictionary["SoundVolumeTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "交互音量:",
			[LanguageCode.ZhTw] = "互動音量:",
			[LanguageCode.En] = "Feedback Volume:",
			[LanguageCode.Ja] = "効果音音量:"
		};
		dictionary["SoundVolumeDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "硬件级数学振幅无损缩放，完全独立于系统主音量，绝不修改 Windows 系统全局音量。",
			[LanguageCode.ZhTw] = "硬體級數學振幅無損縮放，完全獨立於系統主音量，絕不修改 Windows 系統全域音量。",
			[LanguageCode.En] = "Mathematical sample scaling completely independent of Windows master volume.",
			[LanguageCode.Ja] = "Windowsのマスター音量とは完全に独立した数学的振幅スケーリングを行います。"
		};
		dictionary["BtnSoundPreview"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔊 试听全套音效",
			[LanguageCode.ZhTw] = "🔊 試聽全套音效",
			[LanguageCode.En] = "🔊 Preview Sound Pack",
			[LanguageCode.Ja] = "🔊 サウンドを試聴"
		};
		dictionary["SoundSubEventsTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "细项事件独立开关",
			[LanguageCode.ZhTw] = "細項事件獨立開關",
			[LanguageCode.En] = "Independent Event Toggles",
			[LanguageCode.Ja] = "個別イベントのサウンド設定"
		};
		dictionary["SoundOnPopup"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "呼出轮盘",
			[LanguageCode.ZhTw] = "呼出輪盤",
			[LanguageCode.En] = "Menu Popup",
			[LanguageCode.Ja] = "ホイール起動"
		};
		dictionary["SoundOnHover"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "扇区切换/划过",
			[LanguageCode.ZhTw] = "扇區切換/劃過",
			[LanguageCode.En] = "Sector Hover",
			[LanguageCode.Ja] = "セクターホバー"
		};
		dictionary["SoundOnExpand"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "二级菜单展开",
			[LanguageCode.ZhTw] = "二級選單展開",
			[LanguageCode.En] = "Submenu Expand",
			[LanguageCode.Ja] = "サブメニュー展開"
		};
		dictionary["SoundOnExecute"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "动作执行确认",
			[LanguageCode.ZhTw] = "動作執行確認",
			[LanguageCode.En] = "Action Execute",
			[LanguageCode.Ja] = "アクション実行"
		};
		dictionary["SoundOnCancel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "顺势外甩/取消",
			[LanguageCode.ZhTw] = "順勢外甩/取消",
			[LanguageCode.En] = "Flick Cancel",
			[LanguageCode.Ja] = "キャンセル"
		};
		dictionary["CustomSoundConfigBtn"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🎛️ 方案配置",
			[LanguageCode.ZhTw] = "🎛️ 方案配置",
			[LanguageCode.En] = "🎛️ Configure Sound",
			[LanguageCode.Ja] = "🎛️ サウンド設定"
		};
		dictionary["CustomSoundStudioTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🎛️ 自定义交互音效调音台",
			[LanguageCode.ZhTw] = "🎛️ 自定義互動音效調音台",
			[LanguageCode.En] = "🎛️ Custom Sound Studio",
			[LanguageCode.Ja] = "🎛️ カスタムサウンドスタジオ"
		};
		Translations = dictionary;
	}
}
