using System;
using System.IO;
using System.Security.Cryptography;
using Spdx.Compliance;

namespace Spdx.Compliance.Examples {
    static class Program {
        const string NoticeExpression = "OFL-1.1 OR MIT";
        const string NoticeFile = "NOTICE.txt";
        static int Main() {
            using(var catalog = new MemoryStream("""
                [{"id":"sample-correction","artifactKind":"nuget","packageId":"Sample.Library","versionRange":"[1.0.0]",
                  "license":{"id":"BSD-3-Clause","url":"https://spdx.org/licenses/BSD-3-Clause.html"}}]
                """u8.ToArray())) {
                string digest = Convert.ToHexStringLower(SHA256.HashData(catalog));
                catalog.Position = 0;
                string noticeDigest = Convert.ToHexStringLower(SHA256.HashData("notice\n"u8));
                string settings = $$"""
                    {"schemaVersion":1,"id":"example","allow":["MIT","Apache-2.0"],
                     "allowExactPairs":["GPL-2.0-only WITH Classpath-exception-2.0"],
                     "notice":{"license":"OFL-1.1","file":"{{NoticeFile}}","sha256":"{{noticeDigest}}"},
                     "correctionProperties":{"id":"example.correction.id","source":"example.correction.source"},
                     "correctionCatalog":{"sha256":"{{digest}}","source":"Example catalog"} }
                    """;
                using(var policyStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(settings))) {
                    var policy = CompliancePolicy.Load(policyStream, catalog);
                    var undecided = ComplianceEngine.Evaluate("MIT OR Apache-2.0", policy);
                    var selected = ComplianceEngine.Evaluate("MIT OR Apache-2.0", policy, new() { SelectedBranch = "MIT" });
                    const string expression = "(MIT OR GPL-3.0-only) AND (Apache-2.0 OR (MIT OR BSD-3-Clause))";
                    var open = ComplianceEngine.Evaluate(expression, policy).Finding;
                    var left = open.Children[0].Children[0];
                    var right = open.Children[1].Children[0];
                    var nested = right.Children[1].Children[0];
                    var choices = new OrSelectionSet(expression, [
                        new(nested.Span!.Value, 0), new(right.Span!.Value, 1), new(left.Span!.Value, 0)
                    ]);
                    var selectedRoutes = ComplianceEngine.Evaluate(expression, policy, new() { OrSelections = choices });
                    var pair = ComplianceEngine.Evaluate("GPL-2.0-only WITH Classpath-exception-2.0", policy);
                    var reference = ComplianceEngine.Evaluate("LicenseRef-Custom", policy);
                    using(var document = new MemoryStream("""
                        {"bomFormat":"CycloneDX","specVersion":"1.6","components":[
                          {"type":"library","name":"Sample.Library","version":"1.0.0","bom-ref":"example",
                           "purl":"pkg:nuget/Sample.Library@1.0.0",
                           "properties":[{"name":"example.correction.id","value":"sample-correction"},
                                         {"name":"example.correction.source","value":"Example catalog"}],
                           "licenses":[{"license":{"id":"BSD-3-Clause","acknowledgement":"concluded",
                                                   "url":"https://spdx.org/licenses/BSD-3-Clause.html"}}]},
                          {"name":"First sample","licenses":[{"expression":"OFL-1.1 OR MIT"}]},
                          {"name":"Second sample","licenses":[{"expression":"OFL-1.1 OR MIT"}]}]}
                        """u8.ToArray())) {
                        var report = CycloneDxComplianceAnalyzer.Analyze(document, policy, contextForComponent: occurrence =>
                            occurrence.Pointer == "/components/1" ? new() { SelectedBranch = "OFL-1.1" } : null);
                        var corrected = report.Findings[0];
                        Console.WriteLine($"OR without a choice = {undecided.Finding.Decision}");
                        Console.WriteLine($"OR with MIT selected = {selected.Finding.Decision}");
                        Console.WriteLine($"Independent and nested OR choices = {selectedRoutes.Finding.Decision}");
                        Console.WriteLine($"Exact WITH pair = {pair.Finding.Decision}");
                        Console.WriteLine($"Unresolved reference = {reference.Finding.Reason}");
                        Console.WriteLine($"Correction = {corrected.Correction?.Status}, distribution = {corrected.Evaluation.Finding.Decision}");
                        bool noticeActionsPass = ShowNoticeActions(policy, noticeDigest);
                        var first = report.Findings[1].Evaluation.GetActionSummary().CurrentActions[0];
                        var second = report.Findings[2].Evaluation.GetActionSummary().CurrentActions[0];
                        Console.WriteLine($"SBOM actions by pointer = {first.Kind}, {second.Kind}");
                        return report.IsComplete && noticeActionsPass && first.Kind == ComplianceActionKind.VerifyNotice &&
                            second.Kind == ComplianceActionKind.RecordChoice && undecided.Finding.Decision == ComplianceDecision.Conditional &&
                            selected.Finding.Decision == ComplianceDecision.Allow && selectedRoutes.Finding.Decision == ComplianceDecision.Allow &&
                            pair.Finding.Decision == ComplianceDecision.Allow &&
                            reference.Finding.Reason == ComplianceReason.ReferenceUnresolved &&
                            corrected.Correction?.Status == CorrectionStatus.Verified &&
                            corrected.Evaluation.Finding.Reason == ComplianceReason.NoPolicyRule ? 0 : 1;
                    }
                }
            }
        }
        static bool ShowNoticeActions(CompliancePolicy policy, string noticeDigest) {
            var original = ComplianceEngine.Evaluate(NoticeExpression, policy);
            var open = original.GetActionSummary();
            var choice = open.CurrentActions[0];
            var spans = choice.Span!.Value;
            var ofl = new OrSelectionSet(NoticeExpression, [new(spans, 0)]);
            var mit = new OrSelectionSet(NoticeExpression, [new(spans, 1)]);
            var pending = ComplianceEngine.Evaluate(NoticeExpression, policy, new() { OrSelections = ofl }).GetActionSummary();
            var delivered = new NoticeDelivery("release", true, [new(NoticeFile, noticeDigest)]);
            var checkedNotice = ComplianceEngine.Evaluate(NoticeExpression, policy, new() { OrSelections = ofl, Delivery = delivered });
            var other = ComplianceEngine.Evaluate(NoticeExpression, policy, new() { OrSelections = mit });
            Console.WriteLine($"Open NOTICE route = {choice.Kind}, contingent {open.BranchConditions[0].Action.Kind}");
            Console.WriteLine($"OFL selected = {pending.CurrentActions[0].Kind}");
            Console.WriteLine($"NOTICE verified = {checkedNotice.Finding.Decision}, {checkedNotice.GetActionSummary().CurrentActions.Length} current actions");
            Console.WriteLine($"MIT selected = {other.GetActionSummary().CurrentActions.Length} current actions, inactive {other.Finding.Children[0].Reason}");
            return choice.Kind == ComplianceActionKind.RecordChoice && open.BranchConditions[0].Action.Kind == ComplianceActionKind.VerifyNotice &&
                pending.CurrentActions[0].Kind == ComplianceActionKind.VerifyNotice && checkedNotice.Finding.Decision == ComplianceDecision.Allow &&
                checkedNotice.GetActionSummary().CurrentActions.IsEmpty && other.GetActionSummary().CurrentActions.IsEmpty &&
                other.Finding.Children[0].Reason == ComplianceReason.NoticeUnverified;
        }
    }
}
