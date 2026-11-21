using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace WinPieGestures;

public partial class SettingsWindow
{
	// The displayed page is the children of this path. A null child selects the
	// parent itself, so entering a branch also exposes that sector's own action.
	private int[]? _deepCanvasParent;
	private int? _deepCanvasChild;
	private WheelProfile? _deepCanvasProfile;
	private WheelLayer? _deepCanvasLayer;
	private int _deepBindingVersion;
	private int[]? _deepBoundPath;
	private ActionItem? _deepBoundNode;
	private bool _deepBoundInherited;

	private void SelectCanvasSubAction(int slot, int index) => SelectSubAction(slot, index);

	private void OpenLegacyCanvasBranch(int slot, int index)
	{
        ShowStudioActionInspector();
		SelectSubAction(slot, index);
		var profile = GetSelectedProfile();
		var layer = profile?.GetActiveLayer();
		if (profile == null || layer == null || layer.EffectiveMaxMenuDepth <= 2) return;
		_deepCanvasProfile = profile;
		_deepCanvasLayer = layer;
		_deepCanvasParent = [slot, index];
		_deepCanvasChild = null;
		RenderMappingsWheelPreview();
	}

	private void CloseDeepCanvas()
	{
		if (_deepCanvasParent == null) return;
		_deepCanvasParent = null;
		_deepCanvasChild = null;
		_deepCanvasProfile = null;
		_deepCanvasLayer = null;
		_deepBoundPath = null;
		_deepBoundNode = null;
		_deepBindingVersion++;
		DeepCanvasEditorCard.Visibility = Visibility.Collapsed;
		DeepCanvasEditor.BindAction(null, _ => { });
		FocusActionEditorBorder.Visibility = Visibility.Visible;
		CanvasTierSelector.Visibility = Visibility.Visible;
		DeepCanvasBreadcrumb.Children.Clear();
		DeepCanvasBreadcrumb.Visibility = Visibility.Collapsed;
		MappingsCanvasInstructionsText.Text = I18n.T("MappingsCanvasInstructions");
	}

	private bool HasDeepCanvasContext(WheelProfile? profile) =>
		profile != null && _deepCanvasParent is { Length: >= 2 } path &&
		ReferenceEquals(profile, _deepCanvasProfile) &&
		ReferenceEquals(GestureController.CaptureReleaseLayer(profile), _deepCanvasLayer) &&
		_selectedSlotIndex == path[0] && _selectedSubActionIndex == path[1] &&
		MappingsViewModeCanvasRadio.IsChecked == true;

	private static ActionItem? GetCanvasNode(ActionItem? root, IReadOnlyList<int> path)
	{
		for (int i = 1; i < path.Count; i++)
		{
			if (root?.SubActions is not { } children || (uint)path[i] >= (uint)children.Count) return null;
			root = children[path[i]];
		}
		return root;
	}

	private static ActionItem? GetCanvasRoot(WheelProfile profile, int slot) =>
		(uint)slot < (uint)profile.Actions.Count ? profile.GetEffectiveAction(slot) ?? profile.Actions[slot] : null;

