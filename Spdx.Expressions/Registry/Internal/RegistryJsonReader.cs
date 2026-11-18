using System;
using System.Buffers;
using System.Text;
using System.Threading;

namespace Spdx.Expressions.Internal {
    sealed partial class RegistryLoader {
        static void ValidateUtf8(ReadOnlySpan<byte> bytes, SpdxRegistryInput input, CancellationToken cancellationToken) {
            // Report the attempted scalar's start for every failure under RFC 3629 §4.
            // https://www.rfc-editor.org/rfc/rfc3629#section-4
            int lastCheck = 0;
            for(int offset = 0; offset < bytes.Length;) {
                PollRawScan(offset, ref lastCheck, cancellationToken);
                if(bytes[offset] < 0x80) {
                    offset++;
                    continue;
                }
                if(Rune.DecodeFromUtf8(bytes[offset..], out _, out int consumed) != OperationStatus.Done)
                    throw Invalid("INVALID_UTF8", InvalidUtf8Message, input, offset);
                offset += consumed;
            }
        }
        static void PollRawScan(int offset, ref int lastCheck, CancellationToken cancellationToken) {
            // Leave room for bounded lookahead at a scalar or escape boundary.
            if(offset - lastCheck >= ByteChunkSize / 2) {
                cancellationToken.ThrowIfCancellationRequested();
                lastCheck = offset;
            }
        }
        enum TokenKind { Object, Array, End, String, Number, True, False, Null }
        readonly record struct Token(TokenKind Kind, int Offset, int RawLength, int TextLength = 0, int UnpairedEscape = -1);
        // Recognize RFC 8259 tokens with explicit scalar boundaries and original byte coordinates.
        // https://www.rfc-editor.org/rfc/rfc8259#section-2
        ref struct JsonReader {
            readonly ReadOnlyMemory<byte> data;
            readonly SpdxRegistryInput input;
            readonly CancellationToken cancellationToken;
            readonly ReadOnlySpan<byte> bytes;
            int position;
            int lastCheck;
            internal JsonReader(ReadOnlyMemory<byte> data, SpdxRegistryInput input, CancellationToken cancellationToken) {
                this.data = data;
                this.input = input;
                this.cancellationToken = cancellationToken;
                bytes = data.Span;
            }
            int Current => position < bytes.Length ? bytes[position] : -1;
            internal void SkipBom() {
                ReadOnlySpan<byte> bom = "\uFEFF"u8;
                if(bytes.StartsWith(bom))
                    position += bom.Length;
            }
            internal Token ReadValue() {
                cancellationToken.ThrowIfCancellationRequested();
                SkipWhitespace();
                int start = position;
                switch(Current) {
                    case '{':
                        position++;
                        return new(TokenKind.Object, start, 1);
                    case '[':
                        position++;
                        return new(TokenKind.Array, start, 1);
                    case '"':
                        return ReadString();
                    case 't':
                        return ReadLiteral("true"u8, TokenKind.True);
                    case 'f':
                        return ReadLiteral("false"u8, TokenKind.False);
                    case 'n':
                        return ReadLiteral("null"u8, TokenKind.Null);
                    case '-' or >= '0' and <= '9':
                        return ReadNumber();
                    default:
                        throw SyntaxError();
                }
            }
            internal Token ReadProperty() {
                cancellationToken.ThrowIfCancellationRequested();
                SkipWhitespace();
                if(Current != '"')
                    throw SyntaxError();
                var name = ReadString();
                // Property recognition requires the colon. Only the quoted name counts toward raw token size.
                SkipWhitespace();
                if(Current != ':')
                    throw SyntaxError();
                position++;
                return name;
            }
            internal bool TryReadEnd(byte closer, bool first, out Token end) {
                cancellationToken.ThrowIfCancellationRequested();
                SkipWhitespace();
                if(Current == closer) {
                    end = new(TokenKind.End, position++, 1);
                    return true;
                }
                if(!first) {
                    if(Current != ',')
                        throw SyntaxError();
                    position++;
                }
                end = default;
                return false;
            }
            internal void RequireEndOfInput() {
                cancellationToken.ThrowIfCancellationRequested();
                SkipWhitespace();
                if(Current != -1)
                    throw Invalid("TRAILING_DATA", TrailingDataMessage, input, position);
            }
            void SkipWhitespace() {
                while(IsWhitespace(Current)) {
                    PollRawScan(position, ref lastCheck, cancellationToken);
                    position++;
                }
            }
            static bool IsWhitespace(int value) {
                return value is ' ' or '\t' or '\r' or '\n';
            }
            Token ReadLiteral(ReadOnlySpan<byte> spelling, TokenKind kind) {
                int start = position;
                foreach(byte expected in spelling) {
                    if(Current != expected)
                        throw SyntaxError();
                    position++;
                }
                RequireScalarBoundary();
                return new(kind, start, position - start);
            }
            Token ReadNumber() {
                int start = position;
                if(Current == '-')
                    position++;
                if(Current == '0') {
                    position++;
                    if(IsDigit(Current))
                        throw SyntaxError();
                }
                else ReadDigits();
                if(Current == '.') {
                    position++;
                    ReadDigits();
                }
                if(Current is 'e' or 'E') {
                    position++;
                    if(Current is '+' or '-')
                        position++;
                    ReadDigits();
                }
                RequireScalarBoundary();
                return new(TokenKind.Number, start, position - start);
            }
            void ReadDigits() {
                if(!IsDigit(Current))
                    throw SyntaxError();
                do {
                    PollRawScan(position, ref lastCheck, cancellationToken);
                    position++;
                } while(IsDigit(Current));
            }
            static bool IsDigit(int value) {
                return value is >= '0' and <= '9';
            }
            void RequireScalarBoundary() {
                if(Current is not (-1 or ',' or ']' or '}') && !IsWhitespace(Current))
                    throw SyntaxError();
            }
            Token ReadString() {
                int start = position++;
                int length = 0;
                int pendingHigh = -1;
                int firstUnpaired = -1;
                while(Current != '"') {
                    int characterOffset = position;
                    int character = ReadStringCharacter();
                    length += character <= char.MaxValue ? 1 : 2;
                    if(firstUnpaired >= 0)
                        continue;
                    // A syntactically valid escape is counted even when its surrogate does not pair.
                    bool low = character is >= 0xDC00 and <= 0xDFFF;
                    if(pendingHigh >= 0 && !low)
                        firstUnpaired = pendingHigh;
                    else if(pendingHigh < 0 && low)
                        firstUnpaired = characterOffset;
                    pendingHigh = character is >= 0xD800 and <= 0xDBFF ? characterOffset : -1;
                }
                position++;
                int unpaired = firstUnpaired >= 0 ? firstUnpaired : pendingHigh;
                return new(TokenKind.String, start, position - start, length, unpaired);
            }
            int ReadStringCharacter() {
                PollRawScan(position, ref lastCheck, cancellationToken);
                int value = Current;
                if(value < 0x20)
                    throw SyntaxError();
                if(value >= 0x80) {
                    // UTF-8 preflight has already validated the owned input bytes.
                    Rune.DecodeFromUtf8(bytes[position..], out var scalar, out int consumed);
                    position += consumed;
                    return scalar.Value;
                }
                position++;
                if(value != '\\')
                    return value;
                int escape = Current;
                if(escape == 'u') {
                    position++;
                    return ReadHexEscape();
                }
                int decoded = escape switch {
                    '"' or '\\' or '/' => escape,
                    'b' => '\b',
                    'f' => '\f',
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    _ => throw SyntaxError()
                };
                position++;
                return decoded;
            }
            int ReadHexEscape() {
                const int HexEscapeDigits = 4;
                int value = 0;
                for(int index = 0; index < HexEscapeDigits; index++) {
                    int digit = Current switch {
                        >= '0' and <= '9' => Current - '0',
                        >= 'a' and <= 'f' => Current - 'a' + 10,
                        >= 'A' and <= 'F' => Current - 'A' + 10,
                        _ => throw SyntaxError()
                    };
                    value = (value << 4) | digit;
                    position++;
                }
                return value;
            }
            internal string Decode(Token token) {
                cancellationToken.ThrowIfCancellationRequested();
                var contents = data.Slice(token.Offset + 1, token.RawLength - 2);
                // Materialize only checked strings needed for property equality or the resulting registry.
                return string.Create(token.TextLength, (contents, input, cancellationToken), static (destination, state) => {
                    var scanner = new JsonReader(state.contents, state.input, state.cancellationToken);
                    int offset = 0;
                    while(offset < destination.Length) {
                        int value = scanner.ReadStringCharacter();
                        if(value <= char.MaxValue)
                            destination[offset++] = (char)value;
                        else
                            offset += new Rune(value).EncodeToUtf16(destination[offset..]);
                    }
                });
            }
            InputFailure SyntaxError() {
                return Invalid("INVALID_JSON", InvalidJsonMessage, input, position);
            }
        }
    }
}
