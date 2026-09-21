# StarPie 插件开发快速入门

> **适用版本**：StarPie `v1.8.0-beta.2` 及以上
> **SDK 契约**：`StarPie.Plugin.Abstractions` API `1.6`
> **最后核对**：2026-09-21
> **目标**：从零创建一个可安装、可配置、可执行、可自检的社区动作插件。
>
> 本文只描述仓库当前已经实现并开放的能力。接口事实以
> `plugin/sdk/StarPie.Plugin.Abstractions/` 中的公共类型和 XML 注释为准；宿主运行机制见
> [`plugin-system-architecture.md`](plugin-system-architecture.md)。

---

## 1. 先了解插件模型

StarPie 使用 .NET 8 进程内 DLL 插件。一个插件通常包含：

```text
插件包
├── MyPlugin.dll          # 插件入口程序集
├── plugin.json           # 推荐提供的插件清单
├── plugin.schema.json    # 可选，供编辑器校验清单
└── 其它运行所需文件       # 资源或插件自己的依赖
```

插件有一个全局唯一的**插件 ID**，并可注册多个**动作贡献 ID**：

```text
插件 ID：com.example.hello

动作短 ID：hello
完整动作 ID：com.example.hello.hello
```

用户配置保存的是：

```json
{
  "Type": "Plugin",
  "PluginActionRef": {
    "PluginId": "com.example.hello",
    "ContributionId": "hello"
  }
}
```

宿主根据 `PluginId` 惰性加载插件，再根据完整动作 ID 找到对应的
`IActionContribution` 实例并调用 `ExecuteAsync()`。

---

## 2. 开发环境

需要：

- Windows 10/11 x64；
- .NET 8 SDK；
- 当前 StarPie 源码或至少 `StarPie.Plugin.Abstractions` SDK 工程；
- Visual Studio 2022、Rider 或 VS Code 均可。

确认 SDK：

```powershell
dotnet --version
```

推荐先构建官方入门示例：

```powershell
dotnet build plugin/samples/HelloAction/HelloAction.csproj -c Release
```

如果该命令成功，再开始创建自己的插件。

---

## 3. 最快方式：复制 `HelloAction`

仓库已经提供两个示例：

| 示例 | 用途 |
|---|---|
| `plugin/samples/HelloAction/` | 入门模板：动作、参数、图标、i18n、宿主服务、事件订阅 |
| `plugin/samples/ScreenBrightness/` | 进阶示例：P/Invoke、COM、硬件访问、后台任务和降级处理 |

第一次开发建议复制：

```text
plugin/samples/HelloAction/
```

然后至少修改：

1. 项目文件名和 `AssemblyName`；
2. 命名空间；
3. `plugin.json` 中的 `id`、名称、作者和入口类型；
4. `.csproj` 中的程序集元数据；
5. 动作类和 `ActionDescriptor.Id`。

下文给出一个更小的完整示例，方便理解每个文件的作用。

---

## 4. 创建项目结构

例如创建：

```text
plugin/samples/MyHelloPlugin/
├── MyHelloPlugin.csproj
├── plugin.json
├── MyHelloPlugin.cs
└── HelloAction.cs
```

> 如果插件工程不在 StarPie 仓库内，请调整下面 `ProjectReference` 的相对路径，或引用你本地构建的
> `StarPie.Plugin.Abstractions` 项目。不要引用 `StarPie.dll`。

---

## 5. 编写项目文件

`MyHelloPlugin.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>12.0</LangVersion>

    <AssemblyName>StarPie.Plugin.MyHello</AssemblyName>
    <RootNamespace>StarPie.Plugin.MyHello</RootNamespace>

    <Version>1.0.0</Version>
    <AssemblyVersion>1.0.0.0</AssemblyVersion>
    <FileVersion>1.0.0.0</FileVersion>

    <Platforms>x64</Platforms>
    <PlatformTarget>x64</PlatformTarget>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\sdk\StarPie.Plugin.Abstractions\StarPie.Plugin.Abstractions.csproj">
      <Private>false</Private>
    </ProjectReference>
  </ItemGroup>

  <!-- 裸 DLL 分发时的静态识别兜底。值必须与 plugin.json 保持一致。 -->
  <ItemGroup>
    <AssemblyMetadata Include="StarPiePluginId" Value="com.example.myhello" />
    <AssemblyMetadata Include="StarPiePluginName" Value="我的 Hello 插件" />
    <AssemblyMetadata Include="StarPiePluginCapabilities" Value="" />
    <AssemblyMetadata Include="StarPiePluginLicense" Value="MIT" />
    <AssemblyMetadata Include="StarPiePluginHomepage" Value="https://example.com/myhello" />
  </ItemGroup>

  <ItemGroup>
    <None Update="plugin.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

</Project>
```

