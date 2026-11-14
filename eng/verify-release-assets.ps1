#Requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Version, [Parameter(Mandatory)][string]$Directory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$Version = & "$PSScriptRoot/get-version.ps1" -Version $Version
$Directory = (Resolve-Path -LiteralPath $Directory).ProviderPath
$name = "Spdx.Expressions.$Version.nupkg"
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
    if ($specifications.Count -ne 1 -or $specifications[0].FullName -cne 'Spdx.Expressions.nuspec') {
        throw 'Expected exactly one Spdx.Expressions package specification.'
    }
    $stream = $specifications[0].Open()
    try { $spec = [Xml.Linq.XDocument]::Load($stream) }
    finally { $stream.Dispose() }
    $ns = $spec.Root.Name.Namespace
    $metadata = $spec.Root.Element($ns + 'metadata')
    if ($metadata.Element($ns + 'id').Value -cne 'Spdx.Expressions' -or
        $metadata.Element($ns + 'version').Value -cne $Version) {
        throw 'Package identity does not match the release version.'
    }
    $repository = $metadata.Element($ns + 'repository')
    $url = 'https://github.com/DmitryGaravsky/Spdx.Expressions'
    if ($metadata.Element($ns + 'projectUrl').Value -cne $url -or
        $repository.Attribute('url').Value -cne $url -or $repository.Attribute('type').Value -cne 'git' -or
        $metadata.Element($ns + 'license').Value -cne 'MIT' -or
        $metadata.Element($ns + 'authors').Value -cne 'Dmitry Garavsky') {
        throw 'Package repository, license or author metadata is incorrect.'
    }
    foreach ($entry in @('lib/net10.0/Spdx.Expressions.dll', 'lib/net10.0/Spdx.Expressions.xml',
        'README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'data/manifest.json',
        'data/licenses.json', 'data/exceptions.json', 'data/UPSTREAM-README.md', 'data/UPSTREAM-XML-README.md')) {
        if ($null -eq $archive.GetEntry($entry)) {
            throw "Missing release package entry: $entry"
        }
    }
}
finally { $archive.Dispose() }
Write-Host 'Release file names, checksum, package identity and metadata verified.'
