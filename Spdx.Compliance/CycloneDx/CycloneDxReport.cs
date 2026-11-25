using System.Collections.Immutable;
using System.Text.Json;

namespace Spdx.Compliance {
    /// <summary>The location of a component occurrence in the analyzed document.</summary>
    public enum ComponentRole {
        /// <summary>The subject at /metadata/component.</summary>
        Root,
        /// <summary>An entry in the top-level components array.</summary>
        Component,
        /// <summary>A descendant of a product component.</summary>
        Nested,
        /// <summary>A metadata tool component or any descendant of one.</summary>
        Tool
    }
    /// <summary>The identity and JSON Pointer of one component occurrence.</summary>
    /// <param name="Pointer">Pointer in the input document.</param>
    /// <param name="Role">Structural role of the occurrence.</param>
    /// <param name="BomRef">Reported bom-ref, if textual.</param>
    /// <param name="Name">Reported name, if textual.</param>
    /// <param name="Version">Reported version, if textual.</param>
    /// <param name="Type">Reported component type, if textual.</param>
    /// <param name="Purl">Reported package URL, if textual.</param>
    public sealed record ComponentOccurrence(string Pointer, ComponentRole Role, string? BomRef, string? Name,
        string? Version, string? Type, string? Purl);
    /// <summary>The declaration, raw observations, policy decision and independent correction check for one occurrence.</summary>
    /// <param name="Component">Component identity and location.</param>
    /// <param name="Declaration">Original licenses value. Undefined represents absence.</param>
    /// <param name="ObservedLicenses">Original evidence.licenses value. Undefined represents absence.</param>
    /// <param name="Evaluation">Policy evaluation and action projection for this occurrence.</param>
    /// <param name="Correction">Independent correction check when configured markers were present.</param>
    public sealed record ComponentCompliance(ComponentOccurrence Component, JsonElement Declaration,
        JsonElement ObservedLicenses, ComplianceEvaluation Evaluation, CorrectionFinding? Correction);
    /// <summary>Machine-readable issues in the document projection or its resource budgets.</summary>
    public enum CycloneDxIssueCode {
        /// <summary>The JSON is invalid or contains duplicate properties.</summary>
        InvalidJson,
        /// <summary>The document does not identify itself as CycloneDX JSON 1.6.</summary>
        UnsupportedDocument,
        /// <summary>An expected object or array could not be traversed.</summary>
        InvalidStructure,
        /// <summary>The input exceeded the byte budget.</summary>
        InputLimit,
        /// <summary>The input exceeded the JSON depth budget.</summary>
        DepthLimit,
        /// <summary>The report exceeded its component or structural issue budget.</summary>
        FindingLimit
    }
    /// <summary>One projection or budget issue.</summary>
    /// <param name="Code">Stable issue code.</param>
    /// <param name="Pointer">Affected JSON Pointer; empty denotes the document.</param>
    /// <param name="Message">Human-readable details.</param>
    public sealed record CycloneDxIssue(CycloneDxIssueCode Code, string Pointer, string Message);
    /// <summary>The bounded per-occurrence evaluation of a CycloneDX 1.6 document.</summary>
    /// <param name="Policy">Exact policy identity.</param>
    /// <param name="Findings">Component occurrences in document projection order, including repeats and tools.</param>
    /// <param name="Issues">Structural and resource issues. Check completion before using counts.</param>
    /// <param name="IsComplete">Whether all occurrences could be evaluated within the supported structure and budgets.</param>
    /// <param name="ProductCount">Number of reported root, top-level and nested product occurrences.</param>
    /// <param name="ToolCount">Number of reported tool occurrences.</param>
    public sealed record CycloneDxReport(CompliancePolicyIdentity Policy, ImmutableArray<ComponentCompliance> Findings,
        ImmutableArray<CycloneDxIssue> Issues, bool IsComplete, int ProductCount, int ToolCount);
}
