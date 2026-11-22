# Plan: 17-TrustedSubsystemProxy — a front-door host forwards selected routes to a downstream API with one registration call. The front door signs the user in, and the downstream API receives the application's own access token. The caller's cookie and bearer token never leave the front door, and an anonymous caller can never ride on the application's token.

## Overview

Deliverable #17 of the extraction roadmap. **Binding spec: `_specs/17-TrustedSubsystemProxy.md`** (approved by the
owner 2026-11-22).
- Decision Log D-1…D-8: the owner's answers to OQ-1…OQ-8, every recommended option accepted.
- D-9…D-15: the source-checked differences from `Nihdi.Core.Configuration.Proxy`.

The spec's ACs, Port Decision Table, Public API Sketch, Behaviors, Dependencies, Deliberate Behavior Changes, Edge
Cases and Out-of-Scope list are authoritative and are not re-litigated here.

The plan builds **one new shipped package, `Cloudstrap.Proxy`**, on `Yarp.ReverseProxy`, plus its unit-test suite
and a demonstration in the BlazorWasm Bff. Cloudstrap adds four behaviors on top of YARP and re-models none of its
configuration (D-2):
1. Routes require a signed-in user by default.
2. The caller's `Cookie` and `Authorization` headers are removed.
3. The outbound token is attached per cluster through the shipped #4 seams, and fails loudly when no provider is
   registered.
4. A startup guard refuses anonymous routes on token-bearing clusters.

