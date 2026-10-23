<#
.SYNOPSIS
    Assert that all three release executables are nonempty and genuinely unsigned.
.DESCRIPTION
    Read-only metadata verification. Does not sign files or modify certificate stores.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateCount(3, 3)]
    [ValidateNotNullOrEmpty()]
    [string[]]$Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $Path) {
    $resolved = (Resolve-Path -LiteralPath $entry -ErrorAction Stop).ProviderPath
    if (-not $seen.Add($resolved)) { throw 'Unsigned release verification requires three distinct executables.' }
    $file = Get-Item -LiteralPath $resolved -ErrorAction Stop
    if ($file.PSIsContainer -or $file.Extension -ine '.exe' -or $file.Length -eq 0) {
        throw "Expected a nonempty executable: $entry"
    }
    $signature = Get-AuthenticodeSignature -LiteralPath $file.FullName -ErrorAction Stop
    if ($null -eq $signature -or $signature.Status.ToString() -ne 'NotSigned' -or $null -ne $signature.SignerCertificate) {
        throw "Unsigned mode refuses signed or unverifiable content: $entry"
    }
    Write-Host "Verified NotSigned: $($file.Name)"
}
