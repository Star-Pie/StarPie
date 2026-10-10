using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using StarPie.Plugin;
using WinPieGestures.Plugins;

namespace WinPieGestures;

/// <summary>手势映射编辑行视图模型。</summary>
public class GestureMappingViewModel : INotifyPropertyChanged
{
	public GestureMapping Mapping { get; }
	private ICollectionView? _pluginActionOptions;

	private sealed class PluginSessionSnapshot
	{
		public PluginActionRef? PluginActionRef { get; set; }
		public Dictionary<string, string>? ExtensionData { get; set; }
	}

	private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GestureMapping, PluginSessionSnapshot> s_sessionSnapshots = new();

	private void SavePluginSessionSnapshot()
	{
		if (Mapping.Action.PluginActionRef != null)
		{
			var snapshot = s_sessionSnapshots.GetOrCreateValue(Mapping);
			snapshot.PluginActionRef = Mapping.Action.PluginActionRef;
			snapshot.ExtensionData = Mapping.Action.ExtensionData != null
				? new Dictionary<string, string>(Mapping.Action.ExtensionData, StringComparer.OrdinalIgnoreCase)
				: null;
		}
	}

	private void RestorePluginSessionSnapshot()
	{
		if (s_sessionSnapshots.TryGetValue(Mapping, out var snapshot))
		{
			if (snapshot.PluginActionRef != null)
			{
				Mapping.Action.PluginActionRef = snapshot.PluginActionRef;
			}
			Mapping.Action.ExtensionData = snapshot.ExtensionData != null
				? new Dictionary<string, string>(snapshot.ExtensionData, StringComparer.OrdinalIgnoreCase)
				: null;
		}
	}

	public GestureMappingViewModel(GestureMapping mapping)
	{
		Mapping = mapping;
		if (mapping.Action.Type == PluginActionBinding.TypeName && mapping.Action.PluginActionRef != null)
		{
			SavePluginSessionSnapshot();
		}
	}

	/// <summary>可选图样清单（单段 8 + 常用双段/三段，多段以 "-" 分隔避免歧义）。</summary>
	public List<ActionTypeOption> PatternChoices => PatternOptions;

	public static List<ActionTypeOption> PatternOptions { get; } = new List<ActionTypeOption>
	{
		new ActionTypeOption { Tag = "U", DisplayText = "↑ 上" },
		new ActionTypeOption { Tag = "D", DisplayText = "↓ 下" },
		new ActionTypeOption { Tag = "L", DisplayText = "← 左" },
		new ActionTypeOption { Tag = "R", DisplayText = "→ 右" },
		new ActionTypeOption { Tag = "UL", DisplayText = "↖ 左上" },
		new ActionTypeOption { Tag = "UR", DisplayText = "↗ 右上" },
		new ActionTypeOption { Tag = "DL", DisplayText = "↙ 左下" },
		new ActionTypeOption { Tag = "DR", DisplayText = "↘ 右下" },
		new ActionTypeOption { Tag = "D-R", DisplayText = "下→右 (L)" },
		new ActionTypeOption { Tag = "R-D", DisplayText = "右→下 (Γ)" },
		new ActionTypeOption { Tag = "D-L", DisplayText = "下→左 (反L)" },
		new ActionTypeOption { Tag = "L-D", DisplayText = "左→下" },
		new ActionTypeOption { Tag = "U-R", DisplayText = "上→右" },
		new ActionTypeOption { Tag = "R-U", DisplayText = "右→上 (┘)" },
		new ActionTypeOption { Tag = "U-L", DisplayText = "上→左" },
		new ActionTypeOption { Tag = "L-U", DisplayText = "左→上 (└)" },
		new ActionTypeOption { Tag = "U-D", DisplayText = "上→下" },
		new ActionTypeOption { Tag = "D-U", DisplayText = "下→上" },
		new ActionTypeOption { Tag = "L-R", DisplayText = "左→右" },
		new ActionTypeOption { Tag = "R-L", DisplayText = "右→左" },
		new ActionTypeOption { Tag = "UL-R", DisplayText = "左上→右" },
		new ActionTypeOption { Tag = "UR-L", DisplayText = "右上→左" },
		new ActionTypeOption { Tag = "DL-R", DisplayText = "左下→右" },
		new ActionTypeOption { Tag = "DR-L", DisplayText = "右下→左" },
		new ActionTypeOption { Tag = "D-R-D", DisplayText = "下→右→下 (Z)" },
		new ActionTypeOption { Tag = "R-D-R", DisplayText = "右→下→右" },
		new ActionTypeOption { Tag = "D-L-D", DisplayText = "下→左→下 (反Z)" },
		new ActionTypeOption { Tag = "U-R-U", DisplayText = "上→右→上" },
		new ActionTypeOption { Tag = "U-L-U", DisplayText = "上→左→上" },
		new ActionTypeOption { Tag = "D-R-U", DisplayText = "下→右→上 (S)" },
		new ActionTypeOption { Tag = "D-L-U", DisplayText = "下→左→上 (反S)" },
		new ActionTypeOption { Tag = "L-D-L", DisplayText = "左→下→左" }
	};

