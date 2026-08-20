# Cloudstrap.Demo.IdentityProvider

The demo suite's **shared identity provider host** on **http://127.0.0.1:5310** — demo-only test
infrastructure hosting the seeded `Cloudstrap.TestIdentityProvider` (OpenIddict over EF Sqlite)
as its own process, so the full demo suite is launchable with plain `dotnet run` commands or the
VS Code compound configuration. **Never a real IdP** — placeholder credentials only, loopback
only.

## Single source of truth

[`TestIdentityProviderSeed`](TestIdentityProviderSeed.cs) defines every client and the demo user,
and the E2E fixture calls the **same** helper for its in-process 5310 instance — the two hosts
cannot drift:

| Seeded | Value | Used by |
|---|---|---|
| Machine client | `demo-bff` (audience `demo-selfapi`) | the Bff's `SelfApi` client-credentials flow (#9) |
| Interactive client | `demo-web` (audiences `demo-selfapi`, `demo-api`) | the Bff's cookie OIDC login (#10) + the cross-process user-token hop |
| Interactive client | `demo-blazorserver` (audience `demo-api`) | the BlazorServer demo's OIDC login |
| User | `geobarteam` / `password` (`role: tester`) | every sign-in flow |

Redirect URIs derive from `Demo:ApplicationBaseAddresses` (defaults: the Bff's two launch
addresses) and a BlazorServer base-address parameter (default `http://127.0.0.1:5340/`) — a
differently-ported app is one configuration override away, which is exactly what
`SeparateIdpHost_FullBrowserLogin_AddsDoctorThroughTheUi` does on ports 5311/5304.

The fixture keeps its own loopback instance for `IdentityProviderTokenRequestCount` (the
token-caching assertion); this host deliberately has no counters.

## Running

```powershell
dotnet run --project src/demo/Shared/IdentityProvider    # http://127.0.0.1:5310
```
