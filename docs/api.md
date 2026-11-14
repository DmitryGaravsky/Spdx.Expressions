# API guide

[Overview](../README.md) · [Usage](usage.md) · [Syntax](syntax.md) · [Catalogue](catalogue.md)

All public types use the `Spdx.Expressions` namespace. XML member documentation ships
with the assembly for IDE completion. The operations accept optional cancellation tokens
and are independently usable; only validation needs an explicit registry.

## Operations

| Entry point | Input | Result |
| --- | --- | --- |
| `SpdxExpressionParser.Parse` | String, optional `SpdxParseOptions` | `SpdxParseResult` |
| `SpdxExpressionValidator.Validate` | `SpdxParsedExpression`, registry, optional `SpdxValidationOptions` | `SpdxValidationResult` |
| `SpdxExpressionFormatter.Format` | `SpdxParsedExpression` | String |
| `SpdxLicenseRegistry.Default` | None | Shared immutable embedded catalogue |
| `TryGetLicense` / `TryGetException` | Identifier string | Boolean and nullable entry through an out parameter |

## Results and diagnostics

Parse returns Success, InvalidSyntax or LimitExceeded. Success has an expression and
no diagnostics; failure has one primary diagnostic and no partial AST. Null required
arguments and invalid options throw ordinary argument exceptions. Cancellation throws
OperationCanceledException without a partial result. Unexpected program/process errors
are not converted into malformed-input results.

Validate requires an explicit registry. It returns **all** identifier and reference
occurrences in source order within each list, including repetitions. Spans allow
merging the lists into a single source order. Identifier occurrences report their
License/Exception role, Known/Unknown/WrongKind lookup status, and canonical ID and
deprecation flag only when known in the expected namespace. Plus validates the base ID.
References are never looked up as official identifiers and are never resolved.

The flags are independent:

| Flag | Meaning |
| --- | --- |
| HasIdentifierErrors | At least one unknown or wrong-namespace ID, even if its diagnostic was omitted |
| HasUnresolvedReferences | At least one reference, including a reference-only expression |
| DiagnosticsTruncated | At least one diagnostic message omitted by MaxDiagnostics |

Deprecated IDs produce warnings, not identifier errors. Casing differences do not warn.
There is no license/exception combination allowlist. References are represented by
their occurrences and flag; they do not generate an additional diagnostic code.
Message storage limits never stop traversal or suppress occurrences/outcome flags.
There is no aggregate validity/completeness enum. Parse success and absence of identifier
errors imply neither reference resolution nor legal permission or compatibility.

For example, validating `GPL-2.0 OR GPL-2.0 OR Unknown-1.0 OR LicenseRef-X` with
MaxDiagnostics = 1 retains the first deprecation warning, all three identifiers and
the reference. All three flags are true.

See the [diagnostic code reference](syntax.md#diagnostic-codes) for stable codes and
[examples](usage.md#diagnostic-storage-does-not-limit-evidence) for truncated results.

## Source evidence and AST

`SpdxParsedExpression` retains the original string verbatim. Its immutable AST has
license, reference, WITH, AND, OR and group nodes. Every node has a UTF-16 span;
identifier spans exclude suffixes, and reference component spans exclude prefixes.
Node spans include internal whitespace; the root excludes outer whitespace.
Constructors are not public and collections do not expose mutable backing storage.
There are no builders, public visitors or individual punctuation spans.

| Type | Useful members |
| --- | --- |
| `SpdxParsedExpression` | `SourceText`, `SyntaxProfile`, `Root` |
| `SpdxExpressionNode` | `Kind`, `Span` |
| `SpdxLicenseExpression` | `Identifier`, `IdentifierSpan`, `OrLater` |
| `SpdxReferenceExpression` | `DocumentId`, `LicenseId`, component spans |
| `SpdxWithExpression` | `Operand`, `ExceptionIdentifier`, `ExceptionSpan` |
| `SpdxAndExpression` / `SpdxOrExpression` | Ordered `Operands` |
| `SpdxGroupExpression` | `Inner` |
| `SpdxDiagnostic` | `Code`, `Severity`, `Span`, `Message` |
| `SpdxIdentifierOccurrence` | `Identifier`, `Span`, `Role`, `LookupStatus`, `CanonicalIdentifier`, `IsDeprecated` |
| `SpdxReferenceOccurrence` | `Span`, `DocumentId`, `LicenseId` |

A `SpdxSourceSpan` is a zero-based `Start` and `Length` in UTF-16 code units. Use it with
`SourceText.Substring(span.Start, span.Length)`; it is not a byte offset or a display column.
For a simple list of candidates, use validation occurrences. To distinguish AND from OR,
inspect the AST: flattening an occurrence list loses the expression's logical structure.

## Formatting

Format produces single ASCII spaces around operators, no spaces inside explicit groups
and no space before plus. It retains exact spelling, deprecated forms, references,
repetitions and order. It does not consult a registry or simplify the expression.

| Input | Output |
| --- | --- |
| ` mit  OR Apache-2.0 ` | `mit OR Apache-2.0` |
| `( MIT ) AND MIT` | `(MIT) AND MIT` |
| `GPL-2.0+ WITH Classpath-exception-2.0` | unchanged |
| `LicenseRef-X OR Unknown-1.0` | unchanged |

Formatting is deterministic and idempotent. Reparse with sufficient limits to preserve
structure ignoring spans. Inserted spaces can exceed the original input-length budget;
expressions parsed with custom budgets may also exceed defaults.
