using System.Windows;
using System.Windows.Input;

namespace WinPieGestures;

internal readonly record struct PinnedArrowAddress(int Level, int Index, int Owner = -1);
internal readonly record struct PinnedArrowSlot(PinnedArrowAddress Address, Point Center);

internal enum PinnedNavAction
{
    None,
    Left,
    Up,
    Right,
    Down,
    Confirm
}

internal sealed record PinnedNavBindings(uint Up, uint Down, uint Left, uint Right, uint Confirm)
{
    internal static readonly PinnedNavBindings Default = new(
        PinnedArrowNavigation.Up,
        PinnedArrowNavigation.Down,
        PinnedArrowNavigation.Left,
        PinnedArrowNavigation.Right,
        PinnedArrowNavigation.Confirm);

    private static readonly uint[] FallbackPool =
    [
        PinnedArrowNavigation.Up,
        PinnedArrowNavigation.Down,
        PinnedArrowNavigation.Left,
        PinnedArrowNavigation.Right,
        PinnedArrowNavigation.Confirm,
        0x57, // W
        0x53, // S
        0x41, // A
        0x44, // D
        0x20  // Space
    ];

    internal bool IsDefault =>
        Up == Default.Up &&
        Down == Default.Down &&
        Left == Default.Left &&
        Right == Default.Right &&
        Confirm == Default.Confirm;

    internal static bool IsAllowedKey(uint vkCode)
    {
        if (vkCode is 0 or >= 255 or <= 0x06 or 0x1B or 0xE7)
        {
            return false;
        }
        if (KeyboardHook.IsModifierVk(vkCode))
        {
            return false;
        }
        return KeyInterop.KeyFromVirtualKey((int)vkCode) != Key.None;
    }

    internal static PinnedNavBindings FromConfig(AppConfig? config)
    {
        if (config == null) return Default;

        uint up = ResolveDistinct(config.PinnedNavUpVkCode, Default.Up, 0, 0, 0, 0);
        uint down = ResolveDistinct(config.PinnedNavDownVkCode, Default.Down, up, 0, 0, 0);
        uint left = ResolveDistinct(config.PinnedNavLeftVkCode, Default.Left, up, down, 0, 0);
        uint right = ResolveDistinct(config.PinnedNavRightVkCode, Default.Right, up, down, left, 0);
        uint confirm = ResolveDistinct(config.PinnedNavConfirmVkCode, Default.Confirm, up, down, left, right);
        return new PinnedNavBindings(up, down, left, right, confirm);

        static uint ResolveDistinct(uint candidate, uint fallback, uint u1, uint u2, uint u3, uint u4)
        {
            bool Used(uint v) => v == u1 || v == u2 || v == u3 || v == u4;
            if (IsAllowedKey(candidate) && !Used(candidate)) return candidate;
            if (IsAllowedKey(fallback) && !Used(fallback)) return fallback;
            foreach (uint alt in FallbackPool)
            {
                if (!Used(alt)) return alt;
            }
            return fallback;
        }
    }

    internal PinnedNavAction Match(uint vkCode)
    {
        if (vkCode == 0) return PinnedNavAction.None;
        if (vkCode == Up) return PinnedNavAction.Up;
        if (vkCode == Down) return PinnedNavAction.Down;
        if (vkCode == Left) return PinnedNavAction.Left;
        if (vkCode == Right) return PinnedNavAction.Right;
        if (vkCode == Confirm) return PinnedNavAction.Confirm;
        return PinnedNavAction.None;
    }

    internal uint DirectionVkFor(PinnedNavAction action) => action switch
    {
        PinnedNavAction.Left => PinnedArrowNavigation.Left,
        PinnedNavAction.Up => PinnedArrowNavigation.Up,
        PinnedNavAction.Right => PinnedArrowNavigation.Right,
        PinnedNavAction.Down => PinnedArrowNavigation.Down,
        _ => 0
    };

    internal static string ShortKeyLabel(uint vkCode) => vkCode switch
    {
        PinnedArrowNavigation.Up => "↑",
        PinnedArrowNavigation.Down => "↓",
        PinnedArrowNavigation.Left => "←",
        PinnedArrowNavigation.Right => "→",
        PinnedArrowNavigation.Confirm => "Enter ↵",
        0x20 => "Space",
        0x09 => "Tab",
        0x08 => "Bksp",
        0x14 => "Caps",
        0x2E => "Del",
        0x2D => "Ins",
        0x21 => "PgUp",
        0x22 => "PgDn",
        _ => FormatFallbackKeyLabel(vkCode)
    };