### 三条必须遵守的规则

1. `TargetFramework` 不得高于宿主：使用 `net8.0-windows` 或
   `net8.0-windows10.0.19041.0`；
2. SDK 引用必须设置 `<Private>false</Private>`，构建产物中不能出现第二份
   `StarPie.Plugin.Abstractions.dll`；
3. 插件只能引用 SDK 与 BCL，不能引用 `StarPie.dll` 或 `WinPieGestures.*` 内部类型。

当前项目还要求插件保持零 NuGet 依赖。需要系统能力时，优先使用 .NET BCL、P/Invoke、COM，或调用
`IPluginContext` 提供的宿主服务。

---

## 6. 编写 `plugin.json`

```json
{
  "schemaVersion": 1,

  "id": "com.example.myhello",
  "name": "我的 Hello 插件",
  "description": "演示 StarPie 动作插件的最小实现。",
  "author": "Your Name",
  "homepage": "https://example.com/myhello",
  "license": "MIT",
  "version": "1.0.0",

  "apiVersion": "1.4",
  "minHostVersion": "1.8.0-beta.1",

  "targetFramework": "net8.0-windows",
  "platform": "win-x64",

  "assembly": "StarPie.Plugin.MyHello.dll",
  "entryType": "StarPie.Plugin.MyHello.MyHelloPlugin",

  "capabilities": [],

  "contributions": {
    "actions": true,
    "icons": false,
    "i18n": false
  },

  "tags": ["示例", "入门"]
}
```

### ID 规则

社区插件建议使用反向域名：

```text
com.example.myhello
io.github.username.pluginname
```

不要使用官方保留前缀：

```text
starpie.*
winpiegestures.*
windows.*
microsoft.*
system.*
builtin.*
```

社区插件也不要填写 `claimedTypes`。它只用于官方插件接管已经发布过的历史
`ActionItem.Type`，不是普通插件的扩展方式。

---

## 7. 实现插件入口

`MyHelloPlugin.cs`：

```csharp
using StarPie.Plugin;

namespace StarPie.Plugin.MyHello;

public sealed class MyHelloPlugin : IStarPiePlugin
{
    private IPluginContext? _context;
    private readonly List<IDisposable> _registrations = new();

    public void Initialize(IPluginContext context)
    {
        _context = context;

        // Initialize 只做注册，不做网络、磁盘扫描或其它耗时工作。
        _registrations.Add(
            context.Actions.Register(new HelloAction()));

        context.Log.Info("MyHello 插件初始化完成。");
    }

    public void Shutdown()
    {
        // 必须幂等。宿主也会兜底撤销贡献点，但插件仍应释放自己持有的 token。
        foreach (IDisposable registration in _registrations)
        {
            try
            {
                registration.Dispose();
            }
            catch
            {
            }
        }

        _registrations.Clear();
        _context = null;
    }
}
```

入口类要求：

- `public`；
- 实现 `IStarPiePlugin`；
- 有无参构造函数；
- 构造函数不得做 IO、启动线程或弹窗；
- 一个程序集通常只有一个入口；有多个实现时必须用 `entryType` 明确指定；
- `Shutdown()` 必须允许重复调用。

---

## 8. 实现第一个动作

`HelloAction.cs`：

