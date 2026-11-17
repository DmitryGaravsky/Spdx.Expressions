using System.Linq;
using NUnit.Framework;

namespace Spdx.Expressions.Tests {
    public class RobustnessTests {
        [TestCase(256)]
        [TestCase(1024)]
        public void MixedOperatorChainsPreserveEveryConjunction(int clauses) {
            string input = string.Join(" OR ", Enumerable.Repeat("MIT AND ISC", clauses));
            var expression = GrammarTests.Parse(input);
            var choices = (SpdxOrExpression)expression.Root;
            Assert.That(choices.Operands, Has.Count.EqualTo(clauses));
            foreach(var operand in choices.Operands) {
                var conjunction = (SpdxAndExpression)operand;
                Assert.That(conjunction.Operands, Has.Count.EqualTo(2));
                Assert.That(((SpdxLicenseExpression)conjunction.Operands[0]).Identifier, Is.EqualTo("MIT"));
                Assert.That(((SpdxLicenseExpression)conjunction.Operands[1]).Identifier, Is.EqualTo("ISC"));
            }
            var validation = SpdxExpressionValidator.Validate(expression, SpdxLicenseRegistry.Default);
            Assert.That(validation.IdentifierOccurrences, Has.Count.EqualTo(clauses * 2));
            Assert.That(validation.HasIdentifierErrors, Is.False);
            Assert.That(validation.Diagnostics, Is.Empty);
            Assert.That(SpdxExpressionFormatter.Format(expression), Is.EqualTo(input));
        }
        [TestCase(512)]
        [TestCase(4096)]
        public void DiagnosticFloodRetainsLateReferenceExceptionAndError(int warnings) {
            string input = string.Join(" OR ", Enumerable.Repeat("GPL-2.0", warnings)) + " OR LicenseRef-X WITH LLVM-exception OR Unknown";
            var expression = GrammarTests.Parse(input);
            var result = SpdxExpressionValidator.Validate(expression, SpdxLicenseRegistry.Default, new() { MaxDiagnostics = 1 });
            Assert.That(result.IdentifierOccurrences, Has.Count.EqualTo(warnings + 2));
            Assert.That(result.References, Has.Count.EqualTo(1));
            Assert.That(result.Diagnostics, Has.Count.EqualTo(1));
            Assert.That(result.Diagnostics[0].Code, Is.EqualTo("SPDX_ID_DEPRECATED"));
            Assert.That(result.HasIdentifierErrors, Is.True);
            Assert.That(result.HasUnresolvedReferences, Is.True);
            Assert.That(result.DiagnosticsTruncated, Is.True);
            Assert.That(result.IdentifierOccurrences[^2].CanonicalIdentifier, Is.EqualTo("LLVM-exception"));
            Assert.That(result.IdentifierOccurrences[^1].LookupStatus, Is.EqualTo(SpdxIdentifierLookupStatus.Unknown));
            GrammarTests.Span(result.IdentifierOccurrences[^1].Span, input.Length - 7, 7);
            Assert.That(SpdxExpressionFormatter.Format(expression), Is.EqualTo(input));
        }
        [TestCase("AND")]
        [TestCase("OR")]
        public void MissingOperandAfterLongChainReturnsOnlyEofFailure(string op) {
            string input = string.Join(" OR ", Enumerable.Repeat("MIT", 2048)) + " " + op;
            var result = SpdxExpressionParser.Parse(input);
            Assert.That(result.Status, Is.EqualTo(SpdxParseStatus.InvalidSyntax));
            Assert.That(result.Expression, Is.Null);
            Assert.That(result.Diagnostics, Has.Count.EqualTo(1));
            Assert.That(result.Diagnostics[0].Code, Is.EqualTo("SPDX_PARSE_EXPECTED_OPERAND"));
            GrammarTests.Span(result.Diagnostics[0].Span, input.Length, 0);
        }
    }
}
