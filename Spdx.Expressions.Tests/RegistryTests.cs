using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Spdx.Expressions.Tests {

    public class RegistryTests {
        [TestCase("licenses", "licenseId", 740, "47d1cc681abe31166b342b6cc4aab13a6ba8ea5c48794697fe3bf8b1dbaf509a")]
        [TestCase("exceptions", "licenseExceptionId", 86, "59ee7c150de521d635cbf9367b8cf543b8da6781ea3ac1c073d305524f84ada2")]
        public void EveryUpstreamEntryAndExactBytes(string collection, string idProperty, int count, string expectedHash) {
            byte[] bytes = File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "Data", collection + ".json"));
            Assert.That(Convert.ToHexStringLower(SHA256.HashData(bytes)), Is.EqualTo(expectedHash));
            var registry = SpdxLicenseRegistry.Default;
            Assert.That(registry.Identity.LicenseListVersion, Is.EqualTo("3.29.0"));
            Assert.That(collection == "licenses" ? registry.Identity.LicensesSha256 : registry.Identity.ExceptionsSha256, Is.EqualTo(expectedHash));
            using(var document = JsonDocument.Parse(bytes)) {
                Assert.That(document.RootElement.GetProperty("licenseListVersion").GetString(), Is.EqualTo("3.29.0"));
                var rows = document.RootElement.GetProperty(collection).EnumerateArray().ToArray();
                Assert.That(rows.Length, Is.EqualTo(count));
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(var row in rows) {
                    string id = row.GetProperty(idProperty).GetString()!;
                    bool deprecated = row.GetProperty("isDeprecatedLicenseId").GetBoolean();
                    Assert.That(seen.Add(id), Is.True);
                    foreach(string spelling in new[] { id, id.ToUpperInvariant(), id.ToLowerInvariant() }) {
                        if(collection == "licenses") {
                            Assert.That(registry.TryGetLicense(spelling, out var entry), Is.True, spelling);
                            Assert.That(entry!.Identifier, Is.EqualTo(id));
                            Assert.That(entry.IsDeprecated, Is.EqualTo(deprecated));
                            Assert.That(registry.TryGetException(spelling, out _), Is.False);
                        }
                        else {
                            Assert.That(registry.TryGetException(spelling, out var entry), Is.True, spelling);
                            Assert.That(entry!.Identifier, Is.EqualTo(id));
                            Assert.That(entry.IsDeprecated, Is.EqualTo(deprecated));
                            Assert.That(registry.TryGetLicense(spelling, out _), Is.False);
                        }
                    }
                    var expression = GrammarTests.Parse(collection == "licenses" ? id : "MIT WITH " + id);
                    var validation = SpdxExpressionValidator.Validate(expression, registry);
                    Assert.That(validation.HasIdentifierErrors, Is.False, id);
                    if(collection == "exceptions")
                        Assert.That(validation.IdentifierOccurrences[^1].IsDeprecated, Is.EqualTo(deprecated));
                }
                var entries = collection == "licenses" ? registry.Licenses.Select(x => x.Identifier) : registry.Exceptions.Select(x => x.Identifier);
                Assert.That(entries, Is.EqualTo(seen.OrderBy(x => x, StringComparer.Ordinal)));
                using(var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "Data", "manifest.json")))) {
                    Assert.That(manifest.RootElement.GetProperty("commit").GetString(), Is.EqualTo("31ba1a50e5397e00a304dbadc76531740e89ee48"));
                    var file = manifest.RootElement.GetProperty("files").EnumerateArray().Single(x => x.GetProperty("file").GetString() == collection + ".json");
                    Assert.That(file.GetProperty("sha256").GetString(), Is.EqualTo(expectedHash));
                    Assert.That(file.GetProperty("byteLength").GetInt32(), Is.EqualTo(bytes.Length));
                    Assert.That(file.GetProperty("entries").GetInt32(), Is.EqualTo(count));
                }
            }
        }

        [Test]
        public void NoMutablePublicGraphOrCollections() {
            var parse = SpdxExpressionParser.Parse("(MIT OR MIT) AND LicenseRef-X");
            var root = (SpdxAndExpression)parse.Expression!.Root;
            var validation = SpdxExpressionValidator.Validate(parse.Expression, SpdxLicenseRegistry.Default);
            foreach(var list in new object[] { root.Operands, ((SpdxOrExpression)((SpdxGroupExpression)root.Operands[0]).Inner).Operands,
            parse.Diagnostics, validation.Diagnostics, validation.IdentifierOccurrences, validation.References,
            SpdxLicenseRegistry.Default.Licenses, SpdxLicenseRegistry.Default.Exceptions }) {
                Assert.That(list, Is.Not.InstanceOf<Array>());
                Assert.That(((IList)list).IsReadOnly, Is.True);
                Assert.Throws<NotSupportedException>(() => ((IList)list).Clear());
            }
            var assembly = typeof(SpdxParsedExpression).Assembly;
            foreach(var type in assembly.GetExportedTypes().Where(x => x.IsClass && !x.IsAbstract && !x.Name.EndsWith("Options", StringComparison.Ordinal)))
                Assert.That(type.GetConstructors(), Is.Empty, type.FullName);
            Assert.That(typeof(SpdxExpressionNode).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).All(x => x.IsFamilyAndAssembly), Is.True);
            foreach(var type in assembly.GetExportedTypes().Where(x => !x.Name.EndsWith("Options", StringComparison.Ordinal)))
                Assert.That(type.GetProperties().Where(x => x.SetMethod?.IsPublic == true), Is.Empty, type.FullName);
        }

        [TestCase("")]
        [TestCase("tr-TR")]
        public void ConcurrentCultureIndependentOperations(string cultureName) {
            Parallel.For(0, 128, index => {
                var original = CultureInfo.CurrentCulture;
                try {
                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
                    var registry = SpdxLicenseRegistry.Default;
                    var expression = GrammarTests.Parse("mit OR (GPL-2.0+ WITH llvm-exception) OR LicenseRef-I");
                    var result = SpdxExpressionValidator.Validate(expression, registry);
                    Assert.That(result.HasIdentifierErrors, Is.False);
                    Assert.That(result.HasUnresolvedReferences, Is.True);
                    Assert.That(result.IdentifierOccurrences[0].CanonicalIdentifier, Is.EqualTo("MIT"));
                    Assert.That(SpdxExpressionFormatter.Format(expression), Is.EqualTo(expression.SourceText));
                    Assert.That(registry.TryGetLicense("mıt", out _), Is.False);
                    Assert.That(registry.TryGetLicense(" MIT", out _), Is.False);
                }
                finally { CultureInfo.CurrentCulture = original; }
            });
        }
    }

}
