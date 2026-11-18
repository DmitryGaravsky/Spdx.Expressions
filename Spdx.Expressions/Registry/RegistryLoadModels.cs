using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Spdx.Expressions {
    /// <summary>The outcome of loading a complete catalogue pair.</summary>
    public enum SpdxRegistryLoadStatus {
        /// <summary>Both inputs passed all checks and a registry is available.</summary>
        Success = 0,
        /// <summary>An input or the pair failed encoding, JSON or schema validation.</summary>
        InvalidData = 1,
        /// <summary>An input or the pair exceeded a configured resource limit.</summary>
        LimitExceeded = 2
    }
    /// <summary>The catalogue input to which a diagnostic refers.</summary>
    public enum SpdxRegistryInput {
        /// <summary>The licenses document.</summary>
        Licenses = 0,
        /// <summary>The exceptions document.</summary>
        Exceptions = 1
    }
    /// <summary>The first failure to load a catalogue pair, located in the original input bytes.</summary>
    public sealed class SpdxRegistryDiagnostic {
        internal SpdxRegistryDiagnostic(string code, SpdxRegistryInput input, long byteOffset,
            int? entryIndex, string? propertyName, string? limitName, string message) {
            Code = code;
            Input = input;
            ByteOffset = byteOffset;
            EntryIndex = entryIndex;
            PropertyName = propertyName;
            LimitName = limitName;
            Message = message;
        }
        /// <summary>Gets the stable machine-readable failure code.</summary>
        public string Code { get; }
        /// <summary>Gets the input responsible for the failure.</summary>
        public SpdxRegistryInput Input { get; }
        /// <summary>Gets the zero-based raw byte offset, including any BOM. The offset may indicate end of input.</summary>
        public long ByteOffset { get; }
        /// <summary>Gets the zero-based entry index in source order, when applicable.</summary>
        public int? EntryIndex { get; }
        /// <summary>Gets the relevant decoded or required property name, when available.</summary>
        public string? PropertyName { get; }
        /// <summary>Gets the option property name for a limit failure, otherwise null.</summary>
        public string? LimitName { get; }
        /// <summary>Gets the human-readable explanation.</summary>
        public string Message { get; }
    }
    /// <summary>An atomic load outcome containing either a complete immutable registry or a diagnostic.</summary>
    public sealed class SpdxRegistryLoadResult {
        internal SpdxRegistryLoadResult(SpdxLicenseRegistry registry) {
            Status = SpdxRegistryLoadStatus.Success;
            Registry = registry;
            Diagnostics = ReadOnlyCollection<SpdxRegistryDiagnostic>.Empty;
        }
        internal SpdxRegistryLoadResult(SpdxRegistryDiagnostic diagnostic) {
            Status = diagnostic.LimitName is null ? SpdxRegistryLoadStatus.InvalidData : SpdxRegistryLoadStatus.LimitExceeded;
            Diagnostics = [diagnostic];
        }
        /// <summary>Gets the outcome of loading the pair.</summary>
        public SpdxRegistryLoadStatus Status { get; }
        /// <summary>Gets the complete catalogue on success, otherwise null.</summary>
        public SpdxLicenseRegistry? Registry { get; }
        /// <summary>Gets one primary diagnostic on failure or an empty list on success.</summary>
        public IReadOnlyList<SpdxRegistryDiagnostic> Diagnostics { get; }
    }
}
