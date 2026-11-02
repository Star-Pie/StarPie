using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace WinPieGestures;

internal enum ProfileTriggerResolution
{
	None,
	Match,
	Conflict
}

internal enum ProfileTriggerValidationError
{
	None,
	GlobalProtected,
	InvalidTrigger,
	Conflict
}

/// <summary>
/// Precompiled profile-trigger routing table. It is rebuilt on configuration
/// changes and only performs dictionary/struct lookups from input hooks.
/// </summary>
internal static class ProfileTriggerRouter
{
	private const string GlobalScope = "*";
	private readonly record struct RouteKey(string ProcessToken, ProfileTriggerSignature Signature);

	private sealed class RouteKeyComparer : IEqualityComparer<RouteKey>
	{
		internal static readonly RouteKeyComparer Instance = new();
		public bool Equals(RouteKey x, RouteKey y) => x.Signature == y.Signature &&
			StringComparer.OrdinalIgnoreCase.Equals(x.ProcessToken, y.ProcessToken);
		public int GetHashCode(RouteKey key) => HashCode.Combine(
			StringComparer.OrdinalIgnoreCase.GetHashCode(key.ProcessToken), key.Signature);
	}

	internal static volatile bool IsRecording;

	private sealed class RoutingSnapshot
	{
		internal static readonly RoutingSnapshot Empty = new RoutingSnapshot(
			new Dictionary<string, WheelProfile>(StringComparer.OrdinalIgnoreCase),
			new Dictionary<string, TriggerConfig>(StringComparer.OrdinalIgnoreCase),
			new Dictionary<RouteKey, ProfileTriggerActivation>(RouteKeyComparer.Instance),
			new HashSet<RouteKey>(RouteKeyComparer.Instance),
			new HashSet<string>(StringComparer.OrdinalIgnoreCase),
			null,
			null);

		internal RoutingSnapshot(
			Dictionary<string, WheelProfile> legacyProfiles,
			Dictionary<string, TriggerConfig> legacyTriggers,
			Dictionary<RouteKey, ProfileTriggerActivation> dedicated,
			HashSet<RouteKey> conflicts,
			HashSet<string> invalidDedicatedProcesses,
			WheelProfile? globalProfile,
			TriggerConfig? globalTrigger)
		{
			LegacyProfiles = legacyProfiles;
			LegacyTriggers = legacyTriggers;
			Dedicated = dedicated;
			Conflicts = conflicts;
			InvalidDedicatedProcesses = invalidDedicatedProcesses;
			GlobalProfile = globalProfile;
			GlobalTrigger = globalTrigger;
		}

		internal Dictionary<string, WheelProfile> LegacyProfiles { get; }

		internal Dictionary<string, TriggerConfig> LegacyTriggers { get; }

		internal Dictionary<RouteKey, ProfileTriggerActivation> Dedicated { get; }

		internal HashSet<RouteKey> Conflicts { get; }

		internal HashSet<string> InvalidDedicatedProcesses { get; }

		internal WheelProfile? GlobalProfile { get; }

		internal TriggerConfig? GlobalTrigger { get; }
	}

	private static readonly object PublishSync = new object();

	private static RoutingSnapshot _snapshot = RoutingSnapshot.Empty;

	internal static void Publish(AppConfig? config)
	{
		lock (PublishSync)
		{
			Volatile.Write(ref _snapshot, BuildSnapshot(config));
		}
	}

	internal static ProfileTriggerResolution TryResolveMouse(
		string? processName,
		string? mouseButton,
		ModifierKeys modifiers,
		out ProfileTriggerActivation? activation)
	{
		return TryResolve(
			processName,
			ProfileTriggerSignature.ForMouse(mouseButton, modifiers),
			out activation);
	}

	internal static ProfileTriggerResolution TryResolveKeyboard(
		string? processName,
		uint vkCode,
		ModifierKeys modifiers,
		out ProfileTriggerActivation? activation)
	{
		return TryResolve(
			processName,
			ProfileTriggerSignature.ForKeyboard(vkCode, modifiers),
			out activation);
	}

	internal static bool HasMouseTrigger(string? processName, string? mouseButton, ModifierKeys modifiers)
	{
		return TryResolveMouse(processName, mouseButton, modifiers, out _) != ProfileTriggerResolution.None;
	}

