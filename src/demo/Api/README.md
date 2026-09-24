# Cloudstrap.Demo.Api

A pure JSON API host on **http://127.0.0.1:5330** — the downstream peer of the trusted-subsystem
demo, and the live proof of `Cloudstrap.WebApi`'s **hardened-by-default** posture:
`RequireAuthenticatedEndpoints` is deliberately **not** set anywhere in this project, so it stays
at its `true` default and every endpoint demands a validated JWT through the fallback policy —
there is no `[Authorize]` attribute in the whole app. Only the health probes stay anonymous (the
probe carve-out).

The entire host is [Program.cs](Program.cs): fail-fast options binding, bootstrap logging,
Console-mode observability, `AddCloudstrapWebApi()` + `AddCloudstrapJwtBearer()`, one `self`
health check, `UseCloudstrapWebApi()` with no hooks. That's the teaching point — how little a
hardened versioned API host needs.

## Feature matrix

| Feature | The Cloudstrap call | Proven by (E2E) |
|---|---|---|
| Authenticated **by default** — anonymous callers get 401 with no `[Authorize]` anywhere (#5) | `AddCloudstrapJwtBearer()` with `RequireAuthenticatedEndpoints` left at `true` | `ApiHost_AnonymousWhoAmI_Returns401` |
| Probe carve-out coexists with the hardened default (#5/#4) | `UseCloudstrapWebApi()` maps `/healthz`/`/ready` anonymously | `ApiHost_AnonymousHealthz_Returns200` |
| Cross-process user-token validation — this host's `demo-api` marker proves the hop (#9/#10 plumbing) | `GET api/v1/downstream/whoami` echoes the validated claims | `UserCall_SignedIn_ProvesTheApiHostValidatedTheUsersToken` |
| The HTTP-path transactional outbox: `POST api/v1/orders` stages a row and sends `PlaceOrderCommand` to the Worker over SQL Server in one transaction, dispatch after commit; the correlation id flows across (#14) | `AddCloudstrapMessaging().UseSqlServer().AddCloudstrapTransactionalMessaging<DemoDbContext>()` + `IDbContextOutbox<DemoDbContext>` in `OrdersController` | `Messaging_OrderPlacedThroughTheApiOutbox_IsProcessedByTheWorker_WithTheCorrelationIdObserved` |
| The hardened default still gates the new endpoint (#5/#14) | no `[Authorize]` on `OrdersController` | `Messaging_AnonymousOrdersPost_Returns401` |
| Large messages: an order whose `notes` push `PlaceOrderCommand` over 200 KiB travels to the Worker as one blob in the `demo-claimcheck` container plus a reference; a small order adds no blob (#15) | `AddCloudstrapBlobStorage()` + `AddCloudstrapMessaging()....UseAzureBlobClaimCheck()` | `ClaimCheck_OrderWithNotesAboveTheThreshold_IsProcessedByTheWorker_WithLengthAndHashRecorded_AndExactlyOneNewBlobInTheClaimCheckContainer`, `ClaimCheck_OrderWithNotesBelowTheThreshold_IsProcessed_AndAddsNoBlob` |
| The claim-check posture line names container, threshold and client source — never the connection string (#15) | the startup log of `UseAzureBlobClaimCheck()` | `ClaimCheck_ApiStartupPostureLine_NamesContainerThresholdAndClientSource_NeverTheConnectionString` |

Observability runs in **Console** mode (`Cloudstrap:OpenTelemetry:Mode`), so the E2E fixture can
capture this host's telemetry from stdout; versioned OpenAPI documents and the Scalar reference
UI come with `AddCloudstrapWebApi` like on every WebApi host.

## Messaging harness notes (#14)

- This host is a **SQL Server messaging node**: it needs SQL Server **LocalDB** at startup
  (`ConnectionStrings:DefaultConnection` → the `CloudstrapDemo` database, created on first run in
  Development together with `demo.Orders`). LocalDB ships with Visual Studio and the
  `windows-latest` runners; `CLOUDSTRAP_TEST_SQL` overrides the connection string, and the E2E
  fixture forwards it. The Api boots for **every** E2E run, so the whole E2E suite now needs it.
- One database, several schemas — AC-MSG13 as a teaching point: this host's durability tables
  land in `demo_application_api`, the Worker's in `demo_application_worker`, the queue tables both
  hosts share in the explicitly configured `demo_transport` schema (`Cloudstrap:Messaging:SqlTransport:SchemaName`
  — a demo decision, not a package opinion), and the demo table in `demo`.
- `Cloudstrap:Messaging:Destinations` routes the contracts namespace to `demo-application-worker`
  — the Worker's workload queue. `PlaceOrderCommand` lives in `Cloudstrap.Demo.Contracts` with zero
  package references; the `*Command` suffix is all the routing needs.

## Claim-check harness notes (#15)

- Since #15 this host also needs a **blob backend** at startup: `Cloudstrap:Storage:ConnectionString`
  is `UseDevelopmentStorage=true` — the **Azurite** emulator on `127.0.0.1:10000` (`npm install -g
  azurite` once; the E2E fixture starts `azurite-blob` itself, or attaches to an emulator already
  running, or to whatever `CLOUDSTRAP_TEST_BLOB` names and forwards that value to every host).
  Start it manually with `azurite-blob --skipApiVersionCheck --location <folder>` — the pinned Azure
  SDK may speak a newer service version than the emulator knows.
- `Cloudstrap:Messaging:ClaimCheck` is deliberately **unset**: the defaults are the demo — the
  `demo-claimcheck` container (`{SystemName}-claimcheck`, created by Wolverine's store on first use) and
  the 204 800-byte threshold. The Worker lands on the same container by the same convention. The
  `notes` of an order ride the command, never the row; only the Worker's recorded length and SHA-256
  come back on `GET api/v1/orders/{id}`.

## Running

```powershell
dotnet run --project src/demo/Api        # needs LocalDB; /healthz answers 200 once the node is up
```

Token validation is lazy: without the demo IdP on 5310 the host still boots and probes stay
green, while authenticated calls fail loudly naming the authority. The JWT audience is
`demo-api` — the seed stamps it into the `demo-web` and `demo-blazorserver` users' tokens, and
deliberately **not** into the `demo-bff` machine client's.
