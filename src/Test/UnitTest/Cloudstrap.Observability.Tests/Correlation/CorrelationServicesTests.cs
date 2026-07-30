namespace Cloudstrap.Observability.Tests.Correlation
{
    using System.Diagnostics;
    using Cloudstrap.Observability.Correlation;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using NUnit.Framework;

    [TestFixture]
    public sealed class CorrelationServicesTests
    {
        [Test]
        public void AddCloudstrapCorrelation_ResolvesAccessorAndSource()
        {
            // Arrange
            using ServiceProvider provider = BuildProvider();

            // Act & Assert — both resolve, both are singletons
            Assert.Multiple(() =>
            {
                Assert.That(
                    provider.GetRequiredService<ICorrelationContextAccessor>(),
                    Is.SameAs(provider.GetRequiredService<ICorrelationContextAccessor>()));
                Assert.That(
                    provider.GetRequiredService<ICorrelationSource>(),
                    Is.SameAs(provider.GetRequiredService<ICorrelationSource>()));
            });
        }

        [Test]
        public void UseCloudstrapObservability_RegistersCorrelationServices()
        {
            // Arrange
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(
                new HostApplicationBuilderSettings { DisableDefaults = true });
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cloudstrap:Application:SystemName"] = "Contoso",
                ["Cloudstrap:Application:SubsystemName"] = "Orders",
                ["Cloudstrap:Application:SubsystemType"] = "Api",
            });
            builder.UseCloudstrapObservability();

            // Act
            using IHost host = builder.Build();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(host.Services.GetService<ICorrelationContextAccessor>(), Is.Not.Null);
                Assert.That(host.Services.GetService<ICorrelationSource>(), Is.Not.Null);
            });
        }

        [Test]
        public async Task CorrelationId_SetInAsyncFlow_FlowsAcrossAwait()
        {
            // Arrange
            using ServiceProvider provider = BuildProvider();
            ICorrelationContextAccessor accessor = provider.GetRequiredService<ICorrelationContextAccessor>();

            // Act
            accessor.CorrelationId = "flow-through-await";
            await Task.Yield();

            // Assert
            Assert.That(accessor.CorrelationId, Is.EqualTo("flow-through-await"));
        }

        [Test]
        public async Task CorrelationId_SetInParallelFlows_IsIsolatedPerFlow()
        {
            // Arrange
            using ServiceProvider provider = BuildProvider();
            ICorrelationContextAccessor accessor = provider.GetRequiredService<ICorrelationContextAccessor>();
            using SemaphoreSlim bothSet = new(0, 2);
            string? observedByFirst = null;
            string? observedBySecond = null;

            // Act — each flow sets its own id; setting a second id in another flow never throws
            Task firstFlow = Task.Run(async () =>
            {
                accessor.CorrelationId = "flow-a";
                bothSet.Release();
                await Task.Yield();
                observedByFirst = accessor.CorrelationId;
            });
            Task secondFlow = Task.Run(async () =>
            {
                accessor.CorrelationId = "flow-b";
                bothSet.Release();
                await Task.Yield();
                observedBySecond = accessor.CorrelationId;
            });
            await Task.WhenAll(firstFlow, secondFlow);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(observedByFirst, Is.EqualTo("flow-a"));
                Assert.That(observedBySecond, Is.EqualTo("flow-b"));
            });
        }

        [Test]
        public void GenerateCorrelation_WithCurrentActivity_ReturnsItsTraceId()
        {
            // Arrange
            using ServiceProvider provider = BuildProvider();
            ICorrelationSource source = provider.GetRequiredService<ICorrelationSource>();
            using Activity activity = new("Contoso.Test.Operation");
            activity.Start();

            // Act
            string correlation = source.GenerateCorrelation();

            // Assert — logs, traces and the header agree by construction
            Assert.That(correlation, Is.EqualTo(activity.TraceId.ToString()));
        }

        [Test]
        public void GenerateCorrelation_WithoutActivity_ReturnsParseableGuid()
        {
            // Arrange
            using ServiceProvider provider = BuildProvider();
            ICorrelationSource source = provider.GetRequiredService<ICorrelationSource>();
            Activity? current = Activity.Current;
            Activity.Current = null;
            try
            {
                // Act
                string correlation = source.GenerateCorrelation();

                // Assert
                Assert.That(Guid.TryParse(correlation, out _), Is.True);
            }
            finally
            {
                Activity.Current = current;
            }
        }

        [Test]
        public void AddCloudstrapCorrelation_WithConsumerRegisteredSource_DoesNotOverride()
        {
            // Arrange
            ServiceCollection services = new();
            services.AddSingleton<ICorrelationSource, FakeCorrelationSource>();

            // Act
            services.AddCloudstrapCorrelation();
            using ServiceProvider provider = services.BuildServiceProvider();

            // Assert — "every convention has an override"
            Assert.That(provider.GetRequiredService<ICorrelationSource>(), Is.InstanceOf<FakeCorrelationSource>());
        }

        [Test]
        public void AddCloudstrapCorrelation_CalledTwice_RegistersSingleAccessor()
        {
            // Arrange
            ServiceCollection services = new();

            // Act
            services.AddCloudstrapCorrelation();
            services.AddCloudstrapCorrelation();
            using ServiceProvider provider = services.BuildServiceProvider();

            // Assert
            Assert.That(provider.GetServices<ICorrelationContextAccessor>().Count(), Is.EqualTo(1));
        }

        private static ServiceProvider BuildProvider()
        {
            ServiceCollection services = new();
            services.AddCloudstrapCorrelation();

            return services.BuildServiceProvider();
        }

        private sealed class FakeCorrelationSource : ICorrelationSource
        {
            public string GenerateCorrelation() => "fake-correlation";
        }
    }
}
