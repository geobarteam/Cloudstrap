# Spec: Blob Claim-Check — `Cloudstrap.Messaging.AzureBlob` (Roadmap Deliverable #15)

> Status: **APPROVED — 2026-09-21. Zero Open Questions.** The six spec-gate questions (OQ-1…OQ-6) were
> each ruled on by the user in favor of the analyst's recommended option (a); the rulings are recorded as
> Decision Log entries DL-5…DL-10 and folded into the body below. The planner may be invoked with this
> spec; see **Planner notes** at the end before pinning versions or touching CI.
>
> This is a **rebuild against an observed contract**, not a port: the source feature is one private
> 45-line NServiceBus wiring method (`TryUseDatabus`); the target is Wolverine. What carries over is the
> *behavioral contract* — large payloads leave the message, a reference travels, the handler sees the
> whole message — and nothing else. **The headline finding of the code reading and library check
> (finding 1) is that the bespoke "thin blob-offload middleware" the founding spec anticipated no longer
> needs to exist: Wolverine ships a first-class claim check with an Azure Blob Storage backend.** The
> package therefore reduces to a *seam*: one builder call, one options block, a client-resolution rule,
> one failure rule, one posture log line, and documentation. Cloudstrap owns no serialization, no
> envelope headers, no storage code.
>
> Sources: `_plans/ROADMAP.md` §15 (hand-off brief, source inventory verified 2026-09-03) ·
> `_specs/Cloudstrap.md` (Messaging Migration table row "Claim check / DataBus", AC-M3/AC-M4, De-NIHDI
> checklist rows "Storage account" + internal packages, Aspire Coexistence, Hosting targets, Decisions
> Made) · shape precedent `_specs/14-Messaging.md` (D-1…D-5) · **source reference repo (read-only)**
> `D:\source\Nihdi-Core-Configuration\Nihdi-Core-Configuration\src\` — every Port Decision Table row
> was opened: `Nihdi.Core.Configuration.NServiceBus\EndpointConfigurationBuilder.cs` (lines 1–60 and
> 320–400, `TryUseDatabus` at 335–380), `UseNServiceBusForNihdiOptions.cs`,
> `Nihdi.Core.Configuration.NServiceBus.csproj`, `Encryption\EncryptedDataBusSerializer.cs`,
> `Test\UnitTest\Nihdi.Core.Configuration.NServiceBus.Tests\EndpointConfigurationBuilderTests.cs`,
> `Test\UnitTest\Nihdi.Core.Configuration.Tests\NServiceBus\EncryptedDataBusSerializerTests.cs`,
> old Core `Settings\NServiceBus\AzureServiceBusTransportConfiguration.cs` (`EnableDatabus`) and
> `Settings\ApplicationConfiguration.cs` (`BlobContainerName`, `BlobContainerUri`) · **shipped Cloudstrap
> code read**: `src/Cloudstrap.Messaging/{CloudstrapMessagingBuilder, HostApplicationBuilderExtensions,
> CloudstrapMessagingExtension, CloudstrapMessagingConfigurator, MessagingRegistrationState,
> MessagingTransportSetup, MessagingStartupSummaryLogger, CloudstrapMessagingOptions}.cs`,
> `Correlation\{CorrelationEnvelopeRule, CorrelationMiddleware}.cs`, `Cloudstrap.Messaging.csproj`;
> `src/Cloudstrap.Extensions/{StorageOptions, BlobStorageRegistration, AzureCredentialSettings,
> HostApplicationBuilderExtensions}.cs`, `Cloudstrap.Extensions.csproj`, `Cloudstrap.Worker.csproj`;
> `src/demo/Api/{Program.cs, Controllers/OrdersController.cs, appsettings.json}`,
> `src/demo/Worker/{Program.cs, PlaceOrderCommandHandler.cs, appsettings.json}`,
> `src/demo/Shared/Contracts/PlaceOrderCommand.cs`; `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/
> {MessagingTests, E2eFixture}.cs`; `src/Test/UnitTest/Cloudstrap.Messaging.Tests/{PackageSurfaceTests,
> LocalNodeTests, Infrastructure/MessagingTestHost}.cs`, `Cloudstrap.Extensions.Tests/
> AddCloudstrapBlobStorageTests.cs`; `src/Directory.Packages.props`; `.github/workflows/ci.yml`.
>
> External evidence gathered 2026-09-03:
> - [Wolverine docs — Claim Checks](https://wolverinefx.net/guide/durability/claim-checks): first-class
>   claim check; `opts.UseClaimCheck(cc => …)`; whole-body size threshold
>   `cc.AutoOffloadPayloadsLargerThan(bytes)` (introduced 6.22); per-property `[Blob]` opt-in
>   (`Wolverine.Persistence.BlobAttribute`); backends incl. **Azure Blob Storage**
>   (`cc.UseAzureBlobStorage(connectionString, containerName)` / `cc.UseAzureBlobStorage(BlobContainerClient)`)
>   and a **file-system store in core `WolverineFx`** (`cc.UseFileSystem(path)`); implemented as an
>   `IMessageSerializer` **decorator applied at bootstrap**, uniform across all transports incl. local
>   queues and the durable outbox; reference travels as envelope headers `claim-check.*` carrying a
>   `ClaimCheckToken(Id, ContentType, Length)`; configuration methods **only mutate `WolverineOptions`**
>   (no service registration); missing payload on receive → handler chain throws → normal failure rules;
>   payloads are **retained** (no delete after handling; retries re-read); cloud stores deliberately
>   ship **no TTL sweep** (LIST billing) — native lifecycle policies recommended; container created on
>   first use.
> - [JasperFx/wolverine issue #2412](https://github.com/JasperFx/wolverine/issues/2412) — "Add Claim
>   Check / DataBus pattern via conventional approach", **closed**, shipped via PR #2617.
> - [Releases](https://github.com/JasperFx/wolverine/releases): 6.31.0 (2026-08-30, the suite's pin),
>   **6.32.0 (2026-09-01)**: "`WolverineFx.AmazonS3` and `WolverineFx.AzureBlobStorage` carry document and
>   saga persistence plus the claim check store" — `WolverineFx.ClaimCheck.*` cloud packages superseded,
>   "migration is a package reference swap"; **6.33.0 (2026-09-03)**: new `WolverineFx.AI`, EF Core
>   rollback fix, "requires Weasel 9.30.0; schema comparison now includes character lengths, potentially
>   triggering ALTER TABLE on existing databases".
> - [nuget.org WolverineFx.AzureBlobStorage 6.33.0](https://www.nuget.org/packages/WolverineFx.AzureBlobStorage)
>   — **MIT**, published 2026-09-03, `net9.0`/`net10.0`, deps `Azure.Storage.Blobs ≥ 12.27.0` +
>   `WolverineFx ≥ 6.33.0`, owner jeremydmiller (JasperFx). Brand new (29 downloads) — it is the
>   *consolidation* of a package with 10.2K downloads, not new code.
> - [nuget.org WolverineFx.ClaimCheck.AzureBlobStorage 6.31.0](https://www.nuget.org/packages/WolverineFx.ClaimCheck.AzureBlobStorage)
>   — **MIT**, 2026-08-30, deps `Azure.Storage.Blobs ≥ 12.27.0` + `WolverineFx ≥ 6.31.0`, 10.2K total
>   downloads, versions back to 5.41.0; **not** flagged deprecated on nuget.org, but superseded per the
>   6.32.0 notes.
> - [Azure/Azurite](https://github.com/Azure/Azurite) — **MIT**; npm `azurite`, Docker
>   `mcr.microsoft.com/azure-storage/azurite`, VS Code extension; blob endpoint `http://127.0.0.1:10000`;
>   `UseDevelopmentStorage=true` is the documented shorthand connection string (#4's `StorageOptions`
>   already documents it).
> - [nuget.org Testcontainers.Azurite 4.14.0](https://www.nuget.org/packages/Testcontainers.Azurite) —
>   **MIT**, 2026-08-14, `net8.0`–`net10.0` — recorded follow-up only (D-3 precedent), never referenced.
>
> **⚠️ Risk areas this deliverable touches**: **founding-spec amendment** (the "no built-in claim check"
> premise — DL-5; `_specs/Cloudstrap.md` Messaging Migration row amended 2026-09-21) · **dependency update
> to a shipped package** — the Wolverine family pin (DL-7) · **public API one-way doors**: the builder
> method name/shape and the `Cloudstrap:Messaging:ClaimCheck` block (this spec) · **shipped-surface
> amendment to #14** (the `ConfigureEngine` contribution seam, DL-8) · **cloud-resource naming** (the
> default container name, DL-9 — the D-1 class of one-way door) · **E2E/CI infrastructure** (a blob
> emulator prerequisite, DL-10) · **failure/lifecycle semantics** (Decision Log DL-1/DL-2).