	internal static ProfileTriggerResolution TryResolve(
		string? processName,
		ProfileTriggerSignature signature,
		out ProfileTriggerActivation? activation)
	{
		activation = null;
		RoutingSnapshot snapshot = Volatile.Read(ref _snapshot);
		// All process aliases are compiled at publication; hook lookups do not split
		// or allocate lowercase/substrings for the executable name.
		string processToken = processName ?? string.Empty;
		RouteKey routeKey = new RouteKey(processToken, signature);

		if (snapshot.Conflicts.Contains(routeKey))
		{
			return ProfileTriggerResolution.Conflict;
		}

		if (snapshot.Dedicated.TryGetValue(routeKey, out ProfileTriggerActivation? dedicated))
		{
			activation = new ProfileTriggerActivation(dedicated.Profile, dedicated.Trigger,
				dedicated.Signature, isDedicated: true, processName, dedicated.Layer);
			return ProfileTriggerResolution.Match;
		}

		WheelProfile? legacyProfile = null;
		if (!string.IsNullOrEmpty(processToken))
		{
			snapshot.LegacyProfiles.TryGetValue(processToken, out legacyProfile);
		}
		bool hasApplicationProfile = legacyProfile != null;

		TriggerConfig? legacyTrigger = null;
		if (!string.IsNullOrEmpty(processToken))
		{
			snapshot.LegacyTriggers.TryGetValue(processToken, out legacyTrigger);
		}
		bool hasApplicationOverride = legacyTrigger != null;
		legacyTrigger ??= snapshot.GlobalTrigger;

		// Application-scoped legacy bindings also win over a Global layer shortcut.
		if ((!hasApplicationProfile && !hasApplicationOverride) || legacyTrigger == null || !MatchesLegacy(legacyTrigger, signature))
		{
			RouteKey globalKey = new(GlobalScope, signature);
			if (snapshot.Conflicts.Contains(globalKey)) return ProfileTriggerResolution.Conflict;
			if (snapshot.Dedicated.TryGetValue(globalKey, out ProfileTriggerActivation? globalLayer))
			{
				activation = new ProfileTriggerActivation(globalLayer.Profile, globalLayer.Trigger,
					globalLayer.Signature, isDedicated: true, processName, globalLayer.Layer);
				return ProfileTriggerResolution.Match;
			}
		}
		legacyProfile ??= snapshot.GlobalProfile;

		if (legacyProfile != null && legacyTrigger != null && MatchesLegacy(legacyTrigger, signature))
		{
			activation = new ProfileTriggerActivation(
				legacyProfile,
				legacyTrigger,
				signature,
				isDedicated: false,
				processName);
			return ProfileTriggerResolution.Match;
		}

		// An invalid explicit profile must not silently fall back to the global
		// trigger when it is the only profile bound to that application.
		if (!string.IsNullOrEmpty(processToken) &&
			snapshot.InvalidDedicatedProcesses.Contains(processToken) &&
			!snapshot.LegacyProfiles.ContainsKey(processToken))
		{
			return ProfileTriggerResolution.Conflict;
		}

		return ProfileTriggerResolution.None;
	}

	internal static ProfileTriggerValidationError ValidateProfileTrigger(
		AppConfig? config,
		WheelProfile? profile,
		TriggerConfig? trigger,
		string? proposedBindings = null,
		string? proposedDisplayName = null)
	{
		if (profile == null || string.Equals(profile.ProcessName, "Global", StringComparison.OrdinalIgnoreCase))
		{
			return ProfileTriggerValidationError.GlobalProtected;
		}
		return ValidateBinding(config, profile, profile.Layers?.FirstOrDefault(), trigger, proposedBindings, proposedDisplayName);
	}

	internal static TriggerConfig? GetLayerTrigger(WheelProfile profile, WheelLayer layer)
	{
		if (layer.ActivationTrigger != null) return layer.ActivationTrigger;
		return !IsGlobal(profile) && ReferenceEquals(profile.Layers?.FirstOrDefault(), layer)
			? profile.ActivationTrigger : null;
	}

	internal static ProfileTriggerValidationError ValidateLayerTrigger(AppConfig? config, WheelProfile profile,
		WheelLayer layer, TriggerConfig? trigger) => ValidateBinding(config, profile, layer, trigger, null, null);

	internal static ProfileTriggerValidationError ValidateBindings(AppConfig? config, WheelProfile profile,
		string? proposedBindings = null, string? proposedDisplayName = null)
	{
		foreach (var binding in EnumerateBindings(profile))
		{
			var error = ValidateBinding(config, profile, binding.Layer, binding.Trigger, proposedBindings, proposedDisplayName);
			if (error != ProfileTriggerValidationError.None) return error;
		}
		return ProfileTriggerValidationError.None;
	}

