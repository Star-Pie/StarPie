using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Pen = System.Windows.Media.Pen;
using Matrix = System.Windows.Media.Matrix;
using FontStyles = System.Windows.FontStyles;
using FlowDirection = System.Windows.FlowDirection;

namespace WinPieGestures;

public partial class RadialWindow
{
    private DeepMenuOverlay? _deepOverlay;
    private readonly List<UIElement> _deepHidden = new();
    private double _deepWindowSize;
    private double NativeCanvasSize => Math.Max(_wheelCanvasSize, _deepWindowSize);

    internal DeepMenuFrame ShowDeepMenu(IReadOnlyList<int> parentPath, ActionItem parent, string breadcrumb)
    {
        Dispatcher.VerifyAccess();
        var config = ConfigManager.CurrentConfig;
        var screen = ScreenHelper.GetScreenContextAtPoint(ActualPhysicalCenter);
        var children = parent.InlineSubActions!;
        bool fan = config.SubmenuStyle == "Fan";
        int count = children.Count;
        double direction = GetDeepDirection(_profile, parentPath, fan, LegacyRingGeometry.SecondaryShape(config));
        double second = config.SubWheelOuterRadius > 0 ? config.SubWheelOuterRadius : _outerRadius * 1.55;
        var frame = new DeepMenuFrame(ActualPhysicalCenter, screen.DpiScale, screen.PhysicalWorkArea,
            config.GetEffectiveCoreDeadzone(), _outerRadius, second, direction, parentPath.Count + 1, count, fan,
            config.EnableOuterEscapeCancel ? config.OuterEscapeDistance : 0);

        InvalidatePinnedArrowSlots();
        RemoveDeepOverlay();
        // An edge page occupies the second ring's space; restore those visuals when returning to it.
        if (frame.IsPaged)
        {
            foreach (var item in _subSectorPaths) HideForDeepPage(item);
            foreach (var item in _subContentContainers) HideForDeepPage(item);
        }

        ResizeForDeepMenu(Math.Max(_wheelCanvasSize, 2 * frame.CanvasRadius));
        int remaining = Math.Max(0, (GestureController.CaptureReleaseLayer(_profile)?.EffectiveMaxMenuDepth ?? 2) - parentPath.Count - 1);
        _deepOverlay = new DeepMenuOverlay(this, frame, children, breadcrumb, remaining, fan, parentPath.Count + 1);
        Panel.SetZIndex(_deepOverlay, 2000);
        MainGrid.Children.Add(_deepOverlay);
        return frame;
    }

    internal static double GetDeepDirection(WheelProfile profile, IReadOnlyList<int> path, bool fan, string? shape)
    {
        double unit = 360.0 / Math.Max(1, profile.SectorCount);
        double angle = path[0] * unit;
        var node = profile.GetEffectiveAction(path[0]);
        for (int depth = 1; depth < path.Count && node?.InlineSubActions is { Count: > 0 } items; depth++)
        {
            int index = path[depth];
            int visible = DeepMenuFrame.VisibleSlotCount(items.Count, fan);
            if ((uint)index >= (uint)visible) break;
            if (depth == 1 && fan)
            {
                var (u, v) = GetFanSubOffsetForShape(shape, GetFanSlotIndex(index, visible));
                angle += Math.Atan2(v, u) * 180 / Math.PI;
            }
            else
            {
                double sweep = depth == 1 ? unit : DeepMenuFrame.BranchSweep(visible, fan);
                angle += ((index + .5) / visible - .5) * sweep;
            }
            node = items[index];
        }
        return angle;
    }

    private void HideForDeepPage(UIElement item)
    {
        if (item.Visibility != Visibility.Visible) return;
        _deepHidden.Add(item);
        item.Visibility = Visibility.Hidden;
    }

    private void ResizeForDeepMenu(double size)
    {
        if (NativeCanvasSize == size) return;
        _deepWindowSize = size;
        Width = Height = MainGrid.Width = MainGrid.Height = size;
        PositionWindowOnTargetMonitor();
    }

    private void RemoveDeepOverlay()
    {
        if (_deepOverlay != null) MainGrid.Children.Remove(_deepOverlay);
        _deepOverlay = null;
        foreach (var item in _deepHidden) item.Visibility = Visibility.Visible;
        _deepHidden.Clear();
    }

