# Spdx.Expressions

[![Build and test](https://github.com/DmitryGaravsky/Spdx.Expressions/actions/workflows/ci.yml/badge.svg)](https://github.com/DmitryGaravsky/Spdx.Expressions/actions/workflows/ci.yml)

A standalone .NET 10 library for applications that read, check or display SPDX license
expressions. It preserves original text and source spans, checks identifiers against a
pinned offline SPDX License List, and formats spacing while retaining spelling, grouping
and operand order. There are no runtime NuGet dependencies or runtime network requests.

**Status:** unpublished development package; targets **net10.0 only**. Package-name
availability and publication remain separate decisions. The syntax profile is SPDX-2.3.1;
the embedded catalogue is v3.29.0. These versions are independent of the package version.
Source: [DmitryGaravsky/Spdx.Expressions](https://github.com/DmitryGaravsky/Spdx.Expressions).

## When to use it

- Check expression fields in package metadata or application forms.
- Report syntax and identifier diagnostics at positions in the original input.
- Collect identifiers and custom references, including repeated occurrences.
- Display consistent spacing without rewriting the expression's structure or spelling.

| Operation | What it establishes |
| --- | --- |
| Parse | The entire input follows the fixed syntax profile and configured limits |
| Validate | Which identifiers exist in the pinned catalogue, plus all unresolved references |
| Format | Consistent spacing; no catalogue lookup or expression simplification |

Parse and Format do not initialize the catalogue. Successful parsing does not establish
that identifiers are known. Validation does not resolve references, check license/exception
combinations, or establish legal permission or compatibility. This library consumes
expression strings; it does not extract them from prose or read complete SBOM documents.

## Get started

Install the stable .NET 10 SDK selected by global.json. From a source checkout, run:

```powershell
dotnet run --project Spdx.Expressions.Examples -- quick-start
```

To consume a local `.nupkg` in your own application, follow
[local package setup](docs/usage.md#use-a-local-package). No NuGet.org publication is assumed.
PowerShell 7 is needed for repository verification scripts, not for library use.

The following complete console program is also the `quick-start` example:

<!-- example: quick-start -->
```csharp
using System;
using Spdx.Expressions;

var parsed = SpdxExpressionParser.Parse("MIT OR Apache-2.0");
if(parsed.Status != SpdxParseStatus.Success) {
    var diagnostic = parsed.Diagnostics[0];
    Console.WriteLine($"Parse: {parsed.Status}; {diagnostic.Code} at {diagnostic.Span.Start}");
    return;
}
var expression = parsed.Expression!;
var validation = SpdxExpressionValidator.Validate(expression, SpdxLicenseRegistry.Default);
Console.WriteLine($"Formatted: {SpdxExpressionFormatter.Format(expression)}");
Console.WriteLine($"Identifier errors: {validation.HasIdentifierErrors}");
Console.WriteLine($"Unresolved references: {validation.HasUnresolvedReferences}");
Console.WriteLine($"Diagnostics truncated: {validation.DiagnosticsTruncated}");
foreach(var diagnostic in validation.Diagnostics)
    Console.WriteLine($"{diagnostic.Severity}: {diagnostic.Code} at {diagnostic.Span.Start}");
```

Expected output:

```text
Formatted: MIT OR Apache-2.0
Identifier errors: False
Unresolved references: False
Diagnostics truncated: False
```

## Documentation

| Guide | Contents |
| --- | --- |
| [Usage and examples](docs/usage.md) | Local setup, runnable scenarios and expected output |
| [API guide](docs/api.md) | Operations, result flags, AST and source evidence |
| [Syntax reference](docs/syntax.md) | Grammar policies, diagnostic codes, limits and cancellation |
| [Embedded catalogue](docs/catalogue.md) | Lookup behavior, dataset identity and updates |

For development, see [Contributing](CONTRIBUTING.md), [Architecture](docs/architecture.md),
[Testing](docs/testing.md), [CI](docs/ci.md) and [Release](docs/release.md).
Run `./eng/verify.ps1` for the complete local pipeline, including examples against the
actual package. Local checks do not establish GitHub-hosted or cross-platform success.

## License and scope

The implementation is [MIT licensed](LICENSE), copyright (c) 2026 Dmitry Garavsky.
The embedded catalogue retains its separate [upstream attribution](THIRD-PARTY-NOTICES.md).
There is no CLI product, reference resolver, AST serialization, canonical-casing mode,
external catalogue loader, automatic catalogue update or legal-policy engine.
