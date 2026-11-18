using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
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
static void Cancelled(Action operation, CancellationToken? token = null) {
    try {
        operation();
    }
    catch(OperationCanceledException error) {
        if(token is { } expected)
            Check(error.CancellationToken == expected, "Cancellation token identity");
        return;
    }
    throw new InvalidOperationException("Cancellation did not propagate.");
}

static void CheckTraversal() {
    var expression = Parse(" (mit+ WITH eX OR LicenseRef-X) ");
    var root = expression.Root;
    var visits = root.EnumerateNodes().Select(node => (node.Kind, node.Span.Start, node.Span.Length));
    Check(visits.SequenceEqual(new[] {
        (SpdxExpressionKind.Group, 1, 30), (SpdxExpressionKind.Or, 2, 28),
        (SpdxExpressionKind.With, 2, 12), (SpdxExpressionKind.License, 2, 4),
        (SpdxExpressionKind.Reference, 18, 12)
    }), "Root-inclusive traversal, original order and spans");
    var token = new CancellationToken(true);
    using(var enumerator = root.EnumerateNodes(token).GetEnumerator()) {
        Cancelled(() => {
            enumerator.MoveNext();
        }, token);
    }
    using(var cancellation = new CancellationTokenSource()) {
        var walker = new CancellationWalker(cancellation);
        Cancelled(() => walker.Walk(root, token), token);
        Check(walker.Count == 0, "Pre-canceled walk has no callbacks");
        Cancelled(() => walker.Walk(root, cancellation.Token), cancellation.Token);
        Check(walker.Count == 4, "Walk stopped at the first license before visiting the reference");
    }
}

static SpdxLicenseRegistry CheckSelectedLoad(SpdxRegistryLoadResult loaded) {
    Check(loaded.Status == SpdxRegistryLoadStatus.Success && loaded.Diagnostics.Count == 0, "Selected pair loads");
    var selected = loaded.Registry ?? throw new InvalidOperationException("Selected registry missing.");
    Check(selected.Identity.LicenseListVersion == "selected-1", "Selected list version");
    Check(selected.Identity.LicensesSha256 == "97ea673646c087bcedc6e308700d14607c4c3a6380bb639692b67f62773dd3f1", "Selected licenses hash");
    Check(selected.Identity.ExceptionsSha256 == "f3b52b394fae004d7c54802827d68f7735c4600977c273c4e3325509b24768d9", "Selected exceptions hash");
    Check(selected.Licenses.Select(entry => (entry.Identifier, entry.IsDeprecated)).SequenceEqual(new[] {
        ("S1-Custom", false), ("S1-Retired", true)
    }), "Selected license entries");
    Check(selected.Exceptions.Select(entry => (entry.Identifier, entry.IsDeprecated)).SequenceEqual(new[] {
        ("S1-Exception", false), ("S1-Flip", true)
    }), "Selected exception entries");
    return selected;
}
static void CheckSelectedValidation(SpdxValidationResult validation, SpdxRegistryIdentity identity, bool isSelected) {
    Check(ReferenceEquals(validation.RegistryIdentity, identity), "Identity of the supplied catalogue");
    Check(validation.HasIdentifierErrors && !validation.HasUnresolvedReferences && !validation.DiagnosticsTruncated, "Selected expression flags");
    Check(validation.References.Count == 0, "Selected expression has no references");
    var occurrences = validation.IdentifierOccurrences;
    Check(occurrences.Count == 3, "Selected expression occurrence count");
    var license = SpdxIdentifierRole.License;
    var exception = SpdxIdentifierRole.Exception;
    var known = SpdxIdentifierLookupStatus.Known;
    var unknown = SpdxIdentifierLookupStatus.Unknown;
    var expected = isSelected
        ? new (string, int, int, SpdxIdentifierRole, SpdxIdentifierLookupStatus, string?, bool?)[] {
            ("s1-custom", 0, 9, license, known, "S1-Custom", false),
            ("S1-Exception", 15, 12, exception, known, "S1-Exception", false),
            ("Apache-2.0", 31, 10, license, unknown, null, null)
        }
        : new (string, int, int, SpdxIdentifierRole, SpdxIdentifierLookupStatus, string?, bool?)[] {
            ("s1-custom", 0, 9, license, unknown, null, null),
            ("S1-Exception", 15, 12, exception, unknown, null, null),
            ("Apache-2.0", 31, 10, license, known, "Apache-2.0", false)
        };
    Check(occurrences.Select(item => (item.Identifier, item.Span.Start, item.Span.Length, item.Role,
        item.LookupStatus, item.CanonicalIdentifier, item.IsDeprecated)).SequenceEqual(expected), "Selected expression occurrences");
    var diagnostics = isSelected
        ? new[] { ("SPDX_ID_UNKNOWN_LICENSE", 31, 10) }
        : new[] { ("SPDX_ID_UNKNOWN_LICENSE", 0, 9), ("SPDX_ID_UNKNOWN_EXCEPTION", 15, 12) };
    Check(validation.Diagnostics.All(item => item.Severity == SpdxDiagnosticSeverity.Error) &&
        validation.Diagnostics.Select(item => (item.Code, item.Span.Start, item.Span.Length)).SequenceEqual(diagnostics),
        "Selected expression diagnostics");
}

