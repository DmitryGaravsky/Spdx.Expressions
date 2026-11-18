# API guide

[Overview](../README.md) · [Usage](usage.md) · [Syntax](syntax.md) · [Catalogue](catalogue.md)

All public types use the `Spdx.Expressions` namespace. XML member documentation ships
with the assembly for IDE completion. Parse, ordinary Format and AST traversal work
without a registry. Validation and canonical casing each take an explicit registry.
These operations accept optional cancellation tokens.

This guide describes the 1.0.0 API baseline under the [1.x compatibility policy](compatibility.md).
Parse, Validate, ordinary Format and the embedded registry were available in 0.1.0.
The Load overloads, EnumerateNodes, SpdxExpressionWalker and FormatCanonicalCasing
are available starting with 1.0.0.

## Operations

| Entry point | Input | Result |
| --- | --- | --- |
| `SpdxExpressionParser.Parse` | String, optional `SpdxParseOptions` | `SpdxParseResult` |
| `SpdxExpressionValidator.Validate` | `SpdxParsedExpression`, registry, optional `SpdxValidationOptions` | `SpdxValidationResult` |
| `SpdxExpressionFormatter.Format` | `SpdxParsedExpression` | String |
| `SpdxExpressionFormatter.FormatCanonicalCasing` | `SpdxParsedExpression`, explicit registry | String |
| `SpdxLicenseRegistry.Default` | None | Shared immutable embedded catalogue |
| `SpdxLicenseRegistry.Load` | Two `ReadOnlyMemory<byte>` inputs or two `Stream` inputs, optional `SpdxRegistryLoadOptions` | `SpdxRegistryLoadResult` |
| `SpdxExpressionNode.EnumerateNodes` | Root or subtree | Lazy `IEnumerable<SpdxExpressionNode>` of original nodes |
| `SpdxExpressionWalker.Walk` | Root or subtree | Synchronous typed callbacks; no result value |
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

## Load results and call order

`Load` returns `Success` with a complete immutable registry and empty diagnostics,
or `InvalidData` / `LimitExceeded` with a null registry and one primary diagnostic.
The [catalogue guide](catalogue.md#load-a-caller-selected-pair) defines the supported
schema and identity. The [loader diagnostics](syntax.md#catalogue-load-diagnostics)
and [limits](syntax.md#catalogue-load-limits) are separate from expression diagnostics
and parser options. Validation returns the identity of the registry passed to it
in `RegistryIdentity`.

For streams, both null checks run in input order, followed by the same-instance check
and the `CanRead` checks in input order. The first failing check stops the call.
A null argument throws `ArgumentNullException`.
The same instance throws `ArgumentException` for `exceptionsUtf8`, and an unreadable
stream throws `ArgumentException` for that stream parameter. Exceptions raised by a
`CanRead` getter propagate at that step.

Both overloads validate options in [table order](syntax.md#catalogue-load-limits)
before observing cancellation or acquiring bytes. An invalid option throws
`ArgumentOutOfRangeException` with the option property as `ParamName`. Acquisition
and validation then process licenses before reading or inspecting exceptions. Each
input is acquired within the byte budgets before UTF-8 preflight and JSON/schema
checks. Pair validation compares versions before checking namespace collisions.

Cancellation throws `OperationCanceledException`. A synchronous read that throws
propagates its exception, including I/O and disposal failures. After every normally
returning read, even an EOF read or a byte-limit probe, the cancellation check
precedes interpretation of the byte count. Processing also checks cancellation
while copying, parsing, validating and hashing. A caller's blocking synchronous
read and framework sort/freeze calls can run until they return.

## Source evidence and AST

`SpdxParsedExpression` retains the original string verbatim. Its immutable AST has
license, reference, WITH, AND, OR and group nodes. Every node has a UTF-16 span;
identifier spans exclude suffixes, and reference component spans exclude prefixes.
Node spans include internal whitespace; the root excludes outer whitespace.
Constructors are not public and collections do not expose mutable backing storage.

| Type | Useful members |
| --- | --- |
| `SpdxParsedExpression` | `SourceText`, `SyntaxProfile`, `Root` |
| `SpdxExpressionNode` | `Kind`, `Span`, `EnumerateNodes` |
| `SpdxExpressionWalker` | `Walk` and protected visit hooks |
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
Use validation occurrences for registry lookup results. The AST preserves the logical
structure through its ordered child nodes.

## Traverse syntax nodes

Call `EnumerateNodes` on the root or any subtree. Both it and
`SpdxExpressionWalker.Walk` visit the original instances in depth-first pre-order,
starting with the supplied node. AND and OR visit operands in source order.
A group is followed by its inner expression, and WITH by its operand. The
exception identifier and span are fields of the WITH node. The walker therefore
sees WITH before its operand. Validation still reports the exception occurrence
after its operand. Repeated identifiers, groups, exact spelling and spans are
preserved. Both traversal APIs work without a registry.

`EnumerateNodes` is lazy. Calling it or obtaining an enumerator performs no
traversal or cancellation check. Each active `MoveNext` observes cancellation
before publishing another node or completing. Each enumeration starts at the
receiver with the captured token. Separate enumerators have independent state.
Stopping a `foreach` ends that enumeration without further visits or a cancellation
check on disposal. Nodes already yielded remain available.

`Walk` runs synchronously and owns descent. Override `VisitNode` for a common
handler or override the typed `VisitLicense`, `VisitReference`, `VisitWith`,
`VisitAnd`, `VisitOr` and `VisitGroup` hooks for the relevant node kinds. Each
typed default delegates to `VisitNode`, whose default is empty. An override may
call its base implementation to include the common handler. Descent runs either
way. A null root throws `ArgumentNullException` before cancellation is checked.

Both entry points cooperate with cancellation and throw `OperationCanceledException`
with the supplied token. A walker callback that requests cancellation and returns
normally triggers that exception even on the final visit. Callback exceptions
propagate unchanged and already executed callbacks retain their effects. The walker
base carries no per-walk state. Derived types own their callback state.

The library traverses in linear time with a stack proportional to structural
depth, including for wide AND and OR chains. See the [enumeration](usage.md#collect-license-and-reference-occurrences)
and [walker](usage.md#inspect-groups-and-exceptions) examples.

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

### Canonical casing with an explicit registry

Call `FormatCanonicalCasing(expression, registry, cancellationToken)` to apply the
supplied catalogue's spelling to known license and exception identifiers in their
respective namespaces. The cancellation token is optional. A deprecated identifier
keeps the same ID and uses its own catalogue spelling. A license with a trailing
plus uses the base ID for lookup and retains the suffix. Unknown and wrong-kind
IDs, reference components, explicit groups, repetitions and operand order keep
their original spelling or structure. Formatting returns a new string without
modifying `SourceText`, nodes or their original UTF-16 spans.

Choose `SpdxLicenseRegistry.Default` or pass a registry loaded from a selected pair.
The formatter uses only that registry. Record its `Identity` separately to
identify the catalogue used for an output. Use validation separately for identifier
diagnostics and unresolved references. Null arguments are checked in expression,
registry order before cancellation. Cancellation throws `OperationCanceledException`
without a partial string. See the [canonical casing example](usage.md#apply-the-selected-catalogues-casing).
This method is part of the 1.0.0 baseline; it is not available in 0.1.0.

Both formatters are deterministic and idempotent with the same inputs and, where
applicable, the same catalogue. Reparse with sufficient limits to preserve structure
with spans measured in the new string. Canonical casing uses the same number of
UTF-16 units as ordinary Format. Spaces can still exceed the original input-length
budget, and expressions parsed with custom budgets may exceed defaults.
