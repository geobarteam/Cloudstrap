# Spec: Observability Base — `Cloudstrap.Observability` (Roadmap Deliverable #2)

> **Approved 2026-07-27 — zero Open Questions remain; spec is planner-ready.** All three gate questions were resolved per this spec's recommendations (see the Decision Log at the end): one package with the ASP.NET Core framework reference (OQ-1), `OTEL_EXPORTER_OTLP_ENDPOINT` accepted as an alternative to the Cloudstrap `Endpoint` setting (OQ-2 — carries a recorded amendment to `_specs/1-CoreSettingsModel.md`), and fail-fast when `Mode = AzureMonitor` has no exporter (OQ-3).
>
> Sources: `_plans/ROADMAP.md` §2 (hand-off brief, verified 2026-07-26) · `_specs/Cloudstrap.md` (Decisions Made, De-NIHDI-fication Checklist, Aspire Coexistence, Observability Migration + AC-O1…AC-O4, AC-ASP1…AC-ASP3) · `_specs/1-CoreSettingsModel.md` + the **shipped** code in `src/Cloudstrap.Core/` (options this package consumes, never redefines) · source reference repo (read-only) `D:\source\Nihdi-Core-Configuration\Nihdi-Core-Configuration\src\` — every type in the Port Decision Table was opened and read in full, plus its consumers across the source solution (WebApi, Mvc, Worker, BlazorServer, NServiceBus, test projects).
>
> **⚠️ Risk areas this deliverable touches** — public API surface consumed by nearly every later package (`UseCloudstrapObservability`, `IBusinessTrace`, the correlation abstractions and the delegating-handler seam deliverable 4 consumes) · **new third-party dependency families** (Serilog and OpenTelemetry, both Apache-2.0 — the repo's first non-Microsoft references) · **the first `Microsoft.AspNetCore.App` framework reference** in a Cloudstrap package (decided at the gate: one package carries it — Decision Log OQ-1) · **host startup ordering** (logging providers, options binding, the exporter seam) · the clean split of the source's `Common\` so deliverable 4 inherits no observability internals.

## Code-reading findings that shaped this spec

1. **The correlation feature is incomplete in the source's `Common` package.** `Common\Correlation\` ships the accessor, the context, the generator and the *validation* middleware — but nothing that **establishes** a correlation id. The two components that do live in other packages and disagree with each other: `WebApi\Correlation\CorrelationMiddleware.cs` reads the inbound header and never generates one; `Mvc\Correlation\CorrelationSourceMiddleware.cs` always generates a new id and **ignores the inbound header entirely** — an MVC app therefore silently drops its caller's correlation id. Cloudstrap collapses ingress + validation into one middleware in this package.
2. **`W3CTracingMiddleware` has a live bug and no remaining purpose.** It only parses the inbound `traceparent` when `tracestate` is *also* present (`&&` on lines 37–38), otherwise it fabricates a random `ActivityContext`; it then writes a response `traceparent` with a hard-coded `-01` sampled flag that does not correspond to any real span, and pushes `trace_id`/`span_id` into Serilog's `LogContext` from that fabricated context. ASP.NET Core already creates the real `Activity` from `traceparent`, so the values it logs can disagree with the exported spans.
3. **`TracingEnricher` is an empty no-op** (`Enrich` body is a comment) — dead public API in the source.
4. **`BootstrapLoggerFactory`'s OTLP branch exists to ship a pre-`Build()` crash log to the collector**, and needs a documented workaround (a custom `HttpClientFactory`) to survive its own dispose-flush (lines 133–141). On every supported Cloudstrap hosting target the console stream is collected by the platform, so this branch buys very little for its complexity.
5. **`NihdiConfigurationExtensions.GetLogFilePath`** is the machine-name-digit-parsing convention the De-NIHDI checklist deletes (`Regex.Matches(hostName, @"\d+")` → `api01-.log`). It is also the only consumer of `Nihdi.Core.Functional` in this folder (`Preconditions.NotNull`). Nothing in the ported surface resurrects either.
6. **`BootstrapLoggerFactory` branches on `NihdiConfiguration.IsRunningInAks()` three times** (console format, file logging, backup logger). That helper was **dropped** in deliverable 1 (founding-spec hosting posture); all three branches collapse to a single unconditional behavior here.
7. **`HealthChecks\ServiceCollectionExtensions.AddApiLivenessHealthCheck` belongs to deliverable 4, not here** — it reads `HttpClientServiceRegistry` (Core's `HttpClients`), registers a named `HttpClient`, and exists to health-check a *typed client's* base address. `HttpClientServiceOptions.EnableHealthCheck`/`HealthCheckPrefix` (already shipped in Core) are its configuration, and deliverable 4 owns typed-client registration.

---

## User Story

**As an** ASP.NET Core developer deploying to Azure,
**I want to** turn on one call in `Program.cs` and get structured console logging, a vendor-neutral OpenTelemetry traces/metrics/logs pipeline, correlation that flows through HTTP and messages, and health-check plumbing — all driven by the `Cloudstrap:` settings I already declared,
**So that** my app is observable in production without me assembling ten OpenTelemetry packages, hand-writing samplers and noise filters, and re-inventing correlation — and so that the same call composes cleanly with an Aspire ServiceDefaults pipeline instead of fighting it.

---

## Acceptance Criteria

> AC-O2, AC-O3, AC-O4 are carried **verbatim** from the founding spec (Observability). AC-O1 belongs to deliverable 3 and is *not* claimed here — this spec only defines its seam (AC-B7). AC-ASP1 and AC-ASP2 are carried verbatim from the founding spec (Aspire Coexistence). AC-B1…AC-B13 are new, spec-specific criteria formalizing the roadmap §2 definition of done (AC-B13 was added at the 2026-07-27 gate).

| # | Given | When | Then |
|---|-------|------|------|
| AC-O2 | Mode `Otlp` + collector endpoint | App handles a request | Same telemetry arrives at the OTLP collector; **no Azure dependency loaded**. *(carried verbatim)* |
| AC-O3 | Health probe or `_blazor` static request | Tracing active | No span exported (noise filters preserved). *(carried verbatim)* |
| AC-O4 | Any mode | Solution is searched for "Dynatrace" | Zero occurrences. *(carried verbatim)* |
| AC-ASP1 | An app with an existing OTel pipeline (Aspire ServiceDefaults-style) | `UseCloudstrapObservability` runs in contribute mode | Cloudstrap samplers/filters/enrichment apply to the existing pipeline; no second exporter, no duplicate spans. *(carried verbatim)* |
| AC-ASP2 | Any shipped Cloudstrap package | Its dependency closure is inspected | Zero `Aspire.*` packages. *(carried verbatim)* |
| AC-B1 | A host with a valid `Cloudstrap` section and `OpenTelemetry:Mode = Disabled` | `builder.UseCloudstrapObservability()` runs and the host starts | The host starts; `IBusinessTrace` and the correlation services resolve from DI; no `TracerProvider`/`MeterProvider` is registered; Serilog console logging works. |
| AC-B2 | `OpenTelemetry:Mode = Console` | A request is handled | Traces and metrics are written by the console exporter; no OTLP exporter is registered. |
| AC-B3 | A logger provider registered by the consumer **before** `UseCloudstrapObservability` (e.g. `builder.Logging.AddDebug()`) | The host starts | That provider is still registered — Cloudstrap never calls `ClearProviders()`; Serilog and the OTel log provider are **added** alongside it. |
| AC-B4 | An inbound request carrying the configured correlation header | The request is handled | `ICorrelationContextAccessor.CorrelationId` returns the inbound value for the whole request; outbound calls on a typed `HttpClient` registered with `AddCloudstrapCorrelationHandler()` carry the same value in the same header. |
| AC-B5 | An inbound request **without** the correlation header | The request is handled | A correlation id is generated (default: the current W3C trace id) and used for the request; no exception, no 400 — unless the endpoint requires correlation (AC-B6). |
| AC-B6 | `Correlation:Request:RequireForAllEndpoints = true` (or `[CorrelationRequired]` on the endpoint) and a request **without** the header | The request is handled | The response is `400 Bad Request` as `application/problem+json`, naming the configured header; endpoints matching `HealthEndpoints`/`ExcludeEndpoints`, or marked `[AllowNoCorrelation]`, are exempt. |
| AC-B7 | `OpenTelemetry:Mode = AzureMonitor` and **no** exporter contributed (package `Cloudstrap.Observability.AzureMonitor` not installed/not called) | The host starts | Startup fails with an `InvalidOperationException` naming the missing package — telemetry is never silently dropped. |
| AC-B8 | `Logging:File:Enabled = true` with a `Path` | The app logs | Log files are written under exactly `Path` — no workload-name subfolder, no machine-name-derived file name, no `D:\logsint` fallback anywhere in the package. |
| AC-B9 | Owner mode, any active telemetry mode, and a trace is exported | The resource attributes are inspected | `service.name` = `Application:WorkloadName`, `deployment.environment.name` = `IHostEnvironment.EnvironmentName`, `host.name`, plus `cloudstrap.*` attributes for system/subsystem; **zero** `nihdi.*` attributes. In contribute mode the same `cloudstrap.*` attributes are added but `service.name` is left to the host. |
| AC-B10 | `OpenTelemetry:EnableBlazorHubTracing = false` (default) | A Blazor Server SignalR `ComponentHub` invocation happens | No span is exported for it; a normal HTTP request in the same app is still traced. |
| AC-B11 | The `Cloudstrap.Observability` project and package | Searched case-insensitively for `Nihdi`, `NIHDI`, `Riziv`, `Dynatrace`, `NServiceBus`, `probe.aspx`, `logsint` | Zero occurrences. |
| AC-B12 | A fresh clone | Build, test-executable run, `dotnet format --verify-no-changes` | All green; XML docs on all public API (CS1591 enforced); package metadata complete (description, tags, README); every third-party dependency is OSI-licensed and pinned in `src/Directory.Packages.props`. |
| AC-B13 | `OpenTelemetry:Mode = Otlp`, **no** `Cloudstrap:OpenTelemetry:Endpoint`, and `OTEL_EXPORTER_OTLP_ENDPOINT` set in the environment | The host starts | Startup succeeds and the OTLP exporter is registered with **no** endpoint configured by Cloudstrap, so the OpenTelemetry SDK resolves it from the standard variable. With the Cloudstrap setting present it wins and the signal path is appended as before; with **neither** present, startup still fails validation. *(gate decision OQ-2, 2026-07-27 — depends on the recorded amendment to `_specs/1-CoreSettingsModel.md` AC-C6)* |

---

## Port Decision Table

One row per source type/feature of `Nihdi.Core.Configuration.Common\` in the folders assigned to this deliverable. Verdicts: **Port** (carry over, de-NIHDI-fied) / **Redesign** (capability earns its place, design does not) / **Replace** (an existing library or framework feature does it better) / **Drop** (no credible value) / **Move → N** (belongs to another deliverable).

### `DistributedTracing\`

| Source type | Verdict | Target | Justification |
|---|---|---|---|
| `ServiceCollectionExtensions.AddOpenTelemetry(IServiceCollection, NihdiConfiguration, ILogger)` | **Redesign** | `IHostApplicationBuilder.UseCloudstrapObservability(...)` | The pipeline content (instrumentation, sampler composition, exporter selection, log-record options) is the flagship value and survives nearly intact. The *shape* does not: it takes a fully-materialized settings object **and an `ILogger` that does not exist yet** as parameters (the caller must have built a bootstrap logger first — temporal coupling), and it splits responsibility with `LoggingBuilderExtensions.ConfigureForNihdiOpenTelemetry`, which must run *before* it or `ClearProviders()` deletes the OTel log provider (a documented footgun in three call sites). One entry point on `IHostApplicationBuilder` reads `IConfiguration`, owns ordering, and eliminates the footgun. Gains owner/contribute modes (AC-ASP1). |
| `ServiceCollectionExtensions.AddNihdiBusinessTrace` | **Port** | `IServiceCollection.AddCloudstrapBusinessTrace()` (also called by the main entry point) | `TryAddSingleton<IBusinessTrace>` regardless of pipeline state is exactly right — consumers inject it unconditionally, and a disabled pipeline must not break DI resolution. Ports unchanged apart from naming. |
| `ServiceCollectionExtensions.ConfigureTracing` / `ConfigureMetrics` (private) | **Redesign** | internal pipeline composition | Content ports, including the `EnableRuntimeMetrics`/`EnableHttpClientMetrics`/`EnableAspNetCoreMetrics`/`EnableSqlClientInstrumentation` gates, which map 1:1 onto Core's shipped options. Changes: `AddSource("NServiceBus.*")` and `AddMeter("NServiceBus.*")` are **not** ported — a hard-coded vendor string for a product Cloudstrap replaced; `EnableMessagingMetrics` instead gates a public hook that deliverable 14 fills with Wolverine's source names. Exporter selection gains the `AzureMonitor` seam (AC-B7). |
| `ServiceCollectionExtensions.GetOtlpEndpoint` | **Redesign** | internal | Behavior (append the signal path to the configured base endpoint for `HttpProtobuf`) is correct and stays. Drops its bespoke `InvalidOperationException` — `OpenTelemetryOptions` validation in Core already fails startup when `Mode = Otlp` has no endpoint from either source (AC-C6 as amended 2026-07-27). Gate decision OQ-2: when `Endpoint` is null **and** `OTEL_EXPORTER_OTLP_ENDPOINT` is present in configuration, this package leaves `OtlpExporterOptions.Endpoint` (and the headers/protocol) unset so the OTel SDK reads the standard variables itself — no signal path is appended in that case, because the SDK applies its own per-signal rules. An explicit `Endpoint` still wins. |
| `ServiceCollectionExtensions.GetOtlpHeaders` | **Redesign** | internal | The source hard-codes `Authorization=Api-Token {AccessToken}` — a Dynatrace-shaped helper the founding spec explicitly removes. Replaced by formatting Core's `OpenTelemetryOptions.Headers` dictionary into OTel's `key=value,key=value` header string. |
| `ServiceCollectionExtensions.ShouldTracePath` / `ShouldTraceHttpClientRequest` / `ShouldTraceAspNetCoreRequest` / `IsStaticAssetPath` | **Redesign** | internal noise filter + public override knobs | The capability is AC-O3 and must stay. Redesigned: probe paths come from `HealthChecksOptions.LivenessPath`/`ReadinessPath` instead of the hard-coded `/health`, `/live`, `/ready`, `/probe.aspx`; the product-specific `/MudBlazor/` segment is dropped (already covered by `/_content/` and the static-asset extensions); the filter **composes with** any filter already set on the instrumentation options rather than overwriting it (AC-ASP1); consumers get `IgnoredPathSegments` (appended) and `EnableDefaultTraceNoiseFilter` (off switch) — the source had no override at all. |
| `ServiceCollectionExtensions.EnrichHttpRequest` / `Enrich(HttpResponseMessage)` | **Redesign** | internal enrichment | Setting `http.request.method` / `http.response.status_code` duplicates what `OpenTelemetry.Instrumentation.Http` already emits under the stable HTTP semantic conventions; re-writing them by hand risks disagreeing with the spec-compliant values. Kept: the `DisplayName` rewrite (`{method} {path}`), which is genuinely better than the default for readability, and the ASP.NET Core `endpoint.name` tag. Dropped: the redundant `http.*` tags and the hand-rolled `exception.*` tags (`RecordException = true` already records an exception event with those attributes). |
| `BlazorHubSampler` | **Port** | `BlazorHubSampler` (internal) | Real, non-obvious value: SignalR assigns the friendly `DisplayName` *after* sampling, so the only way to drop per-keystroke `ComponentHub` spans is the `rpc.service` tag at sampling time. The source comment documents the reasoning; the class is 60 lines and has no library equivalent. Stays `internal`, activated by Core's `EnableBlazorHubTracing = false`. |
| `NihdiResourceAttributes` | **Redesign** | `CloudstrapResourceAttributes` (internal) | Resource attributes earn their place; the *names* do not. `nihdi.businessystem.name` (typo in the source) → `cloudstrap.system.name`; `nihdi.subsystem.*` → `cloudstrap.subsystem.*`; `nihdi.environment` **and** its duplicate `nihdi.aspnet.environment` → one standard `deployment.environment.name` from `IHostEnvironment`; `nihdi.host.name` → standard `host.name`; `nihdi.workload.name` dropped as a duplicate of `service.name` (already set from `WorkloadName`). Adds `cloudstrap.environment.tier` only when `ApplicationOptions.EnvironmentTier` is set. Overridable via `ConfigureResource` (the source had no override). |
| `IBusinessTrace` | **Port** | `IBusinessTrace` (public) | The one piece of the source's tracing surface that is not obtainable from the framework: a deliberately narrow, low-cardinality business-span API with documented guidance against user/document identifiers. Consumers across the source solution inject it. Ports unchanged. |
| `BusinessTrace` | **Redesign** | `BusinessTrace` (internal sealed) | Ports; tag names `nihdi.business.*` → `cloudstrap.business.*` and activity source `Nihdi.Business` → `Cloudstrap.Business` (public constant so consumers can add it to their own `AddSource`). The `IDisposable` implementation on a singleton `ActivitySource` is kept (correct: DI disposes singletons at shutdown). |
| `IBusinessTraceScope` | **Port** | `IBusinessTraceScope` (public) | Minimal `IDisposable` + `IsRecording` + `SetOutcome`. Nothing to cut. |
| `BusinessTraceScope` | **Port** | `BusinessTraceScope` (internal sealed) | Ports as-is (tag name follows `BusinessTrace`). |

### `Logging\`

| Source type | Verdict | Target | Justification |
|---|---|---|---|
| `BootstrapLoggerFactory.Create` / `CreateLogger` / `ReadFromNihdiConfiguration` | **Redesign** | `CloudstrapBootstrapLogger.Create(...)` (public static) + internal Serilog configuration | Pre-host logging is a genuine need (deliverable 4's KeyVault bootstrap and Core's eager `GetCloudstrapOptions()` both run before `Build()`), and the source's four consumers prove it. The design does not survive: the OTLP-export branch, its `try`/`catch`-and-warn fallback, its bespoke `HttpClientFactory` workaround (see finding 4) and its second "backup logger" all exist to solve a problem the supported hosting matrix does not have — container/App Service stdout is collected by the platform. Bootstrap logging becomes console (+ optional file) only, built on Serilog's own two-stage `CreateBootstrapLogger()`. |
| `BootstrapLoggerFactory` — `IsRunningInAks()` branches (console format ×2, file logging, backup logger) | **Drop** | — | The helper was dropped in deliverable 1 (founding-spec hosting posture: no on-prem IIS/VM). The two console templates collapse into one; file logging no longer depends on where the app runs (it depends on `Logging:File:Enabled`, which is the override). |
| `BootstrapLoggerFactory` — Dynatrace branch (`ShouldConfigureDynatraceLogging`, `ConfigureDynatraceLogging`, `ResolveDynatraceLogLevel`) | **Drop** | — | Founding-spec decision: Dynatrace removed entirely (AC-O4). |
| `BootstrapLoggerFactory.MapToSerilogLogLevel(string, …)` | **Redesign** | internal `LogLevel` → `LogEventLevel` map | Core already binds `LoggingOptions.Level` as a typed `Microsoft.Extensions.Logging.LogLevel` (deliverable 1), so the string parsing, the "unrecognized → Warning" fallback and its warning message are all dead weight. What remains is a six-case enum map. **Behavior change**: a bad level value now fails configuration binding at startup instead of silently degrading to `Warning`. |
| `BootstrapLoggerFactory` — standard enrichers (`WithMachineName`, `WithThreadId`, `WithEnvironmentUserName`) | **Drop** | — | Two extra NuGet packages (`Serilog.Enrichers.Environment`, `Serilog.Enrichers.Thread`) for: a machine name already exported as the `host.name` resource attribute, a thread id that is meaningless in `async` code, and an environment user name that is `root`/`ContainerUser` in every supported target (and is arguably PII on a developer machine). `Enrich.FromLogContext()` and the configured `EnrichProperties` are kept. |
| `BootstrapLoggerFactory` — `WriteTo.Async(...)` console wrapper | **Drop** | — | `Serilog.Sinks.Async` buys throughput at the price of losing buffered events when the process dies — precisely the crash the bootstrap logger exists to record (the source needs an explicit `using` + dispose to work around it, twice). Synchronous console writes; consumers who want async wrap it themselves via `ConfigureSerilog`. |
| `DeferredLoggerFactory` (+ nested `DeferredLogger`) | **Replace** | Serilog `CreateBootstrapLogger()` (`Serilog.Extensions.Hosting`) | A hand-rolled swappable `ILoggerFactory` whose `Dispose()` is empty and whose inner `DeferredLogger` resolves a **new** logger from the factory on every single call. Its two source consumers are `Program.cs` files that hand the bootstrap logger to startup code — i.e. it exists to bridge the pre-`Build()` gap, which is exactly what Serilog's documented two-stage bootstrap logger covers (buffered events, no lost startup logs). The *swap-after-build* capability is deliberately **not** reproduced: nothing in the ported surface keeps a logger past `Build()`, and reproducing it would mean handing a mutable factory across the host boundary. Deletes a public type, a nested logger implementation, and their tests. |
| `NihdiConfigurationExtensions.GetLogFilePath` / `GetLogFileName` | **Drop** | — | Machine-name digit parsing + a "more than two path segments means the caller overrode the convention" heuristic + `D:\logsint`-era assumptions. Explicitly on the De-NIHDI checklist. The file sink writes to `FileLoggingOptions.Path` verbatim (AC-B8). Also removes this folder's only `Nihdi.Core.Functional` usage (`Preconditions.NotNull`). |
| `NihdiConsoleFormatter` | **Drop** | — | Exists only because the OTel path did `ClearProviders()` and then needed a MEL console formatter that *imitates* the Serilog console template. Cloudstrap never clears providers (AC-B3): Serilog owns console/file in every mode (founding-spec decision), OTel owns export. One console writer, one template, no imitation layer. |
| `LoggingBuilderExtensions.ApplyNihdiLogLevels` *(same folder in `Extensions\`, listed here because it is logging)* | **Redesign** | internal level application | The idea — apply one level vocabulary to MEL so every provider agrees — is right and is kept, including the framework-category seed (`Microsoft.AspNetCore`, `…Hosting.Diagnostics`, `System.Net.Http.HttpClient`, `Microsoft.Hosting.Lifetime` at `Warning`) applied *before* user overrides so users always win. The string parsing and the `"Verbose"` special case go (typed `LogLevel` from Core). |
| `LoggingBuilderExtensions.AddNihdiConsole` / `ConfigureForNihdiOpenTelemetry` | **Drop** | — | Both exist to serve the `ClearProviders()` design that is being abandoned. Their behavior is absorbed by the single entry point. |
| `Extensions\IHostApplicationBuilderExtensions.AddSerilogNihdi` *(listed here to fix the boundary with deliverable 4)* | **Redesign** | absorbed by `UseCloudstrapObservability` | A one-line `services.AddSerilog(cfg => cfg.ReadFromNihdiConfiguration(...))` wrapper that also demands an `ILogger` parameter purely to log "Add Serilog for Nihdi". Serilog registration is part of the single entry point, not a separate public call — and deliverable 4 must not inherit it. |
| `Extensions\IHostApplicationBuilderExtensions.UseSerilogForNihdi` | **Drop** | — | Already `[Obsolete]` in the source in favor of the row above. |
| `TracingEnricher` | **Drop** | — | Empty no-op public class (finding 3). |
| `MessageIdEnricher` | **Drop** | — | Stamps a fresh `Guid` on **every** log event under the misleading name `MessageId` (it is not a message-bus id). Per-event allocation, no correlation value — the trace id correlates events, and the file/console templates that print it go with it. |
| `W3CTracingMiddleware` | **Replace** | framework `Activity` + Serilog's built-in `LogEvent.TraceId`/`SpanId` | Finding 2: the `traceparent`/`tracestate` `&&` bug, a fabricated response `traceparent` with a hard-coded sampled flag, and log properties taken from a context that is not the real span. ASP.NET Core already creates the real `Activity` from the inbound `traceparent`, and the OTel log exporter attaches the real ids to exported records. The one genuine loss — trace ids in the *console/file* output — needs **zero** Cloudstrap code: Serilog ≥ 3.1 populates `LogEvent.TraceId`/`SpanId` from `Activity.Current` automatically and renders them through the `{TraceId}`/`{SpanId}` output-template tokens. This middleware is 60 lines of buggy code replaced by two tokens in a template. |

### `Correlation\`

| Source type | Verdict | Target | Justification |
|---|---|---|---|
| `CorrelationHeader` (`const Name = "NIHDI.Correlation"`) | **Drop** | — (Core's `CorrelationOptions.HeaderName`) | A non-configurable compile-time constant is exactly what the De-NIHDI checklist replaces; Core already ships the configurable `HeaderName` defaulting to `X-Correlation-ID`. Keeping a constant class alongside a setting would guarantee the two drift apart (they already do in the source: the 400 message hard-codes the constant). |
| `ICorrelationContextAccessor` | **Redesign** | `ICorrelationContextAccessor` (public) with `string? CorrelationId { get; set; }` | The ambient-accessor concept earns its place: correlation must be readable from code that has neither an `HttpContext` nor a message context (message handlers, background work), which is why `IHttpContextAccessor` cannot cover it. Redesigned: the value is the correlation id itself, not a disposable object graph; `ThrowIfUnavailable()` is dropped (a `null` check at one call site does not need an interface member). |
| `DefaultCorrelationContextAccessor` | **Redesign** | `CorrelationContextAccessor` (internal sealed) | The behavior is right (`AsyncLocal`, flows through async continuations) but the implementation holds the `AsyncLocal` in a **`static readonly` field on a DI singleton** — process-global state shared across every container in the process (a test-isolation hazard) — and throws `InvalidOperationException` when a second context is set before the first is disposed, which makes nested/parallel scopes a runtime error rather than a scoped override. Redesigned to an instance field, last-write-wins within the current async flow, no throw. |
| `ICorrelationContext` | **Drop** | — | An `IDisposable` interface whose entire payload is one `string`, plus an `IsDisposed` flag that exists only to let the accessor's "already set" guard distinguish states. With the accessor holding a `string?`, none of it has a reason to exist. |
| `DefaultCorrelationContext` | **Drop** | — | Same as above; also a `sealed record` whose value equality is never used and whose constructor throws `ArgumentNullException` for a whitespace argument (wrong exception type). |
| `ICorrelationSource` | **Port** | `ICorrelationSource` (public) | Kept deliberately as the documented override for id generation ("every convention has an override"): a single-method seam registered with `TryAddSingleton` so a consumer can supply their own format. |
| `DefaultCorrelationSource` | **Port** | `TraceIdCorrelationSource` (internal sealed) | "current `Activity.TraceId`, else a new GUID" is the right default — the correlation id equals the W3C trace id, so logs, traces and the correlation header agree by construction. Renamed to say what it does. |
| `CorrelationValidationMiddleware` | **Redesign** | merged into `CloudstrapCorrelationMiddleware` | The capability (requiring correlation on selected endpoints) is kept, but as **one** middleware that establishes *and* validates, fixing finding 1. Redesigned: resolves options through `IOptions<CorrelationOptions>` instead of pulling the settings god-object out of `RequestServices` (service locator); the hard-coded `/swagger`, `/scalar`, `/openapi` exclusions go (they are `ExcludeEndpoints` values, and this package must not know about OpenAPI UIs — that is deliverable 5); the `"Health checks"` **display-name substring match** goes (a locale-and-framework-version-dependent string comparison) in favor of `HealthCheckOptions` endpoint metadata + the configured `HealthEndpoints`; the anonymous-object error body becomes RFC 9457 `application/problem+json` via `IProblemDetailsService`. |
| `CorrelationRequiredAttribute` | **Port** | `CorrelationRequiredAttribute` | Endpoint metadata opt-in; two lines, no library equivalent, used by the middleware. |
| `AllowNoCorrelationAttribute` | **Port** | `AllowNoCorrelationAttribute` | Endpoint metadata opt-out; the counterpart of the above (file reformatted — the source is the only block-scoped-namespace file in the folder). |
| `CorrelationExtensions.AddCorrelation` | **Redesign** | `IServiceCollection.AddCloudstrapCorrelation()` | Registration survives; naming follows the repo convention. Registers accessor + source with `TryAdd` (idempotent, replaceable). |
| `CorrelationExtensions.UseCorrelation` | **Redesign** | `IApplicationBuilder.UseCloudstrapCorrelation()` | Now registers the single merged middleware (establish + validate), so a host that calls it gets working correlation without also hunting for the per-host ingress middleware that lives in another package. |
| `CorrelationHttpDelegatingHandler` | **Redesign** | `CorrelationHttpDelegatingHandler` (public sealed) | The egress half of correlation and the seam deliverable 4 consumes — kept. Two fixes: it reads the header name from `IOptions<CorrelationOptions>` instead of the constant, and it uses a set-if-absent write instead of `Headers.Add`, which throws when the same `HttpRequestMessage` is re-sent (exactly what a resilience/retry handler does). The `Send`/`SendAsync` pair is kept (sync `Send` matters for Blazor/JSON-RPC callers). |
| `IHttpClientBuilderExtensions.AddCorrelationHandler` | **Redesign** | `IHttpClientBuilder.AddCloudstrapCorrelationHandler()` | Kept (the deliverable-4 seam); renamed per convention; the handler registration becomes idempotent so calling it twice — or having it applied by both `ConfigureHttpClientDefaults` and an explicit call — does not stack two handlers (AC-ASP3's sibling concern). |

### `HealthChecks\`

| Source type | Verdict | Target | Justification |
|---|---|---|---|
| `ApiLivenessHealthCheck` | **Move → 4** *(and there: **Replace**)* | `AspNetCore.HealthChecks.Uris` (recommended to deliverable 4) | It is a typed-client health check: it resolves a named `HttpClient`, GETs `live`, and — brittle — requires the response **body** to equal the literal `"Healthy"`, which only works against the source's own response writer. Its configuration (`EnableHealthCheck`, `HealthCheckPrefix`) lives on `HttpClientServiceOptions`, which deliverable 4 owns. Not this package's concern; the Xabaril URI check does the job with status-code semantics instead of body matching — **deliverable 4's spec owns that call and must verify its license and maintenance before taking it**; this row only records that the bespoke check should not be ported as-is. |
| `HealthChecks\ServiceCollectionExtensions.AddApiLivenessHealthCheck` | **Move → 4** | — | Same reason; it reads `HttpClientServiceRegistry` directly. Its Dynatrace severity-tag mapping (`HealthCheckTags.SeverityCritical/Error/Warning` from the internal `Nihdi.Core.Health` package) is **dropped** outright (AC-O4). |
| `Nihdi.Core.Health.HealthCheckTags` (internal package — `Liveness`/`Readiness`/severity tags) | **Redesign** | `CloudstrapHealthCheckTags` (`Liveness = "live"`, `Readiness = "ready"`) | The De-NIHDI checklist replaces the internal package with `Microsoft.Extensions.Diagnostics.HealthChecks`, which has no tag vocabulary of its own — but the liveness/readiness split that `HealthChecksOptions.LivenessPath`/`ReadinessPath` imply needs *some* shared tag constants, used by this package, 4, 5, 7 and 12. Two constants, no severity taxonomy. |
| `Nihdi.Core.Health.HealthReportConverter.NihdiHealthResponseWriter` (internal package) | **Out of scope** | — | Source unavailable (internal package, not in the reference repo) and the JSON response shape belongs with endpoint mapping in deliverable 4/5. Nothing here depends on it. |
| *(considered and rejected)* an `AddCloudstrapHealthChecks()` wrapper | **Drop** | — (stock `services.AddHealthChecks()`) | Deliberately **not** introduced. This package registers no checks and maps no endpoints — it only *reads* `HealthChecksOptions` paths for the trace noise filter and the correlation exemptions. A wrapper around `AddHealthChecks()` would add a Cloudstrap name to a framework call that is already additive and already the Aspire-composable path (founding spec, Aspire §3). Registration and endpoint mapping belong to deliverables 4/5/7; this deliverable contributes only the shared tag vocabulary above. |

### `Dynatrace\` — read and deleted (AC-O4)

| Source type | Verdict | Justification |
|---|---|---|
| `DynatraceExtensions` (Serilog `WriteTo.Dynatrace` / `DurableDynatrace` sinks) | **Drop** | Founding-spec decision. Also removes the `Serilog.Sinks.Http` dependency it is built on. |
| `DynatraceOptions` | **Drop** | Ditto. |
| `DynatraceTextFormatter` | **Drop** | Ditto. |
| `DynatraceBatchFormatter` | **Drop** | Ditto. |
| `DynatraceHttpClient` | **Drop** | Ditto (a bespoke `HttpClient` that can be told to accept invalid certificates — `AllowInvalidCertificates` — which is not a behavior an MIT library should publish). |

---

## Public API Sketch

Namespace **`Cloudstrap.Observability`** for the telemetry/logging surface and **`Cloudstrap.Observability.Correlation`** for correlation (the correlation types are a coherent, separately-consumed group — deliverable 4 imports only those). Everything is `public sealed` unless it is an interface or an attribute; implementations are `internal` unless a consumer must name the type.

```text
Cloudstrap.Observability
├── HostApplicationBuilderExtensions (static)
│     UseCloudstrapObservability(this IHostApplicationBuilder builder,
│                                Action<CloudstrapObservabilityOptions>? configure = null)
│         : CloudstrapObservabilityBuilder
│       — the one entry point. Calls AddCloudstrapCore() (idempotent), configures Serilog
│         (console/file) as an *added* provider, applies MEL levels, registers IBusinessTrace
│         and correlation services, and — when OpenTelemetryOptions.IsActive — builds or
│         contributes to the OTel pipeline.
│
├── CloudstrapObservabilityOptions            — code-level options (NOT a config section)
│     PipelineMode        : ObservabilityPipelineMode = Owner
│     EnableDefaultTraceNoiseFilter : bool = true
│     IgnoredPathSegments : IList<string>     — appended to the default noise list
│     ApplySampler        : bool = true       — set false to leave the host's sampler alone
│     ConfigureResource   : Action<ResourceBuilder>?
│     ConfigureTracing    : Action<TracerProviderBuilder>?
│     ConfigureMetrics    : Action<MeterProviderBuilder>?
│     ConfigureLogging    : Action<OpenTelemetryLoggerOptions>?
│     ConfigureOtlpExporter : Action<OtlpExporterOptions>?
│     ConfigureSerilog    : Action<LoggerConfiguration>?   — final say over console/file logging
│
├── ObservabilityPipelineMode (enum)          Owner = 0, Contribute = 1
│
├── CloudstrapObservabilityBuilder            — returned by the entry point; the exporter seam
│     Services            : IServiceCollection
│     Telemetry           : OpenTelemetryOptions   — the values used for registration decisions
│     MarkExporterContributed()                    — called by exporter packages (deliverable 3)
│                                                    to satisfy the AC-B7 startup check
│
├── IBusinessTrace                            (public interface)
│     StartSpan(string operation, string component) : IBusinessTraceScope
│
├── IBusinessTraceScope : IDisposable         (public interface)
│     IsRecording : bool
│     SetOutcome(string outcome) : void
│
├── CloudstrapActivitySources (static)
│     const Business = "Cloudstrap.Business"   — so consumers can AddSource it themselves
│
├── CloudstrapHealthCheckTags (static)
│     const Liveness  = "live"
│     const Readiness = "ready"
│
├── ServiceCollectionExtensions (static)
│     AddCloudstrapBusinessTrace(this IServiceCollection) : IServiceCollection
│
└── CloudstrapBootstrapLogger (static)
      Create(CloudstrapOptions options) : ILoggerFactory
        — pre-host console (+ optional file) logging, built on Serilog's CreateBootstrapLogger();
          independent of the host pipeline, disposed by the consumer after Build().