static void CheckExternalCanonicalCasing(string licensesPath, string exceptionsPath) {
    var expression = Parse(" mit+ WITH llvm-exception OR apache-2.0 OR LicenseRef-miT ");
    var memory = SpdxLicenseRegistry.Load(File.ReadAllBytes(licensesPath), File.ReadAllBytes(exceptionsPath));
    using(var licenses = File.OpenRead(licensesPath))
    using(var exceptions = File.OpenRead(exceptionsPath)) {
        var streams = SpdxLicenseRegistry.Load(licenses, exceptions);
        foreach(var loaded in new[] { memory, streams }) {
            Check(loaded.Status == SpdxRegistryLoadStatus.Success && loaded.Diagnostics.Count == 0, "Canonical fixture loads");
            var selected = loaded.Registry ?? throw new InvalidOperationException("Canonical fixture registry missing.");
            Check(selected.Identity.LicenseListVersion == "case-fixture" &&
                selected.Identity.LicensesSha256 == "c297ffb202abc89f304c437e827f02ecf18ef1a0578fe8ae2dc7f322367fa2c2" &&
                selected.Identity.ExceptionsSha256 == "ea53d83abd1a45bded9e8171c76e8f839602b1f2caf719b780938a54df8f7543",
                "Exact canonical fixture identity");
            Check(SpdxExpressionFormatter.FormatCanonicalCasing(expression, selected) ==
                "mIt+ WITH lLvM-Exception OR apache-2.0 OR LicenseRef-miT", "External canonical casing with no embedded fallback");
        }
    }
    Check(SpdxExpressionFormatter.Format(expression, default) ==
        "mit+ WITH llvm-exception OR apache-2.0 OR LicenseRef-miT", "Existing default literal binding");
}

CheckTraversal();
CheckExternalCanonicalCasing(args[3], args[4]);
var registry = SpdxLicenseRegistry.Default;
Parallel.For(0, 32, _ => Check(ReferenceEquals(registry, SpdxLicenseRegistry.Default), "Shared snapshot"));
Check(registry.Licenses.Count == 740 && registry.Exceptions.Count == 86, "Entry counts");
Check(registry.Identity.LicenseListVersion == "3.29.0", "List version");
Check(registry.TryGetLicense("mit", out var mit) && mit.Identifier == "MIT", "Case-insensitive license lookup");
Check(registry.TryGetException("llvm-exception", out var llvm) && llvm.Identifier == "LLVM-exception", "Exception namespace");
Check(registry.TryGetLicense("GPL-2.0+", out var plus) && plus.IsDeprecated, "Literal deprecated catalogue ID");

SpdxRegistryLoadResult streamLoad;
using(var licensesFile = File.OpenRead(args[1]))
using(var exceptionsFile = File.OpenRead(args[2])) {
    streamLoad = SpdxLicenseRegistry.Load(licensesFile, exceptionsFile);
    Check(licensesFile.Position == licensesFile.Length && exceptionsFile.Position == exceptionsFile.Length, "Selected files remain open at EOF");
}
var streamRegistry = CheckSelectedLoad(streamLoad);
var memoryRegistry = CheckSelectedLoad(SpdxLicenseRegistry.Load(File.ReadAllBytes(args[1]), File.ReadAllBytes(args[2])));
Check(!ReferenceEquals(streamRegistry, memoryRegistry) && !ReferenceEquals(streamRegistry, registry) && !ReferenceEquals(memoryRegistry, registry),
    "Independent selected registries");
const string selectedSource = "s1-custom WITH S1-Exception OR Apache-2.0";
var selectedExpression = Parse(selectedSource);
Check(SpdxExpressionFormatter.Format(selectedExpression) == selectedSource, "Selected expression formatting");
CheckSelectedValidation(SpdxExpressionValidator.Validate(selectedExpression, streamRegistry), streamRegistry.Identity, isSelected: true);
CheckSelectedValidation(SpdxExpressionValidator.Validate(selectedExpression, memoryRegistry), memoryRegistry.Identity, isSelected: true);
CheckSelectedValidation(SpdxExpressionValidator.Validate(selectedExpression, registry), registry.Identity, isSelected: false);
Check(ReferenceEquals(registry, SpdxLicenseRegistry.Default), "Default survives selected loads");

var expression = Parse(" \t(MIT OR mit) AND GPL-2.0+ WITH LLVM-exception ");
var root = (SpdxAndExpression)expression.Root;
Check(root.Span.Start == 2 && root.Span.Length == 45, "Root source span");
Check(root.Operands.Count == 2 && root.Operands[0] is SpdxGroupExpression, "Explicit group");
var with = (SpdxWithExpression)root.Operands[1];
Check(with.ExceptionSpan.Start == 33 && with.ExceptionSpan.Length == 14, "Exception source span");
Check(((SpdxLicenseExpression)with.Operand).OrLater, "Plus retained");
Check(SpdxExpressionFormatter.Format(expression) == "(MIT OR mit) AND GPL-2.0+ WITH LLVM-exception", "Formatting");
Check(SpdxExpressionFormatter.FormatCanonicalCasing(expression, registry) == "(MIT OR MIT) AND GPL-2.0+ WITH LLVM-exception",
    "Explicit embedded canonical casing");

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
var canonicalCancellation = new CancellationToken(true);
Cancelled(() => SpdxExpressionFormatter.FormatCanonicalCasing(expression, registry, canonicalCancellation), canonicalCancellation);

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
    loadedDllSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assembly.Location))),
    traversal = "passed",
    selected = memoryRegistry.Identity,
    listVersion = registry.Identity.LicenseListVersion,
    licensesSha256 = registry.Identity.LicensesSha256,
    exceptionsSha256 = registry.Identity.ExceptionsSha256
}));

sealed class CancellationWalker : SpdxExpressionWalker {
    readonly CancellationTokenSource cancellation;
    internal CancellationWalker(CancellationTokenSource cancellation) {
        this.cancellation = cancellation;
    }
    internal int Count { get; private set; }
    protected override void VisitNode(SpdxExpressionNode node) {
        Count++;
        if(node is SpdxLicenseExpression)
            cancellation.Cancel();
    }
}
