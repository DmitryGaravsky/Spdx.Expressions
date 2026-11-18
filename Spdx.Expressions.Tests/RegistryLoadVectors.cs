using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using NUnit.Framework;

namespace Spdx.Expressions.Tests {
    static class RegistryLoadVectors {
        static readonly JsonElement Corpus = ReadCorpus();
        internal static IEnumerable<TestCaseData> MemoryCases() {
            return Cases("RegistryMemory_");
        }
        internal static IEnumerable<TestCaseData> StreamCases() {
            return Cases("RegistryStreams_");
        }
        static IEnumerable<TestCaseData> Cases(string prefix) {
            foreach(var vector in Corpus.GetProperty("cases").EnumerateArray()) {
                string name = vector.GetProperty("id").GetString()!;
                yield return new TestCaseData(vector).SetName(prefix + name);
            }
        }
        internal static JsonElement Find(string name) {
            return Corpus.GetProperty("cases").EnumerateArray().Single(vector => vector.GetProperty("id").GetString() == name);
        }
        internal static (byte[] Licenses, byte[] Exceptions, SpdxRegistryLoadOptions Options) Inputs(JsonElement vector) {
            byte[] licenses = Decode(vector.GetProperty("Licenses"));
            byte[] exceptions = Decode(vector.GetProperty("Exceptions"));
            var options = vector.GetProperty("options").Deserialize<SpdxRegistryLoadOptions>()!;
            return (licenses, exceptions, options);
        }
        internal static SpdxRegistryLoadResult LoadPair(byte[] licenses, byte[] exceptions, SpdxRegistryLoadOptions? options, bool useStreams) {
            if(!useStreams)
                return SpdxLicenseRegistry.Load(licenses, exceptions, options);
            using(var left = new MemoryStream(licenses))
            using(var right = new MemoryStream(exceptions)) {
                var result = SpdxLicenseRegistry.Load(left, right, options);
                Assert.That(left.CanRead && right.CanRead, Is.True);
                return result;
            }
        }
        internal static SpdxLicenseRegistry AssertSuccess(SpdxRegistryLoadResult result, string vectorName) {
            AssertOutcome(result, Find(vectorName).GetProperty("expected"));
            return result.Registry!;
        }
        internal static void AssertOutcome(SpdxRegistryLoadResult result, JsonElement expected) {
            Assert.That(result.Status.ToString(), Is.EqualTo(expected.GetProperty("Status").GetString()));
            var diagnostics = expected.GetProperty("Diagnostics");
            Assert.That(result.Diagnostics, Has.Count.EqualTo(diagnostics.GetArrayLength()));
            if(result.Status != SpdxRegistryLoadStatus.Success) {
                Assert.That(result.Registry, Is.Null);
                var diagnostic = diagnostics[0];
                var index = diagnostic.GetProperty("EntryIndex");
                int? entryIndex = index.ValueKind == JsonValueKind.Null ? null : index.GetInt32();
                AssertDiagnostic(result.Diagnostics[0], diagnostic.GetProperty("Code").GetString()!,
                    Enum.Parse<SpdxRegistryInput>(diagnostic.GetProperty("Input").GetString()!),
                    diagnostic.GetProperty("ByteOffset").GetInt64(), entryIndex, diagnostic.GetProperty("PropertyName").GetString(),
                    diagnostic.GetProperty("LimitName").GetString());
                return;
            }
            Assert.That(result.Registry, Is.Not.Null);
            var registry = result.Registry!;
            var projection = expected.GetProperty("Registry");
            var identity = registry.Identity;
            Assert.That((identity.LicenseListVersion, identity.LicensesSha256, identity.ExceptionsSha256),
                Is.EqualTo((projection.GetProperty("LicenseListVersion").GetString(), projection.GetProperty("LicensesSha256").GetString(),
                    projection.GetProperty("ExceptionsSha256").GetString())));
            var licenses = registry.Licenses.Select(entry => (entry.Identifier, entry.IsDeprecated));
            var exceptions = registry.Exceptions.Select(entry => (entry.Identifier, entry.IsDeprecated));
            Assert.That(licenses, Is.EqualTo(Entries(projection.GetProperty("Licenses"))));
            Assert.That(exceptions, Is.EqualTo(Entries(projection.GetProperty("Exceptions"))));
        }
        internal static void AssertDiagnostic(SpdxRegistryDiagnostic actual, string code, SpdxRegistryInput input,
            long offset, int? entryIndex, string? propertyName, string? limitName) {
            Assert.That((actual.Code, actual.Input, actual.ByteOffset, actual.EntryIndex, actual.PropertyName, actual.LimitName),
                Is.EqualTo((code, input, offset, entryIndex, propertyName, limitName)));
            Assert.That(actual.Message, Is.Not.Empty);
        }
        static IEnumerable<(string?, bool)> Entries(JsonElement entries) {
            foreach(var entry in entries.EnumerateArray())
                yield return (entry.GetProperty("Identifier").GetString(), entry.GetProperty("IsDeprecated").GetBoolean());
        }
        static byte[] Decode(JsonElement input) {
            if(input.ValueKind == JsonValueKind.String)
                input = Corpus.GetProperty("inputs").GetProperty(input.GetString()!);
            if(input.TryGetProperty("hex", out var hex))
                return Convert.FromHexString(hex.GetString()!);
            return Encoding.UTF8.GetBytes(input.GetProperty("utf8").GetString()!);
        }
        static JsonElement ReadCorpus() {
            string path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "registry-memory.json");
            using(var data = JsonDocument.Parse(File.ReadAllBytes(path))) {
                return data.RootElement.Clone();
            }
        }
    }
}
