#Requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Tag, [Parameter(Mandatory)][string]$Directory)
$ErrorActionPreference = 'Stop'
# Inspect CLI exit codes explicitly, including the expected missing-release response.
$PSNativeCommandUseErrorActionPreference = $false
Set-StrictMode -Version Latest
$version = & "$PSScriptRoot/get-version.ps1" -Tag $Tag
& "$PSScriptRoot/verify-release-assets.ps1" -Version $version -Directory $Directory
$repository = 'DmitryGaravsky/Spdx.Expressions'
function Invoke-Gh([string[]]$Arguments) {
    $result = & gh @Arguments --repo $repository
    if ($LASTEXITCODE -ne 0) {
        throw "GitHub CLI failed: $($Arguments[0]) $($Arguments[1])"
    }
    return $result
}
function Assert-RemoteTag {
    # Existing drafts also need this check: publishing a draft can otherwise create its tag.
    & gh api "repos/$repository/git/ref/tags/$Tag" --method GET --silent
    if ($LASTEXITCODE -ne 0) {
        throw 'The release tag must already exist and be readable on GitHub.'
    }
}
Assert-RemoteTag
$prerelease = $version.Contains('-')
$details = & gh release view $Tag --json isDraft,isPrerelease,assets --repo $repository
if ($LASTEXITCODE -ne 0) {
    # --verify-tag prevents accidental tag creation. A failed create leaves publication incomplete.
    $arguments = @('release', 'create', $Tag, '--draft', '--verify-tag', '--title', $Tag, '--generate-notes')
    if ($prerelease) {
        $arguments += '--prerelease'
    }
    Invoke-Gh $arguments | Write-Host
    $details = Invoke-Gh @('release', 'view', $Tag, '--json', 'isDraft,isPrerelease,assets')
}
$release = ($details -join [char]10) | ConvertFrom-Json
if ($release.isPrerelease -ne $prerelease) {
    throw 'Existing release has a different prerelease classification.'
}
$files = @(Get-ChildItem -LiteralPath $Directory -File)
$assetNames = @($release.assets | ForEach-Object { $_.name })
if (@($assetNames | Where-Object { $_ -cnotin $files.Name }).Count -ne 0) {
    throw 'Existing release contains unexpected assets; nothing will be overwritten.'
}
$download = Join-Path ([IO.Path]::GetTempPath()) ('spdx-release-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($download) | Out-Null
# Verify every existing asset before uploading anything missing from a draft.
foreach ($file in $files) {
    if ($file.Name -cin $assetNames) {
        Invoke-Gh @('release', 'download', $Tag, '--pattern', $file.Name, '--dir', $download) | Write-Host
        $remoteHash = (Get-FileHash -LiteralPath (Join-Path $download $file.Name)).Hash
        if ($remoteHash -cne (Get-FileHash -LiteralPath $file.FullName).Hash) {
            throw "Existing release asset differs: $($file.Name). Assets will not be overwritten."
        }
    }
    elseif (-not $release.isDraft) {
        throw "Published release is missing asset: $($file.Name)"
    }
}
foreach ($file in $files) {
    if ($file.Name -cnotin $assetNames) {
        Invoke-Gh @('release', 'upload', $Tag, $file.FullName) | Write-Host
    }
}
if ($release.isDraft) {
    $arguments = @('release', 'edit', $Tag, '--draft=false', '--verify-tag')
    if ($prerelease) {
        $arguments += '--latest=false'
    }
    Invoke-Gh $arguments | Write-Host
}
Write-Host "GitHub release $Tag has the verified assets."
