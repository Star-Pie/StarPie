using System;
using System.Collections.Generic;
using System.Linq;

namespace WinPieGestures;

/// <summary>
/// 动作编辑地址：精确定位某个配置方案、层级、主扇区及二级子动作上下文。
/// </summary>
public readonly record struct ActionAddress(string ProfileName, int LayerIndex, int SlotIndex, int? SubIndex, Guid ProfileInstanceId = default, WheelLayer? LayerInstance = null)
{
	public bool IsCenter => SlotIndex == -1;
	public bool IsSubAction => SubIndex.HasValue;

	public bool MatchesContext(WheelProfile? profile, int layerIndex)
	{
		if (profile == null) return false;
		if (!string.Equals(ProfileName, profile.ProcessName, StringComparison.OrdinalIgnoreCase)) return false;
		if (LayerIndex != layerIndex) return false;
		if (LayerInstance != null && (profile.Layers == null || layerIndex < 0 || layerIndex >= profile.Layers.Count ||
			!ReferenceEquals(LayerInstance, profile.Layers[layerIndex]))) return false;
		if (ProfileInstanceId != default)
		{
			Guid curId = SettingsWindow.GetProfileInstanceId(profile);
			if (curId != ProfileInstanceId) return false;
		}
		return true;
	}

	public bool MatchesContext(string? profileName, int layerIndex)
	{
		return string.Equals(ProfileName, profileName, StringComparison.OrdinalIgnoreCase) && LayerIndex == layerIndex;
	}

	public override string ToString()
	{
		string slotDesc = IsCenter ? "中心核圆" : $"扇区 {SlotIndex + 1}";
		string subDesc = SubIndex.HasValue ? $" > 子动作 {SubIndex.Value + 1}" : "";
		return $"[{ProfileName}:L{LayerIndex}] {slotDesc}{subDesc}";
	}
}

/// <summary>
/// 动作快照：对主动作、子动作列表及继承状态进行完整深拷贝。
/// </summary>
public sealed class ActionSnapshot
{
	public ActionItem? Action { get; }
	public List<ActionItem>? SubActions { get; }
	public bool IsInherited { get; }

	public ActionSnapshot(ActionItem? action, bool isInherited = false)
	{
		Action = action?.Clone();
		SubActions = action?.SubActions?.Select(s => s.Clone()).ToList();
		IsInherited = isInherited;
	}

	public ActionSnapshot(ActionItem? action, List<ActionItem>? subActions, bool isInherited = false)
	{
		Action = action?.Clone();
		SubActions = subActions?.Select(s => s.Clone()).ToList();
		IsInherited = isInherited;
	}
}

/// <summary>
/// 强类型动作编辑事务接口。
/// </summary>
public interface IActionTransaction
{
	string Description { get; }
	ActionAddress Address { get; }
	int ItemsCount { get; }
	bool MatchesContext(WheelProfile? profile, int layerIndex);
	bool MatchesContext(string? profileName, int layerIndex);
	void Undo(SettingsWindow window);
	void Redo(SettingsWindow window);
}

/// <summary>
/// 单扇区/单动作编辑事务：涵盖属性修改、子动作增删排序、恢复继承等。
/// </summary>
public sealed class SingleSectorTransaction : IActionTransaction
{
	public string Description { get; }
	public ActionAddress Address { get; }
	public ActionSnapshot Before { get; }
	public ActionSnapshot After { get; }
	public int ItemsCount => 2 + (Before.SubActions?.Count ?? 0) + (After.SubActions?.Count ?? 0);

	public SingleSectorTransaction(string description, ActionAddress address, ActionSnapshot before, ActionSnapshot after)
	{
		Description = description;
		Address = address;
		Before = before;
		After = after;
	}

	public bool MatchesContext(WheelProfile? profile, int layerIndex) => Address.MatchesContext(profile, layerIndex);
	public bool MatchesContext(string? profileName, int layerIndex) => Address.MatchesContext(profileName, layerIndex);

	public void Undo(SettingsWindow window)
	{
		ApplySnapshot(window, Before);
	}

	public void Redo(SettingsWindow window)
	{
		ApplySnapshot(window, After);
	}

