using System;
using System.Collections.Generic;

namespace WinPieGestures.Plugins;

internal static class PluginImpactAnalyzer
{
    public static int CountAffectedActions(AppConfig? config, string pluginId)
    {
        if (config == null || string.IsNullOrWhiteSpace(pluginId)) return 0;

        var claimedTypes = new HashSet<string>(
            PluginHost.ClaimedTypeNamesOf(pluginId),
            StringComparer.OrdinalIgnoreCase);
        int count = 0;

        void CountOne(ActionItem? action)
        {
            if (action == null) return;
            if (string.Equals(action.Type, PluginActionBinding.TypeName, StringComparison.Ordinal))
            {
                if (string.Equals(action.PluginActionRef?.PluginId, pluginId, StringComparison.OrdinalIgnoreCase))
                    count++;
            }
            else if (!string.IsNullOrWhiteSpace(action.Type) && claimedTypes.Contains(action.Type))
            {
                count++;
            }

            if (action.SubActions == null) return;
            foreach (ActionItem subAction in action.SubActions) CountOne(subAction);
        }

        foreach (WheelProfile profile in config.Profiles ?? new List<WheelProfile>())
        {
            if (profile.Actions != null)
                foreach (ActionItem action in profile.Actions) CountOne(action);
            CountOne(profile.CenterAction);

            if (profile.Layers == null) continue;
            foreach (WheelLayer layer in profile.Layers)
            {
                if (layer.Actions != null)
                    foreach (ActionItem action in layer.Actions) CountOne(action);
                CountOne(layer.CenterAction);
                if (layer.FixedRings != null)
                    foreach (ActionItem action in layer.FixedRings.StoredActions()) CountOne(action);
            }
        }

        if (config.GestureMappings != null)
            foreach (GestureMapping mapping in config.GestureMappings) CountOne(mapping.Action);

        return count;
    }
}
