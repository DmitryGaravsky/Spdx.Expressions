using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using Spdx.Expressions;

static void Check(bool condition, string message) {
    if(!condition)
        throw new InvalidOperationException(message);
}
static SpdxParsedExpression Parse(string text) {
    var result = SpdxExpressionParser.Parse(text);
    Check(result.Status == SpdxParseStatus.Success && result.Diagnostics.Count == 0, text);
    return result.Expression!;
}
static void Cancelled(Action operation) {
    try {
        operation();
    }
    catch(OperationCanceledException) {
        return;
    }
    throw new InvalidOperationException("Cancellation did not propagate.");
}

var registry = SpdxLicenseRegistry.Default;
Parallel.For(0, 32, _ => Check(ReferenceEquals(registry, SpdxLicenseRegistry.Default), "Shared snapshot"));
Check(registry.Licenses.Count == 740 && registry.Exceptions.Count == 86, "Entry counts");
Check(registry.Identity.LicenseListVersion == "3.29.0", "List version");
Check(registry.TryGetLicense("mit", out var mit) && mit.Identifier == "MIT", "Case-insensitive license lookup");
Check(registry.TryGetException("llvm-exception", out var llvm) && llvm.Identifier == "LLVM-exception", "Exception namespace");
Check(registry.TryGetLicense("GPL-2.0+", out var plus) && plus.IsDeprecated, "Literal deprecated catalogue ID");

var expression = Parse(" \t(MIT OR mit) AND GPL-2.0+ WITH LLVM-exception ");
var root = (SpdxAndExpression)expression.Root;
Check(root.Span.Start == 2 && root.Span.Length == 45, "Root source span");
Check(root.Operands.Count == 2 && root.Operands[0] is SpdxGroupExpression, "Explicit group");
var with = (SpdxWithExpression)root.Operands[1];
Check(with.ExceptionSpan.Start == 33 && with.ExceptionSpan.Length == 14, "Exception source span");
Check(((SpdxLicenseExpression)with.Operand).OrLater, "Plus retained");
Check(SpdxExpressionFormatter.Format(expression) == "(MIT OR mit) AND GPL-2.0+ WITH LLVM-exception", "Formatting");

var all = SpdxExpressionValidator.Validate(Parse("GPL-2.0 OR GPL-2.0 OR Unknown-1.0 OR DocumentRef-D:LicenseRef-X OR LicenseRef-X"), registry,
    new() { MaxDiagnostics = 1 });
Check(all.HasIdentifierErrors && all.HasUnresolvedReferences && all.DiagnosticsTruncated, "Independent late flags");
Check(all.IdentifierOccurrences.Count == 3 && all.References.Count == 2 && all.Diagnostics.Count == 1, "Full occurrences");
Check(all.Diagnostics[0].Severity == SpdxDiagnosticSeverity.Warning && all.IdentifierOccurrences[^1].LookupStatus == SpdxIdentifierLookupStatus.Unknown, "Late unknown after warning");
Check(all.References[0].DocumentId == "D" && all.References[0].LicenseId == "X", "Reference components");
var wrong = SpdxExpressionValidator.Validate(Parse("MIT WITH Apache-2.0"), registry);
Check(wrong.HasIdentifierErrors && wrong.IdentifierOccurrences[1].LookupStatus == SpdxIdentifierLookupStatus.WrongKind, "Wrong-kind exception");
Check(wrong.IdentifierOccurrences[1].CanonicalIdentifier is null && wrong.IdentifierOccurrences[1].IsDeprecated is null, "Wrong-kind metadata absent");
var reference = (SpdxReferenceExpression)Parse("DocumentRef-D:LicenseRef-X").Root;
Check(reference.DocumentIdSpan!.Value.Start == 12 && reference.LicenseIdSpan.Start == 25 && reference.Span.Length == 26, "Reference spans");
Check(!SpdxExpressionValidator.Validate(Parse("LicenseRef-X"), registry).HasIdentifierErrors, "Reference-only result");
Check(SpdxExpressionParser.Parse("NONE").Expression is null, "Sentinel rejected");
Check(SpdxExpressionParser.Parse("MIT AND").Status == SpdxParseStatus.InvalidSyntax, "Syntax failure");
// Independent precedence expectations from SPDX 2.3.1 Annex D.4.5:
// https://spdx.github.io/spdx-spec/v2.3.1/SPDX-license-expressions/#d45-order-of-precedence-and-parentheses
const string mixedInput = "MIT OR GPL-2.0+ WITH LLVM-exception AND Apache-2.0 OR BSD-3-Clause";
var mixed = Parse(mixedInput);
var choices = (SpdxOrExpression)mixed.Root;
Check(choices.Operands.Count == 3, "Three ordered choices");
Check(((SpdxLicenseExpression)choices.Operands[0]).Identifier == "MIT" && ((SpdxLicenseExpression)choices.Operands[2]).Identifier == "BSD-3-Clause", "Choice source order");
var requirements = (SpdxAndExpression)choices.Operands[1];
Check(requirements.Operands.Count == 2 && ((SpdxLicenseExpression)requirements.Operands[1]).Identifier == "Apache-2.0", "AND before OR");
var exception = (SpdxWithExpression)requirements.Operands[0];
var baseLicense = (SpdxLicenseExpression)exception.Operand;
Check(baseLicense.Identifier == "GPL-2.0" && baseLicense.OrLater && exception.ExceptionIdentifier == "LLVM-exception", "Plus before WITH before AND");
Check(SpdxExpressionFormatter.Format(mixed) == mixedInput, "Mixed precedence formatting");
foreach(var (input, code, start, length) in new[] {
    ("MIT wITh LLVM-exception", "SPDX_PARSE_EXPECTED_OPERATOR", 4, 4),
    ("DocumentRef-D: LicenseRef-X", "SPDX_PARSE_INVALID_REFERENCE", 0, 14),
    ("MIT AND \U0001F600-Unknown", "SPDX_PARSE_INVALID_CHARACTER", 8, 1)
}) {
    var rejected = SpdxExpressionParser.Parse(input);
    Check(rejected.Status == SpdxParseStatus.InvalidSyntax && rejected.Expression is null && rejected.Diagnostics.Count == 1, "Profile rejection without partial AST");
    var diagnostic = rejected.Diagnostics[0];
    Check(diagnostic.Code == code && diagnostic.Span.Start == start && diagnostic.Span.Length == length, "Independent diagnostic and UTF-16 span");
}
var limit = SpdxExpressionParser.Parse("MIT", new() { MaxInputLength = 2 });
Check(limit.Status == SpdxParseStatus.LimitExceeded && limit.Expression is null, "Budget failure");
Cancelled(() => SpdxExpressionParser.Parse("MIT", cancellationToken: new(true)));
Cancelled(() => SpdxExpressionValidator.Validate(expression, registry, cancellationToken: new(true)));
Cancelled(() => SpdxExpressionFormatter.Format(expression, new(true)));

