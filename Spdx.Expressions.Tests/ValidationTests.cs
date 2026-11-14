using System;
using System.Linq;
using NUnit.Framework;

namespace Spdx.Expressions.Tests {
    public class ValidationTests {
        [TestCase("mit", false, false, "Known", "MIT")]
        [TestCase("Unknown-1.0", true, false, "Unknown", null)]
        [TestCase("LLVM-exception", true, false, "WrongKind", null)]
        [TestCase("MIT WITH Apache-2.0", true, false, "Known,WrongKind", "MIT,")]
        [TestCase("MIT WITH Unknown-exception", true, false, "Known,Unknown", "MIT,")]
        [TestCase("LicenseRef-X", false, true, "", "")]
        [TestCase("LicenseRef-X WITH LLVM-exception", false, true, "Known", "LLVM-exception")]
        [TestCase("Unknown-1.0 OR LicenseRef-X", true, true, "Unknown", null)]
        [TestCase("GPL-2.0+", false, false, "Known", "GPL-2.0")]
        public void IndependentOutcomes(string input, bool errors, bool references, string statuses, string? canonical) {
            var result = SpdxExpressionValidator.Validate(GrammarTests.Parse(input), SpdxLicenseRegistry.Default);
            Assert.That(result.HasIdentifierErrors, Is.EqualTo(errors));
            Assert.That(result.HasUnresolvedReferences, Is.EqualTo(references));
            Assert.That(string.Join(',', result.IdentifierOccurrences.Select(x => x.LookupStatus)), Is.EqualTo(statuses));
            Assert.That(string.Join(',', result.IdentifierOccurrences.Select(x => x.CanonicalIdentifier)), Is.EqualTo(canonical ?? ""));
            foreach(var occurrence in result.IdentifierOccurrences.Where(x => x.LookupStatus != SpdxIdentifierLookupStatus.Known)) {
                Assert.That(occurrence.CanonicalIdentifier, Is.Null);
                Assert.That(occurrence.IsDeprecated, Is.Null);
            }
        }
        [TestCase(1, true)]
        [TestCase(2, true)]
        [TestCase(3, false)]
        [TestCase(4, false)]
        public void LateErrorsAndReferencesSurviveDiagnosticTruncation(int budget, bool truncated) {
            var expression = GrammarTests.Parse("GPL-2.0 OR GPL-2.0 OR Unknown-1.0 OR LicenseRef-X OR LicenseRef-X");
            var result = SpdxExpressionValidator.Validate(expression, SpdxLicenseRegistry.Default, new() { MaxDiagnostics = budget });
            Assert.That(result.HasIdentifierErrors, Is.True);
            Assert.That(result.HasUnresolvedReferences, Is.True);
            Assert.That(result.DiagnosticsTruncated, Is.EqualTo(truncated));
            Assert.That(result.IdentifierOccurrences.Select(x => x.Identifier), Is.EqualTo(new[] { "GPL-2.0", "GPL-2.0", "Unknown-1.0" }));
            Assert.That(result.References.Select(x => x.LicenseId), Is.EqualTo(new[] { "X", "X" }));
            Assert.That(result.IdentifierOccurrences.Select(x => x.Span.Start), Is.EqualTo(new[] { 0, 11, 22 }));
            Assert.That(result.References.Select(x => x.Span.Start), Is.EqualTo(new[] { 37, 53 }));
            Assert.That(result.Diagnostics, Has.Count.EqualTo(Math.Min(3, budget)));
            Assert.That(result.Diagnostics[0].Severity, Is.EqualTo(SpdxDiagnosticSeverity.Warning));
            Assert.That(result.Diagnostics[0].Code, Is.EqualTo("SPDX_ID_DEPRECATED"));
        }
        [TestCase("Unknown", "SPDX_ID_UNKNOWN_LICENSE")]
        [TestCase("MIT WITH Unknown", "SPDX_ID_UNKNOWN_EXCEPTION")]
        [TestCase("MIT WITH Apache-2.0", "SPDX_ID_WRONG_KIND")]
        [TestCase("GPL-2.0", "SPDX_ID_DEPRECATED")]
        public void StableDiagnosticCodes(string text, string code) {
            var result = SpdxExpressionValidator.Validate(GrammarTests.Parse(text), SpdxLicenseRegistry.Default);
            Assert.That(result.Diagnostics.Single().Code, Is.EqualTo(code));
        }
        [Test]
        public void MixedOccurrencesAreInSourceOrderAndUseExpectedNamespaces() {
            var result = SpdxExpressionValidator.Validate(GrammarTests.Parse("mit WITH llvm-exception OR DocumentRef-D:LicenseRef-X WITH LLVM-exception OR MIT"), SpdxLicenseRegistry.Default);
            Assert.That(result.IdentifierOccurrences.Select(x => x.Role), Is.EqualTo(new[] { SpdxIdentifierRole.License, SpdxIdentifierRole.Exception, SpdxIdentifierRole.Exception, SpdxIdentifierRole.License }));
            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(result.HasIdentifierErrors, Is.False);
            Assert.That(result.References.Single().DocumentId, Is.EqualTo("D"));
            Assert.That(result.IdentifierOccurrences.Select(x => x.Span.Start), Is.Ordered);
        }
    }
}
