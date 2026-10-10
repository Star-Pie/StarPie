<div align="center">

<img src="https://socialify.git.ci/Star-Pie/StarPie/image?font=Inter&amp;forks=1&amp;issues=1&amp;logo=https%3A%2F%2Fraw.githubusercontent.com%2FStar-Pie%2FStarPie%2Fmain%2Fassets%2Flogo_dark.png&amp;name=1&amp;owner=1&amp;pattern=Floating+Cogs&amp;pulls=1&amp;stargazers=1&amp;theme=Auto" width="100%" alt="StarPie 齿轮背景横幅：项目 Logo 与 Stars、Forks、Issues、Pull Requests 统计" />

# StarPie (星盘)

### 轻量、快捷的 Windows 鼠标轮盘手势与效率工具

**Lightweight, Fast & Configurable Radial Pie Menu for Windows 10 / 11**

[![Release Version](https://img.shields.io/github/v/release/Star-Pie/StarPie?style=flat-square&logo=github&color=2563EB)](https://github.com/Star-Pie/StarPie/releases)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011%20(x64)-0078D4.svg?style=flat-square&logo=windows)](https://microsoft.com/windows)
[![.NET](https://img.shields.io/badge/.NET-8.0%20WPF-512BD4.svg?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-10B981.svg?style=flat-square)](LICENSE)
[![GUI Tests](https://img.shields.io/badge/GUI%20Tests-manual%20verification-64748B.svg?style=flat-square&logo=pytest)](tests/)
[![Language](https://img.shields.io/badge/Language-zh--CN%20%7C%20zh--TW%20%7C%20en%20%7C%20ja-8B5CF6.svg?style=flat-square)](#i18n)
[![Downloads](https://img.shields.io/github/downloads/Star-Pie/StarPie/total?style=flat-square&logo=github)](https://github.com/Star-Pie/StarPie/releases)
[![QQ 公测群：1079199287](https://img.shields.io/badge/QQ-1079199287-12B7F5?style=flat-square&logo=qq&logoColor=white&labelColor=555555)](https://qm.qq.com/q/y1J4gxkUTu)

<br/>

**[简体中文](README.md)** • **[English](README_EN.md)**

<br/>

[🌟 功能概览](#highlights) • [🚀 快速开始](#download) • [✨ 功能特性](#features) • [🎨 外观定制](#visuals) • [🌐 多语言](#i18n) • [🛠️ 本地构建](#build) • [📋 更新日志](CHANGELOG.md) • [🧪 Beta 测试](#beta)

[🤝 参与贡献](#contributing) • [⭐ Star 历史](#star-history) • [💬 社区交流](#community) • [❤️ 支持项目](#support) • [📌 项目状态](#project-status) • [🙏 特别鸣谢](#special-thanks)

</div>

---

<a id="intro"></a>

## 📖 简介

**StarPie (星盘)** 是一款专为 Windows 10 / 11 打造的轻量级鼠标轮盘手势（Radial / Pie Menu）效率工具。

> [!NOTE]
> **正式版：v1.7.4（2026-09-19）**。日常功能与下载说明以 [v1.7.4 Release](https://github.com/Star-Pie/StarPie/releases/tag/v1.7.4) 为准；[插件系统](#plugins) 和主题工作室等标注 **v1.8.0 Beta** 的内容仍处于测试阶段，不属于 v1.7.4 正式版。

在日常使用中可通过**鼠标右键 / 中键 / 侧键或键盘触发键**呼出快捷轮盘，也可使用独立的轨迹手势直接执行动作。v1.7.4 正式版支持 4 / 8 / 12 方位轮盘、多级子动作、专属程序配置（Per-App Profiles）、窗口管理与平铺、屏幕 OCR、快捷键录制、网址与命令调度，以及更完整的视觉形态定制，帮助将高频操作转化为自然的肌肉记忆。

> 💡 **设计重点**：
> - **低资源占用**：基于原生 C# WPF 构建，无浏览器内核打包，后台常驻内存以轻量化为目标，静默后台工作集目标为 **15 ～ 30 MB**（实际占用随环境与使用情况变化）；
> - **低延迟响应**：基于 Win32 `WH_MOUSE_LL` 底层事件流，响应迅速，不影响鼠标正常右键点击；
> - **绿色便携**：提供独立单文件版（内置 .NET 运行时，解压即用），配置保存于本地 `config.json`；

---

<a id="demo"></a>

## 🎬 演示视频

<div align="center">

[![Bilibili 演示视频](https://img.shields.io/badge/Bilibili-📺%20点击观看%20StarPie%20v1.6.8%20实机演示与重构解说-FB7299?style=for-the-badge&logo=bilibili&logoColor=white)](https://www.bilibili.com/video/BV1cubL6WEpB)

</div>

<details open>
<summary><b>🎬 演示视频 / Video Demo </b></summary>
<br/>

<div align="center">
  <a href="https://www.bilibili.com/video/BV1XjtA6KEGL" target="_blank">
    <img src="./attachments/video_cover.png" width="700" alt="StarPie 演示视频" />
  </a>
  <p>
    <a href="https://www.bilibili.com/video/BV1XjtA6KEGL"><b>📺 点击前往 Bilibili 观看原声讲解与实机演示</b></a>
  </p>
</div>
</details>

---

<a id="highlights"></a>

## 🌟 v1.7.4 正式版功能概览

StarPie **v1.7.4** 延续轮盘、轨迹手势、窗口管理与屏幕 OCR 等能力，重点带来 **纯原生极速搜索、专属方案隔离与多层继承优化、自定义交互音效、精简控制台、渲染性能优化和安装版**。

| 功能方向 | 当前能力 |
| :--- | :--- |
| **轮盘交互** | 4 / 8 / 12 扇区、中心核圆动作、多级子轮盘、蜂窝扇与外甩取消 |
| **轨迹手势** | 最多 3 段、8 方向组合、轨迹浮层、分段灵敏度与释放提示 |
| **动作与窗口** | 快捷键、程序、网址、文件夹、命令、系统控制，以及窗口切换、平铺、跨屏、置顶与透明度 |
| **屏幕 OCR** | Windows 本地离线识别、AI 视觉接口与自定义 HTTP OCR |
| **配置体验** | 双栏聚焦编辑、实时轮盘画布、扇区拖拽换位、运行窗口捕捉与紧凑全览列表 |
| **外观定制** | 多种轮盘形态、独立一二级主题、单扇区字体 / 图标 / 文字位置覆盖与屏幕边缘适配 |
| **原生极速搜索** | 不再依赖 Everything；应用预索引、分类过滤、搜索按钮与键盘操作 |
| **交互音效** | 高级模式中配置五类事件的合成音效或本地 WAV，支持方案导入 / 导出 |
| **安装与升级** | Setup 安装版、Standalone 独立便携版与 Lightweight 轻量版 |

> 📋 更完整的版本变化请查看 [CHANGELOG.md](CHANGELOG.md)。

---

<a id="features"></a>

## ✨ 功能特性

### 1. ⚡ 鼠标手势快速呼出与动作触发

- 按住鼠标右键滑动超过设定阈值即呼出轮盘，滑向目标扇区后松开按键即可触发对应动作（热键、打开程序、打开文件夹或系统功能）；
- 普通右键单击依然正常弹出原生右键菜单，互不冲突；
- 支持右键、中键、侧键、键盘单键与组合修饰键作为触发键，并可选择拖动越阈值或长按延时呼出；
- 录制快捷键时可临时独占并暂停全局热键，避免 `Win + D`、`Alt + Tab` 等系统组合被意外执行。

<div align="center">
  <img src="./attachments/第一张.gif" width="680" alt="鼠标手势快速呼出与动作触发演示" />
  <br/><br/>
  <img src="./attachments/按键组合触发录制.gif" width="680" alt="按键组合触发录制演示" />
</div>

---

### 2. 🌟 多级级联子轮盘

- **多级轮盘级联交互**：支持在任意扇区方位自由扩展 1~4 个二级子动作。光标划向扇区并在扇区内停留时，外环以弹性动画平滑展开二级子扇区，向外滑入即可极速触发。
- **一二级主题与配色完全独立定制**：支持单独调节各级尺寸、字号与图标排版；二级轮盘既可**一键同步主轮盘**，也可**完全独立定制专属风格与配色**；
- 支持外圈子环与蜂窝扇两种二级形态，加入迟滞保持和防抖判定，降低边界移动时的闪烁与误收起；
- 一级与二级动作均支持拖拽对调，并可选择拖动一级动作时是否同步交换其二级子动作。

<div align="center">
  <img src="./attachments/第三张.gif" width="680" alt="二级轮盘展示" />
  <br/><br/>
  <img src="./attachments/蜂窝扇.gif" width="680" alt="蜂窝扇二级轮盘展示" />
</div>

---
### 3. 🚀 顺势外甩脱离取消 (Outer Escape Cancel)

- 若划出手势后不想执行任何动作，无需反向拉回中心核圆；
- 只需顺势向外快速滑动脱离轮盘边缘，轮盘自动进入半透明安全取消状态，松开右键不触发任何动作；
- 支持在设置中开启/关闭，并可通过滑块微调外甩距离灵敏度（140px ~ 320px）；还可为外甩取消配置独立动作与常用预设，中心核圆取消仍可保持静默。

<div align="center">
  <img src="./attachments/外甩取消.gif" width="680" alt="顺势外甩脱离取消演示" />
</div>

---

<a id="visuals"></a>

### 4. 🎨 多种轮盘形态与风格预设
- **4 种几何形态**：经典紧凑扇区 (Original)、独立悬浮圆形 (Circle)、圆角胶囊 (Capsule)、蜂巢六边形 (HexagonHive)；
- **多套预设主题**：跟随系统、浅色模式、深色模式、液态毛玻璃、抹茶森林、冰川透蓝、莫兰迪柔灰；
- 右侧提供 **实时交互预览画布**，支持缩放、平移、复位、点击选中与拖拽换位，调节参数即时可见；同时提供屏幕边缘防溢出策略与 X / Y 安全边距。

<div align="center">
  <img src="./attachments/主题样式展示.gif" width="680" alt="轮盘形态与主题风格切换演示" />
  <br/><br/>
  <img src="./attachments/样式展示.gif" width="680" alt="多几何形态与视觉布局展示" />
</div>


---

### 5. 🎯 4 / 8 / 12 扇区方位自适应
- **4 键方位**：上下左右大角度，适合盲操；
- **8 键方位**：经典 8 向均衡布局（默认）；
- **12 键方位**：高密度功能映射，适合多动作工作流；中心核圆也可配置独立动作，并支持唤醒死区灵敏度。

<div align="center">
  <img src="./attachments/06_sector_counts.gif" width="680" alt="4/8/12扇区分割自适应演示" />
</div>

---

### 6. 💼 多程序专属配置方案

- 支持针对 Chrome、VS Code、Photoshop、SolidWorks 等不同前台程序分别设置专属轮盘配置；
- StarPie 会根据当前活动程序自动匹配对应方案，没有专属方案时回退到全局配置；
- 支持配置方案的新建、复制、删除与一键重命名，方便复用和维护不同工作流。
- **v1.7.4 继承规则**：新建专属方案默认关闭「继承全局方案未配置槽位」；按需开启后，第 N 层优先继承全局第 N 层，超出全局层数的部分回退到全局第 1 层。
- 中心核圆动作随当前活跃层同步，切层时同步更新外圈子轮盘与高亮状态。

<div align="center">
  <img src="./attachments/07_per_app_profiles.gif" width="680" alt="多程序专属方案演示" />
</div>

---

### 7. 🎛️ 动作配置工作区、应用快捷录入与拖拽编辑

- 动作页采用“配置方案 + 聚焦编辑卡片 + 实时轮盘画布”的双栏工作区，也保留适合快速查看多个动作的紧凑全览列表；
- 提供智能应用选择器，可汇总已安装程序，并支持名称搜索与快速过滤；
- 提供运行窗口捕捉器，可直接选择当前桌面上的窗口或进程，减少手动查找可执行文件路径；
- 可直接点击画布选择目标扇区，并拖拽交换一级扇区、二级动作和中心核圆的位置；
- 中心核圆可启用独立动作，支持死区松开触发、常用预设及独立文字和图标排版；
- 每个扇区可独立覆盖布局模式、字体、字号、文字颜色、图标大小、文字位置及 X / Y 偏移。

<div align="center">
   <img src="./attachments/程序拖拽配置界面.gif" width="680" alt="程序拖拽配置界面演示" />
    <br/><br/>
  <img src="./attachments/07_1.gif" width="680" alt="应用程序智能检索与动作配置" />
</div>
---

<a id="i18n"></a>

### 8. 🛡️ 场景隔离、全屏防误触与多语言

- **全屏与游戏检测**：运行全屏独占应用或游戏时自动放行原生右键；
- **修饰键穿透**：支持按住 Ctrl / Shift / Alt 时绕过轮盘；
- **黑名单支持**：支持将指定进程加入排除名单；
- **多语言热切换**：内置简体中文、繁体中文、English、日本語，切换即时生效；`ScreenHelper` 统一处理多显示器、混合 DPI 与屏幕边缘坐标，减少副屏唤起漂移和轮盘溢出。

<div align="center">
  <img src="./attachments/08_settings_and_i18n.gif" width="680" alt="防误触与多语言设置演示" />
  <br/><br/>
  <img src="./attachments/边缘呼出防溢出.gif" width="680" alt="边缘呼出防溢出配置" />
</div>

---

### 9. ➡️ 独立轨迹手势与可视化提示

- 可为轨迹手势单独指定右键、中键或侧键，与轮盘触发键并行使用；
- 支持最多 3 段、8 方向的轨迹组合，通过短段过滤和相邻同向合并减少快速绘制时的微小抖动误判；
- 绘制时显示透明轨迹浮层、起点和释放提示，可调节分段灵敏度及提示文字方位；
- 未达到拖动阈值的轻点会回放为原生鼠标点击，不影响日常操作。

<div align="center">
<img src="./attachments/按键组合触发录制.gif" width="680" alt="按键组合触发录制演示" />
</div>

---

### 10. 🧰 更完整的动作类型体系

| 动作分类 | 主要用途 |
| :--- | :--- |
| **快捷热键** | 录制或拼装组合键，支持主按键搜索、Pause / Break 与独占录入 |
| **启动程序** | 启动 EXE、快捷方式或文件，可携带参数并选择常规权限启动 |
| **打开网址** | 使用系统默认、Chrome、Edge、Firefox 或自定义浏览器打开网址 |
| **打开文件夹** | 打开本地路径及桌面、下载等系统虚拟目录 |
| **运行命令** | 支持 CMD、PowerShell、WSL 及隐藏终端模式 |
| **屏幕 OCR** | 框选屏幕区域并调用本地、AI 或自定义 HTTP 识别 |
| **窗口管理** | 切换窗口、平铺、跨屏移动、置顶与透明度控制 |
| **系统控制** | 锁屏、音量、媒体、任务视图、虚拟桌面等系统功能 |

动作执行与显示图标已经解耦，同一动作可独立选择内置矢量图标、程序图标或自定义图片，不再被动作类型限制。

---

<a id="beta"></a>

## 🧪 v1.8.0 Beta 测试

本节集中介绍仍在测试中的插件系统与主题工作室；这些内容不属于 v1.7.4 正式版。

<a id="plugins"></a>

### 🧩 插件系统

> [!IMPORTANT]
> 本节描述 **仍处于 v1.8.0 Beta 测试阶段**的插件系统，不属于 v1.7.4 正式版。正式版的内置动作在测试线中正逐步拆分为官方插件，升级 Beta 后可能需要安装对应模块。测试功能与 SDK 契约以所使用的 Beta 版本及 [插件文档](plugin/README.md) 为准。

StarPie 使用轻量的 **.NET 进程内 DLL 插件架构**。插件只需引用独立的
`StarPie.Plugin.Abstractions` 契约程序集，通过 `Initialize` 注册动作等贡献；主程序则为每个插件创建
宿主包装实例，统一管理扫描、加载、惰性激活、调用租约、异常隔离、异步停用和程序集卸载。

Beta 开发线的动作执行路径已具备从注册、配置、参数校验到 Sequential / Background 调度的完整基础闭环；
交互事件与轮盘结构路径也已建立可扩展入口，后续可以继续扩展而不复制整套插件生命周期代码。

> 📖 **插件开发入口**：开发文档、当前 API、架构说明、示例工程和历史资料统一收录在 [《StarPie 插件开发资源》](plugin/README.md)。
> 🔗 **官方插件仓库**：[StarPie-Official-Plugins](https://github.com/Star-Pie/StarPie-Official-Plugins)。主仓库中的 `plugin/StarPie-Official-Plugins/` 是该仓库的 Git 子模块检出目录；官方插件的源码、打包和发布流程均在这个独立仓库中维护，StarPie 主仓库不重复引用、构建或随发行包携带官方插件 DLL。

- **官方插件手动安装与一次性引导例外 (v1.8.0 Beta)**：
  - **发行包零预置**：StarPie 发行包不随包携带官方插件 DLL，启动与后台运行保持零自动网络请求。
  - **一次性引导知情同意例外**：为了提升新用户上手体验，v1.8.0 Beta 在用户首次交互式打开设置窗口时（非静默启动、非自检模式，且插件系统正常开启），会弹出一次性知情引导弹窗。弹窗展示 5 项核心官方插件（`folder`, `weburl`, `launch`, `system`, `shelltool`）、官方仓库来源及所需权限（`Process` 启动进程、`InputSimulation` 按键模拟）。
  - **零网络请求原则**：用户点击“一键安装”前，宿主绝对不发起任何网络请求（不拉取 catalog、不下载包）；用户点击“一键安装”后，仅下载安装缺失项；已安装项跳过（不覆盖、不升级、保留禁用状态）；若官方目录中包含超出预告范围的权限，暂停并请求用户确认。
  - **常驻重试入口**：若用户选择“暂不安装”或部分项下载失败，在「插件与扩展」页面顶部常驻提供一键重试/安装横幅，已成功项保留，随时可补全缺失项。
- **社区插件仍可本地安装**：把社区插件 `.dll` 放进**程序目录的 `plugin` 文件夹**后点「重新扫描」，在候选卡片上点安装；或在设置里点「手动安装社区插件 (.dll)」直接挑文件。
- **⚠️ 放进 `plugin\` 只会用那一枚 `.dll`**：插件包里的其它文件（图标、资源、依赖 dll）
  不会被一起复制过去。需要**整包安装**时请改用「手动安装社区插件 (.dll)」，选中插件包目录里带
  `plugin.json` 的那一枚 —— 识别到清单后宿主会整目录复制。
- **两个目录职责分开，互不干扰**：

  | 目录 | 角色 |
  | :--- | :--- |
  | `<程序目录>\plugin\` | **社区插件候选区**：仅用于手动安装社区 `.dll`；StarPie **只读不写**，不会在这里创建任何文件 |
  | `%LOCALAPPDATA%\StarPie\plugin-data\` | **可写数据目录**：安装副本、启用状态、插件私有数据都在这里，卸载时整目录清除 |

- **安装与启用分离**：装完默认是「已安装但未启用」，必须手动勾选才会加载 ——
  避免「放一份文件进去」等价于「放行它的代码」。安装前会展示 ID、版本、作者、目标框架、
  架构、SHA256、签名状态与声明的能力清单。
- **候选卡片会直接告诉你结论**：可安装 / 有新版本 / 版本更旧 / 已装同版本 / 内容已变 /
  ID 重复 / 无法识别。同一 ID 出现两份文件时两份都会被标成「ID 重复」且不给安装按钮。
- **失败自保护**：单个插件加载失败或连续触发异常会被隔离，不影响 StarPie 本体；
  连续两次启动异常会进入安全模式并临时禁用可疑插件。
- **给插件作者**：先阅读 [插件开发资源](plugin/README.md) 中的快速入门和当前源码入口。历史 `plugin/samples/` 模板不在当前 checkout，不能照旧路径构建；`FloatingBall` 当前源码位于官方插件子模块。调试时可用
  `StarPie.exe --plugin-selftest <插件.dll> [报告路径] --skip-invoke` 在临时沙箱里跑
  全链路自检（不会碰你已装好的插件），或用 `StarPie.exe --plugin-paths` 查看当前生效的目录。


<a id="art-style-beta"></a>

### 🎨 主题工作室

> [!IMPORTANT]
> 主题工作室属于 v1.8.0 Beta 开发线，不包含在 v1.7.4 正式版中；正式版的轮盘形态与配色见 [外观定制](#visuals)。

「外观样式 → 主题工作室」提供纸感极简、曜石科技、森林柔和、冰川玻璃、复古像素五套风格，可复制编辑配色、字体、圆角与轮盘材质，支持即时预览和主题文件导入／导出。

<div align="center">
  <img src="./assets/主题工作室.png" width="900" alt="StarPie 主题工作室与轮盘实时预览" />
</div>

 查看 [主题工作室说明](docs/art-style-themes.md) 或 [可导入示例](themes/)。旧配置和独立轮盘配色继续保留。

---

<a id="download"></a>

## 🚀 快速开始与下载

**当前正式版：[v1.7.4](https://github.com/Star-Pie/StarPie/releases/tag/v1.7.4)（2026-09-19）**。安装版和独立便携版内置 .NET 8；轻量版需要目标电脑已安装 .NET 8 Desktop Runtime。

| 版本包 | 适用场景 | 说明 | 下载入口 |
| :--- | :--- | :--- | :--- |
| **Setup 安装版** | 安装向导、卸载与版本升级 | 内置 .NET，安装时检测运行中的 StarPie | [下载 Setup](https://github.com/Star-Pie/StarPie/releases/download/v1.7.4/StarPie-v1.7.4-Setup-win-x64.exe) |
| **Standalone 独立便携版** | 无需安装，解压即用 | 内置 .NET 运行时 | [下载 Standalone](https://github.com/Star-Pie/StarPie/releases/download/v1.7.4/StarPie-v1.7.4-Standalone-win-x64.zip) |
| **Lightweight 轻量便携版** | 已安装 .NET 8 Desktop Runtime | 不携带 .NET 运行时 | [下载 Lightweight](https://github.com/Star-Pie/StarPie/releases/download/v1.7.4/StarPie-v1.7.4-Lightweight-win-x64.zip) |
| **测试版与历史版本** | 体验 Beta 或回溯旧版本 | v1.8.0 属于 Beta 测试线，请区分 Pre-release 与正式版 | [浏览 Releases](https://github.com/Star-Pie/StarPie/releases) |

同一发布页提供 `SHA256SUMS.txt`，可用于核验下载文件。升级 v1.7.4 后，建议检查专属方案继承、多层动作、中心核圆动作和自定义音效方案。

### 基础使用流程：
1. 下载并运行 `StarPie.exe`，程序会在系统托盘中后台运行；
2. 双击托盘图标打开设置窗口，在「触发设置」中录制轮盘触发键；
3. 按住触发键拖动超过阈值，或按配置长按，即可在光标附近唤出轮盘；
4. 滑至目标扇区后松开触发键执行动作，向外甩出则可取消或执行自定义取消动作；
5. 如需轨迹手势，可启用独立手势触发键，并在动作页映射 1～3 段方向组合。

---

<a id="build"></a>

## 🛠️ 本地构建与开发

以下命令检出 **v1.7.4 正式版标签**。如需参与 **v1.8.0 Beta** 开发，克隆默认 `main` 分支并参阅 [插件文档](plugin/README.md)；主线源码可能领先于正式发布。

### 环境要求

- Windows 10 / 11 (x64)
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Python 3.10+ (仅运行自动化测试套件需要)

### 编译与运行

```bash
# 1. 克隆代码仓库
git clone --branch v1.7.4 https://github.com/Star-Pie/StarPie.git
cd StarPie

# 2. 编译项目 (Release)
dotnet build WinPieGestures/WinPieGestures.csproj -c Release

# 3. 运行项目
dotnet run --project WinPieGestures/WinPieGestures.csproj

# 4. 发布轻量版（需要目标电脑安装 .NET 8 Desktop Runtime）
dotnet publish WinPieGestures/WinPieGestures.csproj -c Release -r win-x64 --no-self-contained -o releases/local/Lightweight

# 5. 发布独立版（自带 .NET 运行时）
dotnet publish WinPieGestures/WinPieGestures.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o releases/local/Standalone
```

---

<a id="project-status"></a>

## 📌 项目状态 (Project Status)

![StarPie 仓库活动统计](https://repobeats.axiom.co/api/embed/29a1f2c3c5afeb2863ba5e71e72828420d645c14.svg "Repobeats analytics image")

- **正式版**：[v1.7.4](https://github.com/Star-Pie/StarPie/releases/tag/v1.7.4)，发布于 2026-09-19。
- **测试线**：v1.8.0 Beta；插件系统仍处于测试，预发布资产与记录见 [Releases](https://github.com/Star-Pie/StarPie/releases) 和 [CHANGELOG.md](CHANGELOG.md)。

---

<a id="community"></a>

## 💬 社区交流 (Community)

<table>
  <thead>
    <tr>
      <th>QQ 公测群</th>
    </tr>
  </thead>
  <tbody>
    <tr>
      <td align="center">
        <img src="./assets/group.png" alt="StarPie 公测群 QQ 二维码，群号 1079199287" height="250" />
      </td>
    </tr>
  </tbody>
</table>

欢迎使用 QQ 扫描二维码加入 **StarPie 公测群**，群号：**1079199287**。

也欢迎在 GitHub 中与我们交流：

| 入口 | 用途 |
| --- | --- |
| [GitHub Discussions](https://github.com/Star-Pie/StarPie/discussions) | 交流使用经验、分享主题与工作流、讨论功能想法 |
| [GitHub Issues](https://github.com/Star-Pie/StarPie/issues) | 提交 Bug、兼容性问题和明确的功能需求 |
| [Pull Requests](https://github.com/Star-Pie/StarPie/pulls) | 提交或讨论代码、文档与翻译改进 |

请保持友善、尊重与耐心，尽量提供可复现的信息；不要公开密码、API Key 或含隐私的配置文件。

---

<a id="support"></a>

## ❤️ 支持项目 (Support)

如果 StarPie 对你有帮助，你可以通过以下方式支持项目：

- 给 [项目仓库](https://github.com/Star-Pie/StarPie) 一颗 Star，或分享给需要鼠标手势与效率工具的朋友。
- 在 [Discussions](https://github.com/Star-Pie/StarPie/discussions) 分享你的主题、使用场景和演示素材。
- 提交有复现步骤的反馈，或按照 [贡献指南](CONTRIBUTING.md) 帮助完善代码、翻译和文档。

---

<a id="contributing"></a>

## 🤝 参与贡献 (Contributing)

欢迎提交代码、文档与翻译改进，也欢迎提供主题、演示素材和兼容性反馈。开始前请阅读 [贡献指南](CONTRIBUTING.md)；插件开发请参阅 [插件开发文档](plugin/README.md)。

- **反馈问题**：先搜索 [已有 Issues](https://github.com/Star-Pie/StarPie/issues)，再附上 StarPie 版本、Windows 版本、复现步骤与必要的截图或日志。分享日志前请移除隐私信息。
- **讨论方案**：涉及交互行为、配置兼容或插件契约的改动，建议先在 Issue 或 [Discussions](https://github.com/Star-Pie/StarPie/discussions) 中沟通。
- **提交改进**：通过 [Pull Request](https://github.com/Star-Pie/StarPie/pulls) 说明改动目的、验证结果与尚未验证的部分；视觉效果和真实系统交互仍需实机验收。

感谢所有 [项目贡献者](https://github.com/Star-Pie/StarPie/graphs/contributors) 的参与！

<a id="special-thanks"></a>

### 🙏 贡献者致谢

> [!TIP]
> StarPie 的每一步改进都离不开开源社区。感谢每一位贡献代码、反馈问题、参与测试、完善翻译和分享主题的朋友，你们让这个项目变得更好。

<a href="https://github.com/Star-Pie/StarPie/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=Star-Pie/StarPie" alt="StarPie GitHub 贡献者头像墙" />
</a>

---

<a id="star-history"></a>

## ⭐ Star 历史 (Star History)

<a href="https://www.star-history.com/#Star-Pie/StarPie&Date">

  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/svg?repos=Star-Pie/StarPie&amp;type=Date&amp;theme=dark" />
    <source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/svg?repos=Star-Pie/StarPie&amp;type=Date" />
    <img alt="StarPie 的 GitHub Star 历史图表" src="https://api.star-history.com/svg?repos=Star-Pie/StarPie&amp;type=Date" width="100%" />
  </picture>
</a>
