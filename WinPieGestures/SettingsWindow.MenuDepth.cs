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
		if (MenuDepthPicker == null || FixedLayoutToggle == null || FixedOpenCanvas == null) return;
		_refreshingMenuDepth = true;
		try
		{
			var layer = profile?.Layers != null && (uint)profile.ActiveLayerIndex < (uint)profile.Layers.Count
				? profile.Layers[profile.ActiveLayerIndex] : null;
			RefreshSecondaryGeometryNotice(layer);
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
		ChangeMenuLayout(profile, depth, null);
	}

	private void RefreshSecondaryGeometryNotice(WheelLayer? layer)
	{
		if (LegacySubGeometryPanel == null) return;
		bool honeycomb = layer?.ActiveFixedRings != null && layer.EffectiveMaxMenuDepth >= 3 &&
			ConfigManager.CurrentConfig.SubmenuStyle == "Fan";
		LegacySubGeometryPanel.Visibility = honeycomb ? Visibility.Collapsed : Visibility.Visible;
		ResetSubDimensionsButton.Visibility = LegacySubGeometryPanel.Visibility;
		Tab1_SubDimensionsNoticeText.Text = I18n.T(honeycomb ? "FixedHoneycombDimensions" : "Tab1_SubDimensionsNotice");
	}

	// Only explicit depth and style choices create fixed rings. Refreshing a legacy profile is read-only.
	private void ChangeMenuLayout(WheelProfile profile, int depth, string? style)
	{
		var layer = profile.GetActiveLayer();
		var address = new ActionAddress(profile.ProcessName, profile.ActiveLayerIndex, -1, null, GetProfileInstanceId(profile), layer);
		var before = layer.FixedRings;
		var after = before;
		if (Math.Clamp(depth, 2, 5) >= 3 && before == null && !layer.FixedRingsPaused &&
			(style ?? ConfigManager.CurrentConfig.SubmenuStyle) == "Fan")
		{
			after = FixedRingMenu.FromLegacy(profile);
			after.AlwaysVisible = false;
		}
		var beforeStyle = style == null ? null : ConfigManager.CurrentConfig.SubmenuStyle;
		_transactionHistory.Push(new DepthEdit(address, layer.MaxMenuDepth, depth, before, after, beforeStyle, style));
		SetMenuDepth(layer, depth, after, style);
	}

	private void SetMenuDepth(WheelLayer layer, int depth, FixedRingMenu? menu, string? style)
	{
		_twoTierEditorLayer = null;
		layer.FixedRings = menu;
		layer.MaxMenuDepth = depth;
		if (menu != null && !layer.FixedRingsPaused && (style ?? ConfigManager.CurrentConfig.SubmenuStyle) == "Fan")
			CloseDeepCanvas();
		if (style != null) ApplySubmenuStyle(style);
		RefreshMenuDepth(GetSelectedProfile());
		RenderMappingsWheelPreview();
		UpdateUndoRedoButtonStates();
		ScheduleAutoSave();
	}

	private sealed record DepthEdit(ActionAddress Address, int Before, int After, FixedRingMenu? BeforeMenu,
		FixedRingMenu? AfterMenu, string? BeforeStyle, string? AfterStyle) : IActionTransaction
	{
		public string Description => AfterStyle == null ? "MenuDepthLabel" : "SubmenuStyleTitle";
		public int ItemsCount => 1;
		public bool MatchesContext(WheelProfile? profile, int index) => Address.MatchesContext(profile, index);
		public bool MatchesContext(string? name, int index) => Address.MatchesContext(name, index);
		public void Undo(SettingsWindow window) => window.SetMenuDepth(Address.LayerInstance!, Before, BeforeMenu, BeforeStyle);
		public void Redo(SettingsWindow window) => window.SetMenuDepth(Address.LayerInstance!, After, AfterMenu, AfterStyle);
	}

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
