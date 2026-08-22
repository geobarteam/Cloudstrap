namespace Cloudstrap.Authentication.ClientCredentials.Tests.Infrastructure
{
    using System.Security.Claims;
    using Asp.Versioning;
    using Cloudstrap.TestIdentityProvider;
    using Cloudstrap.WebApi;
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.TestHost;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using NUnit.Framework;

    /// <summary>
    /// A TestServer-hosted #5 API protected by <c>AddCloudstrapJwtBearer</c>, validating against the test
    /// identity provider's <em>real</em> discovery document and JWKS — the identity provider's in-process
    /// handler is the bearer metadata backchannel (plan mechanic (a.3)), so no configuration is
    /// pre-seeded and no sockets are opened.
    /// </summary>
    internal sealed class ProtectedApiHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private ProtectedApiHost(WebApplication app)
        {
            _app = app;
        }

        /// <summary>
        /// Creates the in-process handler consumer clients route their requests through.
        /// </summary>
        /// <returns>The TestServer handler.</returns>
        public HttpMessageHandler CreateHandler() => _app.GetTestServer().CreateHandler();

        /// <summary>
        /// Starts the protected API, validating tokens issued by the given identity provider.
        /// </summary>
        /// <param name="identityProvider">The identity provider whose metadata the bearer fetches.</param>
        /// <param name="audience">The audience the API accepts.</param>
        /// <returns>The running host.</returns>
        public static async Task<ProtectedApiHost> StartAsync(
            TestIdentityProviderHost identityProvider,
            string audience = ClientCredentialsTestHost.Audience)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                // Development, so #5's RequireHttpsMetadata exemption lets the http:// test issuer through
                EnvironmentName = "Development",

                // Deliberately not the test assembly (the WebApi.Tests precedent): the named assembly
                // has no controllers of its own, so the fixture controller arrives only via the
                // explicit application part below
                ApplicationName = "Cloudstrap.WebApi",
            });

            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cloudstrap:Application:SystemName"] = "Contoso",
                ["Cloudstrap:Application:SubsystemName"] = "ProtectedApi",
                ["Cloudstrap:Application:SubsystemType"] = "Api",
                ["Cloudstrap:JwtBearer:Authority"] = identityProvider.BaseAddress.AbsoluteUri,
                ["Cloudstrap:JwtBearer:Audience"] = audience,
                ["Logging:LogLevel:Default"] = "Warning",
            });
            builder.WebHost.UseTestServer();

            builder.AddCloudstrapWebApi(configurator =>
                configurator.Mvc = mvc => mvc.AddApplicationPart(typeof(ProtectedApiHost).Assembly));
            builder.AddCloudstrapJwtBearer(bearer =>
                bearer.BackchannelHttpHandler = identityProvider.CreateHandler());

            WebApplication app = builder.Build();
            app.UseCloudstrapWebApi();
            await app.StartAsync(TestContext.CurrentContext.CancellationToken);

            return new ProtectedApiHost(app);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync() => await _app.DisposeAsync();
    }

    /// <summary>The validated caller identity, echoed back from the token's claims.</summary>
    /// <param name="ClientId">The caller's <c>client_id</c> claim.</param>
    /// <param name="Scope">The caller's <c>scope</c> claim.</param>
    public sealed record MachineEchoDto(string ClientId, string Scope);

    /// <summary>
    /// The protected endpoint of the interop proof: reachable only with a validated bearer token, and
    /// answering with what the token said.
    /// </summary>
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/machine-echo")]
    public sealed class MachineEchoController : ControllerBase
    {
        /// <summary>Echoes the validated caller's identity claims.</summary>
        /// <returns>The caller's <c>client_id</c> and <c>scope</c>.</returns>
        [HttpGet("status")]
        [Authorize]
        public ActionResult<MachineEchoDto> GetStatus() => Ok(new MachineEchoDto(
            User.FindFirstValue("client_id") ?? string.Empty,
            User.FindFirstValue("scope") ?? string.Empty));
    }
}
