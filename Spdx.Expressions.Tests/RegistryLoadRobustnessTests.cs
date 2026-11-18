using System;
using System.Buffers;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Spdx.Expressions.Tests {
    public class RegistryLoadRobustnessTests {
        [TestCase("MINIMAL")]
        [TestCase("JSON-EMPTY")]
        public void PreCancellationPrecedesInputAccessAndDiagnostics(string fixture) {
            var (licenses, exceptions, _) = RegistryLoadVectors.Inputs(RegistryLoadVectors.Find(fixture));
            using(var source = new ObservedMemory(licenses, () => Assert.Fail("Cancelled load accessed the source span."))) {
                var token = new CancellationToken(true);
                var error = Assert.Throws<OperationCanceledException>(() => SpdxLicenseRegistry.Load(source.Input, exceptions, cancellationToken: token));
                Assert.That(error!.CancellationToken, Is.EqualTo(token));
            }
        }
        [Test]
        public void EarlierFailureAndByteAllowancePrecedeMemoryAccess() {
            var (licenses, exceptions, _) = RegistryLoadVectors.Inputs(RegistryLoadVectors.Find("MINIMAL"));
            using(var source = new ObservedMemory(exceptions, () => Assert.Fail("A rejected input was accessed."))) {
                var invalid = SpdxLicenseRegistry.Load("{"u8.ToArray(), source.Input);
                Assert.That(invalid.Diagnostics[0].Input, Is.EqualTo(SpdxRegistryInput.Licenses));
                var excess = SpdxLicenseRegistry.Load(licenses, source.Input, new() { MaxInputBytes = 118 });
                Assert.That(excess.Status, Is.EqualTo(SpdxRegistryLoadStatus.LimitExceeded));
                RegistryLoadVectors.AssertDiagnostic(excess.Diagnostics[0], "SPDX_REGISTRY_LIMIT_EXCEEDED",
                    SpdxRegistryInput.Exceptions, 118, null, null, "MaxInputBytes");
            }
        }
        [TestCase(false)]
        [TestCase(true)]
        public void CancellationAtEitherInputAcquisition(bool cancelOnExceptions) {
            var (licenses, exceptions, _) = RegistryLoadVectors.Inputs(RegistryLoadVectors.Find("MINIMAL"));
            byte[] longLicenses = WithMetadata(licenses, "\"x\":\"" + new string('a', 8_388_608) + "\"");
            var options = new SpdxRegistryLoadOptions {
                MaxTokenBytes = 8_388_610,
                MaxStringLength = 8_388_608,
                MaxTotalStringLength = 8_388_767
            };
            using(var cancellation = new CancellationTokenSource())
            using(var entered = new ManualResetEventSlim())
            using(var release = new ManualResetEventSlim())
            using(var source = new ObservedMemory(cancelOnExceptions ? exceptions : longLicenses, () => {
                entered.Set();
                release.Wait();
            })) {
                ReadOnlyMemory<byte> left = cancelOnExceptions ? longLicenses : source.Input;
                ReadOnlyMemory<byte> right = cancelOnExceptions ? source.Input : exceptions;
                var pending = Task.Run(() => SpdxLicenseRegistry.Load(left, right, options, cancellation.Token));
                try {
                    Assert.That(entered.Wait(TimeSpan.FromSeconds(10)), Is.True, "Load did not reach the designated acquisition boundary.");
                    cancellation.Cancel();
                }
                finally { release.Set(); }
                var error = Assert.CatchAsync<OperationCanceledException>(async () => await pending);
                Assert.That(error!.CancellationToken, Is.EqualTo(cancellation.Token));
            }
        }
        [TestCase(false)]
        [TestCase(true)]
        public void LongStringSucceedsWithRaisedBudgets(bool useStreams) {
            var (licenses, exceptions, _) = RegistryLoadVectors.Inputs(RegistryLoadVectors.Find("MINIMAL"));
            byte[] longLicenses = WithMetadata(licenses, "\"x\":\"" + new string('a', 8_388_608) + "\"");
            var options = new SpdxRegistryLoadOptions {
                MaxTokenBytes = 8_388_610,
                MaxStringLength = 8_388_608,
                MaxTotalStringLength = 8_388_767
            };
            var result = RegistryLoadVectors.LoadPair(longLicenses, exceptions, options, useStreams);
            AssertLongInputSuccess(result, "8191d5a8a22e635af4d2b51ac5497ee6bb1323eecaeba5b1d35c0013da02363b");
        }
        [TestCase("B-HUGE-OPTION-VALID", false)]
        [TestCase("B-HUGE-OPTION-VALID", true)]
        [TestCase("B-TOTAL-ZERO-REMAIN", false)]
        [TestCase("B-TOTAL-ZERO-REMAIN", true)]
        [TestCase("B-DEPTH-64-AT", false)]
        [TestCase("B-DEPTH-64-AT", true)]
        [TestCase("B-DEPTH-65-TIE", false)]
        [TestCase("B-DEPTH-65-TIE", true)]
        public void BoundaryVectorsUnderHostileInputWatchdog(string name, bool useStreams) {
            var vector = RegistryLoadVectors.Find(name);
            var (licenses, exceptions, options) = RegistryLoadVectors.Inputs(vector);
            var result = RegistryLoadVectors.LoadPair(licenses, exceptions, options, useStreams);
            RegistryLoadVectors.AssertOutcome(result, vector.GetProperty("expected"));
        }
        [TestCase(false)]
        [TestCase(true)]
        public void HugePropertyNameRejectsAtItsOpeningQuote(bool useStreams) {
            var (licenses, exceptions, _) = RegistryLoadVectors.Inputs(RegistryLoadVectors.Find("MINIMAL"));
            string licenseJson = Encoding.UTF8.GetString(licenses);
            byte[] oversized = Encoding.UTF8.GetBytes("{\"" + new string('A', 262_144) + "\":0," + licenseJson[1..]);
            var result = RegistryLoadVectors.LoadPair(oversized, exceptions, new() { MaxTokenBytes = 32, MaxStringLength = 16 }, useStreams);
            Assert.That(result.Status, Is.EqualTo(SpdxRegistryLoadStatus.LimitExceeded));
            Assert.That(result.Registry, Is.Null);
            Assert.That(result.Diagnostics, Has.Count.EqualTo(1));
            RegistryLoadVectors.AssertDiagnostic(result.Diagnostics[0], "SPDX_REGISTRY_LIMIT_EXCEEDED", SpdxRegistryInput.Licenses,
                1, null, null, "MaxTokenBytes");
        }
        [TestCase(false)]
        [TestCase(true)]
        public void ManyTinyTokensStopAtTheSharedBudget(bool useStreams) {
            var (licenses, exceptions, _) = RegistryLoadVectors.Inputs(RegistryLoadVectors.Find("MINIMAL"));
            string elements = string.Join(',', Enumerable.Repeat("0", 10_000));
            byte[] many = WithMetadata(licenses, "\"x\":[" + elements + "]");
            var result = RegistryLoadVectors.LoadPair(many, exceptions, new() { MaxTokens = 128 }, useStreams);
            Assert.That(result.Status, Is.EqualTo(SpdxRegistryLoadStatus.LimitExceeded));
            Assert.That(result.Registry, Is.Null);
            Assert.That(result.Diagnostics, Has.Count.EqualTo(1));
            RegistryLoadVectors.AssertDiagnostic(result.Diagnostics[0], "SPDX_REGISTRY_LIMIT_EXCEEDED", SpdxRegistryInput.Licenses,
                330, null, "x", "MaxTokens");
        }
        [TestCase(false)]
        [TestCase(true)]
        public void HugeExponentInIgnoredValueKeepsExactByteIdentity(bool useStreams) {
            var (licenses, exceptions, _) = RegistryLoadVectors.Inputs(RegistryLoadVectors.Find("MINIMAL"));
            byte[] number = WithMetadata(licenses, "\"x\":1e+" + new string('9', 32_768));
            var result = RegistryLoadVectors.LoadPair(number, exceptions, options: null, useStreams);
            AssertLongInputSuccess(result, "7d4004bf509c4355ba8570abb5caa35ea315f5fd451bf715d59b6dc87d6b9491");
        }
        [TestCase(false)]
        [TestCase(true)]
        public void IncompleteHugeStringIsNeverChargedAsAToken(bool useStreams) {
            var (_, exceptions, _) = RegistryLoadVectors.Inputs(RegistryLoadVectors.Find("MINIMAL"));
            byte[] incomplete = Encoding.UTF8.GetBytes("{\"x\":\"" + new string('a', 262_144));
            var result = RegistryLoadVectors.LoadPair(incomplete, exceptions, new() { MaxTokenBytes = 8 }, useStreams);
            Assert.That(result.Status, Is.EqualTo(SpdxRegistryLoadStatus.InvalidData));
            Assert.That(result.Registry, Is.Null);
            Assert.That(result.Diagnostics, Has.Count.EqualTo(1));
            RegistryLoadVectors.AssertDiagnostic(result.Diagnostics[0], "SPDX_REGISTRY_INVALID_JSON", SpdxRegistryInput.Licenses,
                262_150, null, null, null);
        }
        static void AssertLongInputSuccess(SpdxRegistryLoadResult result, string expectedLicenseHash) {
            Assert.That(result.Status, Is.EqualTo(SpdxRegistryLoadStatus.Success));
            Assert.That(result.Diagnostics, Is.Empty);
            var registry = result.Registry!;
            Assert.That(registry.Identity.LicenseListVersion, Is.EqualTo("fixture-1"));
            Assert.That(registry.Identity.LicensesSha256, Is.EqualTo(expectedLicenseHash));
            Assert.That(registry.Identity.ExceptionsSha256, Is.EqualTo("3406a940b2d8e502e82ed3915160750f52ededaba45e19f844469aa0bb070d17"));
            Assert.That(registry.Licenses[0].Identifier, Is.EqualTo("MIT"));
            Assert.That(registry.Exceptions[0].Identifier, Is.EqualTo("LLVM-exception"));
        }
        static byte[] WithMetadata(byte[] licenses, string metadata) {
            string prefix = Encoding.UTF8.GetString(licenses.AsSpan(0, licenses.Length - 1));
            return Encoding.UTF8.GetBytes(prefix + "," + metadata + "}");
        }
        sealed class ObservedMemory : MemoryManager<byte> {
            readonly byte[] bytes;
            readonly Action onAccess;
            internal ObservedMemory(byte[] bytes, Action onAccess) {
                this.bytes = bytes;
                this.onAccess = onAccess;
            }
            internal ReadOnlyMemory<byte> Input {
                get {
                    return CreateMemory(bytes.Length);
                }
            }
            public override Span<byte> GetSpan() {
                onAccess();
                return bytes;
            }
            public override MemoryHandle Pin(int elementIndex = 0) {
                throw new NotSupportedException();
            }
            public override void Unpin() {
                throw new NotSupportedException();
            }
            protected override void Dispose(bool disposing) {
            }
        }
    }
}
