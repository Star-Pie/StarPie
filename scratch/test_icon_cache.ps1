param(
    [switch]$KeepSandbox
)

$ErrorActionPreference = "Stop"

$tempBase = [System.IO.Path]::GetTempPath()
$sandboxGuid = [System.Guid]::NewGuid().ToString("N")
$sandboxDir = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($tempBase, "StarPie-IconRunner-$sandboxGuid"))

if (-not $sandboxDir.StartsWith([System.IO.Path]::GetFullPath($tempBase), [System.StringComparison]::OrdinalIgnoreCase)) {
    Write-Error "Sandbox path is not within temp directory, aborting: $sandboxDir"
    exit 2
}

$localAppData = Join-Path $sandboxDir "LocalAppData"
$appData = Join-Path $sandboxDir "AppData"
$tempPath = Join-Path $sandboxDir "Temp"

New-Item -ItemType Directory -Path $localAppData -Force | Out-Null
New-Item -ItemType Directory -Path $appData -Force | Out-Null
New-Item -ItemType Directory -Path $tempPath -Force | Out-Null

$origLocalAppData = $env:LOCALAPPDATA
$origAppData = $env:APPDATA
$origTemp = $env:TEMP
$origTmp = $env:TMP

$exitCode = 1

try {
    $env:LOCALAPPDATA = $localAppData
    $env:APPDATA = $appData
    $env:TEMP = $tempPath
    $env:TMP = $tempPath

    Write-Host "==========================================================" -ForegroundColor Cyan
    Write-Host "StarPie PR #142 Icon Cache & Fallback Automated Regression" -ForegroundColor Cyan
    Write-Host "Sandbox Root: $sandboxDir" -ForegroundColor DarkGray
    Write-Host "==========================================================" -ForegroundColor Cyan

    $repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
    $projectPath = Join-Path $repoRoot "scratch\test_icon_cache.csproj"

    dotnet run --project $projectPath -c Release --no-launch-profile
    $exitCode = $LASTEXITCODE

    if ($exitCode -eq 0) {
        Write-Host "`nAll icon cache regression tests PASSED with exit code 0." -ForegroundColor Green
    } else {
        Write-Host "`nIcon cache regression tests FAILED with exit code $exitCode." -ForegroundColor Red
    }
}
finally {
    $env:LOCALAPPDATA = $origLocalAppData
    $env:APPDATA = $origAppData
    $env:TEMP = $origTemp
    $env:TMP = $origTmp

    try { dotnet build-server shutdown | Out-Null } catch {}

    if (-not $KeepSandbox) {
        try {
            if ([System.IO.Directory]::Exists($sandboxDir)) {
                $fullCheck = [System.IO.Path]::GetFullPath($sandboxDir)
                if ($fullCheck.StartsWith([System.IO.Path]::GetFullPath($tempBase), [System.StringComparison]::OrdinalIgnoreCase) -and
                    $fullCheck.Length -gt $tempBase.Length) {
                    [System.IO.Directory]::Delete($sandboxDir, $true)
                }
            }
        }
        catch {
            Write-Warning "Clean sandbox directory failed: $($_.Exception.Message)"
        }
    } else {
        Write-Host "Sandbox preserved at: $sandboxDir" -ForegroundColor Yellow
    }
}

exit $exitCode
