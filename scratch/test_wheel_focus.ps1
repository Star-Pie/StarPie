# StarPie 鼠标呼出轮盘 · 前台焦点对齐端到端验证（scratch 一次性工具）
# 用法： powershell -ExecutionPolicy Bypass -File scratch\test_wheel_focus.ps1
#
# 会短暂改动前台窗口并弹出几个临时窗口，跑完自动清理并恢复运行前的那个前台窗口。
#
# -m:1 是刻意的：本仓库 scratch 下的测试工程引用 WinPieGestures 时，多节点并行 MSBuild
# 在受限环境里会于 _GetProjectReferenceTargetFrameworkProperties 静默失败（0 错误但生成失败）。
# 单节点构建与运行结果完全一致，只是慢一点。

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'test_wheel_focus.csproj'
$exe = Join-Path $PSScriptRoot 'bin\test_wheel_focus\Release\net8.0-windows10.0.19041.0\test_wheel_focus.exe'

Write-Host "==> dotnet build $project -c Release -m:1" -ForegroundColor Cyan
dotnet build $project -c Release -m:1 -v m
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "==> $exe" -ForegroundColor Cyan
& $exe
exit $LASTEXITCODE
