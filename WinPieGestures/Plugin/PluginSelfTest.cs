using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using StarPie.Plugin;

namespace WinPieGestures.Plugins;

/// <summary>
/// 插件系统端到端自检。
/// <para>
/// 它把「识别 → 安装 → 启用 → 注册 → 调用 → 停用 → 卸载」整条链路跑一遍并输出报告，
/// 存在的意义有两个：① 无界面环境下也能验证插件系统是否真的能跑通（CI 回归）；
/// ② 用户报告「插件装不上」时，一个命令就能拿到全链路证据。
/// </para>
/// <para>
/// 用法：<c>StarPie.exe --plugin-selftest &lt;插件.dll&gt; [报告输出路径] [--skip-invoke]</c>
/// </para>
/// <para>
/// <b>自检整体跑在临时沙箱里</b>：两个根目录（可写宿主区与只读扫描目录）都会被钉到
/// <c>%TEMP%\StarPie-PluginSelfTest-&lt;随机&gt;\</c> 下，跑完即删。以前它直接跑在真实插件目录上，
/// 等于每做一次回归就动一次用户已经装好的插件。
/// </para>
/// </summary>
internal static class PluginSelfTest
{
    public static int Run(string dllPath, string? reportPath, bool skipInvoke = false)
    {
        var report = new StringBuilder();
        bool pass = true;

        void Line(string text)
        {
            report.AppendLine(text);
            // 实时打到终端。这条通道以前只写 Debug（进调试器）与最终的报告文件，
            // 命令行里跑完什么都看不到 —— 而它存在的意义恰恰是「一条命令拿到全链路证据」，
            // 前提是那条命令的输出真的看得见（父控制台的接入见 App.AttachParentConsoleIfCli）。
            Console.WriteLine(text);
            System.Diagnostics.Debug.WriteLine(text);
        }

        void Fail(string stage, string reason)
        {
            pass = false;
            Line($"  [FAIL] {stage}：{reason}");
        }

        PluginCandidate? FindCandidate(string fileName) => PluginHost.Candidates.FirstOrDefault(
            c => string.Equals(c.FileName, fileName, StringComparison.OrdinalIgnoreCase));

        Line("====================================================");
        Line("StarPie 插件系统端到端自检");
        Line($"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Line($"宿主版本：{PluginManifestReader.HostVersion} / SDK 契约：{PluginApi.ApiVersion}");
        Line($"目标文件：{dllPath}");
        if (skipInvoke)
        {
            Line("运行模式：--skip-invoke —— 跳过真实调用，不会改变本机环境");
        }
        Line("====================================================");

        // ---- 沙箱：把两个根目录钉到临时位置，跑完即删 ----
        string sandboxRoot = Path.Combine(
            Path.GetTempPath(), "StarPie-PluginSelfTest-" + Guid.NewGuid().ToString("N"));
        string sandboxHostRoot = Path.Combine(sandboxRoot, "plugin-data");
        string sandboxScanRoot = Path.Combine(sandboxRoot, "plugin");

        try
        {
            Directory.CreateDirectory(sandboxHostRoot);
            Directory.CreateDirectory(sandboxScanRoot);
            PluginPaths.OverrideRootsForTesting(sandboxHostRoot, sandboxScanRoot);
            Line($"沙箱目录：{sandboxRoot}（真实插件目录不会被触碰）");
        }
        catch (Exception sandboxError)
        {
            // 建不出沙箱就如实说明，不要假装自己是隔离的
            Line($"[WARN] 无法创建自检沙箱（{sandboxError.Message}），本次将直接跑在真实插件目录上。");
        }
        Line("====================================================");

        string? installedPluginId = null;

        try
        {
            // ---- 0 初始化 ----
            Line("");
            Line("[0] 初始化插件系统");
            var sw = Stopwatch.StartNew();

            // 自检是无界面短命进程，不该被计入启动健康统计。
            PluginHost.HeadlessMode = true;
            PluginHost.Initialize();
            sw.Stop();
            Line($"  插件根目录：{PluginPaths.Root}");
            Line($"  便携模式：{PluginPaths.IsPortable}");
            Line($"  初始化耗时：{sw.Elapsed.TotalMilliseconds:F1} ms");
            Line($"  已登记插件：{PluginHost.InstalledCount} 个");

            IReadOnlyList<string> supportedPaths = PluginHost.GetSupportedPathIds();
            Line($"  已登记调用路径：{string.Join(", ", supportedPaths)}");
            foreach (string requiredPath in new[]
                     {
                         PluginPathIds.ActionExecution,
                         PluginPathIds.InteractionEvent,
                         PluginPathIds.WheelStructure,
                         PluginPathIds.KeyboardRemap,
                     })
            {
                if (!supportedPaths.Contains(requiredPath, StringComparer.OrdinalIgnoreCase))
                {
                    Fail("调用路径架构", $"宿主未登记协议路径：{requiredPath}");
                }
            }

            // 空置路径必须可以安全调用，不应因为尚无贡献实现而影响主程序。
            int eventReceivers = PluginHost.PublishInteractionEvent(new PluginInteractionEventEnvelope
            {
                EventType = "selftest.runtime.ready",
                SessionId = 0,
                Sequence = 0,
                Context = new ActionContext(),
            });
            if (eventReceivers != 0)
            {
                Fail("交互路径占位", $"尚未开放统一交互贡献时应返回 0，实际为 {eventReceivers}。");
            }

            PluginWheelStructureSnapshot emptyStructure = PluginHost.QueryWheelStructureAsync(
                    new PluginWheelStructureRequest { ProviderId = "selftest.none" })
                .AsTask().GetAwaiter().GetResult();
            if (emptyStructure.IsAvailable)
            {
                Fail("轮盘结构路径占位", "尚未开放结构提供者时应返回空快照。");
            }

            // ---- 1 静态识别 ----
            Line("");
            Line("[1] 静态识别（不加载程序集）");
            sw.Restart();
            PluginScanResult scan = PluginScanner.ScanSelectedDll(
                dllPath,
                allowReservedIdPrefix: true);
            sw.Stop();
            Line($"  识别耗时：{sw.Elapsed.TotalMilliseconds:F2} ms");
            Line($"  结论：{(scan.Accepted ? "通过" : "拒绝")}");

            if (!scan.Accepted)
            {
                Line($"  原因码：{scan.Failure}");
                Line($"  标题：{PluginScanFailureText.Title(scan.Failure)}");
                Line($"  详情：{scan.ErrorDetail}");
                Line($"  修复建议：{PluginScanFailureText.Hint(scan.Failure)}");
                Fail("静态识别", scan.DescribeFailure());
                return Write(report, reportPath, pass);
            }

            PluginManifest manifest = scan.Manifest!;
            Line($"  清单来源：{scan.ManifestSource}");
            Line($"  ID：{manifest.Id}");
            Line($"  名称：{manifest.Name} v{manifest.Version}");
            Line($"  作者：{manifest.Author}");
            Line($"  许可证：{manifest.License}");
            Line($"  能力声明：{manifest.ResolveCapabilities()}");
            Line($"  入口类型：{scan.EntryTypeFullName ?? "(未解析)"}");
            Line($"  实际 TFM：{scan.TargetFramework}");
            Line($"  架构：{scan.MachineText}（依赖文件：{(scan.HasDependencyFile ? "有" : "无")}）");
            Line($"  文件大小：{scan.FileSizeText}");
            Line($"  SHA256：{scan.Sha256}");
            Line($"  签名：{(scan.IsSigned ? $"已签名（{scan.SignerSubject}）" : "未签名")}");

            installedPluginId = manifest.Id;

            // ---- 2 安装 ----
            Line("");
            Line("[2] 安装（复制落盘 + 登记为 Disabled）");
            var options = new PluginInstallOptions
            {
                Acknowledged = true,
                OverwriteExisting = true,
                EnableAfterInstall = false,
                AcknowledgedCapabilities = manifest.Capabilities,
                // 保留前缀（starpie.* 等）说明这是官方模块：必须按官方安装登记。
                // 这不给自检开后门 —— OfficialPluginClient 走的就是这一套（Official = true +
                // 回填 ClaimedTypes），加载路径也会按 Entry.Official 决定是否放行保留前缀。
                // 少了它，自检会在 [3] 启用那一步撞「插件 ID 使用了保留前缀」而整段 FAIL，
                // 于是官方模块这条路反而没人能验。
                Official = PluginPaths.IsReservedPluginId(manifest.Id),
            };
            PluginInstallResult install = PluginHost.CommitInstall(scan, options);
            if (!install.Success)
            {
                Fail("安装", install.Error);
                return Write(report, reportPath, pass);
            }
            Line($"  安装成功：{install.PluginId}");

            PluginInstance? instance = PluginHost.Find(install.PluginId);
            Line($"  安装后状态：{instance?.State}（已加载：{instance?.IsLoaded}）");
            if (instance?.IsLoaded == true)
            {
                Fail("安装语义", "安装后不应加载程序集，但要保持内存红线");
            }

            // ---- 3 启用 + 4 调用 ----
            // 刻意放进独立方法：这两步会拿到 PluginActionRegistration，而它的 Contribution
            // 指向插件程序集里的类型实例。这些引用若留在 Run 的栈帧上，第 5 步卸载时插件的
            // ALC 就回收不掉 —— 自检会把自己测挂，报告里出现假的「需要重启才能释放」。
            string? stageError = RunEnableAndInvoke(install.PluginId, Line, out ActionItem? lazyLoadProbe, skipInvoke);
            if (stageError != null)
            {
                Fail("启用与调用", stageError);
            }

            // ---- 5 活动调用租约 + 异步停用 ----
            string? leaseError = RunInvocationLeaseStopProbe(install.PluginId, Line);
            if (leaseError != null)
            {
                Fail("活动调用租约", leaseError);
            }

            instance = PluginHost.Find(install.PluginId);
            Line($"  停用后状态：{instance?.State}");
            Line($"  活动调用数：{instance?.ActiveCallCount}");
            Line($"  剩余已注册动作：{PluginHost.Catalog.SnapshotActions().Count} 个");

            if (PluginHost.Catalog.SnapshotActions().Count != 0)
            {
                Fail("贡献点撤销", "停用后仍有动作残留在注册表里");
            }

            string? lazyLoadError = RunLazyLoadProbe(install.PluginId, lazyLoadProbe, Line);
            if (lazyLoadError != null)
            {
                Fail("首次惰性调用", lazyLoadError);
            }

            // ---- 6 卸载 ----
            Line("");
            Line("[6] 卸载（删除目录 + 移除登记）");
            PluginUninstallResult uninstall = PluginHost.UninstallForSelfTestAsync(
                    install.PluginId,
                    removePluginData: true)
                .GetAwaiter().GetResult();
            Line($"  卸载结果：{(uninstall.Success ? "成功" : "失败")}");
            if (!uninstall.Success)
            {
                Fail("卸载", uninstall.Error);
            }
            else
            {
                installedPluginId = null;
            }

            // ---- 3d 只读扫描目录（候选识别 → 单枚复制 → 装后状态）----
            Line("");
            Line("[3d] 只读扫描目录与候选安装（沙箱内）");

            string candidateFileName = Path.GetFileName(dllPath);
            const string DecoyFileName = "notaplugin.dll";

            // ① 空目录必须是 0 个候选
            int emptyCount = PluginHost.ScanCandidates();
            if (emptyCount != 0)
            {
                Fail("候选扫描", $"空的扫描目录里扫出了 {emptyCount} 个候选");
            }

            // ② 放一枚真插件，再放一枚「看着像 dll 其实不是」的文件
            File.Copy(dllPath, Path.Combine(sandboxScanRoot, candidateFileName), overwrite: true);
            File.WriteAllText(Path.Combine(sandboxScanRoot, DecoyFileName), "这只是一个文本文件，不是程序集。");
            PluginHost.ScanCandidates();

            PluginCandidate? real = FindCandidate(candidateFileName);
            PluginCandidate? decoy = FindCandidate(DecoyFileName);

            if (real == null)
            {
                Fail("候选扫描", $"扫描目录里没有扫出 {candidateFileName}");
            }
            else if (PluginPaths.IsReservedPluginId(real.PluginId))
            {
                // 保留前缀＝官方模块：它不该被判成「可安装」—— 宿主在 InstallCandidateAsync 里
                // 按契约会拒绝，界面上再留一个能点的按钮，就是让用户点一次必然失败的操作。
                // 期望形态：状态「官方模块」+ 不给安装按钮 + 说明行指出正确的安装入口。
                Line($"  官方模块在扫描目录里的判定：{real.StateText}（可安装={real.CanInstall}）");
                if (real.State != PluginCandidateState.Reserved)
                {
                    Fail("候选扫描", $"保留前缀的官方模块应判为「官方模块」，实际是 {real.State}");
                }
                if (real.CanInstall)
                {
                    Fail("候选扫描", "官方模块不允许从扫描目录安装，就不该给「安装」按钮 —— 点了必然失败");
                }
                if (!real.HasNote)
                {
                    Fail("候选扫描", "官方模块必须有一句说明，告诉用户该去官方插件列表里安装");
                }
            }
            else if (real.State != PluginCandidateState.Installable)
            {
                Fail("候选扫描", $"未安装过的插件应判为「可安装」，实际是 {real.State}");
            }

            if (decoy == null)
            {
                Fail("候选扫描", "非程序集文件没有被扫出来 —— 用户会以为「放进去了却毫无反应」");
            }
            else
            {
                Line($"  非程序集文件的结论：{decoy.StateText}｜{decoy.Note}");
                if (decoy.State != PluginCandidateState.Rejected || decoy.CanInstall)
                {
                    Fail("候选扫描", "非程序集文件必须判为「无法识别」且不给安装按钮");
                }
            }

            Line($"  候选数：{PluginHost.Candidates.Count} 个（可安装 {PluginHost.Candidates.Count(x => x.CanInstall)} 个）");

            if (real != null)
            {
                // ③ 点「安装」—— 与界面上那个按钮完全同一条路
                PluginInstallResult candidateInstall = PluginHost.InstallCandidateAsync(real).GetAwaiter().GetResult();
                bool installedByCandidate = candidateInstall.Success;
                string candidateError = candidateInstall.Error;

                // 保留前缀的模块只能走官方在线目录，社区候选安装必须拒绝它。
                // 所以拿官方 dll 跑自检时，这里要断言的正是「被拒绝」——
                // 改成在线目录分发之前，官方 dll 恰好是从这个扫描目录装进来的，
                // 那时这里断言的是「装成功了」，迁移后若照旧断言，自检会假红。
                if (PluginPaths.IsReservedPluginId(real.PluginId))
                {
                    Line("  本次目标是官方模块（保留前缀）⇒ 候选安装按契约应被拒绝");
                    if (installedByCandidate)
                    {
                        Fail("候选安装", "保留前缀的官方模块不允许从扫描目录安装，但候选安装竟然成功了");
                    }
                    else
                    {
                        Line($"  拒绝理由：{candidateError}");
                    }
                }
                else if (!installedByCandidate)
                {
                    Fail("候选安装", candidateError);
                }
                else
                {
                    PluginInstance? installed = PluginHost.Find(real.PluginId!);
                    Line($"  安装后状态：{installed?.State}｜登记来源：{installed?.Entry.Source}");

                    // 裸 DLL 安装必须「装完就能跑」。这里曾经是个真缺陷：裸 dll 安装不回填
                    // plugin.json，而安装目录的识别要求目录里有清单 —— 于是插件装得上却永远
                    // 启用不了，报错是一句与真实原因无关的「插件目录里缺少 plugin.json」。
                    if (installed?.State != PluginRuntimeState.Active)
                    {
                        Fail("候选安装启用",
                            $"候选安装后插件应处于运行态，实际是 {installed?.State}（{installed?.LastError}）");
                    }

                    string installedManifest = PluginPaths.GetManifestPath(installed?.ManagedDirectory ?? "");
                    if (!File.Exists(installedManifest))
                    {
                        Fail("候选安装启用", $"裸 DLL 安装没有回填清单，后续识别与启用都会失败：{installedManifest}");
                    }

                    // C1 回归断言：裸 DLL 安装只复制那一枚，绝不能把扫描目录里的邻居一起搬走。
                    // 搬走邻居的后果不是「多几个文件」这么轻：装了 A 却连带出现 B，
                    // 而且 B 还会因为目录里存在两枚业务 dll 而识别失败。
                    string managedDirectory = installed?.ManagedDirectory ?? "";
                    string[] managedDlls = Directory.Exists(managedDirectory)
                        ? Directory.GetFiles(managedDirectory, "*.dll", SearchOption.TopDirectoryOnly)
                        : Array.Empty<string>();

                    Line($"  宿主目录内的程序集：{managedDlls.Length} 枚" +
                        (managedDlls.Length > 0 ? $"（{string.Join("、", managedDlls.Select(Path.GetFileName))}）" : ""));

                    if (managedDlls.Length != 1
                        || !string.Equals(Path.GetFileName(managedDlls[0]), candidateFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        Fail("单枚复制",
                            $"裸 DLL 安装只应复制 {candidateFileName} 这一枚，实际宿主目录里有 {managedDlls.Length} 枚");
                    }

                    if (!string.Equals(installed?.Entry.Source, "ScanDirectory", StringComparison.Ordinal))
                    {
                        Fail("安装来源", $"候选安装的登记来源应为 ScanDirectory，实际是 {installed?.Entry.Source}");
                    }

                    // ④ 重扫：同一枚文件应变成「已装同版本」
                    PluginHost.ScanCandidates();
                    PluginCandidate? afterInstall = FindCandidate(candidateFileName);
                    Line($"  重扫后状态：{afterInstall?.StateText ?? "(消失)"}");
                    if (afterInstall?.State != PluginCandidateState.Installed)
                    {
                        Fail("装后状态",
                            $"装完之后同一枚文件应判为「已装同版本」，实际是 {afterInstall?.State.ToString() ?? "(消失)"}");
                    }

                    // ⑤ 同 ID 撞车：两枚都必须是「ID 重复」且都不给安装按钮
                    string duplicateName = "copy-" + candidateFileName;
                    File.Copy(dllPath, Path.Combine(sandboxScanRoot, duplicateName), overwrite: true);
                    PluginHost.ScanCandidates();

                    PluginCandidate? first = FindCandidate(candidateFileName);
                    PluginCandidate? second = FindCandidate(duplicateName);
                    Line($"  ID 重复：{first?.StateText ?? "(消失)"} / {second?.StateText ?? "(消失)"}");

                    if (first?.State != PluginCandidateState.Duplicate || second?.State != PluginCandidateState.Duplicate)
                    {
                        Fail("ID 重复", "扫描目录里两枚 dll 声明同一 ID 时，两者都必须判为「ID 重复」");
                    }
                    else if (first.CanInstall || second.CanInstall)
                    {
                        Fail("ID 重复", "ID 重复的候选一律不能给安装按钮 —— 装哪一枚都说不清");
                    }
                    else if (first.Note.IndexOf(real.PluginId!, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        Fail("ID 重复", $"冲突说明里应写明撞车的是哪个 ID，实际是：{first.Note}");
                    }
                    else if (string.Equals(first.FileName, second.FileName, StringComparison.OrdinalIgnoreCase))
                    {
                        Fail("ID 重复", "两行候选必须各自显示自己的文件名，否则用户看不出该删哪一个");
                    }

                    // ⑥ 收拾干净：停用 → 等 ALC 回收结论 → 卸载 → 删掉扫描目录
                    //
                    // 顺序和 [5]/[6] 一致，不能省掉「等回收」这一步：插件程序集还挂在
                    // 未卸载的 ALC 上时文件是锁着的，此刻删目录会失败 —— 而失败又只体现在
                    // 一个被吞掉的异常里，表现就是临时目录里一次次堆出残留沙箱。
                    _ = PluginHost.DisableAsync(real.PluginId!, PluginStopReason.SelfTest).GetAwaiter().GetResult();
                    PluginHost.Find(real.PluginId!)?.WaitForUnloadVerdict(5000);

                    PluginUninstallResult cleanup = PluginHost.UninstallForSelfTestAsync(real.PluginId!, removePluginData: true).GetAwaiter().GetResult();
                    string cleanupError = cleanup.Error;
                    if (!cleanup.Success)
                    {
                        Fail("候选安装清理", cleanupError);
                    }
                }

                Directory.Delete(sandboxScanRoot, recursive: true);
                int afterDelete = PluginHost.ScanCandidates();
                bool recreated = Directory.Exists(sandboxScanRoot);

                Line($"  扫描目录删除后：候选 {afterDelete} 个，目录被重建={recreated}");
                if (afterDelete != 0)
                {
                    Fail("候选扫描", $"扫描目录已删除，却仍扫出 {afterDelete} 个候选");
                }
                if (recreated)
                {
                    Fail("扫描目录", "扫描目录不存在时被重新创建了 —— 程序装在只读位置会直接变成权限错误");
                }
            }

            // ---- 3j 宿主服务面与能力门禁 ----
            //
            // 这一段验的是「插件干活时真正碰到的那几层宿主接口」，与具体插件无关，
            // 所以刻意放在候选扫描之后 —— 它不需要任何插件在场，也不加载任何程序集。
            //
            // 守的是一处**设计意图**，而不是某个具体实现：
            // 「安装确认页上展示的能力，真的对应一个后果」。
            //
            // 必须在这里说清的是：门禁换来的**不是安全**。进程内插件本来就能自己
            // Process.Start / P/Invoke SetWindowPos，SDK 拦不住 ——
            // 它拦的只是「让宿主替你干活」这条路径。
            // 用户看到「本插件需要『进程』能力」与「它其实什么都能干」之间的矛盾，
            // 是进程内插件模型的固有代价；摊开写在这里，免得后来者以为这里守住了什么。
            //
            // 反过来，这条门禁要是漏了，插件清单里的能力声明就成了一句空话：
            // 安装页照旧弹一个「需要『窗口控制』能力」的确认框，用户点了同意，
            // 而这个勾选在运行时没有任何对应物 —— 那才是真正骗人的地方。
            //
            // ★ 本段曾在 refactor 分支重写自检时被整段丢弃（段落号与行数两重证据见
            // CHANGELOG「自检护栏丢失」一节），此后 AGENTS.md 与若干类注释仍声称它存在。
            // 恢复时按<b>现行</b>服务名重写，编号一律按执行顺序重排（旧版是 ①②③③b③c③d⑤④⑤）。
            //
            // ---- 3e 安装确认页正文（候选安装 / 手动安装共用一份，且整页必须随语言切换）----
            //
            // 这一段守的是前几轮 i18n 漏接的共同形态：编译、静态检查、词表覆盖率全绿，
            // 界面上却仍是中文 —— 只有真的切一次语言才看得见。确认页又是最不能含糊的一页
            // （用户在这里决定「要不要让这段代码在我电脑上跑」），所以它值得一条机器断言。
            //
            // 用<b>合成</b>的扫描结果而不是传入的那枚 dll：正文里唯一的非词条来源就是清单字段
            // 与文件路径，合成数据能把它们钉成纯 ASCII，于是「英文页里有没有方块字」成为无歧义判据。
            // 换成真实插件的话，一个中文插件名就会把断言染红，而那不是缺陷。
            //
            // 正文之所以能从 SettingsWindow 里搬出来（搬进 PluginInstallConfirmationText），
            // 也正是为了这一段：留在窗口类里的话，无界面自检碰不到它。
            Line("");
            Line("[3e] 安装确认页正文（候选安装 / 手动安装共用一份，且整页随语言切换）");

            var confirmScan = new PluginScanResult
            {
                Accepted = true,
                DllPath = @"C:\selftest\Demo.Plugin.dll",
                TargetFramework = ".NET 8.0",
                MachineText = "x64",
                FileSizeText = "24 KB",
                Sha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                IsSigned = false,
                ManifestSource = "plugin.json",
                Manifest = new StarPie.Plugin.PluginManifest
                {
                    Id = "demo.selftest.plugin",
                    Name = "Self Test Plugin",
                    Description = "Synthetic plugin used by the self test only.",
                    Author = "StarPie Self Test",
                    Version = "1.2.3",
                    Capabilities = new List<string> { "Process", "WindowControl" },
                },
            };

            var confirmInput = new PluginInstallConfirmation
            {
                Scan = confirmScan,
                State = PluginCandidateState.Installable,
                Note = "Synthetic note from the scan folder.",
                EnableAfterInstall = true,
            };

            LanguageCode originalLanguage = I18n.CurrentLanguage;
            var confirmTexts = new Dictionary<LanguageCode, string>();

            try
            {
                foreach (LanguageCode language in Enum.GetValues<LanguageCode>())
                {
                    I18n.CurrentLanguage = language;
                    string text = PluginInstallConfirmationText.Build(confirmInput);
                    confirmTexts[language] = text;

                    // ① 不留未替换的占位符。
                    // 词条里的 {n} 个数与调用点实参个数不一致时，string.Format **不报错、不抛异常**，
                    // 只是那一段信息静默消失或多出一个裸 {1} —— 两种都只有把正文打出来才看得见。
                    int open = text.IndexOf('{');
                    if (open >= 0)
                    {
                        int length = Math.Min(60, text.Length - open);
                        Fail("确认页文案", $"{language} 页里残留未替换的占位符：" +
                            text.Substring(open, length).Replace("\n", "\\n"));
                    }

                    if (text.Length < 200)
                    {
                        Fail("确认页文案", $"{language} 页只有 {text.Length} 个字符，正文明显没拼全");
                    }
                }

                // ② 英文页里不许出现方块字与中文标点。
                // 正文里的每一个字都来自词条，所以这一条实际上等价于「这些词条到底翻了没有」。
                // 它抓到过一个真实漏翻：能力清单原本用 <c>string.Join("、", …)</c> 连接，
                // 顿号是写死的中文标点，英文页会出现「Declared capabilities: Process、WindowControl」。
                if (confirmTexts.TryGetValue(LanguageCode.En, out string? enText))
                {
                    char? leak = FindCjkLeak(enText);
                    if (leak.HasValue)
                    {
                        int at = enText.IndexOf(leak.Value);
                        int from = Math.Max(0, at - 25);
                        int length = Math.Min(60, enText.Length - from);
                        Fail("确认页文案",
                            $"英文页里出现了中文/日文字符「{leak.Value}」(U+{(int)leak.Value:X4})：" +
                            $"…{enText.Substring(from, length).Replace("\n", "\\n")}…");
                    }
                    else
                    {
                        Line($"  英文页：{enText.Length} 字符，无方块字与中文标点 ✓");
                    }
                }

                // ③ 四种语言必须产生四份互不相同的正文。
                // 两两相同说明有一门语言根本没走自己的词条 —— 而它看上去「有译文」。
                var languages = confirmTexts.Keys.ToList();
                for (int i = 0; i < languages.Count; i++)
                {
                    for (int j = i + 1; j < languages.Count; j++)
                    {
                        if (string.Equals(confirmTexts[languages[i]], confirmTexts[languages[j]], StringComparison.Ordinal))
                        {
                            Fail("确认页文案", $"{languages[i]} 与 {languages[j]} 的正文完全相同，必然有一门没走自己的词条");
                        }
                    }
                }

                // ④ 关键字段真的拼进去了（守「加了字段忘了接」这类半截改动）。
                // 挑的是三段来源各异的字段：清单里的 ID、磁盘上的路径、宿主计算出的安装位置。
                if (confirmTexts.TryGetValue(LanguageCode.ZhCn, out string? zhText))
                {
                    foreach ((string label, string needle) in new[]
                    {
                        ("插件 ID", confirmScan.Manifest!.Id),
                        ("来源文件路径", confirmScan.DllPath),
                        ("安装目标路径", PluginPaths.Root),
                        ("能力原始 ID", "WindowControl"),
                    })
                    {
                        if (!zhText.Contains(needle, StringComparison.Ordinal))
                        {
                            Fail("确认页文案", $"正文里找不到{label}「{needle}」—— 这一段没拼进去或拼错了来源");
                        }
                    }
                }

                // ⑤ 「装完是否立即启用」的两态必须产生不同正文。
                // 这句话直接决定用户对结果的预期，两个分支接错（都取到同一个键）不会有任何报错。
                string enabledText = PluginInstallConfirmationText.Build(confirmInput);
                string disabledText = PluginInstallConfirmationText.Build(new PluginInstallConfirmation
                {
                    Scan = confirmScan,
                    State = PluginCandidateState.Installable,
                    Note = confirmInput.Note,
                    EnableAfterInstall = false,
                });

                if (string.Equals(enabledText, disabledText, StringComparison.Ordinal))
                {
                    Fail("确认页文案", "「装完立即启用」与「装完保持未启用」产生了同一份正文，两个分支接错了");
                }
            }
            finally
            {
                // 语言是全局状态：中途 return / 抛异常都必须还原，否则后面几段的断言
                // 会在一个非中文环境里跑（而它们大多是中文比对）。
                I18n.CurrentLanguage = originalLanguage;
            }

            // ⑥ 每个状态位都要有一句「装下去会覆盖掉什么」，且这句必须互不相同。
            //
            // 判据刻意写成「与兜底措辞相同的状态集合正好是哪几个」而不是「这几个必须不同」：
            // 将来新增一个<b>会走到确认页</b>的状态位却忘了配文案时，它会落进这个集合里 ⇒ 断言红。
            // 写成列举式的话，新状态位根本没有机会被这条断言看见（项目里已经栽过三次这种「断言跟丢了」）。
            var expectFallbackStates = new[]
            {
                // Replaced 本来就是这句话的本体，不是兜底。
                PluginCandidateState.Replaced,
                // 以下三种到不了确认页：候选路径不为它们显示安装按钮，
                // 手动路径的「识别未通过」也在更早的分支返回了。
                PluginCandidateState.Duplicate,
                PluginCandidateState.Reserved,
                PluginCandidateState.Rejected,
            };

            var fallbackStates = new List<PluginCandidateState>();
            string fallbackOutcome = PluginInstallConfirmationText.DescribeOutcome(PluginCandidateState.Replaced);

            foreach (PluginCandidateState state in Enum.GetValues<PluginCandidateState>())
            {
                string outcome = PluginInstallConfirmationText.DescribeOutcome(state);
                if (string.IsNullOrWhiteSpace(outcome))
                {
                    Fail("确认页文案", $"状态 {state} 没有对应的「会怎样」文案，但它是具名状态位");
                }
                else if (string.Equals(outcome, fallbackOutcome, StringComparison.Ordinal))
                {
                    fallbackStates.Add(state);
                }
            }

            if (!fallbackStates.OrderBy(s => s).SequenceEqual(expectFallbackStates.OrderBy(s => s)))
            {
                Fail("确认页文案",
                    "共用兜底措辞的状态是 [" + string.Join(", ", fallbackStates) + "]，预期是 [" +
                    string.Join(", ", expectFallbackStates) + "] —— 新增了具名状态位却忘了给它配文案" +
                    "（或反过来：某条文案被改成了与兜底相同）。");
            }
            else
            {
                Line($"  安装后果文案：{Enum.GetValues<PluginCandidateState>().Length} 个状态位全部有文案，其中 " +
                    $"{fallbackStates.Count} 个共用兜底（{string.Join(" / ", fallbackStates)}） ✓");
            }

            Line("");
            Line("[3j] 宿主服务面与能力门禁（命令 / Shell 动词 / 窗口控制 / 屏幕截取 / 系统功能 / 轮盘呼出）");

            // ① 类型关系：拒绝异常刻意不继承 PluginContractException。
            //
            // 后者会让宿主把插件整体标记为加载失败并卸载 —— 而「清单里漏了一行能力声明」
            // 远不到那个程度。真继承上去，用户看到的是「插件突然坏了 / 被系统禁用了」，
            // 排查方向会完全跑偏。
            if (typeof(PluginContractException).IsAssignableFrom(typeof(PluginCapabilityDeniedException)))
            {
                Fail("能力门禁",
                    "PluginCapabilityDeniedException 继承了 PluginContractException —— " +
                    "漏写一行能力声明会让整个插件被卸载，而用户看到的提示是「插件坏了」");
            }

            const string gateProbePluginId = "starpie.selftest.gate";
            var deniedCommandService = new PluginCommandService(gateProbePluginId, PluginCapability.None);
            var deniedShellService = new PluginShellService(gateProbePluginId, PluginCapability.None);
            var deniedWindowService = new PluginWindowService(gateProbePluginId, PluginCapability.None);
            var deniedCaptureService = new PluginScreenCaptureService(gateProbePluginId, PluginCapability.None);
            var deniedSystemService = new PluginSystemService(gateProbePluginId, PluginCapability.None);
            var deniedWheelService = new PluginWheelService(gateProbePluginId, PluginCapability.None);
            var deniedRemapService = new PluginKeyboardRemapService(gateProbePluginId, PluginCapability.None);

            // ② 未声明所需能力：必须拒绝。
            //
            // 探针一律传<b>空参数</b>（空命令 / 空动词 / 空布局码）—— 这一点都不影响结论：
            // 门禁是 RequireCapability 的第一件事，排在「空值短路」之前，
            // 所以被拒绝时参数根本没被分析过。更重要的是，它证明门禁确实在 Guard **之外** ——
            // 若挪进 Guard 里，异常会被吞掉、转成一个 false 返回值，
            // 用户看到的是「命令没执行」，而不是「本插件缺少『进程』能力」。
            (bool commandDenied, string commandGateDetail) =
                ProbeCapabilityGate(() => deniedCommandService.Run(""));

            if (commandDenied)
            {
                Line($"  Commands.Run：{commandGateDetail} ✓");
            }
            else
            {
                Fail("能力门禁", $"未声明 Process 的插件调用 Commands.Run 没有被正确拒绝：{commandGateDetail}");
            }

            (bool shellDenied, string shellGateDetail) =
                ProbeCapabilityGate(() => deniedShellService.Invoke(""));

            if (shellDenied)
            {
                Line($"  Shell.Invoke：{shellGateDetail} ✓");
            }
            else
            {
                Fail("能力门禁", $"未声明 Process 的插件调用 Shell.Invoke 没有被正确拒绝：{shellGateDetail}");
            }

            // 窗口服务用空布局码做探针还有一层额外好处：万一门禁真的漏了，
            // 空值短路会让它返回 false —— 探针<b>不会动到自检者自己的窗口</b>。
            // 换成 ToggleTopmost / SetOpacity 之类，门禁一旦写错就会当场改掉用户窗口的状态，
            // 而那时自检已经在报错了，没人会想到这个额外的副作用。
            (bool windowDenied, string windowGateDetail) =
                ProbeCapabilityGate(() => deniedWindowService.ApplyLayout(""), PluginCapability.WindowControl);

            if (windowDenied)
            {
                Line($"  Windows.ApplyLayout：{windowGateDetail} ✓");
            }
            else
            {
                Fail("能力门禁",
                    $"未声明 WindowControl 的插件调用 Windows.ApplyLayout 没有被正确拒绝：{windowGateDetail}");
            }

            // 截屏服务<b>只断言拒绝路径</b>，刻意不断言「声明后放行」，也不做跨能力交叉断言。
            //
            // 它是这一批里唯一没有「可以传空值短路的参数」的服务（CaptureAndRecognize 无参）：
            // 一旦门禁真的漏了，探针会当场弹出全屏框选界面，把自检者正在做的事打断 ——
            // 而那时自检已经在报错了，没人会想到这个额外的副作用。
            // 「放行」那一半的正确性由真实使用保证（官方 Ocr 包的清单声明了 ScreenCapture）。
            (bool captureDenied, string captureGateDetail) =
                ProbeVoidCapabilityGate(
                    () => deniedCaptureService.CaptureAndRecognize(),
                    PluginCapability.ScreenCapture);

            if (captureDenied)
            {
                Line($"  ScreenCapture.CaptureAndRecognize：{captureGateDetail} ✓");
            }
            else
            {
                Fail("能力门禁",
                    $"未声明 ScreenCapture 的插件调用 ScreenCapture.CaptureAndRecognize 没有被正确拒绝：{captureGateDetail}");
            }

            // 系统功能服务的三段断言与窗口服务那三条并排，读起来才成体系。
            //
            // 它与前三个服务有一处不同，也是这一段存在的理由：门禁认的是
            // <b>InputSimulation 而不是 Process</b>。系统控制这个包里两者都会发生
            // （最小化 = 合成按键，关机 = 起进程），所以「认错能力」在这里的后果最具体 ——
            // 若哪天有人把 required 改成 Process，一个只声明了「进程」的插件
            // 就能往用户正在打字的窗口里按键，而安装确认页上那句「模拟输入」变成空话。
            //
            // 三段探针一律传<b>空键</b>：门禁排在空值短路之前，所以拒绝路径照样被验到；
            // 而万一门禁真漏了，空值短路会让它返回 false —— 不起进程、不按键。
            (bool systemDenied, string systemGateDetail) =
                ProbeCapabilityGate(() => deniedSystemService.RunPreset(""), PluginCapability.InputSimulation);

            if (systemDenied)
            {
                Line($"  System.RunPreset：{systemGateDetail} ✓");
            }
            else
            {
                Fail("能力门禁",
                    $"未声明 InputSimulation 的插件调用 System.RunPreset 没有被正确拒绝：{systemGateDetail}");
            }

            // 轮盘服务的两条拒绝探针都用 <b>NaN 坐标 / 无会话状态</b>：门禁若真漏了，
            // 参数校验也会当场拦下 —— 自检绝不可能因为探针写错而弹出一个全屏遮罩。
            (bool wheelShowDenied, string wheelShowGateDetail) =
                ProbeCapabilityGate(() => deniedWheelService.ShowWheel(double.NaN, double.NaN), PluginCapability.Wheel);

            if (wheelShowDenied)
            {
                Line($"  Wheel.ShowWheel：{wheelShowGateDetail} ✓");
            }
            else
            {
                Fail("能力门禁",
                    $"未声明 Wheel 的插件调用 Wheel.ShowWheel 没有被正确拒绝：{wheelShowGateDetail}");
            }

            // Dismiss 单独也有一条：每个方法都得自己过门禁，<b>不能蹭同门的 ShowWheel</b>。
            // 这条是防复制粘贴事故的：将来加第三个方法时，漏写 RequireCapability 的
            // 「收盘」会把属主校验之后的会话替别人收掉 —— ShowWheel 的断言对此全绿。
            (bool wheelDismissDenied, string wheelDismissGateDetail) =
                ProbeCapabilityGate(() => deniedWheelService.DismissWheel(), PluginCapability.Wheel);

            if (wheelDismissDenied)
            {
                Line($"  Wheel.DismissWheel：{wheelDismissGateDetail} ✓");
            }
            else
            {
                Fail("能力门禁",
                    $"未声明 Wheel 的插件调用 Wheel.DismissWheel 没有被正确拒绝：{wheelDismissGateDetail}");
            }

            (bool remapActivateDenied, string remapActivateGateDetail) =
                ProbeRemapCapabilityGate(() => deniedRemapService.Activate(new KeyboardRemapOptions()), PluginCapability.InputRemapping);

            if (remapActivateDenied)
            {
                Line($"  KeyboardRemap.Activate：{remapActivateGateDetail} ✓");
            }
            else
            {
                Fail("能力门禁",
                    $"未声明 InputRemapping 的插件调用 KeyboardRemap.Activate 没有被正确拒绝：{remapActivateGateDetail}");
            }

            (bool remapDeactivateDenied, string remapDeactivateGateDetail) =
                ProbeRemapCapabilityGate(() => deniedRemapService.Deactivate(), PluginCapability.InputRemapping);

            if (remapDeactivateDenied)
            {
                Line($"  KeyboardRemap.Deactivate：{remapDeactivateGateDetail} ✓");
            }
            else
            {
                Fail("能力门禁",
                    $"未声明 InputRemapping 的插件调用 KeyboardRemap.Deactivate 没有被正确拒绝：{remapDeactivateGateDetail}");
            }

            (bool remapToggleDenied, string remapToggleGateDetail) =
                ProbeRemapCapabilityGate(() => deniedRemapService.Toggle(new KeyboardRemapOptions()), PluginCapability.InputRemapping);

            if (remapToggleDenied)
            {
                Line($"  KeyboardRemap.Toggle：{remapToggleGateDetail} ✓");
            }
            else
            {
                Fail("能力门禁",
                    $"未声明 InputRemapping 的插件调用 KeyboardRemap.Toggle 没有被正确拒绝：{remapToggleGateDetail}");
            }

            // ③ 声明了所需能力：同一个调用必须放行。
            //
            // 少了这一半，把门禁写成「永远拒绝」也能通过上面全部断言 ——
            // 而那会让所有正常插件都废掉，且现象与「插件坏了」一模一样。
            var allowedCommandService = new PluginCommandService(
                gateProbePluginId, PluginCapability.Process | PluginCapability.FileSystem);

            try
            {
                bool emptyCommandResult = allowedCommandService.Run("   ");

                if (emptyCommandResult)
                {
                    Fail("能力门禁", "空命令竟然报告执行成功 —— 空值短路失效，用户会以为命令跑过了");
                }
                else
                {
                    Line("  已声明 Process：放行 ✓（空命令由空值短路拦下，未真的起进程）");
                }
            }
            catch (PluginCapabilityDeniedException denied)
            {
                Fail("能力门禁", $"已声明 Process 却被拒绝（{denied.Capability}）—— 门禁判据写错了，正常插件会全部废掉");
            }
            catch (Exception gateError)
            {
                Fail("能力门禁", $"已声明 Process 的调用抛出异常：{gateError}");
            }

            // ③b 同一个基类，服务必须各认自己的能力。
            //
            // 守的是「required 传错」：所有服务的门禁现在是同一段代码（PluginGatedService），
            // 复制粘贴时把 WindowControl 写成 Process（或反过来）不会有任何编译错误，
            // 而后果是「只声明了进程的插件可以任意动用户的窗口」或「合法插件全被拒」。
            // <b>上面那些断言对这个错误照样全绿</b> —— 因为它们只验了各自那一对。
            var mismatchedWindowService = new PluginWindowService(gateProbePluginId, PluginCapability.Process);
            var mismatchedCommandService = new PluginCommandService(gateProbePluginId, PluginCapability.WindowControl);

            try
            {
                // 空布局码：真被放行时也只会走到空值短路并返回 false，不动任何窗口。
                bool leaked = mismatchedWindowService.ApplyLayout("");

                Fail("能力门禁",
                    $"只声明 Process 的插件调用了窗口服务却没被拒绝（返回 {leaked}）—— " +
                    "服务认错了能力标志，安装确认页上的「窗口控制」标签形同虚设");
            }
            catch (PluginCapabilityDeniedException)
            {
                Line("  跨能力：只声明 Process 调用窗口服务仍被拒绝 ✓（各服务认自己的能力）");
            }

            try
            {
                mismatchedCommandService.Run("");
                Fail("能力门禁", "只声明 WindowControl 的插件调用命令服务却没被拒绝 —— 服务认错了能力标志");
            }
            catch (PluginCapabilityDeniedException)
            {
                Line("  跨能力：只声明 WindowControl 调用命令服务仍被拒绝 ✓");
            }

            // 「只声明 Process 也不行」——把「系统服务认的不是 Process」变成机器可验的。
            var processOnlySystemService = new PluginSystemService(
                gateProbePluginId, PluginCapability.Process);

            (bool processOnlyDenied, string processOnlyDetail) =
                ProbeCapabilityGate(() => processOnlySystemService.RunPreset(""), PluginCapability.InputSimulation);

            if (processOnlyDenied)
            {
                Line($"  跨能力：只声明 Process 调用系统服务仍被拒绝 ✓（{processOnlyDetail}）");
            }
            else
            {
                Fail("能力门禁",
                    "只声明了 Process 的插件调用 System.RunPreset 竟然被放行 —— " +
                    "两种能力在系统控制里都会发生，但后果不同：前者是多一个后台进程，" +
                    $"后者是往用户正在打字的窗口里按键。{processOnlyDetail}");
            }

            // 轮盘服务的跨能力探针：只声明 Process 调不动轮盘。轮盘上跑的动作
            // <b>很多确实就是起进程</b> —— 这个「认错能力」在这里同样是最有诱惑力的错法，
            // 与系统服务那段并排，两个都是「顺手的标志恰好覆盖了真正的后果」。
            var processOnlyWheelService = new PluginWheelService(gateProbePluginId, PluginCapability.Process);

            (bool wheelCrossDenied, string wheelCrossDetail) =
                ProbeCapabilityGate(() => processOnlyWheelService.ShowWheel(double.NaN, double.NaN), PluginCapability.Wheel);

            if (wheelCrossDenied)
            {
                Line($"  跨能力：只声明 Process 调用轮盘服务仍被拒绝 ✓（{wheelCrossDetail}）");
            }
            else
            {
                Fail("能力门禁",
                    "只声明了 Process 的插件调用 Wheel.ShowWheel 竟然被放行 —— 轮盘上点的每个扇区" +
                    $"执行的都是用户配置的动作，「会起进程」不等于「可以呼轮盘」。{wheelCrossDetail}");
            }

            var processOnlyRemapService = new PluginKeyboardRemapService(gateProbePluginId, PluginCapability.Process);

            (bool remapCrossDenied, string remapCrossDetail) =
                ProbeRemapCapabilityGate(() => processOnlyRemapService.Activate(new KeyboardRemapOptions()), PluginCapability.InputRemapping);

            if (remapCrossDenied)
            {
                Line($"  跨能力：只声明 Process 调用键盘重映射服务仍被拒绝 ✓（{remapCrossDetail}）");
            }
            else
            {
                Fail("能力门禁",
                    $"只声明了 Process 的插件调用 KeyboardRemap.Activate 竟然被放行。{remapCrossDetail}");
            }

            // ③c 窗口服务声明了对应能力：同样必须放行。
            var allowedWindowService = new PluginWindowService(
                gateProbePluginId, PluginCapability.WindowControl);

            try
            {
                bool emptyLayoutResult = allowedWindowService.ApplyLayout("   ");

                if (emptyLayoutResult)
                {
                    Fail("能力门禁", "空布局码竟然报告应用成功 —— 空值短路失效，用户会以为窗口被排过了");
                }
                else
                {
                    Line("  已声明 WindowControl：放行 ✓（空布局码由空值短路拦下，未真的动窗口）");
                }
            }
            catch (PluginCapabilityDeniedException denied)
            {
                Fail("能力门禁",
                    $"已声明 WindowControl 却被拒绝（{denied.Capability}）—— 门禁判据写错了，正常插件会全部废掉");
            }
            catch (Exception gateError)
            {
                Fail("能力门禁", $"已声明 WindowControl 的调用抛出异常：{gateError}");
            }

            // 反过来：系统服务声明了 InputSimulation 就必须放行，否则「门禁写成永远拒绝」也能过上面两条。
            var allowedSystemService = new PluginSystemService(
                gateProbePluginId, PluginCapability.InputSimulation);

            try
            {
                bool emptyPresetResult = allowedSystemService.RunPreset("   ");

                if (emptyPresetResult)
                {
                    Fail("能力门禁",
                        "空预设键竟然报告「已匹配到预设」—— 空值短路失效，用户会以为系统功能执行过了");
                }
                else
                {
                    Line("  已声明 InputSimulation：放行 ✓（空键由空值短路拦下，未起进程、未按键）");
                }
            }
            catch (PluginCapabilityDeniedException denied)
            {
                Fail("能力门禁",
                    $"已声明 InputSimulation 却被拒绝（{denied.Capability}）—— 门禁判据写错了，正常插件会全部废掉");
            }
            catch (Exception gateError)
            {
                Fail("能力门禁", $"已声明 InputSimulation 的调用抛出异常：{gateError}");
            }

            // 轮盘服务声明了 Wheel 就必须放行。放行后的两道短路让这一半<b>零副作用</b>：
            // ShowWheel 走 NaN 坐标（参数校验必拦，任何运行模式下都不可能成盘），
            // DismissWheel 在没有会话时是无害空操作 —— 两条都只该拿到 false，拿到 true 才是新闻。
            var allowedWheelService = new PluginWheelService(gateProbePluginId, PluginCapability.Wheel);

            try
            {
                bool nanShowResult = allowedWheelService.ShowWheel(double.NaN, double.NaN);
                bool emptyDismissResult = allowedWheelService.DismissWheel();

                if (nanShowResult || emptyDismissResult)
                {
                    Fail("能力门禁",
                        $"已声明 Wheel 的探针调用竟然被受理（Show(NaN)={nanShowResult}, Dismiss={emptyDismissResult}）—— " +
                        "参数校验或空会话短路失效，自检环境里会弹出全屏遮罩");
                }
                else
                {
                    Line("  已声明 Wheel：放行 ✓（NaN 坐标与无会话空操作，未呼出任何轮盘）");
                }
            }
            catch (PluginCapabilityDeniedException denied)
            {
                Fail("能力门禁",
                    $"已声明 Wheel 却被拒绝（{denied.Capability}）—— 门禁判据写错了，正常插件会全部废掉");
            }
            catch (Exception gateError)
            {
                Fail("能力门禁", $"已声明 Wheel 的调用抛出异常：{gateError}");
            }

            var allowedRemapService = new PluginKeyboardRemapService(
                gateProbePluginId, PluginCapability.InputRemapping);

            try
            {
                KeyboardRemapStatus status = allowedRemapService.GetStatus();
                KeyboardRemapResult deactResult = allowedRemapService.Deactivate();

                if (!deactResult.Success)
                {
                    Fail("能力门禁", "已声明 InputRemapping 时 Deactivate 空会话失败");
                }
                else
                {
                    Line("  已声明 InputRemapping：放行 ✓（无活动会话时安全撤销，未改写按键）");
                }
            }
            catch (PluginCapabilityDeniedException denied)
            {
                Fail("能力门禁", $"已声明 InputRemapping 却被拒绝（{denied.Capability}）—— 门禁判据写错了");
            }
            catch (Exception gateError)
            {
                Fail("能力门禁", $"已声明 InputRemapping 的调用抛出异常：{gateError}");
            }

            // ④ 能力位本身的形状：两两不重复。
            //
            // 「取新位不插中间」是约定，但真正会咬人的是**位值撞车**（复制上一行忘了改 << n），
            // 那会让两个能力在 `& required` 下互相代理：勾了 A 就自动获得 B。
            // 枚举值允许有空洞（无副作用），不允许有重复。
            var capabilityBits = new List<PluginCapability>();
            bool capabilityBitsUnique = true;

            foreach (PluginCapability capability in Enum.GetValues<PluginCapability>())
            {
                if (capability == PluginCapability.None) continue;

                if (capabilityBits.Contains(capability))
                {
                    capabilityBitsUnique = false;
                    Fail("能力位", $"PluginCapability 里有两个成员取到了同一个位值「{capability}」—— " +
                        "复制上一行忘了改位移时就是这个现象：声明其中一个会连带获得另一个");
                    continue;
                }

                capabilityBits.Add(capability);
            }

            Line(capabilityBitsUnique
                ? $"  能力位：{capabilityBits.Count} 项，位值两两不重复 ✓"
                : $"  能力位：{capabilityBits.Count} 项，存在重复位值（见上面的 [FAIL]）");

            // ⑤ 每一个能力位都必须在安装确认页上有一句人话。
            //
            // 这条护栏是被两处真实遗漏逼出来的：WindowControl 与 ScreenCapture
            // 各自独立成项的理由，写的都是「安装确认页上必须让用户看见后果」——
            // 而确认页那份清单当初是内联在 SettingsWindow 里的一个 if 串，
            // 没人记得回去补，于是这两项能力至今没在用户眼前出现过一行字。
            //
            // 能力位的全部意义就是「让用户在安装前看见后果」。一个查不到文案的能力位，
            // 既骗用户（什么都没说）也骗审核者（以为已经说过了）。所以这里逐个成员核对，
            // 而不是抽查几个常见的 —— 漏掉的恰好总是新加的那一个。
            string[] labeledCapabilities = PluginCapabilityLabels.All
                .Select(entry => entry.Capability.ToString())
                .ToArray();

            bool capabilityLabelsComplete = true;

            foreach (PluginCapability capability in Enum.GetValues<PluginCapability>())
            {
                if (capability == PluginCapability.None) continue;

                if (!labeledCapabilities.Contains(capability.ToString(), StringComparer.Ordinal))
                {
                    capabilityLabelsComplete = false;
                    Fail("能力文案",
                        $"能力位「{capability}」在安装确认页上没有对应文案（PluginCapabilityLabels.All）—— " +
                        "用户勾选确认时看不到这项能力的后果，而这个能力位存在的全部理由就是要让他看见");
                }
            }

            foreach ((PluginCapability capability, string key) in PluginCapabilityLabels.All)
            {
                // 本表已改为「存键、运行时现取」（见 PluginCapabilityLabels 的类注释：
                // 存文案的话，切语言后确认页还是旧语言），所以这里检查的是<b>解析后</b>的文本。
                // 键名写错时 I18n.T 会把键名原样返回 —— 界面上于是出现一行裸键名：
                // 既不空白、也不像错的，是最容易被放过的一种失效，所以要专门判一次。
                // 跨四种语言的完整性由 scratch/check_i18n.py 静态核对（缺语言分支 / 空值）。
                string label = I18n.T(key);
                if (string.IsNullOrWhiteSpace(label) || string.Equals(label, key, StringComparison.Ordinal))
                {
                    capabilityLabelsComplete = false;
                    Fail("能力文案", $"能力位「{capability}」的确认页文案取不到（键 {key}）—— " +
                        "确认框里会出现一行空白，或一行谁都看不懂的裸键名");
                }

                // 反向核对：表里挂着一个枚举里已经没有的位，通常意味着能力位被改名后这里没跟上。
                if (!Enum.IsDefined(capability))
                {
                    capabilityLabelsComplete = false;
                    Fail("能力文案", $"确认页文案表里的「{capability}」已不是 PluginCapability 的成员 —— 能力位改过名？");
                }
            }

            // 上面两条核对里任何一条响了，这里就<b>不能</b>再印一个勾 ——
            // 一份在 [FAIL] 旁边说「覆盖全部 ✓」的报告，比不打印还糟：
            // 它会让人以为那行 [FAIL] 是误报。
            Line(capabilityLabelsComplete
                ? $"  能力文案：{PluginCapabilityLabels.All.Length} 项，覆盖枚举里全部非空能力位 ✓"
                : $"  能力文案：{PluginCapabilityLabels.All.Length} 项，覆盖不完整（见上面的 [FAIL]）");

            // 两组服务面逐一验完之后，报一次确认页的实际渲染结果。
            // 这一段是给「自检通过但用户看不到」这种情况准备的：断言只看表，这里看拼出来的文本。
            string sampleLabels = PluginCapabilityLabels.Describe(
                PluginCapability.Process | PluginCapability.InputSimulation | PluginCapability.ScreenCapture);

            Line($"  确认页示例（进程+模拟输入+截屏）：{sampleLabels.Replace("\n", "｜")}");

            // ⑥ 元数据不受门禁约束，这是刻意的。
            //
            // 插件的 Parameters 是属性，声明期（注册前）就要读这几份清单。
            // 在那里抛异常，一个「忘了声明能力」的插件会在注册阶段整个崩掉 ——
            // 而它其实只是不能在运行时干活而已。门禁拦的是**产生后果**的调用。
            try
            {
                IReadOnlyList<CommandTerminalOption> terminals = deniedCommandService.Terminals;
                IReadOnlyList<ShellVerbOption> shellVerbs = deniedShellService.Verbs;
                IReadOnlyList<SystemPresetOption> presets = deniedSystemService.Presets;
                IReadOnlyList<WindowLayoutOption> layouts = deniedWindowService.Layouts;

                if (terminals.Count == 0)
                {
                    Fail("宿主服务面", "终端清单为空 —— 「运行命令」动作的终端下拉会是空的，用户选不了终端");
                }
                else if (!terminals.Any(t => string.Equals(t.Id, "cmd", StringComparison.OrdinalIgnoreCase)))
                {
                    Fail("宿主服务面", "终端清单里没有 \"cmd\" —— 动作的默认值在界面上选不中任何一项");
                }
                else if (terminals.Any(t => string.IsNullOrWhiteSpace(t.DisplayName)))
                {
                    Fail("宿主服务面", "终端清单里有显示名为空的项 —— 下拉里会出现一个没有文字的选项");
                }
                else if (terminals.Select(t => t.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != terminals.Count)
                {
                    Fail("宿主服务面", "终端清单里有重复的标识 —— 下拉选中项会错位到另一个终端上");
                }
                else
                {
                    Line($"  终端清单：{terminals.Count} 项，含 cmd ✓（未声明能力也能读，因为它不产生后果）");
                }

                // Shell 动词：用户配置里存的是短 ID（copy_path），不是 Verb（Windows.CopyAsPath）。
                // 清单漏项不会有任何报错 —— 只会让那个动作在挑选器里找不到对应项。
                if (shellVerbs.Count == 0)
                {
                    Fail("宿主服务面", "Shell 动词清单为空 —— 该动作的下拉会是空的");
                }
                else if (!shellVerbs.Any(v => string.Equals(v.Id, "copy_path", StringComparison.OrdinalIgnoreCase)))
                {
                    Fail("宿主服务面",
                        "Shell 动词清单里没有 \"copy_path\" —— 用户配置里存的就是这个短 ID，" +
                        "少了它老配置在挑选器里找不到对应项（注意：清单要的是 Id，不是 Verb）");
                }
                else if (shellVerbs.Select(v => v.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != shellVerbs.Count)
                {
                    Fail("宿主服务面", "Shell 动词清单里有重复的标识 —— 选中项会错位");
                }
                else
                {
                    Line($"  Shell 动词清单：{shellVerbs.Count} 项，含 copy_path ✓");
                }

                // 窗口布局清单：它是「平铺窗口」动作生成下拉的<b>唯一来源</b>，
                // 所以必须与宿主执行体那份表逐项同源（SequenceEqual 连顺序都比 ——
                // 顺序即下拉顺序）。插件另抄一份的后果是宿主加布局之后，
                // 「新布局在下拉里选不到」或「选了不生效」，两种都是静默失效。
                if (layouts.Any(l => string.IsNullOrWhiteSpace(l.Key) || string.IsNullOrWhiteSpace(l.DisplayName)))
                {
                    Fail("宿主服务面", "窗口布局清单里有空键或空显示名 —— 下拉里会出现一个没有文字的选项");
                }
                else if (!layouts.Select(l => l.Key).SequenceEqual(WindowTiler.LayoutKeys, StringComparer.OrdinalIgnoreCase))
                {
                    Fail("宿主服务面",
                        $"窗口布局清单（{layouts.Count} 项）与 WindowTiler.LayoutKeys（{WindowTiler.LayoutKeys.Count} 项）" +
                        "不一致 —— 两者已经漂了，用户会遇到「新布局选不到」或「选了不生效」");
                }
                else if (!string.Equals(deniedWindowService.CycleToken, WindowTiler.CycleParam, StringComparison.Ordinal)
                    || !string.Equals(deniedWindowService.CycleBackToken, WindowTiler.CycleBackParam, StringComparison.Ordinal)
                    || !string.Equals(deniedWindowService.RestoreToken, WindowTiler.RestoreParam, StringComparison.Ordinal))
                {
                    Fail("宿主服务面",
                        "三个布局标记与 WindowTiler 的常量对不上 —— " +
                        "「循环切换 / 循环返回 / 还原」选下去会静默无效（执行体的 switch 认的是宿主那两个常量）");
                }
                else if (deniedWindowService.OpacityMinPercent >= deniedWindowService.OpacityMaxPercent)
                {
                    Fail("宿主服务面",
                        "透明度范围不合法（下界不小于上界）—— 插件据它生成的参数声明会把所有值都判成非法");
                }
                else
                {
                    Line($"  窗口布局清单：{layouts.Count} 项，与 WindowTiler.LayoutKeys 逐项同源 ✓" +
                        $"（透明度 {deniedWindowService.OpacityMinPercent}~{deniedWindowService.OpacityMaxPercent}）");
                }

                // 系统预设清单：与终端 / 动词同理，也必须与宿主那份表同源。
                // 旧版只比了「项数相等」，那挡不住「项数对得上但名字串位」——
                // 插件下拉里会出现「关机」写成「睡眠」这种，用户选下去才发现不对。
                if (presets.Count != SlotViewModel.SystemPresetList.Count)
                {
                    Fail("宿主服务面",
                        $"未声明能力的插件读到的预设清单是 {presets.Count} 项，宿主表是 " +
                        $"{SlotViewModel.SystemPresetList.Count} 项 —— 两者必须是同一份数据");
                }
                else
                {
                    bool presetsAligned = true;

                    for (int i = 0; i < presets.Count; i++)
                    {
                        SystemPresetItem hostItem = SlotViewModel.SystemPresetList[i];

                        if (!string.Equals(presets[i].Key, hostItem.Key, StringComparison.Ordinal)
                            || !string.Equals(presets[i].DisplayName, hostItem.FormattedDisplay, StringComparison.Ordinal))
                        {
                            presetsAligned = false;
                            Fail("宿主服务面",
                                $"系统预设清单第 {i + 1} 项与宿主表不一致：" +
                                $"插件侧（{presets[i].Key} / {presets[i].DisplayName}），" +
                                $"宿主侧（{hostItem.Key} / {hostItem.FormattedDisplay}）—— " +
                                "下标串位会让用户在插件下拉里选中一个不是他要的预设");
                            break;
                        }
                    }

                    Line(presetsAligned
                        ? $"  系统预设清单：{presets.Count} 项，与宿主表逐项同源 ✓"
                        : $"  系统预设清单：{presets.Count} 项，与宿主表不一致（见上面的 [FAIL]）");
                }

                KeyboardRemapStatus ungateStatus = deniedRemapService.GetStatus();
                Line($"  键盘映射状态：IsActive={ungateStatus.IsActive} ✓（未声明能力也能读，属于无副作用元数据）");
            }
            catch (PluginCapabilityDeniedException deniedMeta)
            {
                Fail("宿主服务面",
                    $"读元数据（终端 / 动词 / 布局 / 预设清单）被能力门禁拦下了（{deniedMeta.ServiceName}）—— " +
                    "插件的 Parameters 是声明期就要读它的，这会让忘了声明的插件在注册阶段整个崩掉");
            }

            // ⑦ SDK 契约版本号的内部一致性。
            //
            // ApiVersion 是个手写常量：C# 的常量插值只对 string 常量成立，
            // 这两个组成部分是 int，所以拼不出来（CS0133）。这处重复只能靠断言守。
            // 漏改的表现极其隐蔽：插件按 ApiVersion 做兼容判断，而它和真实版本号对不上。
            string expectedApiVersion = $"{PluginApi.ApiVersionMajor}.{PluginApi.ApiVersionMinor}";

            if (!string.Equals(PluginApi.ApiVersion, expectedApiVersion, StringComparison.Ordinal))
            {
                Fail("SDK 契约",
                    $"ApiVersion（{PluginApi.ApiVersion}）与主次版本号（{expectedApiVersion}）不一致 —— " +
                    "两者手写在两处，改了其中一个却忘了另一个");
            }
            else
            {
                Line($"  SDK 契约版本：{PluginApi.ApiVersion} ✓（与主次版本号一致）");
            }

            // ⑧ 键盘空间映射编解码器与校验器
            IReadOnlyList<KeyboardRemapEntry> spatialPreset = KeyMapCodec.GetDefaultSpatialPreset();
            if (spatialPreset.Count != 10)
            {
                Fail("键盘映射预设", $"默认空间预设应包含严格 10 组映射，实际为 {spatialPreset.Count} 组");
            }
            string encoded = KeyMapCodec.Encode(spatialPreset);
            if (!encoded.StartsWith("v1|", StringComparison.Ordinal))
            {
                Fail("键盘映射协议版本", $"版本化 KeyMap 编码应以 \"v1|\" 开头，实际为: {encoded}");
            }
            if (!KeyMapCodec.TryDecode(encoded, out var decoded, out string? decodeErr) || decoded == null || decoded.Count != 10)
            {
                Fail("键盘映射编解码", $"KeyMap 解码失败: {decodeErr}");
            }
            if (!string.Equals(encoded, KeyMapCodec.Encode(decoded), StringComparison.Ordinal))
            {
                Fail("键盘映射编解码", "KeyMap 序列化与反序列化往返不一致");
            }
            // 严格版本检查：缺少 "v1|" 或非法版本前缀必须被拦截
            if (KeyMapCodec.TryDecode("Q:Num7,W:Num8", out _, out _) || KeyMapValidator.Validate("Q:Num7,W:Num8") == null)
            {
                Fail("键盘映射协议版本", "缺少版本前缀 \"v1|\" 的未版本化字符串未被拒绝");
            }
            if (KeyMapCodec.TryDecode("v2|Q:Num7", out _, out _) || KeyMapValidator.Validate("v2|Q:Num7") == null)
            {
                Fail("键盘映射协议版本", "非当前版本协议前缀未被拒绝");
            }
            string? presetValidError = KeyMapValidator.Validate(spatialPreset);
            if (presetValidError != null)
            {
                Fail("键盘映射校验", $"默认空间预设校验未通过: {presetValidError}");
            }
            if (KeyMapValidator.Validate(new[] { new KeyboardRemapEntry { FromKey = "Q", ToKey = "Num7" }, new KeyboardRemapEntry { FromKey = "Q", ToKey = "Num8" } }) == null)
            {
                Fail("键盘映射校验", "重复源键未被拒绝");
            }
            if (KeyMapValidator.Validate(new[] { new KeyboardRemapEntry { FromKey = "Control", ToKey = "Num1" } }) == null)
            {
                Fail("键盘映射校验", "修饰键作为源键未被拒绝");
            }
            if (KeyMapValidator.Validate(new[] { new KeyboardRemapEntry { FromKey = "Escape", ToKey = "Num1" } }) == null)
            {
                Fail("键盘映射校验", "Escape 作为源键未被拒绝");
            }
            if (KeyMapValidator.Validate("v1|invalid_no_colon") == null)
            {
                Fail("键盘映射严格校验", "缺少分隔符的非法字符串未被拒绝");
            }
            if (KeyMapValidator.Validate("v1|Q:UnknownKey123") == null)
            {
                Fail("键盘映射严格校验", "包含未知键名的非法字符串未被拒绝");
            }
            if (KeyMapValidator.Validate("") != null || KeyMapValidator.Validate("   ") != null)
            {
                Fail("键盘映射严格校验", "合法空配置串应通过校验");
            }

            // 验证硬件扫描码注入结构体内存形状与标志位规范
            var inputDown = KeyboardRemapController.FormatKeyboardInput(0x47, 0, false);
            if (inputDown.type != KeyboardRemapController.INPUT_KEYBOARD ||
                inputDown.U.ki.wVk != 0 ||
                inputDown.U.ki.wScan != 0x47 ||
                (inputDown.U.ki.dwFlags & KeyboardRemapController.KEYEVENTF_SCANCODE) == 0 ||
                (inputDown.U.ki.dwFlags & KeyboardRemapController.KEYEVENTF_KEYUP) != 0 ||
                inputDown.U.ki.dwExtraInfo != KeyboardHook.StarPieExtraInfo)
            {
                Fail("扫描码输入结构", "FormatKeyboardInput 构造的硬件扫描码 KeyDown 结构体不符合规范（wVk!=0 或缺少 SCANCODE/StarPieExtraInfo 标志）");
            }
            var inputUp = KeyboardRemapController.FormatKeyboardInput(0x47, 0, true);
            if ((inputUp.U.ki.dwFlags & KeyboardRemapController.KEYEVENTF_KEYUP) == 0)
            {
                Fail("扫描码输入结构", "FormatKeyboardInput 构造的 KeyUp 结构体缺少 KEYEVENTF_KEYUP 标志");
            }

            Line("  键盘映射编解码、严格校验与硬件扫描码结构：空间预设10组、往返一致、格式拦截、INPUT形状断言 ✓");

            // ⑨ 10,000 次真实生产控制器状态转换自检（KeyDown/KeyUp 配对率 100%、失焦原子释放与首键放行）
            RunRemapStateMachineSimulation(Line, Fail);

            // ⑩ 回归自检：进程名规范化与 PID 安全门禁、编辑器画刷安全契约、停用插件动作保留与不可用提示
            RunRegressionChecks(Line, Fail);

            // ---- 7 环境还原性检查 ----
            Line("");
            Line("[7] 环境还原性检查");
            Line($"  残留登记插件：{PluginHost.InstalledCount} 个");
            Line($"  残留插件词条：{I18n.ExternalTranslationCount} 条");
            if (I18n.ExternalTranslationCount != 0)
            {
                Fail("词条清理", "卸载后仍有插件词条残留（会造成语言切换时显示脏数据）");
            }
        }
        catch (Exception ex)
        {
            Fail("未捕获异常", ex.ToString());
        }
        finally
        {
            // 自检失败时不要把用户的插件目录弄脏
            if (installedPluginId != null)
            {
                try
                {
                    _ = PluginHost.UninstallForSelfTestAsync(installedPluginId, removePluginData: true).GetAwaiter().GetResult();
                }
                catch
                {
                }
            }

            // 删掉整个沙箱。删不掉要如实说 —— 静默吞掉的话，临时目录会一次次堆出残留，
            // 而下次排查「磁盘怎么满了」时没人会想到是自检干的。
            try
            {
                Directory.Delete(sandboxRoot, recursive: true);
            }
            catch (Exception cleanupError)
            {
                Line($"  [WARN] 沙箱未能删除（{cleanupError.Message}）：{sandboxRoot}");
            }
        }

        Line("");
        Line("====================================================");
        Line(pass ? "自检结论：PASS —— 全链路可用" : "自检结论：FAIL —— 见上面 [FAIL] 项");
        Line("====================================================");

        return Write(report, reportPath, pass);
    }

    /// <summary>
    /// 阶段 3（启用）+ 阶段 4（调用）。
    /// <para>
    /// <b>必须独立成方法并禁止内联</b>：本方法持有 <see cref="PluginActionRegistration"/>，
    /// 它间接指向插件程序集里的类型实例。只有让这些引用随本方法的栈帧一起消失，
    /// 后续「停用 → ALC 卸载」的判定才可能为真。
    /// </para>
    /// </summary>
    /// <returns>失败原因；<c>null</c> 表示两个阶段都通过。</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string? RunEnableAndInvoke(
        string pluginId,
        Action<string> line,
        out ActionItem? lazyLoadProbe,
        bool skipInvoke = false)
    {
        lazyLoadProbe = null;
        // ---- 3 启用 ----
        line("");
        line("[3] 启用（加载 → 实例化 → Initialize → 提交贡献点）");
        var sw = Stopwatch.StartNew();
        bool enabled = PluginHost.Enable(pluginId, out string enableError);
        sw.Stop();
        line($"  启用结果：{(enabled ? "成功" : "失败")}");
        line($"  启用耗时：{sw.Elapsed.TotalMilliseconds:F1} ms");
        if (!enabled) return $"启用失败：{enableError}";

        PluginInstance? instance = PluginHost.Find(pluginId);
        line($"  加载耗时（内部计量）：{instance?.LastLoadMs:F1} ms");
        line($"  运行时状态：{instance?.State}");

        List<PluginActionRegistration> actions = PluginHost.Catalog.SnapshotActions()
            .Where(action => string.Equals(action.PluginId, pluginId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        line($"  已注册动作：{actions.Count} 个");
        foreach (PluginActionRegistration action in actions)
        {
            line($"    · {action.FullId} | {action.DisplayName} | {action.Kind} | 参数 {action.Parameters.Count} 项");
        }
        if (actions.Count == 0) return "插件启用成功但一个动作都没注册";

        // 为重启后的首次惰性调用准备一条无副作用负例：移除声明为必填的参数，
        // 让执行管线在加载并解析贡献后停在参数校验，不会真正调用插件动作。
        PluginActionRegistration? lazyCandidate = actions.FirstOrDefault(action =>
            action.Parameters.Any(field => field.Required && field.Type != ParameterFieldType.Bool));
        if (lazyCandidate != null)
        {
            ActionItem? candidateItem = PluginHost.CreateActionItem(
                lazyCandidate.FullId,
                CollectDefaults(lazyCandidate.Parameters));
            ParameterField requiredField = lazyCandidate.Parameters.First(
                field => field.Required && field.Type != ParameterFieldType.Bool);
            candidateItem?.ExtensionData?.Remove(requiredField.Key);
            lazyLoadProbe = candidateItem;
        }

        // 词条命中率单独成段。显示名有字面文案兜底，所以「词条没接上」在界面上
        // 与「接上了」长得一模一样 —— 必须在这里显式暴露，否则插件作者要等到
        // 用户切换语言、发现名字没变，才会意识到自己的 key 一直没生效。
        int keyed = 0;
        int resolved = 0;
        var missed = new List<string>();

        foreach (PluginActionRegistration action in actions)
        {
            if (string.IsNullOrEmpty(action.DisplayNameKey)) continue;
            keyed++;
            if (action.DisplayNameFromI18n) resolved++;
            else missed.Add($"{action.ShortId}（{action.DisplayNameKey}）");
        }

        line("");
        line($"  词条解析：声明了 DisplayNameKey 的 {keyed} 个动作中，命中 {resolved} 个");

        // 一个 key 都没声明不算问题：字面 DisplayName 是完全合法且推荐的兜底写法。
        if (keyed > 0 && resolved < keyed)
        {
            line($"    ⚠️ 未命中：{string.Join("、", missed)}");
            line("    这些动作会退回字面 DisplayName 显示，译文不会生效。");
        }

        // ---- 3b 参数校验 ----
        //
        // 这一段验证的是「声明即校验」：插件只声明 ParameterField、一行校验代码都不写，
        // 宿主也必须能拦下空值、越界值与非法选项。
        // 之所以要在这里断言，是因为这一层「没生效」时完全没有外在症状 ——
        // 界面照常渲染、边界值照常存进配置，直到用户触发时插件自己拒绝才暴露。
        line("");
        line("[3b] 参数校验（声明驱动的约束）");

        List<PluginActionRegistration> parameterized = actions.Where(a => a.Parameters.Count > 0).ToList();

        if (parameterized.Count == 0)
        {
            line("  本插件没有声明任何参数，跳过。");
        }
        else
        {
            foreach (PluginActionRegistration candidate in parameterized)
            {
                line($"  样本动作：{candidate.ShortId}（声明 {candidate.Parameters.Count} 项）");

                foreach (ParameterField field in candidate.Parameters)
                {
                    string range = field.Min.HasValue && field.Max.HasValue
                        ? $"　范围 {FormatBound(field.Min.Value)}~{FormatBound(field.Max.Value)}"
                        : (field.Max.HasValue ? $"　上限 {FormatBound(field.Max.Value)}" : "");

                    line($"    · {field.Key}｜{field.Type}｜必填={field.Required}{range}");
                }

                bool hasRequired = candidate.Parameters.Any(
                    p => p.Required && p.Type != ParameterFieldType.Bool);

                // ① 全空输入：声明了必填就必须被拦下
                List<PluginParameterIssue> emptyIssues = PluginParameterValidator.Validate(
                    candidate.Parameters,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

                line($"    ① 全空输入 → {emptyIssues.Count} 项不通过" +
                     (emptyIssues.Count > 0 ? $"（{emptyIssues[0]}）" : ""));

                if (hasRequired && emptyIssues.Count == 0)
                {
                    return $"「{candidate.ShortId}」声明了必填参数，但全空输入未被拦下 —— 空值会直接存进配置。";
                }

                // ①b 同一份全空输入再走一遍「统一校验入口」（声明约束 + 插件自定义 Validate）。
                // 这一条判失败，不像 ④ 那样只警告。
                //
                // 存在理由是「一个参数都没填」才是插件真实面对的第一种状态，而 ②③④ 喂的全是
                // 按默认值填满的那一份 —— 于是留下「自检一路绿、用户装上点哪都失败」的空档。
                // 真机踩过：FloatingBall 的声明里没有必填项（① 因此放行），而插件自己的 Validate
                // 把缺键读成 0 再判成越界，用户 7 次触发全部失败。
                //
                // 敢判失败，是因为空输入上没有 ④ 那种「规则与默认值互斥」的正当借口：
                // 声明层放行、插件层拒绝只有两种解释 —— 该字段本就该标 Required（声明写漏），
                // 或插件把「未填」当成了非法值（读值写错）。两种都是作者当场改得掉的问题。
                //
                // 这里刻意不用 PluginHost.CreateActionItem 造探针：它会用声明的默认值把参数表填满
                // （见其「用参数默认值填充」一段），空输入根本传不进去 —— ④ 看不见这类缺陷的根因
                // 就在这。手搓 ActionItem 才等价于「用户把一个没填过参数的扇区切成了插件动作」。
                if (emptyIssues.Count == 0)
                {
                    ActionItem emptyProbe = new()
                    {
                        Type = PluginApi.ActionTypeName,
                        Name = candidate.DisplayName,
                        PluginActionRef = new PluginActionRef
                        {
                            PluginId = candidate.PluginId,
                            ContributionId = candidate.ShortId,
                        },
                        ExtensionData = new Dictionary<string, string>(),
                    };

                    PluginActionValidation emptyUnified =
                        PluginHost.ValidateActionParameters(emptyProbe);

                    line("    ①b 同一份全空输入走统一入口 → " +
                         (emptyUnified.IsValid ? "通过" : "不通过"));

                    if (!emptyUnified.IsValid)
                    {
                        return $"「{candidate.ShortId}」的声明校验放行全空输入，插件自己的校验却拒绝" +
                               $"（{emptyUnified.Describe()}）—— 用户装上插件后一个参数都不填就会点哪都失败。" +
                               "要么该字段应声明 Required，要么插件要把「未填」和「填了非法值」分开判。";
                    }
                }
                else
                {
                    line("    ①b 跳过：声明层已经拦下全空输入，插件层是否也拒绝不再影响用户。");
                }

                // ②③④ 都需要一份「除被测字段外其余都合法」的基线。
                //
                // 只有当插件为每个字段都声明了 DefaultValue 时这份基线才存在。
                // 否则我们只能自己编一个值（比如给热键字段填 "x"），而那个值可能
                // 恰好过不了插件自己的 ValidationRegex —— 于是断言会因为「别的字段」而
                // 通过或失败，测试自己制造出假阳性与假阴性。自检工具宁可少测一种情形，
                // 也不能给出不可信的结论。
                bool baselineAvailable = candidate.Parameters.All(
                    p => string.IsNullOrEmpty(p.Key) || p.DefaultValue != null);

                if (!baselineAvailable)
                {
                    line("    ②～④ 跳过：本动作有字段未声明 DefaultValue，无法构造可信的基线输入。");
                    continue;
                }

                Dictionary<string, string> baseline = CollectDefaults(candidate.Parameters);

                // ② 越界输入：把带上限的数值字段设成 上限+1。
                // 断言的是「该字段名下确实出现了错误」，而不是「错误总数 > 0」——
                // 后者可能来自另一个字段，让这条断言在错误的原因下通过。
                ParameterField? ranged = candidate.Parameters.FirstOrDefault(
                    p => p.Type == ParameterFieldType.Number && p.Max.HasValue);

                if (ranged != null)
                {
                    var overflow = new Dictionary<string, string>(baseline, StringComparer.OrdinalIgnoreCase);
                    string tooBig = FormatBound(ranged.Max!.Value + 1);
                    overflow[ranged.Key] = tooBig;

                    List<PluginParameterIssue> overflowIssues =
                        PluginParameterValidator.Validate(candidate.Parameters, overflow);

                    bool attributed = overflowIssues.Any(
                        i => string.Equals(i.Key, ranged.Key, StringComparison.OrdinalIgnoreCase));

                    line($"    ② {ranged.Key}={tooBig}（上限 {FormatBound(ranged.Max.Value)}）→ " +
                         (attributed ? "已拦下" : "未拦下"));

                    if (!attributed)
                    {
                        return $"「{candidate.ShortId}」的 {ranged.Key} 超过声明上限却未被拦下。";
                    }
                }

                // ③ 正向用例：按声明的默认值填充，必须全部通过。
                // 缺了这条，任何「一律报错」的实现都能骗过上面两条断言。
                List<PluginParameterIssue> validIssues =
                    PluginParameterValidator.Validate(candidate.Parameters, baseline);

                line($"    ③ 按声明默认值填充 → {validIssues.Count} 项不通过" +
                     (validIssues.Count > 0 ? $"（{validIssues[0]}）" : ""));

                if (validIssues.Count > 0)
                {
                    return $"「{candidate.ShortId}」合法的默认值被判为不合法，会拦住本可正常使用的配置。";
                }

                // ④ 两层校验（宿主声明约束 + 插件自定义）必须对同一份输入给出一致结论。
                // 结论相反时用户会遇到最难自查的一种状态：表单全绿，一触发却被拒。
                //
                // 这里只警告、不判失败：有些插件的规则本身就与默认值互斥
                // （例如「起止时间不能相同」而两者默认值恰好相同），那是声明的写法问题，
                // 不该被自检判成宿主缺陷。
                ActionItem? probeItem = PluginHost.CreateActionItem(candidate.FullId, baseline);
                if (probeItem != null)
                {
                    PluginActionValidation unified =
                        PluginHost.ValidateActionParameters(probeItem);

                    line($"    ④ 走统一入口校验同一份输入 → {(unified.IsValid ? "通过" : "不通过")}");

                    if (!unified.IsValid)
                    {
                        line($"       ⚠️ {unified.Describe()}");
                        line("          声明约束与插件自定义校验结论相反。若两者规则本身互斥（如默认值不满足自定规则），");
                        line("          属声明写法问题；否则说明有一层漏判。此项不判失败，请作者自行确认。");
                    }
                }
            }
        }

        // ---- 3c 选择器接缝 ----
        line("");
        line("[3c] 动作选择器接缝（类型收敛 + 按插件分组的子下拉）");

        // 类型下拉里的插件项只能有一项。
        // 若像早先那样把每个插件动作都平铺进去，装十个插件就会多出上百项，
        // 把内置动作挤到看不见的地方 —— 而内置项的顺序属于用户的肌肉记忆。
        List<ActionTypeItem> typeItems = PluginActionBinding.BuildActionTypeItems();
        line($"    类型下拉里的插件项：{typeItems.Count} 项（应为 1 项）");
        if (typeItems.Count != 1)
        {
            return $"类型下拉里的插件项应为 1 项，实际 {typeItems.Count} 项 —— 装一个插件就多一项会把内置动作挤走。";
        }
        if (!string.Equals(typeItems[0].Tag, PluginApi.ActionTypeName, StringComparison.Ordinal))
        {
            return $"类型下拉的插件项 Tag 应为 {PluginApi.ActionTypeName}，实际是「{typeItems[0].Tag}」。";
        }

        // 子下拉只展示社区插件动作；被顶层 Type 认领的官方动作必须从这里排除，
        // 否则同一个功能会同时拥有两种互不兼容的持久化形态。
        HashSet<string> claimedFullIds = PluginActionClaimRegistry.Snapshot()
            .Where(binding => string.Equals(binding.PluginId, pluginId, StringComparison.OrdinalIgnoreCase))
            .Select(binding => binding.FullId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<PluginActionRegistration> registered = PluginHost.GetRegisteredActions()
            .Where(action => string.Equals(action.PluginId, pluginId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        List<PluginActionItem> options = PluginActionBinding.BuildPluginActionItems()
            .Where(option => string.Equals(option.PluginId, pluginId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        List<PluginActionRegistration> expectedVisible = actions
            .Where(action => !claimedFullIds.Contains(action.FullId))
            .ToList();

        line($"    子下拉候选：{options.Count} 项 / 应显示动作 {expectedVisible.Count} 项 / 认领隐藏 {claimedFullIds.Count} 项");
        if (registered.Count != expectedVisible.Count || options.Count != expectedVisible.Count)
        {
            return "普通插件子下拉没有精确排除认领动作，界面会出现重复入口或漏掉社区动作。";
        }
        foreach (PluginActionRegistration expected in expectedVisible)
        {
            if (!registered.Any(action => string.Equals(action.FullId, expected.FullId, StringComparison.OrdinalIgnoreCase)) ||
                !options.Any(option => string.Equals(option.FullId, expected.FullId, StringComparison.OrdinalIgnoreCase)))
            {
                return $"普通插件动作 {expected.FullId} 未出现在子下拉候选中。";
            }
        }
        foreach (string claimedFullId in claimedFullIds)
        {
            if (!actions.Any(action => string.Equals(action.FullId, claimedFullId, StringComparison.OrdinalIgnoreCase)))
            {
                return $"类型认领指向的贡献点 {claimedFullId} 没有真实注册。";
            }
            if (options.Any(option => string.Equals(option.FullId, claimedFullId, StringComparison.OrdinalIgnoreCase)))
            {
                return $"认领动作 {claimedFullId} 仍出现在普通插件子下拉中。";
            }
        }

        var groupOfPlugin = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (PluginActionRegistration registration in registered)
        {
            PluginActionItem? option = options.FirstOrDefault(o => o.FullId == registration.FullId);
            if (option == null)
            {
                return $"已注册动作 {registration.FullId} 未出现在子下拉候选里。";
            }
            if (string.IsNullOrWhiteSpace(option.GroupName))
            {
                return $"动作 {registration.FullId} 没有分组名 —— 它在下拉里会成为没有归属的孤儿项。";
            }

            // 同一插件的动作必须归入同一分组。若按动作名去分组，
            // 一个插件的各个动作会各自成组，界面立刻变成一锅粥。
            if (groupOfPlugin.TryGetValue(registration.PluginId, out string? existing))
            {
                if (!string.Equals(existing, option.GroupName, StringComparison.Ordinal))
                {
                    return $"同一插件（{registration.PluginId}）的动作被分到了不同分组：" +
                           $"「{existing}」与「{option.GroupName}」。";
                }
            }
            else
            {
                groupOfPlugin[registration.PluginId] = option.GroupName;
            }
        }

        // 插件显示名是否真的互相冲突 —— 只有冲突时，组标题才允许带上插件 ID 后缀。
        List<string> pluginIds = new List<string>(groupOfPlugin.Keys);
        var displayNames = pluginIds
            .Select(id => PluginActionBinding.ResolvePluginDisplayName(id))
            .ToList();
        bool nameCollision = displayNames.Count != displayNames.Distinct(StringComparer.Ordinal).Count();

        foreach (string ownerId in pluginIds)
        {
            string groupName = groupOfPlugin[ownerId];
            string expectedName = PluginActionBinding.ResolvePluginDisplayName(ownerId);
            int count = registered.Count(r => string.Equals(r.PluginId, ownerId, StringComparison.Ordinal));
            line($"    分组「{groupName}」→ {count} 个动作");

            if (!nameCollision)
            {
                // 插件名互不相同是常态，此时组标题必须就是插件名本身。
                // 多出任何后缀都会让用户以为装了别的什么插件 —— 而这类问题在界面上
                // 看起来完全正常，只有对着插件列表才发现对不上。
                if (!string.Equals(groupName, expectedName, StringComparison.Ordinal))
                {
                    return $"插件 {ownerId} 的分组名「{groupName}」应为「{expectedName}」—— " +
                           "插件名并不重复，不该给组标题加后缀。";
                }
                continue;
            }

            if (!groupName.StartsWith(expectedName, StringComparison.Ordinal))
            {
                return $"插件 {ownerId} 的分组名「{groupName}」与它的显示名「{expectedName}」不一致 —— " +
                       "子下拉的组标题会与详情面板里的插件标识对不上号。";
            }
        }

        // 写读往返：子下拉选中 → 落库 → 再投影回下拉，必须仍是同一个动作。
        // 这条路断了会出现最难查的一类故障：界面看着正常，触发时却是另一个动作。
        foreach (PluginActionRegistration registration in registered)
        {
            var probe = new ActionItem
            {
                Type = PluginApi.ActionTypeName,
                ExtensionData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            };

            if (!PluginActionBinding.Apply(probe, registration.FullId))
            {
                return $"写入动作 {registration.FullId} 失败。";
            }

            string? projected = PluginActionBinding.ProjectSelectedAction(probe);
            if (!string.Equals(projected, registration.FullId, StringComparison.Ordinal))
            {
                return $"动作 {registration.FullId} 写入后投影回来变成了「{projected ?? "(空)"}」—— " +
                       "界面会显示成没选动作。";
            }

            if (PluginActionBinding.IsReferenceBroken(probe))
            {
                return $"刚写入的动作 {registration.FullId} 立刻被判为「引用已失效」。";
            }

            if (!string.Equals(probe.Type, PluginApi.ActionTypeName, StringComparison.Ordinal))
            {
                return $"写入后 Type 变成了「{probe.Type}」，应为 {PluginApi.ActionTypeName} —— 类型下拉会选不中。";
            }
        }
        line($"    写读往返：{registered.Count} 个动作全部一致");

        // 切回内置类型必须清干净，否则留下「内置类型 + 悬挂插件引用 + 插件参数」的混合状态，
        // 那种配置界面上看不出来，却会在导出与执行时各表现一次。
        if (registered.Count > 0)
        {
            var cleared = new ActionItem { Type = PluginApi.ActionTypeName };
            PluginActionBinding.Apply(cleared, registered[0].FullId);
            PluginActionBinding.Clear(cleared);
            if (cleared.PluginActionRef != null || cleared.ExtensionData != null)
            {
                return "Clear 之后仍有插件引用或插件参数残留。";
            }
            line("    切回内置类型：插件引用与插件参数均已清空");
        }
        else
        {
            line("    本插件的贡献点全部由顶层 Type 认领，普通插件写读往返与清理断言跳过。");
        }

        // ---- 3f 插件管理页卡片文案 ----
        //
        // 卡片在 ListBox.ItemTemplate 里，三个按钮的文字绑在 PluginListItem 上，所以
        // 「卡片文案翻没翻」没法靠按键名取控件来查 —— 只能真的构建一次卡片再看结果。
        // Build() 因此被放在 PluginListItem 里而不是窗口类里：窗口类里的私有方法自检够不着。
        //
        // 这一段刻意放在 [4] 之前：--skip-invoke 会在 [4] 开头提前 return，
        // 放到 [4] 之后等于日常回归里根本不会执行（那正是 [5b] 曾经踩过的坑）。
        line("");
        line("[3f] 插件管理页卡片文案（状态名 / 摘要 / 三个按钮，且整卡随语言切换）");

        // ① 状态名逐个成员核对。I18n.T 取不到键时**原样返回键名** —— 既不空白也不像错的，
        //    只有逐条比对才看得见。这里直接按「值」驱动，所以 9 个成员一个都不会漏。
        //
        //    判据是「以 PluginsState 开头」这个**前缀形状**，不是「等于我期望的那个键」——
        //    因为 DescribeState 内部才认识键名，运行时拿不到。所以它抓的是**保留前缀的错写**
        //    （PluginsStateActiveTypo 这种，实测会红）。前缀整个写错（PluginStateActive）
        //    运行时看不出来，那一路由 scratch/check_i18n.py 的「引用但未定义」静态兜住 ——
        //    两者是分工关系，不是互相替代：静态管全覆盖，运行时管「取到手的到底像不像话」。
        var stateTexts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (PluginRuntimeState state in Enum.GetValues<PluginRuntimeState>())
        {
            (string glyph, string text) = PluginListItem.DescribeState(state, requiresRestart: false, entryEnabled: true);
            if (string.IsNullOrWhiteSpace(text))
            {
                return $"[3f] 状态「{state}」没有任何文案 —— 卡片上会显示一行空白。";
            }
            if (text.StartsWith("PluginsState", StringComparison.Ordinal))
            {
                return $"[3f] 状态「{state}」取到的是裸键名「{text}」—— 词条键写错了。";
            }
            if (string.IsNullOrWhiteSpace(glyph))
            {
                return $"[3f] 状态「{state}」没有图标 —— 列表里会少一列用于扫读的标记。";
            }
            stateTexts[state.ToString()] = text;
        }

        // 同一状态、两种处境必须给出不同文案：Active 是否待重启、Installed 是否已启用。
        if (string.Equals(
                PluginListItem.DescribeState(PluginRuntimeState.Active, requiresRestart: true, entryEnabled: true).Text,
                PluginListItem.DescribeState(PluginRuntimeState.Active, requiresRestart: false, entryEnabled: true).Text,
                StringComparison.Ordinal))
        {
            return "[3f] 「运行中」与「运行中 · 待重启」文案相同 —— 用户看不出重启才会生效。";
        }
        if (string.Equals(
                PluginListItem.DescribeState(PluginRuntimeState.Installed, requiresRestart: false, entryEnabled: true).Text,
                PluginListItem.DescribeState(PluginRuntimeState.Installed, requiresRestart: false, entryEnabled: false).Text,
                StringComparison.Ordinal))
        {
            return "[3f] 「已启用待加载」与「未启用」文案相同 —— 用户看不出启用了没启用。";
        }
        line($"    状态名：{stateTexts.Count} 个枚举成员全部有文案且不是裸键名 ✓（含 Active/Installed 两处处境差异）");

        // ② 整卡随语言切换。英文卡片里不许出现方块字与全角标点 ——
        //    但插件自带的数据（名称 / 描述 / 作者 / 许可证 / 安装路径）本来就可能是任何语言，
        //    不属于宿主的翻译责任，断言前先按值把它们从字符串里摘掉，剩下的才是宿主拼的部分。
        PluginInstance? cardInstance = PluginHost.Find(pluginId);
        if (cardInstance == null)
        {
            return "[3f] 插件实例不存在，无法构建卡片 —— 上一段应当已经把它启用。";
        }

        PluginRegistryEntry cardEntry = cardInstance.Entry;
        string StripPluginData(string text)
        {
            // 必须**按长度降序**摘：插件名往往是描述里的一段（实测「启动程序」就嵌在
            // 「…内置动作：启动程序或应用。…」中间）。先摘短的那个，长串的匹配就被破坏了，
            // 于是描述整段留在原文里 —— 断言会报一个看不懂的「摘要里有中文」。
            var pieces = new[]
                {
                    cardEntry.Name, cardEntry.Description, cardEntry.Author,
                    cardEntry.License, cardEntry.ExternalPath, cardInstance.Directory,
                }
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p!)
                .OrderByDescending(p => p.Length);

            foreach (string piece in pieces)
            {
                text = text.Replace(piece, "", StringComparison.Ordinal);
            }
            return text;
        }

        LanguageCode originalLanguage = I18n.CurrentLanguage;
        var cards = new Dictionary<LanguageCode, PluginListItem>();
        try
        {
            foreach (LanguageCode language in Enum.GetValues<LanguageCode>())
            {
                I18n.CurrentLanguage = language;
                cards[language] = PluginListItem.Build(cardInstance);
            }

            PluginListItem enCard = cards[LanguageCode.En];
            foreach ((string name, string value) in new[]
                     {
                         ("状态徽标", enCard.StateText),
                         ("启用按钮", enCard.EnableText),
                         ("设置按钮", enCard.SettingsText),
                         ("卸载按钮", enCard.UninstallText),
                         ("摘要（宿主部分）", StripPluginData(enCard.SummaryText)),
                         ("详情（宿主部分）", StripPluginData(enCard.DetailText)),
                     })
            {
                char? leak = FindCjkLeak(value);
                if (leak.HasValue)
                {
                    return $"[3f] 英文卡片的{name}里出现了中文/日文字符「{leak.Value}」(U+{(int)leak.Value:X4})：" +
                           $"…{value.Replace("\n", "\\n")}…";
                }
            }

            // ③ 关键字段真的拼进去了 —— 否则「没有中文」可能只是「什么都没有」。
            if (!enCard.DetailText.Contains(pluginId, StringComparison.Ordinal))
            {
                return $"[3f] 卡片详情里没有插件 ID「{pluginId}」—— 拼接逻辑漏了字段。";
            }
            if (string.IsNullOrWhiteSpace(enCard.DisplayName) || string.IsNullOrWhiteSpace(enCard.UninstallText))
            {
                return "[3f] 卡片的插件名或卸载按钮文案为空。";
            }
            line($"    英文卡片：摘要 {enCard.SummaryText.Length} 字符 / 详情 {enCard.DetailText.Length} 字符，宿主部分无方块字与中文标点 ✓");

            // ④ 四种语言必须给出四份不同的卡片 —— 相同说明有一门没走自己的词条。
            var cardLanguages = cards.Keys.ToList();
            for (int i = 0; i < cardLanguages.Count; i++)
            {
                for (int j = i + 1; j < cardLanguages.Count; j++)
                {
                    if (string.Equals(cards[cardLanguages[i]].StateText, cards[cardLanguages[j]].StateText, StringComparison.Ordinal)
                        || string.Equals(cards[cardLanguages[i]].UninstallText, cards[cardLanguages[j]].UninstallText, StringComparison.Ordinal))
                    {
                        return $"[3f] {cardLanguages[i]} 与 {cardLanguages[j]} 的卡片文案完全相同，必然有一门没走自己的词条。";
                    }
                }
            }
            line($"    四语言卡片：{cards.Count} 份互不相同 ✓");
        }
        finally
        {
            I18n.CurrentLanguage = originalLanguage;
        }

        // ---- 3g 插件动作面板文案 ----
        //
        // 这块文案与 [3f] 同源：原先全是 SettingsWindow 私有方法里的**代码拼串**，
        // 无界面自检够不着，所以「切了语言它还残不残中文」只能靠人肉点一遍。
        // 搬进 PluginActionPanelText 之后这里才有东西可断言。
        //
        // 同样刻意排在 [4] 之前 —— --skip-invoke 会在 [4] 开头提前 return。
        line("");
        line("[3g] 插件动作面板文案（三种处境 / 逐语言）");

        // 面板会透出插件自带数据（动作名 / 插件名 / ID / 自述）。断言「英文界面里没有方块字」
        // 之前必须先把它摘掉 —— 那是插件写的，不是宿主的翻译责任。这里刻意用**全 ASCII**
        // 的合成数据，免得「摘除顺序」又变成一条隐式依赖（[3f] 正是在这里踩过坑：
        // 插件名嵌在描述里，先摘短串会破坏长串匹配）。
        const string panelPluginId = "selftest.plugin";
        const string panelPluginName = "StarPie SelfTest";
        const string panelContributionId = "selftest.plugin.demo";
        const string panelActionName = "SelfTestAction";
        const string panelDescription = "Synthetic description produced by the self test.";

        LanguageCode[] panelLanguages = Enum.GetValues<LanguageCode>();

        string StripPanelData(string text)
        {
            foreach (string piece in new[]
                         {
                             panelPluginId, panelPluginName, panelContributionId, panelActionName, panelDescription,
                         }.OrderByDescending(p => p.Length))
            {
                text = text.Replace(piece, "", StringComparison.Ordinal);
            }
            return text;
        }

        (string Name, string Title, string Detail, string? Hint)[] BuildPanelSamples()
        {
            (string Title, string Detail, string? Hint) withCandidates = PluginActionPanelText.NotChosen(3);
            (string Title, string Detail, string? Hint) noCandidates = PluginActionPanelText.NotChosen(0);
            (string Title, string Detail, string? Hint) stale = PluginActionPanelText.Unavailable(panelContributionId);
            (string Title, string Detail, string? Hint) healthy = PluginActionPanelText.Registered(
                panelActionName,
                panelPluginId,
                panelPluginName,
                panelContributionId,
                background: false,
                timeoutSeconds: 30,
                description: panelDescription);

            return new (string Name, string Title, string Detail, string? Hint)[]
            {
                ("未选择·有候选", withCandidates.Title, withCandidates.Detail, withCandidates.Hint),
                ("未选择·无候选", noCandidates.Title, noCandidates.Detail, noCandidates.Hint),
                ("引用失效", stale.Title, stale.Detail, stale.Hint),
                ("正常", healthy.Title, healthy.Detail, healthy.Hint),
            };
        }

        LanguageCode panelOriginalLanguage = I18n.CurrentLanguage;
        try
        {
            // ① 四种处境 × 四种语言：标题与正文都不许为空、不许是裸键名。
            //    提示行只有「引用失效」那种处境有；口径必须两端一致 —— 有提示就得非空且不是
            //    裸键名，没提示就老老实实是 null（调用方据此决定显不显示那一行）。
            foreach (LanguageCode language in panelLanguages)
            {
                I18n.CurrentLanguage = language;
                foreach ((string name, string title, string detail, string? hint) in BuildPanelSamples())
                {
                    foreach ((string field, string value) in new[] { ("标题", title), ("正文", detail) })
                    {
                        if (string.IsNullOrWhiteSpace(value))
                        {
                            return $"[3g] {language} 的「{name}」{field}为空 —— 面板上会出现一行空白。";
                        }
                        if (value.StartsWith("PluginsPanel", StringComparison.Ordinal))
                        {
                            return $"[3g] {language} 的「{name}」{field}取到的是裸键名「{value}」—— 词条键写错了。";
                        }
                    }
                    if (hint != null
                        && (string.IsNullOrWhiteSpace(hint)
                            || hint.StartsWith("PluginsPanel", StringComparison.Ordinal)))
                    {
                        return $"[3g] {language} 的「{name}」提示行是空的或裸键名「{hint}」—— " +
                               "用户会看到一行空白或一行键名原文。";
                    }
                }
            }

            // ② 同一处境、四种语言必须给出四份不同的正文 —— 相同说明有一门没走自己的词条。
            var distinctDetails = new HashSet<string>(StringComparer.Ordinal);
            foreach (LanguageCode language in panelLanguages)
            {
                I18n.CurrentLanguage = language;
                distinctDetails.Add(PluginActionPanelText.Unavailable(panelContributionId).Detail);
            }
            if (distinctDetails.Count != panelLanguages.Length)
            {
                return $"[3g] 「引用失效」的正文在 {panelLanguages.Length} 种语言下只得到 " +
                       $"{distinctDetails.Count} 份不同文案 —— 有一门没走自己的词条。";
            }

            // ③ 四种处境的正文必须是四句不同的话。共用同一句意味着有**两处处境被串到了一起** ——
            //    用户照着提示去操作会走错地方（去下拉框里找一个根本不存在的动作）。
            I18n.CurrentLanguage = LanguageCode.ZhCn;
            int situationCount = BuildPanelSamples().Select(s => s.Detail).Distinct(StringComparer.Ordinal).Count();
            if (situationCount != 4)
            {
                return $"[3g] 四种面板处境的正文只得到 {situationCount} 份不同文案 —— 有两处共用了同一句话。";
            }

            // ④ 英文面板的**宿主部分**不许有方块字与全角标点（先照值摘掉插件自带数据）。
            I18n.CurrentLanguage = LanguageCode.En;
            foreach ((string name, string title, string detail, string? hint) in BuildPanelSamples())
            {
                string hosted = StripPanelData(title + "\n" + detail + "\n" + (hint ?? ""));
                char? leak = FindCjkLeak(hosted);
                if (leak.HasValue)
                {
                    return $"[3g] 英文面板的「{name}」里出现了中文/日文字符「{leak.Value}」" +
                           $"(U+{(int)leak.Value:X4})：…{hosted.Replace("\n", "\\n")}…";
                }
            }

            // ⑤ 面板之外的两位：参数提示行的两个分支必须是两句不同的话（否则用户看不出
            //    这个动作有没有必填项），校验结论必须把「几项不合法」这个数字真的拼进去。
            if (string.Equals(
                    PluginActionPanelText.ParamsHint(0),
                    PluginActionPanelText.ParamsHint(2),
                    StringComparison.Ordinal))
            {
                return "[3g] 「参数全部可选」与「有 N 个必填」文案相同 —— 用户看不出这个动作有没有必填项。";
            }
            if (!PluginActionPanelText.ParamsHint(2).Contains('2')
                || !PluginActionPanelText.IssuesCount(2).Contains('2'))
            {
                return "[3g] 参数提示行或校验结论没有把数量拼进去 —— 用户看不到到底差几项。";
            }

            line($"    面板文案：4 种处境 × {panelLanguages.Length} 种语言全部有文案且不是裸键名 ✓");
            line("    英文面板的宿主部分无方块字与全角标点（插件自带数据已按值摘除）✓");
            line("    四种处境互不相同、四语言互不相同；参数提示行两分支不同且数量已拼入 ✓");
        }
        finally
        {
            I18n.CurrentLanguage = panelOriginalLanguage;
        }

        // ⑥ 面板必填提示行（FocusPluginParamsHintText）与校验结论（FocusPluginValidationText）联动状态机：
        //    - 有效配置时橙色必填警告必须 Collapsed，绝不误显；
        //    - 缺失必填参数时橙色必填警告与红色结论均 Visible；
        //    - 语法非法时仅展示红色错误，橙色留空警告保持 Collapsed；
        //    - 动态编辑流转（missing -> valid -> illegal -> valid）即时且严格一致。
        string? panelUiStateMachineError = null;
        RunOnSta(() =>
        {
            var hintBlock = new System.Windows.Controls.TextBlock();
            var valBlock = new System.Windows.Controls.TextBlock();

            // 1. 已配置且有效（IsValid == true）
            var validRemapAction = new ActionItem
            {
                Type = PluginActionBinding.TypeName,
                Name = "按键映射",
                PluginActionRef = new PluginActionRef
                {
                    PluginId = pluginId,
                    ContributionId = "keypadLayer",
                },
                ExtensionData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["keyMap"] = "v1|Q:Num7,W:Num8,E:Num9,A:Num4,S:Num5,D:Num6,Z:Num1,X:Num2,C:Num3,R:Num0"
                }
            };

            var validRes = PluginHost.ValidateActionParameters(validRemapAction);
            if (!validRes.IsValid)
            {
                panelUiStateMachineError = $"[3g] 测试前置条件失败：有效映射应当校验通过，实际为: {validRes.Describe()}";
                return;
            }

            SettingsWindow.UpdateFocusPluginValidationUi(validRes, validRemapAction, null, valBlock, hintBlock);

            if (hintBlock.Visibility != System.Windows.Visibility.Collapsed)
            {
                panelUiStateMachineError = $"[3g] 必填提示隐藏门禁失败：当参数已配置且有效时，FocusPluginParamsHintText 必须隐藏 (Collapsed)，实际为 {hintBlock.Visibility}，文案为: '{hintBlock.Text}'";
                return;
            }
            if (valBlock.Visibility != System.Windows.Visibility.Collapsed)
            {
                panelUiStateMachineError = $"[3g] 必填提示隐藏门禁失败：当参数已配置且有效时，FocusPluginValidationText 必须隐藏 (Collapsed)，实际为 {valBlock.Visibility}";
                return;
            }

            // 2. 缺失必填参数（未填 / 留空，IsValid == false）
            var missingAction = new ActionItem
            {
                Type = PluginActionBinding.TypeName,
                Name = "按键映射",
                PluginActionRef = new PluginActionRef
                {
                    PluginId = pluginId,
                    ContributionId = "keypadLayer",
                },
                ExtensionData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            };

            var missingRes = PluginHost.ValidateActionParameters(missingAction);
            if (missingRes.IsValid)
            {
                panelUiStateMachineError = "[3g] 测试前置条件失败：缺失必填 keyMap 时校验应当拦截";
                return;
            }

            SettingsWindow.UpdateFocusPluginValidationUi(missingRes, missingAction, null, valBlock, hintBlock);

            if (hintBlock.Visibility != System.Windows.Visibility.Visible)
            {
                panelUiStateMachineError = $"[3g] 必填提示警告门禁失败：当必填参数缺失留空时，FocusPluginParamsHintText 必须显示橙色警告 (Visible)，实际为 {hintBlock.Visibility}";
                return;
            }
            if (!hintBlock.Text.Contains('1'))
            {
                panelUiStateMachineError = $"[3g] 必填提示警告门禁失败：缺失必填参数提示文案未包含必填参数计数 1：'{hintBlock.Text}'";
                return;
            }
            if (valBlock.Visibility != System.Windows.Visibility.Visible)
            {
                panelUiStateMachineError = $"[3g] 必填提示警告门禁失败：当必填参数缺失留空时，FocusPluginValidationText 必须显示错误 (Visible)，实际为 {valBlock.Visibility}";
                return;
            }

            // 3. 非法映射参数（语法错误，IsValid == false，但非留空）
            var illegalAction = new ActionItem
            {
                Type = PluginActionBinding.TypeName,
                Name = "按键映射",
                PluginActionRef = new PluginActionRef
                {
                    PluginId = pluginId,
                    ContributionId = "keypadLayer",
                },
                ExtensionData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["keyMap"] = "v1|InvalidSyntaxNoSeparator"
                }
            };

            var illegalRes = PluginHost.ValidateActionParameters(illegalAction);
            if (illegalRes.IsValid)
            {
                panelUiStateMachineError = "[3g] 测试前置条件失败：非法映射配置应当校验拦截";
                return;
            }

            SettingsWindow.UpdateFocusPluginValidationUi(illegalRes, illegalAction, null, valBlock, hintBlock);

            if (hintBlock.Visibility != System.Windows.Visibility.Collapsed)
            {
                panelUiStateMachineError = $"[3g] 非法映射提示门禁失败：当参数非空但语法非法时，橙色留空提示必须隐藏 (Collapsed)，实际为 {hintBlock.Visibility}，文案为: '{hintBlock.Text}'";
                return;
            }
            if (valBlock.Visibility != System.Windows.Visibility.Visible || string.IsNullOrWhiteSpace(valBlock.Text))
            {
                panelUiStateMachineError = "[3g] 非法映射提示门禁失败：当参数非空但语法非法时，FocusPluginValidationText 必须显示具体的校验错误说明";
                return;
            }

            // 4. 动态编辑流转测试（从 missing -> valid -> illegal -> valid）
            var dynamicAction = new ActionItem
            {
                Type = PluginActionBinding.TypeName,
                Name = "按键映射",
                PluginActionRef = new PluginActionRef
                {
                    PluginId = pluginId,
                    ContributionId = "keypadLayer",
                },
                ExtensionData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            };

            // 阶段 1：初始 missing -> hintBlock 与 valBlock 均显示
            SettingsWindow.UpdateFocusPluginValidationUi(
                PluginHost.ValidateActionParameters(dynamicAction), dynamicAction, null, valBlock, hintBlock);
            if (hintBlock.Visibility != System.Windows.Visibility.Visible || valBlock.Visibility != System.Windows.Visibility.Visible)
            {
                panelUiStateMachineError = "[3g] 动态流转阶段1（留空）：hintBlock 与 valBlock 均应为 Visible";
                return;
            }

            // 阶段 2：填入有效映射 -> hintBlock 与 valBlock 均立即隐藏
            dynamicAction.ExtensionData["keyMap"] = "v1|A:Space,B:Enter";
            SettingsWindow.UpdateFocusPluginValidationUi(
                PluginHost.ValidateActionParameters(dynamicAction), dynamicAction, null, valBlock, hintBlock);
            if (hintBlock.Visibility != System.Windows.Visibility.Collapsed || valBlock.Visibility != System.Windows.Visibility.Collapsed)
            {
                panelUiStateMachineError = "[3g] 动态流转阶段2（配置有效）：hintBlock 与 valBlock 均应立即变为 Collapsed";
                return;
            }

            // 阶段 3：编辑为非法映射 -> hintBlock 隐藏（未留空），valBlock 显示语法错误
            dynamicAction.ExtensionData["keyMap"] = "v1|A->";
            SettingsWindow.UpdateFocusPluginValidationUi(
                PluginHost.ValidateActionParameters(dynamicAction), dynamicAction, null, valBlock, hintBlock);
            if (hintBlock.Visibility != System.Windows.Visibility.Collapsed || valBlock.Visibility != System.Windows.Visibility.Visible)
            {
                panelUiStateMachineError = "[3g] 动态流转阶段3（编辑为非法）：hintBlock 应隐藏，valBlock 应展示错误说明";
                return;
            }

            // 阶段 4：修复为有效映射 -> hintBlock 与 valBlock 均恢复隐藏
            dynamicAction.ExtensionData["keyMap"] = "v1|A:Space";
            SettingsWindow.UpdateFocusPluginValidationUi(
                PluginHost.ValidateActionParameters(dynamicAction), dynamicAction, null, valBlock, hintBlock);
            if (hintBlock.Visibility != System.Windows.Visibility.Collapsed || valBlock.Visibility != System.Windows.Visibility.Collapsed)
            {
                panelUiStateMachineError = "[3g] 动态流转阶段4（修复为有效）：hintBlock 与 valBlock 应再次恢复 Collapsed";
                return;
            }
        });

        if (panelUiStateMachineError != null)
        {
            return panelUiStateMachineError;
        }

        line("    必填参数提示行联动状态机：有效隐藏、留空警告、非法拦截、动态编辑四阶段流转全部通过 ✓");

        // ---- 3h 插件级参数页（SDK 1.6）----
        //
        // 这一段管的是「声明出来的东西到底能不能用」：入口判据、字段面是否与声明一致、
        // 标签是否真的走词条、声明的取值范围是否被同一条校验链读过、宿主写进去的键
        // 插件那一侧读不读得回来。
        //
        // 全部只走 PluginSettingsPageService 的静态面，**不建窗口**：一建窗口这些断言
        // 就退化成「得有人去点一下」，而回归恰恰坏在没人点的时候。
        line("");
        line("[3h] 插件级参数页（入口 / 字段面 / 词条 / 校验 / 读写往返）");

        PluginSettingsPageRegistration? declaredPage = PluginHost.Catalog.TryGetSettingsPage(pluginId);
        if (declaredPage == null)
        {
            // 未声明也要留一条断言：入口判据若在「拿不到页」时返回真，卡片上就会出现一个
            // 点开的空按钮 —— 那个判据存在的全部意义就是不让它出现。
            if (PluginSettingsPageService.HasPage(pluginId))
            {
                return "[3h] 贡献点表里没有该插件的参数页，HasPage 却判为有 —— 卡片上会出现点开的空按钮。";
            }
            if (PluginSettingsPageService.Open(pluginId) != null)
            {
                return "[3h] 未声明参数页的插件竟然能 Open 出一张页。";
            }
            line("  本插件未声明参数页：入口判据一致返回「无」 ✓");
        }
        else
        {
            PluginSettingsPageService.Page? page = PluginSettingsPageService.Open(pluginId);
            if (page == null)
            {
                return "[3h] 声明了参数页却 Open 不出来 —— 写读目标（插件的 settings 实例）没接上。";
            }

            // ① 字段面逐位核对。渲染读的是 Page.Fields，它与声明差一个键，
            //    就意味着「用户看到的表」和「插件以为的表」不再是同一张 —— 那种偏差会表现为
            //    用户填了、插件读不到，而两边都没有任何报错。
            if (page.Fields.Count != declaredPage.Fields.Count)
            {
                return $"[3h] 参数字段数不一致：声明 {declaredPage.Fields.Count} 个 / 渲染 {page.Fields.Count} 个。";
            }
            for (int i = 0; i < page.Fields.Count; i++)
            {
                if (!string.Equals(page.Fields[i].Key, declaredPage.Fields[i].Key, StringComparison.Ordinal))
                {
                    return $"[3h] 第 {i} 个字段的键不匹配：声明「{declaredPage.Fields[i].Key}」/ 渲染「{page.Fields[i].Key}」。";
                }
            }

            // ② 标题与字段标签在**每一种语言**下都得解析得开。
            //    判据不是「等于我期望的译文」而是「等于词条里那个值」—— 宿主不认识插件写的文案，
            //    只能核对标签真的取自词条表。这抓的正是历史上踩过的那类缺陷：键的前缀换算写错，
            //    于是永远静默退回字面中文，英文界面要到用户切语言才暴露。
            LanguageCode pageOriginalLanguage = I18n.CurrentLanguage;
            try
            {
                foreach (LanguageCode language in Enum.GetValues<LanguageCode>())
                {
                    I18n.CurrentLanguage = language;
                    PluginSettingsPageService.Page titled = PluginSettingsPageService.Open(pluginId)!;

                    if (string.IsNullOrWhiteSpace(titled.Title))
                    {
                        return $"[3h] {language} 下参数页标题是空的。";
                    }
                    if (titled.Title.StartsWith(PluginApi.I18nKeyPrefix, StringComparison.Ordinal))
                    {
                        return $"[3h] {language} 下参数页标题是裸键名「{titled.Title}」—— TitleKey 写错了。";
                    }
                    // 标题也要和字段标签同一条判据核对「等于词条里那个值」。只查空与裸键名的话，
                    // 键前缀换算写错会静默退回插件给的字面中文 —— 而中文标题在英文界面上不违反
                    // 上面任何一条，正是 ResolveStagedDisplayNames 那次补丁记录过的同一个坑。
                    if (!string.IsNullOrWhiteSpace(declaredPage.TitleKey))
                    {
                        string fullTitleKey = $"{PluginApi.I18nKeyPrefix}{pluginId}.{declaredPage.TitleKey!.Trim()}";
                        string titleFromTable = I18n.GetString(fullTitleKey);
                        if (!string.Equals(titleFromTable, fullTitleKey, StringComparison.Ordinal) &&
                            !string.Equals(titled.Title, titleFromTable, StringComparison.Ordinal))
                        {
                            return $"[3h] {language} 下参数页标题显示成「{titled.Title}」，而词条里是「{titleFromTable}」—— 标题没走词条。";
                        }
                    }

                    foreach (ParameterField field in titled.Fields)
                    {
                        string label = PluginI18n.ResolveLabel(pluginId, field.LabelKey, field.Label);
                        if (string.IsNullOrWhiteSpace(label))
                        {
                            return $"[3h] {language} 下字段「{field.Key}」没有标签 —— 界面上会出现一行空白。";
                        }
                        if (label.StartsWith(PluginApi.I18nKeyPrefix, StringComparison.Ordinal))
                        {
                            return $"[3h] {language} 下字段「{field.Key}」的标签是裸键名「{label}」—— LabelKey 写错了。";
                        }
                        if (string.IsNullOrWhiteSpace(field.LabelKey)) continue;

                        string fullKey = $"{PluginApi.I18nKeyPrefix}{pluginId}.{field.LabelKey!.Trim()}";
                        string fromTable = I18n.GetString(fullKey);
                        if (!string.Equals(fromTable, fullKey, StringComparison.Ordinal) &&
                            !string.Equals(label, fromTable, StringComparison.Ordinal))
                        {
                            return $"[3h] {language} 下字段「{field.Key}」显示成「{label}」，而词条里是「{fromTable}」—— 标签没走词条。";
                        }
                    }
                }
                line($"    标题与 {declaredPage.Fields.Count} 个字段标签：{Enum.GetValues<LanguageCode>().Length} 种语言全部走词条 ✓");
            }
            finally
            {
                I18n.CurrentLanguage = pageOriginalLanguage;
            }

            // ③ 声明里的取值范围必须被真校验读过。用户在设置页填 9999 而界面一声不吭，
            //    比报错更难排查 —— 他会以为范围提示是装饰。
            //    探针打在 page.Fields 上而不是 declaredPage.Fields：窗口渲染读的就是那一份，
            //    校验声明本体只会证明 PluginParameterValidator 自己没坏（那件事 [3b] 已经管了），
            //    证明不了「注册链交出来的字段面」还带着范围。
            ParameterField? ranged = null;
            foreach (ParameterField field in declaredPage.Fields)
            {
                if (field.Type == ParameterFieldType.Number && (field.Min.HasValue || field.Max.HasValue))
                {
                    ranged = field;
                    break;
                }
            }
            if (ranged != null)
            {
                double beyond = (ranged.Max ?? ranged.Min ?? 0) + 100_000;
                var probeValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [ranged.Key] = beyond.ToString("0.##", CultureInfo.InvariantCulture),
                };
                List<PluginParameterIssue> issues = PluginParameterValidator.Validate(page.Fields, probeValues);
                bool flagged = issues.Exists(issue =>
                    string.Equals(issue.Key, ranged!.Key, StringComparison.OrdinalIgnoreCase));
                if (!flagged)
                {
                    return $"[3h] 字段「{ranged.Key}」填了 {beyond}（范围之外）却没被拦 —— 声明的 Min/Max 是装饰。";
                }
                line($"    数字字段「{ranged.Key}」的 Min/Max 在渲染面上依然生效 ✓");
            }
            else
            {
                line("  本插件参数页没有带范围约束的数字字段，Min/Max 断言跳过。");
            }

            // ④ 读写往返。四步各自抓一个不同的坑，缺一步就有一条路没人看过。
            IPluginContext? pluginContext = cardInstance.ContextForSelfTest;
            if (pluginContext == null)
            {
                return "[3h] 拿不到插件上下文 —— 上一段应当已经把它启用。";
            }

            ParameterField firstField = declaredPage.Fields[0];
            string probeValue = "selftest-" + Guid.NewGuid().ToString("N");

            page.Target.Write(firstField.Key, probeValue);
            PluginSettingsPageService.Persist(page);

            if (!string.Equals(cardInstance.Settings.Get(firstField.Key), probeValue, StringComparison.Ordinal))
            {
                return "[3h] 宿主写进参数页的值没落到插件的 settings.json（或写到了别的键上）。";
            }
            if (!string.Equals(pluginContext.SettingsPage.GetValue(firstField.Key), probeValue, StringComparison.Ordinal))
            {
                return "[3h] 插件一侧 SettingsPage.GetValue 读回来的不是宿主写进去的那个值 —— 接缝断了。";
            }

            // 清空 → 键被删 → 读回声明的默认值。「清空即未填写」这条语义要是变成「清空即写入空串」，
            // 插件读到的就是 ""，解析成 0，于是一颗直径 0 的球会在下一次显示时消失。
            page.Target.Write(firstField.Key, "");
            PluginSettingsPageService.Persist(page);

            if (cardInstance.Settings.Get(firstField.Key) != null)
            {
                return "[3h] 清空字段后配置里仍留着这个键 —— 「未填写」在配置里就有了两种表示。";
            }
            string? afterClear = pluginContext.SettingsPage.GetValue(firstField.Key);
            string expectedDefault = firstField.DefaultValue ?? "";
            if (!string.Equals(afterClear ?? "", expectedDefault, StringComparison.Ordinal))
            {
                return $"[3h] 清空后插件读回「{afterClear ?? "(null)"}」，声明的默认值是「{expectedDefault}」—— 回落没生效，" +
                       "插件会把自己的默认值写第二遍。";
            }
            line("    读写往返：宿主写→插件读同值；清空→键被删→读回声明的默认值 ✓");

            // ⑤ 契约边界：一个插件一页，重复注册当场拒绝。放到提交时才判的话，
            //    前一次声明已经被覆盖，插件作者只会看到「我写的页没出现」而看不到原因。
            PluginRegistrationSession gateSession = PluginHost.Catalog.BeginSession(pluginId);
            var gateRegistry = new PluginSettingsPageRegistry(gateSession, pluginId, cardInstance.Settings);
            gateRegistry.Register(new SettingsPageDescriptor
            {
                Title = "自检页",
                Fields = new List<ParameterField> { new() { Key = "selftest.gate", Label = "自检" } },
            });
            try
            {
                gateRegistry.Register(new SettingsPageDescriptor
                {
                    Title = "第二页",
                    Fields = new List<ParameterField> { new() { Key = "selftest.gate2", Label = "自检" } },
                });
                return "[3h] 同一插件重复注册参数页没按契约拒绝 —— 后一页会静默盖掉前一页。";
            }
            catch (PluginContractException)
            {
                gateSession.Discard();
            }
            line("    重复注册按契约拒绝，暂存已丢弃（真实贡献点表未被污染）✓");
        }

        // ---- 4 调用 ----
        line("");
        line("[4] 调用动作（走与轮盘完全相同的接缝）");

        if (skipInvoke)
        {
            // 只想确认识别 / 注册 / 参数校验 / 选择器接缝时应当走这条：「真执行一次动作」
            // 对亮度、音量、剪贴板这类动作就是实打实的副作用，CI 与排查问题
            // 都不该顺手改动用户的机器（实测踩过：反复跑自检把屏幕亮度从 15% 推到 75%）。
            line("  已跳过（--skip-invoke）：识别、注册、参数校验与选择器接缝断言均已跑过，本机环境未被改动。");
            return null;
        }

        // 这一节是**真执行**，不是只读检查。
        // 明写出来是必要的：自检报告通篇读起来像一次静态体检，
        // 而亮度插件这类动作一旦被执行就会真的改变系统状态 ——
        // 作者若以为它是只读的，就会在排查问题时反复跑自检，
        // 结果是把用户的屏幕、音量或剪贴板越改越乱却毫无察觉。
        line("  ⚠️ 本节会真实调用一次动作，可能改变系统状态（如亮度、音量、剪贴板）。");
        PluginActionRegistration first = actions[0];
        ActionItem? actionItem = PluginHost.CreateActionItem(first.FullId);
        if (actionItem == null) return "CreateActionItem 返回 null";

        line($"  动作 Type：{actionItem.Type}");
        line($"  引用：{actionItem.PluginActionRef}");
        line($"  参数：{DescribeParameters(actionItem.ExtensionData)}");

        sw.Restart();
        PluginExecuteOutcome outcome = PluginHost.ExecutePluginAction(actionItem);
        sw.Stop();

        line($"  是否被处理：{outcome.Handled}");
        line($"  成功：{outcome.Success}");
        line($"  后台执行：{outcome.QueuedToBackground}");
        line($"  返回信息：{outcome.Message}");
        line($"  调用耗时：{sw.Elapsed.TotalMilliseconds:F3} ms");

        if (!outcome.Handled || !outcome.Success) return $"动作调用失败：{outcome.Message}";

        // 负向用例：引用一个不存在的贡献点。
        //
        // 这里刻意**不用**「缺少必填参数」来构造负例：那需要真的调用一次动作，
        // 对无参数的动作（例如亮度插件的多数动作）会真的被执行一遍，
        // 于是「自检」本身产生了副作用 —— 屏幕亮度被多调了一次。
        // 改用不存在的贡献点，既能验证宿主的防御路径（不崩溃、不静默成功），
        // 又保证零副作用，而且对任何插件都成立。
        line("  负向用例：引用不存在的贡献点（零副作用）");
        var ghost = new ActionItem
        {
            Type = PluginApi.ActionTypeName,
            Name = first.DisplayName,
            PluginActionRef = new PluginActionRef { PluginId = first.PluginId, ContributionId = "no_such_contribution_zzz" },
            ExtensionData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        };
        PluginExecuteOutcome ghostOutcome = PluginHost.ExecutePluginAction(ghost);
        line($"    被处理={ghostOutcome.Handled} 成功={ghostOutcome.Success} 信息={ghostOutcome.Message}");

        if (ghostOutcome.Success)
        {
            return "宿主防御异常：引用不存在的贡献点却报告成功，用户会看到一个不存在的动作被静默执行。";
        }

        return null;
    }

    private sealed class FailureKindProbeContribution : IActionContribution
    {
        public ActionDescriptor Descriptor { get; } = new()
        {
            Id = "selftestFailureKindProbe",
            DisplayName = "失败原因自检",
            Kind = ActionKind.Sequential,
            TimeoutSeconds = 5,
        };

        public IReadOnlyList<ParameterField> Parameters => Array.Empty<ParameterField>();
        public string? Validate(IReadOnlyDictionary<string, string> parameters) => null;
        public string Preview(IReadOnlyDictionary<string, string> parameters) => "失败原因自检";
        public Task<ActionResult> ExecuteAsync(PluginActionInput input, CancellationToken cancellationToken) =>
            Task.FromResult(ActionResult.Fail("预期中的自检失败"));
    }

    private sealed class LeaseProbeContribution : IActionContribution
    {
        private readonly TaskCompletionSource<ActionResult> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _entered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _cancelled =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ActionDescriptor Descriptor { get; } = new()
        {
            Id = "selftestLeaseProbe",
            DisplayName = "租约自检",
            Kind = ActionKind.Background,
            TimeoutSeconds = 30,
        };

        public IReadOnlyList<ParameterField> Parameters => Array.Empty<ParameterField>();
        public Task Entered => _entered.Task;
        public Task CancellationObserved => _cancelled.Task;

        public string? Validate(IReadOnlyDictionary<string, string> parameters) => null;
        public string Preview(IReadOnlyDictionary<string, string> parameters) => "租约自检";

        public async Task<ActionResult> ExecuteAsync(
            PluginActionInput input,
            CancellationToken cancellationToken)
        {
            using CancellationTokenRegistration registration =
                cancellationToken.Register(() => _cancelled.TrySetResult(true));
            _entered.TrySetResult(true);
            return await _completion.Task.ConfigureAwait(false);
        }

        public void Complete() => _completion.TrySetResult(ActionResult.Ok());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string? RunInvocationLeaseStopProbe(string pluginId, Action<string> line)
    {
        line("");
        line("[5] 活动调用租约与异步停用");

        PluginInstance? instance = PluginHost.Find(pluginId);
        if (instance == null || !instance.IsLoaded) return "租约测试开始前插件未加载。";

        var failureProbe = new FailureKindProbeContribution();
        var failureRegistration = new PluginActionRegistration
        {
            PluginId = pluginId,
            ShortId = "selftestFailureKindProbe",
            FullId = $"{pluginId}.selftestFailureKindProbe",
            Contribution = failureProbe,
            DisplayName = "失败原因自检",
            Kind = ActionKind.Sequential,
            TimeoutSeconds = 5,
        };
        PluginExecuteOutcome expectedFailure = PluginInvoker.Invoke(
            instance,
            failureRegistration,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            new PluginCallCoordinator());
        line($"  插件主动失败：成功={expectedFailure.Success} 原因={expectedFailure.Failure}");
        if (expectedFailure.Success || expectedFailure.Failure != PluginFailureKind.PluginFailed)
        {
            return $"插件返回 ActionResult.Fail 后没有得到结构化 PluginFailed（实际={expectedFailure.Failure}）。";
        }

        var probe = new LeaseProbeContribution();
        var registration = new PluginActionRegistration
        {
            PluginId = pluginId,
            ShortId = "selftestLeaseProbe",
            FullId = $"{pluginId}.selftestLeaseProbe",
            Contribution = probe,
            DisplayName = "租约自检",
            Kind = ActionKind.Background,
            TimeoutSeconds = 30,
        };

        PluginExecuteOutcome queued = PluginInvoker.Invoke(
            instance,
            registration,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            new PluginCallCoordinator());

        if (!queued.QueuedToBackground || !probe.Entered.Wait(2000))
        {
            probe.Complete();
            return "后台租约探针没有进入插件回调。";
        }

        line($"  后台动作已进入，活动调用数：{instance.ActiveCallCount}");
        PluginStopResult pending = PluginHost.DisableAsync(
                pluginId,
                PluginStopReason.SelfTest,
                TimeSpan.FromMilliseconds(150))
            .GetAwaiter().GetResult();

        line($"  首次停用结果：{pending.Status}，剩余调用：{pending.RemainingCalls}");
        if (pending.Status != PluginStopStatus.Pending)
        {
            probe.Complete();
            return $"活动调用未结束时停用应返回 Pending，实际为 {pending.Status}。";
        }
        if (!probe.CancellationObserved.Wait(2000))
        {
            probe.Complete();
            return "停用没有把取消信号传给插件动作。";
        }
        if (instance.ActiveCallCount != 1)
        {
            probe.Complete();
            return $"后台任务未结束时租约计数应为 1，实际为 {instance.ActiveCallCount}。";
        }

        if (instance.TryAcquireInvocation(
                PluginCallKind.ActionExecution,
                out PluginInvocationLease? unexpected,
                out _))
        {
            unexpected?.Dispose();
            probe.Complete();
            return "插件进入停止状态后仍能取得新租约。";
        }

        probe.Complete();
        PluginStopResult stopped = PluginHost.DisableAsync(
                pluginId,
                PluginStopReason.SelfTest,
                PluginHost.DefaultStopGracePeriod)
            .GetAwaiter().GetResult();

        line($"  释放探针后停用结果：{stopped.Status}，活动调用数：{instance.ActiveCallCount}");
        if (!stopped.IsFullyStopped) return $"释放租约后插件仍未停止：{stopped.Message}";
        if (instance.ActiveCallCount != 0) return "停用完成后活动调用计数不为 0。";

        return null;
    }

    /// <summary>
    /// 模拟“重启后插件已启用但尚未加载”的首次动作调用。探针缺少必填参数，
    /// 因此只验证惰性加载与贡献查询，不会进入插件 ExecuteAsync。
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string? RunLazyLoadProbe(string pluginId, ActionItem? probe, Action<string> line)
    {
        line("");
        line("[5b] 重启后首次惰性调用（已启用、未加载、Catalog 为空）");

        if (probe == null)
        {
            line("  插件没有带必填字段的动作，无法构造零副作用探针，跳过。");
            return null;
        }

        PluginInstance? instance = PluginHost.Find(pluginId);
        if (instance == null) return "停用后找不到插件实例。";
        if (instance.IsLoaded) return "探针开始前插件仍处于加载状态，无法模拟重启现场。";
        if (PluginHost.Catalog.SnapshotActions().Count != 0) return "探针开始前 Catalog 仍有动作残留。";

        PluginExecuteOutcome disabledOutcome = PluginHost.ExecutePluginAction(probe);
        line($"  禁用状态调用：处理={disabledOutcome.Handled} 成功={disabledOutcome.Success} 原因={disabledOutcome.Failure} 信息={disabledOutcome.Message}");
        if (instance.IsLoaded) return "用户已禁用插件却被动作路径自动加载。";

        // 判据用结构化的 Failure 而不是 Message 的文案 ——
        // 这段文案迟早要接 i18n，那时 Contains("未启用") 会在非中文语言下静默失效，
        // 而「禁用插件竟然被执行了」这条断言恰恰是最不能失效的一条。
        if (disabledOutcome.Success || disabledOutcome.Failure != PluginFailureKind.NotEnabled)
        {
            return $"禁用插件的动作没有被明确拒绝（原因={disabledOutcome.Failure}）：{disabledOutcome.Message}";
        }

        instance.Entry.Enabled = true;
        PluginRegistryStore.UpsertEntry(instance.Entry);

        PluginExecuteOutcome outcome = PluginHost.ExecutePluginAction(probe);
        bool loadedByAction = instance.IsLoaded;

        line($"  调用结果：处理={outcome.Handled} 成功={outcome.Success} 信息={outcome.Message}");
        line($"  动作触发加载：{loadedByAction}");

        string? failure = null;
        if (!outcome.Handled)
        {
            failure = "有效插件动作引用没有被动作路径处理。";
        }
        else if (!loadedByAction)
        {
            failure = "动作路径没有先加载已启用插件。";
        }
        else if (outcome.Success)
        {
            failure = "缺少必填参数的探针被执行成功，参数校验未在插件调用前生效。";
        }
        else if (outcome.Failure != PluginFailureKind.ValidationFailed)
        {
            // 同上：判据是结构化原因，不是 Message 里的中文。
            failure = $"插件虽然被加载，但没有进入预期的参数校验分支（原因={outcome.Failure}）：{outcome.Message}";
        }

        PluginStopResult stopResult = PluginHost.DisableAsync(pluginId, PluginStopReason.SelfTest).GetAwaiter().GetResult();
        bool disabled = stopResult.IsFullyStopped;
        string disableError = stopResult.Message;
        if (!disabled)
        {
            return failure ?? $"惰性加载探针结束后停用失败：{disableError}";
        }

        bool unloaded = PluginHost.Find(pluginId)?.WaitForUnloadVerdict(5000) ?? false;
        if (!unloaded)
        {
            return failure ?? "惰性加载探针结束后 ALC 未被回收。";
        }

        if (PluginHost.Catalog.SnapshotActions().Count != 0)
        {
            return failure ?? "惰性加载探针停用后仍有动作残留。";
        }

        line("  首次调用已完成加载并命中参数校验，随后再次成功停用。");
        return failure;
    }

    /// <summary>
    /// 收集插件声明的默认值，作为「应当合法」的基线输入。
    /// <para>
    /// 刻意<b>只</b>照抄声明，不为缺失默认值的字段编造任何值：
    /// 编出来的值（例如给热键字段填 <c>"x"</c>）可能过不了插件自己的
    /// <c>ValidationRegex</c>，于是自检会因为「测试自己造的输入」而报出宿主缺陷。
    /// 调用方需先用 <c>baselineAvailable</c> 确认每个字段都有默认值。
    /// </para>
    /// </summary>
    private static Dictionary<string, string> CollectDefaults(IReadOnlyList<ParameterField> fields)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (ParameterField field in fields)
        {
            if (string.IsNullOrEmpty(field.Key)) continue;
            if (field.DefaultValue == null) continue;
            result[field.Key] = field.DefaultValue;
        }

        return result;
    }

    /// <summary>把范围边界显示成「0」而不是「0.0」，避免报告里出现无意义的尾数。</summary>
    private static string FormatBound(double value) =>
        value == Math.Floor(value) && Math.Abs(value) < 1e15
            ? ((long)value).ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string DescribeParameters(Dictionary<string, string>? parameters)
    {
        if (parameters == null || parameters.Count == 0) return "(无)";

        var parts = new List<string>();
        foreach (KeyValuePair<string, string> pair in parameters)
        {
            parts.Add($"{pair.Key}={pair.Value}");
        }
        return string.Join(", ", parts);
    }

    /// <summary>
    /// 能力门禁探针：调一次<b>应当被拒绝</b>的宿主服务，把结果压成一行可读结论。
    /// <para>
    /// 不写成「catch 到异常就算过」：那条断言分三件事，混在一起就没法定位 ——
    /// <list type="number">
    /// <item>门禁存在但归因错了（required 传错，另一项能力的标签变成空话）；</item>
    /// <item>门禁存在但抛了别的异常（例如空引用，看起来也「被拒绝了」）；</item>
    /// <item>正常返回 —— 门禁根本不存在。</item>
    /// </list>
    /// 只判「有没有抛异常」会把后两种混在一起，而它们的修法完全不同。
    /// </para>
    /// <para>
    /// 「拦得清楚」的三条判据：能力必须正好是 <paramref name="expected"/>、
    /// 异常类型不能是 <see cref="PluginContractException"/>（那会让插件被整体卸载）、
    /// 消息里要给出修复动作（去清单里补一行，而不是「权限不足」四个字）。
    /// </para>
    /// </summary>
    /// <param name="expected">
    /// 这次调用<b>应当</b>被拦在哪一项能力上。默认 <c>Process</c>（命令 / Shell 两个服务）——
    /// 窗口服务是 <c>WindowControl</c>、系统服务是 <c>InputSimulation</c>。
    /// 归因错了说明服务的 required 传错了。
    /// </param>
    private static (bool Denied, string Detail) ProbeCapabilityGate(
        Func<bool> call,
        PluginCapability expected = PluginCapability.Process)
    {
        try
        {
            bool accepted = call();
            return (false, $"调用被直接放行（返回 {accepted}）—— 门禁不存在");
        }
        catch (Exception ex)
        {
            return ClassifyGateOutcome(ex, expected);
        }
    }

    /// <summary>
    /// <see cref="ProbeCapabilityGate(Func{bool}, PluginCapability)"/> 的姊妹版，
    /// 供<b>返回 void</b> 的宿主服务使用（目前只有 <c>ScreenCapture.CaptureAndRecognize</c>）。
    /// <para>
    /// 刻意做成<b>另一个名字</b>而不是同名重载。C# 里 <c>() =&gt; M()</c> 这种语句表达式 lambda
    /// 既能转成 <c>Func&lt;bool&gt;</c>（M 返回 bool 时）也能转成 <c>Action</c>（丢弃返回值），
    /// 于是同名重载会让上面几处 <c>Run</c> / <c>Invoke</c> / <c>ApplyLayout</c> 的探针
    /// 落进「谁更匹配」的规则里 —— 那是一个编译器说了算、读代码的人看不出来的选择。
    /// 名字分开，读一眼就知道哪条探针没有返回值可看。
    /// </para>
    /// <para>
    /// 也不能图省事把它包成 <c>() =&gt; { call(); return false; }</c> 塞进上面那个：
    /// 门禁真缺失时打印出来的会是「调用被直接放行（返回 False）」——
    /// 一个凭空捏造的 <c>false</c> 混进结论里，而这条断言的整个意义就是分清
    /// 「门禁不存在」和「门禁在，但它放行了」。
    /// </para>
    /// </summary>
    private static (bool Denied, string Detail) ProbeVoidCapabilityGate(
        Action call,
        PluginCapability expected)
    {
        try
        {
            call();
            return (false, "调用被直接放行（void 方法正常返回）—— 门禁不存在");
        }
        catch (Exception ex)
        {
            return ClassifyGateOutcome(ex, expected);
        }
    }

    private static (bool Denied, string Detail) ProbeRemapCapabilityGate(
        Func<KeyboardRemapResult> call,
        PluginCapability expected)
    {
        try
        {
            KeyboardRemapResult accepted = call();
            return (false, $"调用被直接放行（返回 {accepted.Success}）—— 门禁不存在");
        }
        catch (Exception ex)
        {
            return ClassifyGateOutcome(ex, expected);
        }
    }

    /// <summary>
    /// 把「应当被拒绝」的调用<b>实际抛出的异常</b>压成一行可读结论。
    /// <para>
    /// 两个探针共用这一段的理由是「拦得清楚」的三条判据与「谁去调用它」无关：
    /// 能力必须正好是 <paramref name="expected"/>、异常类型不能是
    /// <see cref="PluginContractException"/>、消息里要给出修复动作。
    /// </para>
    /// <para>
    /// 归因错了说明服务的 required 传错了，而那会让安装确认页上另一项能力的标签变成空话：
    /// 用户勾的是「窗口控制」，运行时拦的却是「进程」。
    /// </para>
    /// </summary>
    private static (bool Denied, string Detail) ClassifyGateOutcome(
        Exception ex,
        PluginCapability expected)
    {
        if (ex is PluginCapabilityDeniedException denied)
        {
            if (denied.Capability != expected)
            {
                return (false, $"拒绝时归因的能力是 {denied.Capability}，应为 {expected}");
            }

            if (!denied.Message.Contains("capabilities", StringComparison.OrdinalIgnoreCase))
            {
                return (false, $"拒绝消息里没说清该怎么修（未提到清单里的 capabilities 数组）：{denied.Message}");
            }

            return (true, $"已拒绝（{denied.ServiceName} / {denied.Capability}）");
        }

        return (false, $"抛出的不是 PluginCapabilityDeniedException，而是 {ex.GetType().Name}：{ex.Message}");
    }

    /// <summary>
    /// 找出正文里第一个「中文/日文方块字或其专属标点」，用于「英文页里不该出现它们」这类断言。
    /// <para>
    /// 四个范围缺一不可：<c>U+3000–U+303F</c> 是 CJK 标点（「」、。），<c>U+3040–U+30FF</c> 是假名，
    /// <c>U+4E00–U+9FFF</c> 是统一表意文字，最后是全角冒号与全角括号 —— 中文词条里
    /// <c>「：」</c> 用得极多，只查汉字会放过一整类「英文页里全角冒号」的漏翻。
    /// </para>
    /// <para>
    /// 反过来，<b>不是</b>漏翻的东西必须放行：省略号 <c>…</c>(U+2026)、emoji（如 ⚠️）、
    /// 以及插件清单自带的字段值。所以这一条只用在<b>合成数据</b>的正文上 ——
    /// 真实插件的名字可能是中文，那时命中不代表缺陷。
    /// </para>
    /// </summary>
    /// <summary>
    /// 找出第一个「不该出现在译文里」的字符：CJK 标点 / 假名 / 方块字 / 全角标点。
    /// <para>
    /// 判据必须与 <c>tests/test_i18n.py</c> 的 <c>CJK_RE</c> <b>逐码位一致</b>：两处守的是同一件事
    /// （「这门语言的界面上还残留着中文」），判据漂了就会得到「自检绿、UI 套件红」这种自相矛盾的结论。
    /// </para>
    /// <para>
    /// 刻意<b>不含 U+3000</b>（表意空格）：它在本项目里被当作**排版分隔符**用
    /// （<c>　|　</c> / <c>　·　</c> / <c>　—　</c>），与语言无关。卡片摘要正是用它分段，
    /// 把 U+3000 算进来会让每一张英文卡片都判成「有中文」。
    /// </para>
    /// </summary>
    private static char? FindCjkLeak(string text)
    {
        foreach (char c in text)
        {
            bool cjkPunctuation = c >= '\u3001' && c <= '\u303F';
            bool kana = c >= '\u3040' && c <= '\u30FF';
            bool ideograph = c >= '\u4E00' && c <= '\u9FFF';
            bool fullWidth = c == '\uFF08' || c == '\uFF09' || c == '\uFF0C' || c == '\uFF1A'
                || c == '\uFF1B' || c == '\uFF1F' || c == '\uFF01';

            if (cjkPunctuation || kana || ideograph || fullWidth)
            {
                return c;
            }
        }

        return null;
    }

    private static int Write(StringBuilder report, string? reportPath, bool pass)
    {
        string text = report.ToString();
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");

        // 报告是这条通道唯一的产物，**绝不能写不出去还不作声**。
        // 这里以前是个空的 catch：结果是「退出码 0、报告却遍寻不着」，而且毫无线索 ——
        // 一次成功的自检看起来和一次静默失败一模一样。
        string[] candidates = !string.IsNullOrWhiteSpace(reportPath)
            ? new[] { reportPath! }
            : new[]
            {
                Path.Combine(Path.GetTempPath(), $"starpie-plugin-selftest-{stamp}.txt"),
                // 临时目录写不进去（权限受限、被重定向、被清理）时退到日志目录：
                // 那里必然可写，否则日志本身也写不了。
                Path.Combine(AppLogger.GetLogFolderPath(), $"starpie-plugin-selftest-{stamp}.txt"),
            };

        string? written = null;
        Exception? lastError = null;

        foreach (string candidate in candidates)
        {
            try
            {
                string? directory = Path.GetDirectoryName(candidate);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                File.WriteAllText(candidate, text, Encoding.UTF8);
                written = candidate;
                break;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        if (written is null)
        {
            AppLogger.LogError("自检报告写入失败（已尝试全部候选路径）", lastError ?? new IOException("未知原因"));
            Console.WriteLine($"[WARN] 自检报告写入失败：{lastError?.Message}");
            Console.WriteLine("报告未能落盘，以下为完整内容：");
            Console.WriteLine(text);
        }
        else
        {
            AppLogger.LogInfo($"自检报告已写入：{written}");
            Console.WriteLine();
            Console.WriteLine($"报告已写入：{written}");
        }

        return pass ? 0 : 1;
    }

    private static void RunRemapStateMachineSimulation(Action<string> line, Action<string, string> fail)
    {
        var controller = KeyboardRemapController.Current;

        int sinkKeyDownCount = 0;
        int sinkKeyUpCount = 0;
        var heldInjectedScans = new HashSet<ushort>();

        try
        {
            uint currentProcId = (uint)Process.GetCurrentProcess().Id;
            string currentProcName = Process.GetCurrentProcess().ProcessName;
            nint normalHwnd = (nint)12345;
            nint otherHwnd = (nint)54321;
            uint otherProcId = currentProcId + 9999;

            controller.SetTestForegroundProcess(normalHwnd, currentProcId);

            // =========================================================================
            // 1. 确定性测试 A：长按自动重复键即刻释放断言
            // 场景：Q Down, Q Down, Q Down, Q Up
            // 必须在任何 Deactivate 之前直接断言，断言收到 3 Down 1 Up，heldInjectedScans 为 0，且会话依然激活
            // =========================================================================
            var detOptions = new KeyboardRemapOptions
            {
                TargetProcessName = currentProcName,
                KeyMap = "v1|Q:Num7,W:Num7",
            };
            var actDet = controller.Activate("selftest-plugin", detOptions);
            if (!actDet.Success)
            {
                fail("自动重复按键断言", $"测试激活失败: {actDet.Message}");
                return;
            }

            var eventLog = new List<(ushort Scan, bool IsKeyUp)>();
            controller.SetTestEventSink((scan, flags, isKeyUp) =>
            {
                eventLog.Add((scan, isKeyUp));
                if (!isKeyUp)
                {
                    sinkKeyDownCount++;
                    heldInjectedScans.Add(scan);
                }
                else
                {
                    sinkKeyUpCount++;
                    heldInjectedScans.Remove(scan);
                }
            });

            eventLog.Clear();
            heldInjectedScans.Clear();

            // 发送 1 次初次 Down，2 次重复 Down，1 次物理 Up
            controller.TryProcessHookEvent((uint)'Q', 256, 0, 0);
            controller.TryProcessHookEvent((uint)'Q', 256, 0, 0);
            controller.TryProcessHookEvent((uint)'Q', 256, 0, 0);
            controller.TryProcessHookEvent((uint)'Q', 257, 0, 0);

            int qDowns = eventLog.Count(e => !e.IsKeyUp);
            int qUps = eventLog.Count(e => e.IsKeyUp);
            if (qDowns != 3 || qUps != 1)
            {
                fail("自动重复按键断言", $"连续重复按键未产生预期配对: KeyDowns={qDowns} (期望3), KeyUps={qUps} (期望1)");
            }
            if (heldInjectedScans.Count != 0)
            {
                fail("自动重复按键断言", $"物理松开后测试下沉存在残留未释放键: {heldInjectedScans.Count}项 (长按自动重复状态机计数泄漏)");
            }
            if (!controller.GetStatus().IsActive)
            {
                fail("自动重复按键断言", "自检控制器在按键松开后不应提前退出会话");
            }

            // =========================================================================
            // 2. 确定性测试 B：多源键映射至同一目标键
            // 场景：Q Down, W Down, Q Up -> 目标键保持按下；W Up -> 目标键释放
            // =========================================================================
            eventLog.Clear();
            heldInjectedScans.Clear();

            controller.TryProcessHookEvent((uint)'Q', 256, 0, 0); // Q Down -> Num7 Down (held = 1)
            if (heldInjectedScans.Count != 1) fail("共享目标键断言", "首次按下 Q 未能成功保持目标键");
            controller.TryProcessHookEvent((uint)'W', 256, 0, 0); // W Down -> 目标键已在按下状态
            controller.TryProcessHookEvent((uint)'Q', 257, 0, 0); // Q Up -> 仍有 W 持有，严禁提前触发 KeyUp！
            if (heldInjectedScans.Count != 1 || eventLog.Any(e => e.IsKeyUp))
            {
                fail("共享目标键断言", "多个源键映射至同一目标时，释放其中一个物理键过早触发了目标键 KeyUp");
            }
            controller.TryProcessHookEvent((uint)'W', 257, 0, 0); // W Up -> 引用计数归零，此时触发 KeyUp
            if (heldInjectedScans.Count != 0 || !eventLog.Any(e => e.IsKeyUp))
            {
                fail("共享目标键断言", "所有源键松开后目标键未能正常注入 KeyUp 释放");
            }

            // =========================================================================
            // 3. 确定性测试 C：前台进程身份未知（fgPid == 0）必须立刻撤销会话且原样放行事件
            // =========================================================================
            eventLog.Clear();
            controller.TryProcessHookEvent((uint)'Q', 256, 0, 0); // 先持有一个映射键
            if (heldInjectedScans.Count != 1) fail("未知前台进程断言", "按键持有失败");
            controller.SetTestForegroundProcess(0, 0); // 模拟前台窗口 PID 为 0
            bool swallowedOnUnknownPid = controller.TryProcessHookEvent((uint)'Q', 256, 0, 0);
            if (swallowedOnUnknownPid)
            {
                fail("未知前台进程断言", "前台 PID 为 0 时按键事件应原样放行（返回 false），实际被拦截");
            }
            if (controller.GetStatus().IsActive)
            {
                fail("未知前台进程断言", "前台 PID 为 0 时会话应立刻被故障安全撤销");
            }
            if (heldInjectedScans.Count != 0)
            {
                fail("未知前台进程断言", "前台 PID 为 0 撤销会话时未释放持有的目标键");
            }

            // =========================================================================
            // 4. 确定性测试 D：焦点切换后的孤儿松开必须撤销会话（前台校验先于孤儿检查）
            // =========================================================================
            controller.SetTestForegroundProcess(normalHwnd, currentProcId);
            actDet = controller.Activate("selftest-plugin", detOptions);
            if (!actDet.Success) fail("失焦孤儿键断言", $"激活失败: {actDet.Message}");
            // 切换到外部前台进程
            controller.SetTestForegroundProcess(otherHwnd, otherProcId);
            // 收到一个从未按下的按键松开（孤儿 KeyUp）
            bool orphanSwallowed = controller.TryProcessHookEvent((uint)'R', 257, 0, 0);
            if (orphanSwallowed)
            {
                fail("失焦孤儿键断言", "失焦后的孤儿松开事件应原样放行");
            }
            if (controller.GetStatus().IsActive)
            {
                fail("失焦孤儿键断言", "失焦后的孤儿松开事件应立刻撤销会话");
            }

            // =========================================================================
            // 5. 确定性测试 E：完整 6 大生命周期与异常触发自愈覆盖
            // 在持键状态下分别触发，断言会话已关闭且 test sink 中持键数完全归零
            // =========================================================================
            string[] lifecycleNames = { "NotifyRecorderActive", "OnPluginStopping", "OnHostPaused", "Deactivate", "FocusLost", "EmergencyEscape" };
            for (int lc = 0; lc < lifecycleNames.Length; lc++)
            {
                controller.SetTestForegroundProcess(normalHwnd, currentProcId);
                var res = controller.Activate("selftest-plugin", detOptions);
                if (!res.Success)
                {
                    fail("生命周期覆盖", $"{lifecycleNames[lc]} 测试前激活失败: {res.Message}");
                    break;
                }

                heldInjectedScans.Clear();
                eventLog.Clear();
                // 按下 Q 键使目标键处于持有状态
                controller.TryProcessHookEvent((uint)'Q', 256, 0, 0);
                if (heldInjectedScans.Count != 1)
                {
                    fail("生命周期覆盖", $"{lifecycleNames[lc]} 按键未能成功进入持有状态");
                }

                switch (lc)
                {
                    case 0: // NotifyRecorderActive
                        controller.NotifyRecorderActive();
                        break;
                    case 1: // OnPluginStopping
                        controller.OnPluginStopping("selftest-plugin");
                        break;
                    case 2: // OnHostPaused
                        controller.OnHostPaused();
                        break;
                    case 3: // Explicit Deactivate
                        controller.Deactivate("selftest-plugin");
                        break;
                    case 4: // FocusLost
                        controller.SetTestForegroundProcess(otherHwnd, otherProcId);
                        controller.TryProcessHookEvent((uint)'E', 256, 0, 0);
                        break;
                    case 5: // EmergencyEscape (>1.5s)
                        controller.TryProcessHookEvent(27, 256, 0, 0); // Escape Down
                        controller.SetEscapeDownTicksForTest(Stopwatch.GetTimestamp() - (long)(Stopwatch.Frequency * 1.6));
                        controller.TriggerEscapeTimerTickForTest();
                        break;
                }

                if (controller.GetStatus().IsActive)
                {
                    fail("生命周期覆盖", $"{lifecycleNames[lc]} 触发后会话仍处于激活状态");
                }
                if (heldInjectedScans.Count != 0)
                {
                    fail("生命周期覆盖", $"{lifecycleNames[lc]} 触发后下沉仍有未释放按键残留: {heldInjectedScans.Count}项");
                }
            }

            // =========================================================================
            // 6. 10,000 次随机状态机压测
            // =========================================================================
            int firstKeyAfterFocusLostPassedCount = 0;
            int focusLostScenariosCount = 0;
            int repeatEventsCount = 0;

            sinkKeyDownCount = 0;
            sinkKeyUpCount = 0;
            heldInjectedScans.Clear();

            controller.SetTestEventSink((scan, flags, isKeyUp) =>
            {
                if (!isKeyUp)
                {
                    sinkKeyDownCount++;
                    heldInjectedScans.Add(scan);
                }
                else
                {
                    sinkKeyUpCount++;
                    heldInjectedScans.Remove(scan);
                }
            });

            controller.SetTestForegroundProcess(normalHwnd, currentProcId);

            var options = new KeyboardRemapOptions
            {
                TargetProcessName = currentProcName,
                KeyMap = KeyMapCodec.Encode(KeyMapCodec.GetDefaultSpatialPreset()),
            };

            var actRes = controller.Activate("selftest-plugin", options);
            if (!actRes.Success)
            {
                fail("按键状态机", $"生产 KeyboardRemapController 激活失败: {actRes.Message}");
                return;
            }

            uint[] sampleKeys = { (uint)'Q', (uint)'W', (uint)'E', (uint)'A', (uint)'S', (uint)'D', (uint)'Z', (uint)'X', (uint)'C', (uint)'R' };
            var rng = new Random(42);

            for (int i = 0; i < 10000; i++)
            {
                if (!controller.GetStatus().IsActive)
                {
                    controller.SetTestForegroundProcess(normalHwnd, currentProcId);
                    var re = controller.Activate("selftest-plugin", options);
                    if (!re.Success)
                    {
                        fail("按键状态机", $"重新激活失败: {re.Message}");
                        break;
                    }
                }

                int scenario = rng.Next(8);
                uint key = sampleKeys[rng.Next(sampleKeys.Length)];

                switch (scenario)
                {
                    case 0: // 单键正常按下与松开
                    {
                        bool swallowedDown = controller.TryProcessHookEvent(key, 256, 0, 0); // WM_KEYDOWN
                        bool swallowedUp = controller.TryProcessHookEvent(key, 257, 0, 0);   // WM_KEYUP
                        if (!swallowedDown || !swallowedUp)
                        {
                            fail("按键状态机", $"映射键 {key} 未被正常拦截");
                        }
                        break;
                    }

                    case 1: // 长按重复输入 KeyDown，随后松开
                    {
                        controller.TryProcessHookEvent(key, 256, 0, 0);
                        controller.TryProcessHookEvent(key, 256, 0, 0);
                        controller.TryProcessHookEvent(key, 256, 0, 0);
                        repeatEventsCount += 2;
                        controller.TryProcessHookEvent(key, 257, 0, 0);
                        break;
                    }

                    case 2: // 多键重叠按下与交叉松开（确保两个不同的物理键）
                    {
                        int idx1 = rng.Next(sampleKeys.Length);
                        int idx2 = (idx1 + 1 + rng.Next(sampleKeys.Length - 1)) % sampleKeys.Length;
                        uint k1 = sampleKeys[idx1];
                        uint k2 = sampleKeys[idx2];
                        controller.TryProcessHookEvent(k1, 256, 0, 0);
                        controller.TryProcessHookEvent(k2, 256, 0, 0);
                        controller.TryProcessHookEvent(k1, 257, 0, 0);
                        controller.TryProcessHookEvent(k2, 257, 0, 0);
                        break;
                    }

                    case 3: // 焦点切换导致会话撤销
                    {
                        controller.TryProcessHookEvent(key, 256, 0, 0);
                        controller.SetTestForegroundProcess(otherHwnd, otherProcId);
                        focusLostScenariosCount++;

                        bool handledInNewProcess = controller.TryProcessHookEvent((uint)'Q', 256, 0, 0);
                        if (!handledInNewProcess)
                        {
                            firstKeyAfterFocusLostPassedCount++;
                        }
                        controller.TryProcessHookEvent((uint)'Q', 257, 0, 0);

                        if (controller.GetStatus().IsActive)
                        {
                            fail("按键状态机", "焦点丢失后会话未自动撤销");
                        }
                        break;
                    }

                    case 4: // 宿主暂停手势与重映射测试
                    {
                        controller.TryProcessHookEvent(key, 256, 0, 0);
                        controller.OnHostPaused();
                        if (controller.GetStatus().IsActive)
                        {
                            fail("按键状态机", "宿主暂停手势后会话未撤销");
                        }
                        break;
                    }

                    case 5: // 主动停用测试
                    {
                        controller.TryProcessHookEvent(key, 256, 0, 0);
                        controller.Deactivate("selftest-plugin");
                        if (controller.GetStatus().IsActive)
                        {
                            fail("按键状态机", "Deactivate 后会话未关闭");
                        }
                        break;
                    }

                    case 6: // 未映射按键测试
                    {
                        bool unmappedHandled = controller.TryProcessHookEvent(0x20, 256, 0, 0);
                        if (unmappedHandled)
                        {
                            fail("按键状态机", "未映射按键不应被拦截");
                        }
                        break;
                    }

                    case 7: // 孤儿 KeyUp 测试
                    {
                        int beforeKeyUps = sinkKeyUpCount;
                        bool orphanHandled = controller.TryProcessHookEvent(key, 257, 0, 0);
                        if (orphanHandled || sinkKeyUpCount != beforeKeyUps)
                        {
                            fail("按键状态机", "孤儿 KeyUp 未被放行或错误触发了注入");
                        }
                        break;
                    }
                }
            }

            bool allPaired = (sinkKeyDownCount - repeatEventsCount == sinkKeyUpCount) && (heldInjectedScans.Count == 0);
            bool allFocusLossPassed = (firstKeyAfterFocusLostPassedCount == focusLostScenariosCount) && (focusLostScenariosCount > 0);

            if (!allPaired)
            {
                fail("按键状态机", $"生产控制器真实状态机按键配对失败: KeyDowns={sinkKeyDownCount}, RepeatKeyDowns={repeatEventsCount}, KeyUps={sinkKeyUpCount}, 残存未释放键数={heldInjectedScans.Count}");
            }
            else if (!allFocusLossPassed)
            {
                fail("按键状态机", $"焦点丢失首键放行失败: {firstKeyAfterFocusLostPassedCount}/{focusLostScenariosCount}");
            }
            else
            {
                line($"  真实控制器状态转换与全生命周期自检：即刻释放/共享目标/未知前台/失焦孤儿/6大生命周期全部通过；10,000次随机状态机 KeyDown/KeyUp 配对率 100% ({sinkKeyDownCount - repeatEventsCount}/{sinkKeyUpCount})，长按重复转发 {repeatEventsCount} 次，残存卡键 0 项，失焦首键放行 100% ({firstKeyAfterFocusLostPassedCount}/{focusLostScenariosCount}) ✓");
            }
        }
        finally
        {
            controller.Deactivate("selftest-plugin");
            controller.ClearTestForegroundProcess();
            controller.SetTestEventSink(null);
        }
    }

    private static void RunRegressionChecks(Action<string> line, Action<string, string> fail)
    {
        // -------------------------------------------------------------------------
        // 回归 1：宿主重映射入口进程名规范化（带/不带 .exe / 路径）与严格 PID 安全门禁
        // -------------------------------------------------------------------------
        if (KeyboardRemapController.NormalizeProcessName("starpie.exe") != "starpie")
        {
            fail("进程名规范化", "带 .exe 后缀的进程名未被正确规范化为裸进程名");
        }
        if (KeyboardRemapController.NormalizeProcessName("StarPie") != "StarPie")
        {
            fail("进程名规范化", "无扩展名进程名在规范化时发生非预期改变");
        }
        if (KeyboardRemapController.NormalizeProcessName(@"C:\Program Files\StarPie\starpie.exe") != "starpie")
        {
            fail("进程名规范化", "带完整路径与 .exe 的进程名未被提取规范化为裸文件名");
        }
        if (KeyboardRemapController.NormalizeProcessName("   ") != "" || KeyboardRemapController.NormalizeProcessName(null) != "")
        {
            fail("进程名规范化", "空字符串或 null 进程名规范化应返回空串");
        }

        var controller = KeyboardRemapController.Current;
        uint currentProcId = (uint)Process.GetCurrentProcess().Id;
        string currentProcName = Process.GetCurrentProcess().ProcessName;
        nint normalHwnd = (nint)12345;

        try
        {
            controller.SetTestForegroundProcess(normalHwnd, currentProcId);

            // 场景 A：插件传入带 .exe 的进程名（例如 "StarPie.exe"），前台为裸进程名，必须成功匹配激活
            var optWithExe = new KeyboardRemapOptions
            {
                TargetProcessName = currentProcName + ".exe",
                KeyMap = "v1|Q:Num7",
            };
            var resWithExe = controller.Activate("regression-plugin", optWithExe);
            if (!resWithExe.Success)
            {
                fail("进程名规范化", $"传入带 .exe 进程名激活失败: {resWithExe.Message}");
            }
            controller.Deactivate("regression-plugin");

            // 场景 B：严格 PID 安全门禁断言 —— 即使进程名一致，若前台 PID 为 0 或不匹配，严禁激活/工作
            controller.SetTestForegroundProcess(0, 0);
            var resZeroPid = controller.Activate("regression-plugin", optWithExe);
            if (resZeroPid.Success)
            {
                fail("前台PID门禁", "前台 PID 为 0 时仍激活成功，违反 fail-closed 门禁要求");
                controller.Deactivate("regression-plugin");
            }

            // 场景 C：焦点切换导致 PID 不匹配时，按键原样放行且会话立刻撤销
            controller.SetTestForegroundProcess(normalHwnd, currentProcId);
            var resOk = controller.Activate("regression-plugin", optWithExe);
            if (resOk.Success)
            {
                controller.SetTestForegroundProcess(normalHwnd, currentProcId + 8888);
                bool swallowed = controller.TryProcessHookEvent((uint)'Q', 256, 0, 0);
                if (swallowed || controller.GetStatus().IsActive)
                {
                    fail("前台PID门禁", "发生进程 PID 不一致时，按键未被原样放行或会话未被及时撤销");
                }
            }
        }
        finally
        {
            controller.Deactivate("regression-plugin");
            controller.ClearTestForegroundProcess();
            controller.SetTestEventSink(null);
        }

        // -------------------------------------------------------------------------
        // 回归 2：KeyMap 编辑器画刷安全、打开异常捕获与主题初始化契约
        // -------------------------------------------------------------------------
        var dummyElement = new System.Windows.FrameworkElement();
        AppThemeManager.ApplyTheme(dummyElement, "Light");
        if (dummyElement.Resources["CardBorderBrush"] == null)
        {
            fail("主题画刷定义", "AppThemeManager 浅色主题未正确注入 CardBorderBrush");
        }
        AppThemeManager.ApplyTheme(dummyElement, "Dark");
        if (dummyElement.Resources["CardBorderBrush"] == null)
        {
            fail("主题画刷定义", "AppThemeManager 深色主题未正确注入 CardBorderBrush");
        }

        RunOnSta(() =>
        {
            try
            {
                var editor = new KeyMapEditorWindow("KeyQ=Numpad7;KeyW=Numpad8");
                if (editor == null)
                {
                    fail("KeyMap编辑器窗口初始化", "KeyMapEditorWindow 构造失败");
                    return;
                }

                string[] requiredBrushes = new[]
                {
                    "BorderSubtleBrush",
                    "CardBorderBrush",
                    "InputBorderBrush",
                    "CardBackgroundBrush",
                    "InputBackgroundBrush",
                    "TextPrimaryBrush",
                    "TextSecondaryBrush",
                    "TextMutedBrush",
                    "WindowBackgroundBrush",
                    "AccentPrimaryBrush",
                    "ButtonHoverBgBrush",
                    "ButtonDefaultBgBrush",
                    "ButtonDefaultBorderBrush",
                    "ButtonDefaultFgBrush"
                };

                foreach (var brushKey in requiredBrushes)
                {
                    try
                    {
                        var brush = editor.FindResource(brushKey);
                        if (brush == null)
                        {
                            fail("KeyMap编辑器画刷解析", $"FindResource 无法解析画刷资源: {brushKey}");
                        }
                    }
                    catch (Exception ex)
                    {
                        fail("KeyMap编辑器画刷解析", $"FindResource('{brushKey}') 抛出异常（原缺失画刷回归）: {ex.Message}");
                    }
                }

                // 验证深浅色主题切换后画刷均能正常解析
                foreach (var theme in new[] { "Light", "Dark" })
                {
                    AppThemeManager.ApplyTheme(editor, theme);
                    foreach (var brushKey in requiredBrushes)
                    {
                        try
                        {
                            var brush = editor.FindResource(brushKey);
                            if (brush == null)
                            {
                                fail("KeyMap编辑器画刷解析", $"主题 {theme} 下 FindResource 无法解析画刷资源: {brushKey}");
                            }
                        }
                        catch (Exception ex)
                        {
                            fail("KeyMap编辑器画刷解析", $"主题 {theme} 下 FindResource('{brushKey}') 抛出异常: {ex.Message}");
                        }
                    }
                }

                // 证明：当画刷缺失时，查找结果不是有效 Brush（为 UnsetValue），原代码的 (Brush) 强转必然导致打开异常
                var missingRes = editor.FindResource("MissingBrush_ShouldNotBeFound");
                if (missingRes is System.Windows.Media.Brush)
                {
                    fail("KeyMap编辑器画刷异常捕获", "缺失画刷不应被解析为有效 Brush 对象");
                }
                bool castFailed = false;
                try
                {
                    _ = (System.Windows.Media.Brush)missingRes;
                }
                catch (InvalidCastException)
                {
                    castFailed = true;
                }
                if (!castFailed)
                {
                    fail("KeyMap编辑器画刷异常捕获", "缺失画刷强转 (Brush) 未能捕获类型转换异常");
                }

                // 验证单键录制控件、规范按键名及友好显示（. vs Num .，OemPeriod vs NumDecimal）
                if (editor.SourceKeyRecorder == null || editor.TargetKeyRecorder == null)
                {
                    fail("KeyMap单键录制控件集成", "KeyMapEditorWindow 未正确初始化 SourceKeyRecorder 或 TargetKeyRecorder");
                }
                else
                {
                    editor.SourceKeyRecorder.SetKey(".");
                    if (editor.SourceKeyRecorder.SelectedKey != "OemPeriod")
                    {
                        fail("KeyMap普通句点规范化", $"输入 '.' 规范键名应为 'OemPeriod'，实际为 '{editor.SourceKeyRecorder.SelectedKey}'");
                    }
                    if (KeyMapCodec.GetFriendlyKeyDisplayName("OemPeriod") != ".")
                    {
                        fail("KeyMap普通句点友好显示", $"OemPeriod 友好显示应为 '.'，实际为 '{KeyMapCodec.GetFriendlyKeyDisplayName("OemPeriod")}'");
                    }

                    editor.TargetKeyRecorder.SetKey("Num.");
                    if (editor.TargetKeyRecorder.SelectedKey != "NumDecimal")
                    {
                        fail("KeyMap小键盘句点规范化", $"输入 'Num.' 规范键名应为 'NumDecimal'，实际为 '{editor.TargetKeyRecorder.SelectedKey}'");
                    }
                    if (KeyMapCodec.GetFriendlyKeyDisplayName("NumDecimal") != "Num .")
                    {
                        fail("KeyMap小键盘句点友好显示", $"NumDecimal 友好显示应为 'Num .'，实际为 '{KeyMapCodec.GetFriendlyKeyDisplayName("NumDecimal")}'");
                    }

                    var conv = new KeyDisplayNameConverter();
                    string? convPeriod = conv.Convert("OemPeriod", typeof(string), null!, System.Globalization.CultureInfo.InvariantCulture) as string;
                    if (convPeriod != ". (OemPeriod)")
                    {
                        fail("KeyMap表格转换器格式", $"OemPeriod 呈现格式应为 '. (OemPeriod)'，实际为 '{convPeriod}'");
                    }
                    string? convNumDec = conv.Convert("NumDecimal", typeof(string), null!, System.Globalization.CultureInfo.InvariantCulture) as string;
                    if (convNumDec != "Num . (NumDecimal)")
                    {
                        fail("KeyMap表格转换器格式", $"NumDecimal 呈现格式应为 'Num . (NumDecimal)'，实际为 '{convNumDec}'");
                    }

                    var testEntries = new[] { new KeyboardRemapEntry { FromKey = "OemPeriod", ToKey = "NumDecimal" } };
                    string encodedPair = KeyMapCodec.Encode(testEntries);
                    if (encodedPair != "v1|OemPeriod:NumDecimal")
                    {
                        fail("KeyMap句点与小键盘编解码", $"OemPeriod:NumDecimal 序列化不符: {encodedPair}");
                    }
                    if (!KeyMapCodec.TryDecode(encodedPair, out var decList, out _) || decList == null || decList.Count != 1 || decList[0].FromKey != "OemPeriod" || decList[0].ToKey != "NumDecimal")
                    {
                        fail("KeyMap句点与小键盘编解码", "OemPeriod:NumDecimal 反序列化失败");
                    }

                    if (!KeyMapValidator.IsModifierVk(16) || !KeyMapValidator.IsModifierVk(17) || !KeyMapValidator.IsModifierVk(18) || !KeyMapValidator.IsModifierVk(91))
                    {
                        fail("KeyMap修饰键判定", "Shift/Ctrl/Alt/Win 等修饰键未被 IsModifierVk 识别");
                    }
                    if (KeyMapValidator.IsModifierVk(0xBE) || KeyMapValidator.IsModifierVk(0x6E))
                    {
                        fail("KeyMap修饰键判定", "OemPeriod 或 NumDecimal 不应被判定为修饰键");
                    }

                    editor.SourceKeyRecorder.StartRecording();
                    if (!editor.SourceKeyRecorder.IsRecording)
                    {
                        fail("KeyMap单键录制控件状态", "StartRecording 后 IsRecording 应为 true");
                    }
                    editor.SourceKeyRecorder.CancelRecording();
                    if (editor.SourceKeyRecorder.IsRecording)
                    {
                        fail("KeyMap单键录制控件状态", "CancelRecording 后 IsRecording 应为 false");
                    }

                    // 回归检验：SingleKeyRecorderBox 构造函数不设置本地 Template，可通过 OverrideMetadata 或模板 ApplyTemplate 实例化子元素
                    var standaloneRecorder = new SingleKeyRecorderBox();
                    standaloneRecorder.ApplyTemplate();
                    if (standaloneRecorder.DisplayTextBlock == null || standaloneRecorder.ClearButton == null)
                    {
                        fail("KeyMap单键录制控件模板自包含", "SingleKeyRecorderBox.ApplyTemplate() 未能正确生成 PART_DisplayText 或 PART_ClearButton");
                    }

                    editor.SourceRecorder.ApplyTemplate();
                    if (editor.SourceRecorder.DisplayTextBlock == null || editor.SourceRecorder.ClearButton == null)
                    {
                        fail("KeyMap窗口内录制框模板应用", "KeyMapEditorWindow 内 SourceRecorder.ApplyTemplate() 未能生成 PART_DisplayText 或 PART_ClearButton");
                    }

                    // 回归检验：KeyDisplayNameConverter.ConvertBack 绝不抛出异常，并正确提取括号内的规范键名
                    try
                    {
                        object? backPeriod = conv.ConvertBack(". (OemPeriod)", typeof(string), null!, System.Globalization.CultureInfo.InvariantCulture);
                        if (backPeriod as string != "OemPeriod")
                        {
                            fail("KeyMap转换器逆向解析", $"'. (OemPeriod)' 逆向解析应为 'OemPeriod'，实际为 '{backPeriod}'");
                        }
                        object? backNumDec = conv.ConvertBack("Num . (NumDecimal)", typeof(string), null!, System.Globalization.CultureInfo.InvariantCulture);
                        if (backNumDec as string != "NumDecimal")
                        {
                            fail("KeyMap转换器逆向解析", $"'Num . (NumDecimal)' 逆向解析应为 'NumDecimal'，实际为 '{backNumDec}'");
                        }
                        object? backEmpty = conv.ConvertBack("", typeof(string), null!, System.Globalization.CultureInfo.InvariantCulture);
                        if (backEmpty as string != "")
                        {
                            fail("KeyMap转换器逆向解析空值", $"空字符串逆向解析应为空，实际为 '{backEmpty}'");
                        }
                        object? backNull = conv.ConvertBack(null!, typeof(string), null!, System.Globalization.CultureInfo.InvariantCulture);
                        if (backNull as string != "")
                        {
                            fail("KeyMap转换器逆向解析null", $"null 逆向解析应为空，实际为 '{backNull}'");
                        }
                    }
                    catch (Exception ex)
                    {
                        fail("KeyMap转换器逆向解析异常", $"KeyDisplayNameConverter.ConvertBack 抛出异常: {ex.Message}");
                    }

                    // 回归检验：MappingsDataGrid 及其列为只读，避免单元格行内编辑
                    if (!editor.MappingsDataGrid.IsReadOnly || !editor.ColSource.IsReadOnly || !editor.ColTarget.IsReadOnly)
                    {
                        fail("KeyMap表格只读属性", "MappingsDataGrid 及其文本列必须设置 IsReadOnly=True");
                    }

                    // 回归检验：录制完整源键与目标键后，保存/提交时自动纳入规则（无需手动点击添加映射）
                    editor.SourceRecorder.SetKey("F");
                    editor.TargetRecorder.SetKey(".");
                    if (!editor.TryCommitPendingMapping(out string? commitErr) || !string.IsNullOrEmpty(commitErr))
                    {
                        fail("KeyMap未点添加直接保存", $"录制完整源键与目标键后提交失败: {commitErr}");
                    }
                    if (!editor.Entries.Any(e => e.FromKey == "F" && e.ToKey == "OemPeriod"))
                    {
                        fail("KeyMap未点添加直接保存", "录制完整的规则未被自动纳入映射表");
                    }
                    if (!string.IsNullOrEmpty(editor.SourceRecorder.SelectedKey) || !string.IsNullOrEmpty(editor.TargetRecorder.SelectedKey))
                    {
                        fail("KeyMap未点添加直接保存", "自动纳入规则后录键框未被清空");
                    }

                    // 回归检验：只录了一半时给出明确提示，不静默丢弃也不允许保存
                    editor.SourceRecorder.SetKey("G");
                    editor.TargetRecorder.ClearKey();
                    if (editor.TryCommitPendingMapping(out string? halfTargetErr) || string.IsNullOrEmpty(halfTargetErr))
                    {
                        fail("KeyMap仅录制源键拦截", "仅录制源键时应拦截并返回明确错误提示");
                    }
                    if (editor.Entries.Any(e => e.FromKey == "G"))
                    {
                        fail("KeyMap仅录制源键拦截", "半录入的规则不应进入映射表");
                    }

                    editor.SourceRecorder.ClearKey();
                    editor.TargetRecorder.SetKey("Num5");
                    if (editor.TryCommitPendingMapping(out string? halfSourceErr) || string.IsNullOrEmpty(halfSourceErr))
                    {
                        fail("KeyMap仅录制目标键拦截", "仅录制目标键时应拦截并返回明确错误提示");
                    }

                    // 回归检验：选中行切换为“更新映射”，取消选中恢复“添加映射”
                    editor.SourceRecorder.ClearKey();
                    editor.TargetRecorder.ClearKey();
                    if (editor.Entries.Count > 0)
                    {
                        editor.MappingsDataGrid.SelectedItem = editor.Entries[0];
                        editor.UpdateAddButtonState();
                        if (editor.AddBtn.Content as string != I18n.T("KeyMapEditorUpdateMapping"))
                        {
                            fail("KeyMap选中行更新文案", $"选中行后按钮文案应为 '更新映射'，实际为 '{editor.AddBtn.Content}'");
                        }

                        editor.MappingsDataGrid.SelectedItem = null;
                        editor.UpdateAddButtonState();
                        if (editor.AddBtn.Content as string != I18n.T("KeyMapEditorAddMapping"))
                        {
                            fail("KeyMap未选中行添加文案", $"未选中行按钮文案应为 '添加映射'，实际为 '{editor.AddBtn.Content}'");
                        }
                    }

                    // 回归检验：选中 Q→Num7 后把源键改为 F，点击“更新映射”，结果应只剩 F→Num7，不得保留 Q→Num7
                    editor.Entries.Clear();
                    editor.Entries.Add(new KeyboardRemapEntry { FromKey = "Q", ToKey = "Num7" });
                    editor.Entries.Add(new KeyboardRemapEntry { FromKey = "W", ToKey = "Num8" });
                    var entryQ1 = editor.Entries[0];
                    editor.MappingsDataGrid.SelectedItem = entryQ1;
                    editor.UpdateAddButtonState();
                    if (editor.SourceRecorder.SelectedKey != "Q" || editor.TargetRecorder.SelectedKey != "Num7")
                    {
                        fail("KeyMap行载入录键框", "选中行未正确将 Q 与 Num7 载入录键框");
                    }
                    if (editor.AddBtn.Content as string != I18n.T("KeyMapEditorUpdateMapping"))
                    {
                        fail("KeyMap编辑行按钮状态", "选中行后按钮文案应为 '更新映射'");
                    }
                    editor.SourceRecorder.SetKey("F");
                    if (editor.TargetRecorder.SelectedKey != "Num7")
                    {
                        fail("KeyMap修改源键保留目标键", $"修改源键为 F 后目标键应保持 Num7，实际为 '{editor.TargetRecorder.SelectedKey}'");
                    }
                    if (!editor.TryCommitPendingMapping(out string? updateErr) || !string.IsNullOrEmpty(updateErr))
                    {
                        fail("KeyMap更新映射提交", $"点击更新映射提交失败: {updateErr}");
                    }
                    if (!editor.Entries.Any(e => e.FromKey == "F" && e.ToKey == "Num7"))
                    {
                        fail("KeyMap更新源键结果", "映射列表中未包含更新后的 F→Num7");
                    }
                    if (editor.Entries.Any(e => e.FromKey == "Q"))
                    {
                        fail("KeyMap更新源键旧键残留", "映射列表中不应保留被修改的原键 Q (Q→Num7)");
                    }
                    if (editor.Entries.Count != 2)
                    {
                        fail("KeyMap更新源键列表项数", $"更新后列表项数应保持为 2，实际为 {editor.Entries.Count}");
                    }

                    // 回归检验：选中 Q→Num7 后把源键改为 F，直接点击“保存”（无点击添加/更新），结果应只剩 F→Num7，不得保留 Q→Num7
                    editor.Entries.Clear();
                    editor.Entries.Add(new KeyboardRemapEntry { FromKey = "Q", ToKey = "Num7" });
                    editor.Entries.Add(new KeyboardRemapEntry { FromKey = "W", ToKey = "Num8" });
                    var entryQ2 = editor.Entries[0];
                    editor.MappingsDataGrid.SelectedItem = entryQ2;
                    editor.SourceRecorder.SetKey("F");
                    if (!editor.TryCommitPendingMapping(out string? directSaveErr) || !string.IsNullOrEmpty(directSaveErr))
                    {
                        fail("KeyMap直接保存提交", $"修改源键后直接保存提交失败: {directSaveErr}");
                    }
                    if (!editor.Entries.Any(e => e.FromKey == "F" && e.ToKey == "Num7"))
                    {
                        fail("KeyMap直接保存结果", "映射列表中未包含更新后的 F→Num7");
                    }
                    if (editor.Entries.Any(e => e.FromKey == "Q"))
                    {
                        fail("KeyMap直接保存旧键残留", "修改源键后直接保存不应保留被修改的原键 Q (Q→Num7)");
                    }
                    if (editor.Entries.Count != 2)
                    {
                        fail("KeyMap直接保存列表项数", $"直接保存后列表项数应保持为 2，实际为 {editor.Entries.Count}");
                    }

                    // -------------------------------------------------------------
                    // 回归：KeyMapEditor 图形区增强（动态键生成、按键区分、多对一高亮、动态清理、缩放钳位）
                    // -------------------------------------------------------------

                    // ① 新增 F ➔ . 能在两侧生成动态图形键
                    editor.Entries.Clear();
                    editor.Entries.Add(new KeyboardRemapEntry { FromKey = "F", ToKey = "OemPeriod" });
                    editor.SyncDynamicVisualKeys();
                    editor.RefreshVisualHighlights();

                    if (!editor.PhysicalButtons.ContainsKey("F") || editor.OtherSources.Children.Count != 1)
                    {
                        fail("KeyMap动态源键生成", "新增 F ➔ . 未能在左侧 OtherSourcesPanel 生成动态按键 F");
                    }
                    if (!editor.TargetButtons.ContainsKey("OemPeriod") || editor.OtherTargets.Children.Count != 1)
                    {
                        fail("KeyMap动态目标键生成", "新增 F ➔ . 未能在右侧 OtherTargetsPanel 生成动态按键 OemPeriod");
                    }
                    var fBtn = editor.PhysicalButtons["F"];
                    var periodBtn = editor.TargetButtons["OemPeriod"];
                    if ((fBtn.Content as string) != "F" || (fBtn.ToolTip as string) != "F")
                    {
                        fail("KeyMap动态键属性", $"动态按键 F 显示或 Tooltip 不符: Content='{fBtn.Content}', ToolTip='{fBtn.ToolTip}'");
                    }
                    if ((periodBtn.Content as string) != "." || (periodBtn.ToolTip as string) != "OemPeriod")
                    {
                        fail("KeyMap动态句点键属性", $"动态按键 OemPeriod 显示或 Tooltip 不符: Content='{periodBtn.Content}', ToolTip='{periodBtn.ToolTip}'");
                    }

                    // ② 小键盘 Num . 与普通 . 正确区分
                    editor.Entries.Add(new KeyboardRemapEntry { FromKey = "G", ToKey = "NumDecimal" });
                    editor.SyncDynamicVisualKeys();
                    editor.RefreshVisualHighlights();

                    if (!editor.TargetButtons.ContainsKey("NumDecimal") || editor.OtherTargets.Children.Count != 2)
                    {
                        fail("KeyMap小键盘句点动态键", "未能正确生成 NumDecimal 动态按键");
                    }
                    var numDecBtn = editor.TargetButtons["NumDecimal"];
                    if ((numDecBtn.Content as string) != "Num ." || (numDecBtn.ToolTip as string) != "NumDecimal")
                    {
                        fail("KeyMap小键盘句点友好名与区分", $"NumDecimal 显示应为 'Num .' 且 Tooltip 为 'NumDecimal'，实际: Content='{numDecBtn.Content}', ToolTip='{numDecBtn.ToolTip}'");
                    }
                    if ((periodBtn.Content as string) != "." || (numDecBtn.Content as string) != "Num .")
                    {
                        fail("KeyMap句点与小键盘句点区分", "OemPeriod 与 NumDecimal 动态键显示名混淆");
                    }

                    // ③ Q、T 映射到 Num7 时点击 Num7 正确高亮两根关联
                    editor.Entries.Clear();
                    editor.Entries.Add(new KeyboardRemapEntry { FromKey = "Q", ToKey = "Num7" });
                    editor.Entries.Add(new KeyboardRemapEntry { FromKey = "T", ToKey = "Num7" });
                    editor.SyncDynamicVisualKeys();
                    editor.RefreshVisualHighlights();

                    // 点击目标键 Num7
                    editor.HandleTargetKeyClick("Num7");

                    if (editor.ActiveDiagramRelationships.Count != 2)
                    {
                        fail("KeyMap多对一关联识别", $"点击 Num7 应识别到 2 项映射关联，实际: {editor.ActiveDiagramRelationships.Count}");
                    }
                    if (!editor.ActiveDiagramRelationships.Any(r => r.FromKey == "Q" && r.ToKey == "Num7") ||
                        !editor.ActiveDiagramRelationships.Any(r => r.FromKey == "T" && r.ToKey == "Num7"))
                    {
                        fail("KeyMap多对一关联内容", "活跃映射关联未能同时包含 Q➔Num7 与 T➔Num7");
                    }
                    if (editor.MappingsDataGrid.SelectedItem != null)
                    {
                        fail("KeyMap多对一不擅自选中", "点击具有多对一映射的目标键时，不得在数据列表中擅自选中某一项");
                    }
                    if (editor.SourceRecorder.SelectedKey != "")
                    {
                        fail("KeyMap多对一源键录制框置空", $"多对一映射时源键录制框不应擅自填充单一源键，实际: '{editor.SourceRecorder.SelectedKey}'");
                    }
                    if (editor.TargetRecorder.SelectedKey != "Num7")
                    {
                        fail("KeyMap目标键点击填充", $"点击 Num7 后目标键录制框应填充 'Num7'，实际: '{editor.TargetRecorder.SelectedKey}'");
                    }

                    // 多对一关联提示词条与多语言回归（杜绝硬编码）
                    LanguageCode prevHintLang = I18n.CurrentLanguage;
                    try
                    {
                        I18n.CurrentLanguage = LanguageCode.ZhCn;
                        editor.HandleTargetKeyClick("Num7");
                        if (editor.MappingHintTextBlock.Text != "2 项映射 ➔ Num7")
                        {
                            fail("KeyMap多对一提示简中", $"简中多对一提示应为 '2 项映射 ➔ Num7'，实际: '{editor.MappingHintTextBlock.Text}'");
                        }

                        I18n.CurrentLanguage = LanguageCode.ZhTw;
                        editor.HandleTargetKeyClick("Num7");
                        if (editor.MappingHintTextBlock.Text != "2 項對應 ➔ Num7")
                        {
                            fail("KeyMap多对一提示繁中", $"繁中多对一提示应为 '2 項對應 ➔ Num7'，实际: '{editor.MappingHintTextBlock.Text}'");
                        }

                        I18n.CurrentLanguage = LanguageCode.En;
                        editor.HandleTargetKeyClick("Num7");
                        if (editor.MappingHintTextBlock.Text != "2 mappings ➔ Num7")
                        {
                            fail("KeyMap多对一提示英文", $"英文多对一提示应为 '2 mappings ➔ Num7'，实际: '{editor.MappingHintTextBlock.Text}'");
                        }

                        I18n.CurrentLanguage = LanguageCode.Ja;
                        editor.HandleTargetKeyClick("Num7");
                        if (editor.MappingHintTextBlock.Text != "2 件のマッピング ➔ Num7")
                        {
                            fail("KeyMap多对一提示日文", $"日文多对一提示应为 '2 件のマッピング ➔ Num7'，实际: '{editor.MappingHintTextBlock.Text}'");
                        }
                    }
                    finally
                    {
                        I18n.CurrentLanguage = prevHintLang;
                    }

                    // 多对一查看→保存不变回归
                    if (!editor.IsDiagramInspectionOnly)
                    {
                        fail("KeyMap多对一查看仅查看标记", "点击多对一目标键后 _isDiagramInspectionOnly 应标记为 true");
                    }
                    bool multiSaveOk = editor.TryCommitPendingMapping(out string? multiSaveErr);
                    if (!multiSaveOk || !string.IsNullOrEmpty(multiSaveErr))
                    {
                        fail("KeyMap多对一查看保存成功", $"点击多对一目标键后直接点保存应原样保存成功，实际失败: {multiSaveErr}");
                    }
                    if (editor.Entries.Count != 2 ||
                        !editor.Entries.Any(e => e.FromKey == "Q" && e.ToKey == "Num7") ||
                        !editor.Entries.Any(e => e.FromKey == "T" && e.ToKey == "Num7"))
                    {
                        fail("KeyMap多对一查看保存内容不变", "点击多对一目标键后保存未能原样保留原有映射");
                    }

                    // 未映射键查看→保存不变回归
                    // A. 未映射源键查看 (Z 当前未映射)
                    editor.HandleSourceKeyClick("Z");
                    if (!editor.IsDiagramInspectionOnly)
                    {
                        fail("KeyMap未映射源键查看标记", "点击未映射源键后 _isDiagramInspectionOnly 应为 true");
                    }
                    bool unmappedSrcSaveOk = editor.TryCommitPendingMapping(out string? unmappedSrcErr);
                    if (!unmappedSrcSaveOk || !string.IsNullOrEmpty(unmappedSrcErr))
                    {
                        fail("KeyMap未映射源键保存成功", $"点击未映射源键后保存应原样保存成功，实际失败: {unmappedSrcErr}");
                    }
                    if (editor.Entries.Count != 2 || editor.Entries.Any(e => e.FromKey == "Z"))
                    {
                        fail("KeyMap未映射源键保存无篡改", "未映射源键查看后保存意外新增了映射");
                    }

                    // B. 未映射目标键查看 (Num0 当前未映射)
                    editor.HandleTargetKeyClick("Num0");
                    if (!editor.IsDiagramInspectionOnly)
                    {
                        fail("KeyMap未映射目标键查看标记", "点击未映射目标键后 _isDiagramInspectionOnly 应为 true");
                    }
                    bool unmappedTgtSaveOk = editor.TryCommitPendingMapping(out string? unmappedTgtErr);
                    if (!unmappedTgtSaveOk || !string.IsNullOrEmpty(unmappedTgtErr))
                    {
                        fail("KeyMap未映射目标键保存成功", $"点击未映射目标键后保存应原样保存成功，实际失败: {unmappedTgtErr}");
                    }
                    if (editor.Entries.Count != 2 || editor.Entries.Any(e => e.ToKey == "Num0"))
                    {
                        fail("KeyMap未映射目标键保存无篡改", "未映射目标键查看后保存意外新增了映射");
                    }

                    // 明确编辑→保存生效回归
                    // A. 明确选择双键添加新映射 (Z➔Num0)
                    editor.SourceRecorder.SetKey("Z");
                    editor.TargetRecorder.SetKey("Num0");
                    bool explicitAddOk = editor.TryCommitPendingMapping(out string? explicitAddErr);
                    if (!explicitAddOk || !string.IsNullOrEmpty(explicitAddErr))
                    {
                        fail("KeyMap明确添加保存成功", $"明确选择源键与目标键后保存应成功，实际失败: {explicitAddErr}");
                    }
                    if (!editor.Entries.Any(e => e.FromKey == "Z" && e.ToKey == "Num0"))
                    {
                        fail("KeyMap明确添加生效", "明确选择源键与目标键后保存未能写入 _entries");
                    }

                    // B. 明确修改既有映射源键 (选中 Q➔Num7 后将源键改为 F)
                    editor.HandleSourceKeyClick("Q");
                    editor.SourceRecorder.SetKey("F");
                    bool explicitEditOk = editor.TryCommitPendingMapping(out string? explicitEditErr);
                    if (!explicitEditOk || !string.IsNullOrEmpty(explicitEditErr))
                    {
                        fail("KeyMap明确修改保存成功", $"修改既有映射源键后保存应成功，实际失败: {explicitEditErr}");
                    }
                    if (editor.Entries.Any(e => e.FromKey == "Q"))
                    {
                        fail("KeyMap旧映射移除", "源键改为 F 后，旧映射 Q➔Num7 仍残留于 _entries 中");
                    }
                    if (!editor.Entries.Any(e => e.FromKey == "F" && e.ToKey == "Num7"))
                    {
                        fail("KeyMap新映射生效", "源键改为 F 后，新映射 F➔Num7 未能在 _entries 中生效");
                    }

                    // ④ 修改或删除后孤立动态键消失
                    // 当前 _entries 包含动态源键 T。现将 T 移除，添加 W➔Num7 (固定物理键)
                    editor.Entries.Remove(editor.Entries.First(e => e.FromKey == "T"));
                    editor.Entries.Add(new KeyboardRemapEntry { FromKey = "W", ToKey = "Num7" });
                    // 也清理刚刚测试添加的 F 与 Z
                    editor.Entries.Remove(editor.Entries.First(e => e.FromKey == "F"));
                    editor.Entries.Remove(editor.Entries.First(e => e.FromKey == "Z"));
                    editor.SyncDynamicVisualKeys();
                    editor.RefreshVisualHighlights();

                    if (editor.PhysicalButtons.ContainsKey("T") || editor.OtherSources.Children.Count != 0)
                    {
                        fail("KeyMap修改后孤立动态键清理", "动态源键 T 被移除后，OtherSourcesPanel 仍残留孤立动态按键");
                    }
                    if (editor.OtherSourcesContainer.Visibility != System.Windows.Visibility.Collapsed)
                    {
                        fail("KeyMap动态源键容器折叠", "无动态源键时 OtherSourcesContainer 应保持 Collapsed");
                    }

                    // 进一步清空所有映射，验证目标键动态容器也完全清空折叠
                    editor.Entries.Clear();
                    editor.SyncDynamicVisualKeys();
                    if (editor.OtherTargets.Children.Count != 0 || editor.OtherTargetsContainer.Visibility != System.Windows.Visibility.Collapsed)
                    {
                        fail("KeyMap清空后动态目标键清理", "清空映射后 OtherTargetsPanel 与容器未能正确清空与折叠");
                    }

                    // ⑤ 画布缩放范围钳位 60%–200%
                    editor.ApplyZoom(0.2); // 极端缩小
                    if (Math.Abs(editor.CurrentZoom - 0.6) > 0.001)
                    {
                        fail("KeyMap缩放最小钳位", $"尝试缩放至 20% 时应被钳位在 60% (0.6)，实际为 {editor.CurrentZoom}");
                    }
                    editor.ApplyZoom(5.0); // 极端放大
                    if (Math.Abs(editor.CurrentZoom - 2.0) > 0.001)
                    {
                        fail("KeyMap缩放最大钳位", $"尝试缩放至 500% 时应被钳位在 200% (2.0)，实际为 {editor.CurrentZoom}");
                    }
                    editor.ApplyZoom(1.25); // 合法范围
                    if (Math.Abs(editor.CurrentZoom - 1.25) > 0.001)
                    {
                        fail("KeyMap合法缩放设置", $"设置 125% (1.25) 缩放未生效，实际为 {editor.CurrentZoom}");
                    }
                    editor.FitCanvas(); // 适应画布
                    if (editor.CurrentZoom < 0.6 || editor.CurrentZoom > 1.2)
                    {
                        fail("KeyMap适应画布缩放范围", $"适应画布后缩放倍率应在 [0.6, 1.2] 内，实际为 {editor.CurrentZoom}");
                    }

                    // ⑥ 允许上限 64 项合法映射与最小窗口尺寸下的 Fit 边界与绝对防裁切几何容纳验证
                    var sixtyFourKeys = new List<string>();
                    for (int i = 1; i <= 24; i++) sixtyFourKeys.Add($"F{i}"); // 24 个功能键
                    for (char c = 'A'; c <= 'Z'; c++) sixtyFourKeys.Add(c.ToString()); // 26 个字母键
                    for (int i = 0; i <= 9; i++) sixtyFourKeys.Add(i.ToString()); // 10 个数字键
                    sixtyFourKeys.Add(".");
                    sixtyFourKeys.Add(",");
                    sixtyFourKeys.Add("-");
                    sixtyFourKeys.Add("="); // 4 个常用符号键，合计正好 64 个互不重复合法源键

                    editor.Entries.Clear();
                    for (int i = 0; i < sixtyFourKeys.Count; i++)
                    {
                        editor.Entries.Add(new KeyboardRemapEntry
                        {
                            FromKey = sixtyFourKeys[i],
                            ToKey = $"Num{i % 10}"
                        });
                    }

                    // 验证这 64 项映射完全合法并符合上限规范
                    string? validationErr = KeyMapValidator.Validate(editor.Entries);
                    if (!string.IsNullOrEmpty(validationErr))
                    {
                        fail("KeyMap64项合法性校验", $"64 项映射规则应完全合法，实际校验失败: {validationErr}");
                    }

                    editor.SyncDynamicVisualKeys();
                    editor.RefreshVisualHighlights();

                    // 最小窗口尺寸下的视口几何 (窗口 MinWidth=660, MinHeight=520, Row 1 MinHeight=190 -> 视口约 600x136)
                    editor.Viewport.Width = 600;
                    editor.Viewport.Height = 136;
                    editor.FitCanvas();

                    if (editor.CurrentZoom >= 0.6)
                    {
                        fail("KeyMap64项Fit突破下限", $"64 项动态键时 Fit 缩放倍率应突破普通手动 60% 下限以容纳全部键位，实际为 {editor.CurrentZoom}");
                    }
                    if (editor.CurrentZoom < 0.01)
                    {
                        fail("KeyMapFit极限保护", $"Fit 缩放倍率不应低于安全保底 0.01，实际为 {editor.CurrentZoom}");
                    }

                    double fitContentW = Math.Max(editor.LayoutGrid.DesiredSize.Width, editor.LayoutGrid.ActualWidth);
                    double fitContentH = Math.Max(editor.LayoutGrid.DesiredSize.Height, editor.LayoutGrid.ActualHeight);
                    double scaledContentW = fitContentW * editor.CurrentZoom;
                    double scaledContentH = fitContentH * editor.CurrentZoom;

                    // 核心几何断言：内容绝对落在视口内部，杜绝任何裁切
                    double contentLeft = editor.TranslateX;
                    double contentTop = editor.TranslateY;
                    double contentRight = contentLeft + scaledContentW;
                    double contentBottom = contentTop + scaledContentH;

                    if (contentLeft < -0.01 || contentTop < -0.01 ||
                        contentRight > editor.Viewport.Width + 0.01 ||
                        contentBottom > editor.Viewport.Height + 0.01)
                    {
                        fail("KeyMap画布几何真正容纳",
                            $"Fit 后内容边界 ([{contentLeft:F1}, {contentTop:F1}] 到 [{contentRight:F1}, {contentBottom:F1}]) " +
                            $"超出视口几何尺寸 (0, 0 到 {editor.Viewport.Width:F1}, {editor.Viewport.Height:F1}) 发生裁切");
                    }

                    // ⑦ 验证普通手动缩放规则仍严格保持 [0.6, 2.0]
                    editor.ApplyZoom(0.3);
                    if (Math.Abs(editor.CurrentZoom - 0.6) > 0.001)
                    {
                        fail("KeyMap普通手动缩放下限保持", $"手动缩放应钳位在 0.6 下限，实际为 {editor.CurrentZoom}");
                    }
                    editor.ApplyZoom(2.5);
                    if (Math.Abs(editor.CurrentZoom - 2.0) > 0.001)
                    {
                        fail("KeyMap普通手动缩放上限保持", $"手动缩放应钳位在 2.0 上限，实际为 {editor.CurrentZoom}");
                    }

                    // ⑧ 小窗模式 (905x765) 与最小尺寸 (660x520) 真实布局与可见边界严密无界面回归
                    editor.Viewport.ClearValue(System.Windows.FrameworkElement.WidthProperty);
                    editor.Viewport.ClearValue(System.Windows.FrameworkElement.HeightProperty);
                    editor.Entries.Clear();
                    foreach (var entry in KeyMapCodec.GetDefaultSpatialPreset())
                    {
                        editor.Entries.Add(new KeyboardRemapEntry { FromKey = entry.FromKey, ToKey = entry.ToKey });
                    }
                    editor.SyncDynamicVisualKeys();
                    editor.RefreshVisualHighlights();

                    var rootVisual = (System.Windows.FrameworkElement)editor.Content;
                    rootVisual.Measure(new Size(905, 765));
                    rootVisual.Arrange(new Rect(0, 0, 905, 765));
                    rootVisual.UpdateLayout();
                    editor.FitCanvas();
                    rootVisual.UpdateLayout();

                    Action<System.Windows.FrameworkElement, string> assertInViewport = (elem, name) =>
                    {
                        System.Windows.Media.GeneralTransform xf = elem.TransformToAncestor(editor.Viewport);
                        Rect b = xf.TransformBounds(new Rect(0, 0, elem.ActualWidth, elem.ActualHeight));
                        double vpH = editor.Viewport.ActualHeight;
                        double vpW = editor.Viewport.ActualWidth;
                        if (b.Top < -0.01 || b.Bottom > vpH + 0.01 || b.Left < -0.01 || b.Right > vpW + 0.01)
                        {
                            fail($"KeyMap按键视口完全容纳_{name}",
                                $"{name} 边界 ([{b.Left:F1}, {b.Top:F1}] 到 [{b.Right:F1}, {b.Bottom:F1}]) 超出视口 ([0, 0] 到 [{vpW:F1}, {vpH:F1}]) 导致裁切");
                        }
                    };

                    // A. 初始默认预设下在 905x765 小窗：Num0 必须完全在视口内可见
                    assertInViewport(editor.Key_Num0, "小窗默认预设_Num0");

                    // B. 用户通过录入区依次添加 F➔Num7, T➔Num8, Tab➔Num9, V➔Num4
                    // 验证每次添加后自动触发适应画布，Num0 与所有新增源键均始终完整落在视口内
                    string[] newSourceKeys = { "F", "T", "Tab", "V" };
                    string[] newTargetKeys = { "Num7", "Num8", "Num9", "Num4" };
                    for (int i = 0; i < newSourceKeys.Length; i++)
                    {
                        editor.SourceRecorder.SetKey(newSourceKeys[i]);
                        editor.TargetRecorder.SetKey(newTargetKeys[i]);
                        editor.AddButton_Click(editor.AddBtn, new System.Windows.RoutedEventArgs());
                        rootVisual.Measure(new Size(905, 765));
                        rootVisual.Arrange(new Rect(0, 0, 905, 765));
                        rootVisual.UpdateLayout();

                        assertInViewport(editor.Key_Num0, $"添加_{newSourceKeys[i]}_后_Num0");
                        foreach (System.Windows.FrameworkElement dynBtn in editor.OtherSources.Children)
                        {
                            assertInViewport(dynBtn, $"添加_{newSourceKeys[i]}_后源键_{(dynBtn as Button)?.Content}");
                        }
                    }

                    // C. 进一步增加动态目标键 (1➔F1, 2➔F2)
                    editor.SourceRecorder.SetKey("1");
                    editor.TargetRecorder.SetKey("F1");
                    editor.AddButton_Click(editor.AddBtn, new System.Windows.RoutedEventArgs());
                    editor.SourceRecorder.SetKey("2");
                    editor.TargetRecorder.SetKey("F2");
                    editor.AddButton_Click(editor.AddBtn, new System.Windows.RoutedEventArgs());
                    rootVisual.Measure(new Size(905, 765));
                    rootVisual.Arrange(new Rect(0, 0, 905, 765));
                    rootVisual.UpdateLayout();

                    assertInViewport(editor.Key_Num0, "添加动态目标键后_Num0");
                    foreach (System.Windows.FrameworkElement dynSrc in editor.OtherSources.Children)
                    {
                        assertInViewport(dynSrc, $"双侧动态键后源键_{(dynSrc as Button)?.Content}");
                    }
                    foreach (System.Windows.FrameworkElement dynTgt in editor.OtherTargets.Children)
                    {
                        assertInViewport(dynTgt, $"双侧动态键后目标键_{(dynTgt as Button)?.Content}");
                    }

                    // D. 删除单条动态映射 (删除 2➔F2)
                    var entryToRemove = editor.Entries.FirstOrDefault(e => e.FromKey == "2");
                    if (entryToRemove != null)
                    {
                        editor.DeleteRow_Click(new Button { DataContext = entryToRemove }, new System.Windows.RoutedEventArgs());
                        rootVisual.Measure(new Size(905, 765));
                        rootVisual.Arrange(new Rect(0, 0, 905, 765));
                        rootVisual.UpdateLayout();

                        if (editor.Entries.Any(e => e.FromKey == "2"))
                        {
                            fail("KeyMap删除动态映射生效", "调用 DeleteRow_Click 后条目仍残留于 Entries");
                        }
                        assertInViewport(editor.Key_Num0, "删除动态条目后_Num0");
                    }

                    // E. 手动缩放交互保持：用户手动缩放到 150% 时，新增条目不得擅自破坏用户的缩放状态
                    editor.ApplyZoom(1.5);
                    double manualZoom = editor.CurrentZoom;
                    editor.SourceRecorder.SetKey("3");
                    editor.TargetRecorder.SetKey("F3");
                    editor.AddButton_Click(editor.AddBtn, new System.Windows.RoutedEventArgs());
                    rootVisual.Measure(new Size(905, 765));
                    rootVisual.Arrange(new Rect(0, 0, 905, 765));
                    rootVisual.UpdateLayout();

                    if (Math.Abs(editor.CurrentZoom - manualZoom) > 0.001)
                    {
                        fail("KeyMap手动缩放状态保持", $"用户手动缩放至 {manualZoom} 后，添加映射应保持用户倍率，实际被重置为 {editor.CurrentZoom}");
                    }

                    // F. 点击“适应画布”按钮：重置交互标记，并重新适配容纳全部键位
                    editor.FitCanvasButton_Click(editor.FitCanvasButton, new System.Windows.RoutedEventArgs());
                    rootVisual.Measure(new Size(905, 765));
                    rootVisual.Arrange(new Rect(0, 0, 905, 765));
                    rootVisual.UpdateLayout();

                    assertInViewport(editor.Key_Num0, "手动点击适应画布后_Num0");
                    foreach (System.Windows.FrameworkElement dynSrc in editor.OtherSources.Children)
                    {
                        assertInViewport(dynSrc, $"手动点击适应画布后源键_{(dynSrc as Button)?.Content}");
                    }
                    foreach (System.Windows.FrameworkElement dynTgt in editor.OtherTargets.Children)
                    {
                        assertInViewport(dynTgt, $"手动点击适应画布后目标键_{(dynTgt as Button)?.Content}");
                    }

                    // G. 最小窗口尺寸 (660x520) 下的极限无裁切容纳断言
                    rootVisual.Measure(new Size(660, 520));
                    rootVisual.Arrange(new Rect(0, 0, 660, 520));
                    rootVisual.UpdateLayout();
                    editor.FitCanvasButton_Click(editor.FitCanvasButton, new System.Windows.RoutedEventArgs());
                    rootVisual.UpdateLayout();

                    assertInViewport(editor.Key_Num0, "最小窗口660x520_Num0");
                    foreach (System.Windows.FrameworkElement dynSrc in editor.OtherSources.Children)
                    {
                        assertInViewport(dynSrc, $"最小窗口660x520源键_{(dynSrc as Button)?.Content}");
                    }
                    foreach (System.Windows.FrameworkElement dynTgt in editor.OtherTargets.Children)
                    {
                        assertInViewport(dynTgt, $"最小窗口660x520目标键_{(dynTgt as Button)?.Content}");
                    }

                    // H. 清空与预设恢复动作自动适配
                    editor.ClearButton_Click(editor.ClearButton, new System.Windows.RoutedEventArgs());
                    rootVisual.Measure(new Size(905, 765));
                    rootVisual.Arrange(new Rect(0, 0, 905, 765));
                    rootVisual.UpdateLayout();
                    if (editor.Entries.Count != 0 || editor.OtherSources.Children.Count != 0 || editor.OtherTargets.Children.Count != 0)
                    {
                        fail("KeyMap清空动作生效", "ClearButton_Click 后 Entries 或动态按键未完全清空");
                    }

                    editor.PresetButton_Click(editor.PresetButton, new System.Windows.RoutedEventArgs());
                    rootVisual.Measure(new Size(905, 765));
                    rootVisual.Arrange(new Rect(0, 0, 905, 765));
                    rootVisual.UpdateLayout();
                    if (editor.Entries.Count != 10)
                    {
                        fail("KeyMap预设恢复条目数", $"PresetButton_Click 后条目数应恢复为 10，实际为 {editor.Entries.Count}");
                    }
                    assertInViewport(editor.Key_Num0, "预设恢复后_Num0");

                    // ⑨ KeyMap 画布『点源键 ➔ 点目标键』直接配对交互完整无界面回归
                    // A. 直接点对点配置与4语系待配对提示
                    editor.Entries.Clear();
                    editor.SyncDynamicVisualKeys();
                    editor.RefreshVisualHighlights();

                    // 点击源键 Q，进入清晰等待目标键状态
                    editor.HandleSourceKeyClick("Q");
                    if (editor.PendingPairSourceKey != "Q")
                    {
                        fail("KeyMap待选源键标记", $"点击源键 Q 后 PendingPairSourceKey 应为 'Q'，实际为 '{editor.PendingPairSourceKey}'");
                    }
                    if (editor.SourceRecorder.SelectedKey != "Q")
                    {
                        fail("KeyMap待配对源键录制同步", $"点击源键 Q 后 SourceRecorder 应为 'Q'，实际为 '{editor.SourceRecorder.SelectedKey}'");
                    }
                    if (editor.Entries.Count != 0)
                    {
                        fail("KeyMap待配对不改动映射", "仅点击源键 Q 时不得修改或新增映射条目");
                    }

                    // 4 语系提示多语言（未映射源键等待目标键）
                    LanguageCode prevPairLang = I18n.CurrentLanguage;
                    try
                    {
                        I18n.CurrentLanguage = LanguageCode.ZhCn;
                        editor.HandleSourceKeyClick("Q");
                        if (editor.MappingHintTextBlock.Text != "Q ➔ ? (请选择目标键)")
                            fail("KeyMap待选目标提示简中", $"简中待选提示错误: '{editor.MappingHintTextBlock.Text}'");

                        I18n.CurrentLanguage = LanguageCode.ZhTw;
                        editor.HandleSourceKeyClick("Q");
                        if (editor.MappingHintTextBlock.Text != "Q ➔ ? (請選擇目標鍵)")
                            fail("KeyMap待选目标提示繁中", $"繁中待选提示错误: '{editor.MappingHintTextBlock.Text}'");

                        I18n.CurrentLanguage = LanguageCode.En;
                        editor.HandleSourceKeyClick("Q");
                        if (editor.MappingHintTextBlock.Text != "Q ➔ ? (Click target key to pair)")
                            fail("KeyMap待选目标提示英文", $"英文待选提示错误: '{editor.MappingHintTextBlock.Text}'");

                        I18n.CurrentLanguage = LanguageCode.Ja;
                        editor.HandleSourceKeyClick("Q");
                        if (editor.MappingHintTextBlock.Text != "Q ➔ ? (目標キーを選択してください)")
                            fail("KeyMap待选目标提示日文", $"日文待选提示错误: '{editor.MappingHintTextBlock.Text}'");
                    }
                    finally
                    {
                        I18n.CurrentLanguage = prevPairLang;
                    }

                    // 点击目标键 Num7：立即在内存建立映射 Q ➔ Num7
                    editor.HandleTargetKeyClick("Num7");
                    if (editor.PendingPairSourceKey != null)
                    {
                        fail("KeyMap配对完成清除标记", "配对完成后 PendingPairSourceKey 应重置为 null");
                    }
                    if (editor.Entries.Count != 1 || editor.Entries[0].FromKey != "Q" || editor.Entries[0].ToKey != "Num7")
                    {
                        fail("KeyMap点对点配置生效", "点击 Num7 后内存映射列表未能正确新增 Q ➔ Num7");
                    }
                    if (editor.MappingHintTextBlock.Text != "Q ➔ Num7")
                    {
                        fail("KeyMap配对成功提示", $"配对成功后提示文案应为 'Q ➔ Num7'，实际为 '{editor.MappingHintTextBlock.Text}'");
                    }
                    if (editor.ActiveDiagramRelationships.Count != 1)
                    {
                        fail("KeyMap配对关联线高亮", $"配对成功后关联关系数应为 1，实际为 {editor.ActiveDiagramRelationships.Count}");
                    }

                    // B. 已有源键换目标时只替换该源键映射，并验证已有映射时的 4 语系提示
                    try
                    {
                        I18n.CurrentLanguage = LanguageCode.ZhCn;
                        editor.HandleSourceKeyClick("Q");
                        if (editor.MappingHintTextBlock.Text != "Q ➔ ? (请选择目标键，当前: Num7)")
                            fail("KeyMap已有映射待选提示简中", $"简中待选提示错误: '{editor.MappingHintTextBlock.Text}'");

                        I18n.CurrentLanguage = LanguageCode.ZhTw;
                        editor.HandleSourceKeyClick("Q");
                        if (editor.MappingHintTextBlock.Text != "Q ➔ ? (請選擇目標鍵，目前: Num7)")
                            fail("KeyMap已有映射待选提示繁中", $"繁中待选提示错误: '{editor.MappingHintTextBlock.Text}'");

                        I18n.CurrentLanguage = LanguageCode.En;
                        editor.HandleSourceKeyClick("Q");
                        if (editor.MappingHintTextBlock.Text != "Q ➔ ? (Click target key to change, current: Num7)")
                            fail("KeyMap已有映射待选提示英文", $"英文待选提示错误: '{editor.MappingHintTextBlock.Text}'");

                        I18n.CurrentLanguage = LanguageCode.Ja;
                        editor.HandleSourceKeyClick("Q");
                        if (editor.MappingHintTextBlock.Text != "Q ➔ ? (目標キーを選択して変更、現在: Num7)")
                            fail("KeyMap已有映射待选提示日文", $"日文待选提示错误: '{editor.MappingHintTextBlock.Text}'");
                    }
                    finally
                    {
                        I18n.CurrentLanguage = prevPairLang;
                    }

                    // 为已有映射源键 Q 点击新目标 Num9：替换 Q ➔ Num9
                    editor.HandleTargetKeyClick("Num9");
                    if (editor.Entries.Count != 1 || editor.Entries[0].FromKey != "Q" || editor.Entries[0].ToKey != "Num9")
                    {
                        fail("KeyMap已有源键替换目标", "已有源键选择新目标时未能只替换该源键映射");
                    }

                    // C. 允许多个源键指向同一目标键（多对一配置）
                    editor.HandleSourceKeyClick("W");
                    editor.HandleTargetKeyClick("Num9");
                    if (editor.Entries.Count != 2 ||
                        !editor.Entries.Any(e => e.FromKey == "Q" && e.ToKey == "Num9") ||
                        !editor.Entries.Any(e => e.FromKey == "W" && e.ToKey == "Num9"))
                    {
                        fail("KeyMap多对一配置", "不同源键 Q 和 W 未能成功同时指向同一目标键 Num9");
                    }

                    // D. 点相同目标不得产生重复项
                    editor.HandleSourceKeyClick("Q");
                    editor.HandleTargetKeyClick("Num9");
                    if (editor.Entries.Count != 2 || editor.Entries.Count(e => e.FromKey == "Q") != 1)
                    {
                        fail("KeyMap相同目标防重复", "对已有映射源键重复点击同一目标产生了重复映射项");
                    }

                    // E. 未先选择源键时，单击目标键保持现有“查看关联”行为，绝不新增或修改映射
                    editor.HandleTargetKeyClick("Num9");
                    if (editor.PendingPairSourceKey != null)
                    {
                        fail("KeyMap未选源键查关联标记", "未选源键点击目标键时不应进入配对状态");
                    }
                    if (editor.Entries.Count != 2)
                    {
                        fail("KeyMap未选源键查关联无增改", "未先选择源键单点目标键意外篡改了条目数");
                    }
                    if (editor.ActiveDiagramRelationships.Count != 2)
                    {
                        fail("KeyMap未选源键查关联关系数", $"单点 Num9 应查看所有关联（2项），实际为 {editor.ActiveDiagramRelationships.Count}");
                    }
                    bool inspectSaveOk = editor.TryCommitPendingMapping(out string? inspectSaveErr);
                    if (!inspectSaveOk || !string.IsNullOrEmpty(inspectSaveErr))
                    {
                        fail("KeyMap查关联后直接保存成功", $"单点目标键后直接保存应成功，实际错误: {inspectSaveErr}");
                    }
                    if (editor.Entries.Count != 2)
                    {
                        fail("KeyMap查关联后直接保存无改动", "单点目标键后直接保存意外修改了映射");
                    }

                    // F. 只点源键后直接保存，绝不意外改动或报错
                    editor.HandleSourceKeyClick("E");
                    if (editor.PendingPairSourceKey != "E")
                    {
                        fail("KeyMap只点源键待配对标记", "HandleSourceKeyClick('E') 未进入待选目标状态");
                    }
                    bool srcOnlySaveOk = editor.TryCommitPendingMapping(out string? srcOnlySaveErr);
                    if (!srcOnlySaveOk || !string.IsNullOrEmpty(srcOnlySaveErr))
                    {
                        fail("KeyMap只点源键直接保存成功", $"只点源键直接保存应安全成功放行，实际错误: {srcOnlySaveErr}");
                    }
                    if (editor.Entries.Count != 2 || editor.Entries.Any(e => e.FromKey == "E"))
                    {
                        fail("KeyMap只点源键直接保存无意外改动", "只点源键直接保存意外添加了半成品条目");
                    }

                    // G. 核心防回归：切换源键不会删错行（点另一个源键应切换待选源键，绝不误改其他行）
                    // 场景：已有 Q➔Num9, W➔Num9。用户先点 A (未映射)，再改主意点 D (未映射)，再点 Num6
                    editor.HandleSourceKeyClick("A");
                    if (editor.PendingPairSourceKey != "A") fail("KeyMap切换源键1", "先点 A 未置待选");
                    editor.HandleSourceKeyClick("D");
                    if (editor.PendingPairSourceKey != "D") fail("KeyMap切换源键2", "改点 D 未切换待选源键");
                    if (editor.Entries.Count != 2 || editor.Entries.Any(e => e.FromKey == "A"))
                    {
                        fail("KeyMap切换源键未配对行无残留", "切换源键过程中产生了非法残留项");
                    }
                    editor.HandleTargetKeyClick("Num6");
                    if (editor.Entries.Count != 3 || !editor.Entries.Any(e => e.FromKey == "D" && e.ToKey == "Num6"))
                    {
                        fail("KeyMap切换源键后配对成功", "切换到 D 后配对 Num6 未能生效");
                    }
                    if (!editor.Entries.Any(e => e.FromKey == "Q" && e.ToKey == "Num9") ||
                        !editor.Entries.Any(e => e.FromKey == "W" && e.ToKey == "Num9"))
                    {
                        fail("KeyMap切换源键不删错行_未映射切已映射", "切换源键导致已有的 Q➔Num9 或 W➔Num9 丢失");
                    }

                    // 场景：在已有映射的源键间连续切换，再配对目标键，确保原条目绝不误删
                    editor.HandleSourceKeyClick("Q");
                    editor.HandleSourceKeyClick("W");
                    editor.HandleSourceKeyClick("D"); // 最终留在 D
                    editor.HandleTargetKeyClick("Num4"); // 将 D 换为 Num4
                    if (editor.Entries.Count != 3)
                    {
                        fail("KeyMap连续切换源键条目数", $"连续切换已有源键后条目数应为 3，实际为 {editor.Entries.Count}");
                    }
                    if (!editor.Entries.Any(e => e.FromKey == "Q" && e.ToKey == "Num9") ||
                        !editor.Entries.Any(e => e.FromKey == "W" && e.ToKey == "Num9") ||
                        !editor.Entries.Any(e => e.FromKey == "D" && e.ToKey == "Num4"))
                    {
                        fail("KeyMap连续切换源键不删错行", "在已有源键间切换导致其他已有行被意外删除或覆盖");
                    }

                    // H. 取消、列表选中、录键框编辑、清空、恢复预设等操作妥善结束待配对状态
                    editor.HandleSourceKeyClick("Q");
                    editor.MappingsDataGrid.SelectedItem = editor.Entries[2];
                    if (editor.PendingPairSourceKey != null)
                    {
                        fail("KeyMap列表选中结束待配对", "列表选中行后待配对状态未结束");
                    }

                    editor.HandleSourceKeyClick("Q");
                    editor.SourceRecorder.StartRecording();
                    if (editor.PendingPairSourceKey != null)
                    {
                        fail("KeyMap录键框录制结束待配对", "录键框开始录制后待配对状态未结束");
                    }
                    editor.SourceRecorder.CancelRecording();

                    editor.HandleSourceKeyClick("Q");
                    editor.ClearButton_Click(editor.ClearButton, new System.Windows.RoutedEventArgs());
                    if (editor.PendingPairSourceKey != null || editor.Entries.Count != 0)
                    {
                        fail("KeyMap清空结束待配对", "点击清空后待配对状态未结束或映射未清空");
                    }

                    editor.HandleSourceKeyClick("Q");
                    editor.PresetButton_Click(editor.PresetButton, new System.Windows.RoutedEventArgs());
                    if (editor.PendingPairSourceKey != null || editor.Entries.Count != 10)
                    {
                        fail("KeyMap预设恢复结束待配对", "点击预设恢复后待配对状态未结束或条目数不对");
                    }

                    // I. 画布动态键直接点对点配置
                    editor.Entries.Clear();
                    editor.Entries.Add(new KeyboardRemapEntry { FromKey = "F", ToKey = "F1" });
                    editor.Entries.Add(new KeyboardRemapEntry { FromKey = "T", ToKey = "F2" });
                    editor.SyncDynamicVisualKeys();
                    editor.RefreshVisualHighlights();

                    if (!editor.PhysicalButtons.ContainsKey("F") || !editor.TargetButtons.ContainsKey("F2"))
                    {
                        fail("KeyMap动态按键存在性", "动态源键 F 或动态目标键 F2 未在画布中生成");
                    }

                    // 点动态源键 F -> 点动态目标键 F2
                    editor.HandleSourceKeyClick("F");
                    if (editor.PendingPairSourceKey != "F") fail("KeyMap动态源键点击标记", "点动态源键 F 未进入待配对状态");
                    editor.HandleTargetKeyClick("F2");
                    if (editor.Entries.First(e => e.FromKey == "F").ToKey != "F2")
                    {
                        fail("KeyMap动态键点对点配置", "动态源键 F ➔ 动态目标键 F2 配对未成功");
                    }

                    // J. 校验失败显示错误并保留原数据
                    // 构造达到 64 上限的条目集合，再尝试点对点添加第 65 项
                    editor.Entries.Clear();
                    for (int i = 0; i < sixtyFourKeys.Count; i++)
                    {
                        editor.Entries.Add(new KeyboardRemapEntry
                        {
                            FromKey = sixtyFourKeys[i],
                            ToKey = $"Num{i % 10}"
                        });
                    }
                    editor.SyncDynamicVisualKeys();
                    editor.RefreshVisualHighlights();

                    // 此时已有 64 项，尝试用未映射的 "Tab" 配对 "Num0"（将超出 64 上限）
                    editor.HandleSourceKeyClick("Tab");
                    editor.HandleTargetKeyClick("Num0");
                    if (editor.Entries.Count != 64)
                    {
                        fail("KeyMap校验失败条目数保持", $"校验失败时条目数应保持 64，实际为 {editor.Entries.Count}");
                    }
                    if (string.IsNullOrEmpty(editor.StatusText.Text))
                    {
                        fail("KeyMap校验失败错误提示", "校验失败时 StatusText 应显示错误提示，实际为空");
                    }
                    if (editor.Entries.Any(e => e.FromKey == "Tab"))
                    {
                        fail("KeyMap校验失败不残留条目", "校验失败项不应写入 Entries");
                    }

                    // K. 引导文字居中完整换行、适度加宽与小窗80%缩放无遮挡回归断言
                    // 1. 验证控件属性：换行开启、居中对齐、中间列加宽
                    if (editor.MappingHintTextBlock.TextWrapping != System.Windows.TextWrapping.Wrap)
                    {
                        fail("KeyMap提示换行配置", "MappingHintTextBlock.TextWrapping 应为 Wrap");
                    }
                    if (editor.MappingHintTextBlock.TextAlignment != System.Windows.TextAlignment.Center)
                    {
                        fail("KeyMap提示居中对齐配置", "MappingHintTextBlock.TextAlignment 应为 Center");
                    }
                    double centerColWidth = editor.LayoutGrid.ColumnDefinitions[1].Width.Value;
                    if (centerColWidth < 140)
                    {
                        fail("KeyMap中间列宽度加宽", $"中间列宽度应适度加宽至 >= 140px，实际为: {centerColWidth}");
                    }

                    // 2. 在小窗尺寸 (905x765) 与最小尺寸 (660x520) 以及约 80% 缩放比例下，
                    // 遍历 4 种语言文案及较长动态键名称，严密断言提示文字绝不与左右两侧键区产生几何遮挡或重叠
                    editor.Entries.Clear();
                    foreach (var entry in KeyMapCodec.GetDefaultSpatialPreset())
                    {
                        editor.Entries.Add(new KeyboardRemapEntry { FromKey = entry.FromKey, ToKey = entry.ToKey });
                    }
                    editor.SyncDynamicVisualKeys();
                    editor.RefreshVisualHighlights();

                    var testWindowSizes = new[] { new Size(905, 765), new Size(660, 520) };
                    var testZooms = new[] { 1.0, 0.8 };
                    var testLanguages = new[] { LanguageCode.ZhCn, LanguageCode.ZhTw, LanguageCode.En, LanguageCode.Ja };

                    LanguageCode savedLang = I18n.CurrentLanguage;
                    try
                    {
                        foreach (var winSize in testWindowSizes)
                        {
                            rootVisual.Measure(winSize);
                            rootVisual.Arrange(new Rect(0, 0, winSize.Width, winSize.Height));
                            rootVisual.UpdateLayout();
                            editor.FitCanvas();
                            rootVisual.UpdateLayout();

                            foreach (var zoom in testZooms)
                            {
                                editor.ApplyZoom(zoom);
                                rootVisual.UpdateLayout();

                                foreach (var lang in testLanguages)
                                {
                                    I18n.CurrentLanguage = lang;
                                    editor.HandleSourceKeyClick("Q");
                                    rootVisual.UpdateLayout();

                                    var gridObj = editor.LayoutGrid;
                                    var hintObj = editor.MappingHintTextBlock;

                                    // 计算左侧所有按键的最大 Right 边界，以及右侧所有按键的最小 Left 边界（在 LayoutGrid 坐标系内）
                                    double maxLeftKeyRight = double.MinValue;
                                    foreach (var btn in editor.PhysicalButtons.Values)
                                    {
                                        if (btn.Visibility != System.Windows.Visibility.Visible) continue;
                                        System.Windows.Media.GeneralTransform xf = btn.TransformToAncestor(gridObj);
                                        Rect b = xf.TransformBounds(new Rect(0, 0, btn.ActualWidth, btn.ActualHeight));
                                        if (b.Right > maxLeftKeyRight) maxLeftKeyRight = b.Right;
                                    }

                                    double minRightKeyLeft = double.MaxValue;
                                    foreach (var btn in editor.TargetButtons.Values)
                                    {
                                        if (btn.Visibility != System.Windows.Visibility.Visible) continue;
                                        System.Windows.Media.GeneralTransform xf = btn.TransformToAncestor(gridObj);
                                        Rect b = xf.TransformBounds(new Rect(0, 0, btn.ActualWidth, btn.ActualHeight));
                                        if (b.Left < minRightKeyLeft) minRightKeyLeft = b.Left;
                                    }

                                    System.Windows.Media.GeneralTransform xfHint = hintObj.TransformToAncestor(gridObj);
                                    Rect bHint = xfHint.TransformBounds(new Rect(0, 0, hintObj.ActualWidth, hintObj.ActualHeight));

                                    // 核心防遮挡断言：提示文字左边界不得侵入左侧键区，右边界不得侵入右侧键区
                                    if (bHint.Left < maxLeftKeyRight - 0.01)
                                    {
                                        fail("KeyMap提示遮挡左侧键区",
                                            $"[尺寸 {winSize.Width}x{winSize.Height}, 缩放 {zoom:P0}, 语言 {lang}] 提示文字左边界 {bHint.Left:F1} 侵入左侧键区 (最右 {maxLeftKeyRight:F1}) 发生遮挡: '{hintObj.Text}'");
                                    }
                                    if (bHint.Right > minRightKeyLeft + 0.01)
                                    {
                                        fail("KeyMap提示遮挡右侧键区",
                                            $"[尺寸 {winSize.Width}x{winSize.Height}, 缩放 {zoom:P0}, 语言 {lang}] 提示文字右边界 {bHint.Right:F1} 侵入右侧键区 (最左 {minRightKeyLeft:F1}) 发生遮挡: '{hintObj.Text}'");
                                    }
                                }

                                // 测试较长动态按键名称 (BrowserBack ➔ VolumeUp)
                                editor.MappingHintTextBlock.Text = I18n.TF("KeyMapWaitingForTargetKeyWithCurrent", "BrowserBack", "VolumeUp");
                                rootVisual.UpdateLayout();
                                System.Windows.Media.GeneralTransform xfLongHint = editor.MappingHintTextBlock.TransformToAncestor(editor.LayoutGrid);
                                Rect bLongHint = xfLongHint.TransformBounds(new Rect(0, 0, editor.MappingHintTextBlock.ActualWidth, editor.MappingHintTextBlock.ActualHeight));

                                double dynMaxLeftRight = double.MinValue;
                                foreach (var btn in editor.PhysicalButtons.Values)
                                {
                                    if (btn.Visibility != System.Windows.Visibility.Visible) continue;
                                    System.Windows.Media.GeneralTransform xf = btn.TransformToAncestor(editor.LayoutGrid);
                                    Rect b = xf.TransformBounds(new Rect(0, 0, btn.ActualWidth, btn.ActualHeight));
                                    if (b.Right > dynMaxLeftRight) dynMaxLeftRight = b.Right;
                                }

                                double dynMinRightLeft = double.MaxValue;
                                foreach (var btn in editor.TargetButtons.Values)
                                {
                                    if (btn.Visibility != System.Windows.Visibility.Visible) continue;
                                    System.Windows.Media.GeneralTransform xf = btn.TransformToAncestor(editor.LayoutGrid);
                                    Rect b = xf.TransformBounds(new Rect(0, 0, btn.ActualWidth, btn.ActualHeight));
                                    if (b.Left < dynMinRightLeft) dynMinRightLeft = b.Left;
                                }

                                if (bLongHint.Left < dynMaxLeftRight - 0.01 || bLongHint.Right > dynMinRightLeft + 0.01)
                                {
                                    fail("KeyMap长按键名提示遮挡",
                                        $"[尺寸 {winSize.Width}x{winSize.Height}, 缩放 {zoom:P0}] 较长动态按键名称提示 ([{bLongHint.Left:F1}, {bLongHint.Right:F1}]) 侵入按键区 ([左最右 {dynMaxLeftRight:F1}, 右最左 {dynMinRightLeft:F1}]): '{editor.MappingHintTextBlock.Text}'");
                                }
                            }
                        }
                    }
                    finally
                    {
                        I18n.CurrentLanguage = savedLang;
                    }

                    // 3. 验证手动缩放和平移不破坏连线且不重置视图
                    editor.ApplyZoom(0.8);
                    if (Math.Abs(editor.CurrentZoom - 0.8) > 0.001)
                    {
                        fail("KeyMap手动缩放保持80", $"手动缩放至 0.8 应保持，实际为 {editor.CurrentZoom}");
                    }
                    editor.HandleTargetKeyClick("Num7"); // 配对 Q➔Num7
                    if (editor.ActiveDiagramRelationships.Count != 1)
                    {
                        fail("KeyMap手动缩放下关联关系数", $"缩放下配对后关联关系数应为 1，实际为 {editor.ActiveDiagramRelationships.Count}");
                    }
                    if (Math.Abs(editor.CurrentZoom - 0.8) > 0.001)
                    {
                        fail("KeyMap缩放后配对不擅自重置视图", $"配对后缩放比例应保持 0.8，实际被重置为 {editor.CurrentZoom}");
                    }
                }

                editor.Close();
            }
            catch (Exception ex)
            {
                fail("KeyMap编辑器打开异常", $"打开 KeyMapEditorWindow 时抛出异常: {ex.Message}");
            }
        });

        // -------------------------------------------------------------------------
        // 回归 3：停用插件动作保留、类型下拉显示与不可用提示
        // -------------------------------------------------------------------------
        try
        {
            PluginRegistryStore.UpsertEntry(new PluginRegistryEntry
            {
                Id = "mock.disabled.plugin",
                Enabled = false,
                InstallPath = "mock.disabled.plugin",
            });

            if (!ActionTypeCatalog.ShouldShowPluginActionType())
            {
                fail("动作类型目录", "当登记簿中存在（即使已停用）插件时，ShouldShowPluginActionType 应返回 true");
            }

            var aggregatedTypes = ActionTypeCatalog.BuildAggregatedActionTypes();
            if (!aggregatedTypes.Any(t => string.Equals(t.Tag, PluginActionBinding.TypeName, StringComparison.OrdinalIgnoreCase)))
            {
                fail("动作类型目录", "BuildAggregatedActionTypes 未包含 PluginActionBinding.TypeName 选项");
            }

            var localizedTypes = ActionTypeCatalog.BuildLocalizedActionTypes();
            if (!localizedTypes.Any(t => string.Equals(t.Tag, PluginActionBinding.TypeName, StringComparison.OrdinalIgnoreCase)))
            {
                fail("动作类型目录", "BuildLocalizedActionTypes 未包含 PluginActionBinding.TypeName 选项");
            }

            var optionTypes = ActionTypeCatalog.BuildActionTypeOptions();
            if (!optionTypes.Any(t => string.Equals(t.Tag, PluginActionBinding.TypeName, StringComparison.OrdinalIgnoreCase)))
            {
                fail("动作类型目录", "BuildActionTypeOptions 未包含 PluginActionBinding.TypeName 选项");
            }
        }
        finally
        {
            PluginRegistryStore.RemoveEntry("mock.disabled.plugin");
        }

        var mockDisabledAction = new ActionItem
        {
            Type = PluginActionBinding.TypeName,
            PluginActionRef = new PluginActionRef
            {
                PluginId = "mock.disabled.plugin",
                ContributionId = "mockAction",
            },
            Name = "未启用的插件动作",
        };

        if (!PluginActionBinding.IsReferenceBroken(mockDisabledAction))
        {
            fail("插件引用失效判定", "不存在/已停用的插件动作引用未被正确识别为失效 (IsReferenceBroken)");
        }
        if (mockDisabledAction.PluginActionRef?.FullId != "mock.disabled.plugin.mockAction")
        {
            fail("插件动作引用持久性", "失效检测时不应修改或清除 PluginActionRef 原始引用");
        }

        var unavailableText = PluginActionPanelText.Unavailable(mockDisabledAction.PluginActionRef.FullId);
        if (string.IsNullOrWhiteSpace(unavailableText.Title) ||
            string.IsNullOrWhiteSpace(unavailableText.Detail) ||
            string.IsNullOrWhiteSpace(unavailableText.Hint) ||
            !unavailableText.Detail.Contains("mock.disabled.plugin.mockAction"))
        {
            fail("不可用提示文案", "PluginActionPanelText.Unavailable 未生成包含贡献点全 ID 与有效提示的文案");
        }

        // -------------------------------------------------------------------------
        // 回归 4：真实往返路径保护（插件动作 → 快捷键 → 插件动作）
        // 验证 SlotViewModel.Type 与 SettingsWindow 主扇区类型切换路径绝不丢失 PluginActionRef 与 ExtensionData.keyMap
        // -------------------------------------------------------------------------
        // 路径 A：SlotViewModel.Type 真实往返
        var slotAction = new ActionItem
        {
            Type = PluginActionBinding.TypeName,
            Name = "按键映射",
            PluginActionRef = new PluginActionRef
            {
                PluginId = "starpie.plugin.keypadlayer",
                ContributionId = "keypadLayer",
            },
            ExtensionData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["keyMap"] = "KeyQ=Numpad7;KeyW=Numpad8",
                ["targetProcess"] = "acad.exe"
            }
        };

        var slotVm = new SlotViewModel(0, 8, "上方", slotAction);
        // 执行真实往返：插件动作 → 快捷键 → 插件动作
        slotVm.Type = "Hotkey";
        slotVm.Type = PluginActionBinding.TypeName;

        if (slotAction.PluginActionRef == null || slotAction.PluginActionRef.FullId != "starpie.plugin.keypadlayer.keypadLayer")
        {
            fail("SlotViewModel类型往返", "SlotViewModel.Type 从'Plugin'切换为'Hotkey'再切回'Plugin'后，PluginActionRef 丢失");
        }
        if (slotAction.ExtensionData == null ||
            !slotAction.ExtensionData.TryGetValue("keyMap", out var slotMap) ||
            slotMap != "KeyQ=Numpad7;KeyW=Numpad8")
        {
            fail("SlotViewModel类型往返", "SlotViewModel.Type 从'Plugin'切换为'Hotkey'再切回'Plugin'后，ExtensionData.keyMap 丢失");
        }

        // 路径 B：SettingsWindow 主扇区类型切换路径真实往返
        var settingsAction = new ActionItem
        {
            Type = PluginActionBinding.TypeName,
            Name = "按键映射",
            PluginActionRef = new PluginActionRef
            {
                PluginId = "starpie.plugin.keypadlayer",
                ContributionId = "keypadLayer",
            },
            ExtensionData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["keyMap"] = "KeyQ=Numpad7;KeyW=Numpad8",
                ["targetProcess"] = "acad.exe"
            }
        };

        // 执行真实往返：主扇区类型切换路径从'Plugin' → 'Hotkey' → 'Plugin'
        SettingsWindow.SwitchFocusActionType(settingsAction, "Hotkey");
        SettingsWindow.SwitchFocusActionType(settingsAction, PluginActionBinding.TypeName);

        if (settingsAction.PluginActionRef == null || settingsAction.PluginActionRef.FullId != "starpie.plugin.keypadlayer.keypadLayer")
        {
            fail("SettingsWindow主扇区类型往返", "SettingsWindow主扇区类型切换路径从'Plugin'切换为'Hotkey'再切回'Plugin'后，PluginActionRef 丢失");
        }
        if (settingsAction.ExtensionData == null ||
            !settingsAction.ExtensionData.TryGetValue("keyMap", out var settingsMap) ||
            settingsMap != "KeyQ=Numpad7;KeyW=Numpad8")
        {
            fail("SettingsWindow主扇区类型往返", "SettingsWindow主扇区类型切换路径从'Plugin'切换为'Hotkey'再切回'Plugin'后，ExtensionData.keyMap 丢失");
        }

        // -------------------------------------------------------------------------
        // 回归 5：Win32 INPUT 结构原生对齐尺寸与注入失败安全路径（无真实按键注入）
        // -------------------------------------------------------------------------
        int expectedInputSize = IntPtr.Size == 8 ? 40 : 28;
        int expectedUnionSize = IntPtr.Size == 8 ? 32 : 24;

        int remapInputSize = Marshal.SizeOf<KeyboardRemapController.INPUT>();
        int remapUnionSize = Marshal.SizeOf<KeyboardRemapController.InputUnion>();
        int hookInputSize = Marshal.SizeOf<KeyboardHook.INPUT>();
        int hookUnionSize = Marshal.SizeOf<KeyboardHook.InputUnion>();

        if (remapInputSize != expectedInputSize)
        {
            fail("INPUT结构尺寸", $"KeyboardRemapController.INPUT 尺寸应为 {expectedInputSize} 字节，实际为 {remapInputSize} 字节");
        }
        if (remapUnionSize != expectedUnionSize)
        {
            fail("InputUnion结构尺寸", $"KeyboardRemapController.InputUnion 尺寸应为 {expectedUnionSize} 字节，实际为 {remapUnionSize} 字节");
        }
        if (hookInputSize != expectedInputSize)
        {
            fail("INPUT结构尺寸", $"KeyboardHook.INPUT 尺寸应为 {expectedInputSize} 字节，实际为 {hookInputSize} 字节");
        }
        if (hookUnionSize != expectedUnionSize)
        {
            fail("InputUnion结构尺寸", $"KeyboardHook.InputUnion 尺寸应为 {expectedUnionSize} 字节，实际为 {hookUnionSize} 字节");
        }

        // 验证 SendInput 格式化构造
        var formattedInput = KeyboardRemapController.FormatKeyboardInput(0x10, 0, isKeyUp: false);
        if (formattedInput.type != KeyboardRemapController.INPUT_KEYBOARD ||
            formattedInput.U.ki.wVk != 0 ||
            formattedInput.U.ki.wScan != 0x10 ||
            (formattedInput.U.ki.dwFlags & KeyboardRemapController.KEYEVENTF_SCANCODE) == 0 ||
            formattedInput.U.ki.dwExtraInfo != KeyboardHook.StarPieExtraInfo)
        {
            fail("INPUT格式化", "FormatKeyboardInput 未正确构造硬件扫描码注入结构体");
        }

        // 验证注入失败路径：不得吞没物理按键，且诊断计数与错误码正确更新（使用 mock 拦截，无真实按键注入）
        try
        {
            controller.SetTestForegroundProcess(normalHwnd, currentProcId);
            var optFailTest = new KeyboardRemapOptions
            {
                TargetProcessName = currentProcName,
                KeyMap = "v1|Q:Num7",
            };
            var resFailTest = controller.Activate("test-injection-fail-plugin", optFailTest);
            if (!resFailTest.Success)
            {
                fail("注入失败回归", $"激活测试映射失败: {resFailTest.Message}");
            }

            controller.ResetInjectionDiagnostics();
            // 模拟 SendInput 失败（返回 false，不调用系统 SendInput，无真实按键注入）
            controller.SetTestInjectionFailureMock((scan, flags, isKeyUp) => false);

            // 1. 物理 KeyDown 阶段：注入失败，必须放行物理源按键（返回 false），且不可吞没
            bool swallowedDown = controller.TryProcessHookEvent((uint)'Q', 256 /* WM_KEYDOWN */, 0, 0);
            if (swallowedDown)
            {
                fail("注入失败放行", "SendInput 注入失败时，宿主不得吞没物理按键，必须返回 false 放行！");
            }

            var (succAfterFail, failAfterFail, errAfterFail) = controller.GetInjectionDiagnostics();
            if (failAfterFail != 1 || succAfterFail != 0)
            {
                fail("注入失败诊断统计", $"注入失败后 FailureCount 应为 1，SuccessCount 应为 0，实际: fail={failAfterFail}, succ={succAfterFail}");
            }
            if (errAfterFail != 87)
            {
                fail("注入失败诊断错误码", $"注入失败后 LastWin32Error 应为 87 (ERROR_INVALID_PARAMETER)，实际为 {errAfterFail}");
            }

            // 2. 物理 KeyUp 阶段：由于 KeyDown 注入失败已回滚状态，物理 KeyUp 同样必须放行
            bool swallowedUp = controller.TryProcessHookEvent((uint)'Q', 257 /* WM_KEYUP */, 0, 0);
            if (swallowedUp)
            {
                fail("注入失败KeyUp放行", "KeyDown 注入失败后物理 KeyUp 必须原样放行，不得吞没！");
            }

            // 3. 正常注入路径（通过测试下沉拦截，无真实按键注入）：注入成功应返回 true 并累加 SuccessCount
            controller.SetTestInjectionFailureMock(null);
            controller.ResetInjectionDiagnostics();
            int sinkEvents = 0;
            controller.SetTestEventSink((scan, flags, isKeyUp) => { sinkEvents++; });

            bool okDown = controller.TryProcessHookEvent((uint)'Q', 256 /* WM_KEYDOWN */, 0, 0);
            if (!okDown)
            {
                fail("注入成功路径", "正常注入时应返回 true（吞没物理按键）");
            }
            var (succOk, failOk, _) = controller.GetInjectionDiagnostics();
            if (succOk != 1 || failOk != 0 || sinkEvents != 1)
            {
                fail("注入成功诊断统计", $"正常注入后 SuccessCount 应为 1 (实际 {succOk}), sinkEvents={sinkEvents}");
            }

            bool okUp = controller.TryProcessHookEvent((uint)'Q', 257 /* WM_KEYUP */, 0, 0);
            if (!okUp)
            {
                fail("注入成功路径", "正常松开注入时应返回 true（吞没物理按键松开）");
            }

            // 4. 验证诊断报告格式
            string reportText = controller.GetDiagnosticsReport();
            if (string.IsNullOrEmpty(reportText) || !reportText.Contains("Success=") || !reportText.Contains("Failure="))
            {
                fail("注入诊断报告", $"GetDiagnosticsReport() 输出格式不符合预期: {reportText}");
            }
        }
        finally
        {
            controller.Deactivate("test-injection-fail-plugin");
            controller.SetTestInjectionFailureMock(null);
            controller.SetTestEventSink(null);
            controller.ClearTestForegroundProcess();
            controller.ResetInjectionDiagnostics();
        }

        // -------------------------------------------------------------------------
        // 回归 6：键盘映射失败路径的三种关键事件序列状态机安全断言
        // ① 目标 KeyDown 成功而 KeyUp 注入失败时，不能清掉唯一的待释放记录后声称避免卡键；
        // ② 首次注入失败后，同一次物理长按的重复事件和 KeyUp 必须保持原键放行，不能中途切换成映射；
        // ③ 映射已开始后重复注入失败，不能放行不成对的原字母 KeyDown。
        // -------------------------------------------------------------------------
        try
        {
            controller.SetTestForegroundProcess(normalHwnd, currentProcId);
            var optSeq = new KeyboardRemapOptions
            {
                TargetProcessName = currentProcName,
                KeyMap = "v1|Q:Num7",
            };
            var resSeq = controller.Activate("test-seq-plugin", optSeq);
            if (!resSeq.Success)
            {
                fail("序列状态机回归", $"激活测试映射失败: {resSeq.Message}");
            }

            // =====================================================================
            // 序列 ①：目标 KeyDown 成功而 KeyUp 注入失败时，不能清掉唯一的待释放记录
            // =====================================================================
            controller.ResetInjectionDiagnostics();
            controller.SetTestEventSink((scan, flags, isKeyUp) => { });
            controller.SetTestInjectionFailureMock((scan, flags, isKeyUp) => true); // KeyDown 成功
            bool seq1Down = controller.TryProcessHookEvent((uint)'Q', 256 /* WM_KEYDOWN */, 0, 0);
            if (!seq1Down)
            {
                fail("序列①-KeyDown", "目标 KeyDown 注入成功时应返回 true（吞没物理源按键）");
            }
            if (controller.GetTargetHoldCount(0x67 /* Num7 */) != 1)
            {
                fail("序列①-待释放记录", $"首次按下成功后目标键 hold count 应为 1，实际为 {controller.GetTargetHoldCount(0x67)}");
            }

            // KeyUp 注入失败（mock 返回 false）
            controller.SetTestInjectionFailureMock((scan, flags, isKeyUp) => !isKeyUp);
            bool seq1Up = controller.TryProcessHookEvent((uint)'Q', 257 /* WM_KEYUP */, 0, 0);
            if (!seq1Up)
            {
                fail("序列①-KeyUp吞没", "目标 KeyDown 已成功映射而 KeyUp 注入失败时，绝不能放行不成对的原字母 KeyUp，必须返回 true！");
            }
            if (controller.GetTargetHoldCount(0x67 /* Num7 */) != 1)
            {
                fail("序列①-待释放记录丢失", $"KeyUp 注入失败后，待释放记录被清空 (count={controller.GetTargetHoldCount(0x67)})，未能保留为 1 供后续清理释放！");
            }

            // 当会话停用时，ReleaseAllHeldTargetKeys 必须依然能补发该目标键的释放
            int seq1DeactReleases = 0;
            controller.SetTestInjectionFailureMock((scan, flags, isKeyUp) => true); // 允许释放成功
            controller.SetTestEventSink((scan, flags, isKeyUp) => { if (isKeyUp) seq1DeactReleases++; });
            controller.Deactivate("test-seq-plugin");
            if (seq1DeactReleases != 1)
            {
                fail("序列①-停用补发释放", $"停用时应为未成功松开的目标键补发释放，实际补发次数: {seq1DeactReleases}");
            }
            controller.SetTestEventSink((scan, flags, isKeyUp) => { });

            // =====================================================================
            // 序列 ②：首次注入失败后，同一次物理长按的重复事件和 KeyUp 必须保持原键放行，不能中途切换成映射
            // =====================================================================
            resSeq = controller.Activate("test-seq-plugin", optSeq);
            if (!resSeq.Success)
            {
                fail("序列状态机回归", $"激活测试映射失败: {resSeq.Message}");
            }
            controller.ResetInjectionDiagnostics();

            // 1. 首次按下 Q：注入失败（mock 返回 false）
            controller.SetTestInjectionFailureMock((scan, flags, isKeyUp) => false);
            bool seq2FirstDown = controller.TryProcessHookEvent((uint)'Q', 256 /* WM_KEYDOWN */, 0, 0);
            if (seq2FirstDown)
            {
                fail("序列②-首次Down放行", "首次注入失败时必须返回 false 放行物理源按键！");
            }

            // 2. 模拟底层临时故障恢复（mock 恢复为 true）
            controller.SetTestInjectionFailureMock((scan, flags, isKeyUp) => true);

            // 3. 同一次物理长按中产生的自动重复事件（auto-repeat KeyDown）
            bool seq2RepeatDown = controller.TryProcessHookEvent((uint)'Q', 256 /* WM_KEYDOWN */, 0, 0);
            if (seq2RepeatDown)
            {
                fail("序列②-重复Down保持放行", "首次注入失败后，同一次物理长按的重复事件必须保持原键放行（返回 false），不能中途切换成映射！");
            }

            // 4. 同一次物理长按松开（KeyUp）
            bool seq2Up = controller.TryProcessHookEvent((uint)'Q', 257 /* WM_KEYUP */, 0, 0);
            if (seq2Up)
            {
                fail("序列②-KeyUp保持放行", "首次注入失败后，同一次物理长按的 KeyUp 必须保持原键放行（返回 false），不能被吞没！");
            }

            // 5. 新一轮独立的物理按键（全新 KeyDown）：直通状态应已解除，应正常进入映射
            bool seq2NewDown = controller.TryProcessHookEvent((uint)'Q', 256 /* WM_KEYDOWN */, 0, 0);
            if (!seq2NewDown)
            {
                fail("序列②-新按键恢复映射", "同一次长按结束后，新一次物理按键应恢复正常映射并吞没物理键（返回 true）！");
            }
            controller.TryProcessHookEvent((uint)'Q', 257 /* WM_KEYUP */, 0, 0);
            controller.Deactivate("test-seq-plugin");

            // =====================================================================
            // 序列 ③：映射已开始后重复注入失败，不能放行不成对的原字母 KeyDown
            // =====================================================================
            resSeq = controller.Activate("test-seq-plugin", optSeq);
            if (!resSeq.Success)
            {
                fail("序列状态机回归", $"激活测试映射失败: {resSeq.Message}");
            }
            controller.ResetInjectionDiagnostics();

            // 1. 首次按下 Q：注入成功
            controller.SetTestInjectionFailureMock((scan, flags, isKeyUp) => true);
            bool seq3FirstDown = controller.TryProcessHookEvent((uint)'Q', 256 /* WM_KEYDOWN */, 0, 0);
            if (!seq3FirstDown)
            {
                fail("序列③-首次Down", "首次按下注入成功应返回 true（吞没物理源按键）");
            }

            // 2. 长按中途重复注入失败（mock 设为 false）
            controller.SetTestInjectionFailureMock((scan, flags, isKeyUp) => false);
            bool seq3RepeatDown = controller.TryProcessHookEvent((uint)'Q', 256 /* WM_KEYDOWN */, 0, 0);
            if (!seq3RepeatDown)
            {
                fail("序列③-重复Down不能放行", "映射已开始后重复注入失败，绝不能放行不成对的原字母 KeyDown，必须返回 true 吞没！");
            }

            // 3. 恢复 mock 并松开
            controller.SetTestInjectionFailureMock((scan, flags, isKeyUp) => true);
            bool seq3Up = controller.TryProcessHookEvent((uint)'Q', 257 /* WM_KEYUP */, 0, 0);
            if (!seq3Up)
            {
                fail("序列③-KeyUp", "正常松开应返回 true");
            }
        }
        finally
        {
            controller.Deactivate("test-seq-plugin");
            controller.SetTestInjectionFailureMock(null);
            controller.SetTestEventSink(null);
            controller.ClearTestForegroundProcess();
            controller.ResetInjectionDiagnostics();
        }

        // =====================================================================
        // SDK 次版本门禁与最低宿主版本门禁回归测试（旧宿主拒绝、新宿主接受）
        // =====================================================================
        // 1. ApiVersion 次版本高于宿主时必须明确拒绝 (ApiVersionMismatch)
        var manifestHigherMinor = new PluginManifest
        {
            SchemaVersion = 1,
            Id = "test.compat.higherminor",
            Name = "Test Higher Minor",
            Author = "Test Author",
            Description = "Test Description",
            License = "MIT",
            Version = "1.0.0",
            ApiVersion = $"{PluginApi.ApiVersionMajor}.{PluginApi.ApiVersionMinor + 1}",
            MinHostVersion = "1.0.0",
            Assembly = "Test.dll",
        };
        if (PluginManifestReader.Validate(manifestHigherMinor, out var failHigherMinor, out string errHigherMinor))
        {
            fail("SDK次版本门禁", $"ApiVersion 次版本高于宿主 ({manifestHigherMinor.ApiVersion} > {PluginApi.ApiVersion}) 应当被拒绝，却被放行！");
        }
        else if (failHigherMinor != PluginScanFailure.ApiVersionMismatch)
        {
            fail("SDK次版本门禁", $"ApiVersion 次版本高于宿主应当报告 ApiVersionMismatch，实际为 {failHigherMinor}: {errHigherMinor}");
        }

        // 2. ApiVersion 次版本等于或低于宿主时通过
        var manifestCurrentMinor = new PluginManifest
        {
            SchemaVersion = 1,
            Id = "test.compat.currentminor",
            Name = "Test Current Minor",
            Author = "Test Author",
            Description = "Test Description",
            License = "MIT",
            Version = "1.0.0",
            ApiVersion = PluginApi.ApiVersion,
            MinHostVersion = "1.0.0",
            Assembly = "Test.dll",
        };
        if (!PluginManifestReader.Validate(manifestCurrentMinor, out var failCurrentMinor, out string errCurrentMinor))
        {
            fail("SDK次版本门禁", $"当前 ApiVersion ({PluginApi.ApiVersion}) 校验应当通过，实际失败 ({failCurrentMinor}): {errCurrentMinor}");
        }

        var manifestLowerMinor = new PluginManifest
        {
            SchemaVersion = 1,
            Id = "test.compat.lowerminor",
            Name = "Test Lower Minor",
            Author = "Test Author",
            Description = "Test Description",
            License = "MIT",
            Version = "1.0.0",
            ApiVersion = $"{PluginApi.ApiVersionMajor}.0",
            MinHostVersion = "1.0.0",
            Assembly = "Test.dll",
        };
        if (!PluginManifestReader.Validate(manifestLowerMinor, out var failLowerMinor, out string errLowerMinor))
        {
            fail("SDK次版本门禁", $"低次版本 ApiVersion ({manifestLowerMinor.ApiVersion}) 向下兼容应当通过，实际失败 ({failLowerMinor}): {errLowerMinor}");
        }

        // 3. SimpleVersion.SatisfiesMinimum 与宿主版本断言：旧宿主 1.8.0-beta.2 拒绝 1.8.0-beta.3，新宿主 1.8.0-beta.3 接受 1.8.0-beta.3
        if (!SimpleVersion.TryParse("1.8.0-beta.2", out var oldHostVer) ||
            !SimpleVersion.TryParse("1.8.0-beta.3", out var newHostVer) ||
            !SimpleVersion.TryParse("1.8.0-beta.3", out var pluginMinVer) ||
            !SimpleVersion.TryParse("1.8.0", out var stableMinVer))
        {
            fail("版本门禁", "解析测试版本号失败");
        }
        else
        {
            // 旧宿主 1.8.0-beta.2 对比插件要求的 minHostVersion 1.8.0-beta.3：必须拒绝！
            bool oldHostSatisfies = SimpleVersion.SatisfiesMinimum(oldHostVer, pluginMinVer);
            if (oldHostSatisfies)
            {
                fail("版本门禁", "旧宿主 1.8.0-beta.2 不满足插件最低版本 1.8.0-beta.3，但 SatisfiesMinimum 错误返回 true！");
            }

            // 新宿主 1.8.0-beta.3 对比插件要求的 minHostVersion 1.8.0-beta.3：必须接受！
            bool newHostSatisfies = SimpleVersion.SatisfiesMinimum(newHostVer, pluginMinVer);
            if (!newHostSatisfies)
            {
                fail("版本门禁", "新宿主 1.8.0-beta.3 满足插件最低版本 1.8.0-beta.3，但 SatisfiesMinimum 错误返回 false！");
            }

            // 既有兼容规则仍保留：同号预发布宿主可满足不带预发布标识的正式版下界。
            if (!SimpleVersion.SatisfiesMinimum(oldHostVer, stableMinVer))
            {
                fail("版本门禁", "宿主 1.8.0-beta.2 应兼容插件最低版本 1.8.0，但 SatisfiesMinimum 错误返回 false！");
            }
        }

        // 回归断言：--test-instance / --plugin-selftest 模式下 EnsureAutoStartRegistryUpToDate 绝不读写 HKCU 开机启动项
        bool isTestMode = ConfigManager.IsTestInstanceMode();
        if (!isTestMode)
        {
            fail("测试实例自启门禁", "在自检模式下 ConfigManager.IsTestInstanceMode() 未能识别测试参数");
        }

        string? beforeRunVal = null;
        try
        {
            using var rk = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: false);
            beforeRunVal = (rk?.GetValue("StarPie") as string) ?? (rk?.GetValue("WinPieGestures") as string);
        }
        catch
        {
        }

        ConfigManager.EnsureAutoStartRegistryUpToDate();

        string? afterRunVal = null;
        try
        {
            using var rk = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: false);
            afterRunVal = (rk?.GetValue("StarPie") as string) ?? (rk?.GetValue("WinPieGestures") as string);
        }
        catch
        {
        }

        if (beforeRunVal != afterRunVal)
        {
            fail("测试实例自启门禁", $"EnsureAutoStartRegistryUpToDate 在测试模式下读写或修改了 HKCU 启动项！Before=[{beforeRunVal}], After=[{afterRunVal}]");
        }

        line("  回归自检：带/不带 .exe 进程名规范化与 PID 安全门禁、SDK 次版本与最低宿主版本门禁（旧宿主拒绝、新宿主接受）、编辑器画刷安全契约与打开检查、单键录制控件与标点句点区分、停用插件类型下拉与不可用提示、主扇区类型往返保持、Win32 INPUT 原生尺寸与三类失败序列状态机、--test-instance 自启注册表零写入保护全部通过 ✓");
    }

    private static void RunOnSta(Action action)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            action();
        }
        else
        {
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error != null)
            {
                throw new InvalidOperationException($"STA 线程执行失败: {error.Message}", error);
            }
        }
    }
}
