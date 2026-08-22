# Cloudstrap.Demo.BlazorWasm

The Blazor WebAssembly demo — the suite's flagship app and its most complete E2E test bed:
a WASM SPA ([Client](Client/), pages in the [Presentation](Presentation/) RCL) served by the
[Bff](Bff/) on **http://127.0.0.1:5300**, which is simultaneously the cookie-secured BFF surface
(interactive OIDC), a JWT bearer surface (per-endpoint pin), and the client of two typed
outbound hops (machine-token self-call + user-token call to the Api demo host on 5330).

## Feature matrix

| Feature | The Cloudstrap call | Proven by (E2E) |
|---|---|---|
| Fail-fast validated `Cloudstrap:` options, server- and WASM-side (#1) | `GetCloudstrapOptions()` / `AddCloudstrapCore()` | `DiagnosticsPage_Loads_ShowsServerBoundCloudstrapOptions` · `Startup_MissingSystemName_FailsFastWithValidationError` |
| Serilog + OTel pipeline, ambient correlation, tagged probes (#2) | `UseCloudstrapObservability()` | `ApiRequest_WithCorrelationHeader_AmbientCorrelationEchoesIt` · `Healthz_Get_Returns200Healthy` |
| Business spans land in the exported telemetry (#2) | `IBusinessTrace` | `AddDoctor_EmitsBusinessTraceInConsoleTelemetry` |
| Application Insights export mode, fail-fast + per-environment flip (#3) | `.AddAzureMonitor(…)` | `AzureMonitorMode_SutBoots_AndDiagnosticsShowsAzureMonitorMode` · `Startup_ModeFlippedToConsole_UnchangedCodeBootsAndServes` |
| Config-driven typed HttpClients: correlation hop + dependency readiness (#4) | `AddCloudstrapHttpServiceClient<ISelfApiClient,…>("SelfApi")` | `Outbound_TypedClientHop_PropagatesTheCallersCorrelationId` · `Ready_WithUnreachablePeerConfigured_Returns503WhileHealthzStays200` |
| One-call versioned API pipeline: OpenAPI per version, Scalar, problem details, security headers (#5) | `AddCloudstrapWebApi()` / `UseCloudstrapWebApi(…)` | `VersionedEndpoint_Get_ReturnsPayloadAndReportsItsSupportedVersion` · `Error_Get_ReturnsProblemDetailsWithoutExceptionDetail` · `ScalarPage_Loads_InTheBrowser` |
| Machine-to-machine tokens on a flagged client, cached (#9) | `AddCloudstrapClientCredentials()` + `AddClientAccessToken: true` | `MachineCall_ThroughTheFlaggedClient_Returns200WithATokenIssuedByTheTestIdp` · `MachineCall_Twice_ReusesTheCachedToken` |
| Interactive login: auth-code + PKCE, `__Host-` cookie, RP-initiated logout (#10) | `AddCloudstrapOpenIdConnect()` + `MapCloudstrapAuthenticationEndpoints()` | `Login_ThroughTheBrowser_SignsTheUserInAndIssuesTheHardenedCookie` · `Logout_EndsBothTheLocalAndTheProviderSession` |
| Cookie/bearer coexistence — browsers challenged, machine callers 401 (#10) | per-endpoint scheme pin | `AnonymousBrowser_IsChallengedWhileTheMachineEndpointStill401s` |
| The user's token crosses to a separate JWT host (#27) | `UserApi` flagged `AddUserAccessToken` + `AddClientAccessToken`, base address → the Api demo host | `UserCall_SignedIn_ProvesTheApiHostValidatedTheUsersToken` |
| Secured feature with auto-triggered login (`/doctors`) | class-level `[Authorize]` + SUT-local challenge shaping | `DoctorsPage_AnonymousNavigation_AutoTriggersLoginAndShowsDoctors` · `GetDoctors_AnonymousApiGet_Returns401` |
| Convention-registered ViewModel + consumer-owned error handler (#11) | `AddCloudstrapBlazorCommon<IDoctorsViewModel>()` + explicit `IErrorHandler` registration | `AddDoctor_WithBlankName_ShowsTheConsumersErrorHandlerSnackbar` · `DoctorsPage_Loads_ShowsSeededDoctors` (the VM-rendered proof) |

## Harness notes

- **The Bff's entire request pipeline is one `UseCloudstrapWebApi` call.** The Blazor composition
  rides on its documented hook points: `BeforeRouting` carries
  `UseBlazorFrameworkFiles()` + `UseStaticFiles()`, `ConfigureEndpoints` carries the auth
  endpoints and `MapFallbackToFile("index.html")` — `HomePage_Loads_ShowsWelcomeHeadingAndNoConsoleErrors`
  proves the static-file branch survived.
- **The application posture stays opt-in**: both `RequireAuthenticatedEndpoints` switches are
  `false` (the documented whole-application opt-outs), so anonymous pages/status/probes stay
  anonymous and the machine, user and doctors surfaces opt back in with `[Authorize]`. The Api
  demo host demonstrates the opposite (hardened default) posture.
- **`api/v1/machine/status` is pinned to the Bearer scheme** — the documented per-endpoint
  override that keeps the #9 contract (tokenless call → 401, never a login redirect) intact in a
  cookie-default host.
- **`UserApi` demonstrates AC-CC13 live**: flagged both `AddUserAccessToken` and
  `AddClientAccessToken`, its calls terminate on the Api demo host — the response's `demo-api`
  marker is the cross-process proof. `EnableHealthCheck: true` couples the Bff's `/ready` to that
  peer (see the suite README's standalone-run note). `SelfApi` keeps its machine-only flag and
  still calls the Bff itself.
- **`IncludeDetails` is pinned `false`** so the hardened problem-details shape is what the E2E
  asserts in Development; one test boots a second instance with the switch flipped.
- **Anonymous API callers get a bare 401, never a login redirect** — SUT-local challenge shaping
  via the documented `CloudstrapOpenIdConnectConfigurator` hook (`Accept: text/html` heuristic);
  browser navigations keep redirecting, which powers the `/doctors` auto-trigger. This and the
  `UserStateDto`/`user/state` probe are placeholder code deliverable #13 replaces.
- **The doctors page is the ViewModel-pattern demonstration (#11)**: `DoctorsViewModel` is
  convention-registered by `AddCloudstrapBlazorCommon<IDoctorsViewModel>()` (the `ViewModel`
  suffix), initialized through `IViewModel.InitializeAsync`, and routes failures to the consumer's
  `SnackbarErrorHandler` (registered explicitly — its name ends in `Handler`, outside the scan).
  Navigation stays in the page: it injects `NavigationManager` directly, the D-3 posture.
- **Scalar assertions are shell-based** (the reference UI pulls its bundle from a CDN CI may not
  reach), so `ScalarPage_Loads_InTheBrowser` asserts the shell only.
- **A manual `dotnet run` without peers still boots** — token acquisition and metadata retrieval
  are lazy; `/healthz` answers while sign-in and outbound calls fail loudly naming the missing
  peer (see the suite README's port map and readiness note).

## Running

```powershell
dotnet run --project src/demo/Shared/IdentityProvider    # sign-in needs the IdP (5310)
dotnet run --project src/demo/Api                        # user-token relay peer (5330)
dotnet run --project src/demo/BlazorWasm/Bff             # 5300; -lp https → https://localhost:7200
```

Browse `/doctors` — login auto-triggers; sign in as `geobarteam` / `password`.