    internal void ClearDeepMenu()
    {
        if (_deepOverlay == null) return;
        InvalidatePinnedArrowSlots();
        RemoveDeepOverlay();
        ResizeForDeepMenu(_wheelCanvasSize);
        _deepWindowSize = 0;
    }

    internal void HighlightDeepMenu(int hit) => _deepOverlay?.Select(hit);

    private void DrawMenuLabel(DrawingContext dc, DeepMenuFrame.Slot slot, ActionItem item, bool entersBranch)
    {
        var cfg = ConfigManager.CurrentConfig;
        string mode = string.IsNullOrEmpty(item.LayoutMode) || item.LayoutMode == "Inherit" ? cfg.IconLayoutMode : item.LayoutMode;
        Brush ink = TryGetFrozenBrush(item.CustomTextColor) ?? _subTextColorBrush;
        double markerWidth = entersBranch || item.IsWheelPortal ? Math.Min(10, slot.LabelWidth * .25) : 0;
        double width = slot.LabelWidth - markerWidth;
        var center = new Point(slot.Label.X - markerWidth / 2, slot.Label.Y);
        Geometry? icon = null;
        ImageSource? image = null;
        if (mode != "TextOnly")
        {
            var source = IconHelper.GetCachedActionIcon(item);
            image = source.Image;
            var path = source.SvgPath;
            if (image == null && string.IsNullOrEmpty(path))
            {
                path = item.Type is "WebUrl" or "Url" ? IconHelper.GetSvgPathByKey("ShowDesktop") :
                    GetVectorIconPath(item.Type, item.Parameter);
            }
            if (!string.IsNullOrEmpty(path))
            {
                try { icon = ParseFrozenGeometry(path); }
                catch (FormatException) { /* Old configs can contain an invalid optional icon. */ }
            }
        }
        if (mode == "IconOnly" && image == null && (icon == null || icon.Bounds.IsEmpty || icon.Bounds.Width <= 0 || icon.Bounds.Height <= 0) &&
            IconHelper.IconOnlyFallback(item) is { } fallback)
            icon = ParseFrozenGeometry(fallback);
        bool hasVector = icon != null && !icon.Bounds.IsEmpty && icon.Bounds.Width > 0 && icon.Bounds.Height > 0;
        bool hasIcon = image != null || hasVector;
        bool text = mode != "IconOnly";
        double size = Math.Min(width, text ? 13 : 21);
        double iconY = center.Y - (text ? 6 : 0);
        if (image != null && image.Width > 0 && image.Height > 0)
        {
            double scale = size / Math.Max(image.Width, image.Height);
            var drawSize = new Size(image.Width * scale, image.Height * scale);
            dc.DrawImage(image, new Rect(new Point(center.X - drawSize.Width / 2, iconY - drawSize.Height / 2), drawSize));
        }
        else if (hasVector)
        {
            var b = icon!.Bounds;
            var matrix = Matrix.Identity;
            matrix.Translate(-b.X - b.Width / 2, -b.Y - b.Height / 2);
            double scale = size / Math.Max(b.Width, b.Height);
            matrix.Scale(scale, scale);
            matrix.Translate(center.X, iconY);
            dc.PushTransform(new MatrixTransform(matrix));
            dc.DrawGeometry(ink, null, icon);
            dc.Pop();
        }
        if (text)
        {
            var name = item.Name ?? "";
            DrawMenuText(dc, name, new Point(center.X, center.Y + (hasIcon ? 7 : 0)), width,
                Math.Clamp(item.CustomFontSize ?? cfg.SubWheelFontSize, 10, 16), ink);
        }
        if (entersBranch || item.IsWheelPortal)
        {
            var marker = new Point(slot.Label.X + (slot.LabelWidth - markerWidth) / 2, center.Y);
            DrawMenuText(dc, item.IsWheelPortal ? "◎" : "›", marker, markerWidth, 12, ink);
        }
    }

    private void DrawMenuText(DrawingContext dc, string value, Point center, double width, double fontSize, Brush ink)
    {
        var text = new FormattedText(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            fontSize, ink, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = Math.Max(1, width), MaxLineCount = 1,
            TextAlignment = TextAlignment.Center, Trimming = TextTrimming.CharacterEllipsis
        };
        dc.DrawText(text, new Point(center.X - text.MaxTextWidth / 2, center.Y - text.Height / 2));
    }

