using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using StarPie.Plugin;

namespace WinPieGestures.Plugins;

/// <summary>一次插件动作调用的结果。</summary>
/// <summary>
/// 插件动作没跑成的<b>原因类别</b>。
/// <para>
/// <b>为什么需要它</b>：在它出现之前，调用方要判断「失败在哪一步」只能拿
/// <see cref="PluginExecuteOutcome.Message"/> 去 <c>Contains</c> 文案（自检里就有两处：
/// <c>Contains("未启用")</c> 与 <c>Contains("参数不合法")</c>）。那种写法的寿命只到
/// 「这条文案被接进 i18n」为止 —— 之后非中文语言下断言会静默失效，
/// 而静默失效是本项目最不能接受的一类缺陷。文案可以翻译，判据不能。
/// </para>
/// <para>
/// <b>它与 <see cref="PluginExecuteOutcome.Success"/> 不是一回事</b>：
/// <c>Success=false</c> 只说明没成功，这里说明为什么。
/// </para>
/// </summary>
internal enum PluginFailureKind
{
    /// <summary>没有失败（成功时保持这个值）。</summary>
    None = 0,

    /// <summary>插件已安装但未启用，或被停用/隔离策略拒用。</summary>
    NotEnabled,

    /// <summary>找不到注册的动作 —— 插件版本变了，配置里的 FullId 对不上。</summary>
    ActionNotFound,

    /// <summary>参数校验没通过（缺必填项，或格式不合法）。</summary>
    ValidationFailed,

    /// <summary>调用被取消 —— 插件正在停用，或调用租约没能取得。</summary>
    CancelledByStop,

    /// <summary>插件自己的执行逻辑出了问题（抛异常 / 超时 / 返回 null）。</summary>
    PluginFailed,

    /// <summary>宿主这一侧的错（有 bug，看日志）。</summary>
    HostError,
}

internal readonly struct PluginExecuteOutcome
{
    /// <summary>是否存在匹配的插件贡献点。false 时调用方应按「未知动作」处理。</summary>
    public bool Handled { get; init; }

    public bool Success { get; init; }

    /// <summary>已提交到后台线程池执行，结果稍后写日志（调用方不要等待）。</summary>
    public bool QueuedToBackground { get; init; }

    public string Message { get; init; }

    /// <summary>
    /// 失败原因类别 —— 供代码判断，<b>不要</b>拿 <see cref="Message"/> 的文案去做判据。
    /// 与 <see cref="Success"/> 的关系：成功时恒为 <see cref="PluginFailureKind.None"/>；
    /// 但 <c>Success=false</c> 不一定都填了它（新加的失败分支要记得填）。
    /// </summary>
    public PluginFailureKind Failure { get; init; }

    public static readonly PluginExecuteOutcome NotHandled = new() { Handled = false, Message = "" };
}

/// <summary>
/// 插件动作调用器 —— 插件与主程序之间<b>唯一的执行接缝</b>。
/// <para>
/// 它存在的全部意义是：<b>把不可信的插件代码与宿主的核心链路彻底隔开</b>。
/// 具体做三件事：
/// ① 统一 try/catch —— 插件异常绝不冒泡到 <see cref="ActionExecutor.Execute"/>，
///    否则会命中它内部的 MessageBox 分支，把动作线程卡死在无人值守的弹窗上；
/// ② 超时 + 协作式取消 —— 保护单线程动作队列不被拖死；
/// ③ 失败计数与自动隔离 —— 让「某个插件坏了」不会变成「StarPie 坏了」。
/// </para>
/// <para>
/// <b>关于超时的一个诚实说明</b>：<see cref="Task"/> 的取消是<b>协作式</b>的。
/// 如果插件写了一个同步死循环并且不检查 CancellationToken，宿主<b>无法强杀它</b> ——
/// 这部分风险只能靠二期独立进程宿主（L2）解决，首版如实告知而不假装能解决。
/// </para>
/// </summary>
internal static class PluginInvoker
{
    public const int DefaultSequentialTimeoutSeconds = 5;
    public const int DefaultBackgroundTimeoutSeconds = 30;

    /// <summary>等待任务结束时的宽限期：先让 CancellationToken 生效，再宣布超时。</summary>
    private const int TimeoutGraceMs = 1000;

