# 1155纯按键与宿主动作面板候选验证

## 基线与范围

宿主工作树`G:\Users\2 Better\.codex\worktrees\profile-trigger-wheels\design`，分支`codex/restart-update-reliability`、HEAD `773e8c8353ee6da1cb1ebbeb921043bdde4d5d99`；插件工作树`H:\starpie-official-worktrees\temporary-keyboard`，分支`codex/temporary-keyboard`、HEAD `c1afceeded7697dba8dc1c5eef64f9138a7fe5ac`。均包含前阶段已在研未提交更改；本轮不提交、合并或发布。14份旧诊断文件逐项SHA256复核一致。

用户确认0948能唤出，但反馈运行布局溢出、方案下拉显示类型名、预览与实际不一致，要求纯键盘编辑与宿主动作面板分离。本轮行列可配置和独立快捷键以用户后续明确回复为准。SDK仍为前阶段1.15，本轮未修改其契约；五级轮盘只有计划。

## 实现与兼容

- 插件实际窗口和预览共享`KeyboardGrid`等尺寸网格；长标签省略及完整提示，调整窗口宽度后点击不复位。下方全键盘模板支持复制、粘贴、清除、拖入、换位和撤销重做。方案名直接显示Name。
- 新纯按键文件`keyboard-key-schemes.json`，KeyId来源白名单为全键盘模板；旧动作文件保留，不自动转换。根／方案／按键未知字段在读写、历史和克隆中保留，并纳入编辑等价判断和历史字节预算。
- 宿主新增独立`ActionPanels`方案、轮盘动作入口和快捷键路由，重用`ActionItem`、宿主参数表单及动作队列。UI保持不激活目标窗口，原HWND/PID及会话有效性在排队和UI投递后复核；快捷键按VK分别保留抬键所有权。
- 选择子扇区仅高亮子扇区，父导航状态保留。定格遵守边缘防溢出策略，显示、扩展阈值和命中使用修正中心，不移动光标。

## 冻结候选

- 主程序：`G:\Users\2 Better\.codex\worktrees\profile-trigger-wheels\design\WinPieGestures\bin\Release\build-out-20261104-1155\StarPie.exe`
- 宿主DLL SHA256：`F0599A21D291D5AA8666CAAD2D63300FF197937C4EF6994213B0A7D3330F1F1E`
- SDK DLL SHA256：`492E2752AD4D0B309CB2826FA889E7682B0BF1F99D3ADA36DE46F4301A5193DA`
- 插件DLL：`H:\starpie-official-worktrees\temporary-keyboard\src\StarPie.Plugin.TemporaryKeyboard\bin\Release\net8.0-windows\StarPie.Plugin.TemporaryKeyboard.dll`
- 插件DLL SHA256：`C190418C222E9CD25BEEDD63EC8BAD2EE82E42AA50B56CD6C48CCA45AE505428`
- 证据根：`H:\starpie-task-evidence\keyboard-key-layout-20261103`。完整命令、退出码在对应工具日志和本聊天记录；清单记录最终源文件及交付文件哈希。

## 自动验证

| 检查 | 结果 | 证据文件（相对证据根） |
| --- | --- | --- |
| 宿主、插件Release构建 | 均exit 0，0 warnings/errors | `host-guard-build.log`、`plugin-final-build.log` |
| 宿主配置、快捷键、编辑器、实际模板、离屏布局与原目标上下文 | 227项通过；绑定1155和最终插件哈希 | `host-tests-final-bound.log` |
| 纯模型与存储 | 13组通过，直接编译链接实际源码 | `model-final.log` |
| 插件离屏UI | 14个窗口布局／模板及伸缩、标签、模板复制、换位、撤销重做、保存失败；通过 | `ui-final.log`、`ui-final-images/` |
| 原输入面板队列和假贡献点回归 | 23组通过；实际加载1155 DLL | `input-1155.log` |
| 目标事件时间戳与取消守卫 | 10组通过，假时钟／dispatcher | `target-1155.log` |
| 父子高亮转换 | 0948基线按新增断言失败；1155通过 | `highlight-baseline-r1.log`、`highlight-1155.log` |
| UI投递后原手势过期 | 1147在新增断言失败；1155通过 | `guard-baseline.log`、`host-tests-final-bound.log` |
| 未知元数据交换断言有效性 | 仅在隔离源码副本去掉元数据比较后，13组中目标1组失败；产品原源码不变 | `equality-counterexample.log` |
| 安全夹具自检 | 固定ControlSelfTestFixture、`--skip-invoke --test-instance`，通过；仅隔离目录安装／启用夹具，不调用真实动作 | `safe-selftest-1155.log`、`safe-selftest-1155.json`（文本报告） |
| 本地预览插件静态PE／manifest识别 | 通过，无安装或激活产品插件 | `preview-scan.log` |

测试未Show窗口、启动真实钩子、输入注入、操控用户窗口、执行真实插件动作或修改系统自启动。UI使用实际控件离屏布局／渲染；宿主样式来自实际源码App.xaml资源字典，不实例化产品App、不等同编译后整窗实机验收。边缘防溢出本轮为调用链复核，真实屏幕几何、焦点和鼠标行为保留人工门禁。23组中的执行均走假贡献点／拦截器，不可提升为真实动作通过证据。

## 委派与独立复核

DeepSeek显式路由`workbuddy-ai/deepseek-v4.1-flash-sg`、MAX，经获授权既有聊天完成纯模型／存储和限定修复；运行身份`reported_model: unknown`。主代理独立复读源码并重新执行模型及候选测试。该工作者在前轮曾超出模型／测试允许路径，构建了插件默认bin/obj；主代理已明确停止此行为，重新自行构建核验。本次等价修复未再进行插件构建，不扩大其写入资格。

独立原生GPT（gpt-6.1-terra/high，未参与实施）复核发现并闭环：重叠快捷键抬键、未知字段保留、损坏方案只读、键盘轮盘缺目标快照、元数据等价短路、投递后原手势守卫丢失。最初“正常类型列表可选择未支持类型”的判断经实际目录核对撤回，不列为产品缺陷。1155及最终插件两份哈希复算一致，最终窄复核无新增阻塞；不代表实机通过。

## 人工门禁与交付

新入口：`H:\starpie-manual-tests\keyboard-keys-1155\Start-Keyboard-keys.cmd`。需用户手动覆盖安装同目录Keyboard-preview DLL，退出后再用新入口重启；沿用1827隔离数据，未自动安装或修改测试数据。

复验纯按键模板与拖拽、实际输入、伸缩／长标签、宿主动作面板独立及轮盘呼出、旧动作文件显式导入、真实焦点／取消／单次动作、双面板互斥、定格四边／多屏DPI及子级单独高亮。详见同目录《测试说明.md》。依据AGENTS.md第6节“人工未通过不得合并”，本候选等待人工验收；此前唤出通过不替代新配置与输入路径。已有更新重启测试通过记录保持原边界，不新增宣称。
