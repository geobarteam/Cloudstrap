---
name: refit
description: "Use when creating, modifying, or reviewing Refit HTTP service clients in Cloudstrap. Covers interface definition, client registration, feature service wrapping, DTO mapping, Result<T> error handling, and Refit attributes ([Body], [Query], [Multipart], [AliasAs], CancellationToken)."
metadata:
  argument-hint: "Describe the Refit task, e.g. 'add a Refit client for the Invoices feature' or 'register a new service client'"
---

# Refit HTTP Service Clients

Refit generates `HttpClient` implementations from C# interfaces decorated with HTTP-method attributes. In this solution, Refit clients are the **only** way to call API endpoints from the presentation layer.

> **Related:** `build-feature` skill's Presentation Layer template shows how the Refit interface fits into a full vertical slice (Refit → `*ServiceClient` → ViewModel → Page). This skill drills into the Refit interface itself — attributes, error handling, file uploads.

## Quick Reference

| Attribute | Purpose |
|-----------|---------|
| `[Get("/path")]` | HTTP GET |
| `[Post("/path")]` | HTTP POST |
| `[Put("/path")]` | HTTP PUT |
| `[Delete("/path")]` | HTTP DELETE |
| `[Patch("/path")]` | HTTP PATCH |
| `[Body]` | Serialize parameter as request body (JSON) |
| `[Query]` | Bind parameter as query string |
| `[AliasAs("name")]` | Rename parameter in route/query |
| `[Multipart]` | Send as multipart/form-data (file uploads) |
| `[Header("X-Custom")]` | Bind parameter as request header |
| `[Headers("Accept: application/json")]` | Static header on method or interface |

---

## 1. Interface Definition

```csharp
using Refit;

public interface I<Feature>ServiceClient
{
    [Get("/api/<feature>")]
    Task<List<<Feature>Dto>> GetAllAsync(CancellationToken cancellationToken = default);

    [Get("/api/<feature>/{id}")]
    Task<<Feature>Dto> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    [Post("/api/<feature>")]
    Task<<Feature>Dto> CreateAsync([Body] Add<Feature>Dto dto, CancellationToken cancellationToken = default);

    [Put("/api/<feature>/{id}")]
    Task<<Feature>Dto> UpdateAsync(int id, [Body] Update<Feature>Dto dto, CancellationToken cancellationToken = default);

    [Delete("/api/<feature>/{id}")]
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}
```

### Rules

- Every method **must** accept `CancellationToken cancellationToken = default` as the last parameter.
- Routes must match the target controller routes exactly.
- Return the DTO type directly — Refit deserializes automatically using the configured `RefitSettings`.
- Use `[Body]` for request payloads, `[Query]` for query-string params.
- Use `[AliasAs("api-version")][Query] string? apiVersion = null` when the controller requires API versioning.
- Use `[Multipart]` with `StreamPart` for file uploads.

---

## 2. Client Registration

### BlazorServer — `AddCloudstrapRefitClient<T>`

Register in the shared client configurator (or equivalent configurator class):

```csharp
private static readonly RefitSettings DefaultRefitSettings = new()
{
    ContentSerializer = new SystemTextJsonContentSerializer(
        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
};

private static IServiceCollection AddCloudstrapRefitClient<TClient>(
    this IServiceCollection services,
    string configSectionName)
    where TClient : class
{
    services.AddCloudstrapHttpServiceClient<TClient, TClient>(
        configSectionName,
        httpClientBuilder =>
        {
            httpClientBuilder.AddTypedClient(httpClient =>
                RestService.For<TClient>(httpClient, DefaultRefitSettings));
        });
    return services;
}
```

Usage — one line per client:

```csharp
services.AddCloudstrapRefitClient<I<Feature>Client>("<Feature>");
```

The `configSectionName` maps to an `appsettings.json` entry under `Cloudstrap.Common.HttpClient` which provides the base address, resilience policies, and token management.

### BlazorWasm — `AddCloudstrapWasmRefitClient<T>`

The shipped helper from `Cloudstrap.BlazorWasm` — one line, no private extension needed:

```csharp
builder.Services.AddCloudstrapWasmRefitClient<I<Feature>ServiceClient>(
    builder.HostEnvironment.BaseAddress);
```

It registers the interface through the cookie+XSRF pipeline (`CookieHandler` + the shared
`IAntiforgeryTokenStore`), returns `IHttpClientBuilder` for chaining, and defaults to
System.Text.Json with camelCase + case-insensitive serialization; pass a `RefitSettings` to
override per registration. Internally the client is built with `RestService.For` — **never**
`Refit.HttpClientFactory`, which was compiled against `Microsoft.Extensions.Http` 9.x and throws
`MissingMethodException` on .NET 10 WASM.

The `CookieHandler` automatically:
1. Sets `BrowserRequestCredentials.Include` on every request (sends the `HttpOnly` session cookie).
2. Adds the configured XSRF header (default `X-XSRF-TOKEN`) on mutating methods (POST, PUT, DELETE, PATCH).

