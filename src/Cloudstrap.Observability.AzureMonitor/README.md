# Cloudstrap.Observability.AzureMonitor

Application Insights export for the Cloudstrap observability pipeline. Flip one setting, add one chained
call, and traces, metrics and logs land in Application Insights — correlated by operation ID.

Part of the [Cloudstrap](https://github.com/geobarteam/Cloudstrap) suite. MIT licensed.

## Quick start

```csharp
builder.UseCloudstrapObservability()
       .AddAzureMonitor();
```

```json
{
  "Cloudstrap": {
    "Application": {
      "SystemName": "Contoso",
      "SubsystemName": "Orders",
      "SubsystemType": "Api"
    },
    "OpenTelemetry": {
      "Mode": "AzureMonitor"
    },
    "AzureMonitor": {
      "ConnectionString": "InstrumentationKey=...;IngestionEndpoint=https://...;LiveEndpoint=https://..."
    }
  }
}
```

**Leave the call in place permanently.** In any mode other than `AzureMonitor` it contributes no exporter and
changes no pipeline behavior, so the same binary moves between environments on configuration alone. It is
also idempotent — calling it twice registers one set of exporters. (A `configure` callback passed only to a
*second* call is discarded: the first call wins.)

## Settings — `Cloudstrap:AzureMonitor`

| Setting | Type | Default | Purpose |
|---------|------|---------|---------|
| `ConnectionString` | string | *(none)* | The Application Insights connection string. Falls back to the standard `APPLICATIONINSIGHTS_CONNECTION_STRING` variable. |
| `SamplingRatio` | float | *(none)* | Fraction of traces sampled, `0.0`–`1.0`. Mutually exclusive with `TracesPerSecond`. |
| `TracesPerSecond` | double | *(none)* | Rate-limited sampling. Mutually exclusive with `SamplingRatio`. |
| `UseDefaultAzureCredential` | bool | `false` | Authenticate ingestion with Entra ID instead of the connection string key. |

The section is bound and validated in **every** mode, not just `AzureMonitor` — a ratio outside `0.0`–`1.0`,
or `SamplingRatio` and `TracesPerSecond` set together, fails startup naming the offending keys rather than
surfacing the day you flip the mode. The connection-string requirement is the one rule scoped to
`AzureMonitor` mode.

## Sampling: the default is not 100%

**When you configure neither `SamplingRatio` nor `TracesPerSecond`, the exporter's own platform default
applies: rate-limited sampling at 5 traces per second.** This is Azure's current default, chosen for cost
protection, and Cloudstrap deliberately inherits it rather than overriding it.

Coming from `Otlp` or `Console` mode — which sample everything — this is the one behavior change that will
surprise you. To raise the volume:

- `"SamplingRatio": 1.0` — export every trace.
- `"SamplingRatio": 0.25` — export a quarter of them.
- `"TracesPerSecond": 20` — raise the rate limit instead.
- `"Cloudstrap:OpenTelemetry:AlwaysOnSampler": true` — record everything regardless of the settings above.
  Intended for development and diagnosis, not production volume.

Counts stay accurate either way: the Application Insights sampler stamps the rate it sampled at, and the
portal renormalizes from it.

## Connection string resolution

1. `Cloudstrap:AzureMonitor:ConnectionString`, when set to a non-blank value.
2. Otherwise the SDK resolves the standard `APPLICATIONINSIGHTS_CONNECTION_STRING` **environment variable**.
3. Otherwise host startup fails, naming both sources.

The fallback must be a real environment variable. Cloudstrap's validator also accepts that key from any
configuration source, but the SDK itself reads only the process environment — so supplying it through
`appsettings.json` alone would pass validation and then export nothing.

Sovereign and regional clouds need no extra setting: the connection string carries its own
`IngestionEndpoint`.

## Entra ID ingestion authentication

For workspaces with local (key) authentication disabled:

```json
{ "Cloudstrap": { "AzureMonitor": { "UseDefaultAzureCredential": true } } }
```

This attaches a `DefaultAzureCredential`, which behaves identically on Azure Web Apps, containers and
developer machines. One credential is shared by all three signals. To supply your own:

```csharp
.AddAzureMonitor(exporter => exporter.Credential = new ManagedIdentityCredential("<client-id>"));
```

A credential supplied in code always wins over the flag.

## The `configure` escape hatch

The callback runs after Cloudstrap's own configuration, so it always has the final say, and it reaches every
exporter setting Cloudstrap does not surface:

```csharp
.AddAzureMonitor(exporter =>
{
    exporter.DisableOfflineStorage = true;      // no telemetry spool on disk
    exporter.StorageDirectory = "/var/tmp/ai";  // or put the spool somewhere writable
    exporter.EnableStandardMetrics = false;     // opt out of pre-aggregated metrics
});
```

Standard (pre-aggregated) metrics are enabled by the exporter by default; Cloudstrap does not touch them.

## Blazor Server

`Cloudstrap:OpenTelemetry:EnableBlazorHubTracing` is honored in this mode too, but through export-time
suppression rather than sampling, because the exporter owns the sampler. Three things follow:

- Under a rate limit the hub spans still consume sampling budget before being scrubbed, so **prefer
  `SamplingRatio` over `TracesPerSecond` on Blazor Server**.
- Only the hub span itself is suppressed — work started inside a hub invocation is still exported, parented
  to the scrubbed span. In `Console` and `Otlp` mode the parent-based sampler drops those descendants too.
- Suppression is **owner-mode only**. In `Contribute` mode the host owns the pipeline: Cloudstrap adds no
  scrub, and this package's exporter replaces whatever sampler the host installed, so hub suppression is the
  host's to arrange.

## Aspire and other pipeline owners

Wire the Application Insights exporter **either** in Aspire's ServiceDefaults **or** through Cloudstrap —
never both, or every span is exported twice.

This package works in `Contribute` pipeline mode, which is the real pairing for an Aspire app: the host owns
the pipeline and Cloudstrap adds the Azure Monitor exporters and its enrichment to it.

## Live Metrics

Live Metrics is **not** available in this version. It requires an exporter entry point that takes ownership
of all three signals and overrides the sampler, which would break two contracts Cloudstrap consumers rely on:
the per-signal `EnableTracing`/`EnableMetrics`/`EnableLogs` flags, and the "consumer hooks run last" rule.

If you need it, either wire `UseAzureMonitorExporter` yourself in `Contribute` mode, or use the
`Azure.Monitor.OpenTelemetry.AspNetCore` distro instead of this package. This will be revisited if Azure
decouples Live Metrics from the distro entry point.

## Failure isolation

Export failures never crash the application — the exporter retries in the background and spools to disk. The
`OpenTelemetry-AzureMonitor-Exporter` EventSource is the diagnostic channel when telemetry does not arrive.

## Verifying against a real Application Insights resource

Automated tests never contact Azure. Verify a real backend once, manually:

1. Create an Application Insights resource in the Azure portal and copy its connection string.
2. Set `Cloudstrap:AzureMonitor:ConnectionString` to it and `Cloudstrap:OpenTelemetry:Mode` to
   `AzureMonitor`. Set `Cloudstrap:AzureMonitor:SamplingRatio` to `1.0` so nothing is sampled away while you
   are looking.
3. Run the application and issue one request that calls a downstream dependency and writes a log entry.
4. Wait two to three minutes for ingestion, then in the portal check:
   - **Transaction search** — the request appears, with `cloudstrap.system.name`, `cloudstrap.subsystem.name`
     and `cloudstrap.subsystem.type` among its custom dimensions.
   - **End-to-end transaction details** — the dependency call and the log entry hang off the same request,
     sharing one `operation_Id`.
   - **Metrics** — `performanceCounters` and the .NET runtime metrics are arriving.
5. Set the sampling back to production values.
