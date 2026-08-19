# Cloudstrap.Mvc

Server-rendered MVC bootstrap for ASP.NET Core: controllers + views, hardened session state on stock
`Microsoft.AspNetCore.Session`, content-negotiated error handling — an error page for browsers,
RFC 9457 problem details for JSON clients — correlation, health probes, security headers, HSTS and
CORS. Two calls and one `Cloudstrap:Mvc` section.

Zero package dependencies: three `Cloudstrap.*` project references and the shared
`Microsoft.AspNetCore.App` framework reference — nothing else.

## Quick start

```csharp
using Cloudstrap.Mvc;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddCloudstrapMvc();

WebApplication app = builder.Build();

app.UseCloudstrapMvc();

await app.RunAsync();
```

Under ten lines: the conventional default route (`{controller=Home}/{action=Index}/{id?}`) and every
attribute route answer, `wwwroot` is served, `/healthz` and `/ready` probe, every request carries an
ambient correlation id, session state is on and hardened, unhandled exceptions produce the right shape
for every caller, and every response carries the constant security headers.

Supply a minimal error action (no default page ships — see *Error handling*):

```csharp
[Route("/error")]
public sealed class ErrorController : Controller
{
    [HttpGet]
    public IActionResult Get() => View();   // a neutral apology page, no exception content
}
```

## The middleware order

The order is the point of the pipeline call, and it is fixed:

1. The error handling head — the developer exception page where selected, otherwise the exception
   handler: Cloudstrap's negotiating handler answers JSON-preferring callers terminally, and
   HTML-preferring callers get the consumer's page re-executed at
   `Cloudstrap:Application:ExceptionHandlerPath` (default `/error`).
2. HSTS — outside `Development`, when enabled.
3. The security-header middleware (`nosniff`, `no-referrer`).
4. The path base — when `Cloudstrap:Application:PathBase` is set.
5. Static files — when `UseStaticFiles` is on (default).
6. **`BeforeRouting` hook** — the slot for a security-headers bundle, rewrites, or forwarded headers.
7. Routing.
8. CORS — only when origins are configured.
9. Correlation (from `Cloudstrap.Observability`).
10. Authentication — only when a scheme is registered.
11. **`BeforeAuthorization` hook** — the slot for anything that must see the principal first.
12. Authorization — under the same condition as authentication.
13. Session — when `Cloudstrap:Mvc:Session:Enabled` is on (default).
14. Antiforgery.
15. **`BeforeEndpoints` hook**.
16. The conventional default route, attribute routes included — when `MapDefaultControllerRoute` is
    on (default).
17. Health probes (from `Cloudstrap.Extensions`).
18. **`ConfigureEndpoints` hook** — the slot for minimal APIs, Razor Pages, or asset endpoints.

Every constituent piece stays independently callable (`UseStaticFiles`, `UseSession`,
`UseCloudstrapCorrelation`, `MapCloudstrapHealthChecks`) — a consumer who needs a different order
simply does not call `UseCloudstrapMvc`.

## Settings

### `Cloudstrap:Mvc:Session`

| Key | Default | Meaning |
|---|---|---|
| `Enabled` | `true` | Session registered and wired at all. `false` removes it entirely. |
| `CookieName` | `.Cloudstrap.Session` | The session cookie name. |
| `CookieSecurePolicy` | `Always` | `Always` / `SameAsRequest` / `None`. |
| `IdleTimeoutMinutes` | `20` | Idle minutes before an untouched session is abandoned. Must be > 0. |
| `IsEssential` | `false` | Exempts the cookie from cookie-consent gating. |

### `Cloudstrap:Mvc:ExceptionHandling`

| Key | Default | Meaning |
|---|---|---|
| `IncludeDetails` | *(unset)* → `Development` only | Exception type/message/stack/inner chain in the **JSON** payload. Never applies to the HTML page. |
| `UseDeveloperExceptionPage` | *(unset)* → `Development` only | The framework's developer page instead of the Cloudstrap error contract. |

### `Cloudstrap:Mvc:Hsts`

