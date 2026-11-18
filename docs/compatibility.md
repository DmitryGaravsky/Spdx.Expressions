# Compatibility policy

[Overview](../README.md) · [API](api.md) · [Syntax](syntax.md) · [Catalogue](catalogue.md) · [Release](release.md)

## The 1.0 baseline

Version 1.0.0 establishes the public contract for the expression library: parsing,
identifier validation, ordinary and canonical-casing formatting, immutable AST
inspection, and embedded or caller-selected catalogues. It targets net10.0 with no
runtime NuGet dependencies.

The supported contract consists of public signatures and XML/API documentation,
the fixed SPDX-2.3.1 syntax profile, documented defaults and limits, result semantics,
source coordinates, ordering, immutability, stream ownership and exception behavior.
Protected walker hooks are also part of the contract. English diagnostic text,
internal types, concrete collection implementations and undocumented serialization
layouts are not public contracts. Consume collections through their declared interfaces.

## Changes within 1.x

Versions follow [Semantic Versioning](https://semver.org/). Compatibility applies
to callers using the documented contract on a supported target framework.

| Change | Version policy |
| --- | --- |
| Fix behavior that violates the documented contract | Patch; describe observable corrections in release notes |
| Add an API or optional capability while preserving existing documented calls and behavior | Minor |
| Mark a public API obsolete | Minor, with a migration path; no removal within 1.x |
| Remove or rename a public member, change its type, tighten accepted arguments or change documented result semantics | Major |
| Change existing parameter names, optional defaults, nullability guarantees or required walker overrides incompatibly | Major |
| Remove net10.0 support or silently replace the fixed syntax profile/default resource limits | Major |
| Update the embedded catalogue | Minor, under the explicit data policy below |

Additions must preserve overload resolution for supported existing calls, including
named arguments and default literals. Walker extensions must preserve existing
overrides and traversal order; no new abstract implementation requirement is added
within 1.x. A future syntax profile needs an explicit opt-in API or a major release;
the current profile is not silently broadened into a different grammar.

Normal returns and documented failures remain distinct. Invalid input and limit
outcomes do not replace cancellation, I/O or packaging exceptions. Fixing a defect
may change the defective result, but a deliberate contract change is not classified
as a bug fix merely to avoid a major release. Release notes identify such corrections.

## Enums, diagnostics and result interpretation

Existing enum names, numeric values and meanings are preserved. Minor releases may
add values or diagnostic codes for new opt-in capabilities without reclassifying
documented outcomes of existing inputs under the same profile and catalogue.
Callers should handle unfamiliar values explicitly and never treat them as success.
New syntax-node kinds cannot alter the trees produced by the existing profile.

Existing diagnostic codes keep their documented meanings. Do not parse `Message`
or depend on its exact wording. Use result status, role, severity, code and structured
context. The three validation flags remain independent, and stored diagnostic
messages are not a complete list of occurrences when a message budget is reached.
UTF-16 expression spans and UTF-8 catalogue byte offsets remain distinct coordinates.

Results are immutable snapshots, not a versioned JSON schema. Applications that
persist them should define their own projection and schema rather than serialize
implementation details or rely on reflection into private state.

## Catalogue updates and reproducibility

The grammar profile, package version and catalogue identity are separate. Version
1.0.0 embeds SPDX License List 3.29.0. The embedded pair is fixed for a given package
version and never updates at runtime. Patch releases retain that pair and its hashes.

A minor release may adopt a reviewed newer official pair. Its release notes identify
the old/new list versions and hashes and summarize additions, removals, spelling and
deprecation changes. Consequently, `Default` lookup, validation and canonical casing
can produce different results after that package upgrade. This is the documented
data-update boundary, not a change to parsing or source evidence.

For reproducibility across package upgrades, retain the exact external pair and load
it explicitly; record its `Identity` with the results. A label alone is insufficient.
For the same input, options, syntax profile and catalogue bytes, documented behavior
is preserved within 1.x, except for corrections to defects described in release notes.
Hash equality identifies bytes; it does not establish authenticity or legal permission.

## Release checks

Review source compatibility, exported signatures, enum values, nullable contracts
and protected hooks against the previous release. Check observable behavior with
independent fixtures and actual nupkg consumers, including existing call patterns.
Run the [complete pipeline](testing.md#full-pipeline) for the selected version and
verify the release candidate on the configured platforms. New consumers, serializers
and framework targets require their own acceptance; they are not implicitly supported
by the presence of an additional public member.
