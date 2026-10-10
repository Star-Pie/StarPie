# SDK 1.11 插件设置回归

来源为已有 `desktop-pet-controls` 工作树中的专用测试，移植到当前主分支；保留所有动作执行禁止断言。测试入口要求 `--test-instance`，所有数据目录独立，不显示窗口。

```powershell
dotnet run --project scratch/plugin-controls-tests/PluginControlsTests.csproj -c Release -- --test-instance
```

默认夹具 `ControlFixture` 的 `ExecuteAsync` 直接抛错，测试只检查声明和动作封装，不点击真实命令按钮。`SelfTestFixture.csproj` 仅供 `--plugin-selftest --skip-invoke`，不得省略该参数。
