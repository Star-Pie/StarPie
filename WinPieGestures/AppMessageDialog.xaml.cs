using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WinPieGestures;

internal partial class AppMessageDialog : Window
{
    private readonly MessageBoxButton buttons;
    private bool answered;
    public MessageBoxResult Response { get; private set; }

    public AppMessageDialog(string message, string caption, MessageBoxButton buttons, MessageBoxImage icon, MessageBoxResult defaultResult)
    {
        this.buttons = buttons;
        Response = buttons == MessageBoxButton.OK ? MessageBoxResult.OK : MessageBoxResult.Cancel;
        InitializeComponent();
        Title = caption;
        MessageText.Text = message;
        MessageScroll.MaxHeight = Math.Min(420, SystemParameters.WorkArea.Height * .6);
        AppThemeManager.ApplyTheme(this, ConfigManager.CurrentConfig?.AppTheme ?? "System");
        HostWindowFrame.SetCloseEnabled(this, buttons != MessageBoxButton.YesNo);
        var results = buttons switch
        {
            MessageBoxButton.OKCancel => new[] { MessageBoxResult.OK, MessageBoxResult.Cancel },
            MessageBoxButton.YesNo => new[] { MessageBoxResult.Yes, MessageBoxResult.No },
            MessageBoxButton.YesNoCancel => new[] { MessageBoxResult.Yes, MessageBoxResult.No, MessageBoxResult.Cancel },
            _ => new[] { MessageBoxResult.OK }
        };
        if (!results.Contains(defaultResult)) defaultResult = results[0];
        foreach (var result in results)
        {
            var button = new Button { Content = I18n.T(result switch { MessageBoxResult.Yes => "PromptYes", MessageBoxResult.No => "PromptNo", MessageBoxResult.Cancel => "BtnCancel", _ => "BtnOk" }),
                Style = (Style)FindResource("PromptButton"), IsDefault = result == defaultResult,
                IsCancel = result == MessageBoxResult.Cancel || buttons == MessageBoxButton.OK };
            if (result == defaultResult)
            {
                button.SetResourceReference(Control.BackgroundProperty, "AccentPrimaryBrush");
                button.SetResourceReference(Control.ForegroundProperty, "AccentTextBrush");
            }
            button.Click += (_, _) => Complete(result);
            ResultButtons.Children.Add(button);
        }
        string? geometry = icon switch
        {
            MessageBoxImage.Error => "M 2,2 L 14,14 M 14,2 L 2,14",
            MessageBoxImage.Warning => "M 8,0 L 16,15 L 0,15 Z M 8,4 L 8,10 M 8,12 L 8,13",
            MessageBoxImage.Question => "M 3,4 C 3,-1 13,-1 13,4 C 13,7 8,6 8,10 M 8,13 L 8,14",
            MessageBoxImage.Information => "M 8,0 L 8,2 M 8,5 L 8,15",
            _ => null
        };
        StatusIconFrame.Visibility = geometry == null ? Visibility.Collapsed : Visibility.Visible;
        if (geometry != null) { var data = Geometry.Parse(geometry); data.Freeze(); StatusIcon.Data = data; }
        Closing += Dialog_Closing;
    }

    private void Dialog_Closing(object? sender, CancelEventArgs e)
    {
        if (!answered && buttons == MessageBoxButton.YesNo) e.Cancel = true;
    }

    private void Complete(MessageBoxResult result)
    {
        Response = result;
        answered = true;
        if (IsVisible) DialogResult = true;
        else Close();
    }
}

internal static class AppMessageBox
{
    public static MessageBoxResult Show(string message, string caption = "", MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.None, MessageBoxResult defaultResult = MessageBoxResult.None)
        => Show(null, message, caption, buttons, icon, defaultResult);

    public static MessageBoxResult Show(Window? owner, string message, string caption = "", MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.None, MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        var app = Application.Current;
        if (app == null) return owner == null ? MessageBox.Show(message, caption, buttons, icon, defaultResult) : MessageBox.Show(owner, message, caption, buttons, icon, defaultResult);
        if (!app.Dispatcher.CheckAccess()) return app.Dispatcher.Invoke(() => Show(owner, message, caption, buttons, icon, defaultResult));
        owner ??= app.Windows.Cast<Window>().FirstOrDefault(window => window.IsActive && window.IsVisible && !window.AllowsTransparency);
        var dialog = new AppMessageDialog(message, caption, buttons, icon, defaultResult);
        if (owner != null && owner.IsVisible) dialog.Owner = owner;
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dialog.ShowDialog();
        return dialog.Response;
    }
}
