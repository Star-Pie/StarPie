# StarPie 插件开发资源

> **适用范围**：当前主仓库实现；接口起始版本与已发布宿主兼容性需分别核对
> **SDK 契约**：API `1.10`（当前源码候选）
> **最后核对**：2026-10-09

本目录集中保存 StarPie 插件开发相关的当前文档、历史资料和示例工程。第一次开发插件时，从本页按顺序阅读即可，不需要直接翻阅主程序宿主源码。

> 仓库中的 `plugin/` 是源码资料目录；程序运行时的社区插件候选区则是发布产物旁边的 `<StarPie 程序目录>\plugin\`。两者名称相同，但用途不同，仓库中的文档和示例不会作为候选插件自动安装。

---

## 1. 推荐阅读顺序

### 第一步：快速入门

[《StarPie 插件开发快速入门》](docs/plugin-development-quickstart.md)

适合第一次编写插件，内容包括：

- 创建插件工程；
- 编写 `plugin.json`；
- 实现 `IStarPiePlugin`、动作贡献或[交互观察贡献](docs/plugin-development-quickstart.md#9-实现第一个交互贡献)；
- 动作 ID、参数表单、图标和多语言；
- 调用宿主服务或自行实现功能；
- 构建、安装、日志与 `--plugin-selftest`；
- 社区插件打包和发布检查表。

### 第二步：API 与性能参考

[《StarPie 插件系统当前 API 与性能参考》](docs/plugin-system-api-and-performance.md)

适合已经完成第一个插件、需要查阅具体契约时使用，内容包括：

- 当前 SDK 接口总览，包括轮盘会话和 1.10 交互观察候选；
- manifest 和兼容性规则；
- 动作、参数、结果和调度类别；
- [交互接口、不可变字段、筛选/队列、终结与旧接口边界](docs/plugin-system-api-and-performance.md#5-交互事件-api)；
- `IPluginContext` 宿主服务；
- 能力声明和运行时门禁；
- 插件登记、惰性加载和生命周期；
- 性能、线程、内存和卸载纪律；
- 当前尚未开放的扩展能力。

### 第三步：架构深入

[《StarPie 插件系统架构与调用路径》](docs/plugin-system-architecture.md)

适合宿主维护者、代码审查者和需要深入排障的插件作者，内容包括：

- `PluginHost`、`PluginRuntime` 和路径模块；
- 静态扫描、安装登记和 `PluginInstance`；
- 原子注册与 `PluginCatalog`；
- FullId 查询、参数校验和 `PluginInvoker`；
- 活动调用租约、超时和异步停用；
- 官方历史类型认领；
- 热重载、更新、卸载和 ALC 回收；
- 锁、健康度、安全模式和自检。
配套的可视化架构图：[《StarPie 插件系统架构图》](docs/plugin-system-architecture-map.md)

这份文档适合在阅读架构正文时对照查看，重点展示：

- `PluginHost`、`PluginRuntime`、`PluginInstance`、`PluginCatalog` 和 `PluginContext` 的层级关系；
- 宿主启动、插件安装、首次加载、实例封装和原子注册流程；
- `IPluginContext` 服务边界、动作执行路径、调用租约与停用流程；
- 三条插件调用路径以及最终的宿主、运行时、SDK 和主程序职责边界。

架构正文负责解释设计和实现细节；架构图文档负责提供全局关系图和关键流程图，两者应配套阅读。

---

## 2. 示例工程与当前检出

当前 checkout 不包含历史 `plugin/samples/HelloAction/` 或 `ScreenBrightness/`，不要直接运行旧路径构建命令。

- [动作完整最小例子](docs/plugin-development-quickstart.md#8-实现第一个动作)：按快速入门创建自己的工程，不依赖已移除模板目录。
- [交互观察完整最小例子](docs/plugin-development-quickstart.md#9-实现第一个交互贡献)：SDK/BCL 实现与清单，不播放音频或操控系统。
- [记录型交互验证夹具](../scratch/interaction-event-tests/Fixture/)：用于无副作用回归，不作为发行插件静默携带。
- [当前悬浮球源码](StarPie-Official-Plugins/src/StarPie.Plugin.FloatingBall/)：位于官方插件子模块；不是旧 samples 目录，也不是运行时自动安装来源。

历史 HelloAction/ScreenBrightness 的说明只作早期资料，硬件/真实动作演示不得作为默认自动测试。当前完整自检要求的 KeypadLayer 夹具及 --skip-invoke 命令见[快速入门第 24.4 节](docs/plugin-system-api-and-performance.md#111-自检命令)。

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
| `Interactions.cs` | SDK 1.10 可选交互上下文、观察贡献与不可变事件 |
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


## 交互路径速查

- 插件作者：[完整 API](docs/plugin-system-api-and-performance.md#5-交互事件-api) → [最小无副作用例子](docs/plugin-development-quickstart.md#9-实现第一个交互贡献)。
- 宿主维护者：[职责/调用树](docs/plugin-system-architecture.md#13-交互事件路径的完整调用过程) → [注册、Render、队列、撤回与停用图](docs/plugin-system-architecture-map.md#10-交互事件调用主路径)。
- SDK 事实来源：[Interactions.cs](sdk/StarPie.Plugin.Abstractions/Interactions.cs)；验证器：[记录型行为测试](../scratch/interaction-event-tests/)。
- 广播不加载未激活插件；音效完整迁入独立官方候选 `starpie.plugin.sound`，需用户安装、启用并选择预加载；宿主没有播放兜底。旧事件接口和程序集身份不变。候选不是发布承诺，真实窗口、DPI、多屏与手感仍须实机验收。

### 音效插件候选（beta.7）

宿主只发布交互语义，播放/调度/主题/自定义方案均由官方音效插件拥有。设置入口位于插件卡片；试听、导入和导出使用后台插件动作。旧音效键不再是宿主属性，但经通用扩展数据保留，插件只读迁入私有目录。未安装或禁用时无音效，不自动下载或启用。官方包和catalog尚需单独打包、发布授权；源码候选不等于已可下载发行版。
