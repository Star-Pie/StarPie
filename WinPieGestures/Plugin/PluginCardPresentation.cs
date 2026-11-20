namespace WinPieGestures.Plugins;

internal static class PluginCardPresentation
{
    // Product-owned symbols keep card rendering local even when a catalogue is cached offline.
    internal static string Glyph(string id) => id.ToLowerInvariant() switch
    {
        "starpie.plugin.quicksearch" => "\uE721",
        "starpie.plugin.cadcommand" => "\uE809",
        "starpie.builtin.folder" => "\uE8B7",
        "starpie.plugin.sound" => "\uE767",
        "starpie.plugin.keypadlayer" or "starpie.plugin.temporarykeyboard" => "\uE765",
        "starpie.plugin.macro" => "\uE768",
        "starpie.builtin.weburl" => "\uE774",
        "starpie.builtin.command" or "starpie.builtin.shelltool" => "\uE756",
        "starpie.builtin.tile" or "starpie.builtin.switchwindow" => "\uE8A9",
        "starpie.builtin.movemonitor" or "starpie.builtin.windowopacity" or "starpie.builtin.toggletopmost" => "\uE7F4",
        "starpie.builtin.system" => "\uE713",
        "starpie.builtin.launch" => "\uE8A7",
        "starpie.plugin.qrscanner" or "starpie.builtin.ocr" => "\uE722",
        "starpie.plugin.floatingball" or "starpie.plugin.desktoppet" => "\uE7FC",
        _ => "\uE74C"
    };

    internal static string CategoryName(string category) => category switch
    {
        "CAD 工具" => I18n.T("PluginLibraryCad"),
        "系统工具" => I18n.T("PluginLibrarySystem"),
        "交互形态" => I18n.T("PluginLibraryInteraction"),
        "文件管理" => I18n.T("PluginLibraryFiles"),
        "输入工具" => I18n.T("PluginLibraryInput"),
        "效率工具" => I18n.T("PluginLibraryProductivity"),
        "窗口管理" => I18n.T("PluginLibraryWindows"),
        "交互反馈" => I18n.T("PluginLibraryFeedback"),
        "音效" => I18n.T("PluginLibrarySound"),
        "网络工具" => I18n.T("PluginLibraryWeb"),
        _ => category
    };
}
