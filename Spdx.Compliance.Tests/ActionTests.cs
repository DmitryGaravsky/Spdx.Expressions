using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using NUnit.Framework;
using static Spdx.Compliance.ComplianceActionKind;
using static Spdx.Compliance.ComplianceDecision;
using static Spdx.Compliance.ComplianceReason;

namespace Spdx.Compliance.Tests {
    [TestFixture]
    public sealed class ActionTests {
        const string Simple = "OFL-1.1 OR MIT";
        const string Nested = "MIT OR (OFL-1.1 OR Apache-2.0)";
        const string Independent = "(MIT OR OFL-1.1) AND (OFL-1.1 OR MIT)";
        [Test]
        public void NoticeMovesFromContingentToCurrentThenCompletes() {
            var policy = Fixtures.Policy();
            var open = Evaluate(Simple, policy);
            var summary = open.GetActionSummary();
            var choice = summary.CurrentActions.Single();
            AssertAction(choice, RecordChoice, ChoiceRequired, 0, 14);
            Assert.That(choice.Finding, Is.SameAs(open.Finding));
            Assert.That(choice.Alternatives.Select(c => c.OperandIndex), Is.EqualTo(new[] { 0, 1 }));
            var condition = summary.BranchConditions.Single();
            Assert.That(condition.RequiredChoices, Is.EqualTo(new OrSelection[] { new(0, 14, 0) }));
            AssertAction(condition.Action, VerifyNotice, NoticeUnverified, 0, 7);
            Assert.That(condition.Action.Finding.Notice, Is.EqualTo(new NoticeFinding(null, "notes/NOTICE", Fixtures.NoticeDigest, null, false)));
            var chosen = Evaluate(Simple, policy, [new(0, 14, 0)]);
            var pending = chosen.GetActionSummary();
            Assert.That(chosen.Finding.Decision, Is.EqualTo(Conditional));
            AssertAction(pending.CurrentActions.Single(), VerifyNotice, NoticeUnverified, 0, 7);
            Assert.That(pending.BranchConditions, Is.Empty);
            var delivery = new NoticeDelivery("release", true, [new("notes/NOTICE", Fixtures.NoticeDigest)]);
            var verified = Evaluate(Simple, policy, [new(0, 14, 0)], delivery);
            Assert.That(verified.Finding.Reason, Is.EqualTo(ChoiceAllowed));
            Assert.That(verified.Finding.Children[0].Notice!.ObservedSha256, Is.EqualTo(Fixtures.NoticeDigest));
            AssertEmpty(verified);
            var alternative = Evaluate(Simple, policy, [new(0, 14, 1)]);
            Assert.That(alternative.Finding.Reason, Is.EqualTo(ChoiceAllowed));
            Assert.That(alternative.Finding.Children[0].Reason, Is.EqualTo(NoticeUnverified));
            AssertEmpty(alternative);
        }
        [Test]
        public void NestedPathsHoldOnlyTheMissingAncestorChoices() {
            var policy = Fixtures.Policy();
            var evaluation = Evaluate(Nested, policy);
            var outer = new OrSelection(0, 30, 1);
            var inner = new OrSelection(8, 21, 0);
            var open = evaluation.GetActionSummary();
            AssertAction(open.CurrentActions.Single(), RecordChoice, ChoiceRequired, 0, 30);
            Assert.That(open.BranchConditions.Length, Is.EqualTo(2));
            Assert.That(open.BranchConditions[0].RequiredChoices, Is.EqualTo(new[] { outer }));
            AssertAction(open.BranchConditions[0].Action, RecordChoice, ChoiceRequired, 8, 21);
            Assert.That(open.BranchConditions[1].RequiredChoices, Is.EqualTo(new[] { outer, inner }));
            AssertAction(open.BranchConditions[1].Action, VerifyNotice, NoticeUnverified, 8, 7);
            var selectedOuter = Evaluate(Nested, policy, [outer]).GetActionSummary();
            AssertAction(selectedOuter.CurrentActions.Single(), RecordChoice, ChoiceRequired, 8, 21);
            Assert.That(selectedOuter.BranchConditions.Single().RequiredChoices, Is.EqualTo(new[] { inner }));
            var selectedBoth = Evaluate(Nested, policy, [inner, outer]).GetActionSummary();
            AssertAction(selectedBoth.CurrentActions.Single(), VerifyNotice, NoticeUnverified, 8, 7);
            Assert.That(selectedBoth.BranchConditions, Is.Empty);
        }
        [Test]
        public void IndependentOccurrencesStaySeparateInSourceOrder() {
            var policy = Fixtures.Policy();
            var open = Evaluate(Independent, policy).GetActionSummary();
            var left = new OrSelection(1, 14, 1);
            var right = new OrSelection(22, 14, 0);
            Assert.That(open.CurrentActions.Select(a => a.Span!.Value.Start), Is.EqualTo(new[] { 1, 22 }));
            Assert.That(open.CurrentActions.Select(a => a.Kind), Is.EqualTo(new[] { RecordChoice, RecordChoice }));
            Assert.That(open.BranchConditions.Select(c => c.RequiredChoices.Single()), Is.EqualTo(new[] { left, right }));
            Assert.That(open.BranchConditions.Select(c => c.Action.Span!.Value.Start), Is.EqualTo(new[] { 8, 22 }));
            var partial = Evaluate(Independent, policy, [left]).GetActionSummary();
            Assert.That(partial.CurrentActions.Select(a => a.Kind), Is.EqualTo(new[] { VerifyNotice, RecordChoice }));
            AssertAction(partial.CurrentActions[0], VerifyNotice, NoticeUnverified, 8, 7);
            AssertAction(partial.CurrentActions[1], RecordChoice, ChoiceRequired, 22, 14);
            Assert.That(partial.BranchConditions.Single().RequiredChoices, Is.EqualTo(new[] { right }));
        }
        [Test]
        public void ChoiceAlternativesPreserveIndicesAndExcludeIneligibleRoutes() {
            var policy = Fixtures.Policy();
            var duplicate = Evaluate("MIT OR MIT", policy).GetActionSummary().CurrentActions.Single();
            Assert.That(duplicate.Alternatives.Select(c => c.OperandIndex), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(duplicate.Alternatives.Select(c => c.Finding.Span!.Value.Start), Is.EqualTo(new[] { 0, 7 }));
            var selected = Evaluate("MIT OR MIT", policy, [new(0, 10, 1)]);
            Assert.That(selected.Finding.SelectedIndex, Is.EqualTo(1));
            AssertEmpty(selected);
            const string deniedFirst = "GPL-3.0-only OR OFL-1.1";
            var open = Evaluate(deniedFirst, policy).GetActionSummary();
            Assert.That(open.CurrentActions.Single().Alternatives.Single().OperandIndex, Is.EqualTo(1));
            Assert.That(open.BranchConditions.Single().RequiredChoices, Is.EqualTo(new OrSelection[] { new(0, 23, 1) }));
            var denied = Evaluate(deniedFirst, policy, [new(0, 23, 0)]);
            Assert.That(denied.Finding.Reason, Is.EqualTo(ChoiceDenied));
            AssertEmpty(denied);
            var rejected = ComplianceEngine.Evaluate("MIT OR MIT", policy, new() { SelectedBranch = "MIT" });
            Assert.That(rejected.Finding.Reason, Is.EqualTo(SelectionMismatch));
            AssertEmpty(rejected);
        }
        [TestCase(null, Unknown, FieldMissing)]
        [TestCase("NOASSERTION", Unknown, FieldNoassertion)]
        [TestCase("Unregistered-1.0", Review, UnknownId)]
        [TestCase("MIT OR (", Invalid, InvalidSyntax)]
        [TestCase("BSD-3-Clause OR GPL-3.0-only", Review, OrUnresolved)]
        [TestCase("GPL-3.0-only OR GPL-3.0-only", Deny, OrAllDenied)]
        public void BoundaryOutcomesHaveNoAction(string? source, ComplianceDecision decision, ComplianceReason reason) {
            var evaluation = ComplianceEngine.Evaluate(source, Fixtures.Policy());
            Assert.That(evaluation.Finding.Decision, Is.EqualTo(decision));
            Assert.That(evaluation.Finding.Reason, Is.EqualTo(reason));
            AssertEmpty(evaluation);
        }
        [Test]
        public void IncompleteAndIneligibleAlternativesDoNotSuggestSteps() {
            var policy = Fixtures.Policy();
            var limited = Evaluate(new string(' ', 201), policy);
            Assert.That(limited.IsComplete, Is.False);
            Assert.That(limited.Finding.Reason, Is.EqualTo(LimitExceeded));
            AssertEmpty(limited);
            var reviewOnly = Evaluate("(OFL-1.1 AND BSD-3-Clause) OR MIT", policy).GetActionSummary();
            Assert.That(reviewOnly.CurrentActions.Single().Alternatives.Single().OperandIndex, Is.EqualTo(1));
            Assert.That(reviewOnly.BranchConditions, Is.Empty);
        }
        [Test]
        public void OrdinaryDenialLeavesMandatorySiblingActions() {
            var result = Evaluate("OFL-1.1 AND GPL-3.0-only", Fixtures.Policy());
            Assert.That(result.Finding.Decision, Is.EqualTo(Deny));
            Assert.That(result.Finding.Reason, Is.EqualTo(AndDenied));
            AssertAction(result.GetActionSummary().CurrentActions.Single(), VerifyNotice, NoticeUnverified, 0, 7);
        }
        [Test]
        public void RejectedBatchesAndPresenceBanDoNotPromoteExplanatoryActions() {
            var policy = Fixtures.Policy();
            ComplianceContext[] rejected = [
                new() { OrSelections = new(" " + Nested, [new(0, 30, 0)]) },
                new() { OrSelections = new(Nested, [new(0, 30, 1)]), SelectedBranch = "MIT" },
                new() { OrSelections = new(Nested, [new(0, 30, 1), new(0, 30, 1)]) },
                new() { OrSelections = new(Nested, [new(0, 30, 0), new(8, 21, 0)]) }
            ];
            foreach(var context in rejected) {
                var result = ComplianceEngine.Evaluate(Nested, policy, context);
                Assert.That(result.Finding.Reason, Is.EqualTo(SelectionMismatch));
                Assert.That(result.Finding.Children[0].Reason, Is.EqualTo(ChoiceRequired));
                AssertEmpty(result);
            }
            const string banned = "OFL-1.1 OR (MIT OR GPL-3.0-only)";
            foreach(var batch in new OrSelection[][] { [new(0, 32, 0)], [new(12, 19, 0)] }) {
                var result = ComplianceEngine.Evaluate(banned, policy, new() { Profile = "ban", OrSelections = new(banned, batch) });
                Assert.That(result.Finding.Reason, Is.EqualTo(ProhibitedPresence));
                Assert.That(result.Finding.Children[0].Reason, Is.EqualTo(batch[0].Start == 0 ? NoticeUnverified : SelectionMismatch));
                AssertEmpty(result);
            }
        }
        [Test]
        public void ComponentClaimsAndConflictsProjectOnlyTheFinalOccurrence() {
            var pendingPolicy = Fixtures.Policy("""
                {"schemaVersion":1,"id":"pending","correctionProperties":{"id":"fix.id","source":"fix.source"},
                 "pendingCorrections":[{"package":"pkg:nuget/Sample@1.0.0","license":"BSD-3-Clause","id":"fix-1"}]}
                """);
            const string claim = """
                "name":"Sample","version":"1.0.0","purl":"pkg:nuget/Sample@1.0.0",
                "properties":[{"name":"fix.id","value":"fix-1"},{"name":"fix.source","value":"sample"}],
                "licenses":[{"license":{"id":"BSD-3-Clause"}}]
                """;
            var corrected = CycloneDxTests.AnalyzeComponent(claim, pendingPolicy).Findings[0];
            var confirmation = corrected.Evaluation.GetActionSummary().CurrentActions.Single();
            Assert.That(confirmation.Kind, Is.EqualTo(ConfirmCorrection));
            Assert.That(confirmation.Reason, Is.EqualTo(CorrectionUnconfirmed));
            Assert.That(confirmation.Finding.PendingCorrectionId, Is.EqualTo("fix-1"));
            Assert.That(confirmation.Finding.Children[0].Reason, Is.EqualTo(NoPolicyRule));
            var conflicted = CycloneDxTests.AnalyzeComponent("""
                "licenses":[{"expression":"OFL-1.1 OR MIT"}],"evidence":{"licenses":[{"expression":"Apache-2.0"}]}
                """, Fixtures.Policy()).Findings[0].Evaluation;
            Assert.That(conflicted.Finding.Reason, Is.EqualTo(SourceConflict));
            Assert.That(conflicted.Finding.ConflictingObservations, Is.EqualTo(new[] { "Apache-2.0" }));
            Assert.That(conflicted.Finding.Children[0].Reason, Is.EqualTo(ChoiceRequired));
            AssertEmpty(conflicted);
        }
        [Test]
        public void CorrectionMismatchKeepsTheChoiceAsEvidenceOnly() {
            using(var catalog = Fixtures.Json("""
                [{"id":"fix-1","artifactKind":"nuget","packageId":"Sample","versionRange":"[1.0.0]",
                  "license":{"id":"MIT","url":"https://example.invalid/license"}}]
                """)) {
                string digest = Convert.ToHexStringLower(SHA256.HashData(catalog.ToArray()));
                var policy = Fixtures.Policy($$"""
                    {"schemaVersion":1,"id":"pinned","allow":["MIT"],
                     "correctionProperties":{"id":"fix.id","source":"fix.source"},
                     "correctionCatalog":{"sha256":"{{digest}}","source":"sample"} }
                    """, catalog);
                var component = CycloneDxTests.AnalyzeComponent("""
                    "licenses":[{"expression":"OFL-1.1 OR MIT"}],"properties":[{"name":"fix.id","value":"fix-1"}]
                    """, policy).Findings[0];
                Assert.That(component.Evaluation.Finding.Reason, Is.EqualTo(CorrectionMismatch));
                Assert.That(component.Correction!.Status, Is.EqualTo(CorrectionStatus.InvalidProvenance));
                Assert.That(component.Evaluation.Finding.Children[0].Reason, Is.EqualTo(ChoiceRequired));
                AssertEmpty(component.Evaluation);
            }
        }
        [Test]
        public void ProjectionsFollowTheSbomOccurrenceAndDoNotChangeSerialization() {
            using(var stream = Fixtures.Json("""
                {"bomFormat":"CycloneDX","specVersion":"1.6","components":[
                  {"name":"first","licenses":[{"expression":"OFL-1.1 OR MIT"}]},
                  {"name":"second","licenses":[{"expression":"OFL-1.1 OR MIT"}]}]}
                """)) {
                var report = CycloneDxComplianceAnalyzer.Analyze(stream, Fixtures.Policy(), contextForComponent: c =>
                    c.Pointer == "/components/0" ? new() { OrSelections = new(Simple, [new(0, 14, 0)]) } : null);
                var first = report.Findings[0].Evaluation;
                var second = report.Findings[1].Evaluation;
                string before = JsonSerializer.Serialize(first);
                AssertAction(first.GetActionSummary().CurrentActions.Single(), VerifyNotice, NoticeUnverified, 0, 7);
                AssertAction(second.GetActionSummary().CurrentActions.Single(), RecordChoice, ChoiceRequired, 0, 14);
                Assert.That(second.GetActionSummary().BranchConditions, Has.Length.EqualTo(1));
                Assert.That(JsonSerializer.Serialize(first), Is.EqualTo(before));
                Assert.That(before, Does.Not.Contain(nameof(ComplianceActionSummary.CurrentActions)));
                using(var cancellation = new CancellationTokenSource()) {
                    cancellation.Cancel();
                    var error = Assert.Throws<OperationCanceledException>(() => first.GetActionSummary(cancellation.Token));
                    Assert.That(error!.CancellationToken, Is.EqualTo(cancellation.Token));
                }
            }
        }
        static ComplianceEvaluation Evaluate(string source, CompliancePolicy policy, OrSelection[]? entries = null, NoticeDelivery? delivery = null) {
            var context = new ComplianceContext { OrSelections = entries is null ? null : new(source, entries), Delivery = delivery };
            return ComplianceEngine.Evaluate(source, policy, context);
        }
        static void AssertEmpty(ComplianceEvaluation evaluation) {
            var actions = evaluation.GetActionSummary();
            Assert.That(actions.CurrentActions, Is.Empty);
            Assert.That(actions.BranchConditions, Is.Empty);
        }
        static void AssertAction(ComplianceAction action, ComplianceActionKind kind, ComplianceReason reason, int start, int length) {
            Assert.That(action.Kind, Is.EqualTo(kind));
            Assert.That(action.Reason, Is.EqualTo(reason));
            Assert.That(action.Span?.Start, Is.EqualTo(start));
            Assert.That(action.Span?.Length, Is.EqualTo(length));
        }
    }
}
