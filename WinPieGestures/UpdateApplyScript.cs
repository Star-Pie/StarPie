namespace WinPieGestures;

internal static class UpdateApplyScript
{
    internal const string Content = """
param([Parameter(Mandatory=$true)][string]$RequestPath)
$ErrorActionPreference = 'Stop'
$request = $null
$applied = [System.Collections.Generic.List[object]]::new()
$newProcess = $null
$canRollback = $true
$accepted = $false
$confirmed = $false
function Write-UpdateLog([string]$message) {
    if ($null -ne $request) {
        try { [IO.File]::AppendAllText($request.LogFile, ([DateTime]::UtcNow.ToString('o') + ' ' + $message + [Environment]::NewLine)) } catch { }
    }
}
function Assert-LocalPath([string]$path, [string]$parent) {
    $full = [IO.Path]::GetFullPath($path)
    $prefix = [IO.Path]::GetFullPath($parent).TrimEnd('\') + '\'
    if (-not $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Path escaped its owned directory.' }
    $check = $full
    while ($check) {
        if ([IO.File]::Exists($check) -or [IO.Directory]::Exists($check)) {
            if (([IO.File]::GetAttributes($check) -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Redirected update path.' }
        }
        $check = [IO.Path]::GetDirectoryName($check)
    }
    return $full
}
try {
    $RequestPath = [IO.Path]::GetFullPath($RequestPath)
    $root = [IO.Path]::GetDirectoryName($RequestPath)
    $request = [IO.File]::ReadAllText($RequestPath) | ConvertFrom-Json
    if ($request.Token -cne [IO.Path]::GetFileName($root)) { throw 'Invalid update token.' }
    if ([IO.Path]::GetFullPath($request.PayloadDirectory) -ine (Join-Path $root 'payload') -or
        [IO.Path]::GetFullPath($request.BackupDirectory) -ine (Join-Path $root 'backup')) { throw 'Invalid staging directories.' }
    $files = @($request.Files)
    if ($files.Count -eq 0 -or $files.Count -gt 4096) { throw 'Invalid payload manifest.' }
    foreach ($file in $files) {
        $source = Assert-LocalPath (Join-Path $request.PayloadDirectory $file.RelativePath) $request.PayloadDirectory
        $null = Assert-LocalPath (Join-Path $request.TargetDirectory $file.RelativePath) $request.TargetDirectory
        if ((Get-Item -LiteralPath $source).Length -ne $file.Length -or
            (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ine $file.Sha256) { throw 'Staged payload verification failed.' }
    }
    Write-UpdateLog 'Prepared verified payload; waiting for parent authorization.'
    [IO.File]::WriteAllText((Join-Path $root 'prepared'), '1')
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while (-not [IO.File]::Exists((Join-Path $root 'proceed'))) {
        if ([IO.File]::Exists((Join-Path $root 'abort')) -or $timer.Elapsed.TotalSeconds -gt 30) { throw 'Update authorization was cancelled.' }
        Start-Sleep -Milliseconds 100
    }
    if ([IO.File]::Exists((Join-Path $root 'abort'))) { throw 'Update was cancelled.' }
    [IO.File]::WriteAllText((Join-Path $root 'accepted.tmp'), $request.Token)
    [IO.File]::Move((Join-Path $root 'accepted.tmp'), (Join-Path $root 'accepted'))
    $accepted = $true
    $timer.Restart()
    $confirmation = Join-Path $root 'confirmed'
    while (-not [IO.File]::Exists($confirmation)) {
        if ([IO.File]::Exists((Join-Path $root 'abort')) -or $timer.Elapsed.TotalSeconds -gt 15) { throw 'Parent did not confirm update authorization.' }
        Start-Sleep -Milliseconds 100
    }
    if ([IO.File]::Exists((Join-Path $root 'abort')) -or [IO.File]::ReadAllText($confirmation) -cne $request.Token) { throw 'Parent cancelled update authorization.' }
    $confirmed = $true
    $previous = $null
    try { $previous = [Diagnostics.Process]::GetProcessById([int]$request.PreviousPid) }
    catch [ArgumentException] { }
    if ($null -ne $previous) {
        try {
            if (-not $previous.HasExited -and $previous.StartTime.ToUniversalTime().Ticks -eq [long]$request.PreviousStartTicks) {
                if (-not $previous.WaitForExit(60000)) { throw 'Previous instance did not exit; no files were changed.' }
            }
        } finally { $previous.Dispose() }
    }
    if ([IO.File]::Exists((Join-Path $root 'abort'))) { throw 'Update was cancelled before file replacement.' }
    Write-UpdateLog 'Previous instance exited; backing up only manifest-owned files.'
    foreach ($file in $files) {
        $target = Assert-LocalPath (Join-Path $request.TargetDirectory $file.RelativePath) $request.TargetDirectory
        $backup = Assert-LocalPath (Join-Path $request.BackupDirectory $file.RelativePath) $request.BackupDirectory
        $existed = [IO.File]::Exists($target)
        if ($existed) {
            $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($backup))
            [IO.File]::Copy($target, $backup, $false)
        }
        $applied.Add([pscustomobject]@{ Target=$target; Backup=$backup; Existed=$existed })
    }
    foreach ($file in $files) {
        $target = Assert-LocalPath (Join-Path $request.TargetDirectory $file.RelativePath) $request.TargetDirectory
        $source = Assert-LocalPath (Join-Path $request.PayloadDirectory $file.RelativePath) $request.PayloadDirectory
        $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
        [IO.File]::Copy($source, $target, $true)
        if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ine $file.Sha256) { throw 'Installed payload verification failed.' }
    }
    $exe = Assert-LocalPath $request.Executable $request.TargetDirectory
    $arguments = '--silent --restart-after-exit ' + $request.PreviousPid + ' ' + $request.PreviousStartTicks + ' --update-request "' + $RequestPath + '"'
    $newProcess = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $request.TargetDirectory -WindowStyle Hidden -PassThru
    if ($null -eq $newProcess) { throw 'Updated process was not created.' }
    Write-UpdateLog ('Created updated process PID=' + $newProcess.Id)
    $canRollback = $false
    $timer.Restart()
    $waitSeconds = 45
    if (-not $request.RequireReadySignal) { $waitSeconds = 3 }
    $ready = Join-Path $root 'ready'
    while (-not [IO.File]::Exists($ready)) {
        if ($newProcess.HasExited) { $canRollback = $true; throw 'Updated process exited before readiness.' }
        if ($timer.Elapsed.TotalSeconds -gt $waitSeconds) {
            if ($request.RequireReadySignal) { throw 'Updated process has not acknowledged readiness; backup retained.' }
            Write-UpdateLog 'Legacy version is running; that version does not support readiness acknowledgement.'
            break
        }
        Start-Sleep -Milliseconds 100
    }
    if ([IO.File]::Exists($ready) -and [IO.File]::ReadAllText($ready) -cne $request.Token) { throw 'Invalid readiness response.' }
    Write-UpdateLog 'Update handoff completed.'
    [IO.File]::WriteAllText((Join-Path $root 'result.json'), '{"Success":true}')
    # Keep the small log/result for diagnosis; the downloaded archive is owned by
    # the download workflow, not deleted here on an unconfirmed failure.
    $payloadRoot = Assert-LocalPath $request.PayloadDirectory $root
    $backupRoot = Assert-LocalPath $request.BackupDirectory $root
    try {
        if ([IO.Directory]::Exists($payloadRoot)) { Remove-Item -LiteralPath $payloadRoot -Recurse -Force }
        if ([IO.Directory]::Exists($backupRoot)) { Remove-Item -LiteralPath $backupRoot -Recurse -Force }
    } catch { Write-UpdateLog 'Update succeeded; temporary file cleanup is pending.' }
} catch {
    Write-UpdateLog ('FAILED: ' + $_.Exception.Message)
    if ($canRollback -and $applied.Count -gt 0) {
        foreach ($entry in $applied) {
            try {
                $null = Assert-LocalPath $entry.Target $request.TargetDirectory
                if ($entry.Existed) { [IO.File]::Copy($entry.Backup, $entry.Target, $true) }
                elseif ([IO.File]::Exists($entry.Target)) { [IO.File]::Delete($entry.Target) }
            } catch { Write-UpdateLog ('Rollback requires attention: ' + $_.Exception.Message) }
        }
        Write-UpdateLog 'Restored backed-up application files; original configuration was not modified.'
    }
    if ($null -ne $request) {
        try {
            [IO.File]::WriteAllText((Join-Path $root 'result.json'), '{"Success":false}')
            if ($confirmed) {
                Add-Type -AssemblyName System.Windows.Forms
                $null = [System.Windows.Forms.MessageBox]::Show(($request.FailureMessage + [Environment]::NewLine + $request.LogFile), 'StarPie')
            }
        } catch { }
    }
    exit 1
} finally {
    if ($null -ne $newProcess) { $newProcess.Dispose() }
}
""";
}
