# Packaging and releases

The repository is [DmitryGaravsky/Spdx.Expressions](https://github.com/DmitryGaravsky/Spdx.Expressions).
Shared package metadata in Directory.Build.props records this project/repository URL,
Git repository type, MIT license and author Dmitry Garavsky. The package includes README,
LICENSE, XML documentation and separate upstream SPDX data/notices. Relative README links
are expanded to repository URLs during packing; see [Architecture](architecture.md) for
revision selection. Ensure the referenced revision and documentation are published before
distributing the package. The real package check verifies the generated README contents.
The package does not contain the repository guides or runnable example sources.
The implementation's MIT declaration does not replace the dataset's attribution.

Only the library is packable. Ordinary builds use 0.1.0-dev. The SDK version and
roll-forward policy are declared in global.json; changing either requires renewed
verification. CI installs the declared SDK.

## Local preparation

```powershell
./eng/verify.ps1
./eng/test-release.ps1
./eng/prepare-release.ps1 -Tag v0.1.0-preview.1
./eng/verify-release-assets.ps1 -Version 0.1.0-preview.1 -Directory ./artifacts/release/0.1.0-preview.1
```

Verification already includes the standalone release-script tests. Those tests use
synthetic packages and a mocked GitHub CLI: no network, credentials or publication.
Both verify.ps1 and prepare-release.ps1 accept an optional RestoreSource for a local feed.

Preparation validates the tag, runs the complete pipeline with that version, then copies
the exact tested nupkg into a new release directory. Version is applied to restore,
Debug/Release builds and packing; the isolated consumer checks the loaded assembly's
informational version against the nuspec. Directory.Build.props is not rewritten.
Preparation needs no Git tag, remote, credentials or network when a complete local feed
is supplied. It does not change Git state or publish.

The release directory contains exactly:

- Spdx.Expressions.&lt;version&gt;.nupkg
- SHA256SUMS, containing the lowercase SHA-256 and package filename

Existing output directories are rejected before verification. Preserve prepared files
and failed evidence; use a new version for changed published bytes. Asset verification
checks the complete file set, hash, nuspec identity/version, repository/license/author
metadata and required package contents. Relative Directory and OutputDirectory arguments
are resolved against the caller's PowerShell location, including after Set-Location.

## Version policy

Tags are v followed by three canonical numeric components, optionally a lowercase
SemVer prerelease suffix: v1.2.3 or v1.2.3-preview.1. Leading zeroes in numeric components
or numeric prerelease identifiers, uppercase suffixes and build metadata are rejected.
The tag controls both package and assembly informational versions. A prerelease tag
creates a GitHub prerelease; a stable tag creates a stable release.

## GitHub workflow

The separate [release workflow](../.github/workflows/release.yml) runs only on pushed
v* tags. Main/PR/manual [build CI](ci.md) never publishes.

```text
Version tag ──→ validate version ──→ Windows / Linux / macOS verification
                                                │
                                                ▼
                              retain verified Linux package + SHA256SUMS
                                                │
                                                ▼
                                     NuGet.org trusted publishing
                                                │
                                                ▼
                                    GitHub release with the same files
```

All three verification jobs must succeed before publication. Each runs local preparation
and an actual isolated package consumer. Only the retained Linux artifact is published;
publication jobs download and validate it without rebuilding. Verification logs are
retained even on failure; release artifacts are retained for 30 days.

Verification has contents: read. NuGet publication uses the nuget environment and
id-token: write for [NuGet trusted publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing).
Only the GitHub release job has contents: write. Checkout does not persist credentials.
Every SDK job has the five isolation guards described in [Testing](testing.md).

## Setup before the first publication

1. Confirm control of the Spdx.Expressions package ID on NuGet.org. Repository ownership
   alone does not establish NuGet package-name availability or ownership.
2. Create the GitHub environment named nuget. Restrict its deployment tags to v* and
   configure any required reviewers according to repository policy.
3. Set environment variable NUGET_USER to the NuGet account username that owns the
   trusted publishing policy. This is a GitHub Actions variable, not an API-key secret.
4. On that NuGet account, configure a trusted publishing policy for GitHub owner
   DmitryGaravsky, repository Spdx.Expressions, workflow release.yml and environment nuget.
5. Review hosted Windows/Linux/macOS results and deliberately authorize the release.
   Pushing a valid version tag starts publication, including prereleases.

These settings and package ownership are external prerequisites; local checks do not
configure or prove them. No long-lived NuGet API key is required by this workflow.
Do not run publish-github-release.ps1 during local preparation: it is the publishing
entry point, reserved for the tag workflow or an explicitly authorized publication.

## Failure and retry

Use **Re-run failed jobs** while the original release artifact remains available.
Do not rerun successful verification jobs to replace it. Re-running all jobs encounters
the immutable artifact name; after artifact expiry, investigate and use a new reviewed
version rather than silently rebuilding the old release.

NuGet push uses --skip-duplicate to resume after a successful prior push. That flag does
not compare existing NuGet bytes; investigate a pre-existing version from another run.
Package publication is irreversible here; a later GitHub failure does not roll it back.

The GitHub publisher checks that the remote tag exists before changing a release, including
when resuming an existing draft. Both draft creation and final publication use --verify-tag;
a tag removed during uploading cannot be silently recreated by publication. See the
[GitHub CLI edit contract](https://cli.github.com/manual/gh_release_edit).
The publisher creates a draft, uploads the two files and publishes it. On retry it downloads and hashes existing assets before
uploading anything missing from a draft. It rejects differing or unexpected assets and
incomplete published releases without overwriting them. A matching published release is
left unchanged. Prereleases are not marked latest.

Hosted execution, authentication and publication remain unverified until actually run.
See [verification status](testing.md#verification-status) for completed local checks.
