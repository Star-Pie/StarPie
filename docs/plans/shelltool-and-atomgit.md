# 动作面板工具选择与AtomGit默认源

基线为profile-trigger-wheels/design、codex/restart-update-reliability、HEAD773e8c8加1230在研改动；既有脏文件／14份诊断保留，1230不覆盖。用户反馈ShellTool仍是功能ID输入框，需同轮盘摘要及挑选器。新增要求是AtomGit作为软件更新、首次核心插件及后续官方插件下载的默认首选。

用户随后确认AtomGit目前只有主仓库，官方插件仓库未建立。公开插件镜像API查询返回404；尚不能宣称镜像下载可用。已提供URL导入及Pull镜像官方步骤；Release不随Git导入，需要另行附件同步。客户端镜像功能先使用隔离HTTP夹具验证，外部仓库建立、首次附件同步和真实下载为门禁，不替用户创建仓库、配置密钥或公开发布。

2026-11-04补充：用户已导入https://gitcode.com/Star-Pie/StarPie-Official-Plugins；匿名API当前返回200及空Release列表。插件工作树已准备镜像工作流、校验脚本与docs/atomgit-mirror.md。首次需补齐最新目录引用的7个原标签，不只复制最新包；工作流和实际远程下载均尚未执行。

允许宿主：ActionPanelActionEditor、必要I18n、AppConfig、UpdateManager/Sources默认值、OfficialPluginDownloadOptions/Client及独立AtomGit适配器、首次引导与插件下载源UI／持久化、对应安全测试与文档。允许插件工作树：.github/workflows的镜像流程及必要复用脚本／离线测试、镜像设置说明；禁止SDK、插件动作源／小键盘源、运行中实例、用户数据、提交／推送／远程发布。共享UI由主代理独占。

兼容：显式软件更新源保留；新配置／缺失值默认AtomGit。新增独立官方插件下载源字段，避免插件选择误改GitHub代理；旧代理字段不删除。保留所有原下载通道、官方catalog与包的SHA256／大小／兼容性／权限校验，镜像只改变传输，原catalog中的官方GitHub来源及资产身份不改写。选择AtomGit后不静默回落GitHub；缺附件、目录不完整或网络错误明确失败，用户可选其他通道。首次引导确认前零网络仍为硬门禁。

主代理负责ShellTool UI、客户端源边界／整合。DeepSeek WorkBuddy/MAX完成固定只读探针后，允许仅写独立纯Release数据解析器、测试与专用证据目录，不写共享UI或联网；最终由独立原生GPT复核。最多两工作者，无递归，不设自造配额。

验证：1230基线缺picker失败、新候选通过；工具取消／ID或Verb回填／元数据保留／权限表单／快照；新旧配置及显式源往返；AtomGit发现、目录及按tag+文件名解析下载、无GitHub请求、重定向白名单、SHA失败／部分镜像／取消／超时；引导确认前零网络；新时间戳Release、候选哈希、离屏四语主题、现有输入与下载回归，独立审查。真实工具挑选／动作、镜像建立与附件同步／网络下载均需人工门禁，不把HTTP假夹具提升为实机通过。

镜像完整性补充：catalog最后上传；客户端发现只接受同步器写入的合法原发布时间标记，按源时间排序后取8个候选，拒绝当前tag缺包的目录且不覆盖缓存。最多读取100条元数据／2MiB，其他历史tag的完整性需首次补齐及实际下载验收。SDK和小键盘DLL本轮不变。DeepSeek首次写入用了错误H盘路径，已回收实际diff并将错误路径的3个新增文件移至证据目录quarantine，写入资格暂停，本轮后续由主代理实施、独立原生GPT复核；不能将材料资格或只读探针升级为整体写入资格。
