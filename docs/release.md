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

Only the library is packable. The source version is 1.0.0. The SDK version and
roll-forward policy are declared in global.json; changing either requires renewed
verification. CI installs the declared SDK.

## Local preparation

```powershell
./eng/verify.ps1
./eng/test-release.ps1
./eng/prepare-release.ps1 -Tag v1.0.0
./eng/verify-release-assets.ps1 -Version 1.0.0 -Directory ./artifacts/release/1.0.0
```

Verification includes offline release-script tests with synthetic packages and substituted
GitHub/HTTP commands: no network, credentials or publication. Both verify.ps1 and
prepare-release.ps1 accept an optional RestoreSource for a local feed.

Preparation validates the tag, runs the complete pipeline with that version, then copies
the exact tested nupkg into a new release directory. Version is applied to restore,
Debug/Release builds and packing; the isolated consumer checks the loaded assembly's
informational version against the nuspec. Its catalogue checks cover both loader
overloads and compare validation with caller-selected and embedded data. The package
ships the pinned embedded pair. Loader inputs for other snapshots are supplied by callers.
Preparation needs no Git tag, remote, credentials or network when a complete local feed
is supplied. It does not change Git state or publish.

The release directory contains exactly:

- Spdx.Expressions.&lt;version&gt;.nupkg
- SHA256SUMS, containing the lowercase SHA-256 and package filename

Existing output directories are rejected before verification. Preserve prepared files
and failed evidence; use a new version for changed published bytes. Asset verification
checks the complete file set, hash, nuspec identity/version, repository/license/author
metadata and required package contents. Publication additionally requires the nuspec
repository commit to equal the release event commit. Relative Directory and
OutputDirectory arguments follow the caller's PowerShell location.

## Version policy

The [1.x compatibility policy](compatibility.md) defines stable API contracts,
additive changes, diagnostic/enum evolution and embedded catalogue updates.
The 1.0.0 baseline includes the catalogue loader, traversal and canonical-casing APIs.
The release tag supplies the final version to all verification and packaging steps,
overriding the source version in Directory.Build.props. Local preparation never publishes.

Tags are v followed by three canonical numeric components, optionally a lowercase
SemVer prerelease suffix: v1.2.3 or v1.2.3-preview.1. Leading zeroes in numeric components
or numeric prerelease identifiers, uppercase suffixes and build metadata are rejected.
The tag controls both package and assembly informational versions. The GitHub release's
pre-release checkbox must match the tag: checked for a suffix such as -preview.1,
unchecked for a stable version. Classification mismatches stop publication.

## Publish from GitHub UI

1. Commit and push the reviewed changes to main and check the build CI results. The
   selected commit must contain this release workflow; existing old tags retain their
   old workflow. Do not move or reuse a published version tag.
2. Open **Releases → Draft a new release**. Create a new tag such as v1.0.0 targeting
   main, or select an existing version tag whose commit belongs to main.
3. Enter the release title and description. Select **This is a pre-release** only for
   a prerelease version. Leave the asset list empty; the workflow supplies both files.
4. Click **Publish release**. This creates the public GitHub release immediately and
   starts the package pipeline. Saving a draft, editing the release text or pushing
   a tag alone does not start publication.
5. Follow the **Release** run in Actions. If the nuget environment requires approval,
   approve it after verification. The workflow publishes to NuGet and attaches the
   verified nupkg and SHA256SUMS to the existing GitHub release, preserving its text
   and latest-release selection.
6. Confirm that all jobs passed. NuGet acceptance is followed by asynchronous validation
   and indexing: a successful push does not mean that installation is available yet.
   Check the version on NuGet.org after processing completes.

