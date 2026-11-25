using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Spdx.Expressions;

namespace Spdx.Compliance {
    sealed class ActionProjection(CancellationToken token) {
        static readonly ComplianceActionSummary Empty = new([], []);
        readonly ImmutableArray<ComplianceAction>.Builder current = ImmutableArray.CreateBuilder<ComplianceAction>();
        readonly ImmutableArray<ComplianceBranchCondition>.Builder contingent = ImmutableArray.CreateBuilder<ComplianceBranchCondition>();
        readonly List<OrSelection> path = new();
        internal static ComplianceActionSummary Project(ComplianceEvaluation evaluation, CancellationToken token) {
            token.ThrowIfCancellationRequested();
            var finding = evaluation.Finding;
            if(!evaluation.IsComplete || finding.Decision is ComplianceDecision.Invalid or ComplianceDecision.Unknown ||
                !finding.ConflictingObservations.IsEmpty ||
                finding.Reason is ComplianceReason.ProhibitedPresence or ComplianceReason.SourceConflict or ComplianceReason.CorrectionMismatch)
                return Empty;
            var projection = new ActionProjection(token);
            projection.Visit(finding);
            token.ThrowIfCancellationRequested();
            return new(projection.current.ToImmutable(), projection.contingent.ToImmutable());
        }
        void Visit(ComplianceFinding finding) {
            token.ThrowIfCancellationRequested();
            switch(finding.Kind) {
                case SpdxExpressionKind.Group:
                case SpdxExpressionKind.And:
                    foreach(var child in finding.Children)
                        Visit(child);
                    break;
                case SpdxExpressionKind.Or:
                    VisitOr(finding);
                    break;
                default:
                    // Non-operators can carry explanatory children. Only the finding's own evidence can initiate a step.
                    if(finding is { Reason: ComplianceReason.NoticeUnverified, Notice: not null })
                        Add(new(ComplianceActionKind.VerifyNotice, finding, []));
                    else if(finding is { Reason: ComplianceReason.CorrectionUnconfirmed, PendingCorrectionId: not null })
                        Add(new(ComplianceActionKind.ConfirmCorrection, finding, []));
                    break;
            }
        }
        void VisitOr(ComplianceFinding finding) {
            if(finding.SelectedIndex is int selected) {
                Visit(finding.Children[selected]);
                return;
            }
            if(finding.Reason != ComplianceReason.ChoiceRequired || finding.Span is not { } span)
                return;
            var alternatives = ImmutableArray.CreateBuilder<OrActionAlternative>();
            for(int i = 0; i < finding.Children.Length; i++) {
                token.ThrowIfCancellationRequested();
                var child = finding.Children[i];
                if(child.Decision is ComplianceDecision.Allow or ComplianceDecision.Conditional)
                    alternatives.Add(new(i, child));
            }
            Add(new(ComplianceActionKind.RecordChoice, finding, alternatives.ToImmutable()));
            foreach(var alternative in alternatives) {
                path.Add(new(span, alternative.OperandIndex));
                Visit(alternative.Finding);
                path.RemoveAt(path.Count - 1);
            }
        }
        void Add(ComplianceAction action) {
            if(path.Count == 0)
                current.Add(action);
            else
                contingent.Add(new([.. path], action));
        }
    }
}
