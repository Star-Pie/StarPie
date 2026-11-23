using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace WinPieGestures;

public class CanvasHitItem
{
	public Geometry Geometry { get; set; } = null!;
	public ActionAddress Address { get; set; }
	public Rect Bounds { get; set; }
	public bool IsCenter { get; set; }
	public bool IsSubAction { get; set; }
	public int SlotIndex { get; set; }
	public int? SubIndex { get; set; }
	public ActionItem? Action { get; set; }
	public int? FixedLevel { get; init; }
	internal FixedRingMenu? FixedMenu { get; init; }
	internal bool IsDropOnly { get; init; }
}

public partial class SettingsWindow
{
	private readonly List<CanvasHitItem> _canvasHitItems = new();
	private System.Windows.Shapes.Path? _dropHighlightOverlay;
	private System.Windows.Shapes.Line? _dropInsertionLineOverlay;
	private CanvasHitItem? _currentHoverDropTarget;
	private bool _isCtrlDragCopy = false;

	public void ClearCanvasHitItems()
	{
		_canvasHitItems.Clear();
		_paletteHits.Clear();
		_paletteRenderVersion++;
		if (_paletteOrigin != null) EndPaletteDrag();
	}

	public void RegisterCanvasHitItem(Geometry geometry, ActionAddress address, bool isCenter, bool isSubAction, int slotIndex, int? subIndex, ActionItem? action)
	{
		if (geometry == null) return;
		_canvasHitItems.Add(new CanvasHitItem
		{
			Geometry = geometry,
			Address = address,
			Bounds = geometry.Bounds,
			IsCenter = isCenter,
			IsSubAction = isSubAction,
			SlotIndex = slotIndex,
			SubIndex = subIndex,
			Action = action
		});
	}

	public CanvasHitItem? HitTestCanvasPoint(Point pt)
	{
		// 优先检测二级子动作（位于外环，几何位于顶层），再检测一级主扇区与中心核心
		for (int i = _canvasHitItems.Count - 1; i >= 0; i--)
		{
			var item = _canvasHitItems[i];
			if (item.IsSubAction && !item.IsDropOnly && item.Geometry.FillContains(pt))
			{
				return item;
			}
		}

		for (int i = _canvasHitItems.Count - 1; i >= 0; i--)
		{
			var item = _canvasHitItems[i];
			if (!item.IsSubAction && item.Geometry.FillContains(pt))
			{
				return item;
			}
		}

		if (_canvasDragSource?.FixedLevel is >= 1)
			return _canvasHitItems.LastOrDefault(item => item.IsDropOnly && item.Geometry.FillContains(pt));
		return null;
	}

	public void UpdateCanvasDragHover(Point currentPos, bool isCtrlDown)
	{
		_isCtrlDragCopy = isCtrlDown;
		CanvasHitItem? target = HitTestCanvasPoint(currentPos);
		string? operation = target == null ? null : CanvasDropOperation(target, isCtrlDown);
		if (operation == null) target = null;
		_currentHoverDropTarget = target;

		if (target == null)
		{
			HideCanvasDragOverlays();
			if (MappingsCurrentEditIndicator != null && _isDraggingSlot)
			{
				MappingsCurrentEditIndicator.Text = I18n.T("EditDragCancel");
				MappingsCurrentEditIndicator.Foreground = new SolidColorBrush(Color.FromRgb(245, 158, 11));
			}
			return;
		}

		ShowCanvasDropHighlight(target, isCtrlDown);

		if (MappingsCurrentEditIndicator != null)
		{
			string modePrefix = I18n.T(operation!);
			string targetDesc = target.FixedLevel is >= 3
				? I18n.TF("CanvasEditLevel", target.FixedLevel, (target.SlotIndex + 1).ToString("00"))
				: target.IsCenter
				? I18n.T("CenterCoreTitle")
				: (target.IsSubAction ? I18n.TF("EditSubActionTarget", target.SlotIndex + 1, target.SubIndex + 1) : I18n.TF("EditSectorNumber", target.SlotIndex + 1));

			MappingsCurrentEditIndicator.Text = I18n.TF("EditDragTarget", modePrefix, targetDesc);
			MappingsCurrentEditIndicator.Foreground = new SolidColorBrush(isCtrlDown ? Color.FromRgb(56, 189, 248) : Color.FromRgb(16, 185, 129));
		}
	}