	private void ApplySnapshot(SettingsWindow window, ActionSnapshot snapshot)
	{
		WheelProfile? profile = window.GetSelectedProfile();
		if (profile == null || !MatchesContext(profile, profile.ActiveLayerIndex)) return;

		if (Address.IsCenter)
		{
			profile.CenterAction = snapshot.Action?.Clone();
			profile.SyncActiveLayerFromRootProperties();
		}
		else if (Address.SlotIndex >= 0 && profile.Actions != null && Address.SlotIndex < profile.Actions.Count)
		{
			if (snapshot.Action != null)
			{
				profile.Actions[Address.SlotIndex] = snapshot.Action.Clone();
				if (snapshot.SubActions != null)
				{
					profile.Actions[Address.SlotIndex].SubActions = snapshot.SubActions.Select(s => s.Clone()).ToList();
				}
				profile.Actions[Address.SlotIndex].IsInherited = snapshot.IsInherited;
			}
		}

		window.OnTransactionApplied(Address);
	}
}

/// <summary>
/// 跨扇区操作事务：涵盖跨扇区移动、对调子动作等，同时原子备份和恢复两端扇区。
/// </summary>
public sealed class CrossSectorTransaction : IActionTransaction
{
	public string Description { get; }
	public ActionAddress SourceAddress { get; }
	public ActionAddress TargetAddress { get; }
	public ActionSnapshot SourceBefore { get; }
	public ActionSnapshot SourceAfter { get; }
	public ActionSnapshot TargetBefore { get; }
	public ActionSnapshot TargetAfter { get; }
	public int ItemsCount => 4 + (SourceBefore.SubActions?.Count ?? 0) + (SourceAfter.SubActions?.Count ?? 0) + (TargetBefore.SubActions?.Count ?? 0) + (TargetAfter.SubActions?.Count ?? 0);

	public ActionAddress Address => SourceAddress;

	public CrossSectorTransaction(
		string description,
		ActionAddress sourceAddress,
		ActionAddress targetAddress,
		ActionSnapshot sourceBefore,
		ActionSnapshot sourceAfter,
		ActionSnapshot targetBefore,
		ActionSnapshot targetAfter)
	{
		Description = description;
		SourceAddress = sourceAddress;
		TargetAddress = targetAddress;
		SourceBefore = sourceBefore;
		SourceAfter = sourceAfter;
		TargetBefore = targetBefore;
		TargetAfter = targetAfter;
	}

	public bool MatchesContext(WheelProfile? profile, int layerIndex)
	{
		return SourceAddress.MatchesContext(profile, layerIndex) && TargetAddress.MatchesContext(profile, layerIndex);
	}

	public bool MatchesContext(string? profileName, int layerIndex)
	{
		return SourceAddress.MatchesContext(profileName, layerIndex) && TargetAddress.MatchesContext(profileName, layerIndex);
	}

	public void Undo(SettingsWindow window)
	{
		WheelProfile? profile = window.GetSelectedProfile();
		if (profile == null || !MatchesContext(profile, profile.ActiveLayerIndex)) return;
		ApplySector(window, SourceAddress, SourceBefore);
		ApplySector(window, TargetAddress, TargetBefore);
		window.OnTransactionApplied(TargetAddress);
	}

	public void Redo(SettingsWindow window)
	{
		WheelProfile? profile = window.GetSelectedProfile();
		if (profile == null || !MatchesContext(profile, profile.ActiveLayerIndex)) return;
		ApplySector(window, SourceAddress, SourceAfter);
		ApplySector(window, TargetAddress, TargetAfter);
		window.OnTransactionApplied(TargetAddress);
	}

	private void ApplySector(SettingsWindow window, ActionAddress addr, ActionSnapshot snapshot)
	{
		WheelProfile? profile = window.GetSelectedProfile();
		if (profile == null) return;

		if (addr.IsCenter)
		{
			profile.CenterAction = snapshot.Action?.Clone();
			profile.SyncActiveLayerFromRootProperties();
		}
		else if (addr.SlotIndex >= 0 && profile.Actions != null && addr.SlotIndex < profile.Actions.Count)
		{
			if (snapshot.Action != null)
			{
				profile.Actions[addr.SlotIndex] = snapshot.Action.Clone();
				if (snapshot.SubActions != null)
				{
					profile.Actions[addr.SlotIndex].SubActions = snapshot.SubActions.Select(s => s.Clone()).ToList();
				}
				profile.Actions[addr.SlotIndex].IsInherited = snapshot.IsInherited;
			}
		}
	}
}

