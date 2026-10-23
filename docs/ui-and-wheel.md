# UI、轮盘与 DPI 规范

## 极坐标与命中

WPF 坐标系中：

```text
theta = atan2(deltaY, deltaX)
```

角度 0 指向正东，Y 轴向下。4/8/12 扇区按 `2π/N` 等分，既有索引与空间方向不能改变。

蜂窝二级菜单禁止按欧氏圆心距离选叶片，必须比较归一化后的角距离：

```text
target = argmin(abs(NormalizeAngle(mouseAngle - leafAngle)))
```

这样左右移动总是命中同侧叶片，不随半径和视觉偏移反转。

## 渲染与 DPI

- `RadialWindow` 和 `Renderers/` 是轮盘绘制入口；几何计算与视觉样式分离。
- 透明窗口、混合 DPI 和多显示器位置计算必须明确物理像素与 WPF DIP 的边界，不在同一表达式里混用。
- 高频画刷、笔刷、几何在不可变后 `Freeze()`；避免每帧分配集合、字符串或新资源。
- 高亮与光晕使用已有动画节奏，不能通过高频声音、同步 IO 或 Dispatcher 堆积来驱动。
- 第 0 扇区方向、扇区编号和用户既有配置是兼容契约。

## 设置控制台

- 复用现有 `DynamicResource`、控件样式、间距和字体层级。
- 简单/高级模式的差异是信息密度，不应改变配置语义。
- 实时画布需要能反映当前编辑对象；UI 初始化时使用 `_isUpdatingUi` 等守卫，防止控件回写配置。
- 新增 DataTemplate 文案优先绑定视图模型的本地化属性。`Name` 不能跨 DataTemplate 命名域充当可靠 i18n 护栏。
- 插件动作 UI 固定为一个顶层“插件动作”选项和一个按插件分组的子下拉；分组头不可选择。

### 关于页内置更新日志

- 根目录 `CHANGELOG.md` 是版本日志的唯一内容来源，由主程序工程嵌入为 `WinPieGestures.Changelog.md`；不再手写 XAML 版本卡片或维护对应的历史翻译词条。修改日志后，下次构建自动带入，不修改运行时版本号或 SDK。
- 只在首次进入关于页时读取与解析资源，保留文档版本顺序并省略空的 `Unreleased`；最新非空段落默认展开，历史段落正文按展开创建、折叠释放。正文以只读可复制的 Markdown 原文展示，不自动翻译，不执行 HTML 或加载外链图片；标题、说明与错误提示覆盖四语。
- 展示不依赖联网、安装目录或额外 Markdown/浏览器组件；资源缺失、空内容或无法读取时显示明确的本地提示，不回退到过期静态卡片。原始文本读取与导出均有大小上限。
- “查看完整 CHANGELOG”使用相同的内置文档，仅在用户点击时导出到随机命名、禁止覆盖的临时 `.md` 文件并调用默认应用；不向安装目录写入文件，也不优先使用可能遗留的外置日志。运行期间不会自动获得未来版本的日志。
- 离线验证：`dotnet run --project scratch/changelog-tests/ChangelogTests.csproj -c Release -- --candidate <本轮 StarPie.dll 的绝对路径>`，覆盖精确嵌入、无外置文件读取、解析边界、空/缺失内容、临时导出、正文懒加载与实际资源布局；不联网、不启动外部查看器或产品窗口。真实显示、复制及默认应用打开仍需人工验收。

### 关于页更新源

