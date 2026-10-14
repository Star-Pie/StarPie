using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace WinPieGestures;

public enum ClipboardContentType
{
	None,
	SingleAction,
	SectorBundle
}

/// <summary>
/// StarPie 动作与扇区内部剪贴板。
/// 在内存沙箱中保持完全独立副本（切断引用共享），支持单动作复制与整扇区包复制。
/// </summary>
public static class ActionClipboard
{
	private static ActionItem? _copiedAction;
	private static List<ActionItem>? _copiedSubActions;
	private static ClipboardContentType _contentType = ClipboardContentType.None;
	private static string? _sourceProfileName;
	private static int? _sourceLayerIndex;
	private static int? _sourceSlotIndex;

	public static bool HasContent => _contentType != ClipboardContentType.None && _copiedAction != null;
	public static ClipboardContentType ContentType => _contentType;
	public static string? SourceProfileName => _sourceProfileName;
	public static int? SourceLayerIndex => _sourceLayerIndex;
	public static int? SourceSlotIndex => _sourceSlotIndex;

	public static void CopySingleAction(ActionItem action, string? profile = null, int? layer = null, int? slot = null)
	{
		if (action == null) return;
		_copiedAction = action.Clone();
		_copiedAction.IsInherited = false;
		// 单动作复制时不携带子动作列表
		_copiedAction.SubActions.Clear();
		_copiedSubActions = null;
		_contentType = ClipboardContentType.SingleAction;
		_sourceProfileName = profile;
		_sourceLayerIndex = layer;
		_sourceSlotIndex = slot;
	}

	public static void CopySectorBundle(ActionItem primaryAction, string? profile = null, int? layer = null, int? slot = null)
	{
		if (primaryAction == null) return;
		_copiedAction = primaryAction.Clone();
		_copiedAction.IsInherited = false;
		_copiedSubActions = primaryAction.SubActions?.Select(s =>
		{
			var clone = s.Clone();
			clone.IsInherited = false;
			return clone;
		}).ToList();
		_contentType = ClipboardContentType.SectorBundle;
		_sourceProfileName = profile;
		_sourceLayerIndex = layer;
		_sourceSlotIndex = slot;
	}

	public static (ActionItem? Action, List<ActionItem>? SubActions, ClipboardContentType Type) Paste()
	{
		if (!HasContent || _copiedAction == null)
		{
			return (null, null, ClipboardContentType.None);
		}

		var clonedAction = _copiedAction.Clone();
		clonedAction.IsInherited = false;
		if (_contentType == ClipboardContentType.SingleAction)
		{
			clonedAction.SubActions.Clear();
		}
		var clonedSubs = _copiedSubActions?.Select(s =>
		{
			var clone = s.Clone();
			clone.IsInherited = false;
			return clone;
		}).ToList();

		return (clonedAction, clonedSubs, _contentType);
	}

	public static void Clear()
	{
		_copiedAction = null;
		_copiedSubActions = null;
		_contentType = ClipboardContentType.None;
		_sourceProfileName = null;
		_sourceLayerIndex = null;
		_sourceSlotIndex = null;
	}
}

public partial class SettingsWindow
{
	public void CopyCurrentSelection()
	{
		WheelProfile? profile = GetSelectedProfile();
		if (profile == null) return;

		string profName = profile.ProcessName;
		int layer = profile.ActiveLayerIndex;

		if (_selectedSlotIndex == -1)
		{
			// 中心核心：复制单动作
			ActionItem? center = profile.CenterAction;
			if (center != null)
			{
				ActionClipboard.CopySingleAction(center, profName, layer, -1);
				NotifyClipboardCopied(center.Name ?? I18n.T("CenterCoreTitle"));
			}
		}
		else if (_selectedSubActionIndex.HasValue && _selectedSlotIndex >= 0)
		{
			// 选中的是特定二级子动作：复制该子动作（单动作）
			ActionItem? effPrimary = profile.GetEffectiveAction(_selectedSlotIndex);
			var subs = (profile.Actions != null && _selectedSlotIndex < profile.Actions.Count && profile.Actions[_selectedSlotIndex].SubActions != null && profile.Actions[_selectedSlotIndex].SubActions.Count > 0)
				? profile.Actions[_selectedSlotIndex].SubActions
				: effPrimary?.SubActions;

			int subIdx = _selectedSubActionIndex.Value;
			if (subs != null && subIdx >= 0 && subIdx < subs.Count)
			{
				ActionItem subItem = subs[subIdx];
				ActionClipboard.CopySingleAction(subItem, profName, layer, _selectedSlotIndex);
				NotifyClipboardCopied(subItem.Name ?? $"子动作 {subIdx + 1}");
			}
		}
		else if (_selectedSlotIndex >= 0)
		{
			// 选中的是一级主扇区：复制整扇区包（包含其二级子动作列表）
			ActionItem? effPrimary = profile.GetEffectiveAction(_selectedSlotIndex);
			ActionItem? targetPrimary = (profile.Actions != null && _selectedSlotIndex < profile.Actions.Count && WheelProfile.IsActionConfigured(profile.Actions[_selectedSlotIndex]))
				? profile.Actions[_selectedSlotIndex]
				: effPrimary;

			if (targetPrimary != null)
			{
				ActionClipboard.CopySectorBundle(targetPrimary, profName, layer, _selectedSlotIndex);
				NotifyClipboardCopied(targetPrimary.Name ?? $"扇区 {_selectedSlotIndex + 1}");
			}
		}

		UpdateClipboardButtonStates();
	}

