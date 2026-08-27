# Cloudstrap.Demo.BlazorServer

A Blazor Server app on **http://127.0.0.1:5340** running on the **#12 composite**
(`Cloudstrap.BlazorServer`): two calls bootstrap the hardened pipeline — probes, security headers,
correlation, hardened antiforgery, conditional auth placement — plus OIDC login at the shared demo
IdP, a user-token typed client into the Api demo host, Otlp-mode observability, and the D-9
interaction trace around the WhoAmI API call.

## Feature matrix

| Feature | The Cloudstrap call | Proven by (E2E) |
|---|---|---|
| The Blazor Server composite: fixed hardened pipeline, anonymous `/healthz`+`/ready`, security headers, correlation (#12) | `AddCloudstrapBlazorServer()` + `UseCloudstrapBlazorServer<App>()` | `BlazorServer_CompositePipeline_ServesAnonymousProbesAndHardenedHeaders` |
| The interaction trace + ViewModel convention: the WhoAmI call exports as a root span of its own (#12 D-9 + #11) | `IBlazorInteractionTrace.StartInteraction("whoami")` inside `WhoAmIViewModel`, registered by `AddCloudstrapBlazorCommon<IWhoAmIViewModel>()` | `BlazorServer_WhoAmI_EmitsAnInteractionRootSpanThroughTheCompositePipeline` |
| Interactive OIDC login on server-rendered Blazor — auto-triggered challenge, hardened cookie session (#10) | `AddCloudstrapOpenIdConnect()` + `MapCloudstrapAuthenticationEndpoints()` (via the composite's `ConfigureEndpoints` hook) | `BlazorServer_SignInAndWhoAmI_RendersUserAndApiEcho_NoConsoleErrors` |
| The signed-in user's token transparently reaches the Api demo host (#4 + #9/#10 plumbing, unchanged by #12) | `AddCloudstrapHttpServiceClient<IDemoApiClient, DemoApiClient>("DemoApi")` with `AddUserAccessToken: true` | the same test — the page renders the Api host's `demo-api` marker |

## Harness notes

- **Otlp mode** (`Cloudstrap:OpenTelemetry:Mode: Otlp`, endpoint `http://localhost:4317`):
  telemetry exports when a collector listens on the conventional localhost endpoint — **no
  collector is required to boot**; without one the exporter retries in the background.
- `/whoami` is `[Authorize]` and **statically server-rendered on purpose**: the request's
  `HttpContext` carries the cookie session, so the user-token handler works with zero extra
  plumbing. #12 ships interaction *tracing* (`IBlazorInteractionTrace`), not circuit token
  plumbing — the page stays SSR so the user-token handler keeps its `HttpContext`.
- `/healthz` + `/ready` and the hardened security headers now come from the composite —
  `UseCloudstrapBlazorServer<App>` maps and emits them; nothing is hand-rolled here.
- Anonymous visits to protected pages auto-trigger the challenge via the `RedirectToLogin`
  component (full-load navigation to `/account/login?returnUrl=…`); the home page stays anonymous
  so the app boots gracefully without peers.
- The IdP client is `demo-blazorserver` (placeholder secret, token audience `demo-api` only — this
  app hosts no API of its own).

## Running

```powershell
dotnet run --project src/demo/Shared/IdentityProvider    # first — sign-in needs the IdP
dotnet run --project src/demo/Api                        # the peer /whoami calls
dotnet run --project src/demo/BlazorServer
```

The app boots alone (home page anonymous); `/whoami` fails loudly naming the authority until the
IdP listens, and its API echo fails naming the peer until the Api host listens.
