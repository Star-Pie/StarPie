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
		if (code != null)
		{
			int length = code.Length;
			if (length != 2)
			{
				if (length != 5)
				{
					if (length == 7 && code == "zh-Hant")
					{
						goto IL_0169;
					}
				}
				else
				{
					switch (code[3])
					{
					case 'T':
						break;
					case 'H':
						goto IL_0100;
					case 'U':
						goto IL_010f;
					case 'G':
						goto IL_011e;
					case 'J':
						goto IL_012d;
					default:
						goto IL_0175;
					}
					if (code == "zh-TW")
					{
						goto IL_0169;
					}
				}
			}
			else
			{
				char c = code[0];
				if (c != 'e')
				{
					if (c == 'j' && code == "ja")
					{
						goto IL_0171;
					}
				}
				else if (code == "en")
				{
					goto IL_016d;
				}
			}
		}
		goto IL_0175;
		IL_012d:
		if (code == "ja-JP")
		{
			goto IL_0171;
		}
		goto IL_0175;
		IL_0100:
		if (code == "zh-HK")
		{
			goto IL_0169;
		}
		goto IL_0175;
		IL_010f:
		if (code == "en-US")
		{
			goto IL_016d;
		}
		goto IL_0175;
		IL_0169:
		LanguageCode currentLanguage = LanguageCode.ZhTw;
		goto IL_0177;
		IL_0175:
		currentLanguage = LanguageCode.ZhCn;
		goto IL_0177;
		IL_0171:
		currentLanguage = LanguageCode.Ja;
		goto IL_0177;
		IL_0177:
		CurrentLanguage = currentLanguage;
		return;
		IL_016d:
		currentLanguage = LanguageCode.En;
		goto IL_0177;
		IL_011e:
		if (code == "en-GB")
		{
			goto IL_016d;
		}
		goto IL_0175;
	}

	public static string T(string key)
	{
		return GetString(key);
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
		return key;
	}

	static I18n()
	{
		Dictionary<string, Dictionary<LanguageCode, string>> dictionary = new Dictionary<string, Dictionary<LanguageCode, string>>();
		dictionary["SidebarModeSimple"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "💡 简单模式",
			[LanguageCode.ZhTw] = "💡 簡易模式",
			[LanguageCode.En] = "💡 Simple Mode",
			[LanguageCode.Ja] = "💡 シンプルモード"
		};
		dictionary["SidebarModePro"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚙️ 高级全星模式",
			[LanguageCode.ZhTw] = "⚙️ 進階全星模式",
			[LanguageCode.En] = "⚙️ Pro Full Mode",
			[LanguageCode.Ja] = "⚙️ プロ全星モード"
		};
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
			[LanguageCode.ZhCn] = "启用多级轮盘与级联子菜单",
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
			[LanguageCode.ZhCn] = "唤出时直接同时展开一二级轮盘",
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
		dictionary["BlacklistTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "进程排除黑名单",
			[LanguageCode.ZhTw] = "處理程序排除黑名單",
			[LanguageCode.En] = "Process Exclusion Blacklist",
			[LanguageCode.Ja] = "プロセス除外ブラックリスト"
		};
		dictionary["BlacklistDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "在排除黑名单中的应用程序（如远程桌面、画图、3D建模软件）中，完全放行鼠标右键。",
			[LanguageCode.ZhTw] = "在排除黑名單中的應用程式（如遠端桌面、小畫家、3D建模軟體）中，完全放行滑鼠右鍵。",
			[LanguageCode.En] = "Bypass mouse gestures in blacklisted applications (e.g. Remote Desktop, Paint, CAD tools).",
			[LanguageCode.Ja] = "ブラックリストに登録されたアプリ（リモートデスクトップ、ペイントなど）ではジェスチャーを無効化します。"
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
		dictionary["BtnRenameProfile"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "✏\ufe0f 重命名当前配置",
			[LanguageCode.ZhTw] = "✏\ufe0f 重新命名當前配置",
			[LanguageCode.En] = "✏\ufe0f Rename Profile",
			[LanguageCode.Ja] = "✏\ufe0f 名前を変更"
		};
		dictionary["BtnDeleteProfile"] = new Dictionary<LanguageCode, string>
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
			[LanguageCode.ZhCn] = "触发设置",
			[LanguageCode.ZhTw] = "觸發設定",
			[LanguageCode.En] = "Triggers",
			[LanguageCode.Ja] = "トリガー設定"
		};
		dictionary["TabAppearance"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "外观样式",
			[LanguageCode.ZhTw] = "外觀樣式",
			[LanguageCode.En] = "Appearance",
			[LanguageCode.Ja] = "外観スタイル"
		};
		dictionary["TabGestures"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "手势动作",
			[LanguageCode.ZhTw] = "手勢動作",
			[LanguageCode.En] = "Gestures & Actions",
			[LanguageCode.Ja] = "ジェスチャー"
		};
		dictionary["TabAdvanced"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "系统设置",
			[LanguageCode.ZhTw] = "系統設定",
			[LanguageCode.En] = "System Settings",
			[LanguageCode.Ja] = "システム設定"
		};
		dictionary["TabAbout"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "关于软件",
			[LanguageCode.ZhTw] = "關於軟體",
			[LanguageCode.En] = "About StarPie",
			[LanguageCode.Ja] = "バージョン情報"
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
		dictionary["ClickSectorHint"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "💡 提示：在右侧画布中点击任意扇区可直接选中",
			[LanguageCode.ZhTw] = "💡 提示：在右側畫布中點選任意扇區可直接選取",
			[LanguageCode.En] = "Tip: Click any sector on the canvas to select",
			[LanguageCode.Ja] = "ヒント: キャンバス上の扇形をクリックして直接選択"
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
			[LanguageCode.ZhCn] = "界面语言",
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
		dictionary["AutoStartAsAdminTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "以管理员权限自启动 (推荐)",
			[LanguageCode.ZhTw] = "以系統管理員權限自啟動 (推薦)",
			[LanguageCode.En] = "Run with Administrator Privileges on Startup",
			[LanguageCode.Ja] = "管理者権限で自動起動 (推奨)"
		};
		dictionary["AutoStartAsAdminDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "通过 Windows 任务计划程序以最高权限静默自启，无需每次弹出 UAC，可在各类高权限窗口中正常响应手势。",
			[LanguageCode.ZhTw] = "透過 Windows 工作排程器以最高權限靜默自啟，無需每次彈出 UAC，可在各類高權限視窗中正常回應手勢。",
			[LanguageCode.En] = "Launches via Windows Task Scheduler with highest privileges without UAC prompt, ensuring gestures work in elevated windows.",
			[LanguageCode.Ja] = "Windowsタスクスケジューラを利用してUACなしで最高権限で自動起動し、管理者権限ウィンドウでも動作します。"
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

		dictionary["SidebarThemeSystem"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "系统",
			[LanguageCode.ZhTw] = "系統",
			[LanguageCode.En] = "System",
			[LanguageCode.Ja] = "システム"
		};
		dictionary["SidebarThemeLight"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "浅色",
			[LanguageCode.ZhTw] = "淺色",
			[LanguageCode.En] = "Light",
			[LanguageCode.Ja] = "ライト"
		};
		dictionary["SidebarThemeDark"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "曜黑",
			[LanguageCode.ZhTw] = "曜黑",
			[LanguageCode.En] = "Dark",
			[LanguageCode.Ja] = "ダーク"
		};
		dictionary["SidebarThemeGray"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "钛灰",
			[LanguageCode.ZhTw] = "鈦灰",
			[LanguageCode.En] = "Gray",
			[LanguageCode.Ja] = "グレー"
		};
		dictionary["SidebarThemeToggleTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "切换控制台界面主题 (点击循环切换)",
			[LanguageCode.ZhTw] = "切換控制台介面主題 (點擊循環切換)",
			[LanguageCode.En] = "Toggle Console Theme (Click to cycle)",
			[LanguageCode.Ja] = "コンソールテーマを切り替え（クリックで循環）"
		};
		dictionary["PARTClearShortcutTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "清空快捷键",
			[LanguageCode.ZhTw] = "清空快捷鍵",
			[LanguageCode.En] = "Clear shortcut",
			[LanguageCode.Ja] = "ショートカットをクリア"
		};
		dictionary["LiveSensorReadyTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "💡 硬件感知器已就绪：随时按下鼠标任意侧键、中键或键盘按键，此处将实时高亮反馈对应按键与键码。",
			[LanguageCode.ZhTw] = "💡 硬體感知器已就緒：隨時按下滑鼠任意側鍵、中鍵或鍵盤按鍵，此處將即時高亮反饋對應按鍵與鍵碼。",
			[LanguageCode.En] = "💡 Hardware sensor ready: Press any mouse button or key to instantly see live feedback and key codes.",
			[LanguageCode.Ja] = "💡 ハードウェアセンサー準備完了: マウスボタンやキーを押すと、リアルタイムでキーコードがハイライト表示されます。"
		};
		dictionary["TriggerThresholdTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "一级轮盘呼出触发位移:",
			[LanguageCode.ZhTw] = "一級輪盤呼出觸發位移:",
			[LanguageCode.En] = "Primary Wheel Popup Trigger Distance:",
			[LanguageCode.Ja] = "メインホイール起動トリガー移動量:"
		};
		dictionary["TriggerThresholdDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "按住触发键移动超过此距离后呼出手势轮盘。距离越小越灵敏，过小可能造成按键微抖误触。",
			[LanguageCode.ZhTw] = "按住觸發鍵移動超過此距離後呼出手勢輪盤。距離越小越靈敏，過小可能造成按鍵微抖誤觸。",
			[LanguageCode.En] = "Hold the trigger key and drag beyond this distance to open the wheel. Smaller values are more sensitive, but may cause jitter misclicks.",
			[LanguageCode.Ja] = "トリガーキーを押しながらこの距離以上ドラッグするとホイールを表示します。値が小さいほど高感度ですが、手のブレで誤作動しやすくなります。"
		};
		dictionary["CoreDeadzoneTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🎯 核心圆唤醒与死区灵敏度:",
			[LanguageCode.ZhTw] = "🎯 核心圓喚醒與死區靈敏度:",
			[LanguageCode.En] = "🎯 Center Deadzone & Activation Sensitivity:",
			[LanguageCode.Ja] = "🎯 センターデッドゾーンと起動感度:"
		};
		dictionary["CoreDeadzoneDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "调节呼出轮盘后光标停留在中心核心圆触发核心动作或静默取消的有效半径。数值较小时轻划即可命中扇区，数值较大时中心判定区更宽容，更易触发中心核圆动作或防手抖取消。",
			[LanguageCode.ZhTw] = "調節呼出輪盤後游標停留在中心核心圓觸發核心動作或靜默取消的有效半徑。數值較小時輕劃即可命中扇區，數值較大時中心判定區更寬容，更易觸發中心核圓動作或防手抖取消。",
			[LanguageCode.En] = "Effective radius to trigger the center core action or silently cancel. Smaller values select outer sectors easily; larger values provide a wider center safe zone to prevent jitter.",
			[LanguageCode.Ja] = "センター円で中央アクションまたはサイレントキャンセルをトリガーする有効半径。値が小さいと少しのスワイプでセクターを選択でき、値が大きいと中央の許容範囲が広がり手ブレを防止します。"
		};
		dictionary["MultiTierSectionTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "多级轮盘与级联子菜单",
			[LanguageCode.ZhTw] = "多級輪盤與級聯子選單",
			[LanguageCode.En] = "Multi-Tier Cascading Sub-Wheels",
			[LanguageCode.Ja] = "マルチ階層カスケードサブホイール"
		};
		dictionary["SubWheelTriggerDistLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "二级轮盘展开触发距离:",
			[LanguageCode.ZhTw] = "二級輪盤展開觸發距離:",
			[LanguageCode.En] = "Sub-Wheel Expansion Trigger Distance:",
			[LanguageCode.Ja] = "サブホイール展開トリガー距離:"
		};
		dictionary["SubWheelTriggerDistDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "调节光标划出距离中心多远时展开二级级联菜单。数值较小时轻划即可展开，数值较大时需向外划出更远距离才展开二级，防止快速触发一级动作时产生视觉干扰。",
			[LanguageCode.ZhTw] = "調節游標劃出距離中心多遠時展開二級級聯選單。數值較小時輕劃即可展開，數值較大時需向外劃出更遠距離才展開二級，防止快速觸發一級動作時產生視覺干擾。",
			[LanguageCode.En] = "Drag distance required from the center to expand sub-actions. Smaller values expand quickly; larger values avoid visual clutter during fast primary gestures.",
			[LanguageCode.Ja] = "中心からどれだけドラッグした時にサブメニューを展開するか設定します。小さい値では素早く展開し、大きい値ではメインアクション実行時の視覚的邪魔を防ぎます。"
		};
		dictionary["DirAuto"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "✨ 自动（运动反方向）",
			[LanguageCode.ZhTw] = "✨ 自動（運動反方向）",
			[LanguageCode.En] = "✨ Auto (Opposite Motion)",
			[LanguageCode.Ja] = "✨ 自動（移動の逆方向）"
		};
		dictionary["DirUp"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⬆ 上方",
			[LanguageCode.ZhTw] = "⬆ 上方",
			[LanguageCode.En] = "⬆ Up",
			[LanguageCode.Ja] = "⬆ 上"
		};
		dictionary["DirDown"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⬇ 下方",
			[LanguageCode.ZhTw] = "⬇ 下方",
			[LanguageCode.En] = "⬇ Down",
			[LanguageCode.Ja] = "⬇ 下"
		};
		dictionary["DirLeft"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⬅ 左方",
			[LanguageCode.ZhTw] = "⬅ 左方",
			[LanguageCode.En] = "⬅ Left",
			[LanguageCode.Ja] = "⬅ 左"
		};
		dictionary["DirRight"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "➡ 右方",
			[LanguageCode.ZhTw] = "➡ 右方",
			[LanguageCode.En] = "➡ Right",
			[LanguageCode.Ja] = "➡ 右"
		};
		dictionary["DirUpLeft"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "↖ 左上",
			[LanguageCode.ZhTw] = "↖ 左上",
			[LanguageCode.En] = "↖ Up-Left",
			[LanguageCode.Ja] = "↖ 左上"
		};
		dictionary["DirUpRight"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "↗ 右上",
			[LanguageCode.ZhTw] = "↗ 右上",
			[LanguageCode.En] = "↗ Up-Right",
			[LanguageCode.Ja] = "↗ 右上"
		};
		dictionary["DirDownLeft"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "↙ 左下",
			[LanguageCode.ZhTw] = "↙ 左下",
			[LanguageCode.En] = "↙ Down-Left",
			[LanguageCode.Ja] = "↙ 左下"
		};
		dictionary["DirDownRight"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "↘ 右下",
			[LanguageCode.ZhTw] = "↘ 右下",
			[LanguageCode.En] = "↘ Down-Right",
			[LanguageCode.Ja] = "↘ 右下"
		};
		dictionary["GestureMinSegmentTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "最小段长（像素）：越大越难把中途小拐弯误识别为方向段",
			[LanguageCode.ZhTw] = "最小段長（像素）：越大越難把中途小拐彎誤識別為方向段",
			[LanguageCode.En] = "Minimum segment length (px): Higher values prevent jitter turns from registering as direction strokes.",
			[LanguageCode.Ja] = "最小セグメント長（px）: 値が大きいほど、軌跡の微小な曲がりを誤検出にくくなります。"
		};
		dictionary["TipGestureAppPath"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "选择的应用程序路径",
			[LanguageCode.ZhTw] = "選擇的應用程式路徑",
			[LanguageCode.En] = "Selected application path",
			[LanguageCode.Ja] = "選択されたアプリのパス"
		};
		dictionary["TipGestureBrowseApp"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "选择应用程序或快捷方式...",
			[LanguageCode.ZhTw] = "選擇應用程式或捷徑...",
			[LanguageCode.En] = "Select application or shortcut...",
			[LanguageCode.Ja] = "アプリやショートカットを選択..."
		};
		dictionary["TipGestureFolderPath"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "选择的本地文件夹路径",
			[LanguageCode.ZhTw] = "選擇的本機資料夾路徑",
			[LanguageCode.En] = "Selected local folder path",
			[LanguageCode.Ja] = "選択されたフォルダパス"
		};
		dictionary["TipGestureBrowseFolder"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "选择本地文件夹...",
			[LanguageCode.ZhTw] = "選擇本機資料夾...",
			[LanguageCode.En] = "Select local folder...",
			[LanguageCode.Ja] = "ローカルフォルダを選択..."
		};
		dictionary["TipGestureCmd"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "要运行的命令，如 ping -n 3 127.0.0.1",
			[LanguageCode.ZhTw] = "要運行的命令，如 ping -n 3 127.0.0.1",
			[LanguageCode.En] = "Command to run, e.g. ping -n 3 127.0.0.1",
			[LanguageCode.Ja] = "実行するコマンド（例: ping -n 3 127.0.0.1）"
		};
		dictionary["TipGestureTaskbarSlot"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "任务栏第 N 个应用（同 Win+N 槽位语义）",
			[LanguageCode.ZhTw] = "工作列第 N 個應用（同 Win+N 槽位語義）",
			[LanguageCode.En] = "N-th taskbar app (equivalent to Win+N slot)",
			[LanguageCode.Ja] = "タスクバーの N 番目のアプリ (Win+N 相当)"
		};
		dictionary["TipGestureTilePreset"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "平铺布局预设",
			[LanguageCode.ZhTw] = "平鋪佈局預設",
			[LanguageCode.En] = "Tile layout preset",
			[LanguageCode.Ja] = "タイルレイアウトプリセット"
		};
		dictionary["TipGestureCustomName"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "名称（显示自定义，可留空）",
			[LanguageCode.ZhTw] = "名稱（顯示自訂，可留空）",
			[LanguageCode.En] = "Display name (optional)",
			[LanguageCode.Ja] = "表示名（任意、空欄可）"
		};
		dictionary["BtnTestGesture"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "测试",
			[LanguageCode.ZhTw] = "測試",
			[LanguageCode.En] = "Test",
			[LanguageCode.Ja] = "テスト"
		};
		dictionary["TipDeleteGesture"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "删除此手势映射",
			[LanguageCode.ZhTw] = "刪除此手勢映射",
			[LanguageCode.En] = "Delete this gesture mapping",
			[LanguageCode.Ja] = "このジェスチャー割り当てを削除"
		};
		dictionary["BtnAddGestureMapping"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "➕ 添加手势映射",
			[LanguageCode.ZhTw] = "➕ 新增手勢映射",
			[LanguageCode.En] = "➕ Add Gesture Mapping",
			[LanguageCode.Ja] = "➕ ジェスチャー割り当てを追加"
		};
		dictionary["AnimSpeedCustom"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🎛️ 自定义速度",
			[LanguageCode.ZhTw] = "🎛️ 自訂速度",
			[LanguageCode.En] = "🎛️ Custom Speed",
			[LanguageCode.Ja] = "🎛️ カスタム速度"
		};
		dictionary["SoundPresetMechanical"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚙️ 机械手感",
			[LanguageCode.ZhTw] = "⚙️ 機械手感",
			[LanguageCode.En] = "⚙️ Mechanical",
			[LanguageCode.Ja] = "⚙️ メカニカル"
		};
		dictionary["SoundPresetCrisp"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "✨ 现代清脆",
			[LanguageCode.ZhTw] = "✨ 現代清脆",
			[LanguageCode.En] = "✨ Modern Crisp",
			[LanguageCode.Ja] = "✨ クリスプモダン"
		};
		dictionary["SoundPresetBubble"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🫧 柔和气泡",
			[LanguageCode.ZhTw] = "🫧 柔和氣泡",
			[LanguageCode.En] = "🫧 Soft Bubble",
			[LanguageCode.Ja] = "🫧 ソフトバブル"
		};
		dictionary["SoundPresetShort"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚡ 极简短音",
			[LanguageCode.ZhTw] = "⚡ 極簡短音",
			[LanguageCode.En] = "⚡ Minimal Click",
			[LanguageCode.Ja] = "⚡ ミニマルショート"
		};
		dictionary["SoundPresetCustom"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🎛️ 自定义方案",
			[LanguageCode.ZhTw] = "🎛️ 自訂方案",
			[LanguageCode.En] = "🎛️ Custom Studio",
			[LanguageCode.Ja] = "🎛️ カスタムスタジオ"
		};
		dictionary["SoundMixerTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🎛️ 自定义交互音效调音台",
			[LanguageCode.ZhTw] = "🎛️ 自訂互動音效調音台",
			[LanguageCode.En] = "🎛️ Custom Interaction Sound Studio",
			[LanguageCode.Ja] = "🎛️ カスタム効果音ミキサー"
		};
		dictionary["SoundMixerBadge"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "原生支持",
			[LanguageCode.ZhTw] = "原生支援",
			[LanguageCode.En] = "Native",
			[LanguageCode.Ja] = "ネイティブ"
		};
		dictionary["SoundMixerDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "为 5 个核心交互手势事件单独调校程序化极微波形、本地音频采样与音高音量。",
			[LanguageCode.ZhTw] = "為 5 個核心互動手勢事件單獨調校程式化極微波形、本地音訊取樣與音高音量。",
			[LanguageCode.En] = "Fine-tune procedural micro-waveforms, local samples, pitch and volume across 5 core interaction events.",
			[LanguageCode.Ja] = "5つのコアジェスチャーイベントごとに微小波形、ローカル音声サンプル、ピッチ、音量を個別に調整できます。"
		};
		dictionary["BtnNewSoundProfile"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "➕ 新建",
			[LanguageCode.ZhTw] = "➕ 新建",
			[LanguageCode.En] = "➕ New",
			[LanguageCode.Ja] = "➕ 新規"
		};
		dictionary["TipNewSoundProfile"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "新建自定义方案",
			[LanguageCode.ZhTw] = "新建自訂方案",
			[LanguageCode.En] = "Create new custom sound profile",
			[LanguageCode.Ja] = "カスタム音効プロファイルを新規作成"
		};
		dictionary["BtnDeleteSoundProfile"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🗑️ 删除",
			[LanguageCode.ZhTw] = "🗑️ 刪除",
			[LanguageCode.En] = "🗑️ Delete",
			[LanguageCode.Ja] = "🗑️ 削除"
		};
		dictionary["TipDeleteSoundProfile"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "删除当前选中的自定义方案",
			[LanguageCode.ZhTw] = "刪除當前選中的自訂方案",
			[LanguageCode.En] = "Delete selected custom sound profile",
			[LanguageCode.Ja] = "選択したプロファイルを削除"
		};
		dictionary["BtnImportSoundProfile"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "📂 导入",
			[LanguageCode.ZhTw] = "📂 匯入",
			[LanguageCode.En] = "📂 Import",
			[LanguageCode.Ja] = "📂 インポート"
		};
		dictionary["TipImportSoundProfile"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "导入音效方案",
			[LanguageCode.ZhTw] = "匯入音效方案",
			[LanguageCode.En] = "Import sound profile",
			[LanguageCode.Ja] = "音効プロファイルをインポート"
		};
		dictionary["BtnExportSoundProfile"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "💾 导出",
			[LanguageCode.ZhTw] = "💾 匯出",
			[LanguageCode.En] = "💾 Export",
			[LanguageCode.Ja] = "💾 エクスポート"
		};
		dictionary["TipExportSoundProfile"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "导出当前方案",
			[LanguageCode.ZhTw] = "匯出當前方案",
			[LanguageCode.En] = "Export current sound profile",
			[LanguageCode.Ja] = "現在のプロファイルをエクスポート"
		};
		dictionary["BtnResetSoundProfile"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔄 重置",
			[LanguageCode.ZhTw] = "🔄 重設",
			[LanguageCode.En] = "🔄 Reset",
			[LanguageCode.Ja] = "🔄 初期化"
		};
		dictionary["TipResetSoundProfile"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "重置当前方案为预置默认值",
			[LanguageCode.ZhTw] = "重設當前方案為預設預設值",
			[LanguageCode.En] = "Reset profile to preset defaults",
			[LanguageCode.Ja] = "プロファイルを初期プリセットに戻す"
		};
		dictionary["BtnOpenSoundEditorWindow"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🎛️ 独立大窗",
			[LanguageCode.ZhTw] = "🎛️ 獨立大窗",
			[LanguageCode.En] = "🎛️ Studio Window",
			[LanguageCode.Ja] = "🎛️ 専用ウィンドウ"
		};
		dictionary["TipOpenSoundEditorWindow"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "打开独立大窗口精细调音台",
			[LanguageCode.ZhTw] = "開啟獨立大視窗精細調音台",
			[LanguageCode.En] = "Open standalone fine-tuning sound studio",
			[LanguageCode.Ja] = "独立した大画面サウンドスタジオを開く"
		};
		dictionary["SoundSelectProfileLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "选择配置方案:",
			[LanguageCode.ZhTw] = "選擇配置方案:",
			[LanguageCode.En] = "Select Sound Profile:",
			[LanguageCode.Ja] = "音効プロファイルを選択:"
		};
		dictionary["SoundPlayFlowBtn"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔊 连续模拟完整手势交互体验",
			[LanguageCode.ZhTw] = "🔊 連續模擬完整手勢互動體驗",
			[LanguageCode.En] = "🔊 Simulate Full Gesture Interaction Flow",
			[LanguageCode.Ja] = "🔊 ジェスチャー操作フロー全体を連続シミュレート"
		};
		dictionary["SoundPlayFlowTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "依次回放：唤出 ➔ 划过 ➔ 展开 ➔ 执行 ➔ 脱离",
			[LanguageCode.ZhTw] = "依次回放：喚出 ➔ 劃過 ➔ 展開 ➔ 執行 ➔ 脫離",
			[LanguageCode.En] = "Playback sequence: Popup ➔ Hover ➔ Expand ➔ Execute ➔ Escape",
			[LanguageCode.Ja] = "再生順: ポップアップ ➔ ホバー ➔ 展開 ➔ 実行 ➔ 脱出"
		};
		dictionary["SoundFlowReadyStatus"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "准备就绪",
			[LanguageCode.ZhTw] = "準備就緒",
			[LanguageCode.En] = "Ready",
			[LanguageCode.Ja] = "準備完了"
		};
		dictionary["SoundSynthNotice"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚡ 纯内存波形合成，零延迟 < 2ms，不占额外资源",
			[LanguageCode.ZhTw] = "⚡ 純記憶體波形合成，零延遲 < 2ms，不佔額外資源",
			[LanguageCode.En] = "⚡ In-memory procedural synthesis, zero latency < 2ms, zero bloat",
			[LanguageCode.Ja] = "⚡ メモリ内プロシージャル波形合成、超低遅延 < 2ms、リソース消費ゼロ"
		};
		dictionary["SystemAudioWarning"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚠️ 检测到 Windows 系统主音量当前为 0% 或已静音，会导致所有交互音效无声。",
			[LanguageCode.ZhTw] = "⚠️ 檢測到 Windows 系統主音量當前為 0% 或已靜音，會導致所有互動音效無聲。",
			[LanguageCode.En] = "⚠️ System master volume is muted or at 0%, causing interaction sound effects to be silent.",
			[LanguageCode.Ja] = "⚠️ システムのマスター音量がミュートまたは0%のため、効果音が聞こえません。"
		};
		dictionary["BtnRestoreSystemAudio"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔊 一键解除静音并恢复音量 (50%)",
			[LanguageCode.ZhTw] = "🔊 一鍵解除靜音並恢復音量 (50%)",
			[LanguageCode.En] = "🔊 Unmute & Restore Volume (50%)",
			[LanguageCode.Ja] = "🔊 ミュート解除して音量を復元 (50%)"
		};
		dictionary["OuterEscapeCheckboxDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "超过轮盘外圈范围后立即解除高亮，松开右键 0 误触安全放弃。",
			[LanguageCode.ZhTw] = "超過輪盤外圈範圍後立即解除高亮，放開右鍵 0 誤觸安全放棄。",
			[LanguageCode.En] = "De-highlights sectors when dragging outside the wheel; release safely without accidental triggers.",
			[LanguageCode.Ja] = "ホイール外枠を超えると選択を即座に解除し、右クリックを離しても誤作動なく安全に中止します。"
		};
		dictionary["OuterEscapeSilentNote"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "关闭则外甩取消仍为静默关闭。",
			[LanguageCode.ZhTw] = "關閉則外甩取消仍為靜默關閉。",
			[LanguageCode.En] = "When disabled, flick-out cancel silently closes the wheel.",
			[LanguageCode.Ja] = "無効の場合、外側スワイプによるキャンセルは静かにホイールを閉じます。"
		};
		dictionary["OuterEscapePresetsLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚡ 外甩常用预设:",
			[LanguageCode.ZhTw] = "⚡ 外甩常用預設:",
			[LanguageCode.En] = "⚡ Common Flick-Out Presets:",
			[LanguageCode.Ja] = "⚡ 外側スワイプの常用プリセット:"
		};
		dictionary["PresetShowDesktop"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🖥️ 显示桌面 (Win+D)",
			[LanguageCode.ZhTw] = "🖥️ 顯示桌面 (Win+D)",
			[LanguageCode.En] = "🖥️ Show Desktop (Win+D)",
			[LanguageCode.Ja] = "🖥️ デスクトップ表示 (Win+D)"
		};
		dictionary["PresetShowDesktopTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "外甩快速显示桌面，查阅文件或切换任务",
			[LanguageCode.ZhTw] = "外甩快速顯示桌面，查閱檔案或切換任務",
			[LanguageCode.En] = "Quickly minimize all to view desktop or switch tasks",
			[LanguageCode.Ja] = "素早くデスクトップを表示し、ファイル確認やタスク切替を行います"
		};
		dictionary["PresetTaskView"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "📑 任务视图 (Win+Tab)",
			[LanguageCode.ZhTw] = "📑 任務檢視 (Win+Tab)",
			[LanguageCode.En] = "📑 Task View (Win+Tab)",
			[LanguageCode.Ja] = "📑 タスクビュー (Win+Tab)"
		};
		dictionary["PresetTaskViewTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "外甩浏览多虚拟桌面与所有活动任务窗口",
			[LanguageCode.ZhTw] = "外甩瀏覽多虛擬桌面與所有活動任務視窗",
			[LanguageCode.En] = "View virtual desktops and active task windows",
			[LanguageCode.Ja] = "仮想デスクトップとすべてのアクティブウィンドウを表示"
		};
		dictionary["PresetCancelEsc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "↩️ 取消/返回 (Esc)",
			[LanguageCode.ZhTw] = "↩️ 取消/返回 (Esc)",
			[LanguageCode.En] = "↩️ Cancel / Back (Esc)",
			[LanguageCode.Ja] = "↩️ キャンセル/戻る (Esc)"
		};
		dictionary["PresetCancelEscTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "外甩退出当前弹窗或中断当前操作",
			[LanguageCode.ZhTw] = "外甩退出當前彈窗或中斷當前操作",
			[LanguageCode.En] = "Dismiss popups or abort current operation",
			[LanguageCode.Ja] = "現在のポップアップや操作を中断して終了"
		};
		dictionary["PresetScreenSnipping"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "✂️ 系统截屏 (Win+Shift+S)",
			[LanguageCode.ZhTw] = "✂️ 系統截圖 (Win+Shift+S)",
			[LanguageCode.En] = "✂️ Snipping Tool (Win+Shift+S)",
			[LanguageCode.Ja] = "✂️ 画面キャプチャ (Win+Shift+S)"
		};
		dictionary["PresetScreenSnippingTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "外甩快速唤起 Windows 区域截屏工具",
			[LanguageCode.ZhTw] = "外甩快速喚起 Windows 區域截圖工具",
			[LanguageCode.En] = "Launch Windows Snipping Tool region capture",
			[LanguageCode.Ja] = "Windows 領域キャプチャツールを素早く起動"
		};
		dictionary["PresetTileHalfSplit"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🪟 左右对半平铺",
			[LanguageCode.ZhTw] = "🪟 左右對半平鋪",
			[LanguageCode.En] = "🪟 Snap Left/Right Half",
			[LanguageCode.Ja] = "🪟 左右分割スナップ"
		};
		dictionary["PresetTileHalfSplitTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "外甩将当前窗口以左右对半形式快速分屏",
			[LanguageCode.ZhTw] = "外甩將當前視窗以左右對半形式快速分屏",
			[LanguageCode.En] = "Snap active window into half-screen split",
			[LanguageCode.Ja] = "アクティブウィンドウを左右半分に分割配置"
		};
		dictionary["PresetStarPieSettings"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚙️ StarPie 控制台",
			[LanguageCode.ZhTw] = "⚙️ StarPie 控制台",
			[LanguageCode.En] = "⚙️ StarPie Settings",
			[LanguageCode.Ja] = "⚙️ StarPie 設定"
		};
		dictionary["PresetStarPieSettingsTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "外甩呼出 StarPie 配置控制台界面",
			[LanguageCode.ZhTw] = "外甩呼出 StarPie 配置控制台介面",
			[LanguageCode.En] = "Open StarPie settings console",
			[LanguageCode.Ja] = "StarPie 設定コンソール画面を開く"
		};
		dictionary["ActionFormTypeLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "动作类型:",
			[LanguageCode.ZhTw] = "動作類型:",
			[LanguageCode.En] = "Action Type:",
			[LanguageCode.Ja] = "アクションの種類:"
		};
		dictionary["ActionFormNameLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "动作显示名称:",
			[LanguageCode.ZhTw] = "動作顯示名稱:",
			[LanguageCode.En] = "Display Name:",
			[LanguageCode.Ja] = "表示名:"
		};
		dictionary["ActionFormNameTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "自定义此动作的显示名称",
			[LanguageCode.ZhTw] = "自訂此動作的顯示名稱",
			[LanguageCode.En] = "Custom display label for this action",
			[LanguageCode.Ja] = "このアクションの表示名をカスタマイズ"
		};
		dictionary["ActionFormHotkeysLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "快捷按键组合:",
			[LanguageCode.ZhTw] = "快捷按鍵組合:",
			[LanguageCode.En] = "Shortcut Combo:",
			[LanguageCode.Ja] = "ショートカットの組み合わせ:"
		};
		dictionary["BtnActionBuildHotkeys"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚙️ 拼装...",
			[LanguageCode.ZhTw] = "⚙️ 拼裝...",
			[LanguageCode.En] = "⚙️ Builder...",
			[LanguageCode.Ja] = "⚙️ 構成..."
		};
		dictionary["TipActionBuildHotkeys"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "打开快捷键组合拼装器",
			[LanguageCode.ZhTw] = "開啟快捷鍵組合拼裝器",
			[LanguageCode.En] = "Open hotkey combination builder",
			[LanguageCode.Ja] = "ショートカットキービルダーを開く"
		};
		dictionary["ActionFormAppPathLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "目标应用程序路径:",
			[LanguageCode.ZhTw] = "目標應用程式路徑:",
			[LanguageCode.En] = "Application Path:",
			[LanguageCode.Ja] = "アプリのパス:"
		};
		dictionary["BtnActionPickProgram"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "📦 软件库选择...",
			[LanguageCode.ZhTw] = "📦 軟體庫選擇...",
			[LanguageCode.En] = "📦 App Library...",
			[LanguageCode.Ja] = "📦 アプリ一覧から選択..."
		};
		dictionary["TipActionPickProgram"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "从已安装软件与开始菜单中选择",
			[LanguageCode.ZhTw] = "從已安裝軟體與開始功能表中選擇",
			[LanguageCode.En] = "Select from installed apps and Start Menu",
			[LanguageCode.Ja] = "インストール済みアプリやスタートメニューから選択"
		};
		dictionary["BtnActionCaptureWindow"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🎯 捕捉运行窗口...",
			[LanguageCode.ZhTw] = "🎯 捕捉運行視窗...",
			[LanguageCode.En] = "🎯 Window Sniper...",
			[LanguageCode.Ja] = "🎯 ウィンドウ捕捉..."
		};
		dictionary["TipActionCaptureWindow"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "从桌面正在运行的程序中选择或拖拽准星瞄准抓取",
			[LanguageCode.ZhTw] = "從桌面正在運行的程式中選擇或拖拽準星瞄準抓取",
			[LanguageCode.En] = "Select from running windows or drag crosshair to target",
			[LanguageCode.Ja] = "実行中のウィンドウから選択するか照準をドラッグして捕捉"
		};
		dictionary["BtnActionBrowseFile"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "📂 浏览...",
			[LanguageCode.ZhTw] = "📂 瀏覽...",
			[LanguageCode.En] = "📂 Browse...",
			[LanguageCode.Ja] = "📂 参照..."
		};
		dictionary["TipActionBrowseFile"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "打开文件浏览窗口选择可执行文件",
			[LanguageCode.ZhTw] = "開啟檔案瀏覽視窗選擇可執行檔",
			[LanguageCode.En] = "Browse for an executable file",
			[LanguageCode.Ja] = "実行可能ファイルを参照して選択"
		};
		dictionary["ActionFormWebUrlLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "目标网址 URL:",
			[LanguageCode.ZhTw] = "目標網址 URL:",
			[LanguageCode.En] = "Website URL:",
			[LanguageCode.Ja] = "ウェブサイト URL:"
		};
		dictionary["ActionFormCommonUrlsLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "常用网址:",
			[LanguageCode.ZhTw] = "常用網址:",
			[LanguageCode.En] = "Quick Links:",
			[LanguageCode.Ja] = "クイックリンク:"
		};
		dictionary["ActionFormFolderPathLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "目标本地文件夹路径:",
			[LanguageCode.ZhTw] = "目標本機資料夾路徑:",
			[LanguageCode.En] = "Folder Path:",
			[LanguageCode.Ja] = "フォルダパス:"
		};
		dictionary["BtnActionBrowseFolder"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "📂 浏览文件夹...",
			[LanguageCode.ZhTw] = "📂 瀏覽資料夾...",
			[LanguageCode.En] = "📂 Browse Folder...",
			[LanguageCode.Ja] = "📂 フォルダを参照..."
		};
		dictionary["TipActionBrowseFolder"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "选择本地文件夹路径...",
			[LanguageCode.ZhTw] = "選擇本機資料夾路徑...",
			[LanguageCode.En] = "Select a local directory path...",
			[LanguageCode.Ja] = "ローカルフォルダのパスを選択..."
		};
		dictionary["ActionFormCmdLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "命令行指令与终端类型:",
			[LanguageCode.ZhTw] = "命令列指令與終端機類型:",
			[LanguageCode.En] = "Command Line & Shell:",
			[LanguageCode.Ja] = "コマンドラインとシェル:"
		};
		dictionary["ActionFormWindowCtrlLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🪟 窗口控制子模式:",
			[LanguageCode.ZhTw] = "🪟 視窗控制子模式:",
			[LanguageCode.En] = "🪟 Window Control Sub-Mode:",
			[LanguageCode.Ja] = "🪟 ウィンドウ制御モード:"
		};
		dictionary["ActionFormSysCmdsLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "系统全局指令预设:",
			[LanguageCode.ZhTw] = "系統全域指令預設:",
			[LanguageCode.En] = "System Command Presets:",
			[LanguageCode.Ja] = "システムコマンドプリセット:"
		};
		dictionary["BtnTestCancelAction"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🧪 模拟测试触发",
			[LanguageCode.ZhTw] = "🧪 模擬測試觸發",
			[LanguageCode.En] = "🧪 Simulate Trigger",
			[LanguageCode.Ja] = "🧪 テスト実行"
		};
		dictionary["CancelActionStatusHint"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "💡 外甩脱离轮盘时，立即执行此自定义动作；回到轮盘中心仍为静默关闭。",
			[LanguageCode.ZhTw] = "💡 外甩脫離輪盤時，立即執行此自訂動作；回到輪盤中心仍為靜默關閉。",
			[LanguageCode.En] = "💡 Executes this custom action when flicked outward. Moving back to center still closes silently.",
			[LanguageCode.Ja] = "💡 ホイール外側にスワイプするとこのアクションを実行します。中心に戻すと静かに閉じます。"
		};
		dictionary["EdgeOverflowTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "屏幕边缘呼出智能防溢出与光标自动对齐",
			[LanguageCode.ZhTw] = "螢幕邊緣呼出智慧防溢出與游標自動對齊",
			[LanguageCode.En] = "Edge Overflow Protection & Smart Cursor Alignment",
			[LanguageCode.Ja] = "画面端オーバーフロー防止とカーソル自動整列"
		};
		dictionary["EdgeOverflowDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "当在屏幕四周边缘（顶部、底部或两侧）呼出轮盘时，智能检测显示器安全边界，防止轮盘扇区被截断并自动对齐光标至轮盘物理中心。",
			[LanguageCode.ZhTw] = "當在螢幕四周邊緣（頂部、底部或兩側）呼出輪盤時，智慧檢測顯示器安全邊界，防止輪盤扇區被截斷並自動對齊游標至輪盤物理中心。",
			[LanguageCode.En] = "Detects screen boundaries when opening near screen edges, preventing sector clipping and aligning cursor to the wheel center.",
			[LanguageCode.Ja] = "画面端付近でホイールを表示する際に境界を検知し、セクターの画面外はみ出しを防ぎカーソルを物理中心に整列させます。"
		};
		dictionary["EdgeOverflowStrategyLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "防溢出处理策略:",
			[LanguageCode.ZhTw] = "防溢出處理策略:",
			[LanguageCode.En] = "Overflow Prevention Strategy:",
			[LanguageCode.Ja] = "はみ出し防止ポリシー:"
		};
		dictionary["EdgeOverflowStrategyDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "智能贴边：自动推入屏幕并对齐光标；屏幕中心：直接在当前显示器正中展现。",
			[LanguageCode.ZhTw] = "智慧貼邊：自動推入螢幕並對齊游標；螢幕中心：直接在當前顯示器正中展現。",
			[LanguageCode.En] = "Smart Snap: Push wheel into bounds and align cursor; Screen Center: Always pop up at monitor center.",
			[LanguageCode.Ja] = "スマートスナップ: 画面内に収めカーソルを整列; 画面中央: モニターの中央に直接表示。"
		};
		dictionary["EdgeOverflowStrategyAuto"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🛡️ 智能贴边防溢出 (推荐)",
			[LanguageCode.ZhTw] = "🛡️ 智慧貼邊防溢出 (推薦)",
			[LanguageCode.En] = "🛡️ Smart Edge Push (Recommended)",
			[LanguageCode.Ja] = "🛡️ スマートスナップ (推奨)"
		};
		dictionary["EdgeOverflowStrategyCenter"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🎯 屏幕物理正中心呼出",
			[LanguageCode.ZhTw] = "🎯 螢幕物理正中心呼出",
			[LanguageCode.En] = "🎯 Monitor Center Popup",
			[LanguageCode.Ja] = "🎯 モニター中央にポップアップ"
		};
		dictionary["EdgeOverflowStrategyNone"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🚫 原生跟随光标 (允许溢出)",
			[LanguageCode.ZhTw] = "🚫 原生跟隨游標 (允許溢出)",
			[LanguageCode.En] = "🚫 Strict Cursor Follow (Allow Overflow)",
			[LanguageCode.Ja] = "🚫 カーソル追従 (はみ出し許可)"
		};
		dictionary["EdgeOverflowMarginXLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "X 轴边缘安全边距:",
			[LanguageCode.ZhTw] = "X 軸邊緣安全邊距:",
			[LanguageCode.En] = "Horizontal (X) Safe Margin:",
			[LanguageCode.Ja] = "水平 (X) 安全マージン:"
		};
		dictionary["EdgeOverflowMarginXDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "调节轮盘左右边缘距离屏幕物理视口边界的保留间距。增大数值可让轮盘更早贴入屏幕内侧并自动对齐光标。",
			[LanguageCode.ZhTw] = "調節輪盤左右邊緣距離螢幕物理視口邊界的保留間距。增大數值可讓輪盤更早貼入螢幕內側並自動對齊游標。",
			[LanguageCode.En] = "Safety padding between wheel sides and monitor viewport edges. Higher values push wheel inward sooner.",
			[LanguageCode.Ja] = "左右エッジと画面端の安全マージン。値を大きくするとより内側にスナップします。"
		};
		dictionary["EdgeOverflowMarginYLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "Y 轴边缘安全边距:",
			[LanguageCode.ZhTw] = "Y 軸邊緣安全邊距:",
			[LanguageCode.En] = "Vertical (Y) Safe Margin:",
			[LanguageCode.Ja] = "垂直 (Y) 安全マージン:"
		};
		dictionary["EdgeOverflowMarginYDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "调节轮盘上下边缘距离屏幕物理视口边界（避让任务栏与顶部标题栏）的保留间距。",
			[LanguageCode.ZhTw] = "調節輪盤上下邊緣距離螢幕物理視口邊界（避讓任務欄與頂部標題欄）的保留間距。",
			[LanguageCode.En] = "Safety padding between wheel top/bottom and viewport edges (clears taskbars and title bars).",
			[LanguageCode.Ja] = "上下エッジと画面端（タスクバーやタイトルバーを回避）の安全マージン。"
		};
		dictionary["BlacklistModeLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "排除黑名单模式",
			[LanguageCode.ZhTw] = "排除黑名單模式",
			[LanguageCode.En] = "Blacklist Mode",
			[LanguageCode.Ja] = "ブラックリストモード"
		};
		dictionary["BlacklistModeSub"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "全局生效，仅在名单内程序放行右键",
			[LanguageCode.ZhTw] = "全域生效，僅在名單內程式放行右鍵",
			[LanguageCode.En] = "Global activation; releases trigger key in listed apps",
			[LanguageCode.Ja] = "全体で有効。リスト内のアプリでのみキーを通過"
		};
		dictionary["WhitelistModeLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "启用白名单模式",
			[LanguageCode.ZhTw] = "啟用白名單模式",
			[LanguageCode.En] = "Whitelist Mode",
			[LanguageCode.Ja] = "ホワイトリストモード"
		};
		dictionary["WhitelistModeSub"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "仅在名单内程序生效，其余完全放行",
			[LanguageCode.ZhTw] = "僅在名單內程式生效，其餘完全放行",
			[LanguageCode.En] = "Active only in listed apps; releases trigger key elsewhere",
			[LanguageCode.Ja] = "リスト内のアプリでのみ有効。他は完全にキーを通過"
		};
		dictionary["BtnConfigProcessTrigger"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚙️ 配置触发键",
			[LanguageCode.ZhTw] = "⚙️ 配置觸發鍵",
			[LanguageCode.En] = "⚙️ Custom Trigger",
			[LanguageCode.Ja] = "⚙️ トリガー設定"
		};
		dictionary["TipConfigProcessTrigger"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "为此进程录制专属的呼出按键或组合键",
			[LanguageCode.ZhTw] = "為此處理程序錄製專屬的呼出按鍵或組合鍵",
			[LanguageCode.En] = "Record dedicated popup trigger or combo for this process",
			[LanguageCode.Ja] = "このアプリ専用の起動キーやコンボを登録"
		};
		dictionary["BtnRestoreProcessPass"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔄 恢复放行",
			[LanguageCode.ZhTw] = "🔄 恢復放行",
			[LanguageCode.En] = "🔄 Passthrough",
			[LanguageCode.Ja] = "🔄 通過に戻す"
		};
		dictionary["TipRestoreProcessPass"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "清除专属按键，恢复完全放行",
			[LanguageCode.ZhTw] = "清除專屬按鍵，恢復完全放行",
			[LanguageCode.En] = "Clear dedicated trigger and restore full key passthrough",
			[LanguageCode.Ja] = "専用キーをクリアし完全通過に戻す"
		};
		dictionary["TipRemoveProcessItem"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "从名单中移除此进程",
			[LanguageCode.ZhTw] = "從名單中移除此處理程序",
			[LanguageCode.En] = "Remove process from list",
			[LanguageCode.Ja] = "リストからこのプロセスを削除"
		};
		dictionary["ProcessCustomTriggerCardTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🎯 进程专属唤醒按键配置：",
			[LanguageCode.ZhTw] = "🎯 處理程序專屬喚醒按鍵配置：",
			[LanguageCode.En] = "🎯 Dedicated Process Trigger Configuration:",
			[LanguageCode.Ja] = "🎯 アプリ専用トリガーキー設定:"
		};
		dictionary["BtnCloseCardTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "收起此配置卡片",
			[LanguageCode.ZhTw] = "收起此配置卡片",
			[LanguageCode.En] = "Collapse card",
			[LanguageCode.Ja] = "カードを閉じる"
		};
		dictionary["ProcessCustomTriggerCardDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "为选中的程序配置单独的轮盘呼出按键（如在 SolidWorks 中配置中键/侧键，避免与右键笔势冲突；普通右键将 100% 放行给该软件）。",
			[LanguageCode.ZhTw] = "為選中的程式配置單獨的輪盤呼出按鍵（如在 SolidWorks 中配置中鍵/側鍵，避免與右鍵筆勢衝突；普通右鍵將 100% 放行給該軟體）。",
			[LanguageCode.En] = "Configure dedicated triggers for specific apps (e.g. Middle/Side click in SolidWorks to avoid right-click gesture conflicts; standard right-click is fully passed through).",
			[LanguageCode.Ja] = "指定アプリ専用のトリガーキーを設定（例: SolidWorks で中クリックやサイドボタンを割り当て、右クリックジェスチャーとの競合を防止。通常右クリックはアプリに通過）。"
		};
		dictionary["ProcessCurrentTriggerLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "当前专属触发键：",
			[LanguageCode.ZhTw] = "當前專屬觸發鍵：",
			[LanguageCode.En] = "Current Dedicated Trigger:",
			[LanguageCode.Ja] = "現在の専用トリガー:"
		};
		dictionary["ProcessTriggerUnconfigured"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🚫 未配置",
			[LanguageCode.ZhTw] = "🚫 未配置",
			[LanguageCode.En] = "🚫 Unconfigured",
			[LanguageCode.Ja] = "🚫 未設定"
		};
		dictionary["BtnRecordProcessTrigger"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔴 点击录制专属按键 / 组合键",
			[LanguageCode.ZhTw] = "🔴 點擊錄製專屬按鍵 / 組合鍵",
			[LanguageCode.En] = "🔴 Click to Record Dedicated Key / Combo",
			[LanguageCode.Ja] = "🔴 クリックして専用キー/コンボを録画"
		};
		dictionary["BtnResetProcessTrigger"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔄 恢复默认",
			[LanguageCode.ZhTw] = "🔄 恢復預設",
			[LanguageCode.En] = "🔄 Reset Default",
			[LanguageCode.Ja] = "🔄 デフォルトに戻す"
		};
		dictionary["ProcessSensorReadyTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "硬件感知器已就绪：点击上方录制按钮后，按下你想作为该程序呼出键的鼠标按键（如中键/侧键）或键盘按键，即可自动捕获。",
			[LanguageCode.ZhTw] = "硬體感知器已就緒：點擊上方錄製按鈕後，按下你想作為該程式呼出鍵的滑鼠按鍵（如中鍵/側鍵）或鍵盤按鍵，即可自動捕獲。",
			[LanguageCode.En] = "Hardware sensor ready: Click record above, then press the desired mouse button (Middle/Side) or key to capture automatically.",
			[LanguageCode.Ja] = "ハードウェアセンサー準備完了: 上の録画ボタンをクリック後、割り当てたいマウスボタンやキーを押すと自動登録されます。"
		};
		dictionary["ProcessTriggerDedicated"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "专属按键",
			[LanguageCode.ZhTw] = "專屬按鍵",
			[LanguageCode.En] = "Dedicated",
			[LanguageCode.Ja] = "専用キー"
		};
		dictionary["ProcessTriggerDefaultPass"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "未配置专属键 (完全放行右键)",
			[LanguageCode.ZhTw] = "未配置專屬鍵 (完全放行右鍵)",
			[LanguageCode.En] = "Not configured (right click passed through)",
			[LanguageCode.Ja] = "未設定（右クリックを完全通過）"
		};




		dictionary["ActionBingSearch"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "Bing 搜索",
			[LanguageCode.ZhTw] = "Bing 搜尋",
			[LanguageCode.En] = "Bing Search",
			[LanguageCode.Ja] = "Bing 検索"
		};
		dictionary["TipGestureOpacity"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "输入不透明度百分比 (30~100)",
			[LanguageCode.ZhTw] = "輸入不透明度百分比 (30~100)",
			[LanguageCode.En] = "Enter opacity percentage (30~100)",
			[LanguageCode.Ja] = "不透明度のパーセンテージを入力 (30~100)"
		};
		dictionary["SettingsWindowTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "StarPie 设置控制台",
			[LanguageCode.ZhTw] = "StarPie 設定主控台",
			[LanguageCode.En] = "StarPie Settings Console",
			[LanguageCode.Ja] = "StarPie 設定コンソール"
		};
		dictionary["ClearHotkey"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "清空快捷键",
			[LanguageCode.ZhTw] = "清空快捷鍵",
			[LanguageCode.En] = "Clear Hotkey",
			[LanguageCode.Ja] = "ショートカットをクリア"
		};
		dictionary["HotkeyPlaceholder"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "点击录制/按Esc取消...",
			[LanguageCode.ZhTw] = "點擊錄製/按Esc取消...",
			[LanguageCode.En] = "Click to record / Esc to cancel...",
			[LanguageCode.Ja] = "クリックして録音 / Escでキャンセル..."
		};
		dictionary["HotkeyRecordingHint"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔴 录制中... 点击或按Esc完成",
			[LanguageCode.ZhTw] = "🔴 錄製中... 點擊或按Esc完成",
			[LanguageCode.En] = "🔴 Recording... Click or press Esc to finish",
			[LanguageCode.Ja] = "🔴 録音中... クリックまたはEscで完了"
		};
		dictionary["HotkeyPressCombination"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔴 请按下快捷键组合...",
			[LanguageCode.ZhTw] = "🔴 請按下快捷鍵組合...",
			[LanguageCode.En] = "🔴 Please press key combination...",
			[LanguageCode.Ja] = "🔴 ショートカットキーの組み合わせを押してください..."
		};
				dictionary["Tab1_UiStyleLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "主题风格:",
			[LanguageCode.ZhTw] = "主題風格:",
			[LanguageCode.En] = "Theme Style:",
			[LanguageCode.Ja] = "テーマスタイル:"
		};
		dictionary["UiStyleClassicRing"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "经典圆环",
			[LanguageCode.ZhTw] = "經典圓環",
			[LanguageCode.En] = "Classic Ring",
			[LanguageCode.Ja] = "クラシックリング"
		};
		dictionary["UiStyleCleanSectors"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "极简扇区",
			[LanguageCode.ZhTw] = "極簡扇區",
			[LanguageCode.En] = "Clean Sectors",
			[LanguageCode.Ja] = "クリーンセクター"
		};
		dictionary["UiStyleGlassmorphism"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "液态毛玻璃",
			[LanguageCode.ZhTw] = "液態毛玻璃",
			[LanguageCode.En] = "Liquid Glassmorphism",
			[LanguageCode.Ja] = "リキッドグラス"
		};
		dictionary["Tab1_ThemePresetLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "配色方案:",
			[LanguageCode.ZhTw] = "配色方案:",
			[LanguageCode.En] = "Color Scheme:",
			[LanguageCode.Ja] = "カラースキーム:"
		};
		dictionary["ThemeItemSystem"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "跟随系统",
			[LanguageCode.ZhTw] = "跟隨系統",
			[LanguageCode.En] = "Follow System",
			[LanguageCode.Ja] = "システムに従う"
		};
		dictionary["ThemeItemDark"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "深色模式",
			[LanguageCode.ZhTw] = "深色模式",
			[LanguageCode.En] = "Dark Mode",
			[LanguageCode.Ja] = "ダークモード"
		};
		dictionary["ThemeItemLight"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "浅色模式",
			[LanguageCode.ZhTw] = "淺色模式",
			[LanguageCode.En] = "Light Mode",
			[LanguageCode.Ja] = "ライトモード"
		};
		dictionary["ThemeItemMatchaForest"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "抹茶森林",
			[LanguageCode.ZhTw] = "抹茶森林",
			[LanguageCode.En] = "Matcha Forest",
			[LanguageCode.Ja] = "抹茶フォレスト"
		};
		dictionary["ThemeItemGlacialIce"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "冰川透蓝",
			[LanguageCode.ZhTw] = "冰川透藍",
			[LanguageCode.En] = "Glacial Ice",
			[LanguageCode.Ja] = "氷河アイスブルー"
		};
		dictionary["ThemeItemMorandiMuted"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "莫兰迪柔灰",
			[LanguageCode.ZhTw] = "莫蘭迪柔灰",
			[LanguageCode.En] = "Morandi Muted Gray",
			[LanguageCode.Ja] = "モランディグレー"
		};
		dictionary["ThemeItemCustom"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🎨 自定义配色",
			[LanguageCode.ZhTw] = "🎨 自訂配色",
			[LanguageCode.En] = "🎨 Custom Colors",
			[LanguageCode.Ja] = "🎨 カスタム配色"
		};
		dictionary["BtnNewCustomPreset"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "➕ 新建配色",
			[LanguageCode.ZhTw] = "➕ 新建配色",
			[LanguageCode.En] = "➕ New Preset",
			[LanguageCode.Ja] = "➕ 新規プリセット"
		};
		dictionary["TipNewCustomPreset"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "基于当前色彩创建全新的自定义配色方案预设",
			[LanguageCode.ZhTw] = "基於當前色彩創建全新的自訂配色方案預設",
			[LanguageCode.En] = "Create a new custom color preset based on current colors",
			[LanguageCode.Ja] = "現在の色に基づいて新しいカスタムカラースキームプリセットを作成"
		};
		dictionary["BtnRenameCustomPreset"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "✏️ 重命名预设",
			[LanguageCode.ZhTw] = "✏️ 重新命名預設",
			[LanguageCode.En] = "✏️ Rename Preset",
			[LanguageCode.Ja] = "✏️ プリセット名を変更"
		};
		dictionary["TipRenameCustomPreset"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "重命名当前选中的自定义配色方案预设",
			[LanguageCode.ZhTw] = "重命名當前選中的自訂配色方案預設",
			[LanguageCode.En] = "Rename the selected custom color preset",
			[LanguageCode.Ja] = "選択したカスタムカラープリセットの名前を変更"
		};
		dictionary["BtnDeleteCustomPreset"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🗑️ 删除预设",
			[LanguageCode.ZhTw] = "🗑️ 刪除預設",
			[LanguageCode.En] = "🗑️ Delete Preset",
			[LanguageCode.Ja] = "🗑️ プリセットを削除"
		};
		dictionary["TipDeleteCustomPreset"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "删除当前选中的自定义配色方案预设",
			[LanguageCode.ZhTw] = "刪除當前選中的自訂配色方案預設",
			[LanguageCode.En] = "Delete the selected custom color preset",
			[LanguageCode.Ja] = "選択したカスタムカラープリセットを削除"
		};
		dictionary["Tab1_CustomColorsSectionLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "自定义颜色 (色盘调色 / 屏幕吸色):",
			[LanguageCode.ZhTw] = "自訂顏色 (色盤調色 / 螢幕吸色):",
			[LanguageCode.En] = "Custom Colors (Palette / Eyedropper):",
			[LanguageCode.Ja] = "カスタムカラー (パレット / スポイト):"
		};
		dictionary["Tab1_SectorBgLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "扇区底色:",
			[LanguageCode.ZhTw] = "扇區底色:",
			[LanguageCode.En] = "Sector Background:",
			[LanguageCode.Ja] = "セクター背景色:"
		};
		dictionary["TipPickColor"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "打开调色板选取颜色",
			[LanguageCode.ZhTw] = "開啟調色盤選取顏色",
			[LanguageCode.En] = "Open color picker to select color",
			[LanguageCode.Ja] = "カラーパレットを開いて選択"
		};
		dictionary["TipEyedropColor"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "从屏幕任意位置吸取颜色",
			[LanguageCode.ZhTw] = "從螢幕任意位置吸取顏色",
			[LanguageCode.En] = "Pick color from anywhere on screen",
			[LanguageCode.Ja] = "画面上の任意の位置から色を抽出"
		};
		dictionary["Tab1_SectorBorderLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "扇区边框:",
			[LanguageCode.ZhTw] = "扇區邊框:",
			[LanguageCode.En] = "Sector Border:",
			[LanguageCode.Ja] = "セクター境界線:"
		};
		dictionary["Tab1_HighlightBgLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "高亮底色:",
			[LanguageCode.ZhTw] = "高亮底色:",
			[LanguageCode.En] = "Highlight Background:",
			[LanguageCode.Ja] = "ハイライト背景色:"
		};
		dictionary["Tab1_HighlightBorderLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "高亮边框:",
			[LanguageCode.ZhTw] = "高亮邊框:",
			[LanguageCode.En] = "Highlight Border:",
			[LanguageCode.Ja] = "ハイライト境界線:"
		};
		dictionary["Tab1_TextColorLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "文字颜色:",
			[LanguageCode.ZhTw] = "文字顏色:",
			[LanguageCode.En] = "Text Color:",
			[LanguageCode.Ja] = "テキスト色:"
		};
		dictionary["BtnSavePresetChanges"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "💾 保存当前配色修改",
			[LanguageCode.ZhTw] = "💾 儲存當前配色修改",
			[LanguageCode.En] = "💾 Save Preset Changes",
			[LanguageCode.Ja] = "💾 配色の変更を保存"
		};
		dictionary["TipSavePresetChanges"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "将当前调整的颜色直接保存到正在使用的配色预设中",
			[LanguageCode.ZhTw] = "將當前調整的顏色直接儲存到正在使用的配色預設中",
			[LanguageCode.En] = "Save current adjusted colors directly to the active preset",
			[LanguageCode.Ja] = "現在調整した色を使用中のプリセットに直接保存"
		};
		dictionary["BtnSaveAsNewPreset"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "➕ 另存为新预设...",
			[LanguageCode.ZhTw] = "➕ 另存為新預設...",
			[LanguageCode.En] = "➕ Save as New Preset...",
			[LanguageCode.Ja] = "➕ 新規プリセットとして保存..."
		};
		dictionary["TipSaveAsNewPreset"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "将当前调整的颜色另存为一个全新的独立配色预设",
			[LanguageCode.ZhTw] = "將當前調整的顏色另存為一個全新的獨立配色預設",
			[LanguageCode.En] = "Save adjusted colors as a brand new independent preset",
			[LanguageCode.Ja] = "調整した色を新しい独立したプリセットとして保存"
		};
		dictionary["Tab1_HighlightGlowModeLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "高亮边缘光晕模式:",
			[LanguageCode.ZhTw] = "高亮邊緣光暈模式:",
			[LanguageCode.En] = "Highlight Edge Glow Mode:",
			[LanguageCode.Ja] = "ハイライトエッジグローモード:"
		};
		dictionary["GlowItemFollowHighlight"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🌈 跟随主题高亮色",
			[LanguageCode.ZhTw] = "🌈 跟隨主題高亮色",
			[LanguageCode.En] = "🌈 Follow Theme Highlight",
			[LanguageCode.Ja] = "🌈 テーマのハイライトに従う"
		};
		dictionary["GlowItemLilacPurple"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "💜 丁香晶紫",
			[LanguageCode.ZhTw] = "💜 丁香晶紫",
			[LanguageCode.En] = "💜 Lilac Purple",
			[LanguageCode.Ja] = "💜 ライラックパープル"
		};
		dictionary["GlowItemGlacialBlue"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "💙 冰川湛蓝",
			[LanguageCode.ZhTw] = "💙 冰川湛藍",
			[LanguageCode.En] = "💙 Glacial Blue",
			[LanguageCode.Ja] = "💙 グレイシャルブルー"
		};
		dictionary["GlowItemEmeraldGreen"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "💚 翡翠荧绿",
			[LanguageCode.ZhTw] = "💚 翡翠熒綠",
			[LanguageCode.En] = "💚 Emerald Green",
			[LanguageCode.Ja] = "💚 エメラルドグリーン"
		};
		dictionary["GlowItemSakuraPink"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "💖 樱花粉晕",
			[LanguageCode.ZhTw] = "💖 櫻花粉暈",
			[LanguageCode.En] = "💖 Sakura Pink",
			[LanguageCode.Ja] = "💖 サクラピンク"
		};
		dictionary["GlowItemAmberGold"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🧡 琥珀金光",
			[LanguageCode.ZhTw] = "🧡 琥珀金光",
			[LanguageCode.En] = "🧡 Amber Gold",
			[LanguageCode.Ja] = "🧡 アンバーゴールド"
		};
		dictionary["GlowItemCoralRed"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔴 珊瑚赤光",
			[LanguageCode.ZhTw] = "🔴 珊瑚赤光",
			[LanguageCode.En] = "🔴 Coral Red",
			[LanguageCode.Ja] = "🔴 コーラルレッド"
		};
		dictionary["GlowItemIceWhite"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚪ 冰魄纯白",
			[LanguageCode.ZhTw] = "⚪ 冰魄純白",
			[LanguageCode.En] = "⚪ Ice Pure White",
			[LanguageCode.Ja] = "⚪ アイスピュアホワイト"
		};
		dictionary["GlowItemCustom"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🎨 自定义光晕颜色",
			[LanguageCode.ZhTw] = "🎨 自訂光暈顏色",
			[LanguageCode.En] = "🎨 Custom Glow Color",
			[LanguageCode.Ja] = "🎨 カスタムグロー色"
		};
		dictionary["Tab1_GlowColorLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "光晕色值:",
			[LanguageCode.ZhTw] = "光暈色值:",
			[LanguageCode.En] = "Glow Color Value:",
			[LanguageCode.Ja] = "グローカラー値:"
		};
		dictionary["TipPickGlowColor"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "打开调色板选取光晕颜色",
			[LanguageCode.ZhTw] = "開啟調色盤選取光暈顏色",
			[LanguageCode.En] = "Open color picker to select glow color",
			[LanguageCode.Ja] = "カラーパレットを開いてグロー色を選択"
		};
		dictionary["TipEyedropGlowColor"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "从屏幕任意位置吸取光晕颜色",
			[LanguageCode.ZhTw] = "從螢幕任意位置吸取光暈顏色",
			[LanguageCode.En] = "Pick glow color from anywhere on screen",
			[LanguageCode.Ja] = "画面上の任意の位置からグロー色を抽出"
		};
		dictionary["Tab1_GlowRadiusLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "光晕弥散半径:",
			[LanguageCode.ZhTw] = "光暈彌散半徑:",
			[LanguageCode.En] = "Glow Blur Radius:",
			[LanguageCode.Ja] = "グローぼかし半径:"
		};
		dictionary["Tab1_GlowOpacityLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "光晕不透明度:",
			[LanguageCode.ZhTw] = "光暈不透明度:",
			[LanguageCode.En] = "Glow Opacity:",
			[LanguageCode.Ja] = "グロー不透明度:"
		};
		dictionary["Tier2ThemeExpanderHeader"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🌐 二级轮盘风格与配色 (展开定制)",
			[LanguageCode.ZhTw] = "🌐 二級輪盤風格與配色 (展開自訂)",
			[LanguageCode.En] = "🌐 Tier-2 Wheel Style & Colors (Expand to Customize)",
			[LanguageCode.Ja] = "🌐 第2階層ホイールスタイルと配色 (展開して設定)"
		};
		dictionary["Tab1_SubThemeNotice"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🌟 当前正在单独定制二级级联轮盘专属视觉风格与色彩，支持与一级主轮盘自由组合！",
			[LanguageCode.ZhTw] = "🌟 當前正在單獨自訂二級級聯輪盤專屬視覺風格與色彩，支援與一級主輪盤自由組合！",
			[LanguageCode.En] = "🌟 Currently customizing visual style and colors for Tier-2 cascade wheel independently from Tier-1!",
			[LanguageCode.Ja] = "🌟 現在、第1階層とは独立して第2階層カスケードホイールのビジュアルスタイルと配色を個別にカスタマイズ中！"
		};
		dictionary["Tab1_SubUiStyleLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "二级轮盘视觉风格:",
			[LanguageCode.ZhTw] = "二級輪盤視覺風格:",
			[LanguageCode.En] = "Tier-2 Visual Style:",
			[LanguageCode.Ja] = "第2階層ビジュアルスタイル:"
		};
		dictionary["SubUiStyleItemFollowPrimary"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "跟随一级主轮盘风格",
			[LanguageCode.ZhTw] = "跟隨一級主輪盤風格",
			[LanguageCode.En] = "Follow Tier-1 Wheel Style",
			[LanguageCode.Ja] = "第1階層ホイールスタイルに従う"
		};
		dictionary["Tab1_SubThemePresetLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "二级轮盘配色方案:",
			[LanguageCode.ZhTw] = "二級輪盤配色方案:",
			[LanguageCode.En] = "Tier-2 Color Scheme:",
			[LanguageCode.Ja] = "第2階層カラースキーム:"
		};
		dictionary["SubThemeItemFollowPrimary"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "跟随一级主轮盘配色",
			[LanguageCode.ZhTw] = "跟隨一級主輪盤配色",
			[LanguageCode.En] = "Follow Tier-1 Color Scheme",
			[LanguageCode.Ja] = "第1階層カラースキームに従う"
		};
		dictionary["SubCustomColorsExpanderTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🎨 二级轮盘高级配色",
			[LanguageCode.ZhTw] = "🎨 二級輪盤高級配色",
			[LanguageCode.En] = "🎨 Tier-2 Wheel Advanced Colors",
			[LanguageCode.Ja] = "🎨 第2階層ホイール高度な配色"
		};
		dictionary["SubCustomColorsExpanderDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "展开后可精准微调二级扇区底色、高亮光晕、边框线条、文字等各项色彩。",
			[LanguageCode.ZhTw] = "展開後可精準微調二級扇區底色、高亮光暈、邊框線條、文字等各項色彩。",
			[LanguageCode.En] = "Expand to fine-tune Tier-2 sector background, highlight glow, border lines, text, etc.",
			[LanguageCode.Ja] = "展開して第2階層セクター背景、ハイライトグロー、境界線、テキストなどを微調整します。"
		};
		dictionary["Tab1_SubCustomColorsSectionLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "自定义十六进制色彩 (色盘调色 / 屏幕吸色):",
			[LanguageCode.ZhTw] = "自訂十六進位色彩 (色盤調色 / 螢幕吸色):",
			[LanguageCode.En] = "Custom Hex Colors (Palette / Eyedropper):",
			[LanguageCode.Ja] = "カスタム16進数カラー (パレット / スポイト):"
		};
		dictionary["TipSaveSubPresetChanges"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "将当前调整的颜色直接保存到正在使用的二级配色预设中",
			[LanguageCode.ZhTw] = "將當前調整的顏色直接儲存到正在使用的二級配色預設中",
			[LanguageCode.En] = "Save current adjusted colors directly to the active Tier-2 preset",
			[LanguageCode.Ja] = "現在調整した色を使用中の第2階層プリセットに直接保存"
		};
		dictionary["Tab1_SubHighlightGlowLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "二级轮盘高亮边缘光晕:",
			[LanguageCode.ZhTw] = "二級輪盤高亮邊緣光暈:",
			[LanguageCode.En] = "Tier-2 Highlight Edge Glow:",
			[LanguageCode.Ja] = "第2階層ハイライトエッジグロー:"
		};
		dictionary["SubGlowItemFollowPrimary"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔘 跟随一级主轮盘光晕",
			[LanguageCode.ZhTw] = "🔘 跟隨一級主輪盤光暈",
			[LanguageCode.En] = "🔘 Follow Tier-1 Wheel Glow",
			[LanguageCode.Ja] = "🔘 第1階層グローに従う"
		};
		dictionary["SubGlowItemFollowHighlight"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🌈 跟随二级主题高亮色",
			[LanguageCode.ZhTw] = "🌈 跟隨二級主題高亮色",
			[LanguageCode.En] = "🌈 Follow Tier-2 Theme Highlight",
			[LanguageCode.Ja] = "🌈 第2階層テーマのハイライトに従う"
		};
		dictionary["SubGlowItemNone"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🚫 关闭边缘光晕",
			[LanguageCode.ZhTw] = "🚫 關閉邊緣光暈",
			[LanguageCode.En] = "🚫 Disable Edge Glow",
			[LanguageCode.Ja] = "🚫 エッジグローを無効化"
		};
		dictionary["BtnResetSubTheme"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔄 恢复与一级轮盘相同主题",
			[LanguageCode.ZhTw] = "🔄 恢復與一級輪盤相同主題",
			[LanguageCode.En] = "🔄 Reset to Same Theme as Tier-1",
			[LanguageCode.Ja] = "🔄 第1階層と同じテーマにリセット"
		};
		dictionary["Tab1_SectorCutStyleLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "扇区切削形态:",
			[LanguageCode.ZhTw] = "扇區切削形態:",
			[LanguageCode.En] = "Sector Cut Shape:",
			[LanguageCode.Ja] = "セクター切削形状:"
		};
		dictionary["CutStyleItemClassic"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "经典紧凑扇区",
			[LanguageCode.ZhTw] = "經典緊湊扇區",
			[LanguageCode.En] = "Classic Compact Sectors",
			[LanguageCode.Ja] = "クラシックコンパクトセクター"
		};
		dictionary["CutStyleItemCircles"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "独立圆形卡片",
			[LanguageCode.ZhTw] = "獨立圓形卡片",
			[LanguageCode.En] = "Detached Circular Cards",
			[LanguageCode.Ja] = "独立した円形カード"
		};
		dictionary["CutStyleItemCapsules"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "悬浮圆角胶囊",
			[LanguageCode.ZhTw] = "懸浮圓角膠囊",
			[LanguageCode.En] = "Floating Rounded Capsules",
			[LanguageCode.Ja] = "フローティング角丸カプセル"
		};
		dictionary["CutStyleItemHexagons"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "蜂巢六边形矩阵",
			[LanguageCode.ZhTw] = "蜂巢六邊形矩陣",
			[LanguageCode.En] = "Honeycomb Hexagon Grid",
			[LanguageCode.Ja] = "ハニカム六角形グリッド"
		};
		dictionary["Tab1_SectorGapLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "扇区缝隙间距:",
			[LanguageCode.ZhTw] = "扇區縫隙間距:",
			[LanguageCode.En] = "Sector Gap Spacing:",
			[LanguageCode.Ja] = "セクター間の隙間:"
		};
		dictionary["Tab1_SectorCornerRadiusLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "扇区边缘平滑倒角:",
			[LanguageCode.ZhTw] = "扇區邊緣平滑倒角:",
			[LanguageCode.En] = "Sector Corner Radius:",
			[LanguageCode.Ja] = "セクター角丸半径:"
		};
		dictionary["Tab1_WheelRadiusLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "轮盘整体半径:",
			[LanguageCode.ZhTw] = "輪盤整體半徑:",
			[LanguageCode.En] = "Wheel Outer Radius:",
			[LanguageCode.Ja] = "ホイール全体半径:"
		};
		dictionary["Tab1_InnerRadiusLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "扇区内半径:",
			[LanguageCode.ZhTw] = "扇區內半徑:",
			[LanguageCode.En] = "Sector Inner Radius:",
			[LanguageCode.Ja] = "セクター内半径:"
		};
		dictionary["Tab1_CoreRadiusLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "中心核心圆半径:",
			[LanguageCode.ZhTw] = "中心核心圓半徑:",
			[LanguageCode.En] = "Center Core Radius:",
			[LanguageCode.Ja] = "センターコア半径:"
		};
		dictionary["Tier2DimensionsExpanderHeader"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🌐 二级轮盘几何形态与尺寸 (展开微调)",
			[LanguageCode.ZhTw] = "🌐 二級輪盤幾何形態與尺寸 (展開微調)",
			[LanguageCode.En] = "🌐 Tier-2 Wheel Geometry & Dimensions (Expand to Fine-Tune)",
			[LanguageCode.Ja] = "🌐 第2階層ホイール幾何形状と寸法 (展開して微調整)"
		};
		dictionary["Tab1_SubDimensionsNotice"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🌟 当前正在单独调节二级级联轮盘专属尺寸，与一级轮盘完全独立互不影响。",
			[LanguageCode.ZhTw] = "🌟 當前正在單獨調節二級級聯輪盤專屬尺寸，與一級輪盤完全獨立互不影響。",
			[LanguageCode.En] = "🌟 Currently adjusting Tier-2 cascade wheel dimensions independently from Tier-1.",
			[LanguageCode.Ja] = "🌟 現在、第1階層とは独立して第2階層カスケードホイールの寸法を個別に調整中。"
		};
		dictionary["Tab1_SubOuterRadiusLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "二级轮盘整体外径:",
			[LanguageCode.ZhTw] = "二級輪盤整體外徑:",
			[LanguageCode.En] = "Tier-2 Outer Radius:",
			[LanguageCode.Ja] = "第2階層ホイール外半径:"
		};
		dictionary["Tab1_SubGapLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "二级与一级轮盘间距:",
			[LanguageCode.ZhTw] = "二級與一級輪盤間距:",
			[LanguageCode.En] = "Tier-2 to Tier-1 Gap:",
			[LanguageCode.Ja] = "第2階層と第1階層のホイール間隔:"
		};
		dictionary["Tab1_SubCornerRadiusLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "二级扇区边缘平滑倒角:",
			[LanguageCode.ZhTw] = "二級扇區邊緣平滑倒角:",
			[LanguageCode.En] = "Tier-2 Sector Corner Radius:",
			[LanguageCode.Ja] = "第2階層セクター角丸半径:"
		};
		dictionary["Tab1_SubIconSizeLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "二级轮盘图标尺寸:",
			[LanguageCode.ZhTw] = "二級輪盤圖示尺寸:",
			[LanguageCode.En] = "Tier-2 Icon Size:",
			[LanguageCode.Ja] = "第2階層アイコンサイズ:"
		};
		dictionary["Tab1_SubFontSizeLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "二级轮盘字体字号:",
			[LanguageCode.ZhTw] = "二級輪盤字體字號:",
			[LanguageCode.En] = "Tier-2 Font Size:",
			[LanguageCode.Ja] = "第2階層フォントサイズ:"
		};
		dictionary["BtnResetSubDimensions"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔄 恢复二级轮盘默认尺寸",
			[LanguageCode.ZhTw] = "🔄 恢復二級輪盤預設尺寸",
			[LanguageCode.En] = "🔄 Reset Tier-2 to Default Dimensions",
			[LanguageCode.Ja] = "🔄 第2階層をデフォルト寸法にリセット"
		};
		dictionary["LayoutOptionsSectionTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "图标与排版选项",
			[LanguageCode.ZhTw] = "圖示與排版選項",
			[LanguageCode.En] = "Icon & Layout Options",
			[LanguageCode.Ja] = "アイコンとレイアウトのオプション"
		};
		dictionary["LayoutModeItemBoth"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "图标 + 文字 (双行居中)",
			[LanguageCode.ZhTw] = "圖示 + 文字 (雙行居中)",
			[LanguageCode.En] = "Icon + Text (Centered 2-line)",
			[LanguageCode.Ja] = "アイコン + テキスト (中央揃え2行)"
		};
		dictionary["LayoutModeItemIconOnly"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "仅显示图标 (极大化居中)",
			[LanguageCode.ZhTw] = "僅顯示圖示 (極大化居中)",
			[LanguageCode.En] = "Icon Only (Maximized Center)",
			[LanguageCode.Ja] = "アイコンのみ (最大化中央)"
		};
		dictionary["LayoutModeItemTextOnly"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "仅显示文字 (纯文字居中)",
			[LanguageCode.ZhTw] = "僅顯示文字 (純文字居中)",
			[LanguageCode.En] = "Text Only (Pure Text Center)",
			[LanguageCode.Ja] = "テキストのみ (テキスト中央)"
		};
		dictionary["WheelFontItemSystem"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🖥️ 系统默认",
			[LanguageCode.ZhTw] = "🖥️ 系統預設",
			[LanguageCode.En] = "🖥️ System Default",
			[LanguageCode.Ja] = "🖥️ システムデフォルト"
		};
		dictionary["WheelFontItemYaHei"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔤 微软雅黑",
			[LanguageCode.ZhTw] = "🔤 微軟雅黑",
			[LanguageCode.En] = "🔤 Microsoft YaHei",
			[LanguageCode.Ja] = "🔤 メイリオ / 微软雅黑"
		};
		dictionary["WheelFontItemHarmony"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔤 鸿蒙字体",
			[LanguageCode.ZhTw] = "🔤 鴻蒙字體",
			[LanguageCode.En] = "🔤 HarmonyOS Sans",
			[LanguageCode.Ja] = "🔤 HarmonyOS フォント"
		};
		dictionary["WheelFontItemPingFang"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔤 苹方字体",
			[LanguageCode.ZhTw] = "🔤 蘋方字體",
			[LanguageCode.En] = "🔤 PingFang SC",
			[LanguageCode.Ja] = "🔤 PingFang フォント"
		};
		dictionary["WheelFontItemMiSans"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔤 小米兰亭",
			[LanguageCode.ZhTw] = "🔤 小米蘭亭",
			[LanguageCode.En] = "🔤 MiSans",
			[LanguageCode.Ja] = "🔤 MiSans フォント"
		};
		dictionary["WheelFontItemSimHei"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔤 黑体",
			[LanguageCode.ZhTw] = "🔤 黑體",
			[LanguageCode.En] = "🔤 SimHei",
			[LanguageCode.Ja] = "🔤 ゴシック体"
		};
		dictionary["WheelFontItemKaiTi"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔤 楷体",
			[LanguageCode.ZhTw] = "🔤 楷體",
			[LanguageCode.En] = "🔤 KaiTi",
			[LanguageCode.Ja] = "🔤 明朝体 / 楷書体"
		};
		dictionary["WheelFontItemConsolas"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔤 等宽代码体",
			[LanguageCode.ZhTw] = "🔤 等寬程式碼體",
			[LanguageCode.En] = "🔤 Monospace Code",
			[LanguageCode.Ja] = "🔤 等幅コードフォント"
		};
		dictionary["BtnResetTextOffset"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔄 位置归位",
			[LanguageCode.ZhTw] = "🔄 位置歸位",
			[LanguageCode.En] = "🔄 Reset Position",
			[LanguageCode.Ja] = "🔄 位置リセット"
		};
		dictionary["TipResetTextOffset"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "一键将文字相对位置与水平/垂直偏移恢复为默认",
			[LanguageCode.ZhTw] = "一鍵將文字相對位置與水平/垂直偏移恢復為預設",
			[LanguageCode.En] = "Reset text relative position and horizontal/vertical offsets to default",
			[LanguageCode.Ja] = "テキストの相対位置と水平/垂直オフセットをデフォルトにリセット"
		};
		dictionary["PlacementItemBottom"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⬇️ 图标下方 (默认)",
			[LanguageCode.ZhTw] = "⬇️ 圖示下方 (預設)",
			[LanguageCode.En] = "⬇️ Below Icon (Default)",
			[LanguageCode.Ja] = "⬇️ アイコンの下 (デフォルト)"
		};
		dictionary["PlacementItemTop"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⬆️ 图标上方",
			[LanguageCode.ZhTw] = "⬆️ 圖示上方",
			[LanguageCode.En] = "⬆️ Above Icon",
			[LanguageCode.Ja] = "⬆️ アイコンの上"
		};
		dictionary["Tab1_TextOffsetXLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "水平 X:",
			[LanguageCode.ZhTw] = "水平 X:",
			[LanguageCode.En] = "Horizontal X:",
			[LanguageCode.Ja] = "水平 X:"
		};
		dictionary["Tab1_TextOffsetYLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "垂直 Y:",
			[LanguageCode.ZhTw] = "垂直 Y:",
			[LanguageCode.En] = "Vertical Y:",
			[LanguageCode.Ja] = "垂直 Y:"
		};
		dictionary["CoreSectionTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "中心核心圆与图案文字设置",
			[LanguageCode.ZhTw] = "中心核心圓與圖案文字設定",
			[LanguageCode.En] = "Center Core Circle & Pattern/Text Settings",
			[LanguageCode.Ja] = "センターコア＆パターン・テキスト設定"
		};
		dictionary["ShowCoreIconTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "启用中心图案/图标显示",
			[LanguageCode.ZhTw] = "啟用中心圖案/圖示顯示",
			[LanguageCode.En] = "Enable Center Pattern/Icon Display",
			[LanguageCode.Ja] = "センターパターン/アイコン表示を有効化"
		};
		dictionary["Tab1_CorePatternTypeLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "图案类型:",
			[LanguageCode.ZhTw] = "圖案類型:",
			[LanguageCode.En] = "Pattern Type:",
			[LanguageCode.Ja] = "パターンタイプ:"
		};
		dictionary["CoreIconTypeItemCrosshair"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "精准十字准星",
			[LanguageCode.ZhTw] = "精準十字準星",
			[LanguageCode.En] = "Precision Crosshair",
			[LanguageCode.Ja] = "高精度クロスヘア"
		};
		dictionary["CoreIconTypeItemWindows"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "Windows 徽标",
			[LanguageCode.ZhTw] = "Windows 徽標",
			[LanguageCode.En] = "Windows Logo",
			[LanguageCode.Ja] = "Windows ロゴ"
		};
		dictionary["CoreIconTypeItemBreatheDot"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "中心呼吸光点",
			[LanguageCode.ZhTw] = "中心呼吸光點",
			[LanguageCode.En] = "Breathing Glow Dot",
			[LanguageCode.Ja] = "センターブリージングライト"
		};
		dictionary["CoreIconTypeItemHomeReturn"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "主页与返回",
			[LanguageCode.ZhTw] = "首頁與返回",
			[LanguageCode.En] = "Home & Back",
			[LanguageCode.Ja] = "ホーム＆戻る"
		};
		dictionary["CoreIconTypeItemCompassStar"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "八向罗盘星芒",
			[LanguageCode.ZhTw] = "八向羅盤星芒",
			[LanguageCode.En] = "8-Point Compass Star",
			[LanguageCode.Ja] = "8方向コンパススター"
		};
		dictionary["CoreIconTypeItemCatPaw"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "猫爪肉垫图案",
			[LanguageCode.ZhTw] = "貓爪肉墊圖案",
			[LanguageCode.En] = "Cat Paw Pad",
			[LanguageCode.Ja] = "猫の肉球パターン"
		};
		dictionary["CoreIconTypeItemVector"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "矢量图标库选择",
			[LanguageCode.ZhTw] = "向量圖示庫選擇",
			[LanguageCode.En] = "Vector Icon Library",
			[LanguageCode.Ja] = "ベクターアイコンライブラリ選択"
		};
		dictionary["CoreIconTypeItemCustomImage"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "本地自定义图片",
			[LanguageCode.ZhTw] = "本地自訂圖片",
			[LanguageCode.En] = "Local Custom Image",
			[LanguageCode.Ja] = "ローカルカスタム画像"
		};
		dictionary["CustomCoreIconNone"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "未选择图标",
			[LanguageCode.ZhTw] = "未選擇圖示",
			[LanguageCode.En] = "No Icon Selected",
			[LanguageCode.Ja] = "アイコン未選択"
		};
		dictionary["BtnPickCoreIcon"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "选择图标...",
			[LanguageCode.ZhTw] = "選擇圖示...",
			[LanguageCode.En] = "Select Icon...",
			[LanguageCode.Ja] = "アイコンを選択..."
		};
		dictionary["TipCoreImagePath"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "自定义图片本地路径",
			[LanguageCode.ZhTw] = "自訂圖片本地路徑",
			[LanguageCode.En] = "Local path to custom image",
			[LanguageCode.Ja] = "カスタム画像のローカルパス"
		};
		dictionary["BtnBrowseCoreImage"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "浏览图片...",
			[LanguageCode.ZhTw] = "瀏覽圖片...",
			[LanguageCode.En] = "Browse Image...",
			[LanguageCode.Ja] = "画像を参照..."
		};
		dictionary["BtnClearCoreImage"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "清除",
			[LanguageCode.ZhTw] = "清除",
			[LanguageCode.En] = "Clear",
			[LanguageCode.Ja] = "クリア"
		};
		dictionary["CoreTextOptionsSectionTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "中心文字与选中显示定制",
			[LanguageCode.ZhTw] = "中心文字與選中顯示自訂",
			[LanguageCode.En] = "Center Text & Selection Display Customization",
			[LanguageCode.Ja] = "センターテキスト＆選択表示カスタマイズ"
		};
		dictionary["CoreTextColorTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "中心文字颜色:",
			[LanguageCode.ZhTw] = "中心文字顏色:",
			[LanguageCode.En] = "Center Text Color:",
			[LanguageCode.Ja] = "センターテキスト色:"
		};
		dictionary["LayerStyleItemDark"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🌌 沉浸深邃暗黑 (推荐)",
			[LanguageCode.ZhTw] = "🌌 沉浸深邃暗黑 (推薦)",
			[LanguageCode.En] = "🌌 Immersive Deep Dark (Recommended)",
			[LanguageCode.Ja] = "🌌 ディープダーク（推奨）"
		};
		dictionary["LayerStyleItemAuroraBlue"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🧊 晶莹极光蓝透",
			[LanguageCode.ZhTw] = "🧊 晶瑩極光藍透",
			[LanguageCode.En] = "🧊 Aurora Translucent Blue",
			[LanguageCode.Ja] = "🧊 オーロラクリスタルブルー"
		};
		dictionary["LayerStyleItemObsidianPurple"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔮 钛金晶透曜紫",
			[LanguageCode.ZhTw] = "🔮 鈦金晶透曜紫",
			[LanguageCode.En] = "🔮 Titanium Crystal Purple",
			[LanguageCode.Ja] = "🔮 チタンクリスタルパープル"
		};
		dictionary["LayerStyleItemLight"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚪ 极简透白浅色",
			[LanguageCode.ZhTw] = "⚪ 極簡透白淺色",
			[LanguageCode.En] = "⚪ Minimalist Translucent Light",
			[LanguageCode.Ja] = "⚪ ミニマルクリアライト"
		};
		dictionary["LayerStyleItemFollowTheme"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔘 跟随当前轮盘主题",
			[LanguageCode.ZhTw] = "🔘 跟隨當前輪盤主題",
			[LanguageCode.En] = "🔘 Follow Current Wheel Theme",
			[LanguageCode.Ja] = "🔘 現在のホイールテーマに従う"
		};
		dictionary["LayerStyleItemCustom"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🎨 完全自定义色彩",
			[LanguageCode.ZhTw] = "🎨 完全自訂色彩",
			[LanguageCode.En] = "🎨 Fully Custom Colors",
			[LanguageCode.Ja] = "🎨 完全カスタムカラー"
		};
		dictionary["LayerIconItemStar"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🌟 璀璨星芒 (默认)",
			[LanguageCode.ZhTw] = "🌟 璀璨星芒 (預設)",
			[LanguageCode.En] = "🌟 Radiant Star (Default)",
			[LanguageCode.Ja] = "🌟 輝く星（デフォルト）"
		};
		dictionary["LayerIconItemSnowflake"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "❄️ 冰晶雪花",
			[LanguageCode.ZhTw] = "❄️ 冰晶雪花",
			[LanguageCode.En] = "❄️ Crystal Snowflake",
			[LanguageCode.Ja] = "❄️ クリスタルスノー"
		};
		dictionary["LayerIconItemGalaxy"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🌀 宇宙星盘",
			[LanguageCode.ZhTw] = "🌀 宇宙星盤",
			[LanguageCode.En] = "🌀 Cosmic Galaxy",
			[LanguageCode.Ja] = "🌀 コズミックスター"
		};
		dictionary["LayerIconItemBolt"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚡ 极速闪电",
			[LanguageCode.ZhTw] = "⚡ 極速閃電",
			[LanguageCode.En] = "⚡ Lightning Bolt",
			[LanguageCode.Ja] = "⚡ スピードライトニング"
		};
		dictionary["LayerIconItemCrosshair"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🎯 准星靶心",
			[LanguageCode.ZhTw] = "🎯 準星靶心",
			[LanguageCode.En] = "🎯 Crosshair Bullseye",
			[LanguageCode.Ja] = "🎯 ターゲットブルズアイ"
		};
		dictionary["LayerIconItemGem"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "💎 纯净宝石",
			[LanguageCode.ZhTw] = "💎 純淨寶石",
			[LanguageCode.En] = "💎 Pristine Gem",
			[LanguageCode.Ja] = "💎 ピュアジェム"
		};
		dictionary["LayerIconItemNone"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🚫 无前置图标",
			[LanguageCode.ZhTw] = "🚫 無前置圖示",
			[LanguageCode.En] = "🚫 No Leading Icon",
			[LanguageCode.Ja] = "🚫 前置アイコンなし"
		};
		dictionary["Tab1_LayerCustomColorsSectionLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🎨 徽标色彩微调:",
			[LanguageCode.ZhTw] = "🎨 徽標色彩微調:",
			[LanguageCode.En] = "🎨 Badge Color Fine-Tuning:",
			[LanguageCode.Ja] = "🎨 バッジカラー微調整:"
		};
		dictionary["Tab1_LayerBgLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "徽标底色:",
			[LanguageCode.ZhTw] = "徽標底色:",
			[LanguageCode.En] = "Badge Background:",
			[LanguageCode.Ja] = "バッジ背景色:"
		};
		dictionary["Tab1_LayerBorderLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "边框颜色:",
			[LanguageCode.ZhTw] = "邊框顏色:",
			[LanguageCode.En] = "Border Color:",
			[LanguageCode.Ja] = "境界線色:"
		};
		dictionary["Tab1_LayerTextLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "文字色彩:",
			[LanguageCode.ZhTw] = "文字色彩:",
			[LanguageCode.En] = "Text Color:",
			[LanguageCode.Ja] = "テキスト色:"
		};
		dictionary["Tab1_LivePreviewTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "实时交互画布",
			[LanguageCode.ZhTw] = "即時互動畫布",
			[LanguageCode.En] = "Live Interactive Canvas",
			[LanguageCode.Ja] = "リアルタイムプレビューキャンバス"
		};
		dictionary["Tab1_LivePreviewBadge"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "60FPS 同步渲染",
			[LanguageCode.ZhTw] = "60FPS 同步渲染",
			[LanguageCode.En] = "60FPS Synchronized Rendering",
			[LanguageCode.Ja] = "60FPS 同期レンダリング"
		};
		dictionary["Tab1_LivePreviewHint"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "💡 移动鼠标至下方轮盘可实时测试高亮与磁吸手感",
			[LanguageCode.ZhTw] = "💡 移動滑鼠至下方輪盤可即時測試高亮與磁吸手感",
			[LanguageCode.En] = "💡 Hover mouse over wheel below to test highlight and snapping feel",
			[LanguageCode.Ja] = "💡 下のホイールにマウスを合わせると、ハイライトと吸着の感触をテストできます"
		};
		dictionary["TipPreviewZoomOut"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "缩小视图 (或使用鼠标滚轮)",
			[LanguageCode.ZhTw] = "縮小檢視 (或使用滑鼠滾輪)",
			[LanguageCode.En] = "Zoom Out (or use mouse wheel)",
			[LanguageCode.Ja] = "縮小 (またはマウスホイールを使用)"
		};
		dictionary["TipPreviewZoomReset"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "点击复位为 100%",
			[LanguageCode.ZhTw] = "點擊重設為 100%",
			[LanguageCode.En] = "Click to reset to 100%",
			[LanguageCode.Ja] = "クリックして100%にリセット"
		};
		dictionary["TipPreviewZoomIn"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "放大视图 (或使用鼠标滚轮)",
			[LanguageCode.ZhTw] = "放大檢視 (或使用滑鼠滾輪)",
			[LanguageCode.En] = "Zoom In (or use mouse wheel)",
			[LanguageCode.Ja] = "拡大 (またはマウスホイールを使用)"
		};
		dictionary["TipPreviewResetView"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "重置视图位置与缩放 (双击画布空白处也可复位)",
			[LanguageCode.ZhTw] = "重設檢視位置與縮放 (按兩下畫布空白處也可重設)",
			[LanguageCode.En] = "Reset view position and zoom (or double-click empty canvas)",
			[LanguageCode.Ja] = "表示位置とズームをリセット (キャンバスの空白部分をダブルクリックでもリセット)"
		};
		dictionary["BtnResetAllGeometry"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "一键重置为推荐几何尺寸",
			[LanguageCode.ZhTw] = "一鍵重設為推薦幾何尺寸",
			[LanguageCode.En] = "One-Click Reset to Recommended Dimensions",
			[LanguageCode.Ja] = "推奨寸法にワンクリックでリセット"
		};
		dictionary["BtnResetSlotLayoutBatch"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔄 批量恢复继承全局",
			[LanguageCode.ZhTw] = "🔄 批次恢復繼承全域",
			[LanguageCode.En] = "🔄 Batch Reset to Inherit Global",
			[LanguageCode.Ja] = "🔄 一括で全体継承にリセット"
		};
		dictionary["Tier1MainWheel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "一级主轮盘",
			[LanguageCode.ZhTw] = "一級主輪盤",
			[LanguageCode.En] = "Tier-1 Wheel",
			[LanguageCode.Ja] = "第1階層メインホイール"
		};
		dictionary["Tier2SubWheel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "二级级联轮盘",
			[LanguageCode.ZhTw] = "二級級聯輪盤",
			[LanguageCode.En] = "Tier-2 Cascade Wheel",
			[LanguageCode.Ja] = "第2階層カスケードホイール"
		};
		dictionary["ActionNotConfigured"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "未设置动作",
			[LanguageCode.ZhTw] = "未設定動作",
			[LanguageCode.En] = "Action Not Configured",
			[LanguageCode.Ja] = "アクション未設定"
		};
		dictionary["CustomizingSlotFormat"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "📍 正在定制: {0} - 扇区 {1} [{2}]: {3}",
			[LanguageCode.ZhTw] = "📍 正在自訂: {0} - 扇區 {1} [{2}]: {3}",
			[LanguageCode.En] = "📍 Customizing: {0} - Sector {1} [{2}]: {3}",
			[LanguageCode.Ja] = "📍 カスタマイズ中: {0} - セクター {1} [{2}]: {3}"
		};
		dictionary["CustomizingSubSlotFormat"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "📍 正在定制: {0} [{1}] -> 子项 {2}: {3}",
			[LanguageCode.ZhTw] = "📍 正在自訂: {0} [{1}] -> 子項 {2}: {3}",
			[LanguageCode.En] = "📍 Customizing: {0} [{1}] -> Sub-item {2}: {3}",
			[LanguageCode.Ja] = "📍 カスタマイズ中: {0} [{1}] -> サブ項目 {2}: {3}"
		};
		dictionary["CustomizingBatchFormat"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🎯 批量修改模式 (已多选 {0} 个扇区: {1})",
			[LanguageCode.ZhTw] = "🎯 批次修改模式 (已多選 {0} 個扇區: {1})",
			[LanguageCode.En] = "🎯 Batch Edit Mode ({0} sectors selected: {1})",
			[LanguageCode.Ja] = "🎯 一括編集モード ({0} 個のセクターを選択: {1})"
		};
		dictionary["PreviewLayerFormat"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "第 {0} 层 ({0}/{1})",
			[LanguageCode.ZhTw] = "第 {0} 層 ({0}/{1})",
			[LanguageCode.En] = "Layer {0} ({0}/{1})",
			[LanguageCode.Ja] = "レイヤー {0} ({0}/{1})"
		};
		dictionary["BatchLayoutHint"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "💡 按住 Ctrl 点击可继续增减选择；下方选项将统一批量应用至全部选中扇区",
			[LanguageCode.ZhTw] = "💡 按住 Ctrl 點擊可繼續增減選擇；下方選項將統一批次套用至全部選中扇區",
			[LanguageCode.En] = "💡 Hold Ctrl and click to add/remove selection; options below will be batch applied to all selected sectors",
			[LanguageCode.Ja] = "💡 Ctrlを押しながらクリックして選択を追加/削除。下のオプションは選択したすべてのセクターに一括適用されます"
		};
				dictionary["LayerLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🌀 轮盘层:",
			[LanguageCode.ZhTw] = "🌀 輪盤層:",
			[LanguageCode.En] = "🌀 Wheel Layer:",
			[LanguageCode.Ja] = "🌀 ホイールレイヤー:"
		};
		dictionary["AddLayerBtnText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "➕ 加层",
			[LanguageCode.ZhTw] = "➕ 加層",
			[LanguageCode.En] = "➕ Add Layer",
			[LanguageCode.Ja] = "➕ レイヤー追加"
		};
		dictionary["AddLayerBtnToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "新增一层独立轮盘配置（支持无限多层）",
			[LanguageCode.ZhTw] = "新增一層獨立輪盤設定（支援無限多層）",
			[LanguageCode.En] = "Add a new independent wheel layer (unlimited layers supported)",
			[LanguageCode.Ja] = "新しい独立したホイールレイヤーを追加（無制限）"
		};
		dictionary["CopyLayerBtnText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "📑 复制",
			[LanguageCode.ZhTw] = "📑 複製",
			[LanguageCode.En] = "📑 Copy",
			[LanguageCode.Ja] = "📑 複製"
		};
		dictionary["CopyLayerBtnToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "复制当前层的所有扇区动作与中心核圆到新层",
			[LanguageCode.ZhTw] = "複製目前層的所有扇區動作與中心核圓至新層",
			[LanguageCode.En] = "Copy all sector actions and center core of current layer to a new layer",
			[LanguageCode.Ja] = "現在のレイヤーの全セクターアクションと中心コアを新規レイヤーに複製"
		};
		dictionary["RenameLayerBtnToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "重命名当前轮盘层",
			[LanguageCode.ZhTw] = "重新命名目前輪盤層",
			[LanguageCode.En] = "Rename current wheel layer",
			[LanguageCode.Ja] = "現在のホイールレイヤーの名前を変更"
		};
		dictionary["DeleteLayerBtnToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "删除当前轮盘层（至少保留一层）",
			[LanguageCode.ZhTw] = "刪除目前輪盤層（至少保留一層）",
			[LanguageCode.En] = "Delete current wheel layer (at least one layer must be kept)",
			[LanguageCode.Ja] = "現在のホイールレイヤーを削除（最低1レイヤー保持）"
		};
		dictionary["LayerSwitchTriggerLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔄 切换:",
			[LanguageCode.ZhTw] = "🔄 切換:",
			[LanguageCode.En] = "🔄 Switch:",
			[LanguageCode.Ja] = "🔄 切替:"
		};
		dictionary["LayerSwitchTriggerComboBoxToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "多层轮盘切换方式：支持鼠标滚轮上下滑动切换或 Tab 键循环切换",
			[LanguageCode.ZhTw] = "多層輪盤切換方式：支援滑鼠滾輪上下滾動切換或 Tab 鍵循環切換",
			[LanguageCode.En] = "Multi-layer wheel switching method: switch via mouse wheel scroll or Tab key cycle",
			[LanguageCode.Ja] = "マルチレイヤー切替方式：マウスホイールの上下スクロールまたはTabキー巡回切替"
		};
		dictionary["LayerSwitchModeScroll"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🖱️ 滚轮切换",
			[LanguageCode.ZhTw] = "🖱️ 滾輪切換",
			[LanguageCode.En] = "🖱️ Wheel Scroll",
			[LanguageCode.Ja] = "🖱️ マウスホイール"
		};
		dictionary["LayerSwitchModeTab"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⌨️ Tab 键切换",
			[LanguageCode.ZhTw] = "⌨️ Tab 鍵切換",
			[LanguageCode.En] = "⌨️ Tab Key",
			[LanguageCode.Ja] = "⌨️ Tabキー"
		};
		dictionary["GesturesPageSubheader"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "支持针对不同前台应用程序设置专属的多向手势轮盘、按键动作、中心核圆与级联子动作。",
			[LanguageCode.ZhTw] = "支援針對不同前景應用程式設定專屬的多向手勢輪盤、按鍵動作、中心核圓與級聯子動作。",
			[LanguageCode.En] = "Configure dedicated radial gesture wheels, hotkeys, center core, and cascaded sub-actions for different foreground applications.",
			[LanguageCode.Ja] = "前面の各アプリケーションに応じた専用の多方向ジェスチャーホイール、ショートカット、中心コア、カスケードサブアクションを設定できます。"
		};
		dictionary["MappingsViewModeCanvasText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🎯 画布联动精调 (推荐)",
			[LanguageCode.ZhTw] = "🎯 畫布聯動精調 (推薦)",
			[LanguageCode.En] = "🎯 Interactive Canvas (Recommended)",
			[LanguageCode.Ja] = "🎯 インタラクティブキャンバス（推奨）"
		};
		dictionary["MappingsViewModeListText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "📋 紧凑全览列表",
			[LanguageCode.ZhTw] = "📋 緊湊全覽清單",
			[LanguageCode.En] = "📋 Compact Overview List",
			[LanguageCode.Ja] = "📋 コンパクト一覧リスト"
		};
		dictionary["CurrentProfileLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "当前配置方案:",
			[LanguageCode.ZhTw] = "目前設定方案:",
			[LanguageCode.En] = "Current Profile:",
			[LanguageCode.Ja] = "現在のプロファイル:"
		};
		dictionary["AddProfileBtnText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "➕ 新增",
			[LanguageCode.ZhTw] = "➕ 新增",
			[LanguageCode.En] = "➕ Add",
			[LanguageCode.Ja] = "➕ 追加"
		};
		dictionary["AddProfileBtnToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "添加新的轮盘配置方案（点击可选择从程序添加、捕捉窗口或自定义命名）",
			[LanguageCode.ZhTw] = "新增輪盤設定方案（點擊可選擇從程式新增、捕捉視窗或自訂命名）",
			[LanguageCode.En] = "Add a new wheel profile (choose from installed app, window capture, or custom name)",
			[LanguageCode.Ja] = "新しいプロファイルを追加（インストール済みアプリ、ウィンドウキャプチャ、またはカスタム名から選択）"
		};
		dictionary["AddProfileFromProgram"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🖥️ 从已安装软件中添加 (专属程序配置)...",
			[LanguageCode.ZhTw] = "🖥️ 從已安裝軟體中新增 (專屬程式設定)...",
			[LanguageCode.En] = "🖥️ Add from Installed Programs (App Profile)...",
			[LanguageCode.Ja] = "🖥️ インストール済みソフトから追加（専用プロファイル）..."
		};
		dictionary["AddProfileCaptureWindow"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🎯 捕捉运行中窗口添加 (专属程序配置)...",
			[LanguageCode.ZhTw] = "🎯 捕捉執行中視窗新增 (專屬程式設定)...",
			[LanguageCode.En] = "🎯 Capture Running Window (App Profile)...",
			[LanguageCode.Ja] = "🎯 実行中ウィンドウからキャプチャ（専用プロファイル）..."
		};
		dictionary["AddProfileBrowseExe"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "📁 浏览本地程序文件添加 (.exe / .lnk)...",
			[LanguageCode.ZhTw] = "📁 瀏覽本機程式檔案新增 (.exe / .lnk)...",
			[LanguageCode.En] = "📁 Browse Local Executable File (.exe / .lnk)...",
			[LanguageCode.Ja] = "📁 ローカル実行ファイルを参照して追加 (.exe / .lnk)..."
		};
		dictionary["AddProfileCustom"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "✏️ 新建自定义名称方案 (工作流/模式配置)...",
			[LanguageCode.ZhTw] = "✏️ 新建自訂名稱方案 (工作流程/模式設定)...",
			[LanguageCode.En] = "✏️ Create Custom Named Profile (Workflow/Mode)...",
			[LanguageCode.Ja] = "✏️ カスタム名プロファイルを新規作成（ワークフロー/モード）..."
		};
		dictionary["RenameProfileBtnText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "✏️ 重命名",
			[LanguageCode.ZhTw] = "✏️ 重新命名",
			[LanguageCode.En] = "✏️ Rename",
			[LanguageCode.Ja] = "✏️ 名前変更"
		};
		dictionary["RenameProfileBtnToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "重命名选中的配置方案",
			[LanguageCode.ZhTw] = "重新命名選取的設定方案",
			[LanguageCode.En] = "Rename selected profile",
			[LanguageCode.Ja] = "選択したプロファイルの名前を変更"
		};
		dictionary["DeleteProfileBtnToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "删除当前选中的配置方案",
			[LanguageCode.ZhTw] = "刪除目前選取的設定方案",
			[LanguageCode.En] = "Delete selected profile",
			[LanguageCode.Ja] = "選択したプロファイルを削除"
		};
		dictionary["GlobalProfileHint"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "全局通用基础方案：当活动前台程序未配置专属轮盘时，手势将自动应用此全局方案。",
			[LanguageCode.ZhTw] = "全域通用基礎方案：當使用中的前景程式未設定專屬輪盤時，手勢將自動套用此全域方案。",
			[LanguageCode.En] = "Global default profile: when the active foreground app has no dedicated wheel, gestures will automatically use this global profile.",
			[LanguageCode.Ja] = "グローバル基本プロファイル：アクティブな前面アプリに専用ホイールが設定されていない場合、自動的にこのグローバル設定が適用されます。"
		};
		dictionary["ProfileBoundProcessesLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🎯 绑定程序情景:",
			[LanguageCode.ZhTw] = "🎯 綁定程式情境:",
			[LanguageCode.En] = "🎯 Bound Processes:",
			[LanguageCode.Ja] = "🎯 バインド対象プロセス:"
		};
		dictionary["ProfileBoundProcessesToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "目标程序进程名（如 photoshop.exe 或 code.exe）。支持以英文逗号分隔多个进程。",
			[LanguageCode.ZhTw] = "目標程式處理程序名稱（如 photoshop.exe 或 code.exe）。支援以半形逗號分隔多個處理程序。",
			[LanguageCode.En] = "Target process name (e.g. photoshop.exe or code.exe). Multiple processes separated by commas.",
			[LanguageCode.Ja] = "対象プロセス名（例: photoshop.exe または code.exe）。カンマ区切りで複数指定可能。"
		};
		dictionary["ProfileCaptureWindowBtnText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🎯 捕捉窗口...",
			[LanguageCode.ZhTw] = "🎯 捕捉視窗...",
			[LanguageCode.En] = "🎯 Capture Window...",
			[LanguageCode.Ja] = "🎯 ウィンドウ捕捉..."
		};
		dictionary["ProfileCaptureWindowBtnToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "直接点击桌面上运行中的目标软件窗口，自动识别并绑定其进程",
			[LanguageCode.ZhTw] = "直接點擊桌面上執行中的目標軟體視窗，自動識別並綁定其處理程序",
			[LanguageCode.En] = "Click any running window on desktop to automatically detect and bind its process",
			[LanguageCode.Ja] = "デスクトップ上で実行中のウィンドウをクリックしてプロセスを自動識別・バインド"
		};
		dictionary["ProfilePickProgramBtnText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🖥️ 软件库...",
			[LanguageCode.ZhTw] = "🖥️ 軟體庫...",
			[LanguageCode.En] = "🖥️ App Library...",
			[LanguageCode.Ja] = "🖥️ アプリ一覧..."
		};
		dictionary["ProfilePickProgramBtnToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "从已安装的软件列表中选择程序并绑定",
			[LanguageCode.ZhTw] = "從已安裝的軟體清單中選擇程式並綁定",
			[LanguageCode.En] = "Select an application from installed programs list to bind",
			[LanguageCode.Ja] = "インストール済みアプリ一覧からプログラムを選択してバインド"
		};
		dictionary["ProfileBrowseExeBtnText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "📁 浏览...",
			[LanguageCode.ZhTw] = "📁 瀏覽...",
			[LanguageCode.En] = "📁 Browse...",
			[LanguageCode.Ja] = "📁 参照..."
		};
		dictionary["ProfileBrowseExeBtnToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "浏览选取本地可执行程序文件 (.exe / .lnk)",
			[LanguageCode.ZhTw] = "瀏覽選取本機可執行程式檔案 (.exe / .lnk)",
			[LanguageCode.En] = "Browse and select local executable file (.exe / .lnk)",
			[LanguageCode.Ja] = "ローカルの実行可能ファイル (.exe / .lnk) を参照"
		};
		dictionary["ProfileBoundProcessesHint"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "💡 提示：在此程序处于前台活跃状态时唤起轮盘将自动应用本方案。支持以英文逗号分隔多个进程名 (例如 chrome.exe, msedge.exe)。",
			[LanguageCode.ZhTw] = "💡 提示：在此程式處於前景使用中狀態時喚起輪盤將自動套用本方案。支援以半形逗號分隔多個處理程序名稱 (例如 chrome.exe, msedge.exe)。",
			[LanguageCode.En] = "💡 Hint: When this program is active in foreground, invoking the wheel will automatically apply this profile. Multiple process names can be comma-separated (e.g. chrome.exe, msedge.exe).",
			[LanguageCode.Ja] = "💡 ヒント：このプログラムが前面でアクティブな時にホイールを呼び出すと自動適用されます。カンマ区切りで複数のプロセス名を指定できます（例: chrome.exe, msedge.exe）。"
		};
		dictionary["SectorCountLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "扇区方位数量:",
			[LanguageCode.ZhTw] = "扇區方位數量:",
			[LanguageCode.En] = "Sector Count:",
			[LanguageCode.Ja] = "セクター数:"
		};
		dictionary["SectorCount4Text"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "4 键十字方位",
			[LanguageCode.ZhTw] = "4 鍵十字方位",
			[LanguageCode.En] = "4 Sectors (Cross)",
			[LanguageCode.Ja] = "4方向（十字）"
		};
		dictionary["SectorCount8Text"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "8 键全向方位 (推荐)",
			[LanguageCode.ZhTw] = "8 鍵全向方位 (推薦)",
			[LanguageCode.En] = "8 Sectors (Omni, Recommended)",
			[LanguageCode.Ja] = "8方向（全方位・推奨）"
		};
		dictionary["SectorCount12Text"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "12 键钟表方位",
			[LanguageCode.ZhTw] = "12 鍵鐘錶方位",
			[LanguageCode.En] = "12 Sectors (Clock)",
			[LanguageCode.Ja] = "12方向（時計盤）"
		};
		dictionary["EnableGlobalInheritanceText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🌐 继承全局方案未配置槽位",
			[LanguageCode.ZhTw] = "🌐 繼承全域方案未設定位置",
			[LanguageCode.En] = "🌐 Inherit Unset Slots from Global",
			[LanguageCode.Ja] = "🌐 未設定スロットをグローバルから継承"
		};
		dictionary["EnableGlobalInheritanceToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "当专属程序方案中的某个扇区未配置动作时，自动级联继承并执行全局方案对应方位的动作",
			[LanguageCode.ZhTw] = "當專屬程式方案中的某個扇區未設定動作時，自動級聯繼承並執行全域方案對應方位的動作",
			[LanguageCode.En] = "When a sector is not configured in an app profile, automatically inherit and execute the corresponding sector action from the global profile",
			[LanguageCode.Ja] = "専用プロファイルで未設定のセクターがある場合、グローバル設定の同方向アクションを自動継承して実行します"
		};
		dictionary["FocusSlotInheritedBadgeToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "当前槽位在专属方案中未配置，已自动继承全局方案同向动作",
			[LanguageCode.ZhTw] = "目前位置在專屬方案中未設定，已自動繼承全域方案同向動作",
			[LanguageCode.En] = "Slot not configured in app profile; automatically inheriting action from global profile",
			[LanguageCode.Ja] = "専用プロファイルで未設定のため、グローバル設定の同方向アクションを自動継承しています"
		};
		dictionary["FocusSlotInheritedBadgeText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🌐 全局继承",
			[LanguageCode.ZhTw] = "🌐 全域繼承",
			[LanguageCode.En] = "🌐 Inherited",
			[LanguageCode.Ja] = "🌐 継承済み"
		};
		dictionary["FocusBackToParentBtnText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "◀ 返回父级扇区",
			[LanguageCode.ZhTw] = "◀ 返回父級扇區",
			[LanguageCode.En] = "◀ Back to Parent Sector",
			[LanguageCode.Ja] = "◀ 親セクターに戻る"
		};
		dictionary["FocusPrevSlotBtnText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "◀ 上一槽",
			[LanguageCode.ZhTw] = "◀ 上一槽",
			[LanguageCode.En] = "◀ Prev Slot",
			[LanguageCode.Ja] = "◀ 前のスロット"
		};
		dictionary["FocusNextSlotBtnText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "下一槽 ▶",
			[LanguageCode.ZhTw] = "下一槽 ▶",
			[LanguageCode.En] = "Next Slot ▶",
			[LanguageCode.Ja] = "次のスロット ▶"
		};
		dictionary["FocusCenterCoreBtnText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🎯 中心核圆",
			[LanguageCode.ZhTw] = "🎯 中心核圓",
			[LanguageCode.En] = "🎯 Center Core",
			[LanguageCode.Ja] = "🎯 中心コア"
		};
		dictionary["EnableCenterActionText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "启用中心核圆动作",
			[LanguageCode.ZhTw] = "啟用中心核圓動作",
			[LanguageCode.En] = "Enable Center Core Action",
			[LanguageCode.Ja] = "中心コアアクションを有効化"
		};
		dictionary["CenterDeadzoneReleaseHint"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "死区松开触发 · 外甩脱离取消",
			[LanguageCode.ZhTw] = "死區放開觸發 · 外甩脫離取消",
			[LanguageCode.En] = "Release in Deadzone to Trigger · Fling Out to Cancel",
			[LanguageCode.Ja] = "デッドゾーン解放でトリガー・外側フリックでキャンセル"
		};
		dictionary["CenterPresetsToggleBtnText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚡ 常用预设 ▾",
			[LanguageCode.ZhTw] = "⚡ 常用預設 ▾",
			[LanguageCode.En] = "⚡ Common Presets ▾",
			[LanguageCode.Ja] = "⚡ 定番プリセット ▾"
		};
		dictionary["CenterInfoToggleBtnText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "ℹ️ 说明 ▾",
			[LanguageCode.ZhTw] = "ℹ️ 說明 ▾",
			[LanguageCode.En] = "ℹ️ Info ▾",
			[LanguageCode.Ja] = "ℹ️ 説明 ▾"
		};
		dictionary["CenterPatternPriorityNotice"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "当前已启用自定义中心图案，轮盘中心将优先展示该图案；在中心死区内松开鼠标仍会照常触发本功能。",
			[LanguageCode.ZhTw] = "目前已啟用自訂中心圖案，輪盤中心將優先展示該圖案；在中心死區內放開滑鼠仍會照常觸發本功能。",
			[LanguageCode.En] = "Custom center pattern is active and prioritized in display; releasing in deadzone will still trigger this action.",
			[LanguageCode.Ja] = "カスタム中心パターンが有効な場合そちらが優先表示されますが、中心デッドゾーン内でマウスを離せば通常通り機能が実行されます。"
		};
		dictionary["CenterPresetFillLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "一键填入:",
			[LanguageCode.ZhTw] = "一鍵填入:",
			[LanguageCode.En] = "Quick Fill:",
			[LanguageCode.Ja] = "ワンクリック入力:"
		};
		dictionary["CenterPresetSettings"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚙️ 控制台",
			[LanguageCode.ZhTw] = "⚙️ 控制台",
			[LanguageCode.En] = "⚙️ Settings Console",
			[LanguageCode.Ja] = "⚙️ 設定画面"
		};
		dictionary["CenterPresetDesktop"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🖥️ 显示桌面",
			[LanguageCode.ZhTw] = "🖥️ 顯示桌面",
			[LanguageCode.En] = "🖥️ Show Desktop",
			[LanguageCode.Ja] = "🖥️ デスクトップ表示"
		};
		dictionary["CenterPresetLock"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔒 锁定屏幕",
			[LanguageCode.ZhTw] = "🔒 鎖定螢幕",
			[LanguageCode.En] = "🔒 Lock Screen",
			[LanguageCode.Ja] = "🔒 画面ロック"
		};
		dictionary["CenterPresetWebUrl"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🌐 常用网站",
			[LanguageCode.ZhTw] = "🌐 常用網站",
			[LanguageCode.En] = "🌐 Favorite Website",
			[LanguageCode.Ja] = "🌐 お気に入りサイト"
		};
		dictionary["CenterPresetExplorer"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "📁 资源管理",
			[LanguageCode.ZhTw] = "📁 檔案總管",
			[LanguageCode.En] = "📁 File Explorer",
			[LanguageCode.Ja] = "📁 エクスプローラー"
		};
		dictionary["CenterFlingExplanation"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "💡 外甩脱离机制说明：开启「外甩脱离取消」后，手势若在中心内径死区内释放光标，将直接触发在此配置的动作（如呼出控制台、启动工具或热键）；若需废弃/取消手势，直接向外快速甩出轮盘边缘即可。",
			[LanguageCode.ZhTw] = "💡 外甩脫離機制說明：開啟「外甩脫離取消」後，手勢若在中心內徑死區內釋放游標，將直接觸發在此設定的動作（如呼出控制台、啟動工具或快速鍵）；若需廢棄/取消手勢，直接向外快速甩出輪盤邊緣即可。",
			[LanguageCode.En] = "💡 Fling Cancellation Guide: When 'Fling Out to Cancel' is enabled, releasing the cursor inside the center deadzone triggers this action (e.g. open console, tool, hotkey); to cancel, simply fling the cursor outward past the wheel edge.",
			[LanguageCode.Ja] = "💡 外側フリックキャンセル説明：「外側フリックキャンセル」有効時、中心デッドゾーン内でカーソルを離すと本機能（設定画面、ツール、ショートカットなど）が実行されます。ジェスチャーを中止したい場合は外側へ素早くフリックします。"
		};
		dictionary["FocusTier2EmptyTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🌟 当前主扇区尚未配置二级级联子动作",
			[LanguageCode.ZhTw] = "🌟 目前主扇區尚未設定二級級聯子動作",
			[LanguageCode.En] = "🌟 No Tier-2 Sub-Actions Configured for this Sector",
			[LanguageCode.Ja] = "🌟 このセクターには第2階層サブアクションが設定されていません"
		};
		dictionary["FocusTier2EmptySubtitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "向外划动此扇区时可展开二级子菜单。支持添加 1~4 个二级子动作。",
			[LanguageCode.ZhTw] = "向外劃動此扇區時可展開二級子選單。支援新增 1~4 個二級子動作。",
			[LanguageCode.En] = "Swipe outward from this sector to expand the sub-menu. Supports 1 to 4 sub-actions.",
			[LanguageCode.Ja] = "外側にスワイプすると第2階層サブメニューが展開します。1〜4個のサブアクションを追加可能。"
		};
		dictionary["FocusAddFirstSubActionText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "➕ 添加第 1 个二级子动作",
			[LanguageCode.ZhTw] = "➕ 新增第 1 個二級子動作",
			[LanguageCode.En] = "➕ Add 1st Sub-Action",
			[LanguageCode.Ja] = "➕ 最初のサブアクションを追加"
		};
		dictionary["FocusPickIconButtonToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "点击选取矢量图标或自定SVG",
			[LanguageCode.ZhTw] = "點擊選取向量圖示或自訂SVG",
			[LanguageCode.En] = "Click to select vector icon or custom SVG",
			[LanguageCode.Ja] = "クリックしてベクターアイコンまたはカスタムSVGを選択"
		};
		dictionary["FocusIconLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "图标...",
			[LanguageCode.ZhTw] = "圖示...",
			[LanguageCode.En] = "Icon...",
			[LanguageCode.Ja] = "アイコン..."
		};
		dictionary["FocusActionNameLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "轮盘显示文本:",
			[LanguageCode.ZhTw] = "輪盤顯示文字:",
			[LanguageCode.En] = "Wheel Label:",
			[LanguageCode.Ja] = "ホイール表示テキスト:"
		};
		dictionary["FocusActionTypeLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "触发动作类型:",
			[LanguageCode.ZhTw] = "觸發動作類型:",
			[LanguageCode.En] = "Action Type:",
			[LanguageCode.Ja] = "トリガー動作タイプ:"
		};
		dictionary["FocusRestoreInheritBtnText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🌐 恢复继承全局",
			[LanguageCode.ZhTw] = "🌐 恢復繼承全域",
			[LanguageCode.En] = "🌐 Restore Global",
			[LanguageCode.Ja] = "🌐 グローバル継承に戻す"
		};
		dictionary["FocusRestoreInheritBtnToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "清除当前槽位的专属覆写，恢复继承全局方案对应方位的动作",
			[LanguageCode.ZhTw] = "清除目前位置的專屬覆寫，恢復繼承全域方案對應方位的動作",
			[LanguageCode.En] = "Clear local override for this slot and restore inheritance from global profile",
			[LanguageCode.Ja] = "このスロットの個別上書きを解除し、グローバル設定の継承に戻します"
		};
		dictionary["FocusTestActionBtnText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "▶ 测试触发",
			[LanguageCode.ZhTw] = "▶ 測試觸發",
			[LanguageCode.En] = "▶ Test Trigger",
			[LanguageCode.Ja] = "▶ テスト実行"
		};
		dictionary["TogglePauseHotkeysBtnText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⏸️ 暂停全局热键",
			[LanguageCode.ZhTw] = "⏸️ 暫停全域快速鍵",
			[LanguageCode.En] = "⏸️ Pause Global Hotkeys",
			[LanguageCode.Ja] = "⏸️ グローバルショートカットを一時停止"
		};
		dictionary["TogglePauseHotkeysBtnToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "暂停桌面系统及其他软件的所有全局快捷键，在此独占录入快捷键而不会触发系统（如 Win+D、Alt+Tab、截屏等）或其他软件",
			[LanguageCode.ZhTw] = "暫停桌面系統及其他軟體的所有全域快速鍵，在此獨佔錄入快速鍵而不會觸發系統（如 Win+D、Alt+Tab、截圖等）或其他軟體",
			[LanguageCode.En] = "Pause all global shortcuts in Windows and other apps to record combinations without triggering system hotkeys (Win+D, Alt+Tab, etc.)",
			[LanguageCode.Ja] = "システムや他アプリのグローバルショートカットを一時停止し、誤爆せずに安全に入力記録します"
		};
		dictionary["FocusHotkeyBuilderBtnText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚙️ 拼装组合",
			[LanguageCode.ZhTw] = "⚙️ 拼裝組合",
			[LanguageCode.En] = "⚙️ Hotkey Builder",
			[LanguageCode.Ja] = "⚙️ 組み合わせビルダー"
		};
		dictionary["FocusLaunchPathToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "应用程序路径",
			[LanguageCode.ZhTw] = "應用程式路徑",
			[LanguageCode.En] = "Application executable path",
			[LanguageCode.Ja] = "アプリケーション実行パス"
		};
		dictionary["FocusLaunchPickProgramBtnText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "📦 软件库选择...",
			[LanguageCode.ZhTw] = "📦 軟體庫選擇...",
			[LanguageCode.En] = "📦 Select from Apps...",
			[LanguageCode.Ja] = "📦 アプリ一覧から選択..."
		};
		dictionary["FocusLaunchPickProgramBtnToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "从已安装的软件、微软商店与开始菜单中模糊搜索选取",
			[LanguageCode.ZhTw] = "從已安裝的軟體、微軟商店與開始功能表中模糊搜尋選取",
			[LanguageCode.En] = "Search and select from installed apps, Microsoft Store, and Start Menu",
			[LanguageCode.Ja] = "インストール済みアプリ、MSストア、スタートメニューから検索選択"
		};
		dictionary["FocusLaunchCaptureWindowBtnText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🎯 捕捉运行窗口...",
			[LanguageCode.ZhTw] = "🎯 捕捉執行視窗...",
			[LanguageCode.En] = "🎯 Capture Window...",
			[LanguageCode.Ja] = "🎯 実行中ウィンドウを捕捉..."
		};
		dictionary["FocusLaunchCaptureWindowBtnToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "直接探测并捕捉桌面上正在运行的活跃窗口与程序执行路径",
			[LanguageCode.ZhTw] = "直接探測並捕捉桌面上正在執行的使用中視窗與程式執行路徑",
			[LanguageCode.En] = "Detect and capture running window and its executable path directly from desktop",
			[LanguageCode.Ja] = "デスクトップ上で実行中のウィンドウと実行パスを検出して捕捉"
		};
		dictionary["FocusLaunchBrowseExeBtnText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "📂 浏览...",
			[LanguageCode.ZhTw] = "📂 瀏覽...",
			[LanguageCode.En] = "📂 Browse...",
			[LanguageCode.Ja] = "📂 参照..."
		};
		dictionary["FocusLaunchBrowseExeBtnToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "手动浏览可执行文件或快捷方式",
			[LanguageCode.ZhTw] = "手動瀏覽可執行檔案或捷徑",
			[LanguageCode.En] = "Browse executable file or shortcut manually",
			[LanguageCode.Ja] = "実行ファイルやショートカットを手動で参照"
		};
		dictionary["FocusLaunchArgsLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "启动参数:",
			[LanguageCode.ZhTw] = "啟動參數:",
			[LanguageCode.En] = "Arguments:",
			[LanguageCode.Ja] = "引数:"
		};
		dictionary["FocusLaunchArgsToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "启动命令行参数 (可选)",
			[LanguageCode.ZhTw] = "啟動命令列參數 (選填)",
			[LanguageCode.En] = "Command line launch arguments (optional)",
			[LanguageCode.Ja] = "コマンドライン引数（省略可能）"
		};
		dictionary["FocusLaunchAsUserTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🛡️ 以常规普通权限启动 (解决高权限下外部文件无法拖入目标软件的问题)",
			[LanguageCode.ZhTw] = "🛡️ 以一般普通權限啟動 (解決高權限下外部檔案無法拖入目標軟體的問題)",
			[LanguageCode.En] = "🛡️ Launch with Standard User Privileges (Fixes drag-and-drop file restrictions under elevated admin)",
			[LanguageCode.Ja] = "🛡️ 標準ユーザー権限で起動（管理者権限下でのファイルドラッグ＆ドロップ制限を解決）"
		};
		dictionary["FocusLaunchAsUserSubtitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "当 StarPie 以管理员权限运行时，通过 Windows Shell 降权启动目标程序，恢复文件拖拽交互支持。",
			[LanguageCode.ZhTw] = "當 StarPie 以系統管理員權限執行時，透過 Windows Shell 降權啟動目標程式，恢復檔案拖曳互動支援。",
			[LanguageCode.En] = "When StarPie runs as administrator, launches target app via Windows Shell de-elevation to restore drag-and-drop functionality.",
			[LanguageCode.Ja] = "StarPieが管理者権限で動作している際、Windows Shell経由で通常権限起動しドラッグ＆ドロップ操作を復元します。"
		};
		dictionary["FocusWebUrlToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "目标网址，如 https://github.com",
			[LanguageCode.ZhTw] = "目標網址，如 https://github.com",
			[LanguageCode.En] = "Target URL, e.g. https://github.com",
			[LanguageCode.Ja] = "対象URL（例: https://github.com）"
		};
		dictionary["BrowserChoiceDefault"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🌐 系统默认",
			[LanguageCode.ZhTw] = "🌐 系統預設",
			[LanguageCode.En] = "🌐 System Default",
			[LanguageCode.Ja] = "🌐 システム既定"
		};
		dictionary["BrowserChoiceCustom"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "自定义浏览器...",
			[LanguageCode.ZhTw] = "自訂瀏覽器...",
			[LanguageCode.En] = "Custom Browser...",
			[LanguageCode.Ja] = "カスタムブラウザ..."
		};
		dictionary["FocusCustomBrowserPathToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "自定义浏览器可执行文件路径",
			[LanguageCode.ZhTw] = "自訂瀏覽器可執行檔案路徑",
			[LanguageCode.En] = "Custom browser executable path",
			[LanguageCode.Ja] = "カスタムブラウザ実行パス"
		};
		dictionary["FocusCustomBrowserBrowseBtnText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "📂 选择...",
			[LanguageCode.ZhTw] = "📂 選擇...",
			[LanguageCode.En] = "📂 Select...",
			[LanguageCode.Ja] = "📂 選択..."
		};
		dictionary["FocusWebPresetsLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "常用网址:",
			[LanguageCode.ZhTw] = "常用網址:",
			[LanguageCode.En] = "Favorite Sites:",
			[LanguageCode.Ja] = "定番サイト:"
		};
		dictionary["FocusWebPresetBingText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "Bing 搜索",
			[LanguageCode.ZhTw] = "Bing 搜尋",
			[LanguageCode.En] = "Bing Search",
			[LanguageCode.Ja] = "Bing検索"
		};
		dictionary["FocusFolderPathToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "本地文件夹绝对路径",
			[LanguageCode.ZhTw] = "本機資料夾絕對路徑",
			[LanguageCode.En] = "Absolute local folder path",
			[LanguageCode.Ja] = "ローカルフォルダの絶対パス"
		};
		dictionary["FocusFolderBrowseBtnText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "📂 浏览...",
			[LanguageCode.ZhTw] = "📂 瀏覽...",
			[LanguageCode.En] = "📂 Browse...",
			[LanguageCode.Ja] = "📂 参照..."
		};
		dictionary["FocusFolderPresetsLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "常用目录:",
			[LanguageCode.ZhTw] = "常用目錄:",
			[LanguageCode.En] = "Common Folders:",
			[LanguageCode.Ja] = "定番フォルダ:"
		};
		dictionary["FocusFolderPresetThisPcText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "💻 此电脑",
			[LanguageCode.ZhTw] = "💻 本機",
			[LanguageCode.En] = "💻 This PC",
			[LanguageCode.Ja] = "💻 PC"
		};
		dictionary["FocusFolderPresetThisPcToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "直接打开系统「此电脑」命名空间",
			[LanguageCode.ZhTw] = "直接開啟系統「本機」命名空間",
			[LanguageCode.En] = "Open This PC namespace",
			[LanguageCode.Ja] = "「PC」を開く"
		};
		dictionary["FocusFolderPresetRecycleBinText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🗑️ 回收站",
			[LanguageCode.ZhTw] = "🗑️ 資源回收筒",
			[LanguageCode.En] = "🗑️ Recycle Bin",
			[LanguageCode.Ja] = "🗑️ ごみ箱"
		};
		dictionary["FocusFolderPresetRecycleBinToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "直接打开系统「回收站」命名空间",
			[LanguageCode.ZhTw] = "直接開啟系統「資源回收筒」命名空間",
			[LanguageCode.En] = "Open Recycle Bin namespace",
			[LanguageCode.Ja] = "「ごみ箱」を開く"
		};
		dictionary["FocusFolderPresetDesktopText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🖥️ 桌面",
			[LanguageCode.ZhTw] = "🖥️ 桌面",
			[LanguageCode.En] = "🖥️ Desktop",
			[LanguageCode.Ja] = "🖥️ デスクトップ"
		};
		dictionary["FocusFolderPresetDownloadsText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "📥 下载",
			[LanguageCode.ZhTw] = "📥 下載",
			[LanguageCode.En] = "📥 Downloads",
			[LanguageCode.Ja] = "📥 ダウンロード"
		};
		dictionary["FocusFolderPresetDocumentsText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "📄 文档",
			[LanguageCode.ZhTw] = "📄 文件",
			[LanguageCode.En] = "📄 Documents",
			[LanguageCode.Ja] = "📄 ドキュメント"
		};
		dictionary["FocusCommandToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "要执行的命令行语句，如 ping -t 127.0.0.1",
			[LanguageCode.ZhTw] = "要執行的命令列語句，如 ping -t 127.0.0.1",
			[LanguageCode.En] = "Command line to execute, e.g. ping -t 127.0.0.1",
			[LanguageCode.Ja] = "実行するコマンドライン（例: ping -t 127.0.0.1）"
		};
		dictionary["FocusWindowSubModeLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "控制模式:",
			[LanguageCode.ZhTw] = "控制模式:",
			[LanguageCode.En] = "Control Mode:",
			[LanguageCode.Ja] = "制御モード:"
		};
		dictionary["WindowModeTile"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔲 平铺窗口排布",
			[LanguageCode.ZhTw] = "🔲 平鋪視窗排布",
			[LanguageCode.En] = "🔲 Tile Windows",
			[LanguageCode.Ja] = "🔲 ウィンドウ整列"
		};
		dictionary["WindowModeCycle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔄 循环切换平铺",
			[LanguageCode.ZhTw] = "🔄 循環切換平鋪",
			[LanguageCode.En] = "🔄 Cycle Tile Layouts",
			[LanguageCode.Ja] = "🔄 レイアウト巡回"
		};
		dictionary["WindowModeCycleReverse"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⬅️ 反向循环平铺",
			[LanguageCode.ZhTw] = "⬅️ 反向循環平鋪",
			[LanguageCode.En] = "⬅️ Cycle Tile Reverse",
			[LanguageCode.Ja] = "⬅️ 逆順レイアウト巡回"
		};
		dictionary["WindowModeRestore"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⏪ 还原平铺快照",
			[LanguageCode.ZhTw] = "⏪ 還原平鋪快照",
			[LanguageCode.En] = "⏪ Restore Tile Snapshot",
			[LanguageCode.Ja] = "⏪ 整列スナップショット復元"
		};
		dictionary["WindowModeTopmost"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "📌 窗口置顶 / 取消置顶",
			[LanguageCode.ZhTw] = "📌 視窗最上層顯示 / 取消最上層",
			[LanguageCode.En] = "📌 Toggle Window Always On Top",
			[LanguageCode.Ja] = "📌 最前面表示 / 解除"
		};
		dictionary["WindowModeMoveMonitor"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🖥️ 移到下一显示器",
			[LanguageCode.ZhTw] = "🖥️ 移至下一台螢幕",
			[LanguageCode.En] = "🖥️ Move to Next Monitor",
			[LanguageCode.Ja] = "🖥️ 次のディスプレイへ移動"
		};
		dictionary["WindowModeOpacity"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "👁️ 窗口透明度调节",
			[LanguageCode.ZhTw] = "👁️ 視窗透明度調節",
			[LanguageCode.En] = "👁️ Adjust Window Opacity",
			[LanguageCode.Ja] = "👁️ ウィンドウ不透明度調整"
		};
		dictionary["WindowModeSwitch"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🗂️ 任务栏切换 (Win+N)",
			[LanguageCode.ZhTw] = "🗂️ 工作列切換 (Win+N)",
			[LanguageCode.En] = "🗂️ Switch Taskbar App (Win+N)",
			[LanguageCode.Ja] = "🗂️ タスクバー切替 (Win+N)"
		};
		dictionary["FocusPopulateTileSubActionsBtnText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "✨ 预设 8 布局二级轮盘",
			[LanguageCode.ZhTw] = "✨ 預設 8 版面二級輪盤",
			[LanguageCode.En] = "✨ Preset 8 Layouts Sub-Wheel",
			[LanguageCode.Ja] = "✨ 8分割レイアウトをプリセット"
		};
		dictionary["FocusPopulateTileSubActionsBtnToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "自动在二级级联菜单中填充 8 种常用平铺布局",
			[LanguageCode.ZhTw] = "自動在二級級聯選單中填入 8 種常用平鋪版面",
			[LanguageCode.En] = "Automatically populate 8 common tiling layouts into the tier-2 sub-wheel",
			[LanguageCode.Ja] = "第2階層サブホイールに8種類の定番ウィンドウ整列レイアウトを自動設定"
		};
		dictionary["FocusTileCommonLayoutsLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "常用布局:",
			[LanguageCode.ZhTw] = "常用版面:",
			[LanguageCode.En] = "Common Layouts:",
			[LanguageCode.Ja] = "定番レイアウト:"
		};
		dictionary["FocusTilePreset2LText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "左右对半 (2L)",
			[LanguageCode.ZhTw] = "左右對半 (2L)",
			[LanguageCode.En] = "Split Left-Right (2L)",
			[LanguageCode.Ja] = "左右2分割 (2L)"
		};
		dictionary["FocusTilePreset2TText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "上下对半 (2T)",
			[LanguageCode.ZhTw] = "上下對半 (2T)",
			[LanguageCode.En] = "Split Top-Bottom (2T)",
			[LanguageCode.Ja] = "上下2分割 (2T)"
		};
		dictionary["FocusTilePreset3L12Text"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "左大列 (3L12)",
			[LanguageCode.ZhTw] = "左大欄 (3L12)",
			[LanguageCode.En] = "Left Large Column (3L12)",
			[LanguageCode.Ja] = "左主列 (3L12)"
		};
		dictionary["FocusTilePreset4GText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "四宫格 (4G)",
			[LanguageCode.ZhTw] = "四宮格 (4G)",
			[LanguageCode.En] = "2x2 Grid (4G)",
			[LanguageCode.Ja] = "4分割グリッド (4G)"
		};
		dictionary["FocusTilePreset3RText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "三等分 (3R)",
			[LanguageCode.ZhTw] = "三等分 (3R)",
			[LanguageCode.En] = "Three Columns (3R)",
			[LanguageCode.Ja] = "3等分 (3R)"
		};
		dictionary["FocusTileCycleHint"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "💡 触发手势时，自动在下方「平铺窗口设置」中勾选的排布列表中循环轮换下一个布局。",
			[LanguageCode.ZhTw] = "💡 觸發手勢時，自動在下方「平鋪視窗設定」中勾選的版面清單中循環輪換下一個版面。",
			[LanguageCode.En] = "💡 When triggered, automatically cycles to the next layout checked in 'Tiling Window Settings' below.",
			[LanguageCode.Ja] = "💡 ジェスチャー実行時、下の「ウィンドウ整列設定」でチェックされたレイアウトを順次切り替えます。"
		};
		dictionary["FocusTileRestoreHint"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⏪ 还原所有窗口到平铺前的初始大小与屏幕坐标位置。",
			[LanguageCode.ZhTw] = "⏪ 還原所有視窗至平鋪前的初始大小與螢幕座標位置。",
			[LanguageCode.En] = "⏪ Restores all windows to their size and screen positions before tiling.",
			[LanguageCode.Ja] = "⏪ すべてのウィンドウを整列前の元のサイズと位置に復元します。"
		};
		dictionary["FocusTileTopmostHint"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "📌 将当前鼠标所在窗口或前台活动窗口固定置顶于最前（再次触发即可恢复）。",
			[LanguageCode.ZhTw] = "📌 將目前滑鼠所在視窗或前景使用中視窗固定置頂於最前（再次觸發即可恢復）。",
			[LanguageCode.En] = "📌 Pin window under cursor or active window always on top (trigger again to unpin).",
			[LanguageCode.Ja] = "📌 カーソル位置またはアクティブなウィンドウを最前面に固定（再実行で解除）。"
		};
		dictionary["FocusTileMoveMonitorHint"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🖥️ 将当前活动窗口移动至下一个物理显示器对应的工作区位置。",
			[LanguageCode.ZhTw] = "🖥️ 將目前使用中視窗移動至下一台實體螢幕對應的工作區位置。",
			[LanguageCode.En] = "🖥️ Move active window to corresponding workspace on the next physical monitor.",
			[LanguageCode.Ja] = "🖥️ 現在のアクティブウィンドウを次のディスプレイの対応エリアに移動します。"
		};
		dictionary["FocusTileOpacityLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "不透明度:",
			[LanguageCode.ZhTw] = "不透明度:",
			[LanguageCode.En] = "Opacity:",
			[LanguageCode.Ja] = "不透明度:"
		};
		dictionary["FocusTileOpacityPresetsLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "快捷预设:",
			[LanguageCode.ZhTw] = "捷徑預設:",
			[LanguageCode.En] = "Quick Presets:",
			[LanguageCode.Ja] = "クイックプリセット:"
		};
		dictionary["FocusOpacity70Text"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "70% 极淡",
			[LanguageCode.ZhTw] = "70% 極淡",
			[LanguageCode.En] = "70% Faint",
			[LanguageCode.Ja] = "70% 薄い"
		};
		dictionary["FocusOpacity80Text"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "80% 查阅",
			[LanguageCode.ZhTw] = "80% 查閱",
			[LanguageCode.En] = "80% Glance",
			[LanguageCode.Ja] = "80% 参照"
		};
		dictionary["FocusOpacity90Text"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "90% 透视",
			[LanguageCode.ZhTw] = "90% 透視",
			[LanguageCode.En] = "90% Translucent",
			[LanguageCode.Ja] = "90% 半透明"
		};
		dictionary["FocusOpacity100Text"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "100% 不透明",
			[LanguageCode.ZhTw] = "100% 不透明",
			[LanguageCode.En] = "100% Opaque",
			[LanguageCode.Ja] = "100% 不透明"
		};
		dictionary["FocusSwitchWindowIndexLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "任务栏序号 (1~20):",
			[LanguageCode.ZhTw] = "工作列編號 (1~20):",
			[LanguageCode.En] = "Taskbar Slot Index (1~20):",
			[LanguageCode.Ja] = "タスクバー位置番号 (1〜20):"
		};
		dictionary["FocusSwitchWindowIndexHint"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "等同快捷键 Win + 序号",
			[LanguageCode.ZhTw] = "等同快速鍵 Win + 編號",
			[LanguageCode.En] = "Equivalent to Win + Number shortcut",
			[LanguageCode.Ja] = "ショートカット Win + 数字 に相当"
		};
		dictionary["FocusSwitchWindowQuickSelectLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "快速选择:",
			[LanguageCode.ZhTw] = "快速選擇:",
			[LanguageCode.En] = "Quick Select:",
			[LanguageCode.Ja] = "クイック選択:"
		};
		dictionary["FocusSwitchSlot1Text"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "#1 槽位",
			[LanguageCode.ZhTw] = "#1 位置",
			[LanguageCode.En] = "#1 Slot",
			[LanguageCode.Ja] = "#1 スロット"
		};
		dictionary["FocusSwitchSlot2Text"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "#2 槽位",
			[LanguageCode.ZhTw] = "#2 位置",
			[LanguageCode.En] = "#2 Slot",
			[LanguageCode.Ja] = "#2 スロット"
		};
		dictionary["FocusSwitchSlot3Text"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "#3 槽位",
			[LanguageCode.ZhTw] = "#3 位置",
			[LanguageCode.En] = "#3 Slot",
			[LanguageCode.Ja] = "#3 スロット"
		};
		dictionary["FocusSwitchSlot4Text"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "#4 槽位",
			[LanguageCode.ZhTw] = "#4 位置",
			[LanguageCode.En] = "#4 Slot",
			[LanguageCode.Ja] = "#4 スロット"
		};
		dictionary["FocusOcrDefaultStatus"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "默认调用 Windows 本地原生 OCR 离线引擎 (0延迟 · 隐私安全)",
			[LanguageCode.ZhTw] = "預設呼叫 Windows 本機原生 OCR 離線引擎 (0延遲 · 隱私安全)",
			[LanguageCode.En] = "Uses Windows Native offline OCR engine by default (Zero latency · Privacy safe)",
			[LanguageCode.Ja] = "Windowsローカル標準OCRオフラインエンジンを既定で使用（低遅延・高セキュリティ）"
		};
		dictionary["FocusOcrTestScreenshotBtnText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "✂️ 立即测试截屏",
			[LanguageCode.ZhTw] = "✂️ 立即測試截圖",
			[LanguageCode.En] = "✂️ Test Snipping Now",
			[LanguageCode.Ja] = "✂️ 今すぐキャプチャテスト"
		};
		dictionary["FocusOcrTestScreenshotBtnToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "立即启动全屏框选测试 OCR 识别效果",
			[LanguageCode.ZhTw] = "立即啟動全螢幕框選測試 OCR 辨識效果",
			[LanguageCode.En] = "Launch fullscreen region selection immediately to test OCR",
			[LanguageCode.Ja] = "全画面範囲選択を起動してOCR認識効果をテスト"
		};
		dictionary["FocusOcrConfigBtnText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚙️ 接口配置",
			[LanguageCode.ZhTw] = "⚙️ 介面設定",
			[LanguageCode.En] = "⚙️ OCR Settings",
			[LanguageCode.Ja] = "⚙️ OCRエンジン設定"
		};
		dictionary["FocusOcrConfigBtnToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "配置 OCR 引擎（本地引擎 / AI 视觉大模型 / 自定义 HTTP）",
			[LanguageCode.ZhTw] = "設定 OCR 引擎（本機引擎 / AI 視覺大模型 / 自訂 HTTP）",
			[LanguageCode.En] = "Configure OCR engine (Windows Native / Vision AI / Custom HTTP)",
			[LanguageCode.Ja] = "OCRエンジンの設定（ローカルエンジン / AI Vision / カスタムHTTP）"
		};
		dictionary["FocusShellToolDefaultTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "未挑选功能 (点击右侧挑选)",
			[LanguageCode.ZhTw] = "未挑選功能 (點擊右側挑選)",
			[LanguageCode.En] = "No Tool Selected (Click right to choose)",
			[LanguageCode.Ja] = "機能未選択（右側をクリックして選択）"
		};
		dictionary["FocusShellToolDefaultDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "从系统原生增强与右键扩展中选择常用高频功能",
			[LanguageCode.ZhTw] = "從系統原生增強與右鍵擴充中選擇常用高頻功能",
			[LanguageCode.En] = "Choose common utilities from native Windows enhancements and context menu extensions",
			[LanguageCode.Ja] = "Windows標準拡張機能や右クリックメニューから定番機能を選択"
		};
		dictionary["FocusPickShellToolBtnText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚡ 挑选功能...",
			[LanguageCode.ZhTw] = "⚡ 挑選功能...",
			[LanguageCode.En] = "⚡ Pick Tool...",
			[LanguageCode.Ja] = "⚡ 機能を選択..."
		};
		dictionary["FocusPickShellToolBtnToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "打开系统与右键工具库，支持搜索与分类",
			[LanguageCode.ZhTw] = "開啟系統與右鍵工具庫，支援搜尋與分類",
			[LanguageCode.En] = "Open system and context menu tools catalog with search and filters",
			[LanguageCode.Ja] = "システムとコンテキストメニューのツール一覧を開く（検索・分類対応）"
		};
		dictionary["FocusInheritIconLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🏷️ 关联外部程序图标:",
			[LanguageCode.ZhTw] = "🏷️ 關聯外部程式圖示:",
			[LanguageCode.En] = "🏷️ Linked App Icon:",
			[LanguageCode.Ja] = "🏷️ 外部アプリアイコン連携:"
		};
		dictionary["FocusInheritIconUnlinked"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "未关联 (显示默认动作图标)",
			[LanguageCode.ZhTw] = "未關聯 (顯示預設動作圖示)",
			[LanguageCode.En] = "Unlinked (shows default action icon)",
			[LanguageCode.Ja] = "未連携（標準アクションアイコン表示）"
		};
		dictionary["FocusInheritIconLinkedFormat"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "已关联: {0}",
			[LanguageCode.ZhTw] = "已關聯: {0}",
			[LanguageCode.En] = "Linked: {0}",
			[LanguageCode.Ja] = "連携中: {0}"
		};
		dictionary["FocusClearInheritedIconBtnText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "✕ 清除关联",
			[LanguageCode.ZhTw] = "✕ 清除關聯",
			[LanguageCode.En] = "✕ Unlink",
			[LanguageCode.Ja] = "✕ 連携解除"
		};
		dictionary["FocusClearInheritedIconBtnToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "清除关联的外部程序图标，恢复默认矢量图标",
			[LanguageCode.ZhTw] = "清除關聯的外部程式圖示，恢復預設向量圖示",
			[LanguageCode.En] = "Clear linked program icon and restore default vector icon",
			[LanguageCode.Ja] = "関連付けられた外部アイコンを解除し、標準ベクターアイコンに戻す"
		};
		dictionary["FocusInheritIconPathToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "关联提取图标的外部程序或快捷方式路径",
			[LanguageCode.ZhTw] = "關聯擷取圖示的外部程式或捷徑路徑",
			[LanguageCode.En] = "Path of external application or shortcut to extract icon from",
			[LanguageCode.Ja] = "アイコンを抽出する外部アプリまたはショートカットのパス"
		};
		dictionary["FocusInheritIconPickProgramBtnText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "📦 软件库...",
			[LanguageCode.ZhTw] = "📦 軟體庫...",
			[LanguageCode.En] = "📦 App Library...",
			[LanguageCode.Ja] = "📦 アプリ一覧..."
		};
		dictionary["FocusInheritIconPickProgramBtnToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "从已安装软件与微软商店应用中选取官方高清图标",
			[LanguageCode.ZhTw] = "從已安裝軟體與微軟商店應用中選取官方高畫質圖示",
			[LanguageCode.En] = "Select official high-res icon from installed software or Microsoft Store",
			[LanguageCode.Ja] = "インストール済みアプリやMSストアから公式高解像度アイコンを選択"
		};
		dictionary["FocusInheritIconCaptureWindowBtnText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🎯 捕捉窗口...",
			[LanguageCode.ZhTw] = "🎯 捕捉視窗...",
			[LanguageCode.En] = "🎯 Capture Window...",
			[LanguageCode.Ja] = "🎯 ウィンドウ捕捉..."
		};
		dictionary["FocusInheritIconCaptureWindowBtnToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "直接捕捉桌面上运行中的软件并继承其图标",
			[LanguageCode.ZhTw] = "直接捕捉桌面上執行中的軟體並繼承其圖示",
			[LanguageCode.En] = "Capture running window from desktop to inherit its icon directly",
			[LanguageCode.Ja] = "デスクトップで実行中のアプリをキャプチャしてアイコンを継承"
		};
		dictionary["FocusInheritIconBrowseBtnText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "📂 浏览...",
			[LanguageCode.ZhTw] = "📂 瀏覽...",
			[LanguageCode.En] = "📂 Browse...",
			[LanguageCode.Ja] = "📂 参照..."
		};
		dictionary["FocusInheritIconBrowseBtnToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "手动浏览提取 .exe / .ico / .lnk 图标",
			[LanguageCode.ZhTw] = "手動瀏覽擷取 .exe / .ico / .lnk 圖示",
			[LanguageCode.En] = "Browse file system to extract icon from .exe / .ico / .lnk",
			[LanguageCode.Ja] = ".exe / .ico / .lnk ファイルを手動参照してアイコンを抽出"
		};
		dictionary["FocusSubActionsSectionLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "二级级联子动作:",
			[LanguageCode.ZhTw] = "二級級聯子動作:",
			[LanguageCode.En] = "Tier-2 Sub-Actions:",
			[LanguageCode.Ja] = "第2階層サブアクション:"
		};
		dictionary["FocusSubActionsCountFormat"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "({0} 项)",
			[LanguageCode.ZhTw] = "({0} 項)",
			[LanguageCode.En] = "({0} items)",
			[LanguageCode.Ja] = "（{0}件）"
		};
		dictionary["FocusAddSubActionBtnText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "➕ 添加二级动作",
			[LanguageCode.ZhTw] = "➕ 新增二級動作",
			[LanguageCode.En] = "➕ Add Sub-Action",
			[LanguageCode.Ja] = "➕ サブアクション追加"
		};
		dictionary["FocusClearSubActionsBtnText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🗑️ 清空",
			[LanguageCode.ZhTw] = "🗑️ 清空",
			[LanguageCode.En] = "🗑️ Clear",
			[LanguageCode.Ja] = "🗑️ クリア"
		};
		dictionary["FocusUndoSubActionsBtnText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "↩️ 撤销",
			[LanguageCode.ZhTw] = "↩️ 復原",
			[LanguageCode.En] = "↩️ Undo",
			[LanguageCode.Ja] = "↩️ 元に戻す"
		};
		dictionary["FocusUndoSubActionsBtnToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "撤销上一次的修改或清空，恢复二级动作列表",
			[LanguageCode.ZhTw] = "復原上一次的修改或清空，恢復二級動作清單",
			[LanguageCode.En] = "Undo the last modification or clear, restoring sub-action list",
			[LanguageCode.Ja] = "直前の変更またはクリアを元に戻し、サブアクションリストを復元"
		};
		dictionary["FocusBatchBadgeText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "多选",
			[LanguageCode.ZhTw] = "多選",
			[LanguageCode.En] = "Multi",
			[LanguageCode.Ja] = "複数選択"
		};
		dictionary["FocusBatchTitleText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "批量修改模式",
			[LanguageCode.ZhTw] = "批次修改模式",
			[LanguageCode.En] = "Batch Edit Mode",
			[LanguageCode.Ja] = "一括編集モード"
		};
		dictionary["FocusBatchTagFormat"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "已多选 {0} 个扇区",
			[LanguageCode.ZhTw] = "已多選 {0} 個扇區",
			[LanguageCode.En] = "{0} sectors selected",
			[LanguageCode.Ja] = "{0}個のセクターを選択中"
		};
		dictionary["FocusBatchSubtitleText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "在右侧画布中按住 Ctrl 点击可增减多选扇区；在此统一调整排版属性",
			[LanguageCode.ZhTw] = "在右側畫布中按住 Ctrl 點擊可增減多選扇區；在此統一調整排版屬性",
			[LanguageCode.En] = "Ctrl+Click sectors on the canvas to multi-select; adjust layout properties together here",
			[LanguageCode.Ja] = "右側のキャンバスでCtrlキーを押しながらクリックして複数選択；レイアウトプロパティを一括調整"
		};
		dictionary["FocusBatchExitBtnText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "✕ 退出多选",
			[LanguageCode.ZhTw] = "✕ 結束多選",
			[LanguageCode.En] = "✕ Exit Multi-Select",
			[LanguageCode.Ja] = "✕ 複数選択を終了"
		};
		dictionary["BatchLayoutModeLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "批量切换排版模式:",
			[LanguageCode.ZhTw] = "批次切換排版模式:",
			[LanguageCode.En] = "Batch Layout Mode:",
			[LanguageCode.Ja] = "一括レイアウトモード:"
		};
		dictionary["BatchLayoutBothBtnText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🖼️+🔤 图文",
			[LanguageCode.ZhTw] = "🖼️+🔤 圖文",
			[LanguageCode.En] = "🖼️+🔤 Both",
			[LanguageCode.Ja] = "🖼️+🔤 画像＋文字"
		};
		dictionary["BatchLayoutBothBtnToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "将所有选中扇区批量设为图文并茂居中",
			[LanguageCode.ZhTw] = "將所有選取扇區批次設為圖文並茂置中",
			[LanguageCode.En] = "Set all selected sectors to show both icon and text centered",
			[LanguageCode.Ja] = "選択した全セクターをアイコンと文字の両方表示に設定"
		};
		dictionary["BatchLayoutIconOnlyBtnText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🖼️ 仅图标",
			[LanguageCode.ZhTw] = "🖼️ 僅圖示",
			[LanguageCode.En] = "🖼️ Icon Only",
			[LanguageCode.Ja] = "🖼️ アイコンのみ"
		};
		dictionary["BatchLayoutIconOnlyBtnToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "将所有选中扇区批量设为仅显示图标",
			[LanguageCode.ZhTw] = "將所有選取扇區批次設為僅顯示圖示",
			[LanguageCode.En] = "Set all selected sectors to show icon only",
			[LanguageCode.Ja] = "選択した全セクターをアイコンのみ表示に設定"
		};
		dictionary["BatchLayoutTextOnlyBtnText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔤 仅文字",
			[LanguageCode.ZhTw] = "🔤 僅文字",
			[LanguageCode.En] = "🔤 Text Only",
			[LanguageCode.Ja] = "🔤 文字のみ"
		};
		dictionary["BatchLayoutTextOnlyBtnToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "将所有选中扇区批量设为仅显示文字",
			[LanguageCode.ZhTw] = "將所有選取扇區批次設為僅顯示文字",
			[LanguageCode.En] = "Set all selected sectors to show text only",
			[LanguageCode.Ja] = "選択した全セクターを文字のみ表示に設定"
		};
		dictionary["BatchLayoutInheritBtnText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🌐 继承全局",
			[LanguageCode.ZhTw] = "🌐 繼承全域",
			[LanguageCode.En] = "🌐 Inherit Global",
			[LanguageCode.Ja] = "🌐 グローバル継承"
		};
		dictionary["BatchLayoutInheritBtnToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "将所有选中扇区排版模式批量恢复继承全局",
			[LanguageCode.ZhTw] = "將所有選取扇區排版模式批次恢復繼承全域",
			[LanguageCode.En] = "Reset layout mode of all selected sectors to inherit global",
			[LanguageCode.Ja] = "選択した全セクターのレイアウトをグローバル設定の継承にリセット"
		};
		dictionary["BatchFontSizeLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "文字字号大小:",
			[LanguageCode.ZhTw] = "文字字型大小:",
			[LanguageCode.En] = "Font Size:",
			[LanguageCode.Ja] = "フォントサイズ:"
		};
		dictionary["BatchIconSizeLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "图标尺寸大小:",
			[LanguageCode.ZhTw] = "圖示尺寸大小:",
			[LanguageCode.En] = "Icon Size:",
			[LanguageCode.Ja] = "アイコンサイズ:"
		};
		dictionary["BatchTextColorLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "批量文字颜色:",
			[LanguageCode.ZhTw] = "批次文字顏色:",
			[LanguageCode.En] = "Text Color:",
			[LanguageCode.Ja] = "テキストカラー:"
		};
		dictionary["BatchTextColorPaletteToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "打开调色板选取颜色",
			[LanguageCode.ZhTw] = "開啟調色盤選取顏色",
			[LanguageCode.En] = "Open color palette",
			[LanguageCode.Ja] = "カラーパレットを開く"
		};
		dictionary["BatchTextColorEyedropperToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "从屏幕任意位置吸取颜色",
			[LanguageCode.ZhTw] = "從螢幕任意位置吸取顏色",
			[LanguageCode.En] = "Pick color from anywhere on screen",
			[LanguageCode.Ja] = "画面上の任意の位置から色を抽出"
		};
		dictionary["BatchOffsetXLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "水平 X 偏移:",
			[LanguageCode.ZhTw] = "水平 X 偏移:",
			[LanguageCode.En] = "Horizontal X Offset:",
			[LanguageCode.Ja] = "水平Xオフセット:"
		};
		dictionary["BatchOffsetYLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "垂直 Y 偏移:",
			[LanguageCode.ZhTw] = "垂直 Y 偏移:",
			[LanguageCode.En] = "Vertical Y Offset:",
			[LanguageCode.Ja] = "垂直Yオフセット:"
		};
		dictionary["BatchResetCustomHint"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "💡 清除所有选中槽位的独立定制，恢复跟随全局统一外观",
			[LanguageCode.ZhTw] = "💡 清除所有選取位置的獨立自訂，恢復跟隨全域統一外觀",
			[LanguageCode.En] = "💡 Clear custom styling for all selected slots and restore uniform global appearance",
			[LanguageCode.Ja] = "💡 選択したすべてのスロットの個別カスタマイズを解除し、グローバルの統一デザインに戻します"
		};
		dictionary["BatchResetCustomBtnText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔄 清除自定义，恢复跟随全局统一",
			[LanguageCode.ZhTw] = "🔄 清除自訂，恢復跟隨全域統一",
			[LanguageCode.En] = "🔄 Reset Custom Styling, Follow Global",
			[LanguageCode.Ja] = "🔄 個別設定を解除しグローバルに統一"
		};
		dictionary["Tab2GridSplitterToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "拖拽调整画布与配置区比例，双击恢复默认比例",
			[LanguageCode.ZhTw] = "拖曳調整畫布與設定區比例，按兩下恢復預設比例",
			[LanguageCode.En] = "Drag to adjust canvas and settings ratio, double-click to reset",
			[LanguageCode.Ja] = "ドラッグでキャンバスと設定エリアの比率を調整、ダブルクリックでリセット"
		};
		dictionary["LiveCanvasHeaderTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "实时交互画布",
			[LanguageCode.ZhTw] = "即時互動畫布",
			[LanguageCode.En] = "Interactive Wheel Canvas",
			[LanguageCode.Ja] = "インタラクティブキャンバス"
		};
		dictionary["MappingsLinkSubActionsToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "开启时：拖拽一级扇区将连同绑定的二级子轮盘一起对调换位\n关闭时：仅对调一级扇区主动作，保留各方位现存的二级子菜单",
			[LanguageCode.ZhTw] = "開啟時：拖曳一級扇區將連同綁定的二級子輪盤一起對調換位\n關閉時：僅對調一級扇區主動作，保留各方位現存的二級子選單",
			[LanguageCode.En] = "When enabled: dragging a primary sector will swap its bound tier-2 sub-wheel together\nWhen disabled: only swaps the primary sector action, keeping existing sub-menus in place",
			[LanguageCode.Ja] = "有効時：第1階層セクターをドラッグすると紐づく第2階層サブホイールも一緒に位置交換\n無効時：第1階層のアクションのみを入れ替え、各方向のサブメニューは保持"
		};
		dictionary["MappingsLinkSubActionsOn"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "一二级链接: 开启",
			[LanguageCode.ZhTw] = "一二級連結: 開啟",
			[LanguageCode.En] = "Link Sub-Wheels: ON",
			[LanguageCode.Ja] = "サブホイール連動: 有効"
		};
		dictionary["MappingsLinkSubActionsOff"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "一二级链接: 关闭",
			[LanguageCode.ZhTw] = "一二級連結: 關閉",
			[LanguageCode.En] = "Link Sub-Wheels: OFF",
			[LanguageCode.Ja] = "サブホイール連動: 無効"
		};
		dictionary["MappingsFpsBadgeText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "60FPS 同步",
			[LanguageCode.ZhTw] = "60FPS 同步",
			[LanguageCode.En] = "60FPS Sync",
			[LanguageCode.Ja] = "60FPS 同期"
		};
		dictionary["MappingsCanvasInstructions"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "💡 点击内圈选一级扇区，点击外环选二级动作，点击中心选核圆；按住拖动可对调功能位置！",
			[LanguageCode.ZhTw] = "💡 點擊內圈選一級扇區，點擊外環選二級動作，點擊中心選核圓；按住拖曳可對調功能位置！",
			[LanguageCode.En] = "💡 Click inner ring for sector, outer ring for sub-action, center for core; drag to swap positions!",
			[LanguageCode.Ja] = "💡 内側クリックで主セクター、外側でサブアクション、中央でコアを選択；ドラッグで位置を入れ替え！"
		};
		dictionary["MappingsTier1SegmentText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔘 一级主轮盘",
			[LanguageCode.ZhTw] = "🔘 一級主輪盤",
			[LanguageCode.En] = "🔘 Tier-1 Primary Wheel",
			[LanguageCode.Ja] = "🔘 第1階層メインホイール"
		};
		dictionary["MappingsTier2SegmentText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🌟 二级级联",
			[LanguageCode.ZhTw] = "🌟 二級級聯",
			[LanguageCode.En] = "🌟 Tier-2 Cascaded",
			[LanguageCode.Ja] = "🌟 第2階層カスケード"
		};
		dictionary["MappingsShowTextToggleBtnText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔤 图文",
			[LanguageCode.ZhTw] = "🔤 圖文",
			[LanguageCode.En] = "🔤 Text",
			[LanguageCode.Ja] = "🔤 文字"
		};
		dictionary["MappingsShowTextToggleBtnToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "开启/关闭动作名称复合展示（开启后在扇区中直观渲染动作名称与图标，让拖拽对调一目了然）",
			[LanguageCode.ZhTw] = "開啟/關閉動作名稱複合展示（開啟後在扇區中直觀轉譯動作名稱與圖示，讓拖曳對調一目了然）",
			[LanguageCode.En] = "Toggle compound display of action names (renders text and icons inside sectors for intuitive dragging)",
			[LanguageCode.Ja] = "アクション名の複合表示のオン/オフ（セクター内に名前とアイコンを表示し、ドラッグ交換を直感的に）"
		};
		dictionary["MappingsZoomOutBtnToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "缩小视图",
			[LanguageCode.ZhTw] = "縮小檢視",
			[LanguageCode.En] = "Zoom Out",
			[LanguageCode.Ja] = "縮小"
		};
		dictionary["MappingsZoomLabelToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "点击复位为 100%",
			[LanguageCode.ZhTw] = "點擊重設為 100%",
			[LanguageCode.En] = "Click to reset zoom to 100%",
			[LanguageCode.Ja] = "クリックで100%にリセット"
		};
		dictionary["MappingsZoomInBtnToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "放大视图",
			[LanguageCode.ZhTw] = "放大檢視",
			[LanguageCode.En] = "Zoom In",
			[LanguageCode.Ja] = "拡大"
		};
		dictionary["MappingsResetViewBtnToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "重置视图",
			[LanguageCode.ZhTw] = "重設檢視",
			[LanguageCode.En] = "Reset View",
			[LanguageCode.Ja] = "ビューをリセット"
		};
		dictionary["MappingsSaveNotice"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "💡 修改在内存中即时生效，点击主窗口右下角【保存并生效】持久化至硬盘。",
			[LanguageCode.ZhTw] = "💡 修改在記憶體中即時生效，點擊主視窗右下角【儲存並生效】持久化至硬碟。",
			[LanguageCode.En] = "💡 Changes take effect immediately in memory; click [Save & Apply] at the bottom-right to persist to disk.",
			[LanguageCode.Ja] = "💡 変更はメモリ上で即時反映されます。右下の【保存して適用】をクリックして永続化してください。"
		};
		dictionary["ListModeProfileHeaderTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "当前配置方案",
			[LanguageCode.ZhTw] = "目前設定方案",
			[LanguageCode.En] = "Current Profile",
			[LanguageCode.Ja] = "現在のプロファイル"
		};
		dictionary["ListModeProfileHeaderDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "选择或新建针对特定程序（如 Chrome、VS Code）或特定工作流的轮盘配置方案（支持双击重命名）。",
			[LanguageCode.ZhTw] = "選擇或新建針對特定程式（如 Chrome、VS Code）或特定工作流程的輪盤設定方案（支援按兩下重新命名）。",
			[LanguageCode.En] = "Select or create wheel profiles for specific programs (like Chrome, VS Code) or workflows (double-click to rename).",
			[LanguageCode.Ja] = "特定アプリ（Chrome、VS Codeなど）やワークフロー用のプロファイルを選択または作成（ダブルクリックで名前変更）。"
		};
		dictionary["ListModeSectorHeaderTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "扇区方位数量",
			[LanguageCode.ZhTw] = "扇區方位數量",
			[LanguageCode.En] = "Sector Count",
			[LanguageCode.Ja] = "セクター分割数"
		};
		dictionary["ListModeSectorHeaderDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "切换手势轮盘的切分数量。4 键最快最不易误触，8 键为标准全能方位，12 键适合功能密集场景。",
			[LanguageCode.ZhTw] = "切換手勢輪盤的劃分數量。4 鍵最快最不易誤觸，8 鍵為標準全能方位，12 鍵適合功能密集場景。",
			[LanguageCode.En] = "Change radial wheel sector divisions. 4 sectors is fastest and prevents misclicks; 8 is standard all-around; 12 is for dense workflows.",
			[LanguageCode.Ja] = "ホイールの分割数を変更します。4方向は最速で誤爆しにくく、8方向は標準的、12方向は高密度な操作に最適です。"
		};
		dictionary["ListModeActionListHeaderTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "扇区动作映射列表",
			[LanguageCode.ZhTw] = "扇區動作對應清單",
			[LanguageCode.En] = "Sector Action Mappings",
			[LanguageCode.Ja] = "セクターアクション割り当て一覧"
		};
		dictionary["ListModeActionListHeaderDesc1"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "为每个方位指定触发动作与图标。支持热键组合（如 Ctrl+C）、启动本地程序与系统级操作。",
			[LanguageCode.ZhTw] = "為每個方位指定觸發動作與圖示。支援快速鍵組合（如 Ctrl+C）、啟動本機程式與系統層級操作。",
			[LanguageCode.En] = "Assign actions and icons to each direction. Supports hotkeys (Ctrl+C), launching apps, and system actions.",
			[LanguageCode.Ja] = "各方向にトリガーアクションとアイコンを割り当てます。ショートカット、アプリ起動、システム操作に対応。"
		};
		dictionary["ListModeActionListHeaderDesc2"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "点击右侧功能卡的 ▲ / ▼ 箭头，将功能移动到相邻的轮盘位置槽。",
			[LanguageCode.ZhTw] = "點擊右側功能卡的 ▲ / ▼ 箭頭，將功能移動至相鄰的輪盤位置。",
			[LanguageCode.En] = "Click ▲ / ▼ arrows on slot cards to move functions to adjacent wheel positions.",
			[LanguageCode.Ja] = "各スロット右側の ▲ / ▼ 矢印をクリックして、アクションを隣接スロットに移動します。"
		};
		dictionary["PickIconToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "点击选取矢量图标",
			[LanguageCode.ZhTw] = "點擊選取向量圖示",
			[LanguageCode.En] = "Click to select vector icon",
			[LanguageCode.Ja] = "クリックしてベクターアイコンを選択"
		};
		dictionary["HotkeyBuilderButtonText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚙️ 拼装",
			[LanguageCode.ZhTw] = "⚙️ 拼裝",
			[LanguageCode.En] = "⚙️ Build",
			[LanguageCode.Ja] = "⚙️ ビルド"
		};
		dictionary["HotkeyBuilderToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "打开快捷热键拼装组合器（支持 Alt+Tab、Win+Tab、Shift+Alt、多位连续数值等）",
			[LanguageCode.ZhTw] = "開啟快速熱鍵拼裝組合器（支援 Alt+Tab、Win+Tab、Shift+Alt、多位連續數值等）",
			[LanguageCode.En] = "Open hotkey combo builder (supports Alt+Tab, Win+Tab, Shift+Alt, multi-digit sequence, etc.)",
			[LanguageCode.Ja] = "ホットキー作成ビルダーを開く（Alt+Tab、Win+Tab、Shift+Alt、複数桁キー列などに対応）"
		};
		dictionary["AppPathToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "选择的应用程序路径",
			[LanguageCode.ZhTw] = "選擇的應用程式路徑",
			[LanguageCode.En] = "Selected application executable path",
			[LanguageCode.Ja] = "選択したアプリの実行パス"
		};
		dictionary["BrowseAppToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "选择应用程序或快捷方式...",
			[LanguageCode.ZhTw] = "選擇應用程式或捷徑...",
			[LanguageCode.En] = "Browse for application or shortcut...",
			[LanguageCode.Ja] = "アプリまたはショートカットを選択..."
		};
		dictionary["WebUrlToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "目标网址，如 https://github.com",
			[LanguageCode.ZhTw] = "目標網址，如 https://github.com",
			[LanguageCode.En] = "Target URL, e.g. https://github.com",
			[LanguageCode.Ja] = "対象URL（例: https://github.com）"
		};
		dictionary["FolderPathToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "选择的本地文件夹路径",
			[LanguageCode.ZhTw] = "選擇的本機資料夾路徑",
			[LanguageCode.En] = "Selected local folder path",
			[LanguageCode.Ja] = "選択したフォルダパス"
		};
		dictionary["BrowseFolderToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "选择本地文件夹...",
			[LanguageCode.ZhTw] = "選擇本機資料夾...",
			[LanguageCode.En] = "Select local folder...",
			[LanguageCode.Ja] = "フォルダを選択..."
		};
		dictionary["CommandParamToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "要运行的命令，如 ping -n 3 127.0.0.1",
			[LanguageCode.ZhTw] = "要執行的命令，如 ping -n 3 127.0.0.1",
			[LanguageCode.En] = "Command to run, e.g. ping -n 3 127.0.0.1",
			[LanguageCode.Ja] = "実行コマンド（例: ping -n 3 127.0.0.1）"
		};
		dictionary["SwitchWindowIndexToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "任务栏第 N 个应用（顺序同任务栏/Win+N 槽位，稳定）；图标与切换目标一致；固定未运行的槽位无法启动，托盘驻留不计入",
			[LanguageCode.ZhTw] = "工作列第 N 個應用（順序同工作列/Win+N 位置，穩定）；圖示與切換目標一致；固定未執行的位置無法啟動，系統匣駐留不計入",
			[LanguageCode.En] = "Taskbar N-th app (matches Win+N position); icon matches target; pinned non-running apps cannot launch, tray apps excluded",
			[LanguageCode.Ja] = "タスクバーのN番目アプリ（Win+N相当）；対象アイコンを表示；未起動ピン留めアプリは起動不可"
		};
		dictionary["TileLayoutToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "平铺布局预设",
			[LanguageCode.ZhTw] = "平鋪版面預設",
			[LanguageCode.En] = "Tile layout preset",
			[LanguageCode.Ja] = "ウィンドウ整列プリセット"
		};
		dictionary["LaunchArgsToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "启动参数 (如命令行参数或URL)",
			[LanguageCode.ZhTw] = "啟動參數 (如命令列參數或URL)",
			[LanguageCode.En] = "Launch arguments (command line parameters or URL)",
			[LanguageCode.Ja] = "起動引数（コマンドライン引数またはURL）"
		};
		dictionary["ManageSubActionsToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "配置该扇区的二级级联子动作菜单",
			[LanguageCode.ZhTw] = "設定該扇區的二級級聯子動作選單",
			[LanguageCode.En] = "Configure tier-2 cascaded sub-action menu for this sector",
			[LanguageCode.Ja] = "このセクターの第2階層サブアクションメニューを設定"
		};
		dictionary["TileSettingsCollapsed"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "已收纳 (点击展开)",
			[LanguageCode.ZhTw] = "已收納 (點擊展開)",
			[LanguageCode.En] = "Collapsed (click to expand)",
			[LanguageCode.Ja] = "折りたたみ中（クリックで展開）"
		};
		dictionary["TileSettingsExpanded"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "已展开 (点击收起)",
			[LanguageCode.ZhTw] = "已展開 (點擊收起)",
			[LanguageCode.En] = "Expanded (click to collapse)",
			[LanguageCode.Ja] = "展開中（クリックで折りたたむ）"
		};
		dictionary["TileSettingsToggleExpand"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "展开配置",
			[LanguageCode.ZhTw] = "展開設定",
			[LanguageCode.En] = "Expand Settings",
			[LanguageCode.Ja] = "設定を展開"
		};
		dictionary["TileSettingsToggleCollapse"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "收起配置",
			[LanguageCode.ZhTw] = "收起設定",
			[LanguageCode.En] = "Collapse Settings",
			[LanguageCode.Ja] = "設定を閉じる"
		};
		dictionary["TileExcludeMinimizedHint"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "默认关闭：最小化窗口不参与，仅排布可见窗口。",
			[LanguageCode.ZhTw] = "預設關閉：最小化視窗不參與，僅排布可見視窗。",
			[LanguageCode.En] = "Default off: minimized windows are excluded, only tiling visible windows.",
			[LanguageCode.Ja] = "デフォルト無効：最小化されたウィンドウは除外され、表示中ウィンドウのみ整列します。"
		};
		dictionary["TileCaptureExcludeProcessBtnText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🎯 捕捉排除进程...",
			[LanguageCode.ZhTw] = "🎯 捕捉排除處理程序...",
			[LanguageCode.En] = "🎯 Capture Excluded Process...",
			[LanguageCode.Ja] = "🎯 除外プロセスを捕捉..."
		};
		dictionary["TileCaptureExcludeProcessBtnToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "打开智能窗口捕捉器，选取桌面运行中的程序加入平铺排除名单（自动安全排除 StarPie 自身）",
			[LanguageCode.ZhTw] = "開啟智慧視窗捕捉器，選取桌面執行中的程式加入平鋪排除清單（自動安全排除 StarPie 自身）",
			[LanguageCode.En] = "Open window capture tool to pick running apps to exclude from tiling (StarPie itself is always safely excluded)",
			[LanguageCode.Ja] = "ウィンドウキャプチャを開いて整列除外リストに追加（StarPie自身は自動で安全除外）"
		};
		dictionary["TileMarginTopToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "上边距（0~1000 物理像素）",
			[LanguageCode.ZhTw] = "上邊距（0~1000 實體像素）",
			[LanguageCode.En] = "Top margin (0~1000 physical px)",
			[LanguageCode.Ja] = "上マージン（0〜1000物理px）"
		};
		dictionary["TileMarginBottomToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "下边距（0~1000 物理像素）",
			[LanguageCode.ZhTw] = "下邊距（0~1000 實體像素）",
			[LanguageCode.En] = "Bottom margin (0~1000 physical px)",
			[LanguageCode.Ja] = "下マージン（0〜1000物理px）"
		};
		dictionary["TileMarginLeftToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "左边距（0~1000 物理像素）",
			[LanguageCode.ZhTw] = "左邊距（0~1000 實體像素）",
			[LanguageCode.En] = "Left margin (0~1000 physical px)",
			[LanguageCode.Ja] = "左マージン（0〜1000物理px）"
		};
		dictionary["TileMarginRightToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "右边距（0~1000 物理像素）",
			[LanguageCode.ZhTw] = "右邊距（0~1000 實體像素）",
			[LanguageCode.En] = "Right margin (0~1000 physical px)",
			[LanguageCode.Ja] = "右マージン（0〜1000物理px）"
		};
		dictionary["TileGapToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "相邻窗口之间的空隙（0~500 物理像素）",
			[LanguageCode.ZhTw] = "相鄰視窗之間的間隙（0~500 實體像素）",
			[LanguageCode.En] = "Gap between adjacent windows (0~500 physical px)",
			[LanguageCode.Ja] = "隣接ウィンドウ間の間隔（0〜500物理px）"
		};
		dictionary["TilePresetClassic4BtnText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "✨ 经典常用 (4项)",
			[LanguageCode.ZhTw] = "✨ 經典常用 (4項)",
			[LanguageCode.En] = "✨ Classic 4 Layouts",
			[LanguageCode.Ja] = "✨ 定番4種レイアウト"
		};
		dictionary["TilePresetClassic4BtnToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "一键勾选 2L、2T、3L12、4G 四种高频排布",
			[LanguageCode.ZhTw] = "一鍵勾選 2L、2T、3L12、4G 四種高頻版面",
			[LanguageCode.En] = "One-click check 4 common layouts: 2L, 2T, 3L12, 4G",
			[LanguageCode.Ja] = "定番の4種レイアウト（2L、2T、3L12、4G）を一括選択"
		};
		dictionary["TileMoveLayoutUpToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "上移",
			[LanguageCode.ZhTw] = "上移",
			[LanguageCode.En] = "Move Up",
			[LanguageCode.Ja] = "上へ移動"
		};
		dictionary["TileMoveLayoutDownToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "下移",
			[LanguageCode.ZhTw] = "下移",
			[LanguageCode.En] = "Move Down",
			[LanguageCode.Ja] = "下へ移動"
		};
		dictionary["TileSelectAllLayoutsBtnText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "全",
			[LanguageCode.ZhTw] = "全",
			[LanguageCode.En] = "All",
			[LanguageCode.Ja] = "全"
		};
		dictionary["TileSelectAllLayoutsBtnToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "全部参与循环",
			[LanguageCode.ZhTw] = "全部參與循環",
			[LanguageCode.En] = "All participate in cycle",
			[LanguageCode.Ja] = "すべて巡回対象にする"
		};
		dictionary["TileClearAllLayoutsBtnText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "空",
			[LanguageCode.ZhTw] = "空",
			[LanguageCode.En] = "None",
			[LanguageCode.Ja] = "空"
		};
		dictionary["TileClearAllLayoutsBtnToolTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "清空（等效全部参与）",
			[LanguageCode.ZhTw] = "清空（等效全部參與）",
			[LanguageCode.En] = "Clear all (equivalent to all participate)",
			[LanguageCode.Ja] = "すべてクリア（全参加と同等）"
		};
		dictionary["FocusSlotCenterCoreTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "中心核心圆动作 (Center Core)",
			[LanguageCode.ZhTw] = "中心核心圓動作 (Center Core)",
			[LanguageCode.En] = "Center Core Action",
			[LanguageCode.Ja] = "中心コアアクション"
		};
		dictionary["FocusSlotCenterCoreTag"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "核心圆",
			[LanguageCode.ZhTw] = "核心圓",
			[LanguageCode.En] = "Center Core",
			[LanguageCode.Ja] = "中心コア"
		};
		dictionary["FocusSlotCenterCoreSubtitleInherited"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "💡 专属方案未配置中心动作，已自动继承全局方案「{0}」",
			[LanguageCode.ZhTw] = "💡 專屬方案未設定中心動作，已自動繼承全域方案「{0}」",
			[LanguageCode.En] = "💡 Not configured in app profile; inherited from global profile \"{0}\"",
			[LanguageCode.Ja] = "💡 専用プロファイル未設定のため、グローバル「{0}」から自動継承"
		};
		dictionary["FocusSlotCenterCoreSubtitleDefault"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "在开启外甩脱离取消时，鼠标在中心内径死区内松开即可触发",
			[LanguageCode.ZhTw] = "在開啟外甩脫離取消時，滑鼠在中心內徑死區內放開即可觸發",
			[LanguageCode.En] = "When fling-out cancel is enabled, release cursor in center deadzone to trigger",
			[LanguageCode.Ja] = "外側フリックキャンセル有効時、中心デッドゾーン内でマウスを離すとトリガー"
		};
		dictionary["FocusSlotTier2EmptyTitleFormat"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "扇区 {0} [{1}] 级联子动作",
			[LanguageCode.ZhTw] = "扇區 {0} [{1}] 級聯子動作",
			[LanguageCode.En] = "Sector {0} [{1}] Cascaded Sub-Actions",
			[LanguageCode.Ja] = "セクター {0} [{1}] カスケードサブアクション"
		};
		dictionary["FocusSlotTier2EmptyTag"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "二级级联 (未添加)",
			[LanguageCode.ZhTw] = "二級級聯 (未新增)",
			[LanguageCode.En] = "Tier-2 Sub-Wheel (Empty)",
			[LanguageCode.Ja] = "第2階層（未追加）"
		};
		dictionary["FocusSlotTier2EmptySubtitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "当前扇区尚未配置二级级联子动作，点击【➕ 添加第 1 个二级子动作】以创建",
			[LanguageCode.ZhTw] = "目前扇區尚未設定二級級聯子動作，點擊【➕ 新增第 1 個二級子動作】以建立",
			[LanguageCode.En] = "No sub-actions configured yet; click [+ Add 1st Sub-Action] to create",
			[LanguageCode.Ja] = "第2階層サブアクションが未設定です。「➕ 最初のサブアクションを追加」をクリックして作成"
		};
		dictionary["FocusSlotTier2SubActionTitleFormat"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "二级动作 [{0}]",
			[LanguageCode.ZhTw] = "二級動作 [{0}]",
			[LanguageCode.En] = "Sub-Action [{0}]",
			[LanguageCode.Ja] = "サブアクション [{0}]"
		};
		dictionary["FocusSlotTier2SubActionTagFormat"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "所属父级: 扇区 {0} [{1}]",
			[LanguageCode.ZhTw] = "所屬父級: 扇區 {0} [{1}]",
			[LanguageCode.En] = "Parent: Sector {0} [{1}]",
			[LanguageCode.Ja] = "親: セクター {0} [{1}]"
		};
		dictionary["FocusSlotTier2SubActionSubtitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "向外划动二级扇区即可触发此动作",
			[LanguageCode.ZhTw] = "向外劃動二級扇區即可觸發此動作",
			[LanguageCode.En] = "Swipe outward onto this sub-sector to trigger this action",
			[LanguageCode.Ja] = "第2階層セクターへ外側にスワイプしてこのアクションをトリガー"
		};
		dictionary["FocusSlotPrimaryTitleFormat"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "扇区 {0} [{1}]",
			[LanguageCode.ZhTw] = "扇區 {0} [{1}]",
			[LanguageCode.En] = "Sector {0} [{1}]",
			[LanguageCode.Ja] = "セクター {0} [{1}]"
		};
		dictionary["FocusSlotPrimaryTag"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "一级主扇区",
			[LanguageCode.ZhTw] = "一級主扇區",
			[LanguageCode.En] = "Tier-1 Primary Sector",
			[LanguageCode.Ja] = "第1階層主セクター"
		};
		dictionary["FocusSlotPrimarySubtitleInherited"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "💡 专属方案未配置本槽位，已自动继承全局方案「{0}」",
			[LanguageCode.ZhTw] = "💡 專屬方案未設定本位置，已自動繼承全域方案「{0}」",
			[LanguageCode.En] = "💡 Slot not configured in app profile; inherited from global profile \"{0}\"",
			[LanguageCode.Ja] = "💡 専用プロファイル未設定のため、グローバル「{0}」から自動継承"
		};
		dictionary["FocusSlotPrimarySubtitleDefault"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "点击右侧轮盘直接选中扇区，或在下方配置动作与级联子菜单",
			[LanguageCode.ZhTw] = "點擊右側輪盤直接選取扇區，或在下方設定動作與級聯子選單",
			[LanguageCode.En] = "Click the wheel on the right to select a sector, or configure actions below",
			[LanguageCode.Ja] = "右側のホイールをクリックして選択するか、以下でアクションとサブメニューを設定"
		};
		dictionary["MappingsEditIndicatorBatch"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🎯 批量修改模式 (已多选 {0} 个扇区)",
			[LanguageCode.ZhTw] = "🎯 批次修改模式 (已多選 {0} 個扇區)",
			[LanguageCode.En] = "🎯 Batch Edit Mode ({0} sectors selected)",
			[LanguageCode.Ja] = "🎯 一括編集モード（{0}個のセクターを選択中）"
		};
		dictionary["MappingsEditIndicatorCenter"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🎯 正在编辑: 中心核心圆动作",
			[LanguageCode.ZhTw] = "🎯 正在編輯: 中心核心圓動作",
			[LanguageCode.En] = "🎯 Editing: Center Core Action",
			[LanguageCode.Ja] = "🎯 編集中: 中心コアアクション"
		};
		dictionary["MappingsEditIndicatorSub"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🌟 正在编辑: 二级动作 [{0}]",
			[LanguageCode.ZhTw] = "🌟 正在編輯: 二級動作 [{0}]",
			[LanguageCode.En] = "🌟 Editing: Sub-Action [{0}]",
			[LanguageCode.Ja] = "🌟 編集中: サブアクション [{0}]"
		};
		dictionary["MappingsEditIndicatorPrimary"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🎯 正在编辑: 扇区 {0} [{1}]",
			[LanguageCode.ZhTw] = "🎯 正在編輯: 扇區 {0} [{1}]",
			[LanguageCode.En] = "🎯 Editing: Sector {0} [{1}]",
			[LanguageCode.Ja] = "🎯 編集中: セクター {0} [{1}]"
		};
		dictionary["MappingsEditIndicatorDragging"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔄 正在拖拽 [{0}]，{1}",
			[LanguageCode.ZhTw] = "🔄 正在拖曳 [{0}]，{1}",
			[LanguageCode.En] = "🔄 Dragging [{0}], {1}",
			[LanguageCode.Ja] = "🔄 [{0}] をドラッグ中、{1}"
		};
		dictionary["MappingsEditIndicatorSubSwapped"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🎯 已对调二级动作顺序：[{0}] ↔ [{1}]！",
			[LanguageCode.ZhTw] = "🎯 已對調二級動作順序：[{0}] ↔ [{1}]！",
			[LanguageCode.En] = "🎯 Swapped sub-action order: [{0}] ↔ [{1}]!",
			[LanguageCode.Ja] = "🎯 サブアクション順序を入れ替えました: [{0}] ↔ [{1}]!"
		};
		dictionary["MappingsEditIndicatorSubCrossSwapped"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🎯 已跨扇区对调二级动作：[{0}] ↔ [{1}]！",
			[LanguageCode.ZhTw] = "🎯 已跨扇區對調二級動作：[{0}] ↔ [{1}]！",
			[LanguageCode.En] = "🎯 Swapped sub-actions across sectors: [{0}] ↔ [{1}]!",
			[LanguageCode.Ja] = "🎯 セクター間でサブアクションを入れ替えました: [{0}] ↔ [{1}]!"
		};
		dictionary["MappingsEditIndicatorSubMoved"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🎯 已将二级动作 [{0}] 移动至目标扇区！",
			[LanguageCode.ZhTw] = "🎯 已將二級動作 [{0}] 移動至目標扇區！",
			[LanguageCode.En] = "🎯 Moved sub-action [{0}] to target sector!",
			[LanguageCode.Ja] = "🎯 サブアクション [{0}] を対象セクターに移動しました！"
		};
		dictionary["MappingsEditIndicatorSwapped"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🎯 已将 [{0}] 与 [{1}] 成功对调位置{2}！",
			[LanguageCode.ZhTw] = "🎯 已將 [{0}] 與 [{1}] 成功對調位置{2}！",
			[LanguageCode.En] = "🎯 Successfully swapped [{0}] and [{1}]{2}!",
			[LanguageCode.Ja] = "🎯 [{0}] と [{1}] の位置を入れ替えました{2}！"
		};
		dictionary["MappingsEditIndicatorLinkedHint"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = " (已联动二级菜单)",
			[LanguageCode.ZhTw] = " (已聯動二級選單)",
			[LanguageCode.En] = " (linked sub-wheel)",
			[LanguageCode.Ja] = "（サブホイール連動）"
		};
		Translations = dictionary;
	}
}