| Key | Default | Meaning |
|---|---|---|
| `Enabled` | `true` | Emit `Strict-Transport-Security` outside `Development`. |
| `MaxAgeDays` | `365` | The advertised `max-age`. Must be > 0 while enabled. |
| `IncludeSubDomains` | `true` | Include subdomains in the policy. |
| `Preload` | `false` | The `preload` token — a domain-owner commitment, never a library default. |

### `Cloudstrap:Mvc:Cors`

| Key | Default | Meaning |
|---|---|---|
| `AllowedOrigins` | *(empty)* | Origins allowed cross-origin, credentialed; `https://*.contoso.example` allows wildcard subdomains. **Empty means no CORS is registered at all.** |

### Owned elsewhere, never redefined here

| Section | Owner | Consumed as |
|---|---|---|
| `Cloudstrap:Application` (`PathBase`, `ExceptionHandlerPath`) | `Cloudstrap.Core` | The path base slot; the error re-execution path (default `/error`). |
| `Cloudstrap:Correlation` | `Cloudstrap.Observability` | The correlation middleware and header. |
| `Cloudstrap:HealthChecks` | `Cloudstrap.Extensions` | `/healthz` + `/ready` mapping. |

**Configuration ordering**: registration-time decisions (session on/off, HSTS/CORS registration) read
`builder.Configuration` eagerly inside `AddCloudstrapMvc`. Configuration sources added *after* the
call — `AddCloudstrapKeyVault`, for example — do not affect them: call KeyVault first.

## Session posture

Session state is on by default and hardened by default, flowing entirely through stock
`Microsoft.AspNetCore.Session` — this package ships **zero** session middleware, store or
cookie-protection code, and the cookie stays compatible with the stock one (same DataProtection
purpose). The delta is startup options only:

- `.Cloudstrap.Session`, **`Secure` always**, `HttpOnly` (stock), `SameSite=Lax` (stock),
  path-base-scoped path, `IsEssential=false`, 20-minute idle timeout.
- The override ladder: hardened defaults → `Cloudstrap:Mvc:Session` → the
  `CloudstrapMvcConfigurator.Session` hook (full `SessionOptions` access, runs last, final say).
- **Plain HTTP**: with `Secure` always on, a browser will not return the cookie over plain HTTP on a
  non-loopback origin. Develop over HTTPS, or set `CookieSecurePolicy: SameAsRequest` explicitly for
  local HTTP — the default is never silently downgraded. (Loopback — `http://127.0.0.1` — is a
  trustworthy origin; browsers accept the `Secure` cookie there.)
