using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinPieGestures;

public sealed class ActionPanelScheme
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public int Rows { get; set; } = 4;
    public int Columns { get; set; } = 3;
    public string Hotkey { get; set; } = "";
    public List<ActionItem?> Keys { get; set; } = Enumerable.Repeat<ActionItem?>(null, 12).ToList();
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
    public override string ToString() => Name;
    public ActionPanelScheme Clone() => new()
    { Id = Id, Name = Name, Rows = Rows, Columns = Columns, Hotkey = Hotkey, Keys = Keys.Select(item => item?.Clone()).ToList(),
      AdditionalData = AdditionalData?.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal) };

    public bool TryResize(int rows, int columns)
    {
        if (rows is < 1 or > 10 || columns is < 1 or > 16) return false;
        int count = rows * columns;
        if (Keys.Skip(count).Any(item => item != null)) return false;
        Keys = Keys.Take(count).Concat(Enumerable.Repeat<ActionItem?>(null, Math.Max(0, count - Keys.Count))).ToList();
        Rows = rows; Columns = columns; return true;
    }

    internal static bool Validate(IReadOnlyList<ActionPanelScheme>? schemes)
    {
        if (schemes == null || schemes.Count > 32) return false;
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hotkeys = new HashSet<(uint, ModifierKeys)>();
        foreach (var scheme in schemes)
        {
            if (scheme == null || string.IsNullOrWhiteSpace(scheme.Id) || !ids.Add(scheme.Id) ||
                string.IsNullOrWhiteSpace(scheme.Name) || !names.Add(scheme.Name.Trim()) || scheme.Name.Length > 64 || scheme.Rows is < 1 or > 10 ||
                scheme.Columns is < 1 or > 16 || scheme.Keys == null || scheme.Keys.Count != scheme.Rows * scheme.Columns ||
                scheme.Keys.Any(item => item?.Type == "ActionPanel")) return false;
            if (!string.IsNullOrWhiteSpace(scheme.Hotkey) &&
                (!ActionPanelHotkey.TryParse(scheme.Hotkey, out var key, out var mods) || !hotkeys.Add((key, mods)))) return false;
        }
        return true;
    }
}

internal static class ActionPanelHotkey
{
    internal static bool TryParse(string? value, out uint key, out ModifierKeys modifiers)
    {
        key = 0; modifiers = ModifierKeys.None;
        if (string.IsNullOrWhiteSpace(value) || !Plugins.InputPanelHotkey.TryNormalize(value, out var normalized)) return false;
        var parsed = ActionExecutor.ParseHotkey(normalized);
        if (parsed.Steps.Count != 1 || !ProfileTriggerRouter.IsAllowedKeyboardTrigger(parsed.MainKey)) return false;
        key = parsed.MainKey;
        foreach (var modifier in parsed.Modifiers)
            modifiers |= modifier switch { 162 or 163 or 17 => ModifierKeys.Control, 160 or 161 or 16 => ModifierKeys.Shift,
                164 or 165 or 18 => ModifierKeys.Alt, 91 or 92 => ModifierKeys.Windows, _ => ModifierKeys.None };
        return modifiers != ModifierKeys.None || key is >= 112 and <= 135;
    }

    internal static bool ConflictsWithWheels(uint key, ModifierKeys modifiers, AppConfig config)
    {
        bool Matches(TriggerConfig? trigger) => trigger?.TriggerType == "Keyboard" && trigger.VkCode == key &&
            (!trigger.RequireCtrl || modifiers.HasFlag(ModifierKeys.Control)) &&
            (!trigger.RequireShift || modifiers.HasFlag(ModifierKeys.Shift)) &&
            (!trigger.RequireAlt || modifiers.HasFlag(ModifierKeys.Alt)) &&
            (!trigger.RequireWin || modifiers.HasFlag(ModifierKeys.Windows));
        if (Matches(config.Trigger)) return true;
        return (config.Profiles ?? new()).Any(profile => Matches(profile.ActivationTrigger) ||
            (profile.Layers ?? new()).Any(layer => Matches(layer.ActivationTrigger)));
    }
}
