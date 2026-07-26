# Plan: 1-CoreSettingsModel — A consumer binds and validates the `Cloudstrap:` configuration section into a typed `CloudstrapOptions` model

## Overview

Deliverable #1 of the extraction roadmap: the `Cloudstrap.Core` package — the typed, fail-fast-validated settings model every later package consumes. **Binding spec: `_specs/1-CoreSettingsModel.md`** (approved 2026-07-25, zero Open Questions; its Port Decision Table, Public API Sketch, and Out of Scope list are authoritative — nothing marked Drop or Move-out may appear here, and per the post-approval amendment `IsRunningInAks()`/`CloudstrapEnvironment` must **not** be resurrected). Reference patterns, both read in full before planning:

- **Source feature (redesign, not copy)**: `D:\source\Nihdi-Core-Configuration\Nihdi-Core-Configuration\src\Nihdi.Core.Configuration\` — `Settings\NihdiConfiguration.cs`, `Settings\ApplicationConfiguration.cs`, `Settings\LoggingConfiguration.cs` + `Settings\Logging\{Console,File}Configuration.cs`, `Settings\OpenTelemetryConfiguration.cs` + `OpenTelemetryMode.cs`, `Settings\CorrelationConfiguration.cs` + `Settings\Correlation\*`, `Settings\HealthChecks\HealthChecksConfiguration.cs`, `Settings\HttpClient\*`, `ConfigurationExtensions.cs`, `ConfigurationException.cs`. Every rename/type-upgrade below follows the spec's verdict table (e.g. `BusinessSystemName` → `SystemName`, `BaseUri` string → `Endpoint` `Uri?`, `AccessToken` → `Headers`, hand-rolled `Validate()` cascade → framework options validation).
- **Repo pattern (deliverable 0, verified on disk)**: `src/Test/UnitTest/Cloudstrap.Scaffolding.Tests/` is the shape for the new test project (csproj = Sdk + `TargetFramework` only; everything else from `src/Test/Directory.Build.props`: NUnit 4 on Microsoft.Testing.Platform, `OutputType=Exe`, `EnableNUnitRunner`, `IsPackable=false`, CS1591+CA1707 off). Package projects inherit metadata (MIT, icon, SourceLink, CPM) from the frozen `src/Directory.Build.props`.

This is a library deliverable with no UI, no controllers, and no database: a "vertical slice" here is an end-to-end **consumer capability** (bind → validate eagerly → validate via DI → ship as package), and the template's integration-endpoint test block does not apply — the DI-resolution integration tests (AC-C1/AC-C2) live in the unit-test project via a real `ServiceCollection` + in-memory `IConfiguration`, exactly as the spec's test strategy prescribes (no files, no network, neutral fixture values `contoso`/`example.com`).

**Prerequisites (deliverable 0 — state verified 2026-07-25):** `src/Cloudstrap.sln`, `src/Directory.Build.props` (frozen), `src/Directory.Packages.props` (CPM), `src/Test/Directory.Build.props`, and the placeholder test project all exist and are green locally — nothing blocks local implementation. Two items from `_plans/0-RepoScaffolding.md`'s closing gate are still unchecked and are **carried, not blocking**: (a) the GitHub-side CI behavioral verification + that gate's approval box, and (b) the nuget.org `Cloudstrap.` prefix reservation — (b) must be done before this package is ever *published* (re-flagged at Gate 3). Also noted: `.claude/instructions/tests.md` still says MSTest/`dotnet test` — stale drift; this plan follows the repo's actual NUnit-4-on-MTP convention and the CLAUDE.md rule that `dotnet test` is unsupported (run the test exe directly).

**AC coverage map** (from `_specs/1-CoreSettingsModel.md`): AC-C4/AC-C5 → Step 1 · AC-C10 + AC-C1 (binding half) → Step 2 · AC-C3/AC-C6 → Step 3 · AC-C1/AC-C2 → Step 4 · AC-C7/AC-C8/AC-ASP2 + AC-C9 (metadata) → Step 5 · AC-C9 (build/test/format/XML docs) → every step's VERIFY.

**Validation mechanics (planner's choice, per the spec's stated preference):** simple required rules via DataAnnotations `[Required]` (only `ApplicationOptions` carries any) validated by a source-generated `[OptionsValidator]` class; conditional/cross-property rules (`Otlp` ⇒ `Endpoint` http/https, `File.Enabled` ⇒ `Path`, `BaseAddress` required + absolute) via hand-written `internal sealed` `IValidateOptions<T>` classes; one root `CloudstrapOptionsValidator` composes the per-section validators (prefixing failure messages with the section path) and iterates the `HttpClients` dictionary. Every rule lives in exactly one class; the eager path (`GetCloudstrapOptions`) and the DI path (`AddCloudstrapCore` + `ValidateOnStart`) both invoke these same validators — Step 4 proves it with a same-rule-both-paths test. Consequence: `Microsoft.Extensions.Options.DataAnnotations` is expected to be **unnecessary** (the source generator emits the `[Required]` checks directly) — a deliberate minimize-dependencies deviation from the spec's dependency table, flagged for confirmation at Gate 2.

New CPM entries (all Microsoft-maintained, MIT; executor pins the latest patch of the .NET 10-era line in `src/Directory.Packages.props` at implementation time): production — `Microsoft.Extensions.Logging.Abstractions` (Step 2), `Microsoft.Extensions.Options`, `Microsoft.Extensions.Configuration.Abstractions`, `Microsoft.Extensions.Configuration.Binder` (Step 3), `Microsoft.Extensions.Options.ConfigurationExtensions`, `Microsoft.Extensions.DependencyInjection.Abstractions` (Step 4); test-only — `Microsoft.Extensions.Configuration` + `Microsoft.Extensions.Configuration.Binder` (Step 1), `Microsoft.Extensions.DependencyInjection` (Step 4).

---

## Slice 1 — A consumer binds the `Cloudstrap` section into typed options with conventions and defaults

---

## Step 1 — Bind application identity: `Cloudstrap:Application` yields the computed workload name and normalized path base (package + test project bootstrap)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope** *(all layers touched by this slice)*:
- `src/Cloudstrap.Core/Cloudstrap.Core.csproj` *(create)* — `<Project Sdk="Microsoft.NET.Sdk">`, `TargetFramework=net10.0`, `GeneratePackageOnBuild=true`, `GenerateDocumentationFile=true` (the CLAUDE.md two-property packaging rule; all other metadata inherits from the frozen `src/Directory.Build.props`). No package references yet. **No `<FrameworkReference Include="Microsoft.AspNetCore.App">`** — the source csproj line 18 has one; the new Core is host-agnostic (AC-C8).
- `src/Cloudstrap.Core/ApplicationOptions.cs` *(create)*
- `src/Test/UnitTest/Cloudstrap.Core.Tests/Cloudstrap.Core.Tests.csproj` *(create)* — mirror of `Cloudstrap.Scaffolding.Tests.csproj` (Sdk + `TargetFramework=net10.0`) plus `<ProjectReference Include="..\..\..\Cloudstrap.Core\Cloudstrap.Core.csproj" />` and version-less `<PackageReference>`s: `Microsoft.Extensions.Configuration` (ConfigurationBuilder + `AddInMemoryCollection`), `Microsoft.Extensions.Configuration.Binder` (`Get<T>`).
- `src/Test/UnitTest/Cloudstrap.Core.Tests/ApplicationOptionsTests.cs` *(create)*
- `src/Cloudstrap.sln` *(modify)* — add `Cloudstrap.Core` at the solution root and `Cloudstrap.Core.Tests` under the existing `Test\UnitTest` solution folders.
- `src/Directory.Packages.props` *(modify)* — add `PackageVersion` items for `Microsoft.Extensions.Configuration` and `Microsoft.Extensions.Configuration.Binder`.

**RED** *(write these tests first, run them, confirm they fail — for brand-new types the failure is a compile error of the test project, the standard RED for new code)*:
- Unit test file: `src/Test/UnitTest/Cloudstrap.Core.Tests/ApplicationOptionsTests.cs` — `[TestFixture] public sealed class ApplicationOptionsTests`, NUnit 4 constraint model, AAA. Binding tests build the config with `new ConfigurationBuilder().AddInMemoryCollection(...).Build().GetSection(ApplicationOptions.SectionName).Get<ApplicationOptions>()`. Fixture values: `Contoso` / `Orders` / `Api`.
- Unit test methods:
  - `WorkloadName_WithoutExplicitValue_ComputesLowercaseSystemSubsystemType` — expects `contoso-orders-api` (AC-C4).
  - `WorkloadName_WithExplicitConfigValue_OverridesComputation` — `Cloudstrap:Application:WorkloadName = my-workload` wins verbatim (AC-C4).
  - `PathBase_WithConfiguredValue_NormalizesToLeadingSlashNoTrailing` — `[TestCase("myapp", "/myapp")]`, `[TestCase("/myapp/", "/myapp")]`, `[TestCase("", "")]` (AC-C5).
  - `SectionValues_WhenBound_PopulateIdentityProperties` — `SystemName`/`SubsystemName`/`SubsystemType`/`EnvironmentTier` round-trip from config.
  - `Defaults_WithoutConfiguredValues_HoldDocumentedValues` — `ExceptionHandlerPath == "/error"`, `EnvironmentTier` is null, `PathBase == ""`.
- Failing-run command *(this repo forbids `dotnet test` — run the MTP executable directly)*:
  ```powershell
  dotnet build src/Cloudstrap.sln   # RED = test project fails to compile against missing types
  src\Test\UnitTest\Cloudstrap.Core.Tests\bin\Debug\net10.0\Cloudstrap.Core.Tests.exe --filter "ApplicationOptionsTests"
  ```

**GREEN** *(minimal production code to make RED pass)*:
- `ApplicationOptions` — `public sealed class`, namespace `Cloudstrap.Core` (all package types share this single namespace per the spec), full XML docs (CS1591 is build-breaking):
  - `public const string SectionName = "Cloudstrap:Application";`
  - `[Required] public string SystemName { get; set; } = string.Empty;` — same for `SubsystemName`, `SubsystemType` (renamed from source `BusinessSystemName`/`SubSystemName`/`SubSystemType`; `System.ComponentModel.DataAnnotations` comes from the shared framework, no package needed).
  - `public string WorkloadName` — backing field `string? _workloadName`; getter returns the explicit value when not null/empty, else `$"{SystemName}-{SubsystemName}-{SubsystemType}".ToLowerInvariant()` (source used culture-sensitive `ToLower()` — `ToLowerInvariant` avoids CA1311); setter stores the override.
  - `public string? EnvironmentTier { get; set; }` — optional free-form tier label, no behavior (replaces the dropped LOC/DEV/TST/VAL/PRD taxonomy).
  - `public string PathBase` — backing field, setter normalizes exactly like the source (`$"/{value.Trim('/')}"` when non-empty, else `""`).
  - `public string ExceptionHandlerPath { get; set; } = "/error";`
  - **Not ported** (spec verdicts): `Environment` + `EnvironmentIs*()` predicates, `ConfidentialityLevel`, `StorageName`/`BlobContainerName`/`BlobContainerUri`.

**DB changes**: none — this repository has no database.

**VERIFY** *(after making GREEN changes, run these checks; when all green, mark this step's `Done` checkbox and continue straight to the next step — stop only when the next plan item is a 🛑 HUMAN GATE)*:
1. `src\Test\UnitTest\Cloudstrap.Core.Tests\bin\Debug\net10.0\Cloudstrap.Core.Tests.exe` → all tests pass, exit 0 — the assembly now computes `contoso-orders-api` and normalizes path bases, which nothing in the repo did before.
2. `dotnet build src/Cloudstrap.sln` → zero warnings/errors (XML docs on all public members enforced by CS1591).
3. `dotnet format src/Cloudstrap.sln --verify-no-changes` → exit 0.
4. `dotnet build src/Cloudstrap.sln -c Release` → a `Cloudstrap.Core.*.nupkg` appears under `src/Cloudstrap.Core/bin/Release/` (packable from day one; metadata completed in Step 5) and none under `src/Test/`.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 2 — Bind the full `Cloudstrap` section: every subsection resolves typed with the documented defaults

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Core/CloudstrapOptions.cs` *(create)* — root model.
- `src/Cloudstrap.Core/LoggingOptions.cs`, `ConsoleLoggingOptions.cs`, `FileLoggingOptions.cs` *(create)*
- `src/Cloudstrap.Core/OpenTelemetryOptions.cs`, `OpenTelemetryMode.cs` *(create)*
- `src/Cloudstrap.Core/CorrelationOptions.cs`, `CorrelationRequestOptions.cs`, `CorrelationMessageOptions.cs` *(create)*
- `src/Cloudstrap.Core/HealthChecksOptions.cs` *(create)*
- `src/Cloudstrap.Core/HttpClientServiceOptions.cs`, `TokenRequestOptions.cs` *(create)*
- `src/Cloudstrap.Core/Cloudstrap.Core.csproj` *(modify)* — add `<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />` (the `LogLevel` enum).
- `src/Directory.Packages.props` *(modify)* — add `Microsoft.Extensions.Logging.Abstractions`.
- `src/Test/UnitTest/Cloudstrap.Core.Tests/CloudstrapOptionsTests.cs` *(create)* — full-graph binding + defaults.
- `src/Test/UnitTest/Cloudstrap.Core.Tests/OpenTelemetryOptionsTests.cs` *(create)* — computed properties + enum binding.

