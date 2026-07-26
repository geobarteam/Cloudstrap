# Cloudstrap.Core

The typed, fail-fast-validated settings model behind the Cloudstrap library suite.

Describe your application once — identity, logging, telemetry, correlation, health probes and outbound
HTTP clients — in a `Cloudstrap:` configuration section. Every Cloudstrap package reads that same model
instead of inventing its own keys, and a typo in `appsettings.json` stops the app at startup with a
message naming the offending setting.

`Cloudstrap.Core` is a leaf package: settings, validation, and two entry points. It registers no
middleware, no exporters, no health checks and no HTTP handlers, and it takes no dependency on
ASP.NET Core — so it loads from web apps, workers, console apps and Blazor WebAssembly alike.

## Install

```shell
dotnet add package Cloudstrap.Core
```

## Use it

### From a host — `AddCloudstrapCore()`

```csharp
using Cloudstrap.Core;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddCloudstrapCore();
```

This binds the `Cloudstrap` section and every subsection, and validates all of them at startup. After the
call, `IOptions<T>` resolves for `CloudstrapOptions` and for each section type — `ApplicationOptions`,
`LoggingOptions`, `OpenTelemetryOptions`, `CorrelationOptions`, `HealthChecksOptions`:

```csharp
public sealed class OrderService(IOptions<ApplicationOptions> application)
{
    private readonly ApplicationOptions _application = application.Value;
}
```

Invalid configuration fails host startup with the framework's `OptionsValidationException`, naming the
offending settings. Binding reads the `IConfiguration` in the container, which the standard host builders
register for you.

### Before the host exists — `GetCloudstrapOptions()`

Bootstrap logging and key-vault configuration run before dependency injection. For those, bind eagerly:

```csharp
using Cloudstrap.Core;

CloudstrapOptions options = builder.Configuration.GetCloudstrapOptions();
```

It validates before returning, and throws `ConfigurationValidationException` when the `Cloudstrap` section
is missing or any rule fails. The message lists every failure, and `Failures` exposes them individually:

```
The 'Cloudstrap' configuration section is invalid.
Application:SystemName: The ApplicationOptions.SystemName field is required.
Logging:File:Path is required when File:Enabled is true.
OpenTelemetry:Endpoint is required when Mode is Otlp.
HttpClients:CatalogApi:BaseAddress must be an absolute URI.
```

Both entry points apply exactly the same rules — one validator implementation, two doors.

Want a tolerant read instead? Bind without validating:

```csharp
CloudstrapOptions? options = configuration
    .GetSection(CloudstrapOptions.SectionName)
    .Get<CloudstrapOptions>();
```

### Overriding values in code

There is deliberately no configure callback on `AddCloudstrapCore()`. Layer overrides with the standard
options idiom, after the call:

```csharp
builder.Services.AddCloudstrapCore();
builder.Services.PostConfigure<ApplicationOptions>(options => options.PathBase = "/orders");
```

## Configuration

```jsonc
{
  "Cloudstrap": {
    "Application": {
      "SystemName": "Contoso",          // required
      "SubsystemName": "Orders",        // required
      "SubsystemType": "Api",           // required
      "PathBase": "/orders",
      "EnvironmentTier": "acceptance"
    },
    "Logging": {
      "Level": "Information",
      "LevelOverrides": { "Microsoft.AspNetCore": "Warning" },
      "EnrichProperties": { "Team": "Fulfilment" },
      "Console": { "Enabled": true },
      "File": { "Enabled": false }
    },
    "OpenTelemetry": {
      "Mode": "Otlp",                   // Disabled | Console | Otlp | AzureMonitor
      "Endpoint": "https://collector.example.com/",
      "Headers": { "X-Api-Key": "..." }
    },
    "Correlation": {
      "HeaderName": "X-Correlation-ID"
    },
    "HealthChecks": {
      "LivenessPath": "/healthz",
      "ReadinessPath": "/ready"
    },
    "HttpClients": {
      "CatalogApi": {
        "BaseAddress": "https://catalog.example.com/",
        "Timeout": "00:00:10",
        "AddClientAccessToken": true,
        "TokenRequestParameters": { "Scope": "catalog.read" }
      }
    }
  }
}
```

### Defaults

