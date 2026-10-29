using System;
using System.IO;

namespace WinPieGestures;

internal readonly record struct StandardUserTokenIdentity(string UserSid, int SessionId, bool IsStandardUser);
internal readonly record struct StandardUserLaunchRequest(string FileName, string Arguments, string WorkingDirectory, int ShowCommand)
{
    internal string CommandLine => "\"" + FileName + "\"" + (string.IsNullOrEmpty(Arguments) ? "" : " " + Arguments);
    internal bool Hidden => ShowCommand == 0;
}

// 只在桌面 Shell 已确认提权时使用；不把检测失败或 COM 执行失败当作降权后备条件。
internal static partial class StandardUserProcessLauncher
{
    internal interface IBackend
    {
        bool TryGetDesktopIdentity(out StandardUserTokenIdentity identity);
        bool TryGetCurrentIdentity(out StandardUserTokenIdentity identity);
        bool TryGetLinkedDesktopIdentity(out StandardUserTokenIdentity identity);
        bool StartCurrent(StandardUserLaunchRequest request);
        bool StartLinked(StandardUserLaunchRequest request);
    }

    internal static bool TryStart(string fileName, string arguments, string workingDirectory, int showCommand)
    {
        try
        {
            using var backend = new StandardUserLaunchNative();
            return TryStart(fileName, arguments, workingDirectory, showCommand, backend, ResolveExecutable,
                reason => AppLogger.LogWarn("Standard-user fallback refused: " + reason));
        }
        catch (Exception ex)
        {
            AppLogger.LogWarn("Standard-user fallback failed: " + ex.Message);
            return false;
        }
    }

    internal static bool TryStart(string fileName, string arguments, string workingDirectory, int showCommand,
        IBackend backend, Func<string, string, string?> resolveExecutable, Action<string> logFailure)
    {
        bool Refuse(string reason) { logFailure(reason); return false; }
        if (showCommand is not (0 or 1)) return Refuse("Unsupported window display mode.");
        if (!backend.TryGetDesktopIdentity(out var desktop)) return Refuse("Desktop token could not be verified.");
        if (desktop.IsStandardUser) return Refuse("Desktop Shell is not elevated; no token fallback is required.");
        if (!backend.TryGetCurrentIdentity(out var current)) return Refuse("Host token could not be verified.");
        if (!SameUserAndSession(current, desktop)) return Refuse("Host and desktop user/session do not match.");

        if (arguments.Contains('\0') || workingDirectory.Contains('\0')) return Refuse("Invalid launch arguments or directory.");
        string directory = string.IsNullOrEmpty(workingDirectory) ? Environment.CurrentDirectory : workingDirectory;
        string? executable = resolveExecutable(fileName, directory);
        if (executable == null) return Refuse("Fallback supports resolvable EXE files only; Explorer mediation is excluded.");
        var request = new StandardUserLaunchRequest(executable, arguments, directory, showCommand);
        if (current.IsStandardUser)
            return backend.StartCurrent(request) || Refuse("Non-elevated host process creation failed.");

        if (!backend.TryGetLinkedDesktopIdentity(out var linked)) return Refuse("No usable linked desktop token.");
        if (!linked.IsStandardUser || !SameUserAndSession(linked, desktop))
            return Refuse("Linked token is elevated or belongs to a different user/session.");
        // CreateProcessWithTokenW 有独立命令行长度上限；超限不得截断或改用管理员启动。
        if (request.CommandLine.Length >= 1024) return Refuse("Linked-token command line exceeds the supported length.");
        return backend.StartLinked(request) || Refuse("Linked-token process creation failed.");
    }

    private static bool SameUserAndSession(StandardUserTokenIdentity left, StandardUserTokenIdentity right) =>
        !string.IsNullOrEmpty(left.UserSid) && left.SessionId >= 0 &&
        string.Equals(left.UserSid, right.UserSid, StringComparison.Ordinal) && left.SessionId == right.SessionId;

    internal static string? ResolveExecutable(string fileName, string workingDirectory)
    {
        string file = Environment.ExpandEnvironmentVariables(fileName.Trim().Trim('"'));
        if (string.IsNullOrWhiteSpace(file) || file.Contains('\0') || file.Contains('"') ||
            !string.Equals(Path.GetExtension(file), ".exe", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFileName(file), "explorer.exe", StringComparison.OrdinalIgnoreCase))
            return null;

        if (Path.IsPathFullyQualified(file)) return File.Exists(file) ? Path.GetFullPath(file) : null;
        if (Path.IsPathRooted(file)) return null; // 不推测驱动器相对路径。
        string directory = string.IsNullOrEmpty(workingDirectory) ? Environment.CurrentDirectory : workingDirectory;
        string candidate = Path.GetFullPath(file, directory);
        if (File.Exists(candidate)) return candidate;
        if (file.Contains(Path.DirectorySeparatorChar) || file.Contains(Path.AltDirectorySeparatorChar)) return null;
        foreach (string folder in new[] { AppContext.BaseDirectory, Environment.SystemDirectory, Environment.GetFolderPath(Environment.SpecialFolder.Windows) })
        {
            candidate = Path.Combine(folder, file);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            string path = folder.Trim().Trim('"');
            if (!Path.IsPathFullyQualified(path)) continue;
            candidate = Path.Combine(path, file);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        return null;
    }
}
