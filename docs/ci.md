# Continuous integration

The [build workflow](../.github/workflows/ci.yml) runs on pushes to main, pull requests
and manual dispatch. Pull requests check Ubuntu/Windows; main/manual runs also check
macOS. Feature-branch pushes do not add duplicate PR verification.

Each job installs the SDK declared in global.json using
[setup-dotnet's global-json-file input](https://github.com/actions/setup-dotnet/blob/main/action.yml)
and executes the same eng/verify.ps1 pipeline as local development, including documentation
links/snippets, documentation fixtures and runnable examples against both packed libraries.
The Expressions package consumer loads a synthetic catalogue pair from files through both
overloads and checks its hashes and validation results alongside the embedded snapshot.
The Compliance consumer restores the exact candidate from a dedicated local feed into
an empty cache, checks the installed package and assembly hashes, and runs the policy
and CycloneDX example. Development CI supplies the paired Expressions build on the same feed.

```text
Docs + offline script tests ──→ Restore ──→ Debug/Release tests
                                                  │
                                                  ▼
                                      whitespace verification
                                                  │
                                                  ▼
                           Both nupkgs ──→ isolated consumers + examples
                                                  │
                                                  ▼
                                      retain logs and verified artifacts
```

Jobs have a 20-minute timeout. OS failures do not cancel other matrix entries.
Scripts also bound SDK child processes and preserve stdout/stderr. Results/logs are
uploaded even after failures. A successful Linux job retains both development packages.
Temporary consumer caches and CLI homes are not uploaded. Build artifacts include the
run attempt in their names so retries retain earlier evidence without name collisions.

Build CI has contents: read, does not persist checkout credentials and never publishes.
The separate [release workflow](../.github/workflows/release.yml) starts when a GitHub
release is published for a version tag. It can reuse the Linux nupkg from a successful
three-platform main CI run at the exact release commit. It checks the package identity,
version, commit and CI-recorded SHA-256. If no verified candidate is available, the
release runs its own three-platform build. Both paths test Compliance against public
NuGet dependencies before publication.
NuGet authentication and GitHub write permissions are confined to its publication jobs.
Existing NuGet versions must match the verified payload; GitHub releases retain their
user-authored text and receive the package and checksum attachments. See [Release](release.md)
for setup, version policy, manual protections and retries.

Both workflows apply SDK isolation guards, including before SDK preparation. Script
tests substitute GitHub and HTTP commands and use synthetic packages, so normal CI
does not need publication credentials. Both workflows pin actions to full commit SHAs;
[Dependabot](../.github/dependabot.yml) proposes weekly updates without automatic merging.

See the [build runs](https://github.com/DmitryGaravsky/Spdx.Expressions/actions/workflows/ci.yml)
and [release runs](https://github.com/DmitryGaravsky/Spdx.Expressions/actions/workflows/release.yml)
for hosted results by commit and tag. [Testing](testing.md) describes the local checks.
