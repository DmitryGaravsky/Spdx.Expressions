using System;
using System.Collections.Generic;
using System.Threading;
using Spdx.Expressions.Internal;

namespace Spdx.Expressions {
    /// <summary>Checks identifier namespaces and reports every occurrence without resolving references.</summary>
    public static class SpdxExpressionValidator {
        const string UnknownLicense = "The license identifier is absent from the supplied catalogue.";
        const string UnknownException = "The exception identifier is absent from the supplied catalogue.";
        const string WrongKind = "The identifier belongs only to the other catalogue namespace.";
        const string Deprecated = "The identifier is deprecated in the supplied catalogue; its spelling is preserved.";
        /// <summary>Visits the complete AST even after exhausting the diagnostic storage budget.</summary>
        /// <param name="expression">A successfully parsed expression.</param>
        /// <param name="registry">The explicit immutable catalogue to use.</param>
        /// <param name="options">Diagnostic storage options, or defaults.</param>
        /// <param name="cancellationToken">Cancellation returns no partial result.</param>
        /// <returns>All occurrences and independent identifier-error, reference and truncation flags.</returns>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">MaxDiagnostics is less than one.</exception>
        /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
        public static SpdxValidationResult Validate(SpdxParsedExpression expression, SpdxLicenseRegistry registry,
            SpdxValidationOptions? options = null, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(expression);
            ArgumentNullException.ThrowIfNull(registry);
            int budget = options?.MaxDiagnostics ?? 128;
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(budget);
            cancellationToken.ThrowIfCancellationRequested();
            var identifiers = new List<SpdxIdentifierOccurrence>();
            var references = new List<SpdxReferenceOccurrence>();
            var diagnostics = new List<SpdxDiagnostic>();
            bool errors = false;
            bool truncated = false;
            foreach(var (node, phase) in SyntaxTraversal.Walk(expression.Root, cancellationToken)) {
                if(phase == SyntaxTraversal.Enter && node is SpdxReferenceExpression reference)
                    references.Add(new(reference));
                if(phase == SyntaxTraversal.Enter && node is SpdxLicenseExpression license)
                    Check(license.Identifier, license.IdentifierSpan, SpdxIdentifierRole.License);
                if(phase == SyntaxTraversal.Exit && node is SpdxWithExpression with)
                    Check(with.ExceptionIdentifier, with.ExceptionSpan, SpdxIdentifierRole.Exception);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new(registry.Identity, errors, truncated, identifiers, references, diagnostics);
            void Check(string identifier, SpdxSourceSpan span, SpdxIdentifierRole role) {
                bool licenseFound = registry.TryGetLicense(identifier, out var license);
                bool exceptionFound = registry.TryGetException(identifier, out var exception);
                bool known = role == SpdxIdentifierRole.License ? licenseFound : exceptionFound;
                bool other = role == SpdxIdentifierRole.License ? exceptionFound : licenseFound;
                var status = known ? SpdxIdentifierLookupStatus.Known : other ? SpdxIdentifierLookupStatus.WrongKind : SpdxIdentifierLookupStatus.Unknown;
                string? canonical = known ? role == SpdxIdentifierRole.License ? license!.Identifier : exception!.Identifier : null;
                bool? deprecated = known ? role == SpdxIdentifierRole.License ? license!.IsDeprecated : exception!.IsDeprecated : null;
                identifiers.Add(new(identifier, span, role, status, canonical, deprecated));
                errors |= !known;
                if(known && deprecated != true)
                    return;
                if(diagnostics.Count == budget) {
                    truncated = true;
                    return;
                }
                string code = known ? "SPDX_ID_DEPRECATED" : other ? "SPDX_ID_WRONG_KIND" :
                    role == SpdxIdentifierRole.License ? "SPDX_ID_UNKNOWN_LICENSE" : "SPDX_ID_UNKNOWN_EXCEPTION";
                string message = known ? Deprecated : other ? WrongKind :
                    role == SpdxIdentifierRole.License ? UnknownLicense : UnknownException;
                diagnostics.Add(new(code, known ? SpdxDiagnosticSeverity.Warning : SpdxDiagnosticSeverity.Error, span, message));
            }
        }
    }
}
