# 配置与兼容性规范

## 链式轮盘

- `OpenWheel` 扇区通过可选的 `PortalTarget` 指向其他轮盘层。切换到此类型时先挂起该槽位的内联子菜单，原有动作参数和子树继续保存，切回普通动作后恢复。
- `PortalParentVisualMode`（默认 `ParentCore`、可选 `FadedWheel`、`CustomMarker`、`OutlineCircle`）、`PortalTrailShowBeam`、`PortalFadeOpacity` 与自定义标记字段（`PortalCustomShape`/`PortalCustomSizeDip`/`PortalCustomFillColor`/`PortalCustomBorderColor`/`PortalCustomOpacity`/`PortalCustomIconKey`/`PortalCustomImagePath`）均支持旧配置缺省回退与边界钳位；五级固定外圈由 `FixedRingProximity` 按光标就近计算，不再使用固定父级从属索引约束。
- 旧层缺省 `LayerId`。新建层或首次选为目标时分配标识。目标层被删除后保留原标识和名称用于断链提示。无标识的目标按索引解析。克隆方案时，同方案引用改指新方案的对应层。
- 单方案便携包可包含关联方案及其 SVG。主方案在首项，旧的单方案包继续可读。导入时为各方案分配独立名称，并重映射包内引用。取消或保存失败时回滚本次导入的资源和方案草稿。

## 配置显示与平行轮盘（在研）

- 配置友好名称与内部 ProcessName 身份分离，包括 Global；层名继续使用 WheelLayer.Name。运行切层提示和外观预览共用格式化入口。
- UseApplicationContext 缺失默认 true；false 为平行轮盘，仅在有效独立触发时接管全局精确路由，不成为 Global 触发的隐式应用配置。
- HasExplicitProcessBindings 缺失为 false，保留旧名称回退；编辑/移除绑定后为 true，空列表即未绑定。关闭情景保留原绑定以便恢复。显式 false 和克隆均须往返保留。
- 旧绑定字符串保留空格分隔兼容；新显式列表使用逗号/分号/竖线分隔，使包含空格的真实进程名不会拆成多个绑定。UI、普通匹配和路由编译共用归一化入口。
- ApplicationIconPath 只为设置页图标提供本地路径提示，失效时显示后备图标，不影响动作和触发，不在钩子中读取。
- `AppConfig.ProfileQuickSwitchDisplayMode` 为配置快捷列表三态显示方式（`IconAndText` 默认、`TextOnly`、`IconOnly`）；旧配置缺失或未知值均回退为 `IconAndText`，三种合法值在 JSON 与 `.starpie-config` 便携包往返保持。
- 平行键与 Global 默认、有效层键、应用专属键、黑名单覆盖的相同完整按键冲突。冲突/无键的导入方案保留数据但不注册平行路由；旧 Global 与应用层复用语义保持。
- 删除层须清除被删层的 `WheelLayer.ActivationTrigger`，删除第一层同时清除旧根 `WheelProfile.ActivationTrigger`，预先验证剩余路由，禁止隐藏旧键转移到新的第一层；删除后立即同步落盘并重新发布路由快照。
- `ConfigsFolder` 方案列表按实际 `.json` 文件名茎枚举；`DeleteSavedConfig`、`SwitchToConfig`、`RenameSavedConfig`、`ExportConfigToFile`、`GetConfigJsonForExport`、`SaveConfig` 与 `EnsureConfigsFolder` 均在 `ConfigsFolder` 目录边界内按精确名、忽略大小写名、去首尾空白名与清洗名安全解析目标文件并拒绝路径穿越。目标不存在或删除失败（含文件仍残留）时不得切走配置或返回成功；删除当前激活方案后选取后备方案须同时排除已删原始名与清洗名，并在不重建已删文件的前提下更新 `ActiveConfigProfileName`。
- 原 JSON 接口保留；版本化 .starpie-config ZIP 携带有界 JSON、清单及实际引用的自定义 SVG。校验、资源准备和配置提交分步进行，准备期间配置改变则取消，失败只回滚本次资源。不迁移插件或位图。

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
- `PinWheelAfterTrigger` 为全局可选布尔字段，默认 `false`；旧配置无需迁移，显式 `false` 和 `true` 均须落盘并往返保留。定格目标、点击版本与输入释放所有权属于会话，不写入配置，也不复用插件常驻轮盘的会话状态。
- `PinnedNavUpVkCode`、`PinnedNavDownVkCode`、`PinnedNavLeftVkCode`、`PinnedNavRightVkCode` 与 `PinnedNavConfirmVkCode` 为可选单键虚拟键码，默认依次对应方向键 ↑/↓/←/→ 与 Enter；缺失、非法、修饰键或重复冲突时由 `PinnedNavBindings.FromConfig` 回退至安全不冲突键码，并在 JSON 与 `.starpie-config` 便携包中往返保留。

