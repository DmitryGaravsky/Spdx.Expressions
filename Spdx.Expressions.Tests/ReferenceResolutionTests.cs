using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;

namespace Spdx.Expressions.Tests {
    public class ReferenceResolutionTests {
        [Test]
        public void DocumentScopedLookupsPreserveDefinitionsNodesAndAbsoluteSpans() {
            const string source = "  LicenseRef-X OR (DocumentRef-D:LicenseRef-X WITH LLVM-exception) OR LicenseRef-X ";
            const string terms = " \r\nVerbatim \U0001f4dc\t\n";
            var expression = SpdxLicenseFieldReader.Read(source).Expression!;
            var a = new SpdxLicenseDefinition("urn:doc:A", "X", terms, "Original name", "https://example.org/terms");
            var b = new SpdxLicenseDefinition("urn:doc:B", "X", "Other text.");
            var context = SpdxReferenceContext.Create(["urn:doc:A", "urn:doc:B"], [a, b],
                [new("urn:doc:A", "D", "urn:doc:B"), new("urn:doc:B", "D", "urn:doc:A")]);
            var fromA = context.Resolve(expression, "urn:doc:A");
            var fromB = context.Resolve(expression, "urn:doc:B");
            Assert.That(fromA.Select(r => r.Status), Is.All.EqualTo(SpdxReferenceResolutionStatus.Resolved));
            Assert.That(fromA.Select(r => r.SourceDocumentIdentity), Is.All.EqualTo("urn:doc:A"));
            Assert.That(fromA.Select(r => r.TargetDocumentIdentity), Is.EqualTo(new[] { "urn:doc:A", "urn:doc:B", "urn:doc:A" }));
            Assert.That(fromB.Select(r => r.TargetDocumentIdentity), Is.EqualTo(new[] { "urn:doc:B", "urn:doc:A", "urn:doc:B" }));
            Assert.That(fromA[0].Definition, Is.SameAs(a));
            Assert.That(fromA[1].Definition, Is.SameAs(b));
            Assert.That(fromA[2].Definition, Is.SameAs(a));
            Assert.That(fromB[1].Definition, Is.SameAs(a));
            Assert.That(fromB[1].Definition!.ExtractedText, Is.EqualTo(terms));
            Assert.That(fromB[1].Definition!.Name, Is.EqualTo("Original name"));
            Assert.That(fromB[1].Definition!.CrossReference, Is.EqualTo("https://example.org/terms"));
            var originalNodes = expression.Root.EnumerateNodes().OfType<SpdxReferenceExpression>().ToArray();
            Assert.That(fromA.Select(r => r.Occurrence), Is.EqualTo(originalNodes));
            GrammarTests.Span(fromA[0].Occurrence.Span, 2, 12);
            GrammarTests.Span(fromA[1].Occurrence.Span, 19, 26);
            GrammarTests.Span(fromA[1].Occurrence.DocumentIdSpan!.Value, 31, 1);
            GrammarTests.Span(fromA[1].Occurrence.LicenseIdSpan, 44, 1);
            GrammarTests.Span(fromA[2].Occurrence.Span, 70, 12);
        }
        [TestCase("LicenseRef-x", SpdxReferenceResolutionStatus.DefinitionMissing, "src")]
        [TestCase("DocumentRef-d:LicenseRef-X", SpdxReferenceResolutionStatus.UnknownDocumentAlias, null)]
        [TestCase("DocumentRef-absent:LicenseRef-X", SpdxReferenceResolutionStatus.DocumentUnavailable, "VENDOR")]
        [TestCase("DocumentRef-D:LicenseRef-X", SpdxReferenceResolutionStatus.DefinitionMissing, "vendor")]
        [TestCase("DocumentRef-next:LicenseRef-X", SpdxReferenceResolutionStatus.UnknownDocumentAlias, null)]
        public void FailuresIdentifyOnlyTheTargetEstablishedByTheSource(string input, SpdxReferenceResolutionStatus status, string? target) {
            var context = SpdxReferenceContext.Create(["src", "vendor"], [new("src", "X", "Local text")],
                [new("src", "D", "vendor"), new("src", "absent", "VENDOR"), new("vendor", "next", "src")]);
            var result = context.Resolve(GrammarTests.Parse(input), "src").Single();
            Assert.That(result.SourceDocumentIdentity, Is.EqualTo("src"));
            Assert.That(result.TargetDocumentIdentity, Is.EqualTo(target));
            Assert.That(result.Status, Is.EqualTo(status));
            Assert.That(result.Definition, Is.Null);
        }
        [TestCase(false)]
        [TestCase(true)]
        public void IdenticalAndConflictingDeclarationsRemainAmbiguous(bool identical) {
            var a = new SpdxLicenseDefinition("a", "X", "text");
            var other = new SpdxLicenseDefinition("a", "X", identical ? "text" : "another text");
            var alias = new SpdxDocumentBinding("b", "D", "a");
            var otherAlias = new SpdxDocumentBinding("b", "D", identical ? "a" : "absent");
            var expression = GrammarTests.Parse("DocumentRef-D:LicenseRef-X OR DocumentRef-good:LicenseRef-X OR LicenseRef-X");
            foreach(bool reverse in new[] { false, true }) {
                var context = SpdxReferenceContext.Create(["a", "b"],
                    [reverse ? other : a, reverse ? a : other, a, new("b", "X", "local")],
                    [reverse ? otherAlias : alias, reverse ? alias : otherAlias, alias, new("b", "good", "a")]);
                var results = context.Resolve(expression, "b");
                Assert.That(results.Select(r => r.Status), Is.EqualTo(new[] { SpdxReferenceResolutionStatus.DuplicateBinding,
                    SpdxReferenceResolutionStatus.DuplicateDefinition, SpdxReferenceResolutionStatus.Resolved }));
                Assert.That(results[0].TargetDocumentIdentity, Is.Null);
                Assert.That(results[1].TargetDocumentIdentity, Is.EqualTo("a"));
                Assert.That(results[0].Definition, Is.Null);
                Assert.That(results[1].Definition, Is.Null);
            }
        }
        [Test]
        public void ContextOwnsItsIndexAndResultsAreReadOnly() {
            var documents = new List<string> { "s" };
            var definition = new SpdxLicenseDefinition("s", "LicenseRef-X", "original");
            var definitions = new List<SpdxLicenseDefinition> { definition };
            var bindings = new List<SpdxDocumentBinding> { new("s", "D.1-2", "s") };
            var context = SpdxReferenceContext.Create(documents, definitions, bindings);
            documents.Clear();
            definitions[0] = new("s", "LicenseRef-X", "replacement");
            bindings.Clear();
            var expression = GrammarTests.Parse("DocumentRef-D.1-2:LicenseRef-LicenseRef-X");
            var results = context.Resolve(expression, "s");
            Assert.That(results.Single().Definition, Is.SameAs(definition));
            RegistryTests.AssertReadOnly((System.Collections.IList)results);
            Assert.That(context.Resolve(GrammarTests.Parse("MIT"), "s"), Is.Empty);
        }
        [Test]
        public void TruncatedValidationRetainsIndependentOutcomes() {
            var expression = GrammarTests.Parse("Unknown OR Missing OR LicenseRef-X OR LicenseRef-X");
            var validation = SpdxExpressionValidator.Validate(expression, SpdxLicenseRegistry.Default, new() { MaxDiagnostics = 1 });
            var context = SpdxReferenceContext.Create(["s"], [new("s", "X", "text")], []);
            var results = context.Resolve(expression, "s");
            Assert.That(results.Select(r => r.Occurrence.Span.Start), Is.EqualTo(new[] { 22, 38 }));
            Assert.That(results.Select(r => r.Status), Is.All.EqualTo(SpdxReferenceResolutionStatus.Resolved));
            Assert.That(validation.HasIdentifierErrors, Is.True);
            Assert.That(validation.DiagnosticsTruncated, Is.True);
            Assert.That(validation.HasUnresolvedReferences, Is.True);
            Assert.That(validation.References, Has.Count.EqualTo(2));
        }
        [TestCase("")]
        [TestCase("a+b")]
        [TestCase("a:b")]
        [TestCase(" x")]
        [TestCase("é")]
        public void InvalidComponentsAreRejected(string component) {
            Assert.Throws<ArgumentException>(() => SpdxReferenceContext.Create(["a"], [new("a", component, "text")], []));
            Assert.Throws<ArgumentException>(() => SpdxReferenceContext.Create(["a"], [], [new("a", component, "a")]));
        }
        [Test]
        public void RejectsInvalidOwnersIdentitiesAndRequiredFields() {
            Assert.Throws<ArgumentException>(() => SpdxReferenceContext.Create(["a", "a"], [], []));
            Assert.Throws<ArgumentException>(() => SpdxReferenceContext.Create([""], [], []));
            Assert.Throws<ArgumentException>(() => SpdxReferenceContext.Create(["a"], [new("A", "X", "text")], []));
            Assert.Throws<ArgumentException>(() => SpdxReferenceContext.Create(["a"], [], [new("A", "D", "a")]));
            Assert.Throws<ArgumentException>(() => SpdxReferenceContext.Create(["a"], [], [new("a", "D", "")]));
            Assert.Throws<ArgumentException>(() => SpdxReferenceContext.Create(["a"], [new("a", "X", "")], []));
            Assert.Throws<ArgumentNullException>(() => SpdxReferenceContext.Create(null!, [], []));
            Assert.Throws<ArgumentNullException>(() => SpdxReferenceContext.Create(["a"], [null!], []));
            Assert.Throws<ArgumentNullException>(() => SpdxReferenceContext.Create(["a"], [], [null!]));
            var context = SpdxReferenceContext.Create(["a"], [], []);
            Assert.Throws<ArgumentNullException>(() => context.Resolve(null!, "a"));
            Assert.Throws<ArgumentException>(() => context.Resolve(GrammarTests.Parse("MIT"), "A"));
        }
        [Test]
        public void BudgetsChargeAllEntriesAndEveryUtf16FieldBeforeIndexing() {
            SpdxLicenseDefinition[] definitions = [new("a", "X", "\U0001f4dc", "n", "url")];
            SpdxDocumentBinding[] bindings = [new("a", "D", "b")];
            var context = SpdxReferenceContext.Create(["a"], definitions, bindings, new() { MaxEntries = 3, MaxCharacters = 12 });
            Assert.That(context.Resolve(GrammarTests.Parse("LicenseRef-X"), "a").Single().Definition, Is.SameAs(definitions[0]));
            foreach(int limit in new[] { 0, 2 }) {
                Assert.Throws<ArgumentOutOfRangeException>(() => SpdxReferenceContext.Create(["a"], definitions, bindings, new() { MaxEntries = limit }));
            }
            foreach(int limit in new[] { 0, 11 }) {
                Assert.Throws<ArgumentOutOfRangeException>(() => SpdxReferenceContext.Create(["a"], definitions, bindings, new() { MaxCharacters = limit }));
            }
            var duplicates = Enumerable.Repeat(definitions[0], int.MaxValue);
            Assert.Throws<ArgumentOutOfRangeException>(() => SpdxReferenceContext.Create(["a"], duplicates, [], new() { MaxEntries = 3 }));
        }
        [Test]
        public void CancellationIsObservedAtEntryAndDuringContextEnumeration() {
            var cancelled = new CancellationToken(true);
            Assert.Throws<OperationCanceledException>(() => SpdxReferenceContext.Create([], [], [], cancellationToken: cancelled));
            var context = SpdxReferenceContext.Create(["a"], [], []);
            var expression = GrammarTests.Parse("LicenseRef-X");
            Assert.Throws<OperationCanceledException>(() => context.Resolve(expression, "a", cancelled));
            using(var source = new CancellationTokenSource()) {
                var error = Assert.Throws<OperationCanceledException>(() => SpdxReferenceContext.Create(["a"], [], Bindings(), cancellationToken: source.Token));
                Assert.That(error!.CancellationToken, Is.EqualTo(source.Token));
                IEnumerable<SpdxDocumentBinding> Bindings() {
                    yield return new("a", "D", "b");
                    source.Cancel();
                }
            }
        }
    }
}
