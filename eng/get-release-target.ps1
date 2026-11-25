#Requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][AllowEmptyString()][string]$Tag)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($Tag.StartsWith('compliance-v', [StringComparison]::Ordinal)) {
    $id = 'Spdx.Compliance'
    $version = & "$PSScriptRoot/get-version.ps1" -Version $Tag.Substring('compliance-v'.Length)
    $directory = "artifacts/release/compliance/$version"
}
elseif ($Tag.StartsWith('v', [StringComparison]::Ordinal)) {
    $id = 'Spdx.Expressions'
    $version = & "$PSScriptRoot/get-version.ps1" -Version $Tag.Substring(1)
    $directory = "artifacts/release/$version"
}
else {
    throw 'Release tags must start with v or compliance-v.'
}
[pscustomobject]@{ PackageId = $id; Version = $version; Directory = $directory }