**RED** *(write these tests first, run them, confirm they fail)*:
- Unit test files: `CloudstrapOptionsTests.cs`, `OpenTelemetryOptionsTests.cs`
- Unit test methods:
  - `GetSection_WithFullCloudstrapSection_BindsEverySubsection` — one in-memory config covering all six sections plus two `HttpClients` entries (`CatalogApi` → `https://catalog.example.com/`, `OrdersApi`); asserts representative values across the whole graph, including `LogLevel` enum binding (`"Warning"` → `LogLevel.Warning`), `LevelOverrides` dictionary, `Headers` dictionary, `Uri` binding of `Endpoint` and `BaseAddress`, `TimeSpan` binding of `Timeout` (`"00:00:10"` → 10 s), and `TokenRequestParameters.Scope` (AC-C1 binding half).
  - `GetSection_WithOnlyRequiredValues_AppliesDocumentedDefaults` — full AC-C10 assertion set: `HealthChecks` → `Enabled=true`, `LivenessPath="/healthz"`, `ReadinessPath="/ready"`; `Correlation.HeaderName="X-Correlation-ID"`, `Request.RequireForAllEndpoints=false`, `Request.HealthEndpoints == ["/healthz","/ready"]`, `Message.RequireForAllMessageHandlers=false`; `Logging.Level=LogLevel.Information`, `Console.Enabled=true`, `File.Enabled=false`, `File.Path` null; `OpenTelemetry.Mode=Disabled` and `EnableTracing/Metrics/Logs/Console/RuntimeMetrics/HttpClientMetrics/AspNetCoreMetrics/MessagingMetrics=true`, `EnableSqlClientInstrumentation=false`, `EnableBlazorHubTracing=false`, `AlwaysOnSampler=false`; `HttpClientServiceOptions.Timeout` default 30 s.
  - `Mode_WithAzureMonitorString_BindsReservedEnumValue` — `"AzureMonitor"` → `OpenTelemetryMode.AzureMonitor` (value 3, reserved for deliverable 3).
  - `IsActive_ForEachMode_TracksModeNotDisabled` — `[TestCase]` over the four modes.
  - `IsConsoleEnabled_ForModeAndFlagCombinations_ComposesCorrectly` — `[TestCase]`s: Disabled/any → false; Console → true; Otlp+`EnableConsole=true` → true; Otlp+`EnableConsole=false` → false; AzureMonitor+`EnableConsole=true` → true (mirrors the source's non-obvious composition, extended to the new mode).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Core.Tests\bin\Debug\net10.0\Cloudstrap.Core.Tests.exe --filter "CloudstrapOptionsTests"
  ```

**GREEN** — all types `public sealed`, namespace `Cloudstrap.Core`, mutable auto-properties (binder requirement), parameterless ctors, full XML docs, each with its `SectionName` const exactly per the spec sketch:
- `CloudstrapOptions` — `const SectionName = "Cloudstrap"`; properties `Application`, `Logging`, `OpenTelemetry`, `Correlation`, `HealthChecks` (each `= new();`) and `HttpClients` (`Dictionary<string, HttpClientServiceOptions>`, `= [];`). **No** `AppRegistration`/`Security`/`Scalar`/`Swagger`/`NServiceBus`/`Hangfire`/`Dashboard`/`ConnectionStrings` members, no `IsRunningInAks`, no `Validate()` methods (spec verdicts).
- `LoggingOptions` — `Level` : `Microsoft.Extensions.Logging.LogLevel` `= LogLevel.Information` (source: string `"Debug"`); `LevelOverrides` : `Dictionary<string, LogLevel>` (renamed from `LevelOverwrites`); `EnrichProperties` : `Dictionary<string, string>`; `Console` : `ConsoleLoggingOptions`; `File` : `FileLoggingOptions`. **No** `Dynatrace` property.
- `ConsoleLoggingOptions` — `Enabled = true` (straight port).
- `FileLoggingOptions` — `Enabled = false`; `Path` : `string?` with **no default** (the source's `LogFolderPathRoot = "D:\\logsint"` is deleted — De-NIHDI; required-when-enabled enforced in Step 3).
- `OpenTelemetryMode` — `Disabled = 0, Console = 1, Otlp = 2, AzureMonitor = 3`; doc comments carry no vendor references.
- `OpenTelemetryOptions` — `Mode = Disabled`; `Endpoint` : `Uri?` (was `BaseUri` string); `Headers` : `Dictionary<string, string>` (replaces `AccessToken`); `EnableTracing/EnableMetrics/EnableLogs/EnableConsole = true`; `EnableRuntimeMetrics/EnableHttpClientMetrics/EnableAspNetCoreMetrics = true`; `EnableMessagingMetrics = true` (was `EnableNServiceBusMetrics`); `EnableSqlClientInstrumentation = false` (was `AddSqlClientSupport`); `EnableBlazorHubTracing = false`; `AlwaysOnSampler = false`; computed `IsActive => Mode != OpenTelemetryMode.Disabled` and `IsConsoleEnabled => IsActive && (Mode == OpenTelemetryMode.Console || EnableConsole)`. **No** `IsOtlp` helper.
- `CorrelationOptions` — `HeaderName = "X-Correlation-ID"` (new, configurable — replaces the source's hard-coded `NIHDI.Correlation` constant); `Request` : `CorrelationRequestOptions`; `Message` : `CorrelationMessageOptions`.
- `CorrelationRequestOptions` — `RequireForAllEndpoints = false`; `HealthEndpoints` initialized to `["/healthz", "/ready"]` (source: `/live`,`/ready`,`/health`); `ExcludeEndpoints` initialized empty. ⚠️ *Binder gotcha for gate review*: `Microsoft.Extensions.Configuration` **appends** to pre-initialized collections, so configuring `HealthEndpoints` adds to (not replaces) the defaults — spec still mandates these defaults; the semantics get documented in the Step 5 README and consuming deliverables (2/4/5) inherit the caveat.
- `CorrelationMessageOptions` — `RequireForAllMessageHandlers = false`; `ExcludeMessageHandlers` empty (straight port; docs mention message handlers generically, not NServiceBus).
- `HealthChecksOptions` — `Enabled = true`; new `LivenessPath = "/healthz"`, `ReadinessPath = "/ready"` (the source type had only `Enabled`; the paths were hard-coded elsewhere).
- `HttpClientServiceOptions` — `BaseAddress` : `Uri?` (was string, unvalidated); `Timeout` : `TimeSpan = TimeSpan.FromSeconds(30)` (was `TimeoutInSeconds` int); `AddUserAccessToken`/`AddClientAccessToken` `= false`; `EnableHealthCheck = false`; `HealthCheckPrefix` : `string?`; `TokenRequestParameters` : `TokenRequestOptions?`. (The source's `HttpClientServiceRegistryConfiguration` dictionary subclass is **not** ported — the plain dictionary lives on the root.)
- `TokenRequestOptions` — `Scope`/`Resource`/`SignInScheme`/`ChallengeScheme` : `string?`; `ForceRenewal = false` (sealed, renamed from `TokenRequestParameters`).
- Analyzer note for the executor: if CA1002/CA2227 (`List<T>`/settable collections) fire at `latest-recommended`, prefer get-only initialized collection properties (the binder populates them in place) — do not suppress; keep the config shape identical.

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: the full `Cloudstrap` section now binds end-to-end with every documented default (AC-C10) — observable behavior that did not exist before this step.
2. `dotnet build src/Cloudstrap.sln` → zero warnings; `dotnet format src/Cloudstrap.sln --verify-no-changes` → exit 0.
3. Quick sweep ahead of the gate: `Get-ChildItem -Recurse -File src/Cloudstrap.Core | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } | Select-String -Pattern '(?i)(nihdi|riziv|dynatrace|nservicebus|hangfire|dashboard|swagger|scalar)'` → zero matches (early AC-C7 signal).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 1: the options shape is frozen *(covers Steps 1–2)*

*Executor: STOP here. Present the results of all covered steps and WAIT for user approval — do not start the next step.*

⚠️ **Risk area — public API surface.** This model is consumed by every later package (roadmap §1 risk: "shape mistakes propagate"). Renames after later deliverables build on it require a deprecation cycle. Review the shape now, before validation and DI entry points are built on top of it.

- [x] Behavioral verification: test exe run output shows the Step 1 convention tests (`contoso-orders-api` computation, explicit `WorkloadName` override, `PathBase` normalization cases) and the Step 2 full-graph binding + AC-C10 defaults tests all green; build/format gates green.
- [x] Code review — public API vs the spec's Public API Sketch, type by type: names, property types (`LogLevel`, `Uri?`, `TimeSpan`), defaults, `SectionName` constants, single `Cloudstrap.Core` namespace, everything `sealed`, XML docs complete; **zero** dropped/moved-out members resurrected (no `Environment` taxonomy, no storage naming, no `AppRegistration`, no `IsRunningInAks`, no `Dynatrace`, no `ConnectionStrings`).
- [x] ⚠️ Confirm the flagged binder semantics on `CorrelationRequestOptions.HealthEndpoints` (config values append to defaults) are acceptable, to be documented in the Step 5 README.
- [x] ⚠️ Dependency review (risk area): new CPM entries so far — `Microsoft.Extensions.Configuration` + `Microsoft.Extensions.Configuration.Binder` (test-only), `Microsoft.Extensions.Logging.Abstractions` (production). All MIT, versions pinned only in `src/Directory.Packages.props`.
- [x] User approved — implementation may continue past this gate *(approved 2026-07-26)*

---

## Slice 2 — Invalid configuration fails fast through both entry points

---

## Step 3 — Pre-host reads fail fast: `GetCloudstrapOptions()` returns validated options or throws `ConfigurationValidationException` naming every failure

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Core/ConfigurationValidationException.cs` *(create)*
- `src/Cloudstrap.Core/ApplicationOptionsValidator.cs` *(create)* — source-generated DataAnnotations validator.
- `src/Cloudstrap.Core/LoggingOptionsValidator.cs` *(create)* — conditional rule.
- `src/Cloudstrap.Core/OpenTelemetryOptionsValidator.cs` *(create)* — conditional rule.
- `src/Cloudstrap.Core/CloudstrapOptionsValidator.cs` *(create)* — root composition + `HttpClients` rule.
- `src/Cloudstrap.Core/ConfigurationExtensions.cs` *(create)* — eager entry point.
- `src/Cloudstrap.Core/Cloudstrap.Core.csproj` *(modify)* — add `Microsoft.Extensions.Options` (brings `IValidateOptions<T>`, `ValidateOptionsResult`, and the `[OptionsValidator]` source generator), `Microsoft.Extensions.Configuration.Abstractions` (`IConfiguration` in the public signature), `Microsoft.Extensions.Configuration.Binder` (`Get<T>`).
- `src/Directory.Packages.props` *(modify)* — add those three `PackageVersion` items.
- `src/Test/UnitTest/Cloudstrap.Core.Tests/ConfigurationExtensionsTests.cs` *(create)*
- `src/Test/UnitTest/Cloudstrap.Core.Tests/ConfigurationValidationExceptionTests.cs` *(create)*

