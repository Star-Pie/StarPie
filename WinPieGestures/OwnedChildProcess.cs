using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace WinPieGestures;

/// <summary>一次受控启动的资源所有者；调用方必须持有到子进程退出或确认停止。</summary>
internal sealed class OwnedChildProcess : IDisposable
{
    private int _disposed;
    internal Process Process { get; }
    internal StreamReader? StandardOutput { get; }
    internal StreamReader? StandardError { get; }
    internal DateTime StartedAtUtc { get; }

    internal OwnedChildProcess(Process process, StreamReader? standardOutput, StreamReader? standardError,
        DateTime? startedAtUtc = null)
    {
        Process = process;
        StandardOutput = standardOutput;
        StandardError = standardError;
        // 生产入口不提供时间覆盖值；使用内核报告的真实创建时间，而非启动前的墙钟。
        StartedAtUtc = startedAtUtc ?? process.StartTime.ToUniversalTime();
    }

    internal static OwnedChildProcess StartManaged(ProcessStartInfo info)
    {
        var process = new Process { StartInfo = info };
        try
        {
            if (!process.Start()) throw new InvalidOperationException(I18n.T("PluginsProcessLaunchDenied"));
            return new OwnedChildProcess(process,
                info.RedirectStandardOutput ? process.StandardOutput : null,
                info.RedirectStandardError ? process.StandardError : null);
        }
        catch
        {
            try { if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(2000); } } catch { }
            process.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { StandardOutput?.Dispose(); } catch (IOException) { }
        try { StandardError?.Dispose(); } catch (IOException) { }
        Process.Dispose();
    }
}
