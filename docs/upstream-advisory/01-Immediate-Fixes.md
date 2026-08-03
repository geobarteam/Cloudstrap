# Immediate Fixes — Nihdi.Core.Configuration

**Report 1 of 2** · Companion: [02-Modernization-Roadmap.md](02-Modernization-Roadmap.md)

| | |
|---|---|
| **Subject** | `Nihdi.Core.Configuration` (private enterprise suite), current release line **4.0.0** |
| **Verified against** | HEAD `2d38c712` at `D:\source\Nihdi-Core-Configuration\Nihdi-Core-Configuration\src` — see the staleness note below |
| **Origin** | Findings surfaced while extracting the suite into the open-source Cloudstrap library |
| **Date** | 2026-08-03 |
| **Scope** | Non-breaking, low-risk, urgent. No public API signature changes. Everything here targets 4.0.1. |

> **⚠ Verify before acting.** Every finding below was confirmed by reading the working tree at HEAD `2d38c712`, whose `RELEASE_NOTES.md` tops out at 3.4.1 and whose newest tag is `3.4.0`. **That snapshot predates the current 4.0.0 line**, so anything fixed between 3.4.1 and 4.0.0 will show here as open when it is not. Re-check each item against `main` before scheduling it. The file:line citations make that cheap — most are a single grep.

## How this relates to the existing review

The legacy repo contains its own review at `_reviews/2026-07-03-full-codebase-review.md` — 320 lines, P0–P3, with its own remediation order. **Per the team, that review has been implemented apart from its low-priority tail**, and spot-checks against HEAD `2d38c712` confirm it:

- **P0-2 (TLS bypass) is remediated.** `Common/Dynatrace/DynatraceHttpClient.cs:49-61` now gates the callback behind an `allowInvalidCertificates` parameter, documented "never enable outside local/dev", with tests covering both states.
- **P0-3 (OAuth client secret in the browser) is remediated** on the live Scalar path (commit `2d38c712`, "remove obsolete client secret references"). The only surviving occurrence is `WebApi/Swagger/SwaggerBootstrapper.cs:85`, which has **zero references anywhere in the tree** — that is the review's own P2-9, still open, and correctly classified as low priority.

This report is written from a different angle: it lists what the *migration* exposed — defects and gaps that only become visible when you rebuild the library from scratch against modern .NET conventions. Sections A and B are **new findings not present in the July review**. Section C records what is verifiably still in the tree, and corrects two items.

One finding below (**B4**) is worth reading even if you read nothing else, because it explains why one of the review's *implemented* P1 fixes is not actually in effect at runtime.

---

# Section A — Build hygiene

Two small items. Neither touches product code.

> **Settled, not findings.** Two build-configuration observations from the extraction were reviewed by the team and closed as working-as-intended. They are recorded here so nobody re-raises them:
>
> - **`NU1901`–`NU1904` in `WarningsNotAsErrors` is deliberate policy.** Some private-feed dependencies have no alternative package, so the build must survive a published advisory against them. The blanket carve-out is the accepted trade-off. *(If you ever want the narrower version, `<NuGetAuditSuppress Include="https://github.com/advisories/GHSA-…" />` suppresses named advisories individually, so new ones against packages that **do** have alternatives still surface. Entirely optional.)*
> - **The hard-coded `<Version>` elements are inert.** Versioning is applied by the CI/CD pipeline outside the local build; a command-line `-p:Version=` overrides the csproj property, so the checked-in values never reach a published package. No `GitVersion.yml` is needed in the repo, and the July review's P2-26 is a cosmetic cleanup at most.

### A1 · `TreatWarningsAsErrors` is substantially neutered, and contradicts `.editorconfig`