---

## 3. Feature Service (DTO → Model Mapping)

Never inject Refit clients into ViewModels. Wrap them in a feature service:

```csharp
public class <Feature>Service(I<Feature>ServiceClient client) : I<Feature>Service
{
    public async Task<List<<Feature>Model>> GetAllAsync(CancellationToken ct = default)
    {
        List<<Feature>Dto> dtos = await client.GetAllAsync(ct);
        return dtos.Select(d => new <Feature>Model { Id = d.Id, Name = d.Name }).ToList();
    }

    public async Task<Result<<Feature>Model>> CreateAsync(<Feature>Model model, CancellationToken ct = default)
    {
        var dto = new Add<Feature>Dto { Name = model.Name };
        <Feature>Dto created = await client.CreateAsync(dto, ct);
        return new Result<<Feature>Model>(new <Feature>Model { Id = created.Id, Name = created.Name });
    }
}
```

### Service Rules

- Use primary constructor injection.
- Map DTO → Model inside the service. ViewModels never see DTOs.
- Return a success/failure type from **LanguageExt.Core** (e.g. `Fin<T>` / `Either<Error, T>`, per the plan's chosen mapping) for operations that can fail — Cloudstrap has no hand-rolled `Result<T>`.
- Return plain collections for read-only queries.

---

## 4. Error Handling

Refit throws `ApiException` for non-success HTTP status codes.

```csharp
try
{
    var result = await client.GetByIdAsync(id, ct);
}
catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
{
    // Handle 404 — entity not found
}
catch (ApiException ex)
{
    // ex.Content contains the raw error body
    // ex.StatusCode has the HTTP status
    string? message = ex.GetUserFriendlyMessage();
}
```

Use `ApiExceptionExtensions` from the shared service clients:
- `exception.GetUserFriendlyMessage()` — maps HTTP status codes to user messages.
- `exception.RequiresReauthentication()` — returns `true` for 401.
- `exception.TryGetErrorMessage(out string msg)` — handles both `ApiException` and `HttpRequestException`.

For typed error responses, use `ApiResponse<T>` as the return type:

```csharp
[Get("/api/<feature>/{id}")]
Task<ApiResponse<<Feature>Dto>> GetByIdAsync(int id, CancellationToken cancellationToken = default);
```

This never throws on non-success — check `response.IsSuccessStatusCode` and `response.Error` instead.

---

## 5. Advanced Patterns

### File Upload

```csharp
[Multipart]
[Post("/api/<feature>/upload")]
Task<UploadResultDto> UploadAsync(
    [AliasAs("file")] StreamPart file,
    [AliasAs("description")] string description,
    CancellationToken cancellationToken = default);
```

Call with:

```csharp
var streamPart = new StreamPart(stream, fileName, contentType);
await client.UploadAsync(streamPart, "My document", ct);
```

### Dynamic Headers

```csharp
[Get("/api/<feature>")]
Task<List<<Feature>Dto>> GetAllAsync(
    [Header("X-Correlation-Id")] string correlationId,
    CancellationToken cancellationToken = default);
```

### Query Parameters with Collections

```csharp
[Get("/api/<feature>")]
Task<List<<Feature>Dto>> SearchAsync(
    [Query(CollectionFormat.Multi)] int[] ids,
    CancellationToken cancellationToken = default);
```

---

## Anti-Patterns

| Don't | Do |
|-------|-----|
| Register Refit clients in `Program.cs` | Use the shared client configurator / service-client registration entry point |
| Skip `CancellationToken` on Refit methods | Always include as last parameter with `= default` |
| Return DTOs from feature services | Map DTO → Model in the service layer |
| Inject Refit clients into ViewModels | Inject the `I<Feature>Service` wrapper |
| Create a new `HttpClient` manually | Use the registration helpers that configure resilience and auth |
| Bypass `CookieHandler` (WASM) | All WASM Refit clients must go through `AddCloudstrapWasmRefitClient<T>` |
| Store or forward tokens manually | `CookieHandler` + `IAntiforgeryTokenStore` handles XSRF (WASM) |
| Use `Newtonsoft.Json` serialization | `RefitSettings` uses `SystemTextJsonContentSerializer` with camelCase |

## Best Practices

1. **One interface per bounded context / feature** — keep client interfaces focused.
2. **Match route casing to controllers** — Refit is case-sensitive for route parameters.
3. **Prefer DTO records** — use `record` types for request/response DTOs for immutability.
4. **Propagate `CancellationToken`** — from the UI layer through service to Refit call.
5. **Use `ApiResponse<T>` sparingly** — prefer catching `ApiException` in the feature service unless you need to inspect headers or status without exceptions.
6. **Keep `RefitSettings` centralized** — share the single `DefaultRefitSettings` instance declared in the configurator.
7. **Never duplicate `HttpMessageHandler` pipelines** — the registration helpers already wire auth, resilience, and cookie handling.
