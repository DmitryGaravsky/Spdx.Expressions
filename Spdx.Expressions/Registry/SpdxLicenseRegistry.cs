using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using Spdx.Expressions.Internal;

namespace Spdx.Expressions {
    /// <summary>The list version and exact-byte SHA-256 identities of the paired source files.</summary>
    public sealed class SpdxRegistryIdentity {
        internal SpdxRegistryIdentity(string version, string licenses, string exceptions) {
            LicenseListVersion = version;
            LicensesSha256 = licenses;
            ExceptionsSha256 = exceptions;
        }
        /// <summary>Gets the declared list version, independent of the grammar and package versions.</summary>
        public string LicenseListVersion { get; }
        /// <summary>Gets lowercase SHA-256 hex of the exact licenses.json bytes.</summary>
        public string LicensesSha256 { get; }
        /// <summary>Gets lowercase SHA-256 hex of the exact exceptions.json bytes.</summary>
        public string ExceptionsSha256 { get; }
    }
    /// <summary>A canonical entry in the license namespace.</summary>
    public sealed class SpdxLicenseInfo {
        internal SpdxLicenseInfo(string identifier, bool deprecated) {
            Identifier = identifier;
            IsDeprecated = deprecated;
        }
        /// <summary>Gets the catalogue's canonical spelling.</summary>
        public string Identifier { get; }
        /// <summary>Gets the catalogue's declared deprecation flag.</summary>
        public bool IsDeprecated { get; }
    }
    /// <summary>A canonical entry in the exception namespace.</summary>
    public sealed class SpdxExceptionInfo {
        internal SpdxExceptionInfo(string identifier, bool deprecated) {
            Identifier = identifier;
            IsDeprecated = deprecated;
        }
        /// <summary>Gets the catalogue's canonical spelling.</summary>
        public string Identifier { get; }
        /// <summary>Gets the catalogue's declared deprecation flag for this exception.</summary>
        public bool IsDeprecated { get; }
    }
    /// <summary>An immutable, offline catalogue of license and exception identifiers.</summary>
    public sealed class SpdxLicenseRegistry {
        const string Version = "3.29.0";
        const string LicenseHash = "47d1cc681abe31166b342b6cc4aab13a6ba8ea5c48794697fe3bf8b1dbaf509a";
        const string ExceptionHash = "59ee7c150de521d635cbf9367b8cf543b8da6781ea3ac1c073d305524f84ada2";
        const string CorruptMessage = "The embedded SPDX catalogue is corrupt or inconsistent with its pinned identity.";
        const string DistinctStreamsMessage = "The catalogue inputs must be distinct streams.";
        const string UnreadableLicensesMessage = "The licenses stream must be readable.";
        const string UnreadableExceptionsMessage = "The exceptions stream must be readable.";
        static readonly Lazy<SpdxLicenseRegistry> Snapshot = new(LoadEmbedded);
        readonly FrozenDictionary<string, SpdxLicenseInfo> licenses;
        readonly FrozenDictionary<string, SpdxExceptionInfo> exceptions;
        internal SpdxLicenseRegistry(SpdxRegistryIdentity identity, List<SpdxLicenseInfo> licenseEntries,
            List<SpdxExceptionInfo> exceptionEntries, CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();
            Licenses = [.. licenseEntries.OrderBy(x => x.Identifier, StringComparer.Ordinal)];
            cancellationToken.ThrowIfCancellationRequested();
            Exceptions = [.. exceptionEntries.OrderBy(x => x.Identifier, StringComparer.Ordinal)];
            cancellationToken.ThrowIfCancellationRequested();
            licenses = Licenses.ToFrozenDictionary(x => x.Identifier, StringComparer.OrdinalIgnoreCase);
            cancellationToken.ThrowIfCancellationRequested();
            exceptions = Exceptions.ToFrozenDictionary(x => x.Identifier, StringComparer.OrdinalIgnoreCase);
            cancellationToken.ThrowIfCancellationRequested();
            Identity = identity;
        }
        /// <summary>Gets the thread-safe embedded v3.29.0 snapshot without network access.</summary>
        /// <exception cref="InvalidOperationException">Embedded resources are corrupt (a packaging defect).</exception>
        public static SpdxLicenseRegistry Default => Snapshot.Value;
        /// <summary>Loads a schema-checked catalogue from a complete pair of UTF-8 JSON documents.</summary>
        /// <param name="licensesUtf8">The licenses document. Its contents must remain stable during the call.</param>
        /// <param name="exceptionsUtf8">The exceptions document. Its contents must remain stable during the call.</param>
        /// <param name="options">Inclusive budgets, or null to use defaults.</param>
        /// <param name="cancellationToken">Cancellation observed during acquisition and validation.</param>
        /// <returns>A complete registry with the declared version and hashes of the supplied bytes, or one failure diagnostic.</returns>
        /// <remarks>
        /// The loader checks options before cancellation and processes the licenses input first.
        /// Inputs are copied during the call. Hashes cover the exact supplied slices, including
        /// BOMs and whitespace. The returned registry is independent of the inputs and <see cref="Default"/>.
        /// Success establishes structural validity of the supplied subset.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">An option is outside its documented range.</exception>
        /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
        public static SpdxRegistryLoadResult Load(ReadOnlyMemory<byte> licensesUtf8, ReadOnlyMemory<byte> exceptionsUtf8,
            SpdxRegistryLoadOptions? options = null, CancellationToken cancellationToken = default) {
            return RegistryLoader.Load(licensesUtf8, exceptionsUtf8, options, cancellationToken);
        }
        /// <summary>Loads a schema-checked catalogue from two readable streams containing UTF-8 JSON.</summary>
        /// <param name="licensesUtf8">The licenses document, starting at the stream's current position.</param>
        /// <param name="exceptionsUtf8">The exceptions document, starting at the stream's current position.</param>
        /// <param name="options">Inclusive budgets, or null to use defaults.</param>
        /// <param name="cancellationToken">Cancellation observed between synchronous reads and during validation.</param>
        /// <returns>A complete registry with the declared version and hashes of the supplied bytes, or one failure diagnostic.</returns>
        /// <remarks>
        /// Both null checks precede the same-instance check, then <see cref="Stream.CanRead"/> is checked
        /// in input order. Options are checked before cancellation. A read that throws propagates its
        /// exception; a normally returning read observes cancellation before interpreting the byte count.
        /// The streams remain open on every outcome. Each input is read through EOF or one byte past its
        /// effective byte budget, without seeking. The licenses input is acquired and validated before
        /// reading the exceptions input. Diagnostic offsets and hashes use the supplied suffix.
        /// Keep ownership and contents stable during the call. The returned registry is independent of
        /// the inputs and <see cref="Default"/>. Success establishes structural validity of the supplied subset.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Either stream is null.</exception>
        /// <exception cref="ArgumentException">The streams are the same instance or either is unreadable.</exception>
        /// <exception cref="ArgumentOutOfRangeException">An option is outside its documented range.</exception>
        /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
        public static SpdxRegistryLoadResult Load(Stream licensesUtf8, Stream exceptionsUtf8,
            SpdxRegistryLoadOptions? options = null, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(licensesUtf8);
            ArgumentNullException.ThrowIfNull(exceptionsUtf8);
            if(ReferenceEquals(licensesUtf8, exceptionsUtf8))
                throw new ArgumentException(DistinctStreamsMessage, nameof(exceptionsUtf8));
            if(!licensesUtf8.CanRead)
                throw new ArgumentException(UnreadableLicensesMessage, nameof(licensesUtf8));
            if(!exceptionsUtf8.CanRead)
                throw new ArgumentException(UnreadableExceptionsMessage, nameof(exceptionsUtf8));
            return RegistryLoader.Load(licensesUtf8, exceptionsUtf8, options, cancellationToken);
        }
        /// <summary>Gets the exact paired dataset identity.</summary>
        public SpdxRegistryIdentity Identity { get; }
        /// <summary>Gets all licenses in deterministic ordinal identifier order.</summary>
        public IReadOnlyList<SpdxLicenseInfo> Licenses { get; }
        /// <summary>Gets all exceptions in deterministic ordinal identifier order.</summary>
        public IReadOnlyList<SpdxExceptionInfo> Exceptions { get; }
        /// <summary>Looks up a license using OrdinalIgnoreCase. No aliases or whitespace trimming are applied.</summary>
        /// <exception cref="ArgumentNullException">The identifier is null.</exception>
        public bool TryGetLicense(string identifier, [NotNullWhen(true)] out SpdxLicenseInfo? license) {
            ArgumentNullException.ThrowIfNull(identifier);
            return licenses.TryGetValue(identifier, out license);
        }
        /// <summary>Looks up an exception using OrdinalIgnoreCase. No aliases or whitespace trimming are applied.</summary>
        /// <exception cref="ArgumentNullException">The identifier is null.</exception>
        public bool TryGetException(string identifier, [NotNullWhen(true)] out SpdxExceptionInfo? exception) {
            ArgumentNullException.ThrowIfNull(identifier);
            return exceptions.TryGetValue(identifier, out exception);
        }
        static SpdxLicenseRegistry LoadEmbedded() {
            try {
                var licenseEntries = Read("licenses", "licenseId", LicenseHash)
                    .Select(x => new SpdxLicenseInfo(x.Id, x.Deprecated)).ToList();
                var exceptionEntries = Read("exceptions", "licenseExceptionId", ExceptionHash)
                    .Select(x => new SpdxExceptionInfo(x.Id, x.Deprecated)).ToList();
                if(licenseEntries.Count == 0 || exceptionEntries.Count == 0 ||
                    licenseEntries.Select(x => x.Identifier).Intersect(exceptionEntries.Select(x => x.Identifier), StringComparer.OrdinalIgnoreCase).Any())
                    throw new InvalidOperationException(CorruptMessage);
                var identity = new SpdxRegistryIdentity(Version, LicenseHash, ExceptionHash);
                return new(identity, licenseEntries, exceptionEntries, CancellationToken.None);
            }
            catch(Exception error)
                when(error is JsonException or KeyNotFoundException or FormatException or ArgumentException) {
                throw new InvalidOperationException(CorruptMessage, error);
            }
        }
        static List<(string Id, bool Deprecated)> Read(string collection, string idProperty, string expectedHash) {
            byte[] data;
            using(var stream = typeof(SpdxLicenseRegistry).Assembly.GetManifestResourceStream($"Spdx.Expressions.Data.{collection}.json")
                ?? throw new InvalidOperationException(CorruptMessage))
            using(var bytes = new MemoryStream()) {
                stream.CopyTo(bytes);
                data = bytes.ToArray();
            }
            if(Convert.ToHexStringLower(SHA256.HashData(data)) != expectedHash)
                throw new InvalidOperationException(CorruptMessage);
            using(var json = JsonDocument.Parse(data)) {
                var root = json.RootElement;
                if(root.GetProperty("licenseListVersion").GetString() != Version)
                    throw new InvalidOperationException(CorruptMessage);
                var entries = new List<(string, bool)>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(var entry in root.GetProperty(collection).EnumerateArray()) {
                    string? id = entry.GetProperty(idProperty).GetString();
                    bool deprecated = entry.GetProperty("isDeprecatedLicenseId").GetBoolean();
                    if(string.IsNullOrEmpty(id) || !seen.Add(id) || id.StartsWith("LicenseRef-", StringComparison.Ordinal) ||
                        id.StartsWith("DocumentRef-", StringComparison.Ordinal))
                        throw new InvalidOperationException(CorruptMessage);
                    // Six upstream deprecated license entries include a trailing plus in their canonical ID.
                    var candidate = collection == "licenses" && id.EndsWith('+') ? id.AsSpan(0, id.Length - 1) : id.AsSpan();
                    if(candidate.IsEmpty)
                        throw new InvalidOperationException(CorruptMessage);
                    foreach(char character in candidate) {
                        if(!SpdxExpressionParser.IsIdentifierCharacter(character))
                            throw new InvalidOperationException(CorruptMessage);
                    }
                    entries.Add((id, deprecated));
                }
                return entries;
            }
        }
    }
}
