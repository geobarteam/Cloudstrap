# Cloudstrap.Authentication.OpenIdConnect

Interactive OpenID Connect login for ASP.NET Core, on the stock handlers and Duende
AccessTokenManagement: one registration call, and your application signs users in with
authorization code + PKCE against any standards-compliant identity provider, holds the session in a
hardened `__Host-Cloudstrap` cookie, refreshes user tokens transparently, and puts the signed-in
user's token on every flagged Cloudstrap typed `HttpClient`.

## Quick start

```jsonc
// A configuration section…
"Cloudstrap:OpenIdConnect": {
  "Authority": "https://idp.example.com/",
  "ClientId": "my-web-app"
  // ClientSecret comes from KeyVault / environment / user-secrets — see below
}
```

```csharp
// …one call…
builder.Services.AddCloudstrapOpenIdConnect();

// …and, if you want ready-made login/logout endpoints, one opt-in call:
app.MapCloudstrapAuthenticationEndpoints();   // GET /account/login + GET /account/logout
```

That's it: an unauthenticated request is challenged to the identity provider with
`response_type=code`, PKCE (S256) and `response_mode=form_post`; the completed sign-in issues the
hardened session cookie; tokens are stored in the authentication session and refreshed behind the
user's back. The flow is **not configurable** — no implicit, no hybrid, no PKCE opt-out.

## Settings — `Cloudstrap:OpenIdConnect`

| Key | Default | Meaning |
|---|---|---|
| `Authority` | *(required)* | The identity provider's absolute URL; endpoints are discovered from its metadata. |
| `ClientId` | *(required)* | This application's **own** interactive client — never read from `Cloudstrap:ClientCredentials`. |
| `ClientSecret` | *(none)* | Optional: omit for public clients or secret-free client authentication. |
| `Scope` | `openid profile offline_access` | Space-delimited; setting it **replaces** the default entirely. |
| `MapInboundClaims` | `false` | Claims keep the names the token used — `sub` stays `sub`. |
| `RequireHttpsMetadata` | *(unset)* | Unset = required everywhere except `Development`. |
| `CallbackPath` | `/signin-oidc` | Where the provider posts the authorization response. |
| `SignedOutCallbackPath` | `/signout-callback-oidc` | Where the provider returns after sign-out. |
| `LoginPath` / `LogoutPath` | `/account/login` / `/account/logout` | The opt-in endpoints' paths. |
| `RequireAuthenticatedEndpoints` | `true` | The require-authenticated fallback policy — see below. |
| `Cookie:Name` | `__Host-Cloudstrap` | The session cookie's name. |
| `Cookie:Lifetime` | `08:00:00` | The session lifetime. |
| `Cookie:SlidingExpiration` | `true` | Whether activity extends the session. |

Misconfiguration fails at host startup naming the exact key — never echoing a configured value.

## The session cookie

The cookie ships hardened: name **`__Host-Cloudstrap`** (the `__Host-` prefix makes the browser
itself enforce `Secure`, `Path=/` and the absence of a `Domain` attribute), **`HttpOnly`** always,
**`Secure`** always, **`SameSite=Lax`** (Strict would break the OpenID Connect callback
navigation), 8 h sliding expiration.

Only the name, lifetime and sliding behavior are configuration. The hardened trio is deliberately
reachable **only in code**, through `configurator.Cookie` — weakening it is a visible act in
`Program.cs`, never config drift:

```csharp
builder.Services.AddCloudstrapOpenIdConnect(configurator =>
    configurator.Cookie = cookie => cookie.Cookie.SameSite = SameSiteMode.None); // your responsibility
```

## Where tokens live

User tokens (access, refresh, id) are stored **in the authentication session** — the encrypted
cookie ticket (`SaveTokens`, Duende AccessTokenManagement's requirement). **No server-side ticket
store, no distributed token store and no cache is registered**; Cloudstrap owns zero storage code.
The trade-off is a larger cookie carrying the encrypted tokens.

> ⚠️ **Running more than one instance?** The cookie can only be decrypted where the Data Protection
> key ring is shared — add `Cloudstrap.Extensions`' `AddCloudstrapDataProtection` (the
> `Cloudstrap:DataProtection` section) as the companion. The symptom of forgetting it: users are
> bounced back to the login page whenever the load balancer sends them to the second instance.

## Secret handling

