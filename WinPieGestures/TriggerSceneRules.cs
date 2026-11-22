using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace WinPieGestures;

internal enum TriggerSceneInput
{
	Global,
	ProcessOverride,
	MouseGesture
}

internal enum TriggerSceneOutcome
{
	MissingProcess,
	MissingOverride,
	UnspecifiedTriggerKey,
	NoMatchingTrigger,
	TriggerConflict,
	GestureDisabled,
	GestureOnHoldOnly,
	WheelPriority,
	Excluded,
	OutsideAllowList,
	ModifierBypass,
	FullScreenBypass,
	WheelAllowed,
	GestureAllowed
}

internal static class TriggerSceneRules
{
	internal static TriggerSceneOutcome Simulate(AppConfig config, string? processName, TriggerSceneInput input,
		bool isFullScreen, ModifierKeys extraModifiers)
	{
		if (string.IsNullOrWhiteSpace(processName)) return TriggerSceneOutcome.MissingProcess;
		string cleanProcess = processName.Trim().ToLowerInvariant();
		if (!cleanProcess.EndsWith(".exe", StringComparison.Ordinal)) cleanProcess += ".exe";

		TriggerConfig? activeTrigger = null;
		ModifierKeys modifiers = extraModifiers & ProfileTriggerSignature.SupportedModifiers;
		bool isGesture = input == TriggerSceneInput.MouseGesture;
		if (isGesture)
		{
			if (!config.GestureEnabled) return TriggerSceneOutcome.GestureDisabled;
			if (config.LongPressTrigger && config.LongPressOnlyTrigger) return TriggerSceneOutcome.GestureOnHoldOnly;

			string button = config.GestureTriggerButton ?? "MiddleButton";
			if (!ProfileTriggerRouter.IsValidMouseButton(button)) return TriggerSceneOutcome.NoMatchingTrigger;
			var gestureSignature = ProfileTriggerSignature.ForMouse(button, modifiers);
			// The live raw-mouse guard gives even a conflicted wheel binding priority over the gesture.
			if (ProfileTriggerRouter.TryResolveForConfig(config, cleanProcess, gestureSignature, out _) != ProfileTriggerResolution.None)
				return TriggerSceneOutcome.WheelPriority;
		}
		else
		{
			TriggerConfig? selectedTrigger;
			if (input == TriggerSceneInput.ProcessOverride)
			{
				selectedTrigger = GetOverrideTrigger(config, cleanProcess);
				if (selectedTrigger == null) return TriggerSceneOutcome.MissingOverride;
			}
			else if (input == TriggerSceneInput.Global)
			{
				selectedTrigger = config.Trigger;
			}
			else
			{
				return TriggerSceneOutcome.NoMatchingTrigger;
			}

			if (selectedTrigger == null) return TriggerSceneOutcome.NoMatchingTrigger;
			if (string.Equals(selectedTrigger.TriggerType, "Keyboard", StringComparison.OrdinalIgnoreCase) &&
				selectedTrigger.VkCode == 0) return TriggerSceneOutcome.UnspecifiedTriggerKey;
			modifiers |= ProfileTriggerRouter.GetModifiers(selectedTrigger);
			if (!TryGetWheelSignature(selectedTrigger, modifiers, out var signature)) return TriggerSceneOutcome.NoMatchingTrigger;

			var resolution = ProfileTriggerRouter.TryResolveForConfig(config, cleanProcess, signature, out var activation);
			if (resolution == ProfileTriggerResolution.Conflict) return TriggerSceneOutcome.TriggerConflict;
			if (activation == null) return TriggerSceneOutcome.NoMatchingTrigger;
			activeTrigger = activation.Trigger;
		}

		TriggerSceneOutcome? processOutcome = EvaluateProcess(config, cleanProcess, activeTrigger,
			out TriggerConfig? modifierTrigger, out bool isWhitelisted);
		if (processOutcome != null) return processOutcome.Value;
		if (IsModifierSuppressed(config, modifierTrigger, modifiers)) return TriggerSceneOutcome.ModifierBypass;
		if (ShouldCheckFullScreen(config, isWhitelisted) && isFullScreen) return TriggerSceneOutcome.FullScreenBypass;
		return isGesture ? TriggerSceneOutcome.GestureAllowed : TriggerSceneOutcome.WheelAllowed;
	}

	internal static TriggerSceneOutcome? EvaluateProcess(AppConfig config, string cleanProcess, TriggerConfig? activeTrigger,
		out TriggerConfig? modifierTrigger, out bool isWhitelisted)
	{
		isWhitelisted = IsListed(config.WhitelistedProcesses, cleanProcess);
		TriggerConfig? overrideTrigger = GetOverrideTrigger(config, cleanProcess);
		// Independent gestures use the effective wheel trigger for modifier exemptions only.
		modifierTrigger = activeTrigger ?? overrideTrigger ?? config.Trigger;

		if (string.Equals(config.IsolationMode, "Whitelist", StringComparison.OrdinalIgnoreCase))
			return isWhitelisted ? null : TriggerSceneOutcome.OutsideAllowList;
		if (IsListed(config.BlacklistedProcesses, cleanProcess) &&
			(overrideTrigger == null || !ProfileTriggerRouter.AreEquivalent(activeTrigger, overrideTrigger)))
			return TriggerSceneOutcome.Excluded;
		return null;
	}

	internal static TriggerConfig? GetOverrideTrigger(AppConfig? config, string cleanProcess)
	{
		if (string.IsNullOrEmpty(cleanProcess) || config?.BlacklistTriggerOverrides == null) return null;
		return config.BlacklistTriggerOverrides.TryGetValue(cleanProcess, out var trigger) ? trigger : null;
	}

	internal static bool IsModifierSuppressed(AppConfig config, TriggerConfig? trigger, ModifierKeys modifiers) =>
		(config.DisableOnCtrl && (modifiers & ModifierKeys.Control) != 0 && trigger?.RequireCtrl != true) ||
		(config.DisableOnShift && (modifiers & ModifierKeys.Shift) != 0 && trigger?.RequireShift != true) ||
		(config.DisableOnAlt && (modifiers & ModifierKeys.Alt) != 0 && trigger?.RequireAlt != true);

	internal static bool ShouldCheckFullScreen(AppConfig config, bool isWhitelisted) =>
		config.DisableOnFullScreen && !isWhitelisted;

	private static bool IsListed(List<string>? processes, string cleanProcess)
	{
		if (processes == null) return false;
		foreach (string? process in processes)
		{
			if (string.Equals(process?.Trim(), cleanProcess, StringComparison.OrdinalIgnoreCase)) return true;
		}
		return false;
	}

	private static bool TryGetWheelSignature(TriggerConfig trigger, ModifierKeys modifiers, out ProfileTriggerSignature signature)
	{
		if (string.Equals(trigger.TriggerType, "Mouse", StringComparison.OrdinalIgnoreCase))
		{
			signature = ProfileTriggerSignature.ForMouse(trigger.MouseButton, modifiers);
			return true;
		}
		if (string.Equals(trigger.TriggerType, "Keyboard", StringComparison.OrdinalIgnoreCase) && trigger.VkCode is > 0 and < 0xff)
		{
			signature = ProfileTriggerSignature.ForKeyboard(trigger.VkCode, modifiers);
			return true;
		}

		signature = default;
		return false;
	}
}
