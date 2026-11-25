# Spdx.Compliance

[![Spdx.Compliance on NuGet](https://img.shields.io/nuget/v/Spdx.Compliance?label=NuGet)](https://www.nuget.org/packages/Spdx.Compliance/)

Spdx.Compliance is an offline .NET 10 library for evaluating SPDX license fields and
CycloneDX 1.6 component occurrences against a caller-supplied distribution policy.

```shell
dotnet add package Spdx.Compliance --version 0.1.0
```

- Load a versioned JSON policy with `CompliancePolicy.Load`. An optional correction catalog
  must match the SHA-256 recorded in that policy.
- Call `ComplianceEngine.Evaluate` for one field, with optional SPDX document scope,
  recorded choices for independent and nested ORs, and delivery evidence.
- Call `CycloneDxComplianceAnalyzer.Analyze` for a CycloneDX 1.6 JSON stream. Inspect each
  occurrence's decision, declared and observed licenses, and independent correction finding.
- Call `GetActionSummary()` on either evaluation to see current choices and evidence checks,
  plus branch conditions for eligible routes.

A verified correction confirms the expected supplier statement. Distribution depends on the
applicable policy rules and evidence. A complete document report describes the evaluated
occurrences and does not issue a release approval.

The package depends on `Spdx.Expressions` and `NuGet.Versioning`. All policy data and evidence are supplied by
the application. Evaluation performs no network or filesystem access.

See the [policy schema, API and runnable example for 0.1.0](https://github.com/DmitryGaravsky/Spdx.Expressions/blob/compliance-v0.1.0/docs/compliance.md)
in the source repository.
