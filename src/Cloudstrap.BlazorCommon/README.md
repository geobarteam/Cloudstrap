# Cloudstrap.BlazorCommon

Shared Blazor abstractions for Cloudstrap apps: the `IViewModel` initialization contract, the
consumer-implemented `IErrorHandler` feedback contract, and one-call [Scrutor](https://github.com/khellang/Scrutor)
convention registration of your presentation layer — usable from Blazor Server and WebAssembly alike.
The package references no Blazor package at all: it works in any host that has
`Microsoft.Extensions.DependencyInjection`.

## Quick start

```csharp
// Program.cs — WASM client (or any Blazor host)
builder.Services.AddCloudstrapBlazorCommon<IDoctorsViewModel>();          // scans the marker's assembly
builder.Services.AddScoped<IErrorHandler, SnackbarErrorHandler>();        // consumer-owned, any lifetime
```

One call scans the marker type's assembly and registers every public concrete class whose name ends
in `ViewModel` or `Service` as **all of its implemented interfaces**.

## Knobs

All conventions are code-level knobs at the call site — this package has **no** `Cloudstrap:`
configuration section and never reads `IConfiguration`.

| Knob | Default | Meaning |
|------|---------|---------|
| `ConventionSuffixes` | `["ViewModel", "Service"]` | Class-name suffixes the scan matches (ordinal). Replace the contents to change the conventions; clear the list for a deliberate no-op. |
| `Lifetime` | `Transient` | The lifetime applied to every convention registration. |
| `AdditionalAssemblies` | empty | Assemblies scanned in addition to the marker type's assembly. |

```csharp
builder.Services.AddCloudstrapBlazorCommon<IDoctorsViewModel>(options =>
{
    options.ConventionSuffixes.Add("Presenter");
    options.Lifetime = ServiceLifetime.Scoped;
    options.AdditionalAssemblies.Add(typeof(SomeOtherFeature).Assembly);
});
```

## Registration semantics

- **Interfaces-only** — a matching class with no interfaces registers nothing; the concrete type
  itself is never registered.
- **Append-on-repeat** — calling the method again appends registrations (standard
  `IServiceCollection` semantics). Call it once per scanned assembly set.
- **Ordinal suffix matching, once per suffix** — a name is matched by each suffix pass
  independently; don't name classes so that they end in two configured suffixes.
- A null/empty/whitespace suffix entry throws `ArgumentException` at the call — an emptied list is
  the supported no-op, a blank entry is always a bug.

## Escape hatch

Scrutor is a normal public dependency of this package. For anything beyond the three knobs —
self-registration, decorators, predicate filters — call `services.Scan(...)` directly; it composes
freely with `AddCloudstrapBlazorCommon`.

## `IErrorHandler` — consumer-implemented

The package ships the **contract only**: no implementation, no default registration. Implement it in
your app (for example over MudBlazor's `ISnackbar`) and register it at any lifetime. ViewModels
route failures to `HandleError(Exception)` / `ShowError(string)` instead of throwing into the render
loop.

## `IViewModel` — page initialization

```csharp
protected override async Task OnInitializedAsync() => await ViewModel.InitializeAsync();
```

`InitializeAsync(CancellationToken cancellationToken = default)` loads the ViewModel's initial
state. Cancellation is the implementer's duty: pass the token to every downstream call.

## Migrating from the source library

| Source | Here |
|--------|------|
| `IViewModel.InitializeAsync()` | Gains an optional `CancellationToken` (D-1). |
| `IErrorHandler.ShowWarning` / `ShowSuccess` | Dropped — zero call sites existed (D-2). |
| `INavigationService` / `AddNavigationService` | Dropped — inject `NavigationManager` directly (D-3). |
| `AddPresentationServices<T>` / `AddBlazorCommonForNihdi` | Folded into `AddCloudstrapBlazorCommon<T>` with overridable conventions (D-4). |
| `NihdiWasmControls` | Dropped — use the router's native `AdditionalAssemblies` parameter (D-5). |

## License

MIT — part of the [Cloudstrap](https://github.com/geobarteam/Cloudstrap) suite.