	public void CopyCurrentAction()
	{
		WheelProfile? profile = GetSelectedProfile();
		if (profile == null) return;
		string profName = profile.ProcessName;
		int layer = profile.ActiveLayerIndex;

		ActionItem? current = GetCurrentFocusActionItem();
		if (current != null)
		{
			ActionClipboard.CopySingleAction(current, profName, layer, _selectedSlotIndex);
			NotifyClipboardCopied(current.Name ?? I18n.T("SingleActionCopied"));
			UpdateClipboardButtonStates();
		}
	}

	public void CopyEntireSector()
	{
		WheelProfile? profile = GetSelectedProfile();
		if (profile == null || _selectedSlotIndex < 0) return;
		string profName = profile.ProcessName;
		int layer = profile.ActiveLayerIndex;

		ActionItem? effPrimary = profile.GetEffectiveAction(_selectedSlotIndex);
		ActionItem? targetPrimary = (profile.Actions != null && _selectedSlotIndex < profile.Actions.Count && WheelProfile.IsActionConfigured(profile.Actions[_selectedSlotIndex]))
			? profile.Actions[_selectedSlotIndex]
			: effPrimary;

		if (targetPrimary != null)
		{
			ActionClipboard.CopySectorBundle(targetPrimary, profName, layer, _selectedSlotIndex);
			NotifyClipboardCopied(targetPrimary.Name ?? $"扇区 {_selectedSlotIndex + 1}");
			UpdateClipboardButtonStates();
		}
	}

