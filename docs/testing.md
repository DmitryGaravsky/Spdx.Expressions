# Verification

## Full pipeline

```powershell
./eng/verify.ps1
```

Restore uses repository NuGet.Config. An optional `-RestoreSource <local-feed>` selects
an explicit local development feed for offline environments with the pinned dependencies.
Offline release-script tests run first. Debug and Release tests run under external process-tree watchdogs. Whitespace must already
match .editorconfig; verification reports formatting changes rather than applying them.
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
| Development and tag-version packages with external PackageReference consumers | Passed (0.1.0-dev and 0.1.0-preview.2) |
| Offline release scripts | 24 scenarios passed; mocked GitHub CLI, no publication |
| Release preparation and SHA256SUMS validation | Passed; existing output rejected without changes |
| Build/release workflow static validation | Passed with actionlint 1.7.12 |
| Fresh CLI homes and required SDK environment guards | Passed |
| User/machine PATH raw registry values and kinds | Unchanged |
| Linux/macOS and GitHub-hosted CI | Not executed; unverified |

The completed pipeline used an explicit local feed for the pinned test dependencies.
The package consumer used a separate local-only feed and fresh caches. Local success
does not establish hosted or cross-platform success; the required Linux package check
remains outstanding. Work reports and detailed historical analysis are not part of the
published source tree and are not required to build, test or use the library.