	// Hover and commit validate the same operation. The painted source and target
	// also distinguish inserting a copy between siblings from overwriting a slot.
	private string? CanvasDropOperation(CanvasHitItem target, bool copy)
	{
		if (!CanDropOnFixedCanvas(target)) return null;
		if (_canvasDragSource is not { } source) return copy ? "EditDragCopy" : "StudioDragMove";
		if (!_canvasHitItems.Contains(source) || !_canvasHitItems.Contains(target) || ReferenceEquals(source, target)) return null;
		if (IsCrossLevelSwap(source, target))
			return copy ? (source.Action == null ? null : "EditDragCopy") :
				source.Action == null || target.Action == null ? "StudioDragMove" : "StudioDragSwap";
		if (target.FixedLevel is >= 3)
			return copy ? "EditDragCopy" : target.Action == null ? "StudioDragMove" : "StudioDragSwap";
		if (!source.IsSubAction)
		{
			if (target.IsSubAction || (target.IsCenter && (source.Action?.IsWheelPortal == true || source.Action?.SubActions?.Count > 0)) ||
				(source.IsCenter && target.Action?.IsWheelPortal == true)) return null;
			return copy ? "EditDragCopy" : WheelProfile.IsActionConfigured(target.Action) ? "StudioDragSwap" : "StudioDragMove";
		}
		if (target.IsCenter || (!target.IsSubAction && source.SlotIndex == target.SlotIndex)) return null;
		var root = target.IsSubAction
			? _canvasHitItems.FirstOrDefault(hit => !hit.IsSubAction && !hit.IsCenter && hit.FixedLevel is null or 1 &&
				hit.SlotIndex == target.SlotIndex)?.Action : target.Action;
		int count = root?.SubActions?.Count ?? 0;
		int limit = ConfigManager.CurrentConfig.SubmenuStyle == "Fan" ? RadialWindow.FanSubmenuSlotCount : 4;
		if ((copy || !target.IsSubAction) && count >= limit) return null;
		if (copy && target.IsSubAction && source.SlotIndex == target.SlotIndex) return "StudioDragInsert";
		return copy ? "EditDragCopy" : target.IsSubAction ? "StudioDragSwap" : "StudioDragMove";
	}

	private void ShowCanvasDropHighlight(CanvasHitItem target, bool isCtrlDown)
	{
		if (MappingsWheelPreviewCanvas == null) return;

		if (_dropHighlightOverlay == null || !MappingsWheelPreviewCanvas.Children.Contains(_dropHighlightOverlay))
		{
			_dropHighlightOverlay = new System.Windows.Shapes.Path
			{
				IsHitTestVisible = false
			};
			Panel.SetZIndex(_dropHighlightOverlay, 99);
			MappingsWheelPreviewCanvas.Children.Add(_dropHighlightOverlay);
		}

		_dropHighlightOverlay.Data = target.Geometry;
		Color highlightColor = isCtrlDown ? Color.FromRgb(56, 189, 248) : Color.FromRgb(16, 185, 129);
		_dropHighlightOverlay.Fill = new SolidColorBrush(Color.FromArgb(50, highlightColor.R, highlightColor.G, highlightColor.B));
		_dropHighlightOverlay.Stroke = new SolidColorBrush(highlightColor);
		_dropHighlightOverlay.StrokeThickness = 2.4;
		_dropHighlightOverlay.StrokeDashArray = new DoubleCollection { 3, 2 };
		_dropHighlightOverlay.Effect = new DropShadowEffect
		{
			Color = highlightColor,
			BlurRadius = 10,
			ShadowDepth = 0,
			Opacity = 0.9
		};
		_dropHighlightOverlay.Visibility = Visibility.Visible;
	}