    public static PluginExecuteOutcome Invoke(
        PluginInstance instance,
        PluginActionRegistration registration,
        IReadOnlyDictionary<string, string> parameters,
        PluginCallCoordinator calls)
    {
        if (instance == null || registration == null) return PluginExecuteOutcome.NotHandled;

        if (!calls.TryAcquireInvocation(
                instance,
                PluginCallKind.ActionExecution,
                out PluginInvocationLease? lease,
                out string leaseError))
        {
            return new PluginExecuteOutcome
            {
                Handled = true,
                Success = false,
                Failure = PluginFailureKind.CancelledByStop,
                Message = leaseError,
            };
        }

        int timeoutSeconds = registration.TimeoutSeconds > 0
            ? registration.TimeoutSeconds
            : (registration.Kind == ActionKind.Background
                ? DefaultBackgroundTimeoutSeconds
                : DefaultSequentialTimeoutSeconds);

        PluginActionInput input;
        try
        {
            input = new PluginActionInput
            {
                ContributionId = registration.FullId,
                Parameters = parameters ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                Context = BuildActionContext(instance),
            };
        }
        catch (Exception ex)
        {
            lease!.Dispose();
            return Fail(instance, registration, $"构造调用上下文失败：{ex.Message}", 0);
        }

        return registration.Kind == ActionKind.Background
            ? InvokeInBackground(instance, registration, input, timeoutSeconds, lease!)
            : InvokeSequential(instance, registration, input, timeoutSeconds, lease!);
    }

    // ------------------------------------------------------------------ 串行类

    private static PluginExecuteOutcome InvokeSequential(
        PluginInstance instance,
        PluginActionRegistration registration,
        PluginActionInput input,
        int timeoutSeconds,
        PluginInvocationLease lease)
    {
        var stopwatch = Stopwatch.StartNew();
        PluginInvocationLease? ownedLease = lease;
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            timeoutCts.Token,
            lease.CancellationToken);

