using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Spdx.Expressions.Tests {
    public class FormattingTests {
        [TestCase(" mit  OR Apache-2.0 ", "mit OR Apache-2.0")]
        [TestCase("( MIT ) AND MIT", "(MIT) AND MIT")]
        [TestCase("GPL-2.0+\tWITH Classpath-exception-2.0", "GPL-2.0+ WITH Classpath-exception-2.0")]
        [TestCase(" LicenseRef-X\r\nOR\tUnknown-1.0", "LicenseRef-X OR Unknown-1.0")]
        [TestCase("DocumentRef-d:LicenseRef-x WITH llvm-exception", "DocumentRef-d:LicenseRef-x WITH llvm-exception")]
        [TestCase("(( MIT))OR(MIT AND MIT)", "((MIT)) OR (MIT AND MIT)")]
        [TestCase("GPL-2.0 OR GPL-2.0", "GPL-2.0 OR GPL-2.0")]
        [TestCase(" mit+", "mit+")]
        [TestCase("LicenseRef-x\t", "LicenseRef-x")]
        [TestCase("\r\nDocumentRef-d:LicenseRef-x ", "DocumentRef-d:LicenseRef-x")]
        public void IndependentGoldenOutputs(string input, string expected) {
            var expression = GrammarTests.Parse(input);
            Assert.That(SpdxExpressionFormatter.Format(expression), Is.EqualTo(expected));
            Roundtrip(expression);
        }
        [Test]
        public void GeneratedRoundtripsAndIdempotence() {
            string[] atoms = ["MIT", "mit", "Unknown-1.0", "GPL-2.0+", "LicenseRef-X", "DocumentRef-D:LicenseRef-X", "MIT WITH LLVM-exception"];
            foreach(var left in atoms)
                foreach(var right in atoms)
                    foreach(string op in new[] { "AND", "OR" })
                        foreach(bool group in new[] { false, true }) {
                            string input = group ? $" ({left})\t{op}\r\n({right}) " : $" {left}\t{op}\r\n{right} ";
                            Roundtrip(GrammarTests.Parse(input));
                        }
        }
        [Test]
        public void FormattingCanRequireLargerInputBudget() {
            var expression = GrammarTests.Parse("(A)OR(B)", new() { MaxInputLength = 8 });
            string output = SpdxExpressionFormatter.Format(expression);
            Assert.That(output, Is.EqualTo("(A) OR (B)"));
            Assert.That(SpdxExpressionParser.Parse(output, new() { MaxInputLength = 8 }).Status, Is.EqualTo(SpdxParseStatus.LimitExceeded));
            Roundtrip(expression);
        }
        [Test]
        public void DeterministicMutationSet() {
            // Fixed LCG, seed 231, 600 single insert/delete/replace mutations. External watchdog: eng/verify.ps1.
            uint state = 231;
            string[] seeds = ["MIT", "(MIT OR Apache-2.0) AND GPL-2.0+ WITH LLVM-exception", "DocumentRef-D:LicenseRef-X OR Unknown-1.0", "((LicenseRef-X))"];
            char[] alphabet = ['A', ' ', '\t', '\r', '\n', '(', ')', '+', ':', '-', '_', '\0', '\u00a0', '\ud800'];
            var exercisedSeedOperations = new HashSet<(int Seed, int Operation)>();
            var exercisedCharacters = new HashSet<char>();
            int endInsertions = 0;
            for(int index = 0; index < 600; index++) {
                int seed = index % seeds.Length;
                string input = seeds[seed];
                int operation = Next(3);
                int position = Next(input.Length + (operation == 0 ? 1 : 0));
                char value = alphabet[Next(alphabet.Length)];
                exercisedSeedOperations.Add((seed, operation));
                if(operation != 1)
                    exercisedCharacters.Add(value);
                if(operation == 0 && position == input.Length)
                    endInsertions++;
                input = operation switch {
                    0 => input.Insert(position, value.ToString()),
                    1 => input.Remove(position, 1),
                    _ => input[..position] + value + input[(position + 1)..]
                };
                var result = SpdxExpressionParser.Parse(input);
                if(result.Status == SpdxParseStatus.Success) {
                    Roundtrip(result.Expression!);
                    var validation = SpdxExpressionValidator.Validate(result.Expression!, SpdxLicenseRegistry.Default, new() { MaxDiagnostics = 1 });
                    Assert.That(validation.IdentifierOccurrences.Select(x => x.Span.Start), Is.Ordered);
                    Assert.That(validation.References.Select(x => x.Span.Start), Is.Ordered);
                }
                else {
                    Assert.That(result.Expression, Is.Null);
                    Assert.That(result.Diagnostics, Has.Count.EqualTo(1));
                    var span = result.Diagnostics[0].Span;
                    Assert.That(span.Start, Is.GreaterThanOrEqualTo(0));
                    Assert.That(span.Start + (long)span.Length, Is.LessThanOrEqualTo(input.Length));
                }
            }
            Assert.That(exercisedSeedOperations.Count, Is.EqualTo(seeds.Length * 3), "Every seed must exercise insertion, deletion and replacement.");
            Assert.That(exercisedCharacters, Is.EquivalentTo(alphabet), "Include plus, NUL and surrogate mutations.");
            Assert.That(endInsertions, Is.Positive, "Exercise full-consumption failures at EOF.");
            int Next(int max) {
                state = unchecked(state * 1664525 + 1013904223);
                // The low bits cycle in step with the number of draws per case.
                return (int)((state >> 16) % (uint)max);
            }
        }
        static void Roundtrip(SpdxParsedExpression expression) {
            string formatted = SpdxExpressionFormatter.Format(expression);
            var reparsed = GrammarTests.Parse(formatted);
            Assert.That(GrammarTests.Describe(reparsed.Root), Is.EqualTo(GrammarTests.Describe(expression.Root)));
            Assert.That(SpdxExpressionFormatter.Format(reparsed), Is.EqualTo(formatted));
            CheckSpans(expression.Root, expression.SourceText);
        }
        static void CheckSpans(SpdxExpressionNode node, string source) {
            Assert.That(node.Span.Start, Is.GreaterThanOrEqualTo(0));
            Assert.That(node.Span.Length, Is.Positive);
            Assert.That(node.Span.Start + (long)node.Span.Length, Is.LessThanOrEqualTo(source.Length));
            switch(node) {
                case SpdxLicenseExpression license:
                    Assert.That(source.Substring(license.IdentifierSpan.Start, license.IdentifierSpan.Length), Is.EqualTo(license.Identifier));
                    break;
                case SpdxReferenceExpression reference:
                    Assert.That(source.Substring(reference.LicenseIdSpan.Start, reference.LicenseIdSpan.Length), Is.EqualTo(reference.LicenseId));
                    if(reference.DocumentIdSpan is { } doc)
                        Assert.That(source.Substring(doc.Start, doc.Length), Is.EqualTo(reference.DocumentId));
                    break;
                case SpdxWithExpression with:
                    CheckSpans(with.Operand, source);
                    Assert.That(source.Substring(with.ExceptionSpan.Start, with.ExceptionSpan.Length), Is.EqualTo(with.ExceptionIdentifier));
                    break;
                case SpdxGroupExpression group:
                    Assert.That(source[group.Span.Start], Is.EqualTo('('));
                    Assert.That(source[group.Span.Start + group.Span.Length - 1], Is.EqualTo(')'));
                    CheckSpans(group.Inner, source);
                    break;
                case SpdxAndExpression and:
                    foreach(var operand in and.Operands)
                        CheckSpans(operand, source);
                    break;
                case SpdxOrExpression or:
                    foreach(var operand in or.Operands)
                        CheckSpans(operand, source);
                    break;
            }
        }
    }
}
