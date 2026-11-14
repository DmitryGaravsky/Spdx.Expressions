# Embedded catalogue

[Overview](../README.md) · [Usage](usage.md) · [API](api.md) · [Syntax](syntax.md)

## Snapshot and lookup

`SpdxLicenseRegistry.Default` is a thread-safe immutable snapshot of **v3.29.0**,
not a claim about the newest list.
There are 740 license entries and 86 exception entries. Lookup uses OrdinalIgnoreCase;
enumeration uses ordinal canonical-ID ordering. Public entries contain only Identifier
and IsDeprecated. Six upstream deprecated license entries contain a literal trailing `+`;
lookup preserves them, while expression parsing treats plus as a suffix and validation
checks the base ID. Upstream exceptions also use the field `isDeprecatedLicenseId`.

Both exact JSON files come from the same
[upstream commit](https://github.com/spdx/license-list-data/tree/31ba1a50e5397e00a304dbadc76531740e89ee48).
`SpdxRegistryIdentity` exposes the list version and the lowercase SHA-256 of each file:

- licenses.json: `47d1cc681abe31166b342b6cc4aab13a6ba8ea5c48794697fe3bf8b1dbaf509a`
- exceptions.json: `59ee7c150de521d635cbf9367b8cf543b8da6781ea3ac1c073d305524f84ada2`

Source paths, byte lengths, tag/commit, import date and notice provenance are in the
shipped `data/manifest.json`. The exact JSON and upstream notices are embedded and
also included as package data. Corrupt resources cause InvalidOperationException,
not an empty catalogue or a user-input error.

No build or runtime update occurs. A manual development update imports both exact
files from one reviewed commit, updates the manifest and pinned hashes, reviews
notices, and reruns all entry/hash/package checks. There is no public JSON/stream loader,
external catalogue API or combined fingerprint.

`TryGetLicense` and `TryGetException` use separate namespaces. They do not trim whitespace
or apply aliases. A failed lookup returns false and a null entry. A null identifier throws
`ArgumentNullException`. Full license texts are not embedded; entries are identifier and
deprecation metadata, not license documents.

The catalogue's version and hashes are available from `Identity`; validation results retain
that identity as `RegistryIdentity`. Record it with results when reproducibility matters.
See [third-party notices](../THIRD-PARTY-NOTICES.md) for attribution.
