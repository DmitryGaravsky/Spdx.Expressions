# Architecture

Spdx.Expressions is an independent net10.0 library with no runtime NuGet dependencies.
Its grammar, catalogue and package identities are separate. Font.Metadata is not a
dependency, and its testing/release process does not gate this project's development.

```text
Source string ──→ Parse ──→ immutable AST + source/spans
                      │              │
                      │              ├──→ Format ──→ deterministic spacing
                      │              │
                      │              └──→ Validate ←── explicit immutable registry
                      │                         │
                      │                         └──→ all occurrences + independent flags
                      └──→ syntax/limit diagnostic, no partial AST
```

| Location | Responsibility |
| --- | --- |
| Spdx.Expressions/Parsing | Parser, options, diagnostics, AST and parse results |
| Spdx.Expressions/Registry | Fixed embedded catalogue, minimal entries and identity |
| Spdx.Expressions/Validation | Complete occurrence collection and bounded diagnostics |
| Spdx.Expressions/Formatting | Spacing-only output |
| Spdx.Expressions/Internal | Explicit-stack syntax traversal shared by operations |
| Spdx.Expressions/Data | Exact upstream source bytes, provenance and notices |
| Spdx.Expressions.Tests | Independent NUnit fixtures |
| eng | Verification driver, process watchdog and external package consumer |

Public types always use Spdx.Expressions; folder names do not change the API namespace.
Internal shared helpers use Spdx.Expressions.Internal. AST constructors are non-public.
Collections do not expose mutable storage. Options are immutable and operations have
no shared mutable request state. The registry is a lazy, thread-safe immutable snapshot.

Parser recursion is bounded by explicit group depth; operator chains are flat lists.
Validator/formatter traverse with an explicit stack. Diagnostic storage never limits
occurrence collection or changes error/reference flags. Format does no catalogue lookup.

The [README](../README.md) specifies lexical policies, spans, errors, catalogue identity
and resource limits. Keep those contracts and independently specified fixtures aligned.
