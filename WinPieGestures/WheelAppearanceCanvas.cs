using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Pen = System.Windows.Media.Pen;

namespace WinPieGestures;

internal enum AppearanceDimension
{
    CoreRadius,
    OuterRadius,
    InnerRadius,
    SecondaryRadius,
    RingThickness,
    RingSpacing,
    HoneycombCellSize,
    HoneycombSpacing
}

internal sealed record AppearanceHandle(AppearanceDimension Dimension, double Value, double Minimum, double Maximum, string Label);

/// <summary>Settings-only geometry editor. All distances and pointer positions are in WPF DIPs.</summary>
public sealed class WheelAppearanceCanvas : FrameworkElement
{
    private static readonly DependencyProperty AccentProperty = Palette("Accent", Brushes.SteelBlue);
    private static readonly DependencyProperty InkProperty = Palette("Ink", Brushes.Black);
    private static readonly DependencyProperty MutedProperty = Palette("Muted", Brushes.DimGray);
    private static readonly DependencyProperty RimProperty = Palette("Rim", Brushes.LightGray);
    private static readonly DependencyProperty ActiveFillProperty = Palette("ActiveFill", Brushes.AliceBlue);
    private static readonly DependencyProperty FillProperty = Palette("Fill", Brushes.White);
    private static readonly Typeface CanvasTypeface = new("Segoe UI, Microsoft YaHei UI, Yu Gothic UI");
    private static readonly Point Origin = new(0, 0);
    private static readonly Vector HandleDirection = new(Math.Cos(-Math.PI / 7), Math.Sin(-Math.PI / 7));

    private readonly List<Cell> _cells = new();
    private readonly Dictionary<string, Geometry?> _icons = new(StringComparer.Ordinal);
    private readonly int?[] _branch = new int?[5];
    private WheelProfile? _profile;
    private FixedRingGeometry? _fixed;
    private int _depth, _selected;
    private string? _layout;
    private bool _runtime, _resident, _secondOnPopup, _canExpand;
    private string _iconLayout = "IconOnly";
    private double _zoom = 1, _referenceRadius, _sceneRadius, _coreRadius, _rootOuter, _rootInner, _secondaryOuter;
    private Point _center;
    private double _scale;
    private AppearanceHandle? _handle;
    private Guide? _guide;
    private Drag? _drag;

    internal event Action<int>? LevelSelected;
    internal event Action<double>? HandleChanged;
    internal event Action? HandleDragCompleted;

    private sealed record Cell(int Level, int Index, int? Parent, Geometry Shape, Point Center,
        ActionItem? Action, bool Configured, int Number = 0);
    private sealed record Guide(Point Start, Point End, double Gain);
    private sealed record Drag(Point Start, Vector Axis, double Value, double Scale, double Gain);

    public WheelAppearanceCanvas()
    {
        ClipToBounds = true;
        SetResourceReference(AccentProperty, "AccentPrimaryBrush");
        SetResourceReference(InkProperty, "TextPrimaryBrush");
        SetResourceReference(MutedProperty, "TextSecondaryBrush");
        SetResourceReference(RimProperty, "InputBorderBrush");
        SetResourceReference(ActiveFillProperty, "NavTabActiveBgBrush");
        SetResourceReference(FillProperty, "CardBackgroundBrush");
        UseLayoutRounding = false;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
    }

    private static DependencyProperty Palette(string name, Brush fallback) =>
        DependencyProperty.Register(name, typeof(Brush), typeof(WheelAppearanceCanvas),
            new FrameworkPropertyMetadata(fallback, FrameworkPropertyMetadataOptions.AffectsRender));

    internal void Render(AppConfig config, WheelProfile? profile, int depth, int selectedLevel,
        bool runtimePreview, double zoom, AppearanceHandle? handle)
    {
        depth = Math.Clamp(depth, 1, 5);
        if (!ReferenceEquals(_profile, profile) || _depth != depth || _layout != config.SubmenuStyle)
        {
            _referenceRadius = 0;
            Array.Clear(_branch);
        }
        if (_runtime != runtimePreview) Array.Clear(_branch);
        if (_drag != null && (_selected != selectedLevel || _handle?.Dimension != handle?.Dimension || runtimePreview))
            EndDrag();
        _profile = profile;
        _depth = depth;
        _layout = config.SubmenuStyle;
        _selected = selectedLevel;
        _runtime = runtimePreview;
        _zoom = double.IsFinite(zoom) ? Math.Clamp(zoom, .2, 4) : 1;
        _handle = handle;
        _iconLayout = config.IconLayoutMode;
        BuildScene(config, profile);
        if (_referenceRadius == 0) _referenceRadius = _sceneRadius;
        InvalidateVisual();
    }