	private bool RenderActiveDeepCanvas(WheelProfile profile)
	{
		if (_deepCanvasParent is not { } path) return false;
		if (!HasDeepCanvasContext(profile))
		{
			CloseDeepCanvas();
			return false;
		}
		var root = GetCanvasRoot(profile, path[0]);
		int depth = _deepCanvasLayer!.EffectiveMaxMenuDepth;
		bool fan = ConfigManager.CurrentConfig.SubmenuStyle == "Fan";
		// Revalidate after undo, deletion, a style change, or lowering the layer limit.
		ActionItem? node = root;
		int valid = 1;
		for (; valid < path.Length && valid < depth - 1; valid++)
		{
			if (node?.SubActions is not { } children ||
				(uint)path[valid] >= (uint)DeepMenuFrame.VisibleSlotCount(children.Count, fan)) break;
			node = children[path[valid]];
		}
		if (valid < 2 || node == null)
		{
			CloseDeepCanvas();
			if (GetCanvasNode(root, path[..2]) == null) _selectedSubActionIndex = null;
			UpdateFocusEditorUi();
			ResetFocusEditSnapshot();
			return false;
		}
		if (valid < path.Length)
		{
			_deepCanvasParent = path = path[..valid];
			_deepCanvasChild = null;
		}
		int count = DeepMenuFrame.VisibleSlotCount(node.SubActions?.Count ?? 0, fan);
		if (_deepCanvasChild is int oldIndex && (uint)oldIndex >= (uint)count) _deepCanvasChild = null;
		int[] selection = _deepCanvasChild is int child ? [..path, child] : path;
		var selected = _deepCanvasChild is int index ? node.SubActions![index] : node;
		if (selected == null) { CloseDeepCanvas(); return false; }

		ClearCanvasHitItems();
		FocusActionEditorBorder.Visibility = Visibility.Collapsed;
		DeepCanvasEditorCard.Visibility = Visibility.Visible;
		CanvasTierSelector.Visibility = Visibility.Collapsed;
		DeepCanvasBreadcrumb.Visibility = Visibility.Visible;
		MappingsCanvasInstructionsText.Text = I18n.T("CanvasBranchGuide");
		DeepCanvasTitle.Text = I18n.TF("CanvasEditLevel", selection.Length, selected.Name);
		MappingsCurrentEditIndicator.Text = DeepCanvasTitle.Text;
		DeepEnter.Content = I18n.T("MenuEditBranch");
		DeepEnter.Visibility = _deepCanvasChild.HasValue && selection.Length < depth ? Visibility.Visible : Visibility.Collapsed;
		var siblings = GetCanvasNode(root, selection[..^1])!.SubActions;
		DeepEarlier.Content = I18n.T("MoveSubActionUp");
		DeepLater.Content = I18n.T("MoveSubActionDown");
		DeepEarlier.IsEnabled = selection[^1] > 0;
		DeepLater.IsEnabled = selection[^1] + 1 < DeepMenuFrame.VisibleSlotCount(siblings.Count, fan);
		DeepDelete.Content = I18n.T("DeleteSubAction");
		DeepUndo.Content = I18n.T("FocusUndoSubActionsBtnText");
		DeepRedo.Content = I18n.T("FocusRedoSubActionsBtnText");
		DeepUndo.IsEnabled = _transactionHistory.CanUndo(profile, profile.ActiveLayerIndex);
		DeepRedo.IsEnabled = _transactionHistory.CanRedo(profile, profile.ActiveLayerIndex);

		DeepCanvasBreadcrumb.Children.Clear();
		AddDeepBreadcrumb(I18n.T("CanvasMainWheel"), 0);
		var ancestor = root!;
		for (int i = 0; i < path.Length; i++)
		{
			if (i > 0) ancestor = ancestor.SubActions[path[i]];
			AddDeepBreadcrumb(ancestor.Name, i + 1);
		}
		bool inherited = root!.IsInherited;
		if (_deepBoundPath == null || !_deepBoundPath.SequenceEqual(selection) || inherited != _deepBoundInherited ||
			!(inherited ? AreActionItemsEquivalent(_deepBoundNode, selected) : ReferenceEquals(_deepBoundNode, selected)))
		{
			_deepBoundPath = selection;
			_deepBoundNode = selected;
			_deepBoundInherited = inherited;
			int version = ++_deepBindingVersion;
			DeepCanvasEditor.BindAction(selected, edited => ApplyCanvasEdit(profile, selection, version, edited));
		}
		DrawDeepCanvasBranch(profile, path, node, _deepCanvasChild);
		return true;
	}

