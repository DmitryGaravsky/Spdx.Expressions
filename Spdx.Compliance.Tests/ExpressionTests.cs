using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using NUnit.Framework;
using Spdx.Expressions;
using static Spdx.Compliance.ComplianceDecision;
using static Spdx.Compliance.ComplianceReason;

namespace Spdx.Compliance.Tests {
    [TestFixture]
    public sealed class ExpressionTests {
        [TestCase(null, Unknown, FieldMissing)]
        [TestCase("", Invalid, FieldEmpty)]
        [TestCase(" ", Invalid, InvalidSyntax)]
        [TestCase(" NONE ", Review, FieldNone)]
        [TestCase("NOASSERTION", Unknown, FieldNoassertion)]
        [TestCase("MIT AND", Invalid, InvalidSyntax)]
        [TestCase("Example-Unknown", Review, UnknownId)]
        [TestCase("GPL-2.0", Review, DeprecatedId)]
        [TestCase("MIT+", Review, OrLaterUnlisted)]
        [TestCase("GPL-2.0+", Review, OrLaterUnlisted)]
        [TestCase("mit", Allow, ListedAllow)]
        [TestCase("GPL-3.0-only", Deny, ListedDeny)]
        [TestCase("GPL-2.0-only WITH Classpath-exception-2.0", Allow, PairAllowed)]
        [TestCase("GPL-2.0-only WITH LLVM-exception", Review, PairUnlisted)]
        [TestCase("MIT WITH LLVM-exception", Deny, PairDenied)]
        [TestCase("MIT AND Apache-2.0", Allow, AndAllAllowed)]
        [TestCase("MIT AND Example-Unknown AND GPL-3.0-only", Deny, AndDenied)]
        [TestCase("MIT AND Example-Unknown", Review, AndUnresolved)]
        [TestCase("MIT AND OFL-1.1", Conditional, AndConditional)]
        [TestCase("GPL-3.0-only OR GPL-3.0-only", Deny, OrAllDenied)]
        [TestCase("GPL-3.0-only OR Example-Unknown", Review, OrUnresolved)]
        [TestCase("LicenseRef-Example", Review, ReferenceUnresolved)]
        public void EvaluatesFieldAndOperands(string? source, ComplianceDecision decision, ComplianceReason reason) {
            var result = ComplianceEngine.Evaluate(source, Fixtures.Policy());
            Assert.That(result.SourceText, Is.EqualTo(source));
            Assert.That(result.Finding.Decision, Is.EqualTo(decision));
            Assert.That(result.Finding.Reason, Is.EqualTo(reason));
            Assert.That(result.IsComplete, Is.True);
        }
        [Test]
        public void RequiresUniqueChoiceAtOuterOrAndKeepsNestedAlternatives() {
            var policy = Fixtures.Policy();
            const string source = "(MIT OR (Apache-2.0 OR BSD-3-Clause))";
            var open = ComplianceEngine.Evaluate(source, policy);
            var selected = ComplianceEngine.Evaluate(source, policy, new() { SelectedBranch = "MIT" });
            var nested = ComplianceEngine.Evaluate(source, policy, new() { SelectedBranch = "(Apache-2.0 OR BSD-3-Clause)" });
            var outside = ComplianceEngine.Evaluate(source, policy, new() { SelectedBranch = "Apache-2.0" });
            var ambiguous = ComplianceEngine.Evaluate("MIT OR MIT", policy, new() { SelectedBranch = "MIT" });
            Assert.That(open.Finding.Reason, Is.EqualTo(ChoiceRequired));
            Assert.That(open.Finding.Children[0].Choices, Is.EqualTo(new[] { "MIT", "(Apache-2.0 OR BSD-3-Clause)" }));
            Assert.That(selected.Finding.Children[0].SelectedIndex, Is.EqualTo(0));
            Assert.That(selected.Finding.Children[0].Children[0].Span?.Start, Is.EqualTo(1));
            Assert.That(selected.Finding.Decision, Is.EqualTo(Allow));
            Assert.That(nested.Finding.Decision, Is.EqualTo(Conditional));
            Assert.That(nested.Finding.Children[0].Children[1].Children[0].Choices, Is.EqualTo(new[] { "Apache-2.0" }));
            Assert.That(outside.Finding.Reason, Is.EqualTo(SelectionMismatch));
            Assert.That(ambiguous.Finding.Reason, Is.EqualTo(SelectionMismatch));
        }
        [Test]
        public void ChecksPresenceInUnselectedAndNestedWithOperands() {
            const string source = "MIT OR (GPL-3.0-only WITH LLVM-exception AND Apache-2.0)";
            var context = new ComplianceContext { Profile = "ban", SelectedBranch = "MIT" };
            var result = ComplianceEngine.Evaluate(source, Fixtures.Policy(), context);
            Assert.That(result.Finding.Reason, Is.EqualTo(ProhibitedPresence));
            Assert.That(result.Finding.Operand, Is.EqualTo("GPL-3.0-only"));
            Assert.That(result.Finding.Span?.Start, Is.EqualTo(8));
            Assert.That(result.Finding.Children[0].SelectedIndex, Is.EqualTo(0));
        }
        [Test]
        public void ReportsSeparateSpansForWithOperands() {
            const string source = " GPL-2.0-only WITH Classpath-exception-2.0 ";
            var result = ComplianceEngine.Evaluate(source, Fixtures.Policy()).Finding;
            var license = result.LicenseSpan!.Value;
            var exception = result.ExceptionSpan!.Value;
            Assert.That(source.Substring(license.Start, license.Length), Is.EqualTo("GPL-2.0-only"));
            Assert.That(source.Substring(exception.Start, exception.Length), Is.EqualTo("Classpath-exception-2.0"));
        }
        [Test]
        public void RequiresMatchingDefinitionBytesAndDocument() {
            var policy = Fixtures.Policy();
            var definition = new ComplianceReferenceDefinition("A", "Example", "approved\n"u8);
            var other = new ComplianceReferenceDefinition("B", "Example", "approved\n"u8);
            var differentBytes = new ComplianceReferenceDefinition("A", "Example", "approved\r\n"u8);
            var references = new ComplianceReferenceEvidence(["A", "B", "C"], [definition, other], [new("C", "Foreign", "A")]);
            var unapproved = new ComplianceReferenceEvidence(["A"], [differentBytes], []);
            var resolved = ComplianceEngine.Evaluate("DocumentRef-Foreign:LicenseRef-Example", policy,
                new() { References = references, Document = "C" }).Finding;
            var wrongDocument = ComplianceEngine.Evaluate("LicenseRef-Example", policy,
                new() { References = references, Document = "B" }).Finding;
            var wrongBytes = ComplianceEngine.Evaluate("LicenseRef-Example", policy,
                new() { References = unapproved, Document = "A" }).Finding;
            var unknownAlias = ComplianceEngine.Evaluate("DocumentRef-Other:LicenseRef-Example", policy,
                new() { References = references, Document = "C" }).Finding;
            Assert.That(resolved.Reason, Is.EqualTo(ReferenceApproved));
            Assert.That(resolved.Reference, Is.EqualTo(new ReferenceFinding("C", "A", "LicenseRef-Example",
                SpdxReferenceResolutionStatus.Resolved, Fixtures.DefinitionDigest, true)));
            Assert.That(wrongDocument.Reason, Is.EqualTo(ReferenceUnapproved));
            Assert.That(wrongBytes.Reason, Is.EqualTo(ReferenceUnapproved));
            Assert.That(unknownAlias.Reason, Is.EqualTo(AliasUnknown));
        }
        [Test]
        public void RequiresNoticeDigestAndUsesInventoryCompletenessForAbsence() {
            var policy = Fixtures.Policy();
            var values = new Dictionary<string, string> { ["notes/NOTICE"] = Fixtures.NoticeDigest };
            var correct = new NoticeDelivery("zip", true, values);
            values["notes/NOTICE"] = Fixtures.DefinitionDigest;
            var wrong = new NoticeDelivery("zip", false, values);
            Assert.That(ComplianceEngine.Evaluate("OFL-1.1", policy, new() { Delivery = correct }).Finding.Reason,
                Is.EqualTo(NoticeVerified));
            Assert.That(ComplianceEngine.Evaluate("OFL-1.1", policy, new() { Delivery = wrong }).Finding.Reason,
                Is.EqualTo(NoticeMismatch));
            Assert.That(ComplianceEngine.Evaluate("OFL-1.1", policy, new() { Delivery = new("zip", true, []) }).Finding.Reason,
                Is.EqualTo(NoticeMissing));
            Assert.That(ComplianceEngine.Evaluate("OFL-1.1", policy, new() { Delivery = new("zip", false, []) }).Finding.Reason,
                Is.EqualTo(NoticeUnverified));
        }
        [Test]
        public void EnforcesExpressionBudgetAndCancellation() {
            var policy = Fixtures.Policy();
            var result = ComplianceEngine.Evaluate(new string(' ', 201), policy);
            Assert.That(result.IsComplete, Is.False);
            Assert.That(result.Finding.Reason, Is.EqualTo(LimitExceeded));
            using(var cancellation = new CancellationTokenSource()) {
                cancellation.Cancel();
                Assert.Throws<OperationCanceledException>(() =>
                    ComplianceEngine.Evaluate("MIT", policy, cancellationToken: cancellation.Token));
            }
        }
    }
    static class Fixtures {
        internal static readonly string DefinitionDigest = Convert.ToHexStringLower(SHA256.HashData("approved\n"u8));
        internal static readonly string NoticeDigest = Convert.ToHexStringLower(SHA256.HashData("notice\n"u8));
        internal static CompliancePolicy Policy(string? settings = null, Stream? catalog = null, ComplianceLimits? limits = null) {
            using(var stream = Json(settings ?? $$"""
                {"schemaVersion":1,"id":"policy-example","allow":["MIT","Apache-2.0"],"deny":["GPL-2.0-only","GPL-3.0-only"],
                 "allowExactPairs":["GPL-2.0-only WITH Classpath-exception-2.0"],"denyExactPairs":["MIT WITH LLVM-exception"],
                 "maxExpressionCharacters":200,"profiles":{"ban":["GPL-3.0-only"]},
                 "notice":{"license":"OFL-1.1","file":"notes/NOTICE","sha256":"{{NoticeDigest}}"},
                 "referenceApprovals":[{"document":"A","id":"LicenseRef-Example","sha256":"{{DefinitionDigest}}"}],
                 "namedLicenses":[{"names":["Example End User Terms","Example End-User Terms"],"owner":"Example"},
                                  {"names":["Example Permissive"],"license":"MIT"}]}
                """)) {
                return CompliancePolicy.Load(stream, catalog, limits);
            }
        }
        internal static MemoryStream Json(string text) {
            return new(Encoding.UTF8.GetBytes(text));
        }
    }
}