	public List<ActionTypeItem> ActionTypes => SlotViewModel.AggregatedActionTypes;

	public List<ActionTypeItem> AggregatedActionTypes => SlotViewModel.AggregatedActionTypes;

	public string AggregatedType
	{
		get
		{
			string t = Mapping.Action.Type ?? "Hotkey";
			if (t == PluginActionBinding.TypeName)
			{
				// 类型下拉里插件动作只有一项，类型即自身。
				// 具体是哪一个动作由子下拉承载，不必再把贡献点 ID 编码进 Tag。
				return PluginActionBinding.TypeName;
			}
			if (t == "Tile" || t == "ToggleTopmost" || t == "MoveMonitor" || t == "WindowOpacity" || t == "SwitchWindow" || t == "WindowManager")
			{
				return "WindowManager";
			}
			if (t == "Url") return "WebUrl";
			if (t == "App") return "Launch";
			if (t == "OpenFolder") return "Folder";
			if (t == "ScreenOcr") return "Ocr";
			return t;
		}
		set
		{
			if (!string.IsNullOrEmpty(value))
			{
				if (IsExclusiveRecording)
				{
					IsExclusiveRecording = false;
					OnExclusiveRecordingCancelledRequest?.Invoke();
				}
				if (value == PluginActionBinding.TypeName)
				{
					Type = PluginActionBinding.TypeName;
				}
				else if (value == "WindowManager")
				{
					if (!IsWindowManagerType)
					{
						string preferredWindowType = ActionTypeCatalog.GetPreferredWindowManagerType() ?? "Tile";
						Type = preferredWindowType;
						if (preferredWindowType == "Tile" && string.IsNullOrEmpty(Mapping.Action.Parameter))
						{
							Mapping.Action.Parameter = "2L";
						}
					}
				}
				else
				{
					Type = value;
				}
				OnPropertyChanged(nameof(AggregatedType));
				OnPropertyChanged(nameof(IsWindowManagerType));
				OnPropertyChanged(nameof(IsPluginType));
				_pluginActionOptions = PluginActionBinding.BuildPluginActionView(Mapping.Action.PluginActionRef?.FullId);
				OnPropertyChanged(nameof(PluginActionOptions));
				NotifyAllPropertiesChanged();
			}
		}
	}

	/// <summary>当前动作是否为插件动作（用于界面显示对应的编辑面板与子下拉）。</summary>
	public bool IsPluginType => Type == PluginActionBinding.TypeName;

	/// <summary>
	/// 子下拉的候选插件动作，已按插件分组（分组头即插件显示名，本身不可选中）。
	/// </summary>
	public ICollectionView? PluginActionOptions => _pluginActionOptions ??= PluginActionBinding.BuildPluginActionView(Mapping.Action.PluginActionRef?.FullId);

