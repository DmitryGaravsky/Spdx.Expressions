using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using NUnit.Framework;

namespace Spdx.Expressions.Tests {
    public class RegistryStreamLoadTests {
        const int LicenseLength = 97;
        const int ExceptionLength = 119;
        const int PairLength = LicenseLength + ExceptionLength;
        [TestCaseSource(typeof(RegistryLoadVectors), nameof(RegistryLoadVectors.StreamCases))]
        public void AllOverloadsMatchFrozenOutcomes(JsonElement vector) {
            AssertVector(vector, 1);
        }
        [TestCase("UNKNOWN-METADATA")]
        [TestCase("UNICODE-NESTED-VALUE")]
        [TestCase("LEXICAL-NESTED-NO-CONTEXT")]
        [TestCase("UTF8-BAD-FOURTH")]
        [TestCase("PV-06")]
        public void SelectedVectorsMatchAcrossSplitScalarsAndTokens(string name) {
            AssertVector(RegistryLoadVectors.Find(name), 2, 1, 3);
        }
        [TestCase("SLICE-PAYLOAD")]
        [TestCase("DUPLICATE-ESCAPED-65")]
        public void NonzeroPositionsRebaseHashesAndDiagnostics(string name) {
            var vector = RegistryLoadVectors.Find(name);
            var (licenses, exceptions, options) = RegistryLoadVectors.Inputs(vector);
            byte[] prefixedLicenses = [0xFF, 0x80, 0, .. licenses];
            byte[] prefixedExceptions = [0xFF, 0xFE, .. exceptions];
            var expected = vector.GetProperty("expected");
            var memory = SpdxLicenseRegistry.Load(prefixedLicenses.AsMemory(3), prefixedExceptions.AsMemory(2), options);
            RegistryLoadVectors.AssertOutcome(memory, expected);
            using(var left = new MemoryStream(prefixedLicenses) { Position = 3 })
            using(var right = new MemoryStream(prefixedExceptions) { Position = 2 }) {
                var result = SpdxLicenseRegistry.Load(left, right, options);
                RegistryLoadVectors.AssertOutcome(result, expected);
                Assert.That(left.Position, Is.EqualTo(prefixedLicenses.Length));
                int rightPosition = result.Registry is null ? 2 : prefixedExceptions.Length;
                Assert.That(right.Position, Is.EqualTo(rightPosition));
            }
        }
        [TestCase("B-INPUT-AT")]
        [TestCase("B-TOTAL-AT")]
        public void ExactAllowanceRequiresAnEofProbe(string name) {
            var vector = RegistryLoadVectors.Find(name);
            var (licenses, exceptions, options) = RegistryLoadVectors.Inputs(vector);
            using(var pair = new Inputs(licenses, exceptions, 1)) {
                var result = SpdxLicenseRegistry.Load(pair.Licenses, pair.Exceptions, options);
                RegistryLoadVectors.AssertOutcome(result, vector.GetProperty("expected"));
                Assert.That(pair.Licenses.Cursor, Is.EqualTo(LicenseLength));
                Assert.That(pair.Licenses.Reads[^1].Count, Is.Zero);
                Assert.That(pair.Exceptions.Cursor, Is.EqualTo(ExceptionLength));
                Assert.That(pair.Exceptions.Reads[^1], Is.EqualTo((1, (int?)0)));
                pair.AssertCanReadOrder("licenses", "exceptions");
            }
        }
        [TestCase("B-INPUT-OVER", 1)]
        [TestCase("B-TOTAL-OVER", 1)]
        [TestCase("B-BYTE-TIE", 1)]
        [TestCase("B-INPUT-OVER", 16_384)]
        [TestCase("B-TOTAL-OVER", 16_384)]
        [TestCase("B-BYTE-TIE", 16_384)]
        public void OverAllowanceLeavesTheUnreadTail(string name, int cap) {
            var vector = RegistryLoadVectors.Find(name);
            var (licenses, exceptions, options) = RegistryLoadVectors.Inputs(vector);
            byte[] padded = [.. exceptions, .. "          "u8.ToArray()];
            using(var pair = new Inputs(licenses, padded, cap)) {
                var result = SpdxLicenseRegistry.Load(pair.Licenses, pair.Exceptions, options);
                RegistryLoadVectors.AssertOutcome(result, vector.GetProperty("expected"));
                Assert.That(pair.Exceptions.Cursor, Is.EqualTo(ExceptionLength));
                Assert.That(padded.Length - pair.Exceptions.Cursor, Is.EqualTo(10));
                int consumed = 0;
                foreach(var (requested, count) in pair.Exceptions.Reads) {
                    Assert.That(requested, Is.InRange(1, ExceptionLength - consumed));
                    Assert.That(count, Is.GreaterThan(0));
                    consumed += count!.Value;
                }
            }
        }
        [TestCase("B-TOTAL-ZERO-REMAIN", 1)]
        [TestCase("B-TOTAL-ZERO-REMAIN-EMPTY", 0)]
        public void ZeroPairAllowanceMakesExactlyOneProbe(string name, int returned) {
            var vector = RegistryLoadVectors.Find(name);
            var (licenses, exceptions, options) = RegistryLoadVectors.Inputs(vector);
            using(var pair = new Inputs(licenses, exceptions)) {
                var result = SpdxLicenseRegistry.Load(pair.Licenses, pair.Exceptions, options);
                RegistryLoadVectors.AssertOutcome(result, vector.GetProperty("expected"));
                Assert.That(pair.Exceptions.Reads, Is.EqualTo(new[] { (1, (int?)returned) }));
                Assert.That(pair.Exceptions.Cursor, Is.EqualTo(returned));
            }
        }
        [Test]
        public void LicenseFailureStopsBeforeAnyExceptionRead() {
            var vector = RegistryLoadVectors.Find("PV-06");
            var (licenses, exceptions, options) = RegistryLoadVectors.Inputs(vector);
            var unexpected = new IOException("Exceptions.Read must not be called.");
            using(var pair = new Inputs(licenses, exceptions)) {
                pair.Exceptions.BeforeRead = () => throw unexpected;
                var result = SpdxLicenseRegistry.Load(pair.Licenses, pair.Exceptions, options);
                RegistryLoadVectors.AssertOutcome(result, vector.GetProperty("expected"));
                pair.AssertCanReadOrder("licenses", "exceptions");
                Assert.That(pair.Licenses.Reads[^1].Count, Is.Zero);
                Assert.That(pair.Exceptions.Reads, Is.Empty);
            }
            using(var pair = new Inputs([0x7B, 0x80, 0x7D, 0x20, 0x20], exceptions)) {
                pair.Exceptions.BeforeRead = () => throw unexpected;
                var result = SpdxLicenseRegistry.Load(pair.Licenses, pair.Exceptions, new() { MaxInputBytes = 2 });
                Assert.That(result.Status, Is.EqualTo(SpdxRegistryLoadStatus.LimitExceeded));
                Assert.That(result.Registry, Is.Null);
                RegistryLoadVectors.AssertDiagnostic(result.Diagnostics.Single(), "SPDX_REGISTRY_LIMIT_EXCEEDED",
                    SpdxRegistryInput.Licenses, 2, null, null, "MaxInputBytes");
                Assert.That(pair.Licenses.Reads, Is.EqualTo(new[] { (3, (int?)3) }));
                Assert.That(pair.Licenses.Cursor, Is.EqualTo(3));
                Assert.That(pair.Exceptions.Reads, Is.Empty);
                pair.AssertCanReadOrder("licenses", "exceptions");
            }
        }
        [Test]
        public void NullAndSameInstanceChecksPrecedeCanRead() {
            var options = new SpdxRegistryLoadOptions { MaxInputBytes = 0 };
            var token = new CancellationToken(true);
            Assert.That(Assert.Throws<ArgumentNullException>(() => SpdxLicenseRegistry.Load((Stream)null!, null!, options, token))!.ParamName,
                Is.EqualTo("licensesUtf8"));
            using(var pair = Inputs.Minimal()) {
                pair.Licenses.Readable = () => throw new IOException("Licenses.CanRead must not be called.");
                Assert.That(Assert.Throws<ArgumentNullException>(() => SpdxLicenseRegistry.Load(pair.Licenses, null!, options, token))!.ParamName,
                    Is.EqualTo("exceptionsUtf8"));
                Assert.That(Assert.Throws<ArgumentException>(() => SpdxLicenseRegistry.Load(pair.Licenses, pair.Licenses, options, token))!.ParamName,
                    Is.EqualTo("exceptionsUtf8"));
                pair.AssertCanReadOrder();
                pair.AssertNoReads();
            }
        }
        [TestCase(false, "licensesUtf8")]
        [TestCase(true, "exceptionsUtf8")]
        public void UnreadableArgumentsPrecedeOptionsAndCancellation(bool unreadableExceptions, string parameter) {
            using(var pair = Inputs.Minimal()) {
                var options = new SpdxRegistryLoadOptions { MaxInputBytes = 0 };
                if(unreadableExceptions)
                    pair.Exceptions.Readable = () => false;
                else {
                    pair.Licenses.Readable = () => false;
                    pair.Exceptions.Readable = () => throw new IOException("Exceptions.CanRead must not be called.");
                }
                var error = Assert.Throws<ArgumentException>(() => SpdxLicenseRegistry.Load(pair.Licenses, pair.Exceptions, options, new(true)));
                Assert.That(error!.ParamName, Is.EqualTo(parameter));
                pair.AssertCanReadOrder(unreadableExceptions ? ["licenses", "exceptions"] : ["licenses"]);
                pair.AssertNoReads();
            }
        }
        [TestCase(false, "licensesUtf8")]
        [TestCase(true, "exceptionsUtf8")]
        public void DisposedMemoryStreamIsUnreadable(bool disposedExceptions, string parameter) {
            using(var disposed = new MemoryStream())
            using(var pair = Inputs.Minimal()) {
                disposed.Dispose();
                Stream licenses = disposedExceptions ? pair.Licenses : disposed;
                Stream exceptions = disposedExceptions ? disposed : pair.Exceptions;
                Assert.That(Assert.Throws<ArgumentException>(() => SpdxLicenseRegistry.Load(licenses, exceptions))!.ParamName, Is.EqualTo(parameter));
                pair.AssertCanReadOrder(disposedExceptions ? ["licenses"] : []);
                pair.AssertNoReads();
            }
        }
        [TestCase(false)]
        [TestCase(true)]
        public void ThrowingDisposedGetterPropagatesItsOriginalException(bool disposedExceptions) {
            using(var pair = Inputs.Minimal()) {
                var sentinel = new ObjectDisposedException("Disposed input");
                var stream = disposedExceptions ? pair.Exceptions : pair.Licenses;
                stream.Readable = () => throw sentinel;
                var error = Assert.Throws<ObjectDisposedException>(() =>
                    SpdxLicenseRegistry.Load(pair.Licenses, pair.Exceptions, new() { MaxInputBytes = 0 }, new(true)));
                Assert.That(error, Is.SameAs(sentinel));
                pair.AssertCanReadOrder(disposedExceptions ? ["licenses", "exceptions"] : ["licenses"]);
                pair.AssertNoReads();
            }
        }
        [TestCase(0, -1, 65, 0, "MaxInputBytes")]
        [TestCase(119, -1, 65, 0, "MaxTotalInputBytes")]
        [TestCase(119, PairLength, 65, 0, "MaxJsonDepth")]
        [TestCase(119, PairLength, 64, 0, "MaxTokenBytes")]
        public void InvalidOptionsFollowCanReadAndPrecedeCancellation(int input, int total, int depth, int tokenBytes, string parameter) {
            using(var pair = Inputs.Minimal()) {
                var options = new SpdxRegistryLoadOptions {
                    MaxInputBytes = input,
                    MaxTotalInputBytes = total,
                    MaxJsonDepth = depth,
                    MaxTokenBytes = tokenBytes
                };
                var token = new CancellationToken(true);
                var streamError = Assert.Throws<ArgumentOutOfRangeException>(() => SpdxLicenseRegistry.Load(pair.Licenses, pair.Exceptions, options, token));
                var memoryError = Assert.Throws<ArgumentOutOfRangeException>(() => SpdxLicenseRegistry.Load(ReadOnlyMemory<byte>.Empty, default, options, token));
                Assert.That(streamError!.ParamName, Is.EqualTo(parameter));
                Assert.That(memoryError!.ParamName, Is.EqualTo(parameter));
                pair.AssertCanReadOrder("licenses", "exceptions");
                pair.AssertNoReads();
            }
        }
        [Test]
        public void DistinctWrappersMayShareAStreamCursor() {
            var (licenses, exceptions, _) = RegistryLoadVectors.Inputs(RegistryLoadVectors.Find("MINIMAL"));
            using(var shared = new MemoryStream([.. licenses, .. exceptions]))
            using(var pair = new Inputs(shared, shared)) {
                var result = SpdxLicenseRegistry.Load(pair.Licenses, pair.Exceptions);
                Assert.That(result.Status, Is.EqualTo(SpdxRegistryLoadStatus.InvalidData));
                Assert.That(result.Registry, Is.Null);
                RegistryLoadVectors.AssertDiagnostic(result.Diagnostics.Single(), "SPDX_REGISTRY_TRAILING_DATA",
                    SpdxRegistryInput.Licenses, LicenseLength, null, null, null);
                Assert.That(shared.Position, Is.EqualTo(PairLength));
                Assert.That(pair.Exceptions.Reads, Is.Empty);
                pair.AssertCanReadOrder("licenses", "exceptions");
            }
        }
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void ExceptionCanReadIoFailurePrecedesAllInputWork(bool malformedLicenses, bool preCancelled) {
            using(var pair = Inputs.Minimal(malformedLicenses)) {
                var sentinel = new IOException("Exceptions.CanRead failed.");
                pair.Exceptions.Readable = () => throw sentinel;
                var error = Assert.Throws<IOException>(() =>
                    SpdxLicenseRegistry.Load(pair.Licenses, pair.Exceptions, new() { MaxInputBytes = 0 }, new(preCancelled)));
                Assert.That(error, Is.SameAs(sentinel));
                pair.AssertCanReadOrder("licenses", "exceptions");
                pair.AssertNoReads();
            }
        }
        [TestCase(false)]
        [TestCase(true)]
        public void PreCancellationFollowsCanReadAndPrecedesReads(bool malformedLicenses) {
            using(var pair = Inputs.Minimal(malformedLicenses))
            using(var cancellation = new CancellationTokenSource()) {
                cancellation.Cancel();
                AssertCancelled(pair, new() { MaxInputBytes = 119, MaxTotalInputBytes = PairLength, MaxJsonDepth = 64 }, cancellation.Token);
                pair.AssertNoReads();
            }
        }
        [TestCase(false)]
        [TestCase(true)]
        public void CancellationFromFirstReadStopsAfterOneByte(bool cancelOnExceptions) {
            using(var pair = Inputs.Minimal(cap: 1))
            using(var cancellation = new CancellationTokenSource()) {
                var target = cancelOnExceptions ? pair.Exceptions : pair.Licenses;
                target.AfterRead = _ => cancellation.Cancel();
                AssertCancelled(pair, null, cancellation.Token);
                Assert.That(target.Cursor, Is.EqualTo(1));
                Assert.That(target.Reads.Single().Count, Is.EqualTo(1));
                AssertOtherInputAtExpectedStage(pair, cancelOnExceptions);
            }
        }
        [Test]
        public void CancellationOnLicenseBoundaryEofPrecedesExceptionAcquisition() {
            using(var pair = Inputs.Minimal())
            using(var cancellation = new CancellationTokenSource()) {
                CancelOnEof(pair.Licenses, cancellation);
                AssertCancelled(pair, new() { MaxInputBytes = LicenseLength }, cancellation.Token);
                Assert.That(pair.Licenses.Cursor, Is.EqualTo(LicenseLength));
                Assert.That(pair.Licenses.Reads[^1], Is.EqualTo((1, (int?)0)));
                Assert.That(pair.Exceptions.Reads, Is.Empty);
            }
        }
        [Test]
        public void CancellationOnMalformedLicenseEofPrecedesJsonDiagnostic() {
            using(var pair = Inputs.Minimal(malformedLicenses: true))
            using(var cancellation = new CancellationTokenSource()) {
                CancelOnEof(pair.Licenses, cancellation);
                AssertCancelled(pair, null, cancellation.Token);
                Assert.That(pair.Licenses.Reads.Select(read => read.Count), Is.EqualTo(new[] { 1, 0 }));
                Assert.That(pair.Exceptions.Reads, Is.Empty);
            }
        }
        [Test]
        public void CancellationOnExceptionBoundaryEofPrecedesParsing() {
            using(var pair = Inputs.Minimal())
            using(var cancellation = new CancellationTokenSource()) {
                CancelOnEof(pair.Exceptions, cancellation);
                AssertCancelled(pair, new() { MaxTotalInputBytes = PairLength }, cancellation.Token);
                Assert.That(pair.Exceptions.Cursor, Is.EqualTo(ExceptionLength));
                Assert.That(pair.Exceptions.Reads[^1], Is.EqualTo((1, (int?)0)));
                AssertOtherInputAtExpectedStage(pair, true);
            }
        }
        [TestCase(false, LicenseLength - 1, LicenseLength)]
        [TestCase(true, ExceptionLength - 1, ExceptionLength)]
        public void CancellationReturningExcessBytePrecedesLimit(bool cancelOnExceptions, int allowance, int cursor) {
            using(var pair = Inputs.Minimal(cap: allowance))
            using(var cancellation = new CancellationTokenSource()) {
                var target = cancelOnExceptions ? pair.Exceptions : pair.Licenses;
                target.AfterRead = _ => {
                    if(target.Cursor > allowance)
                        cancellation.Cancel();
                };
                var options = cancelOnExceptions ? new SpdxRegistryLoadOptions { MaxTotalInputBytes = PairLength - 1 }
                    : new SpdxRegistryLoadOptions { MaxInputBytes = LicenseLength - 1 };
                AssertCancelled(pair, options, cancellation.Token);
                Assert.That(target.Reads.Select(read => read.Count), Is.EqualTo(new[] { allowance, 1 }));
                Assert.That(target.Reads[^1], Is.EqualTo((1, (int?)1)));
                Assert.That(target.Cursor, Is.EqualTo(cursor));
                AssertOtherInputAtExpectedStage(pair, cancelOnExceptions);
            }
        }
        [TestCase(false)]
        [TestCase(true)]
        public void ReadThatCancelsAndThrowsPropagatesItsOriginalIoException(bool throwOnExceptions) {
            using(var pair = Inputs.Minimal())
            using(var cancellation = new CancellationTokenSource()) {
                var sentinel = new IOException("Read failed after signalling cancellation.");
                var target = throwOnExceptions ? pair.Exceptions : pair.Licenses;
                target.BeforeRead = () => {
                    cancellation.Cancel();
                    throw sentinel;
                };
                var error = Assert.Throws<IOException>(() => SpdxLicenseRegistry.Load(pair.Licenses, pair.Exceptions, cancellationToken: cancellation.Token));
                Assert.That(error, Is.SameAs(sentinel));
                Assert.That(cancellation.IsCancellationRequested, Is.True);
                Assert.That(target.Reads.Single().Count, Is.Null);
                Assert.That(target.Cursor, Is.Zero);
                pair.AssertCanReadOrder("licenses", "exceptions");
                AssertOtherInputAtExpectedStage(pair, throwOnExceptions);
            }
        }
        [TestCase(false)]
        [TestCase(true)]
        public void ReadPropagatesItsOriginalDisposedException(bool throwOnExceptions) {
            using(var pair = Inputs.Minimal()) {
                var sentinel = new ObjectDisposedException("Input disposed during Read.");
                var target = throwOnExceptions ? pair.Exceptions : pair.Licenses;
                target.BeforeRead = () => throw sentinel;
                Assert.That(Assert.Throws<ObjectDisposedException>(() => SpdxLicenseRegistry.Load(pair.Licenses, pair.Exceptions)), Is.SameAs(sentinel));
                Assert.That(target.Reads.Single().Count, Is.Null);
                Assert.That(target.Cursor, Is.Zero);
                pair.AssertCanReadOrder("licenses", "exceptions");
                AssertOtherInputAtExpectedStage(pair, throwOnExceptions);
            }
        }
        [TestCase(false, 1)]
        [TestCase(true, ExceptionLength - 1)]
        public void ReadFailureAtEofOrProbePrecedesDataResult(bool throwOnExceptions, int accepted) {
            using(var pair = Inputs.Minimal(malformedLicenses: !throwOnExceptions, cap: accepted)) {
                var target = throwOnExceptions ? pair.Exceptions : pair.Licenses;
                var sentinel = new IOException("Boundary Read failed.");
                target.BeforeRead = () => {
                    if(target.Cursor == accepted)
                        throw sentinel;
                };
                var options = new SpdxRegistryLoadOptions { MaxTotalInputBytes = PairLength - 1 };
                Assert.That(Assert.Throws<IOException>(() => SpdxLicenseRegistry.Load(pair.Licenses, pair.Exceptions, options)), Is.SameAs(sentinel));
                Assert.That(target.Reads.Select(read => read.Count), Is.EqualTo(new int?[] { accepted, null }));
                Assert.That(target.Cursor, Is.EqualTo(accepted));
                if(throwOnExceptions)
                    Assert.That(target.Reads[^1].Request, Is.EqualTo(1));
                pair.AssertCanReadOrder("licenses", "exceptions");
                AssertOtherInputAtExpectedStage(pair, throwOnExceptions);
            }
        }
        [Test]
        public void RegistryRemainsUsableAfterCallerDisposalAndMutation() {
            var (licenses, exceptions, _) = RegistryLoadVectors.Inputs(RegistryLoadVectors.Find("SLICE-PAYLOAD"));
            SpdxRegistryLoadResult result;
            using(var pair = new Inputs(licenses, exceptions, 2)) {
                result = SpdxLicenseRegistry.Load(pair.Licenses, pair.Exceptions);
            }
            licenses.AsSpan().Fill(0xFF);
            exceptions.AsSpan().Fill(0xFF);
            var registry = RegistryLoadVectors.AssertSuccess(result, "SLICE-PAYLOAD");
            var expression = GrammarTests.Parse("mit WITH LLVM-exception");
            for(int index = 0; index < 3; index++) {
                Assert.That(registry.TryGetLicense("mIt", out var license), Is.True);
                Assert.That(license!.Identifier, Is.EqualTo("MIT"));
                Assert.That(registry.TryGetException("llvm-exception", out var exception), Is.True);
                Assert.That(exception!.Identifier, Is.EqualTo("LLVM-exception"));
                var validation = SpdxExpressionValidator.Validate(expression, registry);
                Assert.That(validation.Diagnostics, Is.Empty);
                Assert.That(validation.RegistryIdentity, Is.SameAs(registry.Identity));
            }
        }
        static void AssertVector(JsonElement vector, params int[] caps) {
            var (licenses, exceptions, options) = RegistryLoadVectors.Inputs(vector);
            var expected = vector.GetProperty("expected");
            RegistryLoadVectors.AssertOutcome(SpdxLicenseRegistry.Load(licenses, exceptions, options), expected);
            using(var left = new MemoryStream(licenses))
            using(var right = new MemoryStream(exceptions)) {
                RegistryLoadVectors.AssertOutcome(SpdxLicenseRegistry.Load(left, right, options), expected);
                Assert.That(left.CanRead && right.CanRead, Is.True);
            }
            using(var pair = new Inputs(licenses, exceptions, caps)) {
                RegistryLoadVectors.AssertOutcome(SpdxLicenseRegistry.Load(pair.Licenses, pair.Exceptions, options), expected);
                pair.AssertCanReadOrder("licenses", "exceptions");
            }
        }
        static void AssertCancelled(Inputs pair, SpdxRegistryLoadOptions? options, CancellationToken token) {
            var error = Assert.Throws<OperationCanceledException>(() => SpdxLicenseRegistry.Load(pair.Licenses, pair.Exceptions, options, token));
            Assert.That(error!.CancellationToken, Is.EqualTo(token));
            pair.AssertCanReadOrder("licenses", "exceptions");
        }
        static void AssertOtherInputAtExpectedStage(Inputs pair, bool onExceptions) {
            if(onExceptions) {
                Assert.That(pair.Licenses.Cursor, Is.EqualTo(LicenseLength));
                Assert.That(pair.Licenses.Reads[^1].Count, Is.Zero);
            }
            else
                Assert.That(pair.Exceptions.Reads, Is.Empty);
        }
        static void CancelOnEof(ProbeStream stream, CancellationTokenSource cancellation) {
            stream.AfterRead = count => {
                if(count == 0)
                    cancellation.Cancel();
            };
        }
        sealed class Inputs : IDisposable {
            readonly List<string> getters = new();
            internal Inputs(byte[] licenses, byte[] exceptions, params int[] caps)
                : this(new MemoryStream(licenses), new MemoryStream(exceptions), caps) {
            }
            internal Inputs(Stream licenses, Stream exceptions, params int[] caps) {
                if(caps.Length == 0)
                    caps = [16_384];
                Licenses = new ProbeStream(licenses, getters, "licenses", caps);
                Exceptions = new ProbeStream(exceptions, getters, "exceptions", caps);
            }
            internal ProbeStream Licenses { get; }
            internal ProbeStream Exceptions { get; }
            internal static Inputs Minimal(bool malformedLicenses = false, int cap = 16_384) {
                var (licenses, exceptions, _) = RegistryLoadVectors.Inputs(RegistryLoadVectors.Find("MINIMAL"));
                return new Inputs(malformedLicenses ? "{"u8.ToArray() : licenses, exceptions, cap);
            }
            internal void AssertCanReadOrder(params string[] order) {
                Assert.That(getters, Is.EqualTo(order));
            }
            internal void AssertNoReads() {
                Assert.That(Licenses.Reads, Is.Empty);
                Assert.That(Exceptions.Reads, Is.Empty);
            }
            public void Dispose() {
                try {
                    Assert.That(Licenses.DisposeCalls, Is.Zero, "Loader disposed the licenses stream.");
                    Assert.That(Exceptions.DisposeCalls, Is.Zero, "Loader disposed the exceptions stream.");
                }
                finally {
                    Licenses.Dispose();
                    Exceptions.Dispose();
                }
            }
        }
        sealed class ProbeStream(Stream source, List<string> getters, string input, int[] caps) : Stream {
            internal Func<bool>? Readable { get; set; }
            internal Action? BeforeRead { get; set; }
            internal Action<int>? AfterRead { get; set; }
            internal List<(int Request, int? Count)> Reads { get; } = new();
            internal int Cursor { get; private set; }
            internal int DisposeCalls { get; private set; }
            public override bool CanRead {
                get {
                    getters.Add(input);
                    return Readable?.Invoke() ?? true;
                }
            }
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException("Length was queried.");
            public override long Position {
                get { throw new NotSupportedException("Position was queried."); }
                set { throw new NotSupportedException("Position was set."); }
            }
            public override int Read(byte[] buffer, int offset, int count) {
                Assert.That(count, Is.InRange(1, 16_384));
                int cap = caps[Reads.Count % caps.Length];
                Reads.Add((count, null));
                BeforeRead?.Invoke();
                int read = source.Read(buffer, offset, Math.Min(cap, count));
                Cursor += read;
                Reads[^1] = (count, read);
                AfterRead?.Invoke(read);
                return read;
            }
            public override long Seek(long offset, SeekOrigin origin) {
                throw new NotSupportedException("Seek was called.");
            }
            public override void Flush() {
                throw new NotSupportedException();
            }
            public override void SetLength(long value) {
                throw new NotSupportedException();
            }
            public override void Write(byte[] buffer, int offset, int count) {
                throw new NotSupportedException();
            }
            protected override void Dispose(bool disposing) {
                DisposeCalls++;
                if(disposing)
                    source.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
