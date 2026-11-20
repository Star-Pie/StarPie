# StarPie v1.8.0-beta.4 本地发布版生成与同步脚本
[CmdletBinding()]
param (
    [string]$Version = "1.8.0-beta.4",
    [string]$TargetDir = "",
    [switch]$Clean
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptDir "..")).Path
. (Join-Path $repoRoot 'installer/Assert-CleanDistributionRoot.ps1')
$cleanVersion = $Version.TrimStart('v', 'V')
$tag = "v$cleanVersion"

if ([string]::IsNullOrWhiteSpace($TargetDir)) {
    $TargetDir = Join-Path $repoRoot "releases\$tag"
}

Write-Host "====================================================" -ForegroundColor Cyan
Write-Host "StarPie 本地发布版同步生成器" -ForegroundColor Cyan
Write-Host "目标版本: $tag ($cleanVersion)" -ForegroundColor Cyan
Write-Host "源码仓库: $repoRoot"
Write-Host "目标目录: $TargetDir"
Write-Host "===================================================="

# 1. 验证目标版本号一致性
Write-Host "`n[1/7] 验证版本元数据一致性..." -ForegroundColor DarkCyan
$csprojPath = Join-Path $repoRoot "WinPieGestures\WinPieGestures.csproj"
[xml]$csprojXml = Get-Content $csprojPath -Raw -Encoding utf8
$csprojVersion = [string]$csprojXml.Project.PropertyGroup.Version
$assemblyVersion = [string]$csprojXml.Project.PropertyGroup.AssemblyVersion
$fileVersion = [string]$csprojXml.Project.PropertyGroup.FileVersion

if ($csprojVersion -ne $cleanVersion) {
    throw "WinPieGestures.csproj Version ($csprojVersion) 与目标版本 ($cleanVersion) 不匹配！"
}
Write-Host " [PASS] csproj 版本已就绪: Version=$csprojVersion, AssemblyVersion=$assemblyVersion, FileVersion=$fileVersion" -ForegroundColor Green

