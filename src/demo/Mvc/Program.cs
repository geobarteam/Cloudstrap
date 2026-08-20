using Cloudstrap.Mvc;

// The Cloudstrap.Mvc README's consumer example, live (D-3): two calls, one configuration section.
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddCloudstrapMvc();

WebApplication app = builder.Build();

app.UseCloudstrapMvc();

await app.RunAsync();
