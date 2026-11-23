using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace WinPieGestures;

public partial class RadialWindow
{
    private const string PortalIconPath = "M12,2A10,10 0 1 0 12,22A10,10 0 1 0 12,2Z M12,4A8,8 0 1 1 12,20A8,8 0 1 1 12,4Z M12,7A5,5 0 1 0 12,17A5,5 0 1 0 12,7Z M12,9A3,3 0 1 1 12,15A3,3 0 1 1 12,9Z";
    private PortalTrailOverlay? _portalTrail;
    private Border? _portalBack;
    private bool _keepPortalCenter;
    private long _portalPresentationEpoch;

    internal double PortalVisualRadius(int fixedDepth = 2, double deepRadius = 0)
    {
        double radius = Math.Max(deepRadius, _outerRadius);
        if (FixedGeometry is { } geometry && GestureController.CaptureReleaseLayer(_profile)?.ActiveFixedRings is { } menu)
        {
            if (HasHoneycomb) radius = Math.Max(radius, geometry.Outer(Math.Clamp(fixedDepth, 1, 5)));
            else if (fixedDepth >= 3) radius = Math.Max(radius, geometry.OuterForCount(fixedDepth, menu.GetCount(fixedDepth)));
            if (_subSectorPaths.Count > 0) radius = Math.Max(radius, LegacyOuterExtent);
            return radius;
        }
        if (_subSectorPaths.Count > 0)
            radius = Math.Max(radius, LegacyRingGeometry.SecondaryExtent(ConfigManager.CurrentConfig, _outerRadius, _innerRadius));
        return radius;
    }

    internal static double PortalInitialRadius(WheelProfile profile, ScreenContext screen)
    {
        var config = ConfigManager.CurrentConfig;
        double radius = Math.Max(1, config.WheelRadius);
        var layer = GestureController.CaptureReleaseLayer(profile)!;
        if (!config.EnableMultiTier) return radius;
        var menu = layer.ActiveFixedRings;
        bool honeycomb = config.SubmenuStyle == "Fan";
        bool showAll = menu?.ShowsAllOnPopup(config) == true && layer.EffectiveMaxMenuDepth > 2;
        double secondary = LegacyRingGeometry.SecondaryExtent(config, radius, config.InnerRadius);
        double marginX = config.EdgeSafeMarginX >= 0 ? config.EdgeSafeMarginX : Math.Max(0, config.EdgeSafeMargin);
        double marginY = config.EdgeSafeMarginY >= 0 ? config.EdgeSafeMarginY : Math.Max(0, config.EdgeSafeMargin);
        double limit = Math.Min(screen.PhysicalWorkArea.Width / screen.DpiScale.DpiScaleX / 2 - marginX,
            screen.PhysicalWorkArea.Height / screen.DpiScale.DpiScaleY / 2 - marginY);
        double legacy = honeycomb ? 0 : Math.Max(radius, secondary);
        int depth = layer.EffectiveMaxMenuDepth;
        FixedRingGeometry CreateFixedGeometry()
        {
            double scale = FixedRingGeometry.FitScale(config, layer.SectorCount, radius, legacy, depth, limit);
            return new FixedRingGeometry(layer.SectorCount, radius,
                FixedRingGeometry.GetThirdInnerRadius(config, legacy, scale, radius), scale, honeycomb, config);
        }
        if (!showAll)
        {
            if (menu != null && honeycomb && depth > 2) return Math.Max(radius, CreateFixedGeometry().Outer(1));
            if (config.ExpandsSubRingsOnPopup &&
                FixedRingMenu.LowerConfiguredSlots(profile, 2, honeycomb).Any(slot => slot.Level == 2))
                return Math.Max(radius, secondary);
            return radius;
        }
        var geometry = CreateFixedGeometry();
        int outermost = menu!.ConfiguredSlots(profile, depth, honeycomb).Select(slot => slot.Level).DefaultIfEmpty(1).Max();
        if (honeycomb) return Math.Max(radius, geometry.Outer(Math.Clamp(outermost, 1, 5)));
        return outermost < 3 ? outermost == 2 ? Math.Max(radius, secondary) : radius :
            Math.Max(radius, geometry.OuterForCount(outermost, menu.GetCount(outermost)));
    }

