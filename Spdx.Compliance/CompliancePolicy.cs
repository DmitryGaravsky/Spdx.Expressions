using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Spdx.Expressions;

namespace Spdx.Compliance {
    /// <summary>A policy load failure with a stable category and an actionable message.</summary>
    public sealed class CompliancePolicyException : FormatException {
        internal CompliancePolicyException(CompliancePolicyError error, string message, Exception? inner = null)
            : base(message, inner) {
            Error = error;
        }
        /// <summary>Gets the category of the failure.</summary>
        public CompliancePolicyError Error { get; }
    }
    /// <summary>Categories of policy and catalog load failures.</summary>
    public enum CompliancePolicyError {
        /// <summary>The policy JSON or rules are invalid.</summary>
        InvalidPolicy,
        /// <summary>The catalog input and policy settings are inconsistent.</summary>
        CatalogRequired,
        /// <summary>The acquired catalog bytes do not match the configured pin.</summary>
        CatalogPinMismatch,
        /// <summary>The pinned catalog has invalid JSON or rules.</summary>
        InvalidCatalog,
        /// <summary>A stream exceeded the configured byte budget.</summary>
        InputLimit
    }
    /// <summary>An immutable distribution policy, optionally bound to a pinned correction catalog.</summary>
    public sealed class CompliancePolicy {
        const int SupportedSchema = 1;
        const int MaxSupportedExpressionCharacters = 65_536;
        static readonly JsonSerializerOptions JsonOptions = new() {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        CompliancePolicy(CompliancePolicyIdentity identity, PolicyRules rules, CorrectionProperties? markers,
            CorrectionCatalog? catalog, int maxCharacters) {
            Identity = identity;
            Rules = rules;
            Markers = markers;
            Catalog = catalog;
            ParseOptions = new() { MaxInputLength = maxCharacters };
        }
        /// <summary>Gets the identity of the exact policy and optional catalog bytes.</summary>
        public CompliancePolicyIdentity Identity { get; }
        internal PolicyRules Rules { get; }
        internal CorrectionProperties? Markers { get; }
        internal CorrectionCatalog? Catalog { get; }
        internal SpdxParseOptions ParseOptions { get; }
        /// <summary>Loads schema version 1 from UTF-8 JSON and verifies any pinned catalog before parsing its rules.
        /// Both streams start at their current positions, remain open and may be non-seekable.</summary>
        /// <exception cref="CompliancePolicyException">Invalid configuration or catalog, mismatched pin or exceeded byte limit.</exception>
        /// <exception cref="IOException">A supplied stream could not be read.</exception>
        /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
        public static CompliancePolicy Load(Stream policy, Stream? correctionCatalog = null, ComplianceLimits? limits = null,
            CancellationToken cancellationToken = default) {
            limits ??= new();
            limits.Validate();
            var bytes = Acquire(policy, limits, "policy", cancellationToken);
            PolicyInput data;
            try {
                using(var json = Input.Parse(bytes, limits.MaxJsonDepth, cancellationToken)) {
                    data = json.RootElement.Deserialize<PolicyInput>(JsonOptions) ??
                        throw Invalid("The policy root must be an object.");
                }
            }
            catch(InputDepthException exception) {
                const string message = "The policy exceeds the configured JSON depth limit.";
                throw new CompliancePolicyException(CompliancePolicyError.InputLimit, message, exception);
            }
            catch(JsonException exception) {
                const string message = "Invalid policy JSON.";
                throw new CompliancePolicyException(CompliancePolicyError.InvalidPolicy, $"{message} {exception.Message}", exception);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if(data.SchemaVersion != SupportedSchema)
                throw Invalid($"schemaVersion must be {SupportedSchema}.");
            string id = RequiredText(data.Id, "id");
            int maxCharacters = data.MaxExpressionCharacters ?? MaxSupportedExpressionCharacters;
            if(maxCharacters is < 1 or > MaxSupportedExpressionCharacters)
                throw Invalid($"maxExpressionCharacters must be between 1 and {MaxSupportedExpressionCharacters}.");
            if((data.CorrectionCatalog is null) != (correctionCatalog is null))
                throw new CompliancePolicyException(CompliancePolicyError.CatalogRequired,
                    "Supply a catalog stream exactly when the policy declares correctionCatalog.");
            var markers = ReadMarkers(data.CorrectionProperties);
            var rules = new PolicyRules(data, markers, cancellationToken);
            CorrectionCatalog? catalog = null;
            if(correctionCatalog is not null && data.CorrectionCatalog is { } pin) {
                if(markers is null)
                    throw Invalid("correctionCatalog requires correctionProperties.");
                if(!Input.IsDigest(pin.Sha256))
                    throw Invalid("correctionCatalog.sha256 must contain 64 hexadecimal digits.");
                string source = RequiredText(pin.Source, "correctionCatalog.source");
                var catalogBytes = Acquire(correctionCatalog, limits, "correction catalog", cancellationToken);
                string digest = Input.Digest(catalogBytes.Span, cancellationToken);
                if(!string.Equals(digest, pin.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new CompliancePolicyException(CompliancePolicyError.CatalogPinMismatch,
                        $"Correction catalog SHA-256 mismatch. Expected {pin.Sha256}, got {digest}.");
                catalog = CorrectionCatalog.Load(catalogBytes, limits.MaxJsonDepth, source, digest, cancellationToken);
            }
            string policyDigest = Input.Digest(bytes.Span, cancellationToken);
            var identity = new CompliancePolicyIdentity(id, SupportedSchema, policyDigest, catalog?.Digest);
            return new(identity, rules, markers, catalog, maxCharacters);
        }
        static ReadOnlyMemory<byte> Acquire(Stream stream, ComplianceLimits limits, string input, CancellationToken token) {
            try {
                return Input.Read(stream, limits.MaxInputBytes, token);
            }
            catch(InputLimitException exception) {
                const string message = "Input exceeds the configured byte limit.";
                throw new CompliancePolicyException(CompliancePolicyError.InputLimit, $"{message} Input: {input}.", exception);
            }
        }
        static CorrectionProperties? ReadMarkers(CorrectionProperties? properties) {
            if(properties is null)
                return null;
            string id = RequiredText(properties.Id, "correctionProperties.id");
            string source = RequiredText(properties.Source, "correctionProperties.source");
            if(id == source)
                throw Invalid("correctionProperties.id and .source must be distinct.");
            return properties;
        }
        internal static string RequiredText(string? text, string field) {
            if(string.IsNullOrWhiteSpace(text) || text != text.Trim())
                throw Invalid($"{field} must be a nonempty string without outer whitespace.");
            return text;
        }
        internal static CompliancePolicyException Invalid(string message) {
            return new(CompliancePolicyError.InvalidPolicy, message);
        }
    }
    sealed record PolicyInput(int SchemaVersion, string? Id, string?[]? Allow, string?[]? Deny,
        string?[]? AllowExactPairs, string?[]? DenyExactPairs, NoticeRule? Notice, int? MaxExpressionCharacters,
        ReferenceApproval?[]? ReferenceApprovals, Dictionary<string, string?[]?>? Profiles, NamedRule?[]? NamedLicenses,
        CorrectionProperties? CorrectionProperties, CatalogPin? CorrectionCatalog, PendingCorrection?[]? PendingCorrections);
    sealed record NoticeRule(string License, string File, string Sha256);
    sealed record ReferenceApproval(string Document, string Id, string Sha256);
    sealed record NamedRule(string?[]? Names, string? Owner, string? License);
    sealed record CorrectionProperties(string Id, string Source);
    sealed record CatalogPin(string Sha256, string Source);
    sealed record PendingCorrection(string Package, string License, string Id);
    sealed class PolicyRules {
        readonly FrozenDictionary<string, ComplianceDecision> licenses;
        readonly FrozenDictionary<(string License, string Exception), ComplianceDecision> pairs;
        readonly FrozenSet<(string Document, string Id, string Sha256)> approvals;
        internal PolicyRules(PolicyInput policy, CorrectionProperties? markers, CancellationToken token) {
            var licenseRules = new Dictionary<string, ComplianceDecision>(StringComparer.Ordinal);
            AddLicenses(licenseRules, policy.Allow, ComplianceDecision.Allow, "allow", token);
            AddLicenses(licenseRules, policy.Deny, ComplianceDecision.Deny, "deny", token);
            if(policy.Notice is { } notice) {
                string id = License(notice.License, "notice.license", token);
                if(licenseRules.ContainsKey(id) || id.EndsWith('+') || !Input.IsRelativeFile(notice.File) || !Input.IsDigest(notice.Sha256) ||
                    !SpdxLicenseRegistry.Default.TryGetLicense(id, out var entry) || entry.IsDeprecated)
                    throw CompliancePolicy.Invalid("notice requires an unlisted, nondeprecated license, a relative file and a SHA-256 digest.");
                Notice = new(id, notice.File, notice.Sha256.ToLowerInvariant());
            }
            licenses = licenseRules.ToFrozenDictionary(StringComparer.Ordinal);
            var pairRules = new Dictionary<(string, string), ComplianceDecision>();
            AddPairs(pairRules, policy.AllowExactPairs, ComplianceDecision.Allow, "allowExactPairs", token);
            AddPairs(pairRules, policy.DenyExactPairs, ComplianceDecision.Deny, "denyExactPairs", token);
            pairs = pairRules.ToFrozenDictionary();
            approvals = ReadApprovals(policy.ReferenceApprovals, token);
            Profiles = ReadProfiles(policy.Profiles, token);
            Names = ReadNames(policy.NamedLicenses, token);
            Pending = ReadPending(policy.PendingCorrections, markers, token);
        }
        internal NoticeRule? Notice { get; }
        internal FrozenDictionary<string, FrozenSet<string>> Profiles { get; }
        internal FrozenDictionary<string, NamedRule> Names { get; }
        internal FrozenDictionary<(string Package, string License, string Id), PendingCorrection> Pending { get; }
        internal bool TryLicense(string license, out ComplianceDecision decision) {
            return licenses.TryGetValue(license, out decision);
        }
        internal bool TryPair(string license, string exception, out ComplianceDecision decision) {
            return pairs.TryGetValue((license, exception), out decision);
        }
        internal bool Approves(string document, string id, string digest) {
            return approvals.Contains((document, id, digest));
        }
        static FrozenDictionary<string, FrozenSet<string>> ReadProfiles(Dictionary<string, string?[]?>? input, CancellationToken token) {
            var profiles = new Dictionary<string, FrozenSet<string>>(StringComparer.Ordinal) {
                ["default"] = FrozenSet<string>.Empty
            };
            if(input is not null) {
                foreach(var (name, items) in input) {
                    CompliancePolicy.RequiredText(name, "profiles key");
                    if(items is null)
                        throw CompliancePolicy.Invalid($"profiles.{name} must be an array.");
                    var banned = new Dictionary<string, ComplianceDecision>(StringComparer.Ordinal);
                    AddLicenses(banned, items, ComplianceDecision.Deny, $"profiles.{name}", token);
                    if(banned.Count == 0 && name != "default")
                        throw CompliancePolicy.Invalid($"profiles.{name} must contain a prohibited license.");
                    profiles[name] = banned.Keys.ToFrozenSet(StringComparer.Ordinal);
                }
            }
            return profiles.ToFrozenDictionary(StringComparer.Ordinal);
        }
        static FrozenSet<(string, string, string)> ReadApprovals(ReferenceApproval?[]? input, CancellationToken token) {
            var approvals = new HashSet<(string, string, string)>();
            foreach(var approval in input ?? []) {
                token.ThrowIfCancellationRequested();
                if(approval is null)
                    throw CompliancePolicy.Invalid("referenceApprovals contains a null entry.");
                string document = CompliancePolicy.RequiredText(approval.Document, "referenceApprovals.document");
                string id = CompliancePolicy.RequiredText(approval.Id, "referenceApprovals.id");
                if(SpdxExpressionParser.Parse(id, cancellationToken: token).Expression?.Root is not SpdxReferenceExpression { DocumentId: null } ||
                    !Input.IsDigest(approval.Sha256) || !approvals.Add((document, id, approval.Sha256.ToLowerInvariant())))
                    throw CompliancePolicy.Invalid($"Invalid or duplicate reference approval for {document}/{id}.");
            }
            return approvals.ToFrozenSet();
        }
        static FrozenDictionary<string, NamedRule> ReadNames(NamedRule?[]? input, CancellationToken token) {
            var names = new Dictionary<string, NamedRule>(StringComparer.Ordinal);
            foreach(var rule in input ?? []) {
                token.ThrowIfCancellationRequested();
                if(rule?.Names is not { Length: > 0 } || (rule.Owner is not null && rule.License is not null))
                    throw CompliancePolicy.Invalid("Each namedLicenses rule requires names and at most one of owner or license.");
                string? owner = rule.Owner is null ? null : CompliancePolicy.RequiredText(rule.Owner, "namedLicenses.owner");
                string? license = rule.License is null ? null : License(rule.License, "namedLicenses.license", token);
                var normalized = rule with { Owner = owner, License = license };
                foreach(string? item in rule.Names) {
                    token.ThrowIfCancellationRequested();
                    string name = CompliancePolicy.RequiredText(item, "namedLicenses.names");
                    if(!names.TryAdd(name, normalized))
                        throw CompliancePolicy.Invalid($"Duplicate named license alias '{name}'.");
                }
            }
            return names.ToFrozenDictionary(StringComparer.Ordinal);
        }
        static FrozenDictionary<(string, string, string), PendingCorrection> ReadPending(
            PendingCorrection?[]? input, CorrectionProperties? markers, CancellationToken token) {
            var pending = new Dictionary<(string, string, string), PendingCorrection>();
            foreach(var rule in input ?? []) {
                token.ThrowIfCancellationRequested();
                if(rule is null || markers is null || !PackageIdentity.IsVersionedNuGetPurl(rule.Package))
                    throw CompliancePolicy.Invalid("pendingCorrections requires correctionProperties and a versioned NuGet package.");
                string id = CompliancePolicy.RequiredText(rule.Id, "pendingCorrections.id");
                string license = License(rule.License, "pendingCorrections.license", token);
                if(!pending.TryAdd((rule.Package, license, id), rule))
                    throw CompliancePolicy.Invalid($"Duplicate pending correction for {rule.Package}/{id}.");
            }
            return pending.ToFrozenDictionary();
        }
        static void AddLicenses(Dictionary<string, ComplianceDecision> target, string?[]? terms,
            ComplianceDecision decision, string field, CancellationToken token) {
            foreach(string? term in terms ?? []) {
                string id = License(term, field, token);
                if(!target.TryAdd(id, decision))
                    throw CompliancePolicy.Invalid($"Duplicate or conflicting rule for '{id}' in {field}.");
            }
        }
        static string License(string? term, string field, CancellationToken token) {
            string text = CompliancePolicy.RequiredText(term, field);
            var parsed = SpdxExpressionParser.Parse(text, cancellationToken: token);
            if(parsed.Expression?.Root is not SpdxLicenseExpression license ||
                !SpdxLicenseRegistry.Default.TryGetLicense(license.Identifier, out var entry))
                throw CompliancePolicy.Invalid($"{field} requires a known simple SPDX license, got '{text}'.");
            return license.OrLater ? entry.Identifier + "+" : entry.Identifier;
        }
        static void AddPairs(Dictionary<(string, string), ComplianceDecision> target, string?[]? entries,
            ComplianceDecision decision, string field, CancellationToken token) {
            foreach(string? item in entries ?? []) {
                string text = CompliancePolicy.RequiredText(item, field);
                var parsed = SpdxExpressionParser.Parse(text, cancellationToken: token);
                if(parsed.Expression?.Root is not SpdxWithExpression { Operand: SpdxLicenseExpression license } pair ||
                    !SpdxLicenseRegistry.Default.TryGetLicense(license.Identifier, out var entry) ||
                    !SpdxLicenseRegistry.Default.TryGetException(pair.ExceptionIdentifier, out var exception))
                    throw CompliancePolicy.Invalid($"{field} requires a known SPDX license and exception pair, got '{text}'.");
                string id = license.OrLater ? entry.Identifier + "+" : entry.Identifier;
                if(!target.TryAdd((id, exception.Identifier), decision))
                    throw CompliancePolicy.Invalid($"Duplicate or conflicting WITH rule '{text}' in {field}.");
            }
        }
    }
}
