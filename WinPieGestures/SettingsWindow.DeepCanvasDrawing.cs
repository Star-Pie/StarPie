using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WinPieGestures;

public partial class SettingsWindow
{
	private static StackPanel CanvasSlotLabel(ActionItem? action, string caption, double width, double iconSize,
		double fontSize, Brush ink, FontWeight weight)
	{
		var config = ConfigManager.CurrentConfig;
		string mode = action?.LayoutMode is { Length: > 0 } and not "Inherit" ? action.LayoutMode : config.IconLayoutMode;
		var panel = new StackPanel { Width = width, IsHitTestVisible = false };
		var icon = action != null && mode != "TextOnly" ? CreateMappingsSubIconElement(action, ink, iconSize) : null;
		if (icon != null) panel.Children.Add(icon);
		if (icon == null || config.MappingsCanvasShowText)
		{
			var label = new TextBlock { Text = caption, Foreground = ink, FontSize = fontSize, FontWeight = weight,
				TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
			ConfigurePreviewTextElement(label);
			panel.Children.Add(label);
		}
		return panel;
	}

	private void MappingsPreviewViewport_SizeChanged(object sender, SizeChangedEventArgs e)
	{
		UpdateStudioWheelFit();
        if (_deepCanvasParent != null || _fixedLayer != null) RenderMappingsWheelPreview();
	}

	private void DrawDeepCanvasBranch(WheelProfile profile, IReadOnlyList<int> parentPath, ActionItem parent, int? selectedChild)
	{
		var canvas = MappingsWheelPreviewCanvas;
		canvas.Children.Clear();
		var cfg = ConfigManager.CurrentConfig;
		bool fan = cfg.SubmenuStyle == "Fan";
		int count = parent.SubActions?.Count ?? 0;
		double radius = cfg.WheelRadius, inner = cfg.InnerRadius;
		double second = cfg.SubWheelOuterRadius > 0 ? cfg.SubWheelOuterRadius : radius * 1.55;
		double sweep = 360.0 / profile.SectorCount;
		double rootAngle = parentPath[0] * sweep;
		// Keep the reference wheel and the branch in a single coordinate system, including when zoomed.
		double viewWidth = MappingsPreviewViewportContainer.ActualWidth;
		double viewHeight = MappingsPreviewViewportContainer.ActualHeight;
		viewWidth = viewWidth > 0 ? viewWidth : 300;
		viewHeight = viewHeight > 0 ? viewHeight : 300;
		double footerY = 150 + viewHeight / 2 - 35;
		double extent = second + 4 * (Math.Max(32, second - radius) + 3) + radius + 200;
		var work = new Rect(-extent, -extent, 2 * extent, 2 * extent);
		DeepMenuFrame Frame(IReadOnlyList<int> path, int children) =>
			new(new Point(), new DpiScale(1, 1), work, cfg.GetEffectiveCoreDeadzone(), radius, second,
				RadialWindow.GetDeepDirection(profile, path, fan, LegacyRingGeometry.SecondaryShape(cfg)), path.Count + 1, children, fan,
				cfg.EnableOuterEscapeCancel ? cfg.OuterEscapeDistance : 0);

		var root = GetCanvasRoot(profile, parentPath[0])!;
		int outerCount = DeepMenuFrame.VisibleSlotCount(root.SubActions.Count, fan);
		var ancestorShapes = new List<Geometry>();
		if (fan)
		{
			double ratio = second / (radius * 1.55);
			double itemRadius = (radius - inner) * .4 * Math.Clamp(ratio, .5, 2.5);
			double orbit = (radius + inner) / 2 * ratio + (cfg.SubWheelInnerGap >= 0 ? cfg.SubWheelInnerGap : 4);
			var (u, v) = RadialWindow.GetFanSubOffsetForShape(LegacyRingGeometry.SecondaryShape(cfg), RadialWindow.GetFanSlotIndex(parentPath[1], outerCount));
			double rad = rootAngle * Math.PI / 180;
			double px = (Math.Cos(rad) * u - Math.Sin(rad) * v) * orbit;
			double py = (Math.Sin(rad) * u + Math.Cos(rad) * v) * orbit;
			ancestorShapes.Add(RadialWindow.CreateSubMenuGeometry(LegacyRingGeometry.SecondaryShape(cfg), px, py, itemRadius, rad, 0, 0, cfg.SubWheelCornerRadius));
		}
		else
		{
			double step = sweep / outerCount;
			double start = rootAngle - sweep / 2 + parentPath[1] * step;
			double gap = cfg.SubWheelInnerGap >= 0 ? cfg.SubWheelInnerGap : cfg.SectorGap;
			ancestorShapes.Add(IconHelper.CreateAdvancedSectorGeometry(0, 0, start, start + step, radius + gap + 2,
				second, LegacyRingGeometry.SecondaryShape(cfg), gap, cfg.SubWheelCornerRadius >= 0 ? cfg.SubWheelCornerRadius : cfg.SectorCornerRadius));
		}
		var prefix = new List<int> { parentPath[0], parentPath[1] };
		var ancestor = root.SubActions[parentPath[1]];
		for (int d = 2; d < parentPath.Count; d++)
		{
			int index = parentPath[d];
			ancestorShapes.Add(Frame(prefix, ancestor.SubActions.Count).Slots[index].Shape);
			prefix.Add(index);
			ancestor = ancestor.SubActions[index];
		}
		var frame = count > 0 ? Frame(parentPath, count) : null;
		var vacantShape = count == 0 ? Frame(parentPath, 1).Slots[0].Shape : null;
		var bounds = new Rect(-radius, -radius, 2 * radius, 2 * radius);
		if (vacantShape != null) bounds.Union(vacantShape.Bounds);
		foreach (var shape in ancestorShapes) bounds.Union(shape.Bounds);
		if (frame != null) foreach (var slot in frame.Slots) bounds.Union(slot.Shape.Bounds);
		double availableHeight = Math.Max(1, viewHeight - (count == 0 ? 105 : 78));
		double scale = Math.Min(1.7, Math.Min(Math.Max(1, viewWidth - 24) / bounds.Width, availableHeight / bounds.Height));
		double x = 150 - (bounds.Left + bounds.Width / 2) * scale;
		double y = 150 - viewHeight / 2 + 34 + availableHeight / 2 - (bounds.Top + bounds.Height / 2) * scale;

		bool independent = StyleRendererFactory.UsesIndependentSubAppearance(cfg);
		string style = independent && cfg.SubWheelUiStyle is { Length: > 0 } and not "FollowPrimary" ? cfg.SubWheelUiStyle : cfg.UiStyle;
		string theme = independent && cfg.SubWheelTheme is { Length: > 0 } and not "FollowPrimary" ? cfg.SubWheelTheme : cfg.Theme;
		var renderer = StyleRendererFactory.CreateRenderer(style, cfg, independent);
		renderer.Initialize(theme, cfg);
		bool custom = independent && theme == "Custom";
		Brush fill = CreateBrushFromHexSafe(custom ? cfg.SubWheelCustomSectorBg : null, renderer.DefaultSectorBrush);
		Brush border = CreateBrushFromHexSafe(custom ? cfg.SubWheelCustomSectorBorder : null, renderer.SectorBorderBrush);
		Brush active = CreateBrushFromHexSafe(custom ? cfg.SubWheelCustomHighlightBg : null, renderer.HighlightSectorBrush);
		Brush activeBorder = CreateBrushFromHexSafe(custom ? cfg.SubWheelCustomHighlightBorder : null, renderer.HighlightBorderBrush);
		Brush text = CreateBrushFromHexSafe(custom ? cfg.SubWheelCustomText : null, renderer.TextColorBrush);
		var directions = ResolveDirectionNames(profile.SectorCount);
		Geometry OnCanvas(Geometry shape)
		{
			var transformed = new GeometryGroup { Children = { shape }, Transform = new MatrixTransform(scale, 0, 0, scale, x, y) };
			transformed.Freeze();
			return transformed;
		}
		if (cfg.ShowCoreCircle)
		{
			var core = new EllipseGeometry(new Point(), cfg.CoreRadius, cfg.CoreRadius);
			PlaceSlot(core, "DeepCanvasCenter", I18n.T("CenterCoreTitle"), false, SelectCenterCore);
			RegisterPaletteTreeHit(OnCanvas(core), [-1]);
		}
		for (int i = 0; i < profile.SectorCount; i++)
		{
			int index = i;
			double angle = i * sweep;
			var shape = IconHelper.CreateAdvancedSectorGeometry(0, 0, angle - sweep / 2, angle + sweep / 2,
				inner, radius, cfg.Shape, cfg.SectorGap, cfg.SectorCornerRadius);
			var action = GetCanvasRoot(profile, i);
			bool current = index == parentPath[0];
			string name = !string.IsNullOrWhiteSpace(action?.Name) ? action.Name : directions[i];
			var button = PlaceSlot(shape, "DeepCanvasRoot" + index, name, current, () => SelectPrimarySlot(index));
			RegisterPaletteTreeHit(OnCanvas(shape), [index]);
			button.Opacity = current ? .80 : .42;
			double mid = (inner + radius) * .5;
			var center = new Point(mid * Math.Cos(angle * Math.PI / 180), mid * Math.Sin(angle * Math.PI / 180));
			double labelWidth = Math.Max(10, Math.Min((radius - inner) * scale, 2 * mid * Math.Sin(sweep * Math.PI / 360) * scale) * .85);
			var marker = CanvasSlotLabel(action, name, labelWidth, Math.Clamp((radius - inner) * scale / 3, 8, 16),
				10, text, current ? FontWeights.Bold : FontWeights.Normal);
			marker.Opacity = current ? 1 : .64;
			marker.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
			Canvas.SetLeft(marker, x + center.X * scale - marker.DesiredSize.Width / 2);
			Canvas.SetTop(marker, y + center.Y * scale - marker.DesiredSize.Height / 2);
			canvas.Children.Add(marker);
		}
		// Selected ancestors retain their outlines behind the active tier. Paged tiers may overlap.
		var ancestorItem = root;
		for (int i = 0; i < ancestorShapes.Count; i++)
		{
			int depth = i + 2;
			ancestorItem = ancestorItem.SubActions[parentPath[depth - 1]];
			var shape = ancestorShapes[i];
			var slot = PlaceSlot(shape, "DeepCanvasAncestor" + depth, ancestorItem.Name, false, () => ShowDeepAncestor(depth));
			RegisterPaletteTreeHit(OnCanvas(shape), parentPath.Take(depth).ToArray());
			slot.Opacity = .68;
			double width = shape.Bounds.Width * scale * .82;
			if (width < 20) continue;
			var label = CanvasSlotLabel(ancestorItem, ancestorItem.Name, width, 12, 10, text, FontWeights.Normal);
			label.Measure(new Size(width, double.PositiveInfinity));
			Canvas.SetLeft(label, x + (shape.Bounds.Left + shape.Bounds.Width * .5) * scale - label.DesiredSize.Width * .5);
			Canvas.SetTop(label, y + (shape.Bounds.Top + shape.Bounds.Height * .5) * scale - label.DesiredSize.Height * .5);
			canvas.Children.Add(label);
		}
		if (frame != null)
		{
			for (int i = 0; i < frame.Slots.Count; i++)
			{
				int index = i;
				var slot = frame.Slots[i];
				var item = parent.SubActions![i];
				string name = !string.IsNullOrWhiteSpace(item?.Name) ? item.Name : I18n.TF("EditSubActionNumber", i + 1);
				bool selected = selectedChild == i;
				bool branch = item?.SubActions?.Count > 0 && parentPath.Count + 1 < _deepCanvasLayer!.EffectiveMaxMenuDepth;
				var button = PlaceSlot(slot.Shape, "DeepCanvasChild" + i, name, selected, () => SelectDeepCanvasChild(index));
				RegisterPaletteTreeHit(OnCanvas(slot.Shape), [..parentPath, index]);
				if (branch) button.ToolTip = name + "  ›";
				Brush ink = CreateBrushFromHexSafe(item?.CustomTextColor, text);
				double totalWidth = Math.Max(1, slot.LabelWidth * scale);
				double markerWidth = branch ? Math.Min(9, totalWidth * .25) : 0;
				double width = totalWidth - markerWidth;
				var label = CanvasSlotLabel(item, name, width, Math.Min(width, 15),
					Math.Clamp((item?.CustomFontSize ?? cfg.SubWheelFontSize) * scale, 9, 16), ink,
					selected ? FontWeights.SemiBold : FontWeights.Normal);
				label.Measure(new Size(width, double.PositiveInfinity));
				double labelLeft = x + slot.Label.X * scale - totalWidth / 2;
				double labelCenterY = y + slot.Label.Y * scale;
				Canvas.SetLeft(label, labelLeft);
				Canvas.SetTop(label, labelCenterY - label.DesiredSize.Height / 2);
				canvas.Children.Add(label);
				if (branch)
				{
					var chevron = new TextBlock { Text = "›", FontSize = 13, Foreground = ink, IsHitTestVisible = false };
					Canvas.SetLeft(chevron, labelLeft + width);
					Canvas.SetTop(chevron, labelCenterY - 9);
					canvas.Children.Add(chevron);
				}
			}
		}
		else
		{
			if (vacantShape != null) RegisterPaletteTreeHit(OnCanvas(vacantShape), [..parentPath, 0], vacant: true);
			var empty = new TextBlock { Text = I18n.T("MenuEmpty"), Width = viewWidth, TextAlignment = TextAlignment.Center,
				IsHitTestVisible = false };
			empty.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
			ConfigurePreviewTextElement(empty);
			Canvas.SetLeft(empty, 150 - viewWidth / 2);
			Canvas.SetTop(empty, footerY - 23);
			canvas.Children.Add(empty);
		}

		DeepCanvasSlotButton PlaceSlot(Geometry shape, string id, string name, bool selected, Action click)
		{
			var button = new DeepCanvasSlotButton(shape, scale, selected ? active : fill,
				selected ? activeBorder : border, selected ? 2.8 : 1.2) { ToolTip = name };
			AutomationProperties.SetAutomationId(button, id);
			AutomationProperties.SetName(button, name);
			button.Click += (_, e) => { e.Handled = true; click(); };
			Canvas.SetLeft(button, x + shape.Bounds.Left * scale);
			Canvas.SetTop(button, y + shape.Bounds.Top * scale);
			canvas.Children.Add(button);
			return button;
		}

		var back = MakeControl("DeepCanvasBack", "‹  " + I18n.T("DeepMenuBack"), LeaveDeepCanvasBranch);
		var add = count < (fan ? RadialWindow.FanSubmenuSlotCount : 4)
			? MakeControl("DeepCanvasAdd", "+  " + I18n.T("MenuAddChild"), AddDeepCanvasChild) : null;
		double left = 150 - (back.DesiredSize.Width + (add == null ? 0 : 8 + add.DesiredSize.Width)) / 2;
		Canvas.SetLeft(back, left);
		if (add != null)
		{
			double addLeft = left + back.DesiredSize.Width + 8;
			Canvas.SetLeft(add, addLeft);
			RegisterPaletteTreeHit(new RectangleGeometry(new Rect(new Point(addLeft, footerY), add.DesiredSize)),
				[..parentPath, count], vacant: true);
		}

		Button MakeControl(string id, string caption, Action click)
		{
			var button = new Button { Content = caption, Padding = new Thickness(9, 2, 9, 2), Height = 27, Cursor = Cursors.Hand };
			button.SetResourceReference(StyleProperty, "ModernButtonStyle");
			AutomationProperties.SetAutomationId(button, id);
			button.Click += (_, e) => { e.Handled = true; click(); };
			canvas.Children.Add(button);
			button.Measure(new Size(double.PositiveInfinity, 27));
			Canvas.SetTop(button, footerY);
			return button;
		}
	}

	// Keep native Button keyboard/UIA activation while confining pointer hits to the painted slot.
	private sealed class DeepCanvasSlotButton : Button
	{
		private readonly Geometry _shape;
		private readonly System.Windows.Media.Pen _outline;

		internal DeepCanvasSlotButton(Geometry source, double scale, Brush fill, Brush stroke, double thickness)
		{
			var box = source.Bounds;
			_shape = new GeometryGroup
			{
				Children = { source }, Transform = new MatrixTransform(scale, 0, 0, scale, -box.Left * scale, -box.Top * scale)
			};
			_shape.Freeze();
			Width = box.Width * scale;
			Height = box.Height * scale;
			Template = null;
			Clip = _shape;
			Cursor = Cursors.Hand;
			Background = fill;
			_outline = new System.Windows.Media.Pen(stroke, thickness * 2);
			if (_outline.CanFreeze) _outline.Freeze();
		}

		protected override void OnRender(DrawingContext dc) => dc.DrawGeometry(Background, _outline, _shape);

		protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
		{
			if (IsMouseCaptured && !_shape.FillContains(e.GetPosition(this))) ReleaseMouseCapture();
			base.OnMouseLeftButtonUp(e);
		}
	}
}
