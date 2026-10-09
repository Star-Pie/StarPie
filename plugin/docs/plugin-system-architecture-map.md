# StarPie 插件系统架构图

> 本文根据当前仓库实现整理插件系统的运行时架构、加载/注册、动作执行、统一交互事件与停用流程。SDK 1.10 为当前源码候选，真实窗口/手感仍需人工验收。
>
> 文档中的业务节点均对应当前源码中的具体类、属性或方法；Mermaid 中的 `subgraph` 仅用于视觉分组，不代表运行时对象。
>
> 本次更新以 Mermaid 与文字调用树为当前来源；已有 PNG 是历史静态快照，未重绘，不应据此判断 SDK 1.10 路径是否存在。
>
> 最后更新：2026-10-09

## 1. 源码范围

主要实现位于：

- `WinPieGestures/Plugin/`
- `plugin/sdk/StarPie.Plugin.Abstractions/`

核心入口文件：

```text
WinPieGestures/Plugin/PluginHost.cs
WinPieGestures/Plugin/PluginRuntime.cs
WinPieGestures/Plugin/PluginPathModules.cs
WinPieGestures/Plugin/PluginInstance.cs
WinPieGestures/Plugin/PluginCatalog.cs
WinPieGestures/Plugin/PluginContext.cs
WinPieGestures/Plugin/PluginInvoker.cs
WinPieGestures/Plugin/PluginInteractionRegistration.cs
WinPieGestures/Plugin/PluginInteractionQueue.cs
WinPieGestures/Plugin/PluginInteractionSession.cs
WinPieGestures/GestureController.cs
WinPieGestures/RadialWindow.xaml.cs
WinPieGestures/Plugin/StickyWheelSession.cs
WinPieGestures/Plugin/PluginScanner.cs
WinPieGestures/Plugin/PluginManifestReader.cs
WinPieGestures/Plugin/PluginLoadContext.cs
plugin/sdk/StarPie.Plugin.Abstractions/IStarPiePlugin.cs
plugin/sdk/StarPie.Plugin.Abstractions/IPluginContext.cs
plugin/sdk/StarPie.Plugin.Abstractions/Actions.cs
plugin/sdk/StarPie.Plugin.Abstractions/Interactions.cs
```

## 2. 总体架构

```mermaid
flowchart LR
    A["应用启动<br/>App.OnStartup()"]

    H["初始化<br/>PluginHost.Initialize()"]
    PATHS["配置插件目录<br/>PluginPaths.Configure()"]
    SYNC["同步磁盘登记<br/>PluginHost.SyncFromDisk()"]
    SCAN["扫描已安装插件<br/>PluginScanner.ScanInstalledPlugin()"]
    MANIFEST["读取清单<br/>PluginManifestReader.TryLoad() / Validate()"]
    STORE["读取登记快照<br/>PluginRegistryStore.SnapshotEntries()"]
    CLAIM["重建类型认领表<br/>PluginActionClaimRegistry.Rebuild()"]

    RUNTIME["统一调用运行时<br/>PluginRuntime"]
    PATHREG["调用路径注册表<br/>PluginPathRegistry"]
    ACTIVATION["插件激活协调器<br/>PluginActivationCoordinator"]
    CALLS["调用治理协调器<br/>PluginCallCoordinator"]

    ACTIONPATH["动作执行路径<br/>ActionExecutionPathModule"]
    EVENTPATH["交互事件路径<br/>InteractionEventPathModule"]
    WHEELPATH["轮盘结构路径<br/>WheelStructurePathModule"]

    INSTANCE["宿主插件封装<br/>PluginInstance"]
    ALC["程序集加载上下文<br/>PluginLoadContext"]
    CONTEXT["插件专属上下文<br/>PluginContext"]
    ENTRY["初始化<br/>IStarPiePlugin.Initialize(IPluginContext)"]

    CATALOG["运行时贡献目录<br/>PluginCatalog"]
    SESSION["原子注册事务<br/>PluginRegistrationSession"]
    ACTIONREG["注册贡献或路径<br/>PluginActionRegistry.Register()"]
    I18NREG["注册贡献或路径<br/>PluginI18nRegistry.Register()"]
    ICONREG["注册矢量图标<br/>PluginIconRegistry.RegisterSvg()"]
    SETTINGREG["注册贡献或路径<br/>PluginSettingsPageRegistry.Register()"]
    SDK_ACTION["动作贡献接口<br/>IActionContribution"]
    INTERACTIONREG["注册贡献或路径<br/>PluginInteractionRegistry.Register"]
    SDK_EVENT["交互观察贡献接口<br/>IInteractionContribution"]
    EVENT_SOURCE["普通手势控制器<br/>GestureController / StickyWheelSession"]
    EVENT_SESSION["交互语义会话<br/>PluginInteractionSession"]
    EVENT_QUEUE["每插件有界队列<br/>PluginInteractionQueue"]

    A --> H
    H --> PATHS
    H --> SYNC
    SYNC --> SCAN
    SCAN --> MANIFEST
    SYNC --> STORE
    H --> CLAIM

    H --> RUNTIME
    RUNTIME --> PATHREG
    RUNTIME --> ACTIVATION
    RUNTIME --> CALLS

    PATHREG --> ACTIONPATH
    PATHREG --> EVENTPATH
    PATHREG --> WHEELPATH

    ACTIVATION --> INSTANCE
    INSTANCE --> SCAN
    INSTANCE --> ALC
    INSTANCE --> CONTEXT
    INSTANCE --> ENTRY
    CONTEXT --> ENTRY

    ENTRY --> ACTIONREG
    ENTRY --> I18NREG
    ENTRY --> ICONREG
    ENTRY --> SETTINGREG
    ACTIONREG --> SESSION
    I18NREG --> SESSION
    ICONREG --> SESSION
    SETTINGREG --> SESSION
    SESSION --> CATALOG
    ACTIONREG --> SDK_ACTION
    ENTRY --> INTERACTIONREG
    INTERACTIONREG --> SESSION
    INTERACTIONREG --> SDK_EVENT
    EVENT_SOURCE --> EVENT_SESSION
    EVENT_SESSION --> H
    H --> EVENTPATH
    EVENTPATH --> CATALOG
    EVENTPATH --> EVENT_QUEUE
    EVENT_QUEUE --> CALLS
    CALLS --> SDK_EVENT
```

![总体架构图](../pictures/1.总体架构图.png)



### 2.1 以 `PluginHost` 为根的调用树

下面的图只保留调用链上的重点类、成员和方法。`PluginHost` 是主程序看到的唯一插件门面；其余节点分别承担路径选择、插件激活、贡献点查找、动作执行和生命周期管理。

