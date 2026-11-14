#Requires -Version 7.0
[CmdletBinding()]
param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. "$PSScriptRoot/Documentation.ps1"
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path (Split-Path $PSScriptRoot -Parent) ('artifacts/documentation-tests/' + [Guid]::NewGuid().ToString('N'))
}
$suite = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
if (Test-Path -LiteralPath $suite) {
    throw "Refusing to overwrite documentation test evidence: $suite"
}
[IO.Directory]::CreateDirectory("$suite/docs") | Out-Null
[IO.Directory]::CreateDirectory("$suite/Spdx.Expressions.Examples") | Out-Null
$cases = [Collections.Generic.List[string]]::new()
function Reject([string]$Name, [scriptblock]$Action, [string]$ExpectedMessage) {
    $rejected = $false
    try { & $Action | Out-Null }
    catch {
        if ($_.Exception.Message -notlike $ExpectedMessage) {
            throw
        }
        $rejected = $true
    }
    if (-not $rejected) { throw "Expected documentation rejection: $Name" }
    $cases.Add($Name)
}
$result = [ordered]@{ status = 'Running'; cases = $cases }
try {
    foreach ($file in 'AGENTS.md', 'CONTRIBUTING.md', 'THIRD-PARTY-NOTICES.md') {
        [IO.File]::WriteAllText("$suite/$file", "# Fixture`n")
    }
    [IO.File]::WriteAllText("$suite/docs/guide.md", "# Guide`n`n## A heading`n")
    $valid = "# Fixture`n`n[Guide](docs/guide.md#a-heading)`n"
    [IO.File]::WriteAllText("$suite/README.md", $valid)
    Test-DocumentLinks $suite
    $cases.Add('Valid file and heading')
    foreach ($target in 'docs/missing.md', 'docs/guide.md#missing', 'docs/Guide.md', '../outside.md') {
        [IO.File]::WriteAllText("$suite/README.md", "[Invalid]($target)`n")
        $expectedMessage = if ($target.Contains('#')) { '*documentation heading*' } else { '*documentation target*' }
        Reject "Reject $target" { Test-DocumentLinks $suite } $expectedMessage
    }
    [IO.File]::WriteAllText("$suite/README.md", $valid)
    $package = "$suite/fixture.nupkg"
    function Write-FixturePackage([string]$Text, [string]$Commit = '') {
        $archive = [IO.Compression.ZipFile]::Open($package, 'Update')
        try {
            $entry = $archive.GetEntry('README.md')
            if ($null -ne $entry) { $entry.Delete() }
            $writer = [IO.StreamWriter]::new($archive.CreateEntry('README.md').Open())
            try { $writer.Write($Text) }
            finally { $writer.Dispose() }
            $entry = $archive.GetEntry('Spdx.Expressions.nuspec')
            if ($null -ne $entry) { $entry.Delete() }
            $writer = [IO.StreamWriter]::new($archive.CreateEntry('Spdx.Expressions.nuspec').Open())
            try { $writer.Write("<package><metadata><repository commit=`"$Commit`" /></metadata></package>") }
            finally { $writer.Dispose() }
        }
        finally { $archive.Dispose() }
    }
    Write-FixturePackage ($valid.Replace('(docs/', '(https://github.com/DmitryGaravsky/Spdx.Expressions/blob/main/docs/'))
    Test-PackageReadme -Root $suite -Package $package
    $cases.Add('Portable package README')
    Write-FixturePackage $valid
    Reject 'Relative package link' { Test-PackageReadme -Root $suite -Package $package } '*non-portable link*'
    Write-FixturePackage ($valid.Replace('(docs/', '(https://github.com/DmitryGaravsky/Spdx.Expressions/blob/main/docs/').Replace('# Fixture', '# Changed'))
    Reject 'Changed package prose' { Test-PackageReadme -Root $suite -Package $package } '*changed content*'
    $commit = '0123456789abcdef0123456789abcdef01234567'
    $portable = $valid.Replace('(docs/', "(https://github.com/DmitryGaravsky/Spdx.Expressions/blob/$commit/docs/")
    Write-FixturePackage $portable $commit
    Test-PackageReadme -Root $suite -Package $package
    $cases.Add('Package commit matches links')
    Write-FixturePackage ($portable.Replace($commit, 'main')) $commit
    Reject 'Main instead of package commit' { Test-PackageReadme -Root $suite -Package $package } '*target differs*'
    Write-FixturePackage ($portable.Replace($commit, ('f' * 40))) $commit
    Reject 'Wrong package commit' { Test-PackageReadme -Root $suite -Package $package } '*target differs*'
    $code = 'Console.WriteLine("Hello");'
    $sample = "<!-- example: hello -->`n" + '```csharp' + "`nusing System;`nusing Spdx.Expressions;`n`n$code`n" + '```' + "`n`nExpected output:`n`n" + '```text' + "`nHello`n" + '```' + "`n"
    [IO.File]::WriteAllText("$suite/README.md", $sample)
    [IO.File]::WriteAllText("$suite/docs/usage.md", "# Usage`n")
    [IO.File]::WriteAllText("$suite/Spdx.Expressions.Examples/Program.cs", "            #region hello`n            $code`n            #endregion`n")
    Test-DocumentExamples $suite
    $cases.Add('Matching runnable example')
    [IO.File]::WriteAllText("$suite/README.md", $sample.Replace('WriteLine', 'Write'))
    Reject 'Changed documented code' { Test-DocumentExamples $suite } '*code differs*'
    [IO.File]::WriteAllText("$suite/README.md", $sample.Replace('Expected output:', 'Output omitted:'))
    Reject 'Missing expected output' { Test-DocumentExamples $suite } '*unpaired documented example*'
    [IO.File]::WriteAllText("$suite/README.md", $sample + "`n<!-- example: unfinished -->`n")
    Reject 'Extra incomplete example' { Test-DocumentExamples $suite } '*unpaired documented example*'
    [IO.File]::WriteAllText("$suite/README.md", $sample + "`n" + '```csharp' + "`n$code`n" + '```')
    Reject 'Unmarked program' { Test-DocumentExamples $suite } '*unpaired documented example*'
    [IO.File]::WriteAllText("$suite/README.md", $sample.Replace('Expected output:', 'Missing:') + "`n" + $sample.Replace('example: hello', 'example: second'))
    Reject 'Missing output cannot borrow next block' { Test-DocumentExamples $suite } '*unpaired documented example*'
    $example = [pscustomobject]@{ Name = 'hello'; Output = 'Hello' }
    Test-ExampleOutput -Example $example -Output "Hello`r`n"
    $cases.Add('CRLF output')
    Reject 'Changed output' { Test-ExampleOutput -Example $example -Output "Wrong`n" } '*output differs*'
    Reject 'Extra output' { Test-ExampleOutput -Example $example -Output "Hello`nExtra`n" } '*output differs*'
    Reject 'Unexpected stderr' { Test-ExampleOutput -Example $example -Output "Hello`n" -ErrorOutput 'Failure' } '*output differs*'
    $result.status = 'Passed'
}
finally {
    if ($result.status -eq 'Running') { $result.status = 'Failed' }
    $result | ConvertTo-Json | Set-Content -Encoding utf8 "$suite/result.json"
}
Write-Output "Documentation fixtures: $($cases.Count) scenarios passed."
