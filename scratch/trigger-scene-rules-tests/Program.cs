using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Input;
using WinPieGestures;

string candidateDir = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
	.Single(attribute => attribute.Key == "StarPieCandidateDir").Value!;
string candidatePath = Path.GetFullPath(Path.Combine(candidateDir, "StarPie.dll"));
Assembly host = typeof(AppConfig).Assembly;
string candidateHash = Sha256(candidatePath);
if (!string.Equals(Sha256(host.Location), candidateHash, StringComparison.Ordinal))
	throw new InvalidOperationException("Loaded StarPie.dll differs from the requested candidate.");
Console.WriteLine($"Candidate {candidatePath} SHA256 {candidateHash}");

Type rules = host.GetType("WinPieGestures.TriggerSceneRules", throwOnError: true)!;
Type inputs = host.GetType("WinPieGestures.TriggerSceneInput", throwOnError: true)!;
MethodInfo simulate = rules.GetMethod("Simulate", BindingFlags.Static | BindingFlags.NonPublic)!;
FieldInfo publishedRoutes = host.GetType("WinPieGestures.ProfileTriggerRouter", throwOnError: true)!
	.GetField("_snapshot", BindingFlags.Static | BindingFlags.NonPublic)!;
object? originalSnapshot = publishedRoutes.GetValue(null);
int checks = 0;

var config = new AppConfig
{
	Profiles = [new WheelProfile { ProcessName = "Global" }],
	BlacklistedProcesses = ["  CAD.EXE  "],
	WhitelistedProcesses = ["editor.exe"],
	DisableOnCtrl = true,
	DisableOnShift = true,
	DisableOnAlt = true
};

Expect("MissingProcess", "  ", "Global");
Expect("MissingOverride", "editor.exe", "ProcessOverride");
Expect("Excluded", " CAD ", "Global");
Expect("WheelAllowed", "editor.exe", "Global", fullScreen: true);
Expect("FullScreenBypass", "other.exe", "Global", fullScreen: true);
Expect("ModifierBypass", "editor.exe", "Global", modifiers: ModifierKeys.Control);

config.IsolationMode = "Whitelist";
Expect("OutsideAllowList", "cad.exe", "Global");
Expect("WheelAllowed", "editor.exe", "Global", fullScreen: true);
config.IsolationMode = "Blacklist";

config.Trigger.RequireShift = true;
Expect("WheelAllowed", "editor.exe", "Global");
Expect("ModifierBypass", "editor.exe", "Global", modifiers: ModifierKeys.Alt);
config.Trigger = new TriggerConfig { TriggerType = "Keyboard", VkCode = 0x41, RequireCtrl = true };
Expect("WheelAllowed", "editor.exe", "Global");
Expect("ModifierBypass", "editor.exe", "Global", modifiers: ModifierKeys.Shift);
config.Trigger.VkCode = 0;
Expect("UnspecifiedTriggerKey", "editor.exe", "Global");
config.Trigger = new TriggerConfig();
config.Trigger.MouseButton = "Unknown legacy value";
Expect("WheelAllowed", "editor.exe", "Global");
config.Trigger = new TriggerConfig();

config.BlacklistTriggerOverrides["cad.exe"] = new TriggerConfig { MouseButton = "XButton1", RequireAlt = true };
Expect("NoMatchingTrigger", "cad.exe", "Global");
Expect("WheelAllowed", "cad.exe", "ProcessOverride");
Expect("ModifierBypass", "cad.exe", "ProcessOverride", modifiers: ModifierKeys.Control);
config.IsolationMode = "Whitelist";
Expect("OutsideAllowList", "cad.exe", "ProcessOverride");
config.IsolationMode = "Blacklist";

Expect("GestureDisabled", "editor.exe", "MouseGesture");
config.GestureEnabled = true;
config.LongPressOnlyTrigger = true;
Expect("GestureAllowed", "editor.exe", "MouseGesture");
config.LongPressTrigger = true;
Expect("GestureOnHoldOnly", "editor.exe", "MouseGesture");
config.LongPressTrigger = false;
Expect("Excluded", "cad.exe", "MouseGesture", modifiers: ModifierKeys.Alt);
Expect("ModifierBypass", "editor.exe", "MouseGesture", modifiers: ModifierKeys.Control);
config.Trigger.RequireCtrl = true;
Expect("GestureAllowed", "editor.exe", "MouseGesture", modifiers: ModifierKeys.Control);
config.Trigger.RequireCtrl = false;
config.GestureTriggerButton = "RightButton";
Expect("WheelPriority", "editor.exe", "MouseGesture");
config.GestureTriggerButton = "UnavailableButton";
Expect("NoMatchingTrigger", "editor.exe", "MouseGesture");
config.GestureTriggerButton = "RightButton";

var dedicatedTrigger = new TriggerConfig();
config.Profiles.Add(DedicatedProfile("CAD wheel", "cad.exe", dedicatedTrigger));
Expect("Excluded", "cad.exe", "Global");
Expect("WheelPriority", "cad.exe", "MouseGesture");
config.Profiles.Add(DedicatedProfile("Second CAD wheel", "cad.exe", dedicatedTrigger));
Expect("TriggerConflict", "cad.exe", "Global");
Expect("WheelPriority", "cad.exe", "MouseGesture");

if (!ReferenceEquals(originalSnapshot, publishedRoutes.GetValue(null)))
	throw new InvalidOperationException("Simulation replaced the published live routing snapshot.");
Console.WriteLine($"PASS {checks} scenario checks. The live routing snapshot was preserved.");

void Expect(string expected, string processName, string input, bool fullScreen = false, ModifierKeys modifiers = ModifierKeys.None)
{
	object? actual = simulate.Invoke(null, [config, processName, Enum.Parse(inputs, input), fullScreen, modifiers]);
	if (!string.Equals(actual?.ToString(), expected, StringComparison.Ordinal))
		throw new InvalidOperationException($"{input} with {processName} expected {expected}, got {actual} (check {checks + 1}).");
	checks++;
}

static WheelProfile DedicatedProfile(string name, string processName, TriggerConfig trigger) => new()
{
	ProcessName = name,
	BoundProcesses = processName,
	HasExplicitProcessBindings = true,
	Layers = [new WheelLayer { ActivationTrigger = trigger }]
};

static string Sha256(string path)
{
	using var stream = File.OpenRead(path);
	return Convert.ToHexString(SHA256.HashData(stream));
}