    internal Vector PortalLegacyAnchor(int sector, int sub)
    {
        if (sub >= 0)
        {
            for (int i = _subSectorPaths.Count - 1; i >= 0; i--)
            {
                if (_subSectorParentIndices[i] != sector || _subSectorChildIndices[i] != sub) continue;
                Rect bounds = _subSectorPaths[i].Data.Bounds;
                return new(bounds.X + bounds.Width / 2 - _canvasCenter, bounds.Y + bounds.Height / 2 - _canvasCenter);
            }
        }
        if (HasHoneycomb && FixedGeometry != null)
            return (Vector)FixedGeometry.GetCenter(1, sector, _profile.SectorCount);
        double angle = sector * Math.Tau / _profile.SectorCount;
        double radius = (_innerRadius + _outerRadius) / 2;
        return new(Math.Cos(angle) * radius, Math.Sin(angle) * radius);
    }

    internal double PortalLegacyOuter(Vector anchor, int sub) => sub < 0 && !HasHoneycomb ? _outerRadius :
        sub >= 0 && ConfigManager.CurrentConfig.SubmenuStyle == "Wheel"
            ? Math.Max(anchor.Length, LegacyRingGeometry.SecondaryExtent(ConfigManager.CurrentConfig, _outerRadius, _innerRadius))
            : anchor.Length + Math.Max(8, (_outerRadius - _innerRadius) / 4);

    internal double PortalCoreRadius =>
        Math.Max(12, (CoreGrid.Width > 0 ? CoreGrid.Width : ConfigManager.CurrentConfig.CoreRadius * 2) / 2);

    internal (ImageSource? Image, double RadiusDip) CapturePortalParentSnapshot(string mode, double parentRadius,
        int highlightSector, int highlightSub)
    {
        double coreR = PortalCoreRadius;
        if (mode is not ("ParentCore" or "FadedWheel")) return (null, 0);
        if (!IsLoaded || MainGrid.ActualWidth <= 0 || MainGrid.ActualHeight <= 0)
            return (null, mode == "ParentCore" ? coreR : parentRadius + 12);

        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        double scale = Math.Max(1.0, Math.Max(dpi.DpiScaleX, dpi.DpiScaleY));
        var prevOverlayVis = CoreSelectionOverlay.Visibility;
        var prevSelTextVis = CoreSelectionTextPanel.Visibility;
        var prevVolVis = CoreVolumeText.Visibility;
        var prevBadgeVis = LayerIndicatorBadge.Visibility;
        try
        {
            if (mode == "ParentCore")
            {
                CoreSelectionOverlay.Visibility = Visibility.Collapsed;
                CoreSelectionTextPanel.Visibility = Visibility.Collapsed;
                CoreVolumeText.Visibility = Visibility.Collapsed;
                if (CoreGrid.ActualWidth <= 0 || CoreGrid.ActualHeight <= 0) return (null, coreR);
                CoreGrid.UpdateLayout();
                int px = Math.Clamp((int)Math.Ceiling(coreR * 2 * scale), 32, 220);
                return (RenderRegionToBitmap(CoreGrid, new Rect(0, 0, CoreGrid.ActualWidth, CoreGrid.ActualHeight), px), coreR);
            }

            if (highlightSector >= 0) HighlightSector(highlightSector, highlightSub, highlightSub >= 0);
            CoreSelectionOverlay.Visibility = Visibility.Collapsed;
            CoreSelectionTextPanel.Visibility = Visibility.Collapsed;
            CoreVolumeText.Visibility = Visibility.Collapsed;
            LayerIndicatorBadge.Visibility = Visibility.Collapsed;
            MainGrid.UpdateLayout();
            double snapR = Math.Max(48, Math.Ceiling(Math.Max(parentRadius, _outerRadius) + 12));
            int wheelPx = Math.Clamp((int)Math.Ceiling(snapR * 2 * scale), 64, 512);
            double cx = MainGrid.ActualWidth / 2.0, cy = MainGrid.ActualHeight / 2.0;
            return (RenderRegionToBitmap(MainGrid, new Rect(cx - snapR, cy - snapR, snapR * 2, snapR * 2), wheelPx), snapR);
        }
        finally
        {
            CoreSelectionOverlay.Visibility = prevOverlayVis;
            CoreSelectionTextPanel.Visibility = prevSelTextVis;
            CoreVolumeText.Visibility = prevVolVis;
            LayerIndicatorBadge.Visibility = prevBadgeVis;
        }
    }

