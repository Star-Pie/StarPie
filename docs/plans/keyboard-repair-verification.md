# 小键盘闪现与可视化编辑器：0948候选记录

## 状态与绑定

1827已被用户以“闪一下就消失、无法使用”判为未通过。原产物、测试记录和隔离数据保留；本记录不把1827的自动通过当人工通过。0948完成本轮实现、自动测试和独立源码复核，等待重新实机验收。无本轮提交、合并、安装器或发布；五级轮盘尚未实施。

宿主工作树：`G:\Users\2 Better\.codex\worktrees\profile-trigger-wheels\design`，分支`codex/restart-update-reliability`，基准`773e8c8353ee6da1cb1ebbeb921043bdde4d5d99`加已存在的未提交beta.4改动。插件工作树：`H:\starpie-official-worktrees\temporary-keyboard`，分支`codex/temporary-keyboard`，基准`c1afceeded7697dba8dc1c5eef64f9138a7fe5ac`加已有新插件。相对1827不再改SDK接口和版本，配置格式不变。

| 产物 | 路径／SHA256 |
| --- | --- |
| 宿主EXE | `G:\Users\2 Better\.codex\worktrees\profile-trigger-wheels\design\WinPieGestures\bin\Release\build-out-20261104-0948\StarPie.exe` |
| 宿主DLL | 同目录`StarPie.dll`，`FBA67F4833449F4A52DE2540B2EE2796B7CF680F352B988C43C610465FE96423` |
| SDK | 同目录`StarPie.Plugin.Abstractions.dll`，`492E2752AD4D0B309CB2826FA889E7682B0BF1F99D3ADA36DE46F4301A5193DA`，与1827相同 |
| 冻结插件 | `H:\starpie-manual-tests\keyboard-repair-0948\Keyboard-preview\StarPie.Plugin.TemporaryKeyboard.dll`，`F8D51326969AC292716940838D94E7A0C5D4F4138803792129876DF6441D7186` |

时间戳目录采用香港时间。`candidate-final-manifest.json`记录EXE／SDK／插件／清单及源码指纹。宿主目录不含官方插件DLL，冻结插件目录不含SDK私有副本。

## 修复与交互

- 窗口先创建隐藏句柄，完成非激活样式和鼠标激活消息处理，再打开目标监控会话。输入执行仍校验原窗口、PID、前台、插件代次和暂停状态。
- 新目标事件守卫只忽略早于会话开始的通知；会话内的事件立即使队列失效，即使通知送达时已经回焦。原轮盘监控继续使用原无参数回调，新增重载只传递时间戳。新增关闭日志仅含枚举。
- 插件局部调度适配捕获回调异常，避免旧宿主Dispatcher吞异常后仍报动作成功；不改全局调度器语义。无效输入目标明确失败。
- 编辑器为“方案／已用动作—键盘画布—选中按键属性”，支持五模式预览、自定义3×4点击选择、双击绑定、拖拽交换／复制、撤销／重做、标签编辑和保存。预览不创建输入会话、不执行动作。拖拽带窗口、方案和修订版本戳，拒绝过期或跨窗口载荷。编辑历史有32项／4MiB产品内存上限。
- 空动作选择器显示缺少其他已安装启用插件的原因，并隐藏空选择框；不自动安装、启用或选中首个插件。只有用户选择插件后才读取动作。采用现有宿主样式及四语文案。

根因边界：用户机器的闪现原因尚未证实。旧日志证明某些Open失败被调度器吞掉，不能单凭该日志断言每次闪现均由旧事件造成。[微软文档说明WinEvent的进程外通知异步排队](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook)，因此短暂切走再切回、通知尚未送达时，已执行请求无法被后来的事件追溯撤销；同一毫秒的时间戳也不能证明严格先后。守卫的时钟重建属于工程实现与假后端验证，不宣称消除了所有Windows焦点竞态。

## 自动验证

