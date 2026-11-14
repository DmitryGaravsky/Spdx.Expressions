using System;
using Spdx.Expressions;

namespace Spdx.Expressions.Examples {
    static class Program {
        const string UnknownScenario = "Choose quick-start, syntax-error, unknown-id, reference, deprecated, source-spans, formatting, diagnostic-budget, exception-kind, parse-limit, or cancellation.";
        static int Main(string[] args) {
            string scenario = args.Length == 0 ? "quick-start" : args[0];
            if(args.Length > 1) {
                Console.Error.WriteLine(UnknownScenario);
                return 1;
            }
            switch(scenario) {
                case "quick-start":
                    QuickStart();
                    break;
                case "syntax-error":
                    SyntaxError();
                    break;
                case "unknown-id":
                    UnknownIdentifier();
                    break;
                case "reference":
                    Reference();
                    break;
                case "deprecated":
                    Deprecated();
                    break;
                case "source-spans":
                    SourceSpans();
                    break;
                case "formatting":
                    Formatting();
                    break;
                case "diagnostic-budget":
                    DiagnosticBudget();
                    break;
                case "exception-kind":
                    ExceptionKind();
                    break;
                case "parse-limit":
                    ParseLimit();
                    break;
                case "cancellation":
                    Cancellation();
                    break;
                default:
                    Console.Error.WriteLine(UnknownScenario);
                    return 1;
            }
            return 0;
        }
        static void QuickStart() {
            #region quick-start
            var parsed = SpdxExpressionParser.Parse("MIT OR Apache-2.0");
            if(parsed.Status != SpdxParseStatus.Success) {
                var diagnostic = parsed.Diagnostics[0];
                Console.WriteLine($"Parse: {parsed.Status}; {diagnostic.Code} at {diagnostic.Span.Start}");
                return;
            }
            var expression = parsed.Expression!;
            var validation = SpdxExpressionValidator.Validate(expression, SpdxLicenseRegistry.Default);
            Console.WriteLine($"Formatted: {SpdxExpressionFormatter.Format(expression)}");
            Console.WriteLine($"Identifier errors: {validation.HasIdentifierErrors}");
            Console.WriteLine($"Unresolved references: {validation.HasUnresolvedReferences}");
            Console.WriteLine($"Diagnostics truncated: {validation.DiagnosticsTruncated}");
            foreach(var diagnostic in validation.Diagnostics)
                Console.WriteLine($"{diagnostic.Severity}: {diagnostic.Code} at {diagnostic.Span.Start}");
            #endregion
        }
        static void SyntaxError() {
            #region syntax-error
            var parsed = SpdxExpressionParser.Parse("MIT AND");
            Console.WriteLine($"Parse: {parsed.Status}");
            foreach(var diagnostic in parsed.Diagnostics)
                Console.WriteLine($"{diagnostic.Code}: start={diagnostic.Span.Start}, length={diagnostic.Span.Length}");
            #endregion
        }
        static void UnknownIdentifier() {
            #region unknown-id
            var parsed = SpdxExpressionParser.Parse("Unknown-1.0");
            Console.WriteLine($"Parse: {parsed.Status}");
            if(parsed.Expression is null)
                return;
            var validation = SpdxExpressionValidator.Validate(parsed.Expression, SpdxLicenseRegistry.Default);
            Console.WriteLine($"Identifier errors: {validation.HasIdentifierErrors}");
            foreach(var diagnostic in validation.Diagnostics)
                Console.WriteLine($"{diagnostic.Severity}: {diagnostic.Code}");
            #endregion
        }
        static void Reference() {
            #region reference
            var parsed = SpdxExpressionParser.Parse("LicenseRef-X");
            if(parsed.Expression is null)
                return;
            var validation = SpdxExpressionValidator.Validate(parsed.Expression, SpdxLicenseRegistry.Default);
            Console.WriteLine($"Identifier errors: {validation.HasIdentifierErrors}");
            Console.WriteLine($"Unresolved references: {validation.HasUnresolvedReferences}");
            foreach(var reference in validation.References)
                Console.WriteLine($"License component: {reference.LicenseId}");
            #endregion
        }
        static void Deprecated() {
            #region deprecated
            var parsed = SpdxExpressionParser.Parse("GPL-2.0+");
            if(parsed.Expression is null)
                return;
            var validation = SpdxExpressionValidator.Validate(parsed.Expression, SpdxLicenseRegistry.Default);
            Console.WriteLine($"Identifier errors: {validation.HasIdentifierErrors}");
            foreach(var diagnostic in validation.Diagnostics)
                Console.WriteLine($"{diagnostic.Severity}: {diagnostic.Code}");
            Console.WriteLine($"Formatted: {SpdxExpressionFormatter.Format(parsed.Expression)}");
            #endregion
        }
        static void SourceSpans() {
            #region source-spans
            var parsed = SpdxExpressionParser.Parse("  mit OR Apache-2.0 ");
            if(parsed.Expression is null)
                return;
            var expression = parsed.Expression;
            var validation = SpdxExpressionValidator.Validate(expression, SpdxLicenseRegistry.Default);
            foreach(var occurrence in validation.IdentifierOccurrences) {
                var span = occurrence.Span;
                string source = expression.SourceText.Substring(span.Start, span.Length);
                Console.WriteLine($"{span.Start}:{span.Length} '{source}' -> {occurrence.CanonicalIdentifier}");
            }
            #endregion
        }
        static void Formatting() {
            #region formatting
            var parsed = SpdxExpressionParser.Parse(" ( mit  OR Apache-2.0 ) AND MIT ");
            if(parsed.Expression is null)
                return;
            Console.WriteLine($"Original: '{parsed.Expression.SourceText}'");
            Console.WriteLine($"Formatted: '{SpdxExpressionFormatter.Format(parsed.Expression)}'");
            #endregion
        }
        static void ExceptionKind() {
            #region exception-kind
            foreach(string input in new[] { "MIT WITH LLVM-exception", "MIT WITH Apache-2.0" }) {
                var parsed = SpdxExpressionParser.Parse(input);
                if(parsed.Expression is null)
                    return;
                var validation = SpdxExpressionValidator.Validate(parsed.Expression, SpdxLicenseRegistry.Default);
                Console.WriteLine($"{input}: identifier errors={validation.HasIdentifierErrors}");
                foreach(var occurrence in validation.IdentifierOccurrences)
                    Console.WriteLine($"{occurrence.Role}: {occurrence.Identifier} -> {occurrence.LookupStatus}");
                foreach(var diagnostic in validation.Diagnostics)
                    Console.WriteLine(diagnostic.Code);
            }
            #endregion
        }
        static void ParseLimit() {
            #region parse-limit
            var options = new SpdxParseOptions { MaxInputLength = 2 };
            var parsed = SpdxExpressionParser.Parse("MIT", options);
            Console.WriteLine($"Parse: {parsed.Status}");
            Console.WriteLine($"Expression available: {parsed.Expression is not null}");
            foreach(var diagnostic in parsed.Diagnostics)
                Console.WriteLine(diagnostic.Code);
            #endregion
        }
        static void Cancellation() {
            #region cancellation
            try {
                var parsed = SpdxExpressionParser.Parse("MIT", cancellationToken: new(true));
                Console.WriteLine($"Parse: {parsed.Status}");
            }
            catch(OperationCanceledException) {
                Console.WriteLine("Cancelled: no parse result");
            }
            #endregion
        }
        static void DiagnosticBudget() {
            #region diagnostic-budget
            var parsed = SpdxExpressionParser.Parse("GPL-2.0 OR GPL-2.0 OR Unknown-1.0 OR LicenseRef-X");
            if(parsed.Expression is null)
                return;
            var options = new SpdxValidationOptions { MaxDiagnostics = 1 };
            var validation = SpdxExpressionValidator.Validate(parsed.Expression, SpdxLicenseRegistry.Default, options);
            Console.WriteLine($"Identifier errors: {validation.HasIdentifierErrors}");
            Console.WriteLine($"Unresolved references: {validation.HasUnresolvedReferences}");
            Console.WriteLine($"Diagnostics truncated: {validation.DiagnosticsTruncated}");
            Console.WriteLine($"Identifiers: {validation.IdentifierOccurrences.Count}; references: {validation.References.Count}");
            foreach(var diagnostic in validation.Diagnostics)
                Console.WriteLine($"{diagnostic.Severity}: {diagnostic.Code}");
            #endregion
        }
    }
}
