---
name: configure-hangfire
description: "Use when setting up, configuring, or extending Hangfire with Cloudstrap.Hangfire in a Cloudstrap project. Covers choosing the host topology (Worker / BFF / CFE / WFE proxy), registration (AddHangfireForCloudstrap), scheduling-vs-dashboard wiring, recurring tasks (IBackgroundRecurringTask), one-off & delayed jobs (ICloudstrapBackgroundJobScheduler), dashboard authorization (BFF trusted-subsystem / CFE role / WFE proxy), per-job config overrides, read-only dashboard, server/storage tuning, health checks, and the SQL-client/schema requirements. Use for: adding Hangfire to a new host, serving the dashboard behind a WFE⇒BFF proxy, adding a recurring job, enqueuing background work, fixing 'No registered recurring task' / disappearing jobs / 401 dashboard errors."
metadata:
  argument-hint: "Describe your scenario, e.g. 'add Hangfire to my worker', 'serve the dashboard on the BFF behind a WFE', 'add a daily recurring job', 'enqueue a one-off job', 'add a Hangfire health check'"
---

# Configure Hangfire — Setup, Scheduling, Dashboard & Jobs

Guide for wiring up `Cloudstrap.Hangfire` in a consuming Cloudstrap project. Covers the host
topology decision, DI registration, scheduling vs dashboard, recurring tasks, one-off/delayed jobs, dashboard
authorization (incl. the WFE⇒BFF proxy), per-job config overrides, tuning, and health checks.

> **Source of truth.** The package's own guide is the authoritative deep reference:
> [`src/Cloudstrap.Hangfire/README.md`](../../../src/Cloudstrap.Hangfire/README.md).
> For exact signatures/defaults, read the library code under `src/Cloudstrap.Hangfire/`
> (`IServiceCollectionExtension`, `WebApplicationExtensions`, `IHostExtensions`, `IBackgroundRecurringTask`,
> `ICloudstrapBackgroundJobScheduler`, `HangfireForCloudstrapOptions`, `HealthChecks/`, `Authorization/`) — not external
> Hangfire docs that may be outdated. Always read before answering questions about keys, defaults, or behavior.

> **Related skills:** dashboard token claims / trusted-subsystem propagation → **`configure-sts`**;
> the Hangfire SQL schema in a DACPAC → **`database-changes`**.

---

## The four golden rules (read first — violating them causes the two classic failures)

1. **Exactly one host schedules** — the **processing** host. A dashboard-only host must **not** call any
   scheduling method, or its orphan-reconciliation deletes the processing host's jobs from shared storage.
2. **Dashboard-only hosts set `runServer: false`** — so they never dequeue a job whose implementation they
   don't reference (which would throw `No registered recurring task named 'X'` on every tick).
3. **All participating hosts share the same storage** — the same connection string / database.
4. **The `IBackgroundRecurringTask` implementations live with the processing host** (Worker, BFF-no-worker, or
   CFE-monolith). The dashboard renders jobs via the shared `RecurringTaskRunner` dispatcher and needs no
   reference to the implementation assembly.

---

## First action — contextual discovery

Before generating anything, explore the developer's project and infer as much as possible:

1. **Existing Hangfire wiring** — search for `AddHangfireForCloudstrap`, `UseHangfireForCloudstrap`,
   `UseHangfireDashboardForCloudstrap`, `UseHangfireForCloudstrapWithoutDashboard`.
2. **Host type** — is this a Worker (`Host.CreateApplicationBuilder`), a BFF/WebApi/Blazor-Server
   (`WebApplication`), a CFE (Blazor WASM host), or a WFE (Blazor Server front-end that proxies to a BFF)?
3. **Existing tasks** — search for `IBackgroundRecurringTask` implementations.
4. **Package references** — is `Cloudstrap.Hangfire` referenced? Is a SQL client present
   (`Microsoft.Data.SqlClient`, or transitively via `Microsoft.EntityFrameworkCore.SqlServer`)?
5. **Proxy** — for a WFE, is `Cloudstrap.Hangfire.Proxy` or `Cloudstrap.Proxy`
   referenced, and is there a BFF entry in `Cloudstrap:HttpClientServiceRegistry`?

