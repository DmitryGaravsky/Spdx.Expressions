#Requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Tag, [Parameter(Mandatory)][string]$Commit,
    [Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$target = & "$PSScriptRoot/get-release-target.ps1" -Tag $Tag
if ($Commit -cnotmatch '\A[0-9a-f]{40}\z') { throw 'Expected the full release commit SHA.' }
$output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw "Refusing to overwrite release files: $output" }
$repository = 'DmitryGaravsky/Spdx.Expressions'
$name = "$($target.PackageId).$($target.Version).nupkg"
function Read-Pages([string]$Path) {
    $json = & gh api --paginate --slurp "repos/$repository/$Path"
    if ($LASTEXITCODE -ne 0) { throw "GitHub artifact lookup failed: $Path" }
    ($json -join [char]10) | ConvertFrom-Json
}
function Find-Candidate {
    $pages = Read-Pages "actions/workflows/ci.yml/runs?branch=main&head_sha=$Commit&status=success&per_page=100"
    foreach ($page in $pages) {
        foreach ($run in $page.workflow_runs) {
            if ($run.head_sha -cne $Commit -or $run.head_branch -cne 'main' -or
                $run.event -cnotin @('push', 'workflow_dispatch') -or $run.status -cne 'completed' -or
                $run.conclusion -cne 'success' -or $run.head_repository.full_name -ne $repository) { continue }
            $inventory = Read-Pages "actions/runs/$($run.id)/artifacts?per_page=100"
            $available = @($inventory | ForEach-Object { $_.artifacts } | Where-Object { -not $_.expired } | ForEach-Object { $_.name })
            # A partial rerun can retain the successful Linux job from a previous attempt.
            for ($attempt = [int]$run.run_attempt; $attempt -ge 1; $attempt--) {
                $bundle = "development-package-$attempt"
                $evidence = "verification-ubuntu-latest-$attempt"
                if ($bundle -cnotin $available -or $evidence -cnotin $available) { continue }
                $work = Join-Path ([IO.Path]::GetTempPath()) ('spdx-ci-' + [Guid]::NewGuid().ToString('N'))
                try {
                    & gh run download $run.id --repo $repository --name $bundle --name $evidence --dir $work | Out-Null
                    if ($LASTEXITCODE -ne 0) { throw 'Artifact download failed.' }
                    $report = Get-Content -LiteralPath "$work/$evidence/latest-verification.json" -Raw | ConvertFrom-Json -AsHashtable
                    $pathKey = if ($target.PackageId -ceq 'Spdx.Compliance') { 'compliancePackage' } else { 'package' }
                    $hash = $report["${pathKey}Sha256"]
                    if ($report.status -cne 'Passed' -or [IO.Path]::GetFileName([string]$report[$pathKey]) -cne $name -or
                        $hash -cnotmatch '\A[0-9a-f]{64}\z') { throw "No passing CI record for $name." }
                    $packages = @(Get-ChildItem -LiteralPath "$work/$bundle" -Filter $name -File -Recurse | Where-Object Name -CEQ $name)
                    if ($packages.Count -ne 1) { throw "Expected exactly one $name in the artifact." }
                    $stage = [IO.Directory]::CreateDirectory("$work/release").FullName
                    Copy-Item -LiteralPath $packages[0].FullName -Destination $stage
                    [IO.File]::WriteAllText("$stage/SHA256SUMS", "$hash  $name" + [char]10)
                    & "$PSScriptRoot/verify-release-assets.ps1" -PackageId $target.PackageId -Version $target.Version -Commit $Commit -Directory $stage
                    Write-Host "Reusing $name ($hash) from CI run $($run.id), Linux attempt $attempt."
                    return $stage
                }
                catch {
                    Write-Warning "Skipping CI run $($run.id), attempt ${attempt}: $($_.Exception.Message)"
                }
            }
        }
    }
}
try { $candidate = Find-Candidate }
catch {
    Write-Warning "CI lookup unavailable: $($_.Exception.Message)"
    $candidate = $null
}
if (-not $candidate) {
    Write-Host 'No matching CI package. Run the release verification matrix.'
    return $false
}
$null = New-Item -ItemType Directory -Path $output -ErrorAction Stop
Copy-Item -LiteralPath "$candidate/$name", "$candidate/SHA256SUMS" -Destination $output
& "$PSScriptRoot/verify-release-assets.ps1" -PackageId $target.PackageId -Version $target.Version -Commit $Commit -Directory $output
return $true
