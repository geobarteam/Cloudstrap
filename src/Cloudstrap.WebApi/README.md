# Cloudstrap.WebApi

API versioning, per-version OpenAPI documents with a Scalar reference UI, RFC 9457 problem-details error
handling, correlation, health probes, security headers, HSTS, CORS and optional hardened JWT bearer
validation — two calls and one `Cloudstrap:` subsection each.

> **Runtime requirement**: this package carries a `Microsoft.AspNetCore.App` framework reference. Every
> consumer requires the ASP.NET Core shared framework at run time — `mcr.microsoft.com/dotnet/aspnet` base
> images work; `mcr.microsoft.com/dotnet/runtime`-only base images are **not** supported.

## Quick start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddCloudstrapKeyVault();          // first: secrets take part in everything bound below
builder.UseCloudstrapObservability();
builder.AddCloudstrapWebApi();
builder.AddCloudstrapJwtBearer();         // optional — omit for an anonymous API

var app = builder.Build();
app.UseCloudstrapWebApi();
app.Run();
```

```json
{
  "Cloudstrap": {
    "Application": { "SystemName": "Contoso", "SubsystemName": "Catalog", "SubsystemType": "Api" },
    "WebApi": {
      "ApiVersioning": { "DefaultVersion": "1.0" },
      "Cors": { "AllowedOrigins": [ "https://app.contoso.example" ] }
    },
    "OpenApi": { "Title": "Contoso Catalog" },
    "JwtBearer": {
      "Authority": "https://idp.contoso.example/",
      "Audience": "contoso-catalog-api"
    }
  }
}
```

`AddCloudstrapWebApi` reads its configuration when it is called, so call it **after** any configuration
source that supplies those values — `AddCloudstrapKeyVault` first, as above.

## The middleware order

`UseCloudstrapWebApi` exists to encode this order. It is the same in every environment, and each hook point
names the slot it occupies:

1. **exception handler** — unhandled exceptions become problem details, always
2. **HSTS** — outside `Development`, when enabled
3. **security headers** — `nosniff` and `no-referrer`, before routing so short-circuited probes carry them
4. **path base** — when `Cloudstrap:Application:PathBase` is set
5. `hooks.BeforeRouting` — static files, a SPA framework's files, anything that short-circuits early
6. **routing**
7. **CORS** — only when origins are configured, before correlation so a preflight is never rejected for
   carrying no correlation header
8. **correlation** — after routing so endpoint metadata is visible, before authentication so a rejected
   request is still traceable
9. **authentication** — only when an authentication scheme is registered
10. `hooks.BeforeAuthorization`
11. **authorization** — same condition as authentication
12. `hooks.BeforeEndpoints`
13. **controllers** (unless `MapControllers = false`), **health probes**, **OpenAPI documents + Scalar UI**
14. `hooks.ConfigureEndpoints` — minimal APIs, hubs, a SPA fallback

`UseCloudstrapWebApi` may be called once; a second call throws. The service-side registrations are
repeat-safe.

### Composing a SPA or BFF host around it

```csharp
app.UseCloudstrapWebApi(pipeline =>
{
    pipeline.BeforeRouting = branch => branch.UseBlazorFrameworkFiles().UseStaticFiles();
    pipeline.ConfigureEndpoints = endpoints => endpoints.MapFallbackToFile("index.html");
});
```

The API, the probes, the static files and the SPA fallback all stay reachable.

## Settings

### `Cloudstrap:WebApi`

| Key | Default | Meaning |
|---|---|---|
| `ApiVersioning:DefaultVersion` | `1.0` | Version assumed for unversioned requests and unattributed controllers |
| `ApiVersioning:AssumeDefaultVersionWhenUnspecified` | `true` | Serve rather than reject a request naming no version |
| `ApiVersioning:ReportApiVersions` | `true` | Emit `api-supported-versions` / `api-deprecated-versions` |
| `Json:IgnoreNullValues` | `true` | Omit `null` properties from responses |
| `LowercaseUrls` | `true` | Lowercase generated URLs and query strings |
| `Cors:AllowedOrigins` | *(empty)* | Empty ⇒ **no CORS policy at all**; `https://*.example.com` matches subdomains |
| `Hsts:Enabled` · `MaxAgeDays` · `IncludeSubDomains` · `Preload` | `true` · `365` · `true` · `false` | Emitted outside `Development`, over HTTPS |
| `ExceptionHandling:IncludeDetails` | *(unset)* | Unset ⇒ detail in `Development` only |

### `Cloudstrap:OpenApi`

| Key | Default | Meaning |
|---|---|---|
| `Enabled` | `true` | Publish `/openapi/v{n}.json`, one document per discovered version |
| `Title` · `Description` | *(derived)* | Default to neutral values built from `Cloudstrap:Application` |
| `OAuth:TokenUrl` · `AuthorizationUrl` · `Scopes` | *(unset)* | The documented flow. **No URL is ever derived from the authority** |

### `Cloudstrap:Scalar`

| Key | Default | Meaning |
|---|---|---|
| `Enabled` | *(unset)* | Unset ⇒ mapped in `Development` only; explicit `true` exposes it anywhere |
| `Path` | `/scalar` | Where the reference UI is served |
| `OAuth:ClientId` · `SelectedScopes` | *(unset)* | Public client sign-in. **There is deliberately no client-secret setting** |

### `Cloudstrap:JwtBearer`

