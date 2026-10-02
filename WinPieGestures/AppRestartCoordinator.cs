using System;
using System.Diagnostics;
using System.Globalization;

namespace WinPieGestures;

/// <summary>普通重启交接：新进程在争用单实例互斥体之前等待旧进程真正退出，不依赖固定延时。</summary>
internal static class AppRestartCoordinator
{
    internal const string WaitArgument = "--restart-after-exit";

    internal static ProcessStartInfo CreateStartInfo(string executable, int processId, long startTimeUtcTicks)
    {
        var info = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        info.ArgumentList.Add("--silent");
        info.ArgumentList.Add(WaitArgument);
        info.ArgumentList.Add(processId.ToString(CultureInfo.InvariantCulture));
        info.ArgumentList.Add(startTimeUtcTicks.ToString(CultureInfo.InvariantCulture));
        return info;
    }

    /// <summary>只在显式重启交接时等待；普通启动不访问其他进程。等待失败时不得抢先启动或强杀旧进程。</summary>
    internal static bool WaitForPreviousProcess(string[] arguments, Func<int, long, bool>? wait = null)
    {
        int index = Array.FindIndex(arguments, a => string.Equals(a, WaitArgument, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return true;
        if (index + 2 >= arguments.Length ||
            !int.TryParse(arguments[index + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int processId) || processId <= 0 ||
            !long.TryParse(arguments[index + 2], NumberStyles.None, CultureInfo.InvariantCulture, out long ticks) || ticks <= 0 ||
            processId == Environment.ProcessId)
        {
            AppLogger.LogWarn("普通重启交接参数无效，取消新实例启动。");
            return false;
        }

        try
        {
            bool exited = (wait ?? WaitForExit)(processId, ticks);
            if (!exited) AppLogger.LogWarn("普通重启等待旧进程退出超时，取消新实例启动；没有强制结束旧进程。");
            return exited;
        }
        catch (Exception ex)
        {
            AppLogger.LogError("普通重启等待旧进程退出失败", ex);
            return false;
        }
    }

    private static bool WaitForExit(int processId, long startTimeUtcTicks)
    {
        Process previous;
        try { previous = Process.GetProcessById(processId); }
        catch (ArgumentException) { return true; } // 旧进程已在新实例开始等待前退出。
        using (previous)
        {
            try
            {
                if (previous.HasExited) return true;
                // PID 已被其他进程复用时不等待该无关进程，更不向其发送退出或终止请求。
                if (previous.StartTime.ToUniversalTime().Ticks != startTimeUtcTicks) return true;
                return previous.WaitForExit(60_000);
            }
            catch (InvalidOperationException) when (previous.HasExited)
            {
                return true; // 旧进程恰好在检查与读取启动时间之间退出。
            }
        }
    }
}
