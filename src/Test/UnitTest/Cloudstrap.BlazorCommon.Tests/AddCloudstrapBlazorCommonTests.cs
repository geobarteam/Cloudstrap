namespace Cloudstrap.BlazorCommon.Tests
{
    using Cloudstrap.BlazorCommon.Tests.Fixtures;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using NUnit.Framework;

    /// <summary>
    /// Behavioral tests for the default conventions of
    /// <see cref="ServiceCollectionExtensions.AddCloudstrapBlazorCommon{TAssemblyMarker}"/>:
    /// AC-BC1 (suffix classes registered as their interfaces, transient, distinct), AC-BC2's parity
    /// halves, AC-BC4's exclusion set, AC-BC5's no-default-registration half and AC-BC7
    /// (zero configuration involved).
    /// </summary>
    [TestFixture]
    public sealed class AddCloudstrapBlazorCommonTests
    {
        [Test]
        public void AddCloudstrapBlazorCommon_OnNullServices_ThrowsArgumentNullException()
        {
            // Arrange
            IServiceCollection services = null!;

            // Act & Assert
            Assert.That(
                () => services.AddCloudstrapBlazorCommon<ISampleViewModel>(),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddCloudstrapBlazorCommon_DefaultConventions_RegistersSuffixClassesAsTheirInterfaces()
        {
            // Arrange — a plain service collection with no configuration of any kind
            var services = new ServiceCollection();

            // Act
            services.AddCloudstrapBlazorCommon<ISampleViewModel>();

            // Assert
            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.Multiple(() =>
            {
                Assert.That(provider.GetRequiredService<ISampleViewModel>(), Is.InstanceOf<SampleViewModel>());
                Assert.That(provider.GetRequiredService<ISampleService>(), Is.InstanceOf<SampleService>());
            });
        }

        [Test]
        public void AddCloudstrapBlazorCommon_DefaultLifetime_IsTransientWithDistinctInstances()
        {
            // Arrange
            var services = new ServiceCollection();

            // Act
            services.AddCloudstrapBlazorCommon<ISampleViewModel>();

            // Assert
            List<ServiceDescriptor> viewModelDescriptors = [.. services
                .Where(descriptor => descriptor.ServiceType == typeof(ISampleViewModel))];
            using ServiceProvider provider = services.BuildServiceProvider();
            using IServiceScope scope = provider.CreateScope();
            Assert.Multiple(() =>
            {
                Assert.That(viewModelDescriptors, Is.Not.Empty);
                Assert.That(
                    viewModelDescriptors.Select(descriptor => descriptor.Lifetime),
                    Is.All.EqualTo(ServiceLifetime.Transient));
                Assert.That(
                    scope.ServiceProvider.GetRequiredService<ISampleViewModel>(),
                    Is.Not.SameAs(scope.ServiceProvider.GetRequiredService<ISampleViewModel>()));
            });
        }

        [Test]
        public void AddCloudstrapBlazorCommon_RegisteredViewModel_ResolvesViaIViewModel()
        {
            // Arrange
            var services = new ServiceCollection();

            // Act
            services.AddCloudstrapBlazorCommon<ISampleViewModel>();

            // Assert — AsImplementedInterfaces covers the package's own contract too
            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(provider.GetRequiredService<IViewModel>(), Is.InstanceOf<SampleViewModel>());
        }

        [Test]
        public void AddCloudstrapBlazorCommon_NonMatchingAbstractAndInternalTypes_AreNotRegistered()
        {
            // Arrange
            var services = new ServiceCollection();

            // Act
            services.AddCloudstrapBlazorCommon<ISampleViewModel>();

            // Assert
            Type[] excluded =
                [typeof(AbstractSampleViewModel), typeof(InternalSampleViewModel), typeof(PlainHelper)];
            Assert.That(
                services.Where(descriptor => excluded.Contains(descriptor.ImplementationType)),
                Is.Empty);
        }

        [Test]
        public void AddCloudstrapBlazorCommon_MatchingClassWithNoInterfaces_RegistersNothing()
        {
            // Arrange
            var services = new ServiceCollection();

            // Act
            services.AddCloudstrapBlazorCommon<ISampleViewModel>();

            // Assert — interfaces-only registration: a class with no interfaces contributes nothing
            Assert.That(
                services.Where(descriptor =>
                    descriptor.ServiceType == typeof(OrphanViewModel)
                    || descriptor.ImplementationType == typeof(OrphanViewModel)),
                Is.Empty);
        }

        [Test]
        public void AddCloudstrapBlazorCommon_OnHostBuilderServices_ResolvesIdentically()
        {
            // Arrange — the server-style half of AC-BC2's parity
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();

            // Act
            builder.Services.AddCloudstrapBlazorCommon<ISampleViewModel>();

            // Assert
            using IHost host = builder.Build();
            Assert.Multiple(() =>
            {
                Assert.That(host.Services.GetRequiredService<ISampleViewModel>(), Is.InstanceOf<SampleViewModel>());
                Assert.That(host.Services.GetRequiredService<ISampleService>(), Is.InstanceOf<SampleService>());
                Assert.That(host.Services.GetRequiredService<IViewModel>(), Is.InstanceOf<SampleViewModel>());
            });
        }

        [Test]
        public void AddCloudstrapBlazorCommon_AddsNoErrorHandlerRegistration()
        {
            // Arrange
            var services = new ServiceCollection();

            // Act
            services.AddCloudstrapBlazorCommon<ISampleViewModel>();

            // Assert — AC-BC5: the consumer owns the IErrorHandler registration, the package adds none
            Assert.That(
                services.Where(descriptor => descriptor.ServiceType == typeof(IErrorHandler)),
                Is.Empty);
        }
    }
}
