# 更新重启与退出交接：beta.4 第一阶段

## 状态与候选

实现与自动验证已完成。用户已确认退出后立即普通启动、隔离副本软件内回退覆盖及自动重启通过；扩展权限与现代目标回执场景保留后述边界。原始间歇故障尚未通过真实更新稳定复现；下述根因证据来自调用链、受控反例和实际候选测试，不代表已证明用户机器上的唯一原因。

- 基准：`773e8c8353ee6da1cb1ebbeb921043bdde4d5d99`，已发布的 beta.3 保持不变。
- 隔离工作树：`G:\Users\2 Better\.codex\worktrees\profile-trigger-wheels\design`，分支 `codex/restart-update-reliability`。
- 候选：`WinPieGestures/bin/Release/build-out-20261103-1641/StarPie.exe`，版本 `2.0.1-beta.4`。
- 实际宿主 DLL SHA-256：`BEBFB8C0C34E5DA91FAD29DA24AC906B780AF2EBFBC22C24E825CA260E8F7ECB`。
- SDK 仍为 1.14；本阶段不实现小键盘或五级展开，不提交、合并或发布。
- 完整本机证据根：`H:\starpie-task-evidence\restart-update-reliability-20261103`。

## 改动与边界

旧更新路径启动助手后立即退出，解压、启动错误可能被吞掉，等待超时会按 PID 强制结束进程。新路径先在独占临时目录解压、验证清单与哈希，并验证目标可写，再完成 `prepared → proceed → accepted → confirmed` 交接。助手校验旧进程 PID 与启动时间并等待退出，替换前再次检查取消；不强制结束旧实例。只备份和覆盖清单文件，保护配置与插件数据目录。

新版本达到 HostReady 后回报与请求及 EXE 哈希绑定的 ready。回退到不支持该协议的旧版本时，仅采用三秒存活的兼容判断，不能称为完整就绪确认。失败时保留日志和可用备份；新进程仍存活但未回报 ready 时，不冒险覆盖它正在使用的文件。最终确认之后发生助手或主进程崩溃，仍存在异步交接的可靠性边界。

正常退出先暂停输入，再在 Dispatcher 仍能处理消息时异步收尾插件；共享一次退出序列，避免多个退出请求重复收尾。插件退出有整体五秒宽限期，最终仅请求结束宿主持有且创建时间匹配的插件子进程，不追加每个子进程两秒的串行等待。普通禁用插件的等待语义不变。配置保存和钩子清理完成后才释放单实例所有权。退出期间的新启动等待交接；权限和句柄错误明确失败，不允许因异常产生第二个主实例。

安装目录不可写时，准备失败会保留当前应用并提示错误。本阶段没有新增自动 UAC 更新助手；普通权限运行的 Program Files 安装需通过明确授权的安装器升级，或在用户主动选择的权限下更新。真实标准用户／管理员之间的命名对象权限仍需验收。

## 候选绑定验证

所有测试输出均独立于主程序候选；候选绑定的测试验证实际加载 DLL 哈希。

| 检查 | 命令或入口 | 1641 结果与证据子目录 |
| --- | --- | --- |
| Release | `dotnet build WinPieGestures/WinPieGestures.csproj -c Release --nologo -p:OutDir=<1641目录>` | 退出 0，零警告／错误；`candidate-build-r3.log` |
| 更新、互斥体与退出 | `scratch/update-restart-tests/UpdateRestartTests.csproj`，显式 `StarPieCandidateDir` | 93 项，退出 0；`new-checks-r3` |
| 既有进程与更新回归 | `scratch/process-launch-tests/test_process_launch.csproj`，显式 `StarPieCandidateDir` | 382 项，退出 0；`process-r3`；未加载三个官方插件的冷编辑器集成明确 SKIP |
| 更新包验证 | `scratch/update-payload-tests/UpdatePayloadTests.csproj`，显式 `StarPieCandidateDir` | 89 项，退出 0；`payload-r3`；包括两个 beta.3 发行 ZIP 的隔离解包与哈希检查，未执行包内程序 |
| 更新脚本行为 | `scratch/update-script-tests/UpdateScriptTests.csproj`，`--candidate <1641 DLL> --output <独占目录>` | DeepSeek 与主代理分别得到 73 项、0 失败；`script-worker/verify-1641`、`script-main-review-r3` |
| 安全宿主自检 | `dotnet <1641>/StarPie.dll --plugin-selftest <ControlSelfTestFixture.dll> <report> --skip-invoke --test-instance` | 退出 0，PASS；`selftest-r3`，AppData/TEMP/BundleExtract 全部隔离 |
| UI 测试入口护栏 | `python scratch/check_test_button_thread.py` | 9 个入口通过；无真实动作执行 |
| 格式及脚本语法 | `git diff --check`、PowerShell `Parser.ParseInput` | 退出 0；脚本语法解析未执行更新器 |