The client secret is a configuration value like any other: supply it through **Azure Key Vault**
(the KeyVault configuration provider), an **environment variable**, or **user-secrets** — never
`appsettings.json`. Every example in this README is an obvious placeholder. The secret is optional:
public clients omit it, and secret-free client authentication (client assertions, DPoP-adjacent
setups) is reachable through `configurator.OpenIdConnect`, which hands you the stock
`OpenIdConnectOptions` last, with the final say.

## Scopes

`offline_access` is in the default scope because transparent renewal needs a refresh token —
without it, the session dies with the first access token. Where a provider treats `offline_access`
specially (consent prompts, disabled by policy), set `Cloudstrap:OpenIdConnect:Scope` to
`"openid profile"`: the setting **replaces** the default, so nothing is silently appended. A
per-HTTP-client `TokenRequestParameters:Scope` narrows the *token request*, not the login.

## Claims

`MapInboundClaims` defaults to `false`: `sub` stays `sub`, `name` stays `name`, `role` stays `role`
— no legacy URI-style renaming. `User.Identity.Name` resolves from the `name` claim and role checks
from `role`.

## Coexistence with `AddCloudstrapJwtBearer`

One host can serve browser users and API callers at the same time. When `Cloudstrap.WebApi`'s
`AddCloudstrapJwtBearer` is also registered, a request carrying `Authorization: Bearer …`
authenticates **and fails** through the JWT scheme — a 401 with `WWW-Authenticate: Bearer`, never a
login redirect — while everything else uses the cookie/OpenID Connect path. This holds whichever
order the two packages were registered in. Without the JWT package the forwarding is inert.

Pin an endpoint to one side explicitly with the standard attribute:

```csharp
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] // machine callers only
```

## Endpoint protection

`RequireAuthenticatedEndpoints` defaults to `true`: a require-authenticated fallback policy covers
every endpoint that doesn't opt out — the same flag name, default and opt-outs as
`Cloudstrap:JwtBearer`. The two documented opt-outs: `[AllowAnonymous]` for one endpoint, the flag
for the whole application. Health probes and the API documentation endpoints are mapped anonymously
by Cloudstrap and stay reachable either way.

## Login and logout

`MapCloudstrapAuthenticationEndpoints()` is opt-in and maps exactly two endpoints:

- `GET /account/login?returnUrl=/somewhere` — challenges to the identity provider and returns to
  the given URL, **only when it is local**. Absolute URLs, `//host` and `/\host` shapes fall back
  to `/` — a caller-supplied return URL can never bounce a user off-site.
- `GET /account/logout` — RP-initiated sign-out of **both** the local session and the identity
  provider session. A provider whose metadata advertises no `end_session_endpoint` still gets a
  completed local sign-out, with a warning logged once.

Nothing is mapped unless you call it. Prefer your own endpoints? `Challenge()` and `SignOut()` work
unqualified, because the stock scheme names are kept: the default scheme is the cookie, the default
challenge and sign-out scheme is OpenID Connect — and signing out of the OpenID Connect scheme also
ends the cookie session, so a bare `SignOut()` ends both.

## BFF user endpoint + XSRF

`MapCloudstrapBffUserEndpoint()` is a second, separate opt-in for BFF hosts serving a browser
client (the `Cloudstrap.BlazorWasm` pairing). It maps `GET /bff/user`
(`Cloudstrap:OpenIdConnect:UserEndpointPath`) — anonymous-safe, `200` always — answering the wire
contract in camelCase JSON:

```json
{ "isAuthenticated": true, "userName": "alice", "claims": [ { "type": "sub", "value": "u-1" } ] }
```

with `userName`/`claims` present only for a signed-in session (claims mirror the cookie principal
1:1), and carrying the XSRF **request token** in the `X-XSRF-TOKEN` response header
(`Cloudstrap:OpenIdConnect:XsrfHeaderName`).

The endpoint *issues* tokens; **validation is your stock wiring**, and the mapper throws at map
time if antiforgery services are missing — issuing tokens nothing validates would be security
theater:

```csharp
builder.Services.AddAntiforgery(o => o.HeaderName = "X-XSRF-TOKEN");   // must match XsrfHeaderName

// then validate mutating endpoints:
[HttpPost]
[ValidateAntiForgeryToken]
public IActionResult Add(...) => ...;               // controllers
// or, in minimal APIs: await antiforgery.ValidateRequestAsync(httpContext);
```

