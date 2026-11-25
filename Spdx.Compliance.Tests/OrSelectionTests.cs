using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using NUnit.Framework;
using Spdx.Expressions;
using static Spdx.Compliance.ComplianceDecision;
using static Spdx.Compliance.ComplianceReason;

namespace Spdx.Compliance.Tests {
    [TestFixture]
    public sealed class OrSelectionTests {
        const string Independent = "(MIT OR GPL-3.0-only) AND (Apache-2.0 OR OFL-1.1)";
        const string Nested = "MIT OR (Apache-2.0 OR GPL-3.0-only)";
        [Test]
        public void AppliesIndependentChoicesAndRetainsInactiveNotice() {
            var policy = Fixtures.Policy();
            var result = Evaluate(Independent, policy, Independent, [new(27, 21, 0), new(1, 19, 0)]);
            var partial = Evaluate(Independent, policy, Independent, [new(1, 19, 0)]);
            Assert.That(result.Reason, Is.EqualTo(AndAllAllowed));
            Assert.That(result.Children[0].Children[0].SelectedIndex, Is.Zero);
            var right = result.Children[1].Children[0];
            Assert.That(right.Kind, Is.EqualTo(SpdxExpressionKind.Or));
            Assert.That(right.Span?.Start, Is.EqualTo(27));
            Assert.That(right.Span?.Length, Is.EqualTo(21));
            Assert.That(right.SelectedIndex, Is.Zero);
            Assert.That(right.Children[1].Reason, Is.EqualTo(NoticeUnverified));
            Assert.That(right.Children[1].Notice!.File, Is.EqualTo("notes/NOTICE"));
            Assert.That(partial.Reason, Is.EqualTo(AndConditional));
            Assert.That(partial.Children[1].Children[0].Reason, Is.EqualTo(ChoiceRequired));
        }
        [TestCase(true)]
        [TestCase(false)]
        public void BindsNestedChoicesRegardlessOfOrder(bool innerFirst) {
            var outer = new OrSelection(0, 35, 1);
            var inner = new OrSelection(8, 26, 0);
            var policy = Fixtures.Policy();
            var result = Evaluate(Nested, policy, Nested, innerFirst ? [inner, outer] : [outer, inner]);
            var partial = Evaluate(Nested, policy, Nested, [outer]);
            Assert.That(result.Decision, Is.EqualTo(Allow));
            Assert.That(result.SelectedIndex, Is.EqualTo(1));
            Assert.That(result.Children[1].Children[0].SelectedIndex, Is.Zero);
            Assert.That(partial.Decision, Is.EqualTo(Conditional));
            Assert.That(partial.SelectedIndex, Is.EqualTo(1));
            Assert.That(partial.Children[1].Children[0].SelectedIndex, Is.Null);
        }
        [Test]
        public void UsesFullChildIndexForDeniedAndRepeatedOperands() {
            var policy = Fixtures.Policy();
            var repeated = Evaluate("MIT OR MIT", policy, "MIT OR MIT", [new(0, 10, 1)]);
            var denied = Evaluate(Nested, policy, Nested, [new(0, 35, 1), new(8, 26, 1)]);
            Assert.That(repeated.Reason, Is.EqualTo(ChoiceAllowed));
            Assert.That(repeated.SelectedIndex, Is.EqualTo(1));
            Assert.That(repeated.Children[0].Span?.Start, Is.Zero);
            Assert.That(repeated.Children[1].Span?.Start, Is.EqualTo(7));
            Assert.That(denied.Reason, Is.EqualTo(ChoiceDenied));
            Assert.That(denied.Children[1].Children[0].SelectedIndex, Is.EqualTo(1));
        }
        [TestCase(0, 49, 0)]
        [TestCase(0, 21, 0)]
        [TestCase(27, 20, 0)]
        [TestCase(27, 21, -1)]
        [TestCase(27, 21, 2)]
        [TestCase(-1, 19, 0)]
        [TestCase(1, 19, 0)]
        [TestCase(1, 19, 1)]
        public void RejectsWrongSpansInvalidIndicesAndDuplicatesAtomically(int start, int length, int index) {
            var result = Evaluate(Independent, Fixtures.Policy(), Independent, [new(1, 19, 0), new(start, length, index)]);
            AssertRejected(result);
            Assert.That(result.Children[0].Children[0].Children[0].Reason, Is.EqualTo(ChoiceRequired));
        }
        [Test]
        public void RejectsStaleMixedAndInactiveChoices() {
            var policy = Fixtures.Policy();
            AssertRejected(Evaluate(Nested, policy, Nested, [new(8, 26, 0)]));
            AssertRejected(Evaluate(Nested, policy, Nested, [new(8, 26, 0), new(0, 35, 0)]));
            AssertRejected(Evaluate(Nested, policy, " " + Nested, [new(0, 35, 0)]));
            AssertRejected(Evaluate(Nested, policy, Nested.Replace("MIT", "mit", StringComparison.Ordinal), [new(0, 35, 0)]));
            AssertRejected(ComplianceEngine.Evaluate(Nested, policy,
                new() { SelectedBranch = "MIT", OrSelections = new(Nested, []) }).Finding);
        }
        [Test]
        public void PresenceBanKeepsTheValidOrRejectedBatchFinding() {
            var policy = Fixtures.Policy();
            var rejected = Evaluate(Nested, policy, Nested, [new(8, 26, 0)], "ban");
            var selected = Evaluate(Nested, policy, Nested, [new(0, 35, 0)], "ban");
            Assert.That(rejected.Decision, Is.EqualTo(Deny));
            Assert.That(rejected.Reason, Is.EqualTo(ProhibitedPresence));
            Assert.That(rejected.Operand, Is.EqualTo("GPL-3.0-only"));
            AssertRejected(rejected.Children[0]);
            Assert.That(selected.Reason, Is.EqualTo(ProhibitedPresence));
            Assert.That(selected.Children[0].Reason, Is.EqualTo(ChoiceAllowed));
            Assert.That(selected.Children[0].SelectedIndex, Is.Zero);
        }
        [Test]
        public void ReusesImmutableSnapshotWithNewDeliveryEvidence() {
            var policy = Fixtures.Policy();
            var entries = new List<OrSelection> { new(1, 19, 0), new(27, 21, 1) };
            var choices = new OrSelectionSet(Independent, entries);
            entries.Clear();
            var open = ComplianceEngine.Evaluate(Independent, policy, new() { OrSelections = choices });
            var delivery = new NoticeDelivery("sample", true, [new("notes/NOTICE", Fixtures.NoticeDigest)]);
            var verified = ComplianceEngine.Evaluate(Independent, policy, new() { OrSelections = choices, Delivery = delivery });
            Assert.That(open.Finding.Reason, Is.EqualTo(AndConditional));
            Assert.That(verified.Finding.Reason, Is.EqualTo(AndAllAllowed));
        }
        [Test]
        public void BoundsSnapshotAndObservesCancellation() {
            var oversized = Enumerable.Repeat(new OrSelection(0, 10, 0), OrSelectionSet.MaxEntries + 1);
            Assert.Throws<ArgumentOutOfRangeException>(() => new OrSelectionSet("MIT OR MIT", oversized));
            using(var source = new CancellationTokenSource()) {
                source.Cancel();
                var error = Assert.Throws<OperationCanceledException>(() => new OrSelectionSet(Nested, [], source.Token));
                Assert.That(error!.CancellationToken, Is.EqualTo(source.Token));
            }
        }
        [Test]
        public void AppliesSetsOnlyToExplicitSbomOccurrences() {
            string component = JsonSerializer.Serialize(new { licenses = new[] { new { expression = Nested } } });
            using(var sbom = Fixtures.Json($$"""
                {"bomFormat":"CycloneDX","specVersion":"1.6","components":[{{component}},{{component}},{{component}},{{component}}]}
                """)) {
                var policy = Fixtures.Policy();
                var report = CycloneDxComplianceAnalyzer.Analyze(sbom, policy, contextForComponent: c => c.Pointer switch {
                    "/components/0" => new() { OrSelections = new(Nested, [new(0, 35, 1), new(8, 26, 0)]) },
                    "/components/1" => new() { OrSelections = new(Nested, [new(0, 35, 1), new(8, 26, 1)]) },
                    "/components/3" => new() { OrSelections = new(Nested, [new(8, 26, 1)]) },
                    _ => null
                });
                Assert.That(report.IsComplete, Is.True);
                Assert.That(report.Findings.Select(c => c.Evaluation.Finding.Decision), Is.EqualTo(new[] { Allow, Deny, Conditional, Invalid }));
                Assert.That(report.Findings[0].Evaluation.Finding.Children[1].Children[0].SelectedIndex, Is.Zero);
                Assert.That(report.Findings[1].Evaluation.Finding.Children[1].Children[0].SelectedIndex, Is.EqualTo(1));
                Assert.That(report.Findings[2].Evaluation.Finding.SelectedIndex, Is.Null);
                AssertRejected(report.Findings[3].Evaluation.Finding);
                sbom.Position = 0;
                Assert.Throws<ArgumentException>(() => CycloneDxComplianceAnalyzer.Analyze(sbom, policy,
                    new() { OrSelections = new(Nested, []) }));
                Assert.That(sbom.Position, Is.Zero);
            }
        }
        static ComplianceFinding Evaluate(string actual, CompliancePolicy policy, string bound, OrSelection[] entries, string profile = "default") {
            return ComplianceEngine.Evaluate(actual, policy, new() { OrSelections = new(bound, entries), Profile = profile }).Finding;
        }
        static void AssertRejected(ComplianceFinding finding) {
            Assert.That(finding.Decision, Is.EqualTo(Invalid));
            Assert.That(finding.Reason, Is.EqualTo(SelectionMismatch));
            AssertUnselected(finding);
        }
        static void AssertUnselected(ComplianceFinding finding) {
            Assert.That(finding.SelectedIndex, Is.Null);
            foreach(var child in finding.Children)
                AssertUnselected(child);
        }
    }
}
