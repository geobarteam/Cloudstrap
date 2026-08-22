# Cloudstrap.Demo.Mvc

The `Cloudstrap.Mvc` README's consumer example, live, on **http://127.0.0.1:5320**:
`AddCloudstrapMvc()` + `UseCloudstrapMvc()` and nothing else — under ten lines of
[Program.cs](Program.cs), **anonymous by design** (no auth package, no IdP dependency; OIDC is
demonstrated by the Blazor apps). It serves a session-backed visit counter and the hardened
browser error page.

## Feature matrix

| Feature | The Cloudstrap call | Proven by (E2E) |
|---|---|---|
| Hardened session — exactly one `.Cloudstrap.Session` cookie (Secure/HttpOnly/Lax), round-tripping in a real browser (#6) | `AddCloudstrapMvc()` | `MvcHost_VisitCounter_RoundTripsWithTheHardenedSessionCookie` |
| Browser callers get the consumer's error page, never a stack trace (#6) | `UseCloudstrapMvc()` error contract | `MvcHost_ThrowingAction_ShowsTheConsumersErrorPageNotAStackTrace` |
| JSON callers get generic RFC 9457 problem details from the same failure (#6) | content-negotiated error contract | `MvcHost_ThrowingAction_PreferringJson_GetsGenericProblemDetails` |
| Conventional routing, static assets and probes all live (#6/#4) | `UseCloudstrapMvc()` pipeline | `MvcHost_HomePage_LoadsWithStaticAssetsAndNoConsoleErrors` |

## Harness notes

- `appsettings.json` pins `UseDeveloperExceptionPage: false` and `IncludeDetails: false`: the E2E
  fixture forces `ASPNETCORE_ENVIRONMENT=Development`, where the unset defaults would select the
  developer page and detail-bearing JSON — the pins make the *hardened* shapes the ones asserted.
- The `Secure` session cookie works in Chromium over plain `http://127.0.0.1:5320` because
  loopback is a trustworthy origin.

## Running

```powershell
dotnet run --project src/demo/Mvc        # needs no other process
```