```csharp
using StarPie.Plugin;

namespace StarPie.Plugin.MyHello;

internal sealed class HelloAction : IActionContribution
{
    private static readonly IReadOnlyList<ParameterField> Fields =
        new List<ParameterField>
        {
            new()
            {
                Key = "name",
                Label = "姓名",
                Type = ParameterFieldType.Text,
                Required = true,
                MaxLength = 40,
                Placeholder = "StarPie 用户"
            }
        };

    public ActionDescriptor Descriptor => new()
    {
        // 插件内短 ID。完整 ID 会自动变成 com.example.myhello.hello。
        Id = "hello",
        DisplayName = "打个招呼",
        Description = "显示一条问候信息。",
        Category = "示例",
        IconKey = "Info",
        Kind = ActionKind.Sequential,
        TimeoutSeconds = 3
    };

    public IReadOnlyList<ParameterField> Parameters => Fields;

    public string? Validate(IReadOnlyDictionary<string, string> parameters)
    {
        return parameters.TryGetValue("name", out string? name)
               && !string.IsNullOrWhiteSpace(name)
            ? null
            : "姓名不能为空。";
    }

    public string Preview(IReadOnlyDictionary<string, string> parameters)
    {
        // Preview 可能在设置页滚动时高频调用，禁止 IO、网络请求和耗时计算。
        return parameters.TryGetValue("name", out string? name)
            ? $"向 {name} 问好"
            : "等待填写姓名";
    }

    public Task<ActionResult> ExecuteAsync(
        PluginActionInput input,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string name = input.Parameter("name") ?? "StarPie 用户";

        // silent: false 表示让宿主向用户显示成功消息。
        return Task.FromResult(
            ActionResult.Ok($"你好，{name}！", silent: false));
    }
}
```

宿主注册时会把：

```text
PluginId       = com.example.myhello
ContributionId = hello
```

组合为：

```text
com.example.myhello.hello
```

执行时宿主先按完整 ID 找到这个 `HelloAction` 实例，再通过
`IActionContribution.ExecuteAsync()` 的接口多态调用具体实现。

---

## 9. 一个插件注册多个动作

在 `Initialize()` 中多次调用：

```csharp
context.Actions.Register(new HelloAction());
context.Actions.Register(new OpenFolderAction(context));
context.Actions.Register(new TranslateAction(context));
```

每个动作必须有不同的短 ID：

```text
hello
openFolder
translate
```

完整 ID 分别是：

```text
com.example.myhello.hello
com.example.myhello.openFolder
com.example.myhello.translate
```

不是每个 C# 方法都需要 ID。只有需要被用户选择并由宿主直接调用的
`IActionContribution` 才注册动作 ID；内部辅助方法、解析器和缓存逻辑不注册。

一个贡献也可以通过参数提供多个模式。例如只注册 `convert`，再用 `format` 参数选择 PNG/JPEG/WebP。

---

## 10. 参数表单

插件只声明字段，不提供 XAML。宿主支持：

| 类型 | `ParameterFieldType` | 用途 |
|---|---|---|
| 单行文本 | `Text` | 名称、路径片段、普通字符串 |
| 多行文本 | `MultilineText` | 模板、脚本、说明 |
| 数字 | `Number` | 百分比、步长、时间 |
| 布尔值 | `Bool` | 开关 |
| 文件夹 | `Folder` | 目录选择器 |
| 文件 | `File` | 文件选择器 |
| 枚举 | `Enum` | 下拉选项 |
| 快捷键 | `Hotkey` | 快捷键录制控件 |
| 颜色 | `Color` | 颜色选择器 |

常用约束：

```csharp
new ParameterField
{
    Key = "level",
    Label = "强度",
    Type = ParameterFieldType.Number,
    DefaultValue = "50",
    Required = true,
    Min = 0,
    Max = 100,
    HelpText = "范围 0～100。"
}
```

宿主会先执行声明式校验，再调用插件自己的 `Validate()`。保存动作和执行动作使用同一个校验入口。

参数最终以字符串字典保存在用户配置中。读取数字时使用：

```csharp
int level = input.Int("level", 50);
double ratio = input.Double("ratio", 0.5);
bool enabled = input.Bool("enabled", false);
```

这些方法使用不变文化，避免不同区域设置造成小数解析差异。

单个参数值不得超过 8 KiB。不要把图片、音频或其它大对象编码成 Base64 写入动作参数。

---

## 11. 使用多语言词条和图标

注册词条：

```csharp
context.I18n.Register(
    "action.hello.name",
    "打个招呼",
    "Say Hello");
```

动作中声明短键：

```csharp
public ActionDescriptor Descriptor => new()
{
    Id = "hello",
    DisplayName = "打个招呼",
    DisplayNameKey = "action.hello.name"
};
```

宿主会把短键归一化为：

```text
plugin.com.example.myhello.action.hello.name
```

注册 SVG 图标：

```csharp
string iconKey = context.Icons.RegisterSvg(
    "hello",
    "M4 12 L12 4 L20 12 L12 20 Z");
```

将返回值写入 `ActionDescriptor.IconKey`。SVG path 应使用简单、稳定的路径命令；复杂或非法语法可能导致图标解析失败。

如果注册了词条或图标，记得修改 `plugin.json`：

