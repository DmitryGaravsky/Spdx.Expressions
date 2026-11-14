using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Spdx.Expressions {

    /// <summary>An official-ID candidate occurrence, including repeated occurrences.</summary>
    public sealed class SpdxIdentifierOccurrence {
        internal SpdxIdentifierOccurrence(string identifier, SpdxSourceSpan span, SpdxIdentifierRole role,
            SpdxIdentifierLookupStatus status, string? canonical, bool? deprecated) {
            Identifier = identifier;
            Span = span;
            Role = role;
            LookupStatus = status;
            CanonicalIdentifier = canonical;
            IsDeprecated = deprecated;
        }
        /// <summary>Gets the original identifier spelling, excluding plus.</summary>
        public string Identifier { get; }
        /// <summary>Gets its range in the original source.</summary>
        public SpdxSourceSpan Span { get; }
        /// <summary>Gets the expected catalogue namespace.</summary>
        public SpdxIdentifierRole Role { get; }
        /// <summary>Gets the namespace-specific lookup outcome.</summary>
        public SpdxIdentifierLookupStatus LookupStatus { get; }
        /// <summary>Gets canonical spelling only for a known entry in the expected namespace.</summary>
        public string? CanonicalIdentifier { get; }
        /// <summary>Gets deprecation only for a known entry in the expected namespace.</summary>
        public bool? IsDeprecated { get; }
    }
    /// <summary>An unresolved reference occurrence. No resolution was attempted.</summary>
    public sealed class SpdxReferenceOccurrence {
        internal SpdxReferenceOccurrence(SpdxReferenceExpression reference) {
            Span = reference.Span;
            DocumentId = reference.DocumentId;
            LicenseId = reference.LicenseId;
        }
        /// <summary>Gets the complete reference range, including prefixes.</summary>
        public SpdxSourceSpan Span { get; }
        /// <summary>Gets the optional document component without its prefix.</summary>
        public string? DocumentId { get; }
        /// <summary>Gets the license component without its prefix.</summary>
        public string LicenseId { get; }
    }
    /// <summary>A full traversal outcome. The three flags are independent; none implies legal permission.</summary>
    public sealed class SpdxValidationResult {
        internal SpdxValidationResult(SpdxRegistryIdentity identity, bool errors, bool truncated,
            List<SpdxIdentifierOccurrence> identifiers, List<SpdxReferenceOccurrence> references, List<SpdxDiagnostic> diagnostics) {
            RegistryIdentity = identity;
            HasIdentifierErrors = errors;
            DiagnosticsTruncated = truncated;
            IdentifierOccurrences = identifiers.Count == 0 ? ReadOnlyCollection<SpdxIdentifierOccurrence>.Empty : Array.AsReadOnly(identifiers.ToArray());
            References = references.Count == 0 ? ReadOnlyCollection<SpdxReferenceOccurrence>.Empty : Array.AsReadOnly(references.ToArray());
            Diagnostics = diagnostics.Count == 0 ? ReadOnlyCollection<SpdxDiagnostic>.Empty : Array.AsReadOnly(diagnostics.ToArray());
        }
        /// <summary>Gets the exact catalogue used for validation.</summary>
        public SpdxRegistryIdentity RegistryIdentity { get; }
        /// <summary>Gets whether any identifier is unknown or in the wrong namespace, including omitted diagnostics.</summary>
        public bool HasIdentifierErrors { get; }
        /// <summary>Gets whether any reference occurs. Every reference remains unresolved.</summary>
        public bool HasUnresolvedReferences => References.Count != 0;
        /// <summary>Gets whether any diagnostic message was omitted by the message budget.</summary>
        public bool DiagnosticsTruncated { get; }
        /// <summary>Gets every official-ID candidate in source order, including repeats.</summary>
        public IReadOnlyList<SpdxIdentifierOccurrence> IdentifierOccurrences { get; }
        /// <summary>Gets every reference in source order, including repeats.</summary>
        public IReadOnlyList<SpdxReferenceOccurrence> References { get; }
        /// <summary>Gets the first MaxDiagnostics messages in source order, with no suppression marker.</summary>
        public IReadOnlyList<SpdxDiagnostic> Diagnostics { get; }
    }

}