## Code-reading findings that shaped this spec

1. **The founding table's premise is stale: Wolverine now has the feature.** The Messaging Migration row
   says "Wolverine has no built-in claim check" and prescribes a bespoke offload middleware. Between the
   founding spec (2026-07-24) and today, Wolverine shipped claim checks (issue #2412 → PR #2617; size
   threshold in 6.22; Azure backend consolidated into `WolverineFx.AzureBlobStorage` in 6.32.0). It does
   *exactly* what the founding row asks — payloads over a size threshold go to Blob, a reference travels
   in the message, the handler sees the full message — as a serializer decorator that works on every
   transport including the durable outbox, with a pluggable store. Building the same thing in Cloudstrap
   would be code owned forever for zero differentiation. **Verdict: Replace** (DL-5 — user ruling
   2026-09-21; the founding-spec row is amended accordingly).
2. **The source method is four De-NIHDI violations wrapped around two library calls.** `TryUseDatabus`
   (a) treats `Application.BlobContainerName` — the *lower-cased business system name* — as both the
   **storage account name** (`https://{name}.blob.core.windows.net`) and the container name, an
   enterprise naming convention where account and system coincide; (b) builds a `ClientSecretCredential`
   from the **ASB transport's** tenant/client/secret settings (a credential borrowed across resources —
   the #1 OQ-2 ruling forbids modeling credentials at all); (c) **silently does nothing** when any of the
   four values is missing (`if (a != null && b != null && c != null && d != null)`), so a mistyped setting
   turns the feature off without a word; (d) optionally routes through the dropped RSA
   `EncryptedDataBusSerializer`. The two library calls that remain — `UseClaimCheck<AzureClaimCheck>`
   with a JSON serializer, and a property convention — are what Wolverine now provides natively.
3. **The property-name convention is the source's one design opinion, and it kept contracts
   engine-free on purpose.** The comment above `DefiningClaimCheckPropertiesAs(p => p.Name.EndsWith("DataBus"))`
   says it: "removing the need for having a dependency on NServiceBus from the message types". Wolverine's
   per-property path (`[Blob]`) needs a Wolverine reference in the contracts assembly — which #14 made a
   principle to avoid (`PlaceOrderCommand` lives in a zero-reference project; AC-MSG4). Wolverine offers
   **no** name-convention hook. The founding spec's *whole-body size threshold* keeps contracts
   dependency-free with zero bespoke code and needs no naming discipline from consumers; the source's
   size-blind convention (a 12-byte `*DataBus` property is offloaded, a 5 MB `Notes` string is not) is the
   weaker design anyway. **Verdict: Redesign to the threshold** (DL-6).
4. **The Wolverine configuration surface already contains every knob the source had, and the ones the
   founding spec asks for.** Store selection, threshold, per-message routing (`StoreForMessage<T>`,
   `StoreWhen(...)`), explicit client injection, container name — all on `opts.UseClaimCheck(cc => …)`.
   Cloudstrap's value is binding these to the suite's conventions: `Cloudstrap:` configuration, the
   `{system}` naming opinion, `DefaultAzureCredential` through the already-shipped blob registration,
   fail-fast validation, the D-2 dead-letter posture, and Aspire coexistence.
5. **The #14 seam is smaller than the roadmap feared — but one ordering fact matters.** Wolverine's claim
   check methods only mutate `WolverineOptions` and may run inside a container-registered
   `IWolverineExtension`; the leaf never needs `MessagingRegistrationState`. The public
   `CloudstrapMessagingBuilder.HostBuilder` is enough to register services, and `UseClaimCheck` is
   idempotent and wraps whatever serializer is finally configured, so it is order-insensitive. What is
   **not** order-insensitive is a failure rule: #14's `CloudstrapMessagingExtension` appends the global
   retry ladder *last*, and Wolverine matches failure rules in order — a leaf extension registered after
   #14's (the only option without touching #14) would append its "missing blob → dead-letter" rule
   *behind* the catch-all ladder, where it never matches. Hence DL-8: a small public contribution seam
   on the builder (`ConfigureEngine`), applied before the consumer's delegate and the ladder — the same
   door the PostgreSQL leaf will walk through.
6. **The storage decision is a safety decision, not a tidiness one.** #4's `AddCloudstrapBlobStorage`
   yields *the one container an application works against* (`Cloudstrap:Storage:ContainerName`,
   default = system name); the same account often hosts the DataProtection key ring. Wolverine deliberately
   provides no sweep for cloud stores, so cleanup means an **Azure lifecycle-management policy** on the
   claim-check blobs' container. A delete-after-N-days policy on a container that also holds application
   blobs or a key ring would be a production incident. **A dedicated container by default**
   (`{system}-claimcheck`) is the only shape under which "document a lifecycle policy" is safe advice
   (DL-9, DL-2).
