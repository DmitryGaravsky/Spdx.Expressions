using System.Collections.Immutable;
using Spdx.Expressions;

namespace Spdx.Compliance {
    /// <summary>A policy step that can be identified from an evaluated declaration.</summary>
    public enum ComplianceActionKind {
        /// <summary>Record a direct alternative for an open OR.</summary>
        RecordChoice,
        /// <summary>Verify the NOTICE obligation for the applicable delivery.</summary>
        VerifyNotice,
        /// <summary>Confirm the correction identified by the legacy pending rule.</summary>
        ConfirmCorrection
    }
    /// <summary>An eligible direct alternative of an OR awaiting a recorded choice.</summary>
    /// <param name="OperandIndex">Zero-based index in the OR's full child list.</param>
    /// <param name="Finding">The direct child's original finding, including its text and span.</param>
    public sealed record OrActionAlternative(int OperandIndex, ComplianceFinding Finding);
    /// <summary>One actionable occurrence and its original decision evidence.</summary>
    /// <param name="Kind">The step to take on the applicable route.</param>
    /// <param name="Finding">The originating finding. Notice evidence and pending correction identifiers remain on this finding.</param>
    /// <param name="Alternatives">Eligible direct alternatives for a choice, in source order, or empty for other steps.</param>
    public sealed record ComplianceAction(ComplianceActionKind Kind, ComplianceFinding Finding, ImmutableArray<OrActionAlternative> Alternatives) {
        /// <summary>Gets the stable reason on the originating finding.</summary>
        public ComplianceReason Reason => Finding.Reason;
        /// <summary>Gets the UTF-16 span of the originating finding, when available.</summary>
        public SpdxSourceSpan? Span => Finding.Span;
    }
    /// <summary>A step contingent on choices at currently open OR occurrences.</summary>
    /// <param name="RequiredChoices">The missing choices, ordered from the outermost ancestor to the innermost.</param>
    /// <param name="Action">The step that becomes applicable on that route.</param>
    public sealed record ComplianceBranchCondition(ImmutableArray<OrSelection> RequiredChoices, ComplianceAction Action);
    /// <summary>Known steps for one evaluation, in source traversal order within each collection.</summary>
    /// <param name="CurrentActions">Steps on the active route, including those of mandatory AND operands beside a blocker.</param>
    /// <param name="BranchConditions">Steps on eligible routes contingent on missing OR choices.</param>
    public sealed record ComplianceActionSummary(ImmutableArray<ComplianceAction> CurrentActions,
        ImmutableArray<ComplianceBranchCondition> BranchConditions);
}
