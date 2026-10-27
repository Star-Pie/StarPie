using System;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace WinPieGestures.Plugins;

/// <summary>
/// 宿主拉起的进程插件登记簿。
/// <para>
/// 它存在的唯一理由是<b>停用插件时别留下孤儿进程</b>。宿主从不等待进程退出
/// （UI 型插件本来就常驻），所以它对已经拉起的进程没有任何句柄 —— 只靠 PID 回收又不够稳，
/// 因为 PID 会被系统复用。这里同时记下进程的<b>启动时间</b>，回收前重新确认，
/// 避免误杀一个与插件无关的新进程。
/// </para>
/// <para>
/// 只登记宿主自己 <see cref="Process.Start()"/> 出来的进程。用户在外部启动的同一个程序
/// 不在册，不会被插件停用波及 —— 这是有意的，插件的生命周期不该伸到用户手动的启动里去。
/// </para>
/// </summary>
internal static class PluginProcessRuntime
{
    private sealed record TrackedProcess(int Pid, DateTime StartedAtUtc);

    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<int, TrackedProcess>> Registry =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>登记一个刚拉起的进程。进程已退出时静默跳过（它本来也就不需要回收）。</summary>
    public static void Track(string pluginId, Process process)
    {
        if (string.IsNullOrWhiteSpace(pluginId) || process == null) return;

        int pid;
        DateTime startedAt;
        try
        {
            pid = process.Id;
            startedAt = process.StartTime.ToUniversalTime();
        }
        catch (Exception)
        {
            return;
        }

        ConcurrentDictionary<int, TrackedProcess> bucket =
            Registry.GetOrAdd(pluginId, _ => new ConcurrentDictionary<int, TrackedProcess>());
        bucket[pid] = new TrackedProcess(pid, startedAt);
    }

    /// <summary>结束某插件名下、仍活着且确认是「当初那个」进程。返回实际结束的数量。</summary>
    public static int TerminateAll(string pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId)) return 0;
        if (!Registry.TryRemove(pluginId, out ConcurrentDictionary<int, TrackedProcess>? bucket)) return 0;

        int killed = 0;
        foreach (TrackedProcess tracked in bucket.Values)
        {
            if (TryKill(tracked)) killed++;
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

    private static bool TryKill(TrackedProcess tracked)
    {
        try
        {
            using Process process = Process.GetProcessById(tracked.Pid);
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
            // 进程已经退出了
            return false;
        }
        catch (Exception)
        {
            // 权限不足 / 进程处于内核态等：回收是尽力而为，不能因为它失败就阻断停用流程
            return false;
        }
    }
}
