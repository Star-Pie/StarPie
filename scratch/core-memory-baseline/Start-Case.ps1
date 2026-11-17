param(
    [Parameter(Mandatory=$true)][string]$Session,
    [Parameter(Mandatory=$true)][ValidateSet('host-only','core-three')][string]$Case,
    [switch]$ValidateOnly
)
$ErrorActionPreference='Stop'
$owned=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'artifacts')).TrimEnd('\')+'\'
$sessionPath=[IO.Path]::GetFullPath($Session).TrimEnd('\')
if(-not $sessionPath.StartsWith($owned,[StringComparison]::OrdinalIgnoreCase)){throw 'Session must be in owned artifacts.'}
$m=Get-Content -LiteralPath (Join-Path $sessionPath 'session.json') -Raw|ConvertFrom-Json
if(-not $m.ManualLaunchOnly){throw 'Manual-only fixture required.'}
foreach($h in $m.CandidateHashes.PSObject.Properties){
    if((Get-FileHash -LiteralPath (Join-Path $m.CandidateDirectory $h.Name) -Algorithm SHA256).Hash -ne $h.Value){throw 'Candidate hash changed.'}
}
if(Test-Path -LiteralPath (Join-Path $m.CandidateDirectory 'portable.flag')){throw 'Portable flag would bypass isolated data.'}
$selected=$m.Cases.$Case
$caseRoot=Join-Path $sessionPath $Case
$expectedFiles=New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach($h in $selected.Files.PSObject.Properties){[void]$expectedFiles.Add($h.Name)}
$actualFiles=New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
$pending=New-Object 'Collections.Generic.Queue[string]'
$pending.Enqueue($caseRoot)
while($pending.Count -gt 0){
    foreach($item in Get-ChildItem -LiteralPath $pending.Dequeue() -Force){
        if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Reparse points are not allowed in baseline cases.'}
        if($item.PSIsContainer){$pending.Enqueue($item.FullName)}
        else{[void]$actualFiles.Add($item.FullName.Substring($sessionPath.Length+1))}
    }
}
if(-not $actualFiles.SetEquals($expectedFiles)){throw 'Unexpected or missing fixture files; baseline is contaminated.'}
foreach($h in $selected.Files.PSObject.Properties){
    $p=[IO.Path]::GetFullPath((Join-Path $sessionPath $h.Name))
    if(-not $p.StartsWith($caseRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture path escaped selected case.'}
    if((Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash -ne $h.Value){throw 'Fixture changed; do not reuse a mutated baseline.'}
}
if($selected.Environment.PSObject.Properties.Name.Count -ne 4){throw 'Exactly four isolated environment variables required.'}
foreach($name in @('LOCALAPPDATA','APPDATA','TEMP','TMP')){
    if($name -notin $selected.Environment.PSObject.Properties.Name){throw 'Required isolated environment variable missing.'}
}
foreach($h in $selected.Environment.PSObject.Properties){
    $p=[IO.Path]::GetFullPath($h.Value)
    if(-not $p.StartsWith($sessionPath+'\'+$Case+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Environment escaped selected case.'}
}
$result=[ordered]@{Case=$Case;Candidate=$m.CandidateDirectory;PluginIds=$selected.PluginIds;Arguments='--test-instance --silent';Environment=$selected.Environment;ValidateOnly=[bool]$ValidateOnly}
if($ValidateOnly){$result|ConvertTo-Json -Depth 5;return}
if(@(Get-Process -Name StarPie -ErrorAction SilentlyContinue).Count -ne 0){throw 'Please normally exit the current StarPie tray instance before this manual run.'}
$marker=Join-Path $sessionPath ($Case+'-launch.json')
$claim=[IO.File]::Open($marker,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
$claim.Dispose()
try {
    $info=New-Object Diagnostics.ProcessStartInfo
    $info.FileName=Join-Path $m.CandidateDirectory 'StarPie.exe'
    $info.WorkingDirectory=$m.CandidateDirectory
    $info.Arguments='--test-instance --silent'
    $info.UseShellExecute=$false
    $info.CreateNoWindow=$true
    $info.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
    foreach($h in $selected.Environment.PSObject.Properties){$info.EnvironmentVariables[$h.Name]=[string]$h.Value}
    $process=[Diagnostics.Process]::Start($info)
    $result.ProcessId=$process.Id
    $result.ProcessStartedUtc=$process.StartTime.ToUniversalTime().ToString('o')
    $result.LaunchRecordedUtc=[DateTime]::UtcNow.ToString('o')
    $result|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $marker -Encoding utf8
    $process.Dispose()
    Write-Output 'Started isolated tray baseline. Keep settings closed until sampling is complete.'
} catch {
    [pscustomobject]@{Case=$Case;Error=$_.Exception.Message;FailedUtc=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json|Set-Content -LiteralPath $marker -Encoding utf8
    throw
}
