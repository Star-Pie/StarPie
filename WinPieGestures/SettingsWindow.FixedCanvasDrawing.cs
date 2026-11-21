using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace WinPieGestures;

public partial class SettingsWindow
{
    private FixedRingGeometry? _fixedGeometry;
    private (int Count, double Radius, double ThirdInner, bool Honeycomb) _fixedGeometrySize;

    private FixedRingGeometry FixedGeometry(WheelProfile profile)
    {
        var cfg = ConfigManager.CurrentConfig;
        double unit = cfg.WheelRadius / 72;
        double start = LegacyRingGeometry.SecondaryExtent(cfg, cfg.WheelRadius, cfg.InnerRadius) + 5 * unit;
        bool honeycomb = cfg.SubmenuStyle == "Fan";
        var size = (profile.SectorCount, cfg.WheelRadius, start, honeycomb);
        if (_fixedGeometry == null || _fixedGeometrySize != size)
        {
            _fixedGeometrySize = size;
            _fixedGeometry = new FixedRingGeometry(profile.SectorCount, cfg.WheelRadius, start, unit, honeycomb);
        }
        return _fixedGeometry;
    }

    private void DrawFixedWheel(WheelProfile profile, WheelLayer layer, FixedRingMenu menu)
    {
        var canvas = MappingsWheelPreviewCanvas;
        var cfg = ConfigManager.CurrentConfig;
        var geometry = FixedGeometry(profile);
        int depth = menu.AlwaysVisible ? layer.EffectiveMaxMenuDepth : _fixedLevel;
        double width = MappingsPreviewViewportContainer.ActualWidth;
        double height = MappingsPreviewViewportContainer.ActualHeight;
        double fit = Math.Max(24, Math.Min(width > 0 ? width - 16 : 284, height > 0 ? height - 60 : 240));
        double scale = fit / (2 * geometry.OuterForCount(depth, menu.GetCount(depth)));
        var text = (Brush)FindResource("TextPrimaryBrush");
        var muted = (Brush)FindResource("TextSecondaryBrush");
        var accent = (Brush)FindResource("AccentPrimaryBrush");
        var border = (Brush)FindResource("CardBorderBrush");
        var vacantFill = (Brush)FindResource("CardBackgroundBrush");
        var assignedFill = (Brush)FindResource("SubtleCardBrush");

        var path = new Dictionary<int, int>();
        int? ancestor = _fixedIndices[_fixedLevel];
        for (int d = _fixedLevel; d >= 3 && ancestor is int i; d--)
        {
            path[d] = i;
            ancestor = menu.GetSlot(d, i)?.ParentIndex;
        }
        if (ancestor is int key && FixedRingMenu.TryDecodeSecondaryKey(key, out int parent, out _))
        {
            path[2] = key;
            path[1] = parent;
        }
        MappingsCurrentEditIndicator.Text = string.Join(" › ", path.OrderBy(p => p.Key)
            .Select(pair => FixedAction(profile, menu, pair.Key, pair.Value)?.Name is { Length: > 0 } name ? name : $"{pair.Key} · {pair.Value + 1:00}"));
        MappingsCurrentEditIndicator.SetResourceReference(TextBlock.ForegroundProperty, "AccentPrimaryBrush");

        for (int i = 0; i < profile.SectorCount; i++)
        {
            int primary = i;
            var (shape, label) = LegacyRingGeometry.Primary(cfg, profile.SectorCount, i, cfg.WheelRadius, cfg.InnerRadius);
            Place(1, i, i, shape, label, FixedAction(profile, menu, 1, i), () => SelectPrimarySlot(primary));
        }
        foreach (var (primary, child, root, action) in SecondaryParents(profile, includeVacant: true))
        {
            int count = root.SubActions.Count;
            var (shape, label) = LegacyRingGeometry.Secondary(cfg, profile.SectorCount, primary, child, count, cfg.WheelRadius, cfg.InnerRadius);
            int key2 = FixedRingMenu.SecondaryKey(primary, child);
            Place(2, key2, child, shape, label, action, () => SelectSubAction(primary, child));
        }
        for (int d = 3; d <= depth; d++)
        {
            int count = menu.GetCount(d);
            for (int i = 0; i < count; i++)
            {
                int level = d, index = i;
                var shape = geometry.GetShape(d, i, count);
                var pos = geometry.GetCenter(d, i, count);
                Place(d, i, i, shape, pos, menu.GetSlot(d, i)?.Action, () => SelectFixedSlot(level, index));
            }
        }
        if (cfg.ShowCoreCircle)
        {
            var circle = new EllipseGeometry(new Point(), cfg.CoreRadius, cfg.CoreRadius);
            circle.Freeze();
            var center = new DeepCanvasSlotButton(circle, scale, assignedFill, border, 1.5) { ToolTip = I18n.T("FocusSlotCenterCoreTitle") };
            RegisterFixedCanvasHit(profile, menu, circle, scale, 0, -1, profile.CenterAction);
            PlaceButton(center, circle, "FixedRingCenter", SelectCenterCore);
        }

        void Place(int level, int key, int number, Geometry shape, Point position, ActionItem? action, Action select)
        {
            if (shape.Bounds.IsEmpty) return;
            RegisterFixedCanvasHit(profile, menu, shape, scale, level, key, action);
            bool selected = level == _fixedLevel && _fixedIndices[level] == key;
            bool onPath = path.TryGetValue(level, out int member) && member == key;
            string name = string.IsNullOrWhiteSpace(action?.Name) ? I18n.T("FixedEmpty") : action.Name;
            var button = new DeepCanvasSlotButton(shape, scale, action == null ? vacantFill : assignedFill,
                onPath ? accent : action == null ? border : muted, selected ? 3 : onPath ? 2 : 1)
            {
                ToolTip = I18n.TF("CanvasEditLevel", level, (number + 1).ToString("00")) + " · " + name,
                Opacity = level == _fixedLevel || onPath ? 1 : .78
            };
            PlaceButton(button, shape, $"FixedRing{level}Slot{key}", select);
            double slotWidth = Math.Max(9, Math.Min(shape.Bounds.Width, shape.Bounds.Height) * scale * .8);
            var icon = action != null && action.LayoutMode != "TextOnly" ? CreateMappingsSubIconElement(action, text, Math.Min(14, slotWidth * .55)) : null;
            var label = new StackPanel { Width = slotWidth, IsHitTestVisible = false };
            if (icon != null) label.Children.Add(icon);
            if (icon == null || cfg.MappingsCanvasShowText)
                label.Children.Add(new TextBlock
                {
                    Text = action == null ? (number + 1).ToString("00") : name,
                    FontSize = Math.Clamp(slotWidth / 3.2, 8, 13), Foreground = action == null ? muted : text,
                    FontWeight = selected ? FontWeights.Bold : FontWeights.Normal,
                    TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
                });
            label.Measure(new Size(slotWidth, double.PositiveInfinity));
            Canvas.SetLeft(label, 150 + position.X * scale - slotWidth / 2);
            Canvas.SetTop(label, 150 + position.Y * scale - label.DesiredSize.Height / 2);
            canvas.Children.Add(label);
        }

        void PlaceButton(Button button, Geometry shape, string id, Action select)
        {
            AutomationProperties.SetAutomationId(button, id);
            AutomationProperties.SetName(button, button.ToolTip.ToString() ?? id);
            button.Click += (_, e) =>
            {
                e.Handled = true;
                if (EditingOuterFixedSlot && ReferenceEquals(layer.ActiveFixedRings, menu)) select();
            };
            Canvas.SetLeft(button, 150 + scale * shape.Bounds.Left);
            Canvas.SetTop(button, 150 + scale * shape.Bounds.Top);
            canvas.Children.Add(button);
        }
    }
}