# 2. 准备输出目录
Write-Host "`n[2/7] 准备目标目录结构..." -ForegroundColor DarkCyan
# These output directories can also have been launched as a portable installation.
foreach ($dir in @($TargetDir, "$TargetDir/Lightweight", "$TargetDir/Standalone", "$TargetDir/StarPie-$tag-Lightweight-win-x64")) {
    Assert-CleanDistributionRoot $dir
}
if ($Clean -and (Test-Path $TargetDir)) {
    Assert-NoPortableProfileInCleanupTree $TargetDir
    Write-Host "  清理已有目录: $TargetDir" -ForegroundColor Yellow
    Remove-Item -LiteralPath $TargetDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $TargetDir | Out-Null
Write-Host " [PASS] 目标目录已就绪: $TargetDir" -ForegroundColor Green

# 3. 构建并发布 Lightweight (依赖运行时轻量包)
Write-Host "`n[3/7] 编译并发布 Lightweight (轻量版)..." -ForegroundColor DarkCyan
$lightweightDir = Join-Path $TargetDir "Lightweight"
if (Test-Path $lightweightDir) { Remove-Item -LiteralPath $lightweightDir -Recurse -Force }

& dotnet publish "$csprojPath" -c Release -r win-x64 --no-self-contained -o "$lightweightDir"

if (-not (Test-Path (Join-Path $lightweightDir "StarPie.exe"))) {
    throw "Lightweight 发布产物中缺失 StarPie.exe！"
}
Write-Host " [PASS] Lightweight 发布成功: $lightweightDir" -ForegroundColor Green

# 4. 构建并发布 Standalone (独立自包含版)
Write-Host "`n[4/7] 编译并发布 Standalone (独立自包含版)..." -ForegroundColor DarkCyan
$standaloneDir = Join-Path $TargetDir "Standalone"
if (Test-Path $standaloneDir) { Remove-Item -LiteralPath $standaloneDir -Recurse -Force }

& dotnet publish "$csprojPath" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "$standaloneDir"

if (-not (Test-Path (Join-Path $standaloneDir "StarPie.exe"))) {
    throw "Standalone 发布产物中缺失 StarPie.exe！"
}
Write-Host " [PASS] Standalone 发布成功: $standaloneDir" -ForegroundColor Green

# 5. 同步生成已解包目录与 ZIP 压缩归档
Write-Host "`n[5/7] 打包 ZIP 压缩包与解包目录..." -ForegroundColor DarkCyan

Assert-CleanDistributionRoot $lightweightDir
Assert-CleanDistributionRoot $standaloneDir

# 5.1 生成轻量解包文件夹 (与历史结构完全对齐)
$unpackedLightweightDir = Join-Path $TargetDir "StarPie-$tag-Lightweight-win-x64"
if (Test-Path $unpackedLightweightDir) { Remove-Item -LiteralPath $unpackedLightweightDir -Recurse -Force }
Copy-Item -LiteralPath $lightweightDir -Destination $unpackedLightweightDir -Recurse -Force
Write-Host " [PASS] 生成轻量解包目录: $unpackedLightweightDir" -ForegroundColor Green

# 5.2 压缩 Lightweight ZIP
$lightweightZip = Join-Path $TargetDir "StarPie-$tag-Lightweight-win-x64.zip"
if (Test-Path $lightweightZip) { Remove-Item -LiteralPath $lightweightZip -Force }
Write-Host "  正在压缩 Lightweight 归档: $lightweightZip ..."
Compress-Archive -Path "$lightweightDir\*" -DestinationPath $lightweightZip -Force
Write-Host " [PASS] Lightweight ZIP 归档完成 (大小: $([Math]::Round((Get-Item $lightweightZip).Length / 1MB, 2)) MB)" -ForegroundColor Green

# 5.3 压缩 Standalone ZIP
$standaloneZip = Join-Path $TargetDir "StarPie-$tag-Standalone-win-x64.zip"
if (Test-Path $standaloneZip) { Remove-Item -LiteralPath $standaloneZip -Force }
Write-Host "  正在压缩 Standalone 归档: $standaloneZip ..."
Compress-Archive -Path "$standaloneDir\*" -DestinationPath $standaloneZip -Force
Write-Host " [PASS] Standalone ZIP 归档完成 (大小: $([Math]::Round((Get-Item $standaloneZip).Length / 1MB, 2)) MB)" -ForegroundColor Green

# 6. 使用 Inno Setup 编译 Windows 安装程序 (Setup.exe)
Write-Host "`n[6/7] 使用 Inno Setup 编译安装程序..." -ForegroundColor DarkCyan
$buildInstallerScript = Join-Path $repoRoot "installer\build-installer.ps1"
if (-not (Test-Path $buildInstallerScript)) {
    throw "未找到 Inno Setup 打包脚本: $buildInstallerScript"
}

& pwsh -NoProfile -ExecutionPolicy Bypass -File "$buildInstallerScript" -Version "$cleanVersion" -SourceDir "$standaloneDir" -OutputDir "$TargetDir"

$setupExe = Join-Path $TargetDir "StarPie-$tag-Setup-win-x64.exe"
if (-not (Test-Path $setupExe)) {
    throw "预期安装程序未生成: $setupExe"
}
Write-Host " [PASS] Inno Setup 安装包构建完成 (大小: $([Math]::Round((Get-Item $setupExe).Length / 1MB, 2)) MB)" -ForegroundColor Green

# 7. 提取发布说明与生成哈希清单
Write-Host "`n[7/7] 提取发布说明与生成校验哈希..." -ForegroundColor DarkCyan

# 7.1 从 CHANGELOG.md 提取 Release Notes
$changelogPath = Join-Path $repoRoot "CHANGELOG.md"
$notesFile = Join-Path $TargetDir "RELEASE_NOTES.md"
if (Test-Path $changelogPath) {
    $content = Get-Content $changelogPath -Raw -Encoding utf8
    $pattern = "(?s)##\s*\[[vV]?$([regex]::Escape($cleanVersion))\][^\r\n]*\r?\n(.*?)(?=\r?\n##\s*\[|\Z)"
    if ($content -match $pattern) {
        $notes = $Matches[1].Trim()
        [System.IO.File]::WriteAllText($notesFile, $notes, [System.Text.Encoding]::UTF8)
        Write-Host " [PASS] 成功提取发布说明: $notesFile" -ForegroundColor Green
    }
    else {
        [System.IO.File]::WriteAllText($notesFile, "StarPie $tag", [System.Text.Encoding]::UTF8)
        Write-Host " [WARN] 未精确匹配到版本段落，已生成基础说明: $notesFile" -ForegroundColor Yellow
    }
}

# 7.2 生成 SHA256SUMS.txt
$checksumFile = Join-Path $TargetDir "SHA256SUMS.txt"
$hashLines = [System.Collections.Generic.List[string]]::new()
$artifacts = Get-ChildItem -Path $TargetDir -File | Where-Object { $_.Name -like "*.zip" -or $_.Name -like "*.exe" } | Sort-Object Name
foreach ($art in $artifacts) {
    $sha = (Get-FileHash -LiteralPath $art.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $hashLines.Add("$sha  $($art.Name)")
}
[System.IO.File]::WriteAllLines($checksumFile, $hashLines, [System.Text.Encoding]::UTF8)
Write-Host " [PASS] SHA-256 校验清单已生成: $checksumFile" -ForegroundColor Green

Write-Host "`n====================================================" -ForegroundColor Green
Write-Host "StarPie $tag 发布版已成功生成至目标目录！" -ForegroundColor Green
Write-Host "===================================================="
Get-ChildItem -Path $TargetDir | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize
