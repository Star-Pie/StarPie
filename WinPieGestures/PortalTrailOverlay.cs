using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Pen = System.Windows.Media.Pen;

namespace WinPieGestures;

internal sealed class PortalTrailOverlay : Window
{
    private const uint SwpNoActivate = 0x0010;
    private static readonly nint HwndTopmost = new(-1);

    private sealed class TrailSurface : FrameworkElement
    {
        internal DrawingVisual Visual { get; } = new();
        internal TrailSurface() => AddVisualChild(Visual);
        protected override int VisualChildrenCount => 1;
        protected override Visual GetVisualChild(int index) => index == 0 ? Visual : throw new ArgumentOutOfRangeException(nameof(index));
    }

    private readonly TrailSurface _surface = new();
    private int _physLeft;
    private int _physTop;
    private int _physWidth;
    private int _physHeight;
    private nint _insertAfter = HwndTopmost;

    internal PortalTrailOverlay()
    {
        AllowsTransparency = true;
        WindowStyle = WindowStyle.None;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = false;
        Focusable = false;
        IsHitTestVisible = false;
        Background = Brushes.Transparent;
        UseLayoutRounding = false;
        Content = _surface;
    }

    internal static Geometry BuildCustomMarkerGeometry(string? shape, Point center, double radius)
    {
        double r = Math.Max(4, radius);
        Geometry geo;
        switch (shape)
        {
            case "FourPointStar":
                geo = BuildPolygon(center, 8, i => i % 2 == 0 ? r : r * 0.36);
                break;
            case "Diamond":
                geo = BuildPolygon(center, 4, _ => r);
                break;
            case "Hexagon":
                geo = BuildPolygon(center, 6, _ => r);
                break;
            case "RoundedSquare":
                double s = r * 1.65;
                geo = new RectangleGeometry(new Rect(center.X - s / 2, center.Y - s / 2, s, s), s * 0.24, s * 0.24);
                break;
            default:
                geo = new EllipseGeometry(center, r, r);
                break;
        }
        if (geo.CanFreeze) geo.Freeze();
        return geo;
    }

