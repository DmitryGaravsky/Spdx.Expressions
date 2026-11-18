using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace Spdx.Expressions.Internal {
    sealed partial class RegistryLoader {
        const int ByteChunkSize = 16 * 1024;
        const int TextChunkSize = 8 * 1024;
        const string VersionProperty = "licenseListVersion";
        const string InvalidUtf8Message = "The input contains an invalid UTF-8 sequence.";
        const string InvalidJsonMessage = "The input contains invalid JSON.";
        const string InvalidUnicodeMessage = "A JSON string contains an unpaired surrogate escape.";
        const string TrailingDataMessage = "The complete catalogue object is followed by non-whitespace data.";
        const string RootTypeMessage = "The catalogue root must be a JSON object.";
        const string MissingPropertyMessage = "A required catalogue property is absent.";
        const string PropertyTypeMessage = "A required catalogue property has the wrong JSON value type.";
        const string DuplicatePropertyMessage = "A JSON object contains a duplicate property name.";
        const string UnexpectedPropertyMessage = "This property belongs to the other catalogue input.";
        const string InvalidVersionMessage = "The declared list version must be non-empty, without edge whitespace or control characters.";
        const string VersionMismatchMessage = "The declared list versions of the two catalogue inputs differ.";
        const string EntryTypeMessage = "A catalogue entry must be a JSON object.";
        const string EmptyCollectionMessage = "The required catalogue collection must contain at least one entry.";
        const string InvalidIdentifierMessage = "The catalogue identifier is invalid for its namespace or deprecation flag.";
        const string DuplicateIdentifierMessage = "The catalogue identifier duplicates an earlier identifier in this input.";
        const string NamespaceCollisionMessage = "The exception identifier is already present in the license namespace.";
        const string LimitMessage = "The catalogue exceeds the configured resource limit.";
        readonly SpdxRegistryLoadOptions options;
        readonly CancellationToken cancellationToken;
        long tokens;
        long stringLength;
        RegistryLoader(SpdxRegistryLoadOptions? options, CancellationToken cancellationToken) {
            options ??= new SpdxRegistryLoadOptions();
            options.Check();
            this.options = options;
            this.cancellationToken = cancellationToken;
        }
        internal static SpdxRegistryLoadResult Load(ReadOnlyMemory<byte> licenses, ReadOnlyMemory<byte> exceptions,
            SpdxRegistryLoadOptions? options, CancellationToken cancellationToken) {
            var loader = new RegistryLoader(options, cancellationToken);
            return loader.LoadPair(licenses, exceptions, loader.Acquire);
        }
        internal static SpdxRegistryLoadResult Load(Stream licenses, Stream exceptions,
            SpdxRegistryLoadOptions? options, CancellationToken cancellationToken) {
            var loader = new RegistryLoader(options, cancellationToken);
            return loader.LoadPair(licenses, exceptions, loader.Acquire);
        }
        SpdxRegistryLoadResult LoadPair<T>(T licenses, T exceptions, Func<T, SpdxRegistryInput, int, ReadOnlyMemory<byte>> acquire) {
            SpdxRegistryLoadResult result;
            try {
                var licenseBytes = acquire(licenses, SpdxRegistryInput.Licenses, 0);
                var licenseDocument = Parse(licenseBytes, SpdxRegistryInput.Licenses);
                var exceptionBytes = acquire(exceptions, SpdxRegistryInput.Exceptions, licenseBytes.Length);
                var exceptionDocument = Parse(exceptionBytes, SpdxRegistryInput.Exceptions);
                var registry = Complete(licenseDocument, exceptionDocument, licenseBytes.Span, exceptionBytes.Span);
                result = new(registry);
            }
            catch(InputFailure failure) {
                result = new(failure.Diagnostic);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        (int Bytes, string LimitName) GetByteAllowance(int earlierBytes) {
            int remaining = options.MaxTotalInputBytes - earlierBytes;
            if(options.MaxInputBytes <= remaining)
                return (options.MaxInputBytes, nameof(options.MaxInputBytes));
            return (remaining, nameof(options.MaxTotalInputBytes));
        }
        ReadOnlyMemory<byte> Acquire(ReadOnlyMemory<byte> source, SpdxRegistryInput input, int earlierBytes) {
            cancellationToken.ThrowIfCancellationRequested();
            var (allowance, limitName) = GetByteAllowance(earlierBytes);
            if(source.Length > allowance)
                throw Limit(input, allowance, limitName);
            ReadOnlySpan<byte> sourceBytes = source.Span;
            cancellationToken.ThrowIfCancellationRequested();
            byte[] copy = GC.AllocateUninitializedArray<byte>(sourceBytes.Length);
            CopyChunks(sourceBytes, copy);
            return copy;
        }
        ReadOnlyMemory<byte> Acquire(Stream source, SpdxRegistryInput input, int earlierBytes) {
            cancellationToken.ThrowIfCancellationRequested();
            var (allowance, limitName) = GetByteAllowance(earlierBytes);
            // Only the scratch buffer is passed to the stream. Accepted bytes stay in owned storage.
            byte[] scratch = new byte[(int)Math.Min(ByteChunkSize, (long)allowance + 1)];
            byte[] bytes = Array.Empty<byte>();
            int length = 0;
            while(true) {
                int remaining = allowance - length;
                int request = (int)Math.Min(scratch.Length, (long)remaining + 1);
                cancellationToken.ThrowIfCancellationRequested();
                int count = source.Read(scratch, 0, request);
                cancellationToken.ThrowIfCancellationRequested();
                if(count == 0)
                    return bytes.AsMemory(0, length);
                if(count > remaining)
                    throw Limit(input, allowance, limitName);
                int required = length + count;
                if(required > bytes.Length) {
                    long growthLimit = Math.Min(allowance, Array.MaxLength);
                    int capacity = (int)Math.Max(required, Math.Min(growthLimit, 2L * bytes.Length));
                    byte[] grown = GC.AllocateUninitializedArray<byte>(capacity);
                    CopyChunks(bytes.AsSpan(0, length), grown);
                    bytes = grown;
                }
                CopyChunks(scratch.AsSpan(0, count), bytes.AsSpan(length, count));
                length = required;
            }
        }
        void CopyChunks(ReadOnlySpan<byte> source, Span<byte> destination) {
            for(int offset = 0; offset < source.Length;) {
                cancellationToken.ThrowIfCancellationRequested();
                int count = Math.Min(ByteChunkSize, source.Length - offset);
                source.Slice(offset, count).CopyTo(destination.Slice(offset, count));
                offset += count;
            }
        }
        ParsedDocument Parse(ReadOnlyMemory<byte> bytes, SpdxRegistryInput input) {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateUtf8(bytes.Span, input, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var parser = new DocumentParser(this, bytes, input);
            return parser.Parse();
        }
        SpdxLicenseRegistry Complete(ParsedDocument licenses, ParsedDocument exceptions,
            ReadOnlySpan<byte> licenseBytes, ReadOnlySpan<byte> exceptionBytes) {
            cancellationToken.ThrowIfCancellationRequested();
            if(!TextEquals(licenses.Version, exceptions.Version, StringComparison.Ordinal, cancellationToken))
                throw Invalid("VERSION_MISMATCH", VersionMismatchMessage, SpdxRegistryInput.Exceptions,
                    exceptions.VersionOffset, property: VersionProperty);
            for(int index = 0; index < exceptions.Entries.Count; index++) {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = exceptions.Entries[index];
                if(licenses.Identifiers.Contains(entry.Identifier))
                    throw Invalid("NAMESPACE_COLLISION", NamespaceCollisionMessage, SpdxRegistryInput.Exceptions,
                        entry.Offset, index, ExceptionIdProperty);
            }
            cancellationToken.ThrowIfCancellationRequested();
            string licenseHash = Hash(licenseBytes);
            string exceptionHash = Hash(exceptionBytes);
            var identity = new SpdxRegistryIdentity(licenses.Version, licenseHash, exceptionHash);
            cancellationToken.ThrowIfCancellationRequested();
            var licenseEntries = new List<SpdxLicenseInfo>();
            foreach(var entry in licenses.Entries) {
                cancellationToken.ThrowIfCancellationRequested();
                licenseEntries.Add(new(entry.Identifier, entry.Deprecated));
            }
            var exceptionEntries = new List<SpdxExceptionInfo>();
            foreach(var entry in exceptions.Entries) {
                cancellationToken.ThrowIfCancellationRequested();
                exceptionEntries.Add(new(entry.Identifier, entry.Deprecated));
            }
            return new SpdxLicenseRegistry(identity, licenseEntries, exceptionEntries, cancellationToken);
        }
        string Hash(ReadOnlySpan<byte> bytes) {
            using(var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256)) {
                while(!bytes.IsEmpty) {
                    cancellationToken.ThrowIfCancellationRequested();
                    int count = Math.Min(ByteChunkSize, bytes.Length);
                    hash.AppendData(bytes[..count]);
                    bytes = bytes[count..];
                }
                cancellationToken.ThrowIfCancellationRequested();
                return Convert.ToHexStringLower(hash.GetHashAndReset());
            }
        }
        static bool TextEquals(string left, string right, StringComparison comparison, CancellationToken cancellationToken) {
            if(left.Length != right.Length)
                return false;
            for(int offset = 0; offset < left.Length;) {
                cancellationToken.ThrowIfCancellationRequested();
                int count = Math.Min(TextChunkSize, left.Length - offset);
                if(!left.AsSpan(offset, count).Equals(right.AsSpan(offset, count), comparison))
                    return false;
                offset += count;
            }
            return true;
        }
        // Ignore-case comparisons here operate only on lexically validated ASCII IDs.
        sealed class NameComparer : IEqualityComparer<string> {
            readonly StringComparison comparison;
            readonly CancellationToken cancellationToken;
            internal NameComparer(StringComparison comparison, CancellationToken cancellationToken) {
                this.comparison = comparison;
                this.cancellationToken = cancellationToken;
            }
            public bool Equals(string? left, string? right) {
                return ReferenceEquals(left, right) ||
                    left is not null && right is not null && TextEquals(left, right, comparison, cancellationToken);
            }
            public int GetHashCode(string name) {
                var hash = new HashCode();
                for(int offset = 0; offset < name.Length;) {
                    cancellationToken.ThrowIfCancellationRequested();
                    int count = Math.Min(TextChunkSize, name.Length - offset);
                    hash.Add(string.GetHashCode(name.AsSpan(offset, count), comparison));
                    offset += count;
                }
                return hash.ToHashCode();
            }
        }
        static InputFailure Invalid(string suffix, string message, SpdxRegistryInput input,
            int offset, int? entry = null, string? property = null) {
            return new(new("SPDX_REGISTRY_" + suffix, input, offset, entry, property, null, message));
        }
        static InputFailure Limit(SpdxRegistryInput input, int offset, string name, int? entry = null, string? property = null) {
            return new(new("SPDX_REGISTRY_LIMIT_EXCEEDED", input, offset, entry, property, name, LimitMessage));
        }
        // Only deliberately classified input failures are converted to a result.
        sealed class InputFailure : Exception {
            internal InputFailure(SpdxRegistryDiagnostic diagnostic) : base(diagnostic.Message) {
                Diagnostic = diagnostic;
            }
            internal SpdxRegistryDiagnostic Diagnostic { get; }
        }
        sealed record ParsedDocument(string Version, int VersionOffset, List<Entry> Entries, HashSet<string> Identifiers);
        readonly record struct Entry(string Identifier, bool Deprecated, int Offset);
    }
}
