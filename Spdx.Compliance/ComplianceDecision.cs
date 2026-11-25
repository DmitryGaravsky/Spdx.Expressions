using System.Collections.Immutable;
using System.Threading;
using Spdx.Expressions;

namespace Spdx.Compliance {
    /// <summary>The outcome of applying a distribution policy to one supplied declaration.</summary>
    public enum ComplianceDecision {
        /// <summary>The applicable rules allow distribution under the supplied evidence.</summary>
        Allow,
        /// <summary>The applicable rules deny distribution.</summary>
        Deny,
        /// <summary>A required choice or delivery check remains open.</summary>
        Conditional,
        /// <summary>A human policy determination or better evidence is required.</summary>
        Review,
        /// <summary>There is insufficient information to determine the outcome.</summary>
        Unknown,
        /// <summary>The declaration or recorded choice is invalid.</summary>
        Invalid
    }
    /// <summary>Stable reasons for license decisions. Serialize names instead of ordinal values.</summary>
    public enum ComplianceReason {
        /// <summary>Profile is absent from the supplied policy.</summary>
        ProfileUnconfigured,
        /// <summary>No license field was supplied.</summary>
        FieldMissing,
        /// <summary>A string field is empty.</summary>
        FieldEmpty,
        /// <summary>Explicit SPDX NONE.</summary>
        FieldNone,
        /// <summary>Explicit SPDX NOASSERTION.</summary>
        FieldNoassertion,
        /// <summary>The observed value has an unsupported shape or identifier slot.</summary>
        WrongField,
        /// <summary>The license array is empty.</summary>
        NoLicenseEntries,
        /// <summary>The component supplies multiple declarations.</summary>
        MultipleEntries,
        /// <summary>The SPDX parser rejected the syntax.</summary>
        InvalidSyntax,
        /// <summary>A configured or native expression budget was exhausted.</summary>
        LimitExceeded,
        /// <summary>The named license points to a file without identifying its terms.</summary>
        FileOnly,
        /// <summary>The name has no applicable configured alias or ownership rule.</summary>
        NamedTerms,
        /// <summary>No matching trusted owner confirmation was supplied.</summary>
        OwnershipUnconfirmed,
        /// <summary>The original license name and versioned identity match the owner rule.</summary>
        OwnershipConfirmed,
        /// <summary>The identifier is absent from the license catalogue.</summary>
        UnknownId,
        /// <summary>The identifier is deprecated in the license catalogue.</summary>
        DeprecatedId,
        /// <summary>No applicable distribution rule exists.</summary>
        NoPolicyRule,
        /// <summary>An explicit allow rule applies.</summary>
        ListedAllow,
        /// <summary>An explicit deny rule applies.</summary>
        ListedDeny,
        /// <summary>A license with plus has no exact or-later rule.</summary>
        OrLaterUnlisted,
        /// <summary>The complete WITH pair is explicitly allowed.</summary>
        PairAllowed,
        /// <summary>The complete WITH pair has no exact rule.</summary>
        PairUnlisted,
        /// <summary>The complete WITH pair is explicitly denied.</summary>
        PairDenied,
        /// <summary>The selected presence-ban profile prohibits an occurrence.</summary>
        ProhibitedPresence,
        /// <summary>One conjunct is denied.</summary>
        AndDenied,
        /// <summary>One conjunct is invalid.</summary>
        AndInvalid,
        /// <summary>One conjunct requires review or is unknown.</summary>
        AndUnresolved,
        /// <summary>One conjunct has an open condition; the others are allowed or conditional.</summary>
        AndConditional,
        /// <summary>Every conjunct is allowed.</summary>
        AndAllAllowed,
        /// <summary>An eligible alternative exists but no choice has been recorded.</summary>
        ChoiceRequired,
        /// <summary>The supplied choice identifies exactly one direct alternative and it is allowed.</summary>
        ChoiceAllowed,
        /// <summary>The chosen alternative is denied.</summary>
        ChoiceDenied,
        /// <summary>The supplied choices are stale, mixed, duplicated, invalid or outside the active route.</summary>
        SelectionMismatch,
        /// <summary>Every alternative is denied.</summary>
        OrAllDenied,
        /// <summary>There is no eligible alternative and at least one alternative is unresolved.</summary>
        OrUnresolved,
        /// <summary>The reference could not be resolved with the supplied evidence.</summary>
        ReferenceUnresolved,
        /// <summary>The source document has no binding for the qualifier.</summary>
        AliasUnknown,
        /// <summary>The target document has no matching definition.</summary>
        DefinitionMissing,
        /// <summary>The resolved definition matches the approved document, identifier and digest.</summary>
        ReferenceApproved,
        /// <summary>The resolved definition has no matching approval.</summary>
        ReferenceUnapproved,
        /// <summary>The configured notice has the expected digest.</summary>
        NoticeVerified,
        /// <summary>The complete delivery inventory lacks the required notice.</summary>
        NoticeMissing,
        /// <summary>The supplied notice digest differs from the obligation.</summary>
        NoticeMismatch,
        /// <summary>There is insufficient delivery evidence to verify the notice.</summary>
        NoticeUnverified,
        /// <summary>Observed license evidence differs from the declaration.</summary>
        SourceConflict,
        /// <summary>A legacy pending correction route requires confirmation without a catalog.</summary>
        CorrectionUnconfirmed,
        /// <summary>A correction claim failed verification against the pinned catalog.</summary>
        CorrectionMismatch
    }
    /// <summary>An immutable explanation for an expression, operand or declaration-level decision.</summary>
    /// <param name="Decision">Policy outcome.</param>
    /// <param name="Reason">Machine-readable reason.</param>
    public sealed record ComplianceFinding(ComplianceDecision Decision, ComplianceReason Reason) {
        /// <summary>Gets the original operand text, when applicable.</summary>
        public string? Operand { get; internal init; }
        /// <summary>Gets the range in the original input string, when applicable.</summary>
        public SpdxSourceSpan? Span { get; internal init; }
        /// <summary>Gets the left operand range of a WITH pair, when applicable.</summary>
        public SpdxSourceSpan? LicenseSpan { get; internal init; }
        /// <summary>Gets the exception identifier range of a WITH pair, when applicable.</summary>
        public SpdxSourceSpan? ExceptionSpan { get; internal init; }
        /// <summary>Gets the operand type, when applicable.</summary>
        public SpdxExpressionKind? Kind { get; internal init; }
        /// <summary>Gets all child explanations in source order, including unselected alternatives.</summary>
        public ImmutableArray<ComplianceFinding> Children { get; internal init; } = [];
        /// <summary>Gets the eligible direct alternatives when a choice is still open.</summary>
        public ImmutableArray<string> Choices { get; internal init; } = [];
        /// <summary>Gets the selected operand text, or the attempted legacy text on a mismatch.</summary>
        public string? Selection { get; internal init; }
        /// <summary>Gets the selected operand index in the full direct child list.</summary>
        public int? SelectedIndex { get; internal init; }
        /// <summary>Gets the canonical SPDX license identifier when the simple license is known.</summary>
        public string? CanonicalLicenseId { get; internal init; }
        /// <summary>Gets whether the known simple license identifier is deprecated in the registry.</summary>
        public bool? IsDeprecated { get; internal init; }
        /// <summary>Gets available reference resolution and approval evidence.</summary>
        public ReferenceFinding? Reference { get; internal init; }
        /// <summary>Gets available delivery evidence.</summary>
        public NoticeFinding? Notice { get; internal init; }
        /// <summary>Gets available ownership evidence.</summary>
        public OwnershipFinding? Ownership { get; internal init; }
        /// <summary>Gets conflicting observed texts in source order.</summary>
        public ImmutableArray<string> ConflictingObservations { get; internal init; } = [];
        /// <summary>Gets the configured license identifier for a named alias evaluated as an SPDX ID.</summary>
        public string? AliasTarget { get; internal init; }
        /// <summary>Gets the correction claim ID associated with a legacy pending rule.</summary>
        public string? PendingCorrectionId { get; internal init; }
        /// <summary>Gets the underlying SPDX diagnostics supplied by the field reader.</summary>
        public ImmutableArray<SpdxDiagnostic> Diagnostics { get; internal init; } = [];
    }
    /// <summary>Reference lookup and exact-definition approval evidence.</summary>
    /// <param name="SourceDocument">Supplied local scope, if any.</param>
    /// <param name="TargetDocument">Resolved target document identity, if known.</param>
    /// <param name="LicenseId">The complete local LicenseRef identifier.</param>
    /// <param name="Status">Native resolution outcome, or null when no scope was available.</param>
    /// <param name="Sha256">Digest of the original definition bytes, on successful resolution.</param>
    /// <param name="Approved">Whether the definition matched a configured approval.</param>
    public sealed record ReferenceFinding(string? SourceDocument, string? TargetDocument, string LicenseId,
        SpdxReferenceResolutionStatus? Status, string? Sha256, bool Approved);
    /// <summary>Evidence used to evaluate a notice obligation.</summary>
    /// <param name="Delivery">The inventory label, if supplied.</param>
    /// <param name="File">Configured relative notice path.</param>
    /// <param name="ExpectedSha256">Configured digest.</param>
    /// <param name="ObservedSha256">Supplied digest, if available.</param>
    /// <param name="InventoryComplete">Whether the caller asserted the inventory is complete.</param>
    public sealed record NoticeFinding(string? Delivery, string File, string ExpectedSha256,
        string? ObservedSha256, bool InventoryComplete);
    /// <summary>Evidence used to evaluate a named ownership rule.</summary>
    /// <param name="PackageIdentity">Supplied versioned package identity, if available.</param>
    /// <param name="Name">Original declaration, including punctuation and hyphenation.</param>
    /// <param name="ExpectedOwner">The owner required by policy.</param>
    /// <param name="ObservedOwner">The independently supplied owner, if available.</param>
    public sealed record OwnershipFinding(string? PackageIdentity, string Name, string ExpectedOwner, string? ObservedOwner);
    /// <summary>Identity of the exact versioned policy snapshot.</summary>
    /// <param name="Id">Caller-assigned policy identifier.</param>
    /// <param name="SchemaVersion">Policy schema version.</param>
    /// <param name="Sha256">Digest over the original UTF-8 policy bytes.</param>
    /// <param name="CatalogSha256">Digest of the separately pinned correction catalog, when configured.</param>
    public sealed record CompliancePolicyIdentity(string Id, int SchemaVersion, string Sha256, string? CatalogSha256);
    /// <summary>The evaluation of one license field in original source coordinates.</summary>
    /// <param name="Policy">Exact policy identity.</param>
    /// <param name="Profile">Requested profile name.</param>
    /// <param name="SourceText">Original declaration text, if a single string was supplied.</param>
    /// <param name="Finding">Decision and explanation.</param>
    /// <param name="IsComplete">False if a resource limit prevented complete evaluation.</param>
    public sealed record ComplianceEvaluation(CompliancePolicyIdentity Policy, string Profile, string? SourceText,
        ComplianceFinding Finding, bool IsComplete) {
        /// <summary>Projects known policy steps from the final finding. The decision, blocking evidence and
        /// completion status remain on this evaluation. The projection acquires no external evidence.</summary>
        /// <exception cref="System.OperationCanceledException">Cancellation was requested.</exception>
        public ComplianceActionSummary GetActionSummary(CancellationToken cancellationToken = default) {
            return ActionProjection.Project(this, cancellationToken);
        }
    }
}
