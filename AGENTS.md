# StarPie 工程与协作规范

本文件是仓库级入口，只保留所有任务都必须知道的规则。领域细节采用按需加载：先判断改动涉及什么，再阅读文末对应文档和技能。不要为了开始一个普通任务而一次性加载整个项目知识库。

## 1. 项目概览

StarPie（原 WinPieGestures）是基于 **.NET 8、WPF 与 Win32** 的 Windows 全局鼠标手势轮盘。产品目标是把工业 CAD 中高效、可盲操的空间手势迁移到桌面全局，同时保持轻量、低延迟和可预测。

主要目录：

- `WinPieGestures/`：主程序、轮盘、输入钩子、配置和插件宿主。
- `WinPieGestures/Plugin/`：插件运行时、安装、校验、贡献点和宿主服务。
- `plugin/sdk/StarPie.Plugin.Abstractions/`：插件唯一允许引用的公共 SDK。
- `plugin/docs/`、`plugin/samples/`：插件 API 文档与样例。
- `tests/`：会启动 GUI 的 UIA/pywinauto 回归套件，由人工明确运行。
- `scratch/`：一次性验证器、复现工程和静态护栏，不参与产品打包。
- `installer/`、`releases/`：安装器与发行产物。

架构职责与入口类详见 [`docs/architecture.md`](docs/architecture.md)。

## 2. 每次任务都必须遵守的约束

### 2.1 性能与交互红线

- 静默后台目标工作集为 15–30 MB；轮盘交互峰值目标为 25–50 MB；完整设置窗口目标为 60–110 MB。
- 鼠标触发到轮盘呈现目标小于 16 ms。低级钩子回调中禁止 IO、等待、网络、复杂计算或 UI 构造。
- 不引入 Electron、MAUI、Chromium 类重量级运行时或大型 UI 框架。
- 高频渲染用到的 `Freezable` 应冻结；禁止把大型 Base64 内容写入常驻模型或 `config.json`。
- 扇区方向、索引和角度必须保持确定性，不能破坏既有肌肉记忆。

### 2.2 架构边界

- `App` 持有进程级生命周期；托盘归 `TrayController`；设置窗口只能通过 `App.ShowSettingsWindow()` 按需创建。
- `App.xaml` 保持 `ShutdownMode="OnExplicitShutdown"`。关闭设置窗口不能结束后台钩子和托盘进程。
- `PluginHost` 是主程序接入插件系统的唯一入口；不要把插件解析、激活或执行逻辑重新散落到 UI 或 `ActionExecutor`。
- 插件只能引用 SDK 与 BCL，禁止引用主程序 `StarPie.dll`。公共 SDK 只增不改，任何 SDK 变化都按公共契约变更处理。
- 配置字段、动作类型和持久化语义默认向后兼容；禁止无迁移地重命名、删除或改变既有含义。

### 2.3 数据、权限与用户控制

- 初始化、窗口打开和普通浏览不得产生系统副作用；WPF 初始化事件必须有状态守卫。
- 官方插件不得随主程序发行包静默携带或静默安装。首次引导在用户确认前不得联网。
- 不得在测试中执行真实插件动作、系统命令、输入注入、窗口操控或硬件操作。
- 用户禁用的插件不得被更新、安装或引导流程擅自重新启用。
- 网络失败不得破坏本地已安装功能；下载必须验证 SHA-256，并遵守现有能力门禁。

## 3. 禁止事项

- 禁止在不理解现有调用链时重写 `PluginSelfTest.cs`、大型 XAML 或配置模型。
- 禁止用 `git reset --hard`、`git checkout --` 或清理命令覆盖他人的未提交更改。
- 禁止把测试删掉、改弱、扩大 `NoWarn` 或只验证字符串存在来冒充行为验证。
- 禁止在 UI 线程同步等待插件动作；设置页“测试”必须走真实动作队列并使用快照。
- 禁止在钩子、轮盘显示、动作热路径中进行插件下载、目录扫描或网络请求。
- 禁止把普通插件动作编码回多个顶层动作类型；UI 保持“插件动作 + 分组子下拉”。
- 禁止未经单独批准执行 commit、push、PR 合并、Tag、打包或 Release 发布。
- 禁止修改与当前任务无关的文件；发现脏工作树时保留并绕开已有修改。

## 4. 按任务读取文档

| 任务涉及 | 开始前必须阅读 |
| --- | --- |
| 架构边界、生命周期、模块归属 | [`docs/architecture.md`](docs/architecture.md) |
| 插件 SDK、宿主、安装、动作、能力或卸载 | [`docs/plugin-system.md`](docs/plugin-system.md) |
| 轮盘几何、渲染、DPI、主题、动画、设置 UI | [`docs/ui-and-wheel.md`](docs/ui-and-wheel.md) |
| 桌宠轮盘会话、中心圆显隐与配置保留 | [`docs/desktop-pet-interaction-plan.md`](docs/desktop-pet-interaction-plan.md)、[`docs/plugin-system.md`](docs/plugin-system.md) |
| 配置模型、序列化、导入导出、动作兼容 | [`docs/config-compatibility.md`](docs/config-compatibility.md) |
| 钩子、SendInput、全屏、UIPI、托盘或窗口 | [`docs/windows-integration.md`](docs/windows-integration.md) |
| 测试、复现、证据、人工门禁 | [`docs/testing.md`](docs/testing.md) |
| 版本号、打包、安装器、发布 | [`docs/release.md`](docs/release.md) |

