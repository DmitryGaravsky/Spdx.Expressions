using System;

namespace Spdx.Expressions.Examples {
    static class Program {
        const string UnknownScenario = "Choose quick-start, syntax-error, unknown-id, reference, deprecated, source-spans, formatting, canonical-casing, diagnostic-budget, exception-kind, parse-limit, cancellation, load-memory, load-stream, enumerate-nodes, or inspect-structure.";
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
                case "canonical-casing":
                    CanonicalCasing();
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
                case "load-memory":
                    LoadMemory();
                    break;
                case "load-stream":
                    LoadStream();
                    break;
                case "enumerate-nodes":
                    EnumerateNodes();
                    break;
                case "inspect-structure":
                    InspectStructure();
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
        static void CanonicalCasing() {
            #region canonical-casing
            var parsed = SpdxExpressionParser.Parse(" ( mit+ WITH llvm-exception ) OR LicenseRef-MiT ");
            if(parsed.Expression is not { } expression)
                return;
            var registry = SpdxLicenseRegistry.Default;
            Console.WriteLine(SpdxExpressionFormatter.Format(expression));
            Console.WriteLine(SpdxExpressionFormatter.FormatCanonicalCasing(expression, registry));
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
        static void LoadMemory() {
            #region load-memory
            var licenses = """{"licenseListVersion":"selected-1","licenses":[{"licenseId":"S1-Retired","isDeprecatedLicenseId":true},{"licenseId":"S1-Custom","isDeprecatedLicenseId":false}]}"""u8.ToArray();
            var exceptions = """{"licenseListVersion":"selected-1","exceptions":[{"licenseExceptionId":"S1-Flip","isDeprecatedLicenseId":true},{"licenseExceptionId":"S1-Exception","isDeprecatedLicenseId":false}]}"""u8.ToArray();
            var loaded = SpdxLicenseRegistry.Load(licenses, exceptions);
            if(loaded.Registry is not { } registry) {
                var diagnostic = loaded.Diagnostics[0];
                Console.WriteLine($"{loaded.Status}: {diagnostic.Code} in {diagnostic.Input} at byte {diagnostic.ByteOffset}");
                return;
            }
            var parsed = SpdxExpressionParser.Parse("s1-custom WITH S1-Exception OR Apache-2.0");
            var expression = parsed.Expression ?? throw new InvalidOperationException("Invalid expression.");
            var validated = SpdxExpressionValidator.Validate(expression, registry);
            Console.WriteLine($"{validated.RegistryIdentity.LicenseListVersion} {validated.HasIdentifierErrors}");
            foreach(var occurrence in validated.IdentifierOccurrences)
                Console.WriteLine($"{occurrence.Role} {occurrence.Identifier} {occurrence.LookupStatus}");
            #endregion
        }
        static void LoadStream() {
            #region load-stream
            var licenses = """{"licenseListVersion":"local","licenses":[{"licenseId":"MIT","isDeprecatedLicenseId":false}]}"""u8.ToArray();
            var exceptions = """{"licenseListVersion":"local","exceptions":[{"licenseExceptionId":"LLVM-exception","isDeprecatedLicenseId":false}]}"""u8.ToArray();
            foreach(var (exceptionsBytes, options) in new (byte[], SpdxRegistryLoadOptions?)[] {
                (exceptions, null),
                ("{}"u8.ToArray(), null),
                (exceptions, new() { MaxInputBytes = 8 })
            }) {
                using(var licenseStream = new System.IO.MemoryStream(licenses))
                using(var exceptionStream = new System.IO.MemoryStream(exceptionsBytes)) {
                    var loaded = SpdxLicenseRegistry.Load(licenseStream, exceptionStream, options);
                    if(loaded.Registry is { } registry) {
                        Console.WriteLine($"{loaded.Status}: {registry.Identity.LicenseListVersion}, {registry.Licenses.Count} license, {registry.Exceptions.Count} exception");
                    }
                    else {
                        var diagnostic = loaded.Diagnostics[0];
                        Console.WriteLine($"{loaded.Status}: {diagnostic.Code} in {diagnostic.Input} at byte {diagnostic.ByteOffset}; {diagnostic.LimitName ?? diagnostic.PropertyName}");
                    }
                }
            }
            #endregion
        }
        static void EnumerateNodes() {
            #region enumerate-nodes
            var parsed = SpdxExpressionParser.Parse(" mit+ WITH eX OR LicenseRef-X OR mit+ ");
            if(parsed.Expression is not { } expression)
                return;
            foreach(var node in expression.Root.EnumerateNodes()) {
                if(node is SpdxLicenseExpression or SpdxReferenceExpression) {
                    var span = node.Span;
                    string source = expression.SourceText.Substring(span.Start, span.Length);
                    Console.WriteLine($"{node.Kind} {source} @{span.Start}");
                }
            }
            #endregion
        }
        static void InspectStructure() {
            #region inspect-structure
            var parsed = SpdxExpressionParser.Parse("(MIT WITH eX) AND (LicenseRef-X OR ISC)");
            if(parsed.Expression is not { } expression)
                return;
            var inspection = new StructureInspection();
            inspection.Walk(expression.Root);
            #endregion
        }
        #region helper inspect-structure
        sealed class StructureInspection : SpdxExpressionWalker {
            protected override void VisitGroup(SpdxGroupExpression node) {
                Console.WriteLine($"Group {node.Span.Start}, length {node.Span.Length}");
            }
            protected override void VisitWith(SpdxWithExpression node) {
                Console.WriteLine($"Exception {node.ExceptionIdentifier} @{node.ExceptionSpan.Start}");
            }
        }
        #endregion
    }
}
