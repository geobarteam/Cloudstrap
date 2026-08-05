# Plan: 5-WebApiBootstrap — A consumer calls `AddCloudstrapWebApi` + `UseCloudstrapWebApi` and gets a versioned, documented, hardened Web API — with optional `AddCloudstrapJwtBearer` — in under ten lines of `Program.cs`

## Overview

Deliverable #5 of the extraction roadmap: the new `Cloudstrap.WebApi` package. **Binding spec: `_specs/5-WebApiBootstrap.md`** (approved 2026-08-02, zero Open Questions). Its Port Decision Table (**4 Port · 13 Redesign · 3 Replace · 10 Drop · 6 Superseded-reuse**), Public API Sketch, Behaviors & Conventions table, Dependencies table, Deliberate Behavior Changes, Edge Cases, Out of Scope list and Decision Log (**D-1** OpenAPI stack · **D-2** JWT hardened defaults · **D-3** composite pipeline) are authoritative and are not re-litigated here. Nothing the spec marked Drop appears in this plan: no NSwag anywhere, no `SwaggerBootstrapper`, no `Cloudstrap:Swagger` section, no `UrlHelper`/`AddLegacyIssuer`, no `NormalizedQueryStringApiVersionReader`, no WebApi `CorrelationMiddleware`, no `AddWebOptions`, no `DictionaryTKeyEnumTValueConverter`, no `EnableDebug` event wiring, no `/probe.aspx`, no DataProtection or KeyVault wiring (that is #4's, called explicitly by the consumer), no OIDC/client-credentials anything (#9/#10).

Reference patterns, all read in full before planning:

- **Repo pattern (deliverables 1–4, verified on disk)**: `src/Cloudstrap.Extensions/Cloudstrap.Extensions.csproj` (Sdk + `TargetFramework` + two packaging properties + metadata block + `FrameworkReference Microsoft.AspNetCore.App` + version-less `PackageReference`s against `src/Directory.Packages.props` + `InternalsVisibleTo` to its own test project only); `src/Test/UnitTest/Cloudstrap.Extensions.Tests/` (NUnit 4 on MTP, host-level fixtures, `Microsoft.AspNetCore.TestHost` for real-HTTP assertions, `PackageSurfaceTests` guard idiom, neutral fixtures `Contoso`/`Catalog`); source-generated `[OptionsValidator]` (`Cloudstrap.Core/ApplicationOptionsValidator.cs`) vs. hand-written conditional validator (`Cloudstrap.Extensions/KeyVaultOptionsValidator.cs`); plan `_plans/4-ConfigKeyVaultHttpExtensions.md` for slice/step/gate granularity.
- **Shipped seams this package consumes (read in the shipped code, never rebuilt)**: Core `ApplicationOptions` (`PathBase` normalized to `/x`, `WorkloadName`, `SystemName`/`SubsystemName`/`SubsystemType`, `ExceptionHandlerPath` — deliberately *not* consumed here), `HealthChecksOptions`, `CorrelationOptions`, `AddCloudstrapCore()` (idempotent), `ConfigurationValidationException`; Observability `AddCloudstrapCorrelation()` (idempotent; also calls the framework's `AddProblemDetails`), `UseCloudstrapCorrelation()` (`CloudstrapCorrelationMiddleware` — generation, `[CorrelationRequired]`/`[AllowNoCorrelation]`/health exemptions, 400 `application/problem+json`), `CloudstrapHealthCheckTags` (`live`/`ready`); Extensions `MapCloudstrapHealthChecks()` (marker-based idempotence, `AllowAnonymous().ShortCircuit()`, Core-configured paths).
- **Source material (read in the read-only reference repo `D:\source\Nihdi-Core-Configuration\...\src\`)**: `Nihdi.Core.Configuration.WebApi\WebApi\WebApplicationBuilderExtensions.cs` (the entry point + `ConfigureApiVersioning` — the one **Port** of versioning defaults — plus the JWT wrapper and `UseNihdiWebApiMiddleware`/`ConfigureExceptionHandling` whose two broken branches the spec's finding 2 documents), `WebApi\DefaultApiVersionConvention.cs` (the 20-line **Port**), `ExceptionHandlers\WebApiExceptionHandler.cs` + `WebApiExceptionHandlerForDevTst.cs` (collapsed into one handler with an explicit detail switch; the bounded inner-exception walk at depth 5 is the detail payload's shape reference), `Common\Scalar\ServiceCollectionExtensions.cs` + `EndpointRouteBuilderExtensions.cs` (the live `AddOpenApi()` + `MapScalarApiReference()` path and the deliberate no-secret-to-the-browser rule — the `Microsoft.OpenApi` object-model shape reference), `Common\Services\ServiceCollectionExtensions.AddNihdiWebApiProtections` (HSTS 365 d/subdomains/**preload**, and the `AllowAnyOrigin` CORS fallback + wildcard-subdomain support — the insecure default this package removes and the good part it keeps).
- **Demonstration harness (deliverable 25 + #4's demo, verified on disk)**: `src/Test/WasmTestProject/src/Host/Bff/Program.cs`, `Controllers/DiagnosticsController.cs`, `Controllers/DoctorController.cs`, `appsettings.json`, `E2eFixture` (Bff on `http://127.0.0.1:5300`, `CapturedSutOutput`), `Infrastructure/SutProcess.Start(baseUrl, applicationArguments)` (short-lived second instances with config overrides; sets `ASPNETCORE_ENVIRONMENT=Development`), `Infrastructure/PageTestBase` (headless Chromium), and the **17 existing E2E tests across six fixtures** (`HomePageTests` 1 · `DiagnosticsTests` 3 · `HealthAndCorrelationTests` 4 · `DoctorsTests` 3 · `AzureMonitorTests` 3 · `ExtensionsTests` 3).

This is a library deliverable with no database and no UI of its own: the plan-template's endpoint-integration block does not apply literally. Its equivalent here is that **every step's tests boot a real ASP.NET Core pipeline in-process on `Microsoft.AspNetCore.TestHost` (already CPM-pinned, test-only) and assert over real HTTP** — status codes, headers, JSON bodies and OpenAPI documents — plus the mandatory E2E demonstration slice (Step 11). No live IdP anywhere (founding AC-A1's Keycloak-container test belongs to #10); JWT tests issue their own tokens locally.

### Two verified SUT constraints this plan handles explicitly (not implementation-time discoveries)

1. **The Bff is a BFF, not a plain Web API host.** `src/Test/WasmTestProject/src/Host/Bff/Program.cs` today calls `UseBlazorFrameworkFiles()`, `UseStaticFiles()`, `UseRouting()`, `UseCloudstrapCorrelation()`, `MapControllers()`, `MapCloudstrapHealthChecks()` and `MapFallbackToFile("index.html")`, and it already demonstrates #1–#4 (`GetCloudstrapOptions`, `CloudstrapBootstrapLogger`, `UseCloudstrapObservability().AddAzureMonitor(...)`, `AddCloudstrapHttpServiceClient<ISelfApiClient, SelfApiClient>("SelfApi")`, the `self` health check, `MapCloudstrapHealthChecks`). The conversion in Step 11 **preserves every one of those demonstrations** and routes the SPA composition through D-3's hook points — `BeforeRouting` carries `UseBlazorFrameworkFiles()` + `UseStaticFiles()`, `ConfigureEndpoints` carries `MapFallbackToFile("index.html")` — so the composite cannot swallow the static-file branches or the SPA fallback. The same composition is proven **at unit level in Step 2** (`PipelineCompositionTests`), five steps before the SUT touches it, so the risk surfaces early.
2. **All 17 existing E2E tests call the app anonymously, and that stays true by design.** The SUT deliberately does **not** call `AddCloudstrapJwtBearer` — that is exactly AC-W10's scenario (pipeline never assumes auth exists), and the JWT surface is proven by Step 7–9 unit tests instead. `AddCloudstrapJwtBearer`'s SUT demonstration belongs to #9/#10, which bring an IdP to demonstrate against. "**All 17 pre-existing E2E tests still green, unchanged**" is a verification line in Step 11 and a checklist item at the final gate.

### AC coverage map (every criterion claimed by at least one step)

| Criterion | Step(s) |
|---|---|
| AC-W1 (versioned endpoint responds, `api-supported-versions`, lowercase URLs) | 1 (+ live in 11) |
| AC-W2 (unattributed controller assumes the configured default version) | 1 |
| AC-W3 (one OpenAPI document per discovered version, neutral metadata) | 5 (+ live in 11) |
| AC-W4 (Scalar UI: Development default, explicit `Enabled` wins, no secret representable) | 6 (+ live in 11) |
| AC-W5 (document carries the security scheme; `[AllowAnonymous]` carries none) | 9 |
| AC-W6 (500 problem+json, generic, logged once) | 3 (+ live in 11) |
| AC-W7 (detail mode: type/message/stack/bounded inner chain) | 3 (+ live in 11) |
| AC-W8 (JWT: valid 200 · wrong audience 401 · expired past the 60 s skew 401; HTTPS metadata; `MapInboundClaims=false`) | 7 |
| AC-W9 (fallback policy: 401 · `[AllowAnonymous]` 200 · flag off ⇒ anonymous) | 8 |
| AC-W10 (no `AddCloudstrapJwtBearer` ⇒ anonymous, no auth middleware failures) | 8 (+ live in 11) |
| AC-W11 (`/healthz` + `/ready` via #4, idempotent, stock builder only) | 2 |
| AC-W12 (correlation exactly per #2; no correlation middleware type here) | 2 |
| AC-W13 (security headers always · HSTS outside Development · CORS only when configured) | 4 |
| AC-W14 (build/tests/format, XML docs, metadata, closure: no `NSwag.*`/`Nihdi.*`/`NWebsec.*`/`Aspire.*`, identifier sweep) | 10 |
| AC-W15 (SUT conversion, 17 tests green, ≥ 1 new E2E proving versioned endpoint + Scalar + hardened error) | 11 |
| AC-ASP2 (zero `Aspire.*` in the closure) | 10 |
| AC-A3 (zero `Nihdi.AspNetCore` references) | 7 (the replacement) + 10 (the permanent guard) |

### New CPM entries (`src/Directory.Packages.props` — `CentralPackageTransitivePinningEnabled` is on; the executor verifies the exact stable version on nuget.org at pin time and reports any deviation at the covering gate)

| Package | Version | License | Step |
|---|---|---|---|
| `Asp.Versioning.Mvc` | 10.0.0 (spec-verified 2026-08-02, dotnet org) | MIT | 1 |
| `Asp.Versioning.Mvc.ApiExplorer` | 10.0.0 (same release train) | MIT | 1 |
| `Asp.Versioning.OpenApi` | 10.0.0 (same release train) | MIT | 5 |
| `Microsoft.AspNetCore.OpenApi` | 10.0.x — match the repo's `Microsoft.*` 10.0.10 family | MIT | 5 |
| `Scalar.AspNetCore` | 2.16.16 (spec-verified 2026-08-02) | MIT | 6 |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | 10.0.x — same family | MIT | 7 |
| *(test only, add **only if** the transitive compile reference does not flow through the project reference)* `Microsoft.IdentityModel.JsonWebTokens` | the version resolved by the JwtBearer pin | MIT | 7 |

`Microsoft.AspNetCore.TestHost` (10.0.10) is already pinned by #4 and is referenced by the new test project from Step 1. Transitive pinning may surface version conflicts when these packages land — the executor resolves them by pinning, never by disabling the setting, and reports at the covering gate.

### ⚠️ Risk areas (spec header; reviewed at the gates named)

- **Auth code — the suite's first shipped authentication surface** (`AddCloudstrapJwtBearer`, the four D-2 hardened defaults, the fallback policy): **Gate 4**, a dedicated gate at the end of the auth slice. The *defaults* are signed off (D-2); the *implementation* gets explicit human review.
- **Public API one-way doors**: the `AddCloudstrapWebApi`/`UseCloudstrapWebApi` pair, `CloudstrapWebApiConfigurator` and `WebApiPipelineOptions` are **the pipeline pattern #6 (Mvc) and #7 (Worker) inherit** (D-3) — **Gate 1**; the four owned configuration sections (`Cloudstrap:WebApi`, `Cloudstrap:OpenApi`, `Cloudstrap:Scalar`, `Cloudstrap:JwtBearer`) are permanent surface — **Gates 1–4**; the error-response contract is public behavior — **Gate 2**.
- **New external dependencies**: `Asp.Versioning.*` trio (**Gates 1, 3**), `Microsoft.AspNetCore.OpenApi` + `Scalar.AspNetCore` (**Gate 3**), `Microsoft.AspNetCore.Authentication.JwtBearer` (**Gate 4**) — all MIT, all CPM-pinned; the closure must stay free of `NSwag.*` (AC-W14).
- **Third `Microsoft.AspNetCore.App` framework reference** in the suite (after #2, #4) — **Gate 1**, where the csproj is created.
- **Aspire overlap**: health registration stays additive on the stock `IHealthChecksBuilder` and re-uses #4's idempotent mapping (AC-ASP3 posture); zero `Aspire.*` (AC-ASP2) — **Gates 1 and 5**.

### Planner mechanics decided here (no spec conflict; each flagged for review at the named gate)

**(a) Validators.** Source-generated `[OptionsValidator]` for pure attribute rules — `CloudstrapJwtBearerOptions` (`[Required] Authority`, `[Required] Audience`), per inherited fact #1 (no `Microsoft.Extensions.Options.DataAnnotations`). Hand-written `internal sealed IValidateOptions<T>` (Core's `OpenTelemetryOptionsValidator` / Extensions' `KeyVaultOptionsValidator` precedent) where rules are conditional or parse-shaped: `WebApiOptionsValidator` (`ApiVersioning:DefaultVersion` must parse via `ApiVersion.TryParse`; `Hsts:MaxAgeDays` must be > 0) and `CloudstrapScalarOptionsValidator` (the cross-section "UI over no documents" rule, reading `Cloudstrap:OpenApi` through a ctor-injected `IConfiguration`). Every failure message names the full `Cloudstrap:` key. *(Gates 1, 3, 4.)*

**(b) One environment-default helper.** Three settings are `bool?` meaning "explicit wins, null follows the environment": `WebApi:ExceptionHandling:IncludeDetails` (null → Development), `Scalar:Enabled` (null → Development), `JwtBearer:RequireHttpsMetadata` (null → everywhere except Development). They resolve through one internal `EnvironmentDefault` helper so the rule is written and tested once. *(Gate 2.)*

**(c) `UseCloudstrapWebApi` runs once.** An `app.Properties` marker key guards it; a second call throws `InvalidOperationException` naming the method (spec edge case: a pipeline is built once). Service-side registrations stay repeat-safe (`TryAdd*`/`AddOptions` semantics), matching Core/Observability/Extensions. *(Gate 1.)*

**(d) Auth middleware is wired when the container has an `IAuthenticationSchemeProvider`** — i.e. exactly when someone called `AddAuthentication`, whether that is Cloudstrap's `AddCloudstrapJwtBearer` or the consumer's own scheme. When nobody did, neither `UseAuthentication` nor `UseAuthorization` is added and every endpoint is anonymous (AC-W10). Because the composite calls both explicitly, minimal hosting's automatic auth middleware never inserts a second copy (it checks the same `app.Properties` keys). This is a deliberate superset of the spec sketch's "only when `AddCloudstrapJwtBearer` was called" — it makes the composite usable by consumers who bring their own scheme instead of silently skipping their middleware. *(Gate 4 — confirm or direct a change to the strict reading.)*

**(e) The documentation endpoints are anonymous.** `MapOpenApi()` and the Scalar mapping are mapped with `.AllowAnonymous()`, so the D-2 require-authenticated fallback policy cannot lock the API description out of the reference UI that exists to read it (a Scalar page that 401s before the user can enter a token is a dead feature). The escape hatches are documented, not new API: `Cloudstrap:OpenApi:Enabled=false` (typical production posture), `Cloudstrap:Scalar:Enabled` unset (Development-only default), or mapping the documents yourself through `hooks.ConfigureEndpoints`. **No options property is added** — the spec's Public API Sketch stays exact. *(Gate 4 — a one-line flip either way if the user prefers protected documents.)*

**(f) The problem-details payload carries the ambient correlation id.** When `ICorrelationContextAccessor.CorrelationId` is set, the handler adds it as a problem-details extension (`correlationId`) in **both** detail modes, so a caller can quote it in a support request; nothing else is added beyond the spec'd payload (generic title/status by default; type/message/stack/bounded inner chain in detail mode). *(Gate 2 — drop it there if unwanted.)*

**(g) Test strategy.** Every step boots a real pipeline: `WebApplication.CreateBuilder` + `builder.WebHost.UseTestServer()` + in-memory `Cloudstrap:` configuration + `app.GetTestClient()`, asserting over real HTTP. Test controllers live in the test assembly and are discovered through the documented `configurator.Mvc` hook (`mvc => mvc.AddApplicationPart(typeof(...).Assembly)`) — which doubles as that hook's own proof. JWT tests issue tokens locally (symmetric signing key) and inject the key plus a pre-seeded `OpenIdConnectConfiguration` through the `Action<JwtBearerOptions>` hook, so the configured `Authority` (`https://idp.contoso.example/`) is never contacted. Fixtures are neutral (`Contoso`, `Catalog`, `Widgets`, `example.com`).

**(h) `InternalsVisibleTo` to `Cloudstrap.WebApi.Tests` only** (Extensions precedent), so the internal convention, exception handler, transformers and helpers are directly testable. No cross-package IVT.

**(i) Library-API confirmations the executor makes during RED and reports at the covering gate.** The plan states the *outcome to hit*, not a guessed call name, for three surfaces that cannot be verified offline at planning time:
   1. `Asp.Versioning.OpenApi` 10.0.0 — the wiring that yields **one document per discovered API version** with sunset/deprecation policies (the .NET 10 announcement describes a `WithDocumentPerVersion()`-style hook over the API-explorer/OpenAPI integration). Outcome: `/openapi/v1.json` and `/openapi/v2.json` exist and each contains only its version's operations, with **no hand-written version-filter transformer** in this package (finding 8 — if a bespoke filter turns out to be unavoidable, that is a deviation to raise at Gate 3, not to absorb silently).
   2. `Microsoft.OpenApi` 2.x object model for security metadata — the read source `AddScalarNihdi` pins the shape (`document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme { Type = SecuritySchemeType.OAuth2, Flows = …, Scheme = "bearer", BearerFormat = … }` against an `IDictionary<string, IOpenApiSecurityScheme>`); the per-operation requirement uses that model's scheme-reference type. Confirm in RED (Step 9).
   3. `Scalar.AspNetCore` 2.16.x — the `MapScalarApiReference` overload that accepts the route/pattern, and the `ScalarOptions` members used for the OAuth client id + selected scopes and for listing multiple version documents. Confirm in RED (Step 6).

**(j) The Scalar E2E assertion is shell-based.** The load-bearing assertion is an `HttpClient` one (200 + `text/html` + the shell references the v1 document); the Playwright navigation asserts the page loads and its title. The zero-console-errors assertion used by `HomePageTests` is deliberately **not** applied to the Scalar page, because the reference UI pulls its JavaScript bundle from a CDN and CI agents may be offline. *(Gate 5.)*

**(k) Collection settings append to defaults** (inherited fact #2): `WebApi:Cors:AllowedOrigins`, `OpenApi:OAuth:Scopes` and `Scalar:OAuth:SelectedScopes` are get-only initialized, so configured values append rather than replace. All three ship **empty** defaults precisely so the caveat stays theoretical; XML docs and the package README (Step 10) document it anyway.

**Canonical middleware order** (spec Public API Sketch, established in Step 2 and never re-ordered afterwards — later steps only fill their reserved slot):

```
exception handler → UseHsts (non-Development, when enabled) → security headers → UsePathBase (when
ApplicationOptions.PathBase is non-empty) → hooks.BeforeRouting → UseRouting → UseCors (only when
origins are configured) → UseCloudstrapCorrelation (#2) → UseAuthentication (only when a scheme
provider exists) → hooks.BeforeAuthorization → UseAuthorization (same condition) →
hooks.BeforeEndpoints → MapControllers (when hooks.MapControllers) → MapCloudstrapHealthChecks (#4)
→ MapOpenApi + Scalar (per options) → hooks.ConfigureEndpoints
```

> **Gate 1 decision (user, 2026-08-03) — correlation moved ahead of authentication.** The spec's order put correlation *after* authorization, which would leave a fallback-policy `401` uncorrelated in the logs and without a `correlationId` in its problem-details body. Decided: correlation runs immediately after `UseRouting` (endpoint metadata is visible, so `[CorrelationRequired]`/`[AllowNoCorrelation]`/health exemptions still work) and after `UseCors` (so a preflight is answered by the CORS middleware and can never be rejected for lacking a correlation header), but **before** `UseAuthentication`. Pinned by `PipelineCompositionTests.Use_EstablishesCorrelationBeforeTheAuthorizationSlot`. This supersedes the spec's Public API Sketch ordering for #5, and is the order #6 and #7 inherit.

### Gate 1 decisions (user, 2026-08-03) — binding on every later step

1. **Path base stays stock `UsePathBase`.** A configured `Cloudstrap:Application:PathBase` strips a matching prefix and re-applies it to generated links; a request *without* the prefix passes through and still routes. The `app.Map(pathBase, …)` branching alternative (which would 404 un-prefixed requests) was considered and rejected — it diverges from the spec's Port Decision Table row and from how an ingress-stripped deployment behaves. Step 2's test asserts the two properties that actually hold: the prefixed route is served **and** generated links carry the prefix; with no path base configured, the prefixed route 404s.
2. **Correlation before authentication** — see the note above the canonical order.
3. **`AddCloudstrapWebApi` reads `Cloudstrap:WebApi` eagerly** from `builder.Configuration` at registration time (the #4 `AddCloudstrapKeyVault`/`AddCloudstrapDataProtection` precedent) and uses that instance for the versioning, routing and JSON wiring; `AddOptions<WebApiOptions>().BindConfiguration().ValidateOnStart()` still provides DI resolution and startup validation. Consequence, to be documented in the Step 10 README: a configuration source added *after* `AddCloudstrapWebApi` does not affect those defaults, so `AddCloudstrapKeyVault` is called first. The same eager pattern applies to the options added in Steps 3–7.
4. **CPM pins take the current stable of the named release train**, reported at each covering gate: `Asp.Versioning.Mvc`/`.Mvc.ApiExplorer` **10.0.1** (plan said 10.0.0); `Scalar.AspNetCore` is expected to pin **2.16.17** (plan said 2.16.16) at Step 6.

---

## Slice 1 — One `Add`/`Use` pair serves a versioned API that composes the shipped Cloudstrap seams and the consumer's own middleware

---

## Step 1 — Two calls in `Program.cs` and a versioned controller answers: default version assumed, supported versions reported, lowercase URLs, null-free JSON (AC-W1, AC-W2)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.WebApi/Cloudstrap.WebApi.csproj` *(create)* — Sdk project, `TargetFramework=net10.0`, `GeneratePackageOnBuild=true`, `GenerateDocumentationFile=true`; `<FrameworkReference Include="Microsoft.AspNetCore.App" />` (the suite's **third**); `<ProjectReference>` to `..\Cloudstrap.Core\`, `..\Cloudstrap.Observability\` and `..\Cloudstrap.Extensions\`; `<PackageReference>` `Asp.Versioning.Mvc`, `Asp.Versioning.Mvc.ApiExplorer`; `<InternalsVisibleTo Include="Cloudstrap.WebApi.Tests" />` (mechanic (h)). Description/tags/README metadata land in Step 10.
- `src/Cloudstrap.WebApi/WebApiOptions.cs` *(create)* — `public sealed`, `const string SectionName = "Cloudstrap:WebApi"`; this step's members: `ApiVersioning : ApiVersioningSettings`, `Json : JsonSettings`, `LowercaseUrls : bool = true` (`Cors`/`Hsts` arrive in Step 4, `ExceptionHandling` in Step 3).
- `src/Cloudstrap.WebApi/ApiVersioningSettings.cs` *(create)* — `DefaultVersion : string = "1.0"`, `AssumeDefaultVersionWhenUnspecified : bool = true`, `ReportApiVersions : bool = true`.
- `src/Cloudstrap.WebApi/JsonSettings.cs` *(create)* — `IgnoreNullValues : bool = true`.
- `src/Cloudstrap.WebApi/WebApiOptionsValidator.cs` *(create)* — `internal sealed : IValidateOptions<WebApiOptions>`, mechanic (a): `ApiVersion.TryParse(DefaultVersion)` failure names `Cloudstrap:WebApi:ApiVersioning:DefaultVersion`.
- `src/Cloudstrap.WebApi/CloudstrapWebApiConfigurator.cs` *(create)* — code-level hooks carried by `AddCloudstrapWebApi`; this step: `ApiVersioning : Action<ApiVersioningOptions>?`, `Json : Action<JsonOptions>?`, `Mvc : Action<IMvcBuilder>?` (`OpenApi` and `Scalar` arrive in Steps 5/6).
- `src/Cloudstrap.WebApi/DefaultApiVersionConvention.cs` *(create)* — the **Port**: `internal sealed : IControllerConvention` assigning the default version to controllers carrying neither `IApiVersionProvider` nor `IApiVersionNeutral` attributes. Re-typed neutral (no company header), full XML docs.
- `src/Cloudstrap.WebApi/ApiVersioningRegistration.cs` *(create)* — `internal static`: the ported `ConfigureApiVersioning` defaults (`DefaultApiVersion`, `AssumeDefaultVersionWhenUnspecified`, `ReportApiVersions`, readers = query string + URL segment, `VersionByNamespaceConvention` + `DefaultApiVersionConvention`, ApiExplorer `GroupNameFormat = "'v'VVV"` + `SubstituteApiVersionInUrl = true`), with the consumer hook invoked last. **No** `NormalizedQueryStringApiVersionReader` (Drop).
- `src/Cloudstrap.WebApi/WebApplicationBuilderExtensions.cs` *(create)* — `public static AddCloudstrapWebApi(this WebApplicationBuilder builder, Action<CloudstrapWebApiConfigurator>? configure = null) : WebApplicationBuilder`.
- `src/Cloudstrap.WebApi/WebApplicationExtensions.cs` *(create)* — `public static UseCloudstrapWebApi(this WebApplication app, Action<WebApiPipelineOptions>? configure = null) : WebApplication`; this step wires `UseRouting()` + `MapControllers()` only — the reserved slots fill in Steps 2–9 in the canonical order above.
- `src/Cloudstrap.WebApi/WebApiPipelineOptions.cs` *(create)* — `BeforeRouting`, `BeforeAuthorization`, `BeforeEndpoints` (`Action<IApplicationBuilder>?`), `ConfigureEndpoints` (`Action<IEndpointRouteBuilder>?`), `MapControllers : bool = true`. Declared in full here (it is the D-3 shape); honored from Step 2.
- `src/Test/UnitTest/Cloudstrap.WebApi.Tests/Cloudstrap.WebApi.Tests.csproj` *(create)* — mirror of `Cloudstrap.Extensions.Tests.csproj`: `net10.0`, `<ProjectReference>` to the package, version-less `<PackageReference>`s `Microsoft.AspNetCore.TestHost`, `Microsoft.Extensions.Configuration`, `Microsoft.Extensions.Configuration.Binder`, `Microsoft.Extensions.Hosting`.
- `src/Test/UnitTest/Cloudstrap.WebApi.Tests/Infrastructure/WebApiTestHost.cs` *(create)* — mechanic (g) fixture helper: builds a `TestServer`-hosted app from an in-memory `Cloudstrap:` dictionary (valid `Application` values) plus optional configurator/pipeline actions, and returns the `HttpClient`.
- `src/Test/UnitTest/Cloudstrap.WebApi.Tests/Infrastructure/TestControllers.cs` *(create)* — neutral fixtures: `WidgetsController` (`[ApiVersion("1.0")]`, `[ApiVersion("2.0")]`, route `api/widgets`, actions mapped per version), `VersionedWidgetsController` (route `api/v{version:apiVersion}/widgets`), `LegacyController` (no version attribute, route `api/legacy`), `PayloadController` (returns a DTO with a null property), `LinkController` (returns a generated link) + their DTOs.
- `src/Test/UnitTest/Cloudstrap.WebApi.Tests/VersionedRoutingTests.cs` *(create)*
- `src/Test/UnitTest/Cloudstrap.WebApi.Tests/WebApiDefaultsTests.cs` *(create)*
- `src/Cloudstrap.sln` *(modify)* — package at the solution root, test project under the `Test\UnitTest` solution folder (same nesting/GUID pattern as the existing four).
- `src/Directory.Packages.props` *(modify)* — pin `Asp.Versioning.Mvc` + `Asp.Versioning.Mvc.ApiExplorer` 10.0.0.

**RED** *(write these tests first; for a brand-new project the honest first failure is the test project failing to compile against missing types — the plan-4 precedent — followed by real red runs once the types exist)*:
- Unit test file: `VersionedRoutingTests.cs`
  - `AddAndUse_VersionedController_ServesTheEndpointAndReportsSupportedVersions` — `GET /api/v1/widgets` → 200 and the response carries `api-supported-versions` containing `1.0` (AC-W1).
  - `AddAndUse_QueryStringVersion_SelectsTheRequestedVersion` — `GET /api/widgets?api-version=2.0` returns the v2 payload while `?api-version=1.0` returns the v1 payload (the two stock readers, both live).
  - `AddAndUse_UnattributedController_AssumesTheDefaultVersion` — `GET /api/legacy` (no version anywhere) → 200 (the ported `DefaultApiVersionConvention` + `AssumeDefaultVersionWhenUnspecified`, AC-W2).
  - `AddAndUse_WithConfiguredDefaultVersion_AssumesThatOne` — `Cloudstrap:WebApi:ApiVersioning:DefaultVersion=2.0` → the unattributed controller's response reports `api-supported-versions: 2.0` (finding 5: the default version comes from `Cloudstrap:WebApi`, never from documentation settings).
  - `AddAndUse_WithReportApiVersionsFalse_OmitsTheHeader` — override proof.
  - `AddAndUse_UnsupportedVersion_Returns400ProblemDetails` — `?api-version=9.0` → 400 `application/problem+json` (spec edge case: Asp.Versioning's stock response, not customized).
  - `AddCloudstrapWebApi_InvalidDefaultVersion_FailsStartupNamingTheKey` — `DefaultVersion=abc` → `StartAsync` throws with a message containing `Cloudstrap:WebApi:ApiVersioning:DefaultVersion`.
  - `AddCloudstrapWebApi_VersioningHook_RunsAfterCloudstrapDefaults` — the hook adds a header reader; a request versioned through that header succeeds (hooks have final say).
- Unit test file: `WebApiDefaultsTests.cs`
  - `Response_OmitsNullProperties_ByDefault` — the payload DTO's null property is absent from the JSON; with `Cloudstrap:WebApi:Json:IgnoreNullValues=false` the key is present with `null` (the ported `WhenWritingNull` opinion + its override).
  - `GeneratedLinks_AreLowercase_ByDefault` — the link-generating action returns a lowercase path for a mixed-case action/controller name; with `Cloudstrap:WebApi:LowercaseUrls=false` the generated path keeps its casing.
  - `AddCloudstrapWebApi_JsonHook_WinsOverCloudstrapDefaults` — the hook sets a property naming policy and the response reflects it.
  - `AddCloudstrapWebApi_MvcHook_RunsAndCanAddApplicationParts` — asserted implicitly by every fixture (test controllers are only discoverable through it) and explicitly by a controller added through a second application part.
  - `AddCloudstrapWebApi_OnNullBuilder_ThrowsArgumentNullException` / `UseCloudstrapWebApi_OnNullApp_ThrowsArgumentNullException` (guard clauses).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln   # RED = the new test project fails to compile against missing types
  src\Test\UnitTest\Cloudstrap.WebApi.Tests\bin\Debug\net10.0\Cloudstrap.WebApi.Tests.exe --filter "VersionedRoutingTests"
  ```

**GREEN**:
- `AddCloudstrapWebApi` — guards; `services.AddCloudstrapCore()` and `AddCloudstrapCorrelation()` (both idempotent, so one call is self-sufficient — #4's precedent); bind + `ValidateOnStart` `WebApiOptions` with `WebApiOptionsValidator`; `Configure<RouteOptions>` from `LowercaseUrls` (URLs **and** query strings); `AddControllers().AddJsonOptions(...)` applying `IgnoreNullValues` → `JsonIgnoreCondition.WhenWritingNull` and then the `Json` hook; `ApiVersioningRegistration.Configure(...)`; invoke `configurator.Mvc` last; return the builder. **Registers no authentication** (that is `AddCloudstrapJwtBearer`, Step 7).
- `UseCloudstrapWebApi` — guards; `app.UseRouting()`; `app.MapControllers()`; return the app. (The remaining slots arrive in Steps 2–9; the XML docs already state the canonical order so they do not drift.)
- `DefaultApiVersionConvention` + `ApiVersioningRegistration` per Scope.
- Full XML docs on every public member: the two entry points, the configurator, the pipeline options, the options types (naming the exact configuration keys and the append-to-defaults caveat where relevant).

**DB changes**: none — this repository has no database.

**VERIFY** *(when all green, mark this step's `Done` checkbox and continue straight to the next step)*:
1. Test exe → all pass: an app with two Cloudstrap calls now serves versioned controllers, assumes the configured default version for unattributed ones, reports supported versions, generates lowercase links and omits null JSON properties — none of which existed before (AC-W1, AC-W2).
2. `dotnet build src/Cloudstrap.sln` → zero warnings/errors; full `runTests` green (Core, Observability, AzureMonitor, Extensions, E2E untouched); `dotnet format src/Cloudstrap.sln --verify-no-changes` → exit 0.
3. `dotnet build src/Cloudstrap.sln -c Release` → a `Cloudstrap.WebApi.*.nupkg` appears under `src/Cloudstrap.WebApi/bin/Release/` (packable from day one; metadata completed in Step 10).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 2 — The one pipeline call composes everything else: probes and correlation from the shipped packages, the configured path base, and the consumer's own middleware at four documented hook points ⚠️ *(Risk Area: this is the D-3 shape #6 and #7 inherit — AC-W11, AC-W12)*

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.WebApi/WebApplicationExtensions.cs` *(modify)* — fill the pipeline to the canonical order minus the slots owned by Steps 3–9: `UsePathBase` (only when `ApplicationOptions.PathBase` is non-empty — no env var, no `/{WorkloadName}` magic) → `hooks.BeforeRouting` → `UseRouting` → *(CORS/auth slots reserved)* → `hooks.BeforeAuthorization` → `UseCloudstrapCorrelation()` → `hooks.BeforeEndpoints` → `MapControllers()` when `hooks.MapControllers` → `MapCloudstrapHealthChecks()` → `hooks.ConfigureEndpoints`; plus the run-once guard (mechanic (c)).
- `src/Cloudstrap.WebApi/WebApplicationBuilderExtensions.cs` *(modify)* — `AddCloudstrapWebApi` also calls the stock `services.AddHealthChecks()` (additive, Aspire posture) so the probes always answer, and `AddHttpContextAccessor()`.
- `src/Test/UnitTest/Cloudstrap.WebApi.Tests/PipelineCompositionTests.cs` *(create)*
- `src/Test/UnitTest/Cloudstrap.WebApi.Tests/Infrastructure/TestControllers.cs` *(modify)* — add a `[CorrelationRequired]` action and an action echoing `ICorrelationContextAccessor.CorrelationId`.

**RED** *(write these tests first, run them, confirm they fail — all assertions are real HTTP through `app.GetTestClient()`; hook ordering is observed by having each hook insert a middleware that appends its name to a request-scoped list that a test endpoint echoes)*:
- Unit test file: `PipelineCompositionTests.cs`
  - `Use_ServesLivenessAndReadinessProbes` — `/healthz` = 200 with a healthy `live`-tagged check while an unhealthy `ready`-tagged check keeps `/ready` at 503: #4's mapping and #2's tag contract are live inside this pipeline, and this package maps nothing of its own (AC-W11).
  - `Use_WithExplicitMapCloudstrapHealthChecksCall_DoesNotDuplicateEndpoints` — calling `app.MapCloudstrapHealthChecks()` again afterwards leaves `/healthz` at 200 with no `AmbiguousMatchException` (AC-W11 idempotence clause).
  - `Use_FlowsInboundCorrelationId` — request with `X-Correlation-ID` → the echo action returns exactly that id (AC-W12 flow half).
  - `Use_OnCorrelationRequiredEndpointWithoutHeader_Returns400ProblemDetails` — 400 `application/problem+json` naming the configured header (AC-W12 enforcement half, produced by #2's middleware).
  - `WebApiAssembly_DeclaresNoCorrelationMiddlewareOfItsOwn` — reflection over the package assembly finds no type whose name contains `Correlation` (AC-W12's "no correlation middleware type of its own"; the WebApi `CorrelationMiddleware` is Dropped).
  - `Use_WithConfiguredPathBase_ServesUnderIt` — `Cloudstrap:Application:PathBase=myapi` → `/myapi/api/v1/widgets` 200 and `/api/v1/widgets` 404; with no `PathBase` configured the root route works and nothing is prefixed (no path-base magic — Deliberate Behavior Change 8).
  - `Use_Hooks_RunInTheDocumentedOrder` — the echoed list is exactly `BeforeRouting`, `BeforeAuthorization`, `BeforeEndpoints`, and an endpoint mapped through `ConfigureEndpoints` responds 200.
  - `Use_WithMapControllersFalse_MapsNoControllersButKeepsProbesAndHooks` — controller route 404, `/healthz` 200, the `ConfigureEndpoints` endpoint 200 (spec edge case: minimal-API-only consumers).
  - `Use_CalledTwice_ThrowsInvalidOperationException` — message names `UseCloudstrapWebApi` (spec edge case).
  - `Use_ComposesAroundStaticFilesAndASpaFallback` — **the SUT-shaped composition, proven five steps early**: `BeforeRouting` adds a static-file-style middleware serving `/app.css`, `ConfigureEndpoints` maps a fallback returning `index-stub`; assert (a) `/api/v1/widgets` reaches the controller, (b) `/healthz` reaches the probe, (c) `/app.css` is served by the hook middleware, (d) an unknown path reaches the fallback. The composite swallows neither branch (spec edge case "Bff SUT composition").
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.WebApi.Tests\bin\Debug\net10.0\Cloudstrap.WebApi.Tests.exe --filter "PipelineCompositionTests"
  ```

**GREEN**: the pipeline per Scope, in the canonical order, with the run-once marker. XML docs on `UseCloudstrapWebApi` spell out the full order (including the reserved exception-handler/HSTS/security-header/CORS/auth/OpenAPI slots), state that the granular pieces stay independently callable as the escape hatch (`AddCloudstrapJwtBearer`, `MapCloudstrapHealthChecks`, `UseCloudstrapCorrelation`), and document the SPA composition idiom used by the SUT.

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: one call now yields the whole request pipeline — standard probes, correlation exactly per #2, the configured path base, four working hook points, a `MapControllers` switch, a run-once guard, and provable coexistence with static files and a SPA fallback (AC-W11, AC-W12).
2. `dotnet build src/Cloudstrap.sln` → zero warnings; full `runTests` green; `dotnet format src/Cloudstrap.sln --verify-no-changes` → exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 1: the entry-point pair and the pipeline shape are frozen *(covers Steps 1–2)*

*Executor: STOP here. Present the results of all covered steps and WAIT for user approval — do not start the next step.*

⚠️ **Risk areas at this gate**: **public API one-way door** — `AddCloudstrapWebApi`, `UseCloudstrapWebApi`, `CloudstrapWebApiConfigurator` and `WebApiPipelineOptions` are **the pattern deliverables #6 (Mvc) and #7 (Worker) inherit** (D-3); review the signatures against the spec's Public API Sketch verbatim, and the middleware order against the canonical list in this plan's Overview — a defect here propagates to two more packages · the **third `Microsoft.AspNetCore.App` framework reference** in the suite (csproj created in Step 1; the runtime-image consequence goes in the README at Step 10) · the first two `Asp.Versioning.*` dependencies (MIT, dotnet org, CPM-pinned) · **the correlation-after-authorization ordering note** in the Overview — confirm the spec's order or direct the move · mechanic (c)'s run-once semantics (`Use` throws, `Add` stays repeat-safe) and mechanic (a)'s validator split.

- [x] Behavioral verification: test exe output shows — versioned routing over both stock readers, the assumed default version for unattributed controllers incl. the configured-default proof, `api-supported-versions` reported and suppressible, the stock 400 for an unsupported version, invalid-default fail-fast naming the key, and the JSON/lowercase-URL defaults with their overrides (Step 1); probes served through #4 with the tag contract live and idempotent, correlation flow + `[CorrelationRequired]` 400 through #2, no correlation type of our own, path base on/off, the four hooks in documented order, the `MapControllers` switch, the double-call throw, and the static-files-plus-SPA-fallback composition (Step 2). *(32 tests in `Cloudstrap.WebApi.Tests`, 311 solution-wide, all green.)*
- [x] Code review: entry-point/configurator/pipeline-options signatures vs the spec sketch, verbatim; the ported `DefaultApiVersionConvention` and versioning defaults vs the source (neutral, no company header, no `NormalizedQueryStringApiVersionReader`, no `UrlHelper`); `internal` by default + sealed + full XML docs; `dotnet list src/Cloudstrap.WebApi/Cloudstrap.WebApi.csproj package` → the two `Asp.Versioning.*` packages and the three project references only (no OpenAPI/Scalar/auth package yet, zero `Aspire.*`, zero `NSwag.*`).
- [x] User approved — implementation may continue past this gate *(2026-08-03, together with the four Gate 1 decisions recorded in the Overview)*

---

## Slice 2 — The API is safe at the edge: unhandled exceptions never leak, and every response carries the right hardening headers

---

## Step 3 — An endpoint throws and the caller gets RFC 9457 problem details: generic in production, fully diagnosable when details are switched on, logged exactly once either way (AC-W6, AC-W7)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.WebApi/ExceptionHandlingSettings.cs` *(create)* — `IncludeDetails : bool? = null` (null → details in `Development` only).
- `src/Cloudstrap.WebApi/WebApiOptions.cs` *(modify)* — add `ExceptionHandling : ExceptionHandlingSettings`.
- `src/Cloudstrap.WebApi/EnvironmentDefault.cs` *(create)* — `internal static` mechanic (b): `Resolve(bool? explicitValue, bool environmentDefault)` plus the three documented rules in one place.
- `src/Cloudstrap.WebApi/CloudstrapWebApiExceptionHandler.cs` *(create)* — `internal sealed : IExceptionHandler`: logs the exception once (`LogError`), writes `500` `application/problem+json` through `IProblemDetailsService` with a generic title; in detail mode adds the exception type, message, stack trace and a **depth-bounded inner-exception chain** (source parity: bound of 5) as problem-details extensions; adds the ambient `correlationId` extension when available (mechanic (f)); always returns `true` (terminal fallback). Collapses both source handlers (Redesign rows) — `ApplicationOptions.ExceptionHandlerPath` is **not** consumed (no re-execution).
- `src/Cloudstrap.WebApi/WebApplicationBuilderExtensions.cs` *(modify)* — `AddProblemDetails()` + `AddExceptionHandler<CloudstrapWebApiExceptionHandler>()` registered **after** any consumer handler already in the collection (registration order gives the consumer's handler first crack — the source's `configureExceptionHandlers` contract preserved by ordering, no extra parameter).
- `src/Cloudstrap.WebApi/WebApplicationExtensions.cs` *(modify)* — `UseExceptionHandler()` as the **first** middleware, in every environment (no `UseDeveloperExceptionPage`, no environment structural switch — Deliberate Behavior Change 1).
- `src/Test/UnitTest/Cloudstrap.WebApi.Tests/ProblemDetailsExceptionTests.cs` *(create)*
- `src/Test/UnitTest/Cloudstrap.WebApi.Tests/Infrastructure/TestControllers.cs` *(modify)* — a throwing action (with a nested inner-exception chain deeper than the bound) and an `/error` action that must never be re-executed.
- `src/Test/UnitTest/Cloudstrap.WebApi.Tests/Infrastructure/WebApiTestHost.cs` *(modify)* — environment name parameter + a capturing `ILoggerProvider`.

**RED** *(write these tests first, run them, confirm they fail)*:
- Unit test file: `ProblemDetailsExceptionTests.cs`
  - `Throwing_InProduction_Returns500ProblemJsonWithoutAnyExceptionDetail` — 500, `Content-Type: application/problem+json`, body has `title`/`status`, and contains **none** of the exception type, message or stack text (AC-W6).
  - `Throwing_InProduction_LogsTheExceptionExactlyOnce` — the capturing provider recorded one `Error` entry carrying the exception (AC-W6 logging half).
  - `Throwing_InDevelopment_IncludesTypeMessageAndStackTrace` — the extensions carry the exception type, message and stack trace (AC-W7).
  - `Throwing_InDevelopment_IncludesADepthBoundedInnerChain` — a 8-deep chain surfaces at most 5 levels (source-parity bound, documented).
  - `Throwing_WithIncludeDetailsFalseInDevelopment_ExplicitWins` and `Throwing_WithIncludeDetailsTrueInProduction_ExplicitWins` — the explicit option beats the environment default in both directions (the replacement for the DevTst taxonomy; and the SUT's Step 11 posture).
  - `Throwing_IncludesTheAmbientCorrelationId` — the `correlationId` extension equals the inbound `X-Correlation-ID` (mechanic (f)).
  - `Throwing_WithAConsumerExceptionHandlerRegisteredFirst_TheConsumerHandlerWins` — a handler registered before `AddCloudstrapWebApi` returns `true` → its response is returned and Cloudstrap's payload is absent (spec edge case).
  - `Throwing_WithExceptionHandlerPathConfigured_DoesNotReExecuteIt` — `Cloudstrap:Application:ExceptionHandlerPath=/error` plus a real `/error` endpoint → the response is still the problem-details body, and the `/error` action never ran (the dead re-execution path stays dead by design).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.WebApi.Tests\bin\Debug\net10.0\Cloudstrap.WebApi.Tests.exe --filter "ProblemDetailsExceptionTests"
  ```

**GREEN**: the Scope items. XML docs on `ExceptionHandlingSettings` state the environment default, the two-mode payload, the "never enable details in a public production API" warning, and the consumer-handler ordering contract.

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: unhandled exceptions now produce a platform-standard `application/problem+json` response in every environment — safe by default, fully diagnosable on explicit opt-in, correlated, logged once, and never re-executing a dead `/error` path (AC-W6, AC-W7; the two broken source branches of finding 2 are provably gone).
2. `dotnet build src/Cloudstrap.sln` → zero warnings; full `runTests` green; `dotnet format src/Cloudstrap.sln --verify-no-changes` → exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 4 — Every response is hardened: constant security headers always, HSTS outside Development, and CORS only for origins you actually configured (AC-W13)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.WebApi/CorsSettings.cs` *(create)* — `AllowedOrigins : IList<string>` (get-only initialized, empty default; mechanic (k) documented).
- `src/Cloudstrap.WebApi/HstsSettings.cs` *(create)* — `Enabled : bool = true`, `MaxAgeDays : int = 365`, `IncludeSubDomains : bool = true`, `Preload : bool = false` (Deliberate Behavior Change 7 — a browser-preload-list commitment belongs to the domain owner).
- `src/Cloudstrap.WebApi/WebApiOptions.cs` *(modify)* — add `Cors` and `Hsts`.
- `src/Cloudstrap.WebApi/WebApiOptionsValidator.cs` *(modify)* — `Hsts.MaxAgeDays > 0` when `Hsts.Enabled`, naming `Cloudstrap:WebApi:Hsts:MaxAgeDays`.
- `src/Cloudstrap.WebApi/SecurityHeadersMiddleware.cs` *(create)* — `internal sealed`, ~15 lines: sets `X-Content-Type-Options: nosniff` and `Referrer-Policy: no-referrer` on every response without overwriting a value the app already set. **No NWebsec, no NetEscapades** (#6 re-evaluates a library for HTML surfaces).
- `src/Cloudstrap.WebApi/WebApplicationBuilderExtensions.cs` *(modify)* — `AddHsts(...)` from the options; `AddCors(...)` with a default policy **only when at least one origin is configured** (`WithOrigins` + `AllowCredentials` + `AllowAnyHeader` + `AllowAnyMethod`, and `SetIsOriginAllowedToAllowWildcardSubdomains()` when any origin contains `*`). No origins → **no CORS services and no policy at all** (the source's `AllowAnyOrigin` fallback is gone — Deliberate Behavior Change 3).
- `src/Cloudstrap.WebApi/WebApplicationExtensions.cs` *(modify)* — fill the reserved slots: `UseHsts()` (non-Development, when enabled) and the security-header middleware before the path base; `UseCors()` right after `UseRouting` **only when origins are configured**.
- `src/Test/UnitTest/Cloudstrap.WebApi.Tests/EdgeHardeningTests.cs` *(create)*

**RED** *(write these tests first, run them, confirm they fail — HSTS is only emitted for HTTPS requests, so those cases issue the request against `https://localhost/...` through the TestServer client)*:
- Unit test file: `EdgeHardeningTests.cs`
  - `EveryResponse_CarriesNosniffAndNoReferrer` — an API response **and** a `/healthz` probe response both carry both headers (the middleware sits before routing, so short-circuited probes are covered too).
  - `SecurityHeaders_DoNotOverwriteAValueTheAppAlreadySet` — an action setting `Referrer-Policy` keeps its value.
  - `Hsts_InProductionOverHttps_EmitsStrictTransportSecurityWithoutPreload` — `max-age=31536000; includeSubDomains`, and **no** `preload` token (Deliberate Behavior Change 7).
  - `Hsts_InDevelopment_EmitsNothing` and `Hsts_WithEnabledFalse_EmitsNothing`.
  - `Hsts_WithConfiguredMaxAgeAndPreload_ReflectsThem` — overrides land in the header.
  - `Hsts_WithMaxAgeDaysZero_FailsStartupNamingTheKey`.
  - `Cors_WithNoOriginsConfigured_NeverEmitsAccessControlAllowOrigin` — an `OPTIONS` preflight carrying `Origin` gets no `Access-Control-Allow-Origin` (secure default, finding 4).
  - `Cors_WithConfiguredOrigin_PreflightSucceedsForThatOriginOnly` — the configured origin gets `Access-Control-Allow-Origin` + `Access-Control-Allow-Credentials: true`; a different origin gets neither.
  - `Cors_WithWildcardSubdomainOrigin_AllowsMatchingSubdomains` — `https://*.contoso.example` allows `https://app.contoso.example` and rejects `https://app.fabrikam.example` (the one good source behavior, kept).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.WebApi.Tests\bin\Debug\net10.0\Cloudstrap.WebApi.Tests.exe --filter "EdgeHardeningTests"
  ```

**GREEN**: the Scope items. XML docs: the two header values are constants for a JSON API and the documented override is `hooks.BeforeRouting` (write your own middleware); the CORS row states that Cloudstrap's default policy is additive and that consumers needing named policies use stock `AddCors`/`RequireCors`; `AllowedOrigins` carries the append-to-defaults caveat.

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: every response — API and probe alike — now carries the two constant security headers; HSTS is emitted exactly where it belongs and never claims preload without being told; and CORS is genuinely off until origins are configured, then exact, credentialed and wildcard-subdomain capable (AC-W13; the registered-but-inert source behavior of finding 4 and its insecure fallback are both gone).
2. `dotnet build src/Cloudstrap.sln` → zero warnings; full `runTests` green; `dotnet format src/Cloudstrap.sln --verify-no-changes` → exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 2: the error contract and the edge posture *(covers Steps 3–4)*

*Executor: STOP here. Present the results of all covered steps and WAIT for user approval — do not start the next step.*

⚠️ **Risk areas at this gate**: **the error-response contract is public behavior** — the problem-details shape (both modes), the depth bound and the `correlationId` extension of mechanic (f) become what every consumer's clients parse; confirm or direct changes now · **security defaults** — HSTS without preload (Deliberate Behavior Change 7), two constant headers instead of a security-headers library (rule 4: minimize dependencies — #6 re-evaluates for HTML surfaces), and "no configured origins ⇒ no CORS policy at all" replacing the source's `AllowAnyOrigin` · mechanic (b)'s single environment-default helper (the same rule now governs `IncludeDetails`, and will govern Scalar exposure and HTTPS metadata) · confirm `ApplicationOptions.ExceptionHandlerPath` stays unconsumed here (re-execution is #6's MVC pattern).

- [x] Behavioral verification: test exe output shows — the generic production payload with nothing leaked and exactly one log entry, the Development detail payload with the bounded inner chain, both explicit-override directions, the correlation extension, the consumer-handler-first proof and the never-re-executed `/error` proof (Step 3); both constant headers on API and probe responses incl. the no-overwrite rule, HSTS emitted/withheld per environment and flag with no preload by default, the fail-fast on a zero max age, and the three CORS proofs incl. wildcard subdomains (Step 4). *(56 tests in `Cloudstrap.WebApi.Tests`, 336 solution-wide, all green.)*
- [x] Code review: `CloudstrapWebApiExceptionHandler` against the two collapsed source handlers (one handler, explicit switch, problem details, no bespoke `{StatusCode, Message}` JSON, no environment structural switch); `SecurityHeadersMiddleware` is ~15 lines with no new dependency; the CORS registration has no `AllowAnyOrigin` path anywhere; `dotnet list src/Cloudstrap.WebApi/Cloudstrap.WebApi.csproj package` → unchanged since Gate 1.
- [x] User approved — implementation may continue past this gate *(2026-08-03)*

### Gate 2 finding — fixed in `Cloudstrap.Observability` (#2), under the standing pre-release permission

**The defect.** `ICorrelationContextAccessor` (#2) is backed by `AsyncLocal<string?>`. The exception handler runs in `UseExceptionHandler`, the **first** middleware, so by the time an exception has unwound past `UseCloudstrapCorrelation` the async-local write is no longer visible — async-local values flow down a call chain, never back up. Measured, not assumed: the first implementation of mechanic (f) returned no `correlationId` at all, and the fallback to reading the inbound header still could not recover a *generated* identifier.

**The decision.** The user confirmed on 2026-08-03 that no Cloudstrap package is published yet, so breaking or extending an already-"shipped" package is allowed until they say otherwise. The gap was therefore fixed at its source rather than worked around downstream.

**The fix** (additive, not breaking):
- `Cloudstrap.Observability/Correlation/CloudstrapCorrelationMiddleware.cs` — also stores the established identifier in `HttpContext.Items`, which is request-scoped and survives the unwind.
- `Cloudstrap.Observability/Correlation/HttpContextExtensions.cs` *(new, public)* — `HttpContext.GetCloudstrapCorrelationId()`. Its XML docs steer ordinary application code to `ICorrelationContextAccessor` (which also works in message handlers and background work) and reserve this seam for code holding the `HttpContext` outside the middleware's async scope.
- `Cloudstrap.WebApi/CloudstrapWebApiExceptionHandler.cs` — `ResolveCorrelationId` now tries the stored identifier, then the async-local accessor, then the inbound header.
- Four new tests in `Cloudstrap.Observability.Tests` (stash for inbound and generated ids, null before the middleware runs, guard clause); `Throwing_WithNoInboundCorrelationHeader_EchoesTheGeneratedIdentifier` in the WebApi suite replaces the test that pinned the limitation.

`Cloudstrap.Observability.Correlation` was already an approved public namespace in that package's `PackageSurfaceTests`, so no guard needed relaxing.

**Follow-up taken at the final gate (user, 2026-08-03): the identifier is also echoed in a response header.** `CloudstrapCorrelationMiddleware` now writes the established identifier to a response header of the configured name, never overwriting a value the application set itself, governed by the new `Cloudstrap:Correlation:Request:EchoInResponse` (default `true`). This closes the remaining half of the gap: a caller who sent no identifier now learns the generated one, and can quote it. It is set directly rather than from a response callback, because the middleware runs before the endpoint and the response has not started; one deliberate consequence is that an exception handler which clears the response drops the header, and the identifier travels in the problem-details payload instead. Four unit tests in `Cloudstrap.Observability.Tests` (echo of an inbound id, of a generated id, under a configured header name, and the opt-out) plus `WebApiTests.Response_EchoesTheCorrelationIdBackToTheCaller` proving it through the running SUT, where a real response feature is involved.

---

## Slice 3 — The API is discoverable: one OpenAPI document per version, rendered by a Scalar reference UI that cannot leak a client secret

---

## Step 5 — Controllers spanning versions 1.0 and 2.0 produce `/openapi/v1.json` and `/openapi/v2.json` with neutral, overridable metadata (AC-W3)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.WebApi/CloudstrapOpenApiOptions.cs` *(create)* — `const string SectionName = "Cloudstrap:OpenApi"`; `Enabled : bool = true`, `Title : string?`, `Description : string?`, `OAuth : OpenApiOAuthSettings`.
- `src/Cloudstrap.WebApi/OpenApiOAuthSettings.cs` *(create)* — `TokenUrl : Uri?`, `AuthorizationUrl : Uri?`, `Scopes : IDictionary<string,string>` (get-only initialized, empty default). **No IdP path convention anywhere** (finding 7); consumed in Step 9.
- `src/Cloudstrap.WebApi/OpenApiRegistration.cs` *(create)* — `internal static`: registers the per-version documents through `Asp.Versioning.OpenApi` (mechanic (i.1)) with `ApplicationOptions`-derived neutral defaults (`Title` → e.g. `"{WorkloadName} API"`, `Description` → a neutral sentence built from `SystemName`/`SubsystemName`/`SubsystemType`) and invokes `configurator.OpenApi` per generated document last.
- `src/Cloudstrap.WebApi/CloudstrapWebApiConfigurator.cs` *(modify)* — add `OpenApi : Action<OpenApiOptions>?` (the Microsoft type).
- `src/Cloudstrap.WebApi/WebApplicationBuilderExtensions.cs` *(modify)* — bind + `ValidateOnStart` `CloudstrapOpenApiOptions`; call `OpenApiRegistration` when `Enabled`.
- `src/Cloudstrap.WebApi/WebApplicationExtensions.cs` *(modify)* — map the documents when enabled, anonymously (mechanic (e)), in the reserved slot after the health probes.
- `src/Cloudstrap.WebApi/Cloudstrap.WebApi.csproj` *(modify)* + `src/Directory.Packages.props` *(modify)* — add and pin `Microsoft.AspNetCore.OpenApi`, `Asp.Versioning.OpenApi`.
- `src/Test/UnitTest/Cloudstrap.WebApi.Tests/OpenApiDocumentTests.cs` *(create)*
- `src/Test/UnitTest/Cloudstrap.WebApi.Tests/Infrastructure/TestControllers.cs` *(modify)* — a v2-only controller and a deprecated-version action, so "one document per discovered version" is not vacuous.

**RED** *(write these tests first, run them, confirm they fail — documents are fetched over real HTTP and parsed with `JsonDocument`)*:
- Unit test file: `OpenApiDocumentTests.cs`
  - `OpenApi_ServesOneDocumentPerDiscoveredVersion` — `/openapi/v1.json` and `/openapi/v2.json` both 200; the v1 document contains the v1 path and **not** the v2-only path, and vice versa (AC-W3; the source's hard-coded single `"v1"` document is gone — finding 8).
  - `OpenApi_DocumentTitleAndDescription_DefaultToApplicationOptions` — both contain the configured `WorkloadName`/system names and match no forbidden identifier (neutral metadata; the NIHDI title/license text is gone).
  - `OpenApi_ConfiguredTitleAndDescription_Win` — `Cloudstrap:OpenApi:Title`/`Description` override the derived defaults.
  - `OpenApi_Disabled_ServesNoDocument` — `Cloudstrap:OpenApi:Enabled=false` → `/openapi/v1.json` 404.
  - `OpenApi_ConfigureHook_AppliesAConsumerTransformer` — the hook adds a document transformer whose extension appears in every generated document (hooks run last).
  - `OpenApi_DeprecatedVersion_IsMarkedInTheDocument` — an action on a deprecated version is flagged by the library's ApiExplorer integration *(mechanic (i.1): the executor confirms the exact document shape in RED and reports any deviation at Gate 3)*.
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.WebApi.Tests\bin\Debug\net10.0\Cloudstrap.WebApi.Tests.exe --filter "OpenApiDocumentTests"
  ```

**GREEN**: the Scope items; **no hand-written version-filter transformer** — per-version documents come from `Asp.Versioning.OpenApi` (mechanic (i.1)). XML docs on `CloudstrapOpenApiOptions` name the route pattern, the derived defaults, the per-document hook and the `Scopes` append caveat.

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: an API spanning two versions now publishes one correct document per version with neutral, overridable metadata and a working consumer transformer hook — capability the source never had (AC-W3).
2. `dotnet build src/Cloudstrap.sln` → zero warnings; full `runTests` green; `dotnet format src/Cloudstrap.sln --verify-no-changes` → exit 0.
3. `dotnet list src/Cloudstrap.WebApi/Cloudstrap.WebApi.csproj package` → the two new MIT packages appear; **no `NSwag.*`, no `Swashbuckle.*`** (D-1).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 6 — A developer opens `/scalar` in Development and browses the API; production stays dark unless explicitly opened, and a client secret is unrepresentable (AC-W4)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.WebApi/CloudstrapScalarOptions.cs` *(create)* — `const string SectionName = "Cloudstrap:Scalar"`; `Enabled : bool? = null` (null → Development only), `Path : string = "/scalar"`, `OAuth : ScalarOAuthSettings`.
- `src/Cloudstrap.WebApi/ScalarOAuthSettings.cs` *(create)* — `ClientId : string?`, `SelectedScopes : IList<string>` (get-only initialized, empty). **Deliberately no `ClientSecret` property** — the source's production-only secret validation becomes structurally unnecessary (finding 7).
- `src/Cloudstrap.WebApi/CloudstrapScalarOptionsValidator.cs` *(create)* — `internal sealed`, mechanic (a): Scalar enabled while `Cloudstrap:OpenApi:Enabled=false` → failure naming **both** keys (spec edge case: a UI over no documents is a misconfiguration, not a silent 404).
- `src/Cloudstrap.WebApi/CloudstrapWebApiConfigurator.cs` *(modify)* — add `Scalar : Action<ScalarOptions>?` (the Scalar.AspNetCore type).
- `src/Cloudstrap.WebApi/WebApplicationBuilderExtensions.cs` *(modify)* — bind + `ValidateOnStart` `CloudstrapScalarOptions`.
- `src/Cloudstrap.WebApi/WebApplicationExtensions.cs` *(modify)* — map the reference UI at `Path` when exposure resolves true (mechanic (b)), anonymously (mechanic (e)), listing every version document; apply `OAuth.ClientId`/`SelectedScopes` and then the `Scalar` hook (mechanic (i.3)).
- `src/Cloudstrap.WebApi/Cloudstrap.WebApi.csproj` *(modify)* + `src/Directory.Packages.props` *(modify)* — add and pin `Scalar.AspNetCore`.
- `src/Test/UnitTest/Cloudstrap.WebApi.Tests/ScalarUiTests.cs` *(create)*

**RED** *(write these tests first, run them, confirm they fail)*:
- Unit test file: `ScalarUiTests.cs`
  - `Scalar_InDevelopmentByDefault_ServesTheReferenceUi` — `GET /scalar` → 200 `text/html` and the shell references the OpenAPI document route (AC-W4).
  - `Scalar_InProductionByDefault_IsNotMapped` — 404 (the DEV/TST taxonomy replaced by an explicit, environment-defaulted option).
  - `Scalar_ExplicitlyEnabledInProduction_ServesTheUi` — `Cloudstrap:Scalar:Enabled=true` in Production → 200 (a conscious production choice).
  - `Scalar_WithEnabledFalseInDevelopment_IsNotMapped`.
  - `Scalar_WithConfiguredPath_ServesThere` — `Path=/docs` → `/docs` 200 and `/scalar` 404.
  - `Scalar_EnabledWhileOpenApiDisabled_FailsStartupNamingBothKeys` — message contains `Cloudstrap:Scalar:Enabled` and `Cloudstrap:OpenApi:Enabled` (spec edge case).
  - `ScalarOAuthSettings_ExposesNoClientSecretProperty` — reflection guard: no public member whose name contains `Secret` (finding 7 made structural).
  - `Scalar_ConfigureHook_RunsAfterCloudstrapDefaults` — the hook mutates a Scalar option that is observable in the served shell (or, if the served HTML does not expose it, the hook's invocation and resulting `ScalarOptions` state) *(mechanic (i.3): executor confirms in RED, reports at Gate 3)*.
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.WebApi.Tests\bin\Debug\net10.0\Cloudstrap.WebApi.Tests.exe --filter "ScalarUiTests"
  ```

**GREEN**: the Scope items. XML docs on `CloudstrapScalarOptions` state the Development-only default, that an explicit `true` exposes the UI anywhere, that the UI is mapped anonymously (mechanic (e)) and why no client secret exists.

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: developers get a working reference UI where it belongs, production stays dark unless a human opts in, a UI-over-no-documents misconfiguration fails startup naming both keys, and the browser-secret anti-pattern is impossible to express (AC-W4).
2. `dotnet build src/Cloudstrap.sln` → zero warnings; full `runTests` green; `dotnet format src/Cloudstrap.sln --verify-no-changes` → exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 3: the D-1 documentation stack, as built *(covers Steps 5–6)*

*Executor: STOP here. Present the results of all covered steps and WAIT for user approval — do not start the next step.*

⚠️ **Risk areas at this gate**: **three new dependencies** — `Microsoft.AspNetCore.OpenApi`, `Asp.Versioning.OpenApi`, `Scalar.AspNetCore` (all MIT; two Microsoft/dotnet-org) — rule-4 review, and confirm the closure still contains **zero `NSwag.*`** (D-1, AC-W14) · **public API** — `CloudstrapOpenApiOptions`/`CloudstrapScalarOptions` and their two `Cloudstrap:` sections are permanent surface (D-1's residual risk); check them against the spec sketch verbatim, including the deliberate absence of a client-secret property and of any `Cloudstrap:Swagger` section · **mechanic (e)** — the documents and the UI are mapped anonymously so the Step 8 fallback policy cannot lock the reference UI out of the description it renders; confirm or direct protected documents · **mechanic (i.1)/(i.3) deviations** — the executor reports exactly how `Asp.Versioning.OpenApi` produced the per-version documents and which `Scalar.AspNetCore` members were used, and confirms **no hand-written version-filter transformer** was needed.

- [x] Behavioral verification: test exe output shows — two version-correct documents with neutral derived metadata, configured overrides, the disabled 404 and a working consumer transformer (Step 5); the UI served in Development, dark in Production, openable by explicit option, path-configurable, the both-keys startup failure, the structural no-secret guard and the Scalar hook (Step 6). *(75 tests in `Cloudstrap.WebApi.Tests`, 359 solution-wide, all green.)*
- [x] Code review: options types vs the spec sketch verbatim; the security-scheme work is **not** here (it lands in Step 9 with auth); no NIHDI title/description/license text anywhere; no Keycloak token-URL convention anywhere.
- [x] ⚠️ Dependency review (risk area): `dotnet list src/Cloudstrap.WebApi/Cloudstrap.WebApi.csproj package` → exactly the five runtime packages (`Asp.Versioning.Mvc`, `.Mvc.ApiExplorer`, `.OpenApi`, `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`) plus the three project references; every version CPM-pinned and OSI-licensed.
- [x] User approved — implementation may continue past this gate *(2026-08-03)*

### Gate 3 executor report — mechanics (i.1), (i.3) and (j) resolved

**(i.1) `Asp.Versioning.OpenApi` 10.0.1 — confirmed, no bespoke filter needed.** The API was verified by loading the shipped assembly and enumerating its public surface before any code was written. The wiring is `services.AddApiVersioning(…).AddMvc(…).AddApiExplorer(…).AddOpenApi(Action<VersionedOpenApiOptions>)` on the service side, where `VersionedOpenApiOptions.Document` is the Microsoft `OpenApiOptions` for that version's document, plus `app.MapOpenApi().WithDocumentPerVersion()` on the endpoint side. `/openapi/v1.json` and `/openapi/v2.json` each carry only their own version's operations. **`Cloudstrap.WebApi` contains no hand-written version-filter transformer** — finding 8 closed as specced. All eight Step 5 tests passed on the first run against this wiring.

**(i.3) `Scalar.AspNetCore` 2.16.17 — members used.** `MapScalarApiReference(IEndpointRouteBuilder, string endpointPrefix, Action<ScalarOptions>)` for the configurable path; `ScalarOptions.Title`; `AddDocuments(IEnumerable<string>)` fed from `IApiVersionDescriptionProvider.ApiVersionDescriptions`, so every discovered version is listed; `AddAuthorizationCodeFlow(scheme, Action<AuthorizationCodeFlow>)` for the sign-in. **Deviation from the plan's wording:** `ClientId` is a member of the *flow* (`OAuthFlow.ClientId`), not of the security scheme, and there is no scheme-level client id — so `AddOAuth2Authentication`/`AddDefaultScopes` were not the right pair. Cloudstrap wires the **authorization code flow with PKCE** (`flow.Pkce = Pkce.Sha256`), the only flow a browser client completes without a secret; `SelectedScopes` carries the pre-selected scopes and the flow's URLs come from `Cloudstrap:OpenApi:OAuth`. Scalar's own `AuthorizationCodeFlow` type *does* expose a `ClientSecret`, which Cloudstrap never sets and which no `Cloudstrap:` key can reach — a consumer can only set it by reaching for the `configurator.Scalar` hook deliberately.

**Two test-shape corrections, behavior unchanged:**
1. The reference UI answers the configured prefix with a **302** to the page for a specific document. Tests follow that single redirect, which is what a browser does.
2. The shell does not inline document URLs as absolute paths; it carries them in its initializer payload as relative URLs (`{"title":"v1","url":"openapi/v1.json"}`). Assertions match that exact form. Mechanic (j)'s E2E assertion in Step 11 must use the same shape.

**Validator scope.** `CloudstrapScalarOptionsValidator` fails startup only when `Cloudstrap:Scalar:Enabled` is *explicitly* `true` while the documents are disabled — the spec's edge case verbatim. When exposure was merely implied by `Development`, the UI is quietly left unmapped instead of failing the build, so switching documents off during local work does not stop the application starting.

---

## Slice 4 — The API can require a token: hardened JWT validation, secure-by-default authorization, and a document that says so ⚠️ AUTH RISK AREA

---

## Step 7 — `AddCloudstrapJwtBearer` validates inbound tokens with the four hardened defaults: valid 200, wrong audience 401, expired past the 60-second skew 401 (AC-W8, AC-A3) ⚠️ *(Risk Area: the suite's first shipped authentication surface)*

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.WebApi/CloudstrapJwtBearerOptions.cs` *(create)* — `const string SectionName = "Cloudstrap:JwtBearer"`; `[Required] Authority : string`, `[Required] Audience : string`, `RequireHttpsMetadata : bool? = null`, `ClockSkewSeconds : int = 60`, `MapInboundClaims : bool = false`, `RequireAuthenticatedEndpoints : bool = true` (Step 8 consumes the last one). All four D-2 defaults are settable properties — the every-convention-has-an-override rule is satisfied per default, not merely by the escape hatch.
- `src/Cloudstrap.WebApi/CloudstrapJwtBearerOptionsValidator.cs` *(create)* — `[OptionsValidator] internal sealed partial : IValidateOptions<CloudstrapJwtBearerOptions>` (mechanic (a); source-generated, reflection-free).
- `src/Cloudstrap.WebApi/WebApplicationBuilderExtensions.cs` *(modify)* — `public static AddCloudstrapJwtBearer(this WebApplicationBuilder builder, Action<JwtBearerOptions>? configure = null) : WebApplicationBuilder`: bind + `ValidateOnStart`; `AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(...)` applying `Authority`, `Audience` (audience validation **on**), `RequireHttpsMetadata` (mechanic (b)), `TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(ClockSkewSeconds)`, `MapInboundClaims`; the `configure` hook runs **last** as the final escape hatch. **Nothing else**: no legacy-issuer machinery, no `UrlHelper`, no `EnableDebug` event wiring, no `CircuitServicesAccessor`, no `AddDistributedMemoryCache`, no access-token-management package (all Drop/Replace rows; #9/#10 own token acquisition).
- `src/Test/UnitTest/Cloudstrap.WebApi.Tests/Infrastructure/TestTokens.cs` *(create)* — mechanic (g): issues locally signed tokens (symmetric key, configurable issuer/audience/expiry/claims) and exposes the matching `JwtBearerOptions` hook that installs the signing key and a pre-seeded `OpenIdConnectConfiguration` so `Authority` is never contacted.
- `src/Test/UnitTest/Cloudstrap.WebApi.Tests/JwtBearerValidationTests.cs` *(create)*
- `src/Test/UnitTest/Cloudstrap.WebApi.Tests/Infrastructure/TestControllers.cs` *(modify)* — a `[Authorize]` action and an action echoing the authenticated principal's raw claim types.
- `src/Cloudstrap.WebApi/Cloudstrap.WebApi.csproj` *(modify)* + `src/Directory.Packages.props` *(modify)* — add and pin `Microsoft.AspNetCore.Authentication.JwtBearer` (+ the test-only identity-model pin only if the transitive compile reference does not flow).

**RED** *(write these tests first, run them, confirm they fail — every case is a real HTTP request with a locally issued token; no live IdP)*:
- Unit test file: `JwtBearerValidationTests.cs`
  - `ValidToken_OnSecuredEndpoint_Returns200` (AC-W8 a).
  - `WrongAudienceToken_Returns401` — audience validation is on (AC-W8 b).
  - `TokenExpired90SecondsAgo_Returns401` — beyond the reduced 60 s skew (AC-W8 c).
  - `TokenExpired30SecondsAgo_Returns200` — inside the skew: the pair of tests pins the value `60`, not merely "some skew".
  - `WithConfiguredClockSkew_TheOverrideWins` — `ClockSkewSeconds=300` → the 90-s-expired token is accepted.
  - `InboundClaims_AreNotRemapped` — the echoed claim types contain `sub` and **not** the SOAP-style `nameidentifier` URI (`MapInboundClaims = false`, D-2 b).
  - `RequireHttpsMetadata_DefaultsToTrueOutsideDevelopmentAndFalseInDevelopment` — asserted on the resolved `JwtBearerOptions` for both environments (D-2 c).
  - `RequireHttpsMetadata_ExplicitValueWins_InBothDirections`.
  - `MissingAuthority_FailsStartupNamingTheKey` and `MissingAudience_FailsStartupNamingTheKey` — messages name `Cloudstrap:JwtBearer:Authority` / `…:Audience` (spec edge case).
  - `ConfigureHook_RunsLastAndCanOverrideValidationParameters` — e.g. adding a second `ValidIssuers` entry makes a token from that issuer valid (the documented replacement for the dropped `AddLegacyIssuer`).
  - `AddCloudstrapJwtBearer_OnNullBuilder_ThrowsArgumentNullException`.
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.WebApi.Tests\bin\Debug\net10.0\Cloudstrap.WebApi.Tests.exe --filter "JwtBearerValidationTests"
  ```

**GREEN**: the Scope items. XML docs on `AddCloudstrapJwtBearer` and `CloudstrapJwtBearerOptions`: each hardened default with its rationale and its override key; that this call **validates inbound tokens only** (acquisition is #9/#10); that the handler's own log categories (`Microsoft.AspNetCore.Authentication.JwtBearer`) replace the dropped `EnableDebug` wiring; and the spec edge case that a consumer using a custom pipeline must place `UseAuthentication`/`UseAuthorization` themselves.

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: an API can now require a bearer token with defaults that are stricter than the framework's, every one of them overridable, and misconfiguration fails startup naming the key — on stock ASP.NET Core with zero `Nihdi.AspNetCore.*` anywhere (AC-W8; AC-A3's replacement half).
2. `dotnet build src/Cloudstrap.sln` → zero warnings; full `runTests` green; `dotnet format src/Cloudstrap.sln --verify-no-changes` → exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 8 — Registering the bearer makes every endpoint authenticated by default — controllers and minimal APIs alike — with `[AllowAnonymous]` and one flag as the two documented opt-outs; not registering it changes nothing (AC-W9, AC-W10) ⚠️ *(Risk Area: auth)*

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.WebApi/WebApplicationBuilderExtensions.cs` *(modify)* — `AddCloudstrapJwtBearer` also calls `AddAuthorization(...)` installing a require-authenticated `FallbackPolicy` when `RequireAuthenticatedEndpoints` is true (D-2 d).
- `src/Cloudstrap.WebApi/WebApplicationExtensions.cs` *(modify)* — fill the reserved auth slots per mechanic (d): `UseAuthentication()` → `hooks.BeforeAuthorization` → `UseAuthorization()`, only when the container has an `IAuthenticationSchemeProvider`. `MapControllers()` gets **no** `RequireAuthorization()` call — authorization comes from the fallback policy (spec sketch).
- `src/Test/UnitTest/Cloudstrap.WebApi.Tests/AuthorizationPostureTests.cs` *(create)*
- `src/Test/UnitTest/Cloudstrap.WebApi.Tests/Infrastructure/TestControllers.cs` *(modify)* — a plain (unattributed) action and an `[AllowAnonymous]` action.

**RED** *(write these tests first, run them, confirm they fail)*:
- Unit test file: `AuthorizationPostureTests.cs`
  - `WithBearerRegistered_UnauthenticatedRequestToAPlainEndpoint_Returns401` — no `[Authorize]` attribute anywhere: the fallback policy is what challenges (AC-W9 a).
  - `WithBearerRegistered_UnauthenticatedRequestToAnAllowAnonymousEndpoint_Returns200` (AC-W9 b — the per-endpoint opt-out).
  - `WithBearerRegistered_ValidToken_ReachesThePlainEndpoint` — the policy is satisfiable, not merely blocking.
  - `WithRequireAuthenticatedEndpointsFalse_BothEndpointsAreAnonymous` (AC-W9 c — the global opt-out).
  - `FallbackPolicy_AlsoCoversMinimalApiEndpoints` — an endpoint mapped through `hooks.ConfigureEndpoints` returns 401 unauthenticated and 200 with a token (D-2's "covers minimal APIs, not just mapped controllers").
  - `HealthProbes_StayAnonymous_UnderTheFallbackPolicy` — `/healthz` and `/ready` still 200 without a token (#4 maps them `AllowAnonymous`).
  - `OpenApiDocumentAndScalarUi_StayReachable_UnderTheFallbackPolicy` — mechanic (e) proven, not assumed.
  - `WithoutBearerRegistered_EverythingIsAnonymousAndNothingFails` — no `AddCloudstrapJwtBearer` call: the plain endpoint returns 200, no authentication/authorization middleware error occurs, and the resolved pipeline contains no auth middleware (**AC-W10 — the SUT's mode**).
  - `WithConsumerRegisteredAuthenticationScheme_ThePipelineStillWiresTheMiddleware` — mechanic (d)'s superset behavior, made explicit for the gate.
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.WebApi.Tests\bin\Debug\net10.0\Cloudstrap.WebApi.Tests.exe --filter "AuthorizationPostureTests"
  ```

**GREEN**: the Scope items. XML docs: the secure-by-default posture, the two opt-outs, the fact that health probes and documentation endpoints are anonymous by design, and the mechanic (d) trigger condition.

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: registering the bearer now secures every endpoint by default — including minimal APIs — while probes and documentation stay reachable and both documented opt-outs work; and an app that never registers it behaves exactly as before, with no auth middleware in the pipeline at all (AC-W9, AC-W10).
2. `dotnet build src/Cloudstrap.sln` → zero warnings; full `runTests` green; `dotnet format src/Cloudstrap.sln --verify-no-changes` → exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 9 — The published document tells the truth about auth: a Bearer scheme with an explicit token URL, security requirements on secured operations and none on anonymous ones (AC-W5)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.WebApi/OpenApiSecurityTransformer.cs` *(create)* — `internal sealed` document + operation transformer on the built-in `Microsoft.AspNetCore.OpenApi` API (mechanic (i.2)): adds the Bearer/OAuth2 security scheme built **only** from `Cloudstrap:OpenApi:OAuth` (`TokenUrl`, `AuthorizationUrl`, `Scopes` — no IdP path convention, finding 7) and attaches a security requirement to every operation whose endpoint is not anonymous. Registered **only** when an authentication scheme provider exists (mechanic (d)) — zero lines carried over from the dropped NSwag `IOperationProcessor`.
- `src/Cloudstrap.WebApi/OpenApiRegistration.cs` *(modify)* — register the transformer conditionally.
- `src/Test/UnitTest/Cloudstrap.WebApi.Tests/OpenApiSecurityTests.cs` *(create)*

**RED** *(write these tests first, run them, confirm they fail — documents fetched over HTTP and parsed with `JsonDocument`)*:
- Unit test file: `OpenApiSecurityTests.cs`
  - `Document_WithBearerRegistered_ContainsTheSecurityScheme` — `components.securitySchemes.Bearer` present with `type: oauth2`, `scheme: bearer` and the flow's `tokenUrl` equal **exactly** to the configured `Cloudstrap:OpenApi:OAuth:TokenUrl` (no `/protocol/openid-connect/token` composition anywhere — finding 7).
  - `Document_WithConfiguredAuthorizationUrlAndScopes_ReflectsThem`.
  - `Document_SecuredOperation_CarriesASecurityRequirement` (AC-W5).
  - `Document_AllowAnonymousOperation_CarriesNoSecurityRequirement` (AC-W5's second half — the capability the dropped operation processor existed for).
  - `Document_WithoutBearerRegistered_ContainsNoSecuritySchemeAndNoRequirements` — an API with no auth publishes no security metadata.
  - `Document_WithBearerRegisteredButNoTokenUrlConfigured_StillDocumentsTheBearerScheme` — the scheme is present without an OAuth flow rather than inventing a URL (explicit-only rule).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.WebApi.Tests\bin\Debug\net10.0\Cloudstrap.WebApi.Tests.exe --filter "OpenApiSecurityTests"
  ```

**GREEN**: the Scope items, written from scratch on the built-in transformer API (mechanic (i.2); the read source `AddScalarNihdi` is the object-model shape reference only). XML docs on `OpenApiOAuthSettings` state that no URL is ever derived from `Authority`.

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: the published documents now describe exactly the authentication the middleware enforces — scheme, explicit URLs, per-operation requirements, and nothing at all when the API is anonymous (AC-W5).
2. `dotnet build src/Cloudstrap.sln` → zero warnings; full `runTests` green; `dotnet format src/Cloudstrap.sln --verify-no-changes` → exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 4: ⚠️ AUTH RISK AREA — the suite's first shipped authentication surface *(covers Steps 7–9)*

*Executor: STOP here. Present the results of all covered steps and WAIT for user approval — do not start the next step. This gate is a mandatory human review under CLAUDE.md's risk-area rule: the D-2 **defaults** were signed off at the spec gate, the **implementation** is signed off here.*

⚠️ **Risk areas at this gate**: **auth code end to end** — token validation, the four hardened defaults and their overrides, the fallback policy's blast radius, and the fact that no credential material, no token *acquisition* and no auth package beyond `Microsoft.AspNetCore.Authentication.JwtBearer` exist anywhere in the package · **mechanic (d)** — auth middleware is wired whenever *any* scheme provider is registered (a deliberate superset of the spec's wording): confirm or direct the strict reading · **mechanic (e)** — documents and the Scalar UI are anonymous under the fallback policy: confirm or direct protected documents · **new dependency** `Microsoft.AspNetCore.Authentication.JwtBearer` (MIT, Microsoft, CPM-pinned) · **AC-A3** — confirm the whole solution contains zero `Nihdi.AspNetCore` references.

- [x] Behavioral verification: test exe output shows — valid/wrong-audience/expired-past-skew/expired-within-skew outcomes pinning the 60-second value, the skew override, raw (unmapped) claim types, HTTPS-metadata defaults per environment plus explicit override, both missing-key startup failures, and the hook-runs-last override (Step 7); 401 on a plain endpoint, 200 on `[AllowAnonymous]`, 200 with a token, the global opt-out, minimal-API coverage, anonymous probes and documentation, the no-bearer-registered anonymous baseline and the consumer-scheme case (Step 8); the scheme with an exact configured token URL, requirements on secured operations, none on anonymous ones, and nothing at all without auth (Step 9). *(105 tests in `Cloudstrap.WebApi.Tests`, 389 solution-wide, all green.)*
- [x] Code review (auth): `AddCloudstrapJwtBearer` line by line against the spec sketch and D-2 — the four defaults as settable properties, the `Action<JwtBearerOptions>` hook running last, and **none** of the dropped source machinery (`AddLegacyIssuer`, `UrlHelper`, `EnableDebug` events, `CircuitServicesAccessor`, `AddDistributedMemoryCache`, access-token management); no token acquisition, no secrets, no logging of token contents.
- [x] ⚠️ Dependency + identifier review (risk area): `dotnet list src/Cloudstrap.WebApi/Cloudstrap.WebApi.csproj package` → exactly six runtime packages + three project references; a solution-wide case-insensitive search for `Nihdi.AspNetCore` returns nothing (AC-A3).
- [ ] User approved — implementation may continue past this gate

### Gate 4 executor report — two discoveries that changed the implementation

**1. Mechanic (d)'s stated trigger condition does not work; the predicate was corrected.** The plan says to wire the auth middleware "when the container has an `IAuthenticationSchemeProvider`". Measured: **MVC registers the authentication core services regardless**, so that provider exists in every application built by `AddCloudstrapWebApi` and the condition would always be true — the opposite of AC-W10. The shipped predicate is the registered *scheme map* instead:

```csharp
bool hasAuthentication = app.Services
    .GetService<IOptions<AuthenticationOptions>>()?.Value.SchemeMap.Count > 0;
```

True only when someone actually called `AddAuthentication(...).AddXxx(...)` — Cloudstrap's bearer or a consumer's own scheme, which is the superset behavior mechanic (d) intended. Pinned by `WithoutBearerRegistered_EverythingIsAnonymousAndNothingFails` (asserts the scheme map is empty) and `WithConsumerRegisteredAuthenticationScheme_ThePipelineStillWiresTheMiddleware`.

**2. Minimal hosting inserts auth middleware *ahead of routing*, which would silently break `[AllowAnonymous]`.** `WebApplicationBuilder` adds `UseAuthentication`/`UseAuthorization` itself when the corresponding services exist and its two marker keys are unset — into the outer pipeline, before routing, where no endpoint metadata is visible. Under the D-2 fallback policy that means `[AllowAnonymous]` is ignored and health probes and the reference UI are challenged. Confirmed by the RED run: five tests failed for exactly that reason before the fix. `UseCloudstrapWebApi` now claims both keys (`__AuthenticationMiddlewareSet`, `__AuthorizationMiddlewareSet`) before placing the middleware itself, after routing. The constants are framework-internal; `WithBearerRegistered_UnauthenticatedRequestToAnAllowAnonymousEndpoint_Returns200` is the tripwire that catches it if a future framework release changes them.

**Step 7 test shape.** Token *validation* is proven through an action that calls `HttpContext.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme)` and maps the result to 200/401, rather than through `[Authorize]`. This keeps Step 7 about "was the token accepted, and why" and leaves authorization posture entirely to Step 8, where the middleware is wired — the two concerns fail independently and their tests say which one broke.

**Scalar's client secret, revisited.** Step 9's document scheme is built only from `Cloudstrap:OpenApi:OAuth`. `ScalarOAuthSettings` still has no secret property, and no `Cloudstrap:` key reaches Scalar's own `AuthorizationCodeFlow.ClientSecret`.

---

## Slice 5 — Publishable, permanently guarded, and demonstrated in the running WASM SUT

---

## Step 10 — The package is publishable and guarded forever: metadata, README, and tripwires on the surface, the closure and the forbidden identifiers (AC-W14, AC-ASP2, AC-A3)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.WebApi/Cloudstrap.WebApi.csproj` *(modify)* — `<Description>` (API versioning, per-version OpenAPI documents with a Scalar reference UI, RFC 9457 problem-details error handling, correlation, health probes, security headers/HSTS/CORS and optional hardened JWT bearer validation — two calls and one `Cloudstrap:` subsection each), `<PackageTags>$(PackageTags);webapi;openapi;scalar;versioning;jwt;problemdetails;aspnetcore</PackageTags>`, `<PackageReadmeFile>README.md</PackageReadmeFile>` + `<None Include="README.md" Pack="true" PackagePath="/" />`.
- `src/Cloudstrap.WebApi/README.md` *(create)* — the sub-ten-line `Program.cs` quick start (`AddCloudstrapWebApi` · optional `AddCloudstrapJwtBearer` · `UseCloudstrapObservability` · `UseCloudstrapWebApi`); **the canonical middleware order** as a numbered list with each hook point's slot named; settings tables for the four owned sections (`Cloudstrap:WebApi`, `Cloudstrap:OpenApi`, `Cloudstrap:Scalar`, `Cloudstrap:JwtBearer`) plus the consumed Core sections (`Cloudstrap:Application` PathBase/WorkloadName, `Cloudstrap:HealthChecks`, `Cloudstrap:Correlation`) marked "owned elsewhere, never redefined"; the append-to-defaults caveat for the three collection settings (mechanic (k)); the security posture section — the four JWT hardened defaults with their override keys and the two fallback-policy opt-outs, the anonymous documentation endpoints (mechanic (e)), the "never enable `IncludeDetails` on a public production API" warning, and the JWT log category that replaces the dropped `EnableDebug`; the SPA/BFF composition recipe (hooks carrying static files + SPA fallback — the shape the SUT uses); escape hatches per Behaviors row (stock `AddCors`/`RequireCors`, stock `MapHealthChecks`, your own security-header middleware via `BeforeRouting`, `MapControllers = false` for minimal-API-only hosts, `Asp.Versioning.Http` for versioned minimal APIs); the **Aspire coexistence** note (health additive on the stock builder, observability owner/contribute from #2, typed clients from #4, zero `Aspire.*`); the framework-reference consequence (server apps only); and the migration note that NSwag, `?api-version=v1` normalization, enum-keyed-dictionary custom JSON and path-base magic are deliberately gone (Deliberate Behavior Changes 4, 5, 6, 8).
- `src/Test/UnitTest/Cloudstrap.WebApi.Tests/PackageSurfaceTests.cs` *(create)* — permanent guards mirroring `Cloudstrap.Extensions.Tests/PackageSurfaceTests.cs`.

**RED** *(guard tests are written and run first but, as tripwires against already-correct code, may pass immediately — the honest failing state is in the artifacts: before GREEN the Release nupkg has no README/description/tags; recorded per the plan-2/3/4 precedent)*:
- Unit test file: `PackageSurfaceTests.cs`
  - `ReferencedAssemblies_OfWebApiAssembly_MatchTheApprovedClosure` — every referenced assembly starts with `System`, `Microsoft.`, `Asp.Versioning`, `Scalar.` or equals `Cloudstrap.Core`/`Cloudstrap.Observability`/`Cloudstrap.Extensions`; explicitly **zero** names starting `Aspire` (AC-ASP2), `NSwag` (D-1/AC-W14), `Swashbuckle`, `Nihdi` (AC-A3), `NWebsec`, `Duende`, `LanguageExt`.
  - `PublicTypes_OfWebApiAssembly_ContainNoForbiddenIdentifiers` — no public type/member matches `(?i)nihdi|riziv|dynatrace|nservicebus|swagger`.
  - `PublicTypes_OfWebApiAssembly_AreSealedOrStaticAndInTheSingleApprovedNamespace` — namespace `Cloudstrap.WebApi` only; every public class sealed or static; **no public interfaces** (this package publishes none).
  - `WebApiAssembly_DeclaresNoTypeNamedForSwaggerOrCorrelation` — the D-1 and finding-3 drops made permanent.
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.WebApi.Tests\bin\Debug\net10.0\Cloudstrap.WebApi.Tests.exe --filter "PackageSurfaceTests"
  ```

**GREEN**: add the csproj metadata and write `README.md` per Scope.

**DB changes**: none.

**VERIFY**:
1. Test exe (full run) → all tests pass, including the four new guards.
2. `dotnet build src/Cloudstrap.sln -c Release` → `src/Cloudstrap.WebApi/bin/Release/Cloudstrap.WebApi.<version>.nupkg`; expand a `.zip` copy → contains `README.md`, `icon.png`, `lib/net10.0/Cloudstrap.WebApi.dll` **and** `.xml`; the nuspec shows the MIT license expression, description, tags, repository URL, and a dependency list with **no `NSwag.*`**, no `Nihdi.*`, no `NWebsec.*`, no `Aspire.*` (AC-W14, AC-ASP2).
3. **AC-W14 identifier sweep** (new package + tests):
   ```powershell
   Get-ChildItem -Recurse -File -Path src/Cloudstrap.WebApi, src/Test/UnitTest/Cloudstrap.WebApi.Tests |
     Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
     Select-String -Pattern '(?i)(nihdi|riziv)'
   ```
   → zero matches (guard-test tripwire patterns are self-referential and excluded by reading the hits, as in plans 2–4).
4. **Closure check**: `dotnet list src/Cloudstrap.WebApi/Cloudstrap.WebApi.csproj package` reviewed against the spec's Dependencies table — every entry OSI-licensed and CPM-pinned.
5. Full gates on the final tree: `dotnet build src/Cloudstrap.sln` (zero warnings) + `runTests` (all suites) + `dotnet format src/Cloudstrap.sln --verify-no-changes` (exit 0).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 11 — The WASM SUT Bff runs on this package: versioned endpoints, per-version documents, the Scalar UI and the hardened error response — proven through the real running app while all 17 existing E2E tests stay green (AC-W15)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Test/WasmTestProject/src/Host/Bff/Cloudstrap.WasmTestProject.Host.Bff.csproj` *(modify)* — `<ProjectReference>` to `Cloudstrap.WebApi`.
- `src/Test/WasmTestProject/src/Host/Bff/Program.cs` *(modify)* — the conversion, mechanic (k):
  - `builder.Services.AddControllers();` → `builder.AddCloudstrapWebApi();`
  - the pipeline block `app.UseBlazorFrameworkFiles(); app.UseStaticFiles(); app.UseRouting(); app.UseCloudstrapCorrelation(); app.MapControllers(); app.MapCloudstrapHealthChecks(); app.MapFallbackToFile("index.html");` → **one** `app.UseCloudstrapWebApi(pipeline => { pipeline.BeforeRouting = branch => branch.UseBlazorFrameworkFiles().UseStaticFiles(); pipeline.ConfigureEndpoints = endpoints => endpoints.MapFallbackToFile("index.html"); });`
  - **unchanged and still demonstrated**: `GetCloudstrapOptions()` (#1), `CloudstrapBootstrapLogger` (#2), `UseCloudstrapObservability().AddAzureMonitor(...)` (#2/#3), `AddCloudstrapHttpServiceClient<ISelfApiClient, SelfApiClient>("SelfApi")` + the `self` health check (#4), `InMemoryDoctorStore`.
  - **deliberately absent**: `AddCloudstrapJwtBearer` — the SUT stays anonymous by design (AC-W10; auth demos arrive with #9/#10). Add a comment saying exactly that, so it reads as a decision rather than an omission.
- `src/Test/WasmTestProject/src/Contracts/StatusDto.cs` *(create)* — `record StatusDto(string ApiVersion, string WorkloadName)`.
- `src/Test/WasmTestProject/src/Host/Bff/Controllers/StatusController.cs` *(create)* — `[ApiVersion("1.0")] [Route("api/v{version:apiVersion}/status")]`: `GET` returns the DTO; `GET boom` throws a nested exception (the error-path fixture).
- `src/Test/WasmTestProject/src/Host/Bff/Controllers/StatusV2Controller.cs` *(create)* — `[ApiVersion("2.0")] [Route("api/v{version:apiVersion}/status")]`: `GET` returns the v2 DTO, so **two** versions are discovered and two documents are generated.
- `src/Test/WasmTestProject/src/Host/Bff/appsettings.json` *(modify)* — add `Cloudstrap:WebApi:ExceptionHandling:IncludeDetails: false` (spec edge case: explicit beats the Development default, so the hardened shape is assertable in a Development run) and `Cloudstrap:OpenApi:Title` (a neutral SUT title the document assertion can pin). `Cloudstrap:Scalar` stays unset — the Development-only default is what the E2E proves.
- `src/Test/WasmTestProject/test/Cloudstrap.WasmTestProject.E2E.Tests/WebApiTests.cs` *(create)*
- `src/Test/WasmTestProject/test/Cloudstrap.WasmTestProject.E2E.Tests/ScalarPageTests.cs` *(create)* — `: PageTestBase`.
- `src/Test/WasmTestProject/README.md` *(modify)* — a demo-table row (`GET api/v1/status` + `api/v2/status` · `/openapi/v{n}.json` · `/scalar` · the error path | Cloudstrap.WebApi (#5) | `WebApiTests` + `ScalarPageTests`) and harness notes: the Bff's whole pipeline is now one `UseCloudstrapWebApi` call with the SPA composition carried by its hook points, the SUT is **anonymous by design** (no `AddCloudstrapJwtBearer`), `IncludeDetails` is pinned `false` so the safe error shape is assertable in Development, and the Scalar assertion is shell-based because the reference UI's JavaScript comes from a CDN (mechanic (j)).

**RED** *(write these tests first, run them, confirm they fail — before the conversion the Bff has no versioned route, no OpenAPI document, no `/scalar` and no problem-details error path, so all of them fail)*:
- E2E test file: `src/Test/WasmTestProject/test/Cloudstrap.WasmTestProject.E2E.Tests/WebApiTests.cs`
  - `VersionedEndpoint_Get_ReturnsPayloadAndReportsBothSupportedVersions` — `GET api/v1/status` → 200 with the workload name, and `api-supported-versions` lists `1.0` **and** `2.0`; the request carries no `Authorization` header, which is simultaneously the running-app proof of AC-W10 (AC-W1 live).
  - `VersionedEndpoint_V2_ReturnsTheSecondVersionsPayload` — `GET api/v2/status` → the v2 body.
  - `OpenApiDocuments_AreServedPerVersion` — `/openapi/v1.json` and `/openapi/v2.json` both 200; each contains its own status path and not the other's, and the v1 document also contains the unversioned-controller paths (`api/diagnostics/...`, `api/doctor`) that the default-version convention assigned to 1.0 (AC-W3 + AC-W2 live).
  - `Error_Get_ReturnsProblemDetailsWithoutExceptionDetail` — `GET api/v1/status/boom` → 500, `Content-Type: application/problem+json`, no exception type/message/stack text in the body (AC-W6 live, with `IncludeDetails=false` pinned in the SUT config).
  - `Error_WithIncludeDetailsEnabled_ReturnsTheExceptionDetail` — a second short-lived instance via `SutProcess.Start("http://127.0.0.1:5303", ["--Cloudstrap:WebApi:ExceptionHandling:IncludeDetails=true"])`, polled to liveness, then `GET api/v1/status/boom` → the body carries the exception type, message and inner chain (AC-W7 live; the plan-3/plan-4 second-instance precedent).
  - `Scalar_Get_ServesTheReferenceUiShell` — `GET /scalar` → 200 `text/html` referencing the v1 document (AC-W4 live, mechanic (j)).
- E2E test file: `ScalarPageTests.cs` *(`: PageTestBase`)*
  - `ScalarPage_Loads_InTheBrowser` — navigate to `{BaseUrl}/scalar`, assert the response status is OK and the document title is non-empty; **no console-error assertion** (mechanic (j) — the bundle may come from a CDN the agent cannot reach).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\WasmTestProject\test\Cloudstrap.WasmTestProject.E2E.Tests\bin\Debug\net10.0\Cloudstrap.WasmTestProject.E2E.Tests.exe --filter "WebApiTests"
  ```

**GREEN**: the Scope items — project reference, the `Program.cs` conversion, the two status controllers + DTO, the two configuration keys, the README row. **Every pre-existing E2E test must stay green unchanged**: in particular `HomePageTests` (the WASM app still boots and renders through the hook-carried static files and SPA fallback), `HealthAndCorrelationTests` (probes and correlation now served from inside `UseCloudstrapWebApi`), `DiagnosticsTests`/`DoctorsTests` (unversioned controllers keep their routes under the assumed default version and their JSON bodies survive the `WhenWritingNull` default), `AzureMonitorTests` and `ExtensionsTests` (observability and the typed-client hop untouched). *(If the versioning defaults, the null-omitting JSON default or the security headers disturb any existing test, the executor reports it at the gate rather than weakening the assertion.)*

**DB changes**: none.

**VERIFY**:
1. E2E exe → the six new tests pass **and all 17 pre-existing E2E tests pass unchanged** (build first; one-time `playwright.ps1 install chromium` if needed).
2. Manual smoke (optional but recorded): `dotnet run --project src/Test/WasmTestProject/src/Host/Bff` then browse `/scalar`, `/openapi/v1.json`, `api/v1/status` and `api/v1/status/boom`.
3. Full gates on the final tree: `dotnet build src/Cloudstrap.sln` (zero warnings) + `runTests` (Core, Observability, AzureMonitor, Extensions, WebApi, E2E — all green) + `dotnet format src/Cloudstrap.sln --verify-no-changes` (exit 0).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — final: deliverable #5 complete *(covers Steps 10–11; closes the deliverable)*

*Executor: STOP here. Present the results and WAIT for user approval. Any Git push afterwards requires the user's explicit go-ahead (CLAUDE.md: no push without confirmation).*

### Step 11 executor report — two E2E predictions corrected

1. **`api-supported-versions` reports one version per endpoint, not the union.** The plan's Scope puts each
   status version on its own controller (`StatusController` / `StatusV2Controller`), and with URL-segment
   versioning that produces two distinct endpoints — so `/api/v1/status` reports `1.0` and `/api/v2/status`
   reports `2.0`. The plan's RED text expected the v1 response to list both, which only happens when one
   controller declares both versions (the unit-suite `VersionedWidgetsController` shape). The Scope's
   two-controller shape was kept — it is the realistic evolution pattern — and each test now asserts its own
   endpoint's version. That **both** versions exist is proven by the v2 payload test and the two documents.
2. **`HttpClient` follows the Scalar redirect automatically**, unlike `TestServer`'s client, so the E2E test
   asserts the final `200 text/html` directly instead of the interim `302`.

Ten E2E tests were added rather than the planned six: the plan's six plus the configured document title, the
correlation id in the error payload, the v2 `api-supported-versions`, and the security headers on both an API
response and a probe response.

- [x] Behavioral verification: the ten new `WebApiTests`/`ScalarPageTests` methods pass; **the 17 pre-existing E2E tests pass unchanged** against a Bff whose entire pipeline is now one `UseCloudstrapWebApi` call with the SPA composition carried by its hook points; the four `PackageSurfaceTests` guards are green; the expanded Release `.nupkg` contents were reviewed; the identifier sweep is empty.
- [x] Spec acceptance sign-off: walk **AC-W1…AC-W15 + AC-ASP2 + AC-A3** against the step evidence using the Overview's AC coverage map — all met; confirm nothing from the spec's Drop / Out-of-Scope lists was resurrected (no `NSwag.*` in the closure and no `Cloudstrap:Swagger` section, no `SwaggerBootstrapper`, no `UrlHelper`/`AddLegacyIssuer`, no `NormalizedQueryStringApiVersionReader`, no correlation middleware of our own, no `AddWebOptions`, no `DictionaryTKeyEnumTValueConverter`, no `/probe.aspx`, no health/DataProtection/KeyVault re-implementation, no OIDC or client-credentials code, no `NWebsec.*`, no `Aspire.*`) and that every De-NIHDI row for this deliverable is closed (`AddNihdiX` → `AddCloudstrapX`, the `ForDevTst` taxonomy → an explicit option, neutral fixtures, no internal IdP URLs or Keycloak path conventions).
- [x] Pipeline-pattern sign-off (⚠️ inherited by #6 and #7): the as-built `Add`/`Use` pair, the four hook points, the `MapControllers` switch and the canonical middleware order are what deliverables #6 (Mvc) and #7 (Worker) will copy — approve the shape explicitly, including every executor deviation reported at Gates 1–4.
- [x] Docs review: `src/Cloudstrap.WebApi/README.md` matches as-built behavior (canonical order, four settings tables, the security posture section, the SPA/BFF recipe, the Aspire note); `src/Test/WasmTestProject/README.md` demo-table row and harness notes accurate, including the "anonymous by design" statement and the CDN caveat.
- [x] User approved — deliverable #5 done *(2026-08-03)*; project-manager still to flip the ROADMAP row to ✅.