- 软件更新卡片直接放在“关于软件”顶部版本信息卡下方，不再放在系统设置页。关于页的检查按钮原地检查，不切换页面；更新偏好默认展开，首次检查之前即可选择来源、通道和下载加速，仍可手动折叠。
- `UpdateReleaseSource` 独立保存主程序发行版来源：`github`（默认，兼容旧配置）或 `atomgit`（`Star-Pie/StarPie` 国内镜像）。`UpdateProxySource` / `CustomProxyUrl` 的含义和插件下载行为不变；选择 AtomGit 时仅禁用关于页的 GitHub 加速下拉，保留原选择。
- 检查、包下载、历史回退和发行版网页跟随所选来源。切换来源只保存配置并清除旧候选、已下载待安装引用和上次检查时间，不立即联网；取消旧请求并使用修订号丢弃旧检查结果及进度回调，切回原来源也不复活旧结果。
- AtomGit 只访问公开 Release API，无客户端 Token，不回退 GitHub；缺少对应 ZIP 或 `SHA256SUMS.txt` 的未完整镜像版本不作为候选。附件仅使用 API 实际返回的 HTTPS 地址，不推测文件地址。镜像可落后，界面只能表述“所选源暂无更新”，不能据此断言官方最新版。
- AtomGit 下载必须读取对应文件的 SHA-256 并校验完整包；失败不进入安装就绪态。重定向仅允许 AtomGit/GitCode/OBS 的 HTTPS 主机，不跟随至 GitHub。每次下载使用独立临时路径，避免取消旧下载时删除新任务的文件。
- 选择 AtomGit 后的更新检查不额外启动 GitHub 贡献者名单同步。原有更新覆盖与重启过程不在本功能内重写。
- 离线护栏：`dotnet run --project scratch/update-source-tests/UpdateSourceTests.csproj -c Release -- --candidate <本轮候选 StarPie.dll 的绝对路径>`。仅使用模拟 HTTP、临时文件、配置往返和无 Window 的控件资源探针；不下载真实包，不执行更新、回退或重启。源切换、语言/主题/DPI 和真实镜像接口仍需人工验收。

### 二级菜单父扇区锁定

- 触发设置中的“进入二级菜单后锁定父级扇区”默认关闭，适用于按住鼠标或键盘触发键的普通轮盘；桌宠粘滞轮盘不在此开关范围内。
- 开启后，实际进入有效二级子项才锁定父扇区；“同时展开全部子环”仅控制显示。跨越一级角度边界保持父项，退回一级区域并越过防抖阈值后解锁。
- 移出可选子项、进入禁用或占位子项后松手不执行父动作。切层、结束、取消、暂停和新手势会清理会话状态。
- 实时预览沿用渲染尺度和同样的锁定规则。蜂窝扇几何在界面准备阶段冻结，输入热路径只读取快照，以角距离选择可见候选。

## 主题和可访问性

- `ObsidianDark`、`TitaniumGray` 下正文与控件文字对背景对比度至少 4.5:1。
- 标题和主要文本使用项目高亮前景资源，不写与主题绑定的固定灰色。
- 所有弹窗在 `<Window.Resources>` 内具备自包含样式，并在构造时调用：

```csharp
AppThemeManager.ApplyTheme(
    this,
    ConfigManager.CurrentConfig?.AppTheme ?? "System");
```

- 用户可见文案覆盖 `zh-CN`、`zh-TW`、`en-US`、`ja-JP`。新增 XAML/代码路径不得硬编码单一语言。

## 快捷键录制

`HotkeyRecorderBox` 必须禁用 WPF 默认焦点导航，确保 Tab 及系统组合键能够被录制：

```csharp
KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.None);
KeyboardNavigation.SetDirectionalNavigation(this, KeyboardNavigationMode.None);
KeyboardNavigation.SetControlTabNavigation(this, KeyboardNavigationMode.None);
```

在 `OnPreviewKeyDown` 中处理 `Key.Tab`、`Key.System` 和修饰键，不让焦点切走。

## UI 改动验证

1. Release 构建和相关静态护栏。
2. 以至少浅色/深色、简中/英文检查新增区域；涉及本地化时扩展到四语系。
3. 小窗、常用 DPI、多显示器或缩放相关场景。
4. 动态列表的空、单项、多项、长文本和失效引用状态。
5. 最终由用户进行视觉、手感与真实交互验收；AI 不自动运行会弹窗的 UI 套件。