    private static StreamGeometry BuildPolygon(Point center, int vertices, Func<int, double> radiusAt)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            for (int i = 0; i < vertices; i++)
            {
                double rad = (-90.0 + i * 360.0 / vertices) * (Math.PI / 180.0);
                double r = radiusAt(i);
                var pt = new Point(center.X + Math.Cos(rad) * r, center.Y + Math.Sin(rad) * r);
                if (i == 0) ctx.BeginFigure(pt, isFilled: true, isClosed: true);
                else ctx.LineTo(pt, isStroked: true, isSmoothJoin: true);
            }
        }
        return g;
    }

    internal static void DrawCustomMarkerVisual(DrawingContext dc, Point center, double radius, AppConfig config,
        Brush themeEdge, Brush themeFill, double opacityScale = 1.0)
    {
        string shape = config.EffectivePortalCustomShape;
        double opacity = Math.Clamp(config.EffectivePortalCustomOpacity * opacityScale, 0.05, 1.0);
        Brush fillBrush = TryParseSolidBrush(config.PortalCustomFillColor) ?? CloneFrozen(themeFill, 0.36);
        Brush borderBrush = TryParseSolidBrush(config.PortalCustomBorderColor) ?? CloneFrozen(themeEdge, 0.92);
        var geo = BuildCustomMarkerGeometry(shape, center, radius);
        var borderPen = new Pen(borderBrush, Math.Clamp(radius * 0.05, 1.3, 3.0));
        if (borderPen.CanFreeze) borderPen.Freeze();

        dc.PushOpacity(opacity);
        try
        {
            dc.DrawGeometry(fillBrush, null, geo);
            if (!string.IsNullOrWhiteSpace(config.PortalCustomImagePath) &&
                IconHelper.GetCustomImageSource(config.PortalCustomImagePath, 256) is { } img)
            {
                dc.PushClip(geo);
                try
                {
                    dc.DrawImage(img, new Rect(center.X - radius, center.Y - radius, radius * 2, radius * 2));
                }
                finally
                {
                    dc.Pop();
                }
            }
            else if (!string.IsNullOrWhiteSpace(config.PortalCustomIconKey) &&
                     IconHelper.GetSvgPathByKey(config.PortalCustomIconKey) is { Length: > 0 } svg)
            {
                DrawCenteredSvgGlyph(dc, svg, center, radius, shape, fillBrush);
            }
            dc.DrawGeometry(null, borderPen, geo);
        }
        finally
        {
            dc.Pop();
        }
    }

    private static void DrawCenteredSvgGlyph(DrawingContext dc, string svg, Point center, double radius,
        string shape, Brush bgBrush)
    {
        try
        {
            Geometry raw = Geometry.Parse(svg);
            Rect bounds = raw.Bounds;
            if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) return;
            double boxRatio = shape == "FourPointStar" ? 0.56 : 0.90;
            double target = Math.Max(8, radius * boxRatio);
            double scale = target / Math.Max(bounds.Width, bounds.Height);
            var matrix = Matrix.Identity;
            matrix.Translate(-bounds.X - bounds.Width / 2.0, -bounds.Y - bounds.Height / 2.0);
            matrix.Scale(scale, scale);
            matrix.Translate(center.X, center.Y);
            Geometry glyph = raw.Clone();
            glyph.Transform = new MatrixTransform(matrix);
            if (glyph.CanFreeze) glyph.Freeze();
            dc.DrawGeometry(ContrastGlyphBrush(bgBrush), null, glyph);
        }
        catch
        {
        }
    }

    private static Brush ContrastGlyphBrush(Brush bg)
    {
        if (bg is SolidColorBrush s)
        {
            Color c = s.Color;
            double lum = 0.2126 * (c.R / 255.0) + 0.7152 * (c.G / 255.0) + 0.0722 * (c.B / 255.0);
            if (c.A >= 96 && lum >= 0.55) return Brushes.SlateGray;
        }
        return Brushes.White;
    }

    private static SolidColorBrush? TryParseSolidBrush(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        try
        {
            if (ColorConverter.ConvertFromString(hex.Trim()) is Color color)
            {
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
        }
        catch
        {
        }
        return null;
    }

    private static Brush CloneFrozen(Brush source, double opacity)
    {
        var copy = source.CloneCurrentValue();
        copy.Opacity *= opacity;
        if (copy.CanFreeze) copy.Freeze();
        return copy;
    }

    internal void Draw(IReadOnlyList<PortalFrame> frames, ScreenContext screen, Brush edge, Brush fill,
        nint radialHwnd = 0, Brush? coreBgBrush = null, Brush? coreBorderBrush = null)
    {
        if (frames.Count < 2) { ClearTrail(); return; }
        var config = ConfigManager.CurrentConfig;
        string mode = config.EffectivePortalParentVisualMode;
        var dpi = screen.DpiScale;

        double NodeVisualRadius(PortalFrame f) => mode switch
        {
            "ParentCore" => Math.Max(12, f.CoreRadius) + 6,
            "FadedWheel" => Math.Max(f.OuterRadius, f.SnapshotRadiusDip) + 8,
            "CustomMarker" => config.EffectivePortalCustomSizeDip + 8,
            _ => f.OuterRadius + 6
        };

        var bounds = new Rect(frames[^1].PhysicalCenter, new Size());
        double curR = Math.Max(12, frames[^1].CoreRadius);
        bounds.Inflate(curR * dpi.DpiScaleX, curR * dpi.DpiScaleY);
        for (int i = 0; i < frames.Count - 1; i++)
        {
            var frame = frames[i];
            double vr = NodeVisualRadius(frame);
            double x = vr * dpi.DpiScaleX;
            double y = vr * dpi.DpiScaleY;
            bounds.Union(new Rect(frame.PhysicalCenter.X - x, frame.PhysicalCenter.Y - y, x * 2, y * 2));
            bounds.Union(frames[i + 1].AnchorPhysicalPoint);
        }
        bounds.Inflate(18 * dpi.DpiScaleX, 18 * dpi.DpiScaleY);
        bounds.Intersect(screen.PhysicalWorkArea);
        if (bounds.IsEmpty || bounds.Width < 1 || bounds.Height < 1) { ClearTrail(); return; }

        double left = Math.Floor(bounds.Left), top = Math.Floor(bounds.Top);
        double width = Math.Ceiling(bounds.Right) - left, height = Math.Ceiling(bounds.Bottom) - top;
        _physLeft = (int)left;
        _physTop = (int)top;
        _physWidth = (int)width;
        _physHeight = (int)height;
        _insertAfter = radialHwnd != IntPtr.Zero ? radialHwnd : HwndTopmost;

        if (!IsVisible)
        {
            var helper = new WindowInteropHelper(this);
            if (helper.Handle == IntPtr.Zero) helper.EnsureHandle();
            ApplyPhysicalBounds();
            Left = left / dpi.DpiScaleX;
            Top = top / dpi.DpiScaleY;
        }
        Width = width / dpi.DpiScaleX;
        Height = height / dpi.DpiScaleY;

        var outlineBrush = CloneFrozen(edge, .62);
        var fillBrush = CloneFrozen(fill, .14);
        var outline = new Pen(outlineBrush, 1.5);
        outline.Freeze();
        var beam = new Pen(outlineBrush, 2.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        beam.Freeze();
        Brush fallbackCoreFill = coreBgBrush != null ? CloneFrozen(coreBgBrush, .92) : fillBrush;
        var fallbackCoreStroke = new Pen(coreBorderBrush != null ? CloneFrozen(coreBorderBrush, .85) : outlineBrush, 1.5);
        fallbackCoreStroke.Freeze();

        using (var dc = _surface.Visual.RenderOpen())
        {
            for (int i = 0; i < frames.Count - 1; i++)
            {
                var frame = frames[i];
                var next = frames[i + 1];
                int age = frames.Count - 2 - i;
                double ageFade = Math.Pow(0.68, age);
                Point origin = Local(frame.PhysicalCenter);
                Point nextCenter = Local(next.PhysicalCenter);
                Point anchor = Local(next.AnchorPhysicalPoint);

                double parentSkip = 4;
                if (mode == "ParentCore")
                {
                    double coreR = Math.Max(12, frame.CoreRadius);
                    parentSkip = coreR + 3;
                    dc.PushOpacity(Math.Clamp(0.95 * Math.Pow(0.78, age), 0.25, 1.0));
                    try
                    {
                        if (frame.ParentSnapshot != null)
                        {
                            var clip = new EllipseGeometry(origin, coreR, coreR);
                            clip.Freeze();
                            dc.PushClip(clip);
                            try
                            {
                                dc.DrawImage(frame.ParentSnapshot, new Rect(origin.X - coreR, origin.Y - coreR, coreR * 2, coreR * 2));
                            }
                            finally
                            {
                                dc.Pop();
                            }
                            dc.DrawEllipse(null, outline, origin, coreR, coreR);
                        }
                        else
                        {
                            dc.DrawEllipse(fallbackCoreFill, fallbackCoreStroke, origin, coreR, coreR);
                        }
                    }
                    finally
                    {
                        dc.Pop();
                    }
                }
                else if (mode == "FadedWheel")
                {
                    double snapR = frame.SnapshotRadiusDip > 0 ? frame.SnapshotRadiusDip : frame.OuterRadius;
                    double fadeAlpha = config.EffectivePortalFadeOpacity * ageFade;
                    if (frame.ParentSnapshot != null)
                    {
                        dc.PushOpacity(fadeAlpha);
                        try
                        {
                            dc.DrawImage(frame.ParentSnapshot, new Rect(origin.X - snapR, origin.Y - snapR, snapR * 2, snapR * 2));
                        }
                        finally
                        {
                            dc.Pop();
                        }
                    }
                    else
                    {
                        dc.DrawEllipse(fillBrush, outline, origin, frame.OuterRadius, frame.OuterRadius);
                    }
                    parentSkip = 4;
                }
                else if (mode == "CustomMarker")
                {
                    double customR = config.EffectivePortalCustomSizeDip;
                    DrawCustomMarkerVisual(dc, origin, customR, config, edge, fill, Math.Pow(0.78, age));
                    parentSkip = customR + 3;
                }
                else
                {
                    dc.DrawEllipse(fillBrush, outline, origin, frame.OuterRadius, frame.OuterRadius);
                    parentSkip = 4;
                }

                if (config.PortalTrailShowBeam)
                {
                    double childSkip = Math.Max(12, next.CoreRadius) + 4;
                    Vector delta = nextCenter - origin;
                    double dist = delta.Length;
                    if (dist > parentSkip + childSkip + 2)
                    {
                        delta /= dist;
                        dc.DrawLine(beam, origin + delta * parentSkip, nextCenter - delta * childSkip);
                    }
                    if ((anchor - nextCenter).Length > childSkip && (anchor - origin).Length > parentSkip)
                        dc.DrawEllipse(outlineBrush, null, anchor, 4, 4);
                }
            }
        }
        if (!IsVisible) Show();
        ApplyPhysicalBounds();

        Point Local(Point physical) => new((physical.X - left) / dpi.DpiScaleX, (physical.Y - top) / dpi.DpiScaleY);
    }

    private void ApplyPhysicalBounds()
    {
        if (_physWidth <= 0 || _physHeight <= 0) return;
        nint hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
            SetWindowPos(hwnd, _insertAfter, _physLeft, _physTop, _physWidth, _physHeight, SwpNoActivate);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        if (!IsVisible || _physWidth <= 0 || _physHeight <= 0) return;
        Dispatcher.BeginInvoke((Action)(() =>
        {
            if (IsVisible && _physWidth > 0 && _physHeight > 0) ApplyPhysicalBounds();
        }), DispatcherPriority.Render);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // The overlay may draw over another application but must never become its input target.
        nint hwnd = new WindowInteropHelper(this).Handle;
        SetWindowLongPtr(hwnd, -20, GetWindowLongPtr(hwnd, -20) | 0x080000A0);
    }

    internal void ClearTrail()
    {
        _physWidth = _physHeight = 0;
        if (!IsVisible) return;
        using (_surface.Visual.RenderOpen()) { }
        Hide();
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);
}
