# Mutation check: 1-3

<!-- Written by scripts/mutation-check.mjs and committed with the slice. For each survivor the build writes the classification
     ('equivalent' only when no test could ever observe the change; else 'accepted', for the reviewer to approve) and one line why;
     the classifications are kept when the check runs again. -->

mutation: score 95.9% (killed 116, survived 5, no coverage 0, timeout 0)
outside the slice: 0 (not scored)

- Base: `01d4102`
- Threshold: 80%
- Report: `artifacts/mutation/1-3/` (not committed)

## Files mutated

<!-- Only the lines the slice added or modified are mutated and scored; a survivor outside them is never a reason to add a test. -->

- `src/Cloudstrap.Proxy/CloudstrapForwarderHttpClientFactory.cs` lines 1-156
- `src/Cloudstrap.Proxy/CloudstrapProxyOptions.cs` lines 1-55
- `src/Cloudstrap.Proxy/CloudstrapProxyOptionsValidator.cs` lines 1-73
- `src/Cloudstrap.Proxy/EndpointRouteBuilderExtensions.cs` lines 1-30
- `src/Cloudstrap.Proxy/HostApplicationBuilderExtensions.cs` lines 1-158
- `src/Cloudstrap.Proxy/ProxyClusterTokenOptions.cs` lines 1-36
- `src/Cloudstrap.Proxy/ProxyMetadataKeys.cs` lines 1-27
- `src/Cloudstrap.Proxy/ProxyMetadataReader.cs` lines 1-106
- `src/Cloudstrap.Proxy/ProxyRouteConfigFilter.cs` lines 1-128
- `src/Cloudstrap.Proxy/ProxyTokenMode.cs` lines 1-29

## Survivors

| Mutant | Status | Classification | Why |
|---|---|---|---|
| `src/Cloudstrap.Proxy/CloudstrapProxyOptionsValidator.cs:58 Statement mutation` | survived | accepted | Null guard on an internal `IValidateOptions`; its only caller, the options framework, never passes null options. Without it a direct null call throws `NullReferenceException` instead of `ArgumentNullException`; no production path makes that call. |
| `src/Cloudstrap.Proxy/EndpointRouteBuilderExtensions.cs:25 Statement mutation` | survived | equivalent | YARP's `MapReverseProxy` guards the same parameter with the same exception; `MapCloudstrapProxy_NullEndpoints_ThrowsArgumentNullException` passes either way, so no caller can tell the difference. |
| `src/Cloudstrap.Proxy/HostApplicationBuilderExtensions.cs:150 String mutation` | survived | equivalent | The fallback applies only when `ConfigurationSection` is null, and the Required rule then fails start before any route is loaded (`AddCloudstrapProxy_ConfigurationSectionClearedInCode_FailsStartNamingTheKey`); the section name used in that case is never read. |
| `src/Cloudstrap.Proxy/ProxyClusterTokenOptions.cs:33 Conditional (true) mutation` | survived | equivalent | `Get<T>()` on a section that does not exist returns null, the same as the `Exists()` branch; both give the provider defaults. |
| `src/Cloudstrap.Proxy/ProxyRouteConfigFilter.cs:54 Statement mutation` | survived | accepted | Null guard on an internal `IProxyConfigFilter` callback; YARP never calls it with a null route. Without it only the exception type of a direct null call changes; no production path makes that call. |

## Tests added or strengthened by this check

Written after the behavior existed, so none was seen red before it (T-2); this check is their proof that they can fail. Run 1: 73.6% (89 killed, 29 survived, 3 no coverage); run 3: 95.9%.

- `ClientTokenModeTests.ClientCluster_NoProviderRegistered_…` — AC-PX4: the fix (`AddCloudstrapClientCredentials`, or `None`) and "no request without the token" dropped from the message.
- `ClientTokenModeTests.ClientCluster_RouteForwardsInboundCredentials_…`, `…_StripInboundCredentialsFalse_…` — AC-PX3/AC-PX4: the reason (caller's credentials sent in place of the app token) or a remedy dropped from the fail-closed message.
- `ClientTokenModeTests.ClientCluster_ProviderReturnsHandlerWithInnerHandlerSet_…` — AC-PX4: the seam contract ("return a new handler whose InnerHandler is null") dropped from the message.
- `TokenModeTests.UserCluster_NoUserProviderRegistered_…` — AC-PX6: the fix (`AddCloudstrapOpenIdConnect`) or the no-token promise dropped.
- `TokenModeTests.UnknownDefaultTokenMode_…` — AC-PX10: "numbers are not accepted" dropped.
- `TokenModeTests.ForwarderFactory_UndefinedTokenMode_RefusesToBuildTheClusterClient` (new, unit) — AC-PX10: the factory's last guard removed, so an undefined mode could build a client.
- `ConfusedDeputyGuardTests` (shared guard assertion) — AC-PX5: the reason or a remedy (signed-in user, `None`) dropped.
- `RegistrationTests.AddCloudstrapProxy_ConfigurationSectionMissing_…`, `…_CalledTwice_…` — AC-PX10/AC-PX1: the remedy dropped from the message.
- `RegistrationTests.AddCloudstrapProxy_ConfigurationSectionSetInCode_IsUsed` (new) — AC-PX10: a section named in code ignored when YARP is loaded.
- `RegistrationTests.AddCloudstrapProxy_ConfigurationSectionClearedInCode_FailsStartNamingTheKey` (new) — AC-PX10: a null section accepted instead of failing start.
