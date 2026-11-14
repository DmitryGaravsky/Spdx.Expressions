# Repository guidance

This is the single source of rules for coding agents working in this repository.
User instructions determine task scope. Tool-specific instruction files, if introduced,
must reference this file rather than duplicate it. Preserve unrelated concurrent changes.

## Scope and current implementation

Spdx.Expressions is a standalone net10.0 library implementing Parse, Validate and Format
for the fixed SPDX-2.3.1 expression profile. It preserves immutable source evidence and
uses a pinned, offline SPDX v3.29.0 licence/exception catalogue. It has no runtime NuGet
dependencies and no dependency on Font.Metadata, CLI, font or SBOM models.

Work directly in this repository for authorized tasks. Integration, external consumers,
reference resolution, external catalogue loading, serialization, automatic updates and
publication require their own tasks. Font.Metadata testing is independent of this project.

## Git ownership

Agents may use read-only Git operations: status, diff, log, show, blame and ls-files.
The user performs operations changing Git or remote state after reviewing the work.
Do not initialize repositories, stage, commit, create/switch branches, manage worktrees,
merge, rebase, stash, reset, clean, create/delete tags, change remotes, fetch, pull or push.
Do not modify Git metadata directly or use wrappers to bypass this rule.
Include a suggested commit title at the end of each reasonably complete implementation
task unless the user explicitly changes this rule.

Keep research, hypothesis experiments, ideas, feature proposals, comparisons, work/review/
validation reports and temporary planning outside this repository. Product and contributor
documentation may retain concise verification status, but must remain self-contained.
Do not link to, import or require content from an external research workspace. Keep local
workspace paths and research transcripts out of published documentation. Generated local
verification outputs remain ignored; preserve historical evidence.

## Read by task

| Task | Document |
| --- | --- |
| Current functionality, API, syntax, limits and catalogue | [README](README.md) |
| Source organization and invariants | [Architecture](docs/architecture.md) |
| Contribution workflow | [Contributing](CONTRIBUTING.md) |
| Verification and package checks | [Testing](docs/testing.md) |
| Hosted build configuration | [CI](docs/ci.md) |
| Packaging and release boundaries | [Release](docs/release.md) |
| Upstream attribution | [Third-party notices](THIRD-PARTY-NOTICES.md) |

Read only what is relevant. Source and tests establish implemented behavior; plans and
configured workflows do not prove that features work or hosted checks have passed.

## Architecture and API

- Keep the solution, library and test project at repository root; no src/tests wrappers.
  Shared test settings belong in eng/Tests.props; package versions in Directory.Packages.props.
- Target net10.0 only. Keep stable SDK selection explicit in global.json; version and roll-forward policy changes require renewed verification.
- Group sources by responsibility: Parsing (parser/options/AST/results), Registry (embedded
  catalogue/identity/entries), Validation (validation/results), Formatting, and Internal.
  Public namespaces remain Spdx.Expressions regardless of folders. Internal shared helpers
  use Spdx.Expressions.Internal. Working package and assembly names are Spdx.Expressions.
- Keep Parse, Validate and Format independently usable. Validate takes an explicit registry;
  Parse and Format must not initialize it. Do not introduce convenience facades without need.
- Preserve immutable results, source order, explicit groups, exact spelling and UTF-16 spans.
  Do not expose mutable backing storage, public AST builders or caller-constructed graphs.
- Public members must serve independent consumers. Do not widen visibility for tests or add
  interfaces, wrappers, DTOs or caches solely to enable test doubles.
- Do not introduce asynchronous/disposable public contracts without corresponding I/O or ownership.

## Parser, catalogue and validation invariants

- Syntax is independent of catalogue membership; unknown identifier-shaped tokens may parse.
  Fully consume input. Syntax/limit failures return one diagnostic and no partial AST.
- Retain all identifier/reference occurrences, including repetitions. Diagnostic storage limits
  must not stop traversal, remove evidence or hide late errors/references from outcome flags.
- Keep identifier errors, unresolved references and diagnostic truncation independent. Do not
  infer usage permission, compatibility or resolved references from a successful operation.
- Keep formatting limited to spacing: preserve casing, suffixes, grouping and operand order.
- Check budgets and arithmetic before growth. Keep traversal safe for long chains and bounded
  nesting. Observe cancellation during whitespace/candidate scanning and traversal.
- Catch expected input failures at deliberate boundaries. Never disguise cancellation,
  programming errors or process-level failures as malformed input.
- Keep messages separate from stable diagnostic codes. Callers must not parse English text.
- Keep catalogue bytes, hashes, commit provenance and notices together. Update paired files
  manually from one reviewed upstream commit and verify every imported entry. Respect binary
  attributes for exact source bytes; do not format or reserialize upstream data/notices.
