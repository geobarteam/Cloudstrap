---
applyTo: "src/Cloudstrap.Blazor*/**"
description: "Blazor conventions for BlazorWasm, BlazorServer, and BlazorCommon projects. Covers browser-auth patterns, HTTP client registration, distributed tracing, Scrutor scanning, and shared abstractions."
---

# Blazor Project Conventions

## Project Roles

| Project | Role | Dependencies |
|---------|------|--------------|
| **BlazorCommon** | Shared interfaces (`IErrorHandler`, `INavigationService`, `IViewModel`) | Scrutor only |
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

- **Interfaces only** — no heavy implementations.
- `AddPresentationServices<TAssemblyMarker>()` uses **Scrutor** to auto-register classes ending in `ViewModel` or `Service` as their interfaces (transient).
- `IErrorHandler`: consumers implement (e.g., MudBlazor toasts). Methods: `HandleError`, `ShowError`, `ShowWarning`, `ShowSuccess`.
- `IViewModel.InitializeAsync()`: async component initialization pattern.

## Patterns

- Extension methods follow `Add<Feature>ForCloudstrap()` or `Use<Feature>ForCloudstrap()` naming.
- HTTP client registration returns `IHttpClientBuilder` for Refit chaining.
- No cross-references between BlazorWasm and BlazorServer — they are independent hosting models.