	private static bool IsGlobal(WheelProfile profile) =>
		string.Equals(profile.ProcessName, "Global", StringComparison.OrdinalIgnoreCase);

	private static IEnumerable<(WheelLayer? Layer, TriggerConfig Trigger)> EnumerateBindings(WheelProfile profile)
	{
		WheelLayer? first = profile.Layers?.FirstOrDefault();
		// The old scheme shortcut always opened layer 0. A new explicit layer-0
		// shortcut supersedes it, while its stored value remains roundtrip-compatible.
		if (!IsGlobal(profile) && profile.ActivationTrigger != null && first?.ActivationTrigger == null)
		{
			yield return (first, profile.ActivationTrigger);
		}
		if (profile.Layers == null) yield break;
		foreach (WheelLayer? layer in profile.Layers)
		{
			if (layer?.ActivationTrigger != null) yield return (layer, layer.ActivationTrigger);
		}
	}

	private static ProfileTriggerValidationError ValidateBinding(AppConfig? config, WheelProfile profile,
		WheelLayer? layer, TriggerConfig? trigger, string? proposedBindings, string? proposedDisplayName)
	{
		if (trigger == null)
		{
			return ProfileTriggerValidationError.None;
		}
		if (!TryCreateDedicatedSignature(trigger, out ProfileTriggerSignature signature))
		{
			return ProfileTriggerValidationError.InvalidTrigger;
		}

		HashSet<string> tokens = GetProcessTokens(profile, proposedBindings, proposedDisplayName);
		if (config?.Profiles == null)
		{
			return ProfileTriggerValidationError.None;
		}

		foreach (WheelProfile other in config.Profiles)
		{
			if (other == null || IsGlobal(other) != IsGlobal(profile))
			{
				continue;
			}

			if (!IsGlobal(profile) && !ReferenceEquals(profile, other) && !tokens.Overlaps(GetProcessTokens(other))) continue;
			foreach (var binding in EnumerateBindings(other))
			{
				if (ReferenceEquals(other, profile) && ReferenceEquals(binding.Layer, layer)) continue;
				if (TryCreateDedicatedSignature(binding.Trigger, out var otherSignature) && otherSignature == signature)
				{
					return ProfileTriggerValidationError.Conflict;
				}
			}
		}

		return ProfileTriggerValidationError.None;
	}

	internal static bool AreEquivalent(TriggerConfig? left, TriggerConfig? right)
	{
		if (ReferenceEquals(left, right)) return true;
		if (left == null || right == null) return false;

		string leftType = NormalizeTriggerType(left.TriggerType);
		string rightType = NormalizeTriggerType(right.TriggerType);
		if (!string.Equals(leftType, rightType, StringComparison.Ordinal)) return false;
		if (!ModifiersEqual(left, right)) return false;

		if (leftType == "Mouse")
		{
			return string.Equals(
				ProfileTriggerSignature.NormalizeMouseButton(left.MouseButton),
				ProfileTriggerSignature.NormalizeMouseButton(right.MouseButton),
				StringComparison.Ordinal);
		}
		return left.VkCode == right.VkCode;
	}

	internal static bool IsKeyboardRelease(TriggerConfig trigger, uint vkCode)
	{
		if (trigger == null || !string.Equals(NormalizeTriggerType(trigger.TriggerType), "Keyboard", StringComparison.Ordinal))
		{
			return false;
		}
		if (trigger.VkCode != 0 && vkCode == trigger.VkCode) return true;
		if (trigger.RequireCtrl && IsCtrlVk(vkCode)) return true;
		if (trigger.RequireShift && IsShiftVk(vkCode)) return true;
		if (trigger.RequireAlt && IsAltVk(vkCode)) return true;
		if (trigger.RequireWin && IsWinVk(vkCode)) return true;
		return false;
	}

	internal static bool IsAllowedKeyboardTrigger(uint vkCode)
	{
		return vkCode != 0 && vkCode != 27 && !IsModifierVk(vkCode);
	}

