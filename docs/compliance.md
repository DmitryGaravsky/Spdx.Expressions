# Component license policy

`Spdx.Compliance` is a .NET 10 library for evaluating SPDX license fields and CycloneDX 1.6
JSON against an explicit distribution policy. It uses `Spdx.Expressions` for syntax,
catalogue lookup and reference resolution, and `NuGet.Versioning` for correction
selectors. All operations work offline. Applications acquire policies, definitions,
inventories, correction catalogs and SBOMs at their own I/O boundary.

Install the [NuGet package](https://www.nuget.org/packages/Spdx.Compliance/) in your .NET 10 project:

```shell
dotnet add package Spdx.Compliance --version 0.1.0
```

## Operations

| Operation | Result |
| --- | --- |
| `CompliancePolicy.Load(policyStream, correctionCatalog, limits, cancellationToken)` | Immutable, schema-validated policy. Its identity includes the policy identifier, schema version and SHA-256 of the original bytes, plus the catalog digest when configured. |
| `ComplianceEngine.Evaluate(expression, policy, context, cancellationToken)` | Original text, profile, policy identity, completion flag, and a `ComplianceFinding` containing the decision, stable reason, original UTF-16 spans and available evidence. |
| `CycloneDxComplianceAnalyzer.Analyze(document, policy, context, limits, contextForComponent, cancellationToken)` | Immutable per-occurrence findings with JSON Pointer, role, component name/version/type, bom-ref, PURL, raw declaration and observations, evaluation and optional correction check. |

Stream operations start at their current position, accept non-seekable streams and leave
them open. JSON inputs are UTF-8 with an optional BOM. The BOM is included in the hash.
An unsupported CycloneDX version, malformed JSON or duplicate JSON property produces an
incomplete report with a typed issue. Invalid policy or catalog configuration throws
`CompliancePolicyException` with an error category. Stream I/O failures and cancellation
propagate. Synchronous blocking reads are cancellable between read calls.

The default limits are 16 MiB per stream, 64 JSON levels and 10,000 component occurrences.
Structural diagnostics have the same count ceiling plus one truncation marker. The
policy can set an expression limit up to 65,536 UTF-16 code units, alongside the native
parser's token, node and nesting limits. A limit prevents a complete evaluation.
An `IsComplete` report indicates that the supported projection was evaluated. It can
contain `Invalid`, `Deny`, `Review`, `Unknown` and `Conditional` findings and does not
constitute a release approval.

CycloneDX projection visits the metadata subject, top-level components and metadata tool
components in that order, descending through each component's `components` array.
A descendant of a tool also has the `Tool` role. Identical package coordinates and even
repeated bom-refs remain independent occurrences. Product counts include the metadata
subject. Legacy tool records and tool services are outside the component projection.
The adapter evaluates component licenses and performs no full CycloneDX schema
validation. `JsonElement.ValueKind` distinguishes an absent field (`Undefined`) from
a literal JSON `null`. Both raw license values remain available after the input stream
closes. A per-occurrence callback can replace the shared context, for example to supply
a choice keyed by the JSON Pointer. Pass `OrSelections` through that callback for the
specific occurrence. The analyzer rejects an OR selection set on the shared context.

## Policy JSON version 1

`schemaVersion` must be `1` and `id` must identify the policy. All other top-level
properties are optional. Property names are case-sensitive. Unknown properties and
duplicate keys are rejected. SPDX rule identifiers are canonicalized against the
embedded SPDX catalogue. Profile names, names of licenses and owners, correction
labels and file paths match ordinally. Conflicting decisions and duplicate rules
for the same canonical term or exact pair are rejected.

| Property | Value |
| --- | --- |
| `allow`, `deny` | Arrays of known simple SPDX IDs. An adjacent `+` is part of an exact rule. |
| `allowExactPairs`, `denyExactPairs` | Arrays of SPDX `license WITH exception` expressions containing known IDs. |
| `profiles` | Object mapping profile names to arrays of prohibited license IDs or plus terms. `default` exists with an empty ban list unless explicitly configured. |
| `notice` | One obligation for a nondeprecated license with no simple allow/deny rule. Supply `license`, slash-separated relative `file` and expected `sha256`. |
| `referenceApprovals` | Array of objects containing `document`, a local `id` including `LicenseRef-`, and `sha256` of the original UTF-8 definition. Multiple distinct reviewed digests for one document and identifier are permitted. |
| `namedLicenses` | Array of alias groups. Each has `names` and optionally `owner` or a simple SPDX `license`. A group with only names routes to ownership review. A mapped SPDX term goes through the same policy and presence checks as an expression. |
| `maxExpressionCharacters` | Positive integer expression length ceiling. The default and maximum are 65,536. |
| `correctionProperties` | Object with distinct `id` and `source` marker property names in the component's `properties` array. |
| `correctionCatalog` | Object with expected `sha256` and ordinal `source` label. Requires `correctionProperties` and a caller-opened catalog stream. |
| `pendingCorrections` | Optional legacy routing entries with `package` (versioned NuGet PURL), `license` and correction `id`. Used only without a pinned catalog. A matching, otherwise unlisted license with one ID and one nonempty source becomes `Conditional / CorrectionUnconfirmed`. |

Reference evidence uses exact definition bytes and a `ComplianceReferenceEvidence`
index with available document identities and native `SpdxDocumentBinding` values.
A matching definition digest alone is insufficient. The approval must name the
resolved target document and local LicenseRef ID. Successful native lookup is visible
whether or not the definition has an approval.

`NoticeDelivery` snapshots a trusted file-to-SHA-256 inventory for one delivery.
Completeness is a caller assertion about which paths are known to be absent.
`PackageOwnership` snapshots an independently confirmed, exact versioned NuGet PURL
to owner mapping. The component name and version must agree with its PURL before
ownership evidence applies. A name or prefix in a component never establishes ownership.

## Decision and evidence rules

The [stable decision contract](compliance-decisions.md) defines the operator tables, whole-input outcomes and when a branch condition becomes a current action.

| Input or rule | Decision and reason |
| --- | --- |
| Missing field / empty CycloneDX array | `Unknown / FieldMissing` or `Unknown / NoLicenseEntries` |
| Empty string / whitespace-only expression | `Invalid / FieldEmpty` or `Invalid / InvalidSyntax` |
| `NONE` / `NOASSERTION` in an expression slot | `Review / FieldNone` or `Unknown / FieldNoassertion` |
| Invalid expression syntax / expression limit | `Invalid / InvalidSyntax` or incomplete `Unknown / LimitExceeded`, with native diagnostics where available |
| Unsupported declaration shape / multiple entries | `Invalid / WrongField` or `Review / MultipleEntries`. A CycloneDX `license.id` requires a known SPDX ID; custom references, operators and sentinels belong in the expression slot. |
| Named or file-only terms | `Review / NamedTerms` or `Review / FileOnly` without an applicable alias. Ownership confirmation or absence produces `OwnershipConfirmed` or `OwnershipUnconfirmed`. |
| Unknown / deprecated / unlisted SPDX ID | `Review / UnknownId`, `DeprecatedId` or `NoPolicyRule`. Explicit denial can apply to a deprecated ID. |
| Or-later suffix | Match only an exact `+` rule. Otherwise a known ID gives `Review / OrLaterUnlisted`, with deprecation recorded separately. |
| `WITH` | Use the complete pair. An unlisted pair requires review, even when the simple license has a rule. |
| Reference | `Review` when unresolved or resolved without a matching approval, `Allow / ReferenceApproved` for a matching approval. |
| Notice | `Allow / NoticeVerified` for the expected digest, `Deny / NoticeMismatch` for a different digest, `Deny / NoticeMissing` for absence from a complete inventory, and `Conditional / NoticeUnverified` for unverified absence. |
| Unknown profile | `Review / ProfileUnconfigured`. |
| Conflicting observed declaration | Record the first conflicting license text and retain the original evaluation. A conflict normally gives `Review / SourceConflict`, with existing denials and invalid outcomes retained. File-only references remain available in the raw observations. |

An AND evaluates every operand. Among reachable operands, denial takes precedence,
followed by review, an open condition, and then all operands allowed. An OR with
an eligible alternative requires an explicit choice and returns `Conditional` if
none was supplied. If every alternative is denied, the OR is denied. Other ORs with
no eligible alternative require review. A selected OR uses the selected child's
decision. Allow and Deny receive the reasons `ChoiceAllowed` and `ChoiceDenied`.
The tree retains every branch, group, source span and its evidence, including obligations
on inactive branches. Only the selected route contributes obligations. A presence ban
examines the entire declaration, including WITH operands and unselected branches.
A bare presence-ban ID also covers that ID with `+`. A detected ban takes precedence
over a selection result and retains it as a child finding.

### Recording OR choices

Enumerate the first evaluation's findings with `Kind == SpdxExpressionKind.Or`. For
each OR, its `Span` identifies the node and its `Children` contains every direct
alternative in original source order. Create an `OrSelection` from that UTF-16 span
and a zero-based index in `Children`. Indices can select denied alternatives and
distinguish repeated text. `Choices` is a display list of eligible text for an open
OR. `SelectedIndex` and `Selection` on a selected OR identify the applied operand.

Pass `new OrSelectionSet(originalDeclaration, selections)` as
`ComplianceContext.OrSelections` to re-evaluate. The source string must match
ordinally, including casing, whitespace and parentheses. Changing source text
requires a new binding. Policy and evidence can change while the binding stays valid.

AND activates every operand. A selected OR activates only its chosen operand. An open
OR keeps all alternatives visible and permits no selections under it. Choices under
independent AND operands can be recorded together or left open. A nested OR can
be selected in the same batch as its ancestor. The evaluator validates top-down
regardless of the order of the entries. A batch with no entries and matching text
leaves every OR open. A set accepts at most 8,192 entries.

Duplicate spans, non-OR spans, out-of-range indices, changed source text, mixed
selection modes and selections under open or unselected ancestors yield
`Invalid / SelectionMismatch`. A rejected batch applies none of its entries.
`SelectedBranch` on its own retains its legacy meaning. It accepts an exact,
case-sensitive source slice that identifies one direct alternative of the outer
OR, optionally enclosed by whole-expression parentheses. A repeated matching
slice or an attempt to select an inner alternative yields `SelectionMismatch`.
With an unconfigured profile, a single selection mode receives `ProfileUnconfigured`
and mixed modes still receive `SelectionMismatch`.

### Inspecting actions and branch conditions

Call `evaluation.GetActionSummary()` for a license field or
`component.Evaluation.GetActionSummary()` for a CycloneDX occurrence. This explicit
operation projects the existing finding tree into two immutable lists without
acquiring evidence or changing evaluation serialization:

- `CurrentActions` contains steps on the active route. `RecordChoice` identifies an
  open OR and provides its eligible `Alternatives` with their full child indices.
  `VerifyNotice` carries the delivery, path and digest in `Finding.Notice`.
  `ConfirmCorrection` carries the legacy pending ID in `Finding.PendingCorrectionId`.
- `BranchConditions` pairs a step with `RequiredChoices`, an outer-to-inner chain of
  missing `OrSelection` span/index pairs. Apply those choices alongside already
  recorded choices for that same declaration. Independent ORs have separate
  occurrences. The summary keeps source order and skips Cartesian combinations.

For `OFL-1.1 OR MIT`, an open evaluation has a current `RecordChoice` and a
contingent `VerifyNotice` under index zero. Selecting OFL makes the NOTICE current.
With matching delivery evidence, no action remains. Selecting MIT also yields an
empty summary, while `evaluation.Finding.Children[0].Notice` exposes OFL's inactive
evidence. A CycloneDX caller supplies choices by occurrence and reads actions beside
the occurrence's JSON Pointer.

An AND can retain a mandatory action alongside a denied or review-only sibling.
Other decisions and their evidence stay on the evaluation. Incomplete evaluations,
rejected batches, global presence bans and unresolved source conflicts suppress
action guidance. A correction mismatch wrapper retains its earlier findings as
evidence. An empty summary means the projection has no known step for this evaluation.
Inspect `Finding.Decision` and `IsComplete` for the disposition, including
`Review`, `Deny`, `Invalid` and `Unknown`.

### Correction catalogs

The catalog stream contains an array of entries identified by unique `id` and
`artifactKind`. A `nuget` entry has one `packageId` and `versionRange` or an array
of those pairs under `packageSelectors`. Its `license` has exactly one `id` or
`name`, and an absolute `url`. Extra catalog metadata can accompany these fields.
The loader verifies the pin before parsing catalog rules. NuGet version comparisons
honor prerelease precedence and open or exclusive endpoints. Exact pins win over
ranges, and catalog order breaks remaining ties. Floating selectors are outside this
schema. Entries for other artifact kinds cannot verify a NuGet claim.

The presence of either configured marker triggers a check. Verification requires exactly
one ID and one expected source, a matching versioned NuGet PURL, the winning selector,
and one concluded license with the exact expected ID or name and URL. A mismatch changes
the decision to `Review / CorrectionMismatch` unless a denial or invalid outcome
already applies. Without a catalog, marked occurrences carry `Unverified` correction
evidence and follow ordinary evaluation or an explicit pending route. Unmarked
occurrences follow ordinary evaluation.

`Verified` establishes that the claim matches the pinned evidence. A component with a
verified correction can still require review when the corrected license has no
distribution rule.

## Run the example

From the repository root with the SDK selected by `global.json`:

```powershell
dotnet run --project Spdx.Compliance.Examples -c Release
```

The [example source](../Spdx.Compliance.Examples/Program.cs) loads a synthetic policy and
catalog, evaluates a legacy outer OR choice and a batch of independent and nested
choices, evaluates an exact WITH pair and an unresolved reference, and analyzes a
component with a verified correction. It follows a NOTICE condition from an open
choice to a verified delivery, shows inactive evidence, and projects separate actions
for two CycloneDX occurrences with the same declaration.

```text
OR without a choice = Conditional
OR with MIT selected = Allow
Independent and nested OR choices = Allow
Exact WITH pair = Allow
Unresolved reference = ReferenceUnresolved
Correction = Verified, distribution = Review
Open NOTICE route = RecordChoice, contingent VerifyNotice
OFL selected = VerifyNotice
NOTICE verified = Allow, 0 current actions
MIT selected = 0 current actions, inactive NoticeUnverified
SBOM actions by pointer = VerifyNotice, RecordChoice
```

The release check installs the packed library in an isolated consumer and runs this
example with its dependencies from NuGet.org. See [Packaging and releases](release.md)
for local preparation and offline dependency sources.
