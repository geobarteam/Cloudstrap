---
applyTo: "src/Cloudstrap.Blazor*/**"
description: "Blazor conventions for BlazorWasm, BlazorServer, and BlazorCommon projects. Covers browser-auth patterns, HTTP client registration, distributed tracing, Scrutor scanning, and shared abstractions."
---

# Blazor Project Conventions

## Project Roles

| Project | Role | Dependencies |
|---------|------|--------------|
| **BlazorCommon** | Shared contracts (`IErrorHandler`, `IViewModel`) + convention scan — Scrutor only | Scrutor only |
| **BlazorServer** | Hardened Blazor Server composite (`AddCloudstrapBlazorServer`/`UseCloudstrapBlazorServer<TRoot>`) + `IBlazorInteractionTrace` | `Cloudstrap.Extensions` only; **no `Cloudstrap.BlazorCommon` reference (D-13 — demo-level adoption only)** |
| **BlazorWasm** | BFF client composite (`AddCloudstrapBlazorWasm`) + cookie/XSRF pipeline + one-line typed/Refit clients | Four NuGet packages (`Components.WebAssembly`, `Components.Authorization`, `Extensions.Http`, `Refit`), no project refs; **no `Cloudstrap.BlazorCommon` reference (DL-1 — demo-level adoption only)** |

## BlazorWasm — Browser Cookie Authentication

- Composite entry point: `AddCloudstrapBlazorWasm(Action<CloudstrapBlazorWasmOptions>?)` on
  `WebAssemblyHostBuilder` — registers the shared `IAntiforgeryTokenStore`, the `CookieHandler`
  (browser credentials always; the **configured** XSRF header attached on POST/PUT/DELETE/PATCH —
  one option drives capture and attachment, D-3), the named BFF auth client, the BFF-driven
  `AuthenticationStateProvider` (+ `IBffAuthenticationStateProvider.ClearAuthenticationState()`
  refresh seam), `AddAuthorizationCore` and `AddCascadingAuthenticationState`. No localization, no
  login/logout machinery (full-page navigations to the #10 `/account/*` endpoints).
- Settings bind from `Cloudstrap:BlazorWasm` (`UserEndpointPath`=`bff/user`,
  `XsrfHeaderName`=`X-XSRF-TOKEN`, `AuthHttpClientName`); the delegate wins; **no secrets** — the
  section is publicly downloadable in `wwwroot/appsettings.json`.
- Clients: `AddCloudstrapWasmHttpClient<TClient>(baseAddress)` and
  `AddCloudstrapWasmRefitClient<TClient>(baseAddress, RefitSettings?)` — both return
  `IHttpClientBuilder`, both ride the cookie+XSRF pipeline. Refit registers via `RestService.For`
  (never `Refit.HttpClientFactory` — .NET 10 WASM `MissingMethodException`). Default serialization:
  System.Text.Json, camelCase, case-insensitive.
- Server pairing (DL-2): the Bff maps `MapCloudstrapBffUserEndpoint()` from
  `Cloudstrap.Authentication.OpenIdConnect` — the wire contract + XSRF issuance; the consumer's
  `AddAntiforgery(o => o.HeaderName = ...)` must match `XsrfHeaderName`, and mutating endpoints
  validate with stock antiforgery.

## BlazorServer — Composite Pipeline & Interaction Tracing

- Two entry points: `AddCloudstrapBlazorServer(Action<CloudstrapBlazorServerConfigurator>?)` on the
  builder and `UseCloudstrapBlazorServer<TRootComponent>(Action<BlazorServerPipelineOptions>?)` on
  the app. The pipeline order is fixed; every hook (`BeforeRouting`, `BeforeAuthorization`,
  `BeforeEndpoints`, `ConfigureComponentEndpoints`, `ConfigureEndpoints`) is an insertion point.
- **Interactivity is decided once**, at registration time (`Configurator.Interactivity`:
  `InteractiveServer` default, or `StaticServer`); the `Use` call follows it — there is no
  render-mode knob on the pipeline options. `Use` without `Add` throws.
- Registers **no authentication** (pair with `AddCloudstrapOpenIdConnect` or your own scheme —
  middleware appears exactly when a scheme is registered) and **no observability pipeline**
  (`UseCloudstrapObservability` is a separate call).
- `IBlazorInteractionTrace` — a **singleton** with one method, `StartInteraction(name)`: starts a
  detached root span per circuit interaction (the D-9 replacement for `IDistributedTraceService`
  and its 1–5 generic overloads), points the ambient correlation id at it, restores on dispose.
  Source constant: `BlazorServerActivitySources.Interaction`, contributed additively to any
  DI-built tracer pipeline.
- Typed HTTP clients are #4's `AddCloudstrapHttpServiceClient<TInterface, TImplementation>()`
  unchanged — there is **no** BlazorServer client API and **no** auto-added
  `ActivitySourceDelegatingHandler`; outbound tracing belongs to the OTel HTTP instrumentation.

## BlazorCommon — Shared Abstractions

- **Contracts + one entry point** — the package's whole public surface is four types (`IViewModel`,
  `IErrorHandler`, `BlazorCommonOptions`, `ServiceCollectionExtensions`); no Blazor package
  reference, no `Cloudstrap:` configuration section.
- `AddCloudstrapBlazorCommon<TAssemblyMarker>(Action<BlazorCommonOptions>?)` uses **Scrutor** to
  register public concrete classes ending in `ViewModel` or `Service` as their implemented
  interfaces (transient). Three overridable knobs: `ConventionSuffixes`, `Lifetime`,
  `AdditionalAssemblies`. Escape hatch: call `services.Scan(...)` directly — Scrutor is a public
  dependency.
- `IErrorHandler`: consumers implement and register it themselves (e.g., MudBlazor snackbar);
  Cloudstrap ships no implementation. Methods: `HandleError(Exception)`, `ShowError(string)` only.
- `IViewModel.InitializeAsync(CancellationToken cancellationToken = default)`: async page
  initialization pattern; implementations own cancellation.
- **No navigation abstraction** — pages inject `NavigationManager` directly (D-3).

## Patterns

- Extension methods follow `AddCloudstrap<Feature>` / `UseCloudstrap<Feature>` naming.
- HTTP client registration returns `IHttpClientBuilder` for Refit chaining.
- No cross-references between BlazorWasm and BlazorServer — they are independent hosting models.