**Not in this plan** (spec Out of Scope + Deliberate Behavior Changes):
- no Cloudstrap route/cluster schema;
- no custom TLS certificate validator (D-13);
- no AKS/environment detection (D-11);
- no user-identity header (D-6);
- no Hangfire dashboard proxy (D-15 → #18);
- no token exchange;
- no resilience handlers;
- no `Aspire.*` / `Microsoft.Extensions.ServiceDiscovery.*` reference anywhere, test projects included (D-8);
- no change to any shipped package. The #9 warning-path wording (spec finding 6) is a recorded follow-up.

Reference patterns (read before planning):
- **Plan shape**: `_plans/8-TestHelpers.md` (mechanics section, Baseline record, Pin record, full-suite check, gate
  shape) and `_plans/16-HangfireScheduler.md`.
- **Seams consumed**:
  - `src/Cloudstrap.Extensions/IClientAccessTokenHandlerProvider.cs` (`CreateClientTokenHandler(string clientName,
    TokenRequestOptions? tokenRequest)`, a fresh `DelegatingHandler` with no `InnerHandler`);
  - `IUserAccessTokenHandlerProvider.cs` (`CreateUserTokenHandler`, same shape);
  - `AccessTokenHandlerWiring.cs`: the fail-loud message shape to mirror.
- **Correlation**: `src/Cloudstrap.Observability/Correlation/CorrelationHttpDelegatingHandler.cs` (public sealed;
  set-if-absent; constructed from `ICorrelationContextAccessor` + `IOptions<CorrelationOptions>`).
- **Token request shape**: `src/Cloudstrap.Core/TokenRequestOptions.cs`.
- **Demo topology**:
  - `src/demo/BlazorWasm/Bff/Program.cs` and `appsettings.json`: `AddCloudstrapClientCredentials()`; the machine
    client `demo-bff` with scope `selfapi`; `UserApi` → `http://127.0.0.1:5330/`;
  - `src/demo/Api/Controllers/DownstreamController.cs` (`GET api/v1/downstream/whoami`) and
    `src/demo/Shared/Contracts/DownstreamWhoAmIDto.cs`;
  - E2E: `E2eFixture.cs`, `ClientCredentialsTests.cs`, `Infrastructure/BrowserSignIn.cs`.
- **Package surface test idiom**: the `PackageSurfaceTests.cs` of existing suites (e.g. `Cloudstrap.WebApi.Tests`).
- **Repo-file tripwire idiom**: `src/Test/UnitTest/Cloudstrap.Hangfire.Tests/DependencyPinTests.cs` (`FindRepoRoot()`
  + `XDocument.Load(src/Directory.Packages.props)`).

### Starting point

#8 is closed (final gate approved 2026-11-22, commit `dac473a`). `Cloudstrap.TestInfrastructure` is available but
not needed: this suite has no SQL. `src/Directory.Packages.props` pins no `Yarp.*` package today.

### AC coverage map (every criterion claimed by at least one step)

| Criterion | Step(s) |
|---|---|
| AC-PX1 (one-call registration over YARP config; YARP builder returned) | 1 |
| AC-PX2 (no policy → signed-in user required; nothing forwarded otherwise) | 2 |
| AC-PX3 (caller `Cookie`/`Authorization` never reach the destination; per-route opt-out) | 3 |
| AC-PX4 (`Client` token via the seam; fail-loud without a provider) | 4 |
| AC-PX5 (confused-deputy startup guard + explicit override) | 6 |
| AC-PX6 (`User` / `None` modes; unknown mode fails) | 5 |
| AC-PX7 (correlation header + `traceparent` on the proxied hop) | 7 |
| AC-PX8 (`X-Forwarded-*` kept; spoofed `X-Forwarded-Prefix` replaced; destination `Host`) | 3 |
| AC-PX9 (no resilience handler, no second exporter) | 7 |
| AC-PX10 (`Cloudstrap:Proxy` options validated on start) | 1 (section) · 5 (token mode) |
| AC-PX11 (packaging, XML docs, sealed, README, CPM pin + license) | 1 (pin) · 9 |
| AC-PX12 (destination-resolver composability, no Aspire/ServiceDiscovery reference) | 8 |
| AC-ASP2 / AC-A3 (dependency-closure tripwires) | 1 (closure guard) · 9 |
| AC-PX13 (zero `Nihdi`/`NIHDI`/`Riziv`/`Wfe`/`Cfe` in added files) | 9 (permanent guard) + final-gate grep |
| AC-PX14 (Bff → Api demo, ≥ 1 E2E: machine identity, anonymous rejected, cookie absent) | 10 |

### Workflow rule 9 / planner rule 15: demonstration

Slice 4 (Step 10) extends the **BlazorWasm Bff** (5300). It proxies a path-prefixed route to the **Api** (5330) in
`Client` mode (D-7). New E2E tests in `Cloudstrap.Demo.E2E.Tests` prove the three AC-PX14 behaviors through the
running apps. No new host and no new port.

### ⚠️ Risk areas (reviewed at the gates named)

- **Auth / credential handling** (human review): the default authorization, the header stripping, the token
  attachment and the confused-deputy guard. Reviewed at **Gate 2** (Slice 1 security core) and **Gate 3** (token
  modes + guard).
- **Dependency update**: first `Yarp.ReverseProxy` pin (MIT). Its transitive closure under
  `CentralPackageTransitivePinningEnabled`. Gated on its own at **Gate 1**, right after Step 1.
- **Public API one-way doors**:
  - `AddCloudstrapProxy` / `MapCloudstrapProxy`, `CloudstrapProxyOptions`, `ProxyTokenMode`;
  - the three `ProxyMetadataKeys` constants;
  - the `Cloudstrap:Proxy:Clusters:{clusterId}:TokenRequestParameters` key.

  Reviewed at **Gate 3**, once all of them exist, and again at the final gate.
- **Shared contract with #18**: path-prefixed routes, `X-Forwarded-Prefix`, `Client` mode, authenticated by
  default. Reviewed at the **final gate**.
- **Aspire overlap**: composition through YARP's own `IDestinationResolver`, never a reference. **Gate 4**.

### Planner mechanics decided here (each reviewed at the named gate)

**(a) Baseline record: filled by the executor before Step 1 (all gates).** Build `src/Cloudstrap.sln`, run every
test executable, and record per-executable totals. #8's final gate recorded 17 unit exes = 926 + 65 and E2E = 68.
The executor records what it actually observes. Afterwards only `Cloudstrap.Proxy.Tests` (new) and the E2E suite
(Step 10) may change count.

| Executable | Baseline tests (passed / total) |
|---|---|
| Cloudstrap.Core.Tests | 54 / 54 |
| Cloudstrap.Extensions.Tests | 76 / 76 |
| Cloudstrap.Observability.Tests | 106 / 106 |
| Cloudstrap.Observability.AzureMonitor.Tests | 38 / 38 |
| Cloudstrap.WebApi.Tests | 109 / 109 |
| Cloudstrap.Mvc.Tests | 66 / 66 |
| Cloudstrap.Worker.Tests | 30 / 30 |
| Cloudstrap.Authentication.ClientCredentials.Tests | 41 / 41 |
| Cloudstrap.Authentication.OpenIdConnect.Tests | 66 / 66 |
| Cloudstrap.BlazorCommon.Tests | 22 / 22 |
| Cloudstrap.BlazorServer.Tests | 47 / 47 |
| Cloudstrap.BlazorWasm.Tests | 41 / 41 |
| Cloudstrap.Messaging.Tests | 62 / 62 |
| Cloudstrap.Messaging.AzureBlob.Tests | 37 / 37 |
| Cloudstrap.Hangfire.Tests | 105 / 105 |
| Cloudstrap.TestIdentityProvider.Tests | 26 / 26 |
| Cloudstrap.TestInfrastructure.Tests | 65 / 65 |
| **Unit total (17 exes; #8's gate recorded 926 + 65)** | **991 / 991** |
| Cloudstrap.Demo.E2E.Tests (#8's gate recorded 68) | 68 / 68 |
| Build warnings at baseline | 0 code warnings; 144 `NU1902`/`NU1903` restore advisories already present on `System.Security.Cryptography.Pkcs` 9.0.11/10.0.10 and `System.Security.Cryptography.Xml` 10.0.10 (new advisories since #8; not raised by this deliverable, not fixed here, as a dependency update needs the owner) |
| Recorded on / by | 2026-11-22, branch `zyggy/01M81J827ARHZRN913B3H8WZ3N` @ `768eb72`, `CLOUDSTRAP_TEST_SQL` unset (LocalDB) / executor (Claude, job 01M81J827ARHZRN913B3H8WZ3N) |

**(b) Full-suite check** (`runTests` is not on the agent PATH, so every executable is run directly):
1. `dotnet build src/Cloudstrap.sln`.
2. Every unit executable `src\Test\UnitTest\<Name>\bin\Debug\net10.0\<Name>.exe`, plus `Cloudstrap.Proxy.Tests`
   from Step 1.
3. The E2E executable (LocalDB + Chromium required).
4. `dotnet format src/Cloudstrap.sln --verify-no-changes`.

**(c) Pin-time procedure for `Yarp.ReverseProxy` (Step 1, Gate 1).** The planning session had no network access.
Before editing `src/Directory.Packages.props` the executor:
1. Reads nuget.org for the latest **stable** `Yarp.ReverseProxy` with a `net10.0` (or `net8.0`+ compatible)
   dependency group, **≥ 2.3.0** (the source's version and the floor for `IDestinationResolver`). It records the
   version, license (expected MIT, .NET Foundation, `dotnet/yarp`) and dependency group.
2. Adds the pin to the **shipped** dependency group with a comment: MIT; first in the suite; deliverable #17; the
   forwarder for `Cloudstrap.Proxy`.
3. Restores. Any NU1101–NU1608 / NU1901–NU1904 warning (fatal under `TreatWarningsAsErrors`) **stops the step** and
   is reported. No floor pin is added without the owner.
4. Confirms these YARP facts the plan relies on, at the pinned version, and records each:
   - `ForwarderHttpClientFactory.WrapHandler(ForwarderHttpClientContext, HttpMessageHandler)` is overridable, and
     the context exposes `ClusterId` + `NewMetadata`;
   - `IProxyConfigFilter.ConfigureRouteAsync(RouteConfig, ClusterConfig?, CancellationToken)`;
   - the reserved `Default` / `Anonymous` policy names;
   - the `X-Forwarded-*` default action is **Set** (it replaces inbound values), and `Prefix` comes from `PathBase`;
   - the original `Host` is not copied by default;
   - `IDestinationResolver` exists.

   If any fact differs, the executor stops and reports before writing code that depends on it.
5. Fills the Pin record.

#### Pin record *(filled by the executor in Step 1; not a planning-time value)*

| Item | Value |
|---|---|
| `Yarp.ReverseProxy` version pinned + license + dependency group as read | **2.3.0**, the latest stable *and* the floor: `dotnet list package --outdated` (with and without `--include-prerelease`) against nuget.org reports no newer version. License MIT (.NET Foundation, `dotnet/yarp`), recorded from the package's known metadata. The job sandbox did not allow reading the nuspec in the global packages folder, so the owner may re-check it. The package ships `lib/net6.0`, `net7.0` and `net8.0` only (no net9.0/net10.0 group), so `net10.0` consumes the **net8.0** asset (`obj/project.assets.json`). Its dependency group lists one package: `System.IO.Hashing >= 8.0.0`. |
| Transitive packages new to the solution (resolved versions) | **None**. `System.IO.Hashing` already resolves at 10.0.3 through the Azure SDK closure of `Cloudstrap.Extensions`. `dotnet list package --include-transitive` for `Cloudstrap.Proxy` shows only YARP plus Extensions' existing closure. |
| Restore / audit warnings (expected none) | **None from YARP**: `dotnet list package --vulnerable --include-transitive` reports no vulnerable package for `Cloudstrap.Proxy`, and neither Proxy project raises a NU* warning. The 144 pre-existing Pkcs/Xml advisories (Baseline record) are unchanged. |
| YARP facts (c)4 confirmed at this version | All six, checked by a throwaway probe (compiled and run against 2.3.0, not committed). (1) `ForwarderHttpClientFactory.WrapHandler(ForwarderHttpClientContext, HttpMessageHandler)` is `protected virtual`; the context exposes `ClusterId` and `NewMetadata`. (2) `IProxyConfigFilter.ConfigureRouteAsync(RouteConfig, ClusterConfig?, CancellationToken)` → `ValueTask<RouteConfig>`. (3) `Default` → anonymous caller 401 with no fallback policy; `Anonymous` → forwarded even under a fallback policy that requires a user. (4) `X-Forwarded-*` default is **Set**: spoofed `X-Forwarded-Prefix: /spoofed` → `/front` (from `PathBase`), and spoofed `X-Forwarded-Host` → the front door's host. (5) The destination sees its own `Host` (`127.0.0.1:<port>`), not the caller's. (6) `Yarp.ReverseProxy.ServiceDiscovery.IDestinationResolver` exists. **Harness note for Step 3**: `TestServer` has no remote IP, so YARP's Set action *removes* a caller's `X-Forwarded-For` instead of replacing it. `Destination_ReceivesXForwardedForProtoAndHost` must set `Connection.RemoteIpAddress`, for example through `TestServer.SendAsync`. |
| Verified on / source | 2026-11-22, nuget.org v3 (via `dotnet list package`), `obj/project.assets.json`, and the pin-time probe, job 01M81J827ARHZRN913B3H8WZ3N |

**(d) Per-cluster token request parameters (Step 4, Gate 3).** The parameters bind from
`Cloudstrap:Proxy:Clusters:{clusterId}:TokenRequestParameters` to the shipped `Cloudstrap.Core.TokenRequestOptions`.
Cluster metadata is a flat string map and cannot carry that shape. The value is passed as `tokenRequest`, with the
**cluster id** as `clientName`. The subsection is absent → `null` (provider defaults). The spec records this decision
in its Public API Sketch.

**(e) Handler chain (Steps 4, 5, 7).** `Cloudstrap.Proxy` replaces YARP's `IForwarderHttpClientFactory` with an
internal sealed `CloudstrapForwarderHttpClientFactory : ForwarderHttpClientFactory` that overrides `WrapHandler`.
Outer to inner:
1. `CorrelationHttpDelegatingHandler`;
2. the token handler for the cluster's mode;
3. YARP's `SocketsHttpHandler`.

Rules:
- The token mode comes from cluster metadata `Cloudstrap.TokenMode`, else `CloudstrapProxyOptions.DefaultTokenMode`.
- The seam provider is resolved **lazily when the cluster's client is built**. Missing provider → an
  `InvalidOperationException` naming the cluster id, the metadata key and the package to install.
- Fail-loud happens at startup for clusters in the initial config, and at config reload for new ones (spec edge
  case).
- The README states that a consumer who replaces `IForwarderHttpClientFactory` takes over this chain.
- No resilience or timeout handler is ever added (AC-PX9).

**(f) Authorization default + guard (Steps 2, 6, Gates 2 and 3).** One internal `IProxyConfigFilter`
(`AddConfigFilter<…>()` inside `AddCloudstrapProxy`):
- `ConfigureRouteAsync`: a route whose `AuthorizationPolicy` is null or empty gets `"Default"` (the app's default
  policy, a signed-in user unless the consumer changed it).
- The same callback receives the route's `ClusterConfig`. A route with policy `Anonymous` on a cluster whose
  effective mode is `Client` or `User` throws, naming the route, the cluster and the override key. Exception: the
  route metadata `Cloudstrap.AllowAnonymousWithToken` is `"true"`.

The expected observable is that `app.StartAsync()` fails. **Confirm at Step 6**: if YARP's initial load reports a
filter exception only as a log entry rather than failing start, the executor adds an internal startup check. That
check runs the same rule over `IProxyConfigProvider.GetConfig()` from an `IHostedLifecycleService.StartingAsync`, and
the choice is reported at Gate 3. On hot reload YARP rejects the bad config and keeps the previous one. That is
documented, not changed.

**(g) Header stripping (Step 3).** `AddTransforms(context => …)` registered by `AddCloudstrapProxy`. It applies
`RequestHeaderRemove("Cookie")` and `RequestHeaderRemove("Authorization")` unless either of these holds:
- `CloudstrapProxyOptions.StripInboundCredentials` is `false`;
- the route metadata `Cloudstrap.ForwardInboundCredentials` is `"true"`.

Transforms run before the forwarder's `HttpClient` pipeline, so the token handler of mechanic (e) sets the outbound
`Authorization` **after** the inbound one is gone. In `None` mode with the opt-out, the caller's `Authorization`
passes through (documented as unsafe).

**(h) Test harness (`Cloudstrap.Proxy.Tests`, Step 1).**
- **Front door**: `WebApplication.CreateBuilder()` + `UseTestServer()`. A test-local `TestAuthenticationHandler`
  signs in a user when the request carries `Authorization: Test <name>`, so the header stripping is observable on
  the same request.
- **Downstream**: an in-process **Kestrel** echo app on `127.0.0.1:0` (loopback only, no external network). It
  records each received request (path, headers) and answers 200 with a JSON echo.
- **Fake seams**: `FakeClientTokenHandlerProvider` / `FakeUserTokenHandlerProvider` set
  `Authorization: Bearer app:{clusterName}` / `Bearer user:{name}` and record the `(clientName, tokenRequest)` they
  were asked for. A `ThrowingClientTokenHandlerProvider` simulates an unreachable token endpoint.
- YARP config comes from `LoadFromMemory(routes, clusters)` or an in-memory `IConfiguration` `ReverseProxy` section,
  depending on the test.
- No Duende, no auth package, no Aspire reference in the test project.

---

## Slice 1: One registration call forwards a route, requires a signed-in user, and keeps the caller's credentials at the front door ⚠️ AUTH + NEW DEPENDENCY

---

## Step 1: `AddCloudstrapProxy` + `MapCloudstrapProxy` forward a configured route to its cluster and return YARP's own builders (AC-PX1; AC-PX10 section; AC-PX11 pin; AC-ASP2/AC-A3 closure guard; mechanics (a), (c), (h)) ⚠️ dependency add

After this step:
- A host with routes/clusters in the `ReverseProxy` section calls `builder.AddCloudstrapProxy()` and
  `app.MapCloudstrapProxy()`, and matching requests reach the destination.
- The calls return YARP's `IReverseProxyBuilder` / `ReverseProxyConventionBuilder`.
- `Yarp.ReverseProxy` is pinned.
- In this step, routes without a policy still fall through to the app's fallback policy (the tests use an
  authenticated caller). Step 2 adds the default.

- [x] Done *(2026-11-22. RED: the test project did not compile against the missing `AddCloudstrapProxy` /
  `MapCloudstrapProxy` / `HostApplicationBuilderExtensions` (CS1061/CS0103). The pin tests pass on arrival because
  the pin is a Scope item made first. GREEN: `Cloudstrap.Proxy.Tests` 11/11. Full suite: 17 pre-existing unit
  exes at baseline (991), E2E 68/68, 0 code warnings, `dotnet format` exit 0. Release pack:
  `Cloudstrap.Proxy.1.0.0.nupkg` with MIT, icon, README and XML docs. **VERIFY 3 wording**: the nuspec lists
  `Cloudstrap.Extensions` + `Yarp.ReverseProxy` plus Extensions' own pinned closure (Azure/OTel/Serilog/HealthChecks)
  as direct dependencies. That is the repo-wide `CentralPackageTransitivePinningEnabled` packing effect;
  `Cloudstrap.Extensions.1.0.0.nuspec` lists the same set. The only *new* dependencies are Extensions and YARP.
  Deviations: the mechanic (h) fake token providers are deferred to Step 4, their first consumer (no unused test
  types). Step 9's `RepositoryHygieneTests` must also exclude `DependencyPinTests.cs`, which names `Nihdi.` by design,
  as every suite's AC-A3 tripwire does.)*

**Scope**:
- `_plans/17-TrustedSubsystemProxy.md` *(modify)*: the **Baseline record** filled before any code change, and the
  **Pin record** filled (mechanic (c)).
- `src/Directory.Packages.props` *(modify)*: the `Yarp.ReverseProxy` pin, mechanic (c).
- `src/Cloudstrap.Proxy/Cloudstrap.Proxy.csproj` *(create)*:
  - the shipped-package shape of `Cloudstrap.Extensions.csproj` (`GeneratePackageOnBuild`,
    `GenerateDocumentationFile`, MIT, icon, README, SourceLink via the shared props);
  - `ProjectReference` → `Cloudstrap.Extensions`;
  - version-less `PackageReference` → `Yarp.ReverseProxy`;
  - `InternalsVisibleTo` → `Cloudstrap.Proxy.Tests`.
- `src/Cloudstrap.Proxy/HostApplicationBuilderExtensions.cs` *(create)*: `AddCloudstrapProxy(this
  IHostApplicationBuilder builder, Action<CloudstrapProxyOptions>? configure = null)`. It:
  - applies guards;
  - binds `Cloudstrap:Proxy` with `ValidateOnStart`;
  - calls `AddReverseProxy().LoadFromConfig(section)`;
  - returns the builder.

  **A second call throws `InvalidOperationException`** naming the method (spec edge case; planner's pick:
  explicit over silently idempotent).
- `src/Cloudstrap.Proxy/EndpointRouteBuilderExtensions.cs` *(create)*: `MapCloudstrapProxy(this IEndpointRouteBuilder)`
  → `MapReverseProxy()`.
- `src/Cloudstrap.Proxy/CloudstrapProxyOptions.cs` *(create, public sealed)*: `ConfigurationSection` (default
  `"ReverseProxy"`), `DefaultTokenMode`, `StripInboundCredentials`. Only `ConfigurationSection` is consumed in this
  step.
- `src/Cloudstrap.Proxy/CloudstrapProxyOptionsValidator.cs` *(create)*: the #1 source-generated validator pattern,
  `ConfigurationSection` required. **Plus a startup check**: the named section exists, else a message naming
  `Cloudstrap:Proxy:ConfigurationSection` and the missing section (AC-PX10).
- `src/Cloudstrap.Proxy/ProxyTokenMode.cs`, `ProxyMetadataKeys.cs` *(create; public; consumed from Step 3 on)*.
- `src/Cloudstrap.Proxy/README.md` *(create, skeleton; completed in Step 9)*.
- `src/Test/UnitTest/Cloudstrap.Proxy.Tests/` *(create)*: csproj (one `ProjectReference` → `Cloudstrap.Proxy`;
  `Microsoft.AspNetCore.TestHost`, already pinned) and the mechanic (h) harness under `Infrastructure/`.
- `src/Cloudstrap.sln` *(modify)*: the package and the test project in their solution folders.

**RED**:
- `RegistrationTests.cs`:
  - `MapCloudstrapProxy_ConfiguredRoute_ForwardsToTheClusterDestination`: the echo server records the path, and the
    front door answers 200 with the echo body.
  - `MapCloudstrapProxy_UnmatchedPath_IsNotForwarded` (404 at the front door; echo records nothing).
  - `AddCloudstrapProxy_ReturnsYarpsReverseProxyBuilder_SoYarpExtensionsChain`: `.AddTransforms(…)` chained on the
    result takes effect.
  - `AddCloudstrapProxy_CustomConfigurationSection_IsUsed`
  - `AddCloudstrapProxy_ConfigurationSectionMissing_FailsStartNamingTheKey` (AC-PX10)
  - `AddCloudstrapProxy_CalledTwice_ThrowsInvalidOperationException`
  - `AddCloudstrapProxy_NullBuilder_ThrowsArgumentNullException` and `MapCloudstrapProxy_NullEndpoints_…`
- `DependencyPinTests.cs`:
  - `YarpReverseProxy_IsPinned_AtOrAbove_2_3_0`
  - `ProxyPackage_ReferencesNoAspireServiceDiscoveryDuendeOrNihdiAuthPackage` (AC-ASP2, AC-A3, D-8; scans
    `Cloudstrap.Proxy.csproj` and the test csproj).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln   # RED = the suite does not compile against the missing types
  src\Test\UnitTest\Cloudstrap.Proxy.Tests\bin\Debug\net10.0\Cloudstrap.Proxy.Tests.exe --filter "RegistrationTests|DependencyPinTests"
  ```

**GREEN**: the Scope items.

**DB changes**: none.

**VERIFY** *(when all green, mark `Done`, then STOP at Gate 1)*:
1. `Cloudstrap.Proxy.Tests.exe` → all pass. A configured route is forwarded through one registration call.
2. Full-suite check (mechanic (b)): every pre-existing exe at **baseline counts**; zero warnings (including no NU*
   restore warning); `dotnet format` exit 0.
3. `dotnet build src/Cloudstrap.sln -c Release` → `Cloudstrap.Proxy.*.nupkg` produced. Its nuspec lists
   `Cloudstrap.Extensions` and `Yarp.ReverseProxy` only.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE 1: dependency review, `Yarp.ReverseProxy` adopted *(covers Step 1; extra gate after a ⚠️ dependency-update step)*

*Executor: STOP here. Present the results and WAIT for user approval. Do not start Step 2.*

⚠️ **Risk areas at this gate**:
- **Dependency update**: the `Directory.Packages.props` diff and comment; the **filled Pin record** (version,
  license, dependency group, new transitive packages, no restore/audit warnings, YARP facts (c)4 confirmed).
- **Planner interpretations to confirm**: second `AddCloudstrapProxy` call throws (rather than being idempotent);
  Kestrel loopback echo server in unit tests (mechanic (h)).

- [x] Behavioral verification: `RegistrationTests` + `DependencyPinTests` green; every other exe at baseline.
- [x] Code review: csproj closure (Extensions + YARP only); public types sealed and documented; validator pattern.
- [x] User approved: implementation may continue past this gate. *(Approved by the owner 2026-11-22: Yarp.ReverseProxy
  2.3.0, MIT licence, test setup (second call throws; Kestrel loopback echo). D-13 decided at the same time: no
  custom TLS validator in v1, .NET's standard TLS validation; recorded in the spec Decision Log.)*

---

## Step 2: A proxied route with no authorization policy requires a signed-in user, and nothing is forwarded otherwise (AC-PX2; D-4, D-10; mechanic (f) default)

After this step:
- A route without `AuthorizationPolicy` behaves as if it said `Default`. An anonymous caller is challenged by the
  front door and the echo server records nothing.
- A route that says `Anonymous` forwards anonymous callers. The guard against token-bearing clusters comes in
  Step 6; the tests here use clusters with `Cloudstrap.TokenMode: None`, whose metadata key is read from Step 4 on.
- A route with a named policy keeps it.

- [x] Done *(2026-11-22. RED: 3 of the 6 `AuthorizationDefaultTests` failed before the filter existed, each with the
  anonymous caller forwarded (200, echo recorded the request): `RouteWithoutPolicy_AnonymousCaller_Returns401_…`,
  `…_CookieFrontDoor_IsChallenged_…` and `…_AppWithoutFallbackPolicy_StillRequiresASignedInUser`. The other 3 were
  **green on arrival**, as they characterize behavior YARP already has and that the filter must keep:
  `RouteWithoutPolicy_SignedInCaller_IsForwarded`, `RouteWithNamedPolicy_CallerFailingIt_Returns403_…` and
  `RouteMarkedAnonymous_AnonymousCaller_IsForwarded`. GREEN: internal sealed `ProxyRouteConfigFilter` gives a route
  with a null/empty policy YARP's reserved `Default`, registered with `AddConfigFilter` in `AddCloudstrapProxy`.
  `Cloudstrap.Proxy.Tests` 17/17. Full suite: the 17 pre-existing unit exes at baseline (991), E2E 68/68, 0 code
  warnings, `dotnet format` exit 0. The build shows 144 NU190x restore advisories; besides the Pkcs/Xml ones of the
  Baseline record, these now include `Microsoft.AspNetCore.Components.WebAssembly.Server` 10.0.10 on the demo Bff
  (new advisory data, not raised by this step, left for the owner). Also removed: the stray empty
  `ZzYarpFactsProbe.cs` committed by `856e92d`.)*

**Scope**:
- `src/Cloudstrap.Proxy/ProxyRouteConfigFilter.cs` *(create, internal sealed, `IProxyConfigFilter`)*: the
  default-policy half of mechanic (f).
- `HostApplicationBuilderExtensions.cs` *(modify)*: `AddConfigFilter<ProxyRouteConfigFilter>()`.
- `src/Test/UnitTest/Cloudstrap.Proxy.Tests/AuthorizationDefaultTests.cs` *(create)*.

**RED**:
- `RouteWithoutPolicy_AnonymousCaller_Returns401_AndForwardsNothing` (bearer-style test scheme)
- `RouteWithoutPolicy_AnonymousCaller_CookieFrontDoor_IsChallenged_AndForwardsNothing`: a cookie scheme as the
  default challenge → 302 to the login path; the echo records nothing.
- `RouteWithoutPolicy_SignedInCaller_IsForwarded`
- `RouteWithoutPolicy_AppWithoutFallbackPolicy_StillRequiresASignedInUser`: the hole stock YARP leaves (finding 4).
- `RouteWithNamedPolicy_CallerFailingIt_Returns403_AndForwardsNothing`
- `RouteMarkedAnonymous_AnonymousCaller_IsForwarded`
- Failing-run command:
  ```powershell
  src\Test\UnitTest\Cloudstrap.Proxy.Tests\bin\Debug\net10.0\Cloudstrap.Proxy.Tests.exe --filter "AuthorizationDefaultTests"
  ```

**GREEN**: the Scope items.

**DB changes**: none.

**VERIFY**:
1. `Cloudstrap.Proxy.Tests.exe` → all pass.
2. Full-suite check (mechanic (b)): all green at baseline counts; `dotnet format` exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 3: The caller's `Cookie` and `Authorization` never reach the destination; forwarding headers follow YARP's defaults, with a spoofed prefix replaced (AC-PX3; AC-PX8; D-5, D-11, D-14; mechanic (g))

After this step:
- Every proxied request loses the caller's `Cookie` and `Authorization` headers, whatever the hosting platform. No
  environment check exists.
- A route with `Cloudstrap.ForwardInboundCredentials: true`, or the global `StripInboundCredentials = false`, keeps
  them.
- The destination receives `X-Forwarded-For/Proto/Host/Prefix` from YARP. A caller-sent `X-Forwarded-Prefix` is
  replaced by the front door's `PathBase`, and `Host` is the destination's own. This is the source transformer's
  contract, now met by YARP defaults.

- [x] Done *(2026-11-22. First, the Step 2 verify follow-ups: the 3 green-on-arrival `AuthorizationDefaultTests` are
  labelled `[Category("RegressionGuard")]` with a doc comment, and 2 tests are added, both green on arrival and labelled
  the same way: `RouteWithWhitespaceOnlyPolicy_FailsStart` (YARP rejects the blank policy name; the filter does not
  widen it to `Default`) and `ConfigReload_AddedRouteWithoutPolicy_StillRequiresASignedInUser`. RED: the 2 planned
  drivers failed before the transform, each with the header reaching the echo: `ProxiedRequest_CallerCookie_…` and
  `ProxiedRequest_CallerAuthorization_…`. **Green on arrival, labelled `RegressionGuard`**: the 4
  `ForwardedHeaderTests` (YARP defaults, D-14), `ProxiedRequest_OtherHeaders_AreForwardedUnchanged` and both opt-out
  tests (trivially green while nothing stripped; they now guard the opt-outs). GREEN: an `AddTransforms` callback in
  `AddCloudstrapProxy` removes `Cookie` and `Authorization` unless `StripInboundCredentials` is `false` or the route
  metadata `Cloudstrap.ForwardInboundCredentials` is case-insensitive `"true"`. No environment/platform check exists
  (D-11). README: default-authorization, stripping/opt-outs ("unsafe") and forwarding-header sections.
  `Cloudstrap.Proxy.Tests` 28/28. Full suite: 17 pre-existing unit exes at baseline (991), E2E 68/68, 0 code warnings
  (144 NU190x advisories unchanged), `dotnet format` exit 0.)*

**Scope**:
- `src/Cloudstrap.Proxy/HostApplicationBuilderExtensions.cs` *(modify)*: the mechanic (g) transform.
- `src/Test/UnitTest/Cloudstrap.Proxy.Tests/InboundCredentialTests.cs`, `ForwardedHeaderTests.cs` *(create)*.

**RED**:
- `InboundCredentialTests.cs`:
  - `ProxiedRequest_CallerCookie_IsAbsentDownstream`
  - `ProxiedRequest_CallerAuthorization_IsAbsentDownstream` (the test scheme's own `Authorization: Test …` header
    signs the caller in at the front door and must not arrive)
  - `ProxiedRequest_OtherHeaders_AreForwardedUnchanged` (e.g. `Accept-Language`, a custom `X-Demo` header)
  - `RouteOptOut_ForwardInboundCredentialsTrue_KeepsBothHeaders`
  - `GlobalOptOut_StripInboundCredentialsFalse_KeepsBothHeaders`
- `ForwardedHeaderTests.cs`:
  - `PathPrefixedRoute_Destination_ReceivesXForwardedPrefixFromPathBase` (`UsePathBase("/front")` + a route with
    `PathRemovePrefix`)
  - `CallerSentXForwardedPrefix_IsReplaced_NotPassedThrough`
  - `Destination_ReceivesXForwardedForProtoAndHost`
  - `Destination_HostHeader_IsTheDestinationsOwn`
- Failing-run command:
  ```powershell
  src\Test\UnitTest\Cloudstrap.Proxy.Tests\bin\Debug\net10.0\Cloudstrap.Proxy.Tests.exe --filter "InboundCredentialTests|ForwardedHeaderTests"
  ```
  The `ForwardedHeaderTests` are expected to be **green on arrival**: they characterize YARP defaults. The executor
  labels them as such in the step report. They are permanent guards of the #18 contract (D-14). The RED of this
  step is the `InboundCredentialTests` cookie/authorization pair.

**GREEN**: the Scope items.

**DB changes**: none.

**VERIFY**:
1. `Cloudstrap.Proxy.Tests.exe` → all pass.
2. Full-suite check (mechanic (b)): all green at baseline counts; `dotnet format` exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE 2: end of Slice 1, the security core *(covers Steps 2–3)* ⚠️ AUTH

*Executor: STOP here. Present the results of Steps 2–3 and WAIT for user approval.*

⚠️ **Risk areas at this gate**:
- **Auth / credential handling**:
  - the default-policy filter;
  - the stripping transform and its two opt-outs;
  - evidence that the cookie front door's challenge forwards nothing;
  - confirmation that no environment/platform detection exists (D-11).
- **Characterization tests**: the four `ForwardedHeaderTests` green on arrival (YARP defaults, D-14).

- [x] Behavioral verification: `AuthorizationDefaultTests`, `InboundCredentialTests`, `ForwardedHeaderTests` green;
  every other exe at baseline.
- [x] Code review: filter + transform code; README notes on the opt-outs ("unsafe for the trusted-subsystem pattern").
- [x] User approved: implementation may continue past this gate. *(Approved by the owner 2026-11-22. A blank
  policy name keeps failing startup (Central's decision).)*

---

## Slice 2: The downstream API receives the application's token per cluster, and an anonymous route can never carry it ⚠️ AUTH + PUBLIC API

---

## Step 4: A `Client` cluster forwards with the application's token from the client-credentials seam, and fails loudly without a provider (AC-PX4; D-12; mechanics (d), (e))

After this step:
- Clusters default to `Client`. Each forwarded request carries `Authorization: Bearer <app token>` from
  `IClientAccessTokenHandlerProvider.CreateClientTokenHandler(clusterId, tokenRequest)`.
- No provider registered → startup fails with a message naming the cluster, `Cloudstrap.TokenMode` and
  `Cloudstrap.Authentication.ClientCredentials`. No request is ever sent without the token.
- A failing token handler → the front door answers 502 and the destination records nothing.
- A downstream 401 passes through unchanged.

- [x] Done *(2026-11-22. First, the Step 3 verify follow-ups, both green on arrival and labelled `RegressionGuard`:
  `ProxiedRequest_CallerProxyAuthorization_IsAbsentDownstream` (YARP drops it as hop-by-hop; no fix needed) and
  `ConfigReload_AddedRoute_StripsCookieAndAuthorization`; README notes that an app transform registered after
  `AddCloudstrapProxy` can re-add the headers. RED: all 9 `ClientTokenModeTests` failed before the factory existed
  (no token downstream; start not failing without a provider or with a pre-set `InnerHandler`; 200 instead of 502).
  The plan's parameters test is split in two (`…_AndItsTokenRequestParameters` + `…_NoTokenRequestParameters_
  ProviderIsAskedWithNull`). GREEN: internal sealed `CloudstrapForwarderHttpClientFactory` replaces YARP's factory;
  `WrapHandler` puts the seam's handler (resolved lazily, cluster id as client name) in front of YARP's handler;
  missing provider → `InvalidOperationException` naming the cluster, `Cloudstrap.TokenMode` and
  `Cloudstrap.Authentication.ClientCredentials`, which fails startup; a pre-set `InnerHandler` fails the same way.
  `CanReuseOldClient` also compares the token-mode metadata. `ProxyClusterTokenOptions` reads mechanic (d). Scope
  note: `None` attaches nothing (needed by the existing suites, whose `ProxyConfig.SingleRoute` now defaults the
  cluster to `None`); `User` and unparsable values fail start with a generic "not supported" message until Step 5.
  The reload test chains `LoadFromMemory` and uses `InMemoryConfigProvider.Update`. `Cloudstrap.Proxy.Tests` 39/39.
  Full suite: 17 pre-existing unit exes at baseline (991), E2E 68/68, 0 code warnings (144 NU190x advisories
  unchanged), `dotnet format` exit 0.)*

**Scope**:
- `src/Cloudstrap.Proxy/CloudstrapForwarderHttpClientFactory.cs` *(create, internal sealed)*: mechanic (e), with
  `Client` and the default mode only.
- `src/Cloudstrap.Proxy/ProxyClusterTokenOptions.cs` *(create, internal)*: the mechanic (d) binding.
- `HostApplicationBuilderExtensions.cs` *(modify)*: register the factory (replace YARP's).
- `src/Test/UnitTest/Cloudstrap.Proxy.Tests/ClientTokenModeTests.cs` *(create)*.

**RED**:
- `ClientCluster_ForwardedRequest_CarriesTheApplicationToken` (echo sees `Bearer app:{clusterId}`)
- `ClusterWithoutTokenMode_DefaultsToClient`
- `ClientCluster_ProviderIsAskedWithTheClusterId_AndItsTokenRequestParameters` (mechanic (d); `null` when the
  subsection is absent)
- `ClientCluster_NoProviderRegistered_StartFailsNamingClusterKeyAndPackage`
- `ClientCluster_TokenHandlerThrows_Returns502_AndForwardsNothing` (spec edge case: no unauthenticated retry)
- `ClientCluster_Downstream401_IsPassedThrough`
- `ConfigReload_AddedClientCluster_AlsoCarriesTheToken` (`InMemoryConfigProvider.Update`, spec edge case)
- `ClientCluster_ProviderReturnsHandlerWithInnerHandlerSet_IsNotSupported`: assert the documented failure, mirroring
  #4's seam contract.
- Failing-run command:
  ```powershell
  src\Test\UnitTest\Cloudstrap.Proxy.Tests\bin\Debug\net10.0\Cloudstrap.Proxy.Tests.exe --filter "ClientTokenModeTests"
  ```

**GREEN**: the Scope items.

**DB changes**: none.

**VERIFY**:
1. `Cloudstrap.Proxy.Tests.exe` → all pass.
2. Full-suite check (mechanic (b)): all green at baseline counts; `dotnet format` exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 5: `User` and `None` clusters, and an unknown token mode fails start (AC-PX6; AC-PX10 token mode; D-6)

After this step:
- `User` attaches the signed-in user's token through `IUserAccessTokenHandlerProvider`, with the same fail-loud rule
  naming `Cloudstrap.Authentication.OpenIdConnect`.
- `None` attaches nothing. The caller's `Authorization` is still stripped unless the route opts out.
- An unknown `Cloudstrap.TokenMode` value or `DefaultTokenMode` fails start, listing `None`, `Client`, `User`.
- No user-identity header is ever added in any mode (D-6).

- [ ] Done

**Scope**:
- `CloudstrapForwarderHttpClientFactory.cs` *(modify)*: the `User` / `None` branches; metadata parsing
  (case-insensitive `Enum.TryParse`, numeric values rejected).
- `CloudstrapProxyOptionsValidator.cs` *(modify)*: `DefaultTokenMode` defined.
- `src/Test/UnitTest/Cloudstrap.Proxy.Tests/TokenModeTests.cs` *(create)*.

**RED**:
- `UserCluster_ForwardedRequest_CarriesTheUserToken`
- `UserCluster_NoUserProviderRegistered_StartFailsNamingThePackage`
- `NoneCluster_ForwardedRequest_HasNoAuthorizationHeader`
- `NoneCluster_CallerAuthorization_IsStillStripped`
- `UnknownTokenModeMetadata_StartFailsListingTheAllowedValues` (`[TestCase("Machine")]`, `[TestCase("1")]`)
- `UnknownDefaultTokenMode_ValidateOnStartFailsNamingTheKey`
- `AnyMode_ForwardedRequest_CarriesNoUserIdentityHeader`: the echo's header set minus the caller's own headers and
  YARP's `X-Forwarded-*` adds only `Authorization` (Client/User) and the correlation header.
- Failing-run command:
  ```powershell
  src\Test\UnitTest\Cloudstrap.Proxy.Tests\bin\Debug\net10.0\Cloudstrap.Proxy.Tests.exe --filter "TokenModeTests"
  ```

**GREEN**: the Scope items.

**DB changes**: none.

**VERIFY**:
1. `Cloudstrap.Proxy.Tests.exe` → all pass.
2. Full-suite check (mechanic (b)): all green at baseline counts; `dotnet format` exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 6: An anonymous route on a token-bearing cluster is refused at startup unless explicitly overridden (AC-PX5; D-4; mechanic (f) guard)

After this step:
- A route with `AuthorizationPolicy: Anonymous` whose cluster's effective mode is `Client` or `User` fails start,
  naming the route, the cluster and `Cloudstrap.AllowAnonymousWithToken`.
- With that route metadata set to `true`, it starts and forwards with the app token (a deliberate public route).
- `Anonymous` + `None` starts normally.

- [ ] Done

**Scope**:
- `ProxyRouteConfigFilter.cs` *(modify)*: the guard half of mechanic (f). If YARP does not fail start on a filter
  exception, add the internal startup check (mechanic (f)) and record the choice for Gate 3.
- `src/Test/UnitTest/Cloudstrap.Proxy.Tests/ConfusedDeputyGuardTests.cs` *(create)*.

**RED**:
- `AnonymousRoute_ClientCluster_StartFailsNamingRouteClusterAndOverride`
- `AnonymousRoute_UserCluster_StartFails` (spec edge case "`User` outside a user context")
- `AnonymousRoute_DefaultModeCluster_StartFails` (default `Client`, no metadata)
- `AnonymousRoute_ClientCluster_WithOverride_StartsAndForwardsWithTheAppToken`
- `AnonymousRoute_NoneCluster_Starts`
- `OverrideValueNotTrue_IsIgnored` (`"yes"`, `"1"` → still fails; only a case-insensitive `"true"` counts)
- Failing-run command:
  ```powershell
  src\Test\UnitTest\Cloudstrap.Proxy.Tests\bin\Debug\net10.0\Cloudstrap.Proxy.Tests.exe --filter "ConfusedDeputyGuardTests"
  ```

**GREEN**: the Scope items.

**DB changes**: none.

**VERIFY**:
1. `Cloudstrap.Proxy.Tests.exe` → all pass.
2. Full-suite check (mechanic (b)): all green at baseline counts; `dotnet format` exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE 3: end of Slice 2, token modes + confused-deputy guard *(covers Steps 4–6)* ⚠️ AUTH + PUBLIC API

*Executor: STOP here. Present the results of Steps 4–6 and WAIT for user approval.*

⚠️ **Risk areas at this gate**:
- **Auth**:
  - the handler chain;
  - every fail-loud message (text shown);
  - the 502-without-retry behavior;
  - the guard and its override;
  - the mechanism chosen for the guard (filter vs startup check, mechanic (f)).
- **Public API one-way doors**, all now in place:
  - `AddCloudstrapProxy` / `MapCloudstrapProxy`, `CloudstrapProxyOptions`, `ProxyTokenMode`;
  - the `ProxyMetadataKeys` constants `Cloudstrap.TokenMode`, `Cloudstrap.ForwardInboundCredentials`,
    `Cloudstrap.AllowAnonymousWithToken`;
  - the `Cloudstrap:Proxy:Clusters:{clusterId}:TokenRequestParameters` key.
- **D-13** (not part of the owner's OQ answers): confirm that the source's custom TLS validator stays out of v1.

- [ ] Behavioral verification: `ClientTokenModeTests`, `TokenModeTests`, `ConfusedDeputyGuardTests` green; every
  other exe at baseline.
- [ ] Code review: no Duende / auth-package reference; the seam is resolved lazily; no resilience handler.
- [ ] User approved: implementation may continue past this gate.

---

## Slice 3: The proxied hop composes with Cloudstrap observability and YARP service discovery, and the package is complete

---

## Step 7: Correlation and trace context flow on the proxied hop, and Cloudstrap adds no resilience or exporter (AC-PX7; AC-PX9; mechanic (e) correlation)

After this step:
- The destination receives the Cloudstrap correlation header with the front door's ambient id, in the header
  `CorrelationOptions` names. Set-if-absent: a caller-sent value is kept, exactly like #4's typed clients.
- `traceparent` continues the front door's trace.
- A host that configured `ConfigureHttpClientDefaults` resilience keeps it for its typed clients. The forwarder gets
  no resilience handler and no second OTel pipeline.

- [ ] Done

**Scope**:
- `CloudstrapForwarderHttpClientFactory.cs` *(modify)*: the correlation handler as the outermost wrapper.
- `src/Test/UnitTest/Cloudstrap.Proxy.Tests/ObservabilityCompositionTests.cs` *(create)*.

**RED**:
- `ProxiedRequest_CarriesTheFrontDoorsCorrelationId`
- `ProxiedRequest_CustomCorrelationHeaderName_IsHonoured`
- `ProxiedRequest_Traceparent_ContinuesTheFrontDoorsTrace` (an `ActivityListener`; the echo's `traceparent` trace id
  equals the inbound one)
- `ConfigureHttpClientDefaultsResilience_IsNotAppliedToTheForwarder` (the forwarder chain built by the factory
  contains only correlation → token → YARP handler)
- `AddCloudstrapProxy_RegistersNoTracerOrMeterProviderOrExporter`: the service collection gains no `TracerProvider`
  / `MeterProvider` / exporter registration compared with a host without it.
- Failing-run command:
  ```powershell
  src\Test\UnitTest\Cloudstrap.Proxy.Tests\bin\Debug\net10.0\Cloudstrap.Proxy.Tests.exe --filter "ObservabilityCompositionTests"
  ```
  The `traceparent` and "no exporter" tests may be green on arrival (YARP behavior / absence). They are labelled as
  characterization tests in the step report.

**GREEN**: the Scope items.

**DB changes**: none.

**VERIFY**:
1. `Cloudstrap.Proxy.Tests.exe` → all pass.
2. Full-suite check (mechanic (b)): all green at baseline counts; `dotnet format` exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 8: Destinations resolved through YARP's destination-resolver hook keep every Cloudstrap behavior (AC-PX12; D-8)

After this step:
- A test-local `IDestinationResolver` registered on the builder `AddCloudstrapProxy()` returns maps a logical
  destination (`http://downstream-api`) to the echo server's loopback address.
- Forwarding reaches it with the token attached, credentials stripped and the authorization default applied.
- This is the seam `Microsoft.Extensions.ServiceDiscovery.Yarp` uses. Neither it nor any `Aspire.*` package is
  referenced.

- [ ] Done

**Scope**:
- `src/Test/UnitTest/Cloudstrap.Proxy.Tests/Infrastructure/StaticDestinationResolver.cs`,
  `DestinationResolverCompositionTests.cs` *(create)*. No production change is expected. If one is needed (e.g. the
  factory ignores resolved destinations), the executor makes it in this step and reports it.

**RED**:
- `ResolvedDestination_IsForwardedTo_WithTheApplicationToken`
- `ResolvedDestination_CallerCredentials_AreStillStripped`
- `ResolvedDestination_AnonymousCaller_IsStillRejected`
- Failing-run command:
  ```powershell
  src\Test\UnitTest\Cloudstrap.Proxy.Tests\bin\Debug\net10.0\Cloudstrap.Proxy.Tests.exe --filter "DestinationResolverCompositionTests"
  ```
  RED here is the missing test-local resolver type (the suite does not compile). The behaviors are expected to hold
  once it exists. The executor reports whether any production change was required.

**GREEN**: the Scope items.

**DB changes**: none.

**VERIFY**:
1. `Cloudstrap.Proxy.Tests.exe` → all pass.
2. Full-suite check (mechanic (b)): all green at baseline counts; `dotnet format` exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 9: The package surface is documented, sealed and free of enterprise identifiers, and its README explains the trusted-subsystem rules (AC-PX11; AC-PX13; AC-ASP2; AC-A3)

After this step:
- `Cloudstrap.Proxy` exposes exactly the intended public types, all sealed (or static/enum) with XML docs.
- The README covers:
  - the trusted-subsystem pattern;
  - the default authorization and the guard;
  - the stripping rule and its "unsafe" opt-outs;
  - token modes and the packages each needs;
  - the `TokenRequestParameters` key;
  - "one owner" for destination resolution (Cloudstrap's YARP config **or** Aspire service discovery on the same
    builder);
  - WebSocket/SSE upgrade (token attached on the upgrade request; spec edge case, README note);
  - that replacing `IForwarderHttpClientFactory` takes over the handler chain;
  - the D-13 note (platform TLS validation; no custom validator).
- Permanent tripwires guard the surface and the vocabulary.

- [ ] Done

**Scope**:
- `src/Cloudstrap.Proxy/README.md` *(complete)*.
- `src/Test/UnitTest/Cloudstrap.Proxy.Tests/PackageSurfaceTests.cs`, `RepositoryHygieneTests.cs` *(create)*.
- Any public member found undocumented or unsealed *(modify)*.

**RED**:
- `PackageSurfaceTests.cs`:
  - `PublicTypes_AreExactlyTheIntendedSurface`: `HostApplicationBuilderExtensions`,
    `EndpointRouteBuilderExtensions`, `CloudstrapProxyOptions`, `ProxyTokenMode`, `ProxyMetadataKeys`.
  - `PublicClasses_AreSealedOrStatic`
  - `ProxyMetadataKeys_HaveTheSpecifiedValues` (the three strings, pinned as a one-way door).
  - `PackageReadme_CoversTheTrustedSubsystemRules`: the README mentions `Cookie`, `Authorization`,
    `AllowAnonymousWithToken`, `ForwardInboundCredentials`, `TokenMode`, `Cloudstrap.Authentication.ClientCredentials`,
    `IDestinationResolver`. Fails on the Step 1 skeleton.
- `RepositoryHygieneTests.cs`:
  - `ProxyFiles_CarryNoEnterpriseIdentifiers`: case-insensitive `Nihdi|Riziv|Wfe|Cfe` over `src/Cloudstrap.Proxy/**`
    and the test project (excluding this file) → zero.
  - `ProxyPackageClosure_HasNoAspireOrNihdiAuthPackage`: walks `obj/project.assets.json` of `Cloudstrap.Proxy` for
    `Aspire.*`, `Microsoft.Extensions.ServiceDiscovery*`, `Nihdi.AspNetCore.*` → zero (AC-ASP2, AC-A3, D-8).
- Failing-run command:
  ```powershell
  src\Test\UnitTest\Cloudstrap.Proxy.Tests\bin\Debug\net10.0\Cloudstrap.Proxy.Tests.exe --filter "PackageSurfaceTests|RepositoryHygieneTests"
  ```

**GREEN**: the Scope items.

**DB changes**: none.

**VERIFY**:
1. `Cloudstrap.Proxy.Tests.exe` → all pass.
2. Full-suite check (mechanic (b)): all green at baseline counts; `dotnet format` exit 0.
3. `dotnet build src/Cloudstrap.sln -c Release` → the `.nupkg` contains the README, the icon and the XML docs, and
   declares `MIT`.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE 4: end of Slice 3, composition + package complete *(covers Steps 7–9)*

*Executor: STOP here. Present the results of Steps 7–9 and WAIT for user approval.*

⚠️ **Risk areas at this gate**:
- **Aspire overlap**: the resolver test and the closure tripwire; no Aspire or ServiceDiscovery reference anywhere.
- **Docs**: README wording on the unsafe opt-outs, the "one owner" note and D-13.
- **Characterization tests**: list the ones green on arrival (Step 7).

- [ ] Behavioral verification: `ObservabilityCompositionTests`, `DestinationResolverCompositionTests`,
  `PackageSurfaceTests`, `RepositoryHygieneTests` green; every other exe at baseline.
- [ ] Code review: the handler chain order; the package surface; the README.
- [ ] User approved: implementation may continue past this gate.

---

## Slice 4: The demo Bff proxies a route to the Api with its own token, proven end to end (rule 9)

---

## Step 10: The BlazorWasm Bff forwards `/proxy/api/**` to the Api in `Client` mode; a signed-in browser sees the machine identity, an anonymous call is stopped at the Bff, and the Bff cookie never reaches the Api (AC-PX14; D-7)

After this step:
- The Bff (5300) calls `builder.AddCloudstrapProxy()` and `app.MapCloudstrapProxy()`. Its `appsettings.json` has a
  `ReverseProxy` section: route `/proxy/api/{**catch-all}` with `PathRemovePrefix: /proxy/api` → cluster `demo-api`,
  destination `http://127.0.0.1:5330/`, `Cloudstrap.TokenMode: Client`.
- The token is the existing `demo-bff` machine token (scope `selfapi`, the `Cloudstrap:ClientCredentials` defaults,
  no per-cluster parameters needed).
- The Api gains one authenticated demo endpoint, `GET api/v1/downstream/inbound`. It reports whether a `Cookie`
  header arrived and the scheme of `Authorization`, and is used only to observe AC-PX14 (c).

- [ ] Done

**Scope**:
- `src/demo/BlazorWasm/Bff/Cloudstrap.Demo.BlazorWasm.Bff.csproj` *(modify)*:
  `ProjectReference` → `Cloudstrap.Proxy`.
- `src/demo/BlazorWasm/Bff/Program.cs`, `appsettings.json` *(modify)*: registration, mapping, `ReverseProxy`
  section with a `//` comment explaining the trusted-subsystem hop.
- `src/demo/Api/Controllers/DownstreamController.cs` *(modify)*: the `inbound` action.
- `src/demo/Shared/Contracts/DownstreamInboundDto.cs` *(create)*: `record DownstreamInboundDto(bool CookiePresent,
  string? AuthorizationScheme, string Host)`.
- `src/demo/README.md` *(modify)*: the Bff feature row gains "trusted-subsystem proxy (#17)"; architecture note for
  `/proxy/api`.
- `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/TrustedSubsystemProxyTests.cs` *(create)*.

**RED**:
- `TrustedSubsystemProxyTests.cs`:
  - `SignedInBrowser_ProxiedWhoAmI_ReturnsTheMachineIdentity_NotTheUser`. The test signs in through
    `BrowserSignIn.SignInAsync`, then `fetch('/proxy/api/api/v1/downstream/whoami')` from the page. It asserts
    `clientId == "demo-bff"`, `host == "demo-api"`, and that `subject` is not the signed-in user's subject (AC-PX14
    (a)).
  - `AnonymousCall_ToTheProxiedRoute_IsStoppedAtTheBff`: a plain `HttpClient` with no redirects gets a 401 or a 302
    challenge to the IdP, and the body carries no `demo-api` marker (AC-PX14 (b)).
  - `SignedInBrowser_ProxiedInbound_ShowsNoCookieAndTheBearerScheme`: `cookiePresent == false`,
    `authorizationScheme == "Bearer"` (AC-PX14 (c)).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\E2E\Cloudstrap.Demo.E2E.Tests\bin\Debug\net10.0\Cloudstrap.Demo.E2E.Tests.exe --filter "TrustedSubsystemProxyTests"
  ```
  The tests fail with 404 at the Bff before the proxy is wired.

**GREEN**: the Scope items.

**DB changes**: none.

**VERIFY**:
1. `TrustedSubsystemProxyTests` → 3 pass.
2. Full-suite check (mechanic (b)): every unit exe at its count (baseline + `Cloudstrap.Proxy.Tests`); E2E =
   baseline + 3, all green; `dotnet format` exit 0.
3. Manual evidence (recorded): `dotnet run` the IdP, Api and Bff. Sign in at `http://127.0.0.1:5300/` and open
   `/proxy/api/api/v1/downstream/whoami` → `demo-bff`.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE: final, deliverable #17 complete *(covers Step 10; closes the deliverable)*

*Executor: STOP here. Present the results and WAIT for user approval. Any Git push afterwards requires the user's
explicit go-ahead.*

⚠️ **Risk areas at this gate**:
- **Auth, live**: the three E2E results; the Bff `appsettings.json` route/cluster.
- **Shared contract with #18**: path-prefixed route + `X-Forwarded-Prefix` (Step 3), `Client` mode (Step 4),
  authenticated by default (Step 2). #18 rebuilds the source's `/hangfire` role-policy route on these (D-15).
- **Regression**: per-executable counts, baseline vs final.

- [ ] Behavioral verification: `TrustedSubsystemProxyTests` 3/3; `Cloudstrap.Proxy.Tests` all green.
- [ ] Regression: full-suite check green end to end; every pre-existing unit exe at baseline; E2E = baseline + 3.
- [ ] Spec acceptance sign-off:
  - walk AC-PX1…AC-PX14, AC-ASP2 and AC-A3 against the step evidence using the AC coverage map;
  - grep evidence: case-insensitive `Nihdi|Riziv|Wfe|Cfe` over every file added or touched by #17 → zero;
  - confirm nothing Out of Scope was resurrected: Cloudstrap route schema, TLS validator, environment detection,
    identity header, Hangfire proxy, token exchange, resilience, Aspire/ServiceDiscovery reference, shipped-package
    change.
- [ ] Docs review: package README; `src/demo/README.md`. **Recorded follow-ups (not in this plan)**:
  - #9 `ClientCredentialsAccessTokenHandlerProvider` warnings name `Cloudstrap:HttpClients:{name}` even for a proxy
    cluster (spec finding 6);
  - CLAUDE.md "Project Structure" already lists `Cloudstrap.Proxy/`, so no change is needed there;
  - #18 hand-off: the facts recorded in spec D-15.
- [ ] User approved: deliverable #17 is done; the project-manager flips the ROADMAP row to ✅.
