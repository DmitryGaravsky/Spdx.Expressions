using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;

namespace Spdx.Expressions {
    /// <summary>The observed value of a license field, including unsuccessful expression reads.</summary>
    public enum SpdxLicenseFieldKind {
        /// <summary>The caller supplied null.</summary>
        Missing,
        /// <summary>The field contained a zero-length string.</summary>
        Empty,
        /// <summary>The entire field contained the explicit NONE value with optional outer profile whitespace.</summary>
        None,
        /// <summary>The entire field contained the explicit NOASSERTION value with optional outer profile whitespace.</summary>
        NoAssertion,
        /// <summary>The field contained a syntactically complete expression.</summary>
        Expression,
        /// <summary>The supplied text has invalid expression syntax.</summary>
        InvalidSyntax,
        /// <summary>The input or expression exceeded a configured parser budget.</summary>
        LimitExceeded
    }
    /// <summary>The classification and exact original text of one license field.</summary>
    public sealed class SpdxLicenseFieldResult {
        internal SpdxLicenseFieldResult(SpdxLicenseFieldKind kind, string? sourceText, SpdxParseResult? parsed = null) {
            Kind = kind;
            SourceText = sourceText;
            Expression = parsed?.Expression;
            Diagnostics = parsed?.Diagnostics ?? ReadOnlyCollection<SpdxDiagnostic>.Empty;
        }
        /// <summary>Gets the observed field value or failure kind.</summary>
        public SpdxLicenseFieldKind Kind { get; }
        /// <summary>Gets the original string verbatim, including padding, or null for a missing field.</summary>
        public string? SourceText { get; }
        /// <summary>Gets the parsed AST only when the kind is Expression.</summary>
        public SpdxParsedExpression? Expression { get; }
        /// <summary>Gets one native parser diagnostic for InvalidSyntax or LimitExceeded, otherwise an empty list.</summary>
        public IReadOnlyList<SpdxDiagnostic> Diagnostics { get; }
    }
    /// <summary>Reads an observed SPDX license field without applying document defaults or consulting a registry.</summary>
    public static class SpdxLicenseFieldReader {
        /// <summary>Classifies the complete field and parses expression text in its original coordinates.</summary>
        /// <param name="text">Original nullable field text. Only a zero-length string is classified as Empty.</param>
        /// <param name="options">Inclusive parser budgets. The input length includes any padding.</param>
        /// <param name="cancellationToken">Cancellation observed at entry and during scans.</param>
        /// <returns>Field evidence, including the expression on success or the parser diagnostic on failure.</returns>
        /// <exception cref="ArgumentOutOfRangeException">An option is outside its documented range.</exception>
        /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
        public static SpdxLicenseFieldResult Read(string? text, SpdxParseOptions? options = null, CancellationToken cancellationToken = default) {
            options ??= new SpdxParseOptions();
            options.Check();
            cancellationToken.ThrowIfCancellationRequested();
            if(text is null)
                return new(SpdxLicenseFieldKind.Missing, null);
            if(text.Length == 0)
                return new(SpdxLicenseFieldKind.Empty, text);
            int start = 0;
            if(text.Length <= options.MaxInputLength) {
                start = SpdxExpressionParser.SkipWhitespace(text, 0, cancellationToken);
                if(ReadSentinel(text, start, cancellationToken) is { } sentinel)
                    return new(sentinel, text);
            }
            var parsed = SpdxExpressionParser.ParseCore(text, start, options, cancellationToken);
            var kind = parsed.Status switch {
                SpdxParseStatus.Success => SpdxLicenseFieldKind.Expression,
                SpdxParseStatus.InvalidSyntax => SpdxLicenseFieldKind.InvalidSyntax,
                SpdxParseStatus.LimitExceeded => SpdxLicenseFieldKind.LimitExceeded,
                _ => throw new InvalidOperationException()
            };
            return new(kind, text, parsed);
        }
        static SpdxLicenseFieldKind? ReadSentinel(string text, int start, CancellationToken cancellationToken) {
            if(IsWholeValue(SpdxExpressionParser.NoneKeyword))
                return SpdxLicenseFieldKind.None;
            if(IsWholeValue(SpdxExpressionParser.NoAssertionKeyword))
                return SpdxLicenseFieldKind.NoAssertion;
            return null;
            bool IsWholeValue(string keyword) {
                if(!text.AsSpan(start).StartsWith(keyword, StringComparison.Ordinal))
                    return false;
                int end = SpdxExpressionParser.SkipWhitespace(text, start + keyword.Length, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                return end == text.Length;
            }
        }
    }
}
