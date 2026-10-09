# 无音频交互事件验证

从 StarPie 根目录运行：

```powershell
dotnet run --project .\scratch\interaction-event-tests\InteractionEventTests.csproj -c Release
```

这是控制台验证器，不创建 App/轮盘窗口、不播放音频、不执行真实动作或输入/窗口/硬件操作。插件根目录和 LOCALAPPDATA 指向独立的随机临时目录，证据路径会打印在结果末尾。

## 覆盖

- 真实 SDK、注册事务、PluginRuntime/PluginHost 路径，而不是复制一份队列模型。
- 只引用 SDK/BCL 的 Recorder 夹具通过真实扫描、Initialize、回调、停止和可回收 ALC 卸载。
- 事件类型索引只含匹配登记；稳定快照复用、实际投递项只携带匹配组、撤销重建和旧投递项门禁、订阅快照变化阻止选择合并。
- 每插件容量上限、同插件多贡献串行、跨插件异常隔离、同会话选择合并、跨会话/生命周期屏障。
- 暂存不可见、失败丢弃、捕获注册器关闭、个别 token 撤销、代际/实例替换、真实 Task 完成前租约不释放。
- 持有实际宿主安装 Gate 时发布仍能完成，不争用安装 IO 的互斥锁。
- 会话语义的不可变快照、快速松手、空动作、返回/取消/替代/结束及呈现前失败。

当前 156 项测试覆盖真实生产 Render 纯接缝：未运行回调不发 Presented，旧版本/关闭/处置/揭示失败不通知，重入失效不误报。撤销使用实际 ReserveDismissal/旧 Present/FinishDismissal，通过闸门强制交错验证冻结原因及单次发布。

这些测试不构造真实轮盘窗口，不证明显示器实际合成、DPI、多屏或手感。此前阶段的独立 Standards/Spec 结论不覆盖本轮索引改动；本轮独立复核仍待进行；真实视觉与手感仍须用户验收，不能只凭绿色测试宣称完整任务完成。
## 自检夹具纪律

现有 PluginSelfTest 的 [3g] 参数面板状态机要求 keypadLayer 贡献和必填 keyMap。Folder/Recorder 夹具不满足这项前置条件，不能用来判断完整自检是否全绿。

```powershell
dotnet build .\scratch\onboarding-download-tests\Fixture\Fixture.csproj -c Release --nologo
dotnet run --no-build --project .\WinPieGestures\WinPieGestures.csproj -c Release -- --plugin-selftest ".\scratch\onboarding-download-tests\Fixture\bin\Release\net8.0-windows\TestFixturePlugin.dll" ".\scratch\interaction-event-evidence\candidate-keypad-selftest.log" --skip-invoke
```

夹具只注册参数与空结果动作，--skip-invoke 仍必须保留。报告而非 WinExe 控制台输出是判读依据。

## 安全变异证据

`../interaction-event-evidence/mutation-*.log` 保存以下回归的红态：代际校验缺失、提前释放租约、跨屏障合并、容量限制失效、发布查找退回宿主 Gate、真实清理不 Discard、撤销未原子冻结原因、Render 版本守卫缺失、通知早于揭示、终结发布持有会话锁。每项执行后逐字节恢复生产文件，再构建/运行正常候选。仅编译失败不算有效红态。

验证器没有删除临时目录；用户如需清理，应按输出的具体临时路径单独操作，不使用工作区清理命令。