    private static BitmapSource RenderRegionToBitmap(Visual target, Rect viewbox, int pixelSize)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var brush = new VisualBrush(target)
            {
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = viewbox,
                Stretch = Stretch.Fill
            };
            dc.DrawRectangle(brush, null, new Rect(0, 0, pixelSize, pixelSize));
        }
        var bitmap = new RenderTargetBitmap(pixelSize, pixelSize, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private (Brush Foreground, Effect? Shadow) ResolvePortalBackAppearance()
    {
        Brush? fill = CoreEllipse?.Fill;
        if (fill is SolidColorBrush solid && solid.Color.A >= 96)
        {
            Color c = solid.Color;
            double lum = 0.2126 * (c.R / 255.0) + 0.7152 * (c.G / 255.0) + 0.0722 * (c.B / 255.0);
            return lum < 0.52 ? (CoreTextLightBrush, Resources["TextShadow"] as Effect) : (CoreTextDarkBrush, null);
        }
        if (fill is GradientBrush grad && grad.GradientStops.Count > 0)
        {
            double avgLum = grad.GradientStops.Average(s =>
                0.2126 * (s.Color.R / 255.0) + 0.7152 * (s.Color.G / 255.0) + 0.0722 * (s.Color.B / 255.0));
            return avgLum < 0.52 ? (CoreTextLightBrush, Resources["TextShadow"] as Effect) : (CoreTextDarkBrush, null);
        }
        if (fill is ImageBrush && !string.IsNullOrEmpty(ConfigManager.CurrentConfig.CoreBgImagePath) &&
            GetImageAverageLuminance(ConfigManager.CurrentConfig.CoreBgImagePath) is double imgLum && imgLum >= 0.52)
        {
            return (CoreTextDarkBrush, null);
        }
        return (CoreTextLightBrush, Resources["TextShadow"] as Effect);
    }

    internal void PresentPortalFrame(PortalFrame frame, WheelProfile profile, long revision, long version, string? parentLabel)
    {
        PresentCore(frame.PhysicalCenter, profile, revision, version, null, false, null, keepPortalCenter: true);
        InvalidatePinnedArrowSlots();
        if (parentLabel != null)
        {
            CoreTextPanel.Visibility = Visibility.Collapsed;
            CoreExitIcon.Visibility = Visibility.Collapsed;
            CoreCustomImageEllipse.Visibility = Visibility.Collapsed;
            var (backBrush, backShadow) = ResolvePortalBackAppearance();
            var text = new TextBlock
            {
                Text = "↩\n" + parentLabel, TextAlignment = TextAlignment.Center, FontWeight = FontWeights.SemiBold,
                FontSize = 11, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = backBrush, Effect = backShadow, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 4, 0)
            };
            _portalBack = new Border
            {
                Child = text, Background = Brushes.Transparent, CornerRadius = new CornerRadius(CoreGrid.Width / 2),
                IsHitTestVisible = false
            };
            Panel.SetZIndex(_portalBack, 10);
            CoreGrid.Visibility = Visibility.Visible;
            CoreGrid.Children.Add(_portalBack);
        }
        UpdateLayout();
    }

    internal void DrawPortalTrail(PortalSessionStack stack, ScreenContext screen)
    {
        if (stack.Depth < 2) { _portalTrail?.ClearTrail(); return; }
        _portalTrail ??= new PortalTrailOverlay();
        _portalTrail.Draw(stack.Frames, screen, _highlightBorderBrush, _highlightSectorBrush,
            new WindowInteropHelper(this).Handle, _coreBgBrush, _coreBorderBrush);
        EnsureTopmostNoActivate();
    }

    private void ClearPortalBack()
    {
        if (_portalBack == null) return;
        CoreGrid.Children.Remove(_portalBack);
        _portalBack = null;
        CoreTextPanel.Visibility = Visibility.Collapsed;
        CoreExitIcon.Visibility = _defaultCoreExitIconVisibility;
        CoreCustomImageEllipse.Visibility = _defaultCoreCustomImageVisibility;
    }

    internal void ShowPortalFeedback(string key)
    {
        ApplyLayerIndicatorStyle(force: true);
        _layerBadgeStoryboard?.Stop();
        LayerIndicatorBadge.BeginAnimation(OpacityProperty, null);
        LayerIndicatorText.Text = I18n.T(key);
        LayerIndicatorBadge.Visibility = Visibility.Visible;
        LayerIndicatorBadge.Opacity = 1;
        var storyboard = new Storyboard();
        var fade = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(300)))
        {
            BeginTime = TimeSpan.FromSeconds(1.3)
        };
        Storyboard.SetTarget(fade, LayerIndicatorBadge);
        Storyboard.SetTargetProperty(fade, new PropertyPath(OpacityProperty));
        storyboard.Children.Add(fade);
        _layerBadgeStoryboard = storyboard;
        storyboard.Begin();
    }

    private static void AddPortalRuntimeBadge(Grid slot, ActionItem? action, Brush ink)
    {
        if (action?.IsWheelPortal != true) return;
        slot.Children.Add(new TextBlock
        {
            Text = "◎", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = ink,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(1), IsHitTestVisible = false
        });
    }
}
