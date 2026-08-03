namespace Cloudstrap.Observability.Tests.Correlation
{
    using System.Diagnostics;
    using Cloudstrap.Core;
    using Cloudstrap.Observability.Correlation;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Diagnostics.HealthChecks;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using NUnit.Framework;

    [TestFixture]
    public sealed class CloudstrapCorrelationMiddlewareTests
    {
        [Test]
        public async Task Invoke_WithInboundHeader_UsesInboundValueForTheWholeRequest()
        {
            // Arrange & Act
            (DefaultHttpContext context, string? observed) = await RunRequest(
                MinimalValid(),
                context => context.Request.Headers["X-Correlation-ID"] = "abc-123");

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(observed, Is.EqualTo("abc-123"));
                Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
            });
        }

        [Test]
        public async Task Invoke_WithConfiguredHeaderName_ReadsThatHeader()
        {
            // Arrange
            Dictionary<string, string?> values = MinimalValid();
            values["Cloudstrap:Correlation:HeaderName"] = "X-Request-ID";

            // Act
            (_, string? observed) = await RunRequest(
                values,
                context => context.Request.Headers["X-Request-ID"] = "req-456");

            // Assert
            Assert.That(observed, Is.EqualTo("req-456"));
        }

        [Test]
        public async Task Invoke_WithoutHeader_GeneratesIdFromCurrentTraceId()
        {
            // Arrange
            using Activity activity = new("Contoso.Test.Request");
            activity.Start();

            // Act
            (DefaultHttpContext context, string? observed) = await RunRequest(MinimalValid());

            // Assert — no exception, no 400, the trace id is the correlation id
            Assert.Multiple(() =>
            {
                Assert.That(observed, Is.EqualTo(activity.TraceId.ToString()));
                Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
            });
        }

        [Test]
        public async Task Invoke_WithInboundHeader_AlsoStoresTheIdOnTheHttpContext()
        {
            // Arrange & Act
            (DefaultHttpContext context, _) = await RunRequest(
                MinimalValid(),
                context => context.Request.Headers["X-Correlation-ID"] = "abc-123");

            // Assert — request-scoped, so it survives an exception unwinding past this middleware
            Assert.That(context.GetCloudstrapCorrelationId(), Is.EqualTo("abc-123"));
        }

        [Test]
        public async Task Invoke_WithoutHeader_StoresTheGeneratedIdOnTheHttpContext()
        {
            // Arrange
            using Activity activity = new("Contoso.Test.Request");
            activity.Start();

            // Act
            (DefaultHttpContext context, string? observed) = await RunRequest(MinimalValid());

            // Assert — the generated identifier is readable by an outer middleware too
            Assert.Multiple(() =>
            {
                Assert.That(context.GetCloudstrapCorrelationId(), Is.EqualTo(activity.TraceId.ToString()));
                Assert.That(context.GetCloudstrapCorrelationId(), Is.EqualTo(observed));
            });
        }

        [Test]
        public void GetCloudstrapCorrelationId_BeforeTheMiddlewareRan_ReturnsNull()
        {
            // Arrange
            DefaultHttpContext context = new();

            // Act + Assert
            Assert.That(context.GetCloudstrapCorrelationId(), Is.Null);
        }

        [Test]
        public void GetCloudstrapCorrelationId_OnNullContext_ThrowsArgumentNullException()
        {
            // Arrange
            HttpContext context = null!;

            // Act + Assert
            Assert.That(() => context.GetCloudstrapCorrelationId(), Throws.ArgumentNullException);
        }

        [Test]
        public async Task Invoke_WithoutHeaderAndRequireForAllEndpoints_Returns400ProblemJsonNamingHeader()
        {
            // Arrange
            Dictionary<string, string?> values = MinimalValid();
            values["Cloudstrap:Correlation:Request:RequireForAllEndpoints"] = "true";

            // Act
            (DefaultHttpContext context, string? observed) = await RunRequest(
                values,
                context => context.Request.Path = "/orders/42");

            // Assert
            context.Response.Body.Position = 0;
            string body = await new StreamReader(context.Response.Body).ReadToEndAsync();
            Assert.Multiple(() =>
            {
                Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
                Assert.That(context.Response.ContentType, Does.StartWith("application/problem+json"));
                Assert.That(body, Does.Contain("X-Correlation-ID"));
                Assert.That(observed, Is.Null);
            });
        }

        [Test]
        public async Task Invoke_RequiredButPathIsConfiguredHealthEndpoint_PassesWithoutHeader()
        {
            // Arrange
            Dictionary<string, string?> values = MinimalValid();
            values["Cloudstrap:Correlation:Request:RequireForAllEndpoints"] = "true";

            // Act — /healthz is in the default HealthEndpoints
            (DefaultHttpContext context, string? observed) = await RunRequest(
                values,
                context => context.Request.Path = "/healthz");

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
                Assert.That(observed, Is.Not.Null);
            });
        }

        [Test]
        public async Task Invoke_RequiredButPathIsExcludedEndpoint_PassesWithoutHeader()
        {
            // Arrange
            Dictionary<string, string?> values = MinimalValid();
            values["Cloudstrap:Correlation:Request:RequireForAllEndpoints"] = "true";
            values["Cloudstrap:Correlation:Request:ExcludeEndpoints:0"] = "/status";

            // Act
            (DefaultHttpContext context, _) = await RunRequest(
                values,
                context => context.Request.Path = "/status");

            // Assert
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        }

        [Test]
        public async Task Invoke_RequiredButEndpointHasAllowNoCorrelation_PassesWithoutHeader()
        {
            // Arrange
            Dictionary<string, string?> values = MinimalValid();
            values["Cloudstrap:Correlation:Request:RequireForAllEndpoints"] = "true";

            // Act
            (DefaultHttpContext context, _) = await RunRequest(
                values,
                context =>
                {
                    context.Request.Path = "/orders/42";
                    context.SetEndpoint(new Endpoint(
                        requestDelegate: null,
                        new EndpointMetadataCollection(new AllowNoCorrelationAttribute()),
                        "OptedOutEndpoint"));
                });

            // Assert
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        }

        [Test]
        public async Task Invoke_RequiredButEndpointHasHealthCheckMetadata_PassesWithoutHeader()
        {
            // Arrange
            Dictionary<string, string?> values = MinimalValid();
            values["Cloudstrap:Correlation:Request:RequireForAllEndpoints"] = "true";

            // Act
            (DefaultHttpContext context, _) = await RunRequest(
                values,
                context =>
                {
                    context.Request.Path = "/custom-probe";
                    context.SetEndpoint(new Endpoint(
                        requestDelegate: null,
                        new EndpointMetadataCollection(new HealthCheckOptions()),
                        "HealthProbeEndpoint"));
                });

            // Assert
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        }

        [Test]
        public async Task Invoke_EndpointMarkedCorrelationRequired_Returns400WithoutHeaderEvenWhenGlobalOff()
        {
            // Arrange & Act — RequireForAllEndpoints stays at its false default
            (DefaultHttpContext context, _) = await RunRequest(
                MinimalValid(),
                context =>
                {
                    context.Request.Path = "/orders/42";
                    context.SetEndpoint(new Endpoint(
                        requestDelegate: null,
                        new EndpointMetadataCollection(new CorrelationRequiredAttribute()),
                        "MandatoryCorrelationEndpoint"));
                });

            // Assert
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
        }

        [Test]
        public async Task Invoke_WithHeaderAndRequirement_PassesAndUsesHeader()
        {
            // Arrange
            Dictionary<string, string?> values = MinimalValid();
            values["Cloudstrap:Correlation:Request:RequireForAllEndpoints"] = "true";

            // Act
            (DefaultHttpContext context, string? observed) = await RunRequest(
                values,
                context =>
                {
                    context.Request.Path = "/orders/42";
                    context.Request.Headers["X-Correlation-ID"] = "abc-123";
                });

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
                Assert.That(observed, Is.EqualTo("abc-123"));
            });
        }

        private static Dictionary<string, string?> MinimalValid() => new()
        {
            ["Cloudstrap:Application:SystemName"] = "Contoso",
            ["Cloudstrap:Application:SubsystemName"] = "Orders",
            ["Cloudstrap:Application:SubsystemType"] = "Api",
        };

        private static async Task<(DefaultHttpContext Context, string? ObservedCorrelationId)> RunRequest(
            Dictionary<string, string?> configValues,
            Action<DefaultHttpContext>? configureContext = null)
        {
            ServiceCollection services = new();
            services.AddSingleton<IConfiguration>(
                new ConfigurationBuilder().AddInMemoryCollection(configValues).Build());
            services.AddCloudstrapCore();
            services.AddCloudstrapCorrelation();
            await using ServiceProvider provider = services.BuildServiceProvider();

            string? observed = null;
            ApplicationBuilder app = new(provider);
            app.UseCloudstrapCorrelation();
            app.Run(context =>
            {
                observed = provider.GetRequiredService<ICorrelationContextAccessor>().CorrelationId;
                return Task.CompletedTask;
            });
            RequestDelegate pipeline = app.Build();

            DefaultHttpContext context = new() { RequestServices = provider };
            context.Response.Body = new MemoryStream();
            configureContext?.Invoke(context);

            await pipeline(context);

            return (context, observed);
        }
    }
}
