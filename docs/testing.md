# Verification

## Full pipeline

```powershell
./eng/verify.ps1
```

Restore uses repository NuGet.Config. An optional `-RestoreSource <local-feed>` selects
an explicit local development feed for offline environments with the pinned dependencies.
Documentation checks and offline release-script tests run first. Restore and test builds
use one MSBuild node. Debug and Release tests run under external process-tree watchdogs.
Whitespace must already match .editorconfig; verification reports changes rather than applying them.
The final Release nupkg is tested through a PackageReference consumer outside the solution
with isolated package/HTTP/CLI caches and a local-only feed. SDK first-run PATH updates
are explicitly disabled; a successful consumer check also requires unchanged user and
machine PATH raw registry values, value existence and registry value kinds on Windows.
The process PATH is checked too. Non-Windows runs mark persistent registry checks as
not applicable. The consumer exercises public node enumeration and walker
cancellation, and compiles both documented traversal examples against the package.
Each invocation retains its package, logs and run.json under a unique
`artifacts/verification/<run-id>` directory, including failed runs. The mutable
`artifacts/latest-verification.json` pointer identifies the latest run and its status.
Optional Version applies one validated version to restore, builds and packing; the
isolated consumer compares package and loaded assembly versions. OutputDirectory can
select a new evidence directory; an existing directory is rejected.

## Focused checks

```powershell
./eng/check-docs.ps1
./eng/test-docs.ps1
dotnet restore Spdx.Expressions.slnx --configfile NuGet.Config
dotnet build Spdx.Expressions.slnx -c Release --no-restore
dotnet test Spdx.Expressions.Tests/Spdx.Expressions.Tests.csproj -c Release --no-build --no-restore --filter GrammarTests
dotnet format whitespace Spdx.Expressions.slnx --verify-no-changes --no-restore
$run = Get-Content ./artifacts/latest-verification.json -Raw | ConvertFrom-Json
./eng/package-smoke.ps1 -Package $run.package
```

Only use no-restore/no-build when assets/build outputs reflect current edits. For bounded
full tests, use verify.ps1 rather than an unbounded direct test invocation. The consumer
driver also verifies package metadata, XML docs, LICENSE, upstream notices and data hashes.
It reads the exact version from the package, uses an exact PackageReference constraint,
and compares the loaded DLL bytes with the supplied artifact. It also checks the GitHub
project/repository URLs, repository type, author, license and assembly informational version. A caller-specified output
directory must not already exist; individual command logs also cannot be overwritten.

Coverage includes independent grammar/AST/span/diagnostic fixtures, all catalogue entries,
resource boundaries, cancellation, concurrency/culture, golden formatting, 196 generated
roundtrips and a reproducible 600-case mutation set (seed 231). This is not a benchmark,
long fuzzing campaign or complete security audit.
Mutation coverage assertions require all four seed expressions, all three operations per
seed, every mutation character (including NUL and a surrogate), and insertion at EOF.

