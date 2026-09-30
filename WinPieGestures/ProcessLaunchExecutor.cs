using System;
using System.Diagnostics;
using StarPie.Plugin;

namespace WinPieGestures;

/// <summary>只有启动机制，没有插件参数或动作策略。显式权限没有默认启动兜底。</summary>
internal static class ProcessLaunchExecutor
{
    internal static bool Start(ProcessStartInfo info, ProcessLaunchMode mode) =>
        Start(info, mode,
            (file, args, directory, show) => ActionExecutor.TryLaunchUnelevatedViaExplorer(file, args, directory, show),
            startInfo =>
            {
                using Process? process = Process.Start(startInfo);
                return true; // Shell 激活可能成功但不返回进程句柄；返回值表示发起，不是进程退出状态。
            });

    // 可替换的内部接缝：回归测试不启动进程、不弹 UAC，也能证明失败时没有回退。
    internal static bool Start(ProcessStartInfo info, ProcessLaunchMode mode,
        Func<string, string, string, int, bool> startStandardUser,
        Func<ProcessStartInfo, bool> startProcess)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (mode == ProcessLaunchMode.Default) return startProcess(info);

        bool hidden = info.CreateNoWindow || info.WindowStyle == ProcessWindowStyle.Hidden;
        string directory = string.IsNullOrEmpty(info.WorkingDirectory) ? Environment.CurrentDirectory : info.WorkingDirectory;
        if (mode == ProcessLaunchMode.StandardUser)
            return startStandardUser(info.FileName, info.Arguments, directory, hidden ? 0 : 1);

        return startProcess(new ProcessStartInfo
        {
            FileName = info.FileName,
            Arguments = info.Arguments,
            WorkingDirectory = directory,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = hidden ? ProcessWindowStyle.Hidden : info.WindowStyle,
        });
    }
}
