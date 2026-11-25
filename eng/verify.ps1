#Requires -Version 7.0
[CmdletBinding()]
param([string]$RestoreSource, [string]$Version, [string]$OutputDirectory,
    [ValidateSet('Spdx.Expressions', 'Spdx.Compliance', IgnoreCase = $false)][string]$PackageId = 'Spdx.Expressions',
    [string]$DependencySource)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. "$PSScriptRoot/Invoke-Checked.ps1"
. "$PSScriptRoot/PathState.ps1"
$root = Split-Path $PSScriptRoot -Parent
$runId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '-' + [Guid]::NewGuid().ToString('N')
$checkCompliance = $PackageId -ceq 'Spdx.Compliance' -or
    (-not $PSBoundParameters.ContainsKey('PackageId') -and -not $PSBoundParameters.ContainsKey('Version'))
$expressionsVersion = & "$PSScriptRoot/get-version.ps1"
[xml]$project = Get-Content -LiteralPath "$root/Spdx.Compliance/Spdx.Compliance.csproj" -Raw
$complianceVersion = & "$PSScriptRoot/get-version.ps1" -Version $project.Project.PropertyGroup.ComplianceVersion.InnerText
if ($PackageId -ceq 'Spdx.Compliance') {
    if ($PSBoundParameters.ContainsKey('Version')) { $complianceVersion = & "$PSScriptRoot/get-version.ps1" -Version $Version }
    $Version = $complianceVersion
}
else {
    if ($PSBoundParameters.ContainsKey('Version')) { $expressionsVersion = & "$PSScriptRoot/get-version.ps1" -Version $Version }
    $Version = $expressionsVersion
}
$versionProperties = @("-p:Version=$expressionsVersion", "-p:ComplianceVersion=$complianceVersion")
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
    runDirectory = $runRelative; packageId = $PackageId; version = $Version; status = 'Running'; package = $null; packageSha256 = $null
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
    $restore = @('restore', 'Spdx.Expressions.slnx', '-m:1', '--configfile', 'NuGet.Config', '-p:NuGetAudit=false') + $versionProperties
    if ($RestoreSource) { $restore += @('--source', $RestoreSource) }
    Invoke-Checked -Arguments $restore -Environment $sdkEnvironment -LogBase "$runDirectory/checks/restore"
    foreach ($configuration in 'Debug', 'Release') {
        Invoke-Checked -Arguments (@('test', 'Spdx.Expressions.Tests/Spdx.Expressions.Tests.csproj', '--no-restore', '-m:1', '-c', $configuration,
            '--logger', "trx;LogFileName=$configuration.trx", '--results-directory', "$runDirectory/test-results") + $versionProperties) -Environment $sdkEnvironment -LogBase "$runDirectory/checks/$configuration" -TimeoutSeconds 60
        Invoke-Checked -Arguments (@('test', 'Spdx.Compliance.Tests/Spdx.Compliance.Tests.csproj', '--no-restore', '-m:1', '-c', $configuration,
            '--logger', "trx;LogFileName=Compliance-$configuration.trx", '--results-directory', "$runDirectory/test-results") + $versionProperties) -Environment $sdkEnvironment -LogBase "$runDirectory/checks/compliance-$configuration" -TimeoutSeconds 60
    }
    Invoke-Checked -Arguments (@('build', 'Spdx.Compliance.Examples/Spdx.Compliance.Examples.csproj', '-c', 'Release', '--no-restore', '-m:1') + $versionProperties) -Environment $sdkEnvironment -LogBase "$runDirectory/checks/compliance-examples-build"
    Invoke-Checked -Arguments @('run', '--project', 'Spdx.Compliance.Examples/Spdx.Compliance.Examples.csproj', '-c', 'Release', '--no-build', '--no-restore') -Environment $sdkEnvironment -LogBase "$runDirectory/checks/compliance-examples"
    Invoke-Checked -Arguments (@('build', 'Spdx.Expressions.Examples/Spdx.Expressions.Examples.csproj', '-c', 'Release', '--no-restore', '-m:1') + $versionProperties) -Environment $sdkEnvironment -LogBase "$runDirectory/checks/examples-build"
    Invoke-Checked -Arguments @('format', 'whitespace', 'Spdx.Expressions.slnx', '--verify-no-changes', '--no-restore') -Environment $sdkEnvironment -LogBase "$runDirectory/checks/format"
    Invoke-Checked -Arguments @('format', 'whitespace', 'eng/package-consumer', '--folder', '--verify-no-changes') -Environment $sdkEnvironment -LogBase "$runDirectory/checks/format-consumer"
    Invoke-Checked -Arguments (@('pack', 'Spdx.Expressions/Spdx.Expressions.csproj', '-c', 'Release', '--no-build', '--no-restore', '-o', "$runDirectory/packages") + $versionProperties) -Environment $sdkEnvironment -LogBase "$runDirectory/checks/pack"
    $packages = @(Get-ChildItem -LiteralPath "$runDirectory/packages" -Filter '*.nupkg' -File)
    if ($packages.Count -ne 1) { throw 'Expected exactly one newly packed library artifact.' }
    $summary.package = "$runRelative/packages/$($packages[0].Name)"
    $summary.packageSha256 = (Get-FileHash -LiteralPath $packages[0].FullName).Hash.ToLowerInvariant()
    & "$PSScriptRoot/check-docs.ps1" -Package $packages[0].FullName | Tee-Object -FilePath "$runDirectory/package-documentation.log"
    & "$PSScriptRoot/package-smoke.ps1" -Package $packages[0].FullName -OutputDirectory "$runDirectory/package-smoke"
    if ($checkCompliance) {
        Invoke-Checked -Arguments (@('pack', 'Spdx.Compliance/Spdx.Compliance.csproj', '-c', 'Release', '--no-build', '--no-restore', '-o', "$runDirectory/packages") + $versionProperties) -Environment $sdkEnvironment -LogBase "$runDirectory/checks/compliance-pack"
        $name = "Spdx.Compliance.$complianceVersion.nupkg"
        $package = Join-Path "$runDirectory/packages" $name
        if (-not $DependencySource) { $DependencySource = $RestoreSource }
        if (-not $DependencySource) { $DependencySource = 'https://api.nuget.org/v3/index.json' }
        $consumer = @{ Package = $package; Version = $complianceVersion; DependencySource = $DependencySource
            OutputDirectory = "$runDirectory/compliance-smoke" }
        if ($PackageId -cne 'Spdx.Compliance') { $consumer.ExpressionsPackage = $packages[0].FullName }
        & "$PSScriptRoot/compliance-package-smoke.ps1" @consumer
        $summary.compliancePackage = "$runRelative/packages/$name"
        $summary.compliancePackageSha256 = (Get-FileHash -LiteralPath $package).Hash.ToLowerInvariant()
        if ($PackageId -ceq 'Spdx.Compliance') {
            $summary.package = $summary.compliancePackage
            $summary.packageSha256 = $summary.compliancePackageSha256
        }
    }
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
