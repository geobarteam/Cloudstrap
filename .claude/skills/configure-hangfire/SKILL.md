---
name: configure-hangfire
description: "Use when setting up, configuring, or extending Hangfire with Cloudstrap.Hangfire in a Cloudstrap project. Covers choosing the host role (processing host vs storage-only host), registration (AddCloudstrapHangfire + CloudstrapHangfireConfigurator), recurring tasks (IBackgroundRecurringTask), fire-and-forget and delayed jobs (Hangfire's IBackgroundJobClient), the authenticated dashboard (MapCloudstrapHangfireDashboard: role, policy, read-only), per-job config overrides (Cloudstrap:Hangfire:Jobs), server/storage tuning hatches, the storage health check (AddCloudstrapHangfireHealthCheck), and schema preparation. Use for: adding Hangfire to a worker or web host, adding a recurring job, enqueuing background work, exposing the dashboard, fixing 'No registered recurring task' / disappearing jobs / 401-403 dashboard / startup failures."
metadata:
  argument-hint: "Describe your scenario, e.g. 'add Hangfire to my worker', 'show the dashboard in my Blazor Server app', 'add a daily recurring job', 'enqueue a one-off job', 'add a Hangfire health check'"
---

# Configure Hangfire — host roles, recurring tasks, dashboard & health

Guide for wiring up `Cloudstrap.Hangfire` in a consuming Cloudstrap project.

> **Source of truth.** The package guide is the authoritative reference:
> [`src/Cloudstrap.Hangfire/README.md`](../../../src/Cloudstrap.Hangfire/README.md).
> For exact signatures and defaults read the code under `src/Cloudstrap.Hangfire/`
> (`HostApplicationBuilderExtensions`, `CloudstrapHangfireConfigurator`, `IBackgroundRecurringTask`,
> `EndpointRouteBuilderExtensions`, `HealthChecksBuilderExtensions`, `HangfireOptions` and its sub-options) — not
> external Hangfire docs. The spec is `_specs/16-HangfireScheduler.md`.

## The golden rules (read first)

1. **Exactly one processing host (one task set) per storage schema.** A processing host removes, at startup, every
   dispatcher-owned job it does not declare. Two different task sets on one schema remove each other's jobs.
2. **Independent processing hosts sharing a database each set their own `Cloudstrap:Hangfire:Storage:SchemaName`.**
3. **Every other host sets `RunServer = false`** (dashboard host, enqueue-only API, a future proxy host).
4. **All hosts of one application share the same storage and schema; tasks live with the processing host.**

## First action — contextual discovery

Before changing anything, find out:
- Which hosts exist (`Program.cs` of each) and which one should run the jobs.
- Whether the host is a generic host (worker) or a web host on a Cloudstrap composite (WebApi, MVC, Blazor Server),
  and which authentication scheme it registers.
- The `ConnectionStrings:` entry the job storage should use.
- Whether a `Cloudstrap:Hangfire` section already exists in `appsettings*.json`.

## Step 1 — Decide the host role

| Host | `RunServer` | Dashboard | Health check `failureStatus` |
|---|---|---|---|
| Worker that runs the jobs (processing host) | `true` (default) | usually not mapped | default (`Unhealthy`) |
| Web host that shows the dashboard (storage-only) | `false` | `MapCloudstrapHangfireDashboard()` | `HealthStatus.Degraded` |
| API that only enqueues work (storage-only) | `false` | not mapped | `HealthStatus.Degraded` |
| Proxy host forwarding the dashboard | — | deliverable #18 (`Cloudstrap.Hangfire.Proxy`), not yet shipped | — |

A single web host may also be the processing host (`RunServer = true` plus the dashboard) as long as rule 1 holds.

## Step 2 — Register (`AddCloudstrapHangfire`)

```csharp
builder.AddCloudstrapHangfire();                                     // processing host
builder.AddCloudstrapHangfire(hangfire => hangfire.RunServer = false); // storage-only host
```

- One call per host; a second call throws.
- Storage: SQL Server on `ConnectionStrings:{Storage:ConnectionStringName}` (default `DefaultConnection`), Hangfire's
  recommended options, Hangfire's default schema `HangFire` unless `Storage:SchemaName` is set.