插件 API 的进一步细节以 [`plugin/README.md`](plugin/README.md) 和 [`plugin/docs/`](plugin/docs/) 为准；版本历史只维护在 [`CHANGELOG.md`](CHANGELOG.md)。

## 5. Build 与 Test 命令

### 5.1 固定主程序编译输出

- 主程序 Release 候选的输出父目录固定为 `StarPie\WinPieGestures\bin\Release\`。
- 每轮构建使用 `build-out-yyyyMMdd-HHmm\` 子目录，即 `build-out-年月日-时分\`；例如 `build-out-20261013-1630\`。
- 主程序 `StarPie.exe`、`StarPie.dll`、SDK 依赖与运行配置直接放在该时间目录下，不得回退覆盖常规 `bin/Release/<TFM>` 输出。
- 同一轮候选的构建、自检和人工验收复用同一个输出目录；源码候选变更后使用新的时间目录。若同分钟重名，不覆盖已有候选，应等待下一分钟或先向用户确认复用。
- 构建必须显式指定输出目录；自检必须直接运行该目录下的候选，禁止使用默认路径的 `dotnet run` 误测旧产物。完成报告须给出候选 EXE 的绝对路径。
- 官方插件仍独立构建，不得将官方插件 DLL 或其私有 SDK 副本混入主程序候选目录。此约定只改变产物位置，不授权安装、替换运行中实例、打包或发布。

### 5.2 构建与验证示例

在仓库根目录运行：

```powershell
# 使用香港时间生成本轮目录；不要覆盖已存在的候选
$buildTime = [TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([DateTime]::UtcNow, 'China Standard Time')
$buildParent = '.\WinPieGestures\bin\Release'
$buildDir = Join-Path $buildParent ('build-out-' + $buildTime.ToString('yyyyMMdd-HHmm'))
if (Test-Path -LiteralPath $buildDir) { throw "候选目录已存在，禁止覆盖：$buildDir" }
New-Item -ItemType Directory -Path $buildDir | Out-Null

# 主程序 Release 构建；必须 0 error，新增 warning 也要解释
# 末尾双反斜杠避免带空格路径经 Windows 命令行转义时丢失引号边界
dotnet build .\WinPieGestures\WinPieGestures.csproj -c Release --nologo "-p:OutDir=$buildDir\\"
if ($LASTEXITCODE -ne 0) { throw "Release 构建失败：$LASTEXITCODE" }

# 插件无界面自检：先指定已构建的测试插件 DLL，不得省略该位置参数
$testPluginDll = '<待验证插件 DLL 的绝对路径>'
dotnet (Join-Path $buildDir 'StarPie.dll') --plugin-selftest $testPluginDll --skip-invoke

# 静态与差异卫生
git diff --check
git status --short
```

按改动范围运行 `scratch/` 中对应护栏。涉及 UI 时，AI 默认不运行会弹窗的 `tests/`；由用户明确启动：

```powershell
python -m pytest .\tests -v
```

完整测试矩阵、i18n 台账和夹具纪律见 [`docs/testing.md`](docs/testing.md)。发布命令不属于普通验证，见 [`docs/release.md`](docs/release.md)。

## 6. 修改后的验证要求

每个改动都必须形成可追溯闭环：

1. **基线**：记录目标仓库、工作树、基准提交和已有脏文件。
2. **范围**：列出允许修改路径和禁止路径；超出范围先停下说明。
3. **复现或失败证据**：Bug 修复先证明问题存在；无法自动复现时写明人工步骤。
4. **实现**：只改任务所需最小范围，不借机重构相邻模块。
5. **自动验证**：重新运行与当前候选绑定的构建、测试、静态护栏和 `git diff --check`，记录命令与退出码。
6. **独立复核**：中高风险、插件契约、配置迁移、发布或跨模块修改需要独立审查；执行者自报成功不能替代复核。
7. **人工门禁**：视觉、手感、硬件、真实插件和系统集成由用户实机验收。人工未通过不得合并。

“构建通过”不等于“任务完成”。完成结论必须绑定当前 diff/候选提交，并同时说明未验证项。

## 7. 编码风格

- C# 使用项目现有命名和 nullable 约定：类型/公开成员 `PascalCase`，局部与参数 `camelCase`，私有字段 `_camelCase`。
- 优先小型、强类型、可测试的类和方法；不要引入 `object` 路由、巨型 `switch` 或重复事实来源。
- 异步路径保持异步；UI 更新通过 Dispatcher，但只在确实有工作时投递，避免闭包阻止插件 ALC 卸载。
- 用户可见文本必须走 `I18n`，覆盖简中、繁中、英文、日文；不要在新 XAML 或代码路径里硬编码中文。
- 数值持久化和插件参数使用 `InvariantCulture`。布尔值 `false` 必须能显式落盘。
- 注释解释非显而易见的约束和原因，不复述代码；事故复盘和长篇推理放入对应 `docs/`。
- XAML 沿用现有资源、间距和主题；新弹窗必须自包含样式并调用 `AppThemeManager.ApplyTheme`。
- 新验证应证明行为或不变量；新增断言要能通过变异或基线失败证明其有效性。

## 8. 项目内技能

项目技能位于 `.agents/skills/`，按任务调用：

- Bug 诊断与根因定位：`bug-investigation`
- 功能规划和范围契约：`feature-planning`
- 回归矩阵与证据收集：`regression-testing`
- 版本、安装器与发布前审查：`release-review`

技能只加载相关领域文档，不得把所有文档重新塞回每次任务上下文。
