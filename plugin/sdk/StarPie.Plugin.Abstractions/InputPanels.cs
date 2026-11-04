namespace StarPie.Plugin;

/// <summary>SDK 1.15 optional context for a plugin-owned, nonactivating input panel.</summary>
public interface IInputPanelPluginContext : IPluginContext
{
    IHostInputPanelService InputPanels { get; }
}

/// <summary>Serializable plugin action reference. Does not expose host configuration types.</summary>
public sealed class ConfiguredPluginAction
{
    public string ActionId { get; init; } = "";
    public IReadOnlyDictionary<string, string> Parameters { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed class PluginActionBindingInfo
{
    public bool IsAvailable { get; init; }
    public string DisplayName { get; init; } = "";
    public string? UnavailableReason { get; init; }
}

/// <summary>Theme values for plugin-owned UI, without a WPF dependency in the SDK.</summary>
public sealed class InputPanelAppearance
{
    public string Background { get; init; } = "#F8FAFC";
    public string Surface { get; init; } = "#FFFFFF";
    public string Foreground { get; init; } = "#172331";
    public string Border { get; init; } = "#CBD5E1";
    public string Accent { get; init; } = "#526F55";
    public string AccentForeground { get; init; } = "#FFFFFF";
}

public enum InputPanelCloseReason { Disposed, Escape, TargetChanged, Paused, PluginStopped, Replaced }
public enum InputPanelQueueResult { Accepted, Closed, Invalid, Busy, Unavailable }

/// <summary>
/// Target-bound queue access. Open and editing run on the host UI thread, outside hooks.
/// Opening requires Ui and InputSimulation; binding inspection, editing and dispatch also
/// require PluginActions. No method changes a plugin's enabled preference.
/// </summary>
public interface IHostInputPanelService
{
    /// <summary>Read the current host palette on the UI dispatcher.</summary>
    InputPanelAppearance GetAppearance();

    /// <summary>
    /// Bind to the action's still-foreground target. The host calls onClosed on its UI
    /// thread while the owner generation accepts callbacks. Plugin shutdown must also
    /// close its window; a stopping plugin is not called back through a stale delegate.
    /// </summary>
    IInputPanelSession Open(ActionContext target, Action<InputPanelCloseReason> onClosed);

    /// <summary>Read-only availability; does not load or enable the target plugin.</summary>
    PluginActionBindingInfo DescribeAction(ConfiguredPluginAction binding);

    /// <summary>
    /// Resolve a saved reference when the user opens its custom panel page. May load
    /// the referenced enabled plugin for action metadata; never enables or executes it.
    /// </summary>
    Task<PluginActionBindingInfo> ResolveActionAsync(ConfiguredPluginAction binding,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Explicitly open the host action picker and parameter form. Only the selected,
    /// already enabled plugin may be loaded. Returns null on cancel and never executes.
    /// Input sessions must be closed before entering this activating editor.
    /// </summary>
    Task<ConfiguredPluginAction?> EditActionAsync(ConfiguredPluginAction? current,
        long ownerWindowHandle = 0, CancellationToken cancellationToken = default);
}

/// <summary>
/// One original target shared by text, key and configured actions. Queue acceptance is
/// not execution success: the host rechecks focus and owner generation when consumed.
/// Closing invalidates pending input. Busy means this session's bounded queue is full.
/// Disposing does not inject key releases because modifiers are never held across calls.
/// </summary>
public interface IInputPanelSession : IDisposable
{
    bool IsOpen { get; }
    InputPanelQueueResult QueueText(string text);
    InputPanelQueueResult QueueHotkey(string hotkey);
    InputPanelQueueResult QueueAction(ConfiguredPluginAction binding);
}
