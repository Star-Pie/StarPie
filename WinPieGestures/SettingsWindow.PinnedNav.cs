using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using WpfButton = System.Windows.Controls.Button;
using WpfControl = System.Windows.Controls.Control;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfTextBlock = System.Windows.Controls.TextBlock;

namespace WinPieGestures;

public partial class SettingsWindow
{
    private PinnedNavAction _recordingPinnedNavSlot = PinnedNavAction.None;
    private string? _pinnedNavStatusMessage;

    private bool IsPinnedNavKeyButton(DependencyObject? element) =>
        ReferenceEquals(element, PinnedNavUpKeyButton) ||
        ReferenceEquals(element, PinnedNavDownKeyButton) ||
        ReferenceEquals(element, PinnedNavLeftKeyButton) ||
        ReferenceEquals(element, PinnedNavRightKeyButton) ||
        ReferenceEquals(element, PinnedNavConfirmKeyButton);

    internal void StopPinnedNavRecording(bool clearStatus = true)
    {
        if (_recordingPinnedNavSlot == PinnedNavAction.None && (!clearStatus || _pinnedNavStatusMessage == null))
            return;

        _recordingPinnedNavSlot = PinnedNavAction.None;
        if (clearStatus) _pinnedNavStatusMessage = null;
        if (_recordingActivationProfile == null) ProfileTriggerRouter.IsRecording = false;
        RefreshPinnedNavKeyControls();
    }