| Key | Default | Meaning |
|---|---|---|
| `Authority` | *(required)* | The identity provider that issued the tokens this API accepts |
| `Audience` | *(required)* | This API's identifier. Audience validation is not switchable |
| `RequireHttpsMetadata` | *(unset)* | Unset ⇒ required everywhere except `Development` |
| `ClockSkewSeconds` | `60` | Tighter than the framework's 300 |
| `MapInboundClaims` | `false` | Keep the claim names the token used — `sub` stays `sub` |
| `RequireAuthenticatedEndpoints` | `true` | Install a require-authenticated fallback policy |

### Owned elsewhere, never redefined here

`Cloudstrap:Application` (`PathBase`, and the workload identity the OpenAPI metadata is derived from),
`Cloudstrap:HealthChecks` and `Cloudstrap:Correlation` belong to `Cloudstrap.Core`, `Cloudstrap.Extensions`
and `Cloudstrap.Observability`. This package consumes them.

> **Collection settings append.** `Cors:AllowedOrigins`, `OpenApi:OAuth:Scopes` and
> `Scalar:OAuth:SelectedScopes` are get-only initialized, so configured values are *added to* the default
> rather than replacing it. All three ship empty, so this only matters if you set defaults in code.

## Security posture

- **Authentication is code, not configuration.** `AddCloudstrapJwtBearer` is a call you can see in
  `Program.cs`; there is no `EnableAuthentication` flag a deployment mistake can switch off.
- **Four defaults are stricter than the framework's**, each with its own override key: audience validation
  on (`Audience`) · clock skew 60 s (`ClockSkewSeconds`) · HTTPS metadata outside `Development`
  (`RequireHttpsMetadata`) · no inbound claim remapping (`MapInboundClaims`).
- **Endpoints are authenticated by default** once the bearer is registered. Two opt-outs:
  `[AllowAnonymous]` for one endpoint, `RequireAuthenticatedEndpoints = false` for the application.
- **Health probes and the API documentation are mapped anonymously by design.** An orchestrator holds no
  token, and a reference UI that challenges before you can enter one is a dead page. Set
  `Cloudstrap:OpenApi:Enabled = false` for a production API whose description is not public.
- **No client secret can reach a browser.** `Cloudstrap:Scalar:OAuth` has no secret property; the reference
  UI signs in with the authorization code flow and PKCE.
- **Never enable `Cloudstrap:WebApi:ExceptionHandling:IncludeDetails` on a public production API.** Stack
  traces and exception messages describe internal structure; they belong in the log.
- **Rejected tokens are diagnosed through the handler's own log category**,
  `Microsoft.AspNetCore.Authentication.JwtBearer`, at `Debug`/`Information`. This package logs no token
  contents and acquires no tokens — obtaining tokens to call other services is a different package.
- A consumer who builds their own pipeline instead of calling `UseCloudstrapWebApi` must place
  `UseAuthentication` and `UseAuthorization` themselves, after routing.

## Error responses

Unhandled exceptions become `application/problem+json` in every environment, and are logged server-side
exactly once. The payload carries `title`, `status` and a `correlationId` a caller can quote — the one they
sent, or the one Cloudstrap generated. With `IncludeDetails` resolved true it also carries the exception
type, message, stack trace and an inner-exception chain bounded to five levels.

Successful responses carry the same identifier in the `X-Correlation-ID` response header (see
`Cloudstrap.Observability`). The error path deliberately relies on the payload instead: the framework's
exception handler clears response headers before writing.

Register your own `IExceptionHandler` **before** `AddCloudstrapWebApi` and it gets the first attempt;
Cloudstrap's remains the terminal fallback.

## Escape hatches

| Instead of | Do this |
|---|---|
| Cloudstrap's single CORS policy | Stock `AddCors` / `RequireCors`; Cloudstrap's policy is additive |
| Cloudstrap's probe endpoints | Stock `MapHealthChecks`, or `Cloudstrap:HealthChecks:Enabled = false` |
| The two security headers | Your own middleware via `hooks.BeforeRouting`; the values are constants |
| Controllers | `MapControllers = false` and map minimal APIs via `hooks.ConfigureEndpoints` |
| Versioned minimal APIs | `Asp.Versioning.Http` directly — Cloudstrap does not wrap it |
| The whole pipeline | Don't call `UseCloudstrapWebApi`; every piece stays independently callable |

## Aspire coexistence

This package references no `Aspire.*` package. Health checks are registered through the stock
`IHealthChecksBuilder` and mapped idempotently, so an Aspire ServiceDefaults host and Cloudstrap add to the
same set. Observability modes come from `Cloudstrap.Observability`, typed clients from
`Cloudstrap.Extensions`. Versioning, OpenAPI and the reference UI do not overlap ServiceDefaults at all.

## Migrating from the enterprise predecessor

Deliberately gone: NSwag (the built-in generator plus `Asp.Versioning.OpenApi` and Scalar replace it, and
there is no `Cloudstrap:Swagger` section) · `?api-version=v1` normalization (use `1.0`; the versioning hook
restores a custom reader in one line) · custom enum-keyed-dictionary JSON (stock `System.Text.Json` handles
it) · path-base magic (no `basepath` environment variable, no workload-name default — the path base applies
only when configured) · HSTS `preload` by default (a domain-owner commitment, not a library's) · the
`AllowAnyOrigin` CORS fallback (no origins now means no policy).
