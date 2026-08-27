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
| **BlazorWasm** | browser-based cookie auth, XSRF, Refit HTTP clients | Minimal (no project refs) |

## BlazorWasm — Browser Cookie Authentication

- Auth is **cookie-based** using `CookieHandler` which auto-attaches browser cookies and XSRF tokens.
- XSRF token (`X-XSRF-TOKEN`) is sent on mutating HTTP methods (POST, PUT, DELETE, PATCH) via `AntiforgeryTokenStore`.
- Entry point: `WebAssemblyHostBuilderExtensions.AddBlazorWasmForCloudstrap()`.
- HTTP clients: `AddCloudstrapWasmHttpClient<TClient>()` returns `IHttpClientBuilder` for Refit chaining.
- Default Refit settings: `System.Text.Json` with `CamelCase` naming policy.

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

> ⚠️ Drift note: the BlazorWasm section of this file still describes the source-repo surface until
> deliverable #13 ships. The BlazorServer and BlazorCommon sections are the shipped truth.

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

- BlazorWasm extension methods still follow the source repo's `Add<Feature>ForCloudstrap()` naming
  until #13 ships; shipped packages use `AddCloudstrap<Feature>` / `UseCloudstrap<Feature>`.
- HTTP client registration returns `IHttpClientBuilder` for Refit chaining.
- No cross-references between BlazorWasm and BlazorServer — they are independent hosting models.
