# Spec: Config/KeyVault/HTTP Extensions — `Cloudstrap.Extensions` (Roadmap Deliverable #4)

> **Approved 2026-08-02 — zero Open Questions remain; spec is planner-ready.** All three gate questions were resolved per this spec's recommendations (see the Decision Log at the end): the runtime handler-factory token seam `IAccessTokenHandlerProvider` (OQ-1), the additive Core amendment `HttpClientServiceOptions.HealthCheckPath` plus the `AspNetCore.HealthChecks.Uris` dependency (OQ-2), and `KeyVaultKeyId` required when DataProtection is enabled (OQ-3).
>
> Sources: `_plans/ROADMAP.md` §4 (hand-off brief, file inventory re-verified 2026-08-02) · `_specs/Cloudstrap.md` (Decisions Made, De-NIHDI-fication Checklist rows "KeyVault name" and "Storage account", Aspire Coexistence AC-ASP1–AC-ASP3, hosting-targets decision 2026-07-25) · `_specs/2-ObservabilityBase.md` (finding 7 + Port Decision Table: `ApiLivenessHealthCheck` routed **to** this deliverable; `AspNetCore.HealthChecks.*` suggested with license/maintenance verification owed here) · **shipped** code in `src/Cloudstrap.Core/` (`CloudstrapOptions.HttpClients`, `HttpClientServiceOptions`, `TokenRequestOptions`, `HealthChecksOptions`, `ApplicationOptions`) and `src/Cloudstrap.Observability/` (`AddCloudstrapCorrelationHandler`, `CloudstrapHealthCheckTags`, `UseCloudstrapObservability`) · source reference repo (read-only) `D:\source\Nihdi-Core-Configuration\Nihdi-Core-Configuration\src\Nihdi.Core.Configuration.Common\` — every file listed in the Port Decision Table was opened and read, plus its consumers in `WebApi\`, `Mvc\`, `BlazorServer\`, `Worker\` and the old test projects · external evidence gathered 2026-08-02: [AspNetCore.HealthChecks.Uris on NuGet](https://www.nuget.org/packages/AspNetCore.HealthChecks.Uris/) (9.0.0, 2024-12-19, Apache-2.0, [Xabaril repo](https://github.com/Xabaril/AspNetCore.Diagnostics.HealthChecks)) and [NetEscapades.AspNetCore.SecurityHeaders](https://github.com/andrewlock/NetEscapades.AspNetCore.SecurityHeaders) (1.3.1, MIT, active — routed recommendation for #5/#6 only).
>
> **⚠️ Risk areas this deliverable touches** — **public API one-way door**: `AddCloudstrapHttpServiceClient<TI,TImpl>` and the token-attachment seam are consumed by #5/#6/#7/#9/#12/#17 (Decision Log OQ-1) · **auth-adjacent seam design** (token handler attachment — human review at plan gates) · **shared-contract amendment** to shipped `Cloudstrap.Core` (`HttpClientServiceOptions.HealthCheckPath`, Decision Log OQ-2) · **new external dependencies**: `Azure.Identity`, `Azure.Extensions.AspNetCore.Configuration.Secrets`, `Azure.Extensions.AspNetCore.DataProtection.Blobs`/`.Keys`, `Azure.Storage.Blobs` (all MIT, Microsoft), `AspNetCore.HealthChecks.Uris` (Apache-2.0, Xabaril — Decision Log OQ-2) · **flagged legacy dependencies resolved here**: `NWebsec.AspNetCore.Middleware` dropped suite-wide (unmaintained since 2019), `Microsoft.AspNet.WebApi.Client` dropped (dead reference) · **second `Microsoft.AspNetCore.App` framework reference** in the suite (decision below, precedent #2 OQ-1).

## Code-reading findings that shaped this spec

1. **The old KeyVault feature is two halves of one enterprise convention.** `KeyVault\AddAzureKeyvaultForNihdi.cs` (an `IConfigurationBuilder` extension) composes the vault URI from a vault *name* and excludes six credential types from `DefaultAzureCredential` (leaving effectively managed/workload identity only); `Extensions\IHostApplicationBuilderExtensions.AddAzureKeyVaultForNihdi` supplies the name from the hard-coded pattern `kv-Riziv-IT-{ENV}-App-001` and gates the whole call on `IsRunningInAks()` **and** a `CLOUD_PIPELINE` environment variable. Only `PrefixKeyVaultSecretManager` (prefix filter `{prefix}-`, `--` → `:` mapping) is generic value. The two extensions collapse into one explicit, options-driven `AddCloudstrapKeyVault`.
2. **Blob/DataProtection code is credential-switching by host sniffing.** `BlobStorage\ServiceCollectionExtensions` branches on `EnvironmentIsLocal()` (→ Azurite + auto-create) and `IsRunningInAks()` (→ `WorkloadIdentityCredential`, else `ClientSecretCredential` from `AppRegistrationConfiguration` — a type **deliverable #1 already dropped**, OQ-2 there). `Services\ServiceCollectionExtensions.ProtectKeysWithAzureKeyVaultDefaultCredentials` (internal) silently no-ops unless running in AKS with two `AZURE_DPAPI_*` environment variables set. Founding-spec hosting decision: replace all of it with explicit options + `DefaultAzureCredential`, fail fast instead of silently skipping.
3. **The typed-client registration is the real crown jewel, and its auth is a compile-time dependency.** `HttpClient\ServiceCollectionExtensions.AddNihdiHttpServiceClient<TI,TImpl>` wires base address/timeout, `UseProxy = false` (a corporate-proxy workaround), optional user/client token handlers from the internal `Nihdi.AspNetCore.AccessTokenManagement`, an optional `{prefix}-liveness` health check, and the correlation handler. Cloudstrap must keep the config-driven flags (`AddUserAccessToken`/`AddClientAccessToken` already shipped in Core's `HttpClientServiceOptions`) **without** referencing any auth package — the seam #9 fills is the decided `IAccessTokenHandlerProvider` (Decision Log OQ-1). The old health-check dedupe uses a **process-wide `static ConcurrentDictionary`** — ambient state that leaks across service collections (fixed by redesign).
4. **The middleware god-method belongs to the hosting packages, not here.** `Extensions\ApplicationBuilderExtensions.UseNihdiWebMiddleware` (plus `AddWebOptions`/`UseWebOptions`, `AddNihdiWebApiProtections`, path-base logic, TLS-from-env-vars, XFF known-networks with the fallback CIDR list `85.91.0.0/16,…` — an enterprise public IP range) is consumed by WebApi/Mvc/BlazorServer, which each build their *own* pipelines around it. The founding Package Map assigns middleware to #5 ("hardened middleware") and #6 ("session hardening"). This deliverable ships only the host-agnostic-ish building blocks (KeyVault, DataProtection, storage, typed clients, health-endpoint mapping); everything pipeline-shaped moves out. Nothing in `Cloudstrap.Extensions` takes an `IApplicationBuilder`.
5. **The health-endpoint mapping is the answer to the `InternalsVisibleTo` question.** The old `AssemblyVisibility.cs` grants internals to WebApi, Mvc, BlazorServer, Worker and the tests. What those consumers actually use: `UseNihdiHealthChecksInternal` + `TryRegisterHealthEndpointPath` (WebApi, BlazorServer), `ProtectKeysWithAzureKeyVaultDefaultCredentials` (WebApi, Mvc, BlazorServer), `ConfigureForNihdiOpenTelemetry` (Worker). The first two become one **public** `MapCloudstrapHealthChecks`; the third becomes public `AddCloudstrapDataProtection`; the Worker case is already covered by #2's public `UseCloudstrapObservability` (which works on any `IHostApplicationBuilder`). **Verdict: public seams; no cross-package `InternalsVisibleTo` in Cloudstrap** (only the own-test-project grant).
6. **Two csproj references are dead code.** `Microsoft.AspNet.WebApi.Client` and `System.Security.Cryptography.Xml` appear in `Nihdi.Core.Configuration.Common.csproj` with **zero** usages in any `Common\` source file (verified by repo-wide grep for `System.Net.Http.Formatting`/`ReadAsAsync` and for any `Cryptography.Xml` using). Both are dropped without replacement. `NWebsec.AspNetCore.Middleware` (last release 3.0.0, 2019) is used only for two trivial headers (`X-Content-Type-Options`, `Referrer-Policy`) inside the middleware that moves to #5/#6 — the dependency is **dropped suite-wide here**; #5/#6 decide between ~10 lines of inline middleware and `NetEscapades.AspNetCore.SecurityHeaders` (MIT, active) when they spec the pipeline.
7. **`HostRunner` and `DictionaryTKeyEnumTValueConverter` fail the value test.** `HostRunner` has zero consumers anywhere in the source repo (grep). The JSON converter's only consumers are two lines in `WebApi\WebApplicationBuilderExtensions.cs`; `System.Text.Json` has supported enum-keyed dictionaries natively since .NET 5, so #5 re-evaluates against stock behavior if a real gap surfaces during its port. Both are dropped from this deliverable.
8. **Old health checks carried Dynatrace severity vocabulary and a body-string contract.** `HealthChecks\ApiLivenessHealthCheck` GETs the peer's `/live` and compares the response **body** to `"Healthy"`; registration maps failure status to `HealthCheckTags.SeverityCritical/Warning` from the internal `Nihdi.Core.Health`. #2 already dropped the severity vocabulary (AC-O4); the peer-probe capability survives as a status-code URI check tagged with `CloudstrapHealthCheckTags.Readiness`, probing the peer's configurable `HealthCheckPath` (default `/healthz`), implemented on `AspNetCore.HealthChecks.Uris` (Decision Log OQ-2).
9. **Everything logging/correlation-shaped in the #4 file list is already shipped by #2.** `Extensions\LoggingBuilderExtensions.cs` (internal level-seeding + console formatter selection) is superseded by the shipped `UseCloudstrapObservability` (same four framework-category seeds, consumer overrides win); `AddSerilogNihdi`/`UseSerilogForNihdi`, `UseNihdiConfiguration`/`GetNihdiConfigurationFromContext` and the classic-`IHostBuilder` extensions are superseded by #1's `GetCloudstrapOptions`/`AddCloudstrapCore` and #2's entry point. All Drop.
10. **Core's shipped shapes constrain this package.** `Cloudstrap:HttpClients:{name}` → `HttpClientServiceOptions` (with `AddUserAccessToken`/`AddClientAccessToken`/`EnableHealthCheck`/`HealthCheckPrefix`/`TokenRequestParameters`) already exists — this package **consumes** it and must not redefine it. `HealthChecksOptions` (`Enabled`, `LivenessPath = /healthz`, `ReadinessPath = /ready`) and `CloudstrapHealthCheckTags` (`"live"`/`"ready"`) are the endpoint-mapping contract. The three new option types this package needs (`KeyVaultOptions`, `StorageOptions`, `DataProtectionOptions`) live **here**, not in Core — #3's precedent (leaf-feature options in the owning package; Core stays Azure-free).

---

## User Story

**As an** ASP.NET Core developer deploying to Azure,
**I want to** bootstrap KeyVault-backed configuration, blob-persisted DataProtection keys, a conventional blob container client, config-driven typed `HttpClient`s with correlation and optional token attachment, and standard health probe endpoints — each with one call and one `Cloudstrap:` subsection,
**So that** my `Program.cs` stays under ten lines while secrets, keys, outbound calls and probes follow the same conventions on Azure Web Apps and containers alike — and everything composes with an Aspire-style host without stacking resilience or duplicating providers.

---

## Acceptance Criteria

> AC-ASP2 and AC-ASP3 are carried **verbatim** from the founding spec — AC-ASP3 is this deliverable's named criterion. The founding spec has no dedicated Extensions AC block; AC-E1…AC-E15 are new, spec-specific criteria (precedent: AC-AM1…AC-AM12 in `_specs/3-AzureMonitorExporter.md`). Live-Azure KeyVault/Blob/DataProtection cannot run in tests: unit tests mock at the boundary, the E2E demo covers typed-client + health-endpoint behavior, and the package README documents a manual KeyVault verification procedure (AC-O1-style, per the hand-off brief).

| # | Given | When | Then |
|---|-------|------|------|
| AC-ASP2 | Any shipped Cloudstrap package | Its dependency closure is inspected | Zero `Aspire.*` packages. *(carried verbatim)* |
| AC-ASP3 | Resilience handlers already applied via `ConfigureHttpClientDefaults` | `AddCloudstrapHttpServiceClient<TI,TImpl>` registers a typed client | The client works; Cloudstrap does not stack a second resilience layer. *(carried verbatim; covered by test using `Microsoft.Extensions.Http.Resilience` test-only)* |
| AC-E1 | `Cloudstrap:KeyVault:Enabled = true`, `VaultUri` set, vault holds secrets `{prefix}-Foo--Bar` and `other-Baz` (default prefix = `Application:WorkloadName`) | `AddCloudstrapKeyVault()` runs and configuration is read | Only `{prefix}-`-secrets load; `{prefix}-Foo--Bar` surfaces as configuration key `Foo:Bar`; `other-Baz` is absent. *(De-NIHDI row "KeyVault name")* |
| AC-E2 | `Enabled = true`, `VaultUri` missing/empty | `AddCloudstrapKeyVault()` runs | Startup fails with a validation error naming `Cloudstrap:KeyVault:VaultUri` — no silent skip. |
| AC-E3 | `Enabled = false` or the `Cloudstrap:KeyVault` section absent | `AddCloudstrapKeyVault()` runs | No-op: no KeyVault configuration source added, no Azure type touched — the call is safe to make unconditionally and per-environment config decides. |
| AC-E4 | `SecretPrefix` explicitly set (including empty string) or a `TokenCredential` supplied through the configure hook | `AddCloudstrapKeyVault()` runs | The explicit prefix wins (`""` disables filtering, standard `--` mapping still applies); the supplied credential replaces `DefaultAzureCredential`. |
| AC-E5 | The package README | Reviewed at the gate | Documents "use Cloudstrap's KeyVault configuration **or** Aspire's, not both" with the secret-prefix filter named as Cloudstrap's differentiator, and the AC-O1-style manual KeyVault verification procedure against a real vault. |
| AC-E6 | `Cloudstrap:HttpClients:Catalog` with `BaseAddress` + `Timeout` | `AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog")` and the typed client is resolved | The client is injectable as `ICatalogClient`; `BaseAddress`/`Timeout` applied; `CorrelationHttpDelegatingHandler` attached exactly once (idempotent with a defaults-level registration, per #2's shipped handler contract). |
| AC-E7 | No `Cloudstrap:HttpClients:{name}` section for the requested name | `AddCloudstrapHttpServiceClient` is called | Registration fails fast with an error naming the missing section. |
| AC-E8 | `AddUserAccessToken` or `AddClientAccessToken` is `true` and **no** token-handler provider is registered (no auth package referenced) | The typed client is built | A fail-fast, actionable error names the flag and the package that fills the seam (`Cloudstrap.Authentication.ClientCredentials` / OIDC). The failure never silently sends unauthenticated requests. |
| AC-E9 | A (test-double) `IAccessTokenHandlerProvider` registered (Decision Log OQ-1) | Clients with `AddUserAccessToken` / `AddClientAccessToken` are built | The user-token / client-token handler produced by the provider is in the handler chain for exactly the flagged clients, receiving the client name and its `TokenRequestParameters`. |
| AC-E10 | `EnableHealthCheck = true` on a client (with `HealthCheckPrefix` unset) | Registration runs | A URI health check named `{clientName}-liveness` is added additively via the stock `IHealthChecksBuilder`, tagged `CloudstrapHealthCheckTags.Readiness`, probing `BaseAddress` + `HealthCheckPath` (default `/healthz`); registering the same client twice does not duplicate the check, and no process-wide static state is used. |
| AC-E11 | Health checks registered by the app; `Cloudstrap:HealthChecks` at defaults | `MapCloudstrapHealthChecks()` on the endpoint route builder | `/healthz` serves checks tagged `live`, `/ready` serves checks tagged `ready`, both anonymous and short-circuited; `Enabled = false` maps nothing; calling it twice maps one set of endpoints. |
| AC-E12 | `Cloudstrap:DataProtection:Enabled = true` with `KeysBlobUri` and `KeyVaultKeyId` (both required when enabled — Decision Log OQ-3) | `AddCloudstrapDataProtection()` runs | DataProtection persists keys to the blob and protects them with the KeyVault key using `DefaultAzureCredential` (hook-supplied credential wins); enabled-but-missing URIs fail startup validation — never the source's silent skip. |
| AC-E13 | `Cloudstrap:Storage:BlobServiceUri` set, `ContainerName` unset | `AddCloudstrapBlobStorage()` runs | A `BlobContainerClient` singleton is registered for container `{SystemName lowercase}` on that service URI with `DefaultAzureCredential`; a configured `ConnectionString` (or `ConnectionStrings:CloudstrapStorage`) wins over `BlobServiceUri` (Azurite/dev path); the container is only auto-created when `CreateContainerIfNotExists = true`. *(De-NIHDI row "Storage account")* |
| AC-E14 | A fresh clone with this package | Build, tests, `dotnet format --verify-no-changes`, closure review, case-insensitive search for `Nihdi`, `Riziv` | All green; XML docs on all public API; package metadata complete; zero forbidden identifiers; the closure contains **no** auth package, no `NWebsec.*`, no `Microsoft.AspNet.WebApi.Client`, no `Aspire.*`; every dependency OSI-licensed and CPM-pinned. |
| AC-E15 | The WASM SUT Bff host adopts this package (`MapCloudstrapHealthChecks` replacing its hand-mapped probe endpoints; a typed client registered via `AddCloudstrapHttpServiceClient` with `EnableHealthCheck = true`) | The E2E suite runs | The SUT boots and ≥ 1 passing E2E test proves the typed client and probe endpoints through the running app (standing SUT rule / workflow rule 9). |

