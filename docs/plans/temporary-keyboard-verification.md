# 临时小键盘：候选与验收记录

## 当前状态

宿主 beta.4 的更新重启核心场景已由用户通过；下文保留1827旧候选的自动验证历史。用户随后报告小键盘闪现消失，1827小键盘人工验收未通过。修复与可视化编辑器的新候选见[小键盘修复验收记录](keyboard-repair-verification.md)，旧自动结果不能当新候选的人工通过。最多五级子菜单尚未实施。不提交、合并或发布本轮未验收源码；公开 beta.3 不变。

宿主工作树为 `G:\Users\2 Better\.codex\worktrees\profile-trigger-wheels\design`，分支 `codex/restart-update-reliability`，基准 `773e8c8353ee6da1cb1ebbeb921043bdde4d5d99`。插件工作树为 `H:\starpie-official-worktrees\temporary-keyboard`，分支 `codex/temporary-keyboard`，基准 `c1afceeded7697dba8dc1c5eef64f9138a7fe5ac`。两处改动仍未提交；已有14个诊断文件哈希未变。

## 冻结产物

| 产物 | 路径／SHA-256 |
| --- | --- |
| 宿主 | `WinPieGestures\bin\Release\build-out-20261103-1827\StarPie.dll`，`ED82B05C213104FF91312FD52F5BBBF551531626CB799117CB2DD08A8693210E` |
| 宿主SDK | 同目录 `StarPie.Plugin.Abstractions.dll`，`492E2752AD4D0B309CB2826FA889E7682B0BF1F99D3ADA36DE46F4301A5193DA` |
| 小键盘 | 插件工作树 `src\StarPie.Plugin.TemporaryKeyboard\bin\Release\net8.0-windows\StarPie.Plugin.TemporaryKeyboard.dll`，`BD89CF0F0F5E6735501E347598D0FDD0F90602D67A0790F0D5BFFF53751ED982` |

宿主和插件各自Release构建0警告、0错误。插件不引用主程序集，不带SDK私有副本，不混入宿主输出。SDK1.15通过可选派生上下文提供输入会话、动作绑定与调色板；旧IPluginContext不增成员，程序集身份不变。插件仓库保留的Features元数据使其SDK二进制与宿主不同；三个新增／变更SDK源文件与宿主一致，来源记录明确仍为未提交候选。

## 最终自动结果

证据根目录 `H:\starpie-task-evidence\temporary-keyboard-20261103`。下表列实际成功运行；先前命令失败和旧候选结果保留，不混入最终通过记录。

| 检查 | 命令／结果与证据 |
| --- | --- |
| 布局、方案、私有文件 | `KEYBOARD_TEST_ROOT=<隔离目录> dotnet run --project tests/starpie.plugin.temporarykeyboard/ModelTests.csproj -c Release`；20组通过，`models-storage.log`。覆盖超过旧8KiB限制、8192字符参数、损坏／非法UTF8、逐字节备份、写入失败、并发编辑、超限及BOM |
| WPF构造／模板／布局／关闭 | `dotnet run --project tests/starpie.plugin.temporarykeyboard-ui/UiTests.csproj -c Release -p:StarPieCandidateDir=<1827> -- <插件DLL> <SHA256>`；12个布局通过，`ui-layout-final.log`。使用假调色板与服务，未Show，不能替代真实宿主主题、焦点或DPI验收 |
| 输入会话生产接缝 | 构建 `scratch/input-panel-tests/InputPanelTests.csproj`，指定1827与隔离OutDir，运行 `dotnet <输出>/test_process_launch.dll <1827目录>`；23组通过，`host-main-1827`。涵盖快照／顺序／64容量、OEM键名、目标／会话取消、原始上下文、后台及超时真实结束释放、正常与异常日志脱敏。贡献点和输入后端均为替身 |
| 更新／退出 | `scratch/update-restart-tests/UpdateRestartTests.csproj` 绑定1827，运行参数为1827目录；93项通过，`regression-1827/restart-rerun.log` |
| 宿主回归 | `scratch/process-launch-tests/test_process_launch.csproj` 绑定1827；382项通过，`regression-1827/process.log`。三个既有官方插件的冷编辑器集成因该隔离工作树无相应Release产物而跳过 |
| 载荷 | 构建 `scratch/update-payload-tests/UpdatePayloadTests.csproj` 绑定1827，再显式运行TFM输出 `test_process_launch.dll`；70项通过，`regression-1827/payload.log`。本轮不重复真实发行压缩包检查 |
| 助手脚本 | `scratch/update-script-tests/UpdateScriptTests.csproj --candidate <1827 DLL> --output <独占目录>`；73项通过，`script-1827`。真实启动／消息框均为夹具适配，不修改安装目录 |
| 安全宿主自检 | `dotnet <1827>/StarPie.dll --plugin-selftest <ControlSelfTestFixture.dll> <report> --skip-invoke --test-instance`；PASS，`regression-1827/selftest-report.txt`。配置／TEMP全部隔离，不执行真实动作 |
| 预览清单 | `scratch/input-panel-preview-check` 绑定宿主／插件实际哈希，静态PE及清单扫描通过，`preview-scan.log`；不安装或激活 |