**RED** *(write these tests first, run them, confirm they fail — all rules are exercised through the public eager path; validators stay `internal` and untested directly, no `InternalsVisibleTo`)*:
- Unit test file: `ConfigurationExtensionsTests.cs` — helper builds an in-memory config from a dictionary; a "minimal valid" baseline sets only `Cloudstrap:Application:{SystemName,SubsystemName,SubsystemType}`.
- Unit test methods:
  - `GetCloudstrapOptions_WithValidSection_ReturnsBoundOptions` — baseline returns options with `Application.SystemName == "Contoso"`.
  - `GetCloudstrapOptions_WithoutCloudstrapSection_ThrowsNamingMissingSection` — empty config → `ConfigurationValidationException`, message contains `Cloudstrap` (AC-C3).
  - `GetCloudstrapOptions_WithMissingSystemName_ThrowsWithFailureNamingSystemName` — `Failures` has an entry containing `SystemName` (AC-C3).
  - `GetCloudstrapOptions_WithMultipleViolations_ListsEveryFailure` — missing `SystemName` **and** `Otlp` without `Endpoint` → both failures present in `Failures` (AC-C3 "lists every validation failure").
  - `GetCloudstrapOptions_WithOtlpModeAndNoEndpoint_ThrowsNamingEndpoint` (AC-C6).
  - `GetCloudstrapOptions_WithOtlpModeAndNonHttpEndpoint_ThrowsNamingEndpoint` — `ftp://collector.example.com` (AC-C6).
  - `GetCloudstrapOptions_WithOtlpModeAndHttpsEndpoint_ReturnsOptions` (AC-C6 pass; also asserts `Mode = AzureMonitor` with nothing extra passes — AC-C6 last clause).
  - `GetCloudstrapOptions_WithFileLoggingEnabledAndNoPath_ThrowsNamingFilePath` — failure names `Logging:File:Path`.
  - `GetCloudstrapOptions_WithHttpClientMissingBaseAddress_ThrowsNamingClientEntry` — failure names `HttpClients:CatalogApi:BaseAddress`.
  - `GetCloudstrapOptions_WithRelativeHttpClientBaseAddress_ThrowsNamingClientEntry`.
  - `GetCloudstrapOptions_WithNullConfiguration_ThrowsArgumentNullException` (guard clause).
