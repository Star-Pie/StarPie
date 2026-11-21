using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace WinPieGestures;

public partial class SettingsWindow
{
	private bool _refreshingMenuDepth;

	private void RefreshMenuDepth(WheelProfile? profile)
	{
		if (MenuDepthPicker == null || FixedLayoutToggle == null || EditMenuBranchButton == null || FixedOpenCanvas == null) return;
		_refreshingMenuDepth = true;
		try
		{
			var layer = profile?.Layers != null && (uint)profile.ActiveLayerIndex < (uint)profile.Layers.Count
				? profile.Layers[profile.ActiveLayerIndex] : null;
			FixedLayoutToggle.IsEnabled = layer != null;
			FixedLayoutToggle.Content = I18n.T(layer?.ActiveFixedRings != null ? "FixedReturnLegacy" : "FixedEnableLayout");
			FixedOpenCanvas.Visibility = _fixedLayer == null && layer?.ActiveFixedRings != null && layer.EffectiveMaxMenuDepth >= 3 ? Visibility.Visible : Visibility.Collapsed;
			FixedOpenCanvas.Content = I18n.T("FixedOuterEditor");
			MenuDepthPicker.IsEnabled = layer != null;
			MenuDepthPicker.SelectedIndex = layer == null ? -1 : layer.EffectiveMaxMenuDepth - 2;
		}
		finally { _refreshingMenuDepth = false; }
	}

	private void MenuDepthPicker_Changed(object sender, SelectionChangedEventArgs e)
	{
		if (_refreshingMenuDepth || !_isUiInitialized || _isUpdatingUi || MenuDepthPicker.SelectedIndex < 0 ||
			GetSelectedProfile() is not { } profile) return;
		var layer = profile.GetActiveLayer();
		int depth = MenuDepthPicker.SelectedIndex + 2;
		if (depth == layer.EffectiveMaxMenuDepth) return;
		var address = new ActionAddress(profile.ProcessName, profile.ActiveLayerIndex, -1, null, GetProfileInstanceId(profile), layer);
		_transactionHistory.Push(new DepthEdit(address, layer.MaxMenuDepth, depth));
		SetMenuDepth(layer, depth);
	}

	private void SetMenuDepth(WheelLayer layer, int depth)
	{
		layer.MaxMenuDepth = depth;
		RefreshMenuDepth(GetSelectedProfile());
		RenderMappingsWheelPreview();
		UpdateUndoRedoButtonStates();
		ScheduleAutoSave();
	}

	private sealed record DepthEdit(ActionAddress Address, int Before, int After) : IActionTransaction
	{
		public string Description => "MenuDepthLabel";
		public int ItemsCount => 1;
		public bool MatchesContext(WheelProfile? profile, int index) => Address.MatchesContext(profile, index);
		public bool MatchesContext(string? name, int index) => Address.MatchesContext(name, index);
		public void Undo(SettingsWindow window) => window.SetMenuDepth(Address.LayerInstance!, Before);
		public void Redo(SettingsWindow window) => window.SetMenuDepth(Address.LayerInstance!, After);
	}

	private void FocusEditMenuBranch_Click(object sender, RoutedEventArgs e) =>
		EditMenuBranch(_selectedSlotIndex, _selectedSubActionIndex ?? -1);

	private void EditMenuBranch(int slot, int child)
	{
		if (GetSelectedProfile() is not { } profile || (uint)slot >= (uint)profile.Actions.Count) return;
		MappingsViewModeCanvasRadio.IsChecked = true;
		if (child >= 0)
		{
			if (profile.GetActiveLayer().ActiveFixedRings != null && profile.GetActiveLayer().EffectiveMaxMenuDepth >= 3)
				OpenFixedCanvas(profile);
			else OpenLegacyCanvasBranch(slot, child);
			return;
		}
		SelectPrimarySlot(slot);
		MappingsTier2SegmentRadio.IsChecked = true;
	}

	private static bool MenuChildrenEqual(List<ActionItem>? left, List<ActionItem>? right)
	{
		int count = left?.Count ?? 0;
		if (count != (right?.Count ?? 0)) return false;
		for (int i = 0; i < count; i++)
			if (!AreActionItemsEquivalent(left![i], right![i])) return false;
		return true;
	}
}