全局i18n静态脚本既有动态键误报 `AutoStartResult` 未改动，见更新重启验收记录；新面板／键盘文案覆盖简中、繁中、英文、日文。

## 委派与独立复核

DeepSeek WorkBuddy/MAX完成纯模型和初始无副作用测试。后续工具任务出现停滞，明确改用已通过窄只读探针的CodeArts DeepSeek；其策略默认为medium，API不能设置实际档位，未硬传思考参数。该工作者只写 `scratch/input-panel-tests` 与独占证据，产品由主代理整合。主代理检查并独立重新构建／运行23组测试。第三方实际底层身份记为unknown，不将界面标签当作运行身份。

原生独立GPT审查依次发现并复核：大方案截断和IO失败、非法UTF8原文丢失、OEM解析、动作可用状态、原目标在插件加载后的丢失、监控注册竞态、关闭事件重入、按键与异常值日志。最终1827只读复核确认指定异常测试在任何Sleep／SendInput前命中替身，面板路由在BuiltinActionCatalog前返回；修复后23组通过。源码与产物审查不证明真实输入、Windows权限或性能。

## 测试隔离事故与修复

1824第一次主代理运行异常日志用例时，BuiltinActionCatalog先截获Hotkey，绕过fake并进入旧的真实 `Ctrl+Shift+K` 路径；fakeCalls为0，该轮22通过／1失败，不能声明无实际输入。未证明外部应用发生了何种影响。已向用户披露，原始日志与 `test-boundary-incident-1824.json` 保留。

主代理修正面板专用分发，先经原目标验证，再在通用builtin路由前处理限定的Text／Hotkey／Plugin。独立审查先静态确认新1827安全路径，随后才重跑。fakeCalls必须为1的断言未删除或放宽。失败历史不因最终测试通过而消除。

## 人工门禁与测试入口

入口：`H:\starpie-manual-tests\keyboard-1827\Start-Keyboard-preview.cmd`；完整步骤在同目录 `测试说明.md`。启动器只在用户运行时指向隔离AppData，用户需自行确认安装 `Keyboard-preview` 内DLL。本地清单使用非保留别名 `local.temporarykeyboard.preview`，DLL哈希与正式源码产物相同；不把本地别名安装当正式catalog／签名或发行验证。未自动安装插件。

须检查五模式连续输入、OEM和一次修饰、非激活焦点、Esc／切换应用／暂停／停用、3×4命名方案及实际跨插件动作、普通／管理员目标、IME与多屏DPI。新增输入路径人工未通过不得合并。实际输入与焦点、外观及系统权限结果目前待用户反馈；宿主内存和启动性能不由本轮静态或假后端测试宣称已达指标。
