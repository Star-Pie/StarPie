using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using WpfMouseArgs = System.Windows.Input.MouseEventArgs;
using WinPieGestures.Plugins;

namespace WinPieGestures;

public partial class ActionPaletteDock : UserControl
{
    internal sealed record Entry(string Kind, string Label, string Glyph)
    {
        internal const string MacroId = "starpie.plugin.macro";
        internal bool Available => Kind switch
        {
            "AddChild" or "Hotkey" => true,
            "WindowManager" => ActionTypeCatalog.GetPreferredWindowManagerType() != null,
            "Macro" => PluginHost.IsMenuActionAvailable(NewMacro()),
            "Plugin" => ActionTypeCatalog.ShouldShowPluginActionType(),
            _ => PluginHost.IsOfficialClaimedType(Kind) && PluginHost.IsClaimedTypeAvailable(Kind, out _)
        };

        private static ActionItem NewMacro() => new()
        {
            Type = PluginActionBinding.TypeName,
            PluginActionRef = new() { PluginId = MacroId, ContributionId = "macro" }
        };

        internal ActionItem CreateAction()
        {
            var item = Kind == "Macro" ? NewMacro() : new ActionItem
            {
                Type = Kind == "WindowManager" ? ActionTypeCatalog.GetPreferredWindowManagerType()! :
                    Kind == "AddChild" ? "Hotkey" : Kind
            };
            item.Name = I18n.T(Label);
            if (item.Type == "Folder") item.IconKey = "Folder";
            if (item.Type == "WebUrl") item.IconKey = "Globe";
            return item;
        }
    }

    internal static IReadOnlyList<Entry> Entries { get; } =
    [
        new("AddChild", "StudioAddChild", "\uE710"), new("Hotkey", "ActionTypeHotkeyShort", "\uE765"),
        new("Launch", "ActionTypeLaunchShort", "\uE768"), new("Folder", "ActionTypeFolderShort", "\uE838"),
        new("WebUrl", "ActionTypeWebUrlShort", "\uE774"), new("Command", "ActionTypeCommandShort", "\uE756"),
        new("WindowManager", "ActionTypeWindowManagerShort", "\uE8A7"), new("System", "ActionTypeSystemShort", "\uE713"),
        new("Macro", "StudioWorkflow", "\uE8EF"), new("Plugin", "ActionTypePluginShort", "\uE74C")
    ];

    internal event Action<Entry>? Activated;
    internal event Action<Entry>? DragStarted;
    internal event Action<Entry, WpfMouseArgs, bool>? DragPosition;
    internal event Action? DragEnded;
    private Button? _pressed;
    private Point _start;
    private bool _dragging;

    public ActionPaletteDock()
    {
        InitializeComponent();
        foreach (var entry in Entries)
        {
            var body = new StackPanel { Orientation = Orientation.Horizontal };
            body.Children.Add(new TextBlock { Text = entry.Glyph, FontFamily = new("Segoe MDL2 Assets"),
                FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) });
            body.Children.Add(new TextBlock());
            var button = new Button { Content = body, Tag = entry, Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(2), Cursor = Cursors.Hand, MinHeight = 27 };
            button.SetResourceReference(StyleProperty, "ModernButtonStyle");
            AutomationProperties.SetAutomationId(button, "StudioPalette" + entry.Kind);
            ToolTipService.SetShowOnDisabled(button, true);
            button.Click += (_, _) => Activated?.Invoke(entry);
            button.PreviewMouseLeftButtonDown += (_, e) =>
            {
                if (!entry.Available) return;
                button.Focus();
                if (!CaptureMouse()) return;
                _pressed = button;
                _start = e.GetPosition(this);
                e.Handled = true;
            };
            EntryPanel.Children.Add(button);
        }
        Loaded += (_, _) => RefreshEntries();
        Unloaded += (_, _) => CancelGesture();
        IsVisibleChanged += (_, _) => { if (!IsVisible) CancelGesture(); };
        RefreshEntries();
    }

    internal void SetCompact(bool compact)
    {
        DockTitle.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        foreach (Button button in EntryPanel.Children)
        {
            var items = ((StackPanel)button.Content).Children;
            items[1].Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            ((FrameworkElement)items[0]).Margin = new Thickness(0, 0, compact ? 0 : 5, 0);
            button.Padding = compact ? new Thickness(5, 2, 5, 2) : new Thickness(8, 3, 8, 3);
        }
    }

    public void RefreshEntries()
    {
        DockTitle.Text = I18n.T("StudioPaletteTitle");
        foreach (Button button in EntryPanel.Children)
        {
            var entry = (Entry)button.Tag;
            string caption = I18n.T(entry.Label);
            ((TextBlock)((StackPanel)button.Content).Children[1]).Text = caption;
            AutomationProperties.SetName(button, caption);
            button.IsEnabled = entry.Available;
            button.ToolTip = caption + " · " + I18n.T(button.IsEnabled ? "StudioPaletteHint" : "StudioProviderUnavailable");
        }
    }

    protected override void OnPreviewMouseMove(WpfMouseArgs e)
    {
        base.OnPreviewMouseMove(e);
        if (_pressed?.Tag is not Entry entry) return;
        if (e.LeftButton != MouseButtonState.Pressed) { CancelGesture(); return; }
        Vector distance = e.GetPosition(this) - _start;
        if (!_dragging)
        {
            if (Math.Abs(distance.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(distance.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _dragging = true;
            DragStarted?.Invoke(entry);
        }
        DragPosition?.Invoke(entry, e, false);
        e.Handled = true;
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        if (_pressed?.Tag is not Entry entry) return;
        bool click = !_dragging && new Rect(_pressed.RenderSize).Contains(e.GetPosition(_pressed));
        try
        {
            if (_dragging) DragPosition?.Invoke(entry, e, true);
            else if (click) Activated?.Invoke(entry);
        }
        finally { CancelGesture(); }
        e.Handled = true;
    }

    internal bool CancelGesture()
    {
        if (_pressed == null) return false;
        bool wasDragging = _dragging;
        _pressed = null;
        _dragging = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (wasDragging) DragEnded?.Invoke();
        return true;
    }

    protected override void OnLostMouseCapture(WpfMouseArgs e)
    {
        base.OnLostMouseCapture(e);
        CancelGesture();
    }
}
