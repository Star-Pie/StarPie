using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using StarPie.Plugin;
using WinPieGestures.Plugins;

namespace WinPieGestures;

internal enum ActionPanelPathPicker { Library, RunningWindow, File }
internal sealed record ActionPanelPathSelection(string Path, string Name);

internal sealed class ActionPanelActionEditor : Window
{
    private readonly ActionItem _item;
    private readonly StackPanel _fields = new();
    private readonly StackPanel _parameters = new();
    private readonly TextBox _name = new() { MinHeight = 32, MaxLength = 128, Margin = new Thickness(0, 6, 0, 12) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly PluginParameterForm _form;
    private readonly Func<Window, ActionPanelPathPicker, ActionPanelPathSelection?> _pickPath;
    private readonly Func<ActionItem, Task<PluginActionValidation>> _validate;
    private readonly Action<ActionItem> _enqueue;
    private readonly Func<Window, string, ShellToolItem?> _pickShellTool;
    private bool _busy, _closed;
    internal ActionItem? Result { get; private set; }

    internal ActionPanelActionEditor(ActionItem? original,
        Func<Window, ActionPanelPathPicker, ActionPanelPathSelection?>? pickPath = null,
        Func<ActionItem, Task<PluginActionValidation>>? validate = null, Action<ActionItem>? enqueue = null,
        Func<Window, string, ShellToolItem?>? pickShellTool = null)
    {
        _item = original?.Clone() ?? new ActionItem { Name = "", Type = "Hotkey", Parameter = "" };
        _item.ExtensionData ??= new();
        _pickPath = pickPath ?? PickPath;
        _validate = validate ?? (action => Task.Run(() => PluginHost.ValidateActionParameters(action)));
        _enqueue = enqueue ?? ActionExecutor.ExecuteForTesting;
        _pickShellTool = pickShellTool ?? PickShellTool;
        Title = ActionPanelUi.T("EditAction"); Width = 760; Height = 580; MinWidth = 520; MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ActionPanelUi.Theme(this);
        var root = new DockPanel { Margin = new Thickness(20) };
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        footer.Children.Add(ActionPanelUi.Button(I18n.T("InputPanelSave"), async () => await SubmitAsync(save: true)));
        footer.Children.Add(ActionPanelUi.Button(I18n.T("InputPanelCancel"), Close));
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var body = new StackPanel(); root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        body.Children.Add(new TextBlock { Text = ActionPanelUi.T("EditAction"), FontSize = 20, Margin = new Thickness(0, 0, 0, 12) });
        body.Children.Add(new TextBlock { Text = ActionPanelUi.T("Label") });
        _name.Text = _item.Name;
        _name.TextChanged += (_, _) => _item.Name = _name.Text; body.Children.Add(_name);
        var types = ActionTypeCatalog.BuildLocalizedActionTypes().Where(item => item.Tag != "ActionPanel").ToList();
        if (!types.Any(item => item.Tag == _item.Type)) types.Add(new() { Tag = _item.Type, DisplayText = _item.Type });
        body.Children.Add(new TextBlock { Text = I18n.T("FocusActionTypeLabel"), Margin = new Thickness(0, 0, 0, 6) });
        var commands = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        commands.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        commands.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var type = new ComboBox { ItemsSource = types, DisplayMemberPath = "DisplayText", SelectedValuePath = "Tag", SelectedValue = _item.Type, MinHeight = 32 };
        commands.Children.Add(type);
        var test = ActionPanelUi.Button(I18n.T("FocusTestActionBtnText"), async () => await TestActionAsync());
        test.Name = "TestPanelAction"; Grid.SetColumn(test, 1); commands.Children.Add(test);
        body.Children.Add(commands); body.Children.Add(_fields); body.Children.Add(_status);
        _form = new PluginParameterForm(_parameters, () => new ContributedActionParameterTarget(_item), () => { }, false);
        type.SelectionChanged += (_, _) =>
        {
            if (type.SelectedValue is not string value || value == _item.Type) return;
            _item.Type = value; _item.Parameter = ""; _item.ExtensionData?.Clear(); _item.PluginActionRef = null; Build();
        };
        Content = root; Build(); Closed += (_, _) => { _closed = true; _form.Reset(); };
    }
    private void Build()
    {
        _form.Reset(); _fields.Children.Clear(); _fields.Children.Add(_parameters); _status.Text = "";
        if (_item.Type == "Hotkey")
        {
            var recorder = new HotkeyRecorderBox { HotkeyText = _item.Parameter, MinHeight = 34, Margin = new Thickness(0, 14, 0, 0) };
            recorder.HotkeyChanged += (_, value) => _item.Parameter = value ?? ""; _fields.Children.Add(recorder);
            var builder = ActionPanelUi.Button(I18n.T("FocusHotkeyBuilderBtnText"), () =>
            {
                var dialog = new HotkeyBuilderDialog(_item.Parameter) { Owner = this };
                if (dialog.ShowDialog() == true && !_closed) recorder.HotkeyText = dialog.ResultHotkey;
            });
            builder.HorizontalAlignment = HorizontalAlignment.Left; _fields.Children.Add(builder);
        }
        else if (_item.Type is "Text" or "String")
        {
            var text = new TextBox { Text = _item.Parameter, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                MinHeight = 100, MaxLength = PluginApi.MaxParameterValueLength, Margin = new Thickness(0, 14, 0, 0) };
            text.TextChanged += (_, _) => _item.Parameter = text.Text; _fields.Children.Add(text);
        }
        else if (_item.Type == PluginActionBinding.TypeName)
        {
            _status.Text = _item.PluginActionRef?.FullId ?? ActionPanelUi.T("Empty");
            _fields.Children.Add(ActionPanelUi.Button(ActionPanelUi.T("EditAction"), () =>
            {
                var initial = _item.PluginActionRef == null ? null : new ConfiguredPluginAction
                { ActionId = _item.PluginActionRef.FullId, Parameters = _item.ExtensionData };
                var picker = new PluginActionBindingDialog("", initial, 0) { Owner = this };
                if (picker.ShowDialog() == true && picker.Result != null)
                {
                    var built = PluginInputPanelService.BuildAction(picker.Result)!;
                    _item.PluginActionRef = built.PluginActionRef; _item.ExtensionData = built.ExtensionData;
                    _status.Text = built.PluginActionRef!.FullId;
                }
            }));
        }
        else if (PluginHost.TryGetClaimedEditorRegistration(_item.Type, out var registration))
            BuildClaimedFields(registration);
        else _status.Text = I18n.T("InputPanelActionUnavailable");
    }
    private void BuildClaimedFields(PluginActionRegistration registration)
    {
        if (_item.Type == "ShellTool") { BuildShellToolFields(registration); return; }
        var path = registration.Parameters.FirstOrDefault(field => field.Key == HostActionFields.Parameter && field.Type == ParameterFieldType.File);
        if (_item.Type != "Launch" || path == null) { _form.Build(registration.Parameters, registration.PluginId); return; }

        var launch = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        launch.Children.Add(new TextBlock { Text = PluginI18n.ResolveLabel(registration.PluginId, path.LabelKey, path.Label) + (path.Required ? " *" : "") });
        string current = ActionParameterProjection.Project(_item).GetValueOrDefault(HostActionFields.Parameter, "");
        var box = new TextBox { Text = current, MinHeight = 32, Margin = new Thickness(0, 6, 0, 4), Name = "PanelLaunchPath" };
        box.TextChanged += (_, _) =>
        {
            // Keep the legacy fallback in sync, including an explicitly cleared path.
            _item.Parameter = box.Text;
            new ContributedActionParameterTarget(_item).Write(HostActionFields.Parameter, box.Text);
        };
        launch.Children.Add(box);
        var tools = new WrapPanel();
        void AddPicker(string key, ActionPanelPathPicker source)
        {
            var button = ActionPanelUi.Button(I18n.T(key), () =>
            {
                try
                {
                    var selected = _pickPath(this, source);
                    if (_closed || selected == null || string.IsNullOrWhiteSpace(selected.Path)) return;
                    box.Text = selected.Path; _item.InheritAppIconPath = selected.Path;
                    if (string.IsNullOrWhiteSpace(_item.Name) || ActionNameDefaults.IsAutoFilled(_item.Name))
                        _name.Text = string.IsNullOrWhiteSpace(selected.Name) ? System.IO.Path.GetFileNameWithoutExtension(selected.Path) : selected.Name;
                }
                catch (Exception ex) { _status.Text = ActionPanelUi.T("Invalid"); AppLogger.LogError("Action panel path selection failed", ex); }
            });
            button.Name = "PanelPath" + source; tools.Children.Add(button);
        }
        AddPicker("FocusLaunchPickProgramBtnText", ActionPanelPathPicker.Library);
        AddPicker("FocusLaunchCaptureWindowBtnText", ActionPanelPathPicker.RunningWindow);
        AddPicker("FocusLaunchBrowseExeBtnText", ActionPanelPathPicker.File);
        launch.Children.Add(tools);
        string? help = PluginI18n.Resolve(registration.PluginId, path.HelpTextKey) ?? path.HelpText;
        if (!string.IsNullOrWhiteSpace(help))
        {
            var hint = new TextBlock { Text = help, TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 4, 0, 0) };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush"); launch.Children.Add(hint);
        }
        _fields.Children.Insert(0, launch);
        _form.Build(registration.Parameters.Where(field => !ReferenceEquals(field, path)).ToArray(), registration.PluginId);
    }
    private void BuildShellToolFields(PluginActionRegistration registration)
    {
        var field = registration.Parameters.FirstOrDefault(parameter => parameter.Key == HostActionFields.Parameter);
        if (field == null) { _form.Build(registration.Parameters, registration.PluginId); return; }
        var summary = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        summary.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        summary.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var title = new TextBlock { FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Name = "PanelShellToolTitle" };
        var description = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 4, 0, 0) };
        description.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        var icon = new TextBlock { FontFamily = new System.Windows.Media.FontFamily("Segoe UI Emoji, Segoe UI Symbol, Segoe UI"), FontSize = 22, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "AccentPrimaryBrush");
        var details = new StackPanel(); details.Children.Add(title); details.Children.Add(description);
        var row = new DockPanel(); DockPanel.SetDock(icon, Dock.Left); row.Children.Add(icon); row.Children.Add(details);
        var card = new Border { Child = row, Padding = new Thickness(10), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8) };
        card.SetResourceReference(Border.BackgroundProperty, "SubtleCardBrush"); card.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush"); summary.Children.Add(card);
        string Current() => ActionParameterProjection.Project(_item).GetValueOrDefault(HostActionFields.Parameter, "");
        void Refresh()
        {
            string current = Current();
            var tool = ShellActionPickerWindow.ShellTools.FirstOrDefault(item => item.Id == current || string.Equals(item.Verb, current, StringComparison.OrdinalIgnoreCase));
            title.Text = tool == null ? (string.IsNullOrWhiteSpace(current) ? I18n.T("FocusShellToolDefaultTitle") : current) : $"{tool.Name} ({tool.Id})";
            title.ToolTip = title.Text; description.Text = tool?.Description ?? I18n.T("FocusShellToolDefaultDesc"); icon.Text = tool?.Icon ?? "⚡";
        }
        var pick = ActionPanelUi.Button(I18n.T("FocusPickShellToolBtnText"), () =>
        {
            try
            {
                var tool = _pickShellTool(this, Current());
                if (_closed || tool == null) return;
                _item.Parameter = tool.Id; new ContributedActionParameterTarget(_item).Write(HostActionFields.Parameter, tool.Id);
                _name.Text = tool.Name; _item.IconKey = tool.IconKey; Refresh();
            }
            catch (Exception ex) { _status.Text = ActionPanelUi.T("Invalid"); AppLogger.LogError("Action panel tool selection failed", ex); }
        });
        pick.Name = "PanelPickShellTool"; pick.ToolTip = I18n.T("FocusPickShellToolBtnToolTip");
        pick.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(pick, 1); summary.Children.Add(pick);
        Refresh(); _fields.Children.Insert(0, summary);
        _form.Build(registration.Parameters.Where(parameter => !ReferenceEquals(parameter, field)).ToArray(), registration.PluginId);
    }
    private static ShellToolItem? PickShellTool(Window owner, string parameter)
    {
        var picker = new ShellActionPickerWindow(parameter) { Owner = owner };
        return picker.ShowDialog() == true ? picker.SelectedTool : null;
    }
    private static ActionPanelPathSelection? PickPath(Window owner, ActionPanelPathPicker source)
    {
        if (source == ActionPanelPathPicker.Library)
        {
            var picker = new ProgramPickerWindow { Owner = owner };
            return picker.ShowDialog() == true ? new(picker.SelectedPath, picker.SelectedName) : null;
        }
        if (source == ActionPanelPathPicker.RunningWindow)
        {
            var picker = new WindowPickerWindow(WindowPickerMode.ExecutablePath) { Owner = owner };
            return picker.ShowDialog() == true ? new(picker.SelectedPath, string.IsNullOrWhiteSpace(picker.SelectedTitle) ? picker.SelectedProcessName : picker.SelectedTitle) : null;
        }
        var file = new Microsoft.Win32.OpenFileDialog { Filter = I18n.T("FileDialogFilterExecutable"), Title = I18n.T("FileDialogTitleSelectProgram") };
        return file.ShowDialog(owner) == true ? new(file.FileName, System.IO.Path.GetFileNameWithoutExtension(file.FileName)) : null;
    }
    internal Task TestActionAsync() => SubmitAsync(save: false);
    private async Task SubmitAsync(bool save)
    {
        if (_busy || _closed) return;
        if (_item.Type == "Hotkey" && string.IsNullOrWhiteSpace(_item.Parameter)) { _status.Text = ActionPanelUi.T("Invalid"); return; }
        if (_item.Type == PluginActionBinding.TypeName && _item.PluginActionRef == null) { _status.Text = ActionPanelUi.T("Invalid"); return; }
        var snapshot = _item.Clone();
        _busy = true; ((UIElement)Content).IsEnabled = false;
        try
        {
            var validation = await _validate(snapshot);
            if (_closed) return;
            if (!validation.IsValid) { _status.Text = validation.Describe(); return; }
            if (save) { Result = snapshot; DialogResult = true; }
            else { _enqueue(snapshot); _status.Text = ActionPanelUi.T("TestQueued"); }
        }
        catch (Exception ex) { if (!_closed) _status.Text = ActionPanelUi.T("Invalid"); AppLogger.LogError("Action panel parameter validation failed", ex); }
        finally { _busy = false; if (!_closed) ((UIElement)Content).IsEnabled = true; }
    }
}
