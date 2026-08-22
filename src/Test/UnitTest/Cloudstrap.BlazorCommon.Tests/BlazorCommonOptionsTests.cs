namespace Cloudstrap.BlazorCommon.Tests
{
    using System.Reflection;
    using Cloudstrap.BlazorCommon.Tests.Fixtures;
    using Microsoft.Extensions.DependencyInjection;
    using NUnit.Framework;

    /// <summary>
    /// Behavioral tests for the <see cref="BlazorCommonOptions"/> knobs: AC-BC3 (custom suffixes
    /// and lifetime fully replace the defaults), AC-BC4's assembly boundary both ways, and the
    /// spec's edge-case table (empty no-op, loud whitespace failure, append-on-repeat,
    /// once-per-suffix matching).
    /// </summary>
    [TestFixture]
    public sealed class BlazorCommonOptionsTests
    {
        [Test]
        public void AddCloudstrapBlazorCommon_CustomSuffix_ReplacesTheDefaultsEntirely()
        {
            // Arrange
            var services = new ServiceCollection();

            // Act — the defaults are fully replaceable, not merely extendable
            services.AddCloudstrapBlazorCommon<ISampleViewModel>(options =>
            {
                options.ConventionSuffixes.Clear();
                options.ConventionSuffixes.Add("Presenter");
            });

            // Assert
            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.Multiple(() =>
            {
                Assert.That(provider.GetRequiredService<ICustomSuffixPresenter>(), Is.InstanceOf<CustomSuffixPresenter>());
                Assert.That(provider.GetService<ISampleViewModel>(), Is.Null);
                Assert.That(provider.GetService<ISampleService>(), Is.Null);
            });
        }

        [Test]
        public void AddCloudstrapBlazorCommon_LifetimeOverride_IsAppliedToEveryConventionRegistration()
        {
            // Arrange
            var services = new ServiceCollection();

            // Act
            services.AddCloudstrapBlazorCommon<ISampleViewModel>(options =>
                options.Lifetime = ServiceLifetime.Scoped);

            // Assert — every descriptor scoped; same instance within a scope, distinct across scopes
            using ServiceProvider provider = services.BuildServiceProvider();
            using IServiceScope firstScope = provider.CreateScope();
            using IServiceScope secondScope = provider.CreateScope();
            Assert.Multiple(() =>
            {
                Assert.That(services, Is.Not.Empty);
                Assert.That(
                    services.Select(descriptor => descriptor.Lifetime),
                    Is.All.EqualTo(ServiceLifetime.Scoped));
                Assert.That(
                    firstScope.ServiceProvider.GetRequiredService<ISampleViewModel>(),
                    Is.SameAs(firstScope.ServiceProvider.GetRequiredService<ISampleViewModel>()));
                Assert.That(
                    firstScope.ServiceProvider.GetRequiredService<ISampleViewModel>(),
                    Is.Not.SameAs(secondScope.ServiceProvider.GetRequiredService<ISampleViewModel>()));
            });
        }

        [Test]
        public void AddCloudstrapBlazorCommon_AdditionalAssemblies_AreScannedAndTheMarkerBoundaryHolds()
        {
            // Arrange — inverted roles: marker = the package assembly (which has no matching classes)
            var withAdditions = new ServiceCollection();
            var markerOnly = new ServiceCollection();
            Assembly testAssembly = typeof(ISampleViewModel).Assembly;

            // Act
            withAdditions.AddCloudstrapBlazorCommon<BlazorCommonOptions>(options =>
                options.AdditionalAssemblies.Add(testAssembly));
            markerOnly.AddCloudstrapBlazorCommon<BlazorCommonOptions>();

            // Assert — extra assemblies are scanned; the marker assembly alone yields nothing
            using ServiceProvider provider = withAdditions.BuildServiceProvider();
            Assert.Multiple(() =>
            {
                Assert.That(provider.GetRequiredService<ISampleViewModel>(), Is.InstanceOf<SampleViewModel>());
                Assert.That(provider.GetRequiredService<ISampleService>(), Is.InstanceOf<SampleService>());
                Assert.That(markerOnly, Is.Empty);
            });
        }

        [Test]
        public void AddCloudstrapBlazorCommon_EmptySuffixList_ScansNothing()
        {
            // Arrange
            var services = new ServiceCollection();

            // Act — the legal no-op profile
            services.AddCloudstrapBlazorCommon<ISampleViewModel>(options =>
                options.ConventionSuffixes.Clear());

            // Assert
            Assert.That(services, Is.Empty);
        }

        [Test]
        public void AddCloudstrapBlazorCommon_WhitespaceSuffix_ThrowsArgumentException()
        {
            // Arrange
            var services = new ServiceCollection();

            // Act & Assert — fail loud: a silent EndsWith("") would match every type
            Assert.That(
                () => services.AddCloudstrapBlazorCommon<ISampleViewModel>(options =>
                    options.ConventionSuffixes.Add("   ")),
                Throws.ArgumentException.With.Message.Contains("whitespace"));
        }

        [Test]
        public void AddCloudstrapBlazorCommon_CalledTwiceWithTheSameMarker_AppendsDuplicateRegistrations()
        {
            // Arrange
            var services = new ServiceCollection();

            // Act — standard IServiceCollection semantics, documented not "fixed"
            services.AddCloudstrapBlazorCommon<ISampleViewModel>();
            services.AddCloudstrapBlazorCommon<ISampleViewModel>();

            // Assert
            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.Multiple(() =>
            {
                Assert.That(
                    services.Count(descriptor => descriptor.ServiceType == typeof(ISampleViewModel)),
                    Is.EqualTo(2));
                Assert.That(provider.GetServices<ISampleViewModel>().Count(), Is.EqualTo(2));
                Assert.That(provider.GetRequiredService<ISampleViewModel>(), Is.InstanceOf<SampleViewModel>());
            });
        }

        [Test]
        public void AddCloudstrapBlazorCommon_ClassMatchingTwoSuffixes_IsRegisteredPerMatchingPass()
        {
            // Arrange
            var services = new ServiceCollection();

            // Act
            services.AddCloudstrapBlazorCommon<ISampleViewModel>();

            // Assert — ordinal EndsWith: "SampleServiceViewModel" matches the ViewModel pass only
            Assert.That(
                services.Count(descriptor => descriptor.ServiceType == typeof(ISampleServiceViewModel)),
                Is.EqualTo(1));
        }
    }
}
