using System.Collections.Generic;
using NUnit.Framework;
using static Spdx.Compliance.ComplianceDecision;
using static Spdx.Compliance.ComplianceReason;

namespace Spdx.Compliance.Tests {
    [TestFixture]
    public sealed class DecisionMatrixTests {
        static readonly string[] Licenses = ["MIT", "OFL-1.1", "BSD-3-Clause", "GPL-3.0-only"];
        static readonly string[] AndTable = ["ACRD", "CCRD", "RRRD", "DDDD"];
        static readonly string[] OpenOrTable = ["CCCC", "CCCC", "CCRR", "CCRD"];
        static readonly ComplianceReason[] SelectedReasons = [ChoiceAllowed, NoticeUnverified, NoPolicyRule, ChoiceDenied];
        static IEnumerable<TestCaseData> OrderedPairs() {
            for(int left = 0; left < Licenses.Length; left++) {
                for(int right = 0; right < Licenses.Length; right++) {
                    char and = AndTable[left][right];
                    char or = OpenOrTable[left][right];
                    yield return Case(left, right, "AND", null, and, AggregateReason("AND", and));
                    yield return Case(left, right, "OR", null, or, AggregateReason("OR", or));
                    yield return Case(left, right, "OR", 0, "ACRD"[left], SelectedReasons[left]);
                    yield return Case(left, right, "OR", 1, "ACRD"[right], SelectedReasons[right]);
                }
            }
        }
        static TestCaseData Case(int left, int right, string operation, int? selected, char outcome, ComplianceReason reason) {
            return new TestCaseData(Licenses[left], Licenses[right], operation, selected, Decision(outcome), reason)
                .SetName($"Matrix_{operation}_{left}_{right}_{selected?.ToString() ?? "open"}");
        }
        static ComplianceReason AggregateReason(string operation, char outcome) {
            return (operation, outcome) switch {
                ("AND", 'A') => AndAllAllowed,
                ("AND", 'C') => AndConditional,
                ("AND", 'R') => AndUnresolved,
                ("AND", 'D') => AndDenied,
                ("OR", 'C') => ChoiceRequired,
                ("OR", 'R') => OrUnresolved,
                ("OR", 'D') => OrAllDenied,
                _ => throw new AssertionException("Unknown matrix entry.")
            };
        }
        static ComplianceDecision Decision(char code) {
            return code switch {
                'A' => Allow,
                'C' => Conditional,
                'R' => Review,
                'D' => Deny,
                _ => throw new AssertionException("Unknown outcome.")
            };
        }
        [TestCaseSource(nameof(OrderedPairs))]
        public void MatchesRevisionOne(string left, string right, string operation, int? selected, ComplianceDecision outcome,
            ComplianceReason reason) {
            string expression = $"{left} {operation} {right}";
            ComplianceContext? context = selected is int index ? new() { OrSelections = new(expression, [new(0, expression.Length, index)]) } : null;
            var result = ComplianceEngine.Evaluate(expression, Fixtures.Policy(), context);
            Assert.That(result.Finding.Decision, Is.EqualTo(outcome));
            Assert.That(result.Finding.Reason, Is.EqualTo(reason));
            Assert.That(result.Finding.SelectedIndex, Is.EqualTo(selected));
            Assert.That(result.Finding.Children[0].Operand, Is.EqualTo(left));
            Assert.That(result.Finding.Children[1].Operand, Is.EqualTo(right));
        }
        [TestCase("MIT AND OFL-1.1 AND BSD-3-Clause AND GPL-3.0-only")]
        [TestCase("GPL-3.0-only AND (BSD-3-Clause AND MIT AND OFL-1.1)")]
        public void GroupingAndPermutationKeepTheMandatoryNotice(string expression) {
            var result = ComplianceEngine.Evaluate(expression, Fixtures.Policy());
            Assert.That(result.Finding.Reason, Is.EqualTo(AndDenied));
            Assert.That(result.GetActionSummary().CurrentActions[0].Reason, Is.EqualTo(NoticeUnverified));
        }
        [TestCase("BSD-3-Clause OR GPL-3.0-only OR MIT", 2)]
        [TestCase("MIT OR BSD-3-Clause OR GPL-3.0-only", 0)]
        public void NaryOrPermutationKeepsTheChoiceAtItsRemappedIndex(string expression, int selected) {
            var policy = Fixtures.Policy();
            var open = ComplianceEngine.Evaluate(expression, policy);
            var choice = open.GetActionSummary().CurrentActions[0];
            Assert.That(choice.Alternatives[0].OperandIndex, Is.EqualTo(selected));
            var result = ComplianceEngine.Evaluate(expression, policy, new() {
                OrSelections = new(expression, [new(0, expression.Length, selected)])
            });
            Assert.That(result.Finding.Reason, Is.EqualTo(ChoiceAllowed));
            Assert.That(result.Finding.SelectedIndex, Is.EqualTo(selected));
        }
    }
}