Use this to skip questions you can already answer.

---

## Step 1 — Decide the topology (the single most important decision)

Every host opts into a subset of four concerns: **storage**, **processing server**, **scheduling**,
**dashboard**. Pick the row that matches the host being configured:

| Host | `AddHangfireForCloudstrap` | `runServer` | Schedules? | Holds task impls? | Dashboard | Dashboard auth |
|------|----------------------|-------------|-----------|-------------------|-----------|----------------|
| **Worker** (processing) | ✅ | `true` | ✅ `UseHangfireForCloudstrapWithoutDashboard` | ✅ | — | — |
| **BFF with worker** (dashboard-only) | ✅ | **`false`** | **no** | no | ✅ `UseHangfireDashboardForCloudstrapBff` | trusted-subsystem (audience) |
| **BFF no worker** (does everything) | ✅ | `true` | ✅ | ✅ | ✅ `UseHangfireDashboardForCloudstrapBff` | trusted-subsystem (audience) |
| **CFE monolith** | ✅ | `true` | ✅ | ✅ | ✅ `UseHangfireDashboardForCloudstrapCfe` | end-user role |
| **CFE with worker** (dashboard-only) | ✅ | **`false`** | **no** | no | ✅ `UseHangfireDashboardForCloudstrapCfe` | end-user role |
| **WFE** (front-end proxy) | — | — | — | — | forwards `/hangfire` | end-user role (front door) |
| **Self-hosted / dev (legacy)** | ✅ | `true` | ✅ `UseHangfireForCloudstrap` (combined) | ✅ | ✅ (same call) | localhost-only |

**Maps to the four canonical scenarios** (full walkthroughs in the package README §5–§8):
A = WFE⇒BFF⇒Worker · B = WFE⇒BFF (no worker) · C = CFE monolith · D = CFE⇒Worker.

---

## Step 2 — Register Hangfire (`AddHangfireForCloudstrap`)

Every participating host calls this in DI. Only `runServer` and `backgroundRecurringTaskAssemblies` change per
host; everything else is defaulted.

```csharp
using Cloudstrap.Hangfire;

builder.Services.AddHangfireForCloudstrap(
    cloudstrapConfiguration,
    loggerFactory,
    connectionString: null,                    // null => cloudstrapConfiguration.GetDefaultConnectionString()
    backgroundRecurringTaskAssemblies: null,   // null => the CALLING assembly is scanned for IBackgroundRecurringTask
    runServer: true,                           // false for a DASHBOARD-ONLY host (see golden rule #2)
    configure: null);                          // optional Action<HangfireForCloudstrapOptions> (see Step 6)
```

- On the **processing host**, set `backgroundRecurringTaskAssemblies` to the assembly that holds the task
  implementations when they don't live in the calling assembly, e.g. `[typeof(ReminderSender).Assembly]`.
- On a **dashboard-only host**, set `runServer: false` and leave the assemblies null — it scans nothing
  meaningful and renders jobs via the shared dispatcher.
- This call also registers `ICloudstrapBackgroundJobScheduler` (Step 5), the `RecurringTaskRunner` dispatcher, and
  the per-job config overrides — no extra wiring needed.

`cloudstrapConfiguration` / `loggerFactory` come from the standard bootstrap:

```csharp
IConfiguration configuration = BootstrapConfiguration.ReadAppSettings();
CloudstrapConfiguration cloudstrapConfiguration = configuration.GetCloudstrapConfiguration();
ILoggerFactory loggerFactory = BootstrapLoggerFactory.Create(cloudstrapConfiguration);
```

---

## Step 3 — Wire scheduling and/or the dashboard (middleware)

> **Order:** dashboard / proxy methods read `HttpContext.User`, so call them **after** `UseAuthentication()`
> and `UseAuthorization()`. Before them, every request is unauthenticated ⇒ 401/403.

### Processing host — schedule (no dashboard)

```csharp
host.UseHangfireForCloudstrapWithoutDashboard(loggerFactory);   // schedules + reconciles orphans. Processing host ONLY.
```

### Dashboard-only BFF (behind a WFE) — trusted-subsystem auth, never schedules

