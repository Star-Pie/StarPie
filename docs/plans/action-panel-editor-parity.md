# 动作面板编辑体验与轮盘对齐

用户确认1155候选唤出和使用正常，但编辑器缺软件库／窗口捕捉／测试入口，主设置配置按钮整行灰色。该确认只覆盖用户明确说明的唤出和使用，不扩展成此前所有人工项目通过。

宿主工作树仍为profile-trigger-wheels/design，分支codex/restart-update-reliability、HEAD 773e8c8加既有未提交改动；保留其余改动及14份诊断。1155候选不覆盖，改后创建新时间戳Release目录。

允许：ActionPanelActionEditor、ActionPanelUi、SettingsWindow.ActionPanels、ContributedActionParameterPanel、App.xaml和SettingsWindow.xaml中对应共享样式、必要四语文案、action-panel-tests及本计划／验证材料。禁止：SDK、插件源、轮盘／输入状态机、存储格式、系统集成、日常数据与发布操作。所有共享UI由主代理修改。

实现：复用ProgramPickerWindow、ExecutablePath模式的WindowPickerWindow和现有程序文件过滤器；保留认领插件参数声明／表单作为参数事实来源，只对Launch的Parameter/File字段提供宿主路径选择UI。取消选择不写入；自定义名称不覆盖；路径写入保持投影一致。类型和测试按钮并列，测试异步校验私有快照，再经既有队列；校验时冻结编辑，关闭后不入队，重复点击不重复提交。主、二级配置入口使用ModernButtonStyle及内容宽度。提取现有TextBox样式供轮盘设置与独立编辑器共用，不更改原视觉语义。

DeepSeek显式WorkBuddy/MAX仅给定材料评议，不工具读写；主代理实施。候选冻结后独立原生GPT只读审查，最多两工作者，无递归。自动验证使用假的picker／校验／队列，不Show、不真实动作／输入／系统修改；覆盖路径入口、取消、投影优先级与清除、名称／扩展字段保留、快照与关闭／重复点击、共享样式及窄窗口。新增断言对1155基线或隔离变异形成失败证据。真实软件库、捕捉、测试动作、主题与DPI由用户新候选验收，未通过不合并。

验证期间补充范围：修复scratch/input-panel-tests/Program.cs的既有异步日志夹具等待，保留所有隐私断言；失败运行后日志已包含预期两条净化信息，证明此前检查读早。新探针移至action-panel-tests/parity-probe子目录，避免与主验证器共用obj中的同名程序集。产品代码和断言标准不因此变更，保留首次失败日志。
