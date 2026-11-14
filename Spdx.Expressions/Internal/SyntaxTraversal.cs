using System;
using System.Collections.Generic;
using System.Threading;

namespace Spdx.Expressions.Internal {

    // The stack grows with structural depth, never with the length of a flattened operand chain.
    static class SyntaxTraversal {
        internal const int Enter = -1;
        internal const int Exit = int.MaxValue;

        internal static IEnumerable<(SpdxExpressionNode Node, int Phase)> Walk(SpdxExpressionNode root, CancellationToken cancellation) {
            var stack = new Stack<(SpdxExpressionNode Node, int Phase)>();
            stack.Push((root, Enter));
            while(stack.TryPop(out var frame)) {
                cancellation.ThrowIfCancellationRequested();
                var (node, phase) = frame;
                if(phase == Enter) {
                    yield return frame;
                    stack.Push((node, 0));
                    continue;
                }
                int count = node switch {
                    SpdxAndExpression and => and.Operands.Count,
                    SpdxOrExpression or => or.Operands.Count,
                    SpdxGroupExpression or SpdxWithExpression => 1,
                    _ => 0
                };
                if(phase == count) {
                    yield return (node, Exit);
                    continue;
                }
                yield return frame;
                SpdxExpressionNode child = node switch {
                    SpdxAndExpression and => and.Operands[phase],
                    SpdxOrExpression or => or.Operands[phase],
                    SpdxGroupExpression group => group.Inner,
                    SpdxWithExpression with => with.Operand,
                    _ => throw new InvalidOperationException()
                };
                stack.Push((node, phase + 1));
                stack.Push((child, Enter));
            }
        }
    }

}