- Corrupt embedded resources are packaging defects. Do not return an empty successful registry.
  Runtime and builds never download catalogue updates. No shared mutable request state or caches
  keyed by untrusted expression text are allowed.

## Coding conventions

- Use nullable references, explicit using directives and warnings as errors. NUnit is the test framework.
- Follow .editorconfig and .gitattributes; do not inherit unrelated parent settings. Use UTF-8/LF
  for authored files, block-scoped namespaces and the configured brace/spacing conventions.
- Use narrow visibility, omit private when it is the default and make stateless helpers static.
- Use block-bodied methods/constructors. Keep bodies on separate lines, including one-statement
  helpers and test doubles. Only simple property accessors stay compact.
- Put if/loop bodies on the next line. Short closing else/finally clauses may stay compact when
  readable. Keep return, continue, break and throw on separate lines.
- Name intermediate results instead of nesting parsing, snapshots and result construction.
  Extract complex conditions into focused helpers when that improves readability.
- Default absent collections on receiving models where useful. Prefer ReadOnlyCollection<T>.Empty
  and ReadOnlyDictionary<TKey, TValue>.Empty over new wrappers for empty collections.
- Use explicit using blocks for meaningful lifetimes, with braces for multiline bodies.
  Put exception filters on continuation lines and disposal close to ownership.
- Document public API contracts with English XML comments. Internal comments explain non-obvious
  grammar/boundary decisions with primary references, not obvious statements.
- Replace meaningful prefix lengths, limits and similar format constants with named constants.
  Ordinary indexes and independently specified test vectors can remain literals.
- Keep C# diagnostic messages in named constants. Keep PowerShell messages inline and control flow readable.
- Keep fixtures independent of production constants/algorithms. Do not replace expectations
  automatically with current output, or remove useful public contracts merely because unused locally.

## Verification, documentation and publication

- Run [eng/verify.ps1](eng/verify.ps1) after reasonably complete implementation work. Use focused
  checks while iterating; do not run a full baseline automatically at every task start.
- Use --no-restore only with current assets and --no-build only after building relevant changes.
- Verify observable behavior with independent fixtures. Resource/cancellation changes require
  boundary and negative checks; packaging/API changes require actual isolated nupkg consumers.
- Run bounded robustness with external process timeouts. Report failed/interrupted/unavailable
  checks accurately, preserve evidence and never count an unavailable platform as passed.
- Keep docs in English and use relative links. Product docs describe implemented functionality;
  contributor docs describe procedures. Use UTF text diagrams, never Mermaid.
- MIT covers this implementation under Dmitry Garavsky's authorship. Upstream data notices are
  separate. Do not invent repository URLs or claim ownership/availability of a NuGet package name.
- Build CI uses read permissions and does not publish. Local verification and release preparation
  never publish. The separate release workflow publishes only version tags and only the retained
  verified artifacts. The GitHub publisher is reserved for that workflow or an explicitly
  authorized publication task. Never push tags or publish implicitly.
- Inspect the complete change and report tests, limitations and remaining risks. Local success
  does not imply hosted CI passed. At a major phase, review any existing PR description against
  final scope and propose corrected text if needed; GitHub mutation requires authorization.

## Isolated .NET SDK execution

Before the first `dotnet` invocation in an isolated run, including `--version`,
set these values explicitly in the child process environment:

| Variable | Value | Purpose |
| --- | --- | --- |
| `DOTNET_ADD_GLOBAL_TOOLS_TO_PATH` | `0` | Prevent persistent tools-directory additions to PATH |
| `DOTNET_GENERATE_ASPNET_CERTIFICATE` | `false` | Prevent development-certificate generation |
| `DOTNET_CLI_TELEMETRY_OPTOUT` | `1` | Disable CLI telemetry |
| `DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE` | `true` | Disable background workload advertising-manifest downloads |
| `DOTNET_NOLOGO` | `1` | Suppress welcome output; this is not an isolation guard |

- A fresh `DOTNET_CLI_HOME` does not isolate persistent user settings. Apply the
  guards to every SDK process, including preparation, restore, build and nested consumers.
- Do not rely on `DOTNET_SKIP_FIRST_TIME_EXPERIENCE`; modern SDKs do not support it.
- Never persist these overrides or modify user/machine PATH to isolate a test.
  Persistent environment changes require explicit user authorization.
- Prefer child-process environment dictionaries. If temporarily changing the parent
  process environment, restore previous values in `finally`. Do not repurpose HOME or USERPROFILE.
- When adding or changing an isolated launcher, verify a fresh-home run leaves user
  and machine PATH unchanged; on Windows preserve raw values and registry value kinds.
- These settings do not make restore offline. Use explicit local feeds when required.

Reference: [Microsoft SDK environment variables](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-environment-variables).
