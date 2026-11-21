using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using StarPie.Plugin;
using WinPieGestures.Plugins;

namespace WinPieGestures;

public partial class CanvasMenuActionEditor : UserControl
{
    private ActionItem? _draft;
    private Action<ActionItem>? _apply;
    private PluginParameterForm? _form;
    private bool _filling;
    private bool _allowEmptyAction;

    public CanvasMenuActionEditor() => InitializeComponent();

    internal void BindAction(ActionItem? selected, Action<ActionItem> apply, bool allowEmptyAction = false)
    {
        _allowEmptyAction = allowEmptyAction;
        _apply = apply;
        _draft = selected?.Clone();
        RefreshFields();
    }

    private void RefreshFields()
    {
        _filling = true;
        try
        {
            // An old picker or parameter control can return after the canvas has changed selection.
            var previous = _form;
            _form = null;
            previous?.Reset();
            HotkeyBox.IsRecording = false;
            PluginBox.ItemsSource = null;
            PanelBox.ItemsSource = null;
            Fields.Visibility = VisibleIf(_draft != null);
            if (_draft is not { } item) return;

            NameLabel.Text = I18n.T("FocusActionNameLabel");
            TypeLabel.Text = I18n.T("FocusActionTypeLabel");
            PluginLabel.Text = I18n.T("ActionTypePluginShort");
            TextLabel.Text = I18n.T("TextResultTypeText");
            IconCaption.Text = I18n.T("FocusIconLabel");
            IconButton.ToolTip = I18n.T("FocusPickIconButtonToolTip");
            AutomationProperties.SetName(IconButton, IconCaption.Text);
            HotkeyBox.Placeholder = I18n.T("HotkeyPlaceholder");
            AutomationProperties.SetName(HotkeyBox, I18n.T("ActionTypeHotkeyShort"));
            AutomationProperties.SetName(PanelBox, I18n.T("ActionPanelTitle"));
            PlacementBox.Content = I18n.T("LaunchPlacementLabel");
            NameBox.Text = item.Name ?? "";
            RefreshIcon(item);

            string type = DisplayType(item.Type);
            var types = ActionTypeCatalog.BuildLocalizedActionTypes();
            if (_allowEmptyAction) types.Add(new ActionTypeItem { Tag = "None", DisplayText = I18n.T("FixedNoAction") });
            types.Insert(1, new ActionTypeItem { Tag = "Text", DisplayText = TextLabel.Text });
            if (!types.Any(option => option.Tag == type))
                types.Add(new ActionTypeItem { Tag = type, DisplayText = type == PluginActionBinding.TypeName ? PluginLabel.Text : type });
            TypeBox.ItemsSource = types;
            TypeBox.SelectedValue = type;

            TextRow.Visibility = VisibleIf(type == "Text");
            ParameterBox.Text = type == "Text" ? item.Parameter ?? "" : "";
            HotkeyBox.Visibility = VisibleIf(type == "Hotkey");
            HotkeyBox.HotkeyText = type == "Hotkey" ? EffectiveHotkey(item) : "";
            PluginRow.Visibility = VisibleIf(type == PluginActionBinding.TypeName);
            PanelBox.Visibility = VisibleIf(type == "ActionPanel");
            PlacementBox.Visibility = VisibleIf(LaunchMonitorPlacement.IsSupportedAction(item));
            PlacementBox.IsChecked = item.LaunchOnCursorMonitor;
            Unavailable.Visibility = Visibility.Collapsed;

            if (type == "ActionPanel")
            {
                PanelBox.ItemsSource = ConfigManager.CurrentConfig.ActionPanels;
                PanelBox.SelectedValue = item.Parameter;
            }
            else if (type == PluginActionBinding.TypeName)
            {
                string? fullId = item.PluginActionRef?.FullId;
                PluginBox.ItemsSource = PluginActionBinding.BuildPluginActionView(fullId);
                PluginBox.SelectedValue = PluginActionBinding.ProjectSelectedAction(item);
                if (item.PluginActionRef is { IsValid: true } reference && PluginHost.TryGetAction(reference.FullId, out var registration))
                    BuildSchema(registration, new ActionItemParameterTarget(item));
                else if (item.PluginActionRef != null)
                    ShowUnavailable(fullId);
            }
            else if (type is not ("Text" or "Hotkey" or "None"))
            {
                if (PluginHost.TryGetClaimedEditorRegistration(item.Type, out var registration))
                    BuildSchema(registration, new ClaimedFields(item));
                else
                    ShowUnavailable(item.Type);
            }
        }
        finally { _filling = false; }
    }

    private static string DisplayType(string? type) => type switch
    {
        null or "" => "Hotkey",
        "String" => "Text",
        "OpenFolder" => "Folder",
        "ScreenOcr" => "Ocr",
        "Url" => "WebUrl",
        _ => type
    };

