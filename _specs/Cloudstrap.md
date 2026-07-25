# Spec: Cloudstrap — Open-Source Extraction of Nihdi.Core.Configuration

> **Source reference**: file paths in this spec (e.g. `Common/DistributedTracing/...`) refer to the private source repository `Nihdi.Core.Configuration`, locally at `D:\source\Nihdi-Core-Configuration` (read-only reference for the extraction).

## User Story

**As a** .NET developer deploying ASP.NET Core applications to Azure,
**I want to** bootstrap a production-ready application (configuration, observability, auth, messaging, health, background jobs) with a few opinionated extension-method calls,
**So that** I get enterprise-grade defaults (OpenTelemetry + Application Insights, KeyVault config, correlation, hardened middleware) without assembling and maintaining 20+ packages myself.

---

## Decisions Made

| Topic | Decision |
|-------|----------|
| Name / NuGet prefix | **Cloudstrap** (`Cloudstrap.*`) — verified free on nuget.org (exact IDs and prefix) on 2026-07-24. Reserve the ID prefix on nuget.org early. |
| License | **MIT** |
| Telemetry backend | **Application Insights via Azure Monitor OpenTelemetry exporter**, keeping the existing vendor-neutral OTel pipeline. Generic OTLP and Console modes remain as options. Dynatrace support is removed entirely. |
| Messaging | **Wolverine** (JasperFx) replaces NServiceBus — Azure Service Bus + SQL Server transports, EF Core transactional inbox/outbox, native OpenTelemetry. |
| Auth / token management | Stock `Microsoft.AspNetCore.Authentication.*` + **Duende.AccessTokenManagement** (Apache-2.0) replaces the internal `Nihdi.AspNetCore.*` suite. |
| Analytics | Provider abstraction with **Matomo** (open-source, self-hostable — the privacy-friendly default) and **Google Analytics 4** adapters. No default endpoint URL. |
| v1 scope | Full surface: core bootstrap + observability + WebApi/Worker + Blazor suite + Messaging + Hangfire/YARP proxy + Dashboard + CookieConsent + Analytics + Localization. |
| Repository | **github.com/geobarteam/Cloudstrap** (public, MIT). Local working folder: `D:\Data\gv10141\Private\Cloudstrap`. Fresh history — no NIHDI commits. |
| Message encryption | **Dropped permanently.** Transport-level security (TLS + ASB encryption at rest) is the documented baseline. The Dashboard's message-decryption feature is dropped with it. |
| Dashboard scope | **Full port** (ASB queue peek/purge/retry, diagnostics, claims viewer) minus the decryption feature; internal design system replaced by plain MudBlazor. |
| Localization | **Ported** as `Cloudstrap.Localization` — thin setup layer over stock ASP.NET Core localization (culture negotiation defaults, one-call registration), no custom localization engine. |
| Messaging durability store | **SQL Server only in v1**, but behind a storage-provider seam (`AddCloudstrapMessaging(...).UseSqlServer(...)`) so PostgreSQL can be added later without breaking the API. |
| Functional primitives | **Not ported.** The hand-rolled `Nihdi.Core.Functional` (`Result<T>`, `Option`, `Preconditions`) is replaced by the **LanguageExt.Core** NuGet package (MIT) as a direct dependency of consuming packages — there is no `Cloudstrap.Functional` package. Exact type mapping (e.g. `Fin<T>` / `Either<Error, T>` for success/failure, `Option<T>`, `Unit`) is settled when the first consuming package is planned. Decided 2026-07-25. |
| Aspire posture | **Coexist without depending.** Zero `Aspire.*` package references in any shipped v1 package — Cloudstrap builds on the same substrate Aspire does (`Microsoft.Extensions.*`, OpenTelemetry .NET, Azure SDK) and must compose cleanly inside an Aspire app (see the **Aspire Coexistence** section, AC-ASP1–AC-ASP3). Deeper integration, if demand ever justifies it, is one optional post-v1 leaf package `Cloudstrap.Aspire`. Decided 2026-07-25. |

---

## Goals

1. A newcomer can go from `dotnet new` to a fully instrumented, Azure-deployable app with < 10 lines in `Program.cs` and one config section.
2. Zero references to NIHDI/RIZIV-INAMI infrastructure, names, conventions, or internal NuGet feeds.
3. No commercial or source-unavailable dependencies (NServiceBus, Dynatrace, internal packages all removed).
4. Opinionated but overridable: every convention (naming, environments, correlation header) has a sensible default and a documented override.

## Non-Goals

