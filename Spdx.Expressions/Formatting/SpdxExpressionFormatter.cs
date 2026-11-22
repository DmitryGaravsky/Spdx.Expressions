using System;
using System.Text;
using System.Threading;
using Spdx.Expressions.Internal;

namespace Spdx.Expressions {
    /// <summary>Formats spacing and optionally applies the casing of identifiers in a supplied registry.</summary>
    public static class SpdxExpressionFormatter {
        const int TextChunkLength = 1024;
        /// <summary>Uses single ASCII spaces around operators and no spaces inside parentheses or before plus.</summary>
        /// <remarks>No registry lookup, normalization, simplification or reference resolution occurs.
        /// Re-parsing may require larger input limits because formatting can insert spaces.</remarks>
        /// <exception cref="ArgumentNullException">The expression is null.</exception>
        /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
        public static string Format(SpdxParsedExpression expression, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(expression);
            if(expression.Root is SpdxLicenseExpression or SpdxReferenceExpression &&
                expression.Root.Span.Length == expression.SourceText.Length) {
                cancellationToken.ThrowIfCancellationRequested();
                return expression.SourceText;
            }
            return Render(expression, null, cancellationToken);
        }
        /// <summary>Formats spacing and uses the supplied registry's spelling for known license and exception identifiers.</summary>
        /// <param name="expression">The parsed expression. Its source text, nodes and spans are preserved.</param>
        /// <param name="registry">The explicit registry whose spelling to use in each identifier's namespace.</param>
        /// <param name="cancellationToken">Cancellation observed while formatting.</param>
        /// <returns>The complete formatted expression, preserving references, groups, suffixes and operand order.</returns>
        /// <remarks>Case is applied to known IDs, including deprecated IDs. Unknown and wrong-kind IDs keep their
        /// source spelling. A trailing plus is preserved after looking up the base license identifier.
        /// The result can require a larger input length limit when reparsed because spacing can grow.</remarks>
        /// <exception cref="ArgumentNullException">The expression or registry is null.</exception>
        /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
        public static string FormatCanonicalCasing(SpdxParsedExpression expression, SpdxLicenseRegistry registry,
            CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(expression);
            ArgumentNullException.ThrowIfNull(registry);
            return Render(expression, registry, cancellationToken);
        }
        static string Render(SpdxParsedExpression expression, SpdxLicenseRegistry? registry, CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();
            var output = new StringBuilder();
            foreach(var (node, phase) in SyntaxTraversal.Walk(expression.Root, cancellationToken)) {
                if(phase == SyntaxTraversal.Enter) {
                    if(node is SpdxLicenseExpression license)
                        AppendLicense(license);
                    else if(node is SpdxReferenceExpression)
                        AppendSource(node.Span);
                    else if(node is SpdxGroupExpression)
                        output.Append('(');
                }
                else if(phase == SyntaxTraversal.Exit) {
                    if(node is SpdxGroupExpression)
                        output.Append(')');
                    else if(node is SpdxWithExpression with) {
                        output.Append(" WITH ");
                        AppendException(with);
                    }
                }
                else if(phase > 0)
                    output.Append(node is SpdxAndExpression ? " AND " : " OR ");
            }
            cancellationToken.ThrowIfCancellationRequested();
            return output.ToString();
            void AppendLicense(SpdxLicenseExpression license) {
                if(registry is not null) {
                    cancellationToken.ThrowIfCancellationRequested();
                    registry.TryGetLicense(license.Identifier, out var entry);
                    cancellationToken.ThrowIfCancellationRequested();
                    if(entry is not null) {
                        AppendText(entry.Identifier, 0, entry.Identifier.Length);
                        if(license.OrLater)
                            output.Append('+');
                        return;
                    }
                }
                AppendSource(license.Span);
            }
            void AppendException(SpdxWithExpression with) {
                if(registry is not null) {
                    cancellationToken.ThrowIfCancellationRequested();
                    registry.TryGetException(with.ExceptionIdentifier, out var entry);
                    cancellationToken.ThrowIfCancellationRequested();
                    if(entry is not null) {
                        AppendText(entry.Identifier, 0, entry.Identifier.Length);
                        return;
                    }
                }
                AppendSource(with.ExceptionSpan);
            }
            void AppendSource(SpdxSourceSpan span) {
                AppendText(expression.SourceText, span.Start, span.Length);
            }
            void AppendText(string text, int offset, int remaining) {
                while(remaining > 0) {
                    cancellationToken.ThrowIfCancellationRequested();
                    int count = Math.Min(remaining, TextChunkLength);
                    output.Append(text, offset, count);
                    offset += count;
                    remaining -= count;
                }
            }
        }
    }
}
