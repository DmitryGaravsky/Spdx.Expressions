# Spdx.Expressions

A standalone .NET 10 library for parsing, validating and formatting SPDX expressions.
Source: [DmitryGaravsky/Spdx.Expressions](https://github.com/DmitryGaravsky/Spdx.Expressions).
It has no runtime NuGet dependencies, font/SBOM dependencies or runtime networking.
The three operations are independent: parsing and formatting do not initialize the registry.

```csharp
using Spdx.Expressions;

var parsed = SpdxExpressionParser.Parse("mit OR (GPL-2.0+ WITH Classpath-exception-2.0)");
if (parsed.Status == SpdxParseStatus.Success)
{
    var expression = parsed.Expression!;
    var validation = SpdxExpressionValidator.Validate(expression, SpdxLicenseRegistry.Default);
    Console.WriteLine(SpdxExpressionFormatter.Format(expression));
    Console.WriteLine(validation.HasIdentifierErrors);
    Console.WriteLine(validation.HasUnresolvedReferences);
    Console.WriteLine(validation.DiagnosticsTruncated);
}
```

## Syntax and evidence

The fixed `SyntaxProfile` is `SPDX-2.3.1`, based on
[Annex D](https://spdx.github.io/spdx-spec/v2.3.1/SPDX-license-expressions/).
The profile accepts uppercase AND, OR and WITH, adjacent plus, parentheses,
LicenseRef-X and DocumentRef-D:LicenseRef-X. Precedence is plus, WITH, AND, OR.
AND/OR chains are flattened only across uninterrupted occurrences of the same
operator; explicit groups and operand order remain intact.

Policies are fixed, with no permissive mode:

- Whitespace is ASCII space, tab, CR and LF. Other Unicode whitespace is rejected.
- ID candidates and reference components contain one or more ASCII letters, digits,
  hyphens or dots. Unknown candidates can parse successfully.
- Reference prefixes require exact casing. `licenseref-X` is an ordinary ID candidate;
  malformed recognized prefixes fail. Component spelling is retained.
- AND/OR require whitespace or parentheses at both boundaries. WITH requires
  whitespace on both sides and attaches only to a license or reference, never a group.
- One adjacent plus is allowed on a license candidate, never a reference or exception.
- Uppercase AND, OR, WITH, NONE and NOASSERTION are reserved whole tokens in ID positions.
  NONE/NOASSERTION are field values outside this API. Lowercase forms are ordinary candidates.
  Reference components such as `LicenseRef-NONE` are allowed.
- Full input consumption is required. Comments, declaration prefixes, quotes, underscores,
  NUL, Unicode lookalikes, repeated WITH and trailing prose are rejected.

`SpdxParsedExpression` retains the original string verbatim. Its immutable AST has
license, reference, WITH, AND, OR and group nodes. Every node has a UTF-16 span;
identifier spans exclude suffixes, and reference component spans exclude prefixes.
Node spans include internal whitespace; the root excludes outer whitespace.
Constructors are not public and collections do not expose mutable backing storage.
There are no builders, public visitors or individual punctuation spans.

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

Stable diagnostic codes (English message wording is not a machine contract):

| Code | Condition |
| --- | --- |
| SPDX_PARSE_EMPTY_INPUT | Empty or whitespace-only input |
| SPDX_PARSE_INVALID_CHARACTER | Character outside the profile |
| SPDX_PARSE_EXPECTED_OPERAND | Missing expression operand |
| SPDX_PARSE_EXPECTED_OPERATOR | Missing/incorrectly separated operator |
| SPDX_PARSE_UNBALANCED_PARENTHESIS | Unmatched parenthesis |
| SPDX_PARSE_INVALID_REFERENCE | Malformed recognized reference |
| SPDX_PARSE_INVALID_PLUS | Invalid suffix use |
| SPDX_PARSE_INVALID_WITH | Invalid attachment, spacing or exception operand |
| SPDX_PARSE_FIELD_SENTINEL | NONE/NOASSERTION in an ID position |
| SPDX_PARSE_LIMIT_EXCEEDED | Inclusive parser budget exceeded |
| SPDX_ID_UNKNOWN_LICENSE | Unknown license candidate |
| SPDX_ID_UNKNOWN_EXCEPTION | Unknown exception candidate |
| SPDX_ID_WRONG_KIND | ID belongs only to the opposite namespace |
| SPDX_ID_DEPRECATED | Known deprecated ID |

Parser diagnostics use original UTF-16 spans; EOF can have length zero.
The scanner classifies the next token before the parser checks its grammatical role.
Invalid characters or token budgets discovered during that lookahead can therefore
take precedence over a missing-operator error. Specific arbitration cases are frozen
in the grammar fixtures. No recovery or suppression-marker diagnostics are emitted.

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

## Limits and cancellation

All budgets are inclusive positive `int` values. Parenthesis depth additionally cannot
exceed 256. Integer values outside the `int` range cannot be supplied; counters and
span arithmetic avoid unchecked growth beyond accepted input ranges.

| Option | Default | Counted units |
| --- | ---: | --- |
| MaxInputLength | 65,536 | UTF-16 units including whitespace, checked before scanning |
| MaxIdentifierLength | 256 | Each ID/reference component, excluding prefixes and plus |
| MaxTokens | 16,384 | ID/reference, operator, plus or parenthesis; no whitespace/EOF |
| MaxNodes | 8,192 | Every AST node, including groups and flattened operator nodes |
| MaxParenthesisDepth | 64 | Concurrent open parentheses |
| MaxDiagnostics (validation) | 128 | Stored messages only; minimum one |

Parser recursion is bounded by parenthesis depth, not chain length. Traversals use an
explicit stack and cancellation checks. Long whitespace and candidate scans also poll
cancellation. Configured maxima do not cause eager maximum-sized allocations.
These budgets are engineering controls, not process isolation, a wall-clock guarantee
or a completed security audit. Host-level resource controls remain the caller's concern.

## Embedded catalogue

Default is a thread-safe immutable snapshot of **v3.29.0**, not a claim about the newest list.
There are 740 license entries and 86 exception entries. Lookup uses OrdinalIgnoreCase;
enumeration uses ordinal canonical-ID ordering. Public entries contain only Identifier
and IsDeprecated. Six upstream deprecated license entries contain a literal trailing `+`;
lookup preserves them, while expression parsing treats plus as a suffix and validation
checks the base ID. Upstream exceptions also use the field `isDeprecatedLicenseId`.

Both exact JSON files come from the same
[upstream commit](https://github.com/spdx/license-list-data/tree/31ba1a50e5397e00a304dbadc76531740e89ee48).
`SpdxRegistryIdentity` exposes the list version and the lowercase SHA-256 of each file:

- licenses.json: `47d1cc681abe31166b342b6cc4aab13a6ba8ea5c48794697fe3bf8b1dbaf509a`
- exceptions.json: `59ee7c150de521d635cbf9367b8cf543b8da6781ea3ac1c073d305524f84ada2`

Source paths, byte lengths, tag/commit, import date and notice provenance are in the
shipped `data/manifest.json`. The exact JSON and upstream notices are embedded and
also included as package data. Corrupt resources cause InvalidOperationException,
not an empty catalogue or a user-input error.

No build or runtime update occurs. A manual development update imports both exact
files from one reviewed commit, updates the manifest and pinned hashes, reviews
notices, and reruns all entry/hash/package checks. There is no public JSON/stream loader,
external catalogue API or combined fingerprint.

## Development and verification

SDK selection follows the version and roll-forward policy in global.json. NUnit and test runner
dependencies are pinned centrally and are development-only. Shared test settings live
in eng/Tests.props. NuGet.Config explicitly selects nuget.org for development restore.
Builds use explicit usings, nullable references, deterministic output and warnings as errors.
See [Contributing](CONTRIBUTING.md), [repository rules](AGENTS.md) and [Architecture](docs/architecture.md).

```powershell
./eng/verify.ps1
```

The script checks release automation with offline fixtures, restores, runs Debug/Release tests under external process-tree watchdogs,
verifies C# whitespace, packs Release and runs an external consumer through PackageReference using a local
feed and isolated package/HTTP/CLI caches. The consumer also verifies package resources,
notices and absence of runtime third-party dependencies. It never uses ProjectReference
or internal access. Its portable source and driver are in `eng/package-consumer` and
`eng/package-smoke.ps1`; run the latter with PowerShell 7 and .NET 10 on Windows or Linux.

The bounded mutation suite uses seed 231 and 600 cases. The generated formatting matrix
has 196 cases. This is not a long fuzzing campaign or benchmark. See [Testing](docs/testing.md)
for actual checks and unverified platforms; a provided script is not evidence it ran.
The [CI workflow](docs/ci.md) configures Windows/Linux checks for pull requests and
adds macOS for main/manual runs. Hosted results are not implied by local verification.

## Scope and release status

The implementation is [MIT licensed](LICENSE), copyright (c) 2026 Dmitry Garavsky.
This is an unpublished development package. Publication and package-name availability
remain separate decisions. A version-tag-only workflow is prepared for verified releases;
see [Release policy](docs/release.md) for local preparation and publication setup. The embedded catalogue
retains its separate [upstream attribution](THIRD-PARTY-NOTICES.md).
There is no font adapter, CLI, prose extraction, reference
resolver, AST serialization, analyzer facade, canonical-casing mode, automatic updates
or legal-policy engine. These are not hidden prerequisites for the implemented MVP.
