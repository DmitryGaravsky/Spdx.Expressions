#Requires -Version 7.0
[CmdletBinding()]
param([string]$Version, [string]$Tag)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($PSBoundParameters.ContainsKey('Version') -and $PSBoundParameters.ContainsKey('Tag')) {
    throw 'Specify Version or Tag, not both.'
}
if ($PSBoundParameters.ContainsKey('Tag')) {
    if ([string]::IsNullOrWhiteSpace($Tag) -or -not $Tag.StartsWith('v', [StringComparison]::Ordinal)) {
        throw 'Release tags must start with v.'
    }
    $Version = $Tag.Substring(1)
}
elseif (-not $PSBoundParameters.ContainsKey('Version')) {
    [xml]$properties = Get-Content -LiteralPath "$PSScriptRoot/../Directory.Build.props" -Raw
    $Version = [string]$properties.Project.PropertyGroup.Version
}
# https://semver.org/#spec-item-9
# Canonical NuGet/SemVer subset: lowercase prerelease labels, no metadata or numeric leading zeros.
$number = '(0|[1-9][0-9]*)'
$identifier = '(0|[1-9][0-9]*|[0-9]*[a-z-][0-9a-z-]*)'
if ($Version -cnotmatch "\A$number\.$number\.$number(-$identifier(\.$identifier)*)?\z") {
    throw "Invalid version: $Version. Use major.minor.patch[-prerelease] with lowercase labels."
}
return $Version
