namespace Spdx.Compliance {
    /// <summary>Independent verification of a declared NuGet correction.</summary>
    public enum CorrectionStatus {
        /// <summary>Markers were present under a policy without a pinned catalog.</summary>
        Unverified,
        /// <summary>Exactly one correction ID and one expected source were required.</summary>
        InvalidProvenance,
        /// <summary>The component name, version and NuGet PURL did not agree.</summary>
        PackageIdentityMismatch,
        /// <summary>The claim did not identify the winning NuGet correction.</summary>
        SelectorMismatch,
        /// <summary>The claim identified an entry for a different artifact kind.</summary>
        WrongArtifactKind,
        /// <summary>The component lacked the single matching concluded license.</summary>
        LicenseMismatch,
        /// <summary>The pinned catalog confirmed the correction.</summary>
        Verified
    }
    /// <summary>Correction evidence carried alongside the distribution decision.</summary>
    /// <param name="Id">Claimed correction ID when unique and textual.</param>
    /// <param name="Source">Claimed source label when unique and textual.</param>
    /// <param name="Status">Independent verification outcome.</param>
    /// <param name="CatalogSha256">Verified catalog digest, when available.</param>
    public sealed record CorrectionFinding(string? Id, string? Source, CorrectionStatus Status, string? CatalogSha256);
}
