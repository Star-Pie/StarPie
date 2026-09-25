# StarPie 插件系统：当前 API 与性能参考

> **文档状态**：当前实现参考（Current Reference）
> **适用版本**：StarPie `v1.8.0-beta.2` 及以上
> **SDK 契约**：`StarPie.Plugin.Abstractions` API `1.6`
> **最后核对**：2026-09-20
> **首选入门文档**：[插件开发快速入门](plugin-development-quickstart.md)
>
> 本文描述当前已经实现并开放的插件 API、安装模型、运行时约束和性能纪律。插件作者应先阅读快速入门；本文用于查阅接口边界、兼容性和性能要求。当前接口的最终事实来源是 `plugin/sdk/StarPie.Plugin.Abstractions/` 中的公共类型和 XML 注释。
>
> 历史设计草案和早期实现说明已经移到 `archive/`，不再作为当前 API 的依据。

---

## 目录

1. [当前架构边界](#1-当前架构边界)
2. [公共 API 总览](#2-公共-api-总览)
3. [插件清单与兼容性](#3-插件清单与兼容性)
4. [动作 API](#4-动作-api)
5. [宿主服务 API](#5-宿主服务-api)
6. [安装、登记与加载模型](#6-安装登记与加载模型)
7. [生命周期与卸载纪律](#7-生命周期与卸载纪律)
8. [性能和线程约束](#8-性能和线程约束)
9. [能力声明与安全边界](#9-能力声明与安全边界)
10. [调试、自检与常见故障](#10-调试自检与常见故障)
11. [当前不开放的能力](#11-当前不开放的能力)
12. [参考基准的口径](#12-参考基准的口径)

---

# 1. 当前架构边界

## 1.1 三层程序集

```text
社区 / 官方插件 DLL
    └── 只引用 StarPie.Plugin.Abstractions + BCL

StarPie.Plugin.Abstractions.dll
    └── 只包含接口、枚举、DTO、清单模型和常量

StarPie.exe
    └── 扫描、安装、加载、注册、调度、停用和卸载插件
```

插件**禁止**引用：

```text
StarPie.dll
WinPieGestures.*
主程序内部的 AppConfig、ActionExecutor、WindowTiler 等类型
```

插件应该只引用：

```text
StarPie.Plugin.Abstractions.dll
```

这样插件和主程序通过公共契约通信，宿主内部可以继续重构而不改变插件的类型身份。

## 1.2 当前支持的贡献类型

当前真正开放并由插件页和动作运行时支持的是：

- 动作贡献：`IActionContribution`；
- SVG 图标：`IIconRegistry.RegisterSvg`；
- 多语言词条：`II18nRegistry`。

`IPluginContext` 中的交互事件服务可以用于当前已提供的事件订阅；轮盘结构和其它扩展路径仍属于宿主内部扩展接缝，不能把规划中的接口当作当前稳定公共 API 使用。

## 1.3 当前权威文档层级

```text
第一次开发插件
    → plugin-development-quickstart.md

查当前 SDK 类型和方法
    → plugin/sdk/StarPie.Plugin.Abstractions/

查宿主加载、注册、租约和卸载实现
    → plugin-system-architecture.md

查性能、线程和兼容性约束
    → 本文
```

---

# 2. 公共 API 总览

## 2.1 插件入口：`IStarPiePlugin`

插件程序集需要提供一个 `IStarPiePlugin` 实现：

```csharp
public interface IStarPiePlugin
{
    void Initialize(IPluginContext context);
    void Shutdown();
}
```

约束：

- 入口类必须是 `public`、非抽象、可无参构造；
- 一个程序集通常只有一个入口实现；多个实现时用 manifest 的 `entryType` 指定；
- 构造函数不得做 IO、启动线程、网络请求或弹窗；
- `Initialize` 主要用于注册动作、图标、词条和事件；
- `Shutdown` 必须幂等，不得抛出异常；
- 插件不得把未等待的长期任务伪装成已经完成的动作。

## 2.2 上下文：`IPluginContext`

插件由宿主注入一个 `IPluginContext`：

```text
IPluginContext
├── Me             插件元数据
├── PluginDirectory 插件安装目录（只读）
├── DataDirectory   插件私有数据目录
├── Log             插件日志
├── Settings        插件私有设置
├── Actions         动作注册
├── I18n            词条注册与读取
├── Icons           SVG 图标注册
├── Host            快捷键、启动程序、文件夹、网址、剪贴板
├── Commands        CMD / PowerShell / WSL 命令
├── Shell           Explorer Shell 动词
├── Windows         窗口控制
├── ScreenCapture   截屏和 OCR
├── System          系统预设
├── Info            宿主环境信息
├── Notify          非侵入式通知
├── Wheel           呼出配置轮盘（SDK 1.5 起）
├── SettingsPage    插件级参数页声明（SDK 1.6 起）
├── KeyboardRemap   受控键盘空间重映射（SDK 1.7 起）
├── Events          宿主事件订阅
└── Dispatcher      UI 线程调度
```

`_context.Host` 只是宿主服务的一部分；插件不调用它时，仍可能通过 `Commands`、`Windows`、`System` 等服务使用宿主功能。插件也可以完全使用自己的 BCL、P/Invoke、COM 或网络实现。

## 2.3 动作注册表：`IActionRegistry`

```csharp
IDisposable token = context.Actions.Register(contribution);
```

注册动作时，宿主会读取：

- `ActionDescriptor`；
- `Parameters`；
- 参数约束；
- 调度类别和超时；
- 插件显示名、图标和词条引用。

返回的 `IDisposable` 是撤销凭据。插件应在 `Shutdown` 中释放自己保存的 token，宿主也会在停用时兜底撤销该插件的全部贡献。

## 2.4 运行时动作 ID

每个插件有一个插件级 ID，每个动作有一个插件内短 ID：

```text
插件 ID：com.example.timer
动作短 ID：start
完整动作 ID：com.example.timer.start
```

宿主使用完整动作 ID 在 `PluginCatalog` 中查找注册记录，注册记录保存指向实际 `IActionContribution` 实例的接口引用，再通过接口多态调用 `Validate`、`Preview` 或 `ExecuteAsync`。

类名不是持久化契约；`ActionDescriptor.Id` 一旦发布，应保持稳定。

---

# 3. 插件清单与兼容性

## 3.1 `plugin.json` 当前字段

最小推荐清单：

```json
{
  "schemaVersion": 1,
  "id": "com.example.myplugin",
  "name": "我的插件",
  "description": "插件简介",
  "author": "作者",
  "homepage": "https://example.com",
  "license": "MIT",
  "version": "1.0.0",
  "apiVersion": "1.4",
  "minHostVersion": "1.8.0-beta.1",
  "targetFramework": "net8.0-windows",
  "platform": "win-x64",
  "assembly": "StarPie.Plugin.MyPlugin.dll",
  "entryType": "StarPie.Plugin.MyPlugin.MyPlugin",
  "capabilities": [],
  "contributions": {
    "actions": true,
    "icons": false,
    "i18n": false
  },
  "tags": ["示例"]
}
```

主要字段：

| 字段 | 当前要求或用途 |
|---|---|
| `schemaVersion` | 当前为 `1` |
| `id` | 全局唯一，社区插件使用反向域名风格 |
| `version` | 插件自身的语义化版本 |
| `apiVersion` | 当前 SDK 契约为 `1.4`；主版本必须匹配 |
| `minHostVersion` | 插件要求的最低 StarPie 版本 |
| `maxHostVersion` | 可选，限制最高支持的宿主版本 |
| `targetFramework` | 不得高于宿主；推荐 `net8.0-windows` |
| `platform` | 当前必须为 `win-x64` |
| `assembly` | 入口程序集文件名 |
| `entryType` | `IStarPiePlugin` 实现的完整类型名 |
| `capabilities` | 插件实际需要的能力声明 |
| `contributions` | 安装确认页使用的贡献类型预声明 |
| `tags` | 插件分类和搜索标签 |
| `sha256` | 可选的入口程序集完整性校验值 |

## 3.2 SDK 和宿主兼容性

当前宿主：

```text
目标框架：net8.0-windows10.0.19041.0
平台：win-x64
SDK 契约：1.4
```

兼容规则：

- SDK 主版本不一致时拒绝加载；
- 插件目标框架不得高于宿主；
- 插件必须是 .NET 程序集；
- 当前平台必须是 `win-x64`；
- `minHostVersion` 不满足时拒绝安装或加载；
- 插件输出目录不得包含第二份 `StarPie.Plugin.Abstractions.dll`；
- 当前插件系统不支持插件携带自己的主程序副本或引用 `StarPie.dll`。

## 3.3 动作类型认领

社区插件和新开发的官方功能统一使用：

```text
ActionItem.Type = "Plugin"
PluginActionRef = PluginId + ContributionId
```

`claimedTypes` 只用于官方插件接管已经发布过的历史动作类型，例如 `Launch`、`Command` 或 `System`。社区插件不得填写，也不应为新功能创建新的顶层动作类型。

---

# 4. 动作 API

## 4.1 `IActionContribution`

```csharp
public interface IActionContribution
{
    ActionDescriptor Descriptor { get; }
    IReadOnlyList<ParameterField> Parameters { get; }
    string? Validate(IReadOnlyDictionary<string, string> parameters);
    string Preview(IReadOnlyDictionary<string, string> parameters);
    Task<ActionResult> ExecuteAsync(
        PluginActionInput input,
        CancellationToken cancellationToken);
}
```

### `Descriptor`

```csharp
public ActionDescriptor Descriptor => new()
{
    Id = "start",
    DisplayName = "开始计时",
    Description = "启动一个计时任务。",
    Category = "效率工具",
    IconKey = "Timer",
    Kind = ActionKind.Sequential,
    TimeoutSeconds = 3
};
```

`Id` 只允许插件内短 ID；宿主会拼接插件 ID。不要在这里写完整 ID。

### `Parameters`

插件声明字段，宿主生成统一风格的参数表单。支持：

```text
Text / MultilineText / Number / Bool / Folder
File / Enum / Hotkey / Color
```

参数值以字符串字典持久化。使用 `PluginActionInput.Parameter`、`Bool`、`Int`、`Double` 读取，数字使用不变文化解析。

### `Validate`

宿主会在两个时机调用：

1. 用户保存动作时；
2. 用户实际执行动作前。

宿主先执行声明式约束，再调用插件自己的 `Validate`。通过返回 `null` 或空字符串；失败返回面向用户的一句话原因。

### `Preview`

用于动作列表副标题和设置界面预览。必须很快完成：

- 不做文件 IO；
- 不做网络请求；
- 不启动进程；
- 不修改系统状态；
- 不依赖耗时计算。

### `ExecuteAsync`

真正执行动作。必须：

- 响应 `CancellationToken`；
- 返回代表真实工作的完整 `Task`；
- 成功返回 `ActionResult.Empty` 或 `ActionResult.Ok(...)`；
- 失败返回 `ActionResult.Fail("用户可理解的原因")`；
- 不弹 `MessageBox`；
- 不启动未等待的长期后台任务。

## 4.2 `ActionResult`

```csharp
return ActionResult.Empty;

return ActionResult.Ok("已完成", silent: false);

return ActionResult.Fail("输入文件不存在，请检查路径。");
```

插件环境不满足条件时，只有在它确实是动作失败时才返回 `Fail`。例如某项可选硬件能力不存在，若动作仍然完成了可接受的降级，应返回成功并通过消息说明降级结果，避免触发连续失败隔离。

## 4.3 调度类别

### `Sequential`

默认值，适合：

- 快捷键和文本输入；
- 剪贴板；
- 当前前台窗口操作；
- 启动程序后立即交互。

它占用唯一的动作顺序线程，执行必须短且可预测。

### `Background`

适合：

- 网络请求；
- 文件遍历和文件处理；
- 图片、文本或数据计算；
- COM/WMI/硬件访问等可能较慢的操作。

后台动作可能并发调用同一个贡献实例。共享状态必须自行同步；调用期间不要修改静态全局状态。

---

# 5. 宿主服务 API

## 5.1 `IHostActionInvoker`：通用动作原语

```csharp
context.Host.SendHotkey("Ctrl+Shift+G");
context.Host.SendText("Hello");
context.Host.Launch(path, arguments, runAsStandardUser: false);
context.Host.OpenFolder(folder);
context.Host.OpenUrl(url, "Default");
context.Host.SetClipboardText(text);
string? text = context.Host.GetClipboardText();
```

这些服务复用主程序已经验证过的输入、前台窗口和 Shell 逻辑。需要根据实际行为声明相应能力。

## 5.2 其它服务

| 服务 | 用途 | 运行时门禁 |
|---|---|---|
| `Commands` | 在 CMD、PowerShell 或 WSL 中运行命令 | `Process` |
| `Shell` | 对活动 Explorer 选中项执行 Shell 动词 | `Process` |
| `Windows` | 平铺、置顶、透明度、跨屏、激活任务栏槽位 | `WindowControl` |
| `ScreenCapture` | 框选截屏并进行 OCR | `ScreenCapture` |
| `System` | 最小化、任务视图、音量、锁屏、关机等预设 | `InputSimulation`，按实际行为补充 `Process` |
| `Notify` | 托盘气泡等非侵入式通知 | `Ui`（按实际行为） |
| `Log` | 插件独立日志 | 无 |
| `Settings` | 插件私有设置 | 无 |
| `Events` | 宿主事件订阅 | 按实际行为 |
| `Dispatcher` | 切换到 UI 线程 | `Ui` |

`IHostWindowService.Layouts`、`IHostSystemService.Presets` 等元数据清单可以用于生成参数选项；真正执行对应操作时才会进行能力检查。

## 5.3 自己实现功能

插件不必调用宿主服务，可以在 `ExecuteAsync` 中使用自己的：

- .NET BCL；
- 文件和网络 API；
- P/Invoke；
- COM/WMI；
- 第三方系统接口（但当前插件项目仍建议保持零 NuGet 依赖）。

宿主服务的价值是复用已经验证的 StarPie 行为，不是插件必须使用的唯一实现方式。插件自己的实现仍必须遵守动作结果、取消、线程和能力声明纪律。

---

## 5.4 插件级设置页（SDK 1.6）

插件可以在 `Initialize()` 中通过 `context.SettingsPage.Register(...)` 声明一张插件级参数页。宿主在插件管理卡片上渲染“设置”入口，并复用动作参数使用的 `PluginParameterForm`。

- 每个插件最多注册一张设置页；
- 设置页只声明字段，不提供 XAML、回调或变更事件；
- 值写入插件自己的 `settings.json`，插件通过 `context.Settings` 或 `SettingsPage.GetValue()` 读取；
- 设置页不新增能力位。

## 5.5 宿主轮盘服务（SDK 1.5）

常驻形态插件可以通过 `context.Wheel.ShowWheel(x, y)` 呼出用户配置的宿主轮盘，并通过 `DismissWheel()` 收起自己发起的轮盘。该服务需要声明 `Wheel` 能力，并与 `Ui` 分开。

插件不应复刻 `RadialWindow` 或自行实现第二套轮盘。
# 6. 安装、登记与加载模型

## 6.1 两个目录

```text
<程序目录>\plugin\
    只读社区候选区：只扫描，不自动安装，不写入，不删除

%LOCALAPPDATA%\StarPie\plugin-data\
    可写宿主区：安装副本、registry.json、health.json、插件 data\
```

便携模式只改变可写宿主区的位置；程序目录下的 `plugin\` 仍是候选区。

## 6.2 从安装到执行

```text
DLL / plugin.json
    ↓ 静态扫描（不加载程序集）
用户确认安装
    ↓ 复制到 plugin-data\<pluginId>\
写入 registry.json
    ↓ 用户启用插件
创建 PluginInstance
    ↓ 首次调用时惰性加载程序集
调用 IStarPiePlugin.Initialize(context)
    ↓ 注册动作、图标和词条
原子提交到 PluginCatalog
    ↓ FullId 查询具体 IActionContribution
调用 Validate / Preview / ExecuteAsync
```

启动时会读取 `registry.json`、静态扫描已登记目录并恢复官方历史类型认领表；默认不会加载所有插件程序集。只有 `Preload` 或首次实际调用才会激活插件。

## 6.3 持久化内容的边界

`registry.json` 保存插件级信息：

- 插件 ID、版本、安装目录；
- 启用状态和来源；
- 官方身份和历史类型认领快照；
- 入口程序集哈希；
- 能力确认和健康状态关联信息。

它不会保存插件所有动作的完整实现。动作清单在 `Initialize()` 中重新注册到内存里的 `PluginCatalog`。用户动作槽位则保存在主配置的 `PluginActionRef` 和参数字典中。

---

# 7. 生命周期与卸载纪律

## 7.1 生命周期

```text
静态扫描
  → 安装登记
  → 启用
  → ALC 加载
  → 创建入口实例
  → Initialize
  → 原子提交贡献点
  → 动作调用
  → 停用
  → Shutdown
  → 撤销贡献点
  → 等待活动调用结束
  → 尝试卸载 ALC
```

## 7.2 插件作者必须释放的内容

- `context.Events` 返回的所有订阅 token；
- 自建线程和计时器；
- 自建 `CancellationTokenSource`；
- 静态事件订阅；
- 指向插件对象、`IPluginContext` 或插件程序集类型的静态引用；
- 所有需要显式 Dispose 的外部资源。

`Shutdown()` 示例：

```csharp
public void Shutdown()
{
    foreach (IDisposable token in _subscriptions)
    {
        try { token.Dispose(); } catch { }
    }

    _subscriptions.Clear();
    _context = null;
}
```

## 7.3 不要做的事情

- 不要在 `Initialize()` 中做网络请求或全盘扫描；
- 不要把长期后台任务 fire-and-forget 后立即返回成功；
- 不要在动作中弹 `MessageBox`；
- 不要注册全局鼠标或键盘钩子；
- 不要直接改写 StarPie 的 `config.json`；
- 不要保存第二份 SDK 程序集；
- 不要依赖宿主内部类；
- 不要把大段 Base64 塞入动作参数或插件配置。

---

# 8. 性能和线程约束

## 8.1 启动阶段

宿主启动阶段设计为：

- 读取路径和登记表；
- 清理残留；
- 静态扫描清单和程序集元数据；
- 不加载普通插件程序集；
- 不执行插件 `Initialize()`。

因此插件作者不应通过构造函数或静态初始化制造启动副作用。

## 8.2 热路径

动作触发时的主要成本来自插件自己的实现，而不是 ID 查询。宿主已经缓存：

- `FullId → PluginActionRegistration`；
- `PluginActionRegistration → IActionContribution` 实例；
- 参数声明和调度元数据。

插件作者应重点避免：

- 每次 `Preview()` 访问磁盘或网络；
- 每次动作调用重复加载程序集；
- 不必要的 `MethodInfo.Invoke`；
- 高频路径反复创建大字典或大字符串；
- 在 `Sequential` 动作中执行长任务。

## 8.3 参数和内存

- 单个参数字符串最大长度为 8 KiB；
- 不要把文件、图片、音频转换成 Base64 写入 `config.json`；
- 复用静态只读参数描述，避免每次读取 `Parameters` 都创建大量临时对象；
- 高并发后台动作的共享状态必须线程安全；
- 所有动作结果和异常都应尽快返回明确结论。

## 8.4 历史基准参考

仓库曾在 2026-09-15 使用 .NET 8、Release、Workstation GC 和零依赖空插件做过基准。以下数字只用于判断数量级，不是所有真实插件的保证：

| 项目 | 历史测量结果 | 说明 |
|---|---:|---|
| `plugin.json` 解析 | 约 12 µs | 约 320 字节清单 |
| 静态程序集识别 | 约 50～90 µs | 不加载程序集 |
| 空插件首次启用 | 约 0.3 ms | 不含真实依赖和 WPF 首次 JIT |
| 跨 ALC 接口调用 | 约 2.3～3.9 ns/次 | 空插件基准 |
| `MethodInfo.Invoke` | 约 39～43 ns/次 | 明显慢于强类型接口调用 |

真实插件的启动和内存成本主要取决于：

- 自带程序集大小；
- 第三方依赖；
- WPF/COM/P/Invoke 首次初始化；
- 网络或文件操作；
- 插件自身缓存和后台线程。

不要把空插件的数字直接当作社区插件的性能承诺。若插件包含重依赖或长任务，应在自己的 README 中提供单独测量结果。

---

# 9. 能力声明与安全边界

## 9.1 能力枚举

当前 SDK 支持：

```text
None
Process
FileSystem
Network
Clipboard
Registry
GlobalHook
Ui
Admin
WindowControl
ScreenCapture
InputSimulation
```

`GlobalHook` 当前首版禁止插件直接注册全局输入钩子；需要接收宿主事件时使用 `IPluginEvents`。

## 9.2 如实声明

能力声明用于：

- 安装确认页向用户展示插件行为；
- 宿主对部分服务调用实施真实门禁；
- 审核者对照源码检查插件行为；
- 升级后判断是否需要重新确认能力。

它不是进程级安全沙箱。进程内插件仍可以自行调用 BCL、`Process.Start` 或 P/Invoke；因此只安装可信来源的插件。

## 9.3 认领历史类型的限制

只有官方在线模块可以认领历史顶层动作类型。社区插件和新增功能都使用普通 `Plugin` 动作，不要声明新的顶层类型。

---

# 10. 调试、自检与常见故障

## 10.1 自检命令

构建后执行：

```powershell
.\StarPie.exe --plugin-selftest `
  "C:\path\to\MyPlugin.dll" `
  --skip-invoke
```

`--skip-invoke` 会跳过真实动作副作用，但仍验证扫描、安装、注册、参数、惰性加载、调用租约、停用和卸载流程。需要验证真实动作效果时再去掉该选项。

指定报告文件：

```powershell
.\StarPie.exe --plugin-selftest `
  "C:\path\to\MyPlugin.dll" `
  "C:\path\to\selftest-report.txt" `
  --skip-invoke
```

查看当前目录和登记数量：

```powershell
.\StarPie.exe --plugin-paths
```

## 10.2 日志位置

```text
%LOCALAPPDATA%\StarPie\logs\starpie_yyyy-MM-dd.log
%LOCALAPPDATA%\StarPie\logs\plugins\<pluginId>_yyyy-MM-dd.log
```

插件内使用：

```csharp
context.Log.Info("信息");
context.Log.Warn("警告");
context.Log.Error("错误");
```

## 10.3 常见故障

| 现象 | 常见原因 | 处理方式 |
|---|---|---|
| 保留前缀错误 | 社区插件使用 `starpie.*` 等 ID | 改成反向域名 ID |
| 无法转换 `IStarPiePlugin` | 输出目录携带第二份 SDK DLL | 设置 `<Private>false</Private>` 并清理重建 |
| 目标框架不兼容 | 使用 `net9.0` 或更高 TFM | 改为 `net8.0-windows` |
| 插件已加载但无动作 | `Initialize` 未注册或中途抛异常 | 查插件日志和 `entryType` |
| 动作找不到 | 修改了已发布的 `ActionDescriptor.Id` | 保持动作 ID 稳定，重新编辑旧槽位 |
| 缺少能力 | manifest 未声明对应能力 | 补充能力后重新安装并确认 |
| 停用后 DLL 仍被占用 | 静态事件、线程、计时器或 Task 持有插件引用 | 在 `Shutdown` 中释放全部资源 |
| 动作列表不显示 | 插件未启用、加载失败或被隔离 | 查插件管理状态和日志 |

---

# 11. 当前不开放的能力

以下内容不要按照历史设计文档中的规划直接实现：

- 自定义插件 XAML 设置页；
- 插件自定义轮盘渲染器公共契约；
- 插件方案/配置模板公共契约；
- 插件自定义 OCR 引擎替换契约；
- 插件自定义音效提供者契约；
- 自动从社区索引下载并安装插件；
- 通过插件声明绕过官方历史类型认领限制；
- 通过 `StarPie.dll` 访问宿主内部实现。

如果未来开放新的贡献点，应先更新 `StarPie.Plugin.Abstractions`、契约版本说明、快速入门和本参考文档，再让插件作者使用。

---

# 12. 参考基准的口径

本文第 8.4 节的数字来自早期基准环境：

```text
.NET 8.0.26
Release
Workstation GC
16 核 Windows
约 5 KB、零第三方依赖的空插件
```

这些数据的用途是说明数量级：

- 静态扫描通常远小于加载和初始化插件；
- 强类型接口调用远低于反射调用；
- 真正的风险来自插件自身代码、依赖、IO、网络和未释放引用。

换机器、换插件或加入第三方依赖后应重新测量。性能数据不能替代真实动作测试和卸载测试。

---

## 相关文档

- [插件开发快速入门](plugin-development-quickstart.md)
- [当前架构与动作执行路径](plugin-system-architecture.md)
- [SDK 入口接口](../sdk/StarPie.Plugin.Abstractions/IStarPiePlugin.cs)
- [插件上下文](../sdk/StarPie.Plugin.Abstractions/IPluginContext.cs)
- [动作契约](../sdk/StarPie.Plugin.Abstractions/Actions.cs)
- [宿主服务](../sdk/StarPie.Plugin.Abstractions/Services.cs)
- [清单模型](../sdk/StarPie.Plugin.Abstractions/PluginManifest.cs)
- [入门示例](../samples/HelloAction/)
- [进阶示例](../samples/ScreenBrightness/)