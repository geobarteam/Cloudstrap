# Spec: WebApi Bootstrap — `Cloudstrap.WebApi` (Roadmap Deliverable #5)

> **Approved 2026-08-02 — zero Open Questions remain; spec is planner-ready.** All three gate questions were resolved by the user per this spec's recommendations (see the Decision Log at the end): the built-in OpenAPI + Scalar stack with NSwag dropped (D-1, which also amended the founding spec's Package Map row and Decisions Made table), the four JWT bearer hardened defaults (D-2, ⚠️ auth risk area — user is the sign-off authority), and the composite `UseCloudstrapWebApi` pipeline with four hook points + the `MapControllers` switch (D-3 — the pipeline pattern deliverables #6 and #7 inherit).
>
> Sources: `_plans/ROADMAP.md` §5 (hand-off brief, file inventory verified 2026-08-02) · `_specs/Cloudstrap.md` (Decisions Made, Package Map WebApi row, De-NIHDI-fication Checklist, Auth Replacement, Aspire Coexistence AC-ASP1–AC-ASP3) · `_specs/4-ConfigKeyVaultHttpExtensions.md` (routed items: middleware pipeline, `AddWebOptions`/`UseWebOptions`, `AddNihdiWebApiProtections`, path base, security-header choice, JSON converter re-evaluation) · `_specs/2-ObservabilityBase.md` (correlation contract, `W3CTracingMiddleware` Drop, `CloudstrapHealthCheckTags`) · **shipped** code in `src/Cloudstrap.Core/` (`ApplicationOptions.PathBase`/`ExceptionHandlerPath`, `HealthChecksOptions`, `CorrelationOptions`), `src/Cloudstrap.Observability/Correlation/` (`CloudstrapCorrelationMiddleware`, `UseCloudstrapCorrelation`, `AddCloudstrapCorrelation`) and `src/Cloudstrap.Extensions/` (`MapCloudstrapHealthChecks`, `AddCloudstrapHttpServiceClient`) · source reference repo (read-only) `D:\source\Nihdi-Core-Configuration\Nihdi-Core-Configuration\src\` — every file in the Port Decision Table was opened and read (`Nihdi.Core.Configuration.WebApi\*`, `Common\Scalar\*`, `Common\Services\ServiceCollectionExtensions.cs`, `Common\Extensions\ApplicationBuilderExtensions.cs`, `Common\Options\*`, `Common\Serialization\DictionaryTKeyEnumTValueConverter.cs`, old Core `Settings\Swagger|Scalar|Security\*`, the WebApi `.csproj`) · external evidence gathered 2026-08-02: [Asp.Versioning.Mvc 10.0.0 on NuGet](https://www.nuget.org/packages/Asp.Versioning.Mvc) (MIT, dotnet org, released 2026-04-21, 122.9M downloads) · [Scalar.AspNetCore on NuGet](https://www.nuget.org/packages/Scalar.AspNetCore) (MIT, 2.16.16, updated 2026-07-20, scalar/scalar monorepo) · [.NET Blog: Combining API versioning with OpenAPI in .NET 10](https://devblogs.microsoft.com/dotnet/api-versioning-in-dotnet-10-applications/) (new `Asp.Versioning.OpenApi` library: per-version documents + `WithDocumentPerVersion()` in the box).
>
> **⚠️ Risk areas this deliverable touches** — **auth code**: `AddCloudstrapJwtBearer` is the suite's first shipped authentication surface; its four hardened defaults were reviewed and signed off by the user at the spec gate (D-2), and every plan gate touching auth stays a human-review point · **public API one-way doors**: the OpenAPI stack fixes the options shape (D-1); the pipeline shape is inherited by #6 (Mvc) and #7 (Worker) (D-3) · **new external dependencies**: `Asp.Versioning.Mvc`/`.Mvc.ApiExplorer`/`.OpenApi` (MIT), `Microsoft.AspNetCore.OpenApi` (MIT), `Scalar.AspNetCore` (MIT), `Microsoft.AspNetCore.Authentication.JwtBearer` (MIT) — all CPM pins · **third `Microsoft.AspNetCore.App` framework reference** in the suite (after Observability, Extensions).

## Code-reading findings that shaped this spec

1. **The source's NSwag path is dead code.** `Swagger\SwaggerBootstrapper.cs` and `Swagger\OperationProcessors\AspNetCoreOperationSecurityScopeProcessorCustom.cs` are referenced **nowhere** (repo-wide grep for `SwaggerBootstrapper`/`ConfigureSwaggerServices`/`ConfigureSwaggerMiddleware`: only their own definitions). The live path in `WebApplicationBuilderExtensions.ConfigureOpenApiDocumentation` calls `AddScalarNihdi` (`Common\Scalar\`), which uses **`Microsoft.AspNetCore.OpenApi`'s `AddOpenApi()`** + **`Scalar.AspNetCore`'s `MapScalarApiReference()`**. The `NSwag.AspNetCore 14.7.1` PackageReference exists only to keep the dead bootstrapper compiling. The source itself already migrated to the stock stack — this is the evidence behind the decided OpenAPI stack (D-1).
2. **The DEV/TST exception handler is unreachable, and `/error` re-execution is dead.** `AddNihdiApiServices` registers `WebApiExceptionHandlerForDevTst` (DEV/TST) then `WebApiExceptionHandler` (always), but the pipeline's `ConfigureExceptionHandling` uses `UseDeveloperExceptionPage()` in DEV/TST — which never invokes `IExceptionHandler`s — so the verbose handler never runs where it is registered. Outside DEV/TST, `UseExceptionHandler(ExceptionHandlerPath)` tries the `IExceptionHandler` chain first and `WebApiExceptionHandler.TryHandleAsync` always returns `true`, so the `/error` re-execution never happens either. The environment-split design is broken in both branches — collapsing to **one** handler with an explicit include-details option (the roadmap's directive, per the #1 `IsRunningInAks()` precedent) also fixes two latent defects.
3. **WebApi's `CorrelationMiddleware` is a strict subset of #2's shipped middleware** (the mandated diff). The WebApi middleware only copies the inbound `CorrelationHeader.Name` value into `ICorrelationContextAccessor` (no generation when absent, no requirement enforcement, hard-coded header name). Shipped `CloudstrapCorrelationMiddleware` (#2) does all of that **plus** id generation via `ICorrelationSource`, configurable header (`Cloudstrap:Correlation:HeaderName`), `[CorrelationRequired]`/`[AllowNoCorrelation]`/health-endpoint exemptions, and a 400 `application/problem+json` response. WebApi adds nothing on top → **Drop**, pipeline uses `UseCloudstrapCorrelation()`.
4. **`AddNihdiWebApiProtections` registered HSTS + CORS that the WebApi pipeline never activated.** `UseNihdiWebApiMiddleware` contains no `UseHsts()`, no `UseCors()`, and no security-header middleware (those exist only in Common's `UseNihdiWebMiddleware`, the MVC-shaped pipeline). So in source WebApi apps, HSTS and CORS services were configured but inert. The new pipeline activates exactly what it registers (Deliberate Behavior Change 6). The CORS fallback (`no origins configured → AllowAnyOrigin` with a log warning) is the insecure-by-default trap #4's route note flags — replaced by "no origins → no CORS policy registered".
5. **API-versioning defaults live in the Scalar settings.** `ScalarConfiguration.DefaultMajorVersion/DefaultMinorVersion` drive `ApiVersioningOptions.DefaultApiVersion` — documentation settings steering routing behavior. The redesign moves the default version to `Cloudstrap:WebApi:ApiVersioning`, leaving `Cloudstrap:OpenApi`/`Cloudstrap:Scalar` purely documentation-shaped (the roadmap's section split).
6. **The JWT layer is wrapped around one enterprise IdP.** `AddNihdiJwtBearerAuthentication` builds on the internal `Nihdi.AspNetCore.Authentication.JwtBearer`; its `AddLegacyIssuer` flag + `UrlHelper.ReplaceLastPartWithLegacy` exist solely for the legacy Keycloak realm (`https://rias-[env].riziv-inami.fgov.be/auth/realms/legacy`, per the source XML doc); it also registers `CircuitServicesAccessor` (a Blazor-websocket auth artifact) and `AddDistributedMemoryCache` inside a WebApi entry point. Replacement: stock `Microsoft.AspNetCore.Authentication.JwtBearer` + hardened defaults (roadmap-fixed direction; the four decided values in D-2). The `EnableDebug` event-logging flag duplicates the stock handler's `Microsoft.AspNetCore.Authentication.JwtBearer` log categories → dropped.
7. **The OpenAPI security scheme hard-codes a Keycloak URL convention.** `AddScalarNihdi` composes `TokenUrl` as `Authority + "/protocol/openid-connect/token"` — a Keycloak path layout — and embeds NIHDI-exclusive license/terms text (`SwaggerBootstrapper` worse still). Replaced by an explicit `TokenUrl` option and neutral metadata defaults derived from `ApplicationOptions`. One good source idea is kept and strengthened: the Scalar path deliberately never sends the OAuth client secret to the browser (`ScalarOAuthConfiguration.Validate` rejects secrets in production) — the new options type simply **has no secret property**, making the mistake unrepresentable.
8. **Asp.Versioning 10.0.0 closes the versioned-documents gap in the box.** The source generates a single hard-coded `"v1"` document even though versioning + ApiExplorer (`'v'VVV`, URL substitution) are fully configured — v2 endpoints would be mislabeled. Asp.Versioning v10 (released 2026-04-21) ships the new **`Asp.Versioning.OpenApi`** bridge: one OpenAPI document per discovered API version with sunset/deprecation policies, no hand-written transformer. Per-version documents are therefore specced as the default — the capability comes from a library we already ship, not from new bespoke code.
9. **`DictionaryTKeyEnumTValueConverter` re-evaluated against stock `System.Text.Json` (the #4 route note): Drop.** Stock STJ has supported enum-keyed dictionaries since .NET 5. The converter's remaining deltas — case-insensitive key parsing on read, `PropertyNamingPolicy` (instead of `DictionaryKeyPolicy`) applied to keys on write — are legacy-payload compatibility concerns with no fresh-consumer value. Consumers with exotic needs use the JSON configure hook.
10. **Everything health/hosting-shaped is already owned by #2/#4 — reuse, do not rebuild.** `Nihdi.Core.Health` + `UseNihdiHealthChecksInternal` + `AddNihdiLoadBalancerProbe` (`/probe.aspx`) → #4's shipped `MapCloudstrapHealthChecks` honoring Core's `HealthChecksOptions` and #2's `CloudstrapHealthCheckTags` (`"live"`/`"ready"`); `AddProbeHealthChecks` + `ConfigureProbeHealthChecksDelegate` → stock `IHealthChecksBuilder` (#4 Drop, Aspire-additive posture); `ProtectKeysWithAzureKeyVaultDefaultCredentials` → #4's `AddCloudstrapDataProtection` (explicit, no longer bundled into the WebApi entry point); `W3CTracingMiddleware` → dropped by #2 (OTel owns trace context). Core already ships the normalized `ApplicationOptions.PathBase` this package's pipeline consumes.

---

## User Story

**As an** ASP.NET Core developer deploying a Web API to Azure,
**I want to** bootstrap API versioning, per-version OpenAPI documents with a Scalar reference UI, hardened problem-details error handling, correlation, health probes, CORS/HSTS/security headers, and optional JWT bearer authentication with two calls (`AddCloudstrapWebApi` / `UseCloudstrapWebApi`) and `Cloudstrap:` configuration,
**So that** my `Program.cs` stays under ten lines, my API speaks platform standards (RFC 9457 problem details, `api-supported-versions`, `/openapi/v{n}.json`, `/healthz` + `/ready`) and is secure by default — while every convention remains overridable and everything composes with an Aspire-style host.

---

## Acceptance Criteria

> AC-ASP2 and AC-A3 are carried **verbatim** from the founding spec. The founding spec has no dedicated WebApi AC block; AC-W1…AC-W15 are new, spec-specific criteria (precedent: AC-E1…AC-E15 in `_specs/4-ConfigKeyVaultHttpExtensions.md`). JWT tests run against a locally-issued test token / in-memory metadata — no live IdP in unit tests (founding AC-A1's Keycloak-container test belongs to #10).

| # | Given | When | Then |
|---|-------|------|------|
| AC-ASP2 | Any shipped Cloudstrap package | Its dependency closure is inspected | Zero `Aspire.*` packages. *(carried verbatim)* |
| AC-A3 | Solution searched for `Nihdi.AspNetCore` | — | Zero references. *(carried verbatim — this deliverable removes the suite's last planned `Nihdi.AspNetCore.Authentication.JwtBearer` usage from the WebApi path)* |
| AC-W1 | An app with controllers | `AddCloudstrapWebApi()` + `UseCloudstrapWebApi()` and a request to a versioned route | Controllers are mapped; the versioned endpoint responds; the response carries `api-supported-versions` (`ReportApiVersions`); URLs are lowercase by default. |
| AC-W2 | A controller without any `[ApiVersion]` attribute or version namespace | A request without an explicit version | The configured default version (default `1.0`, from `Cloudstrap:WebApi:ApiVersioning:DefaultVersion`) is assumed and the request succeeds (ported `DefaultApiVersionConvention` + `AssumeDefaultVersionWhenUnspecified`). |
| AC-W3 | `Cloudstrap:OpenApi:Enabled = true` (default) and controllers spanning versions `1.0` and `2.0` | The app starts | One OpenAPI document per discovered version is served (`/openapi/v1.json`, `/openapi/v2.json` — `Asp.Versioning.OpenApi` integration); document title/description default to `ApplicationOptions`-derived neutral values and are overridable via `Cloudstrap:OpenApi` + the document configure hook. |
| AC-W4 | `Cloudstrap:Scalar:Enabled` unset, environment `Development` | A request to the Scalar UI route (default `/scalar`) | The UI renders and references the OpenAPI documents. In `Production` with `Enabled` unset (or any environment with `Enabled = false`), the route is not mapped (404). No OAuth client secret can ever reach the browser — the options type has no secret property. |
| AC-W5 | `AddCloudstrapJwtBearer` registered and OpenAPI enabled | The document is generated | It contains the Bearer/OAuth2 security scheme (explicit `TokenUrl` — no IdP path convention) and marks secured operations; `[AllowAnonymous]` operations carry no security requirement (redesigned operation-security capability). |
| AC-W6 | An endpoint throws; `IncludeDetails` unset; environment `Production` | The response is inspected | `500` `application/problem+json` (RFC 9457) with a generic title, **no** exception type/message/stack trace, and the exception logged server-side once. |
| AC-W7 | Same, but environment `Development` (or `IncludeDetails = true` explicitly) | The response is inspected | `500` `application/problem+json` including exception type, message, stack trace and a depth-bounded inner-exception chain (the collapsed DevTst handler's detail, as problem-details extensions). |
| AC-W8 | `AddCloudstrapJwtBearer` with `Authority` + `Audience`; test-issued tokens | Requests with (a) a valid token, (b) a wrong-audience token, (c) a token expired beyond the reduced clock skew (60 s) | (a) 200 · (b) 401 (audience validation on) · (c) 401. `RequireHttpsMetadata` is enforced outside `Development`; inbound claim names are not remapped (`MapInboundClaims = false`). *(decided defaults, D-2 — each overridable)* |
| AC-W9 | `AddCloudstrapJwtBearer` registered (which installs a require-authenticated **fallback authorization policy**) | An unauthenticated request to an endpoint without `[AllowAnonymous]`; then to one with it; then with `RequireAuthenticatedEndpoints = false` | 401 challenge for the first; 200 for the second; with the flag off, both are anonymous (secure by default, two documented opt-outs). *(D-2)* |
| AC-W10 | `AddCloudstrapJwtBearer` **not** called | The app starts and serves requests | No authentication/authorization middleware failures; controllers are anonymous; the pipeline never assumes auth exists (the SUT Bff mode). |
| AC-W11 | `UseCloudstrapWebApi()` runs | `/healthz` and `/ready` are requested | Both respond per #4's `MapCloudstrapHealthChecks` (tag contract `"live"`/`"ready"`); calling `MapCloudstrapHealthChecks` again explicitly does not duplicate endpoints; health checks are registered only through the stock `IHealthChecksBuilder` (Aspire-additive posture, AC-ASP3 pattern). This package re-implements **no** health code. |
| AC-W12 | The pipeline handles a request carrying the correlation header; and one missing it on a `[CorrelationRequired]` endpoint | Responses are inspected | Correlation behaves exactly per #2's shipped contract (flow + 400 problem details); `Cloudstrap.WebApi` contains **no** correlation middleware type of its own. |
| AC-W13 | Defaults; then `Cloudstrap:WebApi:Cors:AllowedOrigins = ["https://app.example.com"]` | A cross-origin preflight request | With no origins configured: no `Access-Control-Allow-Origin` is ever emitted (no CORS policy registered). With origins configured: preflight succeeds for exactly those origins (credentialed; wildcard-subdomain support kept). Every response carries `X-Content-Type-Options: nosniff` + `Referrer-Policy: no-referrer`; HSTS is emitted outside `Development`. |
| AC-W14 | A fresh clone with this package | Build, tests, `dotnet format --verify-no-changes`, closure review, case-insensitive search for `Nihdi`, `Riziv` | All green; XML docs on all public API; package metadata complete; zero forbidden identifiers; the closure contains no `NSwag.*` (D-1), no `Nihdi.*`, no `NWebsec.*`, no `Aspire.*`; every dependency OSI-licensed and CPM-pinned. |
| AC-W15 | The WASM SUT Bff host adopts this package (`AddCloudstrapWebApi` + `UseCloudstrapWebApi` replacing its hand-rolled `AddControllers`/`UseRouting`/`UseCloudstrapCorrelation`/`MapControllers`/`MapCloudstrapHealthChecks` wiring, Blazor static files + fallback composed around it) | The E2E suite runs | The 17 pre-existing E2E tests stay green and ≥ 1 new E2E test proves, through the running app: a versioned endpoint, the Scalar UI, and the hardened problem-details error response (standing SUT rule / workflow rule 9). |

---

## Port Decision Table

One row per source public type/feature (all read in full; private members that constitute features are rowed too). "Superseded" = already adjudicated and shipped by an earlier deliverable — this deliverable consumes the shipped seam and must not rebuild it.

| Source | Verdict | Target | Justification |
|---|---|---|---|
| `WebApi\WebApplicationBuilderExtensions.AddNihdiWebApi` (modern overload) | **Redesign** | `AddCloudstrapWebApi(this WebApplicationBuilder, …)` | The composite entry point earns its place (founding goal: < 10 lines). Design does not: takes `NihdiConfiguration` + `ILogger` parameters (Cloudstrap binds from config), bundles DataProtection (#4's explicit `AddCloudstrapDataProtection` now) and JWT auth (separate `AddCloudstrapJwtBearer` — auth must be an explicit, reviewable call). |
| `AddNihdiWebApi` (Type-list overload) + `AddNihdiApiServicesLegacy` | **Drop** | — | Already `[Obsolete]` in source; reflection-based `AddExceptionHandler` invocation, not AOT-compatible. |
| `AddNihdiApiServices` | **Redesign** | folded into `AddCloudstrapWebApi` internals | Same content as the builder overload; two public spellings of one operation is surface without value. Kept inside: controllers + JSON defaults, versioning, exception handler, OpenAPI, route options, HSTS/CORS registration; removed: DataProtection, JWT, probe-delegate (findings 4, 10). |
| `AddNihdiJwtBearerAuthentication` | **Replace** | `AddCloudstrapJwtBearer` on stock `Microsoft.AspNetCore.Authentication.JwtBearer` | Founding decision (Auth Replacement) + roadmap-fixed. Internal `Nihdi.AspNetCore.*` builder chain, `AddNihdiAccessTokenManagement`, `CircuitServicesAccessor` + `AddDistributedMemoryCache` (Blazor-websocket artifacts, finding 6) all gone. Hardened defaults decided in D-2. ⚠️ auth risk area. |
| `AddNihdiJwtBearerAuthentication` `EnableDebug` event logging | **Drop** | — | Duplicates the stock handler's built-in logging (`Microsoft.AspNetCore.Authentication.JwtBearer` categories at Debug/Information); README documents the log category instead of owning event-wiring code. |
| `AddLegacyIssuer` flag + `UrlHelper.ReplaceLastPartWithLegacy` (`WebApi\UrlHelper.cs`, public) | **Drop** | — | Exists solely for the enterprise Keycloak legacy realm (finding 6) — De-NIHDI checklist; zero OSS value. Consumers with multi-issuer needs use the `Action<JwtBearerOptions>` hook (`TokenValidationParameters.ValidIssuers`). |
| `UseNihdiWebApiMiddleware` (+ private `ConfigureExceptionHandling`) | **Redesign** | `UseCloudstrapWebApi(this WebApplication, …)` | The one-call pipeline earns its place; the shape does not: `NihdiConfiguration`/`ILogger` parameters, DEV/TST structural switches (finding 2), dead HSTS/CORS registration (finding 4), `/probe.aspx`, superseded correlation/tracing middleware. New order: exception handler → HSTS → security headers → path base → routing → CORS → authN/Z (when registered) → correlation (#2) → hooks → controllers + health + OpenAPI/Scalar endpoints. Composite shape decided in D-3 — the pattern #6 (Mvc) and #7 (Worker) inherit. |
| private `ConfigureApiVersioning` (versioning defaults) | **Port** | internal wiring of `AddCloudstrapWebApi` | The defaults are sound and library-idiomatic: `DefaultApiVersion`, `AssumeDefaultVersionWhenUnspecified`, `ReportApiVersions`, query-string + URL-segment readers, `'v'VVV` group format, URL substitution, `VersionByNamespaceConvention`. Default version sourced from `Cloudstrap:WebApi:ApiVersioning` (finding 5), consumer hook kept. |
| `WebApi\DefaultApiVersionConvention` (internal) | **Port** | internal `DefaultApiVersionConvention` | 20 lines restoring assume-default behavior for unattributed controllers — still needed with Asp.Versioning 10; well-scoped, tested. |
| `WebApi\NormalizedQueryStringApiVersionReader` (internal) | **Drop** | — | Its own doc comment states its purpose: backward compatibility with clients of the deprecated internal library ("v1" → "1.0"). Cloudstrap has no legacy clients; stock readers suffice; the versioning hook lets a consumer add any reader. |
| `ExceptionHandlers\WebApiExceptionHandler` | **Redesign** | one internal `IExceptionHandler` producing RFC 9457 problem details | Capability (safe, consistent API error responses) is core value. Design replaced: bespoke `{StatusCode, Message}` JSON → `application/problem+json` via `IProblemDetailsService` (platform standard; consistent with #2's correlation 400). |
| `ExceptionHandlers\WebApiExceptionHandlerForDevTst` | **Redesign** | collapsed into the same handler's `IncludeDetails` mode | Roadmap directive (environment-taxonomy split → explicit overridable option, #1 precedent). Bonus: the source registration was unreachable (finding 2) — the collapsed design actually works. Detail payload (type/message/stack/bounded inner chain) kept as problem-details extensions. |
| `Swagger\SwaggerBootstrapper` | **Drop** | — | Dead code (finding 1) on the NSwag path dropped by D-1, carrying NIHDI-exclusive license/terms text and a UI-embedded client secret (`OAuth2ClientSettings.ClientSecret` — secret-to-browser anti-pattern). No successor file. |
| `Swagger\OperationProcessors\AspNetCoreOperationSecurityScopeProcessorCustom` | **Redesign** *(the NSwag type itself is dropped with D-1 — zero lines carried over)* | internal `Microsoft.AspNetCore.OpenApi` document/operation transformer | The `IOperationProcessor` implementation dies with NSwag (finding 1, D-1). Its **capability** — documents whose security metadata matches middleware-enforced auth, honoring `[AllowAnonymous]` — is real, is asserted by AC-W5, and is re-expressed from scratch on the built-in transformer API. Recorded as Redesign rather than Drop so the planner still builds the capability; nothing is ported textually. |
| `Correlation\CorrelationMiddleware` | **Drop** | — (use #2's `UseCloudstrapCorrelation`) | Strict subset of the shipped `CloudstrapCorrelationMiddleware` — the mandated diff is finding 3. A second correlation middleware would double-process every request. |
| *(moved in)* `Common\Scalar\ServiceCollectionExtensions.AddScalarNihdi` | **Redesign** | internal OpenAPI registration + `CloudstrapOpenApiOptions` (`Cloudstrap:OpenApi`) | Live-path capability kept. Removed: Keycloak `/protocol/openid-connect/token` path convention (finding 7 — explicit `TokenUrl` option instead), NIHDI title/description phrasing, hard-coded single `"v1"` document (finding 8 — per-version documents). |
| *(moved in)* `Common\Scalar\EndpointRouteBuilderExtensions.UseScalarNihdi` | **Redesign** | Scalar mapping inside `UseCloudstrapWebApi`, gated by `CloudstrapScalarOptions` (`Cloudstrap:Scalar`) | DEV/TST-only gating → explicit `Enabled` option with a Development-only default (De-NIHDI row). Kept and strengthened: no client secret to the browser (finding 7). |
| *(moved in)* old Core `Settings\Swagger\SwaggerConfiguration` + `SwaggerOAuthConfiguration` | **Drop** | — | Already `[Obsolete]` on `NihdiConfiguration.Swagger`; settings for the NSwag path dropped by D-1; `SwaggerOAuthConfiguration.ClientSecret` fed the UI-embedded secret. Nothing to carry — **no `Cloudstrap:Swagger` section exists**; documentation settings live in `Cloudstrap:OpenApi` + `Cloudstrap:Scalar`. |
| *(moved in)* old Core `Settings\Scalar\ScalarConfiguration` | **Redesign** | `CloudstrapOpenApiOptions` + `CloudstrapScalarOptions` (owned **here**, not Core) | Split per concern; `DefaultMajorVersion`/`DefaultMinorVersion` move to `Cloudstrap:WebApi:ApiVersioning:DefaultVersion` (finding 5). |
| *(moved in)* old Core `Settings\Scalar\ScalarOAuthConfiguration` | **Redesign** | `CloudstrapScalarOptions.OAuth` (ClientId, scopes) — **no secret property** | The source's prod-only secret validation becomes structurally unnecessary: the property does not exist (finding 7). |
| old Core `Settings\Security\JwtBearerConfiguration` (+ the WebApi-consumed slice of `SecurityConfiguration`/`AuthenticationConfiguration`: `EnableAuthentication`, `Authority`, `AllowedOrigins`, `EnableHttps`) | **Redesign** | `CloudstrapJwtBearerOptions` (`Cloudstrap:JwtBearer`) + `WebApiOptions.Cors`/`Hsts` | `Audience` stays required; `Authority` moves in as required; `EnableAuthentication` config flag → the explicit `AddCloudstrapJwtBearer` call (auth on/off must be visible in code, not buried in config); `AllowedOrigins` → `Cloudstrap:WebApi:Cors` (finding 4); `EnableHttps` → HSTS options. The rest of `Settings\Security\` remains #9/#10 material — untouched here. |
| *(routed by #4)* `Common\Services\ServiceCollectionExtensions.AddNihdiWebApiProtections` (HSTS + `IHttpContextAccessor` + CORS) | **Redesign** | HSTS + CORS registration inside `AddCloudstrapWebApi`, **activated** in `UseCloudstrapWebApi` | Finding 4: registered-but-inert in the source WebApi pipeline; insecure CORS fallback removed (no origins → no policy). HSTS defaults hardened-but-sane: 365 d + subdomains, **`Preload` off by default** (preload is a domain-owner commitment a library must not make — override available). |
| *(routed by #4)* NWebsec security headers (`XContentTypeOptionsMiddleware`, `UseReferrerPolicy`) — the deferred inline-vs-library choice | **Redesign** | ~15 lines of internal inline middleware (`X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`) | For a JSON API exactly two response headers matter; a `NetEscapades.AspNetCore.SecurityHeaders` dependency to emit two constant headers fails the minimize-dependencies bar. #6 (Mvc, HTML surface: CSP etc.) re-evaluates the library with its own evidence. NWebsec stays dead suite-wide (#4 decision). |
| *(routed by #4)* `Common\Extensions\ApplicationBuilderExtensions.UseNihdiPathBase` | **Redesign** | `UsePathBase(ApplicationOptions.PathBase)` when non-empty, inside `UseCloudstrapWebApi` | Core already ships the normalized `PathBase`. Dropped inside it: the `basepath` env var, the `EnvironmentIsLocal()` skip, and the silent `/{WorkloadName}` auto-default (enterprise-ingress convention; surprising 404s for OSS consumers — no path base unless configured). |
| *(routed by #4)* `Common\Options\AddWebOptions` (4 delegates incl. `ConfigureProbeHealthChecksDelegate`) | **Drop** | — | Its only WebApi-path use is the probe delegate, superseded by the stock `IHealthChecksBuilder` (#4 Drop of `AddProbeHealthChecks`); the other delegates are unused on the WebApi path. Service-side extensibility = call the services yourself before `Build()`. |
| *(routed by #4)* `Common\Options\UseWebOptions` (4 pipeline hooks) | **Redesign** | `WebApiPipelineOptions` hook properties (before-routing / before-authorization / before-endpoints / endpoints) | The hook points are proven necessary (three source consumers); the shape is modernized: plain `Action<IApplicationBuilder>` / `Action<IEndpointRouteBuilder>`, no `NihdiConfiguration` parameter (consumers resolve options from DI), no nested delegate types. |
| *(routed by #4)* `Common\Serialization\DictionaryTKeyEnumTValueConverter` | **Drop** | — | Finding 9: stock STJ covers enum-keyed dictionaries since .NET 5; remaining deltas are legacy-payload compat. The JSON hook is the escape hatch. |
| `AddControllers().AddJsonOptions(...)` defaults (`WhenWritingNull`) + `RouteOptions` lowercase URLs/query strings | **Port** | defaults inside `AddCloudstrapWebApi` | Sound, widely-expected API opinions; both overridable (JSON hook / `LowercaseUrls` option). |
| csproj `Asp.Versioning.Mvc` + `Asp.Versioning.Mvc.ApiExplorer` 10.0.0 | **Port** | same packages, CPM-pinned | MIT, dotnet-org maintained (10.0.0, 2026-04-21), the .NET versioning standard — exactly what the roadmap says to keep. |
| csproj `NSwag.AspNetCore` 14.7.1 | **Replace** | `Microsoft.AspNetCore.OpenApi` + `Asp.Versioning.OpenApi` + `Scalar.AspNetCore` | Finding 1: only dead code uses NSwag; the live source path already runs on the replacement stack; net10.0-only removes NSwag's remaining reason (multi-targeting). **Decided D-1 (2026-08-02)**; the founding spec's Package Map row and Decisions Made table were amended accordingly by user authorization. AC-W14 asserts zero `NSwag.*` in the closure. |
| csproj `Nihdi.AspNetCore.Authentication.JwtBearer` 5.2.5 | **Replace** | `Microsoft.AspNetCore.Authentication.JwtBearer` | Founding Auth Replacement + AC-A3. |
| csproj `Nihdi.Core.Health` 1.0.24 | **Drop** | — (superseded by #4) | Roadmap-fixed: reuse `MapCloudstrapHealthChecks` + the `"live"`/`"ready"` tag contract; no health code in this package (finding 10). |
| Consumed-and-superseded: `W3CTracingMiddleware`, `UseNihdiHealthChecksInternal`, `AddNihdiLoadBalancerProbe`, `AddProbeHealthChecks`, `ProtectKeysWithAzureKeyVaultDefaultCredentials`, `AddNihdiCommonServices` | **Superseded** | #2 / #4 shipped seams | Adjudicated in `_specs/2-ObservabilityBase.md` / `_specs/4-ConfigKeyVaultHttpExtensions.md`; listed so the planner reuses, never rebuilds (finding 10). |

**Tally**: 4 Port · 13 Redesign · 3 Replace · 10 Drop · 6 Superseded-reuse.

---

## Public API Sketch

Namespace **`Cloudstrap.WebApi`** (single namespace — Core/Extensions precedent). Everything `public sealed` / `static`; middleware, transformers, validators and the exception handler `internal`. Type names carry the `Cloudstrap` prefix where the ecosystem name collides (`Microsoft.AspNetCore.OpenApi.OpenApiOptions`, `Scalar.AspNetCore.ScalarOptions`, `JwtBearerOptions`) — precedent: `CloudstrapObservabilityOptions` (#2).

```text
Cloudstrap.WebApi
├── WebApplicationBuilderExtensions (static)
│     AddCloudstrapWebApi(this WebApplicationBuilder builder,
│                         Action<CloudstrapWebApiConfigurator>? configure = null)
│         : WebApplicationBuilder
│       — binds + validates WebApiOptions / CloudstrapOpenApiOptions / CloudstrapScalarOptions;
│         ensures Core options registration (AddCloudstrapCore, idempotent) and correlation
│         services (#2's AddCloudstrapCorrelation, idempotent); AddControllers + JSON defaults;
│         AddProblemDetails + the internal exception handler; API versioning + ApiExplorer +
│         per-version OpenAPI documents (Asp.Versioning.OpenApi); RouteOptions defaults;
│         AddHealthChecks() (stock, additive); HSTS + CORS registration per options.
│         Registers NO authentication (see AddCloudstrapJwtBearer).
│     AddCloudstrapJwtBearer(this WebApplicationBuilder builder,
│                            Action<JwtBearerOptions>? configure = null)
│         : WebApplicationBuilder                      ⚠️ auth risk area (defaults: D-2)
│       — binds + validates CloudstrapJwtBearerOptions (Cloudstrap:JwtBearer); stock
│         AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer with the four
│         hardened defaults of D-2 (audience validation on, ClockSkew 60 s, HTTPS metadata
│         outside Development, MapInboundClaims = false); AddAuthorization() with a
│         require-authenticated FallbackPolicy when RequireAuthenticatedEndpoints is true;
│         the configure hook runs last (full override, e.g. extra ValidIssuers).
│
├── WebApplicationExtensions (static)
│     UseCloudstrapWebApi(this WebApplication app,
│                         Action<WebApiPipelineOptions>? configure = null)
│         : WebApplication            (composite shape decided D-3; inherited by #6 and #7)
│       — pipeline, in order: exception handler (problem details, all environments) → UseHsts
│         (non-Development, when enabled) → security-header middleware → UsePathBase (when
│         ApplicationOptions.PathBase non-empty) → hooks.BeforeRouting → UseRouting → UseCors
│         (only when origins configured) → UseAuthentication/UseAuthorization (only when
│         AddCloudstrapJwtBearer was called) → hooks.BeforeAuthorization (before UseAuthorization)
│         → UseCloudstrapCorrelation (#2) → hooks.BeforeEndpoints → MapControllers
│         (authorization comes from the D-2 fallback policy, not per-endpoint calls)
│         → MapCloudstrapHealthChecks (#4) → OpenAPI documents + Scalar UI (per options)
│         → hooks.ConfigureEndpoints.
│
├── CloudstrapWebApiConfigurator          — code-level hooks carried by AddCloudstrapWebApi
│     ApiVersioning : Action<ApiVersioningOptions>?    — after Cloudstrap defaults
│     Json          : Action<JsonOptions>?             — after Cloudstrap defaults
│     OpenApi       : Action<OpenApiOptions>?          — per generated document (Microsoft type)
│     Scalar        : Action<ScalarOptions>?           — Scalar.AspNetCore passthrough
│     Mvc           : Action<IMvcBuilder>?             — e.g. AddApplicationPart
│
├── WebApiPipelineOptions                 — code-level hooks carried by UseCloudstrapWebApi
│     BeforeRouting       : Action<IApplicationBuilder>?
│     BeforeAuthorization : Action<IApplicationBuilder>?
│     BeforeEndpoints     : Action<IApplicationBuilder>?
│     ConfigureEndpoints  : Action<IEndpointRouteBuilder>?
│     MapControllers      : bool = true                — off for minimal-API-only consumers
│
├── WebApiOptions                         — section Cloudstrap:WebApi (owned HERE)
│     const SectionName = "Cloudstrap:WebApi"
│     ApiVersioning : ApiVersioningSettings
│         DefaultVersion                    : string = "1.0"     (finding 5 — moved from Scalar)
│         AssumeDefaultVersionWhenUnspecified : bool = true
│         ReportApiVersions                 : bool = true
│     Json : JsonSettings
│         IgnoreNullValues : bool = true                          (WhenWritingNull default)
│     LowercaseUrls : bool = true
│     Cors : CorsSettings
│         AllowedOrigins : IList<string> (get-only init — binder append caveat documented)
│         — empty (default) → no CORS policy registered at all (secure default, finding 4)
│     Hsts : HstsSettings
│         Enabled : bool = true · MaxAgeDays : int = 365 · IncludeSubDomains : bool = true ·
│         Preload : bool = false                                  (deliberate change 7)
│     ExceptionHandling : ExceptionHandlingSettings
│         IncludeDetails : bool? = null   — null → details only in Development (explicit,
│                                           overridable replacement of the ForDevTst taxonomy)
│
├── CloudstrapOpenApiOptions              — section Cloudstrap:OpenApi (owned HERE, not Core)
│     const SectionName = "Cloudstrap:OpenApi"
│     Enabled     : bool = true            — per-version document generation + /openapi/{doc}.json
│     Title       : string?                — null → derived from ApplicationOptions (neutral)
│     Description : string?
│     OAuth : OpenApiOAuthSettings         — the documented security-scheme flow (AC-W5)
│         TokenUrl         : Uri?          — explicit; NO IdP path convention (finding 7)
│         AuthorizationUrl : Uri?
│         Scopes : IDictionary<string,string> (get-only init — append caveat; default empty)
│
├── CloudstrapScalarOptions               — section Cloudstrap:Scalar (owned HERE)
│     const SectionName = "Cloudstrap:Scalar"
│     Enabled : bool? = null               — null → mapped in Development only; explicit wins
│     Path    : string = "/scalar"
│     OAuth : ScalarOAuthSettings
│         ClientId : string?               — deliberately NO ClientSecret property (finding 7)
│         SelectedScopes : IList<string> (get-only init — append caveat; default empty)
│
└── CloudstrapJwtBearerOptions            — section Cloudstrap:JwtBearer (owned HERE)  ⚠️ D-2
      const SectionName = "Cloudstrap:JwtBearer"
      Authority                   : string   — required
      Audience                    : string   — required (source parity: [Required] kept)
      RequireHttpsMetadata        : bool? = null   — null → true except Development   [D-2, override]
      ClockSkewSeconds            : int = 60       — reduced from the stock 300        [D-2, override]
      MapInboundClaims            : bool = false   — raw JWT claim names               [D-2, override]
      RequireAuthenticatedEndpoints : bool = true  — installs a require-authenticated
                                                     AuthorizationOptions.FallbackPolicy;
                                                     [AllowAnonymous] opts out per endpoint,
                                                     false opts out globally           [D-2, override]
      — every one of the four hardened defaults is a settable property of this options type
        (every-convention-has-an-override rule); the Action<JwtBearerOptions> hook, running last,
        is the final escape hatch for anything the type does not model.

internal: CloudstrapWebApiExceptionHandler (single IExceptionHandler, problem details, detail
mode), SecurityHeadersMiddleware (~15 lines: nosniff + no-referrer), DefaultApiVersionConvention
(ported), OpenAPI security transformer (AC-W5), source-generated [OptionsValidator] validators
(inherited fact #1 — no Microsoft.Extensions.Options.DataAnnotations).
```

**Configuration** — this package owns four new subsections: `Cloudstrap:WebApi`, `Cloudstrap:OpenApi`, `Cloudstrap:Scalar`, `Cloudstrap:JwtBearer` (multi-subsection precedent: #4). It **consumes** Core's shipped `Cloudstrap:Application` (PathBase, WorkloadName for OpenAPI metadata), `Cloudstrap:HealthChecks` and `Cloudstrap:Correlation` — never redefining them. Collection/dictionary properties (`Cors.AllowedOrigins`, `OAuth.Scopes`, `SelectedScopes`) follow inherited fact #2: get-only initialized, configured values **append to defaults** — all three ship empty defaults precisely so the caveat stays theoretical, and the package README documents it.

---

## Behaviors & Conventions

| Behavior | Default | Override |
|---|---|---|
| API versioning | Default version `1.0` assumed when unspecified (incl. unattributed controllers via the ported convention); `api-supported-versions`/`api-deprecated-versions` reported; readers: `?api-version=` query string + URL segment (`'v'VVV`, substituted in routes); namespace-based version convention active. | `Cloudstrap:WebApi:ApiVersioning:*`; `configurator.ApiVersioning` hook (add readers, conventions — full `ApiVersioningOptions` access). |
| OpenAPI documents | One document per discovered API version at `/openapi/v{n}.json` (`Asp.Versioning.OpenApi`), neutral title/description derived from `ApplicationOptions`, sunset/deprecation policies included; Bearer security scheme + per-operation requirements only when JWT is registered, `[AllowAnonymous]` honored. | `Cloudstrap:OpenApi:*` (incl. `Enabled = false`); `configurator.OpenApi` per-document hook (transformers). |
| Scalar UI | Mapped at `/scalar` in `Development` only; references all version documents; OAuth client id + scopes only — a client secret is unrepresentable. | `Cloudstrap:Scalar:Enabled` (explicit `true` exposes it anywhere — a conscious production choice), `Path`, `configurator.Scalar` hook. |
| Error responses | RFC 9457 `application/problem+json` for unhandled exceptions in **all** environments (no structural environment switch); generic payload by default, full detail (type/message/stack/bounded inner chain) when `IncludeDetails` resolves true (Development, unless overridden); exception always logged server-side. `ApplicationOptions.ExceptionHandlerPath` is **not consumed** here (the handler terminates; re-execution is the MVC/#6 pattern). | `Cloudstrap:WebApi:ExceptionHandling:IncludeDetails`; consumers may register their own `IExceptionHandler`s before `AddCloudstrapWebApi` — registration order lets them intercept first (source's `configureExceptionHandlers` contract, preserved by ordering instead of a dedicated parameter). |
| JSON | `WhenWritingNull` ignore condition (null properties omitted); otherwise stock STJ web defaults — including native enum-keyed dictionaries (finding 9). | `configurator.Json` hook (full `JsonOptions` access). |
| Routing | Lowercase URLs + query strings. | `Cloudstrap:WebApi:LowercaseUrls = false`; standard `RouteOptions` configuration. |
| HSTS | Emitted outside `Development`: 365 days, include-subdomains, **no preload**. | `Cloudstrap:WebApi:Hsts:*`; stock `AddHsts` configuration afterwards wins. |
| Security headers | `X-Content-Type-Options: nosniff` + `Referrer-Policy: no-referrer` on every response (inline middleware — no NWebsec, no NetEscapades; #6 re-evaluates for HTML surfaces). | `hooks.BeforeRouting` to add/replace header middleware; values not configurable (constants for a JSON API — overriding means writing your own middleware, documented). |
| CORS | No origins configured → **no CORS policy registered** (browser default-deny; the source's `AllowAnyOrigin` fallback is gone). Origins configured → default policy with exactly those origins, credentialed, wildcard-subdomain support, any header/method. | `Cloudstrap:WebApi:Cors:AllowedOrigins`; consumers needing named/multiple policies use stock `AddCors`/`RequireCors` — Cloudstrap's policy is additive, not exclusive. |
| Path base | Applied from Core's normalized `ApplicationOptions.PathBase` only when non-empty; no env-var, no workload-name magic. | `Cloudstrap:Application:PathBase`. |
| Health probes | `MapCloudstrapHealthChecks` (#4) inside the pipeline: `/healthz` (tag `live`) + `/ready` (tag `ready`), idempotent; `AddHealthChecks()` called by the entry point so probes always answer. No health code owned here; registrations stay additive via the stock builder (Aspire posture). | Core's `Cloudstrap:HealthChecks:*`; extra checks via stock `AddHealthChecks()`. |
| Correlation | #2's shipped middleware after routing: inbound `X-Correlation-ID` (configurable) flows, generated when absent, enforced per `Cloudstrap:Correlation` with 400 problem details. | `Cloudstrap:Correlation:*` (#2's contract). |
| Authentication | None unless `AddCloudstrapJwtBearer` is called (explicit code, not a config flag). When called: the four hardened stock JWT bearer defaults of D-2 (audience validation on · `ClockSkewSeconds = 60` · `RequireHttpsMetadata` outside Development · `MapInboundClaims = false`) plus a require-authenticated fallback authorization policy. | Each of the four is a property on `Cloudstrap:JwtBearer` (`ClockSkewSeconds`, `RequireHttpsMetadata`, `MapInboundClaims`, `Audience`/`Authority` validation is required by design); the fallback policy is disabled by `RequireAuthenticatedEndpoints = false` or bypassed per endpoint with `[AllowAnonymous]`; the `Action<JwtBearerOptions>` hook runs last and has final say. |
| Aspire coexistence | The versioning/OpenAPI/Scalar surface does not overlap ServiceDefaults. The overlap points route through already-composable seams: health (stock builder + #4's idempotent mapping — AC-ASP3 posture), OTel/correlation (#2 owner/contribute modes), typed clients (#4). Zero `Aspire.*` (AC-ASP2). | — (posture). |

**Test strategy (spec-level):** NUnit 4 on Microsoft.Testing.Platform in `src/Test/UnitTest/Cloudstrap.WebApi.Tests`; integration-style tests boot the pipeline in-process (`Microsoft.AspNetCore.TestHost`, already CPM-pinned test-only) to assert versioned routing, document content, problem-details shapes (both modes), header/CORS behavior, and JWT outcomes with locally-issued test tokens (no live IdP; signing key injected via the `JwtBearerOptions` hook). The demonstration slice (AC-W15) converts the WASM SUT Bff to `AddCloudstrapWebApi`/`UseCloudstrapWebApi` and adds ≥ 1 Playwright E2E test (versioned endpoint + Scalar UI + hardened error response) while keeping the existing 17 E2E tests green.

---

## Dependencies

| Package | License | Evidence & justification |
|---|---|---|
| `Cloudstrap.Core` *(project reference)* | MIT | `ApplicationOptions` (PathBase, workload-derived OpenAPI metadata), `HealthChecksOptions`, `CorrelationOptions`. |
| `Cloudstrap.Observability` *(project reference)* | MIT | The one correlation middleware (`UseCloudstrapCorrelation`, `AddCloudstrapCorrelation`) — finding 3. |
| `Cloudstrap.Extensions` *(project reference)* | MIT | `MapCloudstrapHealthChecks` — finding 10; also makes `AddCloudstrapHttpServiceClient` available to WebApi consumers with one package. |
| `Asp.Versioning.Mvc` | MIT | [10.0.0, dotnet org, 2026-04-21, 122.9M downloads](https://www.nuget.org/packages/Asp.Versioning.Mvc) — kept from source (roadmap-fixed). |
| `Asp.Versioning.Mvc.ApiExplorer` | MIT | Same release train; group-name/URL-substitution formatting the documents depend on. |
| `Asp.Versioning.OpenApi` 10.0.0 | MIT | **Taken (D-1)**. New in v10 ([.NET Blog, 2026-04](https://devblogs.microsoft.com/dotnet/api-versioning-in-dotnet-10-applications/)): per-version documents + sunset/deprecation policies in the box — eliminates the hand-written version-filter transformer this package would otherwise own (finding 8). Same release train as the two `Asp.Versioning.Mvc*` pins. |
| `Microsoft.AspNetCore.OpenApi` 10.0.x | MIT | **Taken (D-1)**. Microsoft, ships with the .NET 10 release train; the document generator + transformer API the live source path already uses (finding 1). Requires an explicit PackageReference (not in the shared framework); pinned to the same 10.0.x patch line as the other Microsoft pins. |
| `Scalar.AspNetCore` 2.16.16 | MIT | **Taken (D-1)**. [2.16.16, updated 2026-07-20](https://www.nuget.org/packages/Scalar.AspNetCore), scalar/scalar monorepo, very active. The reference UI the source already ships. |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | MIT | Microsoft, 10.0.x release train. Replaces `Nihdi.AspNetCore.Authentication.JwtBearer` (founding Auth Replacement, AC-A3). ⚠️ auth dependency — human review. |
| `Microsoft.AspNetCore.App` *(framework reference)* | MIT | MVC controllers, endpoint routing, authentication middleware. Third framework reference in the suite (#2/#4 precedent; README documents the runtime-image consequence). |

**Four new CPM pins** land in `src/Directory.Packages.props`: `Asp.Versioning.Mvc` + `Asp.Versioning.Mvc.ApiExplorer` + `Asp.Versioning.OpenApi` (10.0.0), `Microsoft.AspNetCore.OpenApi` (10.0.x), `Scalar.AspNetCore` (2.16.16), `Microsoft.AspNetCore.Authentication.JwtBearer` (10.0.x) — counting the `Asp.Versioning.*` trio as one release-train pin group; exact patch versions confirmed against the feed at plan time.

Considered and **rejected**: `NSwag.AspNetCore` (dead path in source, redundant with the built-in generator on net10.0 — decided out in D-1; MIT and maintained, so the rejection is redundancy, not health), `Swashbuckle.AspNetCore` (not in source; same redundancy), `NetEscapades.AspNetCore.SecurityHeaders` (MIT, active — but two constant headers do not justify a dependency here; #6 re-evaluates), `NWebsec.*` (dead suite-wide, #4), `Duende.AccessTokenManagement` (belongs to #9/#10 — token *validation* here needs none of it), any `Nihdi.*` internal package (founding replacements), any `Aspire.*` package (AC-ASP2).

---

## Deliberate Behavior Changes (vs. the source library)

1. **Error responses become RFC 9457 problem details** (`application/problem+json`) instead of the bespoke `{StatusCode, Message}` JSON; one handler with an explicit detail switch replaces the (unreachable) DEV/TST handler and the (dead) `/error` re-execution path — findings 2. Consistent with #2's correlation 400.
2. **JWT bearer is stock ASP.NET Core with the four hardened defaults of D-2** (clock skew 60 s instead of the framework's 300 s; HTTPS metadata required outside Development; no inbound claim remapping; require-authenticated fallback policy); the legacy-issuer Keycloak machinery, `EnableDebug` event wiring, `CircuitServicesAccessor` and `AddDistributedMemoryCache` registrations are gone. Auth activation moves from the `Security:EnableAuthentication` config flag to the explicit `AddCloudstrapJwtBearer` call. All four defaults are overridable via `Cloudstrap:JwtBearer`.
3. **CORS is secure by default**: no configured origins → no CORS policy at all (source: `AllowAnyOrigin` with a log warning). HSTS and CORS are now actually activated by the pipeline that registers them (source: registered but inert — finding 4).
4. **OpenAPI is per-version and neutral**: one document per discovered API version (source: single hard-coded `"v1"`); NIHDI titles/license text and the Keycloak token-URL convention replaced by `ApplicationOptions`-derived defaults and an explicit `TokenUrl`. Scalar exposure is an explicit option (Development-only default) instead of the DEV/TST taxonomy.
5. **`?api-version=v1` is no longer accepted** (only `1.0`/`1` etc.) — the normalizing reader existed for old internal clients; the versioning hook restores it in one line for anyone who wants it.
6. **Enum-keyed dictionary JSON uses stock STJ semantics** (case-sensitive-then-insensitive custom parsing and `PropertyNamingPolicy`-on-keys are gone — finding 9).
7. **HSTS no longer defaults to `preload`** (a browser-preload-list commitment belongs to the domain owner, not a library); max-age/subdomains kept.
8. **No path-base magic**: applied only when `Cloudstrap:Application:PathBase` is set — the `basepath` env var and `/{WorkloadName}` auto-default are gone.
9. **Probes**: `/probe.aspx`, `/live`, `/health`-on-port-9000 and the LOC switch are gone — `/healthz` + `/ready` via #4 (paths configurable), unchanged from what #4 already shipped.
10. **Security headers are two inline constants** (nosniff, no-referrer) instead of NWebsec middleware.

---

## Edge Cases

| Case | Expected behavior |
|------|-------------------|
| Request with an unknown/unsupported API version | Asp.Versioning's stock 400 problem-details response (`UnsupportedApiVersion`) — not customized. |
| `[ApiVersionNeutral]` controller | Served for any version; appears in every OpenAPI document per the library's ApiExplorer semantics. |
| `Cloudstrap:OpenApi:Enabled = false` but `Cloudstrap:Scalar:Enabled = true` | Startup validation fails naming both keys — a UI over no documents is a misconfiguration, not a silent 404. |
| `AddCloudstrapJwtBearer` called, `Authority`/`Audience` missing | Startup fails via options validation naming `Cloudstrap:JwtBearer:Authority`/`Audience` (inherited `[OptionsValidator]` pattern). |
| `AddCloudstrapJwtBearer` called but `UseCloudstrapWebApi` not used (custom pipeline) | Auth services registered; the consumer owns middleware order — README documents the required `UseAuthentication`/`UseAuthorization` placement. |
| Consumer registers own `IExceptionHandler` before `AddCloudstrapWebApi` | It runs first (framework chain order); Cloudstrap's handler remains the terminal fallback — the source's ordering contract, preserved without a dedicated parameter. |
| `UseCloudstrapWebApi` called twice | Throws `InvalidOperationException` — a pipeline is built once (unlike idempotent service registrations). |
| Bff SUT composition (Blazor host) | `UseBlazorFrameworkFiles`/`UseStaticFiles` before `UseCloudstrapWebApi`, `MapFallbackToFile` after — endpoint registrations on `WebApplication` compose; proven by AC-W15 with the existing E2E suite green. |
| `MapControllers = false` (pipeline option) | Everything else still wired; consumer maps minimal APIs via `hooks.ConfigureEndpoints`; versioned minimal APIs are the consumer's own `Asp.Versioning.Http` usage (documented, not wrapped). |
| Health endpoints when the consumer also calls `MapCloudstrapHealthChecks` manually | One set of endpoints (#4's marker-based idempotence). |
| Development E2E run asserting the safe error shape | Set `IncludeDetails = false` explicitly in the SUT config for the error-path E2E test (explicit beats environment default). |

---

## Out of Scope (this deliverable — the planner must not resurrect any of it)

- Everything in the founding spec's global Out of Scope: message encryption, MessagingBridge, Dynatrace, ServicePlatform/ServicePulse, `Cloudstrap.Functional`, `Cloudstrap.Aspire`.
- OIDC login and client-credentials token acquisition (`Duende.AccessTokenManagement`) — **#9/#10**; this package only validates inbound JWTs.
- The remainder of old Core `Settings\Security\` (`OpenIdConnectConfiguration`, `ClientCredentialsConfiguration`, `OAuthConfiguration`, `AuthenticationFlow`, cache-lifetime settings) — **#9/#10**.
- MVC/session/HTML-surface middleware, CSP-grade security headers and the `NetEscapades.AspNetCore.SecurityHeaders` re-evaluation — **#6**. Worker health listener — **#7**.
- Everything Dropped above: NSwag bootstrapper + operation processor, `UrlHelper`, legacy `AddNihdiWebApi`/`AddNihdiApiServicesLegacy` overloads, `NormalizedQueryStringApiVersionReader`, WebApi `CorrelationMiddleware`, Swagger settings pair, `AddWebOptions`, `DictionaryTKeyEnumTValueConverter`, `EnableDebug`/`AddLegacyIssuer`, `/probe.aspx`.
- Re-implementation of anything Superseded: health mapping/tags (#4/#2), correlation (#2), DataProtection (#4), KeyVault (#4), typed clients (#4), W3C trace middleware (#2).
- NSwag-based client/TS code generation (never a feature of the live source path; consumers can point NSwag/Kiota at the generated documents themselves).
- Automated tests against a live IdP (deferred to #10's Keycloak-container test per founding AC-A1).

---

## Decision Log (gate answers, 2026-08-02 — zero Open Questions remain; spec is planner-ready)

All three gate questions were answered by the user on 2026-08-02; each accepted this spec's recommendation. The full findings/options/rationale for each question live in this repo's git history of this file (the pre-gate draft); the decided outcomes are:

| # | Question | Answer (user, 2026-08-02) |
|---|---|---|
| **D-1** | OpenAPI stack: keep NSwag 14.x or adopt the built-in generator + Scalar? (⚠️ public API one-way door; the founding spec's Package Map row read "NSwag/Scalar") | **Drop NSwag; adopt the stock stack**: `Microsoft.AspNetCore.OpenApi` 10.0.x + `Asp.Versioning.OpenApi` 10.0.0 + `Scalar.AspNetCore` 2.16.16 (all MIT; two of the three Microsoft/dotnet-org). Rationale preserved from the analysis: the source's NSwag code is unreferenced **dead code** (`SwaggerBootstrapper`, `AspNetCoreOperationSecurityScopeProcessorCustom` — finding 1) while its live path already calls `AddOpenApi()` + `MapScalarApiReference()`; Asp.Versioning 10 ships per-version documents with sunset/deprecation policies **in the box**, eliminating the hand-written version-filter transformer NSwag would force this package to own (finding 8); Cloudstrap is `net10.0`-only, so NSwag's multi-targeting rationale is gone; the built-in generator has been the platform default since .NET 9. Consequences applied here: `NSwag.AspNetCore` is not a dependency (AC-W14 asserts zero `NSwag.*` in the closure), `SwaggerBootstrapper` and both old-Core `Settings\Swagger\*` classes are Drop with no successor, no `Cloudstrap:Swagger` section exists, and the operation-security capability is rebuilt from scratch on the built-in transformer API (AC-W5). **Founding-spec amendment made under explicit user authorization**: `_specs/Cloudstrap.md` Package Map WebApi row now reads "Versioning, OpenAPI (built-in `Microsoft.AspNetCore.OpenApi` + `Asp.Versioning.OpenApi`) + Scalar UI, middleware", and a new **Decisions Made** row "API documentation stack" records the decision and points back to this log entry. |
| **D-2** | JWT bearer hardened defaults — the four values and the secure-by-default posture (⚠️ **auth risk area**; the user is the sign-off authority) | **All four accepted as proposed**: (a) `ClockSkewSeconds = 60` (framework default is 300); (b) `MapInboundClaims = false` — raw JWT claim names, source-default parity; (c) `RequireHttpsMetadata` = `null` → enforced in every environment except `Development`; (d) `RequireAuthenticatedEndpoints = true` → registering `AddCloudstrapJwtBearer` installs a **require-authenticated `AuthorizationOptions.FallbackPolicy`**, with `[AllowAnonymous]` as the per-endpoint opt-out and the flag as the global opt-out. **Every one of the four is a settable property of `CloudstrapJwtBearerOptions` (section `Cloudstrap:JwtBearer`)** — the every-convention-has-an-override rule is satisfied per default, not merely by the escape hatch; the `Action<JwtBearerOptions>` hook runs last and remains the final override for anything the options type does not model. Covered by AC-W8 (a/b/c) and AC-W9 (d, incl. both opt-outs). Auth remains a human-review area at every plan gate that touches it. |
| **D-3** | Pipeline ownership: composite `UseCloudstrapWebApi` or granular-only surface? (sets the pattern #6/#7 inherit) | **Composite `UseCloudstrapWebApi(app, configure)`** with the four hook points (`BeforeRouting`, `BeforeAuthorization`, `BeforeEndpoints`, `ConfigureEndpoints`) plus the `MapControllers` switch, exactly as sketched. Rationale: middleware **ordering** is the domain knowledge this package exists to encode (exception handler before routing, correlation after routing, auth between); the granular escape hatch already exists because every constituent piece is independently callable (`AddCloudstrapJwtBearer`, `MapCloudstrapHealthChecks` from #4, `UseCloudstrapCorrelation` from #2) — a consumer who wants full control simply does not call the composite. **This is the pipeline pattern deliverables #6 (Mvc) and #7 (Worker) inherit**: composite `Add*`/`Use*` pair, hooks as plain `Action<IApplicationBuilder>`/`Action<IEndpointRouteBuilder>` properties on an options carrier, no configuration object or logger parameters. AC-W15 proves the shape composes inside a mixed Blazor/API host (the WASM SUT Bff) before #6/#7 inherit it. |