---

## Port Decision Table

One row per source public type/feature (all read in full; internals that constitute features are rowed too). "Move-out" = the capability survives but belongs to a later deliverable — the planner for **this** deliverable must not build it.

| Source (under `Nihdi.Core.Configuration.Common\`) | Verdict | Target | Justification |
|---|---|---|---|
| `KeyVault\AddAzureKeyvaultForNihdi.cs` — `ConfigurationManagerExtension.AddAzureKeyVaultForNihdi(IConfigurationBuilder, name, prefix)` | **Redesign** | `AddCloudstrapKeyVault` (options-driven) | Capability earns its place (KeyVault-backed config is a founding feature). Design does not: vault addressed by name-composition, credential list hard-excludes all dev credentials (enterprise policy, breaks local dev), no enable switch. |
| `KeyVault\PrefixKeyVaultSecretManager` (internal) | **Port** | internal `PrefixKeyVaultSecretManager` | The genuinely differentiating piece (prefix filter + `--`→`:` mapping) — Aspire's KeyVault config has no equivalent. Ported with an `Ordinal` comparison fix (source uses culture-sensitive `StartsWith`) and a configurable prefix. |
| `Extensions\IHostApplicationBuilderExtensions.AddAzureKeyVaultForNihdi(IHostApplicationBuilder)` | **Redesign** | folded into `AddCloudstrapKeyVault` | Hard-coded `kv-Riziv-IT-{ENV}-App-001` naming (the De-NIHDI checklist's flagship row), `IsRunningInAks()` + `CLOUD_PIPELINE` env-var gating — all replaced by `Cloudstrap:KeyVault` options (finding 1; hosting-posture decision). |
| `Extensions\IHostApplicationBuilderExtensions.UseNihdiConfiguration` / `GetNihdiConfigurationFromContext` | **Drop** | — | Config-object stashing in builder `Properties` + DI-descriptor surgery — superseded by #1's `GetCloudstrapOptions()`/`AddCloudstrapCore()` (validated options, no ambient context). |
| `Extensions\IHostApplicationBuilderExtensions.UseSerilogForNihdi` *(already `[Obsolete]`)* / `AddSerilogNihdi` | **Drop** | — | Superseded by #2's shipped `UseCloudstrapObservability` (Serilog bootstrap + host logging). |
| `Extensions\IHostBuilderExtensions.*` (classic `IHostBuilder`) | **Drop** | — | Legacy host model; Cloudstrap targets `IHostApplicationBuilder` only (#2 precedent — no classic-host overloads shipped). |
| `Extensions\WebApplicationBuilderExtensions.AddNihdiCommonServices` | **Drop** | — (decomposed) | God-method: business trace + OTel/Serilog (#2 shipped), KeyVault (`AddCloudstrapKeyVault` here), config stashing (dropped), TLS + XFF (rows below), correlation (#2 shipped). Nothing left to own. |
| `Extensions\WebApplicationBuilderExtensions.EnableTLSOptions` (TLS from `IS_TLS_ENABLED` + Kestrel env vars) | **Drop** | — | Cloud-native posture: TLS terminates at the platform front end (Web Apps) or ingress (K8s). Consumers who need in-process TLS use Kestrel's built-in configuration (`Kestrel:Certificates:Default:Path/KeyPath` supports PEM natively) — the bespoke full-chain loader is an on-prem-ingress workaround. |
| `Extensions\WebApplicationBuilderExtensions` forwarded-headers block (`XFFHeader` env var, fallback CIDRs incl. `85.91.0.0/16`) | **Drop** | — (route note → #5/#6) | The fallback list is an enterprise public IP range (De-NIHDI); the capability is one line of stock `ForwardedHeadersOptions` config (or `ASPNETCORE_FORWARDEDHEADERS_ENABLED`). #5/#6 decide whether their pipelines re-expose a typed setting. |
| `Extensions\ApplicationBuilderExtensions.UseNihdiWebMiddleware` (+ `ConfigureExceptionHandling`) | **Move-out → #5/#6** | — | The whole `IApplicationBuilder` pipeline (HSTS, security headers, routing, auth, antiforgery, endpoints) is the hosting packages' charter per the founding Package Map (finding 4). Nothing in `Cloudstrap.Extensions` takes an `IApplicationBuilder`. |
| `Extensions\ApplicationBuilderExtensions.UseNihdiHealthChecksInternal` (internal, via IVT) | **Redesign** | public `MapCloudstrapHealthChecks` | The shared health-endpoint mapping three hosts consumed through `InternalsVisibleTo` becomes one public seam honoring Core's `HealthChecksOptions` + #2's tag contract. Dropped inside it: `/health` restricted to port 9000 (on-prem LB convention), the `LOC` environment switch, `HealthReportConverter.NihdiHealthResponseWriter` (from internal `Nihdi.Core.Health`). |
| `Extensions\ApplicationBuilderExtensions.UseNihdiPathBase` | **Move-out → #5/#6/#12** | — | Pipeline-order-sensitive middleware; Core already ships the normalized `ApplicationOptions.PathBase`. The `basepath` env var and local-environment skip are dropped by whoever ports it (hosting posture). |
| `Extensions\ApplicationBuilderExtensions.TryRegisterHealthEndpointPath` | **Drop** | — (internal detail) | Existed to dedupe `/probe.aspx` across god-methods. `MapCloudstrapHealthChecks` is idempotent by its own internal guard; no public surface. |
| `Extensions\EndpointRouteBuilderExtension.AddNihdiLoadBalancerProbe` (`/probe.aspx`) | **Drop** | — | De-NIHDI checklist row "Probe path `/probe.aspx`". `/healthz` + `/ready` from `HealthChecksOptions` replace it. |
| `Extensions\LoggingBuilderExtensions` (internal: `ApplyNihdiLogLevels`, `AddNihdiConsole`, `ConfigureForNihdiOpenTelemetry`) | **Drop** | — | Superseded byte-for-byte in intent by #2's shipped pipeline (same framework-category seeds, consumer overrides win) — finding 9. This also dissolves Worker's `InternalsVisibleTo` need (finding 5). |
| `Extensions\ProbeHealthCheckExtensions.AddProbeHealthChecks` | **Drop** | — | A pass-through over `services.AddHealthChecks()` + a delegate. Consumers call the stock `IHealthChecksBuilder` directly — inherently additive and Aspire-composable (founding Aspire posture §3). |
| `BlobStorage\IHostApplicationBuilderExtension.AddBlobContainerClientForNihdi` *(already `[Obsolete]`)* | **Drop** | — | Deprecated in the source itself; forwards to the `IServiceCollection` overload. |
| `BlobStorage\ServiceCollectionExtensions.AddBlobContainerClientForNihdi` | **Redesign** | `AddCloudstrapBlobStorage` | Capability stays (#15 claim-check builds on it; De-NIHDI row "Storage account"). Design replaced wholesale: `EnvironmentIsLocal()`/`IsRunningInAks()` credential switching, `AppRegistrationConfiguration` client secrets (dropped in #1), URI composed from enterprise `StorageName` convention → explicit `Cloudstrap:Storage` options + `DefaultAzureCredential` + explicit Azurite connection string. `Microsoft.Extensions.Azure` client factory was considered and rejected: it adds registration machinery without reducing the ~20 lines owned for exactly one container client, and consumers can layer it themselves. |
| `HttpClient\ConfigurationExtension.GetHttpServiceClientConfig` (`Nihdi::HttpClientServiceRegistry` key) | **Drop** | — | Malformed double-colon config key; the registry shape already ships in Core as `Cloudstrap:HttpClients:{name}` — binding is Core's job, not a bespoke accessor's. |
| `HttpClient\ServiceCollectionExtensions.AddNihdiHttpServiceClient<TI,TImpl>` | **Redesign** | `AddCloudstrapHttpServiceClient<TI,TImpl>` | The suite-wide typed-client entry point (finding 3). Kept: typed registration, config-driven base address/timeout, correlation handler, config-driven token flags, optional dependency health check. Changed: consumes Core's shipped options by client name; token attachment via the OQ-1 seam instead of a compile-time internal-ATM dependency; `UseProxy = false` dropped; static health-check dedupe dictionary dropped; no resilience added ever (AC-ASP3). |
| `HealthChecks\ApiLivenessHealthCheck` *(routed here by #2, finding 7 there)* | **Replace** | `AspNetCore.HealthChecks.Uris` URI check *(Decision Log OQ-2)* | Status-code URI probing is a solved problem in a maintained Apache-2.0 package (9.0.0, Xabaril); the bespoke check's body-string comparison ties peers to the dropped Nihdi response-writer format. |
| `HealthChecks\IServiceCollectionExtensions.AddApiLivenessHealthCheck` *(routed here by #2)* | **Redesign** | folded into `AddCloudstrapHttpServiceClient` (`EnableHealthCheck`) | The Dynatrace severity-tag mapping is gone (#2, AC-O4). No standalone public wrapper: a consumer wanting an ad-hoc URI check calls the stock `AddHealthChecks().AddUrlGroup(...)` directly — a wrapper would own API without adding value. |
| `Host\HostRunner` | **Drop** | — | Zero consumers in the entire source repo (finding 7). Multi-host parallel running is a niche solved by `Task.WhenAll` + `IHostApplicationLifetime` in app code; no OSS consumer would miss it. |
| `Options\AddWebOptions` / `Options\UseWebOptions` | **Move-out → #5/#6 (#12)** | — | Delegate-carriers for the middleware pipelines that move out (finding 4). Their redesign (likely plain `Action<>` hooks, no `NihdiConfiguration` parameter) belongs to the packages that own the pipelines. |
| `Serialization\DictionaryTKeyEnumTValueConverter` | **Drop** | — (route note → #5) | Only consumed by WebApi's JSON options; `System.Text.Json` supports enum-keyed dictionaries natively since .NET 5. #5 verifies stock behavior covers its serializer needs during its port; if a real gap appears it lands there, not here. |
| `Services\ServiceCollectionExtensions.AddNihdiWebApiProtections` (HSTS + `IHttpContextAccessor` + CORS) | **Move-out → #5/#6** | — | "Hardened middleware" is #5's charter. Route note: the CORS fallback (`no origins configured → AllowAnyOrigin` with a log warning) is an insecure-by-default trap #5 must not reproduce. |
| `Services\ServiceCollectionExtensions.ProtectKeysWithAzureKeyVaultDefaultCredentials` (internal, via IVT) | **Redesign** | public `AddCloudstrapDataProtection` | Capability is real (shared DataProtection keys are required for any scaled-out cookie/antiforgery scenario). Design replaced: AKS sniffing + `AZURE_DPAPI_*` env vars + silent skip + `WorkloadIdentityCredential`-only → explicit `Cloudstrap:DataProtection` options, fail-fast validation, `DefaultAzureCredential`. |
| `AssemblyVisibility.cs` (`InternalsVisibleTo` → WebApi/Mvc/BlazorServer/Worker/Tests) | **Redesign** | public seams only | Finding 5: every cross-package internal becomes public API here or is superseded by #2. `Cloudstrap.Extensions` grants `InternalsVisibleTo` **only** to `Cloudstrap.Extensions.Tests`. |
| csproj: `NWebsec.AspNetCore.Middleware` | **Drop** (suite-wide decision, owed by the brief) | — | Last release 2019 — fails the maintenance bar. Used only for two headers inside moved-out middleware; #5/#6 choose inline middleware or `NetEscapades.AspNetCore.SecurityHeaders` (MIT, 1.3.1, active) and must verify at their spec time. |
| csproj: `Microsoft.AspNet.WebApi.Client`, `System.Security.Cryptography.Xml` | **Drop** | — | Dead references — zero usages in `Common\` source (finding 6). |
| csproj: `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore` (+ `Scalar\*` folder) | **Move-out → #5** | — | Per the hand-off brief and #1's settings routing. |
| csproj: `Nihdi.AspNetCore.Localization` | **Drop** (edge) | — | Roadmap discrepancy note 6: no Extensions→Localization edge; #24 is standalone. |
| csproj: `Nihdi.AspNetCore.Authentication.JwtBearer`/`Authorization`/`AccessTokenManagement`, `Nihdi.Core.Health` | **Drop** | — | Founding replacements: stock auth + Duende ATM (#9/#10/#5, AC-A3); stock `Microsoft.Extensions.Diagnostics.HealthChecks` (#2 decision). This package references **no** auth package — the `IAccessTokenHandlerProvider` seam (Decision Log OQ-1) is the entire coupling. |
| csproj: Serilog\*/OpenTelemetry\* suites | *(handled by #2)* | — | Arrive transitively via the `Cloudstrap.Observability` project reference only. |

**Tally**: 1 Port · 7 Redesign · 2 Replace · 6 Move-out · 16 Drop (incl. dependency rows).

---

## Public API Sketch

Namespace **`Cloudstrap.Extensions`** (single namespace — small surface, Core precedent). Everything `public sealed` / `static`; implementations and validators `internal`. No `IApplicationBuilder` extension anywhere in this package.

```text
Cloudstrap.Extensions
├── HostApplicationBuilderExtensions (static)
│     AddCloudstrapKeyVault(this IHostApplicationBuilder builder,
│                           Action<KeyVaultConnectionSettings>? configure = null)
│         : IHostApplicationBuilder
│       — reads Cloudstrap:KeyVault from the already-loaded configuration; Enabled=false → no-op
│         (AC-E3); Enabled=true → validates VaultUri, adds the Azure KeyVault configuration
│         source with the prefix secret manager and DefaultAzureCredential. Call it FIRST in
│         Program.cs (before GetCloudstrapOptions / UseCloudstrapObservability) so secrets
│         participate in options binding. Idempotent.
│     AddCloudstrapDataProtection(this IHostApplicationBuilder builder,
│                                 Action<AzureCredentialSettings>? configure = null)
│         : IHostApplicationBuilder
│       — binds + validates Cloudstrap:DataProtection; Enabled=false → no-op; else
│         PersistKeysToAzureBlobStorage(KeysBlobUri) + ProtectKeysWithAzureKeyVault(KeyVaultKeyId)
│         + SetApplicationName(ApplicationName ?? WorkloadName). Both URIs required when Enabled
│         (Decision Log OQ-3).
│     AddCloudstrapBlobStorage(this IHostApplicationBuilder builder,
│                              Action<AzureCredentialSettings>? configure = null)
│         : IHostApplicationBuilder
│       — binds + validates Cloudstrap:Storage; registers a BlobContainerClient singleton (AC-E13).
│
├── ServiceCollectionExtensions (static)
│     AddCloudstrapHttpServiceClient<TInterface, TImplementation>(
│             this IServiceCollection services,
│             string? name = null,                       // default: TInterface name minus leading 'I'
│             Action<HttpClient>? configureClient = null,
│             Action<IHttpClientBuilder>? configureBuilder = null)
│         : IHttpClientBuilder
│       — binds Cloudstrap:HttpClients:{name} (Core's shipped HttpClientServiceOptions; missing
│         section → fail fast, AC-E7); applies BaseAddress/Timeout; attaches the correlation
│         handler via AddCloudstrapCorrelationHandler (idempotent, AC-E6); honors the token
│         flags through the IAccessTokenHandlerProvider seam (Decision Log OQ-1; AC-E8/E9);
│         registers the {prefix|name}-liveness URI health check when EnableHealthCheck=true
│         (AC-E10). Adds NO resilience (AC-ASP3); consumer hooks run last per signal.
│
├── EndpointRouteBuilderExtensions (static)
│     MapCloudstrapHealthChecks(this IEndpointRouteBuilder endpoints)
│         : IEndpointRouteBuilder
│       — maps HealthChecksOptions.LivenessPath (tag "live") and .ReadinessPath (tag "ready"),
│         AllowAnonymous + ShortCircuit, stock response writer; Enabled=false → maps nothing;
│         idempotent (AC-E11).
│
├── KeyVaultOptions                 — section Cloudstrap:KeyVault   (owned HERE, not Core)
│     const SectionName = "Cloudstrap:KeyVault"
│     Enabled      : bool = false
│     VaultUri     : Uri?           — required when Enabled (AC-E2)
│     SecretPrefix : string?        — null → Application:WorkloadName; "" → no prefix filter (AC-E4)
│
├── StorageOptions                  — section Cloudstrap:Storage
│     const SectionName = "Cloudstrap:Storage"
│     BlobServiceUri             : Uri?     — required unless a connection string is provided
│     ContainerName              : string?  — null → Application:SystemName lowercased
│     ConnectionString           : string?  — wins when set (Azurite/dev); when unset,
│                                             ConnectionStrings:CloudstrapStorage is honored
│                                             (platform convention, founding Aspire posture §3)
│     CreateContainerIfNotExists : bool = false
│
├── DataProtectionOptions           — section Cloudstrap:DataProtection
│     const SectionName = "Cloudstrap:DataProtection"
│     Enabled         : bool = false
│     KeysBlobUri     : Uri?        — full blob URI for the key ring; required when Enabled
│     KeyVaultKeyId   : Uri?        — KeyVault key identifier; required when Enabled
│                                     (Decision Log OQ-3)
│     ApplicationName : string?     — null → Application:WorkloadName (payload isolation when
│                                     multiple apps share storage)
│
├── KeyVaultConnectionSettings      — code-level hook (not configuration)
│     Credential     : TokenCredential?   — replaces DefaultAzureCredential (AC-E4)
│     ReloadInterval : TimeSpan?          — passthrough to the Azure config source
├── AzureCredentialSettings         — code-level hook (not configuration)
│     Credential : TokenCredential?      — replaces DefaultAzureCredential
│
└── IAccessTokenHandlerProvider     — THE #9 SEAM (Decision Log OQ-1)
      CreateUserTokenHandler(string clientName, TokenRequestOptions? tokenRequest)  : DelegatingHandler
      CreateClientTokenHandler(string clientName, TokenRequestOptions? tokenRequest): DelegatingHandler
      — implemented by Cloudstrap.Authentication.* (#9/#10); resolved from DI lazily at
        client-build time via named HttpClientFactoryOptions configuration, so Program.cs
        registration order between #4 and #9 calls never matters. Absent while a flag demands
        it → fail fast with the actionable message (AC-E8). This package ships the interface
        ONLY — no implementation, no auth dependency.

internal: PrefixKeyVaultSecretManager (ported, Ordinal), KeyVaultOptionsValidator /
StorageOptionsValidator / DataProtectionOptionsValidator (source-generated [OptionsValidator]
pattern — inherited fact #1, no Microsoft.Extensions.Options.DataAnnotations), health-check and
token-handler wiring internals.
```

**Core amendment (additive, Decision Log OQ-2 — precedent: #2's AC-C6 Core amendment, #3's base-package amendment):** shipped `Cloudstrap.Core`'s `HttpClientServiceOptions` gains one property, `HealthCheckPath : string = "/healthz"` — the relative path the per-client dependency health check probes on the peer. Additive to a not-yet-published package; scheduled inside this deliverable's plan; Core stays Azure-free and gains no other change.

**Configuration** — this package owns three new subsections: `Cloudstrap:KeyVault`, `Cloudstrap:Storage`, `Cloudstrap:DataProtection` (section names mandated by the founding De-NIHDI rows; Core precedent already has multiple subsections per package). It additionally **consumes** Core's shipped `Cloudstrap:HttpClients:{name}`, `Cloudstrap:HealthChecks` and `Cloudstrap:Application` — never redefining them. None of the new option types contains a collection property, so inherited fact #2 (append-to-defaults binder caveat) has nothing to document here.

**Framework reference decision** — `Cloudstrap.Extensions` takes the `Microsoft.AspNetCore.App` framework reference (second in the suite after Observability). Forced by `MapCloudstrapHealthChecks` (`IEndpointRouteBuilder`/`MapHealthChecks`) and DataProtection's ASP.NET Core surface; #2's one-package posture (its Decision Log OQ-1) is the precedent, the runtime-image consequence is documented in the README, and WASM clients are served by `Cloudstrap.BlazorWasm` (#13), never by this package. Splitting a host-agnostic core from an AspNetCore leaf was considered and rejected: it would create a package whose only content is one endpoint-mapping method.

---

## Behaviors & Conventions

| Behavior | Default | Override |
|---|---|---|
| KeyVault activation | Explicit: `Enabled = true` + `VaultUri`. No environment sniffing, no pipeline-variable gates — the call is unconditional in `Program.cs`, per-environment configuration decides (AC-E3). | `Cloudstrap:KeyVault:Enabled` per environment. |
| KeyVault secret filtering | Secrets `{WorkloadName}-Section--Key` → config key `Section:Key` (prefix stripped, `--` → `:`). The prefix filter is Cloudstrap's differentiator vs Aspire's KeyVault provider (one shared vault, many workloads). | `SecretPrefix` (explicit value or `""` for no filtering). |
| KeyVault precedence | Added after file/env sources → KeyVault values win over `appsettings.json`; standard `IConfiguration` layering, documented. | Source ordering is fixed (call placement); per-key overrides via later env vars remain possible. |
| Credentials (all three Azure features) | `DefaultAzureCredential` — behaves identically on Azure Web Apps, containers/AKS (workload identity) and dev machines (founding hosting decision). No credential-type exclusion lists, no `WorkloadIdentityCredential`/`ClientSecretCredential` switching. | `Credential` on the configure hook (a supplied `TokenCredential` always wins). |
| DataProtection | Explicit opt-in; keys persisted to `KeysBlobUri`, protected with `KeyVaultKeyId`, app-isolated by `ApplicationName` (default `WorkloadName`). Misconfiguration fails startup — never the source's silent skip. | The section's properties; the stock `AddDataProtection()` chain remains the documented escape hatch for exotic setups. |
| Blob storage | One `BlobContainerClient` singleton: `BlobServiceUri` + `ContainerName` (default `SystemName` lowercased — De-NIHDI row). Dev/Azurite via explicit `ConnectionString` (`UseDevelopmentStorage=true`), not environment detection. Container creation only on explicit `CreateContainerIfNotExists`. This is the seam #15 (claim-check) builds on. | Every property; `ConnectionStrings:CloudstrapStorage` honored as the platform-convention alternative. |
| Typed client naming | Client name defaults to `TInterface` name minus the leading `I` (`ICatalogClient` → `CatalogClient`), binding `Cloudstrap:HttpClients:CatalogClient`. | Pass `name` explicitly. |
| Typed client pipeline | BaseAddress + Timeout from Core's options → token handler from the Decision Log OQ-1 seam (when flagged) → correlation handler (idempotent). **No resilience handler is ever added** — resilience belongs to the consumer's `ConfigureHttpClientDefaults`/per-client choice, which Cloudstrap tolerates untouched (AC-ASP3). No `UseProxy = false`, no primary-handler replacement. | `configureClient` / `configureBuilder` run after Cloudstrap's wiring — final say. |
| Token attachment | Off by default. `AddUserAccessToken`/`AddClientAccessToken` (Core's shipped flags) activate the `IAccessTokenHandlerProvider` seam (Decision Log OQ-1); both true is allowed only if the provider supports it (provider's contract, #9). No auth package referenced; absence fails fast with an actionable message (AC-E8). | The flags per client; the seam implementation via #9/#10. |
| Dependency health checks | `EnableHealthCheck = true` → URI check `{HealthCheckPrefix ?? name}-liveness` on `BaseAddress + HealthCheckPath` (default `/healthz` — matches what Cloudstrap peers expose), tagged `ready` (an unavailable dependency is a readiness concern, not liveness), registered on the stock `IHealthChecksBuilder` (additive, Aspire-composable). Idempotent per service collection — no static state. | `HealthCheckPrefix`, `HealthCheckPath` (Core amendment, Decision Log OQ-2); tags/failure status via stock health-check registration for advanced cases. |
| Probe endpoints | `MapCloudstrapHealthChecks`: `LivenessPath` (`/healthz`) filtered on tag `live`, `ReadinessPath` (`/ready`) on `ready` — the #2 tag contract; anonymous + short-circuited; stock response writer (no bespoke JSON body). | `Cloudstrap:HealthChecks:LivenessPath/ReadinessPath/Enabled`; consumers needing custom writers map endpoints themselves with stock `MapHealthChecks`. |
| Validation | Source-generated `[OptionsValidator]` internals (inherited fact #1). KeyVault validates eagerly at the call (config-build time, before DI exists); Storage/DataProtection validate at bind + `ValidateOnStart`; HttpClient options validate per registered name at registration. All failures name the offending `Cloudstrap:` key. | Fix the configuration. |
| Aspire coexistence | KeyVault: README states the one-owner rule — "Cloudstrap's KeyVault configuration or Aspire's, not both" (AC-E5). HTTP: no resilience stacked (AC-ASP3). Health: stock builder + additive mapping. Storage: `ConnectionStrings:` convention honored. Zero `Aspire.*` (AC-ASP2). | — (posture). |

**Test strategy (spec-level):** NUnit 4 on Microsoft.Testing.Platform in `src/Test/UnitTest/Cloudstrap.Extensions.Tests`. All Azure interaction mocked at the boundary — no live KeyVault/Blob/DataProtection in tests: KeyVault coverage asserts source/manager wiring and validation (the prefix manager is unit-tested directly against `SecretProperties` fixtures); blob/DataProtection coverage asserts DI registrations and options plumbing. AC-ASP3 is proven with `Microsoft.Extensions.Http.Resilience` as a **test-only** dependency (defaults-level `AddStandardResilienceHandler` + a Cloudstrap typed client → functional client, single resilience layer). The demonstration slice (AC-E15) extends the WASM SUT Bff: `MapCloudstrapHealthChecks` replaces the two hand-mapped `MapHealthChecks` calls in `src/Test/WasmTestProject/src/Host/Bff/Program.cs`, and a typed client with `EnableHealthCheck = true` is exercised by ≥ 1 Playwright E2E test. The manual KeyVault procedure (AC-E5) is README-documented, AC-O1-style.

---

## Dependencies

| Package | License | Evidence & justification |
|---|---|---|
| `Cloudstrap.Core` *(project reference)* | MIT | Shipped options this package consumes (`HttpClients` registry, `HealthChecksOptions`, `ApplicationOptions`). |
| `Cloudstrap.Observability` *(project reference)* | MIT | The correlation handler seam (`AddCloudstrapCorrelationHandler`) and `CloudstrapHealthCheckTags` — the two seams #2 explicitly left for this deliverable. Brings Serilog/OTel transitively (roadmap-sanctioned edge). |
| `Azure.Extensions.AspNetCore.Configuration.Secrets` | MIT | Microsoft-maintained (azure-sdk-for-net; source repo used 1.5.1). The KeyVault configuration provider + `KeyVaultSecretManager` extension point the ported prefix manager plugs into — zero bespoke provider code. |
| `Azure.Identity` | MIT | Microsoft-maintained; `DefaultAzureCredential` is the founding credential convention. Already in the suite via #3. |
| `Azure.Extensions.AspNetCore.DataProtection.Blobs` | MIT | Microsoft-maintained (source used 1.5.3). `PersistKeysToAzureBlobStorage` — replaces bespoke persistence code. |
| `Azure.Extensions.AspNetCore.DataProtection.Keys` | MIT | Microsoft-maintained (source used 1.6.3). `ProtectKeysWithAzureKeyVault`. |
| `Azure.Storage.Blobs` | MIT | Microsoft-maintained. Direct use for the `BlobContainerClient` registration (also transitive via DataProtection.Blobs; referenced explicitly because used directly). |
| `AspNetCore.HealthChecks.Uris` | Apache-2.0 | **Taken** (Decision Log OQ-2). Verified 2026-08-02: 9.0.0 (2024-12-19), Xabaril org, active repo, canonical ecosystem package for URI checks. Replaces the bespoke `ApiLivenessHealthCheck` (~40 lines + timeout/error handling owned forever). |
| `Microsoft.AspNetCore.App` *(framework reference)* | MIT | Endpoint mapping (`MapHealthChecks`), `Microsoft.Extensions.Http` typed clients, DataProtection surface. Decision + rationale in the API sketch. |
| *(test only)* `Microsoft.Extensions.Http.Resilience` | MIT | AC-ASP3 proof — never referenced by the shipped package. |

Considered and **rejected**: `Microsoft.Extensions.Azure` (client-factory machinery without code reduction for one client — Port Decision Table row), `NWebsec.AspNetCore.Middleware` (unmaintained since 2019 — dropped suite-wide), `Microsoft.AspNet.WebApi.Client` + `System.Security.Cryptography.Xml` (dead references), any `Nihdi.*` internal package (founding replacements), any `Aspire.*` package (AC-ASP2), `Duende.AccessTokenManagement` (belongs to #9 — this package ships only the seam interface).

---

## Deliberate Behavior Changes (vs. the source library)

1. **KeyVault is explicit, not sniffed**: `Enabled` + full `VaultUri` replace AKS-detection + `CLOUD_PIPELINE` pipeline-variable gating + the `kv-Riziv-IT-{ENV}-App-001` naming convention. A misconfigured-but-enabled vault fails startup instead of being silently skipped.
2. **Dev credentials work**: plain `DefaultAzureCredential` replaces the source's exclusion list (which barred CLI/VS/environment credentials) — local development against a dev vault/storage account now works out of the box; production behavior (managed identity) is unchanged.
3. **No client-secret credentials**: the `AppRegistrationConfiguration`-based `ClientSecretCredential` path is gone (settings type dropped by #1); `TokenCredential` injection is the escape hatch.
4. **Azurite/dev storage by explicit connection string**, not `EnvironmentIsLocal()`; container auto-creation only on an explicit flag (was automatic in local).
5. **DataProtection misconfiguration fails startup** (source: silent skip with a log warning); payloads are app-isolated by `ApplicationName` (default `WorkloadName`) — new behavior, safe because Cloudstrap has no existing consumers.
6. **Typed clients no longer set `UseProxy = false`** (corporate-proxy workaround) and never replace the primary handler.
7. **Dependency health checks probe `/healthz` (configurable) instead of `/live`**, compare **status code** instead of the response body string, and carry the `ready` tag instead of Dynatrace severity tags. Dedupe is per service collection, not a process-wide static.
8. **Probe endpoints**: `/probe.aspx`, `/health`-on-port-9000 and the `LOC` environment switch are gone; `/healthz` + `/ready` with the stock response writer replace them (paths configurable).
9. **Token attachment without an auth package fails fast** with an actionable error — the source made auth a compile-time dependency of the HTTP layer.
10. **No forwarded-headers, TLS, path-base, HSTS or CORS behavior in this package** — those either dissolve into framework configuration or move to #5/#6 (Port Decision Table).

---

## Edge Cases

| Case | Expected behavior |
|------|-------------------|
| `AddCloudstrapKeyVault` enabled but the vault is unreachable / access denied | Configuration load throws at startup — fail fast, no silent secret-less boot. README documents the required RBAC (`Key Vault Secrets User`). |
| KeyVault secret shadows an `appsettings.json` key | KeyVault wins (added later) — standard layering, documented. |
| `SecretPrefix = ""` | No prefix filtering; all secrets load with standard `--` → `:` mapping. |
| `AddCloudstrapKeyVault` called after options were already read | Secrets still land in configuration (sources reload), but eager pre-host reads (`GetCloudstrapOptions()`) that already ran saw pre-KeyVault values — README states "call it first". |
| `AddCloudstrapHttpServiceClient` called twice for the same name | Standard `AddHttpClient` semantics (configurations accumulate); the health check registers once (AC-E10). |
| `BaseAddress` relative or missing | Named-options validation error at registration naming `Cloudstrap:HttpClients:{name}:BaseAddress`. |
| Both `AddUserAccessToken` and `AddClientAccessToken` true | Both passed to the seam; supporting or rejecting the combination is the provider's contract (#9) — this package forwards both flags verbatim. |
| Storage: both `ConnectionString` and `BlobServiceUri` set | `ConnectionString` wins (documented). |
| Storage: `CreateContainerIfNotExists = true` but the identity lacks data-plane rights | Creation failure surfaces on first client resolution with the Azure SDK's error — never swallowed. |
| `MapCloudstrapHealthChecks` with `Cloudstrap:HealthChecks:Enabled = false` | No endpoints mapped; calling it remains safe. |
| No checks registered but endpoints mapped | Both probes return Healthy (stock behavior of an empty check set) — documented. |

---

## Out of Scope (this deliverable — the planner must not resurrect any of it)

- Everything in the founding spec's global Out of Scope: message encryption, MessagingBridge, Dynatrace, ServicePlatform/ServicePulse, `Cloudstrap.Functional`, `Cloudstrap.Aspire`.
- The full middleware pipeline and its option carriers: `UseNihdiWebMiddleware` successor, `AddWebOptions`/`UseWebOptions` redesigns, HSTS/CORS/security-header/exception-handler/path-base/forwarded-headers middleware — **#5/#6 (#12)**.
- Security-header middleware replacement choice (inline vs `NetEscapades.AspNetCore.SecurityHeaders`) — decided at #5/#6 spec time; the only decision made here is that `NWebsec.*` is dead suite-wide.
- `Scalar\*`, `Microsoft.AspNetCore.OpenApi`, JSON serializer converters — **#5**.
- Any token-handler *implementation* (Duende ATM) — **#9/#10**; this package ships only the seam interface.
- Worker health-listener hosting — **#7** (consumes this package's public surface, no internals).
- Blob claim-check middleware — **#15** (builds on `Cloudstrap:Storage`).
- `HostRunner`, `DictionaryTKeyEnumTValueConverter`, `/probe.aspx`, `AddProbeHealthChecks`, classic-`IHostBuilder` overloads, TLS-from-env-vars — dropped (Port Decision Table).
- Automated tests against live Azure resources (manual procedures documented instead).

---

## Decision Log (gate answers, 2026-08-02 — zero Open Questions remain; spec is planner-ready)

All three gate questions were answered by the user on 2026-08-02; each accepted this spec's recommendation. The full findings/options/rationale for each question live in this repo's git history of this file (the pre-gate draft); the decided outcomes are:

| Question | Answer (user, 2026-08-02) |
|---|---|
| OQ-1 — Token-attachment seam shape (⚠️ auth-adjacent, one-way door consumed by #5/#6/#7/#9/#12/#17)? | **(A) Runtime handler-factory seam**: public `IAccessTokenHandlerProvider` in `Cloudstrap.Extensions` (`CreateUserTokenHandler` / `CreateClientTokenHandler`, receiving client name + `TokenRequestOptions?`, returning `DelegatingHandler`), resolved **lazily from DI at client-build time** via named `HttpClientFactoryOptions` configuration — no Program.cs ordering constraint between #4 and #9 calls; #9's Duende ATM implementation keeps its full handler features (renewal, 401-retry, DPoP). A client flagging `AddUserAccessToken`/`AddClientAccessToken` with no provider registered **fails fast** with an actionable message naming the auth package (AC-E8); AC-E9 pins the contract with a test double. This package ships the interface only — zero auth dependencies. |
| OQ-2 — Per-client dependency health check: Core amendment + library choice (⚠️ shared-contract change + new dependency)? | **(A) Both**: additive Core amendment `HttpClientServiceOptions.HealthCheckPath : string = "/healthz"` (satisfies the every-convention-has-an-override rule; precedent: #2's AC-C6 Core amendment, #3's base-package amendment; scheduled inside this deliverable's plan) **and** take `AspNetCore.HealthChecks.Uris` (Apache-2.0, Xabaril, 9.0.0 verified 2026-08-02) as the check implementation. Status-code semantics replace the old body-string comparison (Deliberate Behavior Change 7); check named `{prefix|name}-liveness`, tagged `ready`, registered additively on the stock `IHealthChecksBuilder` (AC-E10). |
| OQ-3 — DataProtection: is `KeyVaultKeyId` required when enabled? | **(A) Required**: when `Cloudstrap:DataProtection:Enabled = true`, both `KeysBlobUri` and `KeyVaultKeyId` are required — secure by default, startup validation fails when either is absent (AC-E12), never the source's silent skip. Consumers wanting blob-only key persistence use the stock `AddDataProtection().PersistKeysToAzureBlobStorage(...)` chain — the one-line escape hatch documented in the README. |