/// <summary>
/// 事务历史记录栈：提供有边界、强类型、上下文隔离的撤销与重做。
/// </summary>
public sealed class ActionTransactionHistory
{
	public const int MaxHistoryCount = 50;
	public const int MaxSnapshotItemsBudget = 500;
	private readonly LinkedList<IActionTransaction> _undoStack = new LinkedList<IActionTransaction>();
	private readonly LinkedList<IActionTransaction> _redoStack = new LinkedList<IActionTransaction>();
	private int _currentSnapshotItemsCount = 0;

	public int UndoCount => _undoStack.Count;
	public int RedoCount => _redoStack.Count;
	public int ItemsBudgetCount => _currentSnapshotItemsCount;

	public bool CanUndo(WheelProfile? profile, int layerIndex)
	{
		if (_undoStack.Count == 0 || profile == null) return false;
		return _undoStack.Last!.Value.MatchesContext(profile, layerIndex);
	}

	public bool CanRedo(WheelProfile? profile, int layerIndex)
	{
		if (_redoStack.Count == 0 || profile == null) return false;
		return _redoStack.Last!.Value.MatchesContext(profile, layerIndex);
	}

	public bool CanUndo(string? profileName, int layerIndex)
	{
		if (_undoStack.Count == 0) return false;
		return _undoStack.Last!.Value.MatchesContext(profileName, layerIndex);
	}

	public bool CanRedo(string? profileName, int layerIndex)
	{
		if (_redoStack.Count == 0) return false;
		return _redoStack.Last!.Value.MatchesContext(profileName, layerIndex);
	}

	public string? GetUndoDescription(WheelProfile? profile, int layerIndex)
	{
		if (!CanUndo(profile, layerIndex)) return null;
		return _undoStack.Last?.Value.Description;
	}

	public string? GetRedoDescription(WheelProfile? profile, int layerIndex)
	{
		if (!CanRedo(profile, layerIndex)) return null;
		return _redoStack.Last?.Value.Description;
	}

	public string? GetUndoDescription(string? profileName, int layerIndex)
	{
		if (!CanUndo(profileName, layerIndex)) return null;
		return _undoStack.Last?.Value.Description;
	}

	public string? GetRedoDescription(string? profileName, int layerIndex)
	{
		if (!CanRedo(profileName, layerIndex)) return null;
		return _redoStack.Last?.Value.Description;
	}

	public void Push(IActionTransaction transaction)
	{
		_undoStack.AddLast(transaction);
		_currentSnapshotItemsCount += transaction.ItemsCount;
		while (_undoStack.Count > MaxHistoryCount || _currentSnapshotItemsCount > MaxSnapshotItemsBudget)
		{
			var first = _undoStack.First!.Value;
			_undoStack.RemoveFirst();
			_currentSnapshotItemsCount -= first.ItemsCount;
		}
		_redoStack.Clear();
	}

	public void PushOrCoalesce(IActionTransaction transaction)
	{
		if (_undoStack.Last?.Value is SingleSectorTransaction lastTx &&
		    transaction is SingleSectorTransaction newTx &&
		    lastTx.Address == newTx.Address &&
		    lastTx.Description == newTx.Description)
		{
			var coalesced = new SingleSectorTransaction(newTx.Description, newTx.Address, lastTx.Before, newTx.After);
			_currentSnapshotItemsCount -= lastTx.ItemsCount;
			_undoStack.RemoveLast();
			_undoStack.AddLast(coalesced);
			_currentSnapshotItemsCount += coalesced.ItemsCount;
			_redoStack.Clear();
			return;
		}
		Push(transaction);
	}

	public bool Undo(SettingsWindow window)
	{
		WheelProfile? profile = window.GetSelectedProfile();
		if (profile == null) return false;

		if (!CanUndo(profile, profile.ActiveLayerIndex))
		{
			return false;
		}

		IActionTransaction tx = _undoStack.Last!.Value;
		_undoStack.RemoveLast();
		_currentSnapshotItemsCount -= tx.ItemsCount;
		tx.Undo(window);
		_redoStack.AddLast(tx);
		while (_redoStack.Count > MaxHistoryCount)
		{
			_redoStack.RemoveFirst();
		}
		return true;
	}