	public void HideCanvasDragOverlays()
	{
		if (_dropHighlightOverlay != null)
		{
			_dropHighlightOverlay.Visibility = Visibility.Collapsed;
		}
		if (_dropInsertionLineOverlay != null)
		{
			_dropInsertionLineOverlay.Visibility = Visibility.Collapsed;
		}
		_currentHoverDropTarget = null;
	}

	public void CancelCanvasDrag()
	{
		if (_isCommittingDrag) return;
		_isDraggingSlot = false;
		_dragSourceSlotIndex = -999;
		_canvasDragSource = null;
		_mappingsDragStartPos = null;
		_mappingsPanStartPoint = null;
		HideCanvasDragOverlays();
		if (MappingsPreviewViewportContainer != null && MappingsPreviewViewportContainer.IsMouseCaptured)
		{
			MappingsPreviewViewportContainer.ReleaseMouseCapture();
		}
		if (MappingsPreviewViewportContainer != null)
		{
			MappingsPreviewViewportContainer.Cursor = Cursors.Arrow;
		}
		if (OnFixedCanvas) RenderMappingsWheelPreview();
		else
		{
			UpdateActionBreadcrumb();
			UpdateFocusEditorUi();
		}
	}

	public void ProcessCanvasDrop(WheelProfile profile, CanvasHitItem target, bool isCtrlDown)
	{
		if (profile == null || profile.Actions == null || target == null) return;
		if (CanvasDropOperation(target, isCtrlDown) == null) return;
		if (target.FixedLevel != null && !ReferenceEquals(profile, _fixedProfile)) return;
		if (_canvasDragSource is { } source && IsCrossLevelSwap(source, target))
		{
			DropAcrossFixedLevels(profile, source, target, isCtrlDown);
			return;
		}
		if (target.FixedLevel is >= 3)
		{
			DropFixedSlot(profile, _canvasDragSource!, target, isCtrlDown);
			return;
		}

		int sourceRaw = _dragSourceSlotIndex;
		bool isSourceSub = sourceRaw <= -100;
		bool isTargetSub = target.IsSubAction && target.SubIndex.HasValue;

		bool isFan = string.Equals(ConfigManager.CurrentConfig?.SubmenuStyle, "Fan", StringComparison.OrdinalIgnoreCase);
		int maxAllowed = isFan ? 3 : 4;

		if (isSourceSub)
		{
			int raw = -sourceRaw - 100;
			int srcSlot = _canvasDragSource?.SlotIndex ?? raw / 100;
			int srcSubIdx = _canvasDragSource?.SubIndex ?? raw % 100;

			if (srcSlot < 0 || srcSlot >= profile.Actions.Count) return;
			if (target.IsCenter) return; // 二级动作不能拖放至中心核心

			ActionItem? effectiveSrcPrimary = profile.GetEffectiveAction(srcSlot);
			int srcCount = (profile.Actions[srcSlot].SubActions != null && profile.Actions[srcSlot].SubActions.Count > 0)
				? profile.Actions[srcSlot].SubActions.Count
				: (effectiveSrcPrimary?.SubActions?.Count ?? 0);
			if (srcSubIdx < 0 || srcSubIdx >= srcCount) return;

			if (isTargetSub)
			{
				int tgtSlot = target.SlotIndex;
				int tgtSubIdx = target.SubIndex!.Value;
				if (tgtSlot < 0 || tgtSlot >= profile.Actions.Count) return;

				ActionItem? effectiveTgtPrimary = profile.GetEffectiveAction(tgtSlot);
				int tgtCount = (profile.Actions[tgtSlot].SubActions != null && profile.Actions[tgtSlot].SubActions.Count > 0)
					? profile.Actions[tgtSlot].SubActions.Count
					: (effectiveTgtPrimary?.SubActions?.Count ?? 0);

				if (srcSlot == tgtSlot)
				{
					if (srcSubIdx == tgtSubIdx) return; // 同位置无变化，零修改
					if (isCtrlDown && tgtCount >= maxAllowed) return; // 满容量拒绝，零修改

					var before = CaptureSectorSnapshot(profile, srcSlot);
					EnsureLocalSlotForEdit(profile, srcSlot);
					ActionItem srcPrimary = profile.Actions[srcSlot];
					srcPrimary.SubActions ??= new List<ActionItem>();
					if (srcSubIdx >= srcPrimary.SubActions.Count) return;

					ActionItem srcSubItem = srcPrimary.SubActions[srcSubIdx];

					if (isCtrlDown)
					{
						ActionItem clonedSub = srcSubItem.Clone();
						clonedSub.IsInherited = false;
						if (tgtSubIdx >= 0 && tgtSubIdx <= srcPrimary.SubActions.Count)
						{
							srcPrimary.SubActions.Insert(tgtSubIdx, clonedSub);
						}
						else
						{
							srcPrimary.SubActions.Add(clonedSub);
						}
						_selectedSubActionIndex = tgtSubIdx;
					}
					else
					{
						if (tgtSubIdx >= 0 && tgtSubIdx < srcPrimary.SubActions.Count)
						{
							ActionItem temp = srcPrimary.SubActions[srcSubIdx];
							srcPrimary.SubActions[srcSubIdx] = srcPrimary.SubActions[tgtSubIdx];
							srcPrimary.SubActions[tgtSubIdx] = temp;
							_selectedSubActionIndex = tgtSubIdx;
						}
						else return;
					}

					var after = CaptureSectorSnapshot(profile, srcSlot);
					RecordSingleSectorTransaction(profile, srcSlot, isCtrlDown ? "EditCopyChild" : "EditSwapChildren", before, after);
				}
				else
				{
					if (isCtrlDown && tgtCount >= maxAllowed) return; // 满容量拒绝，零修改
					bool isAppendMove = !isCtrlDown && (tgtSubIdx < 0 || tgtSubIdx >= tgtCount);
					if (isAppendMove && tgtCount >= maxAllowed) return; // 满容量拒绝，零修改

					var srcBefore = CaptureSectorSnapshot(profile, srcSlot);
					var tgtBefore = CaptureSectorSnapshot(profile, tgtSlot);

					EnsureLocalSlotForEdit(profile, srcSlot);
					EnsureLocalSlotForEdit(profile, tgtSlot);
					ActionItem srcPrimary = profile.Actions[srcSlot];
					ActionItem tgtPrimary = profile.Actions[tgtSlot];
					srcPrimary.SubActions ??= new List<ActionItem>();
					tgtPrimary.SubActions ??= new List<ActionItem>();

					if (srcSubIdx >= srcPrimary.SubActions.Count) return;
					ActionItem srcSubItem = srcPrimary.SubActions[srcSubIdx];

					if (isCtrlDown)
					{
						ActionItem clonedSub = srcSubItem.Clone();
						clonedSub.IsInherited = false;
						if (tgtSubIdx >= 0 && tgtSubIdx < tgtPrimary.SubActions.Count)
						{
							tgtPrimary.SubActions[tgtSubIdx] = clonedSub;
						}
						else
						{
							tgtPrimary.SubActions.Add(clonedSub);
						}
						_selectedSlotIndex = tgtSlot;
						_selectedSubActionIndex = tgtSubIdx;
					}
					else
					{
						if (tgtSubIdx >= 0 && tgtSubIdx < tgtPrimary.SubActions.Count)
						{
							ActionItem tgtSubItem = tgtPrimary.SubActions[tgtSubIdx];
							srcPrimary.SubActions[srcSubIdx] = tgtSubItem;
							tgtPrimary.SubActions[tgtSubIdx] = srcSubItem;
						}
						else
						{
							srcPrimary.SubActions.RemoveAt(srcSubIdx);
							tgtPrimary.SubActions.Add(srcSubItem);
						}
						_selectedSlotIndex = tgtSlot;
						_selectedSubActionIndex = tgtSubIdx;
					}

					var srcAfter = CaptureSectorSnapshot(profile, srcSlot);
					var tgtAfter = CaptureSectorSnapshot(profile, tgtSlot);
					RecordCrossSectorTransaction(isCtrlDown ? "EditCopyChildAcrossSectors" : "EditSwapChildrenAcrossSectors", srcSlot, srcBefore, srcAfter, tgtSlot, tgtBefore, tgtAfter);
				}
			}
			else
			{
				int tgtSlot = target.SlotIndex;
				if (tgtSlot < 0 || tgtSlot >= profile.Actions.Count) return;
				if (srcSlot == tgtSlot) return;

				ActionItem? effectiveTgtPrimary = profile.GetEffectiveAction(tgtSlot);
				int tgtCount = (profile.Actions[tgtSlot].SubActions != null && profile.Actions[tgtSlot].SubActions.Count > 0)
					? profile.Actions[tgtSlot].SubActions.Count
					: (effectiveTgtPrimary?.SubActions?.Count ?? 0);

				if (tgtCount >= maxAllowed) return; // 满容量拒绝，零修改

				var srcBefore = CaptureSectorSnapshot(profile, srcSlot);
				var tgtBefore = CaptureSectorSnapshot(profile, tgtSlot);

				EnsureLocalSlotForEdit(profile, srcSlot);
				EnsureLocalSlotForEdit(profile, tgtSlot);
				ActionItem srcPrimary = profile.Actions[srcSlot];
				ActionItem tgtPrimary = profile.Actions[tgtSlot];
				srcPrimary.SubActions ??= new List<ActionItem>();
				tgtPrimary.SubActions ??= new List<ActionItem>();

				if (srcSubIdx >= srcPrimary.SubActions.Count) return;
				ActionItem srcSubItem = srcPrimary.SubActions[srcSubIdx];

				if (isCtrlDown)
				{
					ActionItem clonedSub = srcSubItem.Clone();
					clonedSub.IsInherited = false;
					tgtPrimary.SubActions.Add(clonedSub);
				}
				else
				{
					srcPrimary.SubActions.RemoveAt(srcSubIdx);
					tgtPrimary.SubActions.Add(srcSubItem);
				}

				_selectedSlotIndex = tgtSlot;
				_selectedSubActionIndex = tgtPrimary.SubActions.Count - 1;

				var srcAfter = CaptureSectorSnapshot(profile, srcSlot);
				var tgtAfter = CaptureSectorSnapshot(profile, tgtSlot);
				RecordCrossSectorTransaction(isCtrlDown ? "EditCopyChildToSector" : "EditMoveChildToSector", srcSlot, srcBefore, srcAfter, tgtSlot, tgtBefore, tgtAfter);
			}
		}
		else
		{
			int srcSlot = sourceRaw;
			int tgtSlot = target.IsCenter ? -1 : target.SlotIndex;

			if (srcSlot == tgtSlot) return;

			if (srcSlot == -1 || tgtSlot == -1)
			{
				int sectorSlot = srcSlot == -1 ? tgtSlot : srcSlot;
				if (sectorSlot < 0 || sectorSlot >= profile.Actions.Count) return;

				ActionItem? effectiveSector = profile.GetEffectiveAction(sectorSlot);
				int sectorSubCount = (profile.Actions[sectorSlot].SubActions != null && profile.Actions[sectorSlot].SubActions.Count > 0)
					? profile.Actions[sectorSlot].SubActions.Count
					: (effectiveSector?.SubActions?.Count ?? 0);

				if (tgtSlot == -1 && sectorSubCount > 0)
				{
					AppMessageBox.Show(this, I18n.T("CenterSingleActionOnlyWarning"), "StarPie", MessageBoxButton.OK, MessageBoxImage.Warning);
					return;
				}

				var srcBefore = srcSlot == -1 ? CaptureCenterSnapshot(profile) : CaptureSectorSnapshot(profile, srcSlot);
				var tgtBefore = tgtSlot == -1 ? CaptureCenterSnapshot(profile) : CaptureSectorSnapshot(profile, tgtSlot);

				EnsureLocalSlotForEdit(profile, sectorSlot);

				ActionItem sectorAction = profile.Actions[sectorSlot];
				ActionItem centerAction = profile.GetEffectiveCenterAction() ?? profile.CenterAction ?? new ActionItem { Name = "", Type = "None" };

				if (isCtrlDown)
				{
					if (srcSlot == -1)
					{
						profile.Actions[sectorSlot] = centerAction.Clone();
						profile.Actions[sectorSlot].IsInherited = false;
						profile.Actions[sectorSlot].SubActions = sectorAction.SubActions ?? new List<ActionItem>();
					}
					else
					{
						profile.CenterAction = sectorAction.Clone();
						profile.CenterAction.SubActions = null;
						profile.CenterAction.IsInherited = false;
					}
				}
				else
				{
					var preservedSubs = sectorAction.SubActions;
					ActionItem newCenter = sectorAction.Clone();
					newCenter.SubActions = null;
					newCenter.IsInherited = false;

					ActionItem newSector = centerAction.Clone();
					newSector.SubActions = preservedSubs;
					newSector.IsInherited = false;

					profile.CenterAction = newCenter;
					profile.Actions[sectorSlot] = newSector;
				}

				profile.SyncActiveLayerFromRootProperties();

				_selectedSlotIndex = tgtSlot;
				_selectedSubActionIndex = null;

				var srcAfter = srcSlot == -1 ? CaptureCenterSnapshot(profile) : CaptureSectorSnapshot(profile, srcSlot);
				var tgtAfter = tgtSlot == -1 ? CaptureCenterSnapshot(profile) : CaptureSectorSnapshot(profile, tgtSlot);

				RecordCrossSectorTransaction(isCtrlDown ? "EditCopyToCenterOrSector" : "EditSwapCenterAndSector",
					srcSlot, srcBefore, srcAfter, tgtSlot, tgtBefore, tgtAfter);
			}
			else
			{
				if (srcSlot < 0 || srcSlot >= profile.Actions.Count || tgtSlot < 0 || tgtSlot >= profile.Actions.Count) return;

				var srcBefore = CaptureSectorSnapshot(profile, srcSlot);
				var tgtBefore = CaptureSectorSnapshot(profile, tgtSlot);

				EnsureLocalSlotForEdit(profile, srcSlot);
				EnsureLocalSlotForEdit(profile, tgtSlot);

				bool linkSubActions = ConfigManager.CurrentConfig?.LinkSubActionsWhenDragging ?? true;

				if (isCtrlDown)
				{
					ActionItem cloned = profile.Actions[srcSlot].Clone();
					cloned.IsInherited = false;
					if (!linkSubActions)
					{
						cloned.SubActions = profile.Actions[tgtSlot]?.SubActions;
					}
					profile.Actions[tgtSlot] = cloned;
				}
				else
				{
					ActionItem srcAction = profile.Actions[srcSlot];
					ActionItem tgtAction = profile.Actions[tgtSlot];
					if (!linkSubActions)
					{
						var srcSubs = srcAction.SubActions;
						var tgtSubs = tgtAction.SubActions;
						srcAction.SubActions = tgtSubs;
						tgtAction.SubActions = srcSubs;
					}
					profile.Actions[srcSlot] = tgtAction;
					profile.Actions[tgtSlot] = srcAction;
				}

				_selectedSlotIndex = tgtSlot;
				_selectedSubActionIndex = null;

				var srcAfter = CaptureSectorSnapshot(profile, srcSlot);
				var tgtAfter = CaptureSectorSnapshot(profile, tgtSlot);

				RecordCrossSectorTransaction(isCtrlDown ? "EditCopySector" : "EditSwapSectors", srcSlot, srcBefore, srcAfter, tgtSlot, tgtBefore, tgtAfter);
			}
		}

		UpdateFocusEditorUi();
		RefreshSlots();
		RefreshFocusSubActionsListAndChips();
		RenderMappingsWheelPreview();
		ScheduleAutoSave();
	}
}
