using System;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace Spdx.Compliance {
    /// <summary>Reads CycloneDX JSON 1.6 and evaluates license declarations at component occurrences.</summary>
    public static class CycloneDxComplianceAnalyzer {
        const string ByteLimitMessage = "The CycloneDX input exceeds the configured byte limit.";
        const string DepthLimitMessage = "The CycloneDX input exceeds the configured JSON depth limit.";
        const string FormatMessage = "Expected a CycloneDX JSON 1.6 document.";
        /// <summary>Analyzes a stream from its current position and leaves it open. Per-occurrence context
        /// replaces the default context when the supplied callback returns a value for that occurrence.
        /// OR selection sets belong in the per-occurrence context.</summary>
        /// <exception cref="ArgumentException">The shared context contains an OR selection set.</exception>
        /// <exception cref="IOException">The input stream could not be read.</exception>
        /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
        public static CycloneDxReport Analyze(Stream document, CompliancePolicy policy, ComplianceContext? context = null,
            ComplianceLimits? limits = null, Func<ComponentOccurrence, ComplianceContext?>? contextForComponent = null,
            CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(policy);
            if(context?.OrSelections is not null)
                throw new ArgumentException("Supply OR selection sets through the per-component context callback.", nameof(context));
            limits ??= new();
            limits.Validate();
            JsonDocument json;
            try {
                var bytes = Input.Read(document, limits.MaxInputBytes, cancellationToken);
                json = Input.Parse(bytes, limits.MaxJsonDepth, cancellationToken);
            }
            catch(InputLimitException) {
                return Failure(policy, CycloneDxIssueCode.InputLimit, ByteLimitMessage);
            }
            catch(InputDepthException) {
                return Failure(policy, CycloneDxIssueCode.DepthLimit, DepthLimitMessage);
            }
            catch(JsonException exception) {
                return Failure(policy, CycloneDxIssueCode.InvalidJson, exception.Message);
            }
            using(json) {
                var root = json.RootElement;
                if(root.Text("bomFormat") != "CycloneDX" || root.Text("specVersion") != "1.6")
                    return Failure(policy, CycloneDxIssueCode.UnsupportedDocument, FormatMessage);
                var traversal = new ComponentTraversal(policy, context ?? new(), limits.MaxFindings, contextForComponent, cancellationToken);
                return traversal.Run(root);
            }
        }
        static CycloneDxReport Failure(CompliancePolicy policy, CycloneDxIssueCode code, string message) {
            return new(policy.Identity, [], [new(code, "", message)], false, 0, 0);
        }
    }
    sealed class ComponentTraversal(CompliancePolicy policy, ComplianceContext fallback, int maximum,
        Func<ComponentOccurrence, ComplianceContext?>? contextForComponent, CancellationToken token) {
        const string ExpectedObject = "Expected a JSON object at this location.";
        const string ExpectedTools = "Expected an object containing tool components or a legacy array of tool records.";
        const string ExpectedArray = "Expected an array of component objects.";
        const string FindingLimit = "The report exceeds its configured component or issue limit.";
        readonly ImmutableArray<ComponentCompliance>.Builder findings = ImmutableArray.CreateBuilder<ComponentCompliance>();
        readonly ImmutableArray<CycloneDxIssue>.Builder issues = ImmutableArray.CreateBuilder<CycloneDxIssue>();
        bool complete = true;
        bool stopped;
        int tools;
        internal CycloneDxReport Run(JsonElement root) {
            var metadata = root.Get("metadata");
            if(metadata.ValueKind == JsonValueKind.Object) {
                var component = metadata.Get("component");
                if(component.ValueKind != JsonValueKind.Undefined)
                    Visit(component, "/metadata/component", ComponentRole.Root);
            }
            else if(metadata.ValueKind != JsonValueKind.Undefined)
                Invalid("/metadata", ExpectedObject);
            VisitArray(root.Get("components"), "/components", ComponentRole.Component);
            var toolNodes = metadata.Get("tools");
            if(toolNodes.ValueKind == JsonValueKind.Object)
                VisitArray(toolNodes.Get("components"), "/metadata/tools/components", ComponentRole.Tool);
            else if(toolNodes.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Array))
                Invalid("/metadata/tools", ExpectedTools);
            token.ThrowIfCancellationRequested();
            return new(policy.Identity, findings.ToImmutable(), issues.ToImmutable(), complete, findings.Count - tools, tools);
        }
        void VisitArray(JsonElement collection, string pointer, ComponentRole role) {
            if(stopped || collection.ValueKind == JsonValueKind.Undefined)
                return;
            if(collection.ValueKind != JsonValueKind.Array) {
                Invalid(pointer, ExpectedArray);
                return;
            }
            int index = 0;
            foreach(var node in collection.EnumerateArray()) {
                if(stopped)
                    return;
                string childPointer = pointer + "/" + index.ToString(CultureInfo.InvariantCulture);
                Visit(node, childPointer, role);
                index++;
            }
        }
        void Visit(JsonElement node, string pointer, ComponentRole role) {
            token.ThrowIfCancellationRequested();
            if(node.ValueKind != JsonValueKind.Object) {
                Invalid(pointer, ExpectedObject);
                return;
            }
            if(findings.Count == maximum) {
                Stop(pointer);
                return;
            }
            var occurrence = new ComponentOccurrence(pointer, role, node.Text("bom-ref"), node.Text("name"),
                node.Text("version"), node.Text("type"), node.Text("purl"));
            var context = contextForComponent?.Invoke(occurrence) ?? fallback;
            token.ThrowIfCancellationRequested();
            var result = ComponentLicenses.Evaluate(node, occurrence, policy, context, token);
            findings.Add(result);
            complete &= result.Evaluation.IsComplete;
            if(role == ComponentRole.Tool)
                tools++;
            var childRole = role == ComponentRole.Tool ? role : ComponentRole.Nested;
            VisitArray(node.Get("components"), pointer + "/components", childRole);
        }
        void Invalid(string pointer, string message) {
            if(stopped)
                return;
            if(issues.Count == maximum) {
                Stop(pointer);
                return;
            }
            complete = false;
            issues.Add(new(CycloneDxIssueCode.InvalidStructure, pointer, message));
        }
        void Stop(string pointer) {
            stopped = true;
            complete = false;
            issues.Add(new(CycloneDxIssueCode.FindingLimit, pointer, FindingLimit));
        }
    }
}
