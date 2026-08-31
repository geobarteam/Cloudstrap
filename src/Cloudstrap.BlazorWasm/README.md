# Cloudstrap.BlazorWasm

Blazor WebAssembly client helpers for a BFF-hosted SPA. One composite call gives the app
cookie-credentialed HTTP with automatic XSRF protection and BFF-driven authentication state; one
line registers a typed or Refit API client that rides the same hardened pipeline. No token ever
lives in the browser — the session is a server-side cookie, the BFF pattern's whole point.

The package is a standalone leaf: four NuGet dependencies
(`Microsoft.AspNetCore.Components.WebAssembly`, `Microsoft.AspNetCore.Components.Authorization`,
`Microsoft.Extensions.Http`, `Refit`), zero project references, zero framework reference — safe for
the WASM linker.

## Quick start

Both halves of the contract, client and BFF:

```csharp
// WASM client (Program.cs)
builder.AddCloudstrapBlazorWasm();                          // cookie+XSRF pipeline, BFF auth state,
                                                            // AuthorizationCore + cascading state
builder.Services.AddCloudstrapWasmRefitClient<IDoctorServiceClient>(
    builder.HostEnvironment.BaseAddress);                   // rides the same hardened pipeline

// Bff host (pairs with Cloudstrap.Authentication.OpenIdConnect)
builder.Services.AddAntiforgery(o => o.HeaderName = "X-XSRF-TOKEN");   // must match XsrfHeaderName
app.UseCloudstrapWebApi(pipeline => pipeline.ConfigureEndpoints = endpoints =>
{
    endpoints.MapCloudstrapAuthenticationEndpoints();       // login/logout
    endpoints.MapCloudstrapBffUserEndpoint();               // the user endpoint + XSRF issuance
    endpoints.MapFallbackToFile("index.html");
});
```

## Settings — `Cloudstrap:BlazorWasm`

The section is optional; every default applies without it. A delegate passed to
`AddCloudstrapBlazorWasm(o => ...)` wins over configuration.

> ⚠️ **No secrets.** In a WASM app this section lives in `wwwroot/appsettings.json`, which the
> browser downloads publicly. Paths and header names only — never a credential.

| Key | Default | Meaning |
|-----|---------|---------|
| `UserEndpointPath` | `bff/user` | Relative path of the BFF's user endpoint, resolved against the base address |
| `XsrfHeaderName` | `X-XSRF-TOKEN` | One header name for both capture (from the user endpoint response) and attachment (on mutating requests) |
| `AuthHttpClientName` | `CloudstrapBffAuth` | Name of the internal client the auth state provider fetches with |

## The wire contract

`GET {UserEndpointPath}` answers 200 always, camelCase JSON:

```json
{ "isAuthenticated": true, "userName": "alice", "claims": [ { "type": "sub", "value": "u-1" } ] }
```

and carries the XSRF request token in the `{XsrfHeaderName}` response header. The client captures
the token into the shared `IAntiforgeryTokenStore`; every package-registered client attaches it to
POST/PUT/DELETE/PATCH requests (never GET, never with an empty store; a header already on the
request is replaced).

**Both sides must agree on the header name**: the server's `AddAntiforgery(o => o.HeaderName = ...)`
and `Cloudstrap:OpenIdConnect:XsrfHeaderName` must match this package's `XsrfHeaderName` — a
mismatch means the token is issued under one name and validated under another, and every mutating
call fails with 400.

## Authentication state

`AddCloudstrapBlazorWasm` registers a BFF-driven `AuthenticationStateProvider`: the user endpoint is
fetched once and cached; the signed-in principal has `AuthenticationType = "BffCookie"`, the
`userName` as `ClaimTypes.Name` and the wire claims 1:1. Every failure mode — signed out, HTTP
error, network error, empty body — yields the anonymous principal without a throw. `AuthorizeView`
and `CascadingAuthenticationState` work out of the box.

## Login and logout

Sign-in and sign-out are **full-page navigations** to the BFF (a `fetch` cannot follow the OIDC
redirect dance):

```razor
@inject NavigationManager Navigation

<button @onclick="SignIn">Sign in</button>

@code {
    private void SignIn() => Navigation.NavigateTo(
        $"account/login?returnUrl={Uri.EscapeDataString(Navigation.Uri)}", forceLoad: true);
}
```

The reload after login re-runs the app, refetches the user endpoint and picks up a fresh XSRF token.
In a scenario without a reload, call `IBffAuthenticationStateProvider.ClearAuthenticationState()` to
drop the cache, notify subscribers and refetch.

## Escape hatches

- **Your own client chain**: `CookieHandler` is public — `services.AddHttpClient("mine")
  .AddHttpMessageHandler<CookieHandler>()` rides the same pipeline (register the store and handler
  through any package helper first, or `TryAdd` them yourself).
- **Token seam**: resolve `IAntiforgeryTokenStore` to pre-seed or inspect the token.
- **Per-client serialization**: pass `RefitSettings` to `AddCloudstrapWasmRefitClient<T>` — the
  default is System.Text.Json, camelCase, case-insensitive.

## Edge cases

- `baseAddress` is passed to `Uri` as-is — end it with a trailing slash
  (`builder.HostEnvironment.BaseAddress` already does) so relative paths resolve under it.
- Repeat registrations are safe: services register once, options delegates compose, named-client
  configuration appends.
- Blazor Server and prerendering are out of scope — this package is for browser-hosted WASM;
  server-side apps use `Cloudstrap.BlazorServer`.

## Migrating from the enterprise predecessor

- `AddBlazorWasmForNihdi` → `AddCloudstrapBlazorWasm`; the composite no longer hides an
  `AddLocalization()` — call it yourself when you need it.
- The default user endpoint moved from `api/user` to `bff/user` (industry BFF convention).
- The XSRF header name is one option driving capture **and** attachment — overriding it can no
  longer split the two.
- Options bind from `Cloudstrap:BlazorWasm`; the delegate wins.
- The composite now also registers cascading authentication state.
- `AddNihdiWasmHttpClient`/`AddNihdiWasmRefitClient` → `AddCloudstrapWasmHttpClient`/
  `AddCloudstrapWasmRefitClient`; both now return `IHttpClientBuilder`.
- The XSRF contract is two-sided and validated: the BFF issues the token via
  `MapCloudstrapBffUserEndpoint()` and validates mutating endpoints — the predecessor's client-only
  machinery never actually protected anything.
- Provider and store implementations are internal; the interfaces are the contract.
- The stored-culture bootstrap helper was not ported — localization is its own deliverable.

## License

MIT — part of the [Cloudstrap](https://github.com/geobarteam/Cloudstrap) suite.
