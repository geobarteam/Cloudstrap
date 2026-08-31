# Plan: 13-BlazorWasmHelpers — A Blazor WebAssembly consumer gets cookie-credentialed HTTP, automatic validated XSRF protection and BFF-driven auth state from one composite call (`AddCloudstrapBlazorWasm`) plus one-line typed/Refit clients, the Bff opts in to the matching server half (`MapCloudstrapBffUserEndpoint`), and the demo proves the two-sided contract live in the browser

## Overview

Deliverable #13 of the extraction roadmap: the new `Cloudstrap.BlazorWasm` package — the browser-auth
triad (`CookieHandler` + `IAntiforgeryTokenStore` + BFF auth state) and the WASM client helpers
(`AddCloudstrapWasmHttpClient<T>` / `AddCloudstrapWasmRefitClient<T>`) — plus the user-approved DL-2
amendment to shipped `Cloudstrap.Authentication.OpenIdConnect` (#10): the opt-in
`MapCloudstrapBffUserEndpoint()` that makes the XSRF contract two-sided and *validated* for the first
time (the source's server half never existed — spec finding 2). **Binding spec:
`_specs/13-BlazorWasmHelpers.md`** (APPROVED 2026-08-31, zero Open Questions, Decision Log DL-1…DL-4
final). Its Port Decision Table, Public API Sketch, Behaviors & Conventions, Dependencies table,
Deliberate Behavior Changes D-1…D-9, Edge Cases and Out-of-Scope list are authoritative and are not
re-litigated here. Nothing the spec marked Drop appears in this plan: no `BlazorWasmOptions` wrapper,
no hidden `AddLocalization()` (D-1), no `ApplyStoredCultureAsync` / culture persistence /
`Microsoft.JSInterop` usage (**DL-4 — deferred to #24; it must not appear anywhere in this
deliverable**), no `Microsoft.Extensions.Localization` reference, no browser-held tokens
(`Microsoft.AspNetCore.Components.WebAssembly.Authentication`, PKCE-in-browser), no Duende.BFF, no
server-side session/token management (#10's remit beyond the DL-2 mapper), no WASM variant of #4's
`AddCloudstrapHttpServiceClient`, no Dashboard client/assembly discovery (#20), no
`PathBaseDelegatingHandler` (skill-doc drift — never existed), no `Refit.HttpClientFactory`
(finding 7 — the registration is `RestService.For`, always), no `Cloudstrap.BlazorCommon`
ProjectReference (DL-1 — demo-level adoption only), no cross-reference to `Cloudstrap.BlazorServer`
(band rule).

Reference patterns, all read in full before planning:

- **Plan-shape precedent: `_plans/12-BlazorServerHelpers.md`** (closest sibling) and
  `_plans/11-BlazorSharedAbstractions.md` — brand-new-package RED mechanics (the honest first failure
  is the test project failing to compile against missing types), `PackageSurfaceTests` permanent
  guards, the packaging step shape, demo + E2E demonstration slice, final-gate AC walk, gates at
  slice boundaries only.
- **Source package (read on disk, all 12 files + its 6-file test suite)**:
  `D:\source\Nihdi-Core-Configuration\Nihdi-Core-Configuration\src\Nihdi.Core.Configuration.BlazorWasm\`
  — `Http\CookieHandler.cs` (credentials Include, mutating-method set, `Remove`+`Add` replace
  semantics, the hard-coded header constant D-3 fixes), `Http\AntiforgeryTokenStore.cs` +
  `IAntiforgeryTokenStore.cs`, `Authentication\BffAuthenticationStateProvider.cs` (cache flag,
  anonymous fallbacks, `BffCookie` identity, XSRF capture, clear-and-notify),
  `Authentication\ServiceCollectionExtensions.cs` (the double-invoked-delegate and factory-lambda
  defects finding 5 sheds), `HttpClient\ServiceCollectionExtensions.cs` (the `TryAdd` semantics and
  the documented `RestService.For` workaround), `Extensions\WebAssemblyHostBuilderExtensions.cs` (the
  internal `IServiceCollection` seam + `InternalsVisibleTo` testability mechanic — kept), and the
  source tests `CookieHandlerTests` / `BffAuthenticationStateProviderTests` /
  `HttpClient\ServiceCollectionExtensionsTests` — the observed contract this plan preserves
  test-for-test (the spec's "source-test parity").
- **Closure-guard precedent: `src/Test/UnitTest/Cloudstrap.BlazorCommon.Tests/PackageSurfaceTests.cs`**
  — the shape for a NuGet-only package with **no** FrameworkReference (the AC-BW8 WASM-linker-safe
  closure guard mirrors its no-`Microsoft.AspNetCore.*` assertion, adapted to allow the three
  Components/Http packages).
- **DL-2 host + test idiom (read on disk)**: `src/Cloudstrap.Authentication.OpenIdConnect/`
  (`EndpointRouteBuilderExtensions.cs` — the mapper class the amendment extends;
  `CloudstrapOpenIdConnectOptions.cs` — where the two new knobs land; `AuthenticationEndpointsState`)
  and `src/Test/UnitTest/Cloudstrap.Authentication.OpenIdConnect.Tests/` (`OidcTestHost` with its
  `mapEndpoints`/`afterRegistration` seams + `BrowserlessUserAgent` full sign-in,
  `AuthenticationEndpointTests` as the endpoint-test shape, `PackageSurfaceTests` — unchanged: the
  amendment adds a method to an existing approved type, no new public type).
- **Demonstration vehicle (read on disk)**: `src/demo/BlazorWasm/` — `Client/Program.cs` (the plain
  `AddScoped(_ => new HttpClient(...))` line the composite replaces), `Presentation/Doctors/`
  (`DoctorsViewModel` with its raw `HttpClient` + hand-rolled `api/v1/user/state` probe,
  `DoctorsPage.razor(.cs)` and its `data-testid` contract), `Bff/Program.cs` (the
  `UseCloudstrapWebApi` hook points; the challenge-shaping configurator), `Bff/Controllers/
  UserController.cs` (the "the BFF user-info contract is deliverable #13" placeholder comments),
  `Bff/Controllers/DoctorController.cs` (the mutating POST that gains validation), `README.md`
  (feature matrix + harness notes).
- **E2E harness (read on disk)**: `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/DoctorsTests.cs` — every
  pre-existing flow is this deliverable's regression net and must pass **unchanged**; `E2eFixture`
  (IdP 5310 → Api 5330 → Bff 5300) unchanged.

This is a library deliverable with no database. The plan-template's endpoint-integration block
applies literally only to Step 4 (the DL-2 endpoint, tested through the full `OidcTestHost` pipeline
with a real sign-in); the package steps' equivalent is that every test builds a real
`ServiceCollection`, resolves through the real `IHttpClientFactory` pipeline and asserts observable
HTTP behavior — plus the mandatory demonstration slice (Step 5) driving the real browser against the
real three-host topology.

### AC coverage map (every criterion claimed by at least one step)

| Criterion | Step(s) |
|---|---|
| AC-BW1 (BFF auth state: `BffCookie` principal, name + wire claims; anonymous on signed-out/`null`/HTTP/network error — never a throw) | 2 (unit) · 5 (live) |
| AC-BW2 (credentials Include always; configured XSRF header on mutating calls with a token, never on GET / empty store) | 1 (unit) · 5 (live) |
| AC-BW3 (token captured from the response header into the one shared store, used by all package-registered clients) | 1 (shared singleton) · 2 (capture) · 5 (live) |
| AC-BW4 (`ClearAuthenticationState()`: cache dropped, notify fired, exactly two HTTP calls across the sequence) | 2 |
| AC-BW5 (Refit client through the cookie+XSRF pipeline; STJ camelCase case-insensitive default; `RefitSettings` overridable) | 1 (unit) · 5 (live) |
| AC-BW6 (every default overridable via `Cloudstrap:BlazorWasm` or the delegate; capture **and** attachment share one header option) | 1 · 2 (client halves) · 4 (server halves) |
| AC-BW7 (two-sided contract: endpoint + XSRF header; token-carrying mutating call validates, header-less is **rejected**) | 4 (unit round trip) · 5 (live in the browser) |
| AC-BW8 (build/tests/format, XML docs, metadata, identifier sweep, **no FrameworkReference in the closure**, OSI + CPM-pinned deps) | 3 (guards + packaging) |
| AC-BW9 (demo rewritten onto the package; all pre-existing E2E green unchanged; ≥ 1 new E2E) | 5 |
| AC-ASP2 (zero `Aspire.*`) / AC-A3 (zero `Nihdi.AspNetCore`) | 3 (permanent guards) |

### Dependency closure — ⚠️ new CPM entries (dependency-update risk area, reviewed at Gate 1)

The package ships with **zero ProjectReferences, zero FrameworkReference** (AC-BW8's WASM-linker-safe
closure) and exactly four version-less `PackageReference`s, all CPM-pinned in
`src/Directory.Packages.props`:

- **`Refit` 11.2.0** — **new CPM entry** (⚠️ DL-3, user-approved; MIT, reactiveui/refit, release
  2026-06-18) with the repo's license/justification comment (CLAUDE.md rule 4), noting that
  `Refit.HttpClientFactory` is deliberately **not** referenced (finding 7's
  `Microsoft.Extensions.Http` version-mismatch footgun, owned once by the library).
- **`Microsoft.AspNetCore.Components.Authorization` 10.0.10** and **`Microsoft.Extensions.Http`
  10.0.10** — **new CPM entries**: the spec's Dependencies table says "repo-pinned", but neither is
  currently in `Directory.Packages.props` (only `Microsoft.Extensions.Http.Resilience` is); pinned
  at the repo's 10.0.10 family. *(Flagged as spec-drift interpretation — Gate 1.)*
- **`Microsoft.AspNetCore.Components.WebAssembly` 10.0.10** — already pinned, but inside the
  "Demo apps + E2E only — never referenced by shipped packages" comment group; the pin **moves to a
  shipped-package group with an updated comment** (it now backs a shipped package). *(Gate 1.)*

The DL-2 amendment adds **no** dependency to #10 (stock antiforgery ships in its existing
`Microsoft.AspNetCore.App` FrameworkReference). The unit-test project adds only already-pinned
packages; the demo projects gain only ProjectReferences to the new package.

### ⚠️ Risk areas (reviewed at the gates named)

- **All-new public API surface** (consumed later by #20) — six public types in the single flat
  namespace `Cloudstrap.BlazorWasm`, signed off **verbatim against the spec's Public API Sketch** at
  **Gate 1**: `CloudstrapBlazorWasmOptions`, `WebAssemblyHostBuilderExtensions`,
  `ServiceCollectionExtensions`, `IBffAuthenticationStateProvider`, `IAntiforgeryTokenStore`,
  `CookieHandler`.
- **Auth code** (cookie credentials, XSRF, auth state — the whole deliverable) — the client half at
  **Gate 1**, the **DL-2 server half in its own slice at Gate 2** (a sanctioned pre-release
  breaking-allowed amendment to shipped #10 — explicit auth review), the live demo surface at the
  **final gate**.
- **New external dependency (`Refit` 11.2.0) + the two new/moved framework pins** — **Gate 1**
  (CPM diff + license) and **Gate 1's nupkg dependency-list inspection**.
- **Demo auth surface** — Step 5 turns on real antiforgery validation on the Bff's mutating POST and
  rewires the client's auth probe; every pre-existing E2E test (the `DoctorsTests` flows in
  particular) staying green **unchanged** is the tripwire (**final gate**).

### Planner mechanics decided here (no spec conflict; each flagged for review at the named gate)

**(a) The testable registration seam (spec finding 5, kept as a design constraint).**
`AddCloudstrapBlazorWasm(this WebAssemblyHostBuilder, Action<CloudstrapBlazorWasmOptions>? = null)`
is a thin guard-clause wrapper over `internal static IServiceCollection
AddCloudstrapBlazorWasmServices(this IServiceCollection services, string baseAddress,
IConfiguration configuration, Action<CloudstrapBlazorWasmOptions>? configure)` — passing
`builder.HostEnvironment.BaseAddress` and `builder.Configuration`. A `WebAssemblyHostBuilder` cannot
be constructed outside a browser, so unit tests exercise the internal seam via `InternalsVisibleTo`
(the source's exact mechanic); the public wrapper is proven live by the demo + E2E (Step 5).
*(Gate 1.)*

**(b) The options pipeline (D-4; the finding-5 defects structurally dead).**
`services.AddOptions<CloudstrapBlazorWasmOptions>().Bind(configuration.GetSection(
CloudstrapBlazorWasmOptions.SectionName))` then `.Configure(configure)` when non-null — registration
order makes the delegate win over configuration. No `ValidateOnStart` (spec edge case: a WASM host
has no startup-validation gate; absent section → all defaults). The configure delegate is invoked
exactly once, by the options pipeline — never on a throwaway instance. Defaults:
`UserEndpointPath = "bff/user"` (D-2), `XsrfHeaderName = "X-XSRF-TOKEN"`,
`AuthHttpClientName = "CloudstrapBffAuth"`. *(Gate 1.)*

**(c) `CookieHandler` reads the configured header (D-3) with an optional-options constructor.**
`public CookieHandler(IAntiforgeryTokenStore tokenStore, IOptions<CloudstrapBlazorWasmOptions>?
options = null)` — when `options` is null (the public escape hatch used outside DI, the source
tests' arrangement) the defaults apply; under DI the bound options flow in, so capture and attachment
always share one `XsrfHeaderName`. Replace semantics kept (`Remove` + `Add` — last writer is the
pipeline, documented edge case). *(Gate 1.)*

**(d) The provider shape (D-8).** `internal sealed BffAuthenticationStateProvider :
AuthenticationStateProvider, IBffAuthenticationStateProvider` — constructor
`(IHttpClientFactory httpClientFactory, IAntiforgeryTokenStore tokenStore,
IOptions<CloudstrapBlazorWasmOptions> options)`, creating the named client
(`options.Value.AuthHttpClientName`) itself: normal DI, no hand-rolled factory lambda. Registered
`TryAddScoped<BffAuthenticationStateProvider>()` + scoped forwards for both
`AuthenticationStateProvider` and `IBffAuthenticationStateProvider` (one instance per scope).
Behavior is the source's, verbatim: cache-until-cleared, `BffCookie` identity, `ClaimTypes.Name`
from `userName`, wire claims 1:1, anonymous on `EnsureSuccessStatusCode` failure /
`HttpRequestException` / `null` body, XSRF captured from `options.Value.XsrfHeaderName`. *(Gate 1.)*

**(e) The composite's exact registration list (DL-1, D-5; nothing else).** Options per (b);
`TryAddSingleton<IAntiforgeryTokenStore, AntiforgeryTokenStore>()`; `TryAddTransient<CookieHandler>()`;
the named auth `HttpClient` (`AddHttpClient(name)` reading the options for the name, base address =
the seam's `baseAddress`, `.AddHttpMessageHandler<CookieHandler>()`); the provider per (d);
`AddAuthorizationCore()`; `AddCascadingAuthenticationState()`. **No** localization (D-1), no
analytics, no MudBlazor, no `AddCloudstrapCore`. Repeat calls: `TryAdd` services once, options
delegates compose, named-client configuration appends (documented, not "fixed" — spec edge case).
*(Gate 1.)*

**(f) The DL-2 server-side option home and fail-loud posture.** Two new bound properties on the
existing `CloudstrapOpenIdConnectOptions` (section `Cloudstrap:OpenIdConnect`):
`UserEndpointPath` (default `"/bff/user"`) and `XsrfHeaderName` (default `"X-XSRF-TOKEN"`) — the
spec sketch's `{UserEndpointPath}`/`{XsrfHeaderName}` made concrete next to the existing
`LoginPath`/`LogoutPath` knobs. `MapCloudstrapBffUserEndpoint()` maps `GET {UserEndpointPath}` with
`AllowAnonymous` (the fallback policy cannot lock out the probe — the login-endpoint precedent),
returns the wire contract from `HttpContext.User` (camelCase JSON:
`{ "isAuthenticated": bool, "userName": string?, "claims": [{ "type", "value" }]? }`, 200 always),
and sets the XSRF request token from stock `IAntiforgery.GetAndStoreTokens(httpContext)` as the
`{XsrfHeaderName}` response header. Antiforgery **services** are not registered by the mapper
(endpoint stage is too late) and **validation** stays the consumer's stock wiring (DL-2):
`MapCloudstrapBffUserEndpoint` throws `InvalidOperationException` at map time naming
`AddAntiforgery` when `IAntiforgery` is unresolvable — fail loud, never security theater. The
documented contract: the consumer's `AddAntiforgery(o => o.HeaderName = ...)` must agree with
`XsrfHeaderName` (both sides shown in the demo). *(Gate 2 — auth review.)*

**(g) The demo keeps its regression net intact.** The Bff's `api/v1/user/state` endpoint and the
SUT-local challenge shaping stay (pre-existing E2E tests pin both behaviors — `UserState_
AnonymousApiGet_Returns200SignedOut` and the bare-401 tests); only the **client-side** probe is
replaced (the ViewModel moves to the package's auth state + Refit client), and the stale
"placeholder code deliverable #13 replaces" comments in `UserController`, `Bff/Program.cs` and the
demo README are rewritten to the shipped truth. `Cloudstrap.Demo.Contracts` is untouched. *(Final
gate — this resolves the spec's "resolving the placeholder" wording without breaking AC-BW9's
unchanged-tests clause.)*

**(h) The demo ViewModel's auth-state source.** `DoctorsViewModel` injects
`AuthenticationStateProvider` (the DI service the composite registers) rather than a cascading
parameter — `IViewModel.InitializeAsync(CancellationToken)` is a fixed #11 contract and ViewModels
are not components; the *page/layout* demonstrates `AuthorizeView` (the composite's
`AddCascadingAuthenticationState` making it work). `Cloudstrap.Demo.BlazorWasm.Presentation` gains a
ProjectReference to `Cloudstrap.BlazorWasm` (the Refit interface and provider seam live there —
demo-level adoption, the DL-1/D-13 pattern; BlazorCommon usage stays as band-interop evidence).
*(Final gate.)*

**(i) Full-suite check** (standing convention: `runTests` is not on the agent PATH — VERIFY invokes
each exe directly): `dotnet build src/Cloudstrap.sln`, then the **13** unit exes under
`src/Test/UnitTest/<Name>.Tests/bin/Debug/net10.0/<Name>.Tests.exe` (Core, Observability,
Observability.AzureMonitor, Extensions, WebApi, Mvc, Worker, TestIdentityProvider,
Authentication.ClientCredentials, Authentication.OpenIdConnect, BlazorCommon, BlazorServer,
**BlazorWasm** — new in Step 1), then the E2E exe
`src\Test\E2E\Cloudstrap.Demo.E2E.Tests\bin\Debug\net10.0\Cloudstrap.Demo.E2E.Tests.exe`, then
`dotnet format src/Cloudstrap.sln --verify-no-changes`.

**Target consumer composition** (the spec made concrete — also the demo `Client/Program.cs`, Step 5,
and the package README, Step 3):

```csharp
// WASM client (Program.cs)
builder.AddCloudstrapBlazorWasm();                          // cookie+XSRF pipeline, BFF auth state,
                                                            // AuthorizationCore + cascading state
builder.Services.AddCloudstrapWasmRefitClient<IDoctorServiceClient>(
    builder.HostEnvironment.BaseAddress);                   // rides the same hardened pipeline

// Bff host (the #10 pairing)
app.UseCloudstrapWebApi(pipeline => pipeline.ConfigureEndpoints = endpoints =>
{
    endpoints.MapCloudstrapAuthenticationEndpoints();       // #10, unchanged
    endpoints.MapCloudstrapBffUserEndpoint();               // the DL-2 opt-in server half
    endpoints.MapFallbackToFile("index.html");
});
```

---

## Slice 1 — The client package: one composite call + one-line clients give a WASM app cookie-credentialed, XSRF-protected HTTP and BFF-driven auth state ⚠️ PUBLIC-API / AUTH / DEPENDENCY RISK AREA

---

## Step 1 — Every HTTP client registered through the package rides the cookie+XSRF pipeline: browser credentials always included, the *configured* XSRF header attached on mutating calls only (never on GET, never with an empty store, replacing any pre-set value), typed and Refit clients registered in one line against one shared token store (AC-BW2; AC-BW5; AC-BW3's shared-store half; AC-BW6's header/Refit halves; D-3, D-6; DL-3)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Directory.Packages.props` *(modify)* — the ⚠️ dependency-closure section's four pin changes:
  new `Refit` 11.2.0 (license/justification comment incl. the no-`Refit.HttpClientFactory` note),
  new `Microsoft.AspNetCore.Components.Authorization` 10.0.10, new `Microsoft.Extensions.Http`
  10.0.10, and `Microsoft.AspNetCore.Components.WebAssembly` moved out of the demo-only comment
  group.
- `src/Cloudstrap.BlazorWasm/Cloudstrap.BlazorWasm.csproj` *(create)* — `Microsoft.NET.Sdk`,
  `net10.0`, `GeneratePackageOnBuild=true`, `GenerateDocumentationFile=true`; version-less
  `PackageReference`s: `Microsoft.AspNetCore.Components.WebAssembly`,
  `Microsoft.AspNetCore.Components.Authorization`, `Microsoft.Extensions.Http`, `Refit`; **no
  FrameworkReference, no ProjectReference**; `<InternalsVisibleTo
  Include="Cloudstrap.BlazorWasm.Tests" />`. Description/tags/README metadata land in Step 3
  (packable from day one — the #11/#12 precedent).
- `src/Cloudstrap.BlazorWasm/CloudstrapBlazorWasmOptions.cs` *(create)* — sealed; the spec sketch
  verbatim: `SectionName = "Cloudstrap:BlazorWasm"`, `UserEndpointPath = "bff/user"`,
  `XsrfHeaderName = "X-XSRF-TOKEN"`, `AuthHttpClientName = "CloudstrapBffAuth"`; XML docs carry the
  public-configuration statement (the section lives in the publicly downloadable
  `wwwroot/appsettings.json` and may never carry secrets) and the both-sides-one-option D-3 contract.
- `src/Cloudstrap.BlazorWasm/IAntiforgeryTokenStore.cs` *(create)* — public, the sketch verbatim
  (`string? Token { get; set; }`).
- `src/Cloudstrap.BlazorWasm/AntiforgeryTokenStore.cs` *(create)* — **internal** sealed (D-8).
- `src/Cloudstrap.BlazorWasm/CookieHandler.cs` *(create)* — public sealed `DelegatingHandler`,
  mechanic (c): `SetBrowserRequestCredentials(BrowserRequestCredentials.Include)` on every request;
  on POST/PUT/DELETE/PATCH with a non-empty store token, `Remove` + `Add` of the **configured**
  header name (D-3); guard clauses.
- `src/Cloudstrap.BlazorWasm/ServiceCollectionExtensions.cs` *(create)* —
  `AddCloudstrapWasmHttpClient<TClient>(this IServiceCollection, string baseAddress,
  Action<HttpClient>? configureClient = null) : IHttpClientBuilder` and
  `AddCloudstrapWasmRefitClient<TClient>(this IServiceCollection, string baseAddress,
  RefitSettings? refitSettings = null) : IHttpClientBuilder` (D-6 — both return the builder), both
  `where TClient : class`, guard clauses (`ThrowIfNull` / `ThrowIfNullOrWhiteSpace`), both
  `TryAddSingleton` store + `TryAddTransient` handler then `AddHttpClient` +
  `.AddHttpMessageHandler<CookieHandler>()`; the Refit overload registers
  `.AddTypedClient((httpClient, _) => RestService.For<TClient>(httpClient, refitSettings ??
  defaults))` — **never `Refit.HttpClientFactory`** (DL-3); default `RefitSettings`:
  `SystemTextJsonContentSerializer` with camelCase + case-insensitive. `baseAddress` passed to `Uri`
  as-is (trailing-slash recommendation documented, no silent fixing — spec edge case).
- `src/Test/UnitTest/Cloudstrap.BlazorWasm.Tests/Cloudstrap.BlazorWasm.Tests.csproj` *(create)* —
  `Microsoft.NET.Sdk` (the source test suite's precedent: the handler/provider/registration seams
  run fine on the desktop runtime), `net10.0`, ProjectReference to the package, version-less
  `PackageReference` `Microsoft.Extensions.DependencyInjection` (already pinned); NUnit/MTP wiring
  inherited from `src/Test/Directory.Build.props`.
- `src/Test/UnitTest/Cloudstrap.BlazorWasm.Tests/CookieHandlerTests.cs` *(create)*
- `src/Test/UnitTest/Cloudstrap.BlazorWasm.Tests/WasmClientRegistrationTests.cs` *(create)*
- `src/Cloudstrap.sln` *(modify)* — package at the solution root, test project under `Test\UnitTest`.

**RED** *(write these tests first; for a brand-new project the honest first failure is the test
project failing to compile against missing types — the #11/#12 precedent — followed by real red runs
once the types exist)*:
- Unit test file: `CookieHandlerTests.cs` *(the source's `TestCookieHandler` + stub-inner-handler
  arrangement, NUnit-ified; Moq for the store)*
  - `SendAsync_OnAnyRequest_SetsBrowserRequestCredentialsInclude` — the WebAssembly request option
    is set on GET and POST alike (AC-BW2's credentials clause, asserted via the request's options).
  - `SendAsync_GetRequest_DoesNotAttachTheXsrfHeader` (token present — still no header).
  - `SendAsync_MutatingRequest_WithToken_AttachesTheXsrfHeader` — `[TestCase]` over POST, PUT,
    DELETE, PATCH (source-test parity).
  - `SendAsync_MutatingRequest_WithoutToken_DoesNotAttachTheHeader` (empty store — AC-BW2).
  - `SendAsync_WithOverriddenXsrfHeaderName_AttachesTheConfiguredName` — options with
    `XsrfHeaderName = "X-CUSTOM-XSRF"`: the custom header carries the token and the default name is
    absent (**the D-3 fix — impossible in the source**; AC-BW6).
  - `SendAsync_RequestAlreadyCarryingTheHeader_ReplacesItWithTheStoresToken` (the replace-semantics
    edge case).
  - `Ctor_NullTokenStore_ThrowsArgumentNullException`.
- Unit test file: `WasmClientRegistrationTests.cs`
  - `AddCloudstrapWasmHttpClient_RegistersStoreHandlerAndTypedClient_AndReturnsTheBuilder`
    (source parity: descriptors present, `IHttpClientBuilder` returned).
  - `AddCloudstrapWasmHttpClient_ConfigureClient_AppliesToTheResolvedClient` (base address +
    a `configureClient` timeout observed on the resolved typed client — source parity).
  - `AddCloudstrapWasmHttpClient_ResolvedClientPipeline_ContainsTheCookieHandler` — a stub primary
    handler (`ConfigurePrimaryHttpMessageHandler`) captures the request: a POST through the resolved
    client carries the XSRF header from a pre-seeded store (the pipeline is really wired, not just
    registered).
  - `AddCloudstrapWasmRefitClient_ResolvesTheInterfaceAndCallsThroughTheCookiePipeline` — a small
    Refit fixture interface (`ITestApiClient` with one `[Get]` + one `[Post]`); stub primary handler:
    the GET deserializes a camelCase payload into a PascalCase DTO case-insensitively (AC-BW5's
    serialization default) and the POST carries the XSRF header + the configured base address
    (AC-BW5's pipeline clause).
  - `AddCloudstrapWasmRefitClient_CustomRefitSettings_WinPerRegistration` — a custom serializer
    observably used (AC-BW5's override clause).
  - `BothHelpers_ShareOneSingletonTokenStore` — two registrations (one Http, one Refit): exactly one
    `IAntiforgeryTokenStore` descriptor and the same instance resolves (AC-BW3's one-store-per-app
    half; `TryAdd` semantics).
  - Guard clauses: null services / null-or-whitespace `baseAddress` for both helpers.
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln   # RED = the new test project fails to compile against missing types
  src\Test\UnitTest\Cloudstrap.BlazorWasm.Tests\bin\Debug\net10.0\Cloudstrap.BlazorWasm.Tests.exe --filter "CookieHandlerTests|WasmClientRegistrationTests"
  ```

**GREEN**: the Scope items — minimal implementations passing these tests. Full XML docs on every
public member from the start (the escape-hatch paragraph on `CookieHandler`, the
never-`Refit.HttpClientFactory` remark ported from the source with the version-mismatch rationale,
the pre-seed/inspect note on `IAntiforgeryTokenStore`).

**DB changes**: none — this repository has no database.

**VERIFY** *(when all green, mark this step's `Done` checkbox and continue straight to the next step)*:
1. Test exe → all pass: any client registered through the package now sends cookie-credentialed
   requests with correctly scoped, correctly named XSRF attachment, and a Refit interface becomes a
   working client in one line — none of which existed before.
2. Full-suite check (mechanic (i)) — all green (the new exe joins the set); zero build warnings;
   `dotnet format` exit 0.
3. `dotnet build src/Cloudstrap.sln -c Release` → a `Cloudstrap.BlazorWasm.*.nupkg` appears under
   `src/Cloudstrap.BlazorWasm/bin/Release/`.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 2 — One composite call gives the app BFF-driven Blazor auth state: the user endpoint is fetched once and cached, the principal is `BffCookie` with name and wire claims, every failure mode yields the anonymous principal without a throw, the XSRF token is captured into the shared store from the *configured* header, `ClearAuthenticationState()` drops-notifies-refetches, and every option binds from `Cloudstrap:BlazorWasm` with the delegate winning (AC-BW1; AC-BW3's capture half; AC-BW4; AC-BW6's client half; D-1, D-2, D-4, D-5, D-8; mechanics (a), (b), (d), (e))

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.BlazorWasm/IBffAuthenticationStateProvider.cs` *(create)* — the sketch verbatim:
  one method, `void ClearAuthenticationState()`.
- `src/Cloudstrap.BlazorWasm/BffAuthenticationStateProvider.cs` *(create)* — **internal** sealed,
  mechanic (d); behavior ported test-for-test from the source (cache flag, anonymous fallbacks on
  non-success/`HttpRequestException`/`null` body, `BffCookie` identity, `ClaimTypes.Name`, claims
  1:1, capture from `options.Value.XsrfHeaderName`, `ClearAuthenticationState` resets and calls
  `NotifyAuthenticationStateChanged(GetAuthenticationStateAsync())`).
- `src/Cloudstrap.BlazorWasm/UserInfo.cs` + `src/Cloudstrap.BlazorWasm/ClaimDto.cs` *(create)* —
  internal wire DTOs (camelCase JSON via case-insensitive deserialization), ported.
- `src/Cloudstrap.BlazorWasm/WebAssemblyHostBuilderExtensions.cs` *(create)* — mechanic (a): the
  public `AddCloudstrapBlazorWasm` wrapper + the internal `AddCloudstrapBlazorWasmServices` seam;
  mechanic (e)'s exact registration list; XML docs state what the composite does **and does not**
  register (no localization D-1, no login/logout — full-page navigations to #10's `/account/*`, the
  README pattern).
- `src/Test/UnitTest/Cloudstrap.BlazorWasm.Tests/BffAuthenticationStateProviderTests.cs` *(create)*
- `src/Test/UnitTest/Cloudstrap.BlazorWasm.Tests/CompositeRegistrationTests.cs` *(create)*
- `src/Test/UnitTest/Cloudstrap.BlazorWasm.Tests/Cloudstrap.BlazorWasm.Tests.csproj` *(modify)* —
  add version-less `Microsoft.Extensions.Configuration` (already pinned) for the in-memory
  config-binding tests.

**RED** *(write these tests first, run them, confirm they fail — the source `StubHttpHandler`
arrangement, NUnit-ified; the provider is reachable through `InternalsVisibleTo`)*:
- Unit test file: `BffAuthenticationStateProviderTests.cs`
  - `GetAuthenticationStateAsync_AuthenticatedUser_ReturnsBffCookiePrincipalWithNameAndClaims` —
    authenticated, `Identity.AuthenticationType == "BffCookie"`, `Identity.Name == "testuser"`,
    both wire claims present (AC-BW1; source parity).
  - `GetAuthenticationStateAsync_SignedOutUser_ReturnsAnonymous` (`isAuthenticated: false`).
  - `GetAuthenticationStateAsync_NullBody_ReturnsAnonymous` (`"null"` body — source parity).
  - `GetAuthenticationStateAsync_HttpError_ReturnsAnonymous` (500 — no throw).
  - `GetAuthenticationStateAsync_NetworkError_ReturnsAnonymous` (handler throws
    `HttpRequestException` — no throw; AC-BW1's full failure ladder).
  - `GetAuthenticationStateAsync_XsrfResponseHeader_CapturesTheTokenIntoTheStore` (AC-BW3).
  - `GetAuthenticationStateAsync_WithOverriddenHeaderName_CapturesFromTheConfiguredHeader` — the
    same option Step 1 proved on attachment now proves on capture (AC-BW6's one-option contract).
  - `GetAuthenticationStateAsync_UsesTheConfiguredUserEndpointPath` — the stub captures the request
    URI: default `bff/user` resolved against the base address; an override is honored (D-2, AC-BW6).
  - `GetAuthenticationStateAsync_CachedState_MakesExactlyOneHttpCall` (source parity).
  - `ClearAuthenticationState_DropsTheCacheNotifiesAndRefetches` — subscribe
    `AuthenticationStateChanged`; exactly **two** HTTP calls across the
    fetch → clear → fetch sequence (AC-BW4; source parity).
  - `ClearAuthenticationState_BeforeAnyFetch_IsSafe` (spec edge case — no throw, notification fires).
- Unit test file: `CompositeRegistrationTests.cs` *(builds a `ServiceCollection` + in-memory
  `IConfiguration`, calls the internal seam with a base address — mechanic (a))*
  - `AddCloudstrapBlazorWasmServices_RegistersTheProviderAsBothSeams` — `AuthenticationStateProvider`
    and `IBffAuthenticationStateProvider` resolve to the **same scoped instance**, and it is the BFF
    provider (AC-BW1's registration half).
  - `AddCloudstrapBlazorWasmServices_BindsOptionsFromConfigurationAndTheDelegateWins` —
    `Cloudstrap:BlazorWasm:UserEndpointPath` set in config **and** overridden by the delegate: the
    delegate's value is in `IOptions<CloudstrapBlazorWasmOptions>` (D-4; AC-BW6).
  - `AddCloudstrapBlazorWasmServices_NoConfigSection_AllDefaultsApply` — `bff/user` /
    `X-XSRF-TOKEN` / `CloudstrapBffAuth` (spec edge case).
  - `AddCloudstrapBlazorWasmServices_AuthClient_UsesTheConfiguredNameAndBaseAddress` — a stub
    primary handler on the configured client name captures the provider's fetch: the request went to
    `{baseAddress}bff/user` through the named client, **with the `CookieHandler` in its pipeline**
    (mechanic (e); AC-BW2 applies to the auth client too).
  - `AddCloudstrapBlazorWasmServices_RegistersAuthorizationCoreAndCascadingAuthenticationState` —
    the descriptors `AddAuthorizationCore`/`AddCascadingAuthenticationState` add are present (D-5).
  - `AddCloudstrapBlazorWasmServices_RegistersNoLocalization` — no `IStringLocalizerFactory`
    descriptor (D-1 made observable; the composite hides nothing).
  - `AddCloudstrapBlazorWasmServices_CalledTwice_TryAddServicesRegisterOnce` (spec edge case).
  - Guard clauses: null services / null-or-whitespace base address / null configuration; and
    `AddCloudstrapBlazorWasm_OnNullBuilder_ThrowsArgumentNullException` (the public wrapper's guard).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.BlazorWasm.Tests\bin\Debug\net10.0\Cloudstrap.BlazorWasm.Tests.exe --filter "BffAuthenticationStateProviderTests|CompositeRegistrationTests"
  ```

**GREEN**: the Scope items — mechanics (a), (b), (d), (e) exactly; the finding-5 defects (double
delegate invocation, factory lambdas) must not reappear.

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: one composite call now yields working, cached, failure-safe BFF auth state
   with XSRF capture and a refresh seam, configured from `Cloudstrap:BlazorWasm` — observable
   behavior that did not exist before this step.
2. Full-suite check (mechanic (i)) — all green; `dotnet format` exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 3 — The package is publishable and guarded forever: metadata, README (quick start, options table, wire contract, RedirectToLogin snippet, no-secrets statement, migration notes D-1…D-9), permanent tripwires on the WASM-linker-safe closure and the dropped concepts, the forbidden-identifier sweep — and the `blazor.md` + refit-skill doc drift closed (AC-BW8; AC-ASP2; AC-A3; the spec's doc-drift definition-of-done row)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.BlazorWasm/Cloudstrap.BlazorWasm.csproj` *(modify)* — `<Description>` (Blazor
  WebAssembly client helpers for a BFF-hosted SPA: cookie-credentialed HTTP with automatic XSRF
  protection, Blazor authentication state driven by the BFF's session, and one-line typed/Refit API
  clients riding the same hardened pipeline — no token ever lives in the browser; pairs with
  Cloudstrap's OIDC BFF login), `<PackageTags>$(PackageTags);blazor;webassembly;wasm;bff;xsrf;refit;
  cookie-auth</PackageTags>`, `<PackageReadmeFile>README.md</PackageReadmeFile>` + the pack item.
- `src/Cloudstrap.BlazorWasm/README.md` *(create)* — quick start (the Overview's consumer
  composition, both halves); the `Cloudstrap:BlazorWasm` options table with the **no-secrets
  statement** (the section lives in the publicly downloadable `wwwroot/appsettings.json` — spec
  Behaviors row, verbatim requirement); the wire contract (both halves, the both-sides-must-agree
  `XsrfHeaderName` note and the mismatch edge case); the login/logout pattern incl. a
  `RedirectToLogin`-style full-page-navigation snippet to #10's `/account/login` (spec Behaviors
  row); the escape hatches (`CookieHandler` in consumer-owned `AddHttpClient` chains, pre-seeding
  `IAntiforgeryTokenStore`, per-call `RefitSettings`); the repeat-registration and trailing-slash
  edge-case notes; the Blazor-Server/prerendering out-of-scope note; migration notes vs the source
  (D-1 composite rename + no hidden localization · D-2 `api/user` → `bff/user` · D-3 one header
  option both sides · D-4 config binding · D-5 cascading state · D-6 `IHttpClientBuilder` return ·
  D-7 validated two-sided XSRF · D-8 internals · D-9 culture helpers → #24).
- `src/Test/UnitTest/Cloudstrap.BlazorWasm.Tests/PackageSurfaceTests.cs` *(create)* — permanent
  guards mirroring `Cloudstrap.BlazorCommon.Tests/PackageSurfaceTests.cs`, adapted.
- `.claude/instructions/blazor.md` *(modify — docs; human-reviewed at Gate 1)* — the BlazorWasm
  project-roles row (deps: the four NuGet packages, no project refs, **no BlazorCommon reference —
  DL-1, demo-level adoption only**); the BlazorWasm section rewritten to the shipped truth
  (`AddCloudstrapBlazorWasm` + the two client helpers, the `Cloudstrap:BlazorWasm` section, the
  configured-header D-3 contract, the DL-2 server pairing); the stale
  `Add<Feature>ForCloudstrap()` naming line and the "until #13 ships" drift note **removed** (the
  band's doc drift is closed). No other section edited.
- `.claude/skills/refit/SKILL.md` *(modify — docs; human-reviewed at Gate 1)* — the WASM half only:
  `AddRefitClientWithCookies` + `PathBaseDelegatingHandler` example replaced with
  `AddCloudstrapWasmRefitClient<T>(baseAddress)` (the spec's definition-of-done row, verbatim); the
  WASM anti-pattern rows updated to name the shipped helper. The BlazorServer half untouched.

**RED** *(the guard tests are tripwires against already-correct code and may pass immediately — the
honest failing state is in the artifacts: before GREEN the Release nupkg has no README/description/
tags and the two instruction files still describe the source-repo surface; recorded per the #2…#12
precedent)*:
- Unit test file: `PackageSurfaceTests.cs`
  - `ReferencedAssemblies_OfBlazorWasmAssembly_MatchTheApprovedClosure` — every referenced assembly
    starts with `System` / is `netstandard` / starts with `Microsoft.Extensions.` or is exactly
    `Microsoft.AspNetCore.Components.WebAssembly`, `Microsoft.AspNetCore.Components.Authorization`,
    `Microsoft.AspNetCore.Components` (their transitive), `Microsoft.AspNetCore.Authorization`,
    `Microsoft.AspNetCore.Metadata` (Authorization's transitives, as observed) or `Refit`;
    explicitly **zero** names starting `Aspire` (AC-ASP2), `Nihdi` (AC-A3), `Duende`, `MudBlazor`,
    `Scrutor`, and **not** `Cloudstrap.BlazorCommon` or any `Cloudstrap.*` (the standalone-leaf
    fact, DL-1), **not** `Microsoft.AspNetCore.App` / `Microsoft.AspNetCore.Antiforgery` /
    `Microsoft.AspNetCore.Mvc.*` and **not** `Microsoft.JSInterop`-consuming culture machinery
    (DL-4) — the AC-BW8 WASM-linker-safe closure made permanent. *(Executor latitude: trim the
    allow-list to the actually observed transitive set at GREEN time; the forbidden list is fixed.)*
  - `PublicTypes_OfBlazorWasmAssembly_ContainNoForbiddenIdentifiers` — no public type/member matches
    `(?i)nihdi|riziv|cfe|dynatrace|nservicebus` (the spec's AC-BW8 sweep incl. `Cfe`).
  - `PublicSurface_IsExactlyTheSixApprovedTypes` — exported types are exactly
    `CloudstrapBlazorWasmOptions`, `WebAssemblyHostBuilderExtensions`,
    `ServiceCollectionExtensions`, `IBffAuthenticationStateProvider`, `IAntiforgeryTokenStore`,
    `CookieHandler`, all in namespace `Cloudstrap.BlazorWasm`; every public class sealed or static;
    exactly the two approved interfaces (the D-8 internals made permanent).
  - `BlazorWasmAssembly_DeclaresNoDroppedConcepts` — no declared type/method name contains
    `Localization`, `Culture`, `StoredCulture` (D-1/DL-4 made permanent), no method name contains
    `AddRefitClientWithCookies` or `PathBase` (the skill-drift ghosts stay dead), and no type name
    contains `BlazorWasmOptions` other than `CloudstrapBlazorWasmOptions` (the dropped wrapper).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.BlazorWasm.Tests\bin\Debug\net10.0\Cloudstrap.BlazorWasm.Tests.exe --filter "PackageSurfaceTests"
  ```

**GREEN**: add the csproj metadata, write `README.md`, edit `blazor.md` and the refit skill per Scope.

**DB changes**: none.

**VERIFY**:
1. Test exe (full run) → all tests pass, including the four permanent guards.
2. `dotnet build src/Cloudstrap.sln -c Release` →
   `src/Cloudstrap.BlazorWasm/bin/Release/Cloudstrap.BlazorWasm.<version>.nupkg`; expand a `.zip`
   copy → contains `README.md`, `icon.png`, `lib/net10.0/Cloudstrap.BlazorWasm.dll` **and** `.xml`;
   the nuspec shows the MIT license expression, description, tags, repository URL, and a dependency
   list of exactly the four packages (`Microsoft.AspNetCore.Components.WebAssembly`,
   `Microsoft.AspNetCore.Components.Authorization`, `Microsoft.Extensions.Http`, `Refit`) — no
   `Aspire.*`, no `Cloudstrap.*`, no `Refit.HttpClientFactory`, **no frameworkReference group**
   (AC-BW8, AC-ASP2, DL-1, DL-3).
3. **AC-BW8 identifier sweep** (new package + tests):
   ```powershell
   Get-ChildItem -Recurse -File -Path src/Cloudstrap.BlazorWasm, src/Test/UnitTest/Cloudstrap.BlazorWasm.Tests |
     Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
     Select-String -Pattern '(?i)(nihdi|riziv|cfe)'
   ```
   → zero matches (guard-test tripwire patterns are self-referential and excluded by reading the
   hits, as in plans 2–12).
4. Doc check: `blazor.md`'s BlazorWasm content matches the shipped surface with the drift note gone;
   the refit skill's WASM half names `AddCloudstrapWasmRefitClient` and no
   `PathBaseDelegatingHandler`; no other sections changed.
5. Full-suite check (mechanic (i)) — all green; `dotnet format` exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 1: the client package surface is frozen *(covers Steps 1–3)*

*Executor: STOP here. Present the results of all covered steps and WAIT for user approval — do not start the next step.*

⚠️ **Risk areas at this gate**: **all-new public API** — the six public types against the spec's
Public API Sketch **verbatim** (any deviation needs naming; confirm the mechanic-(c) optional-options
`CookieHandler` constructor as the sketch's `/* ... */` made concrete) · **auth code (client half)**
— `CookieHandler`'s credentials/attachment rules and the provider's failure ladder reviewed in code ·
**dependency updates** — the `Directory.Packages.props` diff: the new `Refit` 11.2.0 pin (license +
comment), the two new 10.0.10 framework-package pins the spec called "repo-pinned" but which did not
yet exist (**spec-drift interpretation — confirm**), the `Components.WebAssembly` pin's move out of
the demo-only group; the expanded Release nupkg's dependency list · mechanic (a)'s internal
`IServiceCollection` seam + `InternalsVisibleTo` as the composite's test strategy (the public
builder wrapper is proven live in Step 5 — confirm) · the two instruction-file edits reviewed
verbatim.

- [ ] Behavioral verification: test exe output shows — credentials-Include on every request, the
  mutating-only/configured-name/replace/empty-store XSRF attachment matrix incl. the D-3
  overridden-header proof, the wired typed-client and Refit pipelines with camelCase defaults and
  per-call override, the one shared store (Step 1); the `BffCookie` principal with claims, the
  four-way anonymous failure ladder without a throw, capture from the configured header, the
  one-HTTP-call cache and the exactly-two-calls clear-notify-refetch, config binding with the
  delegate winning, the no-localization and TryAdd-idempotence proofs (Step 2); the four permanent
  surface guards green, the expanded Release nupkg reviewed, the identifier sweep empty (Step 3).
- [ ] Code review: the registration code against mechanics (b)/(d)/(e) — the finding-5 defects
  (double-invoked delegate, factory lambdas) absent; `sealed`/static/internal per D-8; single
  namespace; full XML docs incl. the no-secrets configuration statement; the csproj → four
  PackageReferences, zero ProjectReferences, zero FrameworkReference.
- [ ] User approved — implementation may continue past this gate

---

## Slice 2 — The DL-2 server half: a Bff opts in to the user endpoint and the XSRF contract becomes two-sided and validated ⚠️ AUTH RISK AREA (amendment to shipped #10 — explicit auth review)

---

## Step 4 — A Bff calling `MapCloudstrapBffUserEndpoint()` serves the documented wire contract at the configured path (anonymous-safe, 200 always) with the XSRF request token in the configured response header — and a browser-style client that signed in through #10's real login can make a mutating call that **passes** stock antiforgery validation with the header and is **rejected** without it (AC-BW7's unit round trip; AC-BW6's server halves; DL-2; D-7; mechanic (f))

- [ ] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Authentication.OpenIdConnect/CloudstrapOpenIdConnectOptions.cs` *(modify)* —
  mechanic (f): two new bound properties `UserEndpointPath` (default `"/bff/user"`) and
  `XsrfHeaderName` (default `"X-XSRF-TOKEN"`), XML-documented next to `LoginPath`/`LogoutPath` with
  the both-sides-must-agree contract and a pointer to `Cloudstrap.BlazorWasm`'s matching client
  options. *(Pre-release breaking allowance: supersedes #10 D-5's "no user-info endpoint" remark —
  proposed explicitly per the spec, never silently.)*
- `src/Cloudstrap.Authentication.OpenIdConnect/EndpointRouteBuilderExtensions.cs` *(modify)* — the
  new opt-in `MapCloudstrapBffUserEndpoint(this IEndpointRouteBuilder) : IEndpointRouteBuilder` on
  the **existing** class (no new public type — the shipped `PackageSurfaceTests` stay green
  unchanged): guard clause; map-time `IAntiforgery` presence check throwing
  `InvalidOperationException` naming `AddAntiforgery` (mechanic (f)); `MapGet(options.
  UserEndpointPath, ...)` with `.AllowAnonymous()`; the handler builds the wire contract from
  `HttpContext.User` (`isAuthenticated` from the identity, `userName` from `Identity.Name`, claims
  type/value 1:1; claims may be omitted/`null` when anonymous — the client tolerates both), calls
  `IAntiforgery.GetAndStoreTokens(httpContext)` and sets the request token as the
  `options.XsrfHeaderName` response header, returns 200 JSON (camelCase — the framework's web
  defaults; the XML remarks document the exact wire shape and that
  `MapCloudstrapAuthenticationEndpoints` stays login/logout-only, its D-5 remark updated to point
  here instead of "no user-info endpoint").
- `src/Cloudstrap.Authentication.OpenIdConnect/README.md` *(modify)* — a "BFF user endpoint + XSRF"
  section: the opt-in mapper, the wire contract, the two options, the consumer's stock validation
  recipe (`AddAntiforgery(o => o.HeaderName = ...)` matching `XsrfHeaderName` + validating the
  mutating endpoints), the anonymous-token note (a token issued to an anonymous session does not
  validate for the later signed-in user — the full-page login reload refreshes it, the documented
  pattern).
- `src/Test/UnitTest/Cloudstrap.Authentication.OpenIdConnect.Tests/BffUserEndpointTests.cs`
  *(create)* — on the existing `OidcTestHost` (`afterRegistration` registers
  `AddAntiforgery(o => o.HeaderName = ...)`; `mapEndpoints` maps the new mapper plus a fixture
  mutating POST endpoint that validates via stock `IAntiforgery.ValidateRequestAsync` — the
  consumer-validation stand-in mirroring the demo's wiring).

**RED** *(write these tests first, run them, confirm they fail — the mapper does not exist; this is
the plan-template's endpoint-integration block: real pipeline, real sign-in, full-stack assertions)*:
- Unit/integration test file: `BffUserEndpointTests.cs`
  - `BffUserEndpoint_Anonymous_Returns200AnonymousWireContractWithTheXsrfHeader` — no sign-in: 200,
    `isAuthenticated: false`, camelCase property names asserted on the raw JSON, and the
    `X-XSRF-TOKEN` response header present and non-empty (the anonymous-safe probe + issuance —
    happy path, error-free by contract).
  - `BffUserEndpoint_SignedIn_ReturnsNameAndClaimsFromTheCookiePrincipal` — full
    `BrowserlessUserAgent` sign-in through #10's real `/account/login`: `isAuthenticated: true`,
    `userName` = the seeded display identity, the claims array carries the principal's claims 1:1.
  - `BffUserEndpoint_MutatingCall_WithTheIssuedToken_PassesValidation_AndWithoutIt_IsRejected` —
    **the AC-BW7 round trip**: signed-in agent GETs the user endpoint, echoes the issued header
    token (and the antiforgery cookie the agent's cookie container already carries) on a POST to
    the fixture endpoint → 200; the same POST without the header → 400 (validation is real, not
    theater — D-7; the error case).
  - `BffUserEndpoint_ConfiguredPathAndHeaderName_AreHonored` —
    `Cloudstrap:OpenIdConnect:UserEndpointPath=/session/me` +
    `:XsrfHeaderName=X-CUSTOM-XSRF` (with the fixture's `AddAntiforgery` header matched): the
    contract serves at the new path with the custom header, and `/bff/user` 404s (AC-BW6's server
    half).
  - `BffUserEndpoint_MapperNotCalled_MapsNothing` — without the opt-in call `/bff/user` is 404
    (the #10 "nothing mapped unless the consumer calls the mapper" posture preserved).
  - `MapCloudstrapBffUserEndpoint_WithoutAntiforgeryServices_ThrowsNamingAddAntiforgery`
    (mechanic (f)'s fail-loud clause).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Authentication.OpenIdConnect.Tests\bin\Debug\net10.0\Cloudstrap.Authentication.OpenIdConnect.Tests.exe --filter "BffUserEndpointTests"
  ```

**GREEN**: the Scope items — the mapper only; **no change** to `MapCloudstrapAuthenticationEndpoints`
behavior, no new services in `AddCloudstrapOpenIdConnect`, no new package dependency (stock
antiforgery rides the existing FrameworkReference).

**DB changes**: none.

**VERIFY**:
1. Test exe (full OpenIdConnect.Tests run) → the new tests pass **and every pre-existing #10 test
   passes unchanged** (incl. `PackageSurfaceTests` — the amendment added no public type): a Bff can
   now opt in to a documented, XSRF-issuing user endpoint whose tokens really validate — the
   source's dead client half (finding 2) has a living server counterpart.
2. Full-suite check (mechanic (i)) — all green; `dotnet format` exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 2: the #10 amendment shipped ⚠️ AUTH REVIEW *(covers Step 4)*

*Executor: STOP here. Present the results and WAIT for user approval — do not start the next step.
This gate is the explicit human review CLAUDE.md requires for auth-code changes.*

⚠️ **Risk areas at this gate**: **auth code in a shipped package** — the sanctioned pre-release
breaking-allowed amendment to `Cloudstrap.Authentication.OpenIdConnect` reviewed line by line: the
mapper's `AllowAnonymous` posture, the `GetAndStoreTokens` issuance, the fail-loud
missing-`AddAntiforgery` check, no change to login/logout, no new dependency · **public API of #10**
— one new method on an existing approved class + two new option properties (mechanic (f)'s option
home — **confirm** `CloudstrapOpenIdConnectOptions` over a separate options type) · the D-5 XML
remark's supersession wording · the README's validation recipe (the security-relevant consumer
instruction — reviewed for correctness, incl. the anonymous-token/login-reload note).

- [ ] Behavioral verification: test exe output shows — the anonymous 200 wire contract with the
  issued header, the signed-in principal round trip through the real #10 login, the
  token-passes/header-less-rejected validation pair (AC-BW7's unit proof), the configured
  path/header override, the not-mapped 404, and the fail-loud throw; the full #10 suite green
  unchanged.
- [ ] Code review: wire-contract JSON shape against the spec's Behaviors row verbatim (camelCase,
  200 always, claims 1:1); no antiforgery service registration smuggled into `Add`; XML docs
  complete.
- [ ] User approved — implementation may continue past this gate

---

## Slice 3 — Demonstrated live: the demo WASM app runs on the package, the Bff completes the server half, and the browser proves the two-sided validated XSRF contract end to end

---

## Step 5 — The `Cloudstrap.Demo.BlazorWasm` app is rewritten onto the package: the Client boots through `AddCloudstrapBlazorWasm()` + `AddCloudstrapWasmRefitClient<IDoctorServiceClient>`, `DoctorsViewModel` swaps its raw `HttpClient` and hand-rolled state probe for the Refit client and the package's auth state (BlazorCommon usage stays — band interop), the Bff maps `MapCloudstrapBffUserEndpoint()` and turns on real antiforgery validation for `POST api/doctor`, `AuthorizeView` renders the session — and new E2E tests prove the wire contract and the AC-BW7 rejection live while every pre-existing E2E test stays green unchanged (AC-BW1/2/3/5 live; AC-BW7 live; AC-BW9; mechanics (g), (h); demonstration slice — workflow rule 9) ⚠️ DEMO AUTH-SURFACE RISK AREA

- [ ] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/demo/BlazorWasm/Client/Cloudstrap.Demo.BlazorWasm.Client.csproj` *(modify)* — add
  `<ProjectReference>` → `..\..\..\Cloudstrap.BlazorWasm\Cloudstrap.BlazorWasm.csproj`.
- `src/demo/BlazorWasm/Client/Program.cs` *(modify)* — the headline rewrite (teaching comments): the
  plain `AddScoped(_ => new HttpClient(...))` line is **deleted**, replaced by
  `builder.AddCloudstrapBlazorWasm();` +
  `builder.Services.AddCloudstrapWasmRefitClient<IDoctorServiceClient>(builder.HostEnvironment.BaseAddress);`
  (DL-3 live); `AddCloudstrapCore`, `AddCloudstrapBlazorCommon<IDoctorsViewModel>`, `AddMudServices`
  and the `SnackbarErrorHandler` registration stay exactly as they are.
- `src/demo/BlazorWasm/Presentation/Cloudstrap.Demo.BlazorWasm.Presentation.csproj` *(modify)* —
  add the `Cloudstrap.BlazorWasm` ProjectReference (mechanic (h): the Refit interface,
  `AuthenticationStateProvider` and Refit attributes flow from it).
- `src/demo/BlazorWasm/Presentation/Doctors/IDoctorServiceClient.cs` *(create)* — the Refit
  interface: `[Get("/api/doctor")] Task<List<DoctorDto>> GetDoctorsAsync(CancellationToken ct =
  default);` and `[Post("/api/doctor")] Task<DoctorDto> AddDoctorAsync([Body] AddDoctorDto doctor,
  CancellationToken ct = default);` (routes match `DoctorController` exactly; name ends in `Client`
  — outside the #11 convention scan, registered by the package helper).
- `src/demo/BlazorWasm/Presentation/Doctors/DoctorsViewModel.cs` *(modify)* — mechanic (h):
  constructor becomes `(IDoctorServiceClient client, AuthenticationStateProvider authState,
  IErrorHandler errorHandler)`; `InitializeAsync` awaits `authState.GetAuthenticationStateAsync()`
  (the package's cached BFF fetch — **this call also captures the XSRF token before any POST**),
  sets `SignedIn`/`SignedInName` from the principal, loads doctors through the Refit client when
  signed in; `AddDoctorAsync` posts through the Refit client, routing `ApiException` /
  `HttpRequestException` to the consumer's `IErrorHandler` (the blank-name 400's server message
  extraction preserved so the #11 snackbar E2E test stays green); the `api/v1/user/state` probe and
  the raw `HttpClient` are gone from the client side.
- `src/demo/BlazorWasm/Presentation/Doctors/IDoctorsViewModel.cs` *(modify — only if the member set
  shifts; the page-facing surface stays: `SignedIn`, `SignedInName`, `Doctors`, `NewName`,
  `NewSpecialty`, `AddDoctorAsync`)*.
- `src/demo/BlazorWasm/Presentation/Shared/MainLayout.razor` *(modify)* — an `<AuthorizeView>` block
  in the app bar showing the signed-in name (`data-testid="auth-status"`) — the composite's
  `AddCascadingAuthenticationState` demonstrated in markup (the spec's "the router gains
  `AuthorizeView`-driven sign-in state"); existing testids untouched.
- `src/demo/BlazorWasm/Bff/Program.cs` *(modify)* — `builder.Services.AddAntiforgery(o =>
  o.HeaderName = "X-XSRF-TOKEN");` (the consumer's stock validation wiring, matched to the default
  `XsrfHeaderName` — the DL-2 both-sides contract in one visible place, teaching comment);
  `endpoints.MapCloudstrapBffUserEndpoint();` added between the auth endpoints and the SPA
  fallback; the stale "(replaced by deliverable #13)" challenge-shaping comment rewritten to the
  shipped truth (mechanic (g): the shaping stays — it is what keeps API callers on bare 401s).
- `src/demo/BlazorWasm/Bff/Controllers/DoctorController.cs` *(modify)* —
  `[ValidateAntiForgeryToken]` on `Add` (the mutating POST now really validates — D-7 live; a
  teaching comment naming AC-BW7).
- `src/demo/BlazorWasm/Bff/Controllers/UserController.cs` *(modify — comments only)* — the two
  "the BFF user-info contract is deliverable #13" placeholder remarks rewritten: `user/state` stays
  as SUT app code pinned by its own E2E test; the shipped contract is
  `MapCloudstrapBffUserEndpoint`'s `/bff/user` (mechanic (g)).
- `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/DoctorsTests.cs` *(modify)* — two new tests (below);
  **every pre-existing test untouched**.
- `src/demo/BlazorWasm/README.md` *(modify)* — feature-matrix rows for #13: the composite + auth
  state (`AddCloudstrapBlazorWasm()` | the new wire-contract E2E test), the Refit client + validated
  XSRF (`AddCloudstrapWasmRefitClient<IDoctorServiceClient>` + `MapCloudstrapBffUserEndpoint()` +
  `[ValidateAntiForgeryToken]` | the new rejection E2E test); the harness note calling the
  `user/state` probe and challenge shaping "placeholder code deliverable #13 replaces" rewritten per
  mechanic (g).

**RED** *(write these tests first, run them, confirm they fail — today the Bff maps no `/bff/user`,
issues no XSRF token, and validates nothing, so neither test can pass)*:
- E2E test file: `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/DoctorsTests.cs`
  - `BffUserEndpoint_AnonymousApiGet_ReturnsTheWireContractWithTheXsrfHeader` — plain `HttpClient`
    against 5300: GET `/bff/user` → 200 JSON with `isAuthenticated: false` (camelCase asserted on
    the raw body) and a non-empty `X-XSRF-TOKEN` response header (the DL-2 server half live).
  - `AddDoctor_SignedInPostWithoutTheXsrfHeader_IsRejected_WhileTheFormFlowSucceeds` — **the AC-BW7
    live proof**: sign in via `BrowserSignIn.SignInAsync(Page, BaseUrl, "/doctors")`; wait for the
    grid; (1) `Page.EvaluateAsync` issues an in-page `fetch("api/doctor", { method: "POST", ... })`
    **without** the XSRF header (the session cookie rides along — a genuine CSRF-shaped call): the
    returned status is 400, the grid gains no row; (2) the real form add
    (`doctor-name-input`/`add-doctor-submit`) succeeds and the new row appears — the package
    attached the captured token, validation passed; (3) `GetByTestId("auth-status")` contains the
    seeded display name (`AuthorizeView` + cascading state live — AC-BW1). *(No `ConsoleErrors`
    assertion around the deliberate 400 — the `AddDoctor_WithBlankName` precedent.)*
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\E2E\Cloudstrap.Demo.E2E.Tests\bin\Debug\net10.0\Cloudstrap.Demo.E2E.Tests.exe --filter "BffUserEndpoint_AnonymousApiGet_ReturnsTheWireContractWithTheXsrfHeader|AddDoctor_SignedInPostWithoutTheXsrfHeader_IsRejected_WhileTheFormFlowSucceeds"
  ```

**GREEN**: the Scope items. **Every pre-existing E2E test must stay green unchanged** — in
particular the whole `DoctorsTests` suite (auto-triggered login, seeded grid, form add, blank-name
snackbar, business-trace telemetry, anonymous 401s, `user/state` 200) — that is AC-BW9's carried
regression net, now flowing through the package's cookie+XSRF pipeline and the validated POST.
*(If any existing test is disturbed, the executor reports it at the gate rather than weakening any
assertion.)*

**DB changes**: none.

**VERIFY**:
1. E2E exe → the two new tests pass **and every pre-existing E2E test passes unchanged** (build
   first; one-time `playwright.ps1 install chromium` if needed) — the untouched
   `DoctorsPage_AddDoctor_NewDoctorAppearsInGrid` green run now proves the package-attached token
   passes real validation, and the untouched anonymous/401/state tests prove mechanic (g) kept the
   contract.
2. Manual smoke (optional but recorded): run IdP + Api + Bff per the README, browse `/doctors`,
   sign in → grid renders, add succeeds, the app-bar shows the signed-in name;
   `curl http://127.0.0.1:5300/bff/user` → the wire contract + header.
3. Full-suite check (mechanic (i)) — all green; `dotnet format` exit 0; the demo projects still pack
   nothing.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — final: deliverable #13 complete *(covers Step 5; closes the deliverable)*

*Executor: STOP here. Present the results and WAIT for user approval. Any Git push afterwards requires
the user's explicit go-ahead (CLAUDE.md: no push without confirmation).*

⚠️ **Risk areas at this gate**: **the demo auth surface** — real antiforgery validation is now live
on the demo's mutating POST and the client's auth probe moved into the shipped package; the untouched
pre-existing E2E suite is the tripwire · **mechanic (g)'s placeholder resolution** — `user/state` and
the challenge shaping deliberately kept (their E2E tests pin them); confirm this reading of the
spec's "resolving the placeholder" wording · **mechanic (h)** — the Presentation RCL's demo-level
`Cloudstrap.BlazorWasm` reference and the `AuthenticationStateProvider`-injecting ViewModel ·
no shared-contract change (`Cloudstrap.Demo.Contracts` untouched), no IdP change.

- [ ] Behavioral verification: the two new E2E tests pass
  (`BffUserEndpoint_AnonymousApiGet_ReturnsTheWireContractWithTheXsrfHeader`,
  `AddDoctor_SignedInPostWithoutTheXsrfHeader_IsRejected_WhileTheFormFlowSucceeds`) and **all
  pre-existing E2E tests pass unchanged** (every `DoctorsTests` flow in particular); the full-suite
  check (build + 13 unit exes + E2E exe + `dotnet format --verify-no-changes`) is green end to end.
- [ ] Spec acceptance sign-off: walk **AC-BW1…AC-BW9 + AC-ASP2 + AC-A3** against the step evidence
  using the Overview's AC coverage map — all met; confirm nothing from the spec's Drop /
  Out-of-Scope lists was resurrected (no `BlazorWasmOptions` wrapper, no hidden `AddLocalization`,
  no `ApplyStoredCultureAsync`/culture/JS-interop code — DL-4, no
  `Microsoft.Extensions.Localization` or `Refit.HttpClientFactory` reference, no browser-held-token
  machinery, no Duende.BFF, no WASM `AddCloudstrapHttpServiceClient` variant, no
  `PathBaseDelegatingHandler`, no `Cloudstrap.BlazorCommon` ProjectReference in the package, no
  BlazorServer cross-reference, zero `Aspire.*`, zero `Nihdi.*`, zero FrameworkReference in the
  package closure) and every De-NIHDI row is closed (`AddBlazorWasmForNihdi`/`AddNihdiWasm*` → the
  Cloudstrap names, `api/user` → `bff/user`, no company headers, no `Cfe` identifiers).
- [ ] Docs review: `src/Cloudstrap.BlazorWasm/README.md` and the #10 README's user-endpoint section
  match as-built behavior (incl. the no-secrets statement and the both-sides `XsrfHeaderName`
  contract); `src/demo/BlazorWasm/README.md` matrix/harness notes cite the real E2E test names and
  no longer call the kept endpoints placeholders; `blazor.md` + the refit skill carry the shipped
  truth with the band drift note gone. **User-owned follow-ups (not in this plan)**: the DL-1
  founding-spec Package Map amendment (`_specs/Cloudstrap.md` is user-amended only — wording in the
  spec's Decision Log); #24 inherits `ApplyStoredCultureAsync` per DL-4.
- [ ] User approved — deliverable #13 done; project-manager flips the ROADMAP row to ✅.
