# Usage and examples

[Overview](../README.md) · [API](api.md) · [Syntax](syntax.md) · [Catalogue](catalogue.md)

## Run the examples

From a source checkout with the .NET 10 SDK selected by global.json:

```powershell
dotnet run --project Spdx.Expressions.Examples -- quick-start
dotnet run --project Spdx.Expressions.Examples -- syntax-error
```

Replace the final argument with any scenario name below. Omitting it runs `quick-start`.
The [example source](../Spdx.Expressions.Examples/Program.cs) uses only the public API.
Each C# block is a complete console program; the verification pipeline checks it against
the corresponding source region, compiles each block independently against the actual nupkg,
and compares both the runner and standalone program output with the text shown here.
The [quick-start program](../README.md#get-started) covers successful parsing and validation.

## Use a local package

This project is an unpublished development package. From a checkout, use PowerShell 7 to
build and verify a local package:

```powershell
./eng/verify.ps1
$run = Get-Content ./artifacts/latest-verification.json -Raw | ConvertFrom-Json
if ($run.status -ne 'Passed') { throw 'Package verification did not pass.' }
$package = (Resolve-Path -LiteralPath $run.package).Path
$feed = Split-Path $package -Parent
$version = $run.version
```

Create a console application in a new directory outside the checkout. Keep the variables
above in the same PowerShell session; replace the example destination with your own new path:

```powershell
$app = Join-Path ([IO.Path]::GetTempPath()) ('SpdxExample-' + [Guid]::NewGuid().ToString('N'))
dotnet new console --name SpdxExample --framework net10.0 --output $app --no-restore
dotnet add "$app/SpdxExample.csproj" package Spdx.Expressions --version "[$version]" --source $feed
```

Replace Program.cs with the [quick-start code](../README.md#get-started), then run:

```powershell
dotnet run --project "$app/SpdxExample.csproj" --no-restore
```

The exact PackageReference version comes from the verified package. The `--source` argument
selects its local feed; no published package is required. Library execution is offline;
building the repository may need development dependencies. See [Testing](testing.md) for
local-feed restore and isolated SDK execution. Ordinary commands above use your normal SDK
environment; repository verification runs the documented isolation guards.

## Handle a syntax error

Scenario: `syntax-error`. The missing operand is reported at end of input. A zero-length span is valid here; it marks
an insertion position, not a substring to underline.

<!-- example: syntax-error -->
```csharp
using System;
using Spdx.Expressions;

var parsed = SpdxExpressionParser.Parse("MIT AND");
Console.WriteLine($"Parse: {parsed.Status}");
foreach(var diagnostic in parsed.Diagnostics)
    Console.WriteLine($"{diagnostic.Code}: start={diagnostic.Span.Start}, length={diagnostic.Span.Length}");
```

Expected output:

```text
Parse: InvalidSyntax
SPDX_PARSE_EXPECTED_OPERAND: start=7, length=0
```

## Distinguish syntax from catalogue membership

Scenario: `unknown-id`. Identifier-shaped tokens can parse even when the pinned catalogue does not contain them.
Use validation flags for outcomes and codes for machine-readable diagnostics.

<!-- example: unknown-id -->
```csharp
using System;
using Spdx.Expressions;

var parsed = SpdxExpressionParser.Parse("Unknown-1.0");
Console.WriteLine($"Parse: {parsed.Status}");
if(parsed.Expression is null)
    return;
var validation = SpdxExpressionValidator.Validate(parsed.Expression, SpdxLicenseRegistry.Default);
Console.WriteLine($"Identifier errors: {validation.HasIdentifierErrors}");
foreach(var diagnostic in validation.Diagnostics)
    Console.WriteLine($"{diagnostic.Severity}: {diagnostic.Code}");
```

Expected output:

```text
Parse: Success
Identifier errors: True
Error: SPDX_ID_UNKNOWN_LICENSE
```

## Keep references unresolved

Scenario: `reference`. A custom reference is not an unknown official identifier. Its presence requires separate
handling by your application; the library does not look up or resolve its target.

<!-- example: reference -->
```csharp
using System;
using Spdx.Expressions;

var parsed = SpdxExpressionParser.Parse("LicenseRef-X");
if(parsed.Expression is null)
    return;
var validation = SpdxExpressionValidator.Validate(parsed.Expression, SpdxLicenseRegistry.Default);
Console.WriteLine($"Identifier errors: {validation.HasIdentifierErrors}");
Console.WriteLine($"Unresolved references: {validation.HasUnresolvedReferences}");
foreach(var reference in validation.References)
    Console.WriteLine($"License component: {reference.LicenseId}");
```

Expected output:

```text
Identifier errors: False
Unresolved references: True
License component: X
```

## Report deprecation warnings

Scenario: `deprecated`. The adjacent plus is retained, while validation checks the base GPL-2.0 identifier.
Deprecation produces a warning, without setting HasIdentifierErrors. Formatting does not
upgrade or replace deprecated spellings.

<!-- example: deprecated -->
```csharp
using System;
using Spdx.Expressions;

var parsed = SpdxExpressionParser.Parse("GPL-2.0+");
if(parsed.Expression is null)
    return;
var validation = SpdxExpressionValidator.Validate(parsed.Expression, SpdxLicenseRegistry.Default);
Console.WriteLine($"Identifier errors: {validation.HasIdentifierErrors}");
foreach(var diagnostic in validation.Diagnostics)
    Console.WriteLine($"{diagnostic.Severity}: {diagnostic.Code}");
Console.WriteLine($"Formatted: {SpdxExpressionFormatter.Format(parsed.Expression)}");
```

Expected output:

```text
Identifier errors: False
Warning: SPDX_ID_DEPRECATED
Formatted: GPL-2.0+
```

## Read original source spans

Scenario: `source-spans`. Lookup supplies canonical spelling independently of the original source. Span offsets
refer to the original string, including its leading whitespace.

<!-- example: source-spans -->
```csharp
using System;
using Spdx.Expressions;

var parsed = SpdxExpressionParser.Parse("  mit OR Apache-2.0 ");
if(parsed.Expression is null)
    return;
var expression = parsed.Expression;
var validation = SpdxExpressionValidator.Validate(expression, SpdxLicenseRegistry.Default);
foreach(var occurrence in validation.IdentifierOccurrences) {
    var span = occurrence.Span;
    string source = expression.SourceText.Substring(span.Start, span.Length);
    Console.WriteLine($"{span.Start}:{span.Length} '{source}' -> {occurrence.CanonicalIdentifier}");
}
```

Expected output:

```text
2:3 'mit' -> MIT
9:10 'Apache-2.0' -> Apache-2.0
```

## Format without rewriting identifiers

Scenario: `formatting`. Formatting changes spacing while keeping explicit groups, lowercase spelling and order.
It does not need a registry and does not modify SourceText.

<!-- example: formatting -->
```csharp
using System;
using Spdx.Expressions;

var parsed = SpdxExpressionParser.Parse(" ( mit  OR Apache-2.0 ) AND MIT ");
if(parsed.Expression is null)
    return;
Console.WriteLine($"Original: '{parsed.Expression.SourceText}'");
Console.WriteLine($"Formatted: '{SpdxExpressionFormatter.Format(parsed.Expression)}'");
```

Expected output:

```text
Original: ' ( mit  OR Apache-2.0 ) AND MIT '
Formatted: '(mit OR Apache-2.0) AND MIT'
```

## Diagnostic storage does not limit evidence

Scenario: `diagnostic-budget`. With one stored message, the first deprecation warning fills the budget. The later unknown
identifier and reference are still collected and reflected in the independent flags.
Do not infer success from the severity of the stored messages alone.

<!-- example: diagnostic-budget -->
```csharp
using System;
using Spdx.Expressions;

var parsed = SpdxExpressionParser.Parse("GPL-2.0 OR GPL-2.0 OR Unknown-1.0 OR LicenseRef-X");
if(parsed.Expression is null)
    return;
var options = new SpdxValidationOptions { MaxDiagnostics = 1 };
var validation = SpdxExpressionValidator.Validate(parsed.Expression, SpdxLicenseRegistry.Default, options);
Console.WriteLine($"Identifier errors: {validation.HasIdentifierErrors}");
Console.WriteLine($"Unresolved references: {validation.HasUnresolvedReferences}");
Console.WriteLine($"Diagnostics truncated: {validation.DiagnosticsTruncated}");
Console.WriteLine($"Identifiers: {validation.IdentifierOccurrences.Count}; references: {validation.References.Count}");
foreach(var diagnostic in validation.Diagnostics)
    Console.WriteLine($"{diagnostic.Severity}: {diagnostic.Code}");
```

Expected output:

```text
Identifier errors: True
Unresolved references: True
Diagnostics truncated: True
Identifiers: 3; references: 1
Warning: SPDX_ID_DEPRECATED
```

## Check the exception namespace

Scenario: `exception-kind`. WITH requires an exception identifier. Apache-2.0 is known as a license, so it
produces WrongKind in the exception position. Knowing both identifiers does not establish
that a license/exception combination is legally applicable.

<!-- example: exception-kind -->
```csharp
using System;
using Spdx.Expressions;

foreach(string input in new[] { "MIT WITH LLVM-exception", "MIT WITH Apache-2.0" }) {
    var parsed = SpdxExpressionParser.Parse(input);
    if(parsed.Expression is null)
        return;
    var validation = SpdxExpressionValidator.Validate(parsed.Expression, SpdxLicenseRegistry.Default);
    Console.WriteLine($"{input}: identifier errors={validation.HasIdentifierErrors}");
    foreach(var occurrence in validation.IdentifierOccurrences)
        Console.WriteLine($"{occurrence.Role}: {occurrence.Identifier} -> {occurrence.LookupStatus}");
    foreach(var diagnostic in validation.Diagnostics)
        Console.WriteLine(diagnostic.Code);
}
```

Expected output:

```text
MIT WITH LLVM-exception: identifier errors=False
License: MIT -> Known
Exception: LLVM-exception -> Known
MIT WITH Apache-2.0: identifier errors=True
License: MIT -> Known
Exception: Apache-2.0 -> WrongKind
SPDX_ID_WRONG_KIND
```

## Handle a parser budget

Scenario: `parse-limit`. MIT is syntactically valid, but exceeds the configured input budget. LimitExceeded
is distinct from InvalidSyntax and exposes no partial expression. Invalid option values
(such as MaxInputLength = 0) instead throw an argument exception.

<!-- example: parse-limit -->
```csharp
using System;
using Spdx.Expressions;

var options = new SpdxParseOptions { MaxInputLength = 2 };
var parsed = SpdxExpressionParser.Parse("MIT", options);
Console.WriteLine($"Parse: {parsed.Status}");
Console.WriteLine($"Expression available: {parsed.Expression is not null}");
foreach(var diagnostic in parsed.Diagnostics)
    Console.WriteLine(diagnostic.Code);
```

Expected output:

```text
Parse: LimitExceeded
Expression available: False
SPDX_PARSE_LIMIT_EXCEEDED
```

## Handle cancellation separately

Scenario: `cancellation`. This already-cancelled token makes the example deterministic. In an application,
pass the operation's cancellation token; Parse, Validate and Format all accept one.
Cancellation throws rather than returning InvalidSyntax or a partial result.

<!-- example: cancellation -->
```csharp
using System;
using Spdx.Expressions;

try {
    var parsed = SpdxExpressionParser.Parse("MIT", cancellationToken: new(true));
    Console.WriteLine($"Parse: {parsed.Status}");
}
catch(OperationCanceledException) {
    Console.WriteLine("Cancelled: no parse result");
}
```

Expected output:

```text
Cancelled: no parse result
```