    internal void ResetFit()
    {
        _referenceRadius = _sceneRadius;
        InvalidateVisual();
    }

    private void BuildScene(AppConfig config, WheelProfile? profile)
    {
        _cells.Clear();
        var layer = ReadLayer(profile);
        int count = ValidRootCount(layer?.SectorCount ?? profile?.SectorCount ?? 8);
        bool fan = config.SubmenuStyle == "Fan";
        var menu = _depth > 2 && layer is { EffectiveMaxMenuDepth: > 2 } ? layer.ActiveFixedRings : null;
        var roots = ReadEffectiveRoots(config, profile, layer, count);
        // An unconfigured simulated depth still has its suggested positions. It has no runtime actions.
        int configuredDepth = Math.Min(_depth, layer?.EffectiveMaxMenuDepth ?? 2);
        var configured = menu?.ConfiguredSlots(roots, configuredDepth, fan) ??
            FixedRingMenu.LowerConfiguredSlots(roots, configuredDepth, fan);
        _canExpand = config.EnableMultiTier && _depth > 1;
        _resident = menu?.ShowsAllOnPopup(config) == true;
        _secondOnPopup = config.ExpandsSubRingsOnPopup;
        _rootOuter = Positive(config.WheelRadius, 133);
        _rootInner = Math.Clamp(Positive(config.InnerRadius, 70), 0, Math.Max(0, _rootOuter - 1));
        _secondaryOuter = LegacyRingGeometry.SecondaryExtent(config, _rootOuter, _rootInner);
        _fixed = null;
        if (_depth > 2)
        {
            double unit = _rootOuter / 72;
            double thirdInner = FixedRingGeometry.GetThirdInnerRadius(config, fan ? 0 : _secondaryOuter, unit, _rootOuter);
            _fixed = new FixedRingGeometry(count, _rootOuter, thirdInner, unit, fan, config);
        }

        _coreRadius = Positive(config.CoreRadius, 36);
        if (_fixed != null) _coreRadius = _fixed.CoreRadius(_coreRadius);
        _sceneRadius = Math.Max(_coreRadius, _rootOuter);
        if (config.ShowCoreCircle || !_runtime)
        {
            var circle = new EllipseGeometry(Origin, _coreRadius, _coreRadius);
            circle.Freeze();
            ActionItem? action = layer is { EnableCenterAction: true } ? layer.CenterAction :
                layer == null && profile?.EnableCenterAction == true ? profile.CenterAction : null;
            Add(0, 0, null, (circle, Origin), action, config.ShowCoreCircle, 0);
        }

        for (int i = 0; i < count; i++)
        {
            var primary = _fixed?.Primary(config, i) ?? LegacyRingGeometry.Primary(config, count, i, _rootOuter, _rootInner);
            bool visible = _runtime && !_canExpand
                ? Plugins.PluginInteractionSession.IsExecutableAction(roots[i], allowWhitespaceText: true) : configured.Contains((1, i));
            Add(1, i, null, primary, roots[i], visible, i + 1);
        }

        if (_depth >= 2)
        {
            int capacity = fan ? RadialWindow.FanSubmenuSlotCount : 4;
            for (int i = 0; i < count; i++)
            {
                var actions = roots[i]?.SubActions;
                int actualCount = actions?.Count ?? 0;
                int geometryCount = fan ? Math.Min(actualCount, capacity) : actualCount;
                int total = Math.Max(capacity, geometryCount);
                int occupiedFanPositions = 0;
                for (int child = 0; child < geometryCount; child++)
                {
                    int key = FixedRingMenu.SecondaryKey(i, child);
                    var shape = Secondary(config, count, i, child, geometryCount);
                    Add(2, key, i, shape, actions![child], configured.Contains((2, key)), child + 1);
                    if (fan && _fixed == null)
                        occupiedFanPositions |= 1 << RadialWindow.GetFanSlotIndex(child, geometryCount);
                }

                for (int child = geometryCount; child < total; child++)
                {
                    // A partially populated legacy arc already subdivides its full parent span.
                    // Mark spare capacity with small dashed positions outside those exact outlines.
                    var shape = Secondary(config, count, i, child, total);
                    if (geometryCount != 0 && _fixed?.IsHoneycomb != true)
                    {
                        if (fan)
                        {
                            int position = 0;
                            while ((occupiedFanPositions & (1 << position)) != 0) position++;
                            occupiedFanPositions |= 1 << position;
                            shape = Secondary(config, count, i, position, capacity);
                        }
                        else shape = SecondaryCapacityMarker(shape.Label);
                    }
                    Add(2, FixedRingMenu.SecondaryKey(i, child), i, shape, null, false, child + 1);
                }
            }
        }

        if (_fixed == null) return;
        for (int level = 3; level <= _depth; level++)
        {
            int tierCount = menu?.GetCount(level) ?? FixedRingMenu.RecommendedCount(level);
            for (int index = 0; index < tierCount; index++)
            {
                var slot = menu?.GetSlot(level, index);
                var shape = (_fixed.GetShape(level, index, tierCount), _fixed.GetCenter(level, index, tierCount));
                Add(level, index, slot?.ParentIndex, shape, slot?.Action, configured.Contains((level, index)), index + 1);
            }
        }
    }