var assembly = typeof(SpdxLicenseRegistry).Assembly;
Check(assembly.GetName().Name == "Spdx.Expressions", "Assembly identity");
Check(assembly.GetExportedTypes().All(t => t.Namespace == "Spdx.Expressions"), "Public namespaces");
Check(assembly.GetReferencedAssemblies().All(a => a.Name!.StartsWith("System.", StringComparison.Ordinal)), "Runtime dependencies");
foreach(var (name, hash) in new[] { ("licenses", registry.Identity.LicensesSha256), ("exceptions", registry.Identity.ExceptionsSha256) }) {
    using(var stream = assembly.GetManifestResourceStream($"Spdx.Expressions.Data.{name}.json")!) {
        Check(stream is not null, "Embedded source");
        Check(Convert.ToHexStringLower(SHA256.HashData(stream!)) == hash, "Embedded exact-byte identity");
    }
}
using(var package = ZipFile.OpenRead(args[0])) {
    using(var packedAssembly = package.GetEntry("lib/net10.0/Spdx.Expressions.dll")!.Open())
    using(var loadedAssembly = File.OpenRead(assembly.Location)) {
        Check(SHA256.HashData(loadedAssembly).SequenceEqual(SHA256.HashData(packedAssembly)), "Loaded assembly matches the exact package");
    }
    foreach(string name in new[] { "lib/net10.0/Spdx.Expressions.dll", "lib/net10.0/Spdx.Expressions.xml", "README.md", "LICENSE", "THIRD-PARTY-NOTICES.md", "data/manifest.json", "data/UPSTREAM-README.md", "data/UPSTREAM-XML-README.md" })
        Check(package.GetEntry(name) is not null, "Package entry: " + name);
    using(var nuspec = package.Entries.Single(e => e.FullName.EndsWith(".nuspec", StringComparison.Ordinal)).Open()) {
        var xml = XDocument.Load(nuspec);
        Check(!xml.Descendants().Any(x => x.Name.LocalName == "dependency"), "No runtime NuGet dependencies");
        Check(xml.Descendants().Single(x => x.Name.LocalName == "authors").Value == "Dmitry Garavsky", "Package author");
        Check(xml.Descendants().Single(x => x.Name.LocalName == "license").Value == "MIT", "Implementation license");
        const string repositoryUrl = "https://github.com/DmitryGaravsky/Spdx.Expressions";
        Check(xml.Descendants().Single(x => x.Name.LocalName == "projectUrl").Value == repositoryUrl, "Package project URL");
        var repository = xml.Descendants().Single(x => x.Name.LocalName == "repository");
        Check((string?)repository.Attribute("url") == repositoryUrl && (string?)repository.Attribute("type") == "git", "Package repository");
        var packageVersion = xml.Descendants().Single(x => x.Name.LocalName == "version").Value;
        var assemblyVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        Check(assemblyVersion.Split('+')[0] == packageVersion, "Built assembly and package versions agree");
        Check(xml.Descendants().Any(x => x.Name.LocalName == "group" && (string?)x.Attribute("targetFramework") == "net10.0"), "Target framework");
    }
    using(var manifest = package.GetEntry("data/manifest.json")!.Open())
    using(var json = JsonDocument.Parse(manifest)) {
        Check(json.RootElement.GetProperty("commit").GetString() == "31ba1a50e5397e00a304dbadc76531740e89ee48", "Upstream commit");
        foreach(var file in json.RootElement.GetProperty("files").EnumerateArray()) {
            var entry = package.GetEntry("data/" + file.GetProperty("file").GetString())!;
            Check(entry.Length == file.GetProperty("byteLength").GetInt64(), "Source byte length");
            using(var stream = entry.Open()) {
                Check(Convert.ToHexStringLower(SHA256.HashData(stream)) == file.GetProperty("sha256").GetString(), "Shipped data hash");
            }
        }
    }
}
Console.WriteLine(JsonSerializer.Serialize(new {
    status = "passed",
    os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
    runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    packageSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(args[0]))),
    listVersion = registry.Identity.LicenseListVersion,
    licensesSha256 = registry.Identity.LicensesSha256,
    exceptionsSha256 = registry.Identity.ExceptionsSha256
}));
