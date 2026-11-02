# StarPie 架构概览

## 产品目标

StarPie 将 CAD 类软件中的空间手势轮盘扩展到 Windows 桌面全局。架构优先级依次是：交互确定性、热路径延迟、后台资源占用、配置兼容性和可扩展性。

## 主要模块

| 模块 | 职责 |
| --- | --- |
| `App.xaml(.cs)` | 进程宿主、单例、钩子/托盘/设置窗口生命周期 |
| `TrayController.cs` | 进程级托盘、菜单、主题、UIPI、提示和退出 |
| `GestureController.cs` | 手势状态机、位移、极坐标与命中 |
| `MouseHook.cs` / `KeyboardHook.cs` | 低级全局输入钩子，只做轻量捕获 |
| `RadialWindow.xaml(.cs)` | 透明轮盘窗口、渲染与动画 |
| `SettingsWindow.xaml(.cs)` | 按需创建的设置控制台和实时预览 |
| `ActionExecutor.cs` | 动作排队、调度及内建快捷键执行入口 |
| `ConfigManager.cs` 与模型 | 配置读写、导入导出和兼容 |
| `WinPieGestures/Plugin/` | 插件宿主、运行时、安装、能力与参数 UI |
| `Renderers/` | 轮盘几何渲染策略 |

## 生命周期

- `App` 持有 `MouseHook`、`KeyboardHook`、`GestureController` 和 `TrayController`。
- 静默启动或开机自启不得构造完整设置窗口。
- 所有打开设置入口统一调用 `App.ShowSettingsWindow(int tabIndex = -1)`。
- 用户关闭设置窗口后先 `Hide()`；30 秒内复用，超时后才真正释放。最终只允许 `MemoryOptimizer.TrimMemory(force: false)`，不做日常 Full GC 或强制工作集剥离。
- 托盘与设置窗口解耦。关闭最后一个设置窗口不能结束进程，`ShutdownMode` 保持 `OnExplicitShutdown`。

## 分层与依赖方向

```text
Windows hooks / tray
        ↓
GestureController ──→ RadialWindow
        ↓
ActionExecutor ──→ Built-in Hotkey
        └────────→ PluginHost → PluginRuntime → typed path modules

SettingsWindow → view models / ConfigManager / PluginHost
Plugin implementation → StarPie.Plugin.Abstractions only
```

- UI 不直接承担插件加载、执行或状态机。
- `PluginHost` 是主程序与插件系统的唯一接缝。
- `PluginRuntime` 统一实例、激活、租约、停用与卸载；各路径模块拥有强类型请求和自身策略。
- 新的插件路径通过新的 `PluginPathModule` 接入，不复制运行时，也不退化成 `Invoke(string, object)`。
- 统一交互路径（SDK 1.10 候选）：语义会话 → PluginHost → Runtime/事件模块 → 每插件有界队列 → 代际/租约 → 观察贡献；广播不加载插件，旧同步订阅不改契约。API/图与模块归属见 [plugin-system.md §9](plugin-system.md#9-统一交互路径文档入口)。

## 资源与性能边界

- 后台、轮盘、完整控制台分别按根 `AGENTS.md` 的工作集目标评估。
- 高频几何、画刷和笔刷尽量复用并 `Freeze()`。
- 设置页、插件目录和网络行为不进入输入钩子或轮盘显示热路径。
- 可能超过百毫秒的插件动作使用 `ActionKind.Background`；`Sequential` 只给短动作。
- UI Dispatcher 只在有真实工作时投递，空闭包也可能延迟 ALC 回收。

## 事实来源

- 运行时版本：`AppVersionInfo`；版本同步点见 `docs/release.md`。
- 配置：`ConfigManager` 与模型类型；兼容规则见 `docs/config-compatibility.md`。
- 插件公共契约：`plugin/sdk/StarPie.Plugin.Abstractions/`。
- 插件现行运行时：`WinPieGestures/Plugin/` 与 `plugin/docs/plugin-system-architecture.md`。
- 版本历史：`CHANGELOG.md`，不要在架构文档重复维护里程碑。

## 改动判断

开始实现前回答：

1. 该能力属于宿主、插件 SDK、官方插件还是 UI？
2. 是否触及输入热路径、配置持久化或用户权限？
3. 是否已有单一事实来源，还是会制造第二份清单/状态？
4. 生命周期由谁拥有，停止与释放如何完成？
5. 哪个自动门禁能证明改动，哪个环节必须人工验证？

## 启动配置初始化与时序

- `ConfigManager` 的显式静态构造器负责首次加载；`App.InitializeConfiguration()` 只访问既有配置，依靠CLR保证一次初始化，不重复强制加载。显式 `LoadConfig()` 的重新读取与损坏恢复语义保持不变。
- `StartupDiagnostics` 仅记录启动阶段累计耗时，经既有异步日志队列写入；不采样进程、不轮询、不进入输入钩子。图标预载仍在钩子就绪前完成，插件预载偏好和设置窗口按需创建不变。