	/// <summary>
	/// 子下拉当前选中的插件动作全 ID。
	/// </summary>
	public string? SelectedPluginActionFullId
	{
		get
		{
			if (!IsPluginType) return null;
			return PluginActionBinding.ProjectSelectedAction(Mapping.Action);
		}
		set
		{
			if (!IsPluginType) return;
			// ItemsSource 重建时下拉框会把 SelectedValue 置空 —— 那不是用户的意图。
			// 不忽略的话，每次刷新都会把用户配好的动作清掉。
			if (string.IsNullOrEmpty(value)) return;

			if (PluginActionBinding.ProjectSelectedAction(Mapping.Action) == value) return;

			if (PluginActionBinding.Apply(Mapping.Action, value))
			{
				if (Mapping.Action.PluginActionRef != null)
				{
					SavePluginSessionSnapshot();
				}
				OnPropertyChanged(nameof(SelectedPluginActionFullId));
				OnPropertyChanged(nameof(IsPluginActionBroken));
				OnPropertyChanged(nameof(HasPluginParameters));
				OnPropertyChanged(nameof(MissingRequiredPluginParametersCount));
				OnPropertyChanged(nameof(HasMissingRequiredPluginParameters));
				OnPropertyChanged(nameof(PluginParamsButtonText));
				OnPropertyChanged(nameof(PluginParamsTip));
				NotifyAllPropertiesChanged();
			}
		}
	}

	public bool HasPluginParameters
	{
		get
		{
			if (!IsPluginType) return false;
			string? fullId = Mapping.Action.PluginActionRef?.FullId;
			if (string.IsNullOrEmpty(fullId)) return false;
			if (PluginHost.TryGetAction(fullId, out PluginActionRegistration reg))
			{
				return reg.Parameters != null && reg.Parameters.Count > 0;
			}
			return false;
		}
	}

	public static bool HasRequiredParameters(IEnumerable<ParameterField>? parameters)
	{
		if (parameters == null) return false;
		foreach (var p in parameters)
		{
			if (p.Required && p.Type != ParameterFieldType.Bool)
			{
				return true;
			}
		}
		return false;
	}

	public static int CountMissingRequiredParameters(
		IEnumerable<ParameterField>? parameters,
		IReadOnlyDictionary<string, string>? extensionData)
	{
		if (parameters == null) return 0;
		int count = 0;
		foreach (var p in parameters)
		{
			if (p.Required && p.Type != ParameterFieldType.Bool)
			{
				if (extensionData == null ||
					!extensionData.TryGetValue(p.Key, out string? val) ||
					string.IsNullOrWhiteSpace(val))
				{
					count++;
				}
			}
		}
		return count;
	}

	public int MissingRequiredPluginParametersCount
	{
		get
		{
			if (!IsPluginType) return 0;
			string? fullId = Mapping.Action.PluginActionRef?.FullId;
			if (string.IsNullOrEmpty(fullId)) return 0;
			if (PluginHost.TryGetAction(fullId, out PluginActionRegistration reg))
			{
				return CountMissingRequiredParameters(reg.Parameters, Mapping.Action.ExtensionData);
			}
			return 0;
		}
	}

	public bool HasMissingRequiredPluginParameters => MissingRequiredPluginParametersCount > 0;

	public string PluginParamsButtonText
	{
		get
		{
			string text = I18n.T("PluginsCardSettingsButton");
			return HasMissingRequiredPluginParameters ? text.Replace("⚙", "⚠️") : text;
		}
	}

	public string PluginParamsTip
	{
		get
		{
			int missingCount = MissingRequiredPluginParametersCount;
			if (missingCount > 0)
			{
				return string.Format(I18n.T("PluginsPanelRequiredParams"), missingCount);
			}

			string? fullId = Mapping.Action.PluginActionRef?.FullId;
			if (!string.IsNullOrEmpty(fullId) && PluginHost.TryGetAction(fullId, out PluginActionRegistration reg))
			{
				if (HasRequiredParameters(reg.Parameters))
				{
					return I18n.T("PluginsCardSettingsButton");
				}
			}

			return I18n.T("PluginsPanelAllOptional");
		}
	}

	public string OpacityTip => I18n.T("TipGestureOpacity");

	public string PluginActionBrokenTip => I18n.T("PluginsActionBrokenHint");

	/// <summary>
	/// 所引用的插件动作是否已失效（插件被停用或卸载）。
	/// <para>与「尚未选定」严格区分：这种情况必须显式提示，否则用户会以为自己的配置丢了。</para>
	/// </summary>
	public bool IsPluginActionBroken => IsPluginType && PluginActionBinding.IsReferenceBroken(Mapping.Action);

