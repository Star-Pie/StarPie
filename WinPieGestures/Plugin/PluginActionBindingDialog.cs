using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using StarPie.Plugin;

namespace WinPieGestures.Plugins;

/// <summary>Small activating editor; configuration only, with the shared parameter form.</summary>
internal sealed class PluginActionBindingDialog : Window
{
    private sealed record PluginChoice(string Id, string Name)
    {
        public override string ToString() => Name;
    }

    private readonly ConfiguredPluginAction? _initial;
    private readonly ComboBox _plugins = new() { MinHeight = 32, Margin = new Thickness(0, 5, 0, 12) };
    private readonly ComboBox _actions = new() { MinHeight = 32, Margin = new Thickness(0, 5, 0, 12), DisplayMemberPath = nameof(PluginActionRegistration.CurrentDisplayName) };
    private readonly StackPanel _fields = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    private readonly Button _save = new() { MinWidth = 100, Padding = new Thickness(12, 6, 12, 6) };
    private readonly PluginParameterForm _form;
    private ActionItem? _editing;
    private int _revision;
    private bool _closed;
    internal ConfiguredPluginAction? Result { get; private set; }

    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    internal PluginActionBindingDialog(string callerId, ConfiguredPluginAction? initial, long ownerWindowHandle)
    {
        _initial = initial;
        Title = I18n.T("InputPanelChooseAction");
        Width = 560; Height = 530; MinWidth = 400; MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        AppThemeManager.ApplyTheme(this, AppThemeManager.CurrentEffectiveTheme);
        SetResourceReference(BackgroundProperty, "WindowBackgroundBrush");
        SetResourceReference(ForegroundProperty, "TextPrimaryBrush");
        foreach (Type type in new[] { typeof(Button), typeof(ComboBox), typeof(TextBox) })
        {
            string? key = type == typeof(Button) ? "ModernButtonStyle" : type == typeof(ComboBox) ? "FlatComboBoxStyle" : null;
            var style = new Style(type, (key != null ? TryFindResource(key) : TryFindResource(type)) as Style);
            style.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("TextPrimaryBrush")));
            style.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("InputBackgroundBrush")));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("InputBorderBrush")));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            Resources[type] = style;
        }
        var root = new DockPanel { Margin = new Thickness(20) };
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = I18n.T("InputPanelCancel"), MinWidth = 88, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(12, 6, 12, 6), IsCancel = true };
        _save.Content = I18n.T("InputPanelSave"); _save.IsEnabled = false;
        _save.Click += SaveClicked;
        footer.Children.Add(_save); footer.Children.Add(cancel);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = I18n.T("InputPanelChooseAction"), FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        body.Children.Add(new TextBlock { Text = I18n.T("InputPanelPickerHint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) });
        var selectors = new StackPanel();
        selectors.Children.Add(new TextBlock { Text = I18n.T("InputPanelPluginLabel") }); selectors.Children.Add(_plugins);
        selectors.Children.Add(new TextBlock { Text = I18n.T("InputPanelActionLabel") }); selectors.Children.Add(_actions);
        selectors.Children.Add(_fields);
        body.Children.Add(selectors); body.Children.Add(_status);
        root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
        _form = new PluginParameterForm(_fields, () => _editing == null ? null : new ActionItemParameterTarget(_editing), () => { }, includeSettingsShortcut: false);
        var choices = PluginHost.ListInstances().Where(instance => instance.Entry.Enabled &&
                !instance.PluginId.Equals(callerId, StringComparison.OrdinalIgnoreCase))
            .Select(instance => new PluginChoice(instance.PluginId, instance.Entry.Name)).ToArray();
        _plugins.ItemsSource = choices;
        _plugins.IsEnabled = choices.Length > 0;
        _actions.IsEnabled = false;
        if (choices.Length == 0)
        {
            selectors.Visibility = Visibility.Collapsed;
            _status.Text = I18n.T("InputPanelNoOtherPlugins");
            Height = 360;
        }
        _plugins.SelectionChanged += PluginSelected;
        _actions.SelectionChanged += ActionSelected;
        Closed += (_, _) => { _closed = true; _revision++; _form.Reset(); _plugins.ItemsSource = null; _actions.ItemsSource = null; };
        if (ownerWindowHandle != 0)
        {
            GetWindowThreadProcessId((nint)ownerWindowHandle, out uint ownerPid);
            if (ownerPid == (uint)Environment.ProcessId) new WindowInteropHelper(this).Owner = (nint)ownerWindowHandle;
        }
        if (choices.Length > 0) _status.Text = I18n.T("InputPanelSelectPlugin");
    }

    private async void PluginSelected(object sender, SelectionChangedEventArgs e)
    {
        int revision = ++_revision;
        _editing = null; _form.Reset(); _actions.ItemsSource = null; _save.IsEnabled = false;
        _actions.IsEnabled = false;
        if (_plugins.SelectedItem is not PluginChoice plugin) return;
        _status.Text = I18n.T("InputPanelLoadingActions");
        try
        {
            var actions = await Task.Run(() => PluginHost.LoadActionsForBinding(plugin.Id));
            if (_closed || revision != _revision) return;
            _actions.ItemsSource = actions;
            _actions.IsEnabled = actions.Count > 0;
            _status.Text = actions.Count == 0 ? I18n.T("InputPanelActionUnavailable") : "";
            _actions.SelectedItem = actions.FirstOrDefault(item => item.FullId.Equals(_initial?.ActionId, StringComparison.OrdinalIgnoreCase)) ?? actions.FirstOrDefault();
        }
        catch (Exception ex)
        {
            if (!_closed && revision == _revision) _status.Text = I18n.T("InputPanelActionUnavailable");
            AppLogger.LogError("Loading an enabled plugin for the action picker failed", ex);
        }
    }

    private void ActionSelected(object sender, SelectionChangedEventArgs e)
    {
        _editing = null; _form.Reset(); _save.IsEnabled = false;
        if (_actions.SelectedItem is not PluginActionRegistration registration) return;
        Dictionary<string, string>? values = registration.FullId.Equals(_initial?.ActionId, StringComparison.OrdinalIgnoreCase)
            && _initial != null ? new Dictionary<string, string>(_initial.Parameters, StringComparer.OrdinalIgnoreCase) : null;
        _editing = PluginHost.CreateActionItem(registration.FullId, values);
        if (_editing == null) return;
        _form.Build(registration.Parameters, registration.PluginId);
        _save.IsEnabled = true;
    }

    private async void SaveClicked(object sender, RoutedEventArgs e)
    {
        if (_editing?.PluginActionRef == null) return;
        ActionItem snapshot = _editing.Clone();
        int revision = _revision;
        _save.IsEnabled = false; _fields.IsEnabled = _plugins.IsEnabled = _actions.IsEnabled = false;
        try
        {
            PluginActionValidation validation = await Task.Run(() => PluginHost.ValidateActionParameters(snapshot));
            if (_closed || revision != _revision) return;
            _form.ShowIssues(validation.DeclaredIssues);
            if (!validation.IsValid) { _status.Text = validation.Describe(); return; }
            Result = new ConfiguredPluginAction
            {
                ActionId = snapshot.PluginActionRef!.FullId,
                Parameters = snapshot.ExtensionData ?? new Dictionary<string, string>(),
            };
            DialogResult = true;
        }
        catch (Exception ex)
        {
            if (!_closed) _status.Text = I18n.T("InputPanelActionUnavailable");
            AppLogger.LogError("Validating a configured plugin action failed", ex);
        }
        finally
        {
            if (!_closed) { _save.IsEnabled = true; _fields.IsEnabled = _plugins.IsEnabled = _actions.IsEnabled = true; }
        }
    }
}