        try
        {
            Task<ActionResult>? task = registration.Contribution.ExecuteAsync(input, linkedCts.Token);
            if (task == null)
            {
                stopwatch.Stop();
                return Fail(instance, registration, "ExecuteAsync 返回了 null。", stopwatch.Elapsed.TotalMilliseconds);
            }

            if (!task.Wait(TimeSpan.FromSeconds(timeoutSeconds + TimeoutGraceMs / 1000.0)))
            {
                stopwatch.Stop();
                ObserveTimedOutTask(task, ownedLease, instance, registration);
                ownedLease = null;
                if (lease.CancellationToken.IsCancellationRequested)
                {
                    return CancelledByStop(registration);
                }

                return Fail(
                    instance,
                    registration,
                    $"执行超时（{timeoutSeconds}s）。已取消等待；宿主会继续追踪任务，直到插件代码真实结束。",
                    stopwatch.Elapsed.TotalMilliseconds);
            }

            stopwatch.Stop();
            ActionResult result = task.Result;
            return Interpret(instance, registration, result, stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (AggregateException aggregate)
        {
            stopwatch.Stop();
            Exception inner = aggregate.GetBaseException();
            if (inner is OperationCanceledException && lease.CancellationToken.IsCancellationRequested)
            {
                return CancelledByStop(registration);
            }
            return Fail(instance, registration, DescribeException(inner), stopwatch.Elapsed.TotalMilliseconds, inner);
        }
        catch (OperationCanceledException) when (lease.CancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return CancelledByStop(registration);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return Fail(instance, registration, DescribeException(ex), stopwatch.Elapsed.TotalMilliseconds, ex);
        }
        finally
        {
            ownedLease?.Dispose();
        }
    }

    private static void ObserveTimedOutTask(
        Task<ActionResult> task,
        PluginInvocationLease lease,
        PluginInstance instance,
        PluginActionRegistration registration)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                instance.Logger.Warn(
                    $"[{registration.FullId}] 超时后的任务最终结束：{ex.GetBaseException().Message}");
            }
            finally
            {
                lease.Dispose();
            }
        });
    }

    // ------------------------------------------------------------------ 后台类

    private static PluginExecuteOutcome InvokeInBackground(
        PluginInstance instance,
        PluginActionRegistration registration,
        PluginActionInput input,
        int timeoutSeconds,
        PluginInvocationLease lease)
    {
        string pluginId = instance.PluginId;
        IActionContribution contribution = registration.Contribution;

        try
        {
            _ = Task.Run(async () =>
            {
                using (lease)
                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds)))
                using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                           timeoutCts.Token,
                           lease.CancellationToken))
                {
                    var stopwatch = Stopwatch.StartNew();

                    try
                    {
                        Task<ActionResult>? task = contribution.ExecuteAsync(input, linkedCts.Token);
                        if (task == null)
                        {
                            stopwatch.Stop();
                            ApplyOutcome(instance, registration, false, stopwatch.Elapsed.TotalMilliseconds,
                                "ExecuteAsync 返回了 null。");
                            return;
                        }

                        Task completed = await Task.WhenAny(
                            task,
                            Task.Delay(TimeSpan.FromSeconds(timeoutSeconds + TimeoutGraceMs / 1000.0)))
                            .ConfigureAwait(false);

                        if (!ReferenceEquals(completed, task))
                        {
                            stopwatch.Stop();
                            if (lease.CancellationToken.IsCancellationRequested)
                            {
                                instance.Logger.Info($"[{registration.FullId}] 插件停用后任务仍未结束，继续持有租约等待。");
                            }
                            else
                            {
                                instance.Logger.Warn($"[{registration.FullId}] 后台执行超时（{timeoutSeconds}s），继续等待真实任务结束。");
                                ApplyOutcome(instance, registration, false, stopwatch.Elapsed.TotalMilliseconds, "后台执行超时");
                            }

                            try { await task.ConfigureAwait(false); }
                            catch (Exception ex)
                            {
                                instance.Logger.Warn($"[{registration.FullId}] 超时后的后台任务最终结束：{ex.GetBaseException().Message}");
                            }
                            return;
                        }

                        ActionResult result = await task.ConfigureAwait(false);
                        stopwatch.Stop();
                        ApplyOutcome(
                            instance, registration, result.Success, stopwatch.Elapsed.TotalMilliseconds,
                            result.Success ? null : result.Message);
                    }
                    catch (OperationCanceledException) when (lease.CancellationToken.IsCancellationRequested)
                    {
                        stopwatch.Stop();
                        instance.Logger.Info($"[{registration.FullId}] 因插件停用而取消，不计入插件故障。");
                    }
                    catch (OperationCanceledException)
                    {
                        stopwatch.Stop();
                        instance.Logger.Warn($"[{registration.FullId}] 后台执行被取消（超时）。");
                        ApplyOutcome(instance, registration, false, stopwatch.Elapsed.TotalMilliseconds, "后台执行被取消");
                    }
                    catch (Exception ex)
                    {
                        stopwatch.Stop();
                        instance.Logger.Error($"[{registration.FullId}] 后台执行抛出异常", ex);
                        ApplyOutcome(instance, registration, false, stopwatch.Elapsed.TotalMilliseconds, DescribeException(ex));
                    }
                }
            });
        }
        catch (Exception ex)
        {
            lease.Dispose();
            return Fail(instance, registration, $"提交后台任务失败：{ex.Message}", 0, ex);
        }

        return new PluginExecuteOutcome
        {
            Handled = true,
            Success = true,
            QueuedToBackground = true,
            Message = $"已提交后台执行（{pluginId}）",
        };
    }

    // ------------------------------------------------------------------ 结果处理

    private static PluginExecuteOutcome CancelledByStop(PluginActionRegistration registration) => new()
    {
        Handled = true,
        Success = false,
        Failure = PluginFailureKind.CancelledByStop,
        Message = $"{registration.DisplayName} 因插件正在停用而取消。",
    };

    private static PluginExecuteOutcome Interpret(
        PluginInstance instance,
        PluginActionRegistration registration,
        ActionResult? result,
        double elapsedMs)
    {
        if (result == null)
        {
            return Fail(instance, registration, "ExecuteAsync 返回了 null 结果。", elapsedMs);
        }

        bool success = result.Success;
        ApplyOutcome(instance, registration, success, elapsedMs, success ? null : result.Message);

        if (!success)
        {
            return new PluginExecuteOutcome
            {
                Handled = true,
                Success = false,
                Message = string.IsNullOrWhiteSpace(result.Message)
                    ? $"插件动作失败（{registration.DisplayName}）"
                    : result.Message!,
            };
        }

        // 成功且要求提示时，交给通知服务（托盘气泡），而不是干扰用户的对话框
        if (!result.Silent && !string.IsNullOrWhiteSpace(result.Message))
        {
            try
            {
                new PluginNotificationService(instance.PluginId)
                    .Notify(instance.Scan.Manifest?.Name ?? instance.PluginId, result.Message!);
            }
            catch
            {
            }
        }

        return new PluginExecuteOutcome
        {
            Handled = true,
            Success = true,
            Message = result.Message ?? "",
        };
    }

    private static PluginExecuteOutcome Fail(
        PluginInstance instance,
        PluginActionRegistration registration,
        string message,
        double elapsedMs,
        Exception? exception = null)
    {
        if (exception != null)
        {
            instance.Logger.Error($"[{registration.FullId}] {message}", exception);
        }
        else
        {
            instance.Logger.Warn($"[{registration.FullId}] {message}");
        }

        ApplyOutcome(instance, registration, false, elapsedMs, message);

        return new PluginExecuteOutcome
        {
            Handled = true,
            Success = false,
            Failure = PluginFailureKind.PluginFailed,
            Message = $"{registration.DisplayName} 执行失败：{message}",
        };
    }

    /// <summary>记录健康度，并在达到隔离阈值时自动禁用插件。</summary>
    private static void ApplyOutcome(
        PluginInstance instance,
        PluginActionRegistration registration,
        bool success,
        double elapsedMs,
        string? error)
    {
        try
        {
            bool quarantine = instance.RecordInvoke(success, elapsedMs, error);
            if (!quarantine) return;

            instance.MarkQuarantined(
                $"连续失败 {PluginInstance.QuarantineThreshold} 次，已自动禁用。最后一次错误：{error}");

            PluginRegistryStore.SetEnabled(instance.PluginId, false);
            instance.FlushHealth();

            new PluginNotificationService(instance.PluginId).Notify(
                instance.Scan.Manifest?.Name ?? instance.PluginId,
                $"该插件连续出错 {PluginInstance.QuarantineThreshold} 次，已自动禁用以保护 StarPie。可在「插件」页查看日志。");

            // 这里刻意不卸载：动作线程上做 ALC 卸载 + GC 会明显卡顿，
            // 真正的卸载交给用户在插件页确认，或下次启动时的清理。
        }
        catch (Exception ex)
        {
            instance.Logger.Warn($"记录健康度失败：{ex.Message}");
        }

        _ = registration;
    }

    private static string DescribeException(Exception exception)
    {
        string type = exception.GetType().Name;
        string message = exception.Message ?? "";

        if (exception is OperationCanceledException)
        {
            return "执行被取消（通常是宿主超时）。";
        }
        if (exception is System.IO.FileNotFoundException || exception is System.IO.DirectoryNotFoundException)
        {
            return $"文件路径不存在：{message}";
        }
        if (exception is UnauthorizedAccessException)
        {
            return $"权限不足：{message}（若需要写入受保护目录，请尝试以管理员身份运行 StarPie）";
        }
        if (exception is System.Net.Http.HttpRequestException)
        {
            return $"网络请求失败：{message}";
        }

        return string.IsNullOrWhiteSpace(message) ? type : $"{type}: {message}";
    }

    // ------------------------------------------------------------------ 上下文构造

    /// <summary>采集只读环境信息。任何一项失败都只影响该项，绝不让上下文构造失败。</summary>
    private static ActionContext BuildActionContext(PluginInstance instance)
    {
        string processName = "";
        string windowTitle = "";
        long windowHandle = 0;

        try
        {
            processName = ActiveWindowHelper.GetActiveWindowProcessName() ?? "";
        }
        catch
        {
        }

        try
        {
            windowTitle = ActiveWindowHelper.GetActiveWindowInfo(out nint handle) ?? "";
            windowHandle = handle;
        }
        catch
        {
        }

        int cursorX = 0;
        int cursorY = 0;
        try
        {
            System.Drawing.Point position = System.Windows.Forms.Cursor.Position;
            cursorX = position.X;
            cursorY = position.Y;
        }
        catch
        {
        }

        bool elevated = false;
        try
        {
            elevated = ConfigManager.IsElevated();
        }
        catch
        {
        }

        return new ActionContext
        {
            ForegroundProcessName = processName,
            ForegroundWindowTitle = windowTitle,
            ForegroundWindowHandle = windowHandle,
            CursorX = cursorX,
            CursorY = cursorY,
            IsElevated = elevated,
            LanguageCode = I18n.CurrentLanguageCode,
        };
    }
}