- Unit test file: `ConfigurationValidationExceptionTests.cs`
  - `Ctor_WithMessageAndFailures_ExposesFailuresList`; `Ctor_WithMessageOnly_HasEmptyFailures`; `Ctor_WithInnerException_PreservesInner`.
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Core.Tests\bin\Debug\net10.0\Cloudstrap.Core.Tests.exe --filter "ConfigurationExtensionsTests"
  ```

**GREEN**:
- `ConfigurationValidationException` — `public sealed class ... : Exception`; ctors `(string message)`, `(string message, Exception innerException)`, `(string message, IEnumerable<string> failures)`; `public IReadOnlyList<string> Failures { get; }` (empty when not supplied). Redesign of the source `ConfigurationException` per the spec row (renamed, sealed, structured failures). Add the conventional parameterless ctor only if an analyzer (CA1032) demands it.
- `ApplicationOptionsValidator` — `[OptionsValidator] internal sealed partial class ApplicationOptionsValidator : IValidateOptions<ApplicationOptions>` — the generator emits the `[Required]` checks (reflection-free; failure messages name the member, e.g. `SystemName`).
- `LoggingOptionsValidator` — `internal sealed class : IValidateOptions<LoggingOptions>`: when `File.Enabled` and `string.IsNullOrWhiteSpace(File.Path)` → `ValidateOptionsResult.Fail("Logging:File:Path is required when Logging:File:Enabled is true.")`; else `Success`.
- `OpenTelemetryOptionsValidator` — `internal sealed class : IValidateOptions<OpenTelemetryOptions>`: only when `Mode == Otlp` — `Endpoint` null → fail naming `Endpoint`; `Endpoint` not absolute or scheme not `http`/`https` → fail naming `Endpoint`. Other modes → `Success` (AC-C6: `AzureMonitor` requires nothing extra here).
- `CloudstrapOptionsValidator` — `internal sealed class : IValidateOptions<CloudstrapOptions>` — **the single cascade both entry points share**: runs `ApplicationOptionsValidator` on `options.Application`, `LoggingOptionsValidator` on `options.Logging`, `OpenTelemetryOptionsValidator` on `options.OpenTelemetry` (static readonly instances), prefixing each failure with its section path (`Application:` / `OpenTelemetry:`); then iterates `options.HttpClients` — entry with `BaseAddress` null or `!BaseAddress.IsAbsoluteUri` → failure `HttpClients:{name}:BaseAddress must be an absolute URI.`. Aggregates everything into one `ValidateOptionsResult.Fail(failures)` (replaces the source's buggy `Validator.TryValidateObject` cascade — spec finding 3).
- `ConfigurationExtensions` — `public static class`, method `public static CloudstrapOptions GetCloudstrapOptions(this IConfiguration configuration)`: `ArgumentNullException.ThrowIfNull`; `GetSection(CloudstrapOptions.SectionName)`; `!section.Exists()` → throw `ConfigurationValidationException` with a neutral message naming the missing `Cloudstrap` section; `section.Get<CloudstrapOptions>()`; run `CloudstrapOptionsValidator`; on failure throw `ConfigurationValidationException("Cloudstrap configuration is invalid.", result.Failures)`. **No** `ConnectionStrings` copy-in, **no** `AppRegistration` credential-smearing (spec verdicts on the source `GetNihdiConfiguration`).

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: an invalid `Cloudstrap` section now stops a pre-host read with a structured exception naming every offending member — behavior that did not exist before (AC-C3, AC-C6).
2. `dotnet build src/Cloudstrap.sln` → zero warnings; `dotnet format src/Cloudstrap.sln --verify-no-changes` → exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 4 — Host startup fails on invalid config: `AddCloudstrapCore()` resolves every options type with `ValidateOnStart`

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Core/ServiceCollectionExtensions.cs` *(create)*
- `src/Cloudstrap.Core/Cloudstrap.Core.csproj` *(modify)* — add `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Options.ConfigurationExtensions` (`BindConfiguration`).
- `src/Directory.Packages.props` *(modify)* — add those two plus test-only `Microsoft.Extensions.DependencyInjection` (`BuildServiceProvider`).
- `src/Test/UnitTest/Cloudstrap.Core.Tests/Cloudstrap.Core.Tests.csproj` *(modify)* — add `<PackageReference Include="Microsoft.Extensions.DependencyInjection" />`.
- `src/Test/UnitTest/Cloudstrap.Core.Tests/ServiceCollectionExtensionsTests.cs` *(create)* — these are the DI-registration/service-resolution **integration tests** for this library (no controllers exist, so the endpoint-integration block does not apply; a real `ServiceCollection` + real options pipeline is the full stack here).

