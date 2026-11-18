using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using Spdx.Expressions.Internal;

namespace Spdx.Expressions {
    /// <summary>A zero-based range measured in UTF-16 code units.</summary>
    public readonly struct SpdxSourceSpan {
        internal SpdxSourceSpan(int start, int length) {
            Start = start;
            Length = length;
        }
        /// <summary>Gets the inclusive start offset.</summary>
        public int Start { get; }
        /// <summary>Gets the length; EOF diagnostics may have zero length.</summary>
        public int Length { get; }
    }
    /// <summary>The result of syntactic parsing, independent of the registry.</summary>
    public enum SpdxParseStatus {
        /// <summary>A complete expression was parsed.</summary>
        Success,
        /// <summary>The input violates the fixed syntax profile.</summary>
        InvalidSyntax,
        /// <summary>A parser budget was exceeded.</summary>
        LimitExceeded
    }
    /// <summary>The severity of a diagnostic.</summary>
    public enum SpdxDiagnosticSeverity {
        /// <summary>Informational concern such as a deprecated identifier.</summary>
        Warning,
        /// <summary>A syntax, budget or identifier error.</summary>
        Error
    }
    /// <summary>The structural kind of an immutable syntax node.</summary>
    public enum SpdxExpressionKind {
        /// <summary>A license identifier, optionally with an adjacent plus.</summary>
        License,
        /// <summary>An unresolved custom license reference.</summary>
        Reference,
        /// <summary>A simple expression with an exception.</summary>
        With,
        /// <summary>An ordered conjunction.</summary>
        And,
        /// <summary>An ordered disjunction.</summary>
        Or,
        /// <summary>An explicit parenthesized group.</summary>
        Group
    }
    /// <summary>The namespace expected at an identifier occurrence.</summary>
    public enum SpdxIdentifierRole {
        /// <summary>A license ID.</summary>
        License,
        /// <summary>An exception ID following WITH.</summary>
        Exception
    }
    /// <summary>The outcome of looking up an identifier in its expected namespace.</summary>
    public enum SpdxIdentifierLookupStatus {
        /// <summary>Present in the expected namespace.</summary>
        Known,
        /// <summary>Absent from both namespaces.</summary>
        Unknown,
        /// <summary>Present only in the other namespace.</summary>
        WrongKind
    }
    /// <summary>A stable diagnostic code and source range with a human-readable English message.</summary>
    public sealed class SpdxDiagnostic {
        internal SpdxDiagnostic(string code, SpdxDiagnosticSeverity severity, SpdxSourceSpan span, string message) {
            Code = code;
            Severity = severity;
            Span = span;
            Message = message;
        }
        /// <summary>Gets the stable machine-readable code.</summary>
        public string Code { get; }
        /// <summary>Gets the severity.</summary>
        public SpdxDiagnosticSeverity Severity { get; }
        /// <summary>Gets the range in the original expression.</summary>
        public SpdxSourceSpan Span { get; }
        /// <summary>Gets explanatory text, which is not a machine contract.</summary>
        public string Message { get; }
    }
    /// <summary>Inclusive parser budgets. No maximum-sized buffers are allocated from these settings.</summary>
    public sealed class SpdxParseOptions {
        /// <summary>Maximum input UTF-16 units, including whitespace. Default 65,536.</summary>
        public int MaxInputLength { get; init; } = 65_536;
        /// <summary>Maximum units per identifier or reference component, excluding prefixes/suffixes. Default 256.</summary>
        public int MaxIdentifierLength { get; init; } = 256;
        /// <summary>Maximum tokens, excluding whitespace and EOF. A complete reference is one token. Default 16,384.</summary>
        public int MaxTokens { get; init; } = 16_384;
        /// <summary>Maximum AST nodes, including groups and flattened operator nodes. Default 8,192.</summary>
        public int MaxNodes { get; init; } = 8_192;
        /// <summary>Maximum concurrently open parentheses, from 1 through 256. Default 64.</summary>
        public int MaxParenthesisDepth { get; init; } = 64;
        internal void Check() {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxInputLength);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxIdentifierLength);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxTokens);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxNodes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxParenthesisDepth);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxParenthesisDepth, 256);
        }
    }
    /// <summary>Controls diagnostic storage without limiting validation traversal or occurrences.</summary>
    public sealed class SpdxValidationOptions {
        /// <summary>Maximum stored messages, at least one. Default 128. Omitted messages set DiagnosticsTruncated.</summary>
        public int MaxDiagnostics { get; init; } = 128;
    }
    /// <summary>A complete parse outcome. Failure never exposes a partial AST.</summary>
    public sealed class SpdxParseResult {
        internal SpdxParseResult(SpdxParseStatus status, SpdxParsedExpression? expression, SpdxDiagnostic? diagnostic) {
            Status = status;
            Expression = expression;
            if(diagnostic is not null)
                Diagnostics = [diagnostic];
        }
        /// <summary>Gets the syntax/budget outcome.</summary>
        public SpdxParseStatus Status { get; }
        /// <summary>Gets the expression only on success.</summary>
        public SpdxParsedExpression? Expression { get; }
        /// <summary>Gets no messages on success, or one primary diagnostic on failure.</summary>
        public IReadOnlyList<SpdxDiagnostic> Diagnostics { get; } = ReadOnlyCollection<SpdxDiagnostic>.Empty;
    }
    /// <summary>An immutable expression retaining the caller's original string and explicit groups.</summary>
    public sealed class SpdxParsedExpression {
        internal SpdxParsedExpression(string text, SpdxExpressionNode root) {
            SourceText = text;
            Root = root;
        }
        /// <summary>Gets the original string verbatim.</summary>
        public string SourceText { get; }
        /// <summary>Gets the root; its span excludes outer whitespace.</summary>
        public SpdxExpressionNode Root { get; }
        /// <summary>Gets SPDX-2.3.1; the syntax reference documents the fixed lexical policies.</summary>
        public string SyntaxProfile => "SPDX-2.3.1";
    }
    /// <summary>A source-preserving immutable node. Construction and inheritance are controlled by the parser.</summary>
    public abstract class SpdxExpressionNode {
        private protected SpdxExpressionNode(SpdxExpressionKind kind, SpdxSourceSpan span) {
            Kind = kind;
            Span = span;
        }
        /// <summary>Gets the structural kind.</summary>
        public SpdxExpressionKind Kind { get; }
        /// <summary>Gets the range from first through last token, including internal whitespace.</summary>
        public SpdxSourceSpan Span { get; }
        /// <summary>Enumerates this node and all its descendants in depth-first pre-order.</summary>
        /// <param name="cancellationToken">Cancellation observed as the enumerator advances.</param>
        /// <returns>A lazy sequence of the original nodes, visiting each node's children in source order.</returns>
        /// <remarks>
        /// Calling this method or obtaining an enumerator performs no traversal or
        /// cancellation check. Each enumeration starts at this node. A WITH exception
        /// remains a field of its node.
        /// Disposing an enumerator ends that enumeration without visiting further nodes or
        /// checking cancellation. Nodes already yielded remain available.
        /// </remarks>
        /// <exception cref="OperationCanceledException">
        /// The supplied token was canceled before or during an advance, including the advance to completion.
        /// </exception>
        public IEnumerable<SpdxExpressionNode> EnumerateNodes(CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            foreach(var (node, phase) in SyntaxTraversal.Walk(this, cancellationToken)) {
                if(phase == SyntaxTraversal.Enter)
                    yield return node;
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
    /// <summary>A license candidate; parsing does not require catalogue membership.</summary>
    public sealed class SpdxLicenseExpression : SpdxExpressionNode {
        internal SpdxLicenseExpression(string identifier, SpdxSourceSpan idSpan, bool orLater, SpdxSourceSpan span)
            : base(SpdxExpressionKind.License, span) {
            Identifier = identifier;
            IdentifierSpan = idSpan;
            OrLater = orLater;
        }
        /// <summary>Gets the exact spelling, excluding plus.</summary>
        public string Identifier { get; }
        /// <summary>Gets the identifier range, excluding plus.</summary>
        public SpdxSourceSpan IdentifierSpan { get; }
        /// <summary>Gets whether an adjacent plus suffix was present.</summary>
        public bool OrLater { get; }
    }
    /// <summary>A custom reference. Components and component spans exclude their prefixes.</summary>
    public sealed class SpdxReferenceExpression : SpdxExpressionNode {
        internal SpdxReferenceExpression(string? documentId, SpdxSourceSpan? documentSpan, string licenseId,
            SpdxSourceSpan licenseSpan, SpdxSourceSpan span) : base(SpdxExpressionKind.Reference, span) {
            DocumentId = documentId;
            DocumentIdSpan = documentSpan;
            LicenseId = licenseId;
            LicenseIdSpan = licenseSpan;
        }
        /// <summary>Gets the document component, if qualified.</summary>
        public string? DocumentId { get; }
        /// <summary>Gets its range excluding DocumentRef-.</summary>
        public SpdxSourceSpan? DocumentIdSpan { get; }
        /// <summary>Gets the license component.</summary>
        public string LicenseId { get; }
        /// <summary>Gets its range excluding LicenseRef-.</summary>
        public SpdxSourceSpan LicenseIdSpan { get; }
    }
    /// <summary>A license or reference with an exception candidate.</summary>
    public sealed class SpdxWithExpression : SpdxExpressionNode {
        internal SpdxWithExpression(SpdxExpressionNode operand, string identifier, SpdxSourceSpan idSpan, SpdxSourceSpan span)
            : base(SpdxExpressionKind.With, span) {
            Operand = operand;
            ExceptionIdentifier = identifier;
            ExceptionSpan = idSpan;
        }
        /// <summary>Gets the simple license or reference operand.</summary>
        public SpdxExpressionNode Operand { get; }
        /// <summary>Gets the exact exception spelling.</summary>
        public string ExceptionIdentifier { get; }
        /// <summary>Gets the exception identifier range.</summary>
        public SpdxSourceSpan ExceptionSpan { get; }
    }
    /// <summary>An uninterrupted conjunction of at least two operands in source order.</summary>
    public sealed class SpdxAndExpression : SpdxExpressionNode {
        internal SpdxAndExpression(List<SpdxExpressionNode> operands, SpdxSourceSpan span) : base(SpdxExpressionKind.And, span) {
            Operands = [.. operands];
        }
        /// <summary>Gets the ordered operands; explicit groups remain separate nodes.</summary>
        public IReadOnlyList<SpdxExpressionNode> Operands { get; }
    }
    /// <summary>An uninterrupted disjunction of at least two operands in source order.</summary>
    public sealed class SpdxOrExpression : SpdxExpressionNode {
        internal SpdxOrExpression(List<SpdxExpressionNode> operands, SpdxSourceSpan span) : base(SpdxExpressionKind.Or, span) {
            Operands = [.. operands];
        }
        /// <summary>Gets the ordered operands; explicit groups remain separate nodes.</summary>
        public IReadOnlyList<SpdxExpressionNode> Operands { get; }
    }
    /// <summary>An explicit group, including both parentheses in its node span.</summary>
    public sealed class SpdxGroupExpression : SpdxExpressionNode {
        internal SpdxGroupExpression(SpdxExpressionNode inner, SpdxSourceSpan span) : base(SpdxExpressionKind.Group, span) {
            Inner = inner;
        }
        /// <summary>Gets the expression inside the parentheses.</summary>
        public SpdxExpressionNode Inner { get; }
    }
}
