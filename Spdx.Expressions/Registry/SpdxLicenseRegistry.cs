using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

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
        /// <summary>Gets the upstream deprecation flag.</summary>
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
        /// <summary>Gets the upstream isDeprecatedLicenseId flag for this exception.</summary>
        public bool IsDeprecated { get; }
    }
    /// <summary>An immutable, offline catalogue. The MVP exposes only the pinned embedded snapshot.</summary>
    public sealed class SpdxLicenseRegistry {
        const string Version = "3.29.0";
        const string LicenseHash = "47d1cc681abe31166b342b6cc4aab13a6ba8ea5c48794697fe3bf8b1dbaf509a";
        const string ExceptionHash = "59ee7c150de521d635cbf9367b8cf543b8da6781ea3ac1c073d305524f84ada2";
        const string CorruptMessage = "The embedded SPDX catalogue is corrupt or inconsistent with its pinned identity.";
        static readonly Lazy<SpdxLicenseRegistry> Snapshot = new(Load);
        readonly FrozenDictionary<string, SpdxLicenseInfo> licenses;
        readonly FrozenDictionary<string, SpdxExceptionInfo> exceptions;

        SpdxLicenseRegistry(List<SpdxLicenseInfo> licenseEntries, List<SpdxExceptionInfo> exceptionEntries) {
            var orderedLicenses = licenseEntries.OrderBy(x => x.Identifier, StringComparer.Ordinal).ToArray();
            var orderedExceptions = exceptionEntries.OrderBy(x => x.Identifier, StringComparer.Ordinal).ToArray();
            Licenses = Array.AsReadOnly(orderedLicenses);
            Exceptions = Array.AsReadOnly(orderedExceptions);
            licenses = Licenses.ToFrozenDictionary(x => x.Identifier, StringComparer.OrdinalIgnoreCase);
            exceptions = Exceptions.ToFrozenDictionary(x => x.Identifier, StringComparer.OrdinalIgnoreCase);
            Identity = new(Version, LicenseHash, ExceptionHash);
        }
        /// <summary>Gets the thread-safe embedded v3.29.0 snapshot without network access.</summary>
        /// <exception cref="InvalidOperationException">Embedded resources are corrupt (a packaging defect).</exception>
        public static SpdxLicenseRegistry Default => Snapshot.Value;
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

        static SpdxLicenseRegistry Load() {
            try {
                var licenseEntries = Read("licenses", "licenseId", LicenseHash)
                    .Select(x => new SpdxLicenseInfo(x.Id, x.Deprecated)).ToList();
                var exceptionEntries = Read("exceptions", "licenseExceptionId", ExceptionHash)
                    .Select(x => new SpdxExceptionInfo(x.Id, x.Deprecated)).ToList();
                if(licenseEntries.Count == 0 || exceptionEntries.Count == 0 ||
                    licenseEntries.Select(x => x.Identifier).Intersect(exceptionEntries.Select(x => x.Identifier), StringComparer.OrdinalIgnoreCase).Any())
                    throw new InvalidOperationException(CorruptMessage);
                return new(licenseEntries, exceptionEntries);
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
