# Plan: 4-ConfigKeyVaultHttpExtensions — A consumer bootstraps KeyVault config, blob DataProtection, a conventional blob client, typed HTTP clients and standard health probes, each with one call and one `Cloudstrap:` subsection

## Overview

Deliverable #4 of the extraction roadmap: the `Cloudstrap.Extensions` package plus the committed additive amendment to the shipped `Cloudstrap.Core`. **Binding spec: `_specs/4-ConfigKeyVaultHttpExtensions.md`** (approved 2026-08-02, zero Open Questions; its Port Decision Table — 1 Port · 7 Redesign · 2 Replace · 6 Move-out · 16 Drop — Public API Sketch, Behaviors & Conventions table, Decision Log OQ-1/OQ-2/OQ-3 and Out of Scope list are authoritative. Nothing marked Move-out/Drop appears in this plan: no `IApplicationBuilder` extension of any kind, no middleware/TLS/XFF/path-base/HSTS/CORS, no `/probe.aspx`, no `HostRunner`, no classic-`IHostBuilder` overloads, no Scalar/OpenApi, no NWebsec, no auth package, no token-handler implementation.) Reference patterns, all read in full before planning:

- **Repo pattern (deliverables 1–3, verified on disk)**: `src/Cloudstrap.Observability.AzureMonitor/` csproj shape (Sdk + `TargetFramework` + two packaging properties + metadata block, version-less `PackageReference`s against `src/Directory.Packages.props`), `src/Cloudstrap.Observability/` for the `FrameworkReference Microsoft.AspNetCore.App` precedent, sealed public types with full XML docs + `internal` implementations, NUnit-4-on-MTP test projects under `src/Test/UnitTest/`, host-level fixtures (`HostApplicationBuilder`/`WebApplication.CreateBuilder` + in-memory `Cloudstrap` config dictionary), the `PackageSurfaceTests` guard idiom, neutral fixtures (`Contoso`/`Catalog`/`Orders`).
- **Shipped seams this package consumes (read in the shipped code)**: Core's `CloudstrapOptions.HttpClients` registry + `HttpClientServiceOptions` (`BaseAddress`, `Timeout`, `AddUserAccessToken`, `AddClientAccessToken`, `EnableHealthCheck`, `HealthCheckPrefix`, `TokenRequestParameters`) and `TokenRequestOptions` — consumed, never redefined; Core's `HealthChecksOptions` (`Enabled`, `LivenessPath=/healthz`, `ReadinessPath=/ready`) + `ApplicationOptions` (`WorkloadName`, `SystemName`); Core's hand-written conditional-validator precedent (`OpenTelemetryOptionsValidator` with ctor-injected `IConfiguration`) and `ConfigurationValidationException` eager path (`GetCloudstrapOptions`); Observability's `AddCloudstrapCorrelationHandler` (idempotent per client — its XML doc names this package as the intended caller) and `CloudstrapHealthCheckTags` (`"live"`/`"ready"`).
- **Source material (read in the read-only reference repo)**: `PrefixKeyVaultSecretManager.cs` (the one Port item — culture-sensitive `StartsWith` fixed to `Ordinal`, prefix made configurable), `AddAzureKeyvaultForNihdi.cs` (credential exclusion list and vault-name composition — both replaced), `HttpClient\ServiceCollectionExtensions.cs` (the typed-client crown jewel: kept behaviors vs the dropped `UseProxy=false`, static dedupe dictionary, compile-time ATM dependency), `BlobStorage\ServiceCollectionExtensions.cs` (environment-sniffed credential switching — replaced), `Services\ServiceCollectionExtensions.ProtectKeysWithAzureKeyVaultDefaultCredentials` (AKS-sniff + env-vars + silent skip — replaced), `Extensions\ApplicationBuilderExtensions.UseNihdiHealthChecksInternal` (the `/live`+`/ready` tag-filtered mapping that becomes public `MapCloudstrapHealthChecks`; its `/health`-on-port-9000, `LOC` switch and bespoke response writer are dropped).
- **Demonstration harness (deliverable 25, verified on disk)**: `src/Test/WasmTestProject/` — Bff `Program.cs` currently hand-maps the two probe endpoints (the exact lines Step 10 replaces) and registers no outbound client; `E2eFixture` (Bff on `http://127.0.0.1:5300`, `CapturedSutOutput`), `SutProcess.Start(baseUrl, applicationArguments)` for short-lived startup-scenario instances, `PageTestBase` for browser tests, existing `HealthAndCorrelationTests` pin `/healthz` + `/ready` + correlation behavior.

This is a library deliverable with no controllers and no database: the template's endpoint-integration block does not apply. The integration layer is host-level unit fixtures (including real HTTP through `Microsoft.AspNetCore.TestHost` for the probe endpoints) plus the mandatory E2E demonstration slice (Step 10). **No live Azure anywhere in tests** (spec test strategy): KeyVault coverage asserts manager/wiring/validation against local fixtures, blob/DataProtection coverage asserts DI registrations and options plumbing offline, and the live-vault walk is the README's manual verification procedure (AC-E5, AC-O1-style), reviewed at the final gate.

**AC coverage map** (from `_specs/4-ConfigKeyVaultHttpExtensions.md`):
AC-E6 + AC-E7 + AC-ASP3 (+ default-name and hooks-run-last edge cases) → Step 1 · AC-E8 + AC-E9 (+ both-flags-forwarded edge) → Step 2 · AC-E11 (+ no-checks-registered and disabled edge cases) → Step 3 · AC-E10 + the Core `HealthCheckPath` amendment (Decision Log OQ-2) → Step 4 · AC-E1 (prefix/mapping mechanics) + AC-E4 (prefix rules) → Step 5 · AC-E2 + AC-E3 + AC-E4 (credential rule) + De-NIHDI row "KeyVault name" → Step 6 (AC-E1's against-a-real-vault half is the Step 9 manual procedure, per the spec's test strategy) · AC-E13 + De-NIHDI row "Storage account" → Step 7 · AC-E12 (Decision Log OQ-3) → Step 8 · AC-E5 + AC-E14 + AC-ASP2 (closure/identifier guards + README incl. the manual KeyVault procedure and the one-owner Aspire rule) → Step 9 · AC-E15 → Step 10 · full walk at the final gate.

**New CPM entries** (`src/Directory.Packages.props`; the executor verifies latest stable on nuget.org at pin time except where the spec already pinned a version):

| Package | Version | License | Step |
|---|---|---|---|
| *(test only)* `Microsoft.Extensions.Http.Resilience` | latest stable | MIT | 1 |
| *(test only)* `Microsoft.AspNetCore.TestHost` | 10.0.x (match the `Microsoft.Extensions.*` 10.0.10 family) | MIT | 3 |
| `AspNetCore.HealthChecks.Uris` | **9.0.0** (spec-verified 2026-08-02, Xabaril) | Apache-2.0 | 4 |
| `Azure.Extensions.AspNetCore.Configuration.Secrets` | latest stable (brings `Azure.Security.KeyVault.Secrets` transitively) | MIT | 5 |
| `Azure.Identity` | **1.21.0 — already pinned by #3**; this step only adds the `PackageReference` | MIT | 6 |
| `Azure.Storage.Blobs` | latest stable | MIT | 7 |
| `Azure.Extensions.AspNetCore.DataProtection.Blobs` / `.Keys` | latest stable | MIT | 8 |

⚠️ **Risk areas (spec header, reviewed at the covering gates):** **public API one-way door** — `AddCloudstrapHttpServiceClient<TI,TImpl>` and the `IAccessTokenHandlerProvider` seam are consumed by #5/#6/#7/#9/#12/#17 (Gate 1) · **auth-adjacent seam design** — token-handler attachment (Decision Log OQ-1; Gate 1) · **shared-contract amendment** to shipped `Cloudstrap.Core` — `HttpClientServiceOptions.HealthCheckPath` (Decision Log OQ-2; Gate 2) · **new external dependencies** — five `Azure.*` packages (Gate 3) and the suite's **first non-Microsoft-non-project runtime dependency** `AspNetCore.HealthChecks.Uris`, Apache-2.0 (Gate 2) · the **second `Microsoft.AspNetCore.App` framework reference** in the suite (spec API-sketch decision; Gate 1, where the csproj is created).

