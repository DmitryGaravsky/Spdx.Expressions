using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Spdx.Expressions;
using static Spdx.Compliance.ComplianceDecision;
using static Spdx.Compliance.ComplianceReason;

namespace Spdx.Compliance {
    /// <summary>Evaluates supplied license text against a loaded policy without performing I/O.</summary>
    public static class ComplianceEngine {
        /// <summary>Evaluates one original SPDX license field. Null represents a missing field.</summary>
        public static ComplianceEvaluation Evaluate(string? expression, CompliancePolicy policy, ComplianceContext? context = null,
            CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(policy);
            cancellationToken.ThrowIfCancellationRequested();
            context ??= new();
            if(!TryProfile(policy, context, out var prohibited))
                return Result(policy, context, expression, UnconfiguredProfile(context));
            return EvaluateField(expression, policy, context, prohibited!, false, cancellationToken);
        }
        internal static bool TryProfile(CompliancePolicy policy, ComplianceContext context, out IReadOnlySet<string>? prohibited) {
            prohibited = null;
            if(context.Profile is null || !policy.Rules.Profiles.TryGetValue(context.Profile, out var profile))
                return false;
            prohibited = profile;
            return true;
        }
        internal static ComplianceEvaluation EvaluateField(string? text, CompliancePolicy policy, ComplianceContext context,
            IReadOnlySet<string> prohibited, bool idSlot, CancellationToken token) {
            var field = SpdxLicenseFieldReader.Read(text, policy.ParseOptions, token);
            if(field.Expression is null) {
                var finding = field.Kind switch {
                    SpdxLicenseFieldKind.Missing => new ComplianceFinding(Unknown, FieldMissing),
                    SpdxLicenseFieldKind.Empty => new(Invalid, FieldEmpty),
                    SpdxLicenseFieldKind.LimitExceeded => new(Unknown, LimitExceeded),
                    SpdxLicenseFieldKind.None when !idSlot => new(Review, FieldNone),
                    SpdxLicenseFieldKind.NoAssertion when !idSlot => new(Unknown, FieldNoassertion),
                    SpdxLicenseFieldKind.InvalidSyntax => new(Invalid, InvalidSyntax),
                    _ => new(Invalid, WrongField)
                };
                finding = finding with { Diagnostics = [.. field.Diagnostics] };
                if(context.OrSelections is not null ||
                    (context.SelectedBranch is not null && finding.Decision != Invalid && field.Kind != SpdxLicenseFieldKind.LimitExceeded))
                    finding = RejectSelection(finding, context.SelectedBranch);
                return Result(policy, context, text, finding, field.Kind != SpdxLicenseFieldKind.LimitExceeded);
            }
            var syntax = field.Expression;
            if(idSlot && (syntax.Root is not SpdxLicenseExpression { OrLater: false } ||
                !SpdxLicenseRegistry.Default.TryGetLicense(text!, out _))) {
                var invalid = new ComplianceFinding(Invalid, WrongField);
                if(context.OrSelections is not null)
                    invalid = RejectSelection(invalid, context.SelectedBranch);
                return Result(policy, context, text, invalid);
            }
            var evaluator = new ExpressionEvaluation(policy, context, syntax, token);
            var explanation = evaluator.Run(prohibited);
            return Result(policy, context, text, explanation);
        }
        internal static ComplianceFinding UnconfiguredProfile(ComplianceContext context) {
            var finding = new ComplianceFinding(Review, ProfileUnconfigured);
            if(context.OrSelections is not null && context.SelectedBranch is not null)
                return RejectSelection(finding, context.SelectedBranch);
            return finding;
        }
        internal static ComplianceFinding RejectSelection(ComplianceFinding finding, string? legacyText) {
            return new(Invalid, SelectionMismatch) { Selection = legacyText, Children = [finding] };
        }
        internal static ComplianceEvaluation Result(CompliancePolicy policy, ComplianceContext context, string? text,
            ComplianceFinding finding, bool complete = true) {
            return new(policy.Identity, context.Profile, text, finding, complete);
        }
        internal static ComplianceFinding License(CompliancePolicy policy, ComplianceContext context, string identifier, bool orLater) {
            if(!SpdxLicenseRegistry.Default.TryGetLicense(identifier, out var entry))
                return new(Review, UnknownId);
            string id = orLater ? entry.Identifier + "+" : entry.Identifier;
            bool listed = policy.Rules.TryLicense(id, out var decision);
            ComplianceFinding finding;
            if(listed && decision == Deny)
                finding = new(Deny, ListedDeny);
            else if(orLater)
                finding = listed ? new(Allow, ListedAllow) : new(Review, OrLaterUnlisted);
            else if(entry.IsDeprecated)
                finding = new(Review, DeprecatedId);
            else if(listed)
                finding = new(Allow, ListedAllow);
            else if(policy.Rules.Notice is { } notice && notice.License == id)
                finding = CheckNotice(notice, context.Delivery);
            else
                finding = new(Review, NoPolicyRule);
            return finding with { CanonicalLicenseId = entry.Identifier, IsDeprecated = entry.IsDeprecated };
        }
        static ComplianceFinding CheckNotice(NoticeRule rule, NoticeDelivery? delivery) {
            string? observed = delivery?.Find(rule.File);
            bool complete = delivery?.IsComplete == true;
            var finding = observed switch {
                null when complete => new ComplianceFinding(Deny, NoticeMissing),
                null => new(Conditional, NoticeUnverified),
                _ when observed == rule.Sha256 => new(Allow, NoticeVerified),
                _ => new(Deny, NoticeMismatch)
            };
            return finding with { Notice = new(delivery?.Id, rule.File, rule.Sha256, observed, complete) };
        }
    }
    sealed class ExpressionEvaluation(CompliancePolicy policy, ComplianceContext context, SpdxParsedExpression syntax,
        CancellationToken token) {
        readonly Dictionary<SpdxReferenceExpression, SpdxReferenceResolution> resolutions = new();
        Dictionary<(int Start, int Length), int>? selections;
        internal ComplianceFinding Run(IReadOnlySet<string> prohibited) {
            if(context.References is { } references) {
                foreach(var resolution in references.Resolve(syntax, context.Document, token)) {
                    token.ThrowIfCancellationRequested();
                    resolutions.Add(resolution.Occurrence, resolution);
                }
            }
            var root = syntax.Root;
            while(root is SpdxGroupExpression group)
                root = group.Inner;
            SpdxOrExpression? legacyOuter = null;
            bool rejected;
            if(context.OrSelections is { } batch) {
                if(context.SelectedBranch is null)
                    selections = batch.Bind(syntax, token);
                rejected = selections is null;
            }
            else {
                legacyOuter = root as SpdxOrExpression;
                rejected = context.SelectedBranch is not null && legacyOuter is null;
            }
            var finding = Visit(syntax.Root, legacyOuter);
            if(rejected)
                finding = ComplianceEngine.RejectSelection(finding, context.SelectedBranch);
            if(prohibited.Count > 0) {
                foreach(var node in syntax.Root.EnumerateNodes(token)) {
                    if(node is not SpdxLicenseExpression license ||
                        !SpdxLicenseRegistry.Default.TryGetLicense(license.Identifier, out var entry))
                        continue;
                    if(prohibited.Contains(entry.Identifier) || (license.OrLater && prohibited.Contains(entry.Identifier + "+")))
                        return OnNode(new(Deny, ProhibitedPresence) { Children = [finding], Selection = context.SelectedBranch }, node);
                }
            }
            token.ThrowIfCancellationRequested();
            return finding;
        }
        ComplianceFinding Visit(SpdxExpressionNode node, SpdxOrExpression? outer = null) {
            token.ThrowIfCancellationRequested();
            var finding = node switch {
                SpdxLicenseExpression license => ComplianceEngine.License(policy, context, license.Identifier, license.OrLater),
                SpdxReferenceExpression reference => Reference(reference),
                SpdxWithExpression pair => Pair(pair),
                SpdxGroupExpression group => Group(group, outer),
                SpdxAndExpression and => All(Children(and.Operands)),
                SpdxOrExpression or => VisitOr(or, outer),
                _ => throw new InvalidOperationException()
            };
            return OnNode(finding, node);
        }
        ComplianceFinding OnNode(ComplianceFinding finding, SpdxExpressionNode node) {
            return finding with { Operand = syntax.SourceText.Substring(node.Span.Start, node.Span.Length), Span = node.Span, Kind = node.Kind };
        }
        ComplianceFinding Group(SpdxGroupExpression group, SpdxOrExpression? outer) {
            var inner = Visit(group.Inner, outer);
            return new(inner.Decision, inner.Reason) { Children = [inner] };
        }
        ImmutableArray<ComplianceFinding> Children(IReadOnlyList<SpdxExpressionNode> operands) {
            var children = ImmutableArray.CreateBuilder<ComplianceFinding>(operands.Count);
            foreach(var operand in operands)
                children.Add(Visit(operand));
            return children.MoveToImmutable();
        }
        ComplianceFinding Pair(SpdxWithExpression pair) {
            var result = new ComplianceFinding(Review, PairUnlisted);
            if(pair.Operand is SpdxLicenseExpression license &&
                SpdxLicenseRegistry.Default.TryGetLicense(license.Identifier, out var entry) &&
                SpdxLicenseRegistry.Default.TryGetException(pair.ExceptionIdentifier, out var exception)) {
                string id = license.OrLater ? entry.Identifier + "+" : entry.Identifier;
                if(policy.Rules.TryPair(id, exception.Identifier, out var decision)) {
                    if(decision == Deny)
                        result = new(Deny, PairDenied);
                    else if(entry.IsDeprecated || exception.IsDeprecated)
                        result = new(Review, DeprecatedId);
                    else
                        result = new(Allow, PairAllowed);
                }
            }
            if(pair.Operand is SpdxReferenceExpression reference)
                result = result with { Children = [Visit(reference)] };
            return result with { LicenseSpan = pair.Operand.Span, ExceptionSpan = pair.ExceptionSpan };
        }
        ComplianceFinding Reference(SpdxReferenceExpression node) {
            resolutions.TryGetValue(node, out var match);
            var definition = match?.Definition;
            string id = "LicenseRef-" + node.LicenseId;
            string? digest = definition is null ? null : context.References!.GetDigest(definition);
            bool approved = digest is not null && policy.Rules.Approves(definition!.DocumentIdentity, id, digest);
            var reason = match?.Status switch {
                SpdxReferenceResolutionStatus.Resolved => approved ? ReferenceApproved : ReferenceUnapproved,
                SpdxReferenceResolutionStatus.UnknownDocumentAlias => AliasUnknown,
                SpdxReferenceResolutionStatus.DefinitionMissing => DefinitionMissing,
                _ => ReferenceUnresolved
            };
            return new(approved ? Allow : Review, reason) {
                Reference = new(context.Document, match?.TargetDocumentIdentity, id, match?.Status, digest, approved)
            };
        }
        static ComplianceFinding All(ImmutableArray<ComplianceFinding> children) {
            var strongest = Allow;
            foreach(var child in children) {
                if(AndRank(child.Decision) > AndRank(strongest))
                    strongest = child.Decision;
            }
            var result = strongest switch {
                Deny => new ComplianceFinding(Deny, AndDenied),
                Invalid => new(Invalid, AndInvalid),
                Review or Unknown => new(Review, AndUnresolved),
                Conditional => new(Conditional, AndConditional),
                _ => new(Allow, AndAllAllowed)
            };
            return result with { Children = children };
        }
        static int AndRank(ComplianceDecision decision) {
            return decision switch {
                Deny => 5,
                Invalid => 4,
                Review or Unknown => 3,
                Conditional => 2,
                _ => 1
            };
        }
        ComplianceFinding VisitOr(SpdxOrExpression node, SpdxOrExpression? legacyOuter) {
            var children = Children(node.Operands);
            if(selections is not null && selections.TryGetValue((node.Span.Start, node.Span.Length), out int index))
                return Selected(children, index);
            if(node == legacyOuter && context.SelectedBranch is { } legacyText)
                return SelectByText(children, legacyText);
            return Any(children);
        }
        static ComplianceFinding Any(ImmutableArray<ComplianceFinding> children) {
            var choices = ImmutableArray.CreateBuilder<string>();
            bool allDenied = true;
            foreach(var child in children) {
                allDenied &= child.Decision == Deny;
                if(child.Decision is Allow or Conditional)
                    choices.Add(child.Operand!);
            }
            var result = choices.Count > 0 ? new ComplianceFinding(Conditional, ChoiceRequired) { Choices = choices.ToImmutable() }
                : allDenied ? new(Deny, OrAllDenied) : new(Review, OrUnresolved);
            return result with { Children = children };
        }
        static ComplianceFinding SelectByText(ImmutableArray<ComplianceFinding> children, string selection) {
            var result = new ComplianceFinding(Invalid, SelectionMismatch) { Selection = selection, Children = children };
            int? index = null;
            for(int i = 0; i < children.Length; i++) {
                if(children[i].Operand != selection)
                    continue;
                if(index is not null)
                    return result;
                index = i;
            }
            return index is int selected ? Selected(children, selected) : result;
        }
        static ComplianceFinding Selected(ImmutableArray<ComplianceFinding> children, int index) {
            var child = children[index];
            var reason = child.Decision switch {
                Allow => ChoiceAllowed,
                Deny => ChoiceDenied,
                _ => child.Reason
            };
            return new(child.Decision, reason) { Selection = child.Operand, SelectedIndex = index, Children = children };
        }
    }
}
