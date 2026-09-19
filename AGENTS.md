# StarPie (星盘) - 核心架构与多轮开发继承规范 (AGENTS.md)

> **致未来的 AI Agent 与开发者**：  
> 本文档是 **StarPie (原 WinPieGestures)** 项目的唯一权威工程架构与协作规范指南。当你在新的对话轮次或全新环境中接手本项目时，**必须首先通读本文档**，严格遵循本规范中所确立的架构分层、设计哲学、避坑指南与发布工作流，确保项目在持续迭代中保持高内聚、高品质、零退化与丝滑流畅。

---

## 目录
1. [🌟 项目起源、使命与设计哲学](#1-项目起源使命与设计哲学)
2. [🏗️ 源码架构与核心模块分工](#2-源码架构与核心模块分工)
3. [⚙️ 核心技术机制与避坑规范](#3-核心技术机制与避坑规范)
4. [🧩 插件系统运行时重构](#4-插件系统运行时重构)
5. [🔄 代码生成、编译与发布流水线](#5-代码生成编译与发布流水线)
6. [🎨 UI/UX 与视觉设计规范](#6-uiux-与视觉设计规范)
7. [📜 版本演进与发布记录](CHANGELOG.md)
8. [🤝 Agent 接力协作与交付验收闭环](#8-agent-接力协作与交付验收闭环)

---

## 1. 🌟 项目起源、使命与设计哲学

### 1.1 灵感来源与初衷
- **创作者背景**：机械设计制造及其自动化专业学生，深度使用工业 CAD 软件 **SolidWorks**。
- **灵感核心**：SolidWorks 内置的**鼠标笔势手势轮盘 (Mouse Gestures Wheel)** 能在三维建模中带来行云流水般的盲操提效体验。本项目旨在将这种**工业级的高效轮盘交互迁移至 Windows 桌面全局**，使所有用户在日常办公、代码编写、设计创作与游戏多任务中，均能享受指尖翻飞的极速操作。
- **开源仓库**：GitHub `SoftBlack42/StarPie`。

### 1.2 产品三大工程红线 (Core Non-Negotiables)
1. **极致轻量与低内存驻留 (Ultra-Lightweight & Efficient Working Set)**：
   - **分态务实内存基准**（基于原生 .NET 8 WPF + Win32 真实运行时物理工作集）：
     - **静默后台守护态**（仅托盘与全局底层钩子常驻，控制台未打开）：物理内存平稳驻留于 **15MB ~ 30MB**（系统深睡整理后约 **10MB ~ 20MB**），远优于 Electron 框架应用（普遍 150MB ~ 300MB+）；
     - **轮盘唤出与手势交互态**（DirectX 硬件加速透明渲染、瞬时视觉树与图标缓存）：峰值控制在 **25MB ~ 50MB**，确保热代码常驻物理 RAM，绝不因硬缺页引发掉帧卡顿；
     - **控制台全量 UI 开启态**（4 标签页、实时交互 Canvas、复杂控件树与应用搜索）：控制在 **60MB ~ 110MB**，窗口关闭 30 秒按需释放后平稳回落至后台驻留态；
   - 杜绝引入重量级第三方 UI 库（如 Electron、MAUI、Heavy Chromium），纯基于原生 **.NET 8 WPF + Win32 P/Invoke API** 深度调优；
   - 绘图画刷、笔刷必须显式调用 `Freezable.Freeze()` 消除内存泄漏与 GC 抖动；严禁在常驻数据模型或 `config.json` 中塞入巨型 Base64 字符串，防止 .NET 大对象堆 (LOH) 碎片化导致工作集异常膨胀。
2. **零延迟与极致丝滑 (Zero Latency & 60/120 FPS Fluidity)**：
   - 鼠标右键/中键/侧键按下到轮盘完全呈现场景延迟必须 **< 16ms**；
   - 扇区高亮动画与光晕过渡采用贝塞尔平滑插值，杜绝卡顿与掉帧；
   - 钩子回调线程内绝不执行耗时 IO 或复杂计算，纯轻量位运算捕获。
3. **肌肉记忆与确定性 (Muscle Memory & Reliability)**：
   - 扇区角度与操作严格绑定空间极坐标方向（4 字键、8 字键、12 字键预设档，均为 360°/N 等分且第 0 位固定正东）；
   - 盲操触发命中率 100%，杜绝误触、漂移与错选。

---

## 2. 🏗️ 源码架构与核心模块分工

### 2.1 目录结构全景
```text
g:\Users\2 Better\Desktop\design\
├── WinPieGestures/                # 主工程源码目录 (.NET 8.0 WPF)
│   ├── WinPieGestures.csproj      # 项目配置文件 (版本号、依赖与打包参数)
│   ├── App.xaml / App.xaml.cs     # 应用宿主、单例互斥锁、Hook/托盘/设置窗口生命周期
│   ├── TrayController.cs          # 进程级系统托盘控制器（菜单、主题、UIPI 防护、提示与退出）
│   ├── RadialWindow.xaml(.cs)     # 核心悬浮轮盘窗口 (硬件加速透明渲染、高频动画)
│   ├── SettingsWindow.xaml(.cs)   # 按需创建的控制台主界面（配置面板、实时交互画布）
│   ├── SubActionEditorWindow.xaml(.cs) # 二级级联子动作独立编辑器
│   ├── HotkeyBuilderDialog.xaml(.cs)   # 快捷键拼装组合器 (自包含样式、一键预设芯片)
│   ├── ColorPickerWindow.xaml(.cs)     # 颜色选择器 (色盘选择、色相环与屏幕实时吸色)
│   ├── IconPickerWindow.xaml(.cs)      # 内置矢量 SVG / 图标提取器
│   ├── ProgramPickerWindow.xaml(.cs)   # 软件检索器 (模糊搜索、拼音索引、MRU缓存)
│   ├── InputDialog.xaml(.cs)      # 通用文本输入与配置重命名弹窗
│   ├── GestureController.cs       # 手势状态机 (拖拽位移、极坐标计算、命中测试)
│   ├── MouseHook.cs               # 低级鼠标全局钩子 (WH_MOUSE_LL)
│   ├── KeyboardHook.cs            # 低级键盘全局钩子 (WH_KEYBOARD_LL)
│   ├── ActionExecutor.cs          # 动作调度与 Win32 SendInput 模拟执行引擎
│   ├── ConfigManager.cs           # 配置序列化、持久化、导入/导出与注册表自启管理
│   ├── AppConfig.cs               # 全局配置数据模型 (主题、尺寸、动作、白名单等)
│   ├── WheelProfile.cs            # 单个轮盘方案数据模型 (扇区数、动作槽位列表)
│   ├── CustomColorPreset.cs       # 自定义配色方案模型
│   ├── AppThemeManager.cs         # 窗口深浅色主题画刷注入管理器
│   ├── FullScreenHelper.cs        # 独占全屏检测与 Windows Explorer 穿透识别
│   ├── ActiveWindowHelper.cs      # 前台活动窗口探测器
│   ├── MemoryOptimizer.cs         # 内存整理与工作集压缩工具
│   ├── I18n.cs                    # 多语言国际化字典 (zh-CN, zh-TW, en-US, ja-JP)
│   ├── Renderers/                 # 轮盘切削形态渲染器策略族
│   │   ├── IRadialStyleRenderer.cs    # 渲染器通用抽象接口
│   │   ├── BaseStyleRenderer.cs       # 几何切削基础类
│   │   ├── StyleRendererFactory.cs    # 渲染器工厂
│   │   ├── ClassicRingRenderer.cs     # 经典圆弧与圆角胶囊渲染器
│   │   ├── CleanSectorsRenderer.cs    # 悬浮圆角矩形渲染器
│   │   └── GlassmorphismRenderer.cs   # 液态毛玻璃渲染器
│   └── Plugin/                    # ★ 插件系统宿主实现（详见第 4 节）
│       ├── PluginHost.cs              # 执行/校验/安装的唯一入口接缝
│       ├── PluginRuntime.cs           # 路径注册、激活、调用与异步停用公共运行时
│       ├── PluginPathModules.cs       # 动作/交互事件/轮盘结构三条强类型路径模块
│       ├── PluginCatalog.cs           # 贡献点注册表 + 注册会话（暂存→提交的原子性）
│       ├── PluginLoadContext.cs       # 可回收 ALC（停用即卸载，需重启比例是硬指标）
│       ├── PluginInstance.cs          # 单个插件的运行时状态机与加载计量
│       ├── PluginScanner.cs           # 纯静态 PE 识别（不加载程序集即可读清单与 TFM）
│       ├── PluginManifestReader.cs    # plugin.json / 程序集元数据双通道清单读取
│       ├── PluginInvoker.cs           # 动作调用与超时/取消/串行化调度
│       ├── PluginParameterValidator.cs # 声明式参数约束校验（Required/MaxLength/Min/Max/Regex）
│       ├── PluginParameterForm.cs     # 参数表单动态渲染（9 种 ParameterFieldType）
│       ├── PluginActionBinding.cs     # 「Type + PluginActionRef ⇄ 单 Tag」双向投影
│       ├── ActionParameterProjection.cs # 认领动作的宿主裸字段 → 参数字典投影
│       ├── PluginActionClaimRegistry.cs # 官方在线动作的顶层 Type 认领快照与冲突拒绝
│       ├── OfficialPluginClient.cs    # 官方 catalog 拉取、.spkg 下载与 SHA-256 校验
│       ├── BuiltinActionCatalog.cs     # 仅保留 Hotkey 的进程内动作目录
│       ├── BuiltinActions/             # 内建动作的插件模型适配实现
│       ├── PluginCapabilityLabels.cs   # 能力位 → 安装确认风险文案
│       ├── PluginI18n.cs              # 插件词条 key 的统一解析（短键 ⇄ 全键）
│       ├── PluginListItem.cs          # 插件管理页的列表项 DTO
│       ├── PluginSelfTest.cs          # --plugin-selftest 无界面端到端自检通道
│       ├── PluginSettings.cs          # 插件私有持久化（settings.json）
│       ├── PluginPaths.cs             # 插件目录/清单/日志/便携模式判定
│       ├── PluginRegistryStore.cs     # 启用状态与哈希登记（registry.json）
│       ├── PluginLogger.cs            # 按插件分文件的日志
│       ├── PluginContext.cs           # IPluginContext 实现 + 能力门禁 + 词条注册表
│       └── PluginHostServices.cs      # 动作执行/窗口/剪贴板/通知等宿主服务实现
├── StarPie.Plugin.Abstractions/   # ★ 插件 SDK 契约层（插件唯一允许引用的 StarPie 程序集）
│   ├── IStarPiePlugin.cs          # 插件入口契约（Initialize / Shutdown）
│   ├── IPluginContext.cs          # 插件可见的宿主能力集合
│   ├── Actions.cs                 # ActionDescriptor / ParameterField / ActionResult
│   ├── Registries.cs              # 动作、图标、词条注册表契约
│   ├── Services.cs                # 宿主服务契约（窗口、剪贴板、通知、事件）
│   ├── PluginManifest.cs          # plugin.json 清单模型
│   ├── PluginMetadata.cs          # 程序集元数据兜底模型
│   └── PluginApi.cs               # 契约常量（ApiVersion / 前缀 / 上限）
├── samples/                       # ★ 社区插件示例（可直接构建为可分发的插件目录）
│   ├── HelloAction/               # 参考模板，演示 SDK 全部可做之事（Text/Bool/Enum/Folder 参数）
│   └── ScreenBrightness/          # 压力测试样本：P/Invoke + COM 互操作 + 耗时 IO（Number/Bool 参数）
├── releases/                      # 正式发行包构建归档目录
│   └── vX.Y.Z/
│       ├── Lightweight/           # 依赖运行时的轻量绿色包 (~2.5MB)
│       ├── Standalone/            # 独立单文件免安装自解压包 (~65MB)
│       ├── StarPie-vX.Y.Z-Lightweight-win-x64.zip
│       └── StarPie-vX.Y.Z-Standalone-win-x64.zip
├── assets/                        # 品牌素材（app_icon / tray_icon / 各版 logo / screenshots）
├── attachments/                   # 文档与演示动图素材库 (GIF / PNG)
├── 主题尺寸配置文件/               # 轮盘尺寸预设 JSON（用户可在「高级与系统设置」最下方导入）
├── scratch/                       # 一次性开发工具与验证脚本 —— 不参与构建，可随时清理
│   ├── IconGenerator/             # 从 attachments/cover.v3.png 生成 logo / app_icon / tray_icon
│   ├── Issue17Demo/               # 复现用户反馈用的小 Demo 工程
│   ├── Everything-SDK/            # 本地搜索联调用的 SDK 头文件
│   └── *.ps1 / *.png / test_v170.cs   # 临时验证脚本与对照截图
├── tests/                         # UI 回归套件（pywinauto + UIA；会弹 GUI，由用户手动运行）
│   ├── conftest.py                # 隔离 AppData 沙箱 fixture（另导出 find_exe / launch_app 供需要自控启动的用例复用）
│   ├── test_settings.py           # 设置窗口回归
│   ├── test_plugins.py            # 插件页回归
│   ├── test_i18n.py               # 「切英文后整页已翻译」棘轮回归（台账驱动）
│   └── i18n_baseline.json         # 上述用例的台账基线 —— 只允许变少，新增即红
├── installer/                     # Inno Setup 打包
│   ├── StarPie.iss                # 安装脚本（语言文件在 Languages/）
│   └── build-installer.ps1        # 打包入口 —— 版本号 6 处同步点之一（$Version 兜底值）
├── docs/
│   ├── plugin-system-architecture.md # 插件系统**现行实现**说明（统一调用运行时 + 路径模块 + 活动调用租约 + 异步停用状态机）
│   ├── prototypes/                # 早期交互原型（v1.6.0 / v1.6.5，纯 HTML，仅供设计参考，不参与构建）
│   └── issue100.txt               # 用户反馈原始记录（对应修复留档）
├── PLUGIN_SYSTEM_DESIGN.md        # 插件系统设计大纲（设计草案，正文路径非现状，勿照抄）
├── PLUGIN_SYSTEM_IMPLEMENTATION.md # 实现说明：结构与职责边界、接口用法、已踩过的坑
├── PLUGIN_SYSTEM_PERFORMANCE_AND_API.md # 性能实测 · 完整接口清单 · 社区开发指南
├── PLUGIN_FIRST_ROADMAP.md        # 插件化边界规划（三问判据；定位是规划，不是现状描述）
├── AGENTS.md                      # 本架构与继承开发规范
└── CHANGELOG.md                   # 完整版本演进与发布日志
```

---

## 3. ⚙️ 核心技术机制与避坑规范

### 3.1 极坐标分区与扇区命中测试 (Hit-Testing)
- **极坐标基准**：
  $$\theta = \text{atan2}(\Delta y, \Delta x) \in [-\pi, \pi]$$
  角度 $0$ 为右侧，$\pi/2$ 为正下方（WPF 坐标系 Y 轴向下）。
- **一级轮盘与外圈子环 (Outer Sub-Ring)**：
  根据扇区数量 $N \in \{4, 8, 12\}$ 均匀划分扇区角度 $\Delta \theta = 2\pi / N$，扇区中心角 $\theta_i = -\pi/2 + i \cdot \Delta \theta$。
- **蜂窝扇 (Honeycomb Fan)**：
  - 二级菜单以被选中的主扇区为圆心展开（最多 3 项）；
  - **命中判定算法**：**严禁使用欧几里得圆心欧氏距离判定**！必须采用极坐标夹角绝对距离最近邻划分：
    $$\text{TargetIndex} = \arg\min_j |\text{NormalizeAngle}(\theta_{\text{mouse}} - \theta_j)|$$
    使得光标向左划动时必然命中左侧子叶，向右划动时必然命中右侧子叶，彻底杜绝判定左右颠倒的缺陷。

### 3.2 Win32 `SendInput` 模拟与硬件扫描码注入
- **硬件扫描码映射 (Scan Code)**：
  Windows 部分应用（如 Photoshop、Illustrator、3D CAD 等）直接监听底层硬件扫描码而非虚拟键码。下发按键时必须使用 Win32 `MapVirtualKey(vk, MAPVK_VK_TO_VSC)` 转换扫描码，并为方向键、Delete、Insert、PageUp/Down、Home/End、Win 等键打上 `KEYEVENTF_EXTENDEDKEY` 标志。
- **修饰键时延保持 (Modifier Hold Delay)**：
  下发 `Ctrl + G` 或 `Ctrl + Shift + G` 等组合键时，必须在修饰键按下和主键按下之间保留 **10ms ~ 15ms** 保持时延，并在主键释放后再释放修饰键，避免宿主应用因时序竞争丢失修饰键而误判为单键 `G`。
- **连续数值与文本注入**：
  遇到多位数值（如 `"100"`、`"1920"`）或字符串时，采用 `KEYEVENTF_UNICODE` 字符流逐字符发送，完美规避输入法阻断。

### 3.3 快捷键录制框 (`HotkeyRecorderBox`) 焦点规范
- **禁止 WPF 默认焦点跳转**：
  `Tab` 是 WPF 系统的焦点切换键。在 `HotkeyRecorderBox` 控件中必须在构造函数中执行：
  ```csharp
  KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.None);
  KeyboardNavigation.SetDirectionalNavigation(this, KeyboardNavigationMode.None);
  KeyboardNavigation.SetControlTabNavigation(this, KeyboardNavigationMode.None);
  ```
  确保按下 `Tab`、`Alt + Tab`、`Win + Tab` 时控件不丢失焦点，并能在 `OnPreviewKeyDown` 中拦截 `Key.Tab` / `Key.System` 顺利完成组合录制。

### 3.4 场景隔离、全屏检测与白名单穿透
- **Windows Explorer 进程穿透**：
  桌面窗口（`Progman`、`WorkerW`、`SHELLDLL_DefView`、`SysListView32`）与任务栏（`Shell_TrayWnd`、`Shell_SecondaryTrayWnd`）在全屏检测中必须显式穿透，不得被判定为全屏独占应用。
- **白名单优先级**：
  在「全屏游戏/独占应用自动禁用手势」开启时，位于 `WhitelistedProcesses` 白名单中的程序（如 Photoshop、CAD、Visual Studio 等）具有**最高旁路优先级**，无论是否全屏独占均能呼出轮盘。

### 3.5 配置持久化与自定义配色无损导入/导出
- **数据完整性**：
  `ConfigManager.ExportConfig` 必须将全局 `CustomColorPresets` 列表连同当前主题选择、微调色彩参数、几何尺寸全部导出至 JSON。
- **导入即时刷新**：
  `ImportConfigButton_Click` 导入成功后，必须立即调用：
  ```csharp
  ReloadThemePresets(); // 重构一二级主题下拉列表
  RefreshSlots();       // 刷新动作列表绑定
  RenderLiveWheelPreview(); // 刷新实时交互画布
  ```
  杜绝由于 `_isUpdatingUi` 状态锁导致界面控件脱节的问题。

### 3.6 应用宿主、托盘与设置窗口生命周期
- **进程级所有权**：`App` 是程序宿主，负责持有 `MouseHook`、`KeyboardHook`、`GestureController`、`TrayController`，并通过 `ShowSettingsWindow(int tabIndex = -1)` 统一管理 `SettingsWindow` 的创建与显示。
- **托盘必须独立于设置窗口**：
  - `NotifyIcon`、托盘菜单、暂停/恢复、主题、本地化、提权、退出和提示气泡统一由 `TrayController` 管理；
  - 管理员权限运行时的 `ChangeWindowMessageFilter` / `ChangeWindowMessageFilterEx` UIPI 消息放行必须保留在 `TrayController`；
  - 严禁重新把托盘生命周期放回 `SettingsWindow`，否则静默启动会再次加载完整控制台 UI。
- **设置窗口按需创建**：
  - 静默启动、开机自启时严禁直接 `new SettingsWindow()`；
  - 普通启动、托盘菜单、第二实例唤醒，以及轮盘动作「打开 StarPie 控制台」都必须调用 `App.ShowSettingsWindow()`；
  - 严禁使用 `App.MainSettingsWindow?.ShowSettings()` 作为入口，因为窗口释放后该调用会静默失效。
- **30 秒延迟释放策略**：
  - 用户关闭控制台时先 `Hide()` 并启动 30 秒一次性计时器；
  - 30 秒内重新打开时取消计时器并复用原窗口；
  - 空闲超过 30 秒后才真正 `Close()`，解除外部事件、停止计时器、取消下载/更新任务并释放 ViewModel；
  - 最终释放后只允许调用 `MemoryOptimizer.TrimMemory(force: false)`，不得在日常关闭路径执行 Full GC 与强制工作集剥离，避免下一次轮盘唤起发生硬缺页或卡顿。
- **初始化不得产生系统副作用**：WPF 给 `CheckBox.IsChecked` 赋值时也可能触发 `Checked/Unchecked`。加载自启动状态时必须同时使用 `_isUpdatingUi`、`_isUiInitializing` 与 `_isLoadingAutoStartState` 防护，并比较已加载状态；只有用户实际修改开关时才能调用 `ConfigManager.SetAutoStart()`，严禁打开控制台时创建或删除计划任务。
- **显式退出模式**：`App.xaml` 必须保持 `ShutdownMode="OnExplicitShutdown"`，关闭最后一个设置窗口不能结束后台 Hook 与托盘进程；只有托盘退出、提权重启或明确的应用退出流程可以调用 `Shutdown()`。

---

### 3.7 插件系统 (`StarPie.Plugin.Abstractions` + `WinPieGestures/Plugin/`)
- **三层分界，任何一层都不许越界**：
  - **SDK 契约层** `StarPie.Plugin.Abstractions/`（独立程序集，插件唯一允许引用的 StarPie 程序集）。改动它等于改公共契约，只增不改；
  - **宿主实现层** `WinPieGestures/Plugin/`（`PluginHost` 是主程序唯一的调用接缝）；
  - **示例层** `samples/`（`HelloAction` 是社区参考模板，`ScreenBrightness` 是 P/Invoke + COM + 耗时 IO 的压力测试样本）。
- **插件工程的四条硬约束**（改错任一条都会导致加载失败或类型身份分裂）：
  1. `TargetFramework` 不得高于宿主（`net8.0-windows` / `net8.0-windows10.0.19041.0`），宿主直接读 `TargetFrameworkAttribute` 核对；
  2. `ProjectReference` 必须带 `<Private>false</Private>`，否则产物里会多出一份 `StarPie.Plugin.Abstractions.dll`，出现两份 `IStarPiePlugin` 类型身份，强转全部失败；
  3. 只允许引用 SDK 与 BCL，**严禁引用主程序 `StarPie.dll`**；
  4. **零 NuGet 依赖**（项目内存红线的一部分，也是「插件不得成为新的依赖黑洞」的保证）。
- **注册会话的原子性**：插件在 `Initialize` 期间的一切注册（动作、图标、词条）都只是**暂存**，必须等 `Initialize` 成功返回后才由 `PluginCatalog.Commit` 一次性落表。失败则 `Discard`，绝不留下半套贡献点。
- **⚠️ 词条时序坑（易复发）**：显示名解析发生在 `Actions.Register` 的当时，而词条要等 `Commit` 才写进 `I18n`。若只在注册当时解析，带 `DisplayNameKey` 的动作会**全部落空并静默退回字面文案** —— 而字面文案与译文常常一模一样，所以这个缺陷在中文环境下不露面，等用户切成英文才发现。修法是 `Commit` 落完词条后调用 `ResolveStagedDisplayNames` 补解析一次。**不要**改成「让解析去读暂存表」，那会要求插件遵守「词条必须写在动作之前」这种没人记得的顺序约定。
- **参数契约 = 声明式，插件不提供 XAML**：
  - 插件只声明 `ParameterField`（9 种类型：`Text` / `MultilineText` / `Number` / `Bool` / `Folder` / `File` / `Enum` / `Hotkey` / `Color`），控件由 `PluginParameterForm` 用主程序的隐式样式创建 —— 深浅色、字体、圆角因此由宿主统一保证，主程序改版也不会让插件界面错位；
  - **两层校验，同一入口**：`PluginHost.ValidateActionParameters` 先跑宿主底线 `PluginParameterValidator`（只认 `Required` / `MaxLength` / `Min` / `Max` / `ValidationRegex` 声明，不依赖插件是否记得自查），再跑插件自定义 `IActionContribution.Validate`。**「保存动作」与「执行前」必须都走这一个方法**，否则迟早分叉成「存的时候没事、一触发说参数不合法」。
  - `Bool` 字段未填视为 `false`，**不算必填失败**，也不要「空值即删除」——取消勾选必须显式落盘 `false`，否则插件读到的会是它自己的兜底值（可能为 `true`），表现为「取消勾选没生效」。
  - 数值参数一律用 `InvariantCulture` 读写（宿主侧与 `PluginActionInput.Int/Double` 都是），否则德法等以逗号作小数点的区域会把 `0.5` 解析失败并静默退回默认值。
- **动作调度类别 `ActionKind`**：`Sequential` 占用唯一的动作线程，**任何可能上百毫秒的操作（DDC/CI、网络、目录遍历）都必须声明为 `Background`**，否则用户会明显感到「触发后轮盘卡一下」，直接违背零延迟红线。
- **熔断与「伪失败」**：宿主对连续失败 5 次的动作会判定为插件缺陷并自动 `Quarantined`。因此**环境不具备条件不是插件失败**（如显示器未开启 DDC/CI），必须返回 `ActionResult.Ok(..., silent: false)` 并说明原因；返回 `Fail` 会让用户连点几次就把一个正常插件弄成「已隔离」。
- **界面接缝：动作类型下拉「收敛成一个类型 + 一个子下拉」**：插件动作在数据模型上仍是 `ActionItem.Type = "Plugin"` + `PluginActionRef`（`PluginId` + `ContributionId` + `FullId`），但界面上**类型下拉只承载一个选项**「插件动作」，具体是哪个动作由紧随其后的子下拉决定。因此：
  - 类型下拉的 `Tag` 就是**裸 `Plugin`**，不需要也不应该编码身份（历史上有过 `Plugin:<贡献点全ID>` 的编码与配套的 `TryParseTag`/`ProjectTag` 退化逻辑，收敛后全部成了死代码，已删除）；
  - 子下拉用 `ListCollectionView` + `PropertyGroupDescription(GroupName)` 按插件名分组，分组头不是 `ComboBoxItem`，**天然不可选中** —— 从结构上排除「选中了插件名却不是一个动作」这种非法状态；
  - **`PluginActionOptions` 每次求值都新建视图**，所以它只能在**类型切换**时通知重建（`Type` / `AggregatedType` 的 setter），**绝不能**纳入 `NotifyAllPropertiesChanged`：否则任何无关属性变更都会重建视图，而 `ItemsSource` 一变 `ComboBox` 就会把 `SelectedValue` 置空，用户配好的动作会被静默清掉。同理 `SelectedPluginActionFullId` 的 setter 要**忽略空值写入**；
  - **切换类型时不要清空插件引用**：来回切一次类型就把配置弄丢，是最容易被当成「软件有 bug」的行为。
  - 分组名必须**按插件去重统计**重名：直接对注册动作逐个取名，一个有 9 个动作的插件会被数成 9 次，「重名」于是永远成立，组标题会莫名其妙拖上一串插件 ID。
- **图标 key 前缀**：插件图标形如 `plugin:<pluginId>:<shortKey>`，由 `IconHelper.GetSvgPathByKey` 在 `IconMap` 命中之后、裸 path 判定之前解析。插件 SVG 必须用最朴素的 `M/A/L/H/V/Z` 构造 —— 语法一错会让轮盘几何解析抛异常，收益远小于风险。
- **官方在线模块是默认来源**：12 个原内建动作的源码、打包和发布均在 `StarPie-Official-Plugins` 仓库完成；主仓库不得再引用、构建、复制或随发行包携带这些 DLL。
  - 启动完成后由 `OfficialPluginClient` 在后台读取官方 GitHub Release catalog；缺失或版本过旧的官方模块下载 `.spkg`，校验包大小、包 SHA-256、`module.manifest.json` 和程序集 SHA-256 后安装并启用。
  - 网络请求、下载和解压严禁进入鼠标钩子、轮盘显示或动作执行热路径；网络失败只记录日志，本地已安装版本继续可用。
  - 当前阶段只使用哈希校验；不得下载或依赖 `cosign.exe`、`module-catalog.sigstore.json` 或其他外部验签程序。
- **两个插件目录，职责严格分开**：
  - **社区插件候选区** `程序目录\plugin\`（`PluginPaths.ScanRoot`）：只供用户手动安装社区 `.dll`；宿主只读、只扫描，绝不创建、写入或删除。`starpie.*` 保留 ID 不得通过这里安装。
  - **可写宿主区** `%LOCALAPPDATA%\StarPie\plugin-data\`（`PluginPaths.Root`）：官方在线模块、社区插件、`registry.json`、`health.json`、插件私有 `data\` 都在这里。
  - 本次插件系统尚未进入主分支发布，`registry.json` 不承担旧版兼容义务；可直接按官方在线模块模型演进。
- **顶层类型认领（Type Claim）**：只有 `Official=true` 的官方在线模块能认领 `Launch`、`Command` 等历史 `ActionItem.Type`。`PluginActionClaimRegistry` 从登记表快照构造路由，不加载 DLL；冲突认领必须整组拒绝。
  - 官方模块安装后必须把 `ClaimedTypes` 写入登记表，并在安装、启用、停用、卸载后重建路由表。
  - 保留 ID 前缀只在官方在线安装和已登记官方模块的装载路径放行；社区手动安装必须拒绝保留 ID。
  - 认领类型的宿主裸字段仍通过 `ActionParameterProjection` 以显式白名单投影为参数字典；不投影外观字段。
- **派发顺序不可改变**：`ActionExecutor` 固定按「内建 Hotkey → 官方类型认领 → 普通 `Type="Plugin"` / 历史 switch 兜底」执行。
- **能力门禁（Capability Gate）：五个服务面、四个能力位，每一位都对应一个真实强制点**：
  - 带门禁的是五个「产生不可忽略后果」的服务：`IHostCommandService.Run`（命令）与 `IHostShellService.Invoke`（Shell 动词里有 UAC 提权的 `Windows.RunAs`、清空回收站这类不可撤销操作）挂 `Process`；`IHostWindowService` 的五个执行方法（挪走 / 置顶 / 改透明度 / 切走用户正在用的窗口）挂 `WindowControl`；`IHostScreenCaptureService.CaptureAndRecognize`（会抓走用户整个屏幕的内容）挂 `ScreenCapture`；`IHostSystemService.RunPreset`（系统控制既起进程也发按键）挂 `InputSimulation`。清单未声明对应能力时直接抛 `PluginCapabilityDeniedException`，**绝不静默降级**。
  - **每个服务认自己那项能力，不复用别人的**。`WindowControl` 刻意不与 `Process` 合并：安装确认页上展示的能力必须对应一个真实后果，用户看到「进程」想的是「它要启动程序」，而实际后果是他的窗口被挪走 —— 那是标签名不副实。`Ui` 同样不符（它的语义是「打开自己的窗口」）。`InputSimulation` 也刻意不与 `Process` 合并，虽然「系统控制」里两者都会发生：`Process` 的后果是**多出一个后台进程**，`InputSimulation` 的后果是**往用户正在打字的那个窗口里按键** —— 用户能接受前者不代表能接受后者。五个服务的门禁实现共用一个基类（`PluginGatedService`），所以**复制粘贴时把 required 传错不会有任何编译错误** —— 自检 `[3j]` 用「只声明 A 的插件调 B 的服务」这一组交叉断言守它，否则「认错能力标志」会让上面那些断言照样全绿。
  - **门禁必须在 `Guard` 之外**。若挪进 `Guard` 里，异常会被吞掉、转成一个 `false` 返回值，用户看到的是「命令没执行」而不是「本插件缺少「进程」能力」—— 前者会被当成软件 bug 反复报，后者才指向真正该改的地方。
  - **元数据（`Terminals` / `Verbs` / `Layouts` / `OpacityMinPercent` …）刻意不受门禁约束**：插件的 `Parameters` 是属性、声明期（注册前）就要读这几份清单，在那里抛异常会让一个「忘了声明能力」的插件在注册阶段整个崩掉 —— 而它其实只是不能在运行时干活而已。**门禁拦的是「产生后果」的调用**。
  - **`PluginCapabilityDeniedException` 刻意不继承 `PluginContractException`**：后者的语义是「违反注册契约」，宿主会因此把插件整体标记为加载失败并卸载；而「清单里漏了一行能力声明」远不到那个程度。真继承上去，用户看到的是「插件突然坏了 / 被系统禁用了」，排查方向会完全跑偏。
  - **必须说清它换来的不是安全**：进程内插件本来就能自己 `Process.Start` / P/Invoke `SetWindowPos`，SDK 拦不住。门禁换到的是「安装确认页上展示的能力真的对应一个后果」—— 漏掉它，那个勾选在运行时没有任何对应物，才是真正骗人的地方。
  - **只能加在新接口上**。`IHostActionInvoker` 的七个方法是既有契约，补门禁会让已发布、未声明该能力的插件突然失败（破坏性变更）。
  - **新增能力项要一次只加「有强制点的那一项」**。S3a 只加了 `WindowControl`（同时落地了它的门禁），`ScreenCapture` / `InputSimulation` 各自等 `IHostScreenCaptureService`（S4a）/ `IHostSystemService`（S4b）落地时再加（均已落地）—— 提前加会出现「安装确认页展示了这个能力、运行时却没有任何对应物」，正是上面那条要消除的东西。**配套纪律**：确认页能力文案集中在 `PluginCapabilityLabels`，枚举每加一个非空能力位必须同步加文案，自检 `[3j]` 有机器护栏 —— 文案缺失的症状是用户在确认页看到一个勾选项却读不到它意味着什么。表里存的是**词条键**而不是文案（存文案的话，`static readonly` 只在类型初始化时求值一次，切完语言确认页还是旧语言）；键名写错时 `I18n.T` 会原样返回键名 ⇒ 界面上出现一行裸键名，既不空白也不像错的，`[3j]` 专门判了这一种；英文页漏翻由自检 `[3e]` 守（见 §5.1）。
  - **选探针时用「注定无副作用」的调用**：`[3j]` 验门禁一律传空参数（空命令 / 空动词 / 空布局码）。这样即使门禁真的写错了、调用被放行，也只会撞上服务内部的空值短路并返回 `false`，**不会动到自检者自己的窗口或起一个真进程** —— 否则门禁一错，自检就会顺手改掉用户窗口的状态，而那时所有人都在看报错，没人会想到这个附加副作用。
- **宿主服务面的元数据必须只有一份来源**：`PluginCommandService.Terminals` 是终端清单的**唯一事实来源**（外移后的「运行命令」动作直接读它，不在插件里另抄一份），所以不存在「宿主改了下拉、插件没跟上」的漂移。**每次访问都重取词条、不缓存** —— `I18n` 的当前语言可以在运行时切换，缓存住的话用户切到英文之后下拉里还是中文。
  - 同理 `PluginShellService.Verbs` 取 `ShellToolItem.Id`（`copy_path`）而不是 `Verb`（`Windows.CopyAsPath`）：用户配置里存的是短 ID，而 `Verb` 是执行体 `switch` 里的规范名。**传错这一个字段，动作会静默无效** —— 因为 `ExecuteShellTool` 的 `default` 分支是空的。
  - 同理 `PluginWindowService.Layouts` 由 `WindowTiler.LayoutKeys` + `LayoutDisplayName` 现取，三个标记（`Cycle` / `CycleBack` / `Restore`）与透明度范围（`MinOpacityPercent` / `MaxOpacityPercent`）也一律转发宿主常量。**这些值写死在插件里必然漂**：宿主加一个布局、或把透明度上界从 100 调到 90，插件那份会继续把旧范围展示给用户并据此判断合法性。自检 `[3j]` 用 `SequenceEqual` 逐项比对（连顺序都比 —— 顺序即下拉顺序）。
  - **`Verbs` 不是白名单**：`ExecuteShellTool` 的每个功能同时接受 `Id` 与 `Verb` 两套命名，按清单校验会把另一套命名的老配置整体判死。它只用来做下拉与展示。
  - **ShellTool 的参数刻意声明成自由文本而不是 `Enum`**：它的正式入口是带搜索/分类的 `ShellActionPickerWindow`，压进通用下拉是体验降级、还会让清单出现两份；而执行体接受两套命名，按清单校验会判死老配置。
- **`PluginApi.ApiVersion` 那处重复无法用语言特性消除**：`public const string ApiVersion = $"{ApiVersionMajor}.{ApiVersionMinor}"` 编译不过（CS0133 —— C# 的常量插值只对 `string` 常量成立，这两个组成部分是 `int`）。所以它手写在 `PluginApi` 里，改版时必须两处同改，由自检 `[3j]` 断言两者一致。**当前契约版本 1.4**，演进清单以 `PluginApi.ApiVersionMinor` 的注释为准（1.1 新增能力门禁 + `IHostCommandService` / `IHostShellService` / `IHostInfo.HasCapability`；1.2 新增 `IHostWindowService` + `WindowControl`；1.3 新增 `IHostScreenCaptureService` + `ScreenCapture`；1.4 新增 `IHostSystemService` + `InputSimulation`）。**每加一个服务面就在那份注释里补一条，别只改数字。**

---

## 4. 🧩 插件系统运行时重构

> 本节是 2026-09-17 与 `devplugin` 侧同步后**已采用**的运行时形态；插件系统的通用规范
> （两个插件目录、候选安装三条规则、顶层类型认领、宿主能力门禁、派发顺序等）见 **§3.7**。

- **详细架构文档**：宿主分层、插件加载与原子注册、`PluginInstance` 包装、活动调用租约、异步停用和动作执行全链路统一维护在 [`docs/plugin-system-architecture.md`](docs/plugin-system-architecture.md)。
- **统一调用入口与路径模块**：`PluginHost` 仍是主程序唯一接缝，其后由 `PluginRuntime` 登记并分流 `action-execution` / `interaction-event` / `wheel-structure` 路径。路径公共接口只统一生命周期通知与异常隔离，具体请求和结果必须保持强类型；严禁退化成 `Invoke(path, object)` 或中央巨型 `switch`。新增路径应注册新的 `PluginPathModule`，不得复制一套插件状态、停用和卸载逻辑。
- **激活机制公用、加载策略归路径所有**：`PluginActivationCoordinator` 只负责查找实例、检查启用/隔离/兼容状态、合并并发加载和执行 `PluginInstance.Load`，绝不擅自修改用户的 `Entry.Enabled` 偏好。动作执行允许对“已启用但未加载”的插件惰性加载；交互事件不得因广播而加载插件；轮盘结构将来只允许按明确 Provider 引用有条件加载并配合缓存回退。用户停用、更新与卸载必须先把 `Entry.Enabled=false` 落盘，再开始撤销与卸载；插件系统总开关和应用退出只停止当前运行时，不得清空插件自身的启用偏好。实例级加载锁内还要复核一次，防止停用与首次调用交错后重新拉起插件。
- **动作路径拥有完整执行语义**：`ActionExecutionPathModule` 负责从 `ActionItem` 复制不可变 `PluginActionRequest`，再按“公用激活 → FullId 查询 → 同一 registration 参数校验 → `PluginInvoker` 调度”执行。设置页校验复用同一校验实现但不得触发惰性加载；动作解析和执行逻辑不得重新塞回 `PluginHost`。
- **活动调用租约由宿主自动维护**：每次进入插件自定义 `Validate`、`ExecuteAsync`、事件回调或结构查询前，必须从 `PluginInstance` 获取内部 `PluginInvocationLease`；插件开发者不可见也不手动维护。`PluginInstance` 按实例保存 `_activeCallCount`、`_acceptingCalls` 与停止取消源，进入 `Stopping` 后原子拒绝新租约。Background 与超时任务的租约必须保持到真实 `Task` 结束，不能在排队或向用户报告超时后提前释放。
- **停用是异步状态机**：`DisableAsync` 必须先关闭 `Entry.Enabled` 和新租约入口，再撤销路径路由、发送取消并等待活动租约归零，最后才允许 `Shutdown` 与 ALC 卸载。普通停用默认等待 5 秒；超时后返回 `Pending`、保持后台观察且不得强制卸载。热重载、覆盖安装和卸载只有拿到完全停止结果后才能继续；设置页不得在 UI 线程同步等待。

---

## 5. 🔄 代码生成、编译与发布流水线

### 5.1 代码构建与修改原则
- **优先直接维护 `WinPieGestures/` 源码**：项目源码已完整解耦，可以直接在 `WinPieGestures` 中进行修改、扩展与调试。
- **流水线工具 `scratch/Decompiler/Program.cs`**：当需要批量从基线生成或大范围重构时，同步维护 `Program.cs` 并通过 `dotnet run --project scratch/Decompiler` 生成源码。
- **`PluginSelfTest.cs` 的段落号是结构契约，不是装饰。** 它承载 `[0]`…`[7]`（含 `[3b]`/`[3c]`/`[3d]`/`[3e]`/`[3j]`）共十余段断言，而本文件是全仓**唯一**没有单测保护的执行体 —— 它自己就是验证手段。任何「整文件重写」或「解决冲突整体取一侧」都可能在无人察觉的情况下整段顶掉断言。
  - **真实事故**：`refactor` 分支在旧基线上重写了本文件（2813 行 → 945 行），合并时整体取它，导致 `devplugin` 侧后加的 `[3j]`（宿主服务面与能力门禁，~200 行，含跨能力交叉断言）连同 `[3k]`/`[3m]` 一起消失。此后 `AGENTS.md` §3.7、`PluginCapabilityLabels` 类注释、`PluginHostServices.RunPreset` 注释**仍在引用 `[3j]`**，也就是说后续所有「已由 `[3j]` 守」的结论全都没有依据。已于 2026-09-19 恢复（按现行服务名重写，非照抄）。
  - **改本文件前后都要比对段落号集合**：`grep -o '\[[0-9][a-z]*\]' WinPieGestures/Plugin/PluginSelfTest.cs | sort -u`。少一段就得回答「它守的东西现在由谁守」，答不上来就是回归。这条纪律同样适用于其它「文档/注释在引用它」的断言集合（见技能 `merge-integrity-audit`）。
  - **新增断言要自证有效**：`[3j]` 恢复时用变异测试验过 —— 把 `PluginWindowService` 的 required 从 `WindowControl` 改成 `Process`，构建**仍 0 警告**（编译器抓不到），自检当场报 3 条 [FAIL]。一条从没红过的断言不算护栏。
  - **`[3e]` 守「确认页整页随语言切换」**（2026-09-19 加）。安装确认页是最不能含糊的一页（用户在这里决定要不要让这段代码在自己电脑上跑），而它的正文里唯一的非词条来源是清单字段与文件路径 —— 所以自检用**合成**的扫描结果（全 ASCII）构造正文，逐语言断言：① 无未替换的 `{n}` 占位符；② **英文页里一个方块字、假名或全角标点都不许有**；③ 四种语言产生四份互不相同的正文；④ 关键字段真的拼进去了；⑤ 两种「装完是否立即启用」产生不同正文。另有一条守「每个状态位都有安装后果文案」，判据写成「共用兜底措辞的状态集合正好是哪几个」—— 新增状态位忘了配文案时会落进那个集合，断言随即变红（写成列举式的话，新状态位根本没机会被这条断言看见）。
    - **这条断言上线当天就抓到一个真实漏翻**：能力位说明（`PluginCapabilityLabels`）整块是硬编码中文，且**能力连接符是写死的顿号**。它藏在「已经接好 i18n 的确认页」内部 —— 只读那页的代码、或只看词表覆盖率，都发现不了；只有真的切到英文才会看见「This plugin declares the following capabilities: · 启动进程 / 执行命令」。**教训**：`string.Join("、", …)` 这类「分隔符也算文案」的漏翻，是 i18n 审查最稳定的盲区（`PluginScanFailureSeparator` 当初就是为同一件事建的）。
    - 能写出来是因为正文被搬进了 `PluginInstallConfirmationText`（纯函数、不碰控件不弹窗）。**留在 `SettingsWindow` 里的话，无界面自检根本碰不到它** —— 这就是「可测性」与「机器护栏」之间那条直接通路。

### 5.2 标准构建与发布命令集
```powershell
# 1. 编译 Release 版本并校验 0 错误
dotnet build "g:\Users\2 Better\Desktop\design\WinPieGestures" -c Release

# 2. 发布轻量版 (Lightweight, 需本地 .NET 8 运行时, 体积 ~2.5MB)
dotnet publish "g:\Users\2 Better\Desktop\design\WinPieGestures" -c Release -r win-x64 --no-self-contained -o "g:\Users\2 Better\Desktop\design\releases\vX.Y.Z\Lightweight"

# 3. 发布独立免安装版 (Standalone, 自带运行时, 单文件绿色版, 体积 ~65MB)
dotnet publish "g:\Users\2 Better\Desktop\design\WinPieGestures" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "g:\Users\2 Better\Desktop\design\releases\vX.Y.Z\Standalone"

# 4. 编译 Inno Setup 自动化安装包 (Setup.exe, 自包含 .NET 8 独立运行时, LZMA2 固实压缩, 体积 ~30MB)
# (需本地安装 Inno Setup 6, 或直接运行 powershell installer/build-installer.ps1)
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "/DMyAppVersion=X.Y.Z" "/DSourceDir=g:\Users\2 Better\Desktop\design\releases\vX.Y.Z\Standalone" "/DOutputDir=g:\Users\2 Better\Desktop\design\releases\vX.Y.Z" "/DOutputBaseFilename=StarPie-vX.Y.Z-Setup-win-x64" "g:\Users\2 Better\Desktop\design\installer\StarPie.iss"

# 5. 自动化打包生成 ZIP 压缩归档
powershell -Command "Compress-Archive -Path 'g:\Users\2 Better\Desktop\design\releases\vX.Y.Z\Lightweight\*' -DestinationPath 'g:\Users\2 Better\Desktop\design\releases\vX.Y.Z\StarPie-vX.Y.Z-Lightweight-win-x64.zip' -Force; Compress-Archive -Path 'g:\Users\2 Better\Desktop\design\releases\vX.Y.Z\Standalone\*' -DestinationPath 'g:\Users\2 Better\Desktop\design\releases\vX.Y.Z\StarPie-vX.Y.Z-Standalone-win-x64.zip' -Force"
```

> **发布前校验：主程序发布包不得再包含官方插件 DLL 或旧的 `plugin\` 随包来源区。** 官方模块由 `StarPie-Official-Plugins` 的 GitHub Release catalog 在运行后后台下载；安装包只需包含主程序、SDK 契约程序集和插件运行时。
>
> 发布前至少验证：干净 `plugin-data` 下启动能后台拉取模块；网络失败时不阻塞首帧；已安装模块在离线时仍可加载。

### 5.3 版本号同步检查清单 (Version Sync Checklist)
每次发布新版本 `vX.Y.Z` 时，必须同步更新以下位置（`AppVersionInfo` 是运行时版本的唯一来源，界面 / 日志 / 托盘 / User-Agent 全部从它取，**不要再在别处写版本字面量**）：
1. `WinPieGestures.csproj`：`<Version>X.Y.Z</Version>`, `<AssemblyVersion>X.Y.Z.0</AssemblyVersion>`, `<FileVersion>X.Y.Z.0</FileVersion>`（`<Version>` 经 `AssemblyInformationalVersion` 驱动 `AppVersionInfo`）
2. `AppVersionInfo.cs`：`FallbackVersion` 兜底值（取不到程序集元数据时用）
3. `SettingsWindow.xaml`：设计期占位文本（侧边栏版本、关于卡片徽标、更新页「当前运行版本」、关于页里程碑），共 4 处
4. `CHANGELOG.md`：在顶部添加规范的 `## [vX.Y.Z] - YYYY-MM-DD` 详细变更日志
5. `installer/build-installer.ps1`：`$Version` 兜底值（正常情况下脚本从 csproj 读）
6. `installer/StarPie.iss`：`MyAppVersion` 与 `MyAppNumericVersion` 两个兜底值（正常情况下由脚本以 `/D` 覆盖）

### 5.4 UI 回归套件（pywinauto + UIA）

```bash
/c/Users/23836/.workbuddy/binaries/python/envs/default/Scripts/python.exe -m pytest tests/ -v
```

**由用户手动运行**（会弹 GUI）。改 UI 后必须重跑；AI 侧只跑 `--plugin-selftest` 与 `scratch/` 下的静态护栏脚本。

| 文件 | 只管 |
| --- | --- |
| `test_settings.py` | 设置窗口交互（滑块、标签页、方案管理、快捷键录制…） |
| `test_plugins.py` | 插件页渲染与扫描目录语义 |
| `test_i18n.py` | **切英文后整页翻干净没有**（台账驱动） |
| `conftest.py` | 沙箱与启动夹具。另导出 `find_exe()` / `launch_app()`，供需要自己控制启动现场的用例复用（**别再抄一份候选路径列表**） |

#### `test_i18n.py` 是台账（棘轮）断言，不是「一条中文都不许有」

判据是**集合包含**：本次观测集合 ⊆ `tests/i18n_baseline.json`。**只允许变少，新增即红。**

之所以不写成「清零」，是因为漏接的真实量级是「一整批无名控件从没接过线」：`SettingsWindow.xaml` 里含中文的
`Text`/`Content` 有 **441 处没有 `Name`**，其中 **376 处词表里压根没建键**。这批控件既进不了静态差集
（`scratch/check_i18n.py` 只扫带 `Name="X"` 的），也进不了按 auto_id 查询的 UI 断言 —— 静态与动态双双漏掉。
先落一张网挡住**新增**，比等全部还清再上护栏有用得多。

**修完一批漏接后收紧台账**：

```bash
STARPIE_I18N_UPDATE_BASELINE=1 <venv>/python.exe -m pytest tests/test_i18n.py -v
```

它会用**同一条采集代码路径**重采（所以台账不会与用例逻辑漂移），该用例以 `skipped` 结束 ——
标定不是验收，别当成跑过了。跑完 `git diff tests/i18n_baseline.json`：**少掉多少条**就是这一轮的真实战果。

**已知盲区**（别把「这条绿了」当成「全站已翻译」）：
1. 只覆盖**遍历到的页签**里、真的渲染出来且含中文的控件（折叠页签的内容不进自动化树）；
2. `tab_4`（关于 / 更新日志）**不在范围内**：正文是发行说明散文，项目有意只发中文，
   纳入台账会让「每次发版新增一条 release note」都变成一次失败，从而训练人去改台账；
3. 数字已归一化为 `#`（版本号 / 时间戳 / 扇区号每次运行都会变）；CJK 判据**刻意不含 U+3000**
   （表意空格在本项目里当作排版分隔符用，与语言无关）；
4. 操作系统提供的窗口按钮（`关闭` / `最大化` / `最小化`）跟随系统语言而非应用语言，属假阳性，已排除；
5. **`DataTemplate` 里的文案看不到**：命名域不同，`Name` 对它无效，正确解法只能是
   `{Binding}` 到视图模型的**本地化属性**或 `AutomationProperties.AutomationId`。
   已知欠账（**约 22 条**，归入「插件面 i18n」）：`SettingsWindow.xaml:3990` 的
   `Content="🗑 卸载"` 与 `Content="启用"`，以及 `BuildPluginListItem` / `DescribePluginState`
   里 `作者` / `贡献 N 个动作` / `未加载` / `声明能力：` / `已签名` / `未签名` / `外部路径`
   与 10 个状态名 —— 两处都是**代码拼串**，静态差集同样看不见。

**为什么用例要主动掐网**：插件系统在启动约 2 秒后后台同步官方模块（`ScheduleOfficialPluginSync`）。
同步赶在采集之前完成，插件页就会多出「已安装模块」卡片；赶不上就没有 —— 同一份代码给出
时多时少的观测集合，台账会随机红。所以 `test_i18n.py` 把 `HTTP(S)_PROXY` / `ALL_PROXY`
指向一个必然拒绝连接的本地端口（`127.0.0.1:1`）。实测 .NET 在 Windows 上同样优先读这些变量，
同步立即失败且**只记日志**。这样用例在离线 / CI 环境同样成立。

> **连带影响（记在这，免得日后查错方向）**：断网后 `tab_3` 的两条更新状态文案停在
> `正在检查更新...` / `上次检查: 未检查`（联网时是「当前已是最新版本」与带时间戳的另一份）。
> 它们是「网络可达性」的函数，不是界面接线的结果，台账冻结的是断网那一份。
> **若恰好只有这两条报红，先确认代理变量是否仍生效。**

#### 夹具纪律（`test_plugins.py` 与 `test_i18n.py` 都踩过）

- **要改「进程启动前的现场」，就自己用 `launch_app()` 启动**，别指望 `app` 夹具能替你改 ——
  它启动的时机比用例早。`test_i18n.py` 即以此把 `Language` 写进 `config.json`，绕开
  `combo.select("🌐 [EN] English")` 因 emoji 被剥掉而抛的 `IndexError`。
- **别自己拼一份假配置**：`EnsureConfigHealth` 只校验、不补默认数据，写一个 `{"Language":"en"}`
  得到的是**空轮盘**（动作名显示占位符、子动作数 0）。`test_i18n.py` 的做法是先让程序
  自己生成默认配置、再改语言那一个字段，这样标定到的才是**真实首装现场**。

---

## 6. 🎨 UI/UX 与视觉设计规范

### 6.1 界面布局与卡片规范 (Settings Console)
- **四标签页导航**：
  1. 🎨 **外观与形态**：主轮盘/二级轮盘尺寸、内径、外径、倒角、图标大小、文字字号、切削形态（经典圆弧、圆角胶囊、极简扇区、蜂巢六边形）、主题与自定义配色面板；
  2. ⚡ **手势与动作**：触发按键（右键/中键/侧键）、多级轮盘总开关、二级菜单展示样式（外圈子环 / 蜂窝扇）、动作映射列表（支持 `[ ⚙️ 拼装 ]` 与直接录入）；
  3. 🛡️ **触发与场景**：黑白名单切换、全屏独占检测、手势容差距离、外甩脱离取消开关与灵敏度；
  4. ⚙️ **高级与系统**：开机静默自启、语言切换（四语系）、配置导入/导出备份、版本里程碑。
- **实时交互画布 (Live Preview)**：
  - 位于主界面右侧常驻，支持鼠标悬停、扇区高亮动画即时反馈；
  - 顶部配备 `[ 🔘 一级主轮盘配置    🌟 二级级联轮盘配置 ]` 分段切换开关，左侧尺寸与配色面板随之联动。

### 6.2 深色模式高对比度规范
- 在极夜曜黑（`ObsidianDark`）与钛金深灰（`TitaniumGray`）主题下：
  - 标题、常规文本与开关控件文字的前景颜色必须严格绑定为高亮度白色（`#F8FAFC` / `#FFFFFF`）；
  - 严禁出现与背景色（`#0F172A` / `#18181B`）对比度低于 4.5:1 的灰暗文字。

### 6.3 对话框与子窗口自包含规范
- 所有弹窗（如 `HotkeyBuilderDialog`、`InputDialog`、`ColorPickerWindow`）：
  - 必须在 `<Window.Resources>` 内置完整的自包含按钮与控件样式；
  - 必须在构造函数中调用 `AppThemeManager.ApplyTheme(this, ConfigManager.CurrentConfig?.AppTheme ?? "System")`，跟随主程序主题。

---

## 7. 📜 版本演进与发布记录

完整的版本发布时间、功能新增、问题修复与架构演进记录统一维护在：

- [CHANGELOG.md](CHANGELOG.md)

为避免版本信息在多个文件中重复维护并产生偏差，`AGENTS.md` 不再保存版本里程碑明细；发布或调整版本时，应直接更新 `CHANGELOG.md`。

---

## 8. 🤝 Agent 接力协作与交付验收闭环

当新的 AI Agent 会话开始时，请务必执行以下**五步交付闭环**：

```mermaid
graph TD
    A[1. 需求与上下文分析] --> B[2. 查阅 AGENTS.md 与避坑规范]
    B --> C[3. 架构解耦实现与代码编写]
    C --> D[4. 本地编译构建与 0 错误校验]
    D --> E[5. 双架构打包、更新日志与交付归档]
```

1. **需求理解与方案规划**：确认修改范围，严防破坏既有轮盘手感、内存优化与场景隔离逻辑；
2. **规范核对**：检查样式是否自包含、快捷键是否支持扫描码、命中判定是否使用极坐标、文字对比度是否达标；
3. **精准编码**：修改对应模块，保持命名规范与注释完整性；
4. **编译与验证**：执行 `dotnet build WinPieGestures -c Release` 确保 **0 错误**；
5. **打包与日志归档**：
   - 运行轻量版与独立版发布命令；
   - 生成 `StarPie-vX.Y.Z-Lightweight-win-x64.zip` 与 `StarPie-vX.Y.Z-Standalone-win-x64.zip`；
   - 更新 `CHANGELOG.md` 与 `WinPieGestures.csproj` 版本号。

---
*StarPie 致力于将工业级的极速手感带给每一位创作者。遵循本规范，共同打造最纯粹、极致的 Windows 轮盘工具！*
