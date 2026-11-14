#Requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Tag, [string]$RestoreSource)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$version = & "$PSScriptRoot/get-version.ps1" -Tag $Tag
$output = Join-Path $root "artifacts/release/$version"
if (Test-Path -LiteralPath $output) {
    throw "Refusing to overwrite release files: $output"
}
$runId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '-' + [Guid]::NewGuid().ToString('N')
$runDirectory = Join-Path $root "artifacts/verification/$runId"
$arguments = @{ Version = $version; OutputDirectory = $runDirectory }
if ($RestoreSource) {
    $arguments.RestoreSource = $RestoreSource
}
& "$PSScriptRoot/verify.ps1" @arguments
$run = Get-Content -LiteralPath "$runDirectory/run.json" -Raw | ConvertFrom-Json
if ($run.status -cne 'Passed' -or $run.version -cne $version) {
    throw 'Release preparation requires a passing verification run for the exact version.'
}
$package = Join-Path $root $run.package
$hash = (Get-FileHash -LiteralPath $package).Hash.ToLowerInvariant()
if ($hash -cne $run.packageSha256) {
    throw 'Verified package changed before release preparation.'
}
[void](New-Item -ItemType Directory -Path $output -ErrorAction Stop)
$name = "Spdx.Expressions.$version.nupkg"
Copy-Item -LiteralPath $package -Destination (Join-Path $output $name)
if ((Get-FileHash -LiteralPath (Join-Path $output $name)).Hash.ToLowerInvariant() -cne $hash) {
    throw 'Copied release package differs from the verified package.'
}
[IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS'), "$hash  $name" + [char]10, [Text.UTF8Encoding]::new($false))
& "$PSScriptRoot/verify-release-assets.ps1" -Version $version -Directory $output
Write-Output "Release files prepared and verified in $output. Nothing was published."