7. **Deleting blobs after handling is wrong under D-1, not merely unnecessary.** Events publish to a
   topic per event type with one subscription per consuming workload — N consumers rehydrate the *same*
   blob. The first successful handler deleting it would break the other N−1. Retention + lifecycle policy
   is the correct model (Wolverine's own posture; DL-2).
8. **A missing blob is a deterministic failure; a storage outage is not.** Azure Blob Storage is strongly
   consistent: a 404 on receive means the blob was never written, was swept, or the reference is wrong —
   ten retries over minutes change nothing and delay dead-lettering. A 5xx/timeout is transient and
   *should* ride the retry ladder. #14 already has this exact precedent (`CorrelationRequiredException`
   → `MoveToErrorQueue()` without retries). One failure rule expresses it (DL-1).
9. **No new dependency family.** `WolverineFx.AzureBlobStorage` is the same JasperFx family (MIT) and
   floors `Azure.Storage.Blobs ≥ 12.27.0` (the suite pins 12.29.1). The leaf needs neither
   `Azure.Identity` (credentials arrive inside the registered client) nor a reference to
   `Cloudstrap.Extensions` (it looks up Azure SDK client *types* from DI, not Cloudstrap types) — which
   keeps the ASP.NET framework reference out of the closure. `Cloudstrap.Worker` already references
   `Cloudstrap.Extensions`, so every supported host type has `AddCloudstrapBlobStorage` at hand.
10. **The source tests prove almost nothing and the source demo proves nothing.** The two
    `EndpointConfigurationBuilderTests` assert a log fragment; no in-repo host enables the databus; the
    encrypted-serializer tests depend on a certificate in the machine store and lack `[TestMethod]`
    attributes (they never ran). The observed contract is the code, not its tests.

---

## User Story

**As an** ASP.NET Core developer deploying to Azure whose services exchange commands and events through
`Cloudstrap.Messaging`, some of which carry large payloads (documents, exports, generated files) that
exceed what Azure Service Bus accepts or what a SQL transport row should carry,
**I want to** add one call — `AddCloudstrapMessaging(...).UseAzureBlobClaimCheck()` — and have any message
whose body exceeds a size threshold stored transparently in an Azure Blob Storage container while only a
reference travels through the transport,
**So that** my message contracts stay plain records with no package references, my handlers receive the
full message without knowing about blobs, broker size limits never fail a send, and the storage is
reached with the same `DefaultAzureCredential` and `Cloudstrap:Storage` conventions the rest of my app
already uses.

---

## Acceptance Criteria

> AC-M4 is carried **verbatim** from the founding spec. AC-M3, AC-A3 and AC-ASP2 are carried as standing
> criteria/tripwires. AC-CK1…AC-CK14 are new, spec-specific criteria (precedent: AC-MSG1…16).