    private void PinnedNavKeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditTriggerScene || sender is not WpfButton { Tag: string tag } ||
            !Enum.TryParse<PinnedNavAction>(tag, out var slot) || slot == PinnedNavAction.None)
            return;

        if (_recordingPinnedNavSlot == slot)
        {
            StopPinnedNavRecording();
            return;
        }

        if (_isRecordingTrigger) StopTriggerRecording(saved: false);
        if (_isRecordingProcessTrigger) StopProcessTriggerRecording(saved: false);
        StopProfileActivationRecording();
        CancelExclusiveRecordingIfActive();

        _recordingPinnedNavSlot = slot;
        ProfileTriggerRouter.IsRecording = true;
        _pinnedNavStatusMessage = I18n.TF("PinnedNavRecordingHint", PinnedNavRoleText(slot));
        RefreshPinnedNavKeyControls();
        (sender as WpfButton)?.Focus();
    }

    private void PinnedNavResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditTriggerScene || ConfigManager.CurrentConfig is not { } config) return;
        _recordingPinnedNavSlot = PinnedNavAction.None;
        if (_recordingActivationProfile == null) ProfileTriggerRouter.IsRecording = false;

        config.PinnedNavUpVkCode = PinnedNavBindings.Default.Up;
        config.PinnedNavDownVkCode = PinnedNavBindings.Default.Down;
        config.PinnedNavLeftVkCode = PinnedNavBindings.Default.Left;
        config.PinnedNavRightVkCode = PinnedNavBindings.Default.Right;
        config.PinnedNavConfirmVkCode = PinnedNavBindings.Default.Confirm;
        _pinnedNavStatusMessage = I18n.T("PinnedNavResetHint");
        SyncUiToConfigAndSave();
        RefreshPinnedNavKeyControls();
    }

    internal bool TryHandlePinnedNavRecordingKeyDown(WpfKeyEventArgs e)
    {
        if (_recordingPinnedNavSlot == PinnedNavAction.None) return false;
        e.Handled = true;

        Key actualKey = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            Key.DeadCharProcessed => e.DeadCharProcessedKey,
            _ => e.Key
        };
        uint vkCode = (uint)KeyInterop.VirtualKeyFromKey(actualKey);
        if (vkCode == 27)
        {
            StopPinnedNavRecording();
            return true;
        }
        if (KeyboardHook.IsModifierVk(vkCode))
        {
            _pinnedNavStatusMessage = I18n.T("PinnedNavModifierRejectedHint");
            RefreshPinnedNavKeyControls();
            return true;
        }
        if (!PinnedNavBindings.IsAllowedKey(vkCode))
        {
            _pinnedNavStatusMessage = I18n.T("PinnedNavInvalidKeyHint");
            RefreshPinnedNavKeyControls();
            return true;
        }
        if (ConfigManager.CurrentConfig is not { } config)
        {
            StopPinnedNavRecording();
            return true;
        }

        PinnedNavAction slot = _recordingPinnedNavSlot;
        AssignPinnedNavVk(config, slot, vkCode, out PinnedNavAction? swappedSlot);
        _recordingPinnedNavSlot = PinnedNavAction.None;
        if (_recordingActivationProfile == null) ProfileTriggerRouter.IsRecording = false;
        string keyLabel = PinnedNavBindings.ShortKeyLabel(vkCode);
        _pinnedNavStatusMessage = swappedSlot is { } swapped
            ? I18n.TF("PinnedNavSwappedHint", PinnedNavRoleText(slot), keyLabel, PinnedNavRoleText(swapped))
            : I18n.TF("PinnedNavSavedHint", PinnedNavRoleText(slot), keyLabel);
        SyncUiToConfigAndSave();
        RefreshPinnedNavKeyControls();
        return true;
    }

    private static void AssignPinnedNavVk(AppConfig config, PinnedNavAction targetSlot, uint newVk, out PinnedNavAction? swappedSlot)
    {
        var current = PinnedNavBindings.FromConfig(config);
        uint previousVk = GetPinnedNavSlotVk(current, targetSlot);
        swappedSlot = null;

        uint up = current.Up;
        uint down = current.Down;
        uint left = current.Left;
        uint right = current.Right;
        uint confirm = current.Confirm;

        foreach (var other in new[] { PinnedNavAction.Up, PinnedNavAction.Down, PinnedNavAction.Left, PinnedNavAction.Right, PinnedNavAction.Confirm })
        {
            if (other == targetSlot) continue;
            if (GetSlotRef(other) == newVk)
            {
                SetSlotRef(other, previousVk);
                swappedSlot = other;
                break;
            }
        }
        SetSlotRef(targetSlot, newVk);

        config.PinnedNavUpVkCode = up;
        config.PinnedNavDownVkCode = down;
        config.PinnedNavLeftVkCode = left;
        config.PinnedNavRightVkCode = right;
        config.PinnedNavConfirmVkCode = confirm;

        uint GetSlotRef(PinnedNavAction s) => s switch
        {
            PinnedNavAction.Up => up,
            PinnedNavAction.Down => down,
            PinnedNavAction.Left => left,
            PinnedNavAction.Right => right,
            PinnedNavAction.Confirm => confirm,
            _ => 0
        };

        void SetSlotRef(PinnedNavAction s, uint vk)
        {
            switch (s)
            {
                case PinnedNavAction.Up: up = vk; break;
                case PinnedNavAction.Down: down = vk; break;
                case PinnedNavAction.Left: left = vk; break;
                case PinnedNavAction.Right: right = vk; break;
                case PinnedNavAction.Confirm: confirm = vk; break;
            }
        }
    }

    private static uint GetPinnedNavSlotVk(PinnedNavBindings bindings, PinnedNavAction slot) => slot switch
    {
        PinnedNavAction.Up => bindings.Up,
        PinnedNavAction.Down => bindings.Down,
        PinnedNavAction.Left => bindings.Left,
        PinnedNavAction.Right => bindings.Right,
        PinnedNavAction.Confirm => bindings.Confirm,
        _ => 0
    };

    private static string PinnedNavRoleText(PinnedNavAction slot) => slot switch
    {
        PinnedNavAction.Up => I18n.T("PinnedNavKeyUpRole"),
        PinnedNavAction.Down => I18n.T("PinnedNavKeyDownRole"),
        PinnedNavAction.Left => I18n.T("PinnedNavKeyLeftRole"),
        PinnedNavAction.Right => I18n.T("PinnedNavKeyRightRole"),
        PinnedNavAction.Confirm => I18n.T("PinnedNavKeyConfirmRole"),
        _ => string.Empty
    };

    private void RefreshPinnedNavKeyControls()
    {
        if (PinnedNavKeysTitleText == null) return;

        var bindings = PinnedNavBindings.FromConfig(ConfigManager.CurrentConfig);
        PinnedNavKeysTitleText.Text = I18n.T("PinnedNavKeysTitle");
        PinnedNavKeysDescText.Text = I18n.T("PinnedNavKeysDesc");
        PinnedNavResetButton.Content = I18n.T("PinnedNavResetBtn");
        PinnedNavResetButton.IsEnabled = !bindings.IsDefault || _recordingPinnedNavSlot != PinnedNavAction.None;

        UpdateKeycap(PinnedNavUpKeyButton, PinnedNavUpKeyGlyph, PinnedNavUpKeyRole, PinnedNavAction.Up, bindings.Up);
        UpdateKeycap(PinnedNavDownKeyButton, PinnedNavDownKeyGlyph, PinnedNavDownKeyRole, PinnedNavAction.Down, bindings.Down);
        UpdateKeycap(PinnedNavLeftKeyButton, PinnedNavLeftKeyGlyph, PinnedNavLeftKeyRole, PinnedNavAction.Left, bindings.Left);
        UpdateKeycap(PinnedNavRightKeyButton, PinnedNavRightKeyGlyph, PinnedNavRightKeyRole, PinnedNavAction.Right, bindings.Right);
        UpdateKeycap(PinnedNavConfirmKeyButton, PinnedNavConfirmKeyGlyph, PinnedNavConfirmKeyRole, PinnedNavAction.Confirm, bindings.Confirm);

        if (PinnedNavStatusText != null)
        {
            if (_recordingPinnedNavSlot != PinnedNavAction.None)
            {
                PinnedNavStatusText.Text = string.IsNullOrEmpty(_pinnedNavStatusMessage)
                    ? I18n.TF("PinnedNavRecordingHint", PinnedNavRoleText(_recordingPinnedNavSlot))
                    : _pinnedNavStatusMessage;
                PinnedNavStatusText.SetResourceReference(WpfTextBlock.ForegroundProperty, "AccentPrimaryBrush");
                PinnedNavStatusText.Visibility = Visibility.Visible;
            }
            else if (!string.IsNullOrEmpty(_pinnedNavStatusMessage))
            {
                PinnedNavStatusText.Text = _pinnedNavStatusMessage;
                PinnedNavStatusText.SetResourceReference(WpfTextBlock.ForegroundProperty, "TextSecondaryBrush");
                PinnedNavStatusText.Visibility = Visibility.Visible;
            }
            else
            {
                PinnedNavStatusText.Text = string.Empty;
                PinnedNavStatusText.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void UpdateKeycap(WpfButton button, WpfTextBlock glyph, WpfTextBlock role, PinnedNavAction slot, uint vkCode)
    {
        if (button == null || glyph == null || role == null) return;
        bool recording = _recordingPinnedNavSlot == slot;
        string roleLabel = PinnedNavRoleText(slot);
        string keyLabel = PinnedNavBindings.ShortKeyLabel(vkCode);

        role.Text = roleLabel;
        glyph.Text = recording ? I18n.T("PinnedNavListeningPrompt") : keyLabel;
        glyph.FontSize = recording ? 11 : (keyLabel.Length > 4 ? 12 : 14);
        AutomationProperties.SetName(button, $"{roleLabel}: {keyLabel}");
        button.ToolTip = I18n.TF("PinnedNavRecordingHint", roleLabel);

        if (recording)
        {
            button.SetResourceReference(WpfControl.BackgroundProperty, "NavTabActiveBgBrush");
            button.SetResourceReference(WpfControl.BorderBrushProperty, "AccentPrimaryBrush");
            button.SetResourceReference(WpfControl.ForegroundProperty, "AccentPrimaryBrush");
        }
        else
        {
            button.ClearValue(WpfControl.BackgroundProperty);
            button.ClearValue(WpfControl.BorderBrushProperty);
            button.ClearValue(WpfControl.ForegroundProperty);
        }
    }
}
