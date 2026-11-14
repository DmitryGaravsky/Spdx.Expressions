using System;
using System.Linq;
using NUnit.Framework;

namespace Spdx.Expressions.Tests {

    public class GrammarTests {
        internal static SpdxParsedExpression Parse(string source, SpdxParseOptions? options = null) {
            var result = SpdxExpressionParser.Parse(source, options);
            Assert.That(result.Status, Is.EqualTo(SpdxParseStatus.Success), source);
            Assert.That(result.Diagnostics, Is.Empty);
            return result.Expression!;
        }

        [TestCase("MIT", "L:MIT")]
        [TestCase("mit", "L:mit")]
        [TestCase("MIT OR Apache-2.0 AND BSD-3-Clause", "O(L:MIT,A(L:Apache-2.0,L:BSD-3-Clause))")]
        [TestCase("(MIT OR Apache-2.0) AND BSD-3-Clause", "A(G(O(L:MIT,L:Apache-2.0)),L:BSD-3-Clause)")]
        [TestCase("MIT AND MIT AND MIT", "A(L:MIT,L:MIT,L:MIT)")]
        [TestCase("MIT OR MIT OR MIT", "O(L:MIT,L:MIT,L:MIT)")]
        [TestCase("MIT AND (MIT AND MIT)", "A(L:MIT,G(A(L:MIT,L:MIT)))")]
        [TestCase("(MIT)", "G(L:MIT)")]
        [TestCase("((MIT))", "G(G(L:MIT))")]
        [TestCase("GPL-2.0+ WITH Classpath-exception-2.0", "W(L:GPL-2.0+,Classpath-exception-2.0)")]
        [TestCase("LicenseRef-X", "R::X")]
        [TestCase("DocumentRef-D:LicenseRef-X", "R:D:X")]
        [TestCase("LicenseRef-X WITH LLVM-exception", "W(R::X,LLVM-exception)")]
        [TestCase("DocumentRef-D:LicenseRef-X WITH LLVM-exception", "W(R:D:X,LLVM-exception)")]
        [TestCase("Unknown-1.0 OR MIT", "O(L:Unknown-1.0,L:MIT)")]
        [TestCase("MIT WITH Apache-2.0", "W(L:MIT,Apache-2.0)")]
        [TestCase("(MIT)AND(Apache-2.0)", "A(G(L:MIT),G(L:Apache-2.0))")]
        [TestCase("MIT AND(Apache-2.0)", "A(L:MIT,G(L:Apache-2.0))")]
        [TestCase("(MIT)OR Apache-2.0", "O(G(L:MIT),L:Apache-2.0)")]
        [TestCase("MITANDMIT", "L:MITANDMIT")]
        [TestCase("none", "L:none")]
        [TestCase("noassertion", "L:noassertion")]
        [TestCase("licenseref-X", "L:licenseref-X")]
        [TestCase("documentref-D", "L:documentref-D")]
        [TestCase("LicenseRef-AND", "R::AND")]
        [TestCase("DocumentRef-NONE:LicenseRef-NOASSERTION", "R:NONE:NOASSERTION")]
        [TestCase(". AND -", "A(L:.,L:-)")]
        [TestCase("MIT\r\nWITH\tLLVM-exception", "W(L:MIT,LLVM-exception)")]
        public void IndependentStructures(string input, string expected) {
            var expression = Parse(input);
            Assert.That(Describe(expression.Root), Is.EqualTo(expected));
            Assert.That(expression.SourceText, Is.SameAs(input));
            Assert.That(expression.SyntaxProfile, Is.EqualTo("SPDX-2.3.1"));
        }

