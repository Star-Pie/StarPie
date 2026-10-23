# 插件离线导入护栏

运行 `dotnet run --project scratch/plugin-offline-tests/PluginOfflineTests.csproj -c Release`。
可追加一个已发布 Folder `.spkg` 的绝对路径，验证无缓存时匹配内置目录；文件仅静态扫描，不安装或执行。

测试将 LOCALAPPDATA、插件宿主区和扫描区重定向至临时目录。使用 SDK-only 夹具生成包，离线阶段所有 HTTP 请求均抛错并计数；不构造窗口、不发送输入、不调用插件动作。

覆盖可信目录/内置回退、忽略自带目录、完整包 SHA-256、PE/manifest、确认前零安装、默认禁用、更新保留禁用和预加载、防降级、非法路径/重复项/条目数/解压上限、失败与取消下载保留原文件、四语文案及已安装插件导出入口。

`--ui-resources-only` 在独立 STA 线程加载生产 SettingsWindow 的真实资源字典和下载通道 ComboBox 片段，实例化控件、应用模板并执行布局；不构造 Window/App，不显示窗口或弹出菜单。它在缺失 `ModernComboBoxStyle` 的候选上失败，改用已有 `FlatComboBoxStyle` 后通过；不是仅检查源码字符串或键名存在。

`--hash-guard-only` 是独立变异探针：有效 ZIP、匹配大小/身份、故意不匹配的可信 SHA-256 必须被拒绝。临时移除生产 hash 比较时该探针失败，恢复后通过。旧 DLL 扫描入口仍保留反例：直接选择 `.spkg` 无法识别，证明统一导入分流的必要性。

本套件不替代首次引导点击、实际文件选择、主题/DPI、关闭设置后下载取消与真实启用的人工验收。
