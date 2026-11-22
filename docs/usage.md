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
casing examples require version 1.0.0 or later. The field reader, catalogue display
metadata and document-scoped reference lookup require 1.1.0 or later. To try the
working copy before publication, use a verified [local package](#use-a-local-package).
See the [compatibility policy](compatibility.md) for the 1.x guarantees.

## Run the examples

From a source checkout with the .NET 10 SDK selected by global.json:

```powershell
dotnet run --project Spdx.Expressions.Examples -- quick-start
dotnet run --project Spdx.Expressions.Examples -- syntax-error
dotnet run --project Spdx.Expressions.Examples -- inspect-license-field
dotnet run --project Spdx.Expressions.Examples -- license-field
dotnet run --project Spdx.Expressions.Examples -- resolve-references
dotnet run --project Spdx.Expressions.Examples -- diagnostic-report
```

Replace the final argument with any scenario name below. Omitting it runs `quick-start`.
For a package-only application, copy the scenario's complete C# block into `Program.cs`
and run `dotnet run` from the application directory. The named runner lives in the
[examples project](../Spdx.Expressions.Examples/Program.cs).
Each C# block is a complete console program; the verification pipeline checks it against
the corresponding source region, compiles each block independently against the actual nupkg,
and compares both the runner and standalone program output with the text shown here.
The [quick-start program](../README.md#get-started) covers successful parsing and validation.

## Use a local package

From the checkout root, use PowerShell 7 to give each local build its own prerelease
version. NuGet can then distinguish it from earlier builds already in the package cache.

```powershell
$localVersion = '1.1.1-local-' + [Guid]::NewGuid().ToString('N')
$output = Join-Path 'artifacts/verification' $localVersion
./eng/verify.ps1 -Version $localVersion -OutputDirectory $output
$run = Get-Content (Join-Path $output 'run.json') -Raw | ConvertFrom-Json
if ($run.status -ne 'Passed') { throw 'Package verification did not pass.' }
$package = (Resolve-Path -LiteralPath $run.package).Path
$feed = Split-Path $package -Parent
$version = $run.version
```

In the same PowerShell session, create a console application outside the checkout and
install the exact version from the verified feed.

```powershell
$app = Join-Path ([IO.Path]::GetTempPath()) ('SpdxExample-' + [Guid]::NewGuid().ToString('N'))
dotnet new console --name SpdxExample --framework net10.0 --output $app --no-restore
dotnet add "$app/SpdxExample.csproj" package Spdx.Expressions --version "[$version]" --source $feed
```

Replace the new application's `Program.cs` with [the field inspection example](../README.md#inspect-a-license-field), then run

```powershell
dotnet run --project "$app/SpdxExample.csproj" --no-restore
```

The exact PackageReference version comes from the verified package. The `--source` argument
selects its local feed; no published package is required. Library execution is offline;
building the repository may need development dependencies. See [Testing](testing.md) for
local-feed restore and isolated SDK execution. Ordinary commands above use your normal SDK
environment; repository verification runs the documented isolation guards.

## Read a license field

Run `dotnet run --project Spdx.Expressions.Examples -- license-field` from the checkout.
`SpdxLicenseFieldReader.Read` classifies
nullable metadata strings. Use `Expression` to inspect a parsed tree or `Diagnostics`
to locate a failure in the original text.

<!-- example: license-field -->
```csharp
using System;
using Spdx.Expressions;

string?[] fields = [null, "", " ", "NONE", " NOASSERTION ", " MIT OR Apache-2.0 "];
foreach(string? text in fields) {
    var field = SpdxLicenseFieldReader.Read(text);
    string source = field.SourceText is null ? "<absent>" : $"'{field.SourceText}'";
    Console.WriteLine($"{source} -> {field.Kind}");
}
```

Expected output:

```text
<absent> -> Missing
'' -> Empty
' ' -> InvalidSyntax
'NONE' -> None
' NOASSERTION ' -> NoAssertion
' MIT OR Apache-2.0 ' -> Expression
```

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

Scenario: `reference`. Validation records each custom reference. To look up its definition,
pass the parsed expression to a [reference context](#resolve-references-in-a-document).

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

## Resolve references in a document

Run `dotnet run --project Spdx.Expressions.Examples -- resolve-references` from the checkout.
Both documents define `LicenseRef-X`. The qualifier in the second occurrence selects
the vendor's definition. The third occurrence uses the binding to `archive`, a
document the application has yet to supply.

<!-- example: resolve-references -->
```csharp
using System;
using Spdx.Expressions;

var parsed = SpdxExpressionParser.Parse(" LicenseRef-X OR DocumentRef-D:LicenseRef-X OR DocumentRef-E:LicenseRef-X");
if(parsed.Expression is not { } expression)
    return;
var context = SpdxReferenceContext.Create(
    availableDocuments: ["app", "vendor"],
    definitions: [new("app", "X", "Application terms."), new("vendor", "X", "Vendor terms.")],
    bindings: [new("app", "D", "vendor"), new("app", "E", "archive")]);
foreach(var result in context.Resolve(expression, "app")) {
    var span = result.Occurrence.Span;
    string spelling = expression.SourceText.Substring(span.Start, span.Length);
    Console.WriteLine($"{spelling} @{span.Start} {result.SourceDocumentIdentity} -> {result.TargetDocumentIdentity} {result.Status}");
    if(result.Definition is { } definition)
        Console.WriteLine(definition.ExtractedText);
}
```

Expected output:

```text
LicenseRef-X @1 app -> app Resolved
Application terms.
DocumentRef-D:LicenseRef-X @17 app -> vendor Resolved
Vendor terms.
DocumentRef-E:LicenseRef-X @47 app -> archive DocumentUnavailable
```

Use the document namespace as its identity when importing SPDX documents. The
context treats identities as nonempty opaque strings and compares them ordinally.
The application supplies available documents and checks external document checksums
and provenance. See SPDX 2.3.1 [document namespaces and external references](https://spdx.github.io/spdx-spec/v2.3.1/document-creation-information/#65-spdx-document-namespace-field)
and [extracted licensing information](https://spdx.github.io/spdx-spec/v2.3.1/other-licensing-information-detected/).

Pass only the components after `LicenseRef-` and `DocumentRef-` in definitions
and bindings. The expression profile accepts `[A-Za-z0-9.-]+` for both, with exact
case. Every character supplied is literal. For example, the component `LicenseRef-X`
would match the expression `LicenseRef-LicenseRef-X`. Names, extracted text
and optional cross-reference strings retain their original spelling and whitespace.

Each available document must be listed once, including an empty document. A definition
or binding must name an available owner. Null entries, missing required strings,
empty extracted text, invalid components and repeated document identities cause
an argument exception at creation. Repeated alias and license keys, including
identical declarations, give `DuplicateBinding` and `DuplicateDefinition` for
affected occurrences. Other keys remain usable.

Resolve requires an available source. An undeclared alias gives `UnknownDocumentAlias`,
a declared but unavailable target gives `DocumentUnavailable`, and an available
target lacking the ID gives `DefinitionMissing`. A local definition is considered
only for an unqualified reference or a binding to that same document.

The context indexes each sequence once. `SpdxReferenceContextOptions` defaults to
65,536 entries and 16,777,216 UTF-16 units summed across all input string fields,
including repeats. Limits are positive and inclusive. Invalid or exceeded limits
raise `ArgumentOutOfRangeException`. Resolution traverses the bounded parse tree once
and returns one result per occurrence, independent of validation diagnostics.
Both operations accept a cancellation token and throw `OperationCanceledException`.

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

## Report syntax and validation diagnostics

Run `dotnet run --project Spdx.Expressions.Examples -- diagnostic-report` from the checkout.
The report uses half-open UTF-16 ranges in the original input. For `MIT OR`, the missing
operand has a zero-length range at the end of the string. The second expression parses
successfully. With the embedded 3.29.0 catalogue, a two-message budget retains both the
deprecation warning and the unknown-license error. A one-message budget retains the
earlier warning. Compare the flags, ID occurrences and both source positions for the
same reference across the two runs. To look up either occurrence, use the
[document context](#resolve-references-in-a-document).

<!-- example: diagnostic-report -->
```csharp
using System;
using Spdx.Expressions;

foreach(string source in new[] { "MIT OR", "GPL-2.0 OR LicenseRef-X OR Unknown-1.0 OR LicenseRef-X" }) {
    var parsed = SpdxExpressionParser.Parse(source);
    Console.WriteLine($"Source: {source}");
    Console.WriteLine($"Parse: {parsed.Status}, AST: {parsed.Expression?.Root.Kind.ToString() ?? "absent"}");
    foreach(var diagnostic in parsed.Diagnostics)
        WriteDiagnostic(diagnostic);
    if(parsed.Expression is not { } expression)
        continue;
    foreach(int budget in new[] { 2, 1 }) {
        var options = new SpdxValidationOptions { MaxDiagnostics = budget };
        var validation = SpdxExpressionValidator.Validate(expression, SpdxLicenseRegistry.Default, options);
        Console.WriteLine($"MaxDiagnostics={budget}, DiagnosticsTruncated={validation.DiagnosticsTruncated}");
        foreach(var diagnostic in validation.Diagnostics)
            WriteDiagnostic(diagnostic);
        Console.WriteLine($"HasIdentifierErrors={validation.HasIdentifierErrors}, HasUnresolvedReferences={validation.HasUnresolvedReferences}");
        foreach(var id in validation.IdentifierOccurrences)
            Console.WriteLine($"  {id.LookupStatus} ID {id.Identifier} {Range(id.Span)}");
        foreach(var reference in validation.References) {
            var span = reference.Span;
            string spelling = source.Substring(span.Start, span.Length);
            Console.WriteLine($"  Reference {spelling} {Range(span)} (resolution not attempted)");
        }
    }
}
static void WriteDiagnostic(SpdxDiagnostic diagnostic) {
    Console.WriteLine($"  {diagnostic.Severity} {diagnostic.Code} {Range(diagnostic.Span)}");
}
static string Range(SpdxSourceSpan span) {
    return $"[{span.Start}..{span.Start + span.Length})";
}
```

Expected output:

```text
Source: MIT OR
Parse: InvalidSyntax, AST: absent
  Error SPDX_PARSE_EXPECTED_OPERAND [6..6)
Source: GPL-2.0 OR LicenseRef-X OR Unknown-1.0 OR LicenseRef-X
Parse: Success, AST: Or
MaxDiagnostics=2, DiagnosticsTruncated=False
  Warning SPDX_ID_DEPRECATED [0..7)
  Error SPDX_ID_UNKNOWN_LICENSE [27..38)
HasIdentifierErrors=True, HasUnresolvedReferences=True
  Known ID GPL-2.0 [0..7)
  Unknown ID Unknown-1.0 [27..38)
  Reference LicenseRef-X [11..23) (resolution not attempted)
  Reference LicenseRef-X [42..54) (resolution not attempted)
MaxDiagnostics=1, DiagnosticsTruncated=True
  Warning SPDX_ID_DEPRECATED [0..7)
HasIdentifierErrors=True, HasUnresolvedReferences=True
  Known ID GPL-2.0 [0..7)
  Unknown ID Unknown-1.0 [27..38)
  Reference LicenseRef-X [11..23) (resolution not attempted)
  Reference LicenseRef-X [42..54) (resolution not attempted)
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