更新脚本夹具从实际候选提取常量，将启动新应用、MessageBox 替换为文件记录，将等待时间缩短并逐项记录适配。真实执行的仅为测试自有隐藏 PowerShell 进程；目标、备份、载荷均为自有无执行能力文件。PID 等待只使用不可用哨兵，PID 身份逻辑的文字护栏不等于真实 PID 复用验证。符号链接用例因当前账户权限不足 SKIP，未提权或使用替代进程。

同一夹具对 1633 候选得到 73 项、8 个失败（退出 1）：缺少最终确认或确认前取消时，旧脚本仍替换测试文件并记录假启动；1641 两组反例均通过。证据：`script-worker/repro-1633-r3`。主进程侧原子 confirmed 写入及取消路径由 93 项候选检查覆盖。

`python scratch/check_i18n.py` 当前退出 1：它将未改动的 `I18n.T("AutoStartResult" + result.Status)` 误认为完整键 `AutoStartResult`。基准 773e8c8 已有该表达式；实际六个结果键与六个新增更新键的四语言运行时解析均通过。保留原护栏与失败记录 `i18n-r3.log`，未添加占位词条或削弱检查。

独立原生审查已复算 1641 DLL 哈希并阅读完整改动，未发现新的阻塞项；审查不替代真实权限、插件或更新验收。审查发现的助手死亡、配置目录重叠、启动事件创建顺序、延迟 accepted 四类问题均已修正并加入验证。

## 委派与范围审计

DeepSeek V4.1 Flash/MAX 完成隔离载荷校验和脚本夹具，主代理整合、独立运行检查；原生 GPT 工作者负责源码独立审查。第三方入口的底层运行身份仍为 unknown。

旧脚本夹具曾执行超出允许范围的 `cmd.exe /c mklink /J` 后备，`script-worker/run-20261103-1616/summary.json` 留有 `cmd.exe exit 1`。当前已删除后备，重新审查并复跑；历史证据保留，不将旧结果宣称为范围遵循通过。当前 reparse 测试仅调用 BCL 符号链接 API，失败明确 SKIP。

基线中 14 个未跟踪诊断文件按 SHA-256 核对未改变；`H:\design` 中原有 README、支持者资源和临时工程未处理。本阶段不做清理。

## 人工门禁

2026-11-03 用户确认“退出后立即普通双击能立即启动没问题”，并在隔离软件内更新说明后确认“测试通过无误”：

- 普通退出后立即普通双击已通过；短时间重复启动的单实例行为仍待明确反馈。
- 连续普通重启、管理员重启、UAC 取消：仍需分别明确验收旧实例结束、取消时原实例保留与无双实例。
- 隔离副本 beta.4 → beta.3 的软件内回退覆盖、自动重启验收已通过。本次未安装其他插件，不将结果外推为真实插件集合的升级验收。
- 代表性核心插件加载后退出、日志和配置保存与输入资源收尾：仍需对应实机反馈。
- 标准用户／管理员对象权限及 Program Files 不可写反馈：未由本次简短通过反馈单独证明。

未明确覆盖的权限和后续现代目标回执场景不宣称通过；合并与发布仍需对应授权。本阶段核心故障场景通过后继续交付小键盘插件与最多五级菜单展开；两项保留各自 SDK／配置兼容与视觉输入人工门禁。

已准备 `H:\starpie-manual-tests\update-1641\Start-isolated-test.cmd` 和同目录测试说明、恢复脚本。13 个程序文件已按原始候选逐一校验；启动器将配置和 TEMP 指向本目录 data。尚未由 AI 启动。当前可通过已有“历史版本回退”把测试副本从 beta.4 切换至公开 beta.3，验收同一个软件内下载、覆盖及静默重启入口；该旧目标没有新 HostReady 协议，不能据此宣布现代目标的真实回执路径通过。
