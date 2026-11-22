using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace WinPieGestures;

public partial class SettingsWindow
{
    private FixedRingGeometry? _fixedGeometry;
    private (int Count, double Radius, double ThirdInner, bool Honeycomb, string Shape, double Inner, double Core, double Gap, double Corner, long Revision) _fixedGeometrySize;

    private FixedRingGeometry FixedGeometry(WheelProfile profile)
    {
        var cfg = ConfigManager.CurrentConfig;
        double unit = cfg.WheelRadius / 72;
        bool honeycomb = cfg.SubmenuStyle == "Fan";
        double secondaryExtent = honeycomb ? 0 : LegacyRingGeometry.SecondaryExtent(cfg, cfg.WheelRadius, cfg.InnerRadius);
        double start = FixedRingGeometry.GetThirdInnerRadius(cfg, secondaryExtent, unit, cfg.WheelRadius);
        var size = (profile.SectorCount, cfg.WheelRadius, start, honeycomb, cfg.Shape, cfg.InnerRadius, cfg.CoreRadius,
            cfg.SectorGap, cfg.SectorCornerRadius, ConfigManager.ConfigurationRevision);
        if (_fixedGeometry == null || _fixedGeometrySize != size)
        {
            _fixedGeometrySize = size;
            _fixedGeometry = new FixedRingGeometry(profile.SectorCount, cfg.WheelRadius, start, unit, honeycomb, cfg);
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
        int? ancestor = _fixedInnerLevel == null ? _fixedIndices[_fixedLevel] : null;
        if (_fixedInnerLevel is 1 or 2) path[1] = _selectedSlotIndex;
        if (_fixedInnerLevel == 2 && _selectedSubActionIndex is int subIndex) path[2] = FixedRingMenu.SecondaryKey(_selectedSlotIndex, subIndex);
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
        UpdateStudioFixedBreadcrumb(profile, menu, path);
        MappingsCurrentEditIndicator.Text = _fixedInnerLevel == 0 ? I18n.T("FocusSlotCenterCoreTitle") : string.Join(" › ", path.OrderBy(p => p.Key)
            .Select(pair => FixedAction(profile, menu, pair.Key, pair.Value)?.Name is { Length: > 0 } name ? name : $"{pair.Key} · {pair.Value + 1:00}"));
        MappingsCurrentEditIndicator.SetResourceReference(TextBlock.ForegroundProperty, "AccentPrimaryBrush");

        for (int i = 0; i < profile.SectorCount; i++)
        {
            int primary = i;
            var (shape, label) = geometry.Primary(cfg, i);
            Place(1, i, i, shape, label, FixedAction(profile, menu, 1, i), () => SelectPrimarySlot(primary));
        }
        foreach (var (primary, child, root, action) in SecondaryParents(profile, includeVacant: true))
        {
            int count = root.SubActions.Count;
            var (shape, label) = geometry.Secondary(cfg, primary, child, count);
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
            double radius = geometry.CoreRadius(cfg.CoreRadius);
            var circle = new EllipseGeometry(new Point(), radius, radius);
            circle.Freeze();
            var center = new DeepCanvasSlotButton(circle, scale, assignedFill, _fixedInnerLevel == 0 ? accent : border, _fixedInnerLevel == 0 ? 3 : 1.5) { ToolTip = I18n.T("FocusSlotCenterCoreTitle") };
            RegisterFixedCanvasHit(profile, menu, circle, scale, 0, -1, profile.CenterAction);
            PlaceButton(center, circle, "FixedRingCenter", SelectCenterCore);
        }

        void Place(int level, int key, int number, Geometry shape, Point position, ActionItem? action, Action select)
        {
            if (shape.Bounds.IsEmpty) return;
            RegisterFixedCanvasHit(profile, menu, shape, scale, level, key, action);
            bool selected = _fixedInnerLevel is int inner ? level == inner && path.GetValueOrDefault(level, -1) == key : level == _fixedLevel && _fixedIndices[level] == key;
            bool onPath = path.TryGetValue(level, out int member) && member == key;
            bool assigned = WheelProfile.IsActionConfigured(action);
            string name = assigned && !string.IsNullOrWhiteSpace(action!.Name) ? action.Name : I18n.T("FixedEmpty");
            var button = new DeepCanvasSlotButton(shape, scale, assigned ? assignedFill : vacantFill,
                onPath ? accent : assigned ? muted : border, selected ? 3 : onPath ? 2 : 1)
            {
                ToolTip = I18n.TF("CanvasEditLevel", level, (number + 1).ToString("00")) + " · " + name,
                Opacity = level == (_fixedInnerLevel ?? _fixedLevel) || onPath ? 1 : .78
            };
            PlaceButton(button, shape, $"FixedRing{level}Slot{key}", select);
            double slotWidth = Math.Max(9, Math.Min(shape.Bounds.Width, shape.Bounds.Height) * scale * .8);
            var label = CanvasSlotLabel(assigned ? action : null, assigned ? name : (number + 1).ToString("00"),
                slotWidth, Math.Min(14, slotWidth * .55), Math.Clamp(slotWidth / 3.2, 8, 13),
                assigned ? text : muted, selected ? FontWeights.Bold : FontWeights.Normal);
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
                if (OnFixedCanvas && ReferenceEquals(layer.ActiveFixedRings, menu)) select();
            };
            Canvas.SetLeft(button, 150 + scale * shape.Bounds.Left);
            Canvas.SetTop(button, 150 + scale * shape.Bounds.Top);
            canvas.Children.Add(button);
        }
    }
}
