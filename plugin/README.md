# StarPie 插件开发资源

> **适用版本**：StarPie `v1.8.0-beta.2` 及以上
> **SDK 契约**：`StarPie.Plugin.Abstractions` API `1.6`
> **最后核对**：2026-09-21

本目录集中保存 StarPie 插件开发相关的当前文档、历史资料和示例工程。第一次开发插件时，从本页按顺序阅读即可，不需要直接翻阅主程序宿主源码。

> 仓库中的 `plugin/` 是源码资料目录；程序运行时的社区插件候选区则是发布产物旁边的 `<StarPie 程序目录>\plugin\`。两者名称相同，但用途不同，仓库中的文档和示例不会作为候选插件自动安装。

---

## 1. 推荐阅读顺序

### 第一步：快速入门

[《StarPie 插件开发快速入门》](docs/plugin-development-quickstart.md)

适合第一次编写插件，内容包括：

- 创建插件工程；
- 编写 `plugin.json`；
- 实现 `IStarPiePlugin` 和 `IActionContribution`；
- 动作 ID、参数表单、图标和多语言；
- 调用宿主服务或自行实现功能；
- 构建、安装、日志与 `--plugin-selftest`；
- 社区插件打包和发布检查表。

### 第二步：API 与性能参考

[《StarPie 插件系统当前 API 与性能参考》](docs/plugin-system-api-and-performance.md)

适合已经完成第一个插件、需要查阅具体契约时使用，内容包括：

- 当前 SDK 1.6 接口总览，包括宿主轮盘服务和插件级参数页；
- manifest 和兼容性规则；
- 动作、参数、结果和调度类别；
- `IPluginContext` 宿主服务；
- 能力声明和运行时门禁；
- 插件登记、惰性加载和生命周期；
- 性能、线程、内存和卸载纪律；
- 当前尚未开放的扩展能力。

### 第三步：架构深入

[《StarPie 插件系统架构与动作执行路径》](docs/plugin-system-architecture.md)

适合宿主维护者、代码审查者和需要深入排障的插件作者，内容包括：

- `PluginHost`、`PluginRuntime` 和路径模块；
- 静态扫描、安装登记和 `PluginInstance`；
- 原子注册与 `PluginCatalog`；
- FullId 查询、参数校验和 `PluginInvoker`；
- 活动调用租约、超时和异步停用；
- 官方历史类型认领；
- 热重载、更新、卸载和 ALC 回收；
- 锁、健康度、安全模式和自检。

---

## 2. 示例工程

### `HelloAction`

路径：[samples/HelloAction/](samples/HelloAction/)

社区插件参考模板，演示：

- 插件入口与多个动作注册；
- Text、Bool、Enum、Folder、MultilineText、Number、File、Hotkey、Color 参数；
- SVG 图标和多语言词条；
- 宿主服务调用；
- 事件订阅 token 与幂等 `Shutdown()`；
- `plugin.json`、Schema 和程序集元数据兜底。

构建：

```powershell
dotnet build plugin/samples/HelloAction/HelloAction.csproj -c Release
```

### `FloatingBall`

路径：[samples/FloatingBall/](samples/FloatingBall/)

常驻形态示例，演示插件自己绘制 WPF 悬浮球，并通过 `IHostWheelService` 呼出用户配置的宿主轮盘。

构建：

```powershell
dotnet build plugin/samples/FloatingBall/FloatingBall.csproj -c Release
```
### `ScreenBrightness`

路径：[samples/ScreenBrightness/](samples/ScreenBrightness/)

进阶和压力测试示例，演示：

- P/Invoke 调用 DDC/CI；
- COM/WMI 互操作；
- 硬件不可用时的降级；
- `ActionKind.Background`；
- 耗时 IO、取消和参数校验。

构建：

```powershell
dotnet build plugin/samples/ScreenBrightness/ScreenBrightness.csproj -c Release
```

---

## 3. SDK 源码入口

插件唯一允许引用的 StarPie 程序集位于：

```text
plugin/sdk/StarPie.Plugin.Abstractions/
```

主要文件：

| 文件 | 内容 |
|---|---|
| `IStarPiePlugin.cs` | 插件入口和生命周期 |
| `IPluginContext.cs` | 插件可使用的注册表和宿主服务 |
| `Actions.cs` | 动作描述、参数、输入和结果 |
| `Registries.cs` | 动作、图标和词条注册接口 |
| `Services.cs` | 启动、命令、窗口、截屏、系统、事件等宿主服务 |
| `PluginManifest.cs` | `plugin.json` 清单模型 |
| `PluginMetadata.cs` | 能力枚举、运行时元数据和动作引用 |
| `PluginApi.cs` | API 版本、保留前缀和参数上限 |

当前接口的最终事实来源是 SDK 源码和 XML 注释；文档与源码冲突时，以 SDK 公共契约为准。

---

## 4. 最小开发流程

```text
复制 HelloAction 或创建 net8.0-windows 类库
    ↓
