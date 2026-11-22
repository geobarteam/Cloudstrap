# Cloudstrap.Proxy

A trusted-subsystem front door on [YARP](https://github.com/dotnet/yarp) in one registration call.

```csharp
builder.AddCloudstrapProxy();   // YARP routes/clusters from the "ReverseProxy" section
// ...
app.MapCloudstrapProxy();
```

Routes and clusters use YARP's own `ReverseProxy` configuration schema, unchanged. Both calls return YARP's
own builders, so every YARP extension stays reachable.

> This README is still a skeleton. Token modes and the anonymous-route guard are documented as they are built.

## Routes require a signed-in user by default

A route without an `AuthorizationPolicy` gets YARP's reserved `Default` policy: the application's default
authorization policy, which requires a signed-in user unless you changed it. This applies even when the app sets
no fallback policy, and to routes added by a configuration reload. A caller who fails it is challenged by the
front door (401, or a redirect to sign-in for a cookie front door), and nothing is forwarded.

- A named policy is kept as written.
- `"AuthorizationPolicy": "Anonymous"` makes a route deliberately public.
- A blank policy name (whitespace only) is not treated as "no policy": YARP rejects it and startup fails.

## The caller's credentials stay at the front door

Every proxied request loses the caller's `Cookie` and `Authorization` headers, on every hosting platform. The
destination never sees the user's session cookie or bearer token. Other headers are forwarded unchanged.

Two opt-outs keep both headers. **Both are unsafe for the trusted-subsystem pattern**: the destination then
receives the caller's own credentials.

- Per route: route metadata `"Cloudstrap.ForwardInboundCredentials": "true"`.
- Globally: `Cloudstrap:Proxy:StripInboundCredentials = false`.

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
  `"Cloudstrap.ForwardInboundCredentials": "true"` the caller's token would reach the destination instead of the
  application's. The message names the route, the cluster and both metadata keys. A reloaded configuration with this
  combination is rejected, and YARP keeps the previous one.
- `"Cloudstrap.TokenMode": "None"` on a cluster attaches no token.
- Cloudstrap replaces YARP's `IForwarderHttpClientFactory` to build this handler chain. Registering your own
  factory takes the chain over, token included.

## Forwarding headers

`X-Forwarded-For`, `-Proto`, `-Host` and `-Prefix` follow YARP's defaults. A value the caller sends is replaced,
not passed through: `X-Forwarded-Prefix` comes from the front door's `PathBase`. The destination receives its
own `Host` header, not the caller's.

## License

MIT. `Yarp.ReverseProxy` is MIT (.NET Foundation).