	public bool Redo(SettingsWindow window)
	{
		WheelProfile? profile = window.GetSelectedProfile();
		if (profile == null) return false;

		if (!CanRedo(profile, profile.ActiveLayerIndex))
		{
			return false;
		}

		IActionTransaction tx = _redoStack.Last!.Value;
		_redoStack.RemoveLast();
		tx.Redo(window);
		_undoStack.AddLast(tx);
		_currentSnapshotItemsCount += tx.ItemsCount;
		while (_undoStack.Count > MaxHistoryCount || _currentSnapshotItemsCount > MaxSnapshotItemsBudget)
		{
			var first = _undoStack.First!.Value;
			_undoStack.RemoveFirst();
			_currentSnapshotItemsCount -= first.ItemsCount;
		}
		return true;
	}

	public void Clear()
	{
		_undoStack.Clear();
		_redoStack.Clear();
		_currentSnapshotItemsCount = 0;
	}
}

// SettingsWindow partial class extension
public partial class SettingsWindow
{
	private readonly ActionTransactionHistory _transactionHistory = new ActionTransactionHistory();
	private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<WheelProfile, ProfileSessionId> _profileSessionMap = new();
	private sealed class ProfileSessionId { public Guid Id { get; } = Guid.NewGuid(); }
	internal static Guid GetProfileInstanceId(WheelProfile profile) => _profileSessionMap.GetOrCreateValue(profile).Id;

	private ActionSnapshot? _focusEditStartSnapshot;

	internal WheelProfile? GetSelectedProfile() => _selectedProfile ?? ConfigManager.CurrentConfig?.Profiles.FirstOrDefault();

	internal ActionAddress GetCurrentAddress()
	{
		WheelProfile? profile = GetSelectedProfile();
		string profName = profile?.ProcessName ?? "Global";
		Guid profId = profile != null ? GetProfileInstanceId(profile) : default;
		int layer = profile?.ActiveLayerIndex ?? 0;
		return new ActionAddress(profName, layer, _selectedSlotIndex, _selectedSubActionIndex, profId, profile?.GetActiveLayer());
	}

	internal ActionItem? EnsureLocalSlotForEdit(WheelProfile profile, int slotIndex)
	{
		return EnsureLocalPrimaryActionForEdit(profile, slotIndex);
	}

	internal ActionSnapshot CaptureCenterSnapshot(WheelProfile? profile = null)
	{
		profile ??= GetSelectedProfile();
		if (profile == null) return new ActionSnapshot(null);
		return new ActionSnapshot(profile.CenterAction);
	}

	internal ActionSnapshot CaptureSectorSnapshot(WheelProfile profile, int slotIndex)
	{
		if (profile == null) return new ActionSnapshot(null);

		if (slotIndex == -1)
		{
			return new ActionSnapshot(profile.CenterAction);
		}

		if (slotIndex >= 0 && profile.Actions != null && slotIndex < profile.Actions.Count)
		{
			ActionItem action = profile.Actions[slotIndex];
			bool isInherited = action.IsInherited || !WheelProfile.IsActionConfigured(action);
			return new ActionSnapshot(action, action.SubActions, isInherited);
		}

		return new ActionSnapshot(null);
	}

	internal ActionSnapshot CaptureSectorSnapshot(int slotIndex)
	{
		WheelProfile? profile = GetSelectedProfile();
		return CaptureSectorSnapshot(profile!, slotIndex);
	}

	internal void RecordCenterTransaction(WheelProfile profile, string description, ActionSnapshot before, ActionSnapshot after)
	{
		if (profile == null) return;
		if (AreSnapshotsEquivalent(before, after)) return;

		ActionAddress addr = new ActionAddress(profile.ProcessName, profile.ActiveLayerIndex, -1, null, GetProfileInstanceId(profile), profile.GetActiveLayer());
		var tx = new SingleSectorTransaction(description, addr, before, after);
		_transactionHistory.Push(tx);
		UpdateUndoRedoButtonStates();
	}

	internal void RecordSingleSectorTransaction(WheelProfile profile, int slotIndex, string description, ActionSnapshot before, ActionSnapshot after)
	{
		if (profile == null) return;
		if (AreSnapshotsEquivalent(before, after)) return;

		ActionAddress addr = new ActionAddress(profile.ProcessName, profile.ActiveLayerIndex, slotIndex, _selectedSubActionIndex, GetProfileInstanceId(profile), profile.GetActiveLayer());
		var tx = new SingleSectorTransaction(description, addr, before, after);
		_transactionHistory.Push(tx);
		UpdateUndoRedoButtonStates();
	}

	internal void RecordSingleSectorTransaction(string description, int slotIndex, ActionSnapshot before, ActionSnapshot after)
	{
		WheelProfile? profile = GetSelectedProfile();
		if (profile != null)
		{
			RecordSingleSectorTransaction(profile, slotIndex, description, before, after);
		}
	}

