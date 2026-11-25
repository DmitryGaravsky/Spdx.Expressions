using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;

namespace Spdx.Compliance {
    static class Input {
        internal static ReadOnlyMemory<byte> Read(Stream stream, int limit, CancellationToken token) {
            ArgumentNullException.ThrowIfNull(stream);
            token.ThrowIfCancellationRequested();
            using(var buffer = new MemoryStream()) {
                Span<byte> chunk = stackalloc byte[Math.Min(8192, limit)];
                while(buffer.Length < limit) {
                    token.ThrowIfCancellationRequested();
                    int read = stream.Read(chunk[..Math.Min(chunk.Length, limit - (int)buffer.Length)]);
                    token.ThrowIfCancellationRequested();
                    if(read == 0)
                        return buffer.GetBuffer().AsMemory(0, (int)buffer.Length);
                    buffer.Write(chunk[..read]);
                }
                token.ThrowIfCancellationRequested();
                int extra = stream.ReadByte();
                token.ThrowIfCancellationRequested();
                if(extra >= 0)
                    throw new InputLimitException();
                return buffer.GetBuffer().AsMemory(0, (int)buffer.Length);
            }
        }
        internal static string Digest(ReadOnlySpan<byte> bytes, CancellationToken token = default) {
            using(var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256)) {
                const int blockSize = 64 * 1024;
                while(!bytes.IsEmpty) {
                    token.ThrowIfCancellationRequested();
                    int size = Math.Min(blockSize, bytes.Length);
                    hash.AppendData(bytes[..size]);
                    bytes = bytes[size..];
                }
                token.ThrowIfCancellationRequested();
                return Convert.ToHexStringLower(hash.GetHashAndReset());
            }
        }
        internal static bool IsDigest(string? value) {
            return value is { Length: 64 } && value.AsSpan().IndexOfAnyExcept("0123456789abcdefABCDEF") < 0;
        }
        internal static bool IsRelativeFile(string? value) {
            if(string.IsNullOrWhiteSpace(value) || value.AsSpan().IndexOfAny(':', '\\', '\0') >= 0)
                return false;
            foreach(string part in value.Split('/')) {
                if(string.IsNullOrWhiteSpace(part) || part is "." or "..")
                    return false;
            }
            return true;
        }
        internal static JsonDocument Parse(ReadOnlyMemory<byte> bytes, int maxDepth, CancellationToken token) {
            token.ThrowIfCancellationRequested();
            // A UTF-8 BOM is outside the JSON token stream but remains part of the pinned bytes.
            if(bytes.Span.StartsWith("\uFEFF"u8))
                bytes = bytes[3..];
            var options = new JsonReaderOptions { MaxDepth = maxDepth + 1 };
            var reader = new Utf8JsonReader(bytes.Span, options);
            Stack<HashSet<string>> objects = new();
            while(reader.Read()) {
                token.ThrowIfCancellationRequested();
                if(reader.CurrentDepth >= maxDepth && reader.TokenType is (JsonTokenType.StartObject or JsonTokenType.StartArray))
                    throw new InputDepthException();
                switch(reader.TokenType) {
                    case JsonTokenType.StartObject:
                        objects.Push(new(StringComparer.Ordinal));
                        break;
                    case JsonTokenType.EndObject:
                        objects.Pop();
                        break;
                    case JsonTokenType.PropertyName:
                        string name = ReadString(ref reader);
                        if(!objects.Peek().Add(name))
                            throw new JsonException($"Duplicate property '{name}' at byte {reader.TokenStartIndex}.");
                        break;
                    case JsonTokenType.String:
                        // Validate Unicode even in fields outside the adapter's projection.
                        ReadString(ref reader);
                        break;
                }
            }
            var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = maxDepth });
            if(token.IsCancellationRequested) {
                document.Dispose();
                token.ThrowIfCancellationRequested();
            }
            return document;
        }
        static string ReadString(ref Utf8JsonReader reader) {
            try {
                return reader.GetString()!;
            }
            catch(InvalidOperationException exception) {
                // The reader is on a property or string token; GetString also uses this exception for invalid UTF-8 and surrogates.
                const string message = "A JSON string contains invalid Unicode.";
                throw new JsonException(message, exception);
            }
        }
        internal static JsonElement Get(this JsonElement node, string key) {
            return node.ValueKind == JsonValueKind.Object && node.TryGetProperty(key, out var value) ? value : default;
        }
        internal static string? Text(this JsonElement node, string key) {
            var field = node.Get(key);
            return field.ValueKind == JsonValueKind.String ? field.GetString() : null;
        }
    }
    sealed class InputLimitException : Exception { }
    sealed class InputDepthException : Exception { }
}
