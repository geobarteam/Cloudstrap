using Cloudstrap.Core;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Fail fast: an invalid 'Cloudstrap' section aborts startup before the host is built
// (deliverable #1 demo — see the Startup_MissingSystemName E2E test).
_ = builder.Configuration.GetCloudstrapOptions();

builder.Services.AddCloudstrapCore();
builder.Services.AddControllers();

WebApplication app = builder.Build();

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.MapControllers();
app.MapFallbackToFile("index.html");

await app.RunAsync();
