# 配置与兼容性规范

## 基本原则

- 用户配置是长期资产。已有字段、动作类型、默认值和优先级构成兼容契约。
- 新字段应提供稳定默认值，并允许旧配置缺失该字段。
- 不允许无迁移地删除、重命名字段或改变既有值的语义。
- 读取应宽容历史别名，写回使用当前规范形态，但不得在普通浏览时破坏未知扩展数据。
- 数字使用 `InvariantCulture`；布尔 `false` 要显式写入，不能把未提供与 false 混为一谈。

## ActionItem 与插件动作

- 普通插件动作保存为 `ActionItem.Type = "Plugin"` 与 `PluginActionRef`，参数保存在 `ExtensionData`。
- 类型下拉刷新、语言切换和普通属性通知不得清空 `PluginActionRef` 或参数。
- `SelectedPluginActionFullId` 忽略 UI 绑定产生的空值写入；真正选择新动作时才替换引用。
- 官方类型认领的历史裸字段只通过 `ActionParameterProjection` 的白名单投影，不复制外观字段。
- 同一设置有多个来源时：动作参数 > 插件级设置 > 插件默认。

## 插件参数

- 宿主声明校验与插件自定义校验都通过 `PluginHost.ValidateActionParameters`。
- `Bool` 未填按 false 处理，取消勾选写入 false。
- 可选数值要区分“未提供”“已提供但非法”“已提供且合法”，不要用 0 充当未填哨兵。
- `ActionItemParameterTarget` 是封闭参数集，多余键应提示；`PluginSettingsParameterTarget` 是开放命名空间，不报告插件私有键。
- 参数默认值只在从未填写/键被清除时回落；不要在声明、UI 和插件代码复制三份默认值。

## 导入、导出与即时刷新

- `LockParentSectorOnSubmenu` 为可选布尔字段，缺失时按 `false` 保持原交互；显式 `false` 和 `true` 均须往返保留。父扇区锁定状态只属于当前手势，不持久化。

- `ConfigManager.ExportConfig` 必须包含 `CustomColorPresets`、主题、色彩微调和几何尺寸等全局状态。
- 导入成功后刷新主题预设、动作槽位和实时画布；防止 `_isUpdatingUi` 留在错误状态。
- 不要手工构造最小 JSON 当真实首装配置。`EnsureConfigHealth` 负责校验，不保证补全默认轮盘。
- 配置迁移要对旧版本、当前版本和重复执行做测试；迁移失败不得覆盖原文件。

## 初始化副作用

WPF 设置 `IsChecked` 也会触发事件。加载配置和自启动状态时使用 `_isUpdatingUi`、`_isUiInitializing`、`_isLoadingAutoStartState` 等现有守卫，并比较旧值。仅用户真实操作才允许创建/删除计划任务或写系统状态。

## 变更检查清单

1. 列出受影响字段、历史别名和默认值。
2. 明确旧配置读入、当前配置写出和未知字段保留策略。
3. 验证 round-trip，不只验证反序列化成功。
4. 检查 UI 类型切换、刷新、语言切换和插件失效场景是否保留数据。
5. 若需要破坏性迁移，必须单独设计备份、失败回滚和人工批准；普通功能任务不得顺带执行。

## 组件识别模式

- 新增全局布尔字段 EnableComponentIdentification，默认 false。旧配置缺失字段时按根窗口识别，保存和导出显式包含 false。
- 设置开关遵循现有 UI 初始化守卫，仅用户变更后自动保存；不迁移或改写已有程序绑定。开启后需在目标组件区域重新捕捉或手动绑定对应 EXE。
