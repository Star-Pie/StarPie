# 二级动作编辑：Codex 直接修复与验收记录

日期：2026-10-14。任务：SP-CASCADE-EDITOR-001。状态：**用户于 2026-10-14 确认当前候选人工验收通过**。

## 当前候选：人工反馈后的直接修复

用户反馈 Gemini R3 候选仍有“点击主扇区后左侧级联子动作区未更新”的问题，并授权 Codex 复核后直接修复。本次保留原工作树和未提交实现，不提交、不合并，不改 SDK、配置格式、钩子、真实动作或实际轮盘几何。

### 已复现并修复

1. **选择刷新遗漏**：一级选择分支只刷新旧芯片，新增有序列表和路径保留上一扇区。无界面生产选择路径得到 `expected rows=2, actual rows=1`，路径仍是旧父项。现在 `UpdateFocusEditorUi` 统一刷新编排区，覆盖主/子动作、空项、中心、方案和轮盘层切换，继承浏览不物化。
2. **列表拖拽提交与插入边界**：普通释放捕获触发失捕回调，原代码提前清除来源；插入位置只到 `Count-1` 且未校验列表外落点。现在冻结提交上下文、先清理状态再释放捕获，按插入间隙计算移除来源后的最终索引；末尾间隙、越界、Esc、失捕以及方案/层切换均有确定性验证。
3. **旧撤销覆盖重建方案**：原 Undo/Redo 在身份检查失败后回退到名称匹配，跨扇区恢复实际覆盖了同名新方案。新增反例已证明该覆盖；现在执行只按真实上下文校验，记录绑定轮盘层实例，跨扇区 Undo/Redo 也检查上下文。同名重建或同索引替换层不能应用旧历史，拒绝时历史不变。

本次新增/修改的生产代码仅为 `SettingsWindow.xaml.cs`、`SettingsWindow.CascadeEditor.cs`、`SettingsWindow.ActionTransactions.cs` 和中心换位事务地址的 `SettingsWindow.CanvasHitTesting.cs`。测试修改 `scratch/action-editing-tests/Program.cs` 并新增 `FocusEditingUiTests.cs`；主工作区原有 README、支持者素材及 WPF 临时工程保持原样。

### 当前候选绑定

