param(
    [Parameter(Mandatory=$true)][ValidateRange(1,2147483647)][int]$TargetProcessId,
    [ValidateRange(1,120)][int]$Samples = 6,
    [ValidateRange(1,30)][int]$IntervalSeconds = 5,
    [string]$ExpectedExePath,
    [string]$StateLabel = 'unspecified'
)
$ErrorActionPreference = 'Stop'
$initial = Get-Process -Id $TargetProcessId
try {
    $started = $initial.StartTime.ToUniversalTime()
    $actualPath = $initial.Path
    if ($ExpectedExePath -and -not [string]::Equals([IO.Path]::GetFullPath($actualPath), [IO.Path]::GetFullPath($ExpectedExePath), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Target process executable does not match the expected candidate.'
    }
    $fileHash = if ($actualPath) { (Get-FileHash -LiteralPath $actualPath -Algorithm SHA256).Hash } else { $null }
    $hostDllPath = if ($actualPath) { Join-Path (Split-Path -Parent $actualPath) 'StarPie.dll' } else { $null }
    $hostDllHash = if ($hostDllPath -and (Test-Path -LiteralPath $hostDllPath)) { (Get-FileHash -LiteralPath $hostDllPath -Algorithm SHA256).Hash } else { $null }
} finally { $initial.Dispose() }
$rows = for ($sample=0; $sample -lt $Samples; $sample++) {
    $process = Get-Process -Id $TargetProcessId
    try {
        if ($process.StartTime.ToUniversalTime() -ne $started) { throw 'Process identity changed; stop rather than sample a reused PID.' }
        $privateResident = $null
        $faultRate = $null
        try {
            $counter = Get-CimInstance -ClassName Win32_PerfFormattedData_PerfProc_Process -Filter "IDProcess = $TargetProcessId"
            if ($counter) { $privateResident = [math]::Round($counter.WorkingSetPrivate / 1MB, 3); $faultRate = $counter.PageFaultsPersec }
        } catch { }
        [pscustomobject]@{
            TimeUtc = [DateTime]::UtcNow.ToString('o')
            State = $StateLabel
            ProcessId = $TargetProcessId
            ProcessStartedUtc = $started.ToString('o')
            ExePath = $actualPath
            ExeSha256 = $fileHash
            HostDllOnDiskPath = $hostDllPath
            HostDllOnDiskSha256 = $hostDllHash
            TotalWorkingSetMiB = [math]::Round($process.WorkingSet64 / 1MB, 3)
            PrivateWorkingSetMiB = $privateResident
            PrivateBytesMiB = [math]::Round($process.PrivateMemorySize64 / 1MB, 3)
            Handles = $process.HandleCount
            Threads = $process.Threads.Count
            CpuSeconds = $process.TotalProcessorTime.TotalSeconds
            PageFaultsPerSec = $faultRate
            ManagedLiveHeapBytes = $null
        }
    } finally { $process.Dispose() }
    if ($sample -lt $Samples-1) { Start-Sleep -Seconds $IntervalSeconds }
}
ConvertTo-Json -InputObject @($rows) -Depth 3
