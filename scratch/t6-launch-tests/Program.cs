using System.Drawing;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using StarPie.Plugin;
using WinPieGestures;
using TriggerPoint = System.Windows.Point;

internal static class Program
{
    private const string PluginId = "starpie.builtin.launch";
    private static int _checks;
    private static void Check(bool value, string name)
    {
        if (!value) throw new InvalidOperationException(name);
        Console.WriteLine("PASS " + name);
        _checks++;
    }

    private static void Main()
    {
        var fixture = Directory.CreateTempSubdirectory("StarPieLaunchPlacement-");
        string user = Path.Combine(fixture.FullName, "user");
        Environment.SetEnvironmentVariable("LOCALAPPDATA", user);
        Environment.SetEnvironmentVariable("APPDATA", user);
        try
        {
            string candidate = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "CandidateDll").Value!;
            using var expected = File.OpenRead(candidate);
            using var actual = File.OpenRead(typeof(ActionItem).Assembly.Location);
            Check(SHA256.HashData(actual).SequenceEqual(SHA256.HashData(expected)), "executing exact candidate");
            var launch = JsonSerializer.Deserialize<ActionItem>("{\"Type\":\"Launch\"}")!;
            Check(!launch.LaunchOnCursorMonitor, "legacy action preserves launch policy");
            var source = new ActionItem { Type="Plugin", PluginActionRef=new() {PluginId=PluginId, ContributionId="launch"}, LaunchOnCursorMonitor=true };
            Check(LaunchMonitorPlacement.IsSupportedAction(launch) && LaunchMonitorPlacement.IsSupportedAction(source),
                "legacy launch claim and official explicit reference");
            Check(!LaunchMonitorPlacement.IsSupportedAction(new ActionItem { Type="Plugin", PluginActionRef=new() {PluginId=PluginId, ContributionId="different"} }) &&
                  !LaunchMonitorPlacement.IsSupportedAction(new ActionItem { Type="Plugin", PluginActionRef=new() {PluginId="other", ContributionId="launch"} }),
                "unrelated contributions do not opt in");
            var parent = new ActionItem {Type="Submenu", SubActions={source}};
            var cloned = JsonSerializer.Deserialize<ActionItem>(JsonSerializer.Serialize(parent.Clone()))!.SubActions.Single();
            Check(cloned.LaunchOnCursorMonitor && cloned.PluginActionRef!.FullId==source.PluginActionRef!.FullId, "nested action copy and JSON round-trip");
            source.LaunchOnCursorMonitor=false;
            Check(JsonSerializer.Serialize(source).Contains("\"LaunchOnCursorMonitor\":false"), "disabled preference can be explicitly saved");

            int calls=0;
            string? Lookup(TriggerPoint? value)
            {
                calls++;
                return value==new TriggerPoint(-25,13) ? "Left" : "Right";
            }
            Check(ActionEnvelope.Snapshot(launch, null, new TriggerPoint(0,0), Lookup).LaunchMonitor==null && calls==0,
                "default launch does not query monitor");
            var unrelated=new ActionItem {Type="Text", LaunchOnCursorMonitor=true};
            Check(ActionEnvelope.Snapshot(unrelated,null,null,Lookup).LaunchMonitor==null && calls==0, "inactive field on other types does not query monitor");
            var first=ActionEnvelope.Snapshot(cloned,null,new TriggerPoint(-25,13),Lookup);
            var second=ActionEnvelope.Snapshot(cloned,null,new TriggerPoint(150,13),Lookup);
            cloned.LaunchOnCursorMonitor=false;
            Check(first.LaunchMonitor=="Left" && second.LaunchMonitor=="Right" && calls==2 && first.Action.LaunchOnCursorMonitor,
                "per-action physical point and preference captured before queue processing");
            var observed=new List<string?>();
            try
            {
                ActionExecutor.ActionExecutionOverride=(_,_) =>
                {
                    observed.Add(Task.Run(() =>
                    {
                        Check(LaunchMonitorPlacement.TakeForPlugin("other")==null,"unrelated plugin cannot consume location");
                        string? result=LaunchMonitorPlacement.TakeForPlugin(PluginId);
                        Check(LaunchMonitorPlacement.TakeForPlugin(PluginId)==null,"one launch consumes location once");
                        return result;
                    }).GetAwaiter().GetResult());
                };
                Check(ActionExecutor.ProcessActionEnvelope(second) && ActionExecutor.ProcessActionEnvelope(first) && observed.SequenceEqual(new[]{"Right","Left"}),
                    "consume in reverse order without rebinding cursor or crossing invocation Tasks");
                var invalid = new ActionEnvelope(first.Action, new ActionExecutionContext(0,0,"",() => false), "Left");
                Check(!ActionExecutor.ProcessActionEnvelope(invalid) && observed.Count==2, "invalid gesture target prevents dispatch");
                Check(LaunchMonitorPlacement.TakeForPlugin(PluginId)==null,"queue clears monitor context");
            }
            finally { ActionExecutor.ActionExecutionOverride=null; }

            const string path=@"C:\Tools\Cad.exe";
            var started=DateTime.UtcNow;
            Check(LaunchMonitorPlacement.MatchesNewProcess(path,path,started,started), "fresh matching executable can own a window");
            Check(!LaunchMonitorPlacement.MatchesNewProcess(path,path,started.AddSeconds(-1),started), "pre-existing returned process ignored");
            Check(!LaunchMonitorPlacement.MatchesNewProcess(path,@"C:\Other\Cad.exe",started,started), "different executable cannot receive placement");
            Check(LaunchMonitorPlacement.CanTrack("Left",path,ProcessLaunchMode.Default,false) &&
                !LaunchMonitorPlacement.CanTrack("Left",path,ProcessLaunchMode.Administrator,false) &&
                !LaunchMonitorPlacement.CanTrack("Left",path,ProcessLaunchMode.StandardUser,false) &&
                !LaunchMonitorPlacement.CanTrack("Left","drawing.pdf",ProcessLaunchMode.Default,false) &&
                !LaunchMonitorPlacement.CanTrack("Left",path,ProcessLaunchMode.Default,true), "only traceable ordinary executable launch qualifies");
            int toggles = 0, starts = 0, follows = 0;
            LaunchWindowToggleResult Toggle(string _) { toggles++; return LaunchWindowToggleResult.Activated; }
            bool Start(System.Diagnostics.ProcessStartInfo info, ProcessLaunchMode mode) { starts++; return true; }
            bool Follow(System.Diagnostics.ProcessStartInfo info, string device)
            {
                follows++;
                return info.FileName == path && device == "Left";
            }
            Check(ActionExecutor.ExecuteLaunchWithMode(path, "", ProcessLaunchMode.Default, Toggle, Start, "Left", Follow) &&
                follows == 1 && starts == 0 && toggles == 0, "existing target with opt-in follows instead of legacy toggle");
            Check(ActionExecutor.ExecuteLaunchWithMode(path, "", ProcessLaunchMode.Default, Toggle, Start) &&
                follows == 1 && starts == 0 && toggles == 1, "option off preserves ordinary existing-window toggle");
            Check(ActionExecutor.ExecuteLaunchWithMode(path, "/edit", ProcessLaunchMode.Default, Toggle, Start, "Left",
                (info, device) => info.Arguments == "/edit" && Follow(info, device)) && follows == 2 && toggles == 1,
                "original path and arguments reach placement backend exactly once");
            ActionExecutor.ExecuteLaunchWithMode(path, "", ProcessLaunchMode.StandardUser, Toggle, Start, "Left", Follow);
            ActionExecutor.ExecuteLaunchWithMode(path, "", ProcessLaunchMode.Administrator, Toggle, Start, "Left", Follow);
            Check(follows == 2 && toggles == 2 && starts == 1, "permission modes preserve ordinary dispatch");
            WindowSnapshot Window(int pid=11,long style=0,nint owner=0,bool minimized=false,bool visible=true) =>
                new(){Handle=10,ProcessId=pid,Title="Main",ClassName="FixtureMain",Width=640,Height=480,IsVisible=visible,IsIconic=minimized,Owner=owner,ExStyle=style};
            Check(LaunchMonitorPlacement.IsOwnedMainWindow(Window(),11), "matching visible main window");
            Check(!LaunchMonitorPlacement.IsOwnedMainWindow(Window(pid:12),11) && !LaunchMonitorPlacement.IsOwnedMainWindow(Window(owner:2),11) &&
                !LaunchMonitorPlacement.IsOwnedMainWindow(Window(style:ProcessWindowSelector.WS_EX_TOOLWINDOW),11) &&
                !LaunchMonitorPlacement.IsOwnedMainWindow(Window(minimized:true),11) &&
                !LaunchMonitorPlacement.IsOwnedMainWindow(Window(visible:false),11), "foreign, owned, tool, minimized and hidden windows are ignored");
            var owners = new HashSet<int> {11};
            var hidden = Window(visible:false);
            var minimized = Window(minimized:true);
            var main = Window();
            Check(LaunchMonitorPlacement.SelectUserWindow([Window(pid:12), Window(owner:2), Window(style:ProcessWindowSelector.WS_EX_NOACTIVATE), Window(style:ProcessWindowSelector.WS_EX_TOOLWINDOW), hidden], owners, true) == null,
                "recall excludes other PIDs, auxiliary HWNDs, and an entirely hidden tray");
            Check(LaunchMonitorPlacement.SelectUserWindow([minimized], owners, true) == minimized &&
                LaunchMonitorPlacement.SelectUserWindow([minimized], owners, false) == null, "minimized recalled, never moved until restored");
            var hiddenIconic = Window(visible:false, minimized:true);
            Check(LaunchMonitorPlacement.SelectUserWindow([hiddenIconic], owners, true) == null &&
                Observe(hiddenIconic, hiddenIconic, main, main) == main, "hidden iconic tray needs native application wake first");
            var foreground = new WindowSnapshot { Handle=14, ProcessId=11, Width=500, Height=400, IsForeground=true, IsVisible=true };
            Check(LaunchMonitorPlacement.SelectUserWindow([main, foreground], owners, true) == foreground,
                "foreground wins among matching windows of the same app");
            Check(LaunchMonitorPlacement.IsRecallReplacement(foreground,11) && !LaunchMonitorPlacement.IsRecallReplacement(foreground,12) &&
                !LaunchMonitorPlacement.IsRecallReplacement(main,11), "only an independently foregrounded original process may replace destroyed or hidden HWND");
            Check(LaunchMonitorPlacement.GetRecallAction(foreground, "Left", "Right") == LaunchMonitorPlacement.RecallAction.Activate &&
                LaunchMonitorPlacement.GetRecallAction(foreground, "LEFT", "left") == LaunchMonitorPlacement.RecallAction.Minimize,
                "foreground on another screen follows; same-screen foreground toggles");
            Check(LaunchMonitorPlacement.GetRecallAction(minimized, "Left", "Left") == LaunchMonitorPlacement.RecallAction.Restore &&
                LaunchMonitorPlacement.GetRecallAction(main, "Left", "Right") == LaunchMonitorPlacement.RecallAction.Activate,
                "restore minimized and activate background window");
            WindowSnapshot? Observe(params WindowSnapshot[] frames)
            {
                int frame = 0;
                return LaunchMonitorPlacement.WaitForStableWindowAsync(() => frame < frames.Length,
                    () => LaunchMonitorPlacement.SelectUserWindow([frames[frame++]], owners, false),
                    () => Task.CompletedTask).GetAwaiter().GetResult();
            }
            Check(Observe(hidden, Window(pid:33), minimized, main, main) == main,
                "tray handoff selects restored original process after transient launcher closes");
            Check(Observe(hidden, Window(pid:33), hidden) == null && Observe(main, hidden) == null,
                "fully hidden and transient windows time out without movement");
            using (var older = new LaunchMonitorPlacement.PlacementRequest(path))
            using (var another = new LaunchMonitorPlacement.PlacementRequest(@"C:\Other\Cad.exe"))
            using (var newer = new LaunchMonitorPlacement.PlacementRequest(path.ToLowerInvariant()))
            {
                Check(!older.IsCurrent && newer.IsCurrent && another.IsCurrent, "last trigger wins per image, other images unaffected");
                older.Dispose();
                Check(newer.IsCurrent, "older observer cleanup preserves later request");
            }
            Check(LaunchMonitorPlacement.ShouldActivate(true, true, 3200) && !LaunchMonitorPlacement.ShouldActivate(false, true, 3200),
                "slow tray wake can still activate only while requested");
            Check(!LaunchMonitorPlacement.ShouldActivate(true, false, 3200) && LaunchMonitorPlacement.ShouldActivate(true, false, 200),
                "pure new launch retains its short post-launch focus period");
            var left = new Rectangle(-1920,0,1920,1080);
            var sourceWork = new Rectangle(-1880,0,1880,1080);
            var target = new Rectangle(0,-200,1366,768);
            var targetWork = new Rectangle(0,-176,1366,744);
            Check(LaunchMonitorPlacement.Translate(new Rectangle(-1780,50,800,560), sourceWork, targetWork)==new Point(100,-126),
                "normal window position translated in physical work areas");
            Check(LaunchMonitorPlacement.Translate(new Rectangle(-200,800,800,560),sourceWork,targetWork)==new Point(566,8),
                "normal window fits at right and bottom without shrinking");
            Check(LaunchMonitorPlacement.TranslateRestored(new Rectangle(-1820,50,800,560), left,sourceWork,target,targetWork)==new Rectangle(100,-150,800,560),
                "maximized restore rectangle keeps size and handles side/top taskbars");
            Check(LaunchMonitorPlacement.Translate(new Rectangle(-1500,600,2000,1200),sourceWork,targetWork)==targetWork.Location,
                "oversize windows preserve size and keep accessible origin");
            Console.WriteLine($"RESULT {_checks} passed");
        }
        finally
        {
            AppLogger.Shutdown();
            Directory.Delete(fixture.FullName, true);
        }
    }
}
