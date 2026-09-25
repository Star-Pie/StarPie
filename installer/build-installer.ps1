# StarPie Inno Setup Local Build Script
[CmdletBinding()]
param (
    [string]$Version = "",
    [string]$SourceDir = "",
    [string]$OutputDir = "",
    [switch]$BuildStandaloneFirst
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = (Resolve-Path "$ScriptDir\..").Path

# 1. Detect ISCC.exe
$isccPath = $null

$lnkPath = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Inno Setup 6\Inno Setup Compiler.lnk"
if (Test-Path $lnkPath) {
    try {
        $sh = New-Object -ComObject WScript.Shell
        $lnk = $sh.CreateShortcut($lnkPath)
        if ($lnk.TargetPath) {
            $candidate = Join-Path (Split-Path $lnk.TargetPath) "ISCC.exe"
            if (Test-Path $candidate) {
                $isccPath = $candidate
            }
        }
    } catch {
    }
}

if (-not $isccPath) {
    $pf86 = ${env:ProgramFiles(x86)}
    $pf = $env:ProgramFiles
    $localApp = $env:LOCALAPPDATA

    $searchPaths = @(
        "H:\Inno Setup 6\ISCC.exe",
        "$pf86\Inno Setup 6\ISCC.exe",
        "$pf\Inno Setup 6\ISCC.exe",
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe",
        "$localApp\Programs\Inno Setup 6\ISCC.exe"
    )

    foreach ($p in $searchPaths) {
        if ($p -and (Test-Path $p)) {
            $isccPath = $p
            break
        }
    }
}

if (-not $isccPath) {
    $cmd = Get-Command "iscc" -ErrorAction SilentlyContinue
    if ($cmd) {
        $isccPath = $cmd.Source
    }
}

if (-not $isccPath) {
    Write-Error "Cannot find Inno Setup compiler (ISCC.exe)! Please install Inno Setup 6."
    exit 1
}

Write-Host "[OK] Found Inno Setup compiler: $isccPath" -ForegroundColor Green

# 2. Version resolution
if ([string]::IsNullOrWhiteSpace($Version)) {
    $csprojContent = Get-Content "$RepoRoot\WinPieGestures\WinPieGestures.csproj" -Raw -Encoding utf8
    if ($csprojContent -match '<Version>(?<v>[^<]+)</Version>') {
        $Version = $Matches['v'].Trim()
    } else {
        $Version = "1.8.0-beta.3"
    }
}

$cleanVersion = $Version.TrimStart('v', 'V')
Write-Host "[*] Target Version: $cleanVersion" -ForegroundColor Cyan

# 3. Source directory resolution
if ([string]::IsNullOrWhiteSpace($SourceDir)) {
    $localReleaseDir = "$RepoRoot\releases\v$cleanVersion\Standalone"
    if (Test-Path "$localReleaseDir\StarPie.exe") {
        $SourceDir = $localReleaseDir
    } else {
        $SourceDir = "$RepoRoot\publish\Standalone"
    }
}

if ($BuildStandaloneFirst -or -not (Test-Path "$SourceDir\StarPie.exe")) {
    Write-Host "[*] Publishing Standalone binary via dotnet publish..." -ForegroundColor Yellow
    dotnet publish "$RepoRoot\WinPieGestures\WinPieGestures.csproj" `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -o "$SourceDir"
}

if (-not (Test-Path "$SourceDir\StarPie.exe")) {
    Write-Error "StarPie.exe not found in source directory: $SourceDir"
    exit 1
}

Write-Host "[OK] Source directory: $SourceDir" -ForegroundColor Green

# 4. Output directory
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = "$RepoRoot\dist"
}
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
}
$OutputDir = [System.IO.Path]::GetFullPath($OutputDir)
if (-not [string]::IsNullOrWhiteSpace($SourceDir)) {
    $SourceDir = [System.IO.Path]::GetFullPath($SourceDir)
}

$outputBaseName = "StarPie-v$cleanVersion-Setup-win-x64"
$numericVersion = ($cleanVersion -replace '-.*$', '')
$versionParts = @($numericVersion.Split('.'))
while ($versionParts.Count -lt 4) {
    $versionParts += "0"
}
$quadVersion = ($versionParts[0..3] -join '.')

Write-Host "[*] Compiling installer: $OutputDir\$outputBaseName.exe..." -ForegroundColor Cyan

# 5. Run ISCC
$issPath = "$ScriptDir\StarPie.iss"
& $isccPath `
    "/DMyAppVersion=$cleanVersion" `
    "/DMyAppNumericVersion=$quadVersion" `
    "/DSourceDir=$SourceDir" `
    "/DOutputDir=$OutputDir" `
    "/DOutputBaseFilename=$outputBaseName" `
    "$issPath"

if ($LASTEXITCODE -ne 0) {
    Write-Error "ISCC compilation failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

$finalExe = "$OutputDir\$outputBaseName.exe"
if (Test-Path $finalExe) {
    $sizeMb = [Math]::Round((Get-Item $finalExe).Length / 1MB, 2)
    Write-Host "[OK] Installer built successfully!" -ForegroundColor Green
    Write-Host "     Path: $finalExe" -ForegroundColor Green
    Write-Host "     Size: $sizeMb MB" -ForegroundColor Green
} else {
    Write-Error "Expected installer file was not found: $finalExe"
    exit 1
}
