param(
    [Parameter(Mandatory=$true)][int]$TargetProcessId,
    [Parameter(Mandatory=$true)][string]$ExpectedExePath,
    [Parameter(Mandatory=$true)][string]$OutputPath,
    [Parameter(Mandatory=$true)][string]$StateLabel,
    [int]$Samples = 5
)
$ErrorActionPreference = 'Stop'
# Process.MainModule may be unavailable for a differently elevated user-launched
# process. Request only the OS-supported limited query right, never VM access.
if (-not ('SettingsMemoryProcessQuery' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
public static class SettingsMemoryProcessQuery {
    [DllImport("kernel32.dll", SetLastError=true)] static extern IntPtr OpenProcess(uint access, bool inherit, int id);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool QueryFullProcessImageName(IntPtr handle, int flags, StringBuilder name, ref int size);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    public static string ImagePath(int id) {
        IntPtr handle=OpenProcess(0x1000, false, id);
        if(handle==IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try {
            int size=32768; var name=new StringBuilder(size);
            if(!QueryFullProcessImageName(handle,0,name,ref size)) throw new Win32Exception(Marshal.GetLastWin32Error());
            return name.ToString();
        } finally { CloseHandle(handle); }
    }
}
'@
}
$imagePath = [SettingsMemoryProcessQuery]::ImagePath($TargetProcessId)
if (-not [string]::Equals([IO.Path]::GetFullPath($imagePath),[IO.Path]::GetFullPath($ExpectedExePath),[StringComparison]::OrdinalIgnoreCase)) {
    throw 'Limited-query process path does not match the required candidate.'
}
$started = (Get-Process -Id $TargetProcessId).StartTime.ToUniversalTime()
$exeHash = (Get-FileHash -LiteralPath $imagePath -Algorithm SHA256).Hash
$dllPath = [IO.Path]::ChangeExtension($imagePath,'.dll')
$dllHash = (Get-FileHash -LiteralPath $dllPath -Algorithm SHA256).Hash
$sampler = Join-Path $PSScriptRoot '..\startup-baseline-tests\Sample-Memory.ps1'
$rows = @((& $sampler -TargetProcessId $TargetProcessId -Samples $Samples -IntervalSeconds 5 -StateLabel $StateLabel) | ConvertFrom-Json)
if ((Get-Process -Id $TargetProcessId).StartTime.ToUniversalTime() -ne $started -or [SettingsMemoryProcessQuery]::ImagePath($TargetProcessId) -ne $imagePath) { throw 'Process identity changed during sampling.' }
foreach ($row in $rows) {
    if ([datetime]$row.ProcessStartedUtc -ne $started) { throw 'Sampler process identity does not match limited query.' }
    $row.ExePath=$imagePath; $row.ExeSha256=$exeHash
    $row.HostDllOnDiskPath=$dllPath; $row.HostDllOnDiskSha256=$dllHash
    $row | Add-Member -NotePropertyName PathSource -NotePropertyValue 'QueryFullProcessImageName: PROCESS_QUERY_LIMITED_INFORMATION'
}
ConvertTo-Json -InputObject @($rows) -Depth 4 | Set-Content -LiteralPath $OutputPath -Encoding utf8
$rows | Select-Object -First 1 -Last 1 TimeUtc,ProcessId,ProcessStartedUtc,ExePath,HostDllOnDiskSha256,TotalWorkingSetMiB,PrivateWorkingSetMiB,PrivateBytesMiB,Handles,Threads | ConvertTo-Json