证据：`H:\starpie-task-evidence\keyboard-repair-20261103`。下列均为退出码0的最终记录；早期编译失败、0931被否决的回焦保留方案及1824真实快捷键隔离事故均保留，不改写为成功。

| 检查 | 结果／记录 |
| --- | --- |
| 主程序Release | 显式`OutDir=...build-out-20261104-0948\\`，0警告0错误，`host-build-final.log` |
| 插件Release | `src/StarPie.Plugin.TemporaryKeyboard/TemporaryKeyboard.csproj`，0警告0错误，`plugin-build-r3.log`；冻结DLL哈希匹配 |
| 方案和存储 | 原模型20组，`models-storage.log`；保存、损坏恢复、并发和超限等纯测试，不执行动作 |
| 编辑状态 | DeepSeek实现；主代理独立构建／运行20组，`editing-main.log`，深克隆、交换／复制、历史容量和往返 |
| 局部调度结果 | 6项，`dispatch.log`；旧吞异常反例、失败／跳过／取消／调度故障，无窗口或输入 |
| 目标事件 | CodeArts编写的纯反射测试，先验0939；主代理重新构建并绑定0948与SDK哈希，10项，`target-main-final.log`。旧事件、epoch相等／之后、延迟回焦、幂等、32位环绕及49天uptime；队列为替身，不构造原生监控 |
| 输入会话回归 | 主代理先核对ActionExecutor与1827安全接缝源码哈希未变，再绑定0948重建／运行23组，`input-main-final.log`。输入和贡献点均为替身，日志与AppData隔离 |
| WPF构造／布局 | 冻结插件DLL，宿主SDK哈希相同；14个不显示窗口的布局／模板，五模式选择、交换／撤销／重做／清空、保存失败留窗／成功一次关闭，`ui-final-modes.log`。声明窗口尺寸测量，浅／深假调色板，未Show、未调用原生拖拽或真实输入 |
| 安全宿主自检 | 0948、固定ControlSelfTestFixture、`--skip-invoke --test-instance`，全部AppData／TEMP隔离，PASS，`safe-selftest-report.txt`。不以报告内“全链路”措辞当真实动作验收 |
| 本地预览静态扫描 | 0948＋冻结插件实际哈希，PE／清单通过，`preview-scan-final.log`；未安装／激活 |

与1827源码未变的更新重启等大范围结果仍属于旧候选历史，本轮没有冒充重新运行。离屏图片在`ui-images-actual-size`，仅供布局参考，不能证明实际主题、IME、DPI、不抢焦点或拖拽手感。

## 委派与独立复核

通过已获授权的既有聊天调用WorkBuddy `deepseek-v4.1-flash-sg`／MAX，只写纯编辑状态与其测试；主代理回收diff并独立运行20组。CodeArts `deepseek-v4.1-flash-1017`负责隔离的目标守卫测试，策略默认medium，API省略思考参数，实际档位未验证。两者实际底层身份均记`unknown`。最多两位工作者，无递归。

主代理负责焦点边界、WPF与整合。未实施的原生GPT／high独立审查发现并复核：回焦不应放过真实切换、初始自动选择会加载插件、绑定拒绝不能显示成功。0948最后只读复核确认空状态隐藏不影响正常分支，两DLL哈希匹配，未发现新的阻塞项；并保留上述异步边界。独立审查没有运行GUI或真实输入。

## 人工入口与未验证项

运行`H:\starpie-manual-tests\keyboard-repair-0948\Start-Keyboard-repair.cmd`，按同目录`测试说明.md`手动覆盖本地预览插件，再从托盘退出并使用新入口重启。启动器沿用1827隔离数据，未自动安装、迁移或覆盖用户配置。

待用户复验：实际呼出不闪退、连续输入与修饰键、Esc／切换应用取消、实际拖拽、保存重开、动作选择空状态与真实跨插件动作、IME、权限、常用主题及多屏DPI。项目`AGENTS.md`第6节“人工未通过不得合并”继续生效。新输入路径未获人工通过，不能合并；也不由自动测试宣称达到内存／延迟指标。
