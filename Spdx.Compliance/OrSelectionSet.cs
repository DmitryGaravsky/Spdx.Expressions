using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Spdx.Expressions;

namespace Spdx.Compliance {
    /// <summary>One choice for an OR occurrence in the original declaration.</summary>
    /// <param name="Start">Zero-based UTF-16 offset of the OR node.</param>
    /// <param name="Length">UTF-16 length of the OR node.</param>
    /// <param name="OperandIndex">Zero-based index in the full list of direct operands.</param>
    public readonly record struct OrSelection(int Start, int Length, int OperandIndex) {
        /// <summary>Creates a choice using the span of an OR finding.</summary>
        public OrSelection(SpdxSourceSpan span, int operandIndex) : this(span.Start, span.Length, operandIndex) {
        }
    }
    /// <summary>An immutable batch of choices bound to one exact declaration string.</summary>
    public sealed class OrSelectionSet {
        /// <summary>Maximum number of entries accepted in a batch, including repeats.</summary>
        public const int MaxEntries = 8_192;
        /// <summary>Snapshots the entries. The evaluator validates their coordinates and active routes atomically.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The batch exceeds <see cref="MaxEntries"/>.</exception>
        /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
        public OrSelectionSet(string sourceText, IEnumerable<OrSelection> selections, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(sourceText);
            ArgumentNullException.ThrowIfNull(selections);
            cancellationToken.ThrowIfCancellationRequested();
            SourceText = sourceText;
            var entries = ImmutableArray.CreateBuilder<OrSelection>();
            foreach(var selection in selections) {
                cancellationToken.ThrowIfCancellationRequested();
                if(entries.Count == MaxEntries)
                    throw new ArgumentOutOfRangeException(nameof(selections), "The OR selection entry limit was exceeded.");
                entries.Add(selection);
            }
            cancellationToken.ThrowIfCancellationRequested();
            Selections = entries.ToImmutable();
        }
        /// <summary>Gets the exact original declaration, including spacing and casing.</summary>
        public string SourceText { get; }
        /// <summary>Gets the supplied entries in caller order.</summary>
        public ImmutableArray<OrSelection> Selections { get; }
        internal Dictionary<(int Start, int Length), int>? Bind(SpdxParsedExpression expression, CancellationToken token) {
            if(!string.Equals(SourceText, expression.SourceText, StringComparison.Ordinal))
                return null;
            var entries = new Dictionary<(int, int), int>(Selections.Length);
            foreach(var selection in Selections) {
                token.ThrowIfCancellationRequested();
                if(!entries.TryAdd((selection.Start, selection.Length), selection.OperandIndex))
                    return null;
            }
            // Any unmatched entry was inactive, had no OR at its exact span, or had an invalid operand index.
            return CountActive(expression.Root, entries, token) == entries.Count ? entries : null;
        }
        static int CountActive(SpdxExpressionNode node, Dictionary<(int, int), int> entries, CancellationToken token) {
            token.ThrowIfCancellationRequested();
            switch(node) {
                case SpdxGroupExpression group:
                    return CountActive(group.Inner, entries, token);
                case SpdxAndExpression and:
                    int count = 0;
                    foreach(var operand in and.Operands)
                        count += CountActive(operand, entries, token);
                    return count;
                case SpdxOrExpression disjunction when entries.TryGetValue((disjunction.Span.Start, disjunction.Span.Length),
                    out int index) && (uint)index < (uint)disjunction.Operands.Count:
                    return 1 + CountActive(disjunction.Operands[index], entries, token);
                default:
                    return 0;
            }
        }
    }
}
