# Spdx.Expressions

[![Build and test](https://github.com/DmitryGaravsky/Spdx.Expressions/actions/workflows/ci.yml/badge.svg?branch=main&event=push)](https://github.com/DmitryGaravsky/Spdx.Expressions/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Spdx.Expressions)](https://www.nuget.org/packages/Spdx.Expressions/)
[![License](https://img.shields.io/github/license/DmitryGaravsky/Spdx.Expressions)](LICENSE)
[![.NET 10.0](https://img.shields.io/badge/.NET-10.0-512BD4)](Spdx.Expressions/Spdx.Expressions.csproj)
[![SPDX License List 3.29.0](https://img.shields.io/badge/SPDX%20License%20List-3.29.0-blue)](docs/catalogue.md)

A standalone .NET 10 library for parsing, inspecting, validating and formatting SPDX license
expressions while preserving source evidence. It retains original text alongside an
immutable AST with exact spelling, explicit groups and source spans for diagnostics
and application-specific analysis. It checks identifiers against a
pinned offline SPDX License List or a caller-selected UTF-8 catalogue pair, and formats
spacing while retaining grouping and operand order. Callers can also opt in to the supplied
catalogue's casing for known identifiers. The library has no runtime NuGet dependencies
or runtime network requests.

The library targets **net10.0 only**. The syntax profile is SPDX-2.3.1 and the
embedded catalogue is v3.29.0. These versions are independent of the package version.
Source: [DmitryGaravsky/Spdx.Expressions](https://github.com/DmitryGaravsky/Spdx.Expressions).

## When to use it

- Check expression fields in package metadata or application forms.
- Report diagnostics at positions in the original input while keeping the source
  available alongside formatted output.
- Collect identifiers and custom references, including repeated occurrences; diagnostic
  storage limits preserve all occurrences and outcome flags.
- Inspect groups and operators with lazy AST enumeration or typed walker callbacks
  to build application-specific checks.
- Pin a catalogue independently of the parser, validate the same AST against different
  datasets, and record exact-byte catalogue hashes with results.
- Process supplied expressions and catalogue JSON with configurable resource limits,
  stable diagnostic codes and cooperative cancellation.
- Display consistent spacing and optionally apply catalogue spelling while preserving expression structure.

| Operation | What it establishes |
| --- | --- |
| Parse | The entire input follows the fixed syntax profile and configured limits |
| Validate | Which identifiers exist in the supplied catalogue, plus all unresolved references |
| Load | Whether a caller-selected licenses and exceptions pair meets the supported JSON schema and resource limits |
| EnumerateNodes / Walk | Original syntax nodes in root-inclusive source order, without catalogue lookup |
| Format | Consistent spacing and exact original identifier spelling |
| FormatCanonicalCasing | Consistent spacing and the supplied catalogue's casing for known identifiers in their namespace |

Parse, AST traversal and ordinary Format work without a catalogue. Validation and canonical
casing use a registry you supply. Successful parsing does not establish that identifiers are
known. Validation does not resolve references, check license/exception
combinations, or establish legal permission or compatibility. This library consumes
expression strings; it does not extract them from prose or read complete SBOM documents.

## Get started

With the .NET 10 SDK, install the package from your application's project directory:

```powershell
dotnet add package Spdx.Expressions --source https://api.nuget.org/v3/index.json
```

See [NuGet setup](docs/usage.md#install-from-nuget) for a complete console application setup.
To run the repository examples, use a source checkout with the SDK selected by global.json:

```powershell
dotnet run --project Spdx.Expressions.Examples -- quick-start
```

To consume a local `.nupkg` in your own application, follow
[local package setup](docs/usage.md#use-a-local-package).
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

The quick start also works with 0.1.0. The
[catalogue loader](docs/usage.md#load-a-selected-catalogue-from-memory),
[AST traversal](docs/usage.md#collect-license-and-reference-occurrences) and
[canonical casing](docs/usage.md#apply-the-selected-catalogues-casing) APIs are
available starting with **1.0.0** and are absent from 0.1.0. See the
[1.x compatibility policy](docs/compatibility.md) for API stability and catalogue updates.

## Documentation

| Guide | Contents |
| --- | --- |
| [Usage and examples](docs/usage.md) | NuGet and local setup, runnable scenarios and expected output |
| [API guide](docs/api.md) | Operations, result flags, AST and source evidence |
| [Syntax reference](docs/syntax.md) | Grammar policies, diagnostic codes, limits and cancellation |
| [Catalogues](docs/catalogue.md) | Embedded snapshot, caller-selected schema, lookup and dataset identity |
| [Compatibility](docs/compatibility.md) | Stable 1.x contracts, additive changes and catalogue update policy |

For development, see [Contributing](CONTRIBUTING.md), [Architecture](docs/architecture.md),
[Testing](docs/testing.md), [CI](docs/ci.md) and [Release](docs/release.md).
Run `./eng/verify.ps1` for the complete local pipeline, including examples against the
actual package. Local checks do not establish GitHub-hosted or cross-platform success.

## License and scope

The implementation is [MIT licensed](LICENSE), copyright (c) 2026 Dmitry Garavsky.
The embedded catalogue retains its separate [upstream attribution](THIRD-PARTY-NOTICES.md).
The library covers expression operations and catalogue selection. Reference resolution,
AST serialization, automatic catalogue updates and legal-policy decisions are outside
its scope.