- `ConfigManager.ExportConfig` 必须包含 `CustomColorPresets`、主题、色彩微调和几何尺寸等全局状态。
- 导入成功后刷新主题预设、动作槽位和实时画布；防止 `_isUpdatingUi` 留在错误状态。
- 不要手工构造最小 JSON 当真实首装配置。`EnsureConfigHealth` 负责校验，不保证补全默认轮盘。
- 首次生成和主动恢复默认配置时创建一个全局二级轮盘，包含 8 个一级扇区，每个一级扇区预配 3 个二级动作。已保存的应用方案、扇区数量、菜单深度和动作继续随原配置载入。
- 新默认配置开启 `Plugins.PreloadOnStartup`。旧配置缺失该字段时沿用旧默认值 `false`，已有值照常载入。损坏配置的恢复会话关闭预加载。
- 配置迁移要对旧版本、当前版本和重复执行做测试；迁移失败不得覆盖原文件。

## 自定义按键与宿主动作面板（beta.4候选）

- `AppConfig.ActionPanels` 为新增列表，旧配置缺失时为空；每个方案保留独立ID、名称、行列、可选快捷键和`ActionItem`槽位。方案未知JSON字段和动作扩展字段随克隆、编辑和保存保留。损坏的方案列表只读打开，不用默认空方案覆盖原数据。
- 小键盘纯按键方案使用插件私有`keyboard-key-schemes.json`；旧`keyboard-schemes.json`不自动读取、删除或转换。新的按键绑定仅允许全键盘模板中的KeyId，不承载插件动作。根、方案、按键三层未知JSON字段均保留；未知值按原始JSON保守比较，内部空白或属性顺序差异也视为编辑。
- 宿主提供显式导入旧插件动作方案入口，仅将用户选择的文件转换成内存中的`ActionItem`，保存后才写宿主配置；源文件保留。不删除既有公共SDK接口，本轮分离功能不新增SDK契约。

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

## 方案与分层专属触发键

- `WheelLayer.ActivationTrigger` 为可选 `TriggerConfig`；缺失或 null 不新增专属路由，Global 的各层也可设置。层与方案克隆均独立复制此字段。
- 旧 `WheelProfile.ActivationTrigger` 原样保留，非 Global 方案仍直达第一层；第一层的显式层触发键优先生效，旧字段保留但不额外占键。清除第一层按键时同时清除旧字段，避免旧键重新生效。Global 的旧方案字段仍不作为专属路由。
- 同一应用可绑定多个唯一命名的方案，专属按键优先按应用和完整修饰键匹配；普通默认触发只选择没有专属触发键的方案，再回退 Global。应用专属层路由优先，其次为匹配的旧应用默认/黑名单覆盖，再其次为 Global 层路由，最后为全局默认。旧全局/黑名单覆盖的最少修饰键语义保持不变。
- 相同完整按键在重叠应用绑定中的不同方案/层之间属于冲突；Global 与应用专属作用域可复用按键，同一 Global 内不同层不可重复。录制、绑定编辑/捕捉、重命名和复制检查冲突；复制冲突时须明确确认清除复制项的专属键，否则取消复制；复制整方案检查所有层，绑定编辑同样验证所有有效触发。手工导入的冲突不按列表顺序选中，数据保留但该专属路由不接管输入。
- 新专属键禁止纯修饰键和 Esc；CapsLock、字母、标点、功能键及鼠标组合可用。旧配置中的修饰键触发保持兼容。失败导入/切换恢复原配置时同步恢复路由快照。

## 长按延时零值

LongPressDelayMs 缺失时保留450ms默认值，显式0作为合法值往返保存；有限非负值由内部 EffectiveLongPressDelayMs 统一读取，负数或非有限值按450ms回退。该内部派生属性不写入JSON。设置回填使用初始化守卫，不因归一化显示而覆写原始配置；滑块0至1000ms，步进1ms。

## 启动偏好与便携数据

