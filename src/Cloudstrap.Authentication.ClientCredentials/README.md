# Cloudstrap.Authentication.ClientCredentials

Machine-to-machine (OAuth 2.0 client credentials) tokens for Cloudstrap typed HTTP clients, built on
[Duende.AccessTokenManagement](https://docs.duendesoftware.com/accesstokenmanagement/) (Apache-2.0). One
registration call, and every typed client flagged in configuration transparently carries a cached,
renewed bearer token.

## Quick start

```jsonc
// A configuration flag on the client…
{
  "Cloudstrap": {
    "HttpClients": {
      "Catalog": {
        "BaseAddress": "https://catalog.example.com/",
        "AddClientAccessToken": true
      }
    },
    "ClientCredentials": {
      "TokenEndpoint": "https://sts.example.com/connect/token",
      "ClientId": "my-service",
      "Scope": "catalog.read"
      // ClientSecret comes from KeyVault / environment / user-secrets — see below
    }
  }
}
```

```csharp
// …and one call:
builder.Services.AddCloudstrapClientCredentials();
```

Every `Cloudstrap.Extensions` typed client with `AddClientAccessToken: true` now sends
`Authorization: Bearer <token>`, with acquisition, caching and renewal handled by Duende
AccessTokenManagement. Unflagged clients are untouched.

## Settings — `Cloudstrap:ClientCredentials`

| Key | Required | Default | Meaning |
|---|---|---|---|
| `TokenEndpoint` | yes | — | **Absolute** URL of the token endpoint. Read it from the identity provider's `/.well-known/openid-configuration` document — this package deliberately performs no discovery of its own. |
| `ClientId` | yes | — | The client identifier. |
| `ClientSecret` | no | — | The client secret — omit for secret-free operation (below). |
| `Scope` | no | — | Scope requested with every token. |
| `Resource` | no | — | RFC 8707 resource indicator sent with every token request. |
| `TokenCache` | no | `Isolated` | Where tokens are cached — see the trade-off below. |
| `BackchannelHttpClientName` | no | `cloudstrap-clientcredentials` | Name of the `HttpClient` that calls the token endpoint. |

Startup validation fails fast naming the exact offending key, and never echoes a configured value.

## Secret handling

The secret is a configuration value like any other — supply it through **Azure Key Vault** (Cloudstrap's
KeyVault configuration provider), an **environment variable**, or **user-secrets**. Never put a real
secret in `appsettings.json`. Every example in this README is an obvious placeholder.

**Secret-free operation**: register Duende's `IClientAssertionService` and omit `ClientSecret` entirely —
your service signs a `client_assertion` (for example `private_key_jwt` with a KeyVault key) and
Cloudstrap never overwrites it:

```csharp
builder.Services.AddSingleton<IClientAssertionService, MyAssertionService>();
builder.Services.AddCloudstrapClientCredentials();
```

The startup log states the credential type in force — a name, never a value.

## The token cache trade-off (`TokenCache`)

- **`Isolated` (default)** — tokens live in a memory-only cache private to this package. Nothing
  token-shaped can ever reach your application's `IDistributedCache`. Cost: one token acquisition per
  application instance.
- **`Shared`** — tokens use the application's `HybridCache`, including its distributed second tier when
  one is registered: fewer token requests across instances, but bearer tokens at rest in a shared store.
  Before opting in, see Duende's guidance on
  [encrypting cached tokens](https://docs.duendesoftware.com/accesstokenmanagement/advanced/client-credentials/)
  with a custom `IHybridCacheSerializer`.

The mode in force is stated in the startup log.

## Per-client token parameters

Each flagged client may override the request through its own section:

```jsonc
"Cloudstrap:HttpClients:Catalog:TokenRequestParameters": {
  "Scope": "catalog.read",        // overrides Cloudstrap:ClientCredentials:Scope for this client
  "Resource": "urn:catalog",      // RFC 8707
  "ForceRenewal": false           // true = bypass the cache on every request (diagnostics only; warns)
}
```

Clients with identical parameters share one cached token; different `Scope`/`Resource` values are
acquired and cached separately. `SignInScheme` and `ChallengeScheme` are user-token settings: on a
client-credentials request they are ignored, with one warning naming the key —
interactive user tokens are `Cloudstrap.Authentication.OpenIdConnect`'s job.

A client may set **both** `AddUserAccessToken` and `AddClientAccessToken`. The user-token handler
runs first, and this handler **leaves an `Authorization` header another handler already set alone** —
so on a both-flagged client the signed-in **user's** token is the one that reaches the peer, and no
machine token is acquired for that request. The machine token applies only where no user token was
attached.

## Direct access to the token

The Duende client this package registers is named `cloudstrap`
(`CloudstrapClientCredentials.TokenClientName`). There is no Cloudstrap facade — inject Duende's
`IClientCredentialsTokenManager` and ask it directly:

```csharp
TokenResult<ClientCredentialsToken> result = await tokenManager.GetAccessTokenAsync(
    ClientCredentialsClientName.Parse(CloudstrapClientCredentials.TokenClientName),
    new TokenRequestParameters(),
    cancellationToken);
```

## The backchannel

Token requests go through a dedicated named `HttpClient` (`cloudstrap-clientcredentials`):

- It deliberately carries **no correlation header** — the identity provider is infrastructure, not part
  of the request's business trace.
- `configurator.Backchannel` customizes it (proxy, extra handlers, a test server's in-process handler).
  The hook applies to the default-named client; if you rename it via `BackchannelHttpClientName`,
  configure the renamed client with the standard `AddHttpClient("your-name")` registration instead.
- A flagged client's `{name}-liveness` health probe client never carries a token.

## Failure behavior

- **Lazy**: nothing talks to the identity provider at startup — an IdP outage never stops your service
  from starting. The first outbound call that needs a token is what fails.
- **Loud**: a rejected credential, a failing token endpoint or an unreachable IdP throws an exception
  naming token acquisition and the configured endpoint, logged once — and the outbound request is
  **never** sent unauthenticated.
- **Secret-free**: no credential or token value ever appears in a log line or an exception, at any log
  level.
- A `401` from the destination triggers exactly one forced token renewal and one retry through the
  intact handler chain (your resilience handlers included), then the response is yours.
- A token response without `expires_in` falls back to Duende's default cache lifetime.

## Notes

- **Dependency pins**: `System.IdentityModel.Tokens.Jwt` must stay ≥ 8.0.1 and **< 9.0.0** — this
  package's Duende dependency and `Cloudstrap.WebApi`'s JWT bearer both ride the
  `Microsoft.IdentityModel` 8.x family; a future 9.x pin breaks restore.
- **Framework reference**: referencing this package pulls in `Cloudstrap.Extensions`, which carries the
  `Microsoft.AspNetCore.App` framework reference.
- **Aspire**: no overlap — token acquisition is outside ServiceDefaults' remit. Your resilience
  configuration (per client or `ConfigureHttpClientDefaults`) is neither duplicated nor bypassed.
- This package registers **no inbound authentication** and no authorization policy. Validating tokens
  your API receives is `Cloudstrap.WebApi`'s `AddCloudstrapJwtBearer`.
