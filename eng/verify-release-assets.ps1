#Requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Version, [Parameter(Mandatory)][string]$Directory, [string]$Commit,
    [ValidateSet('Spdx.Expressions', 'Spdx.Compliance', IgnoreCase = $false)][string]$PackageId = 'Spdx.Expressions')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$Version = & "$PSScriptRoot/get-version.ps1" -Version $Version
$Directory = (Resolve-Path -LiteralPath $Directory).ProviderPath
$name = "$PackageId.$Version.nupkg"
$expected = @($name, 'SHA256SUMS')
$files = @(Get-ChildItem -LiteralPath $Directory -Force)
$unexpected = @($files | Where-Object {
    $_.PSIsContainer -or $_.Name -cnotin $expected -or ($_.Attributes -band [IO.FileAttributes]::ReparsePoint)
})
if ($files.Count -ne $expected.Count -or $unexpected.Count -ne 0) {
    throw 'Expected exactly the library package and SHA256SUMS, with no directories or links.'
}
$hash = (Get-FileHash -LiteralPath (Join-Path $Directory $name) -Algorithm SHA256).Hash.ToLowerInvariant()
$lines = @(Get-Content -LiteralPath (Join-Path $Directory 'SHA256SUMS'))
if ($lines.Count -ne 1 -or $lines[0] -cne "$hash  $name") {
    throw 'Package checksum does not match SHA256SUMS.'
}
$archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $Directory $name))
try {
    $specifications = @($archive.Entries | Where-Object { $_.FullName.EndsWith('.nuspec', [StringComparison]::Ordinal) })
    if ($specifications.Count -ne 1 -or $specifications[0].FullName -cne "$PackageId.nuspec") {
        throw "Expected exactly one $PackageId package specification."
    }
    $stream = $specifications[0].Open()
    try { $spec = [Xml.Linq.XDocument]::Load($stream) }
    finally { $stream.Dispose() }
    $ns = $spec.Root.Name.Namespace
    $metadata = $spec.Root.Element($ns + 'metadata')
    if ($metadata.Element($ns + 'id').Value -cne $PackageId -or
        $metadata.Element($ns + 'version').Value -cne $Version) {
        throw 'Package identity does not match the release version.'
    }
    $repository = $metadata.Element($ns + 'repository')
    if ($PSBoundParameters.ContainsKey('Commit') -and
        ($Commit -cnotmatch '\A[0-9a-f]{40}\z' -or $null -eq $repository.Attribute('commit') -or
        $repository.Attribute('commit').Value -cne $Commit)) {
        throw 'Package repository commit does not match the release event.'
    }
    $url = 'https://github.com/DmitryGaravsky/Spdx.Expressions'
    if ($metadata.Element($ns + 'projectUrl').Value -cne $url -or
        $repository.Attribute('url').Value -cne $url -or $repository.Attribute('type').Value -cne 'git' -or
        $metadata.Element($ns + 'license').Value -cne 'MIT' -or
        $metadata.Element($ns + 'authors').Value -cne 'Dmitry Garavsky') {
        throw 'Package repository, license or author metadata is incorrect.'
    }
    $required = @("lib/net10.0/$PackageId.dll", "lib/net10.0/$PackageId.xml", 'README.md', 'LICENSE')
    if ($PackageId -ceq 'Spdx.Expressions') {
        $required += @('THIRD-PARTY-NOTICES.md', 'data/manifest.json', 'data/licenses.json',
            'data/exceptions.json', 'data/UPSTREAM-README.md', 'data/UPSTREAM-XML-README.md')
    }
    foreach ($entry in $required) {
        if ($null -eq $archive.GetEntry($entry)) {
            throw "Missing release package entry: $entry"
        }
    }
}
finally { $archive.Dispose() }
Write-Host 'Release file names, checksum, package identity and metadata verified.'
