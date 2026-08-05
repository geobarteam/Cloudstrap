# Cloudstrap.Extensions

KeyVault-backed configuration, Azure Blob data protection, a conventional blob container client,
config-driven typed `HttpClient`s with correlation and access-token seams, and the standard health probe
endpoints — one call and one `Cloudstrap:` subsection each.

> **Runtime requirement**: this package carries a `Microsoft.AspNetCore.App` framework reference. Every
> consumer requires the ASP.NET Core shared framework at run time — `mcr.microsoft.com/dotnet/aspnet` base
> images work; `mcr.microsoft.com/dotnet/runtime`-only base images are **not** supported. Blazor WebAssembly
> clients are served by `Cloudstrap.BlazorWasm`, never by this package.

## Quick start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddCloudstrapKeyVault();          // first: secrets take part in everything bound below
builder.UseCloudstrapObservability();
builder.AddCloudstrapBlobStorage();
builder.AddCloudstrapDataProtection();
builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog");

var app = builder.Build();
app.MapCloudstrapHealthChecks();          // /healthz and /ready
app.MapControllers();
app.Run();
```

```json
{
  "Cloudstrap": {
    "Application": { "SystemName": "Contoso", "SubsystemName": "Orders", "SubsystemType": "Api" },
    "KeyVault": { "Enabled": true, "VaultUri": "https://contoso-vault.vault.azure.net/" },
    "Storage": { "BlobServiceUri": "https://contosostore.blob.core.windows.net/" },
    "DataProtection": {
      "Enabled": true,
      "KeysBlobUri": "https://contosostore.blob.core.windows.net/keys/keys.xml",
      "KeyVaultKeyId": "https://contoso-vault.vault.azure.net/keys/dataprotection"
    },
    "HttpClients": {
      "Catalog": { "BaseAddress": "https://catalog.contoso.example/", "EnableHealthCheck": true }
    }
  }
}
```

Every `AddCloudstrap…` call above is safe to leave in unconditionally. Configuration decides where each one
does something, so the same `Program.cs` runs on a laptop and in production.

## KeyVault-backed configuration

`AddCloudstrapKeyVault()` adds the vault as a configuration source when `Cloudstrap:KeyVault:Enabled` is
set. Secrets are filtered by prefix and their flat names are mapped onto nested configuration keys:

| Secret in the vault | Configuration key |
|---|---|
| `contoso-orders-api-ConnectionStrings--Orders` | `ConnectionStrings:Orders` |
| `contoso-orders-api-Catalog--ApiKey` | `Catalog:ApiKey` |
| `other-workload-Secret` | *(not loaded)* |

**The prefix filter is the reason this exists.** Several workloads can share one vault without reading each
other's secrets. The prefix defaults to `Cloudstrap:Application:WorkloadName` — the lowercase
`{SystemName}-{SubsystemName}-{SubsystemType}` — and is overridable:

| Setting | Default | Notes |
|---|---|---|
| `Cloudstrap:KeyVault:Enabled` | `false` | Nothing Azure-related is even constructed when this is off. |
| `Cloudstrap:KeyVault:VaultUri` | — | Required, and absolute, when enabled. |
| `Cloudstrap:KeyVault:SecretPrefix` | the workload name | An explicit prefix wins; `""` loads every secret unfiltered. |

**Call it first**, before `GetCloudstrapOptions()` and the other registrations, so secrets participate in
options binding. The source is added last, so vault values win over `appsettings.json` — standard
configuration layering.

Failures are deliberately loud: enabling the section without a `VaultUri` throws
`ConfigurationValidationException` before the host exists, and an unreachable vault fails the configuration
build. An application must never start believing its secrets are simply absent.

The credential and reload interval are code-level, not configuration:

```csharp
builder.AddCloudstrapKeyVault(settings =>
{
    settings.Credential = new ManagedIdentityCredential("<client-id>");
    settings.ReloadInterval = TimeSpan.FromHours(1);      // default: read once at startup
});
```

### Use Cloudstrap's KeyVault configuration **or** Aspire's — never both

Both add a KeyVault configuration source. Two sources over one vault means two sets of providers, doubled
startup calls, and last-one-wins precedence that depends on registration order. Pick one owner:

- **Cloudstrap's**, when you want the secret-prefix filter — one vault serving several workloads. This is
  the capability Aspire's integration does not provide.
- **Aspire's**, when the vault serves exactly one application and you want it wired through the AppHost.
  Then leave `Cloudstrap:KeyVault:Enabled` at `false`; everything else in this package works unchanged.

Cloudstrap references no `Aspire.*` package and never will.

### Required permissions

| Feature | Role on the resource |
|---|---|
| KeyVault configuration | `Key Vault Secrets User` on the vault |
| Blob storage | `Storage Blob Data Contributor` on the account or container |
| Data protection | `Storage Blob Data Contributor` on the key container, plus a KeyVault key policy granting **wrap** and **unwrap** |

Credentials default to `DefaultAzureCredential` everywhere — the same code path resolves a managed identity
in Azure and your own sign-in locally. There is no environment sniffing and no credential-type exclusion
list; supply a `TokenCredential` through the `configure` hook when you need something specific.

## Typed HTTP clients

```csharp
builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog");
```

The client name defaults to the interface name without its leading `I` (`ICatalogClient` binds
`Cloudstrap:HttpClients:CatalogClient`); pass `name` to override it.

| Setting | Default | Notes |
|---|---|---|
| `BaseAddress` | — | Required, absolute. |
| `Timeout` | `00:00:30` | |
| `AddUserAccessToken` / `AddClientAccessToken` | `false` | See the token seam below. |
| `EnableHealthCheck` | `false` | Registers a readiness check probing the peer. |
| `HealthCheckPrefix` | the client name | Names the check `{prefix}-liveness`. |
| `HealthCheckPath` | `/healthz` | Probed on the peer, relative to `BaseAddress`. |

A missing section, or a `BaseAddress` that is absent or relative, fails at host startup — or at first
resolution of the client, whichever comes first — with a message naming the offending key. A misconfigured
client never reaches the network.

The correlation handler is attached exactly once, even when you already registered it through
`ConfigureHttpClientDefaults`. Your `configureClient` and `configureBuilder` hooks run after Cloudstrap's own
wiring, so they always have the final say.

### Resilience is yours

**Cloudstrap never adds a resilience handler** and never replaces the primary handler. Retries, circuit
breakers and hedging stay your choice, applied however you prefer:

```csharp
builder.Services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());
```

Cloudstrap-registered clients pick that up like any other client, with exactly one resilience layer — the one
you asked for. This is also what makes the package composable with an Aspire `ServiceDefaults` project that
applies resilience at the defaults level.

### Access tokens: the seam

Setting `AddUserAccessToken` or `AddClientAccessToken` activates the corresponding token handler seam. This
package declares the two interfaces and never implements them, so nothing here depends on an authentication
stack. The implementations ship separately:

| Flag | Seam interface | Implementing package |
|---|---|---|
| `AddUserAccessToken` | `IUserAccessTokenHandlerProvider` | `Cloudstrap.Authentication.OpenIdConnect` |
| `AddClientAccessToken` | `IClientAccessTokenHandlerProvider` | `Cloudstrap.Authentication.ClientCredentials` |

Each provider is resolved from the container when the client's pipeline is first built, never at registration
time, so the order of your `AddCloudstrap…` calls does not matter. A client may set both flags: both handlers
are then added, user first. Until an implementation is registered, setting a flag fails client creation with
a message naming exactly the missing flag(s) and package(s) — Cloudstrap will not quietly send an
unauthenticated, or partially authenticated, request in its place.

## Health probes

`app.MapCloudstrapHealthChecks()` serves both standard probes, filtered by the shared tag vocabulary from
`Cloudstrap.Observability`:

| Path (configurable) | Tag | Meaning |
|---|---|---|
| `/healthz` — `Cloudstrap:HealthChecks:LivenessPath` | `live` | The process is functioning. A failure here means restart me. |
| `/ready` — `Cloudstrap:HealthChecks:ReadinessPath` | `ready` | The instance can serve traffic. A failure here means take me out of rotation. |

Both are anonymous and short-circuited, and use the framework's own response writer. Setting
`Cloudstrap:HealthChecks:Enabled` to `false` maps nothing; calling the method twice maps one set.

Checks register additively on the stock `IHealthChecksBuilder`, so `AddHealthChecks().AddCheck(...)` — yours
or another library's — composes with Cloudstrap's without special handling. A client with
`EnableHealthCheck` adds a readiness-tagged URI check named `{HealthCheckPrefix ?? name}-liveness` that
probes `BaseAddress + HealthCheckPath` and judges by status code. An unreachable dependency therefore takes
the instance out of rotation without provoking a restart loop.

The probe has its own named `HttpClient`, `{name}-liveness`, which you can reconfigure like any other client
— that is the seam for a custom handler, proxy or timeout.

## Blob storage

`AddCloudstrapBlobStorage()` registers a single `BlobContainerClient` for the container the application works
against.

| Setting | Default | Notes |
|---|---|---|
| `Cloudstrap:Storage:BlobServiceUri` | — | Required unless a connection string is supplied. |
| `Cloudstrap:Storage:ContainerName` | `SystemName` lowercased | |
| `Cloudstrap:Storage:ConnectionString` | — | Wins when set. `UseDevelopmentStorage=true` targets Azurite. |
| `Cloudstrap:Storage:CreateContainerIfNotExists` | `false` | |

When no connection string is configured in the section, the standard
`ConnectionStrings:CloudstrapStorage` entry is honored, so platform tooling can supply one the usual way.

Targeting a local emulator is an explicit setting, never an inferred environment. Container creation stays
opt-in because creating storage usually belongs to deployment and needs rights the application should not
hold; when you do ask for it, a failure surfaces rather than being swallowed.

## Data protection

`AddCloudstrapDataProtection()` persists the key ring to blob storage and encrypts it with a KeyVault key, so
cookies and antiforgery tokens issued by one instance are readable by every other and survive a restart.

| Setting | Default | Notes |
|---|---|---|
| `Cloudstrap:DataProtection:Enabled` | `false` | Disabled leaves the framework's own key storage in place. |
| `Cloudstrap:DataProtection:KeysBlobUri` | — | Required when enabled. |
| `Cloudstrap:DataProtection:KeyVaultKeyId` | — | Required when enabled. |
| `Cloudstrap:DataProtection:ApplicationName` | the workload name | Isolates payloads; set two apps to the same value to share deliberately. |

**Encryption is not optional by design.** Keys written unencrypted because a setting was forgotten is exactly
the failure this prevents, so a missing key identifier fails startup naming it. If you genuinely want blob
persistence without envelope encryption, configure the framework's own chain instead:

```csharp
builder.Services.AddDataProtection().PersistKeysToAzureBlobStorage(uri, credential);
```

> **Name collision**: `Cloudstrap.Extensions.DataProtectionOptions` shares its simple name with
> `Microsoft.AspNetCore.DataProtection.DataProtectionOptions`. Qualify or alias whichever you mean when both
> namespaces are in scope.

## Escape hatches

Every convention here has an override, and where a framework primitive is a better fit, use it directly:

| Instead of | Use |
|---|---|
| A custom probe response body | the framework's `MapHealthChecks` with your own writer |
| Data protection without KeyVault encryption | the framework's `AddDataProtection()` chain |
| A dependency check that is not a Cloudstrap peer | `AddHealthChecks().AddUrlGroup(...)` |
| Resilience | `ConfigureHttpClientDefaults` or per-client `AddStandardResilienceHandler` |

## Verifying KeyVault against a real vault

KeyVault is the one feature no automated test can prove — a test would have to contact a live vault. Run this
once against a real vault to confirm the behavior end to end.

1. **Create a vault** and note its URI, for example `https://contoso-vault.vault.azure.net/`.
2. **Grant yourself access.** Assign `Key Vault Secrets User` to the identity you will run as (your own
   sign-in for a local run, the app's managed identity in Azure).
3. **Add two secrets**, where `{prefix}` is your workload name — the lowercase
   `{SystemName}-{SubsystemName}-{SubsystemType}`, e.g. `contoso-orders-api`:
   - `{prefix}-Demo--Message` with value `hello from key vault`
   - `other-Ignored` with value `must not be loaded`
4. **Point the application at the vault:**
   ```json
   { "Cloudstrap": { "KeyVault": { "Enabled": true, "VaultUri": "https://contoso-vault.vault.azure.net/" } } }
   ```
5. **Run the application** and read configuration key `Demo:Message` — for example inject
   `IConfiguration` and log `configuration["Demo:Message"]`.

   **Expect:** `Demo:Message` resolves to `hello from key vault` — the prefix was stripped and `--` became
   `:`. Reading `other-Ignored` or `other:Ignored` yields nothing: the filter kept another workload's secret
   out.
6. **Verify precedence.** Add `"Demo": { "Message": "from appsettings" }` to `appsettings.json` and run
   again. **Expect:** the vault value still wins, because the vault source is added last.
7. **Verify the fail-fast.** Change `VaultUri` to a vault that does not exist and run again. **Expect:**
   startup fails with an Azure request error rather than starting with the secret missing. Then remove
   `VaultUri` entirely while leaving `Enabled` at `true`. **Expect:** a `ConfigurationValidationException`
   naming `Cloudstrap:KeyVault:VaultUri`.
8. **Verify the off switch.** Set `Enabled` to `false`. **Expect:** the application starts, contacts no
   vault, and `Demo:Message` falls back to whatever `appsettings.json` provides.

## Dependencies

All OSI-licensed. `AspNetCore.HealthChecks.Uris` (Xabaril) is **Apache-2.0**; every other dependency is MIT.

`Azure.Extensions.AspNetCore.Configuration.Secrets` · `Azure.Extensions.AspNetCore.DataProtection.Blobs` ·
`Azure.Extensions.AspNetCore.DataProtection.Keys` · `Azure.Identity` · `Azure.Storage.Blobs` ·
`AspNetCore.HealthChecks.Uris` · `Cloudstrap.Core` · `Cloudstrap.Observability`

There is no authentication package here, and no `Aspire.*` package anywhere in the closure.

## License

MIT
