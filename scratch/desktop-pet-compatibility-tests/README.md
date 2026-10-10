# 桌宠真实加载兼容探针

先设置 `STARPIE_DESKTOP_PET_DLL` 为待验收的 `StarPie.Plugin.DesktopPet.dll` 绝对路径，再运行：

```powershell
dotnet run --project scratch/desktop-pet-compatibility-tests/DesktopPetCompatibilityTests.csproj -c Release -- --test-instance
```

只复制 DLL、清单和依赖描述到临时目录。SDK 检查、实际 Load/Initialize/Commit、设置声明读取和停止/卸载都使用隔离目录；不复制用户设置或素材，不执行插件动作、不创建桌宠窗口，也不更改源插件启用状态。缺少路径参数或测试模式时退出 2，失败退出 1。
