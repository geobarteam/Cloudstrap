# Plan: 12-BlazorServerHelpers — A consumer bootstraps a hardened, observable Blazor Server app with one registration call and one pipeline call (`AddCloudstrapBlazorServer` + `UseCloudstrapBlazorServer<App>`), gets circuit-originated work traced through `IBlazorInteractionTrace`, and the demo app proves it end to end

## Overview

Deliverable #12 of the extraction roadmap: the new `Cloudstrap.BlazorServer` package — the suite's
**eleventh** — the Blazor-flavored composite that orders the already-shipped constituents (#1 core
options, #2 correlation, #4 health probes, #10 auth pairing) into the middleware order a hardened
Blazor Server app needs, plus the one genuinely new capability: the D-9 `IBlazorInteractionTrace`
interaction scope. **Binding spec: `_specs/12-BlazorServerHelpers.md`** (APPROVED 2026-08-22, zero
Open Questions, Decision Log D-1…D-13 final). Its Port Decision Table, Public API Sketch, Behaviors
& Conventions table, Dependencies table, Deliberate Behavior Changes, Edge Cases and Out-of-Scope
list are authoritative and are not re-litigated here. Nothing the spec marked Drop appears in this
plan: no `NihdiControls`/assembly registry (`AdditionalAssemblies` replaces it), no
`IDistributedTraceService`/`DistributedTrace`/`UseDistributedTrace<T1..T5>`/static `ActivityListener`,
no `ActivitySourceDelegatingHandler` (the automatic-handler variant was explicitly rejected in D-9),
no BlazorServer-specific typed-HttpClient registration API (#4's `AddCloudstrapHttpServiceClient` is
the one way — AC-ASP3 holds by construction), no `SecurityHardeningOptions`, no NWebsec, no
controllers/`RequireAuthorization` (D-6), no WASM reflection (D-7), no KeyVault-data-protection /
localization / Scalar auto-wiring (D-8), no `[Obsolete]` legacy methods (D-10), no forwarded headers /
HTTPS redirection (D-5), no `Cloudstrap.BlazorCommon` ProjectReference (D-13 — demo-level adoption
only).

Reference patterns, all read in full before planning:

- **Primary shape precedent: `src/Cloudstrap.Mvc`** — the closest shipped sibling and the idiom this
  package mirrors per spec finding 9: `CloudstrapMvcConfigurator` (code-level) + `CloudstrapMvcOptions`
  (`Cloudstrap:Mvc` section, validated at startup, section optional) + `UseCloudstrapMvc` with the
  fixed order, the `_pipelineMarker` double-call throw, the `__AuthenticationMiddlewareSet` /
  `__AuthorizationMiddlewareSet` placement markers, the scheme-map auth test, path base from
  `Cloudstrap:Application:PathBase`, the internal set-if-absent `SecurityHeadersMiddleware`, the
  internal `EnvironmentDefault.Resolve(bool?, bool)` helper, `HstsSettings`/`ExceptionHandlingSettings`
  as package-local re-expressions.
- **Constituents composed (read on disk)**: `src/Cloudstrap.Extensions` —
  `MapCloudstrapHealthChecks` (marker-based idempotence, `Cloudstrap:HealthChecks` section),
  `AddCloudstrapHttpServiceClient` + the `IUserAccessTokenHandlerProvider` seam,
  `AddCloudstrapDataProtection` (README pointer only, D-8); `src/Cloudstrap.Observability` —
  `AddCloudstrapCorrelation` + `UseCloudstrapCorrelation`, `ICorrelationContextAccessor` (async-local,
  last-write-wins), `ICorrelationSource`/`TraceIdCorrelationSource`, the `BusinessTrace`/
  `BusinessTraceScope` no-op-when-unsampled shape D-9 matches, `CloudstrapActivitySources` (the
  published-constant pattern), `OpenTelemetryPipeline` (owner and contribute both build on
  `services.AddOpenTelemetry()` — the fact the AC-BS6 mechanism relies on), `BlazorHubSampler`
  (**stays in Observability** — this package must not duplicate or move it).
- **Test infrastructure**: `src/Test/UnitTest/Cloudstrap.Mvc.Tests` — `Infrastructure/MvcTestHost`
  (real pipeline on `TestServer`, in-memory configuration, neutral `Cloudstrap:Application` identity),
  `PipelineCompositionTests` (probes/correlation/hooks-order/double-call/scheme-map shapes reused),
  `EdgeHardeningTests` (HSTS-over-https and header assertions), `PackageSurfaceTests` (permanent
  guards); `src/Test/Directory.Build.props` (`.Tests`-suffix NUnit/MTP wiring inherited).
- **Demonstration vehicle (read on disk)**: `src/demo/BlazorServer/Cloudstrap.Demo.BlazorServer`
  (port 5340) — `Program.cs` (the hand-rolled block the composite replaces, resolving #27's D-B
  placeholder), `Components/Pages/WhoAmI.razor` (the API-calling page restructured to the ViewModel
  pattern), `Services/IDemoApiClient.cs`/`DemoApiClient.cs` (unchanged — the AC-BS4 posture),
  `appsettings.json` (Otlp mode with `EnableConsole` defaulting `true` — the console exporter the
  live D-9 assertion reads), `README.md` (the feature matrix this plan extends).
- **E2E harness (read on disk)**: `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/BlazorServerTests.cs`
  (fixture-owned host boot + `BrowserSignIn` + `GetByTestId`), `DoctorsTests.cs` line ~181 (the
  `CapturedOutput` polling precedent for console-telemetry assertions), `E2eFixture` (IdP 5310 →
  Api 5330 → Bff 5300 unchanged).
- **Plan-shape precedents**: `_plans/11-BlazorSharedAbstractions.md`, `_plans/7-WorkerBootstrap.md`,
  `_plans/6-MvcBootstrap.md` — brand-new-project RED mechanics, `PackageSurfaceTests` permanent
  guards, packaging step shape, demo + E2E demonstration slice, final-gate AC walk.

This is a library deliverable with no database. The plan-template's endpoint-integration block does
not apply literally; its equivalent here is that **every step's tests boot a real ASP.NET Core
pipeline on `TestServer` through the same two calls a consumer writes and assert over real HTTP
responses** — plus the mandatory demonstration slice (Step 6) driving the real browser and the real
three-host topology.

### AC coverage map (every criterion claimed by at least one step)

| Criterion | Step(s) |
|---|---|
| AC-BS1 (two calls: root component + Interactive Server, anonymous probes, correlation active) | 1 (unit) · 6 (live) |
| AC-BS2 (three security headers set-if-absent + frame-options switch, hardened antiforgery cookie, HSTS outside Development) | 2 (unit) · 6 (live headers) |
| AC-BS3 (no scheme → no auth middleware, everything anonymous; scheme → auth after routing, before antiforgery/endpoints) | 3 |
| AC-BS4 (user-flagged #4 typed client works from the composite pipeline; no BlazorServer client API exists) | 5 (surface guard) · 6 (live round trip) |
| AC-BS5 (interaction root span detached from the hub trace, child parenting, correlation = interaction trace id, restore on dispose) | 4 (unit) · 6 (live console span) |
| AC-BS6 (source contributed additively — no second pipeline, no exporter) | 4 |
| AC-BS7 (`StaticServer`: no interactive services/render mode, rest of pipeline unchanged) | 1 |
| AC-BS8 (every convention has an override) | 2 · 3 (each default's override pinned where the default is pinned) |
| AC-BS9 (build/tests/format, XML docs, metadata, identifier sweep, no new external dependency) | 5 |
| AC-BS10 (demo rewritten onto the composite; pre-existing E2E green; ≥ 1 new E2E) | 6 |
| AC-ASP2 (zero `Aspire.*`) / AC-A3 (zero `Nihdi.AspNetCore`) | 5 (permanent guards) |
| AC-ASP3 (no second resilience layer — no wrapper around #4's registration) | 5 (no-client-API guard; holds by construction) |

### Dependency closure (hand-off constraint 4 — nothing new)

Exactly **one ProjectReference** (`Cloudstrap.Extensions`, bringing `Cloudstrap.Core` +
`Cloudstrap.Observability` transitively) + the `Microsoft.AspNetCore.App` **FrameworkReference**.
**Zero new NuGet packages and zero new CPM entries.** The one subtlety, flagged for Gate 2 review:
Step 4 calls `ConfigureOpenTelemetryTracerProvider` (namespace
`Microsoft.Extensions.DependencyInjection`, shipped in `OpenTelemetry.Api.ProviderBuilderExtensions`),
which reaches this package **transitively** through `Cloudstrap.Extensions` →
`Cloudstrap.Observability` → `OpenTelemetry.Extensions.Hosting` — package compile assets flow through
ProjectReferences, so no direct `PackageReference` (and no new pin) is added. The nupkg dependency
list stays exactly `Cloudstrap.Extensions`. `Cloudstrap.BlazorCommon` is referenced **only** by
`Cloudstrap.Demo.BlazorServer.csproj` (D-13). The unit-test project adds only already-pinned packages
(`Microsoft.AspNetCore.TestHost`, `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Exporter.InMemory`).

### ⚠️ Risk areas (reviewed at the gates named)

- **All-new public API surface** — ten public types in namespace `Cloudstrap.BlazorServer`, signed
  off against the spec's Public API Sketch: the composite six at **Gate 1**
  (`CloudstrapBlazorServerOptions` + `HstsSettings` + `ExceptionHandlingSettings` — the sketch's two
  settings property types made concrete as package-local re-expressions, the Mvc D-2 precedent —
  `CloudstrapBlazorServerConfigurator`, `BlazorInteractivity`, `BlazorServerPipelineOptions`,
  `WebApplicationBuilderExtensions`, `WebApplicationExtensions`); `IBlazorInteractionTrace` and the
  planner-added `BlazorServerActivitySources` constants class (sanctioned by the spec's Behaviors
  table: "consumers … can also `AddSource` the published constant themselves"; mirrors
  `CloudstrapActivitySources`) at **Gate 2**.
- **Auth code / auth middleware placement** — `UseCloudstrapBlazorServer` places authentication and
  authorization middleware conditionally (scheme-map test + the `__AuthenticationMiddlewareSet` /
  `__AuthorizationMiddlewareSet` placement markers — the shipped Mvc mechanic, D-3). Reviewed at
  **Gate 1** (unit-proven placement) and the **final gate** (the demo's OIDC login and user-token
  round trip through the composite pipeline).
- **Demo auth surface** — Step 6 rewrites the demo's `Program.cs` (which currently hand-places
  `UseAuthentication`/`UseAuthorization`) onto the composite; the untouched pre-existing E2E sign-in
  test staying green is the tripwire (**final gate**).
- **Transitive OpenTelemetry API usage** (above) — **Gate 2** (nupkg dependency-list inspection).

### Planner mechanics decided here (no spec conflict; each flagged for review at the named gate)

**(a) The Interactivity-decided-once mechanism (hand-off constraint 2).** `AddCloudstrapBlazorServer`
registers an internal singleton `BlazorServerRegistrationState` carrying the resolved
`BlazorInteractivity` value (the `AuthenticationEndpointsState` precedent from #10).
`UseCloudstrapBlazorServer<TRoot>` resolves it and follows it — there is **no** render-mode knob on
`BlazorServerPipelineOptions`, so the source's duplicated Add/Use knob defect is structurally
impossible. The state's absence doubles as the missing-Add detection: `Use` without `Add` throws
`InvalidOperationException` naming `AddCloudstrapBlazorServer`. *(Gate 1.)*

**(b) The pipeline order, spec row made concrete.** Exception head (developer page per
`EnvironmentDefault.Resolve(options.ExceptionHandling.UseDeveloperExceptionPage, IsDevelopment())`,
else `UseExceptionHandler(new ExceptionHandlerOptions { ExceptionHandlingPath =
Cloudstrap:Application:ExceptionHandlerPath, CreateScopeForErrors = true })` — the razor-components
idiom) → HSTS (enabled + outside Development) → `SecurityHeadersMiddleware` (three constant headers)
→ `UsePathBase` (when configured) → `BeforeRouting` → `UseRouting` → `UseCloudstrapCorrelation` →
markers claimed + conditional `UseAuthentication` → `BeforeAuthorization` → conditional
`UseAuthorization` → `UseAntiforgery` → `BeforeEndpoints` → `MapStaticAssets` (flag, default on) →
`MapRazorComponents<TRoot>` (+ `.AddInteractiveServerRenderMode()` per Interactivity, +
`.AddAdditionalAssemblies(options.AdditionalAssemblies)` when non-empty, + `ConfigureComponentEndpoints`
hook last) → `MapCloudstrapHealthChecks` → `ConfigureEndpoints`. Double `Use` throws on a
package-local `"Cloudstrap.BlazorServer.Pipeline"` marker (deliberately distinct from Mvc's/WebApi's,
the cross-package-collision rationale in `Cloudstrap.Mvc/WebApplicationExtensions.cs`). *(Gate 1.)*

**(c) The D-9 scope shape.** `internal sealed class BlazorInteractionTrace : IBlazorInteractionTrace,
IDisposable` — a singleton owning `new ActivitySource(BlazorServerActivitySources.Interaction)`
(`"Cloudstrap.BlazorServer.Interaction"`), constructor-injecting `ICorrelationContextAccessor` +
`ICorrelationSource`. `StartInteraction(name)`: guard `ThrowIfNullOrWhiteSpace`; capture `previous
= Activity.Current` and `previousCorrelationId`; set `Activity.Current = null`; `StartActivity(name)`
(→ a **root**, detached from the dropped hub trace); set `CorrelationId = activity?.TraceId.ToString()
?? source.GenerateCorrelation()` (the no-listener edge case: a fresh identifier so the outbound header
stays stable). The returned internal scope restores `Activity.Current = previous` and the previous
correlation id on dispose, disposes the activity, never throws, tolerates double dispose
(restore-to-previous per scope — the spec's stack-safe reading). Registration in the composite:
`TryAddSingleton<IBlazorInteractionTrace, BlazorInteractionTrace>()` +
`services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddSource(
BlazorServerActivitySources.Interaction))` — the deferred contribution that applies **only if/when**
a DI-built tracer pipeline exists (Cloudstrap owner, Cloudstrap contribute, or Aspire-style — all
build on `services.AddOpenTelemetry()`), creating no pipeline and no exporter of its own (AC-BS6).
*(Gate 2.)*

**(d) Unit-test fixture strategy.** `Cloudstrap.BlazorServer.Tests` uses `Sdk="Microsoft.NET.Sdk.Razor"`
so it can carry real fixture components: `Fixtures/App.razor` (minimal HTML shell +
`<Routes />` + the `blazor.web.js` script), `Fixtures/Routes.razor` (`<Router
AppAssembly="typeof(App).Assembly">`), `Fixtures/StaticPage.razor` (`@page "/static-page"`, marker
text), `Fixtures/InteractivePage.razor` (`@page "/interactive"`, `@rendermode InteractiveServer`,
marker text), `Fixtures/ThrowingPage.razor` (`@page "/throws"`, throws in `OnInitialized`).
"Interactive Server rendering wired" is asserted behaviorally: the prerendered HTML of `/interactive`
carries the framework's `<!--Blazor:` server-marker comment (emitted on the initial HTTP response, no
WebSocket needed on `TestServer`); in `StaticServer` mode the same request fails with the framework's
own error (the spec's documented edge case — no package detection). A second, tiny Razor class
library **`Cloudstrap.BlazorServer.TestComponents`** (named without the `.Tests` suffix so the MTP
wiring ignores it; `IsPackable=false`) carries one routable `ExtraPage.razor` (`@page "/extra"`) —
the framework's assembly-boundary semantics make a second assembly the only honest way to test
`AdditionalAssemblies` both directions (without → 404, with → 200). `Infrastructure/
BlazorServerTestHost.cs` mirrors `MvcTestHost`: `TestServer`, neutral `Cloudstrap:Application`
identity, `beforeBuild`/`configure`/`pipeline`/`afterUse`/`environment` seams. Where the built-asset
manifest makes `MapStaticAssets` awkward on `TestServer`, fixtures may set `MapStaticAssets = false`
— the flag's default-on behavior is pinned separately. *(Gate 1.)*

**(e) Options posture.** `CloudstrapBlazorServerOptions` binds `Cloudstrap:BlazorServer` with
`.ValidateOnStart()` + `CloudstrapBlazorServerOptionsValidator` (`IValidateOptions`, Mvc precedent —
validates `Hsts.MaxAgeDays >= 1`, names the offending key); the section is optional (spec edge case:
all defaults pass). `EnableFrameOptions` defaults `true` (D-12). `ExceptionHandlingSettings` carries
**only** `UseDeveloperExceptionPage: bool?` (no `IncludeDetails` — this package ships no negotiated
JSON error contract; that is Mvc/WebApi territory). *(Gate 1.)*

**(f) Full-suite check** (the standing convention: `runTests` is not on the agent PATH — VERIFY
invokes each exe directly): `dotnet build src/Cloudstrap.sln`, then the **12** unit exes under
`src/Test/UnitTest/<Name>.Tests/bin/Debug/net10.0/<Name>.Tests.exe` (Core, Observability,
Observability.AzureMonitor, Extensions, WebApi, Mvc, Worker, TestIdentityProvider,
Authentication.ClientCredentials, Authentication.OpenIdConnect, BlazorCommon, **BlazorServer** — new
in Step 1), then the E2E exe
`src\Test\E2E\Cloudstrap.Demo.E2E.Tests\bin\Debug\net10.0\Cloudstrap.Demo.E2E.Tests.exe`, then
`dotnet format src/Cloudstrap.sln --verify-no-changes`.

**Target consumer composition** (the spec made concrete — also the demo `Program.cs`, Step 6, and the
README example, Step 5):

```csharp
builder.UseCloudstrapObservability();                 // separate, visible call (D-8)
builder.AddCloudstrapBlazorServer();                  // core, correlation, probes, razor components,
                                                      // hardened antiforgery, HSTS, IBlazorInteractionTrace
builder.Services.AddCloudstrapOpenIdConnect();        // pairing is a separate, visible call (D-3)
builder.Services.AddCloudstrapHttpServiceClient<IDemoApiClient, DemoApiClient>("DemoApi"); // #4, unchanged

WebApplication app = builder.Build();
app.UseCloudstrapBlazorServer<App>(pipeline =>
    pipeline.ConfigureEndpoints = endpoints => endpoints.MapCloudstrapAuthenticationEndpoints());
await app.RunAsync();
```

---

## Slice 1 — Two calls bootstrap a hardened, observable Blazor Server pipeline ⚠️ PUBLIC-API / AUTH-PLACEMENT RISK AREA

---

## Step 1 — A fresh Blazor Server app on the two composite calls serves routable components with Interactive Server rendering, answers `/healthz` and `/ready` anonymously, correlates every response, honors the once-made `Interactivity` decision (`StaticServer` wires nothing interactive), and fails loud on a second `Use` call or a missing `Add` call (AC-BS1; AC-BS7; mechanics (a), (b) skeleton, (e))

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.BlazorServer/Cloudstrap.BlazorServer.csproj` *(create)* — `Microsoft.NET.Sdk`,
  `net10.0`, `GeneratePackageOnBuild=true`, `GenerateDocumentationFile=true`;
  `<FrameworkReference Include="Microsoft.AspNetCore.App" />`; **exactly one**
  `<ProjectReference>` → `..\Cloudstrap.Extensions\Cloudstrap.Extensions.csproj`;
  `<InternalsVisibleTo Include="Cloudstrap.BlazorServer.Tests" />` (Mvc precedent).
  Description/tags/README metadata land in Step 5 (packable from day one — the #6/#7/#11 precedent).
- `src/Cloudstrap.BlazorServer/CloudstrapBlazorServerOptions.cs` *(create)* — sealed;
  `SectionName = "Cloudstrap:BlazorServer"`; `Hsts` (`HstsSettings`), `ExceptionHandling`
  (`ExceptionHandlingSettings`), `EnableFrameOptions` (default `true`, D-12).
- `src/Cloudstrap.BlazorServer/HstsSettings.cs` *(create)* — package-local re-expression, property
  set and defaults identical to `Cloudstrap.Mvc.HstsSettings` (`Enabled=true`, `MaxAgeDays=365`,
  `IncludeSubDomains=true`, `Preload=false`).
- `src/Cloudstrap.BlazorServer/ExceptionHandlingSettings.cs` *(create)* — mechanic (e): only
  `bool? UseDeveloperExceptionPage`.
- `src/Cloudstrap.BlazorServer/CloudstrapBlazorServerOptionsValidator.cs` *(create)* — internal
  `IValidateOptions<CloudstrapBlazorServerOptions>`, Mvc validator shape.
- `src/Cloudstrap.BlazorServer/CloudstrapBlazorServerConfigurator.cs` *(create)* — sealed;
  `BlazorInteractivity Interactivity` (default `InteractiveServer`), `Action<AntiforgeryOptions>?
  Antiforgery`, `Action<IRazorComponentsBuilder>? RazorComponents` (spec sketch verbatim).
- `src/Cloudstrap.BlazorServer/BlazorInteractivity.cs` *(create)* — `InteractiveServer`, `StaticServer`.
- `src/Cloudstrap.BlazorServer/BlazorServerPipelineOptions.cs` *(create)* — sealed; `bool
  MapStaticAssets = true`, get-only `IList<Assembly> AdditionalAssemblies` (initialized empty),
  `BeforeRouting`/`BeforeAuthorization`/`BeforeEndpoints` (`Action<IApplicationBuilder>?`),
  `Action<RazorComponentsEndpointConventionBuilder>? ConfigureComponentEndpoints`,
  `Action<IEndpointRouteBuilder>? ConfigureEndpoints` (spec sketch verbatim; **no render-mode knob**
  — mechanic (a)).
- `src/Cloudstrap.BlazorServer/BlazorServerRegistrationState.cs` *(create)* — internal sealed
  singleton carrying the resolved `Interactivity` (mechanic (a)).
- `src/Cloudstrap.BlazorServer/WebApplicationBuilderExtensions.cs` *(create)* —
  `AddCloudstrapBlazorServer(this WebApplicationBuilder, Action<CloudstrapBlazorServerConfigurator>? = null)`:
  guard clause; configurator applied eagerly; `AddCloudstrapCore()`, `AddCloudstrapCorrelation()`,
  `AddHttpContextAccessor()`, stock `AddHealthChecks()` (Aspire-additive by design), options binding +
  validator (mechanic (e)), `AddRazorComponents()` + `.AddInteractiveServerComponents()` only when
  `Interactivity == InteractiveServer`, `AddCascadingAuthenticationState()`, hardened
  `AddAntiforgery` (cookie `HttpOnly`, `SecurePolicy=Always`, `SameSite=Strict`; `configurator.
  Antiforgery` invoked **last** — final say, Step 2 pins it), `AddHsts` from options,
  `BlazorServerRegistrationState` singleton, `configurator.RazorComponents` invoked last on the
  builder. Registers **no authentication and no observability pipeline** (XML remarks state both,
  the Mvc wording). D-9 registration lands in Step 4. Repeat calls additive/idempotent.
- `src/Cloudstrap.BlazorServer/WebApplicationExtensions.cs` *(create)* —
  `UseCloudstrapBlazorServer<TRootComponent>(this WebApplication, Action<BlazorServerPipelineOptions>? = null)`
  where `TRootComponent : IComponent`: mechanic (b) order; this step wires the skeleton (exception
  head, HSTS, headers middleware pass-through, routing, correlation, conditional auth, antiforgery,
  `MapRazorComponents<TRoot>` + render mode per state, `MapCloudstrapHealthChecks`, `ConfigureEndpoints`),
  with Steps 2–3 pinning each behavior red-first. Double-call marker throw; missing-state
  (`Add` not called) throw naming `AddCloudstrapBlazorServer`.
- `src/Cloudstrap.BlazorServer/SecurityHeadersMiddleware.cs` *(create)* — internal, `OnStarting`
  set-if-absent (Mvc shape): `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`,
  and — when `EnableFrameOptions` — `X-Frame-Options: SAMEORIGIN` (D-12). Behavior pinned in Step 2.
- `src/Cloudstrap.BlazorServer/EnvironmentDefault.cs` *(create)* — internal `Resolve(bool?, bool)`
  re-expression (Mvc precedent).
- `src/Cloudstrap.BlazorServer/BlazorServerLog.cs` *(create)* — internal `LoggerMessage` set written
  fresh for this pipeline (the spec's re-expression of `WebApplicationExtensions.Log`; no orphaned
  messages).
- `src/Test/UnitTest/Cloudstrap.BlazorServer.Tests/Cloudstrap.BlazorServer.Tests.csproj` *(create)* —
  `Microsoft.NET.Sdk.Razor` (mechanic (d)), `net10.0`, `<FrameworkReference
  Include="Microsoft.AspNetCore.App" />`, ProjectReferences: the package +
  `Cloudstrap.Authentication.OpenIdConnect` (Step 3's scheme test; harmless a step early —
  already shipped); the `Cloudstrap.BlazorServer.TestComponents` reference is added **in Step 3**,
  where that RCL is created; version-less `<PackageReference>`s `Microsoft.AspNetCore.TestHost` (+ Step 4 adds
  `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Exporter.InMemory` — all already pinned);
  NUnit/MTP wiring inherited from `src/Test/Directory.Build.props`.
- `src/Test/UnitTest/Cloudstrap.BlazorServer.Tests/Infrastructure/BlazorServerTestHost.cs` *(create)* —
  mechanic (d), mirrors `MvcTestHost` (`Build`/`StartAsync`, in-memory config compose, TestServer).
- `src/Test/UnitTest/Cloudstrap.BlazorServer.Tests/Fixtures/App.razor`, `Routes.razor`,
  `StaticPage.razor`, `InteractivePage.razor`, `ThrowingPage.razor` *(create)* — mechanic (d).
- `src/Test/UnitTest/Cloudstrap.BlazorServer.Tests/CompositeBootTests.cs` *(create)*
- `src/Cloudstrap.sln` *(modify)* — package at the solution root, test project under `Test\UnitTest`.

**RED** *(write these tests first; for a brand-new project the honest first failure is the test
project failing to compile against missing types — the #5/#6/#7/#11 precedent — followed by real red
runs once the types exist)*:
- Unit test file: `CompositeBootTests.cs`
  - `AddCloudstrapBlazorServer_OnNullBuilder_ThrowsArgumentNullException` /
    `Use_OnNullApp_ThrowsArgumentNullException` (guard clauses).
  - `Composite_ServesARoutableComponent` — GET `/static-page` → 200, body contains the fixture
    marker (AC-BS1's "serves the root component").
  - `Composite_DefaultInteractivity_PrerendersTheInteractiveServerMarker` — GET `/interactive` →
    200, body contains `<!--Blazor:` (the server-render-mode marker — Interactive Server is wired
    end to end: `AddInteractiveServerComponents` + `AddInteractiveServerRenderMode`).
  - `Composite_ServesAnonymousProbes` — one healthy liveness + one failing readiness check (the
    Mvc `AddOneLiveOneFailingReadyCheck` shape): `/healthz` → 200, `/ready` → 503, both without
    authentication (AC-BS1's probe clause).
  - `Composite_EchoesACorrelationIdOnEveryResponse` — GET `/static-page` → the `X-Correlation-ID`
    response header is present and non-empty; sending one in → the same value echoes (AC-BS1's
    correlation clause; the #2 middleware is active after routing).
  - `Composite_StaticServerInteractivity_WiresNothingInteractive` — `Interactivity = StaticServer`:
    `/static-page` still 200 (rest of pipeline unchanged); `/interactive` fails with the framework's
    own error (5xx / thrown — the documented edge case, no package detection); and no descriptor from
    `AddInteractiveServerComponents` is registered (assert via a service the interactive wiring adds,
    e.g. the circuit services' presence in the default case and absence here) (AC-BS7 + the
    once-made-decision mechanic (a): the pipeline followed the Add-time choice with no Use-side knob).
  - `Use_CalledTwice_ThrowsInvalidOperationException` (message names `UseCloudstrapBlazorServer`).
  - `Use_WithoutAdd_ThrowsInvalidOperationExceptionNamingTheAddCall` — a bare
    `WebApplication.CreateBuilder().Build()` (mechanic (a)'s missing-state detection).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln   # RED = the new test project fails to compile against missing types
  src\Test\UnitTest\Cloudstrap.BlazorServer.Tests\bin\Debug\net10.0\Cloudstrap.BlazorServer.Tests.exe --filter "CompositeBootTests"
  ```

**GREEN**: the Scope items — minimal implementations passing these tests; Steps 2–3 pin the
hardening and composition details red-first. Full XML docs on every public member from the start
(the Mvc remarks style: the fixed order list, the correlation-before-auth rationale, the
no-forwarded-headers paragraph, the every-piece-independently-callable paragraph).

**DB changes**: none — this repository has no database.

**VERIFY** *(when all green, mark this step's `Done` checkbox and continue straight to the next step)*:
1. Test exe → all pass: two calls now give a Blazor Server app routable components with Interactive
   Server rendering, anonymous probes, correlated responses, an honored one-place interactivity
   decision, and loud double-`Use`/missing-`Add` failures — none of which existed before.
2. Full-suite check (mechanic (f)) — all green (the new exe joins the set); zero build warnings;
   `dotnet format` exit 0.
3. `dotnet build src/Cloudstrap.sln -c Release` → a `Cloudstrap.BlazorServer.*.nupkg` appears under
   `src/Cloudstrap.BlazorServer/bin/Release/`.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 2 — Every page response is hardened by default and every hardening default has an override: three set-if-absent security headers with the D-12 frame-options switch, a hardened antiforgery cookie with the configurator's final say, HSTS outside Development from configuration, and the exception-handling ladder (AC-BS2; AC-BS8's hardening half; D-2, D-11, D-12)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.BlazorServer/SecurityHeadersMiddleware.cs` *(modify — reads `EnableFrameOptions`
  from the bound options if Step 1 hard-coded it)*
- `src/Cloudstrap.BlazorServer/WebApplicationBuilderExtensions.cs` /
  `WebApplicationExtensions.cs` *(modify — only what Step 1's minimal implementation left
  hard-coded; the hardening behaviors are pinned red-first here)*
- `src/Test/UnitTest/Cloudstrap.BlazorServer.Tests/HardeningTests.cs` *(create)*

**RED** *(write these tests first, run them, confirm they fail)*:
- Unit test file: `HardeningTests.cs`
  - `SecurityHeaders_OnAPageResponse_CarryAllThreeDefaults` — GET `/static-page`:
    `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`,
    `X-Frame-Options: SAMEORIGIN` (AC-BS2, D-12).
  - `SecurityHeaders_SetByTheApplication_AreNeverOverwritten` — a `ConfigureEndpoints` endpoint
    pre-setting `X-Frame-Options: DENY` and `X-Content-Type-Options` → the consumer's values
    survive (the set-if-absent contract, spec edge case).
  - `SecurityHeaders_WithEnableFrameOptionsFalse_OmitOnlyTheFrameOptionsHeader` —
    `Cloudstrap:BlazorServer:EnableFrameOptions=false`: no `X-Frame-Options`; the other two still
    present (the D-12 switch — AC-BS8).
  - `Antiforgery_Defaults_AreHardened` — resolve `IOptions<AntiforgeryOptions>` from the built app:
    cookie `HttpOnly=true`, `SecurePolicy=Always`, `SameSite=Strict` (D-2).
  - `Antiforgery_ConfiguratorHook_HasTheFinalSay` — `configurator.Antiforgery = o =>
    o.Cookie.SecurePolicy = SameAsRequest` → the override wins over the hardened default (AC-BS8;
    the override ladder: hardened default → hook last).
  - `Antiforgery_Middleware_RejectsATokenlessFormPost` — a `ConfigureEndpoints` minimal-API form
    endpoint → tokenless POST is rejected 400 (the middleware sits after auth, before endpoints —
    the Mvc `Use_AntiforgeryMiddleware_RejectsATokenlessFormPost` shape).
  - `Hsts_InProductionOverHttps_EmitsTheConfiguredHeader` /
    `Hsts_InDevelopment_EmitsNothing` / `Hsts_WithEnabledFalse_EmitsNothing` /
    `Hsts_WithConfiguredValues_ReflectsThem` (`Cloudstrap:BlazorServer:Hsts:MaxAgeDays=30`,
    `IncludeSubDomains=false`, `Preload=true`) — the `EdgeHardeningTests` precedent, https client
    base address on `TestServer` (AC-BS2's HSTS clause; AC-BS8; D-11: standard `IsDevelopment()`
    gate, no LOC/DEV/TST taxonomy).
  - `ExceptionHandling_OutsideDevelopment_ReExecutesTheConfiguredErrorPath` — `/throws` with a
    minimal `/error` endpoint mapped via `ConfigureEndpoints` → 500 response carrying the error
    endpoint's marker body (`ExceptionHandlingPath` from `Cloudstrap:Application:ExceptionHandlerPath`,
    `CreateScopeForErrors=true` — mechanic (b)).
  - `ExceptionHandling_UseDeveloperExceptionPageFalseInDevelopment_KeepsTheHandler` — the `bool?`
    ladder overrides the environment default (AC-BS8; the `EnvironmentDefault` mechanic).
  - `Options_InvalidHstsMaxAge_FailsStartupNamingTheKey` — `Cloudstrap:BlazorServer:Hsts:MaxAgeDays=0`
    → `OptionsValidationException` at start naming the key (mechanic (e)); and
    `Options_AbsentSection_AllDefaultsApply` — no section, startup validation passes (spec edge case).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.BlazorServer.Tests\bin\Debug\net10.0\Cloudstrap.BlazorServer.Tests.exe --filter "HardeningTests"
  ```

**GREEN**: minimal — these are the Step 1 design's consequences pinned red-first; permitted
production changes are the frame-options switch plumbing, the antiforgery/HSTS/exception wiring
details, and the validator.

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: every page response is now hardened by default, and every hardening default
   (frame options, antiforgery, HSTS, exception page) has a proven override — observable behavior
   that did not exist before this step.
2. Full-suite check (mechanic (f)) — all green; `dotnet format` exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 3 — The pipeline composes around the consumer: auth middleware appears exactly when a scheme is registered (after routing, before antiforgery), the path base comes from configuration only, the four hooks run in the documented order, static-asset mapping and additional routable assemblies are overridable, and the component-endpoint/RazorComponents hooks carry the D-7 WASM escape hatch (AC-BS3; AC-BS8's composition half; D-3, D-4, D-5, D-6, D-7)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.BlazorServer/WebApplicationExtensions.cs` *(modify — the composition behaviors
  pinned red-first: placement markers, scheme-map conditional, hook invocation points,
  `AddAdditionalAssemblies`, `ConfigureComponentEndpoints` last on the convention builder)*
- `src/Test/UnitTest/Cloudstrap.BlazorServer.TestComponents/Cloudstrap.BlazorServer.TestComponents.csproj`
  *(create)* — mechanic (d): Razor class library, `IsPackable=false`, no `.Tests` suffix.
- `src/Test/UnitTest/Cloudstrap.BlazorServer.TestComponents/ExtraPage.razor` *(create)* —
  `@page "/extra"`, marker text.
- `src/Test/UnitTest/Cloudstrap.BlazorServer.Tests/Cloudstrap.BlazorServer.Tests.csproj` *(modify)* —
  add the `Cloudstrap.BlazorServer.TestComponents` ProjectReference.
- `src/Test/UnitTest/Cloudstrap.BlazorServer.Tests/PipelineCompositionTests.cs` *(create)*
- `src/Cloudstrap.sln` *(modify)* — the TestComponents RCL under `Test\UnitTest`.

**RED** *(write these tests first, run them, confirm they fail)*:
- Unit test file: `PipelineCompositionTests.cs`
  - `Use_WithoutAnyAuthenticationScheme_AddsNoAuthMiddlewareAndEverythingIsAnonymous` — no scheme:
    `/static-page` → 200, the scheme map is empty, nothing throws (AC-BS3 first half; the Mvc shape).
  - `Use_WithAConsumerCookieScheme_ChallengesThroughTheSchemeMapPredicate` — `AddAuthentication().
    AddCookie()` before the composite → a `[Authorize]`-protected fixture endpoint (via
    `ConfigureEndpoints` + `RequireAuthorization()`) redirects to `/Account/Login` (auth middleware
    present, placed by the composite — D-3).
  - `Use_WithCloudstrapOpenIdConnectRegistered_ChallengeRedirectsToTheSeededAuthority` — the real
    `AddCloudstrapOpenIdConnect` with pre-seeded metadata (the Mvc test's exact arrangement) →
    302 to `https://idp.example.com/connect/authorize` (AC-BS3 second half: the intended pairing
    works with zero package coupling).
  - `Use_Hooks_RunInTheDocumentedOrder` — `BeforeRouting` → `BeforeAuthorization` →
    `BeforeEndpoints` traced via `HttpContext.Items`, read back from a `ConfigureEndpoints`
    endpoint (the Mvc shape; also proves all four hooks fire — AC-BS8).
  - `Use_WithConfiguredPathBase_ServesUnderItAndNeverWithout` — `Cloudstrap:Application:PathBase`
    set → `/contoso/static-page` 200 and the bare app 404s under the prefix (D-4: configuration
    only, no env-var sniffing).
  - `Use_AdditionalAssemblies_MakeASecondAssemblysPagesRoutable` — without: `/extra` → 404; with
    `pipeline.AdditionalAssemblies.Add(typeof(ExtraPage).Assembly)`: `/extra` → 200 (the
    NihdiControls replacement, both directions).
  - `Use_ConfigureComponentEndpoints_RunsLastOnTheConventionBuilder` — the hook adds endpoint
    metadata via `builder.Add(...)`; assert the metadata is present on the component endpoints
    (the D-7 escape-hatch seam is live; a consumer-referenced WASM render mode would attach here).
  - `Add_RazorComponentsHook_HasTheFinalSay` — `configurator.RazorComponents` observably runs
    against the same `IRazorComponentsBuilder` (e.g. configures `CircuitOptions`; assert via
    `IOptions<CircuitOptions>`) (AC-BS8).
  - `Use_MapStaticAssetsFalse_MapsNoStaticAssetEndpoints` — with the flag off, no static-asset
    endpoint data source is added (the default-on flag's override; default-on is exercised by the
    demo + E2E in Step 6, where a built asset manifest exists).
  - `BlazorServerAssembly_DeclaresNoCorrelationOrForwardedHeadersOrCorsWiring` — reflection guard:
    no type name contains `Correlation` (consumed from #2, never rebuilt — the Mvc precedent) and
    the composite registers no CORS/forwarded-headers services (D-5/spec behaviors table).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.BlazorServer.Tests\bin\Debug\net10.0\Cloudstrap.BlazorServer.Tests.exe --filter "PipelineCompositionTests"
  ```

**GREEN**: minimal — the placement-marker claims (`__AuthenticationMiddlewareSet` /
`__AuthorizationMiddlewareSet`), the scheme-map conditional, hook invocation points,
`AddAdditionalAssemblies` pass-through (duplicates passed through to framework semantics —
documented, not "fixed"), `ConfigureComponentEndpoints` invoked last.

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: the pipeline now composes around whatever the consumer registered — auth
   exactly when a scheme exists, hooks in order, path base from configuration, a second assembly's
   pages routable on request — none of it observable before this step.
2. Full-suite check (mechanic (f)) — all green; `dotnet format` exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 1: the composite surface is frozen *(covers Steps 1–3)*

*Executor: STOP here. Present the results of all covered steps and WAIT for user approval — do not start the next step.*

⚠️ **Risk areas at this gate**: **all-new public API** — the composite's eight public types
(`CloudstrapBlazorServerOptions`, `HstsSettings`, `ExceptionHandlingSettings`,
`CloudstrapBlazorServerConfigurator`, `BlazorInteractivity`, `BlazorServerPipelineOptions`,
`WebApplicationBuilderExtensions`, `WebApplicationExtensions`) against the spec's Public API Sketch
**verbatim** — any deviation needs naming (the two settings classes are the sketch's property types
made concrete, mechanic (e)) · **auth middleware placement** — the scheme-map conditional + placement
markers reviewed in code (`UseCloudstrapBlazorServer`), the D-3 mechanic mirrored from shipped Mvc ·
mechanic (a)'s `BlazorServerRegistrationState` internal (confirm: state-singleton over an
options-based alternative; `Use` without `Add` throws) · mechanic (d)'s fixture strategy (Razor-SDK
test project + the `TestComponents` RCL as the honest `AdditionalAssemblies` boundary; the
`MapStaticAssets` default-on left to the Step 6 demo/E2E where a real asset manifest exists —
confirm) · the D-12 `X-Frame-Options: SAMEORIGIN` default observed live in the header tests.

- [ ] Behavioral verification: test exe output shows — the two-call boot with Interactive Server
  prerender markers, anonymous `/healthz`+`/ready`, correlation echo, the honored `StaticServer`
  decision, double-`Use`/missing-`Add` throws (Step 1); the three set-if-absent headers with the
  frame-options switch and never-overwrite proof, hardened antiforgery + configurator final say,
  the four HSTS behaviors, the exception ladder, startup validation naming the key (Step 2);
  no-scheme/cookie-scheme/OIDC-pairing auth placement, hook order, path base both ways, the
  additional-assembly boundary both ways, the component-endpoint and RazorComponents hooks, the
  static-assets flag, and the no-correlation/no-CORS/no-forwarded-headers guard (Step 3).
- [ ] Code review: pipeline order in `UseCloudstrapBlazorServer` against mechanic (b) / the spec's
  Redesign row, line by line; `sealed`/static on every public type, single namespace, full XML docs
  (fixed-order list, correlation-before-auth rationale, no-forwarded-headers paragraph);
  `Cloudstrap.BlazorServer.csproj` → exactly one ProjectReference + the FrameworkReference, zero
  PackageReferences.
- [ ] User approved — implementation may continue past this gate

---

## Slice 2 — Circuit interactions become visible traces (D-9), and the package is publishable and permanently guarded

---

## Step 4 — A circuit event handler wrapped in `IBlazorInteractionTrace.StartInteraction(name)` produces an exported root span detached from the (dropped) hub trace, children and the ambient correlation id follow it, everything restores on dispose, it is a safe no-op without a listener — and the activity source reaches any DI-built tracer pipeline additively, with the package creating no pipeline and no exporter of its own (AC-BS5's unit halves; AC-BS6; D-9; mechanic (c))

- [ ] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.BlazorServer/BlazorServerActivitySources.cs` *(create)* — static constants class,
  `public const string Interaction = "Cloudstrap.BlazorServer.Interaction";` (the
  `CloudstrapActivitySources` pattern; ⚠️ planner-added public type — this gate).
- `src/Cloudstrap.BlazorServer/IBlazorInteractionTrace.cs` *(create)* — the spec sketch verbatim:
  one method, `IDisposable StartInteraction(string interactionName)`; XML docs carry the detach/
  restore contract, the no-listener no-op, and the undisposed-scope edge case (ends with the
  circuit's async context — documented, never throws).
- `src/Cloudstrap.BlazorServer/BlazorInteractionTrace.cs` *(create)* — mechanic (c); internal sealed,
  also `IDisposable` (disposes the owned `ActivitySource` with the container — the `BusinessTrace`
  shape).
- `src/Cloudstrap.BlazorServer/BlazorInteractionScope.cs` *(create)* — internal sealed disposable:
  restores `Activity.Current` and the previous correlation id, disposes the activity, tolerates
  double dispose, never throws.
- `src/Cloudstrap.BlazorServer/WebApplicationBuilderExtensions.cs` *(modify)* —
  `TryAddSingleton<IBlazorInteractionTrace, BlazorInteractionTrace>()` +
  `services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddSource(
  BlazorServerActivitySources.Interaction))` (mechanic (c); the transitive-API note from the
  Overview's dependency-closure section applies — zero new package entries).
- `src/Test/UnitTest/Cloudstrap.BlazorServer.Tests/Cloudstrap.BlazorServer.Tests.csproj` *(modify)*
  — add version-less `OpenTelemetry.Extensions.Hosting` + `OpenTelemetry.Exporter.InMemory`
  (both already pinned in `src/Directory.Packages.props`).
- `src/Test/UnitTest/Cloudstrap.BlazorServer.Tests/BlazorInteractionTraceTests.cs` *(create)*

**RED** *(write these tests first, run them, confirm they fail)*:
- Unit test file: `BlazorInteractionTraceTests.cs` *(an `ActivityListener` subscribed to the
  interaction source arranges the recorded cases; the `BusinessTraceTests` precedent)*
  - `StartInteraction_UnderAnAmbientActivity_StartsANewRootDetachedFromIt` — with a fake ambient
    hub activity current: the interaction activity has a **different trace id** and no parent
    (AC-BS5's detach clause — the whole point of D-9 given `BlazorHubSampler` drops the hub trace).
  - `StartInteraction_PointsTheAmbientCorrelationIdAtTheInteractionTraceId` — the
    `ICorrelationContextAccessor` value equals the interaction activity's trace id while the scope
    is open (AC-BS5's correlation clause — the outbound header follows via #2's
    `CorrelationHttpDelegatingHandler`, cited not re-tested).
  - `StartInteraction_AChildStartedInsideTheScope_ParentsUnderTheInteractionRoot` — a child
    activity started within the scope carries the interaction's trace id (the outbound-dependency
    parenting clause, unit-level).
  - `Dispose_RestoresThePreviousActivityAndCorrelationId` — after dispose, `Activity.Current` is
    the previous ambient activity and the accessor carries the previous value; double dispose does
    not throw (the stack-safe restore edge case).
  - `StartInteraction_WithNoListener_IsASafeNoOpThatStillSetsAFreshCorrelationId` — no listener:
    no activity is created, nothing throws, and the accessor carries a fresh non-empty identifier
    (the spec's no-listener edge case — the outbound header stays stable).
  - `StartInteraction_WithBlankName_ThrowsArgumentException` (guard clause).
  - `AddCloudstrapBlazorServer_RegistersTheInteractionTraceSingleton` — one
    `IBlazorInteractionTrace` descriptor, singleton, `TryAdd` semantics (a consumer's registration
    wins).
  - `AddCloudstrapBlazorServer_CreatesNoOpenTelemetryPipelineOfItsOwn` — after the composite alone
    on a plain builder, no OpenTelemetry hosted service and no exporter registrations exist
    (AC-BS6's no-second-pipeline/no-exporter clause, structurally).
  - `InteractionSource_IsContributedAdditivelyToAHostOwnedPipeline` — a host-owned pipeline
    (`services.AddOpenTelemetry().WithTracing(t => t.AddInMemoryExporter(exported))` — the
    Aspire-ServiceDefaults-style arrangement) + the composite's registrations: a span started via
    `StartInteraction` lands in the in-memory exporter as a **root** span from
    `Cloudstrap.BlazorServer.Interaction`, exactly once (AC-BS6 behaviorally: contributed, not
    duplicated).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.BlazorServer.Tests\bin\Debug\net10.0\Cloudstrap.BlazorServer.Tests.exe --filter "BlazorInteractionTraceTests"
  ```

**GREEN**: the Scope items — mechanic (c) exactly; no static `ActivityListener`, no forced
`AllData` sampling, no DI-scope creation, no generic overloads (the D-9 defect list stays dead).

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: circuit-style work wrapped in one call now produces a detached, exported,
   correlated root trace with full restore semantics and honest no-op behavior — and the source
   reaches a host-owned pipeline additively while the package itself registers no pipeline.
2. Full-suite check (mechanic (f)) — all green; `dotnet format` exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 5 — The package is publishable and guarded forever: metadata, README (quick start, options/hooks tables, data-protection and WASM-hook recipes, migration notes D-1…D-13), permanent tripwires on the closure and the dropped concepts, the forbidden-identifier sweep — and the `blazor.md` BlazorServer drift closed (AC-BS9; AC-ASP2; AC-ASP3's guard; AC-A3; D-13's doc half; hand-off constraint 5)

- [ ] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.BlazorServer/Cloudstrap.BlazorServer.csproj` *(modify)* — `<Description>` (Blazor
  Server bootstrap for ASP.NET Core: one registration call and one fixed-order pipeline call —
  hardened antiforgery and security headers, HSTS, correlation, anonymous health probes, conditional
  auth placement, Interactive Server or static SSR decided once, and an interaction trace scope that
  makes circuit-originated work visible; pairs with Cloudstrap's OIDC login and typed HttpClients),
  `<PackageTags>$(PackageTags);blazor;blazorserver;pipeline;security;tracing;aspnetcore</PackageTags>`,
  `<PackageReadmeFile>README.md</PackageReadmeFile>` + `<None Include="README.md" Pack="true" PackagePath="/" />`.
- `src/Cloudstrap.BlazorServer/README.md` *(create)* — quick start (the Overview's consumer
  composition); the `Cloudstrap:BlazorServer` options table (Hsts values ·
  `ExceptionHandling:UseDeveloperExceptionPage` · `EnableFrameOptions`, D-12) and the configurator/
  pipeline-hook tables (every convention's override — the spec's Behaviors & Conventions table
  re-expressed for consumers); the pipeline order list; the interaction-trace section
  (`StartInteraction` usage in a circuit event handler, the no-listener semantics, the published
  `BlazorServerActivitySources.Interaction` constant for pipeline owners, AC-BS6's additive
  posture); recipes: multi-instance data protection needs `AddCloudstrapDataProtection` (D-8),
  richer security headers via the NetEscapades bundle in `BeforeRouting` (spec behaviors table),
  Interactive WebAssembly via `Configurator.RazorComponents` + `ConfigureComponentEndpoints` with a
  consumer package reference (D-7), forwarded headers via the platform env var (D-5); migration
  notes vs the source (D-1 probes → `/healthz`+`/ready` · D-2 hardened antiforgery · D-3 scheme-map
  auth gate · D-4 path base from configuration · D-5/D-6/D-7/D-8 removals · D-9
  `IDistributedTraceService` → `IBlazorInteractionTrace` · D-10 obsolete methods gone · D-12 frame
  options · D-13 no BlazorCommon reference).
- `src/Test/UnitTest/Cloudstrap.BlazorServer.Tests/PackageSurfaceTests.cs` *(create)* — permanent
  guards mirroring `Cloudstrap.Mvc.Tests/PackageSurfaceTests.cs`, adapted.
- `.claude/instructions/blazor.md` *(modify — docs; human-reviewed at Gate 2; hand-off constraint 5)*
  — **BlazorServer content only**: the project-roles row → "Hardened Blazor Server composite
  (`AddCloudstrapBlazorServer`/`UseCloudstrapBlazorServer<TRoot>`) + `IBlazorInteractionTrace` —
  depends on `Cloudstrap.Extensions` only; **no `Cloudstrap.BlazorCommon` reference (D-13 — demo-level
  adoption only)**"; the BlazorServer section rewritten to the shipped truth (the two entry points,
  the `Interactivity`-once mechanic, the D-9 one-method scope replacing `IDistributedTraceService`
  and its 1–5 generic overloads, typed clients = #4's `AddCloudstrapHttpServiceClient` with **no**
  auto-added `ActivitySourceDelegatingHandler`); the stale `Add<Feature>ForCloudstrap()` naming line
  scoped down so it no longer claims the BlazorServer surface; the drift note updated to point at
  #13 (BlazorWasm) only. No other section edited.

**RED** *(the guard tests are tripwires against already-correct code and may pass immediately — the
honest failing state is in the artifacts: before GREEN the Release nupkg has no README/description/
tags and `blazor.md` still describes the dropped `IDistributedTraceService` surface; recorded per the
#2…#11 precedent)*:
- Unit test file: `PackageSurfaceTests.cs`
  - `ReferencedAssemblies_OfBlazorServerAssembly_MatchTheApprovedClosure` — every referenced
    assembly starts with `System` or `Microsoft.` or `OpenTelemetry.Api` (the mechanic (c)
    transitive usage, + facades as observed) or is exactly `Cloudstrap.Core` /
    `Cloudstrap.Observability` / `Cloudstrap.Extensions`; explicitly **zero** names starting
    `Aspire` (AC-ASP2), `Nihdi` (AC-A3), `NWebsec`, `NetEscapades`, `Duende`, `Scrutor`,
    `MudBlazor`, and **not** `Cloudstrap.BlazorCommon` (D-13 made permanent).
  - `PublicTypes_OfBlazorServerAssembly_ContainNoForbiddenIdentifiers` — no public type/member
    matches `(?i)nihdi|riziv|dynatrace|nservicebus`.
  - `PublicSurface_IsExactlyTheTenApprovedTypes` — exported types are exactly the eight Gate-1
    types + `IBlazorInteractionTrace` + `BlazorServerActivitySources`, all in namespace
    `Cloudstrap.BlazorServer`; every public class sealed or static; `IBlazorInteractionTrace` is
    the only public interface.
  - `BlazorServerAssembly_DeclaresNoDroppedConcepts` — the Port-Decision drops made permanent: no
    declared type name contains `DistributedTrace`, `Controls`, `SecurityHardening`, or
    `DelegatingHandler` (case-insensitive — no automatic handler may ever return, D-9), and no
    public method name contains `HttpServiceClient` (no BlazorServer typed-client wrapper —
    AC-ASP3/AC-BS4's no-second-way guard, hand-off constraint 1).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.BlazorServer.Tests\bin\Debug\net10.0\Cloudstrap.BlazorServer.Tests.exe --filter "PackageSurfaceTests"
  ```

**GREEN**: add the csproj metadata, write `README.md`, edit `blazor.md` per Scope.

**DB changes**: none.

**VERIFY**:
1. Test exe (full run) → all tests pass, including the four permanent guards.
2. `dotnet build src/Cloudstrap.sln -c Release` →
   `src/Cloudstrap.BlazorServer/bin/Release/Cloudstrap.BlazorServer.<version>.nupkg`; expand a
   `.zip` copy → contains `README.md`, `icon.png`, `lib/net10.0/Cloudstrap.BlazorServer.dll` **and**
   `.xml`; the nuspec shows the MIT license expression, description, tags, repository URL, and a
   dependency list of exactly **`Cloudstrap.Extensions`** — no `Aspire.*`, no `OpenTelemetry.*`
   direct entry, no `Cloudstrap.BlazorCommon` (AC-BS9, AC-ASP2, D-13, the Overview's
   dependency-closure note).
3. **AC-BS9 identifier sweep** (new package + tests):
   ```powershell
   Get-ChildItem -Recurse -File -Path src/Cloudstrap.BlazorServer, src/Test/UnitTest/Cloudstrap.BlazorServer.Tests, src/Test/UnitTest/Cloudstrap.BlazorServer.TestComponents |
     Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
     Select-String -Pattern '(?i)(nihdi|riziv|probe\.aspx)'
   ```
   → zero matches (guard-test tripwire patterns are self-referential and excluded by reading the
   hits, as in plans 2–11).
4. Doc check: `.claude/instructions/blazor.md` BlazorServer content matches the shipped surface
   (no `IDistributedTraceService`, no auto-handler claim, the D-13 reference note); the drift note
   now names #13 only; no other section changed.
5. Full-suite check (mechanic (f)) — all green; `dotnet format` exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 2: D-9 shipped, package publishable *(covers Steps 4–5)*

*Executor: STOP here. Present the results of all covered steps and WAIT for user approval — do not start the next step.*

⚠️ **Risk areas at this gate**: **public API completion** — `IBlazorInteractionTrace` verbatim
against the spec sketch, and the planner-added `BlazorServerActivitySources` constants class
(confirm, or fold the constant elsewhere) · **the transitive OpenTelemetry API usage** (mechanic (c):
`ConfigureOpenTelemetryTracerProvider` reached through the `Cloudstrap.Extensions` closure, no direct
PackageReference, nupkg dependency list stays exactly `Cloudstrap.Extensions` — confirm this posture
over adding a pin) · **the packaging check** — the expanded Release nupkg contents and dependency
list · **the instruction-file edit** (`.claude/instructions/blazor.md` — reviewed verbatim,
BlazorServer content only).

- [ ] Behavioral verification: test exe output shows — the detached root span under a fake hub
  activity, correlation id = interaction trace id with child parenting, full restore + double
  dispose, the no-listener no-op with a stable correlation id, the singleton registration, the
  no-pipeline/no-exporter proof, and the in-memory-exported root span through a host-owned pipeline
  (Step 4); the four permanent surface guards green, the expanded Release nupkg reviewed, the
  identifier sweep empty (Step 5).
- [ ] Code review: `BlazorInteractionTrace`/`BlazorInteractionScope` against mechanic (c) — no
  static `ActivityListener`, no forced sampling, no DI scopes, no generic overloads;
  `Cloudstrap.BlazorServer/README.md` matches as-built behavior; `blazor.md` diff.
- [ ] User approved — implementation may continue past this gate

---

## Slice 3 — Demonstrated live: the demo app runs on the composite, and the browser proves probes, hardened headers, the user-token round trip and the interaction trace through the real three-host topology

---

## Step 6 — The `Cloudstrap.Demo.BlazorServer` app is rewritten onto `AddCloudstrapBlazorServer` + `UseCloudstrapBlazorServer<App>` (resolving #27's D-B placeholder): OIDC login and the `DemoApi` user-token client stay green through the composite pipeline, the WhoAmI page runs on a convention-registered `IViewModel` (demo-level `AddCloudstrapBlazorCommon`, D-13) wrapping its call in `StartInteraction`, and new E2E tests prove probes, hardened headers and the live interaction span (AC-BS1/2/4/5 live; AC-BS10; demonstration slice — workflow rule 9) ⚠️ DEMO AUTH-SURFACE RISK AREA

- [ ] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/demo/BlazorServer/Cloudstrap.Demo.BlazorServer.csproj` *(modify)* — add
  `<ProjectReference>` → `..\..\Cloudstrap.BlazorServer\Cloudstrap.BlazorServer.csproj` and
  `..\..\Cloudstrap.BlazorCommon\Cloudstrap.BlazorCommon.csproj` (the D-13 demo-level reference);
  drop the now-transitive `Cloudstrap.Core` + `Cloudstrap.Extensions` references (flow through
  BlazorServer); keep `Cloudstrap.Observability` (bootstrap logger + `UseCloudstrapObservability`),
  `Cloudstrap.Authentication.OpenIdConnect`, `Cloudstrap.Demo.Contracts`.
- `src/demo/BlazorServer/Program.cs` *(modify)* — the demonstration's headline rewrite (the
  Overview's target composition, with teaching comments): `builder.AddCloudstrapBlazorServer();`
  replaces the manual `AddRazorComponents/AddInteractiveServerComponents/
  AddCascadingAuthenticationState` block; `builder.Services.AddCloudstrapBlazorCommon<IWhoAmIViewModel>();`
  (#11's convention scan over this assembly); `app.UseCloudstrapBlazorServer<App>(pipeline =>
  pipeline.ConfigureEndpoints = endpoints => endpoints.MapCloudstrapAuthenticationEndpoints());`
  replaces the manual `UseAuthentication/UseAuthorization/UseAntiforgery/MapStaticAssets/
  MapRazorComponents` block (the explicit login/logout routes win over the component catch-all by
  endpoint-routing specificity — noted in a comment). `AddCloudstrapOpenIdConnect` and the
  `DemoApi` typed-client registration stay exactly as they are (AC-BS4's no-new-API posture).
- `src/demo/BlazorServer/ViewModels/IWhoAmIViewModel.cs` *(create)* — `public interface
  IWhoAmIViewModel : IViewModel` exposing `string UserName`, `DownstreamWhoAmIDto? WhoAmI`.
- `src/demo/BlazorServer/ViewModels/WhoAmIViewModel.cs` *(create)* — `public sealed class
  WhoAmIViewModel : IWhoAmIViewModel`, constructor-injecting `IDemoApiClient`,
  `IBlazorInteractionTrace` and `AuthenticationStateProvider`; `InitializeAsync(ct)` resolves the
  user name, then fetches the API echo **inside** `using (_ = interactionTrace.StartInteraction(
  "whoami"))` (AC-BS5 live: the root span, with the dependency call parented under it, exports
  through the app's console exporter — `EnableConsole` defaults `true` in Otlp mode).
  Convention-registered by name (`*ViewModel`) — no explicit registration line.
- `src/demo/BlazorServer/Components/Pages/WhoAmI.razor` *(modify)* — thins to framework concerns:
  `@inject IWhoAmIViewModel ViewModel`; `OnInitializedAsync` awaits `ViewModel.InitializeAsync()`;
  bindings move to `ViewModel.*`; **stays statically server-rendered** (the request's `HttpContext`
  carries the cookie session the user-token handler reads — the shipped #10 posture); existing
  `data-testid` attributes unchanged (the pre-existing E2E contract).
- `src/demo/BlazorServer/Components/_Imports.razor` *(modify)* — `@using Cloudstrap.BlazorCommon`
  (+ the ViewModels namespace).
- `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/BlazorServerTests.cs` *(modify)* — two new tests (below);
  the pre-existing `BlazorServer_SignInAndWhoAmI_RendersUserAndApiEcho_NoConsoleErrors` untouched;
  `WaitUntilReadyAsync` may switch its poll to `/healthz` (the composite now serves it — executor
  latitude, noted at the gate if taken).
- `src/demo/BlazorServer/README.md` *(modify)* — the intro paragraph rewritten (stock app → the #12
  composite, D-B placeholder resolved); feature-matrix rows for #12: the composite
  (`AddCloudstrapBlazorServer()` + `UseCloudstrapBlazorServer<App>()` | the new probes/headers E2E
  test), the interaction trace + ViewModel (`IBlazorInteractionTrace.StartInteraction("whoami")` +
  `AddCloudstrapBlazorCommon<IWhoAmIViewModel>()` | the new interaction-span E2E test); harness
  notes updated — the "Interactive-circuit token plumbing is exactly what #12 adds" sentence
  replaced with the shipped truth (D-9 ships interaction *tracing*; the page stays SSR so the
  user-token handler keeps its `HttpContext`), and a note that `/healthz`+`/ready` and the hardened
  headers now come from the composite.

**RED** *(write these tests first, run them, confirm they fail — today the app maps no probes, emits
no security headers, and exports no interaction span, so neither assertion can pass)*:
- E2E test file: `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/BlazorServerTests.cs`
  - `BlazorServer_CompositePipeline_ServesAnonymousProbesAndHardenedHeaders` — plain `HttpClient`
    against 5340, no sign-in: `/healthz` → 200 and `/ready` → 200 (anonymous — AC-BS1 live); a GET
    of `/` carries `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`,
    `X-Frame-Options: SAMEORIGIN` (D-12) and a non-empty `X-Correlation-ID` (AC-BS2 + AC-BS10's
    hardened-headers clause).
  - `BlazorServer_WhoAmI_EmitsAnInteractionRootSpanThroughTheCompositePipeline` — sign in via
    `BrowserSignIn` (the existing test's flow), land on `/whoami`, assert the `user-name` and
    `api-host` testids still render (the ViewModel wiring carries the page — AC-BS4 live through
    the composite), then poll `_blazorServerHost.CapturedOutput` (the `DoctorsTests` precedent)
    until it contains `Cloudstrap.BlazorServer.Interaction` (the console exporter printed the D-9
    root span — AC-BS5 live).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\E2E\Cloudstrap.Demo.E2E.Tests\bin\Debug\net10.0\Cloudstrap.Demo.E2E.Tests.exe --filter "BlazorServer_CompositePipeline_ServesAnonymousProbesAndHardenedHeaders|BlazorServer_WhoAmI_EmitsAnInteractionRootSpanThroughTheCompositePipeline"
  ```

**GREEN**: the Scope items. **The pre-existing E2E test must stay green unchanged** — the rewrite
preserves the app's observable contract (the auto-triggered OIDC challenge, the sign-in landing,
the `user-name`/`api-host`/`api-subject` testids, the user-token round trip to the Api host): that
is AC-BS10's carried-tests proof, now flowing through `UseCloudstrapBlazorServer<App>`'s auth
placement. *(If the existing test is disturbed, the executor reports it at the gate rather than
weakening any assertion.)*

**DB changes**: none.

**VERIFY**:
1. E2E exe → the two new tests pass **and every pre-existing E2E test passes unchanged** (build
   first; one-time `playwright.ps1 install chromium` if needed) — in particular
   `BlazorServer_SignInAndWhoAmI_RendersUserAndApiEcho_NoConsoleErrors`, whose green run now proves
   the composite's conditional auth placement and the untouched #4 typed client (AC-BS4).
2. Manual smoke (optional but recorded): run IdP + Api + BlazorServer per the README, browse
   `/whoami`, sign in → page renders; `curl http://127.0.0.1:5340/healthz` → 200 with the three
   headers on `/`.
3. Full-suite check (mechanic (f)) — all green; `dotnet format` exit 0; the demo project still
   packs nothing (`IsPackable=false` inherited).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — final: deliverable #12 complete *(covers Step 6; closes the deliverable)*

*Executor: STOP here. Present the results and WAIT for user approval. Any Git push afterwards requires
the user's explicit go-ahead (CLAUDE.md: no push without confirmation).*

⚠️ **Risk areas at this gate**: **the demo auth surface** — `Program.cs` no longer hand-places
`UseAuthentication`/`UseAuthorization`; the composite's scheme-map placement carries the live OIDC
login and the user-token round trip (the untouched pre-existing E2E test is the tripwire) · **the
demo restructure** — no shared-contract change (`Cloudstrap.Demo.Contracts` untouched), no IdP
change; the BlazorCommon reference is demo-level only (D-13).

- [ ] Behavioral verification: the two new E2E tests pass
  (`BlazorServer_CompositePipeline_ServesAnonymousProbesAndHardenedHeaders`,
  `BlazorServer_WhoAmI_EmitsAnInteractionRootSpanThroughTheCompositePipeline`) and **all
  pre-existing E2E tests pass unchanged**; the full-suite check (build + 12 unit exes + E2E exe +
  `dotnet format --verify-no-changes`) is green end to end.
- [ ] Spec acceptance sign-off: walk **AC-BS1…AC-BS10 + AC-ASP2 + AC-ASP3 + AC-A3** against the step
  evidence using the Overview's AC coverage map — all met; confirm nothing from the spec's Drop /
  Out-of-Scope lists was resurrected (no `NihdiControls`/assembly registry, no
  `IDistributedTraceService`/`DistributedTrace`/`UseDistributedTrace<T1..T5>`/static
  `ActivityListener`, no `ActivitySourceDelegatingHandler` or any automatic tracing handler, no
  BlazorServer typed-HttpClient API, no `SecurityHardeningOptions`, no NWebsec/NetEscapades, no
  controllers/`RequireAuthorization`, no WASM reflection, no KeyVault-DP/localization/Scalar
  auto-wiring, no obsolete methods, no forwarded headers/HTTPS redirection/CORS wiring, no
  `BlazorHubSampler` duplication, zero `Aspire.*`, zero `Nihdi.*`) and every De-NIHDI row is closed
  (`AddBlazorForNihdi`/`UseBlazorForNihdi` → the Cloudstrap names, `/probe`+`/probe.aspx` →
  `/healthz`+`/ready`, no `basepath`/`WorkloadName` sniffing, no company headers).
- [ ] Docs review: `src/Cloudstrap.BlazorServer/README.md` matches as-built behavior;
  `src/demo/BlazorServer/README.md` intro/matrix/harness notes cite the real E2E test names and no
  longer promise circuit token plumbing; `.claude/instructions/blazor.md` BlazorServer content is
  the shipped truth with the drift note scoped to #13. **User-owned follow-up (not in this plan)**:
  none identified — the founding-spec package map already lists `Cloudstrap.BlazorServer` as
  shipped-shape; flag any wording drift noticed during review.
- [ ] User approved — deliverable #12 done; project-manager flips the ROADMAP row to ✅.