Three names must agree: `XsrfHeaderName` here, the `AddAntiforgery` header name, and the WASM
client's `Cloudstrap:BlazorWasm:XsrfHeaderName`. Note the anonymous-token edge case: a token issued
to an anonymous session does not validate for the later signed-in user — the full-page login
navigation reloads the client, which refetches the endpoint and picks up a fresh token.

## User tokens on typed clients

Flag any Cloudstrap typed client, and its requests carry the **signed-in user's** access token:

```jsonc
"Cloudstrap:HttpClients:Catalog": {
  "BaseAddress": "https://catalog.example.com/",
  "AddUserAccessToken": true
}
```

All five `TokenRequestParameters` members are honored per client: `Scope`, `Resource`,
`ForceRenewal`, `SignInScheme` and `ChallengeScheme`. Duende AccessTokenManagement refreshes an
expiring token transparently — one refresh grant, even under concurrent requests, with the renewed
tokens written back into the session.

Driving a flagged client with **no signed-in user** — an anonymous request, a background service —
throws before anything is sent, naming the flag and pointing at `AddClientAccessToken`
(`Cloudstrap.Authentication.ClientCredentials`) as the machine-identity alternative. A client may
set **both** flags: the user's token is the one that reaches the peer, and the machine token
applies only where no user is signed in… which for a user-flagged client means never — see the
client-credentials README's both-flags note.

## When the refresh token expires

A session cookie can outlive the refresh token; a flagged client then fails loudly (no
unauthenticated request is ever sent). Two postures, your choice:

- **Catch and challenge**: handle the failure at your call site and `Challenge()` — the user
  re-authenticates and comes back.
- **Align the lifetimes**: configure `Cookie:Lifetime` shorter than the provider's refresh-token
  lifetime, so the session always expires first and re-challenges naturally.

## Dependency notes

- ⚠️ `Microsoft.IdentityModel.Protocols.OpenIdConnect` must stay **≥ 8.0.1 and < 9.0.0** — the same
  `Microsoft.IdentityModel` 8.x family constraint the client-credentials package records for
  `System.IdentityModel.Tokens.Jwt`. With central transitive pinning, a future 9.x pin breaks
  restore.
- This package inherits the `Microsoft.AspNetCore.App` framework reference through
  `Cloudstrap.Extensions` — it is for ASP.NET Core applications, not plain workers.

## Verifying against a real identity provider

The test suite verifies the full flow against the in-repo OpenIddict-based provider. To verify
against **Keycloak, Microsoft Entra ID or any conformant provider**, no code changes are needed —
configuration only:

1. Register a confidential web client at the provider with redirect URI
   `https://your-app/signin-oidc` and post-logout redirect URI
   `https://your-app/signout-callback-oidc`.
2. Point `Cloudstrap:OpenIdConnect:Authority` at the provider (for Keycloak, the realm URL; for
   Entra, `https://login.microsoftonline.com/{tenant}/v2.0`), set `ClientId`, and supply the secret
   through a secret store.
3. Sign in, inspect the `__Host-Cloudstrap` cookie, call a flagged client, sign out — the behaviors
   in this README are provider-neutral.

## Troubleshooting

Cryptic `IDX…` token-validation errors redact issuer/audience/key details by default. For **local
diagnosis only**, `IdentityModelEventSource.ShowPII = true` reveals them — ⚠️ it copies tokens and
personal data into your logs, so it must never reach a deployed environment. Cloudstrap never sets
it.

## What is deliberately not here

- **Browser auth state** — the client half of the BFF contract lives in `Cloudstrap.BlazorWasm`;
  this package's `MapCloudstrapBffUserEndpoint()` is the server half.
- **Blazor Server circuit token store** — arrives with `Cloudstrap.BlazorServer`.
- **Front-/back-channel logout endpoints** — demand-driven; not yet shipped.
- **Server-side session or distributed token stores** — the session cookie is the storage; advanced
  setups can register a Duende store themselves.
- **Multi-scheme / multi-tenant OIDC registrations** — one interactive client per application.
- **Consent, registration or account UI** — your identity provider's job.

## Aspire

No overlap: interactive auth is outside ServiceDefaults' remit. The package references zero
`Aspire.*` packages, and the user-token handler joins typed-client pipelines through the same seam
that tolerates resilience handlers added via `ConfigureHttpClientDefaults`.