- **`IsEssential` and cookie consent**: with a consent feature active (`Cloudstrap.CookieConsent`,
  deliverable #21), the session cookie is withheld until the visitor consents unless you set
  `IsEssential: true` — do that only when session state is genuinely essential.

### Multi-instance deployments

The default backing store is the framework's in-memory cache — single-instance only. For more than
one instance:

1. Register a distributed `IDistributedCache` (Redis, SQL Server) **before** `AddCloudstrapMvc` —
   the consumer's registration always wins; the in-memory fallback never displaces it.
2. Call `AddCloudstrapDataProtection` (from `Cloudstrap.Extensions`, deliverable #4) so all instances
   share the key ring that protects the session cookie.

Single-instance apps need nothing.

## Error handling

The contract is content-negotiated. A request is **HTML-preferring** exactly when its `Accept` header
contains the `text/html` or `application/xhtml+xml` media type — browsers always send `text/html` on
navigation. Everything else — `application/json`, `*/*` alone, an absent or unparsable `Accept`
header — is **JSON-preferring**.

- **HTML-preferring** → the consumer's own error page, re-executed at
  `Cloudstrap:Application:ExceptionHandlerPath` (default `/error`). No default page ships — supply
  the minimal action from the quick start. The page never carries exception content, whatever
  `IncludeDetails` says.
- **JSON-preferring** → `500 application/problem+json` with a generic title and the ambient
  `correlationId`; with `IncludeDetails` resolved true, also the exception type, message, stack trace
  and a depth-5-bounded inner-exception chain. **Never enable details on a public production
  application.**
- The failure is logged server-side exactly once on either branch.
- An `IExceptionHandler` you register **before** `AddCloudstrapMvc` gets the first attempt;
  Cloudstrap's handler is the terminal fallback.

## Security headers

Two constant headers on every response, never overwriting a value the application set:
`X-Content-Type-Options: nosniff` and `Referrer-Policy: no-referrer`. There is deliberately no default
CSP or `X-Frame-Options` — wrong defaults break real applications. For the full HTML bundle, use the
`NetEscapades.AspNetCore.SecurityHeaders` package through the hook:

```csharp
app.UseCloudstrapMvc(pipeline => pipeline.BeforeRouting = branch =>
    branch.UseSecurityHeaders(policies => policies.AddDefaultSecurityHeaders()));
```

## Recipes

- **`MapStaticAssets` adopters**: set `UseStaticFiles = false` and map the asset endpoints in
  `ConfigureEndpoints`.
- **Forwarded headers** (behind a proxy): set the platform's `ASPNETCORE_FORWARDEDHEADERS_ENABLED`
  environment variable, or place `UseForwardedHeaders` yourself in `BeforeRouting` — never a silent
  library default.

## Authentication pairing

`AddCloudstrapMvc` registers **no** authentication scheme. Pair it with `AddCloudstrapOpenIdConnect`
(deliverable #10) or any scheme of your own: whenever a scheme is registered, `UseCloudstrapMvc`
places the authentication and authorization middleware after routing; with none, every endpoint is
anonymous. There is no forced `RequireAuthorization()` — endpoint protection belongs to the auth
package's fallback policy or your own `[Authorize]` attributes.

## Not with `AddCloudstrapWebApi`

`UseCloudstrapMvc` and `UseCloudstrapWebApi` are both pipeline **owners** — a host must not call
both. A host serving MVC pages *and* a versioned API composes the granular pieces around one owner
instead: pick the composite that matches the host's primary surface and place the other surface's
pieces through the hooks.

## Aspire coexistence

Health checks are registered additively on the stock `IHealthChecksBuilder`, so an Aspire
ServiceDefaults host's checks and Cloudstrap's land in the same set; correlation and probes route
through the already-composable `Cloudstrap.Observability`/`Cloudstrap.Extensions` seams. The package
references zero `Aspire.*` assemblies.

## Framework reference

This package carries `<FrameworkReference Include="Microsoft.AspNetCore.App" />`: it is for server
applications built on the ASP.NET Core shared framework — the runtime image must include ASP.NET Core
(`mcr.microsoft.com/dotnet/aspnet`), not the bare .NET runtime.

## Migrating from the enterprise predecessor

1. **The session middleware fork is gone.** The hardening ships as stock `SessionOptions`; the cookie
   renames `nihdi.session` → `.Cloudstrap.Session` (existing sessions reset once at rollout).
2. **Inbound correlation ids are honored** (and generated when absent) via `Cloudstrap.Observability`
   — the source's outbound-only `CorrelationSourceMiddleware` is not ported.
3. **Browsers get an error page, not raw JSON.** The source handler wrote `{StatusCode, Message}`
   JSON to every caller; the contract is now negotiated (see *Error handling*).
4. **No `AllowAnyOrigin` fallback.** No configured origins now means no CORS at all.
5. **The conventional default route is on by default** (the source mapped attribute routes only);
   switch it off with `MapDefaultControllerRoute = false`.
6. **No forwarded-headers, static-web-assets or path-base magic.** Each is explicit configuration or
   a hook (see *Recipes*); the path base comes only from `Cloudstrap:Application:PathBase`.
7. **Localization is unbundled** — deliverable #24 (`Cloudstrap.Localization`).
8. **No automatic `RequireAuthorization()`** on mapped controllers — protection is the auth package's
   fallback policy or your attributes.
9. **No missing-cache startup surprise**: without a registered `IDistributedCache`, the in-memory
   fallback simply applies (single-instance semantics) instead of failing at startup.
