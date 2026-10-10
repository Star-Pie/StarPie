# 插件系统工程规范

涉及 `plugin/sdk/`、`WinPieGestures/Plugin/`、插件动作 UI、安装、启停、能力或官方插件时，必须先读本文件。更完整的执行链与 API 参考位于 [`../plugin/docs/`](../plugin/docs/)。

## 1. 三层边界

| 层 | 路径 | 规则 |
| --- | --- | --- |
| SDK 契约 | `plugin/sdk/StarPie.Plugin.Abstractions/` | 插件唯一可引用的 StarPie 程序集；公共 API 只增不改 |
| 宿主实现 | `WinPieGestures/Plugin/` | `PluginHost` 是主程序唯一接缝，运行时和路径模块在此实现 |
| 样例 | `plugin/samples/` | 展示契约用法，不成为宿主依赖 |

插件工程必须满足：

1. `TargetFramework` 不高于宿主（`net8.0-windows` 或 `net8.0-windows10.0.19041.0`）。
2. SDK `ProjectReference` 带 `<Private>false</Private>`，避免两份 SDK 导致类型身份分裂。
3. 只引用 SDK 和 BCL，禁止引用主程序 `StarPie.dll`。
4. 默认零 NuGet 依赖；新增依赖需单独论证体积、加载与卸载影响。

## 2. 注册、词条和动作

- `Initialize` 中的动作、图标、词条和设置页注册先暂存；成功返回后由 `PluginCatalog.Commit` 原子提交，失败则全部 `Discard`。
- 词条提交发生在动作初次注册解析之后，因此 `Commit` 完成词条后必须调用 `ResolveStagedDisplayNames` 再解析一次。不要要求插件依赖“先注册词条、再注册动作”的隐式顺序。
- 普通插件动作的数据模型固定为 `Type="Plugin"` 与 `PluginActionRef`；UI 顶层只有一个“插件动作”，子下拉按插件名分组。
- 分组重名统计按插件去重，不能按动作条目计数。
- `SelectedPluginActionFullId` 忽略绑定刷新产生的空写入；切换类型或普通属性通知不得清空原引用。
- 插件图标键使用 `plugin:<pluginId>:<shortKey>`。SVG 只使用项目解析器可靠支持的基础路径指令。

## 3. 参数表单与设置页

- 插件声明 `ParameterField`，不提供 XAML。宿主统一用 `PluginParameterForm` 渲染主题、布局和验证。
- 支持字段以 SDK 当前定义为准，包括文本、多行、数字、布尔、目录、文件、枚举、快捷键、颜色和 KeyMap。
- 保存前与执行前都走 `PluginHost.ValidateActionParameters`：先宿主声明校验，再插件 `Validate`。
- `Bool` 未填视为 false；取消勾选必须写入 false。
- 数字始终以 `InvariantCulture` 读写。
- 可选数值不得用 0 表示“未填”，必须区分缺失与显式非法值。
- 编辑某个官方认领动作时，`TryGetClaimedEditorRegistration` 只按需激活当前已启用插件以取得参数声明；不改变启用或预加载偏好，参数校验本身仍不触发加载。

插件级设置页使用 `ISettingsPageRegistry`：

- 优先级为动作参数 > 插件设置 > 内置默认。
- 设置页声明在 `Initialize` 代码中，不复制到 `plugin.json`。
- 不为设置页新增回调、事件订阅或虚假能力位；插件在使用时读取当前值。
- `PluginParameterForm` 与 `IPluginParameterTarget` 同时服务动作参数和插件设置，不写第二套表单。
- `ActionItemParameterTarget` 报告未声明键；`PluginSettingsParameterTarget` 允许插件私有键。
- 标题和标签在用户点击设置时解析，此时词条已提交。`HasPage` 至少要求一个字段。
- 关闭窗口时落盘，避免逐按键重写 JSON。校验问题显示但不偷偷丢弃输入。

## 4. 运行时、租约与卸载

