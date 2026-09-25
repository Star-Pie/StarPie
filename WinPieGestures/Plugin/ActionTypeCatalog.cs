using System;
using System.Collections.Generic;
using System.Linq;

namespace WinPieGestures.Plugins;

/// <summary>
/// 主程序动作类型清单的唯一 UI 投影入口。
/// <para>
/// 官方外移动作不是主程序内建动作：只有登记表中存在对应 Type Claim，且插件已启用、未隔离时，
/// 才能出现在动作类型下拉中。这样 UI 与实际派发路径保持同一套可用性语义，不再展示未安装插件的旧动作。
/// </para>
/// </summary>
internal static class ActionTypeCatalog
{
    private static readonly string[] WindowManagerTypes =
    {
        "SwitchWindow",
        "Tile",
        "MoveMonitor",
        "ToggleTopmost",
        "WindowOpacity",
    };

    private static bool IsAvailable(string type)
    {
        if (string.Equals(type, "Hotkey", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return PluginHost.IsOfficialClaimedType(type) &&
               PluginHost.IsClaimedTypeAvailable(type, out _);
    }

    private static void AddClaimed(
        List<ActionTypeItem> items,
        string type,
        string icon,
        string displayTextKey)
    {
        if (IsAvailable(type))
        {
            items.Add(new ActionTypeItem
            {
                Tag = type,
                DisplayText = icon + I18n.T(displayTextKey),
            });
        }
    }

    private static void AddClaimedOption(
        List<ActionTypeOption> items,
        string type,
        string displayTextKey)
    {
        if (IsAvailable(type))
        {
            items.Add(new ActionTypeOption
            {
                Tag = type,
                DisplayText = I18n.T(displayTextKey),
            });
        }
    }

    /// <summary>构建平铺的动作类型列表，用于子动作编辑器。</summary>
    public static List<ActionTypeItem> BuildLocalizedActionTypes()
    {
        var items = new List<ActionTypeItem>
        {
            new ActionTypeItem
            {
                Tag = "Hotkey",
                DisplayText = I18n.T("ActionTypeHotkeyShort"),
            },
        };

        AddClaimed(items, "Launch", "🚀 ", "ActionTypeLaunchShort");
        AddClaimed(items, "WebUrl", "🌐 ", "ActionTypeWebUrlShort");
        AddClaimed(items, "Folder", "📁 ", "ActionTypeFolderShort");
        AddClaimed(items, "Command", "💻 ", "ActionTypeCommandShort");
        AddClaimed(items, "SwitchWindow", "🔢 ", "ActionTypeSwitchWindowShort");
        AddClaimed(items, "Tile", "🔲 ", "ActionTypeTileShort");
        AddClaimed(items, "MoveMonitor", "🖥️ ", "ActionTypeMoveMonitorShort");
        AddClaimed(items, "ToggleTopmost", "📌 ", "ActionTypeTopmostShort");
        AddClaimed(items, "WindowOpacity", "👁️ ", "ActionTypeOpacityShort");
        AddClaimed(items, "Ocr", "📝 ", "ActionTypeOcrShort");
        AddClaimed(items, "ShellTool", "⚡ ", "ActionTypeShellToolShort");
        AddClaimed(items, "System", "⚙️ ", "ActionTypeSystemShort");

        if (ShouldShowPluginActionType())
        {
            items.Add(new ActionTypeItem
            {
                Tag = PluginActionBinding.TypeName,
                DisplayText = "🔌 " + I18n.T("ActionTypePluginShort"),
            });
        }

        return items;
    }

    /// <summary>构建聚合动作类型列表，用于主动作/手势动作编辑器。</summary>
    public static List<ActionTypeItem> BuildAggregatedActionTypes()
    {
        var items = new List<ActionTypeItem>
        {
            new ActionTypeItem
            {
                Tag = "Hotkey",
                DisplayText = "⌨️ " + I18n.T("ActionTypeHotkeyShort"),
            },
        };

        AddClaimed(items, "Launch", "🚀 ", "ActionTypeLaunchShort");
        AddClaimed(items, "WebUrl", "🌐 ", "ActionTypeWebUrlShort");
        AddClaimed(items, "Folder", "📁 ", "ActionTypeFolderShort");
        AddClaimed(items, "Command", "💻 ", "ActionTypeCommandShort");
        AddClaimed(items, "Ocr", "📝 ", "ActionTypeOcrShort");

        if (WindowManagerTypes.Any(IsAvailable))
        {
            items.Add(new ActionTypeItem
            {
                Tag = "WindowManager",
                DisplayText = "🪟 " + I18n.T("ActionTypeWindowManagerShort"),
            });
        }

        AddClaimed(items, "ShellTool", "⚡ ", "ActionTypeShellToolShort");
        AddClaimed(items, "System", "⚙️ ", "ActionTypeSystemShort");

        if (ShouldShowPluginActionType())
        {
            items.Add(new ActionTypeItem
            {
                Tag = PluginActionBinding.TypeName,
                DisplayText = "🔌 " + I18n.T("ActionTypePluginShort"),
            });
        }

        return items;
    }

    /// <summary>构建无图标的动作类型列表，供旧的子动作控件绑定使用。</summary>
    public static List<ActionTypeOption> BuildActionTypeOptions()
    {
        var items = new List<ActionTypeOption>
        {
            new ActionTypeOption
            {
                Tag = "Hotkey",
                DisplayText = I18n.T("ActionTypeHotkeyShort"),
            },
        };

        AddClaimedOption(items, "Launch", "ActionTypeLaunchShort");
        AddClaimedOption(items, "WebUrl", "ActionTypeWebUrlShort");
        AddClaimedOption(items, "Folder", "ActionTypeFolderShort");
        AddClaimedOption(items, "Command", "ActionTypeCommandShort");
        AddClaimedOption(items, "SwitchWindow", "ActionTypeSwitchWindowShort");
        AddClaimedOption(items, "Tile", "ActionTypeTileShort");
        AddClaimedOption(items, "MoveMonitor", "ActionTypeMoveMonitorShort");
        AddClaimedOption(items, "ToggleTopmost", "ActionTypeTopmostShort");
        AddClaimedOption(items, "WindowOpacity", "ActionTypeOpacityShort");
        AddClaimedOption(items, "Ocr", "ActionTypeOcrShort");
        AddClaimedOption(items, "ShellTool", "ActionTypeShellToolShort");
        AddClaimedOption(items, "System", "ActionTypeSystemShort");

        if (ShouldShowPluginActionType())
        {
            items.Add(new ActionTypeOption
            {
                Tag = PluginActionBinding.TypeName,
                DisplayText = I18n.T("ActionTypePluginShort"),
            });
        }

        return items;
    }

    /// <summary>判断是否应当在动作类型列表中展示「插件动作」选项（当存在已注册动作、已安装插件或登记簿记录时）。</summary>
    public static bool ShouldShowPluginActionType()
    {
        try
        {
            if (PluginActionBinding.BuildPluginActionItems().Count > 0) return true;
            if (PluginHost.InstalledCount > 0) return true;
            if (PluginRegistryStore.SnapshotEntries().Count > 0) return true;
            if (PluginHost.Candidates.Count > 0) return true;
        }
        catch
        {
        }
        return false;
    }

    /// <summary>选择窗口管理聚合项时使用的第一个可用具体动作。</summary>
    public static string? GetPreferredWindowManagerType() =>
        WindowManagerTypes.FirstOrDefault(IsAvailable);
}
