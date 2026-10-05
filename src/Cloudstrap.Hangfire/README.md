# Cloudstrap.Hangfire

Hangfire in one call: SQL Server storage with Hangfire's recommended settings, recurring tasks declared by
implementing one interface and scheduled at startup, per-job overrides from configuration, overlap prevention,
cleanup of removed tasks, a dashboard that always requires authentication, a readiness health check, and
additive OpenTelemetry.

## License notice

`Cloudstrap.Hangfire` itself is **MIT**. It depends on the Hangfire family, which is **LGPL-3.0**:

| Package | License | How it arrives |
|---|---|---|
| `Hangfire.Core`, `Hangfire.SqlServer`, `Hangfire.AspNetCore` | LGPL-3.0 (Hangfire OÜ) | Direct dependencies |
| `Hangfire.NetCore` | LGPL-3.0 | Transitive, through `Hangfire.AspNetCore` |
| `Newtonsoft.Json` | MIT | Transitive, through `Hangfire.Core` |
| `Microsoft.Data.SqlClient` | MIT | Direct dependency (the SQL Server provider) |

Hangfire is multi-licensed and LGPL-3.0 by default; commercial licenses and Hangfire Pro exist. This package
uses the free tier only and references no Pro or in-memory storage package. Using an LGPL library as an
unmodified NuGet dependency is generally compatible with closed and open-source applications alike, but review
the terms against your own distribution model.

## Quick start

A **processing host** runs the Hangfire server and schedules the recurring tasks it declares:

```csharp
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.UseCloudstrapObservability();
builder.AddCloudstrapWorker();
builder.AddCloudstrapHangfire();                                         // storage + server + scheduling + discovery
builder.Services.AddHealthChecks().AddCloudstrapHangfireHealthCheck();   // ready-tagged
await builder.Build().RunAsync();

public sealed class NightlyCleanupTask : IBackgroundRecurringTask
{
    public string CronExpression => "0 3 * * *";

    public Task ExecuteAsync(CancellationToken cancellationToken) => /* the work */ Task.CompletedTask;
}
```

A **storage-only host** shows the dashboard (and may enqueue work) but never processes or schedules:

```csharp
builder.AddCloudstrapHangfire(hangfire => hangfire.RunServer = false);
builder.Services.AddHealthChecks().AddCloudstrapHangfireHealthCheck(failureStatus: HealthStatus.Degraded);
// …
app.UseCloudstrapBlazorServer<App>(pipeline => pipeline.ConfigureEndpoints = endpoints =>
{
    endpoints.MapCloudstrapAuthenticationEndpoints();
    endpoints.MapCloudstrapHangfireDashboard();
});
```

Both hosts read `ConnectionStrings:DefaultConnection` and share Hangfire's default schema with zero configuration.

## Mental model

Every recurring job is stored against one shared dispatcher, `RecurringTaskRunner.RunAsync(jobId, cancellationToken)`,
never against your task class. When the job fires, the processing host resolves the task whose `JobId` matches
and runs it. Because storage holds only the dispatcher and the job id, a storage-only host can list, trigger and
inspect the jobs without referencing the assembly that contains the tasks.

## Topology golden rules

- **Exactly one processing host (one task set) per storage schema.** At startup a processing host removes every
  dispatcher-owned job it does not declare, so two different task sets on one schema would remove each other's jobs.
- **Independent processing hosts sharing a database each set their own `Storage:SchemaName`.**
- **Every other host sets `RunServer = false`**: a dashboard host, an API that only enqueues work, a proxy host.
  It never dequeues, never schedules and never removes anything.
- **All hosts of one application share the same storage and schema.**
- **Tasks live with the processing host.** Scale a processing host out by running more instances of the same build:
  they declare the same task set, so they agree.

## `IBackgroundRecurringTask`

| Member | Default | Notes |
|---|---|---|
| `CronExpression` | *(required)* | Hangfire (Cronos) syntax: five fields, or six with seconds. |
| `JobId` | the type name | Shown in the dashboard and used as the `Cloudstrap:Hangfire:Jobs:{JobId}` key. **Rename-sensitive**: renaming the class creates a new job, and the old one is removed with its history. Override it for any task operators reference in configuration. |
| `TimeZone` | `TimeZoneInfo.Utc` | Prefer IANA ids (`Europe/Brussels`). |
| `Queue` | `default` | A server only runs jobs from the queues it listens to (`Server:Queues`). |
| `IsEnabled` | `true` | A disabled task's job is removed. |
| `PreventOverlappingRuns` | `true` | A trigger that fires while the previous run still holds the `recurring-task:{JobId}` lock is skipped with a warning. |
| `ExecuteAsync(CancellationToken)` | *(required)* | Resolved from DI per run, in its own scope, so it may depend on scoped services. Honor the token: it is signalled on shutdown. |

Discovery scans the entry assembly (or `TaskAssemblies`) for concrete implementations, **public or internal**, and
registers them as the interface only, transient. You can also register a task yourself; a task registered both ways
is declared once.

