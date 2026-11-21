using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;

namespace Spdx.Expressions {
    /// <summary>An immutable index of application-supplied documents, extracted licenses and local alias bindings.</summary>
    public sealed class SpdxReferenceContext {
        const string DuplicateDocument = "An available document identity was supplied more than once.";
        const string UnknownOwner = "The document owning this entry must be listed among the available documents.";
        const string UnknownSource = "The source document must be listed among the available documents.";
        const string InvalidComponent = "A reference component must contain only ASCII letters, digits, hyphens or periods.";
        const string EntryLimit = "The combined context entry limit was exceeded.";
        const string CharacterLimit = "The combined context character limit was exceeded.";
        readonly Dictionary<string, DocumentIndex> documents;
        readonly int longestIdentity;
        SpdxReferenceContext(Dictionary<string, DocumentIndex> documents, int longestIdentity) {
            this.documents = documents;
            this.longestIdentity = longestIdentity;
        }
        /// <summary>Indexes each input sequence once using ordinal, case-sensitive keys.</summary>
        /// <param name="availableDocuments">Unique nonempty opaque identities, including documents with no definitions.</param>
        /// <param name="definitions">Definitions owned by available documents. IDs are nonempty unprefixed components.</param>
        /// <param name="bindings">Aliases owned by available documents. Targets may be unavailable.</param>
        /// <param name="options">Positive combined input budgets, or defaults.</param>
        /// <param name="cancellationToken">Cancellation returns no partial context.</param>
        /// <remarks>Duplicate definitions or aliases remain ambiguous for lookup, including identical repeats.</remarks>
        /// <exception cref="ArgumentException">A required input is null, empty or invalid, an owner is missing,
        /// or an available identity is repeated.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A budget is nonpositive or exceeded.</exception>
        /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
        public static SpdxReferenceContext Create(IEnumerable<string> availableDocuments, IEnumerable<SpdxLicenseDefinition> definitions,
            IEnumerable<SpdxDocumentBinding> bindings, SpdxReferenceContextOptions? options = null, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(availableDocuments);
            ArgumentNullException.ThrowIfNull(definitions);
            ArgumentNullException.ThrowIfNull(bindings);
            options ??= new SpdxReferenceContextOptions();
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxEntries);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxCharacters);
            int remainingEntries = options.MaxEntries;
            long remainingCharacters = options.MaxCharacters;
            var documents = new Dictionary<string, DocumentIndex>(StringComparer.Ordinal);
            int longestIdentity = 0;
            cancellationToken.ThrowIfCancellationRequested();
            foreach(string identity in availableDocuments) {
                ArgumentException.ThrowIfNullOrEmpty(identity);
                ChargeEntry(identity.Length);
                if(!documents.TryAdd(identity, new DocumentIndex()))
                    throw new ArgumentException(DuplicateDocument, nameof(availableDocuments));
                longestIdentity = Math.Max(longestIdentity, identity.Length);
            }
            cancellationToken.ThrowIfCancellationRequested();
            foreach(var definition in definitions) {
                ArgumentNullException.ThrowIfNull(definition);
                ArgumentException.ThrowIfNullOrEmpty(definition.DocumentIdentity);
                ArgumentException.ThrowIfNullOrEmpty(definition.LicenseId);
                ArgumentException.ThrowIfNullOrEmpty(definition.ExtractedText);
                ChargeEntry((long)definition.DocumentIdentity.Length + definition.LicenseId.Length + definition.ExtractedText.Length +
                    (definition.Name?.Length ?? 0) + (definition.CrossReference?.Length ?? 0));
                CheckComponent(definition.LicenseId, nameof(definitions), cancellationToken);
                if(!documents.TryGetValue(definition.DocumentIdentity, out var owner))
                    throw new ArgumentException(UnknownOwner, nameof(definitions));
                if(!owner.Definitions.TryAdd(definition.LicenseId, definition))
                    owner.Definitions[definition.LicenseId] = null;
            }
            cancellationToken.ThrowIfCancellationRequested();
            foreach(var binding in bindings) {
                ArgumentNullException.ThrowIfNull(binding);
                ArgumentException.ThrowIfNullOrEmpty(binding.SourceDocumentIdentity);
                ArgumentException.ThrowIfNullOrEmpty(binding.DocumentAlias);
                ArgumentException.ThrowIfNullOrEmpty(binding.TargetDocumentIdentity);
                ChargeEntry((long)binding.SourceDocumentIdentity.Length + binding.DocumentAlias.Length + binding.TargetDocumentIdentity.Length);
                CheckComponent(binding.DocumentAlias, nameof(bindings), cancellationToken);
                if(!documents.TryGetValue(binding.SourceDocumentIdentity, out var owner))
                    throw new ArgumentException(UnknownOwner, nameof(bindings));
                documents.TryGetValue(binding.TargetDocumentIdentity, out var index);
                var target = new Target(binding.TargetDocumentIdentity, index);
                if(!owner.Aliases.TryAdd(binding.DocumentAlias, target))
                    owner.Aliases[binding.DocumentAlias] = null;
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new SpdxReferenceContext(documents, longestIdentity);
            void ChargeEntry(long characters) {
                cancellationToken.ThrowIfCancellationRequested();
                if(remainingEntries == 0)
                    throw new ArgumentOutOfRangeException(nameof(options.MaxEntries), EntryLimit);
                if(characters > remainingCharacters)
                    throw new ArgumentOutOfRangeException(nameof(options.MaxCharacters), CharacterLimit);
                remainingEntries--;
                remainingCharacters -= characters;
            }
        }
        static void CheckComponent(string component, string parameter, CancellationToken cancellationToken) {
            foreach(char c in component) {
                cancellationToken.ThrowIfCancellationRequested();
                if(!SpdxExpressionParser.IsIdentifierCharacter(c))
                    throw new ArgumentException(InvalidComponent, parameter);
            }
        }
        /// <summary>Resolves each reference in source order, including repetitions, using one local or qualified lookup.</summary>
        /// <param name="expression">A complete parsed expression. Its parse budgets bound traversal and result count.</param>
        /// <param name="sourceDocumentIdentity">Identity of the available document containing the expression.</param>
        /// <param name="cancellationToken">Cancellation returns no partial results.</param>
        /// <returns>An immutable list containing one result per reference, with the original syntax nodes.</returns>
        /// <exception cref="ArgumentException">The expression is null or the source identity is null, empty or unavailable.</exception>
        /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
        public IReadOnlyList<SpdxReferenceResolution> Resolve(SpdxParsedExpression expression, string sourceDocumentIdentity,
            CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(expression);
            ArgumentException.ThrowIfNullOrEmpty(sourceDocumentIdentity);
            cancellationToken.ThrowIfCancellationRequested();
            if(sourceDocumentIdentity.Length > longestIdentity || !documents.TryGetValue(sourceDocumentIdentity, out var source))
                throw new ArgumentException(UnknownSource, nameof(sourceDocumentIdentity));
            var results = new List<SpdxReferenceResolution>();
            foreach(var node in expression.Root.EnumerateNodes(cancellationToken)) {
                if(node is SpdxReferenceExpression reference)
                    results.Add(ResolveOccurrence(reference, sourceDocumentIdentity, source));
            }
            if(results.Count == 0)
                return ReadOnlyCollection<SpdxReferenceResolution>.Empty;
            IReadOnlyList<SpdxReferenceResolution> snapshot = [.. results];
            cancellationToken.ThrowIfCancellationRequested();
            return snapshot;
        }
        static SpdxReferenceResolution ResolveOccurrence(SpdxReferenceExpression reference, string sourceIdentity, DocumentIndex source) {
            var target = new Target(sourceIdentity, source);
            if(reference.DocumentId is string alias) {
                if(!source.Aliases.TryGetValue(alias, out var bound))
                    return new(reference, sourceIdentity, null, SpdxReferenceResolutionStatus.UnknownDocumentAlias);
                if(bound is null)
                    return new(reference, sourceIdentity, null, SpdxReferenceResolutionStatus.DuplicateBinding);
                target = bound.Value;
            }
            if(target.Index is null)
                return new(reference, sourceIdentity, target.Identity, SpdxReferenceResolutionStatus.DocumentUnavailable);
            if(!target.Index.Definitions.TryGetValue(reference.LicenseId, out var definition))
                return new(reference, sourceIdentity, target.Identity, SpdxReferenceResolutionStatus.DefinitionMissing);
            if(definition is null)
                return new(reference, sourceIdentity, target.Identity, SpdxReferenceResolutionStatus.DuplicateDefinition);
            return new(reference, sourceIdentity, target.Identity, SpdxReferenceResolutionStatus.Resolved, definition);
        }
        readonly record struct Target(string Identity, DocumentIndex? Index);
        sealed class DocumentIndex {
            internal Dictionary<string, SpdxLicenseDefinition?> Definitions { get; } = new(StringComparer.Ordinal);
            internal Dictionary<string, Target?> Aliases { get; } = new(StringComparer.Ordinal);
        }
    }
}