```csharp
app.UseAuthentication();   // validates the JWT bearer audience == this BFF's workload id
app.UseAuthorization();
app.UseHangfireDashboardForCloudstrapBff(loggerFactory);        // trusts any token the audience validates. No role, no scheduling.
```

### CFE — end-user role auth

```csharp
app.UseAuthentication();
app.UseAuthorization();
app.UseHangfireDashboardForCloudstrapCfe(loggerFactory);        // role from Cloudstrap:Hangfire:Dashboard:AccessRole
```

### BFF-no-worker (Scenario B) — schedule AND serve the dashboard (it's the processing host too)

```csharp
app.UseAuthentication();
app.UseAuthorization();
app.UseHangfireForCloudstrapWithoutDashboard(loggerFactory);    // WebApplication is an IHost
app.UseHangfireDashboardForCloudstrapBff(loggerFactory);
```

### WFE — forward `/hangfire` to the BFF (no storage/server on the WFE)

Recommended (purpose-built, bundles proxy + a role-gated front-door policy):

```csharp
using Cloudstrap.Hangfire.Proxy;

builder.Services.AddHangfireDashboardProxyForCloudstrapWfe(cloudstrapConfiguration, HttpClientServicesNames.MyBff);
...
app.UseAuthentication();
app.UseAuthorization();
app.UseHangfireDashboardProxyForCloudstrapWfe();                // maps /hangfire → BFF, RequireAuthorization(role policy)
```

Lower-level building blocks (custom path or policy):

```csharp
using Cloudstrap.Proxy;

builder.Services.AddCloudstrapTrustedSubsystemProxy(cloudstrapConfiguration, HttpClientServicesNames.MyBff);
...
app.MapCloudstrapTrustedSubsystemForwarder("/hangfire", HttpClientServicesNames.MyBff)
   .RequireAuthorization(/* your end-user role policy */);
```

### Legacy self-hosted / dev — combined schedule + dashboard (localhost-only auth)

```csharp
app.UseHangfireForCloudstrap(loggerFactory);                    // backward-compatible; NOT for use behind a proxy
```

---

## Step 4 — Add a recurring task (`IBackgroundRecurringTask`)

Implement the interface in the **processing host** (or a library it references). Assembly scanning registers
it automatically — no manual DI. Only `CronExpression` and `ExecuteAsync()` are required; the rest are
default interface members you override per task.

```csharp
using Cloudstrap.Hangfire;

public sealed class ReminderSender : IBackgroundRecurringTask
{
    public string CronExpression => "0 7 * * *";            // required (Hangfire/NCrontab syntax)

    // Optional overrides (defaults shown):
    // public string JobId               => GetType().Name;  // stable dashboard id; override to survive a rename
    // public TimeZoneInfo TimeZone      => TimeZoneInfo.Utc; // or override per-job in config (Step 6)
    // public string Queue               => "default";
    // public bool IsEnabled             => true;            // false => the recurring job is removed, not scheduled
    // public bool PreventOverlappingRuns => true;           // skip a trigger if the previous run is still going

    public Task ExecuteAsync() => DoWorkAsync();

    // Override to honour shutdown/timeout cancellation (defaults to the no-token ExecuteAsync()):
    // public Task ExecuteAsync(CancellationToken ct) => DoWorkAsync(ct);
}
```

- The task is resolved per trigger from DI (transient) by **`JobId`** — keep the id stable.
- Cancellation: override `ExecuteAsync(CancellationToken)` and propagate the token; Hangfire injects the real
  job-cancellation token at execution time.

---

## Step 5 — Enqueue one-off / delayed jobs (`ICloudstrapBackgroundJobScheduler`)

Inject the interface anywhere (no direct Hangfire dependency). Registered by `AddHangfireForCloudstrap`. The work
runs on a **processing host** (`runServer: true`) that can resolve the target type.

```csharp
using Cloudstrap.Hangfire;

public sealed class OrderService(ICloudstrapBackgroundJobScheduler scheduler)
{
    public void Reprocess(int orderId)
    {
        scheduler.Enqueue(() => ReprocessAsync(orderId));                          // ASAP
        scheduler.Schedule(() => ReprocessAsync(orderId), TimeSpan.FromMinutes(30)); // after a delay
    }

    public static Task ReprocessAsync(int orderId) => /* ... */;   // must be a PUBLIC method for Hangfire
}
```

