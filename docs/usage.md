# Usage and examples

[Overview](../README.md) · [API](api.md) · [Syntax](syntax.md) · [Catalogue](catalogue.md)

## Install from NuGet

The library is available as [Spdx.Expressions on NuGet.org](https://www.nuget.org/packages/Spdx.Expressions/)
and targets net10.0 only. With the .NET 10 SDK, create a console application and install the package:

```powershell
dotnet new console --name SpdxExample --framework net10.0
Set-Location SpdxExample
dotnet add package Spdx.Expressions --source https://api.nuget.org/v3/index.json
```

Replace Program.cs with the [quick-start code](../README.md#get-started), then run:

```powershell
dotnet run
```

For an existing application, run the package installation command from its project directory.
Package restore requires access to NuGet.org. Library execution is offline.
The quick start also works with 0.1.0. The catalogue loading, traversal and canonical
casing examples require version 1.0.0 or later. To test a source checkout, use a
verified [local package](#use-a-local-package).
See the [compatibility policy](compatibility.md) for the 1.x guarantees.

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

To test changes from a source checkout, use PowerShell 7 to build and verify a local package:

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

Scenario: `unknown-id`. Identifier-shaped tokens can parse even when the selected catalogue lacks them.
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

## Apply the selected catalogue's casing

Scenario `canonical-casing` uses the same AST for spelling-preserving formatting
and catalogue-specific casing. This API is part of the 1.0.0 baseline and is absent
from 0.1.0. Pass the registry you want to use.

<!-- example: canonical-casing -->
```csharp
using System;
using Spdx.Expressions;

var parsed = SpdxExpressionParser.Parse(" ( mit+ WITH llvm-exception ) OR LicenseRef-MiT ");
if(parsed.Expression is not { } expression)
    return;
var registry = SpdxLicenseRegistry.Default;
Console.WriteLine(SpdxExpressionFormatter.Format(expression));
Console.WriteLine(SpdxExpressionFormatter.FormatCanonicalCasing(expression, registry));
```

Expected output:

```text
(mit+ WITH llvm-exception) OR LicenseRef-MiT
(MIT+ WITH LLVM-exception) OR LicenseRef-MiT
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
pass the operation's cancellation token. Parse, Validate, Format and FormatCanonicalCasing
all accept one.
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

## Load a selected catalogue from memory

Run the `load-memory` scenario. The two byte arrays form a synthetic pair.
The example passes the loaded registry
directly to validation and reads its declared version from the validation result.

<!-- example: load-memory -->
```csharp
using System;
using Spdx.Expressions;

var licenses = """{"licenseListVersion":"selected-1","licenses":[{"licenseId":"S1-Retired","isDeprecatedLicenseId":true},{"licenseId":"S1-Custom","isDeprecatedLicenseId":false}]}"""u8.ToArray();
var exceptions = """{"licenseListVersion":"selected-1","exceptions":[{"licenseExceptionId":"S1-Flip","isDeprecatedLicenseId":true},{"licenseExceptionId":"S1-Exception","isDeprecatedLicenseId":false}]}"""u8.ToArray();
var loaded = SpdxLicenseRegistry.Load(licenses, exceptions);
if(loaded.Registry is not { } registry) {
    var diagnostic = loaded.Diagnostics[0];
    Console.WriteLine($"{loaded.Status}: {diagnostic.Code} in {diagnostic.Input} at byte {diagnostic.ByteOffset}");
    return;
}
var parsed = SpdxExpressionParser.Parse("s1-custom WITH S1-Exception OR Apache-2.0");
var expression = parsed.Expression ?? throw new InvalidOperationException("Invalid expression.");
var validated = SpdxExpressionValidator.Validate(expression, registry);
Console.WriteLine($"{validated.RegistryIdentity.LicenseListVersion} {validated.HasIdentifierErrors}");
foreach(var occurrence in validated.IdentifierOccurrences)
    Console.WriteLine($"{occurrence.Role} {occurrence.Identifier} {occurrence.LookupStatus}");
```

Expected output:

```text
selected-1 True
License s1-custom Known
Exception S1-Exception Known
License Apache-2.0 Unknown
```

## Load a catalogue from streams

Run the `load-stream` scenario. The example uses memory-backed streams so it can run on its own. To read an existing
pair, open each file with `File.OpenRead` and pass the two streams to the same
overload. The caller disposes the streams. Inspect the result status and structured
diagnostic fields to distinguish invalid data from a caller-configured limit.

<!-- example: load-stream -->
```csharp
using System;
using Spdx.Expressions;

var licenses = """{"licenseListVersion":"local","licenses":[{"licenseId":"MIT","isDeprecatedLicenseId":false}]}"""u8.ToArray();
var exceptions = """{"licenseListVersion":"local","exceptions":[{"licenseExceptionId":"LLVM-exception","isDeprecatedLicenseId":false}]}"""u8.ToArray();
foreach(var (exceptionsBytes, options) in new (byte[], SpdxRegistryLoadOptions?)[] {
    (exceptions, null),
    ("{}"u8.ToArray(), null),
    (exceptions, new() { MaxInputBytes = 8 })
}) {
    using(var licenseStream = new System.IO.MemoryStream(licenses))
    using(var exceptionStream = new System.IO.MemoryStream(exceptionsBytes)) {
        var loaded = SpdxLicenseRegistry.Load(licenseStream, exceptionStream, options);
        if(loaded.Registry is { } registry) {
            Console.WriteLine($"{loaded.Status}: {registry.Identity.LicenseListVersion}, {registry.Licenses.Count} license, {registry.Exceptions.Count} exception");
        }
        else {
            var diagnostic = loaded.Diagnostics[0];
            Console.WriteLine($"{loaded.Status}: {diagnostic.Code} in {diagnostic.Input} at byte {diagnostic.ByteOffset}; {diagnostic.LimitName ?? diagnostic.PropertyName}");
        }
    }
}
```

Expected output:

```text
Success: local, 1 license, 1 exception
InvalidData: SPDX_REGISTRY_MISSING_PROPERTY in Exceptions at byte 1; licenseListVersion
LimitExceeded: SPDX_REGISTRY_LIMIT_EXCEEDED in Licenses at byte 8; MaxInputBytes
```

## Collect license and reference occurrences

Scenario: `enumerate-nodes`. Enumerate the original syntax nodes to report exact
source slices, including repeated license occurrences, without a registry.
Use enumeration for a simple loop, or the [typed walker](#inspect-groups-and-exceptions)
to organize handling by node kind. Use validation's occurrence lists when you need
catalogue lookup results; a WITH exception is a field on its node, not a separate node.

<!-- example: enumerate-nodes -->
```csharp
using System;
using Spdx.Expressions;

var parsed = SpdxExpressionParser.Parse(" mit+ WITH eX OR LicenseRef-X OR mit+ ");
if(parsed.Expression is not { } expression)
    return;
foreach(var node in expression.Root.EnumerateNodes()) {
    if(node is SpdxLicenseExpression or SpdxReferenceExpression) {
        var span = node.Span;
        string source = expression.SourceText.Substring(span.Start, span.Length);
        Console.WriteLine($"{node.Kind} {source} @{span.Start}");
    }
}
```

Expected output:

```text
License mit+ @1
Reference LicenseRef-X @17
License mit+ @33
```

## Inspect groups and exceptions

Scenario: `inspect-structure`. The walker visits every node; this inspector
handles only explicit groups and WITH expressions. A WITH callback receives
the exception identifier and its source span on the WITH node.

<!-- example: inspect-structure -->
```csharp
using System;
using Spdx.Expressions;

var parsed = SpdxExpressionParser.Parse("(MIT WITH eX) AND (LicenseRef-X OR ISC)");
if(parsed.Expression is not { } expression)
    return;
var inspection = new StructureInspection();
inspection.Walk(expression.Root);

sealed class StructureInspection : SpdxExpressionWalker {
    protected override void VisitGroup(SpdxGroupExpression node) {
        Console.WriteLine($"Group {node.Span.Start}, length {node.Span.Length}");
    }
    protected override void VisitWith(SpdxWithExpression node) {
        Console.WriteLine($"Exception {node.ExceptionIdentifier} @{node.ExceptionSpan.Start}");
    }
}
```

Expected output:

```text
Group 0, length 13
Exception eX @10
Group 18, length 21
```
