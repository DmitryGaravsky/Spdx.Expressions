# Stable decision semantics

Contract revision 1 applies to Spdx.Compliance 0.1.0. The unit of evaluation is one license declaration in one component context. The matrices define the decision at each parsed node. Global presence rules and component evidence can add an enclosing finding. The finding tree exposes decisions, selections and evidence. `ComplianceEvaluation.GetActionSummary()` returns current actions and contingent branch conditions under the rules below.

## AND

All operands are mandatory. Rows are left operands and columns are right operands.

| AND | Allow | Conditional | Review | Deny |
| --- | --- | --- | --- | --- |
| Allow | Allow | Conditional | Review | Deny |
| Conditional | Conditional | Conditional | Review | Deny |
| Review | Review | Review | Review | Deny |
| Deny | Deny | Deny | Deny | Deny |

For two or more operands, any denial yields `AndDenied`, otherwise any review yields `AndUnresolved`, otherwise any pending condition yields `AndConditional`. When all operands pass, the reason is `AndAllAllowed`. Each mandatory operand retains its own evidence.

## OR without a recorded choice

| Open OR | Allow | Conditional | Review | Deny |
| --- | --- | --- | --- | --- |
| Allow | Conditional | Conditional | Conditional | Conditional |
| Conditional | Conditional | Conditional | Conditional | Conditional |
| Review | Conditional | Conditional | Review | Review |
| Deny | Conditional | Conditional | Review | Deny |

For two or more alternatives, any allowed or conditional route yields `Conditional / ChoiceRequired`, even if only one route is eligible. All denied yields `Deny / OrAllDenied`. All other reachable combinations yield `Review / OrUnresolved`.

## OR with a valid recorded choice

| Selected operand | OR decision | OR reason |
| --- | --- | --- |
| Allow | Allow | ChoiceAllowed |
| Conditional | Conditional | Selected operand's reason |
| Review | Review | Selected operand's reason |
| Deny | Deny | ChoiceDenied |

This table applies for every combination of unselected siblings, at every nesting level. Findings retain all operands in source order. A selection identifies a direct operand by its index in the full child list. Two identical license strings can identify distinct occurrences.

## Input and selection boundaries

* **Unknown (S1).** Missing license data, whole-field `NOASSERTION` and an exhausted expression budget can produce `Unknown` for the input. A syntactically valid, unrecognized license ID produces `Review / UnknownId`. The matrices cover operands of successfully parsed SPDX expressions. If a future input model admits an unknown operand, preserve missing information: `Allow AND Unknown` and `Deny OR Unknown` yield `Unknown` in the absence of an independent review reason. A proven denial on a mandatory route still yields `Deny`.
* **Invalid (S2).** A malformed declaration, such as `MIT OR (`, fails as a whole. A stale, conflicting or invalid selection set is rejected atomically with `Invalid / SelectionMismatch`. No decisions are inferred from a usable prefix or a partly accepted set. `Invalid` as a hypothetical child has no public aggregation table. On a parsed declaration, an explicit presence ban can give the top-level `Deny / ProhibitedPresence` even for an unselected occurrence or a rejected selection. The underlying finding remains available.

## Current actions and branch conditions (S3)

A **current action** is a step required on the active route. A **branch condition** is a step that would apply if its alternative were chosen. Selecting a branch changes which actions apply. It does not erase evidence.

* On an active AND, inspect every operand. Parentheses preserve the route.
* At an active open OR with reason `ChoiceRequired`, the current action is to choose one direct alternative. Each alternative's pending conditions are contingent on that particular choice. For nested open ORs, the ancestor choices identify when an inner choice or condition becomes current.
* At a selected OR, follow the selected child. Conditions on discarded alternatives remain available as inactive evidence. They do not enter either the current queue or the remaining possible routes. An active open OR with reason `OrUnresolved` or `OrAllDenied` supplies no eligible choice to request.
* A mandatory AND operand can contribute an independent step alongside an ordinary denial or review elsewhere in the AND. Global presence bans, invalid selections, incomplete evaluations and unresolved source conflicts suppress action guidance while retaining their evidence. A correction mismatch wrapper keeps its underlying finding as evidence.
* `ComplianceEvaluation.GetActionSummary()` projects known choices, NOTICE checks and legacy correction confirmations. Keep reasons, evidence, source locations and completion state with the evaluation when using the summary.

Assume MIT is allowed and an OFL-1.1 rule requires a particular NOTICE in the delivery:

| Evaluation of `OFL-1.1 OR MIT` | Decision / reason | Current action | Branch condition |
| --- | --- | --- | --- |
| No choice, NOTICE unverified | Conditional / ChoiceRequired | Record one choice | Choosing OFL requires checking NOTICE |
| OFL selected, NOTICE unverified | Conditional / NoticeUnverified | Verify NOTICE for this delivery | None |
| OFL selected, NOTICE verified | Allow / ChoiceAllowed | None | None |
| MIT selected, NOTICE unverified | Allow / ChoiceAllowed | None | None. OFL evidence remains in the tree. |

See [policy, selection and evidence APIs](compliance.md).