- Discovery: concrete `IBackgroundRecurringTask` classes — public **or internal** — in the entry assembly, registered
  as the interface only, transient. Override with `TaskAssemblies`.
- A processing host registers the scheduling service **before** the Hangfire server: tasks are scheduled before
  anything is dequeued, and a scheduling failure stops startup.
- The `Cloudstrap.Hangfire` span source is contributed to the host's OpenTelemetry pipeline; no exporter.

Configurator hatches (each runs after the defaults it adjusts):

| Member | Use |
|---|---|
| `RunServer` | Host role. Code-level only — there is no configuration key. |
| `TaskAssemblies` | Assemblies to scan instead of the entry assembly. |
| `SqlServer` | `Action<SqlServerStorageOptions>` — tune storage (for example `QueuePollInterval`). |
| `Storage` | `Action<IGlobalConfiguration>` — bring your own storage; SQL Server and `Storage:*` are ignored. |
| `Server` | `Action<BackgroundJobServerOptions>` — final say over the server (for example `ServerName`). |

## Step 3 — Add a recurring task (`IBackgroundRecurringTask`)

```csharp
internal sealed class NightlyCleanupTask(OrdersDbContext db) : IBackgroundRecurringTask
{
    public string CronExpression => "0 3 * * *";

    public string JobId => "nightly-cleanup";     // recommended: stable across class renames

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await db.PurgeExpiredAsync(cancellationToken);
    }
}
```

| Member | Default |
|---|---|
| `CronExpression` | required (Cronos syntax) |
| `JobId` | the type name — rename-sensitive, override it for anything referenced in config |
| `TimeZone` | UTC (prefer IANA ids) |
| `Queue` | `default` |
| `IsEnabled` | `true` |
| `PreventOverlappingRuns` | `true` (a busy `recurring-task:{JobId}` lock skips the trigger with a warning) |
| `ExecuteAsync(CancellationToken)` | required — resolved per run in its own DI scope |

The task lives in the **processing host**. No Hangfire type is needed in the implementation.

## Step 4 — Fire-and-forget and delayed jobs

Inject Hangfire's `IBackgroundJobClient` (registered by `AddCloudstrapHangfire`). There is no Cloudstrap facade.

```csharp
public sealed class InvoiceService(IBackgroundJobClient jobs)
{
    public void Queue(int invoiceId) => jobs.Enqueue<InvoiceRenderer>(renderer => renderer.RenderAsync(invoiceId, CancellationToken.None));
}
```

The target method must be public on a type the processing host can resolve — that is where the job runs.

## Step 5 — Expose the dashboard (`MapCloudstrapHangfireDashboard`)

Map it inside a composite's `ConfigureEndpoints` hook (so it runs after the authentication middleware):

```csharp
app.UseCloudstrapBlazorServer<App>(pipeline => pipeline.ConfigureEndpoints = endpoints =>
{
    endpoints.MapCloudstrapAuthenticationEndpoints();
    endpoints.MapCloudstrapHangfireDashboard();
});
```

