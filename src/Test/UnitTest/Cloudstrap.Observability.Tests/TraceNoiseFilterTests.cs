namespace Cloudstrap.Observability.Tests
{
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Options;
    using NUnit.Framework;
    using OpenTelemetry.Instrumentation.AspNetCore;
    using OpenTelemetry.Instrumentation.Http;

    [TestFixture]
    public sealed class TraceNoiseFilterTests
    {
        [Test]
        public void AspNetCoreFilter_WithConfiguredLivenessPath_DropsRequest()
        {
            // Arrange
            Dictionary<string, string?> values = ConsoleModeValid();
            values["Cloudstrap:HealthChecks:LivenessPath"] = "/alive";
            using IHost host = BuildHost(values);

            // Act
            bool traced = AspNetCoreFilter(host)(ContextForPath("/alive"));

            // Assert
            Assert.That(traced, Is.False);
        }

        [Test]
        public void AspNetCoreFilter_WithReadinessPath_DropsRequest()
        {
            // Arrange
            using IHost host = BuildHost(ConsoleModeValid());

            // Act
            bool traced = AspNetCoreFilter(host)(ContextForPath("/ready"));

            // Assert
            Assert.That(traced, Is.False);
        }

        [Test]
        public void AspNetCoreFilter_WithBlazorHubPath_DropsRequest()
        {
            // Arrange
            using IHost host = BuildHost(ConsoleModeValid());

            // Act
            bool traced = AspNetCoreFilter(host)(ContextForPath("/_blazor"));

            // Assert
            Assert.That(traced, Is.False);
        }

        [Test]
        public void AspNetCoreFilter_WithFrameworkOrContentPath_DropsRequest()
        {
            // Arrange
            using IHost host = BuildHost(ConsoleModeValid());
            Func<HttpContext, bool> filter = AspNetCoreFilter(host);

            // Act & Assert
            Assert.Multiple(() =>
            {
                Assert.That(filter(ContextForPath("/_framework/blazor.web.js")), Is.False);
                Assert.That(filter(ContextForPath("/_content/lib/site.css")), Is.False);
            });
        }

        [Test]
        public void AspNetCoreFilter_WithStaticAssetExtension_DropsRequest()
        {
            // Arrange
            using IHost host = BuildHost(ConsoleModeValid());

            // Act
            bool traced = AspNetCoreFilter(host)(ContextForPath("/favicon.ico"));

            // Assert
            Assert.That(traced, Is.False);
        }

        [Test]
        public void AspNetCoreFilter_WithBusinessPath_KeepsRequest()
        {
            // Arrange
            using IHost host = BuildHost(ConsoleModeValid());

            // Act
            bool traced = AspNetCoreFilter(host)(ContextForPath("/orders/42"));

            // Assert
            Assert.That(traced, Is.True);
        }

        [Test]
        public void AspNetCoreFilter_WithConsumerFilterAlreadySet_ComposesBothMustPass()
        {
            // Arrange
            HostApplicationBuilder builder = CreateBuilder(ConsoleModeValid());
            builder.Services.Configure<AspNetCoreTraceInstrumentationOptions>(instrumentation =>
                instrumentation.Filter = context => !context.Request.Path.StartsWithSegments("/vetoed"));
            builder.UseCloudstrapObservability();
            using IHost host = builder.Build();
            Func<HttpContext, bool> filter = AspNetCoreFilter(host);

            // Act & Assert
            Assert.Multiple(() =>
            {
                Assert.That(filter(ContextForPath("/vetoed/orders")), Is.False);
                Assert.That(filter(ContextForPath("/healthz")), Is.False);
                Assert.That(filter(ContextForPath("/orders/42")), Is.True);
            });
        }

        [Test]
        public void AspNetCoreFilter_WithIgnoredPathSegments_DropsConfiguredSegment()
        {
            // Arrange
            HostApplicationBuilder builder = CreateBuilder(ConsoleModeValid());
            builder.UseCloudstrapObservability(options => options.IgnoredPathSegments.Add("/metrics-ui"));
            using IHost host = builder.Build();

            // Act
            bool traced = AspNetCoreFilter(host)(ContextForPath("/metrics-ui"));

            // Assert
            Assert.That(traced, Is.False);
        }

        [Test]
        public void AspNetCoreFilter_WithDefaultFilterDisabled_KeepsProbePath()
        {
            // Arrange
            HostApplicationBuilder builder = CreateBuilder(ConsoleModeValid());
            builder.UseCloudstrapObservability(options => options.EnableDefaultTraceNoiseFilter = false);
            using IHost host = builder.Build();
            Func<HttpContext, bool>? filter = host.Services
                .GetRequiredService<IOptions<AspNetCoreTraceInstrumentationOptions>>().Value.Filter;

            // Act
            bool traced = filter?.Invoke(ContextForPath("/healthz")) ?? true;

            // Assert
            Assert.That(traced, Is.True);
        }

        [Test]
        public void HttpClientFilter_WithStaticAssetUri_DropsRequest()
        {
            // Arrange
            using IHost host = BuildHost(ConsoleModeValid());
            using HttpRequestMessage request = new(HttpMethod.Get, "https://cdn.example.com/styles/site.css");

            // Act
            bool traced = HttpClientFilter(host)(request);

            // Assert
            Assert.That(traced, Is.False);
        }

        [Test]
        public void HttpClientFilter_WithApiUri_KeepsRequest()
        {
            // Arrange
            using IHost host = BuildHost(ConsoleModeValid());
            using HttpRequestMessage request = new(HttpMethod.Get, "https://api.example.com/orders/42");

            // Act
            bool traced = HttpClientFilter(host)(request);

            // Assert
            Assert.That(traced, Is.True);
        }

        private static Func<HttpContext, bool> AspNetCoreFilter(IHost host) =>
            host.Services.GetRequiredService<IOptions<AspNetCoreTraceInstrumentationOptions>>().Value.Filter!;

        private static Func<HttpRequestMessage, bool> HttpClientFilter(IHost host) =>
            host.Services.GetRequiredService<IOptions<HttpClientTraceInstrumentationOptions>>()
                .Value.FilterHttpRequestMessage!;

        private static DefaultHttpContext ContextForPath(string path)
        {
            DefaultHttpContext context = new();
            context.Request.Path = path;

            return context;
        }

        private static IHost BuildHost(Dictionary<string, string?> values)
        {
            HostApplicationBuilder builder = CreateBuilder(values);
            builder.UseCloudstrapObservability();

            return builder.Build();
        }

        private static Dictionary<string, string?> ConsoleModeValid() => new()
        {
            ["Cloudstrap:Application:SystemName"] = "Contoso",
            ["Cloudstrap:Application:SubsystemName"] = "Orders",
            ["Cloudstrap:Application:SubsystemType"] = "Api",
            ["Cloudstrap:OpenTelemetry:Mode"] = "Console",
        };

        private static HostApplicationBuilder CreateBuilder(Dictionary<string, string?> values)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(
                new HostApplicationBuilderSettings { DisableDefaults = true });
            builder.Configuration.AddInMemoryCollection(values);

            return builder;
        }
    }
}
