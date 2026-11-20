using StarPie.Plugin;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace WinPieGestures.Plugins;

internal static class LegacyQuickSearchBinding
{
    internal static bool Matches(ActionItem action)
    {
        bool system = string.Equals(action.Type, "System", StringComparison.OrdinalIgnoreCase);
        bool systemPlugin = action.Type == "Plugin" &&
            string.Equals(action.PluginActionRef?.FullId, "starpie.builtin.system.system", StringComparison.OrdinalIgnoreCase);
        if (!system && !systemPlugin) return false;
        var parameters = ActionParameterProjection.Project(action);
        return IsSearchPreset(parameters[nameof(action.Parameter)]);
    }

    internal static bool IsSearchPreset(string? preset) => preset?.Trim().ToLowerInvariant() is
        "quicksearch" or "quickfinder" or "nativesearch" or "文件秒搜" or "快速秒搜" or "原生秒搜";

    internal static ActionItem Project(AppConfig? config) => new()
    {
        Type = "Plugin",
        PluginActionRef = new PluginActionRef { PluginId = "starpie.plugin.quicksearch", ContributionId = "show" },
        ExtensionData = config == null ? null : new Dictionary<string, string>
        {
            [nameof(config.QuickSearchWidth)] = config.QuickSearchWidth.ToString(CultureInfo.InvariantCulture),
            [nameof(config.QuickSearchHeight)] = config.QuickSearchHeight.ToString(CultureInfo.InvariantCulture),
            [nameof(config.QuickSearchPinned)] = config.QuickSearchPinned ? "true" : "false",
        },
    };
}