---

## Step 6 — Optional: per-job overrides, tuning, read-only dashboard

### Config-driven schedule/enable/timezone (no redeploy) — `Cloudstrap:Hangfire:Jobs:<JobId>`

```jsonc
"Cloudstrap": { "Hangfire": { "Jobs": {
  "ReminderSender": {                 // keyed by the task's JobId (case-insensitive)
    "Cron": "0 8 * * *",              // overrides the task's CronExpression (empty => task's own)
    "Enabled": true,                  // false => the recurring job is removed
    "TimeZone": "Europe/Brussels"     // IANA id; empty => the task's TimeZone (UTC by default). Bad id fails fast.
  }
} } }
```

### Server/storage tuning + read-only dashboard — the `configure` callback

```csharp
builder.Services.AddHangfireForCloudstrap(cloudstrapConfiguration, loggerFactory, configure: o =>
{
    o.WorkerCount = 4;                       // null => Hangfire default
    o.Queues = ["critical", "default"];      // null => default
    o.ServerName = "reminder-worker";        // null => machine-derived
    o.QueuePollInterval = TimeSpan.FromSeconds(5); // null/Zero => existing default
    o.DashboardReadOnly = true;              // disables trigger/delete/requeue in the UI (NOT authorization)
});
```

Every `HangfireForCloudstrapOptions` member defaults to "keep existing behavior", so omitting `configure`
reproduces today's setup exactly.

---

## Step 7 — Optional: health check

Opt-in; reports whether the Hangfire storage is reachable (via the monitoring API). Requires
`AddHangfireForCloudstrap` to have registered a `JobStorage`.

```csharp
using Cloudstrap.Hangfire.HealthChecks;

builder.Services.AddHealthChecks()
    .AddHangfireHealthCheckForCloudstrap(name: "hangfire", tags: ["ready"]);
```

---

## appsettings — what each host needs

The connection string is **root-level** `ConnectionStrings:DefaultConnection` (NOT under `Cloudstrap`); it's used
when `AddHangfireForCloudstrap(..., connectionString: null)`. The security keys are detailed in the package README
§9.6 (WFE registry entry, BFF JWT audience, dashboard access role). Minimal per host:

```jsonc
// Processing host (Worker / BFF-no-worker / CFE-monolith)
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=tcp:my-sql.database.windows.net;Database=Hangfire;Authentication=Active Directory Default;Encrypt=True"
  },
  "Cloudstrap": { "Hangfire": {
    "Dashboard": { "AccessRole": "HangfireOps" },   // CFE/WFE end-user role; empty => any authenticated user
    "Jobs": { "ReminderSender": { "Cron": "0 8 * * *", "Enabled": true } }
  } }
}
```

```jsonc
// Dashboard-only BFF (trusted-subsystem) — Dashboard:AccessRole is IGNORED by the BFF filter
{
  "ConnectionStrings": { "DefaultConnection": "...same database as the worker..." },
  "Cloudstrap": { "Security": { "Authentication": { "JwtBearer": { "Audience": "my-bff" } } } }
}
```

```jsonc
// WFE proxy — key must match the bffHttpClientKey passed in code
{
  "Cloudstrap": {
    "Hangfire": { "Dashboard": { "AccessRole": "HangfireOps" } },
    "HttpClientServiceRegistry": {
      "MyBff": {
        "BaseAddress": "https://my-bff.example.com",  // BFF cert SAN must cover this host
        "AddClientAccessToken": true,                          // REQUIRED: attaches the client-credentials token
        "TokenRequestParameters": { "Scope": "my-bff", "Resource": "my-bff" }  // token aud must == BFF Audience
      }
    }
  }
}
```

> **Two values must line up:** the WFE token audience (from `Scope`/`Resource`) == the BFF's
> `Cloudstrap:Security:Authentication:JwtBearer:Audience`, and the registry `BaseAddress` host == the BFF
> certificate SAN (TLS is pinned to that host).

---

## Critical: SQL client & schema (easy to miss)

