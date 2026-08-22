# Spec: Azure Monitor Exporter — `Cloudstrap.Observability.AzureMonitor` (Roadmap Deliverable #3)

> **Approved 2026-08-01 — zero Open Questions remain; spec is planner-ready.** All five gate questions were resolved per this spec's recommendations (see the Decision Log at the end): `AddAzureMonitor` chained on `CloudstrapObservabilityBuilder` (OQ-1), Azure's rate-limited 5-traces/sec default inherited (OQ-2), config-level `UseDefaultAzureCredential` flag with the `Azure.Identity` dependency (OQ-3), AC-B10 preserved via the export-time scrub-processor amendment to the base package (OQ-4), and no Live Metrics in v1 — per-signal exporter path with documented escape hatches (OQ-5).
>
> Sources: `_plans/ROADMAP.md` §3 (hand-off brief) and §2 "Delivered" (the seams #2 left) · `_specs/Cloudstrap.md` (Decisions Made, Observability Migration §1–§4, AC-O1…AC-O4, Aspire Coexistence AC-ASP1–AC-ASP3, De-NIHDI-fication Checklist) · `_specs/2-ObservabilityBase.md` (AC-B7/AC-B10, Decision Log OQ-3) · **shipped** code in `src/Cloudstrap.Observability/` (`CloudstrapObservabilityBuilder`, `ExporterContributionMarker`, `AzureMonitorContributionGuard`, `OpenTelemetryPipeline`, `OtlpExporterSetup`, `CloudstrapObservabilityOptions`) and `src/Cloudstrap.Core/` (`OpenTelemetryOptions`, `OpenTelemetryOptionsValidator`) · source reference repo (read-only): `Common\DistributedTracing\ServiceCollectionExtensions.cs` (mode plumbing, `GetOtlpHeaders` Api-Token helper) and `Common\Dynatrace\*` (5 files — the exporter wiring this deliverable replaces) · external evidence on `Azure.Monitor.OpenTelemetry.Exporter` gathered 2026-08-01: [package README on MS Learn](https://learn.microsoft.com/en-us/dotnet/api/overview/azure/monitor.opentelemetry.exporter-readme?view=azure-dotnet) (doc date 2026-07-24, references release 1.8.3), the [public API surface file](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/monitor/Azure.Monitor.OpenTelemetry.Exporter/api/Azure.Monitor.OpenTelemetry.Exporter.netstandard2.0.cs), [`AzureMonitorExporterExtensions.cs`](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/monitor/Azure.Monitor.OpenTelemetry.Exporter/src/AzureMonitorExporterExtensions.cs), [`OpenTelemetryBuilderExtensions.cs`](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/monitor/Azure.Monitor.OpenTelemetry.Exporter/src/OpenTelemetryBuilderExtensions.cs), the [CHANGELOG](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/monitor/Azure.Monitor.OpenTelemetry.Exporter/CHANGELOG.md), and the [deprecated `OpenTelemetry.Extensions.AzureMonitor` NuGet page](https://www.nuget.org/packages/OpenTelemetry.Extensions.AzureMonitor/).
>
> **⚠️ Risk areas this deliverable touches** — **new third-party dependencies** `Azure.Monitor.OpenTelemetry.Exporter` and `Azure.Identity` (both MIT — Decision Log OQ-3) · **auth-adjacent** (Entra ID credential attachment to the telemetry channel — human review at the plan gates) · **public API shape** that sets the pattern for every future exporter/extension leaf chaining off a Cloudstrap builder (Decision Log OQ-1) · an **amendment to the shipped `Cloudstrap.Observability` package** (Decision Log OQ-4; precedent: the AC-C6 Core amendment carried by deliverable 2's plan) · the **AC-O2 regression guard** (the base package must keep loading zero Azure assemblies in Otlp/Console mode).

## Code-reading findings that shaped this spec

1. **Core ships no connection-string or sampling-rate settings.** `OpenTelemetryOptions` (shipped, deliverable 1) carries `Mode`, `Endpoint`, `Headers`, per-signal `Enable*` flags, `EnableBlazorHubTracing` and `AlwaysOnSampler` — nothing Azure-specific. Per the roadmap's per-feature-settings precedent (old Core's feature folders move to their owning packages), the Azure Monitor settings are a **new section owned by this package**: `Cloudstrap:AzureMonitor`. Core is not amended.
2. **The exporter installs its own sampler.** `AddAzureMonitorTraceExporter` calls `SetSampler(...)` at registration time: `RateLimitedSampler(TracesPerSecond)` when `TracesPerSecond` is set, else `ApplicationInsightsSampler(SamplingRatio)`; **since 1.6.0-beta.2 (2026-01-12) the default is rate-limited sampling at 5 traces/second**, not 100%. `SetSampler` is last-wins and this package registers after the base package, so in `AzureMonitor` mode the shipped `BlazorHubSampler` wrap and the `ParentBased(AlwaysOn)` chain are silently replaced — AC-B10 (no Blazor hub spans) would regress without a deliberate countermeasure (resolved: Decision Log OQ-4), and the sampling default changes from "everything" to "5/sec" (resolved: Decision Log OQ-2).
3. **The AI samplers cannot be composed around.** `ApplicationInsightsSampler`/`RateLimitedSampler` are **not public** in the exporter's API surface, and the one community package that ever exposed them (`OpenTelemetry.Extensions.AzureMonitor`) is **deprecated by its owner and beta-only** (1.0.0-beta.4). Wrapping the AI sampler in `BlazorHubSampler` is therefore not implementable; suppression happens at export time instead (Decision Log OQ-4). Replacing the AI sampler with OTel's `TraceIdRatioBasedSampler` is not acceptable either: only the AI samplers stamp the sample rate that lets Application Insights renormalize counts in the portal.
4. **A newer cross-cutting API exists and was evaluated: `UseAzureMonitorExporter`** (added 1.4.0-beta.3, 2025-04-01 — *after* the founding spec named the per-signal methods). It is the only path to Live Metrics and trace-based log sampling, but it (a) calls `WithLogging().WithMetrics().WithTracing()` itself, force-enabling all three signals and defeating Core's shipped `EnableTracing/EnableMetrics/EnableLogs` contract, and (b) installs its sampler through a **deferred** `ConfigureOpenTelemetryTracerProvider` callback that overrides any sampler — including one a consumer set through the shipped `ConfigureTracing` "final say" hook. This spec keeps the founding spec's per-signal choice; the trade-off was surfaced at the gate and decided (Decision Log OQ-5 — no Live Metrics in v1).
5. **Standard (pre-aggregated) metrics need no bespoke code.** The exporter emits Application Insights standard metrics by default whenever the trace and metric exporters are registered together (since 1.0.0-beta.8; `EnableStandardMetrics` became a public opt-out in 1.7.0). The per-signal path loses nothing here.
6. **The exporter resolves `APPLICATIONINSIGHTS_CONNECTION_STRING` itself** (supported since 1.0.0-beta.8). Cloudstrap therefore mirrors the shipped OQ-2/AC-B13 pattern from deliverable 2: an explicit Cloudstrap setting wins; when absent, Cloudstrap leaves the exporter's `ConnectionString` unset and only *validates* that the standard variable is present — it never copies or re-implements the SDK's resolution.
7. **The source repo contains zero Azure Monitor code.** Its "vendor exporter" was Dynatrace riding two vehicles: OTLP with a hard-coded `Authorization=Api-Token …` header (`GetOtlpHeaders`) and Serilog HTTP sinks (`Common\Dynatrace\*`). Deliverable 2 dropped both; this deliverable ships the replacement capability (vendor backend for traces/metrics/logs) on Application Insights. The Port Decision Table below records that lineage — this package is new code, not a port.
8. **Contribute mode + Azure Monitor is a legitimate pairing, not a conflict.** Aspire ServiceDefaults registers an OTLP exporter only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set — an Aspire app targeting Application Insights has *no* exporter otherwise. A consumer explicitly calling this package's entry point in contribute mode adds the missing exporter to the host-owned pipeline; that is additive, not duplicative, so the call is allowed (and the README documents "don't also wire Azure Monitor in ServiceDefaults — one owner for the App Insights exporter").

---

## User Story

**As an** ASP.NET Core developer deploying to Azure,
**I want to** flip `Cloudstrap:OpenTelemetry:Mode` to `AzureMonitor`, provide a connection string (or let the platform's `APPLICATIONINSIGHTS_CONNECTION_STRING` do it), and add one chained call in `Program.cs`,
**So that** the traces, metrics and logs my app already produces through `UseCloudstrapObservability` land in Application Insights — correlated by operation ID, sampled affordably, authenticated with Entra ID when my organization requires it — without the base package ever loading an Azure assembly for consumers who chose OTLP instead.

---

## Acceptance Criteria

> AC-O1, AC-O2 and AC-ASP2 are carried **verbatim** from the founding spec. AC-O1 is this deliverable's headline criterion; per the roadmap, it is verified against a real Application Insights resource as a **documented manual step** — unit and E2E tests mock at the boundary and never touch live Azure. AC-AM1…AC-AM12 are new, spec-specific criteria; the gate decisions they rested on (OQ-2 sampling default, OQ-3 credential flag, OQ-4 scrub mechanism) are all resolved — see the Decision Log.

| # | Given | When | Then |
|---|-------|------|------|
| AC-O1 | Mode `AzureMonitor` + valid connection string | App handles a request | Request trace, dependency spans, logs, and runtime metrics appear in Application Insights, correlated by operation ID. *(carried verbatim; manual verification procedure documented in the package README)* |
| AC-O2 | Mode `Otlp` + collector endpoint | App handles a request | Same telemetry arrives at the OTLP collector; **no Azure dependency loaded**. *(carried verbatim — re-proven with this package present in the solution: the base package's dependency closure and loaded-assembly set stay Azure-free)* |
| AC-ASP2 | Any shipped Cloudstrap package | Its dependency closure is inspected | Zero `Aspire.*` packages. *(carried verbatim)* |
| AC-AM1 | Mode `AzureMonitor`, the entry point called, `Cloudstrap:AzureMonitor:ConnectionString` set | The host starts | Startup succeeds (the AC-B7 guard is satisfied via `MarkExporterContributed()`); Azure Monitor exporters are registered for **exactly** the signals Core's `EnableTracing`/`EnableMetrics`/`EnableLogs` enable. |
| AC-AM2 | Mode `AzureMonitor`, no Cloudstrap connection-string setting, `APPLICATIONINSIGHTS_CONNECTION_STRING` present in configuration | The host starts | Startup succeeds; Cloudstrap leaves the exporter's `ConnectionString` unset so the Azure SDK resolves the standard variable itself. |
| AC-AM3 | Mode `AzureMonitor`, neither the setting nor the standard variable present | The host starts | Startup fails with a validation error naming both `Cloudstrap:AzureMonitor:ConnectionString` and `APPLICATIONINSIGHTS_CONNECTION_STRING` — telemetry is never silently dropped. |
| AC-AM4 | Mode `Otlp`, `Console` or `Disabled` | The entry point is called anyway | No-op: zero Azure Monitor registrations; the Otlp/Console/Disabled behavior is byte-identical to the base package alone. (This is what makes an unconditional call in `Program.cs` + per-environment mode flipping work.) |
| AC-AM5 | Mode `AzureMonitor`, `Cloudstrap:OpenTelemetry:AlwaysOnSampler = true` | Traces flow | Every trace is sampled (fixed percentage 100%, sample rate stamped); any configured `SamplingRatio`/`TracesPerSecond` is ignored while the dev flag is set. |
| AC-AM6 | Mode `AzureMonitor`, `Cloudstrap:AzureMonitor:SamplingRatio = 0.25` | Traces flow | The Application Insights fixed-percentage sampler is active at 25% and exported spans carry the sample rate, so App Insights renormalizes counts in the portal. |
| AC-AM7 | Both `SamplingRatio` and `TracesPerSecond` set | The host starts (entry point called) | Startup fails with a validation error naming both settings as mutually exclusive. |
| AC-AM8 | `Cloudstrap:AzureMonitor:UseDefaultAzureCredential = true`, or a `TokenCredential` supplied through the configure hook | Exporters are registered | The exporter options carry the credential (`DefaultAzureCredential` from the flag; a hook-supplied credential wins over the flag). Verified with boundary mocks — no live token acquisition in tests. *(Decision Log OQ-3)* |
| AC-AM9 | Mode `AzureMonitor`, `EnableBlazorHubTracing = false` (default) | A Blazor Server `ComponentHub` invocation happens | No hub span is exported to Azure Monitor; a normal request in the same app is still exported (AC-B10 parity in the flagship mode, via the export-time scrub processor — Decision Log OQ-4). |
| AC-AM10 | The entry point called twice on the same builder | The host starts | One set of exporter registrations — no duplicate telemetry (idempotent, matching the base package's posture). |
| AC-AM11 | A fresh clone with this package | Build, test run, `dotnet format --verify-no-changes`, case-insensitive search for `Nihdi`, `Riziv`, `Dynatrace` | All green; XML docs on all public API; package metadata complete (description, tags, README, icon); every dependency OSI-licensed and CPM-pinned; zero forbidden identifiers. |
| AC-AM12 | The WASM SUT's Bff host configured with Mode `AzureMonitor` and a syntactically valid dummy connection string (offline storage disabled via the hook) | The E2E suite runs | The SUT boots (guard lifted) and ≥ 1 E2E test proves the demonstration through the running app (standing workflow rule 9); the package README documents the AC-O1 manual verification steps against a real App Insights resource. |

---

## Port Decision Table

This package is **new code** (founding spec Package Map: "— (new) → `Cloudstrap.Observability.AzureMonitor`"). The table therefore has two parts: (1) the source features whose *capability* this deliverable replaces — their Drop verdicts were recorded by deliverables 1–2; this deliverable closes the loop by shipping the replacement — and (2) the candidate integration surfaces of the replacement library itself, each accepted or rejected on evidence. Nothing is specced that was not read.

### 1. Source features whose capability this package replaces

| Source type / feature | Verdict (recorded in) | Target here | Justification |
|---|---|---|---|
| `Common\DistributedTracing\ServiceCollectionExtensions.GetOtlpHeaders` — hard-coded `Authorization=Api-Token {AccessToken}` | **Replace** (dropped in #2) | Connection-string ingestion auth + optional Entra ID `TokenCredential` on `AzureMonitorExporterOptions` | The Dynatrace-shaped token header is the source's only exporter "auth". App Insights ingestion auth is the connection string, hardened by AAD when the resource enforces Entra-only ingestion — both first-class in the exporter library; zero bespoke auth code. |
| `Common\DistributedTracing\ServiceCollectionExtensions` — per-signal `IsOtlp` exporter branches | **Redesign** (shipped in #2 as the mode seam) | This package fills the `AzureMonitor` arm: per-signal `AddAzureMonitorTraceExporter` / `MetricExporter` / `LogExporter` + `MarkExporterContributed()` | The founding spec names exactly these methods (Observability Migration §1). The shipped seam (`CloudstrapObservabilityBuilder` + AC-B7 guard) was built for this package; no base redesign needed. |
| `Common\Dynatrace\DynatraceExtensions` (Serilog `WriteTo.Dynatrace`/`DurableDynatrace` HTTP sinks) | **Replace** (dropped in #2, AC-O4) | OTel log pipeline (already shipped) + `AddAzureMonitorLogExporter` | Runtime logs already flow MEL → OTel log provider (founding decision §3); attaching the Azure Monitor log exporter to that one pipeline replaces a vendor Serilog sink, its formatter pair and its bespoke HTTP client with a single registration. |
| `Common\Dynatrace\DynatraceOptions` | **Replace** (dropped in #2) | `AzureMonitorOptions` bound from `Cloudstrap:AzureMonitor` | The capability "configure the vendor backend" survives with a minimal surface: connection string, sampling, credential flag. Period/batch/minimum-level knobs are owned by the OTel batch processor and MEL levels — not re-exposed. |
| `Common\Dynatrace\DynatraceTextFormatter` / `DynatraceBatchFormatter` | **Drop** (in #2) | — | Vendor wire-format shaping; the Azure Monitor exporter owns its wire format. Nothing to replace. |
| `Common\Dynatrace\DynatraceHttpClient` (incl. `AllowInvalidCertificates`) | **Drop** (in #2) | — | Bespoke transport with an insecure-TLS switch. The Azure.Core HTTP pipeline owns transport; TLS is never optionalized in an MIT library. |
| Old Core `Settings\Logging\DynatraceConfiguration.cs` (`Logging:Dynatrace` section) | **Replace** (deleted in #1) | `Cloudstrap:AzureMonitor` section | The "vendor backend settings" slot, renamed and reduced. Old `AccessToken`/ingest-URL become `ConnectionString` (+ optional credential); no token material in a header string. |
| `BootstrapLoggerFactory` Dynatrace branch (pre-host export to the vendor) | **Drop** (in #2) | — deliberately **not** resurrected | Pre-host logs stay console/file only (deliverable 2, finding 4 there). No pre-host Application Insights export: the platform collects stdout on every supported hosting target. |
| Old Core `OpenTelemetryConfiguration.BaseUri`/`AccessToken` | **Redesign** (shipped in #1 as `Endpoint`/`Headers`) | Unused in `AzureMonitor` mode | An App Insights connection string is not an OTLP endpoint; conflating them would misuse a shipped setting. `Endpoint`/`Headers` remain Otlp-mode-only; this package ignores them. |

### 2. Integration surfaces of `Azure.Monitor.OpenTelemetry.Exporter` — accepted / rejected

| Candidate | Verdict | Justification |
|---|---|---|
| Per-signal `AddAzureMonitorTraceExporter` / `AddAzureMonitorMetricExporter` / `AddAzureMonitorLogExporter` | **Take (Replace bespoke exporter wiring)** | The founding-spec-named seam. Respects Core's shipped per-signal `Enable*` gates; standard metrics come for free (finding 5); registration order keeps the consumer hooks meaningful. |
| `UseAzureMonitorExporter` (cross-cutting, 1.4.0-beta.3+) | **Reject** *(Decision Log OQ-5)* | Force-enables all three signals (`WithLogging().WithMetrics().WithTracing()`), defeating Core's `EnableTracing/EnableMetrics/EnableLogs`; installs its sampler via deferred callback that overrides even the consumer's shipped `ConfigureTracing` final say. Its exclusive gains (Live Metrics + trace-based log sampling) were weighed at the gate and deferred past v1. |
| `Azure.Monitor.OpenTelemetry.AspNetCore` (the distro, `UseAzureMonitor`) | **Reject** | The distro *owns* the whole pipeline — resource, instrumentation, exporters — which is `Cloudstrap.Observability`'s job. Adopting it would duplicate or fight every shipped base behavior (owner/contribute modes, noise filters, resource identity). Wrong seam by design, not by quality. |
| `OpenTelemetry.Extensions.AzureMonitor` (community `ApplicationInsightsSampler`) | **Reject** | Deprecated by its owner, beta-only (1.0.0-beta.4) — fails the maintenance bar and would trip `NU5104` under `TreatWarningsAsErrors`. Evidence: NuGet deprecation notice, 2026-08-01. |
| `Microsoft.ApplicationInsights.AspNetCore` (classic SDK / `TelemetryClient`) | **Reject** | Legacy pillar the founding spec's OTel decision supersedes; would introduce a second, non-OTel telemetry model. |
| `Azure.Identity` (`DefaultAzureCredential` for the config-level AAD flag) | **Take** *(Decision Log OQ-3)* | MIT, Microsoft-maintained, and the founding spec's credential convention; operationalizes Entra-only ingestion as a per-environment setting with no code change. Inevitable in the suite at deliverable 4 (KeyVault) regardless. |

### 3. New surface introduced by this deliverable (each item traces to a roadmap/founding-spec decision — no gold-plating)

| New item | Mandated by |
|---|---|
| Entry point `AddAzureMonitor` on `CloudstrapObservabilityBuilder` *(Decision Log OQ-1)* | Roadmap §3 goal; AC-B7 seam contract (`MarkExporterContributed`). |
| `AzureMonitorOptions` (`ConnectionString`, `SamplingRatio`, `TracesPerSecond`, `UseDefaultAzureCredential`) + validator | Roadmap §3 migration decisions: "connection string from setting or `APPLICATIONINSIGHTS_CONNECTION_STRING`; AAD credential support; expose fixed-rate sampling". `TracesPerSecond` is included because Azure's *default* policy is rate-limited sampling — exposing the ratio but not the rate would leave the default policy untunable. `UseDefaultAzureCredential` per Decision Log OQ-3. |
| Blazor-hub suppression parity in `AzureMonitor` mode (internal, base amendment) | AC-B10/AC-O3 preservation in the flagship mode — Decision Log OQ-4. |

---

## Public API Sketch

Namespace **`Cloudstrap.Observability.AzureMonitor`**. Everything `public sealed` unless stated; implementations `internal`. The Azure type `AzureMonitorExporterOptions` appears deliberately in the configure hook — this package's whole purpose is Azure Monitor, and hiding the vendor options behind a wrapper would only truncate capabilities (`StorageDirectory`, `DisableOfflineStorage`, `EnableStandardMetrics`, …) while adding code to own. This mirrors the base package exposing `Action<OtlpExporterOptions>`.

```text
Cloudstrap.Observability.AzureMonitor
├── CloudstrapObservabilityBuilderExtensions (static)
│     AddAzureMonitor(this CloudstrapObservabilityBuilder builder,       [Decision Log OQ-1]
│                     Action<AzureMonitorExporterOptions>? configure = null)
│         : CloudstrapObservabilityBuilder
│       — the one entry point, chained off UseCloudstrapObservability(...).
│         Mode ≠ AzureMonitor → returns the builder untouched (AC-AM4).
│         Mode = AzureMonitor → binds + validates Cloudstrap:AzureMonitor, registers the
│         per-signal exporters for the signals Core enables, applies the sampling policy,
│         attaches the credential, calls MarkExporterContributed(), and invokes the
│         consumer hook LAST per signal so it always has the final say. Idempotent.
│
└── AzureMonitorOptions                       — config section Cloudstrap:AzureMonitor
      const SectionName = "Cloudstrap:AzureMonitor"
      ConnectionString          : string?     — wins when set; else the standard
                                                APPLICATIONINSIGHTS_CONNECTION_STRING must be
                                                present (validated; SDK resolves it itself)
      SamplingRatio             : float?      — fixed-percentage sampling, 0.0–1.0
      TracesPerSecond           : double?     — rate-limited sampling; mutually exclusive
                                                with SamplingRatio (validation error)
      UseDefaultAzureCredential : bool = false — Entra ID ingestion auth via
                                                DefaultAzureCredential  [Decision Log OQ-3]

internal: AzureMonitorOptionsValidator (IValidateOptions<AzureMonitorOptions>, consults
IConfiguration for the standard variable — same pattern as Core's OpenTelemetryOptionsValidator),
exporter/sampling registration internals.
```

**Base-package amendment (no public surface change — Decision Log OQ-4):** in owner mode with `Mode = AzureMonitor`, `Cloudstrap.Observability` skips its `SetSampler` call (it would be overridden by the exporter's sampler anyway) and, when `EnableBlazorHubTracing = false`, registers an internal export-time scrub processor that prevents `ComponentHub` spans from reaching export processors. Zero `Azure.*` references — AC-O2 unaffected. Scheduled inside this deliverable's plan, precedent: deliverable 2 carried the AC-C6 Core amendment.

**Configuration** — this package owns exactly one new section, `Cloudstrap:AzureMonitor` (repo rule: one subsection per package). Core's `CloudstrapOptions` is not extended: the per-feature-settings precedent (roadmap discrepancy note 1) keeps feature sections in their owning packages, and Core must stay Azure-free in name and content. Everything else this package consumes is Core's shipped `Cloudstrap:OpenTelemetry` section (`Mode`, `Enable*` flags, `AlwaysOnSampler`, `EnableBlazorHubTracing`).

---

## Behaviors & Conventions

| Behavior | Default | Override |
|---|---|---|
| Activation | The entry point is inert unless `Cloudstrap:OpenTelemetry:Mode = AzureMonitor` — call it unconditionally in `Program.cs` and let per-environment configuration decide the backend (Otlp collector in dev, App Insights in prod). | `Cloudstrap:OpenTelemetry:Mode`. |
| Signal selection | Exporters registered per Core's shipped flags: `EnableTracing` → trace exporter, `EnableMetrics` → metric exporter, `EnableLogs` → log exporter on the base package's OTel log pipeline. All three off + Mode `AzureMonitor` → nothing exported, host still starts (the consumer said so explicitly; the guard is satisfied). | `Cloudstrap:OpenTelemetry:Enable*`. |
| Connection string | `Cloudstrap:AzureMonitor:ConnectionString` wins when set. When unset, Cloudstrap configures nothing and the Azure SDK resolves the standard `APPLICATIONINSIGHTS_CONNECTION_STRING` itself (finding 6 — "speak the platform's conventions", same posture as the shipped OTLP endpoint rule AC-B13). Neither present → startup fails (AC-AM3). Sovereign clouds work unchanged: the connection string carries its own `IngestionEndpoint`. | The setting; the environment variable; `configure` hook (`ConnectionString`) has the final say. |
| Sampling | Neither setting configured → the exporter's platform default, **rate-limited sampling at 5 traces/second** *(Decision Log OQ-2)* — cost-protective, and App Insights renormalizes counts from the stamped sample rate. The default is stated loudly in the README and the XML docs. `SamplingRatio` switches to fixed-percentage; `TracesPerSecond` tunes the rate limit; both set → validation error (AC-AM7). | `Cloudstrap:AzureMonitor:SamplingRatio` / `TracesPerSecond`; `configure` hook. |
| Dev sampling flag | `Cloudstrap:OpenTelemetry:AlwaysOnSampler = true` → fixed percentage 100% (every trace, sample rate stamped 100). It overrides both sampling settings while set — it is a diagnosis flag, not a policy (AC-AM5). | Unset the flag. |
| Sampler ownership | In `AzureMonitor` mode the Application Insights sampler owns the sampling decision (it is the only sampler that stamps App-Insights-compatible sample rates — finding 3). The base package's `ParentBased(AlwaysOn)`/`BlazorHubSampler` chain does not apply in this mode; `CloudstrapObservabilityOptions.ApplySampler` is irrelevant here. | A consumer who insists on a custom sampler applies it after this package's registration via OTel's deferred `ConfigureOpenTelemetryTracerProvider` — documented in the README with the count-renormalization caveat. |
| Blazor hub spans | `EnableBlazorHubTracing = false` (default) keeps suppressing `ComponentHub` spans in `AzureMonitor` mode via the base amendment's export-time scrub *(Decision Log OQ-4)*. Caveat documented: under **rate-limited** sampling, hub invocations still consume traces-per-second budget before being scrubbed — Blazor Server apps should prefer `SamplingRatio`. | `Cloudstrap:OpenTelemetry:EnableBlazorHubTracing = true`. |
| Ingestion auth | Connection-string (local) auth. `UseDefaultAzureCredential = true` → `DefaultAzureCredential` attached (works identically on Azure Web Apps, containers and dev machines — founding-spec credential posture; Decision Log OQ-3). A `TokenCredential` set through the `configure` hook always wins over the flag. | The flag; the hook. |
| Standard metrics, performance counters, offline storage | Left at the Azure SDK's defaults (standard metrics on when traces+metrics both export; offline storage enabled to a temp directory). Cloudstrap adds no knobs it would then have to own. | `configure` hook: `EnableStandardMetrics`, `EnablePerformanceCounters`, `StorageDirectory`, `DisableOfflineStorage`. |
| Console exporter alongside | Unchanged base behavior: `EnableConsole` still adds the console exporter next to Azure Monitor. | `Cloudstrap:OpenTelemetry:EnableConsole`. |
| Cloud role & operation ID | The exporter maps the shipped resource identity automatically: `service.name` (= `WorkloadName`) → cloud role name, W3C trace-id → `operation_Id` (AC-O1's correlation). No Cloudstrap code. | `ConfigureResource` on the base package. |
| Contribute mode | Calling the entry point in contribute mode is **allowed** and adds the App Insights exporters to the host-owned pipeline — the documented pattern for an Aspire app targeting App Insights (finding 8). README states the one-owner rule: wire the App Insights exporter in ServiceDefaults *or* through Cloudstrap, not both (AC-ASP1's no-duplicate-exporters concern). Note: the exporter's sampler applies here too. | Don't call it; the host keeps full ownership. |
| Failure isolation | Export failures never crash the app: the exporter retries with offline buffering (its EventSource `OpenTelemetry-AzureMonitor-Exporter` is the diagnostic channel). Only *configuration* errors fail startup — never data-path errors. | — (by design). |
| Validation | Registered by the entry point (the package is inert otherwise): connection-string presence rule applies only in `AzureMonitor` mode; `SamplingRatio` range (0–1) and the mutual-exclusion rule apply whenever the section is bound, so a typo is caught even before the mode flips in production. Fails at host startup through the same options pipeline Core uses. | Fix the configuration. |

**Test strategy (spec-level):** NUnit 4 on Microsoft.Testing.Platform in `src/Test/UnitTest/Cloudstrap.Observability.AzureMonitor.Tests`. All tests mock at the boundary: exporter registration is asserted through the built service provider and OTel provider inspection with a **dummy connection string** (`InstrumentationKey=00000000-0000-0000-0000-000000000000`) — no live Azure, no token acquisition (AC-O1 is the documented manual step). The AC-O2 regression is asserted from the base package's side (dependency closure + loaded-assembly check in Otlp mode, with this package present in the solution). The demonstration slice (AC-AM12) extends the WASM SUT's Bff host — which already calls `UseCloudstrapObservability()` — with the chained entry point and an `AzureMonitor`-mode configuration (dummy connection string, offline storage disabled via the hook), plus ≥ 1 Playwright E2E test.

**Planner verification notes** (facts to prove with RED tests during implementation, not open design questions): (a) the per-signal DI path picks up `APPLICATIONINSIGHTS_CONNECTION_STRING` when Cloudstrap leaves `ConnectionString` unset; (b) the mechanics of attaching `AddAzureMonitorLogExporter` to the base package's already-configured OTel log pipeline; (c) the scrub processor's reliance on export processors skipping non-`Recorded` activities, on the CPM-pinned OpenTelemetry version.

---

## Dependencies

| Package | License | Evidence & justification |
|---|---|---|
| `Azure.Monitor.OpenTelemetry.Exporter` | MIT | Founding-spec decision (do not reopen). Verified 2026-08-01: latest stable **1.8.3** (2026-07-24), release cadence 1–3 months, Microsoft-maintained in `Azure/azure-sdk-for-net`. Brings `Azure.Core` transitively. Replaces all bespoke exporter/auth/wire-format code (Port Decision Table §1). |
| `Azure.Identity` | MIT | **Taken** (Decision Log OQ-3): backs the config-level `UseDefaultAzureCredential` flag. Microsoft-maintained; arrives in the suite at deliverable 4 (KeyVault) regardless. |
| `Cloudstrap.Observability` *(project reference)* | MIT | The seam this package fills (`CloudstrapObservabilityBuilder`, the AC-B7 guard, the OTel pipeline). One-way reference — the base never references back (AC-O2). |
| *(test only)* `OpenTelemetry.Exporter.InMemory` | Apache-2.0 | Already pinned; span assertions without a collector. |

Considered and **rejected** (evidence in Port Decision Table §2): `Azure.Monitor.OpenTelemetry.AspNetCore` (distro — wrong seam), the `UseAzureMonitorExporter` API path (Decision Log OQ-5), `OpenTelemetry.Extensions.AzureMonitor` (deprecated, beta-only), classic Application Insights SDK (legacy), any `Aspire.*` package (AC-ASP2).

---

## Deliberate Behavior Changes (vs. the source library)

1. **The vendor telemetry backend is Application Insights, not Dynatrace** (founding-spec decision): no `Api-Token` header, no vendor Serilog sink, no bespoke HTTP client. Backend logs travel through the one OTel log pipeline.
2. **Default sampling in the vendor mode changes from "everything" to Azure's rate-limited 5 traces/second** *(Decision Log OQ-2)* — the source exported every trace to Dynatrace; App Insights counts stay accurate through stamped sample rates. `AlwaysOnSampler` remains the everything-switch.
3. **Sampler ownership changes in `AzureMonitor` mode**: the Application Insights sampler replaces the base chain; Blazor hub suppression moves from sampler-wrap to export-time scrub in this mode *(Decision Log OQ-4)*.
4. **Configuration errors fail startup at well-defined points** (missing connection string, conflicting sampling settings) instead of the source's pattern of a runtime warning inside the logging pipeline.
5. **No pre-host export to the backend** — reaffirming deliverable 2's change: bootstrap/crash logs go to console/file only; the source shipped pre-`Build()` logs to Dynatrace.
6. **No insecure-TLS switch** — the source's `AllowInvalidCertificates` has no successor of any kind.

---

## Edge Cases

| Case | Expected behavior |
|------|-------------------|
| Mode `AzureMonitor`, package never called | Base package's shipped AC-B7 guard fails startup naming this package (unchanged). |
| Mode `AzureMonitor`, entry point called, all three `Enable*` flags false | Host starts (guard satisfied — the consumer explicitly disabled the signals); nothing exported. |
| `ConnectionString` set to empty/whitespace | Treated as absent — the AC-AM3 rule applies. |
| Both the Cloudstrap setting and the standard variable present | The Cloudstrap setting wins (it is set explicitly on the exporter options; the SDK only falls back to the variable when the option is unset). |
| Entry point called twice | Idempotent — one registration set (AC-AM10). |
| Contribute mode + entry point called | Allowed: exporters added to the host-owned pipeline; README documents the one-owner rule for the App Insights exporter. |
| Dummy/unreachable connection string (tests, SUT) | Host starts and serves; the exporter retries in the background — export failures are never startup failures. SUT config disables offline storage so test runs leave no residue. |
| `SamplingRatio` outside 0.0–1.0 | Validation error at startup (whenever the section is bound — even if the current mode is not `AzureMonitor`). |

---

## Out of Scope (this deliverable — the planner must not resurrect any of it)

- Everything in the founding spec's global Out of Scope: message encryption, MessagingBridge, Dynatrace, ServicePlatform/ServicePulse, `Cloudstrap.Functional`, `Cloudstrap.Aspire`.
- **Live Metrics and trace-based log sampling** — only available through `UseAzureMonitorExporter`/the distro, which this spec rejects (finding 4, Decision Log OQ-5). Escape hatches documented in the README: contribute mode with the consumer's own `UseAzureMonitorExporter` call, or the distro outright. Revisit post-v1 / if Azure decouples Live Metrics from the cross-cutting API.
- `Azure.Monitor.OpenTelemetry.AspNetCore` (distro), the classic Application Insights SDK, `TelemetryClient`, telemetry initializers/processors of the classic model.
- Application Insights profiler, snapshot debugger, availability (web) tests, workspace/portal provisioning, ingestion sampling overrides configured server-side.
- Pre-host (bootstrap) log export to Application Insights.
- Any new health check, middleware, or configuration beyond the one `Cloudstrap:AzureMonitor` section.
- Automated tests against live Azure (AC-O1 is a documented manual verification; roadmap decision).

---

## Decision Log (gate answers, 2026-08-01 — zero Open Questions remain; spec is planner-ready)

All five gate questions were answered by the user on 2026-08-01; each accepted this spec's recommendation. The full findings/options/rationale for each question live in this repo's git history of this file (the pre-gate draft); the decided outcomes are:

| Question | Answer (user, 2026-08-01) |
|---|---|
| OQ-1 — Entry-point name: `AddAzureMonitor` on `CloudstrapObservabilityBuilder`, or `AddCloudstrapAzureMonitor`? | **(A) `AddAzureMonitor`** chained on `CloudstrapObservabilityBuilder`. The `AddCloudstrap<Feature>` naming rule applies to extensions on framework types (`IServiceCollection` etc.), where the prefix buys discoverability; extensions on Cloudstrap-owned builders drop the redundant prefix. This is the **builder-chained-leaf precedent** for every future leaf that chains off a Cloudstrap builder. |
| OQ-2 — Default sampling in `AzureMonitor` mode when neither `SamplingRatio` nor `TracesPerSecond` is configured? | **(A) Inherit Azure's platform default** — rate-limited sampling at 5 traces/second (the exporter's own default since 1.6.0-beta.2). Cost-protective; App Insights renormalizes counts from stamped sample rates; consistent with the Azure documentation consumers read. Consumers raise volume via `Cloudstrap:AzureMonitor:SamplingRatio` (or `TracesPerSecond`); the README and XML docs state the default prominently. |
| OQ-3 — Config-level Entra ID flag (and the `Azure.Identity` dependency), or code-hook-only AAD? | **(A) Ship the flag**: `UseDefaultAzureCredential : bool = false` on `AzureMonitorOptions`, backed by an `Azure.Identity` (MIT, Microsoft-maintained) reference in this leaf package only; a `TokenCredential` supplied through the `configure` hook always wins over the flag. Operationalizes the founding spec's `DefaultAzureCredential` convention as a per-environment setting. Auth surface flagged for human review at the plan gates. |
| OQ-4 — Blazor hub suppression (AC-B10 parity) in `AzureMonitor` mode? | **(B) Preserve AC-B10 via the base-package amendment**: in owner-mode `AzureMonitor`, `Cloudstrap.Observability` skips its `SetSampler` (the exporter's sampler would override it anyway) and, when `EnableBlazorHubTracing = false`, registers an internal export-time scrub processor — sampler-independent hub suppression, zero `Azure.*` references in the base (AC-O2 intact). Scheduled inside this deliverable's plan (AC-C6 precedent). Documented caveat: under rate-limited sampling, hub invocations still consume traces-per-second budget before being scrubbed; Blazor Server apps should prefer `SamplingRatio`. |
| OQ-5 — Live Metrics posture? | **(A) No Live Metrics in v1** — stay on the founding spec's per-signal exporter path, which honors Core's shipped `EnableTracing/EnableMetrics/EnableLogs` contract and the shipped consumer-hooks-run-last contract. The README documents the limitation and the escape hatches (contribute mode with the consumer's own `UseAzureMonitorExporter` call, or the distro outright). Revisit post-v1. |