	public bool IsWindowManagerType => 
		Type == "Tile" || Type == "ToggleTopmost" || Type == "MoveMonitor" || 
		Type == "WindowOpacity" || Type == "SwitchWindow" || Type == "WindowManager";

	public List<ActionTypeOption> WindowManagerSubModes => new List<ActionTypeOption>
	{
		new ActionTypeOption { Tag = "Tile", DisplayText = "平铺窗口排布" },
		new ActionTypeOption { Tag = "TileCycle", DisplayText = "🔄 循环切换平铺" },
		new ActionTypeOption { Tag = "TileRestore", DisplayText = "⏪ 还原平铺快照" },
		new ActionTypeOption { Tag = "ToggleTopmost", DisplayText = "📌 窗口置顶 / 取消置顶" },
		new ActionTypeOption { Tag = "MoveMonitor", DisplayText = "🖥️ 移动至下一显示器" },
		new ActionTypeOption { Tag = "WindowOpacity", DisplayText = "👻 窗口半透明度" },
		new ActionTypeOption { Tag = "SwitchWindow", DisplayText = "🔢 切换任务栏指定应用" }
	};

	public string WindowManagerSubMode
	{
		get
		{
			if (Type == "ToggleTopmost") return "ToggleTopmost";
			if (Type == "MoveMonitor") return "MoveMonitor";
			if (Type == "WindowOpacity") return "WindowOpacity";
			if (Type == "SwitchWindow") return "SwitchWindow";
			if (Type == "Tile")
			{
				if (Mapping.Action.Parameter == WindowTiler.CycleParam) return "TileCycle";
				if (Mapping.Action.Parameter == WindowTiler.RestoreParam) return "TileRestore";
				return "Tile";
			}
			return "Tile";
		}
		set
		{
			if (!IsWindowManagerType) return;
			if (value == "ToggleTopmost") { Type = "ToggleTopmost"; Mapping.Action.Parameter = ""; }
			else if (value == "MoveMonitor") { Type = "MoveMonitor"; Mapping.Action.Parameter = ""; }
			else if (value == "WindowOpacity") { Type = "WindowOpacity"; if (string.IsNullOrEmpty(Mapping.Action.Parameter)) Mapping.Action.Parameter = "80"; }
			else if (value == "SwitchWindow") { Type = "SwitchWindow"; if (string.IsNullOrEmpty(Mapping.Action.Parameter)) Mapping.Action.Parameter = "1"; }
			else if (value == "TileCycle") { Type = "Tile"; Mapping.Action.Parameter = WindowTiler.CycleParam; }
			else if (value == "TileRestore") { Type = "Tile"; Mapping.Action.Parameter = WindowTiler.RestoreParam; }
			else if (value == "Tile") { Type = "Tile"; if (string.IsNullOrEmpty(Mapping.Action.Parameter) || Mapping.Action.Parameter.StartsWith("__")) Mapping.Action.Parameter = "2L"; }
			OnPropertyChanged(nameof(WindowManagerSubMode));
			OnPropertyChanged(nameof(IsTileSubMode));
			OnPropertyChanged(nameof(IsOpacitySubMode));
			OnPropertyChanged(nameof(IsSwitchWindowSubMode));
			OnPropertyChanged(nameof(Parameter));
		}
	}

	public bool IsTileSubMode => Type == "Tile" && Mapping.Action.Parameter != WindowTiler.CycleParam && Mapping.Action.Parameter != WindowTiler.RestoreParam;
	public bool IsOpacitySubMode => Type == "WindowOpacity";
	public bool IsSwitchWindowSubMode => Type == "SwitchWindow";