	internal void RecordCrossSectorTransaction(
		string description,
		int srcSlot, ActionSnapshot srcBefore, ActionSnapshot srcAfter,
		int tgtSlot, ActionSnapshot tgtBefore, ActionSnapshot tgtAfter)
	{
		WheelProfile? profile = GetSelectedProfile();
		if (profile == null) return;

		if (AreSnapshotsEquivalent(srcBefore, srcAfter) && AreSnapshotsEquivalent(tgtBefore, tgtAfter))
		{
			return;
		}

		ActionAddress srcAddr = new ActionAddress(profile.ProcessName, profile.ActiveLayerIndex, srcSlot, null, GetProfileInstanceId(profile), profile.GetActiveLayer());
		ActionAddress tgtAddr = new ActionAddress(profile.ProcessName, profile.ActiveLayerIndex, tgtSlot, null, GetProfileInstanceId(profile), profile.GetActiveLayer());

		var tx = new CrossSectorTransaction(description, srcAddr, tgtAddr, srcBefore, srcAfter, tgtBefore, tgtAfter);
		_transactionHistory.Push(tx);
		UpdateUndoRedoButtonStates();
	}

	internal void RecordFocusPropertyChange(string description, bool coalesce = false)
	{
		WheelProfile? profile = GetSelectedProfile();
		if (profile == null) return;
		int slotIndex = _selectedSlotIndex;
		int? subIndex = _selectedSubActionIndex;

		ActionSnapshot current = slotIndex == -1 ? CaptureCenterSnapshot(profile) : CaptureSectorSnapshot(profile, slotIndex);
		if (_focusEditStartSnapshot == null)
		{
			_focusEditStartSnapshot = current;
			return;
		}

		if (AreSnapshotsEquivalent(_focusEditStartSnapshot, current)) return;

		ActionAddress addr = new ActionAddress(profile.ProcessName, profile.ActiveLayerIndex, slotIndex, subIndex, GetProfileInstanceId(profile), profile.GetActiveLayer());
		var tx = new SingleSectorTransaction(description, addr, _focusEditStartSnapshot, current);
		if (coalesce)
		{
			_transactionHistory.PushOrCoalesce(tx);
		}
		else
		{
			_transactionHistory.Push(tx);
		}
		UpdateUndoRedoButtonStates();
		_focusEditStartSnapshot = current;
	}

	internal void ResetFocusEditSnapshot()
	{
		WheelProfile? profile = GetSelectedProfile();
		if (profile == null)
		{
			_focusEditStartSnapshot = null;
			return;
		}
		_focusEditStartSnapshot = _selectedSlotIndex == -1 ? CaptureCenterSnapshot(profile) : CaptureSectorSnapshot(profile, _selectedSlotIndex);
	}

	internal void OnTransactionApplied(ActionAddress addr)
	{
		ResetFocusEditSnapshot();
		UpdateUndoRedoButtonStates();
		RefreshSlots();
		RefreshFocusSubActionsListAndChips();
		UpdateFocusEditorUi();
		RenderMappingsWheelPreview();
		ScheduleAutoSave();
	}

	private bool AreSnapshotsEquivalent(ActionSnapshot a, ActionSnapshot b)
	{
		if (a == null && b == null) return true;
		if (a == null || b == null) return false;
		if (a.IsInherited != b.IsInherited) return false;
		if (!AreActionItemsEquivalent(a.Action, b.Action)) return false;

		int aSubCount = a.SubActions?.Count ?? 0;
		int bSubCount = b.SubActions?.Count ?? 0;
		if (aSubCount != bSubCount) return false;

		for (int i = 0; i < aSubCount; i++)
		{
			if (!AreActionItemsEquivalent(a.SubActions![i], b.SubActions![i]))
			{
				return false;
			}
		}

		return true;
	}

