using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;

namespace Spdx.Expressions.Tests {
    public class TraversalTests {
        const string Composite = "  ( mit+ WITH eX  OR DocumentRef-D:LicenseRef-X OR mit+ ) AND LicenseRef-Y WITH q AND (ISC) ";
        static readonly (SpdxExpressionKind Kind, int Start, int Length, string Source)[] Expected = {
            (SpdxExpressionKind.And, 2, 89, "( mit+ WITH eX  OR DocumentRef-D:LicenseRef-X OR mit+ ) AND LicenseRef-Y WITH q AND (ISC)"),
            (SpdxExpressionKind.Group, 2, 55, "( mit+ WITH eX  OR DocumentRef-D:LicenseRef-X OR mit+ )"),
            (SpdxExpressionKind.Or, 4, 51, "mit+ WITH eX  OR DocumentRef-D:LicenseRef-X OR mit+"),
            (SpdxExpressionKind.With, 4, 12, "mit+ WITH eX"),
            (SpdxExpressionKind.License, 4, 4, "mit+"),
            (SpdxExpressionKind.Reference, 21, 26, "DocumentRef-D:LicenseRef-X"),
            (SpdxExpressionKind.License, 51, 4, "mit+"),
            (SpdxExpressionKind.With, 62, 19, "LicenseRef-Y WITH q"),
            (SpdxExpressionKind.Reference, 62, 12, "LicenseRef-Y"),
            (SpdxExpressionKind.Group, 86, 5, "(ISC)"),
            (SpdxExpressionKind.License, 87, 3, "ISC")
        };
        [Test]
        public void BothEntryPointsVisitOriginalNodesInPreOrder() {
            var expression = GrammarTests.Parse(Composite);
            var original = CompositeNodes(expression);
            Assert.That(original.Distinct(ReferenceEqualityComparer.Instance).Count(), Is.EqualTo(Expected.Length));
            var enumerated = expression.Root.EnumerateNodes().ToArray();
            var walker = new TypedWalker();
            walker.Walk(expression.Root);
            Assert.That(walker.Visits.Select(visit => visit.Kind), Is.EqualTo(Expected.Select(row => row.Kind)));
            CheckVisits(enumerated, original);
            CheckVisits(walker.Visits.Select(visit => visit.Node).ToArray(), original);
            Assert.That(expression.SourceText, Is.EqualTo(Composite));
            for(int i = 0; i < Expected.Length; i++) {
                var row = Expected[i];
                var node = original[i];
                Assert.That(node.Kind, Is.EqualTo(row.Kind));
                GrammarTests.Span(node.Span, row.Start, row.Length);
                Assert.That(expression.SourceText.Substring(node.Span.Start, node.Span.Length), Is.EqualTo(row.Source));
            }
            var licenseWith = (SpdxWithExpression)original[3];
            var referenceWith = (SpdxWithExpression)original[7];
            Assert.That(licenseWith.ExceptionIdentifier, Is.EqualTo("eX"));
            GrammarTests.Span(licenseWith.ExceptionSpan, 14, 2);
            Assert.That(referenceWith.ExceptionIdentifier, Is.EqualTo("q"));
            GrammarTests.Span(referenceWith.ExceptionSpan, 80, 1);
            CheckLicense(original[4], "mit", true, 4);
            CheckLicense(original[6], "mit", true, 51);
            CheckLicense(original[10], "ISC", false, 87);
            var qualified = (SpdxReferenceExpression)original[5];
            Assert.That(qualified.DocumentId, Is.EqualTo("D"));
            Assert.That(qualified.LicenseId, Is.EqualTo("X"));
            GrammarTests.Span(qualified.DocumentIdSpan!.Value, 33, 1);
            GrammarTests.Span(qualified.LicenseIdSpan, 46, 1);
            var local = (SpdxReferenceExpression)original[8];
            Assert.That(local.DocumentId, Is.Null);
            Assert.That(local.DocumentIdSpan, Is.Null);
            Assert.That(local.LicenseId, Is.EqualTo("Y"));
            GrammarTests.Span(local.LicenseIdSpan, 73, 1);
        }
        [Test]
        public void BothEntryPointsIncludeSubtreeAndLeafRoots() {
            var root = GrammarTests.Parse(Composite).Root;
            var with = (SpdxWithExpression)((SpdxAndExpression)root).Operands[1];
            var expected = new[] { with, with.Operand };
            var recorder = new Recorder();
            recorder.Walk(with);
            CheckVisits(with.EnumerateNodes().ToArray(), expected);
            CheckVisits(recorder.Nodes, expected);
            var leaf = GrammarTests.Parse("  MIT+ ").Root;
            CheckLicense(leaf, "MIT", true, 2);
            GrammarTests.Span(leaf.Span, 2, 4);
            recorder.Nodes.Clear();
            recorder.Walk(leaf);
            CheckVisits(leaf.EnumerateNodes().ToArray(), new[] { leaf });
            CheckVisits(recorder.Nodes, new[] { leaf });
        }
        [TestCase(false)]
        [TestCase(true)]
        public void TypedOverrideControlsFallbackAndDescentContinues(bool forward) {
            var expression = GrammarTests.Parse(Composite);
            var nodes = CompositeNodes(expression);
            var walker = new WithRecorder(forward);
            walker.Walk(expression.Root);
            CheckVisits(walker.WithNodes, new[] { nodes[3], nodes[7] });
            var fallback = forward ? nodes : new[] { nodes[0], nodes[1], nodes[2], nodes[4], nodes[5], nodes[6], nodes[8], nodes[9], nodes[10] };
            CheckVisits(walker.Nodes, fallback);
        }
        [TestCase(" AND ", 8, SpdxExpressionKind.And)]
        [TestCase(" OR ", 7, SpdxExpressionKind.Or)]
        public void WideChainsVisitEveryOperand(string separator, int stride, SpdxExpressionKind kind) {
            const int Terms = 32_768;
            var parts = new string[Terms];
            Array.Fill(parts, "ISC");
            parts[0] = "mIt";
            parts[^1] = "LicenseRef-Tail WITH eX";
            string input = string.Join(separator, parts);
            var root = GrammarTests.Parse(input, new() { MaxInputLength = input.Length, MaxTokens = 65_537, MaxNodes = 32_770 }).Root;
            var enumerated = root.EnumerateNodes().ToArray();
            var recorder = new Recorder();
            recorder.Walk(root);
            CheckWideChain(enumerated, stride, kind, Terms);
            CheckWideChain(recorder.Nodes, stride, kind, Terms);
        }
        [Test]
        public void MaximumNestingVisitsEveryExplicitGroup() {
            const int Depth = 256;
            string input = new string('(', Depth) + "MIT" + new string(')', Depth);
            var root = GrammarTests.Parse(input, new() { MaxParenthesisDepth = Depth }).Root;
            var enumerated = root.EnumerateNodes().ToArray();
            var recorder = new Recorder();
            recorder.Walk(root);
            CheckGroups(enumerated, Depth);
            CheckGroups(recorder.Nodes, Depth);
        }
        [Test]
        public void PreCanceledEnumerationDefersCancellationUntilAdvance() {
            var root = GrammarTests.Parse(Composite).Root;
            var token = new CancellationToken(true);
            var sequence = root.EnumerateNodes(token);
            using(var unstarted = sequence.GetEnumerator()) {
            }
            using(var enumerator = sequence.GetEnumerator())
                CheckAdvanceCancellation(enumerator, token);
        }
        [Test]
        public void DisposingAfterYieldDoesNotPollAndSequenceCanRestart() {
            var expression = GrammarTests.Parse(Composite);
            var expected = CompositeNodes(expression);
            var sequence = expression.Root.EnumerateNodes();
            using(var first = sequence.GetEnumerator())
                Advance(first, expected[0]);
            CheckVisits(sequence.ToArray(), expected);
            using(var cancellation = new CancellationTokenSource())
            using(var earlyExit = expression.Root.EnumerateNodes(cancellation.Token).GetEnumerator()) {
                Advance(earlyExit, expected[0]);
                cancellation.Cancel();
            }
        }
        [TestCase(1)]
        [TestCase(6)]
        [TestCase(11)]
        public void EnumeratorObservesCancellationOnNextAdvance(int stopAfter) {
            var expression = GrammarTests.Parse(Composite);
            var expected = CompositeNodes(expression);
            using(var cancellation = new CancellationTokenSource())
            using(var enumerator = expression.Root.EnumerateNodes(cancellation.Token).GetEnumerator()) {
                for(int i = 0; i < stopAfter; i++)
                    Advance(enumerator, expected[i]);
                cancellation.Cancel();
                CheckAdvanceCancellation(enumerator, cancellation.Token);
            }
        }
        [Test]
        public void PreCanceledWalkHasNoCallbacks() {
            var root = GrammarTests.Parse(Composite).Root;
            var recorder = new Recorder();
            var token = new CancellationToken(true);
            var exception = Assert.Throws<OperationCanceledException>(() => recorder.Walk(root, token));
            Assert.That(exception!.CancellationToken, Is.EqualTo(token));
            Assert.That(recorder.Nodes, Is.Empty);
        }
        [TestCase(1)]
        [TestCase(6)]
        [TestCase(11)]
        public void WalkerObservesCancellationAfterCallback(int stopAfter) {
            var expression = GrammarTests.Parse(Composite);
            var expected = CompositeNodes(expression);
            var recorder = new Recorder();
            using(var cancellation = new CancellationTokenSource()) {
                recorder.Observe = () => {
                    if(recorder.Nodes.Count == stopAfter)
                        cancellation.Cancel();
                };
                var exception = Assert.Throws<OperationCanceledException>(() => recorder.Walk(expression.Root, cancellation.Token));
                Assert.That(exception!.CancellationToken, Is.EqualTo(cancellation.Token));
                CheckVisits(recorder.Nodes, expected[..stopAfter]);
            }
        }
        [Test]
        public void NullRootWinsOverPreCancellation() {
            var recorder = new Recorder();
            var exception = Assert.Throws<ArgumentNullException>(() => recorder.Walk(null!, new(true)));
            Assert.That(exception!.ParamName, Is.EqualTo("root"));
            Assert.That(recorder.Nodes, Is.Empty);
        }
        [TestCase(false)]
        [TestCase(true)]
        public void CallbackExceptionPropagatesAndWalkerCanBeReused(bool cancelBeforeThrow) {
            var expression = GrammarTests.Parse(Composite);
            var expected = CompositeNodes(expression);
            var recorder = new Recorder();
            var fault = new InvalidOperationException();
            using(var cancellation = new CancellationTokenSource()) {
                recorder.Observe = () => {
                    if(recorder.Nodes.Count != 6)
                        return;
                    if(cancelBeforeThrow)
                        cancellation.Cancel();
                    throw fault;
                };
                var exception = Assert.Throws<InvalidOperationException>(() => recorder.Walk(expression.Root, cancellation.Token));
                Assert.That(exception, Is.SameAs(fault));
                CheckVisits(recorder.Nodes, expected[..6]);
            }
            Reuse(recorder, expression.Root, expected);
        }
        [Test]
        public void WalkerCanBeReusedAfterCancellation() {
            var expression = GrammarTests.Parse(Composite);
            var expected = CompositeNodes(expression);
            var recorder = new Recorder();
            using(var cancellation = new CancellationTokenSource()) {
                recorder.Observe = () => {
                    if(recorder.Nodes.Count == 6)
                        cancellation.Cancel();
                };
                var exception = Assert.Throws<OperationCanceledException>(() => recorder.Walk(expression.Root, cancellation.Token));
                Assert.That(exception!.CancellationToken, Is.EqualTo(cancellation.Token));
                CheckVisits(recorder.Nodes, expected[..6]);
            }
            Reuse(recorder, expression.Root, expected);
        }
        [Test]
        public void InterleavedEnumeratorsHaveIndependentPositionsAndLifetimes() {
            var expression = GrammarTests.Parse(Composite);
            var expected = CompositeNodes(expression);
            var sequence = expression.Root.EnumerateNodes();
            using(var second = sequence.GetEnumerator()) {
                using(var first = sequence.GetEnumerator()) {
                    Advance(first, expected[0]);
                    Advance(first, expected[1]);
                    Advance(second, expected[0]);
                    Assert.That(first.Current, Is.SameAs(expected[1]));
                    Advance(first, expected[2]);
                    Assert.That(second.Current, Is.SameAs(expected[0]));
                    Advance(second, expected[1]);
                    Assert.That(first.Current, Is.SameAs(expected[2]));
                }
                for(int i = 2; i < expected.Length; i++)
                    Advance(second, expected[i]);
                Assert.That(second.MoveNext(), Is.False);
            }
        }
        static SpdxExpressionNode[] CompositeNodes(SpdxParsedExpression expression) {
            var root = (SpdxAndExpression)expression.Root;
            var group = (SpdxGroupExpression)root.Operands[0];
            var or = (SpdxOrExpression)group.Inner;
            var licenseWith = (SpdxWithExpression)or.Operands[0];
            var referenceWith = (SpdxWithExpression)root.Operands[1];
            var last = (SpdxGroupExpression)root.Operands[2];
            return new[] { root, group, or, licenseWith, licenseWith.Operand, or.Operands[1], or.Operands[2],
                referenceWith, referenceWith.Operand, last, last.Inner };
        }
        static void CheckLicense(SpdxExpressionNode node, string identifier, bool orLater, int start) {
            var license = (SpdxLicenseExpression)node;
            Assert.That(license.Identifier, Is.EqualTo(identifier));
            Assert.That(license.OrLater, Is.EqualTo(orLater));
            GrammarTests.Span(license.IdentifierSpan, start, identifier.Length);
        }
        static void CheckVisits(IReadOnlyList<SpdxExpressionNode> visits, IReadOnlyList<SpdxExpressionNode> expected) {
            Assert.That(visits, Has.Count.EqualTo(expected.Count), "Visited node count");
            for(int i = 0; i < expected.Count; i++)
                Assert.That(visits[i], Is.SameAs(expected[i]), $"Visit {i + 1}");
        }
        static void CheckWideChain(IReadOnlyList<SpdxExpressionNode> visits, int stride, SpdxExpressionKind kind, int terms) {
            Assert.That(visits, Has.Count.EqualTo(terms + 2));
            Assert.That(visits[0].Kind, Is.EqualTo(kind));
            for(int i = 0; i < terms - 1; i++) {
                var license = (SpdxLicenseExpression)visits[i + 1];
                Assert.That(license.Identifier, Is.EqualTo(i == 0 ? "mIt" : "ISC"));
                GrammarTests.Span(license.Span, i * stride, 3);
            }
            int tailOffset = (terms - 1) * stride;
            var tail = (SpdxWithExpression)visits[^2];
            Assert.That(tail.ExceptionIdentifier, Is.EqualTo("eX"));
            GrammarTests.Span(tail.Span, tailOffset, 23);
            var reference = (SpdxReferenceExpression)visits[^1];
            Assert.That(reference.LicenseId, Is.EqualTo("Tail"));
            GrammarTests.Span(reference.Span, tailOffset, 15);
        }
        static void CheckGroups(IReadOnlyList<SpdxExpressionNode> visits, int depth) {
            Assert.That(visits, Has.Count.EqualTo(depth + 1));
            for(int i = 0; i < depth; i++) {
                Assert.That(visits[i], Is.TypeOf<SpdxGroupExpression>());
                GrammarTests.Span(visits[i].Span, i, 515 - 2 * i);
            }
            CheckLicense(visits[^1], "MIT", false, 256);
            GrammarTests.Span(visits[^1].Span, 256, 3);
        }
        static void Advance(IEnumerator<SpdxExpressionNode> enumerator, SpdxExpressionNode expected) {
            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(enumerator.Current, Is.SameAs(expected));
        }
        static void CheckAdvanceCancellation(IEnumerator<SpdxExpressionNode> enumerator, CancellationToken token) {
            var exception = Assert.Throws<OperationCanceledException>(() => {
                enumerator.MoveNext();
            }, "The next advance must observe cancellation.");
            Assert.That(exception!.CancellationToken, Is.EqualTo(token));
        }
        static void Reuse(Recorder recorder, SpdxExpressionNode root, SpdxExpressionNode[] expected) {
            recorder.Observe = null;
            recorder.Nodes.Clear();
            using(var fresh = new CancellationTokenSource())
                recorder.Walk(root, fresh.Token);
            CheckVisits(recorder.Nodes, expected);
        }
        class Recorder : SpdxExpressionWalker {
            internal List<SpdxExpressionNode> Nodes { get; } = new();
            internal Action? Observe { get; set; }
            protected override void VisitNode(SpdxExpressionNode node) {
                Nodes.Add(node);
                Observe?.Invoke();
            }
        }
        sealed class WithRecorder : Recorder {
            readonly bool forward;
            internal WithRecorder(bool forward) {
                this.forward = forward;
            }
            internal List<SpdxExpressionNode> WithNodes { get; } = new();
            protected override void VisitWith(SpdxWithExpression node) {
                WithNodes.Add(node);
                if(forward)
                    base.VisitWith(node);
            }
        }
        sealed class TypedWalker : SpdxExpressionWalker {
            internal List<(SpdxExpressionKind Kind, SpdxExpressionNode Node)> Visits { get; } = new();
            protected override void VisitLicense(SpdxLicenseExpression node) {
                Visits.Add((SpdxExpressionKind.License, node));
            }
            protected override void VisitReference(SpdxReferenceExpression node) {
                Visits.Add((SpdxExpressionKind.Reference, node));
            }
            protected override void VisitWith(SpdxWithExpression node) {
                Visits.Add((SpdxExpressionKind.With, node));
            }
            protected override void VisitAnd(SpdxAndExpression node) {
                Visits.Add((SpdxExpressionKind.And, node));
            }
            protected override void VisitOr(SpdxOrExpression node) {
                Visits.Add((SpdxExpressionKind.Or, node));
            }
            protected override void VisitGroup(SpdxGroupExpression node) {
                Visits.Add((SpdxExpressionKind.Group, node));
            }
        }
    }
}