        // Manually specified diagnostic arbitration and UTF-16 spans, not parser-generated expectations.
        [TestCase("", "EMPTY_INPUT", 0, 0)]
        [TestCase(" \t\r\n", "EMPTY_INPUT", 4, 0)]
        [TestCase("MIT AND", "EXPECTED_OPERAND", 7, 0)]
        [TestCase("MIT or Apache-2.0", "EXPECTED_OPERATOR", 4, 2)]
        [TestCase("(MIT) WITH LLVM-exception", "INVALID_WITH", 6, 4)]
        [TestCase("MIT WITH LLVM-exception WITH LLVM-exception", "INVALID_WITH", 24, 4)]
        [TestCase("MIT +", "INVALID_PLUS", 4, 1)]
        [TestCase("MIT++", "INVALID_PLUS", 4, 1)]
        [TestCase("LicenseRef-X+", "INVALID_PLUS", 12, 1)]
        [TestCase("DocumentRef-D:LicenseRef-X+", "INVALID_PLUS", 26, 1)]
        [TestCase("MIT+AND MIT", "EXPECTED_OPERATOR", 4, 3)]
        [TestCase("MIT AND+MIT", "EXPECTED_OPERATOR", 4, 3)]
        [TestCase("DocumentRef-X:MIT", "INVALID_REFERENCE", 0, 14)]
        [TestCase("LicenseRef-", "INVALID_REFERENCE", 0, 11)]
        [TestCase("DocumentRef-:LicenseRef-X", "INVALID_REFERENCE", 0, 12)]
        [TestCase("DocumentRef-X", "INVALID_REFERENCE", 0, 13)]
        [TestCase("DocumentRef-X:licenseref-Y", "INVALID_REFERENCE", 0, 14)]
        [TestCase("LicenseRef-X:Y", "INVALID_REFERENCE", 0, 13)]
        [TestCase("NONE", "FIELD_SENTINEL", 0, 4)]
        [TestCase("MIT OR NOASSERTION", "FIELD_SENTINEL", 7, 11)]
        [TestCase("MIT WITH NONE", "FIELD_SENTINEL", 9, 4)]
        [TestCase("SPDX-License-Identifier: MIT", "INVALID_CHARACTER", 23, 1)]
        [TestCase("MIT\u00a0OR MIT", "INVALID_CHARACTER", 3, 1)]
        [TestCase("MIT\0", "INVALID_CHARACTER", 3, 1)]
        [TestCase("MIT\ud800", "INVALID_CHARACTER", 3, 1)]
        [TestCase("\udc00", "INVALID_CHARACTER", 0, 1)]
        [TestCase("ＭIT", "INVALID_CHARACTER", 0, 1)]
        [TestCase("MIT_", "INVALID_CHARACTER", 3, 1)]
        [TestCase("\"MIT\"", "INVALID_CHARACTER", 0, 1)]
        [TestCase("MIT # comment", "INVALID_CHARACTER", 4, 1)]
        [TestCase("MIT // comment", "INVALID_CHARACTER", 4, 1)]
        [TestCase("MIT trailing prose", "EXPECTED_OPERATOR", 4, 8)]
        [TestCase("(MIT", "UNBALANCED_PARENTHESIS", 4, 0)]
        [TestCase("MIT)", "UNBALANCED_PARENTHESIS", 3, 1)]
        [TestCase("(", "UNBALANCED_PARENTHESIS", 1, 0)]
        [TestCase("()", "EXPECTED_OPERAND", 1, 1)]
        [TestCase("AND MIT", "EXPECTED_OPERAND", 0, 3)]
        [TestCase("MIT MIT", "EXPECTED_OPERATOR", 4, 3)]
        [TestCase("MIT WITH", "INVALID_WITH", 8, 0)]
        [TestCase("MIT WITH(MIT)", "INVALID_WITH", 4, 4)]
        [TestCase("MIT+WITH LLVM-exception", "INVALID_WITH", 4, 4)]
        [TestCase("MIT WITH LicenseRef-X", "INVALID_WITH", 9, 12)]
        [TestCase("MIT WITH DocumentRef-D:LicenseRef-X", "INVALID_WITH", 9, 26)]
        [TestCase("MIT WITH LLVM-exception+", "INVALID_PLUS", 23, 1)]
        [TestCase("MIT\tAND\r\n", "EXPECTED_OPERAND", 9, 0)]
        public void IndependentFailures(string input, string code, int start, int length) {
            var result = SpdxExpressionParser.Parse(input);
            Assert.That(result.Status, Is.EqualTo(SpdxParseStatus.InvalidSyntax));
            Assert.That(result.Expression, Is.Null);
            Assert.That(result.Diagnostics, Has.Count.EqualTo(1));
            Assert.That(result.Diagnostics[0].Code, Is.EqualTo("SPDX_PARSE_" + code));
            Span(result.Diagnostics[0].Span, start, length);
        }

        [Test]
        public void ExactNodeAndComponentSpans() {
            const string input = " \t(DocumentRef-D:LicenseRef-X OR mit+)\r\nAND GPL-2.0+ WITH LLVM-exception ";
            var expression = Parse(input);
            var and = (SpdxAndExpression)expression.Root;
            Span(and.Span, 2, 70);
            var group = (SpdxGroupExpression)and.Operands[0];
            Span(group.Span, 2, 36);
            var or = (SpdxOrExpression)group.Inner;
            Span(or.Span, 3, 34);
            var reference = (SpdxReferenceExpression)or.Operands[0];
            Span(reference.Span, 3, 26);
            Span(reference.DocumentIdSpan!.Value, 15, 1);
            Span(reference.LicenseIdSpan, 28, 1);
            var mit = (SpdxLicenseExpression)or.Operands[1];
            Span(mit.Span, 33, 4);
            Span(mit.IdentifierSpan, 33, 3);
            var with = (SpdxWithExpression)and.Operands[1];
            Span(with.Span, 44, 28);
            Span(with.ExceptionSpan, 58, 14);
            Span(((SpdxLicenseExpression)with.Operand).IdentifierSpan, 44, 7);
        }

        internal static void Span(SpdxSourceSpan span, int start, int length) {
            Assert.That((span.Start, span.Length), Is.EqualTo((start, length)));
        }
        internal static string Describe(SpdxExpressionNode node) {
            return node switch {
                SpdxLicenseExpression l => "L:" + l.Identifier + (l.OrLater ? "+" : ""),
                SpdxReferenceExpression r => "R:" + r.DocumentId + ":" + r.LicenseId,
                SpdxWithExpression w => "W(" + Describe(w.Operand) + "," + w.ExceptionIdentifier + ")",
                SpdxGroupExpression g => "G(" + Describe(g.Inner) + ")",
                SpdxAndExpression a => "A(" + string.Join(',', a.Operands.Select(Describe)) + ")",
                SpdxOrExpression o => "O(" + string.Join(',', o.Operands.Select(Describe)) + ")",
                _ => throw new InvalidOperationException()
            };
        }
    }

}