	private static bool AreActionItemsEquivalent(ActionItem? a, ActionItem? b)
	{
		if (a == null && b == null) return true;
		if (a == null || b == null) return false;

		if (a.Name != b.Name ||
		    a.Type != b.Type ||
		    a.Parameter != b.Parameter ||
		    a.Arguments != b.Arguments ||
		    a.IconKey != b.IconKey ||
		    a.CustomIconSvg != b.CustomIconSvg ||
		    a.CommandTerminal != b.CommandTerminal ||
		    a.BrowserChoice != b.BrowserChoice ||
		    a.BrowserPath != b.BrowserPath ||
		    a.InheritAppIconPath != b.InheritAppIconPath ||
		    a.RunAsStandardUser != b.RunAsStandardUser ||
		    a.LayoutMode != b.LayoutMode ||
		    a.CustomTextColor != b.CustomTextColor ||
		    a.CustomFontFamily != b.CustomFontFamily ||
		    a.CustomIconSize != b.CustomIconSize ||
		    a.CustomFontSize != b.CustomFontSize ||
		    a.CustomTextPlacement != b.CustomTextPlacement ||
		    a.CustomTextOffsetX != b.CustomTextOffsetX ||
		    a.CustomTextOffsetY != b.CustomTextOffsetY ||
		    a.IsInherited != b.IsInherited)
		{
			return false;
		}

		// PluginActionRef
		if ((a.PluginActionRef == null) != (b.PluginActionRef == null)) return false;
		if (a.PluginActionRef != null)
		{
			if (a.PluginActionRef.PluginId != b.PluginActionRef!.PluginId ||
			    a.PluginActionRef.ContributionId != b.PluginActionRef.ContributionId)
			{
				return false;
			}
		}

		// ExtensionData
		if ((a.ExtensionData == null) != (b.ExtensionData == null)) return false;
		if (a.ExtensionData != null)
		{
			if (a.ExtensionData.Count != b.ExtensionData!.Count) return false;
			foreach (var kvp in a.ExtensionData)
			{
				if (!b.ExtensionData.TryGetValue(kvp.Key, out var bVal) || kvp.Value != bVal)
				{
					return false;
				}
			}
		}

		// Extras
		if ((a.Extras == null) != (b.Extras == null)) return false;
		if (a.Extras != null)
		{
			if (a.Extras.Count != b.Extras!.Count) return false;
			foreach (var kvp in a.Extras)
			{
				if (!b.Extras.TryGetValue(kvp.Key, out var bVal) || kvp.Value.GetRawText() != bVal.GetRawText())
				{
					return false;
				}
			}
		}

		return true;
	}

	public void ExecuteUndo()
	{
		if (_transactionHistory.Undo(this))
		{
			if (MappingsCurrentEditIndicator != null)
			{
				MappingsCurrentEditIndicator.Text = "↩️ 已撤销上一步操作";
				MappingsCurrentEditIndicator.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(56, 189, 248));
			}
		}
	}

	public void ExecuteRedo()
	{
		if (_transactionHistory.Redo(this))
		{
			if (MappingsCurrentEditIndicator != null)
			{
				MappingsCurrentEditIndicator.Text = "↪️ 已重做上一步操作";
				MappingsCurrentEditIndicator.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(56, 189, 248));
			}
		}
	}

	public void UpdateUndoRedoButtonStates()
	{
		WheelProfile? profile = GetSelectedProfile();
		string? profName = profile?.ProcessName;
		int layer = profile?.ActiveLayerIndex ?? 0;

		bool canUndo = _transactionHistory.CanUndo(profile, layer);
		bool canRedo = _transactionHistory.CanRedo(profile, layer);

		if (FocusUndoSubActionsBtn != null)
		{
			FocusUndoSubActionsBtn.IsEnabled = canUndo;
			FocusUndoSubActionsBtn.Opacity = canUndo ? 1.0 : 0.45;
			string? desc = _transactionHistory.GetUndoDescription(profile, layer);
			FocusUndoSubActionsBtn.ToolTip = canUndo
				? (string.IsNullOrEmpty(desc) ? "撤销上一步操作 (Ctrl+Z)" : $"撤销: {desc} (Ctrl+Z)")
				: "暂无可撤销的操作 (Ctrl+Z)";
		}

		if (FocusRedoSubActionsBtn != null)
		{
			FocusRedoSubActionsBtn.IsEnabled = canRedo;
			FocusRedoSubActionsBtn.Opacity = canRedo ? 1.0 : 0.45;
			string? desc = _transactionHistory.GetRedoDescription(profile, layer);
			FocusRedoSubActionsBtn.ToolTip = canRedo
				? (string.IsNullOrEmpty(desc) ? "重做上一步操作 (Ctrl+Y)" : $"重做: {desc} (Ctrl+Y)")
				: "暂无可重做的操作 (Ctrl+Y)";
		}
	}
}
