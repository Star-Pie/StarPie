# StarPie SP-PR178-FOCUS-001 测试套件说明

## 1. 概述
本工程位于 `scratch/test_wheel_focus`，作为 `SP-PR178-FOCUS-001` (R2/R3) 的端到端自动化测试验证套件。
测试通过 Mock 接缝隔离所有真实系统副作用，不产生 `SendInput`、按键释放、蜂鸣声、窗口激活、焦点抢占或持久化配置写入。

## 2. 运行环境与安全守卫
- **进程启动守卫**：必须带 `--test-instance` 参数运行；若缺失该参数，程序直接以退出码 1 拒绝执行，防止意外在生产上下文启动。
- **环境隔离**：在引用任何 StarPie 模块或 `ConfigManager` / `AppLogger` 静态构造之前，将 `LOCALAPPDATA` 环境变量重定向至独立的隔离临时目录（`%TEMP%\StarPie_Focus_Test_<GUID>`），杜绝污染生产配置和自启动注册项。
- **自启动禁用**：`--test-instance` 参数确保 StarPie 初始化时跳过任何系统自启动或计划任务操作。
- **无副作用接缝**：
  - `ReleaseStuckModifiersSeam`: 拦截按键释放并记录计数，避免穿透到底层 `SendInput`。
  - `ActionExecutionOverride`: 统一拦截最终动作分发，记录执行目标与快照。
  - `ForegroundHwndGetterOverride` / `WheelFocusSwitcher.ForegroundWindowGetterOverride`: 模拟前台窗口句柄。
  - `WheelFocusSwitcher.WindowFinderOverride` / `ProcessIdGetterOverride`: 模拟命中测试与实时进程 PID。
  - `App.ApplyPauseStateTransition`: 生产与测试共用的全局暂停状态转换入口，严禁测试自行补调用 `InvalidatePendingExecutionSession`。

## 3. 运行方法

### 3.1 编译
```pwsh
dotnet build .\scratch\test_wheel_focus\test_wheel_focus.csproj -c Release --no-restore --nologo
```

### 3.2 执行全量测试
```pwsh
dotnet run --project .\scratch\test_wheel_focus\test_wheel_focus.csproj -c Release --no-build -- --test-instance
```

## 4. 测试用例矩阵 (22 场景)
1. **Scenario 1**: 正常鼠标悬停触发与精确动作派发（B 窗口获得焦点，B 动作执行）。
2. **Scenario 2**: 快速松手与 UI 排队竞态保护（松手过快时，动作执行前确认会话有效与目标一致）。
3. **Scenario 3**: 激活失败安全回退（无法激活目标 B 时丢弃动作，绝不派发给旧前台 A）。
4. **Scenario 4**: 手势过程中目标窗口销毁（HWND 变为无效，取消执行）。
5. **Scenario 5**: 旧 `ShowRadialUI` 回调在新会话开始后到达（版本过期，拒绝抢焦点或改配置）。
6. **Scenario 6**: 动作入队后、消费前发生取消/Dispose/新会话/暂停与恢复（包括生产暂停转换 6d：暂停前排队旧动作永久作废，恢复后新建合法动作正常执行 1 次）。
7. **Scenario 7**: 初始前台目标在执行前切走（消费前核验真实前台，焦点离开安全取消）。
8. **Scenario 8**: 队列消费前 PID 改变（目标进程退出或重用，安全取消）。
9. **Scenario 9**: 入队动作快照不可变性（`Clone()` 保护，入队后原对象被修改不影响执行）。
10. **Scenario 10**: 鼠标缺少修饰键拒绝时不污染键盘触发（键盘触发保持干净前台上下文）。
11. **Scenario 11**: 窗口 B 中键轮盘与绘图手势冲突（一次解析快照贯穿，命中 B 配置接管手势）。
12. **Scenario 12**: 传统无上下文动作调用保持兼容正常执行。
13. **Scenario 13**: 孤立鼠标释放不创建 `_pendingExecutionSession`。
14. **Scenario 14**: 隔离规则根据目标快照与修饰键严格校验。
15. **Scenario 15**: 非鼠标触发保持原有行为无副作用。
16. **Scenario 16**: 受控 Mock Shell 激活兜底。
17. **Scenario 17**: 保守激活安全性（禁止 Alt 伪按键注入或 Topmost 篡改）。
18. **Scenario 18**: `ReleaseStuckModifiers` 测试接缝覆盖率验证（证明无真实输入穿透）。

## 5. 变异测试检验 (Red -> Green)
- **变异点 1 (R2 快照不可变性)**：在 `WinPieGestures/ActionExecutor.cs` 中的 `EnqueueAction`，若移除 `action.Clone()` 直接入队原对象。
  - **变异结果 (RED)**：Scenario 9 立即报出断言失败：
    `Assertion failed: Action name must remain 'OriginalAction', but was 'MUTATED_ACTION'`。
  - **恢复后 (GREEN)**：18 项全部通过。
- **变异点 2 (R3 暂停状态机失效关联)**：在 `WinPieGestures/App.xaml.cs` 中的 `ApplyPauseStateTransition`，若断开 `gestureController?.OnHostPaused()` 调用。
  - **变异结果 (RED)**：Scenario 6d 立即报出断言失败：
    `Assertion failed: ProcessActionEnvelope must return false for old action enqueued before pause`。
  - **恢复后 (GREEN)**：18 项全部通过。证明暂停时旧待执行会话被永久失效，恢复后旧动作绝不复活，新合法动作正常执行。

## 窗口进程识别一致性补充

新增 Scenario 19–22，直接调用生产捕捉器的静态进程读取入口与运行时解析器，不构造捕捉窗口：

- 校验 PID、标准化进程名、可执行路径及键盘前台读取的一致性。
- 模拟空句柄、PID 缺失、映像路径不可读及查询异常，失败结果不得成为可绑定的占位进程。
- 模拟 `wps.exe` / `et.exe` 两个组件窗口，校验捕捉绑定和运行时命中同一专属方案，不归并父进程。
- 校验 150ms 缓存同时保留路径和进程名，失败缓存到期后恢复，重置接缝清除旧快照。

这些断言不修改或验证真实 WPS 的窗口层级、取窗筛选及前台回退策略；相关流程保持现状，仍需实机验收。
