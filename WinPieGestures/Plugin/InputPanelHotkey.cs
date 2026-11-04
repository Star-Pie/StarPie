using System;
using System.Collections.Generic;
using System.Linq;

namespace WinPieGestures.Plugins;

/// <summary>Canonicalize one chord; never allow the legacy raw-text/sequence fallback.</summary>
internal static class InputPanelHotkey
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["OemMinus"] = "Minus", ["OemPlus"] = "Plus", ["OemOpenBrackets"] = "BracketLeft",
        ["OemCloseBrackets"] = "BracketRight", ["OemBackslash"] = "Backslash",
        ["OemSemicolon"] = "Semicolon", ["OemQuotes"] = "Quote", ["OemComma"] = "Comma",
        ["OemPeriod"] = "Period", ["OemQuestion"] = "Slash", ["OemTilde"] = "Tilde",
        ["Divide"] = "NumDivide", ["Multiply"] = "NumMultiply", ["Add"] = "NumAdd",
        ["Subtract"] = "NumSubtract", ["Decimal"] = "NumDecimal",
        [","] = "Comma", [";"] = "Semicolon", ["Control"] = "Ctrl", ["Windows"] = "Win",
    };

    internal static bool TryNormalize(string? value, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256) return false;
        string[] tokens = value.Split('+').Select(token => token.Trim()).ToArray();
        if (tokens.Length is < 1 or > 5 || tokens.Any(token => string.IsNullOrEmpty(token) || token.Any(char.IsWhiteSpace))) return false;
        var modifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < tokens.Length; index++)
        {
            tokens[index] = Aliases.GetValueOrDefault(tokens[index], tokens[index]);
            if (index < tokens.Length - 1)
            {
                if (!new[] { "Ctrl", "Shift", "Alt", "Win" }.Contains(tokens[index], StringComparer.OrdinalIgnoreCase)
                    || !modifiers.Add(tokens[index])) return false;
            }
        }
        normalized = string.Join("+", tokens);
        var parsed = ActionExecutor.ParseHotkey(normalized);
        if (parsed.Steps.Count != 1 || parsed.Steps[0].MainKey == 0 || parsed.SequenceKeys.Count != 1 ||
            parsed.Steps[0].Modifiers.Count != modifiers.Count)
        { normalized = ""; return false; }
        return true;
    }
}