    private sealed class DeepMenuOverlay : FrameworkElement
    {
        private readonly RadialWindow _wheel;
        private readonly DeepMenuFrame _frame;
        private readonly int _level;
        private readonly DrawingGroup?[] _labels;
        private readonly DrawingGroup _backText;
        private readonly Pen _border, _activeBorder;
        private int _highlight = DeepMenuFrame.None;

        internal bool ArrowFramePresented { get; private set; }
        internal PinnedArrowAddress? ArrowPointerSelection => _highlight >= 0 ? new(_level, _highlight) : null;

        internal DeepMenuOverlay(RadialWindow wheel, DeepMenuFrame frame, IReadOnlyList<ActionItem> actions,
            string breadcrumb, int remaining, bool fan, int level)
        {
            _wheel = wheel;
            _frame = frame;
            _level = level;
            IsHitTestVisible = false;
            _border = new Pen(wheel._subSectorBorderBrush, Math.Max(1, wheel._borderThickness));
            _activeBorder = new Pen(wheel._subHighlightBorderBrush, Math.Max(1.5, wheel._borderThickness));
            _labels = new DrawingGroup?[frame.Slots.Count];
            for (int i = 0; i < _labels.Length; i++)
            {
                var item = actions[i];
                if (!FixedRingMenu.IsLegacySlotVisible(item, remaining, fan)) continue;
                var slot = frame.Slots[i];
                var drawing = new DrawingGroup();
                using (var dc = drawing.Open())
                {
                    if (item != null) wheel.DrawMenuLabel(dc, slot, item,
                        remaining > 0 && item.InlineSubActions is { Count: > 0 } && WheelProfile.IsActionConfigured(item));
                }
                if (drawing.CanFreeze) drawing.Freeze();
                _labels[i] = drawing;
            }
            _backText = new DrawingGroup();
            using (var dc = _backText.Open())
            {
                var box = frame.BackBounds;
                var center = new Point(box.Left + box.Width / 2, box.Top + box.Height / 2);
                wheel.DrawMenuText(dc, "‹  " + I18n.T("DeepMenuBack"), center with { Y = center.Y - 7 }, box.Width - 8, 12, wheel._subTextColorBrush);
                wheel.DrawMenuText(dc, breadcrumb, center with { Y = center.Y + 8 }, box.Width - 8, 10, wheel._subTextColorBrush);
            }
            if (_backText.CanFreeze) _backText.Freeze();
        }

        internal void Select(int hit)
        {
            if (_highlight == hit) return;
            _highlight = hit;
            InvalidateVisual();
        }

        internal void AppendPinnedArrowSlots(List<PinnedArrowSlot> slots)
        {
            for (int i = 0; i < _labels.Length; i++)
            {
                if (_labels[i] == null || _frame.Slots[i].Shape.IsEmpty()) continue;
                slots.Add(new(new(_level, i), _frame.Slots[i].Label));
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.PushTransform(new TranslateTransform(ActualWidth / 2, ActualHeight / 2));
            for (int i = 0; i < _labels.Length; i++)
            {
                if (_labels[i] is not { } label) continue;
                bool active = _wheel.PaintsPinnedArrowSelection(new(_level, i), i == _highlight);
                dc.DrawGeometry(active ? _wheel._subHighlightSectorBrush : _wheel._subDefaultSectorBrush,
                    active ? _activeBorder : _border, _frame.Slots[i].Shape);
                dc.DrawDrawing(label);
            }
            bool back = _wheel._pinnedArrowSelection == null && _highlight == DeepMenuFrame.Back;
            dc.DrawRoundedRectangle(back ? _wheel._subHighlightSectorBrush : _wheel._subDefaultSectorBrush,
                back ? _activeBorder : _border, _frame.BackBounds, 7, 7);
            dc.DrawDrawing(_backText);
            dc.Pop();
            if (_wheel._pinnedArrowSlotsChanged != null && !ArrowFramePresented)
            {
                ArrowFramePresented = true;
                _wheel.QueuePinnedArrowRefresh();
            }
        }
    }
}
