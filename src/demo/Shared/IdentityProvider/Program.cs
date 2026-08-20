using Cloudstrap.TestIdentityProvider;
using Cloudstrap.WasmTestProject.Host.IdentityProvider;

// The demo identity-provider host (demo-only test infrastructure — never a real IdP): the seeded
// test provider as its own process, so the full app is launchable with two dotnet run commands or
// the VS Code compound configuration. The port comes from ASPNETCORE_URLS / launchSettings.json
// (default http://127.0.0.1:5310); the shape mirrors TestIdentityProviderHost's pipeline on a stock
// WebApplication, minus the token-counter middleware — counters are a fixture concern.
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// The application base addresses decide which Bff instances may sign in — every address contributes
// a signin-oidc redirect URI, so a differently-ported instance is one configuration override away.
Uri[] applicationBaseAddresses =
    builder.Configuration.GetSection("WasmTestProject:ApplicationBaseAddresses").Get<string[]>() is
    { Length: > 0 } configured
        ? [.. configured.Select(static address => new Uri(address))]
        : [new Uri("http://127.0.0.1:5300/"), new Uri("https://localhost:7200/")];

builder.Services.AddRouting();
builder.Services.AddCloudstrapTestIdentityProvider(
    options => TestIdentityProviderSeed.Configure(options, applicationBaseAddresses));

WebApplication app = builder.Build();

app.UseAuthentication();
app.MapCloudstrapTestIdentityProvider();

await app.RunAsync();
