using System;

namespace Spdx.Expressions {
    /// <summary>Inclusive resource limits for loading a pair of SPDX catalogue documents.</summary>
    /// <remarks>Each limit must be positive. Pair budgets are shared by both inputs. Each limit is independent.</remarks>
    public sealed class SpdxRegistryLoadOptions {
        const int MaximumJsonDepth = 64;
        /// <summary>Gets the maximum raw byte count per input, including BOM and whitespace. Defaults to 16,777,216.</summary>
        public int MaxInputBytes { get; init; } = 16_777_216;
        /// <summary>Gets the maximum combined raw byte count across both inputs. Defaults to 33,554,432.</summary>
        public int MaxTotalInputBytes { get; init; } = 33_554_432;
        /// <summary>Gets the maximum open JSON container depth, from 1 through 64. The root has depth one. Defaults to 16.</summary>
        public int MaxJsonDepth { get; init; } = 16;
        /// <summary>Gets the maximum direct element count in the required licenses array. Defaults to 10,000.</summary>
        public int MaxLicenses { get; init; } = 10_000;
        /// <summary>Gets the maximum direct element count in the required exceptions array. Defaults to 2,000.</summary>
        public int MaxExceptions { get; init; } = 2_000;
        /// <summary>Gets the maximum UTF-16 length of a decoded identifier, including a legacy trailing plus. Defaults to 256.</summary>
        public int MaxIdentifierLength { get; init; } = 256;
        /// <summary>Gets the maximum decoded UTF-16 length of each property name or string value. Defaults to 16,384.</summary>
        public int MaxStringLength { get; init; } = 16_384;
        /// <summary>Gets the maximum combined decoded UTF-16 length of all property names and string values in the pair. Defaults to 8,388,608.</summary>
        public int MaxTotalStringLength { get; init; } = 8_388_608;
        /// <summary>Gets the maximum number of property occurrences per JSON object. Defaults to 64.</summary>
        public int MaxObjectProperties { get; init; } = 64;
        /// <summary>Gets the maximum number of direct elements per JSON array. Defaults to 10,000.</summary>
        public int MaxArrayLength { get; init; } = 10_000;
        /// <summary>Gets the maximum combined JSON token count across both inputs. Defaults to 250,000.</summary>
        public int MaxTokens { get; init; } = 250_000;
        /// <summary>Gets the maximum raw byte length of a token, including string quotes and excluding separators. Defaults to 131,072.</summary>
        public int MaxTokenBytes { get; init; } = 131_072;
        internal void Check() {
            CheckPositive(MaxInputBytes, nameof(MaxInputBytes));
            CheckPositive(MaxTotalInputBytes, nameof(MaxTotalInputBytes));
            if(MaxJsonDepth is < 1 or > MaximumJsonDepth)
                throw new ArgumentOutOfRangeException(nameof(MaxJsonDepth));
            CheckPositive(MaxLicenses, nameof(MaxLicenses));
            CheckPositive(MaxExceptions, nameof(MaxExceptions));
            CheckPositive(MaxIdentifierLength, nameof(MaxIdentifierLength));
            CheckPositive(MaxStringLength, nameof(MaxStringLength));
            CheckPositive(MaxTotalStringLength, nameof(MaxTotalStringLength));
            CheckPositive(MaxObjectProperties, nameof(MaxObjectProperties));
            CheckPositive(MaxArrayLength, nameof(MaxArrayLength));
            CheckPositive(MaxTokens, nameof(MaxTokens));
            CheckPositive(MaxTokenBytes, nameof(MaxTokenBytes));
        }
        static void CheckPositive(int value, string name) {
            if(value <= 0)
                throw new ArgumentOutOfRangeException(name);
        }
    }
}
