#Requires -Version 7.0
[CmdletBinding()]
param([string]$RestoreSource, [string]$Version, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. "$PSScriptRoot/Invoke-Checked.ps1"
. "$PSScriptRoot/PathState.ps1"
$root = Split-Path $PSScriptRoot -Parent
$runId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '-' + [Guid]::NewGuid().ToString('N')
$versionArguments = @{}
if ($PSBoundParameters.ContainsKey('Version')) {
    $versionArguments.Version = $Version
}
$Version = & "$PSScriptRoot/get-version.ps1" @versionArguments
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $root "artifacts/verification/$runId"
}
$runDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
if (Test-Path -LiteralPath $runDirectory) {
    throw "Verification output already exists: $runDirectory"
}
$runRelative = [IO.Path]::GetRelativePath($root, $runDirectory).Replace('\', '/')
[IO.Directory]::CreateDirectory($runDirectory) | Out-Null
$pathBefore = Get-PathState
$cliHome = Join-Path $runDirectory 'cli-home'
[IO.Directory]::CreateDirectory($cliHome) | Out-Null
$sdkEnvironment = @{ DOTNET_CLI_HOME = $cliHome }
$summary = [ordered]@{
    cliHome = "$runRelative/cli-home"; cliHomeInitiallyEmpty = (@(Get-ChildItem -LiteralPath $cliHome -Force).Count -eq 0)
    runDirectory = $runRelative; version = $Version; status = 'Running'; package = $null; packageSha256 = $null
    startedUtc = [DateTime]::UtcNow.ToString('O'); finishedUtc = $null
}
$summary | ConvertTo-Json | Set-Content -Encoding utf8 "$runDirectory/run.json"
Push-Location $root
try {
    & "$PSScriptRoot/check-docs.ps1" | Tee-Object -FilePath "$runDirectory/documentation.log"
    & "$PSScriptRoot/test-docs.ps1" -OutputDirectory "$runDirectory/documentation-tests"
    & "$PSScriptRoot/test-release.ps1" -OutputDirectory "$runDirectory/release-tests"
    Invoke-Checked -Arguments @('--version') -Environment $sdkEnvironment -LogBase "$runDirectory/checks/sdk-version"
    Invoke-Checked -Arguments @('msbuild', 'eng/SdkIsolation.proj', '-nologo') -Environment $sdkEnvironment -LogBase "$runDirectory/checks/sdk-environment"
    Invoke-Checked -Arguments @('msbuild', 'eng/test-package-readme.proj', '-nologo', "-p:OutputDirectory=$runDirectory/readme-generator") -Environment $sdkEnvironment -LogBase "$runDirectory/checks/readme-generator"
    $restore = @('restore', 'Spdx.Expressions.slnx', '-m:1', '--configfile', 'NuGet.Config', '-p:NuGetAudit=false', "-p:Version=$Version")
    if ($RestoreSource) { $restore += @('--source', $RestoreSource) }
    Invoke-Checked -Arguments $restore -Environment $sdkEnvironment -LogBase "$runDirectory/checks/restore"
    foreach ($configuration in 'Debug', 'Release') {
        Invoke-Checked -Arguments @('test', 'Spdx.Expressions.Tests/Spdx.Expressions.Tests.csproj', '--no-restore', '-m:1', '-c', $configuration, "-p:Version=$Version",
            '--logger', "trx;LogFileName=$configuration.trx", '--results-directory', "$runDirectory/test-results") -Environment $sdkEnvironment -LogBase "$runDirectory/checks/$configuration" -TimeoutSeconds 60
    }
    Invoke-Checked -Arguments @('build', 'Spdx.Expressions.Examples/Spdx.Expressions.Examples.csproj', '-c', 'Release', '--no-restore', '-m:1', "-p:Version=$Version") -Environment $sdkEnvironment -LogBase "$runDirectory/checks/examples-build"
    Invoke-Checked -Arguments @('format', 'whitespace', 'Spdx.Expressions.slnx', '--verify-no-changes', '--no-restore') -Environment $sdkEnvironment -LogBase "$runDirectory/checks/format"
    Invoke-Checked -Arguments @('format', 'whitespace', 'eng/package-consumer', '--folder', '--verify-no-changes') -Environment $sdkEnvironment -LogBase "$runDirectory/checks/format-consumer"
    Invoke-Checked -Arguments @('pack', 'Spdx.Expressions/Spdx.Expressions.csproj', '-c', 'Release', '--no-build', '--no-restore', "-p:Version=$Version", '-o', "$runDirectory/packages") -Environment $sdkEnvironment -LogBase "$runDirectory/checks/pack"
    $packages = @(Get-ChildItem -LiteralPath "$runDirectory/packages" -Filter '*.nupkg' -File)
    if ($packages.Count -ne 1) { throw 'Expected exactly one newly packed library artifact.' }
    $summary.package = "$runRelative/packages/$($packages[0].Name)"
    $summary.packageSha256 = (Get-FileHash -LiteralPath $packages[0].FullName).Hash.ToLowerInvariant()
    & "$PSScriptRoot/check-docs.ps1" -Package $packages[0].FullName | Tee-Object -FilePath "$runDirectory/package-documentation.log"
    & "$PSScriptRoot/package-smoke.ps1" -Package $packages[0].FullName -OutputDirectory "$runDirectory/package-smoke"
    $summary.status = 'Passed'
}
finally {
    $pathAfter = Get-PathState
    $pathUnchanged = Test-PathStateEqual -Before $pathBefore -After $pathAfter
    [ordered]@{ before = $pathBefore; after = $pathAfter; unchanged = $pathUnchanged } |
        ConvertTo-Json -Depth 6 | Set-Content -Encoding utf8 "$runDirectory/path-state.json"
    $summary.pathUnchanged = $pathUnchanged
    $summary.persistentPathCheck = if ($IsWindows) { 'RawRegistryValuesAndKinds' } else { 'NotApplicable' }
    if (-not $pathUnchanged) {
        $summary.status = 'Failed'
    }
    if ($summary.status -eq 'Running') { $summary.status = 'Failed' }
    $summary.finishedUtc = [DateTime]::UtcNow.ToString('O')
    $summary | ConvertTo-Json | Set-Content -Encoding utf8 "$runDirectory/run.json"
    # This pointer may change; the uniquely named run, package and evidence never do.
    $summary | ConvertTo-Json | Set-Content -Encoding utf8 "$root/artifacts/latest-verification.json"
    Pop-Location
    if (-not $pathUnchanged) {
        throw 'Verification changed PATH; see path-state.json.'
    }
}
Write-Output "Verification evidence: $runDirectory"
