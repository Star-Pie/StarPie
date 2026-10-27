# 进程插件运行时（SDK 1.14）

从 1.14 起，插件不必再是 .NET 程序集。清单里写 `"runtime": "process"`，
宿主就会把它当成一个**独立进程**：不加载任何代码，只负责触发与生命周期。

## 为什么要这一层

在这之前，唯一能写插件的方式是产出一个托管程序集，进程内加载进 `AssemblyLoadContext`。
这把整个生态锁死在 C# 上，而 README 里自己就写着：

> **进程内加载**：ALC 用于管理程序集加载与卸载，**不是安全沙箱，也不提供进程级崩溃隔离**。

于是"想接一个已有的 Rust / Go / C++ / Python 程序"这件事，只能靠把它重写一遍。
进程形态补的正是这一块：语言中立、崩溃隔离、卸载 = 杀进程，且不触碰任何既有契约。

## 两种形态

| | `assembly`（缺省） | `process` |
|---|---|---|
| 加载方式 | `AssemblyLoadContext.LoadFromAssemblyPath` | `Process.Start` |
| 入口 | `IStarPiePlugin.Initialize` | 一次触发起一个进程 |
| 能拿到 `IPluginContext` | 是（全部宿主服务） | **否** |
| 动作声明 | 运行时 `IActionContribution` 自描述 | 清单 `actions[]` 静态声明 |
| 参数表单 | 宿主渲染 `ParameterField` | **无** |
| 设置页 | `ISettingsPageRegistry` | **无**（插件自己读写配置文件） |
| 崩溃影响 | 波及宿主 | 仅该进程 |
| 卸载 | ALC 回收（有引用残留风险） | `kill` 进程树 |
| 语言 | 仅 .NET | 任意 |

## 清单

```json
{
  "schemaVersion": 1,
  "id": "starpie.plugin.selinga",
  "name": "Selinga 截屏翻译",
  "version": "0.4.0",
  "apiVersion": "1.14",
  "minHostVersion": "2.0.0",
  "runtime": "process",
  "command": [
    "bin/selinga.exe",
    "--starpie-action", "{action}",
    "--target-lang", "{languageCode}"
  ],
  "actions": [
    {
      "id": "translate",
      "displayName": "截屏翻译",
      "description": "框选屏幕区域，识别后译成目标语言",
      "category": "效率工具"
    }
  ],
  "capabilities": ["ScreenCapture", "Ui"]
}
```

进程形态**不需要** `assembly` / `entryType` / `targetFramework` / `platform`；
写了 `assembly` 会被忽略并在详情页提示，而不是报错——降低迁移时的摩擦。

### 占位符

`command` 的每一项都做纯字符串替换，**不做 shell 解析**（不展开 `%`、`~`、通配符）：

| 占位符 | 取值 |
|---|---|
| `{action}` | 本次触发的动作短 ID（`actions[].id`） |
| `{languageCode}` | 宿主当前界面语言，如 `zh-CN` |
| `{foregroundProcess}` | 触发时的前台进程名（不含扩展名） |
| `{foregroundTitle}` | 前台窗口标题 |
| `{cursorX}` / `{cursorY}` | 触发时的鼠标屏幕坐标（物理像素） |
| `{dataDir}` | 插件私有数据目录的绝对路径（已确保存在） |

**未知占位符原样保留**。这条是刻意的：让插件能用 `{...}` 表达自己的字面量，
也比"宿主少支持一个占位符就整个动作失败"要好。

`command[0]` 若为相对路径，相对**插件目录**解析；解析结果必须落在插件目录内，
否则拒绝加载（防目录穿越）。绝对路径允许，但在安装确认页会额外标注。

## 生命周期

- **每次执行 = 拉起一次。宿主从不等待退出。** 动作在后台线程触发后立即返回，
  进程活多久由插件自己决定。这解决了"UI 型插件会把动作队列卡到用户关窗口"的问题。
- **宿主只结束自己拉起的进程。** 每次 `Process.Start` 记录 PID，插件停用/卸载时按 PID
  结束进程树（best-effort，失败只记日志）。用户在外部启动的同一程序不会被波及。