| # | Given | When | Then |
|---|-------|------|------|
| AC-M4 | Message exceeds blob-offload threshold | Message sent | Payload lands in Blob, message carries reference, consumer reads it transparently. *(carried verbatim)* |
| AC-M3 | In-memory transport, no Azure resources | Full test suite runs | All messaging tests pass locally with no network. *(carried — applies to this package's default suite: no live storage account)* |
| AC-A3 | Solution searched for `Nihdi.AspNetCore` | — | Zero references. *(carried — must stay green)* |
| AC-ASP2 | Any shipped Cloudstrap package | Its dependency closure is inspected | Zero `Aspire.*` packages. *(carried verbatim)* |
| AC-CK1 | A node with `UseAzureBlobClaimCheck()` and a resolvable blob container | A message whose **serialized body** exceeds `OffloadThresholdBytes` is sent | Exactly one blob is written to the claim-check container; the envelope carries Wolverine's claim-check reference header(s); the body that crosses the transport is the stripped body, not the payload. |
| AC-CK2 | Same node | A message whose serialized body is **at or below** the threshold is sent | No blob is written, no claim-check header is added, the body crosses the transport unchanged. |
| AC-CK3 | Same node, an offloaded message | The handler runs | The handler receives the fully rehydrated message; handler code and the contracts assembly reference neither the leaf, Wolverine's claim-check types, nor Azure SDK types (the demo `PlaceOrderCommand` stays a zero-reference record). |
| AC-CK4 | Same node, an envelope whose referenced blob does not exist (HTTP 404 from storage) | The message is received | The message is dead-lettered per D-2 **without** running the retry ladder; the failure is logged with message type, message id and container name — never the payload or a connection string. Any other storage failure (5xx, timeout, auth) follows the normal retry ladder (DL-1). |
| AC-CK5 | Same node, a handler that fails transiently N times then succeeds on an offloaded message | Retries run | The payload is re-read from the store on every attempt; exactly one observed side effect; the blob still exists after successful handling (retention, DL-2). |
| AC-CK6 | Same node, an inbound HTTP request with `X-Correlation-ID` whose handler sends an above-threshold command | The remote handler runs | `ICorrelationContextAccessor.CorrelationId` equals the inbound value (AC-MSG9 unchanged by offloading); Wolverine spans/metrics still flow to the host's OTel pipeline; the leaf registers no exporter and no provider. |
| AC-CK7 | A node **without** `UseAzureBlobClaimCheck()` | A 1 MB message is sent on the local transport | It travels whole; no blob client is resolved or constructed; `Cloudstrap.Messaging`'s closure contains no leaf types (the leaf is strictly additive). |
| AC-CK8 | `UseAzureBlobClaimCheck()` with (a) no blob client of any kind registered; (b) a `BlobContainerClient` registered by `AddCloudstrapBlobStorage`; (c) a `BlobServiceClient` registered by the consumer or an Aspire-style registration; (d) a code-level `ContainerClient` supplied to the call | The host starts | (a) startup fails naming `AddCloudstrapBlobStorage` and the register-a-client alternative; (b) the claim-check container is opened on the **same account and credential** as the registered container client (its parent blob service); (c) the registered service client is used; (d) the supplied client wins over (b)/(c) and the `ContainerName` setting is ignored (stated in the posture line). Resolution order: d > c > b. No second client is constructed in (b)–(d) (AC-ASP composability — "Cloudstrap's blob registration or Aspire's, not both"). |
| AC-CK9 | `Cloudstrap:Messaging:ClaimCheck:OffloadThresholdBytes` ≤ 0, or `ContainerName` violating Azure container naming rules | The host starts | Startup fails naming the exact offending key via the `[OptionsValidator]` + `ValidateOnStart` pattern (#1). |
| AC-CK10 | Any node with the claim check registered | Startup | One Information log line states: container name, effective threshold in bytes, and the client source (`code`, `BlobServiceClient`, `AddCloudstrapBlobStorage`); no account URI, no connection string. |
| AC-CK11 | A fresh clone with this package | Build, tests, `dotnet format --verify-no-changes`, closure review, case-insensitive search for `Nihdi`, `Riziv`, `NServiceBus`, `DataBus`/`Databus`, `Encrypt` | All green; XML docs on all public API; package metadata + README complete (lifecycle-policy sample, required data-plane rights, the outbox interplay); the closure is exactly `Cloudstrap.Messaging` + `WolverineFx.AzureBlobStorage` and their disclosed transitives — zero `Aspire.*`, `NServiceBus.*`, `Nihdi.*`; a `PackageSurfaceTests`-style tripwire pins the public surface and the dropped concepts. |
| AC-CK12 | The demo apps (vehicle: **`Cloudstrap.Demo.Api`** producer → **`Cloudstrap.Demo.Worker`** consumer, the #14 order flow) and the E2E suite, with the E2E blob backend of DL-10 (Azurite by default, `CLOUDSTRAP_TEST_BLOB` override) | The E2E suite runs | ≥ 1 new E2E test: an order placed with above-threshold `Notes` is processed by the Worker, which records the notes' length and SHA-256; the E2E asserts both equal what was sent **and** that exactly one new blob appeared in the claim-check container; a below-threshold order adds no blob. All pre-existing E2E tests stay green. *(workflow rule 9)* |
| AC-CK13 | A node with `UseSqlServer()` + `AddCloudstrapTransactionalMessaging<TDbContext>()` (D-3 LocalDB) | An above-threshold command is sent through `IDbContextOutbox` and the process is killed between commit and dispatch | After restart the durable outbox delivers it and the handler receives the rehydrated message (AC-MSG8 holds for offloaded messages). |
| AC-CK14 | `UseAzureBlobClaimCheck()` called twice on the same builder | Registration | The second call fails fast naming the duplicate (one claim-check store per node; the failure is contractual, the AC-MSG14 precedent). |

---

## Port Decision Table

One row per source artefact/feature. The founding spec's *anticipated* bespoke middleware appears as its
own row because it is the thing this spec most consciously does **not** build.

### Part A — `EndpointConfigurationBuilder.TryUseDatabus` (lines 335–380), decomposed

| Source | Verdict | Target | Justification |
|---|---|---|---|
| `TryUseDatabus` (the wiring method as a whole) | **Replace** | `UseAzureBlobClaimCheck(...)` on `CloudstrapMessagingBuilder` → Wolverine `opts.UseClaimCheck(cc => cc.UseAzureBlobStorage(client))` + `AutoOffloadPayloadsLargerThan(threshold)` | Finding 1: the capability is Wolverine's; Cloudstrap owns the binding to its conventions only. |
| ↳ `EnableDatabus` gate (`UseNServiceBusForNihdiOptions.EnableDatabus` ← `AzureServiceBusTransportConfiguration.EnableDatabus`) | **Drop** | presence of the builder call | #14 precedent (`EnableNServiceBus` dropped, #5 D-2): whether a node offloads payloads is visible in `Program.cs`, not buried in a flag whose `false` branch silently no-ops. |
| ↳ `if (accountName != null && tenantId != null && clientId != null && clientSecret != null)` silent no-op | **Drop** | fail-fast at startup (AC-CK8a, AC-CK9) | Finding 2c: a misconfiguration that silently disables large-message handling surfaces in production as a broker size-limit exception on the first big message. |
| ↳ `new Uri($"https://{Application.BlobContainerName}.blob.core.windows.net")` | **Drop** | the blob service of the DI-registered client (#4's `Cloudstrap:Storage:BlobServiceUri`, Aspire's, or the consumer's) | De-NIHDI "Storage account" row: enterprise account-naming convention where account = system name. |
| ↳ `ClientSecretCredential(tenantId, clientId, clientSecret)` lifted from ASB settings | **Drop** | credential travels with the registered client (`DefaultAzureCredential` by #4's default; `AzureCredentialSettings.Credential` for code overrides) | #1's OQ-2 ruling; hosting posture (identical across Web Apps/AKS); no secret-bearing settings. |
| ↳ `.Container(Application.BlobContainerName)` (app container = lower-cased system name) | **Redesign** | dedicated `{system}-claimcheck` default; `Cloudstrap:Messaging:ClaimCheck:ContainerName` override (DL-9) | Finding 6: lifecycle-policy safety; the `{system}-…` naming opinion (D-1) kept. |
| ↳ `UseClaimCheck<AzureClaimCheck>` (`NServiceBus.DataBus.AzureBlobStorage` 7.0.1) | **Replace** | `WolverineFx.AzureBlobStorage` claim-check store | Library-owned storage code; MIT; same JasperFx family already in the closure. |
| ↳ `new SystemJsonClaimCheckSerializer()` (`NServiceBus.ClaimCheck` 2.0.1) | **Replace** | Wolverine's claim-check serializer decorator over the node's System.Text.Json serializer | Same wire idea (JSON body, external bytes), now the engine's own decorator; applies to every transport incl. the durable outbox. |
| ↳ `EncryptedDataBusSerializer` branch (`EnableMessageEncryption` + `CertificateSubjectName`, RSA key pair) | **Drop** | — | Founding decision: property-level encryption dropped permanently; TLS + storage encryption at rest is the documented baseline. |
| ↳ `conventions.DefiningClaimCheckPropertiesAs(p => p.Name.EndsWith("DataBus"))` | **Redesign** | whole-body size threshold (`OffloadThresholdBytes`, default 200 KiB); Wolverine's `[Blob]` documented as the engine-native per-property opt-in (DL-6) | Finding 3: no Wolverine name-convention hook exists; the threshold keeps contracts dependency-free with zero bespoke code and is size-aware, which the source convention was not. |
| ↳ `"Using Blob Storage for DataBus with Uri = '{Uri}'"` log line | **Redesign** | one startup posture line: container, threshold, client source (AC-CK10) | Keeps the operational signal, drops the URI value (#14's names-not-values posture). |
| *(founding table)* "thin blob-offload middleware in `Cloudstrap.Messaging.AzureBlob`" — never existed in the source | **Replace** | Wolverine native claim check (DL-5) | Finding 1: building it now would duplicate a maintained library feature; the founding row's *contract* (threshold, reference travels, transparent read) is met exactly. |

### Part B — supporting artefacts and settings

| Source | Verdict | Target | Justification |
|---|---|---|---|
| `Encryption\EncryptedDataBusSerializer.cs` (public `IClaimCheckSerializer`, RSA via `Nihdi.Core.NServiceBus.Cryptography`) | **Drop** | — | Founding decision + De-NIHDI internal-packages row; confirmed nothing else references it besides `TryUseDatabus`. |
| `Nihdi.Core.Configuration.Tests\NServiceBus\EncryptedDataBusSerializerTests.cs` | **Drop** | — | Tests of a dropped type; depend on a certificate (`ASB-NIHDI-TST`) in the machine store and carry no test attributes (finding 10). |
| `EndpointConfigurationBuilderTests` (`Build_EnableDatabusTrue_RegistersAzureClaimCheck`, `…False_DoesNotRegisterClaimCheck`) | **Redesign** | registration / no-op / fail-fast tests asserting on engine state (AC-CK7, AC-CK8, AC-CK14) | The contract (registered vs not) survives; asserting on a log string does not. |
| `NServiceBus.ClaimCheck` 2.0.1 + `NServiceBus.DataBus.AzureBlobStorage` 7.0.1 package references | **Replace** | `WolverineFx.AzureBlobStorage` (DL-7; version per Planner note 1) | Gone with NServiceBus (founding). |
| old Core `AzureServiceBusTransportConfiguration.EnableDatabus` | **Drop** | — | Routed here by #14's Part-B row; the flag concept dies with the gate row above. |
| old Core `ApplicationConfiguration.BlobContainerName` / `BlobContainerUri` / `StorageName` | **Drop** | superseded by #4's shipped `StorageOptions` (`BlobServiceUri`, `ContainerName`) | Already replaced by a shipped deliverable; nothing to re-port. |

**Tally**: 0 Port · 4 Redesign · 5 Replace · 9 Drop *(18 rows)*.

---

## Public API Sketch

Namespace **`Cloudstrap.Messaging.AzureBlob`** (single namespace, matching the package id). Everything
`public sealed`/`static`; the Wolverine extension, client resolution, failure rule and posture logger are
`internal`. Wolverine's own claim-check types stay first-class for advanced use (no facade — the #14
posture). Names marked ⚠️ are one-way doors.

```text
Cloudstrap.Messaging.AzureBlob
├── CloudstrapMessagingBuilderExtensions (static)
│     UseAzureBlobClaimCheck(                                         ⚠️ name/shape
│         this CloudstrapMessagingBuilder builder,
│         Action<AzureBlobClaimCheckSettings>? configure = null)
│         : CloudstrapMessagingBuilder
│       — binds + validates AzureBlobClaimCheckOptions (Cloudstrap:Messaging:ClaimCheck) with
│         ValidateOnStart; contributes to the engine (via the DL-8 ConfigureEngine seam) at host start:
│         opts.UseClaimCheck(cc => { cc.UseAzureBlobStorage(<resolved container client>);
│                                    cc.AutoOffloadPayloadsLargerThan(OffloadThresholdBytes);
│                                    settings.ClaimCheck?.Invoke(cc); })
│         + the missing-blob failure rule (DL-1) + the posture log line (AC-CK10).
│         Second call on the same builder → fail fast (AC-CK14). Composes in any order with
│         UseSqlServer / AddCloudstrapTransactionalMessaging.
│
├── AzureBlobClaimCheckOptions                 — section Cloudstrap:Messaging:ClaimCheck   ⚠️ keys
│     const SectionName = "Cloudstrap:Messaging:ClaimCheck"
│     OffloadThresholdBytes : long = 204_800   — bodies strictly larger are offloaded (200 KiB)
│     ContainerName         : string?          — default: "{SystemName}-claimcheck" (lower-cased)
│
└── AzureBlobClaimCheckSettings                — code-level hooks (the AzureCredentialSettings idiom)
      ContainerClient : BlobContainerClient?   — explicit client; wins over anything in DI (AC-CK8d)
      ClaimCheck      : Action<{Wolverine claim-check configuration type}>?
                                               — runs LAST inside UseClaimCheck: per-message store
                                                 routing (StoreForMessage<T>, StoreWhen), etc.
```

> The exact Wolverine type behind `ClaimCheck` (the `cc` lambda parameter of `opts.UseClaimCheck`) is
> pinned by the planner against the chosen package version (DL-7, Planner note 1); the spec fixes the
> *hook*, not the type name.

**Amended shipped surface — `Cloudstrap.Messaging` (#14), per DL-8.** One additive public member on the
existing `CloudstrapMessagingBuilder`; no new public type, so `PackageSurfaceTests`' approved type list is
unchanged (its member-level assertions are updated alongside):

```text
Cloudstrap.Messaging
└── CloudstrapMessagingBuilder (existing, sealed)
      ConfigureEngine(Action<IServiceProvider, WolverineOptions> contribution)   ⚠️ name/shape
          : CloudstrapMessagingBuilder
        — registers an engine contribution for leaf packages. Contributions are stored on
          MessagingRegistrationState and applied by CloudstrapMessagingExtension in registration
          order, BEFORE the consumer's `configurator.Wolverine` delegate and BEFORE RetryLadder.Apply,
          so (i) a leaf's failure rules precede the catch-all ladder and (ii) the consumer keeps the
          "final say" contract #14 promised. The extension gains an IServiceProvider constructor
          dependency to hand contributions their DI scope (the leaf resolves the blob client here).
          Not idempotent by design: each call appends one contribution.
```

The leaf's `UseAzureBlobClaimCheck` is the first consumer of this seam; the planned PostgreSQL
durability leaf is the second. Consumers may call it too, but the documented consumer door remains
`configurator.Wolverine` (runs after all contributions).

**Deliberately not shipped**: no `Enabled` flag (the call is the switch) · no threshold-per-message-type
settings (Wolverine's `StoreForMessage<T>` via the `ClaimCheck` hook covers it) · no blob-name prefix knob
(the dedicated container is the isolation unit; Wolverine owns blob ids) · no envelope header/reference
format of Cloudstrap's own (Wolverine's `claim-check.*` headers are the contract — one fewer one-way
door) · no cleanup/sweep code (DL-2) · no property-name convention (DL-6) · no `Cloudstrap:Messaging:
ClaimCheck:BlobServiceUri`/`ConnectionString` duplicate of `Cloudstrap:Storage` (DL-9) · no
`Cloudstrap.Extensions` project reference (finding 9).

**Configuration** — this package owns exactly one new block, `Cloudstrap:Messaging:ClaimCheck`, bound to
its own options type at a sub-path of the section `Cloudstrap.Messaging` owns (the D-5 sibling-path
posture: configuration sections are not owned by classes; **no shipped `CloudstrapMessagingOptions`
change**). It consumes `Cloudstrap:Application:SystemName` (#1) for the default container name and
whatever blob registration the host has — by preference #4's `Cloudstrap:Storage` — and redefines
neither.

---

## Behaviors & Conventions

| Behavior | Default | Override |
|---|---|---|
| Activation | Nothing happens until `UseAzureBlobClaimCheck()` is called on the #14 builder; messages of any size travel whole (AC-CK7). | Call it, or don't. |
| Offload rule | Whole serialized body **strictly larger than** 200 KiB (204 800 bytes) is stored; the stripped envelope carries Wolverine's reference headers. Rationale: ASB Standard's 256 KB limit is measured *including* headers/system properties — 200 KiB leaves headroom; ASB Premium (100 MB) and the SQL transport have no hard need but benefit from lean rows. Buffered local queues perform no serialization on hand-off (Wolverine semantics), durable local queues do. | `OffloadThresholdBytes`; per-message-type routing/thresholds via `settings.ClaimCheck` (Wolverine's `StoreForMessage<T>(store, threshold)`); Wolverine's `[Blob]` for explicit per-property offload (documented, not wrapped — costs a Wolverine reference in contracts). |
| Container | `{SystemName}-claimcheck`, lower-cased (e.g. `demo-claimcheck`) — dedicated, so a lifecycle policy on it can never touch application blobs or the DataProtection key ring (finding 6). Wolverine creates it on first use; the identity therefore needs container-create rights on the account **or** the container pre-created by IaC (then the create-if-not-exists is a no-op). Independent of `Cloudstrap:Messaging:AutoProvision` — a documented difference (Wolverine's store, not Cloudstrap's). | `ContainerName` (pointing it at `Cloudstrap:Storage:ContainerName`'s value is allowed and documented as "you own the lifecycle risk"); `settings.ContainerClient`. |
| Account & credential | Resolved from DI at host start, in order: `settings.ContainerClient` → a registered `BlobServiceClient` (consumer/Aspire) → the registered `BlobContainerClient`'s parent blob service (#4's `AddCloudstrapBlobStorage`, i.e. `Cloudstrap:Storage:BlobServiceUri` + `DefaultAzureCredential`, or its connection string incl. `UseDevelopmentStorage=true`). None → fail fast naming both routes (AC-CK8). Never constructs a credential of its own. | Register the client you want; the ladder is fixed and logged. |
| Rehydration | Transparent: the handler sees the full message; retries re-read the blob each attempt (AC-CK5). | — (Wolverine semantics). |
| Missing blob (404) | Dead-letter immediately, no retries — durable store's dead-letter table when durability is on, `{SystemName}-error` transport queue otherwise (D-2); log line carries message type, id, container — never the payload (DL-1). | `configurator.Wolverine` on #14 (runs after the leaf's contribution; consumers may add a preceding rule). |
| Other storage failures (5xx, throttling, auth, timeout) | Normal #14 retry ladder, then dead-letter. | `Cloudstrap:Messaging:Retries`; `configurator.Wolverine`. |
| Blob lifecycle | **Retained** after handling (N subscribers may read one blob, dead-lettered messages must stay replayable). Cleanup is an **Azure Storage lifecycle-management policy on the claim-check container** — the README ships a sample rule (delete base blobs older than *N* days, N ≥ retry window + dead-letter replay window; suggested 14) and states the orphan case: a durable-outbox transaction rolled back after serialization leaves a blob the policy removes (DL-2). No Cloudstrap sweep code. | Consumer's policy; `settings.ClaimCheck` for Wolverine store options. |
| Outbox interplay | With `AddCloudstrapTransactionalMessaging<T>`, offloading happens when the envelope is serialized for the outbox — i.e. **inside** `SaveChangesAndFlushMessagesAsync`, before the commit; upload latency lands on the HTTP request. Documented in the README; AC-CK13 proves recovery. | — |
| Correlation & telemetry | Untouched: correlation is an envelope header, offloading is a body concern (AC-CK6). Wolverine's spans/metrics flow to the host's pipeline; the leaf registers no exporter/provider. | — |
| Startup posture | One Information line: container, threshold, client source (AC-CK10). | Log level. |
| Secrets & telemetry | Account URIs, connection strings and payloads never appear in logs, validation failures or exceptions — keys and names only. | None — rule. |
| Aspire coexistence | An Aspire app registers `BlobServiceClient` through `Aspire.Azure.Storage.Blobs`; the ladder prefers it, so "Cloudstrap's blob registration or Aspire's, not both" holds without any Aspire reference. AC-ASP2 carried as a closure tripwire. | — (posture). |

**Test strategy (spec-level):** NUnit 4 on Microsoft.Testing.Platform in
`src/Test/UnitTest/Cloudstrap.Messaging.AzureBlob.Tests`. The default suite runs on the **local
transport with no network and no storage account** (AC-M3): options binding/validation, the client
resolution ladder and its fail-fast messages, duplicate-call detection, the posture line, and the closure/
surface tripwires are pure in-process tests. The behavioral round trip (AC-CK1/2/3/4/5/6) runs through
the leaf's real registration against a **no-network fake of the Azure SDK client** — `BlobContainerClient`
/`BlobClient` are designed for mocking (virtual members; Azure SDK guidance) and the fake is supplied via
`settings.ContainerClient`; the planner verifies which SDK members Wolverine's store calls and, if the
fake proves brittle, falls back to Wolverine's core **file-system store** (`cc.UseFileSystem(tempPath)`)
through `settings.ClaimCheck` for the threshold/rehydration/404 assertions. AC-CK13 is a D-3 LocalDB test
(`CLOUDSTRAP_TEST_SQL` override). The real Azure-store round trip is the E2E demo on the DL-10 backend
(Azurite, fixture-started; `CLOUDSTRAP_TEST_BLOB` override — see Planner note 2 for the CI shape);
verification against a live storage account is a documented manual procedure (the AC-M1/AC-E5 precedent).

---

## Dependencies

| Package | Version (pin at plan time) | License | Why it is justified |
|---|---|---|---|
| `WolverineFx.AzureBlobStorage` | ≥ 6.33.0 — the **latest stable** `WolverineFx.*` release at plan time, moved in lockstep with the five existing family pins in `src/Directory.Packages.props` (DL-7, Planner note 1). `WolverineFx.ClaimCheck.AzureBlobStorage` is **not** used (superseded per the 6.32.0 notes). | MIT (verified 2026-09-03) | The claim-check store — eliminates all bespoke serialization/offload/rehydration/storage code. Brings `Azure.Storage.Blobs` (MIT; suite pin 12.29.1 satisfies its ≥ 12.27.0 floor). |
| `Cloudstrap.Messaging` (project) | — | MIT | The builder the leaf extends, the engine, the retry/dead-letter posture; its `WolverineFx` closure. |
| *(test/demo only, DL-10)* Azurite — no package | — | MIT | E2E blob backend; `Testcontainers.Azurite` (MIT) recorded as the follow-up only. Nothing reaches a shipped closure. |
| *(E2E test project only)* `Azure.Storage.Blobs` | 12.29.1 (existing pin) | MIT | Lets the E2E test list the Azurite container to prove a blob appeared (AC-CK12). |

No `Azure.Identity` reference (credentials arrive inside the registered client), no `Cloudstrap.Extensions`
reference (finding 9), no new dependency family. Zero `Aspire.*`, `NServiceBus.*`, `Nihdi.*`.

---

## Deliberate Behavior Changes (vs the source library)

1. **Engine and pattern swap** — NServiceBus databus → Wolverine claim check; no wire compatibility
   (already true of #14).
2. **Offload trigger: property-name convention (`*DataBus`, size-blind) → whole-body size threshold**
   (200 KiB default). Contracts stay dependency-free either way; per-property offload remains available
   through Wolverine's `[Blob]` at the consumer's discretion (DL-6).
3. **Silent no-op on incomplete settings → fail fast at startup** naming the missing registration/key.
4. **Account + credential: hard-coded `https://{system}.blob.core.windows.net` + client secret from ASB
   settings → the host's registered blob client** (`DefaultAzureCredential` via #4, or Aspire's).
5. **Container: the application's own container → a dedicated `{system}-claimcheck` container**, so a
   lifecycle policy is safe (DL-9).
6. **Missing blob dead-letters immediately** instead of after the full retry ladder (DL-1). (The source
   had no defined behavior — NServiceBus would have retried.)
7. **Encrypted databus path removed** (founding).
8. **Reference format is Wolverine's** (`claim-check.*` headers), not a Cloudstrap-defined one.

---

## Out of Scope

- **A bespoke offload middleware / serializer / envelope-rule implementation** — replaced by Wolverine's
  native claim check (DL-5); the planner must not build one.
- **Property-level message encryption** (`EncryptedDataBusSerializer`, RSA keys, certificate settings)
  — dropped permanently (founding).
- **The `*DataBus` property-name convention** and any `Enable*` configuration flag (DL-6, Drop rows).
- **`WolverineFx.ClaimCheck.AzureBlobStorage`** (the pre-6.32 package) — superseded; never referenced
  (DL-7).
- **Leaf-owned storage-account settings** (`Cloudstrap:Messaging:ClaimCheck:{BlobServiceUri,
  ConnectionString}`) and reuse of the application container as the default (DL-9).
- **Docker/Testcontainers as an E2E prerequisite** — `Testcontainers.Azurite` is a recorded follow-up,
  not part of this deliverable (DL-10).
- **Blob cleanup/sweep code, TTL jobs, delete-after-handling** — lifecycle is a documented Azure policy
  (DL-2).
- **Other claim-check stores** (S3, GCS, NATS, SQL/PostgreSQL LOB, file system in production) — Azure is
  the opinionated target (founding Non-Goal); they remain reachable through `settings.ClaimCheck`.
- **Message-level or blob-level encryption at rest configuration** — storage-account encryption is the
  Azure default; out of Cloudstrap's hands.
- **Dashboard visibility of offloaded payloads** — #19/#20 (they build against D-2 and this spec's
  retention posture).
- **A `Cloudstrap.Aspire` integration** — post-v1 leaf, per founding posture.
- **Changes to `Cloudstrap.Extensions`' `StorageOptions`** — none needed; the leaf consumes its output.

---

## Decision Log

DL-1…DL-4 are analyst decisions accepted at the gate; DL-5…DL-10 are the user's rulings of 2026-09-21 on
the former Open Questions OQ-1…OQ-6 (each accepted the analyst's recommended option (a)).

| DL | Decision | Rationale kept on record |
|---|---|---|
| **DL-1** | **A 404 from the store on receive dead-letters immediately (no retries); every other storage failure rides the #14 retry ladder.** Expressed as one Wolverine failure rule contributed *before* the ladder (through DL-8's `ConfigureEngine` seam). The planner pins the exception surface (Azure SDK `RequestFailedException` with `Status == 404`, possibly wrapped by Wolverine's store) against the chosen package version. | Finding 8: strongly consistent storage makes a 404 deterministic; the `CorrelationRequiredException → MoveToErrorQueue()` precedent in #14; transient faults must still retry. |
| **DL-2** | **Blobs are retained; cleanup is a documented Azure lifecycle-management policy on the dedicated container; the README ships a sample rule and names the orphan case.** No delete-after-handling, no sweep code. | Findings 6–7: multi-subscriber events share one blob; dead-lettered messages must stay replayable; Wolverine deliberately provides no cloud sweep (LIST billing); a dedicated container makes the policy safe. |
| **DL-3** | **The leaf references `Cloudstrap.Messaging` only** — not `Cloudstrap.Extensions` — and resolves Azure SDK client *types* from DI. | Finding 9: keeps the ASP.NET framework reference out of headless hosts' closure while every supported host type already has `AddCloudstrapBlobStorage` available (`Cloudstrap.Worker` references Extensions). |
| **DL-4** | **Naming**: `UseAzureBlobClaimCheck` (the `UseSqlServer` builder idiom — no `Cloudstrap` prefix on builder-scoped methods), options `AzureBlobClaimCheckOptions` at `Cloudstrap:Messaging:ClaimCheck`, settings `AzureBlobClaimCheckSettings`. Neutral, engine-free vocabulary (`ClaimCheck`, never `DataBus`). | De-NIHDI naming row; #14's builder precedent; the `AzureCredentialSettings` code-hook idiom from #4. |
| **DL-5** *(user ruling 2026-09-21, ex OQ-1)* | **Replace the founding spec's anticipated bespoke blob-offload middleware with Wolverine's native claim check and its Azure Blob store.** `Cloudstrap.Messaging.AzureBlob` is a *seam* over `opts.UseClaimCheck(cc => cc.UseAzureBlobStorage(...))`, not bespoke middleware. **This amends the founding spec**: the `_specs/Cloudstrap.md` Messaging Migration row "Claim check / DataBus (Azure Blob)" no longer states "Wolverine has no built-in claim check"; it now records that Wolverine ships the feature and Cloudstrap binds it to its conventions (amended 2026-09-21, pointing here). | Finding 1: the premise went stale between 2026-07-24 and Wolverine 6.22/6.32; building the middleware would duplicate a maintained engine feature already in the closure. |
| **DL-6** *(user ruling 2026-09-21, ex OQ-2)* | **Offload by whole-body size threshold only** — `OffloadThresholdBytes`, default **200 KiB (204 800 bytes)**, bodies strictly larger are offloaded. The source's `*DataBus` property-name convention is **dropped** and not rebuilt; Wolverine's `[Blob]` is documented as the engine-native per-property opt-in for consumers who accept a Wolverine reference in their contracts. | Finding 3: Wolverine has no name-convention hook; the threshold is zero bespoke code, keeps contracts dependency-free with no naming discipline, and is size-aware where the source convention was not. 200 KiB leaves headroom under ASB Standard's 256 KB limit (measured incl. headers). |
| **DL-7** *(user ruling 2026-09-21, ex OQ-3)* | **Bump all five `WolverineFx.*` CPM pins** in `src/Directory.Packages.props` (`WolverineFx`, `.AzureServiceBus`, `.SqlServer`, `.EntityFrameworkCore`, `.RuntimeCompilation`; 6.31.0 at ruling time) **in lockstep to the latest stable release and reference `WolverineFx.AzureBlobStorage`** (first version 6.33.0). `WolverineFx.ClaimCheck.AzureBlobStorage` is not used. The bump is gated by #14's full unit + E2E suite; the plan must note the Weasel 9.30.0 schema-comparison change (possible `ALTER TABLE` on persistent dev databases such as the demo's `CloudstrapDemo`). See Planner note 1 for the pin-time re-check. | Wolverine releases every few days; starting the leaf on the consolidated package avoids a known migration; CPM lockstep forbids bumping only the leaf. Dependency update to a shipped package = risk area, hence the regression gate. |
| **DL-8** *(user ruling 2026-09-21, ex OQ-4)* | **Amend the shipped `CloudstrapMessagingBuilder` (#14) with a public `ConfigureEngine(Action<IServiceProvider, WolverineOptions>)` contribution hook.** Contributions are stored on `MessagingRegistrationState` and applied by `CloudstrapMessagingExtension` **before** the consumer's `Wolverine` delegate and **before** `RetryLadder.Apply`; the extension takes an `IServiceProvider` constructor dependency. Additive member on an existing type — `PackageSurfaceTests`' approved type list is unchanged. The leaf-only `IWolverineExtension` and `InternalsVisibleTo` alternatives were rejected. | Finding 5: without it the leaf's DL-1 rule would land behind the catch-all ladder and never match, and the consumer's "final say" contract would invert. Establishes the leaf-extension door #14's remarks promised; the PostgreSQL leaf reuses it. |
| **DL-9** *(user ruling 2026-09-21, ex OQ-5)* | **Dedicated container `{SystemName}-claimcheck` (lower-cased) by default**, `Cloudstrap:Messaging:ClaimCheck:ContainerName` override; **account and credential come from the DI-registered blob client** in the fixed ladder `settings.ContainerClient` → registered `BlobServiceClient` → registered `BlobContainerClient`'s parent service (#4's `AddCloudstrapBlobStorage`); **no leaf-owned account settings** (no `BlobServiceUri`/`ConnectionString` duplicate of `Cloudstrap:Storage`). Container-creation posture confirmed as-is: Wolverine creates the container on first use, independent of `Cloudstrap:Messaging:AutoProvision`; documented (create rights on the account, or IaC pre-creation). No wrapping of the store to gate creation. | Findings 6–7: a lifecycle-management policy is only safe advice on a container that holds nothing but claim-check blobs; reusing #4's application container (which may also hold the DataProtection key ring) would make "delete after N days" a production incident; a second copy of the account settings is more code and a second place to misconfigure. |
| **DL-10** *(user ruling 2026-09-21, ex OQ-6)* | **E2E/CI blob backend: Azurite by default, fixture-started, `CLOUDSTRAP_TEST_BLOB` override** — the D-3 template. Demo Api + Worker set `Cloudstrap:Storage:ConnectionString = UseDevelopmentStorage=true` in their own appsettings (explicit demo config, not a package opinion). When `CLOUDSTRAP_TEST_BLOB` is unset the E2E fixture starts an `azurite` executable from PATH (`--location` in a per-run temp folder, `--silent`, blob endpoint `127.0.0.1:10000`) and stops it at teardown; when set, the fixture forwards that connection string to every host it spawns (attach mode) and starts nothing. CI provides Azurite and sets `CLOUDSTRAP_TEST_BLOB` on the test step (Planner note 2). `Testcontainers.Azurite` (MIT) is recorded as the follow-up only; Docker is not an E2E prerequisite. | Mirrors D-3 exactly (default local backend + env override + Testcontainers follow-up); a fresh clone stays green after one `npm install -g azurite`; the fixture already owns the IdP/Api/Bff/Worker processes, so owning one more matches its model. Azurite is MIT and the only Azure Blob emulator. |

---

## Open Questions

**None.** OQ-1…OQ-6 were ruled on by the user on 2026-09-21 (all in favor of the analyst's option (a))
and are recorded as DL-5…DL-10 above; their conditional text throughout this spec has been folded into
the corresponding decisions. The spec is planner-ready.

---

## Planner notes

Non-decisions the planner must act on; they refine *how the rulings are carried out*, not *what* was
decided.

1. **Re-check the Wolverine pin before pinning (DL-7).** The release evidence in this spec ends at
   **6.33.0 as of 2026-09-03** (the last day the analyst gathered evidence). Wolverine ships every few
   days, so by plan time newer releases almost certainly exist. Before editing
   `src/Directory.Packages.props` the planner must consult
   [github.com/JasperFx/wolverine/releases](https://github.com/JasperFx/wolverine/releases) and nuget.org
   for the **current latest stable** `WolverineFx` release, pin **all five** family packages plus the new
   `WolverineFx.AzureBlobStorage` to that same version (CPM lockstep; ≥ 6.33.0 is the floor, not the
   target), and read the intervening release notes for anything touching #14's surface (SQL Server
   durability schema/Weasel, EF Core outbox, ASB transport, failure-rule APIs, the claim-check `cc`
   configuration type named in the Public API Sketch). Record the version and the checked notes in the
   plan; #14's full unit + E2E suite is the regression gate for the bump.

2. **Azurite in CI and `CLOUDSTRAP_TEST_BLOB` follow the SQL pattern already in the repo (DL-10).** Since
   this spec's evidence was gathered, two commits landed the D-3 CI shape that OQ-6 flagged as missing:
   - **7596f85** (`ci: provide SQL Server for the D-3 test prerequisite on Linux runners`) —
     `.github/workflows/ci.yml` (which runs on `ubuntu-latest`) now declares a job-level `services:`
     container `mcr.microsoft.com/mssql/server:2022-latest` with a health check, and the
     **"Run all MTP test executables"** step sets `CLOUDSTRAP_TEST_SQL` to a connection string pointing at
     `localhost,1433` (CI-local password, never leaves the job).
   - **1c552b7** (`test(e2e): forward the CLOUDSTRAP_TEST_SQL override to the Worker host fixture`) —
     every E2E spawn point reads the env var and forwards it as a host argument: `E2eFixture.cs` for the
     Api (`--ConnectionStrings:DefaultConnection=<value>` appended to the `SutProcess.Start(...)`
     arguments) and `MessagingTests.cs` / `WorkerHostTests.cs` for the Worker hosts they start.

   The planner mirrors this exactly for the blob backend: (i) in `ci.yml`, add an **Azurite `services:`
   container** (`mcr.microsoft.com/azure-storage/azurite`, blob port `10000`, with a health check) next to
   `sqlserver`, and set **`CLOUDSTRAP_TEST_BLOB`** on the same test step to a connection string targeting
   `127.0.0.1:10000` with Azurite's well-known development account (`UseDevelopmentStorage=true` is
   acceptable if the mapped port matches Azurite's default) — the service-container form is the closest
   mirror of 7596f85; a `npm install -g azurite` step is the fallback if the container proves awkward;
   (ii) in the E2E project, read `CLOUDSTRAP_TEST_BLOB` at **every** host spawn point that 1c552b7 touched
   (`E2eFixture` for the Api, `MessagingTests`/`WorkerHostTests` for the Worker) and forward it as
   `--Cloudstrap:Storage:ConnectionString=<value>`; when the variable is unset, the fixture starts Azurite
   itself per DL-10 and the demo appsettings' `UseDevelopmentStorage=true` takes effect. The
   `Cloudstrap.Messaging.AzureBlob.Tests` unit suite must **not** depend on either variable (AC-M3).
   The OQ-6 observation that CI "sets no `CLOUDSTRAP_TEST_SQL`" is obsolete — 7596f85 resolved it.
