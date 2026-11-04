# 1230动作面板配置对齐验证

## 结果与边界

用户已确认上一1155候选“唤出和使用没问题”；本次反馈为配置体验未对齐轮盘，不把该说明扩展成所有旧人工检查通过。新1230候选自动检查及产品独立只读复核通过，真实选择器、动作测试、主题／DPI仍待本次人工验收。

工作树、HEAD和允许路径见`action-panel-editor-parity.md`；其余在研改动与14份诊断保留。未提交／合并／发布，未改SDK、插件、输入热路径或配置格式。

## 改动

- Launch的路径字段沿用已解析认领注册的声明，提供现有ProgramPickerWindow、ExecutablePath模式WindowPickerWindow及程序文件浏览器；其余参数继续使用原声明表单。程序库／捕捉／浏览取消不写入；路径优先读取扩展值，编辑及清空同步旧裸字段，防止旧值回落；自定义名称、图标键、启动参数和其他扩展字段保留。按轮盘原行为更新继承应用图标路径，空或默认名称从选择结果填充。
- 类型旁增加测试触发，先克隆并异步校验，成功后调用原ActionExecutor.ExecuteForTesting队列。校验时单飞并禁用编辑，关闭后抑制迟到入队；不自动保存。已提交动作采用原队列语义，未增加撤回契约。快捷键增加原组合键拼装入口。
- 主／二级配置入口显式ModernButtonStyle、内容宽度左对齐。将现有设置窗口的TextBox样式提至App.xaml的ModernTextBoxStyle供两处复用；原9项setter及模板逐项XML比较一致，新增BasedOn链无静态循环。

## 候选

- EXE：`G:\Users\2 Better\.codex\worktrees\profile-trigger-wheels\design\WinPieGestures\bin\Release\build-out-20261104-1230\StarPie.exe`
- 宿主DLL：`5A3F5D4545CC9083E1C73CAACD586CDB56CFD5B48A1FB0F13B1BC233D3415F9B`
- SDK：`492E2752AD4D0B309CB2826FA889E7682B0BF1F99D3ADA36DE46F4301A5193DA`
- 小键盘DLL：`C190418C222E9CD25BEEDD63EC8BAD2EE82E42AA50B56CD6C48CCA45AE505428`（未改动、未重建）
- 证据：`H:\starpie-task-evidence\action-panel-parity-20261103`。before及baseline.json保存本轮前文件；最终源文件／交付清单另存哈希。

## 自动检查

| 项目 | 结果及证据 |
| --- | --- |
| Release | `host-final-build.log`：exit0，0 warnings/errors；SDK依赖单份，未夹带官方插件DLL |
| 候选安全测试 | `tests-project-isolation.log`：375项通过；实际加载宿主／SDK及最终插件哈希校验 |
| 参数与选择器 | 假picker覆盖三个按钮路由、取消、投影优先级、清空、名称／参数／未知字段保留、原对象不变；无真实选择器显示 |
| 异步测试 | 假校验／队列覆盖单飞、冻结、快照、无自动保存、关闭后的迟到结果、验证失败／异常；不执行真实动作 |
| UI | I18n.SetLanguage显式切换四语，实际有效Light/Dark；离屏真实控件模板／布局、窄窗口、主／二级内容宽度、共享TextBox样式检查；源码资源字典与候选匹配，非完整设置窗口实机验收 |
| 失败证明 | `probe-baseline.log`：1155缺测试入口、exit1；1230 `probe-fixed.log`、`probe-isolated.log`均通过，绑定各DLL哈希 |
| 既有输入回归 | `input-tests-final.log`：23/23，假贡献点与拦截器；实际加载1230与相同SDK |
| 安全自检 | 固定ControlSelfTestFixture，`--skip-invoke --test-instance`，`safe-selftest.log`通过；仅隔离夹具，不执行真实插件动作 |
| 样式抽取 | `style-extraction.log`：原9项setter／模板逐项一致 |

首次补充探针与主验证器同目录、同AssemblyName，共用obj导致增量编译误跑探针，日志保存`tests-project-collision.log`；已移至独立parity-probe子目录并在主项目排除其源码／obj，探针后连续主构建执行375项通过。未以错误入口结果冒充主测试。

输入日志夹具最初22/23缺净化日志，首次运行结束后日志已有两条，增加等待后仍失败；测试默认读文件句柄可能阻止异步logger追加。仅修正夹具以AppLogger.FileLock协调读取并等待两条日志，原所有隐私和错误断言保留，最终23/23。现有证据不证明共享竞争是唯一根因；不修改AppLogger，不把该现象宣称为本轮产品回归；保留`input-tests-first.log`、`input-tests-wait-only.log`。夹具修正另经独立只读复核，无新增阻塞。

## 协作与人工门禁

DeepSeek显式WorkBuddy/MAX在既有获授权聊天只评议给定材料，无工具／写入，reported_model unknown；其快照、关闭、取消、名称保留和内容宽度意见经主代理实际代码核对采用，不将泛化建议当已证实缺陷。主代理独占共享UI实施。独立原生GPT（gpt-6.1-terra/high，未参与实施）窄审增量、引用链和最终DLL哈希，无新增阻塞；未运行GUI或测试。

新入口`H:\starpie-manual-tests\action-panel-parity-1230\Start-ActionPanel-parity.cmd`，沿用1827隔离数据，不自动运行或安装。无需更换插件。复验范围见同目录《测试说明.md》。依据AGENTS.md第6节“人工未通过不得合并”，本次新增选择器、测试与样式须由用户确认后再进入后续交付。