`Directory.Build.props` enables ~200 StyleCop rules via `EnabledCodeStyleAnalysisRuleIds`, then lists that same variable inside `WarningsNotAsErrors` — so every style rule is advisory. The remainder are suppressed through `NoWarn`. Meanwhile a separate 20 KB `src/.editorconfig` assigns severities to ~110 SA rules, several of which contradict the props file (`SA1600` is `warning` in `.editorconfig` and `NoWarn`'d in the props; the props win).

The net effect is "compiler warnings are errors, style is decoration" — which is a legitimate choice, but it is not the choice the configuration appears to make, and two files disagree about it. Pick one mechanism and let the other go. Cloudstrap dropped StyleCop entirely in favour of `AnalysisLevel=latest-recommended` + `EnforceCodeStyleInBuild` + `.editorconfig` — ~200 lines of build configuration replaced by three properties, with naming still build-breaking via `dotnet_diagnostic.IDE1006.severity = warning`.

**Risk.** None to ship; this is a decision to record, not a code change.

### A2 · `global.json` does not pin the SDK

It pins only the `Microsoft.Testing.Platform` test runner. Builds are therefore not reproducible across developer machines or CI images — an SDK feature-band roll can change analyzer behaviour under `TreatWarningsAsErrors` and break the build for reasons unrelated to any commit.

**Fix.** Add `"sdk": { "version": "10.0.xxx", "rollForward": "latestFeature" }`.

**Risk.** None.

---

# Section B — Live defects not in the July review

### B1 · Configuration binding mutates the object graph it returns

`Nihdi.Core.Configuration/ConfigurationExtensions.cs:20-40`

```csharp
public static NihdiConfiguration GetNihdiConfiguration(this IConfiguration configuration)
{
    NihdiConfiguration nihdiConfiguration = configuration.GetSection("Nihdi").Get<NihdiConfiguration>()
        ?? throw new ConfigurationException("Nihdi section was not found in Appsettings. …");
    nihdiConfiguration.ConnectionStrings = GetConnectionStrings(configuration);
    // …then imperatively copies ClientId / ClientSecret / TenantId from AppRegistration
    //    into NServiceBus.Transport.AzureServiceBusTransport …
```

Binding and business logic are fused. Two consequences, both live:

- A consumer who binds `NihdiConfiguration` through the normal framework route (`GetSection("Nihdi").Get<NihdiConfiguration>()`, or `IOptions<NihdiConfiguration>` as the Dashboard package does) gets a **different object** than one who calls `GetNihdiConfiguration()` — the Service Bus credentials are absent and authentication fails at connect time with no indication why.
- The credential copy is invisible in configuration. Nothing in `appsettings.json` explains why `NServiceBus:Transport:AzureServiceBusTransport:ClientSecret` is populated at runtime but empty on disk, which makes the whole path hostile to debug.

**Fix (non-breaking).** Move the credential propagation into an explicitly named method the caller invokes, or — better and still non-breaking — into a `PostConfigure` step so it applies through every binding route. Document that `GetNihdiConfiguration()` is not a pure bind.

### B2 · `AddOpenTelemetry` collides with the OpenTelemetry SDK's own extension method

`Nihdi.Core.Configuration.Common/DistributedTracing/ServiceCollectionExtensions.cs:62`

```csharp
public static IServiceCollection AddOpenTelemetry(this IServiceCollection services, NihdiConfiguration configuration, ILogger logger)
```

The OpenTelemetry SDK ships `IServiceCollection.AddOpenTelemetry()` in `OpenTelemetry.Extensions.Hosting`, which `Common` already references. A consumer with both `using` directives in scope gets overload resolution between two unrelated methods of the same name on the same receiver — resolved by argument count, so it compiles either way and the failure mode is a silently mis-wired telemetry pipeline rather than a compiler error.

**Fix (non-breaking).** Add a correctly named method (`AddNihdiTelemetry` or, better, whatever single convention Section 3 of the roadmap settles on), mark the colliding one `[Obsolete]`, and forward. The rename itself is breaking; adding the alias is not.

### B3 · The telemetry bootstrap destroys logging providers the host already registered

`Nihdi.Core.Configuration.Common/Extensions/LoggingBuilderExtensions.cs:84` — `ConfigureForNihdiOpenTelemetry` opens with `loggingBuilder.ClearProviders()`.

Any provider registered before `AddNihdiCommonServices` is silently discarded. Today that mostly bites consumers who add their own sink; it becomes a hard blocker the moment any team adopts Aspire ServiceDefaults, which registers providers during `AddServiceDefaults()`.

**Fix (non-breaking).** Delete the `ClearProviders()` call and add Nihdi's provider additively. If clearing is genuinely wanted in some topology, make it an opt-in flag rather than the unconditional default. Cloudstrap registers Serilog "as one provider among the host's — never through Serilog's factory replacement" and has not needed a clear.

### B4 · The configuration validation graph is never invoked by the library — which neutralizes most of the July review's validation batch

`NihdiConfiguration` implements `IValidatableObject`, nine sub-configurations do too, cross-field rules are written, and `Validate()` throws a well-formatted aggregate `ValidationException`. It is genuinely good code.

**A repo-wide search finds exactly one production call site, and it is in a test SUT:** `src/Test/WasmTestProject/src/Host/Cfe/Program.cs:52`. The `TestProject` Bff, Wfe and Worker hosts — the three reference templates consumers copy — never call it. There is no `ValidateDataAnnotations()`, no `ValidateOnStart()`, no `IValidateOptions<T>` anywhere in the suite.

The library documents its own gap in an exception message (`DistributedTracing/ServiceCollectionExtensions.cs:255`):

> `"OpenTelemetry.BaseUri must be configured when Mode is Otlp. Call NihdiConfiguration.Validate() at startup to catch this earlier."`

**Why this matters beyond itself — a fix you already shipped is not in effect.** The review's P1-5 (unrecognized transport type silently falls back to `LearningTransport`) was addressed the right way, as a validation rule. `Settings/NServiceBus/TransportConfiguration.cs:60-66`:

```csharp
public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
{
    if (!ValidTransportTypes.IsValid(Type))
    {
        results.Add(new ValidationResult($"The TransportType '{Type}' is not supported by NServiceBus Nihdi configuration."));
    }
```

But the fallback it defends against is still reachable, because that rule never executes. `EndpointConfigurationBuilder.cs:156` still opens with `TransportDefinition transportDef = new LearningTransport();` followed by three non-exhaustive `if` branches and no `else` — so a typo in `Nihdi:NServiceBus:Transport:Type` in a production endpoint still yields a learning-transport endpoint that processes nothing, exactly as P1-5 described. **The guard is written but not armed.** Arming it is one line in each host's `Main`; it is the highest-value item in this report.

The same reasoning applies to the review's entire "Config binding & validation" batch — P2-24 through P2-32, eleven findings — which describes bugs *inside* validators that do not run in any shipped host. Fix them and nothing observable changes; arm validation without fixing them and latent bugs become startup crashes (P2-24 throws `ArgumentNullException` from inside `Validate` when a Bridge `Destination` is missing). The two halves have to move together, validators first.

**Fix.** Treat "invoke validation" and "fix the validators" as one unit of work, sequenced validators-first. Ship it behind an opt-in flag (`Nihdi:Application:ValidateConfigurationOnStart`, default `false`) so it is non-breaking now, then flip the default in the next major. That sequencing is carried into the roadmap as release 4.1.0 item 3.

### B5 · Documented configuration that does not exist

Two settings are documented but unimplemented, so operators can set them and observe no effect:

| Documented | Reality |
|---|---|
| `Nihdi:Worker` section with `HTTP_PROXY` / `HTTPS_PROXY` / `NO_PROXY` (`readme.md:956`) | No `WorkerConfiguration` type exists anywhere under `Settings/` — verified against the full directory listing | => delete doc
| `Nihdi:Dashboard:MaxPeekMessages` (`readme.md:966`) | Hard-coded `_maxPeekMessagesLimit = 1000` in `Dashboard.Components.Shared/ViewModels/QueueExplorerViewModel.cs:17` |  => implement , should overwrite hard coded

**Fix.** Either implement the binding or delete the documentation. `MaxPeekMessages` is the easier of the two — the constant is already isolated. (Related: the July review's P2-37 covers a third instance of this class of drift, the orphaned `Nihdi:Otlp` block in the Bff SUT's `appsettings.json`.)

---

# Section C — Low-priority tail: what is verifiably still in the tree

Verified at HEAD `2d38c712` on 2026-08-03. These are the review items I could confirm remain — consistent with the team's position that the P0/P1 work landed and the low-priority tail did not. Listed because they are cheap and several are prerequisites for the roadmap.

| ID | Finding | Verified state |
|---|---|---|
| **P2-31** | `"Nihdi::HttpClientServiceRegistry"` — double colon, binds nothing (`Common/HttpClient/ConfigurationExtension.cs:18`) | Present. **`GetHttpServiceClientConfig` has zero callers in the tree** — it is dead as well as broken. Public API, so `[Obsolete]` it now and delete in the next major rather than removing outright. |
| **P2-15** | Process-wide `static ConcurrentDictionary` de-dupes health-check registration (`Common/HttpClient/ServiceCollectionExtensions.cs:20,104`) | Present. One interaction the review does not mention: `HostRunner.RunAsync(logger, params IHost[])` explicitly supports multiple hosts in one process, so a second host silently losing its liveness check is reachable by design, not only in tests. |
| **P2-1** | CORS falls back to `AllowAnyOrigin()` when `Security:AllowedOrigins` is empty (`Common/Services/ServiceCollectionExtensions.cs:70-73`) | Present, warning-only. Worth re-triaging above "low": combined with B4 there is no startup gate to catch it, so a misconfigured production app gets open CORS with nothing but a log line. Fix is small — fail outside dev, or default closed. |
| **P2-9** | Dead `SwaggerBootstrapper` embeds `Scalar.OAuth.ClientSecret` in a browser page config (`WebApi/Swagger/SwaggerBootstrapper.cs:85`) | Present but **unreferenced** — the last surviving copy of the P0-3 pattern. Delete the file; the live path is already fixed. |
| **P2-23 / P3** | Duplicated `NServiceBus/TransportConfiguration/` folder | Present. `LearningBuilder`, `SqlTransportBuilder`, `TransactionModeMapper` are byte-identical to `Transport/` except for the namespace, and `internal`. Safe delete. |
| **P3** | `ValidateAzureKeyVaultUrl` never called (`NServiceBus/Encryption/NihdiConfigurationExtensions.cs:102`) | Present — `private`, zero call sites. Safe delete, no deprecation needed. |
| **P3** | `TracingEnricher` public no-op (`Common/Logging/TracingEnricher.cs:9`) | Present. Public, so `[Obsolete]` cycle. |
| **P3** | `ValidTransportTypes` is a mutable `public struct` of string constants | Present. Converting to an `enum` (or at minimum a `static class`) is breaking — batch it into the major, per the roadmap. Its `IsValid` helper is what B4 needs armed. |

### Correction 1 — the log-level fallback is already partly mitigated

`_bugs/BUG-LogLevelSilentFallbackToWarning.md` describes a silent fallback to `Warning` on an unrecognized log level. The current code (`BootstrapLoggerFactory.cs:379-392`) **does emit a diagnostic**:

```csharp
logger?.Warning(
    "'{LogLevel}' is not a recognized log level. Falling back to Warning. Valid values are: {ValidValues}",
    logLevel, string.Join(", ", Enum.GetNames<LogEventLevel>()));
return LogEventLevel.Warning;
```

It is no longer silent. Residual risk is narrower than the bug file states: the warning goes to a *backup* logger that may not be wired to where operators look, and the fallback still raises the effective level, so a typo like `"Infromation"` costs you every Info-level log. Downgrade this to a validation item — it disappears for free once B4 lands, since an unparseable level becomes a startup validation failure.

### Correction 2 — the committed credentials are already triaged; do not re-escalate

I initially flagged the committed secrets as the top priority. **The July review already triaged this** (P2-39, "downgraded from Critical") with explicit team confirmation that everything under `src/Test/` is local-development-only, that TST/VAL/PRD secrets live in KeyVault, and that there is no rotation emergency. I am deferring to that decision rather than re-litigating it.

Two things are still worth doing, and both are cheap:

- The exposure is broader than the review's list: Entra-shaped client secrets appear in **6 files**, including two distinct live-looking secrets (`iX18Q~…` in both `TestProject/src/Host/Bff/appsettings.json:22` and `WasmTestProject/src/Host/Cfe/appsettings.json:13`; `BRv8Q~…` in `Bridge/BridgeConsole/appsettings.json:58`).
- **Add the secret-scanning pipeline gate** (gitleaks or equivalent) that P2-39's fix recommends. That is the part that prevents the next one, and it is the only item here with lasting value.

---

# Suggested order

Sequenced so nothing later is invalidated by something earlier.

| # | Item | Why here | Effort |
|---|---|---|---|
| 1 | B3 — remove `ClearProviders()`; B2 — add the non-colliding telemetry alias | Both are prerequisites for the observability work in 4.1.0 | Small |
| 2 | C — delete the dead code (`TransportConfiguration/`, `ValidateAzureKeyVaultUrl`, `SwaggerBootstrapper`), `[Obsolete]` the dead-but-public (`GetHttpServiceClientConfig`, `TracingEnricher`) | Shrinks the surface the roadmap has to carry; removes the last P0-3 copy | Small |
| 3 | P2-1 — close the CORS default (fail outside dev, or default to no cross-origin access) | Highest residual risk in the tree, and B4 is not landing this week | Small |
| 4 | B1 — make the credential propagation explicit; B5 — reconcile docs with code | Independent | Small |
| 5 | A2 — pin the SDK in `global.json` | Independent; makes every subsequent build reproducible | Trivial |
| 6 | A1 — settle the analyzer story; add the secret-scanning gate | Housekeeping, no ship risk | Medium |

**Deliberately excluded from this report:** anything that changes a public signature — the `IOptions<T>` migration, the package split, sealing/internalizing, and converting `ValidTransportTypes` to an enum. Those are breaking and are sequenced in [report 2](02-Modernization-Roadmap.md).

**B4 is excluded from this list too, and that is a scheduling judgement worth revisiting.** Arming validation is one line per host, but it must ship together with the validator fixes (P2-24…P2-32) or it converts latent config bugs into startup crashes — so it belongs in 4.1.0, not a patch. If you want the P1-5 transport guard in effect sooner than 4.1.0, the narrow version is to add the `ValidTransportTypes.IsValid` check directly in `ConfigureTransport` and throw — that is a few lines, carries none of the blast radius of arming the whole graph, and closes the specific hole described in B4.
