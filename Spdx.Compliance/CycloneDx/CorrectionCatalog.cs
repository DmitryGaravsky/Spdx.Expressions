using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.Json;
using System.Threading;
using NuGet.Versioning;

namespace Spdx.Compliance {
    readonly record struct CorrectionClaim(int IdCount, string? Id, int SourceCount, string? Source) {
        internal bool IsMarked => IdCount != 0 || SourceCount != 0;
        internal static CorrectionClaim Read(JsonElement component, CorrectionProperties markers, CancellationToken token) {
            int ids = 0, sources = 0;
            string? id = null, source = null;
            var properties = component.Get("properties");
            if(properties.ValueKind == JsonValueKind.Array) {
                foreach(var property in properties.EnumerateArray()) {
                    token.ThrowIfCancellationRequested();
                    string? name = property.Text("name");
                    if(name == markers.Id) {
                        ids++;
                        id = property.Text("value");
                    }
                    else if(name == markers.Source) {
                        sources++;
                        source = property.Text("value");
                    }
                }
            }
            return new(ids, ids == 1 ? id : null, sources, sources == 1 ? source : null);
        }
    }
    sealed class CorrectionCatalog {
        const string NuGetKind = "nuget";
        readonly FrozenDictionary<string, string> artifactKinds;
        readonly FrozenDictionary<string, ImmutableArray<Candidate>> packages;
        readonly string source;
        CorrectionCatalog(Dictionary<string, string> artifactKinds, Dictionary<string, List<Candidate>> candidates,
            string source, string digest) {
            this.artifactKinds = artifactKinds.ToFrozenDictionary(StringComparer.Ordinal);
            packages = candidates.ToFrozenDictionary(item => item.Key, item => item.Value.ToImmutableArray(), StringComparer.OrdinalIgnoreCase);
            this.source = source;
            Digest = digest;
        }
        internal string Digest { get; }
        internal static CorrectionCatalog Load(ReadOnlyMemory<byte> bytes, int maxDepth, string source, string digest,
            CancellationToken token) {
            try {
                using(var json = Input.Parse(bytes, maxDepth, token)) {
                    var root = json.RootElement;
                    if(root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
                        throw Invalid("Expected a nonempty array of correction rules.");
                    var kinds = new Dictionary<string, string>(StringComparer.Ordinal);
                    var candidates = new Dictionary<string, List<Candidate>>(StringComparer.OrdinalIgnoreCase);
                    foreach(var rule in root.EnumerateArray()) {
                        token.ThrowIfCancellationRequested();
                        string? id = rule.Text("id"), kind = rule.Text("artifactKind");
                        if(string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(kind) || !kinds.TryAdd(id, kind))
                            throw Invalid("Each catalog entry requires a unique ID and artifact kind.");
                        if(kind == NuGetKind)
                            AddRule(rule, id, candidates, token);
                    }
                    token.ThrowIfCancellationRequested();
                    return new(kinds, candidates, source, digest);
                }
            }
            catch(InputDepthException exception) {
                const string message = "The correction catalog exceeds the configured JSON depth limit.";
                throw new CompliancePolicyException(CompliancePolicyError.InputLimit, message, exception);
            }
            catch(JsonException exception) {
                const string message = "Invalid correction catalog JSON.";
                throw new CompliancePolicyException(CompliancePolicyError.InvalidCatalog, $"{message} {exception.Message}", exception);
            }
        }
        static void AddRule(JsonElement entry, string id, Dictionary<string, List<Candidate>> candidates, CancellationToken token) {
            var license = entry.Get("license");
            string? licenseId = license.Text("id"), name = license.Text("name"), url = license.Text("url");
            var idKind = license.Get("id").ValueKind;
            var nameKind = license.Get("name").ValueKind;
            bool hasId = idKind != JsonValueKind.Undefined, hasName = nameKind != JsonValueKind.Undefined;
            if(hasId == hasName || string.IsNullOrWhiteSpace(hasId ? licenseId : name) ||
                string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out _))
                throw Invalid($"Correction '{id}' requires one license ID or name and an absolute URL.");
            var rule = new Rule(id, licenseId, name, url);
            var selectors = entry.Get("packageSelectors");
            if(selectors.ValueKind == JsonValueKind.Undefined) {
                AddSelector(entry);
                return;
            }
            if(selectors.ValueKind != JsonValueKind.Array || selectors.GetArrayLength() == 0 ||
                entry.Get("packageId").ValueKind != JsonValueKind.Undefined || entry.Get("versionRange").ValueKind != JsonValueKind.Undefined)
                throw Invalid($"Correction '{id}' requires either one package selector or a nonempty packageSelectors array.");
            foreach(var selector in selectors.EnumerateArray())
                AddSelector(selector);
            void AddSelector(JsonElement selector) {
                token.ThrowIfCancellationRequested();
                string? package = selector.Text("packageId");
                if(package is null || !PackageIdentity.IsPackageId(package) ||
                    selector.Text("versionRange") is not { } versionRange ||
                    !VersionRange.TryParse(versionRange, allowFloating: false, out var range) ||
                    range.Equals(VersionRange.None) || IsEmpty(range))
                    throw Invalid($"Correction '{id}' has an invalid package ID or NuGet version range.");
                if(!candidates.TryGetValue(package, out var list))
                    candidates.Add(package, list = []);
                bool exact = range.IsMinInclusive && range.IsMaxInclusive && range.MinVersion == range.MaxVersion;
                list.Add(new(rule, range, exact));
            }
        }
        static bool IsEmpty(VersionRange range) {
            if(range.MinVersion is not { } min || range.MaxVersion is not { } max)
                return false;
            int order = VersionComparer.VersionRelease.Compare(min, max);
            return order > 0 || (order == 0 && !(range.IsMinInclusive && range.IsMaxInclusive));
        }
        internal CorrectionFinding Check(JsonElement component, CorrectionClaim claim, CancellationToken token) {
            if(string.IsNullOrWhiteSpace(claim.Id) || claim.Source != source)
                return Result(CorrectionStatus.InvalidProvenance);
            string? name = component.Text("name"), rawVersion = component.Text("version");
            if(!PackageIdentity.Matches(component.Text("purl"), name, rawVersion) || !NuGetVersion.TryParse(rawVersion, out var version))
                return Result(CorrectionStatus.PackageIdentityMismatch);
            if(artifactKinds.TryGetValue(claim.Id, out var kind) && kind != NuGetKind)
                return Result(CorrectionStatus.WrongArtifactKind);
            var rule = Find(name!, version, token);
            if(rule is null || rule.Id != claim.Id)
                return Result(CorrectionStatus.SelectorMismatch);
            var entries = component.Get("licenses");
            if(entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() != 1 ||
                entries[0].Get("expression").ValueKind != JsonValueKind.Undefined)
                return Result(CorrectionStatus.LicenseMismatch);
            var license = entries[0].Get("license");
            if(license.Text("acknowledgement") != "concluded" || !Matches(license, "id", rule.LicenseId) ||
                !Matches(license, "name", rule.Name) || !Matches(license, "url", rule.Url))
                return Result(CorrectionStatus.LicenseMismatch);
            return Result(CorrectionStatus.Verified);
            CorrectionFinding Result(CorrectionStatus status) {
                return new(claim.Id, claim.Source, status, Digest);
            }
        }
        Rule? Find(string name, NuGetVersion version, CancellationToken token) {
            if(!packages.TryGetValue(name, out var candidates))
                return null;
            Rule? firstRange = null;
            foreach(var candidate in candidates) {
                token.ThrowIfCancellationRequested();
                if(!candidate.Range.Satisfies(version))
                    continue;
                // Exact pins outrank ranges; preserve catalog order within each selector class.
                if(candidate.Exact)
                    return candidate.Rule;
                firstRange ??= candidate.Rule;
            }
            return firstRange;
        }
        static bool Matches(JsonElement value, string key, string? expected) {
            return expected is null ? value.Get(key).ValueKind == JsonValueKind.Undefined : value.Text(key) == expected;
        }
        static CompliancePolicyException Invalid(string message) {
            return new(CompliancePolicyError.InvalidCatalog, message);
        }
        sealed record Rule(string Id, string? LicenseId, string? Name, string Url);
        readonly record struct Candidate(Rule Rule, VersionRange Range, bool Exact);
    }
}
