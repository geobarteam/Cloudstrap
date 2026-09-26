namespace Cloudstrap.Hangfire.Tests
{
    using Cloudstrap.Hangfire.Tests.Fixtures.Tasks;
    using Cloudstrap.Hangfire.Tests.Infrastructure;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using NUnit.Framework;

    /// <summary>
    /// Task discovery (AC-HF2): concrete implementations — public and internal — from the entry assembly (this
    /// test executable under Microsoft.Testing.Platform) are registered transient, as the interface only;
    /// <c>TaskAssemblies</c> replaces the default; manual registrations are honored.
    /// </summary>
    [TestFixture]
    public sealed class DiscoveryTests
    {
        [Test]
        public void Scan_DefaultEntryAssembly_RegistersPublicAndInternalConcreteTasks_AsTheInterfaceOnly()
        {
            // Arrange
            HostApplicationBuilder builder = HangfireTestHost.CreateBuilder();

            // Act
            builder.AddCloudstrapHangfire();
            using ServiceProvider provider = builder.Services.BuildServiceProvider();
            Type[] tasks = [.. provider.GetServices<IBackgroundRecurringTask>().Select(task => task.GetType())];

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(tasks, Does.Contain(typeof(NightlyCleanupTask)));
                Assert.That(tasks, Does.Contain(typeof(InternalReportTask)));
                Assert.That(tasks, Has.None.Matches<Type>(type => type.IsAbstract));
                Assert.That(tasks, Has.None.Matches<Type>(type => type.IsGenericType));
                Assert.That(provider.GetServices<IDisposable>(), Has.None.InstanceOf<NightlyCleanupTask>());
                Assert.That(provider.GetService<NightlyCleanupTask>(), Is.Null);
            });
        }

        [Test]
        public void Scan_TasksAreTransient()
        {
            // Arrange
            HostApplicationBuilder builder = HangfireTestHost.CreateBuilder();

            // Act
            builder.AddCloudstrapHangfire();

            // Assert
            ServiceDescriptor[] registrations = [.. builder.Services.Where(d => d.ServiceType == typeof(IBackgroundRecurringTask))];
            Assert.Multiple(() =>
            {
                Assert.That(registrations, Is.Not.Empty);
                Assert.That(registrations.Select(d => d.Lifetime), Is.All.EqualTo(ServiceLifetime.Transient));
            });
        }

        [Test]
        public void Scan_TaskAssembliesReplaceTheDefault()
        {
            // Arrange — the package assembly declares no task implementations.
            HostApplicationBuilder builder = HangfireTestHost.CreateBuilder();

            // Act
            builder.AddCloudstrapHangfire(hangfire => hangfire.TaskAssemblies.Add(typeof(IBackgroundRecurringTask).Assembly));

            // Assert
            Assert.That(builder.Services.Where(d => d.ServiceType == typeof(IBackgroundRecurringTask)), Is.Empty);
        }

        [Test]
        public void ManualRegistration_IsHonoredAlongsideTheScan()
        {
            // Arrange
            HostApplicationBuilder builder = HangfireTestHost.CreateBuilder();
            builder.Services.AddTransient<IBackgroundRecurringTask, SameIdTask<First>>();

            // Act
            builder.AddCloudstrapHangfire();
            using ServiceProvider provider = builder.Services.BuildServiceProvider();
            Type[] tasks = [.. provider.GetServices<IBackgroundRecurringTask>().Select(task => task.GetType())];

            // Assert
            Assert.That(tasks, Does.Contain(typeof(SameIdTask<First>)).And.Contain(typeof(NightlyCleanupTask)));
        }
    }
}