- `PluginRuntime` 管理实例、激活、状态、租约、停止和 ALC 卸载。
- `PluginActivationCoordinator` 只查找、检查状态、合并并发加载和调用 `PluginInstance.Load`；不得更改用户的 `Entry.Enabled` 偏好。
- `action-execution`、`interaction-event`、`wheel-structure` 等路径使用强类型模块。不要退化成中央字符串路由。
- 动作可对已启用但未加载插件做惰性加载；广播事件不得因此加载插件；结构 Provider 只按明确引用加载并有缓存回退。
- 每次自定义 `Validate`、`ExecuteAsync`、事件回调或结构查询都持有 `PluginInvocationLease`。超时或向用户返回后，租约仍保持到真实 Task 结束。
- `DisableAsync` 顺序为：写入禁用偏好与关闭新入口 → 撤销路由 → 发送取消 → 等活动租约归零 → `Shutdown` → ALC 卸载。
- 普通停用默认等待 5 秒；超时返回 `Pending` 并继续后台观察，不强制卸载。更新、覆盖或卸载只有完全停止后才能继续。
- 设置页不得在 UI 线程同步执行插件动作。“测试”按钮调用 `ExecuteForTesting`，进入真实动作队列，并传入 `Clone()` 快照。
- 会画窗口的插件只在确有工作时 `Dispatcher.Post`；空投递也会留下握住插件闭包的 `DispatcherOperation`，导致 ALC 探针失败。

## 5. 能力门禁

产生后果的宿主服务检查各自能力：

| 服务 | 能力 |
| --- | --- |
| 命令、Shell 动词 | `Process` |
| 窗口移动、置顶、透明度、切换 | `WindowControl` |
| 截屏与识别 | `ScreenCapture` |
| 系统控制与输入模拟 | `InputSimulation` |
| 呼出宿主轮盘并接管点击 | `Wheel` |
| 键盘空间映射 | `InputRemapping` |

- 门禁在 `Guard` 之外抛 `PluginCapabilityDeniedException`，不能被吞成普通 false。
- 元数据（终端、动词、布局、范围）不受门禁，插件声明参数时可以读取。
- 能力标签必须对应真实强制点，不能复用语义相近但后果不同的能力。
- 进程内插件仍可直接调用 BCL/PInvoke；门禁是授权说明与宿主服务后果的一致性机制，不是安全沙箱。
- 既有公共服务不能事后补破坏性门禁；新增能力与强制点同一版本落地。
- 能力确认文案集中在 `PluginCapabilityLabels`，存词条键而不是缓存后的翻译。

## 6. 官方插件、安装与目录