```mermaid
flowchart LR
    EXT_EXEC["执行动作<br/>ActionExecutor.Execute(ActionItem)"] --> PH_EXEC["派发插件动作<br/>PluginHost.ExecutePluginAction()"]
    EXT_CLAIM["派发认领动作<br/>ActionExecutor.ExecuteClaimedActionItem()"] --> PH_CLAIM["执行历史认领动作<br/>PluginHost.ExecuteClaimedAction()"]
    EXT_EVENT["生成轮盘语义事件<br/>PluginInteractionSession"] --> PH_EVENT["发布交互事件<br/>PluginHost.PublishInteractionEvent"]

    PH["插件宿主门面<br/>PluginHost"]

    PH --> PH_INIT["初始化<br/>PluginHost.Initialize()"]
    PH --> PH_INSTALL["提交插件安装<br/>PluginHost.CommitInstallAsync()"]
    PH --> PH_EXEC
    PH --> PH_CLAIM
    PH --> PH_EVENT
    PH --> PH_RUNTIME["统一调用运行时<br/>PluginHost.Runtime<br/>PluginRuntime"]
    PH --> PH_INSTANCES["宿主插件封装表<br/>PluginHost.Instances<br/>Dictionary&lt;string, PluginInstance&gt;"]
    PH --> PH_CATALOG["全局贡献目录<br/>PluginHost.Catalog<br/>PluginCatalog"]
    PH --> PH_STOP["异步停用插件<br/>PluginHost.DisableAsync()"]

    PH_INIT --> PATH_CONFIG["配置插件目录<br/>PluginPaths.Configure()"]
    PH_INIT --> SYNC["同步磁盘登记<br/>PluginHost.SyncFromDisk()"]
    SYNC --> STORE_SNAPSHOT["读取登记快照<br/>PluginRegistryStore.SnapshotEntries()"]
    SYNC --> SCAN_INSTALLED["扫描已安装插件<br/>PluginScanner.ScanInstalledPlugin()"]
    SYNC --> INSTANCE_CREATE["创建宿主插件封装<br/>new PluginInstance(pluginId, entry, scan)"]
    INSTANCE_CREATE --> PH_INSTANCES
    PH_INIT --> CLAIM_REBUILD["重建类型认领表<br/>PluginActionClaimRegistry.Rebuild()"]

    PH_INSTALL --> SCAN_SELECTED["扫描所选程序集<br/>PluginScanner.ScanSelectedDll()"]
    PH_INSTALL --> COPY_PAYLOAD["复制插件文件<br/>PluginHost.CopyPayload() / CopyDirectory()"]
    PH_INSTALL --> WRITE_MANIFEST["写入插件清单<br/>PluginManifestReader.TryWrite()"]
    PH_INSTALL --> STORE_UPSERT["新增或更新登记<br/>PluginRegistryStore.UpsertEntry()"]
    PH_INSTALL --> CLAIM_REBUILD_INSTALL["重建类型认领表<br/>PluginActionClaimRegistry.Rebuild()"]

    PH_RUNTIME --> RT_CTOR["统一调用运行时<br/>PluginRuntime.PluginRuntime(...)"]
    PH_RUNTIME --> RT_ACTION["执行插件动作<br/>PluginRuntime.ExecuteAction()"]
    PH_RUNTIME --> RT_CLAIM["执行历史认领动作<br/>PluginRuntime.ExecuteClaimedAction()"]
    PH_RUNTIME --> RT_EVENT["广播旧版轮盘事件<br/>PluginRuntime.RaiseWheelOpening() / RaiseWheelClosed()"]
    PH_RUNTIME --> RT_PUBLISH["发布交互事件<br/>PluginRuntime.PublishInteractionEvent"]
    PH_RUNTIME --> RT_WHEEL["查询轮盘结构<br/>PluginRuntime.QueryWheelStructureAsync()"]
    PH_RUNTIME --> RT_STOP["通知路径开始停止<br/>PluginRuntime.NotifyPluginStopping()"]

    RT_CTOR --> PATH_REG["注册贡献或路径<br/>PluginPathRegistry.Register()"]
    RT_CTOR --> ACTIVATION["插件激活协调器<br/>PluginActivationCoordinator"]
    RT_CTOR --> CALLS["调用治理协调器<br/>PluginCallCoordinator"]
    PATH_REG --> ACTION_PATH["动作执行路径<br/>ActionExecutionPathModule"]
    PATH_REG --> EVENT_PATH["交互事件路径<br/>InteractionEventPathModule"]
    PATH_REG --> WHEEL_PATH["轮盘结构路径<br/>WheelStructurePathModule"]

    PH_EXEC --> RT_ACTION
    PH_CLAIM --> RT_CLAIM
    RT_ACTION --> CALL_ACTION["执行统一调用<br/>PluginCallCoordinator.Invoke()"]
    RT_CLAIM --> CALL_CLAIM["执行统一调用<br/>PluginCallCoordinator.Invoke()"]
    CALL_ACTION --> ACTION_EXEC["执行动作<br/>ActionExecutionPathModule.Execute()"]
    CALL_CLAIM --> ACTION_CLAIM["执行认领动作<br/>ActionExecutionPathModule.ExecuteClaimed()"]
    ACTION_EXEC --> REQUEST["复制动作请求<br/>PluginActionRequest.TryCreate()"]
    ACTION_CLAIM --> REQUEST_CLAIM["创建认领请求<br/>PluginActionRequest.CreateClaimed()"]
    REQUEST --> ENSURE["确保目标插件已加载<br/>PluginActivationCoordinator.EnsureLoaded()"]
    REQUEST_CLAIM --> ENSURE
    ENSURE --> FIND["插件宿主门面<br/>PluginHost.Find(pluginId)"]
    FIND --> INSTANCE["宿主插件封装<br/>PluginInstance"]
    INSTANCE --> INSTANCE_LOAD["确保目标插件已加载<br/>PluginInstance.EnsureLoaded()"]
    ACTION_EXEC --> LOOKUP["按完整ID查找动作<br/>PluginCatalog.TryGetAction(fullId)"]
    ACTION_CLAIM --> LOOKUP
    LOOKUP --> VALIDATE["校验参数或清单<br/>PluginParameterValidator.Validate()"]
    VALIDATE --> CUSTOM["校验参数或清单<br/>IActionContribution.Validate()"]
    CUSTOM --> INVOKE["执行统一调用<br/>PluginInvoker.Invoke()"]
    INVOKE --> LEASE_ACQUIRE["检查门禁并获取租约<br/>PluginInstance.TryAcquireInvocation()"]
    LEASE_ACQUIRE --> LEASE["活动调用租约<br/>PluginInvocationLease"]
    INVOKE --> INPUT["构造动作输入快照<br/>new PluginActionInput(...)"]
    INVOKE --> KIND["动作调度类别<br/>PluginActionRegistration.Kind"]
    KIND --> SEQ["串行动作调度<br/>PluginInvoker.InvokeSequential()"]
    KIND --> BG["后台动作调度<br/>PluginInvoker.InvokeInBackground()"]
    SEQ --> CONTRIB["调用插件动作实现<br/>IActionContribution.ExecuteAsync()"]
    BG --> CONTRIB
    CONTRIB --> RESULT["插件动作结果<br/>ActionResult"]
    RESULT --> RECORD["记录调用健康状态<br/>PluginInstance.RecordInvoke()"]
    RECORD --> OUTCOME["宿主执行结果<br/>PluginExecuteOutcome"]

    INSTANCE_LOAD --> SCAN_RECHECK["扫描已安装插件<br/>PluginScanner.ScanInstalledPlugin()"]
    INSTANCE_LOAD --> ALC_LOAD["加载入口程序集<br/>PluginLoadContext.LoadFromAssemblyPath()"]
    INSTANCE_LOAD --> ENTRY_CREATE["创建插件入口对象<br/>Activator.CreateInstance(entryType)"]
    ENTRY_CREATE --> PLUGIN_REF["已创建的插件入口对象<br/>PluginInstance._plugin : IStarPiePlugin"]
    INSTANCE_LOAD --> CONTEXT_CREATE["创建本插件上下文<br/>new PluginContext(...)<br/>PluginInstance._pluginContext"]
    PLUGIN_REF --> INITIALIZE["初始化<br/>IStarPiePlugin.Initialize(IPluginContext)"]
    CONTEXT_CREATE --> INITIALIZE
    INITIALIZE --> ACTION_REGISTER["注册贡献或路径<br/>PluginActionRegistry.Register()"]
    INITIALIZE --> I18N_REGISTER["注册贡献或路径<br/>PluginI18nRegistry.Register()"]
    INITIALIZE --> ICON_REGISTER["注册矢量图标<br/>PluginIconRegistry.RegisterSvg()"]
    ACTION_REGISTER --> SESSION["暂存动作贡献<br/>PluginRegistrationSession.StageAction()"]
    I18N_REGISTER --> SESSION
    ICON_REGISTER --> SESSION
    SESSION --> COMMIT["原子提交贡献<br/>PluginCatalog.Commit()"]
    COMMIT --> LOOKUP

    PH_EVENT --> RT_PUBLISH
    RT_PUBLISH --> EVENT_PUBLISH["查事件索引并入队<br/>InteractionEventPathModule.Publish"]
    EVENT_PUBLISH --> EVENT_GROUPS["读取事件类型匹配组<br/>PluginCatalog.SnapshotInteractions(kind)"]
    EVENT_PUBLISH --> EVENT_OWNER["读取不可变实例快照<br/>PluginHost.FindInteractionInstance"]
    EVENT_PUBLISH --> EVENT_ENQUEUE["受理有界投递<br/>PluginInteractionQueue.Enqueue"]
    EVENT_ENQUEUE --> EVENT_DRAIN["后台串行消费<br/>PluginInteractionQueue.DrainAsync"]
    EVENT_DRAIN --> EVENT_GATE["检查当前实例、加载代际和登记有效性"]
    EVENT_GATE --> EVENT_LEASE["检查门禁并获取租约<br/>PluginCallCoordinator.TryAcquireInvocation"]
    EVENT_LEASE --> EVENT_CALLBACK["调用交互观察实现<br/>IInteractionContribution.OnInteractionAsync"]
    EVENT_CALLBACK --> EVENT_REAL_END["等待真实 ValueTask 结束后释放租约"]

    RT_EVENT --> CALL_EVENT["执行统一调用<br/>PluginCallCoordinator.Invoke()"]
    CALL_EVENT --> EVENT_RAISE["广播旧版轮盘事件<br/>InteractionEventPathModule.RaiseWheelOpening() / RaiseWheelClosed()"]
    EVENT_RAISE --> HANDLER["调用旧事件回调<br/>PluginEventService"]
    HANDLER --> LEASE_EVENT["释放活动租约<br/>PluginInvocationLease.Dispose()"]

    RT_WHEEL --> CALL_WHEEL["执行异步调用<br/>PluginCallCoordinator.InvokeAsync()"]
    CALL_WHEEL --> WHEEL_QUERY["轮盘结构路径<br/>WheelStructurePathModule.QueryAsync()"]
    WHEEL_QUERY --> EMPTY["安全空结构快照<br/>PluginWheelStructureSnapshot.Empty"]

    PH_STOP --> STOP_NOTIFY["通知路径开始停止<br/>PluginRuntime.NotifyPluginStopping()"]
    STOP_NOTIFY --> PATH_STOP["通知路径开始停止<br/>PluginPathRegistry.NotifyPluginStopping()"]
    PATH_STOP --> REVOKE["撤销全部贡献<br/>PluginCatalog.RevokeAll(pluginId)"]
    PATH_STOP --> EVENT_REMOVE["撤销该插件的路由<br/>InteractionEventPathModule.OnPluginStopping()"]
    PH_STOP --> BEGIN_STOP["封闭入口并等待调用排空<br/>PluginInstance.BeginStopping()"]
    BEGIN_STOP --> CANCEL["宿主插件封装<br/>PluginInstance._stoppingCts.Cancel()"]
    BEGIN_STOP --> DRAIN["活动调用排空信号<br/>PluginInstance._callsDrained"]
    LEASE --> RELEASE["释放活动租约<br/>PluginInvocationLease.Dispose()"]
    LEASE_EVENT --> RELEASE
    EVENT_REAL_END --> RELEASE
    EVENT_REMOVE --> EVENT_STOP["停止队列并撤销待投递项<br/>PluginInteractionQueue.Stop"]
    RELEASE --> RELEASE_CALL["减少活动调用计数<br/>PluginInstance.ReleaseInvocation()"]
    RELEASE_CALL --> DRAIN
    DRAIN --> UNLOAD["卸载插件或程序集<br/>PluginInstance.Unload()"]
    UNLOAD --> SHUTDOWN["请求插件清理<br/>IStarPiePlugin.Shutdown()"]
    UNLOAD --> DISPOSE_TOKENS["释放注册凭据<br/>PluginInstance.DisposeTokens()"]
    UNLOAD --> ALC_UNLOAD["卸载插件或程序集<br/>PluginLoadContext.Unload()"]
```

