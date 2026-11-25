#Requires -Version 7.0
[CmdletBinding()]
param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $root ('artifacts/release-tests/' + [Guid]::NewGuid().ToString('N'))
}
$suite = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
if (Test-Path -LiteralPath $suite) {
    throw "Refusing to overwrite release test evidence: $suite"
}
[IO.Directory]::CreateDirectory($suite) | Out-Null
$completed = [Collections.Generic.List[string]]::new()
$result = [ordered]@{ status = 'Running'; cases = $completed; networkUsed = $false }
$getVersion = Join-Path $PSScriptRoot 'get-version.ps1'
$validator = Join-Path $PSScriptRoot 'verify-release-assets.ps1'
$publisher = Join-Path $PSScriptRoot 'publish-github-release.ps1'
$sourceCheck = Join-Path $PSScriptRoot 'verify-release-source.ps1'
$nugetCheck = Join-Path $PSScriptRoot 'check-nuget-package.ps1'
$compare = Join-Path $PSScriptRoot 'compare-package-content.ps1'
$commit = '0123456789abcdef0123456789abcdef01234567'
function Assert([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw $Message
    }
}
function Reject([scriptblock]$Action, [string]$MessagePattern) {
    $rejected = $false
    try { & $Action 6>&1 | Out-Null }
    catch {
        Assert ($_.Exception.Message -match $MessagePattern) "Unexpected failure: $($_.Exception.Message)"
        $rejected = $true
    }
    Assert $rejected 'Expected rejection did not occur.'
}
function Update-Checksum([string]$Directory, [string]$Version = '1.2.3-preview.1', [string]$PackageId = 'Spdx.Expressions') {
    $name = "$PackageId.$Version.nupkg"
    $hash = (Get-FileHash -LiteralPath (Join-Path $Directory $name)).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText((Join-Path $Directory 'SHA256SUMS'), "$hash  $name" + [char]10)
}
function New-Fixture([string]$Name, [string]$Version = '1.2.3-preview.1',
    [string]$MetadataVersion = '', [string]$RepositoryUrl = 'https://github.com/DmitryGaravsky/Spdx.Expressions',
    [string]$OmitEntry = '', [string]$MetadataCommit = $commit, [string]$PackageId = 'Spdx.Expressions') {
    $directory = Join-Path $suite $Name
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    if (-not $MetadataVersion) {
        $MetadataVersion = $Version
    }
    $archive = [IO.Compression.ZipFile]::Open((Join-Path $directory "$PackageId.$Version.nupkg"), 'Create')
    try {
        $writer = [IO.StreamWriter]::new($archive.CreateEntry("$PackageId.nuspec").Open())
        try {
            $writer.Write(@"
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata>
<id>$PackageId</id><version>$MetadataVersion</version><authors>Dmitry Garavsky</authors>
<license type="expression">MIT</license>
<projectUrl>https://github.com/DmitryGaravsky/Spdx.Expressions</projectUrl>
<repository type="git" url="$RepositoryUrl" commit="$MetadataCommit" />
</metadata></package>
"@)
        }
        finally { $writer.Dispose() }
        # Synthetic payloads test publication boundaries, not library behavior.
        $entries = @("lib/net10.0/$PackageId.dll", "lib/net10.0/$PackageId.xml", 'README.md', 'LICENSE')
        if ($PackageId -ceq 'Spdx.Expressions') {
            $entries += @('THIRD-PARTY-NOTICES.md', 'data/manifest.json', 'data/licenses.json', 'data/exceptions.json',
                'data/UPSTREAM-README.md', 'data/UPSTREAM-XML-README.md')
        }
        foreach ($entry in $entries) {
            if ($entry -ceq $OmitEntry) {
                continue
            }
            $writer = [IO.StreamWriter]::new($archive.CreateEntry($entry).Open())
            try { $writer.Write('Independent release fixture') }
            finally { $writer.Dispose() }
        }
    }
    finally { $archive.Dispose() }
    Update-Checksum $directory $Version $PackageId
    return $directory
}
function Copy-Fixture([string]$Name) {
    $directory = Join-Path $suite $Name
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    foreach ($file in Get-ChildItem -LiteralPath $fixture -File) {
        Copy-Item -LiteralPath $file.FullName -Destination $directory
    }
    return $directory
}
function Change-Entry([string]$Directory, [string]$Name, [string]$Text, [switch]$Append, [switch]$Delete) {
    $package = Join-Path $Directory "Spdx.Expressions.$version.nupkg"
    $archive = [IO.Compression.ZipFile]::Open($package, 'Update')
    try {
        if (-not $Append -and $null -ne $archive.GetEntry($Name)) {
            $archive.GetEntry($Name).Delete()
        }
        if (-not $Delete) {
            $writer = [IO.StreamWriter]::new($archive.CreateEntry($Name).Open())
            try { $writer.Write($Text) }
            finally { $writer.Dispose() }
        }
    }
    finally { $archive.Dispose() }
    Update-Checksum $Directory
    return $package
}
function Reset-Mock {
    $script:releaseTestState = @{
        Exists = $true; Draft = $false; Prerelease = $true; Immutable = $false; Id = 7
        TagExists = $true; TagCommit = $commit; MainStatus = 'ahead'; MergeBase = $commit
        MoveTagOnUpload = $false; MoveTagOnDownload = $false; ReplaceOnRead = 0; ReleaseReads = 0
        FailUploadNumber = 0; UploadCount = 0; Assets = @{}; CorruptUpload = $false; DropUploads = $false
        HttpStatus = 404; HttpCalls = 0; PublishedPackage = ''; FailApi = $false; ExpectedPackageId = 'spdx.expressions'
        Calls = [Collections.Generic.List[object]]::new()
    }
}
# Substitutes shadow external commands in child scripts; unknown operations fail closed.
function gh {
    $call = @($args)
    $global:LASTEXITCODE = 0
    $releaseTestState.Calls.Add($call)
    if ($call[0] -ceq 'api') {
        Assert ($call.Count -eq 4 -and $call[2] -ceq '--method' -and $call[3] -ceq 'GET') 'Unexpected API operation.'
        if ($releaseTestState.FailApi) {
            $global:LASTEXITCODE = 1
            return
        }
        switch -Regex ($call[1]) {
            '\Arepos/DmitryGaravsky/Spdx.Expressions/commits/refs/tags/((?:compliance-)?v[0-9a-z.-]+)\z' {
                if (-not $releaseTestState.TagExists) {
                    $global:LASTEXITCODE = 1
                    return
                }
                return (@{ sha = $releaseTestState.TagCommit } | ConvertTo-Json)
            }
            '\Arepos/DmitryGaravsky/Spdx.Expressions/compare/[0-9a-f]{40}\.\.\.main\z' {
                return (@{ status = $releaseTestState.MainStatus; merge_base_commit = @{ sha = $releaseTestState.MergeBase } } | ConvertTo-Json)
            }
            '\Arepos/DmitryGaravsky/Spdx.Expressions/releases/tags/((?:compliance-)?v[0-9a-z.-]+)\z' {
                if (-not $releaseTestState.Exists) {
                    $global:LASTEXITCODE = 1
                    return
                }
                $releaseTestState.ReleaseReads++
                if ($releaseTestState.ReleaseReads -eq $releaseTestState.ReplaceOnRead) {
                    $releaseTestState.Id++
                }
                return (@{
                    id = $releaseTestState.Id; tag_name = $Matches[1]; draft = $releaseTestState.Draft
                    prerelease = $releaseTestState.Prerelease; immutable = $releaseTestState.Immutable
                    assets = @($releaseTestState.Assets.Keys | ForEach-Object { @{ name = $_ } })
                } | ConvertTo-Json -Depth 4)
            }
            default { throw 'Unexpected GitHub API endpoint.' }
        }
    }
    Assert ($call.Count -ge 4 -and $call[-2] -ceq '--repo' -and $call[-1] -ceq 'DmitryGaravsky/Spdx.Expressions') 'Unexpected publication destination.'
    Assert ($call[0] -ceq 'release') 'Only release operations are allowed in the substitute.'
    switch ($call[1]) {
        'upload' {
            Assert (-not $releaseTestState.Immutable -and -not $releaseTestState.Draft) 'Expected a mutable published release.'
            $releaseTestState.UploadCount++
            if ($releaseTestState.UploadCount -eq $releaseTestState.FailUploadNumber) {
                $global:LASTEXITCODE = 1
                return
            }
            $name = [IO.Path]::GetFileName($call[3])
            Assert (-not $releaseTestState.Assets.ContainsKey($name)) 'An asset was overwritten.'
            if (-not $releaseTestState.DropUploads) {
                $bytes = [IO.File]::ReadAllBytes($call[3])
                if ($releaseTestState.CorruptUpload) { $bytes[0] = $bytes[0] -bxor 1 }
                $releaseTestState.Assets[$name] = $bytes
            }
            if ($releaseTestState.MoveTagOnUpload) {
                $releaseTestState.TagCommit = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
            }
        }
        'download' {
            $name = $call[4]
            [IO.File]::WriteAllBytes((Join-Path $call[6] $name), $releaseTestState.Assets[$name])
            if ($releaseTestState.MoveTagOnDownload) {
                $releaseTestState.TagCommit = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
            }
        }
        default { throw "Unexpected GitHub CLI operation: $($call[1]); release text must remain unchanged." }
    }
}
function Invoke-WebRequest {
    param([string]$Uri, [string]$OutFile, [int]$TimeoutSec)
    $releaseTestState.HttpCalls++
    $id = $releaseTestState.ExpectedPackageId
    Assert ($Uri -ceq "https://api.nuget.org/v3-flatcontainer/$id/1.2.3-preview.1/$id.1.2.3-preview.1.nupkg") 'Unexpected package URL.'
    Assert ($TimeoutSec -gt 0 -and $TimeoutSec -le 60) 'Download must have a bounded timeout.'
    if ($releaseTestState.HttpStatus -ne 200) {
        $response = [Net.Http.HttpResponseMessage]::new([Net.HttpStatusCode]$releaseTestState.HttpStatus)
        throw [Microsoft.PowerShell.Commands.HttpResponseException]::new('Simulated HTTP failure', $response)
    }
    Copy-Item -LiteralPath $releaseTestState.PublishedPackage -Destination $OutFile
}
function Assert-NoMutation {
    Assert (@($releaseTestState.Calls | Where-Object { $_[0] -eq 'release' -and $_[1] -ne 'download' }).Count -eq 0) 'Unexpected remote mutation.'
}
$previousExitCode = Get-Variable LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue
$previousExitCodeValue = if ($null -ne $previousExitCode) { $previousExitCode.Value } else { $null }
try {
    foreach ($value in @('0.1.0','1.2.3-preview.1','1.2.3-rc.0','10.0.0','1.2.3-a-1')) {
        Assert ((& $getVersion -Tag "v$value") -ceq $value) 'Valid version rejected.'
    }
    $completed.Add('Stable and prerelease tags')
    foreach ($tag in @('', '1.2.3','V1.2.3','v','v01.2.3','v1.02.3','v1.2','v1.2.3.4','v1.2.3+build',
        'v1.2.3-RC.1','v1.2.3-01','v1.2.3-preview..1','v1.2.3/other',('v1.2.3' + [char]10))) {
        Reject { & $getVersion -Tag $tag } 'Release tags|Invalid version'
    }
    Reject { & $getVersion -Version '' } 'Invalid version'
    Reject { & $getVersion -Version '1.2.3' -Tag 'v1.2.3' } 'Specify Version or Tag'
    $completed.Add('Malformed tags and conflicting inputs rejected')
    $version = '1.2.3-preview.1'
    $fixture = New-Fixture 'valid'
    $package = Join-Path $fixture "Spdx.Expressions.$version.nupkg"
    & $validator -Version $version -Directory $fixture -Commit $commit 6>&1 | Out-Null
    $completed.Add('Valid assets, namespace-qualified nuspec and exact commit')
    Push-Location $fixture
    try { & $validator -Version $version -Directory . 6>&1 | Out-Null }
    finally { Pop-Location }
    $completed.Add('Relative asset paths follow the PowerShell location')
    $bad = Copy-Fixture 'tampered'
    [IO.File]::AppendAllText((Join-Path $bad "Spdx.Expressions.$version.nupkg"), 'changed')
    Reject { & $validator -Version $version -Directory $bad } 'checksum'
    $completed.Add('Package tampering rejected')
    $bad = Copy-Fixture 'checksum'
    [IO.File]::AppendAllText((Join-Path $bad 'SHA256SUMS'), 'unexpected line')
    Reject { & $validator -Version $version -Directory $bad } 'checksum'
    $completed.Add('Invalid checksum manifest rejected')
    $bad = Copy-Fixture 'extra-file'
    [IO.File]::WriteAllText((Join-Path $bad 'unexpected.txt'), 'extra')
    Reject { & $validator -Version $version -Directory $bad } 'Expected exactly'
    $completed.Add('Extra files rejected')
    $bad = Copy-Fixture 'extra-directory'
    [IO.Directory]::CreateDirectory((Join-Path $bad 'unexpected')) | Out-Null
    Reject { & $validator -Version $version -Directory $bad } 'Expected exactly'
    $completed.Add('Extra directories rejected')
    $bad = New-Fixture 'wrong-version' -MetadataVersion '9.9.9'
    Reject { & $validator -Version $version -Directory $bad } 'identity'
    $completed.Add('Nuspec version mismatch rejected despite valid checksum')
    $bad = New-Fixture 'wrong-repository' -RepositoryUrl 'https://example.invalid/other'
    Reject { & $validator -Version $version -Directory $bad } 'metadata'
    $completed.Add('Repository metadata mismatch rejected')
    $bad = New-Fixture 'missing-license' -OmitEntry 'LICENSE'
    Reject { & $validator -Version $version -Directory $bad } 'Missing release package entry'
    $completed.Add('Missing required package content rejected')
    $bad = New-Fixture 'wrong-commit' -MetadataCommit ('a' * 40)
    Reset-Mock
    Reject { & $publisher -Tag "v$version" -Commit $commit -Directory $bad } 'repository commit'
    Assert ($releaseTestState.Calls.Count -eq 0) 'Wrong package commit reached GitHub.'
    $completed.Add('Wrong package commit rejected before network')
    Reset-Mock
    & $publisher -Tag "v$version" -Commit $commit -Directory $fixture -CheckOnly 6>&1 | Out-Null
    Assert-NoMutation
    $completed.Add('Preflight accepts an empty published UI release without changes')
    & $publisher -Tag "v$version" -Commit $commit -Directory $fixture 6>&1 | Out-Null
    Assert ($releaseTestState.Assets.Count -eq 2) 'Published release did not receive both files.'
    $completed.Add('Published UI release receives verified files without editing text')
    $releaseTestState.Calls.Clear()
    & $publisher -Tag "v$version" -Commit $commit -Directory $fixture 6>&1 | Out-Null
    Assert-NoMutation
    $completed.Add('Matching published release is unchanged on retry')
    $releaseTestState.Immutable = $true
    & $publisher -Tag "v$version" -Commit $commit -Directory $fixture 6>&1 | Out-Null
    Assert-NoMutation
    $completed.Add('Complete immutable release permits a read-only retry')
    $releaseTestState.Immutable = $false
    $releaseTestState.Assets['SHA256SUMS'] = [Text.Encoding]::UTF8.GetBytes('different')
    Reject { & $publisher -Tag "v$version" -Commit $commit -Directory $fixture } 'asset differs'
    Assert-NoMutation
    $completed.Add('Conflicting remote bytes rejected without overwrite')
    Reset-Mock
    $releaseTestState.FailUploadNumber = 2
    Reject { & $publisher -Tag "v$version" -Commit $commit -Directory $fixture } 'GitHub CLI failed'
    Assert ($releaseTestState.Assets.Count -eq 1) 'Expected partial upload.'
    $releaseTestState.FailUploadNumber = 0
    & $publisher -Tag "v$version" -Commit $commit -Directory $fixture 6>&1 | Out-Null
    Assert ($releaseTestState.Assets.Count -eq 2) 'Partial upload retry did not finish.'
    $completed.Add('Partial published release upload can resume')
    foreach ($case in @(
        @{ Name='Missing tag'; Field='TagExists'; Value=$false; Pattern='Cannot read' },
        @{ Name='Moved tag'; Field='TagCommit'; Value=('a' * 40); Pattern='no longer points' },
        @{ Name='Commit outside main'; Field='MainStatus'; Value='diverged'; Pattern='belong to main' },
        @{ Name='Wrong merge base'; Field='MergeBase'; Value=('b' * 40); Pattern='belong to main' },
        @{ Name='Missing release'; Field='Exists'; Value=$false; Pattern='Cannot read' },
        @{ Name='Draft release'; Field='Draft'; Value=$true; Pattern='published release' },
        @{ Name='Wrong classification'; Field='Prerelease'; Value=$false; Pattern='classification' },
        @{ Name='Immutable incomplete release'; Field='Immutable'; Value=$true; Pattern='immutable release' },
        @{ Name='GitHub API failure'; Field='FailApi'; Value=$true; Pattern='Cannot read' }
    )) {
        Reset-Mock
        $releaseTestState[$case.Field] = $case.Value
        Reject { & $publisher -Tag "v$version" -Commit $commit -Directory $fixture -CheckOnly } $case.Pattern
        Assert-NoMutation
        $completed.Add($case.Name + ' rejected before publication')
    }
    Reset-Mock
    Reject { & $sourceCheck -Tag "v$version" -Commit 'main' } 'full commit SHA'
    Assert ($releaseTestState.Calls.Count -eq 0) 'Invalid commit reached GitHub.'
    $completed.Add('Non-SHA source rejected before network')
    Reset-Mock
    $releaseTestState.Assets['unexpected.txt'] = [byte[]]@(1)
    Reject { & $publisher -Tag "v$version" -Commit $commit -Directory $fixture } 'unexpected or duplicate assets'
    Assert-NoMutation
    $completed.Add('Unexpected remote asset rejected')
    Reset-Mock
    $releaseTestState.ReplaceOnRead = 2
    Reject { & $publisher -Tag "v$version" -Commit $commit -Directory $fixture } 'replaced'
    Assert-NoMutation
    $completed.Add('Replaced release rejected before upload')
    Reset-Mock
    $releaseTestState.Assets['SHA256SUMS'] = [IO.File]::ReadAllBytes((Join-Path $fixture 'SHA256SUMS'))
    $releaseTestState.MoveTagOnDownload = $true
    Reject { & $publisher -Tag "v$version" -Commit $commit -Directory $fixture } 'no longer points'
    Assert-NoMutation
    $completed.Add('Tag moved during remote verification prevents upload')
    Reset-Mock
    $releaseTestState.MoveTagOnUpload = $true
    Reject { & $publisher -Tag "v$version" -Commit $commit -Directory $fixture } 'no longer points'
    $completed.Add('Tag moved during upload cannot report success')
    Reset-Mock
    $releaseTestState.CorruptUpload = $true
    Reject { & $publisher -Tag "v$version" -Commit $commit -Directory $fixture } 'asset differs'
    $completed.Add('Uploaded bytes are downloaded and verified')
    Reset-Mock
    $releaseTestState.DropUploads = $true
    Reject { & $publisher -Tag "v$version" -Commit $commit -Directory $fixture } 'lost an asset'
    $completed.Add('Missing uploaded asset cannot report success')
    Reset-Mock
    $stable = New-Fixture 'stable' -Version '1.2.3'
    $releaseTestState.Prerelease = $false
    $releaseTestState.MainStatus = 'identical'
    & $publisher -Tag 'v1.2.3' -Commit $commit -Directory $stable 6>&1 | Out-Null
    $completed.Add('Stable release at main HEAD receives assets')
    & $compare -ExpectedPackage $package -ActualPackage $package 6>&1 | Out-Null
    $completed.Add('Identical NuGet package accepted')
    $signed = Copy-Fixture 'signed'
    $signedPackage = Change-Entry $signed '.signature.p7s' 'Independent signature placeholder' -Append
    & $compare -ExpectedPackage $package -ActualPackage $signedPackage 6>&1 | Out-Null
    $completed.Add('Repository signature does not change payload identity')
    foreach ($case in @(
        @{ Name='changed-payload'; Entry='README.md'; Text=('X' * 27); Pattern='bytes differ' },
        @{ Name='extra-payload'; Entry='extra.txt'; Text='extra'; Pattern='file set differs' },
        @{ Name='missing-payload'; Entry='LICENSE'; Text=''; Delete=$true; Pattern='file set differs' },
        @{ Name='duplicate-entry'; Entry='README.md'; Text='duplicate'; Append=$true; Pattern='Duplicate' },
        @{ Name='case-collision'; Entry='readme.md'; Text='duplicate'; Append=$true; Pattern='Duplicate' }
    )) {
        $bad = Copy-Fixture $case.Name
        $parameters = @{ Directory=$bad; Name=$case.Entry; Text=$case.Text }
        if ($case.ContainsKey('Delete')) { $parameters.Delete = $true }
        if ($case.ContainsKey('Append')) { $parameters.Append = $true }
        $badPackage = Change-Entry @parameters
        Reject { & $compare -ExpectedPackage $package -ActualPackage $badPackage } $case.Pattern
        $completed.Add('NuGet ' + $case.Name + ' rejected')
    }
    Reset-Mock
    Assert (-not (& $nugetCheck -Version $version -Directory $fixture -Commit $commit)) '404 must permit a first publication.'
    Assert ($releaseTestState.HttpCalls -eq 1) 'Expected one bounded request.'
    $completed.Add('NuGet 404 reports an absent version')
    $releaseTestState.HttpStatus = 503
    Reject { & $nugetCheck -Version $version -Directory $fixture -Commit $commit } 'Simulated HTTP failure'
    $completed.Add('NuGet service error does not permit publication')
    $releaseTestState.HttpStatus = 200
    $releaseTestState.PublishedPackage = $signedPackage
    Assert (& $nugetCheck -Version $version -Directory $fixture -Commit $commit) 'Matching existing package must skip publication.'
    $completed.Add('Existing matching NuGet version permits a safe retry')
    $releaseTestState.PublishedPackage = Join-Path $suite 'changed-payload/Spdx.Expressions.1.2.3-preview.1.nupkg'
    Reject { & $nugetCheck -Version $version -Directory $fixture -Commit $commit } 'bytes differ'
    $completed.Add('Conflicting existing NuGet version fails closed')
    $target = & "$PSScriptRoot/get-release-target.ps1" -Tag "compliance-v$version"
    Assert ($target.PackageId -ceq 'Spdx.Compliance' -and $target.Version -ceq $version -and $target.Directory -ceq "artifacts/release/compliance/$version") 'Wrong Compliance release target.'
    Reject { & $getVersion -Tag 'Compliance-v1.2.3' } 'Release tags'
    Reject { & $getVersion -Tag 'compliance-v1.2.3/other' } 'Invalid version'
    $compliance = New-Fixture 'compliance' -PackageId 'Spdx.Compliance'
    Reset-Mock
    Reject { & $publisher -Tag "compliance-v$version" -Commit $commit -Directory $fixture } 'exactly'
    Reject { & $publisher -Tag "v$version" -Commit $commit -Directory $compliance } 'exactly'
    Assert ($releaseTestState.Calls.Count -eq 0) 'Cross-package assets reached GitHub.'
    & $publisher -Tag "compliance-v$version" -Commit $commit -Directory $compliance 6>&1 | Out-Null
    Assert ($releaseTestState.Assets.Count -eq 2 -and $releaseTestState.Assets.ContainsKey("Spdx.Compliance.$version.nupkg")) 'Wrong Compliance release assets.'
    $releaseTestState.Immutable = $true
    & $sourceCheck -Tag "compliance-v$version" -Commit $commit | Out-Null
    $releaseTestState.ExpectedPackageId = 'spdx.compliance'
    $releaseTestState.HttpStatus = 200
    $releaseTestState.PublishedPackage = Join-Path $compliance "Spdx.Compliance.$version.nupkg"
    Assert (& $nugetCheck -PackageId Spdx.Compliance -Version $version -Directory $compliance -Commit $commit) 'Compliance retry did not find its package.'
    $completed.Add('Compliance tag, package routing, independent assets and publication retry')
    & {
        $reuse = Join-Path $PSScriptRoot 'reuse-ci-package.ps1'
        $repository = 'DmitryGaravsky/Spdx.Expressions'
        $ciRun = @{ id = 123; head_sha = $commit; head_branch = 'main'; event = 'push'; conclusion = 'success'
            status = 'completed'; head_repository = @{ full_name = $repository }; run_attempt = 2 }
        $artifacts = @(@{ name = 'verification-ubuntu-latest-1'; expired = $false }, @{ name = 'development-package-1'; expired = $false })
        $ciPackage = Join-Path $compliance "Spdx.Compliance.$version.nupkg"
        $report = @{ status = 'Passed'; version = $version; package = "packages/Spdx.Expressions.$version.nupkg"
            packageSha256 = (Get-FileHash $package).Hash.ToLowerInvariant()
            compliancePackage = "packages/Spdx.Compliance.$version.nupkg"; compliancePackageSha256 = (Get-FileHash $ciPackage).Hash.ToLowerInvariant() }
        function gh {
            $global:LASTEXITCODE = 0
            if ($args[0] -eq 'api') {
                if ($args[-1] -ceq "repos/$repository/actions/workflows/ci.yml/runs?branch=main&head_sha=$commit&status=success&per_page=100") {
                    return ConvertTo-Json -InputObject @(@{ workflow_runs = @($ciRun) }) -Depth 8
                }
                if ($args[-1] -ceq "repos/$repository/actions/runs/123/artifacts?per_page=100") {
                    return ConvertTo-Json -InputObject @(@{ artifacts = $artifacts }) -Depth 6
                }
                throw 'Unexpected CI API request.'
            }
            Assert (($args[0..8] -join ' ') -ceq "run download 123 --repo $repository --name development-package-1 --name verification-ubuntu-latest-1") 'Mixed CI run or attempt.'
            $download = $args[-1]
            $packages = [IO.Directory]::CreateDirectory("$download/development-package-1/run/packages")
            Copy-Item -LiteralPath $package, $ciPackage -Destination $packages.FullName
            $evidence = [IO.Directory]::CreateDirectory("$download/verification-ubuntu-latest-1")
            $report | ConvertTo-Json | Set-Content "$($evidence.FullName)/latest-verification.json"
        }
        function Check-Miss([string]$Tag, [string]$Reason) {
            $out = Join-Path $suite 'unused-ci-candidate'
            Assert (-not (& $reuse -Tag $Tag -Commit $commit -OutputDirectory $out 3>$null)) $Reason
            Assert (-not (Test-Path -LiteralPath $out)) 'Fallback left release files behind.'
            $completed.Add($Reason)
        }
        foreach ($tag in @("v$version", "compliance-v$version")) {
            $out = Join-Path $suite "reuse-$tag"
            Assert (& $reuse -Tag $tag -Commit $commit -OutputDirectory $out) 'Expected a verified match.'
            $expected = if ($tag.StartsWith('v')) { $package } else { $ciPackage }
            $copy = Join-Path $out ([IO.Path]::GetFileName($expected))
            Assert ((Get-FileHash $copy).Hash -ceq (Get-FileHash $expected).Hash) 'CI package bytes changed.'
            $completed.Add("Reuse verified Linux package for $tag across a partial CI rerun")
        }
        foreach ($case in @(@('event','pull_request'), @('head_branch','other'), @('head_sha',('a' * 40)), @('conclusion','failure'))) {
            $key, $value = $case
            $original = $ciRun[$key]
            $ciRun[$key] = $value
            Check-Miss "v$version" "Ignore CI run with $key=$value"
            $ciRun[$key] = $original
        }
        $ciRun.head_repository.full_name = 'fork/Spdx.Expressions'
        Check-Miss "v$version" 'Ignore fork CI artifacts'
        $ciRun.head_repository.full_name = $repository
        $artifacts[0].expired = $true
        Check-Miss "v$version" 'Rebuild when Linux evidence expired'
        $artifacts[0].expired = $false
        Check-Miss 'compliance-v0.1.1' 'Rebuild when the CI version differs from the tag'
        $report.compliancePackageSha256 = '0' * 64
        Check-Miss "compliance-v$version" 'Rebuild when package bytes differ from the CI SHA-256'
        $report.status = 'Failed'
        Check-Miss "v$version" 'Rebuild when Linux verification failed'
    }
    $result.status = 'Passed'
}
finally {
    if ($result.status -eq 'Running') { $result.status = 'Failed' }
    $result | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 (Join-Path $suite 'result.json')
    if ($null -eq $previousExitCode) {
        Remove-Variable LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue
    }
    else {
        $global:LASTEXITCODE = $previousExitCodeValue
    }
}
Write-Output "Release script tests passed: $($completed.Count) cases, no network or credentials."
