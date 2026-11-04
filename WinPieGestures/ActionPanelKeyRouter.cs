using System;
using System.Threading;
using System.Windows.Input;

namespace WinPieGestures;

internal sealed record ActionPanelRoute(string Id, uint Key, ModifierKeys Modifiers);

internal sealed class ActionPanelKeyRouter
{
    private readonly int[] _ownedKeys = new int[256];
    private int _escapeDown;
    internal bool TryHandle(uint key, ModifierKeys modifiers, bool down, bool paused, bool hasPanel,
        ActionPanelRoute[] routes, out string? scheme, out bool cancel)
    {
        scheme = null; cancel = false;
        if (key == 27 && Volatile.Read(ref _escapeDown) != 0)
        { if (!down) Interlocked.Exchange(ref _escapeDown, 0); return true; }
        if (key == 0 || key >= _ownedKeys.Length) return false;
        if (Volatile.Read(ref _ownedKeys[key]) != 0)
        { if (!down) Interlocked.Exchange(ref _ownedKeys[key], 0); return true; }
        if (!down || paused) return false;
        if (key == 27 && hasPanel)
        { Interlocked.Exchange(ref _escapeDown, 1); cancel = true; return true; }
        foreach (var route in routes)
        {
            if (route.Key != key || route.Modifiers != modifiers) continue;
            Interlocked.Exchange(ref _ownedKeys[key], 1); scheme = route.Id; return true;
        }
        return false;
    }
}
