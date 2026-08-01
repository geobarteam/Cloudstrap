using Cloudstrap.Core;
using Cloudstrap.WasmTestProject.Presentation;
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

await builder.Build().RunAsync();
