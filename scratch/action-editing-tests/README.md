# 二级动作编辑无界面回归

```powershell
$candidateDir = (Resolve-Path './WinPieGestures/bin/Release/build-out-yyyyMMdd-HHmm').Path  # 换成本轮候选目录
dotnet run --project .\scratch\action-editing-tests\ActionEditingTests.csproj -c Release "-p:StarPieCandidateDir=$candidateDir" -- --test-instance
```

42 个场景包含模型/插件快照、复制粘贴、继承、历史、真实选择刷新以及列表插入和取消。测试以 STA 线程、`--test-instance` 和本次运行独立的临时数据目录执行。指定 `-p:StarPieCandidateDir=<候选目录>` 时会核对实际加载的 StarPie.dll 与候选的 SHA-256。

涉及设置 UI 的用例调用真实设置窗口构造函数，在加载了应用资源的离屏窗口上运行。夹具通过方案下拉、层下拉和生产选择入口切换编辑对象，每项用例结束时释放订阅与定时器。事务模型探针继续独立验证模型。运行期间不显示窗口，不注入输入或执行真实动作。
已证明修改前失败的反例：切换主扇区保留旧列表、最后插入间隙无法表示、跨扇区旧撤销覆盖同名重建方案。当前候选通过这些检查；捕获设备、真实鼠标拖动、视觉/DPI/主题仍需用户实机验收。

最新候选、红绿证据与人工清单见 [验收记录](../../docs/cascade-action-editor-verification.md)。

v2.0.0 追加的 17 项覆盖继承子动作写入、中心恢复/复制/拖拽、快捷键页域、取消零修改、真实插件参数 resolver、四语和本地化绑定刷新。新增反例实际先失败后修复；自动保存失败状态另经安全变异验证。最终候选指纹和自动矩阵见 [收口记录](../../docs/plans/v2.0.0-closeout.md)，原有历史证据不替代此次整合验证。
