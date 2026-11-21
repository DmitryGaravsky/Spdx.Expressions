using System;
using System.Collections.Generic;

namespace Spdx.Expressions.Internal {
    sealed partial class RegistryLoader {
        const string LicensesProperty = "licenses";
        const string ExceptionsProperty = "exceptions";
        const string LicenseIdProperty = "licenseId";
        const string ExceptionIdProperty = "licenseExceptionId";
        const string DeprecatedProperty = "isDeprecatedLicenseId";
        enum ObjectRole { Root, Entry, Ignored }
        enum Field { Version, Collection, Identifier, Deprecated, Name, Reference, SeeAlso, Ignored }
        enum ArrayRole { Collection, SeeAlso, Ignored }
        readonly record struct Location(int Depth, int? Entry, string? Property);
        ref struct DocumentParser {
            readonly RegistryLoader loader;
            readonly SpdxRegistryInput input;
            readonly bool isLicense;
            readonly string collectionProperty;
            readonly string identifierProperty;
            readonly NameComparer propertyComparer;
            readonly HashSet<string> identifiers;
            readonly List<Entry> entries;
            JsonReader reader;
            string? version;
            int versionOffset;
            internal DocumentParser(RegistryLoader loader, ReadOnlyMemory<byte> bytes, SpdxRegistryInput input) {
                this.loader = loader;
                this.input = input;
                isLicense = input == SpdxRegistryInput.Licenses;
                collectionProperty = isLicense ? LicensesProperty : ExceptionsProperty;
                identifierProperty = isLicense ? LicenseIdProperty : ExceptionIdProperty;
                propertyComparer = new(StringComparison.Ordinal, loader.cancellationToken);
                identifiers = new(new NameComparer(StringComparison.OrdinalIgnoreCase, loader.cancellationToken));
                entries = new();
                reader = new(bytes, input, loader.cancellationToken);
            }
            internal ParsedDocument Parse() {
                reader.SkipBom();
                var location = new Location(1, null, null);
                var root = reader.ReadValue();
                CheckToken(root, location);
                if(root.Kind != TokenKind.Object)
                    throw Invalid("ROOT_TYPE", RootTypeMessage, input, root.Offset);
                ReadObject(ObjectRole.Root, location);
                reader.RequireEndOfInput();
                return new(version!, versionOffset, entries, identifiers);
            }
            void ReadObject(ObjectRole role, Location location) {
                var names = new HashSet<string>(propertyComparer);
                string? identifier = null;
                int identifierOffset = 0;
                bool? deprecated = null;
                string? displayName = null;
                string? reference = null;
                IReadOnlyList<string>? seeAlso = null;
                int propertyIndex = 0;
                Token end;
                while(!reader.TryReadEnd((byte)'}', propertyIndex == 0, out end)) {
                    var nameToken = reader.ReadProperty();
                    CheckToken(nameToken, location with { Property = null }, propertyIndex: propertyIndex);
                    string name = reader.Decode(nameToken);
                    if(!names.Add(name))
                        throw Invalid("DUPLICATE_PROPERTY", DuplicatePropertyMessage, input, nameToken.Offset, location.Entry, name);
                    var field = ClassifyProperty(role, name, nameToken.Offset, location.Entry);
                    var valueLocation = new Location(location.Depth + 1, location.Entry, name);
                    var value = reader.ReadValue();
                    CheckToken(value, valueLocation, identifier: field == Field.Identifier);
                    switch(field) {
                        case Field.Version:
                            RequireType(value, TokenKind.String, valueLocation);
                            version = ReadVersion(value);
                            versionOffset = value.Offset;
                            break;
                        case Field.Collection:
                            RequireType(value, TokenKind.Array, valueLocation);
                            ReadArray(valueLocation, ArrayRole.Collection);
                            break;
                        case Field.Identifier:
                            RequireType(value, TokenKind.String, valueLocation);
                            identifier = ReadIdentifier(value, location.Entry);
                            identifierOffset = value.Offset;
                            break;
                        case Field.Deprecated:
                            if(value.Kind is not (TokenKind.True or TokenKind.False))
                                throw Invalid("PROPERTY_TYPE", PropertyTypeMessage, input, value.Offset, location.Entry, name);
                            deprecated = value.Kind == TokenKind.True;
                            break;
                        case Field.Name when value.Kind == TokenKind.String:
                            displayName = reader.Decode(value);
                            break;
                        case Field.Reference when value.Kind == TokenKind.String:
                            reference = reader.Decode(value);
                            break;
                        case Field.SeeAlso when value.Kind == TokenKind.Array:
                            seeAlso = ReadArray(valueLocation, ArrayRole.SeeAlso);
                            break;
                        default:
                            ReadIgnoredValue(value, valueLocation);
                            break;
                    }
                    propertyIndex++;
                }
                CheckToken(end, location);
                if(role == ObjectRole.Root) {
                    if(version is null)
                        throw Invalid("MISSING_PROPERTY", MissingPropertyMessage, input, end.Offset, property: VersionProperty);
                    if(entries.Count == 0)
                        throw Invalid("MISSING_PROPERTY", MissingPropertyMessage, input, end.Offset, property: collectionProperty);
                }
                else if(role == ObjectRole.Entry)
                    FinishEntry(identifier, identifierOffset, deprecated, end.Offset, location.Entry, displayName, reference, seeAlso);
            }
            IReadOnlyList<string>? ReadArray(Location location, ArrayRole role) {
                bool isCollection = role == ArrayRole.Collection;
                List<string>? links = role == ArrayRole.SeeAlso ? new() : null;
                int index = 0;
                Token end;
                while(!reader.TryReadEnd((byte)']', index == 0, out end)) {
                    var element = new Location(location.Depth + 1, isCollection ? index : location.Entry, location.Property);
                    var value = reader.ReadValue();
                    CheckToken(value, element, arrayIndex: index, isCollection: isCollection);
                    if(isCollection) {
                        if(value.Kind != TokenKind.Object)
                            throw Invalid("ENTRY_TYPE", EntryTypeMessage, input, value.Offset, index, collectionProperty);
                        ReadObject(ObjectRole.Entry, element);
                    }
                    else if(links is not null && value.Kind == TokenKind.String)
                        links.Add(reader.Decode(value));
                    else {
                        // One non-string omits the entire display list. Its contents still undergo all JSON checks.
                        links = null;
                        ReadIgnoredValue(value, element);
                    }
                    index++;
                }
                CheckToken(end, location);
                if(isCollection && index == 0)
                    throw Invalid("EMPTY_COLLECTION", EmptyCollectionMessage, input, end.Offset, property: collectionProperty);
                if(links is null || links.Count == 0)
                    return null;
                return [.. links];
            }
            void ReadIgnoredValue(Token value, Location location) {
                if(value.Kind == TokenKind.Object)
                    ReadObject(ObjectRole.Ignored, location);
                else if(value.Kind == TokenKind.Array)
                    ReadArray(location, ArrayRole.Ignored);
            }
            Field ClassifyProperty(ObjectRole role, string name, int offset, int? index) {
                if(role == ObjectRole.Root) {
                    if(name == VersionProperty)
                        return Field.Version;
                    if(name == collectionProperty)
                        return Field.Collection;
                    if(name == (isLicense ? ExceptionsProperty : LicensesProperty))
                        throw Invalid("UNEXPECTED_PROPERTY", UnexpectedPropertyMessage, input, offset, property: name);
                }
                else if(role == ObjectRole.Entry) {
                    if(name == identifierProperty)
                        return Field.Identifier;
                    if(name == DeprecatedProperty)
                        return Field.Deprecated;
                    if(name == (isLicense ? ExceptionIdProperty : LicenseIdProperty))
                        throw Invalid("UNEXPECTED_PROPERTY", UnexpectedPropertyMessage, input, offset, index, name);
                    return name switch {
                        "name" => Field.Name,
                        "reference" => Field.Reference,
                        "seeAlso" => Field.SeeAlso,
                        _ => Field.Ignored
                    };
                }
                return Field.Ignored;
            }
            void RequireType(Token token, TokenKind expected, Location location) {
                if(token.Kind != expected)
                    throw Invalid("PROPERTY_TYPE", PropertyTypeMessage, input, token.Offset, location.Entry, location.Property);
            }
            string ReadVersion(Token token) {
                string value = reader.Decode(token);
                if(value.Length == 0 || char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1]))
                    throw Invalid("INVALID_VERSION", InvalidVersionMessage, input, token.Offset, property: VersionProperty);
                for(int index = 0; index < value.Length; index++) {
                    if(index % TextChunkSize == 0)
                        loader.cancellationToken.ThrowIfCancellationRequested();
                    if(char.IsControl(value[index]))
                        throw Invalid("INVALID_VERSION", InvalidVersionMessage, input, token.Offset, property: VersionProperty);
                }
                return value;
            }
            string ReadIdentifier(Token token, int? index) {
                string identifier = reader.Decode(token);
                var candidate = identifier.AsSpan();
                if(isLicense && candidate.EndsWith("+", StringComparison.Ordinal))
                    candidate = candidate[..^1];
                if(candidate.IsEmpty || IsReserved(candidate))
                    throw InvalidIdentifier(token.Offset, index);
                for(int offset = 0; offset < candidate.Length; offset++) {
                    if(offset % TextChunkSize == 0)
                        loader.cancellationToken.ThrowIfCancellationRequested();
                    if(!SpdxExpressionParser.IsIdentifierCharacter(candidate[offset]))
                        throw InvalidIdentifier(token.Offset, index);
                }
                return identifier;
            }
            static bool IsReserved(ReadOnlySpan<char> candidate) {
                const StringComparison Comparison = StringComparison.OrdinalIgnoreCase;
                return candidate.Equals("AND", Comparison) || candidate.Equals("OR", Comparison) ||
                    candidate.Equals("WITH", Comparison) || candidate.Equals("NONE", Comparison) ||
                    candidate.Equals("NOASSERTION", Comparison) || candidate.StartsWith("LicenseRef-", Comparison) ||
                    candidate.StartsWith("DocumentRef-", Comparison);
            }
            InputFailure InvalidIdentifier(int offset, int? index) {
                return Invalid("INVALID_IDENTIFIER", InvalidIdentifierMessage, input, offset, index, identifierProperty);
            }
            void FinishEntry(string? identifier, int identifierOffset, bool? deprecated, int closingOffset, int? index,
                string? name, string? reference, IReadOnlyList<string>? seeAlso) {
                if(identifier is null)
                    throw Invalid("MISSING_PROPERTY", MissingPropertyMessage, input, closingOffset, index, identifierProperty);
                if(!deprecated.HasValue)
                    throw Invalid("MISSING_PROPERTY", MissingPropertyMessage, input, closingOffset, index, DeprecatedProperty);
                if(!deprecated.Value && identifier.EndsWith('+'))
                    throw InvalidIdentifier(identifierOffset, index);
                if(!identifiers.Add(identifier))
                    throw Invalid("DUPLICATE_IDENTIFIER", DuplicateIdentifierMessage, input, identifierOffset, index, identifierProperty);
                entries.Add(new(identifier, deprecated.Value, identifierOffset, name, reference, seeAlso));
            }
            void CheckToken(Token token, Location location, int? propertyIndex = null, int? arrayIndex = null,
                bool isCollection = false, bool identifier = false) {
                var options = loader.options;
                // Apply the ordered budgets only once lexical recognition has succeeded.
                if(++loader.tokens > options.MaxTokens)
                    throw Exceeded(nameof(options.MaxTokens), token, location);
                if(token.RawLength > options.MaxTokenBytes)
                    throw Exceeded(nameof(options.MaxTokenBytes), token, location);
                if(token.Kind is TokenKind.Object or TokenKind.Array && location.Depth > options.MaxJsonDepth)
                    throw Exceeded(nameof(options.MaxJsonDepth), token, location);
                if(propertyIndex >= options.MaxObjectProperties)
                    throw Exceeded(nameof(options.MaxObjectProperties), token, location);
                if(arrayIndex >= options.MaxArrayLength)
                    throw Exceeded(nameof(options.MaxArrayLength), token, location);
                if(isCollection && arrayIndex >= (isLicense ? options.MaxLicenses : options.MaxExceptions))
                    throw Exceeded(isLicense ? nameof(options.MaxLicenses) : nameof(options.MaxExceptions), token, location);
                if(token.Kind != TokenKind.String)
                    return;
                if(token.TextLength > options.MaxStringLength)
                    throw Exceeded(nameof(options.MaxStringLength), token, location);
                loader.stringLength += token.TextLength;
                if(loader.stringLength > options.MaxTotalStringLength)
                    throw Exceeded(nameof(options.MaxTotalStringLength), token, location);
                if(identifier && token.TextLength > options.MaxIdentifierLength)
                    throw Exceeded(nameof(options.MaxIdentifierLength), token, location);
                if(token.UnpairedEscape >= 0)
                    throw Invalid("INVALID_UNICODE", InvalidUnicodeMessage, input, token.UnpairedEscape, location.Entry, location.Property);
            }
            InputFailure Exceeded(string limit, Token token, Location location) {
                return Limit(input, token.Offset, limit, location.Entry, location.Property);
            }
        }
    }
}