- 原内建官方动作在 `StarPie-Official-Plugins` 仓库构建发布；主仓库不编译、复制或随包携带其 DLL。
- `程序目录\plugin\` 是社区候选扫描区：宿主只读、不创建、不删除；社区插件不能使用 `starpie.*` 保留 ID。
- `%LOCALAPPDATA%\StarPie\plugin-data\` 是可写宿主区：安装包、官方/社区插件、登记表、健康状态与插件数据都在这里。
- 顶层历史动作类型只允许 `Official=true` 的官方模块认领；冲突认领整组拒绝。安装、启停、卸载后重建认领快照。
- 派发顺序保持：内建 Hotkey → 官方类型认领 → 普通 Plugin。
- 官方 `module-catalog.json` 只接受 `schemaVersion=2`：模块记录 `id + versions`，包字段、宿主上下界、SDK 与目标框架要求属于每个历史版本。客户端、缓存、手动安装和引导共用最高兼容版本选择；不保留旧客户端默认包，不自动降级。发布工具可迁移 v1 历史，客户端不接受 v1。

首次核心插件引导：

- 仅在用户首次交互式打开设置、插件系统健康时出现。
- 点击“一键安装”前零网络请求；静态展示 `folder`、`weburl`、`launch`、`system`、`shelltool` 与基线权限。
- 同意后只安装缺失项；已安装项不覆盖、不升级、不重新启用。
- catalog 权限超出 `Process`、`InputSimulation` 时必须二次确认。
- 失败保留已成功项并允许重试，不做整体回滚。
- 成功后通知插件集合变化并刷新 UI，无需重启。
- 当前只依赖 SHA-256，不下载或调用外部 `cosign.exe`。

## 7. 调度、失败与元数据

- 可能耗时上百毫秒的动作声明 `Background`，避免占用唯一顺序动作线程。
- 环境不满足（例如硬件不支持）不是插件故障；返回可见但非失败结果，避免连续失败 5 次触发隔离。
- 宿主服务的元数据只有一份来源，并在访问时读取当前语言，不缓存翻译。
- ShellTool 配置存短 ID；执行体兼容短 ID 与规范 Verb。`Verbs` 仅用于展示，不作为旧配置白名单。
- SDK 版本字符串与 major/minor 常量需要自检保持一致；每次新增公共服务同步更新版本演进注释和契约测试。
- 当前候选 SDK 为 1.10：1.8 增加 `ProcessLaunchMode`、三个显式模式启动入口与通用旧值回填；1.9 增加派生自 `IHostWheelService` 的 `IHostWheelSessionService`。旧接口和程序集身份保持不变。1.10 增加可选 IInteractionPluginContext 和只读交互贡献，Initialize 原子注册后由每插件有界队列调度；旧事件接口的同步契约不变。
- 三态启动权限参数属于插件声明与 `ExtensionData`；未声明新值时可回填旧 `RunAsStandardUser`，显式新值优先，回填不自动写盘。固定权限启动失败或用户取消授权时，不得改用其他权限重试。
- 轮盘会话服务装配到对应插件实例，通知与实例 generation、活动调用租约绑定。旧实例的服务不得操作重载后的会话；插件只追踪自己呼出的轮盘，不复制宿主扇区执行逻辑。

## 8. 必跑验证

1. `dotnet build WinPieGestures/WinPieGestures.csproj -c Release --nologo`
2. `--plugin-selftest --skip-invoke`
3. 修改 `PluginSelfTest.cs` 前后比较段落标记集合，任何缺失都要说明替代护栏。
4. 按范围运行 `scratch/check_test_button_thread.py`、`scratch/check_i18n.py` 等静态护栏。
5. `git diff --check` 和范围审查。
6. SDK、能力、停用、配置迁移或安装流程改动需要独立审查与用户实机验收。

新增自检断言必须证明会红：使用安全变异、基线失败或可复现反例。仅检查源文件是否包含某个字符串不能证明功能正确。

## 9. 统一交互路径文档入口

SDK 1.10 当前源码候选的文档已分为四层：

- [API 第 5 节](../plugin/docs/plugin-system-api-and-performance.md#5-交互事件-api)：签名、字段/枚举、激活前提、注册异常、线程/背压与取消。
- [快速入门第 9 节](../plugin/docs/plugin-development-quickstart.md#9-实现第一个交互贡献)：SDK/BCL 观察插件、清单、启用/预加载与无副作用验证。
- [架构第 13 节](../plugin/docs/plugin-system-architecture.md#13-交互事件路径的完整调用过程)：模块归属、实际生产时点、锁内冻结/锁外发布及兼容边界。
- [架构图第 10 节](../plugin/docs/plugin-system-architecture-map.md#10-交互事件调用主路径)：注册/回滚、Render、队列、撤回竞态和停止排空五条图。

发布边界与人工门禁不能被文档或无 GUI 绿色结果替代；beta.7 音效插件迁移已形成源码候选，双仓库证据、独立复核与剩余门禁见[迁移验收记录](plans/beta7-sound-plugin-verification.md)。

## 音效插件归属（beta.7）

`starpie.plugin.sound` 是官方独立源码候选，订阅SDK1.10的交互事件，不借用旧同步轮盘接口。宿主不提供音效引擎、专用配置或专用编辑器，只保留通用事件/插件设置与未知配置数据。插件声明FileSystem用于旧配置读取、采样和明确的方案文件IO，初始化不发声，不自动改变启用/预加载偏好。插件未安装、禁用或加载失败时无音效兜底。
