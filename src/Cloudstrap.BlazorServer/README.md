# Cloudstrap.BlazorServer

Blazor Server bootstrap for ASP.NET Core, in two calls: `AddCloudstrapBlazorServer()` registers the
service side — razor components with the interactivity decided once, hardened antiforgery, HSTS,
correlation, health checks — and `UseCloudstrapBlazorServer<App>()` builds the request pipeline in
the fixed order a hardened, observable Blazor Server application needs. Circuit-originated work
becomes visible through `IBlazorInteractionTrace`, a one-method scope that starts a fresh root trace
per user interaction.

The package depends on `Cloudstrap.Extensions` only (bringing `Cloudstrap.Core` and
`Cloudstrap.Observability` transitively). It registers **no authentication** and **no observability
pipeline** — both are separate, deliberately visible calls.

## Quick start

```csharp
using Cloudstrap.BlazorServer;

var builder = WebApplication.CreateBuilder(args);

builder.UseCloudstrapObservability();                 // separate, visible call
builder.AddCloudstrapBlazorServer();                  // core, correlation, probes, razor components,
                                                      // hardened antiforgery, HSTS, IBlazorInteractionTrace
builder.Services.AddCloudstrapOpenIdConnect();        // auth pairing is a separate, visible call
builder.Services.AddCloudstrapHttpServiceClient<IDemoApiClient, DemoApiClient>("DemoApi");

WebApplication app = builder.Build();
app.UseCloudstrapBlazorServer<App>(pipeline =>
    pipeline.ConfigureEndpoints = endpoints => endpoints.MapCloudstrapAuthenticationEndpoints());
await app.RunAsync();
```

## The pipeline order

`UseCloudstrapBlazorServer<TRoot>` builds this order, and the order is the point of the call:

1. Error handling head — the developer exception page where selected, otherwise `UseExceptionHandler`
   re-executing `Cloudstrap:Application:ExceptionHandlerPath` with a fresh scope
2. HSTS — when enabled and outside `Development`
3. Security headers (set-if-absent)
4. `UsePathBase` — when `Cloudstrap:Application:PathBase` is configured
5. `BeforeRouting` hook
6. Routing
7. Correlation — before auth, so a `401` is as traceable as a `200`
8. Authentication — only when a scheme is registered
9. `BeforeAuthorization` hook
10. Authorization — under the same condition
11. Antiforgery
12. `BeforeEndpoints` hook
13. Static-asset endpoints — when `MapStaticAssets` is on (default)
14. Razor component endpoints for `TRoot` — Interactive Server render mode when selected,
    `AdditionalAssemblies`, then `ConfigureComponentEndpoints` last
15. Health probes (`/healthz`, `/ready`, anonymous)
16. `ConfigureEndpoints` hook

A second `Use` call throws; a `Use` call without the matching `Add` throws, naming the missing call.

## Settings — `Cloudstrap:BlazorServer`

The section is optional; every default applies without it. Validated at startup, naming the
offending key.

| Key | Default | Meaning |
|-----|---------|---------|
| `Hsts:Enabled` | `true` | Emit `Strict-Transport-Security` outside `Development` |
| `Hsts:MaxAgeDays` | `365` | HSTS max age (must be ≥ 1) |
| `Hsts:IncludeSubDomains` | `true` | HSTS `includeSubDomains` directive |
| `Hsts:Preload` | `false` | HSTS `preload` directive |
| `ExceptionHandling:UseDeveloperExceptionPage` | environment | `true`/`false` overrides the `Development` default |
| `EnableFrameOptions` | `true` | Emit `X-Frame-Options: SAMEORIGIN` (set `false` if the app must be framed) |

## Code-level overrides

`AddCloudstrapBlazorServer(configurator => ...)`:

| Hook | Default | Meaning |
|------|---------|---------|
| `Interactivity` | `InteractiveServer` | `StaticServer` skips all interactive wiring — decided once, here; the pipeline call follows it |
| `Antiforgery` | hardened cookie (`HttpOnly`, `SecurePolicy=Always`, `SameSite=Strict`) | Runs last over the hardened defaults — final say |
| `RazorComponents` | — | Runs last against the `IRazorComponentsBuilder` (e.g. `CircuitOptions`, adding a WASM render mode) |

`UseCloudstrapBlazorServer<TRoot>(pipeline => ...)`:

| Hook | Default | Meaning |
|------|---------|---------|
| `MapStaticAssets` | `true` | Map the built static-asset endpoints |
| `AdditionalAssemblies` | empty | Extra assemblies whose routable components join the router |
| `BeforeRouting` / `BeforeAuthorization` / `BeforeEndpoints` | — | Middleware insertion points, in the order named |
| `ConfigureComponentEndpoints` | — | Runs last on the component convention builder |
| `ConfigureEndpoints` | — | Your own endpoints (auth endpoints, minimal APIs), mapped after the probes |

## Security headers

Set-if-absent on every response — a header the application already set is never overwritten:

- `X-Content-Type-Options: nosniff`
- `Referrer-Policy: no-referrer`
- `X-Frame-Options: SAMEORIGIN` — omitted when `EnableFrameOptions` is `false`

## Interaction tracing — `IBlazorInteractionTrace`

Cloudstrap's observability package drops the noisy SignalR hub trace a circuit runs under — so work
started in a circuit event handler would vanish with it. Wrap the interaction instead:

```csharp
public sealed class CheckoutViewModel(IDemoApiClient api, IBlazorInteractionTrace trace)
{
    public async Task SubmitAsync()
    {
        using (trace.StartInteraction("checkout"))
        {
            await api.SubmitOrderAsync();   // parented under the interaction root,
        }                                    // correlated with its trace id
    }
}
```

`StartInteraction` starts a **root** activity detached from the ambient hub trace, points the ambient
correlation identifier at the new trace id (outbound HTTP calls carry it), and restores both on
dispose. Without a trace listener it is a safe no-op that still sets a fresh correlation identifier.

The activity source, `BlazorServerActivitySources.Interaction`
(`"Cloudstrap.BlazorServer.Interaction"`), is contributed additively to any OpenTelemetry pipeline
built from the application's service collection — Cloudstrap's owner or contribute mode, or an
Aspire-style host's own. The package creates no pipeline and no exporter of its own; a pipeline
owner outside DI can `AddSource` the published constant themselves.

## Recipes

- **Multi-instance deployments** need shared data-protection keys (antiforgery and auth cookies):
  call `AddCloudstrapDataProtection` from `Cloudstrap.Extensions`.
- **Richer security headers** (CSP and friends): add the `NetEscapades.AspNetCore.SecurityHeaders`
  middleware in `BeforeRouting` — the package's own headers never overwrite yours.
- **Interactive WebAssembly render modes**: reference the WASM server package yourself, add the
  services via `configurator.RazorComponents`, and the render mode via
  `ConfigureComponentEndpoints` — the composite stays Server-only by design.
- **Behind a proxy**: forwarded headers are deliberately not configured here — set the platform's
  `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` or place the middleware in `BeforeRouting`.

## Aspire coexistence

No `Aspire.*` reference. Health checks register on the stock `IHealthChecksBuilder`, the interaction
source contributes to an existing DI-built tracer pipeline without duplicating exporters, and typed
HttpClients come from `Cloudstrap.Extensions` unchanged — tolerant of resilience handlers applied
via `ConfigureHttpClientDefaults`.

## Migrating from the enterprise predecessor

- `AddBlazorForNihdi` / `UseBlazorForNihdi` → `AddCloudstrapBlazorServer` /
  `UseCloudstrapBlazorServer<TRoot>`; the render-mode knob exists once, at registration time.
- `/probe` and `/probe.aspx` → `/healthz` + `/ready` (anonymous, additive on the stock builder).
- Antiforgery cookie is hardened by default; the configurator hook has the final say.
- Auth middleware appears exactly when a scheme is registered — after routing, before antiforgery;
  no forced `RequireAuthorization`, no built-in controllers.
- Path base comes from `Cloudstrap:Application:PathBase` only — no environment-variable sniffing.
- Forwarded headers, HTTPS redirection and CORS wiring are gone — platform conventions instead.
- WASM reflection probing is gone — reference and wire WASM render modes explicitly.
- KeyVault data protection, localization and Scalar auto-wiring are separate, visible calls.
- `IDistributedTraceService` and its 1–5 generic overloads → `IBlazorInteractionTrace`, one method;
  the automatic `ActivitySourceDelegatingHandler` is gone — outbound tracing belongs to the
  OpenTelemetry HTTP instrumentation.
- Obsolete legacy methods were not carried over.
- `X-Frame-Options: SAMEORIGIN` is emitted by default — `EnableFrameOptions=false` turns it off.
- No `Cloudstrap.BlazorCommon` dependency — adopt the ViewModel convention at application level.

## License

MIT — part of the [Cloudstrap](https://github.com/geobarteam/Cloudstrap) suite.
