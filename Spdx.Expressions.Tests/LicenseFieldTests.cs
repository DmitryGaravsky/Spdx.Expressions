using System;
using System.Linq;
using System.Threading;
using NUnit.Framework;

namespace Spdx.Expressions.Tests {
    public class LicenseFieldTests {
        [TestCase(null, SpdxLicenseFieldKind.Missing)]
        [TestCase("", SpdxLicenseFieldKind.Empty)]
        [TestCase("NONE", SpdxLicenseFieldKind.None)]
        [TestCase(" \tNONE\r\n", SpdxLicenseFieldKind.None)]
        [TestCase("NOASSERTION", SpdxLicenseFieldKind.NoAssertion)]
        [TestCase("\nNOASSERTION\t ", SpdxLicenseFieldKind.NoAssertion)]
        public void ReadsFieldEvidenceWithoutAnExpression(string? text, SpdxLicenseFieldKind kind) {
            var result = SpdxLicenseFieldReader.Read(text, new() { MaxIdentifierLength = 1, MaxTokens = 1, MaxNodes = 1 });
            Assert.That(result.Kind, Is.EqualTo(kind));
            Assert.That(result.SourceText, Is.SameAs(text));
            Assert.That(result.Expression, Is.Null);
            Assert.That(result.Diagnostics, Is.Empty);
        }
        [TestCase("none", SpdxExpressionKind.License, 0, 4)]
        [TestCase("NoAssertion", SpdxExpressionKind.License, 0, 11)]
        [TestCase("NONE-X", SpdxExpressionKind.License, 0, 6)]
        [TestCase("NOASSERTION1", SpdxExpressionKind.License, 0, 12)]
        [TestCase("LicenseRef-NONE", SpdxExpressionKind.Reference, 0, 15)]
        [TestCase("  (mIt OR Apache-2.0)\t", SpdxExpressionKind.Group, 2, 19)]
        public void ExpressionsKeepTheirSourceAndNativeTree(string text, SpdxExpressionKind kind, int start, int length) {
            var result = SpdxLicenseFieldReader.Read(text);
            Assert.That(result.Kind, Is.EqualTo(SpdxLicenseFieldKind.Expression));
            Assert.That(result.SourceText, Is.SameAs(text));
            Assert.That(result.Expression!.SourceText, Is.SameAs(text));
            Assert.That(result.Expression.Root.Kind, Is.EqualTo(kind));
            GrammarTests.Span(result.Expression.Root.Span, start, length);
            Assert.That(result.Diagnostics, Is.Empty);
        }
        [TestCase(" \t\r\n", "SPDX_PARSE_EMPTY_INPUT", 4, 0)]
        [TestCase("\u00a0NONE", "SPDX_PARSE_INVALID_CHARACTER", 0, 1)]
        [TestCase("NONE\u00a0", "SPDX_PARSE_FIELD_SENTINEL", 0, 4)]
        [TestCase(" NONE OR MIT ", "SPDX_PARSE_FIELD_SENTINEL", 1, 4)]
        [TestCase(" MIT OR NONE ", "SPDX_PARSE_FIELD_SENTINEL", 8, 4)]
        [TestCase("(NOASSERTION)", "SPDX_PARSE_FIELD_SENTINEL", 1, 11)]
        [TestCase("NONE+", "SPDX_PARSE_FIELD_SENTINEL", 0, 4)]
        [TestCase("MIT WITH NOASSERTION", "SPDX_PARSE_FIELD_SENTINEL", 9, 11)]
        [TestCase(" MIT AND ", "SPDX_PARSE_EXPECTED_OPERAND", 9, 0)]
        public void InvalidFieldsReportTheStrictParserFailure(string text, string code, int start, int length) {
            var result = SpdxLicenseFieldReader.Read(text);
            Assert.That(result.Kind, Is.EqualTo(SpdxLicenseFieldKind.InvalidSyntax));
            Assert.That(result.SourceText, Is.SameAs(text));
            Assert.That(result.Expression, Is.Null);
            var diagnostic = result.Diagnostics.Single();
            Assert.That(diagnostic.Code, Is.EqualTo(code));
            GrammarTests.Span(diagnostic.Span, start, length);
            var strict = SpdxExpressionParser.Parse(text);
            Assert.That(strict.Status, Is.EqualTo(SpdxParseStatus.InvalidSyntax));
            Assert.That(strict.Diagnostics.Single().Code, Is.EqualTo(code));
            GrammarTests.Span(strict.Diagnostics[0].Span, start, length);
        }
        [Test]
        public void FullInputLengthIncludesPaddingAndWhitespaceOnlyText() {
            var options = new SpdxParseOptions { MaxInputLength = 5 };
            Assert.That(SpdxLicenseFieldReader.Read("NONE ", options).Kind, Is.EqualTo(SpdxLicenseFieldKind.None));
            foreach(string text in new[] { " NONE ", "      ", "NOASSERTION", "123456" }) {
                var result = SpdxLicenseFieldReader.Read(text, options);
                Assert.That(result.Kind, Is.EqualTo(SpdxLicenseFieldKind.LimitExceeded));
                Assert.That(result.SourceText, Is.SameAs(text));
                Assert.That(result.Expression, Is.Null);
                Assert.That(result.Diagnostics.Single().Code, Is.EqualTo("SPDX_PARSE_LIMIT_EXCEEDED"));
                GrammarTests.Span(result.Diagnostics[0].Span, 0, text.Length);
            }
        }
        [Test]
        public void LongPrefixCountsTowardInputLimitAndKeepsOriginalTokenPositions() {
            string padding = new(' ', 1024);
            string text = padding + "MIT OR ISC";
            var exact = SpdxLicenseFieldReader.Read(text, new() { MaxInputLength = text.Length });
            Assert.That(exact.Kind, Is.EqualTo(SpdxLicenseFieldKind.Expression));
            Assert.That(exact.Expression!.SourceText, Is.SameAs(text));
            var root = (SpdxOrExpression)exact.Expression.Root;
            GrammarTests.Span(root.Span, padding.Length, 10);
            GrammarTests.Span(root.Operands[1].Span, padding.Length + 7, 3);
            var over = SpdxLicenseFieldReader.Read(text, new() { MaxInputLength = text.Length - 1 });
            Assert.That(over.Kind, Is.EqualTo(SpdxLicenseFieldKind.LimitExceeded));
            GrammarTests.Span(over.Diagnostics.Single().Span, 0, text.Length);
        }
        [Test]
        public void ExpressionBudgetsUseOriginalOffsets() {
            var result = SpdxLicenseFieldReader.Read("  MIT OR ISC", new() { MaxTokens = 2 });
            Assert.That(result.Kind, Is.EqualTo(SpdxLicenseFieldKind.LimitExceeded));
            Assert.That(result.Expression, Is.Null);
            Assert.That(result.Diagnostics.Single().Code, Is.EqualTo("SPDX_PARSE_LIMIT_EXCEEDED"));
            GrammarTests.Span(result.Diagnostics[0].Span, 9, 1);
        }
        [TestCase(null)]
        [TestCase("")]
        [TestCase("NONE")]
        [TestCase("MIT")]
        public void ChecksOptionsBeforeCancellationForEveryField(string? text) {
            var token = new CancellationToken(true);
            Assert.Throws<ArgumentOutOfRangeException>(() => SpdxLicenseFieldReader.Read(text, new() { MaxTokens = 0 }, token));
            var error = Assert.Throws<OperationCanceledException>(() => SpdxLicenseFieldReader.Read(text, cancellationToken: token));
            Assert.That(error!.CancellationToken, Is.EqualTo(token));
        }
    }
}
