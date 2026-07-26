# Spec: Core Settings Model — `Cloudstrap.Core` (Roadmap Deliverable #1)

> **Approved 2026-07-25 — zero Open Questions remain; spec is planner-ready.** Both gate questions were resolved per this spec's recommendations (see the Decision Log at the end): root type `CloudstrapOptions` + `GetCloudstrapOptions()` (OQ-1), and `AppRegistrationConfiguration` dropped outright (OQ-2).
>
> Sources: `_plans/ROADMAP.md` §1 (hand-off brief + dependency-analysis items 1 & 5) · `_specs/Cloudstrap.md` (Decisions Made, De-NIHDI-fication Checklist, Aspire Coexistence) · `_specs/0-RepoScaffolding.md` (repo conventions: NUnit 4 on MTP, CPM, SDK analyzers, CS1591 in `src/`, `net10.0` only) · source reference repo (read-only) `D:\source\Nihdi-Core-Configuration\Nihdi-Core-Configuration\src\Nihdi.Core.Configuration\` — every type listed in the Port Decision Table was read in full, plus consumer usage across the source solution (Common, WebApi, BlazorServer, Worker, NServiceBus, Proxy, Dashboard, tests).
>
> Key code-reading findings that shaped this spec:
> 1. **The `Nihdi.Core.Functional` ProjectReference is dead** — `Nihdi.Core.Configuration.csproj` line 24 references it, but no `.cs` file in the project uses any Functional type (repo-wide grep: the only "Functional" hit inside the project is the csproj itself). **`Cloudstrap.Core` therefore takes no LanguageExt.Core dependency**; the founding spec's "exact type mapping is settled when the first consuming package is planned" clause defers to the first package that actually consumes functional types (`Cloudstrap.Testing` or `Cloudstrap.Dashboard.*` on current evidence).
> 2. The old root model is a god-object: `NihdiConfiguration` aggregates every feature's settings (NServiceBus, Hangfire, Security, Scalar/Swagger, Dashboard — the Dashboard one via a `TypeForwardedTo` into `Dashboard.Contracts`, the inverted dependency the roadmap cuts). This spec keeps only the six cross-cutting sections in Core and records the destination of everything else.
> 3. The bespoke validation cascade (`Validator.TryValidateObject` per section with manual `MemberName`s) is both redundant with modern options validation and buggy in places — e.g. `NihdiConfiguration.Validate` validates the `Scalar` section under `MemberName = "Swagger"` (line 146) and calls `TryValidateObject` on a plain `string` (`LoggingConfiguration.cs` line 54), which validates nothing. It is replaced wholesale by framework options validation.

## User Story

**As an** ASP.NET Core developer deploying to Azure,
**I want to** describe my application (identity, logging, telemetry, correlation, health, outbound HTTP clients) once, in a typed, validated `Cloudstrap:` configuration section,
**So that** every Cloudstrap package reads the same strongly-typed, fail-fast-validated settings model instead of each one re-inventing configuration keys — and a typo in my `appsettings.json` stops the app at startup with a message naming the offending property.

---

## Acceptance Criteria

> The founding spec defines no AC-numbered criteria for the Core area (its numbered ACs cover Observability, Messaging, Auth, and Aspire). AC-ASP2 applies to every shipped package and is carried over verbatim. AC-C1…AC-C10 are new, spec-specific criteria formalizing the roadmap §1 definition of done.

| # | Given | When | Then |
|---|-------|------|------|
| AC-C1 | An `appsettings.json` with a valid `Cloudstrap` section | `services.AddCloudstrapCore()` runs and the host starts | `IOptions<CloudstrapOptions>` and each per-section options type (`IOptions<ApplicationOptions>`, `LoggingOptions`, `OpenTelemetryOptions`, `CorrelationOptions`, `HealthChecksOptions`) resolve with the bound values. |
| AC-C2 | A `Cloudstrap` section missing a required value (e.g. `Application:SystemName`) | The host starts (`ValidateOnStart`) | Startup fails with the framework's `OptionsValidationException`; the failure message names the offending option member. |
| AC-C3 | An `IConfiguration` without a `Cloudstrap` section, or with an invalid one | `configuration.GetCloudstrapOptions()` (eager, pre-host path) is called | A `ConfigurationValidationException` is thrown whose message names the missing section or lists every validation failure. |
| AC-C4 | `SystemName = "Contoso"`, `SubsystemName = "Orders"`, `SubsystemType = "Api"`, no explicit workload name | `ApplicationOptions.WorkloadName` is read | It returns `contoso-orders-api` (lowercase `{system}-{subsystem}-{type}`); setting `Cloudstrap:Application:WorkloadName` explicitly overrides the computed value. |
| AC-C5 | `Cloudstrap:Application:PathBase` set to `myapp`, `/myapp/`, or empty | `ApplicationOptions.PathBase` is read | It returns `/myapp` for the first two and `""` for empty (normalized to a single leading slash, no trailing slash). |
| AC-C6 | `Cloudstrap:OpenTelemetry:Mode = Otlp` with no `Endpoint` / with a non-HTTP(S) endpoint / with a valid endpoint | Options validation runs | The first two fail with messages naming `Endpoint`; the third passes. `Mode = AzureMonitor` requires nothing extra at the Core level (exporter settings are deliverable 3's). |
| AC-C7 | The `Cloudstrap.Core` project and package | Searched case-insensitively for `Nihdi`, `NIHDI`, `Riziv`, `Dynatrace`, and for any Dashboard / NServiceBus / Hangfire / Security / Swagger / Scalar settings type | Zero occurrences — Core is a true leaf containing only the types in this spec's Public API Sketch. |
| AC-C8 | The package's dependency closure | Inspected | Only `Microsoft.Extensions.*` packages (MIT); zero `Aspire.*` (AC-ASP2), zero `LanguageExt.*`, and **no** `Microsoft.AspNetCore.App` framework reference (the source project had one; the new Core is host-agnostic and Blazor-WASM-loadable). |
| AC-C9 | A fresh clone | Build, test-executable run, and `dotnet format --verify-no-changes` | All green; XML docs present on all public API (CS1591 enforced per repo conventions); package metadata complete (description, tags, README). |
| AC-C10 | Default values, no `Cloudstrap` section overrides beyond required fields | Options are bound | Documented defaults hold: `HealthChecksOptions` → `Enabled=true`, `LivenessPath="/healthz"`, `ReadinessPath="/ready"`; `CorrelationOptions.HeaderName="X-Correlation-ID"`; `LoggingOptions.Level=Information`, `Console.Enabled=true`, `File.Enabled=false`; `OpenTelemetryOptions.Mode=Disabled`. |
| AC-ASP2 | Any shipped Cloudstrap package | Its dependency closure is inspected | Zero `Aspire.*` packages. *(carried verbatim from the founding spec)* |

---

## Port Decision Table

One row per public type of the source project `Nihdi.Core.Configuration`. Verdicts: **Port** / **Redesign** / **Replace** (by a library or framework feature) / **Drop** / **Move → N** (leaves Core for roadmap deliverable N — final verdict is that spec's job; the destination recorded here is binding for "where", not "whether").

### In scope for Cloudstrap.Core

| Source type (path relative to project root) | Verdict | Target | Justification |
|---|---|---|---|
| `Settings\NihdiConfiguration.cs` — root aggregate | **Redesign** | `CloudstrapOptions` (root, section `Cloudstrap`) *(name settled at the 2026-07-25 gate — OQ-1)* | The aggregate concept survives (the founding spec mandates a typed root model; the pre-host bootstrap path in deliverables 2/4 needs it), but the god-object is cut to the six cross-cutting sections. Removed from it: `NServiceBus`/`Hangfire`/`Security`/`Scalar`/`Swagger`/`Dashboard`/`AppRegistration` properties (see move-out table), the `ConnectionStrings` dictionary + `GetConnectionString`/`GetDefaultConnectionString` (framework's `IConfiguration.GetConnectionString` covers it — Aspire-coexistence "speak platform conventions"), the hand-rolled `Validate()` cascade (replaced by framework options validation, see finding 3), and `IsRunningInAks()` (dropped, next row). |
| `Settings\NihdiConfiguration.cs` → `IsRunningInAks()` static helper | **Drop** *(amended 2026-07-25 — was Redesign → `CloudstrapEnvironment.IsRunningInKubernetes()`)* | — | In the source this was never "am I on Kubernetes" but a proxy for the enterprise's cloud-AKS-vs-on-prem-IIS split — a dichotomy Cloudstrap abandons (supported hosting: Azure Web Apps + containers/Kubernetes; no on-prem IIS/VM). On Azure Web Apps `KUBERNETES_SERVICE_HOST` is absent, so the helper would mis-classify a first-class target as the legacy branch. Its 10+ source call sites all dissolve under the new design: credentials → `DefaultAzureCredential` (identical on Web Apps/AKS/local dev), message encryption dropped, messaging rewritten (14), the `D:\logsint` file-logging default deleted; console format, DataProtection, and forwarded headers become explicit options — "every convention has an override", and environment sniffing has none. If a later deliverable needs a K8s-keyed default, a three-line internal check is trivial to add then. |
| `Settings\ApplicationConfiguration.cs` | **Redesign** | `ApplicationOptions` | Application identity + workload naming earn their place (the `{system}-{subsystem}-{type}` convention drives queue names, secret prefixes, path-base defaults — founding spec keeps it, overridable). Kept: `SystemName` (was `BusinessSystemName`), `SubsystemName`, `SubsystemType` (all required), computed-but-overridable `WorkloadName`, normalized `PathBase`, `ExceptionHandlerPath` (read by Extensions/WebApi/BlazorServer hosting — one shared setting beats three copies). Dropped: `Environment` string + `EnvironmentIsDevTest/IsLocal/IsPrd()` (LOC/DEV/TST/VAL/PRD taxonomy → standard ASP.NET Core environments per founding-spec decision; replaced by the optional free-form `EnvironmentTier`), `StorageName`/`BlobContainerName`/`BlobContainerUri` (hard-coded enterprise storage naming — De-NIHDI checklist; deliverable 4 introduces explicit `Cloudstrap:Storage:BlobServiceUri`), `ConfidentialityLevel` property (next row). |
| `ConfidentialityLevel.cs` (enum) | **Drop** | — | Enterprise data-classification taxonomy whose only functional consumer is the dropped `StorageName` naming convention (repo-wide grep: no other reference outside Core). An open-source consumer gains nothing from a three-value classification enum with no behavior. |
| `EnvironmentConstants.cs` (`Names.LOC/DEV/TST/VAL/PRD`) | **Drop** | — | The enterprise environment taxonomy is replaced by standard ASP.NET Core environments + `ApplicationOptions.EnvironmentTier` (founding-spec decision). Publishing another org's tier names as constants would be noise. |
| `BootstrapConfiguration.cs` | **Drop** | — (framework feature) | `WebApplication.CreateBuilder`/`Host.CreateApplicationBuilder` already layer `appsettings.json` + `appsettings.{Environment}.json` + environment variables. What this type adds on top is all enterprise legacy: `app.xml` (Windows-Services era), `appsettings.{MachineName}.json` (De-NIHDI: drop machine-name parsing), and the `Nihdi__Application__Environment` variable (custom taxonomy). Its own XML docs are wrong twice (`appsettings.overrides.json` is never added; "NIDHI" typo). Later deliverables read `builder.Configuration` instead of a bespoke pre-host builder. |
| `ConfigurationBuilderExtensions.cs` (`AddAppSettingsFile` / `AddAppSettingsEnvironmentFile` / `AddXmlFile` / `AddAppSettingsMachineNameFile`) | **Drop** | — (framework feature) | Same reasoning as `BootstrapConfiguration` — each method wraps a one-line stock `IConfigurationBuilder` call around an enterprise convention that Cloudstrap deliberately abandons. |
| `ConfigurationExtensions.cs` → `GetNihdiConfiguration` | **Redesign** | `GetCloudstrapOptions(this IConfiguration)` | The eager "bind the section now, pre-DI" accessor earns its place (bootstrap logging in deliverable 2 and KeyVault config in deliverable 4 run before the host exists). Redesigned: binds `Cloudstrap`, **validates before returning** (guard behavior per roadmap DoD), throws `ConfigurationValidationException`. Dropped from it: the `ConnectionStrings` copy-in, and the `AppRegistration` → NServiceBus/Bridge credential-smearing block (messaging transport auth is deliverable 14's concern, solved with `TokenCredential` conventions, not by mutating one options object from another). |
| `ConfigurationExtensions.cs` → `GetConnectionStrings` | **Drop** | — (framework feature) | Duplicates `IConfiguration.GetSection("ConnectionStrings")` / `GetConnectionString(name)` into a detached dictionary. Platform convention wins (founding spec, Aspire Coexistence §3). |
| `ConfigurationException.cs` | **Redesign** | `ConfigurationValidationException` (sealed) | The concept (one exception type for "your configuration is invalid") ports — the roadmap DoD names it. Redesigned per repo API rules: renamed, `sealed`, carries the individual failure messages (`IReadOnlyList<string> Failures`) so callers/logs get structure, standard message/inner-exception constructors. |
| `AuthenticationFlow.cs` (enum) | **Move → 10** | decided by the OIDC spec | Sole consumer is `Settings\Security\AuthenticationConfiguration` (repo-wide grep). Travels with the Security settings; the OIDC spec should re-judge it against stock OIDC handler options + Duende ATM (the two `Implicit*` members are deprecated flows and unlikely to survive). |
| `GlobalSuppressions.cs` | **Drop** | — | Empty file (copyright header only). Build artifact, not API. |
| `Settings\LoggingConfiguration.cs` | **Redesign** | `LoggingOptions` | Serilog-backed logging config earns its place (deliverable 2 consumes). Kept: minimum level, per-source overrides, enrichment properties, console/file children. Redesigned: `Level` string → `Microsoft.Extensions.Logging.LogLevel` enum (typo-proof binding, platform vocabulary; deliverable 2 maps to Serilog), default `Information` instead of `Debug` (sane production default), `LevelOverwrites` → `LevelOverrides` (`Dictionary<string, LogLevel>`). Dropped: the `Dynatrace` property (next table) and the no-op string validation (finding 3). |
| `Settings\Logging\ConsoleConfiguration.cs` | **Port** | `ConsoleLoggingOptions` | Single `Enabled = true` flag, consumed by the bootstrap logger (deliverable 2). Nothing to redesign. |
| `Settings\Logging\FileConfiguration.cs` | **Redesign** | `FileLoggingOptions` | File logging stays optional-off. De-NIHDI: the `D:\logsint` default path is deleted; `Path` (was `LogFolderPathRoot`) has **no default and is required when `Enabled = true`** (validated). |
| `Settings\Logging\DynatraceConfiguration.cs` | **Drop** | — | Dynatrace is removed entirely (founding-spec decision; roadmap §1 says delete outright). Its OTLP-ish knobs are not generalized — the `OpenTelemetryOptions.Headers` dictionary is the sanctioned generic path. |
| `Settings\OpenTelemetryConfiguration.cs` | **Redesign** | `OpenTelemetryOptions` | The mode-driven OTel config is the flagship setting and ports nearly whole. Redesigned: `BaseUri` string → `Endpoint` (`Uri?`, OTel-conventional name; validated http/https when `Mode = Otlp`); `AccessToken` → dropped, replaced by a generic `Headers` dictionary (founding spec: "no `Api-Token` helper; configurable headers dictionary instead"); `AddSqlClientSupport` → `EnableSqlClientInstrumentation` (consistent `Enable*` verb); `EnableNServiceBusMetrics` → `EnableMessagingMetrics` (Wolverine, De-NServiceBus); helper `IsOtlp` dropped (trivial, and it doesn't scale to the fourth mode). Kept: `Mode`, `EnableTracing/Metrics/Logs`, `EnableConsole` + `IsConsoleEnabled` (non-obvious composition), `EnableRuntimeMetrics/HttpClientMetrics/AspNetCoreMetrics`, `AlwaysOnSampler` (founding spec keeps the dev flag), `EnableBlazorHubTracing`, `IsActive`. |
| `Settings\OpenTelemetryMode.cs` (enum) | **Redesign** | `OpenTelemetryMode` | Ports with one addition: `AzureMonitor = 3` (founding-spec mode set `Disabled | Console | Otlp | AzureMonitor`). Core owns only the enum value; the exporter settings and wiring are deliverable 3's (which binds its own options under `Cloudstrap:OpenTelemetry:AzureMonitor`). Doc comments lose the "e.g. Dynatrace" reference. |
| `Settings\CorrelationConfiguration.cs` | **Redesign** | `CorrelationOptions` | Correlation config is consumed by deliverables 2 (middleware/enrichment), 4 (delegating handler), 5/6 and 14. Redesigned: gains `HeaderName = "X-Correlation-ID"` — the old header was the *non-configurable* constant `NIHDI.Correlation` in `Common\Correlation\CorrelationHeader.cs`; the founding spec requires a configurable default, and Core's correlation section is its natural owner. The manual child-validation cascade goes (framework handles nested validation). |
| `Settings\Correlation\CorrelationRequestConfiguration.cs` | **Redesign** | `CorrelationRequestOptions` | Kept: `RequireForAllEndpoints=false`, `HealthEndpoints`, `ExcludeEndpoints`. Redesigned: `HealthEndpoints` default `["/live","/ready","/health"]` → `["/healthz","/ready"]` (aligned with the new probe conventions); the "at least one health endpoint" validation rule is deleted — an empty exclusion list is a legitimate configuration ("require correlation absolutely everywhere"), and the old rule fired even when `RequireForAllEndpoints` was off. |
| `Settings\Correlation\CorrelationMessageConfiguration.cs` | **Port** | `CorrelationMessageOptions` | `RequireForAllMessageHandlers=false` + `ExcludeMessageHandlers` port as-is (names are already transport-neutral); doc remarks lose the NServiceBus reference (handlers are Wolverine's from deliverable 14). The empty `Validate` method goes. |
| `Settings\HealthChecks\HealthChecksConfiguration.cs` | **Redesign** | `HealthChecksOptions` | The `Enabled=true` toggle ports. Gains `LivenessPath = "/healthz"` and `ReadinessPath = "/ready"` — the founding spec demands the probe paths be configurable (De-NIHDI row for `/probe.aspx`), and multiple packages consume them (endpoint mapping in 4/5/7/12, trace noise filter in 2, correlation exclusions above); one shared setting in Core prevents four divergent copies. Old Common hard-coded `/live`, `/ready`, `/health`, `/probe.aspx`. |
| `Settings\HttpClient\HttpClientServiceConfiguration.cs` | **Redesign** | `HttpClientServiceOptions` | The named-client registry pattern feeds `AddCloudstrapHttpServiceClient<TI,TImpl>` (deliverable 4) and earns its place. Redesigned: `BaseAddress` string → `Uri?` **required + absolute** (the source had zero validation here — a missing base address surfaced only as a runtime failure in the typed client); `TimeoutInSeconds` int → `Timeout` (`TimeSpan`, default 30 s — binder-native, matches `Microsoft.Extensions.Http` conventions). Kept: `AddUserAccessToken` / `AddClientAccessToken` (auth seams filled by 9/10 — Core holds only the flags), `EnableHealthCheck` / `HealthCheckPrefix` (consumed by 4's per-client URI health checks). |
| `Settings\HttpClient\HttpClientServiceRegistryConfiguration.cs` | **Redesign** | `Dictionary<string, HttpClientServiceOptions>` property `HttpClients` on the root | Subclassing `Dictionary<string,T>` adds nothing (the subclass has no members beyond a default ctor) and costs an extra public type. Config section renamed `Nihdi:HttpClientServiceRegistry` → `Cloudstrap:HttpClients` (shorter, no "registry" jargon). The old cross-check "`AddUserAccessToken` requires `Security.EnableAuthentication`" cannot live in Core anymore (Security leaves) — it moves to deliverable 10 as an `IValidateOptions` there. |
| `Settings\HttpClient\TokenRequestParameters.cs` | **Port** | `TokenRequestOptions` | `Scope`, `Resource`, `ForceRenewal`, `SignInScheme`, `ChallengeScheme` mirror Duende ATM's token request parameters; Core keeps the plain POCO so it doesn't reference Duende — deliverable 9 maps it. Renamed per options naming convention and sealed. |
| `Settings\AppRegistration\AppRegistrationConfiguration.cs` | **Drop** *(gate decision 2026-07-25 — OQ-2)* | — | Exists solely to smear a client-ID/secret/tenant triple from config into the NServiceBus ASB transport settings (`ConfigurationExtensions.GetNihdiConfiguration` lines 25–37 — its only consumer). Encouraging client secrets in app configuration is the anti-pattern the Azure SDK's `TokenCredential` conventions (`DefaultAzureCredential`, standard `AZURE_TENANT_ID`/`AZURE_CLIENT_ID`/`AZURE_CLIENT_SECRET` variables, managed identity) exist to replace. **User decision**: no Cloudstrap type models client-id/secret credentials in appsettings; deliverable 14 builds transport auth on `TokenCredential`/`DefaultAzureCredential` + the standard `AZURE_*` environment variables. |

### Moving out of Core (destination recorded; not specced here)

| Source folder / type | Destination | Note |
|---|---|---|
| `Settings\Dashboard\DashboardConfiguration.cs` | **→ 19** | Already a `TypeForwardedTo` shim into `Dashboard.Contracts` — the inverted dependency the roadmap cuts. Core ships zero Dashboard types. |
| `Settings\Hangfire\HangfireConfiguration.cs`, `HangfireDashboardConfiguration.cs`, `HangfireJobConfiguration.cs` | **→ 16** | Config shape reference for `Cloudstrap.Hangfire`. |
| `Settings\NServiceBus\NServiceBusConfiguration.cs`, `TransportConfiguration.cs`, `AzureServiceBusTransportConfiguration.cs`, `SqlTransportConfiguration.cs`, `SqlPersistenceConfiguration.cs`, `RetryConfiguration.cs`, `NamingConventionsExtensions.cs`, `ValidTransportTypes.cs`, `ValidPersistenceTypes.cs`, `ValidTransactionMode.cs` | **→ 14** | Config-shape *reference only* — Messaging is a Wolverine rewrite, not a port. |
| `Settings\NServiceBus\BridgeConfiguration.cs`, `BridgeDestinationConfiguration.cs` | **Deleted** | MessagingBridge is permanently out of scope (founding spec). |
| `Settings\NServiceBus\MonitoringConfiguration.cs` | **Deleted** | ServicePlatform/ServicePulse monitoring toggle — out of scope (founding spec); Wolverine's native OTel replaces it. |
| `Settings\Security\ClientCredentialsConfiguration.cs`, `OAuthConfiguration.cs` | **→ 9** | Client-credentials auth (Duende ATM). |
| `Settings\Security\SecurityConfiguration.cs`, `AuthenticationConfiguration.cs`, `OpenIdConnectConfiguration.cs` | **→ 10** | OIDC login. `SecurityConfiguration.EnableHttps`/`AllowedOrigins` are hosting concerns read by Common/BlazorServer — the 10-vs-4/5 split of those two properties is decided by those specs, not here. |
| `Settings\Security\JwtBearerConfiguration.cs` | **→ 5** | `AddCloudstrapJwtBearer` lives in `Cloudstrap.WebApi` (founding spec). |
| `Settings\Scalar\ScalarConfiguration.cs`, `ScalarOAuthConfiguration.cs` | **→ 5** | OpenAPI UI config; the `Scalar.OAuth.Validate(isPrd)` cross-check moves with it. |
| `Settings\Swagger\SwaggerConfiguration.cs`, `SwaggerOAuthConfiguration.cs` | **→ 5** | Already `[Obsolete]` in the source in favor of Scalar — recommend the WebApi spec drops rather than ports them (its call). |

---

## Public API Sketch

All types live in the single namespace **`Cloudstrap.Core`** (the package is ~14 small types; per-feature sub-namespaces with one type each would be noise). Everything is `public sealed` unless noted; options classes have mutable auto-properties (binder requirement) and parameterless constructors; no other type is unsealed or inheritable. Naming is final per the 2026-07-25 gate: `CloudstrapOptions` root + `GetCloudstrapOptions()` accessor.

```text
Cloudstrap.Core
├── CloudstrapOptions                         — root model, section "Cloudstrap"
│     Application        : ApplicationOptions
│     Logging            : LoggingOptions
│     OpenTelemetry      : OpenTelemetryOptions
│     Correlation        : CorrelationOptions
│     HealthChecks       : HealthChecksOptions
│     HttpClients        : Dictionary<string, HttpClientServiceOptions>
│     const SectionName  = "Cloudstrap"
│
├── ApplicationOptions                        — section "Cloudstrap:Application"
│     SystemName         : string   [Required]
│     SubsystemName      : string   [Required]
│     SubsystemType      : string   [Required]
│     WorkloadName       : string   — computed "{system}-{subsystem}-{type}" (lowercase);
│                                     explicit config/set value overrides the computation
│     EnvironmentTier    : string?  — optional, free-form (orgs with more tiers than
│                                     Development/Staging/Production); no behavior in Core
│     PathBase           : string   — normalized: "/x" form or ""
│     ExceptionHandlerPath : string = "/error"
│     const SectionName  = "Cloudstrap:Application"
│
├── LoggingOptions                            — section "Cloudstrap:Logging"
│     Level              : LogLevel = Information        (Microsoft.Extensions.Logging)
│     LevelOverrides     : Dictionary<string, LogLevel>  (source-context prefix → level)
│     EnrichProperties   : Dictionary<string, string>
│     Console            : ConsoleLoggingOptions   { Enabled = true }
│     File               : FileLoggingOptions      { Enabled = false, Path : string? — required when Enabled }
│     const SectionName  = "Cloudstrap:Logging"
│
├── OpenTelemetryOptions                      — section "Cloudstrap:OpenTelemetry"
│     Mode               : OpenTelemetryMode = Disabled
│     Endpoint           : Uri?    — required + http/https when Mode = Otlp
│     Headers            : Dictionary<string, string>    (OTLP headers; no token helper)
│     EnableTracing / EnableMetrics / EnableLogs         = true
│     EnableConsole      = true    (console writer alongside Otlp/AzureMonitor)
│     EnableRuntimeMetrics / EnableHttpClientMetrics / EnableAspNetCoreMetrics = true
│     EnableMessagingMetrics = true
│     EnableSqlClientInstrumentation = false
│     EnableBlazorHubTracing = false
│     AlwaysOnSampler    = false
│     IsActive           : bool (computed — Mode != Disabled)
│     IsConsoleEnabled   : bool (computed — Console mode, or EnableConsole && active)
│     const SectionName  = "Cloudstrap:OpenTelemetry"
│
├── OpenTelemetryMode (enum)                  Disabled = 0, Console = 1, Otlp = 2, AzureMonitor = 3
│
├── CorrelationOptions                        — section "Cloudstrap:Correlation"
│     HeaderName         : string = "X-Correlation-ID"
│     Request            : CorrelationRequestOptions
│                            { RequireForAllEndpoints = false,
│                              HealthEndpoints = ["/healthz", "/ready"],
│                              ExcludeEndpoints = [] }
│     Message            : CorrelationMessageOptions
│                            { RequireForAllMessageHandlers = false,
│                              ExcludeMessageHandlers = [] }
│     const SectionName  = "Cloudstrap:Correlation"
│
├── HealthChecksOptions                       — section "Cloudstrap:HealthChecks"
│     Enabled            = true
│     LivenessPath       : string = "/healthz"
│     ReadinessPath      : string = "/ready"
│     const SectionName  = "Cloudstrap:HealthChecks"
│
├── HttpClientServiceOptions                  — section "Cloudstrap:HttpClients:{name}"
│     BaseAddress        : Uri?    [required, absolute — validated]
│     Timeout            : TimeSpan = 30 s
│     AddUserAccessToken / AddClientAccessToken = false   (seams for deliverables 9/10)
│     EnableHealthCheck  = false;  HealthCheckPrefix : string?
│     TokenRequestParameters : TokenRequestOptions?
│
├── TokenRequestOptions
│     Scope / Resource / SignInScheme / ChallengeScheme : string?
│     ForceRenewal       : bool = false
│
├── ConfigurationValidationException : Exception (sealed)
│     Failures           : IReadOnlyList<string>
│     ctors: (message), (message, innerException), (message, failures)
│
├── ServiceCollectionExtensions (static)
│     AddCloudstrapCore(this IServiceCollection) : IServiceCollection
│       — binds CloudstrapOptions to "Cloudstrap" and each per-section options type to its
│         own section path; wires framework validation (DataAnnotations + IValidateOptions
│         for conditional rules) with ValidateOnStart on every registration
│
└── ConfigurationExtensions (static)
      GetCloudstrapOptions(this IConfiguration) : CloudstrapOptions
        — eager pre-host bind: throws ConfigurationValidationException when the
          "Cloudstrap" section is absent or any validation rule fails