- **重复触发由插件自己处理。** 宿主不做去重、不做单实例。约定是：第二次启动应当尽快退出，
  把"重新框选 / 再次显示"的语义留在插件进程内实现（Selinga 用命名事件做这件事）。
- **v0.5 没有 `plugin.start` / `plugin.stop`。** 常驻形态不需要宿主托管——插件第一次被触发后
  自己留在后台即可。需要显式起停时再作为 1.15 的增量加。

## 输出通道：进程活着，管道就必须开着

这是踩过一次真故障之后写下的硬约束。

宿主用 `RedirectStandardOutput/Error` 收集插件输出。**一旦持有 `Process` 的对象被释放，
.NET 会关闭那两个管道**；而常驻型插件在几秒之后才第一次写日志是很常见的。此时：

| 语言 | 对 EPIPE 的反应 |
|---|---|
| Rust | `eprintln!` / `println!` **panic**，进程直接退出 |
| Python | `BrokenPipeError`，若未捕获则退出 |
| C / C++ | `fwrite` 返回错误，通常被忽略 —— 看起来"没事"，实际日志全丢 |

真实表现：插件被第一次触发时正常，3 秒后宿主关闭管道，用户再次触发时插件恰好写一行日志，
于是**插件无声消失**，而被误判成"窗口逻辑有问题"。

因此：

1. 宿主**不得**在动作方法返回时释放 `Process`。句柄交给进程登记簿，直到进程退出或插件停用才释放。
2. 读取器必须一直 `BeginOutputReadLine` / `BeginErrorReadLine`，否则子进程写满缓冲区会阻塞。
3. 退出后用一次无超时的 `WaitForExit()` 冲刷读取器，再取退出码 —— 少了这一步，最后几行
   stderr（往往正是崩溃原因）会丢。

## 能力声明：告知 ≠ 强制

程序集插件的能力位是**真门禁**：未声明 `ScreenCapture` 调用 `IHostScreenCaptureService`
会抛 `PluginCapabilityDeniedException`。

进程插件**做不到这一点**。它就是一个普通程序，想调什么 Win32 就调什么，
宿主没有拦截点（除非上驱动级隔离，那不是本项目的方向）。所以：

- `capabilities` 对进程插件是**安装确认页的如实告知**，不是运行时门禁；
- 安装确认页必须显式写出：「将运行独立程序」「以你的用户权限运行，**不继承宿主提权**」；
- 这条区别要同步写进 `docs/security-model.md`，不能让人误以为能力位一视同仁。

## 兼容矩阵

| 场景 | 行为 |
|---|---|
| 旧宿主（≤1.13）读 `apiVersion: "1.14"` 清单 | 被现有次版本校验拒绝，提示"当前最高支持 1.x"（`PluginManifestReader.cs:221`）。**不会**退化成"找不到入口程序集"。 |
| 旧宿主读 1.14 但把 `apiVersion` 写成 1.13 | 按程序集形态处理 → 报"缺少入口程序集"。这是作者写错，不是兼容性问题。 |
| 新宿主读存量 19 个程序集插件 | **完全不变**：`runtime` 缺省为 `assembly`，走原路径。 |
| 新宿主读未知 `runtime` 值（如 `"proc"`） | 拒绝，错误信息列出合法值。**不静默退回 `assembly`** —— 否则用户看到的是指向错误方向的报错。 |
| `runtime: "process"` 但 `command` 为空 | 拒绝。 |
| `command[0]` 解析到插件目录外 | 拒绝（除非是绝对路径且安装页已标注）。 |
| 进程插件声明了 `targetFramework` 却高于宿主 | **不校验**：它不是 .NET 程序集，拿 TFM 卡它没有意义。 |

## 打包与完整性

进程插件没有"入口程序集"这个中心，所以 `module.manifest.json` 从单程序集哈希扩成文件清单：

```json
{
  "id": "starpie.plugin.selinga",
  "version": "0.4.0",
  "runtime": "process",
  "files": [
    { "path": "plugin.json", "sha256": "…", "size": 812 },
    { "path": "bin/selinga.exe", "sha256": "…", "size": 8126464 }
  ]
}
```

