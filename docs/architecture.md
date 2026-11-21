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

Embedded JSON streams ─┐
                       ├──→ bounded pair loader ──→ registry entries with names and links
Selected bytes/streams ┘                              and exact-byte catalogue identity
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
| eng | Verification driver, process watchdog and external package consumer |

Public types always use Spdx.Expressions; folder names do not change the API namespace.
Internal shared helpers use Spdx.Expressions.Internal. AST constructors are non-public.
Collections do not expose mutable storage. Options are immutable and operations have
no shared mutable request state. The embedded registry is a lazy, thread-safe immutable
snapshot. Embedded resources and external memory and stream inputs share document parsing,
display metadata extraction and pair checks. The embedded snapshot also verifies its pinned identity.
Each load owns its byte snapshots and budgets, then constructs an independent immutable
registry. Parse, ordinary Format and AST traversal work independently of the catalogue.
Validation and opt-in canonical casing each use their explicitly supplied registry.

Parser recursion is bounded by explicit group depth; operator chains are flat lists.
Enumeration projects entry frames from the explicit stack shared with validation and
formatting. The walker dispatches the same nodes to typed callbacks. Diagnostic storage
never limits occurrence collection or changes error/reference flags. The shared renderer
copies source slices for ordinary Format and looks up the expected namespace for
eligible IDs in FormatCanonicalCasing. Both paths preserve the original AST.

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