```json
"contributions": {
  "actions": true,
  "icons": true,
  "i18n": true
}
```

---

## 12. 调用宿主能力，或完全自己实现

插件可以完全使用自己的 .NET、P/Invoke、COM 或网络实现，也可以调用宿主服务。

`IPluginContext` 当前提供：

| 服务 | 主要用途 | 相关能力声明 |
|---|---|---|
| `Host` | 快捷键、文本、启动程序、文件夹、网址、剪贴板 | 按实际行为声明 `Process`、`Clipboard`、`InputSimulation` |
| `Commands` | 在 CMD、PowerShell、WSL 等终端执行命令 | `Process`，运行时强制检查 |
| `Shell` | 执行资源管理器 Shell 动词 | `Process`，运行时强制检查 |
| `Windows` | 平铺、置顶、透明度、跨屏、任务栏槽位 | `WindowControl`，运行时强制检查 |
| `ScreenCapture` | 框选截屏和 OCR | `ScreenCapture`，运行时强制检查 |
| `System` | 最小化、音量、锁屏、关机等系统预设 | `InputSimulation`，部分预设还应声明 `Process` |
| `Notify` | 非阻塞通知 | 按实际行为声明 `Ui` |
| `Log` | 插件独立日志 | 无 |
| `Settings` | 插件私有设置 | 无 |
| `Events` | 宿主事件订阅 | 按订阅和行为声明 |
| `Dispatcher` | 切换到 UI 线程 | `Ui` |

例如打开文件夹：

```csharp
bool success = context.Host.OpenFolder(folder);
```

插件也可以自己实现：

```csharp
Process.Start(new ProcessStartInfo
{
    FileName = folder,
    UseShellExecute = true
});
```

但直接实现不会经过宿主的能力门禁、统一日志和兼容性处理。已有成熟宿主能力时优先复用；插件独有业务逻辑应保留在插件中。

能力声明不是安全沙箱。插件与 StarPie 运行在同一进程，仍能直接调用 BCL 和 P/Invoke；能力字段的意义是让安装确认页如实说明行为，并为宿主服务提供对应的强制检查点。

---

## 13. 选择 `Sequential` 还是 `Background`

### `Sequential`

适合必须按顺序操作前台环境的短动作：

- 发送快捷键；
- 输入文本；
- 切换窗口；
- 操作剪贴板；
- 启动程序后立即与其交互。

它占用唯一的顺序动作线程，不得进行长时间网络、文件遍历或硬件等待。

### `Background`

适合：

- 网络请求；
- 文件 IO；
- 图片或文本计算；
- COM/WMI/DDC 等可能较慢的调用；
- 不依赖当前前台窗口的任务。

后台动作可能并发调用同一个贡献实例。尽量把每次执行状态放在局部变量中；共享状态需要自行同步。

`ExecuteAsync()` 返回的 Task 必须代表真实工作的完整生命周期。禁止启动一个未等待的长期 Task 后立即返回成功，否则宿主会误判调用已经结束并允许插件提前停用。

---

## 14. 构建和检查产物

构建：

```powershell
dotnet build plugin/samples/MyHelloPlugin/MyHelloPlugin.csproj -c Release
```

产物通常位于：

```text
plugin/samples/MyHelloPlugin/bin/Release/net8.0-windows/
```

至少应看到：

```text
StarPie.Plugin.MyHello.dll
plugin.json
```

必须确认输出目录中**没有**：

```text
StarPie.Plugin.Abstractions.dll
```

如果出现它，检查：

```xml
<Private>false</Private>
```

不要仅删除输出文件来掩盖问题，必须修正项目引用。

---

## 15. 运行无界面自检

推荐在安装前执行：

```powershell
.\StarPie.exe --plugin-selftest "C:\path\to\StarPie.Plugin.MyHello.dll" --skip-invoke
```

`--skip-invoke` 会跳过真实动作执行，避免测试插件时改变音量、亮度、剪贴板或当前窗口，但仍会检查：

- 静态识别；
- manifest；
- 安装和登记；
- 加载与初始化；
- 动作注册；
- 参数校验；
- ID 冲突；
- 调用租约；
- 惰性加载；
- 停用和卸载；
- 临时沙箱清理。

需要验证动作实际效果时，再去掉 `--skip-invoke`。注意这会真的执行插件动作。

如需把报告写入指定文件：

```powershell
.\StarPie.exe --plugin-selftest `
  "C:\path\to\StarPie.Plugin.MyHello.dll" `
  "C:\path\to\selftest-report.txt" `
  --skip-invoke
