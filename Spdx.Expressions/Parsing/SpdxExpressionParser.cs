using System;
using System.Collections.Generic;
using System.Threading;

namespace Spdx.Expressions {
    /// <summary>Parses the fixed SPDX-2.3.1 expression profile without consulting a registry.</summary>
    public static class SpdxExpressionParser {
        /// <summary>Parses the entire string, retaining spelling, source ranges, order and explicit groups.</summary>
        /// <param name="text">The original expression, using only ASCII space, tab, CR or LF as whitespace.</param>
        /// <param name="options">Inclusive budgets, or defaults.</param>
        /// <param name="cancellationToken">Cancellation observed at entry and during scans.</param>
        /// <returns>A complete AST on success, otherwise one diagnostic and no AST.</returns>
        /// <exception cref="ArgumentNullException">The text is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">An option is outside its documented range.</exception>
        /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
        public static SpdxParseResult Parse(string text, SpdxParseOptions? options = null, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(text);
            options ??= new SpdxParseOptions();
            options.Check();
            cancellationToken.ThrowIfCancellationRequested();
            try {
                if(text.Length > options.MaxInputLength)
                    throw Failure("LIMIT_EXCEEDED", new(0, text.Length));
                var parser = new Parser(text, options, cancellationToken);
                var root = parser.ParseRoot();
                cancellationToken.ThrowIfCancellationRequested();
                var expression = new SpdxParsedExpression(text, root);
                return new(SpdxParseStatus.Success, expression, null);
            }
            catch(InputFailure failure) {
                return new(failure.Limit ? SpdxParseStatus.LimitExceeded : SpdxParseStatus.InvalidSyntax, null, failure.Diagnostic);
            }
        }
        internal static bool IsIdentifierCharacter(char c) {
            return c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '.';
        }
        static bool IsWhitespace(char c) {
            return c is ' ' or '\t' or '\r' or '\n';
        }
        // Only deliberate input failures cross this boundary. Programming/process exceptions propagate.
        static InputFailure Failure(string code, SpdxSourceSpan span) {
            return new(code, span);
        }
        sealed class InputFailure : Exception {
            internal InputFailure(string code, SpdxSourceSpan span) {
                Limit = code == "LIMIT_EXCEEDED";
                Diagnostic = new("SPDX_PARSE_" + code, SpdxDiagnosticSeverity.Error, span, Messages.For(code));
            }
            internal bool Limit { get; }
            internal SpdxDiagnostic Diagnostic { get; }
        }
        static class Messages {
            const string Empty = "An expression operand is required.";
            const string Character = "This character is not allowed in the expression profile.";
            const string Operand = "A license identifier, reference or group is required.";
            const string Operator = "An AND or OR operator is required between expressions.";
            const string Parenthesis = "The expression contains an unmatched parenthesis.";
            const string Reference = "The recognized reference must have nonempty valid components and exact prefixes.";
            const string Plus = "One adjacent plus is allowed only after a license identifier.";
            const string With = "WITH requires whitespace and a simple operand followed by one exception identifier.";
            const string Sentinel = "NONE and NOASSERTION are field values, not expression operands.";
            const string Limit = "An inclusive parser budget was exceeded.";
            internal static string For(string code) {
                return code switch {
                    "EMPTY_INPUT" => Empty,
                    "INVALID_CHARACTER" => Character,
                    "EXPECTED_OPERAND" => Operand,
                    "EXPECTED_OPERATOR" => Operator,
                    "UNBALANCED_PARENTHESIS" => Parenthesis,
                    "INVALID_REFERENCE" => Reference,
                    "INVALID_PLUS" => Plus,
                    "INVALID_WITH" => With,
                    "FIELD_SENTINEL" => Sentinel,
                    "LIMIT_EXCEEDED" => Limit,
                    _ => throw new InvalidOperationException()
                };
            }
        }
        enum TokenKind { End, Id, Reference, And, Or, With, Sentinel, Plus, Open, Close }
        readonly record struct Token(TokenKind Kind, SpdxSourceSpan Span, SpdxSourceSpan? Document = null, SpdxSourceSpan? License = null);
        sealed class Parser(string text, SpdxParseOptions options, CancellationToken cancellation) {
            const string DocumentReferencePrefix = "DocumentRef-";
            const string LicenseReferencePrefix = "LicenseRef-";
            const int LongestReservedTokenLength = 11;
            const int CancellationPollingMask = 255;
            int position;
            int tokens;
            int nodes;
            int depth;
            Token current;
            internal SpdxExpressionNode ParseRoot() {
                Next();
                if(current.Kind == TokenKind.End)
                    throw Failure("EMPTY_INPUT", current.Span);
                var root = Disjunction();
                if(current.Kind != TokenKind.End)
                    throw Unexpected(false);
                return root;
            }
            SpdxExpressionNode Disjunction() {
                var first = Conjunction();
                if(current.Kind != TokenKind.Or)
                    return first;
                CountNode(current.Span);
                var operands = new List<SpdxExpressionNode> { first };
                while(current.Kind == TokenKind.Or) {
                    Next();
                    operands.Add(Conjunction());
                }
                return new SpdxOrExpression(operands, Cover(first.Span, operands[^1].Span));
            }
            SpdxExpressionNode Conjunction() {
                var first = Term();
                if(current.Kind != TokenKind.And)
                    return first;
                CountNode(current.Span);
                var operands = new List<SpdxExpressionNode> { first };
                while(current.Kind == TokenKind.And) {
                    Next();
                    operands.Add(Term());
                }
                return new SpdxAndExpression(operands, Cover(first.Span, operands[^1].Span));
            }
            SpdxExpressionNode Term() {
                cancellation.ThrowIfCancellationRequested();
                var token = current;
                if(token.Kind == TokenKind.Open) {
                    if(depth == options.MaxParenthesisDepth)
                        throw Failure("LIMIT_EXCEEDED", token.Span);
                    CountNode(token.Span);
                    depth++;
                    Next();
                    if(current.Kind == TokenKind.End)
                        throw Failure("UNBALANCED_PARENTHESIS", current.Span);
                    var inner = Disjunction();
                    if(current.Kind != TokenKind.Close) {
                        if(current.Kind == TokenKind.End)
                            throw Failure("UNBALANCED_PARENTHESIS", current.Span);
                        throw Unexpected(false);
                    }
                    var span = Cover(token.Span, current.Span);
                    depth--;
                    Next();
                    return new SpdxGroupExpression(inner, span);
                }
                if(token.Kind is not (TokenKind.Id or TokenKind.Reference))
                    throw Unexpected(true);
                CountNode(token.Span);
                Next();
                SpdxExpressionNode simple;
                if(token.Kind == TokenKind.Reference) {
                    simple = new SpdxReferenceExpression(token.Document is { } doc ? Slice(doc) : null,
                        token.Document, Slice(token.License!.Value), token.License.Value, token.Span);
                }
                else {
                    bool plus = current.Kind == TokenKind.Plus;
                    var span = token.Span;
                    if(plus) {
                        if(current.Span.Start != End(token.Span))
                            throw Failure("INVALID_PLUS", current.Span);
                        span = Cover(span, current.Span);
                        Next();
                    }
                    simple = new SpdxLicenseExpression(Slice(token.Span), token.Span, plus, span);
                }
                if(current.Kind == TokenKind.Plus)
                    throw Failure("INVALID_PLUS", current.Span);
                if(current.Kind != TokenKind.With)
                    return simple;
                var with = current;
                if(with.Span.Start == End(simple.Span))
                    throw Failure("INVALID_WITH", with.Span);
                CountNode(with.Span);
                Next();
                if(current.Kind == TokenKind.Sentinel)
                    throw Failure("FIELD_SENTINEL", current.Span);
                if(current.Kind != TokenKind.Id || current.Span.Start == End(with.Span))
                    throw Failure("INVALID_WITH", current.Span);
                var exception = current;
                Next();
                return new SpdxWithExpression(simple, Slice(exception.Span), exception.Span, Cover(simple.Span, exception.Span));
            }
            InputFailure Unexpected(bool operand) {
                var code = current.Kind switch {
                    TokenKind.Sentinel => "FIELD_SENTINEL",
                    TokenKind.Plus => "INVALID_PLUS",
                    TokenKind.With => "INVALID_WITH",
                    TokenKind.Close when depth == 0 => "UNBALANCED_PARENTHESIS",
                    _ => operand ? "EXPECTED_OPERAND" : "EXPECTED_OPERATOR"
                };
                return Failure(code, current.Span);
            }
            void CountNode(SpdxSourceSpan span) {
                cancellation.ThrowIfCancellationRequested();
                if(nodes == options.MaxNodes)
                    throw Failure("LIMIT_EXCEEDED", span);
                nodes++;
            }
            void Next() {
                cancellation.ThrowIfCancellationRequested();
                while(position < text.Length && IsWhitespace(text[position])) {
                    Poll();
                    position++;
                }
                int start = position;
                if(start == text.Length) {
                    current = new(TokenKind.End, new(start, 0));
                    return;
                }
                if(tokens == options.MaxTokens)
                    throw Failure("LIMIT_EXCEEDED", new(start, 1));
                tokens++;
                var kind = text[position] switch {
                    '(' => TokenKind.Open,
                    ')' => TokenKind.Close,
                    '+' => TokenKind.Plus,
                    _ => TokenKind.Id
                };
                if(kind != TokenKind.Id) {
                    position++;
                    current = new(kind, new(start, 1));
                    return;
                }
                if(Starts(DocumentReferencePrefix) || Starts(LicenseReferencePrefix)) {
                    ReadReference(start);
                    return;
                }
                if(!IsIdentifierCharacter(text[position]))
                    throw Failure("INVALID_CHARACTER", new(position, 1));
                // Reserve enough room to classify whole keywords even with a tiny ID budget.
                while(position < text.Length && IsIdentifierCharacter(text[position])) {
                    Poll();
                    if(position - start >= Math.Max(options.MaxIdentifierLength, LongestReservedTokenLength))
                        throw Failure("LIMIT_EXCEEDED", new(start, position - start + 1));
                    position++;
                }
                var word = text.AsSpan(start, position - start);
                kind = word switch {
                    "AND" => TokenKind.And,
                    "OR" => TokenKind.Or,
                    "WITH" => TokenKind.With,
                    "NONE" or "NOASSERTION" => TokenKind.Sentinel,
                    _ => TokenKind.Id
                };
                var span = new SpdxSourceSpan(start, position - start);
                if(kind == TokenKind.Id && span.Length > options.MaxIdentifierLength)
                    throw Failure("LIMIT_EXCEEDED", span);
                if(kind is TokenKind.And or TokenKind.Or) {
                    if((start > 0 && !IsWhitespace(text[start - 1]) && text[start - 1] is not '(' and not ')') ||
                        (position < text.Length && !IsWhitespace(text[position]) && text[position] is not '(' and not ')'))
                        throw Failure("EXPECTED_OPERATOR", span);
                }
                if(kind == TokenKind.With &&
                    ((start > 0 && !IsWhitespace(text[start - 1])) || (position < text.Length && !IsWhitespace(text[position]))))
                    throw Failure("INVALID_WITH", span);
                current = new(kind, span);
            }
            void ReadReference(int start) {
                SpdxSourceSpan? document = null;
                if(Starts(DocumentReferencePrefix)) {
                    position += DocumentReferencePrefix.Length;
                    document = Component(start);
                    if(position == text.Length || text[position] != ':')
                        throw Failure("INVALID_REFERENCE", new(start, position - start));
                    position++;
                    if(!Starts(LicenseReferencePrefix))
                        throw Failure("INVALID_REFERENCE", new(start, position - start));
                }
                position += LicenseReferencePrefix.Length;
                var license = Component(start);
                if(position < text.Length && text[position] == ':')
                    throw Failure("INVALID_REFERENCE", new(start, position - start + 1));
                current = new(TokenKind.Reference, new(start, position - start), document, license);
            }
            SpdxSourceSpan Component(int referenceStart) {
                int start = position;
                while(position < text.Length && IsIdentifierCharacter(text[position])) {
                    Poll();
                    if(position - start == options.MaxIdentifierLength)
                        throw Failure("LIMIT_EXCEEDED", new(start, position - start + 1));
                    position++;
                }
                if(position == start)
                    throw Failure("INVALID_REFERENCE", new(referenceStart, position - referenceStart));
                return new(start, position - start);
            }
            bool Starts(string prefix) {
                return text.AsSpan(position).StartsWith(prefix, StringComparison.Ordinal);
            }
            void Poll() {
                if((position & CancellationPollingMask) == 0)
                    cancellation.ThrowIfCancellationRequested();
            }
            string Slice(SpdxSourceSpan span) {
                return text.Substring(span.Start, span.Length);
            }
            static int End(SpdxSourceSpan span) {
                return span.Start + span.Length;
            }
            static SpdxSourceSpan Cover(SpdxSourceSpan first, SpdxSourceSpan last) {
                return new(first.Start, End(last) - first.Start);
            }
        }
    }
}
