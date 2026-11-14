using System.Linq;
using NUnit.Framework;

namespace Spdx.Expressions.Tests {
    // Project-maintained fixtures derived from the fixed specification, not an official SPDX test suite.
    [TestFixture, Category("Conformance")]
    public class ConformanceTests {
        const string AnnexD = "https://spdx.github.io/spdx-spec/v2.3.1/SPDX-license-expressions/";
        // Literal expectations are reviewed independently of parser output. L/R/W/G/A/O use GrammarTests.Describe.
        [TestCase("CDDL-1.0", "L:CDDL-1.0", 1, 0, TestName = "D3_License")]
        [TestCase("CDDL-1.0+", "L:CDDL-1.0+", 1, 0, TestName = "D3_OrLater")]
        [TestCase("LicenseRef-23", "R::23", 0, 1, TestName = "D3_NumericReference")]
        [TestCase("LicenseRef-MIT-Style-1", "R::MIT-Style-1", 0, 1, TestName = "D3_LocalReference")]
        [TestCase("DocumentRef-spdx-tool-1.2:LicenseRef-MIT-Style-2", "R:spdx-tool-1.2:MIT-Style-2", 0, 1, TestName = "D3_DocumentReference")]
        [Property("Source", AnnexD + "#d3-simple-license-expressions")]
        public void SimpleExamples(string input, string structure, int identifiers, int references) {
            CheckExample(input, structure, identifiers, references);
        }
        [TestCase("LGPL-2.1-only OR MIT", "O(L:LGPL-2.1-only,L:MIT)", 2, TestName = "D42_Choice")]
        [TestCase("MIT OR LGPL-2.1-only", "O(L:MIT,L:LGPL-2.1-only)", 2, TestName = "D42_ReversedChoice")]
        [TestCase("LGPL-2.1-only OR MIT OR BSD-3-Clause", "O(L:LGPL-2.1-only,L:MIT,L:BSD-3-Clause)", 3, TestName = "D42_ThreeChoices")]
        [Property("Source", AnnexD + "#d42-disjunctive-or-operator")]
        public void DisjunctionExamples(string input, string structure, int identifiers) {
            CheckExample(input, structure, identifiers, 0);
        }
        [TestCase("LGPL-2.1-only AND MIT", "A(L:LGPL-2.1-only,L:MIT)", 2, TestName = "D43_Conjunction")]
        [TestCase("MIT AND LGPL-2.1-only", "A(L:MIT,L:LGPL-2.1-only)", 2, TestName = "D43_ReversedConjunction")]
        [TestCase("LGPL-2.1-only AND MIT AND BSD-2-Clause", "A(L:LGPL-2.1-only,L:MIT,L:BSD-2-Clause)", 3, TestName = "D43_ThreeRequirements")]
        [Property("Source", AnnexD + "#d43-conjunctive-and-operator")]
        public void ConjunctionExamples(string input, string structure, int identifiers) {
            CheckExample(input, structure, identifiers, 0);
        }
        [Test, Property("Source", AnnexD + "#d44-exception-with-operator")]
        public void D44_ExceptionExample() {
            CheckExample("GPL-2.0-or-later WITH Bison-exception-2.2", "W(L:GPL-2.0-or-later,Bison-exception-2.2)", 2, 0);
        }
        [TestCase("LGPL-2.1-only OR BSD-3-Clause AND MIT", "O(L:LGPL-2.1-only,A(L:BSD-3-Clause,L:MIT))", TestName = "D45_DefaultPrecedence")]
        [TestCase("MIT AND (LGPL-2.1-or-later OR BSD-3-Clause)", "A(L:MIT,G(O(L:LGPL-2.1-or-later,L:BSD-3-Clause)))", TestName = "D45_GroupedPrecedence")]
        [Property("Source", AnnexD + "#d45-order-of-precedence-and-parentheses")]
        public void PrecedenceExamples(string input, string structure) {
            CheckExample(input, structure, 3, 0);
        }
        // Derived counterexamples/combinations, not quoted specification examples.
        [TestCase("MIT OR GPL-2.0+ WITH LLVM-exception AND Apache-2.0 OR BSD-3-Clause",
            "O(L:MIT,A(W(L:GPL-2.0+,LLVM-exception),L:Apache-2.0),L:BSD-3-Clause)", TestName = "D45_AllPrecedenceLevels")]
        [TestCase("MIT AND Apache-2.0 OR BSD-3-Clause AND ISC",
            "O(A(L:MIT,L:Apache-2.0),A(L:BSD-3-Clause,L:ISC))", TestName = "D45_ConjunctionsOnBothSides")]
        [TestCase("MIT OR (Apache-2.0 OR BSD-3-Clause) OR MIT",
            "O(L:MIT,G(O(L:Apache-2.0,L:BSD-3-Clause)),L:MIT)", TestName = "D45_GroupedDisjunctionIsNotFlattened")]
        [TestCase("(MIT OR Apache-2.0) AND GPL-2.0+ WITH LLVM-exception",
            "A(G(O(L:MIT,L:Apache-2.0)),W(L:GPL-2.0+,LLVM-exception))", TestName = "D45_GroupOverridesMixedPrecedence")]
        [Property("Source", AnnexD + "#d45-order-of-precedence-and-parentheses")]
        public void DerivedStructures(string input, string expected) {
            var expression = GrammarTests.Parse(input);
            Assert.That(GrammarTests.Describe(expression.Root), Is.EqualTo(expected));
            Assert.That(SpdxExpressionFormatter.Format(expression), Is.EqualTo(input));
        }
        [TestCase("MIT and Apache-2.0", "EXPECTED_OPERATOR", 4, 3, TestName = "D2_LowercaseAnd")]
        [TestCase("MIT oR Apache-2.0", "EXPECTED_OPERATOR", 4, 2, TestName = "D2_MixedCaseOr")]
        [TestCase("MIT wITh LLVM-exception", "EXPECTED_OPERATOR", 4, 4, TestName = "D2_MixedCaseWith")]
        [Property("Source", AnnexD + "#d2-case-sensitivity")]
        public void OperatorsAreCaseSensitive(string input, string code, int start, int length) {
            Reject(input, code, start, length);
        }
        [TestCase("MIT OR", "EXPECTED_OPERAND", 6, 0, TestName = "D1_MissingOrOperand")]
        [TestCase("MIT OR OR Apache-2.0", "EXPECTED_OPERAND", 7, 2, TestName = "D1_RepeatedOr")]
        [TestCase("MIT AND OR Apache-2.0", "EXPECTED_OPERAND", 8, 2, TestName = "D1_MissingOperandBetweenOperators")]
        [TestCase("MIT(ISC)", "EXPECTED_OPERATOR", 3, 1, TestName = "D1_NoImplicitConjunction")]
        [TestCase("MIT\t+", "INVALID_PLUS", 4, 1, TestName = "D1_PlusSeparatedByTab")]
        [TestCase("(MIT OR ISC) WITH LLVM-exception", "INVALID_WITH", 13, 4, TestName = "D44_WithOnCompoundGroup")]
        [TestCase("(LicenseRef-X) WITH LLVM-exception", "INVALID_WITH", 15, 4, TestName = "D44_WithOnGroupedReference")]
        [TestCase("MIT WITH (LLVM-exception)", "INVALID_WITH", 9, 1, TestName = "D44_GroupedException")]
        [TestCase("(MIT)+", "INVALID_PLUS", 5, 1, TestName = "D1_PlusOnGroup")]
        [TestCase("MIT+OR ISC", "EXPECTED_OPERATOR", 4, 2, TestName = "D1_OrWithoutLeftSeparator")]
        [Property("Source", AnnexD + "#d1-overview")]
        public void DerivedGrammarFailures(string input, string code, int start, int length) {
            Reject(input, code, start, length);
        }
        // Reference colon adjacency is an explicit profile interpretation of D.1's contiguous license-ref production.
        [TestCase("DocumentRef-D :LicenseRef-X", "INVALID_REFERENCE", 0, 13, TestName = "Profile_WhitespaceBeforeReferenceColon")]
        [TestCase("DocumentRef-D: LicenseRef-X", "INVALID_REFERENCE", 0, 14, TestName = "Profile_WhitespaceAfterReferenceColon")]
        [TestCase("DocumentRef-D:\tLicenseRef-X", "INVALID_REFERENCE", 0, 14, TestName = "Profile_TabInsideReference")]
        [TestCase("DocumentRef-D:", "INVALID_REFERENCE", 0, 14, TestName = "D1_MissingReferenceTail")]
        [TestCase("DocumentRef-D:LicenseRef-", "INVALID_REFERENCE", 0, 25, TestName = "D1_EmptyQualifiedLicenseComponent")]
        [TestCase("DocumentRef-D:DocumentRef-E:LicenseRef-X", "INVALID_REFERENCE", 0, 14, TestName = "D1_RepeatedDocumentQualifier")]
        [TestCase("LicenseRef-A_B", "INVALID_CHARACTER", 12, 1, TestName = "D1_UnderscoreInReference")]
        [Property("Source", AnnexD + "#d1-overview")]
        [Property("Profile", "Reference components/prefixes and contiguous colon; docs/syntax.md")]
        public void ReferenceBoundaries(string input, string code, int start, int length) {
            Reject(input, code, start, length);
        }
        [TestCase("MIT\vOR ISC", 3, TestName = "Profile_VerticalTab")]
        [TestCase("MIT\fOR ISC", 3, TestName = "Profile_FormFeed")]
        [TestCase("MIT\u2028OR ISC", 3, TestName = "Profile_UnicodeLineSeparator")]
        [TestCase("MIT AND \U0001F600-Unknown", 8, TestName = "Profile_SurrogatePairAfterOperator")]
        [TestCase("\U0001F600 OR MIT", 0, TestName = "Profile_LeadingSurrogatePair")]
        [Property("Profile", "ASCII lexical policy and first-invalid UTF-16 unit; docs/syntax.md")]
        public void InvalidCharactersStopBeforeLaterEvidence(string input, int start) {
            Reject(input, "INVALID_CHARACTER", start, 1);
        }
        [TestCase("NOASSERTION", 0, 11, TestName = "Profile_NoAssertionOperand")]
        [TestCase("NONE OR MIT", 0, 4, TestName = "Profile_NoneBeforeOperator")]
        [TestCase("(NONE)", 1, 4, TestName = "Profile_GroupedNone")]
        [TestCase("MIT WITH NOASSERTION", 9, 11, TestName = "Profile_NoAssertionException")]
        [Property("Profile", "Document field sentinels are not expression operands; docs/syntax.md")]
        public void FieldSentinelsAreNotExpressions(string input, int start, int length) {
            Reject(input, "FIELD_SENTINEL", start, length);
        }
        [TestCase("ANDROID OR ORACLE AND WITH-extra", "O(L:ANDROID,A(L:ORACLE,L:WITH-extra))", TestName = "Profile_KeywordsAreWholeTokens")]
        [TestCase("DocumentRef-.:LicenseRef--", "R:.:-", TestName = "D1_PunctuationReferenceComponents")]
        [Property("Source", AnnexD + "#d1-overview")]
        [Property("Profile", "Identifier candidates and reserved whole tokens; docs/syntax.md")]
        public void IdentifierAndKeywordBoundaries(string input, string expected) {
            var expression = GrammarTests.Parse(input);
            Assert.That(GrammarTests.Describe(expression.Root), Is.EqualTo(expected));
        }
        [Test, Property("Source", AnnexD + "#d1-overview")]
        [Property("Profile", "ASCII whitespace and source-preserving formatting; docs/syntax.md")]
        public void Profile_WhitespaceDoesNotApplyTagValueLineRestrictions() {
            const string input = "\tmit\r\nAND\t( LicenseRef-A OR gpl-2.0+ WITH llvm-exception )\r";
            const string structure = "A(L:mit,G(O(R::A,W(L:gpl-2.0+,llvm-exception))))";
            const string formatted = "mit AND (LicenseRef-A OR gpl-2.0+ WITH llvm-exception)";
            var expression = GrammarTests.Parse(input);
            Assert.That(GrammarTests.Describe(expression.Root), Is.EqualTo(structure));
            Assert.That(expression.SourceText, Is.SameAs(input));
            string output = SpdxExpressionFormatter.Format(expression);
            Assert.That(output, Is.EqualTo(formatted));
            var reparsed = GrammarTests.Parse(output);
            Assert.That(GrammarTests.Describe(reparsed.Root), Is.EqualTo(structure));
            Assert.That(SpdxExpressionFormatter.Format(reparsed), Is.EqualTo(formatted));
        }
        [Test, Property("Source", AnnexD + "#d2-case-sensitivity")]
        public void D2_CatalogueMatchingPreservesOriginalEvidence() {
            const string input = "mIt WITH lLvM-eXcEpTiOn OR DocumentRef-a.1:LicenseRef-Z-9";
            var expression = GrammarTests.Parse(input);
            var result = SpdxExpressionValidator.Validate(expression, SpdxLicenseRegistry.Default);
            Assert.That(result.HasIdentifierErrors, Is.False);
            Assert.That(result.HasUnresolvedReferences, Is.True);
            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(result.IdentifierOccurrences.Select(x => x.Identifier), Is.EqualTo(new[] { "mIt", "lLvM-eXcEpTiOn" }));
            Assert.That(result.IdentifierOccurrences.Select(x => x.CanonicalIdentifier), Is.EqualTo(new[] { "MIT", "LLVM-exception" }));
            Assert.That(result.IdentifierOccurrences.Select(x => (x.Span.Start, x.Span.Length)), Is.EqualTo(new[] { (0, 3), (9, 14) }));
            var reference = result.References.Single();
            Assert.That((reference.DocumentId, reference.LicenseId), Is.EqualTo(("a.1", "Z-9")));
            GrammarTests.Span(reference.Span, 27, 30);
            Assert.That(SpdxExpressionFormatter.Format(expression), Is.EqualTo(input));
        }
        [Test, Property("Source", AnnexD + "#d1-overview")]
        [Property("Profile", "Syntax candidates are independent of catalogue membership; docs/syntax.md")]
        public void Profile_IdentifierCandidatesDoNotRequireCatalogueMembership() {
            const string input = "Vendor-9.7+ WITH Vendor-exception OR DocumentRef-Z.9:LicenseRef-a-1";
            var expression = GrammarTests.Parse(input);
            Assert.That(GrammarTests.Describe(expression.Root), Is.EqualTo("O(W(L:Vendor-9.7+,Vendor-exception),R:Z.9:a-1)"));
            var registry = SpdxLicenseRegistry.Default;
            var result = SpdxExpressionValidator.Validate(expression, registry);
            Assert.That(result.RegistryIdentity, Is.SameAs(registry.Identity));
            Assert.That(result.RegistryIdentity.LicenseListVersion, Is.EqualTo("3.29.0"));
            Assert.That(result.HasIdentifierErrors, Is.True);
            Assert.That(result.HasUnresolvedReferences, Is.True);
            Assert.That(result.DiagnosticsTruncated, Is.False);
            Assert.That(result.IdentifierOccurrences.Select(x => x.LookupStatus), Is.EqualTo(new[] { SpdxIdentifierLookupStatus.Unknown, SpdxIdentifierLookupStatus.Unknown }));
            Assert.That(result.Diagnostics.Select(x => x.Code), Is.EqualTo(new[] { "SPDX_ID_UNKNOWN_LICENSE", "SPDX_ID_UNKNOWN_EXCEPTION" }));
            Assert.That(result.IdentifierOccurrences.Select(x => (x.Span.Start, x.Span.Length)), Is.EqualTo(new[] { (0, 10), (17, 16) }));
            Assert.That(SpdxExpressionFormatter.Format(expression), Is.EqualTo(input));
        }
        static void CheckExample(string input, string structure, int identifiers, int references) {
            var expression = GrammarTests.Parse(input);
            Assert.That(GrammarTests.Describe(expression.Root), Is.EqualTo(structure));
            Assert.That(expression.SourceText, Is.SameAs(input));
            Assert.That(SpdxExpressionFormatter.Format(expression), Is.EqualTo(input));
            var registry = SpdxLicenseRegistry.Default;
            var validation = SpdxExpressionValidator.Validate(expression, registry);
            Assert.That(validation.RegistryIdentity, Is.SameAs(registry.Identity));
            Assert.That(validation.RegistryIdentity.LicenseListVersion, Is.EqualTo("3.29.0"));
            Assert.That(validation.IdentifierOccurrences, Has.Count.EqualTo(identifiers));
            Assert.That(validation.IdentifierOccurrences.All(x => x.LookupStatus == SpdxIdentifierLookupStatus.Known), Is.True);
            Assert.That(validation.References, Has.Count.EqualTo(references));
            Assert.That(validation.HasIdentifierErrors, Is.False);
            Assert.That(validation.HasUnresolvedReferences, Is.EqualTo(references != 0));
            Assert.That(validation.DiagnosticsTruncated, Is.False);
            Assert.That(validation.Diagnostics, Is.Empty);
        }
        static void Reject(string input, string code, int start, int length) {
            var result = SpdxExpressionParser.Parse(input);
            Assert.That(result.Status, Is.EqualTo(SpdxParseStatus.InvalidSyntax));
            Assert.That(result.Expression, Is.Null);
            Assert.That(result.Diagnostics, Has.Count.EqualTo(1));
            Assert.That(result.Diagnostics[0].Code, Is.EqualTo("SPDX_PARSE_" + code));
            GrammarTests.Span(result.Diagnostics[0].Span, start, length);
        }
    }
}
