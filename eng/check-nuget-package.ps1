#Requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Version, [Parameter(Mandatory)][string]$Directory,
    [Parameter(Mandatory)][string]$Commit)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$Version = & "$PSScriptRoot/get-version.ps1" -Version $Version
& "$PSScriptRoot/verify-release-assets.ps1" -Version $Version -Directory $Directory -Commit $Commit
$name = "Spdx.Expressions.$Version.nupkg"
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('spdx-nuget-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temporary) | Out-Null
$download = Join-Path $temporary $name
$url = "https://api.nuget.org/v3-flatcontainer/spdx.expressions/$Version/$($name.ToLowerInvariant())"
try {
    Invoke-WebRequest -Uri $url -OutFile $download -TimeoutSec 60 | Out-Null
}
catch [Microsoft.PowerShell.Commands.HttpResponseException] {
    if ($_.Exception.Response.StatusCode -eq [Net.HttpStatusCode]::NotFound) {
        return $false
    }
    throw
}
& "$PSScriptRoot/compare-package-content.ps1" -ExpectedPackage (Join-Path $Directory $name) -ActualPackage $download
return $true
