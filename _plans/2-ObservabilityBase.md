# Plan: 2-ObservabilityBase — A consumer turns on one call and gets Serilog logging, a vendor-neutral OTel pipeline, correlation, and health-check plumbing driven by the `Cloudstrap:` settings

## Overview

Deliverable #2 of the extraction roadmap: the `Cloudstrap.Observability` package. **Binding spec: `_specs/2-ObservabilityBase.md`** (approved 2026-07-27, zero Open Questions; its Port Decision Table, Public API Sketch, Behaviors & Conventions table, and Out of Scope list are authoritative — nothing marked Drop/Replace/Move-out may appear here). Reference patterns, both read in full before planning:

- **Repo pattern (deliverable 1, verified on disk)**: `src/Cloudstrap.Core/` + `src/Test/UnitTest/Cloudstrap.Core.Tests/` — csproj shape (Sdk + `TargetFramework` + the two packaging properties + metadata block; everything else inherited from the frozen `src/Directory.Build.props`), single-namespace sealed types with full XML docs, `internal` validators/implementations, NUnit 4 on MTP test project (Sdk + `TargetFramework` + `ProjectReference` + version-less `PackageReference`s; the rest from `src/Test/Directory.Build.props`), `PackageSurfaceTests` guard-test idiom, neutral fixtures (`Contoso`/`Orders`/`Api`, `example.com`).
- **Source feature**: already digested by the spec — the Port Decision Table verdicts are final and this plan does **not** re-read the source repo. Every ported behavior below cites its spec row.

This is a library deliverable with no controllers and no database: the template's endpoint-integration block does not apply. The integration layer here is host-level — `HostApplicationBuilder`/`ServiceCollection` + `DefaultHttpContext`-style tests that exercise the real DI container, the real options pipeline, the real Serilog/OTel providers (in-memory exporter + `ActivityListener`, no collector, no network, no Azure), exactly as the spec's test strategy prescribes.

**Prerequisites (verified 2026-07-29):** deliverables 0 and 1 are ✅ with all gates closed; `src/Cloudstrap.sln`, the frozen `src/Directory.Build.props`, CPM (`src/Directory.Packages.props`, currently `Microsoft.Extensions.*` 10.0.10 + the NUnit trio), and `src/Test/Directory.Build.props` all exist and are green. The publish path is open (prefix reserved, Trusted Publishing active). Known drift carried from plan 1: `.claude/instructions/tests.md` still says MSTest/`dotnet test` — this plan follows the repo's actual NUnit-4-on-MTP convention and the CLAUDE.md rule that `dotnet test` is unsupported (run the test exe directly).

**AC coverage map** (from `_specs/2-ObservabilityBase.md` + the AC-C6 amendment in `_specs/1-CoreSettingsModel.md`):
AC-C6(b)/(a) + AC-B13 (validation half) → Step 1 · AC-B8 (bootstrap file half) → Step 2 · AC-B3 + AC-B8 (host half) → Step 3 · AC-B1 (no-pipeline half) + AC-B2 + AC-B9 (owner half) → Step 4 · AC-O3 + AC-B10 → Step 5 · AC-O2 (export half) + AC-B7 + AC-B13 (exporter half) → Step 6 · AC-ASP1 + AC-B9 (contribute half) → Step 7 · AC-B4 (inbound) + AC-B5 + AC-B6 → Steps 8–9 · AC-B4 (outbound) → Step 10 · AC-B1 (full resolution test) → Step 11 · AC-B11 + AC-O4 + AC-O2 (closure guard) + AC-ASP2 → Step 12 · AC-B12 → Step 12 + every step's VERIFY.

**New CPM entries** (all Apache-2.0 except the Microsoft ones (MIT); versions verified stable on nuget.org 2026-07-29; the executor pins exactly these in `src/Directory.Packages.props` in the step that first needs them):

| Package | Version | Step |
|---|---|---|
| `Serilog` | 4.4.0 | 2 |
| `Serilog.Extensions.Hosting` | 10.0.0 | 2 |
| `Serilog.Sinks.Console` | 6.1.1 | 2 |
| `Serilog.Sinks.File` | 7.0.0 | 2 |
| `Microsoft.Extensions.Hosting.Abstractions` (production) / `Microsoft.Extensions.Hosting` (test-only) | 10.0.10 | 3 |
| `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Instrumentation.Http`, `OpenTelemetry.Instrumentation.Runtime`, `OpenTelemetry.Instrumentation.SqlClient`, `OpenTelemetry.Exporter.Console` | 1.17.0 | 4 |
| `OpenTelemetry.Exporter.InMemory` (test-only) | 1.17.0 | 4 |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | 1.17.0 | 6 |

⚠️ **Risk areas (spec header + roadmap §2, reviewed at the covering gates):** public API surface consumed by nearly every later package (`UseCloudstrapObservability`, `IBusinessTrace`, the correlation abstractions and the delegating-handler seam deliverable 4 consumes) · the repo's **first non-Microsoft dependencies** (Serilog + OpenTelemetry families) · the **first `Microsoft.AspNetCore.App` framework reference** in the suite (gate decision OQ-1: one package carries it — added in Step 4, README consequence in Step 12: consumers need the ASP.NET Core shared framework at run time, `mcr.microsoft.com/dotnet/runtime`-only base images are not supported) · host startup ordering (providers added, never cleared; the AC-B7 fail-fast guard) · Step 1 changes the **shipped** `Cloudstrap.Core` validation behavior (spec-amended, but it alters an existing package — Gate 1 reviews it).