- **Every host that calls `AddHangfireForCloudstrap` must reference a SQL client.** `Hangfire.SqlServer` does not
  carry one — add **`Microsoft.Data.SqlClient`** (recommended). Hosts that already reference
  `Microsoft.EntityFrameworkCore.SqlServer` get it transitively and need add nothing.
- **Schema provisioning:** in dev environments the schema auto-prepares; in **TST/VAL/PRD**
  (`PrepareSchemaIfNecessary` is disabled) the Hangfire SQL schema must be deployed up front (DACPAC /
  Schema Compare — see the `database-changes` skill).
- **Do not** add `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.SqlServer`, or
  `Newtonsoft.Json` to the Hangfire package itself — they were removed as unused.

---

## Validation rules — actively flag these

| Misconfiguration | Warning |
|------------------|---------|
| Dashboard-only host runs `runServer: true` | It will dequeue jobs it can't resolve → `No registered recurring task named 'X'` on every tick. Set `runServer: false`. |
| Dashboard-only host calls a scheduling method | Its reconciliation deletes the worker's jobs from shared storage → jobs vanish on restart. Remove the scheduling call. |
| More than one host schedules | Same as above — only the processing host schedules. |
| Hosts use different databases/connection strings | They won't see each other's jobs. All share one storage. |
| Dashboard method called before `UseAuthentication`/`UseAuthorization` | `HttpContext.User` is empty → 401/403. Move it after. |
| WFE forwards but `AddClientAccessToken` is false / missing token params | The BFF rejects the unauthenticated forward. |
| WFE token audience ≠ BFF `JwtBearer:Audience` | The BFF's bearer auth rejects the token → 401. |
| Registry `BaseAddress` host not covered by the BFF cert SAN | TLS validation fails (`RemoteCertificateNameMismatch`). |
| Host calls `AddHangfireForCloudstrap` with no SQL client referenced | Runtime failure resolving an ADO.NET provider. Add `Microsoft.Data.SqlClient`. |
| Relying on auto schema in TST/VAL/PRD | Schema isn't auto-prepared there; deploy it via DACPAC. |
| Per-job `Cron`/`TimeZone` set under the wrong key | Must be `Cloudstrap:Hangfire:Jobs:<JobId>` (matches the task's `JobId`). |

---

## Troubleshooting (symptom → cause → fix)

- **`No registered recurring task named 'X'` (at `RecurringTaskRunner.RunAsync`)** → a dashboard host is
  running a processing server for a job it can't resolve → set that host `runServer: false` and stop it
  scheduling.
- **Recurring jobs disappear after deploy/restart** → more than one host schedules → only the processing host
  schedules; dashboard-only hosts call a `UseHangfireDashboardForCloudstrap*` method and nothing else.
- **Dashboard 401/403 behind the WFE** → middleware order, mismatched `/hangfire` prefix, or the WFE's
  client-credentials token not targeting the BFF audience.

(Full troubleshooting in the package README §12.)

---

## Checklist

- [ ] Topology chosen (Step 1) — exactly one processing host; dashboard-only hosts are `runServer: false`.
- [ ] `AddHangfireForCloudstrap` called on every participating host; `backgroundRecurringTaskAssemblies` set on the processing host if impls aren't in the calling assembly.
- [ ] Only the processing host calls `UseHangfireForCloudstrapWithoutDashboard` (or legacy `UseHangfireForCloudstrap`).
- [ ] Dashboard method matches the host (`...Bff` / `...Cfe`) and runs after `UseAuthentication`/`UseAuthorization`.
- [ ] WFE (if any): proxy registered, `/hangfire` forwarded with `RequireAuthorization`, registry entry present with `AddClientAccessToken: true`.
- [ ] All hosts share one storage (`ConnectionStrings:DefaultConnection`).
- [ ] `IBackgroundRecurringTask` impls live with the processing host; `JobId` stable.
- [ ] A SQL client (`Microsoft.Data.SqlClient`) is referenced; TST/VAL/PRD schema deployed via DACPAC.
- [ ] BFF `JwtBearer:Audience` == WFE token audience; `BaseAddress` host == BFF cert SAN.
- [ ] Optional: per-job overrides, `HangfireForCloudstrapOptions` tuning, read-only dashboard, health check.
