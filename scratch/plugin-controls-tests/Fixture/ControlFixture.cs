using StarPie.Plugin;

public sealed class ControlFixture : IStarPiePlugin
{
    public void Initialize(IPluginContext context)
    {
        context.Actions.Register(new SafeAction("toggle", true));
        context.Actions.Register(new SafeAction("legacy", false));
        context.Actions.Register(new SafeAction("manage", false));
        context.Actions.Register(new SafeAction("keypadLayer", false));
#if !SELFTEST_CONTRIBUTION_ONLY
        context.SettingsPage.Register(new SettingsPageDescriptor { Title = "Fixture settings", ActionIds = new[] { "manage" } });
#endif
    }
    public void Shutdown() { }
}

internal sealed class SafeAction(string id, bool visible) : IActionContribution
{
    public ActionDescriptor Descriptor => new() { Id = id, DisplayName = id, ShowInActionPicker = visible };
    public IReadOnlyList<ParameterField> Parameters => id == "keypadLayer"
        ? new[] { new ParameterField { Key = "keyMap", Label = "Key map", Type = ParameterFieldType.KeyMap, Required = true } }
        : Array.Empty<ParameterField>();
    public string? Validate(IReadOnlyDictionary<string, string> parameters) => null;
    public string Preview(IReadOnlyDictionary<string, string> parameters) => "fixture";
    public Task<ActionResult> ExecuteAsync(PluginActionInput input, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("This fixture must never be invoked by the contract tests.");
}