**RED** *(write these tests first, run them, confirm they fail)*:
- Unit test file: `ServiceCollectionExtensionsTests.cs` — arrange: `new ServiceCollection()` + `AddSingleton<IConfiguration>(inMemoryConfig)` + `services.AddCloudstrapCore()` + `BuildServiceProvider()`.
- Unit test methods:
  - `AddCloudstrapCore_WithValidConfiguration_ResolvesRootAndAllSectionOptions` — resolves `IOptions<CloudstrapOptions>` **and** `IOptions<ApplicationOptions>`, `IOptions<LoggingOptions>`, `IOptions<OpenTelemetryOptions>`, `IOptions<CorrelationOptions>`, `IOptions<HealthChecksOptions>`, each carrying the bound values (AC-C1).
  - `AddCloudstrapCore_WithValidConfiguration_StartupValidationSucceeds` — resolve `Microsoft.Extensions.Options.IStartupValidator`, call `Validate()` → no throw (host-agnostic equivalent of a clean host start; a real host triggers exactly this service on startup).
  - `AddCloudstrapCore_WithMissingSystemName_StartupValidationThrowsNamingMember` — `IStartupValidator.Validate()` → `OptionsValidationException` whose message contains `SystemName` (AC-C2).
  - `AddCloudstrapCore_WithOtlpModeAndNoEndpoint_StartupValidationThrowsNamingEndpoint` — the same rule that Step 3 proved on the eager path fails the DI path too: single validator implementation, two entry points (spec's validation-mechanics requirement).
  - `AddCloudstrapCore_CalledTwice_ResolvesSingleValidatorPerOptionsType` — `TryAddEnumerable` idempotence: registering twice does not duplicate `IValidateOptions<CloudstrapOptions>` descriptors.
  - `AddCloudstrapCore_WithNullServices_ThrowsArgumentNullException` (guard clause).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Core.Tests\bin\Debug\net10.0\Cloudstrap.Core.Tests.exe --filter "ServiceCollectionExtensionsTests"
  ```

**GREEN**:
- `ServiceCollectionExtensions` — `public static class` with `public static IServiceCollection AddCloudstrapCore(this IServiceCollection services)`. **No `Action<...>` configure parameter** — deliberate spec decision (standard `services.Configure<T>()`/`PostConfigure` layering after the call is the override idiom; deviates from the CLAUDE.md DI template on the spec's authority; documented in the Step 5 README). Implementation:
  - Guard: `ArgumentNullException.ThrowIfNull(services)`.
  - Root: `services.AddOptions<CloudstrapOptions>().BindConfiguration(CloudstrapOptions.SectionName).ValidateOnStart();` + `services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<CloudstrapOptions>, CloudstrapOptionsValidator>());`
  - Per-section, each bound to its own `SectionName` const with `.ValidateOnStart()`: `ApplicationOptions` (+ `TryAddEnumerable` `ApplicationOptionsValidator`), `LoggingOptions` (+ `LoggingOptionsValidator`), `OpenTelemetryOptions` (+ `OpenTelemetryOptionsValidator`), `CorrelationOptions`, `HealthChecksOptions` (no validators — no rules exist for them; `ValidateOnStart` still applied so future rules activate without an API change).
  - Returns `services`. XML docs note that `BindConfiguration` requires `IConfiguration` in the container (hosts register it automatically).

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: a `ServiceCollection` wired with `AddCloudstrapCore` now resolves all six options types, and startup validation rejects the same invalid configs the eager path rejects — new observable behavior (AC-C1, AC-C2).
2. `dotnet build src/Cloudstrap.sln` → zero warnings; `dotnet format src/Cloudstrap.sln --verify-no-changes` → exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 2: fail-fast validation proven on both entry points *(covers Steps 3–4)*

*Executor: STOP here. Present the results of all covered steps and WAIT for user approval — do not start the next step.*

⚠️ **Risk area — public API surface**: `ConfigurationValidationException`, `GetCloudstrapOptions`, and `AddCloudstrapCore` are the package's permanent entry points.

- [x] Behavioral verification: test exe output shows — eager path: missing section / missing `SystemName` / Otlp-without-Endpoint / non-HTTP endpoint / file-logging-without-path / missing-or-relative `BaseAddress` all throw `ConfigurationValidationException` with failures naming the member, multi-violation configs list every failure, and valid configs (including `Mode=AzureMonitor` with nothing extra) pass; DI path: all six options types resolve and `IStartupValidator.Validate()` throws `OptionsValidationException` naming `SystemName`/`Endpoint` on the same bad configs (single-rule-set proof).
- [x] Code review: validator composition (each rule in exactly one `internal` class, root validator the only cascade), failure-message quality (section-path prefixes), guard clauses, `AddCloudstrapCore` registration list vs the spec sketch, no-configure-parameter decision acknowledged.
- [x] ⚠️ Dependency review (risk area): production additions `Microsoft.Extensions.Options`, `Options.ConfigurationExtensions`, `Configuration.Abstractions`, `Configuration.Binder`, `DependencyInjection.Abstractions` (+ test-only `Microsoft.Extensions.DependencyInjection`) — all MIT, CPM-pinned. Confirm the deliberate omission of `Microsoft.Extensions.Options.DataAnnotations` (spec dependency table lists it; the `[OptionsValidator]` source generator makes it redundant — fewer dependencies wins unless the user objects). **Confirmed at the 2026-07-26 gate: omitted.**
- [x] User approved — implementation may continue past this gate *(approved 2026-07-26; also acknowledged: a broken section-level rule is reported twice at startup — once root-relative, once section-relative)*

---

## Slice 3 — Cloudstrap.Core ships as a publishable package

---

## Step 5 — The package is publishable: complete metadata + README, clean dependency closure, zero enterprise identifiers, scaffolding placeholder retired

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Core/Cloudstrap.Core.csproj` *(modify)* — `<Description>` (typed, validated settings model for the `Cloudstrap:` configuration section), `<PackageTags>$(PackageTags);configuration;options;settings</PackageTags>`, `<PackageReadmeFile>README.md</PackageReadmeFile>` + `<None Include="README.md" Pack="true" PackagePath="/" />`.
- `src/Cloudstrap.Core/README.md` *(create)* — package purpose; full `Cloudstrap` section reference with a defaults table (mirrors AC-C10) and a neutral `appsettings.json` example (`Contoso`/`Orders`/`Api`, `example.com`); the two entry points (`AddCloudstrapCore` DI path, `GetCloudstrapOptions` eager path) with the exception contract; the code-level override idiom (`services.Configure<T>()`/`PostConfigure` after `AddCloudstrapCore` — per the spec's Behaviors table); the workload-naming, probe-path, and correlation-header conventions and their overrides; the ⚠️ collection-binding append semantics from Step 2 (Gate 1 decision).
- `src/Test/UnitTest/Cloudstrap.Core.Tests/PackageSurfaceTests.cs` *(create)* — permanent guard tests for AC-C7/AC-C8.
- `src/Test/UnitTest/Cloudstrap.Scaffolding.Tests/` *(delete — entire folder)* and `src/Cloudstrap.sln` *(modify)* — remove the placeholder project (spec 0: "removed (or absorbed) when deliverable 1 brings the first real test project"; spec 1 test strategy confirms). `Cloudstrap.Core.Tests` is now the test leg; the CI loop must still find ≥ 1 test executable.

**RED** *(recorded explicitly: the two guard tests are written and run first, but as regression tripwires against correct code they may pass immediately — the honest failing state of this step is observable instead in the artifacts: before GREEN, the Release `.nupkg` contains no README and carries no `Description`/`configuration` tags [inspect the nuspec], and the solution still builds the scaffolding placeholder)*:
- Unit test file: `src/Test/UnitTest/Cloudstrap.Core.Tests/PackageSurfaceTests.cs`
- Unit test methods:
  - `ReferencedAssemblies_OfCoreAssembly_AreSystemOrMicrosoftExtensionsOnly` — every `typeof(CloudstrapOptions).Assembly.GetReferencedAssemblies()` name starts with `System` or `Microsoft.Extensions.` (permanent AC-C8/AC-ASP2 guard: no `Aspire.*`, no `LanguageExt.*`, no ASP.NET Core assemblies can ever sneak in unnoticed).
  - `PublicTypes_OfCoreAssembly_ContainNoForbiddenIdentifiers` — no public type or member name matches `(?i)nihdi|riziv|dynatrace` (compiled tripwire complementing the textual sweep below).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Core.Tests\bin\Debug\net10.0\Cloudstrap.Core.Tests.exe --filter "PackageSurfaceTests"
  ```

**GREEN**:
- Add the csproj metadata and write `README.md` per Scope.
- Delete the `Cloudstrap.Scaffolding.Tests` project folder and its solution entries (project + nested-folder mapping + configuration platform lines).

**DB changes**: none.

**VERIFY**:
1. Test exe (`Cloudstrap.Core.Tests.exe`) → all tests pass including the two new guards; `src\Test\UnitTest\Cloudstrap.Scaffolding.Tests\` no longer exists and the solution still builds — the test leg did not silently vanish.
2. `dotnet build src/Cloudstrap.sln -c Release` → `src/Cloudstrap.Core/bin/Release/Cloudstrap.Core.<version>.nupkg`; expand it (`Expand-Archive` on a `.zip` copy) → contains `README.md`, `icon.png`, `lib/net10.0/Cloudstrap.Core.dll` **and** `Cloudstrap.Core.xml`; the nuspec shows MIT license expression, description, tags, repository URL (AC-C9 metadata side).
3. **AC-C7 sweep**:
   ```powershell
   Get-ChildItem -Recurse -File -Path src/Cloudstrap.Core, src/Test/UnitTest/Cloudstrap.Core.Tests |
     Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
     Select-String -Pattern '(?i)(nihdi|riziv|dynatrace)'
   ```
   → zero matches; second sweep over `src/Cloudstrap.Core` only for `(NServiceBus|Hangfire|Dashboard|Swagger|Scalar|Security|AppRegistration|ConnectionStrings)` → zero matches (Core is a true leaf).
4. **AC-C8 closure**: `dotnet list src/Cloudstrap.Core/Cloudstrap.Core.csproj package` → only the `Microsoft.Extensions.*` set from this plan; visual check of the csproj → no `<FrameworkReference>`, no `Aspire.*`, no `LanguageExt.*`.
5. Full gates green on the final tree: `dotnet build src/Cloudstrap.sln` (zero warnings) + test exe (exit 0) + `dotnet format src/Cloudstrap.sln --verify-no-changes` (exit 0) — AC-C9.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 3: deliverable #1 complete *(covers Step 5)*

*Executor: STOP here. Present the results and WAIT for user approval. Any Git push afterwards requires the user's explicit go-ahead (CLAUDE.md: no push without confirmation).*

- [ ] Behavioral verification: guard tests green; expanded `.nupkg` contents reviewed (README, icon, dll + XML docs, nuspec metadata); AC-C7 sweep outputs empty; `dotnet list package` output shows the Microsoft.Extensions-only closure; build/test/format gates green with the placeholder project gone.
- [ ] Code review: README accuracy against the implemented model (defaults table = AC-C10, override idioms, conventions, the collection-append caveat); `Description`/tags wording; solution file clean after the placeholder removal.
- [ ] Spec acceptance sign-off: walk AC-C1…AC-C10 + AC-ASP2 against the step evidence (map in the Overview) — all met.
- [ ] Carried prerequisite (operational, before this package is ever *published*): nuget.org `Cloudstrap.` prefix reservation and the deliverable-0 GitHub-side CI verification items are still open in `_plans/0-RepoScaffolding.md`'s final gate — confirm status or explicitly defer again.
- [ ] User approved — deliverable #1 done *(ROADMAP status update belongs to the project-manager, not the executor)*
