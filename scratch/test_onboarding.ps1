param(
    [switch]$KeepSandbox
)

$ErrorActionPreference = "Stop"

$tempBase = [System.IO.Path]::GetTempPath()
$sandboxGuid = [System.Guid]::NewGuid().ToString("N")
$sandboxDir = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($tempBase, "StarPie-OnboardRunner-$sandboxGuid"))

# 验证沙箱路径合法性：必须为绝对路径且严格位于系统临时目录之下
if (-not $sandboxDir.StartsWith([System.IO.Path]::GetFullPath($tempBase), [System.StringComparison]::OrdinalIgnoreCase)) {
    Write-Error "沙箱路径不在系统临时目录中，已中止：$sandboxDir"
    exit 2
}

$localAppData = Join-Path $sandboxDir "LocalAppData"
$appData = Join-Path $sandboxDir "AppData"
$tempPath = Join-Path $sandboxDir "Temp"

New-Item -ItemType Directory -Path $localAppData -Force | Out-Null
New-Item -ItemType Directory -Path $appData -Force | Out-Null
New-Item -ItemType Directory -Path $tempPath -Force | Out-Null

# 暂存原有环境变量
$origLocalAppData = $env:LOCALAPPDATA
$origAppData = $env:APPDATA
$origTemp = $env:TEMP
$origTmp = $env:TMP

$exitCode = 1

try {
    # 注入隔离沙箱环境变量
    $env:LOCALAPPDATA = $localAppData
    $env:APPDATA = $appData
    $env:TEMP = $tempPath
    $env:TMP = $tempPath

    Write-Host "==========================================================" -ForegroundColor Cyan
    Write-Host "StarPie Official Plugins Onboarding Regression Suite" -ForegroundColor Cyan
    Write-Host "Sandbox Root: $sandboxDir" -ForegroundColor DarkGray
    Write-Host "==========================================================" -ForegroundColor Cyan

    $repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
    $projectPath = Join-Path $repoRoot "scratch\test_onboarding.csproj"

    dotnet run --project $projectPath -c Release --no-launch-profile
    $exitCode = $LASTEXITCODE

    if ($exitCode -eq 0) {
        Write-Host "`nAll onboarding tests completed successfully with exit code 0." -ForegroundColor Green
    } else {
        Write-Host "`nOnboarding tests failed with exit code $exitCode." -ForegroundColor Red
    }
}
finally {
    # 恢复环境变量
    $env:LOCALAPPDATA = $origLocalAppData
    $env:APPDATA = $origAppData
    $env:TEMP = $origTemp
    $env:TMP = $origTmp

    # 关闭编译器后台守护，释放任何可能被短暂锁定的临时生成器 DLL
    try { dotnet build-server shutdown | Out-Null } catch {}

    # 清理沙箱
    if (-not $KeepSandbox) {
        try {
            if ([System.IO.Directory]::Exists($sandboxDir)) {
                # 严格边界校验
                $fullCheck = [System.IO.Path]::GetFullPath($sandboxDir)
                if ($fullCheck.StartsWith([System.IO.Path]::GetFullPath($tempBase), [System.StringComparison]::OrdinalIgnoreCase) -and
                    $fullCheck.Length -gt $tempBase.Length) {
                    [System.IO.Directory]::Delete($sandboxDir, $true)
                }
            }
        }
        catch {
            Write-Warning "清理测试沙箱目录失败：$($_.Exception.Message)"
        }
    } else {
        Write-Host "沙箱已保留：$sandboxDir" -ForegroundColor Yellow
    }
}

exit $exitCode