	internal static bool TryCreateDedicatedSignature(TriggerConfig? trigger, out ProfileTriggerSignature signature)
	{
		signature = default;
		if (trigger == null) return false;
		if (!string.Equals(trigger.TriggerType, "Mouse", StringComparison.OrdinalIgnoreCase) &&
			!string.Equals(trigger.TriggerType, "Keyboard", StringComparison.OrdinalIgnoreCase)) return false;

		string triggerType = NormalizeTriggerType(trigger.TriggerType);
		if (triggerType == "Mouse")
		{
			if (!IsValidMouseButton(trigger.MouseButton)) return false;
			signature = ProfileTriggerSignature.ForMouse(trigger.MouseButton, GetModifiers(trigger));
			return true;
		}
		if (triggerType == "Keyboard")
		{
			if (!IsAllowedKeyboardTrigger(trigger.VkCode)) return false;
			signature = ProfileTriggerSignature.ForKeyboard(trigger.VkCode, GetModifiers(trigger));
			return true;
		}
		return false;
	}

	internal static ProfileTriggerActivation CreateLegacyActivation(
		string? processName,
		TriggerConfig? trigger,
		WheelProfile? profile = null)
	{
		TriggerConfig effective = trigger ?? new TriggerConfig();
		WheelProfile effectiveProfile = profile ?? ConfigManager.GetProfileForProcess(processName ?? string.Empty);
		ProfileTriggerSignature signature = string.Equals(NormalizeTriggerType(effective.TriggerType), "Keyboard", StringComparison.Ordinal)
			? ProfileTriggerSignature.ForKeyboard(effective.VkCode, GetModifiers(effective))
			: ProfileTriggerSignature.ForMouse(effective.MouseButton, GetModifiers(effective));
		return new ProfileTriggerActivation(effectiveProfile, effective, signature, isDedicated: false, processName);
	}

