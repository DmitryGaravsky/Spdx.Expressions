#Requires -Version 7.0
# Helpers shared by repository documentation checks and isolated package examples.
function Get-DocumentExamples {
    param([string]$Root)
    foreach ($file in 'README.md', 'docs/usage.md') {
        $text = [IO.File]::ReadAllText((Join-Path $Root $file)).Replace("`r`n", "`n")
        $pattern = '(?m)^<!-- example: ([a-z-]+) -->\n```csharp\n((?:(?!^```)[\s\S])*?)\n```\n\nExpected output:\n\n```text\n((?:(?!^```)[\s\S])*?)\n```[ \t]*$'
        $matches = [regex]::Matches($text, $pattern)
        $markers = [regex]::Matches($text, '<!--\s*example\b')
        $programs = [regex]::Matches($text, '(?m)^```csharp\s*$')
        if ($markers.Count -ne $matches.Count -or $programs.Count -ne $matches.Count) {
            throw "Malformed or unpaired documented example: $file"
        }
        foreach ($match in $matches) {
            [pscustomobject]@{ Name = $match.Groups[1].Value; Code = $match.Groups[2].Value; Output = $match.Groups[3].Value }
        }
    }
}

function Get-DocumentLinks {
    param([string]$Text)
    # Ignore fenced code; examples and scripts can contain Markdown-looking literals.
    $prose = [regex]::Replace($Text, '(?ms)^```[^\n]*\n.*?^```\s*$', '')
    foreach ($match in [regex]::Matches($prose, '\[[^\]]*\]\(([^)]+)\)')) {
        $match.Groups[1].Value
    }
}

function Test-ExampleOutput {
    param([object]$Example, [string]$Output, [string]$ErrorOutput = '')
    if ($Output.Replace("`r`n", "`n") -cne ($Example.Output + "`n") -or $ErrorOutput.Length -ne 0) {
        throw "Example output differs from documentation: $($Example.Name)"
    }
}