- Always requires an authenticated user; `Dashboard:RequiredRole` narrows to a role, `Dashboard:AuthorizationPolicy`
  replaces the built-in policy. With no role, any authenticated caller (for example a proxy host's token) is admitted.
- A Cloudstrap filter inside the dashboard also requires authentication, so `.AllowAnonymous()` never exposes it.
- The optional `configure` delegate over `DashboardOptions` is additive — **tighten, never loosen**.
- Mapping throws when the host registers no authentication scheme.
- `Dashboard:ReadOnly = true` hides trigger/delete/requeue.
- Hosts with a secure-only antiforgery cookie (the Cloudstrap composites) must serve the dashboard over **HTTPS** for
  dashboard actions to work.
- `X-Forwarded-Prefix` is not handled — proxying is deliverable #18.

## Step 6 — Per-job overrides and tuning

```jsonc
"Cloudstrap": { "Hangfire": {
  "Storage":   { "ConnectionStringName": "DefaultConnection", "SchemaName": "jobs", "PrepareSchema": true },
  "Server":    { "WorkerCount": 4, "Queues": [ "critical", "default" ] },
  "Dashboard": { "Path": "/hangfire", "RequiredRole": "ops", "ReadOnly": false },
  "Jobs": {
    "nightly-cleanup": { "Cron": "0 4 * * *", "TimeZone": "Europe/Brussels", "Enabled": true }
  }
} }
```

`Jobs` keys match `JobId` case-insensitively; an empty value falls back to the task's own; an override for an
undeclared id logs one warning.

## Step 7 — Health check

```csharp
builder.Services.AddHealthChecks().AddCloudstrapHangfireHealthCheck();                                   // processing host
builder.Services.AddHealthChecks().AddCloudstrapHangfireHealthCheck(failureStatus: HealthStatus.Degraded); // storage-only host
```

Name `hangfire`, tag `ready` (served by `/ready`), statistics as data; the result never carries a connection string
or exception message (the detail is logged at Warning).

## Schema preparation

`Storage:PrepareSchema` unset → Hangfire prepares its schema in `Development` only. Elsewhere deploy Hangfire's
`Install.sql` from infrastructure-as-code. A processing host against a missing schema fails at startup. Managed
identity: `Authentication=Active Directory Default` in the connection string (the shipped `Microsoft.Data.SqlClient`
6.x supports it in the core package).

## Validation rules — actively flag these

| Rule | Failure |
|---|---|
| `Storage:ConnectionStringName` must resolve under `ConnectionStrings:` (unless the `Storage` hatch is used) | throws at `AddCloudstrapHangfire`, naming the key |
| `Storage:SchemaName`, when set, is non-empty | `ConfigurationValidationException` |
| `Server:WorkerCount` ≥ 1; `Server:Queues` has no empty entry | `ConfigurationValidationException` |
| `Dashboard:Path` rooted, no trailing slash | `ConfigurationValidationException` |
| `JobId` unique across tasks (case-insensitive) | startup fails naming the id and both types; nothing written |
| `Jobs:{id}:TimeZone` is a known IANA id | startup fails naming job, value and key; nothing written |
| Cron expression accepted by Hangfire | startup fails naming job, value and key |
| Only one `AddCloudstrapHangfire` per host | `InvalidOperationException` |
| An authentication scheme exists before mapping the dashboard | `InvalidOperationException` |

## Troubleshooting (symptom → cause → fix)

| Symptom | Cause | Fix |
|---|---|---|
| `No registered recurring task with id 'X' was found on this host` | A host without the task runs a server | `RunServer = false` on that host |
| Recurring jobs disappear after a deployment | Two processing hosts with different task sets share a schema | Own `Storage:SchemaName` per independent processing host |
| Dashboard 401 / redirect to login | Not authenticated, or mapped before the auth middleware | Map inside `ConfigureEndpoints` and sign in |
| Dashboard 403 | Not in `RequiredRole` / fails `AuthorizationPolicy` | Grant the role or adjust the policy |
| Dashboard action fails (antiforgery) | Secure-only antiforgery cookie over HTTP | Serve over HTTPS |
| Startup fails naming a job | Duplicate id, unknown time zone, rejected cron | Fix the task or the `Jobs:` override |
| `Invalid object name '…'` at startup | Schema missing | Deploy `Install.sql` or set `Storage:PrepareSchema` for dev |

## Checklist

- [ ] Exactly one processing host per storage schema; every other host `RunServer = false`.
- [ ] `ConnectionStrings:` entry present on every host; same schema across the application.
- [ ] Tasks implement `IBackgroundRecurringTask` in the processing host, with stable `JobId` overrides where referenced.
- [ ] Dashboard mapped only on a host with an authentication scheme, inside `ConfigureEndpoints`, over HTTPS.
- [ ] `AddCloudstrapHangfireHealthCheck()` registered (`Degraded` on storage-only hosts).
- [ ] Production schema deployed by IaC (`PrepareSchema` left unset or `false`).
- [ ] The LGPL-3.0 license of the Hangfire family reviewed (see the package README).