```

查看当前生效目录：

```powershell
.\StarPie.exe --plugin-paths
```

---

## 16. 在 StarPie 中安装

### 方法一：手动选择 DLL

1. 打开 StarPie 设置；
2. 进入“插件与扩展”；
3. 点击“手动安装社区插件”；
4. 选择构建目录中的主 DLL；
5. 检查安装确认页的 ID、版本、能力和目标框架；
6. 完成安装后手动启用插件；
7. 在动作编辑器中选择“插件动作”，再选择具体动作。

如果 DLL 同目录存在 `plugin.json`，宿主会按完整插件目录安装；这种方式适合带资源文件的完整插件。

### 方法二：社区候选目录

将单个候选 DLL 放入：

```text
<StarPie 程序目录>\plugin\
```

然后在插件页重新扫描并点击候选卡片的安装按钮。

该目录是只读候选区，StarPie 不会创建、修改或删除其中的文件。候选区只保证识别那一枚 DLL；如果插件需要其它依赖或资源，请使用“手动安装社区插件”并选择带 `plugin.json` 的完整输出目录中的 DLL。

### 安装后的可写目录

默认安装到：

```text
%LOCALAPPDATA%\StarPie\plugin-data\<pluginId>\
```

其中还会保存：

```text
plugin-data\registry.json   # 安装、启用、来源、能力确认和哈希
plugin-data\health.json     # 失败次数、隔离和安全模式状态
plugin-data\<id>\data\     # 插件私有数据
```

不要让插件自行修改 `registry.json` 或其它插件目录。

---

## 17. 日志和调试

宿主日志：

```text
%LOCALAPPDATA%\StarPie\logs\starpie_yyyy-MM-dd.log
```

插件独立日志：

```text
%LOCALAPPDATA%\StarPie\logs\plugins\<pluginId>_yyyy-MM-dd.log
```

插件内使用：

```csharp
context.Log.Info("开始执行");
context.Log.Warn("使用了降级路径");
context.Log.Error("执行失败");
```

不要使用 `MessageBox` 报告动作失败。返回：

```csharp
return ActionResult.Fail("用户可以直接理解并处理的失败原因");
```

宿主会负责日志、提示、失败计数和必要的隔离处理。

---

## 18. 生命周期和卸载纪律

为了让插件能够停用、更新和卸载：

- 不要把插件对象挂到宿主不可释放的静态事件上；
- 所有事件订阅 token 必须保存并在 `Shutdown()` 中 `Dispose()`；
- 不要在静态字段中长期保存 `IPluginContext`；
- 自建线程、计时器和取消源必须停止并释放；
- `Shutdown()` 必须幂等且不抛异常；
- 不要在 `Initialize()` 中启动长期工作；
- 后台任务必须响应 `CancellationToken`；
- 不要使用 fire-and-forget 逃离宿主调用生命周期。

宿主会兜底撤销注册贡献，但无法自动切断插件自己创建的所有静态引用和线程。引用未释放时，程序集可能需要重启 StarPie 后才能更新。

---

## 19. 社区插件与官方插件的区别

社区插件：

- 使用自己的反向域名 ID；
- 通过本地候选或手动选择 DLL 安装；
- `official=false`；
- 动作统一使用 `Type="Plugin" + PluginActionRef`；
- 不允许认领历史顶层动作类型。

官方插件：

- 通过 `StarPie-Official-Plugins` catalog 手动安装；
- 使用官方保留 ID；
- 安装时写入 `official=true`；
- 可为迁移历史配置声明 `claimedTypes`。

即使你在社区插件的 manifest 中写入 `starpie.*` ID 或 `claimedTypes`，宿主也不会因此把它视为官方插件。

后续新增功能，无论由官方还是社区开发，默认都应注册普通 `Plugin` 动作。类型认领只用于接管已经发布过的历史 `ActionItem.Type`。

---

## 20. 打包和发布

社区插件推荐发布一个 ZIP，内容是构建输出目录中运行所需的完整文件，例如：

```text
MyHello-1.0.0.zip
└── MyHello/
    ├── StarPie.Plugin.MyHello.dll
    ├── plugin.json
    └── README.md