![API 层级图](../pictures/2.API层级图.png)

### 2.2 这张树的阅读方式

```text
PluginHost
├─ 启动与登记
│  ├─ Initialize()
│  ├─ SyncFromDisk()
│  ├─ PluginScanner.ScanInstalledPlugin()
│  └─ PluginActionClaimRegistry.Rebuild()
│
├─ 安装
│  ├─ CommitInstallAsync()
│  ├─ PluginScanner.ScanSelectedDll()
│  ├─ CopyPayload()
│  ├─ PluginManifestReader.TryWrite()
│  └─ PluginRegistryStore.UpsertEntry()
│
├─ 运行时
│  └─ Runtime: PluginRuntime
│     ├─ Actions: ActionExecutionPathModule
│     ├─ Interactions: InteractionEventPathModule
│     ├─ WheelStructures: WheelStructurePathModule
│     ├─ Activation: PluginActivationCoordinator
│     ├─ Calls: PluginCallCoordinator
│     └─ Paths: PluginPathRegistry
│
├─ 动作执行
│  ├─ ExecutePluginAction()
│  ├─ ExecuteClaimedAction()
│  ├─ PluginCatalog.TryGetAction()
│  ├─ PluginInvoker.Invoke()
│  └─ IActionContribution.ExecuteAsync()
│
├─ 统一交互事件
│  ├─ PublishInteractionEvent()
│  ├─ PluginCatalog.SnapshotInteractions(kind)
│  ├─ FindInteractionInstance() 不争用安装锁
│  ├─ PluginInteractionQueue.Enqueue() / DrainAsync()
│  ├─ 当前实例、代际和活动调用租约
│  └─ IInteractionContribution.OnInteractionAsync()
│
├─ 插件实例
│  └─ Instances[pluginId]: PluginInstance
│     ├─ EnsureLoaded()
│     ├─ LoadCore()
│     ├─ _plugin: IStarPiePlugin
│     ├─ _pluginContext: PluginContext
│     └─ Unload()
│
└─ 停用与卸载
   ├─ DisableAsync()
   ├─ NotifyPluginStopping()
   ├─ PluginCatalog.RevokeAll()
   ├─ BeginStopping()
   ├─ ReleaseInvocation()
   ├─ IStarPiePlugin.Shutdown()
   └─ PluginLoadContext.Unload()
```
系统可以压缩为以下职责链：

```text
App
└─ PluginHost
   ├─ PluginRuntime
   │  ├─ PluginPathRegistry
   │  ├─ PluginActivationCoordinator
   │  ├─ PluginCallCoordinator
   │  ├─ ActionExecutionPathModule
   │  ├─ InteractionEventPathModule
   │  └─ WheelStructurePathModule
   ├─ PluginInstance
   │  ├─ PluginLoadContext
   │  ├─ PluginContext
   │  └─ IStarPiePlugin
   ├─ PluginCatalog
   ├─ PluginRegistryStore
   ├─ PluginScanner
   └─ PluginActionClaimRegistry
```

## 3. 宿主启动、扫描和登记

插件系统由 `App.OnStartup()` 调用 `PluginHost.Initialize()`。

```mermaid
sequenceDiagram
    participant APP as 应用启动<br/>App.OnStartup()
    participant HOST as 插件宿主门面<br/>PluginHost.Initialize()
    participant PATHS as 插件目录配置<br/>PluginPaths.Configure()
    participant SYNC as 插件宿主门面<br/>PluginHost.SyncFromDisk()
    participant SCAN as 静态扫描器<br/>PluginScanner.ScanInstalledPlugin()
    participant MANIFEST as 清单校验器<br/>PluginManifestReader.TryLoad()/Validate()
    participant STORE as 插件登记存储<br/>PluginRegistryStore
    participant CLAIM as 类型认领表<br/>PluginActionClaimRegistry.Rebuild()

    APP->>HOST: 初始化插件系统
    HOST->>PATHS: 配置插件目录
    HOST->>PATHS: 准备插件目录：EnsureDirectories()
    HOST->>SYNC: 同步磁盘插件
    SYNC->>STORE: 取得插件登记快照：SnapshotEntries()
    SYNC->>SCAN: 扫描已登记插件目录
    SCAN->>MANIFEST: 读取并校验 plugin.json/程序集元数据
    SCAN-->>SYNC: 返回静态扫描结果（PluginScanResult）
    SYNC->>STORE: 新增或更新登记：UpsertEntry()
    HOST->>CLAIM: 按登记快照重建认领表：Rebuild(SnapshotEntries())
    HOST-->>APP: 插件系统就绪
```

![宿主启动、扫描和持久化登记](../pictures/3.宿主启动、扫描和持久化登记.png)


### 3.1 关键节点

| 节点 | 作用 |
|---|---|
| `App.OnStartup()` | 主程序启动入口，调用 `PluginHost.Initialize()` |
| `PluginHost.Initialize()` | 初始化插件系统，默认不加载插件程序集 |
| `PluginPaths.Configure()` | 确定插件扫描目录和宿主可写目录 |
| `PluginPaths.ScanRoot` | 社区插件候选扫描目录 |
| `PluginPaths.Root` | 宿主维护的插件目录，通常是 `plugin-data` |
| `PluginHost.SyncFromDisk()` | 根据磁盘目录和 `registry.json` 同步插件实例 |
| `PluginScanner.ScanInstalledPlugin()` | 静态检查 DLL、入口类型、目标框架、依赖和哈希 |
| `PluginManifestReader.TryLoad()` | 读取 `plugin.json` 或程序集元数据 |
| `PluginManifestReader.Validate()` | 校验插件清单 |
| `PluginRegistryStore.SnapshotEntries()` | 读取持久化登记信息 |
| `PluginActionClaimRegistry.Rebuild()` | 根据登记表构建历史动作类型认领路由 |

### 3.2 两类注册表

```text
PluginRegistryStore
└─ registry.json
   ├─ 插件 ID
   ├─ 安装目录
   ├─ Enabled
   ├─ Official
   ├─ ClaimedTypes
   ├─ 版本和哈希
   └─ 登记与健康度状态

PluginCatalog
└─ 运行时内存目录
   ├─ 已注册动作
   ├─ 图标
   ├─ 国际化词条
   └─ 插件级设置页
```

`PluginRegistryStore` 解决“磁盘上登记了什么”，`PluginCatalog` 解决“当前已加载插件贡献了什么”。

## 4. 插件安装流程

```mermaid
flowchart TD
    SETTINGS["设置窗口<br/>SettingsWindow"]
    OFFICIAL["官方插件客户端<br/>OfficialPluginClient"]
    PREPARE["准备安装与识别<br/>PluginHost.PrepareInstall()"]
    SCAN["扫描所选程序集<br/>PluginScanner.ScanSelectedDll()"]
    CLASSIFY["分类手动安装来源<br/>PluginHost.ClassifyManualInstall()"]
    COMMIT["提交插件安装<br/>PluginHost.CommitInstallAsync()"]
    COPY["复制插件文件<br/>PluginHost.CopyPayload() / CopyDirectory()"]
    MANIFEST["写入插件清单<br/>PluginManifestReader.TryWrite()"]
    STORE["新增或更新登记<br/>PluginRegistryStore.UpsertEntry()"]
    CLAIM["重建类型认领表<br/>PluginActionClaimRegistry.Rebuild()"]
    EVENT["插件可用性变化通知<br/>PluginHost.PluginAvailabilityChanged"]

    SETTINGS --> PREPARE
    OFFICIAL --> PREPARE
    PREPARE --> SCAN
    SCAN --> CLASSIFY
    CLASSIFY --> COMMIT
    COMMIT --> COPY
    COMMIT --> MANIFEST
    COMMIT --> STORE
    COMMIT --> CLAIM
    COMMIT --> EVENT
```

![插件安装流程](../pictures/4.插件安装流程.png)


相关方法：

```text
PluginHost.PrepareInstall()
PluginScanner.ScanSelectedDll()
PluginHost.ClassifyManualInstall()
PluginHost.CommitInstallAsync()
PluginHost.CopyPayload()
PluginHost.CopyDirectory()
PluginManifestReader.TryWrite()
PluginRegistryStore.UpsertEntry()
PluginActionClaimRegistry.Rebuild()
```

