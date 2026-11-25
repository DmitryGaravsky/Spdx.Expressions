using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Spdx.Expressions;

namespace Spdx.Compliance {
    /// <summary>Caller-supplied scope and evidence for evaluating one license declaration.</summary>
    public sealed class ComplianceContext {
        /// <summary>Policy profile. The default profile always exists.</summary>
        public string Profile { get; init; } = "default";
        /// <summary>Exact original text of one unique direct alternative of the outer OR.</summary>
        public string? SelectedBranch { get; init; }
        /// <summary>Choices by source span on active paths. Supply this or SelectedBranch for a declaration.</summary>
        public OrSelectionSet? OrSelections { get; init; }
        /// <summary>Identity of the source document in the supplied reference evidence.</summary>
        public string? Document { get; init; }
        /// <summary>Independently acquired reference definitions and document bindings.</summary>
        public ComplianceReferenceEvidence? References { get; init; }
        /// <summary>The relevant delivery inventory, when a notice obligation can be checked.</summary>
        public NoticeDelivery? Delivery { get; init; }
        /// <summary>Externally confirmed owner inventory, keyed by exact versioned NuGet package URL.</summary>
        public PackageOwnership? Ownership { get; init; }
    }
    /// <summary>One extracted license from exact UTF-8 bytes supplied by the caller.</summary>
    public sealed class ComplianceReferenceDefinition {
        /// <summary>Decodes the supplied bytes strictly and records their SHA-256. The caller retains the bytes.
        /// The identifier excludes the LicenseRef- prefix. The reference context checks the indexed fields.</summary>
        public ComplianceReferenceDefinition(string document, string identifier, ReadOnlySpan<byte> utf8,
            CancellationToken cancellationToken = default) {
            string digest = Input.Digest(utf8, cancellationToken);
            string text = new UTF8Encoding(false, true).GetString(utf8);
            cancellationToken.ThrowIfCancellationRequested();
            Definition = new(document, identifier, text);
            Sha256 = digest;
        }
        internal SpdxLicenseDefinition Definition { get; }
        internal string Sha256 { get; }
    }
    /// <summary>An immutable index of caller-supplied SPDX documents, bindings and exact definition digests.</summary>
    public sealed class ComplianceReferenceEvidence {
        readonly FrozenSet<string> documents;
        readonly FrozenDictionary<SpdxLicenseDefinition, string> digests;
        readonly SpdxReferenceContext context;
        /// <summary>Indexes the supplied evidence under the native SPDX reference context's inclusive budgets.
        /// Callers supply document identities and resolve any external acquisition before constructing this index.</summary>
        public ComplianceReferenceEvidence(IEnumerable<string> documents, IEnumerable<ComplianceReferenceDefinition> definitions,
            IEnumerable<SpdxDocumentBinding> bindings, SpdxReferenceContextOptions? options = null,
            CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(documents);
            ArgumentNullException.ThrowIfNull(definitions);
            var available = new HashSet<string>(StringComparer.Ordinal);
            var fingerprints = new Dictionary<SpdxLicenseDefinition, string>();
            context = SpdxReferenceContext.Create(ReadDocuments(), ReadDefinitions(), bindings, options, cancellationToken);
            this.documents = available.ToFrozenSet(StringComparer.Ordinal);
            digests = fingerprints.ToFrozenDictionary();
            cancellationToken.ThrowIfCancellationRequested();
            IEnumerable<string> ReadDocuments() {
                foreach(string document in documents) {
                    yield return document;
                    available.Add(document);
                }
            }
            IEnumerable<SpdxLicenseDefinition> ReadDefinitions() {
                foreach(var item in definitions) {
                    ArgumentNullException.ThrowIfNull(item);
                    yield return item.Definition;
                    fingerprints.TryAdd(item.Definition, item.Sha256);
                }
            }
        }
        internal IReadOnlyList<SpdxReferenceResolution> Resolve(SpdxParsedExpression expression, string? document, CancellationToken token) {
            return document is not null && documents.Contains(document) ? context.Resolve(expression, document, token) : [];
        }
        internal string GetDigest(SpdxLicenseDefinition definition) {
            return digests[definition];
        }
    }
    /// <summary>Caller-supplied hashes for a specific delivery inventory.</summary>
    public sealed class NoticeDelivery {
        readonly FrozenDictionary<string, string> digests;
        /// <summary>Snapshots path-to-SHA-256 entries. Use relative, slash-separated paths.
        /// Set complete only when absent paths are known to be absent from this delivery.</summary>
        public NoticeDelivery(string id, bool complete, IEnumerable<KeyValuePair<string, string>> fileDigests,
            CancellationToken cancellationToken = default) {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            ArgumentNullException.ThrowIfNull(fileDigests);
            Id = id;
            IsComplete = complete;
            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach(var (path, digest) in fileDigests) {
                cancellationToken.ThrowIfCancellationRequested();
                if(!Input.IsRelativeFile(path) || !Input.IsDigest(digest) || !files.TryAdd(path, digest.ToLowerInvariant())) {
                    const string message = "Each delivery entry needs a unique relative path and SHA-256 digest.";
                    throw new ArgumentException(message, nameof(fileDigests));
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            digests = files.ToFrozenDictionary(StringComparer.Ordinal);
        }
        /// <summary>Gets the label identifying the delivery.</summary>
        public string Id { get; }
        /// <summary>Gets the caller's assertion of inventory completeness.</summary>
        public bool IsComplete { get; }
        internal string? Find(string file) {
            return digests.GetValueOrDefault(file);
        }
    }
    /// <summary>An immutable, independently confirmed inventory of versioned NuGet package owners.</summary>
    public sealed class PackageOwnership {
        readonly FrozenDictionary<string, string> owners;
        /// <summary>Snapshots an ordinal mapping from exact versioned NuGet package URLs to owner labels.</summary>
        public PackageOwnership(IEnumerable<KeyValuePair<string, string>> entries, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(entries);
            var packages = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach(var (package, owner) in entries) {
                cancellationToken.ThrowIfCancellationRequested();
                if(!PackageIdentity.IsVersionedNuGetPurl(package) || string.IsNullOrWhiteSpace(owner) || !packages.TryAdd(package, owner)) {
                    const string message = "Each ownership entry needs a unique versioned NuGet package URL and a nonempty owner.";
                    throw new ArgumentException(message, nameof(entries));
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            owners = packages.ToFrozenDictionary(StringComparer.Ordinal);
        }
        internal string? Find(string? purl) {
            return purl is null ? null : owners.GetValueOrDefault(purl);
        }
    }
}
