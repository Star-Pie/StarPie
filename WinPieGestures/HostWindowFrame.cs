using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using Path = System.Windows.Shapes.Path;
using Binding = System.Windows.Data.Binding;

namespace WinPieGestures;

internal static class HostWindowFrame
{
    private static readonly ConditionalWeakTable<Window, Frame> Frames = new();

    public static void Apply(Window window)
    {
        // Overlay and plugin windows keep their own non-client and transparency contracts.
        if (window.Content == null || window.WindowStyle == WindowStyle.None || window.AllowsTransparency ||
            (window.GetType().Assembly != typeof(HostWindowFrame).Assembly && window.GetType() != typeof(Window))) return;
        Frames.GetValue(window, w => new Frame(w)).Refresh();
    }

    public static void SetCloseEnabled(Window window, bool enabled)
    {
        if (Frames.TryGetValue(window, out var frame)) frame.Close.IsEnabled = enabled;
    }

    private sealed class Frame
    {
        private readonly Window window;
        private readonly Border border;
        private readonly Image mark;
        private readonly Button minimize;
        private readonly Button maximize;
        private readonly Path maximizeIcon;
        public Button Close { get; }

        public Frame(Window window)
        {
            this.window = window;
            object? originalContent = window.Content;
            window.Content = null;
            var caption = new Grid { Height = 36, Name = "HostCaption", SnapsToDevicePixels = true };
            caption.SetResourceReference(Panel.BackgroundProperty, "WindowBackgroundBrush");
            caption.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            caption.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var identity = new Grid { Margin = new Thickness(13, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            identity.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            identity.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            mark = new Image { Width = 20, Height = 20, Margin = new Thickness(0, 0, 9, 0) };
            identity.Children.Add(mark);
            var title = new TextBlock { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            title.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            title.SetBinding(TextBlock.TextProperty, new Binding(nameof(Window.Title)) { Source = window });
            Grid.SetColumn(title, 1);
            identity.Children.Add(title);
            caption.Children.Add(identity);
            var controls = new StackPanel { Orientation = Orientation.Horizontal };
            Grid.SetColumn(controls, 1);
            minimize = CaptionButton("M 1,6 L 11,6", false);
            maximize = CaptionButton("M 2,2 L 10,2 L 10,10 L 2,10 Z", false);
            maximizeIcon = (Path)maximize.Content;
            Close = CaptionButton("M 2,2 L 10,10 M 10,2 L 2,10", true);
            controls.Children.Add(minimize); controls.Children.Add(maximize); controls.Children.Add(Close);
            caption.Children.Add(controls);

            var body = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(caption, Dock.Top);
            body.Children.Add(caption);
            if (originalContent is UIElement element) body.Children.Add(element);
            else body.Children.Add(new ContentPresenter { Content = originalContent });
            border = new Border { Child = body, SnapsToDevicePixels = true };
            border.SetResourceReference(Border.BackgroundProperty, "WindowBackgroundBrush");
            border.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
            border.SetResourceReference(Border.BorderThicknessProperty, "ArtBorderThickness");
            window.Content = border;
            WindowChrome.SetWindowChrome(window, new WindowChrome
            {
                CaptionHeight = 36, GlassFrameThickness = new Thickness(0),
                ResizeBorderThickness = new Thickness(CanResize ? 6 : 0),
                UseAeroCaptionButtons = false, CornerRadius = new CornerRadius(0)
            });
            Bind(SystemCommands.MinimizeWindowCommand, () => SystemCommands.MinimizeWindow(window), () => window.ResizeMode != ResizeMode.NoResize);
            Bind(SystemCommands.MaximizeWindowCommand, () => SystemCommands.MaximizeWindow(window), () => CanResize);
            Bind(SystemCommands.RestoreWindowCommand, () => SystemCommands.RestoreWindow(window), () => window.WindowState != WindowState.Normal);
            Bind(SystemCommands.CloseWindowCommand, () => SystemCommands.CloseWindow(window), () => true);
            minimize.Command = SystemCommands.MinimizeWindowCommand;
            Close.Command = SystemCommands.CloseWindowCommand;
            window.StateChanged += StateChanged;
            border.SizeChanged += BorderSizeChanged;
            window.Closed += Closed;
            I18n.LanguageChanged += LanguageChanged;
        }

        private bool CanResize => window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip;
        private void Bind(RoutedCommand command, Action execute, Func<bool> canExecute)
            => window.CommandBindings.Add(new CommandBinding(command, (_, _) => execute(), (_, e) => { e.CanExecute = canExecute(); e.Handled = true; }));
        private void StateChanged(object? sender, EventArgs e) => Refresh();
        private void BorderSizeChanged(object sender, SizeChangedEventArgs e) => RefreshClip();
        private void LanguageChanged()
        {
            if (window.Dispatcher.CheckAccess()) Refresh();
            else window.Dispatcher.BeginInvoke(Refresh);
        }
        private void Closed(object? sender, EventArgs e)
        {
            I18n.LanguageChanged -= LanguageChanged;
            window.StateChanged -= StateChanged;
            border.SizeChanged -= BorderSizeChanged;
            window.Closed -= Closed;
        }

        public void Refresh()
        {
            mark.Source = ThemeBrandMark.Get(window);
            WindowChrome.GetWindowChrome(window).ResizeBorderThickness = new Thickness(CanResize ? 6 : 0);
            minimize.Visibility = window.ResizeMode == ResizeMode.NoResize ? Visibility.Collapsed : Visibility.Visible;
            maximize.Visibility = CanResize ? Visibility.Visible : Visibility.Collapsed;
            bool restored = window.WindowState != WindowState.Maximized;
            maximize.Command = restored ? SystemCommands.MaximizeWindowCommand : SystemCommands.RestoreWindowCommand;
            maximizeIcon.Data = FrozenGeometry(restored ? "M 2,2 L 10,2 L 10,10 L 2,10 Z" : "M 4,2 L 11,2 L 11,9 M 1,5 L 8,5 L 8,12 L 1,12 Z");
            border.Padding = new Thickness(window.WindowState == WindowState.Maximized ? 6 : 0);
            double radius = window.WindowState == WindowState.Maximized ? 0 : 8;
            border.CornerRadius = new CornerRadius(radius);
            // Non-glass WindowChrome forwards this value as the Win32 rounding ellipse diameter.
            WindowChrome.GetWindowChrome(window).CornerRadius = new CornerRadius(radius * 2);
            RefreshClip();
            Label(minimize, "CaptionMinimize");
            Label(maximize, restored ? "CaptionMaximize" : "CaptionRestore");
            Label(Close, window is SettingsWindow ? "CaptionHideSettings" : "CaptionClose");
        }

        private void RefreshClip()
        {
            if (border.ActualWidth <= 0 || border.ActualHeight <= 0) return;
            double radius = border.CornerRadius.TopLeft;
            var clip = new RectangleGeometry(new Rect(0, 0, border.ActualWidth, border.ActualHeight), radius, radius);
            clip.Freeze();
            border.Clip = clip;
        }

        private Button CaptionButton(string geometry, bool close)
        {
            var icon = new Path { Data = FrozenGeometry(geometry), Width = 12, Height = 12, Stretch = Stretch.Uniform, StrokeThickness = 1.25, SnapsToDevicePixels = true };
            icon.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, new Binding(nameof(Control.Foreground)) { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1) });
            var button = new Button { Content = icon, Width = 44, Height = 35, CommandTarget = window, Focusable = false, Padding = new Thickness(0), BorderThickness = new Thickness(0) };
            button.Style = ButtonStyle(close);
            WindowChrome.SetIsHitTestVisibleInChrome(button, true);
            return button;
        }

        private static Style ButtonStyle(bool close)
        {
            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("TextPrimaryBrush")));
            var frame = new FrameworkElementFactory(typeof(Border));
            frame.SetBinding(Border.BackgroundProperty, new Binding(nameof(Control.Background)) { RelativeSource = RelativeSource.TemplatedParent });
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            frame.AppendChild(content);
            style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(Button)) { VisualTree = frame }));
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            if (close)
            {
                var red = new SolidColorBrush(Color.FromRgb(196, 43, 28)); red.Freeze();
                hover.Setters.Add(new Setter(Control.BackgroundProperty, red));
                hover.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            }
            else hover.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("ItemHoverBrush")));
            style.Triggers.Add(hover);
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .4));
            style.Triggers.Add(disabled);
            style.Seal();
            return style;
        }

        private static Geometry FrozenGeometry(string data) { var geometry = Geometry.Parse(data); geometry.Freeze(); return geometry; }
        private static void Label(Button button, string key)
        {
            string label = I18n.T(key);
            button.ToolTip = label;
            AutomationProperties.SetName(button, label);
        }
    }
}
