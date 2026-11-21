# Syntax reference

[Overview](../README.md) · [Usage](usage.md) · [API](api.md) · [Catalogue](catalogue.md)

## Syntax profile

The fixed `SyntaxProfile` is `SPDX-2.3.1`, based on
[Annex D](https://spdx.github.io/spdx-spec/v2.3.1/SPDX-license-expressions/).
The profile accepts uppercase AND, OR and WITH, adjacent plus, parentheses,
LicenseRef-X and DocumentRef-D:LicenseRef-X. Precedence is plus, WITH, AND, OR.
AND/OR chains are flattened only across uninterrupted occurrences of the same
operator; explicit groups and operand order remain intact.

Policies are fixed, with no permissive mode:

- Whitespace is ASCII space, tab, CR and LF. Other Unicode whitespace is rejected.
- ID candidates and reference components contain one or more ASCII letters, digits,
  hyphens or dots. Unknown candidates can parse successfully.
- Reference prefixes require exact casing. `licenseref-X` is an ordinary ID candidate;
  malformed recognized prefixes fail. Component spelling is retained. References are
  contiguous tokens: whitespace inside a reference, including around its colon, is rejected.
- AND/OR require whitespace or parentheses at both boundaries. WITH requires
  whitespace on both sides and attaches only to a license or reference, never a group.
- One adjacent plus is allowed on a license candidate, never a reference or exception.
- Uppercase AND, OR, WITH, NONE and NOASSERTION are reserved whole tokens in ID positions.
  The [license field reader](api.md#license-field-values) recognizes NONE/NOASSERTION as complete field values.
  The expression parser rejects them as operands. Lowercase forms are ordinary candidates.
  Reference components such as `LicenseRef-NONE` are allowed.
- Full input consumption is required. Comments, declaration prefixes, quotes, underscores,
  NUL, Unicode lookalikes, repeated WITH and trailing prose are rejected.

For AST and UTF-16 span contracts, see [source evidence](api.md#source-evidence-and-ast).
The [conformance coverage map](testing.md#specification-conformance) distinguishes Annex D
requirements from these fixed profile policies. The single-line restriction in Annex D.1
applies to tag:value documents; this expression API accepts the whitespace listed above.

## Diagnostic codes

Stable diagnostic codes (English message wording is not a machine contract):

| Code | Condition |
| --- | --- |
| SPDX_PARSE_EMPTY_INPUT | Empty or whitespace-only input |
| SPDX_PARSE_INVALID_CHARACTER | Character outside the profile |
| SPDX_PARSE_EXPECTED_OPERAND | Missing expression operand |
| SPDX_PARSE_EXPECTED_OPERATOR | Missing/incorrectly separated operator |
| SPDX_PARSE_UNBALANCED_PARENTHESIS | Unmatched parenthesis |
| SPDX_PARSE_INVALID_REFERENCE | Malformed recognized reference |
| SPDX_PARSE_INVALID_PLUS | Invalid suffix use |
| SPDX_PARSE_INVALID_WITH | Invalid attachment, spacing or exception operand |
| SPDX_PARSE_FIELD_SENTINEL | NONE/NOASSERTION in an ID position |
| SPDX_PARSE_LIMIT_EXCEEDED | Inclusive parser budget exceeded |
| SPDX_ID_UNKNOWN_LICENSE | Unknown license candidate |
| SPDX_ID_UNKNOWN_EXCEPTION | Unknown exception candidate |
| SPDX_ID_WRONG_KIND | ID belongs only to the opposite namespace |
| SPDX_ID_DEPRECATED | Known deprecated ID |

Parser diagnostics use original UTF-16 spans; EOF can have length zero.
When scanning reaches an invalid supplementary Unicode character, its diagnostic covers the
first UTF-16 code unit with length one. An earlier structural failure takes precedence:
`LicenseRef-` followed by an invalid character has an empty component, so INVALID_REFERENCE
covers the prefix before that character. Parsing stops with one diagnostic and no partial AST
or later evidence.
The scanner classifies the next token before the parser checks its grammatical role.
Invalid characters or token budgets discovered during that lookahead can therefore
take precedence over a missing-operator error. Specific arbitration cases are frozen
in the grammar fixtures. No recovery or suppression-marker diagnostics are emitted.

## Limits and cancellation

All budgets are inclusive positive `int` values. Parenthesis depth additionally cannot
exceed 256. Integer values outside the `int` range cannot be supplied; counters and
span arithmetic avoid unchecked growth beyond accepted input ranges.

| Option | Default | Counted units |
| --- | ---: | --- |
| MaxInputLength | 65,536 | UTF-16 units including whitespace, checked before scanning |
| MaxIdentifierLength | 256 | Each ID/reference component, excluding prefixes and plus |
| MaxTokens | 16,384 | ID/reference, operator, plus or parenthesis; no whitespace/EOF |
| MaxNodes | 8,192 | Every AST node, including groups and flattened operator nodes |
| MaxParenthesisDepth | 64 | Concurrent open parentheses |
| MaxDiagnostics (validation) | 128 | Stored messages only; minimum one |

Parser recursion is bounded by parenthesis depth, not chain length. Traversals use an
explicit stack and cancellation checks. Long whitespace and candidate scans also poll
cancellation. Configured maxima do not cause eager maximum-sized allocations.
These budgets are engineering controls, not process isolation, a wall-clock guarantee
or a completed security audit. Host-level resource controls remain the caller's concern.

Pass `SpdxParseOptions` to Parse and `SpdxValidationOptions` to Validate. Invalid option
values throw argument exceptions; exceeding a valid parser budget returns `LimitExceeded`. Both formatters can add spaces
and require a larger input-length budget on reparse. Applying catalogue casing leaves the
length of each identifier unchanged.
A requested cancellation throws `OperationCanceledException` without a partial result in
Parse, Validate, Format and FormatCanonicalCasing. It is distinct from invalid input and budget exhaustion.

## Catalogue load diagnostics

Catalogue loading uses `SpdxRegistryDiagnostic`. Use `Code` for decisions and
`Message` for display. `Input` identifies Licenses or Exceptions. `ByteOffset`
is a zero-based point in the supplied memory slice or stream suffix, counting
any BOM and allowing EOF. Expression diagnostics instead use UTF-16 spans in
the expression string.

`EntryIndex` is the zero-based source index in the root collection, when
applicable. `PropertyName` can identify a decoded name, a required name, or the
property providing the value context, including through nested arrays. It can
be null when a name is unavailable or does not apply. UTF-8 and JSON lexical
errors, root type and trailing data errors, and acquisition byte limits have
neither entry nor property context. `LimitName` is set only for a limit failure
and gives the exact option property name.

| Code | Location and meaning |
| --- | --- |
| `SPDX_REGISTRY_INVALID_UTF8` | Start of the first malformed or truncated UTF-8 sequence |
| `SPDX_REGISTRY_INVALID_UNICODE` | Backslash starting an unpaired surrogate escape |
| `SPDX_REGISTRY_INVALID_JSON` | Offending byte or EOF in malformed JSON |
| `SPDX_REGISTRY_TRAILING_DATA` | First non-whitespace byte after the root object |
| `SPDX_REGISTRY_ROOT_TYPE` | Start of a root token other than an object |
| `SPDX_REGISTRY_MISSING_PROPERTY` | Closing brace of an object with a missing required property |
| `SPDX_REGISTRY_PROPERTY_TYPE` | Start of a required property's wrong-type value |
| `SPDX_REGISTRY_DUPLICATE_PROPERTY` | Opening quote of the repeated decoded property name |
| `SPDX_REGISTRY_UNEXPECTED_PROPERTY` | Opening quote of an opposite collection or ID property |
| `SPDX_REGISTRY_INVALID_VERSION` | Opening quote of an invalid version label |
| `SPDX_REGISTRY_VERSION_MISMATCH` | Opening quote of the exception input's differing version value |
| `SPDX_REGISTRY_ENTRY_TYPE` | Start of a non-object element of the required array |
| `SPDX_REGISTRY_EMPTY_COLLECTION` | Closing bracket of the required empty array |
| `SPDX_REGISTRY_INVALID_IDENTIFIER` | Opening quote of an invalid ID, including a plus ID without deprecation |
| `SPDX_REGISTRY_DUPLICATE_IDENTIFIER` | Opening quote of the repeated ID in the same namespace |
| `SPDX_REGISTRY_NAMESPACE_COLLISION` | Opening quote of the first colliding ID in exception source order |
| `SPDX_REGISTRY_LIMIT_EXCEEDED` | First byte outside the accepted byte budget or start of the token that exceeds a JSON or schema budget |

The first 16 codes return `InvalidData`. The last returns `LimitExceeded`.
Each failure reports one primary diagnostic according to the
[load order](api.md#load-results-and-call-order).

## Catalogue load limits

All `SpdxRegistryLoadOptions` limits are inclusive. Null options use these defaults.
Limits are independent, and each must be a positive integer. `MaxJsonDepth`
also has an upper bound of 64. The table gives the order used to validate options.

| Option | Default | Unit and scope |
| --- | ---: | --- |
| `MaxInputBytes` | 16,777,216 | Raw bytes per document, including BOM and whitespace |
| `MaxTotalInputBytes` | 33,554,432 | Accepted raw bytes across both documents, including BOMs and whitespace |
| `MaxJsonDepth` | 16 | Simultaneously open containers, with the root at depth 1 |
| `MaxLicenses` | 10,000 | Direct elements of the root licenses array |
| `MaxExceptions` | 2,000 | Direct elements of the root exceptions array |
| `MaxIdentifierLength` | 256 | Decoded UTF-16 units per ID, including a legacy plus |
| `MaxStringLength` | 16,384 | Decoded UTF-16 units per property name or string value |
| `MaxTotalStringLength` | 8,388,608 | Decoded UTF-16 units of all property names and string values across both documents, counting each occurrence |
| `MaxObjectProperties` | 64 | Property occurrences per object |
| `MaxArrayLength` | 10,000 | Direct elements per array, including both catalogue arrays |
| `MaxTokens` | 250,000 | Combined JSON tokens across both documents, counting names, values and opening/closing delimiters |
| `MaxTokenBytes` | 131,072 | Raw bytes per recognized token, including quotes and escapes, excluding separators and intervening whitespace |

The effective byte allowance for each input is the smaller of its per-input limit
and the remaining pair allowance. Equal byte-limit failures report `MaxInputBytes`.
JSON and string budgets apply to ignored metadata and count prospective elements
and properties. A supplementary Unicode character counts as two decoded UTF-16
units. Acquisition budgets bound scans of incomplete tokens; `MaxTokenBytes`
applies once a token is recognized. These options limit input and work without
defining an exact heap or elapsed-time bound.
