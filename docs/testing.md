# 测试、证据与验收规范

## 1. 验证层级

| 层 | 目的 | 默认执行者 |
| --- | --- | --- |
| 静态检查 | 范围、格式、已知调用禁区 | AI |
| 单元/确定性护栏 | 数据转换、不变量、纯逻辑 | AI |
| Release 构建 | 编译、引用、生成器 | AI |
| `--plugin-selftest` | 插件宿主端到端且无真实副作用 | AI |
| UIA/pywinauto | 设置 UI、语言、插件页 | 用户明确运行 |
| 实机 | 手感、DPI、钩子、系统动作、硬件 | 用户 |

验证必须绑定当前 diff 或候选提交。旧日志、执行者自报成功和“窗口看起来正常”都不是当前候选证据。

## 2. 普通命令

```powershell
dotnet build .\WinPieGestures\WinPieGestures.csproj -c Release --nologo
dotnet run --project .\WinPieGestures\WinPieGestures.csproj -c Release -- --plugin-selftest --skip-invoke
git diff --check
git status --short
```

按任务运行 `scratch/` 中的专用验证器。不要为了得到绿色结果删除断言、扩大 `NoWarn` 或忽略退出码。

## 3. Bug 验证

修复前先建立失败证据：

1. 写清输入、环境、预期和实际结果。
2. 找到最小触发路径，区分 UI 显示、数据持久化、执行调度和平台差异。
3. 优先写会在基准版本失败、修复后通过的确定性测试。
4. 如果只能人工复现，记录可重复步骤和观察点；不要伪造自动覆盖。
5. 修复后运行邻近回归，不只重跑新测试。

## 4. PluginSelfTest 纪律

`PluginSelfTest.cs` 的 `[0]`…`[7]` 及子段号是结构契约。修改前后比较集合：

```powershell
rg -o '\[[0-9][a-z]*\]' .\WinPieGestures\Plugin\PluginSelfTest.cs |
  Sort-Object -Unique
```

- 不整文件重写，不在冲突处理中整体取一侧。
- 新断言先证明能够失败，再接受其通过结果。
- 探针必须无副作用：空命令、空动词、空布局、无效坐标或空映射。
- 自检要测试运行时交出来的数据，不只重复测试声明本体。
- 构造器若会自动补默认值，空输入探针要绕开该补齐，保证真的测试缺失字段。

现有关键覆盖包括：能力与服务交叉门禁、安装确认 i18n、插件卡片与动作面板文案、插件设置页数据流、空参数双层校验、卸载租约和 ALC 回收。修改这些区域时先定位对应段落。

## 5. UI 回归套件

`tests/` 会弹 GUI，默认由用户运行：

```powershell
python -m pytest .\tests -v
```

| 文件 | 范围 |
| --- | --- |
| `tests/test_settings.py` | 设置窗口、滑块、页签、方案与快捷键录制 |
| `tests/test_plugins.py` | 插件页渲染和扫描目录语义 |
| `tests/test_i18n.py` | 切换语言后的 CJK 残留台账 |
| `tests/conftest.py` | 隔离 AppData、启动和候选程序定位 |

夹具纪律：

- 要改变启动前状态时自己调用共享 `launch_app()`；已经启动的 `app` fixture 无法倒序修改现场。
- 不复制候选 EXE 搜索逻辑，复用 `find_exe()` / `launch_app()`。
- 不手工写只有一个字段的假配置。先让程序生成默认配置，再修改目标字段。
- 插件和 i18n 测试需要固定网络现场，避免 catalog 返回时机改变观测集合。

## 6. i18n 台账

`tests/i18n_baseline.json` 是棘轮而非“零中文”断言：当前观测集合必须是基线的子集，只允许减少，新增即失败。

修完一批后由人工明确更新：

```powershell
$env:STARPIE_I18N_UPDATE_BASELINE='1'
python -m pytest .\tests\test_i18n.py -v
Remove-Item Env:\STARPIE_I18N_UPDATE_BASELINE
git diff -- .\tests\i18n_baseline.json
```

更新基线是标定，不是验收；命令以 skipped 结束不能算通过。必须审查基线 diff。

已知边界：

- 只覆盖实际渲染并进入 UIA 树的控件；折叠页和空列表模板可能不可见。
- `DataTemplate` 的命名域使静态 Name 扫描失效；应绑定本地化属性或提供 AutomationId。
- 插件列表为空时，插件卡片模板不会实例化，应由 `PluginSelfTest` 的专用段落补位。
- 版本/更新时间等动态文本和 OS 自带窗口按钮需要归一化或排除。
- About/更新日志正文不纳入普通设置 i18n 台账。

## 7. 证据格式

每次交付至少记录：

- 仓库和 worktree 的绝对路径。
- 基准提交或比较 ref。
- 实际 changed paths。
- 每个命令、退出码、运行时间和关键输出。
- 自动未覆盖的部分及原因。
- 人工验收清单与状态。

中高风险任务由独立执行者/模型复核 diff 与证据。复核者不能只读取实施者总结；要读取实际变更并重新运行关键门禁。

## 8. 完成判据

只有同时满足才可说“完成”：

1. 范围内实现存在且 diff 可解释。
2. 规定自动门禁在当前候选上通过。
3. 未引入新的警告、范围违规或未说明副作用。
4. 必需的独立复核通过。
5. 任务要求的人工门禁通过。

否则使用“实现完成、待人工验收”“自动门禁通过、独立复核未完成”或“未完成收口”等精确状态。

## 音效完整插件化候选（beta.7）

- `scratch/test_sound_forensics.csproj` 的292项断言针对生产音效插件和真实控制器语义接缝；宿主不得重引入播放/音效配置。
- `plugin/StarPie-Official-Plugins/tests/starpie.plugin.sound/Sound.Tests.csproj` 为SDK/BCL控制台行为测试，Mock后端，不执行真实试听动作、GUI或系统操作。
- `scratch/beta7-sound-probe` 保留旧红态断言，迁移后必须全绿；不把配置中合法静音误当资源失败，不修改真实播放时刻。
- Root配置的旧音效键经JsonExtensionData往返，插件只读迁入私有sound.json；覆盖显式false、未知方案字段、重复迁移、失败/取消不覆盖源数据。
- 根CI需检出官方子模块；先独立提交插件，再更新主仓库指针。源代码测试通过不等于官方包/catalog已经发布。

当前双仓库候选指纹、准确命令/退出码、独立复核与实机验收清单见[音效迁移验收记录](plans/beta7-sound-plugin-verification.md)。日志及编译产物保留在被忽略的 `scratch/beta7-verification/`，不作为产品或源码提交。
