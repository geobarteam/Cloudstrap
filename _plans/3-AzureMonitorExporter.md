# Plan: 3-AzureMonitorExporter — A consumer flips `Cloudstrap:OpenTelemetry:Mode` to `AzureMonitor`, adds one chained call, and telemetry lands in Application Insights

## Overview

Deliverable #3 of the extraction roadmap: the `Cloudstrap.Observability.AzureMonitor` leaf package plus the committed amendment to the shipped base package. **Binding spec: `_specs/3-AzureMonitorExporter.md`** (approved 2026-08-01, zero Open Questions; its Port Decision Table, Public API Sketch, Behaviors & Conventions table, Decision Log and Out of Scope list are authoritative — nothing marked Reject/Drop may appear here: no distro, no `UseAzureMonitorExporter`, no Live Metrics, no classic AI SDK, no `OpenTelemetry.Extensions.AzureMonitor`). Reference patterns, all read in full before planning:

- **Repo pattern (deliverable 2, verified on disk)**: `src/Cloudstrap.Observability/` + `src/Test/UnitTest/Cloudstrap.Observability.Tests/` — csproj shape (Sdk + `TargetFramework` + the two packaging properties + metadata block), sealed types with full XML docs, `internal` implementations, NUnit-4-on-MTP test project with version-less `PackageReference`s, host-level fixtures (`HostApplicationBuilder` + in-memory config + InMemory exporter + public-hook captures — the exact idiom of `ObservabilityOtlpAndAzureMonitorTests`), `PackageSurfaceTests` guard idiom, neutral fixtures (`Contoso`/`Orders`/`Api`).
- **The seams this package fills (read in the shipped code)**: `CloudstrapObservabilityBuilder` (+ `MarkExporterContributed()` / `ExporterContributionMarker`), `AzureMonitorContributionGuard` (owner-mode fail-fast), `OpenTelemetryPipeline.ConfigureOwner/ConfigureContribute` (idempotent `services.AddOpenTelemetry()`, per-signal `Enable*` gates, consumer hooks last), `OtlpExporterSetup` (the leave-SDK-defaults-alone posture AC-AM2 mirrors), `BlazorHubSampler` (`rpc.service == "ComponentHub"`), Core's `OpenTelemetryOptions`/`OpenTelemetryOptionsValidator` (ctor-injected `IConfiguration`, standard-variable fallback — the pattern `AzureMonitorOptionsValidator` copies).
- **Demonstration harness (deliverable 25, verified on disk)**: `src/Test/WasmTestProject/` — Bff host `Program.cs` already calls `UseCloudstrapObservability()` in Console mode; `E2eFixture` (boots the Bff on `http://127.0.0.1:5300`, `CapturedSutOutput`), `SutProcess.Start(baseUrl, applicationArguments)` for short-lived startup-scenario instances, `PageTestBase` for browser tests. *(Plan 25's Steps 1–6 are implemented and the harness is live; its final gate sign-off is still open in `_plans/25-WasmTestProjectSut.md` — this plan builds on the as-built harness.)*

This is a library deliverable with no controllers and no database: the template's endpoint-integration block does not apply. The integration layer is host-level unit fixtures plus the mandatory E2E demonstration slice (Step 7). **No live Azure anywhere in tests** — the dummy connection string is `InstrumentationKey=00000000-0000-0000-0000-000000000000`; AC-O1 is a documented manual verification (Step 6's README, walked at the final gate).

**AC coverage map** (from `_specs/3-AzureMonitorExporter.md`):
AC-AM4 + AC-AM7 (+ ratio-range and whitespace-as-absent edge cases) + AC-AM3 (unit half) → Step 1 · AC-AM1 + AC-AM2 + AC-AM10 (+ contribute-mode pairing, all-signals-off edge) → Step 2 · AC-AM5 + AC-AM6 (+ the OQ-2 rate-limited default) → Step 3 · AC-AM8 → Step 4 · AC-AM9 (+ owner-mode sampler skip) → Step 5 · AC-AM11 + AC-O2 (dependency-closure and loaded-assembly guards) + AC-ASP2 + AC-O1 (documented manual procedure) → Step 6 · AC-AM12 + AC-AM3/AM4 (running-app halves) → Step 7 · full walk at the final gate.

**New CPM entries** (`src/Directory.Packages.props`; the executor pins them in the step that first needs them):

| Package | Version | License | Step |
|---|---|---|---|
| `Azure.Monitor.OpenTelemetry.Exporter` | **1.8.3** (spec-verified stable, 2026-08-01; brings `Azure.Core` transitively) | MIT | 1 |
| `Azure.Identity` | latest stable — executor verifies on nuget.org at pin time (spec records it MIT, Microsoft-maintained) | MIT | 4 |

⚠️ **Risk areas (spec header + hand-off, reviewed at the covering gates):** the suite's **first `Azure.*` dependencies** (Gate 1) · **public API shape** — `AddAzureMonitor` on `CloudstrapObservabilityBuilder` is the builder-chained-leaf precedent for every future leaf (Decision Log OQ-1; Gate 1) · **auth-adjacent** — Entra ID credential attachment to the telemetry channel (Decision Log OQ-3; Gate 2) · an **amendment to the shipped `Cloudstrap.Observability` package** (Decision Log OQ-4; Gate 3) · the **AC-O2 regression** — the base package must keep loading zero Azure assemblies in Otlp/Console mode (Gate 3).

