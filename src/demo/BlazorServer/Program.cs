using Cloudstrap.Authentication.OpenIdConnect;
using Cloudstrap.Core;
using Cloudstrap.Demo.BlazorServer.Components;
using Cloudstrap.Demo.BlazorServer.Services;
using Cloudstrap.Extensions;
using Cloudstrap.Observability;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Fail fast: an invalid 'Cloudstrap' section aborts startup before the host is built (#1).
CloudstrapOptions cloudstrapOptions = builder.Configuration.GetCloudstrapOptions();

// Bootstrap logging covers the window before the host's own pipeline exists (#2).
using ILoggerFactory bootstrapLoggers = CloudstrapBootstrapLogger.Create(cloudstrapOptions);
ILogger startupLogger = bootstrapLoggers.CreateLogger("Cloudstrap.Demo.Startup");
startupLogger.LogInformation("Configuration loaded for {WorkloadName}", cloudstrapOptions.Application.WorkloadName);

// Otlp-mode observability (Cloudstrap:OpenTelemetry:Mode): telemetry exports to the conventional
// localhost OTLP endpoint when a collector listens — the app boots fine without one (#2).
builder.UseCloudstrapObservability();

// Stock Blazor Server — deliberately no Cloudstrap.BlazorServer helper code (D-B/AC-DR13):
// deliverable #12 extends this app with the real helpers later.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();

// Interactive user login (#10): the hardened cookie session and the auth-code + PKCE challenge
// against the shared demo IdP on 5310 — the same one call the Bff makes.
builder.Services.AddCloudstrapOpenIdConnect();

// A typed client driven by Cloudstrap:HttpClients:DemoApi and flagged AddUserAccessToken: the
// signed-in user's token transparently reaches the Api demo host on 5330 (#4 + #9/#10 plumbing).
builder.Services.AddCloudstrapHttpServiceClient<IDemoApiClient, DemoApiClient>("DemoApi");

WebApplication app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();

// The #10 opt-in login/logout endpoints, before the component endpoints so the explicit routes win.
app.MapCloudstrapAuthenticationEndpoints();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.RunAsync();

/// <summary>The host entry point — partial so components can reference the app assembly.</summary>
public partial class Program
{
    /// <summary>Prevents direct construction — the type exists for assembly identity only.</summary>
    protected Program()
    {
    }
}
