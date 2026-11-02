#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CandidateDirectory,
    [Parameter(Mandatory)][string]$ArtifactsRoot,
    [Parameter(Mandatory)][string[]]$SyntheticImagePath
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$candidate = (Resolve-Path -LiteralPath $CandidateDirectory).Path
$artifacts = [IO.Path]::GetFullPath($ArtifactsRoot)
if (Test-Path -LiteralPath $artifacts) { throw 'ArtifactsRoot must be a new owned directory; existing evidence is never overwritten.' }
foreach ($file in 'StarPie.dll', 'StarPie.exe', 'StarPie.Plugin.Abstractions.dll') {
    if (!(Test-Path -LiteralPath (Join-Path $candidate $file) -PathType Leaf)) { throw "Missing candidate file: $file" }
}
$images = @($SyntheticImagePath | ForEach-Object { (Resolve-Path -LiteralPath $_).Path })
if (!$images.Count) { throw 'Supply at least one self-owned synthetic image; never use a user image.' }
New-Item -ItemType Directory -Path $artifacts | Out-Null
$records = [Collections.Generic.List[object]]::new()
$hashes = @(Get-ChildItem -LiteralPath $candidate -File | Sort-Object Name | ForEach-Object {
    [ordered]@{ Name = $_.Name; Length = $_.Length; SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
$manifest = [ordered]@{
    Repository = $repo; Commit = (& git -C $repo rev-parse HEAD); Status = @(& git -C $repo status --short)
    CandidateDirectory = $candidate; CandidateFiles = $hashes
    SyntheticImages = @($images | ForEach-Object { [ordered]@{ Path = $_; SHA256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash } })
    StartedUtc = [DateTime]::UtcNow.ToString('o')
    Boundary = 'Pure logic and offscreen probes only. No product Window/Application startup, hooks, input injection, live plugin actions or daily instance attachment. Cold official-plugin editor integration is deliberately unavailable.'
}
$manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $artifacts 'candidate.json') -Encoding utf8
# A child probe may initialize ConfigManager before its explicit scenario gate. Keep
# every inherited location owned too; restore this calling process in the finally block.
$saved = @{}
foreach ($key in 'LOCALAPPDATA', 'APPDATA', 'TEMP', 'TMP', 'STARPIE_OFFICIAL_PLUGIN_REPO') {
    $saved[$key] = [Environment]::GetEnvironmentVariable($key)
}
function Invoke-Dotnet([string]$Name, [string[]]$Arguments, [bool]$RequirePass = $false) {
    $log = Join-Path $artifacts ($Name + '.log')
    $watch = [Diagnostics.Stopwatch]::StartNew()
    & dotnet @Arguments 2>&1 | ForEach-Object { $_.ToString() } | Tee-Object -FilePath $log | Write-Host
    $code = $LASTEXITCODE
    $watch.Stop()
    $lines = @(Get-Content -LiteralPath $log)
    $pass = @($lines | Where-Object { $_ -match '^PASS(?:\s|:)' })
    $summaries = @($pass | Where-Object { $_ -match '^PASS:?\s+(\d+)\s' })
    $checks = if ($summaries.Count) { [void]($summaries[-1] -match '^PASS:?\s+(\d+)\s'); [int]$Matches[1] } else { $pass.Count }
    $record = [ordered]@{ Name = $Name; Command = 'dotnet'; Arguments = $Arguments; ExitCode = $code; Seconds = $watch.Elapsed.TotalSeconds; Log = $log; CheckCount = $checks; RawPass = $pass; Skips = @($lines | Where-Object { $_ -match '^SKIP' }) }
    $records.Add($record)
    $records.ToArray() | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $artifacts 'commands.json') -Encoding utf8
    if ($code -ne 0) { throw "dotnet $Name failed ($code); see $log" }
    if ($RequirePass -and !$pass.Count) { throw "No raw PASS evidence from $Name; see $log" }
}
function Build-Probe([string]$Name, [string]$Project) {
    $projectPath = Join-Path $repo $Project
    [xml]$definition = Get-Content -LiteralPath $projectPath -Raw
    if ($definition.SelectNodes('//ProjectReference').Count) { throw "Host-rebuilding ProjectReference forbidden: $Project" }
    $output = Join-Path $artifacts ("build/$Name/bin")
    $intermediate = Join-Path $artifacts ("build/$Name/obj")
    Invoke-Dotnet "$Name-build" @('build', $projectPath, '-c', 'Release', '--nologo',
        "-p:StarPieCandidateDir=$candidate", "-p:ArtifactsRoot=$artifacts", "-p:SourceRoot=$repo",
        "-p:BaseIntermediateOutputPath=$intermediate/", "-p:MSBuildProjectExtensionsPath=$intermediate/",
        '-p:DefaultItemExcludes=**/obj/**', "-p:OutDir=$output/")
    $hostCopy = Join-Path $output 'StarPie.dll'
    if ((Get-FileHash -LiteralPath $hostCopy).Hash -ne (Get-FileHash -LiteralPath (Join-Path $candidate 'StarPie.dll')).Hash) { throw "$Name host copy SHA256 mismatch" }
    return (Join-Path $output 'test_process_launch.dll')
}
try {
    foreach ($key in 'LOCALAPPDATA', 'APPDATA', 'TEMP', 'TMP') {
        $owned = Join-Path $artifacts "inherited/$key"
        New-Item -ItemType Directory -Path $owned -Force | Out-Null
        [Environment]::SetEnvironmentVariable($key, $owned)
    }
    [Environment]::SetEnvironmentVariable('STARPIE_OFFICIAL_PLUGIN_REPO', (Join-Path $artifacts 'unavailable-official-plugins'))
    $dll = Build-Probe 'lazy-studio' 'scratch/native-memory-classification/LazyStudioProbe.csproj'
    Invoke-Dotnet 'lazy-studio-run' @($dll, (Join-Path $artifacts 'runtime/lazy-studio'), '--test-instance') $true
    $dll = Build-Probe 'font' 'scratch/settings-font-memory-tests/FontMemoryTests.csproj'
    Invoke-Dotnet 'font-run' @($dll, (Join-Path $artifacts 'runtime/font'), '--test-instance', '--expect-deferred') $true
    $dll = Build-Probe 'profile-trigger' 'scratch/profile-trigger-tests/ProfileTriggerTests.csproj'
    Invoke-Dotnet 'profile-trigger-run' @($dll, $repo) $true
    $dll = Build-Probe 'process-launch' 'scratch/process-launch-tests/test_process_launch.csproj'
    Invoke-Dotnet 'process-launch-run' @($dll) $true
    $dll = Build-Probe 'chrome-fullscreen' 'scratch/chrome-fullscreen-tests/ChromeFullScreenTests.csproj'
    Invoke-Dotnet 'chrome-fullscreen-run' @($dll) $true
    $dll = Build-Probe 'startup' 'scratch/startup-baseline-tests/StartupTests.csproj'
    foreach ($scenario in 'valid', 'missing', 'corrupt') {
        Invoke-Dotnet "startup-$scenario-run" @($dll, (Join-Path $artifacts "runtime/startup-$scenario"), $scenario, '--test-instance', '--expect-single') $true
    }
    $dll = Build-Probe 'retention' 'scratch/settings-retention-diagnostics/RetentionProbe.csproj'
    Invoke-Dotnet 'retention-run' @($dll, (Join-Path $artifacts 'runtime/retention'), '--test-instance') $true
    $dll = Build-Probe 'thumbnail' 'scratch/core-memory-baseline/ThumbnailProbe.csproj'
    for ($index = 0; $index -lt $images.Count; $index++) {
        foreach ($scenario in 'thumbnail', 'background-helper') {
            Invoke-Dotnet "$scenario-$index-run" @($dll, $scenario, (Join-Path $artifacts "runtime/$scenario-$index"), '--test-instance', $images[$index]) $true
        }
    }
    foreach ($file in $hashes) {
        if ((Get-FileHash -LiteralPath (Join-Path $candidate $file.Name)).Hash -ne $file.SHA256) { throw "Candidate changed during regression: $($file.Name)" }
    }
    [ordered]@{ Result = 'PASS'; CandidateDirectory = $candidate; CandidateDllSha256 = (Get-FileHash -LiteralPath (Join-Path $candidate 'StarPie.dll')).Hash; FinishedUtc = [DateTime]::UtcNow.ToString('o'); Runs = @($records | Where-Object { $_.Name -like '*-run' }); Boundary = $manifest.Boundary } |
        ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $artifacts 'summary.json') -Encoding utf8
    Write-Host "PASS beta.3 candidate regression; evidence: $artifacts"
}
finally {
    foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key, $saved[$key]) }
}
