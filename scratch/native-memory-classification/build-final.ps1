$ErrorActionPreference = 'Stop'
$repo = 'G:\Users\2 Better\.codex\worktrees\profile-trigger-wheels\design'
$report = 'G:\Users\2 Better\Documents\Codex\2026-10-30\starpie-issue-review\native-memory-classification'
Set-Location -LiteralPath $repo
$artifacts = Join-Path $repo 'scratch\native-memory-classification\artifacts'
$stamp = [TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([DateTime]::UtcNow,'China Standard Time').ToString('yyyyMMdd-HHmm')
$candidate = Join-Path $repo ('WinPieGestures\bin\Release\build-out-' + $stamp)
if (Test-Path -LiteralPath $candidate) { throw 'Candidate exists; do not overwrite' }
New-Item -ItemType Directory -Path $candidate | Out-Null
dotnet build .\WinPieGestures\WinPieGestures.csproj -c Release --nologo "-p:OutDir=$candidate\\" > (Join-Path $report 'final-release-build.log') 2>&1
if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath (Join-Path $report 'final-release-build.log') -Tail 20; throw 'Final Release build failed' }
Get-Content -LiteralPath (Join-Path $report 'final-release-build.log') -Tail 5
function Build-Probe([string]$project, [string]$output, [string]$label) {
    dotnet build $project -c Release --nologo "-p:StarPieCandidateDir=$candidate" "-p:ArtifactsRoot=$artifacts" "-p:SourceRoot=$repo" "-p:OutDir=$output\\" -p:NuGetAudit=false > (Join-Path $report ($label+'-final-build.log')) 2>&1
    if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath (Join-Path $report ($label+'-final-build.log')) -Tail 15; throw ($label+' build failed') }
}
$lazyOut = Join-Path $artifacts 'lazy-final'
Build-Probe '.\scratch\native-memory-classification\LazyStudioProbe.csproj' $lazyOut 'lazy'
dotnet (Join-Path $lazyOut 'test_process_launch.dll') (Join-Path $artifacts 'lazy-final-case') --test-instance > (Join-Path $report 'lazy-final-run.log') 2>&1
if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath (Join-Path $report 'lazy-final-run.log'); throw 'Final lazy checks failed' }
Get-Content -LiteralPath (Join-Path $report 'lazy-final-run.log') -Tail 3
$fontOut = Join-Path $artifacts 'font-final'
Build-Probe '.\scratch\settings-font-memory-tests\FontMemoryTests.csproj' $fontOut 'font'
dotnet (Join-Path $fontOut 'test_process_launch.dll') (Join-Path $artifacts 'font-final-case') --test-instance --expect-deferred > (Join-Path $report 'font-final-run.log') 2>&1
if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath (Join-Path $report 'font-final-run.log') -Tail 20; throw 'Final font checks failed' }
Get-Content -LiteralPath (Join-Path $report 'font-final-run.log') -Tail 1
$triggerOut = Join-Path $artifacts 'trigger-final'
Build-Probe '.\scratch\profile-trigger-tests\ProfileTriggerTests.csproj' $triggerOut 'trigger'
dotnet (Join-Path $triggerOut 'test_process_launch.dll') $repo > (Join-Path $report 'trigger-final-run.log') 2>&1
if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath (Join-Path $report 'trigger-final-run.log') -Tail 20; throw 'Final trigger checks failed' }
Get-Content -LiteralPath (Join-Path $report 'trigger-final-run.log') -Tail 1
$launchOut = Join-Path $artifacts 'launch-final'
Build-Probe '.\scratch\process-launch-tests\test_process_launch.csproj' $launchOut 'launch'
dotnet (Join-Path $launchOut 'test_process_launch.dll') > (Join-Path $report 'launch-final-run.log') 2>&1
if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath (Join-Path $report 'launch-final-run.log') -Tail 20; throw 'Final launch checks failed' }
Get-Content -LiteralPath (Join-Path $report 'launch-final-run.log') -Tail 2
git diff --check
if ($LASTEXITCODE -ne 0) { throw 'Diff hygiene failed' }
$files = @('WinPieGestures/SettingsWindow.xaml','WinPieGestures/SettingsWindow.ArtStyles.cs','scratch/test_art_styles.cs','scratch/native-memory-classification/LazyStudioProbe.cs')
$sourceHashes = [ordered]@{}
foreach ($file in $files) { $sourceHashes[$file] = (Get-FileHash -LiteralPath (Join-Path $repo $file) -Algorithm SHA256).Hash }
$snapshot = [ordered]@{
    candidateDirectory=$candidate; sourceRoot=$repo
    hostSha256=(Get-FileHash -LiteralPath (Join-Path $candidate 'StarPie.dll') -Algorithm SHA256).Hash
    exeSha256=(Get-FileHash -LiteralPath (Join-Path $candidate 'StarPie.exe') -Algorithm SHA256).Hash
    sdkSha256=(Get-FileHash -LiteralPath (Join-Path $candidate 'StarPie.Plugin.Abstractions.dll') -Algorithm SHA256).Hash
    greenProbeSha256=(Get-FileHash -LiteralPath (Join-Path $lazyOut 'test_process_launch.dll') -Algorithm SHA256).Hash
    sourceSha256=$sourceHashes; lazyProbeDirectory=$lazyOut
    checks=@{lazy=13;font=48;profile=172;launch=382}
    mainExecUsed=25; firstNativeUsed=3; remainingTotal=2; finalReviewMaxExec=1; finalArchiveExec=1
}
$snapshot | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $report 'final-candidate.json') -Encoding UTF8
$snapshot | ConvertTo-Json -Depth 6
