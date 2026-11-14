using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Spdx.Expressions.Tests {
    public class ResourceTests {
        [TestCase("input", "MIT ", 0, 4)]
        [TestCase("identifier", "ABCD", 0, 4)]
        [TestCase("reference", "LicenseRef-ABCD", 11, 4)]
        [TestCase("tokens", "(A)+", 3, 1)]
        [TestCase("nodes", "(A AND B)", 7, 1)]
        [TestCase("depth", "((((A))))", 3, 1)]
        public void LimitDiagnosticSpansAreStable(string budget, string input, int start, int length) {
            var options = budget switch {
                "input" => new SpdxParseOptions { MaxInputLength = 3 },
                "identifier" or "reference" => new SpdxParseOptions { MaxIdentifierLength = 3 },
                "tokens" => new SpdxParseOptions { MaxTokens = 3 },
                "nodes" => new SpdxParseOptions { MaxNodes = 3 },
                "depth" => new SpdxParseOptions { MaxParenthesisDepth = 3 },
                _ => throw new InvalidOperationException()
            };
            var result = SpdxExpressionParser.Parse(input, options);
            Assert.That(result.Status, Is.EqualTo(SpdxParseStatus.LimitExceeded));
            GrammarTests.Span(result.Diagnostics.Single().Span, start, length);
        }
        [TestCase("input", "MIT", "MIT ")]
        [TestCase("identifier", "ABC", "ABCD")]
        [TestCase("tokens", "(A)", "(A)+")]
        [TestCase("nodes", "A AND B", "(A AND B)")]
        [TestCase("depth", "(((A)))", "((((A))))")]
        public void EveryBudgetIsInclusive(string budget, string boundary, string over) {
            var options = budget switch {
                "input" => new SpdxParseOptions { MaxInputLength = 3 },
                "identifier" => new SpdxParseOptions { MaxIdentifierLength = 3 },
                "tokens" => new SpdxParseOptions { MaxTokens = 3 },
                "nodes" => new SpdxParseOptions { MaxNodes = 3 },
                "depth" => new SpdxParseOptions { MaxParenthesisDepth = 3 },
                _ => throw new InvalidOperationException()
            };
            GrammarTests.Parse(boundary, options);
            AssertLimit(over, options);
        }
        [Test]
        public void TokenAndNodeAccounting() {
            GrammarTests.Parse("DocumentRef-D:LicenseRef-X", new() { MaxTokens = 1, MaxNodes = 1, MaxIdentifierLength = 1 });
            GrammarTests.Parse("A+ WITH B", new() { MaxTokens = 4, MaxNodes = 2, MaxIdentifierLength = 1 });
            AssertLimit("A+ WITH B", new() { MaxTokens = 3 });
            AssertLimit("A+ WITH B", new() { MaxNodes = 1 });
            GrammarTests.Parse("A AND B AND C", new() { MaxNodes = 4 });
            AssertLimit("A AND B AND C", new() { MaxNodes = 3 });
            GrammarTests.Parse("LicenseRef-ABC", new() { MaxIdentifierLength = 3 });
            AssertLimit("LicenseRef-ABCD", new() { MaxIdentifierLength = 3 });
            GrammarTests.Parse("DocumentRef-ABC:LicenseRef-ABC", new() { MaxIdentifierLength = 3 });
            AssertLimit("DocumentRef-ABCD:LicenseRef-X", new() { MaxIdentifierLength = 3 });
            AssertLimit("DocumentRef-X:LicenseRef-ABCD", new() { MaxIdentifierLength = 3 });
            GrammarTests.Parse("MIT", new() { MaxInputLength = int.MaxValue, MaxTokens = int.MaxValue, MaxNodes = int.MaxValue, MaxIdentifierLength = int.MaxValue });
        }
        [Test]
        public void DefaultLimitsAndMaximumDepth() {
            GrammarTests.Parse(new string(' ', 65_533) + "MIT");
            AssertLimit(new string(' ', 65_534) + "MIT", new());
            GrammarTests.Parse(new string('A', 256));
            AssertLimit(new string('A', 257), new());
            GrammarTests.Parse(new string('(', 64) + "MIT" + new string(')', 64));
            AssertLimit(new string('(', 65) + "MIT" + new string(')', 65), new());
            var deepest = GrammarTests.Parse(new string('(', 256) + "MIT" + new string(')', 256), new() { MaxParenthesisDepth = 256 });
            Assert.That(SpdxExpressionFormatter.Format(deepest), Is.EqualTo(deepest.SourceText));
            Assert.That(SpdxExpressionValidator.Validate(deepest, SpdxLicenseRegistry.Default).HasIdentifierErrors, Is.False);
            AssertLimit(new string('(', 257) + "MIT" + new string(')', 257), new() { MaxParenthesisDepth = 256 });
            GrammarTests.Parse(string.Join(" OR ", Enumerable.Repeat("A", 8191)));
            AssertLimit(string.Join(" OR ", Enumerable.Repeat("A", 8192)), new());
            string tokens = string.Join(" OR ", Enumerable.Repeat("A", 8192));
            GrammarTests.Parse(tokens + "+", new() { MaxNodes = 20_000 }); // 16,384 tokens.
            AssertLimit("(" + tokens + ")", new() { MaxNodes = 20_000 }); // 16,385 tokens.
        }
        [Test]
        public void LargeFlatChainsDoNotUseOperandRecursion() {
            string input = string.Join(" OR ", Enumerable.Repeat("MIT", 30_000));
            var expression = GrammarTests.Parse(input, new() { MaxInputLength = input.Length, MaxNodes = 30_001, MaxTokens = 60_000 });
            Assert.That(((SpdxOrExpression)expression.Root).Operands.Count, Is.EqualTo(30_000));
            Assert.That(SpdxExpressionFormatter.Format(expression), Is.EqualTo(input));
            Assert.That(SpdxExpressionValidator.Validate(expression, SpdxLicenseRegistry.Default).IdentifierOccurrences.Count, Is.EqualTo(30_000));
        }
        [Test]
        public void NullAndInvalidOptionsAreArgumentExceptions() {
            var expression = GrammarTests.Parse("MIT");
            Assert.Throws<ArgumentNullException>(() => SpdxExpressionParser.Parse(null!));
            Assert.Throws<ArgumentNullException>(() => SpdxExpressionFormatter.Format(null!));
            Assert.Throws<ArgumentNullException>(() => SpdxExpressionValidator.Validate(null!, SpdxLicenseRegistry.Default));
            Assert.Throws<ArgumentNullException>(() => SpdxExpressionValidator.Validate(expression, null!));
            Assert.Throws<ArgumentNullException>(() => SpdxLicenseRegistry.Default.TryGetLicense(null!, out _));
            Assert.Throws<ArgumentNullException>(() => SpdxLicenseRegistry.Default.TryGetException(null!, out _));
            foreach(int invalid in new[] { 0, -1, int.MinValue }) {
                foreach(var options in new[] { new SpdxParseOptions { MaxInputLength = invalid }, new SpdxParseOptions { MaxIdentifierLength = invalid },
                new SpdxParseOptions { MaxTokens = invalid }, new SpdxParseOptions { MaxNodes = invalid }, new SpdxParseOptions { MaxParenthesisDepth = invalid } })
                    Assert.Throws<ArgumentOutOfRangeException>(() => SpdxExpressionParser.Parse("MIT", options));
                Assert.Throws<ArgumentOutOfRangeException>(() => SpdxExpressionValidator.Validate(expression, SpdxLicenseRegistry.Default, new() { MaxDiagnostics = invalid }));
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => SpdxExpressionParser.Parse("MIT", new() { MaxParenthesisDepth = 257 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => SpdxExpressionParser.Parse("MIT", new() { MaxParenthesisDepth = int.MaxValue }));
        }
        [Test]
        public void PreCancelledOperationsThrowWithoutResults() {
            var token = new CancellationToken(true);
            var expression = GrammarTests.Parse("MIT");
            Assert.Throws<OperationCanceledException>(() => SpdxExpressionParser.Parse("MIT", cancellationToken: token));
            Assert.Throws<OperationCanceledException>(() => SpdxExpressionValidator.Validate(expression, SpdxLicenseRegistry.Default, cancellationToken: token));
            Assert.Throws<OperationCanceledException>(() => SpdxExpressionFormatter.Format(expression, token));
        }
        [TestCase(false)]
        [TestCase(true)]
        public void CancellationDuringLongScans(bool candidate) {
            string input = new(candidate ? 'A' : ' ', 32 * 1024 * 1024);
            AssertCancellation(token => SpdxExpressionParser.Parse(input,
                new() { MaxInputLength = input.Length, MaxIdentifierLength = input.Length }, token));
        }
        [TestCase(false)]
        [TestCase(true)]
        public void CancellationDuringTraversal(bool format) {
            string input = string.Join(" OR ", Enumerable.Repeat("Unknown", 200_000));
            var expression = GrammarTests.Parse(input, new() { MaxInputLength = input.Length, MaxNodes = 200_001, MaxTokens = 400_000 });
            var registry = SpdxLicenseRegistry.Default;
            AssertCancellation(token => format ? SpdxExpressionFormatter.Format(expression, token) :
                (object)SpdxExpressionValidator.Validate(expression, registry, new() { MaxDiagnostics = 1 }, token));
        }
        static void AssertCancellation(Func<CancellationToken, object> operation) {
            // Do not use Thread.Sleep: Windows timer granularity can let the entire operation finish.
            // Retry only a completed-before-cancel scheduling race, never an observed cancellation failure.
            for(int attempt = 0; attempt < 3; attempt++) {
                using(var cancellation = new CancellationTokenSource())
                using(var started = new ManualResetEventSlim()) {
                    var task = Task.Factory.StartNew(() => {
                        started.Set();
                        return operation(cancellation.Token);
                    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                    try {
                        Assert.That(started.Wait(TimeSpan.FromSeconds(5)), Is.True);
                        long timestamp = System.Diagnostics.Stopwatch.GetTimestamp();
                        while(System.Diagnostics.Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds < 0.1)
                            Thread.SpinWait(16);
                        if(task.IsCompleted) {
                            task.GetAwaiter().GetResult();
                            continue;
                        }
                        cancellation.Cancel();
                        Assert.Throws<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
                        return;
                    }
                    finally { cancellation.Cancel(); }
                }
            }
            Assert.Fail("Unable to overlap cancellation with the operation in three bounded attempts.");
        }
        static void AssertLimit(string input, SpdxParseOptions options) {
            var result = SpdxExpressionParser.Parse(input, options);
            Assert.That(result.Status, Is.EqualTo(SpdxParseStatus.LimitExceeded));
            Assert.That(result.Expression, Is.Null);
            Assert.That(result.Diagnostics.Single().Code, Is.EqualTo("SPDX_PARSE_LIMIT_EXCEEDED"));
            Assert.That(result.Diagnostics[0].Span.Start + (long)result.Diagnostics[0].Span.Length, Is.LessThanOrEqualTo(input.Length));
        }
    }
}
