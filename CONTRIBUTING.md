# Contributing

[AGENTS.md](AGENTS.md) is the single source of repository rules.
[Architecture](docs/architecture.md) describes the implemented structure.

## Setup

Install PowerShell 7 and a stable .NET SDK matching the version and roll-forward policy in global.json.
All projects target net10.0. Dependencies are pinned centrally; NuGet.Config explicitly
selects nuget.org rather than inheriting machine-specific feeds.

```powershell
./eng/verify.ps1
```

The pipeline tests release scripts without network or credentials, restores,
runs Debug/Release NUnit tests under process timeouts, checks
C# whitespace, packs Release and tests the actual package with an isolated external consumer.
See [Testing](docs/testing.md) for focused commands and [CI](docs/ci.md) for the hosted matrix.

## Changes and review

- Keep changes focused; preserve unrelated work. Review the whole diff and affected contracts.
- Keep public XML/README documentation in English and aligned with actual behavior.
- Use independent grammar, AST, span and resource fixtures. Do not mirror production algorithms.
- Preserve exact upstream JSON/notice bytes and provenance; do not run formatters over Data.
- Verify appropriately while iterating. Do not run a full baseline at every task start.
- Use --no-restore only with current assets; rebuild before using --no-build after edits.
- Keep research/planning outside the product tree; requested validation evidence stays explicit.
- Do not change Git state, publish or modify external consumers without an explicit request.

## Formatting and licensing

Use .editorconfig and .gitattributes for explicit usings, block-scoped namespaces, brace
placement, UTF-8 and LF. Diagrams use UTF text, not Mermaid. See AGENTS.md for the manual
conventions beyond dotnet format whitespace.

The implementation uses the [MIT license](LICENSE), copyright Dmitry Garavsky.
The embedded SPDX dataset has separate [upstream notices](THIRD-PARTY-NOTICES.md).
Release identity and publication procedures are described in [Release](docs/release.md).
