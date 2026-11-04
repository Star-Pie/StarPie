using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using StarPie.Plugin;
using WinPieGestures;
using WinPieGestures.Plugins;

internal static partial class Program
{
    private static void ShellToolParity(string sandbox)
    {
        var original = new ActionItem { Type = "ShellTool", Name = "My tool", Parameter = "legacy",
            ExtensionData = new() { [HostActionFields.Parameter] = "Windows.CopyAsPath", ["future"] = "kept", ["ProcessLaunchMode"] = "normal" } };
        string before = JsonSerializer.Serialize(original); int calls = 0; ShellToolItem? selection = null; string? initial = null;
        var editor = new ActionPanelActionEditor(original, pickShellTool: (_, current) => { calls++; initial = current; return selection; });
        var schema = new PluginActionRegistration { PluginId = "fixture.shell", Parameters = new ParameterField[]
        { new() { Key = HostActionFields.Parameter, Type = ParameterFieldType.Text, Label = "Function ID" },
          new() { Key = "ProcessLaunchMode", Type = ParameterFieldType.Text, Label = "Permission" } } };
        typeof(ActionPanelActionEditor).GetMethod("BuildClaimedFields", Fields)!.Invoke(editor, [schema]);
        Layout(editor, 480, 540);
        var button = Descendants(editor.Content!).OfType<Button>().Single(item => item.Name == "PanelPickShellTool");
        var title = Descendants(editor.Content!).OfType<TextBlock>().Single(item => item.Name == "PanelShellToolTitle");
        Check(calls == 0 && title.Text.Contains("copy_path"), "ShellTool resolves legacy Verb and shows selected tool without opening a picker");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var edited = (ActionItem)typeof(ActionPanelActionEditor).GetField("_item", Fields)!.GetValue(editor)!;
        Check(initial == "Windows.CopyAsPath" && edited.Name == "My tool" && edited.Parameter == "legacy", "cancelled tool selection preserves the complete current binding");
        selection = ShellActionPickerWindow.ShellTools.First(item => item.Id == "copy_filename");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(edited.Parameter == selection.Id && ActionParameterProjection.Project(edited)[HostActionFields.Parameter] == selection.Id && title.Text.Contains(selection.Id), "selected tool updates projected target and summary together");
        Check(edited.Name == selection.Name && edited.IconKey == selection.IconKey && edited.ExtensionData!["future"] == "kept" && edited.ExtensionData["ProcessLaunchMode"] == "normal", "tool metadata follows wheel picker while preserving permission and future data");
        Check(Descendants(editor.Content!).OfType<TextBox>().Count() == 2, "raw function-ID textbox is replaced while permission and label remain editable");
        editor.Close(); Check(JsonSerializer.Serialize(original) == before, "closing tool editor never writes the original action");
        foreach (string language in new[] { "zh-CN", "zh-TW", "en", "ja" })
        foreach (string theme in new[] { "Light", "Dark" })
        {
            ConfigManager.CurrentConfig.Language = language; I18n.SetLanguage(language);
            ConfigManager.CurrentConfig.AppTheme = theme; AppThemeManager.ApplyTheme(new Grid(), theme);
            var preview = new ActionPanelActionEditor(original, pickShellTool: (_, _) => null);
            typeof(ActionPanelActionEditor).GetMethod("BuildClaimedFields", Fields)!.Invoke(preview, [schema]);
            Layout(preview, 480, 540);
            var picker = Descendants(preview.Content!).OfType<Button>().Single(item => item.Name == "PanelPickShellTool");
            Check(picker.ActualWidth > 20 && picker.ActualWidth <= 440 && picker.Template != null,
                "tool picker uses a laid-out template within a narrow editor in " + language + "/" + theme);
            if (language == "zh-CN")
            { Layout(preview, 760, 540); Render(preview, System.IO.Path.Combine(sandbox, "shelltool-editor-" + theme + ".png")); }
            preview.Close();
        }
    }
}