    private static Visibility VisibleIf(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private void ShowUnavailable(string? identifier)
    {
        Unavailable.Text = I18n.T("InputPanelActionUnavailable") + " " + identifier;
        Unavailable.Visibility = Visibility.Visible;
    }

    private void BuildSchema(PluginActionRegistration registration, IPluginParameterTarget target)
    {
        PluginParameterForm? form = null;
        form = new PluginParameterForm(SchemaFields, () => ReferenceEquals(_form, form) ? target : null,
            () =>
            {
                if (_filling || !ReferenceEquals(_form, form)) return;
                form!.ShowIssues(form.Validate());
                Publish();
            }, includeSettingsShortcut: false);
        _form = form;
        form.Build(registration.Parameters, registration.PluginId);
        form.ShowIssues(form.Validate());
    }

    private void Publish()
    {
        if (!_filling && _draft != null) _apply?.Invoke(_draft.Clone());
    }

    private void NameChanged(object sender, TextChangedEventArgs e)
    {
        if (_filling || _draft == null || _draft.Name == NameBox.Text) return;
        _draft.Name = NameBox.Text;
        Publish();
    }

    private void TypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || _draft == null || TypeBox.SelectedValue is not string type || type == DisplayType(_draft.Type)) return;
        SettingsWindow.SwitchFocusActionType(_draft, type);
        RefreshFields();
        Publish();
    }

    private void PluginChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || _draft?.Type != PluginActionBinding.TypeName || PluginBox.SelectedValue is not string id ||
            string.Equals(id, _draft.PluginActionRef?.FullId, StringComparison.OrdinalIgnoreCase)) return;
        if (!PluginActionBinding.Apply(_draft, id)) { RefreshFields(); return; }
        RefreshFields();
        Publish();
    }

    private void ParameterChanged(object sender, TextChangedEventArgs e)
    {
        if (_filling || _draft?.Type is not ("Text" or "String") || _draft.Parameter == ParameterBox.Text) return;
        _draft.Parameter = ParameterBox.Text;
        Publish();
    }

    private static string EffectiveHotkey(ActionItem item) =>
        item.ExtensionData?.GetValueOrDefault("hotkey") ?? item.Parameter ?? "";

    private void HotkeyChanged(object? sender, string value)
    {
        if (_filling || _draft == null || DisplayType(_draft.Type) != "Hotkey" || value == EffectiveHotkey(_draft)) return;
        _draft.Parameter = value;
        if (_draft.ExtensionData?.ContainsKey("hotkey") == true)
            new ActionItemParameterTarget(_draft).Write("hotkey", value);
        Publish();
    }

    private void PanelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || _draft?.Type != "ActionPanel" || PanelBox.SelectedValue is not string id || _draft.Parameter == id) return;
        _draft.Parameter = id;
        Publish();
    }

    private void PlacementChanged(object sender, RoutedEventArgs e)
    {
        if (_filling || _draft == null || !LaunchMonitorPlacement.IsSupportedAction(_draft)) return;
        bool value = PlacementBox.IsChecked == true;
        if (value == _draft.LaunchOnCursorMonitor) return;
        _draft.LaunchOnCursorMonitor = value;
        Publish();
    }

    private void PickIcon(object sender, RoutedEventArgs e)
    {
        if (_draft is not { } item) return;
        var picker = new IconPickerWindow(item.IconKey) { Owner = Window.GetWindow(this) };
        if (picker.ShowDialog() != true || !ReferenceEquals(item, _draft)) return;
        string key = picker.SelectedIconKey ?? "";
        if (item.IconKey == key && string.IsNullOrEmpty(item.InheritAppIconPath) && string.IsNullOrEmpty(item.CustomIconSvg)) return;
        item.IconKey = key;
        item.InheritAppIconPath = "";
        item.CustomIconSvg = "";
        RefreshIcon(item);
        Publish();
    }

    private void RefreshIcon(ActionItem item)
    {
        IconPath.Data = null;
        IconImage.Source = string.IsNullOrWhiteSpace(item.InheritAppIconPath) ? null : IconHelper.GetIcon(item.InheritAppIconPath);
        if (IconImage.Source != null) return;
        string? svg = string.IsNullOrEmpty(item.CustomIconSvg) ? IconHelper.GetSvgPathByKey(item.IconKey) : item.CustomIconSvg;
        if (!string.IsNullOrWhiteSpace(svg))
        {
            try { IconPath.Data = Geometry.Parse(svg); }
            catch (FormatException) { /* Retain imported geometry that WPF cannot display. */ }
        }
        if (IconPath.Data == null) IconImage.Source = IconHelper.GetCustomImageSource(item.IconKey, 64);
    }

    // A cleared projected field must clear its original value too, or the next read restores it.
    private sealed class ClaimedFields(ActionItem item) : IPluginParameterTarget
    {
        private readonly ActionItemParameterTarget _extensions = new(item);
        public IReadOnlyDictionary<string, string> Stored => ActionParameterProjection.Project(item);
        public bool ReportsUndeclaredValues => false;

        public bool Write(string key, string? value)
        {
            string text = value ?? "";
            if (Stored.TryGetValue(key, out string? previous) && previous == text) return false;
            switch (key)
            {
                case HostActionFields.Parameter: item.Parameter = text; break;
                case HostActionFields.Arguments: item.Arguments = text; break;
                case HostActionFields.CommandTerminal: item.CommandTerminal = text; break;
                case HostActionFields.BrowserChoice: item.BrowserChoice = text; break;
                case HostActionFields.BrowserPath: item.BrowserPath = text; break;
                case HostActionFields.RunAsStandardUser: item.RunAsStandardUser = string.Equals(text, "true", StringComparison.OrdinalIgnoreCase); break;
                default: return _extensions.Write(key, value);
            }
            if (item.ExtensionData?.ContainsKey(key) == true) _extensions.Write(key, value);
            return true;
        }
    }
}
