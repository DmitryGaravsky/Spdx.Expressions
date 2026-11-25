using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using NUnit.Framework;
using static Spdx.Compliance.ComplianceDecision;
using static Spdx.Compliance.ComplianceReason;

namespace Spdx.Compliance.Tests {
    [TestFixture]
    public sealed class PolicyTests {
        const string Catalog = """
            [{"id":"first","artifactKind":"nuget","packageSelectors":[{"packageId":"Example","versionRange":"(1.0.0,2.0.0)"}],
              "license":{"id":"BSD-3-Clause","url":"https://example.invalid/license"}},
             {"id":"pin","artifactKind":"nuget","packageId":"Example","versionRange":"[1.1.0-beta.2]",
              "license":{"name":"Example End-User Terms","url":"https://example.invalid/license"}},
             {"id":"later","artifactKind":"nuget","packageId":"example","versionRange":"[1.0.0,2.0.0]",
              "license":{"id":"MIT","url":"https://example.invalid/license"}},
             {"id":"other-kind","artifactKind":"vsix-host-assembly"}]
            """;
        [TestCase("""{"schemaVersion":2,"id":"x"}""")]
        [TestCase("""{"schemaVersion":1,"id":"x","allow":["MIT","mit"]}""")]
        [TestCase("""{"schemaVersion":1,"id":"x","allow":["MIT"],"deny":["mit"]}""")]
        [TestCase("""{"schemaVersion":1,"id":"x","deny":["Not-A-Listed-Id"]}""")]
        [TestCase("""{"schemaVersion":1,"id":"x","allowExactPairs":["MIT WITH MIT"]}""")]
        [TestCase("""{"schemaVersion":1,"id":"x","namedLicenses":[{"names":["Name"],"owner":"Owner","license":"MIT"}]}""")]
        [TestCase("""{"schemaVersion":1,"id":"x","namedLicenses":[{"names":["Name"]},{"names":["Name"]}]}""")]
        [TestCase("""{"schemaVersion":1,"id":"x","correctionProperties":{"id":"same","source":"same"}}""")]
        [TestCase("""{"schemaVersion":1,"id":"x","profiles":{"unknown":null}}""")]
        [TestCase("""{"schemaVersion":1,"id":"x","allow":[],"allow":["MIT"]}""")]
        [TestCase("""{"schemaVersion":1,"id":"x","allows":["MIT"]}""")]
        public void RejectsInvalidAndAmbiguousPolicy(string json) {
            var exception = Assert.Throws<CompliancePolicyException>(() => Fixtures.Policy(json));
            Assert.That(exception!.Error, Is.EqualTo(CompliancePolicyError.InvalidPolicy));
        }
        [Test]
        public void HashesTheExactBytesFromCurrentStreamPosition() {
            const string json = """{"schemaVersion":1,"id":"Example"}""";
            var bytes = Encoding.UTF8.GetBytes("\uFEFF" + json);
            using(var stream = new MemoryStream([0, .. bytes])) {
                stream.Position = 1;
                var policy = CompliancePolicy.Load(stream, limits: new() { MaxInputBytes = bytes.Length });
                Assert.That(policy.Identity.Sha256, Is.EqualTo(Convert.ToHexStringLower(SHA256.HashData(bytes))));
                Assert.That(stream.CanRead, Is.True);
            }
        }
        [Test]
        public void RejectsCatalogDigestMismatchBeforeCatalogParsing() {
            using(var stream = Fixtures.Json("Not JSON")) {
                var exception = Assert.Throws<CompliancePolicyException>(() => Fixtures.Policy(Settings(new string('a', 64)), stream));
                Assert.That(exception!.Error, Is.EqualTo(CompliancePolicyError.CatalogPinMismatch));
                Assert.That(stream.CanRead, Is.True);
            }
        }
        [TestCase("""[{"id":"duplicate","artifactKind":"vsix"},{"id":"duplicate","artifactKind":"vsix"}]""")]
        [TestCase("""[{"id":"missing","artifactKind":"nuget","license":{"id":"MIT"},"packageId":"Example","versionRange":"[1.0.0]"}]""")]
        [TestCase("""[{"id":"misplaced","artifactKind":"nuget","license":{"id":"MIT","url":"https://example.invalid/"},"packageId":"Example","versionRange":"(1.0.0,1.0.0]"}]""")]
        public void RejectsInvalidPinnedCatalog(string catalog) {
            using(var stream = Fixtures.Json(catalog)) {
                var exception = Assert.Throws<CompliancePolicyException>(() => Fixtures.Policy(Settings(Hash(catalog)), stream));
                Assert.That(exception!.Error, Is.EqualTo(CompliancePolicyError.InvalidCatalog));
            }
        }
        [TestCase("first", "1.1.0-beta.1", "id", "BSD-3-Clause", CorrectionStatus.Verified, Review, NoPolicyRule)]
        [TestCase("first", "1.2.0", "id", "BSD-3-Clause", CorrectionStatus.Verified, Review, NoPolicyRule)]
        [TestCase("first", "1.1.0-beta.2", "id", "BSD-3-Clause", CorrectionStatus.SelectorMismatch, Review, CorrectionMismatch)]
        [TestCase("pin", "1.1.0-beta.2", "name", "Example End-User Terms", CorrectionStatus.Verified, Review, NamedTerms)]
        [TestCase("first", "1.0.0", "id", "BSD-3-Clause", CorrectionStatus.SelectorMismatch, Review, CorrectionMismatch)]
        [TestCase("first", "2.0.0", "id", "BSD-3-Clause", CorrectionStatus.SelectorMismatch, Review, CorrectionMismatch)]
        [TestCase("later", "1.0.0", "id", "MIT", CorrectionStatus.Verified, Allow, ListedAllow)]
        [TestCase("later", "1.2.0", "id", "MIT", CorrectionStatus.SelectorMismatch, Review, CorrectionMismatch)]
        [TestCase("other-kind", "1.2.0", "id", "GPL-3.0-only", CorrectionStatus.WrongArtifactKind, Deny, ListedDeny)]
        [TestCase("first", "1.2.0", "id", "GPL-3.0-only", CorrectionStatus.LicenseMismatch, Deny, ListedDeny)]
        [TestCase("first", "1.2.0", "id", "LicenseRef-X", CorrectionStatus.LicenseMismatch, Invalid, WrongField)]
        public void VerifiesCorrectionSeparatelyFromDistribution(string claim, string version, string slot, string terms,
            CorrectionStatus correction, ComplianceDecision decision, ComplianceReason reason) {
            string text = ClaimedComponent(claim, version, slot, terms);
            var result = CycloneDxTests.AnalyzeComponent(text, PinnedPolicy()).Findings[0];
            Assert.That(result.Correction!.Status, Is.EqualTo(correction));
            Assert.That(result.Correction.Id, Is.EqualTo(claim));
            Assert.That(result.Correction.CatalogSha256, Is.EqualTo(Hash(Catalog)));
            Assert.That(result.Evaluation.Finding.Decision, Is.EqualTo(decision));
            Assert.That(result.Evaluation.Finding.Reason, Is.EqualTo(reason));
        }
        [TestCase("\"name\":\"sample.source\",\"value\":\"Sample\"", "\"name\":\"sample.id\",\"value\":\"first\"", CorrectionStatus.InvalidProvenance)]
        [TestCase("\"name\":\"sample.source\",\"value\":\"Sample\"", "\"name\":\"sample.source\",\"value\":42", CorrectionStatus.InvalidProvenance)]
        [TestCase("pkg:nuget/Example@1.2.0", "pkg:nuget/Other@1.2.0", CorrectionStatus.PackageIdentityMismatch)]
        [TestCase("pkg:nuget/Example@1.2.0", "pkg:nuget/Example@1.1.0", CorrectionStatus.PackageIdentityMismatch)]
        [TestCase("concluded", "declared", CorrectionStatus.LicenseMismatch)]
        [TestCase("https://example.invalid/license", "https://example.invalid/other", CorrectionStatus.LicenseMismatch)]
        public void ChecksProvenanceIdentityAndExactConcludedLicense(string original, string altered, CorrectionStatus status) {
            string text = ClaimedComponent("first", "1.2.0", "id", "BSD-3-Clause");
            var result = CycloneDxTests.AnalyzeComponent(text.Replace(original, altered, StringComparison.Ordinal),
                PinnedPolicy()).Findings[0];
            Assert.That(result.Correction!.Status, Is.EqualTo(status));
            Assert.That(result.Evaluation.Finding.Reason, Is.EqualTo(CorrectionMismatch));
        }
        [Test]
        public void UsesOrdinaryRulesWhenUnmarkedOrWithoutCatalog() {
            var unmarked = CycloneDxTests.AnalyzeComponent("\"licenses\":[{\"expression\":\"MIT\"}]", PinnedPolicy()).Findings[0];
            const string settings = """
                {"schemaVersion":1,"id":"sample","allow":["MIT"],
                 "correctionProperties":{"id":"sample.id","source":"sample.source"}}
                """;
            var unverified = CycloneDxTests.AnalyzeComponent(ClaimedComponent("first", "1.2.0", "id", "MIT"),
                Fixtures.Policy(settings)).Findings[0];
            Assert.That(unmarked.Correction, Is.Null);
            Assert.That(unmarked.Evaluation.Finding.Decision, Is.EqualTo(Allow));
            Assert.That(unverified.Correction!.Status, Is.EqualTo(CorrectionStatus.Unverified));
            Assert.That(unverified.Evaluation.Finding.Decision, Is.EqualTo(Allow));
        }
        [Test]
        public void PropagatesCancellationAndDoesNotDisposePolicyStream() {
            using(var cancellation = new CancellationTokenSource())
            using(var stream = Fixtures.Json("""{"schemaVersion":1,"id":"sample"}""")) {
                cancellation.Cancel();
                Assert.Throws<OperationCanceledException>(() => CompliancePolicy.Load(stream, cancellationToken: cancellation.Token));
                Assert.That(stream.CanRead, Is.True);
                Assert.That(stream.Position, Is.Zero);
            }
        }
        [Test]
        public void ReadsShortChunksFromNonSeekableStreamAndProbesOnlyOneByte() {
            byte[] content = """{"schemaVersion":1,"id":"sample"}"""u8.ToArray();
            using(var stream = new ShortStream(content, default)) {
                var policy = CompliancePolicy.Load(stream, limits: new() { MaxInputBytes = content.Length });
                Assert.That(policy.Identity.Id, Is.EqualTo("sample"));
                Assert.That(stream.CanRead, Is.True);
            }
            using(var stream = new ShortStream([.. content, 0, 0], default)) {
                var exception = Assert.Throws<CompliancePolicyException>(() =>
                    CompliancePolicy.Load(stream, limits: new() { MaxInputBytes = content.Length }));
                Assert.That(exception!.Error, Is.EqualTo(CompliancePolicyError.InputLimit));
                Assert.That(stream.BytesRead, Is.EqualTo(content.Length + 1));
            }
        }
        [Test]
        public void ObservesCancellationDuringStreamAcquisition() {
            using(var cancellation = new CancellationTokenSource())
            using(var stream = new ShortStream("""{"schemaVersion":1,"id":"sample"}"""u8.ToArray(), cancellation.Cancel)) {
                var exception = Assert.Throws<OperationCanceledException>(() =>
                    CompliancePolicy.Load(stream, cancellationToken: cancellation.Token));
                Assert.That(exception!.CancellationToken, Is.EqualTo(cancellation.Token));
                Assert.That(stream.BytesRead, Is.EqualTo(3));
                Assert.That(stream.CanRead, Is.True);
            }
        }
        sealed class ShortStream(byte[] bytes, Action? afterRead) : MemoryStream(bytes) {
            public override bool CanSeek => false;
            public override long Length => throw new NotSupportedException();
            public override long Position {
                get { throw new NotSupportedException(); }
                set { throw new NotSupportedException(); }
            }
            internal int BytesRead => (int)base.Position;
            public override int Read(Span<byte> buffer) {
                int read = base.Read(buffer[..Math.Min(buffer.Length, 3)]);
                afterRead?.Invoke();
                return read;
            }
        }
        static CompliancePolicy PinnedPolicy() {
            using(var stream = Fixtures.Json(Catalog)) {
                return Fixtures.Policy(Settings(Hash(Catalog)), stream);
            }
        }
        static string Hash(string text) {
            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        }
        static string Settings(string digest) {
            return $$"""
                {"schemaVersion":1,"id":"sample","allow":["MIT"],"deny":["GPL-3.0-only"],
                 "correctionProperties":{"id":"sample.id","source":"sample.source"},
                 "correctionCatalog":{"sha256":"{{digest}}","source":"Sample"} }
                """;
        }
        static string ClaimedComponent(string claim, string version, string slot, string terms) {
            return $$"""
                "type":"library","name":"Example","version":"{{version}}","purl":"pkg:nuget/Example@{{version}}",
                "properties":[{"name":"sample.id","value":"{{claim}}"},{"name":"sample.source","value":"Sample"}],
                "licenses":[{"license":{"{{slot}}":"{{terms}}","url":"https://example.invalid/license","acknowledgement":"concluded"} }]
                """;
        }
    }
}