	private void AddDeepBreadcrumb(string name, int depth)
	{
		if (depth > 0) DeepCanvasBreadcrumb.Children.Add(new TextBlock
		{
			Text = "›", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3,0,3,0)
		});
		var button = new Button { Content = name, ToolTip = name, Padding = new Thickness(6,1,6,1),
			Style = (Style)FindResource("ModernButtonStyle"), MaxWidth = 125 };
		button.Click += (_, _) => ShowDeepAncestor(depth);
		DeepCanvasBreadcrumb.Children.Add(button);
	}

	private void ShowDeepAncestor(int depth, bool keepChildSelected = false)
	{
		if (_deepCanvasParent is not { } path) return;
		if (depth < 2)
		{
			if (depth == 0) SelectPrimarySlot(path[0]);
			else if (keepChildSelected) SelectSubAction(path[0], path[1]);
			else SelectPrimarySlot(path[0]);
			return;
		}
		_deepCanvasParent = path[..depth];
		_deepCanvasChild = keepChildSelected && depth < path.Length ? path[depth] : null;
		RenderMappingsWheelPreview();
	}

	private void LeaveDeepCanvasBranch()
	{
		if (_deepCanvasParent != null) ShowDeepAncestor(_deepCanvasParent.Length - 1, keepChildSelected: true);
	}

	private void SelectDeepCanvasChild(int index)
	{
        ShowStudioActionInspector();
		var profile = GetSelectedProfile();
		if (!HasDeepCanvasContext(profile) || _deepCanvasParent is not { } path) return;
		var node = GetCanvasNode(GetCanvasRoot(profile!, path[0]), path);
		if (node?.SubActions is not { } children || (uint)index >= (uint)DeepMenuFrame.VisibleSlotCount(children.Count,
			ConfigManager.CurrentConfig.SubmenuStyle == "Fan")) return;
		_deepCanvasChild = index;
		RenderMappingsWheelPreview();
	}

	private void EnterSelectedDeepBranch_Click(object sender, RoutedEventArgs e)
	{
		if (_deepCanvasParent is not { } path || _deepCanvasChild is not int child ||
			path.Length + 1 >= _deepCanvasLayer!.EffectiveMaxMenuDepth) return;
		_deepCanvasParent = [..path, child];
		_deepCanvasChild = null;
		RenderMappingsWheelPreview();
	}

	private void ApplyCanvasEdit(WheelProfile profile, int[] selection, int version, ActionItem edited)
	{
		if (version != _deepBindingVersion || !HasDeepCanvasContext(GetSelectedProfile()) ||
			!ReferenceEquals(profile, _deepCanvasProfile) || selection.Length > _deepCanvasLayer!.EffectiveMaxMenuDepth) return;
		int[] parentPath = selection[..^1];
		var existing = GetCanvasNode(GetCanvasRoot(profile, selection[0]), selection);
		if (existing == null || !(_deepBoundInherited ? AreActionItemsEquivalent(_deepBoundNode, existing) :
			ReferenceEquals(_deepBoundNode, existing))) return;
		var value = edited.CloneWithoutSubActions();
		value.SubActions = existing.SubActions;
		value.IsInherited = existing.IsInherited;
		if (AreActionItemsEquivalent(existing, value)) return;
		var before = CaptureSectorSnapshot(profile, selection[0]);
		if (GetCanvasNode(EnsureLocalPrimaryActionForEdit(profile, selection[0]), parentPath) is not { } parent) return;
		int i = selection[^1];
		value.SubActions = parent.SubActions[i].SubActions;
		// The property editor replaces a draft at the same address without replacing the secondary parent.
		if (selection.Length == 2)
			_secondaryIdentities.Add(value, _secondaryIdentities.GetValue(parent.SubActions[i], _ => new object()));
		value.IsInherited = false;
		parent.SubActions[i] = value;
		_deepBoundInherited = false;
		_deepBoundNode = value;
		FinishDeepCanvasEdit(profile, selection[0], before);
	}

	private void AddDeepCanvasChild()
	{
		var profile = GetSelectedProfile();
		if (!HasDeepCanvasContext(profile) || _deepCanvasParent is not { } path ||
			path.Length >= _deepCanvasLayer!.EffectiveMaxMenuDepth) return;
		var parent = GetCanvasNode(GetCanvasRoot(profile!, path[0]), path);
		if (parent == null || (parent.SubActions?.Count ?? 0) >= (ConfigManager.CurrentConfig.SubmenuStyle == "Fan" ? 3 : 4)) return;
		var before = CaptureSectorSnapshot(profile!, path[0]);
		parent = GetCanvasNode(EnsureLocalPrimaryActionForEdit(profile!, path[0]), path)!;
		parent.SubActions ??= new();
		_deepCanvasChild = parent.SubActions.Count;
		parent.SubActions.Add(new ActionItem { Name = I18n.TF("EditSubActionNumber", parent.SubActions.Count + 1), Type = "Hotkey" });
		FinishDeepCanvasEdit(profile!, path[0], before);
	}

	private void MoveDeepSelection_Click(object sender, RoutedEventArgs e)
	{
		var profile = GetSelectedProfile();
		if (!HasDeepCanvasContext(profile) || _deepBoundPath is not { } selection) return;
		int index = selection[^1], next = index + (sender == DeepEarlier ? -1 : 1);
		var ownerPath = selection[..^1];
		var owner = GetCanvasNode(GetCanvasRoot(profile!, selection[0]), ownerPath);
		if (owner?.SubActions == null || (uint)next >= (uint)DeepMenuFrame.VisibleSlotCount(owner.SubActions.Count,
			ConfigManager.CurrentConfig.SubmenuStyle == "Fan")) return;
		var before = CaptureSectorSnapshot(profile!, selection[0]);
		var siblings = GetCanvasNode(EnsureLocalPrimaryActionForEdit(profile!, selection[0]), ownerPath)!.SubActions;
		(siblings[index], siblings[next]) = (siblings[next], siblings[index]);
		if (_deepCanvasChild.HasValue) _deepCanvasChild = next;
		else
		{
			_deepCanvasParent = [..ownerPath, next];
			if (selection.Length == 2) _selectedSubActionIndex = next;
		}
		FinishDeepCanvasEdit(profile!, selection[0], before);
	}

	private void DeleteDeepSelection_Click(object sender, RoutedEventArgs e)
	{
		var profile = GetSelectedProfile();
		if (!HasDeepCanvasContext(profile) || _deepBoundPath is not { } selection || _deepBoundNode == null) return;
		int version = _deepBindingVersion;
		long revision = ConfigManager.ConfigurationRevision;
		var expected = _deepBoundNode;
		if (AppMessageBox.Show(this, I18n.TF("CanvasDeleteBranch", expected.Name), I18n.T("DeleteSubAction"),
			MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
		DeleteConfirmedCanvasSelection(profile!, selection, version, revision, expected);
	}

	private void DeleteConfirmedCanvasSelection(WheelProfile profile, int[] selection, int version, long revision, ActionItem expected)
	{
		if (version != _deepBindingVersion || revision != ConfigManager.ConfigurationRevision ||
			!ReferenceEquals(profile, GetSelectedProfile()) || !HasDeepCanvasContext(profile)) return;
		var current = GetCanvasNode(GetCanvasRoot(profile!, selection[0]), selection);
		if (current == null || !(_deepBoundInherited ? AreActionItemsEquivalent(expected, current) : ReferenceEquals(expected, current))) return;
		var before = CaptureSectorSnapshot(profile!, selection[0]);
		var ownerPath = selection[..^1];
		var owner = GetCanvasNode(EnsureLocalPrimaryActionForEdit(profile!, selection[0]), ownerPath);
		if (owner?.SubActions == null || (uint)selection[^1] >= (uint)owner.SubActions.Count) return;
		owner.SubActions.RemoveAt(selection[^1]);
		if (ownerPath.Length == 1) SelectPrimarySlot(selection[0]);
		else { _deepCanvasParent = ownerPath; _deepCanvasChild = null; }
		FinishDeepCanvasEdit(profile!, selection[0], before);
	}

	private void FinishDeepCanvasEdit(WheelProfile profile, int slot, ActionSnapshot before)
	{
		var root = profile.Actions[slot];
		if (!string.Equals(profile.ProcessName, "Global", StringComparison.OrdinalIgnoreCase) &&
			string.Equals(root.Type, "Hotkey", StringComparison.OrdinalIgnoreCase) && !WheelProfile.IsActionConfigured(root))
			root.OverrideGlobalMenu = true;
		RecordSingleSectorTransaction(profile, slot, "MenuEditBranch", before, CaptureSectorSnapshot(profile, slot));
		RenderMappingsWheelPreview();
		ScheduleAutoSave();
	}
}
