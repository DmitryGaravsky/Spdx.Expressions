using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Spdx.Expressions.Tests {
    public class CanonicalFormattingTests {
        const string MixedCatalogueInput = " mit+ WITH llvm-exception OR (aCmE.1 WITH MiT AND apache-2.0 OR LLVM-EXCEPTION+ OR LicenseRef-mit WITH OLD-EXC) ";
        const string MixedCatalogueA = "mIt+ WITH lLvM-Exception OR (AcMe.1 WITH MiT AND apache-2.0 OR LLVM-EXCEPTION+ OR LicenseRef-mit WITH oLd-Exc)";
        const string MixedCatalogueB = "MiT+ WITH LLvm-exCEPTION OR (AcMe.1 WITH MiT AND apache-2.0 OR LLVM-EXCEPTION+ OR LicenseRef-mit WITH oLd-Exc)";
        const string MixedDefault = "MIT+ WITH LLVM-exception OR (aCmE.1 WITH MiT AND Apache-2.0 OR LLVM-EXCEPTION+ OR LicenseRef-mit WITH OLD-EXC)";
        [TestCase("G01", "Default", " mit  OR apache-2.0 ", "mit OR apache-2.0", "MIT OR Apache-2.0")]
        [TestCase("G02", "Default", " \t((gpl-2.0+\tWITH nokia-QT-exception-1.1))OR(ecos-2.0 AND miT AND mit) \r\n", "((gpl-2.0+ WITH nokia-QT-exception-1.1)) OR (ecos-2.0 AND miT AND mit)", "((GPL-2.0+ WITH Nokia-Qt-exception-1.1)) OR (eCos-2.0 AND MIT AND MIT)")]
        [TestCase("G03", "Default", "my-LiC+ WITH fake-EXc OR mit WITH other-EXC", "my-LiC+ WITH fake-EXc OR mit WITH other-EXC", "my-LiC+ WITH fake-EXc OR MIT WITH other-EXC")]
        [TestCase("G04", "Default", "llvm-EXCEPTION+ OR mit WITH apache-2.0", "llvm-EXCEPTION+ OR mit WITH apache-2.0", "llvm-EXCEPTION+ OR MIT WITH apache-2.0")]
        [TestCase("G05", "Default", " LicenseRef-miT WITH llvm-EXCEPTION AND DocumentRef-aBc:LicenseRef-llvM-exception ", "LicenseRef-miT WITH llvm-EXCEPTION AND DocumentRef-aBc:LicenseRef-llvM-exception", "LicenseRef-miT WITH LLVM-exception AND DocumentRef-aBc:LicenseRef-llvM-exception")]
        [TestCase("G06", "Default", "licenseref-MiT OR documentref-d OR none OR and", "licenseref-MiT OR documentref-d OR none OR and", "licenseref-MiT OR documentref-d OR none OR and")]
        [TestCase("G07", "Default", "isc WITH independent-modules-exception OR apache-2.0", "isc WITH independent-modules-exception OR apache-2.0", "ISC WITH Independent-modules-exception OR Apache-2.0")]
        [TestCase("G08", "Default", " \t(gpl-2.0+\tWITH llvm-exception)OR(LicenseRef-miT) ", "(gpl-2.0+ WITH llvm-exception) OR (LicenseRef-miT)", "(GPL-2.0+ WITH LLVM-exception) OR (LicenseRef-miT)")]
        [TestCase("G09", "Default", "(mit)OR(isc)", "(mit) OR (isc)", "(MIT) OR (ISC)")]
        [TestCase("G10", "Default", "mit OR gpl-2.0+ WITH llvm-exception AND apache-2.0 OR isc", "mit OR gpl-2.0+ WITH llvm-exception AND apache-2.0 OR isc", "MIT OR GPL-2.0+ WITH LLVM-exception AND Apache-2.0 OR ISC")]
        [TestCase("G11A", "A", " mit+ WITH llvm-exception OR (aCmE.1 WITH MiT AND apache-2.0 OR LLVM-EXCEPTION+ OR LicenseRef-mit WITH OLD-EXC) ", "mit+ WITH llvm-exception OR (aCmE.1 WITH MiT AND apache-2.0 OR LLVM-EXCEPTION+ OR LicenseRef-mit WITH OLD-EXC)", "mIt+ WITH lLvM-Exception OR (AcMe.1 WITH MiT AND apache-2.0 OR LLVM-EXCEPTION+ OR LicenseRef-mit WITH oLd-Exc)")]
        [TestCase("G11B", "B", " mit+ WITH llvm-exception OR (aCmE.1 WITH MiT AND apache-2.0 OR LLVM-EXCEPTION+ OR LicenseRef-mit WITH OLD-EXC) ", "mit+ WITH llvm-exception OR (aCmE.1 WITH MiT AND apache-2.0 OR LLVM-EXCEPTION+ OR LicenseRef-mit WITH OLD-EXC)", "MiT+ WITH LLvm-exCEPTION OR (AcMe.1 WITH MiT AND apache-2.0 OR LLVM-EXCEPTION+ OR LicenseRef-mit WITH oLd-Exc)")]
        [TestCase("G11C", "Default", " mit+ WITH llvm-exception OR (aCmE.1 WITH MiT AND apache-2.0 OR LLVM-EXCEPTION+ OR LicenseRef-mit WITH OLD-EXC) ", "mit+ WITH llvm-exception OR (aCmE.1 WITH MiT AND apache-2.0 OR LLVM-EXCEPTION+ OR LicenseRef-mit WITH OLD-EXC)", "MIT+ WITH LLVM-exception OR (aCmE.1 WITH MiT AND Apache-2.0 OR LLVM-EXCEPTION+ OR LicenseRef-mit WITH OLD-EXC)")]
        [TestCase("G12", "A", "old+ OR OLD OR plusonly+ OR PLUSONLY", "old+ OR OLD OR plusonly+ OR PLUSONLY", "oLd+ OR oLd OR plusonly+ OR PLUSONLY")]
        [TestCase("G13", "A", "apache-2.0 WITH classpath-exception-2.0", "apache-2.0 WITH classpath-exception-2.0", "apache-2.0 WITH classpath-exception-2.0")]
        [TestCase("G14", "A", "DocumentRef-oLd:LicenseRef-aCmE.1 WITH llvm-exception", "DocumentRef-oLd:LicenseRef-aCmE.1 WITH llvm-exception", "DocumentRef-oLd:LicenseRef-aCmE.1 WITH lLvM-Exception")]
        public void IndependentGoldenOutputs(string vector, string snapshot, string input, string spacing, string canonical) {
            var registry = snapshot == "Default" ? SpdxLicenseRegistry.Default : Load(snapshot);
            var expression = GrammarTests.Parse(input);
            var nodes = expression.Root.EnumerateNodes().ToArray();
            var original = SpdxExpressionValidator.Validate(expression, registry);
            Assert.That(SpdxExpressionFormatter.Format(expression, default), Is.EqualTo(spacing), vector);
            string output = SpdxExpressionFormatter.FormatCanonicalCasing(expression, registry);
            Assert.That(output, Is.EqualTo(canonical), vector);
            Assert.That(output.Length, Is.EqualTo(spacing.Length), vector);
            var expected = GrammarTests.Parse(canonical);
            var reparsed = GrammarTests.Parse(output);
            Assert.That(GrammarTests.Describe(reparsed.Root), Is.EqualTo(GrammarTests.Describe(expected.Root)), vector);
            Assert.That(Shape(reparsed.Root), Is.EqualTo(Shape(expression.Root)), vector);
            Assert.That(SpdxExpressionFormatter.FormatCanonicalCasing(reparsed, registry), Is.EqualTo(output), vector);
            Assert.That(SpdxExpressionFormatter.Format(expression), Is.EqualTo(spacing), vector);
            Assert.That(expression.SourceText, Is.SameAs(input), vector);
            Assert.That(expression.Root.EnumerateNodes(), Is.EqualTo(nodes), vector);
            var after = SpdxExpressionValidator.Validate(expression, registry);
            Assert.That(after.IdentifierOccurrences.Select(SourceOccurrence), Is.EqualTo(original.IdentifierOccurrences.Select(SourceOccurrence)), vector);
            Assert.That(after.References.Select(SourceReference), Is.EqualTo(original.References.Select(SourceReference)), vector);
            Assert.That(after.Diagnostics.Select(SourceDiagnostic), Is.EqualTo(original.Diagnostics.Select(SourceDiagnostic)), vector);
            CheckEquivalentValidation(original, after);
            CheckEquivalentValidation(original, SpdxExpressionValidator.Validate(reparsed, registry));
        }
        [Test]
        public void NamespaceAndLegacyPlusControls() {
            var wrong = SpdxExpressionValidator.Validate(GrammarTests.Parse("llvm-EXCEPTION+ OR mit WITH apache-2.0"), SpdxLicenseRegistry.Default);
            Assert.That(wrong.IdentifierOccurrences.Select(item => item.LookupStatus), Is.EqualTo(new[] {
                SpdxIdentifierLookupStatus.WrongKind, SpdxIdentifierLookupStatus.Known, SpdxIdentifierLookupStatus.WrongKind
            }));
            var deprecated = SpdxExpressionValidator.Validate(
                GrammarTests.Parse("((gpl-2.0+ WITH nokia-QT-exception-1.1)) OR (ecos-2.0 AND miT AND mit)"), SpdxLicenseRegistry.Default);
            Assert.That(deprecated.IdentifierOccurrences.Select(item => (item.CanonicalIdentifier, item.IsDeprecated)), Is.EqualTo(
                new (string?, bool?)[] { ("GPL-2.0", true), ("Nokia-Qt-exception-1.1", true), ("eCos-2.0", true), ("MIT", false), ("MIT", false) }));
            var registry = Load("A");
            Assert.That(registry.TryGetLicense("old+", out var oldPlus) && oldPlus.Identifier == "OlD+" && oldPlus.IsDeprecated, Is.True);
            Assert.That(registry.TryGetLicense("plusonly+", out var plusOnly) && plusOnly.Identifier == "pLuSoNlY+" && plusOnly.IsDeprecated, Is.True);
            var expression = GrammarTests.Parse("old+ OR OLD OR plusonly+ OR PLUSONLY");
            var result = SpdxExpressionValidator.Validate(expression, registry);
            Assert.That(result.IdentifierOccurrences.Select(OccurrenceMeaning), Is.EqualTo(
                new (SpdxIdentifierRole, SpdxIdentifierLookupStatus, string?, bool?)[] {
                    (SpdxIdentifierRole.License, SpdxIdentifierLookupStatus.Known, "oLd", true),
                    (SpdxIdentifierRole.License, SpdxIdentifierLookupStatus.Known, "oLd", true),
                    (SpdxIdentifierRole.License, SpdxIdentifierLookupStatus.Unknown, null, null),
                    (SpdxIdentifierRole.License, SpdxIdentifierLookupStatus.Unknown, null, null)
                }));
        }
        [Test]
        public void RegistrySelectionIsOrdinalAndConcurrentOnSameAst() {
            var a = Load("A");
            var b = Load("B");
            var embedded = SpdxLicenseRegistry.Default;
            Assert.That(a.Identity.LicenseListVersion, Is.EqualTo(b.Identity.LicenseListVersion));
            Assert.That(a.Identity.LicensesSha256, Is.Not.EqualTo(b.Identity.LicensesSha256));
            Assert.That(a.Identity.ExceptionsSha256, Is.Not.EqualTo(b.Identity.ExceptionsSha256));
            var expression = GrammarTests.Parse(MixedCatalogueInput);
            var variants = new[] { (a, MixedCatalogueA), (b, MixedCatalogueB), (embedded, MixedDefault) };
            var cultures = new[] { CultureInfo.InvariantCulture, CultureInfo.GetCultureInfo("tr-TR") };
            Parallel.For(0, 24, index => {
                var previous = CultureInfo.CurrentCulture;
                try {
                    CultureInfo.CurrentCulture = cultures[index % 2];
                    var (registry, expected) = variants[index % variants.Length];
                    Assert.That(SpdxExpressionFormatter.FormatCanonicalCasing(expression, registry), Is.EqualTo(expected));
                    var turkish = GrammarTests.Parse("isc WITH independent-modules-exception OR apache-2.0");
                    Assert.That(SpdxExpressionFormatter.FormatCanonicalCasing(turkish, embedded),
                        Is.EqualTo("ISC WITH Independent-modules-exception OR Apache-2.0"));
                }
                finally { CultureInfo.CurrentCulture = previous; }
            });
            var options = new SpdxValidationOptions { MaxDiagnostics = 1 };
            var before = SpdxExpressionValidator.Validate(expression, a, options);
            var after = SpdxExpressionValidator.Validate(GrammarTests.Parse(MixedCatalogueA), a, options);
            Assert.That((before.HasIdentifierErrors, before.HasUnresolvedReferences, before.DiagnosticsTruncated), Is.EqualTo((true, true, true)));
            Assert.That(before.IdentifierOccurrences, Has.Count.EqualTo(7));
            Assert.That(before.Diagnostics.Select(item => item.Code), Is.EqualTo(new[] { "SPDX_ID_WRONG_KIND" }));
            CheckEquivalentValidation(before, after);
        }
        [Test]
        public void OriginalNodeIdentityAndUtf16SpansRemainExact() {
            const string input = " \t(gpl-2.0+\tWITH llvm-exception)OR(LicenseRef-miT) ";
            var registry = SpdxLicenseRegistry.Default;
            var expression = GrammarTests.Parse(input);
            var root = (SpdxOrExpression)expression.Root;
            var left = (SpdxGroupExpression)root.Operands[0];
            var with = (SpdxWithExpression)left.Inner;
            var license = (SpdxLicenseExpression)with.Operand;
            var right = (SpdxGroupExpression)root.Operands[1];
            var reference = (SpdxReferenceExpression)right.Inner;
            var originalNodes = root.EnumerateNodes().ToArray();
            string output = SpdxExpressionFormatter.FormatCanonicalCasing(expression, registry);
            Assert.That(expression.Root, Is.SameAs(root));
            Assert.That(root.EnumerateNodes(), Is.EqualTo(originalNodes));
            Assert.That(expression.SourceText, Is.SameAs(input));
            CheckSpan(expression, root.Span, 2, 48, "(gpl-2.0+\tWITH llvm-exception)OR(LicenseRef-miT)");
            CheckSpan(expression, left.Span, 2, 30, "(gpl-2.0+\tWITH llvm-exception)");
            CheckSpan(expression, with.Span, 3, 28, "gpl-2.0+\tWITH llvm-exception");
            CheckSpan(expression, license.Span, 3, 8, "gpl-2.0+");
            CheckSpan(expression, license.IdentifierSpan, 3, 7, "gpl-2.0");
            CheckSpan(expression, with.ExceptionSpan, 17, 14, "llvm-exception");
            CheckSpan(expression, right.Span, 34, 16, "(LicenseRef-miT)");
            CheckSpan(expression, reference.Span, 35, 14, "LicenseRef-miT");
            CheckSpan(expression, reference.LicenseIdSpan, 46, 3, "miT");
            Assert.That((license.Identifier, with.ExceptionIdentifier, reference.LicenseId, license.OrLater),
                Is.EqualTo(("gpl-2.0", "llvm-exception", "miT", true)));
            var reparsed = GrammarTests.Parse(output);
            var parsedRoot = (SpdxOrExpression)reparsed.Root;
            var parsedWith = (SpdxWithExpression)((SpdxGroupExpression)parsedRoot.Operands[0]).Inner;
            CheckSpan(reparsed, parsedRoot.Span, 0, 50, "(GPL-2.0+ WITH LLVM-exception) OR (LicenseRef-miT)");
            CheckSpan(reparsed, ((SpdxLicenseExpression)parsedWith.Operand).IdentifierSpan, 1, 7, "GPL-2.0");
            CheckSpan(reparsed, parsedWith.ExceptionSpan, 15, 14, "LLVM-exception");
        }
        [Test]
        public void SpacingCanGrowPastOriginalInputLimit() {
            var atTwelve = new SpdxParseOptions { MaxInputLength = 12 };
            var expression = GrammarTests.Parse("(mit)OR(isc)", atTwelve);
            Assert.That(SpdxExpressionParser.Parse(expression.SourceText, new() { MaxInputLength = 11 }).Status, Is.EqualTo(SpdxParseStatus.LimitExceeded));
            string output = SpdxExpressionFormatter.FormatCanonicalCasing(expression, SpdxLicenseRegistry.Default);
            Assert.That(output, Is.EqualTo("(MIT) OR (ISC)"));
            Assert.That(output.Length, Is.EqualTo(14));
            Assert.That(SpdxExpressionParser.Parse(output, atTwelve).Status, Is.EqualTo(SpdxParseStatus.LimitExceeded));
            Assert.That(SpdxExpressionParser.Parse(output, new() { MaxInputLength = 13 }).Status, Is.EqualTo(SpdxParseStatus.LimitExceeded));
            GrammarTests.Parse(output, new() { MaxInputLength = 14 });
        }
        [Test]
        public void RendersEveryOccurrenceInWideChains() {
            const int orCount = 200_000;
            var orOptions = new SpdxParseOptions { MaxInputLength = 1_399_996, MaxTokens = 399_999, MaxNodes = 200_001 };
            string orInput = string.Join(" OR ", Enumerable.Repeat("mit", orCount));
            string orOutput = SpdxExpressionFormatter.FormatCanonicalCasing(GrammarTests.Parse(orInput, orOptions), SpdxLicenseRegistry.Default);
            Assert.That(string.Equals(orOutput, string.Join(" OR ", Enumerable.Repeat("MIT", orCount)), StringComparison.Ordinal), Is.True);
            Assert.That(orOutput.Length, Is.EqualTo(1_399_996));
            var or = (SpdxOrExpression)GrammarTests.Parse(orOutput, orOptions).Root;
            Assert.That(or.Operands, Has.Count.EqualTo(orCount));
            const int andPairs = 4_000;
            var andOptions = new SpdxParseOptions { MaxInputLength = 83_995, MaxTokens = 15_999, MaxNodes = 8_001 };
            string andInput = string.Join(" AND ", Enumerable.Repeat("mit AND FuTuRe-1", andPairs));
            string andOutput = SpdxExpressionFormatter.FormatCanonicalCasing(GrammarTests.Parse(andInput, andOptions), SpdxLicenseRegistry.Default);
            Assert.That(string.Equals(andOutput, string.Join(" AND ", Enumerable.Repeat("MIT AND FuTuRe-1", andPairs)), StringComparison.Ordinal), Is.True);
            var and = (SpdxAndExpression)GrammarTests.Parse(andOutput, andOptions).Root;
            Assert.That(and.Operands, Has.Count.EqualTo(8_000));
            Assert.That((((SpdxLicenseExpression)and.Operands[0]).Identifier, ((SpdxLicenseExpression)and.Operands[^1]).Identifier),
                Is.EqualTo(("MIT", "FuTuRe-1")));
        }
        [Test]
        public void MaximumDepthAndMultiChunkCanonicalAndSourceText() {
            const int depth = 256;
            string open = new('(', depth);
            string close = new(')', depth);
            var depthOptions = new SpdxParseOptions { MaxParenthesisDepth = depth };
            var nested = GrammarTests.Parse(open + "isc" + close, depthOptions);
            string grouped = SpdxExpressionFormatter.FormatCanonicalCasing(nested, SpdxLicenseRegistry.Default);
            Assert.That(grouped, Is.EqualTo(open + "ISC" + close));
            SpdxExpressionNode inner = GrammarTests.Parse(grouped, depthOptions).Root;
            for(int i = 0; i < depth; i++)
                inner = ((SpdxGroupExpression)inner).Inner;
            Assert.That(((SpdxLicenseExpression)inner).Identifier, Is.EqualTo("ISC"));
            const int length = 2_050;
            string licenseId = new('I', length);
            string exceptionId = new('E', length);
            string reference = "LicenseRef-" + new string('a', length - 1) + "Z";
            byte[] licenses = Encoding.UTF8.GetBytes($$"""{"licenseListVersion":"long","licenses":[{"licenseId":"{{licenseId}}","isDeprecatedLicenseId":false}]}""");
            byte[] exceptions = Encoding.UTF8.GetBytes($$"""{"licenseListVersion":"long","exceptions":[{"licenseExceptionId":"{{exceptionId}}","isDeprecatedLicenseId":false}]}""");
            var loaded = SpdxLicenseRegistry.Load(licenses, exceptions, new() { MaxIdentifierLength = length });
            Assert.That(loaded.Status, Is.EqualTo(SpdxRegistryLoadStatus.Success));
            string input = new string('i', length) + " WITH " + new string('e', length) + " OR " + reference;
            var expression = GrammarTests.Parse(input, new() { MaxIdentifierLength = length });
            Assert.That(SpdxExpressionFormatter.FormatCanonicalCasing(expression, loaded.Registry!),
                Is.EqualTo(licenseId + " WITH " + exceptionId + " OR " + reference));
            Assert.That(SpdxExpressionFormatter.Format(expression), Is.EqualTo(input));
        }
        [Test]
        public void ArgumentPrecedenceAndCancellationToken() {
            var token = new CancellationToken(true);
            var expression = GrammarTests.Parse("mit");
            var registry = SpdxLicenseRegistry.Default;
            Assert.That(Assert.Throws<ArgumentNullException>(() =>
                SpdxExpressionFormatter.FormatCanonicalCasing(null!, null!, token))!.ParamName, Is.EqualTo("expression"));
            Assert.That(Assert.Throws<ArgumentNullException>(() =>
                SpdxExpressionFormatter.FormatCanonicalCasing(expression, null!, token))!.ParamName, Is.EqualTo("registry"));
            Assert.That(Assert.Throws<OperationCanceledException>(() =>
                SpdxExpressionFormatter.FormatCanonicalCasing(expression, registry, token))!.CancellationToken, Is.EqualTo(token));
        }
        [Test]
        public void EmbeddedSpellingRetainsIdentifierLexicalShape() {
            var embedded = SpdxLicenseRegistry.Default;
            foreach(var license in embedded.Licenses) {
                string basis = license.Identifier;
                if(basis.EndsWith('+')) {
                    Assert.That(license.IsDeprecated, Is.True, basis);
                    basis = basis[..^1];
                }
                CheckLexical(basis);
            }
            foreach(var exception in embedded.Exceptions)
                CheckLexical(exception.Identifier);
        }
        static void CheckLexical(string basis) {
            Assert.That(basis, Does.Match(@"\A[A-Za-z0-9.\-]+\z"));
            Assert.That(new[] { "AND", "OR", "WITH", "NONE", "NOASSERTION" }.Contains(basis, StringComparer.OrdinalIgnoreCase), Is.False, basis);
            Assert.That(basis.StartsWith("LicenseRef-", StringComparison.OrdinalIgnoreCase), Is.False, basis);
            Assert.That(basis.StartsWith("DocumentRef-", StringComparison.OrdinalIgnoreCase), Is.False, basis);
        }
        static SpdxLicenseRegistry Load(string name) {
            string path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "canonical-catalogues.json");
            using(var fixture = JsonDocument.Parse(File.ReadAllBytes(path))) {
                var pair = fixture.RootElement.GetProperty(name);
                byte[] licenses = Encoding.UTF8.GetBytes(pair.GetProperty("licenses").GetString()!);
                byte[] exceptions = Encoding.UTF8.GetBytes(pair.GetProperty("exceptions").GetString()!);
                var loaded = SpdxLicenseRegistry.Load(licenses, exceptions);
                Assert.That(loaded.Status, Is.EqualTo(SpdxRegistryLoadStatus.Success));
                Assert.That(loaded.Diagnostics, Is.Empty);
                var registry = loaded.Registry!;
                Assert.That(registry.Identity.LicenseListVersion, Is.EqualTo("case-fixture"));
                Assert.That(registry.Identity.LicensesSha256, Is.EqualTo(pair.GetProperty("licensesHash").GetString()));
                Assert.That(registry.Identity.ExceptionsSha256, Is.EqualTo(pair.GetProperty("exceptionsHash").GetString()));
                return registry;
            }
        }
        static void CheckEquivalentValidation(SpdxValidationResult original, SpdxValidationResult reparsed) {
            Assert.That(reparsed.RegistryIdentity, Is.SameAs(original.RegistryIdentity));
            Assert.That((reparsed.HasIdentifierErrors, reparsed.HasUnresolvedReferences, reparsed.DiagnosticsTruncated),
                Is.EqualTo((original.HasIdentifierErrors, original.HasUnresolvedReferences, original.DiagnosticsTruncated)));
            Assert.That(reparsed.IdentifierOccurrences.Select(OccurrenceMeaning), Is.EqualTo(original.IdentifierOccurrences.Select(OccurrenceMeaning)));
            Assert.That(reparsed.References.Select(item => (item.DocumentId, item.LicenseId)),
                Is.EqualTo(original.References.Select(item => (item.DocumentId, item.LicenseId))));
            Assert.That(reparsed.Diagnostics.Select(item => (item.Code, item.Severity)),
                Is.EqualTo(original.Diagnostics.Select(item => (item.Code, item.Severity))));
        }
        static (SpdxIdentifierRole, SpdxIdentifierLookupStatus, string?, bool?) OccurrenceMeaning(SpdxIdentifierOccurrence item) {
            return (item.Role, item.LookupStatus, item.CanonicalIdentifier, item.IsDeprecated);
        }
        static (string, int, int) SourceOccurrence(SpdxIdentifierOccurrence item) {
            return (item.Identifier, item.Span.Start, item.Span.Length);
        }
        static (string?, string, int, int) SourceReference(SpdxReferenceOccurrence item) {
            return (item.DocumentId, item.LicenseId, item.Span.Start, item.Span.Length);
        }
        static (string, int, int) SourceDiagnostic(SpdxDiagnostic item) {
            return (item.Code, item.Span.Start, item.Span.Length);
        }
        static void CheckSpan(SpdxParsedExpression parsed, SpdxSourceSpan span, int start, int length, string slice) {
            Assert.That((span.Start, span.Length, parsed.SourceText.Substring(span.Start, span.Length)), Is.EqualTo((start, length, slice)));
        }
        static string Shape(SpdxExpressionNode node) {
            return node switch {
                SpdxLicenseExpression license => license.OrLater ? "L+" : "L",
                SpdxReferenceExpression reference => $"R({reference.DocumentId}:{reference.LicenseId})",
                SpdxWithExpression with => $"W({Shape(with.Operand)},E)",
                SpdxGroupExpression group => $"G({Shape(group.Inner)})",
                SpdxAndExpression and => "A(" + string.Join(',', and.Operands.Select(Shape)) + ")",
                SpdxOrExpression or => "O(" + string.Join(',', or.Operands.Select(Shape)) + ")",
                _ => throw new InvalidOperationException()
            };
        }
    }
}