**Planner mechanics decided here (flagged for gate review, no spec conflict):** (a) the AC-C6 relaxation injects `IConfiguration` into `OpenTelemetryOptionsValidator`/`CloudstrapOptionsValidator` via constructors — the DI path resolves it from the container (already required by `BindConfiguration`), the eager path passes its own argument; single rule, both entry points, per the spec-1 validation-mechanics note. (b) `LogLevel.None` (the seventh MEL level, outside the spec's six-case map) is treated as "write nothing" — documented in XML docs and proven by test. (c) The file sink writes `log-.log` with daily rolling inside the configured folder — the spec fixes the folder semantics (verbatim `Path`, AC-B8) but not the file name. (d) `AddCloudstrapCorrelation()` also calls the framework's additive `services.AddProblemDetails()` so AC-B6's `application/problem+json` response works without a hand-assembled host. (e) AC-B2's "no OTLP exporter registered" is observed through the public `ConfigureOtlpExporter` hook (never invoked outside `Otlp` mode).

This package introduces **no new configuration section** — everything it reads is Core's (`Cloudstrap:Logging`, `Cloudstrap:OpenTelemetry`, `Cloudstrap:Correlation`, `Cloudstrap:HealthChecks`, `Cloudstrap:Application`); everything it adds is a code-level option on `CloudstrapObservabilityOptions`.

---

## Slice 1 — Pre-host: `Otlp` mode composes with the platform's standard variable, and code can log before the host exists

---

## Step 1 — `OTEL_EXPORTER_OTLP_ENDPOINT` satisfies the Otlp endpoint rule on both validation paths (Core amendment, AC-C6 as amended 2026-07-27)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope** *(Cloudstrap.Core only — the spec-1 Decision Log schedules this amendment inside this plan; deliverable 1 stays closed)*:
- `src/Cloudstrap.Core/OpenTelemetryOptionsValidator.cs` *(modify)*
- `src/Cloudstrap.Core/CloudstrapOptionsValidator.cs` *(modify)*
- `src/Cloudstrap.Core/ConfigurationExtensions.cs` *(modify)*
- `src/Cloudstrap.Core/OpenTelemetryOptions.cs` *(modify — XML docs on `Endpoint` only: "required when `Mode` is `Otlp` unless `OTEL_EXPORTER_OTLP_ENDPOINT` is present in configuration")*
- `src/Test/UnitTest/Cloudstrap.Core.Tests/ConfigurationExtensionsTests.cs` *(modify)*
- `src/Test/UnitTest/Cloudstrap.Core.Tests/ServiceCollectionExtensionsTests.cs` *(modify)*

**RED** *(write these tests first, run them, confirm they fail — the current validator ignores the variable, so the pass-cases fail and the message-cases fail on wording)*:
- Unit test file: `src/Test/UnitTest/Cloudstrap.Core.Tests/ConfigurationExtensionsTests.cs` — the variable is supplied as the in-memory configuration key `OTEL_EXPORTER_OTLP_ENDPOINT` (environment variables surface through configuration in real hosts; no process-env mutation in tests).
  - `GetCloudstrapOptions_WithOtlpModeNoEndpointAndStandardVariable_ReturnsOptions` — `Mode=Otlp`, no `Endpoint`, `OTEL_EXPORTER_OTLP_ENDPOINT = https://collector.example.com` → returns options, `Endpoint` stays null (AC-C6 case b).
  - `GetCloudstrapOptions_WithOtlpModeAndNeitherEndpointSource_ThrowsNamingEndpointAndVariable` — failure message names **both** `Endpoint` and `OTEL_EXPORTER_OTLP_ENDPOINT` (AC-C6 case a; extend the assertion of the existing `GetCloudstrapOptions_WithOtlpModeAndNoEndpoint_ThrowsNamingEndpoint` or supersede it).
  - `GetCloudstrapOptions_WithNonHttpEndpointAndStandardVariable_StillThrows` — an explicit `ftp://collector.example.com` `Endpoint` is validated even when the variable is present (explicit configuration wins; AC-C6 case c unchanged).
- Unit test file: `src/Test/UnitTest/Cloudstrap.Core.Tests/ServiceCollectionExtensionsTests.cs`
  - `AddCloudstrapCore_WithOtlpModeAndStandardVariable_StartupValidationSucceeds` — same rule on the DI path: `IStartupValidator.Validate()` does not throw (single validator implementation, two entry points).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Core.Tests\bin\Debug\net10.0\Cloudstrap.Core.Tests.exe --filter "ConfigurationExtensionsTests"
  ```

**GREEN**:
- `OpenTelemetryOptionsValidator` — add `internal OpenTelemetryOptionsValidator(IConfiguration configuration)`; in `Validate`, when `Mode == Otlp` and `Endpoint is null`: pass when `!string.IsNullOrWhiteSpace(_configuration["OTEL_EXPORTER_OTLP_ENDPOINT"])`, otherwise fail with a message naming `Endpoint` **and** the `OTEL_EXPORTER_OTLP_ENDPOINT` alternative. A non-null `Endpoint` keeps the existing absolute-http(s) rule regardless of the variable.
- `CloudstrapOptionsValidator` — the three `static readonly` per-section validator fields become instance fields; new `internal CloudstrapOptionsValidator(IConfiguration configuration)` forwards to the OTel validator. The DI registrations in `ServiceCollectionExtensions.AddCloudstrapCore` stay type-based and unchanged — the container ctor-injects `IConfiguration` (already required there by `BindConfiguration`).
- `ConfigurationExtensions.GetCloudstrapOptions` — drop the `static readonly _validator` field; construct `new CloudstrapOptionsValidator(configuration)` per call.
- XML docs updated on the validator classes and `OpenTelemetryOptions.Endpoint`.

**DB changes**: none — this repository has no database.

**VERIFY** *(when all green, mark this step's `Done` checkbox and continue straight to the next step)*:
1. `src\Test\UnitTest\Cloudstrap.Core.Tests\bin\Debug\net10.0\Cloudstrap.Core.Tests.exe` → all tests pass (49 existing + new): `Mode=Otlp` now starts on the ecosystem's standard variable and still fails fast with neither source — behavior the shipped Core did not have (AC-C6 as amended; the validation half of AC-B13).
2. `dotnet build src/Cloudstrap.sln` → zero warnings/errors; `dotnet format src/Cloudstrap.sln --verify-no-changes` → exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 2 — Pre-host code logs to console (and file) through `CloudstrapBootstrapLogger` (package + test project bootstrap)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Observability/Cloudstrap.Observability.csproj` *(create)* — Sdk project, `TargetFramework=net10.0`, `GeneratePackageOnBuild=true`, `GenerateDocumentationFile=true`; `<ProjectReference>` to `..\Cloudstrap.Core\Cloudstrap.Core.csproj`; `<PackageReference>`s `Serilog`, `Serilog.Extensions.Hosting`, `Serilog.Sinks.Console`, `Serilog.Sinks.File`. **No framework reference yet** (added in Step 4, where ASP.NET Core types are first needed). Description/tags/README metadata land in Step 12.
- `src/Cloudstrap.Observability/CloudstrapBootstrapLogger.cs` *(create)* — public static, namespace `Cloudstrap.Observability`.
- `src/Cloudstrap.Observability/SerilogPipeline.cs` *(create)* — `internal static class SerilogPipeline` with `Configure(LoggerConfiguration, CloudstrapOptions)`: the one place the Serilog console/file shape lives, reused by Step 3's host path.
- `src/Cloudstrap.Observability/LogLevelMapping.cs` *(create)* — `internal static` six-case `LogLevel` → `LogEventLevel` map (spec row `MapToSerilogLogLevel` — Redesign: Trace→Verbose, Debug→Debug, Information→Information, Warning→Warning, Error→Error, Critical→Fatal; `LogLevel.None` → write nothing, planner mechanic (b)).
- `src/Test/UnitTest/Cloudstrap.Observability.Tests/Cloudstrap.Observability.Tests.csproj` *(create)* — mirror of `Cloudstrap.Core.Tests.csproj`: Sdk + `TargetFramework=net10.0` + `<ProjectReference Include="..\..\..\Cloudstrap.Observability\Cloudstrap.Observability.csproj" />` + version-less `<PackageReference>`s `Microsoft.Extensions.Configuration`, `Microsoft.Extensions.Configuration.Binder` (in-memory fixture configs).
- `src/Test/UnitTest/Cloudstrap.Observability.Tests/CloudstrapBootstrapLoggerTests.cs` *(create)*
- `src/Cloudstrap.sln` *(modify)* — `Cloudstrap.Observability` at the solution root, `Cloudstrap.Observability.Tests` under the `Test\UnitTest` solution folders (same nesting as `Cloudstrap.Core.Tests`).
- `src/Directory.Packages.props` *(modify)* — pin `Serilog` 4.4.0, `Serilog.Extensions.Hosting` 10.0.0, `Serilog.Sinks.Console` 6.1.1, `Serilog.Sinks.File` 7.0.0.

**RED** *(write these tests first; for a brand-new project the failure is the test project failing to compile against missing types — the standard RED for new code)*:
- Unit test file: `CloudstrapBootstrapLoggerTests.cs` — fixtures bind a `CloudstrapOptions` from an in-memory config (`Contoso`/`Orders`/`Api`). Console assertions redirect `Console.SetOut` to a `StringWriter` (restored in `finally`/teardown); file assertions use a unique temp directory (`Path.Combine(Path.GetTempPath(), ...)`, deleted in teardown).
  - `Create_WithDefaults_WritesInformationToConsole` — a logger from the returned `ILoggerFactory` logs Information; after `Dispose()` (flush) the captured console output contains the message and the level.
  - `Create_WithDefaults_SuppressesDebugBelowConfiguredLevel` — a Debug event is absent at the default `Information` level.
  - `Create_WithLevelOverride_AppliesSourceContextOverride` — `LevelOverrides["Contoso.Noisy"] = Error` silences an Information event from that category while another category still writes.
  - `Create_WithFileLoggingEnabled_WritesUnderExactlyTheConfiguredPath` — `File.Enabled=true`, `File.Path=<temp dir>` → after dispose, a log file exists **directly** under the configured directory (no workload subfolder, no machine-name-derived name) and contains the message (AC-B8).
  - `Create_WithConsoleDisabled_WritesNothingToConsole` — `Logging:Console:Enabled=false` → captured output empty.
  - `Create_WithLevelNone_WritesNothing` — planner mechanic (b) proven.
  - `Create_WithNullOptions_ThrowsArgumentNullException` (guard clause).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln   # RED = test project fails to compile against missing types
  src\Test\UnitTest\Cloudstrap.Observability.Tests\bin\Debug\net10.0\Cloudstrap.Observability.Tests.exe --filter "CloudstrapBootstrapLoggerTests"
  ```

**GREEN**:
- `CloudstrapBootstrapLogger` — `public static class`, one member: `public static ILoggerFactory Create(CloudstrapOptions options)` (spec sketch): guard `ArgumentNullException.ThrowIfNull`; build a `LoggerConfiguration`, apply `SerilogPipeline.Configure`, `CreateBootstrapLogger()` (Serilog's two-stage bootstrap — the spec's Replace verdict for `DeferredLoggerFactory`), wrap in `SerilogLoggerFactory(logger, dispose: true)` (from `Serilog.Extensions.Logging`, transitive via `Serilog.Extensions.Hosting` — add a direct pin only if restore demands it). XML docs state: independent of the host pipeline, consumer disposes it after `Build()`, `Log.Logger` is never set (spec Behaviors row "Bootstrap logging").
- `SerilogPipeline.Configure` — minimum level via `LogLevelMapping` from `LoggingOptions.Level`; `MinimumLevel.Override` per `LevelOverrides`; `Enrich.FromLogContext()` + `Enrich.WithProperty` per `EnrichProperties` (spec: machine/thread/user enrichers dropped); console sink when `Console.Enabled` with the single human-readable template — timestamp, level, message, source context, `{TraceId}`/`{SpanId}` (Serilog's built-in `LogEvent` values), exception; file sink when `File.Enabled`: `WriteTo.File(Path.Combine(File.Path!, "log-.log"), rollingInterval: Day, fileSizeLimitBytes: 10 MB, rollOnFileSizeLimit: true, retainedFileCountLimit: 20, shared: true)` (spec Behaviors row "File output"; planner mechanic (c)). Synchronous writes — no `Serilog.Sinks.Async` (spec Drop row). **Not ported**: OTLP bootstrap branch, backup logger, `IsRunningInAks` branches, machine-name file naming, `D:\logsint` (spec Drop rows; AC-B8).

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: code can now log to console and file before any host exists, with the file landing exactly where configured — behavior that did not exist in the repo (AC-B8 bootstrap half).
2. `dotnet build src/Cloudstrap.sln` → zero warnings (CS1591 enforces XML docs on the new public API); `dotnet format src/Cloudstrap.sln --verify-no-changes` → exit 0.
3. `dotnet build src/Cloudstrap.sln -c Release` → a `Cloudstrap.Observability.*.nupkg` appears under `src/Cloudstrap.Observability/bin/Release/` (packable from day one; metadata completed in Step 12).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 1: Core speaks the platform convention; the package exists *(covers Steps 1–2)*

*Executor: STOP here. Present the results of all covered steps and WAIT for user approval — do not start the next step.*

⚠️ **Risk areas at this gate**: Step 1 changes the **shipped** `Cloudstrap.Core` validation behavior (spec-amended 2026-07-27, but it is a behavior change in a published-surface package — review the failure-message wording and the "explicit setting still validated" semantics). Step 2 introduces the repo's **first non-Microsoft dependencies**.

- [x] Behavioral verification: Core test exe shows the three AC-C6 amendment cases (variable satisfies the rule / neither source still fails naming both / explicit endpoint still validated) green on both entry paths; Observability test exe shows bootstrap console + file logging with the file under exactly the configured path (AC-B8) and `None`/disabled-console writing nothing.
- [x] Code review: validator ctor-injection mechanics (planner mechanic (a) — `IConfiguration` in the validator, DI unchanged, eager path per-call); `CloudstrapBootstrapLogger`/`SerilogPipeline` vs the spec's Drop rows (no OTLP branch, no async wrapper, no enrichers, no `Log.Logger` mutation); `LogLevel.None` handling (planner mechanic (b)); the `log-.log` file-name choice (planner mechanic (c)).
- [x] ⚠️ Dependency review (risk area): first third-party pins — `Serilog` 4.4.0, `Serilog.Extensions.Hosting` 10.0.0, `Serilog.Sinks.Console` 6.1.1, `Serilog.Sinks.File` 7.0.0 (all Apache-2.0, CPM-pinned).
- [x] User approved — implementation may continue past this gate *(approved 2026-07-29; executor decisions confirmed: public ctors on internal validators, CA1848 test-layer NoWarn, InvariantCulture sinks, `None` override = full source exclusion)*

---

## Slice 2 — The host logs through the single entry point

---

## Step 3 — `UseCloudstrapObservability()` adds Serilog console/file alongside the consumer's providers and applies the configured levels (never `ClearProviders`)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Observability/HostApplicationBuilderExtensions.cs` *(create)* — the one public entry point (spec's Redesign of the source `AddOpenTelemetry`/`AddSerilogNihdi`/`ConfigureForNihdiOpenTelemetry` split).
- `src/Cloudstrap.Observability/CloudstrapObservabilityOptions.cs` *(create)* — starts with `ConfigureSerilog : Action<LoggerConfiguration>?`; grows per step (pipeline knobs in Steps 4–7).
- `src/Cloudstrap.Observability/CloudstrapObservabilityBuilder.cs` *(create)* — `public sealed`, internal ctor; `Services : IServiceCollection`, `Telemetry : OpenTelemetryOptions` (the values used for registration decisions); `MarkExporterContributed()` arrives in Step 6.
- `src/Cloudstrap.Observability/Cloudstrap.Observability.csproj` *(modify)* — add `Microsoft.Extensions.Hosting.Abstractions` (`IHostApplicationBuilder`).
- `src/Directory.Packages.props` *(modify)* — add `Microsoft.Extensions.Hosting.Abstractions` 10.0.10 + test-only `Microsoft.Extensions.Hosting` 10.0.10.
- `src/Test/UnitTest/Cloudstrap.Observability.Tests/Cloudstrap.Observability.Tests.csproj` *(modify)* — add `Microsoft.Extensions.Hosting` (`Host.CreateApplicationBuilder`).
- `src/Test/UnitTest/Cloudstrap.Observability.Tests/HostApplicationBuilderExtensionsTests.cs` *(create)* — the logging-behavior fixture (pipeline fixtures come in later steps).

**RED** *(write these tests first, run them, confirm they fail)*:
- Unit test file: `HostApplicationBuilderExtensionsTests.cs` — helper builds `Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true })` (or equivalent) with the minimal-valid in-memory `Cloudstrap` section, calls `UseCloudstrapObservability()`, builds the host.
  - `UseCloudstrapObservability_WithProviderRegisteredBefore_KeepsThatProvider` — a fake `ILoggerProvider` added via `builder.Logging` before the call is still among `host.Services.GetServices<ILoggerProvider>()` after build, **and** a Serilog provider is present alongside it (AC-B3 — no `ClearProviders`, providers added).
  - `UseCloudstrapObservability_WithDefaultLevel_DisablesDebugForApplicationCategories` — `ILogger` for `Contoso.Orders.Service`: `IsEnabled(LogLevel.Information)` true, `IsEnabled(LogLevel.Debug)` false (Core default `Information` applied to MEL).
  - `UseCloudstrapObservability_SeedsFrameworkCategoriesAtWarning` — logger for `Microsoft.AspNetCore.Routing.Matching`: `IsEnabled(LogLevel.Information)` false (seeds: `Microsoft.AspNetCore`, `Microsoft.AspNetCore.Hosting.Diagnostics`, `System.Net.Http.HttpClient`, `Microsoft.Hosting.Lifetime` — spec row `ApplyNihdiLogLevels` Redesign).
  - `UseCloudstrapObservability_WithLevelOverrideOnSeededCategory_ConsumerOverrideWins` — `LevelOverrides["Microsoft.AspNetCore"] = Debug` → `IsEnabled(LogLevel.Debug)` true for that category (user overrides applied after seeds).
  - `UseCloudstrapObservability_WithFileLoggingEnabled_WritesUnderExactlyTheConfiguredPath` — host path honors AC-B8 like the bootstrap path (temp dir, log, dispose host, assert file).
  - `UseCloudstrapObservability_WithInvalidCloudstrapSection_ThrowsConfigurationValidationException` — missing `SystemName` → the exception surfaces **at the `UseCloudstrapObservability` call** (spec: eager `GetCloudstrapOptions()` inside the entry point).
  - `UseCloudstrapObservability_WithConfigureSerilog_HasFinalSay` — `ConfigureSerilog` adds a distinguishable sink/property observable in the output (e.g. a `StringWriter`-backed `TextWriter` sink capturing an event).
  - `UseCloudstrapObservability_CalledOnNullBuilder_ThrowsArgumentNullException` (guard clause).
  - `UseCloudstrapObservability_ReturnsBuilderExposingServicesAndTelemetry` — returned `CloudstrapObservabilityBuilder.Services` is the host's service collection and `.Telemetry` carries the bound `OpenTelemetryOptions` values.
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Observability.Tests\bin\Debug\net10.0\Cloudstrap.Observability.Tests.exe --filter "HostApplicationBuilderExtensionsTests"
  ```

**GREEN**:
- `HostApplicationBuilderExtensions` — `public static class` with `public static CloudstrapObservabilityBuilder UseCloudstrapObservability(this IHostApplicationBuilder builder, Action<CloudstrapObservabilityOptions>? configure = null)`:
  1. Guard; instantiate `CloudstrapObservabilityOptions`, invoke `configure`.
  2. `CloudstrapOptions options = builder.Configuration.GetCloudstrapOptions();` — misconfiguration fails here with `ConfigurationValidationException` (spec §"How the two Core entry points are used").
  3. `builder.Services.AddCloudstrapCore();` (idempotent) — run-time `IOptions<T>` for middleware/handlers in later steps.
  4. Serilog: `builder.Services.AddSerilog(loggerConfiguration => { SerilogPipeline.Configure(loggerConfiguration, options); observabilityOptions.ConfigureSerilog?.Invoke(loggerConfiguration); })` — an **added** provider, never clearing (AC-B3); `ConfigureSerilog` runs last (final say).
  5. MEL levels: `builder.Logging.SetMinimumLevel(options.Logging.Level)`; seed the four framework categories at `Warning` via `AddFilter`; then apply `LevelOverrides` via `AddFilter` so consumer entries win.
  6. Return `new CloudstrapObservabilityBuilder(builder.Services, options.OpenTelemetry)`. (OTel pipeline branches arrive in Steps 4–7; correlation/business-trace registrations are wired in Steps 8/11.)
- XML docs per public-api.md on all three new public types (entry point remarks document: added-not-replaced providers, the exception contract, and that pipeline behavior follows in this same package).

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: a host now gets Cloudstrap logging from one call, keeps its own providers, and fails at that exact line on bad config — new observable behavior (AC-B3; AC-B8 host half).
2. `dotnet build src/Cloudstrap.sln` → zero warnings; `dotnet format src/Cloudstrap.sln --verify-no-changes` → exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 2: the entry-point shape is frozen *(covers Step 3)*

*Executor: STOP here. Present the results and WAIT for user approval — do not start the next step.*

⚠️ **Risk area — public API surface.** `UseCloudstrapObservability(...) : CloudstrapObservabilityBuilder` is the flagship signature every later hosting package (4/5/6/7/12/14) builds on, and `CloudstrapObservabilityOptions` grows through the remaining slices — review the shape now, before the pipeline is built on it.

- [x] Behavioral verification: test exe output shows AC-B3 (pre-registered provider survives, Serilog added alongside), the level-seed/override precedence tests, the host-path AC-B8 file test, and the `ConfigurationValidationException`-at-the-call test all green.
- [x] Code review: entry-point signature and `CloudstrapObservabilityBuilder`/`CloudstrapObservabilityOptions` vs the spec's Public API Sketch (names, nullability, `Action<>` members); ordering inside the entry point (eager validate → AddCloudstrapCore → Serilog → levels); XML-doc completeness; no `ClearProviders` anywhere.
- [x] User approved — implementation may continue past this gate *(approved 2026-07-29; executor decision confirmed: Serilog registered as an added `SerilogLoggerProvider`, not via `AddSerilog`'s factory replacement — AC-B3 letter over sketch letter)*

---

## Slice 3 — Owner mode: an active telemetry mode stands up the vendor-neutral pipeline

---

## Step 4 — An active mode registers traces/metrics/logs with Cloudstrap resource identity; `Disabled` stays inert (AC-B1 pipeline half, AC-B2, AC-B9)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Observability/OpenTelemetryPipeline.cs` *(create)* — `internal static` owner-mode composition (spec Redesign of `ConfigureTracing`/`ConfigureMetrics`).
- `src/Cloudstrap.Observability/CloudstrapResourceAttributes.cs` *(create)* — `internal static` attribute keys + apply helper (spec Redesign of `NihdiResourceAttributes`).
- `src/Cloudstrap.Observability/CloudstrapObservabilityOptions.cs` *(modify)* — add `ConfigureResource : Action<ResourceBuilder>?`, `ConfigureTracing : Action<TracerProviderBuilder>?`, `ConfigureMetrics : Action<MeterProviderBuilder>?`, `ConfigureLogging : Action<OpenTelemetryLoggerOptions>?`.
- `src/Cloudstrap.Observability/HostApplicationBuilderExtensions.cs` *(modify)* — when `options.OpenTelemetry.IsActive`, call the pipeline composition.
- `src/Cloudstrap.Observability/Cloudstrap.Observability.csproj` *(modify)* — add `<FrameworkReference Include="Microsoft.AspNetCore.App" />` ⚠️ (first in the suite — gate decision OQ-1; needed by `OpenTelemetry.Instrumentation.AspNetCore` and Steps 5/9 `HttpContext`/endpoint types; flows transitively to the test project) + `PackageReference`s: `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Instrumentation.Http`, `OpenTelemetry.Instrumentation.Runtime`, `OpenTelemetry.Instrumentation.SqlClient`, `OpenTelemetry.Exporter.Console`.
- `src/Directory.Packages.props` *(modify)* — pin the six packages above at 1.17.0 + test-only `OpenTelemetry.Exporter.InMemory` 1.17.0.
- `src/Test/UnitTest/Cloudstrap.Observability.Tests/Cloudstrap.Observability.Tests.csproj` *(modify)* — add `OpenTelemetry.Exporter.InMemory`.
- `src/Test/UnitTest/Cloudstrap.Observability.Tests/ObservabilityPipelineOwnerTests.cs` *(create)*

**RED** *(write these tests first, run them, confirm they fail — telemetry observed through the public hooks: `ConfigureTracing(b => b.AddInMemoryExporter(exportedActivities).AddSource("Contoso.Test"))` etc.; test spans created with a `Contoso.Test` `ActivitySource`, provider force-flushed)*:
- Unit test file: `ObservabilityPipelineOwnerTests.cs` — `Mode=Console` is the network-free active mode for these fixtures.
  - `UseCloudstrapObservability_WithModeDisabled_RegistersNoTracerOrMeterProvider` — `host.Services.GetService<TracerProvider>()` and `GetService<MeterProvider>()` are null; the host starts; Serilog still logs (AC-B1 pipeline half).
  - `UseCloudstrapObservability_WithModeConsole_RegistersTracerAndMeterProviders` — both resolve non-null (AC-B2 registration half).
  - `UseCloudstrapObservability_WithModeConsole_WritesSpanToConsoleExporter` — `Console.SetOut` capture contains the flushed test activity's name (AC-B2 export half; `IsConsoleEnabled` drives the console exporter, so `Mode=Otlp`+`EnableConsole` reuses this path later).
  - `UseCloudstrapObservability_OwnerMode_SetsCloudstrapResourceAttributes` — an exported span's resource carries `service.name == "contoso-orders-api"` (WorkloadName), `deployment.environment.name == <IHostEnvironment.EnvironmentName>`, `host.name`, `cloudstrap.system.name == "Contoso"`, `cloudstrap.subsystem.name == "Orders"`, `cloudstrap.subsystem.type == "Api"`, and **no** attribute key starting `nihdi.` (AC-B9 owner half).
  - `UseCloudstrapObservability_WithEnvironmentTier_AddsTierAttribute` / `..._WithoutEnvironmentTier_OmitsTierAttribute` — `cloudstrap.environment.tier` only when set.
  - `UseCloudstrapObservability_WithConfigureResource_AppliesConsumerOverride` — a consumer-set attribute lands on the exported resource.
  - `UseCloudstrapObservability_WithEnableTracingFalse_RegistersNoTracerProviderButKeepsMetrics` — the `EnableTracing`/`EnableMetrics`/`EnableLogs` gates.
  - `UseCloudstrapObservability_WithEnableLogs_ExportsLogRecordsThroughOtelProvider` — `ConfigureLogging(o => o.AddInMemoryExporter(records))`; a logged event yields a record (log pipeline present, added alongside Serilog).
  - `UseCloudstrapObservability_WithSqlClientInstrumentationDisabled_DoesNotThrow` — default-off gate smoke (flag on/off both build and start).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Observability.Tests\bin\Debug\net10.0\Cloudstrap.Observability.Tests.exe --filter "ObservabilityPipelineOwnerTests"
  ```

**GREEN**:
- `OpenTelemetryPipeline` (owner mode, called only when `IsActive`): `services.AddOpenTelemetry()`; `.ConfigureResource(...)` applying `CloudstrapResourceAttributes` (service name from `ApplicationOptions.WorkloadName` via `AddService`; `deployment.environment.name` from `IHostEnvironment.EnvironmentName`; `host.name` from `Environment.MachineName`; `cloudstrap.system.name`/`cloudstrap.subsystem.name`/`cloudstrap.subsystem.type`; `cloudstrap.environment.tier` only when `EnvironmentTier` is set — spec's rename table, no `nihdi.*`, no `businessystem` typo, no workload/aspnet duplicates) then the consumer's `ConfigureResource`;
  - `WithTracing` when `EnableTracing`: `AddAspNetCoreInstrumentation`, `AddHttpClientInstrumentation` (`RecordException = true`), `AddSqlClientInstrumentation` when `EnableSqlClientInstrumentation`; console exporter when `IsConsoleEnabled`; consumer `ConfigureTracing` last. **No** `AddSource("NServiceBus.*")` (spec: deliverable 14 contributes via the hooks).
  - `WithMetrics` when `EnableMetrics`: runtime/http-client/aspnetcore meters gated by `EnableRuntimeMetrics`/`EnableHttpClientMetrics`/`EnableAspNetCoreMetrics`; console exporter when `IsConsoleEnabled`; consumer `ConfigureMetrics` last. **No** `AddMeter("NServiceBus.*")`.
  - `WithLogging` when `EnableLogs`: console exporter when `IsConsoleEnabled`; consumer `ConfigureLogging` last.
- Enrichment kept minimal per spec: the `DisplayName` rewrite (`{method} {path}`) + `endpoint.name` tag land with the other span-shaping in Step 5; the redundant `http.*`/hand-rolled `exception.*` tags are **not** written (spec Redesign of `EnrichHttpRequest`).

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: an active mode now produces spans, metrics providers, log records and the `cloudstrap.*` resource identity, while `Disabled` registers nothing — the package's central new behavior (AC-B1 half, AC-B2, AC-B9 owner half).
2. `dotnet build src/Cloudstrap.sln` → zero warnings; `dotnet format src/Cloudstrap.sln --verify-no-changes` → exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 5 — Probe and static-asset noise disappears from traces; Blazor hub chatter is sampled out (AC-O3, AC-B10)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Observability/TraceNoiseFilter.cs` *(create)* — `internal static` predicates over `HttpContext` (inbound) and `HttpRequestMessage` (outbound) (spec Redesign of `ShouldTracePath`/`ShouldTraceHttpClientRequest`/`IsStaticAssetPath`).
- `src/Cloudstrap.Observability/BlazorHubSampler.cs` *(create)* — `internal sealed`, straight port (spec Port row: drop `ComponentHub` spans via the `rpc.service` tag at sampling time — the only moment SignalR exposes it).
- `src/Cloudstrap.Observability/CloudstrapObservabilityOptions.cs` *(modify)* — add `EnableDefaultTraceNoiseFilter : bool = true`, `IgnoredPathSegments : IList<string>` (get-only, initialized empty, appended to the defaults), `ApplySampler : bool = true`.
- `src/Cloudstrap.Observability/OpenTelemetryPipeline.cs` *(modify)* — filter application as **options post-configuration** on `AspNetCoreTraceInstrumentationOptions`/`HttpClientTraceInstrumentationOptions` (wrapping any pre-set `Filter` so both must pass — AC-ASP1 composition, reused verbatim by Step 7); sampler chain: inner = `AlwaysOnSampler` when `AlwaysOnSampler` else `ParentBased(AlwaysOn)`, wrapped in `BlazorHubSampler` unless `EnableBlazorHubTracing`; skipped entirely when `ApplySampler == false`; enrichment: `DisplayName` rewrite + `endpoint.name` tag on the ASP.NET Core instrumentation.
- `src/Test/UnitTest/Cloudstrap.Observability.Tests/TraceNoiseFilterTests.cs` *(create)* — exercised **through the public surface**: resolve `IOptions<AspNetCoreTraceInstrumentationOptions>` / `IOptions<HttpClientTraceInstrumentationOptions>` from a host built with `UseCloudstrapObservability` and invoke the composed `Filter` delegates (internals stay untested directly, no `InternalsVisibleTo` — plan-1 convention).
- `src/Test/UnitTest/Cloudstrap.Observability.Tests/BlazorHubSamplingTests.cs` *(create)* — behavioral, via `ActivitySource.StartActivity(name, ActivityKind.Server, parentContext, tags: ...)` with an in-memory exporter.

**RED** *(write these tests first, run them, confirm they fail)*:
- `TraceNoiseFilterTests`:
  - `AspNetCoreFilter_WithConfiguredLivenessPath_DropsRequest` — `Cloudstrap:HealthChecks:LivenessPath=/alive` + `DefaultHttpContext` with `Path=/alive` → filter returns false (probe paths come from `HealthChecksOptions`, not hard-coded — AC-O3, De-NIHDI `/probe.aspx` row).
  - `AspNetCoreFilter_WithReadinessPath_DropsRequest` / `AspNetCoreFilter_WithBlazorHubPath_DropsRequest` (`/_blazor`) / `AspNetCoreFilter_WithFrameworkOrContentPath_DropsRequest` (`/_framework/blazor.web.js`, `/_content/lib/site.css`) / `AspNetCoreFilter_WithStaticAssetExtension_DropsRequest` (`/favicon.ico`) — the default noise list.
  - `AspNetCoreFilter_WithBusinessPath_KeepsRequest` — `/orders/42` → true.
  - `AspNetCoreFilter_WithConsumerFilterAlreadySet_ComposesBothMustPass` — a filter registered by the host **before** `UseCloudstrapObservability` still vetoes a business path, and Cloudstrap still vetoes `/healthz` (composition, not overwrite).
  - `AspNetCoreFilter_WithIgnoredPathSegments_DropsConfiguredSegment` — `IgnoredPathSegments.Add("/metrics-ui")` → dropped.
  - `AspNetCoreFilter_WithDefaultFilterDisabled_KeepsProbePath` — `EnableDefaultTraceNoiseFilter=false` → `/healthz` passes (the off switch).
  - `HttpClientFilter_WithStaticAssetUri_DropsRequest` / `HttpClientFilter_WithApiUri_KeepsRequest` — outbound half (`https://api.example.com/...`).
- `BlazorHubSamplingTests`:
  - `Tracing_WithComponentHubTagByDefault_ExportsNoSpan` — a server-kind activity created with `rpc.service=ComponentHub` in its sampling-time tags is not exported; a plain activity from the same source **is** (AC-B10 both halves).
  - `Tracing_WithEnableBlazorHubTracing_ExportsHubSpan` — the override.
  - `Tracing_WithAlwaysOnSamplerFlag_RecordsAllSpans` — the dev flag.
  - `Tracing_WithApplySamplerFalse_LeavesHostSamplerAlone` — with `ApplySampler=false`, a `ComponentHub`-tagged span **is** exported (Cloudstrap didn't install its sampler).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Observability.Tests\bin\Debug\net10.0\Cloudstrap.Observability.Tests.exe --filter "TraceNoiseFilterTests"
  ```

**GREEN**:
- `TraceNoiseFilter` — default drop list: `HealthChecksOptions.LivenessPath` + `ReadinessPath`, path segments `/_blazor`, `/_framework/`, `/_content/`, a static-asset extension set (`.css .js .map .ico .png .jpg .svg .gif .woff .woff2`), plus `IgnoredPathSegments`; **no** `/MudBlazor/`, **no** hard-coded `/health` `/live` `/ready` `/probe.aspx` (spec Redesign row). Applied via `services.PostConfigure<AspNetCoreTraceInstrumentationOptions>` / `...HttpClientTraceInstrumentationOptions` capturing the existing `Filter` and AND-composing.
- `BlazorHubSampler` — delegating sampler: `rpc.service == "ComponentHub"` in sampling parameters → `Drop`; else inner sampler's decision. Activated per the sampler chain above.
- Enrichment on ASP.NET Core instrumentation: rewrite `Activity.DisplayName` to `{method} {path}` and set `endpoint.name` when routing metadata is present — nothing else (spec: redundant `http.*`/`exception.*` tags dropped).

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: probe, Blazor-static and hub noise no longer reach exporters while business traffic still does, with every knob (`IgnoredPathSegments`, off switch, `ApplySampler`, `EnableBlazorHubTracing`, `AlwaysOnSampler`, moved probe paths) proven (AC-O3, AC-B10).
2. `dotnet build src/Cloudstrap.sln` → zero warnings; `dotnet format src/Cloudstrap.sln --verify-no-changes` → exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 6 — `Otlp` mode exports over OTLP (explicit endpoint or standard variable); `AzureMonitor` with no exporter fails startup loudly (AC-O2 export half, AC-B13 exporter half, AC-B7)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Observability/OtlpExporterSetup.cs` *(create)* — `internal static`: endpoint/headers resolution + per-signal registration (spec Redesign of `GetOtlpEndpoint`/`GetOtlpHeaders`).
- `src/Cloudstrap.Observability/AzureMonitorContributionGuard.cs` *(create)* — `internal sealed : IHostedService` (AC-B7 mechanism; gate decision OQ-3).
- `src/Cloudstrap.Observability/CloudstrapObservabilityBuilder.cs` *(modify)* — add `public void MarkExporterContributed()` (the deliverable-3 seam), flipping an internal contribution marker registered in DI.
- `src/Cloudstrap.Observability/CloudstrapObservabilityOptions.cs` *(modify)* — add `ConfigureOtlpExporter : Action<OtlpExporterOptions>?`.
- `src/Cloudstrap.Observability/OpenTelemetryPipeline.cs` + `HostApplicationBuilderExtensions.cs` *(modify)* — `Otlp` branch + `AzureMonitor` guard registration (owner mode only).
- `src/Cloudstrap.Observability/Cloudstrap.Observability.csproj` *(modify)* — add `OpenTelemetry.Exporter.OpenTelemetryProtocol`.
- `src/Directory.Packages.props` *(modify)* — pin `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.17.0.
- `src/Test/UnitTest/Cloudstrap.Observability.Tests/ObservabilityOtlpAndAzureMonitorTests.cs` *(create)*

**RED** *(write these tests first, run them, confirm they fail — no collector: OTLP export goes through a background batch processor, so tests assert configuration through the public `ConfigureOtlpExporter` hook and host lifecycle, never a network flush)*:
- Unit test file: `ObservabilityOtlpAndAzureMonitorTests.cs`
  - `OtlpMode_WithExplicitEndpoint_AppendsTraceSignalPathAndSetsHeaders` — `Endpoint=https://collector.example.com`, `Headers:x-api-key=secret`; `ConfigureOtlpExporter` capture observes `Endpoint` ending `/v1/traces` (HttpProtobuf), `Headers` containing `x-api-key=secret`.
  - `OtlpMode_WithExplicitEndpoint_UsesPerSignalPathsForMetricsAndLogs` — captures across signals show `/v1/metrics` and `/v1/logs`.
  - `OtlpMode_WithOnlyStandardVariable_LeavesExporterOptionsUntouched` — `OTEL_EXPORTER_OTLP_ENDPOINT` in configuration, no `Endpoint`: host builds and starts, `TracerProvider` resolves, and the captured `OtlpExporterOptions` still carry the SDK defaults — Cloudstrap set neither endpoint nor headers nor protocol, the SDK owns resolution (AC-B13 exporter half; gate decision OQ-2).
  - `OtlpMode_WithConsumerConfigureOtlpExporter_OverridesCloudstrap` — the hook runs after Cloudstrap's own configuration and wins.
  - `ConsoleMode_DoesNotConfigureOtlpExporter` — the capture is never invoked outside `Otlp` mode (AC-B2's "no OTLP exporter", planner mechanic (e)).
  - `AzureMonitorMode_WithNothingContributed_FailsStartNamingTheMissingPackage` — `Mode=AzureMonitor`, build, `host.StartAsync()` → `InvalidOperationException` whose message contains `Cloudstrap.Observability.AzureMonitor` (AC-B7).
  - `AzureMonitorMode_AfterMarkExporterContributed_StartsCleanly` — same host, but the returned builder's `MarkExporterContributed()` was called → `StartAsync` succeeds (the deliverable-3 seam works).
  - `DisabledMode_DoesNotRegisterTheGuard` — `Mode=Disabled` starts with no guard hosted service interfering.
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Observability.Tests\bin\Debug\net10.0\Cloudstrap.Observability.Tests.exe --filter "ObservabilityOtlpAndAzureMonitorTests"
  ```

**GREEN**:
- `OtlpExporterSetup` — when `Mode == Otlp`: register `AddOtlpExporter` on each enabled signal with a configure callback that (a) when `OpenTelemetryOptions.Endpoint` is set: `Protocol = HttpProtobuf`, `Endpoint = <base> + /v1/{signal}` (append, preserving any base path), `Headers = "k=v,k=v"` formatted from Core's dictionary; (b) when `Endpoint` is null (validation already guaranteed `OTEL_EXPORTER_OTLP_ENDPOINT` is present): set **nothing** — endpoint, headers and protocol are the SDK's to resolve from the standard variables, and no signal path is appended (spec row `GetOtlpEndpoint`; the bespoke `InvalidOperationException` is not ported — Core validates); then invoke the consumer's `ConfigureOtlpExporter` last. Console exporter still added alongside when `IsConsoleEnabled` (Step 4 path).
- `AzureMonitorContributionGuard` — registered (owner mode only) when `Mode == AzureMonitor`: `StartAsync` throws `InvalidOperationException` naming the missing `Cloudstrap.Observability.AzureMonitor` package unless the internal contribution marker was set; `MarkExporterContributed()` sets it (spec sketch; gate decision OQ-3 — telemetry is never silently dropped).
- **No `Azure.*` package anywhere** — the exporter itself is deliverable 3 (AC-O2's dependency half, guarded permanently in Step 12).

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: `Otlp` mode now wires a real OTLP exporter with correct per-signal endpoints/headers, steps aside for the platform variable, and `AzureMonitor` without a contributed exporter refuses to start with an actionable message — none of which existed before (AC-B13, AC-B7).
2. `dotnet build src/Cloudstrap.sln` → zero warnings; `dotnet format src/Cloudstrap.sln --verify-no-changes` → exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 3: owner mode complete *(covers Steps 4–6)*

*Executor: STOP here. Present the results of all covered steps and WAIT for user approval — do not start the next step.*

⚠️ **Risk areas at this gate**: the **first `Microsoft.AspNetCore.App` framework reference** in the suite (Step 4 — gate decision OQ-1; confirm the one-package posture and that `Cloudstrap.Core`'s host-agnostic closure is untouched) · the **OpenTelemetry dependency family** (seven packages, Apache-2.0, all pinned 1.17.0) · the AC-B7 failure mode is a public behavior contract deliverable 3 depends on.

- [x] Behavioral verification: test exe output shows — Disabled inert / Console exporting with the full `cloudstrap.*` + semconv resource identity and zero `nihdi.*` (AC-B9); every noise-filter and sampler knob incl. composition with a pre-set filter (AC-O3, AC-B10); OTLP per-signal paths + headers, the standard-variable hand-off (AC-B13), `ConfigureOtlpExporter` precedence; AzureMonitor fail-fast + `MarkExporterContributed` seam (AC-B7).
- [x] Code review: `OpenTelemetryPipeline` vs the spec's Behaviors table (exporter selection, sampler chain, gates for every `Enable*` flag); no `NServiceBus` sources/meters; enrichment limited to `DisplayName` + `endpoint.name`; filter composition implemented as post-configuration (Step 7 depends on it); `CloudstrapObservabilityOptions`/`CloudstrapObservabilityBuilder` growth vs the Public API Sketch.
- [x] ⚠️ Dependency review (risk area): OTel pins (`Extensions.Hosting`, four instrumentations, two exporters, test-only InMemory — 1.17.0) + the framework reference; `dotnet list src/Cloudstrap.Observability/Cloudstrap.Observability.csproj package` shows zero `Azure.*`, zero `Aspire.*`.
- [x] User approved — implementation may continue past this gate *(approved 2026-07-29; executor notes confirmed: NU1510-driven removal of the redundant `Microsoft.Extensions.Hosting.Abstractions` PackageReference, internal `ExporterContributionMarker` file addition)*

---

## Slice 4 — Contribute mode: Cloudstrap enriches an existing pipeline instead of fighting it

---

## Step 7 — Contribute mode applies Cloudstrap's samplers/filters/enrichment to a ServiceDefaults-style pipeline — no second exporter, no duplicate spans, no `service.name` takeover (AC-ASP1, AC-B9 contribute half)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Observability/ObservabilityPipelineMode.cs` *(create)* — `public enum ObservabilityPipelineMode { Owner = 0, Contribute = 1 }`.
- `src/Cloudstrap.Observability/CloudstrapObservabilityOptions.cs` *(modify)* — add `PipelineMode : ObservabilityPipelineMode = Owner`.
- `src/Cloudstrap.Observability/OpenTelemetryPipeline.cs` + `HostApplicationBuilderExtensions.cs` *(modify)* — contribute branch.
- `src/Test/UnitTest/Cloudstrap.Observability.Tests/ObservabilityContributeModeTests.cs` *(create)*

**RED** *(write these tests first, run them, confirm they fail — the fixture registers a ServiceDefaults-shaped pipeline FIRST: `services.AddOpenTelemetry().ConfigureResource(r => r.AddService("host-owned-name")).WithTracing(b => b.AddSource("Contoso.Test").AddInMemoryExporter(exported))`, then calls `UseCloudstrapObservability(o => o.PipelineMode = Contribute)`)*:
- Unit test file: `ObservabilityContributeModeTests.cs`
  - `ContributeMode_WithHostPipeline_ExportsEachSpanExactlyOnce` — one test activity → exactly one item in the host's in-memory list: no second exporter, no duplicate spans (AC-ASP1's core clause).
  - `ContributeMode_AddsCloudstrapResourceAttributesButLeavesServiceName` — the exported resource carries `cloudstrap.system.name`/`cloudstrap.subsystem.*` **and** `service.name == "host-owned-name"` — not `contoso-orders-api` (AC-B9 contribute half).
  - `ContributeMode_AppliesBlazorHubSampler` — a `ComponentHub`-tagged span is dropped, a plain span exported (Cloudstrap's differentiated sampler applies to the host pipeline).
  - `ContributeMode_WithApplySamplerFalse_LeavesHostSamplerAlone` — the documented opt-out for the `SetSampler`-is-last-wins caveat (spec Behaviors "Sampler" row): hub-tagged span exported.
  - `ContributeMode_ComposesNoiseFilterWithHostInstrumentationOptions` — resolve `IOptions<AspNetCoreTraceInstrumentationOptions>`; the composed `Filter` still drops `/healthz` even though Cloudstrap added no instrumentation (post-configuration composition from Step 5).
  - `ContributeMode_DoesNotConfigureOtlpExporterOrConsoleExporter` — the `ConfigureOtlpExporter` capture never fires and no console-exporter output appears even with `Mode=Otlp` + `EnableConsole=true` in config: exporter selection is entirely the host's in contribute mode.
  - `ContributeMode_DoesNotRegisterAzureMonitorGuard` — `Mode=AzureMonitor` + contribute starts cleanly (the host owns exporters; AC-B7 is an owner-mode contract).
  - `ContributeMode_StillAddsSerilogAndLevels` — the Slice-2 logging behavior is mode-independent (provider list check).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Observability.Tests\bin\Debug\net10.0\Cloudstrap.Observability.Tests.exe --filter "ObservabilityContributeModeTests"
  ```

**GREEN**:
- Contribute branch (when `PipelineMode == Contribute` and `OpenTelemetryOptions.IsActive`): call `services.AddOpenTelemetry()` (idempotent — contributes to the host's builder) and add **only** the differentiated pieces: `ConfigureResource` with the `cloudstrap.*` attributes and **no** `AddService`/`service.name`; `WithTracing` applying the sampler chain (subject to `ApplySampler`) and `AddSource(CloudstrapActivitySources.Business)` once Step 11 lands; the Step-5 noise-filter post-configurations (already registration-shape, they apply regardless of who added instrumentation). **No** instrumentation, **no** exporter, **no** `WithLogging`, **no** OTLP/console exporter setup, **no** AzureMonitor guard (spec Behaviors "OTel pipeline ownership" + "Aspire coexistence" rows).
- XML docs on `PipelineMode`/`ObservabilityPipelineMode` document the Aspire posture and the `ApplySampler` caveat.

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: a host that already owns an OTel pipeline now gets Cloudstrap's samplers, filters and `cloudstrap.*` enrichment with exactly one exporter and one span per request — the Aspire-coexistence behavior (AC-ASP1).
2. `dotnet build src/Cloudstrap.sln` → zero warnings; `dotnet format src/Cloudstrap.sln --verify-no-changes` → exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 4: Aspire coexistence proven *(covers Step 7)*

*Executor: STOP here. Present the results and WAIT for user approval — do not start the next step.*

- [x] Behavioral verification: test exe output shows the exactly-one-exporter/one-span proof, the preserved host `service.name`, sampler + filter application to the foreign pipeline, the `ApplySampler` opt-out, and the no-exporter/no-guard assertions — AC-ASP1 end to end.
- [x] Code review: the contribute branch adds nothing beyond the spec's differentiated-pieces list; `ObservabilityPipelineMode` naming/docs vs the sketch; confirm the founding spec's Aspire posture is described accurately in the XML docs.
- [x] User approved — implementation may continue past this gate *(approved 2026-07-29)*

---

## Slice 5 — Correlation: ids flow in, through, and out of the app

---

## Step 8 — Any code reads and sets the ambient correlation id via DI (`ICorrelationContextAccessor` + `ICorrelationSource`)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope** *(namespace `Cloudstrap.Observability.Correlation` — the coherent group deliverable 4 imports alone)*:
- `src/Cloudstrap.Observability/Correlation/ICorrelationContextAccessor.cs` *(create)* — public interface, `string? CorrelationId { get; set; }` (spec Redesign: the value is the id itself, no disposable object graph, no `ThrowIfUnavailable`).
- `src/Cloudstrap.Observability/Correlation/CorrelationContextAccessor.cs` *(create)* — `internal sealed`; **instance** `AsyncLocal<string?>` field, last-write-wins, never throws (spec Redesign: no `static` field, no already-set guard).
- `src/Cloudstrap.Observability/Correlation/ICorrelationSource.cs` *(create)* — public interface, `string GenerateCorrelation()` (spec Port: the documented id-generation override).
- `src/Cloudstrap.Observability/Correlation/TraceIdCorrelationSource.cs` *(create)* — `internal sealed`: current `Activity.Current.TraceId` else a new GUID (spec Port, renamed from `DefaultCorrelationSource`).
- `src/Cloudstrap.Observability/Correlation/ServiceCollectionExtensions.cs` *(create)* — `public static IServiceCollection AddCloudstrapCorrelation(this IServiceCollection services)`: guard; `TryAddSingleton` accessor + source; also the framework's additive `services.AddProblemDetails()` for Step 9's AC-B6 responses (planner mechanic (d), flagged at the gate).
- `src/Cloudstrap.Observability/HostApplicationBuilderExtensions.cs` *(modify)* — the entry point now calls `AddCloudstrapCorrelation()` (spec sketch: it "registers … correlation services").
- `src/Test/UnitTest/Cloudstrap.Observability.Tests/Correlation/CorrelationServicesTests.cs` *(create)*

**RED** *(write these tests first, run them, confirm they fail)*:
- Unit test file: `Correlation/CorrelationServicesTests.cs`
  - `AddCloudstrapCorrelation_ResolvesAccessorAndSource` — a `ServiceCollection` + the call resolves `ICorrelationContextAccessor` and `ICorrelationSource` as singletons.
  - `UseCloudstrapObservability_RegistersCorrelationServices` — the entry point registers them too (toward AC-B1).
  - `CorrelationId_SetInAsyncFlow_FlowsAcrossAwait` — set, `await Task.Yield()`, read — same value.
  - `CorrelationId_SetInParallelFlows_IsIsolatedPerFlow` — two parallel tasks each set their own id and never observe the other's (`AsyncLocal` isolation; also proves setting a second id no longer throws).
  - `GenerateCorrelation_WithCurrentActivity_ReturnsItsTraceId` — inside a started `Activity`, the source returns the W3C trace id (logs/traces/header agree by construction).
  - `GenerateCorrelation_WithoutActivity_ReturnsParseableGuid`.
  - `AddCloudstrapCorrelation_WithConsumerRegisteredSource_DoesNotOverride` — a consumer `ICorrelationSource` registered first survives (`TryAdd` — "every convention has an override").
  - `AddCloudstrapCorrelation_CalledTwice_RegistersSingleAccessor` (idempotence).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Observability.Tests\bin\Debug\net10.0\Cloudstrap.Observability.Tests.exe --filter "CorrelationServicesTests"
  ```

**GREEN**: the five files above, exactly per the spec's Public API Sketch (public interfaces + attributes only; implementations `internal sealed`); full XML docs including the "readable without an `HttpContext`" rationale on the accessor.

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: any code in the process can now read/set a correlation id that flows through async work and defaults to the current trace id — new observable behavior.
2. `dotnet build src/Cloudstrap.sln` → zero warnings; `dotnet format src/Cloudstrap.sln --verify-no-changes` → exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 9 — Inbound requests establish correlation, and endpoints can require it: header honored, absent id generated, 400 `problem+json` when mandated (AC-B4 inbound, AC-B5, AC-B6)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Observability/Correlation/CloudstrapCorrelationMiddleware.cs` *(create)* — `internal sealed`; the **one** merged establish+validate middleware (spec Redesign fixing source finding 1 — MVC hosts no longer drop the caller's id).
- `src/Cloudstrap.Observability/Correlation/ApplicationBuilderExtensions.cs` *(create)* — `public static IApplicationBuilder UseCloudstrapCorrelation(this IApplicationBuilder app)`.
- `src/Cloudstrap.Observability/Correlation/CorrelationRequiredAttribute.cs` *(create)* — `public sealed`, `[AttributeUsage(Method | Class)]` (spec Port).
- `src/Cloudstrap.Observability/Correlation/AllowNoCorrelationAttribute.cs` *(create)* — `public sealed`, same usage (spec Port).
- `src/Test/UnitTest/Cloudstrap.Observability.Tests/Correlation/CloudstrapCorrelationMiddlewareTests.cs` *(create)*

**RED** *(write these tests first, run them, confirm they fail — fixture: a `ServiceCollection` with `AddCloudstrapCore` + `AddCloudstrapCorrelation` + the in-memory config, an `ApplicationBuilder(provider)` with `.UseCloudstrapCorrelation()` + a terminal delegate capturing `ICorrelationContextAccessor.CorrelationId`; requests are `DefaultHttpContext`s with `RequestServices` set, endpoint metadata attached via `context.SetEndpoint(...)`, response body a `MemoryStream`)*:
- Unit test file: `Correlation/CloudstrapCorrelationMiddlewareTests.cs`
  - `Invoke_WithInboundHeader_UsesInboundValueForTheWholeRequest` — `X-Correlation-ID: abc-123` → the terminal delegate observes `abc-123` (AC-B4 inbound half).
  - `Invoke_WithConfiguredHeaderName_ReadsThatHeader` — `Cloudstrap:Correlation:HeaderName=X-Request-ID` honored (De-NIHDI: constant → configuration).
  - `Invoke_WithoutHeader_GeneratesIdFromCurrentTraceId` — under a started `Activity`, the observed id equals its trace id; no exception, no 400 (AC-B5).
  - `Invoke_WithoutHeaderAndRequireForAllEndpoints_Returns400ProblemJsonNamingHeader` — status 400, `Content-Type` starts `application/problem+json`, body names the configured header (AC-B6).
  - `Invoke_RequiredButPathIsConfiguredHealthEndpoint_PassesWithoutHeader` — `/healthz` (default `HealthEndpoints`) exempt.
  - `Invoke_RequiredButPathIsExcludedEndpoint_PassesWithoutHeader` — an `ExcludeEndpoints` entry exempt (no hard-coded `/swagger`/`/scalar`/`/openapi` — spec: those are configuration values now).
  - `Invoke_RequiredButEndpointHasAllowNoCorrelation_PassesWithoutHeader` — metadata opt-out.
  - `Invoke_RequiredButEndpointHasHealthCheckMetadata_PassesWithoutHeader` — an endpoint carrying `HealthCheckOptions` metadata exempt (replaces the display-name substring match).
  - `Invoke_EndpointMarkedCorrelationRequired_Returns400WithoutHeaderEvenWhenGlobalOff` — per-endpoint opt-in.
  - `Invoke_WithHeaderAndRequirement_PassesAndUsesHeader` — happy path under requirement.
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Observability.Tests\bin\Debug\net10.0\Cloudstrap.Observability.Tests.exe --filter "CloudstrapCorrelationMiddlewareTests"
  ```

**GREEN**:
- `CloudstrapCorrelationMiddleware` — resolves `IOptions<CorrelationOptions>` (+ `ICorrelationContextAccessor`, `ICorrelationSource`) — no settings god-object from `RequestServices` (spec Redesign). Flow: read the configured header; absent → is the endpoint exempt (`HealthEndpoints`/`ExcludeEndpoints` path match, `HealthCheckOptions` endpoint metadata, `[AllowNoCorrelation]`)? if not exempt and required (`Request.RequireForAllEndpoints` or `[CorrelationRequired]` metadata) → 400 via `IProblemDetailsService` with a detail naming the configured header (RFC 9457); otherwise generate via `ICorrelationSource`. Set `accessor.CorrelationId` for the request, invoke next. No response-header echo (spec Out of Scope).
- `ApplicationBuilderExtensions.UseCloudstrapCorrelation` — `app.UseMiddleware<CloudstrapCorrelationMiddleware>()`; XML docs state placement is **not automatic** and must come after routing so endpoint metadata is visible, and that `AddProblemDetails()` (done by `AddCloudstrapCorrelation`) backs the 400 body (spec Behaviors "Correlation middleware placement").
- The two attributes: metadata-only, two lines each, XML-documented.

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: inbound requests now establish a correlation id (inbound value or generated trace id) visible process-wide, and endpoints can mandate it with a compliant `problem+json` 400 and all four exemption kinds — AC-B4 (inbound), AC-B5, AC-B6.
2. `dotnet build src/Cloudstrap.sln` → zero warnings; `dotnet format src/Cloudstrap.sln --verify-no-changes` → exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 10 — Outbound HTTP calls propagate the correlation id (`CorrelationHttpDelegatingHandler` + `AddCloudstrapCorrelationHandler`) (AC-B4 outbound)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Observability/Correlation/CorrelationHttpDelegatingHandler.cs` *(create)* — `public sealed : DelegatingHandler` (the deliverable-4 seam; spec Redesign).
- `src/Cloudstrap.Observability/Correlation/HttpClientBuilderExtensions.cs` *(create)* — `public static IHttpClientBuilder AddCloudstrapCorrelationHandler(this IHttpClientBuilder builder)` (idempotent).
- `src/Test/UnitTest/Cloudstrap.Observability.Tests/Correlation/CorrelationHttpDelegatingHandlerTests.cs` *(create)*

**RED** *(write these tests first, run them, confirm they fail — fixture: `ServiceCollection` + `AddCloudstrapCore` + `AddCloudstrapCorrelation` + `AddHttpClient("catalog").AddCloudstrapCorrelationHandler()` with a stub primary handler capturing the outgoing `HttpRequestMessage`; no network)*:
- Unit test file: `Correlation/CorrelationHttpDelegatingHandlerTests.cs`
  - `SendAsync_WithAmbientCorrelationId_AddsConfiguredHeader` — set `accessor.CorrelationId = "abc-123"`, send via the typed client → captured request carries `X-Correlation-ID: abc-123` (AC-B4 outbound half — same value, same header as Step 9's inbound).
  - `SendAsync_WithConfiguredHeaderName_UsesThatHeader` — reads `IOptions<CorrelationOptions>`, not a constant.
  - `Send_SynchronousPath_AddsHeaderToo` — the sync `Send` override (kept for Blazor/JSON-RPC callers).
  - `SendAsync_WithHeaderAlreadyPresent_DoesNotThrowOrDuplicate` — set-if-absent: a pre-set header survives untouched and a re-sent request does not throw (the retry/resilience fix — spec Deliberate Behavior Change 10).
  - `SendAsync_WithoutAmbientCorrelationId_SendsNoCorrelationHeader` — nothing to propagate → header absent.
  - `AddCloudstrapCorrelationHandler_CalledTwice_AddsSingleHeaderValue` — idempotent registration: the captured request has exactly one header value, not two handlers stacked (spec: tolerates `ConfigureHttpClientDefaults` + explicit call).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Observability.Tests\bin\Debug\net10.0\Cloudstrap.Observability.Tests.exe --filter "CorrelationHttpDelegatingHandlerTests"
  ```

**GREEN**:
- `CorrelationHttpDelegatingHandler` — ctor `(ICorrelationContextAccessor accessor, IOptions<CorrelationOptions> options)`; overrides `SendAsync` **and** `Send`: when the accessor holds a non-empty id and the request does not already carry the configured header, `TryAddWithoutValidation` it; forward to base.
- `HttpClientBuilderExtensions.AddCloudstrapCorrelationHandler` — guard; register the handler transient (`TryAddTransient`) and add it via `AddHttpMessageHandler<CorrelationHttpDelegatingHandler>()` only when not already added for this builder (idempotence — executor picks the mechanism, e.g. inspecting/marking the builder's `HttpClientFactoryOptions`); XML docs name deliverable 4's typed-client registration as the intended caller.
- Types come from the `Microsoft.AspNetCore.App` shared framework (`Microsoft.Extensions.Http` ships inside it) — no new `PackageReference`.

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: a typed client now carries the ambient correlation id in the configured header, safely under retries and double registration — AC-B4 is now proven end to end (inbound Step 9 + outbound here, same header, same value).
2. `dotnet build src/Cloudstrap.sln` → zero warnings; `dotnet format src/Cloudstrap.sln --verify-no-changes` → exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 5: correlation flows in, through, and out *(covers Steps 8–10)*

*Executor: STOP here. Present the results of all covered steps and WAIT for user approval — do not start the next step.*

⚠️ **Risk area — public API surface**: `ICorrelationContextAccessor`, `ICorrelationSource`, `CorrelationHttpDelegatingHandler`, `AddCloudstrapCorrelationHandler` and the two attributes are the seam deliverables 4 and 14 consume — permanent surface.

- [x] Behavioral verification: test exe output shows the ambient accessor flowing/isolated across async flows; the merged middleware honoring the inbound header, generating the trace id when absent, returning 400 `application/problem+json` naming the configured header, and all four exemptions (health path, excluded path, `[AllowNoCorrelation]`, health-check metadata); the delegating handler propagating the same value in the same header with set-if-absent and idempotent registration (AC-B4/B5/B6).
- [x] Code review: `Cloudstrap.Observability.Correlation` surface vs the Public API Sketch, type by type; no resurrected Drop rows (`CorrelationHeader` constant, `ICorrelationContext`/`DefaultCorrelationContext`, static `AsyncLocal`, already-set throw, `/swagger`-style hard-coded exemptions, display-name matching, response-header echo); ⚠️ planner mechanic (d) — `AddCloudstrapCorrelation` calling the framework's additive `AddProblemDetails()` — confirm or direct a change.
- [x] User approved — implementation may continue past this gate *(approved 2026-07-29; planner mechanic (d) — additive `AddProblemDetails()` — confirmed)*

---

## Slice 6 — Business tracing, the shared health-tag vocabulary, and a publishable package

---

## Step 11 — Consumers record low-cardinality business spans through `IBusinessTrace`; the shared health-check tag vocabulary ships; AC-B1 resolution proven whole (AC-B1 full)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Observability/IBusinessTrace.cs` *(create)* — public interface: `IBusinessTraceScope StartSpan(string operation, string component)` (spec Port — with the documented low-cardinality guidance, no user/document identifiers).
- `src/Cloudstrap.Observability/IBusinessTraceScope.cs` *(create)* — public interface: `IDisposable`, `bool IsRecording { get; }`, `void SetOutcome(string outcome)` (spec Port).
- `src/Cloudstrap.Observability/BusinessTrace.cs` *(create)* — `internal sealed : IBusinessTrace, IDisposable`; singleton `ActivitySource(CloudstrapActivitySources.Business)`; tags `cloudstrap.business.*` (spec Redesign — `nihdi.business.*` renamed).
- `src/Cloudstrap.Observability/BusinessTraceScope.cs` *(create)* — `internal sealed` (spec Port).
- `src/Cloudstrap.Observability/CloudstrapActivitySources.cs` *(create)* — `public static class`, `public const string Business = "Cloudstrap.Business"` (so consumers can `AddSource` it themselves).
- `src/Cloudstrap.Observability/CloudstrapHealthCheckTags.cs` *(create)* — `public static class`: `Liveness = "live"`, `Readiness = "ready"` (spec Redesign of `Nihdi.Core.Health` tags — two constants, no severity taxonomy; consumed by deliverables 4/5/7/12).
- `src/Cloudstrap.Observability/ServiceCollectionExtensions.cs` *(create)* — `public static IServiceCollection AddCloudstrapBusinessTrace(this IServiceCollection services)`: guard + `TryAddSingleton<IBusinessTrace, BusinessTrace>` (spec Port: registration regardless of pipeline state).
- `src/Cloudstrap.Observability/HostApplicationBuilderExtensions.cs` + `OpenTelemetryPipeline.cs` *(modify)* — entry point calls `AddCloudstrapBusinessTrace()`; owner **and** contribute tracing add `AddSource(CloudstrapActivitySources.Business)`.
- `src/Test/UnitTest/Cloudstrap.Observability.Tests/BusinessTraceTests.cs` *(create)*

**RED** *(write these tests first, run them, confirm they fail — `ActivityListener` fixtures for source-level assertions; in-memory exporter for the pipeline-integration ones)*:
- Unit test file: `BusinessTraceTests.cs`
  - `StartSpan_WithListener_CreatesActivityFromBusinessSourceWithComponentTag` — activity source name `Cloudstrap.Business`, operation as the span name, `cloudstrap.business.component` tag set.
  - `StartSpan_WithoutListener_ReturnsNonRecordingScopeAndDoesNotThrow` — `IsRecording` false; `SetOutcome` + `Dispose` are safe no-ops (a disabled pipeline never breaks consumers).
  - `SetOutcome_OnRecordingScope_SetsOutcomeTag` — `cloudstrap.business.outcome`.
  - `Dispose_OnRecordingScope_StopsTheActivity`.
  - `UseCloudstrapObservability_OwnerMode_ExportsBusinessSpanThroughPipeline` — a `StartSpan` inside a Console-mode host reaches the in-memory exporter without any consumer `AddSource` (the pipeline pre-wires the source; contribute-mode counterpart asserted via the Step-7 fixture shape).
  - `UseCloudstrapObservability_WithModeDisabled_ResolvesBusinessTraceAndCorrelation` — **the composite AC-B1 test**: `Mode=Disabled` host starts; `IBusinessTrace`, `ICorrelationContextAccessor`, `ICorrelationSource` all resolve; no `TracerProvider`/`MeterProvider`; Serilog console logging works.
  - `HealthCheckTags_HoldTheSharedVocabulary` — `Liveness == "live"`, `Readiness == "ready"` (the cross-package contract, pinned by test).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Observability.Tests\bin\Debug\net10.0\Cloudstrap.Observability.Tests.exe --filter "BusinessTraceTests"
  ```

**GREEN**: the seven files above per the spec sketch; `BusinessTrace` keeps the `IDisposable`-singleton-`ActivitySource` pattern (DI disposes at shutdown); XML docs carry the low-cardinality guidance verbatim in spirit.

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: consumers can now record business spans that ride the pipeline in both modes, everything AC-B1 lists resolves even when telemetry is off, and the shared health-tag vocabulary exists for deliverables 4/5/7/12 (AC-B1 full).
2. `dotnet build src/Cloudstrap.sln` → zero warnings; `dotnet format src/Cloudstrap.sln --verify-no-changes` → exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 12 — The package is publishable: complete metadata + README (incl. the shared-framework requirement), guarded dependency closure, zero enterprise identifiers (AC-B11, AC-B12, AC-O4, AC-O2/AC-ASP2 guards)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Observability/Cloudstrap.Observability.csproj` *(modify)* — `<Description>` (Serilog logging + vendor-neutral OpenTelemetry pipeline + correlation + business tracing driven by the `Cloudstrap:` section), `<PackageTags>$(PackageTags);observability;opentelemetry;serilog;tracing;correlation</PackageTags>`, `<PackageReadmeFile>README.md</PackageReadmeFile>` + `<None Include="README.md" Pack="true" PackagePath="/" />`.
- `src/Cloudstrap.Observability/README.md` *(create)* — package purpose; `UseCloudstrapObservability` quick start with a neutral `appsettings.json` (`Contoso`/`Orders`/`Api`, `example.com`); the mode table (`Disabled`/`Console`/`Otlp`/`AzureMonitor` + the AC-B7 fail-fast note pointing at `Cloudstrap.Observability.AzureMonitor`); OTLP endpoint resolution (explicit setting wins, `OTEL_EXPORTER_OTLP_ENDPOINT` hand-off, neither → startup fails); owner vs contribute (the Aspire posture, `ApplySampler` caveat); logging behavior (providers added — never cleared, level seeds + override precedence, file-sink semantics incl. the `log-.log` naming, `ConfigureSerilog`); correlation (header convention + override, middleware placement after routing, `AddProblemDetails` note, the delegating handler + `AddCloudstrapCorrelationHandler`); `IBusinessTrace` guidance; `CloudstrapHealthCheckTags`; bootstrap logger usage/disposal; **the OQ-1 consequence stated plainly: this package carries a `Microsoft.AspNetCore.App` framework reference — every consumer requires the ASP.NET Core shared framework at run time, so `mcr.microsoft.com/dotnet/runtime`-only base images are not supported**.
- `src/Test/UnitTest/Cloudstrap.Observability.Tests/PackageSurfaceTests.cs` *(create)* — permanent guard tests (mirrors the Core idiom).

**RED** *(recorded explicitly, as in plan 1 Step 5: the guard tests are written and run first but, as tripwires against correct code, may pass immediately — the honest failing state is in the artifacts: before GREEN the Release nupkg has no README and no description/observability tags)*:
- Unit test file: `PackageSurfaceTests.cs`
  - `ReferencedAssemblies_OfObservabilityAssembly_MatchTheApprovedClosure` — every referenced assembly name starts with `System`, `Microsoft.`, `Serilog`, `OpenTelemetry`, or equals `Cloudstrap.Core`; explicitly assert **zero** names starting `Azure` (AC-O2 dependency half), `Aspire` (AC-ASP2), `LanguageExt`, `NServiceBus` — permanent tripwires.
  - `PublicTypes_OfObservabilityAssembly_ContainNoForbiddenIdentifiers` — no public type/member matches `(?i)nihdi|riziv|dynatrace|nservicebus` (compiled AC-B11 complement).
  - `PublicTypes_OfObservabilityAssembly_AreSealedAndInTheTwoApprovedNamespaces` — namespaces ∈ {`Cloudstrap.Observability`, `Cloudstrap.Observability.Correlation`}; public classes sealed or abstract/static.
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Observability.Tests\bin\Debug\net10.0\Cloudstrap.Observability.Tests.exe --filter "PackageSurfaceTests"
  ```

**GREEN**: add the csproj metadata and write `README.md` per Scope.

**DB changes**: none.

**VERIFY**:
1. Test exe (full run) → all tests across all fixtures pass, including the three new guards.
2. `dotnet build src/Cloudstrap.sln -c Release` → `src/Cloudstrap.Observability/bin/Release/Cloudstrap.Observability.<version>.nupkg`; expand a `.zip` copy → contains `README.md`, `icon.png`, `lib/net10.0/Cloudstrap.Observability.dll` **and** `.xml`; nuspec shows MIT license expression, description, tags, repository URL, and the dependency list (Serilog/OTel/Cloudstrap.Core — no `Azure.*`, no `Aspire.*`) (AC-B12 metadata side).
3. **AC-B11 sweep** (package + tests):
   ```powershell
   Get-ChildItem -Recurse -File -Path src/Cloudstrap.Observability, src/Test/UnitTest/Cloudstrap.Observability.Tests |
     Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
     Select-String -Pattern '(?i)(nihdi|riziv|dynatrace|nservicebus|probe\.aspx|logsint)'
   ```
   → zero matches.
4. **AC-O4 sweep** (the whole solution tree — `_specs/`/`_plans/` document the removal and are excluded by scoping to `src/`):
   ```powershell
   Get-ChildItem -Recurse -File -Path src |
     Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
     Select-String -Pattern '(?i)dynatrace'
   ```
   → zero matches.
5. **Closure check**: `dotnet list src/Cloudstrap.Observability/Cloudstrap.Observability.csproj package` → exactly the pinned Serilog/OTel/Microsoft.Extensions set + the `Cloudstrap.Core` project reference; the csproj carries the single `Microsoft.AspNetCore.App` framework reference and nothing else. `Cloudstrap.Core`'s own csproj is untouched (host-agnostic closure not regressed).
6. Full gates on the final tree: `dotnet build src/Cloudstrap.sln` (zero warnings) + both test exes (exit 0) + `dotnet format src/Cloudstrap.sln --verify-no-changes` (exit 0) — AC-B12.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 6: deliverable #2 complete *(covers Steps 11–12)*

*Executor: STOP here. Present the results and WAIT for user approval. Any Git push afterwards requires the user's explicit go-ahead (CLAUDE.md: no push without confirmation).*

- [x] Behavioral verification: `IBusinessTrace` tests green (source spans, pipeline export in both modes, safe no-op without a listener); the composite AC-B1 test green; guard tests green; expanded `.nupkg` contents reviewed (README, icon, dll + XML docs, nuspec metadata + dependency list); AC-B11 and AC-O4 sweeps output empty (only self-referential guard-test tripwire patterns); `dotnet list package` closure reviewed; build/tests/format green on the final tree.
- [x] Code review: README accuracy against the implemented behavior (mode table, endpoint resolution, owner/contribute, level precedence, correlation conventions, the shared-framework/runtime-image consequence — gate decision OQ-1); `Description`/tags wording; `CloudstrapHealthCheckTags` values as the cross-package contract.
- [x] Spec acceptance sign-off: walk AC-O2/O3/O4, AC-ASP1/ASP2, AC-B1…AC-B13 (+ amended AC-C6) against the step evidence using the Overview's AC coverage map — all met; confirm nothing from the spec's Out of Scope list was resurrected.
- [x] User approved — deliverable #2 done *(accepted 2026-07-30; ROADMAP status update delegated to the project-manager)*