安装流程的边界：

- 安装不等于加载。
- 安装不等于启用。
- `PluginInstallOptions.EnableAfterInstall` 决定安装后是否立即启用。
- 官方插件可以使用 `starpie.*` ID 和历史动作类型认领。
- 普通社区插件不能随意认领官方保留的顶层动作类型。
- 覆盖安装时清除旧 DLL 和清单，但保留插件的 `data` 和 `settings.json`。

## 5. 插件首次加载流程

插件采用惰性加载。插件登记在磁盘上，并不代表 DLL 已经加载。首次执行动作或显式预加载时，由 `PluginActivationCoordinator.EnsureLoaded()` 触发。

```mermaid
sequenceDiagram
    participant PATH as 动作执行路径<br/>ActionExecutionPathModule.Execute()
    participant ACT as 插件激活协调器<br/>PluginActivationCoordinator.EnsureLoaded()
    participant INS as 宿主插件封装<br/>PluginInstance.EnsureLoaded()
    participant SCAN as 静态扫描器<br/>PluginScanner.ScanInstalledPlugin()
    participant ALC as 程序集加载上下文<br/>PluginLoadContext.LoadFromAssemblyPath()
    participant CTX as 插件专属上下文<br/>PluginContext
    participant ENTRY as 插件入口<br/>IStarPiePlugin.Initialize()
    participant SESSION as 原子注册事务<br/>PluginRegistrationSession
    participant CATALOG as 运行时贡献目录<br/>PluginCatalog.Commit()

    PATH->>ACT: 确保目标已加载：EnsureLoaded(pluginId)
    ACT->>INS: 检查启用状态并加载：EnsureLoaded(requireEnabled: true)
    INS->>INS: 取得加载锁：lock(_loadGate)
    INS->>SCAN: 重新静态扫描插件
    SCAN-->>INS: 返回静态扫描结果（PluginScanResult）
    INS->>ALC: 创建并加载程序集
    ALC-->>INS: 返回已加载程序集（Assembly）
    INS->>INS: 定位 IStarPiePlugin 实现类
    INS->>CTX: 创建 PluginContext
    INS->>SESSION: 创建注册事务：PluginCatalog.BeginSession(pluginId)
    INS->>ENTRY: 初始化并登记贡献：Initialize(context)
    ENTRY->>CTX: 注册动作：Actions.Register(...)
    ENTRY->>CTX: 注册多语言：I18n.Register(...)
    ENTRY->>CTX: 注册图标：Icons.RegisterSvg(...)
    ENTRY->>CTX: 注册设置页：SettingsPage.Register(...)
    CTX->>SESSION: 暂存动作、多语言、图标和设置页
    INS->>CATALOG: 原子提交注册事务：session.Commit()
    CATALOG-->>INS: 原子提交成功
    INS->>INS: 开放调用入口：OpenInvocationGate()
    INS-->>ACT: 返回激活就绪状态：PluginActivationResult.Ready
    ACT-->>PATH: 返回可调用 PluginInstance
```

![插件首次加载流程](../pictures/5.插件首次加载流程.png)


实际加载链：

```text
PluginInstance.EnsureLoaded()
└─ PluginInstance.LoadCore()
   ├─ PluginScanner.ScanInstalledPlugin()
   ├─ new PluginLoadContext(...)
   ├─ PluginLoadContext.LoadFromAssemblyPath(...)
   ├─ assembly.GetType(...)
   ├─ new PluginContext(...)
   ├─ PluginCatalog.BeginSession(...)
   ├─ IStarPiePlugin.Initialize(...)
   ├─ PluginRegistrationSession.Commit(...)
   └─ PluginInstance.OpenInvocationGate()
```

![IStarPiePlugin 实例宿主封装过程](../pictures/6.IStarPiePlugin 实例宿主封装过程.png)

重要边界：

```text
Initialize()
└─ 只负责注册贡献点，不应执行耗时 IO、长期线程或复杂业务

Commit()
└─ Initialize 成功后一次性提交全部贡献点

Initialize 失败
└─ PluginRegistrationSession.Discard()
   不留下半个动作、半个图标或半套词条
```

## 6. `IPluginContext` 服务边界

插件不直接引用主程序的 `StarPie.dll`，而是只使用宿主创建的 `PluginContext`。

```mermaid
flowchart LR
    PLUGIN["初始化<br/>IStarPiePlugin.Initialize(IPluginContext)"]
    CONTEXT["插件专属上下文<br/>PluginContext"]

    ACTIONS["动作注册入口<br/>PluginContext.Actions"]
    I18N["多语言注册入口<br/>PluginContext.I18n"]
    ICONS["图标注册入口<br/>PluginContext.Icons"]
    SETTINGS_PAGE["插件设置页注册<br/>PluginContext.SettingsPage"]
    HOST["宿主动作服务<br/>PluginContext.Host"]
    COMMANDS["命令执行服务<br/>PluginContext.Commands"]
    SHELL["系统外壳服务<br/>PluginContext.Shell"]
    WINDOWS["窗口控制服务<br/>PluginContext.Windows"]
    CAPTURE["截屏与识别服务<br/>PluginContext.ScreenCapture"]
    SYSTEM["系统功能服务<br/>PluginContext.System"]
    WHEEL["轮盘呼出服务<br/>PluginContext.Wheel"]
    EVENTS["旧版事件订阅<br/>PluginContext.Events"]
    DISPATCHER["界面线程调度<br/>PluginContext.Dispatcher"]
    SETTINGS["本插件配置<br/>PluginContext.Settings"]

    PLUGIN --> CONTEXT
    CONTEXT --> ACTIONS
    CONTEXT --> I18N
    CONTEXT --> ICONS
    CONTEXT --> SETTINGS_PAGE
    CONTEXT --> HOST
    CONTEXT --> COMMANDS
    CONTEXT --> SHELL
    CONTEXT --> WINDOWS
    CONTEXT --> CAPTURE
    CONTEXT --> SYSTEM
    CONTEXT --> WHEEL
    CONTEXT --> EVENTS
    CONTEXT --> DISPATCHER
    CONTEXT --> SETTINGS
```

![IPluginContext 的服务边界](../pictures/7.IPluginContext 的服务边界.png)


接口与实现对应关系：

| SDK 属性 | 宿主实现 |
|---|---|
| `IPluginContext.Actions` | `PluginActionRegistry` |
| `IPluginContext.I18n` | `PluginI18nRegistry` |
| `IPluginContext.Icons` | `PluginIconRegistry` |
| `IPluginContext.SettingsPage` | `PluginSettingsPageRegistry` |
| `IPluginContext.Host` | `PluginHostActionInvoker` |
| `IPluginContext.Commands` | `PluginCommandService` |
| `IPluginContext.Shell` | `PluginShellService` |
| `IPluginContext.Windows` | `PluginWindowService` |
| `IPluginContext.ScreenCapture` | `PluginScreenCaptureService` |
| `IPluginContext.System` | `PluginSystemService` |
| `IPluginContext.Wheel` | `PluginWheelService` |
| `IPluginContext.Events` | `PluginEventService` |
| `IPluginContext.Dispatcher` | `PluginDispatcherFacade` |
| `IPluginContext.Settings` | `PluginSettings` |

SDK 接口定义：

```text
plugin/sdk/StarPie.Plugin.Abstractions/IPluginContext.cs
plugin/sdk/StarPie.Plugin.Abstractions/IStarPiePlugin.cs
```

宿主实现：

```text
WinPieGestures/Plugin/PluginContext.cs
WinPieGestures/Plugin/PluginHostServices.cs
```

### 6.1 可选交互上下文

旧 `IPluginContext` 的成员不变，宿主的 `PluginContext` 同时实现派生扩展：

```mermaid
flowchart LR
    P["初始化<br/>IStarPiePlugin.Initialize(IPluginContext)"] --> CHECK{"是否支持可选交互上下文？<br/>IInteractionPluginContext"}
    CHECK -->|是| REG["交互注册入口<br/>Interactions: IInteractionRegistry"]
    REG --> REGISTER["注册交互观察贡献<br/>Register(IInteractionContribution)"]
    REGISTER --> SESSION["进入初始化注册事务<br/>Initialize"]
    CHECK -->|否| UNSUPPORTED["宿主不支持此 SDK 1.10 契约"]
    P --> OLD["原有事件接口保持不变<br/>context.Events"]
```

新观察插件应声明 API 1.10，旧宿主会按契约门禁拒绝不兼容包；类型检查不是把 manifest 版本改低的替代方案。
## 7. 动作注册流程

插件注册动作时，动作首先进入当前插件的 `PluginRegistrationSession`，不会立即写入全局运行时目录。

```mermaid
flowchart LR
    ENTRY["初始化<br/>IStarPiePlugin.Initialize(IPluginContext)"]
    ACTIONREG["注册贡献或路径<br/>PluginActionRegistry.Register(IActionContribution)"]
    DESCRIPTOR["贡献描述信息<br/>IActionContribution.Descriptor"]
    SESSION["暂存动作贡献<br/>PluginRegistrationSession.StageAction()"]
    CATALOG["原子提交贡献<br/>PluginCatalog.Commit()"]
    REGISTRATION["动作登记记录<br/>PluginActionRegistration"]
    FULLID["完整动作ID<br/>PluginActionRegistration.FullId"]

    ENTRY --> ACTIONREG
    ACTIONREG --> DESCRIPTOR
    ACTIONREG --> SESSION
    SESSION --> CATALOG
    CATALOG --> REGISTRATION
    REGISTRATION --> FULLID
```

