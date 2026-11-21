using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Spdx.Expressions.Tests {
    public class RegistryLoadTests {
        static readonly (string Name, int Default)[] OptionDefaults = {
            ("MaxInputBytes", 16_777_216), ("MaxTotalInputBytes", 33_554_432), ("MaxJsonDepth", 16), ("MaxLicenses", 10_000),
            ("MaxExceptions", 2_000), ("MaxIdentifierLength", 256), ("MaxStringLength", 16_384), ("MaxTotalStringLength", 8_388_608),
            ("MaxObjectProperties", 64), ("MaxArrayLength", 10_000), ("MaxTokens", 250_000), ("MaxTokenBytes", 131_072)
        };
        [TestCaseSource(typeof(RegistryLoadVectors), nameof(RegistryLoadVectors.MemoryCases))]
        public void MemoryVector(JsonElement vector) {
            var (licenses, exceptions, options) = RegistryLoadVectors.Inputs(vector);
            var result = SpdxLicenseRegistry.Load(licenses, exceptions, options);
            RegistryLoadVectors.AssertOutcome(result, vector.GetProperty("expected"));
        }
        [Test]
        public void PublicEnumsOptionsAndResultOwnership() {
            Assert.That(Enum.GetValues<SpdxRegistryLoadStatus>().Select(value => (value.ToString(), (int)value)),
                Is.EqualTo(new[] { ("Success", 0), ("InvalidData", 1), ("LimitExceeded", 2) }));
            Assert.That(Enum.GetValues<SpdxRegistryInput>().Select(value => (value.ToString(), (int)value)),
                Is.EqualTo(new[] { ("Licenses", 0), ("Exceptions", 1) }));
            var options = new SpdxRegistryLoadOptions();
            var properties = typeof(SpdxRegistryLoadOptions).GetProperties();
            Assert.That(properties.Select(property => property.Name), Is.EquivalentTo(OptionDefaults.Select(option => option.Name)));
            foreach(var (name, defaultValue) in OptionDefaults) {
                var property = typeof(SpdxRegistryLoadOptions).GetProperty(name)!;
                Assert.That(property.PropertyType, Is.EqualTo(typeof(int)));
                Assert.That(property.GetValue(options), Is.EqualTo(defaultValue), name);
                Assert.That(property.SetMethod!.ReturnParameter.GetRequiredCustomModifiers(), Does.Contain(typeof(IsExternalInit)), name);
            }
            var (licenses, exceptions, _) = RegistryLoadVectors.Inputs(RegistryLoadVectors.Find("MINIMAL"));
            var loaded = SpdxLicenseRegistry.Load(licensesUtf8: licenses, exceptionsUtf8: exceptions, cancellationToken: default);
            var registry = RegistryLoadVectors.AssertSuccess(loaded, "MINIMAL");
            var failed = SpdxLicenseRegistry.Load("{}"u8.ToArray(), exceptions);
            foreach(object list in new object[] { loaded.Diagnostics, failed.Diagnostics, registry.Licenses, registry.Exceptions })
                RegistryTests.AssertReadOnly((IList)list);
            var licenseError = Assert.Throws<ArgumentNullException>(() => registry.TryGetLicense(null!, out _));
            var exceptionError = Assert.Throws<ArgumentNullException>(() => registry.TryGetException(null!, out _));
            Assert.That(licenseError!.ParamName, Is.EqualTo("identifier"));
            Assert.That(exceptionError!.ParamName, Is.EqualTo("identifier"));
        }
        static IEnumerable<TestCaseData> InvalidOptions() {
            foreach(var (property, _) in OptionDefaults) {
                yield return new TestCaseData(property, 0);
                yield return new TestCaseData(property, -1);
            }
            yield return new TestCaseData("MaxJsonDepth", 65);
        }
        [TestCaseSource(nameof(InvalidOptions))]
        public void InvalidOptionPrecedesCancellation(string property, int value) {
            var settings = new Dictionary<string, int> { [property] = value };
            var options = JsonSerializer.Deserialize<SpdxRegistryLoadOptions>(JsonSerializer.Serialize(settings));
            var error = Assert.Throws<ArgumentOutOfRangeException>(() => SpdxLicenseRegistry.Load(default(ReadOnlyMemory<byte>), default, options, new(true)));
            Assert.That(error!.ParamName, Is.EqualTo(property));
            using(var licenses = new MemoryStream())
            using(var exceptions = new MemoryStream()) {
                var streamError = Assert.Throws<ArgumentOutOfRangeException>(() => SpdxLicenseRegistry.Load(licenses, exceptions, options, new(true)));
                Assert.That(streamError!.ParamName, Is.EqualTo(property));
                Assert.That(licenses.CanRead && exceptions.CanRead, Is.True);
            }
        }
        [Test]
        public void InvalidOptionsFollowDeclaredOrder() {
            for(int first = 0; first < OptionDefaults.Length - 1; first++) {
                var settings = new Dictionary<string, int> { [OptionDefaults[first + 1].Name] = -1, [OptionDefaults[first].Name] = 0 };
                var options = JsonSerializer.Deserialize<SpdxRegistryLoadOptions>(JsonSerializer.Serialize(settings));
                var error = Assert.Throws<ArgumentOutOfRangeException>(() => SpdxLicenseRegistry.Load(default(ReadOnlyMemory<byte>), default, options, new(true)));
                Assert.That(error!.ParamName, Is.EqualTo(OptionDefaults[first].Name));
            }
        }
        [Test]
        public void DefaultMemoryAndNullArrayAreEmptyDocuments() {
            var (_, exceptions, _) = RegistryLoadVectors.Inputs(RegistryLoadVectors.Find("MINIMAL"));
            byte[]? absent = null;
            foreach(ReadOnlyMemory<byte> empty in new ReadOnlyMemory<byte>[] { default, absent }) {
                var result = SpdxLicenseRegistry.Load(empty, exceptions);
                RegistryLoadVectors.AssertOutcome(result, RegistryLoadVectors.Find("JSON-EMPTY").GetProperty("expected"));
            }
        }
        [TestCase(false)]
        [TestCase(true)]
        public void EmbeddedBytesProduceIndependentRegistryWithEverySourceEntry(bool useStreams) {
            string data = Path.Combine(TestContext.CurrentContext.TestDirectory, "Data");
            byte[] licenses = File.ReadAllBytes(Path.Combine(data, "licenses.json"));
            byte[] exceptions = File.ReadAllBytes(Path.Combine(data, "exceptions.json"));
            var loaded = RegistryLoadVectors.LoadPair(licenses, exceptions, options: null, useStreams);
            Assert.That(loaded.Status, Is.EqualTo(SpdxRegistryLoadStatus.Success), loaded.Diagnostics.FirstOrDefault()?.Message);
            var registry = loaded.Registry!;
            var embedded = SpdxLicenseRegistry.Default;
            Assert.That(registry, Is.Not.SameAs(embedded));
            Assert.That(RegistryLoadVectors.LoadPair(licenses, exceptions, options: null, useStreams).Registry, Is.Not.SameAs(registry));
            Assert.That((registry.Identity.LicenseListVersion, registry.Identity.LicensesSha256, registry.Identity.ExceptionsSha256),
                Is.EqualTo(("3.29.0", "47d1cc681abe31166b342b6cc4aab13a6ba8ea5c48794697fe3bf8b1dbaf509a",
                    "59ee7c150de521d635cbf9367b8cf543b8da6781ea3ac1c073d305524f84ada2")));
            Assert.That(loaded.Diagnostics, Is.Empty);
            var expectedLicenses = ReadEntries(licenses, "licenses", "licenseId", 740);
            var expectedExceptions = ReadEntries(exceptions, "exceptions", "licenseExceptionId", 86);
            foreach(var candidate in new[] { registry, embedded }) {
                Assert.That(candidate.Licenses.Select(row => (row.Identifier, row.IsDeprecated)), Is.EqualTo(expectedLicenses));
                Assert.That(candidate.Exceptions.Select(row => (row.Identifier, row.IsDeprecated)), Is.EqualTo(expectedExceptions));
                foreach(var entry in expectedLicenses) {
                    foreach(string spelling in Spellings(entry.Identifier)) {
                        Assert.That(candidate.TryGetLicense(spelling, out var match), Is.True, spelling);
                        Assert.That((match!.Identifier, match.IsDeprecated), Is.EqualTo(entry));
                        Assert.That(candidate.TryGetException(spelling, out _), Is.False);
                    }
                }
                foreach(var entry in expectedExceptions) {
                    foreach(string spelling in Spellings(entry.Identifier)) {
                        Assert.That(candidate.TryGetException(spelling, out var match), Is.True, spelling);
                        Assert.That((match!.Identifier, match.IsDeprecated), Is.EqualTo(entry));
                        Assert.That(candidate.TryGetLicense(spelling, out _), Is.False);
                    }
                }
                foreach(string plus in new[] { "GPL-1.0+", "GPL-2.0+", "GPL-3.0+", "LGPL-2.0+", "LGPL-2.1+", "LGPL-3.0+" }) {
                    Assert.That(candidate.TryGetLicense(plus, out var legacy), Is.True);
                    Assert.That(candidate.TryGetLicense(plus[..^1], out var baseEntry), Is.True);
                    Assert.That(legacy!.Identifier, Is.EqualTo(plus));
                    Assert.That(legacy.IsDeprecated && baseEntry!.IsDeprecated, Is.True);
                }
                foreach(string spelling in new[] { " MIT", "MIT " }) {
                    Assert.That(candidate.TryGetLicense(spelling, out var absent), Is.False);
                    Assert.That(absent, Is.Null);
                }
            }
            Assert.That(SpdxLicenseRegistry.Load("["u8.ToArray(), exceptions).Status, Is.EqualTo(SpdxRegistryLoadStatus.InvalidData));
            Assert.That(SpdxLicenseRegistry.Default, Is.SameAs(embedded));
        }
        [TestCase(false)]
        [TestCase(true)]
        public void OptionalDisplayFieldsPreserveAcceptedExternalEntries(bool useStreams) {
            byte[] licenses = """
                {"licenseListVersion":"test","licenses":[
                  {"licenseId":"MIT","isDeprecatedLicenseId":false,"name":{"name":"nested"},"reference":42,"seeAlso":["first",{}, "last"]},
                  {"licenseId":"Custom","isDeprecatedLicenseId":true,"seeAlso":[" b ",""," b "],"ref\u0065rence":"local#1","name":" A\u00e9 "}]}
                """u8.ToArray();
            byte[] exceptions = """
                {"licenseListVersion":"test","exceptions":[
                  {"seeAlso":{"nested":["x"]},"licenseExceptionId":"Empty","isDeprecatedLicenseId":false},
                  {"name":"","reference":" x ","seeAlso":["z","a"],"licenseExceptionId":"Rich","isDeprecatedLicenseId":false}]}
                """u8.ToArray();
            var loaded = RegistryLoadVectors.LoadPair(licenses, exceptions, options: null, useStreams);
            Assert.That(loaded.Status, Is.EqualTo(SpdxRegistryLoadStatus.Success));
            var registry = loaded.Registry!;
            Assert.That(registry.TryGetLicense("mit", out var malformed), Is.True);
            Assert.That(malformed!.Name, Is.Null);
            Assert.That(malformed.Reference, Is.Null);
            Assert.That(malformed.SeeAlso, Is.Empty);
            Assert.That(registry.TryGetLicense("custom", out var custom), Is.True);
            Assert.That((custom!.Name, custom.Reference, custom.IsDeprecated), Is.EqualTo((" Aé ", "local#1", true)));
            Assert.That(custom.SeeAlso, Is.EqualTo(new[] { " b ", "", " b " }));
            RegistryTests.AssertReadOnly((IList)custom.SeeAlso);
            Assert.That(registry.TryGetException("Empty", out var empty), Is.True);
            Assert.That(empty!.Name, Is.Null);
            Assert.That(empty.Reference, Is.Null);
            Assert.That(empty.SeeAlso, Is.Empty);
            Assert.That(registry.TryGetException("Rich", out var rich), Is.True);
            Assert.That((rich!.Name, rich.Reference), Is.EqualTo(("", " x ")));
            Assert.That(rich.SeeAlso, Is.EqualTo(new[] { "z", "a" }));
            RegistryTests.AssertReadOnly((IList)rich.SeeAlso);
        }
        [TestCase("MINIMAL")]
        [TestCase("SLICE-PAYLOAD")]
        public void ReturnedRegistryOwnsItsDataAndSliceIdentity(string name) {
            var (licenses, exceptions, _) = RegistryLoadVectors.Inputs(RegistryLoadVectors.Find(name));
            var first = WithSentinels(licenses);
            var second = WithSentinels(exceptions);
            var loaded = SpdxLicenseRegistry.Load(first.AsMemory(1, licenses.Length), second.AsMemory(1, exceptions.Length));
            first.AsSpan().Fill(0xFF);
            second.AsSpan().Fill(0xFF);
            var registry = RegistryLoadVectors.AssertSuccess(loaded, name);
            Assert.That(registry.TryGetLicense("mit", out var mit), Is.True);
            Assert.That(mit!.Identifier, Is.EqualTo("MIT"));
            var validation = Validate("MIT WITH LLVM-exception", registry);
            Assert.That(Flags(validation), Is.EqualTo((false, false, false)));
        }
        [Test]
        public void InvalidSliceOffsetsExcludePhysicalPrefix() {
            var fixture = RegistryLoadVectors.Find("DUPLICATE-ESCAPED-65");
            var (licenses, exceptions, options) = RegistryLoadVectors.Inputs(fixture);
            var result = SpdxLicenseRegistry.Load(WithSentinels(licenses).AsMemory(1, licenses.Length), exceptions, options);
            RegistryLoadVectors.AssertOutcome(result, fixture.GetProperty("expected"));
        }
        [TestCase("", false)]
        [TestCase("tr-TR", false)]
        [TestCase("", true)]
        [TestCase("tr-TR", true)]
        public void ConcurrentExternalLoadsRemainIndependentAcrossCultures(string cultureName, bool useStreams) {
            var embedded = SpdxLicenseRegistry.Default;
            Parallel.For(0, 16, index => {
                var original = CultureInfo.CurrentCulture;
                try {
                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
                    string name = index % 2 == 0 ? "MINIMAL" : "SELECTED";
                    var registry = Load(name, useStreams);
                    Assert.That(registry.TryGetLicense("mIt", out _), Is.EqualTo(name == "MINIMAL"));
                    Assert.That(registry.TryGetLicense("s1-CUSTOM", out _), Is.EqualTo(name == "SELECTED"));
                    Assert.That(Validate("S1-Custom OR LicenseRef-local", registry).HasIdentifierErrors, Is.EqualTo(name == "MINIMAL"));
                    Assert.That(SpdxLicenseRegistry.Default, Is.SameAs(embedded));
                    Assert.That(embedded.TryGetLicense("S1-Custom", out _), Is.False);
                }
                finally { CultureInfo.CurrentCulture = original; }
            });
        }
        [Test]
        public void ValidationCarriesSelectedIdentityAndAllEvidenceAfterTruncation() {
            var minimal = Load("MINIMAL");
            var known = Validate("MIT WITH LLVM-exception", minimal);
            Assert.That(known.IdentifierOccurrences.Select(Occurrence),
                Is.EqualTo(new[] { ("MIT", 0, 3, SpdxIdentifierRole.License, SpdxIdentifierLookupStatus.Known, "MIT", (bool?)false),
                    ("LLVM-exception", 9, 14, SpdxIdentifierRole.Exception, SpdxIdentifierLookupStatus.Known, "LLVM-exception", (bool?)false) }));
            Assert.That(known.Diagnostics, Is.Empty);
            var unknown = Validate("Apache-2.0", minimal);
            Assert.That(Occurrence(unknown.IdentifierOccurrences.Single()),
                Is.EqualTo(("Apache-2.0", 0, 10, SpdxIdentifierRole.License, SpdxIdentifierLookupStatus.Unknown, (string?)null, (bool?)null)));
            Assert.That(unknown.Diagnostics.Single().Code, Is.EqualTo("SPDX_ID_UNKNOWN_LICENSE"));
            var embedded = SpdxLicenseRegistry.Default;
            Assert.That(Occurrence(Validate("Apache-2.0", embedded).IdentifierOccurrences.Single()),
                Is.EqualTo(("Apache-2.0", 0, 10, SpdxIdentifierRole.License, SpdxIdentifierLookupStatus.Known, "Apache-2.0", (bool?)false)));
            Assert.That(Validate("S1-Custom", embedded).HasIdentifierErrors, Is.True);
            var selected = Load("SELECTED");
            const string Source = "S1-Retired OR s1-custom OR Apache-2.0 OR S1-Flip WITH S1-Custom OR LicenseRef-local";
            var expression = GrammarTests.Parse(Source);
            Assert.That(SpdxExpressionFormatter.Format(expression), Is.EqualTo(Source));
            var full = Validate(Source, selected);
            var limited = Validate(Source, selected, new() { MaxDiagnostics = 1 });
            var expected = new[] {
                ("S1-Retired", 0, 10, SpdxIdentifierRole.License, SpdxIdentifierLookupStatus.Known, "S1-Retired", (bool?)true),
                ("s1-custom", 14, 9, SpdxIdentifierRole.License, SpdxIdentifierLookupStatus.Known, "S1-Custom", (bool?)false),
                ("Apache-2.0", 27, 10, SpdxIdentifierRole.License, SpdxIdentifierLookupStatus.Unknown, (string?)null, (bool?)null),
                ("S1-Flip", 41, 7, SpdxIdentifierRole.License, SpdxIdentifierLookupStatus.WrongKind, null, null),
                ("S1-Custom", 54, 9, SpdxIdentifierRole.Exception, SpdxIdentifierLookupStatus.WrongKind, null, null)
            };
            foreach(var result in new[] { full, limited }) {
                Assert.That(result.IdentifierOccurrences.Select(Occurrence), Is.EqualTo(expected));
                var reference = result.References.Single();
                Assert.That((reference.Span.Start, reference.Span.Length, reference.DocumentId, reference.LicenseId), Is.EqualTo((67, 16, (string?)null, "local")));
            }
            var expectedDiagnostics = new[] {
                ("SPDX_ID_DEPRECATED", SpdxDiagnosticSeverity.Warning, 0, 10),
                ("SPDX_ID_UNKNOWN_LICENSE", SpdxDiagnosticSeverity.Error, 27, 10),
                ("SPDX_ID_WRONG_KIND", SpdxDiagnosticSeverity.Error, 41, 7),
                ("SPDX_ID_WRONG_KIND", SpdxDiagnosticSeverity.Error, 54, 9)
            };
            Assert.That(full.Diagnostics.Select(Diagnostic), Is.EqualTo(expectedDiagnostics));
            Assert.That(limited.Diagnostics.Select(Diagnostic), Is.EqualTo(expectedDiagnostics.Take(1)));
            Assert.That(Flags(full), Is.EqualTo((true, true, false)));
            Assert.That(Flags(limited), Is.EqualTo((true, true, true)));
            Assert.That(Flags(Validate("S1-Retired OR LicenseRef-local", selected)), Is.EqualTo((false, true, false)));
            var repeated = Validate("S1-Retired OR S1-Retired", selected, new() { MaxDiagnostics = 1 });
            Assert.That(Flags(repeated), Is.EqualTo((false, false, true)));
            Assert.That(repeated.IdentifierOccurrences.Select(row => (row.Span.Start, row.Span.Length, row.IsDeprecated)),
                Is.EqualTo(new[] { (0, 10, (bool?)true), (14, 10, (bool?)true) }));
            var deprecated = Validate("S1-Custom WITH s1-flip", selected);
            Assert.That(Occurrence(deprecated.IdentifierOccurrences[1]),
                Is.EqualTo(("s1-flip", 15, 7, SpdxIdentifierRole.Exception, SpdxIdentifierLookupStatus.Known, "S1-Flip", (bool?)true)));
            Assert.That(Diagnostic(deprecated.Diagnostics.Single()), Is.EqualTo(("SPDX_ID_DEPRECATED", SpdxDiagnosticSeverity.Warning, 15, 7)));
        }
        [Test]
        public void LiteralPlusKeyDoesNotCreateBaseAlias() {
            var registry = Load("PLUS-ONLY");
            Assert.That(registry.TryGetLicense("old+", out var old), Is.True);
            Assert.That((old!.Identifier, old.IsDeprecated), Is.EqualTo(("Old+", true)));
            Assert.That(registry.TryGetLicense("Old", out var absent), Is.False);
            Assert.That(absent, Is.Null);
            Assert.That(SpdxExpressionFormatter.Format(GrammarTests.Parse("Old+")), Is.EqualTo("Old+"));
            var result = Validate("Old+", registry);
            Assert.That(Occurrence(result.IdentifierOccurrences.Single()),
                Is.EqualTo(("Old", 0, 3, SpdxIdentifierRole.License, SpdxIdentifierLookupStatus.Unknown, (string?)null, (bool?)null)));
            Assert.That(Diagnostic(result.Diagnostics.Single()), Is.EqualTo(("SPDX_ID_UNKNOWN_LICENSE", SpdxDiagnosticSeverity.Error, 0, 3)));
            Assert.That(Flags(result), Is.EqualTo((true, false, false)));
        }
        static SpdxLicenseRegistry Load(string fixture, bool useStreams = false) {
            var (licenses, exceptions, options) = RegistryLoadVectors.Inputs(RegistryLoadVectors.Find(fixture));
            var result = RegistryLoadVectors.LoadPair(licenses, exceptions, options, useStreams);
            return RegistryLoadVectors.AssertSuccess(result, fixture);
        }
        static (string Identifier, bool IsDeprecated)[] ReadEntries(byte[] source, string collection, string id, int count) {
            using(var document = JsonDocument.Parse(source)) {
                var entries = document.RootElement.GetProperty(collection).EnumerateArray()
                    .Select(entry => (entry.GetProperty(id).GetString()!, entry.GetProperty("isDeprecatedLicenseId").GetBoolean()))
                    .OrderBy(entry => entry.Item1, StringComparer.Ordinal).ToArray();
                Assert.That(entries, Has.Length.EqualTo(count));
                return entries;
            }
        }
        static string[] Spellings(string id) {
            return new[] { id, id.ToLowerInvariant(), id.ToUpperInvariant() };
        }
        static byte[] WithSentinels(byte[] input) {
            var buffer = new byte[input.Length + 2];
            buffer.AsSpan().Fill(0xFF);
            input.CopyTo(buffer, 1);
            return buffer;
        }
        static SpdxValidationResult Validate(string source, SpdxLicenseRegistry registry, SpdxValidationOptions? options = null) {
            var result = SpdxExpressionValidator.Validate(GrammarTests.Parse(source), registry, options);
            Assert.That(result.RegistryIdentity, Is.SameAs(registry.Identity));
            return result;
        }
        static (string, int, int, SpdxIdentifierRole, SpdxIdentifierLookupStatus, string?, bool?) Occurrence(SpdxIdentifierOccurrence row) {
            return (row.Identifier, row.Span.Start, row.Span.Length, row.Role, row.LookupStatus, row.CanonicalIdentifier, row.IsDeprecated);
        }
        static (string, SpdxDiagnosticSeverity, int, int) Diagnostic(SpdxDiagnostic row) {
            return (row.Code, row.Severity, row.Span.Start, row.Span.Length);
        }
        static (bool, bool, bool) Flags(SpdxValidationResult result) {
            return (result.HasIdentifierErrors, result.HasUnresolvedReferences, result.DiagnosticsTruncated);
        }
    }
}
