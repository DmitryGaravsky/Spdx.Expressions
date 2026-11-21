# Catalogues

[Overview](../README.md) · [Usage](usage.md) · [API](api.md) · [Syntax](syntax.md)

## Snapshot and lookup

`SpdxLicenseRegistry.Default` is a thread-safe immutable snapshot of **v3.29.0**,
not a claim about the newest list.
There are 740 license entries and 86 exception entries. Lookup uses OrdinalIgnoreCase;
enumeration uses ordinal canonical-ID ordering. Both entry types expose `Identifier`
and `IsDeprecated`. Six upstream deprecated license entries contain a literal trailing `+`;
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

Updating the embedded snapshot is a manual development task. Import both exact
files from one reviewed commit, update the manifest and pinned hashes, review
notices, and rerun all entry/hash/package checks.
Within 1.x, embedded catalogue updates require a minor release and an explicit
change summary; patch releases retain the same bytes. Use a caller-selected pair
when results must stay tied to one dataset across upgrades. See the
[compatibility policy](compatibility.md#catalogue-updates-and-reproducibility).

`TryGetLicense` and `TryGetException` use separate namespaces. They do not trim whitespace
or apply aliases. A failed lookup returns false and a null entry. A null identifier throws
`ArgumentNullException`.

See [third-party notices](../THIRD-PARTY-NOTICES.md) for embedded data attribution.

## Names and links

Look up an ID with `TryGetLicense` or `TryGetException` to read the following properties on
`SpdxLicenseInfo` or `SpdxExceptionInfo`. For example, the embedded entry for `MIT` has
`Name` equal to `MIT License` and `Reference` equal to `https://spdx.org/licenses/MIT.html`.

| Property | Catalogue field | Result |
| --- | --- | --- |
| `Name` | `name` | Display name, or null for an absent field or a non-string value. |
| `Reference` | `reference` | Link to the catalogue record, or null for an absent field or a non-string value. In the embedded list it points to the SPDX record page. |
| `SeeAlso` | `seeAlso` | Read-only list of additional link strings in source order, including duplicates. An absent field, a non-array value or any non-string element yields an empty list. |

The loader keeps decoded strings verbatim, including empty strings and whitespace. It
performs no URI validation or fetching. These optional fields follow the same projection
for embedded and caller-selected data. Entries with missing or ill-typed display metadata
still participate in lookup and validation. JSON, duplicate-key and resource checks apply
to every value, including omitted display data.

## Load a caller-selected pair

Use a selected pair to pin the catalogue independently of the parser or to compare
validation results across catalogue versions using the same parsed expression.
Record `RegistryIdentity` with each result to distinguish the exact datasets used.
Loading is offline: the application supplies the bytes or streams and controls
where they come from.

Pass the licenses and exceptions documents, in that order, to
`SpdxLicenseRegistry.Load`. Both inputs must be complete UTF-8 JSON objects. One
leading UTF-8 BOM per document and trailing JSON whitespace are allowed. The loader
rejects malformed UTF-8, unpaired escaped surrogates, comments, trailing commas
and additional root data.

Each root requires a `licenseListVersion` string and its corresponding `licenses`
or `exceptions` non-empty array of objects. A license entry requires a string
`licenseId` and an exception entry requires a string `licenseExceptionId`. Both
require a JSON boolean `isDeprecatedLicenseId`. Keys are case-sensitive after JSON
unescaping, and property order is unrestricted. Opposite collection and ID keys
are rejected in root and entry objects respectively. Duplicate decoded keys are
rejected in every object. Other fields can contain any well-formed JSON value
within the [loader limits](syntax.md#catalogue-load-limits).

The version is a label with at least one character, no surrounding Unicode whitespace
and no control characters. Both labels must match ordinally. IDs use ASCII letters,
digits, hyphen and period. A license marked deprecated may also have one final `+`.
The base spelling excludes `AND`, `OR`, `WITH`, `NONE`, `NOASSERTION` and the prefixes
`LicenseRef-` and `DocumentRef-`, ignoring case. IDs must be unique under ordinal
case-insensitive comparison within and across namespaces.

Success confirms this structural subset of the SPDX list schema. The loader accepts
non-empty subsets and synthetic IDs that meet the rules. Authenticity and official
list completeness require separate verification. A successful load has the same
canonical spelling, ordinal enumeration and case-insensitive lookup semantics as
`Default`. A stored `Old+` is a literal key, distinct from `Old`. Validating the
expression `Old+` looks up the base `Old`.

## Ownership and exact-byte identity

Memory inputs can be slices. Keep their bytes stable during the call. The loader
copies them into owned storage. Supply two distinct readable streams and keep their
contents and read positions stable for the duration of the call. The loader supports
non-seekable and short-read streams, reads from each current position through EOF
within its effective byte allowance, and always leaves both streams open. It never
seeks or rewinds. At the boundary, one additional byte is requested to distinguish
EOF from excess data. A byte-limit failure may consume that byte and leaves any
remaining bytes unread. A failed licenses document prevents reading the exceptions
document. See the [call order](api.md#load-results-and-call-order) for argument
validation, I/O and cancellation.

`Identity` contains the shared declared label and lowercase SHA-256 of each accepted
input. The hashes cover every supplied byte, including any BOM, ignored metadata and
whitespace. For streams they cover the bytes consumed from the starting position
through EOF. Store the exact pair or its hashes with the validation result's
`RegistryIdentity` to identify the dataset used. Each loaded registry owns its
immutable entries and identity, retaining neither input buffers nor streams nor raw
JSON. It is independent of `Default` and of other loads.
