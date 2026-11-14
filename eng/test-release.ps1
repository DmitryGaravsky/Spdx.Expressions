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
function Assert([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw $Message
    }
}
function Reject([scriptblock]$Action) {
    $rejected = $false
    try { & $Action 6>&1 | Out-Null }
    catch { $rejected = $true }
    Assert $rejected 'Expected rejection did not occur.'
}
function New-Fixture([string]$Name, [string]$Version = '1.2.3-preview.1',
    [string]$MetadataVersion = '', [string]$RepositoryUrl = 'https://github.com/DmitryGaravsky/Spdx.Expressions',
    [string]$OmitEntry = '') {
    $directory = Join-Path $suite $Name
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    if (-not $MetadataVersion) {
        $MetadataVersion = $Version
    }
    $packageName = "Spdx.Expressions.$Version.nupkg"
    $archive = [IO.Compression.ZipFile]::Open((Join-Path $directory $packageName), 'Create')
    try {
        $writer = [IO.StreamWriter]::new($archive.CreateEntry('Spdx.Expressions.nuspec').Open())
        try {
            $writer.Write(@"
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata>
<id>Spdx.Expressions</id><version>$MetadataVersion</version><authors>Dmitry Garavsky</authors>
<license type="expression">MIT</license>
<projectUrl>https://github.com/DmitryGaravsky/Spdx.Expressions</projectUrl>
<repository type="git" url="$RepositoryUrl" />
</metadata></package>
"@)
        }
        finally { $writer.Dispose() }
        # Synthetic payloads test publication boundaries, not library behavior.
        foreach ($entry in @('lib/net10.0/Spdx.Expressions.dll', 'lib/net10.0/Spdx.Expressions.xml',
            'README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'data/manifest.json',
            'data/licenses.json', 'data/exceptions.json', 'data/UPSTREAM-README.md', 'data/UPSTREAM-XML-README.md')) {
            if ($entry -ceq $OmitEntry) {
                continue
            }
            $writer = [IO.StreamWriter]::new($archive.CreateEntry($entry).Open())
            try { $writer.Write('Independent release fixture') }
            finally { $writer.Dispose() }
        }
    }
    finally { $archive.Dispose() }
    $hash = (Get-FileHash -LiteralPath (Join-Path $directory $packageName)).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText((Join-Path $directory 'SHA256SUMS'), "$hash  $packageName" + [char]10)
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
function Reset-Mock {
    $script:releaseTestState = @{
        Exists = $false; Draft = $true; Prerelease = $true; FailCreate = $false
        TagExists = $true; RemoveTagOnUpload = $false
        FailUploadNumber = 0; UploadCount = 0; Assets = @{}
        Calls = [Collections.Generic.List[object]]::new()
    }
}
# This function shadows the executable in child scripts. Unknown operations fail closed.
function gh {
    $call = @($args)
    $global:LASTEXITCODE = 0
    $releaseTestState.Calls.Add($call)
    if ($call[0] -ceq 'api') {
        Assert ($call.Count -eq 5 -and $call[1] -cmatch '\Arepos/DmitryGaravsky/Spdx.Expressions/git/ref/tags/v[0-9a-z.-]+\z' -and
            $call[2] -ceq '--method' -and $call[3] -ceq 'GET' -and $call[4] -ceq '--silent') 'Unexpected API operation.'
        if (-not $releaseTestState.TagExists) {
            $global:LASTEXITCODE = 1
        }
        return
    }
    Assert ($call.Count -ge 4 -and $call[-2] -ceq '--repo' -and $call[-1] -ceq 'DmitryGaravsky/Spdx.Expressions') 'Unexpected publication destination.'
    Assert ($call[0] -ceq 'release') 'Only release operations are allowed in the substitute.'
    switch ($call[1]) {
        'view' {
            if (-not $releaseTestState.Exists) {
                $global:LASTEXITCODE = 1
                return
            }
            return (@{
                isDraft = $releaseTestState.Draft; isPrerelease = $releaseTestState.Prerelease
                assets = @($releaseTestState.Assets.Keys | ForEach-Object { @{ name = $_ } })
            } | ConvertTo-Json -Depth 4)
        }
        'create' {
            if ($releaseTestState.FailCreate) {
                $global:LASTEXITCODE = 1
                return
            }
            Assert ($call -contains '--draft' -and $call -contains '--verify-tag') 'Draft creation must verify an existing tag.'
            $releaseTestState.Exists = $true
            $releaseTestState.Draft = $true
            $releaseTestState.Prerelease = $call -contains '--prerelease'
        }
        'upload' {
            Assert $releaseTestState.Draft 'An upload targeted a published release.'
            $releaseTestState.UploadCount++
            if ($releaseTestState.UploadCount -eq $releaseTestState.FailUploadNumber) {
                $global:LASTEXITCODE = 1
                return
            }
            $name = [IO.Path]::GetFileName($call[3])
            Assert (-not $releaseTestState.Assets.ContainsKey($name)) 'An asset was overwritten.'
            $releaseTestState.Assets[$name] = [IO.File]::ReadAllBytes($call[3])
            if ($releaseTestState.RemoveTagOnUpload) {
                $releaseTestState.TagExists = $false
            }
        }
        'download' {
            $name = $call[4]
            [IO.File]::WriteAllBytes((Join-Path $call[6] $name), $releaseTestState.Assets[$name])
        }
        'edit' {
            Assert ($releaseTestState.Assets.Count -eq 2) 'Incomplete draft was published.'
            Assert ($call -contains '--draft=false' -and $call -contains '--verify-tag') 'Publishing must verify an existing tag.'
            if (-not $releaseTestState.TagExists) {
                $global:LASTEXITCODE = 1
                return
            }
            if ($releaseTestState.Prerelease) {
                Assert ($call -contains '--latest=false') 'Prerelease must not become latest.'
            }
            else {
                Assert ($call -notcontains '--latest=false') 'Stable release must use GitHub latest selection.'
            }
            $releaseTestState.Draft = $false
        }
        default { throw "Unexpected GitHub CLI operation: $($call[1])" }
    }
}
function Assert-NoMutation {
    $mutations = @($releaseTestState.Calls | Where-Object { $_[1] -in @('create','upload','edit') })
    Assert ($mutations.Count -eq 0) 'Unexpected remote mutation on retry or collision.'
}
$previousExitCode = Get-Variable LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue
$previousExitCodeValue = if ($null -ne $previousExitCode) { $previousExitCode.Value } else { $null }
try {
    foreach ($version in @('0.1.0','1.2.3-preview.1','1.2.3-rc.0','10.0.0','1.2.3-a-1')) {
        Assert ((& $getVersion -Tag "v$version") -ceq $version) 'Valid version rejected.'
    }
    $completed.Add('Stable and prerelease tags')
    foreach ($tag in @('', '1.2.3','V1.2.3','v','v01.2.3','v1.02.3','v1.2','v1.2.3.4','v1.2.3+build',
        'v1.2.3-RC.1','v1.2.3-01','v1.2.3-preview..1','v1.2.3/other',('v1.2.3' + [char]10))) {
        Reject { & $getVersion -Tag $tag }
    }
    Reject { & $getVersion -Version '' }
    Reject { & $getVersion -Version '1.2.3' -Tag 'v1.2.3' }
    $completed.Add('Malformed tags and conflicting inputs rejected')
    $version = '1.2.3-preview.1'
    $fixture = New-Fixture 'valid'
    & $validator -Version $version -Directory $fixture 6>&1 | Out-Null
    $completed.Add('Valid single-package assets and namespace-qualified nuspec')
    Push-Location $fixture
    try {
        & $validator -Version $version -Directory . 6>&1 | Out-Null
    }
    finally { Pop-Location }
    $completed.Add('Relative asset paths follow the PowerShell location')
    $bad = Copy-Fixture 'tampered'
    [IO.File]::AppendAllText((Join-Path $bad "Spdx.Expressions.$version.nupkg"), 'changed')
    Reject { & $validator -Version $version -Directory $bad }
    $completed.Add('Package tampering rejected')
    $bad = Copy-Fixture 'checksum'
    [IO.File]::AppendAllText((Join-Path $bad 'SHA256SUMS'), 'unexpected line')
    Reject { & $validator -Version $version -Directory $bad }
    $completed.Add('Invalid checksum manifest rejected')
    $bad = Copy-Fixture 'extra-file'
    [IO.File]::WriteAllText((Join-Path $bad 'unexpected.txt'), 'extra')
    Reject { & $validator -Version $version -Directory $bad }
    $completed.Add('Extra files rejected')
    $bad = Copy-Fixture 'extra-directory'
    [IO.Directory]::CreateDirectory((Join-Path $bad 'unexpected')) | Out-Null
    Reject { & $validator -Version $version -Directory $bad }
    $completed.Add('Extra directories rejected')
    $bad = New-Fixture 'wrong-version' -MetadataVersion '9.9.9'
    Reject { & $validator -Version $version -Directory $bad }
    $completed.Add('Nuspec version mismatch rejected despite valid checksum')
    $bad = New-Fixture 'wrong-repository' -RepositoryUrl 'https://example.invalid/other'
    Reject { & $validator -Version $version -Directory $bad }
    $completed.Add('Repository metadata mismatch rejected')
    $bad = New-Fixture 'missing-license' -OmitEntry 'LICENSE'
    Reject { & $validator -Version $version -Directory $bad }
    $completed.Add('Missing required package content rejected')
    Reset-Mock
    & $publisher -Tag "v$version" -Directory $fixture 6>&1 | Out-Null
    Assert (-not $releaseTestState.Draft -and $releaseTestState.Assets.Count -eq 2) 'Draft did not publish completely.'
    $completed.Add('Prerelease draft, assets and publication')
    $releaseTestState.Calls.Clear()
    & $publisher -Tag "v$version" -Directory $fixture 6>&1 | Out-Null
    Assert-NoMutation
    $completed.Add('Matching published release is unchanged on retry')
    $releaseTestState.Assets['SHA256SUMS'] = [Text.Encoding]::UTF8.GetBytes('different')
    $releaseTestState.Calls.Clear()
    Reject { & $publisher -Tag "v$version" -Directory $fixture }
    Assert-NoMutation
    $completed.Add('Conflicting remote bytes rejected without overwrite')
    Reset-Mock
    $releaseTestState.FailUploadNumber = 2
    Reject { & $publisher -Tag "v$version" -Directory $fixture }
    Assert ($releaseTestState.Draft -and $releaseTestState.Assets.Count -eq 1) 'Upload failure did not leave a partial draft.'
    $releaseTestState.FailUploadNumber = 0
    & $publisher -Tag "v$version" -Directory $fixture 6>&1 | Out-Null
    Assert (-not $releaseTestState.Draft) 'Partial draft retry did not finish.'
    $completed.Add('Partial upload failure and draft recovery')
    $releaseTestState.Assets.Remove('SHA256SUMS')
    $releaseTestState.Calls.Clear()
    Reject { & $publisher -Tag "v$version" -Directory $fixture }
    Assert-NoMutation
    $completed.Add('Published release missing an asset rejected')
    Reset-Mock
    $releaseTestState.Exists = $true
    $releaseTestState.Prerelease = $false
    Reject { & $publisher -Tag "v$version" -Directory $fixture }
    Assert-NoMutation
    $completed.Add('Prerelease classification mismatch rejected')
    Reset-Mock
    $releaseTestState.Exists = $true
    $releaseTestState.Assets['unexpected.txt'] = [byte[]]@(1)
    Reject { & $publisher -Tag "v$version" -Directory $fixture }
    Assert-NoMutation
    $completed.Add('Unexpected remote asset rejected')
    Reset-Mock
    $releaseTestState.FailCreate = $true
    Reject { & $publisher -Tag "v$version" -Directory $fixture }
    Assert (@($releaseTestState.Calls | Where-Object { $_[1] -in @('upload','edit') }).Count -eq 0) 'Failed create continued publication.'
    $completed.Add('Failed draft creation stops publication')
    Reset-Mock
    $bad = Copy-Fixture 'publisher-tampered'
    [IO.File]::AppendAllText((Join-Path $bad "Spdx.Expressions.$version.nupkg"), 'changed')
    Reject { & $publisher -Tag "v$version" -Directory $bad }
    Assert ($releaseTestState.Calls.Count -eq 0) 'Invalid local package reached the GitHub CLI.'
    $completed.Add('Invalid local assets rejected before GitHub invocation')
    Reset-Mock
    $stable = New-Fixture 'stable' -Version '1.2.3'
    & $publisher -Tag 'v1.2.3' -Directory $stable 6>&1 | Out-Null
    Assert (-not $releaseTestState.Prerelease -and -not $releaseTestState.Draft) 'Stable release classification failed.'
    $completed.Add('Stable release publication flags')
    Reset-Mock
    $releaseTestState.Exists = $true
    $releaseTestState.TagExists = $false
    Reject { & $publisher -Tag "v$version" -Directory $fixture }
    Assert-NoMutation
    $completed.Add('Existing draft with a missing or unreadable tag is rejected')
    Reset-Mock
    $releaseTestState.TagExists = $false
    Reject { & $publisher -Tag "v$version" -Directory $fixture }
    Assert-NoMutation
    $completed.Add('Missing or unreadable tag prevents draft creation')
    Reset-Mock
    $releaseTestState.RemoveTagOnUpload = $true
    Reject { & $publisher -Tag "v$version" -Directory $fixture }
    Assert ($releaseTestState.Draft) 'Draft published after its tag disappeared.'
    Assert (@($releaseTestState.Calls | Where-Object { $_[1] -eq 'edit' -and $_ -contains '--verify-tag' }).Count -eq 1) 'Final publication did not verify the tag.'
    $completed.Add('Tag disappearance during upload prevents publication')
    $result.status = 'Passed'
}
finally {
    if ($result.status -eq 'Running') {
        $result.status = 'Failed'
    }
    $result | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 (Join-Path $suite 'result.json')
    if ($null -eq $previousExitCode) {
        Remove-Variable LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue
    }
    else {
        $global:LASTEXITCODE = $previousExitCodeValue
    }
}
Write-Output "Release script tests passed: $($completed.Count) cases, no network or credentials."
