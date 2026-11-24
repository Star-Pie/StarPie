using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace WinPieGestures;

internal static class ProfileProcessBindings
{
	private static readonly char[] ExplicitSeparators = { ',', ';', '|', '，', '；' };
	private static readonly Regex ExeBoundaryRegex = new(@"(?<=\.exe)\s+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	internal static HashSet<string> GetNames(WheelProfile profile, string? proposedBindings = null, string? proposedDisplayName = null)
	{
		string? bindings = proposedBindings ?? profile.BoundProcesses;
		IEnumerable<string?> names = Array.Empty<string>();
		if (!string.IsNullOrWhiteSpace(bindings))
		{
			// Explicit lists use comma/semicolon/pipe separators so EXE names containing spaces stay intact.
			// Legacy lists preserve space-separated tokens while keeping whole spaced names ending in .exe or matching ProcessName.
			bool isExplicit = proposedBindings != null || profile.HasExplicitProcessBindings;
			if (isExplicit)
			{
				names = bindings.Split(ExplicitSeparators, StringSplitOptions.RemoveEmptyEntries);
			}
			else
			{
				string profileBase = NormalizeProcessToken(profile.ProcessName);
				bool hasNonGlobalProfileName = profileBase.Length > 0 &&
					!string.Equals(profileBase, "global", StringComparison.OrdinalIgnoreCase);
				var parsed = new List<string>();
				foreach (string rawSegment in bindings.Split(ExplicitSeparators, StringSplitOptions.RemoveEmptyEntries))
				{
					string segment = rawSegment.Trim();
					if (segment.Length == 0) continue;
					if (hasNonGlobalProfileName &&
						string.Equals(NormalizeProcessToken(segment), profileBase, StringComparison.OrdinalIgnoreCase))
					{
						parsed.Add(segment);
					}
					else if (segment.Contains(".exe", StringComparison.OrdinalIgnoreCase))
					{
						foreach (string part in ExeBoundaryRegex.Split(segment))
						{
							if (!string.IsNullOrWhiteSpace(part)) parsed.Add(part);
						}
					}
					else
					{
						foreach (string part in segment.Split(' ', StringSplitOptions.RemoveEmptyEntries))
						{
							parsed.Add(part);
						}
					}
				}
				names = parsed;
			}
		}
		else if (proposedBindings == null && !profile.HasExplicitProcessBindings)
		{
			names = new[] { profile.ProcessName, proposedDisplayName ?? profile.DisplayName };
		}
		return names.Select(NormalizeProcessToken).Where(n => n.Length != 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
	}

	internal static List<string> ParseProcessTokenList(string? input)
	{
		var result = new List<string>();
		if (string.IsNullOrWhiteSpace(input)) return result;
		foreach (string rawSegment in input.Split(ExplicitSeparators, StringSplitOptions.RemoveEmptyEntries))
		{
			string segment = rawSegment.Trim();
			if (segment.Length == 0) continue;
			if (segment.Contains(".exe", StringComparison.OrdinalIgnoreCase))
			{
				foreach (string part in ExeBoundaryRegex.Split(segment))
				{
					string clean = part.Trim();
					if (clean.Length > 0) result.Add(clean);
				}
			}
			else
			{
				result.Add(segment);
			}
		}
		return result;
	}

	internal static string NormalizeProcessToken(string? processName)
	{
		string value = (processName ?? string.Empty).Trim().ToLowerInvariant();
		return value.EndsWith(".exe", StringComparison.Ordinal) ? value[..^4] : value;
	}
}
