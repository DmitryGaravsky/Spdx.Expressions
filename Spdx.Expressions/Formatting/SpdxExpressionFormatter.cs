using System;
using System.Text;
using System.Threading;
using Spdx.Expressions.Internal;

namespace Spdx.Expressions {

    /// <summary>Formats spacing while preserving spelling, order, explicit groups and suffixes.</summary>
    public static class SpdxExpressionFormatter {
        const int SourceChunkLength = 1024;
        /// <summary>Uses single ASCII spaces around operators and no spaces inside parentheses or before plus.</summary>
        /// <remarks>No registry lookup, normalization, simplification or reference resolution occurs.
        /// Re-parsing may require larger input limits because formatting can insert spaces.</remarks>
        /// <exception cref="ArgumentNullException">The expression is null.</exception>
        /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
        public static string Format(SpdxParsedExpression expression, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(expression);
            cancellationToken.ThrowIfCancellationRequested();
            var output = new StringBuilder();
            foreach(var (node, phase) in SyntaxTraversal.Walk(expression.Root, cancellationToken)) {
                if(phase == SyntaxTraversal.Enter) {
                    if(node is SpdxLicenseExpression or SpdxReferenceExpression)
                        AppendSource(node.Span);
                    else if(node is SpdxGroupExpression)
                        output.Append('(');
                }
                else if(phase == SyntaxTraversal.Exit) {
                    if(node is SpdxGroupExpression)
                        output.Append(')');
                    else if(node is SpdxWithExpression with) {
                        output.Append(" WITH ");
                        AppendSource(with.ExceptionSpan);
                    }
                }
                else if(phase > 0)
                    output.Append(node is SpdxAndExpression ? " AND " : " OR ");
            }
            cancellationToken.ThrowIfCancellationRequested();
            return output.ToString();

            void AppendSource(SpdxSourceSpan span) {
                int remaining = span.Length;
                int offset = span.Start;
                while(remaining > 0) {
                    cancellationToken.ThrowIfCancellationRequested();
                    int count = Math.Min(remaining, SourceChunkLength);
                    output.Append(expression.SourceText, offset, count);
                    offset += count;
                    remaining -= count;
                }
            }
        }
    }

}
