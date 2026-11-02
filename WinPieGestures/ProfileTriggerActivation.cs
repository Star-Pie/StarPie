using System;
using System.Windows.Input;

namespace WinPieGestures;

/// <summary>
/// Immutable routing identity for one physical trigger. Display text is deliberately
/// excluded so localized labels can never affect matching.
/// </summary>
internal readonly record struct ProfileTriggerSignature(
	string TriggerType,
	string MouseButton,
	uint VkCode,
	ModifierKeys Modifiers)
{
	internal const ModifierKeys SupportedModifiers =
		ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt | ModifierKeys.Windows;

	internal static ProfileTriggerSignature ForMouse(string? mouseButton, ModifierKeys modifiers)
	{
		return new ProfileTriggerSignature(
			"Mouse",
			NormalizeMouseButton(mouseButton),
			0,
			modifiers & SupportedModifiers);
	}

	internal static ProfileTriggerSignature ForKeyboard(uint vkCode, ModifierKeys modifiers)
	{
		return new ProfileTriggerSignature(
			"Keyboard",
			string.Empty,
			vkCode,
			modifiers & SupportedModifiers);
	}

	internal static string NormalizeMouseButton(string? mouseButton)
	{
		if (string.Equals(mouseButton, "LeftButton", StringComparison.OrdinalIgnoreCase)) return "LeftButton";
		if (string.Equals(mouseButton, "MiddleButton", StringComparison.OrdinalIgnoreCase)) return "MiddleButton";
		if (string.Equals(mouseButton, "XButton1", StringComparison.OrdinalIgnoreCase)) return "XButton1";
		if (string.Equals(mouseButton, "XButton2", StringComparison.OrdinalIgnoreCase)) return "XButton2";
		return "RightButton";
	}
}

/// <summary>
/// A frozen selection of both the wheel profile and the physical trigger that
/// opened it. The profile reference is intentional: it is the existing profile
/// identity, while the trigger is copied so later UI edits cannot alter an
/// in-flight gesture.
/// </summary>
internal sealed class ProfileTriggerActivation
{
	internal ProfileTriggerActivation(
		WheelProfile profile,
		TriggerConfig trigger,
		ProfileTriggerSignature signature,
		bool isDedicated,
		string? processName,
		WheelLayer? layer = null)
	{
		Profile = profile;
		Layer = layer;
		Trigger = trigger.Clone();
		Trigger.TriggerType = signature.TriggerType;
		if (signature.TriggerType == "Mouse") Trigger.MouseButton = signature.MouseButton;
		Signature = signature;
		IsDedicated = isDedicated;
		ProcessName = (processName ?? string.Empty).Trim().ToLowerInvariant();
	}

	internal WheelProfile Profile { get; }

	internal WheelLayer? Layer { get; }

	internal bool IsLayerAvailable => Layer == null || Profile.Layers?.Contains(Layer) == true;

	internal bool TrySelectInitialLayer()
	{
		int index = Layer == null ? 0 : Profile.Layers?.IndexOf(Layer) ?? -1;
		if (index < 0 || Profile.Layers == null || index >= Profile.Layers.Count)
		{
			return false;
		}
		Profile.ActiveLayerIndex = index;
		Profile.SyncRootPropertiesFromActiveLayer();
		return true;
	}

	internal TriggerConfig Trigger { get; }

	internal ProfileTriggerSignature Signature { get; }

	internal bool IsDedicated { get; }

	internal string ProcessName { get; }

	internal bool IsSameRoute(ProfileTriggerActivation? other) =>
		other != null && ReferenceEquals(Profile, other.Profile) && ReferenceEquals(Layer, other.Layer) && Signature == other.Signature &&
		string.Equals(ProcessName, other.ProcessName, StringComparison.OrdinalIgnoreCase);

	internal static bool IsSameTarget(TargetWindowInfo? first, TargetWindowInfo? second) =>
		first != null && second != null && first.Hwnd != IntPtr.Zero && first.ProcessId != 0 &&
		first.Hwnd == second.Hwnd && first.ProcessId == second.ProcessId &&
		first.MatchingIdentity.Equals(second.MatchingIdentity);
}