	private static RoutingSnapshot BuildSnapshot(AppConfig? config)
	{
		Dictionary<string, WheelProfile> legacyProfiles = new(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, TriggerConfig> legacyTriggers = new(StringComparer.OrdinalIgnoreCase);
		Dictionary<RouteKey, ProfileTriggerActivation> dedicated = new(RouteKeyComparer.Instance);
		HashSet<RouteKey> conflicts = new(RouteKeyComparer.Instance);
		HashSet<string> invalidDedicatedProcesses = new(StringComparer.OrdinalIgnoreCase);

		WheelProfile? globalProfile = null;
		TriggerConfig? globalTrigger = config?.Trigger?.Clone();
		if (config?.Profiles != null)
		{
			foreach (WheelProfile? profile in config.Profiles)
			{
				if (profile == null) continue;
				bool global = IsGlobal(profile);
				if (global)
				{
					globalProfile ??= profile;
				}

				HashSet<string> tokens = global ? new HashSet<string> { GlobalScope } : GetProcessTokens(profile);
				if (tokens.Count == 0) continue;

				if (!global && profile.ActivationTrigger == null)
				{
					foreach (string token in tokens) legacyProfiles.TryAdd(token, profile);
				}
				foreach (var binding in EnumerateBindings(profile))
				{
					if (!TryCreateDedicatedSignature(binding.Trigger, out ProfileTriggerSignature signature))
					{
						if (ReferenceEquals(binding.Trigger, profile.ActivationTrigger))
							foreach (string token in tokens) invalidDedicatedProcesses.Add(token);
						continue;
					}

					ProfileTriggerActivation activation = new(
						profile,
						binding.Trigger,
						signature,
						isDedicated: true,
						profile.BoundProcesses,
						binding.Layer);
					foreach (string token in tokens)
					{
						RouteKey key = new(token, signature);
						if (dedicated.TryGetValue(key, out ProfileTriggerActivation? existing) &&
							(!ReferenceEquals(existing.Profile, profile) || !ReferenceEquals(existing.Layer, binding.Layer)))
						{
							conflicts.Add(key);
						}
						else
						{
							dedicated[key] = activation;
						}
					}
				}
			}
		}

		if (config?.BlacklistTriggerOverrides != null)
		{
			foreach (KeyValuePair<string, TriggerConfig> pair in config.BlacklistTriggerOverrides)
			{
				string token = NormalizeProcessToken(pair.Key);
				if (!string.IsNullOrEmpty(token) && pair.Value != null && !legacyTriggers.ContainsKey(token))
				{
				legacyTriggers[token] = pair.Value.Clone();
				legacyTriggers[token + ".exe"] = legacyTriggers[token];
				}
			}
		}

		return new RoutingSnapshot(
			legacyProfiles,
			legacyTriggers,
			dedicated,
			conflicts,
			invalidDedicatedProcesses,
			globalProfile,
			globalTrigger);
	}

	private static HashSet<string> GetProcessTokens(WheelProfile profile, string? bindings = null, string? displayName = null)
	{
		HashSet<string> tokens = new(StringComparer.OrdinalIgnoreCase);
		bindings ??= profile.BoundProcesses;
		if (!string.IsNullOrWhiteSpace(bindings))
		{
			foreach (string token in bindings.Split(
				new[] { ',', ';', '|', ' ', '，' },
				StringSplitOptions.RemoveEmptyEntries))
			{
				string normalized = NormalizeProcessToken(token);
				if (!string.IsNullOrEmpty(normalized)) tokens.Add(normalized);
			}
		}
		else
		{
			string processToken = NormalizeProcessToken(profile.ProcessName);
			if (!string.IsNullOrEmpty(processToken)) tokens.Add(processToken);
			string displayToken = NormalizeProcessToken(displayName ?? profile.DisplayName);
			if (!string.IsNullOrEmpty(displayToken)) tokens.Add(displayToken);
		}
		foreach (string token in tokens.ToArray()) tokens.Add(token + ".exe");
		return tokens;
	}

	private static string NormalizeProcessToken(string? processName)
	{
		string value = (processName ?? string.Empty).Trim().ToLowerInvariant();
		if (value.EndsWith(".exe", StringComparison.Ordinal))
		{
			value = value.Substring(0, value.Length - 4);
		}
		return value;
	}

	private static string NormalizeTriggerType(string? triggerType)
	{
		return string.Equals(triggerType, "Keyboard", StringComparison.OrdinalIgnoreCase) ? "Keyboard" : "Mouse";
	}

	private static bool IsValidMouseButton(string? mouseButton)
	{
		return string.Equals(mouseButton, "LeftButton", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(mouseButton, "RightButton", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(mouseButton, "MiddleButton", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(mouseButton, "XButton1", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(mouseButton, "XButton2", StringComparison.OrdinalIgnoreCase);
	}

	private static ModifierKeys GetModifiers(TriggerConfig trigger)
	{
		ModifierKeys modifiers = ModifierKeys.None;
		if (trigger.RequireCtrl) modifiers |= ModifierKeys.Control;
		if (trigger.RequireShift) modifiers |= ModifierKeys.Shift;
		if (trigger.RequireAlt) modifiers |= ModifierKeys.Alt;
		if (trigger.RequireWin) modifiers |= ModifierKeys.Windows;
		return modifiers;
	}

	private static bool ModifiersEqual(TriggerConfig left, TriggerConfig right)
	{
		return GetModifiers(left) == GetModifiers(right);
	}

	internal static bool MatchesLegacy(TriggerConfig trigger, ProfileTriggerSignature signature)
	{
		string type = NormalizeTriggerType(trigger.TriggerType);
		if (!string.Equals(type, signature.TriggerType, StringComparison.Ordinal)) return false;
		if (!HasRequiredModifiers(trigger, signature.Modifiers)) return false;

		if (type == "Mouse")
		{
			return string.Equals(
				ProfileTriggerSignature.NormalizeMouseButton(trigger.MouseButton),
				signature.MouseButton,
				StringComparison.Ordinal);
		}

		// Preserve the old keyboard behavior: VK 0/modifier-only legacy triggers
		// are still allowed, while dedicated triggers are validated more strictly.
		return trigger.VkCode == 0 || IsModifierVk(trigger.VkCode) || trigger.VkCode == signature.VkCode;
	}

	private static bool HasRequiredModifiers(TriggerConfig trigger, ModifierKeys actual)
	{
		return (!trigger.RequireCtrl || actual.HasFlag(ModifierKeys.Control)) &&
			(!trigger.RequireShift || actual.HasFlag(ModifierKeys.Shift)) &&
			(!trigger.RequireAlt || actual.HasFlag(ModifierKeys.Alt)) &&
			(!trigger.RequireWin || actual.HasFlag(ModifierKeys.Windows));
	}

	private static bool IsModifierVk(uint vkCode)
	{
		return vkCode == 16 || vkCode == 160 || vkCode == 161 ||
			vkCode == 17 || vkCode == 162 || vkCode == 163 ||
			vkCode == 18 || vkCode == 164 || vkCode == 165 ||
			vkCode == 91 || vkCode == 92;
	}

	private static bool IsCtrlVk(uint vkCode) => vkCode == 17 || vkCode == 162 || vkCode == 163;

	private static bool IsShiftVk(uint vkCode) => vkCode == 16 || vkCode == 160 || vkCode == 161;

	private static bool IsAltVk(uint vkCode) => vkCode == 18 || vkCode == 164 || vkCode == 165;

	private static bool IsWinVk(uint vkCode) => vkCode == 91 || vkCode == 92;
}
