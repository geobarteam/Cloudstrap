---
applyTo: "src/Cloudstrap.Blazor*/**"
description: "Blazor conventions for BlazorWasm, BlazorServer, and BlazorCommon projects. Covers browser-auth patterns, HTTP client registration, distributed tracing, Scrutor scanning, and shared abstractions."
---

# Blazor Project Conventions

## Project Roles

| Project | Role | Dependencies |
|---------|------|--------------|
| **BlazorCommon** | Shared contracts (`IErrorHandler`, `IViewModel`) + convention scan — Scrutor only | Scrutor only |
| **BlazorServer** | SSR helpers, distributed tracing, typed HTTP clients | `Common` |
| **BlazorWasm** | browser-based cookie auth, XSRF, Refit HTTP clients | Minimal (no project refs) |

## BlazorWasm — Browser Cookie Authentication

- Auth is **cookie-based** using `CookieHandler` which auto-attaches browser cookies and XSRF tokens.
- XSRF token (`X-XSRF-TOKEN`) is sent on mutating HTTP methods (POST, PUT, DELETE, PATCH) via `AntiforgeryTokenStore`.
- Entry point: `WebAssemblyHostBuilderExtensions.AddBlazorWasmForCloudstrap()`.
- HTTP clients: `AddCloudstrapWasmHttpClient<TClient>()` returns `IHttpClientBuilder` for Refit chaining.
- Default Refit settings: `System.Text.Json` with `CamelCase` naming policy.

## BlazorServer — Distributed Tracing & HTTP Clients

- Entry point: `AddBlazorForCloudstrap()` / `UseBlazorForCloudstrap<TRootComponent>()`.
- `IDistributedTraceService` is **scoped** (per-request). Has overloads for 1–5 generic type parameters.
- `AddCloudstrapHttpServiceClient<TInterface, TImplementation>()` auto-adds `ActivitySourceDelegatingHandler` for trace propagation.
- Depends on `Cloudstrap.Common` for shared infrastructure.

## BlazorCommon — Shared Abstractions

> ⚠️ Drift note: the BlazorServer/BlazorWasm sections of this file still describe the source-repo
> surface until deliverables #12/#13 ship. This BlazorCommon section is the shipped truth.

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

- Extension methods follow `Add<Feature>ForCloudstrap()` or `Use<Feature>ForCloudstrap()` naming.
- HTTP client registration returns `IHttpClientBuilder` for Refit chaining.
- No cross-references between BlazorWasm and BlazorServer — they are independent hosting models.