	public void NotifyAllPropertiesChanged()
	{
		if (IsPluginType && Mapping.Action.PluginActionRef != null)
		{
			SavePluginSessionSnapshot();
		}
		OnPropertyChanged(nameof(Type));
		OnPropertyChanged(nameof(AggregatedType));
		OnPropertyChanged(nameof(IsHotkeyType));
		OnPropertyChanged(nameof(IsLaunchType));
		OnPropertyChanged(nameof(IsWebUrlType));
		OnPropertyChanged(nameof(IsFolderType));
		OnPropertyChanged(nameof(IsSystemType));
		OnPropertyChanged(nameof(IsCommandType));
		OnPropertyChanged(nameof(IsSwitchWindowType));
		OnPropertyChanged(nameof(IsTileType));
		OnPropertyChanged(nameof(IsOcrType));
		OnPropertyChanged(nameof(IsShellToolType));
		OnPropertyChanged(nameof(ShellToolTitle));
		OnPropertyChanged(nameof(BrowseShellToolTip));
		OnPropertyChanged(nameof(PickShellToolText));
		OnPropertyChanged(nameof(WebUrlTip));
		OnPropertyChanged(nameof(CanInheritAppIcon));
		OnPropertyChanged(nameof(InheritAppIconPath));
		OnPropertyChanged(nameof(HasInheritedAppIcon));
		OnPropertyChanged(nameof(RunAsStandardUser));
		OnPropertyChanged(nameof(IsWindowManagerType));
		OnPropertyChanged(nameof(WindowManagerSubMode));
		OnPropertyChanged(nameof(IsTileSubMode));
		OnPropertyChanged(nameof(IsOpacitySubMode));
		OnPropertyChanged(nameof(IsSwitchWindowSubMode));
		OnPropertyChanged(nameof(Parameter));
		OnPropertyChanged(nameof(Name));
		OnPropertyChanged(nameof(SelectedSystemPreset));
		OnPropertyChanged(nameof(TileLayout));

		// 插件动作相关：让子下拉的可见性与选中值跟上类型变化。
		//
		// 注意**不要**在这里通知 PluginActionOptions：它的 getter 每次求值都会重建集合视图，
		// 而本方法在很多路径上被调用（包括用户刚在子下拉里选定动作那一下）。
		// 一旦纳入全量通知，用户选完动作就会立刻重建候选集并重设 ItemsSource。
		// 类型切换那处（AggregatedType 的 setter）已经单独通知过它了，那是唯一真正需要的时机。
		OnPropertyChanged(nameof(IsPluginType));
		OnPropertyChanged(nameof(SelectedPluginActionFullId));
		OnPropertyChanged(nameof(IsPluginActionBroken));
		OnPropertyChanged(nameof(HasPluginParameters));
		OnPropertyChanged(nameof(MissingRequiredPluginParametersCount));
		OnPropertyChanged(nameof(HasMissingRequiredPluginParameters));
		OnPropertyChanged(nameof(PluginParamsButtonText));
		OnPropertyChanged(nameof(PluginParamsTip));
		OnPropertyChanged(nameof(PluginActionBrokenTip));
		OnPropertyChanged(nameof(OpacityTip));
		OnPropertyChanged(nameof(Arguments));
		OnPropertyChanged(nameof(IsExclusiveRecording));
		OnPropertyChanged(nameof(PauseHotkeysButtonText));
		OnPropertyChanged(nameof(PauseHotkeysToolTip));
		OnPropertyChanged(nameof(HotkeyBuilderButtonText));
		OnPropertyChanged(nameof(HotkeyBuilderToolTip));
		OnPropertyChanged(nameof(LaunchArgsLabel));
		OnPropertyChanged(nameof(LaunchArgsToolTip));
		OnPropertyChanged(nameof(PickProgramButtonText));
		OnPropertyChanged(nameof(PickProgramToolTip));
		OnPropertyChanged(nameof(CaptureWindowButtonText));
		OnPropertyChanged(nameof(CaptureWindowToolTip));
		OnPropertyChanged(nameof(BrowseExeButtonText));
		OnPropertyChanged(nameof(BrowseExeToolTip));
		OnPropertyChanged(nameof(RunAsStandardUserLabel));
		OnPropertyChanged(nameof(RunAsStandardUserToolTip));
	}

	public string Pattern
	{
		get => Mapping.Pattern ?? "D";
		set
		{
			if (Mapping.Pattern != value && !string.IsNullOrEmpty(value))
			{
				Mapping.Pattern = value;
				OnPropertyChanged(nameof(Pattern));
			}
		}
	}

