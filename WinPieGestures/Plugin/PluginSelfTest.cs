using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
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
            Line("[3j] 宿主服务面与能力门禁（命令 / Shell 动词 / 窗口控制 / 屏幕截取 / 系统功能）");

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
        // 卡片在 ListBox.ItemTemplate 里，两个按钮的文字绑在 PluginListItem 上，所以
        // 「卡片文案翻没翻」没法靠按键名取控件来查 —— 只能真的构建一次卡片再看结果。
        // Build() 因此被放在 PluginListItem 里而不是窗口类里：窗口类里的私有方法自检够不着。
        //
        // 这一段刻意放在 [4] 之前：--skip-invoke 会在 [4] 开头提前 return，
        // 放到 [4] 之后等于日常回归里根本不会执行（那正是 [5b] 曾经踩过的坑）。
        line("");
        line("[3f] 插件管理页卡片文案（状态名 / 摘要 / 两个按钮，且整卡随语言切换）");

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
}
