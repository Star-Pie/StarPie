using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WinPieGestures;

internal interface IUpdateHelperProcess : IDisposable
{
    bool HasExited { get; }
}

internal sealed class UpdateRestartSession : IDisposable
{
    internal const string RequestArgument = "--update-request";
    private readonly IUpdateHelperProcess _helper;
    private readonly string _root;
    private bool _committed;

    private UpdateRestartSession(IUpdateHelperProcess helper, string root)
    {
        _helper = helper;
        _root = root;
    }

    internal static async Task<UpdateRestartSession> PrepareAsync(string archive, string executable,
        int previousPid, long previousStartTicks, CancellationToken cancellationToken = default,
        Func<ProcessStartInfo, IUpdateHelperProcess>? startHelper = null)
    {
        string target = UpdatePayloadStager.ValidateApplicationDirectory(Path.GetDirectoryName(Path.GetFullPath(executable))!);
        StagedUpdatePayload staged = await Task.Run(() =>
        {
            // Detect a protected installation before asking the running app to exit.
            string probe = Path.Combine(target, ".starpie-write-" + Guid.NewGuid().ToString("N"));
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            return UpdatePayloadStager.Stage(archive, target);
        }, cancellationToken);
        string root = staged.RootDirectory;
        string token = Path.GetFileName(root);
        string stagedExe = Path.Combine(staged.PayloadDirectory, staged.ExeRelativePath);
        string? version = FileVersionInfo.GetVersionInfo(stagedExe).ProductVersion;
        bool requireReady = ReleaseVersion.TryParse(version, out ReleaseVersion? targetVersion) &&
            ReleaseVersion.TryParse("2.0.1-beta.4", out ReleaseVersion? minimum) && targetVersion!.CompareTo(minimum) >= 0;
        string logDirectory = AppLogger.GetLogFolderPath();
        var request = new
        {
            Token = token, PreviousPid = previousPid, PreviousStartTicks = previousStartTicks,
            TargetDirectory = target, Executable = Path.GetFullPath(executable),
            PayloadDirectory = staged.PayloadDirectory, BackupDirectory = staged.BackupDirectory,
            Files = staged.Files, RequireReadySignal = requireReady,
            LogFile = Path.Combine(logDirectory, "update-" + token + ".log"),
            FailureMessage = I18n.T("UpdateApplyFailureDetail"),
        };
        string requestPath = Path.Combine(root, "request.json");
        await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(request), new UTF8Encoding(false), cancellationToken);
        string script = Path.Combine(root, "apply.ps1");
        await File.WriteAllTextAsync(script, UpdateApplyScript.Content, new UTF8Encoding(true), cancellationToken);
        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
            WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", script, "-RequestPath", requestPath })
            start.ArgumentList.Add(argument);
        IUpdateHelperProcess helper = (startHelper ?? StartHelper)(start);
        var session = new UpdateRestartSession(helper, root);
        try
        {
            var elapsed = Stopwatch.StartNew();
            while (!File.Exists(Path.Combine(root, "prepared")))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (helper.HasExited) throw new IOException("The update helper exited before preparation completed.");
                if (elapsed.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("The update helper did not acknowledge preparation.");
                await Task.Delay(100, cancellationToken);
            }
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    private static IUpdateHelperProcess StartHelper(ProcessStartInfo start) =>
        new NativeUpdateHelperProcess(Process.Start(start) ?? throw new IOException("The update helper did not start."));

    private sealed class NativeUpdateHelperProcess(Process process) : IUpdateHelperProcess
    {
        public bool HasExited => process.HasExited;
        public void Dispose() => process.Dispose();
    }

    internal async Task CommitAsync(CancellationToken cancellationToken = default, TimeSpan? timeout = null)
    {
        if (_helper.HasExited) throw new IOException("The prepared update helper has exited.");
        File.WriteAllText(Path.Combine(_root, "proceed"), "1");
        string acknowledgement = Path.Combine(_root, "accepted");
        var elapsed = Stopwatch.StartNew();
        while (!File.Exists(acknowledgement))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_helper.HasExited) throw new IOException("The update helper exited before accepting authorization.");
            if (elapsed.Elapsed > (timeout ?? TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The update helper did not accept authorization.");
            await Task.Delay(50, cancellationToken);
        }
        if (_helper.HasExited || !string.Equals(File.ReadAllText(acknowledgement), Path.GetFileName(_root), StringComparison.Ordinal))
            throw new IOException("The update helper authorization acknowledgement is invalid.");
        string confirmation = Path.Combine(_root, "confirmed.tmp");
        File.WriteAllText(confirmation, Path.GetFileName(_root));
        File.Move(confirmation, Path.Combine(_root, "confirmed"));
        _committed = true;
    }

    public void Dispose()
    {
        if (!_committed)
        {
            try { File.WriteAllText(Path.Combine(_root, "abort"), "1"); } catch { }
        }
        _helper.Dispose();
    }

    internal static void ReportReady(string[] arguments)
    {
        int index = Array.FindIndex(arguments, value => string.Equals(value, RequestArgument, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return;
        try
        {
            if (index + 1 >= arguments.Length) throw new InvalidDataException("Missing update request.");
            string path = Path.GetFullPath(arguments[index + 1]);
            string root = Path.GetDirectoryName(path)!;
            string parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "StarPieUpdate"));
            if (!string.Equals(Path.GetDirectoryName(root), parent, StringComparison.OrdinalIgnoreCase) ||
                !Guid.TryParseExact(Path.GetFileName(root), "N", out _) ||
                !string.Equals(Path.GetFileName(path), "request.json", StringComparison.Ordinal) ||
                new FileInfo(path).Length > 2 * 1024 * 1024 ||
                (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Update request is outside the owned staging directory.");
            for (string? directory = root; directory != null; directory = Path.GetDirectoryName(directory))
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Update request contains a redirected directory.");
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement request = document.RootElement;
            string? executable = Environment.ProcessPath;
            if (!string.Equals(request.GetProperty("Token").GetString(), Path.GetFileName(root), StringComparison.Ordinal) ||
                !string.Equals(request.GetProperty("Executable").GetString(), executable, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Update request does not match this process.");
            JsonElement exeFile = request.GetProperty("Files").EnumerateArray().Single(file =>
                string.Equals(file.GetProperty("RelativePath").GetString(), "StarPie.exe", StringComparison.OrdinalIgnoreCase));
            using FileStream stream = File.OpenRead(executable!);
            string hash = Convert.ToHexString(SHA256.HashData(stream));
            if (!string.Equals(hash, exeFile.GetProperty("Sha256").GetString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Updated executable hash does not match the staged payload.");
            string pending = Path.Combine(root, "ready.tmp");
            File.WriteAllText(pending, Path.GetFileName(root));
            File.Move(pending, Path.Combine(root, "ready"), overwrite: true);
            AppLogger.LogInfo("Update handoff reached HostReady.");
        }
        catch (Exception ex) { AppLogger.LogError("Update readiness acknowledgement failed", ex); }
    }
}