	public string Type
	{
		get => Mapping.Action.Type ?? "Hotkey";
		set
		{
			if (!string.IsNullOrEmpty(value) && Mapping.Action.Type != value)
			{
				if (IsExclusiveRecording)
				{
					IsExclusiveRecording = false;
					OnExclusiveRecordingCancelledRequest?.Invoke();
				}
				bool wasPlugin = IsPluginType || Mapping.Action.PluginActionRef != null;
				if (value != PluginActionBinding.TypeName)
				{
					if (wasPlugin)
					{
						SavePluginSessionSnapshot();
						PluginActionBinding.Clear(Mapping.Action);
					}
				}
				else
				{
					RestorePluginSessionSnapshot();
				}

				Mapping.Action.Type = value;
				OnPropertyChanged(nameof(Type));
				OnPropertyChanged(nameof(AggregatedType));
				OnPropertyChanged(nameof(IsHotkeyType));
				OnPropertyChanged(nameof(IsLaunchType));
				OnPropertyChanged(nameof(IsWebUrlType));
				OnPropertyChanged(nameof(IsFolderType));
				OnPropertyChanged(nameof(IsSystemType));
				OnPropertyChanged(nameof(IsCommandType));
				OnPropertyChanged(nameof(IsSwitchWindowType));
				OnPropertyChanged(nameof(IsTileType));
				OnPropertyChanged(nameof(IsOcrType));
				OnPropertyChanged(nameof(IsShellToolType));
				OnPropertyChanged(nameof(IsWindowManagerType));
				OnPropertyChanged(nameof(WindowManagerSubMode));
				OnPropertyChanged(nameof(IsTileSubMode));
				OnPropertyChanged(nameof(IsOpacitySubMode));
				OnPropertyChanged(nameof(IsSwitchWindowSubMode));

				// 类型切换是子下拉**唯一**需要重建候选集的时机；
				// 其余路径（例如用户刚选定了一个动作）刻意不重建，见 NotifyAllPropertiesChanged 的说明。
				OnPropertyChanged(nameof(IsPluginType));
				_pluginActionOptions = PluginActionBinding.BuildPluginActionView(Mapping.Action.PluginActionRef?.FullId);
				OnPropertyChanged(nameof(PluginActionOptions));
				OnPropertyChanged(nameof(SelectedPluginActionFullId));
				OnPropertyChanged(nameof(IsPluginActionBroken));
				OnPropertyChanged(nameof(HasPluginParameters));
				OnPropertyChanged(nameof(MissingRequiredPluginParametersCount));
				OnPropertyChanged(nameof(HasMissingRequiredPluginParameters));
				OnPropertyChanged(nameof(PluginParamsButtonText));
				OnPropertyChanged(nameof(PluginParamsTip));
			}
		}
	}

	public bool IsHotkeyType => Type == "Hotkey";

	public bool IsLaunchType => Type == "Launch" || Type == "App";

	public bool IsWebUrlType => Type == "WebUrl" || Type == "Url";

	public bool IsFolderType => Type == "Folder" || Type == "OpenFolder";

	public bool IsSystemType => Type == "System";

	public bool IsCommandType => Type == "Command";

	public bool IsSwitchWindowType => Type == "SwitchWindow";

	public bool IsTileType => Type == "Tile";

	public bool IsOcrType => Type == "Ocr" || Type == "ScreenOcr";

	public bool IsShellToolType => Type == "ShellTool";

	public string ShellToolTitle
	{
		get
		{
			if (IsShellToolType && !string.IsNullOrEmpty(Parameter))
			{
				var tool = ShellActionPickerWindow.ShellTools?.FirstOrDefault(t => t.Id == Parameter || string.Equals(t.Verb, Parameter, StringComparison.OrdinalIgnoreCase));
				if (tool != null) return $"{tool.Name} ({tool.Id})";
				return Parameter;
			}
			return I18n.T("FocusShellToolDefaultTitle");
		}
	}

	public string BrowseShellToolTip => I18n.T("FocusPickShellToolBtnToolTip");
	public string PickShellToolText => I18n.T("FocusPickShellToolBtnText");

	public string WebUrlTip => I18n.T("WebUrlToolTip");

	public bool CanInheritAppIcon => Type != "Launch" && Type != "App";

