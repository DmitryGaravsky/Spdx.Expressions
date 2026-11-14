#Requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Package, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. "$PSScriptRoot/Invoke-Checked.ps1"
. "$PSScriptRoot/PathState.ps1"
. "$PSScriptRoot/Documentation.ps1"
$packagePath = (Resolve-Path -LiteralPath $Package).Path
$packageHash = (Get-FileHash -LiteralPath $packagePath).Hash.ToLowerInvariant()
$archive = [IO.Compression.ZipFile]::OpenRead($packagePath)
try {
    $specifications = @($archive.Entries | Where-Object { $_.FullName.EndsWith('.nuspec', [StringComparison]::Ordinal) })
    if ($specifications.Count -ne 1) { throw 'Expected exactly one package specification.' }
    $stream = $specifications[0].Open()
    try { $specification = [Xml.Linq.XDocument]::Load($stream) }
    finally { $stream.Dispose() }
    $metadata = $specification.Root.Element($specification.Root.Name.Namespace + 'metadata')
    $packageId = $metadata.Element($metadata.Name.Namespace + 'id').Value
    $packageVersion = $metadata.Element($metadata.Name.Namespace + 'version').Value
    if ($packageId -cne 'Spdx.Expressions' -or [string]::IsNullOrWhiteSpace($packageVersion)) {
        throw 'Expected a versioned Spdx.Expressions package.'
    }
}
finally { $archive.Dispose() }
$root = Split-Path $PSScriptRoot -Parent
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root ('artifacts/package-smoke-' + [Guid]::NewGuid().ToString('N')) }
$output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw "Refusing to reuse verification evidence directory: $output" }
[IO.Directory]::CreateDirectory($output) | Out-Null
$workspace = Join-Path ([IO.Path]::GetTempPath()) ('Spdx.Expressions-consumer-' + [Guid]::NewGuid().ToString('N'))
$pathBefore = Get-PathState
$result = [ordered]@{
    package = $packagePath; version = $packageVersion; sha256 = $packageHash
    workspace = $workspace; passed = $false; status = 'Running'; utc = [DateTime]::UtcNow.ToString('O')
}
$result | ConvertTo-Json | Set-Content -Encoding utf8 "$output/result.json"
try {
    foreach ($directory in @($workspace, "$workspace/feed", "$workspace/project", "$workspace/packages", "$workspace/http-cache", "$workspace/cli-home")) {
        [IO.Directory]::CreateDirectory($directory) | Out-Null
    }
    $result.cliHomeInitiallyEmpty = (@(Get-ChildItem -LiteralPath "$workspace/cli-home" -Force).Count -eq 0)
    $copiedPackage = Join-Path "$workspace/feed" ([IO.Path]::GetFileName($packagePath))
    Copy-Item -LiteralPath $packagePath -Destination $copiedPackage
    if ((Get-FileHash -LiteralPath $copiedPackage).Hash.ToLowerInvariant() -ne $packageHash) { throw 'Package changed while copying.' }
    Copy-Item -LiteralPath "$root/global.json" -Destination "$workspace/global.json"
    Copy-Item -LiteralPath "$PSScriptRoot/package-consumer/Program.cs" -Destination "$workspace/project/Program.cs"
    $escapedVersion = [Security.SecurityElement]::Escape($packageVersion)
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable><ImplicitUsings>disable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors><NuGetAudit>false</NuGetAudit>
  </PropertyGroup>
  <ItemGroup><PackageReference Include="Spdx.Expressions" Version="[$escapedVersion]" /></ItemGroup>
</Project>
"@ | Set-Content -Encoding utf8 "$workspace/project/Consumer.csproj"
    $feed = [Security.SecurityElement]::Escape("$workspace/feed")
    "<configuration><packageSources><clear/><add key=`"local`" value=`"$feed`"/></packageSources></configuration>" |
        Set-Content -Encoding utf8 "$workspace/NuGet.Config"
    # Invoke-Checked applies all SDK guards to every child process.
    $environment = @{
        NUGET_PACKAGES = "$workspace/packages"; NUGET_HTTP_CACHE_PATH = "$workspace/http-cache"
        DOTNET_CLI_HOME = "$workspace/cli-home"; NUGET_PLUGIN_PATHS = ''
    }
    Invoke-Checked -Arguments @('--version') -WorkingDirectory "$workspace/project" -Environment $environment -LogBase "$output/sdk-version"
    Invoke-Checked -Arguments @('msbuild', "$PSScriptRoot/SdkIsolation.proj", '-nologo') -WorkingDirectory "$workspace/project" -Environment $environment -LogBase "$output/sdk-environment"
    Invoke-Checked -Arguments @('restore', 'Consumer.csproj', '--configfile', "$workspace/NuGet.Config", '--no-http-cache', '-p:NuGetAudit=false') -WorkingDirectory "$workspace/project" -Environment $environment -LogBase "$output/restore"
    Invoke-Checked -Arguments @('build', 'Consumer.csproj', '-c', 'Release', '--no-restore', '-m:1') -WorkingDirectory "$workspace/project" -Environment $environment -LogBase "$output/build"
    Invoke-Checked -Arguments @('bin/Release/net10.0/Consumer.dll', $copiedPackage) -WorkingDirectory "$workspace/project" -Environment $environment -LogBase "$output/run" -TimeoutSeconds 30
    Test-PackageReadme -Root $root -Package $copiedPackage
    Test-DocumentExamples -Root $root
    [IO.Directory]::CreateDirectory("$workspace/examples") | Out-Null
    Copy-Item -LiteralPath "$root/Spdx.Expressions.Examples/Program.cs" -Destination "$workspace/examples/Program.cs"
    Copy-Item -LiteralPath "$workspace/project/Consumer.csproj" -Destination "$workspace/examples/Examples.csproj"
    Invoke-Checked -Arguments @('restore', 'Examples.csproj', '--configfile', "$workspace/NuGet.Config", '--no-http-cache', '-p:NuGetAudit=false') -WorkingDirectory "$workspace/examples" -Environment $environment -LogBase "$output/examples-restore"
    Invoke-Checked -Arguments @('build', 'Examples.csproj', '-c', 'Release', '--no-restore', '-m:1') -WorkingDirectory "$workspace/examples" -Environment $environment -LogBase "$output/examples-build"
    $examples = @(Get-DocumentExamples $root)
    foreach ($example in $examples) {
        $log = "$output/example-$($example.Name)"
        Invoke-Checked -Arguments @('bin/Release/net10.0/Examples.dll', $example.Name) -WorkingDirectory "$workspace/examples" -Environment $environment -LogBase $log -TimeoutSeconds 30
        $actual = [IO.File]::ReadAllText("$log.stdout.log").Replace("`r`n", "`n")
        $errorOutput = [IO.File]::ReadAllText("$log.stderr.log")
        Test-ExampleOutput -Example $example -Output $actual -ErrorOutput $errorOutput
    }
    $result.examplesPassed = $examples.Count
    # Compile each documented program alone so helpers/usings in the example runner cannot hide omissions.
    [IO.Directory]::CreateDirectory("$workspace/snippet") | Out-Null
    Copy-Item -LiteralPath "$workspace/project/Consumer.csproj" -Destination "$workspace/snippet/Snippet.csproj"
    Invoke-Checked -Arguments @('restore', 'Snippet.csproj', '--configfile', "$workspace/NuGet.Config", '--no-http-cache', '-p:NuGetAudit=false') -WorkingDirectory "$workspace/snippet" -Environment $environment -LogBase "$output/snippet-restore"
    foreach ($example in $examples) {
        [IO.File]::WriteAllText("$workspace/snippet/Program.cs", $example.Code)
        $log = "$output/snippet-$($example.Name)"
        Invoke-Checked -Arguments @('build', 'Snippet.csproj', '-c', 'Release', '--no-restore', '-m:1') -WorkingDirectory "$workspace/snippet" -Environment $environment -LogBase "$log-build"
        Invoke-Checked -Arguments @('bin/Release/net10.0/Snippet.dll') -WorkingDirectory "$workspace/snippet" -Environment $environment -LogBase "$log-run" -TimeoutSeconds 30
        $actual = [IO.File]::ReadAllText("$log-run.stdout.log")
        $errorOutput = [IO.File]::ReadAllText("$log-run.stderr.log")
        Test-ExampleOutput -Example $example -Output $actual -ErrorOutput $errorOutput
    }
    $result.standaloneSnippetsPassed = $examples.Count
    if ((Get-FileHash -LiteralPath $packagePath).Hash.ToLowerInvariant() -ne $packageHash) { throw 'Original package changed during verification.' }
    $result.passed = $true
    $result.status = 'Passed'
}
finally {
    $pathAfter = Get-PathState
    $pathUnchanged = Test-PathStateEqual -Before $pathBefore -After $pathAfter
    [ordered]@{ before = $pathBefore; after = $pathAfter; unchanged = $pathUnchanged } |
        ConvertTo-Json -Depth 6 | Set-Content -Encoding utf8 "$output/path-state.json"
    $result.pathUnchanged = $pathUnchanged
    $result.persistentPathCheck = if ($IsWindows) { 'RawRegistryValuesAndKinds' } else { 'NotApplicable' }
    if (-not $pathUnchanged) {
        $result.passed = $false
        $result.status = 'Failed'
    }
    if ($result.status -eq 'Running') { $result.status = 'Failed' }
    $result.utc = [DateTime]::UtcNow.ToString('O')
    $result | ConvertTo-Json | Set-Content -Encoding utf8 "$output/result.json"
    if (-not $pathUnchanged) {
        throw 'Package consumer changed PATH; see path-state.json.'
    }
}