function Test-DocumentLinks {
    param([string]$Root)
    $files = @('README.md', 'AGENTS.md', 'CONTRIBUTING.md', 'THIRD-PARTY-NOTICES.md')
    $files += Get-ChildItem -LiteralPath "$Root/docs" -Filter '*.md' -File | ForEach-Object { "docs/$($_.Name)" }
    $count = 0
    foreach ($file in $files) {
        $text = [IO.File]::ReadAllText((Join-Path $Root $file))
        foreach ($link in Get-DocumentLinks $text) {
            if ($link -match '^[a-zA-Z][a-zA-Z0-9+.-]*:') {
                continue
            }
            $parts = $link.Split('#', 2)
            $relative = [Uri]::UnescapeDataString($parts[0])
            $target = if ($relative) { [IO.Path]::GetFullPath((Join-Path (Split-Path (Join-Path $Root $file) -Parent) $relative)) } else { Join-Path $Root $file }
            $repositoryPath = [IO.Path]::GetRelativePath($Root, $target).Replace('\', '/')
            if ($repositoryPath.StartsWith('../', [StringComparison]::Ordinal) -or -not (Test-Path -LiteralPath $target -PathType Leaf)) {
                throw "Missing or external documentation target: $file -> $link"
            }
            # Match exact path casing even on Windows, so Linux and GitHub links agree.
            $current = $Root
            foreach ($component in $repositoryPath.Split('/')) {
                $names = @(Get-ChildItem -LiteralPath $current -Force | Select-Object -ExpandProperty Name)
                if ($component -cnotin $names) {
                    throw "Documentation target casing differs: $file -> $link"
                }
                $current = Join-Path $current $component
            }
            if ($parts.Count -eq 2 -and $parts[1]) {
                $headings = [Collections.Generic.List[string]]::new()
                $content = [IO.File]::ReadAllText($target)
                $content = [regex]::Replace($content, '(?ms)^```[^\n]*\n.*?^```\s*$', '')
                foreach ($heading in [regex]::Matches($content, '(?m)^#{1,6} (.+?)\r?$')) {
                    $slug = $heading.Groups[1].Value.ToLowerInvariant()
                    $slug = [regex]::Replace($slug, '[^\p{L}\p{N}_\- ]', '').Replace(' ', '-')
                    $candidate = $slug
                    $suffix = 0
                    while ($headings.Contains($candidate)) {
                        $suffix++
                        $candidate = "$slug-$suffix"
                    }
                    $headings.Add($candidate)
                }
                if ([Uri]::UnescapeDataString($parts[1]) -cnotin $headings) {
                    throw "Missing documentation heading: $file -> $link"
                }
            }
            $count++
        }
    }
    Write-Output "Documentation: $count relative file/heading links passed."
}

function Test-DocumentExamples {
    param([string]$Root)
    $source = [IO.File]::ReadAllText("$Root/Spdx.Expressions.Examples/Program.cs").Replace("`r`n", "`n")
    $examples = @(Get-DocumentExamples $Root)
    $regions = [regex]::Matches($source, '(?ms)^\s*#region ([a-z-]+)\n(.*?)^\s*#endregion')
    $helpers = [regex]::Matches($source, '(?ms)^ *#region helper ([a-z-]+)\n(.*?)^ *#endregion')
    foreach ($helper in $helpers) {
        if ($helper.Groups[1].Value -cnotin $examples.Name) {
            throw "Helper region has no documented example: $($helper.Groups[1].Value)"
        }
    }
    if ($examples.Count -eq 0 -or $regions.Count -ne $examples.Count -or
        @($examples.Name | Select-Object -Unique).Count -ne $examples.Count) {
        throw 'Every example must have one source region, one documented program and one expected output.'
    }
    foreach ($example in $examples) {
        $region = @($regions | Where-Object { $_.Groups[1].Value -ceq $example.Name })
        if ($region.Count -ne 1) {
            throw "Missing or duplicated example region: $($example.Name)"
        }
        $body = [regex]::Replace($region[0].Groups[2].Value.TrimEnd(), '(?m)^ {12}', '')
        $helper = @($helpers | Where-Object { $_.Groups[1].Value -ceq $example.Name })
        if ($helper.Count -gt 1) {
            throw "Duplicate helper region: $($example.Name)"
        }
        if ($helper.Count -eq 1) {
            $helperCode = [regex]::Replace($helper[0].Groups[2].Value.TrimEnd(), '(?m)^ {8}', '')
            $body += "`n`n$helperCode"
        }
        $expectedCode = "using System;`nusing Spdx.Expressions;`n`n$body"
        if ($example.Code -cne $expectedCode) {
            throw "Documented code differs from runnable source: $($example.Name)"
        }
    }
    Write-Output "Documentation: $($examples.Count) example source blocks passed."
}

function Test-PackageReadme {
    param([string]$Root, [string]$Package)
    $archive = [IO.Compression.ZipFile]::OpenRead($Package)
    try {
        $stream = $archive.GetEntry('Spdx.Expressions.nuspec').Open()
        try { $specification = [Xml.Linq.XDocument]::Load($stream) }
        finally { $stream.Dispose() }
        $ns = $specification.Root.Name.Namespace
        $repositoryMetadata = $specification.Root.Element($ns + 'metadata').Element($ns + 'repository')
        $commit = $repositoryMetadata.Attribute('commit')
        $expectedRevision = if ($null -ne $commit -and $commit.Value) { $commit.Value } else { 'main' }
        $reader = [IO.StreamReader]::new($archive.GetEntry('README.md').Open())
        try { $packed = $reader.ReadToEnd() }
        finally { $reader.Dispose() }
        $source = [IO.File]::ReadAllText("$Root/README.md")
        $sourceLinks = @(Get-DocumentLinks $source)
        $packedLinks = @(Get-DocumentLinks $packed)
        if ($sourceLinks.Count -ne $packedLinks.Count) {
            throw 'Package README link count differs from the authored README.'
        }
        $repository = 'https://github.com/DmitryGaravsky/Spdx.Expressions/blob/'
        for ($i = 0; $i -lt $sourceLinks.Count; $i++) {
            $original = $sourceLinks[$i]
            $actual = $packedLinks[$i]
            if ($original -match '^(#|[a-zA-Z][a-zA-Z0-9+.-]*:)') {
                if ($original -cne $actual) { throw "Changed external or local-anchor link: $original" }
                continue
            }
            if (-not $actual.StartsWith($repository, [StringComparison]::Ordinal)) {
                throw "Package README has a non-portable link: $actual"
            }
            $tail = $actual.Substring($repository.Length).Split('/', 2)
            if ($tail.Count -ne 2 -or $tail[0] -cne $expectedRevision -or $tail[1] -cne $original) {
                throw "Package README target differs: $original -> $actual"
            }
            $packed = $packed.Replace("]($actual)", "]($original)")
        }
        if ($packed -cne $source) {
            throw 'Package README changed content beyond repository-link expansion.'
        }
    }
    finally { $archive.Dispose() }
    Write-Output 'Package README content and portable links passed.'
}
