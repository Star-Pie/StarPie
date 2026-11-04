using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace WinPieGestures;

public partial class SettingsWindow
{
    private void RefreshActionPanelSettings(ActionItem item)
    {
        FocusActionPanelHost.Children.Clear();
        FocusActionPanelHost.Visibility = item.Type == "ActionPanel" ? Visibility.Visible : Visibility.Collapsed;
        if (item.Type != "ActionPanel") return;
        var schemes = ConfigManager.CurrentConfig.ActionPanels ?? new();
        var combo = new ComboBox { ItemsSource = schemes, DisplayMemberPath = nameof(ActionPanelScheme.Name),
            SelectedValuePath = nameof(ActionPanelScheme.Id), SelectedValue = item.Parameter, MinHeight = 32 };
        FocusActionPanelHost.Children.Add(new TextBlock { Text = ActionPanelUi.T("ChooseScheme"), TextWrapping = TextWrapping.Wrap });
        FocusActionPanelHost.Children.Add(combo);
        combo.SelectionChanged += (_, _) =>
        {
            if (_isUpdatingUi || combo.SelectedValue is not string id) return;
            var current = GetCurrentFocusActionItem(ensureLocalForEdit: true);
            if (current == null || current.Type != "ActionPanel") return;
            current.Parameter = id; ConfigManager.SaveConfig();
        };
        var manage = ActionPanelUi.Button(ActionPanelUi.T("Manage"), () =>
        {
            var editor = new ActionPanelEditor(item.Parameter) { Owner = this };
            if (editor.ShowDialog() == true)
            {
                var current = GetCurrentFocusActionItem(ensureLocalForEdit: true);
                if (current?.Type == "ActionPanel" && editor.SelectedSchemeId != null)
                { current.Parameter = editor.SelectedSchemeId; ConfigManager.SaveConfig(); RefreshActionPanelSettings(current); }
            }
        });
        manage.HorizontalAlignment = HorizontalAlignment.Left;
        FocusActionPanelHost.Children.Add(manage);
    }
}