Cloudstrap.Observability.Correlation
├── ICorrelationContextAccessor               (public interface)
│     CorrelationId : string?   { get; set; }
├── ICorrelationSource                        (public interface)
│     GenerateCorrelation() : string
├── CorrelationHttpDelegatingHandler : DelegatingHandler   (public sealed)
├── CorrelationRequiredAttribute   : Attribute (public sealed, method|class)
├── AllowNoCorrelationAttribute    : Attribute (public sealed, method|class)
├── ServiceCollectionExtensions (static)
│     AddCloudstrapCorrelation(this IServiceCollection) : IServiceCollection
├── ApplicationBuilderExtensions (static)
│     UseCloudstrapCorrelation(this IApplicationBuilder) : IApplicationBuilder
└── HttpClientBuilderExtensions (static)
      AddCloudstrapCorrelationHandler(this IHttpClientBuilder) : IHttpClientBuilder
```

Configuration is **entirely** Core's (`Cloudstrap:Logging`, `Cloudstrap:OpenTelemetry`, `Cloudstrap:Correlation`, `Cloudstrap:HealthChecks`, `Cloudstrap:Application`). This package introduces **no new configuration section** — everything it adds is a code-level option, because every one of them is a decision made in `Program.cs` (which pipeline you own, which delegate you pass), not a deployment setting.

**How the two Core entry points are used** (this is the seam deliverable 1 was built for): `UseCloudstrapObservability` calls `services.AddCloudstrapCore()` — idempotent — so middleware and handlers resolve `IOptions<CorrelationOptions>`/`IOptions<HealthChecksOptions>` at run time, **and** calls `builder.Configuration.GetCloudstrapOptions()` for the values it needs at *registration* time (which exporters, which instrumentation, which sinks). A misconfigured `Cloudstrap` section therefore fails inside `UseCloudstrapObservability` with `ConfigurationValidationException` — at the exact line in `Program.cs` that asked for observability — instead of at some later `ValidateOnStart`. No settings are re-read from raw configuration keys and no options type is redefined here.

---

## Behaviors & Conventions

| Behavior | Default | Override |
|---|---|---|
| Logging providers | Serilog (console; file when `Logging:File:Enabled`) and, when the pipeline is active and `EnableLogs`, the OTel log provider — both **added**, never replacing what the consumer registered. Cloudstrap never calls `ClearProviders()`. | `configure.ConfigureSerilog(...)` for the Serilog pipeline; `builder.Logging.*` for anything else — it survives. |
| Log levels | `LoggingOptions.Level` applied to MEL; framework categories (`Microsoft.AspNetCore`, `Microsoft.AspNetCore.Hosting.Diagnostics`, `System.Net.Http.HttpClient`, `Microsoft.Hosting.Lifetime`) seeded at `Warning` first, then `LevelOverrides` applied on top so a consumer override always wins. | `Cloudstrap:Logging:LevelOverrides` (note deliverable-1 fact 2: configured entries **append to** defaults, they do not replace them). |
| Console output | One human-readable Serilog template — timestamp, level, message, source context, `{TraceId}`/`{SpanId}` (Serilog's built-in `LogEvent` values, populated from `Activity.Current`; empty when no activity), exception — enriched from the log context plus the configured `EnrichProperties`. | `ConfigureSerilog`. |
| File output | Off. When enabled: written verbatim to `FileLoggingOptions.Path`, daily rolling, 10 MB size cap, 20 retained files, shared handle. | `Cloudstrap:Logging:File:*` for on/off + path; `ConfigureSerilog` for the sink parameters. |
| OTel pipeline ownership | **Owner** — Cloudstrap calls `AddOpenTelemetry()`, sets the resource, adds instrumentation, sampler, noise filters, enrichment and the exporter chosen by `Mode`. | `configure.PipelineMode = Contribute` — Cloudstrap then adds **only** its differentiated pieces (activity sources, sampler, noise filters, enrichment, `IBusinessTrace`, `cloudstrap.*` resource attributes) and registers **no exporter, no instrumentation and no `service.name`** (AC-ASP1). |
| Exporter selection | `Disabled` → nothing; `Console` → console exporter; `Otlp` → OTLP over HTTP/protobuf to `Endpoint` (+ `Headers`); `AzureMonitor` → nothing here, deliverable 3 contributes (startup fails if nothing did — AC-B7). | `ConfigureOtlpExporter` (protocol, timeout, batch options); `ConfigureTracing`/`ConfigureMetrics`/`ConfigureLogging` for anything else, including additional exporters. |
| OTLP endpoint resolution | `Cloudstrap:OpenTelemetry:Endpoint` wins when set: Cloudstrap appends the signal path (`/v1/traces`, `/v1/metrics`, `/v1/logs`) and applies `Headers`. When it is **not** set, Cloudstrap configures no endpoint at all and the OTel SDK reads the standard `OTEL_EXPORTER_OTLP_ENDPOINT` / `_HEADERS` / `_PROTOCOL` variables itself (gate decision OQ-2 — "speak the platform's conventions"). Neither source present → startup fails in Core's validation. | Set `Cloudstrap:OpenTelemetry:Endpoint` to take control; set only the environment variables to hand control to the SDK; `ConfigureOtlpExporter` overrides both. |
| Trace noise filter | Drops spans for the configured liveness/readiness paths and for `/_blazor`, `/_framework/`, `/_content/` and static-asset extensions, on both inbound (ASP.NET Core) and outbound (HttpClient) instrumentation; **composes** with any filter the host already set (both must pass). | `IgnoredPathSegments` to add; `EnableDefaultTraceNoiseFilter = false` to remove; `Cloudstrap:HealthChecks:*Path` to move the probes. |
| Sampler | `ParentBased(AlwaysOn)` (the OTel default), wrapped in `BlazorHubSampler` unless `EnableBlazorHubTracing`; `AlwaysOnSampler` when the dev flag is set. | `Cloudstrap:OpenTelemetry:AlwaysOnSampler` / `EnableBlazorHubTracing`; `ApplySampler = false` to leave the host's sampler untouched (`SetSampler` is last-wins in OpenTelemetry, so contribute mode *does* replace a sampler the host already set — this flag is the documented opt-out); `ConfigureTracing(b => b.SetSampler(...))` to replace it outright. |
| Resource attributes | `service.name` = `WorkloadName`, `deployment.environment.name` = `IHostEnvironment.EnvironmentName`, `host.name`, `cloudstrap.system.name`, `cloudstrap.subsystem.name`, `cloudstrap.subsystem.type`, `cloudstrap.environment.tier` (only when set). In Contribute mode, `service.name` is **not** set. | `ConfigureResource`. |
| Correlation id | Read from `Correlation:HeaderName` on the inbound request; when absent, generated by `ICorrelationSource` (default: the current W3C trace id). Available process-wide for the current async flow through `ICorrelationContextAccessor`. | `Cloudstrap:Correlation:HeaderName`; replace `ICorrelationSource` in DI. |
| Correlation requirement | Off. When `RequireForAllEndpoints` or `[CorrelationRequired]` applies and the caller sent no header → `400` + `application/problem+json`. Exempt: configured `HealthEndpoints`, `ExcludeEndpoints`, endpoints carrying health-check metadata, and `[AllowNoCorrelation]`. | `Cloudstrap:Correlation:Request:*` and the two attributes. |
| Correlation propagation | A typed client registered with `AddCloudstrapCorrelationHandler()` sends the current correlation id in the configured header; the handler is registered idempotently so it is never stacked twice. | Don't call it; or register your own `DelegatingHandler`. |
| Correlation middleware placement | **Not automatic.** `UseCloudstrapObservability` registers the correlation *services*; the middleware runs only where `app.UseCloudstrapCorrelation()` is called, and it must sit early in the pipeline (after routing, so endpoint metadata — `[CorrelationRequired]`, health-check metadata — is available). Deliverable 4's pipeline helpers place it; a hand-built pipeline places it itself. No `IStartupFilter` magic. | Place the call where you want it. |
| Health checks | This package registers **no** health checks and maps **no** endpoints. It publishes the shared tag vocabulary (`CloudstrapHealthCheckTags.Liveness`/`Readiness`) and reads `HealthChecksOptions.LivenessPath`/`ReadinessPath` for the trace noise filter and the correlation exemptions. Every Cloudstrap package that *does* register checks (4/5/7/12) uses the stock `services.AddHealthChecks()` builder, which is additive by construction and therefore composes with an Aspire ServiceDefaults registration. | Register checks yourself on the stock builder; move the probes with `Cloudstrap:HealthChecks:*Path`. |
| Bootstrap logging | `CloudstrapBootstrapLogger.Create(options)` gives console (+ file when configured) logging before the host exists, for the pre-`Build()` work in deliverable 4. It is independent of the host pipeline; the consumer disposes it after `Build()`. Cloudstrap neither requires nor sets Serilog's global `Log.Logger`. | Pass any `ILoggerFactory` instead — nothing in this package requires Cloudstrap's. |
| Aspire coexistence | Owner mode is the default and assumes Cloudstrap is the only OTel registrar. Contribute mode is the documented posture inside an Aspire app: ServiceDefaults keeps its exporters and instrumentation; Cloudstrap adds samplers, noise filters, enrichment and `IBusinessTrace`. Health checks always go through the stock builder (additive). Zero `Aspire.*` references (AC-ASP2). | `PipelineMode`. |

**Test strategy (spec-level):** NUnit 4 on Microsoft.Testing.Platform in `src/Test/UnitTest/Cloudstrap.Observability.Tests`. Telemetry assertions use OTel's in-memory exporter (`OpenTelemetry.Exporter.InMemory`) and `ActivityListener` — no collector, no network, no Azure. Middleware and DI assertions use `ServiceCollection` + `DefaultHttpContext`/`TestServer`-style hosts. Contribute mode (AC-ASP1) is proven by registering a ServiceDefaults-shaped pipeline first and asserting exactly one exporter and one span per request. AC-O2 is proven by asserting the package's dependency closure contains no `Azure.*` assembly. Neutral fixture values only (`contoso`, `example.com`).

---

## Dependencies

The repo's first non-Microsoft dependencies (⚠️ risk area). All are OSI-approved, all are pinned in `src/Directory.Packages.props` (CPM — which today holds only `Microsoft.Extensions.*` 10.0.10 and the NUnit trio); exact versions are the planner's to pin at plan time.

| Package | License | Justification |
|---|---|---|
| `Serilog` | Apache-2.0 | Founding-spec decision: Serilog stays for bootstrap/console/file logging. Also supplies `LogEvent.TraceId`/`SpanId` natively (≥ 3.1), which is why no trace enricher is written or referenced. |
| `Serilog.Extensions.Hosting` | Apache-2.0 | `AddSerilog` — registers Serilog as an **added** `ILoggerProvider` (no `ClearProviders`) — plus `CreateBootstrapLogger()`, the replacement for `DeferredLoggerFactory`. |
| `Serilog.Sinks.Console` | Apache-2.0 | Console sink. |
| `Serilog.Sinks.File` | Apache-2.0 | File sink; only touched when `Logging:File:Enabled`. |
| `OpenTelemetry.Extensions.Hosting` | Apache-2.0 | `AddOpenTelemetry()` + provider lifetime management. Pulls in `OpenTelemetry` (SDK) and `OpenTelemetry.Api`. |
| `OpenTelemetry.Instrumentation.AspNetCore` | Apache-2.0 | Inbound request traces + metrics; its options object is where the AC-O3 filter lands. |
| `OpenTelemetry.Instrumentation.Http` | Apache-2.0 | Outbound `HttpClient` traces + metrics. |
| `OpenTelemetry.Instrumentation.Runtime` | Apache-2.0 | `EnableRuntimeMetrics`. |
| `OpenTelemetry.Instrumentation.SqlClient` | Apache-2.0 | `EnableSqlClientInstrumentation` (Core setting, default off). Verified **stable** — 1.16.0, published 2026-06-24; the long-running beta line is over, so no `NU5104` prerelease-dependency conflict with `TreatWarningsAsErrors`. |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | Apache-2.0 | `Mode = Otlp` (AC-O2). |
| `OpenTelemetry.Exporter.Console` | Apache-2.0 | `Mode = Console` / `EnableConsole`. |
| *(no package)* health checks | MIT | The health-check types the correlation middleware and the tag constants use (`HealthCheckOptions` endpoint metadata, `HealthStatus`) ship **inside** the `Microsoft.AspNetCore.App` shared framework — no separate `PackageReference`. This is the De-NIHDI replacement for the internal `Nihdi.Core.Health` package. |
| `Cloudstrap.Core` *(project reference)* | MIT | The settings model; this package consumes it and defines no options of its own. |
| `Microsoft.AspNetCore.App` *(framework reference)* | MIT | Middleware, `HttpContext`-based trace filters, endpoint metadata. **First framework reference in the suite** — one package carries it (Decision Log OQ-1); every consumer of `Cloudstrap.Observability` therefore requires the ASP.NET Core shared framework at run time, which the package README must state. |
| *(test only)* `OpenTelemetry.Exporter.InMemory` | Apache-2.0 | Span/metric assertions without a collector. |

Considered and **rejected** (each would have added code or risk without removing more):

- `Serilog.Sinks.Async` — loses buffered events when the process dies, which is exactly the crash the bootstrap logger exists to record; the source needs an explicit dispose to work around it in two places.
- `Serilog.Enrichers.Environment` / `Serilog.Enrichers.Thread` — two packages for `MachineName` (already the `host.name` resource attribute), `ThreadId` (meaningless under `async`) and `EnvironmentUserName`.
- `Serilog.Enrichers.Span` — **checked and rejected on evidence**: MIT, but its last release is 3.1.0 (January 2023) and the repository is **deprecated by its own author** with the note that Serilog now does this natively. Confirms the built-in `{TraceId}`/`{SpanId}` route.
- `Serilog.AspNetCore` — its headline feature (`UseSerilog` + request logging) is built on the `ClearProviders` ownership model this spec abandons, and its request logging duplicates ASP.NET Core's own.
- `Serilog.Sinks.OpenTelemetry` (Apache-2.0, actively maintained) — a genuine alternative for log export, rejected because it only speaks OTLP: routing logs through MEL's OpenTelemetry provider instead gives one log pipeline that also feeds deliverable 3's Azure Monitor log exporter, so choosing it would have meant two log-export mechanisms.
- `AspNetCore.HealthChecks.*` (Xabaril) — nothing here needs it; suggested to deliverable 4 for the URI liveness check that replaces `ApiLivenessHealthCheck`, where its license and maintenance must be verified before it is taken.
- `LanguageExt.Core` — nothing in this deliverable consumes functional types (no `Result`/`Option`/`Preconditions` usage exists in the ported surface; the source's only `Preconditions` call is in the dropped `GetLogFileName`). The founding spec's type-mapping decision still defers to a later deliverable.
- Any `Aspire.*` package (AC-ASP2) and any `Azure.*` package (AC-O2 — the exporter is deliverable 3, quarantined there).

---

## Deliberate Behavior Changes (vs. the source library)

1. **One correlation middleware instead of three components in three packages** — establish + validate in one place; an MVC-style host no longer discards the caller's correlation id (finding 1).
2. **`ClearProviders()` is never called.** Serilog and OTel logging are added as providers alongside whatever the consumer registered (AC-B3). This is also what makes contribute mode possible.
3. **No bootstrap OTLP export.** Pre-host logs go to console/file only; the crash-flush workaround and the fallback-warning path disappear (finding 4).
4. **Log level parsing is gone** — Core binds a typed `LogLevel`, so an unrecognized level now fails startup instead of silently becoming `Warning`.
5. **`W3CTracingMiddleware` removed** — no fabricated response `traceparent` header, no `LogContext` trace properties taken from a synthetic context. Trace ids reach the console through Serilog's built-in `{TraceId}`/`{SpanId}` and reach exporters through OTel. Consumers that relied on the response `traceparent` echo must add it themselves. **No swappable bootstrap logger factory** either: `DeferredLoggerFactory` is not reproduced — the bootstrap factory is independent of the host pipeline and disposed after `Build()`.
6. **`MessageId` per log event removed**; `MachineName`, `ThreadId` and `EnvironmentUserName` enrichment removed.
7. **One console template.** The AKS-vs-on-prem console formats collapse into a single template (hosting posture: no on-prem).
8. **Correlation is a `string?`, not a disposable object graph** — `ICorrelationContext`/`DefaultCorrelationContext` are gone, the accessor's `AsyncLocal` is per-instance rather than `static`, and setting a second correlation id no longer throws.
9. **Correlation header name is configuration, not a constant** — `NIHDI.Correlation` → `Cloudstrap:Correlation:HeaderName` (default `X-Correlation-ID`), read by the middleware, the delegating handler and the error message alike.
10. **Correlation egress uses set-if-absent** instead of `HttpHeaders.Add`, which throws on a re-sent `HttpRequestMessage` (retry/resilience paths).
11. **Validation failures return `application/problem+json`** instead of an anonymous JSON object, and the hard-coded `/swagger`, `/scalar`, `/openapi` and `"Health checks"`-display-name exemptions are gone.
12. **Probe paths come from configuration** — `/probe.aspx`, `/live`, `/health` no longer appear anywhere; the noise filter reads `HealthChecksOptions`.
13. **Resource attributes renamed to `cloudstrap.*` + OTel semconv**, with the `nihdi.workload.name`/`nihdi.aspnet.environment` duplicates removed and the source's `businessystem` typo not carried over.
14. **Redundant HTTP span tags dropped** — `http.request.*`, `http.response.status_code` and the hand-written `exception.*` tags are left to the instrumentation libraries; only `DisplayName` and `endpoint.name` enrichment are kept.
15. **`AddSource("NServiceBus.*")` / `AddMeter("NServiceBus.*")` are not ported** — messaging telemetry is contributed by deliverable 14 through `ConfigureTracing`/`ConfigureMetrics`.
16. **File logging writes exactly where you point it** — no workload-name subfolder, no machine-name-derived file name (AC-B8).
17. **The OTLP endpoint may come from the ecosystem's standard environment variable** *(gate decision OQ-2, 2026-07-27)* — the source threw `InvalidOperationException` whenever `BaseUri` was empty in `Otlp` mode. Cloudstrap instead steps aside when `Cloudstrap:OpenTelemetry:Endpoint` is unset and `OTEL_EXPORTER_OTLP_ENDPOINT` is present, letting the OpenTelemetry SDK resolve endpoint, headers and protocol itself. Configuring neither still fails startup.

---

## Out of Scope (this deliverable — the planner must not resurrect any of it)

- Everything in the founding spec's global Out of Scope: message encryption, MessagingBridge, Dynatrace, ServicePlatform/ServicePulse, `Cloudstrap.Functional`.
- All **Dropped** rows above: `CorrelationHeader`, `ICorrelationContext`/`DefaultCorrelationContext`, `W3CTracingMiddleware`, `TracingEnricher`, `MessageIdEnricher`, `NihdiConsoleFormatter`, `AddNihdiConsole`/`ConfigureForNihdiOpenTelemetry`, `DeferredLoggerFactory`, `GetLogFilePath`/`GetLogFileName`, the Dynatrace folder, the `IsRunningInAks` branches, the Serilog environment/thread enrichers, `Serilog.Sinks.Async`.
- **Azure Monitor / Application Insights wiring** (deliverable 3) — this package defines the seam and the failure mode, and must load zero `Azure.*` assemblies (AC-O2).
- **Health-check endpoint mapping**, the `/health`-on-port-9000 convention, the health response writer, and the per-typed-client liveness check — deliverables 4, 5 and 7.
- **KeyVault configuration, typed `HttpClient` registration, forwarded headers, TLS/Kestrel setup, path base, exception handling, CORS/HSTS, security headers** — all deliverable 4 (they share the source's `Extensions\`, `Services\`, `Options\` folders, which this deliverable does not touch except for the logging helpers explicitly given verdicts in the Port Decision Table: `ApplyNihdiLogLevels`, `AddNihdiConsole`, `ConfigureForNihdiOpenTelemetry`, `AddSerilogNihdi`, `UseSerilogForNihdi`).
- **Messaging correlation behaviors** (the source's NServiceBus `Correlation*Behavior` classes) — deliverable 14 builds Wolverine middleware on this package's `ICorrelationContextAccessor`.
- **Blazor Server circuit correlation** (`BlazorServer\DistributedTracing\DistributedTraceService`) — deliverable 12.
- **A correlation response header echo** — the source never echoed the correlation header (only a fabricated `traceparent`, which is being dropped); adding one would be a new feature.
- Aspire packages or Aspire-specific code paths (AC-ASP2); `Cloudstrap.Aspire` is post-v1.

---

## Decision Log (gate answers, 2026-07-27 — zero Open Questions remain; spec is planner-ready)

| Question | Answer (user, 2026-07-27) |
|---|---|
| OQ-1 — One package carrying the `Microsoft.AspNetCore.App` framework reference, or a `Cloudstrap.Observability.AspNetCore` split? | **(A) One package** (this spec's recommendation). `Cloudstrap.Observability` carries the framework reference; there is no `Cloudstrap.Observability.AspNetCore` — the founding spec's Package Map stands unchanged. Consequence to document in the package README: every consumer of this package (directly or through deliverables 4/12/14) requires the ASP.NET Core shared framework at run time, so a `mcr.microsoft.com/dotnet/runtime`-only image is not a supported base for those apps. Rationale: only four pieces need ASP.NET Core (the correlation middleware, the `HttpContext` trace filter, health-check endpoint metadata, and `OpenTelemetry.Instrumentation.AspNetCore` — which carries the framework reference regardless), while a split would put a permanent cost on the public API (two same-named entry points in scope at once, or a differently-named web entry point) to serve a currently hypothetical runtime-image-only worker. **The split remains available later**: the base keeps its name and the ASP.NET Core types move out behind `[TypeForwardedTo]` — source-compatible, binary-breaking, acceptable pre-1.0. Revisit only if a real runtime-image-only worker scenario appears. |
| OQ-2 — Should `Mode = Otlp` accept the standard `OTEL_EXPORTER_OTLP_ENDPOINT` instead of requiring `Cloudstrap:OpenTelemetry:Endpoint`? | **(B) Relax the rule** (this spec's recommendation). `Endpoint` is required when `Mode = Otlp` **unless** `OTEL_EXPORTER_OTLP_ENDPOINT` is present in configuration; when `Endpoint` is null, `Cloudstrap.Observability` leaves `OtlpExporterOptions.Endpoint` (and headers/protocol) unset and lets the OpenTelemetry SDK read the standard variables. Explicit Cloudstrap configuration still wins when present. **The rule is relaxed, not removed** — `Mode = Otlp` with neither the setting nor the environment variable must still fail validation. Recorded in this spec as AC-B13, Behaviors row "OTLP endpoint resolution", and Deliberate Behavior Change 17. **Carries a post-approval amendment to `_specs/1-CoreSettingsModel.md`** (AC-C6 wording + the `OpenTelemetryOptions` row + validation mechanics), recorded in that spec's Decision Log on 2026-07-27; the implementing change lands in `src/Cloudstrap.Core/OpenTelemetryOptionsValidator.cs` and its tests and is scheduled inside **this** deliverable's plan — deliverable 1 itself stays closed. |
| OQ-3 — `Mode = AzureMonitor` with no exporter contributed: fail startup, or degrade loudly? | **(A) Fail startup** (this spec's recommendation). Host start throws `InvalidOperationException` naming the missing `Cloudstrap.Observability.AzureMonitor` package. **AC-B7 stands exactly as drafted.** Rationale: Cloudstrap's established posture is fail-fast on configuration (Core's `ValidateOnStart`, the throwing `GetCloudstrapOptions()`), and "you asked for Azure Monitor and no Azure Monitor exporter exists" is a deployment error — silent telemetry loss is discovered during the incident the telemetry was meant to explain. |