```

Validation mechanics (behavioral requirement; exact mechanism is the planner's choice): DataAnnotations for simple required/range rules; conditional and cross-property rules (`Otlp` ⇒ `Endpoint`, `File.Enabled` ⇒ `Path`, `BaseAddress` absolute) via `IValidateOptions<T>` — the framework's source-generated `[OptionsValidator]` with `[ValidateObjectMembers]` is the preferred implementation because it removes the hand-rolled nested-object cascade (finding 3) and is reflection-free. Both the DI path and the eager `GetCloudstrapOptions` path must apply the *same* rules (single validator implementation, two entry points).

---

## Behaviors & Conventions

| Behavior | Default | Override |
|---|---|---|
| Configuration section root | `Cloudstrap:` (one subsection per feature) | None — the section name is the product convention (founding spec drops `Nihdi:` compatibility). |
| Workload naming | `WorkloadName` computed as lowercase `{SystemName}-{SubsystemName}-{SubsystemType}` | Set `Cloudstrap:Application:WorkloadName` explicitly (or assign in code); the explicit value wins verbatim. |
| Environment handling | Standard ASP.NET Core environments (`IHostEnvironment`) drive behavior in later packages; Core stores no environment name | `Cloudstrap:Application:EnvironmentTier` — optional free-form tier label for orgs with more than three tiers; consuming packages may key explicit flags off it, Core attaches no behavior. |
| Startup validation | `AddCloudstrapCore` applies `ValidateOnStart` to every options registration — invalid config fails host startup with `OptionsValidationException` | Consumers wanting lazy validation register options themselves (Core's options types are plain POCOs; `AddCloudstrapCore` is convenience, not a requirement). |
| Pre-host validation | `GetCloudstrapOptions()` always validates and throws `ConfigurationValidationException` | Consumers wanting a tolerant read bind manually: `configuration.GetSection(CloudstrapOptions.SectionName).Get<CloudstrapOptions>()`. |
| Code-level option overrides | Config file values | Standard options layering: `services.Configure<ApplicationOptions>(...)` / `PostConfigure` after `AddCloudstrapCore` — Core deliberately adds no bespoke callback parameter (per-section framework idiom covers it; documented in the package README). |
| Probe paths | `/healthz` (liveness), `/ready` (readiness) in `HealthChecksOptions` | `Cloudstrap:HealthChecks:LivenessPath` / `ReadinessPath`; consuming packages (2/4/5/7/12) must read these rather than hard-coding paths. |
| Correlation header | `X-Correlation-ID` | `Cloudstrap:Correlation:HeaderName`; deliverable 2 must read it (W3C `traceparent` remains the tracing backbone regardless). |
| Connection strings | Standard `ConnectionStrings:` section + `IConfiguration.GetConnectionString` (platform convention, Aspire-friendly) | n/a — Core no longer mirrors them into the model. |
| Environment detection | None — Core ships no runtime host/environment sniffing (amendment 2026-07-25); packages that vary behavior by host expose explicit options instead | n/a |
| Aspire coexistence | Core is options-only: no OTel pipeline, no exporters, no HTTP handlers, no health-check registrations — nothing to collide with ServiceDefaults. AC-ASP2 (zero `Aspire.*`) applies; `OpenTelemetryMode`/probe-path/correlation settings only *model* config — owner-vs-contribute composition is implemented by deliverable 2 (AC-ASP1) and 4 (AC-ASP3). | n/a |

**Test strategy (spec-level):** NUnit 4 on Microsoft.Testing.Platform in `src/Test/UnitTest/Cloudstrap.Core.Tests` (first real test project — absorbs/retires the deliverable-0 `Cloudstrap.Scaffolding.Tests` placeholder per that spec). Tests bind from in-memory `IConfiguration` (`AddInMemoryCollection`) — no files, no network, no live services. Coverage targets: binding of every section, every validation rule (pass + fail + message content), `WorkloadName` computation/override, `PathBase` normalization, defaults (AC-C10), eager-path exception behavior, DI resolution of all options types (AC-C1/AC-C2 via a minimal `ServiceCollection`). Neutral fixture values only (`contoso`, `example.com`).

---

## Dependencies

All Microsoft-maintained, MIT-licensed, versioned via CPM in `src/Directory.Packages.props`. This is the shared substrate itself — no third-party packages, no abstractions needed around them (they *are* the abstraction layer).

| Package | License | Justification |
|---|---|---|
| `Microsoft.Extensions.Options` | MIT | `IOptions<T>`, `ValidateOnStart`, `[OptionsValidator]` source generator. |
| `Microsoft.Extensions.Options.ConfigurationExtensions` | MIT | `BindConfiguration` for the DI registration path. |
| `Microsoft.Extensions.Options.DataAnnotations` | MIT | DataAnnotations-based validation of simple rules. |
| `Microsoft.Extensions.Configuration.Binder` | MIT | Eager `Get<T>`/`Bind` for `GetCloudstrapOptions`. |
| `Microsoft.Extensions.Configuration.Abstractions` | MIT | `IConfiguration` in the public API surface. |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | MIT | `IServiceCollection` extension entry point. |
| `Microsoft.Extensions.Logging.Abstractions` | MIT | The `LogLevel` enum used by `LoggingOptions` (platform vocabulary; every later deliverable references this package anyway). |

Explicitly **not** referenced: `Microsoft.AspNetCore.App` framework reference (source had it; nothing in Core needs ASP.NET Core — dropping it keeps Core loadable from workers, console apps, and Blazor WASM), `LanguageExt.Core` (dead reference in the source — finding 1), `Nihdi.StyleCop.MsBuildProperties`/StyleCop (deliverable-0 decision), any `Aspire.*` (AC-ASP2).

---

## Deliberate Behavior Changes (vs. the source library)

1. **No bespoke configuration bootstrap** — `BootstrapConfiguration`/`ConfigurationBuilderExtensions` dropped; standard host builders own configuration layering. `app.xml`, `appsettings.{MachineName}.json`, and the `Nihdi__Application__Environment` variable are gone (De-NIHDI).
2. **Framework options validation replaces the hand-rolled cascade** — DataAnnotations + `IValidateOptions<T>` + `ValidateOnStart`; DI-path failures throw the framework's `OptionsValidationException` at startup, the eager path throws `ConfigurationValidationException`. The old model validated only when someone remembered to call `.Validate()`.
3. **Environment taxonomy removed** — no `Environment` property, no LOC/DEV/TST/VAL/PRD constants, no `EnvironmentIsDevTest/IsLocal/IsPrd()`; standard ASP.NET Core environments + optional `EnvironmentTier` (founding-spec decision). Later packages key behavior off `IHostEnvironment` or explicit flags.
4. **ConnectionStrings de-duplicated** — the root model no longer carries a `ConnectionStrings` dictionary or `GetConnectionString` helpers; consumers (including deliverables 14/16) use `IConfiguration.GetConnectionString` (platform/Aspire convention).
5. **Storage naming conventions deleted from Application settings** — `StorageName`/`BlobContainerName`/`BlobContainerUri` (and `ConfidentialityLevel`) replaced in deliverable 4 by explicit `Cloudstrap:Storage:*` settings.
6. **Renames and type upgrades**: `BusinessSystemName` → `SystemName`; `LevelOverwrites` → `LevelOverrides`; `Level` string → `LogLevel` enum with default `Debug` → `Information`; `BaseUri` string → `Endpoint` `Uri?`; OTLP `AccessToken` → `Headers` dictionary; `AddSqlClientSupport` → `EnableSqlClientInstrumentation`; `EnableNServiceBusMetrics` → `EnableMessagingMetrics`; `TimeoutInSeconds` int → `Timeout` `TimeSpan`; section `HttpClientServiceRegistry` → `HttpClients`.
7. **New required validation** on `HttpClientServiceOptions.BaseAddress` (absent in source — failures surfaced only at runtime in the typed client).
8. **Correlation defaults modernized** — header `NIHDI.Correlation` (constant) → `X-Correlation-ID` (configurable); health-endpoint exclusions `["/live","/ready","/health"]` → `["/healthz","/ready"]`; the "at least one health endpoint" validation rule removed.
9. **File logging default path removed** — `D:\logsint` gone; `Path` required when file logging is enabled.
10. **`OpenTelemetryMode` gains `AzureMonitor`** — reserved in Core, implemented by deliverable 3.
11. **Environment sniffing removed** *(amendment 2026-07-25)* — `IsRunningInAks()` is dropped with no replacement: the supported hosting matrix is Azure Web Apps + containers/Kubernetes (no on-prem IIS/VM), so the cloud-vs-on-prem discriminator it encoded has no meaning, and it mis-classifies Web Apps (no `KUBERNETES_SERVICE_HOST`). Later packages use explicit options wherever the old code branched on the environment.

---

## Out of Scope (this deliverable — the planner must not resurrect any of it)

- Everything in the founding spec's global Out of Scope: message encryption, MessagingBridge, Dynatrace, ServicePlatform/ServicePulse, `Cloudstrap.Functional`.
- All **Dropped** rows above: `BootstrapConfiguration`, `ConfigurationBuilderExtensions`, `GetConnectionStrings`, `ConfidentialityLevel`, `EnvironmentConstants`, `DynatraceConfiguration`, `AppRegistrationConfiguration` (gate decision — deliverable 14 must not resurrect a credentials POCO either), `GlobalSuppressions`, storage-naming members, environment-predicate helpers, and `IsRunningInAks` (amendment 2026-07-25 — no `CloudstrapEnvironment` helper ships; see Decision Log).
- All **Move →** rows: NServiceBus/Bridge/Monitoring, Hangfire, Security (+ `AuthenticationFlow`), Scalar/Swagger, Dashboard settings — they ship (or die) with deliverables 14/16/9/10/5/19 respectively; Core must compile and pass AC-C7 with zero references to any of them.
- Any behavior: no Serilog/OTel wiring (deliverable 2), no KeyVault/Blob/typed-HttpClient registration (4), no middleware, no health-check registration, no environment detection — Core is settings + validation + its two entry points (`AddCloudstrapCore`, `GetCloudstrapOptions`) only.
- A LanguageExt.Core dependency (finding 1) — the founding spec's type-mapping decision moves to the first genuinely consuming package.
- Aspire packages or Aspire-specific code paths (AC-ASP2; `Cloudstrap.Aspire` is post-v1).

---

## Decision Log (gate answers, 2026-07-25 — zero Open Questions remain; spec is planner-ready)

| Question | Answer (user, 2026-07-25) |
|---|---|
| OQ-1 — Root settings type name: `CloudstrapOptions` vs the founding spec's `CloudstrapConfiguration` | **`CloudstrapOptions`** with a **`GetCloudstrapOptions()`** accessor (this spec's recommendation) — consistent with the repo's `<Feature>Options` naming law and the `IOptions<T>` consumption pattern. The user also approved the corresponding founding-spec amendment: in `_specs/Cloudstrap.md`, Package Map row for `Cloudstrap.Core`, "Settings model → `CloudstrapConfiguration`" reads "Settings model → `CloudstrapOptions`". That file is amended by the user, not by this spec (founding-spec edits are user-owned). |
| OQ-2 — `AppRegistrationConfiguration`: drop outright, or move its shape to deliverable 14 | **Drop outright** (this spec's recommendation). No Cloudstrap type models client-id/secret credentials in appsettings; deliverable 14 (Messaging) builds transport auth on `TokenCredential`/`DefaultAzureCredential` + the standard `AZURE_TENANT_ID`/`AZURE_CLIENT_ID`/`AZURE_CLIENT_SECRET` environment variables (secret-based auth, if ever needed, is exposed through the Azure SDK's own `ClientSecretCredential` wiring, not a Cloudstrap settings type). |
| Amendment (post-approval, 2026-07-25) — `IsRunningInAks()` verdict | **Drop** (was Redesign → `CloudstrapEnvironment.IsRunningInKubernetes()`). User decision: the supported hosting matrix is **Azure Web Apps + containers/Kubernetes** (cloud-native) — on-prem IIS/VM hosting is out of scope. The source helper encoded a cloud-AKS-vs-on-prem discriminator that no longer exists, and it mis-classifies Azure Web Apps (no `KUBERNETES_SERVICE_HOST`) as the legacy branch. All source call-site needs dissolve under the new design (`DefaultAzureCredential` for credentials; explicit options for console format/DataProtection/forwarded headers); keeping it would ship un-overridable environment sniffing as public API. A later deliverable needing a K8s-keyed default adds an internal check at that point. Recorded as founding-spec Non-Goal + Decisions Made row (user-approved). |
