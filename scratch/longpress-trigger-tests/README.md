# 左键长按及零延时回归

必须显式传入候选目录；编译只引用已构建宿主，不重建主程序。验证器的四个参数依次为候选 `StarPie.dll` 绝对路径、预期 SHA-256、对应源码 `SettingsWindow.xaml`、隔离数据目录。

```powershell
dotnet build .\scratch\longpress-trigger-tests\LongPressTriggerTests.csproj -c Release "-p:StarPieCandidateDir=<本轮候选目录>" --artifacts-path <独立验证器产物目录>
dotnet <验证器产物目录>\bin\LongPressTriggerTests\release\test_process_launch.dll <候选DLL> <SHA256> <SettingsWindow.xaml> <隔离数据目录>
```

覆盖有效值/非法值、旧配置与0值往返、生产导入导出、无Window的滑块及设置回填、等待阶段左键移动、其他鼠标与键盘位移、0/非零计时、普通/定格、快速抬起、取消、重复/过期回调、初始层、呈现前选区保持、计时中心发布及取消后的抬起配对。真实计时器仅用于无活动输入的1ms探针，其余使用可控假计时器。所有动作与鼠标回放均记录到假服务；不安装钩子，不创建产品窗口，不发送真实输入。

2006原基线为13通过/23失败，其中包含缺少新增测试切缝；补入切缝且不修复触发行为的2128候选为30通过/6失败，证明滑块和位移/延时问题。独立审查后补充的8项在2132失败；计时中心补充4项在2147失败。最终2153为59通过/0失败。源码XML读取只证明结构，真实设置刷新通过实际候选方法与无Window控件验证；视觉、系统输入和手感仍待实机。