![动作注册流程](../pictures/8.动作注册流程.png)


动作完整 ID：

```text
PluginActionRegistration.FullId
└─ PluginId + "." + ContributionId

示例：
starpie.plugin.cadcommand.cadCommand
```

关键节点：

| 节点 | 作用 |
|---|---|
| `IActionContribution` | 插件动作实现接口 |
| `IActionContribution.Descriptor` | 动作名称、描述、参数、执行类型、超时等 |
| `PluginActionRegistry.Register()` | 接收插件动作 |
| `PluginRegistrationSession.StageAction()` | 暂存动作 |
| `PluginCatalog.Commit()` | 初始化成功后提交动作 |
| `PluginActionRegistration` | 宿主内部动作注册记录 |
| `PluginActionRegistration.FullId` | `PluginId.ContributionId` 的完整动作 ID |
| `PluginCatalog.TryGetAction()` | 根据 FullId 查询动作 |

动作实现的主要执行方法：

```csharp
Task<ActionResult> IActionContribution.ExecuteAsync(
    PluginActionInput input,
    CancellationToken cancellationToken)
```

## 8. 交互贡献注册流程

插件在 Initialize 通过可选上下文注册 `IInteractionContribution`，先暂存，成功返回并 Commit 后才进入全局目录。失败时关闭登记并丢弃暂存引用，与第 7 节的动作注册遵循同一个事务。
```mermaid
sequenceDiagram
    participant I as 宿主插件封装<br/>PluginInstance
    participant P as 插件入口<br/>IStarPiePlugin
    participant R as 交互贡献注册器<br/>PluginInteractionRegistry
    participant S as 原子注册事务<br/>PluginRegistrationSession
    participant C as 运行时贡献目录<br/>PluginCatalog
    I->>P: 调用初始化接口：Initialize(IPluginContext)
    P->>R: 通过可选上下文登记：context.Interactions.Register(contribution)
    R->>P: 读取贡献描述符 Descriptor，仅登记时一次
    R->>R: 校验ID和事件位，记录所属加载代际
    R->>S: 暂存交互贡献登记
    R-->>P: 返回可释放的撤销凭据（IDisposable）
    alt 初始化成功且登记无冲突
        P-->>I: 初始化返回
        I->>C: 原子提交登记：Commit(session)
        C->>C: 原子提交全部贡献并重建事件类型索引
        C-->>I: 提交成功
        I->>I: 开放活动调用入口
    else 初始化抛出异常或提交冲突
        I->>I: 执行失败清理：Teardown
        I->>S: 丢弃暂存内容并关闭登记入口
        S->>S: 释放暂存的贡献引用
        Note over R,C: 没有生效观察者，旧注册器拒绝晚到登记
    end
```

交互完整 ID：

```text
PluginInteractionRegistration.FullId
└─ PluginId + "." + InteractionDescriptor.Id

示例：
com.example.wheelobserver.observeWheel
```

关键节点：

| 节点 | 作用 |
|---|---|
| `IInteractionPluginContext.Interactions` | 可选 SDK 交互注册入口 |
| `IInteractionContribution` | 插件观察实现接口 |
| `InteractionDescriptor` | 短 ID 和关注事件的位筛选 |
| `PluginInteractionRegistry.Register()` | 校验/快照并加入当前注册事务 |
| `PluginRegistrationSession` | 保存暂存交互贡献，失败关闭登记 |
| `PluginCatalog.Commit()` | 所有贡献一起成功才可见 |
| `PluginInteractionRegistration` | 绑定贡献、插件实例和加载代际 |
| `PluginCatalog.SnapshotInteractions(kind)` | 读取当前事件类型下按插件分组的稳定匹配快照 |

交互实现的主要调用方法：

```csharp
ValueTask IInteractionContribution.OnInteractionAsync(
    InteractionEvent input,
    CancellationToken cancellationToken)
```
## 9. 动作执行主路径

这是当前插件系统中最完整、实际投入使用的调用路径。

```mermaid
flowchart TD
    GESTURE["普通手势控制器<br/>GestureController"]
    EXEC["执行动作<br/>ActionExecutor.Execute(ActionItem)"]

    CLAIM_CHECK["判断官方历史类型<br/>PluginHost.IsOfficialClaimedType()"]
    CLAIM_RESOLVE["解析历史类型认领<br/>PluginHost.TryResolveClaimedType()"]
    CLAIM_EXEC["执行历史认领动作<br/>PluginHost.ExecuteClaimedAction()"]

    PLUGIN_EXEC["派发插件动作<br/>PluginHost.ExecutePluginAction()"]
    RUNTIME_EXEC["执行插件动作<br/>PluginRuntime.ExecuteAction()"]
    RUNTIME_CLAIM["执行历史认领动作<br/>PluginRuntime.ExecuteClaimedAction()"]

    CALLS["执行统一调用<br/>PluginCallCoordinator.Invoke()"]
    ACTION_PATH["执行动作<br/>ActionExecutionPathModule.Execute()"]
    CLAIM_PATH["执行认领动作<br/>ActionExecutionPathModule.ExecuteClaimed()"]

    REQUEST["复制动作请求<br/>PluginActionRequest.TryCreate() / CreateClaimed()"]
    ACTIVATE["确保目标插件已加载<br/>PluginActivationCoordinator.EnsureLoaded()"]
    CATALOG_GET["按完整ID查找动作<br/>PluginCatalog.TryGetAction()"]
    PARAMS["校验参数或清单<br/>PluginParameterValidator.Validate()"]
    CUSTOM_VALIDATE["校验参数或清单<br/>IActionContribution.Validate()"]
    INVOKE["执行统一调用<br/>PluginInvoker.Invoke()"]
    EXECUTE_ASYNC["调用插件动作实现<br/>IActionContribution.ExecuteAsync()"]
    RESULT["插件动作结果<br/>ActionResult"]
    HEALTH["记录调用健康状态<br/>PluginInstance.RecordInvoke()"]

    GESTURE --> EXEC
    EXEC --> CLAIM_CHECK
    CLAIM_CHECK -->|历史顶层类型| CLAIM_RESOLVE
    CLAIM_RESOLVE --> CLAIM_EXEC
    CLAIM_EXEC --> RUNTIME_CLAIM
    RUNTIME_CLAIM --> CALLS
    CALLS --> CLAIM_PATH
    CLAIM_CHECK -->|Type=Plugin| PLUGIN_EXEC
    PLUGIN_EXEC --> RUNTIME_EXEC
    RUNTIME_EXEC --> CALLS
    CALLS --> ACTION_PATH
    ACTION_PATH --> REQUEST
    CLAIM_PATH --> REQUEST
    REQUEST --> ACTIVATE
    ACTIVATE --> CATALOG_GET
    CATALOG_GET --> PARAMS
    PARAMS --> CUSTOM_VALIDATE
    CUSTOM_VALIDATE --> INVOKE
    INVOKE --> EXECUTE_ASYNC
    EXECUTE_ASYNC --> RESULT
    RESULT --> HEALTH
```

![动作执行主路径](../pictures/9.动作执行主路径.png)


实际调用链：

```text
GestureController
└─ ActionExecutor.Execute(ActionItem)
   ├─ PluginHost.IsOfficialClaimedType()
   │  └─ PluginHost.TryResolveClaimedType()
   │     └─ PluginActionClaimRegistry.TryResolve()
   │        └─ PluginHost.ExecuteClaimedAction()
   │
   └─ PluginHost.ExecutePluginAction()
      └─ PluginRuntime.ExecuteAction()
         └─ PluginCallCoordinator.Invoke()
            └─ ActionExecutionPathModule.Execute()
               ├─ PluginActionRequest.TryCreate()
               ├─ PluginActivationCoordinator.EnsureLoaded()
               ├─ PluginCatalog.TryGetAction()
               ├─ PluginParameterValidator.Validate()
               ├─ IActionContribution.Validate()
               └─ PluginInvoker.Invoke()
                  └─ IActionContribution.ExecuteAsync()
```

## 10. 交互事件调用主路径

宿主把已经发生的轮盘语义按筛选交给已有贡献；与第 9 节的动作执行并列，共用实例/租约/停用治理，但不触发加载，也不等待业务结果。

