using System.IO;
using WinPieGestures;

internal static class Program
{
    private static int _failed;
    private static long _tick;
    private static void Check(bool condition, string message)
    {
        Console.WriteLine($"[{(condition ? "PASS" : "FAIL")}] {message}");
        if (!condition) _failed++;
    }
    private static void Stop()
    {
        SoundEffectManager.Shutdown();
        foreach (Thread thread in SoundEffectManager.GetHarnessCreatedWorkers())
            if (thread.IsAlive && !thread.Join(3000)) throw new TimeoutException("Probe worker did not stop.");
        SoundEffectManager.ResetTestSeams();
        SoundEffectManager.TestMode = true;
    }
    private static void Configure(bool muted)
    {
        var profile = new CustomSoundProfile
        {
            Id = "beta7-probe", Events = new()
            {
                new() { EventType = SoundType.SectorHover, SourceType = muted ? SoundSourceType.Mute : SoundSourceType.BuiltInPreset,
                    BuiltInTheme = "Mechanical" }
            }
        };
        var config = ConfigManager.CurrentConfig!;
        config.EnableSoundEffects = true; config.SoundOnHover = true; config.SoundOnPopup = true;
        config.SoundTheme = "Custom"; config.SoundVolume = 0.6;
        config.CustomSoundProfiles = new() { profile }; config.ActiveCustomSoundProfileId = profile.Id;
        SoundEffectManager.TestMode = true;
        SoundEffectManager.PlaybackSink = (_, _) => { };
        SoundEffectManager.TimeProvider = () => Interlocked.Read(ref _tick);
        SoundEffectManager.Initialize("Custom", 0.6, force: true);
    }
    private static void TestEmptyHoverReselection(bool muted)
    {
        using var first = new ManualResetEventSlim(); using var second = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim(); using var releaseSecond = new ManualResetEventSlim();
        int selections = 0;
        Interlocked.Exchange(ref _tick, 1000);
        Configure(muted);
        SoundEffectManager.OnHoverCandidateSelectedBeforeAcquireResource = () =>
        {
            int selection = Interlocked.Increment(ref selections);
            if (selection == 1) { first.Set(); if (!releaseFirst.Wait(3000)) throw new TimeoutException("First probe gate timed out."); }
            else { second.Set(); if (!releaseSecond.Wait(3000)) throw new TimeoutException("Second probe gate timed out."); }
        };
        long session = SoundEffectManager.BeginSession(SoundSessionSource.NormalGesture);
        try
        {
            Check((SoundEffectManager.GetCachedSoundBuffer(SoundType.SectorHover)!.Length == 0) == muted,
                $"{(muted ? "Mute" : "Tone")} configuration reaches actual cached resource");
            SoundEffectManager.ReportHover(SoundSessionSource.NormalGesture, session, 0, -1, 0);
            Interlocked.Exchange(ref _tick, 1045); SoundEffectManager.TestSignalWorker();
            Check(first.Wait(3000), "actual ready hover reaches worker resource-acquisition seam");
            releaseFirst.Set();
            bool repeated = second.Wait(350);
            Console.WriteLine($"[OBSERVED] muted={muted}; same-candidate selections={Volatile.Read(ref selections)}; simulated time remains 1045");
            Check(!repeated, muted ? "muted ready hover must not immediately reselect the same empty resource" : "audible control marks the ready candidate consumed");
        }
        finally
        {
            SoundEffectManager.EndSession(SoundSessionSource.NormalGesture, session, allowTerminalFeedback: false);
            releaseFirst.Set(); releaseSecond.Set(); Stop();
        }
    }
    private static void TestBlockedPlaybackHoverExpiration()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        using var dropped = new ManualResetEventSlim();
        Interlocked.Exchange(ref _tick, 2000); Configure(muted: false);
        int starts = 0;
        SoundEffectManager.PlaybackSink = (_, _) =>
        {
            if (Interlocked.Increment(ref starts) == 1)
            { entered.Set(); if (!release.Wait(3000)) throw new TimeoutException("Backend probe gate timed out."); }
        };
        SoundEffectManager.SoundDropped = (type, _, _) => { if (type == SoundType.SectorHover) dropped.Set(); };
        long session = SoundEffectManager.BeginSession(SoundSessionSource.NormalGesture);
        try
        {
            SoundEffectManager.Play(SoundType.WheelPopup, SoundSessionSource.NormalGesture, session);
            Check(entered.Wait(3000), "mock backend holds actual single playback worker without physical audio");
            SoundEffectManager.ReportHover(SoundSessionSource.NormalGesture, session, 0, -1, 1);
            Interlocked.Exchange(ref _tick, 2196); release.Set();
            Check(dropped.Wait(3000), "backend delay beyond 45+150ms expires pending hover by existing policy");
            Check(Volatile.Read(ref starts) == 1, "expired hover never reaches playback backend");
        }
        finally
        {
            release.Set(); SoundEffectManager.EndSession(SoundSessionSource.NormalGesture, session, allowTerminalFeedback: false); Stop();
        }
    }
    private static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "StarPie-Beta7-SoundProbe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); Environment.SetEnvironmentVariable("LOCALAPPDATA", root);
        ConfigManager.DisableAutoStartSync = true; _ = ConfigManager.CurrentConfig;
        SoundEffectManager.TestMode = true;
        Check(AppVersionInfo.DisplayVersion == "1.8.0-beta.7", "actual runtime display version is beta.7");
        Check(typeof(SoundEffectManager).Assembly.GetName().Version == new Version(1, 8, 0, 0), "assembly identity keeps numeric version 1.8.0.0");
        Check(System.Diagnostics.FileVersionInfo.GetVersionInfo(typeof(SoundEffectManager).Assembly.Location).FileVersion == "1.8.0.0",
            "compiled file numeric version remains 1.8.0.0");
        Console.WriteLine("Isolated probe root: " + root);
        Console.WriteLine("Expected healthy behavior: no reselection of a ready muted hover. TestMode remains enabled; no native audio or GUI.");
        try { TestEmptyHoverReselection(muted: false); TestEmptyHoverReselection(muted: true); TestBlockedPlaybackHoverExpiration(); }
        catch (Exception ex) { Check(false, ex.ToString()); }
        finally { Stop(); }
        Console.WriteLine($"Probe result: {_failed} failed assertions (nonzero is the reproduced baseline defect, not a fixed candidate).");
        return _failed == 0 ? 0 : 1;
    }
}