The trigger is [release: published](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#release),
which covers stable releases and prereleases, including releases published from drafts.
The public GitHub release can temporarily have no attachments while checks run or after
an unsuccessful run. Its existence alone is not evidence of a successful NuGet publication.

## GitHub workflow

```text
Publish GitHub release ──→ validate tag, event commit, main ancestry and release state
                                                │
                                                ▼
                               Windows / Linux / macOS verification
                                                │
                                                ▼
                              retain verified Linux package + SHA256SUMS
                                                │
                                                ▼
                         verify GitHub assets and existing NuGet version
                                                │
                                                ▼
                           NuGet.org trusted publishing, if not already identical
                                                │
                                                ▼
                         attach files to the existing release and verify downloads
```

All checkouts use the event's exact commit SHA. The explicit tag ref must still resolve
to that SHA, and the commit must be an ancestor of main or main itself. These checks run
before building, before publication and when attaching files. Repository tag protections
remain necessary: API checks cannot atomically lock a tag against concurrent changes.

All three verification jobs must succeed. Each runs local preparation and an actual
isolated package consumer. Only the retained Linux artifact is published; publication
jobs download and validate it without rebuilding. Logs are retained even on failure;
release artifacts remain available for 30 days.

Verification has contents: read. NuGet uses the nuget environment and id-token: write
for [trusted publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing).
Only the attachment job has contents: write. Checkout does not persist credentials.
Actions are pinned to full upstream commit SHAs; [Dependabot](../.github/dependabot.yml)
proposes weekly Actions and NuGet dependency updates. Review those PRs before merging.
Every SDK job has the five isolation guards described in [Testing](testing.md).

## Manual repository and account settings

These settings are external to the source tree; changing workflow files does not apply them.

| Location | Required or recommended configuration |
| --- | --- |
| Settings → Rules → Rulesets | Create an active tag ruleset for v* restricting updates and deletions, with no routine bypass. If additional writers are invited, restrict tag creation to release maintainers in a separate ruleset so its creation bypass does not permit rewriting existing tags. |
| Settings → Branches → main protection | Require Verify (ubuntu-latest) and Verify (windows-latest), and apply the protection to administrators. Keep force pushes/deletion disabled. macOS is not a required PR check because it runs on main/manual CI and releases. Independent review requires another maintainer; do not require an unavailable reviewer in a solo repository. |
| Settings → Actions → General | Keep default workflow permissions read-only and PR approval by Actions disabled. Require full SHA pinning; allow the used actions/* and NuGet/login actions if limiting the allowed set. Keep approval for all external contributors' PR runs. |
| Settings → Environments → nuget | Keep a Tag rule v*, not a Branch rule, and NUGET_USER as an environment variable containing the NuGet profile name. A required reviewer is optional protection against accidental publication. For a solo reviewer, do not enable Prevent self-review; it would block their own releases. Disable administrator bypass if approval is intended to be mandatory. |
| Settings → Security features | Keep secret scanning and push protection enabled. Enable Dependabot alerts and security updates; version updates are configured in dependabot.yml. |
| Settings → Releases | Keep Immutable releases disabled for this UI-first workflow. GitHub cannot attach files after an immutable release is published. An immutable-release process must upload files before publishing the release and needs a different trigger. |
| NuGet.org → Trusted Publishing | Restrict the policy to owner DmitryGaravsky, repository Spdx.Expressions, workflow release.yml, environment nuget and package pattern Spdx.Expressions. Retain only the publishing scopes needed for this package; no stored API key is required. |
| GitHub and NuGet account security | Confirm appropriate sign-in protection and review any existing long-lived keys or integrations separately. Workflow success does not establish their absence or scope. |

Do not run publish-github-release.ps1 locally to publish implicitly. Its -CheckOnly mode
is read-only but requires GitHub access. The publication entry point is reserved for the
workflow or a separately authorized publication task.

## Failure and retry

Use **Re-run failed jobs** while the original release artifact remains available.
Do not rerun successful verification jobs to replace it. Re-running all jobs encounters
the retained artifact name; after artifact expiry, investigate and use a new reviewed
version rather than silently rebuilding the old release.

Before obtaining a NuGet key, the workflow checks the existing package URL. Only HTTP 404
means absent; authorization, network and service failures stop the job. For an existing
version it compares every ZIP entry name, length and SHA-256 against the retained artifact,
rejecting missing, extra, duplicate or case-colliding entries. Only the root .signature.p7s
is excluded because NuGet.org adds a repository signature. This compares package payloads;
it is not a cryptographic signature-validation command.

An identical existing version skips authentication and push. Different content fails
without overwrite. New pushes do not use --skip-duplicate: if a version appears between
the check and push, or NuGet accepted it but has not exposed it yet, the push fails safely.
Wait for NuGet processing and rerun failed jobs so the existing payload can be verified.

Before NuGet publication, the GitHub attachment check rejects conflicting/unexpected files,
a missing release, wrong classification or an immutable release missing assets. Missing
files in a mutable published release are allowed. The attachment job uploads only those
files, never overwrites existing assets, and downloads the result to verify their hashes.
Partial uploads can resume. Title, description, classification and latest selection are
never edited by the workflow; release creation and deletion are not automated.

NuGet publication is not rolled back by a later GitHub failure. A complete matching
immutable release permits a read-only retry, but missing files cannot be added to it.

See [release verification](testing.md#release-verification) for the accepted checks.
Publication uses the results and retained artifacts of the exact tagged release.
