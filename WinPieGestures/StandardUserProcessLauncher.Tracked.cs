using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace WinPieGestures;

internal enum StandardUserTokenSource { Current, Desktop, LinkedDesktop }

internal static partial class StandardUserProcessLauncher
{
    internal interface ITrackedBackend
    {
        bool TryGetDesktopIdentity(out StandardUserTokenIdentity identity);
        bool TryGetCurrentIdentity(out StandardUserTokenIdentity identity);
        bool TryGetLinkedDesktopIdentity(out StandardUserTokenIdentity identity);
        OwnedChildProcess StartTracked(ProcessStartInfo info, StandardUserTokenSource source);
    }

    /// <summary>保留既有 bool/Shell 入口；只有需要进程、管道所有权的调用使用这个入口。</summary>
    internal static OwnedChildProcess StartTracked(ProcessStartInfo info)
    {
        using var backend = new StandardUserLaunchNative();
        return StartTracked(info, backend, ResolveTrackedExecutable);
    }

    internal static OwnedChildProcess StartTracked(ProcessStartInfo info, ITrackedBackend backend,
        Func<string, string, string?> resolveExecutable)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (info.UseShellExecute || info.RedirectStandardInput || !info.RedirectStandardOutput || !info.RedirectStandardError)
            throw Denied("Tracked launch requires a direct executable without redirected input.");
        string directory = string.IsNullOrEmpty(info.WorkingDirectory) ? Environment.CurrentDirectory : info.WorkingDirectory;
        string arguments = BuildArguments(info);
        if (arguments.Contains('\0') || directory.Contains('\0')) throw Denied("Invalid launch arguments or directory.");
        string? executable = resolveExecutable(info.FileName, directory);
        if (executable == null) throw Denied("Tracked launch requires a resolvable direct EXE.");
        if (!backend.TryGetDesktopIdentity(out var desktop) || !backend.TryGetCurrentIdentity(out var current) ||
            !SameUserAndSession(current, desktop)) throw Denied("Desktop/host identity could not be verified.");

        StandardUserTokenSource source;
        if (current.IsStandardUser) source = StandardUserTokenSource.Current;
        else if (desktop.IsStandardUser) source = StandardUserTokenSource.Desktop;
        else
        {
            if (!backend.TryGetLinkedDesktopIdentity(out var linked) || !linked.IsStandardUser ||
                !SameUserAndSession(linked, desktop)) throw Denied("No verified standard linked token.");
            source = StandardUserTokenSource.LinkedDesktop;
        }

        if (source != StandardUserTokenSource.Current &&
            (QuoteArgument(executable) + (arguments.Length == 0 ? "" : " " + arguments)).Length >= 1024)
            throw Denied("Linked-token command line exceeds the supported length.");

        var normalized = new ProcessStartInfo
        {
            FileName = executable, Arguments = arguments, WorkingDirectory = directory, UseShellExecute = false,
            CreateNoWindow = info.CreateNoWindow, WindowStyle = info.WindowStyle,
            RedirectStandardOutput = info.RedirectStandardOutput, RedirectStandardError = info.RedirectStandardError,
            StandardOutputEncoding = info.StandardOutputEncoding, StandardErrorEncoding = info.StandardErrorEncoding,
        };
        // 不经过“仅发起启动”的 COM 包装，不丢失 ArgumentList，也不在失败时降级权限策略。
        return backend.StartTracked(normalized, source) ?? throw Denied("Launcher returned no process owner.");
    }

    internal static string? ResolveTrackedExecutable(string file, string directory)
    {
        // 插件扫描已解析成绝对路径；这里不得再次展开 %VAR% 或改走 PATH 搜索。
        if (string.IsNullOrWhiteSpace(file) || file.Contains('\0') || file.Contains('"') ||
            !Path.IsPathFullyQualified(file) || !string.Equals(Path.GetExtension(file), ".exe", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFileName(file), "explorer.exe", StringComparison.OrdinalIgnoreCase)) return null;
        return File.Exists(file) ? Path.GetFullPath(file) : null;
    }

    internal static string BuildArguments(ProcessStartInfo info)
    {
        if (info.ArgumentList.Count == 0) return info.Arguments;
        if (!string.IsNullOrEmpty(info.Arguments)) throw new ArgumentException("Arguments and ArgumentList are mutually exclusive.", nameof(info));
        var text = new StringBuilder();
        foreach (string argument in info.ArgumentList)
        {
            if (text.Length > 0) text.Append(' ');
            text.Append(QuoteArgument(argument));
        }
        return text.ToString();
    }

    internal static string QuoteArgument(string value)
    {
        if (value.Contains('\0')) throw Denied("NUL is not allowed in a launch argument.");
        var text = new StringBuilder().Append('"');
        int slashes = 0;
        foreach (char ch in value)
        {
            if (ch == '\\') { slashes++; continue; }
            text.Append('\\', ch == '"' ? slashes * 2 + 1 : slashes);
            text.Append(ch);
            slashes = 0;
        }
        return text.Append('\\', slashes * 2).Append('"').ToString();
    }

    private static InvalidOperationException Denied(string reason)
    {
        AppLogger.LogWarn("Tracked standard-user launch refused: " + reason);
        return new InvalidOperationException(I18n.T("PluginsProcessLaunchDenied"));
    }
}