Reference resolution tests reuse the same local IDs and external aliases across documents.
They check the target identity, verbatim text and UTF-16 spans for each occurrence, all
unresolved outcomes, duplicate declarations, context budgets and cancellation. The
[document-scoped example](usage.md#resolve-references-in-a-document) exercises the public
API through the packaged assembly.

Six additional robustness cases exercise long mixed AND/OR chains, late reference/exception
and error evidence after diagnostic truncation, and exact EOF failures after long chains.
A separate bounded package study passed 4,096 independently constructed expressions,
20,000 seeded mutations, 65,464 disallowed UTF-16 code units at a fixed token boundary,
and 13 large-input/budget cases. Those study inputs are not additional NUnit cases or a CI
dependency. Same-build timing/allocation controls provide a local exploratory baseline,
not a performance threshold, cross-platform result or exhaustive fuzzing claim.

Three deterministic cancellation cases suspend the shared traversal enumerator after
root entry, a nested leaf, and between OR operands. Cancellation must make the next
MoveNext throw before yielding more work, with the original token. The test assembly
alone receives internal access for these checks; no public test hook is needed.
The public enumeration and walker have their own coverage below. Removing the in-loop
cancellation check in an isolated copy makes all three cases fail. Existing public-operation
cancellation tests remain smoke coverage; they do not independently prove polling latency.

Preserve the exact package/hash and failed evidence when changing a candidate.
A prepared workflow/script is not proof of a passing Windows/Linux/macOS run.

## Specification conformance

The project-maintained corpus in [ConformanceTests](../Spdx.Expressions.Tests/ConformanceTests.cs)
uses [SPDX 2.3.1 Annex D](https://spdx.github.io/spdx-spec/v2.3.1/SPDX-license-expressions/)
and the [fixed syntax profile](syntax.md). It is not an official SPDX certification suite.
Named cases carry Source or Profile metadata; inputs, expected trees, formatting and
diagnostic coordinates are literal fixtures, not captured parser output. The shared tree
description helper only observes the AST. Roundtrip/idempotence checks supplement these
independent expectations; they cannot establish conformance on their own.

The corpus includes 14 expression examples from D.3 through D.4.5, plus derived combinations
and counterexamples. Catalogue-sensitive examples explicitly use Default version 3.29.0;
RegistryTests independently pins its exact source hashes and verifies every imported entry.
Existing fixtures remain the authority for already covered boundaries:

| Rule or policy | Positive and negative evidence |
| --- | --- |
| D.1/D.3 simple licenses and adjacent plus | ConformanceTests.SimpleExamples; GrammarTests.IndependentFailures rejects separated/repeated plus and suffixes on references/exceptions; DerivedGrammarFailures adds tab separation and grouped suffixes. |
| D.1 reference components | SimpleExamples, IdentifierAndKeywordBoundaries and GrammarTests.IndependentStructures cover local/document references, punctuation and reserved words inside components; ReferenceBoundaries and IndependentFailures cover empty/malformed components and prefixes. |
| D.1 operator boundaries and complete consumption | IndependentStructures accepts parentheses as AND/OR separators; IndependentFailures covers WITH spacing, comments, declarations and trailing prose; DerivedGrammarFailures adds missing/repeated OR, adjacent groups and mixed missing operands; ComparisonCounterexamples rejects operator substrings and trailing operands after WITH. |
| D.2 operator case | OperatorsAreCaseSensitive and IndependentFailures reject lowercase/mixed-case operators; uppercase operators appear in every successful composite fixture. |
| D.2 identifier case | D2_CatalogueMatchingPreservesOriginalEvidence checks mixed license/exception case, canonical lookup and original spans; RegistryTests.ConcurrentCultureIndependentOperations covers culture/concurrency. |
| D.4.2/D.4.3 binary operators and source order | DisjunctionExamples and ConjunctionExamples cover both orders and three operands; IndependentStructures preserves duplicates and explicit groups; DerivedStructures covers grouped OR chains. |
| D.4.4 WITH operand kinds | D44_ExceptionExample and IndependentStructures cover license, plus and reference operands; IndependentFailures/DerivedGrammarFailures reject repeated WITH, grouped left operands, grouped exceptions and reference exceptions. |
| D.4.5 precedence | PrecedenceExamples checks the published pair; DerivedStructures combines all four precedence levels, conjunctions on both sides of OR and group overrides. |
| Profile: reference casing and colon adjacency | IndependentStructures treats lowercase prefixes as ordinary candidates; IdentifierAndKeywordBoundaries requires the prefix's hyphen; IndependentFailures rejects malformed recognized prefixes; ReferenceBoundaries rejects space/tab around the colon; ComparisonCounterexamples pins empty-component arbitration before invalid characters. |
| Profile: whitespace, Unicode and UTF-16 evidence | Profile_WhitespaceDoesNotApplyTagValueLineRestrictions accepts space/TAB/CR/LF; InvalidCharactersStopBeforeLaterEvidence rejects other whitespace and surrogate pairs; IndependentFailures covers NBSP/NUL/lone surrogates; ExactNodeAndComponentSpans pins node/component coordinates and failure cases pin EOF. |
| Profile: document sentinels and whole tokens | FieldSentinelsAreNotExpressions and IndependentFailures reject NONE/NOASSERTION in ID positions; IndependentStructures accepts lowercase candidates/reference components; IdentifierAndKeywordBoundaries accepts longer identifiers containing keywords. |
| Profile: parsing versus catalogue validation | Profile_IdentifierCandidatesDoNotRequireCatalogueMembership accepts unknown syntax then checks unknown license/exception diagnostics and unresolved references; ValidationTests covers known/unknown/wrong-kind/deprecated IDs, occurrence order and independent truncation flags. |
| Profile: source-preserving formatting | FormattingTests.IndependentGoldenOutputs and Profile_WhitespaceDoesNotApplyTagValueLineRestrictions specify literal outputs; examples and DerivedStructures preserve casing, plus, groups and order. |
| Profile: explicit catalogue casing | CanonicalFormattingTests checks fixed goldens with embedded and selected registries, source evidence and structural roundtrip, legacy plus lookup, culture, wide chains, depth and long text. The actual package consumer uses both external load paths and the embedded registry. |
| Profile: budgets and cancellation | ResourceTests covers inclusive limits, exact limit spans, long chains and cancellation during scanning/traversal; these are engineering contracts, not Annex D rules. |

Interpretation boundaries are deliberate:

- Annex D's license/exception productions refer to catalogue membership. Parse recognizes
  candidates; Validate performs the separate membership check. Parse success alone does
  not establish a valid SPDX document or resolve a LicenseRef.
- ASCII whitespace, exact reference prefix casing, contiguous reference tokens and reserved
  field sentinels are explicit profile choices. Diagnostic codes, UTF-16 coordinates and
  first-error arbitration are library contracts, not requirements imposed by Annex D.
- D.1's tag:value single-line restriction belongs to document serialization. D.4.6 RDF
  representation is outside the expression API. Document-scoped resolution has separate
  fixtures for caller-supplied bindings and extracted definitions.
- D.4.2/D.4.3 commutativity does not require sorting the source-preserving AST. Semantic
  equivalence, license compatibility and legal conclusions are outside these tests.

The real PackageReference consumer checks a representative precedence and rejection subset
against the exact nupkg. These specification-derived fixtures alone do not prove exhaustive
conformance or agreement with other parsers. Hosted verification must run on the actual candidate independently of
local results.

A bounded 68-input comparison on Windows used spdx-expression-parse 5.0.0 with
spdx-license-ids 3.0.24 / spdx-exceptions 2.5.0, and Tethys.SPDX.ExpressionParser 2.2.0
with Default options and case-insensitive lookup callbacks over the same 3.29.0 catalogue
used here. **68/68** counts inputs for which Spdx.Expressions met the independently
specified expectations for its syntax profile. It accepted 32 inputs and rejected 36.
The checks covered the parse outcome for every input, the expected tree on success
and diagnostic positions where specified.

Each pairwise tree comparison used only expressions accepted by both Spdx.Expressions
and the parser in that row. The numerator counts matching trees within that subset.

| Parser compared with Spdx.Expressions | Matching trees / jointly accepted expressions |
| --- | --- |
| spdx-expression-parse 5.0.0 | 20/20 |
| Tethys.SPDX.ExpressionParser 2.2.0 | 19/23 |

The four differing Tethys trees involved two omitted WITH clauses and two differences
in reference recognition.
ComparisonCounterexamples retains additional full-consumption and diagnostic-arbitration
fixtures. No production behavior changed. Peers differ in catalogue checks, casing,
whitespace, references and error handling; their acceptance is not this profile's contract.
The common AST comparison retained operator order, plus, WITH and leaf spelling, removing
only explicit group wrappers and flattening same-operator chains. Full source/group/span
contracts remain checked independently. This comparison is not a performance or security
assessment; external comparator tools are not product dependencies.

## Performance measurements

On 22 November 2026, a single-threaded Release consumer measured the published
NuGet packages on an Intel Core i7-12700H, Windows x64 build 26200, with SDK
10.0.404, runtime 10.0.15, workstation GC and default tiered compilation. The
consumer used an exact PackageReference for each version and verified the SHA-256
of its library DLL against the archive. The downloaded package digests were:

- 1.0.0 — `6f629bcf541052569334c3a45005697a29f955edf52c93a815ead2a37f555484`
- 1.1.0 — `361b1776912eac8b98f102ed22b46add4ddb222989217f53eb12e03193654be3`

Each cell gives the median and the observed minimum–maximum across nine fresh
processes. Allocation figures are median managed bytes allocated on the calling
thread per operation. The field reader and reference context first shipped in 1.1.0.

| Operation and unit | 1.0.0 median (range) | 1.1.0 median (range) | Bytes/op 1.0.0 | Bytes/op 1.1.0 |
| --- | ---: | ---: | ---: | ---: |
| First `SpdxLicenseRegistry.Default` access, ms | 26.3 (24.4–29.1) | 37.5 (37.0–43.6) | 2,729,928 | 2,996,544 |
| `Parse` + `Validate`, million fields/s | 1.164 (1.075–1.176) | 1.153 (1.110–1.184) | 966 | 966 |
| `Read`, million fields/s | — | 2.154 (2.021–2.226) | — | 490 |
| `Read` + `Validate`, million fields/s | — | 1.561 (1.390–1.645) | — | 913.6 |
| `SpdxReferenceContext.Create`, µs/context | — | 13.92 (12.05–16.41) | — | 43,424 |
| `Resolve`, µs/expression of four references | — | 0.315 (0.307–0.378) | — | 672 |

The first-access timer covered the getter only and excluded process launch.
First access cost about 11 ms more in 1.1.0 on this host. For the shared warm
`Parse` + `Validate` workload, the ranges overlapped and allocations were equal.
A second set of nine processes using the identical 1.1.0 binary gave medians of
39.8 ms for first access and 1.177 million fields/s for the shared workload.
The difference between the two warm release medians was smaller than the
difference between the two measurements of the identical binary.

**Field workload.** Each batch cycled through the following 20 non-null strings
in order 5,000 times. Validation ran only for successful expressions and reused
the loaded registry. Both entry points produced 14 expressions per cycle. The
strict parser rejected the other six fields. The field reader classified three
as invalid syntax and recognized the empty string and both sentinels.

```json
["MIT", "Apache-2.0", " ISC ", "BSD-3-Clause", "mit", "MIT OR Apache-2.0",
 "(MIT OR ISC) AND MIT", "MIT WITH LLVM-exception", "GPL-2.0", "LicenseRef-X",
 "DocumentRef-V:LicenseRef-X OR MIT", "LicenseRef-X OR LicenseRef-X",
 "Unknown-1.0", "MIT AND Unknown-1.0", "", " NONE ", "NOASSERTION", "MIT OR",
 "(MIT", "MIT\u00a0OR ISC"]
```

**Reference workload.** Each creation batch indexed the same prebuilt inputs
2,000 times: 32 documents, 16 definitions per document and one alias per
document bound to the next document. Each resolution batch reused one context
and one pre-parsed expression, rotating the source document over 100,000 calls.
Its four occurrences selected a local definition, a qualified definition, a
missing local definition and an unknown alias.

For warm measurements, each worker checked expected outcomes before warm-up,
repeated complete batches for at least one second, then timed one batch with
`Stopwatch`. Result counts were consumed inside the batch and checked afterward.
Package versions and the same-binary control rotated through the serial run order.
These local observations describe the listed workloads and machine. Their ranges
measure variation between batches across processes, not individual field latency.

### First access after the embedded reader change

A second comparison on the same host measured the published 1.1.0 package and a
locally packed Release build with the pinned embedded reader. Both carried version
1.1.0. Each consumer restored an exact PackageReference from its own feed into an
empty package cache. The consumer executable was byte-identical in both runs, and
each loaded library matched the DLL in its archive. The local package SHA-256 was
`e0d384ecc172a4b8d07bb0c9899aee17045aac355d8a610ba5a57b162409e9aa`.

| Package | First access, median ms (min–max) | Median managed bytes on calling thread |
| --- | ---: | ---: |
| Published 1.1.0 | 38.38 (36.86–39.22) | 2,996,544 |
| Local build with pinned reader | 23.03 (22.32–23.61) | 2,012,368 |

Each of 12 rounds timed the first `Default` getter in a fresh process for each
package, alternating which package ran first. Process launch and catalogue checks
were outside the timer. The local build was faster in all 12 pairs, with a median
paired saving of 14.99 ms (14.05–16.67 ms) and 984,176 fewer bytes allocated
when comparing allocation medians. A separate check compared `Default` and both
external `Load` overloads across packages. The catalogue identity, 740 licenses,
86 exceptions and all 1,125 additional links matched, including entry and link order.

### Plain formatting of a single license or reference

On the same Windows x64 host, a Release consumer compared the prepared 1.1.1
archive with a separate local 1.1.1 package containing the plain formatter change
(SHA-256 `3589e8d22427f4946e9a78d9eea8f91ec94fecf982bf5af5dc87471b576bb259`).
Each package was restored from its own feed and empty cache. Six paired rounds
alternated package order, using the same consumer binary and a fresh process per
package per round. Each case warmed up for at least 1.2 seconds. The table gives
median batch times per call and managed bytes allocated on the calling thread.

| Operation | Before, ns | After, ns | Before, B | After, B |
| --- | ---: | ---: | ---: | ---: |
| Format parsed `mit` | 100.4 | 3.1 | 352 | 0 |
| Format parsed `LicenseRef-X` | 110.4 | 3.8 | 368 | 0 |
| Format parsed `DocumentRef-D:LicenseRef-X` | 146.4 | 4.0 | 504 | 0 |
| Parse then format `mit` | 156.5 | 62.7 | 608 | 256 |

All four cases were faster in every pair. The shortest times include the benchmark
loop and delegate call. A control expression with groups, boolean operators and
`WITH` used the renderer in both packages and allocated 1,096 B per call in each.

## Documentation and runnable examples

`eng/check-docs.ps1` validates authored inline Markdown links, exact file casing and heading
anchors in root documents and docs/*.md. It excludes fenced code, external URLs and verbatim
upstream notices. This is a check of the repository's Markdown conventions, not a general
Markdown renderer or external-link availability check.

Each `example` marker in README/Usage pairs a complete C# program with expected text output.
The checker requires a one-to-one match with named source regions in
Spdx.Expressions.Examples/Program.cs. Edit both deliberately; expected output is not generated
from the running library. `eng/test-docs.ps1` uses isolated fixtures to reject missing files,
wrong path casing, missing anchors, altered snippets, unpaired example markers/programs,
missing expected output, relative package links, mismatched repository commits and unintended
package README changes. Rejection fixtures check the failure reason so unrelated exceptions
cannot count as successful rejection. Failed fixture runs retain a result.json too.
The MSBuild readme-generator fixture checks exact output, including unchanged fenced code,
external links, a nested status badge and anchor links.

The full pipeline builds the examples project and includes it in whitespace checks. After
packing, the smoke driver copies its source into an isolated PackageReference project, builds
it against the exact local nupkg, runs every documented scenario under a process timeout and
compares stdout with the expected blocks and requires empty stderr. It also compiles and
executes each documented C# block as a separate console program; helpers or using directives
in the example runner cannot mask incomplete snippets. The same driver checks the package
README preserves all prose and expands repository-relative links to the nuspec commit. No ProjectReference or internal API is used
for these package runs. Fresh SDK homes, local-only feeds and PATH checks cover them too.

Documentation logs and fixture results are retained with each verification run. To check an
existing package README alone, use `./eng/check-docs.ps1 -Package <path-to-nupkg>`. To execute
the examples against it, use `./eng/package-smoke.ps1 -Package <path-to-nupkg>`.

## Caller-selected catalogue checks

The registry tests replay independent schema, encoding, identity and budget
fixtures through memory, seekable and non-seekable stream paths. They cover
short reads, byte probes, ownership, cancellation and I/O ordering, plus
compatibility with the shipped pair. The package check restores the actual
nupkg through an exact PackageReference from an isolated local feed. Its
consumer loads a synthetic pair from files through both overloads, checks
literal expected hashes and entries, and compares selected and embedded
validation results. The standalone memory and stream examples run against
that package as well.

Record the candidate package hash and the source revision with verification
results. See [CI](ci.md) for matrix coverage. The routine pipeline uses only
fixtures and data available in the source checkout.

## Release automation checks

```powershell
./eng/test-release.ps1
./eng/prepare-release.ps1 -Tag v1.1.1
```

The standalone script tests stable/prerelease tags, malformed versions, package
checksums, file sets, nuspec metadata and required contents. A mocked GitHub CLI tests
draft publication, matching retries, conflicting assets, incomplete published releases,
partial upload recovery and failure boundaries. They also cover relative paths after
Set-Location and missing or removed tags when creating or resuming drafts. Synthetic payloads do not replace the
real nupkg consumer. No release-test operation uses the network or credentials.

Preparation runs the full pipeline for the tag version and copies the exact verified
package with SHA256SUMS. It rejects an existing release directory before building.
Neither command publishes or changes Git state. See [Release](release.md) for the
separate tag workflow, publication prerequisites and retry rules.

## SDK isolation

Invoke-Checked explicitly sets all five SDK guards in each child process environment,
after caller-supplied overrides: DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=0,
DOTNET_GENERATE_ASPNET_CERTIFICATE=false, DOTNET_CLI_TELEMETRY_OPTOUT=1,
DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true and DOTNET_NOLOGO=1.
Nested processes inherit them. DOTNET_NOLOGO only suppresses output; a new CLI home
alone does not isolate persistent settings. The launcher does not rely on the obsolete
DOTNET_SKIP_FIRST_TIME_EXPERIENCE setting and never writes persistent environment settings.

The complete pipeline and the external consumer each use a fresh DOTNET_CLI_HOME.
Their first SDK command is --version; an MSBuild check then asserts the actual child
environment before restore/build/test/pack. CI job guards also cover SDK preparation.
Direct dotnet commands above do not use this launcher; prefer the scripts for isolated runs.

Each run retains path-state.json with before/after snapshots and compares them in
finally, including failed runs. Windows snapshots use read-only registry access with
DoNotExpandEnvironmentNames; they retain raw values and value kinds, without rewriting
or repairing PATH. Command records include only the five guards, not the full environment.
CLI homes, caches and PATH snapshots remain local ignored artifacts; CI uploads the
snapshots and command logs but does not upload CLI homes or caches.

These settings do not make NuGet restore offline. Use RestoreSource for the main
pipeline when necessary; the external consumer always has an explicit local-only feed.

Published documentation must be self-contained and link only to files in this
repository or available public resources.
Generated output paths in these instructions describe files created by a local run;
they are not links to files shipped in the repository.

## Release verification

### 1.1.1

Final local release preparation on Windows x64 passed with .NET SDK 10.0.404.
All 869 NUnit tests passed in each of Debug and Release, with zero skips.
All 20 example scenarios and all 20 independently compiled documentation snippets
produced the expected output against the final 1.1.1 package. Its SHA-256 was
`ab556c156de00121ed6daa8c0a187d41cc4b1ba6eab4fb6647e5db8762f42814`.
Its library DLL matched the one used in the [plain formatter comparison](#plain-formatting-of-a-single-license-or-reference) byte for byte.

The [first-access benchmark](#first-access-after-the-embedded-reader-change) in the
release notes compared an earlier 1.1.1 archive (SHA-256
`165816a0638dfa8138276e2a7f20ad660158edc8e30caee77856627c7ed6cfc8`)
with the published 1.1.0 package on .NET 10.0.15.
Each of 12 rounds used a fresh process per version and alternated the run order.
The median time for the first `Default` getter fell from **38.13 ms to 22.67 ms**.
The 1.1.1 package was faster in all 12 pairs, with a median paired saving of
**15.05 ms** (12.96–16.08 ms). Median calling-thread allocation fell from
2,996,544 to 2,012,368 bytes. Both packages returned the same catalogue identity,
740 licenses, 86 exceptions and 1,125 links through `Default` and both external
`Load` overloads.

On the final archive, repeating the same comparisons gave a median cold `Default`
time of 22.57 ms against 37.86 ms for published 1.1.0 (12/12 pairs faster).
Plain `Format` allocated zero managed bytes for each of the three parsed
single-license/reference inputs. Parsing and formatting `mit` together took
47.5 ns against 113.0 ns for the earlier 1.1.1 package, with 256 versus 608
managed bytes per call. All four formatter cases were faster in all six pairs.

### 1.1.0

The local Windows verification run used .NET SDK 10.0.404. All 866 NUnit tests
passed in Debug and all 866 passed in Release, with zero failures or skips.
Against the locally built NuGet package, all 20 example scenarios produced the
documented output. All 20 documented programs also compiled and ran independently
against that package and produced the expected output.

The [v1.1.0 release workflow](https://github.com/DmitryGaravsky/Spdx.Expressions/actions/runs/44910880991)
passed for commit `7be85c3`.

### 1.0.0

The 1.0.0 API and package have passed the complete local verification pipeline
on Windows with .NET SDK 10.0.404 and runtime 10.0.15:

| Check | Result |
| --- | --- |
| Debug and Release NUnit suites | 815 passed in each configuration; zero failures or skips |
| Builds and formatting | Passed; zero build warnings or errors |
| Actual-package examples | 16 scenarios and 16 independently compiled programs passed |
| Documentation fixtures | 24 cases passed; links and source blocks verified |
| Offline release-script checks | 46 cases passed without publication credentials |
| Package identity and contents | Version, XML docs, README, notices and embedded data verified |
| SDK isolation | Required guards applied; persistent PATH unchanged |

The merged implementation passed the
[Ubuntu, Windows and macOS main CI run](https://github.com/DmitryGaravsky/Spdx.Expressions/actions/runs/44354643541).
Each tagged release independently runs the complete three-platform verification
before publishing its retained artifact. Use that release's workflow result and
checksums to identify the distributed package.

These checks support the [1.x compatibility contract](compatibility.md). Bounded
robustness and differential checks do not constitute exhaustive conformance,
security certification or a performance SLA.
