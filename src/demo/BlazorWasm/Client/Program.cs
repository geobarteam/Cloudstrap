using Cloudstrap.BlazorCommon;
using Cloudstrap.Core;
using Cloudstrap.Demo.BlazorWasm.Presentation;
using Cloudstrap.Demo.BlazorWasm.Presentation.Doctors;
using Cloudstrap.Demo.BlazorWasm.Presentation.ErrorHandling;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

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