## Configuration reference

Section `Cloudstrap:Hangfire`. Validation runs at the `AddCloudstrapHangfire` call and again at startup; every
failure names the key and never echoes the value.

| Key | Default | Meaning |
|---|---|---|
| `Storage:ConnectionStringName` | `DefaultConnection` | The `ConnectionStrings:` entry holding the SQL Server storage. |
| `Storage:SchemaName` | *(unset: Hangfire's `HangFire`)* | The schema Hangfire's tables live in. |
| `Storage:PrepareSchema` | *(unset: `true` in Development only)* | Whether Hangfire creates or migrates its schema at startup. An explicit value always wins. |
| `Server:WorkerCount` | *(Hangfire default)* | Worker threads on a processing host; at least 1. |
| `Server:Queues` | *(`default` only)* | Queues the server listens to, in priority order; no empty entries. |
| `Dashboard:Path` | `/hangfire` | Rooted, no trailing slash; relative to the application path base. |
| `Dashboard:RequiredRole` | *(unset: any authenticated user)* | Role required to open the dashboard. |
| `Dashboard:AuthorizationPolicy` | *(unset)* | A named policy that replaces the built-in one. |
| `Dashboard:ReadOnly` | `false` | Hides trigger, delete and requeue actions. |
| `Jobs:{JobId}:Cron` | *(the task's own)* | Overrides the schedule. |
| `Jobs:{JobId}:TimeZone` | *(the task's own)* | Overrides the time zone; must be a known IANA id. |
| `Jobs:{JobId}:Enabled` | *(the task's own)* | `false` removes the job, `true` schedules a task disabled in code. |

`Jobs` ids are matched case-insensitively. An override for an id this host does not declare logs one warning.

```jsonc
"Cloudstrap": { "Hangfire": {
  "Server":    { "WorkerCount": 4, "Queues": [ "critical", "default" ] },
  "Dashboard": { "RequiredRole": "ops" },
  "Jobs": { "NightlyCleanupTask": { "Cron": "0 4 * * *", "TimeZone": "Europe/Brussels" } }
} }
```

## Code hatches

`AddCloudstrapHangfire(hangfire => …)` takes a `CloudstrapHangfireConfigurator`. Each hatch runs after the defaults
it adjusts:

| Member | Use |
|---|---|
| `RunServer` | `false` for a storage-only host. Code-level only: there is no configuration key. |
| `TaskAssemblies` | Assemblies to scan instead of the entry assembly. |
| `SqlServer` | Tune `SqlServerStorageOptions` (runs last over the recommended settings). |
| `Storage` | Bring your own storage; SQL Server and the `Storage:*` keys are then ignored and no connection string is required. |
| `Server` | Final say over `BackgroundJobServerOptions` (for example `ServerName`). |

## Schema preparation

With `Storage:PrepareSchema` unset, Hangfire installs and migrates its schema in `Development` only. Elsewhere the
schema must exist: deploy Hangfire's `Install.sql` (shipped in the `Hangfire.SqlServer` package) from your
infrastructure-as-code or migration pipeline. A processing host started against a missing schema fails at startup.

Azure SQL with a managed identity works through the connection string (`Authentication=Active Directory Default`);
the shipped `Microsoft.Data.SqlClient` 6.x carries that support in the core package.

The startup summary states the storage provider, the schema, the effective `PrepareSchema`, `RunServer`, the task
count and the dashboard path.

## Dashboard

`MapCloudstrapHangfireDashboard()` maps the dashboard at `Dashboard:Path` and serves it **only to authenticated
users**:

- With `Dashboard:AuthorizationPolicy` set, that policy is required.
- Otherwise an authenticated user is required, in `Dashboard:RequiredRole` when set. With no role, any authenticated
  caller is admitted, which is the posture for a proxy host calling with a token.
- A Cloudstrap filter inside the dashboard also requires an authenticated user, so a consumer convention such as
  `.AllowAnonymous()` never exposes it.
- The optional `configure` delegate over Hangfire's `DashboardOptions` adds filters and options; it cannot remove
  the authenticated-user floor. **Tighten, never loosen**: for an unauthenticated dashboard use Hangfire's own
  `MapHangfireDashboard`.
- Mapping throws when no authentication scheme is registered.

Call it from a Cloudstrap composite's `ConfigureEndpoints` hook (WebApi, MVC, Blazor Server), or on a
`WebApplication` after `UseAuthentication` and `UseAuthorization`. An anonymous request gets the scheme's
challenge: a cookie scheme redirects to login, a bearer scheme answers 401. A user outside the role gets 403.

**Serve the dashboard over HTTPS on hosts with hardened antiforgery.** Dashboard actions (trigger, delete,
requeue) are protected with the host's antiforgery tokens, and a secure-only antiforgery cookie is not issued over
plain HTTP.

Proxy path prefixes (`X-Forwarded-Prefix`) are not handled here. Serving the dashboard through a proxy host is the
job of `Cloudstrap.Hangfire.Proxy` (planned).

## Health check

`AddHealthChecks().AddCloudstrapHangfireHealthCheck()` is opt-in and additive on the stock builder:

- name `hangfire`, tag `ready`, failure status `Unhealthy`, all overridable;
- Healthy with `servers`, `recurring`, `enqueued` and `failed` counts as data when storage answers;
- on failure the result states only the exception type; the exception is logged at Warning. No connection string,
  server name or exception message reaches the probe output.

A storage-only host typically passes `failureStatus: HealthStatus.Degraded` so it stays ready while job storage is down.

## Telemetry

Each run is one span named `RecurringTask {jobId}` on the `ActivitySource` `Cloudstrap.Hangfire`
(`CloudstrapHangfireActivitySources.RecurringTask`), tagged `cloudstrap.hangfire.job_id`, with status Ok or Error.
The source is contributed to whatever tracer pipeline the host builds (Cloudstrap observability, your own, or
Aspire ServiceDefaults). The package registers no exporter and no provider; without a pipeline the source is inert.
A pipeline owner outside dependency injection adds the source by name.

## Logging

Per run: started, completed in N ms, failed after N ms (with the exception attached, not in the message), or
skipped because the previous run holds the lock. At startup: the posture line, one line per job, removals of
undeclared jobs, and warnings for overrides of undeclared ids. **Never logged**: task arguments or payloads,
connection strings, server or database names. Category: `Cloudstrap.Hangfire` (and the class names below it).

## Fire-and-forget and delayed jobs

Inject Hangfire's `IBackgroundJobClient`, which `AddCloudstrapHangfire` registers; there is no Cloudstrap facade.
The target method must be public on a type the **processing host** can resolve, because that is where the job runs.
A storage-only host can enqueue, as long as the processing host has the type.

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| `No registered recurring task with id 'X' was found on this host` | The job ran on a host that does not declare the task: usually a dashboard or API host with a server. Set `RunServer = false` there. |
| Recurring jobs disappear after a deployment | Another processing host with a different task set shares the schema and removed them. Give each independent processing host its own `Storage:SchemaName`. |
| Dashboard answers 401, or redirects to login | The request is not authenticated: map the dashboard after `UseAuthentication`/`UseAuthorization` and sign in. |
| Dashboard answers 403 | The user is not in `Dashboard:RequiredRole` or fails `Dashboard:AuthorizationPolicy`. |
| Dashboard actions fail with an antiforgery error | The host's antiforgery cookie is secure-only: serve the dashboard over HTTPS. |
| Startup fails naming a job id | A duplicate `JobId`, an unknown time zone or a rejected cron expression; the message names the job, the value and the key. |
| Startup fails with `Invalid object name '…'` | The schema does not exist: deploy `Install.sql`, or set `Storage:PrepareSchema` to `true` for a development database. |

## Aspire coexistence

Storage comes from a standard `ConnectionStrings:` entry, so an Aspire-provisioned SQL Server resource plugs in
without Cloudstrap-specific keys. The health check is additive on the stock builder, the span source is contributed
to the host's pipeline without a second exporter, and the package references no `Aspire.*` package.

## Migration notes (from the enterprise library this package was extracted from)

1. **Fail-fast now fails the host.** Scheduling runs in a hosted service's `StartAsync`; duplicate ids, bad cron,
   unknown time zones and unreachable storage abort startup instead of logging once and running unscheduled.
2. **The localhost-only dashboard path is gone.** Every mapping is authenticated; a host with no authentication
   scheme cannot map the dashboard at all.
3. **Two host-typed dashboard calls become one endpoint-routed mapping with a stock authorization policy**
   (`RequiredRole` empty = any authenticated principal); the host-type vocabulary is removed.
4. **Scheduling is no longer a separate `Use…` call**: it is a consequence of `RunServer = true`; a storage-only host
   structurally cannot schedule or reconcile; a processing host declaring zero tasks skips reconciliation.
5. **Schema preparation by environment name becomes an explicit `Storage:PrepareSchema`** with a `Development` default.
6. **The health check is `ready`-tagged by default** (previously untagged, so served by no probe).
7. **No job-client facade**: consumers inject `IBackgroundJobClient`.
8. **One `ExecuteAsync(CancellationToken)`** on the task interface; the token-less overload and the token-less
   persisted `RunAsync(string)` are not carried.
9. **Discovery defaults to the entry assembly and registers the task interface only.**
10. **`ServerName` and `QueuePollInterval` are no longer configuration keys**: they are reachable through the
    `Server` and `SqlServer` hatches.
11. **A second registration fails fast** instead of double-registering storage and servers.
12. **The persisted dispatcher identity changes** to `Cloudstrap.Hangfire.RecurringTaskRunner, Cloudstrap.Hangfire`.
    Recurring jobs persisted by the previous library are neither executed nor removed by Cloudstrap (their type
    does not load, so they are treated as another component's jobs). **Delete them from the dashboard once** after
    migrating.
13. **`Microsoft.Data.SqlClient` ships with the package** instead of being a consumer prerequisite.
