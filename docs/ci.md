# Continuous integration

The [build workflow](../.github/workflows/ci.yml) runs on pushes to main, pull requests
and manual dispatch. Pull requests check Ubuntu/Windows; main/manual runs also check
macOS. Feature-branch pushes do not add duplicate PR verification.

Each job installs the SDK declared in global.json using
[setup-dotnet's global-json-file input](https://github.com/actions/setup-dotnet/blob/main/action.yml)
and executes the same eng/verify.ps1 pipeline as local development:

```text
Offline release-script tests ──→ Restore ──→ Debug/Release tests
                                                  │
                                                  ▼
                                      whitespace verification
                                                  │
                                                  ▼
                           Release nupkg ──→ isolated PackageReference consumer
                                                  │
                                                  ▼
                                      retain logs and verified artifacts
```

Jobs have a 20-minute timeout. OS failures do not cancel other matrix entries.
Scripts also bound SDK child processes and preserve stdout/stderr. Results/logs are
uploaded even after failures; a successful Linux job retains the development package.
Temporary consumer caches and CLI homes are not uploaded. Build artifacts include the
run attempt in their names so retries retain earlier evidence without name collisions.

Build CI has contents: read, does not persist checkout credentials and never publishes.
The separate [release workflow](../.github/workflows/release.yml) starts only on version
tags. It requires verification on all three platforms and publishes the exact retained
Linux artifact. NuGet authentication and GitHub write permissions are confined to its
publication jobs. See [Release](release.md) for setup, version policy and retries.

Both workflows apply SDK isolation guards, including before SDK preparation. Script
tests use a mocked GitHub CLI and synthetic packages, so normal CI does not need
publication credentials.

This describes configuration, not a completed hosted run. Local Windows success does
not establish Linux/macOS or GitHub Actions success. Actual results are in [Testing](testing.md).
