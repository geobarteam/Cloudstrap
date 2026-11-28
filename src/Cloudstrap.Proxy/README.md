# Cloudstrap.Proxy

A trusted-subsystem front door on [YARP](https://github.com/dotnet/yarp) in one registration call.

## The trusted-subsystem pattern

The front door (a BFF, a Blazor Server app, an MVC site) signs the user in. A downstream API does not trust the
user's browser; it trusts the front door. So the front door forwards selected routes to the API with **its own**
access token, and keeps the user's credentials to itself:

- a proxied route requires a signed-in user unless it says otherwise;
- the caller's `Cookie` and `Authorization` headers never reach the API;
- the API receives the application's token (`Client` mode, the default), the signed-in user's token (`User`) or
  none (`None`), chosen per cluster;
- an anonymous caller can never borrow the application's token, and every unsafe combination fails startup.

## Getting started

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication(/* your sign-in scheme */);
builder.Services.AddAuthorization();
builder.Services.AddCloudstrapClientCredentials();  // Cloudstrap.Authentication.ClientCredentials: the app's token
builder.AddCloudstrapProxy();                       // YARP routes/clusters from the "ReverseProxy" section

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapCloudstrapProxy();                   // after UseAuthentication/UseAuthorization
app.Run();
```

```json
{
  "ReverseProxy": {
    "Routes": {
      "orders": {
        "ClusterId": "orders-api",
        "Match": { "Path": "/proxy/orders/{**catch-all}" },
        "Transforms": [ { "PathRemovePrefix": "/proxy" } ]
      }
    },
    "Clusters": {
      "orders-api": {
        "Destinations": { "primary": { "Address": "https://orders.example.com/" } }
      }
    }
  }
}
```

Routes and clusters use YARP's own `ReverseProxy` configuration schema, unchanged; the section name can be changed
with `Cloudstrap:Proxy:ConfigurationSection`. `AddCloudstrapProxy` returns YARP's `IReverseProxyBuilder` and
`MapCloudstrapProxy` returns YARP's `ReverseProxyConventionBuilder`, so every YARP extension stays reachable.
`AddCloudstrapProxy` may be called once per host.

| Setting (`Cloudstrap:Proxy:…`) | Default | Meaning |
|---|---|---|
| `ConfigurationSection` | `ReverseProxy` | The YARP section; it must exist |
| `DefaultTokenMode` | `Client` | The token mode of clusters without `Cloudstrap.TokenMode` metadata |
| `StripInboundCredentials` | `true` | Remove the caller's `Cookie` and `Authorization` (`false` is unsafe) |
| `Clusters:{clusterId}:TokenRequestParameters` | absent | Per-cluster token request parameters (scope, resource) |

| Metadata key | On | Meaning |
|---|---|---|
| `Cloudstrap.TokenMode` | cluster | `None`, `Client` or `User` |
| `Cloudstrap.ForwardInboundCredentials` | route | `true` keeps the caller's `Cookie` and `Authorization` (unsafe) |
| `Cloudstrap.AllowAnonymousWithToken` | route | `true` lets an `Anonymous` route forward with a token |

## Routes require a signed-in user by default

A route without an `AuthorizationPolicy` gets YARP's reserved `Default` policy: the application's default
authorization policy, which requires a signed-in user unless you changed it. This applies even when the app sets
no fallback policy, and to routes added by a configuration reload. A caller who fails it is challenged by the
front door (401, or a redirect to sign-in for a cookie front door), and nothing is forwarded.

- A named policy is kept as written.
- `"AuthorizationPolicy": "Anonymous"` makes a route deliberately public. On a cluster that attaches a token, this
  needs an explicit override (see [An anonymous route never carries a token by accident](#an-anonymous-route-never-carries-a-token-by-accident)).
- A blank policy name (whitespace only) is not treated as "no policy": YARP rejects it and startup fails.

## The caller's credentials stay at the front door

Every proxied request loses the caller's `Cookie` and `Authorization` headers, on every hosting platform. The
destination never sees the user's session cookie or bearer token. Other headers are forwarded unchanged.

Two opt-outs keep both headers. **Both are unsafe for the trusted-subsystem pattern**: the destination then
receives the caller's own credentials.

- Per route: route metadata `"Cloudstrap.ForwardInboundCredentials": "true"`.
- Globally: `Cloudstrap:Proxy:StripInboundCredentials = false`.

Neither opt-out is allowed on a route to a `Client` cluster: startup fails (see below).

The stripping applies to routes added by a configuration reload too. A caller's `Proxy-Authorization` is never
forwarded either (YARP drops it as a hop-by-hop header). Note that an application transform registered after
`AddCloudstrapProxy` (for example `.AddTransforms(...)` on the builder it returns) runs after the stripping and
can re-add these headers; doing so is equally unsafe.

## The destination receives the application's token (`Client` mode, the default)

A cluster without `Cloudstrap.TokenMode` metadata uses `Cloudstrap:Proxy:DefaultTokenMode`, which is `Client`:
every forwarded request carries `Authorization: Bearer <application token>` from the registered
`IClientAccessTokenHandlerProvider` (install `Cloudstrap.Authentication.ClientCredentials` and call
`AddCloudstrapClientCredentials`). The token is set after the caller's own `Authorization` is removed.

```json
{
  "Cloudstrap": {
    "Proxy": {
      "Clusters": {
        "orders-api": {
          "TokenRequestParameters": { "Scope": "orders.read" }
        }
      }
    }
  }
}
```

- The provider is asked with the **cluster id** as the client name, and the cluster's
  `Cloudstrap:Proxy:Clusters:{clusterId}:TokenRequestParameters` (absent: the provider's defaults).
- **No provider registered**: startup fails, naming the cluster, `Cloudstrap.TokenMode` and the package. A
  cluster added by a configuration reload is checked when it loads. No request is ever sent without the token.
- **The token cannot be obtained**: the front door answers 502 and nothing is forwarded; there is no
  unauthenticated retry.
- A 401 from the destination is passed through unchanged. The client-credentials handler of
  `Cloudstrap.Authentication.ClientCredentials` first renews the token and re-sends the request once. A request
  **with a body** (for example a `POST`) cannot be re-sent: YARP streams the caller's body and can send it only once.
  Such a request can then end with **502** at the front door instead of the destination's 401. A body-less request
  gets the retry's answer.
- **A route that forwards the caller's credentials to a `Client` cluster fails startup** (fail closed). The
  client-credentials handler keeps an `Authorization` header that is already on the request. With
  `"Cloudstrap.ForwardInboundCredentials": "true"` on the route, or `Cloudstrap:Proxy:StripInboundCredentials =
  false`, the caller's token would reach the destination instead of the application's. The message names the
  route, the cluster and the opt-out that caused it. A reloaded configuration with this combination is rejected,
  and YARP keeps the previous one.
- Cloudstrap replaces YARP's `IForwarderHttpClientFactory` to build this handler chain. Registering your own
  factory takes the chain over, token included.

## Other token modes: `User` and `None`

Set `Cloudstrap.TokenMode` in a cluster's metadata (or `Cloudstrap:Proxy:DefaultTokenMode` for every cluster
without it):

| Mode | The destination receives | Needs |
|---|---|---|
| `Client` (default) | the application's token | `IClientAccessTokenHandlerProvider` (`Cloudstrap.Authentication.ClientCredentials`) |
| `User` | the signed-in user's token | `IUserAccessTokenHandlerProvider` (`Cloudstrap.Authentication.OpenIdConnect`, `AddCloudstrapOpenIdConnect`) |
| `None` | no token | nothing |

- `User` follows the same rules as `Client`: the provider is asked with the cluster id and the cluster's
  `TokenRequestParameters`; a missing provider fails startup naming the cluster, `Cloudstrap.TokenMode` and
  the package.
- `None` attaches nothing. The caller's `Authorization` is still removed unless the route opts out.
- Only the names `None`, `Client` and `User` are accepted, in any case. Anything else, including a number such as
  `1`, a combination such as `Client, User`, a name with surrounding spaces (`" Client"`) or an empty value, fails
  startup and lists the allowed values. This holds for the cluster metadata and for
  `Cloudstrap:Proxy:DefaultTokenMode`.
- A `User` cluster whose signed-in user has no token in the session answers 502, and nothing is forwarded.
- A configuration reload that changes a cluster's `Cloudstrap.TokenMode` applies the new mode.
- No mode ever adds a header carrying the user's identity. The destination learns who the user is only from a
  `User`-mode token.

## An anonymous route never carries a token by accident

A route with `"AuthorizationPolicy": "Anonymous"` (any case) to a cluster whose mode is `Client` or `User`
(including `Client` by default) **fails startup**: any caller could otherwise reach the destination with the
application's identity (a confused deputy). The message names the route, the cluster and the override key. This
holds for routes and clusters supplied in code (`LoadFromMemory`) too.

After startup, the app never fails because of this guard. A configuration reload (or an
`InMemoryConfigProvider.Update`) that would create the combination is **rejected as a whole**: YARP logs the error
and keeps the previous configuration, so nothing changes until a valid one arrives. This covers a reload that:

- sets a route to `Anonymous` while its cluster is `Client` or `User` (the route keeps requiring a signed-in user);
- moves an `Anonymous` route to a `Client` or `User` cluster (the route keeps its old cluster);
- switches the cluster behind an `Anonymous` route from `None` to `Client` or `User` (the cluster stays `None`);
- adds an `Anonymous` route to a `Client` or `User` cluster (the route is not added).

Exceptions:

- An `Anonymous` route to a `None` cluster starts normally.
- For a deliberately public route that must carry the application's token, set the route metadata
  `"Cloudstrap.AllowAnonymousWithToken": "true"` (only `true`, in any case, counts):

```json
{
  "ReverseProxy": {
    "Routes": {
      "public-catalog": {
        "ClusterId": "catalog-api",
        "AuthorizationPolicy": "Anonymous",
        "Match": { "Path": "/catalog/{**catch-all}" },
        "Metadata": { "Cloudstrap.AllowAnonymousWithToken": "true" }
      }
    }
  }
}
```

A `User` cluster behind an anonymous route has no signed-in user to take a token from: its requests answer 502.

## Fail-closed startup rules

Cloudstrap refuses to start rather than forward unsafely. Host startup fails, with a message naming the route,
the cluster and the key to change, when:

- a cluster in `Client` mode has no `IClientAccessTokenHandlerProvider` registered, or a `User` cluster has no
  `IUserAccessTokenHandlerProvider`;
- a token mode (cluster metadata or `Cloudstrap:Proxy:DefaultTokenMode`) is not exactly one of `None`, `Client`,
  `User`;
- a route forwards the caller's credentials (`Cloudstrap.ForwardInboundCredentials`, or
  `StripInboundCredentials = false`) to a `Client` cluster;
- an `Anonymous` route targets a `Client` or `User` cluster without `Cloudstrap.AllowAnonymousWithToken`;
- the YARP configuration section does not exist, or `AddCloudstrapProxy` is called twice.

A configuration reload that breaks one of these rules is rejected as a whole and YARP keeps the previous
configuration. At request time the rule is the same: when the token cannot be obtained the front door answers
**502** and forwards nothing; no request is ever sent without its token.

## Forwarding headers

`X-Forwarded-For`, `-Proto`, `-Host` and `-Prefix` follow YARP's defaults. A value the caller sends is replaced,
not passed through: `X-Forwarded-Prefix` comes from the front door's `PathBase`. The destination receives its
own `Host` header, not the caller's.

## Correlation and telemetry

Every proxied request carries the front door's correlation id in the Cloudstrap correlation header
(`Cloudstrap:Correlation:HeaderName`, `X-Correlation-ID` by default) unless the request already has one, and
YARP continues the trace through `traceparent`. Cloudstrap adds no resilience handler, no
`ConfigureHttpClientDefaults` handler and no OpenTelemetry exporter to the proxied hop: YARP's activities flow to
the host's existing pipeline. The token is never written to a log record or an activity.

## Destination resolution: one owner

Destinations come from YARP's configuration. To resolve logical addresses (for example `http://orders-api`)
through service discovery, register YARP's `IDestinationResolver` hook on the builder `AddCloudstrapProxy`
returns, for example with `Microsoft.Extensions.ServiceDiscovery.Yarp`:

```csharp
builder.AddCloudstrapProxy()
       .AddServiceDiscoveryDestinationResolver();   // your reference, not Cloudstrap's
```

Resolved destinations keep every Cloudstrap behavior: the token, the stripping and the signed-in default. Keep
**one owner** for the proxy: configure YARP through `AddCloudstrapProxy` and chain service discovery on it; do not
also call `AddReverseProxy()` with its own configuration (for example from an Aspire template) in the same host.
Cloudstrap itself references no Aspire or service-discovery package. A cluster with no destination, or one that
resolves to none, answers 503 and sends nothing.

## WebSockets and Server-Sent Events

YARP proxies WebSocket upgrades and streamed responses (Server-Sent Events) through the same handler chain. The
token is attached to the upgrade request (or the streaming request) once, and the caller's `Cookie` and
`Authorization` are stripped from it like any other request. A WebSocket connection then lives on with the
identity it was opened with: a token that expires during the connection is not renewed until the client
reconnects.

## Replacing the forwarder's HTTP client factory

Cloudstrap replaces YARP's `IForwarderHttpClientFactory` to build the handler chain (correlation, then the
token handler, then YARP's handler). Registering your own `IForwarderHttpClientFactory` after
`AddCloudstrapProxy` **takes the chain over**: the token and the correlation header are then yours to add.

## TLS to the destination

The connection to the destination uses the platform's standard TLS certificate validation. Cloudstrap ships
**no custom certificate validator** and adds no switch to accept invalid or self-signed certificates. To reach a
destination whose certificate comes from a private certificate authority, trust that authority in the operating
system's trust store. YARP's own per-cluster `HttpClient` settings still apply unchanged; Cloudstrap neither sets
nor overrides them.

## License

MIT. `Yarp.ReverseProxy` is MIT (.NET Foundation).
