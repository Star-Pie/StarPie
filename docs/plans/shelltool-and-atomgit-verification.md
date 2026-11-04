# 工具选择与AtomGit镜像候选验证

## 基线及边界

宿主工作树：`G:\Users\2 Better\.codex\worktrees\profile-trigger-wheels\design`，分支`codex/restart-update-reliability`，HEAD `773e8c8353ee6da1cb1ebbeb921043bdde4d5d99`加既有在研改动。插件工作树：`H:\starpie-official-worktrees\temporary-keyboard`，分支`codex/temporary-keyboard`，HEAD `c1afceeded7697dba8dc1c5eef64f9138a7fe5ac`加既有小键盘改动。

当前任务只修改动作面板工具选择、AtomGit源默认和传输、对应测试，以及插件仓库镜像工作流／文档。原14份未跟踪诊断均逐项SHA256不变；SDK及小键盘DLL不变。主仓库H:\design的已有README／支持者相关脏文件未改动。保留全部旧候选和失败日志；没有commit、push、合并、安装插件、写入系统自启动或公开发布。

后续交付记录（2026-11-04）：用户单独授权仅提交并推送插件镜像工作流。已在插件工作树创建`codex/atomgit-official-plugin-mirror`，以远程main的`c1afceeded7697dba8dc1c5eef64f9138a7fe5ac`为基线，仅提交7个镜像相关文件，提交`e54d3e4bba2dc276768c87fa3c85b3ad3623aec0`已推送并用ls-remote复核一致；其余38个脏文件逐项SHA256不变。提交前重跑79项离线测试及YAML／暂存范围检查均通过。宿主没有提交；没有创建PR、合并、设置Secret、运行镜像同步或发布。1323 UI仍在用户验收中。详细证据：mirror-commit.json、mirror-push.log、mirror-push-preservation-final.json。

## 最终候选

EXE：`G:\Users\2 Better\.codex\worktrees\profile-trigger-wheels\design\WinPieGestures\bin\Release\build-out-20261104-1323\StarPie.exe`

| 文件 | SHA256 |
| --- | --- |
| StarPie.dll | `915C2A4AA9BF770F2031DBDF89CCA8B36BB2DCD3FC3455A7B0E6B7D4B767FDC2` |
| StarPie.Plugin.Abstractions.dll | `492E2752AD4D0B309CB2826FA889E7682B0BF1F99D3ADA36DE46F4301A5193DA` |
| 既有TemporaryKeyboard.dll | `C190418C222E9CD25BEEDD63EC8BAD2EE82E42AA50B56CD6C48CCA45AE505428` |

## 自动验证

证据根：`H:\starpie-task-evidence\shelltool-atomgit-20261103`。构建显式OutDir；测试引用1323候选DLL并核对实际加载哈希，没有重编主程序默认输出。

| 验证 | 结果 | 日志 |
| --- | --- | --- |
| Release构建 | 0警告／0错误，退出0 | host-final-build.log |
| 动作面板／源及假HTTP集成检查 | 415项通过，退出0 | final-host-tests.log |
| 纯镜像元数据解析 | 8组通过，退出0 | parser-final-tests.log |
| 原GitHub目录发现回归 | 70项通过，退出0 | final-discovery-tests.log |
| 输入队列及假执行回归 | 23组通过，退出0 | final-input-tests.log |
| 安全ControlSelfTestFixture，--skip-invoke --test-instance | PASS，退出0 | final-safe-selftest.log |
| 镜像脚本离线／仅loopback curl检查 | 79项通过，退出0 | workflow-offline-reviewed.log |
| YAML结构／dry-run与启用开关门禁 | PASS，退出0 | workflow-yaml.log |
| 两工作树git diff --check | 退出0 | host-diff-check.log／plugin-diff-check.log |

ShellTool摘要与挑选器使用四语言×浅／深色真实WPF资源、模板和离屏布局；浅深色已渲染并查看。采用假的声明表单和选择器，不代表真实工具动作或实机DPI通过。415项中的输入／动作测试使用快照、回调或安全假执行，无Show、钩子启动、输入注入或真实插件动作。

失败有效性：同一ShellTool命令探针在1230返回1、1323返回0；1305实际DLL运行新增完整性检查时在“缺当前tag包拒绝且缓存不变”处失败，1323通过。误用解析测试程序集文件名的一次执行返回1，已纠正为项目实际AssemblyName并保留日志；初始主程序缺System.IO的14个编译错误已修复后重新构建，不作为最终成功结果。

## 独立复核与修复

DeepSeek WorkBuddy/MAX先通过固定只读探针；实施中将3个Add File写到了H盘的同名错误路径。主代理逐文件回收后将错误新增文件移动到证据quarantine；未删除原有文件，暂停该入口本轮写入资格。产品整合、回归和最终结论由主代理负责。

独立原生GPT（gpt-6.1-terra/high）先后检查1305／1321／1323：发现目录可能早于包可见、历史补同步按镜像创建时间排序，以及旧半成品在补包后备注PATCH前可见的交叉风险。均按源码路径修复：目录最后上传、当前tag包存在性门禁、源发布时间标记及发现资格过滤。1323窄复核确认最后一项关闭，并独立复算DLL哈希一致。复核未运行产品或联网。

## 远程与人工待验

用户已导入AtomGit仓库；2026-11-04匿名API返回200及空Release列表。只读获取GitHub最新目录`modules-2026.10.15.1`发现7个引用标签，已写入插件仓库`docs/atomgit-mirror.md`。未配置令牌、未提交工作流、未触发同步，不能声称AtomGit插件下载已可用。

上述为候选冻结时的状态；工作流随后已按新增授权推送独立分支，仍未合并到默认分支或执行同步，真实下载门禁继续保留。

人工入口：`H:\starpie-manual-tests\shelltool-atomgit-1323\Start-ShellTool-AtomGit.cmd`；复用原1827隔离数据，不替换小键盘DLL。需验收工具摘要／挑选／保存取消及常用主题DPI。镜像附件同步完成后，需实际验证更新源、首次确认后核心插件下载、后续普通插件安装、失败提示和禁用状态保留。未经本候选人工通过不得合并。五级轮盘仍属另一个在研计划，本轮未实现。
