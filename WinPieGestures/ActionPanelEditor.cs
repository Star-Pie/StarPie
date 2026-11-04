using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ListBox = System.Windows.Controls.ListBox;
using DataObject = System.Windows.DataObject;
using DragDropEffects = System.Windows.DragDropEffects;

namespace WinPieGestures;

internal sealed class ActionPanelEditor : Window
{
    private const string DragFormat = "StarPie.HostActionPanel.Key";
    private sealed record KeyDrag(Guid Owner, string Scheme, long Revision, int Slot);
    private readonly Guid _owner = Guid.NewGuid();
    private readonly List<ActionPanelScheme> _schemes;
    private readonly bool _invalidSource;
    private readonly ListBox _list = new() { DisplayMemberPath = nameof(ActionPanelScheme.Name) };
    private readonly UniformGrid _canvas = new();
    private readonly TextBox _name = new();
    private readonly TextBox _rows = new() { Width = 42 };
    private readonly TextBox _columns = new() { Width = 42 };
    private readonly HotkeyRecorderBox _hotkey = new() { MinHeight = 32 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 10) };
    private readonly TextBlock _selection = new();
    private ActionItem? _clipboard;
    private int _selected;
    private long _revision;
    private bool _refreshing;
    private Point _dragStart;
    internal string? SelectedSchemeId { get; private set; }
    private ActionPanelScheme? Current => _list.SelectedItem as ActionPanelScheme;

    internal ActionPanelEditor(string? selectedId)
    {
        Title = ActionPanelUi.T("Title"); Width = 1060; Height = 720; MinWidth = 820; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ActionPanelUi.Theme(this);
        var source = ConfigManager.CurrentConfig.ActionPanels;
        _invalidSource = !ActionPanelScheme.Validate(source);
        _schemes = _invalidSource ? new() : source.Select(item => item.Clone()).ToList();
        if (_schemes.Count == 0) _schemes.Add(new() { Name = ActionPanelUi.T("DefaultName") });
        var root = new DockPanel { Margin = new Thickness(20) };
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var save = ActionPanelUi.Button(I18n.T("InputPanelSave"), Save); save.Name = "SaveActionPanels"; save.IsEnabled = !_invalidSource;
        footer.Children.Add(save);
        footer.Children.Add(ActionPanelUi.Button(I18n.T("InputPanelCancel"), Close));
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        DockPanel.SetDock(_status, Dock.Bottom); root.Children.Add(_status);
        var body = new Grid(); body.ColumnDefinitions.Add(new() { Width = new GridLength(180) });
        body.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new() { Width = new GridLength(235) }); root.Children.Add(body);
        var left = new DockPanel { Margin = new Thickness(0, 0, 14, 0) }; body.Children.Add(left);
        var tools = new WrapPanel();
        tools.Children.Add(ActionPanelUi.Button(ActionPanelUi.T("Add"), () =>
        { if (_schemes.Count < 32) { var added = new ActionPanelScheme { Name = UniqueName(ActionPanelUi.T("DefaultName")) }; _schemes.Add(added); Reload(added.Id); } }));
        tools.Children.Add(ActionPanelUi.Button(ActionPanelUi.T("Duplicate"), () =>
        { if (Current is { } current && _schemes.Count < 32) { var copy = current.Clone(); copy.Id = Guid.NewGuid().ToString("N"); copy.Hotkey = ""; copy.Name = UniqueName(current.Name); _schemes.Add(copy); Reload(copy.Id); } }));
        tools.Children.Add(ActionPanelUi.Button(ActionPanelUi.T("Delete"), () =>
        { if (Current is { } current && _schemes.Count > 1) { _schemes.Remove(current); Reload(_schemes[0].Id); } }));
        tools.Children.Add(ActionPanelUi.Button(ActionPanelUi.T("ImportLegacy"), ImportLegacy));
        DockPanel.SetDock(tools, Dock.Bottom); left.Children.Add(tools); left.Children.Add(_list);
        var center = new DockPanel { Margin = new Thickness(0, 0, 14, 0) }; Grid.SetColumn(center, 1); body.Children.Add(center);
        var hint = new TextBlock { Text = ActionPanelUi.T("CanvasHint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(hint, Dock.Top); center.Children.Add(hint);
        center.Children.Add(_canvas);
        var inspector = new StackPanel(); Grid.SetColumn(inspector, 2); body.Children.Add(inspector);
        inspector.Children.Add(new TextBlock { Text = ActionPanelUi.T("Name") }); inspector.Children.Add(_name);
        inspector.Children.Add(new TextBlock { Text = ActionPanelUi.T("Dimensions"), Margin = new Thickness(0, 14, 0, 6) });
        var dimensions = new WrapPanel(); dimensions.Children.Add(_rows); dimensions.Children.Add(new TextBlock { Text = " × ", VerticalAlignment = VerticalAlignment.Center }); dimensions.Children.Add(_columns);
        dimensions.Children.Add(ActionPanelUi.Button(ActionPanelUi.T("ApplySize"), () =>
        {
            if (Current == null || !int.TryParse(_rows.Text, out int rows) || !int.TryParse(_columns.Text, out int columns) || !Current.TryResize(rows, columns))
            { _status.Text = ActionPanelUi.T("ResizeBlocked"); return; }
            _selected = Math.Min(_selected, Current.Keys.Count - 1); _revision++; Render();
        })); inspector.Children.Add(dimensions);
        inspector.Children.Add(new TextBlock { Text = ActionPanelUi.T("Hotkey"), Margin = new Thickness(0, 14, 0, 6) }); inspector.Children.Add(_hotkey);
        inspector.Children.Add(ActionPanelUi.Button(ActionPanelUi.T("ClearHotkey"), () => { if(Current != null) { Current.Hotkey = ""; _hotkey.HotkeyText = ""; } }));
        inspector.Children.Add(_selection);
        inspector.Children.Add(ActionPanelUi.Button(ActionPanelUi.T("EditAction"), EditAction));
        inspector.Children.Add(ActionPanelUi.Button(ActionPanelUi.T("Copy"), () => _clipboard = Current?.Keys[_selected]?.Clone()));
        inspector.Children.Add(ActionPanelUi.Button(ActionPanelUi.T("Paste"), () => { if (Current != null && _clipboard != null) { Current.Keys[_selected] = _clipboard.Clone(); _revision++; Render(); } }));
        inspector.Children.Add(ActionPanelUi.Button(ActionPanelUi.T("Clear"), () => { if (Current != null) { Current.Keys[_selected] = null; _revision++; Render(); } }));
        _name.TextChanged += (_, _) => { if (!_refreshing && Current != null) { Current.Name = _name.Text; _list.Items.Refresh(); } };
        _hotkey.HotkeyChanged += (_, value) => { if (!_refreshing && Current != null) Current.Hotkey = value ?? ""; };
        _list.SelectionChanged += (_, _) => Refresh(); Content = root;
        if (_invalidSource) { body.IsEnabled = false; _status.Text = ActionPanelUi.T("Unreadable"); }
        Reload(selectedId ?? _schemes[0].Id);
    }
    private void Reload(string id)
    { _revision++; _list.ItemsSource = null; _list.ItemsSource = _schemes; _list.SelectedItem = _schemes.FirstOrDefault(item => item.Id == id) ?? _schemes[0]; }
    private string UniqueName(string stem)
    {
        if (stem.Length > 54) stem = stem[..54];
        for (int i = 1; ; i++) { string name = stem + " " + i; if (!_schemes.Any(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return name; }
    }
    private void Refresh()
    {
        if (Current == null) return;
        _refreshing = true; _name.Text = Current.Name; _rows.Text = Current.Rows.ToString(CultureInfo.InvariantCulture);
        _columns.Text = Current.Columns.ToString(CultureInfo.InvariantCulture); _hotkey.HotkeyText = Current.Hotkey; _refreshing = false;
        _selected = 0; _revision++; Render();
    }
    private void Render()
    {
        _canvas.Children.Clear(); if (Current is not { } scheme) return;
        _canvas.Rows = scheme.Rows; _canvas.Columns = scheme.Columns;
        for (int index = 0; index < scheme.Keys.Count; index++)
        {
            int slot = index;
            var button = ActionPanelUi.Button("", () => { _selected = slot; Render(); });
            button.Content = ActionPanelUi.Caption(scheme.Keys[index]?.Name ?? ActionPanelUi.T("Empty"));
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.AllowDrop = true;
            if (slot == _selected) button.SetResourceReference(Control.BorderBrushProperty, "AccentPrimaryBrush");
            button.PreviewMouseLeftButtonDown += (_, e) => _dragStart = e.GetPosition(this);
            button.PreviewMouseMove += (_, e) =>
            {
                Point point = e.GetPosition(this);
                if (e.LeftButton != MouseButtonState.Pressed ||
                    (Math.Abs(point.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)) return;
                var payload = new KeyDrag(_owner, scheme.Id, _revision, slot);
                DragDrop.DoDragDrop(button, new DataObject(DragFormat, payload), DragDropEffects.Move); e.Handled = true;
            };
            button.Drop += (_, e) =>
            {
                if (e.Data.GetData(DragFormat) is KeyDrag drag && drag.Owner == _owner && drag.Scheme == scheme.Id && drag.Revision == _revision && drag.Slot != slot)
                { (scheme.Keys[slot], scheme.Keys[drag.Slot]) = (scheme.Keys[drag.Slot], scheme.Keys[slot]); _selected = slot; _revision++; Render(); }
                e.Handled = true;
            };
            button.MouseDoubleClick += (_, _) => { _selected = slot; EditAction(); };
            _canvas.Children.Add(button);
        }
        _selection.Text = ActionPanelUi.T("Selected") + " " + (_selected + 1).ToString(CultureInfo.InvariantCulture);
    }
    private void EditAction()
    {
        if (Current == null) return;
        var editor = new ActionPanelActionEditor(Current.Keys[_selected]) { Owner = this };
        if (editor.ShowDialog() == true) { Current.Keys[_selected] = editor.Result; _revision++; Render(); }
    }
    private void Save()
    {
        if (_invalidSource) return;
        if (!ActionPanelScheme.Validate(_schemes) || _schemes.Any(scheme => !string.IsNullOrWhiteSpace(scheme.Hotkey) &&
            ActionPanelHotkey.TryParse(scheme.Hotkey, out uint key, out var mods) && ActionPanelHotkey.ConflictsWithWheels(key, mods, ConfigManager.CurrentConfig)))
        { _status.Text = ActionPanelUi.T("Invalid"); return; }
        var previous = ConfigManager.CurrentConfig.ActionPanels;
        ConfigManager.CurrentConfig.ActionPanels = _schemes.Select(item => item.Clone()).ToList();
        if (!ConfigManager.SaveConfig())
        { ConfigManager.CurrentConfig.ActionPanels = previous; ConfigManager.MarkConfigurationChanged(); _status.Text = ActionPanelUi.T("SaveFailed"); return; }
        SelectedSchemeId = Current?.Id; DialogResult = true;
    }
    private void ImportLegacy()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "JSON (*.json)|*.json", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 1024 * 1024) throw new InvalidDataException();
            using var document = JsonDocument.Parse(File.ReadAllBytes(dialog.FileName));
            var imported = new List<ActionPanelScheme>();
            foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
            {
                var scheme = new ActionPanelScheme { Name = item.GetProperty("name").GetString() ?? "" };
                int slot = 0;
                foreach (var key in item.GetProperty("keys").EnumerateArray())
                {
                    if (slot >= 12) throw new InvalidDataException();
                    if (key.ValueKind != JsonValueKind.Null)
                    {
                        string fullId = key.GetProperty("actionId").GetString() ?? "";
                        var binding = new StarPie.Plugin.ConfiguredPluginAction { ActionId = fullId,
                            Parameters = key.GetProperty("parameters").Deserialize<Dictionary<string, string>>() ?? new() };
                        var action = Plugins.PluginInputPanelService.BuildAction(binding) ?? throw new InvalidDataException();
                        action.Name = key.GetProperty("label").GetString() ?? fullId; scheme.Keys[slot] = action;
                    }
                    slot++;
                }
                imported.Add(scheme);
            }
            var proposed = _schemes.Concat(imported).ToList();
            if (!ActionPanelScheme.Validate(proposed)) throw new InvalidDataException();
            _schemes.AddRange(imported); Reload(_schemes.Last().Id); _status.Text = ActionPanelUi.T("ImportReady");
        }
        catch { _status.Text = ActionPanelUi.T("Invalid"); }
    }
}
