using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using StarPie.Plugin;
using WinPieGestures.Plugins;

namespace WinPieGestures;

internal static class ActionPanelManager
{
    private static ActionPanelRoute[] _routes = Array.Empty<ActionPanelRoute>();
    private static ActionPanelWindow? _window;
    private static readonly ActionPanelKeyRouter KeyRouter = new();
    private static int _postPending;
    private static long _sessionRevision;

    internal static void Initialize()
    { ConfigManager.ConfigurationChanged += Refresh; Refresh(); }
    private static void Refresh()
    {
        var config = ConfigManager.CurrentConfig;
        var schemes = config.ActionPanels;
        if (!ActionPanelScheme.Validate(schemes)) { Volatile.Write(ref _routes, Array.Empty<ActionPanelRoute>()); Close(); return; }
        var routes = schemes.Where(item => !string.IsNullOrWhiteSpace(item.Hotkey))
            .Select(item => ActionPanelHotkey.TryParse(item.Hotkey, out uint key, out var mods) && !ActionPanelHotkey.ConflictsWithWheels(key, mods, config)
                ? new ActionPanelRoute(item.Id, key, mods) : null).Where(item => item != null).Cast<ActionPanelRoute>().ToArray();
        Volatile.Write(ref _routes, routes); Close();
    }
    // Hook callback: bounded immutable route scan, atomics and one UI post. No parsing or I/O.
    internal static bool HandleKey(uint key, ModifierKeys modifiers, bool down)
    {
        bool handled = KeyRouter.TryHandle(key, modifiers, down, App.IsExiting || App.MainKeyboardHook?.IsPaused == true,
            Volatile.Read(ref _window) != null, Volatile.Read(ref _routes), out string? id, out bool cancel);
        if (cancel) Close();
        if (id != null) RequestShow(id);
        return handled;
    }
    internal static void RequestShow(string id, nint? expectedTarget = null, uint expectedPid = 0, Func<bool>? isCurrent = null)
    {
        nint target = expectedTarget ?? WheelFocusSwitcher.GetForegroundWindowSafe();
        uint pid = expectedTarget.HasValue ? expectedPid : ActiveWindowHelper.GetWindowRealProcessId(target);
        long revision = Volatile.Read(ref _sessionRevision);
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || Interlocked.Exchange(ref _postPending, 1) != 0) return;
        dispatcher.BeginInvoke((Action)(() =>
        {
            Interlocked.Exchange(ref _postPending, 0);
            if (revision != Volatile.Read(ref _sessionRevision) || App.IsExiting || App.MainKeyboardHook?.IsPaused == true) return;
            // A new gesture can supersede this request while it waits on the UI dispatcher.
            if (isCurrent != null && !isCurrent()) return;
            if (_window?.SchemeId == id) { Close(); return; }
            Close(); InputPanelCoordinator.CloseActive(InputPanelCloseReason.Replaced);
            if (target == 0 || pid == 0 || pid == (uint)Environment.ProcessId || !InputPanelCoordinator.IsTargetValid(target, pid)) return;
            var scheme = ConfigManager.CurrentConfig.ActionPanels?.FirstOrDefault(item => item.Id == id);
            if (scheme == null || !ActionPanelScheme.Validate(new[] { scheme })) return;
            var cursor = System.Windows.Forms.Cursor.Position;
            var context = new ActionContext { ForegroundWindowHandle = target,
                ForegroundProcessName = ActiveWindowHelper.GetProcessNameForWindow(target),
                CursorX = cursor.X, CursorY = cursor.Y, IsElevated = ConfigManager.IsElevated(), LanguageCode = I18n.CurrentLanguageCode };
            var panel = new ActionPanelWindow(scheme.Clone(), target, pid, context);
            panel.Closed += (_, _) => { if (ReferenceEquals(_window, panel)) Volatile.Write(ref _window, null); };
            Volatile.Write(ref _window, panel);
            try { panel.Open(); }
            catch (Exception ex) { panel.Close(); AppLogger.LogError("Opening the action panel failed", ex); }
        }));
    }
    internal static void Close()
    {
        Interlocked.Increment(ref _sessionRevision);
        var panel = Volatile.Read(ref _window);
        panel?.Cancel();
    }
    internal static ActionItem? Prepare(ActionItem action)
    {
        if (action.Type is "Hotkey" or "Text" or "String" or "Plugin") return action.Clone();
        if (!PluginHost.TryResolveClaimedType(action.Type, out var claim)) return null;
        return PluginInputPanelService.BuildAction(new ConfiguredPluginAction
        { ActionId = claim.FullId, Parameters = ActionParameterProjection.Project(action) });
    }
}
