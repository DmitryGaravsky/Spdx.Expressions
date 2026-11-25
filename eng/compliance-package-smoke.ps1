#Requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Package, [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$DependencySource, [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$ExpressionsPackage)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. "$PSScriptRoot/Invoke-Checked.ps1"
$root = Split-Path $PSScriptRoot -Parent
$Version = & "$PSScriptRoot/get-version.ps1" -Version $Version
$output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw "Refusing to reuse package smoke output: $output" }
[void][IO.Directory]::CreateDirectory($output)
$workspace = Join-Path ([IO.Path]::GetTempPath()) ('Spdx.Compliance-consumer-' + [Guid]::NewGuid().ToString('N'))
$feed = [IO.Directory]::CreateDirectory("$workspace/feed").FullName
$name = "Spdx.Compliance.$Version.nupkg"
Copy-Item -LiteralPath $Package -Destination (Join-Path $feed $name)
$localMapping = '<package pattern="Spdx.Compliance"/>'
if ($ExpressionsPackage) {
    Copy-Item -LiteralPath $ExpressionsPackage -Destination $feed
    $localMapping += '<package pattern="Spdx.Expressions"/>'
}
if (Test-Path -LiteralPath $DependencySource -PathType Container) {
    $DependencySource = (Resolve-Path -LiteralPath $DependencySource).ProviderPath
}
$local = [Security.SecurityElement]::Escape($feed)
$dependencies = [Security.SecurityElement]::Escape($DependencySource)
@"
<configuration>
  <packageSources><clear/><add key="candidate" value="$local"/><add key="dependencies" value="$dependencies"/></packageSources>
  <packageSourceMapping>
    <packageSource key="candidate">$localMapping</packageSource>
    <packageSource key="dependencies"><package pattern="*"/></packageSource>
  </packageSourceMapping>
</configuration>
"@ | Set-Content -Encoding utf8 "$workspace/NuGet.Config"
Copy-Item -LiteralPath "$root/global.json" -Destination $workspace
Copy-Item -LiteralPath "$root/Spdx.Compliance.Examples/Program.cs" -Destination $workspace
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable><ImplicitUsings>disable</ImplicitUsings><TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup><PackageReference Include="Spdx.Compliance" Version="[$Version]"/></ItemGroup>
</Project>
"@ | Set-Content -Encoding utf8 "$workspace/Consumer.csproj"
$options = @{ WorkingDirectory = $workspace; Environment = @{
    DOTNET_CLI_HOME = "$workspace/cli-home"; NUGET_PACKAGES = "$workspace/packages"; NUGET_HTTP_CACHE_PATH = "$workspace/http-cache"
} }
Invoke-Checked @options -Arguments @('restore', 'Consumer.csproj', '--configfile', "$workspace/NuGet.Config", '--no-http-cache', '-p:NuGetAudit=false') -LogBase "$output/restore"
Invoke-Checked @options -Arguments @('build', 'Consumer.csproj', '-c', 'Release', '--no-restore') -LogBase "$output/build"
$assembly = "$workspace/bin/Release/net10.0/Spdx.Compliance.dll"
$info = [Diagnostics.FileVersionInfo]::GetVersionInfo($assembly)
if ($info.ProductVersion.Split('+')[0] -cne $Version) { throw "Consumer assembly version differs from $Version." }
$hash = (Get-FileHash -LiteralPath $Package).Hash
$installed = "$workspace/packages/spdx.compliance/$Version/$($name.ToLowerInvariant())"
if ((Get-FileHash -LiteralPath $installed).Hash -cne $hash) { throw 'Restored package differs from the packed candidate.' }
$archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $Package).ProviderPath)
try {
    $stream = $archive.GetEntry('lib/net10.0/Spdx.Compliance.dll').Open()
    try { $dllHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
    finally { $stream.Dispose() }
}
finally { $archive.Dispose() }
if ((Get-FileHash -LiteralPath $assembly).Hash -cne $dllHash) { throw 'Consumer DLL differs from the candidate.' }
Invoke-Checked @options -Arguments @('bin/Release/net10.0/Consumer.dll') -LogBase "$output/run"
[ordered]@{ package = $name; sha256 = $hash.ToLowerInvariant(); assemblySha256 = $dllHash.ToLowerInvariant()
    version = $Version; workspace = $workspace; passed = $true } | ConvertTo-Json | Set-Content -Encoding utf8 "$output/result.json"
