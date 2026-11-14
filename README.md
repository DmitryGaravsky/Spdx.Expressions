# Spdx.Expressions

[![Build and test](https://github.com/DmitryGaravsky/Spdx.Expressions/actions/workflows/ci.yml/badge.svg?branch=main&event=push)](https://github.com/DmitryGaravsky/Spdx.Expressions/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Spdx.Expressions)](https://www.nuget.org/packages/Spdx.Expressions/)
[![License](https://img.shields.io/github/license/DmitryGaravsky/Spdx.Expressions)](LICENSE)
[![.NET 10.0](https://img.shields.io/badge/.NET-10.0-512BD4)](Spdx.Expressions/Spdx.Expressions.csproj)
[![SPDX License List 3.29.0](https://img.shields.io/badge/SPDX%20License%20List-3.29.0-blue)](docs/catalogue.md)

A standalone .NET 10 library for applications that read, check or display SPDX license
expressions. It preserves original text and source spans, checks identifiers against a
pinned offline SPDX License List, and formats spacing while retaining spelling, grouping
and operand order. There are no runtime NuGet dependencies or runtime network requests.

**Status:** published on [NuGet.org](https://www.nuget.org/packages/Spdx.Expressions/);
targets **net10.0 only**. The syntax profile is SPDX-2.3.1;
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

## Documentation

| Guide | Contents |
| --- | --- |
| [Usage and examples](docs/usage.md) | NuGet and local setup, runnable scenarios and expected output |
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