    private (Geometry Shape, Point Label) Secondary(AppConfig config, int count, int root, int child, int total) =>
        _fixed?.Secondary(config, root, child, total) ??
        LegacyRingGeometry.Secondary(config, count, root, child, total, _rootOuter, _rootInner);

    private (Geometry Shape, Point Label) SecondaryCapacityMarker(Point approximate)
    {
        Vector direction = approximate - Origin;
        if (direction.LengthSquared > 0) direction.Normalize();
        else direction = new Vector(1, 0);
        const double radius = 9;
        // Place the marker just inside the space reserved by the secondary outline. It is a
        // preview-only sizing affordance, so it does not change the actual child address.
        Point center = Origin + (_secondaryOuter - radius) * direction;
        var circle = new EllipseGeometry(center, radius, radius);
        circle.Freeze();
        return (circle, center);
    }

    private void Add(int level, int index, int? parent, (Geometry Shape, Point Label) cell, ActionItem? action,
        bool configured, int number)
    {
        Rect bounds = cell.Shape.Bounds;
        if (bounds.IsEmpty || !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height)) return;
        _cells.Add(new(level, index, parent, cell.Shape, cell.Label, action, configured, number));
        _sceneRadius = Math.Max(_sceneRadius, Math.Max(Math.Max(Math.Abs(bounds.Left), Math.Abs(bounds.Right)),
            Math.Max(Math.Abs(bounds.Top), Math.Abs(bounds.Bottom))));
    }

    private static WheelLayer? ReadLayer(WheelProfile? profile) => profile?.Layers is { Count: > 0 } layers
        ? layers[Math.Clamp(profile.ActiveLayerIndex, 0, layers.Count - 1)] : null;

    private static int ValidRootCount(int count) => count is 4 or 8 or 12 ? count : 8;
    private static double Positive(double value, double fallback) => double.IsFinite(value) && value > 0 ? value : fallback;

    private static ActionItem?[] ReadEffectiveRoots(AppConfig config, WheelProfile? profile, WheelLayer? layer, int count)
    {
        // Read the same local and inherited addresses without calling EnsureLayers or cloning/normalizing user state.
        var roots = new ActionItem?[count];
        if (profile == null) return roots;
        var local = layer?.Actions ?? profile.Actions;
        bool isGlobal = string.Equals(profile.ProcessName, "Global", StringComparison.OrdinalIgnoreCase);
        var inherited = config.EnableGlobalInheritance && !isGlobal
            ? config.Profiles?.FirstOrDefault(item => string.Equals(item?.ProcessName, "Global", StringComparison.OrdinalIgnoreCase)) : null;
        var commonLayer = inherited?.Layers is { Count: > 0 } layers
            ? layers[(uint)profile.ActiveLayerIndex < (uint)layers.Count ? profile.ActiveLayerIndex : 0] : null;
        var common = commonLayer?.Actions ?? inherited?.Actions;
        int commonCount = ValidRootCount(commonLayer?.SectorCount ?? inherited?.SectorCount ?? count);
        for (int i = 0; i < count; i++)
        {
            ActionItem? action = local != null && i < local.Count ? local[i] : null;
            if (string.Equals(action?.Type, "None", StringComparison.OrdinalIgnoreCase)) continue;
            if (isGlobal || WheelProfile.IsActionConfigured(action)) roots[i] = action;
            else if (common != null)
            {
                int index = WheelProfile.MapInheritedSector(i, count, commonCount);
                if ((uint)index < (uint)common.Count && WheelProfile.IsActionConfigured(common[index])) roots[i] = common[index];
            }
        }
        return roots;
    }

    private bool Visible(Cell cell)
    {
        if (!_runtime) return true;
        if (!cell.Configured) return false;
        if (cell.Level <= 1) return true;
        if (!_canExpand) return false;
        if (_resident) return true;
        if (cell.Level == 2 && _secondOnPopup) return true;
        return _branch[cell.Level - 2] is int parent &&
            (cell.Parent == parent || _branch[cell.Level - 1] == cell.Index);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var size = RenderSize;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(size));
        if (size.Width < 24 || size.Height < 24 || _cells.Count == 0) return;

        var accent = (Brush)GetValue(AccentProperty);
        var ink = (Brush)GetValue(InkProperty);
        var muted = (Brush)GetValue(MutedProperty);
        var rim = (Brush)GetValue(RimProperty);
        var fill = (Brush)GetValue(FillProperty);
        var active = (Brush)GetValue(ActiveFillProperty);
        _center = new Point(size.Width / 2, size.Height / 2);
        _scale = _drag?.Scale ?? Math.Max(.01, (Math.Min(size.Width, size.Height) / 2 - 16) /
            Math.Max(1, Math.Max(_referenceRadius, _sceneRadius)) * _zoom);
        var matrix = new Matrix(_scale, 0, 0, _scale, _center.X, _center.Y);
        var transform = new MatrixTransform(matrix);
        transform.Freeze();

        var axes = new Pen(rim, .75);
        dc.PushOpacity(.42);
        dc.DrawLine(axes, new Point(12, _center.Y), new Point(size.Width - 12, _center.Y));
        dc.DrawLine(axes, new Point(_center.X, 12), new Point(_center.X, size.Height - 12));
        dc.Pop();

        var normalOutline = new Pen(rim, 1 / _scale);
        var selectedOutline = new Pen(accent, 1.5 / _scale);
        var vacantOutline = new Pen(rim, 1 / _scale) { DashStyle = DashStyles.Dash };
        // Empty positions are behind real outlines, so configurable capacity never obscures actions.
        dc.PushTransform(transform);
        foreach (bool configured in new[] { false, true })
            foreach (var cell in _cells)
            {
                if (cell.Configured != configured || !Visible(cell)) continue;
                bool selected = !_runtime && cell.Level == _selected;
                dc.DrawGeometry(configured || selected ? selected ? active : fill : null,
                    selected ? selectedOutline : configured ? normalOutline : vacantOutline, cell.Shape);
            }
        dc.Pop();

        foreach (var cell in _cells)
            if (Visible(cell)) DrawCaption(dc, cell, matrix, ink, muted, accent);

        _guide = !_runtime && _handle != null ? MakeGuide(_handle.Dimension) : null;
        if (_guide != null && _handle != null) DrawGuide(dc, _guide, _handle, accent, fill);
    }

    private void DrawCaption(DrawingContext dc, Cell cell, Matrix transform, Brush ink, Brush muted, Brush accent)
    {
        Point center = transform.Transform(cell.Center);
        double screenWidth = cell.Shape.Bounds.Width * _scale, screenHeight = cell.Shape.Bounds.Height * _scale;
        double available = Math.Min(screenWidth, screenHeight) * .72;
        if (available < 4) return;
        bool selected = !_runtime && cell.Level == _selected;
        var brush = selected ? accent : ink;
        if (cell.Level == 0 && cell.Action == null)
        {
            dc.DrawEllipse(brush, null, center, Math.Min(3, available * .2), Math.Min(3, available * .2));
            return;
        }
        if (!cell.Configured)
        {
            if (available >= 8) DrawText(dc, cell.Number.ToString("00", CultureInfo.CurrentUICulture), center,
                Math.Max(9, screenWidth * .9), Math.Clamp(available * .55, 7, 11), muted);
            return;
        }
        var action = cell.Action;
        if (action == null) return;
        string mode = string.IsNullOrEmpty(action.LayoutMode) || action.LayoutMode == "Inherit" ? _iconLayout : action.LayoutMode;
        bool text = mode != "IconOnly";
        bool hasIcon = false;
        if (mode != "TextOnly" && available >= 7)
        {
            var (path, image) = IconHelper.GetCachedActionIcon(action);
            var vector = image == null ? GetIcon(path) ?? GetIcon(IconHelper.IconOnlyFallback(action)) : null;
            double width = Math.Min(available, text ? 13 : 22);
            Point iconCenter = new(center.X, center.Y - (text && available > 20 ? 5 : 0));
            if (image != null && image.Width > 0 && image.Height > 0)
            {
                double fit = width / Math.Max(image.Width, image.Height);
                dc.DrawImage(image, new Rect(iconCenter.X - image.Width * fit / 2, iconCenter.Y - image.Height * fit / 2,
                    image.Width * fit, image.Height * fit));
                hasIcon = true;
            }
            else if (vector != null)
            {
                Rect bounds = vector.Bounds;
                double fit = width / Math.Max(bounds.Width, bounds.Height);
                dc.PushTransform(new MatrixTransform(fit, 0, 0, fit,
                    iconCenter.X - (bounds.X + bounds.Width / 2) * fit,
                    iconCenter.Y - (bounds.Y + bounds.Height / 2) * fit));
                dc.DrawGeometry(brush, null, vector);
                dc.Pop();
                hasIcon = true;
            }
        }
        if ((text || !hasIcon) && available >= 9 && !string.IsNullOrWhiteSpace(action.Name) &&
            (!hasIcon || available > 20))
        {
            center.Y += hasIcon ? 8 : 0;
            DrawText(dc, action.Name, center, Math.Max(12, screenWidth * .7), Math.Clamp(available * .53, 8, 12), brush);
        }
    }

    private Geometry? GetIcon(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (_icons.TryGetValue(path, out var cached)) return cached;
        Geometry? shape = null;
        try
        {
            shape = Geometry.Parse(path);
            if (!shape.Bounds.IsEmpty && shape.Bounds.Width > 0 && shape.Bounds.Height > 0)
                shape.Freeze();
            else shape = null;
        }
        catch (FormatException) { /* Optional imported artwork may not be valid path data. */ }
        if (_icons.Count >= 256) _icons.Clear();
        _icons[path] = shape;
        return shape;
    }

    private FormattedText Text(string text, double size, Brush color) =>
        new(text, CultureInfo.CurrentUICulture, System.Windows.FlowDirection.LeftToRight, CanvasTypeface, size, color,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private void DrawText(DrawingContext dc, string text, Point center, double width, double size, Brush color)
    {
        var label = Text(text, size, color);
        label.MaxTextWidth = width;
        label.MaxLineCount = 1;
        label.TextAlignment = TextAlignment.Center;
        label.Trimming = TextTrimming.CharacterEllipsis;
        dc.DrawText(label, new Point(center.X - width / 2, center.Y - label.Height / 2));
    }

    private Guide? MakeGuide(AppearanceDimension dimension)
    {
        Guide Radial(double inner, double outer, double gain = 1) => new(
            _center + inner * _scale * HandleDirection, _center + outer * _scale * HandleDirection, gain);
        int level = Math.Clamp(_selected, 3, 5);
        switch (dimension)
        {
            case AppearanceDimension.CoreRadius: return Radial(0, _coreRadius);
            case AppearanceDimension.InnerRadius: return Radial(_coreRadius, _rootInner);
            case AppearanceDimension.OuterRadius: return Radial(_rootInner, _rootOuter);
            case AppearanceDimension.SecondaryRadius: return Radial(_rootOuter, _secondaryOuter);
            case AppearanceDimension.RingThickness when _fixed != null:
                return Radial(_fixed.Inner(level), _fixed.Outer(level));
            case AppearanceDimension.RingSpacing when _fixed != null:
                return Radial(level == 3 ? _secondaryOuter : _fixed.Outer(level - 1), _fixed.Inner(level));
            case AppearanceDimension.HoneycombCellSize when _fixed?.IsHoneycomb == true:
            {
                var cell = _cells.FirstOrDefault(item => item.Level == 1);
                if (cell == null) return null;
                double width = _fixed.CellWidth(1, 1) * _scale / 2;
                Point center = _center + (Vector)cell.Center * _scale;
                return new Guide(new Point(center.X - width, center.Y), new Point(center.X + width, center.Y), 2);
            }
            case AppearanceDimension.HoneycombSpacing when _fixed?.IsHoneycomb == true:
            {
                // The settings canvas keeps true shared grid spacing. Use a neighbouring pair
                // close to the primary ring so the label stays in view even at maximum depth.
                var first = _cells.FirstOrDefault(item => item.Level == 1);
                if (first == null) return null;
                var other = _cells.Where(item => item.Level > 0 && !ReferenceEquals(item, first))
                    .OrderBy(item => (item.Center - first.Center).LengthSquared).FirstOrDefault();
                if (other == null) return null;
                Vector axis = other.Center - first.Center;
                axis.Normalize();
                double face = _fixed.CellWidth(1, 1) / 2;
                return new Guide(_center + ((Vector)first.Center + axis * face) * _scale,
                    _center + ((Vector)other.Center - axis * face) * _scale, 1);
            }
            default: return null;
        }
    }

    private void DrawGuide(DrawingContext dc, Guide guide, AppearanceHandle handle, Brush accent, Brush background)
    {
        const double padding = 7;
        dc.DrawLine(new Pen(accent, 1.5) { DashStyle = DashStyles.Dash }, guide.Start, guide.End);
        dc.DrawEllipse(accent, new Pen(background, 1.8), guide.End, 6, 6);
        var text = Text($"{handle.Label} {handle.Value:0.#} DIP", 11, accent);
        text.MaxTextWidth = Math.Max(1, RenderSize.Width - 4 * padding);
        text.MaxLineCount = 1;
        text.Trimming = TextTrimming.CharacterEllipsis;
        double width = text.Width + 2 * padding, height = text.Height + 8;
        double x = Math.Clamp(guide.End.X + 11, 2, Math.Max(2, RenderSize.Width - width - 2));
        double y = Math.Clamp(guide.End.Y - height - 6, 2, Math.Max(2, RenderSize.Height - height - 2));
        dc.DrawRoundedRectangle(background, new Pen(accent, .85), new Rect(x, y, width, height), 5, 5);
        dc.DrawText(text, new Point(x + padding, y + 4));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_scale <= 0 || Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        Point point = e.GetPosition(this);
        if (_guide is { } guide && _handle is { } handle && (point - guide.End).Length <= 12)
        {
            Vector axis = guide.End - guide.Start;
            if (axis.LengthSquared > 0 && CaptureMouse())
            {
                axis.Normalize();
                _drag = new Drag(point, axis, handle.Value, _scale, guide.Gain);
                e.Handled = true;
            }
            return;
        }
        var cell = Hit(point);
        if (cell == null) return;
        if (!_runtime) LevelSelected?.Invoke(cell.Level);
        else if (!_resident)
        {
            if (cell.Level == 0) Array.Clear(_branch);
            else if (cell.Level < _branch.Length)
            {
                _branch[cell.Level - 1] = cell.Index;
                Array.Clear(_branch, cell.Level, _branch.Length - cell.Level);
            }
            InvalidateVisual();
        }
        e.Handled = true;
    }

    private Cell? Hit(Point point)
    {
        var local = Origin + (point - _center) / _scale;
        for (int i = _cells.Count - 1; i >= 0; i--)
        {
            var cell = _cells[i];
            if (Visible(cell) && cell.Shape.Bounds.Contains(local) && cell.Shape.FillContains(local)) return cell;
        }
        return null;
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_scale <= 0) return;
        var point = e.GetPosition(this);
        if (_drag is not { } drag || _handle is not { } handle)
        {
            Cursor = _guide is { } g && (point - g.End).Length <= 12 ? Cursors.SizeWE :
                Hit(point) != null ? Cursors.Hand : Cursors.Arrow;
            return;
        }
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndDrag();
            return;
        }

        double delta = Vector.Multiply(point - drag.Start, drag.Axis) * drag.Gain / drag.Scale;
        double value = Math.Clamp(Math.Round(drag.Value + delta, 1), handle.Minimum, handle.Maximum);
        if (Math.Abs(value - handle.Value) >= .05)
            HandleChanged?.Invoke(value);
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_drag == null) return;
        EndDrag();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(System.Windows.Input.MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        EndDrag();
    }

    private void EndDrag()
    {
        if (_drag == null) return;
        _drag = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
        HandleDragCompleted?.Invoke();
        InvalidateVisual();
    }
}
