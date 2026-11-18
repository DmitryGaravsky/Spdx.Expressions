using System;
using System.Threading;

namespace Spdx.Expressions {
    /// <summary>Visits immutable syntax nodes in depth-first pre-order through typed callbacks.</summary>
    /// <remarks>Descent is handled by the walker. Derived types own their callback state.</remarks>
    public abstract class SpdxExpressionWalker {
        /// <summary>Initializes a syntax walker.</summary>
        protected SpdxExpressionWalker() {
        }
        /// <summary>Synchronously visits the root and all its descendants in source child order.</summary>
        /// <param name="root">The first node to visit. It may be the root of a subtree.</param>
        /// <param name="cancellationToken">Cancellation observed between visits and before successful return.</param>
        /// <remarks>
        /// Callbacks run on the calling thread. A callback exception propagates unchanged.
        /// The caller retains the effects of callbacks that have already run.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="root"/> is null.</exception>
        /// <exception cref="OperationCanceledException">The supplied token was canceled.</exception>
        public void Walk(SpdxExpressionNode root, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(root);
            foreach(var node in root.EnumerateNodes(cancellationToken)) {
                switch(node) {
                    case SpdxLicenseExpression license:
                        VisitLicense(license);
                        break;
                    case SpdxReferenceExpression reference:
                        VisitReference(reference);
                        break;
                    case SpdxWithExpression with:
                        VisitWith(with);
                        break;
                    case SpdxAndExpression and:
                        VisitAnd(and);
                        break;
                    case SpdxOrExpression or:
                        VisitOr(or);
                        break;
                    case SpdxGroupExpression group:
                        VisitGroup(group);
                        break;
                    default:
                        VisitNode(node);
                        break;
                }
            }
        }
        /// <summary>Visits a node through the fallback hook. The default implementation does nothing.</summary>
        protected virtual void VisitNode(SpdxExpressionNode node) {
        }
        /// <summary>Visits a license. The default implementation calls <see cref="VisitNode"/>.</summary>
        protected virtual void VisitLicense(SpdxLicenseExpression node) {
            VisitNode(node);
        }
        /// <summary>Visits a reference. The default implementation calls <see cref="VisitNode"/>.</summary>
        protected virtual void VisitReference(SpdxReferenceExpression node) {
            VisitNode(node);
        }
        /// <summary>Visits a WITH node before its operand. The default implementation calls <see cref="VisitNode"/>.</summary>
        protected virtual void VisitWith(SpdxWithExpression node) {
            VisitNode(node);
        }
        /// <summary>Visits a conjunction before its operands. The default implementation calls <see cref="VisitNode"/>.</summary>
        protected virtual void VisitAnd(SpdxAndExpression node) {
            VisitNode(node);
        }
        /// <summary>Visits a disjunction before its operands. The default implementation calls <see cref="VisitNode"/>.</summary>
        protected virtual void VisitOr(SpdxOrExpression node) {
            VisitNode(node);
        }
        /// <summary>Visits an explicit group before its inner expression. The default implementation calls <see cref="VisitNode"/>.</summary>
        protected virtual void VisitGroup(SpdxGroupExpression node) {
            VisitNode(node);
        }
    }
}