**Planner mechanics decided here (flagged for gate review, no spec conflict):**
(a) `AzureMonitorOptionsValidator` gets `internal AzureMonitorOptionsValidator(IConfiguration configuration, OpenTelemetryMode mode)` — the entry point registers the instance with `builder.Telemetry.Mode`; `IConfiguration` is consulted for `APPLICATIONINSIGHTS_CONNECTION_STRING`, mirroring Core's `OpenTelemetryOptionsValidator`. The connection-string presence rule applies only when the mode is `AzureMonitor`; the ratio-range (0.0–1.0) and mutual-exclusion rules apply whenever the section is bound (spec Validation row).
(b) **Everything that touches an `Azure.*` type lives in a separate internal class** (`AzureMonitorRegistration`) that the entry point calls only on the `AzureMonitor`-mode arm, so the inert path never JIT-compiles Azure code — the mechanism behind AC-AM4's "byte-identical" and the AC-O2 posture.
(c) Cloudstrap's exporter configuration is applied through the options pipeline (an internal `IConfigureOptions<AzureMonitorExporterOptions>` registered **before** the per-signal `AddAzureMonitor{Trace,Metric,Log}Exporter` calls, whose delegate argument carries the consumer hook) — registration order gives Cloudstrap-first / consumer-last per signal. The spec's planner verification notes (a)/(b) — standard-variable pickup on the DI path, and attaching the log exporter to the base package's already-configured OTel log pipeline via the idempotent `services.AddOpenTelemetry().WithLogging(...)` — are exactly what Step 2's RED tests prove; if the pinned exporter reads sampling at registration time instead of through options, the executor falls back to eagerly binding `Cloudstrap:AzureMonitor` inside `AzureMonitorRegistration` and reports the deviation at Gate 2.
(d) The scrub processor is `internal sealed BlazorHubScrubProcessor : BaseProcessor<Activity>` in the base package: `OnEnd` clears `ActivityTraceFlags.Recorded` on activities whose `rpc.service` tag equals `ComponentHub`, so export processors (which skip non-`Recorded` activities on the CPM-pinned OpenTelemetry 1.17.0 — spec planner note (c), proven RED in Step 5) never see them. It is added in owner mode, `Mode == AzureMonitor`, `EnableTracing`, `EnableBlazorHubTracing == false` — **before** the console exporter in the base tracing branch (and therefore before the leaf's exporters, which register later). It is deliberately **not** gated by `ApplySampler`: that knob governs sampler installation, and in `AzureMonitor` mode Cloudstrap installs no sampler at all (suppression parity, not sampler ownership).
(e) Step 2's AC-AM2 test sets the real process environment variable `APPLICATIONINSIGHTS_CONNECTION_STRING` (restored in `finally`) — the Azure SDK reads the process environment, not `IConfiguration`. The one sanctioned process-env mutation in the suite, confined to that fixture.
(f) SUT demo configuration (Step 7): `Cloudstrap:AzureMonitor:SamplingRatio = 1.0` for deterministic console-telemetry assertions (and a live demonstration of the AC-AM6 setting); `DisableOfflineStorage = true` via the `configure` hook per AC-AM12 so test runs leave no residue; `EnableConsole` stays at its default `true`, so the existing stdout-telemetry E2E assertions keep working alongside Azure Monitor — itself the spec's "console exporter alongside" behavior, demonstrated live.
(g) Idempotence (AC-AM10) via an internal marker type `TryAdd`ed into `Services` — second call short-circuits.

This package owns exactly one new configuration section, **`Cloudstrap:AzureMonitor`** — Core is **not** amended (spec finding 1). Everything else it reads is Core's shipped `Cloudstrap:OpenTelemetry` (`Mode`, `Enable*`, `AlwaysOnSampler`, `EnableBlazorHubTracing`) via `CloudstrapObservabilityBuilder.Telemetry`.

---

## Slice 1 — The unconditional call: `AddAzureMonitor()` is safe in every mode, and a broken `Cloudstrap:AzureMonitor` section fails startup loudly

---

## Step 1 — `Program.cs` can call `AddAzureMonitor()` unconditionally: no-op outside `AzureMonitor` mode, section bound + validated in every mode (AC-AM4, AC-AM7, AC-AM3 unit half)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Observability.AzureMonitor/Cloudstrap.Observability.AzureMonitor.csproj` *(create)* — Sdk project, `TargetFramework=net10.0`, `GeneratePackageOnBuild=true`, `GenerateDocumentationFile=true`; `<ProjectReference>` to `..\Cloudstrap.Observability\Cloudstrap.Observability.csproj` (one-way — the base never references back); `<PackageReference Include="Azure.Monitor.OpenTelemetry.Exporter" />`. Description/tags/README metadata land in Step 6.
- `src/Cloudstrap.Observability.AzureMonitor/AzureMonitorOptions.cs` *(create)* — `public sealed`, namespace `Cloudstrap.Observability.AzureMonitor`: `const string SectionName = "Cloudstrap:AzureMonitor"`; `ConnectionString : string?`; `SamplingRatio : float?`; `TracesPerSecond : double?`; `UseDefaultAzureCredential : bool` (default `false`). XML docs state the OQ-2 default prominently: *when neither sampling setting is configured, the exporter's platform default applies — rate-limited sampling at 5 traces/second, not 100%*.
- `src/Cloudstrap.Observability.AzureMonitor/AzureMonitorOptionsValidator.cs` *(create)* — `internal sealed : IValidateOptions<AzureMonitorOptions>`, planner mechanic (a).
- `src/Cloudstrap.Observability.AzureMonitor/CloudstrapObservabilityBuilderExtensions.cs` *(create)* — the one public entry point (Decision Log OQ-1); in this step the `AzureMonitor`-mode arm registers only binding + validation (exporters arrive in Step 2, so an `AzureMonitor`-mode host still fails at the base guard — correct-so-far behavior).
- `src/Test/UnitTest/Cloudstrap.Observability.AzureMonitor.Tests/Cloudstrap.Observability.AzureMonitor.Tests.csproj` *(create)* — mirror of `Cloudstrap.Observability.Tests.csproj`: Sdk + `TargetFramework=net10.0` + `<ProjectReference>` to the new package + version-less `<PackageReference>`s `Microsoft.Extensions.Configuration`, `Microsoft.Extensions.Configuration.Binder`, `Microsoft.Extensions.Hosting`, `OpenTelemetry.Exporter.InMemory`.
- `src/Test/UnitTest/Cloudstrap.Observability.AzureMonitor.Tests/AddAzureMonitorInertModeTests.cs` *(create)*
- `src/Test/UnitTest/Cloudstrap.Observability.AzureMonitor.Tests/AzureMonitorOptionsValidationTests.cs` *(create)*
- `src/Cloudstrap.sln` *(modify)* — new package at the solution root, test project under the `Test\UnitTest` solution folders (same nesting as the existing test projects).
- `src/Directory.Packages.props` *(modify)* — pin `Azure.Monitor.OpenTelemetry.Exporter` 1.8.3.

**RED** *(write these tests first; for a brand-new project the failure is the test project failing to compile against missing types — the standard RED for new code. Fixtures reuse the deliverable-2 idiom: `HostApplicationBuilder` + in-memory `Cloudstrap` config dictionary, `UseCloudstrapObservability().AddAzureMonitor()`)*:
- Unit test file: `AddAzureMonitorInertModeTests.cs`
  - `AddAzureMonitor_InOtlpMode_ReturnsTheSameBuilderAndKeepsOtlpBehavior` — `Mode=Otlp` + explicit endpoint: the returned builder is the same instance; `ConfigureOtlpExporter` capture still observes the `/v1/traces` configuration exactly as the base package alone (AC-AM4's byte-identical clause on the observable surface).
  - `AddAzureMonitor_InConsoleMode_HostStartsAndExportsToConsole` — Console-mode host with the call starts and a flushed test activity still appears in captured console output (no interference).
  - `AddAzureMonitor_InDisabledMode_RegistersNoTracerProvider` — inert stack stays inert.
  - `AddAzureMonitor_InAnyMode_BindsTheAzureMonitorSection` — `Mode=Console` + `Cloudstrap:AzureMonitor:SamplingRatio=0.5`: `host.Services.GetRequiredService<IOptions<AzureMonitorOptions>>().Value.SamplingRatio` is `0.5f` (the section binds even when the mode is not `AzureMonitor` — new observable behavior).
  - `AddAzureMonitor_OnNullBuilder_ThrowsArgumentNullException` (guard clause).
- Unit test file: `AzureMonitorOptionsValidationTests.cs` — validation observed through `IOptions<AzureMonitorOptions>.Value` (throws `OptionsValidationException`) and through host `StartAsync` (`ValidateOnStart`):
  - `Options_WithSamplingRatioAboveOne_FailsValidationEvenInConsoleMode` — `SamplingRatio=1.5` + `Mode=Console` → validation error naming `SamplingRatio` (spec edge case: typo caught before the mode flips).
  - `Options_WithSamplingRatioBelowZero_FailsValidation`.
  - `Options_WithBothSamplingSettings_FailsValidationNamingBoth` — `SamplingRatio=0.5` + `TracesPerSecond=3` → message names both settings as mutually exclusive (AC-AM7).
  - `Options_AzureMonitorModeWithNoConnectionStringAnywhere_FailsNamingBothSources` — `Mode=AzureMonitor`, neither the setting nor `APPLICATIONINSIGHTS_CONNECTION_STRING` in configuration → message names **both** `Cloudstrap:AzureMonitor:ConnectionString` and `APPLICATIONINSIGHTS_CONNECTION_STRING` (AC-AM3).
  - `Options_AzureMonitorModeWithWhitespaceConnectionString_TreatedAsAbsent` — `ConnectionString="   "` → the AC-AM3 failure (spec edge case).
  - `Options_AzureMonitorModeWithStandardVariableInConfiguration_PassesValidation` — the in-memory key `APPLICATIONINSIGHTS_CONNECTION_STRING` satisfies the rule (validator half of AC-AM2; the SDK-resolution half is Step 2).
  - `Options_OtlpModeWithNoConnectionString_PassesValidation` — the presence rule is `AzureMonitor`-mode-only.
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln   # RED = test project fails to compile against missing types
  src\Test\UnitTest\Cloudstrap.Observability.AzureMonitor.Tests\bin\Debug\net10.0\Cloudstrap.Observability.AzureMonitor.Tests.exe --filter "AzureMonitorOptionsValidationTests"
  ```

**GREEN**:
- `CloudstrapObservabilityBuilderExtensions` — `public static class` with `public static CloudstrapObservabilityBuilder AddAzureMonitor(this CloudstrapObservabilityBuilder builder, Action<AzureMonitorExporterOptions>? configure = null)` (spec Public API Sketch, verbatim): guard `ArgumentNullException.ThrowIfNull(builder)`; register `builder.Services.AddOptions<AzureMonitorOptions>().BindConfiguration(AzureMonitorOptions.SectionName).ValidateOnStart()` + the validator instance `new AzureMonitorOptionsValidator(…)` per planner mechanic (a) (the container's `IConfiguration` supplied via a factory registration); when `builder.Telemetry.Mode != OpenTelemetryMode.AzureMonitor` return `builder` with **no further registrations** (planner mechanic (b): the Azure-touching arm is a separate internal call that arrives in Step 2 — in this step the `AzureMonitor` arm registers nothing beyond binding/validation). Idempotence marker per planner mechanic (g) so the Step-1 registrations are also once-only. XML docs document the unconditional-call posture (spec Behaviors "Activation" row) and the OQ-2 sampling default.
- `AzureMonitorOptionsValidator` — planner mechanic (a): ratio range 0.0–1.0 inclusive; mutual exclusion; connection-string presence (setting non-whitespace, else `configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]` non-whitespace) only when the mode is `AzureMonitor`; failure messages name the exact setting keys.
- `AzureMonitorOptions` — properties + XML docs per Scope.

**DB changes**: none — this repository has no database.

**VERIFY** *(when all green, mark this step's `Done` checkbox and continue straight to the next step)*:
1. Test exe → all pass: a consumer can now leave `AddAzureMonitor()` in `Program.cs` permanently — Otlp/Console/Disabled behavior is untouched, the new section binds in every mode, and every configuration mistake (range, mutual exclusion, missing connection string in `AzureMonitor` mode) fails fast naming the offending keys — behavior that did not exist before (AC-AM4, AC-AM7, AC-AM3 unit half).
2. `dotnet build src/Cloudstrap.sln` → zero warnings/errors; existing suites (Core 52, Observability 91, E2E) still green via `runTests`; `dotnet format src/Cloudstrap.sln --verify-no-changes` → exit 0.
3. `dotnet build src/Cloudstrap.sln -c Release` → a `Cloudstrap.Observability.AzureMonitor.*.nupkg` appears under `src/Cloudstrap.Observability.AzureMonitor/bin/Release/` (packable from day one; metadata completed in Step 6).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 1: the entry-point shape and the first Azure dependency are frozen *(covers Step 1)*

*Executor: STOP here. Present the results and WAIT for user approval — do not start the next step.*

⚠️ **Risk areas at this gate**: the suite's **first `Azure.*` dependency** (`Azure.Monitor.OpenTelemetry.Exporter` 1.8.3, MIT, CPM-pinned; `Azure.Core` transitive) — rule-4 review · **public API surface** — `AddAzureMonitor(this CloudstrapObservabilityBuilder, Action<AzureMonitorExporterOptions>?)` is the **builder-chained-leaf precedent** (Decision Log OQ-1) every future leaf follows, and `AzureMonitorOptions` is permanent configuration surface; review the shape now, before exporters are built on it.

- [x] Behavioral verification: test exe output shows the inert-mode tests (same builder instance, Otlp/Console/Disabled untouched, section bound in every mode) and all seven validation cases (range, mutual exclusion naming both, missing-connection-string naming both sources, whitespace-as-absent, standard-variable pass, Otlp-mode pass) green.
- [x] Code review: entry-point signature + `AzureMonitorOptions` member names/nullability vs the spec's Public API Sketch, verbatim; validator mechanics (planner mechanic (a) — mode passed in, `IConfiguration` consulted, single rule set); the inert path registers nothing Azure-touching (planner mechanic (b) groundwork); XML docs state the OQ-2 rate-limited default prominently.
- [x] ⚠️ Dependency review (risk area): `dotnet list src/Cloudstrap.Observability.AzureMonitor/Cloudstrap.Observability.AzureMonitor.csproj package` — exporter 1.8.3 + `Cloudstrap.Observability` project reference, nothing else; zero `Aspire.*` (AC-ASP2).
- [x] User approved — implementation may continue past this gate

> **Gate 1 approved 2026-08-01.** Follow-ups accepted with the approval: the `NaN` hole in the
> `SamplingRatio` range check is fixed with a regression test as a Step 1 addendum (carried into Slice 2's
> verification). Three items deferred by decision: a `TracesPerSecond` range rule (spec question — settle
> before Step 3 applies the value), the `IConfiguration`-vs-process-environment asymmetry of the standard
> connection-string variable (repo-wide pattern; documented in Step 6's README), and the "first call wins"
> idempotence contract for a `configure` callback passed only to a second `AddAzureMonitor` call (Gate 2).

---

## Slice 2 — `AzureMonitor` mode delivers: exporters registered, guard lifted, sampling policy applied, Entra ID supported

---

## Step 2 — `Mode=AzureMonitor` + the chained call starts the host and registers per-signal exporters with the resolved connection string (AC-AM1, AC-AM2, AC-AM10)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Observability.AzureMonitor/AzureMonitorRegistration.cs` *(create)* — `internal static`, planner mechanic (b): the only place `Azure.*` types are touched.
- `src/Cloudstrap.Observability.AzureMonitor/AzureMonitorExporterSetup.cs` *(create)* — `internal sealed : IConfigureOptions<AzureMonitorExporterOptions>`, planner mechanic (c): applies `ConnectionString` **only when the Cloudstrap setting is non-whitespace** (otherwise leaves it unset for the SDK — AC-AM2, mirroring `OtlpExporterSetup`'s leave-the-SDK-alone posture); sampling and credential application grow in Steps 3–4.
- `src/Cloudstrap.Observability.AzureMonitor/CloudstrapObservabilityBuilderExtensions.cs` *(modify)* — the `AzureMonitor`-mode arm now calls `AzureMonitorRegistration.Register(builder, configure)`.
- `src/Test/UnitTest/Cloudstrap.Observability.AzureMonitor.Tests/AddAzureMonitorRegistrationTests.cs` *(create)*

**RED** *(write these tests first, run them, confirm they fail — fixture config: `Mode=AzureMonitor`, `EnableConsole=false`, `Cloudstrap:AzureMonitor:ConnectionString=InstrumentationKey=00000000-0000-0000-0000-000000000000`; exporter options observed through the consumer `configure` hook — the exact `ConfigureOtlpExporter`-capture idiom from deliverable 2; no live Azure, no network)*:
- Unit test file: `AddAzureMonitorRegistrationTests.cs`
  - `AddAzureMonitor_AzureMonitorMode_HostStartsCleanly` — `host.StartAsync()` succeeds: the shipped AC-B7 guard (which failed this exact host before) is lifted via `MarkExporterContributed()` — the headline new behavior (AC-AM1 startup half).
  - `AddAzureMonitor_AzureMonitorMode_AppliesCloudstrapConnectionStringToExporterOptions` — the hook capture observes `ConnectionString == "InstrumentationKey=00000000-0000-0000-0000-000000000000"` (explicit setting wins — spec edge case "both present": the setting is set on the options, the SDK only falls back when unset).
  - `AddAzureMonitor_WithOnlyStandardVariable_LeavesConnectionStringUnset` — planner mechanic (e): process env `APPLICATIONINSIGHTS_CONNECTION_STRING` set to the dummy value (restored in `finally`), no Cloudstrap setting → host starts, hook capture observes `ConnectionString` unset — Cloudstrap configured nothing, the SDK resolves the variable itself (AC-AM2; spec planner note (a) proven).
  - `AddAzureMonitor_RegistersTracerAndMeterProviders` — `TracerProvider` and `MeterProvider` resolve non-null; a logged event confirms the OTel logging pipeline is present (log-exporter attachment to the base pipeline — spec planner note (b) proven).
  - `AddAzureMonitor_WithEnableTracingFalse_DoesNotResurrectTracing` — `EnableTracing=false` → `GetService<TracerProvider>()` stays null (the leaf gates on Core's flags itself — AC-AM1's "exactly the enabled signals"; same assertion shape for `EnableMetrics=false` → no `MeterProvider`).
  - `AddAzureMonitor_WithEnableLogsFalse_AddsNoOtelLoggerProvider` — no OpenTelemetry `ILoggerProvider` among `GetServices<ILoggerProvider>()`.
  - `AddAzureMonitor_WithAllSignalsDisabled_HostStillStarts` — guard satisfied, nothing exported (spec edge case).
  - `AddAzureMonitor_CalledTwice_ConfiguresExportersOnce` — hook invocation count equals the once-called host's count (AC-AM10, planner mechanic (g)).
  - `AddAzureMonitor_ConsumerHook_RunsLastAndWins` — the hook overwrites `ConnectionString`; the final options carry the hook's value.
  - `AddAzureMonitor_InContributeMode_AddsExportersToTheHostPipeline` — ServiceDefaults-shaped fixture (host registers `AddOpenTelemetry().WithTracing(...)` first, `PipelineMode=Contribute`): the hook fires and the host starts (no guard in contribute mode) — the legitimate Aspire-app pairing (spec finding 8).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Observability.AzureMonitor.Tests\bin\Debug\net10.0\Cloudstrap.Observability.AzureMonitor.Tests.exe --filter "AddAzureMonitorRegistrationTests"
  ```

**GREEN**:
- `AzureMonitorRegistration.Register(CloudstrapObservabilityBuilder builder, Action<AzureMonitorExporterOptions>? configure)` — register `AzureMonitorExporterSetup` as `IConfigureOptions<AzureMonitorExporterOptions>` (before the exporter calls — planner mechanic (c)); `OpenTelemetryBuilder openTelemetry = builder.Services.AddOpenTelemetry();` (idempotent — contributes to the pipeline the base package built); gated per `builder.Telemetry`: `EnableTracing` → `openTelemetry.WithTracing(tracing => tracing.AddAzureMonitorTraceExporter(options => configure?.Invoke(options)))`, `EnableMetrics` → `WithMetrics(... AddAzureMonitorMetricExporter ...)`, `EnableLogs` → `WithLogging(configureBuilder: null, configureOptions: loggerOptions => loggerOptions.AddAzureMonitorLogExporter(options => configure?.Invoke(options)))` — the founding-spec-named per-signal methods, nothing from the Reject list; finally `builder.MarkExporterContributed()`.
- `AzureMonitorExporterSetup` — ctor `(IOptions<AzureMonitorOptions> options)`: apply `ConnectionString` per Scope. (Sampling: Step 3. Credential: Step 4.)
- XML docs on everything public; internal classes documented too (repo style).

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: an `AzureMonitor`-mode host that refused to start yesterday now starts and carries per-signal Azure Monitor exporters wired to the resolved connection string, exactly for the enabled signals, idempotently, with the consumer hook last (AC-AM1, AC-AM2, AC-AM10).
2. `dotnet build src/Cloudstrap.sln` → zero warnings; full `runTests` green; `dotnet format src/Cloudstrap.sln --verify-no-changes` → exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 3 — Sampling policy: platform default untouched, `SamplingRatio`/`TracesPerSecond` applied, `AlwaysOnSampler` records everything (AC-AM5, AC-AM6, Decision Log OQ-2)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Observability.AzureMonitor/AzureMonitorExporterSetup.cs` *(modify)* — sampling application (needs `OpenTelemetryOptions.AlwaysOnSampler`, supplied by `AzureMonitorRegistration` from `builder.Telemetry`).
- `src/Test/UnitTest/Cloudstrap.Observability.AzureMonitor.Tests/AzureMonitorSamplingTests.cs` *(create)*

**RED** *(write these tests first, run them, confirm they fail — policy asserted through the hook capture against `new AzureMonitorExporterOptions()` defaults, plus behavioral stamping tests through an InMemory exporter added via `ConfigureTracing`)*:
- Unit test file: `AzureMonitorSamplingTests.cs`
  - `AddAzureMonitor_WithNeitherSamplingSetting_LeavesExporterSamplingAtSdkDefaults` — captured options' `SamplingRatio`/`TracesPerSecond` equal a fresh `AzureMonitorExporterOptions`' values: Cloudstrap set nothing, the platform's rate-limited 5-traces/second default governs (Decision Log OQ-2).
  - `AddAzureMonitor_WithSamplingRatio_AppliesFixedPercentage` — `Cloudstrap:AzureMonitor:SamplingRatio=0.25` → captured `SamplingRatio == 0.25f`, `TracesPerSecond` untouched (AC-AM6 policy half).
  - `AddAzureMonitor_WithTracesPerSecond_AppliesRateLimit` — `TracesPerSecond=2.5` → captured value `2.5`.
  - `AddAzureMonitor_WithAlwaysOnSamplerFlag_ForcesFullSamplingAndIgnoresBothSettings` — `Cloudstrap:OpenTelemetry:AlwaysOnSampler=true` + `SamplingRatio=0.1` + no `TracesPerSecond` conflict → captured `SamplingRatio == 1.0f` and `TracesPerSecond` unset (AC-AM5: diagnosis flag beats policy).
  - `AddAzureMonitor_WithFullSampling_ExportsEveryTraceWithStampedSampleRate` — `AlwaysOnSampler=true`, InMemory exporter via `ConfigureTracing`, N test activities → all N exported and each carries the Application Insights sample-rate attribute (the AI sampler is active and stamping — the renormalization contract of AC-AM5/AM6; the executor asserts the exact attribute key the pinned exporter stamps, discovered in RED).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Observability.AzureMonitor.Tests\bin\Debug\net10.0\Cloudstrap.Observability.AzureMonitor.Tests.exe --filter "AzureMonitorSamplingTests"
  ```

**GREEN**:
- `AzureMonitorExporterSetup` — sampling block: when `AlwaysOnSampler` → `SamplingRatio = 1.0f` (and leave `TracesPerSecond` unset); else when `AzureMonitorOptions.SamplingRatio` set → apply it; else when `TracesPerSecond` set → apply it; else touch nothing (OQ-2: inherit the platform default; validation already rejected the both-set case in Step 1). XML docs on `AzureMonitorOptions.SamplingRatio`/`TracesPerSecond` cross-reference the default and the mutual exclusion.

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: the decided sampling policy is now observable end to end — untouched platform default, fixed-percentage and rate-limited overrides, and the dev flag recording every stamped trace (AC-AM5, AC-AM6).
2. `dotnet build src/Cloudstrap.sln` → zero warnings; full `runTests` green; `dotnet format src/Cloudstrap.sln --verify-no-changes` → exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 4 — Entra ID ingestion auth: the `UseDefaultAzureCredential` flag attaches `DefaultAzureCredential`; a hook-supplied credential wins ⚠️ *(Risk Area: auth-adjacent — AC-AM8, Decision Log OQ-3)*

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Observability.AzureMonitor/Cloudstrap.Observability.AzureMonitor.csproj` *(modify)* — add `<PackageReference Include="Azure.Identity" />`.
- `src/Directory.Packages.props` *(modify)* — pin `Azure.Identity` (latest stable, verified on nuget.org at pin time — MIT).
- `src/Cloudstrap.Observability.AzureMonitor/AzureMonitorExporterSetup.cs` *(modify)* — credential application.
- `src/Test/UnitTest/Cloudstrap.Observability.AzureMonitor.Tests/AzureMonitorCredentialTests.cs` *(create)*

**RED** *(write these tests first, run them, confirm they fail — boundary assertions on the captured `AzureMonitorExporterOptions.Credential` only; constructing `DefaultAzureCredential` performs no token acquisition, and no test ever triggers one — no live Azure, per the spec's test strategy)*:
- Unit test file: `AzureMonitorCredentialTests.cs`
  - `AddAzureMonitor_WithDefaultFlag_AttachesNoCredential` — flag absent → captured `Credential` is null (connection-string local auth, the default).
  - `AddAzureMonitor_WithUseDefaultAzureCredential_AttachesDefaultAzureCredential` — `Cloudstrap:AzureMonitor:UseDefaultAzureCredential=true` → captured `Credential` is a `DefaultAzureCredential` instance (AC-AM8 flag half).
  - `AddAzureMonitor_WithHookSuppliedCredential_WinsOverTheFlag` — flag `true` **and** the `configure` hook sets a stub `TokenCredential` → the captured `Credential` is the hook's instance, not `DefaultAzureCredential` (AC-AM8 precedence half; the stub is a local `internal sealed class` test double extending `TokenCredential` — no Moq needed at this boundary, matching the repo's fixture style).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Observability.AzureMonitor.Tests\bin\Debug\net10.0\Cloudstrap.Observability.AzureMonitor.Tests.exe --filter "AzureMonitorCredentialTests"
  ```

**GREEN**:
- `AzureMonitorExporterSetup` — when `AzureMonitorOptions.UseDefaultAzureCredential`, set `Credential = new DefaultAzureCredential()`. The consumer hook runs after (planner mechanic (c) ordering), so a hook-supplied credential wins by construction. XML docs on `UseDefaultAzureCredential` state the founding-spec credential posture (works identically on Azure Web Apps, containers, dev machines) and the hook-wins rule.

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: Entra-only ingestion is now a per-environment setting with no code change, and a code-supplied credential always has the final say — with zero token acquisition in the test run (AC-AM8).
2. `dotnet build src/Cloudstrap.sln` → zero warnings; full `runTests` green; `dotnet format src/Cloudstrap.sln --verify-no-changes` → exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 2: the flagship mode works *(covers Steps 2–4)*

*Executor: STOP here. Present the results of all covered steps and WAIT for user approval — do not start the next step.*

⚠️ **Risk areas at this gate**: **auth-adjacent** — the credential surface (Step 4: the flag, `DefaultAzureCredential` construction, hook precedence) requires explicit human review (Decision Log OQ-3) · the second Azure dependency (`Azure.Identity`) — rule-4 review with the executor-verified version · planner mechanic (c)/(e) mechanics decided by the executor during RED (options-pipeline vs eager-bind fallback; the one process-env mutation).

- [x] Behavioral verification: test exe output shows — the guard-lift start (a previously failing host now starts), Cloudstrap-setting-wins and SDK-resolves-variable connection-string cases, per-signal gating incl. all-signals-off, idempotent double call, hook-last precedence, contribute-mode pairing (Step 2); the four sampling policies incl. the untouched platform default and the stamped-sample-rate export proof (Step 3); the three credential cases with no token acquisition (Step 4).
- [x] Code review: `AzureMonitorRegistration`/`AzureMonitorExporterSetup` vs the spec's Behaviors table (per-signal methods only — nothing from the Reject list; `MarkExporterContributed` placement; hook-last ordering; nothing set that the SDK owns — standard metrics, offline storage, performance counters untouched); planner mechanic (b) isolation of Azure-touching code; report any mechanic-(c) fallback taken.
- [x] ⚠️ Auth review (risk area): the credential attachment path end to end — flag semantics, `DefaultAzureCredential` construction point, hook-wins rule, XML-doc accuracy; confirm no credential material ever appears in configuration beyond the boolean flag.
- [x] ⚠️ Dependency review (risk area): `Azure.Identity` pin (version, MIT, CPM).
- [x] User approved — implementation may continue past this gate

> **Gate 2 approved 2026-08-02.** Deviations accepted: mechanic (c) held (no eager-bind fallback), but logs
> use the DI-based `LoggerProviderBuilder` overload of `AddAzureMonitorLogExporter` so all three signals share
> one configuration path; `AzureMonitorExporterSetup` reads `AlwaysOnSampler` from `IOptions<OpenTelemetryOptions>`
> rather than from `builder.Telemetry`. Four RED discoveries recorded: applying `SamplingRatio` requires
> clearing the exporter's default `TracesPerSecond = 5.0` or the ratio is silently ignored; the SDK eagerly
> resolves `APPLICATIONINSIGHTS_CONNECTION_STRING` into the options; the stamped sample rate is written during
> wire-format conversion and is therefore not observable in-process (renormalization moves to the AC-O1 manual
> procedure); and in `AzureMonitor` mode configuration errors surface at `builder.Build()` rather than
> `StartAsync`. Follow-ups accepted with the approval: one shared `DefaultAzureCredential` instead of one per
> signal. Confirmed as-is: the "first call wins" idempotence contract — a `configure` callback passed only to a
> second `AddAzureMonitor` call is discarded; documented in Step 6's README.

---

## Slice 3 — The flagship mode keeps its manners: Blazor hub parity in the shipped base, guarded closures, publishable package

---

## Step 5 — Blazor Server apps in `AzureMonitor` mode still export no `ComponentHub` spans: base-package amendment — skip `SetSampler`, export-time scrub ⚠️ *(Risk Area: amendment to the shipped `Cloudstrap.Observability` package — AC-AM9, Decision Log OQ-4)*

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope** *(base package + both test projects; zero `Azure.*` references enter the base — AC-O2 untouched)*:
- `src/Cloudstrap.Observability/BlazorHubScrubProcessor.cs` *(create)* — planner mechanic (d).
- `src/Cloudstrap.Observability/OpenTelemetryPipeline.cs` *(modify)* — owner-mode tracing branch: `SetSampler` is skipped when `Mode == AzureMonitor` (the exporter's AI sampler would override it anyway — spec finding 2); when additionally `EnableBlazorHubTracing == false`, add the scrub processor **before** the console exporter. Contribute mode untouched (the spec scopes the amendment to owner mode; the exporter-sampler caveat there is documented in Step 6's README).
- `src/Cloudstrap.Observability/README.md` *(modify)* — mode-table row for `AzureMonitor`: sampler ownership moves to the AI sampler, hub suppression via export-time scrub, and the budget caveat (under rate-limited sampling, hub invocations consume traces-per-second budget before being scrubbed — Blazor Server apps should prefer `SamplingRatio`).
- `src/Test/UnitTest/Cloudstrap.Observability.Tests/BlazorHubSamplingTests.cs` *(modify)* — AzureMonitor-mode cases (base-side, no Azure dependency: `Mode=AzureMonitor` + `MarkExporterContributed()` + InMemory exporter via `ConfigureTracing`).
- `src/Test/UnitTest/Cloudstrap.Observability.AzureMonitor.Tests/AzureMonitorHubScrubTests.cs` *(create)* — leaf-side parity proof with the real AI sampler installed.

**RED** *(write these tests first, run them, confirm they fail)*:
- Unit test file (base): `BlazorHubSamplingTests.cs` — new methods:
  - `AzureMonitorMode_ComponentHubActivity_IsSampledInButNotExported` — a server-kind activity created with `rpc.service=ComponentHub` in its tags has `Recorded == true` right after start (**no sampler dropped it** — the `SetSampler` skip is observable) yet is absent from the InMemory exporter after flush (**the scrub removed it at export time**) — the two halves of Decision Log OQ-4 in one behavioral test.
  - `AzureMonitorMode_PlainActivity_IsExported` — a normal request span from the same source still exports (AC-AM9's second half).
  - `AzureMonitorMode_WithEnableBlazorHubTracing_ExportsHubSpans` — the override lifts the scrub.
  - `ConsoleMode_KeepsSamplerBasedHubSuppression` — regression pin: outside `AzureMonitor` mode the shipped `BlazorHubSampler` chain is unchanged (existing tests stay green; this method pins the boundary explicitly).
- Unit test file (leaf): `AzureMonitorHubScrubTests.cs`
  - `AddAzureMonitor_ComponentHubSpan_DoesNotReachExportersWhileNormalSpanDoes` — full stack: `Mode=AzureMonitor` + dummy connection string + `AddAzureMonitor()` + InMemory exporter via `ConfigureTracing` + `AlwaysOnSampler=true` (so the AI sampler records everything and the only suppression left is the scrub): hub-tagged span absent, plain span present — AC-AM9 verbatim, in the flagship mode with the real AI sampler active.
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Observability.Tests\bin\Debug\net10.0\Cloudstrap.Observability.Tests.exe --filter "BlazorHubSamplingTests"
  src\Test\UnitTest\Cloudstrap.Observability.AzureMonitor.Tests\bin\Debug\net10.0\Cloudstrap.Observability.AzureMonitor.Tests.exe --filter "AzureMonitorHubScrubTests"
  ```

**GREEN**:
- `BlazorHubScrubProcessor` — `internal sealed : BaseProcessor<Activity>`; `OnEnd`: if `activity.GetTagItem("rpc.service")` equals `"ComponentHub"` (reuse the constants — extract them from `BlazorHubSampler` into a shared internal spot rather than duplicating), clear `ActivityTraceFlags.Recorded` so downstream export processors skip the activity (planner mechanic (d); spec planner note (c) — the reliance on export processors skipping non-`Recorded` activities on OpenTelemetry 1.17.0 is exactly what the RED tests prove).
- `OpenTelemetryPipeline.ConfigureOwner` — tracing branch per Scope; XML docs on the changed member document the AzureMonitor-mode behavior; no public-surface change (spec: "no public surface change").

**DB changes**: none.

**VERIFY**:
1. Both test exes → all pass: AC-B10 parity now holds in the flagship mode — hub spans are sampled in but never exported, normal spans flow, the override works, and non-AzureMonitor modes are bit-for-bit unchanged (AC-AM9).
2. `dotnet build src/Cloudstrap.sln` → zero warnings; full `runTests` green (all existing Observability tests untouched); `dotnet format src/Cloudstrap.sln --verify-no-changes` → exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 6 — The package is publishable and the closures are permanently guarded: metadata + README (incl. the AC-O1 manual procedure), surface guards, AC-O2 re-proven (AC-AM11, AC-O2, AC-ASP2, AC-O1 documentation)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Observability.AzureMonitor/Cloudstrap.Observability.AzureMonitor.csproj` *(modify)* — `<Description>` (Application Insights exporter for the Cloudstrap observability pipeline — per-signal Azure Monitor export, sampling policy, Entra ID ingestion auth, driven by `Cloudstrap:AzureMonitor`), `<PackageTags>$(PackageTags);observability;opentelemetry;azuremonitor;applicationinsights;exporter</PackageTags>`, `<PackageReadmeFile>README.md</PackageReadmeFile>` + `<None Include="README.md" Pack="true" PackagePath="/" />`.
- `src/Cloudstrap.Observability.AzureMonitor/README.md` *(create)* — quick start (`UseCloudstrapObservability().AddAzureMonitor()` + neutral `appsettings.json`); the `Cloudstrap:AzureMonitor` settings table; **the OQ-2 default stated loudly** (rate-limited 5 traces/second when nothing configured; how to raise volume); connection-string resolution order (setting → `APPLICATIONINSIGHTS_CONNECTION_STRING` → startup failure; sovereign clouds via the connection string's own `IngestionEndpoint`); the `AlwaysOnSampler` dev flag; Entra ID (`UseDefaultAzureCredential`, hook-supplied credential wins); the `configure` hook's SDK escape hatches (`EnableStandardMetrics`, `StorageDirectory`, `DisableOfflineStorage`, …); Blazor hub scrub + the rate-limit budget caveat (prefer `SamplingRatio` on Blazor Server) and the contribute-mode note (host owns suppression there; the exporter's sampler applies); the one-owner rule for Aspire apps (wire the App Insights exporter in ServiceDefaults *or* through Cloudstrap, never both — AC-ASP1 posture); **Live Metrics limitation + escape hatches** (contribute mode with the consumer's own `UseAzureMonitorExporter` call, or the distro — Decision Log OQ-5); failure isolation (export failures never crash the app; `OpenTelemetry-AzureMonitor-Exporter` EventSource as the diagnostic channel); **the AC-O1 manual verification procedure** — step-by-step against a real Application Insights resource (create resource → set the real connection string → run a request → verify request trace, dependency spans, logs and runtime metrics correlated by `operation_Id` in the portal).
- `src/Test/UnitTest/Cloudstrap.Observability.AzureMonitor.Tests/PackageSurfaceTests.cs` *(create)* — permanent guards, mirroring the base idiom.
- `src/Test/UnitTest/Cloudstrap.Observability.Tests/PackageSurfaceTests.cs` *(modify)* — the runtime AC-O2 tripwire.

**RED** *(guard tests are written and run first but, as tripwires against correct code, may pass immediately — the honest failing state is in the artifacts: before GREEN the Release nupkg has no README/description/tags; recorded per the plan-2 Step-12 precedent)*:
- Unit test file (leaf): `PackageSurfaceTests.cs`
  - `ReferencedAssemblies_OfAzureMonitorAssembly_MatchTheApprovedClosure` — every referenced assembly name starts with `System`, `Microsoft.`, `OpenTelemetry`, `Azure.`, or equals `Cloudstrap.Core`/`Cloudstrap.Observability`; explicitly assert **zero** names starting `Aspire` (AC-ASP2), `LanguageExt`, `NServiceBus`.
  - `PublicTypes_OfAzureMonitorAssembly_ContainNoForbiddenIdentifiers` — no public type/member matches `(?i)nihdi|riziv|dynatrace|nservicebus` (AC-AM11).
  - `PublicTypes_OfAzureMonitorAssembly_AreSealedAndInTheSingleApprovedNamespace` — namespace `Cloudstrap.Observability.AzureMonitor` only; public classes sealed or static.
- Unit test file (base): `PackageSurfaceTests.cs` — new method:
  - `OtlpMode_HostLifecycle_LoadsNoAzureAssemblies` — build + start + stop an Otlp-mode host through `UseCloudstrapObservability` (base package only — this project cannot even reference the leaf), then assert `AppDomain.CurrentDomain.GetAssemblies()` contains no name starting `Azure` — the runtime half of the AC-O2 regression re-proof, now that Azure packages exist in the solution; the compile-time half is the existing `ReferencedAssemblies_OfObservabilityAssembly_MatchTheApprovedClosure` guard, which must still pass unmodified.
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Observability.AzureMonitor.Tests\bin\Debug\net10.0\Cloudstrap.Observability.AzureMonitor.Tests.exe --filter "PackageSurfaceTests"
  ```

**GREEN**: add the csproj metadata and write `README.md` per Scope; add the base tripwire.

**DB changes**: none.

**VERIFY**:
1. Both test exes (full run) → all tests pass, including the four new guards.
2. `dotnet build src/Cloudstrap.sln -c Release` → `src/Cloudstrap.Observability.AzureMonitor/bin/Release/Cloudstrap.Observability.AzureMonitor.<version>.nupkg`; expand a `.zip` copy → contains `README.md`, `icon.png`, `lib/net10.0/Cloudstrap.Observability.AzureMonitor.dll` **and** `.xml`; nuspec shows MIT license expression, description, tags, repository URL, and the dependency list (`Azure.Monitor.OpenTelemetry.Exporter`, `Azure.Identity`, `Cloudstrap.Observability` — no `Aspire.*`) (AC-AM11 metadata half).
3. **AC-AM11 identifier sweep** (new package + tests):
   ```powershell
   Get-ChildItem -Recurse -File -Path src/Cloudstrap.Observability.AzureMonitor, src/Test/UnitTest/Cloudstrap.Observability.AzureMonitor.Tests |
     Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
     Select-String -Pattern '(?i)(nihdi|riziv|dynatrace)'
   ```
   → zero matches (guard-test tripwire patterns are self-referential and excluded by reading the hits, as in plan 2).
4. **Closure checks**: `dotnet list src/Cloudstrap.Observability.AzureMonitor/Cloudstrap.Observability.AzureMonitor.csproj package` → exactly the two Azure pins + the `Cloudstrap.Observability` project reference; `dotnet list src/Cloudstrap.Observability/Cloudstrap.Observability.csproj package` → **unchanged** — zero `Azure.*` (AC-O2 dependency half), zero `Aspire.*` (AC-ASP2).
5. Full gates on the final tree: `dotnet build src/Cloudstrap.sln` (zero warnings) + `runTests` (all suites) + `dotnet format src/Cloudstrap.sln --verify-no-changes` (exit 0).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 3: the shipped base amendment and the guarded closures *(covers Steps 5–6)*

*Executor: STOP here. Present the results of all covered steps and WAIT for user approval — do not start the next step.*

⚠️ **Risk areas at this gate**: Step 5 changes behavior of the **shipped `Cloudstrap.Observability` package** (spec-committed, Decision Log OQ-4, but it is a behavior change in a published-surface package — review the owner-mode-only scoping, the `SetSampler` skip condition, and planner mechanic (d)'s decision to not gate the scrub on `ApplySampler`) · the **AC-O2 regression** guards are the permanent tripwires every future deliverable relies on.

- [x] Behavioral verification: base test exe shows sampled-in-but-not-exported hub spans, plain spans exported, the `EnableBlazorHubTracing` override, and unchanged Console-mode suppression; leaf test exe shows the full-stack AC-AM9 parity with the real AI sampler; both `PackageSurfaceTests` fixtures green incl. the new loaded-assembly tripwire; expanded `.nupkg` contents reviewed; identifier sweep empty; both `dotnet list package` closures reviewed.
- [x] Code review: `BlazorHubScrubProcessor` mechanics (tag constant sharing with `BlazorHubSampler`, `Recorded`-flag clearing, processor-before-exporter ordering incl. the console exporter); the amendment's condition set vs the spec sentence ("owner mode, `Mode = AzureMonitor`, skip `SetSampler`; scrub when `EnableBlazorHubTracing = false`"); ⚠️ planner mechanic (d) — scrub not gated by `ApplySampler` — confirm or direct a change; README accuracy for both packages (base mode-table row, leaf README incl. the OQ-2 default wording, the Live Metrics escape hatches, and the AC-O1 manual procedure).
- [x] User approved — implementation may continue past this gate

> **Gate 3 approved 2026-08-02.** Mechanic (d) ratified as built: the export-time scrub is **not** gated by
> `ApplySampler`, accepting the deliberate asymmetry that `ApplySampler = false` exports hub spans in
> Console/Otlp but suppresses them in `AzureMonitor` — now pinned by
> `AzureMonitorMode_WithApplySamplerFalse_StillScrubsHubSpans`. Scrub correctness was verified empirically
> against OpenTelemetry 1.17.0 (both export processors early-return on `!Recorded`; `OnEnd` order is
> scrub → console → `ConfigureTracing` hook → Azure Monitor exporter). Known deviation from exact AC-AM9
> parity, accepted as documented-only: export-time scrubbing suppresses the hub span but **not its
> descendants**, which the parent-based sampler drops in Console/Otlp mode. Also accepted: clearing
> `Recorded` mutates the shared `Activity`, so a second OpenTelemetry pipeline in the same process would not
> see hub spans either; and `AzureMonitor` mode loses parent-based sampling (a Gate 2 consequence).

---

## Slice 4 — Demonstration: the WASM SUT runs in `AzureMonitor` mode, proven through the running app (AC-AM12)

---

## Step 7 — The SUT's Bff boots in `AzureMonitor` mode with one chained call; E2E tests prove the guard lift, fail-fast, and per-environment mode flipping through the real app

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Test/WasmTestProject/src/Host/Bff/Cloudstrap.WasmTestProject.Host.Bff.csproj` *(modify)* — `<ProjectReference>` to `Cloudstrap.Observability.AzureMonitor`.
- `src/Test/WasmTestProject/src/Host/Bff/Program.cs` *(modify)* — the one-line demo: `builder.UseCloudstrapObservability().AddAzureMonitor(options => options.DisableOfflineStorage = true);` (unconditional call — the mode decides; offline storage disabled per AC-AM12 so test runs leave no residue).
- `src/Test/WasmTestProject/src/Host/Bff/appsettings.json` *(modify)* — `Cloudstrap:OpenTelemetry:Mode` → `"AzureMonitor"`; new section `Cloudstrap:AzureMonitor` with `ConnectionString: "InstrumentationKey=00000000-0000-0000-0000-000000000000"` (syntactically valid, unreachable — the exporter retries in the background, never crashing the app: the spec's failure-isolation row, demonstrated live) and `SamplingRatio: 1.0` (planner mechanic (f) — deterministic stdout telemetry; `EnableConsole` default keeps the Console exporter alongside, so the existing `DoctorsTests` stdout assertions keep passing in the flagship mode).
- `src/Test/WasmTestProject/test/Cloudstrap.WasmTestProject.E2E.Tests/AzureMonitorTests.cs` *(create)*
- `src/Test/WasmTestProject/test/Cloudstrap.WasmTestProject.E2E.Tests/DiagnosticsTests.cs` *(modify)* — the expected OTel mode on the diagnostics page changes `Console` → `AzureMonitor`.
- `src/Test/WasmTestProject/README.md` *(modify)* — demo-table row: `/diagnostics` (mode badge) + startup scenarios | Cloudstrap.Observability.AzureMonitor (#3) | `AzureMonitorTests` — guard lift, fail-fast on missing connection string, per-environment mode flip.

**RED** *(write these tests first, run them, confirm they fail — the Bff still runs Console mode without the new wiring, so all three fail)*:
- E2E test file: `src/Test/WasmTestProject/test/Cloudstrap.WasmTestProject.E2E.Tests/AzureMonitorTests.cs`
- E2E test methods:
  - `AzureMonitorMode_SutBoots_AndDiagnosticsShowsAzureMonitorMode` — the fixture-booted app is *running in `AzureMonitor` mode* (the shipped guard would have killed it without `AddAzureMonitor` — the lift proven through the real app, AC-AM1/AM12); Playwright asserts the `/diagnostics` page shows OTel mode `AzureMonitor`, no console errors.
  - `Startup_AzureMonitorWithoutConnectionString_FailsFastNamingBothSources` — `SutProcess.Start` launches a second short-lived instance with application argument `--Cloudstrap:AzureMonitor:ConnectionString=` (blanks the setting; the standard variable is not set in the child environment) → non-zero exit, captured output names both `Cloudstrap:AzureMonitor:ConnectionString` and `APPLICATIONINSIGHTS_CONNECTION_STRING` (AC-AM3 through the real app; precedent: `Startup_MissingSystemName_FailsFastWithValidationError`).
  - `Startup_ModeFlippedToConsole_UnchangedCodeBootsAndServes` — a second instance on `http://127.0.0.1:5301` with `--Cloudstrap:OpenTelemetry:Mode=Console` → polls `/healthz` to 200 within a deadline, then disposes: the same binary with the same unconditional `AddAzureMonitor()` call runs happily in Console mode — AC-AM4's per-environment mode flipping, live.
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\WasmTestProject\test\Cloudstrap.WasmTestProject.E2E.Tests\bin\Debug\net10.0\Cloudstrap.WasmTestProject.E2E.Tests.exe --filter "AzureMonitor"
  ```

**GREEN**: the Scope items — csproj reference, the chained call, the config flip, the README row. Existing E2E suites (`HomePageTests`, `DiagnosticsTests` as amended, `HealthAndCorrelationTests`, `DoctorsTests`) must stay green in the flipped mode — `DoctorsTests.AddDoctor_EmitsBusinessTraceInConsoleTelemetry` now doubles as the live proof of the spec's "console exporter alongside Azure Monitor" row. *(If the stdout assertions prove timing-sensitive under the AI sampler despite `SamplingRatio: 1.0`, the executor reports at the gate rather than weakening the assertions.)*

**DB changes**: none.

**VERIFY**:
1. E2E exe → the three new tests pass and every pre-existing E2E test passes in `AzureMonitor` mode (build first; one-time `playwright.ps1 install chromium` if needed).
2. Full gates on the final tree: `dotnet build src/Cloudstrap.sln` (zero warnings) + `runTests` (Core, Observability, AzureMonitor, E2E — all green) + `dotnet format src/Cloudstrap.sln --verify-no-changes` (exit 0).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — final: deliverable #3 complete *(covers Step 7; closes the deliverable)*

*Executor: STOP here. Present the results and WAIT for user approval. Any Git push afterwards requires the user's explicit go-ahead (CLAUDE.md: no push without confirmation).*

- [x] Behavioral verification: the three `AzureMonitorTests` methods pass; the full E2E suite passes with the SUT in `AzureMonitor` mode (incl. the console-telemetry `DoctorsTests` proving the console-alongside behavior); user optionally runs the SUT manually (`dotnet run --project src/Test/WasmTestProject/src/Host/Bff`) and sees it serving under `AzureMonitor` mode with a dummy connection string — no crash, telemetry visible on stdout.
- [x] AC-O1 (manual, headline criterion): user reviews the README's manual verification procedure; optionally executes it against a real Application Insights resource (real connection string, one request, portal shows request trace + dependencies + logs + runtime metrics correlated by operation ID). The deliverable is demonstrable per the roadmap's definition of done either way — the procedure is the documented artifact.
- [x] Spec acceptance sign-off: walk AC-O1, AC-O2, AC-ASP2 and AC-AM1…AC-AM12 against the step evidence using the Overview's AC coverage map — all met; confirm nothing from the spec's Out of Scope list was resurrected (no distro, no `UseAzureMonitorExporter`, no Live Metrics, no classic SDK, no pre-host App Insights export, no extra config beyond `Cloudstrap:AzureMonitor`).
- [x] Docs review: `src/Test/WasmTestProject/README.md` demo table row accurate; both package READMEs consistent with the as-built behavior.
- [x] User approved — deliverable #3 done; project-manager flips the ROADMAP row to ✅. *(Approved 2026-08-02.)*