引用 StarPie.Plugin.Abstractions，设置 Private=false
    ↓
编写 plugin.json 和程序集元数据
    ↓
实现 IStarPiePlugin.Initialize / Shutdown
    ↓
注册一个或多个 IActionContribution
    ↓
dotnet build -c Release
    ↓
确认产物中没有 StarPie.Plugin.Abstractions.dll
    ↓
StarPie.exe --plugin-selftest <插件.dll> --skip-invoke
    ↓
在“插件与扩展”页面手动安装并启用
```

查看当前插件目录：

```powershell
.\StarPie.exe --plugin-paths
```

运行无副作用自检：

```powershell
.\StarPie.exe --plugin-selftest "C:\path\to\MyPlugin.dll" --skip-invoke
```

---

## 5. 目录模型

程序运行时使用两个不同目录：

| 目录 | 用途 |
|---|---|
| `<StarPie 程序目录>\plugin\` | 社区插件只读候选区；只扫描，不自动安装，不写入、不删除 |
| `%LOCALAPPDATA%\StarPie\plugin-data\` | 可写宿主区；保存安装副本、`registry.json`、`health.json` 和插件私有数据 |

把 DLL 放入候选区后仍需在插件页手动点击安装。带资源或其它文件的完整插件包，应使用“手动安装社区插件”并选择与 `plugin.json` 同目录的主 DLL。

---

## 6. 社区插件与官方插件

### 社区插件

- 使用反向域名 ID，例如 `com.example.myplugin`；
- 通过本地候选区或手动选择 DLL 安装；
- 动作使用 `Type="Plugin" + PluginActionRef`；
- 不得使用官方保留 ID；
- 不得认领历史顶层动作类型。

### 官方插件

- 由 `StarPie-Official-Plugins` catalog 手动下载和安装；
- 使用官方保留 ID；
- 安装包和程序集经过 catalog 哈希校验；
- 只有迁移已有历史动作时才使用 `ClaimedTypes`；
- 后续新增官方功能同样优先注册普通 `Plugin` 动作。

插件是进程内代码，不存在操作系统级安全沙箱。只安装来源可信的社区插件。

---

## 7. 历史资料

归档目录：[docs/archive/](docs/archive/)

包含：

- `plugin-system-design.md`：早期设计草案和路线图；
- `plugin-system-implementation.md`：第一阶段实现交付说明和踩坑记录。

归档文档用于保留设计推理和演进历史，其中的目录、接口和发布流程可能已经过时。开发新插件时不要将归档内容作为当前 API 依据。

---

## 8. 修改文档时的维护规则

- 新增或修改公共 SDK 时，同步更新快速入门和 API 参考；
- 修改宿主加载、注册、租约或停用流程时，同步更新架构文档；
- 示例工程路径、项目引用或构建命令变化时，同步更新本 README；
- 历史方案移入 `docs/archive/`，不要继续与当前文档并列展示；
- 主仓库 README 只链接本页，不重复维护具体文档列表。