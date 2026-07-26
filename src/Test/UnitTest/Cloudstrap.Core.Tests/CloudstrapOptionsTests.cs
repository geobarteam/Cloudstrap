namespace Cloudstrap.Core.Tests
{
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;
    using NUnit.Framework;

    [TestFixture]
    public sealed class CloudstrapOptionsTests
    {
        private static readonly string[] _defaultHealthEndpoints = ["/healthz", "/ready"];

        [Test]
        public void GetSection_WithFullCloudstrapSection_BindsEverySubsection()
        {
            // Arrange
            var values = new Dictionary<string, string?>
            {
                ["Cloudstrap:Application:SystemName"] = "Contoso",
                ["Cloudstrap:Application:SubsystemName"] = "Orders",
                ["Cloudstrap:Application:SubsystemType"] = "Api",
                ["Cloudstrap:Application:PathBase"] = "orders/",
                ["Cloudstrap:Logging:Level"] = "Warning",
                ["Cloudstrap:Logging:LevelOverrides:Microsoft.AspNetCore"] = "Error",
                ["Cloudstrap:Logging:EnrichProperties:Team"] = "Fulfilment",
                ["Cloudstrap:Logging:Console:Enabled"] = "false",
                ["Cloudstrap:Logging:File:Enabled"] = "true",
                ["Cloudstrap:Logging:File:Path"] = "/var/log/orders",
                ["Cloudstrap:OpenTelemetry:Mode"] = "Otlp",
                ["Cloudstrap:OpenTelemetry:Endpoint"] = "https://collector.example.com/",
                ["Cloudstrap:OpenTelemetry:Headers:X-Api-Key"] = "abc123",
                ["Cloudstrap:OpenTelemetry:EnableSqlClientInstrumentation"] = "true",
                ["Cloudstrap:OpenTelemetry:AlwaysOnSampler"] = "true",
                ["Cloudstrap:Correlation:HeaderName"] = "X-Request-ID",
                ["Cloudstrap:Correlation:Request:RequireForAllEndpoints"] = "true",
                ["Cloudstrap:Correlation:Request:ExcludeEndpoints:0"] = "/metrics",
                ["Cloudstrap:Correlation:Message:RequireForAllMessageHandlers"] = "true",
                ["Cloudstrap:Correlation:Message:ExcludeMessageHandlers:0"] = "Contoso.Orders.PingHandler",
                ["Cloudstrap:HealthChecks:Enabled"] = "false",
                ["Cloudstrap:HealthChecks:LivenessPath"] = "/alive",
                ["Cloudstrap:HealthChecks:ReadinessPath"] = "/readyz",
                ["Cloudstrap:HttpClients:CatalogApi:BaseAddress"] = "https://catalog.example.com/",
                ["Cloudstrap:HttpClients:CatalogApi:Timeout"] = "00:00:10",
                ["Cloudstrap:HttpClients:CatalogApi:AddClientAccessToken"] = "true",
                ["Cloudstrap:HttpClients:CatalogApi:EnableHealthCheck"] = "true",
                ["Cloudstrap:HttpClients:CatalogApi:HealthCheckPrefix"] = "catalog",
                ["Cloudstrap:HttpClients:CatalogApi:TokenRequestParameters:Scope"] = "catalog.read",
                ["Cloudstrap:HttpClients:CatalogApi:TokenRequestParameters:ForceRenewal"] = "true",
                ["Cloudstrap:HttpClients:OrdersApi:BaseAddress"] = "https://orders.example.com/",
                ["Cloudstrap:HttpClients:OrdersApi:AddUserAccessToken"] = "true",
            };

            // Act
            CloudstrapOptions options = Bind(values);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(CloudstrapOptions.SectionName, Is.EqualTo("Cloudstrap"));

                Assert.That(options.Application.SystemName, Is.EqualTo("Contoso"));
                Assert.That(options.Application.WorkloadName, Is.EqualTo("contoso-orders-api"));
                Assert.That(options.Application.PathBase, Is.EqualTo("/orders"));

                Assert.That(options.Logging.Level, Is.EqualTo(LogLevel.Warning));
                Assert.That(options.Logging.LevelOverrides["Microsoft.AspNetCore"], Is.EqualTo(LogLevel.Error));
                Assert.That(options.Logging.EnrichProperties["Team"], Is.EqualTo("Fulfilment"));
                Assert.That(options.Logging.Console.Enabled, Is.False);
                Assert.That(options.Logging.File.Enabled, Is.True);
                Assert.That(options.Logging.File.Path, Is.EqualTo("/var/log/orders"));

                Assert.That(options.OpenTelemetry.Mode, Is.EqualTo(OpenTelemetryMode.Otlp));
                Assert.That(options.OpenTelemetry.Endpoint, Is.EqualTo(new Uri("https://collector.example.com/")));
                Assert.That(options.OpenTelemetry.Headers["X-Api-Key"], Is.EqualTo("abc123"));
                Assert.That(options.OpenTelemetry.EnableSqlClientInstrumentation, Is.True);
                Assert.That(options.OpenTelemetry.AlwaysOnSampler, Is.True);

                Assert.That(options.Correlation.HeaderName, Is.EqualTo("X-Request-ID"));
                Assert.That(options.Correlation.Request.RequireForAllEndpoints, Is.True);
                Assert.That(options.Correlation.Request.ExcludeEndpoints, Does.Contain("/metrics"));
                Assert.That(options.Correlation.Message.RequireForAllMessageHandlers, Is.True);
                Assert.That(options.Correlation.Message.ExcludeMessageHandlers, Does.Contain("Contoso.Orders.PingHandler"));

                Assert.That(options.HealthChecks.Enabled, Is.False);
                Assert.That(options.HealthChecks.LivenessPath, Is.EqualTo("/alive"));
                Assert.That(options.HealthChecks.ReadinessPath, Is.EqualTo("/readyz"));

                Assert.That(options.HttpClients, Has.Count.EqualTo(2));
                Assert.That(options.HttpClients["CatalogApi"].BaseAddress, Is.EqualTo(new Uri("https://catalog.example.com/")));
                Assert.That(options.HttpClients["CatalogApi"].Timeout, Is.EqualTo(TimeSpan.FromSeconds(10)));
                Assert.That(options.HttpClients["CatalogApi"].AddClientAccessToken, Is.True);
                Assert.That(options.HttpClients["CatalogApi"].EnableHealthCheck, Is.True);
                Assert.That(options.HttpClients["CatalogApi"].HealthCheckPrefix, Is.EqualTo("catalog"));
                Assert.That(options.HttpClients["CatalogApi"].TokenRequestParameters!.Scope, Is.EqualTo("catalog.read"));
                Assert.That(options.HttpClients["CatalogApi"].TokenRequestParameters!.ForceRenewal, Is.True);
                Assert.That(options.HttpClients["OrdersApi"].AddUserAccessToken, Is.True);
                Assert.That(options.HttpClients["OrdersApi"].TokenRequestParameters, Is.Null);
            });
        }

        [Test]
        public void GetSection_WithOnlyRequiredValues_AppliesDocumentedDefaults()
        {
            // Arrange
            var values = new Dictionary<string, string?>
            {
                ["Cloudstrap:Application:SystemName"] = "Contoso",
                ["Cloudstrap:Application:SubsystemName"] = "Orders",
                ["Cloudstrap:Application:SubsystemType"] = "Api",
            };

            // Act
            CloudstrapOptions options = Bind(values);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(options.HealthChecks.Enabled, Is.True);
                Assert.That(options.HealthChecks.LivenessPath, Is.EqualTo("/healthz"));
                Assert.That(options.HealthChecks.ReadinessPath, Is.EqualTo("/ready"));

                Assert.That(options.Correlation.HeaderName, Is.EqualTo("X-Correlation-ID"));
                Assert.That(options.Correlation.Request.RequireForAllEndpoints, Is.False);
                Assert.That(options.Correlation.Request.HealthEndpoints, Is.EqualTo(_defaultHealthEndpoints));
                Assert.That(options.Correlation.Request.ExcludeEndpoints, Is.Empty);
                Assert.That(options.Correlation.Message.RequireForAllMessageHandlers, Is.False);
                Assert.That(options.Correlation.Message.ExcludeMessageHandlers, Is.Empty);

                Assert.That(options.Logging.Level, Is.EqualTo(LogLevel.Information));
                Assert.That(options.Logging.LevelOverrides, Is.Empty);
                Assert.That(options.Logging.EnrichProperties, Is.Empty);
                Assert.That(options.Logging.Console.Enabled, Is.True);
                Assert.That(options.Logging.File.Enabled, Is.False);
                Assert.That(options.Logging.File.Path, Is.Null);

                Assert.That(options.OpenTelemetry.Mode, Is.EqualTo(OpenTelemetryMode.Disabled));
                Assert.That(options.OpenTelemetry.Endpoint, Is.Null);
                Assert.That(options.OpenTelemetry.Headers, Is.Empty);
                Assert.That(options.OpenTelemetry.EnableTracing, Is.True);
                Assert.That(options.OpenTelemetry.EnableMetrics, Is.True);
                Assert.That(options.OpenTelemetry.EnableLogs, Is.True);
                Assert.That(options.OpenTelemetry.EnableConsole, Is.True);
                Assert.That(options.OpenTelemetry.EnableRuntimeMetrics, Is.True);
                Assert.That(options.OpenTelemetry.EnableHttpClientMetrics, Is.True);
                Assert.That(options.OpenTelemetry.EnableAspNetCoreMetrics, Is.True);
                Assert.That(options.OpenTelemetry.EnableMessagingMetrics, Is.True);
                Assert.That(options.OpenTelemetry.EnableSqlClientInstrumentation, Is.False);
                Assert.That(options.OpenTelemetry.EnableBlazorHubTracing, Is.False);
                Assert.That(options.OpenTelemetry.AlwaysOnSampler, Is.False);

                Assert.That(options.HttpClients, Is.Empty);
                Assert.That(new HttpClientServiceOptions().Timeout, Is.EqualTo(TimeSpan.FromSeconds(30)));
            });
        }

        [Test]
        public void SectionNames_OfEverySubsection_AreRootedUnderCloudstrap()
        {
            // Arrange & Act & Assert
            Assert.Multiple(() =>
            {
                Assert.That(CloudstrapOptions.SectionName, Is.EqualTo("Cloudstrap"));
                Assert.That(ApplicationOptions.SectionName, Is.EqualTo("Cloudstrap:Application"));
                Assert.That(LoggingOptions.SectionName, Is.EqualTo("Cloudstrap:Logging"));
                Assert.That(OpenTelemetryOptions.SectionName, Is.EqualTo("Cloudstrap:OpenTelemetry"));
                Assert.That(CorrelationOptions.SectionName, Is.EqualTo("Cloudstrap:Correlation"));
                Assert.That(HealthChecksOptions.SectionName, Is.EqualTo("Cloudstrap:HealthChecks"));
            });
        }

        private static CloudstrapOptions Bind(Dictionary<string, string?> values)
        {
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(values)
                .Build();

            return configuration.GetSection(CloudstrapOptions.SectionName).Get<CloudstrapOptions>()
                ?? new CloudstrapOptions();
        }
    }
}