```

发布前检查：

- 插件 ID 与程序集元数据一致；
- manifest 版本与程序集版本一致；
- `entryType` 和 `assembly` 正确；
- 能力声明与实际行为一致；
- 没有 `StarPie.Plugin.Abstractions.dll` 私有副本；
- 没有引用 `StarPie.dll`；
- 没有无关的调试文件；
- 自检通过；
- 附带许可证和安装说明。

当前社区插件以用户手动下载、解压和安装为主。不要把官方模块的 catalog 与 `.spkg` 发布流程照搬到社区插件，除非后续仓库另行发布正式的社区索引规范。

---

## 21. 常见错误

### “插件 ID 使用了保留前缀”

社区插件使用了 `starpie.*` 等官方前缀。改为自己的反向域名 ID。

### “无法转换为 IStarPiePlugin”或入口类型看似正确却加载失败

通常是输出目录携带了第二份 `StarPie.Plugin.Abstractions.dll`。设置 `<Private>false</Private>` 后清理并重新构建。

### “目标框架高于宿主”

将项目改为：

```xml
<TargetFramework>net8.0-windows</TargetFramework>
```

不要使用 `net9.0` 或更高框架。

### “插件已加载，但没有注册动作”

检查：

- `Initialize()` 是否调用 `context.Actions.Register()`；
- `ActionDescriptor.Id` 是否与配置中的 `ContributionId` 一致；
- `Initialize()` 是否在后续注册时抛异常；
- 插件升级是否删除或重命名了已有动作 ID。

动作 ID 一旦发布就是持久化契约。可以重命名 C# 类，但不要随意修改 `ActionDescriptor.Id`。

### “缺少某项能力”

在 `plugin.json` 的 `capabilities` 中补充对应能力，重新安装并确认能力变化。不要捕获后静默忽略 `PluginCapabilityDeniedException`。

### “停用后需要重启才能更新”

检查静态事件、计时器、线程、未完成 Task 和未释放的订阅 token。它们可能仍持有插件对象，导致 ALC 无法卸载。

### “插件安装了但动作下拉中看不到”

检查插件是否启用、是否隔离、是否成功完成 `Initialize()`，并查看插件日志。动作列表只显示已安装、已启用且当前可用的贡献。

---

## 22. 推荐阅读顺序

1. 本文；
2. `plugin/samples/HelloAction/`；
3. [`IStarPiePlugin.cs`](../sdk/StarPie.Plugin.Abstractions/IStarPiePlugin.cs)；
4. [`IPluginContext.cs`](../sdk/StarPie.Plugin.Abstractions/IPluginContext.cs)；
5. [`Actions.cs`](../sdk/StarPie.Plugin.Abstractions/Actions.cs)；
6. [`Services.cs`](../sdk/StarPie.Plugin.Abstractions/Services.cs)；
7. [`plugin-system-architecture.md`](plugin-system-architecture.md)；
8. `plugin/samples/ScreenBrightness/`。

历史设计和性能材料：

```text
archive/plugin-system-design.md
archive/plugin-system-implementation.md
plugin-system-api-and-performance.md
```

这些文件保留了设计推理、初期实现记录和基准结果，但部分目录、接口和路线图已经过时；开发新插件时应以本文、当前 SDK 源码和示例工程为准。

---

## 23. 发布前检查表

- [ ] 使用唯一的反向域名插件 ID；
- [ ] 未使用官方保留前缀；
- [ ] `apiVersion` 与实际使用的 SDK 契约一致；
- [ ] `minHostVersion` 不低于首次支持所需接口的 StarPie 版本；
- [ ] `TargetFramework` 不高于宿主；
- [ ] SDK 引用设置了 `<Private>false</Private>`；
- [ ] 未引用 `StarPie.dll`；
- [ ] 未引入 NuGet 依赖；
- [ ] manifest 与程序集元数据一致；
- [ ] 每个动作 ID 唯一且发布后保持稳定；
- [ ] 参数声明、默认值与 `Validate()` 一致；
- [ ] `Preview()` 不做 IO 或网络请求；
- [ ] 耗时动作使用 `Background`；
- [ ] `ExecuteAsync()` 的 Task 覆盖真实工作全生命周期；
- [ ] `Shutdown()` 释放所有订阅、线程和计时器；
- [ ] 能力声明与实际行为一致；
- [ ] 构建产物不含 `StarPie.Plugin.Abstractions.dll`；
- [ ] `--plugin-selftest --skip-invoke` 通过；
- [ ] 实际安装、启用、配置和触发测试通过；
- [ ] 插件日志没有未处理异常；
- [ ] 附带 README 和许可证。