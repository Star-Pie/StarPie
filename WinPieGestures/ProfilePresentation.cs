using System;

namespace WinPieGestures;

public static class ProfileQuickSwitchDisplayModes
{
	public const string IconAndText = "IconAndText";
	public const string TextOnly = "TextOnly";
	public const string IconOnly = "IconOnly";
}

public enum LayerTriggerBadgeState
{
	Unset,
	Recording,
	Bound,
	ConflictOrDisabled
}

public readonly record struct LayerTriggerPresentation(
	LayerTriggerBadgeState State,
	string BadgeText,
	string RecordButtonText,
	bool CanRecord,
	bool CanReset,
	string ToolTipText);

public static class ProfilePresentation
{
	public static string NormalizeQuickSwitchDisplayMode(string? mode)
	{
		if (string.Equals(mode, ProfileQuickSwitchDisplayModes.TextOnly, StringComparison.OrdinalIgnoreCase))
		{
			return ProfileQuickSwitchDisplayModes.TextOnly;
		}
		if (string.Equals(mode, ProfileQuickSwitchDisplayModes.IconOnly, StringComparison.OrdinalIgnoreCase))
		{
			return ProfileQuickSwitchDisplayModes.IconOnly;
		}
		return ProfileQuickSwitchDisplayModes.IconAndText;
	}

	public static LayerTriggerPresentation GetLayerTriggerPresentation(
		AppConfig? config,
		WheelProfile? profile,
		WheelLayer? layer,
		bool isRecording)
	{
		if (profile == null || layer == null)
		{
			return new LayerTriggerPresentation(
				LayerTriggerBadgeState.Unset,
				I18n.T("LayerActivationUnset"),
				I18n.T("ProfileActivationRecordShort"),
				CanRecord: false,
				CanReset: false,
				I18n.T("LayerActivationSelect"));
		}

		if (isRecording)
		{
			return new LayerTriggerPresentation(
				LayerTriggerBadgeState.Recording,
				I18n.T("LayerActivationRecordingBadge"),
				I18n.T("ProfileActivationCancel"),
				CanRecord: true,
				CanReset: false,
				I18n.T("ProfileActivationListening"));
		}

		TriggerConfig? trigger = ProfileTriggerRouter.GetLayerTrigger(profile, layer);
		if (trigger == null)
		{
			return new LayerTriggerPresentation(
				LayerTriggerBadgeState.Unset,
				I18n.T("LayerActivationUnset"),
				I18n.T("ProfileActivationRecordShort"),
				CanRecord: true,
				CanReset: false,
				I18n.T("ProfileActivationHelp"));
		}

		string formatted = SettingsWindow.FormatTriggerDisplay(trigger);
		ProfileTriggerValidationError error = ProfileTriggerRouter.ValidateLayerTrigger(config, profile, layer, trigger);
		if (error == ProfileTriggerValidationError.None &&
			!string.Equals(profile.ProcessName, "Global", StringComparison.OrdinalIgnoreCase) &&
			!profile.UseApplicationContext)
		{
			error = ProfileTriggerRouter.ValidateBindings(config, profile);
		}

		if (error != ProfileTriggerValidationError.None)
		{
			return new LayerTriggerPresentation(
				LayerTriggerBadgeState.ConflictOrDisabled,
				I18n.T("LayerActivationConflictBadge"),
				I18n.T("ProfileActivationRerecord"),
				CanRecord: true,
				CanReset: true,
				formatted + " — " + I18n.T("ProfileActivationConflict"));
		}

		return new LayerTriggerPresentation(
			LayerTriggerBadgeState.Bound,
			I18n.TF("LayerActivationBoundFmt", formatted),
			I18n.T("ProfileActivationRerecord"),
			CanRecord: true,
			CanReset: true,
			I18n.TF("LayerActivationStatus", profile.DisplayTitle, layer.DisplayName, formatted));
	}

	public static string LayerCaption(WheelProfile profile, int layerIndex)
	{
		if (profile.Layers == null || profile.Layers.Count == 0) return profile.FriendlyName;
		int index = Math.Clamp(layerIndex, 0, profile.Layers.Count - 1);
		string layerName = profile.Layers[index].DisplayName;
		if (string.IsNullOrWhiteSpace(layerName)) layerName = I18n.TF("WheelLayerFmt", index + 1);
		return I18n.TF("ProfileLayerIndicatorFormat", profile.FriendlyName,
			layerName, index + 1, profile.Layers.Count);
	}
}