- 设置中的「启动时最小化到托盘」对应布尔字段 `StartInTray`，缺省为 `false`。开启后手动冷启动只显示托盘；再次正常启动时，现有实例仍会打开设置。`--minimized`、`--autostart`、`--silent` 等静默参数继续生效。该开关独立于 Windows 登录自启动。
- 退出 StarPie 后，在 `StarPie.exe` 旁创建空文件 `StarPie.portable`。下次启动使用同级 `Data` 保存配置、命名方案、图标、程序缓存、日志和插件宿主数据。设置页显示本次使用的数据根目录。取消便携模式时先退出，再移走标记；两份配置分别保留，切换时不会相互复制。
- 便携启动会检查数据根和已有的关键存储路径。路径不可用或缺少写权限时，启动器显示错误并退出。现有插件专用的 `portable.flag` 仍可单独使用；两个标记同时存在时，插件宿主使用 `Data\plugin-data`。第三方插件自行访问的外部文件与系统临时目录另按各自用途处理。

### 启动程序的显示器偏好

`ActionItem.LaunchOnCursorMonitor` 为可选布尔值，缺省和显式 `false` 均使用原来的启动与窗口切换行为。它属于单个动作，随方案、图层和子动作的克隆保留。旧式 `Launch` 与官方 `starpie.builtin.launch.launch` 贡献使用相同字段，其他贡献不消费它。显示器名称属于单次触发的临时状态，不写入配置。

## 每个轮盘层的菜单深度

`WheelLayer.MaxMenuDepth` 缺失时按 2 级读取，主轮盘为第一级。运行时将深度限制在 2–5，原始数值保留至用户主动选择新值。关闭 `EnableMultiTier` 只显示主轮盘。各层的深度独立，复制层和方案时同时复制深度及整棵 `SubActions`。降低深度、切换样式和浏览分支均保留隐藏动作及插件参数。

应用专属方案沿用全局同向扇区继承规则，优先使用全局同序号的层，超出时回到其第一层。进入有效父项后，只从该父项向下解析。显式禁用的祖先会阻断整条路径。

应用专属方案在画布中清空子菜单的最后一个有效动作时，空快捷键根可保存 `OverrideGlobalMenu: true`，使该方向保留本地编辑。旧配置缺省为 false，沿用原继承规则。

### 固定整圈配置

固定布局按轮盘层保存于可选的 `WheelLayer.FixedRings`。旧配置缺省使用原布局。首次从原布局启用时，从第三级开始将已有后代复制到固定槽位，原来的动作树保留。超过单圈上限的旧动作仍可在原布局访问。暂停固定布局不会删除两套配置。 在当前层明确将蜂窝扇的菜单级数调至三级或更深，或在已有三级以上的当前层选择蜂窝扇时，首次建立固定槽位并默认逐级唤醒。单纯读取旧配置或切换到已暂停的层时保留原布局。

`Tiers[0]` 对应第三级。每圈的 `Slots` 按绝对编号索引，空位为 `null`。三级至五级使用槽位位置确定展开与选择。`ParentIndex` 作为旧配置元数据保留，可随配置读写和复制。一级与二级继续读取原 `Actions` 与 `SubActions`。`Count` 只控制可见范围。缩减、暂停和复制配置仍保留隐藏槽的动作、插件参数和图标。导出便携包时，也会收集并在必要时重映射这些槽位的自定义图标。

二级的展开距离读取全局 `SubWheelTriggerDistance`。固定布局的三级至五级以光标到实际轮廓的距离判断展开，留出少量收起余量以平滑边缘移动。旧预览配置中的 `WakeBandWidth`、`DwellMs` 随配置保留。

## Studio 方案字段与交换

方案的可选字符串列表 `Tags` 用于工作台筛选。旧方案缺少该字段时按空列表处理。复制和单方案导入导出均保留标签。单方案包与完整配置包各自校验导入范围；单方案导入会检查进程与独立触发键冲突。

`StudioRailFraction`、`StudioInspectorFraction` 记录用户拖动分隔线后两侧栏在可用宽度中的比例。缺省或零值使用当前窗口的默认布局。


## 分层几何外观

新字段 `SubWheelShape`、`FixedOuterRingAppearance` 和 `FixedHoneycombAppearance` 均可缺省。二级形状缺省时沿用旧版一级轮廓，首次从新侧栏更换一级轮廓时保留旧的二级轮廓。主动选择「跟随一级」可恢复联动。外圈子环缺省从原轮盘半径、缝隙与倒角派生三级至五级值。三级至五级每层的厚度与同层间距覆盖为可空值，固定蜂窝的公共宽度、间距、轮廓、圆角和中心半径各自缺省继承旧尺寸。新尺寸使用 WPF DIP。现有动作、层级和槽位编号继续通过轮盘配置管理。
