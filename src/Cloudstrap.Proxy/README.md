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

## Forwarding headers

`X-Forwarded-For`, `-Proto`, `-Host` and `-Prefix` follow YARP's defaults. A value the caller sends is replaced,
not passed through: `X-Forwarded-Prefix` comes from the front door's `PathBase`. The destination receives its
own `Host` header, not the caller's.

## License

MIT. `Yarp.ReverseProxy` is MIT (.NET Foundation).