	public string? InheritAppIconPath
	{
		get => Mapping.Action.InheritAppIconPath;
		set
		{
			Mapping.Action.InheritAppIconPath = value;
			OnPropertyChanged(nameof(InheritAppIconPath));
			OnPropertyChanged(nameof(HasInheritedAppIcon));
		}
	}

	public bool HasInheritedAppIcon => !string.IsNullOrWhiteSpace(InheritAppIconPath);

	public bool RunAsStandardUser
	{
		get => Mapping.Action.RunAsStandardUser;
		set
		{
			Mapping.Action.RunAsStandardUser = value;
			OnPropertyChanged(nameof(RunAsStandardUser));
		}
	}

	public System.Action? OnExclusiveRecordingCancelledRequest { get; set; }

	public string Arguments
	{
		get => Mapping.Action.Arguments ?? "";
		set
		{
			if (Mapping.Action.Arguments != value)
			{
				Mapping.Action.Arguments = value ?? "";
				OnPropertyChanged(nameof(Arguments));
			}
		}
	}

	private static readonly HashSet<GestureMapping> s_activelyRecordingMappings = new();
	private static readonly object s_recordingLock = new();

	public static bool IsMappingInExclusiveRecording(GestureMapping? mapping)
	{
		if (mapping == null) return false;
		lock (s_recordingLock)
		{
			return s_activelyRecordingMappings.Contains(mapping);
		}
	}

	public static void SetMappingExclusiveRecording(GestureMapping? mapping, bool isRecording)
	{
		if (mapping == null) return;
		lock (s_recordingLock)
		{
			if (isRecording)
			{
				s_activelyRecordingMappings.Add(mapping);
			}
			else
			{
				s_activelyRecordingMappings.Remove(mapping);
			}
		}
	}

	public static void ClearAllExclusiveRecordingStates()
	{
		lock (s_recordingLock)
		{
			s_activelyRecordingMappings.Clear();
		}
	}

	public bool IsExclusiveRecording
	{
		get => IsMappingInExclusiveRecording(Mapping);
		set
		{
			bool current = IsMappingInExclusiveRecording(Mapping);
			if (current != value)
			{
				SetMappingExclusiveRecording(Mapping, value);
				OnPropertyChanged(nameof(IsExclusiveRecording));
				OnPropertyChanged(nameof(PauseHotkeysButtonText));
				OnPropertyChanged(nameof(PauseHotkeysToolTip));
			}
		}
	}

	public string PauseHotkeysButtonText => IsExclusiveRecording
		? I18n.T("TogglePauseHotkeysBtnActiveText")
		: I18n.T("TogglePauseHotkeysBtnText");

	public string PauseHotkeysToolTip => IsExclusiveRecording
		? I18n.T("TogglePauseHotkeysBtnActiveToolTip")
		: I18n.T("TogglePauseHotkeysBtnToolTip");

	public string HotkeyBuilderButtonText => I18n.T("FocusHotkeyBuilderBtnText");
	public string HotkeyBuilderToolTip => I18n.T("FocusHotkeyBuilderBtnText");

	public string LaunchArgsLabel => I18n.T("FocusLaunchArgsLabel");
	public string LaunchArgsToolTip => I18n.T("FocusLaunchArgsToolTip");

	public string PickProgramButtonText => I18n.T("FocusLaunchPickProgramBtnText");
	public string PickProgramToolTip => I18n.T("FocusLaunchPickProgramBtnToolTip");

	public string CaptureWindowButtonText => I18n.T("FocusLaunchCaptureWindowBtnText");
	public string CaptureWindowToolTip => I18n.T("FocusLaunchCaptureWindowBtnToolTip");

	public string BrowseExeButtonText => I18n.T("FocusLaunchBrowseExeBtnText");
	public string BrowseExeToolTip => I18n.T("FocusLaunchBrowseExeBtnToolTip");

	public string RunAsStandardUserLabel => I18n.T("FocusLaunchAsUserTitle");
	public string RunAsStandardUserToolTip => I18n.T("FocusLaunchAsUserSubtitle");

