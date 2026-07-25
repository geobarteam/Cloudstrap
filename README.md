# Cloudstrap

**Opinionated bootstrapping for ASP.NET Core applications on Azure.**

Cloudstrap is an MIT-licensed suite of NuGet packages that takes an ASP.NET Core application from `dotnet new` to production-ready on Azure with a few extension-method calls and one configuration section:

- **Configuration** — strongly-typed, validated settings model; Azure KeyVault configuration source
- **Observability** — OpenTelemetry traces/metrics/logs wired to Application Insights (or any OTLP backend), Serilog bootstrap logging, correlation, smart noise filtering
- **Authentication** — OIDC login and client-credentials flows on stock ASP.NET Core handlers + Duende.AccessTokenManagement
- **Messaging** — Wolverine-based bootstrap: Azure Service Bus / SQL Server transports, durable transactional outbox, Azure Blob claim check
- **Background jobs** — Hangfire scheduler with recurring-task discovery and dashboard auth patterns
- **Blazor** — server and WebAssembly helpers for browser-based apps (cookie auth, XSRF, Refit)
- **Web** — WebApi bootstrap (versioning, OpenAPI, hardened middleware), Worker-service bootstrap, proxy forwarding helpers
- **Extras** — health checks, ops dashboard, cookie consent, consent-gated analytics (Matomo / GA4), localization setup

## Status

🚧 **Pre-release — extraction in progress.** Cloudstrap is being extracted from a battle-tested enterprise library. The founding specification lives in [`_specs/Cloudstrap.md`](_specs/Cloudstrap.md): package map, architecture decisions, and acceptance criteria. Packages will appear on nuget.org under the `Cloudstrap.*` prefix as extraction slices complete.

## Design principles

1. **Opinionated defaults, always overridable** — every convention (naming, endpoints, headers) has a documented escape hatch.
2. **Azure-first, not Azure-locked** — Application Insights is the default telemetry backend; a generic OTLP mode is the escape hatch.
3. **Minimal dependency surface** — external dependencies are reviewed, wrapped behind abstractions, and OSI-licensed only.
4. **`internal` by default** — small, deliberate public API surface with XML docs, guard clauses, and `CancellationToken` throughout.

## License

[MIT](LICENSE)
