# SP-CASCADE-EDITOR-001 R3 定点返修任务单

用户已批准允许新轮次返修。由 Gemini 执行，Codex 完成后独立复核；本任务单由用户交给 Gemini，不表示已自动派发。

## 现场与授权边界

- 使用现有 `H:\starpie-worktrees\SP-CASCADE-EDITOR-001`，分支 `codex/cascade-action-editor`。
- 基准 `ee9277bde4a692454180da3a8cad05a0a483be49`；在当前 R2 未提交实现上修复，不回退、不覆盖现有改动。
- 沿用当前 Gemini 编码模型设置，接收任务时记录实际模型、起止时间、原累计耗时及轮次。
- 本次新授权追加最多 2 小时，限一次 R3 修复与验证；保留 R1/R2 与追加预算账本，不自动开启 R4。
- 允许修改当前动作模型、设置窗口相关 XAML/partial、I18n、子动作编辑与预设相关入口、`scratch/action-editing-tests/`、本任务验收文档。
- 禁止修改 SDK、插件运行时、ActionExecutor、GestureController、钩子、实际轮盘几何、配置字段契约、版本与发行文件；不加入 UI 框架。
- 禁止 commit、push、合并、Tag、打包、Release；不运行真实动作、系统命令、输入注入或未经用户明确启动的 GUI 测试。

## 1. 先固定红态

阅读 [R2 验收记录](cascade-action-editor-verification.md)。重新执行证据目录中的 `r2-boundaries/Boundary.csproj`，保存两项失败输出；原 18 项测试与独立 8 项反例已通过，必须保持。

证据目录：`G:\Users\2 Better\AppData\Local\Temp\codex-cascade-review-20261013\`。

将需要长期保留的边界输入与实际生产方法测试纳入 `scratch/action-editing-tests/`，不要仅依赖临时文件。原测试 15 将“复制全局叶片”当作“继承”的期待必须按批准的真实契约重新设计，覆盖全局再次修改及 JSON 重载；不得删掉行为约束来取得绿色。

## 2. 修复继承与拒绝操作（最高优先级）

- V1 不新增叶片级动态继承契约。移除伪单叶片恢复入口，或将其明确引导为“恢复整个扇区继承”，确认文字说明主动作和全部子动作覆写将清除。
- 不用 `IsInherited=true` 表示已经复制到本地的数据。该标记是运行时展示信息，不是持久化继承关系。
- 新增、复制、移动、粘贴先以当前生效数据做只读预检；验证索引、地址、目标、容量与中心规则全部通过后才物化和修改。
- 在物化前捕获原始本地状态；拒绝、同位置无变化、取消和异常不得产生本地覆写、保存或撤销记录。
- 审查所有列表/画布/剪贴板入口，特别是满容量继承菜单及中心 Ctrl 拖拽路径，不只修复当前两个测试用例。

## 3. 完成列表拖拽与复制反馈

- 为现有有序列表增加真实拖拽排序、明确插入线和提交落点，复用生产排序事务；上移/下移保留为键盘替代操作。
- 选择、拖动和提交分开：开始拖动不能因选择刷新而销毁拖拽来源。忽略按钮、菜单和文本录制控件的拖动。
- 列表、画布、父级导航同步选择；无效落点、Esc、失捕取消后顺序保持不变。
- 单动作/整包入口明确；整包替换已有子菜单时在修改前预告影响，可取消。中心不接收含子列表整包，不先修改后通知丢弃。
- 新增动态文本和菜单覆盖四语，复用主题资源，不重写大型 XAML 或轮盘渲染器。

## 4. 完成撤销边界

- 将名称、类型、快捷键、插件参数及相关外观编辑纳入一致的编辑事务。连续输入合理合并，不能撤销时覆盖此前已经保存的后续编辑。
- 对方案及层绑定真实会话身份和有效性，避免只凭相同名字/索引将旧记录应用到删除重建或重排后的新对象。配置替换时清理失效历史。
- 保留记录数量上限，并增加快照内容预算；仅保存受影响数据，不复制整份配置。预算策略及超限处理必须有确定性验证。
- 验证跨父级 Undo/Redo 两端恢复、继承首次编辑撤销、拒绝操作零历史以及普通参数修改可撤销。

## 5. 验证与交付

从工作树运行：

```powershell
dotnet build .\WinPieGestures\WinPieGestures.csproj -c Release --nologo
dotnet run --project .\scratch\action-editing-tests\ActionEditingTests.csproj -c Release
dotnet build .\scratch\plugin-controls-tests\Fixture\SelfTestFixture.csproj -c Release --nologo
dotnet run --project .\WinPieGestures\WinPieGestures.csproj -c Release --no-build -- --plugin-selftest .\scratch\plugin-controls-tests\Fixture\bin\Release\net8.0-windows\ControlSelfTestFixture.dll .\scratch\action-editing-tests\plugin-selftest-r3.txt --skip-invoke
git diff --check
git status --short
```

自检报告及构建日志放入被忽略的证据目录，不作为产品。行为测试须覆盖修复前失败或安全变异红态；如果更改了公开的内部测试接缝，更新独立探针以验证新契约，不仅改变期望值。

更新验收记录，提供：实际修改文件（含未跟踪文件）、候选文件指纹、所有命令/退出码/耗时、红绿证据、预算使用、未实现或未覆盖项。

## 完成与停止条件

两项边界问题消除，列表拖拽和撤销要求落实，原回归保持且新行为门禁通过，Codex 独立复核通过。用户已报告的 R2 实机验收保留；R3 需补验整扇区继承、满容量拒绝、列表插入排序和普通参数撤销。

到达 2 小时、出现范围外契约需求或仍有未解决问题时，交付当前 diff 与证据，明确标记 `incomplete_closed`，不得自动扩轮。最低有用交付为两项边界修复及有效的红绿证据；达到最低交付不等于整项完成。