```mermaid
flowchart TD
    SRC["生成不可变交互事件<br/>PluginInteractionSession"] --> HOST["发布交互事件<br/>PluginHost.PublishInteractionEvent"]
    HOST --> RT["发布交互事件<br/>PluginRuntime.PublishInteractionEvent"]
    RT --> PATH["查事件索引并入队<br/>InteractionEventPathModule.Publish"]
    PATH --> GROUPS["读取事件类型匹配组<br/>PluginCatalog.SnapshotInteractions(kind)"]
    PATH --> OWNER["读取不可变实例快照<br/>FindInteractionInstance"]
    GROUPS --> MATCH{"匹配登记、当前实例与加载代际是否有效？"}
    OWNER --> MATCH
    MATCH -->|否| ZERO["不受理，不触发插件加载"]
    MATCH -->|是| Q["每插件有界队列，待处理容量 128"]
    Q --> MERGE{"相邻选择变化是否属于同一会话与订阅组？"}
    MERGE -->|是| LATEST["替换为最新选择快照"]
    MERGE -->|否| FULL{"队列是否已满？"}
    FULL -->|否| ADD["追加投递项"]
    FULL -->|是| REPLACE{"是否存在可淘汰的选择变化？"}
    REPLACE -->|是| EVICT["淘汰一条选择变化，追加新事件"]
    REPLACE -->|否| SUSPEND["暂停此代际路由，清空待处理项，异步取消并记录诊断"]
    LATEST --> ACCEPT["统计已受理的匹配贡献数"]
    ADD --> ACCEPT
    EVICT --> ACCEPT
    ACCEPT --> RETURN["立即返回受理数，不等待回调完成"]
    ACCEPT --> DRAIN["后台串行消费<br/>DrainAsync"]
    DRAIN --> RECEIVERS["只遍历投递项内已匹配登记，不再筛选事件"]
    RECEIVERS --> LEASE["检查实例、代际和登记，获取活动租约"]
    LEASE --> CALLBACK["调用交互观察实现<br/>IInteractionContribution.OnInteractionAsync"]
    CALLBACK --> REALEND["等待真实 ValueTask 结束后释放租约"]
```

每插件一条队列，多贡献共用串行消费者，插件间隔离；没有独立监听进程，也不为每个订阅创建永久线程。Sequence 在合并、筛选、淘汰和停用后可以有间隙；路由溢出不修改用户 Enabled，不创建无界备用队列。End 源头至多一次不意味着每个观察者必达。

### 10.1 事件产生与呈现完成

```mermaid
sequenceDiagram
    participant G as 普通与粘滞轮盘源<br/>GestureController / StickyWheelSession
    participant S as 交互语义会话<br/>PluginInteractionSession
    participant W as 轮盘窗口<br/>RadialWindow
    participant D as 界面渲染调度<br/>Dispatcher Render
    participant H as 插件宿主门面<br/>PluginHost
    G->>S: 建立语义会话，在首个事件前绑定配置
    G->>W: 请求内部呈现：Present(..., onPresented)
    W->>D: 排入既有 Render 回调
    W-->>G: 请求返回，尚未报告 Presented
    G->>S: 等待 Render 期间更新选择快照
    Note over S: 缓存初始目标，不提前报告 Presented
    D->>W: 执行 Render 回调
    W->>W: 检查版本、处置和呈现状态守卫
    alt 帧版本有效且内容揭示成功
        W->>W: 校准中心、保持置顶并揭示 MainGrid
        W->>W: 内容揭示后再次检查版本守卫
        W->>S: 调用呈现完成通知：onPresented
        S->>H: 发布呈现事件和初始选择：Presented、SelectionChanged
        W->>W: 启动既有入场动画
    else 旧帧、已撤回、已处置或内容揭示失败
        Note over W,S: 不报告 Presented，按所属会话处理结束或失败
    end
```

这表示 WPF 内容揭示成功通知，不声称显示器已经完成物理合成。旧四参数 Present 仍可调用；旧 SDK Opening/Closed 和 tracker 的时机没有暗中改成新事件。

### 10.2 原子撤回与旧呈现竞争

```mermaid
sequenceDiagram
    participant C as 插件调用者
    participant T as 粘滞轮盘会话<br/>StickyWheelSession
    participant S as 交互语义会话<br/>PluginInteractionSession
    participant U as 旧界面呈现回调
    participant H as 插件宿主门面<br/>PluginHost
    C->>T: 请求撤回轮盘：Dismiss(pluginId)
    T->>T: 在 Gate 锁内原子预留撤回：ReserveDismissal
    T->>S: 冻结插件撤回原因：FreezeEnd(DismissedByPlugin)
    Note over S: 标记结束并冻结不可变终结快照，不发布
    T->>T: 拆下当前会话并释放 Gate 锁
    par 调用者延后完成撤回
        C->>T: 锁外完成撤回：FinishDismissal
        T->>S: 按插件撤回原因完成：End(DismissedByPlugin)
    and 旧界面回调抢先于完成清理
        U->>T: 旧 Present 命中非当前会话守卫
        U->>S: 尝试按呈现前替代结束：End(SupersededBeforePresentation)
    end
    S->>S: 原子取走终结快照，仅一次
    S->>H: 锁外发布已冻结的 DismissedByPlugin 原因
    Note over S,H: 后续 End 不得改写原因或重复发布
```

### 10.3 停用与调用排空

```mermaid
sequenceDiagram
    participant H as 插件宿主门面<br/>PluginHost
    participant I as 宿主插件封装<br/>PluginInstance
    participant C as 运行时贡献目录<br/>PluginCatalog
    participant Q as 每插件有界队列<br/>PluginInteractionQueue
    participant P as 交互观察实现<br/>IInteractionContribution
    H->>I: 开始停止，拒绝新调用并取消实例
    H->>C: 撤销全部贡献登记
    H->>Q: 撤销插件路由并停止队列
    Q->>Q: 清空待投递引用，在发布线程之外取消
    Note over I,P: 在途回调真正结束前保持活动租约
    alt 回调响应取消或最终结束
        P-->>Q: 真实 ValueTask 已结束
        Q->>I: 释放活动调用租约
        I-->>H: 活动调用已排空
        H->>I: 清理插件并卸载程序集
    else 回调超出宽限期仍未结束
        H-->>H: 沿用等待中（Pending）或需重启治理
        Note over H,I: 不得强制卸载仍在执行的插件代码
    end
```

旧 RegisterWheelOpening/Closed 仍在交互路径模块中托管，保持原 UI 线程同步契约；新贡献不是旧回调的重命名或自动桥接。完整动作路径与事件路径共享实例/激活/租约/停止治理，保留各自强类型接口和调度规则，不合并成 `Invoke(string, object)`。

无副作用实际记录夹具与强制交错验证见[`scratch/interaction-event-tests/`](../../scratch/interaction-event-tests/)；自动/源码复核不替代真实窗口视觉、DPI、多屏与手感验收。音效仍未从内置系统迁移。
## 11. `PluginInvoker` 调用治理

插件动作不能直接调用 `ExecuteAsync()`，必须经过 `PluginInvoker.Invoke()`。

```mermaid
flowchart TD
    INVOKE["执行统一调用<br/>PluginInvoker.Invoke()"]
    KIND["动作调度类别<br/>PluginActionRegistration.Kind"]
    LEASE["检查门禁并获取租约<br/>PluginInstance.TryAcquireInvocation()"]
    INVOKE_SEQ["串行动作调度<br/>PluginInvoker.InvokeSequential()"]
    INVOKE_BG["后台动作调度<br/>PluginInvoker.InvokeInBackground()"]
    TIMEOUT["取消与超时源<br/>CancellationTokenSource(timeout)"]
    OBSERVE["观察超时但未结束的任务<br/>PluginInvoker.ObserveTimedOutTask()"]
    DISPOSE["释放活动租约<br/>PluginInvocationLease.Dispose()"]
    INTERPRET["解释插件返回结果<br/>PluginInvoker.Interpret()"]
    APPLY["应用执行结果<br/>PluginInvoker.ApplyOutcome()"]
    RECORD["记录调用健康状态<br/>PluginInstance.RecordInvoke()"]
    QUARANTINE["隔离故障插件<br/>PluginInstance.MarkQuarantined()"]

    INVOKE --> LEASE
    LEASE --> KIND
    KIND -->|串行 Sequential| INVOKE_SEQ
    KIND -->|后台 Background| INVOKE_BG
    INVOKE_SEQ --> TIMEOUT
    INVOKE_BG --> TIMEOUT
    TIMEOUT --> INTERPRET
    TIMEOUT --> OBSERVE
    OBSERVE --> DISPOSE
    INTERPRET --> APPLY
    APPLY --> RECORD
    RECORD --> QUARANTINE
    INVOKE_SEQ --> DISPOSE
    INVOKE_BG --> DISPOSE
```

![PluginInvoker 的调用治理](../pictures/10.PluginInvoker 的调用治理.png)


治理逻辑：

- `Sequential`：当前调用线程等待动作完成。
- `Background`：将动作放入后台任务执行，尽快返回调用路径。
- 超时后不立即认为插件已经结束。
- `ObserveTimedOutTask()` 继续观察真实任务。
- 真实任务结束后，`PluginInvocationLease.Dispose()` 才减少活动调用数。
- 连续失败达到阈值后，`PluginInstance.MarkQuarantined()` 自动隔离插件。

## 12. 活动调用租约与停用

安全卸载的核心不是直接调用 GC，而是先阻止新调用，再等待已有调用结束。

