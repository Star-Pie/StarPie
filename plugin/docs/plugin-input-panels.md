# 输入面板服务（SDK 1.15）

输入面板是插件拥有的非激活窗口。宿主提供目标会话与队列，不替插件创建键盘，不在未安装插件时初始化窗口。现有 `IPluginContext` 与同步输入接口保持原签名；新插件检测可选的 `IInputPanelPluginContext.InputPanels`，清单声明 API 1.15。

## 能力和生命周期

`Open` 需要 Ui、InputSimulation，并在 UI 调度器上从轮盘动作提供的 `ActionContext` 建立会话。目标必须仍为前台、有效、属于原 PID，不能是宿主本身。会话按插件代际绑定，目标切换／销毁、Esc、暂停、替换或停用取消待处理请求。插件也须在 Shutdown 关闭自己的窗口。物理 Esc 按下与抬起成对处理；程序生成的 Esc 可继续发给目标应用。

`QueueText` 接收至多8192字符 Unicode 文本；`QueueHotkey` 只接受一组有效修饰键与一个主键，不接受序列、纯修饰或未知键名。宿主适配 OEM 标点和数字小键盘别名，通过既有动作队列执行。每会话最多64项待处理／执行中的请求；后台调用和超时后仍运行的任务继续占位，真实结束才释放。Accepted 表示入队，不表示输入或动作成功。

会话关闭使排队请求失效，并协作取消已开始的插件动作；插件必须遵守 CancellationToken。宿主不能撤销已经发出的输入，也不能强杀忽略取消的进程内插件。原 ActionContext 快照传入最终插件调用，开始贡献前再次验证目标。面板自身的按键／文本值及执行异常消息不写入宿主输入日志；其他插件应自行遵守其数据处理约定。

## 自定义动作

`ConfiguredPluginAction` 只含完整 ActionId 和参数快照。DescribeAction 只读已注册贡献点，不加载／启用插件；缺失、停用或尚未解析的引用返回不可用，但调用者应保留配置。用户打开自定义页后可异步调用 ResolveActionAsync，只加载该引用所属且已启用的插件以读取动作元数据，不执行动作。禁用偏好不改变。

编辑时关闭输入面板，再调用 EditActionAsync。宿主弹出统一动作选择与参数表单，只加载用户选中的已启用插件；取消返回 null，保存只返回绑定，不执行动作。QueueAction 需要额外 PluginActions 能力，并沿用目标会话、参数校验、能力与租约门禁。

GetAppearance 在 UI 线程读取当前宿主调色板；SDK不依赖WPF。插件应冻结自己创建的画刷，并自行处理非激活窗口、尺寸与DPI。

当前临时键盘候选的实现、验证与人工边界见[验收记录](../../docs/plans/temporary-keyboard-verification.md)。