安装时逐文件比对；签名目录的语义（SHA-256 + 大小 + 兼容条件）不变。
`.spkg` 仍是确定性 ZIP，只是允许携带 exe 与资源子目录。

## 实现落点

**宿主（13 改 2 新，约 800 行）**

| 文件 | 改动 | 规模 |
|---|---|---|
| `plugin/sdk/.../PluginManifest.cs` | ✅ 已完成：`Runtime` / `Command` / `Actions` / `PluginProcessAction` / `PluginRuntimeTypes` | 已有 |
| `plugin/sdk/.../PluginApi.cs` | ✅ 已完成：1.13 → 1.14 | 已有 |
| `WinPieGestures/Plugin/PluginManifestReader.cs` | ✅ 按 runtime 分流：跳过 TFM/assembly/entryType，校验 command 与动作非空 | 已完成 |
| `WinPieGestures/Plugin/PluginScanner.cs` | ✅ 打开程序集**之前**分支，含目录穿越拦截 | 已完成 |
| `WinPieGestures/Plugin/PluginCandidate.cs` | ✅ 加 `Runtime` | 已完成 |
| `WinPieGestures/Plugin/PluginScanResult.cs` | ✅ 加 `Runtime` / `Command`，`DllPath` 可空 | 已完成 |
| `WinPieGestures/Plugin/PluginInstance.cs` | ✅ `Load`/`Unload` 按 runtime 分支；`IsLoaded` 按进程态判定 | 已完成 |
| **`WinPieGestures/Plugin/ProcessActionContribution.cs`** | ✅ 新：实现 `IActionContribution`，占位符替换 + 拉起 + 退出码映射 | 已完成 |
| `WinPieGestures/Plugin/PluginInstallConfirmation.cs` | 「将运行独立程序」「不继承提权」 | ~25 |
| `WinPieGestures/Plugin/PluginCapabilityLabels.cs` | 进程态措辞 | ~10 |
| `WinPieGestures/Plugin/PluginDetailModel.cs` / `PluginDisplayText.cs` | 详情页显示 runtime 与命令行 | ~15 |
| **`WinPieGestures/Plugin/PluginProcessRuntime.cs`** | 新：PID 记账、结束进程树、启动失败判定 | ~120 |
| `WinPieGestures/Plugin/PluginSelfTest.cs` | 三条断言 | ~60 |

**不改**：`PluginHost.cs`（两处 `new PluginInstance` 无需动）、`PluginInvoker.cs`（派发的还是 `IActionContribution`）、
`PluginLoadContext.cs`、`PluginContext.cs`、`PluginParameterForm.cs`。

**插件仓库（7 改）**

| 文件 | 改动 |
|---|---|
| `catalog/module-catalog.schema.json` | module 加可选 `runtime` |
| `catalog/module-registry.schema.json` | 同上 |
| `build/Pack-Module.ps1` | 带 exe/资源；`files[]` 哈希取代 `assemblySha256` |
| `build/Import-ModuleRegistryFromSource.ps1` | 透传 `runtime` |
| `build/Generate-ModuleCatalog.ps1` | 透传 `runtime` |
| `tests/catalog/Validate-CatalogV2.ps1` | 校验 `runtime` 值域 |
| `docs/security-model.md`、`docs/module-contract.md` | 信任链与"告知≠强制" |

## v0.5 明确不做

- JSON-RPC（`initialize` / `action.execute` / 插件反向调用宿主）
- 进程插件的声明式设置页与参数表单
- `plugin.start` / `plugin.stop` 与常驻会话管理
- 窗口归属协议（`uiModel`）与轮盘开合事件
- 非 x64 平台

## 验收标准

1. `PluginSelfTest`：缺 `command` 被拒；未知 `runtime` 被拒且提示合法值；存量程序集插件加载行为不变。
2. 端到端：打包 `starpie.plugin.selinga` → 装进「插件与扩展」→ 绑到轮盘扇区 → 触发 → Selinga 框选层出现 → 再触发一次 → 重新框选。
3. 停用插件后，任务管理器里不留该插件的残留进程。