```mermaid
sequenceDiagram
    participant UI as 设置窗口<br/>SettingsWindow
    participant HOST as 插件宿主门面<br/>PluginHost.DisableAsync()
    participant RUNTIME as 统一调用运行时<br/>PluginRuntime.NotifyPluginStopping()
    participant PATHS as 调用路径注册表<br/>PluginPathRegistry.NotifyPluginStopping()
    participant ACTION as 动作执行路径<br/>ActionExecutionPathModule.OnPluginStopping()
    participant EVENT as 交互事件路径<br/>InteractionEventPathModule.OnPluginStopping()
    participant INSTANCE as 宿主插件封装<br/>PluginInstance.BeginStopping()
    participant LEASE as 活动调用租约<br/>PluginInvocationLease.Dispose()
    participant UNLOAD as 宿主插件封装<br/>PluginInstance.Unload()
    participant ALC as 程序集加载上下文<br/>PluginLoadContext.Unload()

    UI->>HOST: 异步停用插件：DisableAsync(pluginId)
    HOST->>RUNTIME: 通知插件开始停止：NotifyPluginStopping(pluginId)
    RUNTIME->>PATHS: 通知插件开始停止：NotifyPluginStopping(pluginId)
    PATHS->>ACTION: 撤销本插件路径：OnPluginStopping(pluginId)
    PATHS->>EVENT: 撤销本插件路径：OnPluginStopping(pluginId)
    ACTION->>ACTION: 撤销全部贡献：PluginCatalog.RevokeAll(pluginId)
    EVENT->>EVENT: 删除事件订阅
    HOST->>INSTANCE: 封闭入口并等待排空：BeginStopping()
    INSTANCE->>INSTANCE: 关闭新调用入口：_acceptingCalls = false
    INSTANCE->>INSTANCE: 发送取消：cancellation.Cancel()
    INSTANCE-->>HOST: 等待 _activeCallCount == 0
    LEASE->>INSTANCE: 减少活动调用计数：ReleaseInvocation()
    HOST->>UNLOAD: 卸载程序集：Unload()
    UNLOAD->>UNLOAD: 请求插件清理：IStarPiePlugin.Shutdown()
    UNLOAD->>UNLOAD: 释放注册凭据：DisposeTokens()
    UNLOAD->>ALC: 卸载程序集：Unload()
    ALC-->>UNLOAD: 等待 WeakReference 回收
```

![活动调用租约和停用流程](../pictures/11.活动调用租约和停用流程.png)


关键节点：

| 节点 | 作用 |
|---|---|
| `PluginInstance.TryAcquireInvocation()` | 判断插件是否允许新调用，并增加活动调用数 |
| `PluginInvocationLease` | 代表一次仍在执行的插件回调 |
| `PluginHost.PublishInteractionEvent()` | `WinPieGestures/Plugin/PluginHost.cs` | 统一事件发布门面，返回受理贡献数 |
| `PluginHost.FindInteractionInstance()` | `WinPieGestures/Plugin/PluginHost.cs` | 无安装锁实例快照查找 |
| `PluginInteractionRegistry.Register()` | `WinPieGestures/Plugin/PluginInteractionRegistration.cs` | 校验、快照并暂存交互贡献 |
| `PluginCatalog.SnapshotInteractions(kind)` | `WinPieGestures/Plugin/PluginCatalog.cs` | 读取事件类型对应的稳定匹配组 |
| `InteractionEventPathModule.Publish()` | `WinPieGestures/Plugin/PluginPathModules.cs` | 匹配有效实例与贡献，只入队不加载 |
| `PluginInteractionQueue.Enqueue() / DrainAsync()` | `WinPieGestures/Plugin/PluginInteractionQueue.cs` | 有界合并/背压与每插件串行消费者 |
| `PluginInteractionSession.Presented() / Update() / Confirm()` | `WinPieGestures/Plugin/PluginInteractionSession.cs` | 语义快照和会话内顺序 |
| `PluginInteractionSession.FreezeEnd() / End()` | `WinPieGestures/Plugin/PluginInteractionSession.cs` | 锁内冻结原因、竞争取走并锁外发布 |
| `StickyWheelSession.ReserveDismissal() / FinishDismissal()` | `WinPieGestures/Plugin/StickyWheelSession.cs` | 原子撤回与锁外完成 |
| `WheelPresentationCompletion.TryComplete()` | `WinPieGestures/RadialWindow.xaml.cs` | 真实 Render 揭示前后守卫及成功通知 |
| `IInteractionContribution.OnInteractionAsync()` | `plugin/sdk/StarPie.Plugin.Abstractions/Interactions.cs` | 插件具体观察实现 |
| `PluginInvocationLease.Dispose()` | 释放调用租约 |
| `PluginInstance.BeginStopping()` | 拒绝新调用、取消停用令牌、等待活动调用排空 |
| `PluginInstance.ReleaseInvocation()` | 活动调用数减一 |
| `PluginInstance.Unload()` | 调用插件清理、断开引用、卸载 ALC |
| `IStarPiePlugin.Shutdown()` | 插件自身释放事件、线程和定时器 |
| `PluginLoadContext.Unload()` | 请求卸载插件程序集 |
| `PluginInstance.RecheckUnload()` | 延迟检查 ALC 是否真正回收 |

## 13. 插件调用宿主已有功能

插件使用 `_context.Host` 时，执行的是宿主已经实现的功能，而不是插件自行重新实现。

```mermaid
flowchart LR
    PLUGIN["宿主动作服务<br/>IPluginContext.Host"]
    HOSTAPI["宿主动作服务接口<br/>IHostActionInvoker"]
    INVOKER["宿主动作服务适配器<br/>PluginHostActionInvoker"]

    HOTKEY["请求发送快捷键<br/>PluginHostActionInvoker.SendHotkey()"]
    TEXT["请求输入文本<br/>PluginHostActionInvoker.SendText()"]
    LAUNCH["请求启动程序<br/>PluginHostActionInvoker.Launch()"]
    FOLDER["请求打开文件夹<br/>PluginHostActionInvoker.OpenFolder()"]
    URL["请求打开网址<br/>PluginHostActionInvoker.OpenUrl()"]
    CLIP["请求写入剪贴板<br/>PluginHostActionInvoker.SetClipboardText()"]

    EXEC_HOTKEY["执行快捷键输入<br/>ActionExecutor.ExecuteHotkey()"]
    EXEC_TEXT["执行文本输入<br/>ActionExecutor.SendTextInput()"]
    EXEC_LAUNCH["执行程序启动<br/>ActionExecutor.ExecuteLaunch()"]
    EXEC_FOLDER["执行文件夹操作<br/>ActionExecutor.ExecuteFolder()"]
    EXEC_URL["执行网址打开<br/>ActionExecutor.ExecuteWebUrl()"]
    EXEC_CLIP["安全写入剪贴板<br/>ActionExecutor.SafeSetClipboardText()"]

    PLUGIN --> HOSTAPI
    HOSTAPI --> INVOKER
    INVOKER --> HOTKEY
    INVOKER --> TEXT
    INVOKER --> LAUNCH
    INVOKER --> FOLDER
    INVOKER --> URL
    INVOKER --> CLIP
    HOTKEY --> EXEC_HOTKEY
    TEXT --> EXEC_TEXT
    LAUNCH --> EXEC_LAUNCH
    FOLDER --> EXEC_FOLDER
    URL --> EXEC_URL
    CLIP --> EXEC_CLIP
```

![插件调用宿主已有功能的路径](../pictures/12.插件调用宿主已有功能的路径.png)


典型调用链：

```text
插件 IActionContribution.ExecuteAsync()
└─ _context.Host.SendHotkey(...)
   └─ PluginHostActionInvoker.SendHotkey()
      └─ ActionExecutor.ExecuteHotkey()
```

适配层位于：

```text
WinPieGestures/Plugin/PluginHostServices.cs
```

职责关系：

- 插件负责声明动作和业务流程。
- 主程序继续负责输入模拟、剪贴板、启动程序、打开文件夹等成熟实现。
- 插件不应直接引用 `ActionExecutor`。
- `PluginHostServices.cs` 将 SDK 接口转接到宿主内部实现。
- 能力门禁由 `PluginHostActionInvoker` 和 `PluginGatedService` 执行。

## 14. 三条插件调用路径

`PluginRuntime` 在构造函数中注册三条路径：

```text
PluginRuntime.PluginRuntime(...)
├─ _paths.Register(Actions)
├─ _paths.Register(Interactions)
└─ _paths.Register(WheelStructures)
```

```mermaid
flowchart LR
    RUNTIME["统一调用运行时<br/>PluginRuntime"]
    ACTION["动作执行路径<br/>ActionExecutionPathModule<br/>PathId = action-execution"]
    EVENT["交互事件路径<br/>InteractionEventPathModule<br/>PathId = interaction-event"]
    WHEEL["轮盘结构路径<br/>WheelStructurePathModule<br/>PathId = wheel-structure"]

    RUNTIME --> ACTION
    RUNTIME --> EVENT
    RUNTIME --> WHEEL
```

![三条调用路径](../pictures/13.三条调用路径.png)


### 14.1 动作执行路径

状态：**已实际使用，是当前的主路径。**

入口：

```text
PluginRuntime.ExecuteAction()
PluginRuntime.ExecuteClaimedAction()
```

实现：

```text
ActionExecutionPathModule.Execute()
ActionExecutionPathModule.ExecuteClaimed()
```

包含：

- 动作查找；
- 惰性加载；
- 参数校验；
- 插件自定义校验；
- `Sequential` / `Background` 调度；
- 超时与取消；
- 活动调用租约；
- 健康度和熔断。

### 14.2 交互事件路径

当前源码已实现统一观察贡献：Initialize 原子注册后，宿主只向已加载的匹配贡献投递只读事件。采用每插件有界后台队列与代际/租约治理，不在广播时加载插件，不返回导航或动作结果。

