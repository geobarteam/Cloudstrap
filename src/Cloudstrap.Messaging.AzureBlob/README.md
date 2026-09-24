# Cloudstrap.Messaging.AzureBlob

Transparent large-message handling for [`Cloudstrap.Messaging`](../Cloudstrap.Messaging/README.md): any
message whose serialized body exceeds a size threshold is stored in a dedicated Azure Blob Storage
container while only a reference travels through the transport; the receiving handler sees the whole
message and knows nothing of blobs. One builder call, no contract changes, no account settings of its
own — the storage account and credential come from the blob client the host already registered.

The package is a thin seam over [Wolverine](https://wolverinefx.net)'s native claim check and its Azure
Blob store (`WolverineFx.AzureBlobStorage`). Cloudstrap owns exactly: the builder call, one options
block, the client-resolution ladder, one failure rule, one failure log line, one posture log line — and
this document.

## Quick start

```csharp
// Any host type — the blob registration (Cloudstrap.Extensions) supplies account + credential.
builder.AddCloudstrapBlobStorage();                         // Cloudstrap:Storage → BlobContainerClient (DefaultAzureCredential)

builder.AddCloudstrapMessaging()                            // Cloudstrap:Messaging
    .UseSqlServer()                                         // durable inbox/outbox
    .AddCloudstrapTransactionalMessaging<OrdersDbContext>() // outbox atomicity
    .UseAzureBlobClaimCheck();                              // bodies > 200 KiB → {system}-claimcheck
```

Sender and receiver must both call `UseAzureBlobClaimCheck()` on the same account: the receiver loads
what the sender stored. With the default settings the two agree by convention (same container name
derived from `Cloudstrap:Application:SystemName`, same threshold).

Contracts stay dependency-free — the threshold, not a naming convention or an attribute, decides what is
offloaded:

```csharp
public sealed record PlaceOrderCommand(Guid OrderId, string? Notes = null); // Notes may be megabytes
```

Code-level hooks, the `AzureCredentialSettings` idiom:

```csharp
builder.AddCloudstrapMessaging().UseAzureBlobClaimCheck(claimCheck =>
{
    claimCheck.ContainerClient = myContainerClient;                       // wins over anything registered
    claimCheck.ClaimCheck = cc => cc.StoreForMessage<ExportReadyEvent>(otherStore, 64 * 1024); // Wolverine's own knobs
});
```

## Settings — `Cloudstrap:Messaging:ClaimCheck`

Every convention has an override. No setting in this block carries an account, a URI or a secret.

| Key | Default | Meaning |
|---|---|---|
| `OffloadThresholdBytes` | `204800` (200 KiB) | A body **strictly larger** than this is stored in the container; the envelope carries Wolverine's `claim-check.$body` reference and an empty wire body. Must be positive. The default leaves headroom under Azure Service Bus Standard's 256 KB limit, which counts headers. |
| `ContainerName` | `{SystemName}-claimcheck`, lower-cased (`demo-claimcheck`) | The dedicated container the payloads live in. Must satisfy Azure's container naming rules (3–63 lower-case letters, digits and single hyphens, alphanumeric at both ends). Pointing it at the application container (`Cloudstrap:Storage:ContainerName`) is allowed — you then own the lifecycle risk described below. |

An invalid value fails at the `UseAzureBlobClaimCheck()` call and again at host startup, naming the exact
key — never echoing a value. A system name that yields an invalid default container name (an underscore,
for example) fails the same way, pointing at `ContainerName` as the fix.

Wolverine's own configuration stays reachable through `AzureBlobClaimCheckSettings.ClaimCheck`, which runs
last inside `UseClaimCheck`: `StoreForMessage<T>(store, threshold)` for a per-type store or threshold,
`StoreWhen(...)` for per-envelope routing. Do **not** assign `Store` there (the Azure store already is the
node's `IClaimCheckStore`) and do not set a payload time to live (`DeletePayloadsOlderThan`): both make
Wolverine register services at a point of the bootstrap where it forbids that, and the Azure store does not
sweep — lifecycle is the container policy below.

## Where the account comes from (the client ladder)

Resolved once, when the host starts, from what the host registered — in this order:

1. **Code** — `settings.ContainerClient`. Wins over everything; `ContainerName` is ignored (the client
   already names its container) and the posture line says so.
2. **A registered `BlobServiceClient`** — yours, or the one an Aspire app registers through
   `Aspire.Azure.Storage.Blobs`. The claim-check container is opened on it.
3. **The `BlobContainerClient` registered by `AddCloudstrapBlobStorage`** (`Cloudstrap:Storage`): the
   claim-check container is opened on that client's account with that client's credential.

None of these → the host fails at startup naming `AddCloudstrapBlobStorage()` and the register-a-client
alternative. The package constructs **no credential** and no client against another account: it reuses
what is there. The store is also registered as Wolverine's `IClaimCheckStore`, so your code can resolve
it for ad-hoc operations.

One Information line at startup states the posture:

```
Cloudstrap claim check: container 'demo-claimcheck', offload bodies larger than 204800 bytes, client from AddCloudstrapBlobStorage
```

Client sources are `code`, `BlobServiceClient` and `AddCloudstrapBlobStorage`. No account URI and no
connection string ever appear — in logs, validation failures or exceptions.

Local development runs against the [Azurite](https://github.com/Azure/Azurite) emulator
(`Cloudstrap:Storage:ConnectionString = UseDevelopmentStorage=true`). Start it with
`azurite-blob --skipApiVersionCheck`: the Azure SDK this suite pins may speak a newer service version than
the installed emulator knows.

## The container: creation, rights, lifecycle

**A dedicated container on purpose.** `{system}-claimcheck` holds nothing but claim-check payloads, so a
lifecycle-management policy on it can never touch application blobs or the DataProtection key ring that
may share the account.

**Creation and rights.** The container is created **on first use** — on the first upload that finds it
missing — independently of `Cloudstrap:Messaging:AutoProvision` (Wolverine's store uploads straight into
the container and creates nothing; this package adds the create-if-missing step around it). The identity
therefore needs **container-create rights on the account**, or the container is pre-created by
infrastructure-as-code (then nothing is ever created). At runtime it needs **blob read and write on the
container** (`Storage Blob Data Contributor` covers both). One Information line announces a creation.

**Retention.** Payloads are **kept** after handling. Several subscribers of one event read one blob, a
retried or scheduled delivery re-reads it, and a dead-lettered message must stay replayable. Two facts to
size a policy by:

- A dead-lettered large message holds **two** blobs: the original payload and a second one Wolverine
  writes when it re-serializes the envelope into the dead-letter row.
- **The orphan case.** With the transactional outbox, a message is serialized — and its payload
  uploaded — when it is enrolled in the transaction, *before* the commit. A transaction that rolls back
  after that point (a handler that throws after sending) leaves exactly one blob nothing will ever read.

**Cleanup is an Azure Storage lifecycle-management policy, not code.** Choose a retention *N* that
comfortably exceeds the longest window in which a message can still need its payload: the whole retry
ladder (scheduled retries included), plus the time a dead-lettered message may wait before it is replayed.
Fourteen days is a reasonable starting point for the default ladder. A sample rule scoped to the
claim-check container:

```json
{
  "rules": [
    {
      "enabled": true,
      "name": "expire-claim-check-payloads",
      "type": "Lifecycle",
      "definition": {
        "filters": { "blobTypes": [ "blockBlob" ], "prefixMatch": [ "demo-claimcheck/" ] },
        "actions": { "baseBlob": { "delete": { "daysAfterModificationGreaterThan": 14 } } }
      }
    }
  ]
}
```

A payload the policy removes while a message still needs it fails that delivery deterministically (see the
next section) — so err on the long side.

## Failure semantics

**A missing payload is a deterministic failure.** When the referenced blob answers 404, the message is
dead-lettered in one attempt — the retry ladder never runs — into the durable store's `wolverine_dead_letters`
table (or the transport's `{SystemName}-error` queue on a non-durable node). The dead-letter row keeps the
message type and the `Azure.RequestFailedException`; Wolverine's own line names the envelope id; the
package writes one `Error` line naming the **payload id, the container and the HTTP status** — never the
payload, never a URI or a connection string. Once the payload is available again the row can be replayed.

**Transient storage faults are the Azure SDK's job.** The payload is restored while the envelope is
deserialized, and Wolverine dead-letters a deserialization failure *before* any failure rule — the
package's, or yours — is consulted. Retrying a throttled (503) or timed-out read therefore happens inside
the Azure SDK's own retry policy on the real client (`BlobClientOptions.Retry`: several attempts with
back-off, on by default), not on the message ladder. If the SDK gives up, the message is dead-lettered
like a missing payload and stays replayable. Consequently, a consumer failure rule added through
`configurator.Wolverine` cannot intercept a payload-load failure either; the package's own
`OnException<RequestFailedException>(404) → MoveToErrorQueue()` rule is kept ahead of the ladder for a 404
raised anywhere else in a handler.

**Handler failures behave exactly as without the claim check**: the `Cloudstrap:Messaging:Retries` ladder
applies, each attempt re-reads the payload, the side effect happens once, and the blob is retained.

## Outbox interplay

With `AddCloudstrapTransactionalMessaging<TDbContext>()`, offloading happens when the outgoing envelope is
serialized for the outbox — **inside** `SaveChangesAndFlushMessagesAsync` (or `SaveChangesAsync`), before
the database commit. Upload latency therefore lands on the HTTP request that stages the message, and an
envelope committed but not yet dispatched (a crash in between) is recovered by the next node on the store
and delivered whole: the payload is read from the container, not from the row. A transaction rolled back
after serialization leaves the orphan blob described above.

## Correlation, telemetry, Aspire

The business correlation id is an envelope header and offloading is a body concern: correlation flows
exactly as in `Cloudstrap.Messaging`, and Wolverine's spans and metrics reach the host's OpenTelemetry
pipeline unchanged. This package registers no exporter and no provider.

**Aspire coexistence.** An Aspire application registers a `BlobServiceClient` through
`Aspire.Azure.Storage.Blobs`; the ladder prefers it, so **"Cloudstrap's blob registration or Aspire's —
not both"** holds with zero `Aspire.*` reference in this package. Register one or the other, then call
`UseAzureBlobClaimCheck()`.

## Wolverine's per-property opt-in

Whole-body offload by threshold is the Cloudstrap convention. Wolverine also offers explicit per-property
offload with its `[Blob]` attribute on a `byte[]`/`Stream`/`string` property. It is not wrapped here
because it costs a Wolverine reference in your contracts; if you accept that, it works unchanged alongside
the threshold (per-property blobs are offloaded first, then the remaining body is measured).

## Verifying against a real storage account (manual procedure)

Never automated — the test suite touches no network. To prove the store end to end:

1. Create a storage account; grant your identity **Storage Blob Data Contributor** on it (container
   creation needs the account-level scope; or pre-create `{system}-claimcheck`).
2. Configure two hosts (the demo Api and Worker work) with `Cloudstrap:Storage:BlobServiceUri =
   https://<account>.blob.core.windows.net`, `DefaultAzureCredential` signed in, both calling
   `AddCloudstrapBlobStorage()` and `UseAzureBlobClaimCheck()`.
3. Start the consumer, then the producer; read the posture line on both.
4. Post a message with a body over 200 KiB; observe exactly one new blob in `{system}-claimcheck`, the
   consumer's handler receiving the whole message, and no blob for a small message.
5. Delete the blob of a message parked for replay, replay it, and observe the one-attempt dead-letter with
   the package's log line.

## Migration notes (deliberate changes from the source library)

1. **Engine and pattern swap** — the NServiceBus data bus became Wolverine's claim check; there is no wire
   compatibility (already true of `Cloudstrap.Messaging`).
2. **Offload trigger: property-name convention → whole-body size threshold.** The size-blind `*DataBus`
   property convention is dropped and not rebuilt; contracts stay dependency-free either way, and
   Wolverine's `[Blob]` remains available for explicit per-property offload.
3. **Silent no-op on incomplete settings → fail fast at startup** naming the missing registration or key.
4. **Account and credential: a hard-coded `https://{system}.blob.core.windows.net` plus a client secret →
   the host's registered blob client** (`DefaultAzureCredential` through `AddCloudstrapBlobStorage`, or
   Aspire's).
5. **Container: the application's own container → a dedicated `{system}-claimcheck` container**, so a
   lifecycle policy is safe.
6. **A missing blob dead-letters immediately** instead of after the full retry ladder (the source had no
   defined behavior; NServiceBus would have retried).
7. **The encrypted data-bus path is removed**; property-level message encryption is deliberately not
   provided.
8. **The reference format is Wolverine's** (`claim-check.*` headers), not a Cloudstrap-defined one.
