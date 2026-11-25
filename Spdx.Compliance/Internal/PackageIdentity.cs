using System;
using NuGet.Versioning;

namespace Spdx.Compliance {
    static class PackageIdentity {
        const string Prefix = "pkg:nuget/";
        internal static bool IsVersionedNuGetPurl(string? purl) {
            return TryRead(purl, out _, out _);
        }
        internal static bool Matches(string? purl, string? name, string? rawVersion) {
            return TryRead(purl, out var id, out var version) &&
                id.Equals(name, StringComparison.OrdinalIgnoreCase) && TryVersion(rawVersion.AsSpan(), out var expected) &&
                VersionComparer.VersionReleaseMetadata.Equals(version, expected);
        }
        internal static bool IsPackageId(ReadOnlySpan<char> id) {
            return !id.IsEmpty && id.IndexOfAnyExcept("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._-") < 0;
        }
        static bool TryRead(string? purl, out ReadOnlySpan<char> id, out NuGetVersion? version) {
            id = default;
            version = null;
            if(purl is null || !purl.StartsWith(Prefix, StringComparison.Ordinal))
                return false;
            var coordinate = purl.AsSpan(Prefix.Length);
            int at = coordinate.IndexOf('@');
            if(at < 1 || !IsPackageId(coordinate[..at]))
                return false;
            id = coordinate[..at];
            return TryVersion(coordinate[(at + 1)..], out version);
        }
        static bool TryVersion(ReadOnlySpan<char> text, out NuGetVersion? version) {
            version = null;
            return !text.IsEmpty && text.IndexOfAnyExcept("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.+-") < 0 &&
                NuGetVersion.TryParse(text.ToString(), out version);
        }
    }
}