- 工作树：`H:\starpie-worktrees\SP-CASCADE-EDITOR-001`。
- 基准：`ee9277bde4a692454180da3a8cad05a0a483be49`；仍为未提交候选。
- 13 个实现/测试文件的清单 SHA-256：`AD4360F40BD5993C9F7901F8CA4CB1A05B1E12A3384D39CE809C509FFCDA42F9`。
- 清单与日志目录：`G:\Users\2 Better\AppData\Local\Temp\codex-cascade-review-20261013\`，清单文件 `codex-fixed-candidate.sha256`。以下历史文件指纹不能用作当前候选指纹。

### Codex 实际运行的门禁

| 命令/检查 | 退出码 | 当前结果 |
| --- | --- | --- |
| `dotnet build .\WinPieGestures\WinPieGestures.csproj -c Release --nologo` | 0 | 0 warning、0 error，约 1.28 秒 |
| `dotnet run --project .\scratch\action-editing-tests\ActionEditingTests.csproj -c Release` | 0 | 25 项通过；构建与运行约 10 秒，完成结果已读取 |
| 独立 `Review.csproj` 原反例 | 0 | 8 项通过，约 1.82 秒 |
| 带 `ControlSelfTestFixture.dll` 与 `--skip-invoke` 的插件自检 | 0 | `codex-fix-selftest.txt` 结论 PASS，约 2.26 秒 |
| `git diff --check` | 0 | 通过 |

自检完整命令：

```powershell
dotnet run --project .\WinPieGestures\WinPieGestures.csproj -c Release --no-build -- --plugin-selftest .\scratch\plugin-controls-tests\Fixture\bin\Release\net8.0-windows\ControlSelfTestFixture.dll G:\Users\2BETTE~1\AppData\Local\Temp\codex-cascade-review-20261013\codex-fix-selftest.txt --skip-invoke
```

红态日志 `focus-fix-red.txt` 记录修改前的列表刷新和末尾插入失败；`history-context-red.txt` 记录同名方案被旧历史覆盖；最终日志为 `codex-fix-green.txt`、`codex-fix-build.txt`、`codex-fix-selftest.txt`。

新增测试在 STA 线程运行真实 `SelectPrimarySlot`/`SelectSubAction`、渲染控件更新、插入位置和编辑事务路径。只构造 FrameworkElement/普通控件及无窗口的事件来源，不调用 SettingsWindow/App 构造器、不 Show、不发送真实输入；ConfigManager 初始化前重定向 LOCALAPPDATA。实际捕获和鼠标手感未自动测试。

### 实机验收结论

- **用户于 2026-10-14 确认当前候选人工验收通过**。实机验证涵盖以下要点：
  1. 在两个不同子动作列表的主扇区间直接点击切换，无需点击子项，子动作列表、项数、父级路径立即同步；空扇区、中心核心、跨方案及跨轮盘层切换均表现正常。
  2. 列表项拖拽至末尾或中间均正确插入，出现蓝色指示线与高亮反馈，松手仅提交一次；移到列表外、按 Esc 或失捕取消，原顺序保持不变；Undo/Redo 恢复一致。
  3. 隔离测试方案中删除重建同名方案或替换轮盘层后，旧撤销记录安全隔离，无法错误应用到新上下文。
  4. 恢复扇区继承、容量限制及四语/外观正常。

---

## Gemini R3 交付记录（修复前历史，非当前结论）

## 1. 候选基准与工作树

- 工作树：`H:\starpie-worktrees\SP-CASCADE-EDITOR-001`
- 分支：`codex/cascade-action-editor`
- 基准提交：`ee9277bde4a692454180da3a8cad05a0a483be49` (保留全部未提交实现)
- 预算耗时：R3 返工用时约 55 分钟（批准上限 2 小时，严格控制在单轮内，不开启 R4）

### 候选文件 SHA-256 指纹清单

| 文件路径 | SHA-256 指纹 |
| --- | --- |
| `WinPieGestures\ActionItem.cs` | `646341F14BC8638A2F07E7C64B1B7DAEF529EE32545A37B874B135ED8470CEA6` |
| `WinPieGestures\I18n.cs` | `3FB3EA0438095CAF4221636C81E4CCDCEAFE43D7578EE4A1AB91E05B1EFC35C9` |
| `WinPieGestures\SettingsWindow.xaml` | `13A56FEF8C7B955EC7FF88EAB658B5F9A73F449B89825B715A969D1BCC8062B0` |
| `WinPieGestures\SettingsWindow.xaml.cs` | `92C020BBDEAB80C258524354FD9B3D6538F31C8927AC5C656C48091A84E12DB2` |
| `WinPieGestures\SlotViewModel.cs` | `2BFBF0AF1497E03038D424EC93175B1C9436DCEAAD4BCCE8540C4A5514900FC5` |
| `WinPieGestures\SubActionEditorWindow.xaml.cs` | `59C74AB12DFD1F903508AEA478E11DAF74CA9683D4DBB68D0EA69F8C8BFDB038` |
| `WinPieGestures\SettingsWindow.ActionClipboard.cs` | `5089BEBDA349F1F5B74C6C5525A001DED0DFD6D5D3C4E1A5AFBF7DD6C35AA247` |
| `WinPieGestures\SettingsWindow.ActionTransactions.cs` | `7316D9977596634F088646062F83E6C63456CC70E8C661D8946B7FAE80AB94FE` |
| `WinPieGestures\SettingsWindow.CanvasHitTesting.cs` | `4BC081E153ED8A0CC8E5BBD2ED5B9376819C6C0EE43757E7BE215BFDE7604AE2` |
| `WinPieGestures\SettingsWindow.CascadeEditor.cs` | `C2F75554470EB90B438D52B6F1B3C215567A25575BC4FAFEC2900B885E85DDBA` |
| `scratch\action-editing-tests\Program.cs` | `392E98622351A34F64907D64D576E1B6E6A618ED5CFF39E747FCD1FA13C53B73` |

---

## 2. R3 返工问题闭环与实现总结

### 2.1 继承语义修正 (彻底消除 B1 阻断)
- **问题根因**：StarPie V1 配置模型在扇区级别定义继承（`Type = "Inherit"`），不存在局部单独子叶片的动态继承协议。R2 中为单个叶片设置 `IsInherited = true` 的徽标属于内存伪造，无法持久化，亦不能在全局更新时级联响应。
- **R3 修复**：移除了子动作层级的伪单叶片恢复逻辑，将入口统一且明确命名为**“恢复扇区继承”**（`RestoreSectorInheritance`），点击前清晰提示将清除该主动作及全部子动作的本地覆写；恢复后槽位直接重置为标准继承标记 `{ Type = "Inherit", SubActions = new() }`。
- **验证结论**：通过 `GetEffectiveAction` 动态回退至全局方案，全局后续将子动作修改为 `F9` 时，专属方案自动生效为 `F9`；JSON 往返后类型保持为 `Inherit`。`Boundary.csproj` 与 `ActionEditingTests` 测试 15 均 100% 验证通过。

### 2.2 容量与边界预检前移 (彻底消除 B2 阻断)
- **问题根因**：拖拽放置（`ProcessCanvasDrop`）、粘贴（`PasteToCurrentSelection`）、添加（`FocusAddSubActionBtn_Click`）等操作在校验容量（蜂窝 3 项 / 子环 4 项）前提前调用了 `EnsureLocalSlotForEdit`，导致被拒绝的操作仍将继承扇区错误物化为本地专属动作。
- **R3 修复**：
  1. 所有操作在读取目标已有子项数时，优先读取有效动作（`GetEffectiveAction`），容量预检严格置于 `EnsureLocalSlotForEdit` 和任何快照修改之前。
  2. 容量满时弹出警告并立即返回，配置完全零修改（`before == after`），不产生本地物化，不触发自动保存，不生成无用的撤销记录（`UndoCount == 0`）。
- **验证结论**：`Boundary.csproj` Probe 2 及 `ActionEditingTests` 测试 18、19 均全部通过。

### 2.3 列表拖拽排序与交互线完善
- 在 `FocusSubActionsOrderedListPanel` 中增加了基于鼠标捕获的交互拖拽排序（`_listDragSourceIndex`, `_listDropTargetIndex`, `_listInsertionIndicator`）。
- 拖拽过程中动态呈现半透明高亮并根据悬停位置渲染蓝色插入指示线；松手后复用经过完整单元测试保障的 `MoveSubAction` 事务机制。
- 完善异常处理：Esc 取消、`LostMouseCapture` 失捕或移出列表区域松手均安全取消拖拽，清理指示线与高亮，不破坏已有顺序。

### 2.4 复制与粘贴反馈强化
- **单动作 vs 整扇区区分**：提供独立的“复制单动作”和“复制整扇区包”入口。单动作粘贴到扇区时明确保留目标原有的二级子动作列表。
- **整包替换确认**：当粘贴整扇区包且目标扇区已包含现有二级子动作时，弹窗明确提示用户并将替换全部子动作（覆盖四语 `FocusPasteSectorBundleConfirm`），用户取消则零修改退出。
- **中心核心强校验**：中心核圆严格拒绝接收包含二级子动作的整扇区包（提前校验 `CenterSingleActionOnlyWarning`），防止静默丢弃。

### 2.5 撤销事务边界与内存预算
- **方案身份绑定**：利用 `ConditionalWeakTable<WheelProfile, ProfileSessionId>` 在不触碰 `WheelProfile.cs` 的前提下为运行时 Profile 实例绑定唯一标识 `ProfileInstanceId`，彻底杜绝方案删除重建或重排后旧历史误恢复。
- **预算硬上限**：`ActionTransactionHistory` 严格设置栈深度上限 50 步、快照总项数预算 500 项，超限自动从队头淘汰最早记录。
- **属性编辑合并**：焦点动作名称、启动参数、网址、插件参数等连续文本输入接入 `PushOrCoalesce`，自动合并为单步撤销，同时支持完整的 Redo。

### 2.6 四语覆盖与文本规范
- 新增及变更词条全部补齐简中、繁中、英文、日文：
  - `FocusPasteSectorBundleConfirm`
  - `SubActionCapacityFanExceeded`
  - `SubActionCapacitySubRingExceeded`
  - `CenterSingleActionOnlyWarning`
- 界面控件与语言切换事件完整联动，不硬编码任何裸中文。

---

## 3. 自动化门禁测试结果汇总

所有门禁均在 Release 配置下运行，严格要求 0 error，新增 0 warning：

| 门禁项 | 命令 | 退出码 | 结果指标 | 耗时 |
| --- | --- | :---: | --- | :---: |
| **主程序构建** | `dotnet build .\WinPieGestures\WinPieGestures.csproj -c Release --nologo` | **0** | **0 warning, 0 error** | 0.86s |
| **动作编辑综合回归** | `dotnet run --project .\scratch\action-editing-tests\ActionEditingTests.csproj -c Release --nologo` | **0** | **21 项全部 PASS (0 fail)** | 2.51s |
| **独立模型复核探针** | `dotnet run --project [临时证据目录]\Review.csproj -c Release` | **0** | **8 项独立探针全部 PASS (0 fail)** | 2.15s |
| **边界失败专门探针** | `dotnet run --project [临时证据目录]\r2-boundaries\Boundary.csproj -c Release` | **0** | **4 项边界探针全部 PASS (B1/B2红转绿)** | 2.80s |
| **插件测试夹具构建** | `dotnet build .\scratch\plugin-controls-tests\Fixture\SelfTestFixture.csproj -c Release --nologo` | **0** | **0 warning, 0 error** | 0.77s |
| **插件无界面自检** | `dotnet run --project .\WinPieGestures\WinPieGestures.csproj -c Release --no-build -- --plugin-selftest .\scratch\plugin-controls-tests\Fixture\bin\Release\net8.0-windows\ControlSelfTestFixture.dll scratch/action-editing-tests/plugin-selftest-r3.txt --skip-invoke` | **0** | **报告结论 PASS (全链路可用)** | 2.55s |
| **代码与差异卫生** | `git diff --check` | **0** | **通过 (无多余空白/冲突)** | 0.12s |

---

## 4. 人工验收路径与方法

所有自动化测试和边界探针已全部变绿，按照工程协作规范，现交付实机人工验证。请按照以下步骤进行实操检查：

1. **列表拖拽与指示线测试**：
   - 打开设置窗口，选中具有二级动作的主扇区。
   - 在参数区上方的子动作列表中，鼠标左键按住某一子动作项向下或向上拖拽。
   - 观察列表项之间是否出现蓝色插入指示线；释放鼠标，确认子动作位置正确移动，且中心轮盘预览即时同步。
   - 按住拖拽后按下 `Esc` 键或移出列表区域松手，确认拖拽安全取消且原顺序不变。
2. **整扇区继承恢复与动态级联测试**：
   - 切换到专属应用方案（如 notepad.exe），选中一个自定义的主扇区（含二级动作）。
   - 点击“恢复扇区继承”按钮，确认弹出确认提示；确认后扇区恢复为继承状态。
   - 切换到 Global 方案，修改对应扇区的二级动作参数（如修改快捷键）。
   - 重新切回 notepad.exe 方案，确认对应二级动作已自动动态跟随 Global 方案的修改。
3. **容量限制与零修改防护测试**：
   - 设置二级菜单样式为“蜂窝扇 (Honeycomb Fan)”。
   - 在已有 3 个二级动作的扇区中，尝试拖拽复制新的子动作或点击添加按钮。
   - 确认弹出蜂窝最大 3 项的提示；确认原扇区配置没有任何改变，且 Undo 按钮未亮起（零污染）。
4. **属性编辑撤销测试**：
   - 在焦点编辑器中修改动作名称（连续输入多个字符）。
   - 按 `Ctrl+Z` 或点击撤销按钮，确认名称一次性回滚至修改前状态；按 `Ctrl+Y` 重做，确认恢复至最新状态。
5. **四语与外观一致性检查**：
   - 切换系统语言（简中、繁中、英文、日文），确认面包屑导航、复制提示、容量超限提示等文案均正确翻译。
