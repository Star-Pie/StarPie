using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StarPie.Plugin;

namespace AuditOptFixture;

public sealed class FixturePlugin : IStarPiePlugin
{
    private string _lastLanguage = "";
    private int _wheelClosedCount;

    public void Initialize(IPluginContext context)
    {
        // Deliberately do not store or dispose the returned token when failInit is set:
        // the host must revoke event subscriptions and registered tokens when Initialize throws.
        _ = context.Events.OnLanguageChanged(lang => _lastLanguage = lang);
        _ = context.Events.OnWheelClosed(() => _wheelClosedCount++);
        context.Actions.Register(new KeypadProbeAction());

        if (context.Settings.GetBool("failInit"))
        {
            throw new InvalidOperationException("Simulated Initialize failure after subscribing to static host events.");
        }
    }

    public void Shutdown()
    {
    }

    private sealed class KeypadProbeAction : IActionContribution
    {
        public ActionDescriptor Descriptor { get; } = new()
        {
            Id = "keypadLayer",
            DisplayName = "Keypad Layer Probe",
            Description = "Synthetic probe action for headless self-test and ALC unload verification.",
            Kind = ActionKind.Sequential,
            TimeoutSeconds = 5,
        };

        public IReadOnlyList<ParameterField> Parameters { get; } = new[]
        {
            new ParameterField
            {
                Key = "keyMap",
                Label = "Key Map",
                Type = ParameterFieldType.KeyMap,
                Required = true,
                DefaultValue = "v1|A:Space",
            }
        };

        public string? Validate(IReadOnlyDictionary<string, string> parameters) => null;

        public string Preview(IReadOnlyDictionary<string, string> parameters) => "Keypad Layer Probe";

        public Task<ActionResult> ExecuteAsync(PluginActionInput input, CancellationToken cancellationToken) =>
            Task.FromResult(ActionResult.Ok("noop"));
    }
}

