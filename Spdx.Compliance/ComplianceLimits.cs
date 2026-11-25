using System;

namespace Spdx.Compliance {
    /// <summary>Inclusive resource ceilings for policy, catalog and CycloneDX loading.</summary>
    public sealed class ComplianceLimits {
        /// <summary>Maximum bytes acquired from each input stream.</summary>
        public int MaxInputBytes { get; init; } = 16 * 1024 * 1024;
        /// <summary>Maximum JSON depth, between 1 and 128.</summary>
        public int MaxJsonDepth { get; init; } = 64;
        /// <summary>Maximum stored component occurrences per report.</summary>
        public int MaxFindings { get; init; } = 10_000;
        internal void Validate() {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxInputBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxJsonDepth);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxJsonDepth, 128);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxFindings);
        }
    }
}
