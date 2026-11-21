namespace Spdx.Expressions {
    /// <summary>An application-supplied extracted license. Its fields are checked when building the context.</summary>
    public sealed class SpdxLicenseDefinition {
        /// <summary>Supplies an extracted license for indexing under its owning document.</summary>
        public SpdxLicenseDefinition(string documentIdentity, string licenseId, string extractedText,
            string? name = null, string? crossReference = null) {
            DocumentIdentity = documentIdentity;
            LicenseId = licenseId;
            ExtractedText = extractedText;
            Name = name;
            CrossReference = crossReference;
        }
        /// <summary>Gets the opaque identity of the available document containing the definition.</summary>
        public string DocumentIdentity { get; }
        /// <summary>Gets the case-sensitive component after LicenseRef-.</summary>
        public string LicenseId { get; }
        /// <summary>Gets the nonempty verbatim license text, including original whitespace.</summary>
        public string ExtractedText { get; }
        /// <summary>Gets the optional original license name.</summary>
        public string? Name { get; }
        /// <summary>Gets the optional original source link supplied by the application.</summary>
        public string? CrossReference { get; }
    }
    /// <summary>A document's local name for another document. Fields are checked when building the context.</summary>
    public sealed class SpdxDocumentBinding {
        /// <summary>Supplies one external document binding declared in the source document.</summary>
        public SpdxDocumentBinding(string sourceDocumentIdentity, string documentAlias, string targetDocumentIdentity) {
            SourceDocumentIdentity = sourceDocumentIdentity;
            DocumentAlias = documentAlias;
            TargetDocumentIdentity = targetDocumentIdentity;
        }
        /// <summary>Gets the opaque identity of the available document declaring the binding.</summary>
        public string SourceDocumentIdentity { get; }
        /// <summary>Gets the case-sensitive component after DocumentRef-.</summary>
        public string DocumentAlias { get; }
        /// <summary>Gets the target identity, including when its document is unavailable.</summary>
        public string TargetDocumentIdentity { get; }
    }
    /// <summary>Inclusive budgets for indexing application input. Neither setting preallocates storage.</summary>
    public sealed class SpdxReferenceContextOptions {
        /// <summary>Maximum combined count of available documents, definitions and bindings, including repeats.</summary>
        public int MaxEntries { get; init; } = 65_536;
        /// <summary>Maximum combined UTF-16 length of all supplied string fields, counting each occurrence.</summary>
        public int MaxCharacters { get; init; } = 16_777_216;
    }
    /// <summary>The one-step lookup outcome for a single reference occurrence.</summary>
    public enum SpdxReferenceResolutionStatus {
        /// <summary>The unique definition in the target document is available.</summary>
        Resolved,
        /// <summary>The source document has no binding for the qualifier.</summary>
        UnknownDocumentAlias,
        /// <summary>The binding names a document whose contents were not provided.</summary>
        DocumentUnavailable,
        /// <summary>The available target has no matching license ID.</summary>
        DefinitionMissing,
        /// <summary>The source declared the same alias more than once, even if the targets match.</summary>
        DuplicateBinding,
        /// <summary>The target declared the same license ID more than once, even if the texts match.</summary>
        DuplicateDefinition
    }
    /// <summary>The provenance and lookup outcome of one original syntax node.</summary>
    public sealed class SpdxReferenceResolution {
        internal SpdxReferenceResolution(SpdxReferenceExpression occurrence, string sourceDocumentIdentity,
            string? targetDocumentIdentity, SpdxReferenceResolutionStatus status, SpdxLicenseDefinition? definition = null) {
            Occurrence = occurrence;
            SourceDocumentIdentity = sourceDocumentIdentity;
            TargetDocumentIdentity = targetDocumentIdentity;
            Status = status;
            Definition = definition;
        }
        /// <summary>Gets the original reference with its absolute UTF-16 span and component spans.</summary>
        public SpdxReferenceExpression Occurrence { get; }
        /// <summary>Gets the identity of the document containing the expression.</summary>
        public string SourceDocumentIdentity { get; }
        /// <summary>Gets the target identity, or null for an unknown or duplicate alias.</summary>
        public string? TargetDocumentIdentity { get; }
        /// <summary>Gets the lookup outcome for this occurrence.</summary>
        public SpdxReferenceResolutionStatus Status { get; }
        /// <summary>Gets the actual supplied definition on success, otherwise null.</summary>
        public SpdxLicenseDefinition? Definition { get; }
    }
}