	public void PasteToCurrentSelection()
	{
		if (!ActionClipboard.HasContent) return;

		WheelProfile? profile = GetSelectedProfile();
		if (profile == null) return;

		string profName = profile.ProcessName;
		int layer = profile.ActiveLayerIndex;

		var (pastedAction, pastedSubs, contentType) = ActionClipboard.Paste();
		if (pastedAction == null) return;

		if (_selectedSlotIndex == -1)
		{
			// 中心核心粘贴：
			// 铁律：中心核心仅接收单动作，整组不能静默丢弃子列表；中心拒绝直接接收含子列表的整包。
			if (contentType == ClipboardContentType.SectorBundle && pastedSubs != null && pastedSubs.Count > 0)
			{
				AppMessageBox.Show(this, I18n.T("CenterSingleActionOnlyWarning"), "StarPie", MessageBoxButton.OK, MessageBoxImage.Warning);
				return;
			}

			var before = CaptureCenterSnapshot(profile);

			profile.CenterAction = pastedAction.Clone();
			profile.CenterAction.SubActions.Clear();
			profile.CenterAction.IsInherited = false;
			profile.SyncActiveLayerFromRootProperties();

			var after = CaptureCenterSnapshot(profile);
			RecordCenterTransaction(profile, "粘贴动作至中心核心", before, after);

			UpdateFocusEditorUi();
			RefreshSlots();
			RenderMappingsWheelPreview();
			ScheduleAutoSave();
		}
		else if (_selectedSubActionIndex.HasValue && _selectedSlotIndex >= 0)
		{
			// 粘贴覆盖或更新二级子动作
			ActionItem? effSlot = profile.GetEffectiveAction(_selectedSlotIndex);
			var existingSubs = (profile.Actions != null && _selectedSlotIndex < profile.Actions.Count && profile.Actions[_selectedSlotIndex].SubActions != null)
				? profile.Actions[_selectedSlotIndex].SubActions
				: effSlot?.SubActions;

			int currentSubCount = existingSubs?.Count ?? 0;
			int subIdx = _selectedSubActionIndex.Value;
			bool isAdding = (subIdx < 0 || subIdx >= currentSubCount);

			if (isAdding)
			{
				bool isFan = string.Equals(ConfigManager.CurrentConfig?.SubmenuStyle, "Fan", StringComparison.OrdinalIgnoreCase);
				int maxAllowed = isFan ? 3 : 4;
				if (currentSubCount >= maxAllowed)
				{
					string msg = isFan ? I18n.T("SubActionCapacityFanExceeded") : I18n.T("SubActionCapacitySubRingExceeded");
					AppMessageBox.Show(this, msg, "StarPie", MessageBoxButton.OK, MessageBoxImage.Warning);
					return;
				}
			}

			var before = CaptureSectorSnapshot(profile, _selectedSlotIndex);
			EnsureLocalSlotForEdit(profile, _selectedSlotIndex);
			ActionItem primary = profile.Actions[_selectedSlotIndex];
			primary.SubActions ??= new List<ActionItem>();

			ActionItem newSub = pastedAction.Clone();
			newSub.SubActions.Clear(); // 子动作本身不嵌套子动作
			newSub.IsInherited = false;

			if (subIdx >= 0 && subIdx < primary.SubActions.Count)
			{
				primary.SubActions[subIdx] = newSub;
			}
			else
			{
				primary.SubActions.Add(newSub);
				_selectedSubActionIndex = primary.SubActions.Count - 1;
			}

			var after = CaptureSectorSnapshot(profile, _selectedSlotIndex);
			RecordSingleSectorTransaction(profile, _selectedSlotIndex, "粘贴二级动作", before, after);

			UpdateFocusEditorUi();
			RefreshFocusSubActionsListAndChips();
			RefreshSlots();
			RenderMappingsWheelPreview();
			ScheduleAutoSave();
		}
		else if (_selectedSlotIndex >= 0)
		{
			// 粘贴到一级主扇区
			ActionItem? effSlot = profile.GetEffectiveAction(_selectedSlotIndex);
			var existingSubs = (profile.Actions != null && _selectedSlotIndex < profile.Actions.Count && profile.Actions[_selectedSlotIndex].SubActions != null)
				? profile.Actions[_selectedSlotIndex].SubActions
				: effSlot?.SubActions;
			int existingSubsCount = existingSubs?.Count ?? 0;

			// 整包替换已有包含子动作的扇区时，提前确认
			if (contentType == ClipboardContentType.SectorBundle && existingSubsCount > 0)
			{
				string confirmMsg = string.Format(I18n.T("FocusPasteSectorBundleConfirm"), existingSubsCount);
				var res = AppMessageBox.Show(this, confirmMsg, "StarPie", MessageBoxButton.OKCancel, MessageBoxImage.Question);
				if (res != MessageBoxResult.OK)
				{
					return; // 用户取消，零修改
				}
			}

			var before = CaptureSectorSnapshot(profile, _selectedSlotIndex);
			EnsureLocalSlotForEdit(profile, _selectedSlotIndex);

			ActionItem targetSlot = profile.Actions[_selectedSlotIndex];
			List<ActionItem> targetSubs = targetSlot.SubActions;

			// 使用完整 Clone 替换目标槽位对象，确保所有字段（包括 ExtensionData、运行标记、外观覆盖等）无遗漏
			ActionItem newSlot = pastedAction.Clone();
			newSlot.IsInherited = false;

			if (contentType == ClipboardContentType.SectorBundle && pastedSubs != null)
			{
				newSlot.SubActions = pastedSubs.Select(s => s.Clone()).ToList();
			}
			else
			{
				// 单动作粘贴：明确保留目标原有的二级子动作列表
				newSlot.SubActions = targetSubs != null ? targetSubs.Select(s => s.Clone()).ToList() : new List<ActionItem>();
			}

			profile.Actions[_selectedSlotIndex] = newSlot;

			var after = CaptureSectorSnapshot(profile, _selectedSlotIndex);
			RecordSingleSectorTransaction(profile, _selectedSlotIndex, "粘贴扇区动作", before, after);

			UpdateFocusEditorUi();
			RefreshFocusSubActionsListAndChips();
			RefreshSlots();
			RenderMappingsWheelPreview();
			ScheduleAutoSave();
		}
	}

	private void NotifyClipboardCopied(string itemName)
	{
		if (MappingsCurrentEditIndicator != null)
		{
			MappingsCurrentEditIndicator.Text = $"📋 已复制: [{itemName}]";
			MappingsCurrentEditIndicator.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(56, 189, 248));
		}
	}

	public void UpdateClipboardButtonStates()
	{
		bool hasContent = ActionClipboard.HasContent;
		if (FocusPasteActionBtn != null)
		{
			FocusPasteActionBtn.IsEnabled = hasContent;
			FocusPasteActionBtn.Opacity = hasContent ? 1.0 : 0.45;
			FocusPasteActionBtn.ToolTip = hasContent 
				? $"粘贴剪贴板动作 ({I18n.T("FocusPasteActionBtnToolTip")})"
				: "剪贴板为空，请先复制动作 (Ctrl+C)";
		}
	}
}
