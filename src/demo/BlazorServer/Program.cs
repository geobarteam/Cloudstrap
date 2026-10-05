using Cloudstrap.Authentication.OpenIdConnect;
using Cloudstrap.BlazorCommon;
using Cloudstrap.BlazorServer;
using Cloudstrap.Core;
using Cloudstrap.Demo.BlazorServer.Components;
using Cloudstrap.Demo.BlazorServer.Services;
using Cloudstrap.Demo.BlazorServer.ViewModels;
using Cloudstrap.Extensions;
using Cloudstrap.Hangfire;
using Cloudstrap.Observability;
using Microsoft.Extensions.Diagnostics.HealthChecks;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Fail fast: an invalid 'Cloudstrap' section aborts startup before the host is built (#1).
CloudstrapOptions cloudstrapOptions = builder.Configuration.GetCloudstrapOptions();

// Bootstrap logging covers the window before the host's own pipeline exists (#2).
using ILoggerFactory bootstrapLoggers = CloudstrapBootstrapLogger.Create(cloudstrapOptions);
ILogger startupLogger = bootstrapLoggers.CreateLogger("Cloudstrap.Demo.Startup");
startupLogger.LogInformation("Configuration loaded for {WorkloadName}", cloudstrapOptions.Application.WorkloadName);

// Otlp-mode observability (Cloudstrap:OpenTelemetry:Mode): telemetry exports to the conventional
// localhost OTLP endpoint when a collector listens — the app boots fine without one, and the
// EnableConsole default keeps spans visible on stdout (#2). A separate, visible call by design.
builder.UseCloudstrapObservability();

// The #12 composite replaces the hand-rolled AddRazorComponents/AddInteractiveServerComponents/
// AddCascadingAuthenticationState block: razor components with Interactive Server decided once,
// hardened antiforgery, HSTS, correlation, health checks — and IBlazorInteractionTrace, whose
// activity source is contributed to the observability pipeline above without owning any of it.
//
// Demo-only (#16): the composite hardens the antiforgery cookie to secure-only, and the Hangfire dashboard issues
// antiforgery tokens for its actions — which the framework refuses over the demo's plain http://127.0.0.1. In
// Development only, the cookie follows the request scheme instead; production serves HTTPS and keeps 'Always'.
builder.AddCloudstrapBlazorServer(blazor => blazor.Antiforgery = antiforgery =>
{
    if (builder.Environment.IsDevelopment())
    {
        antiforgery.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    }
});

// The #11 convention scan over this assembly: WhoAmIViewModel registers as IWhoAmIViewModel by its
// suffix — no explicit registration line (demo-level adoption, D-13).
builder.Services.AddCloudstrapBlazorCommon<IWhoAmIViewModel>();

// Interactive user login (#10): the hardened cookie session and the auth-code + PKCE challenge
// against the shared demo IdP on 5310 — pairing stays a separate, deliberately visible call.
builder.Services.AddCloudstrapOpenIdConnect();

// A typed client driven by Cloudstrap:HttpClients:DemoApi and flagged AddUserAccessToken: the
// signed-in user's token transparently reaches the Api demo host on 5330 (#4 + #9/#10 plumbing,
// unchanged by #12 — there is no BlazorServer client API).
builder.Services.AddCloudstrapHttpServiceClient<IDemoApiClient, DemoApiClient>("DemoApi");

// Hangfire (#16) as a storage-only host: the same storage and schema as the Worker demo (DefaultConnection,
// CloudstrapDemo, Hangfire's default 'HangFire' schema), but RunServer = false — it never dequeues, never
// schedules and never reconciles. The dashboard below renders the Worker's jobs through the shared dispatcher
// type: this project does not reference the Worker. The storage check is Degraded on failure, so the app stays
// ready (and boots) without the job storage.
builder.AddCloudstrapHangfire(hangfire => hangfire.RunServer = false);
builder.Services.AddHealthChecks().AddCloudstrapHangfireHealthCheck(failureStatus: HealthStatus.Degraded);

WebApplication app = builder.Build();

// The #12 composite replaces the hand-placed UseAuthentication/UseAuthorization/UseAntiforgery/
// MapStaticAssets/MapRazorComponents block with the fixed hardened order: auth middleware appears
// because the OIDC scheme above is registered. The #10 login/logout endpoints map via the
// ConfigureEndpoints hook — endpoint-routing specificity lets the explicit routes win over the
// component catch-all, so mapping them after the components is fine. The Hangfire dashboard (#16) maps in the
// same hook, after the auth middleware by construction: only an authenticated user in
// Cloudstrap:Hangfire:Dashboard:RequiredRole ('tester', the seeded demo user's role) is served.
app.UseCloudstrapBlazorServer<App>(pipeline => pipeline.ConfigureEndpoints = endpoints =>
{
    endpoints.MapCloudstrapAuthenticationEndpoints();
    endpoints.MapCloudstrapHangfireDashboard();
});

await app.RunAsync();

/// <summary>The host entry point — partial so components can reference the app assembly.</summary>
public partial class Program
{
    /// <summary>Prevents direct construction — the type exists for assembly identity only.</summary>
    protected Program()
    {
    }
}
