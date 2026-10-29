using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using StarPie.Plugin;

namespace WinPieGestures.Plugins;

/// <summary>
/// 进程插件的动作贡献点：把一次触发翻译成「拉起一个进程」。
/// <para>
/// 在宿主里实现 <see cref="IActionContribution"/>，而不是另开一条执行通道 ——
/// 轮盘绑定、参数面板、<see cref="PluginInvoker"/> 派发都只认这个接口，
/// 合成一个实现塞进同一条流水线，那些消费方一行都不用改。
/// </para>
/// <para>
/// <b>从不等待进程退出</b>：只在很短的窗口内观察它是否立刻失败（可执行文件损坏、参数非法等）。
/// 这个窗口同时覆盖了「单实例插件第二次被触发后立刻退出」这种正常情况 —— 退出码 0 视为成功。
/// </para>
/// <para>
/// <b>进程句柄与本方法同生命周期是一个陷阱</b>：一旦释放 Process 对象，.NET 会关闭
/// 重定向用的管道，而子进程可能还在往 stderr 写。Rust 的 <c>eprintln!</c> 遇到 EPIPE
/// 会 panic 退出，表现成「插件莫名消失」。所以这里只拿 <see cref="TrackedPluginProcess"/>，
/// 真正的释放交给 <see cref="PluginProcessRuntime"/>，直到进程结束或插件停用。
/// </para>
/// </summary>
internal sealed class ProcessActionContribution : IActionContribution
{
    /// <summary>启动观察窗口的默认值（秒）。与清单里 LaunchTimeoutSeconds 的 0 值对应。</summary>
    private const int DefaultLaunchTimeoutSeconds = 3;

    private readonly IPluginLogger _logger;
    private readonly string _pluginId;
    private readonly string _pluginDirectory;
    private readonly string _dataDirectory;
    private readonly PluginManifest _manifest;
    private readonly PluginProcessAction _action;
    private readonly string _executablePath;

    public ProcessActionContribution(
        IPluginLogger logger,
        string pluginId,
        string pluginDirectory,
        string dataDirectory,
        PluginManifest manifest,
        PluginProcessAction action,
        string executablePath)
    {
        _logger = logger;
        _pluginId = pluginId;
        _pluginDirectory = pluginDirectory;
        _dataDirectory = dataDirectory;
        _manifest = manifest;
        _action = action;
        _executablePath = executablePath;
    }

    public ActionDescriptor Descriptor => new()
    {
        Id = _action.Id,
        DisplayName = _action.DisplayName,
        Description = _action.Description,
        Category = _action.Category,
        IconKey = _action.IconKey,
        Kind = _action.Kind,
        TimeoutSeconds = _action.LaunchTimeoutSeconds,
    };

    /// <summary>进程插件没有参数表单：v0.5 拿不到宿主渲染的输入控件。</summary>
    public IReadOnlyList<ParameterField> Parameters => Array.Empty<ParameterField>();

    public string? Validate(IReadOnlyDictionary<string, string> parameters) => null;

    /// <summary>
    /// 列表副标题。必须是微秒级的纯字符串拼接 —— 它会在设置页滚动时被高频调用，
    /// 所以用未替换的原始命令行，不解析路径、不碰文件系统。
    /// </summary>
    public string Preview(IReadOnlyDictionary<string, string> parameters)
    {
        string line = string.Join(" ", _manifest.Command);
        return line.Length <= 90 ? line : line.Substring(0, 89) + "…";
    }

    public async Task<ActionResult> ExecuteAsync(PluginActionInput input, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _executablePath,
            WorkingDirectory = _pluginDirectory,
            UseShellExecute = false,
            CreateNoWindow = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        // 参数 = command[1..]，逐项做占位符替换。不做 shell 解析：
        // ArgumentList 直接进 CreateProcess 的命令行，不会被任何 shell 再解释一轮。
        for (int i = 1; i < _manifest.Command.Count; i++)
        {
            startInfo.ArgumentList.Add(Substitute(_manifest.Command[i], input));
        }

        TrackedPluginProcess tracked;
        try
        {
            tracked = PluginProcessRuntime.Spawn(
                _pluginId,
                startInfo,
                (prefix, line) => _logger.Debug($"[plugin:{_pluginId}:{_action.Id}] {prefix}{line}"));
        }
        catch (Exception ex)
        {
            _logger.Error($"进程插件「{_manifest.Id}」启动失败", ex);
            return ActionResult.Fail(I18n.TF("PluginsProcessLaunchFailed", ex.GetBaseException().Message));
        }

        _logger.Info(
            $"[{_action.Id}] 已拉起进程 pid={SafePid(tracked.Process)}：{_executablePath} {string.Join(" ", startInfo.ArgumentList)}");

        int timeoutSeconds = _action.LaunchTimeoutSeconds > 0
            ? _action.LaunchTimeoutSeconds
            : DefaultLaunchTimeoutSeconds;

        using (var window = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            window.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            try
            {
                await tracked.WaitForExitAsync(window.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 到达观察窗口仍在运行 —— 常驻型插件（框选层、悬浮窗）的正常形态。
                // 调用方若主动取消，也只是「不再等」，进程该活着还是活着。
            }
        }

        // 退出任务只读取缓存；动作完成后不释放进程及管道资源。
        if (!tracked.HasExited)
        {
            return ActionResult.Ok(silent: true);
        }

        if (tracked.ExitCode == 0)
        {
            // 单实例插件第二次被触发时走的就是这条路：本进程立刻退出，把动作交给已运行实例。
            return ActionResult.Ok(silent: true);
        }

        string detail = tracked.Snapshot();
        _logger.Warn($"进程插件「{_manifest.Id}」动作「{_action.Id}」退出码 {tracked.ExitCode}：{detail}");
        return ActionResult.Fail(I18n.TF("PluginsProcessExitFailed", tracked.ExitCode, OneLine(detail)).TrimEnd());
    }

    /// <summary>
    /// 占位符替换。<b>未知占位符原样保留</b> —— 让插件能用花括号表达字面量，
    /// 也比"宿主少支持一个占位符就整个动作失败"要好。
    /// </summary>
    private string Substitute(string raw, PluginActionInput input)
    {
        if (string.IsNullOrEmpty(raw) || raw.IndexOf('{') < 0) return raw;

        ActionContext context = input.Context;
        return raw
            .Replace("{action}", _action.Id, StringComparison.Ordinal)
            .Replace("{languageCode}", context.LanguageCode ?? "", StringComparison.Ordinal)
            .Replace("{foregroundProcess}", context.ForegroundProcessName ?? "", StringComparison.Ordinal)
            .Replace("{foregroundTitle}", context.ForegroundWindowTitle ?? "", StringComparison.Ordinal)
            .Replace("{cursorX}", context.CursorX.ToString(), StringComparison.Ordinal)
            .Replace("{cursorY}", context.CursorY.ToString(), StringComparison.Ordinal)
            .Replace("{dataDir}", _dataDirectory ?? "", StringComparison.Ordinal);
    }

    private static string OneLine(string text)
    {
        string flat = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return flat.Length <= 300 ? flat : flat.Substring(0, 299) + "…";
    }

    private static int SafePid(Process process)
    {
        try { return process.Id; } catch (Exception) { return -1; }
    }
}
