using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WinPieGestures.Plugins;

/// <summary>持有一个宿主创建的子进程及其输出资源；动作返回不结束资源寿命。</summary>
internal sealed class TrackedPluginProcess
{
    internal const int MaxCapturedChars = 2000;
    private readonly object _gate = new();
    private readonly StringBuilder _buffer = new(MaxCapturedChars);
    private readonly TaskCompletionSource<int> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private OwnedChildProcess? _owner;
    private int _hasExited;

    internal TrackedPluginProcess(string pluginId, Process process, DateTime startedAtUtc)
    { PluginId = pluginId; Process = process; StartedAtUtc = startedAtUtc; }

    public string PluginId { get; }
    public Process Process { get; }
    public DateTime StartedAtUtc { get; }
    public bool HasExited => Volatile.Read(ref _hasExited) != 0;
    public int ExitCode { get; private set; } = -1;
    public bool WaitForExitInfo(int millisecondsTimeout) => _exit.Task.Wait(millisecondsTimeout);
    internal Task WaitForExitAsync(CancellationToken token) => _exit.Task.WaitAsync(token);

    public void Append(string prefix, string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        lock (_gate)
        {
            AppendTail(prefix);
            AppendTail(line);
            AppendTail(Environment.NewLine);
        }
    }

    private void AppendTail(string value)
    {
        int count = Math.Min(value.Length, MaxCapturedChars);
        int remove = _buffer.Length + count - MaxCapturedChars;
        if (remove > 0) _buffer.Remove(0, remove);
        // 不把整条超长输出临时放进 StringBuilder，避免容量永久膨胀。
        _buffer.Append(value.AsSpan(value.Length - count, count));
    }

    public string Snapshot() { lock (_gate) return _buffer.ToString().TrimEnd(); }

    internal void Observe(OwnedChildProcess owner, Action<string, string> logLine)
    {
        _owner = owner;
        Task output = Pump(owner.StandardOutput, "[stdout] ", logLine);
        Task error = Pump(owner.StandardError, "", logLine);
        _ = ObserveExitAsync(output, error);
    }

    private async Task Pump(TextReader? reader, string prefix, Action<string, string> logLine)
    {
        if (reader == null) return;
        try
        {
            await PluginProcessRuntime.ReadBoundedLines(reader, line =>
            {
                if (string.IsNullOrWhiteSpace(line)) return;
                Append(prefix, line);
                try { logLine(prefix, line); }
                catch (Exception) { /* 日志失败不能停止读取，否则子进程可能堵在输出管道。 */ }
            }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException) { }
    }

    private async Task ObserveExitAsync(Task output, Task error)
    {
        int exitCode = -1;
        try
        {
            await Process.WaitForExitAsync().ConfigureAwait(false);
            exitCode = Process.ExitCode;
            // 后代进程可能继承管道；不能让已退出根进程永远等不到退出信息。
            Task readers = Task.WhenAll(output, error);
            await Task.WhenAny(readers, Task.Delay(1000)).ConfigureAwait(false);
            if (readers.IsCompleted) await readers.ConfigureAwait(false);
        }
        catch (Exception) { }
        ExitCode = exitCode;
        Volatile.Write(ref _hasExited, 1);
        _exit.TrySetResult(exitCode);
    }

    internal void DisposeQuietly()
    {
        try { if (_owner != null) _owner.Dispose(); else Process.Dispose(); } catch (Exception) { }
    }
}

/// <summary>进程插件登记、有限输出读取和确认退出后的资源回收。</summary>
internal static class PluginProcessRuntime
{
    private const int MaxRetainedPerPlugin = 16;
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<int, TrackedPluginProcess>> Registry =
        new(StringComparer.OrdinalIgnoreCase);

    public static TrackedPluginProcess Spawn(string pluginId, ProcessStartInfo startInfo, Action<string, string> logLine)
    {
        OwnedChildProcess child = StandardUserProcessLauncher.StartTracked(startInfo);
        try
        {
            var tracked = new TrackedPluginProcess(pluginId, child.Process, child.StartedAtUtc);
            var bucket = Registry.GetOrAdd(pluginId, _ => new ConcurrentDictionary<int, TrackedPluginProcess>());
            bucket[child.Process.Id] = tracked;
            tracked.Observe(child, logLine);
            PruneExited(bucket);
            return tracked;
        }
        catch
        {
            try { if (!child.Process.HasExited) { child.Process.Kill(entireProcessTree: true); child.Process.WaitForExit(2000); } } catch { }
            child.Dispose();
            throw;
        }
    }

    public static int TerminateAll(string pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId) || !Registry.TryGetValue(pluginId, out var bucket)) return 0;
        int killed = 0;
        foreach (var pair in bucket)
        {
            bool terminated = TryKill(pair.Value);
            if (terminated) killed++;
            if ((terminated || HasExited(pair.Value)) && bucket.TryRemove(pair.Key, out var gone)) gone.DisposeQuietly();
        }
        // 空桶很小且可以复用，不移除桶以免与并发登记竞态导致丢失进程记录。
        // 无法确认停止的记录和输出管道保持存活，允许后续停用或退出兜底重试。
        return killed;
    }

    public static int TerminateAllPlugins()
    {
        int killed = 0;
        foreach (string id in Registry.Keys) killed += TerminateAll(id);
        return killed;
    }

    public static int Count(string pluginId) => Registry.TryGetValue(pluginId, out var bucket) ? bucket.Count : 0;

    private static void PruneExited(ConcurrentDictionary<int, TrackedPluginProcess> bucket)
    {
        int remaining = bucket.Count - MaxRetainedPerPlugin;
        foreach (var pair in bucket)
        {
            if (remaining <= 0) break;
            if (pair.Value.HasExited && bucket.TryRemove(pair.Key, out var gone))
            { gone.DisposeQuietly(); remaining--; }
        }
    }

    private static bool HasExited(TrackedPluginProcess tracked)
    {
        try { return tracked.HasExited || tracked.Process.HasExited; }
        catch (InvalidOperationException) { return tracked.HasExited; }
    }

    private static bool TryKill(TrackedPluginProcess tracked)
    {
        try
        {
            Process process = tracked.Process;
            if (HasExited(tracked)) return false;
            // 始终使用本次创建时持有的 Process/句柄；不按 PID 重新打开别人的进程。
            if (process.StartTime.ToUniversalTime() != tracked.StartedAtUtc) return false;
            process.Kill(entireProcessTree: true);
            return process.WaitForExit(2000);
        }
        catch (Exception ex)
        {
            AppLogger.LogWarn($"[plugin] Could not stop tracked process for {tracked.PluginId}: {ex.Message}");
            return false;
        }
    }

    /// <summary>分块读取并限制每行长度，不能让无换行或超长日志无限扩张缓冲。</summary>
    internal static async Task ReadBoundedLines(TextReader reader, Action<string> onLine, int maximum = TrackedPluginProcess.MaxCapturedChars)
    {
        if (maximum <= 0) throw new ArgumentOutOfRangeException(nameof(maximum));
        var buffer = new char[512];
        var line = new StringBuilder(maximum);
        bool truncated = false;
        void Flush()
        {
            if (line.Length == 0 && !truncated) return;
            string value = truncated ? line.ToString(0, maximum - 1) + "…" : line.ToString();
            onLine(value);
            line.Clear(); truncated = false;
        }
        int count;
        while ((count = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) != 0)
        {
            for (int i = 0; i < count; i++)
            {
                char ch = buffer[i];
                if (ch is '\r' or '\n') { Flush(); continue; }
                if (line.Length < maximum) line.Append(ch); else truncated = true;
            }
        }
        Flush();
    }
}