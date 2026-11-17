using System;
using System.Collections.Generic;
using System.Linq;

namespace WinPieGestures;

internal static class ProfileProcessBindings
{
	internal static HashSet<string> GetNames(WheelProfile profile, string? proposedBindings = null, string? proposedDisplayName = null)
	{
		string? bindings = proposedBindings ?? profile.BoundProcesses;
		IEnumerable<string?> names = Array.Empty<string>();
		if (!string.IsNullOrWhiteSpace(bindings))
		{
			// New explicit lists are comma/semicolon separated, so a legitimate EXE
			// name containing spaces stays one process. Legacy lists retain their split.
			char[] separators = proposedBindings != null || profile.HasExplicitProcessBindings
				? new[] { ',', ';', '|', '，' } : new[] { ',', ';', '|', '，', ' ' };
			names = bindings.Split(separators, StringSplitOptions.RemoveEmptyEntries);
		}
		else if (proposedBindings == null && !profile.HasExplicitProcessBindings)
		{
			names = new[] { profile.ProcessName, proposedDisplayName ?? profile.DisplayName };
		}
		return names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => {
			string value = n!.Trim().ToLowerInvariant();
			return value.EndsWith(".exe", StringComparison.Ordinal) ? value[..^4] : value;
		}).Where(n => n.Length != 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
	}
}
