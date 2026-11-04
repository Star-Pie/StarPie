# beta.4 定格一级动作确认修复

## 基线与复现

宿主工作树为 `G:\Users\2 Better\.codex\worktrees\profile-trigger-wheels\design`，基准为已合入 main 的 `843dbfdaa4a6a09380f338c5870b3ad09d3f5ec3`。已有未跟踪诊断文件保留。官方插件工作树的选择性目录发行红测试与本修复分开，不进入宿主提交。

最终 Lightweight 包实机验收中，用户报告定格后正常高亮但点击不执行；后续明确为“只有一级点不动，二级可以”。当前配置为右键、长按、定格、多级、Wheel 子轮及自动展开；全局第一层八个主扇区均带子项。原确认代码在发现子项后直接展开并返回，未考虑主项自身仍有有效动作。子轮已自动展开时，重复点击主项没有可见结果。

`scratch/beta3-pinned-tests` 新增原生消息结构的合成数据包，直接进入实际 `MouseHook.HookCallback`，覆盖复用事件参数、工作线程到 UI 的确认调度及实际 activation。目标解析、几何命中、修饰键释放和动作入队均使用既有替身；不安装钩子，不创建窗口，不执行输入、命令或插件动作。新主项断言在原候选失败，日志为 `H:\starpie-release-evidence\v2.0.1-beta.4\pinned-repair\red.log`。

## 修复与边界

仅调整 `GestureController.Pinned.cs` 的点击分支顺序：先解析实际命中的动作，有效主动作与二级、中心动作沿用原结束会话和入队流程；没有自身动作的父项才仅展开。悬停、输入配对、目标校验、关闭后入队、过期回调和普通非定格模式保持原路径，不新增 SDK、配置或钩子 IO。

本次没有用“二级也失效”的中间描述扩大修复范围；以用户最新反馈为准。

## 自动验证

- Release 候选：`WinPieGestures\bin\Release\build-out-20261104-1501\StarPie.exe`，构建 0 warning / 0 error。
- 实际 `StarPie.dll` SHA-256：`6AB9C5263F286882FB81F331B4E79A81245737AD33ACD16A39BD7587F7223DA8`。
- `scratch/beta3-pinned-tests/Beta3PinnedTests.csproj` 以显式 `StarPieCandidateDir` 和独占输出构建，运行时检查实际 DLL 哈希；501 条通过，覆盖自动展开开/关、有自身动作的主项、二级目标及无自身动作父项，并保留原配对、取消和过期回调检查。
- `scratch/action-panel-tests/ActionPanelTests.csproj` 绑定同一候选及实际键盘插件，415 条通过；无 Show、钩子、输入、真实动作或系统修改。
- 证据目录：`H:\starpie-release-evidence\v2.0.1-beta.4\pinned-repair`。开发过程中的失败日志保留，不作为最终通过记录。

原生 GPT 独立读取实际差异及调用链、复算源码/测试/DLL 哈希后未发现阻断，并在独占目录重跑同一探针：退出码 0、501 条通过。用户随后确认 `1501` Lightweight 复验“修复通过”。这些结果不替代从最终主分支重建的三包与升级验收。

旧 `843dbfd` 发行草稿仍未公开；旧 beta.4 Tag 已推送且仍指向该旧提交。修复合入后须先说明 Tag 外部状态和更新方案，获明确授权后才处理旧标签，禁止静默改写发行历史。最终发行包绑定实际合入主分支的提交。