详细流程见[交互贡献注册](#8-交互贡献注册流程)和[交互事件调用主路径](#10-交互事件调用主路径)；公共接口见[API 第 5 节](plugin-system-api-and-performance.md#5-交互事件-api)。旧 Opening/Closed 的同步契约不变。
### 14.3 轮盘结构路径

状态：**安全占位，尚未向插件开放正式结构契约。**

入口：

```text
PluginRuntime.QueryWheelStructureAsync()
```

实现：

```text
WheelStructurePathModule.QueryAsync()
└─ PluginWheelStructureSnapshot.Empty
```

当前不会让插件直接创建或重建 `RadialWindow`，也不会向插件暴露 `WheelProfile`、`ActionItem` 或 WPF 控件。

## 15. 最终职责边界

```mermaid
flowchart TB
    APP["应用启动<br/>App.OnStartup()"]
    HOST["初始化<br/>PluginHost.Initialize() / ExecutePluginAction() / DisableAsync()"]
    RUNTIME["执行插件动作<br/>PluginRuntime.ExecuteAction() / NotifyPluginStopping()"]
    PATH["执行动作<br/>ActionExecutionPathModule.Execute()"]
    INSTANCE["确保目标插件已加载<br/>PluginInstance.EnsureLoaded() / Unload()"]
    CATALOG["原子提交贡献<br/>PluginCatalog.Commit() / TryGetAction() / RevokeAll()"]
    CONTEXT["插件专属上下文<br/>PluginContext"]
    SDK["初始化<br/>IStarPiePlugin.Initialize() / IActionContribution.ExecuteAsync()"]
    INVOKER["执行统一调用<br/>PluginInvoker.Invoke()"]
    SERVICE["请求发送快捷键<br/>PluginHostActionInvoker.SendHotkey() / Launch() / OpenFolder()"]
    CORE["执行快捷键输入<br/>ActionExecutor.ExecuteHotkey() / ExecuteLaunch() / ExecuteFolder()"]

    APP --> HOST
    HOST --> RUNTIME
    RUNTIME --> PATH
    PATH --> INSTANCE
    INSTANCE --> CONTEXT
    CONTEXT --> SDK
    INSTANCE --> CATALOG
    PATH --> CATALOG
    PATH --> INVOKER
    INVOKER --> SDK
    SDK --> SERVICE
    SERVICE --> CORE
```

![最终的职责边界](../pictures/14.最终的职责边界.png)


| 层 | 具体节点 | 责任 |
|---|---|---|
| 主程序入口层 | `App.OnStartup()` | 启动插件系统 |
| 宿主门面层 | `PluginHost` | 对主程序暴露唯一插件入口 |
| 运行时组合层 | `PluginRuntime` | 组合路径、激活和调用治理 |
| 路径层 | `ActionExecutionPathModule` | 定义动作执行语义 |
| 生命周期层 | `PluginInstance` | 管理单个插件的加载、停用和卸载 |
| 隔离层 | `PluginLoadContext` | 隔离并卸载插件程序集 |
| 注册层 | `PluginCatalog` | 保存已经提交的贡献点 |
| 插件上下文层 | `PluginContext` | 向插件暴露 SDK 服务 |
| SDK 契约层 | `IStarPiePlugin`、`IActionContribution` | 插件实现的公共接口 |
| 调度层 | `PluginInvoker` | 超时、取消、后台执行和失败统计 |
| 宿主服务层 | `PluginHostActionInvoker` | 将 SDK 调用转到主程序功能 |
| 主程序执行层 | `ActionExecutor`、`WindowTiler` 等 | 执行真实系统操作 |

## 16. 关键类和方法速查

| 类或方法 | 所属文件 | 作用 |
|---|---|---|
| `PluginHost.Initialize()` | `WinPieGestures/Plugin/PluginHost.cs` | 初始化插件系统 |
| `PluginHost.SyncFromDisk()` | `WinPieGestures/Plugin/PluginHost.cs` | 同步磁盘插件和登记表 |
| `PluginHost.CommitInstallAsync()` | `WinPieGestures/Plugin/PluginHost.cs` | 完成插件安装登记 |
| `PluginHost.ExecutePluginAction()` | `WinPieGestures/Plugin/PluginHost.cs` | 执行标准 `Plugin` 动作 |
| `PluginHost.ExecuteClaimedAction()` | `WinPieGestures/Plugin/PluginHost.cs` | 执行官方插件认领的历史动作 |
| `PluginHost.DisableAsync()` | `WinPieGestures/Plugin/PluginHost.cs` | 停用并等待插件结束 |
| `PluginRuntime.ExecuteAction()` | `WinPieGestures/Plugin/PluginRuntime.cs` | 进入动作调用路径 |
| `PluginRuntime.NotifyPluginStopping()` | `WinPieGestures/Plugin/PluginRuntime.cs` | 广播插件停止事件 |
| `PluginPathRegistry.Register()` | `WinPieGestures/Plugin/PluginPathModules.cs` | 注册调用路径模块 |
| `PluginActivationCoordinator.EnsureLoaded()` | `WinPieGestures/Plugin/PluginPathModules.cs` | 检查状态并惰性加载插件 |
| `PluginCallCoordinator.Invoke()` | `WinPieGestures/Plugin/PluginRuntime.cs` | 统一包装路径调用和异常治理 |
| `PluginInstance.EnsureLoaded()` | `WinPieGestures/Plugin/PluginInstance.cs` | 在加载锁内加载单个插件 |
| `PluginInstance.LoadCore()` | `WinPieGestures/Plugin/PluginInstance.cs` | 扫描、加载、初始化和提交贡献点 |
| `PluginInstance.TryAcquireInvocation()` | `WinPieGestures/Plugin/PluginInstance.cs` | 获取活动调用租约 |
| `PluginInstance.BeginStopping()` | `WinPieGestures/Plugin/PluginInstance.cs` | 拒绝新调用并等待活动调用排空 |
| `PluginInstance.Unload()` | `WinPieGestures/Plugin/PluginInstance.cs` | 清理插件并卸载 ALC |
| `PluginCatalog.BeginSession()` | `WinPieGestures/Plugin/PluginCatalog.cs` | 创建原子注册会话 |
| `PluginCatalog.Commit()` | `WinPieGestures/Plugin/PluginCatalog.cs` | 原子提交动作、交互贡献、图标、词条和设置页 |
| `PluginCatalog.TryGetAction()` | `WinPieGestures/Plugin/PluginCatalog.cs` | 查询运行时动作 |
| `PluginContext` | `WinPieGestures/Plugin/PluginContext.cs` | 实现旧上下文及可选 `IInteractionPluginContext` |
| `PluginActionRegistry.Register()` | `WinPieGestures/Plugin/PluginContext.cs` | 暂存动作贡献点 |
| `PluginInvoker.Invoke()` | `WinPieGestures/Plugin/PluginInvoker.cs` | 统一执行插件动作 |
| `PluginInvoker.InvokeSequential()` | `WinPieGestures/Plugin/PluginInvoker.cs` | 串行动作调度 |
| `PluginInvoker.InvokeInBackground()` | `WinPieGestures/Plugin/PluginInvoker.cs` | 后台动作调度 |
| `PluginInvocationLease.Dispose()` | `WinPieGestures/Plugin/PluginInstance.cs` | 释放活动调用租约 |
| `PluginScanner.ScanInstalledPlugin()` | `WinPieGestures/Plugin/PluginScanner.cs` | 静态扫描已安装插件 |
| `PluginManifestReader.TryLoad()` | `WinPieGestures/Plugin/PluginManifestReader.cs` | 读取插件清单 |
| `PluginLoadContext.Load()` | `WinPieGestures/Plugin/PluginLoadContext.cs` | 隔离加载插件依赖 |
| `PluginHostActionInvoker.SendHotkey()` | `WinPieGestures/Plugin/PluginHostServices.cs` | 将 SDK 快捷键调用转到宿主 |
| `PluginHostActionInvoker.Launch()` | `WinPieGestures/Plugin/PluginHostServices.cs` | 将启动程序调用转到宿主 |
| `PluginHostActionInvoker.OpenFolder()` | `WinPieGestures/Plugin/PluginHostServices.cs` | 将打开文件夹调用转到宿主 |
| `ActionExecutor.Execute()` | `WinPieGestures/ActionExecutor.cs` | 主程序动作总入口 |
| `ActionExecutor.ExecuteHotkey()` | `WinPieGestures/ActionExecutor.cs` | 执行快捷键模拟 |
| `ActionExecutor.ExecuteLaunch()` | `WinPieGestures/ActionExecutor.cs` | 执行程序启动 |
| `ActionExecutor.ExecuteFolder()` | `WinPieGestures/ActionExecutor.cs` | 执行文件夹打开 |

## 17. 一句话总结

```text
PluginHost 是唯一门面；
PluginRuntime 负责统一调用治理；
PluginPathModule 负责具体调用语义；
PluginInstance 负责单插件生命周期；
PluginCatalog 负责运行时贡献点；
PluginContext 负责 SDK 边界；
PluginInvoker 负责安全执行；
ActionExecutor 等主程序类负责真正的宿主功能。
```

插件可以拥有自己的业务逻辑，但不能越过 `IPluginContext` 直接访问主程序内部类型。