	/// <summary>平铺布局下拉（key → 显示名）。</summary>
	public List<ActionTypeOption> TileLayoutOptions
	{
		get
		{
			List<ActionTypeOption> list = new List<ActionTypeOption>
			{
				new ActionTypeOption { Tag = WindowTiler.CycleParam, DisplayText = "🔄 " + I18n.T("TileCycleLabel") },
				new ActionTypeOption { Tag = WindowTiler.CycleBackParam, DisplayText = "⬅️ " + I18n.T("TileCycleBackLabel") },
				new ActionTypeOption { Tag = WindowTiler.RestoreParam, DisplayText = "⏪ " + I18n.T("TileRestoreAllLabel") }
			};
			foreach (string key in WindowTiler.LayoutKeys)
			{
				list.Add(new ActionTypeOption { Tag = key, DisplayText = WindowTiler.LayoutDisplayName(key) });
			}
			return list;
		}
	}

	/// <summary>平铺布局（写入 Parameter）。</summary>
	public string TileLayout
	{
		get
		{
			return IsTileSubMode ? (Mapping.Action.Parameter ?? "") : "";
		}
		set
		{
			if (!IsTileSubMode) return;
			if (Mapping.Action.Parameter != value)
			{
				Mapping.Action.Parameter = value;
				OnPropertyChanged(nameof(TileLayout));
			}
		}
	}

	/// <summary>命令动作的终端选项。</summary>
	public List<ActionTypeItem> Terminals => SlotViewModel.LocalizedTerminals;

	public string CommandTerminal
	{
		get => Mapping.Action.CommandTerminal ?? "cmd";
		set
		{
			if (!IsCommandType) return;
			if (Mapping.Action.CommandTerminal != value && !string.IsNullOrEmpty(value))
			{
				Mapping.Action.CommandTerminal = value;
				OnPropertyChanged(nameof(CommandTerminal));
			}
		}
	}

	/// <summary>系统控制预设（Key ↔ Parameter）。</summary>
	public string SelectedSystemPreset
	{
		get => IsSystemType ? Parameter : "";
		set
		{
			if (!IsSystemType) return;
			if (Parameter != value && !string.IsNullOrEmpty(value))
			{
				Parameter = value;
				OnPropertyChanged(nameof(SelectedSystemPreset));
			}
		}
	}

	/// <summary>切换窗口的序号（仅数字 1~20）。</summary>
	public string NthWindowIndex
	{
		get => IsSwitchWindowSubMode ? Parameter : "";
		set
		{
			if (!IsSwitchWindowSubMode) return;
			string digits = string.IsNullOrEmpty(value) ? "" : new string(value.Where(char.IsDigit).ToArray());
			if (int.TryParse(digits, out int n))
			{
				n = Math.Max(1, Math.Min(20, n));
				digits = n.ToString();
			}
			if (Parameter != digits)
			{
				Parameter = digits;
				OnPropertyChanged(nameof(NthWindowIndex));
			}
		}
	}

	public string Parameter
	{
		get => Mapping.Action.Parameter ?? "";
		set
		{
			if (Mapping.Action.Parameter != value)
			{
				Mapping.Action.Parameter = value ?? "";
				OnPropertyChanged(nameof(Parameter));
			}
		}
	}

		public string AppPathTip => I18n.T("TipGestureAppPath");
	public string BrowseAppTip => I18n.T("TipGestureBrowseApp");
	public string FolderPathTip => I18n.T("TipGestureFolderPath");
	public string BrowseFolderTip => I18n.T("TipGestureBrowseFolder");
	public string CmdTip => I18n.T("TipGestureCmd");
	public string TaskbarSlotTip => I18n.T("TipGestureTaskbarSlot");
	public string TilePresetTip => I18n.T("TipGestureTilePreset");
	public string CustomNameTip => I18n.T("TipGestureCustomName");
	public string TestButtonText => I18n.T("BtnTestGesture");
	public string DeleteButtonTip => I18n.T("TipDeleteGesture");

	public string Name
	{
		get => Mapping.Action.Name ?? "";
		set
		{
			if (Mapping.Action.Name != value)
			{
				Mapping.Action.Name = value ?? "";
				OnPropertyChanged(nameof(Name));
			}
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	protected void OnPropertyChanged(string propertyName)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}