| Setting | Type | Default |
|---|---|---|
| `Application:SystemName` | `string` | — *(required)* |
| `Application:SubsystemName` | `string` | — *(required)* |
| `Application:SubsystemType` | `string` | — *(required)* |
| `Application:WorkloadName` | `string` | computed — see *Conventions* |
| `Application:EnvironmentTier` | `string?` | `null` |
| `Application:PathBase` | `string` | `""` |
| `Application:ExceptionHandlerPath` | `string` | `/error` |
| `Logging:Level` | `LogLevel` | `Information` |
| `Logging:LevelOverrides` | `Dictionary<string, LogLevel>` | empty |
| `Logging:EnrichProperties` | `Dictionary<string, string>` | empty |
| `Logging:Console:Enabled` | `bool` | `true` |
| `Logging:File:Enabled` | `bool` | `false` |
| `Logging:File:Path` | `string?` | `null` — *required when `File:Enabled` is `true`* |
| `OpenTelemetry:Mode` | `OpenTelemetryMode` | `Disabled` |
| `OpenTelemetry:Endpoint` | `Uri?` | `null` — *required, absolute http/https, when `Mode` is `Otlp`* |
| `OpenTelemetry:Headers` | `Dictionary<string, string>` | empty |
| `OpenTelemetry:EnableTracing` / `EnableMetrics` / `EnableLogs` | `bool` | `true` |
| `OpenTelemetry:EnableConsole` | `bool` | `true` |
| `OpenTelemetry:EnableRuntimeMetrics` / `EnableHttpClientMetrics` / `EnableAspNetCoreMetrics` | `bool` | `true` |
| `OpenTelemetry:EnableMessagingMetrics` | `bool` | `true` |
| `OpenTelemetry:EnableSqlClientInstrumentation` | `bool` | `false` |
| `OpenTelemetry:EnableBlazorHubTracing` | `bool` | `false` |
| `OpenTelemetry:AlwaysOnSampler` | `bool` | `false` |
| `Correlation:HeaderName` | `string` | `X-Correlation-ID` |
| `Correlation:Request:RequireForAllEndpoints` | `bool` | `false` |
| `Correlation:Request:HealthEndpoints` | `List<string>` | `["/healthz", "/ready"]` |
| `Correlation:Request:ExcludeEndpoints` | `List<string>` | empty |
| `Correlation:Message:RequireForAllMessageHandlers` | `bool` | `false` |
| `Correlation:Message:ExcludeMessageHandlers` | `List<string>` | empty |
| `HealthChecks:Enabled` | `bool` | `true` |
| `HealthChecks:LivenessPath` | `string` | `/healthz` |
| `HealthChecks:ReadinessPath` | `string` | `/ready` |
| `HttpClients:{name}:BaseAddress` | `Uri?` | — *(required, must be absolute)* |
| `HttpClients:{name}:Timeout` | `TimeSpan` | `00:00:30` |
| `HttpClients:{name}:AddUserAccessToken` / `AddClientAccessToken` | `bool` | `false` |
| `HttpClients:{name}:EnableHealthCheck` | `bool` | `false` |
| `HttpClients:{name}:HealthCheckPrefix` | `string?` | `null` |
| `HttpClients:{name}:TokenRequestParameters` | `TokenRequestOptions?` | `null` |

> ⚠️ **Collections append, they do not replace.** The configuration binder populates pre-initialized
> collections in place, so values you configure for `Correlation:Request:HealthEndpoints` are *added to*
> the `["/healthz", "/ready"]` defaults rather than replacing them. The same holds for every other
> collection and dictionary in this model — though only `HealthEndpoints` ships with non-empty defaults.

### Validation rules

| Rule | Fails when |
|---|---|
| `Application:SystemName`, `SubsystemName`, `SubsystemType` | any is missing or empty |
| `Logging:File:Path` | `Logging:File:Enabled` is `true` and the path is missing or blank |
| `OpenTelemetry:Endpoint` | `Mode` is `Otlp` and the endpoint is missing, relative, or not `http`/`https` |
| `HttpClients:{name}:BaseAddress` | missing or not an absolute URI |

`Mode = AzureMonitor` requires nothing extra here; exporter settings belong to the Azure Monitor package.

## Conventions

Every convention has an override.

| Convention | Default | Override |
|---|---|---|
| **Workload name** | lowercase `{SystemName}-{SubsystemName}-{SubsystemType}` — `Contoso`/`Orders`/`Api` yields `contoso-orders-api`. Consuming packages use it for queue names, resource prefixes and telemetry service names. | Set `Cloudstrap:Application:WorkloadName`; the explicit value wins verbatim. |
| **Path base** | `""` (hosted at the root). Configured values are normalized to a single leading slash with no trailing slash: `myapp` and `/myapp/` both become `/myapp`. | `Cloudstrap:Application:PathBase` |
| **Probe paths** | `/healthz` (liveness), `/ready` (readiness) | `Cloudstrap:HealthChecks:LivenessPath` / `ReadinessPath` |
| **Correlation header** | `X-Correlation-ID`. W3C `traceparent` remains the tracing backbone regardless. | `Cloudstrap:Correlation:HeaderName` |
| **Environments** | Standard ASP.NET Core environments (`IHostEnvironment`) drive behavior; Core stores no environment name and performs no host sniffing. | `Cloudstrap:Application:EnvironmentTier` — an optional free-form label for organizations with more tiers than Development/Staging/Production. Core attaches no behavior to it. |
| **Connection strings** | The standard `ConnectionStrings:` section, read with `IConfiguration.GetConnectionString(name)`. Core does not mirror them into the model. | n/a |

## License

MIT