- Not a fork kept in sync with Nihdi.Core.Configuration — Cloudstrap is a one-time extraction that evolves independently. NIHDI may later consume Cloudstrap and layer internal conventions on top, but that is a separate effort.
- No multi-cloud abstraction: Azure is the opinionated target (KeyVault, Blob, Service Bus, Application Insights). The OTLP mode is the escape hatch for other backends, not a support commitment.
- No backward compatibility with existing `Nihdi:*` configuration sections or `*ForNihdi` APIs.

---

## Aspire Coexistence

Aspire and Cloudstrap sit on the same substrate — `Microsoft.Extensions.Configuration/DependencyInjection/HealthChecks`, OpenTelemetry .NET, the Azure SDK. That substrate predates Aspire and survives it; Cloudstrap targets the substrate directly. Coexistence with Aspire is a **docs-and-design posture, not a dependency**:

1. **No Aspire references.** No shipped package references any `Aspire.*` package. If demand ever justifies deeper integration, it becomes one optional leaf `Cloudstrap.Aspire` (post-v1, user-approved) — quarantined the same way `Cloudstrap.Observability.AzureMonitor` quarantines the exporter.
2. **Composable, not conflicting.** The one real collision point is OTel: an app using Aspire's ServiceDefaults already has a tracer/meter/logger pipeline. `UseCloudstrapObservability` therefore supports two documented modes: **owner** (default — Cloudstrap wires the full pipeline) and **contribute** (Cloudstrap adds only its differentiated pieces — samplers, noise filters, enrichment, `IBusinessTrace` — to the existing pipeline; OTel's builder API is additive by design; no duplicate exporters). Likewise the typed `HttpClient` registration must tolerate resilience handlers already applied via `ConfigureHttpClientDefaults` (no stacked resilience), and KeyVault-configuration docs state "use Cloudstrap's or Aspire's, not both" (secret-prefix filtering is Cloudstrap's differentiator).
3. **Speak the platform's conventions.** Support standard `ConnectionStrings:` names and well-known environment variables (`APPLICATIONINSIGHTS_CONNECTION_STRING`) where sensible; register health checks through the stock `IHealthChecksBuilder` (inherently additive). Cloudstrap features then drop into an Aspire solution without ceremony.
4. **A sample, not a reference.** "Cloudstrap in an Aspire app" ships as a docs page plus a sample AppHost — Aspire packages appear only in that sample project, never in a shipped package.

### Acceptance Criteria — Aspire Coexistence

| # | Given | When | Then |
|---|-------|------|------|
| AC-ASP1 | An app with an existing OTel pipeline (Aspire ServiceDefaults-style) | `UseCloudstrapObservability` runs in contribute mode | Cloudstrap samplers/filters/enrichment apply to the existing pipeline; no second exporter, no duplicate spans. |
| AC-ASP2 | Any shipped Cloudstrap package | Its dependency closure is inspected | Zero `Aspire.*` packages. |
| AC-ASP3 | Resilience handlers already applied via `ConfigureHttpClientDefaults` | `AddCloudstrapHttpServiceClient<TI,TImpl>` registers a typed client | The client works; Cloudstrap does not stack a second resilience layer. |

---

## Package Map (old → new)

| Nihdi package | Cloudstrap package | Notes |
|---|---|---|
| Nihdi.Core.Functional | — *(not ported)* | Replaced by the **LanguageExt.Core** NuGet dependency (MIT); consuming packages reference it directly. |
| Nihdi.Core.Configuration | `Cloudstrap.Core` | Settings model → `CloudstrapConfiguration`, section `Cloudstrap:`. **Break the inverted dependency on Dashboard.Contracts** — dashboard settings move to the dashboard package. |
| Nihdi.Core.Configuration.Common | `Cloudstrap.Extensions` (config/KeyVault/HTTP) + `Cloudstrap.Observability` (Serilog, OTel, correlation) | Split the grab-bag: observability is the flagship feature and deserves its own package. |
| — (new) | `Cloudstrap.Observability.AzureMonitor` | Azure Monitor exporter wiring, isolated so the base package stays exporter-agnostic. |
| Nihdi.Core.Configuration.WebApi | `Cloudstrap.WebApi` | Versioning, NSwag/Scalar, middleware. |
| Nihdi.Core.Configuration.Mvc | `Cloudstrap.Mvc` | Session hardening, correlation. |
| Nihdi.Core.Configuration.Worker | `Cloudstrap.Worker` | Health listener port becomes configurable (default 9000). |
| Nihdi.Core.Configuration.OpenIdConnect | `Cloudstrap.Authentication.OpenIdConnect` | Rebuilt on stock OIDC handler + Duende ATM. |
| Nihdi.Core.Configuration.OAuth | `Cloudstrap.Authentication.ClientCredentials` | Rebuilt on Duende ATM client-credentials. |
| Nihdi.Core.Configuration.BlazorServer | `Cloudstrap.BlazorServer` | |
| Nihdi.Core.Configuration.BlazorWasm | `Cloudstrap.BlazorWasm` | Already standalone; rename only. |
| Nihdi.Core.Configuration.BlazorCommon | `Cloudstrap.BlazorCommon` | Already standalone; rename only. |
| Nihdi.Core.Configuration.NServiceBus | `Cloudstrap.Messaging` | Wolverine-based; see Messaging Migration. |
| Nihdi.Core.Configuration.Hangfire (+Proxy) | `Cloudstrap.Hangfire` (+`.Proxy`) | Free Hangfire tier only (LGPL noted in docs). |
| Nihdi.Core.Configuration.Proxy | `Cloudstrap.Proxy` | YARP trusted-subsystem forwarder. |
| Nihdi.Core.Configuration.CookieConsent | `Cloudstrap.CookieConsent` | Verify/ship orejime license attribution. |
| Nihdi.Core.Configuration.Analytics.Matomo | `Cloudstrap.Analytics` + `Cloudstrap.Analytics.Matomo` + `Cloudstrap.Analytics.GoogleAnalytics` | Provider abstraction (`IAnalyticsTracker`); endpoint/site ID always required, no defaults. |
| Nihdi.Core.Configuration.Dashboard.* | `Cloudstrap.Dashboard.*` | Full port; replace internal MudBlazor design system with plain MudBlazor (MIT). Message-decryption feature dropped (encryption is dropped). |
| — (from `Nihdi.AspNetCore.Localization`) | `Cloudstrap.Localization` | Thin one-call setup over stock ASP.NET Core localization: request-culture negotiation defaults, supported-culture config section, resource conventions. No custom engine. |
| Nihdi.Core.Testing | `Cloudstrap.Testing` | Rename only. |

API naming: `AddNihdiX`/`AddXForNihdi` → `AddCloudstrapX` / `UseCloudstrapX` (e.g. `AddCloudstrapWebApi`, `UseCloudstrapObservability`, `AddCloudstrapMessaging`).

---

## De-NIHDI-fication Checklist

Everything below is removed or generalized (source: full codebase analysis, 2026-07-24):

| Item | Today | Cloudstrap |
|---|---|---|
| KeyVault name | Hard-coded enterprise KeyVault naming convention (`Common/Extensions/IHostApplicationBuilderExtensions.cs`) | `Cloudstrap:KeyVault:VaultUri` (required when enabled); optional secret-prefix filter, defaulting to `Application:WorkloadName`. |
| Storage account | Hard-coded enterprise storage-account naming + blob URI convention | `Cloudstrap:Storage:BlobServiceUri` explicit setting; container name defaults to `Application:SystemName`, overridable. |
| NServiceBus license paths on internal file shares | `NServiceBusConfiguration.cs` | Gone with NServiceBus. |
| Log path `D:\logsint` + machine-name instance parsing | `FileConfiguration.cs`, `NihdiConfigurationExtensions.cs` | Default file logging off; when on, path required. Drop machine-name digit parsing. |
| Environment taxonomy LOC/DEV/TST/VAL/PRD | `EnvironmentConstants.cs`, drives license/installer/Hangfire switches | Use standard ASP.NET Core environments (`Development`/`Staging`/`Production`) + a documented `Cloudstrap:Application:EnvironmentTier` for orgs with more tiers. Behavior switches (topology auto-provisioning, dashboards) key off explicit flags with environment-based defaults. |
| Correlation header `NIHDI.Correlation` | `CorrelationHeader.cs` | Default `X-Correlation-ID`, configurable; W3C `traceparent` remains the tracing backbone. |
| OTel resource attributes `nihdi.*` | `NihdiResourceAttributes.cs` | `cloudstrap.*` prefix (or standard `service.*` semconv where one exists). |
| Matomo default pointing to an internal instance | `MatomoConfiguration.cs` | No default; endpoint required. |
| Probe path `/probe.aspx` | BlazorServer extensions + trace filter | Standard `/healthz` (liveness) + `/ready`; paths configurable. |
| ASB topic `nihdi-default-bundle` | `AzureServiceBusTransportConfiguration.cs` | Wolverine conventions; overridable topic naming. |
| Internal NuGet feed | `nuget.config` (internal feed only) | nuget.org. |
| `company="Riziv-Inami"` headers | All files | New MIT header / SPDX identifier. |
| Internal packages (`Nihdi.AspNetCore.*`, `Nihdi.Core.Health`, `Nihdi.Core.NServiceBus.Cryptography`, `Nihdi.StyleCop.MsBuildProperties`, `Nihdi.Common.MudBlazor8.DesignSystem`) | Private feed | Stock ASP.NET auth + Duende ATM · `Microsoft.Extensions.Diagnostics.HealthChecks` + `AspNetCore.HealthChecks.*` · dropped/reimplemented (encryption) · inlined build props · plain MudBlazor. |
| Test fixtures with internal hostnames and project names | Unit tests | Neutral example values (`example.com`, `contoso`). |
| Kept opinion: workload naming `{system}-{subsystem}-{type}` | Implicit convention | Kept, documented, and overridable — drives queue names, KeyVault secret prefix, table prefixes. |

---

## Observability Migration (Dynatrace → Application Insights)

The OTel pipeline in `Common/DistributedTracing/ServiceCollectionExtensions.cs` ports unchanged (samplers, noise filters, `BlazorHubSampler`, enrichment, `IBusinessTrace`). Changes:

1. **Modes** become `Disabled | Console | Otlp | AzureMonitor` (`Cloudstrap:OpenTelemetry:Mode`). `AzureMonitor` uses `Azure.Monitor.OpenTelemetry.Exporter` (`AddAzureMonitorTraceExporter` / `MetricExporter` / `LogExporter`) configured by `ConnectionString` (setting or `APPLICATIONINSIGHTS_CONNECTION_STRING`), with AAD credential support.
2. **Delete** `Common/Dynatrace/*`, `DynatraceConfiguration.cs`, the `Logging:Dynatrace` section, the Dynatrace branch in `BootstrapLoggerFactory`, and the `Api-Token` OTLP header helper (generic OTLP keeps a configurable headers dictionary instead).
3. **Serilog stays** for bootstrap/console/file logging; runtime log export goes through OTel logs to Application Insights (or OTLP).
4. Sampling: expose Azure Monitor's fixed-rate sampling setting; keep `AlwaysOnSampler` flag for dev.

### Acceptance Criteria — Observability

| # | Given | When | Then |
|---|-------|------|------|
| AC-O1 | Mode `AzureMonitor` + valid connection string | App handles a request | Request trace, dependency spans, logs, and runtime metrics appear in Application Insights, correlated by operation ID. |
| AC-O2 | Mode `Otlp` + collector endpoint | App handles a request | Same telemetry arrives at the OTLP collector; no Azure dependency loaded. |
| AC-O3 | Health probe or `_blazor` static request | Tracing active | No span exported (noise filters preserved). |
| AC-O4 | Any mode | Solution is searched for "Dynatrace" | Zero occurrences. |

---

## Messaging Migration (NServiceBus → Wolverine)

`AddCloudstrapMessaging(builder, options)` configures a Wolverine node from `Cloudstrap:Messaging:*`.

| NServiceBus capability | Cloudstrap/Wolverine equivalent |
|---|---|
| Transports: ASB, SQL Server, Learning | Wolverine ASB + SQL Server transports; in-memory ("local") transport for dev replaces Learning. |
| Suffix conventions `*Command/*Event/*Message` | Wolverine message routing conventions; keep suffix-based defaults. |
| SQL persistence + outbox (table prefix `{WorkloadName}_`) | Wolverine durable inbox/outbox on SQL Server; schema/prefix from workload name. Storage provider behind a seam (`UseSqlServer(...)`) — SQL Server only in v1, PostgreSQL planned without API break. |
| `TransactionalCommandExecutor` + `AddNServiceBusSynchronizedDbContext<T>` | Wolverine EF Core integration (`IDbContextOutbox`) — handler DbContext and outgoing messages commit atomically. Public API: `AddCloudstrapTransactionalMessaging<TDbContext>()`. |
| Retries (immediate + delayed) | Wolverine error-handling policies from the same config shape. |
| ServicePlatform connector, audit/error queue conventions | **Dropped.** Replaced by native Wolverine OTel metrics/traces → Application Insights; dead-letter queue conventions `{system}-error` kept. |
| Property-level encryption (internal crypto package) | **Dropped permanently.** Transport-level security (TLS + ASB encryption at rest) documented as the baseline. |
| Claim check / DataBus (Azure Blob) | Thin blob-offload middleware in `Cloudstrap.Messaging.AzureBlob` (payloads over a size threshold stored to Blob, reference travels in the message) — Wolverine has no built-in claim check. |
| MessagingBridge (SQL↔ASB migration) | **Dropped** — NIHDI-migration-specific. |
| UniformSession | Not needed — Wolverine's `IMessageBus` is uniform. |
| Correlation pipeline behaviors | Wolverine middleware propagating the configurable correlation header; W3C traceparent flows via OTel regardless. |
| License handling | None — Wolverine core is OSS. |
| Installers (queue auto-creation in dev) | `AutoProvision` flag, defaulting on in `Development`. |

### Acceptance Criteria — Messaging

| # | Given | When | Then |
|---|-------|------|------|
| AC-M1 | ASB transport configured | Handler publishes an event | Subscriber endpoint receives it; span linked across endpoints in App Insights. |
| AC-M2 | Transactional messaging with EF DbContext | Handler throws after staging entity + outgoing message | Neither the row nor the message is committed (outbox atomicity). |
| AC-M3 | In-memory transport, no Azure resources | Full test suite runs | All messaging tests pass locally with no network. |
| AC-M4 | Message exceeds blob-offload threshold | Message sent | Payload lands in Blob, message carries reference, consumer reads it transparently. |

---

## Auth Replacement

- `Cloudstrap.Authentication.OpenIdConnect`: `AddCloudstrapOpenIdConnect` — stock OIDC handler, secure cookie defaults, PKCE, token refresh via Duende ATM user-token management.
- `Cloudstrap.Authentication.ClientCredentials`: `AddCloudstrapClientCredentials` — Duende ATM client-credentials token client with caching/renewal; feeds the typed `HttpClient` registration (`AddCloudstrapHttpServiceClient<TI,TImpl>`) and proxy-forwarding helpers.
- `Cloudstrap.WebApi`: `AddCloudstrapJwtBearer` — stock JWT bearer with hardened defaults (audience validation on, clock skew reduced, HTTPS metadata required outside Development).
- Blazor WebAssembly browser-auth pattern (cookie auth + XSRF + `BffAuthenticationStateProvider`) ports as-is — it has no internal dependencies.

### Acceptance Criteria — Auth

| # | Given | When | Then |
|---|-------|------|------|
| AC-A1 | OIDC configured against any standards-compliant IdP (test: Keycloak container) | User signs in | Auth code + PKCE flow completes; tokens managed/refreshed by Duende ATM. |
| AC-A2 | Client-credentials HttpClient registered | Two calls 1 h apart with 5-min token lifetime | Token transparently renewed; no 401s. |
| AC-A3 | Solution searched for `Nihdi.AspNetCore` | — | Zero references. |

---

## Analytics

`Cloudstrap.Analytics` defines `IAnalyticsTracker` (page views, events, consent gating — integrates with `Cloudstrap.CookieConsent`: no tracking before consent). Adapters:

- `Cloudstrap.Analytics.Matomo` — recommended default (open-source, self-hostable, GDPR-friendly); ports the existing JS-interop tracker, endpoint URL required.
- `Cloudstrap.Analytics.GoogleAnalytics` — GA4 gtag adapter for teams already on GA.

---

## Repository & Delivery

- Public GitHub repository: `https://github.com/geobarteam/Cloudstrap.git`; local working folder `D:\Data\gv10141\Private\Cloudstrap`. Fresh history (no NIHDI commit history leaks).
- GitVersion + tags on `main` for SemVer, `-preview.N` on `dev` — same model as today.
- GitHub Actions: build, test (MSTest v4 / Microsoft.Testing.Platform), format check, pack, publish to nuget.org on tag. SourceLink + symbol packages.
- Reserve the `Cloudstrap.` ID prefix on nuget.org before first publish.
- Docs: README per package + docs site (e.g. docfx) with a "zero to deployed on Azure" quick-start; sample app replacing TestProject/WasmTestProject as public samples. Include a "Cloudstrap in an Aspire app" docs page + sample AppHost — Aspire packages appear only in that sample project (see Aspire Coexistence).
- StyleCop + `TreatWarningsAsErrors` carried over (build props inlined, no internal package).

---

## Out of Scope

- Migration tooling or compatibility shims for existing NIHDI consumers.
- NServiceBus bridge scenarios and ServicePlatform/ServicePulse integration.
- Multi-cloud providers (AWS/GCP) — OTLP mode is the only escape hatch.
- Multi-targeting: `net10.0` only.
- Property-level message encryption and the Dashboard message-decryption feature (dropped permanently; transport-level security is the baseline).
- PostgreSQL messaging durability (planned post-v1; the v1 API reserves the provider seam).
- `Cloudstrap.Aspire` integration package — post-v1 option only if demand justifies it; v1 ships zero `Aspire.*` references (see Aspire Coexistence).

---

## Open Questions

_All resolved 2026-07-24 — see Decisions Made._
