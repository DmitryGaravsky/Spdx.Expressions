# Architecture

Spdx.Expressions is an independent net10.0 library with no runtime NuGet dependencies.
Its grammar, catalogue and package identities are separate. Applications and adapters
consume the library through its public API; their models and release processes are independent.

```text
Nullable field ──→ Read ──→ Missing / Empty / None / NoAssertion
                      └──→ original string passed to Parse for expression or failure

Source string ──→ Parse ──→ immutable AST + source/spans
                      │              │
                      │              ├──→ EnumerateNodes / Walk ──→ original nodes
                      │              │
                      │              ├──→ Format ──→ deterministic spacing
                      │              ├──→ FormatCanonicalCasing ←── explicit immutable registry
                      │              │
                      │              ├──→ Resolve ←── source document + indexed reference context
                      │              │          └──→ definition or lookup status per reference occurrence
                      │              │
                      │              └──→ Validate ←── explicit immutable registry
                      │                         │
                      │                         └──→ all occurrences + independent flags
                      └──→ syntax/limit diagnostic, no partial AST

Embedded resources ──→ pinned length and SHA-256 ──→ JSON entries ──┐
                                                                    ├──→ sorted entries and lookup indexes
Selected bytes/streams ──→ bounded acquisition and schema checks ───┘
```

| Location | Responsibility |
| --- | --- |
| Spdx.Expressions/Parsing | Parser, options, diagnostics, AST, parse results and walker |
| Spdx.Expressions/Registry | Immutable catalogue model, load contracts and lazy embedded snapshot |
| Spdx.Expressions/Registry/Internal | Bounded acquisition, JSON/schema validation, display metadata and exact-byte identity for catalogue pairs |
| Spdx.Expressions/Validation | Complete occurrence collection and bounded diagnostics |
| Spdx.Expressions/Resolution | Document-scoped definition and alias indexes, lookup outcomes and provenance |
| Spdx.Expressions/Formatting | Shared renderer for spacing and opt-in catalogue casing |
| Spdx.Expressions/Internal | Explicit-stack syntax traversal shared by operations |
| Spdx.Expressions/Data | Exact upstream source bytes, provenance and notices |
| Spdx.Expressions.Tests | Independent NUnit fixtures |
| Spdx.Expressions.Examples | Runnable public-API scenarios with documented output |
| Spdx.Compliance | Distribution policy, decision trees, action summaries and CycloneDX 1.6 analysis |
| Spdx.Compliance.Tests | Policy, decision, evidence and per-occurrence regression tests |
| Spdx.Compliance.Examples | Runnable policy and CycloneDX example used by the isolated package consumer |
| eng | Verification driver, process watchdog and external package consumer |

Expression types use the `Spdx.Expressions` namespace. Policy and component APIs
belong to the separate Spdx.Compliance assembly.
Internal shared helpers use Spdx.Expressions.Internal. AST constructors are non-public.
Collections do not expose mutable storage. Options are immutable and operations have
no shared mutable request state. The embedded registry is a lazy, thread-safe immutable
snapshot. On first access, the embedded reader verifies each resource against its pinned
length and SHA-256 before reading its version and entries with `JsonDocument`.
Each external load owns its byte snapshots, validates the pair under the caller's budgets
and computes its identity. Both paths use the same immutable entry types and registry
constructor. Parse, ordinary Format and AST traversal work independently of the catalogue.
Validation and opt-in canonical casing each use their explicitly supplied registry.

Parser recursion is bounded by explicit group depth; operator chains are flat lists.
Enumeration projects entry frames from the explicit stack shared with validation and
formatting. The walker dispatches the same nodes to typed callbacks. Diagnostic storage
never limits occurrence collection or changes error/reference flags. Ordinary Format
can reuse the source string when a single license or reference spans the entire input.
For other inputs it uses the shared renderer to copy source slices and format
operator spacing. FormatCanonicalCasing uses the renderer for every input and looks
up eligible IDs in the expected namespace. Both formatters preserve the original AST.

The [API guide](api.md), [syntax reference](syntax.md) and [catalogue guide](catalogue.md)
specify spans, results, lexical policies, limits and dataset identity. Keep those contracts
and independently specified fixtures aligned.

Documentation examples are source regions in the examples project. Repository checks compare
them with the complete programs in README/Usage; package smoke checks build the same source
through PackageReference and compare each scenario with the documented expected output.
Each documented program is also compiled and executed independently of the example runner.
The package consumer checks both catalogue input forms and validates the same expression
with the selected and embedded registries.
The library does not depend on the examples or documentation tooling.

Packaging generates README from the authored root file through eng/PackageReadme.targets,
using the SDK MSBuild task compiler. Source-control information is initialized before packing
the README. Relative links use RepositoryCommit when supplied, otherwise SourceRevisionId,
and fall back to main only when no commit is available. The package check requires the links
to match the repository commit stored in the nuspec. Fenced code is preserved verbatim. No second README is maintained; no PowerShell or network
request is needed by the pack target. These links require the referenced source revision to
be published before distribution; local checks verify paths and transformation, not remote
availability. The generated file is kept under obj and is not an embedded resource.

## Compliance package

The [Spdx.Compliance library](compliance.md) is a separate package. It uses the public
Spdx.Expressions APIs and keeps policy, CycloneDX and correction models out of the
parser assembly.

```text
CycloneDX 1.6 JSON ──→ component occurrences ──→ component license decisions
                               │                            ↑
                               └──→ correction check       │
                                     ↑                      │
External policy + pinned catalog ────┴──→ policy snapshot ──┘

Spdx.Compliance ──→ Spdx.Expressions (field reader, catalogue, reference context)
                 └→ NuGet.Versioning (package versions and selectors)
```

The same distribution rules evaluate single expressions and component declarations.
Correction verification runs against the original component data and travels beside
the policy decision in a component report.
