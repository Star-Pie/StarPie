# beta.7 音效调查探针（无声、非 GUI）

这是**故障复现探针，不是已经全绿的回归测试**。当前生产逻辑预期退出码为 **1**，且只有一个失败断言：静音悬停候选不应重复取出同一空资源。修复生产逻辑后，该断言应转为通过，退出码应为0；不要改弱断言来得到绿色结果。

探针使用真实 SoundEffectManager、配置对象和既有内部测试切缝；所有后端调用由 PlaybackSink 接管，TestMode 始终为 true，不播放声音、不创建 GUI、不触碰用户配置/注册表。LOCALAPPDATA 在访问 ConfigManager 前指向随机临时目录，临时目录保留供查看，不自动删除。finally 释放闸门、停止并等待探针创建的工作线程。

## 运行

从仓库根目录执行。使用隔离输出，避免覆盖正在运行的默认 Release 文件：

```powershell
$out = Join-Path $PWD 'scratch\beta7-sound-probe\bin\'
dotnet build .\scratch\beta7-sound-probe\Beta7SoundProbe.csproj -c Release --nologo "-p:BaseOutputPath=$out"
dotnet (Join-Path $out 'Release\net8.0-windows10.0.19041.0\test_sound_forensics.dll')
```

AssemblyName 复用既有测试友元身份，没有新增主程序 InternalsVisibleTo 或公共 SDK。输出目录必须与旧音效取证套件分开，不能把两个同名测试程序集写到同一目录。

## 覆盖与结果

- 验证当前候选实际运行时显示版本为1.8.0-beta.7，程序集/文件数值版本保持1.8.0.0。
- 可播放音色对照：目标就绪后只取出一次，之后消费/标记完成。
- 静音音色反例：目标就绪后缓存长度为0，固定模拟时间1045ms，闸门证实同一候选至少被连续取出两次；预期健康行为断言为红。
- 阻塞后端反例：前一播放占用唯一工作线程，模拟时钟推进到目标报告后196ms，后续悬停按现有45+150ms期限被丢弃；这是已有策略，不当作新缺陷或真实硬件故障。
- 2026-10-10 连续三次运行：每次仅上述静音断言失败、退出码1；对照、版本与阻塞后端策略断言全部通过。

没有动态执行自引用 Custom 预设的栈溢出反例，也没有验证真实 WinMM、设备切换、扬声器、长期运行或用户现场的“失灵”。详情见 ../beta7-sound-investigation-2026-10-10.md。
