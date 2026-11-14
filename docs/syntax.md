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
  malformed recognized prefixes fail. Component spelling is retained.
- AND/OR require whitespace or parentheses at both boundaries. WITH requires
  whitespace on both sides and attaches only to a license or reference, never a group.
- One adjacent plus is allowed on a license candidate, never a reference or exception.
- Uppercase AND, OR, WITH, NONE and NOASSERTION are reserved whole tokens in ID positions.
  NONE/NOASSERTION are field values outside this API. Lowercase forms are ordinary candidates.
  Reference components such as `LicenseRef-NONE` are allowed.
- Full input consumption is required. Comments, declaration prefixes, quotes, underscores,
  NUL, Unicode lookalikes, repeated WITH and trailing prose are rejected.

For AST and UTF-16 span contracts, see [source evidence](api.md#source-evidence-and-ast).

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
values throw argument exceptions; exceeding a valid parser budget returns `LimitExceeded`.
A requested cancellation throws `OperationCanceledException` without a partial result in
all three operations. It is distinct from invalid input and budget exhaustion.
