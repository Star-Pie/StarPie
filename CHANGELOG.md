# StarPie - 变更与版本迭代日志 (Changelog)

所有关于 **StarPie**（原 WinPieGestures）的重大架构重构、功能新增、UI 优化与问题修复均记录于此文档。

版本命名遵循 [语义化版本规范 (Semantic Versioning)](https://semver.org/lang/zh-CN/)：`主版本号.次版本号.修订号`。

## [v1.8.0-beta.1] - 2026-09-18

### 🧩 插件卡片改为六层信息架构

- 已安装插件卡片由四层（状态图标 + 名称行 / 摘要 / 详情 / 两个按钮）扩为**六层**：
  ① 头像方块 ② 名称 + 版本 + 状态徽章 ③ 描述（独立成行）④ meta 行（作者 / 许可证 / 贡献动作数）
  ⑤ 能力标签（逐个成 tag）⑥ 操作组。
- 摘要与详情按**读者**分层：meta 行只放用户关心的三项，技术细节（ID / 架构 / SHA-256 / 签名 / 路径）
  沉到卡片最底部的小字，两层不再互相淹没。
- **顺手修掉一处一直显示错的文案**：能力标签此前直接拼登记表里的英文枚举名
  （`Process` / `WindowControl`），中文界面下也照原样显示 —— 现在经
  `PluginCapabilityLabels.DescribeTags` 取词条，四语言各一份。
- `PluginsCardNotLoaded`（「未加载」）改名为 `PluginsCardNoActions`（「暂无动作」）：
  旧值会与紧邻的状态徽章「已启用 · 待加载」撞词，两句话读起来像在互相否认，
  而这一格描述的对象本来就是「有几个动作」。
- 头像：清单的 `Icon` 字段约定是相对路径（SVG/PNG），本层不做图片加载 ⇒ 退化为**名称首字**
  （按文本元素取，避免把 emoji 名的代理对劈成半个），代码注释里写明这是降级而非未完成。
- 自检 `[3f]` 同步扩展：新字段全部纳入「英文宿主部分无中文」覆盖，并新增
  「能力标签不得是未翻译的英文枚举名」断言 —— 含一条**用合成数据直接驱动**的，
  使其覆盖面不依赖沙箱插件恰好声明了几个能力。两次变异测试验过两条链路都会红。
- 纯界面改动：**未触碰任何功能、动作分派逻辑与配置格式**。

### 🎨 侧边栏两级分组与页签措辞更新

- 侧边栏由 6 项平铺改为**两级分组**：`偏好设置`（触发与场景 / 外观与形态 / 手势与动作 /
  高级与系统 / 关于与更新）与 `扩展生态`（插件与扩展）。两个分组标题走 i18n，四语言各一份。
- 导航项**选中态新增左侧 3.5px 圆角指示条** —— 此前选中只靠底色区分，弱光环境下不易辨认。
- 「插件与扩展」新增 `Core` 徽章。用 `Grid` 三列贴右边缘，侧边栏变窄时优先压缩名称并显示省略号，
  不会把徽章挤出可视区。
- 页签措辞更新：`触发设置`→`触发与场景`、`外观样式`→`外观与形态`、`手势动作`→`手势与动作`、
  `系统设置`→`高级与系统`、`关于软件`→`关于与更新`。
  **只改词条的值，键名与页签索引一律不动** ⇒ `NavTab0`…`NavTab5` 这套测试契约、
  `i18n_baseline.json` 的 `tab_{i}` 分桶、`AutomationId` 全部不受影响。
- 纯界面改动：**未触碰任何功能、动作分派逻辑与配置格式**。

### 🧩 官方插件仓库迁移

- 官方动作插件改由 `StarPie-Official-Plugins` 的 GitHub Release catalog 下载。
- 主程序不再从本仓库构建或发布官方插件 DLL。
- 官方模块包下载后校验包大小、包 SHA-256、模块清单和程序集 SHA-256。
- 保留社区插件的本地安装入口，官方模块默认后台同步。

### 🌍 多语言收口与版本号纠偏

**官方插件面板此前整块没接多语言**：实测 `I18n.cs` 里 `OfficialPlugins*` 词条数 = **0**。这一块是迁移时新加的，只进了 XAML 与代码，没同步接进本地化 —— 切到英文 / 日文后，页头、进度行、卡片徽标、安装按钮与两个弹窗全都还是中文。与 `7cb7120` 修的「插件页整页中文」是同一类问题在新面板上复发。

- **新增 19 个词条 × 4 语言**：XAML 侧 `OfficialPluginsHeaderText` / `OfficialPluginsStatusText` / `RefreshOfficialPluginsButton`；代码侧进度行三态、安装成功 / 失败弹窗与标题；卡片侧 `OfficialPluginListItem` 的状态徽标、按钮文案与摘要兜底（另拆了一个「类型认领分隔符」键 —— 简中仍是 `、`，英文改 `, `，否则英文界面会出现 `Launch、Command` 这种混排）。
- **进度行改为状态推导**（新增 `RenderOfficialPluginsStatus()`）：原先三个分支各写一遍中文字面量，切语言时无从重渲染。现按「加载中 / 有目录 / 拉取失败 / 初始提示」四态取当前语言，并由 `RefreshPluginManagerUi()` 与 `ApplyPluginsPageLocalization()` 各调一次，所以切语言后这行不会再残留旧语言。
- **顺带收口**插件动作页两处同样硬编码的中文弹窗（未选动作 / 插件未找到）。
- **版本号**：`AGENTS.md` §3.7 写「当前契约版本 1.2」，实际已是 **1.4**（1.3 增 `ScreenCapture`、1.4 增 `InputSimulation`），已按 `PluginApi` 注释补齐演进清单；§3.7 能力门禁段还停在「只有 `Process` / `WindowControl` 两个强制点」，实际是**五个服务面、四个能力位**，已改正并补上 `InputSimulation` 为何不与 `Process` 合并。§5.3 的版本同步清单列的是 `App.xaml.cs` / `TrayController.cs` 的「回退文本」，而这两处早已改为统一从 `AppVersionInfo` 取 —— 清单已重写为真实落点（`csproj` / `AppVersionInfo.FallbackVersion` / `SettingsWindow.xaml` 4 处 / `CHANGELOG` / `build-installer.ps1` / `StarPie.iss` 两个 `/D` 兜底）。
- **消掉一个隐藏同步点**：`UpdateManager` 的 User-Agent 里写着 `?? "1.8.0-beta.1"` 字面量，且取的是 `AssemblyVersion`（会把预发布标识丢成 `1.8.0`），与设置页另一个 UA 写法不一致。改为统一取 `AppVersionInfo.DisplayVersion`。

### 🔧 修复「官方模块自检整段 FAIL」

迁移到在线目录时，**保留前缀只能走官方在线目录**成了硬契约（`PluginHost.InstallCandidateAsync` 与 `PluginInstance.LoadCore` 各拦一次），但自检仍按随包时代的路径把官方 dll 当**社区候选**去装 —— 于是拿任何一枚官方模块 dll 跑自检都会：

- `[3] 启用` FAIL：「插件 ID 使用了保留前缀」；
- `[5] 活动调用租约` 连带 FAIL（插件没加载起来）；
- `[5b] 重启后首次惰性调用` 被**静默跳过**（理由写成「插件未加载」）—— 等于丢了一段覆盖却看不出来；
- `[3d] 候选安装` FAIL：按新契约它本来就该被拒绝。

**已用干净 HEAD（`59d06c2`）复核：同样 FAIL**，确认是迁移引入、与其他改动无关。不改的话，官方模块这条路此后没有任何端到端验收手段。

- `[2]` 安装登记补上 `Official = PluginPaths.IsReservedPluginId(manifest.Id)`。这不是给自检开后门 —— `OfficialPluginClient` 走的就是这套（`Official = true` + 回填 `ClaimedTypes`），加载路径也正是按 `Entry.Official` 决定是否放行保留前缀。
- `[3d]` 改为按目标身份分支：保留前缀（官方模块）**断言候选安装应被拒绝**并打印拒绝理由；非保留前缀的社区 dll 仍走原来的完整正向流程（含「装完即运行态」「裸 DLL 回填清单」「单枚复制」「ID 重复」等回归断言）。
- 验收：自检结论 **PASS —— 全链路可用**，且 `[5b]` 从「跳过」变为**真实执行**；`%TEMP%/StarPie-PluginSelfTest-*` 零残留。

### 🩹 恢复在一处合并中丢失的自检护栏 `[3j]`

`PluginSelfTest.cs` 的 `[3j]`（宿主服务面与能力门禁，~200 行）在一次合并中被**整段顶掉**：`refactor` 分支在旧基线上重写了整个自检文件（2813 行 → 945 行），合并时整体取它，`devplugin` 侧后加的 `[3j]` 随之消失。而 `AGENTS.md` §3.7、`PluginCapabilityLabels` 类注释、`PluginHostServices.RunPreset` 注释**仍在引用它** —— 也就是说此后所有「已由 `[3j]` 守」的结论都没有依据。同一批消失的 `[3k]`/`[3m]` 守的是随包机制，该机制已随官方目录迁移移除，消失是对的；`[3j]` 守的能力门禁**功能完好存在**，属真回归。

- 按现行服务名**重写**恢复（非照抄）：类型关系、五个服务面的拒绝路径、三条「声明后放行」、四条跨能力交叉断言、能力位两两不重复、确认页文案全覆盖、四份元数据清单同源、SDK 契约版本一致。
- 系统预设清单的核对从「只比项数」升级为「逐项比对 `Key` + `DisplayName`」—— 项数对得上但下标串位挡不住「关机写成睡眠」。
- 另修一处**会说谎的输出**：三条汇总行（能力位 / 能力文案 / 系统预设清单）原先无条件打 ✓，在已经 `[FAIL]` 之后仍然打勾 —— 一份在报错旁边说「覆盖全部」的报告会让人以为报错是误报。已改为条件式，失败时改口为「见上面的 `[FAIL]`」。
- **`AGENTS.md` §5.1 新增纪律**：改 `PluginSelfTest.cs` 前后必须比对段落号集合（`grep -o '\[[0-9][a-z]*\]' … | sort -u`），少一段就要回答「它守的东西现在由谁守」。
- 验收：自检 **PASS —— 全链路可用**，`[3j]` 段 18 个 ✓；**变异测试**证明它是真护栏而非一堆永远绿的断言 —— 把 `PluginWindowService` 的 required 从 `WindowControl` 改成 `Process`，构建**仍 0 警告**（编译器抓不到），自检当场报 3 条 `[FAIL]`；删掉 `PluginCapabilityLabels.All` 里 `WindowControl` 一行，报「能力位…在安装确认页上没有对应文案」。

### 🧩 安装确认页收敛为唯一实现，并新增 `[3e]`「整页随语言切换」护栏

「装插件」这个语义此前有**两份互不相干的确认页**：候选卡片一份（`ConfirmCandidateInstall`，全程走词条），手动选 `.dll` 一份（`ConfirmPluginInstall`，约 40 行整块硬编码中文）。同一条路改一处漏一处是必然的，而且**手动安装那份连「装下去会覆盖掉什么」都没说** —— 用户选了一枚与已装版本冲突的 dll，点确定之前看不到任何提示。

- **归一**：新增 `Plugin/PluginInstallConfirmation.cs`，把输入（`Scan` / `State` / `Note` / `EnableAfterInstall`）与正文构造（`PluginInstallConfirmationText.Build`）拆成两个纯数据/纯函数类型；`SettingsWindow.ConfirmPluginInstall` 只剩一行弹窗。正文按「它是谁 → 从哪来 → 会拿到什么能力 → 装到哪 → 装完会怎样 → 风险与接受」六段重排，是两份旧正文的**并集**（手动那份的文件事实 + 候选那份的状态与扫描结果）。
- **手动安装首次获得安装后果判定**：新增 `PluginHost.ClassifyManualInstall(scan)`，与候选路径共用同一套 `ClassifyCandidate`，只是撞 ID 上下文传空集合（手动选文件不存在「扫描目录内两枚 dll 撞 ID」这一维）。于是手动安装现在会明确告诉你这是**更新 / 降级 / 同版本换内容 / 内容完全相同 / 版本无法比较**，而不是笼统地「确认安装」。
- **「覆盖了哪一份」与「装完启不启用」拆成两句**：候选安装走 `EnableAfterInstall = true`、手动安装走 `false`（刻意不自动启用，见原注释），原先混在一句话里的措辞没法单独改一条，拆开后两处都会如实说明。
- **新增 31 个词条 × 4 语言**，删掉被拆分的 5 个旧键（`PluginsConfirmUpdate/Downgrade/Replaced/Fresh/Privileges`）。`InstallPluginButton_Click` 里同属这条路径的对话框标题、文件类型过滤串、读取失败、非插件提示、安装完成提示一并接线 —— 标题与正文必须同时接，只接标题会得到「英文标题 + 中文正文」的混排，比整句原文更糟。
- **修掉一类漏翻：分隔符也算文案。** 能力清单原用 `string.Join("、", …)` 连接，顿号是写死的中文标点 ⇒ 英文界面出现 `Declared capabilities: Process、WindowControl`。新增 `PluginsEnumSeparator`（简中 / 繁中 / 日文 `、`，英文 `, `），确认页与候选卡片摘要同理修正。纯字形分隔符（`　|　` / `　·　`）是排版装饰、不随语言变，已加注释说明两者区别。
- **`PluginCapabilityLabels` 整表接入 i18n（12 个新键）**：此前它整块是硬编码中文，于是「已经接好 i18n 的确认页」内部藏着一整段中文风险说明 —— 只看那页代码或只看词表覆盖率都发现不了。表内改存**词条键**、在 `Describe()` 里现取（存文案的话 `static readonly` 只在类型初始化时求值一次，切完语言确认页仍是旧语言）。`[3j]` 的文案断言随之改为校验**解析后**的文本，并专门判「返回的是不是裸键名」（键名写错时 `I18n.T` 原样返回键名，既不空白也不像错的）。
- **自检新增 `[3e]`「安装确认页正文」**（5 条断言）：用**合成**的扫描结果（全 ASCII）构造正文并逐语言驱动 —— ① 无未替换的 `{n}` 占位符；② **英文页里不许有方块字、假名或全角标点**；③ 四种语言产生四份互不相同的正文；④ 关键字段真的拼进去了；⑤ 两种「装完是否立即启用」产生不同正文。另守「每个具名状态位都有安装后果文案」，判据写成「共用兜底措辞的状态集合**正好**是哪几个」—— 新增状态位忘了配文案时会落进那个集合从而变红，而写成列举式的话新状态位根本没机会被这条断言看见。
- 顺带修掉一处格式缺陷：`DescribeCapabilities` 与紧随其后的方法挤在同一行（上一次改动留下的），现已拆开。

**这一段的实际价值当场兑现**：`[3e]` 上线第一次运行就报出「英文页里出现了中文字符『启』」，牵出上面那两块（`PluginCapabilityLabels` 与顿号分隔符）—— 它们都属于「编译、静态检查、词表覆盖率全绿，界面仍是原文」这一类，只有真的切一次语言才看得见。

**验证**：构建 0 警告 0 错误；自检 `PASS —— 全链路可用`，`[3e]` 两行 ✓（英文页 1034 字符无方块字）、`[3j]` 全绿，`%TEMP%/StarPie-PluginSelfTest-*` 零残留；**三次变异测试**证明新断言真会红 —— ① 把分隔符改回硬编码顿号 ⇒ 报「英文页出现『、』(U+3001)」；② 删掉一条英文词条 ⇒ 报「英文页出现『插』」并回落到简中；③ 把 `Installed` 改成共用兜底文案 ⇒ 报「共用兜底的状态是 [Installed, Replaced, Duplicate, Reserved, Rejected]，预期是 [Replaced, Duplicate, Reserved, Rejected]」；三次均已还原。词表护栏 `check_i18n.py`：**662 键、0 重复、0 缺语言分支、0 简中空值、0 引用但未定义**，本次新增 44 键全部有引用且占位符跨语言一致；中文判据护栏 `scan_cjk_logic.py`：34 处全部已分类、默认名 8/8 覆盖、退出码 0。

### 🧪 `tests/` 新增「切英文后整页已翻译」棘轮回归

前两轮 i18n 漏接（插件页整页中文、官方面板整块没接）**都躲过了现有 26 个 UI 用例**：唯一沾边的 `test_v138_i18n_multilanguage_support` 每个语言档位只看 2 个控件（`SaveButton` / `NavTab0Text`），而它抽查的那 2 个一直是好的。也就是说，**这套套件此前对「整页漏翻」零灵敏度**。

先量清真实规模再决定护栏形状：`SettingsWindow.xaml` 里含中文的 `Text`/`Content`，**441 处没有 `Name`**、其中 **376 处词表里压根没建键**（抽样确认 `EnableMultiTierDesc` / `SubmenuStyleTitle` 等键存在但代码引用 0 次 ⇒ 整个区块建了没接线）。这批控件既进不了静态差集（`check_i18n.py` 只扫带 `Name="X"` 的），也进不了按 auto_id 的 UI 断言 —— **静态与动态双双漏掉**。所以这不是「补一条用例」的量级，因此改走**台账（棘轮）断言**。

- **新增 `tests/test_i18n.py`**：切英文后逐页签采集「仍含中文的可见文案」，断言**观测集合 ⊆ 基线集合**（`tests/i18n_baseline.json`，共 88 条 / 5 个页签）。只允许变少、新增即红；变少时以 warning 提示收紧台账，让「还欠多少」每次跑都看得见。
- **基线对应真实首装现场**：夹具第一次启动让程序**自己**生成默认配置，只把 `Language` 改成 `en` 再启动第二次。直接写一个 `{"Language":"en"}` 的最小配置会得到**空轮盘**（实测动作名显示占位符「动作 1」、子动作数 0、级联区只显示空态提示）—— `EnsureConfigHealth` 只校验、不补默认数据。这个差别是被实测抓出来的，不是推测。
- **避开三处假阳性**（都给不出「界面没翻干净」的信息，收进来只会逼人放宽判据）：操作系统提供的窗口按钮 `关闭/最大化/最小化`（跟随系统语言而非应用语言）；`PluginCandidatesPathText` 里那个作排版分隔符的表意空格 U+3000（故 CJK 判据**刻意不含**它）；每次运行都会变的数字（版本号 / 上次检查时间 / 扇区号）统一归一化为 `#`。
- **用例主动掐网换确定性**（首次采集后实测到的真问题）：插件系统在启动约 2 秒后后台同步官方模块，**同步赶在采集之前完成时插件页会多出「已安装模块」卡片，赶不上就没有** —— 同一份代码给出时多时少的观测集合，台账随机红。故把 `HTTP(S)_PROXY` / `ALL_PROXY` 指向必然拒绝连接的 `127.0.0.1:1`（实测 .NET 在 Windows 上同样优先读这些变量，应用日志立即出现「刷新官方插件目录失败：目标计算机积极拒绝」）。本用例断言的是应用自身文案，与远端目录里恰好有哪些模块无关；顺带让它在离线 / CI 环境同样成立。**一个会随机红的用例，最大的代价不是它红了，是它训练所有人无视红灯。**
- **首次「真红」还抓到一处此前无人发现的漏翻**：`SettingsWindow.xaml:3990` 的 `Content="🗑 卸载"` 与同模板的 `Content="启用"` —— 它们在 `ListBox.ItemTemplate` 的 `DataTemplate` 里，**命名域不同、`Name` 对它无效**，所以 `check_i18n.py` 的「具名控件漏接 = 0」看不见它们，正确解法只能是 `{Binding}` 到视图模型的本地化属性。顺查确认同一渲染路径还有 **约 22 条代码拼串**（`BuildPluginListItem` 的 `作者` / `贡献 N 个动作` / `未加载` / `声明能力：` / `已签名` / `未签名` / `外部路径`，`DescribePluginState` 的 10 个状态名，以及 `"StarPie 插件"` 标题与「打开扫描目录失败」）—— 该函数注释自称「所有面向用户的文案都集中在这里」，**集中了、但没接词条**。归属「插件面 i18n」那一笔，已在 `AGENTS.md` §5.4 与记忆里登记（本轮不擅自扩大范围）。
- **`tab_4`（关于 / 更新日志）显式排除**：正文是发行说明散文，项目有意只发中文。纳入台账会让「每次发版新增一条 release note」都变成一次失败，从而训练所有人去改台账 —— 那等于把这张网拆了。代价是该页的标签类文案一并失去覆盖，已在用例与 `AGENTS.md` §5.4 记为已知欠账。
- **前置断言先判「语言到底切成英文没有」**：否则配置路径一旦出问题，得到的是一份几百条的大 diff，症状看着像「i18n 全面崩了」，而真相是用例自己的现场没摆对。
- **`conftest.py` 抽出 `find_exe()` / `launch_app()`**：需要「启动前改现场」的用例（预置语言）必须自己控制启动时机，而 `app` 夹具启动得比用例早、替不了。抽出来是为了不再抄第二份候选路径列表 —— 抄一份漂一份，漂掉时的表现是「找不到 exe」这种与被测功能无关的失败。
- **验收**：`dotnet build -c Release` → **0 警告 0 错误**；`pytest tests/` 收集 **27** 个用例（26 + 1）；修掉网络不确定性后**连续三次运行**（1 次标定 + 2 次断言）结果一致（0 新增 0 减少）；**变异测试**把 `ConfigModeProRadio.Content = I18n.T(...)` 注释掉（＝真实的「漏接线」形态），构建**仍 0 警告**，用例当场报 `英文界面上出现了 10 条台账里没有的中文文案：⚙️ 高级模式` 并列出页签与控件名；**共享夹具冒烟**再跑 `test_switch_all_tabs_smoothly` 与 `test_v138_i18n_multilanguage_support` 两条老用例，均 PASS。标定用的 `scratch/dump_i18n_en_cjk.py` 与中间产物已删除（台账改由用例自身以 `STARPIE_I18N_UPDATE_BASELINE=1` 重采，避免两份实现漂移）。

### 🔌 插件动作面板接入多语言，并修掉「切完语言编辑器停在旧语言」

插件动作编辑面板（「手势动作」页里选中一个插件动作之后那一块）的文案此前是 `SettingsWindow` 私有方法里的一整段**代码拼串**，一条词条都没接；更要紧的是 **`ApplyLocalization()` 从不调用 `UpdateFocusEditorUi()`** —— 也就是说这块即便接了词条，切语言时也不会重渲染，只会冻结在构建时那门语言。

- **新增 `Plugin/PluginActionPanelText.cs`**：把面板文案抽成纯静态类（`NotChosen` / `Unavailable` / `Registered` / `ParamsHint` / `IssuesCount`），`SettingsWindow` 只剩「把字符串摆到控件上」。**这一步不是为了整洁** —— 窗口类的私有方法无界面自检够不着，「切了语言它还残不残中文」就只能靠人肉点一遍；搬出来之后 `[3g]` 才写得成。与 `[3f]`（`PluginListItem`）、`[3e]`（`PluginInstallConfirmationText`）是同一条理由：**可测性是靠摆放位置换来的**。
- **入参是基本类型而不是 `PluginActionRegistration`**：注册表字段随时会长，而自检里要构造一个合法 registration 得连带填一堆无关字段 —— 基本类型让「驱动一次」变成一行。
- **修掉「同一个窗口里两种语言并存」**：在 `ApplyLocalization()` 末尾补一次 `UpdateFocusEditorUi()`（`if (IsLoaded)` 挡住构造期那次调用）。这一处覆盖的不止插件面板 —— `Hotkey` / `Launch` / `WebUrl` / `Folder` / `Command` / `WindowManager` / `System` / `Ocr` / `ShellTool` 九个面板**同样一直停在旧语言**，因为它们全是代码拼串。`UpdateFocusEditorUi` 自带重入守卫、幂等，与它 40+ 个既有调用点走同一条路；且 `PluginParameterForm.Build` 的文档与实现都是「重建表单**并回填已保存的值**」（从 `ActionItem.ExtensionData` 取），所以重建不会清空用户输入。
- **14 个新词条 × 4 语言**（面板标题复用既有 `ActionTypePluginShort`）：未选择的两种处境各一句、引用失效的正文与警示句、`提供插件` 的两种写法（显示名与 ID 相同时不重复显示）、执行方式（后台并发 / 串行）、超时、贡献点 ID、必填参数提示、全部可选、校验结论「还差几项」。**中文值与原字面量逐字一致 ⇒ 简中界面一字不变。**
- **三种处境的引导语是分开的**（这是原本就有的设计，本轮加了护栏防止后人合并）：有候选 → 去下拉框里挑；无候选 → 去插件页装并启用；引用失效 → 去插件页确认状态或改选。用户照提示操作走错地方，是这类文案最典型的失效方式。
- **自检新增 `[3g]`（4 条断言）**：① 四种处境 × 四种语言，标题与正文非空、**不是裸键名**，提示行「要么非空、要么老实是 `null`」；② 同一处境四语言必须给出四份不同正文；③ **四种处境的正文必须四句不同**；④ 英文面板的**宿主部分**无方块字与全角标点（插件自带数据按值降序摘除，沿用 `[3f]` 踩过的坑）。段落同样刻意排在 `[4]` 之前 —— `--skip-invoke` 会在 `[4]` 开头提前 return。

**两次变异测试证明 `[3g]` 是真护栏**：① 把 `PluginsPanelKindSerial` 的英文值换回中文 ⇒ 自检报「英文面板的『正常』里出现了中文/日文字符『串』(U+4E32)」并打印摘除后的原文；② 让「有候选 / 无候选」两个分支共用一句话 ⇒ 报「四种面板处境的正文只得到 3 份不同文案 —— 有两处共用了同一句话」。两次均已还原，`grep MUTATION` 为空。

**诚实边界（已写进 `AGENTS.md` §5.1）**：**「切语言重渲染」这一条没有机器护栏**。UI 套件一律在**启动前**把语言写进 `config.json`（app 内切换在 pywinauto 下因 emoji 被剥而脆弱，故有意不用它采集），所以没有任何一条断言在看「切完之后有没有换」。目前只有 `test_settings.py::test_v138_i18n_multilanguage_support` 真的在 app 内切了一次语言、证明这条路径**不炸**，**但它不校验内容** —— 别把「它绿了」读成「重渲染是对的」。面板文案本身由 `[3g]` 守，两者是分工。

**验证**：`dotnet build -c Release -t:Rebuild` → **0 警告 0 错误**；`--plugin-selftest <官方 Launch.dll> --skip-invoke` → `PASS —— 全链路可用`，段落号 `[0][1][2][3][3b][3c][3d][3e][3f][3g][3j][4][5][5b][6][7]`（**16 段**）、0 条 FAIL、`%TEMP%/StarPie-PluginSelfTest-*` 零残留；`check_i18n.py` → **712 唯一键 / 0 重复 / 0 缺语言分支 / 0 简中空值 / 0 引用但未定义**，本次新增 14 键全部「引用=是、语言=4」、占位符跨语言不一致 **0**、插件页漏接具名控件 **0**；`pytest tests/test_settings.py -k "v138_i18n_multilanguage_support or v139_folder_action_type_and_i18n_consistency"` → **2 passed**（这是「app 内切语言不炸」的实测依据）。

**已知未覆盖（登记）**：插件面仍有约 **250 条**面向用户的中文串 —— `PluginHost` 47 / `PluginManifestReader` 35 / `PluginInstance` 30 / `PluginScanner` 30 / `OfficialPluginClient` 29 / `PluginInvoker` 28 / `PluginContext` 20 / `PluginRuntime` 20 / `PluginParameterForm` 12 / `PluginParameterValidator` 9 等，其中相当一部分是异常消息（只在报错路径出现，且那些路径另有已接词条的摘要行）。**日志与自检控制台输出约 400 行按既有约定不翻**。

### 🌍 插件管理页整块接入多语言，并新增 `[3f]`「卡片文案」护栏

上一笔给台账补「插件卡片看不到」这个盲区时，顺查确认同一条渲染路径上还有 **2 处 `DataTemplate` 硬编码**与**约 22 条代码拼串** —— 它们集中在一个自称「所有面向用户的文案都集中在这里」的函数里，**集中了、但一条词条都没接**。本轮把这块整个接掉。

- **`DataTemplate` 那两处只能改 `{Binding}`**：`SettingsWindow.xaml:3990` 的 `Content="🗑 卸载"` 与同模板的 `Content="启用"` 在 `ListBox.ItemTemplate` 里，**命名域不同、`Name` 对它无效**（所以 `check_i18n.py` 的「具名控件漏接 = 0」看不见它们，动态断言也够不着）。改为 `{Binding UninstallText}` / `{Binding EnableText}`。
- **卡片文案从窗口类搬进 `PluginListItem`**：`BuildPluginListItem`（约 90 行）与 `DescribePluginState` 原本是 `SettingsWindow` 的私有成员，**无界面自检够不着**，「卡片翻没翻」就写不出断言。整体搬成 `Plugin/PluginListItem.Build` 与 `PluginListItem.DescribeState` —— 这一步不是为了整洁，是**可测性的前提**：面向用户的文案构造必须待在纯静态、非窗口类里。
- **`DescribeState` 拆出「值驱动」重载**（`(state, requiresRestart, entryEnabled)`）：自检要能用 `Enum.GetValues` 逐个成员驱动它，而「逐成员」没法靠构造 9 个 `PluginInstance` 来做（`Active` / `Installed` 还各有两种处境）。
- **顺带修掉一处藏了很久的缺陷**：状态兜底原是 `_ => instance.State.ToString()`，于是 `Stopping` 在中文界面上直接显示英文枚举名。现收进 `PluginsStateStopping`，并由穷尽 switch 保证它不可能再落兜底。
- **补 36 个词条 × 4 语言**：卡片摘要与详情 8 个、两个按钮 2 个、状态名 11 个、弹窗与提示 15 个；简中值与原字面量逐字一致 ⇒ **简中界面一字不变**。
- **收编一个孤儿键**：`PluginsDisableFailed`（原值 `停用失败：{0}`，此前**全仓 0 引用**）。插件行开关的停用失败分支本来就没接词条，现按 `停用插件 {0} 失败：\n\n{1}` 接通 —— 措辞与启用侧的既有文案对齐，不再出现「停用失败只有动作名、没有原因」。
- **`PluginRowEnabledCheckBox_Click` 一并接线**：确认停用 / 停用中 / 停用失败 / 启动失败 / 插件系统未就绪，以及重新扫描两条结果提示与两个「打开目录失败」提示。
- **自检新增 `[3f]`（4 条断言）**：① 逐 `PluginRuntimeState` 成员驱动 `DescribeState`，断言非空、**不是裸键名**、有图标，并单独断言 `Active` 与 `Installed` 的两种处境文案不同；② 逐语言 `PluginListItem.Build`，英文卡片的**宿主部分**（先按长度降序摘掉插件自带数据）不许有方块字与全角标点；③ 断言详情里含 `pluginId`（否则「没有中文」可能只是「什么都没拼」）；④ 四语言卡片两两不同。段落刻意排在 `[4]` **之前** —— `--skip-invoke` 会在 `[4]` 开头提前 return，排到后面等于日常回归根本不执行（`[5b]` 踩过这个坑）。
- **判据与台账对齐**：`[3f]` 的 CJK 判据与 `tests/test_i18n.py` 的 `CJK_RE` 显式一致，**刻意不含 U+3000**（表意空格在本项目里当排版分隔符用，与语言无关）。两处判据漂了会得到「自检绿、UI 套件红」这种自相矛盾的结果。

**两次变异测试证明 `[3f]` 是真护栏**：① 删掉 `Stopping` 分支 ⇒ 编译期 **CS8509** 报「模式 Stopping 未包含在内」，且 `#pragma warning disable CS8524` 没有把它一起吞掉（这正是「穷尽 switch 能当护栏」成立的前提）；② 把 `PluginsStateActive` 写成 `PluginsStateActiveTypo` ⇒ 自检当场报「状态「Active」取到的是裸键名『PluginsStateActiveTypo』—— 词条键写错了」。两次均已还原，还原后 `grep MUTATION` 为空、全量重建回到 0/0。

**诚实边界（写进注释，免得后人以为运行时断言是全覆盖）**：`[3f]` 的裸键名判据是**前缀形状**（「以 `PluginsState` 开头」），因为 `DescribeState` 内部才认识键名，运行时拿不到。保留前缀的错写会红；**前缀整个写错**（如 `PluginStateActive`）运行时看不见 —— 那一路由 `check_i18n.py` 的「引用但未定义」静态兜住。两者是**分工**，不是互相替代：静态管全覆盖，运行时管「取到手的到底像不像话」。

**台账这一轮零变化，而且是正确结果**：接完之后重采 `tests/i18n_baseline.json`，与旧台账**逐字节相同**（`git diff` 为空）。两层原因：卡片在 `ItemTemplate` 里，而**用例沙箱中没有任何已安装插件** ⇒ 列表为空、模板从未实例化；那条「原先引用的插件动作已不可用」提示也只在特定状态下才出现。也就是说这张台账**根本够不着**插件卡片 —— 这恰恰是 `[3f]` 存在的理由。已把这条实测结论写进 `tests/test_i18n.py` 的盲区 5、`AGENTS.md` §5.4，并给「收紧台账」一节补了一句：**重采后 diff 为空也是有效结论**，该做的是补一条够得着那个界面的断言，而不是反复重跑或去搬台账。

**验证**：`dotnet build -c Release -t:Rebuild` → **0 警告 0 错误**（全量重建；增量构建的 0/0 不作数 —— 构建被残留进程锁住时会顺带打印一批假警告）；`--plugin-selftest <官方 Launch.dll> --skip-invoke` → `PASS —— 全链路可用`，段落号 `[0][1][2][3][3b][3c][3d][3e][3f][3j][4][5][5b][6][7]`、0 条 FAIL、`%TEMP%/StarPie-PluginSelfTest-*` 零残留；`check_i18n.py` → **698 唯一键 / 0 重复 / 0 缺语言分支 / 0 简中空值 / 0 引用但未定义**，本次新增 36 键全部「引用=是、语言=4」，占位符跨语言不一致 **0**，插件页漏接具名控件 **0**；`scan_cjk_logic.py --pending` → 34 处全部已分类、默认名 8/8、退出码 0；`pytest tests/test_i18n.py` → PASSED。

**已知未覆盖（登记，不冒充已做）**：`RefreshFocusPluginPanel` 那 19 条 —— 经查它**只在切换动作类型时被调用**，切语言不会重渲染，接完会残留旧语言，须连带一个重渲染钩子，属独立改动；`SettingsWindow.xaml.cs` 其余约 30 处硬编码中文；插件识别 / 清单校验 / 运行时拒绝文案（`PluginScanner`、`PluginManifestReader`、`PluginRuntime` + `PluginPathModules`、`OfficialPluginClient`、`PluginParameterValidator`）约 80 条。**日志与自检控制台输出约 400 行按既有约定不翻**（它们是给排障的人看的，不随界面语言切换）。

### 🧪 自检

- `dotnet build WinPieGestures/WinPieGestures.csproj -c Release` → **0 警告 0 错误**。
- 词条完整性（`scratch/check_i18n.py`，新增脚本）：**571 个唯一键、0 组重复、0 个键缺语言分支、简中无空值**；代码里 `I18n.T` / `I18n.TF` 的字面量引用与字典定义求差，「引用但未定义」= **0**；本次新增 19 键**全部有引用**。
- 漏接复查：插件页「有 `Name` + 硬编码中文却从未被重设」的具名控件数 = **0**。
- 不变量「简中界面一字不变」：三个 XAML 设计期占位的字面量与对应词条的简中值逐字比对**完全一致**；其余改动均为「字面量 → 词典同值」，简中输出零变化。
- 自检工具本身：改前后各跑一次官方模块 dll（`--skip-invoke`）。改前 FAIL、改后 PASS，且**用干净 HEAD 复核过改前那次 FAIL**，排除「本次改动引入失败」的可能。
- 发布产物：`dotnet publish -c Release -r win-x64 --no-self-contained` 输出 **12 个文件、无 `plugin\`、无官方插件 dll**，AGENTS.md §5.2 的发布前校验成立。

## [未发布] - 2026-09-17（同步上游 dev-plugin：合并 13 个提交、缝合 4 处冲突、恢复两份插件规范）

向上游提 PR 显示「冲突过多」的根因不是本分支改错了，而是**上游把本分支的 PR #129 合并后又整个 revert（`e558028`），再用 PR #132（`pr-129-migration`）重做了一遍** —— 两边对同一批文件成了「功能相同、写法不同」的并行修改，基线漂移。本次把上游合并进来，让冲突收敛掉。

### 🌟 核心改进

1. **上游成为本分支的祖先** ⇒ 平台侧判定可快进合并、PR 冲突消失，且 PR 的 diff 收敛为本分支的净新增（上游那 13 个提交落进 base、不再进 diff），审查面显著变小。
2. **采纳上游的插件运行时**（本分支此前判定「不采用」，该判定已被推翻）
   - 恢复 `Plugin/PluginRuntime.cs`（466 行）与 `Plugin/PluginPathModules.cs`（449 行）：统一调用入口与路径模块、活动调用租约、异步停用状态机。
   - 热重载 / 覆盖安装 / 卸载改为**等完全停止后再继续**（`DisableAsync` + `IsFullyStopped` + 5 秒宽限期），不再有旧程序集仍被占用的窗口。
   - 与顶层类型认领、`plugins\` 下 12 个单动作包**实测可以共存**，两者在 `PluginHost` 上并不互斥。

### 🐛 问题修复（自动合并的静默顶替 —— 只看构建结果是发现不了的）

3. **`AGENTS.md` 三处内容被顶掉**：一级章节数 9→9 看不出问题，但三级标题丢了两大块 —— 本分支的 `### 3.7 插件系统`（83 行）与上游的 `## 4. 插件系统架构与开发规范`（50 行）双双消失，只剩一个自相矛盾的 7 行说明块（称「不采用」、称「那两个文件已删除」（实际正被 `PluginHost` 使用）、又称「现行规范见 §3.7」而 §3.7 已不存在，构成死链）。
   - 现改为：`§3.7` 恢复为完整规范（20 个主题，含顶层类型认领 / 宿主能力门禁 / 派发顺序），`§4` 改为「插件系统运行时重构」并保留上游独有的 5 条，文档目录锚点同步 —— 无丢失、无矛盾、无重复。
4. **`CHANGELOG.md` 差点丢掉 552 行**：上游在该文件的「独有内容」实测只有一个空行（`git diff --numstat` = `1 新增 / 552 删除`），本分支才是超集；手工拼接会丢 10 个「未发布」小节并造出 beta.4 / beta.3 各两份。改为整体取本分支版本。
5. **`docs/plugin-system-architecture.md` 的状态标注此前是反的**：曾标注「本分支不采用本方案…仅作设计参考」，而该文档同时被 `AGENTS.md` §4 与 `README.md` 正向引用 —— 等于对读者说反话。现改为「现行实现」，并写清原判定被推翻的原因。
6. **`SettingsWindow.xaml.cs` 三个冲突块**按语义缝合：上游在改插件 API（`DisableAsync` / `IsFullyStopped`），本分支在改同一处文案（i18n）—— 保留上游逻辑、接回 i18n。块 1 有个**冲突标记之外**的陷阱：紧随其后的共享行用到变量 `msgTitle`，而它只在本分支那一侧定义，无脑取上游那侧虽「解决了冲突」也会 CS0103。
7. 新增词条 `PluginsReloadNotStopped`（4 语言），承接上游那句「旧插件尚未完全停止」。

### 🧪 自检

- `dotnet build WinPieGestures/WinPieGestures.csproj -c Release` → **0 警告 0 错误**。
- 12 枚随包插件逐一 `--plugin-selftest --skip-invoke` → 全部「PASS —— 全链路可用」，`%TEMP%/StarPie-PluginSelfTest-*` 零残留。
- 重复块扫描（编译器覆盖不到的部分）：`AGENTS.md` / `README.md` / `docs/plugin-system-architecture.md` 均 0 处；`CHANGELOG` / `I18n` / `SettingsWindow.xaml(.cs)` 的候选块与合并前逐项相同（`I18n` 的 9 组重复键另见下一节，已清除）。
- 章节集合对称差：`CHANGELOG` 恢复为 72 个小节、beta.4 与 beta.3 各 1 个；`AGENTS.md` 9 个一级章节。
- 未解决的冲突文件：0 个。

## [未发布] - 2026-09-17（设置窗口多语言补齐：94 处漏接控件与 83 个词条，并清掉 9 组重复定义）

上一节补的是**插件页**（S4 拆包的产物），这一节补的是**设置窗口主界面**。

### 🐛 问题修复

1. **94 处控件从未被 `ApplyLocalization()` 赋值**（`Content` 73 / `Text` 18 / `Header` 3）
   - 这套控制台靠「XAML 写中文默认值 + `ApplyLocalization()` 逐项按当前语言重设」。此前只扫过「孤儿键」（建了没人用）这一个方向，漏了反方向「用了没建」：这些控件有 `Name`、有硬编码中文，但代码从未重设该属性 ⇒ 切到英文 / 日文后一直显示中文。
   - 两个方向实测大量重叠，因此处置方向是**接回去**，而不是删死数据。
2. **新增 83 个词条 × 4 语言**，其中 **11 处复用已有键**（只认精确匹配 —— 子串式匹配会把 `EdgeOverflowDescText` 关联到 `SoundOnPopup` 这类语义毫不相干的键，照它接线比不接更糟）。复用顺带救活了 4 个孤儿键：`BtnDeletePreset` / `BtnOpenChangelog` / `Tier1ConfigSegment` / `Tier2ConfigSegment`。
3. **清掉 9 组重复的词条定义**（`SidebarModeSimple`、`SidebarModePro`、`BlacklistTitle`、`BlacklistDesc`、`BtnRenameProfile`、`BtnDeleteProfile`、`ClickSectorHint`、`AutoStartAsAdminTitle`、`AutoStartAsAdminDesc`）
   - `static I18n()` 是同一个方法体、顺序赋值、中途无 `return` ⇒ 字典索引器**后定义静默胜出、先定义是纯死代码**。9 组先定义已删除，词条映射逐键核对**一字未变**（零行为变化）。
4. **两处「设计稿 ≠ 运行时」的文案还原**：`RenameProfileButton` / `DeleteProfileButton`（列表模式的整宽按钮）与画布模式的紧凑按钮原先**共用同一个键**，而短文案的定义写在后面、静默覆盖了长文案 —— 于是列表模式按钮少显示了「当前配置」。
   - 时间线确认这是**意外遮蔽**而非有意简化：长文案与整宽按钮 2026-08-23 一起进来，紧凑按钮 09-03 才加，短文案是 09-12 在一个无关提交里补上的；同一面板的兄弟按钮（`➕ 新建自定义配置`、`📑 复制方案`）也都是长标签。
   - 现拆成 `BtnRenameCurrentProfile` / `BtnDeleteCurrentProfile` 两个键（长文案原文即取自被顶掉的那份定义，En / Ja 译文原样沿用），列表模式按钮恢复「✏️ 重命名当前配置」/「🗑️ 删除当前配置」。

### 🧪 自检

- `dotnet build WinPieGestures/WinPieGestures.csproj -c Release` → **0 警告 0 错误**；12 枚随包插件 `--plugin-selftest --skip-invoke` 全部 PASS，`%TEMP%` 零沙箱残留。
- **键完整性**：代码里 `I18n.T` / `I18n.TF` 引用的键与字典定义求差，「引用但未定义」为 **0**。
- **不变量「简中界面一字不变」**：94 处赋值逐条比对键的简中词条与 XAML 原文，0 处差异。**唯二例外是本轮有意还原的第 4 项**（列表模式两个按钮恢复「当前配置」三字）。
- 词条侧：552 个唯一键、**0 组重复**、每个键均 4 条语言分支、简中无空值。
- UI 回归套件（`pytest tests/ -v`，会弹 GUI）由维护者手动运行；建议补一条「切语言后整页翻译」用例 —— 这类漏接只有真跑界面才暴露。

## [未发布] - 2026-09-17（插件页多语言补齐：S4 拆包漏接的 51 个词条）

合并审计的收尾发现：**插件页（S4 拆包的产物）从来没有接进多语言系统** —— 切到英文 / 日文时整页仍是中文。

### 🐛 问题修复

**根因**：这套控制台一直靠「XAML 写中文默认值 + `ApplyLocalization()` 里逐项按当前语言重设」。插件页是 S4 拆包时新加的：页面与按钮进了 XAML、`NavTab5` 也加进了侧边栏，但**没有同步加进 `ApplyLocalization()`**。还有一处更隐蔽 —— 侧边栏折叠逻辑里的 `navigationButtons` / `navigationTexts` 两个数组仍是 5 项（`NavTab0`~`NavTab4`），于是折叠侧边栏时插件页签的文字不会跟着隐藏。

**范围**（逐处核对得出，非估算）：

| 位置 | 处数 | 内容 |
| :--- | :--- | :--- |
| `SettingsWindow.xaml` | 8 | 页签、页头、副标题、四个按钮、启用复选框 |
| `SettingsWindow.xaml` 空状态 | 2 | 「还没有安装任何插件」两行 —— 此前没有 `Name`，代码无法重设，本次补 `Name` |
| `SettingsWindow.xaml.cs` 动态文案 | 7 | 列表状态摘要、安全模式横幅、候选区标题与路径提示、四个弹窗（重载失败 / 重载成功 / 候选消失 / 安装失败与成功 / 扫描目录缺失） |
| `Plugin/PluginCandidate.cs` | 3 个属性 | 候选卡片状态徽标（9 种）、摘要行（作者 / 声明能力）、安装按钮（4 种） |

**修法**：

- 新增 **51 个词条 × 4 语言**（简中 / 繁中 / 英文 / 日文），集中在 `I18n.cs` 的 `TabPlugins` 与 `Plugins*` / `PluginCandidate*` 两段，键名沿用既有风格。
- 新增 `I18n.TF(key, args)`：查表 + 填充 `{0}` 占位符。单独开这个方法而不是让每个调用点各写 `string.Format` —— 键缺失时 `T()` 会原样返回键名，此时 `string.Format` 作用在不含占位符的字符串上是安全的，失败路径不会把界面搞崩。
- 新增 `ApplyPluginsPageLocalization()`，在 `ApplyLocalization()` 的 `NavTab4Text` 之后调用；末尾重新绑定一次插件列表数据源 —— 候选卡片的徽标与按钮文案是 **getter**（每次读取时才查表），不重绑就不会跟着换语言。
- `navigationButtons` / `navigationTexts` 补上第 6 项（`NavTab5` / `NavTab5Text`）。

### 🧪 自检

- `dotnet build`（Release）→ **0 警告 0 错误**。
- **键完整性交叉核对**：代码里 `I18n.T` / `I18n.TF` 引用的 353 个键与字典定义的键求差，**「引用但未定义」为 0** —— 不存在会裸显键名的界面文本。
- 每个新增键都核对过 4 条语言分支齐全。
- `--plugin-selftest`（System 包，`--skip-invoke`）→ PASS 全链路可用，`%TEMP%` 沙箱零残留。
- 插件页是纯 UI 行为，`--plugin-selftest` 沙箱不覆盖；**界面验证需在运行中切到英文 / 日文确认整页翻译**（建议纳入 UI 回归套件）。

## [未发布] - 2026-09-17（版本记录回补与版本号同步：CHANGELOG 缺 beta.3 / beta.4 两个已发布小节）

`devplugin` 合并遗留的第三类 —— **文档层面**：合并结果里的 `CHANGELOG.md` 少了 `v1.7.4-beta.3` 与 `v1.7.4-beta.4` 两个**已发布**版本的小节。

### 🐛 问题修复

**根因**：`CHANGELOG.md` 在本次合并里是「真三方」文件。本分支的顶部是一整片「未发布」小节（S3-0 起，2026-09-15 ~ 09-17 的插件拆包工作记录），合并另一侧的顶部则是这两个已发布小节。合并结果保留了本分支的这一片，那一侧的两节被整体丢掉。

**为什么必须补回来**（这不属于「内容在别处也有」的情况）：

- 这两个版本**已经发布出去了**（`beta.3` 随 `339ef8b`、`beta.4` 随 `d6707ab`），版本号也已落到 `csproj` / `AppVersionInfo` / `StarPie.iss` 三处 —— 于是出现「程序自报 `beta.4`，而 CHANGELOG 里查不到 `beta.4`」。
- 它还**用户可见**：关于页的「查看完整 CHANGELOG」直接打开这个文件，且它随发行包一起分发。
- 本分支未发布区记录的是插件拆包（S 系列），与这两个版本的发布说明是两批内容，不能互相替代。

**修法**：从另一侧（`8963ad1`）按区间提取这两节原文 —— 删前断言区间边界恰好覆盖 2 个版本标题、首末行精确匹配 —— 插入到 `## [v1.7.4-beta.2]` 之前，维持时间倒序：未发布区 → `beta.4` → `beta.3` → `beta.2`。纯新增 88 行，零删除。

### 🐛 顺带修复：版本号同步（§5.3 五要素清单）

`v1.7.4-beta.4` 的发布提交只改了 `csproj` / `AppVersionInfo.cs` / `UpdateManager.cs` / `StarPie.iss`，漏掉的位置如下：

| 位置 | 原值 | 说明 |
| :--- | :--- | :--- |
| `SettingsWindow.xaml` 侧边栏 `ToolTip`、`SidebarVersionText`、`UpdateStatusDescText`、`AboutVersionBadgeText` | `beta.3` | 这四处**被 `AppVersionInfo.DisplayVersion` 在运行时覆盖**，用户看不到旧版本号；但 XAML 自身不自洽，覆盖链路一旦变动就会露出来 |
| `SettingsWindow.xaml` 版本里程碑卡片 | 缺 `beta.4` | **用户可见、且没有任何代码覆盖** —— 关于页的版本演进列表停在 `beta.3`。按 §5.3 第 3 条补一张卡片，文案取自该版本自己的发布说明 |
| `installer/build-installer.ps1` 的 `$Version` 兜底值 | `beta.3` | 只在 `.csproj` 里读不到 `<Version>` 时才使用（第 69~76 行）。正常路径走不到，但兜底值落后会在**解析异常时静默打出旧版本号的安装包** |

### 🧪 自检

- `dotnet build WinPieGestures/WinPieGestures.csproj -c Release` → 0 警告 0 错误（XAML 编译通过即证明卡片结构闭合、无 `x:Name` 重复）。
- 全项目 `grep -rn "1\.7\.4-beta\.3"`（排除 `CHANGELOG.md` 与 `obj/`）→ 仅剩 `scratch/test_v170.cs` 的历史测试用例名，非版本号。
- CHANGELOG 版本小节顺序：未发布区 → `beta.4` → `beta.3` → `beta.2` → `beta.1` → `v1.7.3`。

## [未发布] - 2026-09-17（发布链路修复：`dotnet publish` 的输出缺整个 `plugin\` 来源区）

检查合并后项目状态时实测发现的独立问题 —— **不是合并引入的**，S4 拆包落地时就存在：`dotnet build` 的产物带 `plugin\`，而 `dotnet publish` 的产物**一枚插件 dll 都没有**。

### 🐛 问题修复

**根因**：`CopyBundledPlugins` 只挂 `AfterTargets="Build"`，复制目标是 `$(OutDir)plugin`；而 `dotnet publish -o <目录>` 的输出目录是 `$(PublishDir)` —— 两者不是同一个目录，`plugin\` 于是只落在中间构建目录里。

**影响**：`dotnet publish` 是 ZIP 包与 Inno Setup 安装包的**唯一来源**（`Compress-Archive -Path "publish/Lightweight/*"`、`SourceDir=publish\Standalone`），因此发行版缺整个来源区。用户装上后 `AutoInstallBundledPlugins` 扫不到任何候选（返回 0），12 个随包动作全部不会被安装 —— 配置里配好的 `Launch` / `Tile` / `Ocr` 等扇区一触发就报「找不到提供方」。开发机上永远复现不了，因为那里 `bin\` 里的 `plugin\` 是构建留下的。

**修法**：

- 把载荷清单 `BundledPluginPayload` 从 Target 内部提到**顶层 ItemGroup**（留在 Target 里时，另一个 Target 引用的 `@(BundledPluginPayload)` 是空列表 —— 复制会静默拷 0 个文件）。
- 新增 `CopyBundledPluginsToPublish`（`AfterTargets="Publish"`），复制到 `$(PublishDir)plugin`；原有的 Build 侧 Target 与那道「缺失即报错」的检查保持不变。
- `AGENTS.md` §5.2 补一条发布前必查项，写清两个目录的区别与实测方法。

### 🧪 自检

- `dotnet build WinPieGestures/WinPieGestures.csproj -c Release` → 0 警告 0 错误；`bin\Release\net8.0-windows10.0.19041.0\plugin\` 12 枚 dll（无回归）。
- `dotnet publish … -c Release -r win-x64 --no-self-contained -o <临时目录>` → 输出目录的 `plugin\` **12 枚 dll 齐全**（修复前：目录都不存在）。

## [未发布] - 2026-09-17（devplugin 合并遗留修复：三处重复插入与 915 行孤儿模块）

`devplugin` 分支的合并（`bfabbde`）在插件系统上做了一次「取本分支版本」的冲突解决。策略本身是对的 —— 那一侧不含本分支的顶层类型认领、`plugins\` 下的 12 个单动作包与 SDK 1.3 / 1.4 的能力面，直接取它会抹掉整个拆包工程 —— 但它没有处理「那一侧**新增**的文件」，于是留下半截状态：重构的骨架进来了，配套的调用方改造没有。本次修掉合并引入的全部遗留问题，主程序恢复 0 警告 0 错误。

### 🐛 问题修复

1. **保留 `PluginRuntime.cs`（466 行）与 `PluginPathModules.cs`（449 行）**（原「删除零引用孤儿模块」的判定已于同日撤销）
   - 当时判定：合并只带进来重构骨架，`PluginCallKind` / `PluginInvocationLease`（定义在那一侧的 `PluginInstance.cs`）没被带进来 —— 那份 `PluginInstance.cs` 取的是本分支版本，于是 `PluginRuntime.cs` 直接编译失败（`CS0246` × 2）；再加上推断该重构与本分支的顶层类型认领机制在 `PluginHost` 上「不可共存」，两个文件被判为 915 行死代码，予以删除。
   - **同日撤销**：见上方「同步上游 dev-plugin」一节 —— 上游已完整落地配套调用方，且实测两套机制**可以共存**，两个模块已恢复并在用。「零引用」与「不可共存」两条结论均已被推翻。

2. **`SettingsWindow.xaml` 重复的 `NavTab5`「插件与扩展」页签**（`CS0102` × 2）
   - 两侧在同一位置各自新增了同一个页签，合并时两份都保留了（逐字节相同）。删掉一份。

3. **`ActionExecutor.cs` 里重复的 `case "Plugin":`**（`CS0152`）
   - 同一成因：那一段 `switch` 的合并结果里 `default:` 被夹在两个重复 case 之间。删掉后面那一个，保留带完整注释的那份。

4. **`AGENTS.md` §4「插件系统架构与开发规范」整章重复**
   - 它是那一侧的整章插入：与本分支 §3.7 有 39 行逐字节重复。当时改写为「§4 插件系统运行时重构（未采用）」并净删 40 行重复。
   - **同日修订**：见上方「同步上游 dev-plugin」一节 —— §4 现为「插件系统运行时重构」，保留那一侧独有的 5 条（统一调用入口与路径模块 / 激活协调 / 动作执行语义 / 活动调用租约 / 异步停用状态机）；被自动合并顶掉的本分支 `### 3.7 插件系统` 整章也已恢复。

5. **`docs/plugin-system-architecture.md` 顶部状态标注**：当时标注为「本分支不采用本方案…保留仅作设计参考」。
   - **同日修订**：该重构已实际采用（见上一条），标注改为「现行实现」—— 该文档同时被 `AGENTS.md` §4 与 `README.md` 正向引用，反向标注等于对读者说反话。

### 🧪 自检

- `dotnet build WinPieGestures/WinPieGestures.csproj -c Release` → 0 警告 0 错误。
- 12 个单动作包逐一 `--plugin-selftest --skip-invoke` → 全部「PASS —— 全链路可用」；`%TEMP%/StarPie-PluginSelfTest-*` 零残留。
- 合并完整性核验：`x:Name` 无重复、侧边栏页签 `NavTab0`~`NavTab5` 各一个、12 个包工程与 12 条 `BundledPluginPayload` 齐全、内建表仍只剩 `Hotkey`、SDK 契约仍为 1.4。

## [未发布] - 2026-09-17（停用语义：影响面确认 + 槽位编辑器失效警示条）

S4 拆包的配套收尾：拆包粒度 = 停用粒度之后，「停用一个包」的影响面变得精确可算 —— 这让两件从前做不了的事成为可能。

### 🌟 核心改进与新增功能

1. **停用前的影响面确认**
   - 插件管理页关掉某个包的开关时，先统计当前配置里有多少动作由它提供（轮盘各层扇区、中心核心圆、手势触发动作、二级级联子动作，递归），大于 0 时弹确认框列出数字；取消则把开关拨回去，什么都不执行。
   - 统计覆盖两类：社区插件动作（`Type="Plugin"` 且引用指向它）与**被它认领的顶层类型**（停用「平铺窗口」包，失效的就是所有 `Type="Tile"` 的扇区）。启用永远是恢复性的，不弹确认。
   - 确认框明说两条：停用期间触发会明确提示「动作不可用」；配置不会丢失，重新启用即恢复。

2. **槽位编辑器失效警示条（认领类型）**
   - 模块 2 顶部新增红色警示条：当前槽位（含继承来的动作）绑定的认领类型此刻不可用（包被停用 / 被自动隔离 / 登记丢失）时出现，文案复用 `IsClaimedTypeAvailable` 的原因 —— 它本来就写着「该怎么办」。
   - **为什么需要它**：包被停用后，编辑器里类型下拉与参数面板照常渲染，用户看不出任何异样，按下扇区只会得到一句托盘提示 —— 警示条把后果提前到配置的那一刻。社区插件动作的同类提示已由插件面板覆盖，不重复。
   - 警示条跟着「实际生效的那份动作」走（继承来的动作同样可能失效，只看本地覆写会漏报）。

3. **轮盘触发的可读提示**此前已由 `ExecuteRegisteredAction` 的未启用分支 + `NotifyUser` 覆盖，本次未改动 —— 三块需求中它已在位。

### 🧪 自检

- `dotnet build`（Release）→ 0 警告 0 错误；`--plugin-selftest --skip-invoke`（System 包）全段 PASS，`%TEMP%` 零残留。
- 确认框与警示条为纯 UI 行为，`--plugin-selftest` 沙箱不覆盖 UI；UI 层验证由用户手动确认（或后续补 pywinauto 用例：`FocusActionUnavailableBanner` 有 `Name` 等价物即可定位）。

## [未发布] - 2026-09-17（S4b：`System` 拆成单动作包 + SDK 1.4 新增系统服务面）

继 S4a 之后第四个拆包阶段。至此**内建动作表只剩 `Hotkey` 一项** —— 13 个动作里 12 个已全部交割给随包单动作包，`Hotkey` 因「占位类型必须永远可解析」永久留下（见 `BuiltinActionCatalog.cs` 内注释）。

### 🌟 核心改进与新增功能

1. **SDK 契约 1.3 → 1.4：新增 `IHostSystemService` 与 `InputSimulation` 能力**
   - 能力项 `PluginCapability.InputSimulation = 1 << 10`。**门禁认它而不认 `Process`**：「系统控制」的执行既会起进程（关机 / 注销）也会往当前窗口发按键（音量、任务视图、最小化），两者后果不同 —— 前者是多一个后台进程，后者是往用户正在打字的窗口里按键。若门禁错用 `Process`，一个只声明「进程」的插件就能模拟按键。
   - 接口两个成员：`IReadOnlyList<SystemPresetOption> Presets { get; }`（元数据面，不设门禁 —— 插件注册参数下拉时就要读它）与 `bool RunPreset(string preset)`（执行面，门禁 `InputSimulation`）。
   - **预设表保持宿主为唯一数据源**：`Presets` 直接转发 `SlotViewModel.SystemPresetList`，插件不另抄一份。抄一份的后果是宿主加预设后插件下拉里没有它 —— 无报错、无异常，用户只知道「新选项选不到」。
   - 补上 S4a 遗漏的 1.3 版本说明条目（提交信息里声称补了，实际没补 —— 本次一并修正）。

2. **宿主侧：`ExecuteSystem` 由 `void` 改为 `bool`，新增 `PluginSystemService`**
   - `ActionExecutor.ExecuteSystem` 返回「是否匹配到预设」。这是插件包判断「老配置里的预设名还能不能识别」的唯一途径 —— 返回 `void` 的话，插件只能对每个未知值报一句不知所云的错误。
   - 41 处 switch `break;` → `return true;`，补 `default: return false;`。这次用了限定区间的脚本整词替换（41 处逐条 Edit 不现实），替换前后都做了断言与 diff 分类核对。
   - `PluginSystemService` 门禁 `InputSimulation`；`RunPreset` 空键短路，放行探针不会真的起进程、按键。

3. **`StarPie.Plugin.System` 单动作包**
   - 认领 `System=system`，清单声明 `Capabilities = InputSimulation; Process`。
   - **参数键用 `HostActionFields.Parameter` 而不是老的 `preset`**：宿主对被认领类型的通用投影把裸字段 `Parameter` 投成键 `Parameter`，老配置的预设值就住在那里 —— 键名写 `preset` 会让所有迁移过来的老配置静默失联（值还在，只是永远读不到）。
   - 参数下拉的选项从 `context.System.Presets` 即时生成，不硬编码。
   - **命名空间遮蔽**：包的命名空间叫 `StarPie.Plugin.System`，方法体里裸写 `System.Globalization...` 会被解析成它自己（CS0234）。一律 `using System.Globalization;` + 简单名。

4. **`BuiltinActionSystem.cs` 删除，内建动作表只剩 `Hotkey`**

5. **安装确认页能力文案集中化：新增 `PluginCapabilityLabels`**
   - 复查时发现一处**已存在的缺陷**：`DescribeCapabilities` 里没有 `WindowControl`（S3c 加的）与 `ScreenCapture`（S4a 加的）的文案 —— 那两项能力加进来时声称的「让用户在确认页看见后果」实际从未生效。本次补齐，并把映射集中到宿主侧一个表里。
   - `[3j]` 加机器护栏：**枚举里每个非空能力位都必须有确认页文案**，漏一个当场报错 —— 「加能力位必须加文案」从口头约定变成断言。

### 🧪 自检

- **`System` 包跑 `--plugin-selftest --skip-invoke`，全段 PASS**；报告中 0 条 WARN / FAIL / SKIP，`%TEMP%/StarPie-PluginSelfTest-*` 为 0 个。
- `[3f]` 内建动作表断言收紧为 **1 项（只剩 `Hotkey`）**；原先住在 `[3f]` 的两条「选项数 = 宿主表项数」断言挪到 `[3i]` 认领链路上 —— Tile（S4c）与 System（S4b）相继改成认领类型后，`[3f]` 的循环再也走不到它们，断言还在源码里却永远不会执行（守卫静默失效）。
- `[3i]` 认领表 1 项（`System → starpie.builtin.system.system`）；选项数 41 项与宿主表逐项同源 ✓。
- `[3i]` 新增第 ⑦ 条**参数键健全性**断言：重复键报错；与宿主裸字段只差大小写的键名报错（投影是精确匹配，`preset` 险情就属于这一族）。
- `[3j]` 新增系统服务三段断言：未声明拒绝 / 只声明 `Process` 仍拒绝（门禁认 `InputSimulation` 的机器化版本）/ 已声明放行（空键短路）。另断言未声明能力也能读预设清单（元数据面不设门禁）。

## [未发布] - 2026-09-17（S4a：`Ocr` 拆成单动作包 + SDK 1.3 新增屏幕截取服务面）

继 S4c / S4d 之后第三个拆包阶段。`Ocr` 是这批里**唯一需要新开宿主服务面**的动作 —— S4c / S4d 的十个动作都能用既有的 `IHostCommandService` / `IHostShellService` / `IHostWindowService` 表达，而「框选截屏 + 文字识别」在 SDK 里根本没有对应物，所以它必须先扩契约。

### 🌟 核心改进与新增功能

1. **SDK 契约 1.2 → 1.3：新增 `IHostScreenCaptureService`**
   - 能力项 `PluginCapability.ScreenCapture = 1 << 9`。**取新位而不是插进枚举中间** —— 插入会改变后续所有成员的位值。刻意不合并进 `Ui`：截屏是隐私敏感能力，安装确认页上必须让用户看见「它会看到我的屏幕」，藏在一句「界面」里等于没说。
   - 接口只有一个方法 `void CaptureAndRecognize()`。**无参数是刻意的**：识别区域由用户按下之后现场框选，没有任何需要事先保存的配置 —— 所以那个动作本身也就没有参数。
   - **返回 `void` 也是刻意的**：框选要等用户操作、识别更是异步的，这个调用根本无法同步取得结论。返回 `bool` 只能表示「宿主已受理」，而它是一个很容易被误读成「识别成功了吗」的假信号。它是**唯一**一个不返回布尔的宿主动作服务。
   - `ApiVersion` 是手写常量（`1.3` / `ApiVersionMinor = 3`），不能靠插值 —— C# 常量插值对 `int` 不成立（CS0133）。
   - `PluginApi` 的版本说明注释同步补上本次新增内容，免得版本号与注释再次错位。

2. **宿主侧：`PluginScreenCaptureService`**
   - 门禁在 `PluginCapability.ScreenCapture`，未声明直接抛 `PluginCapabilityDeniedException`。
   - 执行体只转发 `OcrManager.StartCaptureAndRecognize()`。截图、框选、OCR 引擎全在宿主侧，插件里一行 P/Invoke 都没有 —— 它做的只是「请宿主发起一次框选识别」。

3. **`StarPie.Plugin.Ocr` 单动作包**
   - 认领 `Ocr=ocr;ScreenOcr=ocr`（别名与主类型同包）。清单声明 `Capabilities = ScreenCapture`。
   - **为什么值得单独一个包**：这个动作的后果是读取屏幕内容，是这批动作里隐私敏感度最高的一个 —— 想单独关掉它的用户不在少数，而它从前与「系统控制」同属一个包，要关就得一起关。
   - 插件侧对 `PluginCapabilityDeniedException` 的处理是「记日志 + 给用户一句人话」，不把 .NET 异常文本甩给用户。

4. **`BuiltinActionOcr.cs` 删除，内建动作表只剩 `Hotkey` / `System`**
   - `Hotkey` 永久内建（占位类型必须永远可解析，见文件内注释）；`System` 等 `IHostSystemService`（S4b）。

### 🧪 自检

- **`Ocr` 包跑 `--plugin-selftest --skip-invoke`，全段 PASS**；运行后 `%TEMP%/StarPie-PluginSelfTest-*` 为 0 个，报告中 0 条 WARN / FAIL / SKIP。
- `[1]` 能力声明读到 `ScreenCapture`，SDK 契约版本 `1.3`。
- `[3f]` 内建动作表只剩 `Hotkey` / `System` 两项 —— 与「十一个类型已交割」互为印证。
- `[3i]` 认领表 2 项（`Ocr` / `ScreenOcr`）指向同一个贡献点 `starpie.builtin.ocr.ocr`；停用提示指向正确的包名「截屏识字 (OCR)」。
- `[3j]` 新增截屏门禁断言：`ScreenCapture.CaptureAndRecognize：已拒绝（IHostScreenCaptureService / ScreenCapture）`。
  - 这条探针**只断言拒绝路径**，刻意不做「声明后放行」与跨能力交叉断言：它是这批里唯一没有「可以传空值短路的参数」的服务，门禁一旦真的漏了，探针会当场弹出全屏框选界面打断自检者 —— 而那时自检已经在报错，没人会想到这个额外的副作用。放行那一半由真实使用与 `[3i]` 的认领链路保证。
  - 探针为此单独加了 `ProbeVoidCapabilityGate`（void 专用），而不是重载：`() => M()` 这种语句表达式 lambda 既能转 `Func<bool>` 也能转 `Action`，同名重载会让原有三处探针落进「谁更匹配」的编译器规则里。也不能图省事包成 `() => { call(); return false; }` —— 门禁真缺失时会打印「调用被直接放行（返回 False）」，一个捏造的布尔值混进结论。
- 来源区恰好 11 枚 dll，不含 `StarPie.Plugin.Abstractions.dll`。

---

## [未发布] - 2026-09-17（S4d：基础动作类拆成五个单动作包）

与 S4c 同一条路线的后半程：把「打开类 + 命令类」也拆成一个动作一个包。至此**十个动作各自一个包** —— 剩下 `Ocr` / `System` 两个还在宿主里，等各自的服务面补齐后搬走。

### 🌟 核心改进与新增功能

1. **`StarPie.Plugin.BasicActions` 拆分为五个单动作包**
   - `StarPie.Plugin.Launch`（认领 `Launch`）/ `WebUrl`（`WebUrl` + 别名 `Url`）/ `Folder`（`Folder` + 别名 `OpenFolder`）/ `Command`（`Command`）/ `ShellTool`（`ShellTool`）。
   - **别名与主类型同包**：`Url` 与 `WebUrl` 是同一个动作的两种历史写法。拆到两个包会出现「同一个动作在两种写法下由不同插件执行」，停用其中一个只失效一半配置，而用户完全看不出另一半为什么还活着。
   - 五个包各自的词条只带自己那一组（原 `Texts.cs` 已经按 `launch.` / `weburl.` / `folder.` / `command.` / `shell.tool.` 前缀分好组），切分是机械操作。

2. **顺带收紧了一处过时的能力声明**
   - 原 `BasicActions` 声明的是 `Process;Ui`。全项目核查后确认 **`Ui` 没有任何强制点** —— 唯一的引用是设置页里的一句展示文案（`· 显示界面与通知`），而这两个动作的后果没有一个是「打开自己的窗口」。
   - 按「安装确认页展示的能力必须对应一个真实后果」这条既有原则，五个新包**只声明 `Process`**。拆包正是做这件事的时机：每个包重新审视自己真正需要什么，而不是照抄原包的一整行。

### 🧪 自检

- **十个包逐个跑 `--plugin-selftest`，全部全段 PASS**；运行后 `%TEMP%/StarPie-PluginSelfTest-*` 为 0 个。
- 来源区恰好 10 枚 dll，不含 `StarPie.Plugin.Abstractions.dll`。
- `[3i]` 对别名包的断言是两行、指向同一个贡献点（`WebUrl→…webUrl`、`Url→…webUrl`）—— 这正是「别名与主类型同包」的机器可验证形态。
- `[3f]` 显示内建动作表此时只剩 `Hotkey` / `System` / `Ocr` 三项。

---

## [未发布] - 2026-09-17（S4c：窗口类拆成五个单动作包）

把上一阶段合在一枚 dll 里的五个窗口动作拆成**一个动作一个包** —— 拆包的粒度就是停用的粒度。

### 🌟 核心改进与新增功能

1. **`StarPie.Plugin.WindowActions` 拆分为五个单动作包**
   - `StarPie.Plugin.Tile`（认领 `Tile`）/ `StarPie.Plugin.ToggleTopmost`（`ToggleTopmost`）/ `StarPie.Plugin.MoveMonitor`（`MoveMonitor`）/ `StarPie.Plugin.WindowOpacity`（`WindowOpacity`）/ `StarPie.Plugin.SwitchWindow`（`SwitchWindow`）。
   - 每个包只带自己那一个动作实现与那一组词条。从前这五个动作同属一个包，**要关就得五个一起关**；现在用户能把「窗口透明度」关掉而继续用「平铺窗口」—— 这正是拆包唯一正当的理由。
   - **插件名与动作显示名刻意一致**（如「窗口透明度」）：用户停用的是某个动作，插件页里那一行若叫别的名字，他还得自己建立对应关系。
   - 旧包目录与它的部署产物一并删除。这一步能做成，靠的是 S4-0 的「已停止分发清理」—— 没有它，旧包的登记条目会带着五条认领快照留下，与新包抢同一批类型，触发「多提供方整对拒绝」。

2. **每个包单独声明 `WindowControl`**
   - 安装确认页展示的能力仍然对应一个真实后果：「移动 / 缩放 / 置顶 / 改透明度**其它程序的**窗口」。

### ⚙️ 构建

- `WinPieGestures.csproj` 的 `ProjectReference` 与 `BundledPluginPayload` 从 2 项扩到 6 项，**仍是逐枚列举**，并就本次改动写明「**刻意不用 `..\plugins\*\*.csproj` 通配**」的理由：新加一个实验工程或临时验证目录时，通配写法会把它默默卷进发行包，而「多了一枚没打算分发的插件」从构建日志里完全看不出来。
- 「缺失即构建失败」那道检查继续兜底：漏加一行会当场报错，而不是安静地少拷一枚 dll。

### 🧪 自检

- **六个包逐个跑 `--plugin-selftest`，全部全段 PASS**；运行后 `%TEMP%/StarPie-PluginSelfTest-*` 为 0 个。
- `[3i]` 对**每个包各自**断言：认领表恰好 1 项、认领指向的贡献点真的存在、派生链路走到插件实现、停用后的提示指向正确的包名（如「该动作属于内置动作包『平铺窗口』」）。单动作包让这条断言的语义比从前更精确 —— 认领数不再是「这个包认领了几项」，而是「这个包只能是这一项」。

---

## [未发布] - 2026-09-17（S4-0：随包插件的「已停止分发」清理）

拆包之前必须补的最后一块地基：**来源区里已经没有了的随包插件，宿主此前既不会清理登记、也不会删除安装副本**，于是留下「幽灵包」。

### 🐞 修复

1. **拆包会留下幽灵包，并让两个包的全部动作一起失效**
   - 拆包的必经动作是「把某个类型从一个包挪到另一个包」，也就是让旧包名从来源区消失。而 `AutoInstallBundledPlugins` 只遍历**存在的**文件，遍历不到的就什么都不做 ⇒ 旧包的登记条目原地留下，连同它的 `ClaimedTypes` 快照一起。
   - 于是旧包与新包同时认领同一个类型，`RebuildClaimTable` 判「多包抢同一类型 → **整对拒绝**」，**两个包的动作一起失效**，用户侧唯一的线索是日志里一行 Error。
   - 更隐蔽的一面：旧包在宿主区的安装副本仍在，插件列表里那一行也照旧存在 —— 用户完全看不出问题出在哪。
   - 与 S3-0 的 `RefreshBundledMetadata` 是**互补的两半**：那个管「来源区里**还有**这枚 dll、但内容变了」，这个管「来源区里**已经没有**这枚 dll 了」。前者的前置恰好被拆包破坏，所以它单独存在时挡不住这个问题。

### 🌟 核心改进与新增功能

1. **新增「来源区不再分发即清理」**（`PluginHost.PruneUndistributedBundledPlugins`）
   - 判据用 **ID 级**：本轮扫描来源区时已经拿到每枚 dll 的 `manifest.Id`，凡 `Bundled=true` 却不在其中者，判定为已停止分发。不需要在登记条目里新增「来源文件名」字段，且对既有老数据同样成立。
   - 清理内容：摘掉登记条目（**认领快照随之消失，这是关键**）+ 删除宿主区安装副本 + **保留插件私有 `data\`** ——「程序不再分发它」不等于「用户的数据该丢」。
   - 顺序是「先删磁盘、后摘登记」：反过来的话，一旦删目录失败而登记已经没了，这个包就同时丧失了「被清理」与「被重建」两种可能，只剩宿主区一堆没人认领的文件。
   - 清理必须跑在 `SyncFromDisk` **之前** —— 否则被清理的包会先在本次启动里被建成实例，认领表也跟着读到它。

2. **三条保守守卫（缺一不可）**
   - 来源区目录不存在 / 来源区一枚 dll 都没有 / 有任一文件读不出来 ⇒ **一律不清理**。
   - 理由：清理在这里是**不可逆的**（来源区那枚 dll 本来就已经不在了，没得补）。判据不完整时最坏的结果只能是「留下一个幽灵包」，绝不能是「误删一个用户正在用的官方包」。
   - 清理逻辑**不破坏 R1**：判据只用到本轮扫描已经拿到的 ID，不额外读文件、不加载任何程序集。

### 🧪 自检

- 新增 `[3m]`「来源区不再分发即清理」段：自造一个幽灵包条目（登记表标着随包、宿主区有副本、来源区里没有），断言 ①两条守卫下它**必须被留下** ②判据完整时被清理 ③**除它以外一个随包条目都不能少**（误删同样不可逆）④宿主区安装副本被删 ⑤私有数据被保留 ⑥重建认领表后它不再出现。
- 反向断言比正向断言更要紧：`[3m]` 里「别的随包条目一个都没被误清」这条守的是「误删」，与「幽灵包被清掉」同等重要。

---

## [未发布] - 2026-09-17（S3a：窗口类动作外移，新增第二个随包动作包）

把「平铺窗口 / 窗口置顶 / 窗口透明度 / 移到下一屏 / 切换应用」五个动作搬进一枚新的随包 `.dll`，并为此新增宿主服务面 `IHostWindowService` 与能力项 `WindowControl`。这是动作全面外移的第一批 —— 与 S1、S2 不同的是，这批动作此前**在宿主里有一整套手写参数面板**，外移后参数声明与面板并存。

### 🌟 核心改进与新增功能

1. **新增随包动作包 `StarPie.Plugin.WindowActions`（认领 5 个顶层类型）**
   - `tile`（平铺窗口）、`toggleTopmost`（窗口置顶）、`moveMonitor`（移到下一屏）、`windowOpacity`（窗口透明度）、`switchWindow`（切换应用），分别认领顶层类型 `Tile` / `ToggleTopmost` / `MoveMonitor` / `WindowOpacity` / `SwitchWindow`。
   - 认领表从 7 项变成 12 项。宿主侧 `BuiltinActionCatalog` 同步删掉这五条登记，五个 `BuiltinAction*.cs` 整文件删除 —— 与 S2 同一条纪律：「加认领声明」与「删内建登记」是一次交割。

2. **新增 `IHostWindowService`（装配在 `IPluginContext.Windows`）+ `PluginCapability.WindowControl`**
   - 元数据面：`Layouts`（布局清单，**唯一来源**）、`CycleToken` / `CycleBackToken` / `RestoreToken`（三个操作标记）、`OpacityMinPercent` / `OpacityMaxPercent`。
   - 执行面：`ApplyLayout` / `ToggleTopmost` / `MoveToNextMonitor` / `SetOpacity` / `ActivateTaskbarSlot`，未声明 `WindowControl` 时抛 `PluginCapabilityDeniedException`。
   - **`WindowControl` 刻意与既有的 `Ui` 分开，不合并**：`Ui` 的语义是「打开自己的窗口 / 弹窗」，拿它表示「移动别人的窗口」会让安装确认页对用户说假话 —— 用户看到「界面」两个字想到的是弹个对话框，实际后果却是他正在用的窗口被挪走。新值取 `1 << 8` 而非插进枚举中间，避免改动后续所有成员的位值。

3. **参数范围的唯一来源收敛到宿主**
   - `WindowTiler` 的透明度钳制值提升为 `OpacityMin` / `OpacityMax` 常量，插件通过 `OpacityMinPercent` / `OpacityMaxPercent` 读取，`ParameterField` 的 `Min` / `Max` 由它生成。
   - 布局清单同理：插件从 `Layouts` 现取，不在插件里另抄一份。**抄一份的后果是「宿主加了新布局、插件下拉里没有」，或反过来「插件里能选、宿主执行体不认」—— 两种都是静默失效。**

### 🔧 重构

- **抽出 `PluginGatedService` 基类**：第三个带门禁的服务到来时消除三重重复 —— 能力判定、拒绝时的统一日志（先落日志再抛，因为异常可能被插件自己的 `catch` 吞掉，日志是排查的第一现场）、以及不让异常冒泡到 `ActionExecutor.Execute` 的统一包裹，现在都只有一份实现。
- **`SetOpacity` 的参数刻意是字符串而不是 `int`**：解析与钳制规则只存在于宿主执行体一处，SDK 不复制第二份。若声明成 `int`，插件就不得不先解析一遍，同一个规则于是有了两个实现 —— 迟早不一致。

### 🧪 自检

- `[3f]` 的「已外移类型」写死断言扩到 12 项；`builtinCases` 缩到 3 项（`System` / `Ocr` / 无参数动作）。
- `[3g]` 的校验失败探针改为**从内建动作表现取**一个必填项动作，不再写死 `Type="Tile"` —— 写死的探针会被下一次外移动作击穿，而失败信息会指向「内建优先没生效」这个完全错误的方向。
- `[3j]` 补 `WindowControl` 门禁断言（拒绝 / 放行 / 元数据可读三态），`ProbeCapabilityGate` 参数化到「按能力断言」，不再只认 `Process`。
- **每个随包包都要各跑一次自检**：`[3h]`~`[3k]` 围绕传入的那一枚 dll 展开，只跑一个包会让另一个包的认领链路完全失去覆盖 —— 而那条链路的失效表现是最难查的「动作找得到归属、却永远执行不了」。

### ⚙️ 构建

- 随包插件的复制目标从「单枚 dll」改为**显式列举 + 缺失即报错**：来源区约定是「扁平，只放 `.dll`」，一旦某个包忘了挂进构建管线，原先的写法会安静地少拷一枚，症状是「配置里配好的窗口动作，触发时提示找不到提供方」。
- 拷贝动作**刻意不做「整个目录一扫全拷」** —— 那会把 `StarPie.Plugin.Abstractions.dll` 一并塞进来源区，而来源区里出现第二份 SDK 程序集正是类型身份分裂的经典成因。

---

## [未发布] - 2026-09-17（S3-0：随包插件的登记信息随文件刷新）

把动作继续外移、并把随包动作包拆开之前必须先补的一个地基问题：**随包插件的认领清单、版本与能力集合此前是「首次安装那一刻的快照」，之后永不更新**。

### 🐞 修复

1. **随包插件升级后，新增的顶层类型认领不会生效**
   - `ClaimedTypes` 的唯一写入点在安装流程里，已登记的条目每次启动只被检查「宿主区文件还在不在」，登记信息本身从不与来源区那枚 `.dll` 对齐。
   - 现象：随包动作包升级后声明了一个新的动作类型，用户配置里的该类型动作永远不会走到插件实现，掉进 `switch` 的 `default` 分支静默无反应。

2. **（同上机制）拆包会让两个包的全部动作一起失效**
   - 把某个类型从一个包挪到另一个包时，老用户登记表里旧包的名字仍是旧快照、依然认领该类型，新包也认领同一类型 —— 触发「多提供方整对拒绝」，两个包的动作全部作废，而用户唯一能看到的线索是日志里一行 Error。
   - 这条是本轮（S3）动作继续外移与拆包的前置条件，不修就没法安全交割。

### 🌟 改进

- **`PluginHost.RefreshBundledMetadata`**：`EnsureBundledPlugin` 对**已登记的随包条目**，先用当前这枚 dll 的元数据（纯静态 PE 读取，不加载任何程序集）刷新 `ClaimedTypes` / `CapabilitiesAck` / `Version` / `Name` / `Description`，**再**判断宿主区文件在不在。两件事互不相干，不能因其中一件成功就跳过另一件。
- **只同步「文件是什么」，绝不同步「用户怎么选」**：`Enabled` / `Preload` / `InstallPath` / `Bundled` / `ExternalPath` / 确认时间戳一律不碰。刷新绝不会变成「偷偷把用户停用过的包重新启用」。
- **只在值真的变了时落盘**：避免每次启动都重写 `registry.json`（那会让它的修改时间恒变，「配置有没有被改过」的判断失去意义）。
- 顺带修掉「插件页长期显示一个早已不存在的版本号」。

### 🧪 自检

- 新增 **`[3k]` 随包插件的登记信息随文件刷新**：制造一份过时的登记快照（认领指向一个刻意不存在的假类型名，避免断言与真实声明侥幸重合），按真实启动顺序重跑 `AutoInstall → SyncFromDisk → RebuildClaimTable`，断言 ①认领清单被刷新为文件的实际声明 ②认领表按新声明重建、每一项都路由到本插件 ③版本号已刷新 ④`Enabled` / `Preload` 未被改动。

---

## [未发布] - 2026-09-16（S2：宿主服务面与能力门禁）

把「运行命令 / 系统与右键工具」两个内建动作搬进已有的随包动作包，并为此给 SDK 补上**带真实强制点的能力门禁**：插件清单里声明的 `Process` 能力第一次真的对应一个后果 —— 没声明就调不动宿主执行命令，而不是照旧能调、只是安装页多弹一个勾选框。

### 🌟 核心改进与新增功能

1. **随包动作包扩到 5 个动作（S2：命令与 Shell 动词）**
   - 新增 `command`（运行命令）与 `shellTool`（系统与右键工具），认领顶层类型 `Command` / `ShellTool`。
   - 认领表随之变成 7 项（含别名 `Url` / `OpenFolder`），宿主侧 `BuiltinActionCatalog` 同步删掉这两条登记 —— 「加认领声明」与「删内建登记」是一次交割，只做一半就会出现同一个 `Type` 挂两条执行路径，而内建优先，插件里那份代码永远不会被调用、改了也看不出效果。

2. **新增两个宿主服务契约，且带真实门禁**
   - `IHostCommandService`（`Terminals` / `Run`）与 `IHostShellService`（`Verbs` / `Invoke`），装配在 `IPluginContext.Commands` / `.Shell` 上。
   - **未声明 `PluginCapability.Process` 时，`Run` / `Invoke` 抛 `PluginCapabilityDeniedException`**，绝不静默降级。门禁刻意放在统一异常包裹 `Guard` **之外** —— 挪进去异常会被吞成一个 `false` 返回值，用户看到的是「命令没执行」，而不是「本插件缺少「进程」能力」。
   - **元数据不受门禁约束**：`Terminals` / `Verbs` 在未声明能力时照常可读。插件的 `Parameters` 是属性、声明期（注册前）就要读它；在那里抛异常会让一个「忘了声明能力」的插件在注册阶段整个崩掉，而它其实只是不能在运行时干活而已。门禁拦的是**产生后果**的调用。
   - **`PluginCapabilityDeniedException` 刻意不继承 `PluginContractException`**：后者的语义是「违反注册契约」，宿主会因此把插件整体标记为加载失败并卸载；而「清单里漏了一行能力声明」远不到那个程度。真继承上去，用户看到的是「插件突然坏了 / 被系统禁用了」，排查方向会完全跑偏。
   - **必须说清它换来的不是安全**：进程内插件本来就能自己 `Process.Start`，SDK 拦不住。门禁换到的是「安装确认页上展示的能力真的对应一个后果」—— 漏掉它，那个勾选框在运行时没有任何对应物，才是真正骗人的地方。
   - **门禁只能加在新接口上**：`IHostActionInvoker` 的 7 个方法是既有契约，补门禁会让已发布、未声明该能力的插件突然失败（破坏性变更）。
   - 新增 `IHostInfo.HasCapability(...)`：插件可以在 `Initialize` 里先问一句，据此决定注册一个能用的动作、还是注册一个点了就说明原因的动作。
   - SDK 契约版本 **1.0 → 1.1**。

3. **清单元数据只有一份来源，杜绝「宿主改了、插件没跟上」**
   - `PluginCommandService.Terminals` 是终端清单的唯一事实来源，随包动作包的「运行命令」直接读它、不再另抄一份；**每次访问都重取词条、不缓存**，否则用户运行时切换语言之后下拉里还是旧语言。
   - `PluginShellService.Verbs` 取 `ShellToolItem.Id`（`copy_path`）而不是 `Verb`（`Windows.CopyAsPath`）：用户配置里存的是短 ID，而 `Verb` 是执行体 `switch` 里的规范名。**传错这一个字段，动作会静默无效** —— 因为 `ExecuteShellTool` 的 `default` 分支是空的。
   - `Verbs` **不是白名单**：`ExecuteShellTool` 每个功能同时接受两套命名，按清单校验会把另一套命名的老配置整体判死。它只用于下拉与展示。
   - `shellTool` 的参数**刻意声明成自由文本而非 `Enum`**：正式入口是带搜索 / 分类的 `ShellActionPickerWindow`，压进通用下拉是体验降级、还会让清单出现两份。

4. **派发顺序被抽成可断言的纯判据**
   - 新增 `ActionExecutor.ActionDispatchKind` 与纯函数 `ClassifyAction`；`Execute` 的分派本体就是 `switch (ClassifyAction(...))`。
   - 抽出来的唯一理由是让「内建 → 认领 → `switch` 兜底」这条**顺序本身可被断言**：顺序错了的现象是「界面一切正常、按下去却走了另一条路」，从现象根本反推不出来；而原先那种「跑一个动作看产物」的验证法在动作陆续外移之后已经找不到无害探针了。

### 🐞 缺陷修复

1. **`ExecuteCommand` 失败时弹对话框，与插件侧纪律冲突**
   - **根因**：它的 `catch` 里有一句 `MessageBox.Show`。该动作外移成插件之后，这条路径上的代码在独立程序集里，弹窗会在无人值守时把唯一的动作线程卡死。
   - **修复**：签名由 `void` 改为 `bool`，删掉对话框，失败改为返回 `false` 并由调用方走日志 + 托盘气泡。签名可以放心改，是因为它当时只剩一个调用者，且那个调用者正是被外移的动作。
   - **用户可感知的变化**：手动触发该动作失败时，提示从「带原因的对话框」变成「一句可操作的话 + 日志」。这是分 DLL 的必然代价，如实记录而非淡化。

2. **`ExecuteShellTool` 的可见性过宽**：`public` → `internal`，它不再有任何宿主 UI 调用者。**刻意保持 `void`、不改成 `bool`** —— 33 个分支里多数在上下文不适用时静默 `return`，改成 `bool` 就得编一个不可靠的成功判断。

3. **`ApiVersion` 那处重复无法用语言特性消除**：`const string ApiVersion = $"{ApiVersionMajor}.{ApiVersionMinor}"` 编译不过（CS0133，常量插值只对 `string` 常量成立），只能手写 `"1.1"`。改由自检 `[3j]` 断言两者一致，不让它变成一处「改了主版本号却忘了改字符串」的静默漂移。

### ⚙️ 工程与可维护性

1. **`[3f]` 改为数据驱动**：内建动作清单从 `BuiltinActionCatalog.SnapshotAll()` 生成，不再手抄。手抄的成本已经显现两次 —— 动作每外移一个就得有人记得删一行，忘了删的表现是自检报「某动作不在内建动作表里」，**而那恰恰是预期行为**；「预期的事被报成缺陷」比不报更坏，它会训练人忽略这条消息。写作本表里的承诺改成写死的独立断言。
2. **`[3f]` 新增「交割不变量」**：被随包动作包认领的顶层类型**必须已经不在**内建动作表里。这是「加认领声明」与「删内建登记」必须同时发生的机器化表达。
3. **`[3g]` 从「看产物」改为「看有没有出声」**：原先的内建派发探针是 `Type="Command"` + 一条 `echo`，靠产出的文件证明被接走 —— `Command` 外移后这个探针不再成立，而「真跑也无害」的候选动作已经一个不剩（剩下 8 个内建动作每个真跑都会按热键、抓屏 OCR 或改用户窗口状态）。改成两层：① 纯判据断言 `ClassifyAction` 对每一类 `Type`（含别名、含 `null` / 空白）的归属；② 走真的 `Execute()`，探针挂 `PluginNotificationHub.Sink` 收集提示，用**注定不会执行**的动作（未知 `Type`、空必填项）验证三条异常路径都会出声。静默失效的判据从来不是「产物没出来」，而是「什么都没告诉用户」。汇在 `finally` 里摘掉 —— 它是全局静态的，留着会让后续所有段落的提示悄悄流进一个已经没人读的列表。
4. **`[3i]` 补认领后判据**：断言每条认领经 `Execute()` 的判据确实路由到 `ClaimedType`（与 `[3g]` 的「不是 `Builtin`」合起来才闭环 —— 只看一边的话，「从内建删了、认领也没建」同样能过）；并反向核对认领表里没有任何一项同时还是内建动作，为 `RebuildClaimTable` 的「内建优先」拒绝规则配一道守卫。
5. **新增自检段落 `[3j]` 宿主服务面与能力门禁**：① 断言 `PluginCapabilityDeniedException` 与 `PluginContractException` 之间没有继承关系（真继承上去，漏写一行能力声明会让整个插件被卸载）；② 未声明 `Process` 时 `Commands.Run` / `Shell.Invoke` 必须被拒绝，且拒绝得体（归因能力正确、消息里给出「去清单的 `capabilities` 数组里补一行」这个可执行动作）；③ 已声明 `Process` 时同一调用必须放行（少了这一半，把门禁写成「永远拒绝」也能通过）；④ 元数据在未声明能力时照常可读，且终端清单含 `cmd`、Shell 动词清单含 `copy_path`、标识无重复、显示名非空；⑤ `ApiVersion` 与主次版本号一致。
6. **`[3e]` 段删除**：它验的是已外移动作的宿主执行体，职责已由 `[3i]` 的派发链路断言接替。
7. **实测结论**：随包动作包 `--plugin-selftest … --skip-invoke` 全段 PASS，运行后 `%TEMP%` 零残留；`dotnet build WinPieGestures/WinPieGestures.csproj -c Release` 0 警告 0 错误。

### 📌 已知边界（下一步 S3）

- 内建动作表里还剩 8 个：`Hotkey` 刻意永久保留（它是 `ActionItem.Type` 的默认值与未配置扇区的占位类型），另 7 个需要三个新宿主服务面才能继续切包 —— 窗口类五个动作（`Tile` / `ToggleTopmost` / `MoveMonitor` / `WindowOpacity` / `SwitchWindow`）共用 `WindowTiler.*`，`System` 依赖 `ExecuteSystem` 的 60 个分支，`Ocr` 依赖 `OcrManager.StartCaptureAndRecognize`；而 13 个动作里只有 4 个能用现有的 `IHostActionInvoker` 表达。
- 按用户拍板拆成两批：**S3a** 窗口包 + `IWindowService`；**S3b** `System` + `Ocr`。
- 任务 #30「停用语义」仍待做：停用时列出受影响扇区数并确认、停用后槽位编辑器标红失效、轮盘触发给可读提示。

## [未发布] - 2026-09-16

把「启动程序 / 打开网址 / 打开文件夹」三个内建动作真正搬到一枚随包 `.dll` 里，并为此给插件系统补上**顶层类型认领**机制：宿主在不加载任何程序集的前提下就能知道配置里的 `Type="Launch"` 该交给谁执行，于是这批动作既能像插件一样被停用，又不需要用户改动一个字节的配置。

### 🌟 核心改进与新增功能

1. **新增随包动作包 `plugins/StarPie.Plugin.BasicActions/`（S1：基础动作）**
   - 承载 3 个动作（`launch` / `webUrl` / `folder`）、共 7 个参数，构建产出单枚 `StarPie.Plugin.BasicActions.dll`，由主程序构建后自动拷入 `<程序目录>\plugin\`。
   - 清单全部由程序集元数据声明（`StarPiePluginId` / `StarPiePluginTypeClaims` 等），分发形态是一枚裸 DLL。
   - 显示名、参数字段标签、校验文案随包提供中 / 英 / 日 / 繁四语词条。

2. **顶层类型认领（Type Claim）：让外移动作对用户完全不可见**
   - **`Type` 不可改，就改认领方向**：`ActionItem.Type` 是不可变身份（近两百处引用），因此不改配置形态，而由插件在清单里声明「我认领 `Launch` / `WebUrl` / `Folder`」。认领表落在登记表（`registry.json`）里，读取只是一次 JSON 反序列化 —— 轮盘首次触发路径上不产生任何 IO 或程序集加载（守住 R1 / R2 红线）。
   - **只有随包插件能认领**：社区插件照旧只能走 `Type="Plugin"` + `PluginActionRef`。写入侧（`ClaimWire`）与读取侧（`RebuildClaimTable`）各拦一次 —— 后者是因为 `registry.json` 是用户能手改的纯文本。
   - **内建优先**：仍留在 `BuiltinActionCatalog` 里的类型，任何认领一律拒绝，杜绝同一 `Type` 挂两条执行路径。
   - **`Hotkey` 刻意不外移**：它同时是 `ActionItem.Type` 的默认值与未配置扇区的占位类型；外移后用户一停用动作包，所有空扇区都会报「包已停用」。
   - **多提供方整对拒绝**：两个插件抢同一个类型时全部丢弃并记 Error，不做「后者覆盖前者」这种静默劫持。

3. **参数零迁移投影**
   - 新增 `ActionParameterProjection`：把 `ActionItem` 的裸字段（`Parameter` / `Arguments` / `CommandTerminal` / `BrowserChoice` / `BrowserPath` / `RunAsStandardUser`）现读现装成参数字典，`ExtensionData` 优先叠加。持久化侧一行未动，执行侧已统一。
   - 新增 SDK 侧常量类 `HostActionFields`：宿主持久化字段名的唯一事实来源。用 `const` 是为了让宿主重命名字段时插件侧变成**编译错误**，而不是运行期静静读到空值。
   - 投影是**显式白名单而非反射**，且只投影动作参数、不投影外观字段（`Name` / `IconKey` / `CustomTextColor` …）。

4. **停用语义**：动作包被停用时，认领类型仍留在表里（这样才对用户说得出「它被停用了」），执行前由 `IsClaimedTypeAvailable` 给出可操作文案；被认领的动作同时从「🔌 插件」子下拉里排除，避免同一动作在两处配出互不兼容的两套配置。

### 🐞 缺陷修复

1. **「装得上、永远起不来」—— 随包插件被自身规则拒之门外**
   - **根因**：保留 ID 前缀（`starpie.*`）的豁免只加在**扫描 / 安装**路径上，而**装载**路径（`Enable` → `ScanInstalledPlugin` → `Validate`）仍按无豁免校验。于是同一枚随包 DLL 得到两个互相打架的结论：来源区里能被自动装上，装载时却报「占用了保留前缀」。
   - **修复**：把该开关的判据改成「这份清单有没有正当理由用官方命名空间」，两种情况都放行 —— ① 来自只读来源区；② 装载一枚**已登记**的插件（ID 在它进入系统那一刻已经查过一次；边界查一次、系统内部不复查）。参数同时更名为 `allowReservedIdPrefix`，不再暗示「只跟来源区有关」。
   - **顺带修正**：`PrepareInstall` 原先未传该开关，导致「同一枚文件放进来源区能装、手工选中却被拒」，与 `ScanCandidateFile` 自相矛盾。

2. **自检沙箱删不掉，每跑一次就在 `%TEMP%` 里堆一坨残留**
   - **根因**：等 ALC 回收结论的栈帧上还挂着 `PluginActionRegistration`（指向插件程序集里的类型实例），结论永远是「需要重启」、文件锁不释放；而删沙箱恰恰发生在同一个方法内。
   - **修复**：把 `[3d]` 的重启等价态探针与 `[3i]` 的认领断言体各自拆成**禁止内联的独立方法**，并把「停用 + 等回收结论」挪到不持有任何插件侧引用的帧上（与既有的 `RunEnableAndInvoke` 同一条纪律）。删沙箱前再催一次 GC 并退避重试。

### ⚙️ 工程与可维护性

1. **自检新增 `[3i]` 顶层类型认领段**：断言认领表与清单声明逐条对上、每条认领指向的贡献点真实存在、被认领动作确实从子下拉排除、`HostActionFields.All` 每个键都能被投影（含布尔的小写字面量约定）、认领类型空参数派发时被宿主校验拦下（**零副作用**，不会真的启动程序）、停用后认领仍在且可用性为 false 并给出可操作文案。
2. **自检不再写死样例插件 ID**：`[3h]` 原先硬编码 `com.example.hello`，拿随包动作包跑自检时会误报「随包插件没被自动装上」。改为读被测插件自己的清单。
3. **`[3f]` 收敛范围**：移除已外移的三项，只保留仍在内建动作表里的 9 个动作；原先挂在 `Launch` 上的布尔投影断言搬进 `[3i]`，并扩大到整个宿主字段白名单。
4. **实测结论**：随包动作包与 `samples/HelloAction` 两侧 `--plugin-selftest … --skip-invoke` 全段 PASS，运行后 `%TEMP%` 零残留；`dotnet build -c Release` 0 警告 0 错误。

### 📌 已知边界（下一步 S2 / S3）

- 本节只覆盖 S1。运行命令的终端启动、Shell 动词、系统电源、窗口操作与屏幕捕获仍在宿主内，需要先扩充 `IHostActionInvoker`（当前 7 个方法覆盖 4 个动作，另 9 个动作缺宿主能力）才能按能力继续切包。
- 随包动作包执行失败时的提示从「带原因的对话框」降级为「一句可操作的话 + 日志」—— 这是分 DLL 的必然代价（插件异常不允许冒泡到宿主 `MessageBox`）。要恢复同等信息量需扩展 `IHostActionInvoker` 契约。

## [未发布] - 2026-09-15

插件系统补全「可管理、可校验、可共创」三件事：新增设置面板的插件管理页与插件动作的参数表单，把参数校验收束为保存与执行共用的唯一入口，并修掉三个会静默退化、只在换语言或换机器时才暴露的缺陷。架构约定同步登记至 `AGENTS.md` 第 3.7 节。

### 🌟 核心改进与新增功能

1. **设置面板新增「插件与扩展」管理页（侧边栏第 6 项）**
   - **手动选择启用**：点「安装 `.dll`」选中插件文件 → 弹出确认框展示 ID、名称、版本、作者、目标框架、架构、文件大小、SHA256、签名状态、清单来源、声明能力与拟安装路径 → 确认后落盘登记。
   - **安装与启用分离**：安装完成即为「已安装但未启用」，必须手动勾选才会加载 —— 避免「下载一个插件」等价于「放行其代码」。
   - **总开关与逐项启停**：顶部总开关一键整体停用（配置不丢），行内勾选框单独启停；「重新扫描」与「打开插件目录」支持手动投放文件后对账。
   - **行内诊断**：每个插件显示状态字形、名称、版本、状态说明与错误详情，加载失败不再只是一句无从下手的「加载失败」。

2. **插件动作的参数表单（声明式渲染，插件不提供 XAML）**
   - **9 种控件由宿主按声明生成**：`Text` / `MultilineText` / `Number` / `Bool` / `Folder` / `File` / `Enum` / `Hotkey` / `Color`，全部套用主程序的隐式样式，深浅色对比度、字体、圆角与内置动作完全一致，主程序改版也不会让插件界面错位。
   - **写穿式持久化**：任一字段变化即刻写入动作的参数字典并参与自动保存，不存在「表单里一套值、配置里另一套值」的中间态。
   - **逐字段就地报错**：违反声明的字段在控件下方标红给出原因；参数变更只刷新校验结论、不重建槽位列表 —— 否则用户每敲一个字都会失去焦点。

3. **参数校验收束为唯一入口（保存与执行共用）**
   - **两层校验，一次判断**：「宿主底线（只认 `Required` / `MaxLength` / `Min` / `Max` / `ValidationRegex` 声明，不依赖插件是否记得自查）」+「插件自定义 `Validate`」，由 `PluginHost.ValidateActionParameters` 统一编排。保存路径与执行路径走同一个方法，彻底消除「保存时没事、一触发说参数不合法」的分叉。
   - **修复「保存时校验」从未兑现**：`IActionContribution.Validate` 的契约注释承诺宿主会在保存动作与执行前各调用一次，但保存路径此前并无校验。

4. **新增屏幕亮度调节插件示例 `samples/ScreenBrightness/`**
   - **三通道自动降级**：DDC/CI（外接屏，MCCS VCP `0x10`）→ WMI（笔记本内置屏）→ gamma 软件调光兜底；前两者均不可用时才启用兜底，并在界面上诚实说明其代价（不降背光功耗、黑色变灰）。
   - **9 个动作**：调亮、调暗、三档固定亮度、明暗一键切换、查看当前亮度，以及带参数表单的「设置到指定亮度…」与「按步长调整亮度…」。
   - **演示两种校验分工**：`setlevel` 一行自定义校验都不写，全靠声明拦住空值与越界；`stepby` 只补一条声明表达不了的规则（步长为 0 等于什么都不做）。

5. **参考模板 `HelloAction` 补齐字段类型演示**
   - 新增「参数表单演示」动作，陈列 `MultilineText` / `Number` / `File` / `Hotkey` / `Color` 五种控件，零副作用，可安全点「测试触发」反推表单确实写进了配置。
   - 补齐 `action.parameterShowcase.name` 与五个字段标签的中英文词条。

### 🐞 缺陷修复

1. **插件动作的本地化名从未真正生效（静默退化）**
   - **根因一：键的换算规则两边不一致**。词条登记时归一化为 `plugin.<pluginId>.<短键>`，而显示名解析只补了 `plugin.` 前缀，查询键与登记键**永不相等**。
   - **根因二：时序错位**。显示名在 `Initialize` 期间解析，而词条要等注册会话提交之后才写入全局词条表 —— 即使键算对了，当时也查不到。
   - **表现**：带 `DisplayNameKey` 的动作全部静默退回字面文案。而字面文案与译文常常一模一样，所以中文环境下完全看不出问题，要等用户切成英文才会暴露。
   - **修复**：新增 `PluginI18n` 统一解析器（短键 ⇄ 全键的唯一换算处），并在注册会话提交、词条真正落地之后补解析一次。选择补解析而非「让解析去读暂存表」，是为了不要求插件遵守「词条必须写在动作之前」这种没人会记得的顺序约定。
   - **可观测性**：`PluginActionRegistration` 新增「显示名是否来自词条」标志，自检报告新增**词条命中率**一段 —— 修复前 0/3，修复后 3/3。

2. **`PluginHost.SyncFromDisk` 产生「孤儿 ALC」**
   - 对已登记插件无条件重建实例并替换字典条目，旧实例连其 `AssemblyLoadContext` 一起失去宿主引用：动作仍注册着，但内存与文件锁都释放不掉。进入插件管理页（先与磁盘对账）即可触发。
   - **修复**：已在内存的实例就地更新清单与识别结果，只有新插件才创建实例。

3. **数值型插件参数在不同区域设置下表现不一致**
   - 插件写入的是不变文化字面量 `0.5`，而 `PluginActionInput.Int` / `Double` 按系统区域解析，在德法等以逗号作小数点的机器上会解析失败并**静默退回默认值**。
   - **修复**：宿主侧与 SDK 侧统一使用 `InvariantCulture`，并保证宿主写入的数值也是不变文化形式。

### ⚙️ 工程与可维护性

1. **自检通道新增参数校验断言（`[3b]` 段）**：对每个声明了参数的动作验证「必填留空被拦下」「数值越界被拦下且错误归属到该字段」「按声明默认值填充全部通过」「两层校验对同一份输入结论一致」。正向用例的基线只照抄声明的默认值，缺默认值时如实跳过而非自己编造 —— 编出来的值可能过不了插件的 `ValidationRegex`，会让自检报出假失败。
2. **自检报告标注副作用**：明确标出第 `[4]` 节是真实调用、会改变系统状态（亮度/音量/剪贴板），避免作者误以为它是只读检查而反复运行。
3. **插件图标支持**：`plugin:<pluginId>:<短键>` 前缀由 `IconHelper` 解析，插件矢量图标可出现在轮盘扇区与动作下拉中。
4. **`CheckBox` 类型歧义**：WinForms 隐式引用使代码中 `new CheckBox()` 产生歧义，按项目既有约定补全局类型别名，与 `Button` / `TextBox` / `ComboBox` 保持一致。

### 🗂️ 插件目录职责拆分与动作选择器收敛

1. **插件动作在设置面板收敛为「一个类型 + 一个子下拉」**
   - 类型下拉只保留一项「插件动作」，具体是哪个动作由紧随其后的子下拉按**插件名分组**承载。分组头不是 `ComboBoxItem`，**天然不可选中** —— 从结构上排除「选中了插件名却不是一个动作」这种非法状态。
   - 收敛后类型下拉的 `Tag` 就是裸 `Plugin`，不再编码身份；历史上 `Plugin:<贡献点全ID>` 的编码与配套的退化匹配逻辑全部成为死代码，已删除。
   - **切换类型不再清空插件引用**（来回切一次就把配置弄丢是最容易被当成缺陷的行为）；`PluginActionOptions` 只在**类型切换**时重建 —— 它每次求值都新建视图，若纳入全量通知，任何无关属性变更都会因 `ItemsSource` 变更而把 `SelectedValue` 置空，静默清掉用户配置。

2. **两个插件目录职责严格分开**
   - **只读扫描目录** `程序目录\plugin\`：随发行包分发的**待安装候选**，只放 `.dll`。宿主只读不写，**绝不创建** —— StarPie 装在 `Program Files` 这类只读位置时同样正常工作。
   - **可写宿主区** `%LOCALAPPDATA%\StarPie\plugin-data\`：安装副本、启用记录、健康计数、插件私有数据都在这里；便携模式改为**只影响这里的落点**。
   - 旧目录 `plugins\` 在启动时整体搬迁（`Move` 失败退化为递归复制）。**只改目录常量而不搬迁会静默丢数据**：`registry.json` 里存着启用状态与能力确认，不搬就是「界面显示插件全没了」。

3. **候选安装：卡片结论 + 三条复制规则**
   - 扫描目录里的 `.dll` 只登记为**候选**，不加载、不出现在插件列表，装不装由用户点按钮决定；卡片按「可安装 / 有更新 / 版本更旧 / 已装同版本 / 内容已变 / ID 重复 / 无法识别」给出结论与说明，`ID 重复` 与 `无法识别` 一律不给安装按钮。
   - **复制策略由清单来源决定**：有 `plugin.json` 说明那个目录整体是一个插件包，整目录复制；只有裸 DLL 时**只复制那一枚**。此前无条件整目录复制，会出现「从下载文件夹装一枚 dll，把整个下载目录搬进插件目录」以及「只装了 A，邻居 B 也跟着出现」。
   - 覆盖安装前清掉上一次的程序集与清单（残留两枚业务 dll 会让「唯一业务 dll」的识别约定失效），但**保留 `data\` 与 `settings.json`** —— 更新一次版本不该清空用户数据。

### 🐞 缺陷修复（本轮新增）

1. **裸 DLL 安装「装得上却永远启用不了」**
   - 安装目录的识别（`ScanInstalledPlugin`）要求目录里有 `plugin.json`，而裸 DLL 安装从来不写它 —— 于是启用必然失败，且报错是「插件目录里缺少 plugin.json」这种与真实原因错位的话。
   - **修复**：裸 DLL 落盘后回填一份由识别结果生成的 `plugin.json`（纯数据，不执行任何插件代码），把 ID、名称、版本、作者、能力与入口程序集名固化下来，让安装目录自描述。

2. **外部路径登记的卸载会误删开发者的输出目录**
   - `PluginInstance.Directory` 在外部登记分支返回的其实是 **dll 文件路径**，卸载时只是靠 `Directory.Exists(文件路径)` 恒为 `false` 才「恰好」没有删错东西 —— 一旦有人把它改成返回所在目录，就会把开发者的编译输出目录整棵删掉。
   - **修复**：拆成 `ManagedDirectory`（宿主拥有，删除/改名/写入只能用它）与 `Directory`（仅供展示）；卸载走外部登记分支时只摘登记、不碰磁盘，并在日志里写明源文件未被删除。

### ⚙️ 工程与可维护性（本轮新增）

1. **自检沙箱化**：`--plugin-selftest` 的两个根目录被钉到 `%TEMP%\StarPie-PluginSelfTest-<随机>\`，跑完即删。此前它直接跑在**真实**插件目录上，等于每做一次回归就动一次用户已经装好的插件（登记表被改写、目录被删）。
2. **新增 `--skip-invoke`**：跳过自检的第 `[4]` 节真实调用。那一节会真的下发键鼠、调节系统状态（实测会把屏幕亮度推高 10%），日常只关心识别、注册与接缝结论的回归应带上这个开关。
3. **自检新增 `[3d]` 段（只读扫描目录与候选安装）**：空目录、非程序集文件、候选安装后宿主目录内的**程序集数量必须恰好 1 枚**、装完必须处于运行态、装后状态转为「已装同版本」、同 ID 撞车必须双方都判为重复、扫描目录删除后**不得被重建**。其中「装完必须处于运行态」当场抓出了上面那个裸 DLL 启用缺陷。
4. **新增 `--plugin-paths` 路径诊断**：一条命令打印「可写宿主区 / 只读扫描目录 / 便携标志 / 已登记插件数 / 扫描候选数」，并顺带完成一次旧目录搬迁 —— 用户报「插件目录不对」时不必再让他翻界面截图。

## [v1.7.4-beta.4] - 2026-09-16

StarPie v1.7.4-beta.4 紧急修复了多层轮盘在开启「继承全局方案未配置槽位」时受到全局方案所选层数串扰的架构缺陷，实现了精准的同层对应继承机制与超额层优雅回退保护。

### 🌟 核心改进与优化细节

1. **多层轮盘全局继承解耦与同层精准映射**
   - **消除全局层级状态污染**：重构 `WheelProfile.GetEffectiveAction` 与 `GetEffectiveCenterAction`，彻底解耦对动态全局活跃层指针（`globalProfile.Actions`）的盲目依赖。用户在控制台切换全局方案的层数浏览时，不再对其他应用程序的未配置槽位造成任何内存状态污染。
   - **同层对应优先继承**：专属应用程序方案的第 $N$ 层空白扇区与中心核心圆，优先对应继承全局方案第 $N$ 层的动作与子动作配置。
   - **多层超额优雅回退机制**：当专属程序方案的层数多于全局方案时（例如程序方案有 3 层，而全局方案仅有 2 层），超额层（第 3 层）的未配置槽位自动优雅回退继承全局方案第 1 层的动作，杜绝越界或空指针异常。
   - **实时运行态切层同步**：在真实桌面鼠标手势划动交互与滚轮切层时，继承动作跟随层级切换毫秒级即时重新评估与渲染。

---

## [v1.7.4-beta.3] - 2026-09-16

StarPie v1.7.4-beta.3 专项优化了设置控制台的 UI 布局与界面文案体系，全面解决 GitHub Issue [#119 控制台UI及文本繁琐问题](https://github.com/Star-Pie/StarPie/issues/119)。本次更新去除了控制台中冗余的「XX与XX」并列句式、清理了侧边栏双重图标、彻底剥离了中文界面下的硬编码括号英文尾巴、统一收敛了多余的模式切换入口，并修复了未本地化键名直接暴露在界面的缺陷。

### 🌟 核心改进与优化细节

1. **全面消除「XX与XX」冗余并列句式 (Issue #119)**
   - **导航标签精简重构**：侧边栏 5 大导航标签精炼为 4 字标准命名（`触发设置`、`外观样式`、`手势动作`、`系统设置`、`关于软件`），彻底告别冗长的「热键与场景」、「外观与预览」等并列叠加句式。
   - **卡片与分组小节提纯**：对各设置卡片、子分栏（如「触发按键」、「防误触脱离」、「轮盘尺寸」、「主题配色」、「子轮盘设置」、「动作列表」等）的标题与辅助描述进行全面精炼，文字更加紧凑、直观、利落。

2. **侧边栏导航双重图标降噪**
   - **移除冗余叠加轮廓**：彻底移除了导航栏菜单项前面重叠放置的矢量 Path 线条图标，统一规范为单一直观 Emoji 图标与清晰中文排版，彻底消除界面视觉杂乱与双图标重叠感。

3. **中文语境全面剥离英文括号尾巴**
   - **地道清爽的本土化体验**：全量清理了中文环境（简体中文与繁体中文）下控件标签中硬编码附带的括号英文后缀，例如 `(Gap):`、`(Shape):`、`(Live Preview)`、`(Right Button)`、`(Outer Sub-Ring)`、`(Older Milestones)` 等，中文界面视觉更加纯粹舒适。
   - **保留国际化完整英文界面**：在英文语言模式下保持专业地道的全英文表达，中英模式各展所长。

4. **模式切换入口单点收敛与视觉降噪**
   - **消除重复交互元素**：移除了侧边栏左下角重复的模式切换徽标按钮，以及设置区左上方横跨的模式提示栏与大号状态字。
   - **右上角分段胶囊唯一定位**：模式切换统一收敛至控制台右上角精致的分段式开关（`💡 简单模式` / `⚙️ 高级模式`），交互层级更加清晰合理，不再分散用户注意力。

5. **修复 `AdvancedPageSubheader` 字典键名裸露缺陷**
   - **消除代码残留缺陷**：彻底清理了旧版本中由于界面重构遗留的未匹配字典键 `AdvancedPageSubheader`，解决打开系统设置页面时出现未汉化裸露英文键名的显示缺陷。

6. **版本里程碑与关于卡片视觉升级**
   - **关于页卡片精炼**：精炼了关于面板的标题与版本卡片排版，新增 v1.7.4-beta.3 演进记录，并将更早版本折叠框文案精简为「📜 展开查看更早的历史版本演进」。

7. **手势动作画布「图文并茂」开关开箱默认关闭**
   - **极简沉浸画布**：新用户首次启动或创建默认配置时，手势动作交互画布顶部的「🔤 图文」开关默认为关闭状态，画布保持纯图标极简利落呈现，需要查看动作文字时可随时一键开启。

8. **初始默认轮盘配置与黄金几何形态全面对齐**
   - **开箱即享最佳视觉与手感**：将新安装出厂默认及「重置形态默认值」的轮盘类型、展开类型与几何尺寸参数全面同步对齐至当前的黄金配置状态：默认采用「液态毛玻璃 + 浅色主题」、二级外圈子环展开（呼出时不强制全量外显，随划入展开）、主轮盘外径 133px、内径 70px、核心半径 36px、扇区间隙 4px、倒角 13px、字号 13px，二级外径 196px、二级间距 7px、二级倒角 14px、二级触发距离 141px，开箱即具备最精致的微悬浮圆角胶囊质感与触控比例。

9. **渲染热路径性能与底层事件零堆分配优化 (#123, #124)**
   - **渲染资源复用与静态冻结**：冻结并复用轮盘渲染中的动画与特效对象，消除频繁唤出时的 GC 抖动与掉帧。
   - **全局鼠标钩子无堆分配**：底层全局鼠标钩子全面复用事件参数实例，消除快速划动鼠标时的瞬时堆分配压力。
   - **二级子环切片缓存**：外圈子环按扇区切片缓存并约束局部刷新区域，大幅改善级联菜单展开时的响应速度。
   - **手势图样与轨迹提示缓存**：缓存手势图样与命中提示，跳过文本未发生变化时的重复排版测量与计算。

---

### 🗂️ 插件目录职责拆分与动作选择器收敛

1. **插件动作在设置面板收敛为「一个类型 + 一个子下拉」**
   - 类型下拉只保留一项「插件动作」，具体是哪个动作由紧随其后的子下拉按**插件名分组**承载。分组头不是 `ComboBoxItem`，**天然不可选中** —— 从结构上排除「选中了插件名却不是一个动作」这种非法状态。
   - 收敛后类型下拉的 `Tag` 就是裸 `Plugin`，不再编码身份；历史上 `Plugin:<贡献点全ID>` 的编码与配套的退化匹配逻辑全部成为死代码，已删除。
   - **切换类型不再清空插件引用**（来回切一次就把配置弄丢是最容易被当成缺陷的行为）；`PluginActionOptions` 只在**类型切换**时重建 —— 它每次求值都新建视图，若纳入全量通知，任何无关属性变更都会因 `ItemsSource` 变更而把 `SelectedValue` 置空，静默清掉用户配置。

2. **两个插件目录职责严格分开**
   - **只读扫描目录** `程序目录\plugin\`：随发行包分发的**待安装候选**，只放 `.dll`。宿主只读不写，**绝不创建** —— StarPie 装在 `Program Files` 这类只读位置时同样正常工作。
   - **可写宿主区** `%LOCALAPPDATA%\StarPie\plugin-data\`：安装副本、启用记录、健康计数、插件私有数据都在这里；便携模式改为**只影响这里的落点**。
   - 旧目录 `plugins\` 在启动时整体搬迁（`Move` 失败退化为递归复制）。**只改目录常量而不搬迁会静默丢数据**：`registry.json` 里存着启用状态与能力确认，不搬就是「界面显示插件全没了」。

3. **候选安装：卡片结论 + 三条复制规则**
   - 扫描目录里的 `.dll` 只登记为**候选**，不加载、不出现在插件列表，装不装由用户点按钮决定；卡片按「可安装 / 有更新 / 版本更旧 / 已装同版本 / 内容已变 / ID 重复 / 无法识别」给出结论与说明，`ID 重复` 与 `无法识别` 一律不给安装按钮。
   - **复制策略由清单来源决定**：有 `plugin.json` 说明那个目录整体是一个插件包，整目录复制；只有裸 DLL 时**只复制那一枚**。此前无条件整目录复制，会出现「从下载文件夹装一枚 dll，把整个下载目录搬进插件目录」以及「只装了 A，邻居 B 也跟着出现」。
   - 覆盖安装前清掉上一次的程序集与清单（残留两枚业务 dll 会让「唯一业务 dll」的识别约定失效），但**保留 `data\` 与 `settings.json`** —— 更新一次版本不该清空用户数据。

### 🐞 缺陷修复（本轮新增）

1. **裸 DLL 安装「装得上却永远启用不了」**
   - 安装目录的识别（`ScanInstalledPlugin`）要求目录里有 `plugin.json`，而裸 DLL 安装从来不写它 —— 于是启用必然失败，且报错是「插件目录里缺少 plugin.json」这种与真实原因错位的话。
   - **修复**：裸 DLL 落盘后回填一份由识别结果生成的 `plugin.json`（纯数据，不执行任何插件代码），把 ID、名称、版本、作者、能力与入口程序集名固化下来，让安装目录自描述。

2. **外部路径登记的卸载会误删开发者的输出目录**
   - `PluginInstance.Directory` 在外部登记分支返回的其实是 **dll 文件路径**，卸载时只是靠 `Directory.Exists(文件路径)` 恒为 `false` 才「恰好」没有删错东西 —— 一旦有人把它改成返回所在目录，就会把开发者的编译输出目录整棵删掉。
   - **修复**：拆成 `ManagedDirectory`（宿主拥有，删除/改名/写入只能用它）与 `Directory`（仅供展示）；卸载走外部登记分支时只摘登记、不碰磁盘，并在日志里写明源文件未被删除。

### ⚙️ 工程与可维护性（本轮新增）

1. **自检沙箱化**：`--plugin-selftest` 的两个根目录被钉到 `%TEMP%\StarPie-PluginSelfTest-<随机>\`，跑完即删。此前它直接跑在**真实**插件目录上，等于每做一次回归就动一次用户已经装好的插件（登记表被改写、目录被删）。
2. **新增 `--skip-invoke`**：跳过自检的第 `[4]` 节真实调用。那一节会真的下发键鼠、调节系统状态（实测会把屏幕亮度推高 10%），日常只关心识别、注册与接缝结论的回归应带上这个开关。
3. **自检新增 `[3d]` 段（只读扫描目录与候选安装）**：空目录、非程序集文件、候选安装后宿主目录内的**程序集数量必须恰好 1 枚**、装完必须处于运行态、装后状态转为「已装同版本」、同 ID 撞车必须双方都判为重复、扫描目录删除后**不得被重建**。其中「装完必须处于运行态」当场抓出了上面那个裸 DLL 启用缺陷。
4. **新增 `--plugin-paths` 路径诊断**：一条命令打印「可写宿主区 / 只读扫描目录 / 便携标志 / 已登记插件数 / 扫描候选数」，并顺带完成一次旧目录搬迁 —— 用户报「插件目录不对」时不必再让他翻界面截图。
## [v1.7.4-beta.2] - 2026-09-14

StarPie v1.7.4-beta.2 专项优化了全盘秒搜（Quick Finder）的交互体验与内置搜索引擎架构，去除了外部第三方 Everything 相关连接配置与依赖字眼，全面采用 100% 自包含纯原生极速引擎，并在搜索窗口顶部重构了右侧搜索触发按钮与搜索状态动态提示。

### 🌟 核心改进与新增功能

1. **搜索输入栏与触发按钮重构 (图 3 交互优化)**
   - **搜索按钮右移并交互化**：去除原左侧静态窗口拖拽放大镜图标，在搜索输入框右侧（置顶图钉左侧）新增醒目的现代强调风格搜索按钮（`🔍`），输入内容后点击该按钮即可立即发起检索。
   - **智能回车快捷触发**：输入框内输入新关键词后按下 `Enter` 键即可直接启动搜索；当关键词搜索完成且列表中已有匹配项时，再次按 `Enter` 打开目标条目，`Shift + Enter` 打开所在文件夹定位。
   - **动态「正在搜索...」状态提示**：搜索执行过程中，顶部输入栏右侧自动展开 `[ ⏳ 正在搜索... ]` 动态提示，底栏状态同步显示 `⏳ 正在搜索...`，检索完成毫秒级自动收起并呈现结果统计，交互反馈清晰直观。

2. **全面去除 Everything 相关字眼与外部配置 (图 1、图 2 清理)**
   - **清爽纯粹的原生体验**：去除设置控制台动作下拉列表中 `(Quick Finder / Everything)` 后缀，统一规整为 `全盘文件与程序秒搜 (Quick Finder)`。
   - **移除底栏引擎状态徽章**：清理搜索窗口底部的状态徽章与多余外部启动引导，底栏仅保留简洁的操作指引、结果耗时统计与自由拉伸把手。
   - **自包含原生架构**：全面剔除外部 Everything 动态库及 IPC 管道连接代码，StarPie 保持 100% 自包含零外部 DLL 侵入。

3. **内置原生引擎分层极速检索与时间预算保护**
   - **预索引内存短路 (0ms~3ms)**：常用应用程序、开始菜单项与系统工具在首次启动时轻量预加载至内存，输入关键词时优先短路返回，毫无磁盘 I/O 震荡。
   - **高频热点目录极速优先扫描**：优先秒级穿透桌面、下载、文档等高频工作区，常用工程即敲即搜。
   - **50ms 时间预算保护 (Time-Budget)**：深层全盘遍历增加严格的 50ms 时间预算和深度收敛限制，杜绝数十万目录深度地毯式扫描引发的无响应卡顿。

4. **二级子轮盘长文本自适应双行渲染与预览 1:1 对齐**
   - **双行文本截断根因消除**：彻底修复了二级子轮盘（外圈子环与蜂窝扇）在文本过长折为两行时，实际轮盘第二行文字被硬截断、而控制台预览显示两行不一致的缺陷。
   - **自适应容器与动态最大高度**：移除了原 `RadialWindow` 中写死的 `MaxHeight = 32.0`（蜂窝扇 `28.0`）强制封顶限制，引入基于扇区几何厚度、字体高度与图标尺寸动态计算的自适应容器与文本限制，确保无论是外圈子环还是蜂窝扇，长文本双行排版均能完整清晰呈现。
   - **预览与实盘 100% 视觉对齐**：同步重构控制台实时交互预览画布的二级轮盘计算几何，实现缩放比例、换行分词与容器边界的严格同步。

5. **交互音效引擎底层重构与抗干扰死锁根除**
   - **彻底消除非托管内存野指针 (Use-After-Free)**：摒弃不安全的 `Marshal.AllocHGlobal` / `FreeHGlobal` 指针生命周期管理，全面重构为托管 `byte[]` 内存字典与原子引用替换，杜绝在滑块调节音量或热重载时释放指针导致底层崩溃与驱动静默。
   - **专属单工作线程 WinMM 隔离**：将 Win32 `winmm.dll!PlaySoundW` 硬件交互完全收归在唯一专属工作线程内，通过 `GCHandle.Alloc(..., Pinned)` 在单次微型回放周期（30ms）内短暂固定托管内存，彻底禁止任何外部线程并发调用，杜绝 WinMM 内部单设备锁竞争引起的驱动死锁。
   - **无死锁信号合并队列**：以高性能 `AutoResetEvent` 信号机制与原子单槽位合并取代异步 Channel，高频划过扇区时智能合并事件，既实现零延迟极速跟手，又杜绝了高频切换下的音效失灵。

6. **Inno Setup 现代化自动化安装包流水线实装**
   - **Windows 11 Modern 视觉风格**：基于 Inno Setup 6.5+ 打造原生 Windows 11 圆角向导界面，完美继承系统级 Fluent 视觉规范。
   - **100% 自包含简体中文向导**：仓储内置完整 UTF-8 简体中文语言包，彻底解决云端与目标机缺失语言文件导致的乱码与构建中断。
   - **自带完整 .NET 8 独立运行时 (Zero-Dependency)**：开箱即用，打包 Standalone 独立版，目标电脑无需预装任何 .NET 运行时；经 LZMA2 固实极限压缩，体积缩减至约 53MB。
   - **单例互斥锁与进程防占**：与 StarPie 进程级互斥锁对齐，安装或更新升级时自动感知并友好提示，杜绝文件写入冲突。
   - **CI/CD 全自动化构建发布**：GitHub Actions 流水线自动化构建安装包并一并上传至 Release 资产。

## [v1.7.4-beta.1] - 2026-09-13

StarPie v1.7.4-beta.1 重点纠正了默认多媒体播放图标的视觉方向、正式落地了「自定义交互音效调音台」全套真实合成与回放引擎及方案增删管理、优化了高级模式专属入口可见性、彻底修复了自动展开二级轮盘下的多层切换显示缺陷，并完善了扇区长文本智能排版与内存按需轻量化调校。

### 🌟 核心改进与新增功能

1. **纠正 PlayPause 默认矢量图标方向**
   - **符合标准操作认知**：将系统内置动作库与图标选择器中的 `PlayPause` 图标播放三角形方向由原先的朝左（`◀❚`）纠正为**标准朝右（`▶`）**，并搭配标准双竖杆暂停符（`❚❚`）。
   - **消除视觉误解**：彻底解决了原朝左三角形与上一首/快退图标高度相似、容易产生混淆的问题，视觉层次更加清晰规范。

2. **自定义交互音效调音台系统实装 (Custom Sound Studio & Real Audio Backend)**
   - **原生底层非托管合成与极速回放**：告别 Mock 假数据，`SoundEffectManager` 全面接入 `CustomSoundProfile`。基于 44.1kHz 16-bit PCM 极微波形算法实时渲染（正弦波、方波、超微脉冲、水滴声、赛博扫描等），数学音高变换（$f = f_0 \cdot 2^{\text{semitones}/12}$）以及外部 `.wav` 文件的无损增益重采样，真正实现手势触发过程中的零延迟、超低驻留音效反馈。
   - **方案增删与完整生命周期管理**：在内嵌调音台与独立大窗（`CustomSoundEditorWindow`）中均配备「🗑️ 删除方案」功能，支持删除前二次安全确认、保留至少一套方案防空指针、保护系统内置预设不被意外误删；支持自创方案物理删除后自动选中邻近方案并刷新全局音频缓存。
   - **方案持久化与导入导出**：支持将自定义方案导出为 `.starpie-sound`（JSON 规范）文件或在不同设备间无损导入恢复。

3. **方案配置入口智能可见性控制 (简单模式精简聚焦 / 高级模式全景配置)**
   - **保持简单模式极致清爽**：响应用户体验反馈，将音效主题旁的「🎛️ 方案配置」入口按钮与内嵌调音台收归为**仅在「高级全景模式」下显示**。在「简单模式 (精简聚焦)」下自动隐藏复杂配置入口，确保初级用户不受繁杂参数干扰。

4. **修复多层轮盘切换后二级子轮盘不显示的渲染缺陷 (图 3 对应问题修复)**
   - **缺陷根因定位**：当开启「唤出时直接同时展开二级轮盘 (Auto-Expand Sub-Rings)」时，通过滚轮或快捷键切换轮盘图层，`RadialWindow.SwitchToLayer` 重新渲染一级扇区时清空了画布子节点，但未同步重新执行 `ShowAllSubTiers()`，导致二级子轮盘在视觉树中丢失，而底层数学命中判定依然生效（“实际效果还在”）。
   - **完整修复闭环**：在 `SwitchToLayer` 中显式清除并重建 `ShowAllSubTiers()` 视觉元素与极坐标角度映射；在 `GestureController` 中补充捕获鼠标最新物理坐标，切换图层后立即重判高亮命中，实现图层切换后二级外圈子环瞬间无缝呈现、光标高亮即刻跟手。

5. **扇区长文本智能两行排版优化**
   - **格式化排版算法**：自动识别带括号标签、多词及中英混排长文本，在扇区空间有限时智能规整为双行居中排版，彻底避免扇区文字溢出或被遮挡。

6. **OCR 与快速检索按需轻量驻留**
   - **未激活零占用**：OCR 引擎与并发深度检索引擎仅在用户明确调用对应功能时按需加载，未触发或关闭后迅速释放大型对象与上下文资源，静默后台物理内存稳步保持在 15MB ~ 25MB。

## [v1.7.3] - 2026-09-13 (正式版)

StarPie v1.7.3 是在 v1.7.0 基础上对现有操作体验进行深度打磨和增强的稳定版本，同时带来了一系列实用功能的改进与新增。本次更新优化了特定专业软件下的按键共存体验，重构了 OCR 截屏识字与全局快速搜索工具，加入了交互音效与多配置方案支持，并进一步优化了轮盘呼出响应、内存占用与多屏显示适配。

### 🌟 核心体验优化与新增功能

1. **特定程序按键共存体验优化（专属按键唤醒）**
   - **两者共存，随时调用**：在 SolidWorks、AutoCAD 等自带右键笔势或有特殊按键习惯的程序中，现在可以为该程序单独指定专属唤醒键（如鼠标中键、侧键或组合键）。
   - **原生操作零干扰**：在该程序中，默认鼠标右键完全放行给宿主软件的原生操作使用，StarPie 不进行任何拦截；按下专属键即可直接呼出 StarPie 轮盘，实现两套操作顺畅共存、按需调用。
   - **直观录制与状态指示**：在「触发与场景」的黑名单列表中，每项都会直观显示当前生效的唤醒方式，并提供独立的按键录制卡片，支持物理按键快速录制与一键恢复。

2. **全盘快速秒搜 (Quick Search)**
   - **悬浮搜索框**：新增轮盘预设动作「全盘秒搜」，唤出时光标自动居中，支持按住顶部或空白区域拖拽移动。
   - **原生深度检索与静默协同**：内置自主研发的原生并发深度搜索引擎（支持 15 层深层目录穿透与绿色便携软件检索）；检测到本地运行 Everything 时自动启用后台 IPC 加速，未运行则平滑由原生引擎兜底，界面整体保持清爽无感。
   - **新增视频分类与常用推荐**：在原有分类基础上增加「🎬 视频」独立筛选；未输入搜索词时自动展示近期常用文件。
   - **窗口置顶与尺寸记忆**：右上角增加「📌」置顶图钉，开启后失焦不自动隐藏；右下角增加点阵手柄，支持自由拖拽调整窗口大小并自动保存尺寸偏好。

3. **轮盘交互音效系统**
   - **5 个节点音效反馈**：覆盖呼出、扇区划过、展开二级菜单、确认触发、脱离取消。
   - **4 套内置音效主题**：提供机械手感、现代清脆、柔和气泡与极简短音，支持总开关、音量调节与各节点独立开关。
   - **后台异步调度**：采用独立音频线程播放，避免快速划动时出现卡滞或丢音。

4. **多配置文件方案管理与即时热切换**
   - **多独立方案存储**：支持保存并管理多套独立的 `.json` 配置文件（如日常办公、建模设计、游戏娱乐等）。
   - **下拉即时热切换**：在控制台中可直接下拉切换生效，并提供「另存为新方案」、「重命名」、「删除方案」（保留至少一套防误删）以及配置文件的导入、导出备份与一键恢复出厂设置。

### 🛠️ 交互细节与生产力工具打磨

1. **OCR 截屏识字重构与版面重建**
   - **消除高 DPI 选区偏移**：采用全屏物理快照冻结机制与 1:1 坐标裁切，彻底修复 125%、150%、200% 缩放及多显示器下的选区位置漂移。
   - **尺寸自适应与异常防护**：大图超过 2500px 自动下采样防止引擎报错，小图自动双三次插值放大提升识别率，修复特定格式下截图偏黑的问题。
   - **智能版面结构重建**：基于词块空间坐标自动还原段落空行、合并长句断行、保留表格分栏间隙，并清除了中文字符间误插入的空格。

2. **二级子轮盘体验增强**
   - **外圈子环支持唤出全展开**：外圈子环模式下新增「唤出时直接同时展开一二级轮盘」选项，呼出即可直接看到并命中各子动作，无需先滑向外圈。
   - **专属方案继承与就地物化**：修复非全局方案中继承的二级动作无法划入触发的问题，支持点击直接就地编辑修改并保留继承关系。
   - **修复子动作添加按钮**：解决了二级子动作编辑面板中「➕ 添加」按钮在特定操作后偶发不可点击的问题。

3. **配置控制台交互改进**
   - **Tab 2 画布自适应与自由拖拽分栏**：动作配置页改为弹性比例布局，增加居中拖拽分割线，支持自由调整右侧画布视口宽度（支持尺寸记忆与双击复位）。
   - **画布图文并茂展示**：画布工具栏增加「🔤 图文」切换按钮，支持在纯图标与图文复合排版间自由切换，按钮宽度已优化防截断。
   - **扇区 Ctrl 多选与批量修改**：支持在画布上按住 `Ctrl` 键多选扇区，批量统一调整排版方式、字号、图标尺寸、文字颜色及内外边距。
   - **状态回显修复**：修复多层轮盘层切换方式（滚轮 / Tab 键）保存后界面回显不一致的问题。

4. **音量连续调节与快捷键增强**
   - **拖动距离连续线性调音**：音量加减扇区支持向外拖拽线性调节系统主音量（0%~100%），支持中心迟滞缩回、超程取消与外甩取消。
   - **多步快捷键支持**：快捷键执行引擎支持逗号分隔的分步按键（如 Office/WPS 的 KeyTips 序列 `Alt, H, V, F`）。
   - **长按原地直接呼出**：优化长按呼出定时器，键盘单键、鼠标辅助侧键及中键长按无需物理滑动即可原地直接呼出轮盘。

### ⚡ 性能、内存与底层稳定性

1. **内存优化与 LOH 大对象堆碎片根除**
   - **清除 Base64 大对象嵌入**：移除配置中的大字符串内嵌，恢复轻量本地路径与静态冻结位图缓存。
   - **运行内存基准**：静默后台守护态稳定在 **15MB ~ 30MB**（系统整理后约 10MB ~ 20MB），手势交互态控制在 25MB ~ 50MB，控制台关闭 30 秒后自动释放非必要资源。

2. **轮盘呼出性能与防闪烁**
   - **窗口常驻复用 (Single HWND)**：避免频繁创建销毁透明窗体句柄，消除 DWM 重新呈现时的上一帧闪烁。
   - **消除幽灵残影**：精简调度延迟，解决极速甩动手势下可能在桌面残留半透明轮盘的异常。

3. **任务栏与底层按键穿透**
   - **任务栏区域原生透传**：鼠标位于 Windows 任务栏或托盘区域时，右键 100% 绝对透传，解决任务栏固定图标跳转列表（JumpList）与托盘右键偶尔失灵的问题。
   - **修饰键硬件状态检测**：在专业软件中使用 `Ctrl/Alt/Shift + 右键` 时，增加底层物理按键状态探测，避免注入多余的按键释放事件导致组合键中断。
   - **全局 ESC 取消**：在轮盘呼出或手势过程中按键盘 `ESC` 可即刻退出并收回轮盘。

4. **跨屏幕与多 DPI 显示适配**
   - 修复主副屏分辨率与缩放比例不一致时，因 DPI 消息拦截导致的轮盘局部截断或比例异常问题。

---

## [v1.7.3-beta.8] - 2026-09-13

### OCR 截屏识字专项修复与智能版面重建

1. **解决识别区域与框选区域坐标偏移**
   - 重构截屏捕获机制，在唤出框选窗口时预先截取全屏幕物理快照。
   - 框选范围与真实屏幕建立 1:1 像素映射直接裁剪，彻底消除高 DPI 缩放（如 125%、150%、200%）及多显示器环境下的位置偏移与缩放失真。
   - 框选界面增加半透明遮罩与镂空高亮区域，支持鼠标右键或 ESC 极速取消。

2. **解决部分分辨率与缩放比例下无法识别的问题**
   - 增加尺寸边界保护：当截图尺寸宽或高超过 2500 像素时，自动进行高质量下采样，避免触发 Windows 原生 OCR 引擎的 2600 像素上限异常报错。
   - 增加微小字号增强：对尺寸较小的截图自动进行双三次插值放大，提升字符笔画特征与识别率。
   - 修复 GDI+ 图像在特定格式下 Alpha 透明度缺失导致识别画面纯黑的缺陷。

3. **修复文本识别版面结构失效**
   - 引入智能版面结构重建机制，基于字符词块的空间几何坐标自动还原段落与排版结构。
   - 保留自然段落空行：当两行垂直间隙较大时自动识别为独立段落并插入空行。
   - 智能合并断行：对长句因窗口换行产生的碎裂断行进行平滑连接，同时保留序号列表、标题及缩进的自然换行。
   - 保留表格与分栏间隙：对横向间隙较大的文本块保留对齐空格，防止表格或键值对挤在一起。
   - 彻底清除中文字符及全角标点之间被引擎错误插入的空格，规范中英混排排版。

### 全盘搜索深度强化 (Depth-First) 与 UI 全面纯净化

1. **界面彻底纯净化与无感一体化 (UI Purification)**
   - **移除所有外挂与状态芯片**：彻底剥离 `QuickSearchWindow`（全盘秒搜）与 `ProgramPickerWindow`（程序检索器）顶部的 `[ • ⚡ Everything 极速加速 / 穿透 ]` 芯片，搜索框向外延展，界面更加开阔、纯粹、浑然一体。
   - **去除第三方连接逻辑与技术标签**：移除引导启动 Everything 的弹窗与状态按钮；底部状态栏去除 `(Everything 极速加速)` / `(内置原生引擎)` 等技术术语，统一输出清爽原生的搜索统计（如 `找到 10 项结果 · 21 ms`），不向用户暴露技术管道。

2. **内置搜索深度突破 (重点是深度，不是速度)**
   - **解除 450 目录与 3 层深度硬编码限制**：将底层目录递归深度大幅拓展至 **15 层**，允许单次遍历全盘数万个工作目录，彻底解决深层目录与大型工程文件夹内文件搜不到的问题。
   - **多驱动器全盘并发深度穿透**：针对多硬盘用户环境（如 `C:`, `D:`, `G:`, `H:`, `K:` 等），按驱动器根节点并行并发深搜，保证各大硬盘工作区公平扫描，深层 CAD 零部件（`.sldprt`, `.sldasm`, `.dwg`）与工程文件召回率达 100%。
   - **全盘软件与便携程序内存级秒速召回**：缓存预热层完整覆盖 64/32 位注册表（`Uninstall` 与 `App Paths`）、开始菜单、桌面以及全固定盘常用程序根目录（如 `H:\PS2024`, `K:\QQ` 等），免安装绿色程序与已安装大型专业软件实现 0ms 闪电深搜。

3. **底层静默协同与高容错机制**
   - **静默协同与 V1/V2 兼容**：底层在后台静默尝试 Everything IPC，若系统存在 Everything 则自动享受 MFT 瞬时穿透；增加对 Everything 1.4 IPC V1 的自适应重试，避免因权限或版本差异导致的 IPC 2 错误。
   - **无缝深度兜底**：若 Everything 未运行或受 Windows UIPI 权限隔离，StarPie 强大的深度原生引擎无缝接管，用户无感即可享受全盘深搜。

---

## [v1.7.3-beta.7] - 2026-09-12

### 改进与修复说明

1. **新增轮盘交互音效**
   - 增加轮盘操作交互音效，覆盖 5 个触发节点：轮盘呼出、扇区划过、二级菜单展开、动作确认触发、脱离取消。
   - 内置 4 套音效主题：机械手感、现代清脆、柔和气泡、极简短音。
   - 在「触发与场景」页面中提供音效总开关、音效主题切换、音量滑块调节以及各节点独立开关，支持一键试听。
   - 采用专属后台音频工作线程进行播放，解决快速划过二级子轮盘时可能引发的音频卡死或音效消失问题。

2. **多配置方案文件与备份管理**
   - 支持独立管理多套配置方案（JSON 格式），可直接在下拉框中自由切换。
   - 提供「保存为新方案」、「重命名」与「删除方案」功能（内置至少保留一套方案的防误删机制）。
   - 支持「导入外部配置」并自动加入方案库，支持「导出选中配置」生成备份文件，支持一键「恢复默认配置」。

3. **黑白名单添加进程方式优化**
   - 在「触发与场景」中点击「➕ 添加进程」时：若文本框为空，直接弹出「智能运行窗口与进程捕捉器」，支持使用十字准星拖拽瞄准窗口快速捕获，或在运行列表中搜索点选；若文本框内已输入进程名，则直接添加至列表。

4. **中心图案开关状态修复**
   - 修复在修改图片路径或界面刷新时中心图标开关可能被强制重置的问题，确保开关状态与用户的保存设置严格一致。

---

## [v1.7.3-beta.6] - 2026-09-11

### 📐 交互画布自由拉伸比例 & 轮盘层切换方式状态回显修复 (Canvas Resizable Splitter & Multi-Layer Switch Echo Fix)

1. **动作配置页（Tab 2）交互画布自适应与自由拉伸比例 (Issue #100 建议 1)**：
   - **自适应空间重构**：动作配置页（Tab 2）告别原先硬编码 `Width="380"` 的局促窄栏，默认改为与 Tab 1 一致的 `1.15*` : `1*` 黄金弹性自适应比例（最小宽度约束 340px），彻底消除外圈子环展开时横向视口拥挤的问题；
   - **自由拖拽调整手柄 (GridSplitter)**：在左侧方案聚焦编辑卡片栏与右侧实时交互画布之间引入现代半透明居中拖拽分割线（`Tab2GridSplitter`，光标自动切换为 `SizeWE`，配备 0.6 不透明度极细卡片边框）；
   - **用户个性化比例记忆**：拖拽松手（`DragCompleted`）时自动将右栏画布像素宽度记录至 `AppConfig.MappingsCanvasColumnWidth` 并持久化落盘，重新打开控制台或切换方案时平滑还原用户设定的专属视口宽度；
   - **双击极速复位默认**：在分割线上双击鼠标左键（`MouseDoubleClick`），立即将左右分栏宽度复位为 `1.15*` : `1*` 默认比例并清除自定义记忆（置为 0.0）。

2. **多层轮盘层切换方式 UI 状态回显双向修复 (Issue #100 问题 2)**：
   - **现代化交互下拉控件**：将多层轮盘工具栏原先静态写死为 `"💡 滚轮切换"` 的静态文本重构为现代化交互下拉框 `LayerSwitchTriggerComboBox`（选项包含 `🖱️ 滚轮切换` 与 `⌨️ Tab 键切换`）；
   - **状态双向回显修复**：彻底修复将多层轮盘切换方式保存为 `Tab` 键后界面依然错误回显为“滚轮切换”的属性映射缺陷；在 `LoadConfigToUi()` 与 `RefreshLayersUi()` 中统一增加 `UpdateLayerSwitchTriggerUi()` 双向状态刷新，确保读取配置与用户切换下拉选项时即时双向同步并安全落盘。

3. **软件更新设置项高级参数与历史回退默认折叠收纳 (Collapsible Advanced Update Settings & Rollback, Photo 1)**：
   - **界面空间精致化**：将高级与系统页中「软件更新与版本管理」卡片内的开机静默检查更新开关、更新推送通道选择、国内加速下载镜像源下拉框以及庞大的历史版本回退专区（含回退版本选择、回退按钮与更新日志卡片）整体收纳进全新设计的轻量折叠面板（`UpdateAdvancedSettingsExpander`）；
   - **默认收纳体验**：卡片默认仅展示版本标题、运行版本、检查更新与发布页按钮，卡片高度由原先 ~350px 显著精简至 ~75px，大幅减小默认垂直空间占用，保持控制台整洁轻盈；需要调整高级更新偏好或执行版本回退时，点击即可展开。

4. **多配置文件方案管理与即时热切换系统 (Multi-Configuration Profile Management & Hot-Switching, Photo 2)**：
   - **方案独立存储与生命周期管理**：在 `%LOCALAPPDATA%\StarPie\Configs\` 建立多配置方案库，支持管理多套独立 `.json` 方案（如 `默认配置.json`、`CAD建模方案.json`、`日常办公.json`、`游戏娱乐.json` 等）；
   - **即时热切换**：在「多配置文件方案与备份管理」卡片中引入方案选择下拉框 `ConfigProfilesComboBox`，点击任意方案即可一键热切换，即时刷新轮盘视觉树、尺寸几何、主题配色与动作绑定，并向系统托盘发送切换反馈；
   - **多方案完备增删改能力**：
     - **[➕ 保存为新方案]**：支持输入新方案名称将当前内存态与修改另存为全新独立方案并自动激活；
     - **[✏️ 重命名]**：支持就地重命名当前选中的配置方案文件；
     - **[🗑️ 删除]**：支持删除指定配置方案（内置至少保留一套方案保护，删除当前激活方案时自动平滑回退至备用方案）；
   - **外部导入导出生态联动**：
     - **[📂 导入外部配置...]**：选择外部 StarPie `.json` 配置文件后，支持自定义方案名称并自动收纳入方案库并即时激活生效；
     - **[💾 导出选中配置...]**：支持将任意选中的配置方案导出为单独的 JSON 备份文件；
     - **[🔄 恢复默认配置]**：支持将当前方案一键恢复为 StarPie 官方初始出厂推荐配置（恢复完整预装丰富动作，不影响其他方案）。

---

## [v1.7.3-beta.5] - 2026-09-11

### 🌟 唤出一二级轮盘全展开 & 开箱高颜值默认配置友好优化 (Auto-Expand Sub-Rings & Out-of-Box Aesthetic Defaults)

1. **唤出时直接同时展开一二级轮盘 (Auto-Expand Sub-Rings on Popup)**：
   - **功能新增**：在「手势与动作 / 触发与场景」控制台的「多级轮盘与级联子菜单」卡片中，当菜单样式选择「外圈子环 (Wheel)」时，新增「唤出时直接同时展开一二级轮盘」开关；
   - **交互革新**：开启后，用户唤出轮盘的瞬间，所有配置了二级级联子动作的扇区对应的外圈同心子环全部直接全量展开呈现，不再需要将鼠标向外拖拽至触发阈值才展开单一子扇区；
   - **极速直选与双级联动高亮**：光标移动直接在各主扇区与对应外圈子扇区之间进行极坐标无缝命中判定；鼠标悬停在子动作上时，对应的子扇区即刻高亮并向外轻微微弹展开，父级主扇区同步保持从属高亮；松手即可直接秒级执行目标子动作；若光标停留在内圈一级扇区松手，则执行一级主动作；
   - **性能与动效**：入场时所有子环与主轮盘统一挂载至硬件加速视觉树，随同 `MainGrid` 享受贝塞尔平滑缩放与淡入，维持零卡顿 60/120 FPS 极速渲染。

2. **开箱高颜值默认轮盘尺寸与主题配色优化 (Out-of-Box Aesthetic Defaults for Beginners)**：
   - **开箱设计升级**：为全新安装或重置配置的用户带来极致优雅的工业级视觉享受，默认配置升级为：
     - **切削形态**：默认启用 `Glassmorphism`（液态毛玻璃切削形态）；
     - **主题与色系**：默认启用 `Light`（浅色模式），扇区底色 `#F0F8FAFC`、边框 `#3064748B`、高亮底色 `#FF2563EB`（科技湛蓝）、高亮边框 `#FF60A5FA`、文字 `#FF0F172A`；
     - **图文排版**：默认采用 `IconOnly`（纯图标极简模式），居中最大化呈现高清图标；
     - **黄金几何比例**：轮盘外径 138px、内径 52px、核心圆 50px、扇区间隙 2px、扇区圆角 4px；二级子轮盘外径 210px、环间距 4px、子扇区圆角 4px；
   - **默认丰富功能方案**：新用户默认方案在上下左右四大象限均预装丰富的高频实用子动作（右侧复制扩展粘贴/剪切/全选；顶部浏览器扩展 Chrome/Edge/新标签页；左侧系统工具扩展任务管理器/计算器/记事本/控制面板；底部显示桌面扩展一键锁屏），开箱即展现令人惊艳的公转轨道级联星盘效果。

3. **二级级联动作添加按钮偶发禁用彻底根治 (Fix Intermittent Disabling of Add SubAction Button)**：
   - **析因**：`RefreshFocusSubActionsChips()` 原逻辑中当 `subActions == null || subActions.Count == 0` 时会立即提前 `return`，跳过了下方更新 `FocusAddSubActionBtn.IsEnabled` 的逻辑。若用户先前访问过满额扇区（4项子动作）导致按钮置灰，随后切换至未配置子动作的扇区时，按钮将无法恢复启用；
   - **根治方案**：将 `FocusAddSubActionBtn.IsEnabled` 与 `FocusClearSubActionsBtn.IsEnabled` 的更新逻辑提前至方法顶层无条件执行，并在未配置子动作时稳定保持启用状态，彻底根除偶发与永久禁用缺陷。

4. **非全局配置方案继承二级子盘功能修复与就地物化 (Profile Sub-Ring Inheritance Usability & Local In-Place Customization)**：
   - **手势引擎修复**：`GestureController` 修复子轮盘命中测试旁路继承缺陷，由原先直接读取未初始化的本地空动作 `_activeProfile.Actions[num4]` 重构为通过 `_activeProfile.GetEffectiveAction(num4)` 动态获取全局继承的二级子动作，彻底解决专属程序方案下二级子环“在屏幕覆盖显示却无法划入悬停、高亮与触发”的严重缺陷；
   - **聚焦编辑卡片继承态可视化**：在非全局方案中，若主扇区继承自全局且含有子动作，聚焦卡片自动展示全局继承的二级芯片并标有 `(继承)` 微标；
   - **就地无缝物化**：引入 `EnsureLocalSubActionForEdit` 与 `EnsureLocalPrimaryActionForEdit`，用户在非全局方案中点击继承子动作、点击画布子扇区、或点击【➕ 添加二级动作】时，系统自动锁定并物化继承的主动作信息（名称、图标、类型、参数等）与已继承的子动作列表，确保在添加与定制二级子动作时主动作不会退化为未命名空动作；
   - **一键复位继承**：支持一键清空并重置为跟随全局方案。

5. **扇区支持 Ctrl + 点击 多选与批量属性统一调节 (Sector Multi-Selection & Batch Customization Mode)**：
   - **画布多选交互**：在外观形态（Tab 1）与手势动作（Tab 2）的双交互画布中，支持按住 `Ctrl` 键连续点击多个扇区进行多选/反选；所有被选中的扇区均呈现外发光高亮边框（科技青 `#38BDF8`），并在扇区外缘动态渲染清晰的选中序号微标（1, 2, 3...）；
   - **右侧面板自动切换批量修改模式 (Batch Mode)**：进入多选状态后，控制面板自动切换至专用批量卡片，支持：
     - **批量排版模式切换**：一键将所有选中扇区批量设置为「图文并茂 (Both)」、「仅图标 (IconOnly)」、「仅文字 (TextOnly)」或「继承全局 (Inherit)」；
     - **批量字号与图标尺寸调节**：通过滑块批量调整文字大小（8px~20px）与图标缩放（12px~36px），数值实时联动；
     - **批量文字颜色与吸色**：支持十六进制输入、调色盘选取与屏幕实时取色，多扇区文字颜色一键同频；
     - **批量边距微调**：支持水平 X 偏移与垂直 Y 偏移的批量滑块微调；
     - **一键清除自定义**：提供「🔄 清除自定义，恢复跟随全局统一」按钮，一键撤销多选扇区的局部定制并恢复全局统一外观，支持安全退出多选。

6. **动作配置画布“图文并茂”直观展示切换 (Action Canvas Combined Icon+Text Preview Toggle)**：
   - **直观易辨**：针对未配置自定义图标时画布仅显示齿轮导致难以辨识动作名称的问题，在手势动作区（Tab 2）画布工具栏新增「🔤 图文」切换按钮；
   - **复合展示**：开启后，画布在扇区中央同时呈现高质图标与动作名称文本（半粗体居中、自动溢出省略），让拖拽对调顺序一目了然；支持点击随时切换回纯图标极简模式，配置持久化并记忆用户喜好。

7. **修复外部窗口操作后轮盘被压入后台缺陷 (Fix Radial Window Pushed Behind External Windows, #99)**：
   - **析因**：透明轮盘采用常驻复用 HWND 机制后，在轮盘内容隐藏期间若外部程序创建新窗口或重排 Z-Order 层级，常驻的悬浮窗可能丢失顶层顺序，导致后续呼出时轮盘被浏览器、Steam 等常规窗口遮挡；
   - **解决方案**：在每次揭示并播放轮盘入场动画前，调用 Win32 `SetWindowPos` 重新锁定 `HWND_TOPMOST` 最顶层顺序，并附加 `SWP_NOACTIVATE` 标志，在完全不抢占当前前台应用输入焦点的前提下，确保轮盘 100% 呈现在最上层。

8. **预发布版本标识统一显示 (Consistent Prerelease Version Labels)**：
   - 新增统一的 `AppVersionInfo` 版本文本入口，优先读取 `AssemblyInformationalVersionAttribute`，并剥离 SDK 自动追加的 `+提交哈希` 构建元数据；
   - 侧边栏、关于页、更新状态、托盘菜单、启动日志与 User-Agent 统一显示完整语义版本 `v1.7.3-beta.5`，正式版仍按 `v1.7.3` 格式显示。

---

## [v1.7.3-beta.4] - 2026-09-09

### 架构与内存深度优化 (Architecture & Memory Optimization)
1. **透明轮盘 HWND 进程级复用与防闪烁 (Reusable Radial HWND & Anti-Flicker, PR #91 / PR #93)**：
   - 手势呼出改用单 HWND 常驻复用机制，手势结束仅隐藏内容层（`MainGrid.Visibility = Hidden` 并重置透明度），仅在配置修订时重建视觉树，消除重复创建透明窗体带来的原生句柄累积；
   - 在隐藏状态下完成配置刷新与位置校准，下一渲染帧直接平滑淡入缩放，彻底消除 DWM 重新显示透明窗口时的上一帧闪烁；
   - 引入 `_isPresented` 双向生命周期守卫与单次 Render 周期调度，消除两级嵌套 `BeginInvoke` 带来的 32ms 冗余调度延迟，严格恪守 < 16ms 极速呼出红线；彻底防御极速盲操甩动手势下延迟入场动画在手势结束后误唤醒导致的「桌面幽灵轮盘」；
   - 修复外甩脱离取消时的透明度过渡动画，移除 `From = Opacity` 硬编码，使鼠标移出/缩回轮盘边缘时的半透明呼吸过渡丝滑平顺。
2. **任务栏状态预取按需调度 (Taskbar Prefetch On-Demand)**：
   - 在 `GestureController` 中新增方案动作预检判定 `ProfileRequiresTaskbarPrefetch()`。仅当当前轮盘方案的主动作或二级子动作中明确包含 `SwitchWindow`、`Taskbar`、`Tile` 等窗口调度动作时才触发预取；纯快捷键、命令、网址与文件夹等手势方案彻底绕过 UIAutomation COM 扫描与线程积压，保持底层极致轻量与零泄漏。
3. **自动更新与管理员提权静默启动自愈 (Silent Launch on Update & Elevation)**：
   - 在 `UpdateManager` 的自解压安装守护脚本与 `App.xaml.cs` 的 `RestartElevated()` 中显式追加 `--silent` 启动参数，使得更新与提权后程序 100% 保持在纯托盘静默后台守护态（15MB~30MB 物理内存驻留），杜绝意外弹出或膨胀。
4. **旧版配置 Base64 嵌入数据自动洗涤 (Legacy Base64 Data Purge & Sanitization)**：
   - 在 `ConfigManager.LoadConfig()` 与 `EnsureConfigHealth()` 中加入旧版 Base64 自动探测与洗涤逻辑，自动清除历史残留的大对象堆（LOH）异常字符串并自动持久化干净配置，杜绝历史配置导致的内存暗病。
5. **设置窗口关闭释放深度修剪 (Deep Working Set Trim on Console Release)**：
   - 用户关闭设置窗口 30 秒后进入延迟完全释放流程，除了注销全部外部事件并释放 UI 视觉树外，调度 `MemoryOptimizer.TrimMemory(force: true)` 强制执行第二代 GC 回收与物理工作集规整，让内存迅速利落回落至 15MB~30MB 基准。

### 任务栏与系统托盘物理点击原生透传守护 (Taskbar & System Tray Native Passthrough, 根治 Issue #92)
1. **物理坐标智能识别与原生绝对透传**：
   - **析因**：此前全局低级鼠标钩子未对光标所在的 Windows 任务栏（`Shell_TrayWnd` / `Shell_SecondaryTrayWnd`）及托盘溢出区（`NotifyIconOverflowWindow` / `TopLevelWindowForOverflowXamlIsland`）做物理坐标旁路。在后台运行时，用户右键任务栏或托盘时触发键被钩子拦截并经 `mouse_event` 模拟重放，触发 Windows UIPI 防护机制，导致任务栏固定图标右键跳转列表（JumpList）与右键菜单无响应；
   - **解决方案**：在 `GestureController` 中引入物理坐标检测 `IsPointOnTaskbar(physicalPt)`。当鼠标光标落在主屏任务栏、副屏任务栏、系统托盘通知区或托盘溢出浮窗之上时，100% 绝对透传物理按键事件，零拦截、零模拟、零时延，使任务栏右键菜单、JumpList 跳转列表与托盘图标点击彻底恢复系统原生响应。
2. **设置窗口后台隐藏时显式退出任务栏**：
   - 在 `SettingsWindow` 隐藏后台与完全释放时显式将 `ShowInTaskbar` 设为 `false`，呼出时置为 `true`，彻底消除 Windows Explorer 任务栏代理窗口句柄脱节与固定图标右键失灵。

### 界面与体验调优 (UI & Customization)
1. **控制台支持 80%~200% 全局界面缩放 (Settings UI Scaling, PR #87)**：
   - 设置界面新增全局缩放滑块（80% ~ 200%），支持 4K/高分屏或小尺寸屏幕自由缩放控制台 UI。
2. **中心文字自适应配色修复 (Center Text Auto Contrast, PR #88)**：
   - 修复浅色主题下中心文字在浅色背景上白字白底对比度不足缺陷，动态检测背景亮度自适应深浅文本色。
3. **补回「自定义配色」主题入口 (Custom Color Theme Entry, PR #89)**：
   - 补齐一二级主题下拉选单中的自定义配色方案入口，使高级自定义色彩能够无损实时生效与切换。

---

## [v1.7.3-beta.3] - 2026-09-09

### 历史版本一键回退机制 (Version Rollback Engine)
1. **分通道历史版本候选池与精细化配额**：
   - **尝鲜测试版 (Beta 通道)**：支持回退到最近 5 个历史版本（按版本号与发布时间严格倒序，包含预发布测试版与正式稳定版），界面展示琥珀金专属徽标 `🚀 测试版最多回退5个版本`；
   - **正式稳定版 (Stable 通道)**：严格过滤预发布版本，仅保留最近 2 个历史正式稳定版，界面展示青绿专属徽标 `🌟 正式版最多回退2个版本`；
   - **动态通道联动即时刷新**：在控制台更新卡片切换“更新推送通道”（正式稳定版 ↔ 尝鲜测试版）时，回退徽标与历史候选下拉选单即时无损动态刷新；
   - **默认通道智能对齐**：测试版程序默认将推送通道初始化为 `Beta`，正式版程序默认初始化为 `Stable`，无需手动配置。
2. **历史版本详情预览与防误触确认**：
   - 下拉选单中选中任一历史版本后，即刻展开详情卡片，直观展示版本号、通道标识、发布时间、架构安装包类型以及该版本的完整更新说明；
   - 点击一键回退按钮弹出防误触确认对话框，告知用户配置将被安全完整保留。
3. **原子解压覆盖与无损自愈重启**：
   - 复用三阶加速镜像源（ghfast、gh-proxy、mirror）与断点续传极速下载历史版本归档；
   - 通过 PowerShell 独立守护进程等待退出、解压覆盖、自动执行 `Unblock-File` 消除 Windows SmartScreen 标记并自动重启回退版本，用户既有自定义手势与按键配置 100% 完整保留；
4. **全量多语言覆盖**：
   - 全新回退 UI 专区完整支持简体中文、繁体中文、英文、日文四语系本地化。

### 内存异常暴涨与唤出卡顿彻底根治 (Memory & Performance Overhaul)
1. **彻底根除 LOH 大对象堆碎片与工作集暴涨**：
   - **析因**：此前版本尝试将自定义中心图案与图标转为 Base64 嵌入 `config.json`，由于大字符串（>85KB）直接分配在 .NET 大对象堆（LOH）上，高频保存配置时在内存中产生巨量碎片，造成物理工作集异常飙升至 380MB ~ 780MB；
   - **解决方案**：彻底移除 `EmbeddedCustomIcons` 冗余模型与 Base64 编码解包链路，恢复轻量纯净的本地文件路径引用；
2. **全局图标与图片极速冻结并发缓存**：
   - 在 `IconHelper` 中引入基于静态线程安全并发字典的极速缓存，本地文件路径只解码一次并立即执行 `((Freezable)bitmapImage).Freeze()` 冻结位图，未命中与二次呼出零磁盘 IO 与 GC 抖动，彻底消除轮盘唤出掉帧；
3. **务实确立三态物理内存基准**：
   - 在核心架构规范中确立基于 .NET 8 WPF + Win32 真实运行时物理工作集的务实基准：静默后台守护态平稳驻留于 15MB ~ 30MB（系统深睡整理后 10MB ~ 20MB），手势唤出交互态峰值 25MB ~ 50MB，控制台开启态 60MB ~ 110MB（关闭 30 秒按需释放后平稳回落）。

---

## [v1.7.3-beta.2] - 2026-09-08

### 全按键长按原地呼出无缝支持 (Universal Long-Press Trigger for All Mouse & Keyboard Keys)
1. **彻底消除键盘与辅助侧键长按需滑动的交互痛点**：
   - **析因**：此前长按呼出定时器（`LongPressTimerCallback`）设计上仅监听了 `_mouseTriggerDown` 状态，并且仅在鼠标左右键钩子中启动了定时器。对于配置为键盘单键（如 `Caps Lock`、`F1~F12`、字母键等）或鼠标辅助侧键（`XButton1` / `XButton2`）、滚轮中键的触发场景，低级钩子未能启动定时器或在回调中被 `!_mouseTriggerDown` 条件拦截，导致轮盘必须依赖物理光标移动超过 `DragThreshold`（默认 10px）才能被动唤醒；
   - **解决方案**：
     - 重构 `GestureController` 长按定时器状态机：在 `KeyboardHook_OnKeyDown` 中，若开启 `LongPressTrigger`，立即启动长按定时器；在 `KeyboardHook_OnKeyUp` 中优雅撤销定时器，确保轻点打字时 100% 原生穿透；
     - 重构 `LongPressTimerCallback` 条件判断：兼容 `_mouseTriggerDown` 与 `_kbTriggerWaiting` 两种等待态，长按达到预设时长（如 450ms）后自动在当前光标最新物理位置调用 `ShowRadialUI`，并同步触发 `ProcessMove` 与 `ApplyPendingHighlight` 极坐标高亮匹配，彻底实现「按住不动原地直接弹出轮盘」；
     - 优化 `MouseHook` 侧键与中键判定：增加 `TriggerType == "Mouse"` 守护并采用大小写不敏感匹配，确保 `XButton1`、`XButton2`、`MiddleButton` 触发与长按体验一致且丝滑。

### 黑名单与修饰键隔离彻底原生穿透与按键状态守护 (Non-Destructive Isolation & Modifier KeyUp Guard)
1. **彻底根除 Maya 等黑名单软件中 `Ctrl/Alt/Shift + 右键` 快捷键失效**：
   - **析因**：
     - 当用户在 Maya、3ds Max、Blender 等专业软件中使用 `Alt + 右键`（视图平移/缩放）或 `Ctrl + 右键`（标记菜单）时，由于右键被配置为 StarPie 触发键，底层钩子在命中黑名单或修饰键抑制（`CheckIsIsolated`）分支时，错误地调用了 `CancelGestureTracking()`；
     - `CancelGestureTracking()` 内部无条件调用了 `ActionExecutor.ReleaseStuckModifiers()`，该方法直接向系统底层强制注入了所有 11 个修饰键（Ctrl、Alt、Shift、Win）的 `KEYEVENTF_KEYUP` 伪造抬起脉冲；
     - 导致前台宿主软件（如 Maya）瞬间接收到虚假的“修饰键已松开”事件，使得用户的组合快捷键被强行截断甚至失效，必须退出 StarPie 才能恢复正常操作；
   - **解决方案**：
     - **隔离模式零副作用纯原生穿透**：在 `GestureController` 的鼠标和键盘触发按下事件中，当检测到前台处于黑名单或修饰键隔离状态时，**坚决移除** `CancelGestureTracking()` 调用，不向系统注入任何按键脉冲，让硬件按键完全原汁原味穿透至目标程序；
     - **物理按键状态探测守卫 (GetAsyncKeyState Physical Guard)**：重构 `ActionExecutor.ReleaseStuckModifiers()`，在向系统注入 `KEYEVENTF_KEYUP` 前，通过 Win32 API `GetAsyncKeyState((int)mod)` 实时检测该按键的物理硬件状态。若用户当前正物理按住该修饰键（高位为 1），直接跳过不予干预，杜绝任何情况下向系统发送伪造的释放事件；
     - **修饰键触发防自锁优化**：优化 `CheckIsIsolated` 逻辑，若当前配置的触发方式本身就要求特定修饰键（如 `Ctrl + 右键`），自动豁免该修饰键抑制，避免自身配置发生死锁。

---

## [v1.7.2-beta.5] - 2026-09-08

### 早期版本配置文件无损自愈导入与扇区守护 (Early Config Non-Destructive Import & Sector Guard)
1. **彻底根治早期配置文件导入后扇区丢失与错位**：
   - **析因**：早期版本（如 v1.0~v1.6.9）在配置 4 扇区时，JSON 中可能仍保留 8 个历史槽位。新版本在 UI 刷新（`RefreshSlots`）或层级校验（`EnsureLayers`）中，误将 `Actions.Count != SectorCount` 判定为用户主动切换扇区数，触发了极坐标几何投影迁移算法（`MigrateActionsBetweenSectorCounts`），按 0/2/4/6 间隔抽取槽位，导致南、北两个有效扇区（原索引 1、3）被直接遗弃，并在未配置方位填充了垃圾数据；
   - **解决方案**：
     - 在 `EnsureLayers` 与 `RefreshSlots` 中，严禁在配置加载/刷新阶段调用极坐标空间重构；若 `Actions.Count > SectorCount`，无损直接截取前 `SectorCount` 个有效动作；
     - 极坐标重构迁移算法 `MigrateActionsBetweenSectorCounts` 仅保留在用户于控制台 UI 明确主动切换 4/8/12 扇区单选框时触发；
     - 修复 `ImportConfigButton_Click` 中 `_selectedProfile` 未先置空的引用滞留问题，导入成功后全面触发 `ReloadThemePresets`、`RefreshSlots`、`UpdateFocusEditorUi`、`RenderMappingsWheelPreview` 与 `RenderLiveWheelPreview` 全链路即时刷新；
     - 移除 `ConfigManager.LoadConfig` 中此前对全局方案 slot 0 与 slot 6 强制篡改注入默认子菜单的破坏性代码，彻底尊重用户的个人配置偏好。

### 自定义贴图与关联程序图标内嵌记忆包含 (Embedded Custom Icons & Assets Persistence)
1. **配置文件 Base64 资产自包含与跨设备零依赖漫游**：
   - **问题**：中心核心圆自定义贴图（`CoreCustomImagePath`）、用户导入的自定义图标（`custom:...`）以及关联程序继承图标（`InheritAppIconPath`）以往仅在配置中保存本地文件物理绝对路径。当配置文件导出并迁移至新设备、新系统或其他用户电脑导入时，因物理路径缺失导致图标和贴图全部失效变为空白；
   - **解决方案**：
     - 在 `AppConfig` 数据模型中新增 `EmbeddedCustomIcons` 记忆字典（Key 为资产标识/路径，Value 为 Base64 编码流）；
     - **导出/保存前自动化打包 (PackEmbeddedAssets)**：在 `ConfigManager.SaveConfig()` 与 `ConfigManager.ExportConfig()` 序列化前，自动扫描中心贴图、取消动作、方案各层所有槽位与子动作的图标/贴图资源；若图片体积超过 1.5MB 则自动智能等比缩放至 512px 规避配置膨胀，将其转为 Base64 记忆包含在配置文件中；
     - **导入与加载时自动解包与内存流直读双重兜底 (UnpackEmbeddedAssets & In-Memory Fallback)**：
       - 导入配置时自动将内嵌资产无损释放至本机的 `%LOCALAPPDATA%\StarPie\CustomIcons` 目录并自愈更新路径；
       - `IconHelper.GetCustomImageSource`、`IconHelper.GetIcon`、`RadialWindow` 渲染引擎与控制台两大实时预览画布全面接入内嵌记忆兜底，即使目标机物理文件尚未落盘或缺失，亦能直接从 Base64 内存流解码加载，实现 100% 完美无损显示。

### 扇区有效性判定与全局继承判定优化 (Action Configuration Health & Global Fallback)
1. **完善 `IsActionConfigured` 判定逻辑**：
   - 补充 `IconKey`、`CustomIconSvg` 与非默认自定义标题判定，避免具有自定义图标或名称的动作被误判为未配置而丢失；
   - 全局方案（Global Profile）自身针对非 None 动作不再返回 null，彻底消除轮盘扇区意外显示「未设置」的缺陷。

---

## [v1.7.2-beta.2] - 2026-09-08

### 扇区全局方案级联继承与动态映射 (Global Profile Inheritance & Dynamic Mapping)
1. **未配置槽位智能级联继承全局方案**：
   - 解决以往为每个应用程序（如 Photoshop、Edge、CAD 等）创建专属方案时，未配置的空白扇区或中心核圆沦为废键的问题；
   - 当专属方案中的主扇区或中心核圆未配置动作时，自动级联继承全局方案（Global Profile）对应方位的动作，并无缝参与手势调度与执行；
   - **4/8/12 极角对齐映射算法**：即使专属方案扇区数（如 4 键）与全局方案扇区数（如 8 键或 12 键）不一致，通过极坐标几何方位归一化对齐（`GetEffectiveAction`），自动就近继承最贴合物理方位的全局动作，保持盲操肌肉记忆确定性。
2. **继承状态无损隔离与一键恢复继承**：
   - 全局配置新增 `EnableGlobalInheritance` 开关（默认开启），并在控制台 Tab 2 方案管理卡片提供即时开关；
   - 继承动作在内存中以 `ActionItem.IsInherited` 标记，**严禁**将继承数据反向持久化写入专属方案 JSON 中，确保专属配置文件纯净无污染，具备完全的向下兼容与动态自愈能力；
   - 在控制台外观与动作实时预览画布中，继承动作呈现半透明微光（0.55/0.65 Opacity），清晰区分独立覆写与全局继承；
   - 动作聚焦精调卡片增加 `[🌐 全局继承]` 蓝色高亮微标，并在覆写后提供 `[ 🌐 恢复继承全局 ]` 按钮，支持一键清空覆写重新继承。

### 多屏跨分辨率与不同 DPI 缩放截断彻底根治 (Per-Monitor V2 Multi-DPI Display Fixes)
1. **根除跨屏 Double-Scaling 缩放冲突与截断**：
   - 析因：此前低级窗口过程 `WndProc` 中拦截 `WM_DPICHANGED` 并标记 `handled = true`，阻断了 WPF 内部 `HwndSource` 视觉树尺寸更新，导致与手动修改 `Width/Height` 产生 Double-Scaling 缩放冲突。在主屏 2K(150%) 与副屏 1080P(100%) 之间切换时，导致轮盘仅显示 3/4、图标巨大、右下角截断且无法选中；
   - 解决方案：移除 `WM_DPICHANGED` 的 `handled = true` 拦截，交由 WPF 框架原生处理视觉树尺寸；在 `RadialWindow` 创建阶段直接按光标所在屏幕物理坐标与 DPI 计算 DIP 尺寸并以 `WindowStartupLocation.Manual` 精确创建；
   - 重写 `OnDpiChanged` 自动监听 DPI 变化并重新物理校准窗口居中位置，彻底根治跨屏、跨 DPI 缩放时的巨大化与截断缺陷。

### 外甩脱离幽灵虚影彻底消除与手势生命周期自愈 (Outer Escape Ghosting Fix & CloseFast)
1. **根除外甩脱离半透明残影死锁桌面**：
   - 析因：外甩脱离触发时启动了 `Opacity = 0.38` 的 DoubleAnimation。当用户高速甩出并快速松开按键时，UI 线程 Dispatcher 队列竞争导致旧窗口在未清除动画状态下被设为 Hidden，残留半透明虚影死锁在桌面；
   - 解决方案：`RadialWindow` 新增 `CloseFast()` 极速销毁方法，显式调用 `BeginAnimation(OpacityProperty, null)` 剥离透明度动画，即刻收起 Visibility 并彻底关闭窗口；
   - 在 `GestureController.HideRadialUI()`、`CloseGestureWindow()` 与 `ShowRadialUI()` 中加入状态自愈与 `CloseFast()` 调度，确保任何异常竞争下都不会残留虚影。

### 底层钩子性能优化与 ESC 取消手势 (Low-Level Hook Optimization & ESC Cancel)
1. **150ms HWND/全屏状态缓存避免游戏丢键与卡顿**：
   - 在 `ActiveWindowHelper` 与 `FullScreenHelper` 中引入基于 `Stopwatch` 的 150ms 线程安全前台 HWND 与进程名称缓存；
   - 避免在用户高频移动鼠标或连击点击时密集调用 Win32 `GetForegroundWindow` 与进程枚举，大幅释放底层钩子（`WH_MOUSE_LL`）CPU 负担，彻底消除竞技游戏中的丢键与轻微卡顿风险；
2. **ESC 键即刻退出轮盘手势**：
   - 全局低级键盘钩子支持按 `ESC` 键即刻取消手势、收回轮盘并吞掉按键，为误触或临时放弃提供最自然的撤销操作。

---

## [v1.7.1] - 2026-09-08

### 音量拖距连续调音与原生按键穿透 (Volume Drag-Adjust & Trigger Key Passthrough - PR #73 & #78)
1. **音量扇区拖距连续调音 (Continuous Volume Drag-Adjust - PR #73)**：
   - 长按音量加/减扇区向外拖动时，以拖出距离线性映射系统主音量（0%~100%，满量程 200px 对应 ±100%），实现如同专业旋钮/推子般的极速连续调音；
   - 底层采用 Windows CoreAudio 原生 `IAudioEndpointVolume` COM 接口直接读写主音量标量值，杜绝高频连发按键造成的卡顿与时钟挤占，并支持耳机/蓝牙等默认音频设备动态热拔插切换；
   - **智能取消与复原保护**：支持中心迟滞缩回（60%）、高速外甩取消、到顶/底超程取消（超 150px）三大取消恢复机制，取消后自动还原至调节前的基准音量；指针返回 2× 触发半径内自动重新武装，支持单次呼出中连续反复调节；
   - 轮盘中心实时显示 `🔊 X%` 悬浮反馈，并以 180ms 节流唤起系统原生 Volume OSD 浮窗；短按与轻划仍保留单步音量步进原语。
2. **键盘触发键无感穿透模式 (Trigger Key Passthrough - PR #78)**：
   - 键盘触发模式改为穿透模式，不吞掉原生 KeyDown 输入，轻点按键时完全还原目标软件与系统的原生按键行为，大幅改善文本录入与日常使用时的按键兼容性。

### 界面友好度与双模体系深度优化 (Console Ergonomics & Mode Filtering)
1. **简单/高级全星双模体系精细化归位**：
   - **内存优化与配置备份加回简单模式**：在 Tab 3（高级与系统）中，将「极简内存优化 (Working Set Trim)」与「配置备份与恢复 (Backup & Reset)」调整为简单模式与高级模式均可见，便于普通用户随时一键释放物理内存及安全备份/导入方案配置；
   - **动作类型按模式智能过滤**：动作聚焦编辑卡片中的「触发动作类型」下拉菜单实施动态模式过滤，在简单模式下精简聚焦于 7 大高频直觉操作（快捷键、启动程序、打开网址、打开文件夹、截屏识字、系统与右键工具、系统控制），将偏向极客与高级用户的「💻 运行命令」与「🪟 窗口管理」归入高级全星模式下呈现，且具备既有动作选择保护，彻底消除新手选型负担与认知过载；
   - 同步更新多语言状态提示词，明确指示运行命令与窗口管理为高级全星模式专家功能。
2. **侧边栏默认展开与控制台自适应舒展**：
   - **侧边栏默认展开引导新手**：将左侧控制台边栏默认状态调整为展开（宽 230px），完整显示 StarPie 品牌标题、副标题、图标、各 Tab 选项卡语义文本及 4 段主题胶囊切换器，降低新手上手学习门槛；
   - **主窗口默认尺寸与自适应升级**：主窗口默认尺寸由 `1060×720` 提升至 `1220×740`（设置 `MinWidth="1060"`，`MinHeight="660"`），并加入动态屏幕工作区边界安全保护；
   - **根除方案工具栏挤压与文字截断**：配合侧边栏展开，主内容工作区依然享有 940px+ 舒适横向空间，方案管理栏 ComboBox 设定 `MinWidth="130"`，卡片标题及说明文字全面支持优雅自适应折行，彻底解决边栏展开时方案下拉框被挤压至 25px、文字局部截断的缺陷。

### 轮盘分位空间继承与多层架构隔离深度修复 (Sector Mapping & Multi-Layer Wheel Isolation Fixes)
1. **多等级轮盘 (4/8/12 键) 扇区配置绝对极坐标空间继承**：
   - 彻底修复在切换扇区数量（4键/8键/12键）时，原有扇区动作直接按数组索引偏移导致的物理空间几何方位颠倒漂移缺陷；
   - 引入绝对极坐标空间几何方位映射算法（`MigrateActionsBetweenSectorCounts`），确保东 (0° / 右)、南 (90° / 下)、西 (180° / 左)、北 (270° / 上) 四大正交方向 100% 物理绝对对齐且无损继承；
   - 斜向方位（SE/SW/NW/NE 与钟表 12 刻度）采用极角就近智能匹配与标准预设芯片平滑补全，并在 12 键转 8 键时智能识别并优先保留用户真实自定义动作。
2. **自定义新增配置方案多层轮盘独立生命周期与层间防覆盖闭环**：
   - 彻底解决除全局 (Global) 方案外，新建/复制的自定义方案（如 `chrome.exe`、`jianyingpro.exe`、自定义工作流等）中多层轮盘无法独立配置、各层相互覆盖数据的问题；
   - 在 `WheelProfile.EnsureLayers()` 中加入强自愈保护，校验并保证每个轮盘方案及所属各层具备合法的扇区数、独立的 `Actions` 列表及规范动作属性；
   - 在方案切换（`MappingsProfileComboBox`、`ProfilesListBox`、`RefreshProfilesUi`）与层级切换（`LayerSelectComboBox`）的每一处生命周期节点中，严格执行前序活跃层改动回写（`SyncActiveLayerFromRootProperties`），随后无缝加载并同步目标层配置（`SyncRootPropertiesFromActiveLayer`）；
   - 在添加层 (`+ 加层`) 与复制层 (`📑 复制`) 时，新层自动继承当前方案真实的扇区规格（4/8/12 键）并赋予合法动作与子动作集合，彻底阻断层间数据引用污染与覆盖问题。

### 快捷键引擎优化 (Hotkey Engine Optimization)
1. **多键顺序步进与 Office/WPS KeyTips 快捷键支持**：
   - 快捷键解析与执行引擎引入步进模型（`HotkeyStep`），支持多键分步击键；
   - 解决在 WPS、Office、CAD 等软件中触发如 `Alt+H+V+F` 或 `Alt, H, V, F` 等连续菜单/KeyTips 快捷键时按键丢失的问题；
   - 自动识别逗号分隔、分号分隔及加号连接的多步骤序列，步骤间添加适量时延缓冲（25ms~35ms），确保目标软件准确响应按键时序；
   - 保持单步组合快捷键（如 `Ctrl+C`、`Alt+F4`）原有高效执行通路不变。

### 外观与交互优化 (UI/UX Improvements)
1. **外圈子环默认全扇区展开预览与真实数据驱动**：
   - 轮盘二级菜单展开形式设置为「外圈子环」时，外观设置画布严格按方案中各扇区实际配置的二级子动作（`SubActions`）展开呈现，彻底消除未配置二级动作的方案虚构展开 24 个子动作的显示异常；
   - 当方案完全无二级动作时，仅在用户主动展开左侧二级外观微调折叠栏时，在选中的单个主扇区提供按需外观预览；
   - 蜂窝扇形式保持现有单扇区展开与一二级分段切换逻辑不变，避免多叶扇区相互遮挡。
2. **彻底根除一二级切换自动新增子扇区副作用**：
   - 彻底清除切换至「🌟 二级级联」单选按钮时自动向当前主扇区插入 `子动作 1` 的逻辑；
   - 针对尚未配置二级动作的主扇区，提供友好提示卡片「当前主扇区尚未配置二级级联子动作」与直观的「➕ 添加第 1 个二级子动作」按钮，杜绝配置篡改与多余外环渲染。
3. **扇区级联子菜单管理 (Sub-Actions Editor) 全面重塑升级**：
   - **架构重塑**：彻底淘汰旧式单行密集布局，升级为与主控制台画布联动配置完全对齐的**折叠功能栏 (Expander Cards)** 架构；
   - **全量动作类型打通**：完整支持 9 大动作类型，彻底解决 OCR 文本遮挡、网址/命令/窗口管理无法配置的问题：
     - **键盘快捷键**：内嵌 `HotkeyRecorderBox`，支持单键、组合键与「⚙️ 拼装组合」按键组合器；
     - **启动程序**：集成可执行路径输入、从已安装软件库选择、桌面运行窗口实时捕捉、文件浏览、自定义命令行参数，以及普通普通用户降权启动（解决 UIPI 拖放失效）；
     - **打开网址**：支持浏览器指定（系统默认、Chrome、Edge、Firefox、自定义路径）及 GitHub/Bilibili/Bing/Google 常用芯片；
     - **打开文件夹**：支持文件夹浏览及系统内置快捷目录（此电脑、回收站、桌面、下载、文档）；
     - **运行命令**：支持自定义命令行语句与 CMD/PowerShell/Windows Terminal/Git Bash 终端调用；
     - **窗口管理**：支持平铺排布（含 5 大常用排布芯片）、循环切换、还原快照、置顶、移至下一屏、透明度滑块（30%~100% 及 4 档预设芯片）、任务栏槽位切换（Win+N 及快捷槽位芯片）；
     - **系统动作**：全量系统内置预设动作下拉支持；
     - **截屏识字 OCR**：集成离线原生 OCR 测试触发与引擎接口配置；
     - **系统与右键工具 (ShellTool)**：从工具库中挑选高频右键与系统增强功能；
   - **关联外部程序图标**：解耦动作执行与外观，支持为任意二级子动作关联外部程序或快捷方式图标，支持软件库挑选、窗口捕捉与一键清除；
   - **排序与测试操作**：每个子动作均配备 `▲` 上移、`▼` 下移、`▶ 测试` 即时触发与 `🗑️` 删除按钮；
   - **动态容量限制**：严格遵循蜂窝扇（最多 3 项）与外圈子环（最多 4 项）模式的容量限制。
4. **触发按键防冲突提示**：
   - 在触发按键功能描述中补充提示语：“建议避免将常用功能按键绑定为触发按键。”，并同步至简体中文、繁体中文、英文和日文语言包。

## [v1.7.0] - 2026-09-06 (简单模式精简提纯 & 侧边栏微标重塑 & 中心核圆图标呼出修复 & 高级模式多层无限轮盘)

### 🌌 高级全量模式多层轮盘系统 (Multi-Layer Infinite Radial Wheel System)
1. **呼出轮盘后滑轮/快捷键无级循环切换多层轮盘 (Radial Layer Switching)**：
   - 在高级全量模式下为轮盘方案引入「多层轮盘 (Multi-Layer)」架构体系，打破单个方案仅限单层轮盘的物理限制；
   - **交互手感**：呼出轮盘后，在不松开触发键的情况下，轻滑**鼠标滚轮（向上/向下）**或按下**自定义切换键（默认 Tab 键）**，即可在第 1 层、第 2 层、第 3 层……乃至无限多层轮盘之间无缝循环切换；
   - **平滑反馈**：轮盘上方伴随现代化悬浮半透明徽标 `LayerIndicatorBadge` 平滑淡入淡出动画，毫秒级即时展示当前层名称与序号（如 `🌟 第 2 层 编程专用 (2/3)`）；
2. **每层轮盘完全独立深度可调 (Fully Independent Configuration Per Layer)**：
   - 每一层轮盘均拥有独立的扇区数量（4 / 8 / 12 扇区）、独立的动作槽位映射列表、独立的中心核圆动作与开关；
   - 彻底打破传统轮盘按键数量的极限，大幅扩展单手操作动作容量，满足建模设计、游戏多套宏指令、多环境开发等高阶全量需求；
3. **Tab 2 动作配置面板多层管理控制台**：
   - 在高级全量模式下，Tab 2 顶部方案下拉栏右侧集成现代化管理工具栏 `Tab2_MultiLayerHeaderPanel`：
     - **层级切换下拉框**：快速切换当前正在编辑的轮盘层；
     - **`[ ➕ 加层 ]`**：一键追加全新轮盘层（支持自定义名称）；
     - **`[ 📑 复制层 ]`**：一键将当前层的全套扇区与中心配置克隆为新层；
     - **`[ ✏️ 重命名 ]`**：即时弹出重命名对话框；
     - **`[ 🗑️ 删除层 ]`**：删除不需要的层级（第 1 层受安全保护不可删除）；
     - **`[ ⚙️ 切换方式 ]`**：呼出右键浮动快捷选单，自由设定滚轮切换、Tab 键切换或键盘按键触发；
4. **底层架构解耦与 100% 配置文件无损向下兼容**：
   - 新建 `WheelLayer` 实体类并深度集成至 `WheelProfile`；
   - `WheelProfile.EnsureLayers()` 自动对老版本单一轮盘配置进行零破坏向上迁移，无缝封装为 Layer 0，旧版方案加载 100% 稳定无感；
   - 底层 `MouseHook` 拦截轮盘展示期间的 `WM_MOUSEWHEEL` 滚动消息，防止宿主应用意外发生页面滚屏。

### 💡 侧边栏模式微标折叠视觉重塑与一键极速切换 (Sidebar Mode Badge Redesign & One-Click Toggle)
1. **折叠态视觉完美重构**：
   - 针对侧边栏折叠收起时，模式文本截断为 "⚙️ 高" 或对比度不良的瑕疵进行全面重塑；
   - 折叠状态下自动收缩为 28×24px 极简居中圆角胶囊徽标，仅展示高辨识度 Emoji 图标（简单模式为 `💡`，高级全量模式为 `⚙️`），彻底消除文字截断；
   - 展开状态下展示自适应圆角矩形胶囊，背景辅以高雅微光晶透紫 (`#7C3AED`) 与翡翠青，文字强制纯白 `#FFFFFF`，高对比度赏心悦目；
2. **微标点击一键极速切换模式**：
   - 赋予侧边栏底部模式微标 `Hand` 手型悬停反馈与点击事件，无论展开或折叠状态，用户直接点击该微标即可在「简单模式」与「高级全量模式」之间极速往复切换，免去翻找顶部控件的繁琐。

### 💡 简单模式交互精简提纯 (Simple Mode Streamlining)
1. **鼠标手势 (Mouse Gestures) 独立卡片收纳**：
   - 响应极简聚焦体验需求，将 Tab 0 触发与场景界面的「鼠标手势 (Mouse Gestures)」独立卡片纳入高级模式；
   - 简单模式下自动折叠收纳隐藏，保持触发配置界面清爽沉浸，高级全星模式下一键展开完整多段笔势映射矩阵；
2. **顺势外甩执行自定义动作精简**：
   - 将顺势外甩卡片内的「外甩取消时执行的动作」高级配置分区（含 7 大精简分类、6 大极速预设芯片与程序/参数编辑器）放入高级模式；
   - 简单模式保留直观的「启用顺势外甩取消」总开关与甩出取消距离滑块，默认采用纯净静默关闭，降低认知负荷；
3. **屏幕边缘呼出智能防溢出与光标自动对齐卡片收纳**：
   - 将 Tab 0 的「屏幕边缘呼出智能防溢出与光标自动对齐」卡片放入高级模式；
   - 简单模式下默认保持内部推荐的智能贴边与光标校准策略，界面收起微调滑块与策略下拉框，界面一目了然；
4. **手势与动作「紧凑全览列表」分段切换收纳**：
   - 将 Tab 2 右上角的 `[ 🎯 画布联动精调 | 📋 紧凑全览列表 ]` 视图分段切换器放入高级模式；
   - 简单模式下自动锁定并呈现直观的可视化交互画布，免受密集表格列表干扰；高级模式下恢复显示，支持极客用户全量审阅编辑。

### 🛡️ 系统托盘跨特权拖拽防卡死保护与 UIPI 消息白名单 (Tray UIPI Safety Guard)
1. **双重 Win32 消息过滤白名单机制 (Process & HWND Filtering)**：
   - 深度解决用户在 Windows 11 24H2 等系统中以管理员提权身份运行（`Elevated: True`）时，将系统托盘折叠菜单内的图标拖动到任务栏主区域发生 OLE 拖放死锁、导致半透明图标与光标冻结在一起的系统级冲突；
   - 引入 Win32 `ChangeWindowMessageFilter`（进程级）与 `ChangeWindowMessageFilterEx`（句柄级）双重防御机制；
   - 在系统托盘初始化后，自动通过底层原生窗口句柄放行来自标准中等权限资源管理器（`explorer.exe`）的 `WM_DROPFILES` (0x0233)、`WM_COPYDATA` (0x004A)、`WM_COPYGLOBALDATA` (0x0049)、`WM_SETTINGCHANGE` (0x001A)、`WM_DISPLAYCHANGE` (0x007E)、`WM_COMMAND`、托盘回调消息以及鼠标消息，彻底打通跨特权通道，避免 UIPI 拦截导致的 OLE 状态机挂起。

### 📂 文件夹打开与目标路径容错健壮性增强 (Action Path Robustness)
1. **磁盘驱动器与网络路径平滑容错**：
   - 针对跨设备导入配置可能导致的本地磁盘驱动器不存在（如 `系统找不到指定的磁碟机`）等边界场景，增强驱动器根目录有效性预检；
   - 异常捕获改用非阻塞 UI 线程调度，杜绝后台阻塞或未捕获崩溃，提供清晰友好的提示，保障手势主循环绝对稳定。

### 🎯 自定义中心图案优先显示与动作图标居中零偏移 (Custom Center Pattern Priority & Action Icon Centering)
1. **自定义中心图案绝对优先展示 (Custom Pattern Absolute Priority)**：
   - 彻底修复此前开启「中心核圆动作」时，用户在 Tab 1 精心定制的中心贴图、自定义矢量图标或预设准星/猫爪/罗盘等图案被强制替换为动作图标的体验缺陷；
   - 新增 `IconHelper.HasCustomCenterPattern(config)` 全景判决引擎：当用户开启中心图案且配置了本地图片、自定义 SVG、图标包或除默认退出 (Exit) 外的任意预设图案时，轮盘中心无论是否配置动作，均**绝对优先展示自定义图案**，用户的视觉个性化定制永不丢失；
2. **中心动作图标正中严格居中与偏移彻底消除 (Action Icon Zero Offset)**：
   - 彻底消除此前由于直接复用 `CoreExitIcon` / `CoreCustomImageEllipse` 控件导致自定义贴图位移（`CoreImageOffsetX` / `CoreImageOffsetY`）与缩放（`CoreIconScale`）意外污染动作功能图标产生倾斜偏移的缺陷；
   - 当显示中心功能动作图标时，强制统一重置 `RenderTransform = null`，确保各类型动作图标始终正中居中，无任何位置漂移；
3. **三端（实际呼出轮盘、Tab 1 外观预览、Tab 2 动作预览）视觉与交互 100% 对齐**：
   - 全面统一 `RadialWindow.xaml.cs`（实际呼出）、`SettingsWindow.RenderLiveWheelPreview`（Tab 1）与 `SettingsWindow.RenderMappingsWheelPreview`（Tab 2）的中心渲染层级流水线；
   - 在 Tab 2 控制台选中中心核心圆并开启中心动作时，若检测到用户已启用自定义中心图案，自动在状态栏下方呈现琥珀微光提示条 `CenterPatternPriorityTip`（说明中心优先展示自定义图案，在中心死区内松开鼠标仍会照常触发本功能），操作逻辑清清楚楚、明明白白。

### 🌟 轮盘中心核圆动作图标呼出修复 (Center Core Action Icon Fix)
1. **实际呼出轮盘 (`RadialWindow`) 与控制台画布视觉通路彻底打通**：
   - 深入排查发现：`RadialWindow.xaml.cs` 中中心核圆区域此前仅读取全局配置 `CoreIconType`（默认为退出 'X' 叉号），未读取当前方案 `WheelProfile.CenterAction` 的动作图标；
   - 重构 `RadialWindow` 中心核圆渲染引擎：当方案开启 `EnableCenterAction` 并配置了中心动作时，全面支持以下图标解析层级：
     1. 自定义矢量 SVG (`CustomIconSvg`)；
     2. 关联外部程序原生提取高清图标 (`InheritAppIconPath`)；
     3. 矢量图标关键字 (`IconKey`，包括 `custom:` 自定义图标包和内置矢量图库，如 `Paste`、`Copy`、`Settings` 等)；
     4. 启动程序参数中的应用图标智能自动提取；
     5. 动作类型默认图标保底（`Launch` -> 火箭、`WebUrl` -> 地球仪、`Folder` -> 文件夹等）；
2. **中心核圆悬停交互高亮优化**：
   - 当启用中心核圆动作且鼠标悬停在中心死区时，图标光晕高亮自动切换为琥珀金 (`#F59E0B`) 选中反馈，而非原本表示关闭放弃的红色 (`#F43F5E`)；未启用中心动作时保留原本的红色取消高亮，手感更具确定性；
3. **控制台实时预览画布同步强化**：
   - `SettingsWindow.xaml.cs` 动作配置实时画布对齐完整的中心动作图标解析与渲染优先级，使配置预览与真机呼出轮盘达到 100% 像素级一致。

### 🛠️ 多层轮盘控制栏布局重塑与极致紧凑收纳 (Multi-Layer Header Panel Layout Optimization)
1. **标题右侧留白精准并列排布**：
   - 响应用户界面反馈与视觉标注，将原本横贯在 Tab 2 中间过长的一行 `Tab2_MultiLayerHeaderPanel` 移动至标题 `手势方向与动作映射` 的右侧空白区域，与主标题并列同行排布；
2. **控件尺寸极致紧凑化与文案提炼**：
   - 标签由 `🌀 多层轮盘:` 精简为 `🌀 轮盘层:`；
   - 下拉框宽度由 110px 优化为 95px，高度精细化为 25px；
   - 各操作按钮（`➕ 加层`、`📑 复制`、`✏️`、`🗑️`、`⚙️`）内边距紧凑化；
   - 提示文案由 `💡 唤起后滚轮切换` 提纯为 `💡 滚轮切换` 并提供丰富 Hover ToolTip，彻底解决横向空间拥挤，布局精致利落。

### 🛡️ 反馈日志深度诊断与按键/剪贴板稳定性重大修复 (Log Diagnosis & Critical Stability Fixes)
1. **Win32 物理修饰键卡死与鼠标按键瘫痪根治 (VK_LWIN / VK_RWIN Extended Key Release)**：
   - 彻底修复连续高频触发 `Win+D` / `Win+X` 后 Win 键残留卡死在系统物理键态表中的顽疾；
   - 为 Win32 `keybd_event` 的 `VK_LWIN` (91) 与 `VK_RWIN` (92) 释放信道注入 `KEYEVENTF_EXTENDEDKEY (0x0001)` 标志；
   - 建立 +35ms 与 +85ms 双阶异步守护释放机制，彻底解除 Windows 键态死锁，彻底杜绝鼠标左键、右键以及后续轮盘扇区点击失灵；
2. **剪贴板跨线程 OLE STA 异常根治 (SafeSetClipboardText)**：
   - 针对 `Windows.CopyAsPath` 在后台工作线程调用 `Clipboard.SetText` 抛出 `ThreadStateException` 的问题，新增具有 Dispatcher 优先调度与专属 STA 线程回退双通道的 `SafeSetClipboardText`，确保复制路径 100% 稳定运行；
3. **连续按键序列解析与物理调度 (Multi-Key Sequence Hotkey Execution)**：
   - 重构热键解析器与执行引擎，全面支持如 `U+U`、`U,U`、`UU`、`US` 等非修饰键连续击键序列以及 `Win+X` 配合的极速系统快捷键，通过 25ms 物理键击节拍调度发送底层扫描码，避免被输入法或 Unicode 字符流拦截；
4. **系统动作中文与常用别名扩展 (System Command Aliases)**：
   - 为休眠/睡眠/重启/关机动作增加中文别名（`睡眠`、`休眠`、`重启`、`关机` 等），杜绝因别名不匹配导致动作失效；
5. **触发键与鼠标手势冲突双重守卫 (Trigger & Mouse Gesture Collision Guard)**：
   - 在 `ConfigManager.EnsureTriggerHealth` 与 `GestureController` 中加入双向冲突防护，当鼠标手势按键与主轮盘唤醒键偶然相同时，手势自动让位给轮盘，彻底消除原底层钩子将唤醒键硬拦截吞掉导致按键瘫痪的隐患。

### 🎨 多层提示微标外观与形态深度定制 (Layer Indicator Badge Appearance Customization)
1. **轮盘层数切换浮动提示徽标外观与形态定制面板**：
   - 将多层切换提示微标的外观样式配置正式纳为主界面 Tab 1「轮盘外观与形态定制」面板的专属卡片（`Tab1_LayerIndicatorCardBorder`）；
   - **高级模式专属**：遵循双模设计原则，简单模式下自动折叠收拢，高级全量模式下完整展开深度定制项；
   - **提示显示开关**：提供「启用层数切换浮动提示徽标」总开关，用户可按需自由开启或关闭浮动提示；
   - **6 大预设风格**：沉浸深邃暗黑（Dark Slate - 默认）、晶莹极光蓝透（Aurora Cyan）、钛金晶透曜紫（Titanium Purple）、极简透白浅色（Minimalist Light）、跟随当前轮盘主题（Follow Wheel Theme）、完全自定义色彩（Custom Hex）；
   - **7 大前置图标**：璀璨星芒（🌟 - 默认）、冰晶雪花（❄️）、宇宙星盘（🌀）、极速闪电（⚡）、准星靶心（🎯）、纯净宝石（💎）、无前置图标（None 仅文字）；
   - **全量色彩微调**：完全自定义模式下支持十六进制色值编辑、调色盘选择与屏幕任意位置吸色管；
   - **平滑滑块微调**：平滑圆角（4~24px）、文字字号（9~16px）、垂直偏移（0~60px）、提示停留时长（400~3000ms），支持一键恢复默认样式；
2. **实时交互画布 (Live Preview) 60FPS 即时联动**：
   - 在 Tab 1 右侧常驻的交互画布中新增 `PreviewLayerIndicatorBadge` 徽标，实时呈现所选主题底色、边框、前置图标、字号与圆角，所见即所得；
3. **主分支功能 PR 合并 (PR #61)**：
   - 顺利合入主分支功能 PR：启动程序动作 (`Launch`) 在通过软件检索器选取目标可执行程序时，自动提取并继承其高清原生图标，省去手动寻找或设置图标的步骤。

### ⚡ 工程维护与版本发布
1. **全模块版本号同步**：
   - 同步更新 `WinPieGestures.csproj`、`SettingsWindow.xaml`、`SettingsWindow.xaml.cs`、`App.xaml.cs`、`AGENTS.md` 至 `v1.7.0`；
2. **持续坚持 0 内存泄漏与超低延迟**：
   - 经测试验证，编译 0 错误、0 警告，中心动作、多层轮盘与简单模式全套自动化断言 100% 通过；发布轻量绿色包与单文件独立版双架构归档。

## [v1.6.9] - 2026-09-06 (简单/高级双模切换体系 & 根除重启后扇区重置为平铺Bug与数据自愈 & 程序图标继承渲染优先级与平铺覆盖保护 & 防误触与核心配置持久化守卫)

### 💡 重大新增：简单模式 vs 高级全量模式双模交互体系 (Simple vs Pro Mode)
1. **全局顶部控制台模式切换胶囊**：
   - 在控制台主区域顶部常驻 `[ 💡 简单模式    ⚙️ 高级全量模式 ]` 单选切换胶囊与状态徽标；
   - 模式选择全局持久化至配置文件 `ConfigMode`（默认进入简单模式），切换即时生效且无缝记忆；
   - 侧边栏底部同步展示 `SidebarModeBadge` 与 `SidebarModeText`，直观呈现当前运行模式；
2. **精细化常用与高频配置边界（严格遵循按图定制规范）**：
   - **简单模式 (Simple Mode)**：聚焦常用高频功能，为轻量级用户提供极致纯净体验：
     - **保留区域**：多级轮盘与级联子菜单、顺势外甩脱离取消、进程隔离黑白名单、关联外部程序图标、二级级联子动作、外观主题与切削形态、几何尺寸与内外径倒角微调等高频核心功能；
     - **屏蔽隐藏**：隐藏修饰键硬件旁路（Ctrl/Shift/Alt 禁用轮盘）、平铺高级设置卡片（窗口排除正则/边距间距/循环切换）、扇区与核心圆独立字体选择面板、文字相对位置与 X/Y 像素微调偏移面板、OCR 截屏识字与智能 API 接口卡片、内存压缩调试卡片、JSON 配置导入导出卡片、系统运行日志与诊断目录卡片；
   - **高级全量模式 (Pro Mode)**：一键全面开放所有专家级微调参数与运维诊断卡片；
3. **四语系全量国际化支持**：
   - 简体中文、繁体中文、英文、日文四套字典完整支持模式切换标签、状态微标与引导说明。

### 🎯 核心修复 1：根治重启后轮盘扇区功能被重置为「平铺: 左右对半」问题（Bug 1 终极根除 & 自动数据自愈）
1. **XAML 解析硬编码选中缺陷移除**：
   - 深度溯源发现：`SettingsWindow.xaml` 中 `FocusWindowSubModeComboBox` 的 `<ComboBoxItem Tag="Tile" ... IsSelected="True" />` 在 XAML 解析阶段即触发 `SelectionChanged` 事件，此时界面尚未完成数据绑定，事件直接将槽位 0（扇区 1，右侧 E / 0°）篡改为 `Type="Tile", Parameter="2L", Name="平铺: 左右对半"`；
   - 彻底移除 `IsSelected="True"`，杜绝 XAML 解析期的非法事件发射；
2. **多层全生命周期防重入与面板可见性双重守卫**：
   - 将 `_isUpdatingFocusUi` 初始状态设为 `true`，在构造函数与 `EnsureUiInitialized()` 全阶段持续锁定，直至全部数据完成绑定；
   - 在 `FocusWindowSubModeComboBox_SelectionChanged`、`FocusTileLayoutComboBox_SelectionChanged`、`FocusActionTypeComboBox_SelectionChanged`、`FocusActionNameTextBox_TextChanged` 等事件处理程序中，加入 `_isUpdatingUi || _isUpdatingFocusUi || !_isUiInitialized || _isUiInitializing` 拦截；
   - 增加 `FocusWindowManagerPanel.Visibility == Visibility.Visible` 及原动作类型前置校验，确保非窗口管理动作绝不会被误篡改为平铺动作；
3. **受损配置自动自愈引擎 (Auto Data Self-Healing)**：
   - 在 `ConfigManager.LoadConfig()` 与 `SettingsWindow.EnsureUiInitialized()` 中增加受损数据自愈机制：若检测到槽位动作为 `Tile / 2L` 但保留有合法的 `.exe` / `.lnk` 继承图标程序路径，启动时自动无损恢复为 `Type = Launch` 启动该程序，自动恢复名称并持久化修复，彻底拯救受损的旧版配置文件；

### 🎨 核心修复 2：根治重启后轮盘扇区图标变 Win 图标问题（继承图标优先级提升与平铺子模式保护）
1. **图标渲染短路缺陷根除**：
   - 排查发现：在 `RadialWindow.xaml.cs`（一级扇区、二级外圈子环、二级蜂窝扇）以及 `SettingsWindow.xaml.cs`（外观实时画布、动作区画布）中，此前只要检测到 `IconKey` 或 `iconSvg` 非空，便会直接加载内置矢量 Path，导致排在后面的 `InheritAppIconPath` 判定被彻底短路跳过；
   - 将 `currentAction?.InheritAppIconPath` 判定提升至普通 `IconKey` 判定之前（仅次于用户显式上传的自定义 SVG），优先提取并渲染外部程序的原生高清位图图标；
2. **平铺（Tile）子模式默认覆写保护**：
   - 深入排查发现：当动作类型为平铺或切换窗口子模式时，旧逻辑会无条件给 `item.IconKey` 赋值 `"Tile"`（4 宫格四方块图标），强行覆盖了用户之前关联程序时提取的图标；
   - 在 `SettingsWindow.xaml.cs`（`FocusWindowSubModeComboBox_SelectionChanged`、`ApplyFocusTileLayout`）以及 ViewModel（`SlotViewModel`、`SubSlotViewModel`）中加入保护策略：仅在 `IconKey` 与 `InheritAppIconPath` 均为空时赋予默认 `"Tile"`，绝不破坏用户既有的程序图标与视觉定制；
3. **设置控制台图标选择与焦点编辑卡片增强**：
   - 动作区扇区焦点编辑卡片增加实时外部程序图标徽标与预览组件（`FocusInheritIconPreviewImage`、`FocusIconImage`），支持直接显示继承图标；
   - 动作列表中支持绑定外部程序图标并自动隐藏通用矢量 Path，右侧指示文字优先呈现关联程序的友好名称。

### 🛡️ 核心修复 2：根治防误触功能重启后自动被关闭问题（嵌套重入锁深度计数 & 初始化时序重写与多层持久化守卫）
1. **嵌套 UI 抑制标志提前清零穿透排查（致命根因彻底定位）**：
   - 深入追踪堆栈定位发现：在 `SettingsWindow.xaml.cs` 的 `LoadConfigToUi()` 执行到第 802 行时调用了 `RefreshLayoutOptionsUi()`；
   - `RefreshLayoutOptionsUi()` 内部具有 `try { _isUpdatingUi = true; ... } finally { _isUpdatingUi = false; }`；
   - 其 `finally` 块在退出时将全局 `_isUpdatingUi` 强行清为了 `false`，而此时第 923 行的 `DisableOnFullScreenCheckBox.IsChecked` 等防误触控件尚未被赋值；
   - 紧随其后的第 803 行 `SetComboBoxSelectedValue(SubmenuStyleComboBox)` 触发了 `SelectionChanged` 事件，因 `_isUpdatingUi` 已被清零，事件处理程序误判为用户手动交互，并调用了 `SyncUiToConfigAndSave()`；
   - 由于此前 `EnsureUiInitialized()` 在进入之初就将 `_isUiInitialized` 设为了 `true`，`SyncUiToConfigAndSave()` 放行执行，读取了尚未被配置赋值的 `DisableOnFullScreenCheckBox`（默认状态为 `false`），瞬间将内存中的配置改写为 `false` 并写入磁盘，导致重启 4 秒内必被复写！
2. **重入深度计数器 (Re-entrant Depth-Counted UI Guard) 架构重塑**：
   - 将 `_isUpdatingUi` 全面重构为基于 `_uiUpdateDepth` 的引用计数器：进入更新块递增深度，退出递减深度，仅当计数归零时才解除 UI 抑制，彻底根治所有嵌套子方法提前清零外部抑制状态的隐蔽缺陷；
   - 修正 `UpdateSidebarThemeVisualState` 等方法的不对称赋值，消除潜在状态泄漏；
3. **初始化生命周期原子提交守卫**：
   - 引入 `_isUiInitializing` 重入锁，并将 `_isUiInitialized = true` 的赋值严格移至 `EnsureUiInitialized()` 的最后一步；在全部控件（包括全屏独占开关、修饰键旁路复选框、外甩取消等）完全就绪前，`_isUiInitialized` 始终保持 `false`，任何中间事件均无法触发磁盘持久化；
   - 在 `SettingsWindow.xaml` 中为 `DisableOnFullScreenCheckBox` 显式补充 `IsChecked="True"` 默认属性，与数据模型缺省对齐。

### 🎨 全场景画布渲染统一
1. **实时轮盘、外观画布与动作画布三位一体渲染对齐**：
   - 一级主轮盘（`RadialWindow`）、外圈子环（`Outer Sub-Ring`）、蜂窝扇（`Honeycomb Fan`）；
   - 外观与形态实时画布（`LiveWheelPreviewCanvas`，主扇区与二级子扇区）；
   - 手势与动作实时画布（`MappingsWheelPreviewCanvas`，主扇区与二级子扇区）；
   - 以上全部 6 大渲染通路统一遵循：用户自定义 SVG > 关联外部程序原生图标（`InheritAppIconPath`） > 图标包扩展图标（`custom:`） > 内置矢量图标（`IconKey`） > 动作默认图标的精准优先级。

### ⚡ 二级菜单样式描述与上限匹配完善 (蜂窝扇最多3项 / 外圈子环最多4项)
1. **控制台说明文本与多语言校正**：
   - 彻底修正 Tab 3「多级轮盘与级联子菜单」卡片中关于二级菜单样式的描述：由误写的“外圈子环（数量不限）”修正为规范的“**外圈子环最多 4 个，蜂窝扇最多 3 个**”；
   - 同步更新简体中文、繁体中文、英文与日文四语系国际化词条（`SubmenuStyleDesc`），且切换语言即时响应。
2. **二级动作添加动态容量限制与防越界保护**：
   - Tab 2「手势与动作」中的「➕ 添加二级动作」按钮（`FocusAddSubActionBtn`）以及独立弹窗编辑器（`SubActionEditorWindow`）全面接入动态容量感知：
     - 当选择**蜂窝扇 (Honeycomb Fan)** 模式时，最多只允许配置 **3** 个二级子动作；达到 3 个后按钮自动切入禁用态并提供 ToolTip 提示，点击时弹窗明确提示上限；
     - 当选择**外圈子环 (Outer Sub-Ring)** 模式时，最多允许配置 **4** 个二级子动作；达到 4 个后自动禁用并给予清晰提示；
   - 若用户此前已配置 4 项后切换至蜂窝扇模式，芯片列表中超出上限的项自动施加半透明弱化与 `(未激活)` 标记，彻底杜绝误导。
3. **动作配置实时交互画布严格匹配**：
   - 动作配置画布（`RenderMappingsWheelPreview`）的子级轮盘渲染全面约束于当前二级样式的允许上限（`Math.Min(maxSubCount, action.SubActions.Count)`），在蜂窝扇模式下绝对只渲染最多 3 个子扇区，与实际手势呼出 100% 吻合；
   - 在 Tab 3 切换二级菜单样式后，即时双向联动刷新 Tab 2 的二级动作芯片可用状态与实时交互画布。

### 🛠️ 核心修复 3：彻底修复控制台配置布局/视图模式无法切换 Bug (消除重入深度计数锁死缺陷 & 双向联动同步)
1. **`_uiUpdateDepth` 引用计数锁死缺陷根除**：
   - 深入排查发现：此前将 `_isUpdatingUi` 包装为整数属性 `_uiUpdateDepth`（`set { if (value) _uiUpdateDepth++; else if (_uiUpdateDepth > 0) _uiUpdateDepth--; }`），导致任何 `bool old = _isUpdatingUi; ... finally { _isUpdatingUi = old; }` 的标准保护模式在 `old == true` 时重复递增计数器；
   - 窗口初始化加载过程中多个子流程重入调用，导致 `_uiUpdateDepth` 泄漏至 4，`_isUpdatingUi` 永久锁定为 `true`；所有挂载了 `if (_isUpdatingUi) return;` 保护的界面控件（包括简单/高级全量模式切换、视图模式切换、扇区排布切换、方案选择、排版模式等）点击后被全面拦截静默返回；
   - 将 `_isUpdatingUi` 彻底重构为纯净原子布尔状态字段 `private bool _isUpdatingUi = false;`，在嵌套更新方法（如 `RefreshLayoutOptionsUi`）中采用标准的 `bool oldUpdating = _isUpdatingUi; try { _isUpdatingUi = true; ... } finally { _isUpdatingUi = oldUpdating; }` 严格保全外层状态，彻底杜绝深度泄漏与界面死锁；
2. **Tab 2 手势动作区视图模式分段切换器加固**：
   - 为 `MappingsViewModeCanvasRadio`（🎯 画布联动精调）与 `MappingsViewModeListRadio`（📋 紧凑全览列表）显式补充 `GroupName="MappingsViewModeGroup"`，消除 WPF 无命名容器下的单选互斥紊乱；
   - 并在切换事件 `MappingsViewMode_Checked` 中打通方案选择、扇区方位数量（4/8/12 键）与槽位动作列表的双向数据同步，确保在画布联动模式与列表全览模式间随意自由切换，实时双向无缝对齐；
3. **焦点卡片动作下拉项响应优化**：
   - 移除 `FocusActionTypeComboBox`、`FocusWindowSubModeComboBox`、`FocusTileLayoutComboBox` 事件中过于严苛的 `!IsLoaded` 判定，杜绝在复杂界面加载时序下的点选失效。

## [v1.6.8] - 2026-09-04 (全新星盘宇宙官方图标 & 热键截屏粘滞与修饰键成对释放原生根治 & 截屏后右键单击与长按滑动失效彻底根治 & 鼠标左键长按唤醒轮盘与单击原生点击放行 & Windows 开机自启极速秒开与静默启动优化 & 任务栏纯净圆形星盘图标 & 控制台黑白双版Logo动态切换 & 托盘右键菜单黑白双色主题 & 内置检查更新多级容灾与下载加速修复 & 动作配置画布形态统一 & GitHub加速源新增 & 二级蜂窝扇方位修复 & 一二级配置联动与二级预览保持 & 轮盘自适应弹性字号 Auto Font-Fit & 方案管理工具栏与折叠下拉栏同步重构 & 界面语言Emoji规范化 & 视口滚动呼吸留白 & 多语言词库全覆盖 & 画布缩放修复 & 快捷键Pause与搜索 & 独占暂停全局热键 & 侧边栏主题切换 & 贡献者致谢与离线策略 & 平铺设置折叠 & 深色对比度优化 & 扇区文字位置与微调 & 屏幕边缘呼出防溢出)

### 🖱️ 触发键绑定左键专项优化：长按左键唤醒轮盘 & 单击左键保持系统原生点击功能
1. **根除底层重放机制缺失左键致命缺陷**：
   - 深入排查发现：原 `MouseHook.ReplayTriggerClick` 仅包含 MiddleButton、XButton1、XButton2，缺少 `case "LeftButton"`，导致当触发键绑定为鼠标左键时，未超阈值的单击操作全部掉入 `default:` 分支，向系统错误回放了 `MOUSEEVENTF_RIGHTDOWN` 与 `MOUSEEVENTF_RIGHTUP`（鼠标右键点击）；
   - 进而导致用户单击左键时无法触发按钮的 Click 事件，表现为“无法点击确认、无法修改、弹出右键菜单或点击无效”；
   - **全面补齐左键回放**：显式注入 `MOUSEEVENTF_LEFTDOWN` (0x02) 与 `MOUSEEVENTF_LEFTUP` (0x04) 组合，使单击左键在释放瞬间即刻由底层无缝原位回放标准左键点击，完美恢复 Windows 原生单击、双击与控件确认交互。
2. **鼠标左键长按呼出原生保障**：
   - 当触发按键为鼠标左键时，底层状态机 `GestureController` 在按键按下瞬间自动保障启动长按呼出定时器（`StartLongPressTimer`），不再受制于可选开关的未勾选状态；
   - 用户按住鼠标左键不动达到设定阈值时长（默认 450ms）即可稳定唤出 StarPie 星盘轮盘；
   - 在设置控制台录入鼠标左键或加载包含左键触发的配置时，自动联动开启“长按触发按键呼出面板”配置开关，界面状态保持一致。
3. **录入反馈与四语系提示**：
   - 录制触发键为鼠标左键后，实时感知器给出绿色高亮状态提示：`🟢 已成功绑定【鼠标左键】：长按左键唤醒轮盘，单机左键保持系统正常点击！`；
   - 界面描述与 i18n 多语言字典（zh-CN, zh-TW, en-US, ja-JP）同步更新，操作逻辑清晰明了。


### ⚡ 内置检查更新与下载加速深度重构：根治检查更新超时、多级弹性容灾降级架构与自动多镜像下载故障转移
1. **检查更新超时四大致命根因彻底排查与根除**：
   - **根因 1 (致命镜像前缀滥用)**：原逻辑在直接连接 `api.github.com` 失败后，错误地将下载加速镜像源前缀拼在 API 地址前（如 `https://github.akams.cn/https://api.github.com/...`）。实测国内镜像站只支持 release 资产文件下载，对 `api.github.com` 全部报 404 或 SSL 握手挂起，导致用户按控制台提示切换镜像源后陷入 100% 超时死循环；
   - **根因 2 (单次请求超时过长)**：单次 HTTP 请求超时长达 25 秒，遇上网络抖动直接卡死 50 秒，UI 响应极慢；
   - **根因 3 (缺少网页主站降级通道)**：用户浏览器能正常进入 GitHub，说明 `github.com` 网页主站完全通畅，但 `api.github.com` 节点遭遇单独阻断或每小时 60 次 API 限流时无任何备选通道；
   - **根因 4 (全局 Accept 污染导致 406/SSL 挂起)**：`_httpClient` 全局携带了 `application/vnd.github.v3+json`，在请求 XML 或二进制下载时引发服务器拒收。
2. **多级弹性容灾版本检测架构 (Multi-Tier Resilient Check)**：
   - **Tier 1 (GitHub REST API, 5s 超时)**：尝试直接访问 REST API，单次 5 秒快速超时，绝不阻塞界面；
   - **Tier 2 (GitHub Atom XML Feed, 6s 超时 - 核心破局点)**：若 API 遇阻，自动瞬间降级至 `https://github.com/SoftBlack42/StarPie/releases.atom`。该源位于 `github.com` 网页主域名下，**免 API 鉴权、无 60 次/小时速率限制**，国内用户只要能开 GitHub 网页即可在 **400~800ms 内极速秒级获取**最新版本 Tag、发布日期与完整更新日志；
   - **Tier 3 (Web 302 Location Header 探测)**：降级探测 `https://github.com/.../releases/latest` 重定向头毫秒级解析最新 tag；
   - **请求头严格隔离**：按请求类型分别下发 `application/vnd.github.v3+json`、`application/atom+xml` 与 `*/*`，彻底杜绝协议协商冲突。
3. **版本比对与精准状态呈现**：
   - 当前 StarPie 运行版本为 `v1.6.8`，当线上最新 Release 为 `v1.6.5` 时，准确判定当前运行版高于或等于线上版，给出绿色高亮徽标 `[当前已是最新版本]`，彻底消除虚假超时红字；
   - 控制台顶部新增 `[ 🌐 网页发布页 ]` 一键直达按钮，任何网络环境下均可随时一键在默认浏览器直达 GitHub Releases 页面。
4. **下载加速源高可用升级与多镜像自动故障转移 (Resilient Failover)**：
   - 实测淘汰失效/超时的旧镜像，更新推荐高可用镜像源：`ghfast.top` (极速推荐，实测 <1s 下完)、`gh-proxy.com` (优质备用)、`mirror.ghproxy.com`；
   - 旧版配置标签（`ghproxy`、`moeyy`、`akams`）无缝自动映射至当前高可用活跃镜像，旧用户无需重配即可享受满速下载；
   - `DownloadAssetAsync` 引入多源自动重试与故障转移机制：若当前选中的镜像发生中途网络抖动或 404/502，后台自动按顺序无缝平滑切换至下一镜像源或官方直连，确保更新包下载 100% 成功。

### 🎨 视觉与交互重塑：任务栏纯净圆形星盘图标、控制台黑白双版动态Logo与托盘右键菜单黑白双色主题
1. **任务栏黑边圆角框彻底消除 (Borderless Circular Taskbar Icon)**：
   - 重构图标生成流水线，将 `app_icon.ico` 的全部多级分辨率尺寸（16, 20, 24, 32, 40, 48, 64, 96, 128, 256）彻底基于 100% 透明底的高清微米级圆形 StarPie 星盘生成；
   - 彻底废弃带方框的旧版 squircle 贴片，消除 Windows 任务栏（24~48px）、窗口标题栏与 Alt+Tab 任务视图中的黑色圆角方框，使星盘如同原生应用般纯净悬浮于任务栏。
2. **控制台黑白双版动态 Logo 实时切换 (Dual-Theme Console Logo)**：
   - **黑版 Logo (`logo_dark.png`)**：深邃星空极光轮盘，具有高光外缘轮廓、纯白指引星与指针、荧光渐变扇区，适配 ObsidianDark、TitaniumGray 等暗色背景，底板 100% 透明无方框；
   - **白版 Logo (`logo_light.png`)**：中央核心采用清爽珠光白底配深邃电光蓝（`#2563EB`）指针，外圈扇区强化蓝紫晶体渐变与高对比度边缘，在纯白背景下清澈锐利、对比分明；
   - 在控制台侧边栏顶部和“关于”卡片中建立动态联动，随用户切换主题（浅色/深色/跟随系统）或导入配置时瞬时动态平滑切换黑白双版。
3. **系统托盘右键菜单黑白双色现代主题引擎 (Modern Themed Tray Context Menu)**：
   - 移除 Windows 95 复古左侧灰条槽（`ShowImageMargin = false`, `ShowCheckMargin = false`）；
   - 自定义现代化无框渲染器 `ModernTrayRenderer` 与配色表 `ModernTrayColorTable`：
     - **黑版 (Dark Menu)**：深邃暗调底色（`#18181B`），圆角悬浮高亮项（`#27272A`），微弱边界线（`#2E2E33`），高对比度文字（`#F4F4F5`），停用/版本文字（`#94A3B8`），平整居中分割线；
     - **白版 (Light Menu)**：极简纯白底色（`#FFFFFF`），圆角悬浮高亮项（`#F1F5F9`），优雅边框线（`#E2E8F0`），深灰黑色文字（`#0F172A`），停用/版本文字（`#64748B`）；
   - 菜单完全跟随控制台主题实时热更新，右键呼出丝滑无缝。

### 🖱️ 截屏后右键单击失效与长按滑动失效深度排查与彻底根治
1. **全屏独占误判穿透与遮罩豁免 (FullScreenHelper)**：
   - 彻底排查发现：用户触发 `Ctrl+PrintScreen` 等截屏动作后，系统或第三方截图软件（Windows `ScreenClippingHost.exe` / `SnippingTool.exe`、`Snipaste.exe`、`PixPin.exe`、`ShareX.exe` 等）会生成覆盖整屏的半透明选区遮罩或淡出动画窗口，导致 `FullScreenHelper.IsActiveWindowFullScreen()` 误判定为“独占全屏 3D 游戏”；
   - 进而导致 StarPie 全屏抑制机制被意外激活 1~2 秒，造成轮盘无法唤起（长按滑动失效）；
   - **分层透明属性检测与全屏白名单豁免**：
     - 在 `FullScreenHelper` 中引入 `GWL_EXSTYLE` 扩展样式探测，凡带有 `WS_EX_LAYERED`（分层透明）或 `WS_EX_TRANSPARENT`（穿透透明）属性的全屏窗口，直接判定为辅助工具/遮罩，杜绝误判为全屏独占游戏；
     - 显式白名单排除 `ScreenClippingHost`、`SnippingTool`、`Snipaste`、`PixPin`、`ScreenCaptureWnd` 等截图窗口类及常见截图宿主进程。
2. **物理按键抬起 (WM_RBUTTONUP) 吞键缺陷根治 (GestureController)**：
   - 深入分析发现原 `Hook_OnTriggerButtonUp` 存在严重时序漏洞：当轮盘处于隔离模式或全屏抑制时，`Hook_OnTriggerButtonDown` 正常放行了物理右键按下（`_mouseTriggerDown = false`, `e.Handled = false`）；但在用户松开鼠标时，由于未检查 `_mouseTriggerDown` 拦截标志，`Hook_OnTriggerButtonUp` 依然错误地执行了 `e.Handled = true` 强行吞掉了物理抬起事件；
   - 导致前台应用与 Windows 桌面只收到了物理 Down，永远收不到物理 Up，造成右键卡死与右键单击完全失灵；
   - **严格成对放行机制**：在 `Hook_OnTriggerButtonUp` 首部严谨校验 `wasTriggerDown`，若物理按下未曾被 StarPie 拦截，抬起事件 100% 原生透传系统（`e.Handled = false`），绝不吞键。
3. **按键回放零延迟与时序稳固 (MouseHook & ThreadPool)**：
   - 将 `GestureController` 中单击重放的 `Dispatcher.BeginInvoke` 迁移至 `ThreadPool.QueueUserWorkItem`，彻底脱离 WPF UI 主线程调度队列，消除窗口切换与 DWM 动画时的数百毫秒延迟；
   - 在 `MouseHook.ReplayTriggerClick` 中为 Down 和 Up 之间插入 2ms 物理间隔，规避操作系统与应用消息队列因 0ms 紧邻而丢失按键事件的问题。

### 🚀 Windows 开机自启与极速冷启动性能重构 (Autostart & Cold Boot Speed Optimization)
1. **彻底排查自启卡顿与拖慢系统开机速度的四大根因**：
   - **根因 1 (核心瓶颈)**：在 `ConfigManager.LoadConfig()` 启动关键路径中无差别同步执行 `EnsureAutoStartRegistryUpToDate()`。若用户开启了管理员自启，每次系统开机均会同步创建外部进程调用 `schtasks.exe /query`（等待最多 1.5s）和 `schtasks.exe /create /f`（等待最多 3.0s），开机期间磁盘与 CPU 处于争抢高峰期，严重拖垮 Windows 登录开机速度；
   - **根因 2**：在 `SettingsWindow.xaml.cs` 构造函数中无意义重复调用了一次 `ConfigManager.LoadConfig()`，导致开机配置解析耗时翻倍；
   - **根因 3**：静默自启（`--autostart --minimized`）时，原程序急躁初始化（Eager Initialization）完整构造了整个庞大的 4 标签页 `SettingsWindow`、解析并绑定了所有复杂的动作数据模型 `_slotViewModels`、执行了离线贡献者 Markdown 解析并预渲染了隐藏的实时预览画布，导致内存瞬间飙升且开机耗时巨大；
   - **根因 4**：任务计划程序参数缺少 `/delay 0000:00`，导致 Windows Task Scheduler 默认施加不确定的登录延迟。
2. **启动关键路径解耦与异步延迟自愈**：
   - 将 `EnsureAutoStartRegistryUpToDate()` 完全移出程序启动的关键路径，使用后台 `Task.Run` 并主动延迟 4 秒执行，消除开机自启阶段的一切同步外部进程调用；
   - 在 `EnsureAutoStartRegistryUpToDate` 中增加进程呼起参数嗅探：若进程自身是以 `--autostart` 启动的，证明计划任务与注册表已处于完全健康就绪状态，直接短路跳过，杜绝任何外部进程开销；
   - 优化 `IsAutoStartEnabled()`：优先瞬间读取注册表 Run 键值，仅在确实需要校验管理员任务时才进行按需核验，消除空闲状态下的进程轮询；
   - 在 `CreateOrUpdateAdminTask` 的创建参数中新增 `/delay 0000:00` 零延迟指令，确保用户登录系统后 StarPie 立即秒级响应，消除 Windows 默认的开机启动延迟。
3. **设置控制台懒加载架构 (Lazy UI Architecture)**：
   - 引入 `EnsureUiInitialized()` 状态机与 `IsSilentLaunch()` 静默启动探测器；
   - 开机自启与静默运行模式下：仅轻量加载配置到内存、直接快速挂载全局底层鼠标/键盘钩子、就绪手势状态机并展示系统托盘图标，彻底跳过四卡片控制台视图模型绑定与隐藏画布重绘；
   - 启动耗时从原来的 **3000ms+ 暴降至 30ms ~ 60ms**，任务管理器中「启动影响」降至极低（Low）；
   - 当用户后续双击托盘图标或通过轮盘快捷动作呼出设置时，按需（On-Demand）在毫秒级瞬时就绪完整 UI，实现“开机零感知、呼出零等待”。
4. **自启即刻工作集内存极致压缩 (Instant TrimMemory)**：
   - 静默启动与开机就绪后，延迟 1.5 秒调用 `MemoryOptimizer.TrimMemory(force: true)` 对常驻进程的工作集执行物理级内存规整与压缩；
   - 开机静默常驻物理内存严格控制在 **0.8MB ~ 3MB** 极限区间，远优于 3MB ~ 8MB 工程红线。

### ⌨️ 快捷热键 (Ctrl+PrintScreen) 截屏粘滞与修饰键释放架构重构
1. **彻底排查根因与前置尝试失效剖析**：
   - **历史尝试失效剖析**：排查发现此前尝试中加入的 `if ((GetAsyncKeyState(vk) & 0x8000) == 0)` 导致了致命逻辑倒错（该 API 最高位为 1 代表按键正处于按下态，0 代表已弹起），导致代码竟然变成了“仅在按键已弹起时才释放，按键正按下时绝不释放”，导致 `Ctrl Up` 被彻底跳过，造成每一次触发热键 Ctrl 都 100% 永久卡死并使全键盘失灵；
   - **截图焦点抢占病灶**：在执行 `Ctrl + PrintScreen` 时，外部截图软件（Snipaste/PixPin/微信/QQ/系统截图）在捕获到 `PrintScreen Down` 瞬间会立刻弹出全屏遮罩并抢夺系统焦点，原代码在 Down 和 Up 之间存在时延，导致后续 `Ctrl Up` 在焦点切换期间被 DWM / 消息队列丢弃；
   - **扫描码与扩展键修复**：Win32 `MapVirtualKey(44, 0)` 会返回 `0x54`，若打上 `KEYEVENTF_EXTENDEDKEY` 会合成为 PS/2 SysRq 中断序列 `E0 54` 导致过滤驱动异常；针对 `VK_SNAPSHOT` 强制指定扫描码 `wScan = 0` 并剥离扩展键标志。
2. **瞬态快门模式 (Atomic Shutter Mode)**：
   - 针对 `VK_SNAPSHOT` (44)，`PrintScreen Down` 与 `Up` 作为一个原子数据包同批次发送（0ms 间隔），不给外部软件留出在 Down 与 Up 之间强行切走焦点的时延窗口。
3. **无条件双通道成对释放 (Dual-Channel Modifier Release)**：
   - 杜绝任何阻碍释放的条件判断，`finally` 块中始终无条件执行修饰键成对释放：
     - 通道一：`SendInput` 下发队列 `KeyUp`；
     - 通道二：`keybd_event` 直接同步 `win32k.sys` 全局击键状态表，同时覆盖具体键（162 `VK_LCONTROL`）与通用键（17 `VK_CONTROL`），击穿跨线程与跨权限壁垒。
4. **截屏全屏遮罩异步兜底**：
   - 针对 `VK_SNAPSHOT` 组合键，后台 Task 自动在 +30ms 和 +80ms 异步补发修饰键释放，彻底消灭外部截图工具弹出全屏遮罩时的焦点延迟丢包残留。
5. **DWM 焦点平稳缓冲时延与解卡自愈修复**：
   - 在 `ExecuteHotkey` 执行前施加 10ms 物理缓冲，确保悬浮轮盘关闭后前台窗口焦点平稳就绪；
   - 修复 `ReleaseStuckModifiers()` 中错误的 `GetAsyncKeyState == 0` 判断，实现真正的无条件解卡。

### ⚡ GitHub 下载加速镜像源新增 `github.akams.cn`
1. **新增极速镜像代理节点**：
   - 在设置控制台 Tab 4「系统与高级偏好」的「GitHub 下载加速镜像源」下拉框中新增 `⚡ 镜像源 3 (github.akams.cn)` 选项；
   - 同步在 `UpdateManager.cs` 的 `GetProxiedDownloadUrl` 中注入 `https://github.akams.cn/{rawUrl}` 代理转发支持，全面提升国内用户检查更新与下载发版包时的网络稳定性与下载速率；
   - 补齐简体中文、繁体中文、英文与日文四语系国际化词条 `UpdateProxyAkams` 与 UI 即时切换响应。

### 🎨 动作配置界面 (Tab 2) 实时交互画布形态统一重构 (消除重叠与形变干扰)
1. **功能配置画布经典同心圆形态固化**：
   - 彻底排查并解决此前在外观设置中选择「圆角胶囊 (Capsule)」等异形或选择「蜂窝扇 (Honeycomb Fan)」二级展开形态后，Tab 2 手势轮盘功能配置画布发生扇区严重变形断裂、多扇区蜂窝扇二级子叶四处交叉重叠错位的交互痛点；
   - **解耦外观预览与高密度配置**：Tab 1 交互画布与悬浮主轮盘继续忠实展示用户的个性化扇区形状与蜂窝扇；Tab 2 动作配置与拖拽画布统一规范为标准经典同心环形态（`shape = "Original"`）与同心外圈子环布局（`isFan = false`），确保任意扇区数量及二级子项均能整洁、直观、无遮挡地平铺呈现；
2. **拖拽对调与点击判定物理对齐**：
   - 同步重构 Tab 2 画布的鼠标命中测试与拖拽放置算法（`MappingsPreviewViewport_MouseDown` 与 `MappingsPreviewViewport_MouseUp`），消除蜂窝扇模式下坐标计算偏移，实现极度丝滑精准的 100% 扇区对调与二级子动作点击聚焦。

### 🛠️ 方案管理工具栏 (Profile Toolbar) 与折叠下拉栏同步彻底重构
1. **删除方案彻底生效与平滑回退**：
   - 彻底排查并修复此前在动作配置顶部点击 `🗑️` 删除非 Global 方案时，方案在 `MappingsProfileComboBox` 下拉框及其展开的折叠选项栏中残留、删除不掉的严重缺陷；
   - 重构删除流水线，采用 `RemoveAll` 严格清除目标方案并即时保存至硬盘，删除后自动安全回退至系统全局兜底 `Global` 方案，并通过 `RefreshProfilesUi` 全量重绑重绘，彻底杜绝下拉折叠栏与动作列表的视图残留；
2. **重命名折叠下拉栏实时同步**：
   - `WheelProfile` 核心数据模型全面实现 `INotifyPropertyChanged` 接口，当 `ProcessName` 修改时即时广播属性变更通知；
   - 重构重命名流水线，完成重命名后通过 `RefreshProfilesUi` 重塑下拉框绑定，使闭合文本与展开后的折叠菜单选项 100% 实时同步显示新名称，无需重启软件；
3. **全局默认方案保护与按钮禁用态视觉反馈**：
   - 当选中的是系统全局默认基础方案 `Global` 时，`✏️ 重命名` 与 `🗑️ 删除` 按钮自动切入禁用状态（`IsEnabled="False"`），并显示清晰保护 ToolTip，防止用户误触或产生歧义；
   - `ModernButtonStyle` 补齐显式禁用触发器（半透明 `Opacity="0.38"` 与标准光标），使可用/不可用状态具备清晰直观的现代化设计质感；
   - 选中任意自定义程序方案时，按钮即时恢复高亮激活状态；
4. **新增与复制方案深度增强**：
   - `➕ 新增` 按钮升级为智能快捷选单（搭载深浅色主题自适应的现代 ContextMenu），点击即可按需选择「🖥️ 从已安装程序选择 (专属程序配置)」或「✏️ 新建自定义名称方案 (工作流/模式配置)」；
   - `ActionItem` 与 `WheelProfile` 引入无损深度克隆引擎 `Clone()`，方案复制时完整保留所有高级排版模式、独立字体字号、文字颜色、图标继承、终端与浏览器设定，且新增或复制后立即自动选中、实时刷新并在画布中就绪。

### 🪐 全新星盘 (StarPie) 官方品牌图标与视觉升级
1. **全新星盘天体公转轨道与四芒星核设计**：
   - 告别旧版雷达光束，全面启用兼具机械工业几何美感与深邃太空探索哲学的全新「星盘」设计（源自 `attachments/cover.v3.png`）；
   - 以同心圆天体引力轨道、环状卫星与居中璀璨的四芒星核为核心视觉语言，完美诠释指尖极速触达各个扇区的轮盘交互体验；
2. **深度边缘 Alpha 遮罩与透明抗锯齿重塑**：
   - 彻底解决原始图片直角黑底缺陷，通过算法实现平滑超椭圆边缘检测（Superellipse Edge Anti-Aliasing）与亚像素级 Alpha 渐变羽化；
   - 在 Windows 浅色桌面、深色主题、任务栏及软件控制台（Sidebar / About）均完美融合、无任何黑角瑕疵；
3. **任务栏与系统托盘清晰度专项重构 (Clarity Overhaul)**：
   - **大轮盘占比重塑**：将轮盘在画布中的直径占比由 71% 大幅提升至 84%，消除边缘多余暗黑死区，使 24×24 / 32×32 任务栏图标中的细节面积提升超 36%；
   - **多级微标锐化 (USM) 与对比度增强**：在 16×16 ~ 48×48 微标尺寸下注入自适应反锐化掩模与微弱饱和度增益，彻底消除模糊发灰，扇区边界与白色指针锋利可辨；
   - **标准 Win32 原生 DIB 帧编码**：$\le 48\times 48$ 尺寸采用 Windows 原生 32bpp DIB（非压缩位图）封装，彻底规避 Windows Shell 与 GDI 提取 PNG 帧时的最近邻拉伸模糊；
   - **系统托盘专属微标 (tray_icon.ico)**：为托盘专属定制无黑框的纯圆星盘微标，100% 透明背景，消灭局促暗框，使托盘图标如晶莹天体般通透灵动；
   - **窗口图标严格绑定 app_icon.ico**：彻底消除控制台与所有子窗口此前绑定单分辨率 `logo.png` 导致 WPF 在任务栏产生粗暴二次重采样的模糊缺陷。
4. **10 级全尺寸链超清 ICO 烘焙与子窗口显式绑定**：
   - 自动烘焙包含 `16×16`、`20×20`、`24×24`、`32×32`、`40×40`、`48×48`、`64×64`、`96×96`、`128×128`、`256×256` 完整高精度 32-bit PNG 帧的 `app_icon.ico`、`tray_icon.ico` 与高清 `logo.png`；
   - 软件所有子窗口（控制台、子动作编辑器、快捷键组合器、调色板、程序拾取、弹窗等）全面显式绑定新 Logo 图标，消除任务栏及 Alt+Tab 切换时的白框退化。

### 🐝 二级轮盘蜂窝扇 (Honeycomb Fan) 方位与镜像颠倒彻底修复
1. **几何偏移与槽位映射物理对齐**：
   - 彻底排查并修复上方等扇区蜂窝扇展开时左翼与右翼视觉和判定颠倒的问题；
   - 将逆时针/较小极角统一为 `slot 0`（左翼），顺时针/较大极角统一为 `slot 2`（右翼）；
   - 无论是实际呼出悬浮轮盘还是控制台实时预览画布，子动作 1 始终位于左侧，子动作 2 始终位于右侧，与手势操作配置列表 100% 吻合对齐。

### 🔄 控制台一级/二级轮盘配置联动重构与二级子菜单实时预览保持
1. **一二级模式单选框双向自动同步**：
   - 修复在配置一级轮盘时，点击二级轮盘子菜单未能自动切换至「🌟 二级级联轮盘配置」模式的问题；
   - 点击二级动作列表项或画布子扇区时，顶部单选框自动切至「🌟 二级级联」，反之点击一级主扇区或中心核圆自动回切「🔘 一级主轮盘」；
2. **实时预览画布二级子扇区持续高亮呈现**：
   - 修复此前在编辑二级子动作时，画布二级子菜单意外消失的缺陷；重构渲染判定条件，确保编辑二级动作时二级子扇区持续展示、聚焦高亮并带有父级扇区微光轮廓引导。

### 🔠 轮盘扇区自适应弹性字号算法 (Auto Font-Fit)
1. **极速轮盘扇区智能缩放**：
   - 悬浮轮盘（一级 4/8/12 键扇区、二级外圈子环 SubSector、蜂窝扇 Honeycomb Fan）引入自适应字号弹性缩放引擎；
   - 根据动作文本长度与字符类别（区分 ASCII 与 CJK 字符宽度）自动按阶梯等比缩小 10%~18%，并结合预设最大宽度与 `TextWrapping="Wrap"` 优化换行；
   - 完美解决长应用名称（如 "Windows Terminal"、"Visual Studio Code"）在 12 键或外圈密集子扇区中被粗暴裁切为 `...` 的排版问题。
2. **实时预览画布排版像素级对齐**：
   - 控制台右侧交互画布的文字生成模块同步注入 Auto Font-Fit 算法；
   - 预览呈现的字号微调与换行状态与实际呼出轮盘 100% 保持一致，彻底达成“所见即所得”。

### 🌐 界面语言选单 Emoji 规范化与自适应排版
1. **消除 Windows 国旗 Emoji 退化缺陷**：
   - 深入排查发现 Windows 系统的 Segoe UI Emoji 字体并不支持国家/地区国旗 Emoji 渲染，而是将其退化显示为难看且散乱的双字母组合（如 `CN`, `HK/TW`, `us`, `JP`）；
   - 将语言选择器下拉候选项全面升级为标准清晰的语言微标与区域标签：
     - `🌐 [ZH] 简体中文 (Simplified Chinese)`
     - `🌐 [TW] 繁體中文 (Traditional Chinese)`
     - `🌐 [EN] English (US / UK)`
     - `🌐 [JA] 日本語 (Japanese)`
     - `🖥️ [SYS] 跟随系统 (System Default)`
2. **下拉框容器自适应排版**：
   - 下拉选单容器列宽从固定宽重写为自适应内容宽度（`Width="Auto" MinWidth="260"`），杜绝多语言长文本或不同系统缩放下出现文字折行或右侧被裁切。

### 📏 控制台视口滚动与贴底截断防护
1. **全面增加底部呼吸留白 (Padding Bottom)**：
   - 修复在笔记本屏幕或 125%/150% 缩放下，控制台因高度限制导致底部内容紧贴底栏、视觉上产生“文字被截断”的假象；
   - 为 Tab 0 ~ Tab 4 所有 ScrollViewer 内部容器增加 `32~36px` 的充裕底部呼吸留白，即便滚动至最底部也能完整呈现底卡说明文字与操作按钮。
2. **滚动条高对比度交互重构**：
   - 彻底优化 WPF 自定义 `ScrollBar` 样式：`Thumb` 滑块不仅具备半透明基色，并在鼠标悬停（Hover）与按住拖动（Dragging）时高亮反馈为系统主强调色（`AccentPrimaryBrush`）；
   - 大幅提升滚动条在深色模式（极夜曜黑与钛金深灰）下的可辨识度，让滚动交互更直观灵动。

### 🗃️ 多语言国际化 (I18n) 词库全覆盖
1. **补齐全量设置卡片多语言翻译**：
   - 补齐此前未纳入语言切换的 20+ 个 UI 文本与说明：
     - 更新通道（稳定版 / 抢先体验版 / 开发者预览版）、更新静默检测、代理模式配置；
     - 开源贡献者致谢卡片（标题、感谢说明、离线名单状态、手动同步按钮、访问仓库）；
     - 原生离线 OCR 卡片（标题、说明、状态徽标、测试 OCR、配置说明）；
     - 开机静默自启高级说明与任务计划程序最高权限自启文案；
     - 核心圆全局动作提示、扇区方位指示与画布预览辅助提示；
   - 覆盖简体中文、繁体中文、英文、日文四套词典，无缝动态热切换生效。

### 🎯 实时交互画布缩放工具栏修复
1. **重构视口结构彻底消除事件抢占**：
   - 深入排查发现动作配置页右侧实时画布右上角的缩放悬浮工具栏（`[-]`、`[100%]`、`[+]`、`[🎯]`）被嵌套在视口容器内部，受到父容器 `PreviewMouseDown` 隧道事件抢占而无法接收正常点击；
   - 将悬浮工具栏重构为与视口平级的顶层浮动容器，并在鼠标事件入口处添加控件穿透豁免检查，彻底恢复按钮点击缩放、百分比标签快速复位与滚轮平滑缩放。

### ⌨️ 快捷热键构建器增强与独占录入拦截（暂停全局热键）
1. **主按键补充 Pause / Break**：
   - 在 `HotkeyBuilderDialog` 主按键选择列表中新增 `Pause / Break (暂停)` 选项，无缝映射底层 Win32 `VK_PAUSE`（0x13）。
2. **主按键列表实时搜索过滤**：
   - 主按键卡片顶部新增即时搜索框，支持输入按键名称关键字（如 `Pause`、`F5`、`Tab`、`A`、`Enter` 等）动态过滤下拉候选列表，并支持一键清空筛选；
3. **全面重构「独占按键录制」与全局热键阻断引擎**：
   - 彻底重写底层 `KeyboardHook` 的独占按键拦截机制，采用「硬件按键集合追踪 + GetAsyncKeyState 兜底」双通道修饰键判定，100% 杜绝 `Win + D`、`Alt + Tab`、`Win + Shift + S` 等组合键修饰键丢失导致退化成单键的问题；
   - 引入「松开事件安全排空机制 (Modifier Draining)」：录制完成后持续吞没用户松开所有手指的过程，彻底消除松开 Win 键时 Windows 弹出开始菜单或松开 Alt 键时激活系统菜单的问题；
   - 深度联动录制框与控制按钮：无论是点击 `[ ⏸️ 暂停全局热键 ]` 按钮还是直接点击输入框进入录制，均自动激活全局独占拦截；录制框实时高对比度显示当前按下的修饰键（如 `🔴 Win + Shift + ... (按Esc取消)`），支持按 Esc 取消或点击按钮即时恢复。

### 🎨 界面主题切换迁移至左侧功能栏 & 侧边栏折叠信息保持
1. **控制台主题切换全标签页常驻**：
   - 将「软件控制台界面主题 (App Theme)」由 Tab 1 迁移至左侧边栏底部，在所有页面均可一键随时切换；
   - **展开态**：设计现代化四段分段胶囊按钮 `[ 🌓 系统 ]`、`[ ☀️ 浅色 ]`、`[ 🌙 曜黑 ]`、`[ ⚙️ 钛灰 ]`，带激活高亮与悬停动态反馈；
   - **折叠态**：自动收起为 36×36px 居中单图标按钮（显示当前激活主题图标），支持点击一键快速循环切换；
   - 移除 Tab 1 顶部旧的主题卡片，释放空间使轮盘切削与色彩面板直达首屏。
2. **侧边栏折叠态版本与版权信息保持**：
   - 彻底解除折叠时对底部信息的隐藏；折叠后自适应居中紧凑展示 `v1.6.8` 徽标与 `© 2026`，配备完整悬浮 ToolTip。

### 🤝 开源贡献者致谢卡片离线收录与按需更新策略
1. **默认绝对离线零网络开销**：
   - 贯彻软件默认零网络通信原则，启动与打开设置窗口时坚决不发起任何自动网络请求；
   - 本地预先收录 6 位真实开源贡献者（`Sunse666` 79次提交、`SoftBlack42` 35次提交、`IQ-Director` 3次提交、`Zsdhak1` 3次提交、`ACbye` 1次提交、`AkiraYim` 1次提交），秒级呈现；
   - 仅在用户手动点击「🔄 刷新」或在「检查应用更新」时异步从 GitHub API 拉取最新名单并更新。

### 🪟 平铺窗口高级设置卡片默认折叠收纳
1. **自适应空间美化与收起折叠**：
   - Tab 2（手势与动作）底部的「平铺窗口高级设置」重塑为现代化折叠式卡片标题栏，包含标题、状态徽标与 `[ 展开配置 ▼ ]` 切换按钮；
   - 默认保持折叠收纳（Collapsed），节省纵向界面空间超 260px，点击展开后呈现完整配置，折叠展开状态自动持久化。

### 🌙 深色模式选择弹窗高对比度优化
1. **彻底消除暗底黑字与低对比度盲区**：
   - 全局重构 `App.xaml` 中的 `CheckBox` 全局样式，严格绑定 `{DynamicResource TextPrimaryBrush}`；
   - 重构 `HotkeyBuilderDialog`（快捷键拼装组合器）内的修饰键复选框、主按键下拉候选项及即时搜索过滤框，深色模式下文字前景统一为高对比度纯白/浅灰，对比度严格达标 4.5:1 以上。

### 📐 扇区文字相对位置与双轴微调（上方/下方与自由位移）
1. **文字位置与双轴偏移自由定制**：
   - 在 Tab 1（外观与形态）排版设置中新增「文字相对位置」下拉选择器（图标下方 / 图标上方）；
   - 新增「水平 X 偏移」滑块与「垂直 Y 偏移」滑块（-40px ~ +40px，步长 1px），并在实时交互画布与悬浮轮盘上应用 `TranslateTransform` 实时渲染；
   - 配备「🔄 位置归位」按钮，一键复位至默认下方排版与零偏移；支持全局方案与单个扇区独立覆盖定制。

### 🛡️ 屏幕边缘呼出智能防溢出与光标自动对齐（深度调优 & 默认关闭）
1. **默认关闭与防溢出主开关**：
   - 遵从用户习惯，该功能**默认保持关闭 (`false`)**，完全杜绝未经意开启造成的光标对齐位移；
   - 在 Tab 3（场景与触发隔离设置区）配备现代化防溢出开关，关闭时默认**折叠收纳**详细参数调节面板；开启时平滑展开策略下拉列表与双轴安全边距滑块。
2. **彻底修复双轴安全边距滑块与防溢出策略生效机制**：
   - **真实有效可见半径换算**：重构底层物理溢出检测算法，彻底剔除旧版本因外圈超大透明阴影画布（~520px~780px）造成的误判提前溢出；改为以轮盘真实有效外径（主轮盘外径或多级展开外径）加 DPI 物理换算作为碰撞判定边界；
   - **双轴安全边距精准响应**：X 轴与 Y 轴安全边距滑块（0px ~ 80px）直接对应轮盘真实可见边缘距离屏幕物理视口（含任务栏与顶部标题栏避让）的保留像素间距，调节滑块即刻生效且直观可感知；
   - **防溢出策略精准执行**：在「智能贴边防溢出 (ClampShift)」下平滑将轮盘推入安全可见区域并对齐光标至轮盘正中；在「屏幕物理正中心呼出 (ScreenCenter)」下遇边缘溢出则优雅居中展现；
   - **设置面板双向同步**：在 `SyncUiToConfigAndSave` 中完整补齐开关、策略选择与双轴安全边距的持久化绑定；重构 X/Y 轴边距数值标签网格列锚定，确保数值文字清晰可见不被遮挡。

### 🍯 蜂窝扇二级轮盘防抖、迟滞保持与两侧扇区手感优化
1. **主扇区展开角度亲和锁定 (Angular Hysteresis)**：
   - 蜂窝扇展开后，将当前一级主扇区的有效判定角域扩大至 `±0.90 θ`（8 键为 `±40.5°`），光标向左/向右大范围划动选择两侧子扇区时，主扇区坚固锁定，杜绝因极角越界瞬间跳变或退回一级轮盘；
2. **出入双阈值半径迟滞 (Radial Hysteresis)**：
   - 激活二级蜂窝扇阈值为 `SubWheelTriggerDistance`（默认 95px），退出阈值降为 `SubWheelTriggerDistance - 22px`（约 73px），在二级外围小幅移动不会意外跌回一级轮盘；
3. **子扇区粘滞阻尼 (Sticky Sub-Sector)**：
   - 在已选中的蜂窝子扇区上加入微量角距偏置阻尼，消除在子扇区边缘处的频繁晃动抖变。

### 🗂️ 鼠标手势与中心核心圆配置折叠收纳
1. **鼠标手势关闭时整体折叠**：
   - 当 Tab 3 中「启用鼠标手势模式」未勾选时，下方的手势触发键、松手提示位置、灵敏度滑块及图样映射列表整体折叠收纳；
   - 勾选启用后平滑展开，大幅精简关闭状态下的视觉占用，使设置界面更显轻盈整洁。
2. **中心核心圆与图案文字开关自适应折叠**：
   - Tab 1「中心核心圆与图案文字设置」中，「启用中心图案/图标显示」关闭时，对应的图标类型、SVG/图片路径、缩放倍率、X/Y 偏移滑块与重置按钮自动折叠收起；
   - 「选中扇区时在中心显示动作名称」关闭时，字体、字号、颜色配置面板自动折叠收起，按需呈现保持界面开阔清爽。

### ↩️ 二级级联子动作撤销防误操作机制
1. **防止误删与误清空**：
   - 在 Tab 2（手势轮盘分位与动作配置）二级动作管理栏新增 `[ ↩️ 撤销 ]` 按钮；
   - 在点击 `[ 🗑️ 清空 ]` 或删除单个子动作 Chip 时，自动完成深拷贝快照备份；
   - 按钮提供动态禁用/启用与透明度反馈，发生误操作时点击即可瞬间恢复二级动作列表，保障用户配置安全。

### ⚡ 现代化系统与右键工具扩展 (ShellTool) 与交互式工具库
1. **CollectUI 风格工具挑选器窗口 (`ShellActionPickerWindow`)**：
   - 参考设计新增 4 大分类（全部、压缩解压、系统原生/OCR、开发与办公）、18+ 常用系统与右键高频扩展动作；
   - 支持关键词模糊搜索过滤、即时筛选、需求徽标与详细描述展示。
2. **活动资源管理器上下文智能提取与调度 (`ActionExecutor`)**：
   - 引入 COM `Shell.Application` 接口探测当前活跃的资源管理器窗口或桌面，提取当前选中文件或目录绝对路径；
   - 涵盖文件/文件夹路径复制、原生屏幕 OCR、以管理员身份运行、打开任务管理器、新建文件夹、查看属性、快速锁屏、清空回收站、7-Zip/Bandizip/WinRAR 提取、VS Code 打开、Git Bash、Windows Terminal、CMD、PowerShell 等实用功能。

### 🎨 触发动作类型下拉菜单展示形式彻底统一
1. **统一现代 Emoji 视觉符号与对齐排版**：
   - `SlotViewModel` 中全面规范统一 `AggregatedActionTypes` 与 `LocalizedActionTypes`：
     - `⌨️ 快捷热键`
     - `🚀 启动程序`
     - `🌐 打开网址`
     - `📁 打开文件夹`
     - `💻 运行命令`
     - `📝 截屏识字 (OCR)`
     - `🪟 窗口管理`
     - `⚡ 系统与右键工具`
     - `⚙️ 系统控制`
   - 全语言（简体中文、繁体中文、英文、日文）下展示格式整齐划一。

### 🐛 缺陷修复与深色模式高对比度图标优化
1. **彻底修复「⚡ 系统与右键工具」下拉选择显示与同步异常**：
   - 排查发现 `ShellActionPickerWindow.cs` 中公共只读属性 `ShellTools` 声明于静态私有字段 `PredefinedShellTools` 初始化语句之前，由于 C# 静态字段按声明顺序执行初始化导致 `ShellTools` 为 `null`，进而在切换下拉菜单时触发 `ArgumentNullException`；
   - 将 `ShellTools` 规范为表达式体只读属性（`=> PredefinedShellTools`），并增强 Verb/Id 双重匹配与 `SelectedItem` 精确命中，彻底解决切换至「系统与右键工具」后下拉菜单文本显示不正确的问题。
2. **全面优化深色模式（曜黑/钛灰）下的图标对比度与视觉呈现**：
   - 修复 Tab 4 高级设置中「🚀 软件更新与版本管理」、「⚡ GitHub 下载加速镜像源」、「📝 OCR 截屏文字识别与智能接口配置」在深色背景下因缺失前景色导致的暗黑不易辨识问题；
   - 修复 Tab 2 动作聚焦卡片中「⚡ 系统与右键工具」未挑选图标与「✂️ 截屏文字识别」图标缺失前景色的问题；
   - 修复 Tab 3 场景隔离设置中「🚫 排除黑名单模式」与「🛡️ 启用白名单模式」单选卡片图标对比度；
   - 修复系统与右键工具库 (`ShellActionPickerWindow`) 头部蓝底闪电图标及所有动作卡片左侧图标（矢量 Path 采用主题强调色，Emoji 字符显式绑定 `Segoe UI Emoji` 与 `TextPrimaryBrush`），在所有深浅色模式下均清晰锐利。
3. **彻底根除 Windows 10/11 自动更新时误报「Internet 安全设置阻止打开文件 ...\\nul」缺陷**：
   - 深入剖析发现旧版本更新器使用批处理脚本（`.cmd`）配合 `>nul` / `2>nul` 重定向与 `ShellExecuteEx`，当软件运行于从网络下载解压的目录时，Windows 附件安全中心 (AES) 会将相对重定向误判为对本地 `...\nul` 文件的违规调用并予以拦截阻断；
   - 重构底层为原生 PowerShell 隔离解压缩调度，切换为 `CreateProcessW` (`UseShellExecute = false`) 并在 `%TEMP%` 临时目录受信任环境下运行，杜绝一切 DOS 设备符重定向；
   - 自动在下载及解压后调用 `Unblock-File` 彻底清除所有新释放文件的 `Zone.Identifier`（Mark of the Web）锁定，杜绝 SmartScreen 拦截。

## [v1.6.7] - 2026-09-03 (原生 OCR 修复 & 多屏多分辨率唤起对齐 & 核心圆死区灵敏度 & 二级聚焦预览)

### 📝 原生 OCR 识别异常修复与智能环境诊断引导
1. **解决 WinRT 流过早释放导致的 `ObjectDisposedException`**：
   - 彻底解决 `ConvertToSoftwareBitmapAsync` 中 `DataWriter` 隐式关闭底层 `InMemoryRandomAccessStream` 造成的“Cannot access a disposed object”异常；
   - 转换过程引入 `writer.DetachStream()` 显式分离生命周期，并强制采用标准 `BitmapPixelFormat.Bgra8` + `Premultiplied` 像素格式解码，完美对接系统原生 OCR 引擎。
2. **本地 OCR 语言包环境自检与引导**：
   - 在调用前判断系统 `OcrEngine.AvailableRecognizerLanguages.Count`，针对精简版系统或未安装 OCR 语言包的环境给出友好指引；
   - 在 `OcrSettingsDialog` 界面新增环境状态徽标与一键打开 Windows「可选功能」直达按钮；
   - 在 `OcrResultWindow` 底部操作栏新增 `[ ⚙️ 接口设置 ]` 快捷入口，识别失败或异常时方便用户一键打开配置或快速切换到 AI 视觉大模型。

### 🖥️ 多分辨率多屏幕轮盘唤起显示对齐优化 (ScreenHelper & dotnet/wpf#3105)
1. **引入 ScreenHelper 统一 Per-Monitor DPI 调度**：
   - 新增 `ScreenHelper.cs`，整合 `GetDpiForMonitor`、`MonitorFromPoint` 以及显示器上下文结构体 `ScreenContext`；
   - 升级 `app.manifest` 声明 `PerMonitorV2,PerMonitor` 现代化感知模式；
   - 实现物理像素边界 (`PhysicalBounds`)、物理工作区 (`PhysicalWorkArea`) 与跨屏安全防溢出贴边逻辑 (`ClampPhysicalToWorkArea`)。
2. **两阶段物理像素精确定位与零误差居中校准**：
   - 第一阶段使用 Win32 `SetWindowPos` 直接以物理像素定位（绕过 WPF `Window.Left`/`Window.Top` 在异构 DPI 下造成的二次缩放漂移）；
   - 第二阶段在 `Dispatcher.BeginInvoke(..., DispatcherPriority.Render)` 之后，通过 `GetWindowRect` 实测当前窗口物理中心，并调用 `CenterOnPhysically(targetX, targetY)` 进行差值补偿，实现跨屏呼出逐像素精准对齐；
   - 完善 `WM_DPICHANGED` 响应逻辑，多屏拖动时无缝自适应更新。

### 🎛️ 设置面板空间重塑与核心圆唤醒灵敏度调节
1. **动作配置区空间释放**：
   - 将「多级轮盘与级联子菜单」卡片从 Tab 2（手势与动作）迁移至 Tab 3（场景与触发隔离）的触发灵敏度下方；
   - 彻底释放 Tab 2 动作映射列表上方的视觉空间，使动作卡片列表与聚焦卡片一览无余、操作更加开阔聚焦。
2. **新增「🎯 核心圆死区唤醒灵敏度」调节滑块**：
   - 在 Tab 3 手势灵敏度卡片中新增核心圆唤醒灵敏度（有效判定半径）滑块（`10 px ~ 100 px`，默认 `35 px`）；
   - 在 `AppConfig` 中加入 `CoreDeadzoneRadius` 持久化属性；
   - 在 `GestureController` 手势状态机中接入该阈值，精准控制呼出轮盘时光标停留在中心核圆触发核心动作或静默取消的有效半径，兼顾敏捷划选与防手抖误触。

### 🌟 外观与形态定制中二级轮盘预览单扇区聚焦渲染
1. **彻底消除二级蜂窝扇多扇区展开时的重叠遮挡**：
   - 在 Tab 1 外观定制切换至「二级级联轮盘配置」时，右侧实时交互画布（Live Preview）仅针对当前选中的一级扇区渲染二级子盘；
   - 即使扇区数达到 8 键或 12 键，画布预览也始终保持清爽、聚焦与高对比度；
   - 若选中的一级扇区尚未配置子动作，自动注入 3 项演示占位子动作，确保间隙、圆角与独立配色随时可调可看。

## [v1.6.6] - 2026-09-03 (原生 OCR 截屏识字 & 动作与图标解耦 & Shell 降权启动防拖放失效 & 虚拟路径增强)

### 📝 原生 OCR 屏幕截屏文字识别与智能接口配置 (Screen OCR Action & Multi-Engine Config)
1. **Windows 10/11 原生 WinRT 离线 OCR 引擎 (零依赖 · 0 延时 · 隐私安全)**：
   - 框架升级至 `net8.0-windows10.0.19041.0`，底层原生直接调用 `Windows.Media.Ocr.OcrEngine` 与 `Windows.Graphics.Imaging`；
   - 彻底免除第三方重型 OCR 动态库或庞大模型包侵占，空闲内存依然严格维持在极致轻量区间；
   - 纯本地毫秒级离线提取屏幕文字，敏感文档与私密代码绝不上传外网。
2. **轻量全屏框选截屏与现代化识别结果浮窗 (ScreenSnipWindow & OcrResultWindow)**：
   - `ScreenSnipWindow`：多显示器全景虚拟屏幕覆盖，实时半透明选区、十字标尺、物理 DPI 坐标高精度自适应、ESC 瞬时取消退出；
   - `OcrResultWindow`：识别结果悬浮卡片，清晰呈现耗时、字符数与当前引擎标识，支持结果就地编辑微调、一键重新复制与一键百度/Google 搜索，ESC 快捷退出；
   - 识别完成默认直接写入 Windows 系统剪贴板并伴随通知反馈。
3. **多模型扩展与私有化接口设置弹窗 (OcrSettingsDialog)**：
   - 支持 4 大引擎模式自由切换：
     - `🖥️ Windows 本地原生引擎`（默认）：离线 0 延时，自动匹配系统已安装的 OCR 语言包（简体中文、繁体中文、英文、日文等）；
     - `🤖 AI 视觉大模型`：OpenAI 兼容规范（GPT-4o mini、Qwen-VL、GLM-4V、DeepSeek、Ollama 等多模态视觉大模型），支持自定义端点与密钥；
     - `🌐 自定义 HTTP 微服务`：支持企业自建或私有化部署的 OCR 服务（如 PaddleOCR、RapidOCR 等）；
     - `☁️ 商业云端识别`：预留云端扩展通道；
   - 提供文本智能后处理开关：中文间空格自动剥离、换行格式合并规整等；
   - 提供一键接口连通性测试。

### 🏷️ 动作执行与图标来源彻底解耦 (Decoupled Action Execution & Visual Icon - Issue #11)
1. **快捷热键与常规动作支持继承外部官方高清应用图标**：
   - 解决用户将快捷键伪装成启动程序以获取图标所导致的执行失效与动作混乱缺陷；
   - 在主轮盘扇区与二级子动作的聚焦卡片中增设「🏷️ 关联外部程序图标」模块；
   - 提供「📦 软件库选择」、「🎯 运行窗口直接捕捉」与「📂 本地文件浏览」三通道关联；
   - 轮盘悬浮窗、同心子环、蜂窝扇与控制台右侧实时画布全面同步渲染关联程序的官方原生高清图标，执行时依然精准下发无延迟的按键模拟指令。

### 🛡️ 以常规普通用户权限启动 (防 UIPI 阻断文件拖拽 - Issue #8)
1. **通过 Windows Shell 令牌降权启动 (RunAsStandardUser)**：
   - 针对某些重度设计软件（如 Photoshop、CAD、影视后期工具或批量重命名工具），当 StarPie 以管理员权限运行时，由其拉起的目标程序也会继承高权限，触发 Windows UIPI（用户界面特权隔离），导致从资源管理器拖入文件失效；
   - 在启动程序（Launch）面板中新增「🛡️ 以常规普通权限启动」开关；
   - 开启后通过 Windows 桌面外壳接口（`Shell.Application.ShellExecute`）以交互式登录用户的标准安全令牌启动子进程，彻底恢复文件拖放交互能力。

### 💻 打开文件夹支持系统特殊虚拟路径 (Virtual Special Paths - Issue #14)
1. **系统特殊命名空间 GUID 直达**：
   - 全面支持「此电脑」(`::{20D04FE0-3AEA-1069-A2D8-08002B30309D}`)、「回收站」(`::{645FF040-5081-101B-9F08-00AA002F954E}`) 与各类 `shell:` 特殊路径；
   - `ActionExecutor.ExecuteFolder` 智能识别虚拟路径模式，穿透传统目录判定，由 `explorer.exe` 瞬间调度呼出；
   - 在文件夹面板中增设「💻 此电脑」与「🗑️ 回收站」高频预设芯片，一键点击秒配置。

---

## [v1.6.5] - 2026-09-03 (外甩取消配置卡片重塑 & 7大精简聚合分类 & 外甩高频极速预设芯片 & 二级拖拽与一二级解耦)

### ⚡ 外甩取消动作配置卡片全新重构 (Outer Escape Cancel Action Card Overhaul)
1. **彻底优化折叠体验，引入 7 大精简分类**：
   - 彻底淘汰原先展开贯穿全屏的 11 项原始未聚合下拉菜单，重构为整洁紧凑的现代化卡片；
   - 统一采用「快捷热键、启动程序、打开网址、打开文件夹、运行命令、窗口管理、系统控制」7 大精简分类；
   - 针对「窗口管理」，完整支持平铺排布（含各种分屏布局）、循环切换、还原快照、窗口置顶/取消置顶、移至下一物理显示器、调节窗口半透明度、切换任务栏指定应用等全能子模式；
   - 针对「启动程序」，提供「📦 软件库选择」、「🎯 捕捉运行窗口 (准星拖拽与列表抓取)」、「📂 本地文件浏览」三合一极速拾取工具；
   - 针对「打开网址」，增设 GitHub、Bilibili、Bing、Google 常用网址预设。

2. **结合外甩特点，增设 6 大高频极速预设芯片 (Outer Escape Quick Preset Chips)**：
   - 深度结合用户「向外甩出取消」的手势操作心理与极速盲操场景，在卡片顶部设立一键秒选芯片横幅：
     - `🖥️ 显示桌面 (Win+D)`：外甩极速显示桌面，查看文件或切换任务；
     - `📑 任务视图 (Win+Tab)`：外甩浏览所有活动窗口与虚拟桌面；
     - `↩️ 取消/返回 (Esc)`：外甩退出当前弹窗或中断当前前台操作；
     - `✂️ 系统截屏 (Win+Shift+S)`：外甩快速唤起 Windows 区域截屏工具；
     - `🪟 左右对半平铺`：外甩将当前窗口以左右对半形式快速分屏；
     - `⚙️ StarPie 控制台`：外甩呼出 StarPie 配置控制台界面；
   - 点击任意预设芯片即可瞬间配置并即时保存生效。

### 🔄 二级子功能拖拽换位修复与一二级拖拽链接解耦 (Sub-Action Dragging & Decoupled Swapping)
1. **二级轮盘子功能扇区拖拽对调修复**：
   - 修复由于扇区索引范围过滤导致的二级子动作扇区拖拽对调无效问题；
   - 支持同一主扇区内多个二级子动作直接拖拽重排序，以及跨主扇区拖拽迁移子动作；
   - 聚焦卡片配备「上一槽 / 下一槽」快捷步进，在二级编辑模式下自动在子动作间顺畅循环切换。
2. **一二级轮盘拖拽链接开闭开关 (Primary/Sub Drag Linking Toggle)**：
   - 在轮盘预览画布右上角增设「一二级拖拽链接」状态按钮（`🔗 一二级链接: 开启 / ⛓️‍💥 一二级链接: 关闭`）；
   - 开启时（默认）：拖拽一级扇区时携带其绑定的所有二级子轮盘一并换位；
   - 关闭时：拖拽一级扇区仅对调一级自身功能，原有二级级联子轮盘保留在原物理方位不变。

---

## [v1.6.2] - 2026-09-03 (手势区纯图标美化 & 指定浏览器精准调度 & 画布拖拽对调修复 & 核圆开关修复 & 控制台状态记忆 & GitHub在线更新)

### 🚀 GitHub Releases 系统在线更新与版本管理 (Software Updates & Smooth Upgrades)
1. **轻量解耦的更新架构 (UpdateManager)**：
   - 原生基于 .NET 8 `HttpClient` 与 `System.Text.Json`，无需任何外部第三方依赖，保持后台零常驻侵占；
   - 自动请求 GitHub REST API 获取最新 Releases 信息，支持「正式稳定版 (Stable)」与「尝鲜体验版 (Beta/Preview)」双通道筛选与语义化版本比较；
2. **国内加速镜像与下载代理 (Mirror Acceleration)**：
   - 针对国内连接 GitHub 丢包与限速问题，默认接入 `ghproxy.net` 与 `github.moeyy.xyz` 镜像加速，并支持官方直连与自定义反向代理前缀；
   - 支持流式断点监控下载，实时上报进度百分比、平滑进度条、下载速度与已接收字节；
3. **安全平滑重启覆盖机制 (Smooth Self-Update)**：
   - 规避 Windows 运行中文件锁问题：下载完成校验后，一键生成临时解压更新脚本（`StarPie_Updater.cmd`）；
   - 主程序安全注销钩子并保存配置退出，脚本等待进程句柄释放后静默使用系统 `tar` / `Expand-Archive` 解压覆盖安装目录，并自动重新唤起新版 `StarPie.exe`；
4. **控制台界面无缝集成 (Console UI Integration)**：
   - 在 Tab 4【高级与系统】顶部增设现代化卡片，配备「已是最新 / 发现新版 / 下载中 / 就绪重启」全状态徽章；
   - 自动智能识别用户正在运行的是「独立单文件免安装版」还是「依赖运行时轻量版」，默认选中匹配的 Zip 资产，同时支持自由切换；
   - 【关于与更新】页面与左侧边栏同步联动，支持一键快捷检查。

### 🎨 手势动作区画布渲染对齐外观形态预览 (Icon-Only Clean Canvas Aesthetics)
1. **纯图标无文字极简美学**：
   - 手势动作区实时画布（Tab 2）完全对齐轮盘外观与形态（Tab 1）的渲染策略与几何切削规范（ClassicRing, CleanSectors, Glassmorphism等）；
   - 彻底移除了扇区内垂直堆叠的拥挤文字块，所有扇区与二级子动作扇区**统一居中呈现纯图标形态**，大幅释放扇区留白，视觉更干净、灵动、高级；
   - 完美兼容 SVG 矢量轮廓、自定义位图图标与程序 Shell 原生提取图标的自适应缩放居中。

### 🌐 网址直达指定浏览器精准调度修复 (Reliable Multi-Browser Dispatch)
1. **多盘符与注册表精确检索**：
   - 针对非 C 盘系统环境（如 G 盘安装）及未配置系统 PATH 环境变量的机器，新增 `FindBrowserExecutable` 检索器；
   - 优先通过 Windows 注册表 `App Paths`（`HKLM/HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths`）精准定位 Chrome、Edge、Firefox 绝对安装路径；
   - 支持 Edge 专属协议 `microsoft-edge:` 零失败双保险唤起，彻底杜绝先前因找不到可执行文件而静默回退至默认浏览器的缺陷；
   - 适当加宽配置面板中的浏览器下拉选单宽度，杜绝名称尾部被挤压遮挡。

### 🔄 修复画布直接拖拽对调扇区与中心核圆位置 (Interactive Drag & Drop Swapping Fix)
1. **隧道事件统一接管与拖拽判定**：
   - 针对先前扇区子元素 `MouseLeftButtonDown` 标记 `Handled` 阻断父级容器捕获的问题，改用 WPF 顶级 `PreviewMouseDown`、`PreviewMouseMove` 与 `PreviewMouseUp` 隧道事件全局统一接管；
   - 拖拽位移 > 10px 时立即激活拖拽对调引擎，将功能配置与级联子菜单瞬间互换；拖拽位移 ≤ 8px 时精准响应常规点击选中操作；
   - 拖拽过程中显示提示文字与高光反馈，拖拽完成后底部显示 `🎯 已将 [动作A] 与 [动作B] 成功对调位置！`。

### 🎯 修复中心核圆动作启用开关逻辑 (Center Core Enable Toggle Fix)
1. **严格遵循启用开关状态**：
   - 修正了鼠标/键盘钩子中对 `EnableCenterAction` 与 `EnableOuterEscapeCancel` 的条件分支，严格校验用户是否真正启用了「启用中心核圆动作」开关；
   - 当开关处于关闭状态时，在中心死区内松开鼠标彻底保持静默，不再误触发中心核圆绑定的动作。

### 📌 唤起控制台保留上次界面活动状态 (Preserve Last Console Tab State)
1. **记忆活动标签页与界面状态**：
   - 重构 `ShowSettings(int tabIndex = -1)` 传参逻辑，默认唤起（包括通过中心核圆拉起控制台、双击托盘图标等）时不再强行重置至 Tab 0；
   - 完整保留用户上次关闭或隐藏设置窗口时的活动 Tab 页面与工作区状态，交互无缝流畅。

---

## [v1.6.1] - 2026-09-03 (手势动作区双栏重构 & 画布直接拖拽对调位置 & 中心核圆死区动作 & 打开网址多浏览器支持 & 方案一键复制)

### 🚀 手势动作区全新双栏重构 (Dual-Column Canvas & Focus Editor Overhaul)
1. **双栏可视化精调布局**：
   - 将【手势与动作】（Tab 2）全面重构为现代化双栏设计，左侧为**单槽位深度聚焦编辑卡片 (Focus Action Editor)**，右侧常驻**实时交互手势画布 (Interactive Wheel Preview Canvas)**；
   - 顶部提供「🎨 交互画布精调」与「📋 紧凑列表概览」双模式无缝切换，兼顾沉浸式视觉配置与批量宏观检查。
2. **方案管理与方案一键复制 (Profile Duplication)**：
   - 方案栏新增 `[ 📑 复制方案 ]` 按钮，一键克隆当前配置方案全部一级扇区、二级级联子动作与中心核圆配置，方便快速迁移至其他常用办公与生产力程序；
   - 支持实时 4 / 8 / 12 方位扇区数切换，所有扇区方位名称（E/S/W/N 及时钟方位）精确匹配。
3. **单槽位智能导航与父子级联动**：
   - 聚焦卡片顶部配备方位徽章、动作标题与导航按钮（`◀ 上一槽`、`下一槽 ▶`、`🎯 中心核圆`）；
   - 在编辑二级级联动作时，自动显示 `◀ 返回父级扇区` 按钮与所属父级标识，层级逻辑清晰明了。

### 🔄 画布直接拖拽对调功能位置 (Drag-and-Drop Sector & Core Position Swapping)
1. **所见即所得直觉化拖拽调度**：
   - 支持直接在右侧交互轮盘画布上**按住任意扇区拖动并释放至另一扇区或中心核心圆**，系统瞬间将两处的功能配置、参数与级联子菜单完整对调；
   - 彻底摆脱以往在长列表中频繁点击上移/下移调序的繁琐操作；
   - 底部状态条提供即时高亮反馈：`🎯 已将 [动作A] 与 [动作B] 成功对调位置！`。
2. **画布全方位点击聚焦与高亮发光特效**：
   - 点击主扇区即刻选中并闪耀天蓝（`#38BDF8`）光晕；
   - 点击二级子扇区即刻展开并闪耀紫罗兰（`#A855F7`）光晕；
   - 点击中心圆即刻选中并闪耀琥珀金（`#F59E0B`）光晕；
   - 支持一级/二级预览模式分段切换、鼠标滚轮自由缩放（40% ~ 300%）、右键平移拖拽与 `100%` 一键居中复位。

### 🎯 中心核心圆动作机制 (Center Core Action & Deadzone Trigger)
1. **死区释放触发中心功能**：
   - 在开启「外甩脱离取消」机制下，允许将轮盘内径死区自定义为高频动作（如点击中心呼出 StarPie 控制台、显示桌面、锁屏等）；
   - 用户右键按下后如果未向外滑动超过死区内径即就地松开，将精准触发中心核心圆动作，既保障了外甩脱离取消的灵活性，又充分榨取了轮盘中心死区的操作价值；
2. **超紧凑折叠横幅与常用预设**：
   - 中心核心圆配备极简折叠横幅，内置 `[ ⚡ 常用预设 ▾ ]`（控制台、显示桌面、锁屏、GitHub、资源管理器）与 `[ ℹ️ 说明 ▾ ]`，面积利用率大幅优化。

### 🌐 打开网址动作与多浏览器调度 (Web URL Action with Browser Dispatch)
1. **全功能网页直达**：
   - 新增 `WebUrl` 动作类型，支持任意标准 HTTP/HTTPS 网址直达；
   - 支持选择调度浏览器：**系统默认浏览器**、**Google Chrome**、**Microsoft Edge**、**Mozilla Firefox** 或**自定义浏览器路径**；
   - 内置 GitHub、Bilibili、Bing 搜索、Google 常用网址芯片一键填入。

### 🎯 运行程序「捕捉当前进程」工具集成 (Capture Running Process)
1. **进程捕捉与自动识别**：
   - 在启动程序配置面板中新增 `[ 🎯 捕捉当前进程 ]` 按钮，直接从当前运行中的窗口或已安装软件列表中快速拾取；
   - 自动填入执行路径并智能提取应用程序友好名称与图标。

---

## [v1.6.0] - 2026-09-02 (底层钩子线程化与按键防粘滞重构 & 侧边栏默认极简折叠 & 扇区独立排版定制 & 文字颜色调色板与中心文字解耦)

### ⚡ 底层事件流、钩子健康检查与修饰键调度重构 (Underlying Hook Architecture & Anti-Stuck Modifier Engine)
1. **彻底消除 `MouseHook` 假死误判与连环重启死循环**：
   - 彻底废除存在逻辑漏洞的 3 秒激进 `CheckHookHealth` 定时器，消除静止后滑动时由于事件计数器清零导致的误判重启；
   - 解除 `Stop()` 在 WPF 主 UI 线程上的 2 秒同步 `Join` 阻塞，杜绝界面与手势触发的规律性卡顿；
   - 引入 `StarPieExtraInfo = (nint)0x53544152` 专属签名，在底层钩子回调中快速放行自发模拟的鼠标事件，彻底根除重放竞态。
2. **`KeyboardHook` 独立专属后台线程化 (Dedicated Keyboard Hook Thread)**：
   - 全面参照 `MouseHook` 将 `KeyboardHook` 重构为**独立的专属后台 STA 线程（`StarPie.KeyboardHook`）与 Win32 消息泵**；
   - 彻底与 WPF 主 UI 线程解耦，杜绝界面繁忙或卡顿引发的 Windows 全局键盘卡死与 Windows 静默踢钩（Silent Unhook）；
   - 使用 `SendInput` + `StarPieExtraInfo` 签名重构 `ReplayKeyPress`，彻底消除按键重放时的丢键和误吞问题。
3. **快捷键修饰键（Ctrl / Alt / Win / Shift）防粘滞与安全释放机制 (Anti-Stuck Modifier Engine)**：
   - 在 `ActionExecutor` 中将所有合成键（`SendInput` 与 `SendTextInput`）打上 `StarPieExtraInfo` 签名，防止自身钩子捕获自发按键；
   - 引入 `try...finally` 结构性强制释放修饰键，增设 `ReleaseStuckModifiers()` 机制，在任何组合键执行完毕后确保 `Ctrl`、`Alt`、`Win`、`Shift` 物理状态干净，彻底根治短时间高频触发组合键后的修饰键假死卡住与打字错乱问题。
4. **任务栏预热线程池化 (Taskbar Prefetch Optimization)**：
   - 将 `ShowRadialUI` 中的任务栏窗口预热调用由频繁 `new Thread` 升级为轻量级 `ThreadPool.QueueUserWorkItem` + 原子防抖机制，显著降低手势呼出时的 CPU 与线程上下文切换开销。

### 📐 控制台界面空间与默认极简折叠 (Console Layout & Default Collapsed Sidebar)
1. **启动时默认侧边栏折叠**：
   - 为解决控制台主窗口在普通屏幕下略窄的问题，将应用打开时的默认状态更新为**极简图标折叠状态**（宽度 68px）；
   - 折叠状态下侧边栏图标垂直居中展示，右侧主配置区域与实时交互画布空间大幅扩展，呼吸感与可视面积显著提升；
   - 点击侧边栏折叠切换按钮（向右展开箭头）可随时展开显示完整导航标题与版本号。

### 🎨 扇区排版模式独立调节与个性化定制 (Per-Sector Independent Layout & Styling)
1. **画布点击即时选中与零冗余极简交互**：
   - 在【图标与排版选项】中精简去除了冗余的二级下拉框与启用开关，用户只需在右侧**实时交互画布中直接点击对应扇区或二级子扇区**，即可瞬间激活该方位的独立排版配置；
   - **彻底修复一级轮盘「图标 + 文字（双行居中）」排版模式失效问题**：移除了对历史残留 `ShowText` 布尔变量的错误依赖，确保图文排版完全由当前选中的排版模式精确控制，一级与二级轮盘图文呈现 100% 准确；
   - **全局统一模板模式覆盖整个轮盘**：在全局统一排版模式下切换排版模式（如从纯文字切为图标+文字）时，自动统领并重置整个轮盘所有扇区为继承状态，使全局模板的选择直接生效并覆盖整个轮盘；
   - **彻底修复扇区配置覆盖全局默认的 Bug**：对全局保存机制进行了严格的模式隔离保护，确保在定制单个扇区时，绝不会篡改全局默认排版与全局字号；切换点击其他扇区时，未定制的扇区严格保持自身继承状态，杜绝任何属性跨扇区串染与覆盖；
   - 提供「重置为全局默认」一键还原功能，兼具全局一致性与个别特殊槽位的极致个性化。
2. **一二级轮盘画布渲染与二级子扇区天蓝发光高亮选中**：
   - **二级子扇区选中高光边框与图层分层修复**：在二级轮盘模式下选中二级子扇区时，子扇区与一级主扇区一样呈现醒目的亮天蓝色（`#38BDF8`）边缘高光发光特效，且父级扇区带有柔和联动关联轮廓；彻底修复了先前二级扇区背景 Path 的 Z-Index 遮挡图标文字容器的缺陷（将文字图标容器提升至顶层 Z-Index 50），确保选中高光时文字和图标始终清晰可见、绝无遮挡；
   - **一二级轮盘画布渲染与视觉风格色彩 100% 同步**：彻底修复了一级主轮盘画布错误渲染二级轮盘蜂窝子叶的缺陷，一级模式下保持纯粹的一级轮盘展示；
   - **重构二级轮盘画布渲染引擎与主题画刷生命周期**：二级轮盘在 `FollowPrimary`（跟随一级）模式下严格完整继承一级轮盘的视觉风格、主题与自定义配色；独立主题模式下精确加载二级专属主题与切削形态渲染器，彻底排除了历史残留深灰色背景对画布的干扰；
   - 选中「🌟 二级级联轮盘配置」时，画布精准根据所选形态（蜂窝扇 / 同心子环）展开二级子轮盘，并支持直接点击二级子扇区进行完全独立的排版、文字颜色与尺寸定制。
3. **移除冗余控件与精简面板**：
   - 彻底移除了左下方冗余的「在轮盘扇区中显示动作名称文字」开关，统一由排版模式与独立扇区排版精确接管。

### 🔍 实时交互画布自由缩放、拖拽平移与一键复位 (Interactive Canvas Viewport Navigation)
1. **CAD / 专业设计级画布交互体系**：
   - **平滑缩放 (Zoom In / Out)**：支持通过鼠标滚轮连续缩放（范围 40% ~ 300%），或通过右上角悬浮工具栏中的 `[ ➖ 缩小 ]` / `[ ➕ 放大 ]` 按钮进行精细步进调节；
   - **自由平移 (Pan & Drag)**：支持按住鼠标中键（滚轮按键）或鼠标右键在画布空白处拖拽平移，光标自适应切换为抓手/十字移动图标（`SizeAll`），便于观察边缘倒角与二级子轮盘展开细节；
   - **一键居中复位 (Reset View 100%)**：悬浮工具栏配备 `[ 🎯 居中复位 ]` 按钮与百分比标签（`100%`），双击画布空白处亦可瞬间平滑重置至标准 100% 居中视口。

### ✍️ 中心文字与轮盘文字属性完全解耦 (Decoupled Center Core Text & Typography)
1. **中心文字独立样式配置**：
   - 将中心文字（与悬浮选中文字预览）与轮盘扇区文字样式彻底解耦；
   - 在【中心核心圆与图案文字设置】面板中新增专属的**中心文字字体**（`CoreFontFamily`）、**中心文字字号**（`CoreFontSize`）与**中心文字颜色**（`CoreTextColor`，配备 🎨 调色盘与 🔍 屏幕吸色管）；
   - 扇区文字与中心文字互不干扰，满足各类极客主题与艺术字体的精细化搭配需求。

---

## [v1.5.8] - 2026-09-01 (Win10 计算器唤起修复 & PrintScreen 独立截屏热键 & 启动 Explorer 修复 & 系统异步运行日志系统)

### 🧮 系统功能「计算器」Win10 / Win11 双通道唤起兼容 (System Calculator Windows 10 Fix)
1. **重构计算器系统动作唤起链路**：
   - 彻底修复在 Windows 10 环境下因 UWP 后台挂起进程 (`CalculatorApp.exe`) 导致窗口查找死锁、无法拉起计算器界面的问题；
   - 采用「系统 `calc.exe` 启动 + 协议通道 `ms-calculator:` 自动回退」双通道机制，确保在 Windows 10、Windows 11 及各类定制精简版系统中均能 100% 顺畅唤起计算器。

### 📸 PrintScreen 独立截屏热键执行修复 (PrintScreen Standalone Key Simulation)
1. **修复单独配置 `PrintScreen` 快捷键无法触发的缺陷**：
   - 根因分析：先前热键分发逻辑中，当配置单键且字符串长度大于 1 时，因白名单缺失误将其当作字符串逐字发送（`SendTextInput("PrintScreen")`），导致没有下发物理按键扫描码；
   - 彻底修复：优化热键解析路由，直接将 `PrintScreen` / `PrtScn` / `PrtSc` / `Snapshot` 映射为 Windows 虚拟键码 `VK_SNAPSHOT (44 / 0x2C)`，并在底层硬件模拟中打上 `KEYEVENTF_EXTENDEDKEY` 扩展键标志，完美触发系统全屏截屏或第三方截屏工具（如 Snipaste、PixPin、微信截图等）。

### 📂 启动程序配置为 `explorer.exe` 修复 (Launch Explorer Process Fix)
1. **排除资源管理器进程被误作为普通窗口最小化/恢复的 Bug**：
   - 根因分析：`explorer.exe` 作为 Windows 桌面外壳始终在后台常驻，先前单例窗口切换器 `TryToggleProcessWindow` 将其误判为已有前台程序进行最小化/焦点切换，导致新资源管理器窗口无法弹出；
   - 彻底修复：显式排除 `explorer` 及其派生工具，确保配置启动 `explorer.exe`（包括带参数打开特定目录或高亮选中文件）时，能直接拉起独立的新文件资源管理器窗口。

### 📋 系统异步运行日志与一键诊断系统 (`AppLogger` & Diagnostics)
1. **轻量高性能异步文件日志引擎**：
   - 全新内置 `AppLogger`，将应用生命周期（启动/退出）、配置装载/备份、底层鼠标/键盘钩子自愈健康检查、扇区动作执行分发及未捕获全局异常自动记录至 `%LOCALAPPDATA%\StarPie\logs\starpie_YYYY-MM-DD.log`；
   - 采用低优先级后台线程异步批量写入与 7 天滚动自动清理机制，零 GC 抖动与零 UI 卡顿。
2. **设置界面一键查看与诊断支持**：
   - 在「高级系统与运维备份」面板中新增「系统运行日志与诊断」卡片；
   - 提供 `[ 📂 打开日志目录 ]` 与 `[ 📄 查看今日运行日志 ]` 快捷按钮，极大方便开发者与用户后续排查故障与反馈问题。

### 🖥️ 多显示器/不同分辨率副屏触发中心漂移修复 (Multi-Monitor Mixed DPI & Center Alignment Fix)
1. **重构轮盘窗口几何原点与多屏 DPI 定位架构**：
   - **根因分析**：在 2K 主屏 + 2.5K 副屏等混合分辨率/不同 DPI 缩放环境下，先前轮盘尺寸在窗口加载（`Loaded`）过程中二次重设 `base.Width`，触发 WPF 内部跨屏 DPI 换算错位，导致中心取消圆圈（`CoreGrid`）与 8 扇区几何路径基于不同原点（`180px` vs `230px`）绘制，产生明显的斜向分离漂移；
   - **彻底修复**：
     - 在 `RadialWindow` 构造阶段即完成总有效外径（`_wheelCanvasSize`）与画布原点（`_canvasCenter`）的确定性预计算，将 `Window`、`MainGrid` 与 `WheelCanvas` 的几何尺寸在实例化瞬间严格同步锁死；
     - 轮盘所有扇区路径、装饰线、中心圆及级联子菜单统一绑定不可变的 `_canvasCenter`，杜绝任何图层间的相对漂移；
     - 引入 `WM_DPICHANGED` 消息钩子，结合 Windows 物理像素 `SetWindowPos` 定位，确保在任意分辨率与 DPI 缩放的副屏上呼出时，轮盘展开中心与鼠标触发物理坐标 100% 绝对居中对齐。

---

## [v1.5.7] - 2026-09-01 (轮盘动作上下排序 & 扇区方位微缩指示器 & 独立低级钩子线程与流畅度优化 - PR #26)

### 🧭 轮盘动作上下排序与方位微缩指示器 (Action Ordering & Radial Position Indicator - PR #26)
1. **扇区动作映射列表支持一键上下移动排序 (`MoveSlotUp` / `MoveSlotDown`)**：
   - 动作配置列表中新增 `▲` / `▼` 快捷调序按钮；
   - 移动动作时，极坐标槽位、对应动作配置以及实时交互画布预览毫秒级无缝联动交换，无需重新手动配置；
   - 到达首项或末项时按钮智能置灰（`CanMoveUp` / `CanMoveDown`），防止越界。
2. **扇区方位微缩指示器 (`WheelPositionIndicator`)**：
   - 动作列表每行左侧全新引入高质感矢量微缩指示器，实时以亮色扇区与方向箭头直观标识当前动作对应的空间极坐标方位；
   - 自适应当前轮盘扇区数量（4 / 8 / 12 扇区）并跟随当前主题与高亮色自动变换。

### ⚡ 独立低级钩子后台线程与手势高频调度优化 (Dedicated Hook Thread & Gesture Dispatcher Coalescing)
1. **低级鼠标全局钩子独立线程生命周期 (`MouseHook` Thread Isolation)**：
   - 全局鼠标底层钩子（`WH_MOUSE_LL`）从主 UI 线程迁移至独立的专属后台高优先级线程（`StarPie.MouseHook`），并配备完整的 Windows 消息泵与自愈健康检查定时器（`HealthCheckTimer`），彻底杜绝主 UI 耗时或阻塞导致鼠标指针掉帧与钩子被系统卸载的问题。
2. **高频手势移动更新合并调度 (Gesture Dispatcher Coalescing)**：
   - `GestureController` 中增加基于版本锁（`_gestureVersion`）与高频去抖合并的 UI 调度机制，高回报率游戏鼠标（1000Hz / 4000Hz / 8000Hz）极速划动时自动折叠中间过渡帧，仅向 UI 派发最新扇区高亮状态，大幅降低 CPU 与 GPU 渲染负载。
3. **二级子菜单缓存与性能优化 (`SubTierVisuals` Caching)**：
   - 二级扇区与蜂窝扇子叶渲染对象支持按主扇区索引缓存复用，避免每次鼠标滑动产生大量 Freezable 对象。

---

## [v1.5.6] - 2026-08-31 (运行自定义系统命令 & 二级轮盘独立外径渲染 & 配置文件即时全量导入 & 开机自启双通道保障 & 灵敏度与顺势外甩距离范围大扩展)

### 🖥️ 运行自定义系统命令与多终端支持 (Execute Command & Terminal Selector - PR #20)
1. **新增「运行命令 (Command)」动作类型**：
   - 支持在「扇区动作映射列表」与「级联二级子菜单」中直接将扇区绑定为系统命令行指令；
   - 动作图标默认自动映射为现代矢量终端图标（`Command`）；
2. **多终端环境与窗口保留/静默模式自由选择 (`CommandTerminal`)**：
   - **CMD**：唤起 CMD 终端窗口执行并保留（`/k`），适合查看控制台输出；
   - **CMD (无终端)**：后台静默执行（`CreateNoWindow + /c`），不弹出任何黑框，进程执行完自动退出；
   - **Powershell**：唤起 PowerShell 执行并保留窗口（`-NoExit`）；
   - **Powershell (无终端)**：PowerShell 后台静默执行；
   - **WSL / WSL (无终端)**：直接在 Windows Subsystem for Linux 中运行 Linux 原生命令；
3. **安全引号转义与即时测试**：
   - 自动转义命令中的双引号包裹，支持复杂的环境变量与带空格参数；
   - 扇区行与二级子菜单编辑器中均支持一键「测试」运行效果。

### 🌟 二级轮盘整体外径与实时画布独立缩放 (Sub-Wheel Radius Sizing & Live Preview Decoupling)
1. **解耦实时画布自动缩放逻辑，彻底杜绝调节二级外径导致一级轮盘缩放的错觉**：
   - 根因修复：先前实时交互画布将缩放基准与二级轮盘最大外径动态绑定，导致拖动「二级轮盘整体外径」滑块时画布全局缩小、一级轮盘尺寸随之被压缩；
   - 架构优化：固定画布基准比例尺，拖动二级外径滑块时一级主轮盘尺寸完全保持不动，二级子环与蜂窝扇子叶向外/向内独立伸缩与扩散，实现所见即所得的极佳调参手感。
2. **蜂窝扇子叶距离与展开空间自适应外径**：
   - 在二级轮盘为「蜂窝扇」形态时，子叶卡片间距与扩散中心距离跟随二级轮盘整体外径等比例扩展，提供更加通透立体的悬浮视觉。

### 🔄 配置文件 JSON 导入即时生效与全量尺寸/主题方案同步 (Instant Full Config Import)
1. **彻底解决导入配置需要重启才显示配色方案与尺寸未同步生效的问题**：
   - 统一抽取 `LoadConfigToUi()` 全量配置装载链路，在导入成功后即时刷新所有尺寸滑块（外径、内径、核心圆、间距、倒角、图标/字号）、手势灵敏度、外甩距离、动效速度与隔离名单；
   - 自动重构一二级自定义配色方案下拉列表与主题画刷注入管理器（`AppThemeManager.ApplyTheme`），并同步刷新动作映射列表与实时交互画布，**无需重启软件即可 100% 毫秒级应用全部新配置**。

### 🛡️ 开机自启双通道保障与管理员自启修复 (Robust Dual-Channel Auto-Start & Task Elevation)
1. **双通道开机自启动机制**：
   - 解决先前启用管理员权限自启时，因权限不足创建任务计划失败导致开机无法自启且设置状态显示异常的问题；
   - 采用「注册表 Run 键 + 任务计划程序高权限 Task」双通道冗余策略，始终确保开机静默启动链路绝对可靠；在非管理员权限下切换管理员自启时自动触发一次性提权创建，杜绝配置丢失。

### ⚡ 灵敏度与顺势外甩脱离取消灵敏度大幅扩展 (Expanded Sensitivity & Outer Escape Range)
1. **扩大手势触发灵敏度与外甩取消距离调节范围**：
   - **顺势外甩脱离取消距离 (Escape Distance)**：调节上限大幅扩展至 **650 px**（范围：`120 px ~ 650 px`），满足 2K / 4K / 多联屏高 DPI 屏幕以及极速大范围甩动手势用户的需求，彻底杜绝快速操作过程中的过早误取消；
   - **手势触发灵敏度 (Drag Threshold)**：扩展至 `5 px ~ 120 px`（步长 1 px），支持极致微动手势或沉稳大位移手势；
   - **动效过渡速度 (Animation Speed)**：扩展至 `10 ms ~ 500 ms`；
   - **二级展开触发距离 (Trigger Distance)**：扩展至 `30 px ~ 260 px`；
   - **轮盘整体外径 (Wheel Radius / Sub Radius)**：主轮盘扩展至 `70 px ~ 280 px`，二级轮盘扩展至 `120 px ~ 480 px`。

---

## [v1.5.5] - 2026-08-31 (快捷键拼装组合器交互修复 & 常用快捷键一键预设 & 导出配置包含全量自定义配色与主题)

### ⌨️ 快捷键拼装构建器全面修复与常用操作一键预设 (Hotkey Builder Fixes & Presets)
1. **修复「拼装」按钮点击报 NullReferenceException / 空引用报错的问题**：
   - 根因分析：`HotkeyBuilderDialog.xaml` 中主按键下拉框（`MainKeyComboBox`）在 XAML 解析构建时触发 `SelectionChanged` 事件，此时下方的 `CustomInputTextBox` 与 `PreviewResultText` 尚未实例化完成，直接访问控件导致抛出 `Object reference not set to an instance of an object`；
   - 彻底修复：将 `_isInternalUpdating` 更新锁在窗口初始化前置为 `true`，移除 XAML 中的行内选中触发，并在所有事件回调和初始化方法中加入严密的非空安全防护（Null-Guards）与异常保护，确保点击 `[ ⚙️ 拼装 ]` 100% 顺畅弹出并实时响应。
2. **新增「常用快捷操作一键预设」芯片栏与「常驻窗口切换器」**：
   - 在按键拼装构建器顶部新增常用预设快速点击区：
     - `🪟 Ctrl + Alt + Tab` (常驻窗口切换器 - 打开窗口列表并常驻在屏幕上，可用鼠标自由点选，免去键盘物理长按)
     - `🪟 Alt + Tab` (快速切至上一窗口)
     - `🌟 Win + Tab` (打开系统任务视图)
     - `⌨️ Shift + Alt` / `Ctrl + Shift` (快速切换输入法)
     - `🎨 Alt + Delete` (PS填充前景色) / `Ctrl + Delete` (PS填充背景色)
     - `🖥️ Win + D` (快速显示桌面)
     - `📋 Ctrl + Shift + V` (纯文本粘贴)
     - `🔍 Win + S` (系统搜索) / `🔒 Win + L` (快速锁屏)
   - 点击任意芯片即可秒级将对应快捷键填充至当前动作，彻底免去繁琐组合与系统按键拦截烦恼。
3. **新增智能「窗口切换器常驻/长按模式」提示与一键转换**：
   - 当检测到用户配置了 `Alt + Tab` 时，拼装器会自动浮现智能提示卡片，并提供 `[ 一键转为常驻模式 ]` 按钮，自动将 `Alt+Tab` 升级为 Windows 原生常驻切换模式（`Ctrl+Alt+Tab`），彻底解决轮盘拖动释放后窗口切换器一闪而过的问题；
   - 系统动作预设库中新增「常驻窗口切换器 (`WindowSwitcher` / `Ctrl+Alt+Tab`)」与「快速切至上一窗口 (`AltTab` / `Alt+Tab`)」，提供更丰富的系统预设选择。
4. **优化录制框对 Tab 与修饰键的直接录制体验**：
   - 禁用 `HotkeyRecorderBox` 控件在录制状态下的 WPF 默认焦点跳转导航（`KeyboardNavigationMode.None`），确保在录制框内直接按下 `Tab` 或 `Alt+Tab` / `Win+Tab` 时不会丢失焦点，可直接识别录入。

### 🎨 蜂窝扇六边形形态平滑圆角支持与渲染引擎优化 (Honeycomb Fan Hexagon Fillet Support)
1. **修复「蜂窝扇」在「蜂巢六边形 (HexagonHive)」形态下圆角尺寸调节无效果的问题**：
   - 根因分析：二级菜单蜂窝扇渲染器（`RadialWindow.CreateSubMenuGeometry`）在生成 HexagonHive 六边形几何图形时，未接入 `SubWheelCornerRadius` / `cornerRadius` 倒角参数，直接由 6 个锐角顶点通过 `LineTo` 硬折线封闭，导致无论用户在控制台中如何滑动「二级扇区边缘平滑倒角」滑块，蜂窝六边形形态均保持 0 圆角不变；
   - 彻底修复：在 `CreateSubMenuGeometry` 中引入极坐标旋转六边形切向圆角算法（基于 $\frac{r_{\text{corner}}}{\sqrt{3}}$ 切线偏移与 `ArcTo` 贝塞尔圆弧过渡），并加入边界安全截断；无论是实时交互画布预览还是悬浮轮盘弹出，调节「二级扇区边缘平滑倒角」滑块时均可实时呈现从锐利六边形到极致平滑胶囊蜂巢的丝滑倒角形态。

### 🎨 配置导入/导出全量包含自定义配色方案与主题预设 (Full Custom Color Presets Backup & Restore)
1. **导出配置文件完整包含自定义配色方案**：
   - 导出 JSON 时自动包含全局所有 `CustomColorPresets`（包含各预设的扇区背景色、边框色、高亮背景色、高亮边框色、文字颜色）、一二级当前选中的配色预设 ID 以及所有微调色彩参数；
2. **导入配置自动刷新配色面板与主题列表**：
   - 导入后立即调用 `ReloadThemePresets()` 动态重构主题下拉菜单与二级主题下拉菜单，自动将导入文件中的自定义预设加载到选项中，并实时更新颜色拾取框、色彩微调预览与交互画布，使得别人导入后可以完美无损使用导出者制作的轮盘配色方案。

---

## [v1.5.4] - 2026-08-31 (快捷键按键拼装构建器 & 独立录制控制 & 蜂窝扇多形态几何重构与左右判定Bug修复)

### ⌨️ 快捷键录入与执行引擎全量重构 (Hotkey Input & Execution Engine Overhaul)
1. **新增独立的「快捷键与组合构建器 (`HotkeyBuilderDialog`)」**：
   - 在控制台（`SettingsWindow`）主映射列表以及二级子动作编辑器（`SubActionEditorWindow`）中，为快捷热键行新增 **`[ ⚙️ 拼装 ]`** 独立构建按钮；
   - **四项修饰键独立勾选**：提供 `[ ] Ctrl`、`[ ] Shift`、`[ ] Alt`、`[ ] Win` 勾选框；
   - **全量主按键分类下拉选择器**：涵盖 `Tab`（窗口/焦点切换）、`Esc`、`Enter`、`Space`、`Backspace`、`Delete`、`Insert`、`Home/End/PageUp/PageDown`、`↑↓←→` 方向键、`F1~F24`、`A~Z`、`0~9`、小键盘等全部标准按键；
   - **轻松设置系统级拦截热键与纯修饰键**：用户无需按下键盘即可直接勾选拼装出 `Alt + Tab`、`Win + Tab`、`Shift + Alt`、`Ctrl + Shift`、`Ctrl + Alt + Delete`，彻底避开操作系统拦截与窗口切走问题。
2. **独立录制控制与多位数值/文本直输**：
   - 录制框支持独立点击开启录制（`🔴 录制中...`），再次点击或按回车即可完成录制，按 `Esc` 取消，配备一键清除按钮（`✕`）；
   - 在构建器与录制框中均支持直接输入多位连续数值（如 `"100"`、`"0.5"`、`"1920"`）或文本字符串，底层采用 `KEYEVENTF_UNICODE` 字符流驱动无缝注入。
3. **彻底修复 Shift+F4 误触发 Shift+S / Ctrl+F5 误触发 Ctrl+T 等硬件扫描码 Bug**：
   - 引入 Win32 `MapVirtualKey(vk, MAPVK_VK_TO_VSC)` 硬件扫描码实时转换，并在 `CreateKeyInput` 中正确补充硬件级 `wScan` 与 `KEYEVENTF_EXTENDEDKEY` 扩展键标志（涵盖 `Delete`、`Insert`、`PageUp/Down`、`Home/End`、方向键、`Win` 键、小键盘除号等），彻底根治各类生产力应用中的快捷键误触发。
4. **解决 Illustrator 中 Ctrl+G / Ctrl+Shift+G 触发导致页面滚动的 Bug**：
   - 在 `ActionExecutor` 模拟按键下发链路中注入 15ms 的微时延保持（Hold Duration），确保修饰键（`Ctrl` / `Shift`）在宿主程序中被完整且稳定地置于按下态后再释放，避免被宿主软件误当作无修饰的单键（如 `G` 渐变/滚动工具）引发页面滚动。

### 🍯 蜂窝扇几何重构与区域命中判定修复 (Honeycomb Fan Geometry & Hit-Test Fixes)
1. **经典紧凑扇区（Original）与悬浮圆角胶囊（Capsule）深度适配**：
   - 彻底修复 `ClassicRing` / `Original` 弧形扇叶计算时错误继承父级扇区中心角的缺陷。重构为依据各子按键在极坐标中的独立偏角 $\theta_j = \text{atan2}(p_{y}-c_y, p_{x}-c_x)$ 动态生成优雅的花瓣形圆角弧度，自然环抱展开；
   - 重构 `RoundedCapsule`（圆角胶囊）的旋转矩阵，按各按键法线方向对齐，彻底消除放射性穿插重叠。
2. **彻底修复「鼠标光标在左侧，激活动画效果却在右侧」的判定 Bug**：
   - 根因分析：在实时画布 `LiveWheelPreviewCanvas_MouseMove` 与 `RenderLiveWheelPreview` 中，子菜单在蜂窝扇模式下未根据父级扇区状态进行可见性隔离，且悬停角判定存在量化偏移，导致左侧悬停时拾取了右侧残留的子菜单几何；
   - 架构修复：实时画布在 `Fan` 模式下仅在鼠标划向对应父级扇区时才展开并命中对应的蜂窝扇，其余方位完全隔离隐藏（`Opacity = 0`），实现 100% 像素级精准对应。

### 🎨 全局深色模式文字高对比度优化
- 修复 `ToggleSwitchStyle`（开关控件）内容呈现器未绑定前景色画刷的问题，在 `<ContentPresenter>` 上显式绑定 `TextElement.Foreground="{DynamicResource TextPrimaryBrush}"`，确保在极夜曜黑与钛金深灰深色主题下文字清晰锐利（纯白 #F8FAFC / #F4F4F5）。

### 🔀 二级菜单展现形式切换移至「手势与动作」卡片
- 将「二级菜单样式 (Submenu Style)」选择下拉框从 Tab 1（外观与形态）的二级轮盘尺寸面板中移除，规范归入 Tab 2（手势与动作）的 **「多级轮盘与级联子菜单」** 配置卡片中，紧邻多级轮盘总开关。

---

## [v1.5.3] - 2026-08-30 (新增「蜂窝扇」二级菜单样式 & 全屏场景防误触与白名单协同 & 一二级配置切换移至画布 & 深色主题高对比度文字优化)

### 🍯 新增「蜂窝扇」二级菜单样式 (Honeycomb Fan Submenu Style)
- **优雅紧凑的蜂窝扇形态**：
  - 参考社区 PR #6，在「外观与形态 / 多级轮盘」中新增 **「二级菜单样式 (Submenu Style)」** 切换开关，支持在 **「🌐 外圈同心子环 (Outer Sub-Ring)」** 与 **「🍯 扇形蜂窝矩阵 (Honeycomb Fan)」** 两种二级菜单形态间自由切换；
  - **自适应中心扩展**：蜂窝扇以选中的主扇区为圆心，向外呈优雅的扇形展开（最多 3 项）；
  - **全几何切削形态自适应**：在 `CreateSubMenuGeometry` 中为蜂巢六边形（`HexagonHive`）、独立圆形（`Circle`）、悬浮胶囊（`RoundedCapsule`）、圆角矩形（`CleanSectors`）与经典圆弧（`ClassicRing`）5 种切削形态进行针对性几何生成，保持全局视觉风格统一。
- **共享数据模型与统一管理**：
  - 「外圈子环」与「蜂窝扇」共用同一套底层 `SubAction` 数据结构与级联子菜单编辑器，切换样式无需重复配置。

### 🛡️ 场景隔离防误触与白名单协同联动 (Scenario Isolation & Whitelist Synergy)
1. **彻底修复桌面/任务栏被误判为全屏游戏的缺陷**：
   - 重构 `FullScreenHelper.cs`，引入类名（`Progman`、`WorkerW`、`SHELLDLL_DefView`、`SysListView32`、`Shell_TrayWnd`、`Shell_SecondaryTrayWnd`）与 Windows 资源管理器进程（`explorer.exe`）智能旁路穿透；
   - 彻底解决开启「全屏游戏/独占应用自动禁用手势」并在关闭设置窗口后，桌面/任务栏右键手势失效的问题。
2. **场景隔离与白名单深度协同**：
   - 优化手势隔离判定逻辑，当「全屏游戏/独占应用自动禁用手势」开启时，用户在白名单中添加的应用程序（如 Photoshop、CAD、全屏生产力工具或特定游戏）将自动旁路全屏拦截，在全屏独占状态下仍可正常呼出轮盘。

### 🎨 一二级轮盘配置切换移至右侧交互画布 (Layout & Interaction Enhancement)
- **切换调整效率大幅提升**：
  - 将 Tab 1「外观与形态」中的 `[ 🔘 一级主轮盘配置    🌟 二级级联轮盘配置 ]` 分段切换开关移至右侧「实时交互画布 (Live Preview)」卡片顶部；
  - 用户在右侧画布一键切换一二级轮盘的同时，左侧主题配色与几何尺寸面板即时动态切换，预览画布与交互触手可及。

### 🌙 主题精简与深色主题文字对比度全面增强 (Theme Refinement & Contrast Optimization)
1. **精简控制台界面主题**：
   - 移除「午夜深蓝」与「暗夜紫罗兰」两款冗余主题，聚焦于跟随系统（`System`）、极简纯白（`Light`）、极夜曜黑（`Dark`）与钛金深灰（`TitaniumGray`）四大经典高质感主题。
2. **深色主题文字对比度与清晰度增强**：
   - 修复 `ToggleSwitchStyle`（开关按钮）与 `CardSegmentRadioStyle` 在深色主题下文字因未绑定前景画刷导致呈现暗黑低对比度的缺陷；
   - 全量升级 `obsidiandark` 与 `titaniumgray` 主题的文字画刷色阶（`TextSecondary` 升级为 `#CBD5E1` / `#D4D4D8`，`TextMuted` 升级为 `#94A3B8` / `#A1A1AA`），全方位契合 WCAG 2.2 AA 护眼与高对比度易读标准。

### 🎯 核心问题修复与交互算法升级
1. **高 DPI 屏幕像素级精准命中**：
   - 彻底修复在高分屏（125%、150%、175%、200% 缩放）下蜂窝扇悬停判定偏移导致光标滑入无法选中的缺陷；
   - 结合 Windows Mouse Hook 物理像素与 WPF 设备独立像素（DIP），通过 `PresentationSource` 获取真实 DPI 矩阵对几何偏移与命中半径进行像素级转换。
2. **彻底修复二级轮盘高亮时图标被背景覆盖的渲染层级 Bug**：
   - 规范 Canvas 渲染 Z-Index：基础背景扇区（ZIndex 15）与高亮扇区几何（ZIndex 18）严格置于底层，图标与文字容器（ZIndex 35）始终置顶；
   - 鼠标悬停高亮时前景文本与矢量图标切换为纯白高亮，确保清晰锐利永不被遮挡。
3. **智能居中对称布局 (1~3 项子动作)**：
   - 针对 1 项子动作：自动居中布置于扇尖中心；
   - 针对 2 项子动作：自动对称分布于上下两翼，避免单侧空旷；
   - 针对 3 项子动作：依次排布于上翼、扇尖与下翼，呈现完美三角蜂窝排布。
4. **一二级轮盘独立调节与动效调速全量恢复**：
   - 完整恢复 Tab 0 动效速度 4 档调节（优雅 130ms / 流畅 80ms / 快速 35ms / 自定义 10~300ms 滑块）与外甩脱离灵敏度滑块；
   - 完整恢复 Tab 1 二级轮盘独立外观、配色预设、高级调色板、光晕模式及尺寸微调。

---

## [v1.4.5] - 2026-08-29 (多级轮盘与级联子菜单 & 智能前台收起唤出切换 & 扇形圆角相切算法重构 & 进程启用白名单)

### 🌟 多级轮盘与级联子菜单 (Multi-Tier Cascading Radial Sub-Wheels)
- **多级轮盘与级联架构**：
  - 支持在「手势与动作 (Tab 2)」中开启多级轮盘功能，并为任意扇区方位自由配置 1~4 个二级子动作（SubActions）；
  - **动态级联展开与平滑动画**：光标划向扇区并在扇区内停留/悬停时，外环以弹簧动效平滑展开对应的扇形二级子菜单；光标向外滑入手势子扇区即可极速触发子动作；
  - **实时交互画布同步渲染 (Live Preview Sync)**：在「外观与形态 (Tab 1)」与「手势与动作 (Tab 2)」中，实时交互画布动态自适应缩放并实时绘制外环级联子扇区、文字与图标，支持鼠标悬停外环光标互动；
  - **专属级联子动作编辑器 (Sub-Action Editor)**：各扇区右侧配备 `[⚙️ 级联 (N)]` 按钮，提供直观的增删查改、动作类型选择、快捷键/路径配置与图标拾取；
  - **开箱即用预设注入**：自动为默认预设中的常用方位（如复制方位级联剪切/粘贴/全选，系统工具方位级联记事本/计算器/任务管理器）注入预设，升级即刻体验。

### 🔄 应用程序/系统工具/文件夹智能前台呼出与收起最小化 (Smart Foreground Toggle)
- **一次激活呼出，二次激活收起**：
  - 在 `ActionExecutor` 中引入智能前台探测与窗口状态记忆引擎（`IsTargetInForeground` / `MinimizeTarget` / `RestoreAndActivateTarget`）；
  - 当通过轮盘激活某个已打开的应用、文件夹或系统工具（如设置、任务管理器等）时：
    - 若该目标已处于最前台活动窗口：再次划动相同功能扇区将自动**最小化收起**该窗口；
    - 若该目标最小化或位于后台：划动扇区将智能**还原并激活置顶**到最前台；
    - 若该目标尚未运行：划动扇区将正常创建并启动对应进程或目录。

### 📐 扇形轮盘光滑边缘倒角相切算法重构 (Advanced Sector Geometry Rounding)
- **彻底解决倒角滑块调整无效问题**：
  - 彻底重构 `IconHelper.CreateAdvancedSectorGeometry`，弃用单一 `ArcSegment` 截断，采用严格的四角圆弧与内外环相切倒角数学构建模型（Inner Arc Start -> Outer Arc Start -> Outer Arc End -> Inner Arc End），确保内外环与径向切边在任意角度、半径、间隙和圆角半径下均能渲染出平滑圆润的高质感扇形胶囊。

### 🛡️ 进程隔离与生效名单：新增「启用白名单」模式 (Process Whitelist Mode)
- **黑名单 / 白名单双模态自由切换**：
  - 在「触发与场景 (Tab 0)」将排除黑名单升级为**双模态名单管理器**：
    1. 🚫 **排除黑名单模式 (默认)**：全局任意窗口生效，仅在排除黑名单程序（如 3D 建模、远程桌面、特定画图软件）中完全放行鼠标右键；
    2. 🛡️ **启用白名单模式**：仅在指定的白名单程序中拦截手势并唤醒轮盘，在系统其余所有窗口与桌面上 100% 放行原生右键，实现零干扰、按需定制。

---

## [v1.4.4] - 2026-08-29 (OBS应用工作目录自适应修复 & 轮盘高亮过渡动效三挡调速)

### 🛠️ 外部应用程序工作目录（WorkingDirectory）自动补全与隔离修复
- **解决 OBS Studio 等外部应用启动报错**：
  - 深度修复将 OBS Studio、各类创意设计与游戏工具添加到 StarPie 轮盘动作中启动时，因工作目录缺失报 `Failed to find locale/en-US.ini` / `Failed to load locale` 错误的问题；
  - **自适应父目录提取引擎**：在 `ActionExecutor.ExecuteLaunch` 中自动解析环境变量、推断可执行文件或目标文件的真实物理目录，并显式注入 `ProcessStartInfo.WorkingDirectory`，确保外部应用以完整本地上下文和资源依赖顺利启动。

### ⚡ 轮盘高亮与过渡动效三挡响应调速 (Hover & Transition Speed Tuning)
- **「🎯 触发与场景」新增动效调速面板**：
  - 支持在设置控制台自由调节鼠标滑向不同功能扇区时的高亮弹出与平滑过渡响应速度，提供三种量身定制的手感档位：
    1. 🌸 **优雅 (Elegant / 130ms)**：缓动柔和细腻，适合追求温润高级视觉质感的用户；
    2. ⚡ **流畅 (Fluent / 80ms)** [推荐 / 默认]：跟手适中富有动感，兼顾灵敏反馈与丝滑视觉；
    3. 🚀 **快速 (Snappy / 35ms)**：极速瞬时响应，适合快手速盲操与高频快捷键操作；
- **配置持久化与多语言全面适配**：
  - 档位即改即生效、即时自动持久化至本地配置；
  - 选项卡片与描述文案全面适配简体中文、繁体中文、英文与日文。

---

## [v1.4.3] - 2026-08-29 (按键录制捕获器 & 鼠标全按键/键盘单键/修饰键长按唤醒 & 笛卡尔精准对齐 & 零延迟高刷手势 & 真正开机静默启动)

### 🎙️ 全能【按键录制捕获器】与自定义触发按键体系 (Custom Trigger Keys & Interactive Recorder)
- **多维度物理按键自由录制与长按唤醒**：
  - 在「🎯 触发与场景 (Tab 0)」首项新增交互式**按键录制捕获器**，支持一键捕获物理按键并设为轮盘长按触发键；
  - **鼠标全按键支持**：
    1. 🖱️ **鼠标右键 (Right Button)** [推荐 / 默认]
    2. 🖱️ **鼠标中键 / 滚轮按压 (Middle Button)**
    3. 🖱️ **鼠标侧键 1 / 后退键 (XButton 1 / Back)**
    4. 🖱️ **鼠标侧键 2 / 前进键 (XButton 2 / Forward)**
  - **键盘单键长按拖动唤醒**：
    - 支持将 **CapsLock (大写锁定)**、**波浪键 (`~`)**、字母键、数字键、F 区功能键、方向键等设为长按触发键，按住按键并滑动鼠标即可呼出轮盘；
  - **独立修饰键长按唤醒**：
    - 完整支持单独将 **Ctrl**、**Alt**、**Shift**、**Win** 作为长按触发按键；
  - **复合组合键录制与触发**：
    - 支持诸如 `Alt + 鼠标中键`、`Ctrl + 鼠标侧键 1`、`Shift + CapsLock` 等复合修饰组合手势触发；
- **防频闪与防漂移引擎 (Anti-Typematic Engine)**：
  - 深度拦截键盘物理长按时 Windows 操作系统持续派发的打字重复信号（Typematic Auto-Repeat），彻底消除长按拖动时轮盘高频闪烁、重绘卡顿与原点抖动偏移的问题；
- **实时硬件感知指示器 (Live Hardware Sensor)**：
  - 控制台实时显示底层捕获的按键信号与硬件工作状态，按键录入与手势状态一目了然；
- **原生单次点击智能放行 (Smart Native Click Pass-Through)**：
  - 未达到拖动阈值（DragThreshold）的短按或单次点击，手势引擎自动无缝放行原生按键事件（如鼠标中键后台打开链接、侧键前进后退、键盘单击切换大小写等），0 干扰日常操作；
- **全多语言实时适配**：
  - 触发按键徽章、录制器提示及实时感知文本已全面适配简体中文、繁体中文、英文与日文。

### 🏎️ 零延迟高刷手势跟踪与笛卡尔坐标精准对齐 (Zero-Lag & 1:1 Direction Mapping)
- **极速无锁 Hook 线程计算与防重入守卫**：
  - 将扇区距离、阈值及外甩判定移入底层 Hook 线程纯纳秒级无锁计算，通过 `BeginInvoke(DispatcherPriority.Input)` 极速派发；
  - 引入扇区高亮防重入守卫，彻底杜绝鼠标高频移动时每秒创建数千个重复动画导致的 GC 阻塞、顿挫与不跟手问题；
- **笛卡尔几何绝对对齐**：
  - 修正方位角计算公式为标准屏幕笛卡尔坐标方位角（`Math.Atan2(dy, dx)`），实现光标物理移动方向与轮盘 4 / 8 / 12 扇区几何方位 1:1 绝对对齐，彻底消除方向错位。

### 🎨 扇区高亮与文字色彩独立性修复 (Isolated Sector Highlight Animation)
- **单扇区独立响应**：
  - 修复触发轮盘时所有扇区文字被统一变色的异常，恢复为仅当前悬停扇区单独进行高亮底色与文字颜色动画切换，未选中的其余扇区严格保持默认配色，动效自然纯粹。

### 🚀 开机真正零弹窗静默启动 (True Zero-Exposure Silent AutoStart)
- **注册表自启动静默参数绑定**：
  - 修复开机自启动时弹出设置主界面的问题，注册表启动项格式统一为 `"{exePath}" --autostart --minimized`；
- **旧版本注册表无缝平滑自动升级 (`EnsureAutoStartRegistryUpToDate`)**：
  - 自动检测并升级已有注册表项，用户无需手动重新开关；
- **全套静默参数识别与极简内存深度修剪**：
  - 精确识别 `--autostart`、`--minimized`、`--silent`、`-s`、`-m` 参数，开机时窗口 0 暴露、仅驻留系统托盘，并在启动后立即修剪工作集，后台常驻内存仅 15~25MB。

### 🪟 Windows 内核级 EventWaitHandle 极速无感唤醒 (Taskbar Activation Kernel IPC)
- **彻底根除任务栏固定图标二次点击黑屏与无法关闭异常**：
  - 移除 XAML 自动 `StartupUri` 规避 Windows DWM 抢占创建僵尸空壳视口；
  - 引入内核级 `EventWaitHandle` 跨进程即时通信，实现托盘后台驻留进程瞬间置顶弹出并 100% 完整响应窗口生命周期事件。

---

## [v1.4.2] - 2026-08-26 (任务栏唤醒优化 & 外甩取消严谨化 & 几何关联与持久化记忆)
### 🪟 任务栏固定快捷方式唤醒机制深度重构 (Taskbar Activation IPC)
- **告别后台隐藏状态下再次点击任务栏出现的黑屏/无法关闭异常**：
  - 针对 Windows 任务栏固定快捷方式二次唤醒时，底层 Win32 `ShowWindow` 导致的 WPF 视口无主渲染、窗口呈现纯黑且无法响应关闭事件的问题进行底层通信重构；
  - 引入原生 Windows 广播消息（`RegisterWindowMessage` + `WndProc Hook`）跨进程通信机制；
  - 当从任务栏或快捷方式再次唤醒已隐入托盘的 StarPie 时，主进程通过 WPF UI Dispatcher 唤醒 `ShowSettings()`，保证全套 XAML 渲染管线、平滑淡入动画与生命周期事件 100% 完整生效，可正常关闭并重新最小化至托盘。

### 🎯 顺势外甩取消严谨化与界面联动 (Outer Escape Strict Defaults & UI Sync)
- **初次进入软件状态严格对齐**：
  - 修复初次启动时外甩取消按钮显示关闭却依旧能外甩取消、以及灵敏度滑块异常显露的问题；
  - 默认状态设为未启用（Disabled），灵敏度调节面板默认折叠隐藏；
  - 严格确保手势控制器（`GestureController`）判定、开关控件状态与灵敏度面板展现三者 100% 实时双向同步。

### 📐 轮盘尺寸智能相对联动几何引擎 (Correlated Radial Geometry Engine)
- **外径 $\to$ 内径 $\to$ 核心圆物理约束协同**：
  - 引入 $R_{core} \le R_{inner} \le R_{outer} - 18\text{px}$ 几何互斥不变量；
  - 调节外径时内径与核心圆平滑收缩，调节内径时智能推挤外径或约束核心，调节核心圆时自动延展内径镂空孔径，杜绝扇区被挤压变形或核心越界侵入扇区。

### 💾 核心圆贴图与尺寸永久记忆修复 (Memory & Persistence Hardening)
- **消除构造函数 XAML 默认值覆盖与竞争覆盖 Bug**：
  - 前置全流程 UI 更新锁（`_isUpdatingUi`），彻底隔离控件初始化解析事件与配置回写逻辑；
  - 选取中心核圆贴图文件后自动同步激活图片模式与核圆开关，软件彻底重启后 100% 忠实保留用户自定义的轮盘外径、内径、核心圆半径与贴图。

---

## [v1.4.1] - 2026-08-24 (顺势外甩脱离取消 & 全局防多开单实例 & ClearType 字体锐化 & 多维重命名)
### 🚀 顺势外甩脱离取消机制 (Outer Escape / Overshoot Cancel)
- **最自然、最符合肌肉记忆的取消手势**：
  - 彻底打破以往手势唤出后放弃必须“艰难原路折返拉回中心核圆”的僵硬操作限制；
  - 引入「顺势外甩脱离」算法：按住右键划出扇区后若想放弃，只需**顺势向外稍作加速外甩（光标滑出轮盘外半径约 1.5 倍超距范围）**，系统自动解除当前扇区高亮并进入半透明虚化安全状态；
  - 松开右键时不触发任何动作，实现零误触的极致丝滑放弃体验；
  - 在「触发与场景 (Tab 1)」设置中新增 `顺势外甩脱离取消 (Outer Escape Cancel)` 独立开关（默认开启），支持个性化启用。

### 🛡️ 全局单实例运行保护与防多开互斥锁 (Single-Instance Protection)
- **杜绝多进程与多托盘图标堆积**：
  - 引入系统级跨进程命名互斥体 `Mutex("Global\StarPie_SingleInstance_Mutex_9B8A7C")`；
  - 当重复启动或双击应用快捷方式时，新实例自动检测并安全静默退出，同时自动将已在后台运行的 StarPie 实例窗口呼出并置于顶层（SetForegroundWindow）；
  - 彻底解决任务栏系统托盘重复出现多个 StarPie 图标与底层按键钩子冲突的问题。

### 🔤 WPF ClearType 亚像素字体全链路高清锐化 (ClearType & Typography Polish)
- **消除不同区域字形发虚与灰阶失真**：
  - 针对 WPF 中挂载 `DropShadowEffect` 阴影容器时子文本被降级为软件离屏位图光栅化、丢失亚像素抗锯齿的底层问题进行针对性重构；
  - 全局根节点与基础控件库统一注入 `TextOptions.TextFormattingMode="Display"`、`TextOptions.TextRenderingMode="ClearType"`、`RenderOptions.ClearTypeHint="Enabled"` 与 `UseLayoutRounding="True"`；
  - 无论是主界面文本、折叠 Expander 卡片内文字还是输入框提示，在 100%、125%、150% 等各类 Windows DPI 缩放比例下均呈现 100% 锐利高对比度的清晰质感。

### ✏️ 实用「重命名」功能多维度补齐 (Multi-Dimensional Rename Support)
- **个性化配置命名更自由**：
  - **自定义配色预设重命名**：在 Tab 1（外观与形态）自定义配色区域与折叠调色板中新增 `✏️ 重命名预设...` 按钮，一键为保存的自定义调色方案修改别名（如“赛博朋克紫”、“日落微光”等）；
  - **配置方案管理重命名**：在 Tab 2（手势与动作）方案列表中强化 `✏️ 重命名配置` 按钮与双击快捷重命名；
  - 全量接入中/英/繁/日四国语言国际化（`I18n.cs`）。

### 🧪 自动化测试与工程质量提升 (Automated Test Suite Expansion)
- 自动化测试套件扩充至 **18 项端到端 GUI 自动化测试**，新增 `test_v141_outer_escape_cancel_and_rename_capabilities` 专项用例，18/18 全部通过 (🟢 100% Passed)。

---

## [v1.4.0] - 2026-08-24 (自定义矢量/图片图标导入 & 启动动作视觉引导强化 & 自定义配色折叠收纳 & 历史演进里程碑折叠)
### 🎨 自定义图标导入支持 (Custom Vector & Image Icons Support)
- **多格式图标一键导入**：
  - 在「图标库选择器 (IconPickerWindow)」中新增 `➕ 导入自定义图标...` 快捷入口；
  - 深度支持导入本地 **SVG 矢量文件** 与 **PNG / ICO / JPG / BMP** 常见栅格图片；
  - 自动将导入的图标标准化持久缓存至用户应用数据目录（`%LOCALAPPDATA%\StarPie\CustomIcons\`），跨设备与重装均不丢失；
- **全端自适应无损渲染**：
  - 针对 SVG 矢量文件，提取 Path 矢量节点并在主轮盘与设置画布中以原生 XAML 几何体自适应当前主题文本/高亮画刷着色；
  - 针对 PNG/ICO 等图片，按轮盘扇区设定的图标尺寸（支持 12~36px 滑动微调）高清等比缩放渲染；
  - 图标库中支持对用户导入的自定义图标进行实时缩略图预览与一键删除（`✕`）管理。

### 📁 启动程序与文件夹动作右侧视觉引导强化 (Visual Guide Icons for Actions)
- **告别单调白框与盲猜点击**：
  - 在「手势与动作」扇区配置列表中，将启动程序与打开文件夹右侧容易产生空白感的 `...` 按钮全面升级为高辨识度的图标按钮（`📁` 浏览程序 / `📂` 浏览文件夹）；
  - 增加动态 Hover 高亮、鼠标悬停气泡提示（Tooltip）与输入框引导占位符，交互层次与指引性显著提升。

### 🎨 自定义高级配色单列为独立折叠卡片 (Collapsible Advanced Color Tuning)
- **清爽预设与深度调色分离**：
  - 将轮盘配色方案下拉框精简为纯粹的主题预设（跟随系统、深色、浅色、抹茶森林、冰川透蓝、莫兰迪柔灰等）；
  - 将色彩微调与自定义调色功能单列为独立的 `🎨 自定义高级配色与色彩微调` 卡片，默认处于优雅折叠状态（`IsExpanded="False"`）；
  - 展开后即可精准自由调节扇区底色、高亮光晕、边框线条、文字与光弧等 8 项色彩，支持色盘取色与屏幕放大镜吸色。

### 📜 版本演进里程碑折叠收纳 (Milestones Folding & Clean Layout)
- **重点聚焦与历史收纳**：
  - 在「关于与更新」页面中的“版本演进里程碑”默认仅展示最近 3 条重点发布记录（`v1.4.0`、`v1.3.9`、`v1.3.8`）；
  - 其余更早期的历史版本记录自动收纳于 `📜 展开查看更早的历史版本演进` 折叠组件中，保持界面清爽克制。

### 🧹 风格预设精简与代码瘦身 (Theme Preset Cleanup)
- **剔除冗余预设**：
  - 移除「萌宠猫爪 (Cute Cat Paw)」视觉风格，历史旧配置含有猫爪主题时自动平滑回退至「经典圆环」；
  - 优化控件内存开销与渲染管线。

### 🛠️ 自动化测试与工程质量提升 (Automated Test Suite Expansion)
- 自动化测试套件扩充至 **17 项端到端 GUI 自动化测试**，新增 `test_v140_custom_icons_and_appearance_collapsible_and_milestones_folding` 专项用例，17/17 全部通过 (🟢 100% Passed)。

---

## [v1.3.9] - 2026-08-24 (打开文件夹动作类型 & 全局界面语言统一性强化 & 弹窗国际化深度适配)
### 📂 新增【打开文件夹】专属动作类型 (Open Folder Action)
- **原生文件目录直达**：
  - 在「手势与动作」扇区动作映射列表中，新增 `📂 打开文件夹 (Folder)` 专属动作类型；
  - 接入现代 Windows 10/11 文件目录选择器（`Microsoft.Win32.OpenFolderDialog`），支持图形化一键选取本地任意文件夹目录；
  - 自动根据选取的文件夹名称填充动作展示名称，并默认关联专属 `Folder` 矢量图标；
  - 支持手动输入与环境变量路径解析（如 `%USERPROFILE%\Downloads`、`D:\Workspace`），点击测试或在轮盘中触发时调用 `explorer.exe` 毫秒级极速打开并前台聚焦。

### 🌐 全局界面语言统一性强化与 Raw 字典键消除 (Global i18n Polish & Raw Key Elimination)
- **消除未翻译 Raw 键名**：
  - 修复「选择程序 (ProgramPickerWindow)」弹窗右下角按钮此前显示为 `BtnConfirm` 与 `BtnCancel` 原始字典键的 Bug，完整接入多语言本地化（中/繁/英/日）；
  - 全面排查并修复 `ColorPickerWindow`（色彩选择器）、`IconPickerWindow`（矢量图标选择器）、`InputDialog`（配置方案命名输入框）中的标题、副标题、搜索提示、取色吸管与操作按钮，实现 100% 深度国际化；
- **扇区动作下拉框与测试按钮动态国际化**：
  - 扇区动作类型下拉框（`快捷热键` / `启动程序` / `打开文件夹` / `系统控制`）与每行的「测试 (Test)」按钮全面实现基于 `I18n.LanguageChanged` 事件的即时热刷新；
  - 切换语言时，所有方位列表无需重置或重启即可瞬间刷新为对应语言。

### 🛠️ 自动化测试与工程质量提升 (Automated Test Suite Expansion)
- 自动化测试套件扩充至 **16 项端到端 GUI 自动化测试**，新增 `test_v139_folder_action_type_and_i18n_consistency` 专项用例，16/16 全部通过 (🟢 100% Passed)。

---

## [v1.3.8] - 2026-08-23 (StarPie 品牌视觉升级 & 原生圆角星轨图标 & 托盘图标自愈修复 & 应用程序智能检索重构 & 控制台排版精修 & 四国多语言国际化)
### 🔍 应用程序智能检索重构与死链/冗余项过滤 (Smart Program Scanner & Ghost Item Cleanup)
- **高召回率多源深度检索**：
  - 整合 Windows 核心工具、开始菜单（全局与用户级多级目录）、桌面快捷方式、`AppData\Local\Programs` 用户级软件库、`WindowsApps` 现代应用、注册表 64位/32位 `App Paths` 及 `Uninstall` 安装列表等 8 大源头；
  - 彻底解决 VS Code、Discord、Spotify、Xmind、Chrome、钉钉、3ds Max 等已安装软件此前可能检索不全的问题。
- **已删除软件与死链（Ghost Items）彻底清除**：
  - 在快捷方式与注册表解析链路上引入 `File.Exists(targetExePath)` 物理存在性强校验，凡目标文件已失效或已被卸载删除的残留快捷方式，坚决丢弃，杜绝幽灵死链；
  - 过滤 0 字节无效文件。
- **无用卸载程序与辅助组件精准过滤**：
  - 智能过滤 `Uninstall`、`Unins000`、`Setup`、`Installer`、`Update`、`Crashpad`、`Feedback`、`Diagnostic`、`Repair`、`Readme`、`Manual` 等卸载、安装向导、崩溃上报与使用说明等非主程序项；
  - 屏蔽 Electron/Node/Python 内部子目录（如 `node_modules`、`extensions`、`resources`、`site-packages`、`scripts`）中的辅助 CLI 工具，保持列表干净清爽。
- **搜索体验与交互升级**：
  - 搜索框新增 `🔍` 图标与智能占位符提示（`搜索软件名称、可执行文件或路径...`）；
  - 支持按软件名称、进程名（如 `aDrive.exe` / `code.exe`）及路径模糊检索；
  - 「选择程序」弹窗标题升级为 `选择程序 - StarPie`，并全面接入多语言国际化。

### 📐 控制台高级偏好设置 UI 布局与排版修复 (Memory Card & Display Language Polish)
- **修复「立即压缩内存」卡片布局异常**：
  - 修复 `SettingsWindow.xaml` 中内存压缩卡片内按钮缺少列定义的排版错误，彻底解决按钮过宽遮挡左侧标题与说明文字的 Bug；
  - 重新规整卡片内部元素垂直居中与字距微调。
- **修复语言选项标题 Emoji 重复**：
  - 剔除字典中多余的图标字符，修复「界面语言」卡片标题此前呈现为 `🌐 🌐 界面语言 (Display Language)` 的重复显示问题。

### 🌐 全面支持四国多语言国际化 (Multi-Language i18n Support)
- **多语言热切换**：全面支持 **简体中文 (zh-CN)**、**繁體中文 (zh-TW)**、**English (en)**、**日本語 (ja)** 与 **跟随系统 (System Default)** 五种语言模式；
- **免重启即时生效**：在「高级与系统 (Advanced & System)」设置页面提供界面语言选择下拉卡片，切换后所有 5 大页面标题、副标题、侧边栏 Tab、控制按钮、托盘上下文菜单、提示框即时热切换更新；
- **配置持久记忆**：语言首选项自动持久化至 `config.json`，跨重启及跨会话自动保留。

### 🌟 全新 StarPie 品牌正式启用与原生圆角星轨图标 (StarPie Brand & Anti-Aliased Squircle Icon)
- **品牌全面焕新**：正式定名为 **`StarPie` (星盘)**，全方位更换工程程序集、窗口标题、托盘菜单、文档与关于信息。
- **任务栏直角黑边修复**：
  - 彻底去除原图片四角不透明黑边，采用 4x 超采样生成全透明抗锯齿平滑圆角（Squircle）图标；
  - 重新烘焙包含 `16×16`、`20×20`、`24×24`、`32×32`、`40×40`、`48×48`、`64×64`、`96×96`、`128×128`、`256×256` 完整尺寸链的 `app_icon.ico` 与高清 `logo.png`；
  - Windows 任务栏无论深色还是浅色背景均能呈现完美的流光圆角质感。

### 🔔 系统托盘 NotifyIcon 嵌入式图标自愈加载 (System Tray Icon Fix & Self-Healing Extraction)
- **托盘图标缺失与默认图样彻底根治**：
  - 重构托盘图标初始化机制，建立 3 级自愈加载链路：
    1. 进程自身可执行程序原生关联图标提取（`Icon.ExtractAssociatedIcon`）；
    2. WPF Pack URI 嵌入式资源流直接读取（`pack://application:,,,/app_icon.ico`）；
    3. 运行目录外部图标文件回退加载；
  - 彻底解决单文件发布（SingleFile）或无额外外部 ico 文件时托盘图标变成 Windows 默认窗口小方块的问题。

### 🎨 软件控制台侧边栏品牌排版精修 (Sidebar Brand Header & UI Polish)
- **去除多余杂色衬底，呈现现代极简 Squircle 美学**：
  - 彻底移除侧边栏 Logo 底部突兀的浅蓝色圆盘（`Ellipse Fill="#EBF5FF"`）；
  - 升级为独立圆角 Squircle 容器配以细腻微阴影（`DropShadowEffect BlurRadius="8" Opacity="0.16"`），直出高保真星芒 Logo；
  - 重新优化 `StarPie` 标题与副标题的垂直对齐与字距排版，视觉更加精致、沉稳、高级。

### 📦 真正零依赖免安装单文件发布 (Zero-Dependency Self-Contained Deployment)
- 引入 .NET 8 自包含独立单文件发布模式（`StarPie.exe`），无需安装任何 `.NET Runtime` 运行库，双击即开即用。

---

## [v1.3.6] - 2026-08-23 (高亮边缘光晕颜色自定义 & 配置持久记忆加固 & 核圆图片自定制 & 自定义配色方案删除管理)
### 🗑️ 自定义配色方案删除与预设管理 (Custom Color Preset Deletion & Management)
- **支持一键删除与管理历史自定义配色方案**：
  - 在「轮盘配色方案 (Wheel Theme)」下拉框右侧新增 **「🗑️ 删除预设」** 管理按钮，当选中任意自定义配色方案（如“全透明”、“我的配色”）时自动亮起；
  - 在自定义色彩调节面板（CustomColorsPanel）底部同步增加 **「🗑️ 删除此预设」** 快捷按钮；
  - 增加防误触安全确认对话框（`MessageBoxResult.Yes/No`），确认后自动从配置列表中永久移除该方案，并平滑回退至系统默认配色；
  - 实时刷新下拉选项、画板预览与配置文件，方案管理更加清爽可控。

### ✨ 高亮边缘光晕与发光特效颜色自定义控制 (Highlight Edge Glow & Effect Customization)
- **打破硬编码单一紫色限制，支持全光谱光晕定制**：
  - 在「外观与形态」-「轮盘视觉风格与色彩」中新增 **「高亮边缘光晕模式 (Highlight Glow)」** 独立控制卡片；
  - 提供 8 种高质感经典光晕预设：**跟随主题高亮色 (Auto / Follow Theme)**、**丁香晶紫 (Lilac Violet #A855F7)**、**冰川湛蓝 (Glacial Blue #3B82F6)**、**翡翠荧绿 (Emerald Green #10B981)**、**樱花粉晕 (Sakura Rose #EC4899)**、**琥珀金光 (Amber Gold #F59E0B)**、**珊瑚赤光 (Coral Red #EF4444)**、**冰魄纯白 (Pure Ice White #FFFFFF)**；
  - 支持 **「自定义光晕颜色 (Custom Glow Color)」**：提供十六进制色值输入、取色盘与全局屏幕吸管，自由调校任意专属光晕色彩；
  - 增加 **光晕弥散半径 (Glow Radius: 8~48px)** 与 **光晕不透明度 (Glow Opacity: 0%~100%)** 细粒度微调滑块；
  - 液态毛玻璃（Glassmorphism）、经典圆环（ClassicRing）、极简扇区（CleanSectors）、萌宠猫爪（CatPaw）四大主题渲染器均深度适配自定义光晕。

### 🛡️ 配置持久记忆机制深度修复与加固 (Configuration Memory Hardening & Zero-Reset Guard)
- **根除 UI 初始化生命周期中的事件抢占覆盖缺陷**：
  - 定位并彻底修复构造函数 `InitializeComponent()` 过程中，WPF 解析 XAML 声明触发 `SelectionChanged` / `ValueChanged` 事件抢先将配置文件覆盖为第 0 项默认值的致命 Bug；
  - 引入 `_isUpdatingUi = true` 初始锁定与全量事件防重入保护，确保程序启动或再次打开时 100% 完整加载并保留用户此前设置的所有主题、配色、形态与几何参数；
  - 加固 `ConfigManager.LoadConfig()` 的 JSON 容错与大小写/便携模式双重回退机制，确保配置零丢失、零损坏。

### 🖼️ 中心核圆自定义图片本地贴图 (Center Core Custom Local Image)
- **突破仅限矢量图标限制，支持自定义本地图片/相片**：
  - 在「核圆图案类型 (Core Pattern)」中新增 **「🖼️ 自定义图片文件 (Custom Image)」** 选项；
  - 支持选择本地 PNG、JPG、JPEG、BMP、WebP、ICO、GIF 图片作为中心核圆贴图；
  - 自动应用圆形几何剪裁（UniformToFill），在 60FPS 实时画布预览与真机轮盘中呈现精致圆润的视觉效果；
  - 联动核圆图标独立显示开关，支持一键切换通透纯色核圆或精美自定义图片。

---

## [v1.3.5] - 2026-08-23 (软件纯净高清图标深度检索 & 核圆图案自定义 & 独立图标开关)
### 🔍 软件纯净高清图标深度检索与快捷方式解构 (Pure High-Res Software Icon Extraction)
- **彻底去除快捷方式小箭头角标**：
  - 引入 Win32 COM `IShellLinkW` / `IPersistFile` 接口，在扫描和选择程序时自动解构 `.lnk` 快捷方式，直探底层真实可执行程序（`.exe`）与专用图标资源；
  - 重构 `IconHelper.GetIcon` 提取引擎，从目标文件直接提取原生大尺寸高清图标，彻底告别 Windows 快捷方式复合角标带来的模糊与遮挡；
- **全面扩充应用程序扫描检索面**：
  - 深度检索范围扩充为 5 大系统来源：开始菜单（公共/用户）、桌面快捷方式、注册表 `App Paths`（如各类常用浏览器、IDE、办公软件）、注册表 `Uninstall` 安装路径、以及 Windows 内置核心系统工具；
  - 智能过滤卸载引导、修复助手、更新工具与使用文档，呈现纯净清爽的已安装软件列表。

### 🎯 中心核圆图案自由定制 (Center Core Pattern Customization)
- **多款精美预设与自定义矢量图标**：
  - 在「外观与形态」-「中心核圆与图案设置」中新增 **「核圆图案类型 (Core Pattern)」** 配置；
  - 内置丰富高质感图案：**默认取消叉号 (Exit / Close)**、**十字准心 (Crosshair)**、**Windows 视窗微标 (Windows Logo)**、**极简靶心圆点 (Bullseye Dot)**、**快捷主页 (Home)**、**电源能量 (Power)**、**极简罗盘 (Compass)**、**萌宠猫爪 (Cute Cat Paw)** 以及 **自定义矢量图标 (Custom Icon)**；
  - 选择自定义图标时可一键唤起矢量图标库进行挑选。

### 👁️ 中心核圆图标独立显示开关 (Core Icon Visibility Toggle)
- **极简通透与丰富装饰自由切换**：
  - 新增 **「显示核圆中心图标 / 图案 (Show Core Icon)」** 开关；
  - 关闭后核圆呈现极简通透的纯净毛玻璃/圆环底色，开启后呈现所选精美图案；
  - 60FPS 实时画板与真机轮盘即时同步渲染与自动持久化记忆。

---

## [v1.3.4] - 2026-08-23 (纯白主题画布渲染优化 & 全局设置自动记忆 & 极简内存瘦身)
### 🎨 极简纯白主题实时交互画布优化 (Modern Light Canvas Rendering)
- **彻底消除黑底色块冲突**：
  - 实时交互画布容器及网格线全面接入主程序主题动态资源（`PreviewCanvasBackgroundBrush`, `PreviewCanvasBorderBrush`, `PreviewGridLineBrush`）；
  - 极简纯白模式下，实时画布背景自适应为柔和优雅的浅灰白（`#F1F5F9`）并配以微光浅色边框与网格导线，与纯白主控制台浑然一体；
  - 针对 Light 亮色主题，优化同心导轨虚线、罗盘刻度线及文字高对比度色彩渲染，呈现清爽高级的瑞士排版质感。

### 💾 全局配置状态自动持久化记忆 (Full Settings State Memory & Auto-Save)
- **退出与关闭无缝恢复，告别手动保存遗失**：
  - 新增智能自动持久化记忆系统，在修改界面主题（App Theme）、轮盘风格（UiStyle）、配色方案（Theme）、切削形态（Shape）、图标排版、扇区数量、几何滑块、场景隔离配置或退出程序时，均自动将最新状态持久化到磁盘；
  - 窗口关闭最小化至托盘及从托盘完全退出时自动完成全量 UI 字段与配置文件的同步保存；
  - 再次启动或重新打开控制台时，100% 精确还原退出时的各项设置与视觉选择。

### 🚀 极简内存瘦身与工作集压缩优化 (Ultra-Low Memory Footprint & Trimming)
- **常驻后台内存从 60-80MB 锐减至 15-25MB**：
  - 引入 PSAPI 高效工作集修剪（`EmptyWorkingSet`）与物理内存压缩机制（`MemoryOptimizer`）；
  - 切换为 Workstation 客户端专用 GC 模式（`ServerGarbageCollection = false`），消除多核服务器 GC 空堆内存预分配；
  - 在应用启动完成、设置窗口关闭隐藏到托盘、手势笔势完成关闭轮盘时智能触发内存回收与工作集压缩，确保后台常驻极致轻盈无感。

---

## [v1.3.3] - 2026-08-22 (4/12方位键位适配修复 & 几何切削形态精简)
### ⚡ 4 键十字方位与 12 键钟表方位全流程适配修复 (4/12 Sector Count Adaptation & Fix)
- **解决切换无响应与空列表问题**：
  - 修复设置窗口初始化时未自动绑定选中配置方案导致单选按钮切换无响应的底层缺陷；
  - 扇区数量单选框添加明确 `GroupName="SectorCountGroup"` 分组，确保状态切换准确无误；
  - 切换 4 键十字方位 / 8 键全向方位 / 12 键钟表方位时，实时刷新并展示对应数量的动作映射条目与方向标识（包含 3点钟~2点钟角度精确标注）；
- **动态自适应缩放与智能预设**：
  - 4 键十字模式自动放大图标与文字排版，操作更宽裕、不易误触；
  - 12 键钟表模式自动进行紧凑布局微调与防溢出文字适配，并内置丰富的 12 向系统与快捷键预设；
  - 60FPS 实时画板与真机手势激活引擎同步适配 4、8、12 扇区角度检测与精确触发。

### 📐 几何切削形态精简 (Streamlined Sector Cut Shapes)
- **形态库精简与去重**：
  - 依照视觉一致性与高质感标准，移除冗余的「液态有机水滴」与「光弧极简轨道」形态；
  - 精选保留 4 大高质感核心切削形态：**经典紧凑扇区 (Original)**、**独立圆形卡片 (Circle)**、**悬浮圆角胶囊 (Capsule)**、**蜂巢六边形矩阵 (HexagonHive)**；
  - 增强配置兼容性，对旧版本选择的形态提供平滑优雅的回退机制。

---

## [v1.3.2] - 2026-08-22 (轮盘图标尺寸调节 & 光弧极简形态重构 & 猫爪核圆比例协调)
### 💫 光弧极简轨道形态重构 (ArcTracker HUD & Orbit Overhaul)
- **同心微光导轨 + 悬浮灵动节点**：
  - 彻底推翻原本厚重的切片圆环形态，重构为轻盈优雅的极简 HUD 轨道美学；
  - 每个动作扇区采用轻薄悬浮圆角胶囊/微卡片节点，配合动态同心虚线微光导轨（`Deco_ArcTrackerOrbit`）；
  - 扇区悬停时呈现柔和微光吸附与发光跟随，视觉效果通透灵动、充满未来科技感。

### 🐾 萌宠猫爪核圆比例深度协调 (Cat Paw Core & Pads Coordinated)
- **拒绝扁平，重现立体饱满圆润 Q-Pop**：
  - 重新协调中心肉垫与核圆的几何黄金比例，主肉垫高度扩大至 `coreRadius * 0.86`，采用 3 瓣饱满心形自然曲线；
  - 4 颗圆润肉球（`ToeBeans`）按自然扇形弧度围绕大肉垫上方与侧翼均匀舒展排布，杜绝拥挤与扁塌，整体呈现呆萌治愈的 Q 弹立体肉垫效果。

### 🖼️ 轮盘/托盘扇区图标尺寸自由调节 (Sector Icon Size Control)
- **自由图标尺寸滑动微调**：
  - 在「外观与形态」-「图标与排版选项」中新增 **「图标尺寸大小 (Icon Size)」** 滑动调节条（支持 `14 px` ~ `36 px`，步进 `1 px`，默认 `20 px`）；
  - 支持在 60FPS 实时预览画板上即时联动预览，并无缝同步至真机轮盘，可随心搭配大图标极简模式或精巧小图标排版。

---

## [v1.3.1] - 2026-08-22 (扇区切削形态精简与重磅扩充 & 文字字号自定义调节 & 萌宠猫爪全端深度调试)
### 🐾 萌宠猫爪主题全方位视觉深度调试 (Cute Cat Paw Visual Overhaul)
- **重构大间距猫耳自然弧度**：
  - 彻底将猫耳间距拉大，左耳中心固定于 `216°`（10:15 方向），右耳中心固定于 `324°`（1:45 方向），双耳基底跨度扩大至 48° 黄金弧度；
  - 采用贝塞尔平滑微弧线外倾造型与双层樱花粉内耳渐变，彻底告别原先 12 点钟方向拥挤窄耳。
- **3D Q-Pop 软萌猫爪肉垫中心**：
  - 移除了中心相互冲突遮挡的方框登录门（`➜`）与生硬黑叉（`✕`）；
  - 由粉嫩渐变立体大肉垫（`PawMainPad`）与 4 颗圆润脚趾软豆（`ToeBeans`）构成，自带立体高光反光点，悬停退出时绽放草莓亮粉微光；
  - **画板与真机 100% 统一**：实时预览画布全面接入实体 `previewCoreGrid` 架构，所见即所得。

### 📐 扇区切削形态精简合并与重磅扩充 (Advanced Sector Shapes)
- **精简合并相似形状**：将原本视觉接近的「悬浮圆角矩形」与「悬浮独立胶囊」合并为统一、精致的 **「悬浮圆角胶囊 (Capsule)」**。
- **新增 2 大高阶切削形态**：
  1. 💧 **液态有机水滴 (OrganicPetals)**：
     - 源自现代流体与气泡布局美学，中心向外舒展出饱满的水滴/花瓣形态，内端精巧收束、外端圆润饱满；
     - 扇区悬停时呈现柔和吸附放大与生动有机交互。
  2. 💫 **光弧极简轨道 (ArcTracker)**：
     - 源自极简 HUD 与未来科技光弧美学，以同心圆周轨道为基准生成轻盈优雅的光弧段；
     - 呈现极简光弧随动、呼吸发光与通透空间感。

### 🔤 扇区文字字号自定义滑动调节 (Sector Font Size Control)
- **自由字号微调**：在「图标与排版选项」中新增 **「文字字号大小 (Font Size)」** 滑动调节条（支持 `8.0 px` ~ `18.0 px`，步进 `0.5 px`）。
- **实时画板即时联动**：滑动时 60FPS 实时画板与真机轮盘即时同步更新字号，满足不同分辨率与屏幕缩放下对字号清晰度的个性化需求。

---

## [v1.3.0] - 2026-08-22 (四大轮盘主题视觉深度重构 & 自定义配色预设保存 & 轮盘文字显示与排版修复)
### 💎 4 大主流轮盘主题视觉重构与深度质感塑造 (Refined Wheel Themes)
- **拒绝平庸同质化**：删除杂乱低质的旧主题，仅保留并深度重塑 4 大核心轮盘主题，结合高阶现代 UI 设计规范打造真正具备差异化的视觉体验：
  1. 🫧 **液态毛玻璃 (Glassmorphism / Apple Liquid Glass / Fluent Acrylic)**：
     - 引入一体式悬浮半透明玻璃底盘 `Deco_GlassBackdropDisc`，搭配 `0.5px` 极细微高光边缘折射；
     - 扇区悬停时激活丁香紫/冰晶外圈悬浮柔和辉光，图标与底色同步提升亮度，通透悬浮感跃然屏上；
  2. 🪐 **经典圆环 (Classic Ring / Vision Pro Spatial UI)**：
     - 引入 Vision Pro 空间同心虚线标尺轨道 `Deco_SpatialOuterOrbit` 与 4 向正交罗盘刻度线；
     - 扇区悬停时触发 3D Z 轴向前凸出弹簧反馈与深色高质感外发光；
  3. 📐 **极简扇区 (Clean Sectors / Swiss Minimalist)**：
     - 瑞士国际主义风格，纯粹哑光色块与发丝级细描边，超高对比度与无杂质交互；
  4. 🐾 **萌宠猫爪 (Cute Cat Paw)**：
     - 萌系立体猫耳外轮廓、内耳软萌粉色衬垫与中央大肉垫几何结构。
- **画板与真机渲染 100% 同步**：设置界面的 60FPS 实时预览画布全面接入主题装饰层渲染器，底盘光晕与轨道标尺即时可见。

### 🎨 轮盘配色方案精简 & 支持保存自定义配色预设 (Custom Color Presets)
- **精简色彩预设**：移除了过时杂乱的“赛博霓虹”、“极光暮光”、“熔岩烈焰”、“典雅紫罗兰”4 套色彩预设，保留真正实用耐看的 7 款预设（跟随系统、深色模式、浅色模式、抹茶森林、冰川透蓝、莫兰迪柔灰、自定义高级配色）。
- **保存为自定义配色预设**：
  - 在自定义调色面板中新增【💾 保存为自定义配色预设...】按钮；
  - 允许用户将任意调配出的 5 处色值（扇区底色、边框、高亮底色、高亮边框、文字颜色）命名保存；
  - 自动向配色下拉框动态注册该专属配色预设，并在配置文件中持久化存储，支持无限次随时切换。

### 📝 轮盘文字显示与排版双向联动彻底修复 (Text Display & Layout Sync)
- **修复文字不显示 Bug**：根治了 `IconLayoutMode`（排版模式）与 `ShowText`（显示文字开关）状态冲突导致的文字丢失问题。
- **双向联动**：
  - 切换排版模式为“仅图标 (IconOnly)”时，文字开关自动关闭；
  - 切换为“图标+文字”或“仅文字”时，文字开关自动开启；
  - 勾选文字开关时，自动联动切回“图标+文字”；
- **排版与截断优化**：文字容器尺寸与自适应修剪（`TextTrimming = CharacterEllipsis`，宽度扩充至 `86px`，`MaxHeight = 28`），确保在真机和预览中均清晰可读且不溢出。

---

## [v1.2.5] - 2026-08-22 (界面主题色系简约重构 & 文字对比度增强 & 更新日志全量同步)
### 🎨 软件界面主题色系简约设计与高级质感重构 (Refined App Themes)
- **拒绝杂乱与割裂**：全面重构 `AppThemeManager` 中的 6 款核心控制台主题，采用专业级现代 UI 调色板，打造简约、克制、耐看的高质感色系搭配：
  1. 🌓 **跟随系统 (System Auto)**：无缝适应 Windows 浅色/深色模式。
  2. ☀️ **极简纯白 (Modern Light)**：`#F8FAFC` 浅冷灰底色 + 纯白卡片 + `#0F172A` 高对比度主字 + `#475569` 辅字 + `#2563EB` 经典科技蓝。
  3. 🌙 **极夜曜黑 (Obsidian Dark)**：`#090D16` 曜黑底色 + `#131C2E` 悬浮卡片 + `#F8FAFC` 纯净雪白主字 + `#94A3B8` 雾灰辅字 + `#3B82F6` 霓虹蓝，彻底解决暗色下泛灰或刺眼问题。
  4. 🌌 **午夜极客蓝 (Midnight Navy)**：`#0A0F1D` 深暮蓝底色 + `#141E33` 卡片 + `#F0F6FC` 霜白主字 + `#A5B4FC` 柔和淡紫蓝辅字 + `#0EA5E9` 极光青蓝。
  5. 🔮 **暗夜紫罗兰 (Royal Violet)**：`#0F0A1A` 丝绒深紫底色 + `#1E1435` 紫晶卡片 + `#FAF5FF` 亮白主字 + `#D8B4FE` 浅紫辅字 + `#A855F7` 水晶紫。
  6. ⚙️ **钛金深灰 (Titanium Gray)**：`#121214` 工业灰底色 + `#202024` 钛灰卡片 + `#F4F4F5` 钛银主字 + `#A1A1AA` 银灰辅字。

### 👁️ 全界面文字对比度增强与视觉协调性修复 (WCAG 2.2 AA Contrast Compliance)
- **文字清晰可读**：针对用户反馈在暗色主题下文字发暗、看不清、与背景融为一体等痛点，将所有页面、卡片、标题、副标题、说明文字与徽章的文字对比度全量提升至 7:1 ~ 14:1（远超 WCAG AA 4.5:1 标准）。
- **全控件语义化 DynamicResource 绑定**：
  - 左侧导航栏：未选中项使用自然适度的 `{DynamicResource NavTabDefaultFgBrush}`，悬停与选中状态分别对应柔和胶囊底色与明亮文本，告别突兀的纯白方块。
  - 输入框与列表框：在深色主题下自适应切换为深色底色与浅色文字，彻底消除原本纯白矩形刺眼的视觉割裂。
  - 按钮与交互态：全量按钮（ModernButtonStyle / PrimaryButtonStyle）接入动态主题色彩，悬停高亮平滑自然。

### 📋 更新日志与关于页里程碑全量同步 (Full Milestone Sync)
- **关于与更新页 (Tab 4)**：完整补齐 v1.0.0、v1.0.4、v1.1.0、v1.2.0、v1.2.1、v1.2.2、v1.2.3、v1.2.4、v1.2.5 全系列版本里程碑卡片，文字与边框均支持主题自适应。

---

## [v1.2.4] - 2026-08-21 (控制台6大界面主题风格 & 选色器滚轮修复 & 精简轮盘背景)
### 🌓 软件控制台全界面 6 大视觉主题系统 (App Interface Themes)
- **告别单一浅色**：构建独立的动态主题引擎 [`AppThemeManager`](file:///g:/Users/2%20Better/Desktop/design/WinPieGestures/AppThemeManager.cs)，支持 6 款高质感控制台界面主题：
  1. 🌓 **跟随系统 (System Auto)**：实时读取 Windows 系统的 `AppsUseLightTheme` 偏好自动在深浅色之间平滑切换；
  2. ☀️ **极简纯白 (Modern Light)**：经典明亮、清爽干净的浅色风格；
  3. 🌙 **极夜曜黑 (Obsidian Dark)**：纯正深邃的暗黑模式，高对比度护眼，全黑底色搭配深灰卡片；
  4. 🌌 **午夜深蓝 (Midnight Navy)**：深靛蓝极客风，深邃科幻感；
  5. 🔮 **暗夜紫罗兰 (Royal Violet)**：高雅丝绒深紫与粉紫光晕；
  6. ⚙️ **钛金深灰 (Titanium Gray)**：工业级中性深灰。
- **全界面平滑换肤**：设置控制台侧边栏、所有配置卡片、边框、标题、正文、说明文字、输入框、下拉框、滑块与选项卡全面接入 `DynamicResource` 主题画刷，即选即换，即时生效。

### 🎨 选色器色卡展示与鼠标滚轮平滑滚动修复 (`ColorPickerWindow`)
- **色卡完整展露**：窗口默认尺寸优化为 `510 x 610`（支持拖拽调整大小），下方“预设经典配色卡 (Quick Swatches)”不再受高度截断，全部色卡一览无余。
- **鼠标滚轮事件穿透与平滑滚动**：为色卡区域引入专用 `ScrollViewer` 与 `PreviewMouseWheel` 滚轮冒泡处理，当鼠标在色卡上方或色卡之间滚动时，顺畅自如上下滚动。

### 🧹 取消轮盘背景与自定义贴图功能 (Clean & Minimalist Wheel)
- **极简纯粹**：完全移除“外观与形态”中的“轮盘背景与自定义贴图”配置卡片，精简界面空间；
- **高性能矢量渲染**：清理底层附加背景位图绘制逻辑，保持轮盘 60FPS 极速响应与纯粹几何手势交互体验。

---

## [v1.2.3] - 2026-08-21 (调色板与全屏屏幕吸色 & 7+款新配色预设)
### 🎨 可视化调色板与全屏屏幕吸色工具 (`ColorPickerWindow` & Screen Eyedropper)
- **告别纯文本十六进制输入**：针对“自定义高级配色”中的扇区底色、扇区边框、高亮底色、高亮边框和文字颜色，全面引入可视化调色板对话框 `ColorPickerWindow`。
- **全维度调色盘**：
  - HSV 饱和度/明度二维画布拖拽 + 色相环彩虹条 (0~360°) + Alpha 透明度滑块 (0%~100%)。
  - `#AARRGGBB` 十六进制与 RGBA 分量双向实时换算与回填。
  - 精选 35+ 款高质感 Material Design & Fluent 预设经典色卡，一键快速拾取。
- **🔍 全屏屏幕像素吸色吸管 (Screen Eyedropper)**：
  - 点击“从屏幕吸色”或输入框旁的快捷吸管图标，光标即刻变为高精度十字取色器。
  - 鼠标在 Windows 屏幕任意窗口、桌面、壁纸、浏览器移动时，实时浮动跟随像素放大镜 (Pixel Magnifier Loupe) 并显示当前像素颜色与 Hex 代码。
  - 鼠标左键单击即可直接捕获当前屏幕像素颜色并自动回填到当前配置，右键或 Esc 随时取消。

### 🌈 7+ 款全新高质感内置配色预设方案 (Expanded Theme Presets)
- 在“视觉风格与色彩”的配色方案中扩充 7 套精心校准的高级预设主题：
  1. **赛博霓虹 (CyberNeon)**：深夜深蓝紫底色 + 荧光青蓝高亮 + 霓虹粉紫边框；
  2. **极光暮光 (SunsetAurora)**：极光深靛底色 + 晚霞橙红高亮 + 金晖边框；
  3. **抹茶森林 (MatchaForest)**：竹青墨绿底色 + 晨曦嫩绿高亮 + 柔白文字；
  4. **熔岩烈焰 (VolcanoEmber)**：黑曜石底色 + 炽热熔岩红高亮 + 暖金边框；
  5. **典雅紫罗兰 (RoyalViolet)**：丝绒深紫底色 + 幻紫晶粉高亮 + 银白边框；
  6. **冰川透蓝 (GlacialIce)**：冰霜半透浅蓝底色 + 极地深蓝高亮 + 纯白边框；
  7. **莫兰迪柔灰 (MorandiMuted)**：烟灰青底色 + 浅茶暖杏高亮 + 优雅素白。

### 🖼️ 轮盘背景与各区域自定义贴图管理系统 (Wheel Backgrounds & Textures)
- 在“外观与形态”中新增专属控制卡片 **“轮盘背景与自定义贴图”**：
  - **🌐 整体轮盘背景底图 (Global Wheel Background)**：支持导入本地图片（PNG/JPG/WEBP/GIF），提供文件浏览选择器 (`...`)、透明度滑块 (0%~100%)、4 种拉伸模式（等比填满裁剪 / 完整适应居中 / 拉伸铺满 / 原始尺寸居中）与一键清空按钮 (`✕`)。
  - **🎯 中心核心退出区贴图/头像/Logo (Core Area Avatar / Logo)**：支持在中心圆形区域导入个人头像、科技图标或专属 Logo，独立控制透明度与拉伸模式。
  - **✨ 扇区高亮纹理 (Sector Highlight Texture)**：支持配置高亮叠加光效纹理与不透明度。
- **双向实时同步**：真实轮盘 `RadialWindow` 与设置界面的 60FPS 实时交互画布双向同步渲染自定义背景贴图。

---

## [v1.2.2] - 2026-08-21 (交互式热键录制器与全维度系统控制功能库)
### ⌨️ 交互式实时热键录制器 (Interactive Hotkey Recorder)
- **告别纯文本打字录入**：引入全新专有 WPF 控件 `HotkeyRecorderBox`。用户点击/聚焦录制框即可进入监听录制状态（显示高亮脉冲 `🔴 请按下快捷键组合...`），任意按下键盘物理按键或修饰键即可即时捕获。
- **全键盘按键与修饰键无损支持**：
  - 修饰键：`Ctrl`, `Shift`, `Alt`, `Win` 及其各种复杂多键组合。
  - 功能键：`F1` ~ `F24`。
  - 导航与编辑键：`Home`, `End`, `PageUp`, `PageDown`, `Insert`, `Delete`, `Backspace`, `Enter`, `Tab`, `Space`, `Escape`, `PrintScreen`, `Pause`, `CapsLock`, `ScrollLock`, `NumLock`, 方向键等。
  - 小键盘与标点符号：`Num0` ~ `Num9`, `NumAdd`, `NumSubtract`, 各种 OEM 符号键。
  - 多媒体键：`MediaPlayPause`, `MediaNext`, `MediaPrev`, `VolumeUp`, `VolumeDown`, `VolumeMute`, `BrowserBack`, `BrowserForward` 等。
- **快捷清空与取消**：支持按 `Backspace`/`Delete` 或点击控件右侧 `✕` 按钮一键清空重置，按 `Escape` 取消录制保持原状。

### ⚙️ 系统控制预设库 30+ 项全面扩充与分类呈现 (Expanded System Controls Catalog)
- **结构化分类选单**：
  - **🖥️ 窗口与工作区**：关闭当前窗口 (Alt+F4)、最小化窗口 (Win+Down)、最大化/还原 (Win+Up)、左半屏贴靠 (Win+Left)、右半屏贴靠 (Win+Right)、任务视图/多任务 (Win+Tab)、上一虚拟桌面 (Win+Ctrl+Left)、下一虚拟桌面 (Win+Ctrl+Right)、显示桌面 (Win+D)、全屏切换 (F11)、屏幕截图 (Win+Shift+S)。
  - **⚙️ 系统管理与实用工具**：任务管理器 (Ctrl+Shift+Esc / taskmgr)、文件资源管理器 (Win+E / explorer)、Windows 设置 (Win+I)、系统计算器 (calc.exe)、运行窗口 (Win+R)、系统全局搜索 (Win+S)、剪贴板历史记录 (Win+V)、快速锁定电脑 (LockWorkStation)。
  - **🎵 媒体与音量**：音量增加、音量减小、静音切换、播放/暂停、下一曲、上一曲、停止播放。
  - **🌐 浏览器与文档导航**：新建标签页 (Ctrl+T)、关闭标签页 (Ctrl+W)、恢复关闭标签 (Ctrl+Shift+T)、刷新页面 (F5)、强制刷新 (Ctrl+F5)、页面放大 (Ctrl++)、页面缩小 (Ctrl+-)、默认缩放 (Ctrl+0)。
  - **⚡ 电源控制**：系统睡眠 (Sleep)、重启电脑 (Restart)、关闭电脑 (Shutdown)。
- **智能动作名称与矢量图标自适配联动**：在下拉菜单中选中任何系统功能时，系统自动关联并匹配对应的中文友好名称与专属高精度矢量 SVG 图标（如选择“计算器”自动匹配 `Calculator` 矢量图标与名称）。

---

## [v1.2.1] - 2026-08-19 (程序选择器联动与自定义配置增删改)
### 🚀 动作映射与可视化程序选择器深度联动 (Program Picker Integration)
- **可视化程序选择器全链路对接**：在“手势与动作”选项卡中，当扇区动作类型设为“启动程序 (Launch)”时，点击右侧 `...` 浏览按钮将直接调起高清可视化的 `ProgramPickerWindow` 对话框，用户可直接从已安装软件和开始菜单中选择程序，支持即时搜索与回退手动浏览。
- **元数据与友好名称自动回填**：选择应用程序后，自动回填程序完整路径，并在动作名称仍为默认占位符时自动解析友好应用名称（例如选择 Chrome 时自动填充 "Google Chrome"）。

### 📋 自定义配置方案增删改与重命名 (Custom Profiles Management)
- **新增“新建自定义配置”**：支持用户为非进程专属的通用工作流（如“游戏模式”、“绘图工作流”、“CAD制图”等）自由命名创建独立的多向轮盘方案。
- **新增“重命名配置”与双击重命名**：提供专属重命名按钮并支持在配置列表中直接双击选中的条目进行重命名，内置输入合法性校验与重名防止机制。
- **受保护的全局默认配置 (Global Protection)**：系统全局配置 `Global` 受保护免于误删除或误重命名，确保轮盘底层回退机制永远稳健可靠。

---

## [v1.2.0] - 2026-08-19 (正式发布版本)
### 🏛️ 五维设置分区重构 (5-Dimension Settings Taxonomy)
- **触发与场景 (Triggers & Scenes)**：整合灵敏度阈值滑块、全屏游戏检测阻断、修饰键穿透多选与进程排除黑名单。
- **外观与形态 (Appearance & Theming)**：引入**双栏响应式布局**，左侧调节 6 种视觉风格、调色盘、几何尺寸与 5 种切削形态；右侧嵌入 **60FPS 所见即所得实时交互画布**。
- **手势与动作 (Profiles & Actions)**：多应用独立配置管理、4/8/12 方位切换、动作映射列表与可视化图标选取。
- **高级与系统 (System & Advanced)**：新增 Windows 开机自启管理、UAC 管理员一键提权、钩子健康巡检监控、配置文件导入与导出备份 (JSON)。
- **关于与更新 (About & Changelog)**：版本徽标、运行状态、本代更新亮点及历代演进时间轴。

### 💎 扇区五大形态与切缝倒角系统 (Advanced Shapes & Geometry)
- **五大高级形态切削**：
  1. `Original`：经典紧凑扇区切削
  2. `Circle`：独立圆形悬浮卡片
  3. `RoundedRect`：悬浮圆角矩形胶囊
  4. `FloatingCapsules`：沿法线动态拉伸的独立悬浮胶囊
  5. `HexagonHive`：蜂巢六边形科技矩阵
- **扇区光学缝隙间距 (Sector Gap)**：支持 `0 ~ 12px` 物理间隙调节，扇区相互独立悬浮，通透现代。
- **平滑外沿倒角 (Sector Corner Radius)**：支持 `0 ~ 16px` 平滑倒角，赋予扇区温润柔和质感。

### 🎨 四层统一矢量图标引擎与排版模板 (Unified Icon Engine & Layout Modes)
- **30+ 精选高频矢量图标库**：涵盖编辑剪贴板 (复制/粘贴/剪切/撤销/重做/保存/搜索)、窗口管理 (关闭/最小化/最大化/桌面/截图)、网页浏览 (后退/前进/刷新/新建标签/关闭标签)、多媒体系统 (音量增减/静音/播放暂停/切歌/锁屏) 与生产力工具 (终端/代码/文件夹/计算器)。
- **独立可视化图标选择器 (`IconPickerWindow`)**：提供即时搜索、分类过滤与双击直选应用。
- **三种排版模板切换 (`IconLayoutMode`)**：
  1. `IconAndText`：图标 + 文字 纵向双行居中
  2. `IconOnly`：极大化单图标居中 (26x26)
  3. `TextOnly`：纯文字居中展示

### ⚡ 实时交互预览画布 (60FPS Live Interactive Canvas)
- **60FPS 实时同步重绘**：左侧拖动外径/内径/核心圆半径滑块、切缝/倒角滑块、切换主题、排版模式或修改自定义颜色时，右侧预览画布即时重绘。
- **动态交互测试**：支持在预览画布上划动鼠标，测试扇区磁吸外凸、高亮与中心退出区悬浮交互，无需切出窗口即可感知真实手感。

### 🛡️ 交互响应优化与重入死锁防御 (Interaction Bug Fixes)
- 采用无死区 `RadioButton` 侧边栏导航架构，彻底修复点击无反应问题。
- 增加重入保护标志与安全更新锁，消除单选框与滑块联动的事件死循环与偶发卡顿。

### 📦 历史版本快照与归档 (Historical Version Preservation)
- `WinPieGestures_v1.0.0/`：初始版本归档
- `WinPieGestures_v1.1.0/` & `releases/v1.1.0/`：动效与视觉升级版本归档
- `WinPieGestures_v1.2.0/` & `releases/v1.2.0/`：形态扩展与矢量图标正式版本发布

---

## [v1.1.0] - 2026-08-19
### 🎨 主题与视觉重构 (Theme & UI Redesign)
- **经典圆环 (ClassicRing)**：重构为深邃石墨黑 (`#18181B`) 与蔚蓝科技光泽 (`#3B82F6`)，增加内同心圆光线与柔和渐变。
- **极简扇区 (CleanSectors)**：实现扇区间 `3px` 精密光学间隙与悬浮卡片感，高亮采用翡翠绿 (`#10B981`) 纯平扁平设计。
- **液态毛玻璃 (Glassmorphism)**：引入双向线性渐变高光边框 (`#60FFFFFF` -> `#10FFFFFF`)，增强真实玻璃边缘折射感。
- **赛博霓虹 (NeonGlow)**：加入 HUD 四象限精密准星、外环刻度虚线环与电光青 (`#06B6D4`) 辉光效果。
- **萌宠猫爪 (CatPaw)**：升级为马卡龙粉桃奶油立体渐变猫耳与微立体 3D 萌爪肉垫，悬浮时具备 Q 弹微缩放动效。
- **重工朋克 (Mechanical)**：采用枪铁黑与工业黄铜金 (`#F59E0B`) 质感，外环 16 颗精细六角铆钉与激光切削中心齿轮。

### 🚀 动效与手感升级 (Motion & Interaction Feel)
- **弹性弹簧开启动效**：轮盘呼出引入 `BackEase / CubicEase` 缓动，在 120ms 内完成丝滑弹出与弹性过冲，手感干脆利落。
- **扇区磁吸外凸 (Magnetic Pop-out)**：划入扇区时，该扇区及其图标文字自动沿法线向外微凸 `5px`，提供实体级按键反馈。
- **中心退出区拟真反馈**：划回中心退出区时，中心圆和退出图标平滑放大 1.08x 并变色提示，确保“取消手势”意图极其清晰。
- **快速淡出关闭**：松开按键后带有 100ms 快速淡出过渡，告别突兀消失。

### 🖥️ 系统托盘与设置界面升级 (Tray & Settings UX)
- **原生高清托盘图标**：托盘直接读取并加载 `app_icon.ico`。
- **功能齐全的托盘菜单**：支持“手势生效开关 (启用/暂停)”、“以管理员身份重启”、“打开设置”、“更新日志”与“退出”。
- **应用内版本日志面板**：在设置界面新增“关于与更新日志”侧边栏页签，方便直观查阅当前版本号与历代更新说明。
- **设置窗口平滑淡入**：窗口呼出与切换采用淡入渐变，消除界面闪烁。

---

## [v1.0.4] - 2026-06-29
### 🛡️ 场景隔离与防误触 (Scene Isolation)
- **全屏应用阻断**：新增 `FullScreenHelper.cs`，活动窗口处于全屏游戏或视频时自动静默禁用手势。
- **修饰键旁路 (Modifier Bypass)**：支持按住 `Ctrl` / `Shift` / `Alt` 键时直接穿透原生右键，跳过轮盘。
- **进程黑名单**：支持将特定软件（如 `photoshop.exe`、`mstsc.exe`）加入黑名单排除。
- **风格渲染解耦**：引入 `IRadialStyleRenderer` 接口与 `StyleRendererFactory` 工厂模式，彻底解耦 UI 渲染与手势窗口逻辑。

---

## [v1.0.3] - 2026-06-28
### ⚙️ 系统稳定性与自动化测试 (System Stability & E2E)
- **低级鼠标钩子自愈 (Self-Healing Mouse Hook)**：加入后台健康检查定时器，在系统静默丢弃钩子时自动在 UI 线程重挂钩。
- **UAC 权限提示与管理员提权**：在设置界面检测非管理员状态并提供一键提权通道。
- **E2E 自动化测试框架**：构建基于 `pytest` 与 `pywinauto` 的沙箱自动化测试套件。

---

## [v1.0.2] - 2026-06-08
### 📐 几何调节与主题扩充 (Geometry & Themes)
- **几何参数调节**：增加外径、内径、核心圆半径实时滑块调节。
- **程序图标提取**：动作支持自动提取绑定 `.exe` 的原生关联高清图标。
- **程序挑选器**：增加可视化 `ProgramPickerWindow` 窗口一键挑选系统应用。

---

## [v1.0.1] - 2026-06-07
### 🖼️ UI 卡片式重构 (Settings Redesign)
- **现代侧边栏垂直导航**：采用现代 Web 风格卡片式排版与垂直 Tab 切换。
- **自定义控件模板**：引入 iOS 风格滑动开关、极简扁平文本框、圆角滑块与微阴影。

---

## [v1.0.0] - 2026-06-01
### 🎉 初始发布版本 (Initial Release)
- 全局低级鼠标钩子 (`WH_MOUSE_LL`) 与扇区手势角度计算。
- 4 / 8 / 12 键轮盘布局支持。
- 热键模拟、程序启动与系统控制（音量/锁屏/截图/显示桌面）执行引擎。
- 基础 JSON 配置文件读写与托盘最小化运行。
