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
not applicable. No internal access is used.
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

Preserve the exact package/hash and failed evidence when changing a candidate.
A prepared workflow/script is not proof of a passing Windows/Linux/macOS run.

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

## Release automation checks

```powershell
./eng/test-release.ps1
./eng/prepare-release.ps1 -Tag v0.1.0-preview.1
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

## Verification status

As of 2026-11-14, local Windows checks with SDK 10.0.404 selected by global.json passed:

| Check | Result |
| --- | --- |
| Debug and Release compilation | Passed; zero warnings and errors |
| Debug NUnit | 127 passed, 0 failed, 0 skipped |
| Release NUnit | 127 passed, 0 failed, 0 skipped |
| Solution and consumer formatting | Passed |
| Documentation links and source blocks | 68 relative links and 11 example programs passed |
| Documentation checker fixtures | 21 scenarios passed |
| Documented example output against the development nupkg | 11 scenarios and 11 standalone programs passed |
| Local package setup commands and standalone quick-start program | Passed with a fresh application and package cache |
| Development package with external PackageReference consumers | Passed (0.1.0-dev) |
| Offline release scripts | 46 scenarios passed; substituted GitHub/HTTP commands, no publication |
| Tag-version release preparation | Not rerun for this update |
| Build/release workflow actionlint validation | Passed with actionlint 1.7.12; Dependabot YAML syntax also checked |
| Read-only release checks against v0.1.0 | Passed: tag/main ancestry, package commit, GitHub checksums and NuGet payload comparison |
| Fresh CLI homes and required SDK environment guards | Passed |
| User/machine PATH raw registry values and kinds | Unchanged |
| Revised UI-triggered workflow on GitHub | Not executed; hosted verification remains required |

The completed pipeline used an explicit local feed for the pinned test dependencies.
The package consumer used a separate local-only feed and fresh caches. The earlier
[v0.1.0 release run](https://github.com/DmitryGaravsky/Spdx.Expressions/actions/runs/43598068968)
passed on Windows, Linux and macOS and published the package. Local success and that
historical run do not establish hosted success for the revised UI-triggered workflow.
The read-only checks downloaded existing public artifacts and did not modify either
service. Work reports and detailed historical analysis are not part of the published
source tree and are not required to build, test or use the library.
