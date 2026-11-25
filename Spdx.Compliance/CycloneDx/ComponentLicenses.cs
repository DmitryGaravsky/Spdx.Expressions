using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Text.Json;
using System.Threading;
using Spdx.Expressions;
using static Spdx.Compliance.ComplianceDecision;
using static Spdx.Compliance.ComplianceReason;

namespace Spdx.Compliance {
    static class ComponentLicenses {
        internal static ComponentCompliance Evaluate(JsonElement component, ComponentOccurrence occurrence, CompliancePolicy policy,
            ComplianceContext context, CancellationToken token) {
            var declaration = component.Get("licenses");
            var observations = component.Get("evidence").Get("licenses");
            var term = ReadDeclaration(declaration);
            var claim = policy.Markers is { } markers ? CorrectionClaim.Read(component, markers, token) : default;
            CorrectionFinding? correction = null;
            if(claim.IsMarked) {
                correction = policy.Catalog is { } catalog ? catalog.Check(component, claim, token)
                    : new(claim.Id, claim.Source, CorrectionStatus.Unverified, null);
            }
            var result = EvaluateTerm(term, occurrence, policy, context, token);
            if(term.Text is not null && result.IsComplete && result.Finding.Reason != ProfileUnconfigured) {
                var conflicts = Conflicts(term, observations, policy, token, out bool complete);
                if(!conflicts.IsEmpty) {
                    var prior = result.Finding;
                    if(prior.Decision is Deny or Invalid)
                        result = result with { Finding = prior with { ConflictingObservations = conflicts } };
                    else
                        result = result with {
                            Finding = new(Review, SourceConflict) { Children = [prior], ConflictingObservations = conflicts }
                        };
                }
                if(!complete) {
                    result = result with { IsComplete = false };
                    if(result.Finding.Decision == Allow)
                        result = Wrap(result, Unknown, LimitExceeded);
                }
            }
            if(correction?.Status == CorrectionStatus.Unverified && result.Finding.Reason == NoPolicyRule &&
                claim is { IdCount: 1, SourceCount: 1, Id: { } correctionId } && !string.IsNullOrWhiteSpace(claim.Source) &&
                occurrence.Purl is { } purl && PackageIdentity.Matches(purl, occurrence.Name, occurrence.Version) &&
                SpdxLicenseRegistry.Default.TryGetLicense(term.Text!, out var license) &&
                policy.Rules.Pending.ContainsKey((purl, license.Identifier, correctionId))) {
                result = result with {
                    Finding = new(Conditional, CorrectionUnconfirmed) { Children = [result.Finding], PendingCorrectionId = correctionId }
                };
            }
            if(correction is not null && correction.Status is not (CorrectionStatus.Verified or CorrectionStatus.Unverified) &&
                result.Finding.Decision is not (Invalid or Deny))
                result = Wrap(result, Review, CorrectionMismatch);
            return new(occurrence, Clone(declaration), Clone(observations), result, correction);
        }
        static JsonElement Clone(JsonElement value) {
            return value.ValueKind == JsonValueKind.Undefined ? default : value.Clone();
        }
        static ComplianceEvaluation Wrap(ComplianceEvaluation result, ComplianceDecision decision, ComplianceReason reason) {
            return result with { Finding = new(decision, reason) { Children = [result.Finding] } };
        }
        static Term ReadDeclaration(JsonElement declaration) {
            if(declaration.ValueKind == JsonValueKind.Undefined)
                return new(FieldMissing, Unknown);
            if(declaration.ValueKind != JsonValueKind.Array)
                return new(WrongField, Invalid);
            return declaration.GetArrayLength() switch {
                0 => new(NoLicenseEntries, Unknown),
                1 => ReadTerm(declaration[0]),
                _ => new(MultipleEntries, Review)
            };
        }
        static Term ReadTerm(JsonElement entry) {
            var expression = entry.Get("expression");
            var license = entry.Get("license");
            if((expression.ValueKind == JsonValueKind.Undefined) == (license.ValueKind == JsonValueKind.Undefined))
                return new(WrongField, Invalid);
            if(expression.ValueKind == JsonValueKind.String)
                return new(expression.GetString()!, TermKind.Expression);
            if(license.ValueKind != JsonValueKind.Object)
                return new(WrongField, Invalid);
            var name = license.Get("name");
            var id = license.Get("id");
            if(name.ValueKind == JsonValueKind.String && id.ValueKind == JsonValueKind.Undefined)
                return new(name.GetString()!, TermKind.Name);
            if(id.ValueKind == JsonValueKind.String && name.ValueKind == JsonValueKind.Undefined)
                return new(id.GetString()!, TermKind.Id);
            return new(WrongField, Invalid);
        }
        static ComplianceEvaluation EvaluateTerm(Term term, ComponentOccurrence occurrence, CompliancePolicy policy,
            ComplianceContext context, CancellationToken token) {
            if(!ComplianceEngine.TryProfile(policy, context, out var prohibited))
                return ComplianceEngine.Result(policy, context, term.Text, ComplianceEngine.UnconfiguredProfile(context));
            if(term.Problem is { } reason) {
                var problem = new ComplianceFinding(term.Decision, reason);
                if(context.OrSelections is not null || (context.SelectedBranch is not null && term.Decision != Invalid))
                    problem = ComplianceEngine.RejectSelection(problem, context.SelectedBranch);
                return ComplianceEngine.Result(policy, context, null, problem);
            }
            if(term.Kind != TermKind.Name)
                return ComplianceEngine.EvaluateField(term.Text, policy, context, prohibited!, term.Kind == TermKind.Id, token);
            string name = term.Text!;
            if(name.Length > policy.ParseOptions.MaxInputLength) {
                var limit = new ComplianceFinding(Unknown, LimitExceeded);
                if(context.OrSelections is not null)
                    limit = ComplianceEngine.RejectSelection(limit, context.SelectedBranch);
                return ComplianceEngine.Result(policy, context, name, limit, complete: false);
            }
            var finding = EvaluateName(name, occurrence, policy, context, prohibited!);
            if(context.OrSelections is not null && finding.Reason == ProhibitedPresence) {
                var rejection = new ComplianceFinding(Invalid, SelectionMismatch) { Selection = context.SelectedBranch };
                finding = finding with { Children = [rejection] };
            }
            else if(context.OrSelections is not null || (context.SelectedBranch is not null && finding.Decision is not (Invalid or Deny)))
                finding = ComplianceEngine.RejectSelection(finding, context.SelectedBranch);
            return ComplianceEngine.Result(policy, context, name, finding);
        }
        static ComplianceFinding EvaluateName(string name, ComponentOccurrence occurrence, CompliancePolicy policy,
            ComplianceContext context, IReadOnlySet<string> prohibited) {
            if(string.IsNullOrWhiteSpace(name))
                return new(Invalid, FieldEmpty);
            if(IsFileReference(name))
                return new(Review, FileOnly);
            if(!policy.Rules.Names.TryGetValue(name, out var rule))
                return new(Review, NamedTerms);
            if(rule.License is { } license) {
                bool later = license.EndsWith('+');
                string id = later ? license[..^1] : license;
                var finding = prohibited.Contains(license) || prohibited.Contains(id) ? new ComplianceFinding(Deny, ProhibitedPresence)
                    : ComplianceEngine.License(policy, context, id, later);
                return finding with { AliasTarget = license };
            }
            if(rule.Owner is not { } expected)
                return new(Review, OwnershipUnconfirmed);
            string? owner = PackageIdentity.Matches(occurrence.Purl, occurrence.Name, occurrence.Version)
                ? context.Ownership?.Find(occurrence.Purl) : null;
            return new(owner == expected ? Allow : Review, owner == expected ? OwnershipConfirmed : OwnershipUnconfirmed) {
                Ownership = new(occurrence.Purl, name, expected, owner)
            };
        }
        static ImmutableArray<string> Conflicts(Term term, JsonElement observations, CompliancePolicy policy,
            CancellationToken token, out bool complete) {
            complete = true;
            if(observations.ValueKind != JsonValueKind.Array)
                return [];
            var conflicts = ImmutableArray.CreateBuilder<string>();
            string? canonical = null;
            bool originalCompared = false;
            policy.Rules.Names.TryGetValue(term.Text!, out var originalRule);
            string? expression = term.Kind != TermKind.Name ? term.Text : originalRule?.License;
            foreach(var entry in observations.EnumerateArray()) {
                token.ThrowIfCancellationRequested();
                var other = ReadTerm(entry);
                if(other.Text is not { } text || (other.Kind == TermKind.Name && IsFileReference(text)))
                    continue;
                if(text == term.Text && (other.Kind == term.Kind || (other.Kind != TermKind.Name && term.Kind != TermKind.Name)))
                    continue;
                if(other.Kind == TermKind.Name && term.Kind == TermKind.Name && originalRule is not null &&
                    policy.Rules.Names.TryGetValue(text, out var observedRule) && ReferenceEquals(originalRule, observedRule))
                    continue;
                if(other.Kind != TermKind.Name && expression is not null) {
                    var read = SpdxLicenseFieldReader.Read(text, policy.ParseOptions, token);
                    complete &= read.Kind != SpdxLicenseFieldKind.LimitExceeded;
                    if(!originalCompared) {
                        var original = SpdxLicenseFieldReader.Read(expression, policy.ParseOptions, token).Expression;
                        if(original is not null)
                            canonical = SpdxExpressionFormatter.FormatCanonicalCasing(original, SpdxLicenseRegistry.Default, token);
                        originalCompared = true;
                    }
                    if(canonical is not null && read.Expression is { } observed &&
                        canonical == SpdxExpressionFormatter.FormatCanonicalCasing(observed, SpdxLicenseRegistry.Default, token))
                        continue;
                }
                // Keep observations in the raw JSON even when one occurrence already proves a conflict.
                if(conflicts.Count == 0)
                    conflicts.Add(text);
            }
            return conflicts.ToImmutable();
        }
        static bool IsFileReference(string name) {
            if(name.StartsWith("SEE LICENSE IN ", StringComparison.OrdinalIgnoreCase))
                return true;
            var leaf = name.AsSpan()[(name.AsSpan().LastIndexOfAny('/', '\\') + 1)..];
            // A standalone file name identifies a location in the package without asserting license terms.
            var extension = Path.GetExtension(leaf);
            if(leaf.Length > extension.Length && leaf.IndexOfAny(' ', '\t') < 0 &&
                (extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) || extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
                 extension.Equals(".rtf", StringComparison.OrdinalIgnoreCase) || extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase)))
                return true;
            var stem = Path.GetFileNameWithoutExtension(leaf);
            return stem.Equals("LICENSE", StringComparison.OrdinalIgnoreCase) || stem.Equals("LICENCE", StringComparison.OrdinalIgnoreCase) ||
                stem.Equals("COPYING", StringComparison.OrdinalIgnoreCase) || stem.Equals("NOTICE", StringComparison.OrdinalIgnoreCase);
        }
        enum TermKind { Expression, Id, Name }
        readonly record struct Term(string? Text, TermKind Kind, ComplianceReason? Problem, ComplianceDecision Decision) {
            internal Term(string text, TermKind kind) : this(text, kind, null, Unknown) {
            }
            internal Term(ComplianceReason reason, ComplianceDecision decision) : this(null, default, reason, decision) {
            }
        }
    }
}