**Planner mechanics decided here (flagged for gate review, no spec conflict):**
(a) **Validators are hand-written** `internal sealed IValidateOptions<T>` classes (Core's `OpenTelemetryOptionsValidator` precedent) because every rule in this package is conditional (`Enabled`-gated, either/or) — attribute-only source-gen `[OptionsValidator]` cannot express them; no `Microsoft.Extensions.Options.DataAnnotations` (inherited fact #1 honored in spirit: reflection-free, no new dependency). Failure messages name full `Cloudstrap:` keys.
(b) **Per-client options** are named options: `services.AddOptions<HttpClientServiceOptions>(name).BindConfiguration($"Cloudstrap:HttpClients:{name}").ValidateOnStart()` plus a named `internal HttpClientServiceOptionsValidator` (ctor-injected `IConfiguration`) that fails when the section does not exist (AC-E7) or `BaseAddress` is missing/relative — message names `Cloudstrap:HttpClients:{name}`. **Interpretation note for the gate**: the spec's Behaviors row says options "validate per registered name at registration"; configuration is not readable at `IServiceCollection` time, so failure surfaces at host start (`ValidateOnStart`) or first client resolution, whichever comes first — still before any request is sent. Confirm or direct a change at Gate 1.
(c) **Token seam wiring** (Decision Log OQ-1 verbatim): the entry point registers, per flagged client, a named `HttpClientFactoryOptions` configuration whose `HttpMessageHandlerBuilderActions` delegate resolves `IAccessTokenHandlerProvider` from `HttpMessageHandlerBuilder.Services` **lazily at client-build time** — no Program.cs ordering constraint between #4 and #9 calls. Provider absent while a flag demands it → `InvalidOperationException` naming the flag key and the filling packages (`Cloudstrap.Authentication.OpenIdConnect` for user tokens, `Cloudstrap.Authentication.ClientCredentials` for client tokens). Handler order: token handler(s) before the correlation handler; consumer hooks last.
(d) **Dependency health check** (AC-E10): an internal `IConfigureOptions<HealthCheckServiceOptions>` per registered client (DI-time, so it can read the bound named options) conditionally adds a `HealthCheckRegistration` named `{HealthCheckPrefix ?? name}-liveness`, tagged `CloudstrapHealthCheckTags.Readiness`, wrapping the `AspNetCore.HealthChecks.Uris` URI check against `BaseAddress` + `HealthCheckPath`, skipping when a registration with that name already exists — idempotence per service collection, zero static state. The probe uses a named `HttpClient` `"{checkName}"` (source parity), which doubles as the every-convention-has-an-override seam and lets tests swap in a `TestServer` handler. The executor confirms the exact Uris-package types (`UriHealthCheck`/`UriHealthCheckOptions`) in RED and reports any API deviation at Gate 2.
(e) **`MapCloudstrapHealthChecks`** reads `Cloudstrap:HealthChecks` itself from the app's `IConfiguration` (`GetSection(...).Get<HealthChecksOptions>() ?? new()`), so it works with or without `AddCloudstrapCore`; idempotence by inspecting `endpoints.DataSources` for an already-mapped route matching the configured paths — no statics, no cross-service-collection leakage.
(f) **KeyVault eager path**: `AddCloudstrapKeyVault` binds `Cloudstrap:KeyVault` eagerly from `builder.Configuration` (config-build time, before DI — spec Validation row) and throws Core's `ConfigurationValidationException` naming `Cloudstrap:KeyVault:VaultUri` when enabled-but-unset (AC-E2). The enabled arm composes credential/manager/reload-interval in an internal seam (`KeyVaultRegistration`) that unit tests exercise directly; the actual `AddAzureKeyVault` source addition is **not** unit-testable because `ConfigurationManager` builds providers eagerly (it would contact the vault) — exactly the fail-fast the spec wants in production, covered by the Step 9 manual procedure and gate code review. Idempotence via an `IHostApplicationBuilder.Properties` marker key.
(g) **Test-only dependencies**: `Microsoft.Extensions.Http.Resilience` (AC-ASP3 proof, spec-sanctioned) and `Microsoft.AspNetCore.TestHost` (real-HTTP probe-endpoint tests — not in the spec's dependency table; test project only, never shipped). Review at Gates 1 and 2 respectively.
(h) **First `InternalsVisibleTo` in the repo**: `Cloudstrap.Extensions` grants IVT **only** to `Cloudstrap.Extensions.Tests` (spec Port Decision Table row for `AssemblyVisibility.cs` sanctions exactly this) so the ported `PrefixKeyVaultSecretManager` and the internal seams are directly testable. No cross-package IVT anywhere.
(i) **Name collision, accepted**: `Cloudstrap.Extensions.DataProtectionOptions` shares its simple name with `Microsoft.AspNetCore.DataProtection.DataProtectionOptions`. The spec mandates the name; namespaces differ; the README notes the qualification a consumer needs when both are in scope.

This package owns three new configuration sections — `Cloudstrap:KeyVault`, `Cloudstrap:Storage`, `Cloudstrap:DataProtection` — and consumes Core's shipped `Cloudstrap:HttpClients:{name}`, `Cloudstrap:HealthChecks` and `Cloudstrap:Application` without redefining them. Single namespace `Cloudstrap.Extensions`. Nothing in this package takes an `IApplicationBuilder`.

---

## Slice 1 — Config-driven typed HTTP clients: one call registers an injectable, correlated, token-ready client that never stacks resilience

---

## Step 1 — A consumer registers `AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog")` and gets an injectable client with config-driven base address/timeout, the correlation handler exactly once, and no Cloudstrap resilience (AC-E6, AC-E7, AC-ASP3)

- [ ] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Extensions/Cloudstrap.Extensions.csproj` *(create)* — Sdk project, `TargetFramework=net10.0`, `GeneratePackageOnBuild=true`, `GenerateDocumentationFile=true`; `<FrameworkReference Include="Microsoft.AspNetCore.App" />` (spec framework-reference decision); `<ProjectReference>` to `..\Cloudstrap.Core\Cloudstrap.Core.csproj` and `..\Cloudstrap.Observability\Cloudstrap.Observability.csproj`; `<InternalsVisibleTo Include="Cloudstrap.Extensions.Tests" />` (planner mechanic (h)). Description/tags/README metadata land in Step 9.
- `src/Cloudstrap.Extensions/ServiceCollectionExtensions.cs` *(create)* — `public static`, the spec Public API Sketch signature verbatim: `AddCloudstrapHttpServiceClient<TInterface, TImplementation>(this IServiceCollection services, string? name = null, Action<HttpClient>? configureClient = null, Action<IHttpClientBuilder>? configureBuilder = null) : IHttpClientBuilder`.
- `src/Cloudstrap.Extensions/HttpClientServiceOptionsValidator.cs` *(create)* — `internal sealed : IValidateOptions<HttpClientServiceOptions>`, planner mechanics (a)+(b): named validation, section-existence via ctor-injected `IConfiguration`, `BaseAddress` absolute-URI rule (message parity with Core's root-validator rule).
- `src/Test/UnitTest/Cloudstrap.Extensions.Tests/Cloudstrap.Extensions.Tests.csproj` *(create)* — mirror of `Cloudstrap.Observability.Tests.csproj`: Sdk + `TargetFramework=net10.0` + `<ProjectReference>` to the new package + version-less `<PackageReference>`s `Microsoft.Extensions.Configuration`, `Microsoft.Extensions.Configuration.Binder`, `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.Http.Resilience` (test-only, AC-ASP3).
- `src/Test/UnitTest/Cloudstrap.Extensions.Tests/AddCloudstrapHttpServiceClientTests.cs` *(create)*
- `src/Test/UnitTest/Cloudstrap.Extensions.Tests/HttpServiceClientResilienceTests.cs` *(create)*
- `src/Cloudstrap.sln` *(modify)* — package at the solution root, test project under the `Test\UnitTest` solution folder (same nesting as the existing test projects).
- `src/Directory.Packages.props` *(modify)* — pin `Microsoft.Extensions.Http.Resilience` (test-only).

**RED** *(write these tests first; for a brand-new project the failure is the test project failing to compile against missing types — the standard RED for new code. Fixture idiom: `HostApplicationBuilder` + in-memory config `Cloudstrap:HttpClients:Catalog:BaseAddress=https://catalog.contoso.example/`, `…:Timeout=00:00:05`; the typed client test double `CatalogClient` captures its injected `HttpClient`; request-level assertions send through a stub primary handler installed with `ConfigurePrimaryHttpMessageHandler` and capture the outgoing `HttpRequestMessage`)*:
- Unit test file: `AddCloudstrapHttpServiceClientTests.cs`
  - `AddCloudstrapHttpServiceClient_WithConfiguredSection_ResolvesTypedClientWithBaseAddressAndTimeout` — `GetRequiredService<ICatalogClient>()` works; the captured `HttpClient` has `BaseAddress == https://catalog.contoso.example/` and `Timeout == 5s` (AC-E6 registration half).
  - `AddCloudstrapHttpServiceClient_WithoutName_DefaultsToInterfaceNameMinusLeadingI` — section `Cloudstrap:HttpClients:CatalogClient` + `AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>()` → same behavior (spec naming convention row).
  - `AddCloudstrapHttpServiceClient_SendsCorrelationHeaderExactlyOnce` — a request sent through the typed client carries exactly one `X-Correlation-ID` header value (handler attached, AC-E6 correlation half).
  - `AddCloudstrapHttpServiceClient_WithDefaultsLevelCorrelationRegistration_DoesNotStackASecondHandler` — `ConfigureHttpClientDefaults(b => b.AddCloudstrapCorrelationHandler())` first, then the typed registration → still exactly one header value (the idempotence clause of AC-E6).
  - `AddCloudstrapHttpServiceClient_MissingSection_FailsStartupNamingTheSection` — no `Cloudstrap:HttpClients:Catalog` section → host `StartAsync` (ValidateOnStart) throws `OptionsValidationException` whose message contains `Cloudstrap:HttpClients:Catalog` (AC-E7, planner mechanic (b)).
  - `AddCloudstrapHttpServiceClient_RelativeBaseAddress_FailsNamingTheKey` — `BaseAddress=catalog` → failure naming `Cloudstrap:HttpClients:Catalog:BaseAddress` (spec edge case).
  - `AddCloudstrapHttpServiceClient_ConsumerHooks_RunAfterCloudstrapWiring` — `configureClient` overrides `Timeout`; the captured client carries the hook's value (spec pipeline row: hooks have final say).
  - `AddCloudstrapHttpServiceClient_CalledTwiceForTheSameName_StillResolvesAndSendsOneCorrelationHeader` — standard accumulate semantics, no duplicate handler (spec edge case).
  - `AddCloudstrapHttpServiceClient_OnNullServices_ThrowsArgumentNullException` (guard clause).
- Unit test file: `HttpServiceClientResilienceTests.cs`
  - `AddCloudstrapHttpServiceClient_WithDefaultsLevelStandardResilience_ClientWorksWithSingleResilienceLayer` — `ConfigureHttpClientDefaults(b => b.AddStandardResilienceHandler())` + the typed registration: a request through the typed client succeeds against the stub primary handler, and the materialized handler chain (captured via a test `IHttpMessageHandlerBuilderFilter` registered by the fixture) contains resilience handlers from exactly one `AddStandardResilienceHandler` application and **zero** resilience handlers contributed by Cloudstrap (AC-ASP3 verbatim; `Microsoft.Extensions.Http.Resilience` is test-only).
  - `AddCloudstrapHttpServiceClient_Alone_AddsNoResilienceHandler` — without the defaults call, the chain contains no handler from the resilience namespace (the "never adds resilience" behavior row).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln   # RED = test project fails to compile against missing types
  src\Test\UnitTest\Cloudstrap.Extensions.Tests\bin\Debug\net10.0\Cloudstrap.Extensions.Tests.exe --filter "AddCloudstrapHttpServiceClientTests"
  ```

**GREEN**:
- `ServiceCollectionExtensions.AddCloudstrapHttpServiceClient<TInterface, TImplementation>` — guards; resolve `name ??= typeof(TInterface).Name` minus the leading `I` (only when the second character is uppercase, standard convention); register named options + validator per planner mechanic (b) (validator instance via factory so it receives the container's `IConfiguration`, mirroring Core's `CloudstrapOptionsValidator` registration); `IHttpClientBuilder builder = services.AddHttpClient<TInterface, TImplementation>(name, ...)` applying `BaseAddress`/`Timeout` from the bound named options (via `IOptionsMonitor<HttpClientServiceOptions>.Get(name)` inside the configure delegate — DI-time, config-driven) then invoking `configureClient` last; chain `builder.AddCloudstrapCorrelationHandler()`; invoke `configureBuilder` last and return the builder. **No** `ConfigurePrimaryHttpMessageHandler`, **no** proxy setting, **no** resilience — deliberate omissions per the spec's Deliberate Behavior Changes 6 and AC-ASP3. Token flags and `EnableHealthCheck` are read but not yet acted on (Steps 2 and 4) — XML docs note the staging.
- `HttpClientServiceOptionsValidator` — planner mechanic (b) rules; skips validation for options names it did not register (named-options hygiene: only names registered through the entry point are validated).
- Full XML docs on the public entry point (naming convention, config section, correlation, the no-resilience posture).

**DB changes**: none — this repository has no database.

**VERIFY** *(when all green, mark this step's `Done` checkbox and continue straight to the next step)*:
1. Test exe → all pass: a consumer can now register a config-driven typed client in one line and get correlation propagation exactly once, fail-fast on a missing/broken section, and coexistence with defaults-level resilience — behavior that did not exist before (AC-E6, AC-E7, AC-ASP3).
2. `dotnet build src/Cloudstrap.sln` → zero warnings/errors; full `runTests` green (Core, Observability, AzureMonitor, E2E suites untouched); `dotnet format src/Cloudstrap.sln --verify-no-changes` → exit 0.
3. `dotnet build src/Cloudstrap.sln -c Release` → a `Cloudstrap.Extensions.*.nupkg` appears under `src/Cloudstrap.Extensions/bin/Release/` (packable from day one; metadata completed in Step 9).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 2 — Token flags come alive through the `IAccessTokenHandlerProvider` seam: flagged clients get the provider's handler in their chain; a missing provider fails fast with an actionable error ⚠️ *(Risk Area: auth-adjacent + public API one-way door — AC-E8, AC-E9, Decision Log OQ-1)*

- [ ] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Extensions/IAccessTokenHandlerProvider.cs` *(create)* — the spec Public API Sketch verbatim: `public interface IAccessTokenHandlerProvider` with `CreateUserTokenHandler(string clientName, TokenRequestOptions? tokenRequest) : DelegatingHandler` and `CreateClientTokenHandler(string clientName, TokenRequestOptions? tokenRequest) : DelegatingHandler`. XML docs name the implementing packages (#9/#10) and the fail-fast contract.
- `src/Cloudstrap.Extensions/AccessTokenHandlerWiring.cs` *(create)* — `internal static`, planner mechanic (c).
- `src/Cloudstrap.Extensions/ServiceCollectionExtensions.cs` *(modify)* — the entry point registers the wiring for every client (the delegate itself no-ops when neither flag is set).
- `src/Test/UnitTest/Cloudstrap.Extensions.Tests/AccessTokenHandlerSeamTests.cs` *(create)*

**RED** *(write these tests first, run them, confirm they fail — the test double `RecordingTokenHandlerProvider : IAccessTokenHandlerProvider` records `(kind, clientName, tokenRequest)` per call and returns a `DelegatingHandler` that stamps a marker header, e.g. `X-Token-Kind: user`; requests observed through the stub primary handler as in Step 1)*:
- Unit test file: `AccessTokenHandlerSeamTests.cs`
  - `FlaggedClient_WithRegisteredProvider_HasUserTokenHandlerInChain` — `AddUserAccessToken=true` + provider registered → a request through the typed client carries `X-Token-Kind: user`; the provider received the client name and the bound `TokenRequestParameters` values (`Scope` from config observed on the recorded `TokenRequestOptions`) (AC-E9).
  - `FlaggedClient_WithClientFlag_HasClientTokenHandlerInChain` — same for `AddClientAccessToken=true` → `X-Token-Kind: client`.
  - `UnflaggedClient_WithRegisteredProvider_GetsNoTokenHandler` — a second registered client without flags sends no marker header — the provider is consulted for exactly the flagged clients (AC-E9's "exactly the flagged clients").
  - `BothFlagsTrue_ForwardsBothToTheProvider` — both handlers created, both invoked, order user-then-client recorded (spec edge case: forwarding verbatim; supporting the combination is the provider's contract).
  - `FlaggedClient_WithoutProvider_FailsFastNamingFlagAndPackage` — no provider registered, `AddUserAccessToken=true` → resolving/using the typed client throws `InvalidOperationException` whose message contains `Cloudstrap:HttpClients:Catalog:AddUserAccessToken`, `IAccessTokenHandlerProvider` and `Cloudstrap.Authentication` (AC-E8; never a silent unauthenticated request).
  - `FlaggedClient_ProviderRegisteredAfterClientRegistration_StillWorks` — provider added to the service collection *after* `AddCloudstrapHttpServiceClient` → handler present (the lazy-resolution point of Decision Log OQ-1: no registration-order constraint).
  - `TokenHandler_RunsBeforeCorrelationHandler` — the marker handler observes the correlation header absent on the way down / present on the wire (assert relative order via the captured request or handler-chain inspection) — the spec pipeline row's ordering.
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Extensions.Tests\bin\Debug\net10.0\Cloudstrap.Extensions.Tests.exe --filter "AccessTokenHandlerSeamTests"
  ```

**GREEN**:
- `AccessTokenHandlerWiring.Register(IServiceCollection services, string name)` — `services.Configure<HttpClientFactoryOptions>(name, options => options.HttpMessageHandlerBuilderActions.Add(builder => { ... }))` per planner mechanic (c): read the bound named `HttpClientServiceOptions`; when a flag is set, `builder.Services.GetService<IAccessTokenHandlerProvider>()` — null → throw the AC-E8 message; else insert the created handler(s) into `builder.AdditionalHandlers` **before** the correlation handler position (user then client when both). Registered so it runs in the factory's action order ahead of the correlation-handler configuration.
- This package ships the interface only — zero auth package references (spec: the seam is the entire coupling; #9/#10 implement it).
- XML docs on `IAccessTokenHandlerProvider` are the seam's contract for #9/#10 — write them as such (lazily resolved, per-client invocation, both-flags forwarding, fail-fast wording).

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: config flags now attach real token handlers through a provider that #9/#10 will supply, in the decided order, for exactly the flagged clients — and a flag without a provider is a loud, actionable startup/first-use error instead of a silent unauthenticated call (AC-E8, AC-E9).
2. `dotnet build src/Cloudstrap.sln` → zero warnings; full `runTests` green; `dotnet format src/Cloudstrap.sln --verify-no-changes` → exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 1: the typed-client entry point and the auth seam are frozen *(covers Steps 1–2)*

*Executor: STOP here. Present the results of all covered steps and WAIT for user approval — do not start the next step.*

⚠️ **Risk areas at this gate**: **public API one-way door** — `AddCloudstrapHttpServiceClient<TI,TImpl>` (signature, naming convention, hook ordering) and `IAccessTokenHandlerProvider` (the exact seam #9/#10/#5/#6/#7/#12/#17 build against) are permanent surface; review both against the spec's Public API Sketch verbatim **before** anything is built on them · **auth-adjacent** (Decision Log OQ-1) — the token-attachment path end to end, the fail-fast message wording, and confirmation that no credential material and no auth package appear anywhere · the **second `Microsoft.AspNetCore.App` framework reference** (csproj created in Step 1) · test-only `Microsoft.Extensions.Http.Resilience` pin (planner mechanic (g)) · planner mechanic (b)'s AC-E7 interpretation (fail at `ValidateOnStart`/first resolution rather than literally at the registration call) — confirm or direct a change.

- [ ] Behavioral verification: test exe output shows — config-driven registration incl. name defaulting, exactly-one correlation header incl. the defaults-level pairing, missing-section and relative-BaseAddress failures naming their keys, hooks-run-last, double-registration safety (Step 1); the single-resilience-layer AC-ASP3 proof and the no-resilience-alone proof (Step 1); provider-attached user/client/both handlers with recorded client name + `TokenRequestOptions`, unflagged-client isolation, missing-provider fail-fast naming flag + packages, order-independence, token-before-correlation ordering (Step 2).
- [ ] Code review: entry-point + seam signatures vs the spec sketch, verbatim; planner mechanics (b) and (c) as built; no `UseProxy`, no primary-handler replacement, no resilience anywhere in the package; `internal` default + sealed types + XML docs; `dotnet list src/Cloudstrap.Extensions/Cloudstrap.Extensions.csproj package` → only the two project references (zero `Azure.*` yet, zero auth, zero `Aspire.*`).
- [ ] User approved — implementation may continue past this gate

---

## Slice 2 — Standard health probes, both directions: the app serves `/healthz` + `/ready` with one call, and flagged clients probe their peers the same way

---

## Step 3 — `MapCloudstrapHealthChecks()` serves tag-filtered `/healthz` and `/ready` — anonymous, short-circuited, config-pathed, idempotent, disable-able (AC-E11)

- [ ] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Extensions/EndpointRouteBuilderExtensions.cs` *(create)* — `public static`, spec sketch verbatim: `MapCloudstrapHealthChecks(this IEndpointRouteBuilder endpoints) : IEndpointRouteBuilder`.
- `src/Test/UnitTest/Cloudstrap.Extensions.Tests/MapCloudstrapHealthChecksTests.cs` *(create)*
- `src/Test/UnitTest/Cloudstrap.Extensions.Tests/Cloudstrap.Extensions.Tests.csproj` *(modify)* — add `<PackageReference Include="Microsoft.AspNetCore.TestHost" />`.
- `src/Directory.Packages.props` *(modify)* — pin `Microsoft.AspNetCore.TestHost` (test-only, planner mechanic (g)).

**RED** *(write these tests first, run them, confirm they fail — fixture: `WebApplication.CreateBuilder` + `builder.WebHost.UseTestServer()` + in-memory `Cloudstrap` config; checks registered via the stock `AddHealthChecks().AddCheck(...)` with `CloudstrapHealthCheckTags`; assertions are real HTTP GETs through `app.GetTestClient()` — observable probe behavior, not registration inspection)*:
- Unit test file: `MapCloudstrapHealthChecksTests.cs`
  - `Map_WithDefaults_ServesLiveTaggedChecksOnHealthz` — one healthy check tagged `live`, one **unhealthy** check tagged `ready` → `GET /healthz` = 200 `Healthy` (the ready-tagged failure is invisible to liveness — tag filtering proven, not vacuous).
  - `Map_WithDefaults_ServesReadyTaggedChecksOnReady` — same fixture → `GET /ready` = 503 (the unhealthy ready check governs readiness).
  - `Map_WithConfiguredPaths_ServesThem` — `Cloudstrap:HealthChecks:LivenessPath=/alive`, `ReadinessPath=/prepared` → probes answer there, defaults 404.
  - `Map_WithEnabledFalse_MapsNothing` — `Cloudstrap:HealthChecks:Enabled=false` → `/healthz` and `/ready` both 404 (the SPA-fallback-free test app), and the call did not throw (AC-E11 disabled clause).
  - `Map_CalledTwice_ServesOneSetOfEndpoints` — double call → `GET /healthz` still 200 with no `AmbiguousMatchException`/500 (idempotence, planner mechanic (e)).
  - `Map_WithNoChecksRegistered_BothProbesReturnHealthy` — `AddHealthChecks()` with zero checks → both 200 (spec edge case, stock empty-set behavior, documented).
  - `Map_OnNullEndpoints_ThrowsArgumentNullException` (guard clause).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Extensions.Tests\bin\Debug\net10.0\Cloudstrap.Extensions.Tests.exe --filter "MapCloudstrapHealthChecksTests"
  ```

**GREEN**:
- `EndpointRouteBuilderExtensions.MapCloudstrapHealthChecks` — planner mechanic (e): read `HealthChecksOptions` from configuration; when `Enabled`, `endpoints.MapHealthChecks(LivenessPath, new HealthCheckOptions { Predicate = r => r.Tags.Contains(CloudstrapHealthCheckTags.Liveness) }).AllowAnonymous().ShortCircuit()` and the `ReadinessPath`/`Readiness` twin — the source's `/live`+`/ready` mapping with Core's configurable paths, the stock response writer, and none of the dropped `/health`-on-port-9000 / `LOC` / bespoke-writer behavior; idempotence guard per mechanic (e). XML docs document the tag contract (#2's `CloudstrapHealthCheckTags`), the anonymous+short-circuit posture, and the stock-`MapHealthChecks` escape hatch for custom writers.

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: an app now gets both standard probes with one call — correctly tag-filtered (an unhealthy dependency flips readiness but never liveness), path-configurable, disable-able and double-call-safe (AC-E11).
2. `dotnet build src/Cloudstrap.sln` → zero warnings; full `runTests` green; `dotnet format src/Cloudstrap.sln --verify-no-changes` → exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 4 — A client with `EnableHealthCheck = true` probes its peer's `/healthz`: the `{prefix|name}-liveness` URI check appears on the stock builder, ready-tagged, idempotent, statics-free ⚠️ *(Risk Area: shared-contract Core amendment + new Apache-2.0 dependency — AC-E10, Decision Log OQ-2)*

- [ ] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Core/HttpClientServiceOptions.cs` *(modify)* — the committed additive amendment (Decision Log OQ-2): `public string HealthCheckPath { get; set; } = "/healthz";` with XML docs (the relative path the dependency health check probes on the peer; pairs with `MapCloudstrapHealthChecks`' default). **No other Core change; Core stays Azure-free.**
- `src/Test/UnitTest/Cloudstrap.Core.Tests/CloudstrapOptionsTests.cs` *(modify)* — binding + default coverage for the new property.
- `src/Cloudstrap.Extensions/DependencyHealthCheckSetup.cs` *(create)* — `internal sealed : IConfigureOptions<HealthCheckServiceOptions>`, planner mechanic (d).
- `src/Cloudstrap.Extensions/ServiceCollectionExtensions.cs` *(modify)* — the entry point registers the setup + the named probe `HttpClient` for every client.
- `src/Cloudstrap.Extensions/Cloudstrap.Extensions.csproj` *(modify)* — add `<PackageReference Include="AspNetCore.HealthChecks.Uris" />`.
- `src/Directory.Packages.props` *(modify)* — pin `AspNetCore.HealthChecks.Uris` 9.0.0.
- `src/Test/UnitTest/Cloudstrap.Extensions.Tests/DependencyHealthCheckTests.cs` *(create)*

**RED** *(write these tests first, run them, confirm they fail — Core half first: bind `Cloudstrap:HttpClients:Catalog:HealthCheckPath=/live-probe` through `GetCloudstrapOptions` and assert the value plus the `/healthz` default. Extensions half: registration-shape assertions via `IOptions<HealthCheckServiceOptions>.Value.Registrations`; execution assertions run `HealthCheckService.CheckHealthAsync` with the named probe client's primary handler swapped to a `TestServer` peer app that itself uses `MapCloudstrapHealthChecks` — Step 3's feature is the peer, no network)*:
- Unit test file (Core): `CloudstrapOptionsTests.cs` — new methods:
  - `HttpClients_HealthCheckPath_DefaultsToHealthz`
  - `HttpClients_HealthCheckPath_BindsConfiguredValue`
- Unit test file (Extensions): `DependencyHealthCheckTests.cs`
  - `EnableHealthCheck_RegistersReadyTaggedLivenessCheckNamedAfterClient` — `EnableHealthCheck=true` → `HealthCheckServiceOptions.Registrations` contains exactly one entry `Catalog-liveness` tagged `CloudstrapHealthCheckTags.Readiness` (AC-E10 naming/tag half).
  - `EnableHealthCheck_WithPrefix_UsesPrefixForTheName` — `HealthCheckPrefix=ContosoCatalog` → `ContosoCatalog-liveness` (override wins).
  - `EnableHealthCheck_False_RegistersNoCheck` — default flag → zero Cloudstrap registrations.
  - `EnableHealthCheck_RegisteredTwice_AddsOneCheck` — same client registered twice → one registration; a *fresh second service collection* gets its own check (the no-process-wide-static half of AC-E10 — the source's `ConcurrentDictionary` leak, fixed).
  - `EnableHealthCheck_HealthyPeer_ReportsHealthy` — peer TestServer serving `MapCloudstrapHealthChecks` with a healthy live-tagged check → `CheckHealthAsync` reports `Healthy` for `Catalog-liveness` (default `/healthz` path proven end to end, status-code semantics).
  - `EnableHealthCheck_PeerServing404OnConfiguredPath_ReportsUnhealthy` — `HealthCheckPath=/nope` against the same peer → `Unhealthy` (the configurable path + status-code comparison — Deliberate Behavior Change 7; body text is irrelevant by construction).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Core.Tests\bin\Debug\net10.0\Cloudstrap.Core.Tests.exe --filter "HealthCheckPath"
  src\Test\UnitTest\Cloudstrap.Extensions.Tests\bin\Debug\net10.0\Cloudstrap.Extensions.Tests.exe --filter "DependencyHealthCheckTests"
  ```

**GREEN**:
- Core amendment per Scope (property + XML docs only).
- `DependencyHealthCheckSetup` — planner mechanic (d): reads the bound named `HttpClientServiceOptions`; when `EnableHealthCheck`, adds the `HealthCheckRegistration` (`{HealthCheckPrefix ?? clientName}-liveness`, failure status `Unhealthy`, tags `[ready]`) wrapping the Uris-package check against `new Uri(BaseAddress, HealthCheckPath)`, using the named probe `HttpClient` (registered by the entry point with a 2-second default timeout — source parity — and overridable like any named client); skips when the name already exists in `Registrations`.
- `AddCloudstrapHttpServiceClient` — registers the setup (TryAddEnumerable per name so double registration of the same client stays idempotent) + the named probe client. XML docs on the flag path updated: check name, tag, probed URI, override seams (`HealthCheckPrefix`, `HealthCheckPath`, the named probe client, stock `AddHealthChecks()` for advanced cases).

**DB changes**: none.

**VERIFY**:
1. Both test exes → all pass: a flagged client's peer is now probed on its configurable `/healthz` with status-code semantics, surfaced as a ready-tagged check on the stock builder — named by convention, deduped per service collection, with zero static state (AC-E10; Core amendment bound and defaulted).
2. `dotnet build src/Cloudstrap.sln` → zero warnings; full `runTests` green (all existing Core tests untouched by the additive property); `dotnet format src/Cloudstrap.sln --verify-no-changes` → exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 2: probes standard in both directions; the Core contract amended *(covers Steps 3–4)*

*Executor: STOP here. Present the results of all covered steps and WAIT for user approval — do not start the next step.*

⚠️ **Risk areas at this gate**: the **shared-contract amendment** to shipped `Cloudstrap.Core` (`HealthCheckPath`, Decision Log OQ-2) — additive, but it is permanent configuration surface on a package other deliverables already consume: review name, default and XML docs · the suite's **first Apache-2.0 / first Xabaril dependency** (`AspNetCore.HealthChecks.Uris` 9.0.0) — rule-4 review; note the license family in the package README (Step 9) · test-only `Microsoft.AspNetCore.TestHost` pin (planner mechanic (g)) · planner mechanic (d)/(e) mechanics decided by the executor during RED (Uris-package API shape; `DataSources`-based idempotence) — report any deviation.

- [ ] Behavioral verification: test exe output shows — tag-filtered probes over real HTTP (unhealthy ready check flips `/ready` to 503 while `/healthz` stays 200), configured paths, disabled mode, double-map safety, empty-set behavior (Step 3); registration naming/tag/prefix/dedupe incl. the fresh-collection statics proof, and the executed peer probes — healthy on the default path, unhealthy on a 404 path (Step 4); Core's new property binding + default (Step 4).
- [ ] Code review: `MapCloudstrapHealthChecks` vs the spec row (stock writer, anonymous + short-circuit, nothing from the dropped `/health`/`LOC`/port-9000 behavior); `DependencyHealthCheckSetup` vs planner mechanic (d) — no static state anywhere (`grep -i static` sweep over the new files finds only the extension-class declarations); the Core diff is exactly one property + docs.
- [ ] ⚠️ Dependency review (risk area): `dotnet list src/Cloudstrap.Extensions/Cloudstrap.Extensions.csproj package` → `AspNetCore.HealthChecks.Uris` 9.0.0 added, nothing else new; Apache-2.0 acknowledged.
- [ ] User approved — implementation may continue past this gate

---

## Slice 3 — Azure-backed configuration and state: KeyVault secrets, a conventional blob container, DataProtection keys — explicit options, `DefaultAzureCredential`, fail-fast

---

## Step 5 — A secret named `{prefix}-Foo--Bar` surfaces as configuration key `Foo:Bar`: the ported prefix manager and the validated `Cloudstrap:KeyVault` options (AC-E1 mechanics, AC-E4 prefix rules)

- [ ] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Extensions/PrefixKeyVaultSecretManager.cs` *(create)* — the deliverable's one **Port**: `internal sealed : KeyVaultSecretManager`; `Load` filters on `{prefix}-` with `StringComparison.Ordinal` (the spec's fix — the source used culture-sensitive `StartsWith`), empty prefix loads everything; `GetKey` strips the prefix and maps `--` → `ConfigurationPath.KeyDelimiter`.
- `src/Cloudstrap.Extensions/KeyVaultOptions.cs` *(create)* — spec sketch verbatim: `SectionName = "Cloudstrap:KeyVault"`, `Enabled` (default `false`), `VaultUri : Uri?`, `SecretPrefix : string?` (null → `WorkloadName` default; `""` → no filter).
- `src/Cloudstrap.Extensions/KeyVaultOptionsValidator.cs` *(create)* — `internal sealed : IValidateOptions<KeyVaultOptions>`, planner mechanic (a): `Enabled` + missing/relative `VaultUri` → failure naming `Cloudstrap:KeyVault:VaultUri`; disabled → always valid.
- `src/Cloudstrap.Extensions/Cloudstrap.Extensions.csproj` *(modify)* — add `<PackageReference Include="Azure.Extensions.AspNetCore.Configuration.Secrets" />`.
- `src/Directory.Packages.props` *(modify)* — pin it.
- `src/Test/UnitTest/Cloudstrap.Extensions.Tests/PrefixKeyVaultSecretManagerTests.cs` *(create)*
- `src/Test/UnitTest/Cloudstrap.Extensions.Tests/KeyVaultOptionsValidationTests.cs` *(create)*

**RED** *(write these tests first, run them, confirm they fail — the manager is tested directly via the IVT grant against `SecretProperties`/`KeyVaultSecret` fixtures (public constructors, zero network — the spec's test strategy verbatim); the validator through its `Validate` result)*:
- Unit test file: `PrefixKeyVaultSecretManagerTests.cs`
  - `Load_SecretWithPrefix_IsLoaded` — prefix `contoso-app`, secret `contoso-app-Foo--Bar` → loaded.
  - `Load_SecretWithOtherPrefix_IsNotLoaded` — `other-Baz` → skipped (AC-E1's exclusion half).
  - `Load_PrefixComparisonIsOrdinalAndCaseSensitive` — prefix `app`, secret `APP-Foo` → **not** loaded (the Ordinal fix pinned; the culture-sensitive source would be locale-dependent here).
  - `GetKey_StripsPrefixAndMapsDoubleDashToColon` — `contoso-app-Foo--Bar` → `Foo:Bar` (AC-E1's mapping half).
  - `Load_WithEmptyPrefix_LoadsEverything` + `GetKey_WithEmptyPrefix_KeepsFullNameWithMapping` — `""` disables filtering, `other-Baz` → `other-Baz`… mapped (`--`→`:` still applies) (AC-E4's empty-prefix rule).
- Unit test file: `KeyVaultOptionsValidationTests.cs`
  - `Validate_EnabledWithoutVaultUri_FailsNamingTheKey` (AC-E2's rule half)
  - `Validate_EnabledWithRelativeVaultUri_FailsNamingTheKey`
  - `Validate_DisabledWithoutVaultUri_Succeeds` (AC-E3's rule half)
  - `Validate_EnabledWithAbsoluteVaultUri_Succeeds`
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Extensions.Tests\bin\Debug\net10.0\Cloudstrap.Extensions.Tests.exe --filter "PrefixKeyVaultSecretManagerTests"
  ```

**GREEN**: the three Scope types per their descriptions — manager ported with the two spec'd changes (Ordinal, configurable prefix incl. `""`), options + validator per the sketch. XML docs on `KeyVaultOptions` document the secret-naming convention (`{prefix}-Section--Key`) and the `""` semantics.

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: the differentiating secret-filtering behavior now exists and is pinned — prefix-filtered (ordinal, case-sensitive), `--`→`:` mapped, empty-prefix pass-through — and every `Cloudstrap:KeyVault` misconfiguration rule fails naming its key (AC-E1 mechanics, AC-E4 prefix rules).
2. `dotnet build src/Cloudstrap.sln` → zero warnings; full `runTests` green; `dotnet format src/Cloudstrap.sln --verify-no-changes` → exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 6 — `Program.cs` calls `AddCloudstrapKeyVault()` unconditionally: no-op when disabled, loud failure when enabled-but-broken, `DefaultAzureCredential`/hook-credential wiring when enabled (AC-E2, AC-E3, AC-E4 credential rule)

- [ ] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Extensions/HostApplicationBuilderExtensions.cs` *(create)* — `public static`, spec sketch verbatim: `AddCloudstrapKeyVault(this IHostApplicationBuilder builder, Action<KeyVaultConnectionSettings>? configure = null) : IHostApplicationBuilder` (the file later gains the Step 7/8 entry points).
- `src/Cloudstrap.Extensions/KeyVaultConnectionSettings.cs` *(create)* — code-level hook per sketch: `Credential : TokenCredential?`, `ReloadInterval : TimeSpan?`.
- `src/Cloudstrap.Extensions/KeyVaultRegistration.cs` *(create)* — `internal static`, planner mechanic (f): composes `(Uri vaultUri, TokenCredential credential, PrefixKeyVaultSecretManager manager, AzureKeyVaultConfigurationOptions sourceOptions)` from the bound options + hook + the `Cloudstrap:Application` section (prefix default `WorkloadName`).
- `src/Cloudstrap.Extensions/Cloudstrap.Extensions.csproj` *(modify)* — add `<PackageReference Include="Azure.Identity" />` (version already CPM-pinned by #3).
- `src/Test/UnitTest/Cloudstrap.Extensions.Tests/AddCloudstrapKeyVaultTests.cs` *(create)*

**RED** *(write these tests first, run them, confirm they fail — fixtures use `HostApplicationBuilder` with in-memory config only; **no test path may add a real KeyVault source**, because `ConfigurationManager` builds providers eagerly and would contact the vault (planner mechanic (f)) — the enabled arm's composition is asserted through the internal seam via IVT)*:
- Unit test file: `AddCloudstrapKeyVaultTests.cs`
  - `AddCloudstrapKeyVault_SectionAbsent_IsANoOpReturningTheSameBuilder` — configuration source list unchanged (count + types), same builder instance (AC-E3).
  - `AddCloudstrapKeyVault_EnabledFalse_IsANoOp` — explicit `Enabled=false` + `VaultUri` set → still no source added (AC-E3's per-environment story).
  - `AddCloudstrapKeyVault_EnabledWithoutVaultUri_ThrowsConfigurationValidationExceptionNamingTheKey` — Core's exception type on the eager path, message contains `Cloudstrap:KeyVault:VaultUri` (AC-E2 — no silent skip).
  - `Compose_WithNoHook_UsesDefaultAzureCredentialAndWorkloadNamePrefix` — seam output: credential is `DefaultAzureCredential` (constructed, never invoked — no token acquisition), manager prefix is the `Cloudstrap:Application`-derived `WorkloadName` (De-NIHDI: no vault-name composition, no credential exclusion list, no AKS/`CLOUD_PIPELINE` gating anywhere).
  - `Compose_WithExplicitPrefix_ExplicitWins` + `Compose_WithEmptyPrefix_DisablesFiltering` (AC-E4 prefix precedence at the wiring level).
  - `Compose_WithHookCredential_HookWins` — a stub `TokenCredential` supplied via `configure` → seam returns the stub, not `DefaultAzureCredential` (AC-E4 credential rule).
  - `Compose_WithReloadInterval_PassesItThrough` — hook value lands on `AzureKeyVaultConfigurationOptions.ReloadInterval`.
  - `AddCloudstrapKeyVault_CalledTwiceWhileDisabled_StaysIdempotent` — marker set, no double work, no throw.
  - `AddCloudstrapKeyVault_OnNullBuilder_ThrowsArgumentNullException` (guard clause).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Extensions.Tests\bin\Debug\net10.0\Cloudstrap.Extensions.Tests.exe --filter "AddCloudstrapKeyVaultTests"
  ```

**GREEN**:
- `AddCloudstrapKeyVault` — planner mechanic (f): guard; idempotence marker in `builder.Properties`; bind `Cloudstrap:KeyVault` eagerly; run `KeyVaultOptionsValidator`, wrap failures in `ConfigurationValidationException`; disabled/absent → return (nothing Azure-touching runs); enabled → `KeyVaultRegistration.Compose(...)` then `builder.Configuration.AddAzureKeyVault(vaultUri, credential, manager)` (with `sourceOptions` when the overload requires it). XML docs: **call it first** in `Program.cs` (before `GetCloudstrapOptions`/`UseCloudstrapObservability`) so secrets participate in options binding; KeyVault-wins layering; the unreachable-vault fail-fast; the one-owner Aspire rule pointer.
- `KeyVaultRegistration.Compose` — per Scope; `DefaultAzureCredential` only constructed on this arm.

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: the call is now safe to leave in every `Program.cs` unconditionally — per-environment configuration decides; a broken enabled section fails startup naming its key; the enterprise naming convention, credential exclusion list and pipeline gating are verifiably gone (AC-E2, AC-E3, AC-E4).
2. `dotnet build src/Cloudstrap.sln` → zero warnings; full `runTests` green; `dotnet format src/Cloudstrap.sln --verify-no-changes` → exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 7 — `AddCloudstrapBlobStorage()` registers the conventional `BlobContainerClient`: explicit URI or connection string, container defaulting to `{SystemName}` lowercase, creation only on the explicit flag (AC-E13)

- [ ] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Extensions/StorageOptions.cs` *(create)* — sketch verbatim: `SectionName = "Cloudstrap:Storage"`, `BlobServiceUri : Uri?`, `ContainerName : string?`, `ConnectionString : string?`, `CreateContainerIfNotExists : bool = false`.
- `src/Cloudstrap.Extensions/StorageOptionsValidator.cs` *(create)* — `internal sealed`, planner mechanic (a): neither `BlobServiceUri` nor a connection string (setting or `ConnectionStrings:CloudstrapStorage`) → failure naming `Cloudstrap:Storage:BlobServiceUri`; ctor-injected `IConfiguration` for the `ConnectionStrings:` lookup (Core's `OpenTelemetryOptionsValidator` idiom).
- `src/Cloudstrap.Extensions/AzureCredentialSettings.cs` *(create)* — code-level hook per sketch: `Credential : TokenCredential?` (shared with Step 8).
- `src/Cloudstrap.Extensions/BlobStorageRegistration.cs` *(create)* — `internal static` factory seam: resolves connection-string-vs-URI precedence, container-name defaulting, credential selection; called by the singleton factory.
- `src/Cloudstrap.Extensions/HostApplicationBuilderExtensions.cs` *(modify)* — add `AddCloudstrapBlobStorage(this IHostApplicationBuilder builder, Action<AzureCredentialSettings>? configure = null) : IHostApplicationBuilder`.
- `src/Cloudstrap.Extensions/Cloudstrap.Extensions.csproj` *(modify)* + `src/Directory.Packages.props` *(modify)* — `Azure.Storage.Blobs`.
- `src/Test/UnitTest/Cloudstrap.Extensions.Tests/AddCloudstrapBlobStorageTests.cs` *(create)*

**RED** *(write these tests first, run them, confirm they fail — constructing `BlobContainerClient` performs no I/O; assertions read its public `Name`/`Uri`/`AccountName`; credential selection asserted through the internal seam via IVT; `CreateContainerIfNotExists` stays `false` in every resolution test so nothing ever touches the network)*:
- Unit test file: `AddCloudstrapBlobStorageTests.cs`
  - `Resolve_WithBlobServiceUri_YieldsClientForDefaultContainerName` — `SystemName=Contoso`, `BlobServiceUri=https://contosostore.blob.core.windows.net/` → singleton `BlobContainerClient` with `Name == "contoso"` and `Uri` ending `/contoso` (De-NIHDI row "Storage account": convention from *the consumer's own* `SystemName`, no enterprise account naming).
  - `Resolve_WithExplicitContainerName_ExplicitWins` — `ContainerName=orders` → `Name == "orders"`.
  - `Resolve_WithConnectionString_WinsOverBlobServiceUri` — `ConnectionString=UseDevelopmentStorage=true` + a `BlobServiceUri` both set → `AccountName == "devstoreaccount1"` (the Azurite/dev path is an explicit setting, not environment sniffing).
  - `Resolve_WithPlatformConventionConnectionString_IsHonored` — no `Cloudstrap:Storage:ConnectionString`, but `ConnectionStrings:CloudstrapStorage=UseDevelopmentStorage=true` → same result (founding Aspire posture §3).
  - `Startup_WithNeitherUriNorConnectionString_FailsNamingTheKey` — host `StartAsync` fails with a message containing `Cloudstrap:Storage:BlobServiceUri` (ValidateOnStart).
  - `SelectCredential_DefaultsToDefaultAzureCredential_HookWins` — seam assertions (constructed only, never invoked).
  - `Resolve_WithCreateFlagDefault_PerformsNoNetworkCall` — resolution of the singleton succeeds against a non-existent account URI (construction-only proof that creation is opt-in; the create path itself is Azure-SDK behavior surfaced un-swallowed, per the spec edge case — README-documented, exercised by the manual procedure).
  - `AddCloudstrapBlobStorage_OnNullBuilder_ThrowsArgumentNullException` (guard clause).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Extensions.Tests\bin\Debug\net10.0\Cloudstrap.Extensions.Tests.exe --filter "AddCloudstrapBlobStorageTests"
  ```

**GREEN**:
- `AddCloudstrapBlobStorage` — bind + `ValidateOnStart` the options with the validator; register the `BlobContainerClient` singleton via a factory calling `BlobStorageRegistration.CreateClient(options, settings, configuration)`: connection string (setting, else `ConnectionStrings:CloudstrapStorage`) → `new BlobContainerClient(connectionString, containerName)`; else service URI + container → `new BlobContainerClient(containerUri, credential)` with `DefaultAzureCredential` or the hook credential; `CreateContainerIfNotExists` → `CreateIfNotExists()` inside the factory, exceptions surfacing untouched. Container default: `SystemName` from `Cloudstrap:Application`, lowercased (`ToLowerInvariant`). XML docs per the Behaviors row (this is the seam #15 claim-check builds on).

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: one call now yields the conventional container client — explicit-config-driven on every axis, dev-storage by explicit connection string, platform `ConnectionStrings:` honored, creation strictly opt-in, misconfiguration loud (AC-E13).
2. `dotnet build src/Cloudstrap.sln` → zero warnings; full `runTests` green; `dotnet format src/Cloudstrap.sln --verify-no-changes` → exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 8 — `AddCloudstrapDataProtection()` persists the key ring to the configured blob and protects it with the configured KeyVault key — enabled-but-incomplete fails startup, never the source's silent skip (AC-E12, Decision Log OQ-3)

- [ ] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Extensions/DataProtectionOptions.cs` *(create)* — sketch verbatim: `SectionName = "Cloudstrap:DataProtection"`, `Enabled` (default `false`), `KeysBlobUri : Uri?`, `KeyVaultKeyId : Uri?` (both required when enabled — OQ-3), `ApplicationName : string?` (null → `WorkloadName`). Planner mechanic (i): the simple-name collision with the framework type is accepted per the spec sketch and documented.
- `src/Cloudstrap.Extensions/DataProtectionOptionsValidator.cs` *(create)* — `internal sealed`, planner mechanic (a): enabled + missing either URI → one failure per missing key, naming `Cloudstrap:DataProtection:KeysBlobUri` / `…:KeyVaultKeyId`.
- `src/Cloudstrap.Extensions/HostApplicationBuilderExtensions.cs` *(modify)* — add `AddCloudstrapDataProtection(this IHostApplicationBuilder builder, Action<AzureCredentialSettings>? configure = null) : IHostApplicationBuilder`.
- `src/Cloudstrap.Extensions/Cloudstrap.Extensions.csproj` *(modify)* + `src/Directory.Packages.props` *(modify)* — `Azure.Extensions.AspNetCore.DataProtection.Blobs`, `Azure.Extensions.AspNetCore.DataProtection.Keys`.
- `src/Test/UnitTest/Cloudstrap.Extensions.Tests/AddCloudstrapDataProtectionTests.cs` *(create)*

**RED** *(write these tests first, run them, confirm they fail — the Azure DataProtection extensions construct clients without I/O; assertions inspect the built container's `KeyManagementOptions` and the framework's `DataProtectionOptions` — offline throughout)*:
- Unit test file: `AddCloudstrapDataProtectionTests.cs`
  - `Enabled_WithBothUris_ConfiguresBlobRepositoryAndKeyVaultEncryptor` — `KeysBlobUri=https://contosostore.blob.core.windows.net/keys/keys.xml`, `KeyVaultKeyId=https://contoso-vault.vault.azure.net/keys/dp-key` → `IOptions<KeyManagementOptions>.Value.XmlRepository` is the Azure blob repository type and `.XmlEncryptor` the Azure KeyVault encryptor type (type-name assertions — the boundary the spec's test strategy allows) (AC-E12 wiring half).
  - `Enabled_SetsApplicationDiscriminatorFromWorkloadNameByDefault` — framework `Microsoft.AspNetCore.DataProtection.DataProtectionOptions.ApplicationDiscriminator == WorkloadName` (payload isolation, spec behavior row).
  - `Enabled_WithExplicitApplicationName_ExplicitWins`.
  - `Enabled_MissingKeysBlobUri_FailsStartupNamingTheKey` + `Enabled_MissingKeyVaultKeyId_FailsStartupNamingTheKey` — `StartAsync` fails naming exactly the missing key (OQ-3: both required; never a silent skip — the source's log-and-return behavior is provably gone).
  - `Disabled_ConfiguresNoXmlRepository` — `XmlRepository` remains null; the call is a safe no-op (parity with the other entry points).
  - `Enabled_WithHookCredential_UsesIt` — internal seam/credential-selection assertion mirroring Step 7's idiom.
  - `AddCloudstrapDataProtection_OnNullBuilder_ThrowsArgumentNullException` (guard clause).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Extensions.Tests\bin\Debug\net10.0\Cloudstrap.Extensions.Tests.exe --filter "AddCloudstrapDataProtectionTests"
  ```

**GREEN**:
- `AddCloudstrapDataProtection` — bind + `ValidateOnStart` with the validator; when enabled: `builder.Services.AddDataProtection().SetApplicationName(ApplicationName ?? WorkloadName).PersistKeysToAzureBlobStorage(KeysBlobUri, credential).ProtectKeysWithAzureKeyVault(KeyVaultKeyId, credential)` with the shared credential selection (one credential instance for both, `DefaultAzureCredential` default, hook wins). XML docs: OQ-3 rationale (secure by default), the stock `AddDataProtection()` chain as the blob-only escape hatch, required RBAC pointers to the README.

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: scaled-out cookie/antiforgery scenarios now have one-call shared DataProtection — blob-persisted, KeyVault-protected, app-isolated — and every misconfiguration is a startup failure naming its key instead of the source's silent skip (AC-E12).
2. `dotnet build src/Cloudstrap.sln` → zero warnings; full `runTests` green; `dotnet format src/Cloudstrap.sln --verify-no-changes` → exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 3: the Azure trio and the credential posture *(covers Steps 5–8)*

*Executor: STOP here. Present the results of all covered steps and WAIT for user approval — do not start the next step.*

⚠️ **Risk areas at this gate**: **five new `Azure.*` dependencies** (`Configuration.Secrets`, `Identity` reference, `Storage.Blobs`, `DataProtection.Blobs`, `DataProtection.Keys` — all MIT, Microsoft, CPM-pinned with executor-verified versions) — rule-4 review · the **credential posture** (founding hosting decision): plain `DefaultAzureCredential` everywhere, constructed never invoked in tests, hook-supplied `TokenCredential` always wins, zero environment sniffing / credential exclusion lists / client-secret paths — confirm the De-NIHDI flagship rows are dead (`kv-Riziv-IT-{ENV}-App-001`, `CLOUD_PIPELINE`, `AZURE_DPAPI_*`, `IsRunningInAks`) · the repo's **first `InternalsVisibleTo`** (planner mechanic (h) — own tests only) · planner mechanic (f)'s testability boundary — the real `AddAzureKeyVault` source addition is proven only by the Step 9 manual procedure; confirm that trade-off.

- [ ] Behavioral verification: test exe output shows — the ported manager's five filtering/mapping proofs incl. the Ordinal pin, the four options-rule proofs (Step 5); no-op/no-op/fail-fast/composition/idempotence for `AddCloudstrapKeyVault` incl. prefix precedence and hook-credential-wins (Step 6); the eight storage proofs incl. connection-string precedence, platform `ConnectionStrings:` convention and the no-network default (Step 7); the DataProtection wiring, discriminator, both fail-fast keys and the disabled no-op (Step 8).
- [ ] Code review: the three new option types + validators vs the spec sketch verbatim (they live **here**, Core untouched since Step 4); `KeyVaultRegistration`/`BlobStorageRegistration` seams contain every Azure-touching decision (nothing Azure-touching on any disabled path); exception types on the eager vs DI paths (`ConfigurationValidationException` vs `OptionsValidationException`) consistent with Core precedent; XML docs on all public surface.
- [ ] ⚠️ Dependency review (risk area): `dotnet list src/Cloudstrap.Extensions/Cloudstrap.Extensions.csproj package` → exactly the six runtime packages (five Azure + Uris) + two project references; `dotnet list src/Cloudstrap.Core/Cloudstrap.Core.csproj package` → **unchanged, zero Azure** (Core stays Azure-free).
- [ ] User approved — implementation may continue past this gate

---

## Slice 4 — Publishable, guarded, demonstrated: metadata + README with the manual KeyVault procedure, permanent closure guards, and the WASM SUT running on this package

---

## Step 9 — The package is publishable and permanently guarded: metadata, README (one-owner Aspire rule + manual KeyVault verification), surface/closure/identifier guards (AC-E5, AC-E14, AC-ASP2)

- [ ] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Extensions/Cloudstrap.Extensions.csproj` *(modify)* — `<Description>` (KeyVault-backed configuration, Azure Blob DataProtection, conventional blob storage, config-driven typed HTTP clients with correlation and token seams, and standard health probe endpoints — one call and one `Cloudstrap:` subsection each), `<PackageTags>$(PackageTags);keyvault;configuration;httpclient;dataprotection;healthchecks;azure</PackageTags>`, `<PackageReadmeFile>README.md</PackageReadmeFile>` + `<None Include="README.md" Pack="true" PackagePath="/" />`.
- `src/Cloudstrap.Extensions/README.md` *(create)* — quick start (a ten-line `Program.cs`: `AddCloudstrapKeyVault` **first**, `GetCloudstrapOptions`, `UseCloudstrapObservability`, typed client, `AddCloudstrapDataProtection`/`AddCloudstrapBlobStorage`, `MapCloudstrapHealthChecks`); settings tables for the three owned sections + the consumed Core sections (incl. the new `HealthCheckPath`); **the one-owner Aspire rule** — "use Cloudstrap's KeyVault configuration **or** Aspire's, not both", with the secret-prefix filter named as the differentiator (AC-E5); KeyVault layering (secrets win over `appsettings.json`) and the call-it-first caveat; required RBAC (`Key Vault Secrets User`; data-plane roles for storage/DataProtection); the token-seam contract for consumers awaiting #9/#10 and the AC-E8 error explained; the no-resilience posture and how to add resilience via `ConfigureHttpClientDefaults` (AC-ASP3); the `AspNetCore.HealthChecks.Uris` Apache-2.0 note; the framework-reference consequence (server apps only; WASM clients are #13's charter); the `DataProtectionOptions` name-qualification note (planner mechanic (i)); escape hatches per Behaviors row (stock `AddDataProtection()`, stock `MapHealthChecks`, stock `AddHealthChecks().AddUrlGroup`); **the AC-E5 manual KeyVault verification procedure** (AC-O1-style, step-by-step against a real vault: create vault → add secrets `{prefix}-Demo--Message` and `other-Ignored` → grant the RBAC role → set `Cloudstrap:KeyVault:Enabled=true` + `VaultUri` → run the WASM SUT or any consumer → verify `Demo:Message` resolves and `other-Ignored` does not → break the URI → verify the fail-fast).
- `src/Test/UnitTest/Cloudstrap.Extensions.Tests/PackageSurfaceTests.cs` *(create)* — permanent guards, mirroring the base idiom.

**RED** *(guard tests are written and run first but, as tripwires against correct code, may pass immediately — the honest failing state is in the artifacts: before GREEN the Release nupkg has no README/description/tags; recorded per the plan-2/plan-3 precedent)*:
- Unit test file: `PackageSurfaceTests.cs`
  - `ReferencedAssemblies_OfExtensionsAssembly_MatchTheApprovedClosure` — every referenced assembly name starts with `System`, `Microsoft.`, `Azure.`, `HealthChecks.` or equals `Cloudstrap.Core`/`Cloudstrap.Observability`; explicitly assert **zero** names starting `Aspire` (AC-ASP2), `Duende`, `NWebsec`, `LanguageExt`, `Nihdi` (AC-E14 closure half — no auth package, no dead references resurrected).
  - `PublicTypes_OfExtensionsAssembly_ContainNoForbiddenIdentifiers` — no public type/member matches `(?i)nihdi|riziv|dynatrace|nservicebus`.
  - `PublicTypes_OfExtensionsAssembly_AreSealedOrStaticAndInTheSingleApprovedNamespace` — namespace `Cloudstrap.Extensions` only; the one interface (`IAccessTokenHandlerProvider`) plus sealed/static classes.
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Extensions.Tests\bin\Debug\net10.0\Cloudstrap.Extensions.Tests.exe --filter "PackageSurfaceTests"
  ```

**GREEN**: add the csproj metadata and write `README.md` per Scope.

**DB changes**: none.

**VERIFY**:
1. Test exe (full run) → all tests pass, including the three new guards.
2. `dotnet build src/Cloudstrap.sln -c Release` → `src/Cloudstrap.Extensions/bin/Release/Cloudstrap.Extensions.<version>.nupkg`; expand a `.zip` copy → contains `README.md`, `icon.png`, `lib/net10.0/Cloudstrap.Extensions.dll` **and** `.xml`; nuspec shows the MIT license expression, description, tags, repository URL, and a dependency list with **no** auth package, no `NWebsec.*`, no `Microsoft.AspNet.WebApi.Client`, no `Aspire.*` (AC-E14 metadata half, AC-ASP2).
3. **AC-E14 identifier sweep** (new package + tests):
   ```powershell
   Get-ChildItem -Recurse -File -Path src/Cloudstrap.Extensions, src/Test/UnitTest/Cloudstrap.Extensions.Tests |
     Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
     Select-String -Pattern '(?i)(nihdi|riziv)'
   ```
   → zero matches (guard-test tripwire patterns are self-referential and excluded by reading the hits, as in plans 2/3).
4. **Closure checks**: `dotnet list src/Cloudstrap.Extensions/Cloudstrap.Extensions.csproj package` reviewed once more against the spec's dependency table — every entry OSI-licensed and CPM-pinned.
5. Full gates on the final tree: `dotnet build src/Cloudstrap.sln` (zero warnings) + `runTests` (all suites) + `dotnet format src/Cloudstrap.sln --verify-no-changes` (exit 0).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 10 — The WASM SUT runs on this package: `MapCloudstrapHealthChecks` replaces the hand-mapped probes, a typed client with a self-probe health check makes a real outbound hop — proven through the running app (AC-E15)

- [ ] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Test/WasmTestProject/src/Host/Bff/Cloudstrap.WasmTestProject.Host.Bff.csproj` *(modify)* — `<ProjectReference>` to `Cloudstrap.Extensions`.
- `src/Test/WasmTestProject/src/Host/Bff/Program.cs` *(modify)* — delete the two hand-mapped `MapHealthChecks` blocks (and the now-unused usings) in favor of one `app.MapCloudstrapHealthChecks();`; register the demo typed client: `builder.Services.AddCloudstrapHttpServiceClient<ISelfApiClient, SelfApiClient>("SelfApi");`.
- `src/Test/WasmTestProject/src/Host/Bff/appsettings.json` *(modify)* — new section `Cloudstrap:HttpClients:SelfApi` with `BaseAddress: "http://127.0.0.1:5300/"`, `Timeout: "00:00:05"`, `EnableHealthCheck: true` (the Bff probes its own `/healthz` — a real URI health check with zero extra infrastructure; `HealthCheckPath` stays at the new Core default, demonstrating it).
- `src/Test/WasmTestProject/src/Host/Bff/Services/ISelfApiClient.cs` + `SelfApiClient.cs` *(create)* — typed client: `Task<string> GetPeerCorrelationIdAsync(CancellationToken cancellationToken)` GETs `api/diagnostics/correlation` through the injected `HttpClient` and returns the peer-reported id.
- `src/Test/WasmTestProject/src/Contracts/OutboundCallDto.cs` *(create)* — `record OutboundCallDto(string PeerCorrelationId)`.
- `src/Test/WasmTestProject/src/Host/Bff/Controllers/DiagnosticsController.cs` *(modify)* — `GET api/diagnostics/outbound`: calls `ISelfApiClient.GetPeerCorrelationIdAsync` and returns the DTO — one real outbound hop through the Cloudstrap-registered client, correlation handler included.
- `src/Test/WasmTestProject/test/Cloudstrap.WasmTestProject.E2E.Tests/ExtensionsTests.cs` *(create)*
- `src/Test/WasmTestProject/README.md` *(modify)* — demo-table row: `/healthz` + `/ready` (Cloudstrap-mapped) + `GET api/diagnostics/outbound` | Cloudstrap.Extensions (#4) | `ExtensionsTests` — typed-client outbound hop with correlation, self-probe dependency health check, readiness flip on an unreachable peer; harness-notes update for the new outbound call.

**RED** *(write these tests first, run them, confirm they fail — the Bff has no `api/diagnostics/outbound` endpoint and no `SelfApi` client yet, so all three fail)*:
- E2E test file: `src/Test/WasmTestProject/test/Cloudstrap.WasmTestProject.E2E.Tests/ExtensionsTests.cs`
- E2E test methods:
  - `Outbound_TypedClientHop_PropagatesTheCallersCorrelationId` — GET `api/diagnostics/outbound` with header `X-Correlation-ID: <fresh guid>` → 200 and `peerCorrelationId` equals the sent id: the typed client resolved from config, made a real outbound HTTP hop, and the correlation handler propagated the ambient id across it (AC-E6/AC-E15 through the running app).
  - `Ready_WithSelfApiProbe_Returns200Healthy` — `/ready` = 200 `Healthy`: the readiness probe now aggregates the `SelfApi-liveness` URI check, which really executed against the running app's own `/healthz` (AC-E10/AC-E11 live).
  - `Ready_WithUnreachablePeerConfigured_Returns503WhileHealthzStays200` — `SutProcess.Start` launches a second short-lived instance on `http://127.0.0.1:5302` with application argument `--Cloudstrap:HttpClients:SelfApi:BaseAddress=http://127.0.0.1:59999/` → poll its `/healthz` to 200, then assert its `/ready` returns 503, then dispose: the dependency check demonstrably probes the *configured* peer and is readiness-only — an unreachable dependency never kills liveness (the tag contract, live; precedent: plan 3's second-instance scenarios).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\WasmTestProject\test\Cloudstrap.WasmTestProject.E2E.Tests\bin\Debug\net10.0\Cloudstrap.WasmTestProject.E2E.Tests.exe --filter "ExtensionsTests"
  ```

**GREEN**: the Scope items — csproj reference, the `Program.cs` swap + registration, the config section, the client/DTO/endpoint, the README row. Every pre-existing E2E test must stay green unchanged — in particular `HealthAndCorrelationTests.Healthz_Get_Returns200Healthy` / `Ready_Get_Returns200Healthy` now run against `MapCloudstrapHealthChecks` instead of the hand-mapped endpoints, doubling as the drop-in-replacement proof. *(If the self-probe introduces readiness flakiness at boot — the check firing before Kestrel listens — the executor reports at the gate rather than weakening assertions; the fixture polls `/` first, so the app is serving before any test hits `/ready`.)*

**DB changes**: none.

**VERIFY**:
1. E2E exe → the three new tests pass and every pre-existing E2E test passes unchanged (build first; one-time `playwright.ps1 install chromium` if needed).
2. Full gates on the final tree: `dotnet build src/Cloudstrap.sln` (zero warnings) + `runTests` (Core, Observability, AzureMonitor, Extensions, E2E — all green) + `dotnet format src/Cloudstrap.sln --verify-no-changes` (exit 0).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — final: deliverable #4 complete *(covers Steps 9–10; closes the deliverable)*

*Executor: STOP here. Present the results and WAIT for user approval. Any Git push afterwards requires the user's explicit go-ahead (CLAUDE.md: no push without confirmation).*

- [ ] Behavioral verification: the three `ExtensionsTests` methods pass; the full E2E suite passes with the SUT's probes served by `MapCloudstrapHealthChecks` and a live typed-client hop; the three `PackageSurfaceTests` guards green; the expanded Release `.nupkg` contents reviewed; the identifier sweep empty; user optionally runs the SUT manually (`dotnet run --project src/Test/WasmTestProject/src/Host/Bff`) and hits `/ready` + `api/diagnostics/outbound`.
- [ ] AC-E5 / manual KeyVault procedure (headline documentation artifact): user reviews the README — the one-owner Aspire rule, the secret-prefix differentiator, and the step-by-step live-vault walk; optionally executes the procedure against a real Key Vault (secrets load filtered and mapped, the fail-fast fires on a broken URI). The deliverable is demonstrable either way — the procedure is the documented artifact, per the spec's test strategy.
- [ ] Spec acceptance sign-off: walk AC-ASP2, AC-ASP3 and AC-E1…AC-E15 against the step evidence using the Overview's AC coverage map — all met; confirm nothing from the spec's Move-out/Drop/Out-of-Scope lists was resurrected (no `IApplicationBuilder` extension, no middleware/TLS/XFF/path-base, no `/probe.aspx`, no `HostRunner`, no serializer converter, no Scalar, no NWebsec, no auth implementation, no `Microsoft.Extensions.Azure`) and both De-NIHDI rows are closed (KeyVault name, Storage account).
- [ ] Docs review: `src/Test/WasmTestProject/README.md` demo-table row accurate; the package README consistent with as-built behavior (incl. every planner-mechanic deviation the executor reported at Gates 1–3).
- [ ] User approved — deliverable #4 done; project-manager flips the ROADMAP row to ✅.
