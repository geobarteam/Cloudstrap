# Cloudstrap.Messaging

A durable [Wolverine](https://wolverinefx.net) messaging node in one call: local, Azure Service Bus or
SQL Server transports selected by configuration, suffix conventions and workload routing for
dependency-free message contracts, a transactional EF Core inbox/outbox, bounded retries and
dead-lettering, business correlation on the suite's shared vocabulary, and OpenTelemetry that rides
whatever pipeline the host already has. No hand-assembled bus, no lost or duplicated messages.

Wolverine's own types stay first-class in your code: inject `IMessageBus` to send, publish or invoke,
write plain Wolverine handlers, and use `IDbContextOutbox<TDbContext>` on the HTTP path. There is no
Cloudstrap facade over the bus.

## Quick start

```csharp
// Producer (an HTTP host, e.g. the demo Api)
builder.AddCloudstrapMessaging()                              // Cloudstrap:Messaging section
    .UseSqlServer()                                           // durable inbox/outbox on ConnectionStrings:DefaultConnection
    .AddCloudstrapTransactionalMessaging<OrdersDbContext>();  // entity + message commit atomically

// Consumer (a headless node, e.g. the demo Worker)
builder.AddCloudstrapMessaging()
    .UseSqlServer()
    .AddCloudstrapTransactionalMessaging<WorkerDbContext>();
```

With no `Cloudstrap:Messaging` section at all, `AddCloudstrapMessaging()` runs an in-process node:
no network, no SQL, no Azure — the whole suite works on a fresh clone. Configuration alone flips it
to a broker.

Handlers are plain Wolverine handlers, discovered in the host's entry assembly:

```csharp
public static class PlaceOrderCommandHandler
{
    public static async Task Handle(PlaceOrderCommand command, WorkerDbContext db, IMessageBus bus)
    {
        db.Orders.Add(new Order { Id = command.OrderId });
        await bus.PublishAsync(new OrderPlacedEvent(command.OrderId)); // committed with the row
    }
}
```

Non-handler code (an endpoint, a background job) gets the same atomicity through the outbox —
the explicit three-line pattern:

```csharp
IDbContextOutbox<OrdersDbContext> outbox = /* injected */;
outbox.DbContext.Orders.Add(order);                     // 1. stage the entity
await outbox.SendAsync(new PlaceOrderCommand(order.Id)); // 2. stage the message
await outbox.SaveChangesAndFlushMessagesAsync();         // 3. one transaction, then dispatch
```

## Settings — `Cloudstrap:Messaging`

Every convention has an override. Connection strings are resolved by **name** through the standard
`ConnectionStrings:` section; no setting in this section ever carries a secret.

| Key | Default | Meaning |
|---|---|---|
| `Transport` | `Local` | `Local`, `AzureServiceBus` or `SqlServer`. An unknown value fails at the call, naming the key. |
| `EndpointName` | `Cloudstrap:Application` workload name (`{system}-{subsystem}-{type}`) | The node's identity: its inbox queue and the subscriptions it creates. |
| `AutoProvision` | `null` → on in `Development` only | Create queues, topics, subscriptions and durability tables at startup. An explicit value wins. |
| `AzureServiceBus:FullyQualifiedNamespace` | — | `contoso.servicebus.windows.net`, authenticated with `DefaultAzureCredential`. Required on ASB unless the connection-string name resolves. |
| `AzureServiceBus:ConnectionStringName` | — | Name of a `ConnectionStrings:` entry — the local-emulator fallback. |
| `SqlTransport:ConnectionStringName` | `DefaultConnection` | The database holding the queue tables (SQL Server transport). |
| `SqlTransport:SchemaName` | Wolverine's default | Schema of the queue tables. **Sender and listener must share it.** |
| `Durability:ConnectionStringName` | `DefaultConnection` | The database holding the message store (`UseSqlServer()`). |
| `Durability:SchemaName` | sanitized workload name | Schema of the inbox/outbox/dead-letter tables; `contoso-orders-worker` → `contoso_orders_worker`. |
| `Retries:NumberOfImmediate` | `5` | In-process retries before the scheduled stage. |
| `Retries:NumberOfDelayed` | `5` | Scheduled retries with a doubling cooldown (5 s, 10 s, 20 s, …) before dead-lettering. |
| `DeadLetter:QueueName` | `{SystemName}-error` | The transport-level error queue, where one materializes. |
| `Destinations` | empty | Command routing map: key = message namespace or type-name prefix, value = destination workload (endpoint) name. |

`Destinations` is a dictionary: the configuration binder **adds** to it. Entries set in code and in
configuration merge, a key present in both takes the configuration value, and configuration cannot
remove an entry added in code.

The sibling block consumed here (owned by `Cloudstrap.Core`, shared with the HTTP correlation
middleware of `Cloudstrap.Observability`):

| Key | Default | Meaning |
|---|---|---|
| `Cloudstrap:Correlation:HeaderName` | `X-Correlation-ID` | The envelope header carrying the business correlation id — the same header HTTP uses. |
| `Cloudstrap:Correlation:Message:RequireForAllMessageHandlers` | `false` | Every handler requires a correlation id; sends without one are blocked too. |
| `Cloudstrap:Correlation:Message:ExcludeMessageHandlers` | empty | Full type names of handlers exempt from the requirement. |

## Conventions and routing (workload-centric topology)

Message contracts need **zero package references**: classification is by type-name suffix.

| Suffix | Kind | Azure Service Bus | SQL Server |
|---|---|---|---|
| `*Command`, `*Message` | command-like | sent to the destination workload's queue via `Destinations` | queue via `Destinations` |
| `*Event` | event | published to a topic per event type; each consuming workload subscribes under its own endpoint name | queue via `Destinations` (queues only) |
| anything else | unclassified | handled locally, or routed explicitly | same |

- The node listens on its own queue, named after its endpoint (workload) name.
- A type this node **handles locally is handled locally**, ahead of any convention route. The
  `Destinations` map is for the commands a node sends elsewhere.
- Wolverine sanitizes broker identifiers: Azure Service Bus names are lowercased; SQL Server queue
  and schema identifiers replace `-` with `_`.
- Every rule is a replaceable delegate on `MessageConventions` (`Classify`, `DestinationFor`,
  `TopicNameFor`), adjusted through the configurator; `configurator.Wolverine` runs **last** with
  full control of the engine:

```csharp
builder.AddCloudstrapMessaging(configurator =>
{
    configurator.Conventions = conventions =>
        conventions.DestinationFor = type => type.Namespace!.StartsWith("Contoso.Billing") ? "contoso-billing-worker" : null;
    configurator.Wolverine = options =>
        options.PublishMessage<AuditRecordedEvent>().ToAzureServiceBusTopic("audit"); // explicit routes always win
});
```

One startup log line states the posture in force: transport, endpoint name, every destination,
durability, dead-letter posture and the effective `AutoProvision` value.

## Durability and dead-lettering

- Without a provider the node runs buffered and non-durable, and says so at startup.
- `UseSqlServer()` turns on the durable inbox/outbox and durable local queues. The store lives in a
  schema per workload, so many workloads share one database without collision (the isolation unit
  is a schema, not a table-name prefix). With the SQL Server transport the store lives on the
  transport's database.
- Failed messages exhaust the retry ladder and land in the store's **`wolverine_dead_letters`
  table** — queryable and replayable. The `{SystemName}-error` name applies to the transport-level
  error queue wherever one materializes (a non-durable Azure Service Bus node, for example).
- The retry ladder is the engine's **last** global failure rule: exception-specific rules added
  through `configurator.Wolverine` (`options.Policies.OnException<T>()`) match first.
- Logging on failure carries the message **type and id, never the payload**.

`AddCloudstrapTransactionalMessaging<TDbContext>()` requires a durability provider; without
`UseSqlServer()` the host fails at startup naming it. The two calls compose in any order. Note that
EF Core's `EnsureCreated` is a no-op once Wolverine's tables exist in a database: create your own
tables through migrations, or before the node starts.

## Correlation

The business correlation id flows on the configured header from the ambient
`ICorrelationContextAccessor` (set by the HTTP middleware, or by you) onto every outgoing envelope,
and back into the accessor on the receiving side — so a remote handler sees the original inbound
value. W3C `traceparent` flows through OpenTelemetry regardless.

Enforcement uses the suite's one vocabulary: `RequireForAllMessageHandlers`, `[CorrelationRequired]`
on a handler method, class or base class, `[AllowNoCorrelation]` to exempt one, and
`ExcludeMessageHandlers` to exempt by name. A blocked message raises a `CorrelationRequiredException`
naming the header and the handler and is dead-lettered without retries; a send without a
correlation id while every handler requires one is blocked the same way, at the call.

## Observability

Wolverine's `ActivitySource` and `Meter` (both named `Wolverine`) are registered **additively** into
whatever OpenTelemetry pipeline the host builds — Cloudstrap's owner or contribute mode, a
consumer's own, or Aspire ServiceDefaults. This package registers no exporter and no provider; with
no pipeline at all it is inert and the host still starts.

## Security baseline

- Credentials never live in this section: Azure Service Bus uses `DefaultAzureCredential`
  (environment, workload identity, managed identity); the connection-string fallback is resolved
  by name.
- Validation failures, startup logs and exceptions name configuration **keys**, never values.
- Transport encryption is TLS in transit and Azure Service Bus encryption at rest. Property-level
  message encryption is deliberately not provided.
- One node per process: a second `AddCloudstrapMessaging()` call throws at the call site.

## Verifying against a real Azure Service Bus namespace (manual procedure)

Never automated — the test suite touches no network. To prove the transport end to end:

1. Create a namespace and grant your identity **Azure Service Bus Data Owner** (provisioning needs
   management rights; runtime needs Data Sender/Receiver).
2. Configure two hosts (the demo Api and Worker work) with `Transport = AzureServiceBus`,
   `AzureServiceBus:FullyQualifiedNamespace = <name>.servicebus.windows.net`, `AutoProvision = true`
   for the first run, a `Destinations` entry from the contracts namespace to the consumer's
   workload name, and `Cloudstrap:OpenTelemetry` pointed at Application Insights.
3. Start the consumer, then the producer; send a command with an `X-Correlation-ID` header on the
   producer's HTTP endpoint.
4. Verify in the portal: the consumer's queue (`{workload}`), a topic per published event type with
   a subscription named after the consumer, and the `{SystemName}-error` queue.
5. Verify in Application Insights: one operation spanning producer and consumer (`traceparent`),
   and the same `X-Correlation-ID` on both sides' logs.

## Migration notes (deliberate changes from the source library)

1. **Engine swap, no wire compatibility.** Wolverine envelopes are not NServiceBus-compatible; a
   Cloudstrap node cannot exchange messages with an existing NServiceBus endpoint.
2. **Durability isolation is a schema, not a table prefix** — same shared-database guarantee.
3. **Dead-lettering** moved from an error queue to the durable store's dead-letter table; the
   `{SystemName}-error` naming convention is kept where a transport queue materializes.
4. **No XML fallback deserializer** — System.Text.Json only, by default.
5. **No audit queue, no ServicePlatform heartbeats or metrics** — OpenTelemetry replaces them.
6. **Installer gating by an environment string became the explicit `AutoProvision` option** with a
   `Development` default.
7. **Credential selection by host sniffing became `DefaultAzureCredential`** with no secret-bearing
   configuration keys.
8. **The command-executor mediator is not ported** — `IMessageBus.InvokeAsync` plus transactional
   middleware is the supported path; functional result types are your own choice.
9. **A second registration fails fast** instead of being silently tolerated.
