using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using NUnit.Framework;
using static Spdx.Compliance.ComplianceDecision;
using static Spdx.Compliance.ComplianceReason;

namespace Spdx.Compliance.Tests {
    [TestFixture]
    public sealed class CycloneDxTests {
        [Test]
        public void EnumeratesOccurrencesAndKeepsRootProductAndToolRoles() {
            const string source = """
                {"bomFormat":"CycloneDX","specVersion":"1.6","metadata":{
                  "component":{"bom-ref":"same","components":[{"bom-ref":"same"}]},
                  "tools":{"components":[{"components":[{}]}]}},
                  "components":[{"bom-ref":"same","licenses":[{"expression":"MIT OR Apache-2.0"}]},
                                {"licenses":[{"expression":"MIT"}],"evidence":{"licenses":[{"license":{"id":"mit"}}]}}]}
                """;
            using(var stream = Fixtures.Json(source)) {
                var report = CycloneDxComplianceAnalyzer.Analyze(stream, Fixtures.Policy(), contextForComponent: occurrence =>
                    occurrence.Pointer == "/components/0" ? new() { SelectedBranch = "MIT" } : null);
                Assert.That(stream.CanRead, Is.True);
                Assert.That(report.IsComplete, Is.True);
                Assert.That(report.ProductCount, Is.EqualTo(4));
                Assert.That(report.ToolCount, Is.EqualTo(2));
                Assert.That(report.Findings[1].Component, Is.EqualTo(new ComponentOccurrence("/metadata/component/components/0",
                    ComponentRole.Nested, "same", null, null, null, null)));
                Assert.That(report.Findings[2].Evaluation.Finding.Reason, Is.EqualTo(ChoiceAllowed));
                Assert.That(report.Findings[3].Evaluation.Finding.Decision, Is.EqualTo(Allow));
                Assert.That(report.Findings[5].Component.Pointer, Is.EqualTo("/metadata/tools/components/0/components/0"));
                Assert.That(report.Findings[5].Component.Role, Is.EqualTo(ComponentRole.Tool));
                Assert.That(report.Findings[3].ObservedLicenses[0].GetProperty("license").GetProperty("id").GetString(), Is.EqualTo("mit"));
            }
        }
        [TestCase(null, Unknown, FieldMissing)]
        [TestCase("[]", Unknown, NoLicenseEntries)]
        [TestCase("null", Invalid, WrongField)]
        [TestCase("[42]", Invalid, WrongField)]
        [TestCase("[{\"expression\":null}]", Invalid, WrongField)]
        [TestCase("[{\"expression\":\"MIT\",\"license\":{}}]", Invalid, WrongField)]
        [TestCase("[{\"license\":{\"id\":\"MIT\",\"name\":null}}]", Invalid, WrongField)]
        [TestCase("[{\"license\":{\"id\":\"LicenseRef-Example\"}}]", Invalid, WrongField)]
        [TestCase("[{\"license\":{\"name\":\"NOTICE.txt\"}}]", Review, FileOnly)]
        [TestCase("[{\"license\":{\"name\":\"Example Permissive\"}}]", Allow, ListedAllow)]
        [TestCase("[{\"license\":{\"name\":\"Other wording\"}}]", Review, NamedTerms)]
        [TestCase("[{\"expression\":\"MIT\"},{\"license\":{\"id\":\"Apache-2.0\"}}]", Review, MultipleEntries)]
        public void DistinguishesLicenseDeclarationShapes(string? declaration, ComplianceDecision decision, ComplianceReason reason) {
            string properties = declaration is null ? "" : $"\"licenses\":{declaration}";
            var result = AnalyzeComponent(properties, Fixtures.Policy()).Findings[0];
            Assert.That(result.Evaluation.Finding.Decision, Is.EqualTo(decision));
            Assert.That(result.Evaluation.Finding.Reason, Is.EqualTo(reason));
        }
        [Test]
        public void ConflictingObservationDoesNotHideADenial() {
            const string declaration = """
                "licenses":[{"expression":"GPL-3.0-only"}],"evidence":{"licenses":[{"license":{"id":"MIT"}}]}
                """;
            var result = AnalyzeComponent(declaration, Fixtures.Policy()).Findings[0].Evaluation.Finding;
            Assert.That(result.Decision, Is.EqualTo(Deny));
            Assert.That(result.ConflictingObservations, Is.EqualTo(new[] { "MIT" }));
        }
        [Test]
        public void NamedFileObservationDoesNotContradictConcludedTerms() {
            const string declaration = """
                "licenses":[{"license":{"name":"Example Terms","acknowledgement":"concluded"}}],
                "evidence":{"licenses":[{"license":{"name":"EXAMPLE_Terms_EN.txt","acknowledgement":"declared"}}]}
                """;
            var result = AnalyzeComponent(declaration, Fixtures.Policy()).Findings[0];
            Assert.That(result.Evaluation.Finding.Reason, Is.EqualTo(NamedTerms));
            Assert.That(result.ObservedLicenses.GetArrayLength(), Is.EqualTo(1));
        }
        [Test]
        public void OwnershipRequiresExactTrustedVersionAndOriginalAlias() {
            const string declaration = """
                "name":"Example","version":"1.2.3","purl":"pkg:nuget/Example@1.2.3",
                "licenses":[{"license":{"name":"Example End-User Terms"}}],
                "evidence":{"licenses":[{"license":{"name":"Example End User Terms"}}]}
                """;
            var options = new Dictionary<string, string> { ["pkg:nuget/Example@1.2.3"] = "Example" };
            var context = new ComplianceContext { Ownership = new(options) };
            options.Clear();
            var policy = Fixtures.Policy();
            var result = AnalyzeComponent(declaration, policy, context).Findings[0];
            var wrong = AnalyzeComponent(declaration.Replace("1.2.3", "1.2.4", StringComparison.Ordinal), policy, context).Findings[0];
            var spoof = AnalyzeComponent(declaration.Replace("\"version\":\"1.2.3\"", "\"version\":\"2.0.0\"", StringComparison.Ordinal),
                policy, context).Findings[0];
            var conflict = AnalyzeComponent(declaration.Replace("Example End User Terms", "Unrelated Terms", StringComparison.Ordinal), policy,
                context).Findings[0];
            Assert.That(result.Evaluation.Finding.Reason, Is.EqualTo(OwnershipConfirmed));
            Assert.That(result.Evaluation.SourceText, Is.EqualTo("Example End-User Terms"));
            Assert.That(wrong.Evaluation.Finding.Reason, Is.EqualTo(OwnershipUnconfirmed));
            Assert.That(spoof.Evaluation.Finding.Reason, Is.EqualTo(OwnershipUnconfirmed));
            Assert.That(conflict.Evaluation.Finding.Reason, Is.EqualTo(SourceConflict));
        }
        [Test]
        public void ReportsLimitsAndPartialStructure() {
            var policy = Fixtures.Policy();
            using(var stream = Fixtures.Json("""
                {"bomFormat":"CycloneDX","specVersion":"1.6","components":[{},null,{}],"metadata":{"tools":{"components":[{}]}}}
                """)) {
                var report = CycloneDxComplianceAnalyzer.Analyze(stream, policy, limits: new() { MaxFindings = 2 });
                Assert.That(report.IsComplete, Is.False);
                Assert.That(report.ProductCount, Is.EqualTo(2));
                Assert.That(report.ToolCount, Is.Zero);
                Assert.That(report.Issues[0].Code, Is.EqualTo(CycloneDxIssueCode.InvalidStructure));
                Assert.That(report.Issues[0].Pointer, Is.EqualTo("/components/1"));
                Assert.That(report.Issues[1].Code, Is.EqualTo(CycloneDxIssueCode.FindingLimit));
            }
            using(var stream = Fixtures.Json("""{"bomFormat":"CycloneDX","specVersion":"1.6","components":[{}]}""")) {
                var limits = new ComplianceLimits { MaxInputBytes = (int)stream.Length, MaxJsonDepth = 3, MaxFindings = 1 };
                var atLimit = CycloneDxComplianceAnalyzer.Analyze(stream, policy, limits: limits);
                stream.Position = 0;
                var depth = CycloneDxComplianceAnalyzer.Analyze(stream, policy, limits: new() { MaxJsonDepth = 2 });
                stream.Position = 0;
                var bytes = CycloneDxComplianceAnalyzer.Analyze(stream, policy, limits: new() { MaxInputBytes = (int)stream.Length - 1 });
                Assert.That(atLimit.IsComplete, Is.True);
                Assert.That(depth.Issues[0].Code, Is.EqualTo(CycloneDxIssueCode.DepthLimit));
                Assert.That(bytes.Issues[0].Code, Is.EqualTo(CycloneDxIssueCode.InputLimit));
            }
        }
        [Test]
        public void ReportsExpressionLimitAtDocumentLevel() {
            string declaration = $$"""
                "licenses":[{"expression":"{{new string(' ', 201)}}"}]
                """;
            var report = AnalyzeComponent(declaration, Fixtures.Policy());
            Assert.That(report.IsComplete, Is.False);
            Assert.That(report.Findings[0].Evaluation.Finding.Reason, Is.EqualTo(LimitExceeded));
        }
        [TestCase("""{"bomFormat":"CycloneDX","specVersion":"1.6","components":[],"components":[{}]}""", CycloneDxIssueCode.InvalidJson)]
        [TestCase("""{"bomFormat":"CycloneDX","specVersion":"1.5"}""", CycloneDxIssueCode.UnsupportedDocument)]
        [TestCase("""{"bomFormat":"CycloneDX","specVersion":"1.6","x":"\uDC00"}""", CycloneDxIssueCode.InvalidJson)]
        public void RejectsAmbiguousOrUnsupportedDocuments(string json, CycloneDxIssueCode expected) {
            using(var stream = Fixtures.Json(json)) {
                var report = CycloneDxComplianceAnalyzer.Analyze(stream, Fixtures.Policy());
                Assert.That(report.IsComplete, Is.False);
                Assert.That(report.Issues[0].Code, Is.EqualTo(expected));
            }
        }
        [Test]
        public void ObservesCancellationAfterComponentCallback() {
            using(var stream = Fixtures.Json("""{"bomFormat":"CycloneDX","specVersion":"1.6","components":[{}]}"""))
            using(var cancellation = new CancellationTokenSource()) {
                var error = Assert.Throws<OperationCanceledException>(() => CycloneDxComplianceAnalyzer.Analyze(stream,
                    Fixtures.Policy(), contextForComponent: _ => {
                        cancellation.Cancel();
                        return null;
                    }, cancellationToken: cancellation.Token));
                Assert.That(error!.CancellationToken, Is.EqualTo(cancellation.Token));
                Assert.That(stream.CanRead, Is.True);
            }
        }
        internal static CycloneDxReport AnalyzeComponent(string properties, CompliancePolicy policy, ComplianceContext? context = null) {
            using(var stream = Fixtures.Json("{\"bomFormat\":\"CycloneDX\",\"specVersion\":\"1.6\",\"components\":[{" + properties + "}]}")) {
                return CycloneDxComplianceAnalyzer.Analyze(stream, policy, context);
            }
        }
    }
}