    private static string FormatFallbackKeyLabel(uint vkCode)
    {
        if (vkCode == 0) return "?";
        Key key = KeyInterop.KeyFromVirtualKey((int)vkCode);
        if (key == Key.None) return $"VK {vkCode}";
        return HotkeyRecorderBox.FormatKeyName(key);
    }
}

internal static class PinnedArrowNavigation
{
    internal const uint Left = 0x25, Up = 0x26, Right = 0x27, Down = 0x28, Confirm = 0x0D;

    internal static bool IsArrow(uint key) => key is >= Left and <= Down;

    internal static int IndexOf(ReadOnlySpan<PinnedArrowSlot> slots, PinnedArrowAddress? address)
    {
        if (!address.HasValue) return -1;
        for (int i = 0; i < slots.Length; i++)
            if (slots[i].Address == address.Value) return i;
        return -1;
    }

    internal static int Next(ReadOnlySpan<PinnedArrowSlot> slots, int current, uint key)
    {
        if (!IsArrow(key)) return -1;
        Point origin = current < 0 ? default : slots[current].Center;
        bool horizontal = key is Left or Right;
        int sign = key is Left or Up ? -1 : 1;
        double bestCost = double.PositiveInfinity;
        int best = -1;
        for (int i = 0; i < slots.Length; i++)
        {
            if (i == current) continue;
            var delta = slots[i].Center - origin;
            double forward = sign * (horizontal ? delta.X : delta.Y);
            if (forward <= 1) continue;
            double sideways = horizontal ? delta.Y : delta.X;
            double cost = forward * forward + 4 * sideways * sideways;
            if (cost >= bestCost) continue;
            bestCost = cost;
            best = i;
        }
        if (best >= 0 || current >= 0) return best;

        // Sparse wheels can have no visible sector in the first requested direction.
        for (int i = 0; i < slots.Length; i++)
        {
            double cost = ((Vector)slots[i].Center).LengthSquared;
            if (cost >= bestCost) continue;
            bestCost = cost;
            best = i;
        }
        return best;
    }
}

internal enum PinnedArrowKeyEvent { Native, Repeat, Owned }

// Ownership belongs to the physical press, so it survives a close, focus change or pause.
internal sealed class PinnedArrowKeys
{
    private const byte Held = 1;
    private const byte Owned = 2;
    private const int ScanSlots = 512;
    private readonly byte[] _state = new byte[ScanSlots + 256];
    private readonly ulong[] _heldAtInstall = new ulong[4];

    internal void Initialize(Func<uint, bool> isDown)
    {
        Array.Clear(_state);
        Array.Clear(_heldAtInstall);
        for (uint key = 1; key < 255; key++)
        {
            if (PinnedNavBindings.IsAllowedKey(key) && isDown(key))
                SetHeldAtInstall(key);
        }
    }

    internal PinnedArrowKeyEvent Observe(uint key, uint scan, uint flags, bool down)
    {
        int identity = Identity(key, scan, flags);
        if (identity < 0) return PinnedArrowKeyEvent.Native;
        byte previous = _state[identity];
        bool trackKey = key < 256 && PinnedNavBindings.IsAllowedKey(key);
        if (previous == 0 && !trackKey) return PinnedArrowKeyEvent.Native;
        bool initiallyHeld = key < 256 && IsHeldAtInstall(key);
        _state[identity] = down ? (byte)(Held | (previous & Owned)) : (byte)0;
        if (!down && key < 256) ClearHeldAtInstall(key);
        if ((previous & Owned) != 0) return PinnedArrowKeyEvent.Owned;
        return down && (previous != 0 || initiallyHeld) ? PinnedArrowKeyEvent.Repeat : PinnedArrowKeyEvent.Native;
    }

    internal void Own(uint key, uint scan, uint flags)
    {
        int identity = Identity(key, scan, flags);
        if (identity >= 0) _state[identity] |= Owned;
    }

    private bool IsHeldAtInstall(uint key) => (_heldAtInstall[key >> 6] & (1UL << (int)(key & 63))) != 0;
    private void SetHeldAtInstall(uint key) => _heldAtInstall[key >> 6] |= 1UL << (int)(key & 63);
    private void ClearHeldAtInstall(uint key) => _heldAtInstall[key >> 6] &= ~(1UL << (int)(key & 63));

    // A dedicated arrow and its numeric keypad counterpart can share a VK but differ in the extended flag.
    private static int Identity(uint key, uint scan, uint flags) => scan is > 0 and < 256
        ? (int)(scan | ((flags & 1) << 8))
        : key < 256 ? ScanSlots + (int)key : -1;
}
