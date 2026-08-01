# Cloudstrap Extraction Roadmap

> Owned by the project-manager agent. High-level deliverables only —
> detailed steps live in _plans/<Deliverable>.md (planner).
> Status: ⬜ not started · 📝 planning · 🔨 in progress · ⛔ blocked · ✅ done
>
> **File naming convention**: specs and plans carry the roadmap number as a prefix —
> `_specs/<N>-<Deliverable>.md` / `_plans/<N>-<Deliverable>.md`
> (e.g. `_specs/0-RepoScaffolding.md`, `_plans/0-RepoScaffolding.md`).
> Applies to all future deliverables.
>
> **Source reference repo (read-only, verified 2026-07-25)**:
> `D:\source\Nihdi-Core-Configuration\Nihdi-Core-Configuration\src\`
> (solution: `Nihdi.Core.Configuration.sln`). All "Source material" paths below are
> relative to that `src\` folder.
>
> **Not a deliverable**: `Nihdi.Core.Functional` is NOT ported — functional primitives
> come from the **LanguageExt.Core** NuGet package (MIT), referenced directly by
> consuming packages. Out of scope everywhere: message encryption, MessagingBridge,
> Dynatrace, ServicePlatform/ServicePulse.
>
> **Aspire posture (founding spec, decided 2026-07-25)**: coexist **without depending** —
> zero `Aspire.*` references in any shipped package (Aspire appears only in docs/samples,
> AC-ASP2); composability with Aspire ServiceDefaults is an explicit spec concern for
> deliverables 2 (AC-ASP1) and 4 (AC-ASP3); `Cloudstrap.Aspire` is NOT a v1 deliverable
> (post-v1 option, user-approved only).
>
> **Standing rule — SUT demonstration (user-directed 2026-08-01, deliverable #25)**: every
> deliverable's definition of done implicitly includes **demonstrating its headline behavior
> in the WASM SUT** (`src/Test/WasmTestProject`) **with ≥ 1 passing E2E test** in
> `Cloudstrap.WasmTestProject.E2E.Tests`, planned as the final slice of its `_plans/` file
> (planner rule 15). This applies to deliverables 3–24 even though their entries below do
> not repeat it.

## Dependency analysis (actual `<ProjectReference>` graph, verified 2026-07-25)

Old-package edges extracted from the source `.csproj` files:

| Old project | ProjectReferences | Notable internal PackageReferences (to replace) |
|---|---|---|
| Nihdi.Core.Configuration | Functional, **Dashboard.Contracts (inverted — cut)** | Nihdi.StyleCop.MsBuildProperties |
| Nihdi.Core.Configuration.Common | Configuration | Nihdi.AspNetCore.Localization, Nihdi.AspNetCore.Authentication.JwtBearer / Authorization / AccessTokenManagement, Nihdi.Core.Health |
| WebApi | Common | Nihdi.AspNetCore.Authentication.JwtBearer, Nihdi.Core.Health |
| Mvc | Common | — |
| Worker | Common, Configuration | — |
| OpenIdConnect | Configuration **only** | Nihdi.AspNetCore.* suite |
| OAuth | Configuration **only** | Nihdi.AspNetCore.Authentication.ClientCredentials, AccessTokenManagement |
| BlazorServer | Common | Nihdi.AspNetCore.AccessTokenManagement |
| BlazorWasm | — (standalone) | — |
| BlazorCommon | — (standalone) | — |
| NServiceBus | Common, Configuration | NServiceBus* suite, Nihdi.Core.Health, Nihdi.Core.NServiceBus.Cryptography |
| Hangfire | Configuration **only** | — (Hangfire.* LGPL) |
| Proxy | Common | — (YARP) |
| Hangfire.Proxy | Hangfire, Proxy | — |
| CookieConsent | — (standalone) | — (orejime JS bundled) |
| Analytics.Matomo | — (standalone) | — |
| Dashboard.Contracts | — (leaf) | — |
| Dashboard.Api | Configuration, NServiceBus, Functional, Dashboard.Contracts | Azure.Messaging.ServiceBus |
| Dashboard.Components.Shared | Dashboard.Contracts | Nihdi.Common.MudBlazor8.DesignSystem |
| Dashboard.Components | Configuration, Common, Functional, Components.Shared, Contracts | Nihdi.Common.MudBlazor8.DesignSystem, Newtonsoft.Json |
| Dashboard.Components.BlazorWasm | BlazorCommon, BlazorWasm, Components.Shared, Contracts | Nihdi.Common.MudBlazor8.DesignSystem |
| Nihdi.Core.Testing | Functional | — |

**Discrepancies / refinements vs the baseline band order** (actual graph wins):

1. **Old Core → Dashboard.Contracts is inverted and is cut** (spec decision): dashboard
   settings (`Nihdi.Core.Configuration\Settings\Dashboard\`) move to the Dashboard
   deliverable. New `Cloudstrap.Core` becomes a true leaf. Likewise the per-feature
   settings folders in old Core (`Settings\NServiceBus\`, `Settings\Hangfire\`,
   `Settings\Security\`, `Settings\Swagger|Scalar\`, `Settings\Logging\Dynatrace`) move
   to their owning Cloudstrap packages (or are deleted — Dynatrace/Bridge/ServicePlatform).
2. **Auth needs only Core, not hosting**: OpenIdConnect/OAuth reference only
   `Nihdi.Core.Configuration` — the baseline "hosting before auth" order is a choice,
   not a graph constraint. Kept (hosting first) because the new ClientCredentials feeds
   the typed-HttpClient registration that lives in `Cloudstrap.Extensions`.
3. **Hangfire needs only Core** (not Common/Extensions) — it is not blocked by
   Messaging or the hosting band; it stays in band 8 by choice, but can be pulled
   forward if priorities change. `Proxy` needs Extensions; `Hangfire.Proxy` needs both.
4. **BlazorCommon, BlazorWasm, CookieConsent, Analytics.Matomo, Dashboard.Contracts and
   Testing are standalone** (zero project references) — schedulable at any point; kept
   in their bands to preserve one-deliverable-in-flight focus.
5. **Old Common bundles what becomes Extensions + Observability**: `DistributedTracing\`,
   `Logging\`, `Correlation\`, `HealthChecks\` → Observability; `KeyVault\`,
   `BlobStorage\`, `HttpClient\`, `Extensions\`, `Host\`, `Options\` → Extensions;
   `Scalar\` → WebApi (per spec); `Dynatrace\` → deleted. Extensions consumes the
   correlation delegating handler and bootstrap logger, so **Observability ports before
   Extensions** (baseline confirmed by actual code coupling).
6. **Old Common → Nihdi.AspNetCore.Localization**: `Cloudstrap.Extensions` must NOT
   depend on `Cloudstrap.Localization` — drop the edge; Localization is a late,
   standalone thin layer. ⚠️ Its source is NOT in the reference repo (internal package)
   — it is a reimplementation over stock ASP.NET Core localization, not a port.
7. **Nihdi.Core.Health (internal) is used by Common, WebApi, NServiceBus** — replaced by
   `Microsoft.Extensions.Diagnostics.HealthChecks` (+ `AspNetCore.HealthChecks.*` where
   needed) inside Observability/WebApi/Worker; there is no separate health package.
8. **Dashboard.Api → NServiceBus** confirms Dashboard must follow Messaging.
9. Old Common references `Microsoft.AspNet.WebApi.Client` and
   `NWebsec.AspNetCore.Middleware` (last release 2019) — ⚠️ review both during the
   Extensions/Mvc ports; prefer dropping or replacing with maintained equivalents.

## Overview

| # | Deliverable | Packages | Depends on | Status | Plan |
|---|-------------|----------|------------|--------|------|
| 0 | Repo scaffolding | `src/Cloudstrap.sln`, `Directory.Build.props` (SDK analyzers, no StyleCop), `Directory.Packages.props` (CPM), `.editorconfig`, `GitVersion.yml`, `global.json`, `nuget.config`, CI workflows | — | ✅ | `_plans/0-RepoScaffolding.md` |
| 1 | Core settings model | Cloudstrap.Core | 0 | ✅ | `_plans/1-CoreSettingsModel.md` |
| 2 | Observability base | Cloudstrap.Observability | 1 ✅ | ✅ | `_plans/2-ObservabilityBase.md` |
| 3 | Azure Monitor exporter ← **next** | Cloudstrap.Observability.AzureMonitor | 2 ✅ | ⬜ | — |
| 4 | Config/KeyVault/HTTP extensions | Cloudstrap.Extensions | 1, 2 | ⬜ | — |
| 5 | WebApi bootstrap | Cloudstrap.WebApi | 4 | ⬜ | — |
| 6 | MVC bootstrap | Cloudstrap.Mvc | 4 | ⬜ | — |
| 7 | Worker bootstrap | Cloudstrap.Worker | 4 | ⬜ | — |
| 8 | Test helpers | Cloudstrap.Testing | 0 (flexible — pull earlier if a prior plan needs it) | ⬜ | — |
| 9 | Client-credentials auth | Cloudstrap.Authentication.ClientCredentials | 1, 4 | ⬜ | — |
| 10 | OIDC login | Cloudstrap.Authentication.OpenIdConnect | 1 | ⬜ | — |
| 11 | Blazor shared abstractions | Cloudstrap.BlazorCommon | 0 | ⬜ | — |
| 12 | Blazor Server helpers (+ TestProject SUT) | Cloudstrap.BlazorServer | 4, 9, 11 | ⬜ | — |
| 13 | Blazor WASM helpers (+ WasmTestProject SUT) | Cloudstrap.BlazorWasm | 11 | ⬜ | — |
| 14 | Messaging (Wolverine) | Cloudstrap.Messaging | 1, 2, 4 | ⬜ | — |
| 15 | Blob claim-check | Cloudstrap.Messaging.AzureBlob | 14 | ⬜ | — |
| 16 | Hangfire scheduler | Cloudstrap.Hangfire | 1 | ⬜ | — |
| 17 | YARP trusted-subsystem proxy | Cloudstrap.Proxy | 4 | ⬜ | — |
| 18 | Hangfire dashboard proxy | Cloudstrap.Hangfire.Proxy | 16, 17 | ⬜ | — |
| 19 | Dashboard backend | Cloudstrap.Dashboard.Contracts + Cloudstrap.Dashboard.Api | 1, 14 | ⬜ | — |
| 20 | Dashboard UI (MudBlazor) | Cloudstrap.Dashboard.Components.Shared + .Components + .Components.BlazorWasm | 11, 13, 19 | ⬜ | — |
| 21 | Cookie consent | Cloudstrap.CookieConsent | 0 | ⬜ | — |
| 22 | Analytics abstraction + Matomo | Cloudstrap.Analytics + Cloudstrap.Analytics.Matomo | 21 | ⬜ | — |
| 23 | Google Analytics adapter | Cloudstrap.Analytics.GoogleAnalytics | 22 | ⬜ | — |
| 24 | Localization setup | Cloudstrap.Localization | 1 | ⬜ | — |
| 25 | WASM SUT + E2E demonstration harness | `src/Test/WasmTestProject` (SUT + E2E tests — not a shipped package) | 1, 2 | 🔨 | `_plans/25-WasmTestProjectSut.md` |

**Reconciled with reality 2026-07-30** (globbed `src/**/*.csproj`, read every plan's gate
checkboxes): four `.csproj` files exist — `src/Cloudstrap.Core/`, `src/Cloudstrap.Observability/`
and their two test projects under `src/Test/UnitTest/`. All three executed plans are 100%
complete with every 🛑 gate box checked: `_plans/0-RepoScaffolding.md` (final gate closed
2026-07-26, incl. the nuget.org prefix reservation) → #0 ✅; `_plans/1-CoreSettingsModel.md`
(19/19 boxes) → #1 ✅; `_plans/2-ObservabilityBase.md` (12 steps, 6 gates — final gate
accepted 2026-07-30) → #2 ✅. **The publish path remains open** — nothing operational blocks
pushing a `Cloudstrap.*` package to a feed. Everything 3–24 is honestly ⬜; #3 is the next
schedulable deliverable (its only dependency, 2, is ✅).

## Deliverable details

### 0. Repo scaffolding
- **Goal**: A contributor can clone Cloudstrap, run `dotnet build src/Cloudstrap.sln`, tests, and format checks, and CI enforces all three — before any package exists.
- **Spec**: `_specs/0-RepoScaffolding.md` — approved 2026-07-25, zero Open Questions; its Decision Log and file inventory are authoritative for this deliverable.
- **Source material**: `Nihdi.Core.Configuration.sln` (solution layout only); source `Directory.Build.props` + `.editorconfig` (conventions to redesign — everything StyleCop-related is dropped, see spec Port Decision Table); `Test\TestProject\src\nuget.config` (`<clear/>` + single-feed pattern). No `GitVersion.yml` or GitHub workflows exist in the source repo (Azure DevOps) — author fresh per spec.
- **Depends on**: —
- **Migration decisions**: GitVersion + tags on `main`, `-preview.N` on `dev`; **NUnit 4 + NUnit3TestAdapter + NUnit.Analyzers on Microsoft.Testing.Platform** (user gate decision — deviates from the founding spec's MSTest v4; founding-spec amendment still pending with the user); **no StyleCop** — `Nihdi.StyleCop.MsBuildProperties` dropped outright (supersedes the earlier "reproduce its effect inline" instruction), replaced by .NET SDK analyzers (`AnalysisLevel=latest-recommended`), `EnforceCodeStyleInBuild` + `.editorconfig` as the single style authority, `TreatWarningsAsErrors` kept, `dotnet format --verify-no-changes` as the style gate; CS1591 XML-doc enforcement on in `src/`, off under `src/Test/`; **Central Package Management** (`src/Directory.Packages.props`); SourceLink via the SDK built-in (no package reference); publishing model: `-preview.N` packages from `dev` pushes only → **GitHub Packages**, stable via tag-push `v*` (`release.yml`) → **nuget.org only**, scheduled `cleanup-previews.yml` keeps the last 20 preview versions per package; `net10.0` only.
- **De-NIHDI items**: internal NuGet feed → nuget.org (root `nuget.config`); `Nihdi.StyleCop.MsBuildProperties` + per-project `stylecop.json` (`companyName = Riziv-Inami`) dropped outright — no StyleCop at all, .NET SDK analyzers replace it; no company copyright headers — licensing carried by `LICENSE` + `PackageLicenseExpression`, no per-file headers.
- **Definition of done**: `src/Cloudstrap.sln` (near-empty) + `src/Directory.Build.props` + `src/Directory.Packages.props` + `src/.editorconfig` + `GitVersion.yml` + `global.json` + `nuget.config` + `.github/workflows/ci.yml`/`release.yml`/`cleanup-previews.yml` + package icon asset (file details owned by the spec's inventory); build/test/format green locally and in CI (placeholder NUnit test project proves the test leg); zero `Nihdi`/`NIHDI`/`Riziv` identifiers; spec AC-R1…AC-R11 met.
- **Risks**: ⚠️ Analyzer ruleset choices are fixed afterwards (CLAUDE.md: `Directory.Build.props` rules are immutable) — get them right here. ~~⚠️ Reserve the `Cloudstrap.` nuget.org ID prefix before first publish~~ — **resolved: prefix reserved 2026-07-26**.
- **Status**: ✅ — done 2026-07-26. Both 🛑 gates approved and every box in `_plans/0-RepoScaffolding.md` checked. Local rails (build · test · format · CPM · SDK analyzers) and all three workflows are proven on GitHub: `ci.yml` on PR (AC-R4), `dev`-branch preview-publish no-op (AC-R5), `v0.1.0` tag through `release.yml` (AC-R6), `cleanup-previews.yml` dispatch, GitVersion probes (AC-R9), workflow code review + empty De-NIHDI sweep (AC-R8). **Publishing prerequisites are all satisfied**: the `Cloudstrap.` package ID prefix is reserved on nuget.org, the `Cloudstrap-GitHubActions-Release` Trusted Publishing policy is active, and the `NUGET_USER` secret is set — no operational item blocks the first package push.

### 1. Core settings model
- **Goal**: A consumer can bind and validate the `Cloudstrap:` configuration section into a typed `CloudstrapOptions` model (retrieved via `GetCloudstrapOptions()`).
- **Spec**: `_specs/1-CoreSettingsModel.md` — approved 2026-07-25, both Open Questions resolved; its verdict table (3 Port · 12 Redesign · 8 Drop · 25 Move-out) is authoritative for this deliverable.
- **Plan**: `_plans/1-CoreSettingsModel.md` — fully executed (5 steps, 3 🛑 gates, 19/19 boxes checked; gates approved 2026-07-26).
- **Source material**: `Nihdi.Core.Configuration\` — `Settings\*` (Application, Correlation, Logging, OpenTelemetry, HealthChecks, HttpClient), `ConfigurationBuilderExtensions.cs`, `ConfigurationExtensions.cs`, `ConfigurationException.cs`, `EnvironmentConstants.cs`, `BootstrapConfiguration.cs`.
- **Depends on**: 0
- **Migration decisions**: section `Nihdi:` → `Cloudstrap:`; root settings type named **`CloudstrapOptions`** with `GetCloudstrapOptions()` accessor (**OQ-1** — repo `*Options` convention; founding-spec amendment `CloudstrapConfiguration` → `CloudstrapOptions` approved by the user, technical-analyst applying it to `_specs/Cloudstrap.md`); **`AppRegistrationConfiguration` dropped outright** (**OQ-2** — no Cloudstrap type models client-id/secret credentials in appsettings; deliverable 14 builds transport auth on `TokenCredential`/`DefaultAzureCredential` + standard `AZURE_*` environment variables); **cut the inverted Dashboard.Contracts reference** (dashboard settings move to deliverable 19); per-feature settings move out (`Settings\NServiceBus\` → 14, `Settings\Hangfire\` → 16, `Settings\Security\` → 9/10, `Settings\Swagger|Scalar\` → 5); delete `Settings\Logging\DynatraceConfiguration.cs` and Bridge settings; the source `Nihdi.Core.Functional` ProjectReference is **dead code — zero usage** in any source file, so new Core takes **no LanguageExt.Core dependency** (exact LanguageExt type-mapping decision defers to the first package that genuinely consumes Functional types — likely Testing or Dashboard); validation via `Microsoft.Extensions.Options` + DataAnnotations replaces the bespoke validation cascade; only `Microsoft.Extensions.*` dependencies — host-agnostic (no `Microsoft.AspNetCore.App` framework reference), WASM-loadable, a true leaf.
- **De-NIHDI items**: environment taxonomy LOC/DEV/TST/VAL/PRD → standard ASP.NET Core environments + `Cloudstrap:Application:EnvironmentTier`; drop machine-name/log-path parsing from settings; keep documented workload naming `{system}-{subsystem}-{type}` (overridable); neutral test fixture values.
- **Definition of done**: build/tests/format green; XML docs on all public API; package metadata complete; zero `Nihdi`/`Riziv` identifiers; validation failures surface through the Options + DataAnnotations pipeline per the spec; no reference to any Dashboard type; spec acceptance criteria met.
- **Risks**: ⚠️ Public API surface for every later package — shape mistakes propagate; ⚠️ deciding which settings stay in Core vs move out changes every later deliverable. (Resolved: the LanguageExt type-mapping decision no longer lands here — Core has zero LanguageExt dependency.)
- **Delivered (2026-07-26)** — `src/Cloudstrap.Core/`, single namespace `Cloudstrap.Core`, everything `public sealed`, full XML docs; `src/Test/UnitTest/Cloudstrap.Core.Tests/` (NUnit 4 on MTP, 49 tests). Verified on the final tree: build 0 warnings/0 errors · 49/49 tests pass (exit 0) · `dotnet format --verify-no-changes` exit 0 · Release build produces `.nupkg` + `.snupkg` with 0 warnings. AC-C1…AC-C10 + AC-ASP2 signed off at the Slice-3 gate.
  - **Public surface**: `CloudstrapOptions` (root, section `Cloudstrap`), `ApplicationOptions`, `LoggingOptions` (+ `ConsoleLoggingOptions`/`FileLoggingOptions`), `OpenTelemetryOptions` (+ `OpenTelemetryMode`: `Disabled|Console|Otlp|AzureMonitor`), `CorrelationOptions` (+ `CorrelationRequestOptions`/`CorrelationMessageOptions`), `HealthChecksOptions`, `HttpClientServiceOptions` (+ `TokenRequestOptions`); entry points `ServiceCollectionExtensions.AddCloudstrapCore()` (binds + `ValidateOnStart` on all six options types) and `ConfigurationExtensions.GetCloudstrapOptions()` (eager pre-host bind + validate); `ConfigurationValidationException` (`IReadOnlyList<string> Failures`). Validators are `internal`, one rule in exactly one class, `CloudstrapOptionsValidator` the single shared cascade — one rule set, two entry points. The `OpenTelemetryMode.AzureMonitor` member is the enum seam deliverables 2 and 3 fill.
  - **Dependency closure** (all Microsoft-maintained, MIT, CPM-pinned at 10.0.10): `Microsoft.Extensions.Configuration.Abstractions`, `.Configuration.Binder`, `.DependencyInjection.Abstractions`, `.Logging.Abstractions`, `.Options`, `.Options.ConfigurationExtensions`. Zero `Aspire.*` (AC-ASP2), zero `LanguageExt.*`, **no `Microsoft.AspNetCore.App` framework reference** — Core is host-agnostic and Blazor-WASM-loadable. Test-only additions: `Microsoft.Extensions.Configuration`, `.DependencyInjection`.
  - **Facts every later deliverable inherits** (established here, do not re-litigate per package):
    1. **No `Microsoft.Extensions.Options.DataAnnotations`** — deliberately not taken despite the spec's dependency table listing it; the `[OptionsValidator]` source generator emits the `[Required]` checks directly. User-confirmed at the Slice-2 gate. Later packages follow the same source-generated-validator pattern.
    2. **Collection/dictionary options properties are get-only initialized**, so the configuration binder populates them in place and configured values **append to defaults rather than replacing them**. Only `CorrelationRequestOptions.HealthEndpoints` ships non-empty defaults (`["/healthz","/ready"]`). Documented in the package README; every consuming deliverable inherits this caveat and must document it for its own collection settings.
    3. **A broken section-level rule is reported twice at host startup** — once root-relative (`Application:SystemName: …`) and once section-relative (`SystemName: …`) — because both the root graph and each section are registered. User-acknowledged; startup fails either way and both messages name the member.
    4. **Zero LanguageExt.Core dependency in Core** (spec finding 1: the source `Nihdi.Core.Functional` reference was dead code). The founding spec's functional-type-mapping decision moves to the first package that genuinely consumes functional types — still open, likely deliverable 8 (Testing) or 19/20 (Dashboard).
- **Status**: ✅ — done 2026-07-26 (plan's final 🛑 gate checked; definition of done holds). Not yet *published* — nothing has been pushed to a feed yet — but no longer blocked from publishing: deliverable 0 is complete and the `Cloudstrap.` prefix is reserved, so `Cloudstrap.Core` ships whenever a `dev` push (preview) or a `v*` tag (stable) says so.

### 2. Observability base
- **Goal**: A consumer calls `UseCloudstrapObservability` to get Serilog bootstrap logging plus a vendor-neutral OTel traces/metrics/logs pipeline (modes `Disabled | Console | Otlp`, `AzureMonitor` enum reserved) with correlation and noise filtering.
- **Spec**: `_specs/2-ObservabilityBase.md` — acceptance criteria (AC-O2/O3/O4, AC-ASP1/ASP2, AC-B1…AC-B13, + amended AC-C6) walked and signed off at the plan's final gate.
- **Plan**: `_plans/2-ObservabilityBase.md` — fully executed (12 steps, 6 🛑 gates, all boxes checked; final gate accepted 2026-07-30).
- **Source material** *(file list re-verified against the reference repo 2026-07-26)*: `Nihdi.Core.Configuration.Common\` —
  - `DistributedTracing\` — `ServiceCollectionExtensions.cs` (the pipeline; spec says it ports largely unchanged), `BlazorHubSampler.cs`, `NihdiResourceAttributes.cs`, `IBusinessTrace.cs`/`BusinessTrace.cs`, `IBusinessTraceScope.cs`/`BusinessTraceScope.cs`;
  - `Logging\` — `BootstrapLoggerFactory.cs`, `DeferredLoggerFactory.cs`, `NihdiConfigurationExtensions.cs`, `NihdiConsoleFormatter.cs`, `TracingEnricher.cs`, `MessageIdEnricher.cs`, `W3CTracingMiddleware.cs`;
  - `Correlation\` — `CorrelationHeader.cs`, `ICorrelationContext`/`DefaultCorrelationContext`, `ICorrelationContextAccessor`/`DefaultCorrelationContextAccessor`, `ICorrelationSource`/`DefaultCorrelationSource`, `CorrelationExtensions.cs`, `CorrelationValidationMiddleware.cs`, `CorrelationRequiredAttribute.cs`/`AllowNoCorrelationAttribute.cs`, **`CorrelationHttpDelegatingHandler.cs` + `IHttpClientBuilderExtensions.cs`** (the pieces deliverable 4 consumes — they confirm Observability ports before Extensions);
  - `HealthChecks\` — `ApiLivenessHealthCheck.cs`, `ServiceCollectionExtensions.cs`;
  - **Read-and-delete, do not port**: `Dynatrace\*` (5 files) and the `BootstrapLoggerFactory` Dynatrace branch.
  - Also relevant: old Core `Settings\Logging\*` + `Settings\OpenTelemetry\*` + `Settings\Correlation\*` + `Settings\HealthChecks\*` are **already shipped** as `LoggingOptions`/`OpenTelemetryOptions`/`CorrelationOptions`/`HealthChecksOptions` in `Cloudstrap.Core` — this deliverable **consumes** them and must not redefine them.
- **Depends on**: 1 ✅
- **Migration decisions**: Dynatrace removed entirely — delete `Common\Dynatrace\*` and the `BootstrapLoggerFactory` Dynatrace branch; generic OTLP keeps a configurable headers dictionary (no `Api-Token` helper); Serilog stays for bootstrap/console/file; runtime logs via OTel; `Nihdi.Core.Health` → `Microsoft.Extensions.Diagnostics.HealthChecks`. **Aspire coexistence**: `UseCloudstrapObservability` supports pipeline-**owner** (default) and **contribute** modes — contribute adds only samplers/noise filters/enrichment/`IBusinessTrace` to an existing (e.g. ServiceDefaults) OTel pipeline, no duplicate exporters (AC-ASP1).
- **De-NIHDI items**: correlation header `NIHDI.Correlation` → `X-Correlation-ID` (configurable); resource attributes `nihdi.*` → `cloudstrap.*`/standard semconv; probe path `/probe.aspx` → `/healthz` + `/ready` (configurable); log path `D:\logsint` default removed.
- **Definition of done**: build/tests/format green; XML docs; AC-O2 (Otlp mode, no Azure dependency loaded), AC-O3 (probe/`_blazor` noise filtered), AC-O4 (zero "Dynatrace" occurrences) verifiable; AC-ASP1 (contribute mode composes with a pre-existing OTel pipeline — no duplicate exporters) covered by test; zero `Nihdi` identifiers.
- **Risks**: ⚠️ Largest port of the foundation bands; ⚠️ new external deps: Serilog suite (Apache-2.0), OpenTelemetry.* (Apache-2.0) — versions to re-pin (CPM: `src/Directory.Packages.props`, which currently holds only Microsoft.Extensions 10.0.10 + the NUnit trio); ⚠️ splitting Common cleanly so Extensions (4) doesn't drag observability internals; ⚠️ **Aspire overlap** — OTel wiring and health checks are exactly what Aspire ServiceDefaults also does, so owner/contribute composability (AC-ASP1) and additive `IHealthChecksBuilder` registration are spec-level requirements, not implementation details; ⚠️ this is the first package with an `Microsoft.AspNetCore.App` framework reference — keep any WASM-relevant surface out of it, and do not regress Core's host-agnostic closure.
- **Delivered (2026-07-30)** — `src/Cloudstrap.Observability/` + `src/Test/UnitTest/Cloudstrap.Observability.Tests/`. Verified on the final tree: build zero warnings · Cloudstrap.Core.Tests 52/52 · Cloudstrap.Observability.Tests 91/91 · `dotnet format --verify-no-changes` clean · Release `.nupkg` contents reviewed (README, icon, XML docs, nuspec metadata).
  - **Shipped surface**: Serilog bootstrap + host logging (`CloudstrapBootstrapLogger`, `UseCloudstrapObservability`); vendor-neutral OTel pipeline with **owner** (default) and **contribute** modes across `Disabled|Console|Otlp|AzureMonitor` (AC-ASP1 proven end to end); trace noise filter + Blazor hub sampler; OTLP per-signal exporter setup; AzureMonitor fail-fast guard; `Cloudstrap.Observability.Correlation` (accessor, source, middleware, attributes); `IBusinessTrace`/`IBusinessTraceScope`; `CloudstrapActivitySources`; full package metadata + README; `PackageSurfaceTests` guards the public surface.
  - **Seams and facts later deliverables inherit** (established here, do not re-litigate per package):
    1. `MarkExporterContributed()` — the seam deliverable 3 fills to lift the AzureMonitor fail-fast guard.
    2. `CorrelationHttpDelegatingHandler` + `AddCloudstrapCorrelationHandler` — the typed-HttpClient correlation seam deliverable 4 consumes.
    3. `CloudstrapHealthCheckTags` (`"live"`/`"ready"`) — the cross-package health-tag contract consumed by deliverables 4, 5, 7 and 12.
    4. **Core amendment (AC-C6)**: `OTEL_EXPORTER_OTLP_ENDPOINT` now satisfies the Otlp endpoint-required rule on both Core validation paths (DI `ValidateOnStart` and eager `GetCloudstrapOptions()`); Cloudstrap.Core.Tests grew to 52.
    5. **First `Microsoft.AspNetCore.App` framework reference** in the suite (gate decision OQ-1 — one-package posture; the runtime-image consequence is documented in the package README). Core's host-agnostic closure is unregressed.
    6. **First non-Microsoft dependencies**: Serilog 4.4.0 family + OpenTelemetry 1.17.0 family (all Apache-2.0, CPM-pinned in `src/Directory.Packages.props`); zero `Azure.*`, zero `Aspire.*` (AC-ASP2).
- **Status**: ✅ — done 2026-07-30 (plan's final 🛑 gate checked; definition of done holds; user accepted 2026-07-30).

### 3. Azure Monitor exporter
- **Goal**: A consumer sets `Cloudstrap:OpenTelemetry:Mode = AzureMonitor` + a connection string and telemetry lands in Application Insights, correlated by operation ID.
- **Source material**: new package (spec "Observability Migration" §1); mode plumbing from `Common\DistributedTracing\ServiceCollectionExtensions.cs`.
- **Depends on**: 2
- **Migration decisions**: `Azure.Monitor.OpenTelemetry.Exporter` (`AddAzureMonitorTraceExporter`/`MetricExporter`/`LogExporter`); connection string from setting or `APPLICATIONINSIGHTS_CONNECTION_STRING`; AAD credential support; expose fixed-rate sampling + `AlwaysOnSampler` dev flag.
- **De-NIHDI items**: none beyond naming (new code).
- **Definition of done**: build/tests/format green; XML docs; AC-O1 demonstrable against a real App Insights resource (manual verification documented — unit tests mock at boundary); base package (2) still loads zero Azure assemblies in Otlp mode (AC-O2 regression).
- **Risks**: ⚠️ New dependency `Azure.Monitor.OpenTelemetry.Exporter` (MIT) — keep isolated so base stays exporter-agnostic.
- **Status**: ⬜

### 4. Config/KeyVault/HTTP extensions
- **Goal**: A consumer bootstraps KeyVault-backed configuration, Azure Blob DataProtection, typed `HttpClient` registration (`AddCloudstrapHttpServiceClient<TI,TImpl>`), and hosting helpers with one call each.
- **Source material**: `Nihdi.Core.Configuration.Common\` — `KeyVault\*` (`AddAzureKeyvaultForNihdi.cs`, `PrefixKeyVaultSecretManager.cs`), `BlobStorage\*`, `HttpClient\*`, `Extensions\*` (incl. `IHostApplicationBuilderExtensions.cs`, `ProbeHealthCheckExtensions.cs`), `Host\HostRunner.cs`, `Options\*`, `Serialization\*`.
- **Depends on**: 1, 2 (correlation delegating handler + bootstrap logger)
- **Migration decisions**: `Scalar\*` moves to deliverable 5 (WebApi); drop `Nihdi.AspNetCore.Localization` dependency entirely; auth-token attachment for typed clients becomes an integration seam filled by deliverable 9 (no dependency from 4 to 9); `Nihdi.Core.Health` → stock health checks. **Aspire coexistence**: `AddCloudstrapHttpServiceClient<TI,TImpl>` tolerates resilience handlers already applied via `ConfigureHttpClientDefaults` — no stacked resilience (AC-ASP3); KeyVault config documented "Cloudstrap's or Aspire's, not both" (secret-prefix filter is the differentiator); support standard `ConnectionStrings:` names where sensible.
- **De-NIHDI items**: hard-coded KeyVault naming → `Cloudstrap:KeyVault:VaultUri` (+ optional secret-prefix defaulting to `Application:WorkloadName`); hard-coded storage naming → `Cloudstrap:Storage:BlobServiceUri` (container defaults to `Application:SystemName`); `AddAzureKeyvaultForNihdi` → `AddCloudstrapKeyVault`.
- **Definition of done**: build/tests/format green; XML docs; no reference to any auth package; ⚠️-flagged deps reviewed (`Microsoft.AspNet.WebApi.Client`, `NWebsec.AspNetCore.Middleware` — drop or justify); AC-ASP3 (no stacked resilience) covered by test; zero `Nihdi` identifiers.
- **Risks**: ⚠️ New external deps: `Azure.Identity`, `Azure.Extensions.AspNetCore.Configuration.Secrets`, `Azure.Extensions.AspNetCore.DataProtection.{Blobs,Keys}` (all MIT); ⚠️ NWebsec appears unmaintained — decide replace/drop here.
- **Status**: ⬜

### 5. WebApi bootstrap
- **Goal**: A consumer calls `AddCloudstrapWebApi` to get API versioning, OpenAPI (NSwag/Scalar UI), hardened middleware, health endpoints, and `AddCloudstrapJwtBearer`.
- **Source material**: `Nihdi.Core.Configuration.WebApi\`; plus `Common\Scalar\*` and old Core `Settings\Swagger|Scalar\*` (moved here).
- **Depends on**: 4
- **Migration decisions**: `Nihdi.AspNetCore.Authentication.JwtBearer` → stock JWT bearer with hardened defaults (audience validation on, reduced clock skew, HTTPS metadata outside Development); `Nihdi.Core.Health` → stock health checks.
- **De-NIHDI items**: API naming `AddNihdiX` → `AddCloudstrapX`; neutral fixtures.
- **Definition of done**: build/tests/format green; XML docs; integration tests verify DI registration + Swagger/Scalar endpoints; JWT hardened defaults covered by tests; zero `Nihdi` identifiers.
- **Risks**: ⚠️ Auth code (risk area — human review); external deps `Asp.Versioning.*` (MIT), `NSwag.AspNetCore` (MIT), `Scalar.AspNetCore` (MIT).
- **Status**: ⬜

### 6. MVC bootstrap
- **Goal**: A consumer calls `AddCloudstrapMvc` for session hardening, correlation, and secure-header middleware in server-rendered apps.
- **Source material**: `Nihdi.Core.Configuration.Mvc\` (small — README’d package, references Common only).
- **Depends on**: 4
- **Migration decisions**: none specific beyond the Common split.
- **De-NIHDI items**: naming; neutral fixtures.
- **Definition of done**: build/tests/format green; XML docs; zero `Nihdi` identifiers.
- **Risks**: low — smallest hosting package.
- **Status**: ⬜

### 7. Worker bootstrap
- **Goal**: A consumer bootstraps a headless worker service with observability and a health listener on a configurable port.
- **Source material**: `Nihdi.Core.Configuration.Worker\` (references Common + Core; `InternalsVisibleTo` from Common — remove or invert cleanly).
- **Depends on**: 4
- **Migration decisions**: health listener port configurable, default 9000.
- **De-NIHDI items**: naming; probe conventions → `/healthz`+`/ready`.
- **Definition of done**: build/tests/format green; XML docs; health listener port override covered by test; zero `Nihdi` identifiers.
- **Risks**: low.
- **Status**: ⬜

### 8. Test helpers
- **Goal**: A consumer (and Cloudstrap's own test projects) gets `WebApplicationFactory`/EF test utilities from one package.
- **Source material**: `Nihdi.Core.Testing\` (references Functional + `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.EntityFrameworkCore`).
- **Depends on**: 0 — **flexible slot**: pull forward if an earlier deliverable's plan needs the factory helpers (e.g. WebApi integration tests).
- **Migration decisions**: Functional reference → LanguageExt.Core (or drop if unused after port).
- **De-NIHDI items**: rename only; neutral fixtures (`example.com`, `contoso`).
- **Definition of done**: build/tests/format green; XML docs; zero `Nihdi` identifiers.
- **Risks**: low.
- **Status**: ⬜

### 9. Client-credentials auth
- **Goal**: A consumer registers machine-to-machine token acquisition (cached, auto-renewed) that plugs into the typed `HttpClient` registration and proxy-forwarding helpers.
- **Source material**: `Nihdi.Core.Configuration.OAuth\` (+ old Core `Settings\Security\ClientCredentialsConfiguration.cs`, `OAuthConfiguration.cs`).
- **Depends on**: 1, 4
- **Migration decisions**: rebuild on **Duende.AccessTokenManagement** (Apache-2.0) client-credentials; fills the token seam of `AddCloudstrapHttpServiceClient<TI,TImpl>`.
- **De-NIHDI items**: `Nihdi.AspNetCore.Authentication.ClientCredentials`/`AccessTokenManagement` → Duende ATM (AC-A3: zero `Nihdi.AspNetCore` references); naming.
- **Definition of done**: build/tests/format green; XML docs; AC-A2 (transparent renewal) covered by tests with mocked token endpoint; human review (auth risk area); zero `Nihdi` identifiers.
- **Risks**: ⚠️ Auth risk area; ⚠️ first Duende ATM usage — de-risks deliverable 10 and BlazorServer (scheduled before OIDC deliberately, ordering rule 3).
- **Status**: ⬜

### 10. OIDC login
- **Goal**: A consumer calls `AddCloudstrapOpenIdConnect` for auth-code + PKCE login with secure cookie defaults and Duende ATM user-token refresh.
- **Source material**: `Nihdi.Core.Configuration.OpenIdConnect\` (+ old Core `Settings\Security\OpenIdConnectConfiguration.cs`, `AuthenticationConfiguration.cs`).
- **Depends on**: 1 (Duende ATM patterns de-risked by 9)
- **Migration decisions**: stock `Microsoft.AspNetCore.Authentication.OpenIdConnect` + Duende ATM user-token management replaces the `Nihdi.AspNetCore.*` suite (incl. Authentication.UI — dropped or minimal stock equivalents).
- **De-NIHDI items**: AC-A3; naming; neutral IdP fixtures.
- **Definition of done**: build/tests/format green; XML docs; AC-A1 verified against a standards-compliant IdP (Keycloak container, E2E/manual documented); human review (auth risk area); zero `Nihdi` identifiers.
- **Risks**: ⚠️ Auth risk area; ⚠️ behavior parity with the internal suite (UI/logout endpoints) needs explicit scoping in the plan.
- **Status**: ⬜

### 11. Blazor shared abstractions
- **Goal**: A consumer gets shared Blazor abstractions (ErrorHandler, Navigation, ViewModel base, Scrutor scanning) usable from Server and WASM.
- **Source material**: `Nihdi.Core.Configuration.BlazorCommon\` (standalone; `Microsoft.AspNetCore.Components` + `Scrutor`).
- **Depends on**: 0 (standalone — kept in Blazor band by choice)
- **Migration decisions**: rename only per spec.
- **De-NIHDI items**: naming; neutral fixtures.
- **Definition of done**: build/tests/format green; XML docs; zero `Nihdi` identifiers.
- **Risks**: low; `Scrutor` (MIT).
- **Status**: ⬜

### 12. Blazor Server helpers (+ TestProject SUT)
- **Goal**: A consumer bootstraps a Blazor Server app with tracing, typed HttpClients with token attachment, and hardened defaults; the repo gains the Blazor Server SUT for E2E smoke tests.
- **Source material**: `Nihdi.Core.Configuration.BlazorServer\`; SUT inspiration: `Test\TestProject\src\*` (rebuild neutral, do not copy wholesale).
- **Depends on**: 4, 9, 11
- **Migration decisions**: `Nihdi.AspNetCore.AccessTokenManagement` → Duende ATM (via deliverable 9); `BlazorHubSampler` stays in Observability.
- **De-NIHDI items**: probe path `/probe.aspx` → `/healthz`; naming; neutral fixtures.
- **Definition of done**: build/tests/format green; XML docs; TestProject SUT boots and E2E smoke test passes; zero `Nihdi` identifiers.
- **Risks**: ~~⚠️ First E2E infrastructure (Playwright) lands here~~ — resolved: the Playwright E2E harness already exists (deliverable #25); this deliverable reuses its patterns for the Blazor Server SUT.
- **Status**: ⬜

### 13. Blazor WASM helpers (+ WasmTestProject SUT)
- **Goal**: A consumer bootstraps a WASM client with cookie auth (BFF pattern), XSRF, and Refit clients; repo gains the WASM SUT.
- **Source material**: `Nihdi.Core.Configuration.BlazorWasm\` (standalone; Refit, Components.Authorization); SUT inspiration: `Test\WasmTestProject\src\*`.
- **Depends on**: 11 (band choice; package itself is standalone)
- **Scope note (2026-08-01)**: the WASM SUT already exists — built by deliverable #25 (`src/Test/WasmTestProject`: Contracts, Presentation, Host/Wasm, Host/**Bff**, E2E harness). #13 no longer scaffolds it; it adds the BlazorWasm helpers and demos them in the existing SUT (cookie/BFF auth, XSRF, Refit clients replacing the plain `HttpClient`).
- **Migration decisions**: browser-auth pattern (cookie + XSRF + `BffAuthenticationStateProvider`) ports as-is per spec; `Microsoft.Extensions.Localization` usage is stock — keep. Source's committee-named `Cfe` host → industry-standard **`Bff`** (decided at #25 Gate 1).
- **De-NIHDI items**: naming; neutral fixtures.
- **Definition of done**: build/tests/format green; XML docs; WASM SUT boots and smoke test passes; zero `Nihdi` identifiers.
- **Risks**: `Refit` (MIT).
- **Status**: ⬜

### 14. Messaging (Wolverine)
- **Goal**: A consumer calls `AddCloudstrapMessaging(...).UseSqlServer(...)` for a Wolverine node with ASB/SQL/local transports, durable inbox/outbox, EF transactional messaging (`AddCloudstrapTransactionalMessaging<TDbContext>`), retries, and correlation.
- **Source material**: `Nihdi.Core.Configuration.NServiceBus\` (behavior reference only — implementation is new on Wolverine) + old Core `Settings\NServiceBus\*` (config shape; Bridge/ServicePlatform settings deleted).
- **Depends on**: 1, 2, 4
- **Migration decisions**: full NServiceBus → Wolverine table from the spec (transports, suffix conventions, outbox with `{WorkloadName}_` prefix, retries, `AutoProvision` default-on in Development, DLQ `{system}-error`); storage-provider seam (SQL Server v1, PostgreSQL later); **dropped**: property-level encryption, MessagingBridge, ServicePlatform connector, UniformSession.
- **De-NIHDI items**: ASB topic `nihdi-default-bundle` → Wolverine conventions (overridable); license paths gone; `Nihdi.Core.NServiceBus.Cryptography` gone; naming.
- **Definition of done**: build/tests/format green; XML docs; AC-M2 (outbox atomicity) + AC-M3 (in-memory transport, no network) covered by tests; AC-M1 verified against real ASB (documented manual/E2E); zero `Nihdi` identifiers; zero NServiceBus references.
- **Risks**: ⚠️ Biggest rewrite (not a port) — new deps `WolverineFx`, `WolverineFx.AzureServiceBus`, `WolverineFx.SqlServer`, EF integration — verify each package's license (Wolverine core is MIT; confirm transport/persistence packages at plan time); ⚠️ public API seam design (`UseSqlServer`) is a one-way door.
- **Status**: ⬜

### 15. Blob claim-check
- **Goal**: Messages over a size threshold transparently store payloads in Azure Blob and travel as references (AC-M4).
- **Source material**: old behavior via `NServiceBus.DataBus.AzureBlobStorage` usage in `Nihdi.Core.Configuration.NServiceBus\` — reimplemented as Wolverine middleware (no Wolverine built-in claim check).
- **Depends on**: 14
- **Migration decisions**: thin middleware in `Cloudstrap.Messaging.AzureBlob`; threshold + container configurable.
- **De-NIHDI items**: storage naming → explicit `BlobServiceUri` convention from deliverable 4.
- **Definition of done**: build/tests/format green; XML docs; AC-M4 covered (Azurite/emulated in tests); zero `Nihdi` identifiers.
- **Risks**: ⚠️ Custom middleware on Wolverine internals — pin Wolverine version range carefully; `Azure.Storage.Blobs` (MIT).
- **Status**: ⬜

### 16. Hangfire scheduler
- **Goal**: A consumer registers Hangfire with SQL storage and attribute/interface-based recurring-task discovery in one call.
- **Source material**: `Nihdi.Core.Configuration.Hangfire\` (+ old Core `Settings\Hangfire\*` moved here).
- **Depends on**: 1 (actual graph: Core only — not blocked by hosting or messaging)
- **Migration decisions**: free Hangfire tier only.
- **De-NIHDI items**: environment-taxonomy dashboard switches → explicit flags with environment-based defaults; naming.
- **Definition of done**: build/tests/format green; XML docs; recurring-task discovery covered by tests; **LGPL notice for Hangfire documented in package README**; zero `Nihdi` identifiers.
- **Risks**: ⚠️ Hangfire.* is LGPL v3 — docs note required (spec decision); `Scrutor` (MIT).
- **Status**: ⬜

### 17. YARP trusted-subsystem proxy
- **Goal**: A consumer configures a YARP-based trusted-subsystem forwarder (token exchange onward) with one call.
- **Source material**: `Nihdi.Core.Configuration.Proxy\` (references Common; `Yarp.ReverseProxy`).
- **Depends on**: 4
- **Migration decisions**: token-forwarding integrates with deliverable 9's helpers where applicable.
- **De-NIHDI items**: naming; neutral fixtures.
- **Definition of done**: build/tests/format green; XML docs; zero `Nihdi` identifiers.
- **Risks**: ⚠️ Auth-adjacent (forwarded credentials) — human review; `Yarp.ReverseProxy` (MIT).
- **Status**: ⬜

### 18. Hangfire dashboard proxy
- **Goal**: A consumer exposes the Hangfire dashboard through a proxy host securely.
- **Source material**: `Nihdi.Core.Configuration.Hangfire.Proxy\` (references Hangfire + Proxy; uses `InternalsVisibleTo` from Hangfire — re-establish or design a public seam).
- **Depends on**: 16, 17
- **Migration decisions**: none beyond parents.
- **De-NIHDI items**: naming.
- **Definition of done**: build/tests/format green; XML docs; zero `Nihdi` identifiers.
- **Risks**: dashboard auth path — human review.
- **Status**: ⬜

### 19. Dashboard backend
- **Goal**: A consumer mounts the ops-dashboard API: ASB queue peek/purge/retry, diagnostics, claims viewer — minus message decryption (dropped).
- **Source material**: `Nihdi.Core.Configuration.Dashboard\Dashboard.Contracts\` + `Dashboard.Api\` (references Core, NServiceBus→Messaging, Functional→LanguageExt, Contracts; `Azure.Messaging.ServiceBus`, `Scrutor`).
- **Depends on**: 1, 14
- **Migration decisions**: dashboard settings (from old Core `Settings\Dashboard\`) live HERE (inverted dependency broken); decryption feature deleted; messaging integration re-targeted from NServiceBus to Wolverine/ASB SDK.
- **De-NIHDI items**: naming; neutral fixtures; no internal queue-naming assumptions beyond the documented workload convention.
- **Definition of done**: build/tests/format green; XML docs; queue operations covered by tests (mocked ASB); zero `Nihdi` identifiers; zero decryption code.
- **Risks**: ⚠️ Largest product-layer surface; `Azure.Messaging.ServiceBus` (MIT); claims viewer touches auth data — human review.
- **Status**: ⬜

### 20. Dashboard UI (MudBlazor)
- **Goal**: A consumer drops dashboard Blazor components (Server and WASM hosts) rendering on plain MudBlazor.
- **Source material**: `Nihdi.Core.Configuration.Dashboard\Dashboard.Components.Shared\`, `Dashboard.Components\`, `Dashboard.Components.BlazorWasm\`.
- **Depends on**: 11, 13, 19
- **Migration decisions**: `Nihdi.Common.MudBlazor8.DesignSystem` → plain **MudBlazor** (MIT); `Newtonsoft.Json` → prefer System.Text.Json (minimize deps); Functional → LanguageExt.Core.
- **De-NIHDI items**: design-system identifiers; naming; neutral fixtures.
- **Definition of done**: build/tests/format green; XML docs; components render in the SUT apps (bUnit/E2E smoke); zero `Nihdi` identifiers.
- **Risks**: ⚠️ Design-system → MudBlazor restyle is UI-visible work, hard to test automatically; MudBlazor (MIT).
- **Status**: ⬜

### 21. Cookie consent
- **Goal**: A consumer adds a CSP-friendly cookie-consent UI (bundled orejime assets, no CDN) with one component.
- **Source material**: `Nihdi.Core.Configuration.CookieConsent\` (standalone Razor lib).
- **Depends on**: 0 (standalone — kept in product band by choice)
- **Migration decisions**: verify and ship orejime license attribution (spec Package Map note).
- **De-NIHDI items**: package metadata (`Company: NIHDI`, `nihdi` tags) → Cloudstrap/MIT; naming.
- **Definition of done**: build/tests/format green; XML docs; orejime license attribution included; zero `Nihdi` identifiers.
- **Risks**: ⚠️ Bundled JS license attribution (orejime — verify current license and NOTICE requirements).
- **Status**: ⬜

### 22. Analytics abstraction + Matomo
- **Goal**: A consumer registers `IAnalyticsTracker` (consent-gated via CookieConsent) with the Matomo adapter; no tracking before consent, endpoint always required.
- **Source material**: `Nihdi.Core.Configuration.Analytics.Matomo\` (JS-interop tracker; abstraction is new).
- **Depends on**: 21
- **Migration decisions**: split into `Cloudstrap.Analytics` (abstraction) + `Cloudstrap.Analytics.Matomo` (adapter); no default endpoint URL.
- **De-NIHDI items**: internal Matomo instance default removed (endpoint required); package metadata; naming.
- **Definition of done**: build/tests/format green; XML docs; `Track_WithoutConsent_DoesNothing`-class tests pass; zero `Nihdi` identifiers.
- **Risks**: low.
- **Status**: ⬜

### 23. Google Analytics adapter
- **Goal**: A consumer swaps in GA4 (gtag) behind the same `IAnalyticsTracker`.
- **Source material**: new code (spec Analytics section); pattern from deliverable 22.
- **Depends on**: 22
- **Migration decisions**: GA4 gtag adapter; consent-gating inherited from abstraction.
- **De-NIHDI items**: none (new code).
- **Definition of done**: build/tests/format green; XML docs; adapter conformance tests pass.
- **Risks**: low.
- **Status**: ⬜

### 24. Localization setup
- **Goal**: A consumer gets one-call culture-negotiation defaults + supported-culture config section over stock ASP.NET Core localization.
- **Source material**: ⚠️ **none in the reference repo** — `Nihdi.AspNetCore.Localization` is an internal package whose source is unavailable; this is a fresh thin implementation per spec (no custom engine).
- **Depends on**: 1
- **Migration decisions**: thin setup layer only; resource conventions documented.
- **De-NIHDI items**: none beyond naming (new code).
- **Definition of done**: build/tests/format green; XML docs; culture-negotiation defaults covered by tests.
- **Risks**: ⚠️ No source reference — scope must be pinned from the spec sentence alone; keep minimal.
- **Status**: ⬜

### 25. WASM SUT + E2E demonstration harness
- **Goal**: Every delivered Cloudstrap feature is demonstrable in a running Blazor WASM app (`src/Test/WasmTestProject`) and proven by an E2E test (NUnit 4 + Microsoft.Playwright) that boots the real Bff host and drives a real browser; the demonstration becomes a standing definition-of-done rule for all deliverables.
- **Plan**: `_plans/25-WasmTestProjectSut.md` (user-directed 2026-07-30; no spec — test infrastructure + process change, not a package port).
- **Source material**: source `Test\WasmTestProject` (layout inspiration — rebuilt neutral and trimmed; the committee-named `Cfe` host renamed to industry-standard `Bff`). Not a shipped package: no NuGet output, no XML-doc requirement (`src/Test` tree).
- **Depends on**: 1, 2 (the features it demonstrates).
- **Delivered so far**: 4 SUT projects (Contracts · Presentation/MudBlazor · Host.Wasm · Host.Bff) + `Cloudstrap.WasmTestProject.E2E.Tests` (11 tests): home page boot; Core demo (`/diagnostics` server binding, client-side WASM binding badge, fail-fast startup validation); Observability demo (tagged health probes, ambient correlation id, `AddDoctor` business span asserted in captured console telemetry); CI runs the E2E suite (Playwright install step in `ci.yml`); `.vscode` launch configs; `src/Test/Directory.Build.props` made SUT-aware (NUnit/MTP wiring only for `*.Tests`); test-only CPM pins MudBlazor 9.7.0 (MIT), Microsoft.Playwright 1.61.0 (Apache-2.0), Microsoft.AspNetCore.Components.* 10.0.10 (MIT).
- **Process artefacts**: planner rule 15 + interview/self-check items, plan-template demonstration-slice block, project-manager DoD + ✅-flip verification, CLAUDE.md workflow rule 9 + commands, tests.md E2E section, this roadmap's standing rule.
- **Risks**: ⚠️ CI change (Playwright browser install step) — CI-green proof pending the first push.
- **Status**: 🔨 — steps 1–5 done and gates 1–3 approved; final gate (process docs + CI green) pending.

## Change log

| Date | Change | Why |
|------|--------|-----|
| 2026-07-25 | Initial roadmap created (25 deliverables, 0–24), grounded in `<ProjectReference>` analysis of all source `.csproj` files. | Extraction kickoff. |
| 2026-07-25 | Reference-repo path corrected: agent briefing said `D:\Data\gv10141\Repos\Common\Nihdi-Core-Configuration` (does not exist); verified actual root `D:\source\Nihdi-Core-Configuration\Nihdi-Core-Configuration\src\`. | Ground truth from filesystem. |
| 2026-07-25 | Within the auth band, ClientCredentials (9) ordered before OpenIdConnect (10), diverging from the baseline listing. | Ordering rule 3 (risk early): first Duende ATM usage, and it feeds the typed-HttpClient seam used by BlazorServer/Proxy. |
| 2026-07-25 | Noted graph refinements: auth needs only Core; Hangfire needs only Core; BlazorCommon/BlazorWasm/CookieConsent/Analytics/Dashboard.Contracts/Testing are standalone. Band order kept for focus; constraints recorded so re-prioritization stays cheap. | Actual graph beats assumed bands. |
| 2026-07-25 | Cloudstrap.Testing given a flexible slot (#8) instead of a fixed band. | Old `Nihdi.Core.Testing` depends only on Functional (→ LanguageExt); first consumer determines timing. |
| 2026-07-25 | `_specs/0-RepoScaffolding.md` approved (zero Open Questions); §0 set to 📝, planner writing `_plans/0-RepoScaffolding.md`. Gate decisions folded into §0: NUnit 4 + NUnit3TestAdapter + NUnit.Analyzers on MTP (replaces MSTest v4); no StyleCop — SDK analyzers (`AnalysisLevel=latest-recommended`) + `EnforceCodeStyleInBuild`/`.editorconfig` + `dotnet format` gate; CS1591 on in `src/`, off under `src/Test/`; Central Package Management (`src/Directory.Packages.props`); previews from `dev` pushes only → GitHub Packages; stable via tag-push `v*` → nuget.org only; scheduled cleanup keeps last 20 preview versions per package. | User gate 2026-07-25. NUnit and no-StyleCop are user-directed deviations from the founding spec/CLAUDE.md — documentation amendments pending with the user (see spec "Documentation amendments required"). |
| 2026-07-25 | Spec/plan file-naming convention adopted: roadmap-number prefix — `_specs/<N>-<Deliverable>.md` / `_plans/<N>-<Deliverable>.md`. | One-glance traceability between roadmap number, spec, and plan; applies to all future deliverables. |
| 2026-07-25 | Aspire-coexistence posture adopted (founding-spec amendment, user-directed): zero `Aspire.*` references in shipped packages (AC-ASP2); §2 gains OTel owner/contribute modes (AC-ASP1); §4 gains resilience-handler tolerance + "Cloudstrap's or Aspire's, not both" KeyVault docs (AC-ASP3); `Cloudstrap.Aspire` is post-v1 only. | Coexist with Aspire on the shared substrate (`Microsoft.Extensions.*`, OTel .NET, Azure SDK) without inheriting Aspire's platform risk — Aspire-based apps become adopters instead of conflicts. |
| 2026-07-25 | `_specs/1-CoreSettingsModel.md` approved (both Open Questions resolved); §1 set to 📝, planner is the next step. **OQ-1**: root settings type named `CloudstrapOptions` with `GetCloudstrapOptions()` accessor (repo `*Options` convention); founding-spec amendment `CloudstrapConfiguration` → `CloudstrapOptions` approved and applied to `_specs/Cloudstrap.md` (founding-spec edits are user-owned — applied by the main agent, not the analyst). **OQ-2**: `AppRegistrationConfiguration` dropped outright — no Cloudstrap type models client-id/secret credentials in appsettings; deliverable 14 builds transport auth on `TokenCredential`/`DefaultAzureCredential` + standard `AZURE_*` env vars. Analyst finding folded into §1: the source `Nihdi.Core.Functional` ProjectReference is dead code (zero usage) — `Cloudstrap.Core` takes no LanguageExt.Core dependency; the type-mapping decision defers to the first genuine consumer (likely Testing or Dashboard). | User gate 2026-07-25. |
| 2026-07-26 | **Deliverable #1 (Cloudstrap.Core) → ✅.** `_plans/1-CoreSettingsModel.md` fully executed (5 steps, 3 🛑 gates, 19/19 boxes; gates approved 2026-07-26); AC-C1…AC-C10 + AC-ASP2 signed off. §1 gains a **Delivered** entry recording the shipped public surface, the Microsoft.Extensions-only / zero-`Aspire.*` / zero-`LanguageExt.*` dependency closure, and four facts later deliverables inherit: (1) `Microsoft.Extensions.Options.DataAnnotations` deliberately NOT taken — `[OptionsValidator]` source generation emits the `[Required]` checks (deviates from the spec's dependency table; user-confirmed at the Slice-2 gate); (2) get-only collection options mean configured values **append** to defaults rather than replace them (only `CorrelationRequestOptions.HealthEndpoints` ships non-empty defaults) — every consuming package inherits and must document this; (3) a broken section-level rule is reported twice at startup (root-relative + section-relative) — user-acknowledged; (4) the LanguageExt type-mapping decision stays open, deferred to the first genuine consumer. Overview row #2 marked **next**. | Deliverable complete and verified on disk (build 0/0, 49/49 tests, format exit 0, Release nupkg+snupkg clean). Facts 1–3 are cross-package conventions, so they belong at roadmap level rather than buried in one plan. |
| 2026-07-26 | **Deliverable #0 (Repo scaffolding) → ✅.** Status went 📝 → 🔨 → ✅ within the day: the roadmap first recorded it as 🔨 because six operational boxes of the plan's final 🛑 gate were open (nuget.org prefix reservation · GitVersion probe review · PR `ci.yml` run · `dev` preview-publish no-op · `cleanup-previews.yml` dispatch · optional `v0.1.0` tag · workflow code review), which gated the first *publish* rather than local development. The user closed all of them the same day and approved the gate — `_plans/0-RepoScaffolding.md` is now 100% complete. **The `Cloudstrap.` package ID prefix is reserved on nuget.org**; together with the active Trusted Publishing policy and the `NUGET_USER` secret, the publish path is fully open and the prefix-reservation risk on §0 is resolved. | Roadmap rule: ✅ only when the plan's final gate is checked `[x]` — verified box-by-box on disk before flipping. The foundation band (0 + 1) is now closed and nothing operational stands between a finished package and a feed. |
| 2026-07-26 | §2 source material re-verified file-by-file against the reference repo and expanded (DistributedTracing / Logging / Correlation / HealthChecks inventories, plus `Dynatrace\*` marked read-and-delete). Noted that `CorrelationHttpDelegatingHandler.cs` + `IHttpClientBuilderExtensions.cs` live in `Correlation\` — confirming the Observability-before-Extensions order — and that the Logging/OpenTelemetry/Correlation/HealthChecks **options types already ship in `Cloudstrap.Core`**, so #2 consumes rather than redefines them. §2 risks gain the explicit Aspire-overlap flag and a "do not regress Core's host-agnostic closure" note. | Pre-hand-off scope check for the next deliverable (ordering rule: verify against the source repo before scheduling). Two files the earlier entry omitted (`DeferredLoggerFactory.cs`, `NihdiConsoleFormatter.cs`) surfaced; the Core options boundary changed since the entry was written. |
| 2026-07-30 | **Deliverable #2 (Cloudstrap.Observability) → ✅.** `_plans/2-ObservabilityBase.md` fully executed (12 steps, 6 🛑 gates, all boxes checked; user accepted 2026-07-30); AC-O2/O3/O4, AC-ASP1/ASP2, AC-B1…AC-B13 (+ amended AC-C6) signed off at the final gate. §2 gains a **Delivered** entry recording the shipped surface and six inherited seams/facts: the `MarkExporterContributed()` guard seam (#3), the correlation delegating-handler seam (#4), the `CloudstrapHealthCheckTags` `"live"`/`"ready"` contract (#4/5/7/12), the AC-C6 Core amendment (`OTEL_EXPORTER_OTLP_ENDPOINT` satisfies the Otlp endpoint rule on both validation paths), the suite's first `Microsoft.AspNetCore.App` framework reference (OQ-1, one-package posture), and the first non-Microsoft deps (Serilog 4.4.0 + OpenTelemetry 1.17.0 families, CPM-pinned, zero `Azure.*`/`Aspire.*`). Overview row #3 marked **next** (its only dependency, 2, is ✅); reconciliation note refreshed to 2026-07-30. | Roadmap rule: ✅ only when the plan's final gate is checked `[x]` — verified box-by-box on disk (52/52 + 91/91 tests, zero warnings, format clean, Release nupkg verified). |
| 2026-08-01 | **Deliverable #25 added (user-directed) and executed to its final gate**: WASM SUT (`src/Test/WasmTestProject`, `Bff`+`Wasm` hosts) + Playwright E2E harness + **standing SUT-demonstration rule** added to the preamble (every deliverable's DoD includes a SUT demo + E2E test). §12 risk updated (E2E infra no longer lands there); §13 re-scoped (SUT exists; adds helpers/auth/Refit demos to it; `Cfe`→`Bff` naming). Roadmap edits applied by the main agent on user instruction (project-manager not invoked). | User directive: "every time you migrate a new feature, demonstrate how it works on this test project" — institutionalized via planner rule 15, CLAUDE.md workflow rule 9, project-manager DoD, plan-template block, tests.md E2E section. |
| 2026-07-25 | Hosting posture adopted (user-directed): supported matrix is **Azure Web Apps + containers/Kubernetes** (cloud-native) — on-prem IIS/VM hosting added to the founding spec's Non-Goals + Decisions Made. `_specs/1-CoreSettingsModel.md` amended post-approval: `IsRunningInAks()` verdict Redesign → **Drop** (no `CloudstrapEnvironment` helper ships); later packages expose explicit options wherever the source branched on the environment. technical-analyst instructions gain the matching drop-heuristic for legacy-hosting accommodations. | The source's K8s check was a cloud-AKS-vs-on-prem proxy that mis-classifies Azure Web Apps (no `KUBERNETES_SERVICE_HOST`); explicit, overridable options beat un-overridable environment sniffing. |
