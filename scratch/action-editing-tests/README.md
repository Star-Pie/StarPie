# 二级动作编辑无界面回归

```powershell
dotnet run --project .\scratch\action-editing-tests\ActionEditingTests.csproj -c Release
```

25 个场景包含模型/插件快照、复制粘贴、继承、历史、真实选择刷新以及列表插入和取消。测试在 STA 线程运行，启动时将 LOCALAPPDATA 重定向至输出目录的 `isolated-data`，不会写用户配置。

`FocusEditingUiTests` 用 FrameworkElement 的基础状态承载未调用构造器的 SettingsWindow，并创建普通 WPF 控件。实际运行选择和渲染方法，检查生成行、路径、数量与名称，避免仅验证源码字符串或仅检查初始对象。没有构造 Window/App，没有 Show、输入注入、钩子或真实动作调用；键盘事件使用无窗口 PresentationSource。

已证明修改前失败的反例：切换主扇区保留旧列表、最后插入间隙无法表示、跨扇区旧撤销覆盖同名重建方案。当前候选通过这些检查；捕获设备、真实鼠标拖动、视觉/DPI/主题仍需用户实机验收。

最新候选、红绿证据与人工清单见 [验收记录](../../docs/cascade-action-editor-verification.md)。
