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
| 0 | Repo scaffolding | `src/Cloudstrap.sln`, `Directory.Build.props` (SDK analyzers, no StyleCop), `Directory.Packages.props` (CPM), `.editorconfig`, `GitVersion.yml`, `global.json`, `nuget.config`, CI workflows | — | 📝 | `_plans/0-RepoScaffolding.md` |
| 1 | Core settings model | Cloudstrap.Core | 0 | 📝 | `_specs/1-CoreSettingsModel.md` (spec) |
| 2 | Observability base | Cloudstrap.Observability | 1 | ⬜ | — |
| 3 | Azure Monitor exporter | Cloudstrap.Observability.AzureMonitor | 2 | ⬜ | — |
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

Reconciled with reality 2026-07-25: `src/` contains no `.csproj` yet and `_plans/` was
empty before this file — every deliverable is honestly ⬜.

## Deliverable details

### 0. Repo scaffolding
- **Goal**: A contributor can clone Cloudstrap, run `dotnet build src/Cloudstrap.sln`, tests, and format checks, and CI enforces all three — before any package exists.
- **Spec**: `_specs/0-RepoScaffolding.md` — approved 2026-07-25, zero Open Questions; its Decision Log and file inventory are authoritative for this deliverable.
- **Source material**: `Nihdi.Core.Configuration.sln` (solution layout only); source `Directory.Build.props` + `.editorconfig` (conventions to redesign — everything StyleCop-related is dropped, see spec Port Decision Table); `Test\TestProject\src\nuget.config` (`<clear/>` + single-feed pattern). No `GitVersion.yml` or GitHub workflows exist in the source repo (Azure DevOps) — author fresh per spec.
- **Depends on**: —
- **Migration decisions**: GitVersion + tags on `main`, `-preview.N` on `dev`; **NUnit 4 + NUnit3TestAdapter + NUnit.Analyzers on Microsoft.Testing.Platform** (user gate decision — deviates from the founding spec's MSTest v4; founding-spec amendment still pending with the user); **no StyleCop** — `Nihdi.StyleCop.MsBuildProperties` dropped outright (supersedes the earlier "reproduce its effect inline" instruction), replaced by .NET SDK analyzers (`AnalysisLevel=latest-recommended`), `EnforceCodeStyleInBuild` + `.editorconfig` as the single style authority, `TreatWarningsAsErrors` kept, `dotnet format --verify-no-changes` as the style gate; CS1591 XML-doc enforcement on in `src/`, off under `src/Test/`; **Central Package Management** (`src/Directory.Packages.props`); SourceLink via the SDK built-in (no package reference); publishing model: `-preview.N` packages from `dev` pushes only → **GitHub Packages**, stable via tag-push `v*` (`release.yml`) → **nuget.org only**, scheduled `cleanup-previews.yml` keeps the last 20 preview versions per package; `net10.0` only.
- **De-NIHDI items**: internal NuGet feed → nuget.org (root `nuget.config`); `Nihdi.StyleCop.MsBuildProperties` + per-project `stylecop.json` (`companyName = Riziv-Inami`) dropped outright — no StyleCop at all, .NET SDK analyzers replace it; no company copyright headers — licensing carried by `LICENSE` + `PackageLicenseExpression`, no per-file headers.
- **Definition of done**: `src/Cloudstrap.sln` (near-empty) + `src/Directory.Build.props` + `src/Directory.Packages.props` + `src/.editorconfig` + `GitVersion.yml` + `global.json` + `nuget.config` + `.github/workflows/ci.yml`/`release.yml`/`cleanup-previews.yml` + package icon asset (file details owned by the spec's inventory); build/test/format green locally and in CI (placeholder NUnit test project proves the test leg); zero `Nihdi`/`NIHDI`/`Riziv` identifiers; spec AC-R1…AC-R11 met.
- **Risks**: ⚠️ Analyzer ruleset choices are fixed afterwards (CLAUDE.md: `Directory.Build.props` rules are immutable) — get them right here. ⚠️ Reserve the `Cloudstrap.` nuget.org ID prefix before first publish (verified free 2026-07-24 per spec).
- **Status**: 📝 — spec approved 2026-07-25; planner writing `_plans/0-RepoScaffolding.md`

### 1. Core settings model
- **Goal**: A consumer can bind and validate the `Cloudstrap:` configuration section into a typed `CloudstrapOptions` model (retrieved via `GetCloudstrapOptions()`).
- **Spec**: `_specs/1-CoreSettingsModel.md` — approved 2026-07-25, both Open Questions resolved; its verdict table (3 Port · 12 Redesign · 8 Drop · 25 Move-out) is authoritative for this deliverable.
- **Source material**: `Nihdi.Core.Configuration\` — `Settings\*` (Application, Correlation, Logging, OpenTelemetry, HealthChecks, HttpClient), `ConfigurationBuilderExtensions.cs`, `ConfigurationExtensions.cs`, `ConfigurationException.cs`, `EnvironmentConstants.cs`, `BootstrapConfiguration.cs`.
- **Depends on**: 0
- **Migration decisions**: section `Nihdi:` → `Cloudstrap:`; root settings type named **`CloudstrapOptions`** with `GetCloudstrapOptions()` accessor (**OQ-1** — repo `*Options` convention; founding-spec amendment `CloudstrapConfiguration` → `CloudstrapOptions` approved by the user, technical-analyst applying it to `_specs/Cloudstrap.md`); **`AppRegistrationConfiguration` dropped outright** (**OQ-2** — no Cloudstrap type models client-id/secret credentials in appsettings; deliverable 14 builds transport auth on `TokenCredential`/`DefaultAzureCredential` + standard `AZURE_*` environment variables); **cut the inverted Dashboard.Contracts reference** (dashboard settings move to deliverable 19); per-feature settings move out (`Settings\NServiceBus\` → 14, `Settings\Hangfire\` → 16, `Settings\Security\` → 9/10, `Settings\Swagger|Scalar\` → 5); delete `Settings\Logging\DynatraceConfiguration.cs` and Bridge settings; the source `Nihdi.Core.Functional` ProjectReference is **dead code — zero usage** in any source file, so new Core takes **no LanguageExt.Core dependency** (exact LanguageExt type-mapping decision defers to the first package that genuinely consumes Functional types — likely Testing or Dashboard); validation via `Microsoft.Extensions.Options` + DataAnnotations replaces the bespoke validation cascade; only `Microsoft.Extensions.*` dependencies — host-agnostic (no `Microsoft.AspNetCore.App` framework reference), WASM-loadable, a true leaf.
- **De-NIHDI items**: environment taxonomy LOC/DEV/TST/VAL/PRD → standard ASP.NET Core environments + `Cloudstrap:Application:EnvironmentTier`; drop machine-name/log-path parsing from settings; keep documented workload naming `{system}-{subsystem}-{type}` (overridable); neutral test fixture values.
- **Definition of done**: build/tests/format green; XML docs on all public API; package metadata complete; zero `Nihdi`/`Riziv` identifiers; validation failures surface through the Options + DataAnnotations pipeline per the spec; no reference to any Dashboard type; spec acceptance criteria met.
- **Risks**: ⚠️ Public API surface for every later package — shape mistakes propagate; ⚠️ deciding which settings stay in Core vs move out changes every later deliverable. (Resolved: the LanguageExt type-mapping decision no longer lands here — Core has zero LanguageExt dependency.)
- **Status**: 📝 — spec approved 2026-07-25 (both OQs resolved); next step: planner writes `_plans/1-CoreSettingsModel.md`

### 2. Observability base
- **Goal**: A consumer calls `UseCloudstrapObservability` to get Serilog bootstrap logging plus a vendor-neutral OTel traces/metrics/logs pipeline (modes `Disabled | Console | Otlp`, `AzureMonitor` enum reserved) with correlation and noise filtering.
- **Source material**: `Nihdi.Core.Configuration.Common\` — `DistributedTracing\*` (samplers, `BlazorHubSampler`, `NihdiResourceAttributes.cs`, `IBusinessTrace`), `Logging\*` (`BootstrapLoggerFactory.cs`, enrichers, `W3CTracingMiddleware.cs`, `NihdiConfigurationExtensions.cs`), `Correlation\*`, `HealthChecks\*`.
- **Depends on**: 1
- **Migration decisions**: Dynatrace removed entirely — delete `Common\Dynatrace\*` and the `BootstrapLoggerFactory` Dynatrace branch; generic OTLP keeps a configurable headers dictionary (no `Api-Token` helper); Serilog stays for bootstrap/console/file; runtime logs via OTel; `Nihdi.Core.Health` → `Microsoft.Extensions.Diagnostics.HealthChecks`. **Aspire coexistence**: `UseCloudstrapObservability` supports pipeline-**owner** (default) and **contribute** modes — contribute adds only samplers/noise filters/enrichment/`IBusinessTrace` to an existing (e.g. ServiceDefaults) OTel pipeline, no duplicate exporters (AC-ASP1).
- **De-NIHDI items**: correlation header `NIHDI.Correlation` → `X-Correlation-ID` (configurable); resource attributes `nihdi.*` → `cloudstrap.*`/standard semconv; probe path `/probe.aspx` → `/healthz` + `/ready` (configurable); log path `D:\logsint` default removed.
- **Definition of done**: build/tests/format green; XML docs; AC-O2 (Otlp mode, no Azure dependency loaded), AC-O3 (probe/`_blazor` noise filtered), AC-O4 (zero "Dynatrace" occurrences) verifiable; AC-ASP1 (contribute mode composes with a pre-existing OTel pipeline — no duplicate exporters) covered by test; zero `Nihdi` identifiers.
- **Risks**: ⚠️ Largest port of the foundation bands; ⚠️ new external deps: Serilog suite (Apache-2.0), OpenTelemetry.* (Apache-2.0) — versions to re-pin; ⚠️ splitting Common cleanly so Extensions (4) doesn't drag observability internals.
- **Status**: ⬜

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
- **Risks**: ⚠️ First E2E infrastructure (Playwright) lands here.
- **Status**: ⬜

### 13. Blazor WASM helpers (+ WasmTestProject SUT)
- **Goal**: A consumer bootstraps a WASM client with cookie auth (BFF pattern), XSRF, and Refit clients; repo gains the WASM SUT.
- **Source material**: `Nihdi.Core.Configuration.BlazorWasm\` (standalone; Refit, Components.Authorization); SUT inspiration: `Test\WasmTestProject\src\*`.
- **Depends on**: 11 (band choice; package itself is standalone)
- **Migration decisions**: browser-auth pattern (cookie + XSRF + `BffAuthenticationStateProvider`) ports as-is per spec; `Microsoft.Extensions.Localization` usage is stock — keep.
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
| 2026-07-25 | Hosting posture adopted (user-directed): supported matrix is **Azure Web Apps + containers/Kubernetes** (cloud-native) — on-prem IIS/VM hosting added to the founding spec's Non-Goals + Decisions Made. `_specs/1-CoreSettingsModel.md` amended post-approval: `IsRunningInAks()` verdict Redesign → **Drop** (no `CloudstrapEnvironment` helper ships); later packages expose explicit options wherever the source branched on the environment. technical-analyst instructions gain the matching drop-heuristic for legacy-hosting accommodations. | The source's K8s check was a cloud-AKS-vs-on-prem proxy that mis-classifies Azure Web Apps (no `KUBERNETES_SERVICE_HOST`); explicit, overridable options beat un-overridable environment sniffing. |
