---
applyTo: "src/Cloudstrap.WebApi/**"
description: "WebApi middleware conventions: exception handling, correlation, API versioning, Swagger/NSwag setup, and middleware ordering."
---

# WebApi Conventions

## Entry Point

`WebApplicationBuilderExtensions.AddCloudstrapWebApi()` — orchestrates registration of Common services, Key Vault, HSTS, Razor components, HTTP client factory, controllers, localization, health checks, API versioning, Swagger, and exception handlers.

## Exception Handling

- Uses ASP.NET Core `IExceptionHandler` pattern (not middleware).
- `WebApiExceptionHandler` logs and returns standardized JSON errors.
- `WebApiExceptionHandlerForDevTst` variant exposes more detail in dev/test.
- Handlers are **order-dependent** — first handler returning `true` stops the chain.
- Register via `configureExceptionHandlers` callback in `AddCloudstrapWebApi()`.

## Correlation

- `CorrelationMiddleware` extracts correlation ID from request headers and stores in `ICorrelationContextAccessor`.
- Propagated automatically to downstream HTTP calls.

## API Versioning & Documentation

- Uses `Asp.Versioning.Mvc` + `Asp.Versioning.Mvc.ApiExplorer` (not URL segment versioning by default).
- Documentation: **NSwag** (v14.6+), not Swashbuckle. Configured via `SwaggerBootstrapper.ConfigureSwaggerServices()`.
- API explorer UI: **Scalar.AspNetCore**.

## Dependencies

- Project reference to `Cloudstrap.Common`.
