using Cloudstrap.BlazorCommon;
using Cloudstrap.BlazorWasm;
using Cloudstrap.Core;
using Cloudstrap.Demo.BlazorWasm.Presentation;
using Cloudstrap.Demo.BlazorWasm.Presentation.Diagnostics;
using Cloudstrap.Demo.BlazorWasm.Presentation.Doctors;
using Cloudstrap.Demo.BlazorWasm.Presentation.ErrorHandling;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// The #13 composite replaces the plain AddScoped(_ => new HttpClient(...)) line: the cookie+XSRF
// pipeline, BFF-driven authentication state (AuthorizeView + cascading state work out of the box),
// and the shared antiforgery token store — configured from Cloudstrap:BlazorWasm when present.
builder.AddCloudstrapBlazorWasm();

// One line makes the Refit interface a working client riding the same hardened pipeline: browser
// credentials always, the captured XSRF token attached on mutating calls (deliverable #13 demo).
builder.Services.AddCloudstrapWasmRefitClient<IDoctorServiceClient>(builder.HostEnvironment.BaseAddress);

// The package's second flavor, same pipeline: a plain typed client for the diagnostics fetch —
// the raw scoped HttpClient this app used to hand-register is gone entirely.
builder.Services.AddCloudstrapWasmHttpClient<DiagnosticsClient>(builder.HostEnvironment.BaseAddress);

// Cloudstrap.Core is host-agnostic and WASM-loadable: the client binds its own
// 'Cloudstrap' section from wwwroot/appsettings.json (deliverable #1 demo).
builder.Services.AddCloudstrapCore();

builder.Services.AddMudServices();

// Cloudstrap.BlazorCommon (deliverable #11): one call scans the Presentation assembly and
// registers every public concrete *ViewModel/*Service as its implemented interfaces (transient) —
// DoctorsViewModel becomes resolvable as IDoctorsViewModel (and IViewModel) right here.
builder.Services.AddCloudstrapBlazorCommon<IDoctorsViewModel>();

// The consumer owns the IErrorHandler implementation and its registration — the package ships
// neither. SnackbarErrorHandler ends in "Handler" on purpose: it sits outside the convention scan.
builder.Services.AddScoped<IErrorHandler, SnackbarErrorHandler>();

await builder.Build().RunAsync();
