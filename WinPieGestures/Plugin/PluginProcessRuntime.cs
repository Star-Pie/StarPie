using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace WinPieGestures.Plugins;

/// <summary>
/// 一个由宿主拉起、仍在监听的进程插件实例。
/// <para>
/// 它必须比 <c>ProcessActionContribution.ExecuteAsync</c> 活得久。原因很硬：
/// <b>一旦 Process 对象被释放，.NET 会关闭重定向所用的管道</b>，而子进程完全可能在那之后
/// 继续往 stderr 写 —— Rust 的 <c>eprintln!</c> 遇到 EPIPE 会直接 panic 退出，
/// 表现成「插件莫名其妙消失」。所以这里持有 Process 直到进程真的结束或插件被停用。
/// </para>
/// </summary>
internal sealed class TrackedPluginProcess
{
    private const int MaxCapturedChars = 2000;

    private readonly object _gate = new();
    private readonly StringBuilder _buffer = new();

    internal TrackedPluginProcess(string pluginId, Process process, DateTime startedAtUtc)
    {
        PluginId = pluginId;
        Process = process;
        StartedAtUtc = startedAtUtc;
    }

    public string PluginId { get; }

    public Process Process { get; }

    public DateTime StartedAtUtc { get; }

    private readonly System.Threading.ManualResetEventSlim _exitedSignal = new(false);

    /// <summary>进程是否已退出。由 <see cref="Process.Exited"/> 置位。</summary>
    public bool HasExited { get; private set; }

    /// <summary>
    /// 等待「退出信息已就绪」，用于消除 Exited 事件与动作线程之间的竞态：
    /// <c>WaitForExitAsync</c> 返回时，事件处理器可能还没把退出码缓存好。
    /// </summary>
    public bool WaitForExitInfo(int millisecondsTimeout) => _exitedSignal.Wait(millisecondsTimeout);

    /// <summary>退出码。仅在 <see cref="HasExited"/> 为真后有意义。</summary>
    public int ExitCode { get; private set; } = -1;

    public void Append(string prefix, string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        lock (_gate)
        {
            if (_buffer.Length > MaxCapturedChars)
            {
                _buffer.Remove(0, _buffer.Length - MaxCapturedChars);
            }
            _buffer.Append(prefix).AppendLine(line);
        }
    }

    public string Snapshot()
    {
        lock (_gate)
        {
            return _buffer.ToString().TrimEnd();
        }
    }

    /// <summary>由 Exited 事件调用：先冲刷异步读取器，再缓存退出码。</summary>
    internal void MarkExited()
    {
        try
        {
            // 没有超时的 WaitForExit 会等待异步输出读取器把剩余内容读完，
            // 少了这一下，退出前最后几行 stderr（往往正是失败原因）会丢。
            Process.WaitForExit();
            ExitCode = Process.ExitCode;
        }
        catch (Exception)
        {
            ExitCode = -1;
        }
        HasExited = true;
        _exitedSignal.Set();
    }

    internal void DisposeQuietly()
    {
        try { Process.Dispose(); } catch (Exception) { }
    }
}

/// <summary>
/// 宿主拉起的进程插件登记簿：负责启动、收集输出、按 PID+启动时间安全回收。
/// </summary>
internal static class PluginProcessRuntime
{
    /// <summary>每个插件最多保留多少个已退出实例的记录。超过就先释放最老的。</summary>
    private const int MaxRetainedPerPlugin = 16;

    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<int, TrackedPluginProcess>> Registry =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 启动并登记一个进程。返回的句柄<b>不由调用方释放</b>：它的生命周期由本类管理。
    /// </summary>
    /// <param name="logLine">逐行回调，用于把子进程输出写进插件日志。</param>
    public static TrackedPluginProcess Spawn(
        string pluginId,
        ProcessStartInfo startInfo,
        Action<string, string> logLine)
    {
        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var tracked = new TrackedPluginProcess(pluginId, process, DateTime.UtcNow);

        process.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            tracked.Append("[stdout] ", e.Data);
            logLine("[stdout] ", e.Data!);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            tracked.Append("", e.Data);
            logLine("", e.Data!);
        };
        process.Exited += (_, _) => tracked.MarkExited();

        if (!process.Start())
        {
            tracked.DisposeQuietly();
            throw new InvalidOperationException($"无法启动进程：{startInfo.FileName}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        int pid = SafePid(process);
        ConcurrentDictionary<int, TrackedPluginProcess> bucket =
            Registry.GetOrAdd(pluginId, _ => new ConcurrentDictionary<int, TrackedPluginProcess>());
        bucket[pid] = tracked;
        PruneExited(pluginId, bucket);

        return tracked;
    }

    /// <summary>结束某插件名下、仍活着且确认是「当初那个」进程。返回实际结束的数量。</summary>
    public static int TerminateAll(string pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId)) return 0;
        if (!Registry.TryRemove(pluginId, out ConcurrentDictionary<int, TrackedPluginProcess>? bucket)) return 0;

        int killed = 0;
        foreach (TrackedPluginProcess tracked in bucket.Values)
        {
            if (TryKill(tracked)) killed++;
            tracked.DisposeQuietly();
        }
        return killed;
    }

    /// <summary>宿主退出时清场，避免进程插件比宿主活得更久。</summary>
    public static int TerminateAllPlugins()
    {
        int killed = 0;
        foreach (string pluginId in Registry.Keys)
        {
            killed += TerminateAll(pluginId);
        }
        return killed;
    }

    /// <summary>某个插件当前在册的进程数（诊断用）。</summary>
    public static int Count(string pluginId) =>
        Registry.TryGetValue(pluginId, out ConcurrentDictionary<int, TrackedPluginProcess>? bucket)
            ? bucket.Count
            : 0;

    /// <summary>
    /// 释放已退出实例占用的记录。为什么不在 Exited 里直接释放 Process：
    /// <c>MarkExited</c> 刚缓存完退出码，而动作线程可能正要读它 —— 那个竞态会变成
    /// 一次随机的 ObjectDisposedException。留到「攒够上限」或「插件停用」时再清，
    /// 代价只是每进程一个已关闭的句柄。
    /// </summary>
    private static void PruneExited(string pluginId, ConcurrentDictionary<int, TrackedPluginProcess> bucket)
    {
        if (bucket.Count <= MaxRetainedPerPlugin) return;

        int removed = 0;
        foreach (var pair in bucket)
        {
            if (removed >= bucket.Count - MaxRetainedPerPlugin) break;
            if (!pair.Value.HasExited) continue;
            if (bucket.TryRemove(pair.Key, out TrackedPluginProcess? gone))
            {
                gone.DisposeQuietly();
                removed++;
            }
        }
    }

    private static bool TryKill(TrackedPluginProcess tracked)
    {
        if (tracked.HasExited) return false;

        try
        {
            using Process process = Process.GetProcessById(SafePid(tracked.Process));
            DateTime startedAt = process.StartTime.ToUniversalTime();

            // 启动时间对不上说明这个 PID 已经被系统复用，杀它就是杀别人的进程。
            if (Math.Abs((startedAt - tracked.StartedAtUtc).TotalSeconds) > 2)
            {
                return false;
            }

            process.Kill(entireProcessTree: true);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (Exception)
        {
            // 回收是尽力而为：权限不足等情况不能阻断停用流程
            return false;
        }
    }

    private static int SafePid(Process process)
    {
        try { return process.Id; } catch (Exception) { return -1; }
    }
}
